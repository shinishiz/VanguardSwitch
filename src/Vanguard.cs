using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;

namespace VanguardSwitch
{
    class RiotProc
    {
        public int Id;
        public string Name;
    }

    class RestoreResult
    {
        public bool ConfigOk;     // vgk and vgc start types are back
        public bool TrayFound;    // Vanguard's tray startup entry exists
        public bool TrayOk;       // ...and it was set to open with Windows
        public bool VgcRunning;   // only informative: the PC restarts right after
        public bool Ok => ConfigOk && TrayFound && TrayOk;
    }

    class TrayChange
    {
        public int Found, Failed;
        public bool Ok => Found > 0 && Failed == 0;
    }

    class Report
    {
        public int? VgkStart, VgcStart;
        public string VgkSvc, VgcSvc;
        public bool VgkLoaded, Tray;
        public bool? VgkFromBoot;   // true = loaded with Windows, false = loaded later (VAN 57), null = unknown
        public bool? TrayBoot;
        public List<RiotProc> Procs = new List<RiotProc>();
        public bool Installed => VgkStart != null || VgkSvc != null;
    }

    class ScResult
    {
        public int Code;
        public string Output;
    }

    class CloseResult
    {
        public List<string> Fails = new List<string>();
        public bool VgkStuck;
    }

    class StartResult
    {
        public bool VgkOk;
        public bool LateStart;   // the driver was started now, not at boot: Riot answers with VAN 57
    }

    /// <summary>
    /// Everything that touches Windows: services vgk (kernel driver) and vgc (user service),
    /// the vgtray startup entry and Riot processes. Runs on a background thread.
    /// </summary>
    static class Vg
    {
        public static Action<string> Log = _ => { };

        public static readonly string[] RiotNames =
        {
            "RiotClientServices", "RiotClientUx", "RiotClientUxRender", "RiotClientCrashHandler", "Riot Client",
            "VALORANT", "VALORANT-Win64-Shipping",
            "LeagueClient", "LeagueClientUx", "LeagueClientUxRender", "League of Legends", "LeagueCrashHandler", "LeagueCrashHandler64",
            "LoR", "2XKO", "vgtray", "vgc"
        };
        public static readonly string[] GameNames = { "VALORANT-Win64-Shipping", "League of Legends", "LoR", "2XKO" };
        public static readonly string[] ClientNames = { "RiotClientServices", "Riot Client", "RiotClientUx" };
        static readonly string[] PathHints = { @"\Riot Games\", @"\Riot Vanguard\" };

        public static readonly string StateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VanguardSwitch");
        static string StateFile => Path.Combine(StateDir, "state.ini");
        static string SettingsFile => Path.Combine(StateDir, "settings.ini");

        static readonly RegistryKey HKLM = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        static readonly RegistryKey HKCU = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

        // "Run" keys where vgtray (the tray icon) lives, and the StartupApproved key Task Manager uses
        static readonly (RegistryKey Root, string Run, string Approved)[] RunMap =
        {
            (HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            (HKLM, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"),
            (HKCU, @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
        };

        static readonly string[] StartWords = { "boot", "system", "auto", "demand", "disabled" };

        public static string StartText(int? t)
        {
            switch (t)
            {
                case 0:
                case 1: return L.T("no boot", "at boot");
                case 2: return L.T("automático", "automatic");
                case 3: return "manual";
                case 4: return L.T("bloqueado", "blocked");
                default: return "?";
            }
        }

        public static string StatusText(string s)
        {
            switch (s)
            {
                case "Running": return L.T("rodando", "running");
                case "Stopped": return L.T("parado", "stopped");
                case "StartPending": return L.T("iniciando", "starting");
                case "StopPending": return L.T("parando", "stopping");
                case "Paused": return L.T("pausado", "paused");
                default: return "?";
            }
        }

        // ---------- reading state ----------

        public static int? StartType(string svc)
        {
            using (var k = HKLM.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svc}"))
                return k?.GetValue("Start") is int i ? i : (int?)null;
        }

        public static string Status(string svc)
        {
            try
            {
                using (var sc = new ServiceController(svc)) return sc.Status.ToString();
            }
            catch
            {
                // fallback: sc.exe (RUNNING/STOPPED are not translated)
                var m = Regex.Match(Sc("query", svc).Output, @"\d\s+(RUNNING|STOPPED|START_PENDING|STOP_PENDING|PAUSED)");
                if (!m.Success) return null;
                switch (m.Groups[1].Value)
                {
                    case "RUNNING": return "Running";
                    case "STOPPED": return "Stopped";
                    case "START_PENDING": return "StartPending";
                    case "STOP_PENDING": return "StopPending";
                    default: return "Paused";
                }
            }
        }

        /// <summary>vgk is in memory: the service says so, or vgk.sys is in the loaded-driver list.</summary>
        public static bool VgkLoaded()
        {
            var s = Status("vgk");
            if (s == "Running" || s == "StopPending" || s == "StartPending") return true;
            try
            {
                return Native.LoadedDrivers().Any(d => d.Equals("vgk.sys", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        public static DateTime BootTime => DateTime.Now - TimeSpan.FromMilliseconds(Native.GetTickCount64());

        static string BootFile => Path.Combine(StateDir, "boot.ini");

        /// <summary>Notes that this app stopped or started vgk during the current Windows session.</summary>
        static void MarkVgkTouched()
        {
            try { WriteIni(BootFile, new Dictionary<string, string> { ["touched"] = BootTime.ToString("o", CultureInfo.InvariantCulture) }); }
            catch { }
        }

        static bool VgkTouchedThisBoot()
        {
            try
            {
                return ReadIni(BootFile).TryGetValue("touched", out var v)
                    && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                    && Math.Abs((t - BootTime).TotalMinutes) < 2;
            }
            catch { return false; }
        }

        /// <summary>
        /// Was vgk loaded together with Windows? Vanguard writes Logs\vgk_yyyy-MM-dd_HH-mm-ss.log every time
        /// the driver starts, so the newest log is compared with the boot time. If this app stopped or
        /// started the driver since boot, the answer is no. Read-only.
        /// </summary>
        public static bool? VgkFromBoot()
        {
            try
            {
                var folder = VanguardFolder();
                var dir = folder == null ? null : Path.Combine(folder, "Logs");
                if (dir == null || !Directory.Exists(dir)) return null;

                if (VgkTouchedThisBoot()) return false;
                var boot = BootTime;
                DateTime? newest = null;
                foreach (var f in Directory.GetFiles(dir, "vgk_*.log"))
                {
                    var m = Regex.Match(Path.GetFileName(f), @"^vgk_(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})");
                    if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                        continue;
                    if (newest == null || t > newest) newest = t;
                }
                if (newest == null || newest < boot.AddMinutes(-2)) return null;   // no log from this session
                return newest <= boot.AddMinutes(3);
            }
            catch { return null; }
        }

        static readonly Dictionary<string, bool> signedCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True if the file has a valid, trusted Authenticode signature (WinVerifyTrust) and the signer's
        /// name is Riot Games. The name is only read after Windows has accepted the signature.
        /// </summary>
        static bool SignedByRiot(string path)
        {
            // keyed by path + last write, so a file Riot updates (or one that was busy) is checked again
            string key;
            try { key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks; }
            catch { return false; }
            lock (signedCache)
                if (signedCache.TryGetValue(key, out bool cached)) return cached;
            bool ok = false;
            try
            {
                if (Native.AuthenticodeValid(path))
                {
                    var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                        System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path));
                    string cn = cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false) ?? "";
                    ok = cn.StartsWith("Riot Games", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { ok = false; }
            lock (signedCache) signedCache[key] = ok;
            return ok;
        }

        /// <summary>
        /// A process counts as Riot's if it has a Riot process name or runs from a Riot folder, AND its exe
        /// has a valid signature from Riot Games. When the path can't be read (protected process), the
        /// name alone is used.
        /// </summary>
        static bool IsRiot(int pid, string name)
        {
            var path = Native.ProcessPath(pid);
            bool knownName = RiotNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (path == null) return knownName;
            bool riotFolder = PathHints.Any(h => path.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);
            return (knownName || riotFolder) && SignedByRiot(path);
        }

        public static List<RiotProc> RiotProcesses(bool includeVgc)
        {
            var list = new List<RiotProc>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        string name = p.ProcessName;   // throws if the process already exited
                        if (!includeVgc && name.Equals("vgc", StringComparison.OrdinalIgnoreCase)) continue;
                        if (IsRiot(p.Id, name)) list.Add(new RiotProc { Id = p.Id, Name = name });
                    }
                    catch { }
                }
            }
            return list;
        }

        static IEnumerable<(RegistryKey Root, string Approved, string Name)> TrayRunEntries()
        {
            foreach (var m in RunMap)
            {
                using (var k = m.Root.OpenSubKey(m.Run))
                {
                    if (k == null) continue;
                    foreach (var v in k.GetValueNames())
                    {
                        var data = k.GetValue(v, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        if (data != null && data.IndexOf("vgtray", StringComparison.OrdinalIgnoreCase) >= 0)
                            yield return (m.Root, m.Approved, v);
                    }
                }
            }
        }

        /// <summary>true = tray opens with Windows, false = disabled, null = no entry found.</summary>
        public static bool? TrayStartupEnabled()
        {
            var entries = TrayRunEntries().ToList();
            if (entries.Count == 0) return null;
            foreach (var e in entries)
            {
                using (var k = e.Root.OpenSubKey(e.Approved))
                {
                    var bytes = k?.GetValue(e.Name) as byte[];
                    if (bytes == null || bytes.Length == 0 || (bytes[0] & 1) == 0) return true;
                }
            }
            return false;
        }

        public static Report GetReport()
        {
            var procs = RiotProcesses(true);
            bool loaded = VgkLoaded();
            return new Report
            {
                VgkStart = StartType("vgk"),
                VgcStart = StartType("vgc"),
                VgkSvc = Status("vgk"),
                VgcSvc = Status("vgc"),
                VgkLoaded = loaded,
                VgkFromBoot = loaded ? VgkFromBoot() : null,
                Procs = procs,
                Tray = procs.Any(p => p.Name.Equals("vgtray", StringComparison.OrdinalIgnoreCase)),
                TrayBoot = TrayStartupEnabled(),
            };
        }

        public static string VanguardFolder()
        {
            try
            {
                using (var k = HKLM.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\vgc"))
                {
                    var img = k?.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (img != null)
                    {
                        img = Environment.ExpandEnvironmentVariables(Regex.Replace(img, "^\"([^\"]+)\".*$", "$1"));
                        if (File.Exists(img)) return Path.GetDirectoryName(img);
                    }
                }
            }
            catch { }
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Riot Vanguard");
            return Directory.Exists(p) ? p : null;
        }

        public static string FindRiotClient()
        {
            var candidates = new List<string>
            {
                Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), @"Riot Games\Riot Client\RiotClientServices.exe"),
            };
            try
            {
                var json = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Riot Games\RiotClientInstalls.json");
                if (File.Exists(json))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(json), "\"rc_(?:default|live|beta)\"\\s*:\\s*\"([^\"]+)\""))
                        candidates.Add(m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\/", "/"));
                }
            }
            catch { }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Riot Games\Riot Client\RiotClientServices.exe"));

            foreach (var c in candidates)
            {
                try
                {
                    // Riot's json uses "/", which explorer.exe does not understand
                    var full = Path.GetFullPath(c.Replace('/', '\\'));
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return null;
        }

        // ---------- changing state ----------

        public static ScResult Sc(params string[] args)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), string.Join(" ", args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                // read both streams in the background so a hung sc.exe can't block us
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(30000))
                {
                    try { p.Kill(); } catch { }
                    Log("sc " + string.Join(" ", args) + ": " + L.T("não respondeu em 30 s, encerrado.", "didn't respond in 30 s, killed."));
                    return new ScResult { Code = -1, Output = "" };
                }
                p.WaitForExit();   // lets the async reads finish
                return new ScResult { Code = p.ExitCode, Output = stdout.Result + stderr.Result };
            }
        }

        public static bool SetStartType(string svc, int type)
        {
            if (StartType(svc) == null) { Log($"{svc}: " + L.T("não instalado.", "not installed.")); return false; }
            var r = Sc("config", svc, "start=", StartWords[type]);
            bool ok = r.Code == 0 && StartType(svc) == type;
            Log(ok ? $"{svc}: " + L.T("inicialização = ", "startup = ") + StartText(type) + "."
                   : $"{svc}: " + L.T("falha ao mudar a inicialização", "could not change startup") + $" ({r.Code}).");
            return ok;
        }

        public static bool WaitStatus(string svc, string want, int seconds)
        {
            var limit = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < limit)
            {
                if (Status(svc) == want) return true;
                Thread.Sleep(250);
            }
            return Status(svc) == want;
        }

        public static List<string> KillRiot(bool includeVgc)
        {
            var killed = new List<string>();
            for (int round = 0; round < 4; round++)
            {
                var procs = RiotProcesses(includeVgc);
                if (procs.Count == 0) break;
                foreach (var rp in procs)
                {
                    try
                    {
                        using (var p = Process.GetProcessById(rp.Id))
                        {
                            // the pid may have been reused since the list was made
                            if (!p.ProcessName.Equals(rp.Name, StringComparison.OrdinalIgnoreCase) || !IsRiot(p.Id, p.ProcessName)) continue;
                            p.Kill();
                        }
                        killed.Add(rp.Name);
                    }
                    catch { }
                }
                Thread.Sleep(800);
            }
            return killed.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>How many tray startup entries were found and how many couldn't be changed.</summary>
        public static TrayChange SetTrayStartup(bool enabled)
        {
            // first byte: 2 = enabled, 3 = disabled (same format as Task Manager)
            var bytes = new byte[12];
            bytes[0] = (byte)(enabled ? 2 : 3);
            var res = new TrayChange();
            var entries = TrayRunEntries().ToList();
            res.Found = entries.Count;
            if (entries.Count == 0) Log(L.T("Bandeja: nenhuma entrada de inicialização do Vanguard encontrada.", "Tray: no Vanguard startup entry found."));
            foreach (var e in entries)
            {
                try
                {
                    using (var k = e.Root.CreateSubKey(e.Approved))
                        k.SetValue(e.Name, bytes, RegistryValueKind.Binary);
                    Log(enabled ? L.T("Bandeja do Vanguard volta a abrir com o Windows.", "Vanguard tray opens with Windows again.")
                                : L.T("Bandeja do Vanguard não abre mais com o Windows.", "Vanguard tray no longer opens with Windows."));
                }
                catch (Exception ex) { res.Failed++; Log(L.T("Não deu para mudar a bandeja: ", "Could not change the tray: ") + ex.Message); }
            }
            return res;
        }

        /// <summary>Starts a program as the normal user (not admin) by handing it to explorer.exe.</summary>
        public static void StartAsUser(string exe)
        {
            Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + exe + "\"");
        }

        public static void StartTray()
        {
            var folder = VanguardFolder();
            if (folder == null) return;
            var exe = Path.Combine(folder, "vgtray.exe");
            if (File.Exists(exe) && Process.GetProcessesByName("vgtray").Length == 0) StartAsUser(exe);
        }

        public static void Reboot(string reason)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), $"/r /t 3 /c \"{reason}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        // ---------- saved state ----------

        static Dictionary<string, string> ReadIni(string file)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(file))
                    foreach (var line in File.ReadAllLines(file))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
            }
            catch { }
            return d;
        }

        static void WriteIni(string file, Dictionary<string, string> d)
        {
            Directory.CreateDirectory(StateDir);
            File.WriteAllLines(file, d.Select(kv => kv.Key + "=" + kv.Value));
        }

        /// <summary>Remembers how vgk was configured before we touched it, for "Riot default".</summary>
        static void RememberOriginal()
        {
            if (File.Exists(StateFile)) return;
            var vgk = StartType("vgk");
            if (vgk == null) return;
            WriteIni(StateFile, new Dictionary<string, string> { ["vgk"] = vgk.ToString() });
        }

        static int OriginalVgkStart()
        {
            var d = ReadIni(StateFile);
            return d.TryGetValue("vgk", out var s) && int.TryParse(s, out int v) && v <= 2 ? v : 1;
        }

        public static bool LightMode
        {
            get { return !ReadIni(SettingsFile).TryGetValue("light", out var v) || v != "0"; }
            set
            {
                try
                {
                    var d = ReadIni(SettingsFile);
                    d["light"] = value ? "1" : "0";
                    WriteIni(SettingsFile, d);
                }
                catch { }
            }
        }

        static bool Check(bool ok, string text)
        {
            Log((ok ? "   [OK] " : "   [!!] ") + text);
            return ok;
        }

        // ---------- actions ----------

        public static CloseResult Close(bool light)
        {
            var res = new CloseResult();
            Log("== " + L.T("Fechando Riot e desligando o Vanguard", "Closing Riot and turning Vanguard off") + " ==");
            RememberOriginal();
            if (light) Log(L.T("Modo leve: o driver vgk continua na memória.", "Light mode: the vgk driver stays in memory."));

            // 1) block first, so nothing turns it back on while we close
            SetStartType("vgc", 4);
            if (light)
            {
                // the driver must keep loading at boot
                var k = StartType("vgk");
                if (k != null && k > 1) SetStartType("vgk", OriginalVgkStart());
            }
            else SetStartType("vgk", 4);
            SetTrayStartup(false);

            // 2) games, Riot Client and tray
            var killed = KillRiot(false);
            Log(killed.Count > 0 ? L.T("Processos fechados: ", "Closed: ") + string.Join(", ", killed)
                                 : L.T("Nenhum processo da Riot aberto.", "No Riot process was running."));

            // 3) vgc service
            var st = Status("vgc");
            if (st != null && st != "Stopped")
            {
                Sc("stop", "vgc");
                Log(WaitStatus("vgc", "Stopped", 10) ? "vgc: " + L.T("parado.", "stopped.")
                                                     : "vgc: " + L.T("não parou pelo serviço, fechando o processo.", "did not stop as a service, killing the process."));
            }

            // 4) vgk driver (full mode only)
            if (!light && VgkLoaded())
            {
                var r = Sc("stop", "vgk");
                MarkVgkTouched();
                Log(WaitStatus("vgk", "Stopped", 10) ? "vgk: " + L.T("driver parado.", "driver stopped.")
                                                     : "vgk: " + L.T("o driver recusou parar", "the driver refused to stop") + $" ({r.Code}).");
            }

            // 5) leftovers (vgc.exe if the service hung)
            var left = KillRiot(true);
            if (left.Count > 0) Log(L.T("Sobras fechadas: ", "Leftovers closed: ") + string.Join(", ", left));

            Thread.Sleep(1000);
            var rep = GetReport();
            Log(L.T("Conferência:", "Check:"));
            if (!Check(rep.Procs.Count == 0, L.T("Nenhum processo da Riot/Vanguard aberto", "No Riot/Vanguard process running")))
                res.Fails.Add(L.T("processos: ", "processes: ") + string.Join(", ", rep.Procs.Select(p => p.Name).Distinct()));
            if (!Check(rep.VgcSvc != "Running", L.T("Serviço vgc parado", "vgc service stopped")))
                res.Fails.Add(L.T("vgc ainda rodando", "vgc still running"));

            if (light)
            {
                if (rep.VgkLoaded) Check(true, L.T("Driver vgk mantido na memória (Ligar funciona sem reiniciar)", "vgk driver kept in memory (Turn on works without rebooting)"));
                else Log("   [--] " + L.T("Driver vgk fora da memória: ele volta no próximo boot.", "vgk driver not in memory: it comes back on the next boot."));
                if (!Check(rep.VgcStart == 4 && rep.VgkStart <= 2, L.T("vgc bloqueado; vgk segue ligando no boot", "vgc blocked; vgk still loads at boot")))
                    res.Fails.Add(L.T("bloqueio não aplicado", "block not applied"));
            }
            else
            {
                res.VgkStuck = !Check(!rep.VgkLoaded, L.T("Driver vgk fora da memória", "vgk driver out of memory"));
                if (!Check(rep.VgkStart == 4 && (rep.VgcStart == null || rep.VgcStart == 4),
                           L.T("vgk e vgc bloqueados (não ligam no boot nem pelo Riot Client)", "vgk and vgc blocked (they won't start at boot or from Riot Client)")))
                    res.Fails.Add(L.T("bloqueio não aplicado", "block not applied"));
            }
            Check(rep.TrayBoot != true, L.T("Bandeja fora da inicialização do Windows", "Tray removed from Windows startup"));

            if (res.Fails.Count > 0) Log(L.T("Algumas coisas não fecharam. Veja os [!!] acima.", "Some things did not close. See [!!] above."));
            else if (!res.VgkStuck) Log(L.T("Pronto. Continua desligado depois de reiniciar, até você clicar em Ligar.",
                                            "Done. It stays off after rebooting until you click Turn on."));
            return res;
        }

        public static StartResult Start()
        {
            var res = new StartResult();
            Log("== " + L.T("Iniciando o Vanguard", "Starting Vanguard") + " ==");
            RememberOriginal();

            // back to automatic: vgk at boot, vgc automatic, tray with Windows
            SetStartType("vgk", OriginalVgkStart());
            SetStartType("vgc", 2);
            SetTrayStartup(true);

            if (!VgkLoaded())
            {
                res.LateStart = true;
                var r = Sc("start", "vgk");
                MarkVgkTouched();
                Log(WaitStatus("vgk", "Running", 15) ? "vgk: " + L.T("driver ligado agora (não veio do boot).", "driver started now (not from boot).")
                                                     : "vgk: " + L.T("não ligou", "did not start") + $" ({r.Code}).");
            }
            else if (VgkFromBoot() == false)
            {
                res.LateStart = true;
                Log("vgk: " + L.T("já estava na memória, mas foi ligado depois do boot.", "already in memory, but it was started after boot."));
            }
            else Log("vgk: " + L.T("já estava na memória desde o boot.", "already in memory since boot."));

            if (VgkLoaded() && Status("vgc") != "Running")
            {
                var r = Sc("start", "vgc");
                Log(WaitStatus("vgc", "Running", 15) ? "vgc: " + L.T("rodando.", "running.")
                                                     : "vgc: " + L.T("não ficou rodando", "did not keep running") + $" ({r.Code}).");
            }

            StartTray();
            Thread.Sleep(1500);

            var rep = GetReport();
            Log(L.T("Conferência:", "Check:"));
            res.VgkOk = Check(rep.VgkLoaded, L.T("Driver vgk carregado", "vgk driver loaded"));
            Check(rep.VgkStart <= 2 && rep.VgcStart == 2, L.T("vgk no boot e vgc automático", "vgk at boot and vgc automatic"));
            if (rep.VgcSvc == "Running") Check(true, L.T("Serviço vgc rodando", "vgc service running"));
            else Log("   [--] " + L.T("Serviço vgc parado: a Riot liga ele quando o jogo abre.", "vgc service stopped: Riot starts it when the game opens."));
            if (rep.Tray) Check(true, L.T("Bandeja do Vanguard aberta", "Vanguard tray open"));
            else Log("   [--] " + L.T("Bandeja do Vanguard não abriu (não impede de jogar).", "Vanguard tray did not open (does not block playing)."));
            if (res.LateStart && res.VgkOk)
                Log("   [!!] " + L.T("O driver não veio do boot: no modo tradicional, isso pode dar VAN 57 até reiniciar.", "The driver did not load at boot: in the traditional mode this can cause VAN 57 until you reboot."));
            return res;
        }

        /// <summary>Riot default without Pre-Check: vgk at boot, vgc automatic. Returns false if it could not apply.</summary>
        public static RestoreResult RestoreDefault()
        {
            var res = new RestoreResult();
            Log("== " + L.T("Voltando ao padrão da Riot", "Back to Riot default") + " ==");
            int vgk = OriginalVgkStart();
            SetStartType("vgk", vgk);
            SetStartType("vgc", 2);
            var tray = SetTrayStartup(true);
            res.TrayFound = tray.Found > 0;
            res.TrayOk = tray.Ok;

            Log(L.T("Conferência:", "Check:"));
            res.ConfigOk = Check(StartType("vgk") == vgk && StartType("vgc") == 2,
                                 L.T("vgk e vgc voltaram a ligar com o Windows", "vgk and vgc start with Windows again"));
            if (res.TrayFound) Check(res.TrayOk, L.T("Bandeja volta a abrir com o Windows", "Tray opens with Windows again"));
            else Check(false, L.T("Entrada da bandeja do Vanguard não encontrada no Registro", "Vanguard tray entry not found in the registry"));

            if (res.ConfigOk && Status("vgc") != "Running" && VgkLoaded())
            {
                Sc("start", "vgc");
                WaitStatus("vgc", "Running", 15);
            }
            res.VgcRunning = Status("vgc") == "Running";
            if (res.VgcRunning) Check(true, L.T("Serviço vgc rodando", "vgc service running"));
            else Log("   [--] " + L.T("Serviço vgc parado agora; ele liga no próximo boot.", "vgc service stopped for now; it starts on the next boot."));

            // keep the saved original until the essential steps are confirmed
            if (res.Ok) { try { File.Delete(StateFile); } catch { } }
            return res;
        }
    }
}
