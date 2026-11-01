using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VanguardSwitch
{
    /// <summary>Colors and shapes close to the Windows 11 Settings dark theme.</summary>
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(32, 32, 32);
        public static readonly Color Card = Color.FromArgb(43, 43, 43);
        public static readonly Color CardLine = Color.FromArgb(29, 29, 29);
        public static readonly Color Button = Color.FromArgb(45, 45, 45);
        public static readonly Color ButtonLine = Color.FromArgb(58, 58, 58);
        public static readonly Color Text = Color.FromArgb(255, 255, 255);
        public static readonly Color Muted = Color.FromArgb(160, 160, 160);
        public static readonly Color Dim = Color.FromArgb(100, 100, 100);
        public static readonly Color Red = Color.FromArgb(220, 64, 60);
        public static readonly Color Green = Color.FromArgb(40, 150, 90);
        public static readonly Color GreenText = Color.FromArgb(108, 203, 95);
        public static readonly Color RedText = Color.FromArgb(255, 153, 164);
        public static readonly Color Amber = Color.FromArgb(252, 225, 0);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        public static void Box(Graphics g, Rectangle r, Color fill, Color line, float radius = 4)
        {
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            using (var p = Round(rf, radius))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(line))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }
    }

    abstract class PaintedControl : Control
    {
        protected bool Hover, Down;

        protected PaintedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = Down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected Graphics Begin(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            return g;
        }
    }

    /// <summary>Plain Windows 11 style button. Filled with Accent when it's the suggested action.</summary>
    class WinButton : PaintedControl
    {
        public Image Image;
        Color? accent;

        /// <summary>null = normal gray button, a color = filled "suggested" button.</summary>
        public Color? Accent { get => accent; set { accent = value; Invalidate(); } }

        public WinButton() { Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Color fill = accent ?? Theme.Button;
            Color line = accent.HasValue ? Theme.Mix(accent.Value, Color.White, 0.12f) : Theme.ButtonLine;
            if (!Enabled) { fill = Theme.Mix(fill, Theme.Bg, 0.6f); line = Theme.Mix(line, Theme.Bg, 0.6f); }
            else if (Down) fill = Theme.Mix(fill, Color.Black, 0.12f);
            else if (Hover) fill = Theme.Mix(fill, Color.White, 0.06f);
            Theme.Box(g, ClientRectangle, fill, line);

            Color fg = Enabled ? Theme.Text : Theme.Dim;
            var size = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int imgW = Image != null ? 16 + 8 : 0;
            int x = Math.Max(10, (Width - size.Width - imgW) / 2);
            if (Image != null)
            {
                var r = new Rectangle(x, (Height - 16) / 2, 16, 16);
                if (Enabled) g.DrawImage(Image, r);
                else
                {
                    // gray and faded, like the disabled text
                    var m = new System.Drawing.Imaging.ColorMatrix(new[]
                    {
                        new[] { .3f, .3f, .3f, 0, 0 },
                        new[] { .59f, .59f, .59f, 0, 0 },
                        new[] { .11f, .11f, .11f, 0, 0 },
                        new[] { 0f, 0, 0, .35f, 0 },
                        new[] { 0f, 0, 0, 0, 1 },
                    });
                    using (var ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetColorMatrix(m);
                        g.DrawImage(Image, r, 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, ia);
                    }
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + imgW, 0, Width - x - imgW, Height), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Status line: colored bar on the left, a title and one sentence.</summary>
    class StatusCard : PaintedControl
    {
        public string Title = "", Sentence = "";
        public Color Accent = Theme.Dim;
        public Font TitleFont, SentenceFont;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Theme.Box(g, ClientRectangle, Theme.Card, Theme.CardLine);
            using (var p = Theme.Round(new RectangleF(1, 1, 4, Height - 2), 2))
            using (var b = new SolidBrush(Accent))
                g.FillPath(b, p);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, Title, TitleFont, new Rectangle(20, 14, Width - 36, 24), Theme.Text, flags | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, Sentence, SentenceFont, new Rectangle(20, 42, Width - 36, Height - 48), Theme.Muted, flags | TextFormatFlags.WordBreak);
        }
    }

    class Row
    {
        public string Label, Value;
        public Color ValueColor = Theme.Text;
    }

    /// <summary>A Settings-style list: label on the left, value on the right, thin lines between rows.</summary>
    class ListCard : PaintedControl
    {
        public List<Row> Rows = new List<Row>();
        public int RowHeight = 38;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Theme.Box(g, ClientRectangle, Theme.Card, Theme.CardLine);
            var left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            var right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            using (var pen = new Pen(Theme.CardLine))
            {
                for (int i = 0; i < Rows.Count; i++)
                {
                    int y = i * RowHeight;
                    if (i > 0) g.DrawLine(pen, 1, y, Width - 2, y);
                    var r = new Rectangle(16, y, Width - 32, RowHeight);
                    TextRenderer.DrawText(g, Rows[i].Label, Font, r, Theme.Text, left);
                    TextRenderer.DrawText(g, Rows[i].Value, Font, r, Rows[i].ValueColor, right);
                }
            }
        }
    }

    /// <summary>Settings-style option: title and description, Windows 11 toggle switch on the right.</summary>
    class ToggleCard : PaintedControl
    {
        public string Title = "", Description = "", OnText = "On", OffText = "Off";
        public Font TitleFont, DescFont;
        public event EventHandler CheckedChanged;
        bool on;

        public bool Checked
        {
            get => on;
            set { if (on == value) return; on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        public ToggleCard() { Cursor = Cursors.Hand; }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Theme.Box(g, ClientRectangle, Hover && Enabled ? Theme.Mix(Theme.Card, Color.White, 0.03f) : Theme.Card, Theme.CardLine);

            var track = new RectangleF(Width - 16 - 40, (Height - 20) / 2f, 40, 20);
            if (on)
            {
                using (var p = Theme.Round(track, 10)) using (var b = new SolidBrush(Enabled ? Theme.Green : Theme.Dim)) g.FillPath(b, p);
                using (var k = new SolidBrush(Color.Black)) g.FillEllipse(k, track.Right - 16, track.Y + 4, 12, 12);
            }
            else
            {
                using (var p = Theme.Round(new RectangleF(track.X + 0.5f, track.Y + 0.5f, track.Width - 1, track.Height - 1), 10))
                using (var pen = new Pen(Theme.Muted)) g.DrawPath(pen, p);
                using (var k = new SolidBrush(Theme.Muted)) g.FillEllipse(k, track.X + 5, track.Y + 5, 10, 10);
            }
            string state = on ? OnText : OffText;
            var ss = TextRenderer.MeasureText(g, state, Font, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, state, Font, new Rectangle((int)track.X - 10 - ss.Width, 0, ss.Width + 2, Height), Enabled ? Theme.Text : Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int textW = (int)track.X - 30 - ss.Width;
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, Title, TitleFont, new Rectangle(16, 14, textW, 20), Enabled ? Theme.Text : Theme.Dim, flags | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Description, DescFont, new Rectangle(16, 36, textW, Height - 40), Theme.Muted, flags | TextFormatFlags.WordBreak);
        }
    }
}
