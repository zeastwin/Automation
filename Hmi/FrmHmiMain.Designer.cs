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
            this.lblFixtureStatus = new System.Windows.Forms.Label();
            this.btnAccount = new System.Windows.Forms.Button();
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
            // btnHome
            //
            this.btnHome.BackColor = System.Drawing.Color.Transparent;
            this.btnHome.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnHome.FlatAppearance.BorderSize = 0;
            this.btnHome.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnHome.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnHome.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnHome.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnHome.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnHome.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnHome.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnHome.Name = "btnHome";
            this.btnHome.Text = "主页";
            this.btnHome.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnHome.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnHome.UseVisualStyleBackColor = false;
            this.btnHome.Click += new System.EventHandler(this.btnHome_Click);
            //
            // btnDebug
            //
            this.btnDebug.BackColor = System.Drawing.Color.Transparent;
            this.btnDebug.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnDebug.FlatAppearance.BorderSize = 0;
            this.btnDebug.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnDebug.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnDebug.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDebug.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnDebug.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnDebug.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnDebug.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnDebug.Name = "btnDebug";
            this.btnDebug.Text = "调试";
            this.btnDebug.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnDebug.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnDebug.UseVisualStyleBackColor = false;
            this.btnDebug.Click += new System.EventHandler(this.btnDebug_Click);
            //
            // btnVideo
            //
            this.btnVideo.BackColor = System.Drawing.Color.Transparent;
            this.btnVideo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnVideo.FlatAppearance.BorderSize = 0;
            this.btnVideo.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnVideo.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnVideo.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnVideo.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnVideo.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnVideo.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnVideo.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnVideo.Name = "btnVideo";
            this.btnVideo.Text = "CCD";
            this.btnVideo.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnVideo.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnVideo.UseVisualStyleBackColor = false;
            this.btnVideo.Click += new System.EventHandler(this.btnVideo_Click);
            //
            // btnAlarm
            //
            this.btnAlarm.BackColor = System.Drawing.Color.Transparent;
            this.btnAlarm.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnAlarm.FlatAppearance.BorderSize = 0;
            this.btnAlarm.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnAlarm.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnAlarm.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAlarm.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnAlarm.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnAlarm.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnAlarm.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnAlarm.Name = "btnAlarm";
            this.btnAlarm.Text = "报警";
            this.btnAlarm.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnAlarm.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnAlarm.UseVisualStyleBackColor = false;
            this.btnAlarm.Click += new System.EventHandler(this.btnAlarm_Click);
            //
            // btnData
            //
            this.btnData.BackColor = System.Drawing.Color.Transparent;
            this.btnData.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnData.FlatAppearance.BorderSize = 0;
            this.btnData.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnData.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnData.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnData.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnData.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnData.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnData.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnData.Name = "btnData";
            this.btnData.Text = "数据";
            this.btnData.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnData.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnData.UseVisualStyleBackColor = false;
            this.btnData.Click += new System.EventHandler(this.btnData_Click);
            //
            // btnStart
            //
            this.btnStart.BackColor = System.Drawing.Color.Transparent;
            this.btnStart.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStart.FlatAppearance.BorderSize = 0;
            this.btnStart.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnStart.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnStart.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnStart.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnStart.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnStart.Name = "btnStart";
            this.btnStart.Text = "启动";
            this.btnStart.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnStart.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnStart.UseVisualStyleBackColor = false;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            //
            // btnStop
            //
            this.btnStop.BackColor = System.Drawing.Color.Transparent;
            this.btnStop.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStop.FlatAppearance.BorderSize = 0;
            this.btnStop.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnStop.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStop.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnStop.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnStop.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnStop.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnStop.Name = "btnStop";
            this.btnStop.Text = "停止";
            this.btnStop.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnStop.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnStop.UseVisualStyleBackColor = false;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            //
            // btnPause
            //
            this.btnPause.BackColor = System.Drawing.Color.Transparent;
            this.btnPause.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnPause.FlatAppearance.BorderSize = 0;
            this.btnPause.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnPause.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnPause.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPause.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnPause.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnPause.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnPause.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnPause.Name = "btnPause";
            this.btnPause.Text = "暂停";
            this.btnPause.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnPause.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnPause.UseVisualStyleBackColor = false;
            this.btnPause.Click += new System.EventHandler(this.btnPause_Click);
            //
            // btnExcel
            //
            this.btnExcel.BackColor = System.Drawing.Color.Transparent;
            this.btnExcel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnExcel.FlatAppearance.BorderSize = 0;
            this.btnExcel.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnExcel.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnExcel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnExcel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnExcel.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnExcel.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnExcel.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnExcel.Name = "btnExcel";
            this.btnExcel.Text = "Excel";
            this.btnExcel.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnExcel.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnExcel.UseVisualStyleBackColor = false;
            this.btnExcel.Click += new System.EventHandler(this.btnExcel_Click);
            //
            // btnLog
            //
            this.btnLog.BackColor = System.Drawing.Color.Transparent;
            this.btnLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnLog.FlatAppearance.BorderSize = 0;
            this.btnLog.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnLog.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnLog.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLog.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnLog.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnLog.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnLog.Margin = new System.Windows.Forms.Padding(3, 10, 3, 3);
            this.btnLog.Name = "btnLog";
            this.btnLog.Text = "Log";
            this.btnLog.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnLog.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnLog.UseVisualStyleBackColor = false;
            this.btnLog.Click += new System.EventHandler(this.btnLog_Click);
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
            this.statusLayout.ColumnCount = 2;
            this.statusLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.statusLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 88F));
            this.statusLayout.Controls.Add(this.lblFixtureStatus, 0, 0);
            this.statusLayout.Controls.Add(this.btnAccount, 1, 0);
            this.statusLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusLayout.Margin = new System.Windows.Forms.Padding(0);
            this.statusLayout.Name = "statusLayout";
            this.statusLayout.RowCount = 1;
            this.statusLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // lblFixtureStatus
            //
            this.lblFixtureStatus.BackColor = System.Drawing.SystemColors.ControlLight;
            this.lblFixtureStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFixtureStatus.Font = new System.Drawing.Font("微软雅黑", 11F);
            this.lblFixtureStatus.Margin = new System.Windows.Forms.Padding(3, 10, 3, 10);
            this.lblFixtureStatus.Name = "lblFixtureStatus";
            this.lblFixtureStatus.Text = "None";
            this.lblFixtureStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblFixtureStatus.Click += new System.EventHandler(this.lblFixtureStatus_Click);
            //
            // btnAccount
            //
            this.btnAccount.AccessibleName = "账户：未登录";
            this.btnAccount.BackColor = System.Drawing.Color.Transparent;
            this.btnAccount.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnAccount.FlatAppearance.BorderSize = 0;
            this.btnAccount.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnAccount.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(239, 246, 255);
            this.btnAccount.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAccount.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
            this.btnAccount.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.btnAccount.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnAccount.Margin = new System.Windows.Forms.Padding(3, 10, 6, 3);
            this.btnAccount.Name = "btnAccount";
            this.btnAccount.Text = "登录";
            this.btnAccount.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnAccount.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnAccount.UseVisualStyleBackColor = false;
            this.btnAccount.Click += new System.EventHandler(this.btnAccount_Click);
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
            // lblDate
            //
            this.lblDate.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDate.Font = new System.Drawing.Font("宋体", 10.5F);
            this.lblDate.Name = "lblDate";
            this.lblDate.Text = "2026-07-24";
            this.lblDate.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // lblTime
            //
            this.lblTime.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblTime.Font = new System.Drawing.Font("宋体", 10.5F);
            this.lblTime.Name = "lblTime";
            this.lblTime.Text = "20:00:00";
            this.lblTime.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // lblFooterUser
            //
            this.lblFooterUser.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFooterUser.Font = new System.Drawing.Font("宋体", 10.5F);
            this.lblFooterUser.Name = "lblFooterUser";
            this.lblFooterUser.Text = "User";
            this.lblFooterUser.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // lblCompany
            //
            this.lblCompany.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCompany.Font = new System.Drawing.Font("宋体", 10.5F);
            this.lblCompany.Name = "lblCompany";
            this.lblCompany.Text = "联合东创科技有限公司";
            this.lblCompany.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // lblPhone
            //
            this.lblPhone.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblPhone.Font = new System.Drawing.Font("宋体", 10.5F);
            this.lblPhone.Name = "lblPhone";
            this.lblPhone.Text = "电话:0769—39026833";
            this.lblPhone.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // lblVersion
            //
            this.lblVersion.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblVersion.Font = new System.Drawing.Font("宋体", 10.5F);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Text = "版本号: V3.0.0";
            this.lblVersion.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
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
        private System.Windows.Forms.Label lblFixtureStatus;
        private System.Windows.Forms.Button btnAccount;
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
