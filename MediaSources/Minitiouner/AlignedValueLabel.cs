using System;
using System.Drawing;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    // A label whose text is "Caption:  value": the caption stands at the left, the value starts at ValueLeft, so the values of
    // several labels one below the other start at the same place (the longest caption of the column sets it).
    public class AlignedValueLabel : Label
    {
        private int _value_left;

        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int ValueLeft
        {
            get { return _value_left; }
            set { _value_left = value; Invalidate(); }
        }

        // ValueLeft from the longest of the captions (with their colon), in the font of this label
        public void AlignTo(params string[] captions)
        {
            int widest = 0;
            foreach (string caption in captions)
                widest = Math.Max(widest, TextRenderer.MeasureText(caption, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width);

            ValueLeft = widest + 8;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int colon = Text.IndexOf(':');
            if (colon < 0 || _value_left <= 0)
            {
                base.OnPaint(e);
                return;
            }

            string caption = Text.Substring(0, colon + 1);
            string value = Text.Substring(colon + 1).Trim();
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            TextRenderer.DrawText(e.Graphics, caption, Font, new Rectangle(0, 0, Width, Height), ForeColor, flags);
            TextRenderer.DrawText(e.Graphics, value, Font, new Rectangle(_value_left, 0, Math.Max(1, Width - _value_left), Height), ForeColor, flags | TextFormatFlags.EndEllipsis);
        }
    }
}