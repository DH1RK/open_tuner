using System;
using System.Drawing;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    // Analogue dial instrument (270 degree sweep, needle) with a title and a value text underneath,
    // in the style of MiniTioune's gauges. The needle is set with SetValue(), which may be called from
    // any thread.
    public class GaugeControl : Control
    {
        private const float StartAngle = 135f; // GDI+ angles: 0 = east, clockwise; 135 = lower left
        private const float SweepAngle = 270f; // ... to 45 = lower right

        private readonly string _title;
        private readonly double _min;
        private readonly double _max;
        private readonly double _major_step;
        private readonly int _minor_per_major;
        private double _value;
        private string _text = "-";

        // optional coloured band along the outer edge of the scale (e.g. "below C/N needed")
        private bool _has_band = false;
        private double _band_from;
        private double _band_to;
        private Color _band_color = Color.Orange;

        public GaugeControl(string title, double min, double max, double major_step, int minor_per_major)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _title = title;
            _min = min;
            _max = max;
            _major_step = major_step;
            _minor_per_major = Math.Max(1, minor_per_major);
            _value = min;
            BackColor = Color.FromArgb(210, 240, 245);
            MinimumSize = new Size(80, 90);
        }

        public void SetValue(double value, string text)
        {
            value = Math.Max(_min, Math.Min(_max, value));

            if (value == _value && text == _text)
                return;

            _value = value;
            _text = text;

            if (!IsHandleCreated || IsDisposed)
                return;

            try
            {
                BeginInvoke((MethodInvoker)Invalidate);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }

        // Colours the outer scale from `from` to `to` (clamped to the scale); to <= from or NaN removes it.
        public void SetBand(double from, double to, Color color)
        {
            bool has_band = !double.IsNaN(from) && !double.IsNaN(to);
            if (has_band)
            {
                from = Math.Max(_min, Math.Min(_max, from));
                to = Math.Max(_min, Math.Min(_max, to));
                has_band = to > from;
            }

            if (has_band == _has_band && (!has_band || (from == _band_from && to == _band_to && color == _band_color)))
                return;

            _has_band = has_band;
            _band_from = from;
            _band_to = to;
            _band_color = color;

            if (!IsHandleCreated || IsDisposed)
                return;

            try
            {
                BeginInvoke((MethodInvoker)Invalidate);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }

        private PointF OnDial(float cx, float cy, float radius, double value)
        {
            double angle = (StartAngle + SweepAngle * (value - _min) / (_max - _min)) * Math.PI / 180.0;
            return new PointF(cx + radius * (float)Math.Cos(angle), cy + radius * (float)Math.Sin(angle));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            const int text_height = 34; // title + value text under the dial
            int side = Math.Min(Width - 6, Height - text_height - 4);
            if (side < 40)
                return;

            float cx = Width / 2f;
            float cy = 3 + side / 2f;
            float radius = side / 2f;

            using (var face = new SolidBrush(Color.White))
            using (var bezel = new Pen(Color.FromArgb(90, 90, 90), 2f))
            {
                g.FillEllipse(face, cx - radius, cy - radius, side, side);
                g.DrawEllipse(bezel, cx - radius, cy - radius, side, side);
            }

            if (_has_band)
            {
                // round strip just outside the tick marks
                float band_radius = radius * 0.94f;
                float band_width = radius * 0.08f;
                float start = StartAngle + (float)(SweepAngle * (_band_from - _min) / (_max - _min));
                float sweep = (float)(SweepAngle * (_band_to - _band_from) / (_max - _min));

                using (var band = new Pen(_band_color, band_width))
                {
                    band.StartCap = System.Drawing.Drawing2D.LineCap.Flat;
                    band.EndCap = System.Drawing.Drawing2D.LineCap.Flat;
                    g.DrawArc(band, cx - band_radius, cy - band_radius, band_radius * 2, band_radius * 2, start, sweep);
                }
            }

            using (var major = new Pen(Color.Black, 1.5f))
            using (var minor = new Pen(Color.FromArgb(90, 90, 90), 1f))
            using (var label_font = new Font("Microsoft Sans Serif", 6.5f))
            using (var label_brush = new SolidBrush(Color.Black))
            using (var center_format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                double minor_step = _major_step / _minor_per_major;
                int steps = (int)Math.Round((_max - _min) / minor_step);

                for (int n = 0; n <= steps; n++)
                {
                    double v = _min + n * minor_step;
                    bool is_major = n % _minor_per_major == 0;

                    g.DrawLine(is_major ? major : minor,
                               OnDial(cx, cy, radius * 0.88f, v),
                               OnDial(cx, cy, radius * (is_major ? 0.74f : 0.80f), v));

                    if (is_major)
                    {
                        PointF p = OnDial(cx, cy, radius * 0.60f, v);
                        g.DrawString(Math.Round(v).ToString(), label_font, label_brush, p, center_format);
                    }
                }
            }

            using (var needle = new Pen(Color.Red, 2f))
            using (var hub = new SolidBrush(Color.FromArgb(60, 60, 60)))
            {
                g.DrawLine(needle, new PointF(cx, cy), OnDial(cx, cy, radius * 0.80f, _value));
                g.FillEllipse(hub, cx - 4, cy - 4, 8, 8);
            }

            using (var title_font = new Font("Microsoft Sans Serif", 8f, FontStyle.Bold))
            using (var text_font = new Font("Microsoft Sans Serif", 8f))
            using (var brush = new SolidBrush(Color.Black))
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString(_title, title_font, brush, new RectangleF(0, Height - text_height + 2, Width, 16), format);
                g.DrawString(_text, text_font, brush, new RectangleF(0, Height - text_height + 17, Width, 16), format);
            }
        }
    }

    // Small round LED with a caption on its right. Colour and caption are set from any thread with Set().
    public class LedControl : Control
    {
        public static readonly Color OffColor = Color.FromArgb(70, 90, 70);

        private Color _color = OffColor;

        public LedControl(string caption)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = caption;
            Font = new Font("Microsoft Sans Serif", 8f); // same size as the values below the gauges
            Size = new Size(200, 18);
        }

        public void Set(Color color, string caption = null)
        {
            if (color == _color && (caption == null || caption == Text))
                return;

            _color = color;

            if (!IsHandleCreated || IsDisposed)
                return;

            try
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    if (caption != null)
                        Text = caption;
                    Invalidate();
                }));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int d = Math.Min(Height - 6, 10);
            using (var fill = new SolidBrush(_color))
            using (var rim = new Pen(Color.FromArgb(60, 60, 60)))
            {
                g.FillEllipse(fill, 3, (Height - d) / 2, d, d);
                g.DrawEllipse(rim, 3, (Height - d) / 2, d, d);
            }

            TextRenderer.DrawText(g, Text, Font, new Rectangle(d + 8, 0, Width - d - 8, Height), ForeColor,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
