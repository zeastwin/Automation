namespace Automation.Hmi
{
    partial class HmiHomePage
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
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.leftGroup = new System.Windows.Forms.GroupBox();
            this.workModeCombo = new System.Windows.Forms.ComboBox();
            this.runTimeLabel = new System.Windows.Forms.Label();
            this.versionGroup = new System.Windows.Forms.GroupBox();
            this.versionLabel = new System.Windows.Forms.Label();
            this.productionGroup = new System.Windows.Forms.GroupBox();
            this.pcManagedCheck = new System.Windows.Forms.CheckBox();
            this.pdcaDisabledCheck = new System.Windows.Forms.CheckBox();
            this.mesDisabledCheck = new System.Windows.Forms.CheckBox();
            this.systemStatusLabel = new System.Windows.Forms.Label();
            this.pageLayout = new System.Windows.Forms.TableLayoutPanel();
            this.pageHost = new System.Windows.Forms.GroupBox();
            this.mainPageButton = new System.Windows.Forms.Button();
            this.logPageButton = new System.Windows.Forms.Button();
            this.hivePageButton = new System.Windows.Forms.Button();
            this.pressurePageButton = new System.Windows.Forms.Button();
            this.torquePageButton = new System.Windows.Forms.Button();
            this.alarmTicker = new Automation.Hmi.LegacyAlarmTickerControl();
            this.rootLayout.SuspendLayout();
            this.leftGroup.SuspendLayout();
            this.versionGroup.SuspendLayout();
            this.productionGroup.SuspendLayout();
            this.pageLayout.SuspendLayout();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 2;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 300F));
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.leftGroup, 0, 0);
            this.rootLayout.Controls.Add(this.pageLayout, 1, 0);
            this.rootLayout.Controls.Add(this.alarmTicker, 0, 1);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Location = new System.Drawing.Point(0, 0);
            this.rootLayout.Margin = new System.Windows.Forms.Padding(0);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 2;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 86.89F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 13.11F));
            this.rootLayout.Size = new System.Drawing.Size(1376, 662);
            this.rootLayout.TabIndex = 0;
            //
            // leftGroup
            //
            this.leftGroup.Controls.Add(this.workModeCombo);
            this.leftGroup.Controls.Add(this.runTimeLabel);
            this.leftGroup.Controls.Add(this.versionGroup);
            this.leftGroup.Controls.Add(this.productionGroup);
            this.leftGroup.Controls.Add(this.systemStatusLabel);
            this.leftGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            this.leftGroup.Location = new System.Drawing.Point(3, 3);
            this.leftGroup.Name = "leftGroup";
            this.leftGroup.Size = new System.Drawing.Size(294, 569);
            this.leftGroup.TabIndex = 0;
            this.leftGroup.TabStop = false;
            //
            // workModeCombo
            //
            this.workModeCombo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.workModeCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.workModeCombo.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.workModeCombo.Font = new System.Drawing.Font("宋体", 25.8F, System.Drawing.FontStyle.Bold);
            this.workModeCombo.FormattingEnabled = true;
            this.workModeCombo.Items.AddRange(new object[] {
            "    工单模式",
            "    单机模式"});
            this.workModeCombo.Location = new System.Drawing.Point(0, 386);
            this.workModeCombo.Name = "workModeCombo";
            this.workModeCombo.Size = new System.Drawing.Size(292, 42);
            this.workModeCombo.TabIndex = 5;
            //
            // runTimeLabel
            //
            this.runTimeLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.runTimeLabel.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.runTimeLabel.Font = new System.Drawing.Font("宋体", 12F);
            this.runTimeLabel.Location = new System.Drawing.Point(0, 433);
            this.runTimeLabel.Name = "runTimeLabel";
            this.runTimeLabel.Size = new System.Drawing.Size(294, 61);
            this.runTimeLabel.TabIndex = 6;
            this.runTimeLabel.Text = "运行：00天00小时00分00秒";
            this.runTimeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // versionGroup
            //
            this.versionGroup.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.versionGroup.Controls.Add(this.versionLabel);
            this.versionGroup.Location = new System.Drawing.Point(6, 502);
            this.versionGroup.Name = "versionGroup";
            this.versionGroup.Size = new System.Drawing.Size(285, 61);
            this.versionGroup.TabIndex = 7;
            this.versionGroup.TabStop = false;
            this.versionGroup.Text = "软件版本";
            //
            // versionLabel
            //
            this.versionLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.versionLabel.Font = new System.Drawing.Font("宋体", 12F);
            this.versionLabel.Location = new System.Drawing.Point(3, 19);
            this.versionLabel.Name = "versionLabel";
            this.versionLabel.Size = new System.Drawing.Size(279, 39);
            this.versionLabel.TabIndex = 0;
            this.versionLabel.Text = "EW_Version_3.0.0";
            this.versionLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // productionGroup
            //
            this.productionGroup.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.productionGroup.Controls.Add(this.pcManagedCheck);
            this.productionGroup.Controls.Add(this.pdcaDisabledCheck);
            this.productionGroup.Controls.Add(this.mesDisabledCheck);
            this.productionGroup.Location = new System.Drawing.Point(3, 82);
            this.productionGroup.Name = "productionGroup";
            this.productionGroup.Size = new System.Drawing.Size(291, 298);
            this.productionGroup.TabIndex = 4;
            this.productionGroup.TabStop = false;
            this.productionGroup.Text = "生产参数";
            //
            // pcManagedCheck
            //
            this.pcManagedCheck.AutoSize = true;
            this.pcManagedCheck.Location = new System.Drawing.Point(212, 20);
            this.pcManagedCheck.Name = "pcManagedCheck";
            this.pcManagedCheck.Size = new System.Drawing.Size(63, 21);
            this.pcManagedCheck.TabIndex = 2;
            this.pcManagedCheck.Text = "PC管理";
            this.pcManagedCheck.UseVisualStyleBackColor = true;
            //
            // pdcaDisabledCheck
            //
            this.pdcaDisabledCheck.AutoSize = true;
            this.pdcaDisabledCheck.Location = new System.Drawing.Point(118, 20);
            this.pdcaDisabledCheck.Name = "pdcaDisabledCheck";
            this.pdcaDisabledCheck.Size = new System.Drawing.Size(82, 21);
            this.pdcaDisabledCheck.TabIndex = 1;
            this.pdcaDisabledCheck.Text = "禁用PDCA";
            this.pdcaDisabledCheck.UseVisualStyleBackColor = true;
            //
            // mesDisabledCheck
            //
            this.mesDisabledCheck.AutoSize = true;
            this.mesDisabledCheck.Location = new System.Drawing.Point(9, 20);
            this.mesDisabledCheck.Name = "mesDisabledCheck";
            this.mesDisabledCheck.Size = new System.Drawing.Size(79, 21);
            this.mesDisabledCheck.TabIndex = 0;
            this.mesDisabledCheck.Text = "禁用MES";
            this.mesDisabledCheck.UseVisualStyleBackColor = true;
            //
            // systemStatusLabel
            //
            this.systemStatusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.systemStatusLabel.BackColor = System.Drawing.Color.White;
            this.systemStatusLabel.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.systemStatusLabel.Font = new System.Drawing.Font("宋体", 22.2F, System.Drawing.FontStyle.Bold);
            this.systemStatusLabel.Location = new System.Drawing.Point(3, 9);
            this.systemStatusLabel.Name = "systemStatusLabel";
            this.systemStatusLabel.Size = new System.Drawing.Size(288, 65);
            this.systemStatusLabel.TabIndex = 3;
            this.systemStatusLabel.Text = "就绪";
            this.systemStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // pageLayout
            //
            this.pageLayout.ColumnCount = 6;
            this.pageLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.pageLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.pageLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.pageLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.pageLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.pageLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pageLayout.Controls.Add(this.pageHost, 0, 1);
            this.pageLayout.Controls.Add(this.mainPageButton, 0, 0);
            this.pageLayout.Controls.Add(this.logPageButton, 1, 0);
            this.pageLayout.Controls.Add(this.hivePageButton, 2, 0);
            this.pageLayout.Controls.Add(this.pressurePageButton, 3, 0);
            this.pageLayout.Controls.Add(this.torquePageButton, 4, 0);
            this.pageLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pageLayout.Location = new System.Drawing.Point(303, 3);
            this.pageLayout.Name = "pageLayout";
            this.pageLayout.RowCount = 2;
            this.pageLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 51F));
            this.pageLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pageLayout.Size = new System.Drawing.Size(1070, 569);
            this.pageLayout.TabIndex = 1;
            //
            // pageHost
            //
            this.pageLayout.SetColumnSpan(this.pageHost, 6);
            this.pageHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pageHost.Location = new System.Drawing.Point(3, 54);
            this.pageHost.Name = "pageHost";
            this.pageHost.Padding = new System.Windows.Forms.Padding(3);
            this.pageHost.Size = new System.Drawing.Size(1064, 512);
            this.pageHost.TabIndex = 5;
            this.pageHost.TabStop = false;
            //
            // page buttons
            //
            this.mainPageButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainPageButton.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.mainPageButton.Location = new System.Drawing.Point(3, 3);
            this.mainPageButton.Name = "mainPageButton";
            this.mainPageButton.Size = new System.Drawing.Size(94, 45);
            this.mainPageButton.TabIndex = 0;
            this.mainPageButton.Text = "主页面";
            this.mainPageButton.UseVisualStyleBackColor = false;
            this.logPageButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logPageButton.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.logPageButton.Location = new System.Drawing.Point(103, 3);
            this.logPageButton.Name = "logPageButton";
            this.logPageButton.Size = new System.Drawing.Size(94, 45);
            this.logPageButton.TabIndex = 1;
            this.logPageButton.Text = "查看Log";
            this.logPageButton.UseVisualStyleBackColor = false;
            this.hivePageButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.hivePageButton.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.hivePageButton.Location = new System.Drawing.Point(203, 3);
            this.hivePageButton.Name = "hivePageButton";
            this.hivePageButton.Size = new System.Drawing.Size(94, 45);
            this.hivePageButton.TabIndex = 2;
            this.hivePageButton.Text = "Hive";
            this.hivePageButton.UseVisualStyleBackColor = false;
            this.pressurePageButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pressurePageButton.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.pressurePageButton.Location = new System.Drawing.Point(303, 3);
            this.pressurePageButton.Name = "pressurePageButton";
            this.pressurePageButton.Size = new System.Drawing.Size(94, 45);
            this.pressurePageButton.TabIndex = 3;
            this.pressurePageButton.Text = "压力曲线图";
            this.pressurePageButton.UseVisualStyleBackColor = false;
            this.torquePageButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.torquePageButton.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.torquePageButton.Location = new System.Drawing.Point(403, 3);
            this.torquePageButton.Name = "torquePageButton";
            this.torquePageButton.Size = new System.Drawing.Size(94, 45);
            this.torquePageButton.TabIndex = 4;
            this.torquePageButton.Text = "扭力曲线图";
            this.torquePageButton.UseVisualStyleBackColor = false;
            //
            // alarmTicker
            //
            this.alarmTicker.BackColor = System.Drawing.Color.White;
            this.alarmTicker.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.rootLayout.SetColumnSpan(this.alarmTicker, 2);
            this.alarmTicker.Dock = System.Windows.Forms.DockStyle.Fill;
            this.alarmTicker.Location = new System.Drawing.Point(5, 580);
            this.alarmTicker.Margin = new System.Windows.Forms.Padding(5);
            this.alarmTicker.Name = "alarmTicker";
            this.alarmTicker.Size = new System.Drawing.Size(1366, 77);
            this.alarmTicker.TabIndex = 2;
            //
            // HmiHomePage
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Control;
            this.ClientSize = new System.Drawing.Size(1376, 662);
            this.Controls.Add(this.rootLayout);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "HmiHomePage";
            this.Text = "主页";
            this.rootLayout.ResumeLayout(false);
            this.leftGroup.ResumeLayout(false);
            this.leftGroup.PerformLayout();
            this.versionGroup.ResumeLayout(false);
            this.productionGroup.ResumeLayout(false);
            this.productionGroup.PerformLayout();
            this.pageLayout.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.GroupBox leftGroup;
        private System.Windows.Forms.ComboBox workModeCombo;
        private System.Windows.Forms.Label runTimeLabel;
        private System.Windows.Forms.GroupBox versionGroup;
        private System.Windows.Forms.Label versionLabel;
        private System.Windows.Forms.GroupBox productionGroup;
        private System.Windows.Forms.CheckBox pcManagedCheck;
        private System.Windows.Forms.CheckBox pdcaDisabledCheck;
        private System.Windows.Forms.CheckBox mesDisabledCheck;
        private System.Windows.Forms.Label systemStatusLabel;
        private System.Windows.Forms.TableLayoutPanel pageLayout;
        private System.Windows.Forms.GroupBox pageHost;
        private System.Windows.Forms.Button mainPageButton;
        private System.Windows.Forms.Button logPageButton;
        private System.Windows.Forms.Button hivePageButton;
        private System.Windows.Forms.Button pressurePageButton;
        private System.Windows.Forms.Button torquePageButton;
        private Automation.Hmi.LegacyAlarmTickerControl alarmTicker;
    }
}
