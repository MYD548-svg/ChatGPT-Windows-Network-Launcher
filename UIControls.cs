using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ChatGPTAntiBanLauncher
{
    public static class Theme
    {
        public static bool IsDark { get; private set; }

        public static Color Window { get; private set; }
        public static Color Card { get; private set; }
        public static Color CardBorder { get; private set; }
        public static Color Text { get; private set; }
        public static Color TextDim { get; private set; }
        public static Color Accent { get; private set; }
        public static Color AccentHover { get; private set; }
        public static Color AccentPressed { get; private set; }
        public static Color InfoBg { get; private set; }
        public static Color InfoText { get; private set; }
        public static Color InfoBorder { get; private set; }
        public static Color SuccessBg { get; private set; }
        public static Color SuccessText { get; private set; }
        public static Color SuccessBorder { get; private set; }
        public static Color SuccessHover { get; private set; }
        public static Color Success { get; private set; }
        public static Color Danger { get; private set; }
        public static Color DangerBg { get; private set; }
        public static Color Warning { get; private set; }
        public static Color DisabledBg { get; private set; }
        public static Color DisabledFg { get; private set; }
        public static Color DisabledBorder { get; private set; }
        public static Color BadgeBg { get; private set; }
        public static Color BadgeBorder { get; private set; }

        private static Dictionary<int, int> activeRemap;

        // (light, dark) pairs covering every color used by the UI
        private static readonly int[][] PalettePairs = new int[][]
        {
            P(248, 249, 250,   27, 28, 31),
            P(255, 255, 255,   36, 38, 43),
            P(226, 232, 240,   58, 61, 69),
            P(30, 41, 59,      231, 233, 236),
            P(100, 116, 139,   154, 160, 168),
            P(15, 108, 189,    76, 155, 232),
            P(29, 122, 203,    96, 168, 236),
            P(11, 92, 163,     58, 130, 200),
            P(239, 246, 255,   30, 42, 58),
            P(29, 78, 216,     138, 180, 248),
            P(191, 219, 254,   56, 78, 110),
            P(219, 234, 254,   35, 48, 66),
            P(236, 253, 245,   23, 53, 43),
            P(5, 150, 105,     74, 222, 128),
            P(167, 243, 208,   46, 93, 73),
            P(209, 250, 229,   30, 66, 54),
            P(22, 163, 74,     74, 222, 128),
            P(34, 197, 94,     74, 222, 128),
            P(239, 68, 68,     248, 113, 113),
            P(220, 38, 38,     248, 113, 113),
            P(254, 242, 242,   62, 32, 32),
            P(234, 179, 8,     251, 191, 36),
            P(217, 119, 6,     251, 191, 36),
            P(241, 245, 249,   49, 52, 59),
            P(148, 163, 184,   110, 116, 125),
            P(238, 242, 255,   42, 51, 66),
            P(248, 250, 252,   44, 46, 52),
            P(224, 231, 255,   58, 74, 94)
        };

        private static int[] P(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            return new int[] { r1, g1, b1, r2, g2, b2 };
        }

        static Theme()
        {
            Apply(false);
        }

        public static void Apply(bool dark)
        {
            IsDark = dark;
            if (dark)
            {
                Window = Color.FromArgb(27, 28, 31);
                Card = Color.FromArgb(36, 38, 43);
                CardBorder = Color.FromArgb(58, 61, 69);
                Text = Color.FromArgb(231, 233, 236);
                TextDim = Color.FromArgb(154, 160, 168);
                Accent = Color.FromArgb(76, 155, 232);
                AccentHover = Color.FromArgb(96, 168, 236);
                AccentPressed = Color.FromArgb(58, 130, 200);
                InfoBg = Color.FromArgb(30, 42, 58);
                InfoText = Color.FromArgb(138, 180, 248);
                InfoBorder = Color.FromArgb(56, 78, 110);
                SuccessBg = Color.FromArgb(23, 53, 43);
                SuccessText = Color.FromArgb(74, 222, 128);
                SuccessBorder = Color.FromArgb(46, 93, 73);
                SuccessHover = Color.FromArgb(30, 66, 54);
                Success = Color.FromArgb(74, 222, 128);
                Danger = Color.FromArgb(248, 113, 113);
                DangerBg = Color.FromArgb(62, 32, 32);
                Warning = Color.FromArgb(251, 191, 36);
                DisabledBg = Color.FromArgb(49, 52, 59);
                DisabledFg = Color.FromArgb(110, 116, 125);
                DisabledBorder = Color.FromArgb(58, 61, 69);
                BadgeBg = Color.FromArgb(42, 51, 66);
                BadgeBorder = Color.FromArgb(58, 74, 94);
            }
            else
            {
                Window = Color.FromArgb(248, 249, 250);
                Card = Color.FromArgb(255, 255, 255);
                CardBorder = Color.FromArgb(226, 232, 240);
                Text = Color.FromArgb(30, 41, 59);
                TextDim = Color.FromArgb(100, 116, 139);
                Accent = Color.FromArgb(15, 108, 189);
                AccentHover = Color.FromArgb(29, 122, 203);
                AccentPressed = Color.FromArgb(11, 92, 163);
                InfoBg = Color.FromArgb(239, 246, 255);
                InfoText = Color.FromArgb(29, 78, 216);
                InfoBorder = Color.FromArgb(191, 219, 254);
                SuccessBg = Color.FromArgb(236, 253, 245);
                SuccessText = Color.FromArgb(5, 150, 105);
                SuccessBorder = Color.FromArgb(167, 243, 208);
                SuccessHover = Color.FromArgb(209, 250, 229);
                Success = Color.FromArgb(22, 163, 74);
                Danger = Color.FromArgb(239, 68, 68);
                DangerBg = Color.FromArgb(254, 242, 242);
                Warning = Color.FromArgb(217, 119, 6);
                DisabledBg = Color.FromArgb(241, 245, 249);
                DisabledFg = Color.FromArgb(148, 163, 184);
                DisabledBorder = Color.FromArgb(226, 232, 240);
                BadgeBg = Color.FromArgb(238, 242, 255);
                BadgeBorder = Color.FromArgb(224, 231, 255);
            }
            BuildActiveRemap();
        }

        private static void BuildActiveRemap()
        {
            activeRemap = new Dictionary<int, int>();
            foreach (int[] pair in PalettePairs)
            {
                int light = Color.FromArgb(pair[0], pair[1], pair[2]).ToArgb();
                int dark = Color.FromArgb(pair[3], pair[4], pair[5]).ToArgb();
                if (IsDark)
                {
                    activeRemap[light] = dark;
                    activeRemap[dark] = dark;
                }
                else
                {
                    activeRemap[dark] = light;
                    activeRemap[light] = light;
                }
            }
        }

        public static Color ToCurrent(Color color)
        {
            int mapped;
            if (activeRemap != null && activeRemap.TryGetValue(color.ToArgb(), out mapped))
            {
                return Color.FromArgb(mapped);
            }
            return color;
        }

        public static bool DetectSystemDark()
        {
            try
            {
                object val = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                if (val is int) return (int)val == 0;
            }
            catch { }
            return false;
        }
    }

    internal static class IconFonts
    {
        public const string Family = "Segoe MDL2 Assets";
        private static bool? available;
        private static readonly Dictionary<float, Font> cache = new Dictionary<float, Font>();

        public static bool IsAvailable
        {
            get
            {
                if (!available.HasValue)
                {
                    try
                    {
                        using (FontFamily ff = new FontFamily(Family))
                        {
                            available = true;
                        }
                    }
                    catch
                    {
                        available = false;
                    }
                }
                return available.Value;
            }
        }

        public static Font Get(float size)
        {
            Font f;
            if (cache.TryGetValue(size, out f)) return f;
            f = new Font(IsAvailable ? Family : "Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point);
            cache[size] = f;
            return f;
        }
    }

    public static class DrawingHelpers
    {
        public static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return path;
            }

            int maxD = Math.Min(rect.Width, rect.Height);
            int d = radius * 2;
            if (d > maxD) d = maxD;

            if (d <= 0 || radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            try
            {
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
            }
            catch
            {
                path.Reset();
                path.AddRectangle(rect);
            }
            return path;
        }
    }

    public class RoundedCard : Panel
    {
        public int CornerRadius { get; set; }
        public Color BorderColor { get; set; }

        public RoundedCard()
        {
            this.DoubleBuffered = true;
            CornerRadius = 8;
            BorderColor = Color.FromArgb(226, 232, 240);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (this.Width <= 1 || this.Height <= 1) return;

            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
                using (GraphicsPath path = DrawingHelpers.GetRoundedPath(rect, CornerRadius))
                {
                    if (path.PointCount > 0)
                    {
                        using (SolidBrush brush = new SolidBrush(this.BackColor))
                        {
                            e.Graphics.FillPath(brush, path);
                        }
                        if (BorderColor != Color.Empty && BorderColor != Color.Transparent)
                        {
                            using (Pen pen = new Pen(BorderColor, 1))
                            {
                                e.Graphics.DrawPath(pen, path);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback paint
            }
        }
    }

    public class ModernButton : Button
    {
        public int CornerRadius { get; set; }
        public Color BorderColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }

        // Segoe MDL2 Assets glyph rendered before the text, e.g. "\uE774"
        public string IconChar { get; set; }

        // Small status dot rendered before the text; Color.Empty disables it
        public Color DotColor { get; set; }

        private const int ContentGap = 6;
        private const int DotSize = 9;

        // Horizontal inset that keeps content clear of the rounded corners
        public int ContentPadding
        {
            get { return Math.Max(8, CornerRadius / 2 + 4); }
        }

        // Width required to render dot + icon + text plus side padding
        public int MeasureContentWidth()
        {
            string text = this.Text ?? string.Empty;
            string icon = (IconChar != null && IconFonts.IsAvailable) ? IconChar : null;
            bool hasDot = (DotColor != Color.Empty && DotColor != Color.Transparent);

            int contentW = 0;
            if (!string.IsNullOrEmpty(text))
            {
                contentW += TextRenderer.MeasureText(text, this.Font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            }
            if (icon != null)
            {
                Font iconFont = IconFonts.Get(this.Font.Size + 1.5f);
                contentW += TextRenderer.MeasureText(icon, iconFont, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + (contentW > 0 ? ContentGap : 0);
            }
            if (hasDot)
            {
                contentW += DotSize + (contentW > 0 ? ContentGap : 0);
            }
            return contentW + ContentPadding * 2;
        }

        private bool isHovered = false;
        private bool isPressed = false;

        public ModernButton()
        {
            this.SetStyle(ControlStyles.UserPaint |
                          ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.ResizeRedraw |
                          ControlStyles.SupportsTransparentBackColor, true);
            this.SetStyle(ControlStyles.Selectable, false);
            this.DoubleBuffered = true;
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 255, 255);
            this.Cursor = Cursors.Hand;
            CornerRadius = 6;
            BorderColor = Color.FromArgb(226, 232, 240);
            HoverColor = Color.FromArgb(241, 245, 249);
            PressedColor = Color.FromArgb(226, 232, 240);
            DotColor = Color.Empty;
            IconChar = null;
        }

        protected override bool ShowFocusCues
        {
            get { return false; }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            isPressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            isPressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            isPressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (this.Width <= 1 || this.Height <= 1) return;

            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                Color parentBg = this.Parent != null ? this.Parent.BackColor : SystemColors.Control;
                using (SolidBrush parentBrush = new SolidBrush(parentBg))
                {
                    e.Graphics.FillRectangle(parentBrush, this.ClientRectangle);
                }

                Color currentBg = !this.Enabled ? Theme.DisabledBg : this.BackColor;
                if (this.Enabled)
                {
                    if (isPressed && PressedColor != Color.Empty) currentBg = PressedColor;
                    else if (isHovered && HoverColor != Color.Empty) currentBg = HoverColor;
                }

                Color currentFore = !this.Enabled ? Theme.DisabledFg : this.ForeColor;
                Color currentBorder = !this.Enabled ? Theme.DisabledBorder : BorderColor;

                Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
                using (GraphicsPath path = DrawingHelpers.GetRoundedPath(rect, CornerRadius))
                {
                    if (path.PointCount > 0)
                    {
                        using (SolidBrush brush = new SolidBrush(currentBg))
                        {
                            e.Graphics.FillPath(brush, path);
                        }
                        if (currentBorder != Color.Empty && currentBorder != Color.Transparent)
                        {
                            using (Pen pen = new Pen(currentBorder, 1))
                            {
                                e.Graphics.DrawPath(pen, path);
                            }
                        }
                    }
                }

                string text = this.Text ?? string.Empty;
                string icon = (IconChar != null && IconFonts.IsAvailable) ? IconChar : null;
                bool hasDot = (DotColor != Color.Empty && DotColor != Color.Transparent);

                Size textSize = string.IsNullOrEmpty(text)
                    ? Size.Empty
                    : TextRenderer.MeasureText(text, this.Font, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

                Font iconFont = icon != null ? IconFonts.Get(this.Font.Size + 1.5f) : null;
                Size iconSize = Size.Empty;
                if (icon != null)
                {
                    iconSize = TextRenderer.MeasureText(icon, iconFont, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }

                int pad = ContentPadding;
                int availW = this.Width - pad * 2;
                int contentW = textSize.Width;
                if (icon != null) contentW += iconSize.Width + (textSize.Width > 0 ? ContentGap : 0);
                if (hasDot) contentW += DotSize + (contentW > 0 ? ContentGap : 0);
                if (contentW <= 0) return;

                int x = (contentW <= availW) ? pad + (availW - contentW) / 2 : pad;
                int cy = this.Height / 2;
                int rightEdge = this.Width - pad;

                if (hasDot)
                {
                    Color dot = this.Enabled ? DotColor : Theme.DisabledFg;
                    using (SolidBrush dotBrush = new SolidBrush(dot))
                    {
                        e.Graphics.FillEllipse(dotBrush, x, cy - DotSize / 2, DotSize, DotSize);
                    }
                    x += DotSize + ContentGap;
                }

                if (icon != null)
                {
                    Rectangle iconRect = new Rectangle(x, cy - iconSize.Height / 2, iconSize.Width, iconSize.Height);
                    TextRenderer.DrawText(e.Graphics, icon, iconFont, iconRect, currentFore,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                    x += iconSize.Width + ContentGap;
                }

                if (!string.IsNullOrEmpty(text))
                {
                    int textW = Math.Min(textSize.Width, rightEdge - x);
                    if (textW > 0)
                    {
                        Rectangle textRect = new Rectangle(x, cy - textSize.Height / 2, textW, textSize.Height);
                        TextRenderer.DrawText(e.Graphics, text, this.Font, textRect, currentFore,
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    }
                }
            }
            catch { }
        }
    }

    public class LoadingSpinner : Control
    {
        private readonly Timer timer;
        private float angle;

        public LoadingSpinner()
        {
            this.SetStyle(ControlStyles.UserPaint |
                          ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.SupportsTransparentBackColor, true);
            this.Size = new Size(16, 16);
            this.BackColor = Color.Transparent;
            this.ForeColor = Theme.Accent;
            this.Visible = false;

            timer = new Timer();
            timer.Interval = 70;
            timer.Tick += (s, e) =>
            {
                angle = (angle + 28f) % 360f;
                Invalidate();
            };
            this.VisibleChanged += (s, e) =>
            {
                if (this.Visible) timer.Start();
                else timer.Stop();
            };
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            try
            {
                Color parentBg = this.Parent != null ? this.Parent.BackColor : SystemColors.Control;
                using (SolidBrush b = new SolidBrush(parentBg))
                {
                    pevent.Graphics.FillRectangle(b, this.ClientRectangle);
                }
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(2, 2, this.Width - 5, this.Height - 5);
                if (r.Width <= 0 || r.Height <= 0) return;

                using (Pen track = new Pen(Color.FromArgb(60, this.ForeColor), 2f))
                {
                    e.Graphics.DrawArc(track, r, 0, 360);
                }
                using (Pen pen = new Pen(this.ForeColor, 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawArc(pen, r, angle, 105);
                }
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { timer.Stop(); timer.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}