using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VanguardSwitch
{
    class MainForm : Form
    {
        const int BaseHeight = 662;

        readonly Font fTitle = new Font("Segoe UI Semibold", 12f);
        readonly Font fState = new Font("Segoe UI Semibold", 12f);
        readonly Font fText = new Font("Segoe UI", 9.75f);
        readonly Font fSmall = new Font("Segoe UI", 8.75f);
        readonly Font fSection = new Font("Segoe UI Semibold", 9.75f);
        readonly Font fLog = new Font("Consolas", 8.75f);

        StatusCard status;
        ListCard parts;
        WinButton btnOff, btnOn, btnRiot, btnDefault;
        ToggleCard toggle;
        LinkLabel lnkDetails, lnkHelp;
        TextBox txtLog;
        readonly Timer timer = new Timer { Interval = 3000 };

        bool busy, refreshing, warned, detailsOpen;
        int warnings;
        enum Watch { None, Off, Light }
        Watch watch = Watch.None;

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Text = "VanguardSwitch";
            ClientSize = new Size(460, BaseHeight);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = fText;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Build();

            Vg.Log = msg =>
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => AppendLog(msg)));
            };
            timer.Tick += (s, e) => RefreshStatus();
            Shown += OnShown;
            FormClosed += (s, e) => timer.Stop();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Native.DarkTitleBar(Handle); } catch { }
        }

        // ---------- layout ----------

        static Image IconOf(string exe)
        {
            // taken from the program installed on this PC, so the button shows Riot's own icon
            try
            {
                if (exe == null || !File.Exists(exe)) return null;
                using (var ico = Icon.ExtractAssociatedIcon(exe)) return ico?.ToBitmap();
            }
            catch { return null; }
        }

        Label NewLabel(string text, int x, int y, int w, int h, Font f, Color c) => new Label
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, h), Font = f, ForeColor = c, BackColor = Color.Transparent,
        };

        LinkLabel NewLink(string text, int x, int y, int w, ContentAlignment align = ContentAlignment.MiddleLeft) => new LinkLabel
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, 20), Font = fSmall, TextAlign = align, BackColor = Color.Transparent,
            LinkColor = Theme.Muted, ActiveLinkColor = Theme.Text, VisitedLinkColor = Theme.Muted, LinkBehavior = LinkBehavior.HoverUnderline,
        };

        void Build()
        {
            var logo = new PictureBox { Location = new Point(20, 17), Size = new Size(20, 20), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            try { logo.Image = new Icon(Icon, 32, 32).ToBitmap(); } catch { }
            var title = NewLabel("VanguardSwitch", 46, 14, 300, 26, fTitle, Theme.Text);

            status = new StatusCard { Location = new Point(20, 52), Size = new Size(420, 82), TitleFont = fState, SentenceFont = fText };

            var section = NewLabel(L.T("Partes do Vanguard", "Vanguard parts"), 20, 148, 420, 22, fSection, Theme.Text);
            parts = new ListCard { Location = new Point(20, 172), Size = new Size(420, 6 * 38), Font = fText };

            btnOff = new WinButton { Text = L.T("Desligar Vanguard", "Turn Vanguard off"), Location = new Point(20, 416), Size = new Size(206, 36), Font = fText };
            btnOn = new WinButton { Text = L.T("Ligar Vanguard", "Turn Vanguard on"), Location = new Point(234, 416), Size = new Size(206, 36), Font = fText };
            btnRiot = new WinButton
            {
                Text = L.T("Abrir Riot Client", "Open Riot Client"), Location = new Point(20, 460), Size = new Size(206, 36), Font = fText,
                Image = IconOf(Vg.FindRiotClient()),
            };
            var vgFolder = Vg.VanguardFolder();
            btnDefault = new WinButton
            {
                Text = L.T("Padrão da Riot e reiniciar", "Riot default and restart"), Location = new Point(234, 460), Size = new Size(206, 36), Font = fText,
                Image = vgFolder == null ? null : IconOf(Path.Combine(vgFolder, "vgtray.exe")) ?? IconOf(Path.Combine(vgFolder, "vgc.exe")),
            };

            toggle = new ToggleCard
            {
                Location = new Point(20, 514), Size = new Size(420, 96), Font = fText, TitleFont = fText, DescFont = fSmall, Checked = Vg.LightMode,
                OnText = L.T("Ligado", "On"), OffText = L.T("Desligado", "Off"),
                Title = L.T("Manter o driver carregado", "Keep the driver loaded"),
                Description = L.T("Deixa o \"Ligar\" funcionar sem reiniciar. Desligado, o Vanguard sai todo da memória e é preciso reiniciar antes de jogar.",
                                  "Lets \"Turn on\" work without a restart. When off, Vanguard is fully unloaded and you must restart before playing."),
            };

            lnkDetails = NewLink("Logs", 18, 628, 160);
            lnkHelp = NewLink(L.T("Como funciona", "How it works"), 180, 628, 140);
            var version = NewLabel("v" + Application.ProductVersion.Split('+')[0], 340, 628, 100, 20, fSmall, Theme.Dim);
            version.TextAlign = ContentAlignment.MiddleRight;

            txtLog = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(20, BaseHeight),
                Size = new Size(420, 150), BackColor = Theme.Card, ForeColor = Theme.Muted, BorderStyle = BorderStyle.None, Font = fLog, Visible = false,
            };

            Controls.AddRange(new Control[] { logo, title, status, section, parts, btnOff, btnOn, btnRiot, btnDefault, toggle, lnkDetails, lnkHelp, version, txtLog });

            var tip = new ToolTip();
            tip.SetToolTip(btnOff, L.T("Fecha o Riot Client, os jogos, o serviço e o ícone da bandeja.", "Closes Riot Client, the games, the service and the tray icon."));
            tip.SetToolTip(btnOn, L.T("Liga o serviço e o ícone da bandeja de novo.", "Starts the service and the tray icon again."));
            tip.SetToolTip(btnRiot, L.T("Abre o Riot Client como usuário normal.", "Opens Riot Client as a normal user."));
            tip.SetToolTip(btnDefault, L.T("Deixa o Vanguard como a Riot instala (liga com o Windows) e reinicia o PC.",
                                           "Puts Vanguard back the way Riot installs it (starts with Windows) and restarts the PC."));

            toggle.CheckedChanged += (s, e) => Vg.LightMode = toggle.Checked;
            btnOff.Click += OnTurnOff;
            btnOn.Click += OnTurnOn;
            btnRiot.Click += (s, e) => OpenRiot(false);
            btnDefault.Click += OnDefault;
            lnkHelp.LinkClicked += (s, e) => ShowHelp();
            lnkDetails.LinkClicked += (s, e) => ToggleDetails();
        }

        void ToggleDetails()
        {
            detailsOpen = !detailsOpen;
            txtLog.Visible = detailsOpen;
            // measured from the controls, so it stays right when Windows scales the window (125%, 150%...)
            int gap = txtLog.Top - lnkDetails.Bottom;
            ClientSize = new Size(ClientSize.Width, detailsOpen ? txtLog.Bottom + gap : txtLog.Top);
            warnings = 0;
            UpdateDetailsLink();
        }

        void UpdateDetailsLink()
        {
            string text = detailsOpen ? L.T("Esconder logs", "Hide logs") : "Logs";
            if (!detailsOpen && warnings > 0) text += $" ({warnings} {(warnings == 1 ? L.T("aviso", "warning") : L.T("avisos", "warnings"))})";
            lnkDetails.Text = text;
            lnkDetails.LinkColor = !detailsOpen && warnings > 0 ? Theme.Amber : Theme.Muted;
        }

        void ShowHelp() => Info(L.T("Como funciona", "How it works"), L.T(
            "O Vanguard tem três partes:\n\n" +
            "Driver (vgk): roda no núcleo do Windows. No modo tradicional do Vanguard, ele precisa carregar junto com o PC.\n" +
            "Serviço (vgc): conversa com o jogo.\n" +
            "Ícone da bandeja (vgtray).\n\n" +
            "Desligar fecha a Riot, o serviço e o ícone e impede que eles voltem sozinhos. Com \"Manter o driver carregado\" ligado, só o driver fica na memória, e por isso o Ligar funciona sem reiniciar.\n\n" +
            "Ligar religa o serviço e o ícone e deixa o Vanguard ligando com o Windows de novo.\n\n" +
            "Erro VAN\u00A057: no modo tradicional, ligar o driver depois do boot pode causar esse erro. Reiniciar o PC resolve. (O modo On-Demand oficial da Riot, para PCs que passam no Pre-Check, funciona de outro jeito.)\n\n" +
            "O VanguardSwitch não altera nenhum arquivo do Vanguard. Ele só usa os controles de serviço do próprio Windows.",

            "Vanguard has three parts:\n\n" +
            "Driver (vgk): runs in the Windows kernel. In Vanguard's traditional mode it needs to load when the PC starts.\n" +
            "Service (vgc): talks to the game.\n" +
            "Tray icon (vgtray).\n\n" +
            "Turn off closes Riot, the service and the tray icon and stops them from coming back on their own. With \"Keep the driver loaded\" on, only the driver stays in memory, which is why Turn on works without a restart.\n\n" +
            "Turn on starts the service and the tray icon again and sets Vanguard to start with Windows.\n\n" +
            "Error VAN\u00A057: in the traditional mode, starting the driver after boot can cause it. Restarting fixes it. (Riot's official On-Demand mode, for PCs that pass Pre-Check, works differently.)\n\n" +
            "VanguardSwitch doesn't change any Vanguard file. It only uses Windows' own service controls."));

        // ---------- helpers ----------

        void AppendLog(string msg) => txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");

        void Warn(string msg)
        {
            AppendLog(msg);
            if (!detailsOpen) { warnings++; UpdateDetailsLink(); }
        }

        void SetBusy(bool on, string what = null)
        {
            busy = on;
            foreach (Control c in new Control[] { btnOff, btnOn, btnRiot, btnDefault, toggle }) c.Enabled = !on;
            Cursor = on ? Cursors.WaitCursor : Cursors.Default;
            if (on) ShowState(Theme.Dim, L.T("Aguarde...", "Working..."), what, null);
        }

        int dialogs;   // dialogs currently open (the update notice waits for them)

        DialogResult Ask(string title, string text, Color? accent = null)
        {
            dialogs++;
            try { return Dialog.Ask(this, title, text, accent); }
            finally { dialogs--; }
        }

        void Info(string title, string text)
        {
            dialogs++;
            try { Dialog.Info(this, title, text); }
            finally { dialogs--; }
        }

        // ---------- status ----------

        /// <summary>suggestOn: true = Turn on is the next step, false = Turn off, null = leave buttons as they are.</summary>
        void ShowState(Color accent, string title, string sentence, bool? suggestOn)
        {
            status.Accent = accent;
            status.Title = title;
            status.Sentence = sentence;
            status.Invalidate();
            if (suggestOn.HasValue)
            {
                btnOn.Accent = suggestOn.Value ? Theme.Green : (Color?)null;
                btnOff.Accent = suggestOn.Value ? (Color?)null : Theme.Red;
            }
        }

        async void OnShown(object sender, EventArgs e)
        {
            AppendLog(L.T("Aberto como administrador.", "Running as administrator."));
            ShowState(Theme.Dim, L.T("Lendo...", "Reading..."), L.T("Conferindo o estado do Vanguard.", "Checking Vanguard's state."), null);
            try
            {
                var r = await Task.Run(Vg.GetReport);
                if (r.VgkStart == 4 && !r.VgkLoaded) watch = Watch.Off;
                else if (r.VgcStart == 4 && r.VgkStart <= 2) watch = Watch.Light;
                Apply(r);
            }
            catch (Exception ex)
            {
                Warn(L.T("Erro ao ler o estado: ", "Error reading the state: ") + ex.Message);
                ShowState(Theme.Amber, L.T("Não deu para ler o estado", "Couldn't read the state"),
                    L.T("O app vai tentar de novo em alguns segundos. Veja os Logs.", "The app will try again in a few seconds. See Logs."), null);
            }
            timer.Start();
            CheckForUpdate();
        }

        // ---------- updates ----------

        async void CheckForUpdate()
        {
            var rel = await Task.Run(Updater.Check);
            if (rel == null || IsDisposed) return;
            while (busy || dialogs > 0) await Task.Delay(1000);   // don't interrupt an action or an open question
            if (IsDisposed) return;

            AppendLog(L.T($"Nova versão disponível: v{rel.Version}.", $"New version available: v{rel.Version}."));
            busy = true;   // nothing else starts while the notice is open
            DialogResult answer;
            try
            {
                answer = Dialog.Choose(this, L.T("Nova versão disponível", "Update available"),
                    L.T($"A versão {rel.Version} do VanguardSwitch saiu (você tem a {Updater.Current}).\n\nAtualizar agora? O app baixa a nova versão do GitHub, confere o arquivo e abre de novo sozinho.",
                        $"VanguardSwitch {rel.Version} is out (you have {Updater.Current}).\n\nUpdate now? The app downloads the new version from GitHub, checks the file and reopens by itself."),
                    L.T("Atualizar", "Update"), L.T("Fechar", "Close"));
            }
            finally { busy = false; }
            if (answer != DialogResult.Yes) return;

            SetBusy(true, L.T($"Baixando a versão {rel.Version}.", $"Downloading version {rel.Version}."));
            try
            {
                await Task.Run(() => Updater.Install(rel));
                Close();
            }
            catch (Exception ex)
            {
                SetBusy(false);
                Warn(L.T("Falha ao atualizar: ", "Update failed: ") + ex.Message);
                if (Dialog.Choose(this, L.T("Não deu para atualizar", "Update failed"),
                        ex.Message + L.T("\n\nVocê pode baixar a nova versão pela página da release.", "\n\nYou can download it from the release page."),
                        L.T("Abrir página", "Open page"), L.T("Fechar", "Close")) == DialogResult.Yes)
                    try { Vg.StartAsUser(rel.Page); } catch { }   // not elevated, like Riot Client
                RefreshStatus();
            }
        }

        async void RefreshStatus()
        {
            if (busy || refreshing) return;
            refreshing = true;
            try { Apply(await Task.Run(Vg.GetReport)); }
            catch { }
            finally { refreshing = false; }
        }

        void Apply(Report r)
        {
            if (busy) return;
            bool vgcBlocked = r.VgcStart == 4, vgkBlocked = r.VgkStart == 4;

            if (!r.Installed)
                ShowState(Theme.Dim, L.T("O Vanguard não está instalado", "Vanguard isn't installed"),
                    L.T("Ele vem junto com o VALORANT e o League of Legends.", "It comes with VALORANT and League of Legends."), true);
            else if (r.VgcStart == null && r.VgcSvc == null)
                ShowState(Theme.Amber, L.T("Serviço do Vanguard não encontrado", "Vanguard service not found"),
                    L.T("O driver está instalado, mas o serviço vgc não. Abra o Riot Client para ele reparar o Vanguard.",
                        "The driver is installed but the vgc service isn't. Open Riot Client so it can repair Vanguard."), null);
            else if (r.VgkStart == null || r.VgcStart == null || r.VgcSvc == null)
                ShowState(Theme.Amber, L.T("Não deu para confirmar o estado", "Couldn't confirm the state"),
                    L.T("Parte das informações dos serviços não pôde ser lida. O app tenta de novo em alguns segundos.",
                        "Some service information couldn't be read. The app tries again in a few seconds."), null);
            else if (r.VgkLoaded && r.VgkFromBoot == false && !vgkBlocked)
                ShowState(Theme.Amber, L.T("Reinicie para jogar", "Restart to play"),
                    L.T("O driver foi ligado depois que o Windows iniciou. No modo tradicional do Vanguard, isso pode causar o erro VAN\u00A057.",
                        "The driver was started after Windows booted. In Vanguard's traditional mode this can cause error VAN\u00A057."), true);
            else if (r.VgkLoaded && !vgcBlocked && !vgkBlocked)
                ShowState(Theme.Green, L.T("O Vanguard está ligado", "Vanguard is on"),
                    L.T("Você já pode jogar. Quando terminar, clique em Desligar Vanguard.", "You can play now. When you're done, click Turn Vanguard off."), false);
            else if (r.VgkLoaded && vgcBlocked && !vgkBlocked)
                ShowState(Theme.Red, L.T("O Vanguard está desligado", "Vanguard is off"),
                    L.T("Só o driver continua carregado, então dá para ligar de novo sem reiniciar.",
                        "Only the driver is still loaded, so you can turn it back on without a restart."), true);
            else if (r.VgkLoaded && vgkBlocked)
                ShowState(Theme.Amber, L.T("Reinicie o PC", "Restart the PC"),
                    L.T("O Vanguard foi desligado, mas o driver só sai da memória depois de reiniciar.",
                        "Vanguard is off, but the driver only leaves memory after a restart."), true);
            else if (vgkBlocked)
                ShowState(Theme.Red, L.T("O Vanguard está desligado", "Vanguard is off"),
                    L.T("O driver também foi descarregado. Para jogar, clique em Ligar Vanguard e reinicie o PC uma vez.",
                        "The driver is unloaded too. To play, click Turn Vanguard on and restart once."), true);
            else
                ShowState(Theme.Amber, L.T("Reinicie para jogar", "Restart to play"),
                    L.T("O driver do Vanguard só carrega quando o Windows inicia.", "The Vanguard driver only loads when Windows starts."), true);

            int riot = r.Procs.Count(p => !p.Name.Equals("vgc", StringComparison.OrdinalIgnoreCase) && !p.Name.Equals("vgtray", StringComparison.OrdinalIgnoreCase));

            Row vgc;
            if (r.VgcSvc == "Running") vgc = new Row { Value = L.T("Rodando", "Running"), ValueColor = Theme.GreenText };
            else if (r.VgcSvc == null && r.VgcStart == null) vgc = new Row { Value = L.T("Não encontrado", "Not found"), ValueColor = Theme.Muted };
            else if (r.VgcSvc == null) vgc = new Row { Value = L.T("Não deu para ler", "Couldn't read"), ValueColor = Theme.Amber };
            else if (vgcBlocked) vgc = new Row { Value = L.T("Parado e bloqueado", "Stopped and blocked"), ValueColor = Theme.RedText };
            else vgc = new Row { Value = L.T("Parado", "Stopped"), ValueColor = Theme.Muted };
            vgc.Label = L.T("Serviço (vgc)", "Service (vgc)");

            Row boot;
            if (vgkBlocked) boot = new Row { Value = L.T("Nada liga", "Nothing starts"), ValueColor = Theme.RedText };
            else if (vgcBlocked) boot = new Row { Value = L.T("Só o driver", "Driver only"), ValueColor = Theme.Muted };
            else if (r.VgkStart <= 1) boot = new Row { Value = L.T("O Vanguard liga junto", "Vanguard starts too"), ValueColor = Theme.GreenText };
            else if (r.VgkStart == null) boot = new Row { Value = "-", ValueColor = Theme.Muted };
            else boot = new Row { Value = "Manual", ValueColor = Theme.Amber };
            boot.Label = L.T("Quando o Windows inicia", "When Windows starts");

            Row bootRow;
            if (!r.VgkLoaded) bootRow = new Row { Value = "-", ValueColor = Theme.Muted };
            else if (r.VgkFromBoot == true) bootRow = new Row { Value = L.T("Sim", "Yes"), ValueColor = Theme.GreenText };
            else if (r.VgkFromBoot == false) bootRow = new Row { Value = L.T("Não (pode dar VAN\u00A057)", "No (may cause VAN\u00A057)"), ValueColor = Theme.RedText };
            else bootRow = new Row { Value = L.T("Não dá para saber", "Unknown"), ValueColor = Theme.Muted };
            bootRow.Label = L.T("Driver carregou no boot", "Driver loaded at boot");

            parts.Rows = new List<Row>
            {
                new Row { Label = "Driver (vgk)", Value = r.VgkLoaded ? L.T("Carregado", "Loaded") : L.T("Não carregado", "Not loaded"), ValueColor = r.VgkLoaded ? Theme.GreenText : Theme.Muted },
                bootRow,
                vgc,
                new Row { Label = L.T("Ícone da bandeja", "Tray icon"), Value = r.Tray ? L.T("Aberto", "Open") : L.T("Fechado", "Closed"), ValueColor = r.Tray ? Theme.GreenText : Theme.Muted },
                // counts processes (Riot Client alone runs several), not programs; vgc and vgtray have their own rows
                new Row { Label = L.T("Processos da Riot em execução", "Riot processes running"), Value = riot == 0 ? L.T("Nenhum", "None") : riot.ToString(), ValueColor = riot == 0 ? Theme.Muted : Theme.GreenText },
                boot,
            };
            parts.Invalidate();

            // watchdog: we turned it off, but something turned it back on or changed it
            if (!warned && watch == Watch.Light && r.VgcSvc == "Running")
            {
                Warn(L.T("Atenção: outro programa religou o serviço vgc.", "Warning: another program turned the vgc service back on."));
                warned = true;
            }
            if (!warned && watch == Watch.Off)
            {
                if (r.VgkLoaded) { Warn(L.T("Atenção: outro programa religou o Vanguard.", "Warning: another program turned Vanguard back on.")); warned = true; }
                else if (r.VgkStart != null && r.VgkStart != 4) { Warn(L.T("Atenção: outro programa mudou a inicialização do Vanguard (a Riot pode ter reparado a instalação).", "Warning: another program changed Vanguard's startup (Riot may have repaired it).")); warned = true; }
            }
        }

        // ---------- actions ----------

        async void OnTurnOff(object sender, EventArgs e)
        {
            if (busy) return;
            // lock the buttons before the first await, so a second click can't start another action
            SetBusy(true, L.T("Conferindo se tem jogo aberto.", "Checking for running games."));
            try
            {
                List<RiotProc> playing;
                try { playing = await Task.Run(() => Vg.RiotProcesses(false).Where(p => Vg.GameNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList()); }
                catch (Exception ex) { Warn("Erro/Error: " + ex.Message); playing = new List<RiotProc>(); }
                if (playing.Count > 0 && Ask(L.T("Jogo aberto", "Game running"), L.T($"Tem um jogo da Riot aberto ({playing[0].Name}). Fechar mesmo assim?",
                                                 $"A Riot game is running ({playing[0].Name}). Close it anyway?"), Theme.Red) != DialogResult.Yes) return;

                SetBusy(true, L.T("Fechando a Riot e desligando o Vanguard.", "Closing Riot and turning Vanguard off."));
                bool light = toggle.Checked;
                var res = await Task.Run(() => Vg.Close(light));
                watch = light ? Watch.Light : Watch.Off;
                warned = false;

                if (res.Fails.Count > 0)
                {
                    Warn(L.T("Nem tudo foi desligado.", "Not everything was turned off."));
                    Info(L.T("Nem tudo foi desligado", "Not everything was turned off"), string.Join("\n", res.Fails));
                }
                else if (res.VgkStuck && Ask(L.T("Reiniciar o PC?", "Restart the PC?"), L.T("Tudo foi fechado, mas o driver vgk não quis sair da memória agora.\nEle já está bloqueado e sai quando o PC reiniciar.\n\nReiniciar agora?",
                                                 "Everything is closed, but the vgk driver refused to unload.\nIt's already blocked and goes away after a restart.\n\nRestart now?")) == DialogResult.Yes)
                    Vg.Reboot("VanguardSwitch");
            }
            catch (Exception ex) { Warn("Erro/Error: " + ex.Message); }
            finally { SetBusy(false); RefreshStatus(); }
        }

        /// <summary>First part of the "Restart the PC?" question after Turn on. "VAN\u00A057" uses a no-break space so it doesn't split across lines.</summary>
        internal static string RestartReason(StartResult res) =>
            !res.VgkOk
                ? L.T("O driver do Vanguard não ligou sem reiniciar.", "The Vanguard driver didn't start without a restart.")
            : res.StartedNow
                ? L.T("O driver ligou agora, mas não junto com o Windows. No modo tradicional do Vanguard, isso pode causar o erro VAN\u00A057.",
                      "The driver started now, not with Windows. In Vanguard's traditional mode this can cause error VAN\u00A057.")
                : L.T("O driver já estava na memória, mas foi ligado depois que o Windows iniciou. No modo tradicional do Vanguard, isso pode causar o erro VAN\u00A057.",
                      "The driver was already in memory, but it was started after Windows booted. In Vanguard's traditional mode this can cause error VAN\u00A057.");

        async void OnTurnOn(object sender, EventArgs e)
        {
            if (busy) return;
            if (Vg.StartType("vgk") == null)
            {
                Info(L.T("Vanguard não encontrado", "Vanguard not found"), L.T("O Vanguard não está instalado. Abra o Riot Client para ele instalar.", "Vanguard isn't installed. Open Riot Client to install it."));
                return;
            }

            SetBusy(true, L.T("Ligando o Vanguard.", "Turning Vanguard on."));
            try
            {
                watch = Watch.None;
                var res = await Task.Run(Vg.Start);

                if (!res.VgkOk || res.LateStart)
                {
                    if (Ask(L.T("Reiniciar o PC?", "Restart the PC?"), RestartReason(res) + L.T("\n\nO Vanguard já vai ligar com o Windows. Reiniciar o PC agora?",
                                      "\n\nVanguard is set to start with Windows. Restart now?")) == DialogResult.Yes)
                        Vg.Reboot("VanguardSwitch");
                }
            }
            catch (Exception ex) { Warn("Erro/Error: " + ex.Message); }
            finally { SetBusy(false); RefreshStatus(); }
        }

        async void OnDefault(object sender, EventArgs e)
        {
            if (busy) return;
            if (Ask(L.T("Padrão da Riot e reiniciar", "Riot default and restart"), L.T("Isso deixa o Vanguard como a Riot instala (liga junto com o Windows) e reinicia o PC agora.\n\nSalve o que estiver aberto antes. Continuar?",
                        "This puts Vanguard back the way Riot installs it (starts with Windows) and restarts the PC now.\n\nSave your work first. Continue?"), Theme.Red) != DialogResult.Yes) return;

            SetBusy(true, L.T("Restaurando o padrão da Riot. O PC vai reiniciar.", "Restoring Riot default. The PC will restart."));
            try
            {
                watch = Watch.None;
                var res = await Task.Run(Vg.RestoreDefault);
                if (res.Ok)
                {
                    AppendLog(L.T("Reiniciando...", "Restarting..."));
                    Vg.Reboot("VanguardSwitch");
                }
                else
                {
                    if (res.ConfigOk && !res.TrayFound)
                    {
                        Warn(L.T("Entrada da bandeja não encontrada.", "Tray entry not found."));
                        // nothing to restore for the tray; the essential part is done
                        if (Ask(L.T("Bandeja não encontrada", "Tray entry not found"),
                                L.T("O driver e o serviço voltaram ao padrão, mas a entrada da bandeja do Vanguard não existe no Registro, então ela não foi restaurada. Reinstalar o Vanguard pelo Riot Client recria ela.\n\nReiniciar o PC mesmo assim?",
                                    "The driver and service are back to default, but Vanguard's tray entry doesn't exist in the registry, so it wasn't restored. Reinstalling Vanguard from Riot Client recreates it.\n\nRestart the PC anyway?")) == DialogResult.Yes)
                            Vg.Reboot("VanguardSwitch");
                    }
                    else
                    {
                        Warn(L.T("Não deu para restaurar o padrão.", "Couldn't restore the default."));
                        string what = !res.ConfigOk
                            ? L.T("Não deu para voltar o driver e o serviço a ligar com o Windows.", "Couldn't set the driver and service to start with Windows.")
                            : L.T("O driver e o serviço voltaram ao padrão, mas não deu para pôr a bandeja de volta na inicialização.", "The driver and service are back to default, but the tray couldn't be added back to startup.");
                        Info(L.T("Algo deu errado", "Something went wrong"), what + L.T("\n\nO PC não foi reiniciado. Veja os Logs.", "\n\nThe PC was not restarted. See the Logs."));
                    }
                }
            }
            catch (Exception ex) { Warn("Erro/Error: " + ex.Message); }
            finally { SetBusy(false); RefreshStatus(); }
        }

        async void OpenRiot(bool quiet)
        {
            var exe = Vg.FindRiotClient();
            if (exe == null)
            {
                Info(L.T("Riot Client não encontrado", "Riot Client not found"), L.T("Não encontrei o Riot Client.\nCaminho esperado: C:\\Riot Games\\Riot Client\\RiotClientServices.exe",
                         "Riot Client not found.\nExpected at: C:\\Riot Games\\Riot Client\\RiotClientServices.exe"));
                return;
            }
            bool opened;
            try
            {
                if (!quiet)
                {
                    bool off = await Task.Run(() => !Vg.VgkLoaded() || Vg.StartType("vgc") == 4);
                    if (off && Ask(L.T("Vanguard desligado", "Vanguard is off"), L.T("O Vanguard está desligado, então VALORANT e LoL não vão abrir.\n\nAbrir o Riot Client mesmo assim?",
                                       "Vanguard is off, so VALORANT and LoL won't launch.\n\nOpen Riot Client anyway?")) != DialogResult.Yes) return;
                }

                AppendLog(L.T("Abrindo o Riot Client: ", "Opening Riot Client: ") + exe);
                try { Vg.StartAsUser(exe); } catch { }

                // check it opened
                opened = await Task.Run(() =>
                {
                    var limit = DateTime.Now.AddSeconds(8);
                    while (DateTime.Now < limit)
                    {
                        if (Vg.ClientNames.Any(n => Process.GetProcessesByName(n).Length > 0)) return true;
                        System.Threading.Thread.Sleep(400);
                    }
                    return false;
                });
            }
            catch (Exception ex) { Warn("Erro/Error: " + ex.Message); return; }
            if (opened) return;

            // Not started directly on purpose: this app runs as admin and Riot Client shouldn't.
            Warn(L.T("O Riot Client não abriu pelo explorer.", "Riot Client didn't open through explorer."));
            Info(L.T("Riot Client não abriu", "Riot Client didn't open"),
                 L.T("Não consegui abrir o Riot Client como usuário normal. Abra pelo atalho da área de trabalho ou do menu Iniciar.",
                     "Couldn't open Riot Client as a normal user. Open it from its desktop or Start menu shortcut."));
        }
    }
}
