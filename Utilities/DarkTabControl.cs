using System.Drawing;
using System.Windows.Forms;

namespace opentuner
{
    // TabControl whose own 3D border (the light frame around the page) is painted in the dark grey of the form frame,
    // so no separation to the rest of the window is seen.
    public class DarkTabControl : TabControl
    {
        private const int WM_PAINT = 0x000F;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_PAINT && IsHandleCreated)
                PaintFrame();
        }

        private void PaintFrame()
        {
            Rectangle outer = ClientRectangle;
            Rectangle page = DisplayRectangle;

            using (Graphics g = CreateGraphics())
            using (var brush = new SolidBrush(DarkTheme.Frame))
            {
                g.FillRectangle(brush, outer.Left, outer.Top, outer.Width, page.Top - outer.Top);
                g.FillRectangle(brush, outer.Left, page.Bottom, outer.Width, outer.Bottom - page.Bottom);
                g.FillRectangle(brush, outer.Left, page.Top, page.Left - outer.Left, page.Height);
                g.FillRectangle(brush, page.Right, page.Top, outer.Right - page.Right, page.Height);
            }
        }
    }
}