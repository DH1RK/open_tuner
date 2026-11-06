using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    // A number entered digit by digit, like the frequency and symbol rate fields of MiniTioune: the mouse wheel changes the digit under
    // the pointer (with carry), a click selects a digit, typing a digit replaces it and moves the selection to the right, the arrow keys
    // move (left / right) or change (up / down) the selected digit. The value is reported with ValueCommitted: after the wheel has rested,
    // on Enter and when the control loses the focus. Escape drops what was typed.
    public class DigitEntry : Control
    {
        private static readonly Color BackgroundColor = Color.FromArgb(255, 31, 31, 31);
        private static readonly Color BorderColor = Color.FromArgb(255, 63, 70, 76);
        private static readonly Color FocusBorderColor = Color.FromArgb(255, 255, 204, 128);
        private static readonly Color DigitColor = Color.FromArgb(255, 220, 220, 220);
        private static readonly Color EditedColor = Color.FromArgb(255, 255, 204, 128);
        private static readonly Color SelectionColor = Color.FromArgb(255, 51, 78, 102);

        private const int WheelDelayMs = 400;   // the wheel tunes once it has rested, not on every notch

        private readonly int _digits;
        private readonly long _maximum;
        private readonly Timer _wheel_timer = new Timer();
        private long _value = 0;          // what is shown
        private long _committed = 0;      // what was last reported or set from outside
        private int _selected = -1;       // index of the selected digit, 0 = leftmost, -1 = none
        private bool _edited = false;     // typed or changed by the wheel and not yet reported
        private int _digit_width = 14;
        private int _digit_height = 24;
        private int _left = 0;            // x of the first digit

        public event Action<long> ValueCommitted;

        // the user changed the shown value (wheel, typing, arrow keys), before it is committed: lets a second field follow
        public event Action<long> ValueEdited;

        public DigitEntry(int digits)
        {
            _digits = Math.Max(1, digits);
            _maximum = 1;
            for (int n = 0; n < _digits; n++)
                _maximum *= 10;
            _maximum -= 1;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Font = new Font("Consolas", 14f, FontStyle.Bold);
            _wheel_timer.Interval = WheelDelayMs;
            _wheel_timer.Tick += (s, e) => Commit();
            MeasureDigits();
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public long Value
        {
            get { return _value; }
        }

        // Shows a value from outside (the tuner's current setting). Ignored while the user is editing, so a status update does not
        // overwrite what is being typed.
        public void SetValue(long value)
        {
            value = Math.Max(0, Math.Min(_maximum, value));
            _committed = value;

            if (_edited)
                return;

            if (value != _value)
            {
                _value = value;
                Invalidate();
            }
        }

        // width of the control that fits the digits
        public int PreferredWidth
        {
            get { return _digits * _digit_width + 8; }
        }

        private void MeasureDigits()
        {
            Size size = TextRenderer.MeasureText("0", Font, new Size(100, 100), TextFormatFlags.NoPadding);
            _digit_width = size.Width + 6;
            _digit_height = size.Height + 6;
            Size = new Size(PreferredWidth, _digit_height + 6);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            MeasureDigits();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackgroundColor);
            _left = Math.Max(3, (Width - _digits * _digit_width) / 2);
            string text = _value.ToString().PadLeft(_digits, '0');
            int top = (Height - _digit_height) / 2;

            for (int n = 0; n < _digits; n++)
            {
                var rect = new Rectangle(_left + n * _digit_width, top, _digit_width, _digit_height);

                if (n == _selected && Focused)
                {
                    using (var brush = new SolidBrush(SelectionColor))
                        e.Graphics.FillRectangle(brush, rect);
                }

                TextRenderer.DrawText(e.Graphics, text[n].ToString(), Font, rect, _edited ? EditedColor : DigitColor,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            using (var pen = new Pen(Focused ? FocusBorderColor : BorderColor))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        // index of the digit at x, -1 outside of the digits
        private int DigitAt(int x)
        {
            int index = (x - _left) / _digit_width;
            return x >= _left && index >= 0 && index < _digits ? index : -1;
        }

        private long StepOf(int index)
        {
            long step = 1;
            for (int n = index + 1; n < _digits; n++)
                step *= 10;
            return step;
        }

        private void Change(long delta)
        {
            long value = Math.Max(0, Math.Min(_maximum, _value + delta));
            if (value == _value)
                return;

            _value = value;
            _edited = true;
            Invalidate();
            ValueEdited?.Invoke(_value);
        }

        private void Commit()
        {
            _wheel_timer.Stop();

            if (!_edited)
                return;

            _edited = false;
            Invalidate();

            if (_value != _committed)
            {
                _committed = _value;
                ValueCommitted?.Invoke(_value);
            }
        }

        private void Revert()
        {
            _wheel_timer.Stop();
            _edited = false;
            _value = _committed;
            Invalidate();
            ValueEdited?.Invoke(_value);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int index = DigitAt(e.X);

            if (index >= 0)
            {
                _selected = index;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // the digit under the pointer, or the selected one if the pointer is beside the digits
            int index = DigitAt(e.X);
            if (index < 0)
                index = _selected >= 0 ? _selected : _digits - 1;

            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;

            _selected = index;
            Change(e.Delta / 120 * StepOf(index));
            _wheel_timer.Stop();
            _wheel_timer.Start();
        }

        protected override bool IsInputKey(Keys key_data)
        {
            switch (key_data & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    return true;
            }

            return base.IsInputKey(key_data);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_selected < 0)
                _selected = _digits - 1;

            switch (e.KeyCode)
            {
                case Keys.Left:
                    _selected = Math.Max(0, _selected - 1);
                    break;
                case Keys.Right:
                    _selected = Math.Min(_digits - 1, _selected + 1);
                    break;
                case Keys.Up:
                    Change(StepOf(_selected));
                    break;
                case Keys.Down:
                    Change(-StepOf(_selected));
                    break;
                case Keys.Back:
                    _selected = Math.Max(0, _selected - 1);
                    SetDigit(_selected, 0);
                    break;
                case Keys.Enter:
                    Commit();
                    break;
                case Keys.Escape:
                    Revert();
                    break;
                default:
                    int digit = DigitOfKey(e.KeyCode);
                    if (digit < 0)
                        return;

                    SetDigit(_selected, digit);
                    _selected = Math.Min(_digits - 1, _selected + 1);
                    break;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            Invalidate();
        }

        private static int DigitOfKey(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return key - Keys.D0;
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return key - Keys.NumPad0;
            return -1;
        }

        // replaces one digit of the value
        private void SetDigit(int index, int digit)
        {
            long step = StepOf(index);
            int old = (int)(_value / step % 10);
            Change((digit - old) * step);
            _edited = true;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (_selected < 0)
                _selected = _digits - 1;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Commit();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _wheel_timer.Dispose();

            base.Dispose(disposing);
        }
    }
}
