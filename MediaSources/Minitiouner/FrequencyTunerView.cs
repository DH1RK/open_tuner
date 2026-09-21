using System;
using System.Drawing;
using System.Windows.Forms;
using opentuner.Utilities;
using Serilog;

namespace opentuner.MediaSources.Minitiouner
{
    // One tuner's group on the "Frequency" tab: how far the demodulator's derotator had to pull the
    // carrier (derotator bar over the search range CFRLOW..CFRUP), the measured symbol rate and the two
    // tuning trims: the capture range of the derotator and a frequency correction for the tuner.
    public class FrequencyTunerView
    {
        // capture range steps in kHz on each side of the tuned frequency, 0 = automatic (1.5 x symbol rate)
        private static readonly uint[] CaptureSteps = { 0, 100, 200, 300, 500, 750, 1000, 1500, 2000, 3000, 5000 };
        private const int MaxCorrectionPpm = 250;        // slider range of the reference error correction in ppm
        private const int UnitsPerPpm = 10;              // slider unit = 0.1 ppm
        private const int ApplyDelayMs = 600; // wait for the slider to rest before tuning again

        // Sign of the carrier offset (CFR) relative to the correction that removes it: the tuner is moved
        // by +CFR so that the derotator has nothing left to do. Flip to -1 if "Adopt CFR" makes it worse.
        private const int CfrSign = 1;

        private readonly CustomGroupBox _group;
        private readonly DerotatorBar _derotator = new DerotatorBar();
        private readonly CarrierTrace _trace = new CarrierTrace();
        private readonly Label _found_label = new Label();
        private readonly Label _carrier_offset_label = new Label();
        private readonly Label _symbol_rate_label = new Label();

        private readonly TrackBar _capture_bar = new TrackBar();
        private readonly Label _capture_label = new Label();
        private readonly TrackBar _correction_bar = new TrackBar();
        private readonly Label _correction_label = new Label();
        private readonly Timer _apply_timer = new Timer();
        private bool _loading = false;
        private int _last_carrier_offset_hz = 0;
        private int _cfr_sign = CfrSign;                 // turned round by AdoptCarrierOffset if the offset grows after an adopt
        private double _adopt_cfr_hz = double.NaN;         // carrier offset at the last adopt
        private long _adopt_time = 0;
        private long _last_log = 0;
        private double _if_khz = 0;       // tuner frequency, for the kHz equivalent of the correction and for Adopt CFR
        private bool _last_locked = false;

        // symbol rates offered as buttons at the top of the group (kS), narrow ones first
        private static readonly uint[] RateButtons = { 20, 25, 33, 66, 125, 250, 333, 500, 1000, 1500, 2000 };
        private readonly System.Collections.Generic.List<Button> _rate_buttons = new System.Collections.Generic.List<Button>();

        // button caption: 1000 / 1500 / 2000 kS as 1k / 1k5 / 2k so the buttons can stay narrow
        private static string RateText(uint rate)
        {
            if (rate >= 1000)
                return rate % 1000 == 0 ? (rate / 1000) + "k" : (rate / 1000) + "k" + ((rate % 1000) / 100);

            return rate.ToString();
        }

        // a rate button was clicked (kS)
        public event Action<uint> SymbolRateSelected;

        // capture range in kHz (0 = automatic) and correction in ppm, fired once the sliders rest
        public event Action<uint, double> TrimChanged;

        public FrequencyTunerView(string title, Control parent)
        {
            _group = new CustomGroupBox();
            _group.Dock = DockStyle.Top;
            _group.Height = 510;
            _group.Text = title;
            _group.Font = new Font("Microsoft Sans Serif", 9.75F, FontStyle.Regular, GraphicsUnit.Point, (byte)0);
            _group.Padding = new Padding(8, 20, 8, 8);

            // Docking is evaluated last-added first, so the controls are added bottom to top.
            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Top;
            buttons.Height = 34;
            var adopt = new Button { Text = "Adopt CFR", Width = 100, Height = 26 };
            var reset = new Button { Text = "Reset", Width = 80, Height = 26 };
            var tips = new ToolTip { ShowAlways = true };
            tips.SetToolTip(adopt, "Adds the current carrier offset (CFR) to the correction, so the tuner is set onto the carrier and the derotator has nothing left to do. Use it while locked on a signal of known frequency, e.g. the QO-100 beacon.");
            tips.SetToolTip(reset, "Frequency correction back to 0 kHz");
            adopt.Click += (s, e) => AdoptCarrierOffset();
            reset.Click += (s, e) => { _correction_bar.Value = 0; _adopt_cfr_hz = double.NaN; ApplyNow(); };
            buttons.Controls.Add(adopt);
            buttons.Controls.Add(reset);
            _group.Controls.Add(buttons);

            AddTrimRow(_correction_label, _correction_bar, -MaxCorrectionPpm * UnitsPerPpm, MaxCorrectionPpm * UnitsPerPpm, 5 * UnitsPerPpm,
                       "Reference (crystal) error of the tuner in ppm of the tuner frequency: the tuner is set that far off, so the correction in kHz grows with the frequency " +
                       "(MiniTioune: ppm calib). Mouse wheel 0.5 ppm, Ctrl 5 ppm. The displayed frequency stays the nominal one.", tips, UnitsPerPpm / 2);
            AddTrimRow(_capture_label, _capture_bar, 0, CaptureSteps.Length - 1, 1,
                       "How far on each side of the tuned frequency the derotator searches for the carrier. Automatic is 1.5 x the symbol rate - too narrow for a low symbol rate on a drifting LNB, a wide range needs longer to lock.", tips);

            _symbol_rate_label.Dock = DockStyle.Top;
            _symbol_rate_label.Height = 26;
            _symbol_rate_label.Text = "Measured symbol rate:  -";
            _symbol_rate_label.TextAlign = ContentAlignment.MiddleLeft;
            _group.Controls.Add(_symbol_rate_label);

            _carrier_offset_label.Dock = DockStyle.Top;
            _carrier_offset_label.Height = 26;
            _carrier_offset_label.Text = "Carrier offset (CFR):  -";
            _carrier_offset_label.TextAlign = ContentAlignment.MiddleLeft;
            _group.Controls.Add(_carrier_offset_label);

            _found_label.Dock = DockStyle.Top;
            _found_label.Height = 26;
            _found_label.Text = "Freq found:  -";
            _found_label.TextAlign = ContentAlignment.MiddleLeft;
            tips.SetToolTip(_found_label, "The frequency the carrier is found at: the nominal frequency of the tuner plus the carrier offset (CFR) of the derotator. Like MiniTioune's \"Freq found\", also shown while the demodulator is still searching.");
            _group.Controls.Add(_found_label);

            _trace.Dock = DockStyle.Top;
            tips.SetToolTip(_trace, "Carrier offset (CFR) over time, newest on top. Yellow = the demodulator searches, green = locked. It runs without a lock, so a signal that does not lock can still be seen.");
            _group.Controls.Add(_trace);

            _derotator.Dock = DockStyle.Top;
            _group.Controls.Add(_derotator);

            // symbol rate buttons on top; the active (requested) rate is highlighted
            var rates = new FlowLayoutPanel();
            rates.Dock = DockStyle.Top;
            rates.Height = 34;
            rates.Padding = new Padding(0, 2, 0, 0);
            var rate_label = new Label { Text = "SR (kS):", AutoSize = false, Width = 56, Height = 26, TextAlign = ContentAlignment.MiddleLeft };
            rates.Controls.Add(rate_label);
            var rate_tips = new ToolTip { ShowAlways = true };
            foreach (uint rate in RateButtons)
            {
                var button = new Button { Text = RateText(rate), Tag = rate, Width = 37, Height = 26, Margin = new Padding(1, 0, 1, 0), FlatStyle = FlatStyle.Flat };
                button.Font = new Font("Microsoft Sans Serif", 8f);
                button.FlatAppearance.BorderColor = Color.Gray;
                button.Click += (s, e) => SymbolRateSelected?.Invoke((uint)((Button)s).Tag);
                rate_tips.SetToolTip(button, "Set the symbol rate of this tuner to " + rate + " kS");
                rates.Controls.Add(button);
                _rate_buttons.Add(button);
            }
            _group.Controls.Add(rates);

            _apply_timer.Interval = ApplyDelayMs;
            _apply_timer.Tick += (s, e) => ApplyNow();

            _capture_bar.ValueChanged += (s, e) => TrimEdited();
            _correction_bar.ValueChanged += (s, e) => TrimEdited();
            UpdateTrimLabels();

            parent.Controls.Add(_group);
            _group.BringToFront(); // stacks the groups top to bottom in creation order
        }

        // a label on the left and a slider on the right, added to the group as one row
        private void AddTrimRow(Label label, TrackBar bar, int min, int max, int ctrl_step, string tip, ToolTip tips, int notch_step = 1)
        {
            var row = new Panel();
            row.Dock = DockStyle.Top;
            row.Height = 40;

            label.Dock = DockStyle.Left;
            label.Width = 210;
            label.TextAlign = ContentAlignment.MiddleLeft;

            bar.Dock = DockStyle.Fill;
            bar.Minimum = min;
            bar.Maximum = max;
            bar.TickStyle = TickStyle.None;
            bar.AutoSize = false;
            bar.SmallChange = 1;
            bar.LargeChange = Math.Max(1, ctrl_step);

            // Mouse wheel: exactly one step per notch (the default scrolls several lines per notch, which
            // was 6 kHz for the correction), Ctrl = ctrl_step per notch. Wheel up = larger value.
            bar.MouseWheel += (s, e) =>
            {
                if (e is HandledMouseEventArgs handled)
                    handled.Handled = true;

                int notches = e.Delta / 120;
                int step = (Control.ModifierKeys & Keys.Control) != 0 ? ctrl_step : notch_step;
                bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, bar.Value + notches * step));
            };

            tips.SetToolTip(bar, tip);
            tips.SetToolTip(label, tip);

            row.Controls.Add(bar);   // Fill first, the label docks to the left of it
            row.Controls.Add(label);
            _group.Controls.Add(row);
        }

        // Sets the sliders from the stored values without firing TrimChanged.
        public void SetTrim(uint capture_range_khz, double correction_ppm)
        {
            _loading = true;
            try
            {
                int step = 0;
                for (int n = 0; n < CaptureSteps.Length; n++)
                {
                    if (Math.Abs((int)CaptureSteps[n] - (int)capture_range_khz) < Math.Abs((int)CaptureSteps[step] - (int)capture_range_khz))
                        step = n;
                }

                _capture_bar.Value = step;
                _correction_bar.Value = Math.Max(-MaxCorrectionPpm * UnitsPerPpm, Math.Min(MaxCorrectionPpm * UnitsPerPpm, (int)Math.Round(correction_ppm * UnitsPerPpm)));
                UpdateTrimLabels();
            }
            finally
            {
                _loading = false;
            }
        }

        // Highlights the button of the symbol rate the tuner is set to (kS). May be called from any thread.
        public void SetRequestedRate(uint symbol_rate)
        {
            if (_rate_buttons.Count == 0 || !_group.IsHandleCreated || _group.IsDisposed)
                return;

            try
            {
                _group.BeginInvoke((MethodInvoker)(() =>
                {
                    foreach (var button in _rate_buttons)
                        button.BackColor = (uint)button.Tag == symbol_rate ? Color.FromArgb(255, 204, 128) : SystemColors.Control;
                }));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }

        private void TrimEdited()
        {
            UpdateTrimLabels();

            if (_loading)
                return;

            _apply_timer.Stop();
            _apply_timer.Start(); // (re)start: tune once the slider has rested
        }

        private void ApplyNow()
        {
            _apply_timer.Stop();
            TrimChanged?.Invoke(CaptureSteps[_capture_bar.Value], _correction_bar.Value / (double)UnitsPerPpm);
        }

        private void AdoptCarrierOffset()
        {
            if (!_last_locked || _if_khz <= 0)
                return;

            long now = Environment.TickCount64;

            // The last adopt should have brought the offset towards 0. If it is clearly larger now, the sign is wrong (for example
            // with I/Q swap): turn it round before this adopt.
            if (!double.IsNaN(_adopt_cfr_hz) && now - _adopt_time < 90000 && Math.Abs(_last_carrier_offset_hz) > Math.Abs(_adopt_cfr_hz) * 1.3 + 500)
            {
                _cfr_sign = -_cfr_sign;
                Log.Information("Adopt CFR " + _group.Text + ": offset grew from " + _adopt_cfr_hz + " to " + _last_carrier_offset_hz + " Hz, sign turned to " + _cfr_sign);
            }

            double old_ppm = _correction_bar.Value / (double)UnitsPerPpm;
            double delta_ppm = _cfr_sign * _last_carrier_offset_hz / (_if_khz * 1000.0) * 1e6;
            int units = _correction_bar.Value + (int)Math.Round(delta_ppm * UnitsPerPpm);
            _correction_bar.Value = Math.Max(-MaxCorrectionPpm * UnitsPerPpm, Math.Min(MaxCorrectionPpm * UnitsPerPpm, units));

            _adopt_cfr_hz = _last_carrier_offset_hz;
            _adopt_time = now;
            Log.Information("Adopt CFR " + _group.Text + ": CFR " + _last_carrier_offset_hz + " Hz, IF " + _if_khz + " kHz, sign " + _cfr_sign +
                            ", correction " + old_ppm.ToString("0.0") + " -> " + (_correction_bar.Value / (double)UnitsPerPpm).ToString("0.0") + " ppm");
            ApplyNow();
        }

        private void UpdateTrimLabels()
        {
            uint capture = CaptureSteps[_capture_bar.Value];
            _capture_label.Text = capture == 0 ? "Capture range:  auto (1.5 x SR)" : "Capture range:  +-" + capture + " kHz";
            _correction_label.Text = CorrectionLabelText();
        }

        // "+42.0 ppm (+48 kHz)": the kHz the tuner is moved by at the current tuner frequency
        private string CorrectionLabelText()
        {
            double ppm = _correction_bar.Value / (double)UnitsPerPpm;
            string text = "Frequency correction:  " + ppm.ToString("+0.0;-0.0;0.0") + " ppm";

            if (_if_khz > 0)
                text += "  (" + (ppm * _if_khz / 1e6).ToString("+0;-0;0") + " kHz)";

            return text;
        }

        // demod_status: 2 = DVB-S2 locked, 3 = DVB-S locked. carrier_offset_hz: CFR in Hz, carrier_low_hz /
        // carrier_up_hz: search range CFRLOW / CFRUP in Hz. symbol_rate: measured symbol rate in Hz.
        public void Update(byte demod_status, int carrier_offset_hz, int carrier_low_hz, int carrier_up_hz, uint symbol_rate, double nominal_khz, double if_khz)
        {
            bool locked = demod_status == stv0910.DEMOD_S || demod_status == stv0910.DEMOD_S2;

            _if_khz = if_khz;
            SetText(_correction_label, CorrectionLabelText());

            _last_locked = locked;
            _last_carrier_offset_hz = carrier_offset_hz;

            long log_now = Environment.TickCount64;
            if (log_now - _last_log >= 2000)
            {
                _last_log = log_now;
                Log.Debug("Special " + _group.Text + ": " + (locked ? "locked" : "searching") + ", CFR " + carrier_offset_hz + " Hz, IF " + if_khz + " kHz, correction " +
                          (_correction_bar.Value / (double)UnitsPerPpm).ToString("0.0") + " ppm");
            }

            _derotator.SetValue(locked && carrier_up_hz > carrier_low_hz, carrier_offset_hz, carrier_low_hz, carrier_up_hz);

            _trace.SetValue(carrier_offset_hz, carrier_low_hz, carrier_up_hz, locked);

            // the carrier offset is valid without a lock too: while searching it is the frequency the derotator tries
            SetText(_carrier_offset_label, "Carrier offset (CFR):  " + (carrier_offset_hz / 1000.0).ToString("+0.000;-0.000;0.000") + " kHz" + (locked ? "" : "  (searching)"));
            SetText(_found_label, "Freq found:  " + (nominal_khz + carrier_offset_hz / 1000.0).ToString("N1") + " kHz" + (locked ? "" : "  (searching)"));
            SetText(_symbol_rate_label, locked
                ? "Measured symbol rate:  " + (symbol_rate / 1000.0).ToString("N3") + " kS/s"
                : "Measured symbol rate:  -");
        }

        private static void SetText(Control control, string text)
        {
            if (control.Text == text || !control.IsHandleCreated || control.IsDisposed)
                return;

            try
            {
                control.BeginInvoke((MethodInvoker)(() => control.Text = text));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }
    }
}
