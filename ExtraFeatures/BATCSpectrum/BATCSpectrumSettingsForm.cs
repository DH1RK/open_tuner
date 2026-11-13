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

        private readonly Func<double> sourceLnbLo;

        public BATCSpectrumSettingsForm(BATCSpectrumSettings _spectrumSettings, Func<double> _sourceLnbLo = null)
        {
            spectrumSettings = _spectrumSettings;
            sourceLnbLo = _sourceLnbLo;
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
            txtPlutoView.Text = spectrumSettings.plutoViewMhz.ToString("0.######", CultureInfo.InvariantCulture);
            txtPlutoLo.Text = spectrumSettings.plutoLnbLoMhz.ToString("0.######", CultureInfo.InvariantCulture);
            txtPlutoSpan.Text = spectrumSettings.plutoSpanMhz.ToString("0.###", CultureInfo.InvariantCulture);
            txtPlutoFrame.Text = spectrumSettings.plutoFrameMs.ToString("0.#", CultureInfo.InvariantCulture);
            checkFlatFloor.Checked = spectrumSettings.plutoFlatFloor;
            checkFollowRate.Checked = spectrumSettings.plutoFollowRate;
            txtOffsetLow.Text = spectrumSettings.plutoOffsetLowMhz.ToString("0.###", CultureInfo.InvariantCulture);
            txtOffsetHigh.Text = spectrumSettings.plutoOffsetHighMhz.ToString("0.###", CultureInfo.InvariantCulture);
            UpdateCentreInfo();
            txtPlutoAverage.Text = spectrumSettings.plutoAverageSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            txtSpectrumUrl.Text = spectrumSettings.spectrumUrl;
            txtRange.Text = spectrumSettings.rangeDb.ToString("0.#", CultureInfo.InvariantCulture);
        }

        // ---- the source of the spectrum (#215) ----------------------------------------------------------------------
        private CustomGroupBox groupSource;
        private Label labelSource, labelFallback, labelPlutoHost, labelPlutoView, labelPlutoLo, labelCentreInfo, labelPlutoSpan, labelPlutoFrame, labelPlutoAverage, labelUrl, labelRange;
        private FlexComboBox comboSource, comboFallback;
        private FlexCheckBox checkFlatFloor, checkFollowRate;
        private Label labelOffsetLow, labelOffsetHigh;
        private TextBox txtOffsetLow, txtOffsetHigh;
        private TextBox txtPlutoHost, txtPlutoView, txtPlutoLo, txtPlutoSpan, txtPlutoFrame, txtPlutoAverage, txtSpectrumUrl, txtRange;

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
            labelPlutoView = new Label { Text = "View centre, RF (MHz):", AutoSize = true };
            txtPlutoView = new TextBox { Width = 50, MaxLength = 10 };
            labelPlutoLo = new Label { Text = "LNB LO (MHz, 0 = source):", AutoSize = true };
            txtPlutoLo = new TextBox { Width = 50, MaxLength = 10 };
            labelCentreInfo = new Label { AutoSize = false, ForeColor = Color.Silver };
            labelPlutoSpan = new Label { Text = "Span (MHz):", AutoSize = true };
            txtPlutoSpan = new TextBox { Width = 60, MaxLength = 7 };
            labelPlutoFrame = new Label { Text = "VFW (ms):", AutoSize = true };
            txtPlutoFrame = new TextBox { Width = 50, MaxLength = 5 };
            checkFlatFloor = new FlexCheckBox { Text = "Flat noise floor", AutoSize = true };
            checkFollowRate = new FlexCheckBox { Text = "Follow the symbol rate, centre shift (MHz):", AutoSize = true };
            labelOffsetLow = new Label { Text = "up to 125 KS", AutoSize = true };
            txtOffsetLow = new TextBox { Width = 50, MaxLength = 5 };
            labelOffsetHigh = new Label { Text = "1000 KS and more", AutoSize = true };
            txtOffsetHigh = new TextBox { Width = 50, MaxLength = 5 };
            labelPlutoAverage = new Label { Text = "Average (s):", AutoSize = true };
            txtPlutoAverage = new TextBox { Width = 50, MaxLength = 6 };

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
            tips.SetToolTip(txtPlutoView, "QO-100 frequency (RF, MHz, decimals allowed) in the middle of the view. OpenTuner writes the centre frequency of the PlutoSky R2 " +
                                          "(this value minus the LNB LO) when the local bridge starts, 0 = leave the centre as it is. 10496 covers about 10491.6 to 10500 MHz.");
            tips.SetToolTip(txtPlutoLo, "LNB LO in MHz. 0 = the Freq Offset of tuner 1 in the Source Settings of the connected source (MiniTiouner, Longmynd, WinterHill), " +
                                        "9360 if no source is connected. Enter it here if the PlutoSky R2 is on another LNB.");
            tips.SetToolTip(txtPlutoSpan, "Span that OpenTuner writes to the PlutoSky R2 when the local bridge starts, 0 = leave it as it is. 16 MHz is what 500 KS gives " +
                                          "(the span follows the symbol rate of the transmitter and is changed again when that is set).");
            tips.SetToolTip(txtPlutoFrame, "Frame interval of the spectrometer of the PlutoSky R2 (VFW of its dashboard), 50 to 500 ms, 0 = leave it as it is. If the spectrometer refuses the interval (it did at a span of 16 MHz for more than 250 ms) the next shorter one that it takes is used. The spectrometer " +
                                           "averages over the interval, so a longer one calms the noise (measured: the noise per bin falls with the square root of the interval) " +
                                           "and needs less processing. OpenTuner writes it when the local bridge starts.");
            tips.SetToolTip(checkFlatFloor, "Every bin is measured against the noise around it (+-2 MHz). The tilt and the hump of the passband then do not push the noise " +
                                            "over the detection threshold of OpenTuner (small false signals). Switch it off to see the unprocessed shape.");
            tips.SetToolTip(checkFollowRate, "The span of the PlutoSky R2 follows the symbol rate of its transmitter and is only 4 to 6 MHz for up to 125 KS and for 1000 KS and more. " +
                                             "OpenTuner reads the symbol rate of the #datv page (dt/pluto/<call>/tx/dvbs2/sr) and shifts the centre frequency by the values here, " +
                                             "also when the rate is changed later. In between (250 to 500 KS) the centre stays.");
            tips.SetToolTip(txtPlutoAverage, "Averaging of the spectrum frames in seconds (0 to 10, decimals allowed, e.g. 0.5; 0 = off). The noise of the PlutoSky R2 is calmed down with it.");
            tips.SetToolTip(txtSpectrumUrl, "WebSocket of a bridge on another machine (ws:// or wss://), e.g. ws://raspberrypi:8096/wb/fft.");
            tips.SetToolTip(txtRange, "Vertical range of the spectrum in dB above the noise floor (5 to 60, default 15.6). A smaller value gives the signals more room, " +
                                      "the floor stays where it is. Valid for every source.");

            foreach (Control c in new Control[] { labelSource, comboSource, labelFallback, comboFallback, labelPlutoHost, txtPlutoHost, labelPlutoView, txtPlutoView, labelPlutoLo, txtPlutoLo, labelCentreInfo, labelPlutoSpan, txtPlutoSpan, labelPlutoFrame, txtPlutoFrame, checkFlatFloor, checkFollowRate, labelOffsetLow, txtOffsetLow, labelOffsetHigh, txtOffsetHigh,
                                                  labelPlutoAverage, txtPlutoAverage, labelUrl, txtSpectrumUrl, labelRange, txtRange })
                groupSource.Controls.Add(c);
            Controls.Add(groupSource);

            foreach (var box in new[] { txtPlutoView, txtPlutoLo, txtOffsetLow, txtOffsetHigh })
                box.TextChanged += (s, e) => UpdateCentreInfo();
            checkFollowRate.CheckedChanged += (s, e) => UpdateCentreInfo();
        }

        // Placed by what is there after the dark theme has changed the fonts (and with them the size of every control): below the
        // groups of the tuning, one after the other by the real width of the labels. The help text is cut to the height of the left
        // column (it scrolls).
        private void LayoutSourceGroup()
        {
            int LabelWidth(Label l) => TextRenderer.MeasureText(l.Text, l.Font).Width + 6;
            int column = 16 + Math.Max(LabelWidth(labelSource), Math.Max(LabelWidth(labelPlutoHost), Math.Max(LabelWidth(labelUrl), Math.Max(LabelWidth(labelRange), LabelWidth(labelPlutoSpan))))) + 8;
            int row_height = txtPlutoHost.Height + 12;
            int y0 = 30;

            // 20 px wider than the designer made it: the help text grows with the window and so does the group below
            ClientSize = new Size(ClientSize.Width + 20, ClientSize.Height);
            textModeHelp.Width += 20;
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
            labelPlutoView.Location = new Point(txtPlutoHost.Right + 24, y1 + 3);
            txtPlutoView.Location = new Point(labelPlutoView.Left + LabelWidth(labelPlutoView) + 4, y1);
            labelPlutoLo.Location = new Point(txtPlutoView.Right + 24, y1 + 3);
            txtPlutoLo.Location = new Point(labelPlutoLo.Left + LabelWidth(labelPlutoLo) + 4, y1);
            // the second row of the local bridge: the span, the spectrometer (VFW) and the averaging
            int y1b = y1 + row_height;
            labelPlutoSpan.Location = new Point(16, y1b + 3);
            txtPlutoSpan.Location = new Point(column, y1b);
            // VFW stands above "up to 125 KS" and Average above "1000 KS and more" (the places of the third row are worked out here)
            int x_low_label = 16 + checkFollowRate.Width + 12;
            int x_low_text = x_low_label + LabelWidth(labelOffsetLow) + 4;
            int x_high_label = x_low_text + txtOffsetLow.Width + 24;
            int x_high_text = x_high_label + LabelWidth(labelOffsetHigh) + 4;
            labelPlutoFrame.Location = new Point(x_low_label, y1b + 3);
            txtPlutoFrame.Location = new Point(x_low_text, y1b);
            labelPlutoAverage.Location = new Point(x_high_label, y1b + 3);
            txtPlutoAverage.Location = new Point(x_high_text, y1b);

            // the third row of the local bridge: the centre follows the symbol rate
            int y1c = y1b + row_height;
            checkFollowRate.Location = new Point(16, y1c + 2);
            labelOffsetLow.Location = new Point(x_low_label, y1c + 3);
            txtOffsetLow.Location = new Point(x_low_text, y1c);
            labelOffsetHigh.Location = new Point(x_high_label, y1c + 3);
            txtOffsetHigh.Location = new Point(x_high_text, y1c);

            // the information: what the centre of the PlutoSky R2 is and what it depends on
            int y1d = y1c + row_height;
            labelCentreInfo.Location = new Point(16, y1d);
            labelCentreInfo.Size = new Size(groupSource.Width - 32, TextRenderer.MeasureText("X", labelCentreInfo.Font).Height * 4 + 6);

            int y2 = labelCentreInfo.Bottom + 8;
            labelUrl.Location = new Point(16, y2 + 3);
            txtSpectrumUrl.Location = new Point(column, y2);

            int y3 = y2 + row_height;
            labelRange.Location = new Point(16, y3 + 3);
            txtRange.Location = new Point(column, y3);
            checkFlatFloor.Location = new Point(txtRange.Right + 24, y3 + 2);

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
            if (!ParseNumber(txtPlutoView.Text, out double pluto_view) || (pluto_view != 0 && (pluto_view < 10400 || pluto_view > 10600)))
            {
                MessageBox.Show("The view centre (QO-100 frequency, RF) must be 0 (leave the centre of the PlutoSky R2) or between 10400 and 10600 MHz");
                return;
            }
            if (!ParseNumber(txtPlutoLo.Text, out double pluto_lo) || (pluto_lo != 0 && (pluto_lo < 5000 || pluto_lo > 12000)))
            {
                MessageBox.Show("The LNB LO must be 0 (from the source) or between 5000 and 12000 MHz");
                return;
            }
            if (!ParseNumber(txtPlutoSpan.Text, out double pluto_span) || (pluto_span != 0 && (pluto_span < 4 || pluto_span > 40)))
            {
                MessageBox.Show("The span of the PlutoSky R2 must be 0 (leave it) or between 4 and 40 MHz");
                return;
            }
            if (!ParseNumber(txtOffsetLow.Text, out double offset_low) || offset_low < -10 || offset_low > 10
                || !ParseNumber(txtOffsetHigh.Text, out double offset_high) || offset_high < -10 || offset_high > 10)
            {
                MessageBox.Show("The centre shifts must be between -10 and 10 MHz");
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
            spectrumSettings.plutoViewMhz = pluto_view;
            spectrumSettings.plutoLnbLoMhz = pluto_lo;
            spectrumSettings.plutoSpanMhz = pluto_span;
            spectrumSettings.plutoFrameMs = pluto_frame;
            spectrumSettings.plutoFlatFloor = checkFlatFloor.Checked;
            spectrumSettings.plutoFollowRate = checkFollowRate.Checked;
            spectrumSettings.plutoOffsetLowMhz = offset_low;
            spectrumSettings.plutoOffsetHighMhz = offset_high;
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

        // What the centre of the PlutoSky R2 is and what it depends on, shown for safety (it is written to the device)
        private void UpdateCentreInfo()
        {
            if (labelCentreInfo == null || txtPlutoView == null)
                return;

            ParseNumber(txtPlutoView.Text, out double view);
            ParseNumber(txtPlutoLo.Text, out double lo_setting);
            ParseNumber(txtOffsetLow.Text, out double offset_low);
            ParseNumber(txtOffsetHigh.Text, out double offset_high);

            double lo;
            string lo_origin;
            double from_source = 0;
            try { from_source = sourceLnbLo != null ? sourceLnbLo() : 0; } catch (Exception) { }
            if (lo_setting > 0) { lo = lo_setting; lo_origin = "this field"; }
            else if (from_source > 1000) { lo = from_source; lo_origin = "Source Settings, Freq Offset of tuner 1"; }
            else { lo = 9360; lo_origin = "default 9360, no source connected"; }

            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            if (view <= 0)
            {
                labelCentreInfo.Text = "The centre of the PlutoSky R2 is not written (view centre 0): the centre stays as it is on the device, the LNB LO " + F(lo) + " MHz (" + lo_origin + ") is used to place the spectrum.";
                return;
            }

            double centre = view - lo;
            string text = "Centre of the PlutoSky R2 (IF): " + F(centre) + " MHz = view " + F(view) + " - LNB LO " + F(lo) + " (" + lo_origin + "). It covers about "
                          + F(view - 4.4) + " to " + F(view + 4.0) + " MHz (RF).";
            if (checkFollowRate != null && checkFollowRate.Checked)
                text += " By the symbol rate of the #datv page: up to 125 KS " + F(centre + offset_low) + ", 250 to 500 KS " + F(centre) + ", 1000 KS and more " + F(centre + offset_high) + " MHz.";
            text += " Depends on: the LNB LO, the view centre, the shifts and the symbol rate. It is written to the device when the local bridge starts.";
            labelCentreInfo.Text = text;
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
