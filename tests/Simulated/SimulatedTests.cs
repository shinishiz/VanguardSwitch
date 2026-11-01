using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace VanguardSwitch
{
    /// <summary>
    /// Simulated tests. Nothing here touches the real Vanguard: the updater gets local file:// URLs and the
    /// window gets made-up reports. Usage: Simulated.exe [folder for screenshots]
    /// </summary>
    static class SimulatedTests
    {
        static int failed;

        static void Check(bool ok, string name, string detail = "")
        {
            if (!ok) failed++;
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " -- " + detail : ""));
        }

        [STAThread]
        static int Main(string[] args)
        {
            string shots = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "vs-simulated");
            Directory.CreateDirectory(shots);
            UpdaterTests(shots);
            UiTests(shots);
            RestartTextTests(shots);
            Console.WriteLine(failed == 0 ? "ALL PASSED" : failed + " FAILED");
            return failed;
        }

        // ---------- Updater.Install with bad downloads ----------

        static void UpdaterTests(string dir)
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string fresh = exe + ".new";
            byte[] before = File.ReadAllBytes(exe);

            string payload = Path.Combine(dir, "payload.exe");
            File.WriteAllBytes(payload, new byte[] { 1, 2, 3, 4 });   // not the real exe, its hash won't match
            string badFormat = Path.Combine(dir, "bad-format.sha256");
            File.WriteAllText(badFormat, "not-a-hash  VanguardSwitch.exe\r\n");
            string wrongHash = Path.Combine(dir, "wrong.sha256");
            File.WriteAllText(wrongHash, new string('a', 64) + "  VanguardSwitch.exe\r\n");

            Run("U1 release without .sha256 is refused",
                new Updater.Release { Version = new Version(9, 9, 9), ExeUrl = new Uri(payload).AbsoluteUri, HashUrl = null }, ".sha256");
            Run("U2 invalid .sha256 content is refused",
                new Updater.Release { Version = new Version(9, 9, 9), ExeUrl = new Uri(payload).AbsoluteUri, HashUrl = new Uri(badFormat).AbsoluteUri }, "SHA-256", "sha256");
            Run("U3 checksum mismatch is refused",
                new Updater.Release { Version = new Version(9, 9, 9), ExeUrl = new Uri(payload).AbsoluteUri, HashUrl = new Uri(wrongHash).AbsoluteUri }, "SHA-256");
            Run("U4 missing download is refused",
                new Updater.Release { Version = new Version(9, 9, 9), ExeUrl = new Uri(Path.Combine(dir, "does-not-exist.exe")).AbsoluteUri, HashUrl = new Uri(wrongHash).AbsoluteUri });

            void Run(string name, Updater.Release rel, params string[] expectInMessage)
            {
                string msg = null;
                try { Updater.Install(rel); }
                catch (Exception ex) { msg = ex.Message; }
                bool refused = msg != null;
                bool msgOk = expectInMessage.Length == 0 || (msg != null && expectInMessage.Any(m => msg.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0));
                bool exeIntact = File.ReadAllBytes(exe).SequenceEqual(before);
                bool noLeftover = !File.Exists(fresh) && !File.Exists(exe + ".old");
                Check(refused && msgOk && exeIntact && noLeftover, name,
                      $"exception=\"{msg}\" exeIntact={exeIntact} noLeftover={noLeftover}");
            }
        }

        // ---------- MainForm.Apply with made-up reports ----------

        static void UiTests(string dir)
        {
            Application.EnableVisualStyles();
            var apply = typeof(MainForm).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic);
            var statusField = typeof(MainForm).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic);
            var partsField = typeof(MainForm).GetField("parts", BindingFlags.Instance | BindingFlags.NonPublic);

            var cases = new List<(string Name, Report R, string Title, string VgcRow)>
            {
                ("M1 Vanguard not installed", new Report(), L.T("O Vanguard não está instalado", "Vanguard isn't installed"), null),
                ("M2 vgc service missing", new Report { VgkStart = 1, VgkSvc = "Running", VgkLoaded = true, VgkFromBoot = true },
                    L.T("Serviço do Vanguard não encontrado", "Vanguard service not found"), L.T("Não encontrado", "Not found")),
                ("M3 incomplete read (vgc status unreadable)", new Report { VgkStart = 1, VgcStart = 2, VgkSvc = "Running", VgkLoaded = true, VgkFromBoot = true },
                    L.T("Não deu para confirmar o estado", "Couldn't confirm the state"), L.T("Não deu para ler", "Couldn't read")),
                ("M4 boot check unknown", new Report { VgkStart = 1, VgcStart = 2, VgkSvc = "Running", VgcSvc = "Running", VgkLoaded = true, VgkFromBoot = null },
                    L.T("O Vanguard está ligado", "Vanguard is on"), null),
                ("M5 driver started after boot, 7 Riot processes", new Report { VgkStart = 1, VgcStart = 2, VgkSvc = "Running", VgcSvc = "Running", VgkLoaded = true, VgkFromBoot = false,
                    Procs = Enumerable.Range(1, 6).Select(i => new RiotProc { Id = i, Name = "Riot Client" }).Append(new RiotProc { Id = 7, Name = "RiotClientServices" })
                                      .Append(new RiotProc { Id = 8, Name = "vgc" }).Append(new RiotProc { Id = 9, Name = "vgtray" }).ToList() },
                    L.T("Reinicie para jogar", "Restart to play"), null),
            };
            string procLabel = L.T("Processos da Riot em execução", "Riot processes running");

            foreach (var c in cases)
            {
                // the window is never shown, so OnShown (real read, timer, update check) doesn't run
                using (var f = new MainForm())
                {
                    var _ = f.Handle;
                    apply.Invoke(f, new object[] { c.R });
                    var status = (StatusCard)statusField.GetValue(f);
                    var parts = (ListCard)partsField.GetValue(f);
                    string vgcRow = parts.Rows.FirstOrDefault(r => r.Label == L.T("Serviço (vgc)", "Service (vgc)"))?.Value;

                    // the form isn't shown, so each control is drawn on its own (the hidden log box is skipped)
                    using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                    {
                        using (var g = Graphics.FromImage(bmp)) g.Clear(Theme.Bg);
                        foreach (Control ctl in f.Controls)
                            if (!(ctl is TextBox) && ctl.Right <= bmp.Width && ctl.Bottom <= bmp.Height)
                                ctl.DrawToBitmap(bmp, ctl.Bounds);
                        bmp.Save(Path.Combine(dir, c.Name.Split(' ')[0] + ".png"));
                    }
                    string procRow = parts.Rows.FirstOrDefault(r => r.Label == procLabel)?.Value;
                    int riot = c.R.Procs.Count(p => p.Name != "vgc" && p.Name != "vgtray");
                    bool procOk = procRow == (riot == 0 ? L.T("Nenhum", "None") : riot.ToString());
                    bool noBreakOk = !status.Sentence.Contains("VAN 57") && parts.Rows.All(r => !r.Value.Contains("VAN 57"));
                    bool ok = status.Title == c.Title && (c.VgcRow == null || vgcRow == c.VgcRow) && procOk && noBreakOk;
                    Check(ok, c.Name, $"title=\"{status.Title}\" vgcRow=\"{vgcRow}\" procRow=\"{procRow}\" noBreakVan57={noBreakOk}");
                }
            }
        }

        // ---------- "Restart the PC?" after Turn on ----------

        static void RestartTextTests(string dir)
        {
            var cases = new (string Name, StartResult R, string Expect)[]
            {
                ("R1 driver started by this Turn on", new StartResult { VgkOk = true, LateStart = true, StartedNow = true }, L.T("ligou agora", "started now")),
                ("R2 driver already in memory, loaded after boot", new StartResult { VgkOk = true, LateStart = true, StartedNow = false }, L.T("já estava na memória", "already in memory")),
            };
            var ctor = typeof(Dialog).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            foreach (var c in cases)
            {
                // same text OnTurnOn builds
                string text = MainForm.RestartReason(c.R) + L.T("\n\nO Vanguard já vai ligar com o Windows. Reiniciar o PC agora?",
                                                                 "\n\nVanguard is set to start with Windows. Restart now?");
                bool ok = text.Contains(c.Expect) && text.Contains("VAN\u00A057") && !text.Contains("VAN 57")
                          && (c.R.StartedNow || !text.Contains(L.T("ligou agora", "started now")));

                // the real dialog, never shown: drawn control by control to see where lines break
                using (var font = new Font("Segoe UI", 9.75f))
                using (var titleFont = new Font("Segoe UI Semibold", 12f))
                using (var d = (Form)ctor.Invoke(new object[] { L.T("Reiniciar o PC?", "Restart the PC?"), text, true, Theme.Green, font, titleFont, null, null }))
                using (var bmp = new Bitmap(d.ClientSize.Width, d.ClientSize.Height))
                {
                    var _ = d.Handle;
                    using (var g = Graphics.FromImage(bmp)) g.Clear(Theme.Bg);
                    foreach (Control ctl in d.Controls) ctl.DrawToBitmap(bmp, ctl.Bounds);
                    bmp.Save(Path.Combine(dir, c.Name.Split(' ')[0] + ".png"));
                }
                Check(ok, c.Name, "text=\"" + text.Split('\n')[0] + "\"");
            }
        }
    }
}
