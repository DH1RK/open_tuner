using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    // The look of the gauges and bars of the Expert and Special tabs: the blue of the BATC spectrum (like the panadapter
    // of SmartSDR). The spectrum runs from dark blue at the top to lighter blue at the bottom, the area below its curve is a
    // translucent grey. The instruments start at the colour in the middle of the spectrum and end with the colour of the
    // fill area as it shows on the spectrum's bottom colour.
    public static class InstrumentLook
    {
        public static readonly Color SpectrumTop = Color.FromArgb(255, 4, 16, 28);
        public static readonly Color SpectrumBottom = Color.FromArgb(255, 14, 70, 105);
        public static readonly Color SpectrumFill = Color.FromArgb(120, 205, 215, 222);

        public static readonly Color BackgroundTop = Blend(SpectrumTop, SpectrumBottom, 128);
        public static readonly Color BackgroundBottom = Blend(SpectrumBottom, SpectrumFill, SpectrumFill.A);
        public static readonly Color Text =Color.FromArgb(255, 240, 245, 250);

        // The colour of `over` (RGB) laid on `under` with `alpha` (0..255) of its own.
        private static Color Blend(Color under, Color over, int alpha)
        {
            return Color.FromArgb(255,
                (under.R * (255 - alpha) + over.R * alpha) / 255,
                (under.G * (255 - alpha) + over.G * alpha) / 255,
                (under.B * (255 - alpha) + over.B * alpha) / 255);
        }

        // Fills the control with the gradient that runs over the whole `group` (controls of the same parent, one gradient
        // from the top of the first to the bottom of the last), so stacked instruments do not each start a new gradient.
        public static void FillBackground(Graphics g, Control control, Control[] group)
        {
            Rectangle area = control.ClientRectangle;

            if (group != null && group.Length > 1 && control.Parent != null)
            {
                int top = int.MaxValue;
                int bottom = int.MinValue;
                foreach (Control member in group)
                {
                    if (member.Parent != control.Parent)
                        continue;

                    top = Math.Min(top, member.Top);
                    bottom = Math.Max(bottom, member.Bottom);
                }

                if (bottom > top)
                    area = new Rectangle(0, top - control.Top, control.Width, bottom - top);
            }

            if (area.Width <= 0 || area.Height <= 0)
                return;

            using (var background = new LinearGradientBrush(area, BackgroundTop, BackgroundBottom, LinearGradientMode.Vertical))
                g.FillRectangle(background, control.ClientRectangle);
        }

        // Fills the whole area (g.Clear for the gradient).
        public static void FillBackground(Graphics g, Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0)
                return;

            using (var background = new LinearGradientBrush(area, BackgroundTop, BackgroundBottom, LinearGradientMode.Vertical))
                g.FillRectangle(background, area);
        }
    }
}
