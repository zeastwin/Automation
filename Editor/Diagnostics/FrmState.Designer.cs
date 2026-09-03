namespace Automation
{
    partial class FrmState
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
            this.SysInfo = new System.Windows.Forms.Label();
            this.lblSystemStatus = new System.Windows.Forms.Label();
            this.lblAccountLevel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // SysInfo
            // 
            this.SysInfo.AutoSize = true;
            this.SysInfo.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.SysInfo.Location = new System.Drawing.Point(6, 1);
            this.SysInfo.Name = "SysInfo";
            this.SysInfo.Size = new System.Drawing.Size(0, 21);
            this.SysInfo.TabIndex = 0;
            this.SysInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblSystemStatus
            // 
            this.lblSystemStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            this.lblSystemStatus.AutoSize = true;
            this.lblSystemStatus.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblSystemStatus.Location = new System.Drawing.Point(6, 1);
            this.lblSystemStatus.Name = "lblSystemStatus";
            this.lblSystemStatus.Size = new System.Drawing.Size(0, 21);
            this.lblSystemStatus.TabIndex = 1;
            this.lblSystemStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblAccountLevel
            //
            this.lblAccountLevel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblAccountLevel.AutoSize = false;
            this.lblAccountLevel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular);
            this.lblAccountLevel.Location = new System.Drawing.Point(912, 1);
            this.lblAccountLevel.Name = "lblAccountLevel";
            this.lblAccountLevel.Size = new System.Drawing.Size(141, 21);
            this.lblAccountLevel.TabIndex = 2;
            this.lblAccountLevel.Text = "账户：未登录";
            this.lblAccountLevel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // FrmState
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1065, 23);
            this.Controls.Add(this.lblAccountLevel);
            this.Controls.Add(this.lblSystemStatus);
            this.Controls.Add(this.SysInfo);
            this.Name = "FrmState";
            this.Text = "FrmState";
            this.Load += new System.EventHandler(this.FrmState_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        public System.Windows.Forms.Label SysInfo;
        private System.Windows.Forms.Label lblSystemStatus;
        private System.Windows.Forms.Label lblAccountLevel;
    }
}
