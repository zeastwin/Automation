namespace Automation.Hmi
{
    partial class FrmHmiMain
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.headerPanel = new System.Windows.Forms.Panel();
            this.btnStartProcess = new System.Windows.Forms.Button();
            this.btnStopAll = new System.Windows.Forms.Button();
            this.btnOpenPlatform = new System.Windows.Forms.Button();
            this.navigationPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.btnHome = new System.Windows.Forms.Button();
            this.btnDebug = new System.Windows.Forms.Button();
            this.btnAlarmHistory = new System.Windows.Forms.Button();
            this.btnCapacity = new System.Windows.Forms.Button();
            this.lblTitle = new System.Windows.Forms.Label();
            this.pageHost = new System.Windows.Forms.Panel();
            this.headerPanel.SuspendLayout();
            this.navigationPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // headerPanel
            //
            this.headerPanel.BackColor = System.Drawing.Color.FromArgb(24, 38, 54);
            this.headerPanel.Controls.Add(this.btnStartProcess);
            this.headerPanel.Controls.Add(this.btnStopAll);
            this.headerPanel.Controls.Add(this.btnOpenPlatform);
            this.headerPanel.Controls.Add(this.navigationPanel);
            this.headerPanel.Controls.Add(this.lblTitle);
            this.headerPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.headerPanel.Location = new System.Drawing.Point(0, 0);
            this.headerPanel.Name = "headerPanel";
            this.headerPanel.Size = new System.Drawing.Size(1200, 60);
            this.headerPanel.TabIndex = 0;
            //
            // btnStartProcess
            //
            this.btnStartProcess.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnStartProcess.BackColor = System.Drawing.Color.FromArgb(46, 133, 75);
            this.btnStartProcess.FlatAppearance.BorderSize = 0;
            this.btnStartProcess.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStartProcess.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.btnStartProcess.ForeColor = System.Drawing.Color.White;
            this.btnStartProcess.Location = new System.Drawing.Point(812, 10);
            this.btnStartProcess.Name = "btnStartProcess";
            this.btnStartProcess.Size = new System.Drawing.Size(114, 40);
            this.btnStartProcess.TabIndex = 4;
            this.btnStartProcess.Text = "启动流程";
            this.btnStartProcess.UseVisualStyleBackColor = false;
            //
            // btnStopAll
            //
            this.btnStopAll.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnStopAll.BackColor = System.Drawing.Color.FromArgb(186, 51, 56);
            this.btnStopAll.FlatAppearance.BorderSize = 0;
            this.btnStopAll.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStopAll.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.btnStopAll.ForeColor = System.Drawing.Color.White;
            this.btnStopAll.Location = new System.Drawing.Point(938, 10);
            this.btnStopAll.Name = "btnStopAll";
            this.btnStopAll.Size = new System.Drawing.Size(114, 40);
            this.btnStopAll.TabIndex = 3;
            this.btnStopAll.Text = "停止全部流程";
            this.btnStopAll.UseVisualStyleBackColor = false;
            this.btnStopAll.Click += new System.EventHandler(this.btnStopAll_Click);
            //
            // btnOpenPlatform
            //
            this.btnOpenPlatform.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnOpenPlatform.BackColor = System.Drawing.Color.FromArgb(29, 121, 198);
            this.btnOpenPlatform.FlatAppearance.BorderSize = 0;
            this.btnOpenPlatform.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenPlatform.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.btnOpenPlatform.ForeColor = System.Drawing.Color.White;
            this.btnOpenPlatform.Location = new System.Drawing.Point(1064, 10);
            this.btnOpenPlatform.Name = "btnOpenPlatform";
            this.btnOpenPlatform.Size = new System.Drawing.Size(112, 40);
            this.btnOpenPlatform.TabIndex = 2;
            this.btnOpenPlatform.Text = "打开平台";
            this.btnOpenPlatform.UseVisualStyleBackColor = false;
            this.btnOpenPlatform.Click += new System.EventHandler(this.btnOpenPlatform_Click);
            //
            // navigationPanel
            //
            this.navigationPanel.Controls.Add(this.btnHome);
            this.navigationPanel.Controls.Add(this.btnDebug);
            this.navigationPanel.Controls.Add(this.btnAlarmHistory);
            this.navigationPanel.Controls.Add(this.btnCapacity);
            this.navigationPanel.Location = new System.Drawing.Point(14, 10);
            this.navigationPanel.Name = "navigationPanel";
            this.navigationPanel.Size = new System.Drawing.Size(436, 42);
            this.navigationPanel.TabIndex = 1;
            this.navigationPanel.WrapContents = false;
            //
            // btnHome
            //
            this.btnHome.BackColor = System.Drawing.Color.FromArgb(20, 126, 197);
            this.btnHome.FlatAppearance.BorderSize = 0;
            this.btnHome.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnHome.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.btnHome.ForeColor = System.Drawing.Color.White;
            this.btnHome.Location = new System.Drawing.Point(0, 0);
            this.btnHome.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this.btnHome.Name = "btnHome";
            this.btnHome.Size = new System.Drawing.Size(90, 40);
            this.btnHome.TabIndex = 0;
            this.btnHome.Text = "主页";
            this.btnHome.UseVisualStyleBackColor = false;
            this.btnHome.Click += new System.EventHandler(this.btnHome_Click);
            //
            // btnDebug
            //
            this.btnDebug.BackColor = System.Drawing.Color.FromArgb(29, 49, 70);
            this.btnDebug.FlatAppearance.BorderSize = 0;
            this.btnDebug.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDebug.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F);
            this.btnDebug.ForeColor = System.Drawing.Color.White;
            this.btnDebug.Location = new System.Drawing.Point(96, 0);
            this.btnDebug.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this.btnDebug.Name = "btnDebug";
            this.btnDebug.Size = new System.Drawing.Size(90, 40);
            this.btnDebug.TabIndex = 1;
            this.btnDebug.Text = "调试";
            this.btnDebug.UseVisualStyleBackColor = false;
            this.btnDebug.Click += new System.EventHandler(this.btnDebug_Click);
            //
            // btnAlarmHistory
            //
            this.btnAlarmHistory.BackColor = System.Drawing.Color.FromArgb(24, 38, 54);
            this.btnAlarmHistory.FlatAppearance.BorderSize = 0;
            this.btnAlarmHistory.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAlarmHistory.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F);
            this.btnAlarmHistory.ForeColor = System.Drawing.Color.White;
            this.btnAlarmHistory.Location = new System.Drawing.Point(192, 0);
            this.btnAlarmHistory.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this.btnAlarmHistory.Name = "btnAlarmHistory";
            this.btnAlarmHistory.Size = new System.Drawing.Size(108, 40);
            this.btnAlarmHistory.TabIndex = 2;
            this.btnAlarmHistory.Text = "报警历史";
            this.btnAlarmHistory.UseVisualStyleBackColor = false;
            this.btnAlarmHistory.Click += new System.EventHandler(this.btnAlarmHistory_Click);
            //
            // btnCapacity
            //
            this.btnCapacity.BackColor = System.Drawing.Color.FromArgb(24, 38, 54);
            this.btnCapacity.FlatAppearance.BorderSize = 0;
            this.btnCapacity.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCapacity.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F);
            this.btnCapacity.ForeColor = System.Drawing.Color.White;
            this.btnCapacity.Location = new System.Drawing.Point(306, 0);
            this.btnCapacity.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this.btnCapacity.Name = "btnCapacity";
            this.btnCapacity.Size = new System.Drawing.Size(90, 40);
            this.btnCapacity.TabIndex = 3;
            this.btnCapacity.Text = "产能";
            this.btnCapacity.UseVisualStyleBackColor = false;
            this.btnCapacity.Click += new System.EventHandler(this.btnCapacity_Click);
            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 15F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            this.lblTitle.Location = new System.Drawing.Point(18, 17);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(164, 27);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Visible = false;
            //
            // pageHost
            //
            this.pageHost.BackColor = System.Drawing.Color.White;
            this.pageHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pageHost.Location = new System.Drawing.Point(0, 60);
            this.pageHost.Name = "pageHost";
            this.pageHost.Padding = new System.Windows.Forms.Padding(0);
            this.pageHost.Size = new System.Drawing.Size(1200, 660);
            this.pageHost.TabIndex = 1;
            //
            // FrmHmiMain
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(245, 246, 248);
            this.ClientSize = new System.Drawing.Size(1200, 720);
            this.Controls.Add(this.pageHost);
            this.Controls.Add(this.headerPanel);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.MinimumSize = new System.Drawing.Size(1000, 680);
            this.Name = "FrmHmiMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Automation";
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.navigationPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel headerPanel;
        private System.Windows.Forms.Button btnStartProcess;
        private System.Windows.Forms.Button btnStopAll;
        private System.Windows.Forms.Button btnOpenPlatform;
        private System.Windows.Forms.FlowLayoutPanel navigationPanel;
        private System.Windows.Forms.Button btnHome;
        private System.Windows.Forms.Button btnDebug;
        private System.Windows.Forms.Button btnAlarmHistory;
        private System.Windows.Forms.Button btnCapacity;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Panel pageHost;
    }
}
