using System;
using System.Collections.Generic;
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

            checkUseIio.Checked = spectrumSettings.IsIioEnabled();
            checkUseBridge.Checked = spectrumSettings.IsBridgeEnabled();
            FillStartList(spectrumSettings.spectrumSetup);
            FillFallbackList(BATCSpectrumSettings.IsKnownSetup(spectrumSettings.spectrumFallback) ? spectrumSettings.spectrumFallback : NoFallback);
            UpdateSetupSections();
            txtView.Text = spectrumSettings.spectrumViewMhz.ToString("0.######", CultureInfo.InvariantCulture);
            txtLnbLo.Text = spectrumSettings.spectrumLnbLoMhz.ToString("0.######", CultureInfo.InvariantCulture);
            txtIioUri.Text = spectrumSettings.iioUri;
            txtIioGain.Text = spectrumSettings.iioGainDb.ToString("0.#", CultureInfo.InvariantCulture);
            txtIioAverage.Text = spectrumSettings.iioAverageSeconds.ToString("0.##", CultureInfo.InvariantCulture);
            txtIioSmooth.Text = spectrumSettings.iioSmoothBins.ToString(CultureInfo.InvariantCulture);
            txtIioBlocks.Text = spectrumSettings.iioBlocksPerSecond.ToString("0.#", CultureInfo.InvariantCulture);
            checkIioDbb.Checked = spectrumSettings.iioShowDbb;
            UpdateCentreInfo();
            txtSpectrumUrl.Text = spectrumSettings.spectrumUrl;
            txtRange.Text = spectrumSettings.rangeDb.ToString("0.#", CultureInfo.InvariantCulture);
            txtRangeIio.Text = spectrumSettings.RangeFor(SpectrumSetup.IioDevice).ToString("0.#", CultureInfo.InvariantCulture);
            txtRangeBridge.Text = spectrumSettings.RangeFor(SpectrumSetup.OtherBridge).ToString("0.#", CultureInfo.InvariantCulture);
        }

        // ---- the source of the spectrum (#215) ----------------------------------------------------------------------
        private CustomGroupBox groupSource;
        private Label headingGeneral, headingIio, headingBridge;
        private Label labelSource, labelFallback, labelView, labelLnbLo, labelCentreInfo, labelUrl, labelRange, labelRangeIio, labelRangeBridge;
        private Label labelIioUri, labelIioGain, labelIioAverage, labelIioSmooth, labelIioBlocks;
        private FlexComboBox comboSource, comboFallback;
        private FlexCheckBox checkIioDbb, checkUseIio, checkUseBridge;
        private TextBox txtView, txtLnbLo, txtSpectrumUrl, txtRange, txtRangeIio, txtRangeBridge;
        private TextBox txtIioUri, txtIioGain, txtIioAverage, txtIioSmooth, txtIioBlocks;

        // The lists offer the BATC server and the sources that are ticked "Use" (#242); the numbers of the setups are 0, 2 and 3 (1 is free, #220)
        private readonly List<int> startSetups = new List<int>();         // the setup of every entry of the start list

        private bool IsSetupUsed(int setup)
        {
            return setup == SpectrumSetup.IioDevice ? checkUseIio.Checked : setup == SpectrumSetup.OtherBridge ? checkUseBridge.Checked : true;
        }

        private int SelectedStart => comboSource.SelectedIndex >= 0 && comboSource.SelectedIndex < startSetups.Count ? startSetups[comboSource.SelectedIndex] : SpectrumSetup.Default;

        // `keep` is the start to select again if it is still offered, else the BATC server
        private void FillStartList(int keep)
        {
            startSetups.Clear();
            comboSource.Items.Clear();
            foreach (int setup in new[] { SpectrumSetup.Default, SpectrumSetup.OtherBridge, SpectrumSetup.IioDevice })
            {
                if (!IsSetupUsed(setup))
                    continue;
                startSetups.Add(setup);
                comboSource.Items.Add(SetupName(setup));
            }
            comboSource.SelectedIndex = Math.Max(0, startSetups.IndexOf(keep));
        }

        // a source was ticked or unticked: the lists follow, the start and the fallback stay if they are still offered
        private void UsedSourcesChanged()
        {
            int start = SelectedStart;
            int fallback = SelectedFallback;
            FillStartList(start);
            FillFallbackList(fallback);
            UpdateSetupSections();
        }

        private const int NoFallback = -1;
        private readonly List<int> fallbackSetups = new List<int>();      // the setup of every entry of the fallback list (NoFallback = "None")

        private static string SetupName(int setup)
        {
            return setup == SpectrumSetup.OtherBridge ? "Other bridge (URL)" : setup == SpectrumSetup.IioDevice ? "ADI Pluto (libiio)" : "Default (BATC server)";
        }

        // The fallback list offers None and every setup except the one chosen as start (#235); `keep` is the fallback to select again if it is still offered.
        private void FillFallbackList(int keep)
        {
            int start = SelectedStart;
            fallbackSetups.Clear();
            comboFallback.Items.Clear();
            fallbackSetups.Add(NoFallback);
            comboFallback.Items.Add("None");
            foreach (int setup in new[] { SpectrumSetup.Default, SpectrumSetup.OtherBridge, SpectrumSetup.IioDevice })
            {
                if (setup == start || !IsSetupUsed(setup))
                    continue;
                fallbackSetups.Add(setup);
                comboFallback.Items.Add(SetupName(setup));
            }
            comboFallback.SelectedIndex = Math.Max(0, fallbackSetups.IndexOf(keep));
        }

        private int SelectedFallback => comboFallback.SelectedIndex >= 0 && comboFallback.SelectedIndex < fallbackSetups.Count ? fallbackSetups[comboFallback.SelectedIndex] : NoFallback;

        // The section of a source is active while it is ticked "Use" (#242). The headings stay as they are (not greyed out), so it
        // is clear which "Use" belongs to which section.
        private void UpdateSetupSections()
        {
            bool use_iio = checkUseIio.Checked;
            bool use_bridge = checkUseBridge.Checked;

            foreach (Control c in new Control[] { labelIioUri, txtIioUri, labelIioGain, txtIioGain, checkIioDbb, labelView, txtView, labelLnbLo, txtLnbLo,
                                                  labelCentreInfo, labelIioAverage, txtIioAverage, labelIioSmooth, txtIioSmooth, labelIioBlocks, txtIioBlocks, labelRangeIio, txtRangeIio })
                c.Enabled = use_iio;

            foreach (Control c in new Control[] { labelUrl, txtSpectrumUrl, labelRangeBridge, txtRangeBridge })
                c.Enabled = use_bridge;
        }

        private void BuildSourceGroup()
        {
            groupSource = new CustomGroupBox { Text = "Spectrum source (right click beside the bandplan bars: for this session)", Size = new Size(665, 168) };

            // what a part of the settings is for: every source, or one of them
            headingGeneral = new Label { Text = "General (every source)", AutoSize = true };
            headingIio = new Label { Text = "ADI Pluto (libiio)", AutoSize = true };
            headingBridge = new Label { Text = "Other bridge (URL)", AutoSize = true };

            // general
            labelSource = new Label { Text = "Spectrum at start:", AutoSize = true };
            comboSource = new FlexComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            // the start and the fallback list are filled by FillStartList / FillFallbackList (the used sources)
            checkUseIio = new FlexCheckBox { Text = "Use", AutoSize = true };
            checkUseBridge = new FlexCheckBox { Text = "Use", AutoSize = true };
            checkUseIio.CheckedChanged += (s, e) => UsedSourcesChanged();
            checkUseBridge.CheckedChanged += (s, e) => UsedSourcesChanged();
            labelFallback = new Label { Text = "Fallback:", AutoSize = true };
            comboFallback = new FlexComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            // the fallback list is filled by FillFallbackList (without the setup of the start)
            comboSource.SelectedIndexChanged += (s, e) =>
            {
                FillFallbackList(SelectedFallback);
                UpdateSetupSections();
            };
            comboFallback.SelectedIndexChanged += (s, e) => UpdateSetupSections();
            labelRange = new Label { Text = "Range BATC (dB):", AutoSize = true };
            txtRange = new TextBox { Width = 50, MaxLength = 5 };
            labelRangeIio = new Label { Text = "ADI Pluto:", AutoSize = true };
            txtRangeIio = new TextBox { Width = 50, MaxLength = 5 };
            labelRangeBridge = new Label { Text = "Other bridge:", AutoSize = true };
            txtRangeBridge = new TextBox { Width = 50, MaxLength = 5 };

            // ADI Pluto (libiio)
            labelIioUri = new Label { Text = "libiio address:", AutoSize = true };
            txtIioUri = new TextBox { Width = 130 };
            labelIioGain = new Label { Text = "Gain (dB):", AutoSize = true };
            txtIioGain = new TextBox { Width = 50, MaxLength = 6 };
            checkIioDbb = new FlexCheckBox { Text = "Show dBb", AutoSize = true };
            labelView = new Label { Text = "View centre, RF (MHz):", AutoSize = true };
            txtView = new TextBox { Width = 50, MaxLength = 10 };
            labelLnbLo = new Label { Text = "LNB LO (MHz, 0 = source):", AutoSize = true };
            txtLnbLo = new TextBox { Width = 50, MaxLength = 10 };
            labelCentreInfo = new Label { AutoSize = false, ForeColor = Color.Silver };
            labelIioAverage = new Label { Text = "Average (s):", AutoSize = true };
            txtIioAverage = new TextBox { Width = 50, MaxLength = 6 };
            labelIioSmooth = new Label { Text = "Smoothing (0 to 5):", AutoSize = true };
            txtIioSmooth = new TextBox { Width = 30, MaxLength = 1 };
            labelIioBlocks = new Label { Text = "Blocks/s:", AutoSize = true };
            txtIioBlocks = new TextBox { Width = 40, MaxLength = 4 };

            // other bridge
            labelUrl = new Label { Text = "URL:", AutoSize = true };
            txtSpectrumUrl = new TextBox { Width = 440 };

            var tips = new ToolTip { AutoPopDelay = 30000 };
            tips.SetToolTip(comboSource, "Where the QO-100 spectrum comes from when OpenTuner starts: the BATC server, a bridge on another machine (URL), " +
                                         "or an ADI Pluto read over libiio. The ADI Pluto shows the dBb (switch in its section) and the overpower, " +
                                         "the other bridge neither.");
            tips.SetToolTip(comboFallback, "The setup OpenTuner switches to when the first one gives no spectrum for 15 seconds, e.g. the ADI Pluto if the BATC server is down. " +
                                           "It does not switch back by itself (right click beside the bandplan bars).");
            tips.SetToolTip(txtRange, "Vertical range of the spectrum in dB above the noise floor (5 to 60, default 15). A smaller value gives the signals more room, " +
                                      "the floor stays where it is. This is the value of the BATC server; the ADI Pluto and the other bridge have their own.");
            var other_range_tip = "Range (dB) of this source, like the one of the BATC server. The signals of a source stand at another height (noise floor, " +
                                  "antenna, LNB), so every source has its own value; the one of the source that is shown is used (also after a switch to the fallback). " +
                                  "Defaults: BATC server 15, ADI Pluto 16.4, other bridge 17.";
            tips.SetToolTip(txtRangeIio, other_range_tip);
            tips.SetToolTip(txtRangeBridge, other_range_tip);
            tips.SetToolTip(txtView, "QO-100 frequency (RF, MHz, decimals allowed) in the middle of the view of the ADI Pluto: its receiver LO is this value minus the LNB LO. " +
                                     "10496 covers about 10491.6 to 10500 MHz. 0 = 10495.");
            tips.SetToolTip(txtLnbLo, "LNB LO in MHz. 0 = the Freq Offset of tuner 1 in the Source Settings of the connected source (MiniTiouner, Longmynd, WinterHill), " +
                                      "9360 if no source is connected. Enter it here if the ADI Pluto is on another LNB.");
            tips.SetToolTip(txtIioUri, "libiio address of the Pluto with the ADI firmware: replace <pluto-ip> by its IP address, e.g. ip:192.168.6.1 (the address of its USB network or LAN), or enter usb:... " +
                                       "It is read with libiio 1.0 (libiio\\libiio1.dll next to opentuner.exe). The receiver is set when the source starts: 16 MS/s, 16 MHz bandwidth, " +
                                       "LO = view centre minus LNB LO, manual gain. Nothing is restored when OpenTuner ends.");
            tips.SetToolTip(txtIioGain, "Hardware gain of the receiver in dB, manual (-1 to 73). The LNB amplifies a lot, so the gain of the Pluto mainly moves the level up or down; " +
                                        "20 dB is a good start. Keep it constant: the levels (dBb) only stay comparable with a fixed gain.");
            tips.SetToolTip(checkIioDbb, "Show the level of the signals relative to the beacon (dBb) at the signals. The ADI receiver is linear and sees the whole beacon. " +
                                         "The overpower indicator works with this source (a signal above the beacon minus 0.75 dB).");
            tips.SetToolTip(txtIioAverage, "Time constant of the average of the spectrum in seconds (0 to 10, decimals allowed; 0 = off). The device is read many times a second and every " +
                                           "block goes into the average, so a short time already gives a calm curve; a longer time calms it more but makes it slower.");
            tips.SetToolTip(txtIioSmooth, "Smoothing over the neighbouring frequencies: 0 = off, 1 = [1 2 1], 2 = [1 4 6 4 1] (about half the ripple of 1), 3 to 5 = more. Narrow signals " +
                                          "(33 KS, 66 KS) lose a little height with more smoothing.");
            tips.SetToolTip(txtIioBlocks, "How often the device is read, blocks per second (2 to 60). Every block is 4 ms of signal and 262 KB over the link, so 30 blocks are about 8 MB/s. " +
                                          "More blocks give a calmer curve with the same average time, but load the USB or network link of the Pluto.");
            tips.SetToolTip(txtSpectrumUrl, "WebSocket of a bridge on another machine (ws:// or wss://), replace <rpi-ip> by the name or IP address of the machine, e.g. ws://raspberrypi:8096/wb/fft.");

            tips.SetToolTip(checkUseIio, "Use an ADI Pluto as a spectrum source: its settings are active, and it is offered at start, as fallback and in the right click menu.");
            tips.SetToolTip(checkUseBridge, "Use a bridge on another machine as a spectrum source: its settings are active, and it is offered at start, as fallback and in the right click menu.");

            foreach (Control c in new Control[] { headingGeneral, headingIio, headingBridge, checkUseIio, checkUseBridge,
                                                  labelSource, comboSource, labelFallback, comboFallback, labelRange, txtRange, labelRangeIio, txtRangeIio, labelRangeBridge, txtRangeBridge,
                                                  labelIioUri, txtIioUri, labelIioGain, txtIioGain, checkIioDbb,
                                                  labelView, txtView, labelLnbLo, txtLnbLo, labelCentreInfo,
                                                  labelIioAverage, txtIioAverage, labelIioSmooth, txtIioSmooth, labelIioBlocks, txtIioBlocks,
                                                  labelUrl, txtSpectrumUrl })
                groupSource.Controls.Add(c);
            Controls.Add(groupSource);

            foreach (var box in new[] { txtView, txtLnbLo })
                box.TextChanged += (s, e) => UpdateCentreInfo();
        }

        // Placed by what is there after the dark theme has changed the fonts (and with them the size of every control): below the
        // groups of the tuning, in sections (general, ADI Pluto, other bridge) one after the other by the real width of the labels.
        // The help text is cut to the height of the left column (it scrolls).
        private void LayoutSourceGroup()
        {
            // the headings: bold and orange like the labels in the spectrum, set after the theme
            foreach (var heading in new[] { headingGeneral, headingIio, headingBridge })
            {
                heading.Font = new Font(heading.Font, FontStyle.Bold);
                heading.ForeColor = Color.Orange;
            }

            int LabelWidth(Label l) => TextRenderer.MeasureText(l.Text, l.Font).Width + 6;
            int column = 16 + Math.Max(LabelWidth(labelSource), Math.Max(LabelWidth(labelView), Math.Max(LabelWidth(labelIioUri), Math.Max(LabelWidth(labelUrl), Math.Max(LabelWidth(labelRange), LabelWidth(labelIioAverage)))))) + 8;
            int row_height = txtIioUri.Height + 8;
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
            labelRangeIio.Location = new Point(txtRange.Right + 24, y + 3);
            txtRangeIio.Location = new Point(labelRangeIio.Left + LabelWidth(labelRangeIio) + 4, y);
            labelRangeBridge.Location = new Point(txtRangeIio.Right + 24, y + 3);
            txtRangeBridge.Location = new Point(labelRangeBridge.Left + LabelWidth(labelRangeBridge) + 4, y);
            y += row_height + gap;

            // ADI Pluto (libiio): the device, the view, the smoothing
            headingIio.Location = new Point(16, y);
            checkUseIio.Location = new Point(16 + LabelWidth(headingIio) + 16, y - 2);
            y += heading_height;
            labelIioUri.Location = new Point(16, y + 3);
            txtIioUri.Location = new Point(column, y);
            labelIioGain.Location = new Point(txtIioUri.Right + 24, y + 3);
            txtIioGain.Location = new Point(labelIioGain.Left + LabelWidth(labelIioGain) + 4, y);
            checkIioDbb.Location = new Point(txtIioGain.Right + 24, y + 2);
            y += row_height;
            labelView.Location = new Point(16, y + 3);
            txtView.Location = new Point(column, y);
            labelLnbLo.Location = new Point(txtView.Right + 24, y + 3);
            txtLnbLo.Location = new Point(labelLnbLo.Left + LabelWidth(labelLnbLo) + 4, y);
            y += row_height;
            // the information: what the receiver LO is and what it depends on
            labelCentreInfo.Location = new Point(16, y);
            labelCentreInfo.Size = new Size(groupSource.Width - 32, TextRenderer.MeasureText("X", labelCentreInfo.Font).Height * 2 + 6);
            y = labelCentreInfo.Bottom + gap;
            labelIioAverage.Location = new Point(16, y + 3);
            txtIioAverage.Location = new Point(column, y);
            labelIioSmooth.Location = new Point(txtIioAverage.Right + 24, y + 3);
            txtIioSmooth.Location = new Point(labelIioSmooth.Left + LabelWidth(labelIioSmooth) + 4, y);
            labelIioBlocks.Location = new Point(txtIioSmooth.Right + 24, y + 3);
            txtIioBlocks.Location = new Point(labelIioBlocks.Left + LabelWidth(labelIioBlocks) + 4, y);
            y += row_height + gap;

            // other bridge
            headingBridge.Location = new Point(16, y);
            checkUseBridge.Location = new Point(16 + LabelWidth(headingBridge) + 16, y - 2);
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
            bool spectrum_url_set = BATCSpectrumSettings.UrlIsSet(spectrum_url);      // false for empty and for the placeholder <rpi-ip> (also the old <raspberry-pi>)
            if (spectrum_url_set && (!Uri.TryCreate(spectrum_url, UriKind.Absolute, out Uri spectrum_uri) || (spectrum_uri.Scheme != "ws" && spectrum_uri.Scheme != "wss")))
            {
                MessageBox.Show("The URL of the other bridge is invalid. It must start with ws:// or wss://");
                return;
            }
            int spectrum_start = SelectedStart;
            int spectrum_fallback = SelectedFallback;      // -1 = none
            if ((spectrum_start == SpectrumSetup.OtherBridge || spectrum_fallback == SpectrumSetup.OtherBridge) && !spectrum_url_set)
            {
                MessageBox.Show("The setup 'Other bridge' needs the address of the bridge: replace <rpi-ip> in the URL by its name or IP address");
                return;
            }
            if (!ParseNumber(txtView.Text, out double view_mhz) || (view_mhz != 0 && (view_mhz < 10400 || view_mhz > 10600)))
            {
                MessageBox.Show("The view centre (QO-100 frequency, RF) must be 0 (10495 MHz) or between 10400 and 10600 MHz");
                return;
            }
            if (!ParseNumber(txtLnbLo.Text, out double lnb_lo_mhz) || (lnb_lo_mhz != 0 && (lnb_lo_mhz < 5000 || lnb_lo_mhz > 12000)))
            {
                MessageBox.Show("The LNB LO must be 0 (from the source) or between 5000 and 12000 MHz");
                return;
            }
            string iio_uri = txtIioUri.Text.Trim();
            bool iio_uri_set = BATCSpectrumSettings.IioUriIsSet(iio_uri);      // false for empty and for the placeholder ip:<pluto-ip>
            if ((spectrum_start == SpectrumSetup.IioDevice || spectrum_fallback == SpectrumSetup.IioDevice) && !iio_uri_set)
            {
                MessageBox.Show("The setup 'ADI Pluto' needs the libiio address of the device: replace <pluto-ip> in the address by its IP address, e.g. ip:192.168.6.1");
                return;
            }
            if (iio_uri_set && iio_uri.IndexOf(':') < 0)
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
            if (!int.TryParse(txtIioSmooth.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int iio_smooth) || iio_smooth < 0 || iio_smooth > 5)
            {
                MessageBox.Show("The smoothing of the ADI Pluto must be a whole number from 0 to 5");
                return;
            }
            if (!ParseNumber(txtIioBlocks.Text, out double iio_blocks) || iio_blocks < 2 || iio_blocks > 60)
            {
                MessageBox.Show("The blocks per second of the ADI Pluto must be between 2 and 60");
                return;
            }
            if (!ParseNumber(txtRange.Text, out double range_db) || range_db < 5 || range_db > 60)
            {
                MessageBox.Show("The spectrum range must be between 5 and 60 dB");
                return;
            }
            if (!ParseNumber(txtRangeIio.Text, out double range_iio_db) || range_iio_db < 5 || range_iio_db > 60)
            {
                MessageBox.Show("The range of the ADI Pluto must be between 5 and 60 dB");
                return;
            }
            if (!ParseNumber(txtRangeBridge.Text, out double range_bridge_db) || range_bridge_db < 5 || range_bridge_db > 60)
            {
                MessageBox.Show("The range of the other bridge must be between 5 and 60 dB");
                return;
            }

            spectrumSettings.useIio = checkUseIio.Checked;
            spectrumSettings.useBridge = checkUseBridge.Checked;
            spectrumSettings.spectrumSetup = spectrum_start;
            spectrumSettings.spectrumFallback = spectrum_fallback;
            spectrumSettings.spectrumUrl = spectrum_url;
            spectrumSettings.spectrumViewMhz = view_mhz;
            spectrumSettings.spectrumLnbLoMhz = lnb_lo_mhz;
            spectrumSettings.iioUri = iio_uri;
            spectrumSettings.iioGainDb = iio_gain;
            spectrumSettings.iioAverageSeconds = iio_average;
            spectrumSettings.iioSmoothBins = iio_smooth;
            spectrumSettings.iioBlocksPerSecond = iio_blocks;
            spectrumSettings.iioShowDbb = checkIioDbb.Checked;
            spectrumSettings.rangeDb = range_db;
            spectrumSettings.rangeIioDb = range_iio_db;
            spectrumSettings.rangeBridgeDb = range_bridge_db;

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

        // What the receiver LO of the ADI Pluto is and what it depends on, shown for safety (it is written to the device)
        private void UpdateCentreInfo()
        {
            if (labelCentreInfo == null || txtView == null)
                return;

            ParseNumber(txtView.Text, out double view);
            ParseNumber(txtLnbLo.Text, out double lo_setting);

            double lo;
            string lo_origin;
            double from_source = 0;
            try { from_source = sourceLnbLo != null ? sourceLnbLo() : 0; } catch (Exception) { }
            if (lo_setting > 0) { lo = lo_setting; lo_origin = "this field"; }
            else if (from_source > 1000) { lo = from_source; lo_origin = "Source Settings, Freq Offset of tuner 1"; }
            else { lo = 9360; lo_origin = "default 9360, no source connected"; }

            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            if (view <= 0)
                view = 10495;
            labelCentreInfo.Text = "Receiver LO of the ADI Pluto (IF): " + F(view - lo) + " MHz = view " + F(view) + " - LNB LO " + F(lo) + " (" + lo_origin + "). It covers about "
                                   + F(view - 4.4) + " to " + F(view + 4.0) + " MHz (RF). It is set when the source starts.";
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
