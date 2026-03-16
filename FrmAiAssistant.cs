using Automation.Bridge;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation
{
    public sealed class FrmAiAssistant : Form
    {
        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel();
        private readonly TableLayoutPanel configLayout = new TableLayoutPanel();
        private readonly Label lblTitle = new Label();
        private readonly Label lblBaseUrl = new Label();
        private readonly Label lblApiKey = new Label();
        private readonly Label lblConfigPathTitle = new Label();
        private readonly Label lblConfigPath = new Label();
        private readonly Label lblTip = new Label();
        private readonly Label lblStatus = new Label();
        private readonly Label lblPromptVariable = new Label();
        private readonly Label lblContextVariable = new Label();
        private readonly TextBox txtBaseUrl = new TextBox();
        private readonly TextBox txtApiKey = new TextBox();
        private readonly TextBox txtPromptVariable = new TextBox();
        private readonly TextBox txtContextVariable = new TextBox();
        private readonly Button btnSaveConfig = new Button();
        private readonly Button btnReloadConfig = new Button();
        private readonly Button btnClearHistory = new Button();
        private readonly RichTextBox rtbConversation = new RichTextBox();
        private readonly RichTextBox rtbWorkflowSchema = new RichTextBox();
        private readonly TextBox txtInputsJson = new TextBox();
        private readonly TextBox txtPrompt = new TextBox();
        private readonly TabControl tabRuntime = new TabControl();
        private readonly TabPage tabConversation = new TabPage("运行记录");
        private readonly TabPage tabDebug = new TabPage("联调面板");
        private readonly Label lblDebugMcpBaseUrl = new Label();
        private readonly Label lblDebugLogRoot = new Label();
        private readonly Label lblDebugIntentTip = new Label();
        private readonly TextBox txtDebugMcpBaseUrl = new TextBox();
        private readonly TextBox txtDebugLogRoot = new TextBox();
        private readonly TextBox txtDebugIntentJson = new TextBox();
        private readonly Button btnDebugReloadDefaults = new Button();
        private readonly Button btnDebugRefreshServer = new Button();
        private readonly Button btnDebugTailLog = new Button();
        private readonly Button btnDebugListProcs = new Button();
        private readonly Button btnDebugSelectedDetail = new Button();
        private readonly Button btnDebugSelectedSchema = new Button();
        private readonly Button btnDebugLoadTemplate = new Button();
        private readonly Button btnDebugBuildIntent = new Button();
        private readonly Button btnDebugPreviewIntent = new Button();
        private readonly Button btnDebugApplyIntent = new Button();
        private readonly RichTextBox rtbDebugBridge = new RichTextBox();
        private readonly RichTextBox rtbDebugMcp = new RichTextBox();
        private readonly RichTextBox rtbDebugLog = new RichTextBox();
        private readonly Button btnSend = new Button();
        private readonly CheckBox chkIncludeContext = new CheckBox();
        private readonly Label lblPromptTip = new Label();
        private readonly Font conversationHeaderFont = new Font("微软雅黑", 10.5F, FontStyle.Bold);
        private readonly Font conversationBodyFont = new Font("微软雅黑", 10.5F, FontStyle.Regular);
        private DifyWorkflowParameters workflowParameters;
        private bool sending;
        private bool loadingWorkflowSchema;

        public FrmAiAssistant()
        {
            InitializeLayout();
            Load += FrmAiAssistant_Load;
        }

        public void ApplyPermissions()
        {
            bool canUseAssistant = SF.HasPermission(PermissionKeys.ProcessAccess);
            bool canEditConfig = SF.HasPermission(PermissionKeys.AppConfigAccess);

            btnSend.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnClearHistory.Enabled = canUseAssistant;
            txtPrompt.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            txtInputsJson.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            txtPromptVariable.ReadOnly = !canUseAssistant || sending || loadingWorkflowSchema;
            txtContextVariable.ReadOnly = !canUseAssistant || sending || loadingWorkflowSchema;
            chkIncludeContext.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugReloadDefaults.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugRefreshServer.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugTailLog.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugListProcs.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugSelectedDetail.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugSelectedSchema.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugLoadTemplate.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugBuildIntent.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugPreviewIntent.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            btnDebugApplyIntent.Enabled = canUseAssistant && !sending && !loadingWorkflowSchema;
            txtDebugMcpBaseUrl.ReadOnly = !canUseAssistant || sending || loadingWorkflowSchema;
            txtDebugLogRoot.ReadOnly = !canUseAssistant || sending || loadingWorkflowSchema;
            txtDebugIntentJson.ReadOnly = !canUseAssistant || sending || loadingWorkflowSchema;
            btnSaveConfig.Enabled = canEditConfig;
            btnReloadConfig.Enabled = canUseAssistant && !sending;
            txtBaseUrl.ReadOnly = !canEditConfig;
            txtApiKey.ReadOnly = !canEditConfig;
        }

        public void RefreshAssistantView()
        {
            LoadConfig();
            LoadDebugDefaults(false);
            ApplyPermissions();
            ResetWorkflowSchemaState("尚未读取 workflow 参数。\r\n点击“刷新参数”后再从 Dify 拉取 user_input_form。");
        }

        private void FrmAiAssistant_Load(object sender, EventArgs e)
        {
            LoadConfig();
            LoadDebugDefaults(false);
            ApplyPermissions();
            ResetWorkflowSchemaState("尚未读取 workflow 参数。\r\n点击“刷新参数”后再从 Dify 拉取 user_input_form。");
        }

        private void InitializeLayout()
        {
            SuspendLayout();

            Text = "AI 助手";
            BackColor = Color.FromArgb(244, 247, 250);
            Font = new Font("微软雅黑", 10F);

            rootLayout.ColumnCount = 1;
            rootLayout.RowCount = 3;
            rootLayout.Dock = DockStyle.Fill;
            rootLayout.Padding = new Padding(18, 14, 18, 14);
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));

            lblTitle.Text = "流程 AI 工作流助手";
            lblTitle.Dock = DockStyle.Fill;
            lblTitle.Font = new Font("微软雅黑", 16F, FontStyle.Bold);
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblTitle.ForeColor = Color.FromArgb(20, 28, 45);

            configLayout.ColumnCount = 4;
            configLayout.RowCount = 4;
            configLayout.Dock = DockStyle.Fill;
            configLayout.BackColor = Color.White;
            configLayout.Padding = new Padding(0);
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            lblBaseUrl.Text = "Dify API地址";
            lblBaseUrl.TextAlign = ContentAlignment.MiddleLeft;
            lblBaseUrl.Dock = DockStyle.Fill;

            lblApiKey.Text = "Dify API Key";
            lblApiKey.TextAlign = ContentAlignment.MiddleLeft;
            lblApiKey.Dock = DockStyle.Fill;

            txtBaseUrl.Dock = DockStyle.Fill;
            txtBaseUrl.BorderStyle = BorderStyle.FixedSingle;
            txtBaseUrl.Margin = new Padding(0, 2, 8, 2);

            txtApiKey.Dock = DockStyle.Fill;
            txtApiKey.BorderStyle = BorderStyle.FixedSingle;
            txtApiKey.UseSystemPasswordChar = true;
            txtApiKey.Margin = new Padding(0, 2, 0, 2);

            btnSaveConfig.Text = "保存配置";
            btnSaveConfig.Dock = DockStyle.Fill;
            btnSaveConfig.Margin = new Padding(0, 2, 6, 2);
            btnSaveConfig.Click += BtnSaveConfig_Click;

            btnReloadConfig.Text = "刷新参数";
            btnReloadConfig.Dock = DockStyle.Fill;
            btnReloadConfig.Margin = new Padding(0, 2, 0, 2);
            btnReloadConfig.Click += BtnReloadConfig_Click;

            lblConfigPathTitle.Text = "配置文件";
            lblConfigPathTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblConfigPathTitle.Dock = DockStyle.Fill;

            lblConfigPath.AutoEllipsis = true;
            lblConfigPath.Dock = DockStyle.Fill;
            lblConfigPath.TextAlign = ContentAlignment.MiddleLeft;
            lblConfigPath.ForeColor = Color.FromArgb(80, 88, 104);

            lblTip.Text = "说明：BaseUrl 直接填写 Dify API 根地址，例如 http://127.0.0.1/v1。页面按 workflow 形态调用 /parameters 和 /workflows/run。";
            lblTip.Dock = DockStyle.Fill;
            lblTip.ForeColor = Color.DimGray;
            lblTip.TextAlign = ContentAlignment.MiddleLeft;
            lblTip.Padding = new Padding(0, 6, 0, 0);

            configLayout.Controls.Add(lblBaseUrl, 0, 0);
            configLayout.Controls.Add(txtBaseUrl, 1, 0);
            configLayout.Controls.Add(btnSaveConfig, 2, 0);
            configLayout.Controls.Add(btnReloadConfig, 3, 0);
            configLayout.Controls.Add(lblApiKey, 0, 1);
            configLayout.Controls.Add(txtApiKey, 1, 1);
            configLayout.SetColumnSpan(txtApiKey, 3);
            configLayout.Controls.Add(lblConfigPathTitle, 0, 2);
            configLayout.Controls.Add(lblConfigPath, 1, 2);
            configLayout.SetColumnSpan(lblConfigPath, 3);
            configLayout.Controls.Add(lblTip, 0, 3);
            configLayout.SetColumnSpan(lblTip, 4);

            rtbWorkflowSchema.Dock = DockStyle.Fill;
            rtbWorkflowSchema.ReadOnly = true;
            rtbWorkflowSchema.BorderStyle = BorderStyle.FixedSingle;
            rtbWorkflowSchema.BackColor = Color.White;
            rtbWorkflowSchema.Font = new Font("Consolas", 9.5F);
            rtbWorkflowSchema.Margin = new Padding(0);

            txtInputsJson.Dock = DockStyle.Fill;
            txtInputsJson.Multiline = true;
            txtInputsJson.BorderStyle = BorderStyle.FixedSingle;
            txtInputsJson.ScrollBars = ScrollBars.Vertical;
            txtInputsJson.Font = new Font("Consolas", 9.5F);
            txtInputsJson.AcceptsTab = true;
            txtInputsJson.Margin = new Padding(0);
            txtInputsJson.Text = "{}";

            rtbConversation.Dock = DockStyle.Fill;
            rtbConversation.ReadOnly = true;
            rtbConversation.BorderStyle = BorderStyle.FixedSingle;
            rtbConversation.BackColor = Color.White;
            rtbConversation.Font = new Font("微软雅黑", 10.5F);
            rtbConversation.HideSelection = false;
            rtbConversation.Margin = new Padding(0);

            tabRuntime.Dock = DockStyle.Fill;
            tabRuntime.Margin = new Padding(0);
            tabRuntime.Padding = new Point(18, 6);

            lblDebugMcpBaseUrl.Text = "MCP 地址";
            lblDebugMcpBaseUrl.Dock = DockStyle.Fill;
            lblDebugMcpBaseUrl.TextAlign = ContentAlignment.MiddleLeft;

            lblDebugLogRoot.Text = "日志目录";
            lblDebugLogRoot.Dock = DockStyle.Fill;
            lblDebugLogRoot.TextAlign = ContentAlignment.MiddleLeft;

            txtDebugMcpBaseUrl.Dock = DockStyle.Fill;
            txtDebugMcpBaseUrl.BorderStyle = BorderStyle.FixedSingle;
            txtDebugMcpBaseUrl.Margin = new Padding(0, 2, 8, 2);

            txtDebugLogRoot.Dock = DockStyle.Fill;
            txtDebugLogRoot.BorderStyle = BorderStyle.FixedSingle;
            txtDebugLogRoot.Margin = new Padding(0, 2, 8, 2);

            btnDebugReloadDefaults.Text = "读取默认值";
            btnDebugReloadDefaults.Dock = DockStyle.Fill;
            btnDebugReloadDefaults.Click += BtnDebugReloadDefaults_Click;

            btnDebugRefreshServer.Text = "刷新MCP状态";
            btnDebugRefreshServer.Dock = DockStyle.Fill;
            btnDebugRefreshServer.Click += BtnDebugRefreshServer_Click;

            btnDebugTailLog.Text = "刷新日志";
            btnDebugTailLog.Dock = DockStyle.Fill;
            btnDebugTailLog.Click += BtnDebugTailLog_Click;

            btnDebugListProcs.Text = "读取流程列表";
            btnDebugListProcs.Dock = DockStyle.Fill;
            btnDebugListProcs.Click += BtnDebugListProcs_Click;

            btnDebugSelectedDetail.Text = "读取选中详情";
            btnDebugSelectedDetail.Dock = DockStyle.Fill;
            btnDebugSelectedDetail.Click += BtnDebugSelectedDetail_Click;

            btnDebugSelectedSchema.Text = "读取选中Schema";
            btnDebugSelectedSchema.Dock = DockStyle.Fill;
            btnDebugSelectedSchema.Click += BtnDebugSelectedSchema_Click;

            btnDebugLoadTemplate.Text = "读取模板";
            btnDebugLoadTemplate.Dock = DockStyle.Fill;
            btnDebugLoadTemplate.Click += BtnDebugLoadTemplate_Click;

            btnDebugBuildIntent.Text = "生成测试意图";
            btnDebugBuildIntent.Dock = DockStyle.Fill;
            btnDebugBuildIntent.Click += BtnDebugBuildIntent_Click;

            btnDebugPreviewIntent.Text = "预演当前意图";
            btnDebugPreviewIntent.Dock = DockStyle.Fill;
            btnDebugPreviewIntent.Click += BtnDebugPreviewIntent_Click;

            btnDebugApplyIntent.Text = "提交当前意图";
            btnDebugApplyIntent.Dock = DockStyle.Fill;
            btnDebugApplyIntent.Click += BtnDebugApplyIntent_Click;

            lblDebugIntentTip.Text = "Bridge 自检动作直接通过本机 npipe 调用 Automation Bridge；右侧同时查看 McpServer /info、/healthz 和最新日志。";
            lblDebugIntentTip.Dock = DockStyle.Fill;
            lblDebugIntentTip.TextAlign = ContentAlignment.MiddleLeft;
            lblDebugIntentTip.ForeColor = Color.DimGray;

            txtDebugIntentJson.Dock = DockStyle.Fill;
            txtDebugIntentJson.Multiline = true;
            txtDebugIntentJson.BorderStyle = BorderStyle.FixedSingle;
            txtDebugIntentJson.ScrollBars = ScrollBars.Vertical;
            txtDebugIntentJson.AcceptsTab = true;
            txtDebugIntentJson.Font = new Font("Consolas", 9.5F);
            txtDebugIntentJson.Text = "{}";

            rtbDebugBridge.Dock = DockStyle.Fill;
            rtbDebugBridge.ReadOnly = true;
            rtbDebugBridge.BorderStyle = BorderStyle.FixedSingle;
            rtbDebugBridge.BackColor = Color.White;
            rtbDebugBridge.Font = new Font("Consolas", 9.3F);
            rtbDebugBridge.Margin = new Padding(0);

            rtbDebugMcp.Dock = DockStyle.Fill;
            rtbDebugMcp.ReadOnly = true;
            rtbDebugMcp.BorderStyle = BorderStyle.FixedSingle;
            rtbDebugMcp.BackColor = Color.White;
            rtbDebugMcp.Font = new Font("Consolas", 9.3F);
            rtbDebugMcp.Margin = new Padding(0);

            rtbDebugLog.Dock = DockStyle.Fill;
            rtbDebugLog.ReadOnly = true;
            rtbDebugLog.BorderStyle = BorderStyle.FixedSingle;
            rtbDebugLog.BackColor = Color.White;
            rtbDebugLog.Font = new Font("Consolas", 9.1F);
            rtbDebugLog.Margin = new Padding(0);

            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.ForeColor = Color.FromArgb(64, 64, 64);
            lblStatus.BackColor = Color.White;
            lblStatus.Padding = new Padding(12, 0, 12, 0);
            lblStatus.Text = "状态：等待输入";

            lblPromptVariable.Text = "主问题变量名";
            lblPromptVariable.Dock = DockStyle.Fill;
            lblPromptVariable.TextAlign = ContentAlignment.MiddleLeft;

            lblContextVariable.Text = "上下文变量名";
            lblContextVariable.Dock = DockStyle.Fill;
            lblContextVariable.TextAlign = ContentAlignment.MiddleLeft;

            txtPromptVariable.Dock = DockStyle.Fill;
            txtPromptVariable.BorderStyle = BorderStyle.FixedSingle;
            txtPromptVariable.Margin = new Padding(0, 1, 12, 1);

            txtContextVariable.Dock = DockStyle.Fill;
            txtContextVariable.BorderStyle = BorderStyle.FixedSingle;
            txtContextVariable.Margin = new Padding(0, 1, 12, 1);

            lblPromptTip.Text = "用户问题只写入“主问题变量名”；勾选上下文时会单独写入“上下文变量名”，不再拼接到 user_request。Ctrl+Enter 执行。";
            lblPromptTip.Dock = DockStyle.Fill;
            lblPromptTip.TextAlign = ContentAlignment.MiddleLeft;
            lblPromptTip.ForeColor = Color.DimGray;

            txtPrompt.Dock = DockStyle.Fill;
            txtPrompt.Multiline = true;
            txtPrompt.BorderStyle = BorderStyle.FixedSingle;
            txtPrompt.ScrollBars = ScrollBars.Vertical;
            txtPrompt.AcceptsTab = true;
            txtPrompt.KeyDown += TxtPrompt_KeyDown;

            btnClearHistory.Text = "清空记录";
            btnClearHistory.Dock = DockStyle.Fill;
            btnClearHistory.Click += BtnClearHistory_Click;

            btnSend.Text = "执行Workflow";
            btnSend.Dock = DockStyle.Fill;
            btnSend.Click += BtnSend_Click;

            chkIncludeContext.Text = "附带当前流程上下文";
            chkIncludeContext.Checked = true;
            chkIncludeContext.AutoSize = true;
            chkIncludeContext.Dock = DockStyle.Left;
            chkIncludeContext.TextAlign = ContentAlignment.MiddleLeft;

            TableLayoutPanel leftLayout = new TableLayoutPanel();
            leftLayout.ColumnCount = 1;
            leftLayout.RowCount = 3;
            leftLayout.Dock = DockStyle.Fill;
            leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 176F));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 238F));

            TableLayoutPanel rightLayout = new TableLayoutPanel();
            rightLayout.ColumnCount = 1;
            rightLayout.RowCount = 2;
            rightLayout.Dock = DockStyle.Fill;
            rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 240F));

            SplitContainer mainSplit = new SplitContainer();
            mainSplit.Dock = DockStyle.Fill;
            mainSplit.Orientation = Orientation.Vertical;
            mainSplit.FixedPanel = FixedPanel.Panel1;
            mainSplit.SplitterWidth = 8;
            mainSplit.BackColor = Color.FromArgb(226, 232, 240);
            mainSplit.HandleCreated += delegate
            {
                ApplyMainSplitLayout(mainSplit);
            };
            mainSplit.SizeChanged += delegate
            {
                ApplyMainSplitLayout(mainSplit);
            };

            TableLayoutPanel promptLayout = new TableLayoutPanel();
            promptLayout.ColumnCount = 1;
            promptLayout.RowCount = 3;
            promptLayout.Dock = DockStyle.Fill;
            promptLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            promptLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            promptLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            promptLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

            TableLayoutPanel promptHeaderLayout = new TableLayoutPanel();
            promptHeaderLayout.ColumnCount = 5;
            promptHeaderLayout.RowCount = 1;
            promptHeaderLayout.Dock = DockStyle.Fill;
            promptHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            promptHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            promptHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            promptHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            promptHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            promptHeaderLayout.Controls.Add(lblPromptVariable, 0, 0);
            promptHeaderLayout.Controls.Add(txtPromptVariable, 1, 0);
            promptHeaderLayout.Controls.Add(lblContextVariable, 2, 0);
            promptHeaderLayout.Controls.Add(txtContextVariable, 3, 0);
            promptHeaderLayout.Controls.Add(lblPromptTip, 4, 0);

            TableLayoutPanel promptActionLayout = new TableLayoutPanel();
            promptActionLayout.ColumnCount = 4;
            promptActionLayout.RowCount = 1;
            promptActionLayout.Dock = DockStyle.Fill;
            promptActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            promptActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            promptActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            promptActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            promptActionLayout.Controls.Add(chkIncludeContext, 1, 0);
            promptActionLayout.Controls.Add(btnClearHistory, 2, 0);
            promptActionLayout.Controls.Add(btnSend, 3, 0);

            promptLayout.Controls.Add(promptHeaderLayout, 0, 0);
            promptLayout.Controls.Add(txtPrompt, 0, 1);
            promptLayout.Controls.Add(promptActionLayout, 0, 2);

            TableLayoutPanel conversationLayout = new TableLayoutPanel();
            conversationLayout.ColumnCount = 1;
            conversationLayout.RowCount = 2;
            conversationLayout.Dock = DockStyle.Fill;
            conversationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            conversationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            conversationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label lblConversationTip = new Label();
            lblConversationTip.Text = "显示请求预览、workflow 返回结果与错误信息。";
            lblConversationTip.Dock = DockStyle.Fill;
            lblConversationTip.TextAlign = ContentAlignment.MiddleLeft;
            lblConversationTip.ForeColor = Color.DimGray;

            conversationLayout.Controls.Add(lblConversationTip, 0, 0);
            conversationLayout.Controls.Add(rtbConversation, 0, 1);

            tabConversation.Padding = new Padding(10);
            tabConversation.BackColor = Color.WhiteSmoke;
            tabConversation.Controls.Add(conversationLayout);
            tabDebug.Padding = new Padding(10);
            tabDebug.BackColor = Color.WhiteSmoke;
            tabDebug.Controls.Add(BuildDebugPanel());
            tabRuntime.TabPages.Add(tabConversation);
            tabRuntime.TabPages.Add(tabDebug);

            leftLayout.Controls.Add(BuildGroupBox("连接配置", configLayout), 0, 0);
            leftLayout.Controls.Add(BuildGroupBox("Workflow 输入结构", rtbWorkflowSchema), 0, 1);
            leftLayout.Controls.Add(BuildGroupBox("Inputs JSON", txtInputsJson), 0, 2);

            rightLayout.Controls.Add(BuildGroupBox("运行与联调", tabRuntime), 0, 0);
            rightLayout.Controls.Add(BuildGroupBox("执行输入", promptLayout), 0, 1);

            mainSplit.Panel1.Padding = new Padding(0, 0, 8, 0);
            mainSplit.Panel2.Padding = new Padding(8, 0, 0, 0);
            mainSplit.Panel1.Controls.Add(leftLayout);
            mainSplit.Panel2.Controls.Add(rightLayout);

            rootLayout.Controls.Add(BuildHeaderPanel(), 0, 0);
            rootLayout.Controls.Add(mainSplit, 0, 1);
            rootLayout.Controls.Add(lblStatus, 0, 2);

            Controls.Add(rootLayout);
            ResumeLayout(false);
        }

        private Control BuildDebugPanel()
        {
            TableLayoutPanel targetLayout = new TableLayoutPanel();
            targetLayout.ColumnCount = 5;
            targetLayout.RowCount = 3;
            targetLayout.Dock = DockStyle.Fill;
            targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118F));
            targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
            targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            targetLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            targetLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            targetLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            targetLayout.Controls.Add(lblDebugMcpBaseUrl, 0, 0);
            targetLayout.Controls.Add(txtDebugMcpBaseUrl, 1, 0);
            targetLayout.Controls.Add(btnDebugReloadDefaults, 2, 0);
            targetLayout.Controls.Add(btnDebugRefreshServer, 3, 0);
            targetLayout.Controls.Add(btnDebugTailLog, 4, 0);
            targetLayout.Controls.Add(lblDebugLogRoot, 0, 1);
            targetLayout.Controls.Add(txtDebugLogRoot, 1, 1);
            targetLayout.SetColumnSpan(txtDebugLogRoot, 4);
            targetLayout.Controls.Add(lblDebugIntentTip, 0, 2);
            targetLayout.SetColumnSpan(lblDebugIntentTip, 5);

            TableLayoutPanel actionLayout = new TableLayoutPanel();
            actionLayout.ColumnCount = 4;
            actionLayout.RowCount = 2;
            actionLayout.Dock = DockStyle.Fill;
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            actionLayout.Controls.Add(btnDebugListProcs, 0, 0);
            actionLayout.Controls.Add(btnDebugSelectedDetail, 1, 0);
            actionLayout.Controls.Add(btnDebugSelectedSchema, 2, 0);
            actionLayout.Controls.Add(btnDebugLoadTemplate, 3, 0);
            actionLayout.Controls.Add(btnDebugBuildIntent, 0, 1);
            actionLayout.Controls.Add(btnDebugPreviewIntent, 1, 1);
            actionLayout.Controls.Add(btnDebugApplyIntent, 2, 1);
            actionLayout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 3, 1);

            SplitContainer statusSplit = new SplitContainer();
            statusSplit.Dock = DockStyle.Fill;
            statusSplit.Orientation = Orientation.Vertical;
            statusSplit.SplitterWidth = 8;
            statusSplit.BackColor = Color.FromArgb(226, 232, 240);
            statusSplit.Panel1.Padding = new Padding(0, 0, 8, 0);
            statusSplit.Panel2.Padding = new Padding(8, 0, 0, 0);
            statusSplit.Panel1.Controls.Add(BuildGroupBox("Bridge 请求与响应", rtbDebugBridge));
            statusSplit.Panel2.Controls.Add(BuildGroupBox("MCP 状态", rtbDebugMcp));
            statusSplit.HandleCreated += delegate { ApplyDebugVerticalSplitLayout(statusSplit); };
            statusSplit.SizeChanged += delegate { ApplyDebugVerticalSplitLayout(statusSplit); };

            TableLayoutPanel topLayout = new TableLayoutPanel();
            topLayout.ColumnCount = 1;
            topLayout.RowCount = 4;
            topLayout.Dock = DockStyle.Fill;
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122F));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126F));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 192F));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            topLayout.Controls.Add(BuildGroupBox("联调目标", targetLayout), 0, 0);
            topLayout.Controls.Add(BuildGroupBox("联调动作", actionLayout), 0, 1);
            topLayout.Controls.Add(statusSplit, 0, 2);
            topLayout.Controls.Add(BuildGroupBox("测试意图 JSON", txtDebugIntentJson), 0, 3);

            SplitContainer outerSplit = new SplitContainer();
            outerSplit.Dock = DockStyle.Fill;
            outerSplit.Orientation = Orientation.Horizontal;
            outerSplit.SplitterWidth = 8;
            outerSplit.BackColor = Color.FromArgb(226, 232, 240);
            outerSplit.Panel1.Padding = new Padding(0, 0, 0, 8);
            outerSplit.Panel2.Padding = new Padding(0, 8, 0, 0);
            outerSplit.Panel1.Controls.Add(topLayout);
            outerSplit.Panel2.Controls.Add(BuildGroupBox("MCP 日志尾部", rtbDebugLog));
            outerSplit.HandleCreated += delegate { ApplyDebugHorizontalSplitLayout(outerSplit); };
            outerSplit.SizeChanged += delegate { ApplyDebugHorizontalSplitLayout(outerSplit); };

            return outerSplit;
        }

        private Control BuildHeaderPanel()
        {
            TableLayoutPanel header = new TableLayoutPanel();
            header.ColumnCount = 1;
            header.RowCount = 2;
            header.Dock = DockStyle.Fill;
            header.BackColor = Color.White;
            header.Padding = new Padding(16, 10, 16, 10);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "左侧维护 Dify 连接与 workflow 输入结构，右侧查看运行记录并提交执行请求。";
            lblSubtitle.Dock = DockStyle.Fill;
            lblSubtitle.TextAlign = ContentAlignment.MiddleLeft;
            lblSubtitle.ForeColor = Color.FromArgb(91, 102, 118);

            header.Controls.Add(lblTitle, 0, 0);
            header.Controls.Add(lblSubtitle, 0, 1);
            return header;
        }

        private static GroupBox BuildGroupBox(string title, Control content)
        {
            GroupBox groupBox = new GroupBox();
            groupBox.Text = title;
            groupBox.Dock = DockStyle.Fill;
            groupBox.BackColor = Color.White;
            groupBox.Padding = new Padding(12, 30, 12, 12);
            content.Dock = DockStyle.Fill;
            groupBox.Controls.Add(content);
            return groupBox;
        }

        private static void ApplyDebugVerticalSplitLayout(SplitContainer split)
        {
            int availableWidth = split.ClientSize.Width - split.SplitterWidth;
            if (availableWidth <= 0)
            {
                return;
            }

            const int desiredLeftMin = 260;
            const int desiredRightMin = 260;
            const int desiredLeftWidth = 0;
            const int fallbackMin = 120;

            int leftMin = desiredLeftMin;
            int rightMin = desiredRightMin;
            if (leftMin + rightMin > availableWidth)
            {
                int compactMin = Math.Max(fallbackMin, availableWidth / 3);
                leftMin = Math.Min(desiredLeftMin, compactMin);
                rightMin = Math.Min(desiredRightMin, Math.Max(fallbackMin, availableWidth - leftMin));
                if (leftMin + rightMin > availableWidth)
                {
                    leftMin = Math.Max(fallbackMin, availableWidth - rightMin);
                }
                if (leftMin + rightMin > availableWidth)
                {
                    rightMin = Math.Max(0, availableWidth - leftMin);
                }
            }

            leftMin = Math.Max(0, Math.Min(leftMin, availableWidth));
            rightMin = Math.Max(0, Math.Min(rightMin, Math.Max(0, availableWidth - leftMin)));

            int minDistance = leftMin;
            int maxDistance = Math.Max(minDistance, availableWidth - rightMin);
            int targetDistance = Math.Min(Math.Max(Math.Max(availableWidth / 2, desiredLeftWidth), minDistance), maxDistance);

            if (split.Panel1MinSize != leftMin)
            {
                split.Panel1MinSize = leftMin;
            }
            if (split.Panel2MinSize != rightMin)
            {
                split.Panel2MinSize = rightMin;
            }
            if (split.SplitterDistance != targetDistance)
            {
                split.SplitterDistance = targetDistance;
            }
        }

        private static void ApplyDebugHorizontalSplitLayout(SplitContainer split)
        {
            int availableHeight = split.ClientSize.Height - split.SplitterWidth;
            if (availableHeight <= 0)
            {
                return;
            }

            const int desiredTopMin = 420;
            const int desiredBottomMin = 160;
            const int fallbackMin = 96;

            int topMin = desiredTopMin;
            int bottomMin = desiredBottomMin;
            if (topMin + bottomMin > availableHeight)
            {
                int compactMin = Math.Max(fallbackMin, availableHeight / 4);
                bottomMin = Math.Min(desiredBottomMin, compactMin);
                topMin = Math.Min(desiredTopMin, Math.Max(fallbackMin, availableHeight - bottomMin));
                if (topMin + bottomMin > availableHeight)
                {
                    bottomMin = Math.Max(0, availableHeight - topMin);
                }
                if (topMin + bottomMin > availableHeight)
                {
                    topMin = Math.Max(0, availableHeight - bottomMin);
                }
            }

            topMin = Math.Max(0, Math.Min(topMin, availableHeight));
            bottomMin = Math.Max(0, Math.Min(bottomMin, Math.Max(0, availableHeight - topMin)));

            int minDistance = topMin;
            int maxDistance = Math.Max(minDistance, availableHeight - bottomMin);
            int targetDistance = Math.Min(Math.Max((availableHeight * 3) / 4, minDistance), maxDistance);

            if (split.Panel1MinSize != topMin)
            {
                split.Panel1MinSize = topMin;
            }
            if (split.Panel2MinSize != bottomMin)
            {
                split.Panel2MinSize = bottomMin;
            }
            if (split.SplitterDistance != targetDistance)
            {
                split.SplitterDistance = targetDistance;
            }
        }

        private static void ApplyMainSplitLayout(SplitContainer mainSplit)
        {
            int availableWidth = mainSplit.ClientSize.Width - mainSplit.SplitterWidth;
            if (availableWidth <= 0)
            {
                return;
            }

            const int desiredLeftWidth = 468;
            const int desiredLeftMin = 380;
            const int desiredRightMin = 480;
            const int fallbackMin = 180;

            int panel1Min = desiredLeftMin;
            int panel2Min = desiredRightMin;
            if (panel1Min + panel2Min > availableWidth)
            {
                int compactMin = Math.Max(fallbackMin, availableWidth / 3);
                panel1Min = Math.Min(desiredLeftMin, compactMin);
                panel2Min = Math.Min(desiredRightMin, Math.Max(fallbackMin, availableWidth - panel1Min));
                if (panel1Min + panel2Min > availableWidth)
                {
                    panel1Min = Math.Max(fallbackMin, availableWidth - panel2Min);
                }
                if (panel1Min + panel2Min > availableWidth)
                {
                    panel2Min = Math.Max(0, availableWidth - panel1Min);
                }
            }

            panel1Min = Math.Max(0, Math.Min(panel1Min, availableWidth));
            panel2Min = Math.Max(0, Math.Min(panel2Min, Math.Max(0, availableWidth - panel1Min)));

            int minDistance = panel1Min;
            int maxDistance = Math.Max(minDistance, availableWidth - panel2Min);
            int targetDistance = Math.Min(Math.Max(desiredLeftWidth, minDistance), maxDistance);

            if (mainSplit.Panel1MinSize != panel1Min)
            {
                mainSplit.Panel1MinSize = panel1Min;
            }
            if (mainSplit.Panel2MinSize != panel2Min)
            {
                mainSplit.Panel2MinSize = panel2Min;
            }
            if (mainSplit.SplitterDistance != targetDistance)
            {
                mainSplit.SplitterDistance = targetDistance;
            }
        }

        private void LoadDebugDefaults(bool forceOverwrite)
        {
            string resolvedBaseUrl = "http://127.0.0.1:8081";
            string resolvedLogRoot = string.Empty;
            string sourceSummary = "未找到 McpServer 运行配置，已使用默认 MCP 地址。";

            string appSettingsPath;
            if (TryFindMcpServerAppSettingsPath(out appSettingsPath))
            {
                try
                {
                    JObject root = JObject.Parse(File.ReadAllText(appSettingsPath, Encoding.UTF8));
                    JObject section = root["AutomationMcp"] as JObject ?? root["automationMcp"] as JObject;
                    if (section != null)
                    {
                        string listenUrl = section["ListenUrl"]?.Value<string>() ?? string.Empty;
                        string host = section["ListenHost"]?.Value<string>() ?? string.Empty;
                        int port = section["ListenPort"]?.Value<int?>() ?? 0;
                        string logRoot = section["LogRoot"]?.Value<string>() ?? string.Empty;

                        if (!string.IsNullOrWhiteSpace(listenUrl))
                        {
                            resolvedBaseUrl = listenUrl.Trim().TrimEnd('/');
                        }
                        else if (!string.IsNullOrWhiteSpace(host) && port > 0)
                        {
                            string normalizedHost = host.Trim();
                            if (string.Equals(normalizedHost, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(normalizedHost, "*", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(normalizedHost, "+", StringComparison.OrdinalIgnoreCase))
                            {
                                normalizedHost = "127.0.0.1";
                            }

                            resolvedBaseUrl = $"http://{normalizedHost}:{port}";
                        }

                        if (!string.IsNullOrWhiteSpace(logRoot))
                        {
                            resolvedLogRoot = Path.IsPathRooted(logRoot)
                                ? logRoot
                                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(appSettingsPath) ?? string.Empty, logRoot));
                        }

                        sourceSummary = "已从 " + appSettingsPath + " 读取 McpServer 运行配置。";
                    }
                    else
                    {
                        sourceSummary = appSettingsPath + " 中未找到 AutomationMcp 配置节，已使用默认 MCP 地址。";
                    }
                }
                catch (Exception ex)
                {
                    sourceSummary = "读取 McpServer 运行配置失败：" + ex.Message;
                }
            }

            if (forceOverwrite || string.IsNullOrWhiteSpace(txtDebugMcpBaseUrl.Text))
            {
                txtDebugMcpBaseUrl.Text = resolvedBaseUrl;
            }

            if (forceOverwrite || string.IsNullOrWhiteSpace(txtDebugLogRoot.Text))
            {
                txtDebugLogRoot.Text = resolvedLogRoot;
            }

            if (forceOverwrite || rtbDebugMcp.TextLength == 0)
            {
                var builder = new StringBuilder();
                builder.AppendLine("联调默认值");
                builder.AppendLine("MCP 地址: " + txtDebugMcpBaseUrl.Text.Trim());
                builder.AppendLine("日志目录: " + (txtDebugLogRoot.Text.Trim().Length == 0 ? "未解析到" : txtDebugLogRoot.Text.Trim()));
                builder.Append("来源: ").Append(sourceSummary);
                rtbDebugMcp.Text = builder.ToString();
            }
        }

        private static bool TryFindMcpServerAppSettingsPath(out string path)
        {
            path = null;
            var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] startDirs = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory
            };

            for (int i = 0; i < startDirs.Length; i++)
            {
                string startDir = startDirs[i];
                if (string.IsNullOrWhiteSpace(startDir) || !Directory.Exists(startDir))
                {
                    continue;
                }

                DirectoryInfo current = new DirectoryInfo(startDir);
                for (int depth = 0; current != null && depth < 8; depth++, current = current.Parent)
                {
                    string[] candidates = new[]
                    {
                        Path.Combine(current.FullName, "McpServer", "bin", "Debug", "net8.0-windows", "appsettings.json"),
                        Path.Combine(current.FullName, "McpServer", "bin", "Release", "net8.0-windows", "appsettings.json"),
                        Path.Combine(current.FullName, "McpServer", "bin", "Debug", "net8.0", "appsettings.json"),
                        Path.Combine(current.FullName, "McpServer", "bin", "Release", "net8.0", "appsettings.json"),
                        Path.Combine(current.FullName, "McpServer", "appsettings.json")
                    };

                    for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                    {
                        string candidate = candidates[candidateIndex];
                        if (!checkedPaths.Add(candidate))
                        {
                            continue;
                        }

                        if (File.Exists(candidate))
                        {
                            path = candidate;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void BtnDebugReloadDefaults_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "使用联调面板"))
            {
                return;
            }

            LoadDebugDefaults(true);
            SetStatus("状态：联调默认值已刷新");
        }

        private async void BtnDebugRefreshServer_Click(object sender, EventArgs e)
        {
            if (sending || loadingWorkflowSchema)
            {
                return;
            }
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "读取 MCP 状态"))
            {
                return;
            }
            if (!TryGetDebugMcpBaseUrl(out string baseUrl, out string error))
            {
                MessageBox.Show(error, "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("状态：MCP 地址无效");
                return;
            }

            SetSendingState(true);
            SetStatus("状态：正在读取 MCP /info 与 /healthz");
            try
            {
                string infoTrace = await GetHttpEndpointTraceAsync(baseUrl.TrimEnd('/') + "/info");
                string healthTrace = await GetHttpEndpointTraceAsync(baseUrl.TrimEnd('/') + "/healthz");
                AppendDebugMessage(rtbDebugMcp, "MCP 状态刷新", infoTrace + Environment.NewLine + Environment.NewLine + healthTrace);
                SetStatus("状态：MCP 状态已刷新");
            }
            catch (Exception ex)
            {
                AppendDebugMessage(rtbDebugMcp, "MCP 状态刷新失败", ex.Message);
                SetStatus("状态：MCP 状态读取失败");
            }
            finally
            {
                SetSendingState(false);
            }
        }

        private void BtnDebugTailLog_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "查看 MCP 日志"))
            {
                return;
            }

            string logRoot = txtDebugLogRoot.Text.Trim();
            if (logRoot.Length == 0)
            {
                MessageBox.Show("日志目录不能为空。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("状态：日志目录为空");
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(logRoot);
                if (!Directory.Exists(fullPath))
                {
                    throw new DirectoryNotFoundException("日志目录不存在：" + fullPath);
                }

                string[] logFiles = Directory.GetFiles(fullPath, "*.log", SearchOption.TopDirectoryOnly);
                if (logFiles.Length == 0)
                {
                    AppendDebugMessage(rtbDebugLog, "MCP 日志尾部", "当前日志目录下没有 .log 文件。\r\n目录：" + fullPath);
                    SetStatus("状态：未找到 MCP 日志");
                    return;
                }

                string latestFile = logFiles
                    .OrderByDescending(item => File.GetLastWriteTime(item))
                    .First();

                string[] lines = File.ReadAllLines(latestFile, Encoding.UTF8);
                int skip = Math.Max(0, lines.Length - 200);
                string tail = string.Join(Environment.NewLine, lines.Skip(skip).ToArray());
                if (tail.Length > 12000)
                {
                    tail = tail.Substring(tail.Length - 12000, 12000);
                }

                var builder = new StringBuilder();
                builder.AppendLine("文件: " + latestFile);
                builder.AppendLine("最后写入: " + File.GetLastWriteTime(latestFile).ToString("yyyy-MM-dd HH:mm:ss"));
                builder.AppendLine();
                builder.Append(tail);
                AppendDebugMessage(rtbDebugLog, "MCP 日志尾部", builder.ToString());
                SetStatus("状态：MCP 日志已刷新");
            }
            catch (Exception ex)
            {
                AppendDebugMessage(rtbDebugLog, "MCP 日志读取失败", ex.Message);
                SetStatus("状态：MCP 日志读取失败");
            }
        }

        private async void BtnDebugListProcs_Click(object sender, EventArgs e)
        {
            if (sending || loadingWorkflowSchema)
            {
                return;
            }
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "读取流程列表"))
            {
                return;
            }

            SetSendingState(true);
            SetStatus("状态：正在通过 Bridge 读取流程列表");
            try
            {
                PipeResponseMessage response = await SendBridgePostAndRenderAsync(
                    "读取流程列表",
                    "/bridge/procs/list",
                    new JObject
                    {
                        ["includeStepSummary"] = false
                    });
                SetStatus(response.StatusCode == 200 ? "状态：流程列表读取完成" : "状态：流程列表读取失败");
            }
            catch (Exception ex)
            {
                AppendDebugMessage(rtbDebugBridge, "读取流程列表失败", ex.Message);
                SetStatus("状态：流程列表读取失败");
            }
            finally
            {
                SetSendingState(false);
            }
        }

        private async void BtnDebugSelectedDetail_Click(object sender, EventArgs e)
        {
            if (sending || loadingWorkflowSchema)
            {
                return;
            }
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "读取流程详情"))
            {
                return;
            }
            if (!TryGetCurrentDebugSelection(false, false, out int procIndex, out int stepIndex, out int opIndex, out Proc proc, out Step step, out OperationType op, out string error))
            {
                MessageBox.Show(error, "缺少选中项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("状态：未选中流程");
                return;
            }

            SetSendingState(true);
            SetStatus("状态：正在通过 Bridge 读取选中流程详情");
            try
            {
                PipeResponseMessage response = await SendBridgePostAndRenderAsync(
                    "读取选中流程详情",
                    "/bridge/procs/detail",
                    new JObject
                    {
                        ["procIndex"] = procIndex
                    });
                SetStatus(response.StatusCode == 200 ? "状态：流程详情读取完成" : "状态：流程详情读取失败");
            }
            catch (Exception ex)
            {
                AppendDebugMessage(rtbDebugBridge, "读取选中流程详情失败", ex.Message);
                SetStatus("状态：流程详情读取失败");
            }
            finally
            {
                SetSendingState(false);
            }
        }

        private async void BtnDebugSelectedSchema_Click(object sender, EventArgs e)
        {
            if (sending || loadingWorkflowSchema)
            {
                return;
            }
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "读取指令 Schema"))
            {
                return;
            }
            if (!TryGetCurrentDebugSelection(true, true, out int procIndex, out int stepIndex, out int opIndex, out Proc proc, out Step step, out OperationType op, out string error))
            {
                MessageBox.Show(error, "缺少选中项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("状态：未选中指令");
                return;
            }

            SetSendingState(true);
            SetStatus("状态：正在通过 Bridge 读取选中指令 Schema");
            try
            {
                PipeResponseMessage response = await SendBridgePostAndRenderAsync(
                    "读取选中指令 Schema",
                    "/bridge/operations/schema",
                    new JObject
                    {
                        ["procIndex"] = procIndex,
                        ["stepId"] = step.Id.ToString("D"),
                        ["opId"] = op.Id.ToString("D")
                    });
                SetStatus(response.StatusCode == 200 ? "状态：指令 Schema 读取完成" : "状态：指令 Schema 读取失败");
            }
            catch (Exception ex)
            {
                AppendDebugMessage(rtbDebugBridge, "读取选中指令 Schema 失败", ex.Message);
                SetStatus("状态：指令 Schema 读取失败");
            }
            finally
            {
                SetSendingState(false);
            }
        }

        private async void BtnDebugLoadTemplate_Click(object sender, EventArgs e)
        {
            if (sending || loadingWorkflowSchema)
            {
                return;
            }
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "读取中间意图模板"))
            {
                return;
            }

            string templateId = GetSuggestedTemplateId();
            SetSendingState(true);
            SetStatus("状态：正在通过 Bridge 读取中间意图模板");
            try
            {
                PipeResponseMessage response = await SendBridgePostAndRenderAsync(
                    "读取中间意图模板",
                    "/bridge/intents/template",
                    new JObject
                    {
                        ["templateId"] = templateId
                    });

                if (response.StatusCode == 200)
                {
                    JObject payload = JObject.Parse(response.BodyJson);
                    JToken exampleIntent = payload["data"]?["template"]?["exampleIntent"] ?? payload["data"]?["template"]?["intentShape"];
                    if (exampleIntent != null)
                    {
                        txtDebugIntentJson.Text = exampleIntent.ToString(Formatting.Indented);
                    }
                }

                SetStatus(response.StatusCode == 200
                    ? "状态：中间意图模板已加载"
                    : "状态：中间意图模板读取失败");
            }
            catch (Exception ex)
            {
                AppendDebugMessage(rtbDebugBridge, "读取中间意图模板失败", ex.Message);
                SetStatus("状态：中间意图模板读取失败");
            }
            finally
            {
                SetSendingState(false);
            }
        }

        private void BtnDebugBuildIntent_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "生成测试意图"))
            {
                return;
            }
            if (!TryGetCurrentDebugSelection(true, true, out int procIndex, out int stepIndex, out int opIndex, out Proc proc, out Step step, out OperationType op, out string error))
            {
                MessageBox.Show(error, "缺少选中项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("状态：未选中指令");
                return;
            }

            JObject intent = new JObject
            {
                ["intentType"] = "update_operation_field",
                ["procIndex"] = procIndex,
                ["baseProcId"] = proc.head?.Id.ToString("D") ?? string.Empty,
                ["stepId"] = step.Id.ToString("D"),
                ["opId"] = op.Id.ToString("D"),
                ["expectedOperaType"] = op.OperaType ?? string.Empty,
                ["fieldChanges"] = new JObject
                {
                    ["Note"] = "联调面板 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }
            };

            txtDebugIntentJson.Text = intent.ToString(Formatting.Indented);
            AppendDebugMessage(rtbDebugBridge, "已生成测试意图", txtDebugIntentJson.Text);
            SetStatus("状态：已生成基于当前选中指令的测试意图");
        }

        private async void BtnDebugPreviewIntent_Click(object sender, EventArgs e)
        {
            if (sending || loadingWorkflowSchema)
            {
                return;
            }
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "预演测试意图"))
            {
                return;
            }
            if (!TryParseDebugIntentJson(out JObject intent, out string error))
            {
                MessageBox.Show(error, "意图 JSON 错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("状态：测试意图 JSON 非法");
                return;
            }

            SetSendingState(true);
            SetStatus("状态：正在通过 Bridge 预演当前意图");
            try
            {
                PipeResponseMessage response = await SendBridgePostAndRenderAsync(
                    "预演当前意图",
                    "/bridge/intents/preview",
                    new JObject
                    {
                        ["intentJson"] = intent.ToString(Formatting.None)
                    });
                SetStatus(response.StatusCode == 200 ? "状态：意图预演完成" : "状态：意图预演失败");
            }
            catch (Exception ex)
            {
                AppendDebugMessage(rtbDebugBridge, "预演当前意图失败", ex.Message);
                SetStatus("状态：意图预演失败");
            }
            finally
            {
                SetSendingState(false);
            }
        }

        private async void BtnDebugApplyIntent_Click(object sender, EventArgs e)
        {
            if (sending || loadingWorkflowSchema)
            {
                return;
            }
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "提交测试意图"))
            {
                return;
            }
            if (!TryParseDebugIntentJson(out JObject intent, out string error))
            {
                MessageBox.Show(error, "意图 JSON 错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("状态：测试意图 JSON 非法");
                return;
            }

            DialogResult dialogResult = MessageBox.Show(
                "提交当前意图会真实修改流程、保存并发布。是否继续？",
                "确认提交",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (dialogResult != DialogResult.OK)
            {
                SetStatus("状态：已取消提交当前意图");
                return;
            }

            SetSendingState(true);
            SetStatus("状态：正在通过 Bridge 提交当前意图");
            try
            {
                PipeResponseMessage response = await SendBridgePostAndRenderAsync(
                    "提交当前意图",
                    "/bridge/intents/apply",
                    new JObject
                    {
                        ["intentJson"] = intent.ToString(Formatting.None)
                    });
                SetStatus(response.StatusCode == 200 ? "状态：意图已提交" : "状态：意图提交失败");
            }
            catch (Exception ex)
            {
                AppendDebugMessage(rtbDebugBridge, "提交当前意图失败", ex.Message);
                SetStatus("状态：意图提交失败");
            }
            finally
            {
                SetSendingState(false);
            }
        }

        private bool TryGetDebugMcpBaseUrl(out string baseUrl, out string error)
        {
            baseUrl = txtDebugMcpBaseUrl.Text.Trim().TrimEnd('/');
            error = null;

            if (baseUrl.Length == 0)
            {
                error = "MCP 地址不能为空。";
                return false;
            }

            Uri uri;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                error = "MCP 地址必须是合法的 http/https 绝对地址。";
                return false;
            }

            baseUrl = uri.AbsoluteUri.TrimEnd('/');
            return true;
        }

        private async Task<string> GetHttpEndpointTraceAsync(string url)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    string body = await reader.ReadToEndAsync();
                    var builder = new StringBuilder();
                    builder.AppendLine("GET " + url);
                    builder.AppendLine("status: " + (int)response.StatusCode + " " + response.StatusDescription);
                    builder.AppendLine("elapsedMs: " + stopwatch.ElapsedMilliseconds);
                    builder.AppendLine("body:");
                    builder.Append(PrettyJson(body));
                    return builder.ToString();
                }
            }
            catch (WebException ex)
            {
                string body = string.Empty;
                string statusText = "status: 未返回 HTTP 响应";
                if (ex.Response is HttpWebResponse errorResponse)
                {
                    statusText = "status: " + (int)errorResponse.StatusCode + " " + errorResponse.StatusDescription;
                    using (errorResponse)
                    using (Stream responseStream = errorResponse.GetResponseStream())
                    using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                    {
                        body = await reader.ReadToEndAsync();
                    }
                }

                var builder = new StringBuilder();
                builder.AppendLine("GET " + url);
                builder.AppendLine(statusText);
                builder.AppendLine("elapsedMs: " + stopwatch.ElapsedMilliseconds);
                builder.AppendLine("error: " + ex.Message);
                if (body.Length > 0)
                {
                    builder.AppendLine("body:");
                    builder.Append(PrettyJson(body));
                }
                return builder.ToString();
            }
        }

        private async Task<PipeResponseMessage> SendBridgePostAndRenderAsync(string title, string path, JObject requestBody)
        {
            string requestJson = requestBody == null ? "{}" : requestBody.ToString(Formatting.None);
            PipeResponseMessage response = await SendBridgeRequestAsync("POST", path, requestJson);

            var builder = new StringBuilder();
            builder.AppendLine("path: " + path);
            builder.AppendLine("request:");
            builder.AppendLine(PrettyJson(requestJson));
            builder.AppendLine();
            builder.AppendLine("statusCode: " + response.StatusCode);
            builder.AppendLine("response:");
            builder.Append(PrettyJson(response.BodyJson));
            AppendDebugMessage(rtbDebugBridge, title, builder.ToString());

            return response;
        }

        private async Task<PipeResponseMessage> SendBridgeRequestAsync(string method, string path, string bodyJson)
        {
            string requestId = Guid.NewGuid().ToString("N");
            using (var pipe = new NamedPipeClientStream(".", AutomationBridgeHost.DefaultPipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await Task.Run(() => pipe.Connect(8000));
                var request = new PipeRequestMessage
                {
                    RequestId = requestId,
                    Method = method ?? "POST",
                    Path = path ?? string.Empty,
                    BodyJson = bodyJson ?? "{}"
                };

                await WritePipeMessageAsync(pipe, JsonConvert.SerializeObject(request));
                string responseJson = await ReadPipeMessageAsync(pipe);
                if (string.IsNullOrWhiteSpace(responseJson))
                {
                    throw new InvalidOperationException("Bridge 返回空响应。");
                }

                PipeResponseMessage response = JsonConvert.DeserializeObject<PipeResponseMessage>(responseJson);
                if (response == null)
                {
                    throw new InvalidOperationException("Bridge 响应反序列化失败。");
                }

                if (!string.IsNullOrWhiteSpace(response.RequestId)
                    && !string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Bridge 响应 requestId 不匹配。");
                }

                return response;
            }
        }

        private static async Task WritePipeMessageAsync(Stream stream, string text)
        {
            byte[] payloadBuffer = Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] lengthBuffer = BitConverter.GetBytes(payloadBuffer.Length);
            await stream.WriteAsync(lengthBuffer, 0, lengthBuffer.Length);
            if (payloadBuffer.Length > 0)
            {
                await stream.WriteAsync(payloadBuffer, 0, payloadBuffer.Length);
            }
            await stream.FlushAsync();
        }

        private static async Task<string> ReadPipeMessageAsync(Stream stream)
        {
            byte[] lengthBuffer = await ReadPipeExactlyAsync(stream, sizeof(int));
            int length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length < 0)
            {
                throw new InvalidDataException("Pipe 响应长度非法。");
            }
            if (length == 0)
            {
                return string.Empty;
            }

            byte[] payloadBuffer = await ReadPipeExactlyAsync(stream, length);
            return Encoding.UTF8.GetString(payloadBuffer);
        }

        private static async Task<byte[]> ReadPipeExactlyAsync(Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Pipe 连接在读取响应时提前关闭。");
                }
                offset += read;
            }
            return buffer;
        }

        private bool TryGetCurrentDebugSelection(bool requireStep, bool requireOperation, out int procIndex, out int stepIndex, out int opIndex, out Proc proc, out Step step, out OperationType op, out string error)
        {
            procIndex = SF.frmProc?.SelectedProcNum ?? -1;
            stepIndex = SF.frmProc?.SelectedStepNum ?? -1;
            opIndex = SF.frmDataGrid?.iSelectedRow ?? -1;
            proc = null;
            step = null;
            op = null;
            error = null;

            if (SF.frmProc?.procsList == null || procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                error = "请先切回流程页选中目标流程，再回到 AI 助手执行联调。";
                return false;
            }

            proc = SF.frmProc.procsList[procIndex];
            if (!requireStep && stepIndex < 0)
            {
                return true;
            }

            if (stepIndex < 0 || proc.steps == null || stepIndex >= proc.steps.Count)
            {
                error = "请先切回流程页选中目标步骤，再回到 AI 助手执行联调。";
                return false;
            }

            step = proc.steps[stepIndex];
            if (!requireOperation && opIndex < 0)
            {
                return true;
            }

            if (opIndex < 0 || step.Ops == null || opIndex >= step.Ops.Count)
            {
                error = "请先切回流程页选中目标指令，再回到 AI 助手执行联调。";
                return false;
            }

            op = step.Ops[opIndex];
            return true;
        }

        private string GetSuggestedTemplateId()
        {
            int procIndex;
            int stepIndex;
            int opIndex;
            Proc proc;
            Step step;
            OperationType op;
            string error;

            if (TryGetCurrentDebugSelection(true, true, out procIndex, out stepIndex, out opIndex, out proc, out step, out op, out error))
            {
                return "update_operation_field";
            }

            if (TryGetCurrentDebugSelection(true, false, out procIndex, out stepIndex, out opIndex, out proc, out step, out op, out error))
            {
                return "update_step_field";
            }

            if (TryGetCurrentDebugSelection(false, false, out procIndex, out stepIndex, out opIndex, out proc, out step, out op, out error))
            {
                return "update_proc_head_field";
            }

            return "update_operation_field";
        }

        private bool TryParseDebugIntentJson(out JObject intent, out string error)
        {
            intent = null;
            error = null;

            string text = txtDebugIntentJson.Text.Trim();
            if (text.Length == 0)
            {
                error = "测试意图 JSON 不能为空。";
                return false;
            }

            try
            {
                JToken token = JToken.Parse(text);
                if (token.Type != JTokenType.Object)
                {
                    error = "测试意图 JSON 必须是对象。";
                    return false;
                }

                intent = (JObject)token;
                return true;
            }
            catch (Exception ex)
            {
                error = "测试意图 JSON 解析失败：" + ex.Message;
                return false;
            }
        }

        private void AppendDebugMessage(RichTextBox box, string title, string content)
        {
            if (box.TextLength > 0)
            {
                box.AppendText(Environment.NewLine + Environment.NewLine);
            }

            box.SelectionColor = Color.FromArgb(36, 74, 122);
            box.SelectionFont = box.Font;
            box.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}");
            box.SelectionColor = Color.Black;
            box.AppendText(Environment.NewLine);
            box.AppendText(content ?? string.Empty);
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
        }

        private static string PrettyJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            try
            {
                return JToken.Parse(trimmed).ToString(Formatting.Indented);
            }
            catch
            {
                return trimmed;
            }
        }

        private void ResetWorkflowSchemaState(string message)
        {
            workflowParameters = null;
            rtbWorkflowSchema.Text = message ?? string.Empty;
            if (txtPromptVariable.TextLength == 0)
            {
                txtPromptVariable.Text = string.Empty;
            }
            SetStatus("状态：已加载本地配置，等待手动刷新 workflow 参数");
        }

        private void LoadConfig()
        {
            lblConfigPath.Text = DifyConfigStorage.ConfigPath;
            if (!DifyConfigStorage.TryLoad(out DifyConfig config, out string error))
            {
                MessageBox.Show(error, "Dify 配置读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                txtBaseUrl.Text = string.Empty;
                txtApiKey.Text = string.Empty;
                SetStatus("状态：Dify 配置读取失败");
                return;
            }

            txtBaseUrl.Text = config.BaseUrl ?? string.Empty;
            txtApiKey.Text = config.ApiKey ?? string.Empty;
            SetStatus("状态：Dify 配置已加载，等待读取 workflow 参数");
        }

        private async void BtnSaveConfig_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.AppConfigAccess, "保存 Dify 配置"))
            {
                return;
            }

            DifyConfig config = new DifyConfig
            {
                BaseUrl = txtBaseUrl.Text.Trim(),
                ApiKey = txtApiKey.Text.Trim()
            };
            if (!DifyConfigStorage.TrySave(config, out string error))
            {
                MessageBox.Show(error, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("状态：Dify 配置保存失败");
                return;
            }

            SetStatus("状态：Dify 配置已保存，正在刷新 workflow 参数");
            MessageBox.Show("Dify 配置保存成功。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshWorkflowSchemaAsync();
        }

        private async void BtnReloadConfig_Click(object sender, EventArgs e)
        {
            LoadConfig();
            ApplyPermissions();
            await RefreshWorkflowSchemaAsync();
        }

        private void BtnClearHistory_Click(object sender, EventArgs e)
        {
            rtbConversation.Clear();
            SetStatus("状态：已清空当前运行记录");
            txtPrompt.Focus();
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            await SendMessageAsync();
        }

        private async Task SendMessageAsync()
        {
            if (sending || loadingWorkflowSchema)
            {
                return;
            }
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "使用 AI 助手"))
            {
                return;
            }

            if (!TryBuildConfig(out DifyConfig config, out string configError))
            {
                MessageBox.Show(configError, "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("状态：Dify 配置不完整");
                return;
            }

            string prompt = txtPrompt.Text.Trim();
            bool includeContext = chkIncludeContext.Checked;
            if (includeContext)
            {
                SetStatus("状态：正在收集本地上下文");
            }
            else
            {
                SetStatus("状态：当前不附带本地上下文，正在准备 workflow 输入");
            }

            JObject localContext = includeContext ? BuildLocalContext() : null;
            if (!TryBuildWorkflowInputs(prompt, localContext, out JObject inputs, out string buildError))
            {
                MessageBox.Show(buildError, "输入参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("状态：workflow 输入构建失败");
                return;
            }

            AppendMessage("用户", BuildRequestPreview(prompt, localContext, inputs, includeContext, txtContextVariable.Text.Trim()));
            txtPrompt.Clear();
            SetSendingState(true);

            try
            {
                string userId = GetUserId();
                DifyWorkflowRunReply reply = await Task.Run(() => DifyWorkflowClient.RunWorkflow(config, inputs, userId, ReportWorkflowTrace));
                AppendMessage("Workflow", BuildWorkflowResultText(reply));
                ReportWorkflowTrace("Dify workflow 结果已返回。", false);
                SetStatus(string.IsNullOrWhiteSpace(reply.WorkflowRunId)
                    ? "状态：workflow 已执行完成"
                    : $"状态：workflow 已执行完成 {reply.WorkflowRunId}");
            }
            catch (Exception ex)
            {
                AppendMessage("系统", $"请求失败：{ex.Message}");
                ReportWorkflowTrace("workflow 请求失败：" + ex.Message, true);
                SetStatus("状态：请求失败");
            }
            finally
            {
                SetSendingState(false);
            }
        }

        private void TxtPrompt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                _ = SendMessageAsync();
            }
        }

        private async Task RefreshWorkflowSchemaAsync()
        {
            if (sending)
            {
                return;
            }
            if (!TryBuildConfig(out DifyConfig config, out string configError))
            {
                workflowParameters = null;
                rtbWorkflowSchema.Text = "未读取到 workflow 参数。\r\n原因：" + configError;
                txtPromptVariable.Text = string.Empty;
                ApplyPermissions();
                return;
            }

            loadingWorkflowSchema = true;
            ApplyPermissions();
            SetStatus("状态：正在读取 workflow 参数");

            try
            {
                workflowParameters = await Task.Run(() => DifyWorkflowClient.GetParameters(config));
                rtbWorkflowSchema.Text = BuildWorkflowSchemaText(workflowParameters);

                if (string.IsNullOrWhiteSpace(txtPromptVariable.Text))
                {
                    string autoVariable = TryDetectSinglePromptVariable(workflowParameters);
                    if (!string.IsNullOrWhiteSpace(autoVariable))
                    {
                        txtPromptVariable.Text = autoVariable;
                    }
                }

                if (string.IsNullOrWhiteSpace(txtContextVariable.Text))
                {
                    string autoContextVariable = TryDetectContextVariable(workflowParameters);
                    if (!string.IsNullOrWhiteSpace(autoContextVariable))
                    {
                        txtContextVariable.Text = autoContextVariable;
                    }
                }

                SetStatus("状态：workflow 参数已刷新");
            }
            catch (Exception ex)
            {
                workflowParameters = null;
                rtbWorkflowSchema.Text = "读取 workflow 参数失败。\r\n" + ex.Message;
                SetStatus("状态：workflow 参数读取失败");
            }
            finally
            {
                loadingWorkflowSchema = false;
                ApplyPermissions();
            }
        }

        private bool TryBuildConfig(out DifyConfig config, out string error)
        {
            config = null;
            error = null;

            string baseUrl = txtBaseUrl.Text.Trim();
            string apiKey = txtApiKey.Text.Trim();
            if (baseUrl.Length == 0)
            {
                error = "Dify API 地址不能为空。";
                return false;
            }
            if (apiKey.Length == 0)
            {
                error = "Dify API Key 不能为空。";
                return false;
            }

            config = new DifyConfig
            {
                BaseUrl = baseUrl,
                ApiKey = apiKey
            };
            return true;
        }

        private bool TryBuildWorkflowInputs(string prompt, JObject localContext, out JObject inputs, out string error)
        {
            inputs = null;
            error = null;

            if (!TryParseInputsJson(out JObject jsonInputs, out error))
            {
                return false;
            }

            string trimmedPrompt = prompt.Trim();
            string promptVariable = txtPromptVariable.Text.Trim();
            if (trimmedPrompt.Length > 0)
            {
                if (promptVariable.Length == 0)
                {
                    promptVariable = TryDetectSinglePromptVariable(workflowParameters);
                    if (promptVariable.Length == 0)
                    {
                        error = "当前 workflow 存在多个或零个可用输入变量，请先填写“主问题变量名”或在 Inputs JSON 中手工提供 inputs。";
                        return false;
                    }
                    txtPromptVariable.Text = promptVariable;
                }

                jsonInputs[promptVariable] = trimmedPrompt;
            }

            if (localContext != null)
            {
                string contextVariable = txtContextVariable.Text.Trim();
                if (contextVariable.Length == 0)
                {
                    contextVariable = TryDetectContextVariable(workflowParameters);
                    if (contextVariable.Length > 0)
                    {
                        txtContextVariable.Text = contextVariable;
                    }
                }

                if (contextVariable.Length == 0)
                {
                    error = "已勾选附带上下文，但当前未配置“上下文变量名”。请在 workflow 中新增如 automation_context 的输入变量，或取消勾选。";
                    return false;
                }

                jsonInputs[contextVariable] = BuildContextInputValue(localContext, FindFieldDefinition(workflowParameters, contextVariable));
            }

            if (!jsonInputs.HasValues)
            {
                error = "Workflow inputs 不能为空。请填写问题内容或 Inputs JSON。";
                return false;
            }

            inputs = jsonInputs;
            return true;
        }

        private bool TryParseInputsJson(out JObject inputs, out string error)
        {
            inputs = null;
            error = null;

            string text = txtInputsJson.Text.Trim();
            if (text.Length == 0)
            {
                inputs = new JObject();
                return true;
            }

            try
            {
                JToken token = JToken.Parse(text);
                if (token.Type != JTokenType.Object)
                {
                    error = "Inputs JSON 必须是对象。";
                    return false;
                }

                inputs = (JObject)token.DeepClone();
                return true;
            }
            catch (Exception ex)
            {
                error = $"Inputs JSON 解析失败:{ex.Message}";
                return false;
            }
        }

        private static string TryDetectSinglePromptVariable(DifyWorkflowParameters parameters)
        {
            if (parameters?.Fields == null || parameters.Fields.Count == 0)
            {
                return string.Empty;
            }

            string[] preferredNames = new[]
            {
                "user_request",
                "query",
                "prompt",
                "question",
                "message"
            };

            for (int i = 0; i < preferredNames.Length; i++)
            {
                string candidate = preferredNames[i];
                DifyWorkflowFieldDefinition matched = parameters.Fields.Find(field =>
                    field != null
                    && !string.IsNullOrWhiteSpace(field.Variable)
                    && string.Equals(field.Variable, candidate, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    return matched.Variable;
                }
            }

            string detectedVariable = string.Empty;
            int count = 0;
            for (int i = 0; i < parameters.Fields.Count; i++)
            {
                DifyWorkflowFieldDefinition field = parameters.Fields[i];
                if (field == null || string.IsNullOrWhiteSpace(field.Variable))
                {
                    continue;
                }
                count++;
                detectedVariable = field.Variable;
            }

            return count == 1 ? detectedVariable : string.Empty;
        }

        private static string TryDetectContextVariable(DifyWorkflowParameters parameters)
        {
            if (parameters?.Fields == null || parameters.Fields.Count == 0)
            {
                return string.Empty;
            }

            string[] preferredNames = new[]
            {
                "automation_context",
                "local_context",
                "context",
                "workflow_context"
            };

            for (int i = 0; i < preferredNames.Length; i++)
            {
                string candidate = preferredNames[i];
                DifyWorkflowFieldDefinition matched = parameters.Fields.Find(field =>
                    field != null
                    && !string.IsNullOrWhiteSpace(field.Variable)
                    && string.Equals(field.Variable, candidate, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    return matched.Variable;
                }
            }

            DifyWorkflowFieldDefinition labelMatched = parameters.Fields.Find(field =>
                field != null
                && !string.IsNullOrWhiteSpace(field.Variable)
                && !string.IsNullOrWhiteSpace(field.Label)
                && field.Label.IndexOf("上下文", StringComparison.OrdinalIgnoreCase) >= 0);
            return labelMatched?.Variable ?? string.Empty;
        }

        private static DifyWorkflowFieldDefinition FindFieldDefinition(DifyWorkflowParameters parameters, string variableName)
        {
            if (parameters?.Fields == null || string.IsNullOrWhiteSpace(variableName))
            {
                return null;
            }

            return parameters.Fields.Find(field =>
                field != null
                && string.Equals(field.Variable, variableName, StringComparison.OrdinalIgnoreCase));
        }

        private static JToken BuildContextInputValue(JObject localContext, DifyWorkflowFieldDefinition field)
        {
            if (localContext == null)
            {
                return JValue.CreateNull();
            }

            string controlType = field?.ControlType ?? string.Empty;
            if (controlType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return localContext.DeepClone();
            }

            return localContext.ToString(Formatting.None);
        }

        private static string BuildWorkflowSchemaText(DifyWorkflowParameters parameters)
        {
            if (parameters?.Fields == null || parameters.Fields.Count == 0)
            {
                return "当前 workflow 未暴露 user_input_form 字段。\r\n如果工作流完全由固定常量驱动，可直接在右侧 Inputs JSON 中手工填写 inputs。";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < parameters.Fields.Count; i++)
            {
                DifyWorkflowFieldDefinition field = parameters.Fields[i];
                builder.Append("控件类型: ").Append(field.ControlType ?? string.Empty).AppendLine();
                builder.Append("显示名称: ").Append(field.Label ?? string.Empty).AppendLine();
                builder.Append("变量名: ").Append(field.Variable ?? string.Empty).AppendLine();
                builder.Append("是否必填: ").Append(field.Required ? "是" : "否").AppendLine();
                if (field.DefaultValue != null)
                {
                    builder.Append("默认值: ").Append(field.DefaultValue.ToString(Formatting.None)).AppendLine();
                }
                if (field.RawDefinition != null)
                {
                    builder.Append("原始定义: ").Append(field.RawDefinition.ToString(Formatting.None)).AppendLine();
                }
                if (i < parameters.Fields.Count - 1)
                {
                    builder.AppendLine(new string('-', 48));
                }
            }
            return builder.ToString();
        }

        private JObject BuildLocalContext()
        {
            JObject context = new JObject
            {
                ["capturedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["userName"] = GetUserId(),
                ["source"] = "Automation",
                ["page"] = "AI助手"
            };

            int procIndex = SF.frmProc?.SelectedProcNum ?? -1;
            int stepIndex = SF.frmProc?.SelectedStepNum ?? -1;
            int opIndex = SF.frmDataGrid?.iSelectedRow ?? -1;
            List<Proc> procs = SF.frmProc?.procsList;
            Proc selectedProc = null;
            Step selectedStep = null;
            OperationType selectedOperation = null;

            JObject selection = new JObject
            {
                ["procIndex"] = CreateNullableIntToken(procIndex),
                ["stepIndex"] = CreateNullableIntToken(stepIndex),
                ["opIndex"] = CreateNullableIntToken(opIndex)
            };

            if (procs != null && procIndex >= 0 && procIndex < procs.Count)
            {
                selectedProc = procs[procIndex];
                selection["procId"] = selectedProc?.head?.Id.ToString();
                selection["procName"] = selectedProc?.head?.Name ?? string.Empty;
                selection["procDisable"] = selectedProc?.head?.Disable ?? false;
                selection["stepCount"] = selectedProc?.steps?.Count ?? 0;

                if (stepIndex >= 0 && selectedProc?.steps != null && stepIndex < selectedProc.steps.Count)
                {
                    selectedStep = selectedProc.steps[stepIndex];
                    selection["stepId"] = selectedStep?.Id.ToString();
                    selection["stepName"] = selectedStep?.Name ?? string.Empty;
                    selection["stepDisable"] = selectedStep?.Disable ?? false;
                    selection["opCount"] = selectedStep?.Ops?.Count ?? 0;

                    if (opIndex >= 0 && selectedStep?.Ops != null && opIndex < selectedStep.Ops.Count)
                    {
                        selectedOperation = selectedStep.Ops[opIndex];
                        selection["opId"] = selectedOperation?.Id.ToString();
                        selection["opName"] = selectedOperation?.Name ?? string.Empty;
                        selection["operaType"] = selectedOperation?.OperaType ?? string.Empty;
                        selection["opDisable"] = selectedOperation?.Disable ?? false;
                        selection["opNote"] = selectedOperation?.Note ?? string.Empty;
                    }
                }
            }

            context["selection"] = selection;

            EngineSnapshot snapshot = null;
            if (SF.DR != null && procIndex >= 0)
            {
                snapshot = SF.DR.GetSnapshot(procIndex);
            }

            JObject runtime = new JObject
            {
                ["selectedProcState"] = snapshot?.State.ToString() ?? ProcRunState.Stopped.ToString(),
                ["selectedProcAlarming"] = snapshot?.IsAlarm ?? false,
                ["selectedProcBreakpoint"] = snapshot?.IsBreakpoint ?? false,
                ["selectedProcAlarmMessage"] = snapshot?.AlarmMessage ?? string.Empty,
                ["engineReady"] = SF.DR != null,
                ["procCount"] = procs?.Count ?? 0
            };
            context["runtime"] = runtime;

            return context;
        }

        private static JToken CreateNullableIntToken(int value)
        {
            if (value < 0)
            {
                return JValue.CreateNull();
            }
            return new JValue(value);
        }

        private static string BuildRequestPreview(string prompt, JObject localContext, JObject inputs, bool includeContext, string contextVariable)
        {
            StringBuilder builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                builder.AppendLine("用户问题：");
                builder.AppendLine(prompt);
                builder.AppendLine();
            }
            builder.AppendLine(includeContext ? $"自动采集上下文（变量 {contextVariable}）：" : "自动采集上下文：未附带（纯AI聊天）");
            if (includeContext)
            {
                builder.AppendLine(localContext?.ToString(Formatting.Indented) ?? "{}");
            }
            builder.AppendLine();
            builder.AppendLine("inputs：");
            builder.Append(inputs?.ToString(Formatting.Indented) ?? "{}");
            return builder.ToString();
        }

        private static string BuildWorkflowResultText(DifyWorkflowRunReply reply)
        {
            StringBuilder builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(reply.WorkflowRunId))
            {
                builder.AppendLine("workflow_run_id: " + reply.WorkflowRunId);
            }
            if (!string.IsNullOrWhiteSpace(reply.TaskId))
            {
                builder.AppendLine("task_id: " + reply.TaskId);
            }
            if (!string.IsNullOrWhiteSpace(reply.Status))
            {
                builder.AppendLine("status: " + reply.Status);
            }
            if (!string.IsNullOrWhiteSpace(reply.Error))
            {
                builder.AppendLine("error: " + reply.Error);
            }
            builder.AppendLine("outputs:");
            builder.Append(reply.Outputs == null ? "{}" : reply.Outputs.ToString(Formatting.Indented));
            return builder.ToString();
        }

        private void AppendMessage(string role, string content)
        {
            if (rtbConversation.TextLength > 0)
            {
                rtbConversation.AppendText(Environment.NewLine + Environment.NewLine);
            }

            string timeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            rtbConversation.SelectionColor = Color.FromArgb(36, 74, 122);
            rtbConversation.SelectionFont = conversationHeaderFont;
            rtbConversation.AppendText($"{role}  [{timeText}]");
            rtbConversation.SelectionColor = Color.Black;
            rtbConversation.SelectionFont = conversationBodyFont;
            rtbConversation.AppendText(Environment.NewLine);
            rtbConversation.AppendText(content ?? string.Empty);
            rtbConversation.SelectionStart = rtbConversation.TextLength;
            rtbConversation.ScrollToCaret();
        }

        private void SetStatus(string text)
        {
            lblStatus.Text = text;
        }

        private void ReportWorkflowTrace(string message)
        {
            ReportWorkflowTrace(message, false);
        }

        private void ReportWorkflowTrace(string message, bool isError)
        {
            if (string.IsNullOrWhiteSpace(message) || IsDisposed)
            {
                return;
            }

            Action action = delegate
            {
                if (IsDisposed)
                {
                    return;
                }

                SetStatus("状态：" + message);
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("AI助手：" + message, isError ? FrmInfo.Level.Error : FrmInfo.Level.Normal);
                }
            };

            if (InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }

            action();
        }

        private void SetSendingState(bool isSending)
        {
            sending = isSending;
            ApplyPermissions();
        }

        private static string GetUserId()
        {
            string userName = SF.userContextStore?.GetUserName();
            if (!string.IsNullOrWhiteSpace(userName))
            {
                return userName;
            }
            return Environment.UserName;
        }
    }
}
