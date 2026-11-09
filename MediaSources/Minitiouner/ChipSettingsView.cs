using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using opentuner.Utilities;

namespace opentuner.MediaSources.Minitiouner
{
    // The "Chip" tab (issue #62): the receiver settings of MiniTioune's Extra Panel that open_tuner can write itself, one group per
    // topic - carrier loops, I/Q, equalizer, noise and constellation of the STV0910 (demodulator), baseband gain of the STV6120
    // (tuner) and the software settings (refresh timing, fill level of the TS buffers). The controls read from and write into the
    // settings of the board; 0.4 s after the last change SettingsChanged is raised (the caller stores the settings and has the
    // chips written again), so dragging the gain slider does not tune again at every step.
    public class ChipSettingsView
    {
        private const int ApplyDelayMs = 400;
        private const int BufferTraceMs = 250;   // one sample of the buffer trace = the peak of this time

        private static readonly string[] IqChoices = { "off", "on", "off, last value kept", "on, fixed" };
        private static readonly string[] DfeChoices = { "off", "frozen", "very slow", "median", "fastest" };
        private static readonly string[] FfeChoices = { "frozen", "very slow", "median", "fastest" };
        private static readonly string[] Algo1Choices = { "costas (MiniTioune)", "citroen 1", "citroen 2 (longmynd)" };
        private static readonly string[] Algo2Choices = { "algorithm 0 (CNR > 7 dB)", "algorithm 1 (CNR < 7 dB)", "native (recommended)" };
        private static readonly string[] NoiseDataChoices = { "2^-20 (slowest)", "2^-18", "2^-16", "2^-14", "2^-12 (default)", "2^-10", "2^-8", "2^0 (fastest)" };
        private static readonly string[] NoisePlhChoices = { "slowest", "2^-10", "2^-8 (default)", "2^0 (fastest)" };
        private static readonly string[] ConstellationChoices =
        {
            "demodulator IQ output", "equalizer output", "derotator 2 output", "symbols + inter symbols", "symbols",
            "inter symbols", "derotator 1 output", "IQ mismatches output", "demodulator input",
        };
        private static readonly int[] RefreshChoices = { 125, 200, 300 };

        private readonly MinitiounerSettings _settings;
        private readonly Func<int, int> _buffer_bytes;
        private readonly int _tuners;
        private readonly Control _first_group;
        private readonly ToolTip _tips = new ToolTip();
        private readonly List<Action> _loaders = new List<Action>();   // controls <- settings
        private readonly RadioButton[] _refresh = new RadioButton[3];
        private readonly SpectrumSlider _gain = new SpectrumSlider();
        private readonly Label _gain_label = new Label();
        private readonly EqualizerControl[] _equalizer;
        private readonly Label[][] _iq_cells;
        private readonly string[] _iq_raw_text;
        private string _iq_read_tip = "";
        private readonly int[][] _iq_values;
        private readonly TraceBar[] _buffer_bars;
        private readonly Timer _buffer_timer = new Timer();   // samples the fill level of the TS buffers fast, independent of the refresh timing
        private readonly int[] _buffer_peak;
        private long _buffer_next_push = 0;
        private readonly Timer _apply_timer = new Timer();
        private readonly Timer _ui_timer = new Timer();
        private bool _loading = false;

        // The settings were changed by the user (the caller stores them and has the chips written again).
        public event Action SettingsChanged;

        // Settings that act at once without writing the chips again and without a retune (the error counter): the caller stores and applies them.
        public event Action LiveSettingsChanged;

        private readonly Label[] _event_label;
        private readonly uint[] _event_raw;
        private readonly bool[] _event_has;
        private double _event_mclk = 0;

        public ChipSettingsView(Control parent, MinitiounerSettings settings, int tuners, Func<int, int> buffer_bytes)
        {
            _settings = settings;
            _tuners = tuners;
            _buffer_bytes = buffer_bytes;
            _equalizer = new EqualizerControl[tuners];
            _event_label = new Label[tuners];
            _event_raw = new uint[tuners];
            _event_has = new bool[tuners];
            _iq_cells = new Label[tuners][];
            _buffer_bars = new TraceBar[tuners];
            _buffer_peak = new int[tuners];
            _iq_raw_text = new string[tuners];
            _iq_values = new int[tuners][];
            _tips.ShowAlways = true;

            // ---- STV0910: carrier loops ----
            TableLayoutPanel loops = AddGroup(parent, "STV0910 (Demodulator) - Carrier Loops / Algo", out _first_group);

            var loop1 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            loop1.Controls.Add(MakeCheck("closed", () => _settings.Loop1On, v => _settings.Loop1On = v, false,
                      "CARCFG.ROTAON: carrier loop 1 derotator in action. Off = loop open. MiniTioune: loop1 On."));
            loop1.Controls.Add(MakeCombo(Algo1Choices, () => _settings.CarrierPhaseAlgo, v => _settings.CarrierPhaseAlgo = (byte)v, false, 175,
                      "CARCFG.PH_DET_ALGO of carrier loop 1 (the phase detector). MiniTioune uses costas, longmynd citroen 2."));
            AddRow(loops, "Loop 1", loop1);

            var loop2 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            loop2.Controls.Add(MakeCheck("closed", () => _settings.Loop2On, v => _settings.Loop2On = v, false,
                      "CAR2CFG.ROTA2ON: carrier loop 2 derotator in action. Off = loop open."));
            loop2.Controls.Add(MakeCombo(Algo2Choices, () => _settings.Algo2, v => _settings.Algo2 = v, false, 190,
                      "CAR2CFG.PH_DET_ALGO2: algorithm for the phase error on QPSK symbols (the phase detector). Algorithms 0 and 1 are for legacy compatibility, native is recommended."));
            AddRow(loops, "Loop 2", loop2);

            // ---- STV0910: I/Q ----
            TableLayoutPanel iq = AddGroup(parent, "STV0910 (Demodulator) - I/Q", out _);

            BindCheck(iq, "I/Q swap", "on", () => _settings.IqSwap, v => _settings.IqSwap = v,
                      "TNRCFG2.TUN_IQSWAP. On flips the sign of the carrier offset (CFR).");

            string iq_tip = "AGC1CFG: off = no correction, on = running (reset value), off with the last measure kept, on but fixed (the value found so far is no longer updated).";
            var compensation = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            compensation.Controls.Add(Stack("DC offset", MakeCombo(IqChoices, () => _settings.IqDc, v => _settings.IqDc = v, false, 100, iq_tip)));
            compensation.Controls.Add(Stack("Imbalance", MakeCombo(IqChoices, () => _settings.IqAmplitude, v => _settings.IqAmplitude = v, false, 100, iq_tip)));
            compensation.Controls.Add(Stack("Quadrature", MakeCombo(IqChoices, () => _settings.IqQuadrature, v => _settings.IqQuadrature = v, false, 100, iq_tip)));
            AddRow(iq, "Compensation", compensation);

            string iq_read_tip = "What the demodulator measures and corrects, as MiniTioune shows it. Iof / Qof: the DC offset error on I and Q in ADC steps (IDCCOMP / QDCCOMP count half steps and the correction: error = -value / 2). " +
                                 "Imbal: amplitude compensation of Q against I (AGC1AMM / 128, 1.00 = both equal). Quadra: quadrature error (AGC1QUAD / 128, 0 = none).";
            _iq_read_tip = iq_read_tip;
            AddRow(iq, "Measured", IqReadoutRow(new[] { "Iof", "Qof", "Imbal", "Quadra" }, null, iq_read_tip));

            for (int i = 0; i < tuners; i++)
            {
                _iq_cells[i] = new Label[4];
                AddRow(iq, "Tuner " + (i + 1), IqReadoutRow(new[] { "-", "-", "-", "-" }, _iq_cells[i], iq_read_tip));
            }

            // ---- STV0910: equalizer ----
            TableLayoutPanel eq = AddGroup(parent, "STV0910 (Demodulator) - Equalizer", out _);

            var eq_modes = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            eq_modes.Controls.Add(Caption("DFE"));
            eq_modes.Controls.Add(MakeCombo(DfeChoices, () => _settings.EqualizerDfe, v => _settings.EqualizerDfe = v, false, 110,
                      "EQUALCFG: the equalizer that removes echoes (cable reflections). Off stops and resets it, frozen keeps the learned coefficients, otherwise the speed of the adaption (MU_EQUALDFE): very slow = reset value and MiniTioune's setting."));
            eq_modes.Controls.Add(Caption("FFE"));
            eq_modes.Controls.Add(MakeCombo(FfeChoices, () => _settings.EqualizerFfe, v => _settings.EqualizerFfe = v, false, 110,
                      "FFECFG: the equalizer that compensates filter and group delay errors. It always stays on (the data sheet says it has to); the speed of the adaption (MU_EQUALFFE): very slow = reset value and MiniTioune's setting."));
            AddRow(eq, "Adaption", eq_modes);

            var taps = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            for (int i = 0; i < tuners; i++)
            {
                var stack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 8, 0) };
                stack.Controls.Add(new Label { Text = "tuner " + (i + 1), AutoSize = true, Margin = new Padding(3, 2, 3, 0), ForeColor = Color.FromArgb(170, 170, 170), Font = new Font("Segoe UI Semibold", 8f) });
                _equalizer[i] = new EqualizerControl { Width = 175, Margin = new Padding(3, 2, 3, 8) };
                stack.Controls.Add(_equalizer[i]);
                taps.Controls.Add(stack);
                _tips.SetToolTip(_equalizer[i], "Tuner " + (i + 1) + ": coefficients of the DFE (8 taps) and the FFE (4 taps), I yellow, Q light blue. Bars near the middle line = nothing to correct. The scale is +-16 steps like MiniTioune's LED grid, larger values are cut off at the border and marked red.");
            }
            AddRow(eq, "Taps", taps);

            // ---- STV0910: noise and constellation ----
            TableLayoutPanel noise = AddGroup(parent, "STV0910 (Demodulator) - Noise / Constellation", out _);

            var noise_speed = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            noise_speed.Controls.Add(Stack("data", MakeCombo(NoiseDataChoices, () => _settings.NoiseData, v => _settings.NoiseData = v, false, 150,
                      "NOSCFG.NOSDATA_BETA: how fast the noise measurement on the data (DVB-S, DVB-S2) follows the signal. Slower = smoother, faster = livelier. MiniTioune: noise speed.")));
            noise_speed.Controls.Add(Stack("PL header", MakeCombo(NoisePlhChoices, () => _settings.NoisePlh, v => _settings.NoisePlh = v, false, 150,
                      "NOSCFG.NOSPLH_BETA: the same for the structure symbols of DVB-S2 (PL header, pilots). The noise bar of the Expert tab uses this one in DVB-S2.")));
            AddRow(noise, "Noise speed", noise_speed);

            BindCombo(noise, "Constellation", ConstellationChoices, () => _settings.ConstellationSource, v => _settings.ConstellationSource = v,
                      "IQCONST.IQSYMB_SEL: the point in the data flow whose I/Q samples the constellation of the Expert tab shows.");

            // ---- STV0910: error counter (MiniTioune's Error/Event panel) ----
            TableLayoutPanel err = AddGroup(parent, "STV0910 (Demodulator) - Error Counter / Events", out _);

            AddRow(err, "Event type", MakeCombo(ErrorEvent.SourceNames(), () => _settings.ChipErrSource, v => _settings.ChipErrSource = v, true, 250,
                      "ERRCTRL1.ERR_SOURCE1: what error counter 1 counts. DVB-S2: LDPC / BCH errors, BB header errors, CRC-8 packet errors, TS errors, bad DFL; the frame rate of the LDPC decoder, the TS FIFO and the packet bitrate " +
                      "are measurements. The entries marked DVB-S count in DVB-S only. Both tuners use the same choice."));
            AddRow(err, "Counting", MakeCombo(ErrorEvent.Modes, () => _settings.ChipErrMode, v => _settings.ChipErrMode = v, true, 250,
                      "ERRCTRL1.NUM_EVENT1 (Nbr of event): count with reset (the events since the last read, about every 200 ms here) or without reset (running total), a rate over 2^14 ... 2^22 bytes " +
                      "(packets, frames: shorter windows) or the average. A change starts the counting again."));
            BindCheck(err, "TS error", "on", () => _settings.ChipTsError, v => _settings.ChipTsError = v, true,
                      "TSCFGH.TSFIFO_ERRMODE. On: packets in error are labelled (TEI bit) and all are output. Off: packets in error are deleted before the output, the picture shows gaps instead of block errors. " +
                      "MiniTioune: TS error On / Off.");

            for (int i = 0; i < tuners; i++)
            {
                _event_label[i] = new Label { AutoSize = true, Text = "-", Margin = new Padding(3, 4, 3, 6) };
                AddRow(err, "Value, tuner " + (i + 1), _event_label[i]);
                _tips.SetToolTip(_event_label[i], "Error counter 1 of tuner " + (i + 1) + " in a readable unit (counts as they are, rates per counting unit; the frame rate of the LDPC decoder in frames/s, the packet bitrate in kbit/s). Only while locked.");
            }

            // ---- STV6120, the tuner ----
            TableLayoutPanel tuner = AddGroup(parent, "STV6120 (Tuner)", out _);

            var gain_panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            _gain.Minimum = 0;
            _gain.Maximum = 8;
            _gain.TickFrequency = 1;
            _gain.LargeChange = 1;
            _gain.SmallChange = 1;
            _gain.Width = 190;
            _gain.Height = SpectrumSlider.StandardHeight;
            _gain.ValueChanged += (s, e) =>
            {
                UpdateGainLabel();
                if (_loading)
                    return;
                _settings.BasebandGainDb = _gain.Value * 2;
                Changed();
            };

            // one step (2 dB) per notch of the mouse wheel; the default of a TrackBar is three steps
            _gain.MouseWheel += (s, e) =>
            {
                if (e is HandledMouseEventArgs handled)
                    handled.Handled = true;

                if (e.Delta != 0)
                    _gain.Value = Math.Max(_gain.Minimum, Math.Min(_gain.Maximum, _gain.Value + (e.Delta > 0 ? 1 : -1)));
            };

            _gain_label.AutoSize = false;
            _gain_label.Width = 60;
            _gain_label.Height = 28;
            _gain_label.TextAlign = ContentAlignment.MiddleLeft;
            gain_panel.Controls.Add(_gain);
            gain_panel.Controls.Add(_gain_label);
            _loaders.Add(() => _gain.Value = Math.Max(0, Math.Min(8, _settings.BasebandGainDb / 2)));
            AddRow(tuner, "Baseband gain", gain_panel);
            _tips.SetToolTip(_gain, "STV6120 CTRL2.BBGAIN in steps of 2 dB (MiniTioune 8 dB, longmynd 6 dB). Written at every tune. The demodulator's AGC makes up for it, so the picture hardly changes.");

            // ---- software ----
            TableLayoutPanel software = AddGroup(parent, "Software", out _);

            var refresh_panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
            for (int i = 0; i < _refresh.Length; i++)
            {
                _refresh[i] = new RadioButton { Text = RefreshChoices[i] + " ms", AutoSize = true, Tag = i };
                _refresh[i].CheckedChanged += (s, e) =>
                {
                    if (_loading || !((RadioButton)s).Checked)
                        return;
                    _settings.RefreshIntervalMs = RefreshChoices[(int)((RadioButton)s).Tag];
                    Changed();
                };
                refresh_panel.Controls.Add(_refresh[i]);
            }
            _loaders.Add(() =>
            {
                int nearest = 0;
                for (int i = 1; i < RefreshChoices.Length; i++)
                {
                    if (Math.Abs(RefreshChoices[i] - _settings.RefreshIntervalMs) < Math.Abs(RefreshChoices[nearest] - _settings.RefreshIntervalMs))
                        nearest = i;
                }
                _refresh[nearest].Checked = true;
            });
            AddRow(software, "Refresh Timing (I2C)", refresh_panel);
            _tips.SetToolTip(refresh_panel, "Pause between two reads of the status registers (I2C), the refresh timing of MiniTioune. It also sets how fast the gauges, equalizer taps, I/Q values and error counter update (they are read with every poll). Shorter = livelier, but more I2C traffic; the measured refresh time is on the Expert tab. The TS data and the TS buffer traces do not depend on it.");

            // the fill level of the TS buffers as a trace of the last ~30 s like the noise of the Expert tab: what matters is whether it keeps growing
            // (the players, recorders or the parser do not keep up), not the single value
            for (int i = 0; i < tuners; i++)
            {
                _buffer_bars[i] = new TraceBar("Tuner " + (i + 1), " kB") { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3, 2, 3, 6) };
                AddRow(software, "TS buffer", _buffer_bars[i]);
                _tips.SetToolTip(_buffer_bars[i], "Bytes waiting in the TS data queue of tuner " + (i + 1) + " (before the players, recorders and the parser take them), in kB. " +
                                                  "Trace of the last ~30 s, every point is the highest level of 250 ms (so the trace does not depend on the status polling), the vertical scale follows its minimum and maximum (shown in brackets). A value that keeps growing means a consumer does not keep up.");
            }

            // ---- defaults and the note, below the groups ----
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 6, 8, 8) };

            var defaults = new FlexButton { Text = "Back to defaults", AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
            defaults.Click += (s, e) =>
            {
                CopyChipFields(new MinitiounerSettings(), _settings);
                LoadAll();
                Changed();
                LiveChanged();
            };
            bottom.Controls.Add(defaults);

            var note = new Label
            {
                Text = "A change takes effect at once: the chips are written again and both tuners of this board are tuned again, the picture is gone for a moment. " +
                       "The defaults are the values the chips start with (MiniTioune's where known).",
                AutoSize = true,
                Margin = new Padding(3, 18, 3, 3),
                ForeColor = Color.FromArgb(170, 170, 170),
                Font = new Font("Segoe UI Semibold", 8f),
            };
            bottom.Controls.Add(note);
            parent.SizeChanged += (s, e) => note.MaximumSize = new Size(Math.Max(120, parent.Width - 40), 0);

            parent.Controls.Add(bottom);
            bottom.BringToFront();

            LoadAll();

            _apply_timer.Interval = ApplyDelayMs;
            _apply_timer.Tick += (s, e) =>
            {
                _apply_timer.Stop();
                SettingsChanged?.Invoke();
            };

            _buffer_timer.Interval = 20;
            _buffer_timer.Tick += (s, e) =>
            {
                if (!_first_group.Visible)
                    return;

                for (int i = 0; i < _tuners; i++)
                    _buffer_peak[i] = Math.Max(_buffer_peak[i], _buffer_bytes(i));

                long now = Environment.TickCount64;
                if (now >= _buffer_next_push)
                {
                    _buffer_next_push = now + BufferTraceMs;
                    for (int i = 0; i < _tuners; i++)
                    {
                        _buffer_bars[i].SetValue(true, _buffer_peak[i] / 1024.0);
                        _buffer_peak[i] = 0;
                    }
                }
            };
            _buffer_timer.Start();

            _ui_timer.Interval = 100;
            _ui_timer.Tick += (s, e) => { if (_first_group.Visible) UpdateLive(); };
            _ui_timer.Start();

            _first_group.Disposed += (s, e) =>
            {
                _apply_timer.Dispose();
                _ui_timer.Dispose();
                _buffer_timer.Dispose();
                _tips.Dispose();
            };
        }

        // the settings the Chip tab changes (copied back to the defaults by "Back to defaults")
        private static void CopyChipFields(MinitiounerSettings from, MinitiounerSettings to)
        {
            to.CarrierPhaseAlgo = from.CarrierPhaseAlgo;
            to.IqSwap = from.IqSwap;
            to.BasebandGainDb = from.BasebandGainDb;
            to.RefreshIntervalMs = from.RefreshIntervalMs;
            to.EqualizerDfe = from.EqualizerDfe;
            to.EqualizerFfe = from.EqualizerFfe;
            to.Loop1On = from.Loop1On;
            to.Loop2On = from.Loop2On;
            to.Algo2 = from.Algo2;
            to.IqDc = from.IqDc;
            to.IqAmplitude = from.IqAmplitude;
            to.IqQuadrature = from.IqQuadrature;
            to.NoiseData = from.NoiseData;
            to.NoisePlh = from.NoisePlh;
            to.ConstellationSource = from.ConstellationSource;
            to.ChipErrSource = from.ChipErrSource;
            to.ChipErrMode = from.ChipErrMode;
            to.ChipTsError = from.ChipTsError;
        }

        // A group box with a table for its rows, as high as the rows need; the groups stand in the order they are added.
        private static TableLayoutPanel AddGroup(Control parent, string title, out Control group)
        {
            var box = new CustomGroupBox();
            box.Dock = DockStyle.Top;
            box.Height = 120;
            box.Text = title;
            box.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point, (byte)0);
            box.Padding = new Padding(8, 7, 8, 8);

            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Top;
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 2;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            table.SizeChanged += (s, e) => box.Height = table.Height + box.Padding.Vertical + 28;

            box.Controls.Add(table);
            parent.Controls.Add(box);
            box.BringToFront();   // stacks the groups top to bottom in creation order

            group = box;
            return table;
        }

        private static void AddRow(TableLayoutPanel table, string caption, Control control)
        {
            int row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(3, 6, 3, 3) }, 0, row);
            table.Controls.Add(control, 1, row);
        }

        // a combo box for a setting; live = acts at once (LiveSettingsChanged), otherwise the chips are written again (SettingsChanged)
        private FlexComboBox MakeCombo(string[] items, Func<int> get, Action<int> set, bool live, int width, string tip)
        {
            var box = new FlexComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(3, 2, 8, 6) };
            box.Items.AddRange(items);

            // a narrow box still lists its entries in full
            int widest = width;
            foreach (string item in items)
                widest = Math.Max(widest, TextRenderer.MeasureText(item, box.Font).Width + 24);
            box.DropDownWidth = widest;

            box.SelectedIndexChanged += (s, e) =>
            {
                if (_loading)
                    return;
                set(box.SelectedIndex);
                if (live)
                    LiveChanged();
                else
                    Changed();
            };
            _loaders.Add(() => box.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, get())));
            _tips.SetToolTip(box, tip);
            return box;
        }

        private void BindCombo(TableLayoutPanel table, string caption, string[] items, Func<int> get, Action<int> set, string tip)
        {
            AddRow(table, caption, MakeCombo(items, get, set, false, 210, tip));
        }

        private void BindCheck(TableLayoutPanel table, string caption, string text, Func<bool> get, Action<bool> set, string tip)
        {
            BindCheck(table, caption, text, get, set, false, tip);
        }

        private void BindCheck(TableLayoutPanel table, string caption, string text, Func<bool> get, Action<bool> set, bool live, string tip)
        {
            AddRow(table, caption, MakeCheck(text, get, set, live, tip));
        }

        // One row of the I/Q readout: four cells of the same fixed width, so the columns of the rows one below the other stand exactly below each other (the
        // values are in a monospaced font and right aligned, a sign or a wider number does not move anything). cells = null: the header row.
        private Control IqReadoutRow(string[] texts, Label[] cells, string tip)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 0) };
            for (int n = 0; n < 4; n++)
            {
                bool header = cells == null;
                var cell = new Label
                {
                    Text = texts[n],
                    AutoSize = false,
                    Size = new Size(IqCellWidth, header ? 18 : 22),
                    TextAlign = ContentAlignment.MiddleRight,
                    Margin = new Padding(0, header ? 2 : 0, 0, header ? 0 : 3),
                };

                if (header)
                {
                    cell.ForeColor = Color.FromArgb(170, 170, 170);
                    cell.Font = new Font("Segoe UI Semibold", 8f);
                }
                else
                {
                    cell.Font = new Font("Consolas", 10f);
                    cells[n] = cell;
                }

                _tips.SetToolTip(cell, tip);
                row.Controls.Add(cell);
            }

            return row;
        }

        private const int IqCellWidth = 80;

        // a small caption above a control, for settings that stand side by side in one row
        private static Control Stack(string caption, Control control)
        {
            var stack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 4, 0) };
            stack.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(3, 0, 3, 0), ForeColor = Color.FromArgb(170, 170, 170), Font = new Font("Segoe UI Semibold", 8f) });
            stack.Controls.Add(control);
            return stack;
        }

        private FlexCheckBox MakeCheck(string text, Func<bool> get, Action<bool> set, bool live, string tip)
        {
            var box = new FlexCheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 6, 12, 6) };
            box.CheckedChanged += (s, e) =>
            {
                if (_loading)
                    return;
                set(box.Checked);
                if (live)
                    LiveChanged();
                else
                    Changed();
            };
            _loaders.Add(() => box.Checked = get());
            _tips.SetToolTip(box, tip);
            return box;
        }

        // a small caption in front of a control in a row
        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        }

        private void LiveChanged()
        {
            if (_loading)
                return;

            for (int i = 0; i < _event_has.Length; i++)
                _event_has[i] = false;

            LiveSettingsChanged?.Invoke();
        }

        // controls <- settings, without reporting a change
        private void LoadAll()
        {
            _loading = true;
            try
            {
                foreach (Action load in _loaders)
                    load();

                UpdateGainLabel();
            }
            finally
            {
                _loading = false;
            }
        }

        private void UpdateGainLabel()
        {
            _gain_label.Text = (_gain.Value * 2) + " dB";
        }

        private void Changed()
        {
            if (_loading)
                return;

            _apply_timer.Stop();
            _apply_timer.Start();
        }

        // The equalizer coefficients of a tuner's demodulator (null = not locked); any thread.
        public void UpdateEqualizer(int tuner, sbyte[] dfe, sbyte[] ffe)
        {
            if (tuner >= 0 && tuner < _equalizer.Length)
                _equalizer[tuner].SetCoefficients(dfe, ffe);
        }

        // The I/Q compensation the demodulator measures (null = not locked); any thread, shown by the timer.
        public void UpdateIq(int tuner, int[] values)
        {
            if (tuner >= 0 && tuner < _iq_values.Length)
                _iq_values[tuner] = values;
        }

        // The error counter of a tuner (NIM thread, any thread, shown by the timer): valid = false is an old value (the last one stays), locked = false clears it.
        public void UpdateEvent(int tuner, uint raw, bool valid, bool locked, double mclk_hz)
        {
            if (tuner < 0 || tuner >= _event_has.Length)
                return;

            _event_mclk = mclk_hz;
            if (!locked)
            {
                _event_has[tuner] = false;
            }
            else if (valid)
            {
                _event_raw[tuner] = raw;
                _event_has[tuner] = true;
            }
        }

        // I/Q and error counter readout, ten times a second
        private void UpdateLive()
        {
            for (int i = 0; i < _tuners; i++)
            {
                int[] v = _iq_values[i];
                string[] cells = v == null
                    ? new[] { "-", "-", "-", "-" }
                    : new[]
                    {
                        Math.Round(-v[0] / 2.0, MidpointRounding.AwayFromZero).ToString("+0;-0;+0"), Math.Round(-v[1] / 2.0, MidpointRounding.AwayFromZero).ToString("+0;-0;+0"),
                        (v[2] / 128.0).ToString("0.00"), (v[3] / 128.0).ToString("+0.00;-0.00;+0.00"),
                    };

                string raw = v == null
                    ? null
                    : "Raw: IDCCOMP 0x" + ((byte)v[0]).ToString("X2") + " (" + v[0] + "), QDCCOMP 0x" + ((byte)v[1]).ToString("X2") + " (" + v[1] +
                      "), AGC1AMM 0x" + ((byte)v[2]).ToString("X2") + ", AGC1QUAD 0x" + ((byte)v[3]).ToString("X2") + " (" + v[3] + ")";

                for (int n = 0; n < 4; n++)
                {
                    _iq_cells[i][n].Text = cells[n];
                    if (raw != _iq_raw_text[i])
                        _tips.SetToolTip(_iq_cells[i][n], _iq_read_tip + (raw == null ? "" : "\n" + raw));
                }

                _iq_raw_text[i] = raw;
            }

            for (int i = 0; i < _tuners; i++)
            {
                _event_label[i].Text = _event_has[i]
                    ? ErrorEvent.Format(_settings.ChipErrSource, _settings.ChipErrMode, _event_raw[i], _event_mclk) + "   (raw " + _event_raw[i] + ")"
                    : "-";
            }
        }
    }
}
