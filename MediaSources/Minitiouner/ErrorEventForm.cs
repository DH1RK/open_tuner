using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using opentuner.Utilities;

namespace opentuner.MediaSources.Minitiouner
{
    // The "Error/Event" window of the Special tab (as in MiniTioune): what error counter 1 of the demodulator counts (ERRCTRL1.ERR_SOURCE1, the event type)
    // and how it counts (ERRCTRL1.NUM_EVENT1, "Nbr of Event"), with the value read from ERRCNT12..10 (24 bits, bit 23 = "old value", 23 bits of count),
    // and "TS error" On / Off (TSCFGH.TSFIFO_ERRMODE). All as MiniTioune writes them (I2C capture ErrorEventDialog.txt).
    // Not modal, every change of a radio button is passed on at once (the callback), Close closes the window.
    public sealed class ErrorEventForm : Form
    {
        // ERR_SOURCE2 (data sheet, ERRCTRLx): the name, the counting unit and the decimal point of the rate modes (value = register / 2^point).
        // The sources without a name in MiniTioune's list are the DVB-S ones (Viterbi / Reed-Solomon).
        public sealed class Source
        {
            public string Name;
            public string Unit;      // what is counted: "bytes", "packets", "frames", "clocks / 4096", "clocks / 512"
            public int Point;
            public Source(string name, string unit, int point) { Name = name; Unit = unit; Point = point; }
        }

        public static readonly Source[] Sources =
        {
            new Source("Demod bit errors (DVB-S)", "bytes", 19),
            new Source("LDPC errors (DVB-S2)", "frames", 7),
            new Source("BCH errors", "frames", 19),
            new Source("BCH error flag", "frames", 23),
            new Source("Frame rate LDPC", "clocks / 4096", 23),
            new Source("BB header error", "frames", 23),
            new Source("Packet errors PER (CRC-8)", "packets", 23),
            new Source("Bit errors Reed-Solomon (DVB-S)", "bytes", 23),
            new Source("Byte errors Reed-Solomon (DVB-S)", "bytes", 23),
            new Source("Packet errors (DVB-S)", "packets", 23),
            new Source("Packet error number (DVB-S)", "packets", 19),
            new Source("Bad DFL (DVB-S2)", "frames", 23),
            new Source("TS error count", "packets", 23),
            new Source("TS FIFO", "clocks / 4096", 7),
            new Source("Frame7 DFL", "frames", 7),
            new Source("Packet bitrate", "clocks / 512", 23),
        };

        // ERRCTRL2.NUM_EVENT2: 0 and 1 count, 2 to 6 give a rate over 2^14 ... 2^22 bytes (2^8 ... 2^16 packets, 2^4 ... 2^12 frames), 7 the average
        public static readonly string[] Modes =
        {
            "count with reset", "count without reset", "2^14", "2^16", "2^18", "2^20", "2^22", "average",
        };

        private readonly RadioButton[] _source_radios = new RadioButton[Sources.Length];
        private readonly RadioButton[] _mode_radios = new RadioButton[Modes.Length];
        private readonly Label _value = new Label();
        private readonly RadioButton _ts_on = new RadioButton();
        private readonly RadioButton _ts_off = new RadioButton();
        private readonly Action<int, int, bool> _apply;
        private bool _loading = true;
        private int _source;
        private int _mode;
        private uint _raw;
        private bool _has_value;
        private double _mclk_hz;

        public ErrorEventForm(int source, int mode, bool ts_error_on, Action<int, int, bool> apply)
        {
            Text = "Error/Event";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(470, 552);

            _apply = apply;
            _source = Math.Max(0, Math.Min(Sources.Length - 1, source));
            _mode = Math.Max(0, Math.Min(Modes.Length - 1, mode));

            var source_box = new CustomGroupBox { Text = "Error/Event type", Location = new Point(10, 8), Size = new Size(250, 16 * 24 + 32) };
            for (int n = 0; n < Sources.Length; n++)
            {
                int index = n;
                var radio = new RadioButton { Text = Sources[n].Name, AutoSize = true, Location = new Point(12, 22 + n * 24) };
                radio.CheckedChanged += (s, e) =>
                {
                    if (radio.Checked && !_loading)
                    {
                        _source = index;
                        Changed();
                    }
                };
                _source_radios[n] = radio;
                source_box.Controls.Add(radio);
            }
            Controls.Add(source_box);

            var mode_box = new CustomGroupBox { Text = "Error counter, Nbr of event", Location = new Point(270, 8), Size = new Size(190, 8 * 24 + 32) };
            for (int n = 0; n < Modes.Length; n++)
            {
                int index = n;
                var radio = new RadioButton { Text = Modes[n], AutoSize = true, Location = new Point(12, 22 + n * 24) };
                radio.CheckedChanged += (s, e) =>
                {
                    if (radio.Checked && !_loading)
                    {
                        _mode = index;
                        Changed();
                    }
                };
                _mode_radios[n] = radio;
                mode_box.Controls.Add(radio);
            }
            Controls.Add(mode_box);

            var ts_box = new CustomGroupBox { Text = "TS error", Location = new Point(270, 8 + 8 * 24 + 40), Size = new Size(90, 80) };
            _ts_on.Text = "On";
            _ts_off.Text = "Off";
            _ts_on.AutoSize = true;
            _ts_off.AutoSize = true;
            _ts_on.Location = new Point(12, 22);
            _ts_off.Location = new Point(12, 48);
            _ts_off.CheckedChanged += (s, e) => Changed(false);
            ts_box.Controls.Add(_ts_on);
            ts_box.Controls.Add(_ts_off);
            Controls.Add(ts_box);

            var value_box = new CustomGroupBox { Text = "Value", Location = new Point(10, 8 + 16 * 24 + 40), Size = new Size(450, 68) };
            _value.Location = new Point(12, 24);
            _value.Size = new Size(430, 34);
            _value.Text = "-";
            value_box.Controls.Add(_value);
            Controls.Add(value_box);

            var close = new FlexButton { Text = "Close", Location = new Point(385, 514), Size = new Size(75, 28) };
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
            close.Click += (s, e) => Close();

            _source_radios[_source].Checked = true;
            _mode_radios[_mode].Checked = true;
            (ts_error_on ? _ts_on : _ts_off).Checked = true;
            _loading = false;

            DarkTheme.ApplyDialog(this);
            ShowValue();
        }

        private void Changed(bool counter_changed = true)
        {
            if (_loading)
                return;

            if (counter_changed)
            {
                _has_value = false;
                ShowValue();
            }

            _apply?.Invoke(_source, _mode, _ts_on.Checked);
        }

        // The counter read from the chip (any thread). valid = false: the chip says the value was read before (old value), the last one stays.
        public void SetValue(uint raw, bool valid, double mclk_hz)
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetValue(raw, valid, mclk_hz)));
                return;
            }

            _mclk_hz = mclk_hz;
            if (valid)
            {
                _raw = raw;
                _has_value = true;
            }

            ShowValue();
        }

        // Nothing to show: the tuner is not locked (any thread).
        public void ClearValue()
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(ClearValue));
                return;
            }

            _has_value = false;
            ShowValue();
        }

        private void ShowValue()
        {
            _value.Text = _has_value ? Format(_source, _mode, _raw, _mclk_hz) : "- (no lock)";
        }

        // The register value in a readable unit: a count as it is, a rate as register / 2^point per counting unit; the frame rate of the LDPC decoder and
        // the packet bitrate (rate modes) are converted with the master clock to frames / s and kbit/s.
        public static string Format(int source, int mode, uint raw, double mclk_hz)
        {
            Source info = Sources[Math.Max(0, Math.Min(Sources.Length - 1, source))];
            var ci = CultureInfo.InvariantCulture;

            if (mode <= 1)
                return raw.ToString("N0", ci) + " " + info.Unit + "\n(raw " + raw + ")";

            double ratio = raw / Math.Pow(2, info.Point);

            if (mclk_hz > 0 && source == 4)
                return (ratio * mclk_hz / 4096.0).ToString("N1", ci) + " frames/s\n(" + ratio.ToString("G4", ci) + " per 4096 clocks)";

            if (mclk_hz > 0 && source == 15)
                return (ratio * mclk_hz / 512.0 * 188.0 * 8.0 / 1000.0).ToString("N0", ci) + " kbit/s\n(" + ratio.ToString("G4", ci) + " packets per 512 clocks)";

            return ratio.ToString("G4", ci) + " per " + info.Unit.TrimEnd('s') + "\n(raw " + raw + ")";
        }
    }
}
