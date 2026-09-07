using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ChatGPTAntiBanLauncher
{
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
        }

        protected override bool ShowFocusCues
        {
            get { return false; }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            if (this.Width <= 0 || this.Height <= 0) return;
            try
            {
                Color parentBg = this.Parent != null ? this.Parent.BackColor : SystemColors.Control;
                using (SolidBrush parentBrush = new SolidBrush(parentBg))
                {
                    pevent.Graphics.FillRectangle(parentBrush, this.ClientRectangle);
                }
            }
            catch { }
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

                Color currentBg = !this.Enabled ? Color.FromArgb(241, 245, 249) : this.BackColor;
                if (this.Enabled)
                {
                    if (isPressed && PressedColor != Color.Empty) currentBg = PressedColor;
                    else if (isHovered && HoverColor != Color.Empty) currentBg = HoverColor;
                }

                Color currentFore = !this.Enabled ? Color.FromArgb(148, 163, 184) : this.ForeColor;
                Color currentBorder = !this.Enabled ? Color.FromArgb(226, 232, 240) : BorderColor;

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

                TextRenderer.DrawText(e.Graphics, this.Text, this.Font, rect, currentFore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            catch { }
        }
    }
}
