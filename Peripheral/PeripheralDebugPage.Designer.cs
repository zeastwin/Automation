namespace Automation.Peripheral
{
    partial class PeripheralDebugPage
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
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.grpPlatformStatus = new System.Windows.Forms.GroupBox();
            this.statusLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblRuntimeTitle = new System.Windows.Forms.Label();
            this.lblRuntimeState = new System.Windows.Forms.Label();
            this.lblSystemTitle = new System.Windows.Forms.Label();
            this.lblSystemState = new System.Windows.Forms.Label();
            this.lblResetTitle = new System.Windows.Forms.Label();
            this.lblResetState = new System.Windows.Forms.Label();
            this.lblConfigTitle = new System.Windows.Forms.Label();
            this.txtConfigRoot = new System.Windows.Forms.TextBox();
            this.btnOpenConfig = new System.Windows.Forms.Button();
            this.grpProcess = new System.Windows.Forms.GroupBox();
            this.processLayout = new System.Windows.Forms.TableLayoutPanel();
            this.cmbProcess = new System.Windows.Forms.ComboBox();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnPause = new System.Windows.Forms.Button();
            this.btnResume = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.lblHint = new System.Windows.Forms.Label();
            this.grpRuntimeLog = new System.Windows.Forms.GroupBox();
            this.lvRuntimeLog = new System.Windows.Forms.ListView();
            this.columnTime = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnLevel = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnMessage = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.grpPlatformStatus.SuspendLayout();
            this.statusLayout.SuspendLayout();
            this.grpProcess.SuspendLayout();
            this.processLayout.SuspendLayout();
            this.grpRuntimeLog.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitMain
            // 
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitMain.Location = new System.Drawing.Point(0, 0);
            this.splitMain.Name = "splitMain";
            // 
            // splitMain.Panel1
            // 
            this.splitMain.Panel1.Controls.Add(this.grpProcess);
            this.splitMain.Panel1.Controls.Add(this.grpPlatformStatus);
            this.splitMain.Panel1.Padding = new System.Windows.Forms.Padding(0, 0, 6, 0);
            // 
            // splitMain.Panel2
            // 
            this.splitMain.Panel2.Controls.Add(this.grpRuntimeLog);
            this.splitMain.Panel2.Padding = new System.Windows.Forms.Padding(6, 0, 0, 0);
            this.splitMain.Size = new System.Drawing.Size(1176, 632);
            this.splitMain.SplitterDistance = 320;
            this.splitMain.TabIndex = 0;
            // 
            // grpPlatformStatus
            // 
            this.grpPlatformStatus.Controls.Add(this.statusLayout);
            this.grpPlatformStatus.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpPlatformStatus.Location = new System.Drawing.Point(0, 0);
            this.grpPlatformStatus.Name = "grpPlatformStatus";
            this.grpPlatformStatus.Padding = new System.Windows.Forms.Padding(10);
            this.grpPlatformStatus.Size = new System.Drawing.Size(314, 245);
            this.grpPlatformStatus.TabIndex = 0;
            this.grpPlatformStatus.TabStop = false;
            this.grpPlatformStatus.Text = "平台状态";
            // 
            // statusLayout
            // 
            this.statusLayout.ColumnCount = 2;
            this.statusLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 88F));
            this.statusLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.statusLayout.Controls.Add(this.lblRuntimeTitle, 0, 0);
            this.statusLayout.Controls.Add(this.lblRuntimeState, 1, 0);
            this.statusLayout.Controls.Add(this.lblSystemTitle, 0, 1);
            this.statusLayout.Controls.Add(this.lblSystemState, 1, 1);
            this.statusLayout.Controls.Add(this.lblResetTitle, 0, 2);
            this.statusLayout.Controls.Add(this.lblResetState, 1, 2);
            this.statusLayout.Controls.Add(this.lblConfigTitle, 0, 3);
            this.statusLayout.Controls.Add(this.txtConfigRoot, 1, 3);
            this.statusLayout.Controls.Add(this.btnOpenConfig, 0, 4);
            this.statusLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusLayout.Location = new System.Drawing.Point(10, 28);
            this.statusLayout.Name = "statusLayout";
            this.statusLayout.RowCount = 5;
            this.statusLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.statusLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.statusLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.statusLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.statusLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.statusLayout.Size = new System.Drawing.Size(294, 207);
            this.statusLayout.TabIndex = 0;
            this.statusLayout.SetColumnSpan(this.btnOpenConfig, 2);
            // 
            // Status labels
            // 
            this.lblRuntimeTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRuntimeTitle.Text = "Runtime";
            this.lblRuntimeTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblRuntimeState.AutoEllipsis = true;
            this.lblRuntimeState.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRuntimeState.Text = "-";
            this.lblRuntimeState.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblSystemTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSystemTitle.Text = "系统状态";
            this.lblSystemTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblSystemState.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSystemState.Text = "-";
            this.lblSystemState.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblResetTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblResetTitle.Text = "复位状态";
            this.lblResetTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblResetState.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblResetState.Text = "-";
            this.lblResetState.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblConfigTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblConfigTitle.Text = "Config";
            this.lblConfigTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.txtConfigRoot.BackColor = System.Drawing.Color.White;
            this.txtConfigRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtConfigRoot.ReadOnly = true;
            this.btnOpenConfig.BackColor = System.Drawing.Color.FromArgb(84, 110, 122);
            this.btnOpenConfig.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnOpenConfig.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenConfig.ForeColor = System.Drawing.Color.White;
            this.btnOpenConfig.Text = "打开 Config（直接编辑前请退出程序）";
            this.btnOpenConfig.UseVisualStyleBackColor = false;
            this.btnOpenConfig.Click += new System.EventHandler(this.btnOpenConfig_Click);
            // 
            // grpProcess
            // 
            this.grpProcess.Controls.Add(this.processLayout);
            this.grpProcess.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpProcess.Location = new System.Drawing.Point(0, 245);
            this.grpProcess.Name = "grpProcess";
            this.grpProcess.Padding = new System.Windows.Forms.Padding(10);
            this.grpProcess.Size = new System.Drawing.Size(314, 387);
            this.grpProcess.TabIndex = 1;
            this.grpProcess.TabStop = false;
            this.grpProcess.Text = "流程控制";
            // 
            // processLayout
            // 
            this.processLayout.ColumnCount = 2;
            this.processLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.processLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.processLayout.Controls.Add(this.cmbProcess, 0, 0);
            this.processLayout.Controls.Add(this.btnStart, 0, 1);
            this.processLayout.Controls.Add(this.btnPause, 1, 1);
            this.processLayout.Controls.Add(this.btnResume, 0, 2);
            this.processLayout.Controls.Add(this.btnStop, 1, 2);
            this.processLayout.Controls.Add(this.lblHint, 0, 3);
            this.processLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.processLayout.Location = new System.Drawing.Point(10, 28);
            this.processLayout.Name = "processLayout";
            this.processLayout.RowCount = 4;
            this.processLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 38F));
            this.processLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 46F));
            this.processLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 46F));
            this.processLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.processLayout.Size = new System.Drawing.Size(294, 349);
            this.processLayout.TabIndex = 0;
            this.processLayout.SetColumnSpan(this.cmbProcess, 2);
            this.processLayout.SetColumnSpan(this.lblHint, 2);
            this.cmbProcess.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbProcess.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.btnStart.BackColor = System.Drawing.Color.FromArgb(46, 125, 50);
            this.btnStart.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.Margin = new System.Windows.Forms.Padding(4);
            this.btnStart.Text = "启动";
            this.btnStart.UseVisualStyleBackColor = false;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            this.btnPause.BackColor = System.Drawing.Color.FromArgb(239, 108, 0);
            this.btnPause.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnPause.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPause.ForeColor = System.Drawing.Color.White;
            this.btnPause.Margin = new System.Windows.Forms.Padding(4);
            this.btnPause.Text = "暂停";
            this.btnPause.UseVisualStyleBackColor = false;
            this.btnPause.Click += new System.EventHandler(this.btnPause_Click);
            this.btnResume.BackColor = System.Drawing.Color.FromArgb(2, 119, 189);
            this.btnResume.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnResume.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnResume.ForeColor = System.Drawing.Color.White;
            this.btnResume.Margin = new System.Windows.Forms.Padding(4);
            this.btnResume.Text = "继续";
            this.btnResume.UseVisualStyleBackColor = false;
            this.btnResume.Click += new System.EventHandler(this.btnResume_Click);
            this.btnStop.BackColor = System.Drawing.Color.FromArgb(198, 40, 40);
            this.btnStop.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStop.ForeColor = System.Drawing.Color.White;
            this.btnStop.Margin = new System.Windows.Forms.Padding(4);
            this.btnStop.Text = "停止";
            this.btnStop.UseVisualStyleBackColor = false;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            this.lblHint.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblHint.ForeColor = System.Drawing.Color.DimGray;
            this.lblHint.Padding = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.lblHint.Text = "此处仅提供平台级流程入口。具体设备手自动、工艺和产量页面由外围业务继续开发。";
            // 
            // grpRuntimeLog
            // 
            this.grpRuntimeLog.Controls.Add(this.lvRuntimeLog);
            this.grpRuntimeLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpRuntimeLog.Location = new System.Drawing.Point(6, 0);
            this.grpRuntimeLog.Name = "grpRuntimeLog";
            this.grpRuntimeLog.Padding = new System.Windows.Forms.Padding(10);
            this.grpRuntimeLog.Size = new System.Drawing.Size(840, 632);
            this.grpRuntimeLog.TabIndex = 0;
            this.grpRuntimeLog.TabStop = false;
            this.grpRuntimeLog.Text = "运行日志与报警";
            // 
            // lvRuntimeLog
            // 
            this.lvRuntimeLog.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { this.columnTime, this.columnLevel, this.columnMessage });
            this.lvRuntimeLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvRuntimeLog.FullRowSelect = true;
            this.lvRuntimeLog.GridLines = true;
            this.lvRuntimeLog.HideSelection = false;
            this.lvRuntimeLog.View = System.Windows.Forms.View.Details;
            this.columnTime.Text = "时间";
            this.columnTime.Width = 150;
            this.columnLevel.Text = "级别";
            this.columnLevel.Width = 80;
            this.columnMessage.Text = "内容";
            this.columnMessage.Width = 560;
            // 
            // PeripheralDebugPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1176, 632);
            this.Controls.Add(this.splitMain);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "PeripheralDebugPage";
            this.Text = "调试";
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
            this.splitMain.ResumeLayout(false);
            this.grpPlatformStatus.ResumeLayout(false);
            this.statusLayout.ResumeLayout(false);
            this.statusLayout.PerformLayout();
            this.grpProcess.ResumeLayout(false);
            this.processLayout.ResumeLayout(false);
            this.grpRuntimeLog.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.SplitContainer splitMain;
        private System.Windows.Forms.GroupBox grpPlatformStatus;
        private System.Windows.Forms.TableLayoutPanel statusLayout;
        private System.Windows.Forms.Label lblRuntimeTitle;
        private System.Windows.Forms.Label lblRuntimeState;
        private System.Windows.Forms.Label lblSystemTitle;
        private System.Windows.Forms.Label lblSystemState;
        private System.Windows.Forms.Label lblResetTitle;
        private System.Windows.Forms.Label lblResetState;
        private System.Windows.Forms.Label lblConfigTitle;
        private System.Windows.Forms.TextBox txtConfigRoot;
        private System.Windows.Forms.Button btnOpenConfig;
        private System.Windows.Forms.GroupBox grpProcess;
        private System.Windows.Forms.TableLayoutPanel processLayout;
        private System.Windows.Forms.ComboBox cmbProcess;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnPause;
        private System.Windows.Forms.Button btnResume;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Label lblHint;
        private System.Windows.Forms.GroupBox grpRuntimeLog;
        private System.Windows.Forms.ListView lvRuntimeLog;
        private System.Windows.Forms.ColumnHeader columnTime;
        private System.Windows.Forms.ColumnHeader columnLevel;
        private System.Windows.Forms.ColumnHeader columnMessage;
    }
}
