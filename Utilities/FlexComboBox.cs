using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace opentuner
{
    // ComboBox in the style of the FlexRadio drop-downs (FlexSlider.png): very dark blue box, thin light grey border, white
    // text and a white triangle. A drop-down list is drawn completely here; a ComboBox that can be edited keeps its own
    // edit field and only gets the colours.
    public class FlexComboBox : ComboBox
    {
        private static readonly Color BoxColor = Color.FromArgb(7, 18, 25);
        private static readonly Color BorderColor = Color.FromArgb(146, 151, 153);
        private static readonly Color SelectedColor = Color.FromArgb(0, 102, 181);

        private const int WM_PAINT = 0x000F;

        public FlexComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = BoxColor;
            ForeColor = Color.White;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var back = new SolidBrush(selected ? SelectedColor : BoxColor))
                e.Graphics.FillRectangle(back, e.Bounds);

            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, e.Bounds, Enabled ? Color.White : Color.Gray,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_PAINT && DropDownStyle == ComboBoxStyle.DropDownList && IsHandleCreated)
                PaintBox();
        }

        private void PaintBox()
        {
            using (Graphics g = CreateGraphics())
            {
                var box = new Rectangle(0, 0, Width, Height);
                using (var back = new SolidBrush(BoxColor))
                    g.FillRectangle(back, box);

                using (var border = new Pen(BorderColor))
                    g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

                Color text_color = Enabled ? Color.White : Color.Gray;
                TextRenderer.DrawText(g, SelectedIndex >= 0 ? GetItemText(SelectedItem) : "", Font,
                                      new Rectangle(3, 1, Width - 22, Height - 2), text_color,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                float cx = Width - 11, cy = Height / 2f;
                using (var arrow = new SolidBrush(text_color))
                    g.FillPolygon(arrow, new[] { new PointF(cx - 4.5f, cy - 2.5f), new PointF(cx + 4.5f, cy - 2.5f), new PointF(cx, cy + 3f) });
            }
        }
    }
}