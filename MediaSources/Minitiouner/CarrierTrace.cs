using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    // Carrier frequency of one demodulator over time, like the frequency display of MiniTioune: the horizontal
    // axis is the offset of the carrier (CFR register) from the tuned frequency, the vertical axis is time with the
    // newest sample at the top, the history scrolls down (about 30 s). While the demodulator searches the line zigzags over the
    // search range, once it has locked it becomes a straight line at the carrier offset. It also runs without a
    // lock, so a signal that does not lock can still be seen. The horizontal scale follows the samples on show
    // (never wider than the search range), so a small deviation stays visible. Values are set from any thread.
    public class CarrierTrace : Control
    {
        private const int MaxSamples = 120;          // about 30 s at one status poll every 250 ms (a sample is 2 px tall)
        private const double MinHalfRangeHz = 5000;  // never zoom in further than +-5 kHz
        private const double DefaultRangeHz = 100000;

        private readonly List<double> _offsets = new List<double>();
        private readonly List<bool> _locked = new List<bool>();
        private readonly object _lock = new object();
        private double _range_hz = DefaultRangeHz;

        public CarrierTrace()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 140;
            BackColor = Color.FromArgb(210, 240, 245);
        }

        // offset_hz: carrier offset (CFR), low_hz / up_hz: the search range CFRLOW / CFRUP (0 = unknown)
        public void SetValue(int offset_hz, int low_hz, int up_hz, bool locked)
        {
            lock (_lock)
            {
                _offsets.Add(offset_hz);
                _locked.Add(locked);
                if (_offsets.Count > MaxSamples)
                {
                    _offsets.RemoveAt(0);
                    _locked.RemoveAt(0);
                }

                if (up_hz > low_hz)
                    _range_hz = Math.Max(Math.Abs((double)low_hz), Math.Abs((double)up_hz));
            }

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

            const int margin = 10;
            const int title_height = 18;
            const int label_height = 16;
            float x0 = margin;
            float x1 = Width - margin;
            float top = title_height;
            float bottom = Height - label_height - 2;
            if (x1 - x0 < 60 || bottom - top < 30)
                return;

            double[] offsets;
            bool[] locked;
            double range;
            lock (_lock)
            {
                offsets = _offsets.ToArray();
                locked = _locked.ToArray();
                range = _range_hz;
            }

            // horizontal scale: the largest deviation on show plus some room, between +-5 kHz and the search range
            double largest = 0;
            foreach (double o in offsets)
                largest = Math.Max(largest, Math.Abs(o));
            double half = Math.Max(MinHalfRangeHz, Math.Min(range, largest * 1.3));

            float centre = (x0 + x1) / 2;
            float ToX(double hz) => (float)(centre + Math.Max(-1, Math.Min(1, hz / half)) * (x1 - x0) / 2);

            using (var title_font = new Font("Microsoft Sans Serif", 8f, FontStyle.Bold))
            using (var text_font = new Font("Microsoft Sans Serif", 8f))
            using (var brush = new SolidBrush(Color.Black))
            using (var right = new StringFormat { Alignment = StringAlignment.Far })
            using (var middle = new StringFormat { Alignment = StringAlignment.Center })
            using (var back = new SolidBrush(Color.Black))
            using (var border = new Pen(Color.FromArgb(90, 90, 90)))
            using (var grid = new Pen(Color.FromArgb(60, 60, 60)))
            using (var zero = new Pen(Color.FromArgb(120, 120, 120)))
            {
                g.DrawString("Carrier frequency (CFR)", title_font, brush, x0, 2);

                g.FillRectangle(back, x0, top, x1 - x0, bottom - top);
                for (int i = 1; i < 4; i++)
                {
                    float gx = x0 + (x1 - x0) * i / 4f;
                    g.DrawLine(i == 2 ? zero : grid, gx, top, gx, bottom);
                }
                g.DrawRectangle(border, x0, top, x1 - x0, bottom - top);

                // horizontal axis labels: the visible range and the centre
                string edge = (half >= 1000) ? (half / 1000).ToString("0.#") + " kHz" : half.ToString("0") + " Hz";
                g.DrawString("-" + edge, text_font, brush, x0, bottom + 1);
                g.DrawString("+" + edge, text_font, brush, new RectangleF(x0, bottom + 1, x1 - x0, label_height), right);
                g.DrawString("0", text_font, brush, new RectangleF(x0, bottom + 1, x1 - x0, label_height), middle);

                if (offsets.Length == 0)
                    return;

                // newest sample on top, older ones below
                float step = (bottom - top - 2) / (MaxSamples - 1);
                float ToY(int index) => top + 1 + (offsets.Length - 1 - index) * step;

                using (var searching = new Pen(Color.Yellow, 1.5f))
                using (var found = new Pen(Color.LimeGreen, 1.5f))
                {
                    for (int i = 1; i < offsets.Length; i++)
                        g.DrawLine(locked[i] ? found : searching, ToX(offsets[i - 1]), ToY(i - 1), ToX(offsets[i]), ToY(i));
                }

                double now = offsets[offsets.Length - 1];
                g.DrawString((now / 1000.0).ToString("+0.00;-0.00;0.00") + " kHz" + (locked[locked.Length - 1] ? "" : "  searching"),
                             text_font, brush, new RectangleF(x0, 2, x1 - x0, 14), right);
            }
        }
    }
}
