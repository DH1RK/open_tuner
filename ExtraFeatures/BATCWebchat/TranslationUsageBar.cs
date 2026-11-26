using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace opentuner.ExtraFeatures.BATCWebchat
{
    // A small bar for the share of Azure's free tier used this month, below "Translate" in the chat (#270): blue like the
    // check boxes, orange from 80 %, red from 95 %. The numbers are in its tooltip.
    public class TranslationUsageBar : Control
    {
        private const double OrangeFrom = 0.80;
        private const double RedFrom = 0.95;

        private double _share;

        public TranslationUsageBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        // 0 = nothing used, 1 = the whole limit (more is shown as full)
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double Share
        {
            get { return _share; }
            set
            {
                if (_share == value)
                    return;
                _share = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var frame = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var back = new SolidBrush(Color.FromArgb(31, 31, 31)))
                g.FillRectangle(back, ClientRectangle);

            int inner = Width - 2;
            if (_share > 0 && inner > 0 && Height > 2)
            {
                // a small share stays visible
                int width = Math.Max(2, (int)Math.Round(inner * Math.Min(1.0, _share)));
                var fill_bounds = new Rectangle(1, 1, width, Height - 2);

                Color top = _share >= RedFrom ? Color.FromArgb(255, 90, 90) : _share >= OrangeFrom ? Color.FromArgb(255, 180, 40) : Color.FromArgb(7, 147, 255);
                Color bottom = _share >= RedFrom ? Color.FromArgb(150, 20, 20) : _share >= OrangeFrom ? Color.FromArgb(170, 90, 0) : Color.FromArgb(0, 73, 129);
                using (var fill = new LinearGradientBrush(new Rectangle(1, 0, width, Height), top, bottom, LinearGradientMode.Vertical))
                    g.FillRectangle(fill, fill_bounds);
            }

            using (var border = new Pen(Color.FromArgb(117, 117, 117)))
                g.DrawRectangle(border, frame);
        }
    }
}
