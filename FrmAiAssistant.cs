using Automation.Bridge;
using Markdig;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public sealed class FrmAiAssistant : Form
    {
        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel();
        private readonly TableLayoutPanel configLayout = new TableLayoutPanel();
        private readonly TableLayoutPanel chatLayout = new TableLayoutPanel();
        private readonly TableLayoutPanel inputLayout = new TableLayoutPanel();
        private readonly Panel topToolbar = new Panel();
        private WebView2 webViewConversation;
        // 标记当前是否正在流式输出 assistant 文本（assistant_chunk），用于在同一渲染段累积而非每 chunk 新建 div。
        private bool streamingAssistant;
        // 流式段累积的 Markdown 源码，流式期间实时转 HTML 渲染，段结束时做最终渲染。
        private readonly StringBuilder streamingMarkdown = new StringBuilder();
        // 当前流式段对应的临时 div id（每段递增），用于流式渲染与最终 HTML 替换定位。
        private string streamingDivId;
        private int streamingSegmentIndex;
        // 串行化 WebView2 脚本执行，保证 HTML 追加/替换顺序与事件顺序一致。
        private Task pendingScriptTask = Task.CompletedTask;
        // Markdig 管道（表格、任务列表等高级扩展），用于 Goose 回复 Markdown→HTML。
        private static readonly MarkdownPipeline markdownPipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        // 流式渲染节流：记录上次实时渲染 HTML 的时间，避免每个 token 都执行 Markdig 转换导致卡顿。
        private DateTime lastStreamRender;
        // 思维链折叠窗口：工具调用/结果/思考过程放在固定高度可滚动窗口内，PromptAsync 结束后自动折叠。
        private string currentThinkingBoxId;
        private int thinkingBoxIndex;
        // 最后一段流式 assistant 文本，PromptAsync 结束后作为最终回答渲染到对话区（其余段是思考过程，折叠在 thinking-box 内）。
        private string lastAssistantText;
        private readonly TextBox txtPrompt = new TextBox();
        private readonly TextBox txtGooseExecutable = new TextBox();
        private readonly TextBox txtWorkingDirectory = new TextBox();
        private readonly TextBox txtMcpUri = new TextBox();
        private readonly TextBox txtSessionName = new TextBox();
        private readonly ComboBox cboProvider = new ComboBox();
        // 从 Goose config.yaml 读取的已注册 provider 列表，供 Provider/Model 下拉框使用。
        private List<GooseProviderInfo> gooseProviders = new List<GooseProviderInfo>();
        private readonly ComboBox cboModel = new ComboBox();
        private readonly NumericUpDown nudMaxTurns = new NumericUpDown();
        private readonly Button btnSaveConfig = new Button();
        private readonly Button btnReloadConfig = new Button();
        private readonly Button btnCheckGoose = new Button();
        private readonly Button btnSend = new Button();
        private readonly Button btnStop = new Button();
        private readonly Button btnResetSession = new Button();
        private readonly Button btnConfig = new Button();
        private Form configForm;
        private CheckBox chkFullPermission;
        private readonly Label lblStatus = new Label();
        private readonly HashSet<string> promptedPreviewIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object clientLock = new object();

        private GooseAcpClient gooseClient;
        private CancellationTokenSource promptCts;
        private bool sending;
        private bool fullPermissionMode = false;
        private bool disposingAll;
        private string lastConfirmedPreviewId;

        // Bridge 服务在生成预演记录时读取此属性，若为 true 则直接标记预演为已确认，
        // 避免 TryPromptPreviewConfirmation 通过 HTTP 回调 Bridge 确认导致 UI 线程死锁。
        public bool IsFullPermissionMode => fullPermissionMode;

        public FrmAiAssistant()
        {
            InitializeLayout();
            Load += FrmAiAssistant_Load;
            FormClosing += FrmAiAssistant_FormClosing;
        }

        public void ApplyPermissions()
        {
            bool canAccess = SF.HasPermission(PermissionKeys.ProcessAccess);
            bool canEditConfig = SF.HasPermission(PermissionKeys.AppConfigAccess);

            txtPrompt.Enabled = canAccess && !sending;
            btnSend.Enabled = canAccess && !sending;
            btnStop.Enabled = canAccess && sending;
            btnResetSession.Enabled = canAccess && !sending;
            btnCheckGoose.Enabled = canAccess && !sending;
            btnReloadConfig.Enabled = canAccess && !sending;

            txtGooseExecutable.ReadOnly = !canEditConfig || sending;
            txtWorkingDirectory.ReadOnly = !canEditConfig || sending;
            txtMcpUri.ReadOnly = !canEditConfig || sending;
            txtSessionName.ReadOnly = !canEditConfig || sending;
            cboProvider.Enabled = canEditConfig && !sending;
            cboModel.Enabled = canEditConfig && !sending;
            nudMaxTurns.Enabled = canEditConfig && !sending;
            btnSaveConfig.Enabled = canEditConfig && !sending;
        }

        public void RefreshAssistantView()
        {
            LoadConfig();
            ApplyPermissions();
        }

        private async void FrmAiAssistant_Load(object sender, EventArgs e)
        {
            LoadConfig();
            ApplyPermissions();
            await InitializeWebViewAsync();
        }

        // WebView2 需异步初始化 CoreWebView2 后才能 NavigateToString/ExecuteScriptAsync，初始化后载入基础 HTML（白底 + #messages 容器 + 内嵌 CSS）。
        private async Task InitializeWebViewAsync()
        {
            if (webViewConversation == null)
            {
                return;
            }
            try
            {
                await webViewConversation.EnsureCoreWebView2Async();
                webViewConversation.CoreWebView2.NavigateToString(BaseConversationHtml);
            }
            catch (Exception ex)
            {
                SetStatus("状态：WebView2 初始化失败：" + ex.Message);
            }
        }

        // 对话区基础 HTML：白底，#messages 容器，气泡布局 CSS（用户右对齐蓝气泡、AI 左对齐浅底）。
        private const string BaseConversationHtml =
"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>" +
"*{box-sizing:border-box;}" +
"body{font-family:'微软雅黑',sans-serif;font-size:14px;margin:12px;color:#18202c;}" +
".msg{margin-bottom:14px;display:flex;flex-direction:column;}" +
".msg.user{align-items:flex-end;}" +
".msg.assistant{align-items:flex-start;}" +
".msg .role{font-size:11px;color:#999;margin-bottom:3px;padding:0 4px;}" +
".msg.user .content{background:#e3f2fd;border-radius:12px 12px 2px 12px;padding:8px 12px;max-width:75%;word-break:break-word;overflow-wrap:anywhere;}" +
".msg.assistant .content{max-width:92%;padding:4px 8px;word-break:break-word;overflow-wrap:anywhere;}" +
".msg.error .content{color:darkred;}" +
".tool-call{color:#603e0e;font-family:monospace;padding:2px 0 2px 16px;align-self:flex-start;max-width:100%;overflow-wrap:anywhere;}" +
".tool-result{color:gray;font-family:monospace;padding:2px 0 2px 16px;align-self:flex-start;max-width:100%;overflow-wrap:anywhere;}" +
"p{margin:4px 0;}" +
"ul,ol{margin:4px 0;padding-left:20px;}" +
"li{margin:2px 0;}" +
"blockquote{margin:4px 0;padding:4px 12px;border-left:3px solid #ddd;color:#666;background:#fafafa;}" +
"table{border-collapse:collapse;width:100%;margin:6px 0;table-layout:fixed;}" +
"th,td{border:1px solid #ccc;padding:4px 8px;word-break:break-word;overflow-wrap:anywhere;}" +
"th{background:#f5f5f5;}" +
"pre{background:#f6f8fa;padding:8px;border-radius:4px;overflow-x:auto;white-space:pre-wrap;word-wrap:break-word;max-width:100%;}" +
"code{background:#f6f8fa;padding:2px 4px;border-radius:3px;font-size:13px;}" +
"pre code{background:none;padding:0;}" +
"h1,h2,h3{margin:8px 0;}" +
"h1{font-size:1.3em;}h2{font-size:1.15em;}h3{font-size:1.05em;}" +
"img{max-width:100%;}" +
"hr{border:none;border-top:1px solid #ddd;margin:8px 0;}" +
".streaming-preview{font-family:inherit;background:#fafafa;padding:6px;border-radius:4px;}" +
".thinking-box{max-height:200px;overflow-y:auto;background:#fafbfc;border:1px solid #d0d7de;border-radius:6px;margin:4px 0 8px 0;padding:4px 8px;}" +
".thinking-box.collapsed{max-height:36px;overflow:hidden;}" +
".thinking-box .toggle-bar{color:#6a737d;font-size:11px;cursor:pointer;padding:4px 0;margin-bottom:2px;user-select:none;border-bottom:1px solid #eee;}" +
".thinking-box.collapsed .toggle-bar{border-bottom:none;margin-bottom:0;}" +
".thinking-box .toggle-bar:hover{color:#0366d6;}" +
".thinking-box .toggle-bar::before{content:'▼ ';}" +
".thinking-box.collapsed .toggle-bar::before{content:'▶ ';}" +
"</style>" +
"<script>function toggleThinkingBox(id){var el=document.getElementById(id);if(el){el.classList.toggle('collapsed');if(el.classList.contains('collapsed')){el.scrollTop=0;}}}</script>" +
"</head><body><div id=\"messages\"></div></body></html>";

        private void FrmAiAssistant_FormClosing(object sender, FormClosingEventArgs e)
        {
            DisposeGooseClient();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                disposingAll = true;
                DisposeGooseClient();
                promptCts?.Dispose();
                webViewConversation?.Dispose();
                configForm?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeLayout()
        {
            SuspendLayout();
            Text = "AI助手";
            BackColor = Color.FromArgb(244, 247, 250);
            Font = new Font("微软雅黑", 10F);

            rootLayout.Dock = DockStyle.Fill;
            rootLayout.ColumnCount = 1;
            rootLayout.RowCount = 3;
            rootLayout.Padding = new Padding(14);
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

            BuildConfigLayout();
            BuildTopToolbar();
            BuildMainLayout();

            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.ForeColor = Color.FromArgb(56, 66, 88);
            lblStatus.Text = "状态：等待 Goose 会话。";

            rootLayout.Controls.Add(topToolbar, 0, 0);
            rootLayout.Controls.Add(chatLayout, 0, 1);
            rootLayout.Controls.Add(lblStatus, 0, 2);
            Controls.Add(rootLayout);
            ResumeLayout(false);
        }

        private void BuildTopToolbar()
        {
            topToolbar.Dock = DockStyle.Fill;
            topToolbar.BackColor = Color.FromArgb(238, 241, 245);
            topToolbar.Padding = new Padding(0, 6, 0, 6);

            btnConfig.Text = "配置";
            btnConfig.Dock = DockStyle.Left;
            btnConfig.Width = 80;
            btnConfig.FlatStyle = FlatStyle.System;
            btnConfig.Margin = new Padding(0);
            btnConfig.Click += BtnConfig_Click;

            btnResetSession.Text = "重置会话";
            btnResetSession.Dock = DockStyle.Left;
            btnResetSession.Width = 90;
            btnResetSession.FlatStyle = FlatStyle.System;
            btnResetSession.Margin = new Padding(6, 0, 0, 0);
            btnResetSession.Click += BtnResetSession_Click;

            // 先加的 Dock=Left 会停在最右侧，故按从右到左的视觉顺序倒序加入
            topToolbar.Controls.Add(btnResetSession);
            topToolbar.Controls.Add(btnConfig);
        }

        private void BuildConfigLayout()
        {
            configLayout.Dock = DockStyle.Fill;
            configLayout.BackColor = Color.White;
            configLayout.Padding = new Padding(10);
            configLayout.ColumnCount = 8;
            configLayout.RowCount = 3;
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

            AddConfigRow(0, "Goose", txtGooseExecutable, "工作目录", txtWorkingDirectory);
            AddConfigRow(1, "MCP地址", txtMcpUri, "会话名", txtSessionName);
            InitializeProviderModelDropdowns();
            AddConfigRow(2, "供应商", cboProvider, "模型", cboModel);

            Label lblTurns = BuildLabel("轮次");
            nudMaxTurns.Dock = DockStyle.Fill;
            nudMaxTurns.Minimum = 1;
            nudMaxTurns.Maximum = 200;
            nudMaxTurns.Value = GooseConfigStorage.DefaultMaxTurns;
            nudMaxTurns.Margin = new Padding(0, 2, 8, 2);
            configLayout.Controls.Add(lblTurns, 4, 2);
            configLayout.Controls.Add(nudMaxTurns, 5, 2);

            btnSaveConfig.Text = "保存配置";
            btnReloadConfig.Text = "重载配置";
            btnCheckGoose.Text = "检查Goose";
            StyleButton(btnSaveConfig);
            StyleButton(btnReloadConfig);
            StyleButton(btnCheckGoose);
            btnSaveConfig.Click += BtnSaveConfig_Click;
            btnReloadConfig.Click += BtnReloadConfig_Click;
            btnCheckGoose.Click += BtnCheckGoose_Click;
            configLayout.Controls.Add(btnSaveConfig, 6, 0);
            configLayout.Controls.Add(btnReloadConfig, 7, 0);
            configLayout.Controls.Add(btnCheckGoose, 6, 1);
            configLayout.SetColumnSpan(btnCheckGoose, 2);
        }

        private void AddConfigRow(int row, string label1, Control control1, string label2, Control control2)
        {
            configLayout.Controls.Add(BuildLabel(label1), 0, row);
            PrepareConfigInput(control1);
            configLayout.Controls.Add(control1, 1, row);
            configLayout.Controls.Add(BuildLabel(label2), 2, row);
            PrepareConfigInput(control2);
            configLayout.Controls.Add(control2, 3, row);
        }

        private void BuildMainLayout()
        {
            chatLayout.Dock = DockStyle.Fill;
            chatLayout.ColumnCount = 1;
            chatLayout.RowCount = 2;
            chatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            chatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));

            webViewConversation = new WebView2
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            chatLayout.Controls.Add(webViewConversation, 0, 0);

            inputLayout.Dock = DockStyle.Fill;
            inputLayout.ColumnCount = 3;
            inputLayout.RowCount = 1;
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));

            txtPrompt.Dock = DockStyle.Fill;
            txtPrompt.Multiline = true;
            txtPrompt.ScrollBars = ScrollBars.Vertical;
            txtPrompt.AcceptsReturn = true;
            txtPrompt.BorderStyle = BorderStyle.FixedSingle;
            txtPrompt.Margin = new Padding(0, 8, 8, 0);
            txtPrompt.KeyDown += TxtPrompt_KeyDown;

            btnSend.Text = "发送";
            btnStop.Text = "停止";
            StyleButton(btnSend);
            StyleButton(btnStop);
            btnSend.Click += BtnSend_Click;
            btnStop.Click += BtnStop_Click;

            inputLayout.Controls.Add(txtPrompt, 0, 0);
            inputLayout.Controls.Add(btnSend, 1, 0);
            inputLayout.Controls.Add(btnStop, 2, 0);
            chatLayout.Controls.Add(inputLayout, 0, 1);
        }

        private static Label BuildLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private static void PrepareTextBox(TextBox textBox, bool multiline)
        {
            textBox.Dock = DockStyle.Fill;
            textBox.Multiline = multiline;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Margin = new Padding(0, 2, 8, 2);
        }

        private static void PrepareConfigInput(Control control)
        {
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 2, 8, 2);
            if (control is TextBox textBox)
            {
                textBox.Multiline = false;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            if (control is ComboBox comboBox)
            {
                comboBox.DropDownStyle = ComboBoxStyle.DropDown;
                comboBox.FlatStyle = FlatStyle.System;
            }
        }

        private void InitializeProviderModelDropdowns()
        {
            // 优先从 Goose config.yaml 读取已注册的 provider（如 custom_deepseek），
            // 避免 GooseAcpClient 传入的 GOOSE_PROVIDER 与 Goose 实际注册名不一致。
            gooseProviders = GooseConfigStorage.TryLoadGooseProviders();

            cboProvider.BeginUpdate();
            cboProvider.Items.Clear();
            cboProvider.Items.Add("使用 Goose 配置");
            foreach (GooseProviderInfo info in gooseProviders)
            {
                if (!string.IsNullOrWhiteSpace(info.Name))
                {
                    cboProvider.Items.Add(info.Name);
                }
            }
            // 追加 Goose 内置 provider 名作为补充选项（config.yaml 未注册时仍可选用）。
            string[] standardProviders = { "openai", "anthropic", "google", "ollama", "openrouter", "azure_openai" };
            foreach (string std in standardProviders)
            {
                bool exists = false;
                foreach (GooseProviderInfo info in gooseProviders)
                {
                    if (string.Equals(info.Name, std, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    cboProvider.Items.Add(std);
                }
            }
            cboProvider.EndUpdate();
            cboProvider.SelectedIndex = 0;
            cboProvider.SelectedIndexChanged += CboProvider_SelectedIndexChanged;

            RefreshModelOptions(string.Empty, string.Empty);
        }

        private void CboProvider_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshModelOptions(NormalizeGooseOverride(cboProvider.Text), cboModel.Text);
        }

        private void RefreshModelOptions(string provider, string currentModel)
        {
            string normalizedCurrentModel = NormalizeGooseOverride(currentModel);
            var models = new List<string> { "使用 Goose 配置" };

            // 优先：若选中的 provider 在 Goose config 里有配 model，显示该 model。
            if (!string.IsNullOrWhiteSpace(provider))
            {
                GooseProviderInfo match = null;
                foreach (GooseProviderInfo info in gooseProviders)
                {
                    if (string.Equals(info.Name, provider, StringComparison.OrdinalIgnoreCase))
                    {
                        match = info;
                        break;
                    }
                }
                if (match != null && !string.IsNullOrWhiteSpace(match.Model))
                {
                    models.Add(match.Model);
                }
            }

            // 追加：标准 provider 的预设模型列表。
            foreach (string m in GetModelOptions(provider))
            {
                if (!models.Contains(m))
                {
                    models.Add(m);
                }
            }

            cboModel.BeginUpdate();
            cboModel.Items.Clear();
            foreach (string model in models)
            {
                cboModel.Items.Add(model);
            }
            cboModel.EndUpdate();

            if (string.IsNullOrWhiteSpace(normalizedCurrentModel))
            {
                cboModel.SelectedIndex = 0;
                return;
            }

            cboModel.Text = normalizedCurrentModel;
        }

        private static string[] GetModelOptions(string provider)
        {
            switch ((provider ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "deepseek":
                    return new[] { "使用 Goose 配置", "deepseek-v4-pro", "deepseek-chat", "deepseek-reasoner" };
                case "openai":
                    return new[] { "使用 Goose 配置", "gpt-5", "gpt-5-mini", "gpt-4.1", "gpt-4.1-mini" };
                case "anthropic":
                    return new[] { "使用 Goose 配置", "claude-sonnet-4-5", "claude-opus-4-1", "claude-3-7-sonnet-latest" };
                case "google":
                    return new[] { "使用 Goose 配置", "gemini-2.5-pro", "gemini-2.5-flash" };
                case "ollama":
                    return new[] { "使用 Goose 配置", "llama3.1", "qwen2.5-coder", "deepseek-r1" };
                default:
                    return new[] { "使用 Goose 配置" };
            }
        }

        private static string NormalizeGooseOverride(string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            return string.Equals(trimmed, "使用 Goose 配置", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
        }

        private static void StyleButton(Button button)
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(4, 8, 0, 0);
            button.FlatStyle = FlatStyle.System;
        }

        private void LoadConfig()
        {
            if (!GooseConfigStorage.TryLoad(out GooseConfig config, out string error))
            {
                SetStatus($"状态：Goose 配置读取失败：{error}");
                MessageBox.Show(error, "Goose 配置读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                config = GooseConfigStorage.CreateDefaultConfig();
            }

            txtGooseExecutable.Text = config.GooseExecutablePath;
            TryResolveGooseExecutablePath(txtGooseExecutable.Text, out string resolvedGoosePath);
            if (!string.IsNullOrWhiteSpace(resolvedGoosePath))
            {
                txtGooseExecutable.Text = resolvedGoosePath;
            }
            txtWorkingDirectory.Text = config.WorkingDirectory;
            txtMcpUri.Text = config.McpUri;
            txtSessionName.Text = config.SessionName;
            cboProvider.Text = string.IsNullOrWhiteSpace(config.Provider) ? "使用 Goose 配置" : config.Provider;
            RefreshModelOptions(config.Provider, config.Model);
            nudMaxTurns.Value = Math.Max(nudMaxTurns.Minimum, Math.Min(nudMaxTurns.Maximum, config.MaxTurns));
            fullPermissionMode = config.FullPermissionMode;
            if (chkFullPermission != null && !chkFullPermission.IsDisposed)
            {
                chkFullPermission.Checked = fullPermissionMode;
            }
            SetStatus($"状态：Goose 配置已加载：{GooseConfigStorage.ConfigPath}");
        }

        private bool TryBuildConfig(out GooseConfig config, out string error)
        {
            config = new GooseConfig
            {
                GooseExecutablePath = txtGooseExecutable.Text.Trim(),
                WorkingDirectory = txtWorkingDirectory.Text.Trim(),
                McpUri = txtMcpUri.Text.Trim(),
                SessionName = txtSessionName.Text.Trim(),
                Provider = NormalizeGooseOverride(cboProvider.Text),
                Model = NormalizeGooseOverride(cboModel.Text),
                MaxTurns = (int)nudMaxTurns.Value,
                FullPermissionMode = fullPermissionMode
            };

            if (TryResolveGooseExecutablePath(config.GooseExecutablePath, out string resolvedGoosePath))
            {
                config.GooseExecutablePath = resolvedGoosePath;
                txtGooseExecutable.Text = resolvedGoosePath;
            }

            return GooseConfigStorage.TryValidate(config, out error);
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "发送 AI 对话"))
            {
                return;
            }
            string prompt = txtPrompt.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }
            if (!TryBuildConfig(out GooseConfig config, out string error))
            {
                MessageBox.Show(error, "Goose 配置无效", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AppendConversation("用户", prompt, Color.FromArgb(22, 72, 130));
            txtPrompt.Clear();
            sending = true;
            lastAssistantText = null;
            promptCts?.Dispose();
            promptCts = new CancellationTokenSource();
            ApplyPermissions();
            SetStatus("状态：正在请求 Goose。");

            try
            {
                GooseAcpClient client = EnsureGooseClient(config);
                await client.PromptAsync(prompt, promptCts.Token).ConfigureAwait(true);
                SetStatus("状态：Goose 本轮响应完成。");
            }
            catch (OperationCanceledException)
            {
                SetStatus("状态：已停止本轮生成。");
                AppendConversation("系统", "已停止本轮生成。", Color.DarkOrange);
            }
            catch (Exception ex)
            {
                SetStatus($"状态：Goose 请求失败：{ex.Message}");
                AppendConversation("错误", ex.Message, Color.Red);
            }
            finally
            {
                // 保证本轮最后一段流式内容被完整渲染（AI 纯文字回复不调工具时，
                // 不会有后续非 chunk 事件触发 FinishStreaming，必须在此兜底）。
                FinishStreaming();
                // 把最后一段 assistant 文本作为最终回答渲染到对话区（其余段是思考过程，已折叠在 thinking-box 内）。
                if (!string.IsNullOrWhiteSpace(lastAssistantText))
                {
                    AppendConversation("Goose", lastAssistantText, Color.FromArgb(30, 104, 74));
                    lastAssistantText = null;
                }
                // 折叠思维链窗口（思考过程 + 工具调用/结果），让最终回复占据视觉焦点。
                CollapseThinkingBox();
                sending = false;
                ApplyPermissions();
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            promptCts?.Cancel();
            lock (clientLock)
            {
                gooseClient?.Cancel();
            }
            SetStatus("状态：已请求停止 Goose。");
        }

        private void BtnResetSession_Click(object sender, EventArgs e)
        {
            DisposeGooseClient();
            promptedPreviewIds.Clear();
            lastConfirmedPreviewId = null;
            streamingAssistant = false;
            streamingMarkdown.Clear();
            streamingDivId = null;
            streamingSegmentIndex = 0;
            lastAssistantText = null;
            currentThinkingBoxId = null;
            pendingScriptTask = Task.CompletedTask;
            ResetConversationHtml();
            // 生成唯一会话名避免 Goose 加载磁盘上的同名历史上下文
            txtSessionName.Text = "automation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            SetStatus("状态：Goose 会话已重置。");
        }

        // 重置对话区：清空 #messages 容器内容（保留基础 HTML/CSS），等价于原 rtbConversation.Clear()。
        private void ResetConversationHtml()
        {
            if (webViewConversation == null || webViewConversation.CoreWebView2 == null)
            {
                return;
            }
            webViewConversation.CoreWebView2.NavigateToString(BaseConversationHtml);
        }

        // 弹出配置窗体（非模态）：configLayout 从主界面移出后，由"配置"按钮触发显示。
        // 关闭时仅 Hide 不销毁，保留 configLayout 控件值供 TryBuildConfig 读取。
        private void BtnConfig_Click(object sender, EventArgs e)
        {
            if (configForm == null || configForm.IsDisposed)
            {
                configForm = new Form
                {
                    Text = "AI 助手配置",
                    StartPosition = FormStartPosition.CenterParent,
                    ClientSize = new Size(640, 220),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false
                };
                configLayout.Dock = DockStyle.Fill;
                chkFullPermission = new CheckBox
                {
                    Text = "完全权限模式（不审核工具调用）",
                    Checked = fullPermissionMode,
                    AutoSize = true
                };
                chkFullPermission.CheckedChanged += (s, ev) => fullPermissionMode = chkFullPermission.Checked;
                var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(10, 4, 10, 4) };
                bottomPanel.Controls.Add(chkFullPermission);
                configForm.Controls.Add(configLayout); // configLayout Fill 填充
                configForm.Controls.Add(bottomPanel);  // 底部完全权限开关
                // 关闭时隐藏不销毁，保证 configLayout 控件值保留（应用退出时 disposingAll 跳过拦截）
                configForm.FormClosing += (s, ev) =>
                {
                    if (disposingAll)
                    {
                        return;
                    }
                    ev.Cancel = true;
                    configForm.Hide();
                };
            }
            // 嵌入态下本窗体 TopLevel=false，不能作为 Show 的 Owner（WinForms 要求 Owner 必须顶层窗体），
            // 故沿 ParentForm 链取首个顶层窗体（即 FrmMain）做 Owner，保留 CenterParent 居中且非模态
            Form ownerForm = ParentForm;
            if (ownerForm != null)
            {
                configForm.Show(ownerForm);
            }
            else
            {
                configForm.Show();
            }
        }

        private void BtnSaveConfig_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.AppConfigAccess, "保存 Goose 配置"))
            {
                return;
            }
            if (!TryBuildConfig(out GooseConfig config, out string error))
            {
                MessageBox.Show(error, "Goose 配置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // 在 TrySave 之前获取旧配置（TrySave 内部 SetCache 会覆盖缓存）
            GooseConfigStorage.TryGetCached(out GooseConfig oldConfig, out _);
            if (!GooseConfigStorage.TrySave(config, out error))
            {
                MessageBox.Show(error, "Goose 配置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // 只有影响 Goose 进程的配置项变化时才重建客户端，避免仅 FullPermissionMode 变化时丢失会话上下文
            bool needRestart = oldConfig == null
                || !string.Equals(oldConfig.GooseExecutablePath, config.GooseExecutablePath, StringComparison.Ordinal)
                || !string.Equals(oldConfig.WorkingDirectory, config.WorkingDirectory, StringComparison.Ordinal)
                || !string.Equals(oldConfig.McpUri, config.McpUri, StringComparison.Ordinal)
                || !string.Equals(oldConfig.SessionName, config.SessionName, StringComparison.Ordinal)
                || !string.Equals(oldConfig.Provider, config.Provider, StringComparison.Ordinal)
                || !string.Equals(oldConfig.Model, config.Model, StringComparison.Ordinal)
                || oldConfig.MaxTurns != config.MaxTurns;
            if (needRestart)
            {
                DisposeGooseClient();
            }
            SetStatus("状态：Goose 配置已保存。");
            MessageBox.Show("Goose 配置保存成功。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnReloadConfig_Click(object sender, EventArgs e)
        {
            LoadConfig();
            ApplyPermissions();
        }

        private async void BtnCheckGoose_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "检查 Goose"))
            {
                return;
            }
            if (!TryBuildConfig(out GooseConfig config, out string error))
            {
                MessageBox.Show(error, "Goose 配置无效", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetStatus("状态：正在检查 Goose。");
            string result = await Task.Run(() => CheckGooseCore(config)).ConfigureAwait(true);
            AppendConversation("系统", result, Color.FromArgb(56, 66, 88));
            SetStatus("状态：Goose 检查完成。");
        }

        private void TxtPrompt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }
            if (e.Alt || e.Shift)
            {
                txtPrompt.AppendText("\r\n");
                txtPrompt.SelectionStart = txtPrompt.TextLength;
                e.SuppressKeyPress = true;
                return;
            }
            e.SuppressKeyPress = true;
            BtnSend_Click(sender, EventArgs.Empty);
        }

        private GooseAcpClient EnsureGooseClient(GooseConfig config)
        {
            lock (clientLock)
            {
                if (gooseClient != null)
                {
                    return gooseClient;
                }

                gooseClient = new GooseAcpClient(config);
                gooseClient.EventReceived += GooseClient_EventReceived;
                gooseClient.PermissionRequestHandler = HandlePermissionRequest;
                return gooseClient;
            }
        }

        private JObject HandlePermissionRequest(JObject request)
        {
            if (IsDisposed)
            {
                return BuildPermissionCancelled();
            }
            if (InvokeRequired)
            {
                return (JObject)Invoke(new Func<JObject, JObject>(HandlePermissionRequest), request);
            }

            string title = request["toolCall"]?["title"]?.Value<string>()
                ?? request["toolCall"]?["name"]?.Value<string>()
                ?? "Goose 权限请求";

            if (fullPermissionMode)
            {
                // 完全权限模式：把工具调用信息显示到聊天区，让用户看到批准了什么
                AppendConversation("系统", "✅ 自动批准：" + title, Color.FromArgb(35, 92, 48));

                // 预演确认在 tool_result 事件中自动完成（TryPromptPreviewConfirmation），
                // 权限请求本身不含 previewId（previewId 由 Bridge 在工具执行后生成）。

                JArray fullOptions = request["options"] as JArray;
                string fullOptionId = FindAllowOptionId(fullOptions);
                if (string.IsNullOrWhiteSpace(fullOptionId))
                {
                    return new JObject { ["outcome"] = new JObject { ["outcome"] = "allowed" } };
                }
                return new JObject { ["outcome"] = new JObject { ["outcome"] = "selected", ["optionId"] = fullOptionId } };
            }

            string text = request.ToString(Formatting.Indented);
            DialogResult dialogResult = MessageBox.Show(this, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dialogResult != DialogResult.Yes)
            {
                return BuildPermissionCancelled();
            }

            // 用户批准后，在 options 中查找 "allow" 类选项（optionId 或 title 含 allow），
            // 避免盲目取 options[0] 导致选中 "deny"/"modify" 等非执行选项。
            JArray options = request["options"] as JArray;
            string optionId = FindAllowOptionId(options);
            if (string.IsNullOrWhiteSpace(optionId))
            {
                // 无选项或未找到 allow 选项：直接返回 allowed，允许工具执行。
                return new JObject
                {
                    ["outcome"] = new JObject
                    {
                        ["outcome"] = "allowed"
                    }
                };
            }

            return new JObject
            {
                ["outcome"] = new JObject
                {
                    ["outcome"] = "selected",
                    ["optionId"] = optionId
                }
            };
        }

        // 在权限请求的 options 数组中查找 "allow" 类选项。
        // Goose ACP 的 options 顺序不保证 allow 在第一位，盲目取 [0] 可能选中 deny/modify。
        private static string FindAllowOptionId(JArray options)
        {
            if (options == null || options.Count == 0)
            {
                return null;
            }
            // 优先匹配 optionId 或 title 中含 "allow" 的选项
            foreach (JToken opt in options)
            {
                string oid = opt["optionId"]?.Value<string>() ?? opt["id"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(oid))
                {
                    continue;
                }
                string optTitle = opt["title"]?.Value<string>() ?? string.Empty;
                if (oid.IndexOf("allow", StringComparison.OrdinalIgnoreCase) >= 0
                    || optTitle.IndexOf("allow", StringComparison.OrdinalIgnoreCase) >= 0
                    || optTitle.IndexOf("允许", StringComparison.Ordinal) >= 0
                    || optTitle.IndexOf("通过", StringComparison.Ordinal) >= 0)
                {
                    return oid;
                }
            }
            // 回退：取第一个选项（多数情况下第一个是 allow）
            return options[0]?["optionId"]?.Value<string>() ?? options[0]?["id"]?.Value<string>();
        }

        private static JObject BuildPermissionCancelled()
        {
            return new JObject
            {
                ["outcome"] = new JObject
                {
                    ["outcome"] = "cancelled"
                }
            };
        }

        private void GooseClient_EventReceived(GooseAcpEvent item)
        {
            if (IsDisposed)
            {
                return;
            }
            if (InvokeRequired)
            {
                BeginInvoke(new Action<GooseAcpEvent>(GooseClient_EventReceived), item);
                return;
            }

            // 流式 token：在同一行追加（打字机效果），不写调试区，不加事件头。
            if (string.Equals(item.Kind, "assistant_chunk", StringComparison.Ordinal))
            {
                AppendStreamingText(item.Text);
                return;
            }

            // 非流式事件：若之前在流式输出，先换行结束当前流式段。
            // justFinished 标记用于跳过紧随其后的 assistant 事件（内容与流式预览重复）。
            bool justFinishedStreaming = false;
            if (streamingAssistant)
            {
                FinishStreaming();
                justFinishedStreaming = true;
            }

            if (string.Equals(item.Kind, "assistant", StringComparison.Ordinal))
            {
                // 流式刚结束，内容已在流式 div 中完整渲染，跳过重复的 assistant 事件。
                if (!justFinishedStreaming)
                {
                    AppendConversation("Goose", item.Text, Color.FromArgb(30, 104, 74));
                }
            }
            else if (string.Equals(item.Kind, "tool_call", StringComparison.Ordinal))
            {
                AppendToolEntry("🔧", item.Text, Color.FromArgb(96, 62, 14));
            }
            else if (string.Equals(item.Kind, "tool_result", StringComparison.Ordinal))
            {
                AppendToolEntry("  →", item.Text, Color.Gray);
            }
            else if (string.Equals(item.Kind, "tool", StringComparison.Ordinal))
            {
                AppendConversation("工具", item.Text, Color.FromArgb(96, 62, 14));
            }
            else if (string.Equals(item.Kind, "error", StringComparison.Ordinal)
                || string.Equals(item.Kind, "stderr", StringComparison.Ordinal)
                || string.Equals(item.Kind, "exit", StringComparison.Ordinal))
            {
                AppendConversation("系统", item.Text, Color.DarkRed);
            }

            // 预演确认：在工具返回结果（tool_result）时检查是否包含 previewId。
            // previewId 由 Bridge 在 preview_intent/preview_patch 执行后生成并返回，
            // tool_call 事件只有工具参数，还没有 previewId。
            if (string.Equals(item.Kind, "tool_result", StringComparison.Ordinal))
            {
                TryPromptPreviewConfirmation(item.Raw);
            }
        }

        // 工具调用/结果紧凑单行显示，放在思维链折叠窗口内（不单独占对话区空间）。
        private void AppendToolEntry(string marker, string text, Color color)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed)
            {
                return;
            }
            bool isCall = marker != null && marker.Contains("🔧");
            string cls = isCall ? "tool-call" : "tool-result";
            string display = isCall ? "🔧" : "→";
            string html = "<div class=\"" + cls + "\">" + display + " " + HtmlEncode(text) + "</div>";
            AppendToThinkingBox(html);
        }

        // 确保思维链窗口存在（首次调用时创建），返回窗口 ID。
        private string EnsureThinkingBox()
        {
            if (currentThinkingBoxId != null)
            {
                return currentThinkingBoxId;
            }
            thinkingBoxIndex++;
            currentThinkingBoxId = "thinking-box-" + thinkingBoxIndex;
            string boxId = currentThinkingBoxId;
            string html = "<div class=\"thinking-box\" id=\"" + boxId + "\">"
                + "<div class=\"toggle-bar\" onclick=\"toggleThinkingBox('" + boxId + "')\">思维过程（点击折叠/展开）</div>"
                + "</div>";
            EnqueueAppendHtml(html);
            return boxId;
        }

        // 将 HTML 追加到当前思维链窗口内部，并自动滚动到底部。
        private void AppendToThinkingBox(string html)
        {
            string boxId = EnsureThinkingBox();
            string htmlJson = JsonConvert.SerializeObject(html);
            string js = "var box=document.getElementById('" + boxId + "');if(box){box.insertAdjacentHTML('beforeend'," + htmlJson + ");box.scrollTop=box.scrollHeight;}";
            EnqueueScript(js);
        }

        // 折叠当前思维链窗口（PromptAsync 结束后调用）。
        private void CollapseThinkingBox()
        {
            if (currentThinkingBoxId == null)
            {
                return;
            }
            string js = "var box=document.getElementById('" + currentThinkingBoxId + "');if(box){box.scrollTop=0;box.classList.add('collapsed');}";
            EnqueueScript(js);
            currentThinkingBoxId = null;
        }

        // 流式追加文本：累积 Markdown 源码，流式期间实时 Markdig 转 HTML 渲染（节流 50ms 避免卡顿）。
        // 流式段渲染到思维链窗口内（思考过程），不直接显示在对话区。
        // 最后一段会在 PromptAsync 结束后由 BtnSend_Click 作为最终回答渲染到对话区。
        private void AppendStreamingText(string text)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed || string.IsNullOrEmpty(text))
            {
                return;
            }
            if (!streamingAssistant)
            {
                streamingSegmentIndex++;
                streamingDivId = "streaming-" + streamingSegmentIndex;
                streamingMarkdown.Clear();
                streamingMarkdown.Append(text);
                streamingAssistant = true;
                lastStreamRender = DateTime.Now;
                string html = "<div class=\"streaming-segment\" id=\"" + streamingDivId + "\">"
                    + MarkdownToHtml(streamingMarkdown.ToString()) + "</div>";
                AppendToThinkingBox(html);
            }
            else
            {
                streamingMarkdown.Append(text);
                if ((DateTime.Now - lastStreamRender).TotalMilliseconds >= 50)
                {
                    lastStreamRender = DateTime.Now;
                    UpdateStreamingPreview();
                }
            }
        }

        // 结束流式段：把累积的 Markdown 用 Markdig 转 HTML，替换思维链窗口内流式 div 的内容。
        // 同时记录最后一段流式文本，BtnSend_Click finally 块会把它作为最终回答渲染到对话区。
        private void FinishStreaming()
        {
            if (!streamingAssistant)
            {
                return;
            }
            string renderedHtml = MarkdownToHtml(streamingMarkdown.ToString());
            string htmlJson = JsonConvert.SerializeObject(renderedHtml);
            string idJson = JsonConvert.SerializeObject(streamingDivId);
            string boxId = currentThinkingBoxId ?? "";
            string js = "var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson + ";}var box=document.getElementById('" + boxId + "');if(box){box.scrollTop=box.scrollHeight;}";
            EnqueueScript(js);
            lastAssistantText = streamingMarkdown.ToString();
            streamingAssistant = false;
            streamingDivId = null;
        }

        // 追加对话消息：根据 role/color 决定 CSS 类，用户消息纯文本转义，Goose 消息走 Markdown→HTML。
        private void AppendConversation(string role, string text, Color color)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed)
            {
                return;
            }
            string time = DateTime.Now.ToString("HH:mm:ss");
            string cls;
            string contentHtml;
            if (role == "用户")
            {
                cls = "msg user";
                contentHtml = HtmlEncode(text);
            }
            else if (role == "Goose")
            {
                cls = "msg assistant";
                contentHtml = MarkdownToHtml(text);
            }
            else if (role == "错误" || color.ToArgb() == Color.DarkRed.ToArgb())
            {
                cls = "msg error";
                contentHtml = HtmlEncode(text);
            }
            else
            {
                cls = "msg assistant";
                contentHtml = HtmlEncode(text);
            }
            string html = "<div class=\"" + cls + "\"><span class=\"role\">" + HtmlEncode(role) + " " + HtmlEncode(time) + "</span>"
                + "<div class=\"content\">" + contentHtml + "</div></div>";
            EnqueueAppendHtml(html);
        }

        // 更新思维链窗口内流式 div 的 innerHTML 为 Markdig 渲染后的 HTML，并滚动窗口到底部。
        private void UpdateStreamingPreview()
        {
            if (string.IsNullOrEmpty(streamingDivId))
            {
                return;
            }
            string html = MarkdownToHtml(streamingMarkdown.ToString());
            string htmlJson = JsonConvert.SerializeObject(html);
            string idJson = JsonConvert.SerializeObject(streamingDivId);
            string boxId = currentThinkingBoxId ?? "";
            string js = "var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson + ";}var box=document.getElementById('" + boxId + "');if(box){box.scrollTop=box.scrollHeight;}";
            EnqueueScript(js);
        }

        // 向 #messages 容器追加 HTML 片段并滚动到底部。
        private void EnqueueAppendHtml(string html)
        {
            string htmlJson = JsonConvert.SerializeObject(html);
            string js = "document.getElementById('messages').insertAdjacentHTML('beforeend'," + htmlJson + ");window.scrollTo(0,document.body.scrollHeight);";
            EnqueueScript(js);
        }

        // 串行化 ExecuteScriptAsync：通过 ContinueWith 链保证脚本按入队顺序执行（状态修改在调用前同步完成，脚本内 HTML 已捕获）。
        private void EnqueueScript(string js)
        {
            WebView2 localWebView = webViewConversation;
            if (localWebView == null || localWebView.CoreWebView2 == null)
            {
                return;
            }
            pendingScriptTask = pendingScriptTask.ContinueWith(
                async _ =>
                {
                    try
                    {
                        await localWebView.CoreWebView2.ExecuteScriptAsync(js);
                    }
                    catch
                    {
                        // 忽略脚本执行异常，避免单条脚本失败阻塞后续渲染。
                    }
                },
                TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
        }

        // HTML 转义文本（< > & 等）。
        private static string HtmlEncode(string text)
        {
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }

        // Markdown→HTML（Markdig 高级扩展，支持表格、任务列表等）。
        // 渲染前先做 NormalizeMarkdownForRendering 预处理，修复 AI 常见的表格格式问题：
        //   1. 表格分隔行前缺少表头行 → 补空表头
        //   2. 多行表格行被拼接到同一行 → 按列数拆分
        private static string MarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return string.Empty;
            }
            string normalized = NormalizeMarkdownForRendering(markdown);
            return Markdown.ToHtml(normalized, markdownPipeline);
        }

        // 预处理 Markdown：修复 AI 生成的常见表格格式问题，使 Markdig 能正确解析。
        private static string NormalizeMarkdownForRendering(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return markdown;
            }

            string text = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = text.Split('\n');
            var result = new StringBuilder();
            int tableColumns = 0;
            bool prevLineIsTableRow = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                // 检测表格分隔行：|---|---| 或 |:---:|:---| 等
                if (IsTableSeparator(trimmed, out int cols))
                {
                    tableColumns = cols;
                    // 分隔行前必须有表头行；若前一行不是表格行，补一个空表头
                    if (!prevLineIsTableRow)
                    {
                        result.AppendLine(BuildEmptyTableRow(cols));
                    }
                    result.AppendLine(line);
                    prevLineIsTableRow = false;
                    continue;
                }

                // 在表格上下文中，若一行含有多组表格行（| 数量超出单行预期），拆分为多行
                if (tableColumns > 0 && trimmed.StartsWith("|"))
                {
                    string[] splitRows = SplitConcatenatedTableRows(trimmed, tableColumns);
                    foreach (string row in splitRows)
                    {
                        result.AppendLine(row);
                    }
                    prevLineIsTableRow = true;
                }
                else
                {
                    if (!trimmed.StartsWith("|"))
                    {
                        // 离开表格上下文
                        tableColumns = 0;
                    }
                    result.AppendLine(line);
                    prevLineIsTableRow = trimmed.StartsWith("|");
                }
            }

            return result.ToString();
        }

        // 判断是否为表格分隔行（如 |---|---| 或 |:---:|:---|），并输出列数。
        private static bool IsTableSeparator(string line, out int columnCount)
        {
            columnCount = 0;
            if (string.IsNullOrEmpty(line) || !line.StartsWith("|") || !line.EndsWith("|"))
            {
                return false;
            }
            // 去掉首尾 | 后按 | 分割，每段应包含 --- 
            string inner = line.Substring(1, line.Length - 2);
            string[] parts = inner.Split('|');
            if (parts.Length == 0)
            {
                return false;
            }
            foreach (string part in parts)
            {
                string p = part.Trim();
                if (p.Length == 0)
                {
                    return false;
                }
                // 允许 :---, ---:, :---:, ---
                string core = p.Trim(':');
                if (core.Length < 3 || !core.All(c => c == '-'))
                {
                    return false;
                }
            }
            columnCount = parts.Length;
            return true;
        }

        // 构建指定列数的空表头行：| | | | |
        private static string BuildEmptyTableRow(int columnCount)
        {
            if (columnCount <= 0)
            {
                return "| |";
            }
            var sb = new StringBuilder();
            for (int i = 0; i < columnCount; i++)
            {
                sb.Append("| ");
            }
            sb.Append("|");
            return sb.ToString();
        }

        // 将拼接在同一行的多个表格行按列数拆分为独立行。
        // 原理：每行有 columnCount+1 个 |，统计 | 数量后按组切分。
        private static string[] SplitConcatenatedTableRows(string line, int columnCount)
        {
            int pipesPerRow = columnCount + 1;
            int totalPipes = line.Count(c => c == '|');

            // 只有一行，无需拆分
            if (totalPipes <= pipesPerRow)
            {
                return new[] { line };
            }

            var rows = new List<string>();
            var current = new StringBuilder();
            int pipeCount = 0;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                current.Append(c);
                if (c == '|')
                {
                    pipeCount++;
                    if (pipeCount == pipesPerRow)
                    {
                        // 当前行结束
                        rows.Add(current.ToString().Trim());
                        current.Clear();
                        pipeCount = 0;
                        // 跳过行间空格
                        while (i + 1 < line.Length && char.IsWhiteSpace(line[i + 1]))
                        {
                            i++;
                        }
                    }
                }
            }

            if (current.Length > 0)
            {
                string remaining = current.ToString().Trim();
                if (remaining.Length > 0)
                {
                    rows.Add(remaining);
                }
            }

            return rows.Count > 0 ? rows.ToArray() : new[] { line };
        }

        private void SetStatus(string text)
        {
            lblStatus.Text = text ?? string.Empty;
        }

        // 从 ACP tool_call_update 完成通知中提取工具返回的 JSON 文本。
        // ACP 通知结构：message.params.update.content[0].text 或 message.params.content[0].text
        // text 字段是 MCP 工具返回的 JSON 字符串，例如 {"ok":true,"type":"intent.preview","data":{"previewId":"..."}}
        private static string ExtractToolResultText(JObject raw)
        {
            JToken parameters = raw?["params"];
            JToken update = parameters?["update"] ?? parameters;
            JToken content = update?["content"];
            if (content is JArray arr && arr.Count > 0)
            {
                JToken first = arr[0];
                JToken textToken = first["text"] ?? first?["content"]?["text"];
                if (textToken != null && textToken.Type == JTokenType.String)
                {
                    return textToken.Value<string>();
                }
            }
            return null;
        }

        private void TryPromptPreviewConfirmation(JObject raw)
        {
            // 从工具返回结果中提取 previewId。
            // previewId 只在 preview_intent / preview_patch 的返回值中存在，
            // 且嵌套在 JSON 字符串里（MCP 工具返回 string），不能用 FindFirstString 深度搜索。
            string resultText = ExtractToolResultText(raw);
            if (string.IsNullOrWhiteSpace(resultText))
            {
                return;
            }

            JObject resultObj;
            try
            {
                resultObj = JObject.Parse(resultText);
            }
            catch
            {
                return;
            }

            // 只处理预演类工具返回（intent.preview / patch.preview），避免误匹配。
            string resultType = resultObj["type"]?.Value<string>() ?? string.Empty;
            if (!string.Equals(resultType, "intent.preview", StringComparison.Ordinal)
                && !string.Equals(resultType, "patch.preview", StringComparison.Ordinal))
            {
                return;
            }

            string previewId = resultObj["data"]?["previewId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(previewId) || promptedPreviewIds.Contains(previewId))
            {
                return;
            }
            promptedPreviewIds.Add(previewId);

            // 完全权限模式：预演记录已在 Bridge 服务端直接标记为已确认（BuildRegisteredPatchPreview），
            // 此处只需提示用户，不需要通过 HTTP 回调确认（避免 UI 线程死锁）。
            if (fullPermissionMode)
            {
                AppendConversation("系统", $"✅ 自动确认预演，previewId={previewId}", Color.FromArgb(35, 92, 48));
                return;
            }

            // 正常模式：弹窗让用户确认预演结果。
            string patchHash = resultObj["data"]?["patchHash"]?.Value<string>() ?? string.Empty;
            string summary = BuildPreviewSummary(resultObj, previewId, patchHash);
            DialogResult result = MessageBox.Show(this, summary, "确认预演结果", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                _ = ConfirmPreviewAsync(previewId);
            }
            else
            {
                AppendConversation("系统", $"预演未确认：{previewId}", Color.DarkOrange);
            }
        }

        // 注意：使用 ConfigureAwait(false) + BeginInvoke 包装 UI 更新，
        // 使得本方法可被 HandlePermissionRequest 同步等待（.GetAwaiter().GetResult()），
        // 避免 fire-and-forget 导致 Goose 在确认完成前就提交变更触发 ValidateConfirmedPreview 失败。
        private async Task ConfirmPreviewAsync(string previewId)
        {
            try
            {
                string response = await SendBridgeRequestAsync("POST", "/bridge/previews/confirm", new JObject
                {
                    ["previewId"] = previewId
                }).ConfigureAwait(false);
                lastConfirmedPreviewId = previewId;
                BeginInvoke((Action)(() => AppendConversation("系统", $"预演已确认，previewId={previewId}。提交时必须携带该 previewId。", Color.FromArgb(35, 92, 48))));
            }
            catch (Exception ex)
            {
                BeginInvoke((Action)(() => AppendConversation("错误", "确认预演失败：" + ex.Message, Color.Red)));
            }
        }

        private static string BuildPreviewSummary(JObject raw, string previewId, string patchHash)
        {
            JArray messages = FindFirstArray(raw, "messages");
            JArray changes = FindFirstArray(raw, "changes");
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("确认后才允许提交本次流程写入。");
            builder.AppendLine();
            builder.AppendLine("previewId: " + previewId);
            builder.AppendLine("patchHash: " + patchHash);
            builder.AppendLine("变更数量: " + (changes?.Count ?? 0));
            if (messages != null && messages.Count > 0)
            {
                builder.AppendLine();
                int count = Math.Min(messages.Count, 6);
                for (int i = 0; i < count; i++)
                {
                    builder.AppendLine("- " + messages[i]);
                }
            }
            builder.AppendLine();
            builder.AppendLine("是否确认本次预演结果？");
            return builder.ToString();
        }

        private static async Task<string> SendBridgeRequestAsync(string method, string path, JObject body)
        {
            return await Task.Run(() =>
            {
                string requestId = Guid.NewGuid().ToString("N");
                JObject envelope = new JObject
                {
                    ["requestId"] = requestId,
                    ["method"] = method,
                    ["path"] = path,
                    ["bodyJson"] = body == null ? "{}" : body.ToString(Formatting.None)
                };
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", AutomationBridgeHost.DefaultPipeName, PipeDirection.InOut))
                {
                    pipe.Connect(30000);
                    WritePipeMessage(pipe, envelope.ToString(Formatting.None));
                    string responseText = ReadPipeMessage(pipe);
                    JObject response = JObject.Parse(responseText);
                    int statusCode = ReadCaseInsensitiveInt(response, "statusCode", "StatusCode") ?? 500;
                    string bodyJson = ReadCaseInsensitiveString(response, "bodyJson", "BodyJson") ?? string.Empty;
                    if (statusCode < 200 || statusCode >= 300)
                    {
                        throw new InvalidOperationException(bodyJson);
                    }
                    return bodyJson;
                }
            }).ConfigureAwait(false);
        }

        private static void WritePipeMessage(Stream stream, string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] length = BitConverter.GetBytes(payload.Length);
            stream.Write(length, 0, length.Length);
            if (payload.Length > 0)
            {
                stream.Write(payload, 0, payload.Length);
            }
            stream.Flush();
        }

        private static string ReadPipeMessage(Stream stream)
        {
            byte[] lengthBuffer = ReadExactly(stream, sizeof(int));
            int length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length < 0)
            {
                throw new InvalidDataException("Bridge 响应长度非法。");
            }
            if (length == 0)
            {
                return string.Empty;
            }
            return Encoding.UTF8.GetString(ReadExactly(stream, length));
        }

        private static int? ReadCaseInsensitiveInt(JObject obj, params string[] names)
        {
            JToken token = ReadCaseInsensitiveToken(obj, names);
            return token != null && token.Type == JTokenType.Integer ? token.Value<int>() : (int?)null;
        }

        private static string ReadCaseInsensitiveString(JObject obj, params string[] names)
        {
            JToken token = ReadCaseInsensitiveToken(obj, names);
            return token != null && token.Type == JTokenType.String ? token.Value<string>() : null;
        }

        private static JToken ReadCaseInsensitiveToken(JObject obj, params string[] names)
        {
            if (obj == null || names == null)
            {
                return null;
            }
            foreach (string name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                {
                    return token;
                }
            }
            return null;
        }

        private static byte[] ReadExactly(Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Bridge 连接提前关闭。");
                }
                offset += read;
            }
            return buffer;
        }

        private static string FormatJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }
            try
            {
                return JToken.Parse(text).ToString(Formatting.Indented);
            }
            catch
            {
                return text;
            }
        }

        private static string CheckGooseCore(GooseConfig config)
        {
            StringBuilder builder = new StringBuilder();
            string gooseExecutable = ResolveGooseExecutablePath(config.GooseExecutablePath);
            builder.AppendLine($"Goose 可执行文件：{gooseExecutable}");
            builder.AppendLine(RunProcessCapture(gooseExecutable, "--version", config.WorkingDirectory, 10000));
            builder.AppendLine(RunProcessCapture(gooseExecutable, "info -v", config.WorkingDirectory, 15000));
            try
            {
                GooseConfig resolvedConfig = new GooseConfig
                {
                    GooseExecutablePath = gooseExecutable,
                    WorkingDirectory = config.WorkingDirectory,
                    McpUri = config.McpUri,
                    SessionName = config.SessionName,
                    Provider = config.Provider,
                    Model = config.Model,
                    MaxTurns = config.MaxTurns
                };
                using (GooseAcpClient client = new GooseAcpClient(resolvedConfig))
                using (CancellationTokenSource cts = new CancellationTokenSource(30000))
                {
                    client.InitializeAsync(cts.Token).GetAwaiter().GetResult();
                }
                builder.AppendLine("goose acp initialize：成功");
            }
            catch (Exception ex)
            {
                builder.AppendLine("goose acp initialize：失败");
                builder.AppendLine(ex.Message);
            }
            return builder.ToString();
        }

        private static string ResolveGooseExecutablePath(string configuredPath)
        {
            return TryResolveGooseExecutablePath(configuredPath, out string resolvedPath)
                ? resolvedPath
                : configuredPath;
        }

        private static bool TryResolveGooseExecutablePath(string configuredPath, out string resolvedPath)
        {
            resolvedPath = null;
            List<string> candidates = new List<string>();
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                AddCandidate(candidates, configuredPath.Trim());
                if (!Path.IsPathRooted(configuredPath))
                {
                    AddCandidate(candidates, Path.Combine(baseDirectory, configuredPath.Trim()));
                }
            }
            AddCandidate(candidates, Path.Combine(baseDirectory, "Tools", "Goose", "goose.exe"));
            AddCandidate(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "goose.exe"));

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }
            return false;
        }

        private static void AddCandidate(List<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            foreach (string candidate in candidates)
            {
                if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            candidates.Add(path);
        }

        private static string RunProcessCapture(string fileName, string arguments, string workingDirectory, int timeoutMs)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return fileName + " " + arguments + "\r\n启动失败";
                    }
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                        return fileName + " " + arguments + "\r\n执行超时";
                    }
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    return fileName + " " + arguments + "\r\n" + output + error;
                }
            }
            catch (Exception ex)
            {
                return fileName + " " + arguments + "\r\n" + ex.Message;
            }
        }

        private static string FindFirstString(JToken token, string fieldName)
        {
            if (token == null)
            {
                return null;
            }
            if (token is JObject obj)
            {
                if (obj.TryGetValue(fieldName, StringComparison.OrdinalIgnoreCase, out JToken value)
                    && value != null
                    && value.Type == JTokenType.String)
                {
                    return value.Value<string>();
                }
                foreach (JProperty property in obj.Properties())
                {
                    string nested = FindFirstString(property.Value, fieldName);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    string nested = FindFirstString(item, fieldName);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }
            return null;
        }

        private static JArray FindFirstArray(JToken token, string fieldName)
        {
            if (token == null)
            {
                return null;
            }
            if (token is JObject obj)
            {
                if (obj.TryGetValue(fieldName, StringComparison.OrdinalIgnoreCase, out JToken value)
                    && value is JArray array)
                {
                    return array;
                }
                foreach (JProperty property in obj.Properties())
                {
                    JArray nested = FindFirstArray(property.Value, fieldName);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }
            else if (token is JArray rootArray)
            {
                foreach (JToken item in rootArray)
                {
                    JArray nested = FindFirstArray(item, fieldName);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }
            return null;
        }

        // 公开以便 FrmMain.FormClosing 早期调用，先 Kill Goose 进程，
        // 避免后台线程的 HandlePermissionRequest 同步 Invoke 等待已被阻塞的 UI 线程导致死锁。
        public void DisposeGooseClient()
        {
            lock (clientLock)
            {
                try
                {
                    gooseClient?.Dispose();
                }
                catch
                {
                }
                gooseClient = null;
            }
        }
    }
}
