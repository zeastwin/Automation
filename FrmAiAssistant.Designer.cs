namespace Automation
{
    partial class FrmAiAssistant
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            this.layoutRoot = new System.Windows.Forms.TableLayoutPanel();
            this.headerPanel = new System.Windows.Forms.Panel();
            this.lblSubtitle = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblRevision = new System.Windows.Forms.Label();
            this.footerPanel = new System.Windows.Forms.Panel();
            this.btnExportTrace = new System.Windows.Forms.Button();
            this.btnRollback = new System.Windows.Forms.Button();
            this.btnApply = new System.Windows.Forms.Button();
            this.btnSimRun = new System.Windows.Forms.Button();
            this.btnVerifyRun = new System.Windows.Forms.Button();
            this.btnLoadDelta = new System.Windows.Forms.Button();
            this.btnLoadCore = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.navPanel = new System.Windows.Forms.Panel();
            this.navGroup = new System.Windows.Forms.GroupBox();
            this.btnNavTelemetry = new System.Windows.Forms.Button();
            this.btnNavDiff = new System.Windows.Forms.Button();
            this.btnNavSim = new System.Windows.Forms.Button();
            this.btnNavVerify = new System.Windows.Forms.Button();
            this.btnNavPropose = new System.Windows.Forms.Button();
            this.quickGroup = new System.Windows.Forms.GroupBox();
            this.lblQuick = new System.Windows.Forms.Label();
            this.tabMain = new System.Windows.Forms.TabControl();
            this.tabPropose = new System.Windows.Forms.TabPage();
            this.layoutPropose = new System.Windows.Forms.TableLayoutPanel();
            this.groupRequest = new System.Windows.Forms.GroupBox();
            this.txtRequest = new System.Windows.Forms.TextBox();
            this.groupOutput = new System.Windows.Forms.GroupBox();
            this.txtOutput = new System.Windows.Forms.TextBox();
            this.groupContext = new System.Windows.Forms.GroupBox();
            this.txtContext = new System.Windows.Forms.TextBox();
            this.groupConfig = new System.Windows.Forms.GroupBox();
            this.layoutConfig = new System.Windows.Forms.TableLayoutPanel();
            this.lblAiEndpoint = new System.Windows.Forms.Label();
            this.txtAiEndpoint = new System.Windows.Forms.TextBox();
            this.lblAiKey = new System.Windows.Forms.Label();
            this.txtAiKey = new System.Windows.Forms.TextBox();
            this.lblAiModel = new System.Windows.Forms.Label();
            this.txtAiModel = new System.Windows.Forms.TextBox();
            this.lblAiAuthHeader = new System.Windows.Forms.Label();
            this.txtAiAuthHeader = new System.Windows.Forms.TextBox();
            this.lblAiAuthPrefix = new System.Windows.Forms.Label();
            this.txtAiAuthPrefix = new System.Windows.Forms.TextBox();
            this.lblAiTimeout = new System.Windows.Forms.Label();
            this.numAiTimeout = new System.Windows.Forms.NumericUpDown();
            this.lblAiOutputKind = new System.Windows.Forms.Label();
            this.cmbAiOutputKind = new System.Windows.Forms.ComboBox();
            this.panelAiActions = new System.Windows.Forms.FlowLayoutPanel();
            this.chkAiShowKey = new System.Windows.Forms.CheckBox();
            this.btnAiLoadConfig = new System.Windows.Forms.Button();
            this.btnAiSaveConfig = new System.Windows.Forms.Button();
            this.btnAiGenerate = new System.Windows.Forms.Button();
            this.tabVerify = new System.Windows.Forms.TabPage();
            this.layoutVerify = new System.Windows.Forms.TableLayoutPanel();
            this.groupRules = new System.Windows.Forms.GroupBox();
            this.listRules = new System.Windows.Forms.ListBox();
            this.groupDiagnostics = new System.Windows.Forms.GroupBox();
            this.txtDiagnostics = new System.Windows.Forms.TextBox();
            this.tabSim = new System.Windows.Forms.TabPage();
            this.layoutSim = new System.Windows.Forms.TableLayoutPanel();
            this.groupScenario = new System.Windows.Forms.GroupBox();
            this.txtScenario = new System.Windows.Forms.TextBox();
            this.groupTrace = new System.Windows.Forms.GroupBox();
            this.txtTrace = new System.Windows.Forms.TextBox();
            this.tabDiff = new System.Windows.Forms.TabPage();
            this.layoutDiff = new System.Windows.Forms.TableLayoutPanel();
            this.groupDiff = new System.Windows.Forms.GroupBox();
            this.txtDiff = new System.Windows.Forms.TextBox();
            this.groupSummary = new System.Windows.Forms.GroupBox();
            this.listSummary = new System.Windows.Forms.ListBox();
            this.groupRevisions = new System.Windows.Forms.GroupBox();
            this.listRevisions = new System.Windows.Forms.ListBox();
            this.tabTelemetry = new System.Windows.Forms.TabPage();
            this.layoutTelemetry = new System.Windows.Forms.TableLayoutPanel();
            this.groupTelemetry = new System.Windows.Forms.GroupBox();
            this.txtTelemetry = new System.Windows.Forms.TextBox();
            this.groupBackflow = new System.Windows.Forms.GroupBox();
            this.txtBackflow = new System.Windows.Forms.TextBox();
            this.layoutRoot.SuspendLayout();
            this.headerPanel.SuspendLayout();
            this.footerPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.navPanel.SuspendLayout();
            this.navGroup.SuspendLayout();
            this.quickGroup.SuspendLayout();
            this.tabMain.SuspendLayout();
            this.tabPropose.SuspendLayout();
            this.layoutPropose.SuspendLayout();
            this.groupRequest.SuspendLayout();
            this.groupOutput.SuspendLayout();
            this.groupConfig.SuspendLayout();
            this.layoutConfig.SuspendLayout();
            this.panelAiActions.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numAiTimeout)).BeginInit();
            this.groupContext.SuspendLayout();
            this.tabVerify.SuspendLayout();
            this.layoutVerify.SuspendLayout();
            this.groupRules.SuspendLayout();
            this.groupDiagnostics.SuspendLayout();
            this.tabSim.SuspendLayout();
            this.layoutSim.SuspendLayout();
            this.groupScenario.SuspendLayout();
            this.groupTrace.SuspendLayout();
            this.tabDiff.SuspendLayout();
            this.layoutDiff.SuspendLayout();
            this.groupDiff.SuspendLayout();
            this.groupSummary.SuspendLayout();
            this.groupRevisions.SuspendLayout();
            this.tabTelemetry.SuspendLayout();
            this.layoutTelemetry.SuspendLayout();
            this.groupTelemetry.SuspendLayout();
            this.groupBackflow.SuspendLayout();
            this.SuspendLayout();
            // 
            // layoutRoot
            // 
            this.layoutRoot.ColumnCount = 1;
            this.layoutRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutRoot.Controls.Add(this.headerPanel, 0, 0);
            this.layoutRoot.Controls.Add(this.footerPanel, 0, 2);
            this.layoutRoot.Controls.Add(this.splitMain, 0, 1);
            this.layoutRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutRoot.Location = new System.Drawing.Point(0, 0);
            this.layoutRoot.Name = "layoutRoot";
            this.layoutRoot.RowCount = 3;
            this.layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 64F));
            this.layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 56F));
            this.layoutRoot.Size = new System.Drawing.Size(1200, 720);
            this.layoutRoot.TabIndex = 0;
            // 
            // headerPanel
            // 
            this.headerPanel.BackColor = System.Drawing.Color.DarkSlateGray;
            this.headerPanel.Controls.Add(this.lblRevision);
            this.headerPanel.Controls.Add(this.lblSubtitle);
            this.headerPanel.Controls.Add(this.lblTitle);
            this.headerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.headerPanel.Location = new System.Drawing.Point(0, 0);
            this.headerPanel.Margin = new System.Windows.Forms.Padding(0);
            this.headerPanel.Name = "headerPanel";
            this.headerPanel.Size = new System.Drawing.Size(1200, 64);
            this.headerPanel.TabIndex = 0;
            // 
            // lblSubtitle
            // 
            this.lblSubtitle.AutoSize = true;
            this.lblSubtitle.Font = new System.Drawing.Font("黑体", 11F);
            this.lblSubtitle.ForeColor = System.Drawing.Color.PaleTurquoise;
            this.lblSubtitle.Location = new System.Drawing.Point(20, 38);
            this.lblSubtitle.Name = "lblSubtitle";
            this.lblSubtitle.Size = new System.Drawing.Size(296, 15);
            this.lblSubtitle.TabIndex = 1;
            this.lblSubtitle.Text = "提案 → 验证 → 仿真 → Diff/回滚";
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("黑体", 16F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            this.lblTitle.Location = new System.Drawing.Point(18, 12);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(120, 22);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "AI流程助手";
            // 
            // lblRevision
            // 
            this.lblRevision.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.lblRevision.AutoSize = true;
            this.lblRevision.Font = new System.Drawing.Font("黑体", 11F);
            this.lblRevision.ForeColor = System.Drawing.Color.WhiteSmoke;
            this.lblRevision.Location = new System.Drawing.Point(960, 24);
            this.lblRevision.Name = "lblRevision";
            this.lblRevision.Size = new System.Drawing.Size(187, 15);
            this.lblRevision.TabIndex = 2;
            this.lblRevision.Text = "Revision: 未加载";
            // 
            // footerPanel
            // 
            this.footerPanel.BackColor = System.Drawing.Color.Gainsboro;
            this.footerPanel.Controls.Add(this.lblStatus);
            this.footerPanel.Controls.Add(this.btnExportTrace);
            this.footerPanel.Controls.Add(this.btnRollback);
            this.footerPanel.Controls.Add(this.btnApply);
            this.footerPanel.Controls.Add(this.btnSimRun);
            this.footerPanel.Controls.Add(this.btnVerifyRun);
            this.footerPanel.Controls.Add(this.btnLoadDelta);
            this.footerPanel.Controls.Add(this.btnLoadCore);
            this.footerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.footerPanel.Location = new System.Drawing.Point(0, 664);
            this.footerPanel.Margin = new System.Windows.Forms.Padding(0);
            this.footerPanel.Name = "footerPanel";
            this.footerPanel.Size = new System.Drawing.Size(1200, 56);
            this.footerPanel.TabIndex = 2;
            // 
            // btnExportTrace
            // 
            this.btnExportTrace.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.btnExportTrace.Location = new System.Drawing.Point(1004, 14);
            this.btnExportTrace.Name = "btnExportTrace";
            this.btnExportTrace.Size = new System.Drawing.Size(84, 28);
            this.btnExportTrace.TabIndex = 7;
            this.btnExportTrace.Text = "导出Trace";
            this.btnExportTrace.UseVisualStyleBackColor = true;
            // 
            // btnRollback
            // 
            this.btnRollback.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.btnRollback.Location = new System.Drawing.Point(912, 14);
            this.btnRollback.Name = "btnRollback";
            this.btnRollback.Size = new System.Drawing.Size(84, 28);
            this.btnRollback.TabIndex = 6;
            this.btnRollback.Text = "回滚";
            this.btnRollback.UseVisualStyleBackColor = true;
            // 
            // btnApply
            // 
            this.btnApply.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.btnApply.Location = new System.Drawing.Point(820, 14);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(84, 28);
            this.btnApply.TabIndex = 5;
            this.btnApply.Text = "落盘";
            this.btnApply.UseVisualStyleBackColor = true;
            // 
            // btnSimRun
            // 
            this.btnSimRun.Location = new System.Drawing.Point(364, 14);
            this.btnSimRun.Name = "btnSimRun";
            this.btnSimRun.Size = new System.Drawing.Size(84, 28);
            this.btnSimRun.TabIndex = 4;
            this.btnSimRun.Text = "仿真";
            this.btnSimRun.UseVisualStyleBackColor = true;
            // 
            // btnVerifyRun
            // 
            this.btnVerifyRun.Location = new System.Drawing.Point(272, 14);
            this.btnVerifyRun.Name = "btnVerifyRun";
            this.btnVerifyRun.Size = new System.Drawing.Size(84, 28);
            this.btnVerifyRun.TabIndex = 3;
            this.btnVerifyRun.Text = "验证";
            this.btnVerifyRun.UseVisualStyleBackColor = true;
            // 
            // btnLoadDelta
            // 
            this.btnLoadDelta.Location = new System.Drawing.Point(156, 14);
            this.btnLoadDelta.Name = "btnLoadDelta";
            this.btnLoadDelta.Size = new System.Drawing.Size(108, 28);
            this.btnLoadDelta.TabIndex = 2;
            this.btnLoadDelta.Text = "加载Delta";
            this.btnLoadDelta.UseVisualStyleBackColor = true;
            // 
            // btnLoadCore
            // 
            this.btnLoadCore.Location = new System.Drawing.Point(20, 14);
            this.btnLoadCore.Name = "btnLoadCore";
            this.btnLoadCore.Size = new System.Drawing.Size(128, 28);
            this.btnLoadCore.TabIndex = 1;
            this.btnLoadCore.Text = "加载Core/Spec";
            this.btnLoadCore.UseVisualStyleBackColor = true;
            // 
            // lblStatus
            // 
            this.lblStatus.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("黑体", 10F);
            this.lblStatus.Location = new System.Drawing.Point(470, 20);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(119, 14);
            this.lblStatus.TabIndex = 8;
            this.lblStatus.Text = "状态：未就绪";
            // 
            // splitMain
            // 
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitMain.IsSplitterFixed = false;
            this.splitMain.Location = new System.Drawing.Point(0, 64);
            this.splitMain.Margin = new System.Windows.Forms.Padding(0);
            this.splitMain.Name = "splitMain";
            // 
            // splitMain.Panel1
            // 
            this.splitMain.Panel1.Controls.Add(this.navPanel);
            // 
            // splitMain.Panel2
            // 
            this.splitMain.Panel2.Controls.Add(this.tabMain);
            this.splitMain.Size = new System.Drawing.Size(1200, 600);
            this.splitMain.SplitterDistance = 220;
            this.splitMain.TabIndex = 1;
            // 
            // navPanel
            // 
            this.navPanel.Controls.Add(this.quickGroup);
            this.navPanel.Controls.Add(this.navGroup);
            this.navPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.navPanel.Location = new System.Drawing.Point(0, 0);
            this.navPanel.Name = "navPanel";
            this.navPanel.Size = new System.Drawing.Size(220, 600);
            this.navPanel.TabIndex = 0;
            // 
            // navGroup
            // 
            this.navGroup.Controls.Add(this.btnNavTelemetry);
            this.navGroup.Controls.Add(this.btnNavDiff);
            this.navGroup.Controls.Add(this.btnNavSim);
            this.navGroup.Controls.Add(this.btnNavVerify);
            this.navGroup.Controls.Add(this.btnNavPropose);
            this.navGroup.Dock = System.Windows.Forms.DockStyle.Top;
            this.navGroup.Font = new System.Drawing.Font("黑体", 11F);
            this.navGroup.Location = new System.Drawing.Point(0, 0);
            this.navGroup.Name = "navGroup";
            this.navGroup.Size = new System.Drawing.Size(220, 270);
            this.navGroup.TabIndex = 0;
            this.navGroup.TabStop = false;
            this.navGroup.Text = "导航";
            // 
            // btnNavTelemetry
            // 
            this.btnNavTelemetry.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnNavTelemetry.Location = new System.Drawing.Point(3, 203);
            this.btnNavTelemetry.Name = "btnNavTelemetry";
            this.btnNavTelemetry.Size = new System.Drawing.Size(214, 40);
            this.btnNavTelemetry.TabIndex = 4;
            this.btnNavTelemetry.Text = "Telemetry";
            this.btnNavTelemetry.UseVisualStyleBackColor = true;
            this.btnNavTelemetry.Click += new System.EventHandler(this.btnNavTelemetry_Click);
            // 
            // btnNavDiff
            // 
            this.btnNavDiff.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnNavDiff.Location = new System.Drawing.Point(3, 163);
            this.btnNavDiff.Name = "btnNavDiff";
            this.btnNavDiff.Size = new System.Drawing.Size(214, 40);
            this.btnNavDiff.TabIndex = 3;
            this.btnNavDiff.Text = "Diff/回滚";
            this.btnNavDiff.UseVisualStyleBackColor = true;
            this.btnNavDiff.Click += new System.EventHandler(this.btnNavDiff_Click);
            // 
            // btnNavSim
            // 
            this.btnNavSim.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnNavSim.Location = new System.Drawing.Point(3, 123);
            this.btnNavSim.Name = "btnNavSim";
            this.btnNavSim.Size = new System.Drawing.Size(214, 40);
            this.btnNavSim.TabIndex = 2;
            this.btnNavSim.Text = "仿真";
            this.btnNavSim.UseVisualStyleBackColor = true;
            this.btnNavSim.Click += new System.EventHandler(this.btnNavSim_Click);
            // 
            // btnNavVerify
            // 
            this.btnNavVerify.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnNavVerify.Location = new System.Drawing.Point(3, 83);
            this.btnNavVerify.Name = "btnNavVerify";
            this.btnNavVerify.Size = new System.Drawing.Size(214, 40);
            this.btnNavVerify.TabIndex = 1;
            this.btnNavVerify.Text = "验证";
            this.btnNavVerify.UseVisualStyleBackColor = true;
            this.btnNavVerify.Click += new System.EventHandler(this.btnNavVerify_Click);
            // 
            // btnNavPropose
            // 
            this.btnNavPropose.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnNavPropose.Location = new System.Drawing.Point(3, 43);
            this.btnNavPropose.Name = "btnNavPropose";
            this.btnNavPropose.Size = new System.Drawing.Size(214, 40);
            this.btnNavPropose.TabIndex = 0;
            this.btnNavPropose.Text = "提案";
            this.btnNavPropose.UseVisualStyleBackColor = true;
            this.btnNavPropose.Click += new System.EventHandler(this.btnNavPropose_Click);
            // 
            // quickGroup
            // 
            this.quickGroup.Controls.Add(this.lblQuick);
            this.quickGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            this.quickGroup.Font = new System.Drawing.Font("黑体", 11F);
            this.quickGroup.Location = new System.Drawing.Point(0, 270);
            this.quickGroup.Name = "quickGroup";
            this.quickGroup.Size = new System.Drawing.Size(220, 330);
            this.quickGroup.TabIndex = 1;
            this.quickGroup.TabStop = false;
            this.quickGroup.Text = "概览";
            // 
            // lblQuick
            // 
            this.lblQuick.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblQuick.Font = new System.Drawing.Font("黑体", 10F);
            this.lblQuick.Location = new System.Drawing.Point(3, 20);
            this.lblQuick.Name = "lblQuick";
            this.lblQuick.Size = new System.Drawing.Size(214, 307);
            this.lblQuick.TabIndex = 0;
            this.lblQuick.Text = "当前流程：未选择\r\n校验状态：未执行\r\n仿真状态：未执行\r\n变更：0";
            this.lblQuick.TextAlign = System.Drawing.ContentAlignment.TopLeft;
            // 
            // tabMain
            // 
            this.tabMain.Controls.Add(this.tabPropose);
            this.tabMain.Controls.Add(this.tabVerify);
            this.tabMain.Controls.Add(this.tabSim);
            this.tabMain.Controls.Add(this.tabDiff);
            this.tabMain.Controls.Add(this.tabTelemetry);
            this.tabMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabMain.Location = new System.Drawing.Point(0, 0);
            this.tabMain.Name = "tabMain";
            this.tabMain.SelectedIndex = 0;
            this.tabMain.Size = new System.Drawing.Size(976, 600);
            this.tabMain.TabIndex = 0;
            // 
            // tabPropose
            // 
            this.tabPropose.Controls.Add(this.layoutPropose);
            this.tabPropose.Location = new System.Drawing.Point(4, 22);
            this.tabPropose.Name = "tabPropose";
            this.tabPropose.Padding = new System.Windows.Forms.Padding(3);
            this.tabPropose.Size = new System.Drawing.Size(968, 574);
            this.tabPropose.TabIndex = 0;
            this.tabPropose.Text = "提案";
            this.tabPropose.UseVisualStyleBackColor = true;
            // 
            // layoutPropose
            // 
            this.layoutPropose.ColumnCount = 2;
            this.layoutPropose.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.layoutPropose.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.layoutPropose.Controls.Add(this.groupRequest, 0, 0);
            this.layoutPropose.Controls.Add(this.groupOutput, 1, 0);
            this.layoutPropose.Controls.Add(this.groupConfig, 0, 1);
            this.layoutPropose.Controls.Add(this.groupContext, 0, 2);
            this.layoutPropose.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutPropose.Location = new System.Drawing.Point(3, 3);
            this.layoutPropose.Name = "layoutPropose";
            this.layoutPropose.RowCount = 3;
            this.layoutPropose.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 55F));
            this.layoutPropose.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 180F));
            this.layoutPropose.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 45F));
            this.layoutPropose.Size = new System.Drawing.Size(962, 568);
            this.layoutPropose.TabIndex = 0;
            // 
            // groupRequest
            // 
            this.groupRequest.Controls.Add(this.txtRequest);
            this.groupRequest.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupRequest.Font = new System.Drawing.Font("黑体", 11F);
            this.groupRequest.Location = new System.Drawing.Point(3, 3);
            this.groupRequest.Name = "groupRequest";
            this.groupRequest.Size = new System.Drawing.Size(475, 334);
            this.groupRequest.TabIndex = 0;
            this.groupRequest.TabStop = false;
            this.groupRequest.Text = "需求描述";
            // 
            // txtRequest
            // 
            this.txtRequest.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtRequest.Font = new System.Drawing.Font("黑体", 10F);
            this.txtRequest.Location = new System.Drawing.Point(3, 20);
            this.txtRequest.Multiline = true;
            this.txtRequest.Name = "txtRequest";
            this.txtRequest.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtRequest.Size = new System.Drawing.Size(469, 311);
            this.txtRequest.TabIndex = 0;
            // 
            // groupOutput
            // 
            this.groupOutput.Controls.Add(this.txtOutput);
            this.groupOutput.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupOutput.Font = new System.Drawing.Font("黑体", 11F);
            this.groupOutput.Location = new System.Drawing.Point(484, 3);
            this.groupOutput.Name = "groupOutput";
            this.groupOutput.Size = new System.Drawing.Size(475, 334);
            this.groupOutput.TabIndex = 1;
            this.groupOutput.TabStop = false;
            this.groupOutput.Text = "AI 输出（FlowDelta/Core）";
            // 
            // txtOutput
            // 
            this.txtOutput.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtOutput.Font = new System.Drawing.Font("黑体", 10F);
            this.txtOutput.Location = new System.Drawing.Point(3, 20);
            this.txtOutput.Multiline = true;
            this.txtOutput.Name = "txtOutput";
            this.txtOutput.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtOutput.Size = new System.Drawing.Size(469, 311);
            this.txtOutput.TabIndex = 0;
            // 
            // groupConfig
            // 
            this.layoutPropose.SetColumnSpan(this.groupConfig, 2);
            this.groupConfig.Controls.Add(this.layoutConfig);
            this.groupConfig.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupConfig.Font = new System.Drawing.Font("黑体", 11F);
            this.groupConfig.Location = new System.Drawing.Point(3, 343);
            this.groupConfig.Name = "groupConfig";
            this.groupConfig.Size = new System.Drawing.Size(956, 174);
            this.groupConfig.TabIndex = 2;
            this.groupConfig.TabStop = false;
            this.groupConfig.Text = "AI 接口配置";
            // 
            // layoutConfig
            // 
            this.layoutConfig.ColumnCount = 4;
            this.layoutConfig.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            this.layoutConfig.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.layoutConfig.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            this.layoutConfig.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.layoutConfig.Controls.Add(this.lblAiEndpoint, 0, 0);
            this.layoutConfig.Controls.Add(this.txtAiEndpoint, 1, 0);
            this.layoutConfig.Controls.Add(this.lblAiKey, 0, 1);
            this.layoutConfig.Controls.Add(this.txtAiKey, 1, 1);
            this.layoutConfig.Controls.Add(this.lblAiModel, 2, 1);
            this.layoutConfig.Controls.Add(this.txtAiModel, 3, 1);
            this.layoutConfig.Controls.Add(this.lblAiAuthHeader, 0, 2);
            this.layoutConfig.Controls.Add(this.txtAiAuthHeader, 1, 2);
            this.layoutConfig.Controls.Add(this.lblAiAuthPrefix, 2, 2);
            this.layoutConfig.Controls.Add(this.txtAiAuthPrefix, 3, 2);
            this.layoutConfig.Controls.Add(this.lblAiTimeout, 0, 3);
            this.layoutConfig.Controls.Add(this.numAiTimeout, 1, 3);
            this.layoutConfig.Controls.Add(this.lblAiOutputKind, 2, 3);
            this.layoutConfig.Controls.Add(this.cmbAiOutputKind, 3, 3);
            this.layoutConfig.Controls.Add(this.panelAiActions, 0, 4);
            this.layoutConfig.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutConfig.Location = new System.Drawing.Point(3, 20);
            this.layoutConfig.Name = "layoutConfig";
            this.layoutConfig.RowCount = 5;
            this.layoutConfig.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 28F));
            this.layoutConfig.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 28F));
            this.layoutConfig.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 28F));
            this.layoutConfig.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 28F));
            this.layoutConfig.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutConfig.Size = new System.Drawing.Size(950, 151);
            this.layoutConfig.TabIndex = 0;
            // 
            // lblAiEndpoint
            // 
            this.lblAiEndpoint.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAiEndpoint.AutoSize = true;
            this.lblAiEndpoint.Font = new System.Drawing.Font("黑体", 10F);
            this.lblAiEndpoint.Location = new System.Drawing.Point(3, 7);
            this.lblAiEndpoint.Name = "lblAiEndpoint";
            this.lblAiEndpoint.Size = new System.Drawing.Size(63, 14);
            this.lblAiEndpoint.TabIndex = 0;
            this.lblAiEndpoint.Text = "接口地址";
            // 
            // txtAiEndpoint
            // 
            this.layoutConfig.SetColumnSpan(this.txtAiEndpoint, 3);
            this.txtAiEndpoint.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtAiEndpoint.Font = new System.Drawing.Font("黑体", 10F);
            this.txtAiEndpoint.Location = new System.Drawing.Point(93, 3);
            this.txtAiEndpoint.Name = "txtAiEndpoint";
            this.txtAiEndpoint.Size = new System.Drawing.Size(854, 23);
            this.txtAiEndpoint.TabIndex = 1;
            // 
            // lblAiKey
            // 
            this.lblAiKey.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAiKey.AutoSize = true;
            this.lblAiKey.Font = new System.Drawing.Font("黑体", 10F);
            this.lblAiKey.Location = new System.Drawing.Point(3, 35);
            this.lblAiKey.Name = "lblAiKey";
            this.lblAiKey.Size = new System.Drawing.Size(28, 14);
            this.lblAiKey.TabIndex = 2;
            this.lblAiKey.Text = "Key";
            // 
            // txtAiKey
            // 
            this.txtAiKey.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtAiKey.Font = new System.Drawing.Font("黑体", 10F);
            this.txtAiKey.Location = new System.Drawing.Point(93, 31);
            this.txtAiKey.Name = "txtAiKey";
            this.txtAiKey.Size = new System.Drawing.Size(382, 23);
            this.txtAiKey.TabIndex = 3;
            this.txtAiKey.UseSystemPasswordChar = true;
            // 
            // lblAiModel
            // 
            this.lblAiModel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAiModel.AutoSize = true;
            this.lblAiModel.Font = new System.Drawing.Font("黑体", 10F);
            this.lblAiModel.Location = new System.Drawing.Point(481, 35);
            this.lblAiModel.Name = "lblAiModel";
            this.lblAiModel.Size = new System.Drawing.Size(35, 14);
            this.lblAiModel.TabIndex = 4;
            this.lblAiModel.Text = "模型";
            // 
            // txtAiModel
            // 
            this.txtAiModel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtAiModel.Font = new System.Drawing.Font("黑体", 10F);
            this.txtAiModel.Location = new System.Drawing.Point(571, 31);
            this.txtAiModel.Name = "txtAiModel";
            this.txtAiModel.Size = new System.Drawing.Size(376, 23);
            this.txtAiModel.TabIndex = 5;
            // 
            // lblAiAuthHeader
            // 
            this.lblAiAuthHeader.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAiAuthHeader.AutoSize = true;
            this.lblAiAuthHeader.Font = new System.Drawing.Font("黑体", 10F);
            this.lblAiAuthHeader.Location = new System.Drawing.Point(3, 63);
            this.lblAiAuthHeader.Name = "lblAiAuthHeader";
            this.lblAiAuthHeader.Size = new System.Drawing.Size(63, 14);
            this.lblAiAuthHeader.TabIndex = 6;
            this.lblAiAuthHeader.Text = "鉴权头";
            // 
            // txtAiAuthHeader
            // 
            this.txtAiAuthHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtAiAuthHeader.Font = new System.Drawing.Font("黑体", 10F);
            this.txtAiAuthHeader.Location = new System.Drawing.Point(93, 59);
            this.txtAiAuthHeader.Name = "txtAiAuthHeader";
            this.txtAiAuthHeader.Size = new System.Drawing.Size(382, 23);
            this.txtAiAuthHeader.TabIndex = 7;
            // 
            // lblAiAuthPrefix
            // 
            this.lblAiAuthPrefix.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAiAuthPrefix.AutoSize = true;
            this.lblAiAuthPrefix.Font = new System.Drawing.Font("黑体", 10F);
            this.lblAiAuthPrefix.Location = new System.Drawing.Point(481, 63);
            this.lblAiAuthPrefix.Name = "lblAiAuthPrefix";
            this.lblAiAuthPrefix.Size = new System.Drawing.Size(63, 14);
            this.lblAiAuthPrefix.TabIndex = 8;
            this.lblAiAuthPrefix.Text = "鉴权前缀";
            // 
            // txtAiAuthPrefix
            // 
            this.txtAiAuthPrefix.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtAiAuthPrefix.Font = new System.Drawing.Font("黑体", 10F);
            this.txtAiAuthPrefix.Location = new System.Drawing.Point(571, 59);
            this.txtAiAuthPrefix.Name = "txtAiAuthPrefix";
            this.txtAiAuthPrefix.Size = new System.Drawing.Size(376, 23);
            this.txtAiAuthPrefix.TabIndex = 9;
            // 
            // lblAiTimeout
            // 
            this.lblAiTimeout.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAiTimeout.AutoSize = true;
            this.lblAiTimeout.Font = new System.Drawing.Font("黑体", 10F);
            this.lblAiTimeout.Location = new System.Drawing.Point(3, 91);
            this.lblAiTimeout.Name = "lblAiTimeout";
            this.lblAiTimeout.Size = new System.Drawing.Size(70, 14);
            this.lblAiTimeout.TabIndex = 10;
            this.lblAiTimeout.Text = "超时(秒)";
            // 
            // numAiTimeout
            // 
            this.numAiTimeout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.numAiTimeout.Font = new System.Drawing.Font("黑体", 10F);
            this.numAiTimeout.Location = new System.Drawing.Point(93, 87);
            this.numAiTimeout.Maximum = new decimal(new int[] {
            600,
            0,
            0,
            0});
            this.numAiTimeout.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numAiTimeout.Name = "numAiTimeout";
            this.numAiTimeout.Size = new System.Drawing.Size(382, 23);
            this.numAiTimeout.TabIndex = 11;
            this.numAiTimeout.Value = new decimal(new int[] {
            60,
            0,
            0,
            0});
            // 
            // lblAiOutputKind
            // 
            this.lblAiOutputKind.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAiOutputKind.AutoSize = true;
            this.lblAiOutputKind.Font = new System.Drawing.Font("黑体", 10F);
            this.lblAiOutputKind.Location = new System.Drawing.Point(481, 91);
            this.lblAiOutputKind.Name = "lblAiOutputKind";
            this.lblAiOutputKind.Size = new System.Drawing.Size(63, 14);
            this.lblAiOutputKind.TabIndex = 12;
            this.lblAiOutputKind.Text = "输出类型";
            // 
            // cmbAiOutputKind
            // 
            this.cmbAiOutputKind.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cmbAiOutputKind.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbAiOutputKind.Font = new System.Drawing.Font("黑体", 10F);
            this.cmbAiOutputKind.FormattingEnabled = true;
            this.cmbAiOutputKind.Location = new System.Drawing.Point(571, 87);
            this.cmbAiOutputKind.Name = "cmbAiOutputKind";
            this.cmbAiOutputKind.Size = new System.Drawing.Size(376, 21);
            this.cmbAiOutputKind.TabIndex = 13;
            // 
            // panelAiActions
            // 
            this.layoutConfig.SetColumnSpan(this.panelAiActions, 4);
            this.panelAiActions.Controls.Add(this.chkAiShowKey);
            this.panelAiActions.Controls.Add(this.btnAiLoadConfig);
            this.panelAiActions.Controls.Add(this.btnAiSaveConfig);
            this.panelAiActions.Controls.Add(this.btnAiGenerate);
            this.panelAiActions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelAiActions.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.panelAiActions.Location = new System.Drawing.Point(3, 115);
            this.panelAiActions.Name = "panelAiActions";
            this.panelAiActions.Size = new System.Drawing.Size(944, 33);
            this.panelAiActions.TabIndex = 14;
            // 
            // chkAiShowKey
            // 
            this.chkAiShowKey.AutoSize = true;
            this.chkAiShowKey.Font = new System.Drawing.Font("黑体", 10F);
            this.chkAiShowKey.Location = new System.Drawing.Point(3, 8);
            this.chkAiShowKey.Margin = new System.Windows.Forms.Padding(3, 8, 10, 3);
            this.chkAiShowKey.Name = "chkAiShowKey";
            this.chkAiShowKey.Size = new System.Drawing.Size(82, 18);
            this.chkAiShowKey.TabIndex = 0;
            this.chkAiShowKey.Text = "显示Key";
            this.chkAiShowKey.UseVisualStyleBackColor = true;
            // 
            // btnAiLoadConfig
            // 
            this.btnAiLoadConfig.Location = new System.Drawing.Point(98, 3);
            this.btnAiLoadConfig.Name = "btnAiLoadConfig";
            this.btnAiLoadConfig.Size = new System.Drawing.Size(90, 27);
            this.btnAiLoadConfig.TabIndex = 1;
            this.btnAiLoadConfig.Text = "加载配置";
            this.btnAiLoadConfig.UseVisualStyleBackColor = true;
            // 
            // btnAiSaveConfig
            // 
            this.btnAiSaveConfig.Location = new System.Drawing.Point(194, 3);
            this.btnAiSaveConfig.Name = "btnAiSaveConfig";
            this.btnAiSaveConfig.Size = new System.Drawing.Size(90, 27);
            this.btnAiSaveConfig.TabIndex = 2;
            this.btnAiSaveConfig.Text = "保存配置";
            this.btnAiSaveConfig.UseVisualStyleBackColor = true;
            // 
            // btnAiGenerate
            // 
            this.btnAiGenerate.Location = new System.Drawing.Point(290, 3);
            this.btnAiGenerate.Name = "btnAiGenerate";
            this.btnAiGenerate.Size = new System.Drawing.Size(90, 27);
            this.btnAiGenerate.TabIndex = 3;
            this.btnAiGenerate.Text = "生成";
            this.btnAiGenerate.UseVisualStyleBackColor = true;
            // 
            // groupContext
            // 
            this.layoutPropose.SetColumnSpan(this.groupContext, 2);
            this.groupContext.Controls.Add(this.txtContext);
            this.groupContext.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupContext.Font = new System.Drawing.Font("黑体", 11F);
            this.groupContext.Location = new System.Drawing.Point(3, 523);
            this.groupContext.Name = "groupContext";
            this.groupContext.Size = new System.Drawing.Size(956, 222);
            this.groupContext.TabIndex = 3;
            this.groupContext.TabStop = false;
            this.groupContext.Text = "上下文/资源";
            // 
            // txtContext
            // 
            this.txtContext.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtContext.Font = new System.Drawing.Font("黑体", 10F);
            this.txtContext.Location = new System.Drawing.Point(3, 20);
            this.txtContext.Multiline = true;
            this.txtContext.Name = "txtContext";
            this.txtContext.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtContext.Size = new System.Drawing.Size(950, 199);
            this.txtContext.TabIndex = 0;
            // 
            // tabVerify
            // 
            this.tabVerify.Controls.Add(this.layoutVerify);
            this.tabVerify.Location = new System.Drawing.Point(4, 22);
            this.tabVerify.Name = "tabVerify";
            this.tabVerify.Padding = new System.Windows.Forms.Padding(3);
            this.tabVerify.Size = new System.Drawing.Size(968, 574);
            this.tabVerify.TabIndex = 1;
            this.tabVerify.Text = "验证";
            this.tabVerify.UseVisualStyleBackColor = true;
            // 
            // layoutVerify
            // 
            this.layoutVerify.ColumnCount = 2;
            this.layoutVerify.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 40F));
            this.layoutVerify.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this.layoutVerify.Controls.Add(this.groupRules, 0, 0);
            this.layoutVerify.Controls.Add(this.groupDiagnostics, 1, 0);
            this.layoutVerify.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutVerify.Location = new System.Drawing.Point(3, 3);
            this.layoutVerify.Name = "layoutVerify";
            this.layoutVerify.RowCount = 1;
            this.layoutVerify.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutVerify.Size = new System.Drawing.Size(962, 568);
            this.layoutVerify.TabIndex = 0;
            // 
            // groupRules
            // 
            this.groupRules.Controls.Add(this.listRules);
            this.groupRules.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupRules.Font = new System.Drawing.Font("黑体", 11F);
            this.groupRules.Location = new System.Drawing.Point(3, 3);
            this.groupRules.Name = "groupRules";
            this.groupRules.Size = new System.Drawing.Size(378, 562);
            this.groupRules.TabIndex = 0;
            this.groupRules.TabStop = false;
            this.groupRules.Text = "规则清单";
            // 
            // listRules
            // 
            this.listRules.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listRules.Font = new System.Drawing.Font("黑体", 10F);
            this.listRules.FormattingEnabled = true;
            this.listRules.ItemHeight = 13;
            this.listRules.Location = new System.Drawing.Point(3, 20);
            this.listRules.Name = "listRules";
            this.listRules.Size = new System.Drawing.Size(372, 539);
            this.listRules.TabIndex = 0;
            // 
            // groupDiagnostics
            // 
            this.groupDiagnostics.Controls.Add(this.txtDiagnostics);
            this.groupDiagnostics.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupDiagnostics.Font = new System.Drawing.Font("黑体", 11F);
            this.groupDiagnostics.Location = new System.Drawing.Point(387, 3);
            this.groupDiagnostics.Name = "groupDiagnostics";
            this.groupDiagnostics.Size = new System.Drawing.Size(572, 562);
            this.groupDiagnostics.TabIndex = 1;
            this.groupDiagnostics.TabStop = false;
            this.groupDiagnostics.Text = "诊断详情";
            // 
            // txtDiagnostics
            // 
            this.txtDiagnostics.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtDiagnostics.Font = new System.Drawing.Font("黑体", 10F);
            this.txtDiagnostics.Location = new System.Drawing.Point(3, 20);
            this.txtDiagnostics.Multiline = true;
            this.txtDiagnostics.Name = "txtDiagnostics";
            this.txtDiagnostics.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtDiagnostics.Size = new System.Drawing.Size(566, 539);
            this.txtDiagnostics.TabIndex = 0;
            // 
            // tabSim
            // 
            this.tabSim.Controls.Add(this.layoutSim);
            this.tabSim.Location = new System.Drawing.Point(4, 22);
            this.tabSim.Name = "tabSim";
            this.tabSim.Padding = new System.Windows.Forms.Padding(3);
            this.tabSim.Size = new System.Drawing.Size(968, 574);
            this.tabSim.TabIndex = 2;
            this.tabSim.Text = "仿真";
            this.tabSim.UseVisualStyleBackColor = true;
            // 
            // layoutSim
            // 
            this.layoutSim.ColumnCount = 1;
            this.layoutSim.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutSim.Controls.Add(this.groupScenario, 0, 0);
            this.layoutSim.Controls.Add(this.groupTrace, 0, 1);
            this.layoutSim.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutSim.Location = new System.Drawing.Point(3, 3);
            this.layoutSim.Name = "layoutSim";
            this.layoutSim.RowCount = 2;
            this.layoutSim.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 45F));
            this.layoutSim.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 55F));
            this.layoutSim.Size = new System.Drawing.Size(962, 568);
            this.layoutSim.TabIndex = 0;
            // 
            // groupScenario
            // 
            this.groupScenario.Controls.Add(this.txtScenario);
            this.groupScenario.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupScenario.Font = new System.Drawing.Font("黑体", 11F);
            this.groupScenario.Location = new System.Drawing.Point(3, 3);
            this.groupScenario.Name = "groupScenario";
            this.groupScenario.Size = new System.Drawing.Size(956, 249);
            this.groupScenario.TabIndex = 0;
            this.groupScenario.TabStop = false;
            this.groupScenario.Text = "场景配置 (scenario.json)";
            // 
            // txtScenario
            // 
            this.txtScenario.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtScenario.Font = new System.Drawing.Font("黑体", 10F);
            this.txtScenario.Location = new System.Drawing.Point(3, 20);
            this.txtScenario.Multiline = true;
            this.txtScenario.Name = "txtScenario";
            this.txtScenario.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtScenario.Size = new System.Drawing.Size(950, 226);
            this.txtScenario.TabIndex = 0;
            // 
            // groupTrace
            // 
            this.groupTrace.Controls.Add(this.txtTrace);
            this.groupTrace.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupTrace.Font = new System.Drawing.Font("黑体", 11F);
            this.groupTrace.Location = new System.Drawing.Point(3, 258);
            this.groupTrace.Name = "groupTrace";
            this.groupTrace.Size = new System.Drawing.Size(956, 307);
            this.groupTrace.TabIndex = 1;
            this.groupTrace.TabStop = false;
            this.groupTrace.Text = "仿真 Trace";
            // 
            // txtTrace
            // 
            this.txtTrace.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtTrace.Font = new System.Drawing.Font("黑体", 10F);
            this.txtTrace.Location = new System.Drawing.Point(3, 20);
            this.txtTrace.Multiline = true;
            this.txtTrace.Name = "txtTrace";
            this.txtTrace.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtTrace.Size = new System.Drawing.Size(950, 284);
            this.txtTrace.TabIndex = 0;
            // 
            // tabDiff
            // 
            this.tabDiff.Controls.Add(this.layoutDiff);
            this.tabDiff.Location = new System.Drawing.Point(4, 22);
            this.tabDiff.Name = "tabDiff";
            this.tabDiff.Padding = new System.Windows.Forms.Padding(3);
            this.tabDiff.Size = new System.Drawing.Size(968, 574);
            this.tabDiff.TabIndex = 3;
            this.tabDiff.Text = "Diff/回滚";
            this.tabDiff.UseVisualStyleBackColor = true;
            // 
            // layoutDiff
            // 
            this.layoutDiff.ColumnCount = 2;
            this.layoutDiff.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this.layoutDiff.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 40F));
            this.layoutDiff.Controls.Add(this.groupDiff, 0, 0);
            this.layoutDiff.Controls.Add(this.groupSummary, 1, 0);
            this.layoutDiff.Controls.Add(this.groupRevisions, 1, 1);
            this.layoutDiff.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutDiff.Location = new System.Drawing.Point(3, 3);
            this.layoutDiff.Name = "layoutDiff";
            this.layoutDiff.RowCount = 2;
            this.layoutDiff.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this.layoutDiff.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 40F));
            this.layoutDiff.Size = new System.Drawing.Size(962, 568);
            this.layoutDiff.TabIndex = 0;
            // 
            // groupDiff
            // 
            this.groupDiff.Controls.Add(this.txtDiff);
            this.groupDiff.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupDiff.Font = new System.Drawing.Font("黑体", 11F);
            this.groupDiff.Location = new System.Drawing.Point(3, 3);
            this.groupDiff.Name = "groupDiff";
            this.layoutDiff.SetRowSpan(this.groupDiff, 2);
            this.groupDiff.Size = new System.Drawing.Size(571, 562);
            this.groupDiff.TabIndex = 0;
            this.groupDiff.TabStop = false;
            this.groupDiff.Text = "Diff 预览";
            // 
            // txtDiff
            // 
            this.txtDiff.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtDiff.Font = new System.Drawing.Font("黑体", 10F);
            this.txtDiff.Location = new System.Drawing.Point(3, 20);
            this.txtDiff.Multiline = true;
            this.txtDiff.Name = "txtDiff";
            this.txtDiff.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtDiff.Size = new System.Drawing.Size(565, 539);
            this.txtDiff.TabIndex = 0;
            // 
            // groupSummary
            // 
            this.groupSummary.Controls.Add(this.listSummary);
            this.groupSummary.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupSummary.Font = new System.Drawing.Font("黑体", 11F);
            this.groupSummary.Location = new System.Drawing.Point(580, 3);
            this.groupSummary.Name = "groupSummary";
            this.groupSummary.Size = new System.Drawing.Size(379, 334);
            this.groupSummary.TabIndex = 1;
            this.groupSummary.TabStop = false;
            this.groupSummary.Text = "变更摘要";
            // 
            // listSummary
            // 
            this.listSummary.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listSummary.Font = new System.Drawing.Font("黑体", 10F);
            this.listSummary.FormattingEnabled = true;
            this.listSummary.ItemHeight = 13;
            this.listSummary.Location = new System.Drawing.Point(3, 20);
            this.listSummary.Name = "listSummary";
            this.listSummary.Size = new System.Drawing.Size(373, 311);
            this.listSummary.TabIndex = 0;
            // 
            // groupRevisions
            // 
            this.groupRevisions.Controls.Add(this.listRevisions);
            this.groupRevisions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupRevisions.Font = new System.Drawing.Font("黑体", 11F);
            this.groupRevisions.Location = new System.Drawing.Point(580, 343);
            this.groupRevisions.Name = "groupRevisions";
            this.groupRevisions.Size = new System.Drawing.Size(379, 222);
            this.groupRevisions.TabIndex = 2;
            this.groupRevisions.TabStop = false;
            this.groupRevisions.Text = "回滚点";
            // 
            // listRevisions
            // 
            this.listRevisions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listRevisions.Font = new System.Drawing.Font("黑体", 10F);
            this.listRevisions.FormattingEnabled = true;
            this.listRevisions.ItemHeight = 13;
            this.listRevisions.Location = new System.Drawing.Point(3, 20);
            this.listRevisions.Name = "listRevisions";
            this.listRevisions.Size = new System.Drawing.Size(373, 199);
            this.listRevisions.TabIndex = 0;
            // 
            // tabTelemetry
            // 
            this.tabTelemetry.Controls.Add(this.layoutTelemetry);
            this.tabTelemetry.Location = new System.Drawing.Point(4, 22);
            this.tabTelemetry.Name = "tabTelemetry";
            this.tabTelemetry.Padding = new System.Windows.Forms.Padding(3);
            this.tabTelemetry.Size = new System.Drawing.Size(968, 574);
            this.tabTelemetry.TabIndex = 4;
            this.tabTelemetry.Text = "Telemetry";
            this.tabTelemetry.UseVisualStyleBackColor = true;
            // 
            // layoutTelemetry
            // 
            this.layoutTelemetry.ColumnCount = 1;
            this.layoutTelemetry.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutTelemetry.Controls.Add(this.groupTelemetry, 0, 0);
            this.layoutTelemetry.Controls.Add(this.groupBackflow, 0, 1);
            this.layoutTelemetry.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutTelemetry.Location = new System.Drawing.Point(3, 3);
            this.layoutTelemetry.Name = "layoutTelemetry";
            this.layoutTelemetry.RowCount = 2;
            this.layoutTelemetry.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.layoutTelemetry.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.layoutTelemetry.Size = new System.Drawing.Size(962, 568);
            this.layoutTelemetry.TabIndex = 0;
            // 
            // groupTelemetry
            // 
            this.groupTelemetry.Controls.Add(this.txtTelemetry);
            this.groupTelemetry.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupTelemetry.Font = new System.Drawing.Font("黑体", 11F);
            this.groupTelemetry.Location = new System.Drawing.Point(3, 3);
            this.groupTelemetry.Name = "groupTelemetry";
            this.groupTelemetry.Size = new System.Drawing.Size(956, 278);
            this.groupTelemetry.TabIndex = 0;
            this.groupTelemetry.TabStop = false;
            this.groupTelemetry.Text = "运行期采集";
            // 
            // txtTelemetry
            // 
            this.txtTelemetry.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtTelemetry.Font = new System.Drawing.Font("黑体", 10F);
            this.txtTelemetry.Location = new System.Drawing.Point(3, 20);
            this.txtTelemetry.Multiline = true;
            this.txtTelemetry.Name = "txtTelemetry";
            this.txtTelemetry.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtTelemetry.Size = new System.Drawing.Size(950, 255);
            this.txtTelemetry.TabIndex = 0;
            // 
            // groupBackflow
            // 
            this.groupBackflow.Controls.Add(this.txtBackflow);
            this.groupBackflow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBackflow.Font = new System.Drawing.Font("黑体", 11F);
            this.groupBackflow.Location = new System.Drawing.Point(3, 287);
            this.groupBackflow.Name = "groupBackflow";
            this.groupBackflow.Size = new System.Drawing.Size(956, 278);
            this.groupBackflow.TabIndex = 1;
            this.groupBackflow.TabStop = false;
            this.groupBackflow.Text = "Trace 回流";
            // 
            // txtBackflow
            // 
            this.txtBackflow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtBackflow.Font = new System.Drawing.Font("黑体", 10F);
            this.txtBackflow.Location = new System.Drawing.Point(3, 20);
            this.txtBackflow.Multiline = true;
            this.txtBackflow.Name = "txtBackflow";
            this.txtBackflow.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtBackflow.Size = new System.Drawing.Size(950, 255);
            this.txtBackflow.TabIndex = 0;
            // 
            // FrmAiAssistant
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 720);
            this.Controls.Add(this.layoutRoot);
            this.Name = "FrmAiAssistant";
            this.Text = "AI流程助手";
            this.layoutRoot.ResumeLayout(false);
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.footerPanel.ResumeLayout(false);
            this.footerPanel.PerformLayout();
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
            this.splitMain.ResumeLayout(false);
            this.navPanel.ResumeLayout(false);
            this.navGroup.ResumeLayout(false);
            this.quickGroup.ResumeLayout(false);
            this.tabMain.ResumeLayout(false);
            this.tabPropose.ResumeLayout(false);
            this.layoutPropose.ResumeLayout(false);
            this.groupRequest.ResumeLayout(false);
            this.groupRequest.PerformLayout();
            this.groupOutput.ResumeLayout(false);
            this.groupOutput.PerformLayout();
            this.groupConfig.ResumeLayout(false);
            this.layoutConfig.ResumeLayout(false);
            this.layoutConfig.PerformLayout();
            this.panelAiActions.ResumeLayout(false);
            this.panelAiActions.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numAiTimeout)).EndInit();
            this.groupContext.ResumeLayout(false);
            this.groupContext.PerformLayout();
            this.tabVerify.ResumeLayout(false);
            this.layoutVerify.ResumeLayout(false);
            this.groupRules.ResumeLayout(false);
            this.groupDiagnostics.ResumeLayout(false);
            this.groupDiagnostics.PerformLayout();
            this.tabSim.ResumeLayout(false);
            this.layoutSim.ResumeLayout(false);
            this.groupScenario.ResumeLayout(false);
            this.groupScenario.PerformLayout();
            this.groupTrace.ResumeLayout(false);
            this.groupTrace.PerformLayout();
            this.tabDiff.ResumeLayout(false);
            this.layoutDiff.ResumeLayout(false);
            this.groupDiff.ResumeLayout(false);
            this.groupDiff.PerformLayout();
            this.groupSummary.ResumeLayout(false);
            this.groupRevisions.ResumeLayout(false);
            this.tabTelemetry.ResumeLayout(false);
            this.layoutTelemetry.ResumeLayout(false);
            this.groupTelemetry.ResumeLayout(false);
            this.groupTelemetry.PerformLayout();
            this.groupBackflow.ResumeLayout(false);
            this.groupBackflow.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutRoot;
        private System.Windows.Forms.Panel headerPanel;
        private System.Windows.Forms.Label lblSubtitle;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblRevision;
        private System.Windows.Forms.Panel footerPanel;
        private System.Windows.Forms.Button btnExportTrace;
        private System.Windows.Forms.Button btnRollback;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.Button btnSimRun;
        private System.Windows.Forms.Button btnVerifyRun;
        private System.Windows.Forms.Button btnLoadDelta;
        private System.Windows.Forms.Button btnLoadCore;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.SplitContainer splitMain;
        private System.Windows.Forms.Panel navPanel;
        private System.Windows.Forms.GroupBox navGroup;
        private System.Windows.Forms.Button btnNavTelemetry;
        private System.Windows.Forms.Button btnNavDiff;
        private System.Windows.Forms.Button btnNavSim;
        private System.Windows.Forms.Button btnNavVerify;
        private System.Windows.Forms.Button btnNavPropose;
        private System.Windows.Forms.GroupBox quickGroup;
        private System.Windows.Forms.Label lblQuick;
        private System.Windows.Forms.TabControl tabMain;
        private System.Windows.Forms.TabPage tabPropose;
        private System.Windows.Forms.TableLayoutPanel layoutPropose;
        private System.Windows.Forms.GroupBox groupRequest;
        private System.Windows.Forms.TextBox txtRequest;
        private System.Windows.Forms.GroupBox groupOutput;
        private System.Windows.Forms.TextBox txtOutput;
        private System.Windows.Forms.GroupBox groupConfig;
        private System.Windows.Forms.TableLayoutPanel layoutConfig;
        private System.Windows.Forms.Label lblAiEndpoint;
        private System.Windows.Forms.TextBox txtAiEndpoint;
        private System.Windows.Forms.Label lblAiKey;
        private System.Windows.Forms.TextBox txtAiKey;
        private System.Windows.Forms.Label lblAiModel;
        private System.Windows.Forms.TextBox txtAiModel;
        private System.Windows.Forms.Label lblAiAuthHeader;
        private System.Windows.Forms.TextBox txtAiAuthHeader;
        private System.Windows.Forms.Label lblAiAuthPrefix;
        private System.Windows.Forms.TextBox txtAiAuthPrefix;
        private System.Windows.Forms.Label lblAiTimeout;
        private System.Windows.Forms.NumericUpDown numAiTimeout;
        private System.Windows.Forms.Label lblAiOutputKind;
        private System.Windows.Forms.ComboBox cmbAiOutputKind;
        private System.Windows.Forms.FlowLayoutPanel panelAiActions;
        private System.Windows.Forms.CheckBox chkAiShowKey;
        private System.Windows.Forms.Button btnAiLoadConfig;
        private System.Windows.Forms.Button btnAiSaveConfig;
        private System.Windows.Forms.Button btnAiGenerate;
        private System.Windows.Forms.GroupBox groupContext;
        private System.Windows.Forms.TextBox txtContext;
        private System.Windows.Forms.TabPage tabVerify;
        private System.Windows.Forms.TableLayoutPanel layoutVerify;
        private System.Windows.Forms.GroupBox groupRules;
        private System.Windows.Forms.ListBox listRules;
        private System.Windows.Forms.GroupBox groupDiagnostics;
        private System.Windows.Forms.TextBox txtDiagnostics;
        private System.Windows.Forms.TabPage tabSim;
        private System.Windows.Forms.TableLayoutPanel layoutSim;
        private System.Windows.Forms.GroupBox groupScenario;
        private System.Windows.Forms.TextBox txtScenario;
        private System.Windows.Forms.GroupBox groupTrace;
        private System.Windows.Forms.TextBox txtTrace;
        private System.Windows.Forms.TabPage tabDiff;
        private System.Windows.Forms.TableLayoutPanel layoutDiff;
        private System.Windows.Forms.GroupBox groupDiff;
        private System.Windows.Forms.TextBox txtDiff;
        private System.Windows.Forms.GroupBox groupSummary;
        private System.Windows.Forms.ListBox listSummary;
        private System.Windows.Forms.GroupBox groupRevisions;
        private System.Windows.Forms.ListBox listRevisions;
        private System.Windows.Forms.TabPage tabTelemetry;
        private System.Windows.Forms.TableLayoutPanel layoutTelemetry;
        private System.Windows.Forms.GroupBox groupTelemetry;
        private System.Windows.Forms.TextBox txtTelemetry;
        private System.Windows.Forms.GroupBox groupBackflow;
        private System.Windows.Forms.TextBox txtBackflow;
    }
}
