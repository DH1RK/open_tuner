using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using opentuner.Utilities;

namespace opentuner.ExtraFeatures.BATCSpectrum
{
    public partial class BATCSpectrumSettingsForm : Form
    {
        private const int TuneModeTimed = BATCSpectrumSettings.ModeAutoTimed;

        // how the tuning modes work, shown at the right of the settings
        private static readonly string ModeHelp = string.Join(Environment.NewLine, new[]
        {
            "A free signal is one that no other tuner is on (the signal of a manual tuner counts as taken). Only signals of at least the threshold are used. Once a second at most one tuner is switched, RX 1 first.",
            "",
            "Manual",
            "Nothing happens by itself.",
            "",
            "Auto (Hold): stay with the station",
            "- No signal yet (e.g. after the start): the free signal with the highest level.",
            "- The signal is there: the tuner stays on it.",
            "- The signal is gone: it waits for the Hold Time for the signal to come back, then takes the free signal nearest to its last frequency (up or down).",
            "- Never the beacon.",
            "",
            "Auto (Next new): stay, but look for new stations",
            "- Like Hold.",
            "- Also: after the Timed interval on the same signal it goes to the nearest free signal without callsign (not decoded yet), if there is one; otherwise it stays.",
            "- Never the beacon.",
            "",
            "Auto (Timed): step through the signals",
            "- No signal yet: the free signal with the lowest frequency.",
            "- Every Timed interval, or at once when the signal is gone: the next free signal above the current frequency; after the highest one it starts again at the lowest.",
            "- The beacon only with Avoid Beacon off."
        });

        private readonly BATCSpectrumSettings spectrumSettings;
        private readonly ComboBox[] tuneModes;
        private readonly CheckBox[] avoidBeacons;

        public BATCSpectrumSettingsForm(BATCSpectrumSettings _spectrumSettings)
        {
            spectrumSettings = _spectrumSettings;
            InitializeComponent();
            BuildSourceGroup();
            DarkTheme.ApplyDialog(this);
            LayoutSourceGroup();

            textModeHelp.Text = ModeHelp;

            tuneModes = new[] { tuneMode1, tuneMode2, tuneMode3, tuneMode4 };
            avoidBeacons = new[] { avoidBeacon1, avoidBeacon2, avoidBeacon3, avoidBeacon4 };

            for (int i = 0; i < tuneModes.Length; i++)
            {
                tuneModes[i].SelectedIndex = Math.Max(0, Math.Min(spectrumSettings.tuneMode[i], tuneModes[i].Items.Count - 1));
                avoidBeacons[i].Checked = spectrumSettings.avoidBeacon[i];
            }

            thresholdValue.Value = Math.Max(thresholdValue.Minimum, Math.Min(thresholdValue.Maximum, Convert.ToDecimal(spectrumSettings.threshold)));
            autoHoldTimeValue.Value = Math.Max(autoHoldTimeValue.Minimum, Math.Min(autoHoldTimeValue.Maximum, spectrumSettings.autoHoldTimeValue));
            autoTuneTimeValue.Value = Math.Max(autoTuneTimeValue.Minimum, Math.Min(autoTuneTimeValue.Maximum, spectrumSettings.autoTuneTimeValue));

            overPowerIndicatorLayout.SelectedIndex = Math.Max(0, Math.Min(spectrumSettings.overPowerIndicatorLayout, overPowerIndicatorLayout.Items.Count - 1));

            comboSource.SelectedIndex = Math.Max(0, Math.Min(spectrumSettings.spectrumSetup, comboSource.Items.Count - 1));
            comboFallback.SelectedIndex = Math.Max(0, Math.Min(spectrumSettings.spectrumFallback + 1, comboFallback.Items.Count - 1));
            txtPlutoHost.Text = spectrumSettings.plutoHost;
            txtPlutoCenter.Text = spectrumSettings.plutoCenterMhz.ToString("0.######", CultureInfo.InvariantCulture);
            txtPlutoSpan.Text = spectrumSettings.plutoSpanMhz.ToString("0.###", CultureInfo.InvariantCulture);
            txtPlutoFrame.Text = spectrumSettings.plutoFrameMs.ToString("0.#", CultureInfo.InvariantCulture);
            txtPlutoAverage.Text = spectrumSettings.plutoAverageSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            txtSpectrumUrl.Text = spectrumSettings.spectrumUrl;
            txtRange.Text = spectrumSettings.rangeDb.ToString("0.#", CultureInfo.InvariantCulture);
        }

        // ---- the source of the spectrum (#215) ----------------------------------------------------------------------
        private CustomGroupBox groupSource;
        private Label labelSource, labelFallback, labelPlutoHost, labelPlutoCenter, labelPlutoSpan, labelPlutoFrame, labelPlutoAverage, labelUrl, labelRange;
        private FlexComboBox comboSource, comboFallback;
        private TextBox txtPlutoHost, txtPlutoCenter, txtPlutoSpan, txtPlutoFrame, txtPlutoAverage, txtSpectrumUrl, txtRange;

        private void BuildSourceGroup()
        {
            groupSource = new CustomGroupBox { Text = "Spectrum source (right click beside the bandplan bars: for this session)", Size = new Size(665, 168) };

            labelSource = new Label { Text = "Spectrum at start:", AutoSize = true };
            comboSource = new FlexComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            comboSource.Items.AddRange(new object[] { "Default (BATC server)", "Local bridge (PlutoSky R2)", "Other bridge (URL)" });
            labelFallback = new Label { Text = "Fallback:", AutoSize = true };
            comboFallback = new FlexComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            comboFallback.Items.AddRange(new object[] { "None", "Default (BATC server)", "Local bridge (PlutoSky R2)", "Other bridge (URL)" });

            labelPlutoHost = new Label { Text = "Local bridge, PlutoSky R2:", AutoSize = true };
            txtPlutoHost = new TextBox { Width = 130 };
            labelPlutoCenter = new Label { Text = "Centre (MHz):", AutoSize = true };
            txtPlutoCenter = new TextBox { Width = 80, MaxLength = 10 };
            labelPlutoSpan = new Label { Text = "Span (MHz):", AutoSize = true };
            txtPlutoSpan = new TextBox { Width = 60, MaxLength = 7 };
            labelPlutoFrame = new Label { Text = "VFW (ms):", AutoSize = true };
            txtPlutoFrame = new TextBox { Width = 55, MaxLength = 5 };
            labelPlutoAverage = new Label { Text = "Average (s):", AutoSize = true };
            txtPlutoAverage = new TextBox { Width = 60, MaxLength = 6 };

            labelUrl = new Label { Text = "Other bridge URL:", AutoSize = true };
            txtSpectrumUrl = new TextBox { Width = 440 };

            labelRange = new Label { Text = "Range (dB):", AutoSize = true };
            txtRange = new TextBox { Width = 60, MaxLength = 5 };

            var tips = new ToolTip { AutoPopDelay = 30000 };
            tips.SetToolTip(comboSource, "Where the QO-100 spectrum comes from when OpenTuner starts: the BATC server, the PlutoSky R2 read by OpenTuner itself " +
                                         "(local bridge), or a bridge on another machine (URL). Without the BATC server the levels (dBb) are not shown and there is no overpower.");
            tips.SetToolTip(comboFallback, "The setup OpenTuner switches to when the first one gives no spectrum for 15 seconds, e.g. the local bridge if the BATC server is down. " +
                                           "It does not switch back by itself (right click beside the bandplan bars).");
            tips.SetToolTip(txtPlutoHost, "Address of the PlutoSky R2 (Tezuka): the spectrum comes from ws://<address>/waterfall, centre and span over MQTT.");
            tips.SetToolTip(txtPlutoCenter, "Centre frequency (IF, in MHz, decimals allowed, e.g. 1135.5) that OpenTuner writes to the PlutoSky R2 when the local bridge starts, " +
                                            "0 = leave it as it is. 1136 covers 10491.6 to 10499.5 MHz; the part of the beacon that is in view is enough.");
            tips.SetToolTip(txtPlutoSpan, "Span that OpenTuner writes to the PlutoSky R2 when the local bridge starts, 0 = leave it as it is. 16 MHz is what 500 kS gives " +
                                          "(the span follows the symbol rate of the transmitter and is changed again when that is set).");
            tips.SetToolTip(txtPlutoFrame, "Frame interval of the spectrometer of the PlutoSky R2 (VFW of its dashboard), 50 to 500 ms, 0 = leave it as it is. The spectrometer " +
                                           "averages over the interval, so a longer one calms the noise (measured: the noise per bin falls with the square root of the interval) " +
                                           "and needs less processing. OpenTuner writes it when the local bridge starts.");
            tips.SetToolTip(txtPlutoAverage, "Averaging of the spectrum frames in seconds (0 to 10, decimals allowed, e.g. 0.5; 0 = off). The noise of the PlutoSky R2 is calmed down with it.");
            tips.SetToolTip(txtSpectrumUrl, "WebSocket of a bridge on another machine (ws:// or wss://), e.g. ws://raspberrypi:8096/wb/fft.");
            tips.SetToolTip(txtRange, "Vertical range of the spectrum in dB above the noise floor (5 to 60, default 15.6). A smaller value gives the signals more room, " +
                                      "the floor stays where it is. Valid for every source.");

            foreach (Control c in new Control[] { labelSource, comboSource, labelFallback, comboFallback, labelPlutoHost, txtPlutoHost, labelPlutoCenter, txtPlutoCenter, labelPlutoSpan, txtPlutoSpan, labelPlutoFrame, txtPlutoFrame,
                                                  labelPlutoAverage, txtPlutoAverage, labelUrl, txtSpectrumUrl, labelRange, txtRange })
                groupSource.Controls.Add(c);
            Controls.Add(groupSource);
        }

        // Placed by what is there after the dark theme has changed the fonts (and with them the size of every control): below the
        // groups of the tuning, one after the other by the real width of the labels. The help text is cut to the height of the left
        // column (it scrolls).
        private void LayoutSourceGroup()
        {
            int LabelWidth(Label l) => TextRenderer.MeasureText(l.Text, l.Font).Width + 6;
            int column = 16 + Math.Max(LabelWidth(labelSource), Math.Max(LabelWidth(labelPlutoHost), Math.Max(LabelWidth(labelUrl), LabelWidth(labelRange)))) + 8;
            int row_height = txtPlutoHost.Height + 12;
            int y0 = 30;

            textModeHelp.Height = groupOverpowerLayout.Bottom - textModeHelp.Top;
            groupSource.Location = new Point(groupOverpowerLayout.Left, groupOverpowerLayout.Bottom + 12);
            groupSource.Width = textModeHelp.Right - groupOverpowerLayout.Left;

            labelSource.Location = new Point(16, y0 + 3);
            comboSource.Location = new Point(column, y0);
            labelFallback.Location = new Point(comboSource.Right + 24, y0 + 3);
            comboFallback.Location = new Point(labelFallback.Left + LabelWidth(labelFallback) + 4, y0);

            int y1 = y0 + row_height;
            labelPlutoHost.Location = new Point(16, y1 + 3);
            txtPlutoHost.Location = new Point(column, y1);
            labelPlutoCenter.Location = new Point(txtPlutoHost.Right + 24, y1 + 3);
            txtPlutoCenter.Location = new Point(labelPlutoCenter.Left + LabelWidth(labelPlutoCenter) + 4, y1);
            labelPlutoSpan.Location = new Point(txtPlutoCenter.Right + 24, y1 + 3);
            txtPlutoSpan.Location = new Point(labelPlutoSpan.Left + LabelWidth(labelPlutoSpan) + 4, y1);
            // the second row of the local bridge: the spectrometer (VFW) and the averaging
            int y1b = y1 + row_height;
            labelPlutoFrame.Location = new Point(16, y1b + 3);
            txtPlutoFrame.Location = new Point(column, y1b);
            labelPlutoAverage.Location = new Point(txtPlutoFrame.Right + 24, y1b + 3);
            txtPlutoAverage.Location = new Point(labelPlutoAverage.Left + LabelWidth(labelPlutoAverage) + 4, y1b);

            int y2 = y1b + row_height;
            labelUrl.Location = new Point(16, y2 + 3);
            txtSpectrumUrl.Location = new Point(column, y2);

            int y3 = y2 + row_height;
            labelRange.Location = new Point(16, y3 + 3);
            txtRange.Location = new Point(column, y3);

            groupSource.Height = txtRange.Bottom + 16;

            // below the new group
            btnCancel.Top = btnSave.Top = groupSource.Bottom + 12;
            ClientSize = new Size(ClientSize.Width, btnSave.Bottom + 14);
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            // the source of the spectrum, checked first
            string spectrum_url = txtSpectrumUrl.Text.Trim();
            if (spectrum_url.Length > 0 && (!Uri.TryCreate(spectrum_url, UriKind.Absolute, out Uri spectrum_uri) || (spectrum_uri.Scheme != "ws" && spectrum_uri.Scheme != "wss")))
            {
                MessageBox.Show("The URL of the other bridge is invalid. It must start with ws:// or wss://");
                return;
            }
            int spectrum_start = comboSource.SelectedIndex;
            int spectrum_fallback = comboFallback.SelectedIndex - 1;      // -1 = none
            if ((spectrum_start == SpectrumSetup.OtherBridge || spectrum_fallback == SpectrumSetup.OtherBridge) && spectrum_url.Length == 0)
            {
                MessageBox.Show("The setup 'Other bridge' needs a URL");
                return;
            }
            string pluto_host = txtPlutoHost.Text.Trim();
            if ((spectrum_start == SpectrumSetup.LocalBridge || spectrum_fallback == SpectrumSetup.LocalBridge) && pluto_host.Length == 0)
            {
                MessageBox.Show("The setup 'Local bridge' needs the address of the PlutoSky R2");
                return;
            }
            if (!ParseNumber(txtPlutoCenter.Text, out double pluto_center) || (pluto_center != 0 && (pluto_center < 1000 || pluto_center > 1300)))
            {
                MessageBox.Show("The centre frequency of the PlutoSky R2 must be 0 (leave it) or between 1000 and 1300 MHz");
                return;
            }
            if (!ParseNumber(txtPlutoSpan.Text, out double pluto_span) || (pluto_span != 0 && (pluto_span < 4 || pluto_span > 40)))
            {
                MessageBox.Show("The span of the PlutoSky R2 must be 0 (leave it) or between 4 and 40 MHz");
                return;
            }
            if (!ParseNumber(txtPlutoFrame.Text, out double pluto_frame) || (pluto_frame != 0 && (pluto_frame < 50 || pluto_frame > 500)))
            {
                MessageBox.Show("The VFW (frame interval) of the PlutoSky R2 must be 0 (leave it) or between 50 and 500 ms");
                return;
            }
            if (!ParseNumber(txtPlutoAverage.Text, out double pluto_average) || pluto_average < 0 || pluto_average > 10)
            {
                MessageBox.Show("The averaging must be between 0 and 10 s");
                return;
            }
            if (!ParseNumber(txtRange.Text, out double range_db) || range_db < 5 || range_db > 60)
            {
                MessageBox.Show("The spectrum range must be between 5 and 60 dB");
                return;
            }

            spectrumSettings.spectrumSetup = spectrum_start;
            spectrumSettings.spectrumFallback = spectrum_fallback;
            spectrumSettings.spectrumUrl = spectrum_url;
            spectrumSettings.plutoHost = pluto_host;
            spectrumSettings.plutoCenterMhz = pluto_center;
            spectrumSettings.plutoSpanMhz = pluto_span;
            spectrumSettings.plutoFrameMs = pluto_frame;
            spectrumSettings.plutoAverageSeconds = pluto_average;
            spectrumSettings.rangeDb = range_db;

            for (int i = 0; i < tuneModes.Length; i++)
            {
                spectrumSettings.tuneMode[i] = tuneModes[i].SelectedIndex;
                spectrumSettings.avoidBeacon[i] = avoidBeacons[i].Checked;
            }

            spectrumSettings.threshold = Convert.ToSingle(thresholdValue.Value);
            spectrumSettings.autoHoldTimeValue = Convert.ToInt32(autoHoldTimeValue.Value);
            spectrumSettings.autoTuneTimeValue = Convert.ToInt32(autoTuneTimeValue.Value);

            spectrumSettings.overPowerIndicatorLayout = overPowerIndicatorLayout.SelectedIndex;

            DialogResult = DialogResult.OK;
            Close();
        }

        // a number with a point or a comma
        private static bool ParseNumber(string text, out double value)
        {
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // "Avoid Beacon" can only be chosen for Auto (Timed): Manual does not tune, the other Auto modes never go to the beacon
        private void tuneMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = Array.IndexOf(tuneModes ?? new FlexComboBox[0], sender);
            if (index < 0)
                return;

            int mode = tuneModes[index].SelectedIndex;
            avoidBeacons[index].Visible = mode == TuneModeTimed;

            if (mode != TuneModeTimed)
                avoidBeacons[index].Checked = mode != BATCSpectrumSettings.ModeManual;
        }
    }
}
