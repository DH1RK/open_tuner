using System;
using System.Drawing;
using System.Windows.Forms;
using opentuner.Utilities;

namespace opentuner.MediaSources.Minitiouner
{
    // The "Chip" tab (issue #62): the receiver settings of MiniTioune's Extra Panel that open_tuner has itself, one group per
    // chip - STV0910 (demodulator: carrier loop 1 algorithm, I/Q swap) and STV6120 (tuner: baseband gain) - and a group of
    // software settings (status polling, fill level of the TS buffers). A change is reported 0.4 s after the last click
    // (SettingsChanged), so dragging the gain slider does not tune again at every step.
    public class ChipSettingsView
    {
        private const int ApplyDelayMs = 400;

        private readonly Control _first_group;
        private readonly RadioButton[] _algo = new RadioButton[3];
        private readonly CheckBox _iq_swap = new CheckBox();
        private readonly ComboBox _dfe = new ComboBox();
        private readonly ComboBox _ffe = new ComboBox();
        private readonly EqualizerControl[] _equalizer;
        private readonly TrackBar _gain = new TrackBar();
        private readonly Label _gain_label = new Label();
        private readonly RadioButton[] _refresh = new RadioButton[3];
        private readonly Label _buffer_label = new Label();
        private readonly Timer _apply_timer = new Timer();
        private readonly Timer _buffer_timer = new Timer();
        private readonly Func<int, int> _buffer_bytes;
        private readonly int _tuners;
        private bool _loading = false;

        private static readonly int[] RefreshChoices = { 125, 200, 300 };

        private static readonly string[] DfeChoices = { "off", "frozen", "very slow", "median", "fastest" };
        private static readonly string[] FfeChoices = { "frozen", "very slow", "median", "fastest" };

        // carrier algorithm (0 costas, 1 citroen 1, 2 citroen 2), I/Q swap, baseband gain in dB, status polling pause in ms,
        // equalizer DFE (0 off ... 4 fastest) and FFE (0 frozen ... 3 fastest)
        public event Action<byte, bool, int, int, int, int> SettingsChanged;

        public ChipSettingsView(string title, Control parent, int tuners, Func<int, int> buffer_bytes)
        {
            _tuners = tuners;
            _buffer_bytes = buffer_bytes;
            _equalizer = new EqualizerControl[tuners];

            var tips = new ToolTip();
            tips.ShowAlways = true;

            // ---- STV0910, the demodulator ----
            TableLayoutPanel demod = AddGroup(parent, "STV0910 (demodulator)", out _first_group);

            var algo_panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
            string[] algo_names = { "costas (MiniTioune)", "citroen 1", "citroen 2 (longmynd)" };
            for (int i = 0; i < _algo.Length; i++)
            {
                _algo[i] = new RadioButton { Text = algo_names[i], AutoSize = true, Tag = i };
                _algo[i].CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) Changed(); };
                algo_panel.Controls.Add(_algo[i]);
            }
            AddRow(demod, "Carrier loop 1,\nphase detector", algo_panel);
            tips.SetToolTip(algo_panel, "CARCFG.PH_DET_ALGO of carrier loop 1. MiniTioune uses costas, longmynd citroen 2.");

            _iq_swap.Text = "on";
            _iq_swap.AutoSize = true;
            _iq_swap.Margin = new Padding(3, 2, 3, 6);
            _iq_swap.CheckedChanged += (s, e) => Changed();
            AddRow(demod, "I/Q swap", _iq_swap);
            tips.SetToolTip(_iq_swap, "TNRCFG2.TUN_IQSWAP. On flips the sign of the carrier offset (CFR).");

            _dfe.DropDownStyle = ComboBoxStyle.DropDownList;
            _dfe.Items.AddRange(DfeChoices);
            _dfe.Width = 130;
            _dfe.Margin = new Padding(3, 2, 3, 6);
            _dfe.SelectedIndexChanged += (s, e) => Changed();
            AddRow(demod, "Equalizer, DFE", _dfe);
            tips.SetToolTip(_dfe, "EQUALCFG: the equalizer that removes echoes (cable reflections). Off stops and resets it, frozen keeps the learned coefficients, otherwise the speed of the adaption (MU_EQUALDFE): very slow = reset value and MiniTioune's setting.");

            _ffe.DropDownStyle = ComboBoxStyle.DropDownList;
            _ffe.Items.AddRange(FfeChoices);
            _ffe.Width = 130;
            _ffe.Margin = new Padding(3, 2, 3, 6);
            _ffe.SelectedIndexChanged += (s, e) => Changed();
            AddRow(demod, "Equalizer, FFE", _ffe);
            tips.SetToolTip(_ffe, "FFECFG: the equalizer that compensates filter and group delay errors. It always stays on (the data sheet says it has to); the speed of the adaption (MU_EQUALFFE): very slow = reset value and MiniTioune's setting.");

            for (int i = 0; i < tuners; i++)
            {
                _equalizer[i] = new EqualizerControl { Width = 270, Margin = new Padding(3, 2, 3, 8) };
                AddRow(demod, "Equalizer taps,\ntuner " + (i + 1), _equalizer[i]);
                tips.SetToolTip(_equalizer[i], "Coefficients of the DFE (8 taps) and the FFE (4 taps), I yellow, Q light blue. Bars near the middle line = nothing to correct.");
            }

            // ---- STV6120, the tuner ----
            TableLayoutPanel tuner = AddGroup(parent, "STV6120 (tuner)", out _);

            var gain_panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            _gain.Minimum = 0;
            _gain.Maximum = 8;
            _gain.TickFrequency = 1;
            _gain.LargeChange = 1;
            _gain.SmallChange = 1;
            _gain.Width = 190;
            _gain.Height = 32;
            _gain.ValueChanged += (s, e) => { UpdateGainLabel(); Changed(); };

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
            AddRow(tuner, "Baseband gain", gain_panel);
            tips.SetToolTip(_gain, "STV6120 CTRL2.BBGAIN in steps of 2 dB (MiniTioune 8 dB, longmynd 6 dB). Written at every tune.");

            // ---- software ----
            TableLayoutPanel software = AddGroup(parent, "Software", out _);

            var refresh_panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
            for (int i = 0; i < _refresh.Length; i++)
            {
                _refresh[i] = new RadioButton { Text = RefreshChoices[i] + " ms", AutoSize = true, Tag = i };
                _refresh[i].CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) Changed(); };
                refresh_panel.Controls.Add(_refresh[i]);
            }
            AddRow(software, "Status polling", refresh_panel);
            tips.SetToolTip(refresh_panel, "Pause between two reads of the status registers (refresh timing of MiniTioune). Shorter = livelier gauges, more I2C traffic; the measured refresh time is on the Expert tab.");

            _buffer_label.AutoSize = true;
            _buffer_label.Margin = new Padding(3, 4, 3, 6);
            _buffer_label.Text = "-";
            AddRow(software, "TS buffer", _buffer_label);
            tips.SetToolTip(_buffer_label, "Bytes waiting in the TS data queue of each tuner (before the players, recorders and the parser take them).");

            // ---- defaults and the note, below the groups ----
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 6, 8, 8) };

            var defaults = new Button { Text = "Back to defaults", AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
            defaults.Click += (s, e) =>
            {
                var d = new MinitiounerSettings();
                SetValues(d.CarrierPhaseAlgo, d.IqSwap, d.BasebandGainDb, d.RefreshIntervalMs, d.EqualizerDfe, d.EqualizerFfe);
                Changed();
            };
            bottom.Controls.Add(defaults);

            var note = new Label
            {
                Text = "A change takes effect at once: the chips are written again and both tuners of this board are tuned again, the picture is gone for a moment.",
                AutoSize = true,
                Margin = new Padding(3, 18, 3, 3),
                ForeColor = SystemColors.GrayText,
                Font = new Font("Microsoft Sans Serif", 8f),
            };
            bottom.Controls.Add(note);
            parent.SizeChanged += (s, e) => note.MaximumSize = new Size(Math.Max(120, parent.Width - 40), 0);

            parent.Controls.Add(bottom);
            bottom.BringToFront();

            _apply_timer.Interval = ApplyDelayMs;
            _apply_timer.Tick += (s, e) =>
            {
                _apply_timer.Stop();
                Report();
            };

            _buffer_timer.Interval = 500;
            _buffer_timer.Tick += (s, e) => { if (_first_group.Visible) UpdateBuffer(); };
            _buffer_timer.Start();

            _first_group.Disposed += (s, e) =>
            {
                _apply_timer.Dispose();
                _buffer_timer.Dispose();
            };
        }

        // A group box with a table for its rows, as high as the rows need; the groups stand in the order they are added.
        private static TableLayoutPanel AddGroup(Control parent, string title, out Control group)
        {
            var box = new CustomGroupBox();
            box.Dock = DockStyle.Top;
            box.Height = 120;
            box.Text = title;
            box.Font = new Font("Microsoft Sans Serif", 9.75F, FontStyle.Regular, GraphicsUnit.Point, (byte)0);
            box.Padding = new Padding(8, 7, 8, 8);

            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Top;
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 2;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
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

        // Sets the controls without reporting a change.
        public void SetValues(byte algo, bool iq_swap, int gain_db, int refresh_ms, int dfe, int ffe)
        {
            _loading = true;
            try
            {
                _dfe.SelectedIndex = Math.Max(0, Math.Min(DfeChoices.Length - 1, dfe));
                _ffe.SelectedIndex = Math.Max(0, Math.Min(FfeChoices.Length - 1, ffe));
                _algo[Math.Max(0, Math.Min(2, (int)algo))].Checked = true;
                _iq_swap.Checked = iq_swap;
                _gain.Value = Math.Max(0, Math.Min(8, gain_db / 2));

                int nearest = 0;
                for (int i = 1; i < RefreshChoices.Length; i++)
                {
                    if (Math.Abs(RefreshChoices[i] - refresh_ms) < Math.Abs(RefreshChoices[nearest] - refresh_ms))
                        nearest = i;
                }
                _refresh[nearest].Checked = true;

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

        private void Report()
        {
            byte algo = 0;
            for (int i = 0; i < _algo.Length; i++)
            {
                if (_algo[i].Checked)
                    algo = (byte)i;
            }

            int refresh = 200;
            for (int i = 0; i < _refresh.Length; i++)
            {
                if (_refresh[i].Checked)
                    refresh = RefreshChoices[i];
            }

            SettingsChanged?.Invoke(algo, _iq_swap.Checked, _gain.Value * 2, refresh, _dfe.SelectedIndex, _ffe.SelectedIndex);
        }

        // The equalizer coefficients of a tuner's demodulator (null = not locked).
        public void UpdateEqualizer(int tuner, sbyte[] dfe, sbyte[] ffe)
        {
            if (tuner >= 0 && tuner < _equalizer.Length)
                _equalizer[tuner].SetCoefficients(dfe, ffe);
        }

        private void UpdateBuffer()
        {
            var parts = new string[_tuners];
            for (int i = 0; i < _tuners; i++)
                parts[i] = "tuner " + (i + 1) + ":  " + _buffer_bytes(i).ToString("N0") + " bytes";

            _buffer_label.Text = string.Join("      ", parts);
        }
    }
}
