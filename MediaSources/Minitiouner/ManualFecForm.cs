using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using opentuner.Utilities;

namespace opentuner.MediaSources.Minitiouner
{
    // The "Manual FEC" window of the Special tab (as in MiniTioune): modulation, frame length, pilots and the code rate of the modulation.
    // It stays open (not modal) until Close is pressed (or the owner closes it, e.g. when FEC mode goes back to Auto); every change of a radio button
    // is passed on at once (the callback), the chip setup is in ManualFec.
    public sealed class ManualFecForm : Form
    {
        private readonly RadioButton[] _modulation_radios = new RadioButton[ManualFec.ModulationCount];
        private readonly RadioButton _frame_long = new RadioButton();
        private readonly RadioButton _frame_short = new RadioButton();
        private readonly RadioButton _pilots_on = new RadioButton();
        private readonly RadioButton _pilots_off = new RadioButton();
        private readonly CustomGroupBox _rate_box = new CustomGroupBox();
        private readonly List<RadioButton> _rate_radios = new List<RadioButton>();
        private int _modulation;
        private int _rate;
        private bool _loading = true;
        private Action<ManualFec> _apply;

        public ManualFecForm(ManualFec start, Action<ManualFec> apply)
        {
            Text = "Manual FEC";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            _apply = apply;
            _modulation = start.Modulation;
            _rate = start.Rate;

            var modulation_box = new CustomGroupBox { Text = "Modulation", Location = new Point(10, 8), Size = new Size(130, 130) };
            for (int n = 0; n < ManualFec.ModulationCount; n++)
            {
                int modulation = n;
                var radio = new RadioButton { Text = ManualFec.Modulations[n], AutoSize = true, Location = new Point(12, 22 + n * 24) };
                radio.CheckedChanged += (s, e) =>
                {
                    if (radio.Checked && !_loading)
                    {
                        _modulation = modulation;
                        _rate = Math.Min(_rate, ManualFec.Rates(modulation).Length - 1);
                        BuildRates();
                        Changed();
                    }
                };
                _modulation_radios[n] = radio;
                modulation_box.Controls.Add(radio);
            }
            Controls.Add(modulation_box);

            var frame_box = new CustomGroupBox { Text = "Frame", Location = new Point(10, 146), Size = new Size(130, 80) };
            _frame_long.Text = "Long";
            _frame_short.Text = "Short";
            _frame_long.AutoSize = true;
            _frame_short.AutoSize = true;
            _frame_long.Location = new Point(12, 22);
            _frame_short.Location = new Point(12, 48);
            _frame_short.CheckedChanged += (s, e) => Changed();
            frame_box.Controls.Add(_frame_long);
            frame_box.Controls.Add(_frame_short);
            Controls.Add(frame_box);

            var pilots_box = new CustomGroupBox { Text = "Pilots", Location = new Point(10, 234), Size = new Size(130, 80) };
            _pilots_on.Text = "On";
            _pilots_off.Text = "Off";
            _pilots_on.AutoSize = true;
            _pilots_off.AutoSize = true;
            _pilots_on.Location = new Point(12, 22);
            _pilots_off.Location = new Point(12, 48);
            _pilots_on.CheckedChanged += (s, e) => Changed();
            pilots_box.Controls.Add(_pilots_on);
            pilots_box.Controls.Add(_pilots_off);
            Controls.Add(pilots_box);

            _rate_box.Location = new Point(150, 8);
            _rate_box.Size = new Size(140, 306);
            Controls.Add(_rate_box);

            var close = new FlexButton { Text = "Close", Location = new Point(215, 332), Size = new Size(75, 28) };
            ClientSize = new Size(300, 372);
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
            close.Click += (s, e) => Close();

            _modulation_radios[start.Modulation].Checked = true;
            (start.Short ? _frame_short : _frame_long).Checked = true;
            (start.Pilots ? _pilots_on : _pilots_off).Checked = true;
            BuildRates();
            _loading = false;

            DarkTheme.ApplyDialog(this);
        }

        // the code rates of the chosen modulation; 9/10 exists with long frames only
        private void BuildRates()
        {
            bool was_loading = _loading;
            _loading = true;

            _rate_box.Controls.Clear();
            _rate_radios.Clear();
            string[] rates = ManualFec.Rates(_modulation);
            _rate_box.Text = "FEC " + ManualFec.Modulations[_modulation];

            for (int n = 0; n < rates.Length; n++)
            {
                int rate = n;
                var radio = new RadioButton { Text = rates[n], AutoSize = true, Location = new Point(14, 22 + n * 24) };
                radio.Checked = n == _rate;
                radio.CheckedChanged += (s, e) =>
                {
                    if (radio.Checked && !_loading)
                    {
                        _rate = rate;
                        UpdateFrameChoice();
                        Changed();
                    }
                };
                _rate_radios.Add(radio);
                _rate_box.Controls.Add(radio);
            }

            UpdateFrameChoice();
            _loading = was_loading;
        }

        private void UpdateFrameChoice()
        {
            bool was_loading = _loading;
            _loading = true;
            bool long_only = ManualFec.IsNineTenth(_modulation, _rate);
            _frame_short.Enabled = !long_only;
            if (long_only)
                _frame_long.Checked = true;
            _loading = was_loading;
        }

        // a radio button was changed by the user: the choice is passed on
        private void Changed()
        {
            if (_loading)
                return;

            _apply?.Invoke(ManualFec.From(_modulation, _rate, _frame_short.Checked, _pilots_on.Checked));
        }
    }
}
