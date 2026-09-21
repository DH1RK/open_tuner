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
            txtTuner1FreqCorrection.Text = PpmAt(_settings.FreqCorrectionPpm, 0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            txtTuner2FreqCorrection.Text = PpmAt(_settings.FreqCorrectionPpm, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            comboSupplyADefault.SelectedIndex = _settings.DefaultLnbASupply;
            comboSupplyBDefault.SelectedIndex = _settings.DefaultLnbBSupply;
            ComboDefaultRFInput.SelectedIndex = _settings.DefaultRFInput;

            checkEnableDigole.Checked = _settings.EnableDigoleDisplay;
            txtDigoleAddress.Text = _settings.DigoleI2cAddress.ToString("X2");
            txtDigoleCallsign.Text = _settings.DigoleCallsign;
            txtDigoleLocator.Text = _settings.DigoleLocator;
            txtDigoleName.Text = _settings.DigoleName;
        }

        private static double PpmAt(double[] values, int index)
        {
            return values != null && values.Length > index ? values[index] : 0;
        }

        private static int At(int[] values, int index)
        {
            return values != null && values.Length > index ? values[index] : 0;
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

            // reference error correction of the tuner in ppm (same range as the slider on the Special tab)
            double correction1 = 0, correction2 = 0;
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(txtTuner1FreqCorrection.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, invariant, out correction1) || correction1 < -250 || correction1 > 250)
            {
                MessageBox.Show("Invalid Tuner 1 Correction (-250 .. 250 ppm)");
                return;
            }

            if (!double.TryParse(txtTuner2FreqCorrection.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, invariant, out correction2) || correction2 < -250 || correction2 > 250)
            {
                MessageBox.Show("Invalid Tuner 2 Correction (-250 .. 250 ppm)");
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

            _settings.FreqCorrectionPpm = new double[] { correction1, correction2 };


            _settings.EnableDigoleDisplay = checkEnableDigole.Checked;
            _settings.DigoleI2cAddress = digoleAddress;
            _settings.DigoleCallsign = txtDigoleCallsign.Text.Trim().ToUpperInvariant();
            _settings.DigoleLocator = txtDigoleLocator.Text.Trim().ToUpperInvariant();
            _settings.DigoleName = txtDigoleName.Text.Trim();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
