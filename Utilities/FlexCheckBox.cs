using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace opentuner
{
    // CheckBox in the style of the FlexRadio controls (FlexSlider.png): a dark glossy box like an inactive button, a blue
    // one with a white check mark when it is checked. The text keeps the ForeColor of the control.
    public class FlexCheckBox : CheckBox
    {
        private const int BoxSize = 13;

        private bool _hover;

        public FlexCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            // the toggle look (a button with text) is drawn by Windows
            if (Appearance == Appearance.Button)
            {
                base.OnPaint(e);
                return;
            }

            var g = e.Graphics;
            ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var box = new RectangleF(2.5f, (Height - BoxSize) / 2f, BoxSize, BoxSize);
            bool on = CheckState != CheckState.Unchecked;
            int delta = !Enabled ? -10 : _hover ? 14 : 0;

            using (var path = new GraphicsPath())
            {
                path.AddRectangle(box);
                Color top = on ? Color.FromArgb(7, 147, 255) : Color.FromArgb(117, 117, 117);
                Color bottom = on ? Color.FromArgb(0, 73, 129) : Color.FromArgb(31, 31, 31);
                using (var fill = new LinearGradientBrush(new RectangleF(box.X, box.Y - 1, box.Width, box.Height + 2), Shade(top, delta), Shade(bottom, delta), LinearGradientMode.Vertical))
                    g.FillRectangle(fill, box);

                using (var border = new Pen(on ? Color.FromArgb(16, 56, 88) : Color.FromArgb(31, 31, 31)))
                    g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            }

            if (on)
            {
                Color mark = Enabled ? Color.White : Color.FromArgb(190, 190, 190);
                using (var pen = new Pen(mark, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    if (CheckState == CheckState.Indeterminate)
                        g.DrawLine(pen, box.X + 3.5f, box.Y + BoxSize / 2f, box.Right - 3.5f, box.Y + BoxSize / 2f);
                    else
                        g.DrawLines(pen, new[] { new PointF(box.X + 3.2f, box.Y + 7.2f), new PointF(box.X + 6f, box.Y + 10f), new PointF(box.Right - 3f, box.Y + 4f) });
                }
            }

            // like the Windows check box: one line, unless the control is high enough for several (then the text wraps)
            var text_bounds = new Rectangle(19, 0, Math.Max(1, Width - 19), Height);
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            if (Height >= Font.Height * 2)
                flags |= TextFormatFlags.WordBreak;

            // disabled: the text colour half way to the background, so it is readable on light and on dark
            Color back = Parent != null && Parent.BackColor.A != 0 ? Parent.BackColor : SystemColors.Control;
            Color text = Enabled ? ForeColor : Color.FromArgb((ForeColor.R + back.R) / 2, (ForeColor.G + back.G) / 2, (ForeColor.B + back.B) / 2);
            TextRenderer.DrawText(g, Text, Font, text_bounds, text, flags);

            if (Focused && ShowFocusCues)
            {
                using (var focus = new Pen(Color.FromArgb(160, 120, 180, 230)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(focus, box.X - 1.5f, box.Y - 1.5f, box.Width + 3, box.Height + 3);
            }
        }

        private static Color Shade(Color c, int delta)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, c.R + delta)), Math.Max(0, Math.Min(255, c.G + delta)), Math.Max(0, Math.Min(255, c.B + delta)));
        }
    }
}