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
            this.rootSplit = new System.Windows.Forms.SplitContainer();
            this.mainLayout = new System.Windows.Forms.TableLayoutPanel();
            this.commandBar = new System.Windows.Forms.TableLayoutPanel();
            this.btnHome = new System.Windows.Forms.Button();
            this.btnDebug = new System.Windows.Forms.Button();
            this.btnVideo = new System.Windows.Forms.Button();
            this.btnAlarm = new System.Windows.Forms.Button();
            this.btnData = new System.Windows.Forms.Button();
            this.brandLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblAutomationBrand = new System.Windows.Forms.Label();
            this.lblDeviceName = new System.Windows.Forms.Label();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnPause = new System.Windows.Forms.Button();
            this.btnExcel = new System.Windows.Forms.Button();
            this.btnLog = new System.Windows.Forms.Button();
            this.statusLayout = new System.Windows.Forms.TableLayoutPanel();
            this.btnLogin = new System.Windows.Forms.Button();
            this.lblFixtureStatus = new System.Windows.Forms.Label();
            this.pageHost = new System.Windows.Forms.Panel();
            this.footerLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblDate = new System.Windows.Forms.Label();
            this.lblTime = new System.Windows.Forms.Label();
            this.lblFooterUser = new System.Windows.Forms.Label();
            this.lblCompany = new System.Windows.Forms.Label();
            this.lblPhone = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.rootSplit)).BeginInit();
            this.rootSplit.Panel1.SuspendLayout();
            this.rootSplit.Panel2.SuspendLayout();
            this.rootSplit.SuspendLayout();
            this.mainLayout.SuspendLayout();
            this.commandBar.SuspendLayout();
            this.brandLayout.SuspendLayout();
            this.statusLayout.SuspendLayout();
            this.footerLayout.SuspendLayout();
            this.SuspendLayout();
            //
            // rootSplit
            //
            this.rootSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootSplit.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.rootSplit.IsSplitterFixed = true;
            this.rootSplit.Location = new System.Drawing.Point(0, 0);
            this.rootSplit.Name = "rootSplit";
            this.rootSplit.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.rootSplit.Panel1.Controls.Add(this.mainLayout);
            this.rootSplit.Panel2.Controls.Add(this.footerLayout);
            this.rootSplit.Size = new System.Drawing.Size(1502, 839);
            this.rootSplit.SplitterDistance = 812;
            this.rootSplit.SplitterWidth = 2;
            this.rootSplit.TabIndex = 0;
            //
            // mainLayout
            //
            this.mainLayout.ColumnCount = 1;
            this.mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayout.Controls.Add(this.commandBar, 0, 0);
            this.mainLayout.Controls.Add(this.pageHost, 0, 1);
            this.mainLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainLayout.Location = new System.Drawing.Point(0, 0);
            this.mainLayout.Name = "mainLayout";
            this.mainLayout.RowCount = 2;
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 81F));
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayout.Size = new System.Drawing.Size(1502, 812);
            //
            // commandBar
            //
            this.commandBar.BackColor = System.Drawing.Color.WhiteSmoke;
            this.commandBar.ColumnCount = 13;
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 0.62F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 18.63F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 6.21F));
            this.commandBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 18.64F));
            this.commandBar.Controls.Add(this.btnHome, 1, 0);
            this.commandBar.Controls.Add(this.btnDebug, 2, 0);
            this.commandBar.Controls.Add(this.btnVideo, 3, 0);
            this.commandBar.Controls.Add(this.btnAlarm, 4, 0);
            this.commandBar.Controls.Add(this.btnData, 5, 0);
            this.commandBar.Controls.Add(this.brandLayout, 6, 0);
            this.commandBar.Controls.Add(this.btnStart, 7, 0);
            this.commandBar.Controls.Add(this.btnStop, 8, 0);
            this.commandBar.Controls.Add(this.btnPause, 9, 0);
            this.commandBar.Controls.Add(this.btnExcel, 10, 0);
            this.commandBar.Controls.Add(this.btnLog, 11, 0);
            this.commandBar.Controls.Add(this.statusLayout, 12, 0);
            this.commandBar.Dock = System.Windows.Forms.DockStyle.Fill;
            this.commandBar.Margin = new System.Windows.Forms.Padding(0);
            this.commandBar.Name = "commandBar";
            this.commandBar.RowCount = 1;
            this.commandBar.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // page buttons
            //
            this.ConfigureCommandButton(this.btnHome, "主页", global::Automation.UiIconKind.Home, global::Automation.UiPalette.TextPrimary, new System.EventHandler(this.btnHome_Click));
            this.ConfigureCommandButton(this.btnDebug, "调试", global::Automation.UiIconKind.Settings, global::Automation.UiPalette.TextPrimary, new System.EventHandler(this.btnDebug_Click));
            this.ConfigureCommandButton(this.btnVideo, "CCD", global::Automation.UiIconKind.Video, global::Automation.UiPalette.TextPrimary, new System.EventHandler(this.btnVideo_Click));
            this.ConfigureCommandButton(this.btnAlarm, "报警", global::Automation.UiIconKind.Alarm, global::Automation.UiPalette.Danger, new System.EventHandler(this.btnAlarm_Click));
            this.ConfigureCommandButton(this.btnData, "数据", global::Automation.UiIconKind.DataTable, global::Automation.UiPalette.TextPrimary, new System.EventHandler(this.btnData_Click));
            this.ConfigureCommandButton(this.btnStart, "启动", global::Automation.UiIconKind.Run, global::Automation.UiPalette.Success, new System.EventHandler(this.btnStart_Click));
            this.ConfigureCommandButton(this.btnStop, "停止", global::Automation.UiIconKind.Stop, global::Automation.UiPalette.Danger, new System.EventHandler(this.btnStop_Click));
            this.ConfigureCommandButton(this.btnPause, "暂停", global::Automation.UiIconKind.Pause, global::Automation.UiPalette.Warning, new System.EventHandler(this.btnPause_Click));
            this.ConfigureCommandButton(this.btnExcel, "Excel", global::Automation.UiIconKind.Spreadsheet, global::Automation.UiPalette.Success, new System.EventHandler(this.btnExcel_Click));
            this.ConfigureCommandButton(this.btnLog, "Log", global::Automation.UiIconKind.Log, global::Automation.UiPalette.TextPrimary, new System.EventHandler(this.btnLog_Click));
            //
            // brandLayout
            //
            this.brandLayout.BackColor = System.Drawing.Color.Gainsboro;
            this.brandLayout.ColumnCount = 1;
            this.brandLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.brandLayout.Controls.Add(this.lblAutomationBrand, 0, 0);
            this.brandLayout.Controls.Add(this.lblDeviceName, 0, 1);
            this.brandLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.brandLayout.Margin = new System.Windows.Forms.Padding(5, 10, 5, 10);
            this.brandLayout.Name = "brandLayout";
            this.brandLayout.RowCount = 2;
            this.brandLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 58F));
            this.brandLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 42F));
            //
            // lblAutomationBrand
            //
            this.lblAutomationBrand.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblAutomationBrand.Font = new System.Drawing.Font("Microsoft YaHei UI", 15F, System.Drawing.FontStyle.Bold);
            this.lblAutomationBrand.Name = "lblAutomationBrand";
            this.lblAutomationBrand.Text = "Automation";
            this.lblAutomationBrand.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.lblAutomationBrand.Click += new System.EventHandler(this.lblDeviceName_Click);
            //
            // lblDeviceName
            //
            this.lblDeviceName.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDeviceName.Font = new System.Drawing.Font("Microsoft YaHei UI", 10.5F);
            this.lblDeviceName.Name = "lblDeviceName";
            this.lblDeviceName.Text = "JS_ICT_NPI_LAX_XXXX";
            this.lblDeviceName.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.lblDeviceName.Click += new System.EventHandler(this.lblDeviceName_Click);
            //
            // statusLayout
            //
            this.statusLayout.ColumnCount = 1;
            this.statusLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.statusLayout.Controls.Add(this.btnLogin, 0, 0);
            this.statusLayout.Controls.Add(this.lblFixtureStatus, 0, 1);
            this.statusLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusLayout.RowCount = 2;
            this.statusLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.statusLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            //
            // btnLogin
            //
            this.btnLogin.BackColor = System.Drawing.Color.FromArgb(204, 204, 204);
            this.btnLogin.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnLogin.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLogin.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.btnLogin.Margin = new System.Windows.Forms.Padding(3, 3, 3, 1);
            this.btnLogin.Name = "btnLogin";
            this.btnLogin.Text = "请登录";
            this.btnLogin.UseVisualStyleBackColor = false;
            this.btnLogin.Click += new System.EventHandler(this.btnLogin_Click);
            //
            // lblFixtureStatus
            //
            this.lblFixtureStatus.BackColor = System.Drawing.SystemColors.ControlLight;
            this.lblFixtureStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFixtureStatus.Font = new System.Drawing.Font("微软雅黑", 11F);
            this.lblFixtureStatus.Margin = new System.Windows.Forms.Padding(3, 1, 3, 8);
            this.lblFixtureStatus.Name = "lblFixtureStatus";
            this.lblFixtureStatus.Text = "None";
            this.lblFixtureStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblFixtureStatus.Click += new System.EventHandler(this.lblFixtureStatus_Click);
            //
            // pageHost
            //
            this.pageHost.BackColor = System.Drawing.Color.White;
            this.pageHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pageHost.Location = new System.Drawing.Point(3, 84);
            this.pageHost.Name = "pageHost";
            this.pageHost.Padding = new System.Windows.Forms.Padding(5);
            this.pageHost.Size = new System.Drawing.Size(1496, 725);
            //
            // footerLayout
            //
            this.footerLayout.ColumnCount = 7;
            this.footerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 110F));
            this.footerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.footerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.footerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 230F));
            this.footerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 190F));
            this.footerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 170F));
            this.footerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 145F));
            this.footerLayout.Controls.Add(this.lblDate, 0, 0);
            this.footerLayout.Controls.Add(this.lblTime, 1, 0);
            this.footerLayout.Controls.Add(this.lblFooterUser, 3, 0);
            this.footerLayout.Controls.Add(this.lblCompany, 4, 0);
            this.footerLayout.Controls.Add(this.lblPhone, 5, 0);
            this.footerLayout.Controls.Add(this.lblVersion, 6, 0);
            this.footerLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.footerLayout.RowCount = 1;
            this.footerLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // footer labels
            //
            this.ConfigureFooterLabel(this.lblDate, "2026-07-24");
            this.ConfigureFooterLabel(this.lblTime, "20:00:00");
            this.ConfigureFooterLabel(this.lblFooterUser, "User");
            this.ConfigureFooterLabel(this.lblCompany, "联合东创科技有限公司");
            this.ConfigureFooterLabel(this.lblPhone, "电话:0769—39026833");
            this.ConfigureFooterLabel(this.lblVersion, "版本号: V3.0.0");
            //
            // FrmHmiMain
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1502, 839);
            this.Controls.Add(this.rootSplit);
            this.MinimumSize = new System.Drawing.Size(1200, 720);
            this.Name = "FrmHmiMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Automation - HMI";
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.rootSplit.Panel1.ResumeLayout(false);
            this.rootSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.rootSplit)).EndInit();
            this.rootSplit.ResumeLayout(false);
            this.mainLayout.ResumeLayout(false);
            this.commandBar.ResumeLayout(false);
            this.brandLayout.ResumeLayout(false);
            this.statusLayout.ResumeLayout(false);
            this.footerLayout.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private void ConfigureCommandButton(
            System.Windows.Forms.Button button,
            string text,
            global::Automation.UiIconKind iconKind,
            System.Drawing.Color iconColor,
            System.EventHandler handler)
        {
            button.BackColor = System.Drawing.Color.Transparent;
            button.Dock = System.Windows.Forms.DockStyle.Fill;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfacePressed;
            button.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.SurfaceHover;
            button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            button.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F, System.Drawing.FontStyle.Regular);
            button.ForeColor = global::Automation.UiPalette.TextPrimary;
            button.Image = global::Automation.UiIconFactory.Create(iconKind, iconColor, 36);
            button.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            button.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            button.Text = text;
            button.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            button.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            button.UseVisualStyleBackColor = false;
            button.Click += handler;
        }

        private void ConfigureFooterLabel(
            System.Windows.Forms.Label label,
            string text)
        {
            label.Dock = System.Windows.Forms.DockStyle.Fill;
            label.Font = new System.Drawing.Font("宋体", 10.5F);
            label.Text = text;
            label.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        }

        private System.Windows.Forms.SplitContainer rootSplit;
        private System.Windows.Forms.TableLayoutPanel mainLayout;
        private System.Windows.Forms.TableLayoutPanel commandBar;
        private System.Windows.Forms.Button btnHome;
        private System.Windows.Forms.Button btnDebug;
        private System.Windows.Forms.Button btnVideo;
        private System.Windows.Forms.Button btnAlarm;
        private System.Windows.Forms.Button btnData;
        private System.Windows.Forms.TableLayoutPanel brandLayout;
        private System.Windows.Forms.Label lblAutomationBrand;
        private System.Windows.Forms.Label lblDeviceName;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnPause;
        private System.Windows.Forms.Button btnExcel;
        private System.Windows.Forms.Button btnLog;
        private System.Windows.Forms.TableLayoutPanel statusLayout;
        private System.Windows.Forms.Button btnLogin;
        private System.Windows.Forms.Label lblFixtureStatus;
        private System.Windows.Forms.Panel pageHost;
        private System.Windows.Forms.TableLayoutPanel footerLayout;
        private System.Windows.Forms.Label lblDate;
        private System.Windows.Forms.Label lblTime;
        private System.Windows.Forms.Label lblFooterUser;
        private System.Windows.Forms.Label lblCompany;
        private System.Windows.Forms.Label lblPhone;
        private System.Windows.Forms.Label lblVersion;
    }
}
