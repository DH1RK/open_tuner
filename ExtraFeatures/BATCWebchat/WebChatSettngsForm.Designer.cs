namespace opentuner.ExtraFeatures.BATCWebchat
{
    partial class WebChatSettngsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.groupBox4 = new opentuner.Utilities.CustomGroupBox();
            this.txtNick = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label17 = new System.Windows.Forms.Label();
            this.numChatFontSize = new System.Windows.Forms.NumericUpDown();
            this.label14 = new System.Windows.Forms.Label();
            this.txtSigReportTemplate = new System.Windows.Forms.TextBox();
            this.label13 = new System.Windows.Forms.Label();
            this.btnSave = new opentuner.FlexButton();
            this.btnCancel = new opentuner.FlexButton();
            this.checkAutoStart = new opentuner.FlexCheckBox();
            this.checkAutoLogin = new opentuner.FlexCheckBox();
            this.labelTranslateLanguage = new System.Windows.Forms.Label();
            this.comboTranslateLanguage = new opentuner.FlexComboBox();
            this.labelTranslateKey = new System.Windows.Forms.Label();
            this.txtTranslateKeyFile = new System.Windows.Forms.TextBox();
            this.labelTranslateNote = new System.Windows.Forms.Label();
            this.btnTranslateKeyBrowse = new opentuner.FlexButton();
            this.labelTranslateKeyState = new System.Windows.Forms.Label();
            this.groupBox4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numChatFontSize)).BeginInit();
            this.SuspendLayout();
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.labelTranslateKeyState);
            this.groupBox4.Controls.Add(this.btnTranslateKeyBrowse);
            this.groupBox4.Controls.Add(this.labelTranslateNote);
            this.groupBox4.Controls.Add(this.txtTranslateKeyFile);
            this.groupBox4.Controls.Add(this.labelTranslateKey);
            this.groupBox4.Controls.Add(this.comboTranslateLanguage);
            this.groupBox4.Controls.Add(this.labelTranslateLanguage);
            this.groupBox4.Controls.Add(this.checkAutoLogin);
            this.groupBox4.Controls.Add(this.checkAutoStart);
            this.groupBox4.Controls.Add(this.txtNick);
            this.groupBox4.Controls.Add(this.label1);
            this.groupBox4.Controls.Add(this.label17);
            this.groupBox4.Controls.Add(this.numChatFontSize);
            this.groupBox4.Controls.Add(this.label14);
            this.groupBox4.Controls.Add(this.txtSigReportTemplate);
            this.groupBox4.Controls.Add(this.label13);
            this.groupBox4.Location = new System.Drawing.Point(16, 15);
            this.groupBox4.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox4.Size = new System.Drawing.Size(924, 320);
            this.groupBox4.TabIndex = 4;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "QO-100 Specific";
            // 
            // txtNick
            // 
            this.txtNick.Location = new System.Drawing.Point(363, 42);
            this.txtNick.Name = "txtNick";
            this.txtNick.Size = new System.Drawing.Size(286, 22);
            this.txtNick.TabIndex = 9;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(266, 46);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(71, 16);
            this.label1.TabIndex = 8;
            this.label1.Text = "Nickname:";
            // 
            // label17
            // 
            this.label17.AutoSize = true;
            this.label17.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label17.Location = new System.Drawing.Point(19, 46);
            this.label17.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label17.Name = "label17";
            this.label17.Size = new System.Drawing.Size(95, 16);
            this.label17.TabIndex = 7;
            this.label17.Text = "Chat Font Size:";
            // 
            // numChatFontSize
            // 
            this.numChatFontSize.Location = new System.Drawing.Point(132, 43);
            this.numChatFontSize.Margin = new System.Windows.Forms.Padding(4);
            this.numChatFontSize.Maximum = new decimal(new int[] {
            20,
            0,
            0,
            0});
            this.numChatFontSize.Minimum = new decimal(new int[] {
            6,
            0,
            0,
            0});
            this.numChatFontSize.Name = "numChatFontSize";
            this.numChatFontSize.ReadOnly = true;
            this.numChatFontSize.Size = new System.Drawing.Size(79, 22);
            this.numChatFontSize.TabIndex = 6;
            this.numChatFontSize.Value = new decimal(new int[] {
            12,
            0,
            0,
            0});
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label14.Location = new System.Drawing.Point(30, 129);
            this.label14.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(330, 112);
            this.label14.TabIndex = 5;
            this.label14.Text = "{SN} - ServiceName\r\n{SP} - ServiceProvider\r\n{DBM} - db Margin (D)\r\n{MER} - Mer\r\n{" +
    "SR} - Symbol Rate\r\n{VCODEC} - Video Codec\r\n{FREQ} - Freq\r\n{TSERR} - TS errors, last 10 s (MiniTiouner)";
            // 
            // txtSigReportTemplate
            // 
            this.txtSigReportTemplate.Location = new System.Drawing.Point(22, 103);
            this.txtSigReportTemplate.Margin = new System.Windows.Forms.Padding(4);
            this.txtSigReportTemplate.Name = "txtSigReportTemplate";
            this.txtSigReportTemplate.Size = new System.Drawing.Size(892, 22);
            this.txtSigReportTemplate.TabIndex = 4;
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label13.Location = new System.Drawing.Point(19, 83);
            this.label13.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(153, 16);
            this.label13.TabIndex = 3;
            this.label13.Text = "Signal Report Template:";
            // 
            // btnSave
            // 
            this.btnSave.Location = new System.Drawing.Point(840, 343);
            this.btnSave.Margin = new System.Windows.Forms.Padding(4);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(100, 28);
            this.btnSave.TabIndex = 5;
            this.btnSave.Text = "Save";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(732, 343);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(4);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 28);
            this.btnCancel.TabIndex = 6;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // checkAutoStart
            // 
            this.checkAutoStart.AutoSize = true;
            this.checkAutoStart.Location = new System.Drawing.Point(22, 277);
            this.checkAutoStart.Name = "checkAutoStart";
            this.checkAutoStart.Size = new System.Drawing.Size(110, 20);
            this.checkAutoStart.TabIndex = 10;
            this.checkAutoStart.Text = "Chat auto start";
            this.checkAutoStart.UseVisualStyleBackColor = true;
            // 
            // checkAutoLogin
            // 
            this.checkAutoLogin.AutoSize = true;
            this.checkAutoLogin.Location = new System.Drawing.Point(168, 277);
            this.checkAutoLogin.Name = "checkAutoLogin";
            this.checkAutoLogin.Size = new System.Drawing.Size(114, 20);
            this.checkAutoLogin.TabIndex = 11;
            this.checkAutoLogin.Text = "Chat auto login";
            this.checkAutoLogin.UseVisualStyleBackColor = true;
            //
            // labelTranslateLanguage
            //
            this.labelTranslateLanguage.AutoSize = true;
            this.labelTranslateLanguage.Location = new System.Drawing.Point(420, 133);
            this.labelTranslateLanguage.Name = "labelTranslateLanguage";
            this.labelTranslateLanguage.Size = new System.Drawing.Size(81, 16);
            this.labelTranslateLanguage.TabIndex = 12;
            this.labelTranslateLanguage.Text = "Translate to:";
            //
            // comboTranslateLanguage
            //
            this.comboTranslateLanguage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboTranslateLanguage.FormattingEnabled = true;
            this.comboTranslateLanguage.Location = new System.Drawing.Point(520, 130);
            this.comboTranslateLanguage.Name = "comboTranslateLanguage";
            this.comboTranslateLanguage.Size = new System.Drawing.Size(200, 24);
            this.comboTranslateLanguage.TabIndex = 13;
            //
            // labelTranslateKey
            //
            this.labelTranslateKey.AutoSize = true;
            this.labelTranslateKey.Location = new System.Drawing.Point(420, 169);
            this.labelTranslateKey.Name = "labelTranslateKey";
            this.labelTranslateKey.Size = new System.Drawing.Size(98, 16);
            this.labelTranslateKey.TabIndex = 14;
            this.labelTranslateKey.Text = "DeepL key file:";
            //
            // txtTranslateKeyFile
            //
            this.txtTranslateKeyFile.Location = new System.Drawing.Point(520, 166);
            this.txtTranslateKeyFile.Name = "txtTranslateKeyFile";
            this.txtTranslateKeyFile.Size = new System.Drawing.Size(296, 22);
            this.txtTranslateKeyFile.TabIndex = 15;
            this.txtTranslateKeyFile.TextChanged += new System.EventHandler(this.txtTranslateKeyFile_TextChanged);
            //
            // btnTranslateKeyBrowse
            //
            this.btnTranslateKeyBrowse.Location = new System.Drawing.Point(824, 164);
            this.btnTranslateKeyBrowse.Name = "btnTranslateKeyBrowse";
            this.btnTranslateKeyBrowse.Size = new System.Drawing.Size(90, 26);
            this.btnTranslateKeyBrowse.TabIndex = 17;
            this.btnTranslateKeyBrowse.Text = "Browse...";
            this.btnTranslateKeyBrowse.UseVisualStyleBackColor = true;
            this.btnTranslateKeyBrowse.Click += new System.EventHandler(this.btnTranslateKeyBrowse_Click);
            //
            // labelTranslateKeyState
            //
            this.labelTranslateKeyState.Location = new System.Drawing.Point(520, 194);
            this.labelTranslateKeyState.Name = "labelTranslateKeyState";
            this.labelTranslateKeyState.Size = new System.Drawing.Size(394, 18);
            this.labelTranslateKeyState.TabIndex = 18;
            //
            // labelTranslateNote
            //
            this.labelTranslateNote.Location = new System.Drawing.Point(420, 218);
            this.labelTranslateNote.Name = "labelTranslateNote";
            this.labelTranslateNote.Size = new System.Drawing.Size(494, 52);
            this.labelTranslateNote.TabIndex = 16;
            this.labelTranslateNote.Text = "The check box \"Translate\" in the chat window switches the translation on. The text of the chat messages is sent to DeepL. The file holds your own DeepL API key (one line, e.g. the key of DeepL API Free, see deepl.com/pro-api); it is read when a message is translated and is not stored in the settings.";
            //
            // WebChatSettngsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(960, 393);
            this.ControlBox = false;
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.groupBox4);
            this.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "WebChatSettngsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "QO-100 Web Chat Settings";
            this.groupBox4.ResumeLayout(false);
            this.groupBox4.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numChatFontSize)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private opentuner.Utilities.CustomGroupBox groupBox4;
        private System.Windows.Forms.Label label17;
        public System.Windows.Forms.NumericUpDown numChatFontSize;
        private System.Windows.Forms.Label label14;
        public System.Windows.Forms.TextBox txtSigReportTemplate;
        private System.Windows.Forms.Label label13;
        private opentuner.FlexButton btnSave;
        private opentuner.FlexButton btnCancel;
        private System.Windows.Forms.TextBox txtNick;
        private System.Windows.Forms.Label label1;
        private opentuner.FlexCheckBox checkAutoLogin;
        private opentuner.FlexCheckBox checkAutoStart;
        private System.Windows.Forms.Label labelTranslateLanguage;
        private opentuner.FlexComboBox comboTranslateLanguage;
        private System.Windows.Forms.Label labelTranslateKey;
        private System.Windows.Forms.TextBox txtTranslateKeyFile;
        private System.Windows.Forms.Label labelTranslateNote;
        private opentuner.FlexButton btnTranslateKeyBrowse;
        private System.Windows.Forms.Label labelTranslateKeyState;
    }
}