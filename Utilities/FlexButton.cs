using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace opentuner
{
    // Button in the style of the FlexRadio buttons (FlexSlider.png): a dark grey glossy button, a blue one when it is
    // active. A button is active when the code gives it a BackColor of its own (the media buttons are set to
    // Color.Transparent when they are off), or when Active is set.
    public class FlexButton : Button
    {
        private static readonly Color OffTop = Color.FromArgb(117, 117, 117);
        private static readonly Color OffBottom = Color.FromArgb(31, 31, 31);
        private static readonly Color OffBorder = Color.FromArgb(31, 31, 31);
        private static readonly Color OnTopA = Color.FromArgb(7, 147, 255);
        private static readonly Color OnTopB = Color.FromArgb(0, 129, 228);
        private static readonly Color OnBottomA = Color.FromArgb(0, 106, 187);
        private static readonly Color OnBottomB = Color.FromArgb(0, 73, 129);
        private static readonly Color OnBorder = Color.FromArgb(16, 56, 88);

        private bool _active;
        private bool _hover;
        private bool _pressed;

        public FlexButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            ForeColor = Color.White;
            UseVisualStyleBackColor = false;
        }

        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Active
        {
            get { return _active; }
            set { _active = value; _active_changes++; Invalidate(); }
        }

        private int _active_changes;

        // While the Click handler runs (a modal dialog, a long action) the button is blue; a handler that sets Active itself
        // (Connect, Source Settings ...) keeps what it set.
        protected override void OnClick(EventArgs e)
        {
            bool was_active = _active;
            int changes = _active_changes;

            _active = true;
            _pressed = false;
            Refresh();

            try
            {
                base.OnClick(e);
            }
            finally
            {
                if (_active_changes == changes && !IsDisposed)
                {
                    _active = was_active;
                    Invalidate();
                }
            }
        }

        // Only a colour set by the code counts, not the BackColor a button inherits from its parent (a dark panel).
        private bool _back_color_set;

        public override Color BackColor
        {
            get { return base.BackColor; }
            set
            {
                _back_color_set = value.A != 0 && value != SystemColors.Control && value != DefaultBackColor;
                base.BackColor = value;
            }
        }

        public override void ResetBackColor()
        {
            _back_color_set = false;
            base.ResetBackColor();
        }

        private bool IsActive
        {
            get { return _active || _back_color_set; }
        }

        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        internal static Color Shade(Color c, int delta)
        {
            return Color.FromArgb(c.A, Math.Max(0, Math.Min(255, c.R + delta)), Math.Max(0, Math.Min(255, c.G + delta)), Math.Max(0, Math.Min(255, c.B + delta)));
        }

        private static GraphicsPath RoundedRectangle(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // The face of a button (dark glossy, or blue when active) in `bounds`; also used for the tabs of the TabRowStrip.
        public static void DrawFace(Graphics g, RectangleF bounds, bool active, bool enabled, int delta)
        {
            using (GraphicsPath path = RoundedRectangle(bounds, 3f))
            {
                // one face with a gradient (blue: light at the top, dark at the bottom; dark button: grey the same way) and a
                // light spot at the top
                Color top_color = active ? OnTopA : OffTop;
                Color bottom_color = active ? OnBottomB : OffBottom;
                using (var back = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2), Shade(top_color, delta), Shade(bottom_color, delta / 2), LinearGradientMode.Vertical))
                    g.FillPath(back, path);

                var spot = new RectangleF(bounds.X + bounds.Width * 0.2f, bounds.Y, bounds.Width * 0.6f, bounds.Height * 0.45f);
                using (var spot_path = new GraphicsPath())
                {
                    spot_path.AddEllipse(spot);
                    using (var spot_brush = new PathGradientBrush(spot_path))
                    {
                        spot_brush.CenterColor = Color.FromArgb(enabled ? (active ? 60 : 70) : 30, 255, 255, 255);
                        spot_brush.SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) };
                        g.SetClip(path);
                        g.FillEllipse(spot_brush, spot);
                        g.ResetClip();
                    }
                }
                using (var border = new Pen(active ? OnBorder : OffBorder, 1f))
                    g.DrawPath(border, path);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // the corners show the colour of the parent
            Color parent_back = Parent != null ? Parent.BackColor : SystemColors.Control;
            g.Clear(parent_back.A == 0 ? SystemColors.Control : parent_back);

            var bounds = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            if (bounds.Width <= 2 || bounds.Height <= 2)
                return;

            bool active = IsActive;
            int delta = !Enabled ? -10 : _pressed ? (active ? 0 : -25) : _hover ? 14 : 0;

            DrawFace(g, bounds, active, Enabled, delta);

            if (Focused && ShowFocusCues)
            {
                using (var focus = new Pen(Color.FromArgb(200, 255, 255, 255)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(focus, 3, 3, Width - 7, Height - 7);
            }

            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), Enabled ? Color.White : Color.FromArgb(150, 150, 150),
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}