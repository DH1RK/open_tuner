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
            txtPlutoSkyHost.Text = spectrumSettings.plutoSkyHost;
            txtPlutoSkyView.Text = spectrumSettings.plutoSkyViewMhz.ToString("0.######", CultureInfo.InvariantCulture);
            txtPlutoSkyLo.Text = spectrumSettings.plutoSkyLnbLoMhz.ToString("0.######", CultureInfo.InvariantCulture);
            txtIioUri.Text = spectrumSettings.iioUri;
            txtIioGain.Text = spectrumSettings.iioGainDb.ToString("0.#", CultureInfo.InvariantCulture);
            txtIioAverage.Text = spectrumSettings.iioAverageSeconds.ToString("0.##", CultureInfo.InvariantCulture);
            txtIioSmooth.Text = spectrumSettings.iioSmoothBins.ToString(CultureInfo.InvariantCulture);
            txtIioBlocks.Text = spectrumSettings.iioBlocksPerSecond.ToString("0.#", CultureInfo.InvariantCulture);
            checkIioDbb.Checked = spectrumSettings.iioShowDbb;
            checkFlatFloor.Checked = spectrumSettings.plutoSkyFlatFloor;
            checkFollowRate.Checked = spectrumSettings.plutoSkyFollowRate;
            txtOffsetLow.Text = spectrumSettings.plutoSkyOffsetLowMhz.ToString("0.###", CultureInfo.InvariantCulture);
            txtOffsetHigh.Text = spectrumSettings.plutoSkyOffsetHighMhz.ToString("0.###", CultureInfo.InvariantCulture);
            UpdateCentreInfo();
            txtSpectrumUrl.Text = spectrumSettings.spectrumUrl;
            txtRange.Text = spectrumSettings.rangeDb.ToString("0.#", CultureInfo.InvariantCulture);
        }

        // ---- the source of the spectrum (#215) ----------------------------------------------------------------------
        private CustomGroupBox groupSource;
        private Label headingGeneral, headingShared, headingR2, headingIio, headingBridge;
        private Label labelSource, labelFallback, labelPlutoSkyHost, labelPlutoSkyView, labelPlutoSkyLo, labelCentreInfo, labelUrl, labelRange;
        private Label labelIioUri, labelIioGain, labelIioAverage, labelIioSmooth, labelIioBlocks;
        private FlexComboBox comboSource, comboFallback;
        private FlexCheckBox checkFlatFloor, checkFollowRate, checkIioDbb;
        private Label labelOffsetLow, labelOffsetHigh;
        private TextBox txtOffsetLow, txtOffsetHigh;
        private TextBox txtPlutoSkyHost, txtPlutoSkyView, txtPlutoSkyLo, txtSpectrumUrl, txtRange;
        private TextBox txtIioUri, txtIioGain, txtIioAverage, txtIioSmooth, txtIioBlocks;

        private void BuildSourceGroup()
        {
            groupSource = new CustomGroupBox { Text = "Spectrum source (right click beside the bandplan bars: for this session)", Size = new Size(665, 168) };

            // what a part of the settings is for: every source, the PlutoSky R2 and the ADI Pluto together, or one of them
            headingGeneral = new Label { Text = "General (every source)", AutoSize = true };
            headingShared = new Label { Text = "PlutoSky R2 and ADI Pluto (both)", AutoSize = true };
            headingR2 = new Label { Text = "PlutoSky R2 (Tezuka, local bridge)", AutoSize = true };
            headingIio = new Label { Text = "ADI Pluto (libiio)", AutoSize = true };
            headingBridge = new Label { Text = "Other bridge (URL)", AutoSize = true };

            // general
            labelSource = new Label { Text = "Spectrum at start:", AutoSize = true };
            comboSource = new FlexComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            comboSource.Items.AddRange(new object[] { "Default (BATC server)", "Local bridge (PlutoSky R2)", "Other bridge (URL)", "ADI Pluto (libiio)" });
            labelFallback = new Label { Text = "Fallback:", AutoSize = true };
            comboFallback = new FlexComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            comboFallback.Items.AddRange(new object[] { "None", "Default (BATC server)", "Local bridge (PlutoSky R2)", "Other bridge (URL)", "ADI Pluto (libiio)" });
            labelRange = new Label { Text = "Range (dB):", AutoSize = true };
            txtRange = new TextBox { Width = 60, MaxLength = 5 };

            // shared by the PlutoSky R2 and the ADI Pluto: where the view is
            labelPlutoSkyView = new Label { Text = "View centre, RF (MHz):", AutoSize = true };
            txtPlutoSkyView = new TextBox { Width = 50, MaxLength = 10 };
            labelPlutoSkyLo = new Label { Text = "LNB LO (MHz, 0 = source):", AutoSize = true };
            txtPlutoSkyLo = new TextBox { Width = 50, MaxLength = 10 };
            checkFlatFloor = new FlexCheckBox { Text = "Flat noise floor", AutoSize = true };
            labelCentreInfo = new Label { AutoSize = false, ForeColor = Color.Silver };

            // PlutoSky R2
            labelPlutoSkyHost = new Label { Text = "Address:", AutoSize = true };
            txtPlutoSkyHost = new TextBox { Width = 130 };
            checkFollowRate = new FlexCheckBox { Text = "Follow the symbol rate, centre shift (MHz):", AutoSize = true };
            labelOffsetLow = new Label { Text = "up to 125 KS", AutoSize = true };
            txtOffsetLow = new TextBox { Width = 50, MaxLength = 5 };
            labelOffsetHigh = new Label { Text = "1000 KS and more", AutoSize = true };
            txtOffsetHigh = new TextBox { Width = 50, MaxLength = 5 };

            // ADI Pluto (libiio)
            labelIioUri = new Label { Text = "libiio address:", AutoSize = true };
            txtIioUri = new TextBox { Width = 130 };
            labelIioGain = new Label { Text = "Gain (dB):", AutoSize = true };
            txtIioGain = new TextBox { Width = 50, MaxLength = 6 };
            checkIioDbb = new FlexCheckBox { Text = "Show dBb", AutoSize = true };
            labelIioAverage = new Label { Text = "Average (s):", AutoSize = true };
            txtIioAverage = new TextBox { Width = 50, MaxLength = 6 };
            labelIioSmooth = new Label { Text = "Smoothing (0 to 3):", AutoSize = true };
            txtIioSmooth = new TextBox { Width = 30, MaxLength = 1 };
            labelIioBlocks = new Label { Text = "Blocks/s:", AutoSize = true };
            txtIioBlocks = new TextBox { Width = 40, MaxLength = 4 };

            // other bridge
            labelUrl = new Label { Text = "URL:", AutoSize = true };
            txtSpectrumUrl = new TextBox { Width = 440 };

            var tips = new ToolTip { AutoPopDelay = 30000 };
            tips.SetToolTip(comboSource, "Where the QO-100 spectrum comes from when OpenTuner starts: the BATC server, the PlutoSky R2 read by OpenTuner itself " +
                                         "(local bridge), a bridge on another machine (URL), or an ADI Pluto read over libiio. Without the BATC server the levels (dBb) are not shown " +
                                         "and there is no overpower; the ADI Pluto shows the dBb (switch in its section).");
            tips.SetToolTip(comboFallback, "The setup OpenTuner switches to when the first one gives no spectrum for 15 seconds, e.g. the local bridge if the BATC server is down. " +
                                           "It does not switch back by itself (right click beside the bandplan bars).");
            tips.SetToolTip(txtRange, "Vertical range of the spectrum in dB above the noise floor (5 to 60, default 15.6). A smaller value gives the signals more room, " +
                                      "the floor stays where it is. Valid for every source.");
            tips.SetToolTip(txtPlutoSkyView, "QO-100 frequency (RF, MHz, decimals allowed) in the middle of the view. OpenTuner writes the centre frequency of the PlutoSky R2 " +
                                          "(this value minus the LNB LO) when the local bridge starts, 0 = leave the centre as it is. 10496 covers about 10491.6 to 10500 MHz. " +
                                          "The ADI Pluto sets its receiver to the same centre (0 = 10495).");
            tips.SetToolTip(txtPlutoSkyLo, "LNB LO in MHz. 0 = the Freq Offset of tuner 1 in the Source Settings of the connected source (MiniTiouner, Longmynd, WinterHill), " +
                                        "9360 if no source is connected. Enter it here if the PlutoSky R2 or the ADI Pluto is on another LNB.");
            tips.SetToolTip(checkFlatFloor, "Every bin is measured against the noise around it (+-2 MHz). The tilt and the hump of the passband then do not push the noise " +
                                            "over the detection threshold of OpenTuner (small false signals). Switch it off to see the unprocessed shape. For the PlutoSky R2 and the ADI Pluto.");
            tips.SetToolTip(txtPlutoSkyHost, "Address of the PlutoSky R2 (Tezuka): the spectrum comes from ws://<address>/waterfall, centre and span over MQTT.");
            tips.SetToolTip(checkFollowRate, "The span of the PlutoSky R2 follows the symbol rate of its transmitter and is only 4 to 6 MHz for up to 125 KS and for 1000 KS and more. " +
                                             "OpenTuner reads the symbol rate of the #datv page (dt/pluto/<call>/tx/dvbs2/sr) and shifts the centre frequency by the values here, " +
                                             "also when the rate is changed later. In between (250 to 500 KS) the centre stays.");
            tips.SetToolTip(txtIioUri, "libiio address of the Pluto with the ADI firmware, e.g. ip:192.168.6.1 (the address of its USB network or LAN) or usb:... " +
                                       "It is read with libiio 1.0 (libiio\\libiio1.dll next to opentuner.exe). The receiver is set when the source starts: 16 MS/s, 16 MHz bandwidth, " +
                                       "LO = view centre minus LNB LO, manual gain. Nothing is restored when OpenTuner ends.");
            tips.SetToolTip(txtIioGain, "Hardware gain of the receiver in dB, manual (-1 to 73). The LNB amplifies a lot, so the gain of the Pluto mainly moves the level up or down; " +
                                        "20 dB is a good start. Keep it constant: the levels (dBb) only stay comparable with a fixed gain.");
            tips.SetToolTip(checkIioDbb, "Show the level of the signals relative to the beacon (dBb) at the signals. The ADI receiver is linear and sees the whole beacon. " +
                                         "Overpower is not evaluated with this source.");
            tips.SetToolTip(txtIioAverage, "Time constant of the average of the spectrum in seconds (0 to 10, decimals allowed; 0 = off). The device is read many times a second and every " +
                                           "block goes into the average, so a short time already gives a calm curve; a longer time calms it more but makes it slower.");
            tips.SetToolTip(txtIioSmooth, "Smoothing over the neighbouring frequencies: 0 = off, 1 = [1 2 1], 2 = [1 4 6 4 1] (about half the ripple of 1), 3 = more. Narrow signals " +
                                          "(33 KS, 66 KS) lose a little height with more smoothing.");
            tips.SetToolTip(txtIioBlocks, "How often the device is read, blocks per second (2 to 60). Every block is 4 ms of signal and 262 KB over the link, so 30 blocks are about 8 MB/s. " +
                                          "More blocks give a calmer curve with the same average time, but load the USB or network link of the Pluto.");
            tips.SetToolTip(txtSpectrumUrl, "WebSocket of a bridge on another machine (ws:// or wss://), replace <raspberry-pi> by the name or IP address of the machine, e.g. ws://raspberrypi:8096/wb/fft.");

            foreach (Control c in new Control[] { headingGeneral, headingShared, headingR2, headingIio, headingBridge,
                                                  labelSource, comboSource, labelFallback, comboFallback, labelRange, txtRange,
                                                  labelPlutoSkyView, txtPlutoSkyView, labelPlutoSkyLo, txtPlutoSkyLo, checkFlatFloor, labelCentreInfo,
                                                  labelPlutoSkyHost, txtPlutoSkyHost, checkFollowRate, labelOffsetLow, txtOffsetLow, labelOffsetHigh, txtOffsetHigh,
                                                  labelIioUri, txtIioUri, labelIioGain, txtIioGain, checkIioDbb, labelIioAverage, txtIioAverage, labelIioSmooth, txtIioSmooth, labelIioBlocks, txtIioBlocks,
                                                  labelUrl, txtSpectrumUrl })
                groupSource.Controls.Add(c);
            Controls.Add(groupSource);

            foreach (var box in new[] { txtPlutoSkyView, txtPlutoSkyLo, txtOffsetLow, txtOffsetHigh })
                box.TextChanged += (s, e) => UpdateCentreInfo();
            checkFollowRate.CheckedChanged += (s, e) => UpdateCentreInfo();
        }

        // Placed by what is there after the dark theme has changed the fonts (and with them the size of every control): below the
        // groups of the tuning, in sections (general, shared, PlutoSky R2, ADI Pluto, other bridge) one after the other by the real
        // width of the labels. The help text is cut to the height of the left column (it scrolls).
        private void LayoutSourceGroup()
        {
            // the headings: bold and orange like the labels in the spectrum, set after the theme
            foreach (var heading in new[] { headingGeneral, headingShared, headingR2, headingIio, headingBridge })
            {
                heading.Font = new Font(heading.Font, FontStyle.Bold);
                heading.ForeColor = Color.Orange;
            }

            int LabelWidth(Label l) => TextRenderer.MeasureText(l.Text, l.Font).Width + 6;
            int column = 16 + Math.Max(LabelWidth(labelSource), Math.Max(LabelWidth(labelPlutoSkyView), Math.Max(LabelWidth(labelPlutoSkyHost), Math.Max(LabelWidth(labelIioUri), Math.Max(LabelWidth(labelUrl), Math.Max(LabelWidth(labelRange), LabelWidth(labelIioAverage))))))) + 8;
            int row_height = txtPlutoSkyHost.Height + 8;
            int heading_height = headingGeneral.Height + 4;
            int gap = 6;

            // 20 px wider than the designer made it: the help text grows with the window and so does the group below
            ClientSize = new Size(ClientSize.Width + 20, ClientSize.Height);
            textModeHelp.Width += 20;
            textModeHelp.Height = groupOverpowerLayout.Bottom - textModeHelp.Top;
            groupSource.Location = new Point(groupOverpowerLayout.Left, groupOverpowerLayout.Bottom + 12);
            groupSource.Width = textModeHelp.Right - groupOverpowerLayout.Left;

            int y = 28;

            // general: valid for every source
            headingGeneral.Location = new Point(16, y);
            y += heading_height;
            labelSource.Location = new Point(16, y + 3);
            comboSource.Location = new Point(column, y);
            labelFallback.Location = new Point(comboSource.Right + 24, y + 3);
            comboFallback.Location = new Point(labelFallback.Left + LabelWidth(labelFallback) + 4, y);
            y += row_height;
            labelRange.Location = new Point(16, y + 3);
            txtRange.Location = new Point(column, y);
            y += row_height + gap;

            // the view, shared by the PlutoSky R2 and the ADI Pluto
            headingShared.Location = new Point(16, y);
            y += heading_height;
            labelPlutoSkyView.Location = new Point(16, y + 3);
            txtPlutoSkyView.Location = new Point(column, y);
            labelPlutoSkyLo.Location = new Point(txtPlutoSkyView.Right + 24, y + 3);
            txtPlutoSkyLo.Location = new Point(labelPlutoSkyLo.Left + LabelWidth(labelPlutoSkyLo) + 4, y);
            checkFlatFloor.Location = new Point(txtPlutoSkyLo.Right + 24, y + 2);
            y += row_height;
            // the information: what the centre is and what it depends on
            labelCentreInfo.Location = new Point(16, y);
            labelCentreInfo.Size = new Size(groupSource.Width - 32, TextRenderer.MeasureText("X", labelCentreInfo.Font).Height * 4 + 6);
            y = labelCentreInfo.Bottom + gap;

            // PlutoSky R2: the address and the centre that follows the symbol rate
            headingR2.Location = new Point(16, y);
            y += heading_height;
            labelPlutoSkyHost.Location = new Point(16, y + 3);
            txtPlutoSkyHost.Location = new Point(column, y);
            y += row_height;
            checkFollowRate.Location = new Point(16, y + 2);
            int x_low_label = 16 + checkFollowRate.Width + 12;
            int x_low_text = x_low_label + LabelWidth(labelOffsetLow) + 4;
            int x_high_label = x_low_text + txtOffsetLow.Width + 24;
            int x_high_text = x_high_label + LabelWidth(labelOffsetHigh) + 4;
            labelOffsetLow.Location = new Point(x_low_label, y + 3);
            txtOffsetLow.Location = new Point(x_low_text, y);
            labelOffsetHigh.Location = new Point(x_high_label, y + 3);
            txtOffsetHigh.Location = new Point(x_high_text, y);
            y += row_height + gap;

            // ADI Pluto (libiio)
            headingIio.Location = new Point(16, y);
            y += heading_height;
            labelIioUri.Location = new Point(16, y + 3);
            txtIioUri.Location = new Point(column, y);
            labelIioGain.Location = new Point(txtIioUri.Right + 24, y + 3);
            txtIioGain.Location = new Point(labelIioGain.Left + LabelWidth(labelIioGain) + 4, y);
            checkIioDbb.Location = new Point(txtIioGain.Right + 24, y + 2);
            y += row_height;
            labelIioAverage.Location = new Point(16, y + 3);
            txtIioAverage.Location = new Point(column, y);
            labelIioSmooth.Location = new Point(txtIioAverage.Right + 24, y + 3);
            txtIioSmooth.Location = new Point(labelIioSmooth.Left + LabelWidth(labelIioSmooth) + 4, y);
            labelIioBlocks.Location = new Point(txtIioSmooth.Right + 24, y + 3);
            txtIioBlocks.Location = new Point(labelIioBlocks.Left + LabelWidth(labelIioBlocks) + 4, y);
            y += row_height + gap;

            // other bridge
            headingBridge.Location = new Point(16, y);
            y += heading_height;
            labelUrl.Location = new Point(16, y + 3);
            txtSpectrumUrl.Location = new Point(column, y);
            y += row_height;

            groupSource.Height = y + 8;

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
            bool spectrum_url_set = BATCSpectrumSettings.UrlIsSet(spectrum_url);      // false for empty and for the placeholder <raspberry-pi>
            if (spectrum_url_set && (!Uri.TryCreate(spectrum_url, UriKind.Absolute, out Uri spectrum_uri) || (spectrum_uri.Scheme != "ws" && spectrum_uri.Scheme != "wss")))
            {
                MessageBox.Show("The URL of the other bridge is invalid. It must start with ws:// or wss://");
                return;
            }
            int spectrum_start = comboSource.SelectedIndex;
            int spectrum_fallback = comboFallback.SelectedIndex - 1;      // -1 = none
            if ((spectrum_start == SpectrumSetup.OtherBridge || spectrum_fallback == SpectrumSetup.OtherBridge) && !spectrum_url_set)
            {
                MessageBox.Show("The setup 'Other bridge' needs the address of the bridge: replace <raspberry-pi> in the URL by its name or IP address");
                return;
            }
            string plutoSkysky_host = txtPlutoSkyHost.Text.Trim();
            if ((spectrum_start == SpectrumSetup.LocalBridge || spectrum_fallback == SpectrumSetup.LocalBridge) && plutoSkysky_host.Length == 0)
            {
                MessageBox.Show("The setup 'Local bridge' needs the address of the PlutoSky R2");
                return;
            }
            if (!ParseNumber(txtPlutoSkyView.Text, out double plutoSkysky_view) || (plutoSkysky_view != 0 && (plutoSkysky_view < 10400 || plutoSkysky_view > 10600)))
            {
                MessageBox.Show("The view centre (QO-100 frequency, RF) must be 0 (leave the centre of the PlutoSky R2) or between 10400 and 10600 MHz");
                return;
            }
            if (!ParseNumber(txtPlutoSkyLo.Text, out double plutoSkysky_lo) || (plutoSkysky_lo != 0 && (plutoSkysky_lo < 5000 || plutoSkysky_lo > 12000)))
            {
                MessageBox.Show("The LNB LO must be 0 (from the source) or between 5000 and 12000 MHz");
                return;
            }
            string iio_uri = txtIioUri.Text.Trim();
            if ((spectrum_start == SpectrumSetup.IioDevice || spectrum_fallback == SpectrumSetup.IioDevice) && iio_uri.Length == 0)
            {
                MessageBox.Show("The setup 'ADI Pluto' needs the libiio address of the device, e.g. ip:192.168.6.1");
                return;
            }
            if (iio_uri.Length > 0 && iio_uri.IndexOf(':') < 0)
            {
                MessageBox.Show("The address of the ADI Pluto is a libiio address, e.g. ip:192.168.6.1 or usb:...");
                return;
            }
            if (!ParseNumber(txtIioGain.Text, out double iio_gain) || iio_gain < -1 || iio_gain > 73)
            {
                MessageBox.Show("The gain of the ADI Pluto must be between -1 and 73 dB");
                return;
            }
            if (!ParseNumber(txtIioAverage.Text, out double iio_average) || iio_average < 0 || iio_average > 10)
            {
                MessageBox.Show("The average of the ADI Pluto must be between 0 and 10 s");
                return;
            }
            if (!int.TryParse(txtIioSmooth.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int iio_smooth) || iio_smooth < 0 || iio_smooth > 3)
            {
                MessageBox.Show("The smoothing of the ADI Pluto must be a whole number from 0 to 3");
                return;
            }
            if (!ParseNumber(txtIioBlocks.Text, out double iio_blocks) || iio_blocks < 2 || iio_blocks > 60)
            {
                MessageBox.Show("The blocks per second of the ADI Pluto must be between 2 and 60");
                return;
            }
            if (!ParseNumber(txtOffsetLow.Text, out double offset_low) || offset_low < -10 || offset_low > 10
                || !ParseNumber(txtOffsetHigh.Text, out double offset_high) || offset_high < -10 || offset_high > 10)
            {
                MessageBox.Show("The centre shifts must be between -10 and 10 MHz");
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
            spectrumSettings.plutoSkyHost = plutoSkysky_host;
            spectrumSettings.plutoSkyViewMhz = plutoSkysky_view;
            spectrumSettings.plutoSkyLnbLoMhz = plutoSkysky_lo;
            spectrumSettings.iioUri = iio_uri;
            spectrumSettings.iioGainDb = iio_gain;
            spectrumSettings.iioAverageSeconds = iio_average;
            spectrumSettings.iioSmoothBins = iio_smooth;
            spectrumSettings.iioBlocksPerSecond = iio_blocks;
            spectrumSettings.iioShowDbb = checkIioDbb.Checked;
            spectrumSettings.plutoSkyFlatFloor = checkFlatFloor.Checked;
            spectrumSettings.plutoSkyFollowRate = checkFollowRate.Checked;
            spectrumSettings.plutoSkyOffsetLowMhz = offset_low;
            spectrumSettings.plutoSkyOffsetHighMhz = offset_high;
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
            if (labelCentreInfo == null || txtPlutoSkyView == null)
                return;

            ParseNumber(txtPlutoSkyView.Text, out double view);
            ParseNumber(txtPlutoSkyLo.Text, out double lo_setting);
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
                labelCentreInfo.Text = "The centre of the PlutoSky R2 is not written (view centre 0): the centre stays as it is on the device, the LNB LO " + F(lo) + " MHz (" + lo_origin + ") is used to place the spectrum. "
                                       + "The ADI Pluto takes 10495 MHz as the view then.";
                return;
            }

            double centre = view - lo;
            string text = "Centre of the PlutoSky R2 (IF): " + F(centre) + " MHz = view " + F(view) + " - LNB LO " + F(lo) + " (" + lo_origin + "). It covers about "
                          + F(view - 4.4) + " to " + F(view + 4.0) + " MHz (RF).";
            if (checkFollowRate != null && checkFollowRate.Checked)
                text += " By the symbol rate of the #datv page: up to 125 KS " + F(centre + offset_low) + ", 250 to 500 KS " + F(centre) + ", 1000 KS and more " + F(centre + offset_high) + " MHz.";
            text += " It is written to the PlutoSky R2 when the local bridge starts."
                    + " The ADI Pluto uses the same view and LNB LO: its receiver LO (" + F(centre) + " MHz) is set when it starts, without the shifts of the symbol rate.";
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
