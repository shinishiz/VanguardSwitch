using System;
using System.Drawing;
using System.Windows.Forms;

namespace VanguardSwitch
{
    /// <summary>Message box in the app's own dark style (replaces MessageBox).</summary>
    class Dialog : Form
    {
        DialogResult result = DialogResult.Cancel;

        Dialog(string title, string text, bool yesNo, Color accent, Font font, Font titleFont, string yesText, string noText)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Text = "VanguardSwitch";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = font;
            KeyPreview = true;

            const int width = 420, pad = 24;
            var bodySize = TextRenderer.MeasureText(text, font, new Size(width - 2 * pad, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            var heading = new Label
            {
                Text = title, Font = titleFont, ForeColor = Theme.Text, BackColor = Color.Transparent,
                Location = new Point(pad, 20), Size = new Size(width - 2 * pad, 26),
            };
            var body = new Label
            {
                Text = text, Font = font, ForeColor = Color.FromArgb(215, 215, 215), BackColor = Color.Transparent, UseMnemonic = false,
                Location = new Point(pad, 56), Size = new Size(width - 2 * pad, bodySize.Height + 4),
            };

            var footer = new Panel { Location = new Point(0, body.Bottom + 22), Size = new Size(width, 64), BackColor = Theme.Card };
            var top = new Panel { Location = new Point(0, 0), Size = new Size(width, 1), BackColor = Theme.CardLine };
            footer.Controls.Add(top);

            var ok = new WinButton { Text = yesText ?? (yesNo ? L.T("Sim", "Yes") : "OK"), Accent = accent, Size = new Size(110, 34), Font = font };
            ok.Click += (s, e) => { result = yesNo ? DialogResult.Yes : DialogResult.OK; Close(); };
            if (yesNo)
            {
                var no = new WinButton { Text = noText ?? L.T("Não", "No"), Size = new Size(110, 34), Font = font };
                no.Click += (s, e) => { result = DialogResult.No; Close(); };
                no.Location = new Point(width - pad - 110, 15);
                ok.Location = new Point(no.Left - 8 - 110, 15);
                footer.Controls.Add(no);
            }
            else ok.Location = new Point(width - pad - 110, 15);
            footer.Controls.Add(ok);

            ClientSize = new Size(width, footer.Bottom);
            Controls.AddRange(new Control[] { heading, body, footer });

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { result = yesNo ? DialogResult.Yes : DialogResult.OK; Close(); }
                else if (e.KeyCode == Keys.Escape) { result = yesNo ? DialogResult.No : DialogResult.OK; Close(); }
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Native.DarkTitleBar(Handle); } catch { }
        }

        static DialogResult Run(IWin32Window owner, string title, string text, bool yesNo, Color accent, string yesText = null, string noText = null)
        {
            var parent = owner as Form;
            var font = parent?.Font ?? new Font("Segoe UI", 9.75f);
            using (var titleFont = new Font("Segoe UI Semibold", 12f))
            using (var d = new Dialog(title, text, yesNo, accent, font, titleFont, yesText, noText))
            {
                d.ShowDialog(owner);
                return d.result;
            }
        }

        public static DialogResult Ask(IWin32Window owner, string title, string text, Color? accent = null) =>
            Run(owner, title, text, true, accent ?? Theme.Green);

        /// <summary>Yes/No with custom button texts.</summary>
        public static DialogResult Choose(IWin32Window owner, string title, string text, string yesText, string noText) =>
            Run(owner, title, text, true, Theme.Green, yesText, noText);

        public static void Info(IWin32Window owner, string title, string text) =>
            Run(owner, title, text, false, Theme.Green);
    }
}
