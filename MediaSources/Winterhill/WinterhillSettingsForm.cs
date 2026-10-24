using System;
using System.Windows.Forms;

namespace opentuner.MediaSources.WinterHill
{
    public partial class WinterHillSettingsForm : Form
    {
        private WinterHillSettings _settings;

        public WinterHillSettingsForm(WinterHillSettings Settings)
        {
            InitializeComponent();
            DarkTheme.ApplyDialog(this);

            _settings = Settings;

            comboDefaultInterface.SelectedIndex = _settings.DefaultInterface;
            txtWHWSIp.Text = _settings.WinterHillWSHost;
            txtWHWSPort.Text = _settings.WinterHillWSPort.ToString();
            txtWHWSBaseUdp.Text = _settings.WinterHillWSUdpBasePort.ToString();

            txtUDPBasePort.Text = _settings.WinterHillUdpBasePort.ToString();
            txtUDPIP.Text = _settings.WinterHillUdpHost.ToString();

            TextBox[] offset_boxes = FreqOffsetBoxes();
            for (int tuner = 0; tuner < offset_boxes.Length; tuner++)
                offset_boxes[tuner].Text = _settings.DefaultOffset[tuner].ToString();
        }

        private TextBox[] FreqOffsetBoxes()
        {
            return new TextBox[] { txtTuner1FreqOffset, txtTuner2FreqOffset, txtTuner3FreqOffset, txtTuner4FreqOffset };
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            int wsport = 0;
            int baseport = 0;

            int udpbaseport = 0;

            if (!int.TryParse(txtWHWSPort.Text, out wsport))
            {
                MessageBox.Show("WinterHill WS Port is not valid.");
                return;
            }

            if (!int.TryParse(txtWHWSBaseUdp.Text, out baseport))
            {
                MessageBox.Show("WinterHill WS Base Port is not valid");
                return;
            }

            if (!int.TryParse(txtUDPBasePort.Text, out udpbaseport))
            {
                MessageBox.Show("WinterHill UDP Base Port is not valid");
                return;
            }

            TextBox[] offset_boxes = FreqOffsetBoxes();
            uint[] offsets = new uint[offset_boxes.Length];

            for (int tuner = 0; tuner < offset_boxes.Length; tuner++)
            {
                if (!uint.TryParse(offset_boxes[tuner].Text, out offsets[tuner]))
                {
                    MessageBox.Show("Freq Offset of tuner " + (tuner + 1) + " is not valid.");
                    return;
                }
            }

            for (int tuner = 0; tuner < offsets.Length; tuner++)
                _settings.DefaultOffset[tuner] = offsets[tuner];

            _settings.WinterHillWSHost = txtWHWSIp.Text;
            _settings.WinterHillWSPort = wsport;
            _settings.WinterHillWSUdpBasePort = baseport;

            _settings.WinterHillUdpBasePort = udpbaseport;
            _settings.WinterHillUdpHost = txtUDPIP.Text;
            _settings.DefaultInterface = (byte)comboDefaultInterface.SelectedIndex;
            
            DialogResult = DialogResult.OK;
            Close();
        }

        private void comboDefaultInterface_SelectedIndexChanged(object sender, EventArgs e)
        {
           
        }

        private void btnBroadcastListener_Click(object sender, EventArgs e)
        {
            PicoWHBroadcastListenerForm picoWHBroadcastListenerForm = new PicoWHBroadcastListenerForm();
            picoWHBroadcastListenerForm.Show();
        }
    }
}
