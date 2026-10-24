namespace opentuner.MediaSources.WinterHill
{
    partial class WinterHillSettingsForm
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
            this.groupBox1 = new opentuner.Utilities.CustomGroupBox();
            this.label2 = new System.Windows.Forms.Label();
            this.txtWHWSPort = new System.Windows.Forms.TextBox();
            this.btnSave = new opentuner.FlexButton();
            this.btnCancel = new opentuner.FlexButton();
            this.groupBox2 = new opentuner.Utilities.CustomGroupBox();
            this.btnBroadcastListener = new opentuner.FlexButton();
            this.comboDefaultInterface = new opentuner.FlexComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.txtWHWSIp = new System.Windows.Forms.TextBox();
            this.groupBox3 = new opentuner.Utilities.CustomGroupBox();
            this.label5 = new System.Windows.Forms.Label();
            this.txtUDPIP = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.txtWHWSBaseUdp = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.txtUDPBasePort = new System.Windows.Forms.TextBox();
            this.groupFreqOffsets = new opentuner.Utilities.CustomGroupBox();
            this.labelTuner1FreqOffset = new System.Windows.Forms.Label();
            this.txtTuner1FreqOffset = new System.Windows.Forms.TextBox();
            this.labelTuner2FreqOffset = new System.Windows.Forms.Label();
            this.txtTuner2FreqOffset = new System.Windows.Forms.TextBox();
            this.labelTuner3FreqOffset = new System.Windows.Forms.Label();
            this.txtTuner3FreqOffset = new System.Windows.Forms.TextBox();
            this.labelTuner4FreqOffset = new System.Windows.Forms.Label();
            this.txtTuner4FreqOffset = new System.Windows.Forms.TextBox();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupFreqOffsets.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.txtWHWSBaseUdp);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this.txtWHWSIp);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.txtWHWSPort);
            this.groupBox1.Location = new System.Drawing.Point(12, 315);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox1.Size = new System.Drawing.Size(471, 144);
            this.groupBox1.TabIndex = 6;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "WinterHill WS Settings";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(24, 74);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(116, 16);
            this.label2.TabIndex = 9;
            this.label2.Text = "WinterHill WS Port:";
            // 
            // txtWHWSPort
            // 
            this.txtWHWSPort.Location = new System.Drawing.Point(191, 71);
            this.txtWHWSPort.Margin = new System.Windows.Forms.Padding(4);
            this.txtWHWSPort.Name = "txtWHWSPort";
            this.txtWHWSPort.Size = new System.Drawing.Size(113, 22);
            this.txtWHWSPort.TabIndex = 8;
            // 
            // btnSave
            // 
            this.btnSave.Location = new System.Drawing.Point(387, 590);
            this.btnSave.Margin = new System.Windows.Forms.Padding(4);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(100, 28);
            this.btnSave.TabIndex = 7;
            this.btnSave.Text = "Save";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(279, 590);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(4);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 28);
            this.btnCancel.TabIndex = 8;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.comboDefaultInterface);
            this.groupBox2.Controls.Add(this.label4);
            this.groupBox2.Location = new System.Drawing.Point(12, 12);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(472, 153);
            this.groupBox2.TabIndex = 9;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "General";
            // 
            // btnBroadcastListener
            // 
            this.btnBroadcastListener.Location = new System.Drawing.Point(13, 590);
            this.btnBroadcastListener.Name = "btnBroadcastListener";
            this.btnBroadcastListener.Size = new System.Drawing.Size(231, 28);
            this.btnBroadcastListener.TabIndex = 2;
            this.btnBroadcastListener.Text = "PicoTuner (WH) Broadcast Listener";
            this.btnBroadcastListener.UseVisualStyleBackColor = true;
            this.btnBroadcastListener.Click += new System.EventHandler(this.btnBroadcastListener_Click);
            // 
            // comboDefaultInterface
            // 
            this.comboDefaultInterface.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboDefaultInterface.FormattingEnabled = true;
            this.comboDefaultInterface.Items.AddRange(new object[] {
            "Always Ask",
            "Websocket (ZR6TG)",
            "PicoTuner Ethernet (G4EWJ)"});
            this.comboDefaultInterface.Location = new System.Drawing.Point(192, 29);
            this.comboDefaultInterface.Name = "comboDefaultInterface";
            this.comboDefaultInterface.Size = new System.Drawing.Size(206, 24);
            this.comboDefaultInterface.TabIndex = 1;
            this.comboDefaultInterface.SelectedIndexChanged += new System.EventHandler(this.comboDefaultInterface_SelectedIndexChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(25, 32);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(109, 16);
            this.label4.TabIndex = 0;
            this.label4.Text = "Default Interface: ";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(24, 41);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(104, 16);
            this.label1.TabIndex = 13;
            this.label1.Text = "WinterHill WS IP:";
            // 
            // txtWHWSIp
            // 
            this.txtWHWSIp.Location = new System.Drawing.Point(191, 38);
            this.txtWHWSIp.Margin = new System.Windows.Forms.Padding(4);
            this.txtWHWSIp.Name = "txtWHWSIp";
            this.txtWHWSIp.Size = new System.Drawing.Size(206, 22);
            this.txtWHWSIp.TabIndex = 12;
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.label6);
            this.groupBox3.Controls.Add(this.txtUDPIP);
            this.groupBox3.Controls.Add(this.txtUDPBasePort);
            this.groupBox3.Controls.Add(this.label5);
            this.groupBox3.Location = new System.Drawing.Point(12, 171);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(471, 137);
            this.groupBox3.TabIndex = 10;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "WinterHill (PicoTuner Ethernet)";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(24, 39);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(76, 16);
            this.label5.TabIndex = 0;
            this.label5.Text = "IP Address:";
            // 
            // txtUDPIP
            // 
            this.txtUDPIP.Location = new System.Drawing.Point(191, 36);
            this.txtUDPIP.Name = "txtUDPIP";
            this.txtUDPIP.Size = new System.Drawing.Size(206, 22);
            this.txtUDPIP.TabIndex = 1;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(24, 106);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(120, 16);
            this.label3.TabIndex = 13;
            this.label3.Text = "WinterHill Udp Port:";
            // 
            // txtWHWSBaseUdp
            // 
            this.txtWHWSBaseUdp.Location = new System.Drawing.Point(191, 103);
            this.txtWHWSBaseUdp.Margin = new System.Windows.Forms.Padding(4);
            this.txtWHWSBaseUdp.Name = "txtWHWSBaseUdp";
            this.txtWHWSBaseUdp.Size = new System.Drawing.Size(113, 22);
            this.txtWHWSBaseUdp.TabIndex = 12;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(24, 68);
            this.label6.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(98, 16);
            this.label6.TabIndex = 15;
            this.label6.Text = "Udp Base Port:";
            // 
            // txtUDPBasePort
            // 
            this.txtUDPBasePort.Location = new System.Drawing.Point(191, 65);
            this.txtUDPBasePort.Margin = new System.Windows.Forms.Padding(4);
            this.txtUDPBasePort.Name = "txtUDPBasePort";
            this.txtUDPBasePort.Size = new System.Drawing.Size(113, 22);
            this.txtUDPBasePort.TabIndex = 14;
            //
            // groupFreqOffsets
            //
            this.groupFreqOffsets.Controls.Add(this.labelTuner1FreqOffset);
            this.groupFreqOffsets.Controls.Add(this.txtTuner1FreqOffset);
            this.groupFreqOffsets.Controls.Add(this.labelTuner2FreqOffset);
            this.groupFreqOffsets.Controls.Add(this.txtTuner2FreqOffset);
            this.groupFreqOffsets.Controls.Add(this.labelTuner3FreqOffset);
            this.groupFreqOffsets.Controls.Add(this.txtTuner3FreqOffset);
            this.groupFreqOffsets.Controls.Add(this.labelTuner4FreqOffset);
            this.groupFreqOffsets.Controls.Add(this.txtTuner4FreqOffset);
            this.groupFreqOffsets.Location = new System.Drawing.Point(12, 465);
            this.groupFreqOffsets.Name = "groupFreqOffsets";
            this.groupFreqOffsets.Size = new System.Drawing.Size(471, 105);
            this.groupFreqOffsets.TabIndex = 6;
            this.groupFreqOffsets.TabStop = false;
            this.groupFreqOffsets.Text = "Default Freq Offset (kHz)";
            //
            // labelTuner1FreqOffset
            //
            this.labelTuner1FreqOffset.AutoSize = true;
            this.labelTuner1FreqOffset.Location = new System.Drawing.Point(24, 35);
            this.labelTuner1FreqOffset.Name = "labelTuner1FreqOffset";
            this.labelTuner1FreqOffset.Size = new System.Drawing.Size(55, 16);
            this.labelTuner1FreqOffset.TabIndex = 0;
            this.labelTuner1FreqOffset.Text = "Tuner 1:";
            //
            // txtTuner1FreqOffset
            //
            this.txtTuner1FreqOffset.Location = new System.Drawing.Point(100, 32);
            this.txtTuner1FreqOffset.Name = "txtTuner1FreqOffset";
            this.txtTuner1FreqOffset.Size = new System.Drawing.Size(110, 22);
            this.txtTuner1FreqOffset.TabIndex = 1;
            //
            // labelTuner2FreqOffset
            //
            this.labelTuner2FreqOffset.AutoSize = true;
            this.labelTuner2FreqOffset.Location = new System.Drawing.Point(250, 35);
            this.labelTuner2FreqOffset.Name = "labelTuner2FreqOffset";
            this.labelTuner2FreqOffset.Size = new System.Drawing.Size(55, 16);
            this.labelTuner2FreqOffset.TabIndex = 2;
            this.labelTuner2FreqOffset.Text = "Tuner 2:";
            //
            // txtTuner2FreqOffset
            //
            this.txtTuner2FreqOffset.Location = new System.Drawing.Point(326, 32);
            this.txtTuner2FreqOffset.Name = "txtTuner2FreqOffset";
            this.txtTuner2FreqOffset.Size = new System.Drawing.Size(110, 22);
            this.txtTuner2FreqOffset.TabIndex = 3;
            //
            // labelTuner3FreqOffset
            //
            this.labelTuner3FreqOffset.AutoSize = true;
            this.labelTuner3FreqOffset.Location = new System.Drawing.Point(24, 69);
            this.labelTuner3FreqOffset.Name = "labelTuner3FreqOffset";
            this.labelTuner3FreqOffset.Size = new System.Drawing.Size(55, 16);
            this.labelTuner3FreqOffset.TabIndex = 4;
            this.labelTuner3FreqOffset.Text = "Tuner 3:";
            //
            // txtTuner3FreqOffset
            //
            this.txtTuner3FreqOffset.Location = new System.Drawing.Point(100, 66);
            this.txtTuner3FreqOffset.Name = "txtTuner3FreqOffset";
            this.txtTuner3FreqOffset.Size = new System.Drawing.Size(110, 22);
            this.txtTuner3FreqOffset.TabIndex = 5;
            //
            // labelTuner4FreqOffset
            //
            this.labelTuner4FreqOffset.AutoSize = true;
            this.labelTuner4FreqOffset.Location = new System.Drawing.Point(250, 69);
            this.labelTuner4FreqOffset.Name = "labelTuner4FreqOffset";
            this.labelTuner4FreqOffset.Size = new System.Drawing.Size(55, 16);
            this.labelTuner4FreqOffset.TabIndex = 6;
            this.labelTuner4FreqOffset.Text = "Tuner 4:";
            //
            // txtTuner4FreqOffset
            //
            this.txtTuner4FreqOffset.Location = new System.Drawing.Point(326, 66);
            this.txtTuner4FreqOffset.Name = "txtTuner4FreqOffset";
            this.txtTuner4FreqOffset.Size = new System.Drawing.Size(110, 22);
            this.txtTuner4FreqOffset.TabIndex = 7;
            //
            // WinterHillSettingsForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(501, 631);
            this.ControlBox = false;
            this.Controls.Add(this.groupFreqOffsets);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.btnBroadcastListener);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.groupBox1);
            this.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "WinterHillSettingsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "WinterHill Settings";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.groupFreqOffsets.ResumeLayout(false);
            this.groupFreqOffsets.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private opentuner.Utilities.CustomGroupBox groupBox1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox txtWHWSPort;
        private opentuner.FlexButton btnSave;
        private opentuner.FlexButton btnCancel;
        private opentuner.Utilities.CustomGroupBox groupBox2;
        private opentuner.FlexButton btnBroadcastListener;
        private opentuner.FlexComboBox comboDefaultInterface;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtWHWSIp;
        private opentuner.Utilities.CustomGroupBox groupBox3;
        private System.Windows.Forms.TextBox txtUDPIP;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtWHWSBaseUdp;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox txtUDPBasePort;
        private opentuner.Utilities.CustomGroupBox groupFreqOffsets;
        private System.Windows.Forms.Label labelTuner1FreqOffset;
        private System.Windows.Forms.TextBox txtTuner1FreqOffset;
        private System.Windows.Forms.Label labelTuner2FreqOffset;
        private System.Windows.Forms.TextBox txtTuner2FreqOffset;
        private System.Windows.Forms.Label labelTuner3FreqOffset;
        private System.Windows.Forms.TextBox txtTuner3FreqOffset;
        private System.Windows.Forms.Label labelTuner4FreqOffset;
        private System.Windows.Forms.TextBox txtTuner4FreqOffset;
    }
}