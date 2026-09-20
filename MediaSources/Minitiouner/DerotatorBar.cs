using System;
using System.Drawing;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    // Horizontal bar for the derotator (carrier frequency correction) of one demodulator: the scale is the
    // carrier search range CFRLOW..CFRUP, the marker is the current carrier offset (CFR). The outer 10 % of
    // the range on both sides are orange - the offset is close to the limit of what the demodulator may
    // correct (DSTATUS2.CFR_OVERFLOW is raised when it reaches it). Values are set from any thread.
    public class DerotatorBar : Control
    {
        private const double OuterZone = 0.10; // fraction of the range coloured as "close to the limit"

        private bool _valid = false;
        private double _offset_hz;
        private double _low_hz;
        private double _up_hz;

        public DerotatorBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 52;
            BackColor = Color.FromArgb(210, 240, 245);
        }

        public void SetValue(bool valid, double offset_hz, double low_hz, double up_hz)
        {
            if (valid == _valid && offset_hz == _offset_hz && low_hz == _low_hz && up_hz == _up_hz)
                return;

            _valid = valid;
            _offset_hz = offset_hz;
            _low_hz = low_hz;
            _up_hz = up_hz;

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

        private static string Khz(double hz, string format)
        {
            return (hz / 1000.0).ToString(format) + " kHz";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            const int margin = 10;
            int bar_top = 20;
            int bar_height = 14;
            float x0 = margin;
            float x1 = Width - margin;
            if (x1 - x0 < 40)
                return;

            using (var title_font = new Font("Microsoft Sans Serif", 8f, FontStyle.Bold))
            using (var text_font = new Font("Microsoft Sans Serif", 8f))
            using (var brush = new SolidBrush(Color.Black))
            using (var right = new StringFormat { Alignment = StringAlignment.Far })
            using (var centre = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString("Derotator", title_font, brush, x0, 3);

                bool have_range = _valid && _up_hz > _low_hz;

                // bar with the two "close to the limit" zones
                using (var back = new SolidBrush(Color.White))
                using (var zone = new SolidBrush(Color.Orange))
                using (var border = new Pen(Color.FromArgb(90, 90, 90)))
                {
                    g.FillRectangle(back, x0, bar_top, x1 - x0, bar_height);

                    if (have_range)
                    {
                        float zone_width = (float)((x1 - x0) * OuterZone);
                        g.FillRectangle(zone, x0, bar_top, zone_width, bar_height);
                        g.FillRectangle(zone, x1 - zone_width, bar_top, zone_width, bar_height);
                    }

                    g.DrawRectangle(border, x0, bar_top, x1 - x0, bar_height);
                }

                if (!have_range)
                {
                    g.DrawString("-", text_font, brush, new RectangleF(x0, 3, x1 - x0, 14), right);
                    return;
                }

                float ToX(double hz)
                {
                    double fraction = (hz - _low_hz) / (_up_hz - _low_hz);
                    return (float)(x0 + Math.Max(0, Math.Min(1, fraction)) * (x1 - x0));
                }

                // zero mark and range labels
                using (var zero = new Pen(Color.FromArgb(90, 90, 90)))
                {
                    float zero_x = ToX(0);
                    g.DrawLine(zero, zero_x, bar_top - 3, zero_x, bar_top + bar_height + 3);
                    g.DrawString("0", text_font, brush, new RectangleF(zero_x - 20, bar_top + bar_height + 2, 40, 14), centre);
                }

                g.DrawString(Khz(_low_hz, "N0"), text_font, brush, x0, bar_top + bar_height + 2);
                g.DrawString(Khz(_up_hz, "+0;-0;0"), text_font, brush, new RectangleF(x0, bar_top + bar_height + 2, x1 - x0, 14), right);

                // current offset: marker and value
                float marker_x = ToX(_offset_hz);
                using (var marker = new Pen(Color.Red, 2f))
                using (var triangle = new SolidBrush(Color.Red))
                {
                    g.DrawLine(marker, marker_x, bar_top - 2, marker_x, bar_top + bar_height + 2);
                    g.FillPolygon(triangle, new PointF[]
                    {
                        new PointF(marker_x - 4, bar_top - 6),
                        new PointF(marker_x + 4, bar_top - 6),
                        new PointF(marker_x, bar_top - 1)
                    });
                }

                g.DrawString(Khz(_offset_hz, "+0.0;-0.0;0.0"), text_font, brush, new RectangleF(x0, 3, x1 - x0, 14), right);
            }
        }
    }
}
