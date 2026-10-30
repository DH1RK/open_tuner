using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace opentuner
{
    public partial class settingsForm : Form
    {

        MainSettings _settings;
        public settingsForm(ref MainSettings settings, ISet<string> command_line = null)
        {
            InitializeComponent();
            DarkTheme.ApplyDialog(this);

            _settings = settings;
            BuildMainSettings();
            MarkCommandLine(command_line);

            // load settings
            comboDefaultSource.SelectedIndex = _settings.default_source;
            checkConnectAtStart.Checked = _settings.connect_at_start;
            checkNoSave.Checked = _settings.no_save_on_exit;
            checkLockWindow.Checked = _settings.gui_window_locked;
            checkStreamDeck.Checked = _settings.streamdeck_enabled;
            txtStreamDeckPort.Text = _settings.streamdeck_port.ToString();
            comboMediaPlayer1.SelectedIndex = _settings.mediaplayer_preferences[0];
            comboMediaPlayer2.SelectedIndex = _settings.mediaplayer_preferences[1];
            comboMediaPlayer3.SelectedIndex = _settings.mediaplayer_preferences[2];
            comboMediaPlayer4.SelectedIndex = _settings.mediaplayer_preferences[3];

            checkWindowed1.Checked = _settings.mediaplayer_windowed[0];
            checkWindowed2.Checked = _settings.mediaplayer_windowed[1];
            checkWindowed3.Checked = _settings.mediaplayer_windowed[2];
            checkWindowed4.Checked = _settings.mediaplayer_windowed[3];

            txtStreaming1IP.Text = _settings.streamer_udp_hosts[0];
            txtStreaming2IP.Text = _settings.streamer_udp_hosts[1];
            txtStreaming3IP.Text = _settings.streamer_udp_hosts[2];
            txtStreaming4IP.Text = _settings.streamer_udp_hosts[3];

            txtStreaming1Port.Text = _settings.streamer_udp_ports[0].ToString();
            txtStreaming2Port.Text = _settings.streamer_udp_ports[1].ToString();
            txtStreaming3Port.Text = _settings.streamer_udp_ports[2].ToString();
            txtStreaming4Port.Text = _settings.streamer_udp_ports[3].ToString();

            checkSteadyUdp1.Checked = _settings.streamer_udp_steady[0];
            checkSteadyUdp2.Checked = _settings.streamer_udp_steady[1];
            checkSteadyUdp3.Checked = _settings.streamer_udp_steady[2];
            checkSteadyUdp4.Checked = _settings.streamer_udp_steady[3];

            var steady_hint = new ToolTip { AutoPopDelay = 30000 };
            string steady_text = "Steady UDP stream: the stream is on from the start and stays on when the signal is lost or the tuner is stopped (Stop TS).\n" +
                                 "Data is sent only while a signal is received. Switch it off with the UDP button, the U key or a click on the U in the info line.\n" +
                                 "External players do not notice a change of the codec.";
            foreach (var steady_check in new[] { checkSteadyUdp1, checkSteadyUdp2, checkSteadyUdp3, checkSteadyUdp4 })
                steady_hint.SetToolTip(steady_check, steady_text);

            txtSnapshotPath.Text = _settings.media_path;
            txtVideoPath.Text = _settings.media_video_path;

            checkBoxMuted.Checked = _settings.mute_at_startup;
            checkBoxShowConsole.Checked = _settings.show_console_window;

            txtFfmpegPath.Text = _settings.ffmpeg_path;
            txtLibmpvPath.Text = _settings.libmpv_path;
        }

        private FlexCheckBox checkConnectAtStart;
        private FlexCheckBox checkNoSave;
        private FlexCheckBox checkLockWindow;
        private FlexCheckBox checkStreamDeck;
        private TextBox txtStreamDeckPort;

        // "Main Settings": everything about how OpenTuner starts and what it keeps, in one frame at the top (the frame "Media
        // Source" with the default source and the frame "Open Tuner Settings" are merged into it).
        private void BuildMainSettings()
        {
            groupBox2.Text = "Main Settings (Mute, Console: after restart; a command line option wins over these)";
            groupBox2.Location = new Point(16, 15);
            groupBox2.Size = new Size(892, 148);

            label18.Text = "Default source:";
            label18.Location = new Point(24, 40);
            groupBox2.Controls.Add(label18);
            comboDefaultSource.Location = new Point(140, 36);
            groupBox2.Controls.Add(comboDefaultSource);
            groupBox5.Visible = false;

            checkBoxMuted.Text = "Mute at startup";
            checkBoxMuted.Location = new Point(420, 38);
            checkBoxShowConsole.Text = "Show console window";
            checkBoxShowConsole.Location = new Point(620, 38);

            checkConnectAtStart = new FlexCheckBox { Text = "Connect the source at start", AutoSize = true, Location = new Point(24, 74) };
            checkNoSave = new FlexCheckBox { Text = "Do not save settings when closing", AutoSize = true, Location = new Point(300, 74) };
            checkLockWindow = new FlexCheckBox { Text = "Keep window size and position", AutoSize = true, Location = new Point(620, 74) };
            groupBox2.Controls.Add(checkConnectAtStart);
            groupBox2.Controls.Add(checkNoSave);
            groupBox2.Controls.Add(checkLockWindow);

            checkStreamDeck = new FlexCheckBox { Text = "Stream Deck plugin (restart needed), port:", AutoSize = true, Location = new Point(24, 110) };
            txtStreamDeckPort = new TextBox { Location = new Point(340, 108), Width = 70, MaxLength = 5 };
            groupBox2.Controls.Add(checkStreamDeck);
            groupBox2.Controls.Add(txtStreamDeckPort);

            var tips = new ToolTip { AutoPopDelay = 30000 };
            tips.SetToolTip(checkStreamDeck, "Switches on a server on 127.0.0.1 (this PC only) for the OpenTuner plugin of the Stream Deck software. " +
                                             "Read at the start of OpenTuner. If the port is used by another program the server does not start (see the log).");
            tips.SetToolTip(checkConnectAtStart, "Connects the selected source right after the start (command line: --autoconnect).");
            tips.SetToolTip(checkNoSave, "What changes while OpenTuner runs (window, tuner values, ...) is not saved when it closes (command line: --nosave). " +
                                         "The Save button of this dialog still saves.");
            tips.SetToolTip(checkLockWindow, "The window opens with the size and position it has when you tick this option and press Save. " +
                                             "Changes made during a session are not saved. Place the window first, then tick it.");

            // the frames below move down by what the main frame needs more
            int shift = groupBox2.Bottom + 7 - groupBox3.Top;
            foreach (Control c in new Control[] { groupBox3, groupBox1, groupBox4, btnSave, btnCancel })
                c.Top += shift;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + shift);
        }

        // What the command line gave for this start is orange; the value shown is the one in the settings (it is saved with the
        // other changes), the command line wins for this start.
        private void MarkCommandLine(ISet<string> command_line)
        {
            if (command_line == null || command_line.Count == 0)
                return;

            var tips = new ToolTip { AutoPopDelay = 30000 };
            var marks = new (string key, Control control, string option)[]
            {
                ("defaultsource", label18, "--defaultsource"), ("autoconnect", checkConnectAtStart, "--autoconnect"),
                ("nosave", checkNoSave, "--nosave"), ("window", checkLockWindow, "--windowwidth / --windowheight / --windowx / --windowy")
            };

            foreach (var mark in marks)
            {
                if (!command_line.Contains(mark.key))
                    continue;

                mark.control.ForeColor = MainForm.CommandLineColor;
                tips.SetToolTip(mark.control, "Command line option " + mark.option + " is active for this start and wins over this setting.");
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // save
            int streamingPort1 = 0;
            int streamingPort2 = 0;
            int streamingPort3 = 0;
            int streamingPort4 = 0;

            if (!int.TryParse(txtStreaming1Port.Text, out streamingPort1))
            {
                MessageBox.Show("Streaming Port 1 is invalid");
                return;
            }

            if (!int.TryParse(txtStreaming2Port.Text, out streamingPort2))
            {
                MessageBox.Show("Streaming Port 2 is invalid");
                return;
            }

            if (!int.TryParse(txtStreaming3Port.Text, out streamingPort3))
            {
                MessageBox.Show("Streaming Port 3 is invalid");
                return;
            }

            if (!int.TryParse(txtStreaming4Port.Text, out streamingPort4))
            {
                MessageBox.Show("Streaming Port 4 is invalid");
                return;
            }

            _settings.streamer_udp_ports[0]= streamingPort1;
            _settings.streamer_udp_ports[1] = streamingPort2;
            _settings.streamer_udp_ports[2] = streamingPort3;
            _settings.streamer_udp_ports[3] = streamingPort4;

            _settings.streamer_udp_hosts[0] = txtStreaming1IP.Text;
            _settings.streamer_udp_hosts[1] = txtStreaming2IP.Text;
            _settings.streamer_udp_hosts[2] = txtStreaming3IP.Text;
            _settings.streamer_udp_hosts[3] = txtStreaming4IP.Text;

            _settings.streamer_udp_steady[0] = checkSteadyUdp1.Checked;
            _settings.streamer_udp_steady[1] = checkSteadyUdp2.Checked;
            _settings.streamer_udp_steady[2] = checkSteadyUdp3.Checked;
            _settings.streamer_udp_steady[3] = checkSteadyUdp4.Checked;

            _settings.mediaplayer_preferences[0] = comboMediaPlayer1.SelectedIndex;
            _settings.mediaplayer_preferences[1] = comboMediaPlayer2.SelectedIndex;
            _settings.mediaplayer_preferences[2] = comboMediaPlayer3.SelectedIndex;
            _settings.mediaplayer_preferences[3] = comboMediaPlayer4.SelectedIndex;

            _settings.mediaplayer_windowed[0] = checkWindowed1.Checked;
            _settings.mediaplayer_windowed[1] = checkWindowed2.Checked;
            _settings.mediaplayer_windowed[2] = checkWindowed3.Checked;
            _settings.mediaplayer_windowed[3] = checkWindowed4.Checked;

            _settings.media_path = txtSnapshotPath.Text;
            _settings.media_video_path = txtVideoPath.Text;

            _settings.default_source = comboDefaultSource.SelectedIndex;
            _settings.connect_at_start = checkConnectAtStart.Checked;
            _settings.no_save_on_exit = checkNoSave.Checked;
            _settings.gui_window_locked = checkLockWindow.Checked;
            _settings.streamdeck_enabled = checkStreamDeck.Checked;
            if (int.TryParse(txtStreamDeckPort.Text, out int streamdeck_port) && streamdeck_port >= 1024 && streamdeck_port <= 65535)
                _settings.streamdeck_port = streamdeck_port;

            _settings.mute_at_startup = checkBoxMuted.Checked;
            // Takes effect on the next app start - AllocConsole() runs before Serilog is
            // configured in Program.cs, well before this dialog can even be shown.
            _settings.show_console_window = checkBoxShowConsole.Checked;

            // Both also only take effect on the next app start (see Program.cs - read once via
            // early_settings, before Engine.Start()/first libmpv P/Invoke call).
            _settings.ffmpeg_path = txtFfmpegPath.Text;
            _settings.libmpv_path = txtLibmpvPath.Text;

            this.DialogResult = DialogResult.OK;
            Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.SelectedPath = txtSnapshotPath.Text;

            if (fbd.ShowDialog() == DialogResult.OK )
            {
                txtSnapshotPath.Text = fbd.SelectedPath + "\\";
            }
        }

        private void btnBrowseFolder1_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.SelectedPath = txtVideoPath.Text;

            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtVideoPath.Text = fbd.SelectedPath + "\\";
            }
        }

        private void btnBrowseFfmpegPath_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.SelectedPath = txtFfmpegPath.Text;

            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtFfmpegPath.Text = fbd.SelectedPath + "\\";
            }
        }

        private void btnBrowseLibmpvPath_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.SelectedPath = txtLibmpvPath.Text;

            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtLibmpvPath.Text = fbd.SelectedPath + "\\";
            }
        }
    }
}
