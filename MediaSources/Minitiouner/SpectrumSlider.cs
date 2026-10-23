using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    // Slider in the style of the FlexRadio sliders (FlexSlider.png): a slim dark track, the part up to the knob filled with a
    // blue gradient, a round light grey knob. It has the members of a TrackBar that open_tuner uses (Value, Minimum,
    // Maximum, SmallChange, LargeChange, ValueChanged, mouse and keys) but draws itself: the knob of a TrackBar is drawn by
    // Windows and its colour cannot be set.
    public class SpectrumSlider : Control
    {
        private static readonly Color TrackColor = Color.FromArgb(255, 31, 31, 31);
        private static readonly Color TrackEdgeColor = Color.FromArgb(255, 48, 48, 48);
        private static readonly Color FillTopColor = Color.FromArgb(255, 51, 78, 102);
        private static readonly Color FillBottomColor = Color.FromArgb(255, 4, 39, 71);
        private static readonly Color KnobLightColor = Color.FromArgb(255, 207, 207, 207);
        private static readonly Color KnobDarkColor = Color.FromArgb(255, 171, 172, 174);
        private static readonly Color KnobRimColor = Color.FromArgb(255, 68, 68, 68);

        private int _minimum = 0;
        private int _maximum = 10;
        private int _value = 0;
        private int _small_change = 1;
        private int _large_change = 5;
        private bool _dragging = false;

        public event EventHandler ValueChanged;

        public SpectrumSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;   // the background is the one of the parent (a group box, a panel ...), not a colour of its own
            TabStop = true;
            Size = new Size(104, StandardHeight);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minimum
        {
            get { return _minimum; }
            set
            {
                _minimum = value;
                if (_maximum < _minimum)
                    _maximum = _minimum;
                Value = _value;
                Invalidate();
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = value;
                if (_minimum > _maximum)
                    _minimum = _maximum;
                Value = _value;
                Invalidate();
            }
        }

        // a value outside Minimum..Maximum is set to the nearest limit (a TrackBar throws)
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get { return _value; }
            set
            {
                value = Math.Max(_minimum, Math.Min(_maximum, value));
                if (value == _value)
                    return;

                _value = value;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SmallChange
        {
            get { return _small_change; }
            set { _small_change = Math.Max(1, value); }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int LargeChange
        {
            get { return _large_change; }
            set { _large_change = Math.Max(1, value); }
        }

        // the two settings of a TrackBar that the callers set; a SpectrumSlider has no tick marks
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public TickStyle TickStyle { get; set; } = TickStyle.None;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int TickFrequency { get; set; } = 1;

        // all sliders have the same size: between the old Volume slider (Properties) and the Baseband gain slider (Chip)
        public const int StandardHeight = 26;
        private const int StandardKnobDiameter = 13;

        private int KnobDiameter => Math.Max(10, Math.Min(StandardKnobDiameter, Height - 4));

        private void SetValueFromMouse(int x)
        {
            int d = KnobDiameter;
            float span = Width - d - 1;
            if (span <= 0 || _maximum == _minimum)
                return;

            float fraction = Math.Max(0f, Math.Min(1f, (x - d / 2f) / span));
            Value = _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left)
                return;

            Focus();
            _dragging = true;
            Capture = true;
            SetValueFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragging)
                SetValueFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging && e.Button == MouseButtons.Left)
            {
                _dragging = false;
                Capture = false;
            }

            base.OnMouseUp(e);
        }

        // Like a TrackBar: the handlers of MouseWheel may set Handled; otherwise one notch moves SmallChange times the
        // number of lines a notch scrolls in Windows.
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (e is HandledMouseEventArgs handled)
            {
                if (handled.Handled)
                    return;
                handled.Handled = true;
            }

            int notches = e.Delta / 120;
            Value += notches * _small_change * Math.Max(1, SystemInformation.MouseWheelScrollLines);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value -= _small_change; e.Handled = true; break;
                case Keys.Right: case Keys.Up: Value += _small_change; e.Handled = true; break;
                case Keys.PageDown: Value -= _large_change; e.Handled = true; break;
                case Keys.PageUp: Value += _large_change; e.Handled = true; break;
                case Keys.Home: Value = _minimum; e.Handled = true; break;
                case Keys.End: Value = _maximum; e.Handled = true; break;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int d = KnobDiameter;
            int track_height = 8;
            float track_left = d / 2f;
            float track_right = Width - d / 2f - 1;
            if (track_right <= track_left)
                return;

            float track_top = (Height - track_height) / 2f;
            float fraction = _maximum == _minimum ? 0f : (_value - _minimum) / (float)(_maximum - _minimum);
            float knob_x = track_left + fraction * (track_right - track_left);
            float knob_y = Height / 2f;

            var track = new RectangleF(track_left - d / 4f, track_top, track_right - track_left + d / 2f, track_height);

            using (var track_path = RoundedTrack(track))
            {
                using (var track_brush = new SolidBrush(TrackColor))
                    g.FillPath(track_brush, track_path);

                // the part up to the knob: blue gradient, lighter at the top
                g.SetClip(track_path);
                var fill = new RectangleF(track.X, track.Y, Math.Max(0f, knob_x - track.X), track.Height);
                if (fill.Width > 0)
                {
                    using (var fill_brush = new LinearGradientBrush(new RectangleF(fill.X, fill.Y - 1, fill.Width, fill.Height + 2),
                               Enabled ? FillTopColor : Color.FromArgb(255, 58, 66, 74), Enabled ? FillBottomColor : Color.FromArgb(255, 36, 42, 48), LinearGradientMode.Vertical))
                        g.FillRectangle(fill_brush, fill);
                }
                g.ResetClip();

                using (var edge = new Pen(TrackEdgeColor))
                    g.DrawPath(edge, track_path);
            }

            var knob = new RectangleF(knob_x - d / 2f, knob_y - d / 2f, d, d);
            using (var knob_brush = new LinearGradientBrush(new RectangleF(knob.X, knob.Y - 1, knob.Width, knob.Height + 2), Enabled ? KnobLightColor : KnobDarkColor, Enabled ? KnobDarkColor : KnobRimColor, LinearGradientMode.Vertical))
            using (var rim = new Pen(Focused && ShowFocusCues ? Color.FromArgb(255, 120, 180, 230) : KnobRimColor, 1.5f))
            using (var spoke = new Pen(Color.FromArgb(70, 120, 120, 124), 1f))
            {
                g.FillEllipse(knob_brush, knob);

                // the radial lines of the knob
                float cx = knob_x, cy = knob_y, r = d / 2f - 1.5f;
                for (int k = 0; k < 3; k++)
                {
                    double angle = (-90 + k * 120) * Math.PI / 180.0;
                    g.DrawLine(spoke, cx, cy, cx + (float)(r * Math.Cos(angle)), cy + (float)(r * Math.Sin(angle)));
                }

                g.DrawEllipse(rim, knob);
            }
        }

        private static GraphicsPath RoundedTrack(RectangleF r)
        {
            float radius = r.Height / 2f;
            float dia = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, dia, dia, 90, 180);
            path.AddArc(r.Right - dia, r.Y, dia, dia, 270, 180);
            path.CloseFigure();
            return path;
        }    }
}
