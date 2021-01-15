namespace SmartBatteryHack
{
    partial class AboutForm
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
            this.VersionLabel = new System.Windows.Forms.Label();
            this.AboutDescriptionLabel = new System.Windows.Forms.Label();
            this.AboutTitleLabel = new System.Windows.Forms.Label();
            this.AboutPictureBox = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.AboutPictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // VersionLabel
            // 
            this.VersionLabel.AutoSize = true;
            this.VersionLabel.Location = new System.Drawing.Point(311, 13);
            this.VersionLabel.Name = "VersionLabel";
            this.VersionLabel.Size = new System.Drawing.Size(52, 13);
            this.VersionLabel.TabIndex = 6;
            this.VersionLabel.Text = "GUI: N/A";
            // 
            // AboutDescriptionLabel
            // 
            this.AboutDescriptionLabel.AutoSize = true;
            this.AboutDescriptionLabel.Location = new System.Drawing.Point(140, 35);
            this.AboutDescriptionLabel.Name = "AboutDescriptionLabel";
            this.AboutDescriptionLabel.Size = new System.Drawing.Size(221, 13);
            this.AboutDescriptionLabel.TabIndex = 5;
            this.AboutDescriptionLabel.Text = "Hacking tool for smart batteries using SMBus.";
            // 
            // AboutTitleLabel
            // 
            this.AboutTitleLabel.AutoSize = true;
            this.AboutTitleLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(238)));
            this.AboutTitleLabel.Location = new System.Drawing.Point(139, 8);
            this.AboutTitleLabel.Name = "AboutTitleLabel";
            this.AboutTitleLabel.Size = new System.Drawing.Size(166, 20);
            this.AboutTitleLabel.TabIndex = 4;
            this.AboutTitleLabel.Text = "Smart Battery Hack";
            // 
            // AboutPictureBox
            // 
            this.AboutPictureBox.BackgroundImage = global::SmartBatteryHack.Properties.Resources.battery_icon;
            this.AboutPictureBox.Location = new System.Drawing.Point(5, 5);
            this.AboutPictureBox.Name = "AboutPictureBox";
            this.AboutPictureBox.Size = new System.Drawing.Size(128, 128);
            this.AboutPictureBox.TabIndex = 7;
            this.AboutPictureBox.TabStop = false;
            // 
            // AboutForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(434, 141);
            this.Controls.Add(this.AboutPictureBox);
            this.Controls.Add(this.VersionLabel);
            this.Controls.Add(this.AboutDescriptionLabel);
            this.Controls.Add(this.AboutTitleLabel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AboutForm";
            this.Text = "About";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.AboutForm_FormClosed);
            ((System.ComponentModel.ISupportInitialize)(this.AboutPictureBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label VersionLabel;
        private System.Windows.Forms.Label AboutDescriptionLabel;
        private System.Windows.Forms.Label AboutTitleLabel;
        private System.Windows.Forms.PictureBox AboutPictureBox;
    }
}