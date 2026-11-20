using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Header;
using Serilog;

namespace opentuner.Utilities
{
    public class DynamicPropertySlider : DynamicPropertyInterface
    {
        public delegate void SliderChanged(string key, int value);

        //private const int ItemHeight = 20;
        private opentuner.MediaSources.Minitiouner.SpectrumSlider _trackBar;

        private int _min;
        private int _max;

        public event SliderChanged OnSliderChanged;

        protected GroupBox _parent;
        protected string _key;
        protected string _title;
        protected string _value;
        protected Label _titleLabel;
        
        public override string Key
        {
            get { return _key; }
            set { _key = value; }
        }

        public override string LastValue => _value;

        // true: the slider is an audio balance (-100 left .. 0 centre .. +100 right, #257) and not a volume (0 .. 200 %)
        private readonly bool _balance;

        public DynamicPropertySlider(GroupBox Group, string Key, string Title, int Min, int Max, bool Balance = false)
        {
            _min = Min;
            _max = Max;
            _balance = Balance;

            InitComponents(Group, Key, Title);
        }

        private string TitleText()
        {
            if (!_balance)
                return _title + " " + _trackBar.Value.ToString() + " %";

            // centre: "Balance C", a side: "Balance L 10 %"
            return _title + " " + opentuner.MediaPlayers.AudioBalance.ToText(_trackBar.Value) + (_trackBar.Value == 0 ? "" : " %");
        }

        protected void InitComponents(GroupBox Group, string Key, string Title)
        {
            _parent = Group;
            _key = Key;
            _title = Title;

            _parent.Resize += _parent_Resize;

            // create controls
            _titleLabel = new Label();
            //_titleLabel.BorderStyle = BorderStyle.FixedSingle;
            _titleLabel.AutoSize = false;
            _titleLabel.Width = _parent.Width / 2 - 5;
            //_titleLabel.Height = ItemHeight;

            using (Graphics g = _titleLabel.CreateGraphics())
            {
                SizeF textSize = g.MeasureString("Hyq", _titleLabel.Font);
                _titleLabel.Height = 2 + (int)Math.Ceiling(textSize.Height);
            }

            _titleLabel.Left = 5;
            _titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            if (_parent.Controls.Count == 0)
                _titleLabel.Top = _titleLabel.Height + 5;
            else
                _titleLabel.Top = _parent.Controls[_parent.Controls.Count - 1].Top + _parent.Controls[_parent.Controls.Count - 1].Height + 5;

            _trackBar = new opentuner.MediaSources.Minitiouner.SpectrumSlider();
            _trackBar.AutoSize = false;
            _trackBar.Top = _titleLabel.Top;
            _trackBar.Height = opentuner.MediaSources.Minitiouner.SpectrumSlider.StandardHeight;
            _titleLabel.Height = _trackBar.Height;   // the text stays in the middle of the slider
            _trackBar.Width = _parent.Width - _trackBar.Left - 5;
            _trackBar.Minimum = _min;
            _trackBar.Maximum = _max;

            _trackBar.Left = _titleLabel.Left + _titleLabel.Width + 5;
            _trackBar.Width = SliderWidth();
            _trackBar.Name = _key;

            if (_balance)
            {
                _trackBar.FillFromCentre = true;
                _trackBar.Value = 0;
            }

            // set initial value
            _titleLabel.Text = TitleText();
            _trackBar.ValueChanged += _trackBar_ValueChanged;
            _trackBar.MouseUp += _trackBar_MouseUp;
            _trackBar.MouseWheel += _trackBar_MouseWheel;


            _parent.Controls.Add(_titleLabel);
            _parent.Controls.Add(_trackBar);
        }

        private void _trackBar_ValueChanged(object sender, EventArgs e)
        {
            OnSliderChanged?.Invoke(_key, _trackBar.Value);
            _titleLabel.Text = TitleText();
        }

        // One step of the volume scale per notch (#254), the same as the mouse wheel on the video: 1 % up to 20 %, then 5 %. The slider
        // moved SmallChange times the number of lines a notch scrolls in Windows before (6 % with the setting of 6 lines).
        private void _trackBar_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;

            if (_balance)
            {
                // balance: 5 per notch
                _trackBar.Value += e.Delta > 0 ? 5 : e.Delta < 0 ? -5 : 0;
                return;
            }

            if (e.Delta > 0)
                _trackBar.Value = VolumeSteps.Up(_trackBar.Value);
            else if (e.Delta < 0)
                _trackBar.Value = VolumeSteps.Down(_trackBar.Value);
        }

        // Right click resets the volume slider to 100% (all three sources use this only for
        // volume, range 0-200) - quicker than dragging back after boosting or muting.
        private void _trackBar_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // volume: 100 %, balance: centre
                _trackBar.Value = _balance ? 0 : Math.Max(_min, Math.Min(_max, 100));
            }
        }

        protected void _parent_Resize(object sender, EventArgs e)
        {
            _titleLabel.Width = _parent.Width / 2 - 5;
            _trackBar.Left = _titleLabel.Left + _titleLabel.Width + 5;
            _trackBar.Width = SliderWidth();
        }

        // Volume and balance slider are 2/3 as long as the space to the right of the title (it was all of it, too long)
        private int SliderWidth()
        {
            return Math.Max(40, (_parent.Width - _trackBar.Left - 5) * 2 / 3);
        }

        public override void UpdateValue(string Value)
        {
            int new_value = 0;

            _value = Value;

            if (Int32.TryParse(Value, out new_value))
            {
                //Log.Information("Update slider to " + new_value.ToString());
                _trackBar.Value = new_value;
            }
        }

        public override void UpdateColor(Color Col)
        {
            throw new NotImplementedException();
        }

        public override void UpdateMuteButtonColor(Color Col)
        {
            throw new NotImplementedException();
        }

        public override void UpdateRecordButtonColor(Color Col)
        {
            throw new NotImplementedException();
        }

        public override void UpdateStreamButtonColor(Color Col)
        {
            throw new NotImplementedException();
        }

        public override void ShowNotice(string text)
        {
            throw new NotImplementedException();
        }
    }
}
