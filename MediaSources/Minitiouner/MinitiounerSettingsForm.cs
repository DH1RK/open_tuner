using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace opentuner.MediaSources.Minitiouner
{
    public partial class MinitiounerSettingsForm : Form
    {
        private MinitiounerSettings _settings;
        public MinitiounerSettingsForm(ref MinitiounerSettings Settings)
        {
            InitializeComponent();
            _settings = Settings;

            comboHardwareInterface.SelectedIndex = _settings.DefaultInterface;
            txtTuner1FreqOffset.Text = _settings.Offset1.ToString();
            txtTuner2FreqOffset.Text = _settings.Offset2.ToString();
            comboSupplyADefault.SelectedIndex = _settings.DefaultLnbASupply;
            comboSupplyBDefault.SelectedIndex = _settings.DefaultLnbBSupply;
            ComboDefaultRFInput.SelectedIndex = _settings.DefaultRFInput;

            checkEnableDigole.Checked = _settings.EnableDigoleDisplay;
            txtDigoleAddress.Text = _settings.DigoleI2cAddress.ToString("X2");
            txtDigoleCallsign.Text = _settings.DigoleCallsign;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            uint offset1 = 0;
            if (!uint.TryParse(txtTuner1FreqOffset.Text, out offset1))
            {
                MessageBox.Show("Invalid Offset 1");
                return;
            }

            uint offset2 = 0;
            if (!uint.TryParse(txtTuner2FreqOffset.Text, out offset2))
            {
                MessageBox.Show("Invalid Offset 2");
                return;
            }

            byte digoleAddress = 0;
            if (!byte.TryParse(txtDigoleAddress.Text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out digoleAddress))
            {
                MessageBox.Show("Invalid Digole I2C Address (hex, e.g. 27)");
                return;
            }

            _settings.DefaultInterface = (byte)comboHardwareInterface.SelectedIndex;
            _settings.DefaultLnbASupply = (byte)comboSupplyADefault.SelectedIndex;
            _settings.DefaultLnbBSupply = (byte)comboSupplyBDefault.SelectedIndex;
            _settings.DefaultRFInput = (byte)ComboDefaultRFInput.SelectedIndex;

            _settings.Offset1 = offset1;
            _settings.Offset2 = offset2;

            _settings.EnableDigoleDisplay = checkEnableDigole.Checked;
            _settings.DigoleI2cAddress = digoleAddress;
            _settings.DigoleCallsign = txtDigoleCallsign.Text.Trim().ToUpperInvariant();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
