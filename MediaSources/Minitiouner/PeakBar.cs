using System;
using System.Drawing;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    // Small horizontal bar with a title, used for the LDPC iterations and LDPC errors of one demodulator (DVB-S2):
    // the bar is the current value, the red vertical marker is the highest value and trails behind like the marker
    // of the derotator bar: it is held for a few seconds and then follows the current maximum down again.
    // The scale starts at initial_scale and doubles until the value and the marker fit, and shrinks back the same
    // way. Values are set from any thread.
    public class PeakBar : Control
    {
        private const int PeakHoldMs = 3000;

        private readonly string _title;
        private readonly int _initial_scale;
        private readonly string _unit;
        private bool _valid = false;
        private int _value;
        private int _peak;
        private long _peak_time;
        private int _scale;

        public PeakBar(string title, int initial_scale, string unit = "")
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _title = title;
            _initial_scale = Math.Max(1, initial_scale);
            _unit = unit;
            _scale = _initial_scale;
            Height = 44;
            BackColor = Color.FromArgb(210, 240, 245);
        }

        // peak_hint: highest value since the last call if the hardware reports one, otherwise the value itself
        public void SetValue(bool valid, int value, int peak_hint)
        {
            long now = Environment.TickCount64;

            if (valid)
            {
                if (peak_hint >= _peak || now - _peak_time > PeakHoldMs)
                {
                    _peak = Math.Max(peak_hint, value);
                    _peak_time = now;
                }
            }
            else
            {
                _peak = 0;
                value = 0;
            }

            _scale = _initial_scale;
            while (Math.Max(value, _peak) > _scale && _scale < int.MaxValue / 2)
                _scale *= 2;

            _valid = valid;
            _value = value;

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

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            const int margin = 6;
            const int bar_top = 18;
            const int bar_height = 10;
            float x0 = margin;
            float x1 = Width - margin;
            if (x1 - x0 < 40)
                return;

            using (var title_font = new Font("Microsoft Sans Serif", 8f, FontStyle.Bold))
            using (var text_font = new Font("Microsoft Sans Serif", 8f))
            using (var brush = new SolidBrush(Color.Black))
            using (var right = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.DrawString(_title, title_font, brush, x0, 2);

                using (var back = new SolidBrush(Color.White))
                using (var fill = new SolidBrush(Color.FromArgb(70, 130, 180)))
                using (var border = new Pen(Color.FromArgb(90, 90, 90)))
                {
                    g.FillRectangle(back, x0, bar_top, x1 - x0, bar_height);

                    if (_valid)
                    {
                        float width = (float)((x1 - x0) * Math.Min(1.0, _value / (double)_scale));
                        g.FillRectangle(fill, x0, bar_top, width, bar_height);
                    }

                    g.DrawRectangle(border, x0, bar_top, x1 - x0, bar_height);
                }

                g.DrawString("0", text_font, brush, x0, bar_top + bar_height + 1);
                g.DrawString(_scale.ToString(), text_font, brush, new RectangleF(x0, bar_top + bar_height + 1, x1 - x0, 14), right);

                if (!_valid)
                {
                    g.DrawString("-", text_font, brush, new RectangleF(x0, 2, x1 - x0, 14), right);
                    return;
                }

                float peak_x = (float)(x0 + Math.Min(1.0, _peak / (double)_scale) * (x1 - x0));
                using (var marker = new Pen(Color.Red, 2f))
                    g.DrawLine(marker, peak_x, bar_top - 3, peak_x, bar_top + bar_height + 3);

                g.DrawString(_value + _unit + " (max " + _peak + _unit + ")", text_font, brush, new RectangleF(x0, 2, x1 - x0, 14), right);
            }
        }
    }
}
