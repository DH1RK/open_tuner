using System;
using System.Linq;
using System.Windows.Forms;

namespace opentuner.ExtraFeatures.BATCWebchat
{
    public partial class WebChatSettngsForm : Form
    {
        WebChatSettings _settings;
        public WebChatSettngsForm(ref WebChatSettings Settings)
        {
            InitializeComponent();
            DarkTheme.ApplyDialog(this);

            _settings = Settings;
            numChatFontSize.Value = _settings.chat_font_size;
            txtSigReportTemplate.Text = _settings.sigreport_template;
            txtNick.Text = _settings.nickname;
            checkAutoStart.Checked = _settings.gui_autostart;
            checkAutoLogin.Checked = _settings.gui_autologin;

            comboTranslateLanguage.DisplayMember = "Key";
            comboTranslateLanguage.ValueMember = "Value";
            foreach (var language in ChatTranslator.Languages)
                comboTranslateLanguage.Items.Add(language);
            comboTranslateLanguage.SelectedIndex = Math.Max(0, Array.FindIndex(ChatTranslator.Languages, l => l.Value == _settings.translate_language));
            // the default (user directory) when empty
            txtTranslateKeyFile.Text = ChatTranslator.KeyFilePath(_settings.translate_azure_key_file);
            UpdateKeyFileState();

            TextBox[] keyword_boxes = KeywordBoxes();
            for (int i = 0; i < keyword_boxes.Length; i++)
                keyword_boxes[i].Text = _settings.alert_keywords != null && i < _settings.alert_keywords.Length ? _settings.alert_keywords[i] : "";
        }

        // the five keyword fields of the keyword alert (#259)
        private TextBox[] KeywordBoxes()
        {
            return new[] { txtAlertKeyword1, txtAlertKeyword2, txtAlertKeyword3, txtAlertKeyword4, txtAlertKeyword5 };
        }

        private void txtTranslateKeyFile_TextChanged(object sender, EventArgs e)
        {
            UpdateKeyFileState();
        }

        private void btnTranslateKeyBrowse_Click(object sender, EventArgs e)
        {
            string current = ChatTranslator.KeyFilePath(txtTranslateKeyFile.Text);

            using (var dialog = new OpenFileDialog { Title = "File with the Azure Translator key", CheckFileExists = false, FileName = current })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    txtTranslateKeyFile.Text = dialog.FileName;
            }
        }

        // only says whether the file exists and has a line, never shows the key
        private void UpdateKeyFileState()
        {
            string path = ChatTranslator.KeyFilePath(txtTranslateKeyFile.Text);

            if (!System.IO.File.Exists(path))
            {
                labelTranslateKeyState.ForeColor = System.Drawing.Color.Firebrick;
                labelTranslateKeyState.Text = "File not found";
            }
            else if (ChatTranslator.ReadKey(path).Length == 0)
            {
                labelTranslateKeyState.ForeColor = System.Drawing.Color.Firebrick;
                labelTranslateKeyState.Text = "File found, but it is empty";
            }
            else
            {
                labelTranslateKeyState.ForeColor = System.Drawing.Color.SeaGreen;
                labelTranslateKeyState.Text = "Key file found";
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            _settings.chat_font_size = (int)numChatFontSize.Value;
            _settings.sigreport_template = txtSigReportTemplate.Text;
            _settings.nickname = txtNick.Text;
            _settings.gui_autostart = checkAutoStart.Checked;
            _settings.gui_autologin = checkAutoLogin.Checked;
            _settings.translate_language = ChatTranslator.Languages[Math.Max(0, comboTranslateLanguage.SelectedIndex)].Value;
            _settings.translate_azure_key_file = txtTranslateKeyFile.Text.Trim();

            // the keywords without empty ones, at most WebChatSettings.KeywordCount
            _settings.alert_keywords = KeywordBoxes().Select(box => box.Text.Trim()).Where(keyword => keyword.Length > 0).Take(WebChatSettings.KeywordCount).ToArray();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
