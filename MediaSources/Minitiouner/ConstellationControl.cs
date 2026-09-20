using System;
using System.Drawing;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    // I/Q constellation scope for one demodulator. The samples are the STV0910 ISYMB/QSYMB registers
    // (constellation editor tracks, 8 bit signed each), a handful per status poll. The last MaxPoints
    // samples are kept and drawn with the newest brightest, so the clusters build up over a second or
    // two. Full-scale is 64, 96 or 128 register units, chosen so that (almost) all samples fit.
    public class ConstellationControl : Control
    {
        private const int MaxPoints = 1024;
        private const int FadeLevels = 8;
        private const float PointSize = 1.5f; // pixels, drawn anti-aliased
        private static readonly float[] ScaleSteps = { 64f, 96f, 128f };

        private readonly sbyte[] _i = new sbyte[MaxPoints];
        private readonly sbyte[] _q = new sbyte[MaxPoints];
        private readonly object _lock = new object();
        private readonly SolidBrush[] _point_brushes = new SolidBrush[FadeLevels];
        private int _head = 0;
        private int _count = 0;
        private bool _locked = false;
        private float _scale = 64f; // full-scale of the plot in register units (one of ScaleSteps, see OnPaint)

        public ConstellationControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            ForeColor = Color.Gray;
            Height = 240;

            // dim green (oldest) -> bright green (newest)
            for (int n = 0; n < FadeLevels; n++)
            {
                int t = n * 255 / (FadeLevels - 1);
                _point_brushes[n] = new SolidBrush(Color.FromArgb(30 + t * 70 / 255, 70 + t * 185 / 255, 30 + t * 90 / 255));
            }
        }

        // data: [n, 0] = I, [n, 1] = Q as read from the registers (two's complement). Not locked or no
        // data clears the plot. Called from the NIM thread, so the repaint is marshalled to the UI thread.
        public void AddSamples(byte[,] data, bool locked)
        {
            lock (_lock)
            {
                if (!locked || data == null)
                {
                    if (!_locked && _count == 0)
                        return; // already empty, nothing to repaint

                    _locked = false;
                    _count = 0;
                    _head = 0;
                }
                else
                {
                    _locked = true;
                    for (int n = 0; n < data.GetLength(0); n++)
                    {
                        _i[_head] = unchecked((sbyte)data[n, 0]);
                        _q[_head] = unchecked((sbyte)data[n, 1]);
                        _head = (_head + 1) % MaxPoints;
                        if (_count < MaxPoints)
                            _count++;
                    }
                }
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

        // samples with |I| or |Q| beyond +-limit
        private static int CountOutside(sbyte[] i_data, sbyte[] q_data, float limit)
        {
            int outside = 0;
            for (int n = 0; n < i_data.Length; n++)
            {
                if (Math.Abs((int)i_data[n]) > limit || Math.Abs((int)q_data[n]) > limit)
                    outside++;
            }
            return outside;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            int side = Math.Min(Width, Height) - 8;
            if (side < 20)
                return;

            float cx = Width / 2f;
            float cy = Height / 2f;
            float half = side / 2f;

            using (var grid = new Pen(Color.FromArgb(60, 60, 60)))
            using (var axis = new Pen(Color.FromArgb(110, 110, 110)))
            {
                g.DrawRectangle(grid, cx - half, cy - half, side, side);
                g.DrawEllipse(grid, cx - half / 2, cy - half / 2, half, half);
                g.DrawLine(axis, cx - half, cy, cx + half, cy);
                g.DrawLine(axis, cx, cy - half, cx, cy + half);
            }

            sbyte[] i_copy;
            sbyte[] q_copy;
            int count;
            bool locked;
            lock (_lock)
            {
                count = _count;
                locked = _locked;
                i_copy = new sbyte[count];
                q_copy = new sbyte[count];

                int start = (_head - count + MaxPoints) % MaxPoints; // oldest sample
                for (int n = 0; n < count; n++)
                {
                    i_copy[n] = _i[(start + n) % MaxPoints];
                    q_copy[n] = _q[(start + n) % MaxPoints];
                }
            }

            using (var font = new Font("Microsoft Sans Serif", 8f))
            using (var text_brush = new SolidBrush(ForeColor))
            {
                if (!locked || count == 0)
                {
                    const string msg = "no signal";
                    var size = g.MeasureString(msg, font);
                    g.DrawString(msg, font, text_brush, cx - size.Width / 2, cy - size.Height / 2);
                    return;
                }

                // Fixed full-scale steps (64/96/128) instead of chasing the largest sample: the symbol
                // clusters sit at about +-38 (QPSK ring radius ~54), so 64 fills the plot the way
                // MiniTioune's does. A step is only left when more than 2 % of the samples fall outside
                // it, and only re-entered when at most 0.5 % would - no flapping between two steps.
                int step = Array.IndexOf(ScaleSteps, _scale);
                if (step < 0)
                    step = 0;
                while (step < ScaleSteps.Length - 1 && CountOutside(i_copy, q_copy, ScaleSteps[step]) > count * 0.02)
                    step++;
                while (step > 0 && CountOutside(i_copy, q_copy, ScaleSteps[step - 1]) <= count * 0.005)
                    step--;
                _scale = ScaleSteps[step];

                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float px_per_unit = half / _scale;
                for (int n = 0; n < count; n++)
                {
                    var brush = _point_brushes[n * (FadeLevels - 1) / Math.Max(1, count - 1)];
                    float x = Math.Max(cx - half, Math.Min(cx + half, cx + i_copy[n] * px_per_unit));
                    float y = Math.Max(cy - half, Math.Min(cy + half, cy - q_copy[n] * px_per_unit)); // Q up
                    g.FillRectangle(brush, x - PointSize / 2, y - PointSize / 2, PointSize, PointSize);
                }
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;

                g.DrawString("±" + Math.Round(_scale).ToString(), font, text_brush, cx - half + 2, cy - half + 2);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var brush in _point_brushes)
                    brush?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
