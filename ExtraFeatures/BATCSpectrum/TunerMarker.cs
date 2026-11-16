using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using opentuner.MediaSources.Minitiouner;

namespace opentuner
{
    // The mark of the signal a receiver is tuned to in the BATC spectrum (#218): an area in the colour of the receiver (the colour of
    // its "RX n" label at the left edge) over the bandwidth of the signal, in the row of the receiver, and the number of the
    // receiver in a framed circle in it. With two receivers on one signal both marks show, each in its own row.
    internal static class TunerMarker
    {
        // the colour of a receiver (0 = RX 1)
        public static Color ColorOf(int tuner)
        {
            var colors = InstrumentLook.TunerColors;
            return colors[((tuner % colors.Length) + colors.Length) % colors.Length];
        }

        // the colour for the name above the signal: the colour of the receiver, lighter so it can be read on the dark spectrum
        public static Color NameColorOf(int tuner)
        {
            Color c = ColorOf(tuner);
            const int toward_white = 40;      // percent
            return Color.FromArgb(c.R + (255 - c.R) * toward_white / 100, c.G + (255 - c.G) * toward_white / 100, c.B + (255 - c.B) * toward_white / 100);
        }

        // the area is the block of the receiver: the width of the signal, the height of the row of the receiver. Drawn before the text of
        // the signals; the number (DrawNumber) after it, so it is not covered by the text.
        public static void DrawArea(Graphics g, int tuner, RectangleF area)
        {
            Color colour = ColorOf(tuner);
            area = Visible(area);
            // translucent, so the curve of the spectrum can be seen through it
            using (var fill = new SolidBrush(Color.FromArgb(100, colour)))
            using (var edge = new Pen(Color.FromArgb(220, colour), 1f))
            {
                g.FillRectangle(fill, area);
                g.DrawRectangle(edge, area.X, area.Y, area.Width, Math.Max(1, area.Height - 1));
            }
        }

        // the size of the circle with the number of a receiver
        public const float NumberDiameter = 20f;

        // the number of the receiver in a framed circle around this point (the place is worked out by the spectrum: in the lower third
        // below the text of the signals, receivers on the same signal side by side)
        public static void DrawNumber(Graphics g, int tuner, PointF centre)
        {
            Color colour = ColorOf(tuner);
            SmoothingMode old_smoothing = g.SmoothingMode;
            System.Drawing.Text.TextRenderingHint old_text = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            try
            {
                var circle = new RectangleF(centre.X - NumberDiameter / 2f, centre.Y - NumberDiameter / 2f, NumberDiameter, NumberDiameter);
                using (var disc = new SolidBrush(colour))
                using (var frame = new Pen(Color.White, 1.5f))
                using (var font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.FillEllipse(disc, circle);
                    g.DrawEllipse(frame, circle);
                    g.DrawString((tuner + 1).ToString(), font, Brushes.White, circle, format);
                }
            }
            finally
            {
                g.SmoothingMode = old_smoothing;
                g.TextRenderingHint = old_text;
            }
        }

        // a narrow signal (33 kS is a few pixels) stays visible
        private static RectangleF Visible(RectangleF area)
        {
            return area.Width < 3 ? new RectangleF(area.X + area.Width / 2 - 1.5f, area.Y, 3, area.Height) : area;
        }
    }
}
