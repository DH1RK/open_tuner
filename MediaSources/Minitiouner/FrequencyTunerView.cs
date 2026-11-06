using System;
using System.Collections.Generic;
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
        private const int MaxOffsetKHz = 100;
        private const int ApplyDelayMs = 600; // wait for the slider to rest before tuning again

        // Sign of the carrier offset (CFR) relative to the correction that removes it: the tuner is moved
        // by +CFR so that the derotator has nothing left to do. Flip to -1 if "Adopt CFR" makes it worse.
        private const int CfrSign = 1;

        private readonly CustomGroupBox _group;
        private readonly DerotatorBar _derotator = new DerotatorBar();
        private readonly CarrierTrace _trace = new CarrierTrace();
        private readonly AlignedValueLabel _found_label = new AlignedValueLabel();
        private readonly AlignedValueLabel _carrier_offset_label = new AlignedValueLabel();
        private readonly AlignedValueLabel _symbol_rate_label = new AlignedValueLabel();

        private readonly SpectrumSlider _capture_bar = new SpectrumSlider();
        private readonly Label _capture_label = new Label();
        private readonly SpectrumSlider _correction_bar = new SpectrumSlider();
        private readonly Label _correction_label = new Label();
        private readonly SpectrumSlider _offset_bar = new SpectrumSlider();      // manual offset in kHz on top of the ppm correction
        private readonly Label _offset_label = new Label();
        private readonly Timer _apply_timer = new Timer();
        private bool _loading = false;
        private int _last_carrier_offset_hz = 0;
        private int _cfr_sign = CfrSign;                 // turned round by AdoptCarrierOffset if the offset grows after an adopt
        private double _adopt_cfr_hz = double.NaN;         // carrier offset at the last adopt
        private long _adopt_time = 0;
        private long _last_log = 0;
        // signal search ("Find signal"): steps the correction through +-SearchRangeKHz and measures the channel level AGC2 after every step
        private const int SearchRangeKHz = 100;
        private const int SearchSettleMs = 1800;   // after a step: the tuner tunes, the acquisition starts at the centre of the derotator window
        private const int SearchDwellMs = 6000;    // the acquisition dwells there for some seconds first: the lowest AGC2 in this time counts
        private readonly FlexButton _find_button = new FlexButton();
        private readonly AlignedValueLabel _agc2_label = new AlignedValueLabel();
        private readonly Label _search_label = new Label();
        private readonly Timer _search_timer = new Timer();
        private readonly object _search_lock = new object();
        private bool _searching = false;
        private bool _search_setting = false;      // the search moves the slider itself
        private readonly System.Collections.Generic.List<int> _search_offsets_khz = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<int> _search_min = new System.Collections.Generic.List<int>();
        private int _search_index = -1;
        private int _search_center_units = 0;
        private long _search_step_start = 0;
        private int _step_min = int.MaxValue;
        private uint _requested_rate_ks = 0;
        // the Reset button goes back to this value (MinitiounerSettings.DefaultFreqCorrectionPpm), not to 0
        public double DefaultCorrectionPpm = 0;
        private double _if_khz = 0;       // tuner frequency, for the kHz equivalent of the correction and for Adopt CFR
        // Mirrors _correction_bar.Value, kept in sync via ValueChanged (fires on the UI thread for both user and
        // programmatic changes). Update() runs on the NIM worker thread and must never read _correction_bar.Value
        // directly - unlike a Label's Text, TrackBar.Value queries the native control and throws a cross-thread
        // InvalidOperationException off the UI thread. That exception is unhandled up to worker_thread(), killing
        // the NIM thread for good (tuning stops entirely) - but only when a debugger is attached, which is why this
        // was invisible running the plain .exe and only showed up under Visual Studio.
        private int _correction_units = 0;
        private int _offset_khz = 0;      // mirrors _offset_bar.Value, same reason as _correction_units above
        private bool _last_locked = false;
        private ushort _last_agc2 = ushort.MaxValue;
        private const int CarrierInChannelAgc2 = 40;   // AGC2 below this: the derotator sits on a carrier (empty channel 50 .. 170)

        // symbol rates offered as buttons at the top of the group (kS), narrow ones first
        private static readonly uint[] RateButtons = { 20, 25, 33, 66, 125, 250, 333, 500, 1000, 1500, 2000 };
        private readonly System.Collections.Generic.List<FlexButton> _rate_buttons = new System.Collections.Generic.List<FlexButton>();

        // button caption: 1000 / 1500 / 2000 kS as 1k / 1k5 / 2k so the buttons can stay narrow
        private static string RateText(uint rate)
        {
            if (rate >= 1000)
                return rate % 1000 == 0 ? (rate / 1000) + "k" : (rate / 1000) + "k" + ((rate % 1000) / 100);

            return rate.ToString();
        }

        // a rate button was clicked or a rate was entered (kS)
        public event Action<uint> SymbolRateSelected;

        // a frequency was entered (kHz, the frequency the tuner is set to: without the LNB offset)
        public event Action<uint> FrequencySelected;

        // direct entry of frequency and symbol rate, digit by digit, above the rate buttons (null for a source without it)
        private DigitEntry _rx_entry;            // receive frequency = LO frequency + LNB offset
        private DigitEntry _frequency_entry;     // LO frequency: what the tuner is set to
        private DigitEntry _symbol_rate_entry;
        private long _entry_offset_khz = 0;      // LNB offset of the tuner, kHz
        private Control _entry_row;              // the line of the three fields, grows when it wraps
        private Action _fit_height;              // sets the group height from the entry row and the rate buttons
        private const int EntryRowHeight = 68;   // label and entries: RX, LO and SR in one line

        // capture range in kHz (0 = automatic) and correction in ppm, fired once the sliders rest
        public event Action<uint, double, int> TrimChanged;      // capture range kHz, correction ppm, manual offset kHz

        // colour of the border, Color.Empty for the normal one (tuner selected by a click on its video)
        public void SetHighlight(Color color)
        {
            _group.HighlightColor = color;
        }

        public FrequencyTunerView(string title, Control parent) : this(title, parent, true, true)
        {
        }

        public FrequencyTunerView(string title, Control parent, bool capture_range) : this(title, parent, capture_range, false)
        {
        }

        // capture_range = false: for a source whose derotator range cannot be changed while it runs (Longmynd takes it only at its start)
        // entry = true: frequency and symbol rate can be entered digit by digit (MiniTiouner)
        public FrequencyTunerView(string title, Control parent, bool capture_range, bool entry)
        {
            _group = new CustomGroupBox();
            _group.Dock = DockStyle.Top;
            _group.Height = 600;
            _group.Text = title;
            _group.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point, (byte)0);
            _group.Padding = new Padding(8, 14, 8, 8);   // less distance of "SR (kS)" to the frame

            // Docking is evaluated last-added first, so the controls are added bottom to top.
            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Top;
            buttons.Height = 34;
            var adopt = new FlexButton { Text = "Adopt CFR", Width = 100, Height = 26 };
            var reset = new FlexButton { Text = "Reset", Width = 80, Height = 26 };
            var tips = new ToolTip { ShowAlways = true };
            tips.SetToolTip(adopt, "Adds the current carrier offset (CFR) to the correction, so the tuner is set onto the carrier and the derotator has nothing left to do. Use it while locked on a signal of known frequency, e.g. the QO-100 beacon. " +
                                     "Also works without a lock as long as the channel level AGC2 shows a carrier (below 40) and the CFR is steady - useful for the low symbol rates that do not lock yet.");
            tips.SetToolTip(reset, "Frequency correction back to the default (setting DefaultFreqCorrectionPpm, 0 ppm if not set)");
            adopt.Click += (s, e) => AdoptCarrierOffset();
            reset.Click += (s, e) => { _correction_bar.Value = (int)Math.Round(DefaultCorrectionPpm * UnitsPerPpm); _offset_bar.Value = 0; _adopt_cfr_hz = double.NaN; ApplyNow(); };
            _find_button.Text = "Find signal";
            _find_button.Width = 100;
            _find_button.Height = 26;
            tips.SetToolTip(_find_button, "Steps the frequency correction through +-100 kHz (about one signal width per step) and measures the channel level AGC2 at every step. A low AGC2 means the carrier is in the channel. " +
                                          "At the end the correction stays where AGC2 was lowest. Ends at once when the demodulator locks. Click again to stop.");
            _find_button.Click += (s, e) => ToggleSearch();
            buttons.Controls.Add(adopt);
            buttons.Controls.Add(reset);
            buttons.Controls.Add(_find_button);
            _group.Controls.Add(buttons);

            // The status line (messages of Adopt CFR and Find signal) takes room only while it has a text: empty it is 0 px high.
            _search_label.Dock = DockStyle.Top;
            _search_label.Height = 0;
            _search_label.Text = "";
            _search_label.TextChanged += (s, e) =>
            {
                _search_label.Height = _search_label.Text.Length == 0 ? 0 : 22;
                FitGroupHeight();
            };
            _search_label.TextAlign = ContentAlignment.MiddleLeft;
            _group.Controls.Add(_search_label);

            _search_timer.Interval = 250;
            _search_timer.Tick += (s, e) => SearchTick();

            AddTrimRow(_correction_label, _correction_bar, -MaxCorrectionPpm * UnitsPerPpm, MaxCorrectionPpm * UnitsPerPpm, 5 * UnitsPerPpm,
                       "Reference (crystal) error of the tuner in ppm of the tuner frequency: the tuner is set that far off, so the correction in kHz grows with the frequency " +
                       "(MiniTioune: ppm calib). Mouse wheel 0.5 ppm, Ctrl 5 ppm. The displayed frequency stays the nominal one.", tips, UnitsPerPpm / 2);
            AddTrimRow(_offset_label, _offset_bar, -MaxOffsetKHz, MaxOffsetKHz, 10,
                       "Manual frequency offset in kHz on top of the ppm correction, for the signal you are tuned to (the transmitter or the spectrum click is a few kHz off). " +
                       "Mouse wheel 1 kHz, Ctrl 10 kHz. It stays until you move it or press Reset, also for other frequencies.", tips, 1);
            if (capture_range)
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

            _agc2_label.Dock = DockStyle.Top;
            _agc2_label.Height = 22;
            _agc2_label.Text = "Channel level (AGC2):  -";
            _agc2_label.TextAlign = ContentAlignment.MiddleLeft;
            tips.SetToolTip(_agc2_label, "Gain of the demodulator's channel loop (AGC2): low (about 15 - 20) while the derotator is on a carrier, high (50 - 170) when the channel is empty. " +
                                         "It is the only measure of a signal while there is no lock.");
            _group.Controls.Add(_agc2_label);

            _found_label.Dock = DockStyle.Top;
            _found_label.Height = 26;
            _found_label.Text = "Freq found:  -";
            _found_label.TextAlign = ContentAlignment.MiddleLeft;
            tips.SetToolTip(_found_label, "The frequency the carrier is found at: the nominal frequency of the tuner plus the carrier offset (CFR) of the derotator. Like MiniTioune's \"Freq found\", also shown while the demodulator is still searching.");
            _group.Controls.Add(_found_label);

            // the values start in one column, on the height of the sliders (where the label of a slider row ends)
            foreach (var label in new[] { _symbol_rate_label, _carrier_offset_label, _agc2_label, _found_label })
                label.ValueLeft = TrimLabelWidth;
            _trace.Dock = DockStyle.Top;
            tips.SetToolTip(_trace, "Carrier offset (CFR) over time, newest on top. Yellow = the demodulator searches, green = locked. It runs without a lock, so a signal that does not lock can still be seen.");
            _group.Controls.Add(_trace);

            _derotator.Dock = DockStyle.Top;
            _group.Controls.Add(_derotator);

            // one background gradient over the derotator and the CFR trace
            _derotator.BackgroundGroup = _trace.BackgroundGroup = new Control[] { _trace, _derotator };

            // symbol rate buttons on top; the active (requested) rate is highlighted
            var rates = new FlowLayoutPanel();
            rates.Dock = DockStyle.Top;
            rates.Height = 34;
            rates.Padding = new Padding(0, 2, 0, 0);

            // In a narrow tab the buttons no longer fit in one line: the row wraps into a second one (2k / 1k5 were cut
            // off) and grows, everything below it (the derotator ...) moves down, and the group gets the extra height.
            rates.WrapContents = true;
            rates.AutoSize = true;
            rates.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rates.MinimumSize = new Size(0, 34);
            Action fit_height = FitGroupHeight;
            rates.SizeChanged += (s, e) => fit_height();
            _fit_height = fit_height;
            var rate_tips = new ToolTip { ShowAlways = true };
            foreach (uint rate in RateButtons)
            {
                var button = new FlexButton { Text = RateText(rate), Tag = rate, Width = 37, Height = 26, Margin = new Padding(1, 0, 1, 0), FlatStyle = FlatStyle.Flat, KeepFontSize = true };
                button.Font = new Font("Segoe UI Semibold", 8f);
                button.FlatAppearance.BorderColor = Color.Gray;
                button.Click += (s, e) => SymbolRateSelected?.Invoke((uint)((Button)s).Tag);
                rate_tips.SetToolTip(button, "Set the symbol rate of this tuner to " + rate + " kS");
                rates.Controls.Add(button);
                _rate_buttons.Add(button);
            }
            // the settings of the receiver below the rate buttons and above the derotator (added before the rates = below them)
            if (entry)
            {
                _group.Controls.Add(BuildPidMemoryRow());
                _pid_memory_row.SizeChanged += (s, e) => _fit_height();
                _group.Controls.Add(BuildPidRow(rate_tips));
                _pid_row.SizeChanged += (s, e) => _fit_height();
                _group.Controls.Add(BuildMemoryRows());
                _memory_row.SizeChanged += (s, e) => _fit_height();
                _group.Controls.Add(BuildModeRow(rate_tips));
                _mode_row.SizeChanged += (s, e) => _fit_height();
            }

            _group.Controls.Add(rates);

            // "SR (kS)" above the buttons, so that they start at the left edge
            var rate_label = new Label { Text = "SR (kS):", Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
            _group.Controls.Add(rate_label);

            // frequency and symbol rate entry above it (added last = on top)
            if (entry)
            {
                _group.Controls.Add(BuildEntryRow(rate_tips));
                _entry_row.SizeChanged += (s, e) => _fit_height();
            }

            _apply_timer.Interval = ApplyDelayMs;
            _apply_timer.Tick += (s, e) => ApplyNow();

            _capture_bar.ValueChanged += (s, e) => TrimEdited();
            _correction_bar.ValueChanged += (s, e) => { _correction_units = _correction_bar.Value; TrimEdited(); };
            _offset_bar.ValueChanged += (s, e) => { _offset_khz = _offset_bar.Value; TrimEdited(); };
            UpdateTrimLabels();

            parent.Controls.Add(_group);
            _group.BringToFront(); // stacks the groups top to bottom in creation order
            FitGroupHeight();
        }

        // Two labelled digit fields side by side: frequency (kHz, up to 7 digits = 9999.999 MHz) and symbol rate (kS, 5 digits).
        private Control BuildEntryRow(ToolTip tips)
        {
            _rx_entry = new DigitEntry(8);
            _frequency_entry = new DigitEntry(7);
            _symbol_rate_entry = new DigitEntry(5);

            // RX = LO + LNB offset: each of the two fields follows the other while it is edited, and either one tunes
            _rx_entry.ValueEdited += value => _frequency_entry.SetValue(Math.Max(0, value - _entry_offset_khz));
            _frequency_entry.ValueEdited += value => _rx_entry.SetValue(value + _entry_offset_khz);
            _rx_entry.ValueCommitted += value => FrequencySelected?.Invoke((uint)Math.Max(0, value - _entry_offset_khz));
            _frequency_entry.ValueCommitted += value => FrequencySelected?.Invoke((uint)value);
            _symbol_rate_entry.ValueCommitted += value => { if (value > 0) SymbolRateSelected?.Invoke((uint)value); };

            const string operation = " Mouse wheel over a digit changes it, click selects a digit, typing replaces it and moves on to the right, " +
                                     "arrow keys move / change. Enter or leaving the field tunes, Esc drops the input.";
            tips.SetToolTip(_rx_entry, "Receive frequency in kHz: the frequency of the tuner plus the LNB offset (QO-100: 10491500). Terrestrial: no offset, the same as the LO frequency." + operation);
            tips.SetToolTip(_frequency_entry, "Frequency the tuner itself is set to in kHz, without the LNB offset (LO frequency). Terrestrial: the receive frequency." + operation);
            tips.SetToolTip(_symbol_rate_entry, "Symbol rate in kS." + operation);

            // like the rate buttons: in a narrow tab the last field wraps into a second line and the row grows
            var row = new FlowLayoutPanel();
            row.Dock = DockStyle.Top;
            row.WrapContents = true;
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.MinimumSize = new Size(0, EntryRowHeight);
            row.Controls.Add(EntryCell("RX Freq (kHz):", _rx_entry));
            row.Controls.Add(EntryCell("LO Freq (kHz):", _frequency_entry));
            row.Controls.Add(EntryCell("SR (kS):", _symbol_rate_entry));
            _entry_row = row;
            return row;
        }

        // DVB mode: which standards the demodulator searches. 0 = automatic (both), 1 = DVB-S only, 2 = DVB-S2 only.
        private const int ModeRowHeight = 32;
        private readonly RadioButton _mode_s = new RadioButton();
        private readonly RadioButton _mode_s2 = new RadioButton();
        private readonly RadioButton _mode_auto = new RadioButton();
        private bool _mode_loading = false;

        // a DVB mode was picked by the user
        public event Action<byte> DvbModeSelected;

        // Memories M1 .. M4: a recall button or a store button was clicked (slot 0 .. 3)
        public event Action<int> MemoryRecall;
        public event Action<int> MemoryStore;

        // PID memories P1 .. P7: recall (slot) and store (slot, video PID, audio PID)
        public event Action<int> PidMemoryRecall;
        public event Action<int, int, int> PidMemoryStore;

        // PID choice: Auto = the player takes what the stream offers; otherwise only the video and audio PID typed in reach the player
        // (0 = none of that kind). "Found" lists the video and audio PIDs the TS analysis sees in the stream.
        private const int PidRowHeight = 40;
        private readonly CheckBox _pid_auto = new CheckBox();
        private DigitEntry _pid_video;
        private DigitEntry _pid_audio;
        private ToolTip _pid_tips;
        private string _pid_video_tip = "";
        private string _pid_audio_tip = "";
        private Control _pid_row;
        private bool _pid_loading = false;

        // the PID choice changed: automatic or not, video PID, audio PID
        public event Action<bool, int, int> PidChoiceChanged;

        private Control BuildPidRow(ToolTip tips)
        {
            _pid_video = new DigitEntry(4);
            _pid_audio = new DigitEntry(4);

            var row = new FlowLayoutPanel();
            row.Dock = DockStyle.Top;
            row.WrapContents = true;
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.MinimumSize = new Size(0, PidRowHeight);

            row.Controls.Add(new Label { Text = "PID:", AutoSize = false, Width = 40, Height = 38, TextAlign = ContentAlignment.MiddleLeft });
            _pid_auto.Text = "Auto";
            _pid_auto.AutoSize = true;
            _pid_auto.Checked = true;
            _pid_auto.Margin = new Padding(3, 9, 12, 0);
            row.Controls.Add(_pid_auto);
            row.Controls.Add(new Label { Text = "Video", AutoSize = false, Width = 46, Height = 38, TextAlign = ContentAlignment.MiddleLeft });
            _pid_video.Margin = new Padding(0, 0, 10, 0);
            row.Controls.Add(_pid_video);
            row.Controls.Add(new Label { Text = "Audio", AutoSize = false, Width = 46, Height = 38, TextAlign = ContentAlignment.MiddleLeft });
            row.Controls.Add(_pid_audio);

            _pid_tips = tips;
            _pid_video.ReadOnly = true;   // automatic: they show the PIDs found
            _pid_audio.ReadOnly = true;

            const string pid_tip = "Automatic: the media player takes the video and audio the stream offers. Off: only the video and audio PID typed in reach the media player " +
                                   "(0 = none of that kind), the other streams are dropped in front of it. Recorder, UDP stream and TS info keep the whole stream.";
            _pid_auto_tip = pid_tip;
            tips.SetToolTip(_pid_auto, pid_tip);
            _pid_video_tip = "PID of the video stream (0 - 8191)";
            _pid_audio_tip = "PID of the audio stream (0 - 8191, 0 = no audio)";
            tips.SetToolTip(_pid_video, _pid_video_tip);
            tips.SetToolTip(_pid_audio, _pid_audio_tip);

            _pid_auto.CheckedChanged += (s, e) =>
            {
                _pid_video.ReadOnly = _pid_audio.ReadOnly = _pid_auto.Checked;
                _pid_video.Marked = _pid_audio.Marked = !_pid_auto.Checked;   // orange: set by hand
                UpdatePidAutoColor();
                PidChanged();
            };
            _pid_video.ValueCommitted += value => PidChanged();
            _pid_audio.ValueCommitted += value => PidChanged();

            _pid_row = row;
            return row;
        }

        // "Auto" in red while the automatic PID choice is off: the user has tied the stream to the two PIDs typed in, and a different
        // station (other PIDs) would show nothing until Auto is switched on again.
        private static readonly Color PidAutoOffColor = Color.FromArgb(255, 255, 32, 32);

        // On: back to the colour the theme gives the control. The colour is not remembered at the start: the dark theme is applied later, a
        // colour taken then would be the black of the default theme.
        private string _pid_auto_tip = "";
        private const string PidAutoOffTip = "Attention: May not match other signal";

        private void UpdatePidAutoColor()
        {
            if (_pid_auto.Checked)
                _pid_auto.ResetForeColor();
            else
                _pid_auto.ForeColor = PidAutoOffColor;

            // off: a short warning instead of the explanation
            _pid_tips?.SetToolTip(_pid_auto, _pid_auto.Checked ? _pid_auto_tip : PidAutoOffTip);
        }

        private void PidChanged()
        {
            if (_pid_loading)
                return;

            PidChoiceChanged?.Invoke(_pid_auto.Checked, (int)Math.Min(8191, _pid_video.Value), (int)Math.Min(8191, _pid_audio.Value));
        }

        // Shows the saved PID choice without firing PidChoiceChanged.
        public void SetPidChoice(bool auto, int video_pid, int audio_pid)
        {
            _pid_loading = true;
            try
            {
                _pid_auto.Checked = auto;
                _pid_video.ReadOnly = _pid_audio.ReadOnly = auto;
                _pid_video.Marked = _pid_audio.Marked = !auto;
                UpdatePidAutoColor();
                _pid_video.SetValue(video_pid);
                _pid_audio.SetValue(audio_pid);
            }
            finally
            {
                _pid_loading = false;
            }
        }

        // The video and audio PIDs the analysis found. In the automatic mode the two fields show the first of each kind (read only, in white),
        // so that a switch to the manual mode starts with the PIDs of the stream. May be called from any thread.
        public void SetFoundPids(System.Collections.Generic.List<int> video, System.Collections.Generic.List<int> audio)
        {
            if (_pid_row == null || !_group.IsHandleCreated || _group.IsDisposed)
                return;

            try
            {
                _group.BeginInvoke((MethodInvoker)(() =>
                {
                    // all PIDs found (a stream can have several) in the tooltips; the fields show the first of each kind
                    _pid_tips.SetToolTip(_pid_video, _pid_video_tip + "\nFound in the stream: " + (video.Count == 0 ? "none" : string.Join(", ", video)));
                    _pid_tips.SetToolTip(_pid_audio, _pid_audio_tip + "\nFound in the stream: " + (audio.Count == 0 ? "none" : string.Join(", ", audio)));

                    if (_pid_auto.Checked)
                    {
                        _pid_video.SetValue(video.Count > 0 ? video[0] : 0);
                        _pid_audio.SetValue(audio.Count > 0 ? audio[0] : 0);
                    }
                }));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }

        private const int MemoryRowsHeight = MemoryButtonRow.StandardHeight;
        private const int MemoryButtons = 7;     // M1 .. M7 (as many slots as MinitiounerSource.MemorySlots)
        private MemoryButtonRow _memory_row;     // frequency memories M1 .. M7, grows when it wraps
        private MemoryButtonRow _pid_memory_row; // PID memories P1 .. P7 (as many slots as MinitiounerSource.PidMemorySlots)

        // One line like MiniTioune: M1 .. M7. A click recalls the memory, holding the button stores the tuner's setting into it.
        private Control BuildMemoryRows()
        {
            _memory_row = new MemoryButtonRow("Memory:", "M", MemoryButtons, "the setting of this tuner");
            _memory_row.Recall += slot => MemoryRecall?.Invoke(slot);
            _memory_row.Store += slot => MemoryStore?.Invoke(slot);
            return _memory_row;
        }

        // Shows whether a memory is in use (grey = empty) and what it holds. May be called from any thread.
        public void SetMemory(int slot, bool used, string text)
        {
            _memory_row?.SetSlot(slot, used, text);
        }

        // PID memories P1 .. P7: the video and audio PID of the fields, recalled with a click (switches to the manual PID choice), stored by holding.
        private Control BuildPidMemoryRow()
        {
            _pid_memory_row = new MemoryButtonRow("PID memory:", "P", MemoryButtons, "the PIDs shown");
            _pid_memory_row.Recall += slot => PidMemoryRecall?.Invoke(slot);
            _pid_memory_row.Store += slot => PidMemoryStore?.Invoke(slot, (int)Math.Min(8191, _pid_video.Value), (int)Math.Min(8191, _pid_audio.Value));
            return _pid_memory_row;
        }

        // Shows whether a PID memory is in use and what it holds. May be called from any thread.
        public void SetPidMemory(int slot, bool used, string text)
        {
            _pid_memory_row?.SetSlot(slot, used, text);
        }

        // FEC mode (Auto / Manual) and Wide scan, as MiniTioune shows them. Both are stored per tuner. Wide scan changes the symbol rate window of the
        // demodulator; the FEC mode is PREPARED ONLY, nothing is sent to the chip yet (needs the chip settings of #68).
        private readonly RadioButton _fec_auto = new RadioButton();
        private readonly RadioButton _fec_manual = new RadioButton();
        private readonly CheckBox _wide_scan = new CheckBox();
        private bool _fec_loading = false;
        private Control _mode_row;               // DVB mode, FEC mode and Wide scan, grows when it wraps

        // the FEC mode was picked: true = manual
        public event Action<bool> FecModeSelected;

        // the Wide scan checkbox was changed
        public event Action<bool> WideScanChanged;

        // a label and the radio buttons that belong together: radio buttons in one container exclude each other, so every group is a container
        private static FlowLayoutPanel RadioGroup(string label, int label_width)
        {
            var group = new FlowLayoutPanel();
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            group.WrapContents = false;
            group.Margin = new Padding(0, 0, 12, 0);
            group.Controls.Add(new Label { Text = label, AutoSize = false, Width = label_width, Height = 24, TextAlign = ContentAlignment.MiddleLeft });
            return group;
        }

        private Control BuildModeRow(ToolTip tips)
        {
            // in a narrow tab the groups wrap into a second line and the row grows, like the rate buttons
            var row = new FlowLayoutPanel();
            row.Dock = DockStyle.Top;
            row.WrapContents = true;
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.MinimumSize = new Size(0, ModeRowHeight);

            // DVB mode
            FlowLayoutPanel dvb = RadioGroup("DVB mode:", 90);
            var modes = new[] { new KeyValuePair<RadioButton, byte>(_mode_s, 1), new KeyValuePair<RadioButton, byte>(_mode_s2, 2), new KeyValuePair<RadioButton, byte>(_mode_auto, 0) };
            var texts = new[] { "DVB-S", "DVB-S2", "Auto" };

            for (int n = 0; n < modes.Length; n++)
            {
                RadioButton radio = modes[n].Key;
                byte mode = modes[n].Value;
                radio.Text = texts[n];
                radio.AutoSize = true;
                radio.Margin = new Padding(3, 3, 8, 0);
                radio.CheckedChanged += (s, e) =>
                {
                    if (radio.Checked && !_mode_loading)
                        DvbModeSelected?.Invoke(mode);
                };
                dvb.Controls.Add(radio);
            }

            tips.SetToolTip(_mode_s, "Search DVB-S only (the demodulator no longer looks for DVB-S2).");
            tips.SetToolTip(_mode_s2, "Search DVB-S2 only (the demodulator no longer looks for DVB-S).");
            tips.SetToolTip(_mode_auto, "Search both standards (the default).");
            _mode_auto.Checked = true;
            row.Controls.Add(dvb);

            // FEC mode (prepared)
            FlowLayoutPanel fec = RadioGroup("FEC mode:", 80);
            _fec_auto.Text = "Auto";
            _fec_manual.Text = "Manual";
            foreach (RadioButton radio in new[] { _fec_auto, _fec_manual })
            {
                radio.AutoSize = true;
                radio.Margin = new Padding(3, 3, 8, 0);
                fec.Controls.Add(radio);
            }

            _fec_auto.Checked = true;
            _fec_manual.CheckedChanged += (s, e) =>
            {
                if (!_fec_loading)
                    FecModeSelected?.Invoke(_fec_manual.Checked);
            };
            const string prepared = " PREPARED: the choice is saved, but it is not sent to the chip yet (comes with the chip settings, issue #68).";
            tips.SetToolTip(_fec_auto, "FEC mode automatic: the demodulator takes the code rates it finds." + prepared);
            tips.SetToolTip(_fec_manual, "FEC mode manual: only the code rates picked in the chip settings." + prepared);
            row.Controls.Add(fec);

            // Wide scan
            _wide_scan.Text = "Wide scan";
            _wide_scan.AutoSize = true;
            _wide_scan.Margin = new Padding(3, 3, 8, 0);
            _wide_scan.CheckedChanged += (s, e) =>
            {
                if (!_fec_loading)
                    WideScanChanged?.Invoke(_wide_scan.Checked);
            };
            tips.SetToolTip(_wide_scan, "Wide scan: the demodulator searches symbol rates from half to one and a half times the set rate. Off (the default): only +-5 %, as in MiniTioune. " +
                                        "Switching it tunes again.");
            row.Controls.Add(_wide_scan);

            _mode_row = row;
            return row;
        }

        // Shows the stored FEC mode and Wide scan without firing the events.
        public void SetFecAndWideScan(bool fec_manual, bool wide_scan)
        {
            _fec_loading = true;
            try
            {
                (fec_manual ? _fec_manual : _fec_auto).Checked = true;
                _wide_scan.Checked = wide_scan;
            }
            finally
            {
                _fec_loading = false;
            }
        }

        // Shows the DVB mode of the tuner without firing DvbModeSelected.
        public void SetDvbMode(byte mode)
        {
            _mode_loading = true;
            try
            {
                (mode == 1 ? _mode_s : mode == 2 ? _mode_s2 : _mode_auto).Checked = true;
            }
            finally
            {
                _mode_loading = false;
            }
        }

        // a label above a digit field, one cell of the entry row
        private static Control EntryCell(string text, DigitEntry entry)
        {
            var cell = new Panel { Width = entry.PreferredWidth + 12, Height = EntryRowHeight, Margin = new Padding(0) };
            cell.Controls.Add(new Label { Text = text, Location = new Point(0, 0), Size = new Size(entry.PreferredWidth + 12, 22), TextAlign = ContentAlignment.MiddleLeft });
            entry.Location = new Point(0, 24);
            cell.Controls.Add(entry);
            return cell;
        }

        // Shows the frequency the tuner is set to (LO frequency in kHz, without the LNB offset) and the LNB offset (kHz), so the RX field
        // can show the sum. May be called from any thread.
        public void SetRequestedFrequency(uint frequency, uint offset)
        {
            if (_frequency_entry == null || !_group.IsHandleCreated || _group.IsDisposed)
                return;

            try
            {
                _group.BeginInvoke((MethodInvoker)(() =>
                {
                    _entry_offset_khz = offset;
                    _frequency_entry.SetValue(frequency);
                    _rx_entry.SetValue((long)frequency + offset);
                }));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }

        // The height of the group follows its rows: the frame (caption and padding) plus the height of every row docked at the top. A row that
        // wraps or collapses (rate buttons, memories, the status line) reports its new height through SizeChanged, which calls this again.
        // Before, a fixed estimate was used, which left empty space below the buttons as soon as rows were taken out or put in.
        // REMARK - the height of this group went wrong several times (the frame overdrawn at the bottom, empty space below the buttons, the
        // group collapsed to its caption). What to keep in mind when it is changed:
        //  - The rows are docked at the top; their sum plus the frame (caption and padding, Height - DisplayRectangle.Height) gives the height.
        //    A fixed estimate ("614 + ...") breaks as soon as a row is added, taken out or wraps.
        //  - Rows that wrap (AutoSize FlowLayoutPanels) get their final height only after layout, and a row that collapses (the status line) changes
        //    it later: every such row must call FitGroupHeight from its SizeChanged (or TextChanged).
        //  - Do not skip rows with Visible = false: while the Special tab is not shown, every control on it reports Visible = false, so the sum
        //    is 0 and the group shrinks to its caption (and stays so).
        //  - The bottom line of the frame needs room of its own (BottomGap). Without it the last row covers the line, although the rows
        //    add up exactly.
        private const int BottomGap = 10;   // room for the bottom line of the frame, which the rows must not cover

        private void FitGroupHeight()
        {
            int chrome = _group.Height - _group.DisplayRectangle.Height;   // caption and padding, the same at any height
            int rows = 0;
            foreach (Control control in _group.Controls)
            {
                // not checked for Visible: while the Special tab is not shown, every control on it reports Visible = false
                if (control.Dock == DockStyle.Top)
                    rows += control.Height;
            }

            int height = chrome + rows + BottomGap;
            if (height > 0 && _group.Height != height)
                _group.Height = height;
        }

        // width of the labels of the slider rows: the sliders start behind it, and so do the values of the lines above them
        private const int TrimLabelWidth = 210;

        // a label on the left and a slider on the right, added to the group as one row
        private void AddTrimRow(Label label, SpectrumSlider bar, int min, int max, int ctrl_step, string tip, ToolTip tips, int notch_step = 1)
        {
            var row = new Panel();
            row.Dock = DockStyle.Top;
            row.Height = SpectrumSlider.StandardHeight + 4;

            label.Dock = DockStyle.Left;
            label.Width = TrimLabelWidth;
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
        public void SetTrim(uint capture_range_khz, double correction_ppm, int offset_khz)
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
                _offset_bar.Value = Math.Max(-MaxOffsetKHz, Math.Min(MaxOffsetKHz, offset_khz));
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
            _requested_rate_ks = symbol_rate;

            if (_rate_buttons.Count == 0 || !_group.IsHandleCreated || _group.IsDisposed)
                return;

            try
            {
                _group.BeginInvoke((MethodInvoker)(() =>
                {
                    foreach (var button in _rate_buttons)
                        button.BackColor = (uint)button.Tag == symbol_rate ? Color.FromArgb(255, 204, 128) : SystemColors.Control;
                    _symbol_rate_entry?.SetValue(symbol_rate);
                }));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }

        private void TrimEdited()
        {
            UpdateTrimLabels();

            if (_search_setting)
                return;

            if (_searching)
                EndSearch("stopped: slider moved", false);

            if (_loading)
                return;

            _apply_timer.Stop();
            _apply_timer.Start(); // (re)start: tune once the slider has rested
        }

        private void ApplyNow()
        {
            _apply_timer.Stop();
            TrimChanged?.Invoke(CaptureSteps[_capture_bar.Value], _correction_bar.Value / (double)UnitsPerPpm, _offset_bar.Value);
        }

        private void AdoptCarrierOffset()
        {
            bool carrier_only = !_last_locked;

            if (_if_khz <= 0 || (carrier_only && _last_agc2 >= CarrierInChannelAgc2))
            {
                _search_label.Text = "Adopt CFR: needs a lock or a carrier in the channel (AGC2 < " + CarrierInChannelAgc2 + ")";
                return;
            }

            long now = Environment.TickCount64;

            // The last adopt should have brought the offset towards 0. If it is clearly larger now, the sign is wrong (for example
            // with I/Q swap): turn it round before this adopt.
            if (!carrier_only && !double.IsNaN(_adopt_cfr_hz) && now - _adopt_time < 90000 && Math.Abs(_last_carrier_offset_hz) > Math.Abs(_adopt_cfr_hz) * 1.3 + 500)
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
            _search_label.Text = "Adopt CFR: " + _last_carrier_offset_hz + " Hz" + (carrier_only ? " (no lock, AGC2 " + _last_agc2 + ")" : "");
            Log.Information("Adopt CFR " + _group.Text + (carrier_only ? " without lock (AGC2 " + _last_agc2 + ")" : "") + ": CFR " + _last_carrier_offset_hz + " Hz, IF " + _if_khz + " kHz, sign " + _cfr_sign +
                            ", correction " + old_ppm.ToString("0.0") + " -> " + (_correction_bar.Value / (double)UnitsPerPpm).ToString("0.0") + " ppm");
            ApplyNow();
        }

        // ---- signal search --------------------------------------------------------------------------------------------------
        private int KHzToUnits(double khz)
        {
            return (int)Math.Round(khz * 1e6 / _if_khz * UnitsPerPpm);   // ppm = kHz * 1e6 / tuner frequency in kHz, slider unit 0.1 ppm
        }

        private void ToggleSearch()
        {
            if (_searching)
            {
                EndSearch("stopped", true);
                return;
            }

            if (_if_khz <= 0)
            {
                _search_label.Text = "Find signal: no tuner frequency yet";
                return;
            }

            uint rate = _requested_rate_ks > 0 ? _requested_rate_ks : 25;
            int step = (int)Math.Max(8, Math.Min(40, rate));       // about one signal width, the windows overlap a little

            _search_offsets_khz.Clear();
            _search_min.Clear();
            _search_offsets_khz.Add(0);
            for (int k = step; k <= SearchRangeKHz; k += step)
            {
                _search_offsets_khz.Add(k);
                _search_offsets_khz.Add(-k);
            }

            _apply_timer.Stop();
            _search_center_units = _correction_bar.Value;
            _search_index = -1;
            _searching = true;
            _find_button.Text = "Stop search";
            Log.Information("Find signal " + _group.Text + ": " + _search_offsets_khz.Count + " steps of " + step + " kHz around " + (_search_center_units / (double)UnitsPerPpm).ToString("0.0") + " ppm, SR " + rate + " kS");
            SearchNextStep();
            _search_timer.Start();
        }

        private void SearchNextStep()
        {
            _search_index++;
            if (_search_index >= _search_offsets_khz.Count)
            {
                FinishSearch();
                return;
            }

            int units = _search_center_units + KHzToUnits(_search_offsets_khz[_search_index]);
            units = Math.Max(-MaxCorrectionPpm * UnitsPerPpm, Math.Min(MaxCorrectionPpm * UnitsPerPpm, units));

            _search_setting = true;
            _correction_bar.Value = units;
            _search_setting = false;

            lock (_search_lock)
            {
                _step_min = int.MaxValue;
                _search_step_start = Environment.TickCount64;
            }

            _search_label.Text = "Find signal " + (_search_index + 1) + "/" + _search_offsets_khz.Count + ":  " + (units / (double)UnitsPerPpm).ToString("+0.0;-0.0;0.0") + " ppm";
            ApplyNow();
        }

        private void SearchTick()
        {
            if (!_searching)
                return;

            if (_last_locked)
            {
                EndSearch("locked at " + (_correction_bar.Value / (double)UnitsPerPpm).ToString("+0.0;-0.0;0.0") + " ppm", false);
                return;
            }

            if (Environment.TickCount64 - _search_step_start < SearchSettleMs + SearchDwellMs)
                return;

            int min;
            lock (_search_lock)
                min = _step_min == int.MaxValue ? 99999 : _step_min;
            _search_min.Add(min);
            Log.Information("Find signal " + _group.Text + ": " + (_search_center_units + KHzToUnits(_search_offsets_khz[_search_index])) / (double)UnitsPerPpm + " ppm (" +
                            _search_offsets_khz[_search_index] + " kHz): lowest AGC2 " + min);
            SearchNextStep();
        }

        private void FinishSearch()
        {
            var sorted = new System.Collections.Generic.List<int>(_search_min);
            sorted.Sort();
            int median = sorted[sorted.Count / 2];
            int best = 0;
            for (int i = 1; i < _search_min.Count; i++)
            {
                if (_search_min[i] < _search_min[best])
                    best = i;
            }

            if (_search_min[best] < 0.6 * median)
            {
                int units = Math.Max(-MaxCorrectionPpm * UnitsPerPpm, Math.Min(MaxCorrectionPpm * UnitsPerPpm, _search_center_units + KHzToUnits(_search_offsets_khz[best])));
                _search_setting = true;
                _correction_bar.Value = units;
                _search_setting = false;
                EndSearch("signal near " + (units / (double)UnitsPerPpm).ToString("+0.0;-0.0;0.0") + " ppm (AGC2 " + _search_min[best] + ", empty channel about " + median + ")", true);
            }
            else
            {
                _search_setting = true;
                _correction_bar.Value = _search_center_units;
                _search_setting = false;
                EndSearch("no signal found (lowest AGC2 " + _search_min[best] + ", median " + median + "), correction back", true);
            }
        }

        // apply = tune with the slider value as it is now (after a search that found something or a restored value)
        private void EndSearch(string text, bool apply)
        {
            _search_timer.Stop();
            _searching = false;
            _find_button.Text = "Find signal";
            _search_label.Text = "Find signal: " + text;
            Log.Information("Find signal " + _group.Text + ": " + text);

            if (apply)
                ApplyNow();
        }

        private void UpdateTrimLabels()
        {
            uint capture = CaptureSteps[_capture_bar.Value];
            _capture_label.Text = capture == 0 ? "Capture range:  auto (1.5 x SR)" : "Capture range:  +-" + capture + " kHz";
            _correction_label.Text = CorrectionLabelText();
            _offset_label.Text = "Manual offset:  " + _offset_bar.Value.ToString("+0;-0;0") + " kHz";
        }

        // "+42.0 ppm (+48 kHz)": the kHz the tuner is moved by at the current tuner frequency
        private string CorrectionLabelText()
        {
            double ppm = _correction_units / (double)UnitsPerPpm;
            string text = "Frequency correction:  " + ppm.ToString("+0.0;-0.0;0.0") + " ppm";

            if (_if_khz > 0)
                text += "  (" + (ppm * _if_khz / 1e6).ToString("+0;-0;0") + " kHz)";

            return text;
        }

        // demod_status: 2 = DVB-S2 locked, 3 = DVB-S locked. carrier_offset_hz: CFR in Hz, carrier_low_hz /
        // carrier_up_hz: search range CFRLOW / CFRUP in Hz. symbol_rate: measured symbol rate in Hz.
        public void Update(byte demod_status, int carrier_offset_hz, int carrier_low_hz, int carrier_up_hz, uint symbol_rate, double nominal_khz, double if_khz, ushort agc2)
        {
            bool locked = demod_status == stv0910.DEMOD_S || demod_status == stv0910.DEMOD_S2;

            SetText(_agc2_label, "Channel level (AGC2):  " + agc2 + (agc2 < 40 ? "   (signal in the channel)" : ""));
            lock (_search_lock)
            {
                if (_searching && Environment.TickCount64 - _search_step_start >= SearchSettleMs)
                    _step_min = Math.Min(_step_min, agc2);
            }

            _if_khz = if_khz;
            SetText(_correction_label, CorrectionLabelText());

            _last_locked = locked;
            _last_agc2 = agc2;
            _last_carrier_offset_hz = carrier_offset_hz;

            long log_now = Environment.TickCount64;
            if (log_now - _last_log >= 2000)
            {
                _last_log = log_now;
                Log.Debug("Special " + _group.Text + ": " + (locked ? "locked" : "searching") + ", CFR " + carrier_offset_hz + " Hz, IF " + if_khz + " kHz, correction " +
                          (_correction_units / (double)UnitsPerPpm).ToString("0.0") + " ppm");
            }

            _derotator.SetValue(locked && carrier_up_hz > carrier_low_hz, carrier_offset_hz, carrier_low_hz, carrier_up_hz);

            _trace.SetValue(carrier_offset_hz, carrier_low_hz, carrier_up_hz, locked);

            // the carrier offset is valid without a lock too: while searching it is the frequency the derotator tries
            SetText(_carrier_offset_label, "Carrier offset (CFR):  " + (carrier_offset_hz / 1000.0).ToString("+0.000;-0.000;0.000") + " kHz" + (locked ? "" : "  (searching)"));
            SetText(_found_label, "Freq found:  " + (nominal_khz + _offset_khz + carrier_offset_hz / 1000.0).ToString("N1") + " kHz" + (locked ? "" : "  (searching)"));
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
