using System;
using System.Drawing;
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
        private readonly TextBox txtBaseUrl = new TextBox();
        private readonly TextBox txtApiKey = new TextBox();
        private readonly TextBox txtPromptVariable = new TextBox();
        private readonly Button btnSaveConfig = new Button();
        private readonly Button btnReloadConfig = new Button();
        private readonly Button btnClearHistory = new Button();
        private readonly RichTextBox rtbConversation = new RichTextBox();
        private readonly RichTextBox rtbWorkflowSchema = new RichTextBox();
        private readonly TextBox txtInputsJson = new TextBox();
        private readonly TextBox txtPrompt = new TextBox();
        private readonly Button btnSend = new Button();
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
            btnSaveConfig.Enabled = canEditConfig;
            btnReloadConfig.Enabled = canUseAssistant && !sending;
            txtBaseUrl.ReadOnly = !canEditConfig;
            txtApiKey.ReadOnly = !canEditConfig;
        }

        public async void RefreshAssistantView()
        {
            LoadConfig();
            ApplyPermissions();
            await RefreshWorkflowSchemaAsync();
        }

        private async void FrmAiAssistant_Load(object sender, EventArgs e)
        {
            LoadConfig();
            ApplyPermissions();
            await RefreshWorkflowSchemaAsync();
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

            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.ForeColor = Color.FromArgb(64, 64, 64);
            lblStatus.BackColor = Color.White;
            lblStatus.Padding = new Padding(12, 0, 12, 0);
            lblStatus.Text = "状态：等待输入";

            lblPromptVariable.Text = "主问题变量名";
            lblPromptVariable.Dock = DockStyle.Fill;
            lblPromptVariable.TextAlign = ContentAlignment.MiddleLeft;

            txtPromptVariable.Dock = DockStyle.Fill;
            txtPromptVariable.BorderStyle = BorderStyle.FixedSingle;
            txtPromptVariable.Margin = new Padding(0, 1, 12, 1);

            lblPromptTip.Text = "下方文本会写入“主问题变量名”对应的 workflow 输入；其他变量请在左侧 Inputs JSON 中填写。Ctrl+Enter 执行。";
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
            promptHeaderLayout.ColumnCount = 3;
            promptHeaderLayout.RowCount = 1;
            promptHeaderLayout.Dock = DockStyle.Fill;
            promptHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            promptHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            promptHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            promptHeaderLayout.Controls.Add(lblPromptVariable, 0, 0);
            promptHeaderLayout.Controls.Add(txtPromptVariable, 1, 0);
            promptHeaderLayout.Controls.Add(lblPromptTip, 2, 0);

            TableLayoutPanel promptActionLayout = new TableLayoutPanel();
            promptActionLayout.ColumnCount = 3;
            promptActionLayout.RowCount = 1;
            promptActionLayout.Dock = DockStyle.Fill;
            promptActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            promptActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            promptActionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            promptActionLayout.Controls.Add(btnClearHistory, 1, 0);
            promptActionLayout.Controls.Add(btnSend, 2, 0);

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

            leftLayout.Controls.Add(BuildGroupBox("连接配置", configLayout), 0, 0);
            leftLayout.Controls.Add(BuildGroupBox("Workflow 输入结构", rtbWorkflowSchema), 0, 1);
            leftLayout.Controls.Add(BuildGroupBox("Inputs JSON", txtInputsJson), 0, 2);

            rightLayout.Controls.Add(BuildGroupBox("运行记录", conversationLayout), 0, 0);
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
            if (!TryBuildWorkflowInputs(prompt, out JObject inputs, out string buildError))
            {
                MessageBox.Show(buildError, "输入参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("状态：workflow 输入构建失败");
                return;
            }

            AppendMessage("用户", BuildRequestPreview(prompt, inputs));
            txtPrompt.Clear();
            SetSendingState(true);
            SetStatus("状态：正在执行 workflow");

            try
            {
                string userId = GetUserId();
                DifyWorkflowRunReply reply = await Task.Run(() => DifyWorkflowClient.RunWorkflow(config, inputs, userId));
                AppendMessage("Workflow", BuildWorkflowResultText(reply));
                SetStatus(string.IsNullOrWhiteSpace(reply.WorkflowRunId)
                    ? "状态：workflow 已执行完成"
                    : $"状态：workflow 已执行完成 {reply.WorkflowRunId}");
            }
            catch (Exception ex)
            {
                AppendMessage("系统", $"请求失败：{ex.Message}");
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

        private bool TryBuildWorkflowInputs(string prompt, out JObject inputs, out string error)
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
            if (parameters?.Fields == null)
            {
                return string.Empty;
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

        private static string BuildRequestPreview(string prompt, JObject inputs)
        {
            StringBuilder builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                builder.AppendLine("问题内容：");
                builder.AppendLine(prompt);
                builder.AppendLine();
            }
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
