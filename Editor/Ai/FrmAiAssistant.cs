// 模块：编辑器 / AI。
// 职责范围：AI 前台、ACP 会话、预演确认与对话渲染。

using Automation.Bridge;
using Markdig;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public sealed partial class FrmAiAssistant : Form
    {
        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel();
        private WebView2 webViewConversation;
        private bool webViewClosing;
        private bool webViewEventsAttached;
        private bool webDocumentReady;
        private bool viewLoaded;
        // 标记当前是否正在流式输出 assistant 文本（assistant_chunk），用于在同一渲染段累积而非每 chunk 新建 div。
        private bool streamingAssistant;
        // 流式段累积的 Markdown 源码，流式期间实时转 HTML 渲染，段结束时做最终渲染。
        private readonly StringBuilder streamingMarkdown = new StringBuilder();
        // 当前流式段对应的临时 div id（每段递增），用于流式渲染与最终 HTML 替换定位。
        private string streamingDivId;
        private int streamingSegmentIndex;
        private string latestAssistantSegmentText;
        private string latestAssistantSegmentDivId;
        private readonly HashSet<string> flowVisualizationCallIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly JArray flowVisualizationProcesses = new JArray();
        private DateTime promptStartedAt;
        private bool streamingThought;
        private readonly StringBuilder streamingThoughtMarkdown = new StringBuilder();
        private string streamingThoughtDivId;
        private int streamingThoughtSegmentIndex;
        // 串行化 WebView2 脚本执行，保证 HTML 追加/替换顺序与事件顺序一致。
        private Task pendingScriptTask = Task.CompletedTask;
        private int conversationViewGeneration;
        private const string CopyButtonHtml =
            "<button class=\"copy-message\" type=\"button\" onclick=\"copyMessage(this)\" title=\"复制本条文字\" aria-label=\"复制本条文字\">"
            + "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><rect x=\"9\" y=\"9\" width=\"11\" height=\"11\" rx=\"2\"/>"
            + "<path d=\"M15 9V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h3\"/></svg></button>";
        // Markdig 管道（表格、任务列表等高级扩展），用于 Goose 回复 Markdown→HTML。
        private static readonly MarkdownPipeline markdownPipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        // 流式渲染节流：记录上次实时渲染 HTML 的时间，避免每个 token 都执行 Markdig 转换导致卡顿。
        private DateTime lastStreamRender;
        private DateTime lastThoughtRender;
        // 思维链折叠窗口：工具调用/结果/思考过程放在固定高度可滚动窗口内，PromptAsync 结束后自动折叠。
        private string currentThinkingBoxId;
        private int thinkingBoxIndex;
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
        private readonly NumericUpDown nudMaxOutputTokens = new NumericUpDown();
        private readonly NumericUpDown nudTemperature = new NumericUpDown();
        private List<AiModelServiceConfig> modelServices = new List<AiModelServiceConfig>();
        private string modelServiceId = string.Empty;
        private string toolProfile = GooseConfigStorage.DefaultToolProfile;
        private GooseConfig appliedConfig;
        private readonly AiPreviewConfirmationCoordinator previewCoordinator =
            new AiPreviewConfirmationCoordinator();
        private readonly AutomationBridgePreviewClient previewClient =
            new AutomationBridgePreviewClient();
        private readonly AiConversationCoordinator conversationCoordinator =
            new AiConversationCoordinator();

        private GooseAcpClient gooseClient;
        private bool sending;
        private bool standardTestRunning;
        private bool standardTestStopRequested;
        private bool autoApproveMode = false;
        private bool fullPermissionEnabled;
        private const int MaxFileAttachmentCount = 4;
        private const long MaxFileAttachmentBytes = 10L * 1024L * 1024L;
        private readonly List<GooseFileAttachment> pendingFileAttachments = new List<GooseFileAttachment>();
        private readonly Dictionary<string, string> fileAttachmentPreviews = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool replayingTaskEvents;

        private bool HasRunningTasks => conversationCoordinator.HasRunningTasks || standardTestRunning;
        private AiTaskRuntime ActiveTaskRuntime => conversationCoordinator.ActiveRuntime;
        private bool IsActiveTaskRunning => !conversationCoordinator.TaskHomeVisible
            && ActiveTaskRuntime?.Running == true;

        // Bridge 服务在生成预演记录时读取此属性，若为 true 则直接标记预演为已确认，
        // 避免 TryPromptPreviewConfirmation 通过 HTTP 回调 Bridge 确认导致 UI 线程死锁。
        public bool IsAutoApproveMode => autoApproveMode;
        internal bool IsViewLoaded => viewLoaded;

        public FrmAiAssistant()
        {
            UiBranding.Apply(this);
            InitializeLayout();
            Load += FrmAiAssistant_Load;
            FormClosing += FrmAiAssistant_FormClosing;
        }

        public void ApplyPermissions()
        {
            bool busy = HasRunningTasks;
            txtPrompt.Enabled = true;
            txtPrompt.ReadOnly = IsActiveTaskRunning || standardTestRunning;
            txtGooseExecutable.ReadOnly = busy;
            txtWorkingDirectory.ReadOnly = busy;
            txtMcpUri.ReadOnly = busy;
            txtSessionName.ReadOnly = busy;
            cboProvider.Enabled = !busy;
            cboModel.Enabled = !busy;
            nudMaxTurns.Enabled = !busy;
            PushWebAppState();
        }

        public void RefreshAssistantView()
        {
            LoadConfig();
            ApplyPermissions();
        }

        private async void FrmAiAssistant_Load(object sender, EventArgs e)
        {
            LoadConversationHistory();
            LoadConfig();
            ApplyPermissions();
            viewLoaded = true;
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
                string webViewUserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Automation", "WebView2");
                Directory.CreateDirectory(webViewUserDataFolder);
                Microsoft.Web.WebView2.Core.CoreWebView2Environment webViewEnvironment =
                    await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                        null,
                        webViewUserDataFolder);
                await webViewConversation.EnsureCoreWebView2Async(webViewEnvironment);
                webViewConversation.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webViewConversation.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                if (!webViewEventsAttached)
                {
                    webViewConversation.CoreWebView2.WebMessageReceived += WebViewConversation_WebMessageReceived;
                    webViewConversation.CoreWebView2.NavigationCompleted += (s, e) =>
                    {
                        webDocumentReady = true;
                        EnqueueScript("window.__automationConversationViewGeneration="
                            + conversationViewGeneration + ";", false);
                        RenderActiveTaskView();
                        PushWebAppState();
                    };
                    webViewEventsAttached = true;
                }
                webDocumentReady = false;
                webViewConversation.CoreWebView2.NavigateToString(BaseConversationHtml);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "WebView2 初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void FrmAiAssistant_FormClosing(object sender, FormClosingEventArgs e)
        {
            webViewClosing = true;
            webDocumentReady = false;
            DisposeGooseClient();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                webViewClosing = true;
                webDocumentReady = false;
                DisposeGooseClient();
                webViewConversation?.Dispose();
                webViewConversation = null;
            }
            base.Dispose(disposing);
        }

        private void InitializeLayout()
        {
            SuspendLayout();
            Text = "EW-AI 助手";
            BackColor = UiPalette.Background;
            Font = new Font("微软雅黑", 10F);

            rootLayout.Dock = DockStyle.Fill;
            rootLayout.ColumnCount = 1;
            rootLayout.RowCount = 1;
            rootLayout.Padding = Padding.Empty;
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            BuildConfigLayout();
            BuildMainLayout();

            rootLayout.Controls.Add(webViewConversation, 0, 0);
            Controls.Add(rootLayout);
            ResumeLayout(false);
        }

        private void BuildConfigLayout()
        {
            InitializeProviderModelDropdowns();
            nudMaxTurns.Minimum = 1;
            nudMaxTurns.Maximum = 200;
            nudMaxTurns.Value = GooseConfigStorage.DefaultMaxTurns;
            nudMaxOutputTokens.Minimum = 1024;
            nudMaxOutputTokens.Maximum = 65536;
            nudMaxOutputTokens.Increment = 1024;
            nudMaxOutputTokens.Value = GooseConfigStorage.DefaultMaxOutputTokens;
            nudTemperature.Minimum = 0;
            nudTemperature.Maximum = 1;
            nudTemperature.DecimalPlaces = 2;
            nudTemperature.Increment = 0.05m;
            nudTemperature.Value = (decimal)GooseConfigStorage.DefaultTemperature;
        }

        private void BuildMainLayout()
        {
            webViewConversation = new WebView2
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                // WebView2 用户数据目录（缓存/Cookie/IndexedDB 等）放到 LocalAppData，
                // 避免在程序目录下生成 Automation.exe.WebView2 文件夹污染部署目录。
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Automation", "WebView2")
                }
            };
        }

        private void InitializeProviderModelDropdowns()
        {
            gooseProviders = GooseConfigStorage.TryLoadGooseProviders();

            cboProvider.BeginUpdate();
            cboProvider.Items.Clear();
            string[] standardProviders = { "deepseek", "openai", "anthropic", "google", "openrouter", "ollama", "azure_openai" };
            foreach (string std in standardProviders)
            {
                cboProvider.Items.Add(std);
            }
            cboProvider.EndUpdate();
            cboProvider.SelectedIndex = 0;
            cboProvider.SelectedIndexChanged += CboProvider_SelectedIndexChanged;

            RefreshModelOptions(GooseConfigStorage.DefaultProvider, GooseConfigStorage.DefaultModel);
        }

        private void CboProvider_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshModelOptions(NormalizeGooseOverride(cboProvider.Text), cboModel.Text);
        }

        private void RefreshModelOptions(string provider, string currentModel)
        {
            string normalizedCurrentModel = NormalizeGooseOverride(currentModel);
            var models = new List<string>();

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
                cboModel.SelectedIndex = cboModel.Items.Count > 0 ? 0 : -1;
                return;
            }

            cboModel.Text = normalizedCurrentModel;
        }

        private static string[] GetModelOptions(string provider)
        {
            switch ((provider ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "deepseek":
                    return new[] { "deepseek-v4-pro", "deepseek-v4-flash" };
                case "openai":
                    return new[] { "gpt-5", "gpt-5-mini", "gpt-4.1", "gpt-4.1-mini" };
                case "anthropic":
                    return new[] { "claude-sonnet-4-5", "claude-opus-4-1", "claude-3-7-sonnet-latest" };
                case "google":
                    return new[] { "gemini-2.5-pro", "gemini-2.5-flash" };
                case "openrouter":
                    return new[] { "openai/gpt-5-mini", "anthropic/claude-sonnet-4-5", "deepseek/deepseek-chat" };
                case "ollama":
                    return new[] { "qwen2.5-coder", "deepseek-r1", "llama3.1" };
                case "azure_openai":
                    return new[] { "gpt-5", "gpt-4.1", "gpt-4.1-mini" };
                default:
                    return new string[0];
            }
        }

        private static string NormalizeGooseOverride(string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            return trimmed;
        }

        private async void ChooseFileAttachments()
        {
            if (sending)
            {
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要分析的文件";
                dialog.Filter = "常用文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.csv;*.txt;*.json;*.xml;*.md|所有文件|*.*";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                dialog.RestoreDirectory = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var errors = new List<string>();
                foreach (string path in dialog.FileNames)
                {
                    if (pendingFileAttachments.Count >= MaxFileAttachmentCount)
                    {
                        errors.Add($"最多同时上传{MaxFileAttachmentCount}个文件。");
                        break;
                    }

                    try
                    {
                        FileInfo fileInfo = new FileInfo(path);
                        if (fileInfo.Length <= 0 || fileInfo.Length > MaxFileAttachmentBytes)
                        {
                            errors.Add($"文件大小必须大于0且不超过10 MB：{fileInfo.Name}");
                            continue;
                        }

                        byte[] data = await Task.Run(() => File.ReadAllBytes(path)).ConfigureAwait(true);
                        AttachmentPreparationResult preparation = await Task.Run(
                            () => AttachmentTextExtractor.Prepare(fileInfo.Name, data)).ConfigureAwait(true);
                        if (!TryAddFileAttachment(fileInfo.Name, data, preparation, out string addError))
                        {
                            errors.Add(addError);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"读取文件失败：{Path.GetFileName(path)}（{ex.Message}）");
                    }
                }

                if (errors.Count > 0)
                {
                    ShowWebToast(string.Join(" ", errors));
                }
            }

            PushWebAppState();
        }

        private async void AddDroppedFile(JObject message)
        {
            if (sending)
            {
                return;
            }

            string fileName = Path.GetFileName(message?["name"]?.Value<string>());
            string encoded = message?["data"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(encoded))
            {
                ShowWebToast("拖入的文件数据无效。");
                return;
            }
            if (encoded.Any(char.IsWhiteSpace))
            {
                ShowWebToast("拖入的文件编码格式无效。");
                return;
            }
            try
            {
                byte[] data = Convert.FromBase64String(encoded);
                AttachmentPreparationResult preparation = await Task.Run(
                    () => AttachmentTextExtractor.Prepare(fileName, data)).ConfigureAwait(true);
                if (!TryAddFileAttachment(fileName, data, preparation, out string error))
                {
                    ShowWebToast(error);
                }
            }
            catch (FormatException)
            {
                ShowWebToast("拖入的文件编码格式无效。");
            }
            PushWebAppState();
        }

        private bool TryAddFileAttachment(
            string fileName,
            byte[] data,
            AttachmentPreparationResult preparation,
            out string error)
        {
            error = null;
            if (pendingFileAttachments.Count >= MaxFileAttachmentCount)
            {
                error = $"最多同时上传{MaxFileAttachmentCount}个文件。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "文件名不能为空。";
                return false;
            }
            if (data == null || data.Length == 0 || data.Length > MaxFileAttachmentBytes)
            {
                error = $"文件大小必须大于0且不超过10 MB：{fileName}";
                return false;
            }
            if (preparation == null)
            {
                error = "文件解析结果无效：" + fileName;
                return false;
            }
            string id = Guid.NewGuid().ToString("N");
            string preview = preparation.IsImage ? CreateImagePreviewDataUri(data) : null;
            if (preparation.IsImage
                && !string.Equals(preparation.MimeType, "image/webp", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(preview))
            {
                preparation.Error = "图片文件损坏、尺寸过大或格式与扩展名不匹配。";
            }
            pendingFileAttachments.Add(new GooseFileAttachment(
                id,
                fileName,
                preparation.MimeType,
                preparation.TypeLabel,
                preparation.IsImage,
                data,
                preparation.ExtractedText,
                preparation.Error));
            if (!string.IsNullOrWhiteSpace(preview))
            {
                fileAttachmentPreviews[id] = preview;
            }
            return true;
        }

        private static string CreateImagePreviewDataUri(byte[] data)
        {
            try
            {
                using (var input = new MemoryStream(data, false))
                using (Image image = Image.FromStream(input, false, true))
                {
                    if (image.Width <= 0 || image.Height <= 0
                        || (long)image.Width * image.Height > 40000000L)
                    {
                        return null;
                    }
                    double scale = Math.Min(72.0 / image.Width, 72.0 / image.Height);
                    int width = Math.Max(1, (int)Math.Round(image.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(image.Height * scale));
                    using (var thumbnail = new Bitmap(width, height))
                    using (Graphics graphics = Graphics.FromImage(thumbnail))
                    using (var output = new MemoryStream())
                    {
                        graphics.Clear(UiPalette.SurfaceStrong);
                        graphics.DrawImage(image, 0, 0, width, height);
                        thumbnail.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private void RemoveFileAttachment(string attachmentId)
        {
            if (sending || string.IsNullOrWhiteSpace(attachmentId))
            {
                return;
            }
            pendingFileAttachments.RemoveAll(item => string.Equals(item.Id, attachmentId, StringComparison.Ordinal));
            fileAttachmentPreviews.Remove(attachmentId);
            PushWebAppState();
        }

        private void LoadConfig()
        {
            if (!GooseConfigStorage.TryLoad(out GooseConfig config, out string error))
            {
                MessageBox.Show(error, "EW-AI 配置读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                config = GooseConfigStorage.CreateDefaultConfig();
            }

            txtGooseExecutable.Text = config.GooseExecutablePath;
            TryResolveGooseExecutablePath(txtGooseExecutable.Text, out string resolvedGoosePath);
            if (!string.IsNullOrWhiteSpace(resolvedGoosePath))
            {
                txtGooseExecutable.Text = resolvedGoosePath;
                config.GooseExecutablePath = resolvedGoosePath;
            }
            txtWorkingDirectory.Text = AppDomain.CurrentDomain.BaseDirectory;
            txtMcpUri.Text = config.McpUri;
            txtSessionName.Text = config.SessionName;
            cboProvider.Text = string.IsNullOrWhiteSpace(config.Provider) ? GooseConfigStorage.DefaultProvider : config.Provider;
            RefreshModelOptions(cboProvider.Text, string.IsNullOrWhiteSpace(config.Model) ? GooseConfigStorage.DefaultModel : config.Model);
            modelServices = GooseConfigStorage.CloneModelServices(config.ModelServices);
            modelServiceId = config.ModelServiceId ?? string.Empty;
            nudTemperature.Value = Math.Max(nudTemperature.Minimum,
                Math.Min(nudTemperature.Maximum, (decimal)config.Temperature));
            nudMaxTurns.Value = Math.Max(nudMaxTurns.Minimum, Math.Min(nudMaxTurns.Maximum, config.MaxTurns));
            nudMaxOutputTokens.Value = Math.Max(nudMaxOutputTokens.Minimum,
                Math.Min(nudMaxOutputTokens.Maximum, config.MaxOutputTokens));
            toolProfile = config.ToolProfile;
            if (!string.Equals(toolProfile, "Editor", StringComparison.Ordinal))
            {
                fullPermissionEnabled = false;
            }
            autoApproveMode = config.AutoApproveMode;
            // 保存界面当前实际采用的配置。配置文件首次缺失或损坏时缓存可能为空，
            // 模式/权限切换必须与这份快照比较，不能因此误判为需要重建 Goose 会话。
            appliedConfig = GooseConfigStorage.CloneConfig(config);
            PushWebAppState();
        }

        private async void WebViewConversation_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            JObject message;
            try
            {
                message = JObject.Parse(e.WebMessageAsJson);
            }
            catch
            {
                return;
            }

            string type = message["type"]?.Value<string>() ?? string.Empty;
            switch (type)
            {
                case "ready":
                    webDocumentReady = true;
                    PushWebAppState();
                    break;
                case "send":
                    txtPrompt.Text = message["prompt"]?.Value<string>() ?? string.Empty;
                    BtnSend_Click(sender, EventArgs.Empty);
                    break;
                case "runStandardTests":
                    await RunStandardTestsAsync(
                        (message["ids"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>(),
                        message["separateConversations"]?.Value<bool?>() ?? true).ConfigureAwait(true);
                    break;
                case "chooseFile":
                    ChooseFileAttachments();
                    break;
                case "dropFile":
                    AddDroppedFile(message);
                    break;
                case "removeFile":
                    RemoveFileAttachment(message["id"]?.Value<string>());
                    break;
                case "stop":
                    if (IsActiveTaskRunning || standardTestRunning)
                    {
                        StopCurrentPrompt();
                    }
                    break;
                case "showTaskHome":
                    ShowTaskHome();
                    break;
                case "openTask":
                    SwitchConversation(message["id"]?.Value<string>());
                    break;
                case "reset":
                    BtnResetSession_Click(sender, EventArgs.Empty);
                    break;
                case "newSession":
                    StartNewConversation();
                    break;
                case "deleteSession":
                    DeleteCurrentConversation();
                    break;
                case "switchSession":
                    SwitchConversation(message["id"]?.Value<string>());
                    break;
                case "copyText":
                    string copyText = message["text"]?.Value<string>() ?? string.Empty;
                    try
                    {
                        if (string.IsNullOrEmpty(copyText))
                        {
                            ShowWebToast("没有可复制的文字。");
                        }
                        else
                        {
                            Clipboard.SetText(copyText);
                            ShowWebToast("已复制到剪贴板。");
                        }
                    }
                    catch (Exception copyError)
                    {
                        ShowWebToast("复制失败：" + copyError.Message);
                    }
                    break;
                case "setToolProfile":
                    if (sending)
                    {
                        PushWebAppState();
                        break;
                    }
                    string requestedProfile = message["profile"]?.Value<string>();
                    if (!string.Equals(requestedProfile, "Diagnostic", StringComparison.Ordinal)
                        && !string.Equals(requestedProfile, "Editor", StringComparison.Ordinal))
                    {
                        ShowWebToast("工具模式无效。");
                        PushWebAppState();
                        break;
                    }
                    toolProfile = requestedProfile;
                    if (!string.Equals(toolProfile, "Editor", StringComparison.Ordinal))
                    {
                        fullPermissionEnabled = false;
                    }
                    SaveWebConfig(requestedProfile == "Diagnostic" ? "已切换到诊断模式。" : "已切换到编辑模式。", false);
                    break;
                case "setFullPermission":
                    if (sending)
                    {
                        PushWebAppState();
                        break;
                    }
                    await SetFullPermissionToolsAsync(message["enabled"]?.Value<bool?>() == true)
                        .ConfigureAwait(true);
                    break;
                case "setAutoApprove":
                    if (sending)
                    {
                        PushWebAppState();
                        break;
                    }
                    autoApproveMode = message["enabled"]?.Value<bool?>() ?? false;
                    SaveWebConfig(autoApproveMode ? "自动批准已开启，请谨慎操作。" : "自动批准已关闭。", false);
                    break;
                case "saveModelService":
                    ApplyWebConfig(message["config"] as JObject);
                    SaveModelService(message["service"] as JObject);
                    break;
                case "deleteModelService":
                    ApplyWebConfig(message["config"] as JObject);
                    DeleteModelService(message["id"]?.Value<string>());
                    break;
                case "clearModelServiceApiKey":
                    ClearModelServiceApiKey(message["id"]?.Value<string>());
                    break;
                case "providerChanged":
                    ApplyWebConfig(message["config"] as JObject);
                    cboProvider.Text = message["provider"]?.Value<string>() ?? "deepseek";
                    RefreshModelOptions(NormalizeGooseOverride(cboProvider.Text), string.Empty);
                    PushWebAppState();
                    break;
                case "reloadConfig":
                    LoadConfig();
                    ApplyPermissions();
                    ShowWebToast("配置已重载。");
                    break;
                case "saveConfig":
                    ApplyWebConfig(message["config"] as JObject);
                    string apiKey = message["config"]?["apiKey"]?.Value<string>();
                    string secretProvider = NormalizeGooseOverride(cboProvider.Text);
                    if (!string.IsNullOrWhiteSpace(apiKey)
                        && !AiProviderSecretStorage.TrySaveSecret(secretProvider, apiKey, out string secretSaveError))
                    {
                        ShowWebToast(secretSaveError);
                        PushWebAppState();
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(apiKey)) DisposeGooseClient();
                    SaveWebConfig();
                    break;
                case "clearApiKey":
                    string clearProvider = NormalizeGooseOverride(message["provider"]?.Value<string>());
                    if (string.IsNullOrWhiteSpace(clearProvider))
                    {
                        ShowWebToast("请先选择具体 Provider。");
                    }
                    else if (!AiProviderSecretStorage.TryDeleteSecret(clearProvider, out string clearError))
                    {
                        ShowWebToast(clearError);
                    }
                    else
                    {
                        DisposeGooseClient();
                        ShowWebToast("已清除当前 Provider 的本机 API Key。");
                    }
                    PushWebAppState();
                    break;
                case "checkConfig":
                    ApplyWebConfig(message["config"] as JObject);
                    await CheckWebConfigAsync().ConfigureAwait(true);
                    break;
            }
        }

        private JObject BuildWebAppState()
        {
            string providerText = string.IsNullOrWhiteSpace(cboProvider.Text) ? GooseConfigStorage.DefaultProvider : cboProvider.Text;
            string modelText = string.IsNullOrWhiteSpace(cboModel.Text) ? GooseConfigStorage.DefaultModel : cboModel.Text;
            string normalizedProvider = NormalizeGooseOverride(providerText);
            return new JObject
            {
                ["sending"] = IsActiveTaskRunning || standardTestRunning,
                ["taskHomeVisible"] = conversationCoordinator.TaskHomeVisible,
                ["canAccess"] = true,
                ["canEditConfig"] = !HasRunningTasks,
                ["config"] = new JObject
                {
                    ["gooseExecutablePath"] = txtGooseExecutable.Text,
                    ["workingDirectory"] = txtWorkingDirectory.Text,
                    ["mcpUri"] = txtMcpUri.Text,
                    ["sessionName"] = txtSessionName.Text,
                    ["provider"] = providerText,
                    ["model"] = modelText,
                    ["modelServiceId"] = modelServiceId,
                    ["temperature"] = (double)nudTemperature.Value,
                    ["hasApiKey"] = !string.IsNullOrWhiteSpace(normalizedProvider)
                        && AiProviderSecretStorage.HasSecret(normalizedProvider),
                    ["maxTurns"] = (int)nudMaxTurns.Value,
                    ["maxOutputTokens"] = (int)nudMaxOutputTokens.Value,
                    ["toolProfile"] = toolProfile,
                    ["fullPermissionEnabled"] = fullPermissionEnabled,
                    ["autoApproveMode"] = autoApproveMode
                },
                ["providerOptions"] = BuildComboOptions(cboProvider, providerText),
                ["modelOptions"] = BuildComboOptions(cboModel, modelText),
                ["modelServices"] = new JArray(modelServices.Select(item => new JObject
                {
                    ["id"] = item.Id,
                    ["name"] = item.Name,
                    ["baseUrl"] = item.BaseUrl,
                    ["model"] = item.Model,
                    ["contextLimit"] = item.ContextLimit,
                    ["supportsVision"] = item.SupportsVision,
                    ["requiresApiKey"] = item.RequiresApiKey,
                    ["hasApiKey"] = AiProviderSecretStorage.HasSecret(
                        AiProviderSecretStorage.GetModelServiceSecretKey(item.Id))
                })),
                ["attachments"] = new JArray(pendingFileAttachments.Select(BuildAttachmentWebState)),
                ["testScenarios"] = new JArray(AiStandardTestSuite.Scenarios.Select(item => new JObject
                {
                    ["id"] = item.Id,
                    ["name"] = item.Name,
                    ["description"] = item.Description,
                    ["turnCount"] = item.Prompts.Count
                })),
                ["activeConversationId"] = conversationCoordinator.TaskHomeVisible
                    ? string.Empty
                    : conversationCoordinator.ActiveConversation?.Id ?? string.Empty,
                ["conversations"] = new JArray(conversationCoordinator.Conversations
                    .OrderByDescending(item => item.UpdatedAt)
                    .Select(item => new JObject
                    {
                        ["id"] = item.Id,
                        ["title"] = item.Title,
                        ["updatedAt"] = item.UpdatedAt,
                        ["status"] = conversationCoordinator.TaskRuntimes.TryGetValue(item.Id, out AiTaskRuntime runtime)
                            ? runtime.Running ? "running" : string.Equals(runtime.Status, "失败", StringComparison.Ordinal) ? "failed" : "completed"
                            : "completed",
                        ["statusText"] = conversationCoordinator.TaskRuntimes.TryGetValue(item.Id, out AiTaskRuntime taskRuntime) && taskRuntime.Running
                            ? "进行中"
                            : taskRuntime?.Status == "失败" ? "失败" : string.Empty
                    }))
            };
        }

        private static JArray BuildComboOptions(ComboBox comboBox, string currentValue)
        {
            var array = new JArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in comboBox.Items)
            {
                string text = item?.ToString();
                if (!string.IsNullOrWhiteSpace(text) && seen.Add(text))
                {
                    array.Add(text);
                }
            }
            if (!string.IsNullOrWhiteSpace(currentValue) && seen.Add(currentValue))
            {
                array.Add(currentValue);
            }
            return array;
        }

        private void PushWebAppState()
        {
            if (!webDocumentReady || webViewConversation == null || webViewConversation.CoreWebView2 == null)
            {
                return;
            }
            string stateJson = BuildWebAppState().ToString(Formatting.None);
            EnqueueScript("if(window.automationSetState){window.automationSetState(" + stateJson + ");}");
        }

        private void ApplyWebConfig(JObject config)
        {
            if (config == null)
            {
                return;
            }

            txtGooseExecutable.Text = config["gooseExecutablePath"]?.Value<string>() ?? string.Empty;
            txtWorkingDirectory.Text = config["workingDirectory"]?.Value<string>() ?? string.Empty;
            txtMcpUri.Text = config["mcpUri"]?.Value<string>() ?? string.Empty;
            txtSessionName.Text = config["sessionName"]?.Value<string>() ?? string.Empty;

            string provider = config["provider"]?.Value<string>() ?? GooseConfigStorage.DefaultProvider;
            string model = config["model"]?.Value<string>() ?? GooseConfigStorage.DefaultModel;
            modelServiceId = config["modelServiceId"]?.Value<string>() ?? string.Empty;
            cboProvider.Text = string.IsNullOrWhiteSpace(provider) ? GooseConfigStorage.DefaultProvider : provider;
            RefreshModelOptions(NormalizeGooseOverride(cboProvider.Text), model);
            cboModel.Text = string.IsNullOrWhiteSpace(model) ? GooseConfigStorage.DefaultModel : model;

            int maxTurns = config["maxTurns"]?.Value<int?>() ?? GooseConfigStorage.DefaultMaxTurns;
            nudMaxTurns.Value = Math.Max(nudMaxTurns.Minimum, Math.Min(nudMaxTurns.Maximum, maxTurns));
            int maxOutputTokens = config["maxOutputTokens"]?.Value<int?>()
                ?? GooseConfigStorage.DefaultMaxOutputTokens;
            nudMaxOutputTokens.Value = Math.Max(nudMaxOutputTokens.Minimum,
                Math.Min(nudMaxOutputTokens.Maximum, maxOutputTokens));
            double temperature = config["temperature"]?.Value<double?>() ?? GooseConfigStorage.DefaultTemperature;
            nudTemperature.Value = Math.Max(nudTemperature.Minimum,
                Math.Min(nudTemperature.Maximum, (decimal)temperature));
            toolProfile = config["toolProfile"]?.Value<string>() ?? GooseConfigStorage.DefaultToolProfile;
            autoApproveMode = config["autoApproveMode"]?.Value<bool?>() ?? false;
        }

        private async void SaveWebConfig(string successMessage = null, bool closeConfigAfterSave = true)
        {
            GooseConfig oldConfig = GooseConfigStorage.CloneConfig(appliedConfig);
            if (oldConfig == null)
            {
                GooseConfigStorage.TryGetCached(out oldConfig, out _);
            }
            if (!TryBuildConfig(out GooseConfig config, out string error))
            {
                if (oldConfig != null)
                {
                    toolProfile = oldConfig.ToolProfile;
                    autoApproveMode = oldConfig.AutoApproveMode;
                }
                ShowWebToast("配置无效：" + error);
                PushWebAppState();
                return;
            }

            bool requiresGooseProcessRestart = oldConfig == null
                || !string.Equals(oldConfig.GooseExecutablePath, config.GooseExecutablePath, StringComparison.Ordinal)
                || !string.Equals(oldConfig.WorkingDirectory, config.WorkingDirectory, StringComparison.Ordinal)
                || !string.Equals(oldConfig.McpUri, config.McpUri, StringComparison.Ordinal)
                || !string.Equals(oldConfig.SessionName, config.SessionName, StringComparison.Ordinal)
                || !string.Equals(oldConfig.Provider, config.Provider, StringComparison.Ordinal)
                || !string.Equals(oldConfig.Model, config.Model, StringComparison.Ordinal)
                || !string.Equals(oldConfig.ModelServiceId, config.ModelServiceId, StringComparison.OrdinalIgnoreCase)
                || !SelectedModelServiceEqual(oldConfig, config)
                || Math.Abs(oldConfig.Temperature - config.Temperature) > 0.0001d
                || oldConfig.MaxTurns != config.MaxTurns
                || oldConfig.MaxOutputTokens != config.MaxOutputTokens;
            bool uriChanged = oldConfig == null
                || !string.Equals(oldConfig.McpUri, config.McpUri, StringComparison.Ordinal);
            bool profileChanged = oldConfig == null
                || !string.Equals(oldConfig.ToolProfile, config.ToolProfile, StringComparison.Ordinal);
            GooseAcpClient activeClient = gooseClient;
            bool sessionToolsReloaded = false;
            if (uriChanged || profileChanged)
            {
                try
                {
                    if (Workspace.Main?.McpServerManager == null)
                    {
                        throw new InvalidOperationException("MCP Server管理器未初始化");
                    }
                    if (uriChanged)
                    {
                        await Workspace.Main.McpServerManager.EnsureStartedAsync(config.McpUri, config.ToolProfile)
                            .ConfigureAwait(true);
                        await AutomationMcpServerManager.SetToolProfileAsync(
                            config.McpUri, config.ToolProfile,
                            fullPermissionEnabled && string.Equals(
                                config.ToolProfile, "Editor", StringComparison.Ordinal)).ConfigureAwait(true);
                    }
                    else
                    {
                        await AutomationMcpServerManager.SetToolProfileAsync(
                            config.McpUri, config.ToolProfile,
                            fullPermissionEnabled && string.Equals(
                                config.ToolProfile, "Editor", StringComparison.Ordinal))
                            .ConfigureAwait(true);
                    }

                    // 仅切换工具模式时保留当前 Goose 会话，通过 ACP 会话扩展接口强制重新读取 tools/list。
                    if (profileChanged && !uriChanged && !requiresGooseProcessRestart && activeClient != null)
                    {
                        sessionToolsReloaded = await activeClient.ReloadAutomationExtensionAsync(config.McpUri, CancellationToken.None)
                            .ConfigureAwait(true);
                    }
                }
                catch (Exception ex)
                {
                    if (oldConfig != null)
                    {
                        try
                        {
                            await AutomationMcpServerManager.SetToolProfileAsync(oldConfig.McpUri, oldConfig.ToolProfile)
                                .ConfigureAwait(true);
                            if (!uriChanged && activeClient != null)
                            {
                                await activeClient.ReloadAutomationExtensionAsync(oldConfig.McpUri, CancellationToken.None)
                                    .ConfigureAwait(true);
                            }
                        }
                        catch
                        {
                            // 保留原始切换异常；当前会话仍保留，用户可修复 Goose 版本或 MCP 状态后重试。
                        }
                        toolProfile = oldConfig.ToolProfile;
                        autoApproveMode = oldConfig.AutoApproveMode;
                    }
                    ShowWebToast("MCP模式切换失败:" + ex.Message);
                    PushWebAppState();
                    return;
                }
            }
            if (!GooseConfigStorage.TrySave(config, out error))
            {
                if (oldConfig != null && profileChanged && !uriChanged)
                {
                    try
                    {
                        await AutomationMcpServerManager.SetToolProfileAsync(oldConfig.McpUri, oldConfig.ToolProfile)
                            .ConfigureAwait(true);
                        if (activeClient != null)
                        {
                            await activeClient.ReloadAutomationExtensionAsync(oldConfig.McpUri, CancellationToken.None)
                                .ConfigureAwait(true);
                        }
                    }
                    catch
                    {
                    }
                    toolProfile = oldConfig.ToolProfile;
                    autoApproveMode = oldConfig.AutoApproveMode;
                }
                ShowWebToast("保存失败：" + error);
                PushWebAppState();
                return;
            }
            if (requiresGooseProcessRestart)
            {
                DisposeGooseClient();
            }
            LoadConfig();
            PushWebAppState();
            if (closeConfigAfterSave)
            {
                EnqueueScript("closeConfig();");
            }
            ShowWebToast(sessionToolsReloaded
                ? "工具模式切换成功，当前对话已保留。"
                : successMessage ?? "配置保存成功。");
        }

        private async Task SetFullPermissionToolsAsync(bool enabled)
        {
            if (!string.Equals(toolProfile, "Editor", StringComparison.Ordinal))
            {
                fullPermissionEnabled = false;
                ShowWebToast("请先切换到编辑模式。完全权限未开启。");
                PushWebAppState();
                return;
            }
            if (fullPermissionEnabled == enabled)
            {
                PushWebAppState();
                return;
            }

            bool previous = fullPermissionEnabled;
            GooseAcpClient activeClient = gooseClient;
            try
            {
                if (Workspace.Main?.McpServerManager == null)
                {
                    throw new InvalidOperationException("MCP Server管理器未初始化");
                }
                string mcpUri = txtMcpUri.Text.Trim();
                await AutomationMcpServerManager.SetToolProfileAsync(mcpUri, toolProfile, enabled)
                    .ConfigureAwait(true);
                bool reloaded = activeClient != null
                    && await activeClient.ReloadAutomationExtensionAsync(mcpUri, CancellationToken.None)
                        .ConfigureAwait(true);
                fullPermissionEnabled = enabled;
                ShowWebToast(enabled
                    ? reloaded ? "完全权限已开启，当前对话已保留。" : "完全权限已开启。"
                    : reloaded ? "完全权限已关闭，当前对话已保留。" : "完全权限已关闭。");
            }
            catch (Exception ex)
            {
                try
                {
                    await AutomationMcpServerManager.SetToolProfileAsync(
                        txtMcpUri.Text.Trim(), toolProfile, previous).ConfigureAwait(true);
                    if (activeClient != null)
                    {
                        await activeClient.ReloadAutomationExtensionAsync(
                            txtMcpUri.Text.Trim(), CancellationToken.None).ConfigureAwait(true);
                    }
                }
                catch
                {
                }
                fullPermissionEnabled = previous;
                ShowWebToast("完全权限切换失败：" + ex.Message);
            }
            PushWebAppState();
        }

        private async Task CheckWebConfigAsync()
        {
            if (!TryBuildConfig(out GooseConfig config, out string error))
            {
                ShowWebToast("配置无效：" + error);
                PushWebAppState();
                return;
            }

            ShowWebToast("正在检查 AI 运行组件...");
            string result = await Task.Run(() => CheckGooseCore(Workspace.Runtime, config)).ConfigureAwait(true);
            AppendConversation("系统", result, UiPalette.TextPrimary);
            ShowWebToast("检查完成，结果已写入对话。");
            PushWebAppState();
        }

        private void ShowWebToast(string text)
        {
            if (!webDocumentReady)
            {
                return;
            }
            EnqueueScript("if(window.showToast){window.showToast(" + JsonConvert.SerializeObject(text ?? string.Empty) + ");}");
        }

        private bool TryBuildConfig(out GooseConfig config, out string error)
        {
            config = new GooseConfig
            {
                GooseExecutablePath = txtGooseExecutable.Text.Trim(),
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                McpUri = txtMcpUri.Text.Trim(),
                SessionName = txtSessionName.Text.Trim(),
                Provider = NormalizeGooseOverride(cboProvider.Text),
                Model = NormalizeGooseOverride(cboModel.Text),
                ModelServiceId = modelServiceId ?? string.Empty,
                ModelServices = GooseConfigStorage.CloneModelServices(modelServices),
                Temperature = (double)nudTemperature.Value,
                MaxTurns = (int)nudMaxTurns.Value,
                MaxOutputTokens = (int)nudMaxOutputTokens.Value,
                ToolProfile = toolProfile,
                AutoApproveMode = autoApproveMode
            };

            if (TryResolveGooseExecutablePath(config.GooseExecutablePath, out string resolvedGoosePath))
            {
                config.GooseExecutablePath = resolvedGoosePath;
                txtGooseExecutable.Text = resolvedGoosePath;
            }

            if (string.IsNullOrWhiteSpace(config.ModelServiceId)
                && !string.IsNullOrWhiteSpace(config.Provider)
                && !AiProviderSecretStorage.TryGetEnvironmentVariableName(config.Provider, out _))
            {
                error = "当前 Provider 未配置严格的 API Key 环境变量映射：" + config.Provider;
                return false;
            }

            return GooseConfigStorage.TryValidate(config, out error);
        }

        private static bool SelectedModelServiceEqual(GooseConfig left, GooseConfig right)
        {
            string leftJson = JsonConvert.SerializeObject(GooseConfigStorage.FindModelService(left));
            string rightJson = JsonConvert.SerializeObject(GooseConfigStorage.FindModelService(right));
            return string.Equals(leftJson, rightJson, StringComparison.Ordinal);
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            if (IsActiveTaskRunning || standardTestRunning)
            {
                StopCurrentPrompt();
                return;
            }

            if (conversationCoordinator.TaskHomeVisible
                || conversationCoordinator.ActiveConversation == null)
            {
                StartNewConversation();
            }

            await SendPromptAsync(ActiveTaskRuntime, txtPrompt.Text, pendingFileAttachments.ToList(), true).ConfigureAwait(true);
        }

        private async Task<bool> SendPromptAsync(
            AiTaskRuntime runtime,
            string enteredPrompt,
            List<GooseFileAttachment> fileAttachments,
            bool restoreComposerOnFailure)
        {
            fileAttachments = fileAttachments ?? new List<GooseFileAttachment>();
            if (string.IsNullOrWhiteSpace(enteredPrompt) && fileAttachments.Count == 0)
            {
                return false;
            }
            if (runtime == null || runtime.Running)
            {
                return false;
            }
            if (!TryBuildConfig(out GooseConfig config, out string error))
            {
                ShowWebToast("配置无效：" + error);
                PushWebAppState();
                return false;
            }
            if (!GooseRuntimeEnvironment.TryValidate(config.GooseExecutablePath, out error))
            {
                ShowWebToast(error);
                PushWebAppState();
                return false;
            }

            GooseFileAttachment invalidAttachment = fileAttachments.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.Error)
                || (item.IsImage && IsTextOnlyConfiguration(config)));
            if (invalidAttachment != null)
            {
                string attachmentError = invalidAttachment.Error;
                if (string.IsNullOrWhiteSpace(attachmentError))
                {
                    attachmentError = "当前 Provider/Model 仅支持文本，不能分析图片。请移除图片或切换支持视觉的模型。";
                }
                ShowWebToast(invalidAttachment.FileName + "：" + attachmentError);
                PushWebAppState();
                return false;
            }

            if (!conversationCoordinator.TryBeginTask(
                runtime,
                enteredPrompt,
                fileAttachments,
                out string preparedPrompt,
                out string conversationText,
                out IReadOnlyList<GooseFileAttachment> preparedAttachments,
                out error))
            {
                ShowWebToast(error);
                return false;
            }
            // 新建或切换任务时 WebView 正在重载，导航完成后会从任务历史统一渲染；
            // 此时不能提前追加气泡，否则同一条用户消息会显示两次。
            if (webDocumentReady)
            {
                AppendConversation("用户", conversationText, UiPalette.BrandPressed);
            }
            SaveConversationHistory();
            // 生成期间持续强制跟随消息区与思考框底部，不允许增量脚本或用户上滑打断信息流。
            EnqueueScript("if(window.startForcedStreamScroll){startForcedStreamScroll();}");
            sending = true;
            latestAssistantSegmentText = null;
            latestAssistantSegmentDivId = null;
            flowVisualizationCallIds.Clear();
            flowVisualizationProcesses.RemoveAll();
            promptStartedAt = DateTime.Now;
            ApplyPermissions();

            try
            {
                AiTaskExecutionResult executionResult = await conversationCoordinator.ExecuteTaskAsync(
                    runtime,
                    preparedPrompt,
                    preparedAttachments,
                    () => EnsureTaskClient(runtime, config)).ConfigureAwait(true);
                if (executionResult.Status == AiTaskExecutionStatus.Cancelled)
                {
                    if (!conversationCoordinator.TaskHomeVisible
                        && ReferenceEquals(conversationCoordinator.ActiveConversation, runtime.Conversation))
                    {
                        AppendConversation("系统", "已停止本轮生成。", UiPalette.Warning);
                        if (restoreComposerOnFailure) RestoreComposerAfterFailedSend(enteredPrompt);
                    }
                    return false;
                }
                if (executionResult.Status == AiTaskExecutionStatus.Failed)
                {
                    if (!conversationCoordinator.TaskHomeVisible
                        && ReferenceEquals(conversationCoordinator.ActiveConversation, runtime.Conversation))
                    {
                        AppendConversation("错误", executionResult.Error, UiPalette.Danger);
                        if (restoreComposerOnFailure) RestoreComposerAfterFailedSend(enteredPrompt);
                    }
                    return false;
                }

                HashSet<string> sentAttachmentIds = new HashSet<string>(
                    preparedAttachments.Select(item => item.Id),
                    StringComparer.Ordinal);
                pendingFileAttachments.RemoveAll(item => sentAttachmentIds.Contains(item.Id));
                foreach (string attachmentId in sentAttachmentIds)
                {
                    fileAttachmentPreviews.Remove(attachmentId);
                }
                bool isActive = !conversationCoordinator.TaskHomeVisible
                    && ReferenceEquals(conversationCoordinator.ActiveConversation, runtime.Conversation);
                if (isActive)
                {
                    txtPrompt.Clear();
                    FinishStreaming();
                    FinishThoughtStreaming();
                }
                string visualizationJson = flowVisualizationProcesses.Count == 0
                    ? null
                    : flowVisualizationProcesses.ToString(Formatting.None);
                string assistantText = isActive
                    ? PromoteLatestAssistantSegment(executionResult.Client.LastAssistantResponse, visualizationJson)
                    : ExtractLatestAssistantText(
                        executionResult.Events,
                        executionResult.Client.LastAssistantResponse);
                conversationCoordinator.CompleteTask(runtime, assistantText, visualizationJson);
                SaveConversationHistory();
                return true;
            }
            catch (Exception ex)
            {
                if (!conversationCoordinator.TaskHomeVisible
                    && ReferenceEquals(conversationCoordinator.ActiveConversation, runtime.Conversation))
                {
                    AppendConversation("错误", ex.Message, UiPalette.Danger);
                    if (restoreComposerOnFailure)
                    {
                        RestoreComposerAfterFailedSend(enteredPrompt);
                    }
                }
                return false;
            }
            finally
            {
                if (!conversationCoordinator.TaskHomeVisible
                    && ReferenceEquals(conversationCoordinator.ActiveConversation, runtime.Conversation))
                {
                    FinishStreaming();
                    FinishThoughtStreaming();
                    CollapseThinkingBox();
                    EnqueueScript("if(window.stopForcedStreamScroll){stopForcedStreamScroll();}");
                    sending = false;
                }
                SaveConversationHistory();
                ApplyPermissions();
            }
        }

        private void SaveModelService(JObject value)
        {
            if (HasRunningTasks)
            {
                ShowWebToast("AI 任务运行期间不能修改模型服务。");
                PushWebAppState();
                return;
            }
            if (value == null)
            {
                ShowWebToast("自定义模型服务配置为空。");
                return;
            }
            string id = value["id"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("D");
            int? contextLimit = value["contextLimit"]?.Value<int?>();
            var service = new AiModelServiceConfig
            {
                Id = id.Trim(),
                Name = value["name"]?.Value<string>()?.Trim(),
                BaseUrl = value["baseUrl"]?.Value<string>()?.Trim(),
                Model = value["model"]?.Value<string>()?.Trim(),
                ContextLimit = contextLimit.HasValue && contextLimit.Value > 0 ? contextLimit : null,
                SupportsVision = value["supportsVision"]?.Value<bool?>() ?? false,
                RequiresApiKey = value["requiresApiKey"]?.Value<bool?>() ?? false
            };
            if (!GooseConfigStorage.ValidateModelService(service, out string error))
            {
                ShowWebToast(error);
                PushWebAppState();
                return;
            }
            if (modelServices.Any(item => !string.Equals(item.Id, service.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Name, service.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ShowWebToast("自定义模型服务名称已存在：" + service.Name);
                PushWebAppState();
                return;
            }
            string apiKey = value["apiKey"]?.Value<string>();
            string serviceSecretKey = AiProviderSecretStorage.GetModelServiceSecretKey(service.Id);
            if (service.RequiresApiKey && string.IsNullOrWhiteSpace(apiKey)
                && !AiProviderSecretStorage.HasSecret(serviceSecretKey))
            {
                ShowWebToast("该模型服务要求鉴权，请填写 API Key。");
                PushWebAppState();
                return;
            }
            if (!string.IsNullOrWhiteSpace(apiKey)
                && !AiProviderSecretStorage.TrySaveSecret(
                    serviceSecretKey, apiKey, out error))
            {
                ShowWebToast(error);
                PushWebAppState();
                return;
            }
            int index = modelServices.FindIndex(item =>
                string.Equals(item.Id, service.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) modelServices[index] = service;
            else modelServices.Add(service);
            modelServiceId = service.Id;
            if (!string.IsNullOrWhiteSpace(apiKey)) DisposeGooseClient();
            SaveWebConfig("自定义模型服务已保存并选中。", false);
        }

        private void DeleteModelService(string id)
        {
            if (HasRunningTasks)
            {
                ShowWebToast("AI 任务运行期间不能删除模型服务。");
                PushWebAppState();
                return;
            }
            AiModelServiceConfig service = modelServices.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (service == null)
            {
                ShowWebToast("自定义模型服务不存在。");
                PushWebAppState();
                return;
            }
            modelServices.Remove(service);
            AiProviderSecretStorage.TryDeleteSecret(
                AiProviderSecretStorage.GetModelServiceSecretKey(service.Id), out _);
            if (string.Equals(modelServiceId, service.Id, StringComparison.OrdinalIgnoreCase))
            {
                modelServiceId = string.Empty;
            }
            SaveWebConfig("自定义模型服务已删除。", false);
        }

        private void ClearModelServiceApiKey(string id)
        {
            if (HasRunningTasks)
            {
                ShowWebToast("AI 任务运行期间不能清除模型服务密钥。");
                PushWebAppState();
                return;
            }
            AiModelServiceConfig service = modelServices.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (service == null)
            {
                ShowWebToast("请先选择自定义模型服务。");
            }
            else if (!AiProviderSecretStorage.TryDeleteSecret(
                AiProviderSecretStorage.GetModelServiceSecretKey(service.Id), out string error))
            {
                ShowWebToast(error);
            }
            else
            {
                DisposeGooseClient();
                ShowWebToast("已清除该模型服务的本机 API Key。");
            }
            PushWebAppState();
        }

        private void StopCurrentPrompt()
        {
            if (standardTestRunning)
            {
                standardTestStopRequested = true;
            }
            AiTaskRuntime runtime = ActiveTaskRuntime;
            conversationCoordinator.Cancel(runtime);
        }

        private async Task RunStandardTestsAsync(IEnumerable<string> scenarioIds, bool separateConversations)
        {
            if (sending || standardTestRunning)
            {
                return;
            }

            List<AiStandardTestScenario> selectedScenarios = AiStandardTestSuite.Select(scenarioIds);
            if (selectedScenarios.Count == 0)
            {
                ShowWebToast("没有选择测试场景。");
                return;
            }
            if (!string.Equals(toolProfile, "Editor", StringComparison.Ordinal))
            {
                ShowWebToast("这些标准测试包含配置变更，请先切换到编辑模式。");
                return;
            }

            standardTestRunning = true;
            standardTestStopRequested = false;
            ApplyPermissions();
            int completedTurns = 0;
            int totalTurns = selectedScenarios.Sum(item => item.Prompts.Count);
            int passedScenarios = 0;
            int failedScenarios = 0;
            try
            {
                for (int scenarioIndex = 0; scenarioIndex < selectedScenarios.Count; scenarioIndex++)
                {
                    if (standardTestStopRequested)
                    {
                        break;
                    }

                    AiStandardTestScenario scenario = selectedScenarios[scenarioIndex];
                    if (separateConversations)
                    {
                        StartNewConversation();
                    }
                    AppendConversation("系统", "标准测试：" + scenario.Name, UiPalette.TextSecondary);

                    if (!AiStandardTestSuite.Prepare(
                        Workspace.Runtime,
                        scenario, out AiStandardTestFixtureState fixture, out string prepareError))
                    {
                        failedScenarios++;
                        AiAnalysisLogger.Write(new JObject
                        {
                            ["event"] = "standard_test.preparation_failed",
                            ["conversationId"] = conversationCoordinator.ActiveConversation?.Id ?? string.Empty,
                            ["scenarioId"] = scenario.Id,
                            ["message"] = prepareError ?? string.Empty
                        });
                        AppendConversation("系统",
                            "### 未执行 · " + scenario.Name + "\n\n- ✗ 测试准备失败：" + prepareError,
                            UiPalette.Danger);
                        continue;
                    }
                    AppendConversation("系统",
                        string.IsNullOrWhiteSpace(fixture.SelectedProcessName)
                            ? "测试环境已清理：仅移除名称以“标准测试_”开头的测试对象。"
                            : "测试夹具已准备并选中流程：" + fixture.SelectedProcessName,
                        UiPalette.TextSecondary);

                    bool scenarioCompleted = true;
                    foreach (string prompt in scenario.Prompts)
                    {
                        if (standardTestStopRequested)
                        {
                            break;
                        }
                        bool completed = await SendPromptAsync(
                            ActiveTaskRuntime,
                            prompt,
                            new List<GooseFileAttachment>(),
                            false).ConfigureAwait(true);
                        if (!completed)
                        {
                            scenarioCompleted = false;
                            standardTestStopRequested = true;
                            break;
                        }
                        completedTurns++;
                    }

                    if (scenarioCompleted)
                    {
                        AiStandardTestEvaluation evaluation = AiStandardTestSuite.Evaluate(
                            Workspace.Runtime, scenario, fixture);
                        if (evaluation.Passed) passedScenarios++;
                        else failedScenarios++;
                        AiAnalysisLogger.Write(new JObject
                        {
                            ["event"] = "standard_test.evaluated",
                            ["conversationId"] = conversationCoordinator.ActiveConversation?.Id ?? string.Empty,
                            ["scenarioId"] = scenario.Id,
                            ["passed"] = evaluation.Passed,
                            ["checks"] = new JArray(evaluation.Details)
                        });
                        AppendConversation("系统", evaluation.ToMarkdown(scenario.Name),
                            evaluation.Passed
                                ? UiPalette.Success
                                : UiPalette.Danger);
                    }

                    if (separateConversations && conversationCoordinator.ActiveConversation != null)
                    {
                        conversationCoordinator.ActiveConversation.Title = "标准测试 · " + scenario.Name;
                        conversationCoordinator.ActiveConversation.UpdatedAt = DateTime.Now;
                        SaveConversationHistory();
                        PushWebAppState();
                    }
                }
            }
            finally
            {
                bool stopped = standardTestStopRequested;
                standardTestRunning = false;
                standardTestStopRequested = false;
                ApplyPermissions();
                ShowWebToast(stopped
                    ? $"标准测试已停止，完成 {completedTurns}/{totalTurns} 轮；通过 {passedScenarios}，未通过 {failedScenarios}。"
                    : $"标准测试已完成，共 {completedTurns} 轮；通过 {passedScenarios}，未通过 {failedScenarios}。");
            }
        }

        private void BtnResetSession_Click(object sender, EventArgs e)
        {
            StartNewConversation();
        }

        private void RestoreComposerAfterFailedSend(string prompt)
        {
            txtPrompt.Text = prompt ?? string.Empty;
            EnqueueScript("var input=document.getElementById('promptInput');if(input){input.value="
                + JsonConvert.SerializeObject(prompt ?? string.Empty)
                + ";if(window.autoGrowPrompt){window.autoGrowPrompt();}};");
            PushWebAppState();
        }

        public bool PreparePrompt(string prompt)
        {
            if (standardTestRunning || conversationCoordinator.HasRunningTasks)
            {
                ShowWebToast("当前 AI 任务仍在运行，未覆盖输入框内容。");
                return false;
            }
            RestoreComposerAfterFailedSend(prompt ?? string.Empty);
            return true;
        }

        private void LoadConversationHistory()
        {
            if (!conversationCoordinator.TryLoad(out string error))
            {
                MessageBox.Show("AI 会话历史读取失败，已用空历史继续启动：" + error,
                    "EW-AI 会话历史", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private JObject BuildAttachmentWebState(GooseFileAttachment attachment)
        {
            string error = attachment.Error;
            TryBuildConfig(out GooseConfig currentConfig, out _);
            if (string.IsNullOrWhiteSpace(error) && attachment.IsImage && IsTextOnlyConfiguration(currentConfig))
            {
                AiModelServiceConfig service = GooseConfigStorage.FindModelService(currentConfig);
                string modelLabel = service == null
                    ? cboProvider.Text + "/" + cboModel.Text
                    : service.Name + "/" + service.Model;
                error = $"当前模型 {modelLabel} 只支持文本，不能分析图片。";
            }
            fileAttachmentPreviews.TryGetValue(attachment.Id, out string preview);
            return new JObject
            {
                ["id"] = attachment.Id,
                ["name"] = attachment.FileName,
                ["typeLabel"] = attachment.TypeLabel,
                ["isImage"] = attachment.IsImage,
                ["preview"] = preview ?? string.Empty,
                ["error"] = error ?? string.Empty
            };
        }

        private static bool IsTextOnlyConfiguration(GooseConfig config)
        {
            if (config == null) return false;
            AiModelServiceConfig service = GooseConfigStorage.FindModelService(config);
            return service != null
                ? !service.SupportsVision
                : GooseAcpClient.IsKnownTextOnlyImageConfiguration(config.Provider, config.Model);
        }

        private void StartNewConversation()
        {
            conversationCoordinator.StartNew();
            SaveConversationHistory();
            ResetConversationViewState();
        }

        private void DeleteCurrentConversation()
        {
            if (conversationCoordinator.ActiveConversation == null
                || ActiveTaskRuntime?.Running == true)
            {
                return;
            }
            if (!conversationCoordinator.TryDeleteActive(out string error))
            {
                ShowWebToast(error);
                return;
            }
            ResetConversationViewState();
            SaveConversationHistory();
            ShowWebToast("当前对话已删除。");
        }

        private void SwitchConversation(string conversationId)
        {
            if (!conversationCoordinator.TrySwitch(
                conversationId, out AiTaskRuntime runtime, out string error))
            {
                ShowWebToast(error);
                PushWebAppState();
                return;
            }
            gooseClient = runtime.Client;
            sending = runtime.Running;
            ResetConversationViewState();
        }

        private void ShowTaskHome()
        {
            conversationCoordinator.ShowHome();
            gooseClient = null;
            sending = false;
            ResetConversationViewState();
        }

        private void ResetConversationViewState()
        {
            pendingFileAttachments.Clear();
            fileAttachmentPreviews.Clear();
            previewCoordinator.Reset();
            streamingAssistant = false;
            streamingMarkdown.Clear();
            streamingDivId = null;
            streamingSegmentIndex = 0;
            latestAssistantSegmentText = null;
            latestAssistantSegmentDivId = null;
            flowVisualizationCallIds.Clear();
            flowVisualizationProcesses.RemoveAll();
            promptStartedAt = default(DateTime);
            streamingThought = false;
            streamingThoughtMarkdown.Clear();
            streamingThoughtDivId = null;
            streamingThoughtSegmentIndex = 0;
            currentThinkingBoxId = null;
            pendingScriptTask = Task.CompletedTask;
            ResetConversationHtml();
        }

        private void SaveConversationHistory()
        {
            if (!conversationCoordinator.TrySave(out string error))
            {
                ShowWebToast("会话历史保存失败：" + error);
            }
            PushWebAppState();
        }

        private void RenderActiveConversation()
        {
            if (conversationCoordinator.ActiveConversation == null)
            {
                return;
            }
            foreach (AiConversationMessage message in conversationCoordinator.ActiveConversation.Messages)
            {
                AppendConversation(message.Role == "user" ? "用户" : "EW-AI", message.Text,
                    message.Role == "user" ? UiPalette.BrandPressed : UiPalette.SuccessHover,
                    message.Time, message.VisualizationJson);
            }
        }

        private void RenderActiveTaskView()
        {
            if (!webDocumentReady || conversationCoordinator.TaskHomeVisible
                || conversationCoordinator.ActiveConversation == null)
            {
                return;
            }
            RenderActiveConversation();
            if (!conversationCoordinator.TaskRuntimes.TryGetValue(
                    conversationCoordinator.ActiveConversation.Id, out AiTaskRuntime runtime)
                || runtime.PendingEvents.Count == 0)
            {
                return;
            }
            replayingTaskEvents = true;
            try
            {
                foreach (GooseAcpEvent item in runtime.PendingEvents.ToList())
                {
                    GooseClient_EventReceived(item);
                }
            }
            finally
            {
                replayingTaskEvents = false;
            }
        }

        // 重置对话区：清空 #messages 容器内容（保留基础 HTML/CSS），等价于原 rtbConversation.Clear()。
        private void ResetConversationHtml()
        {
            if (webViewConversation == null || webViewConversation.CoreWebView2 == null)
            {
                return;
            }
            conversationViewGeneration++;
            webDocumentReady = false;
            webViewConversation.CoreWebView2.NavigateToString(BaseConversationHtml);
        }

        private GooseAcpClient EnsureTaskClient(AiTaskRuntime runtime, GooseConfig config)
        {
            GooseAcpClient client = conversationCoordinator.GetOrCreateClient(runtime, () =>
            {
                var created = new GooseAcpClient(Workspace.Runtime, config, runtime.RestoredContext);
                runtime.RestoredContext = null;
                created.EventReceived += item => TaskClient_EventReceived(runtime, item);
                created.PermissionRequestHandler = HandlePermissionRequest;
                return created;
            });
            if (ReferenceEquals(ActiveTaskRuntime, runtime))
            {
                gooseClient = client;
            }
            return client;
        }

        private void TaskClient_EventReceived(AiTaskRuntime runtime, GooseAcpEvent item)
        {
            if (IsDisposed)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<AiTaskRuntime, GooseAcpEvent>(TaskClient_EventReceived), runtime, item);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            runtime.PendingEvents.Add(item);
            bool isActive = !conversationCoordinator.TaskHomeVisible
                && ReferenceEquals(conversationCoordinator.ActiveConversation, runtime.Conversation);
            // WebView 重载期间只缓存事件，导航完成后由 RenderActiveTaskView 按历史消息和事件顺序统一回放。
            // 直接渲染会让后台到达的思维链脚本先于用户气泡落入新文档，造成消息顺序错乱。
            if (isActive && webDocumentReady && !replayingTaskEvents)
            {
                GooseClient_EventReceived(item);
            }
            else if (string.Equals(item.Kind, "tool_result", StringComparison.Ordinal))
            {
                TryPromptPreviewConfirmation(item.Raw);
            }
            PushWebAppState();
        }

        private static string ExtractLatestAssistantText(IEnumerable<GooseAcpEvent> events, string fallback)
        {
            var current = new StringBuilder();
            string latest = null;
            foreach (GooseAcpEvent item in events ?? Enumerable.Empty<GooseAcpEvent>())
            {
                if (string.Equals(item.Kind, "assistant_chunk", StringComparison.Ordinal))
                {
                    current.Append(item.Text);
                    continue;
                }
                if (current.Length > 0)
                {
                    latest = current.ToString();
                    current.Clear();
                }
                if (string.Equals(item.Kind, "assistant", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(item.Text))
                {
                    latest = item.Text;
                }
            }
            if (current.Length > 0)
            {
                latest = current.ToString();
            }
            return string.IsNullOrWhiteSpace(latest) ? fallback : latest;
        }



        private JObject HandlePermissionRequest(JObject request)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return BuildPermissionCancelled();
            }
            if (InvokeRequired)
            {
                JObject response = null;
                Exception dispatchError = null;
                using (var completed = new ManualResetEventSlim(false))
                {
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            try
                            {
                                response = HandlePermissionRequest(request);
                            }
                            catch (Exception ex)
                            {
                                dispatchError = ex;
                            }
                            finally
                            {
                                completed.Set();
                            }
                        }));
                    }
                    catch
                    {
                        return BuildPermissionCancelled();
                    }
                    if (!completed.Wait(TimeSpan.FromMinutes(2)) || IsDisposed || Disposing)
                    {
                        return BuildPermissionCancelled();
                    }
                }
                if (dispatchError != null)
                {
                    throw dispatchError;
                }
                return response ?? BuildPermissionCancelled();
            }

            string title = request["toolCall"]?["title"]?.Value<string>()
                ?? request["toolCall"]?["name"]?.Value<string>()
                ?? "EW-AI 权限请求";

            string toolName = request["toolCall"]?["name"]?.Value<string>() ?? "";
            JObject arguments = request["toolCall"]?["arguments"] as JObject;
            if (IsDeveloperWriteOutsideHmi(toolName, arguments, out string rejectedPath))
            {
                AppendConversation(
                    "系统",
                    "⛔ 已拒绝修改 Hmi 目录之外的文件：" + rejectedPath,
                    UiPalette.Danger);
                return BuildPermissionCancelled();
            }

            if (autoApproveMode)
            {
                // 自动批准模式：把工具调用信息显示到聊天区，让用户看到批准了什么
                AppendConversation("系统", "✅ 自动批准：" + title, UiPalette.SuccessHover);

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

            DialogResult dialogResult = ShowPermissionApprovalDialog(toolName, title, arguments);
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

        private static bool IsDeveloperWriteOutsideHmi(
            string toolName,
            JObject arguments,
            out string rejectedPath)
        {
            rejectedPath = null;
            if (!string.Equals(toolName, "write", StringComparison.Ordinal)
                && !string.Equals(toolName, "edit", StringComparison.Ordinal))
            {
                return false;
            }

            string path = arguments?["path"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(path))
            {
                rejectedPath = "(路径为空)";
                return true;
            }

            string hmiDirectory = ResolveHmiSourceDirectory();
            string fullPath = Path.GetFullPath(
                Path.IsPathRooted(path) ? path : Path.Combine(hmiDirectory, path));
            string boundary = hmiDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            {
                rejectedPath = fullPath;
                return true;
            }
            return false;
        }

        private static string ResolveHmiSourceDirectory()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string projectFile = Path.Combine(directory.FullName, "Automation.csproj");
                string hmiDirectory = Path.Combine(directory.FullName, "Hmi");
                if (File.Exists(projectFile) && Directory.Exists(hmiDirectory))
                {
                    return Path.GetFullPath(hmiDirectory);
                }
                directory = directory.Parent;
            }
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Hmi"));
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
                try
                {
                    BeginInvoke(new Action<GooseAcpEvent>(GooseClient_EventReceived), item);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            // 流式 token：在同一行追加（打字机效果），不写调试区，不加事件头。
            if (string.Equals(item.Kind, "assistant_chunk", StringComparison.Ordinal))
            {
                FinishThoughtStreaming();
                AppendStreamingText(item.Text);
                return;
            }

            if (string.Equals(item.Kind, "assistant_thought", StringComparison.Ordinal))
            {
                FinishStreaming();
                AppendThoughtText(item.Text);
                return;
            }

            // 非流式事件到达时结束当前流式片段；紧随其后的 assistant 事件与已显示内容重复。
            bool justFinishedStreaming = false;
            if (streamingAssistant)
            {
                FinishStreaming();
                justFinishedStreaming = true;
            }
            FinishThoughtStreaming();
            CaptureFlowVisualizationEvent(item);

            if (string.Equals(item.Kind, "assistant", StringComparison.Ordinal))
            {
                // 流式刚结束，内容已在流式 div 中完整渲染，跳过重复的 assistant 事件。
                if (!justFinishedStreaming)
                {
                    latestAssistantSegmentText = item.Text;
                    latestAssistantSegmentDivId = null;
                }
            }
            else if (string.Equals(item.Kind, "tool_call", StringComparison.Ordinal))
            {
                DemoteLatestAssistantSegmentToThinking();
                AppendToolEntry("call", item.Text, item.Raw);
            }
            else if (string.Equals(item.Kind, "tool_result", StringComparison.Ordinal))
            {
                AppendToolEntry("result", item.Text, item.Raw);
            }
            else if (string.Equals(item.Kind, "tool", StringComparison.Ordinal))
            {
                AppendConversation("工具", item.Text, UiPalette.WarningHover);
            }
            else if (string.Equals(item.Kind, "error", StringComparison.Ordinal)
                || string.Equals(item.Kind, "stderr", StringComparison.Ordinal)
                || string.Equals(item.Kind, "exit", StringComparison.Ordinal))
            {
                AppendConversation("系统", item.Text, UiPalette.Danger);
            }

            // ChangeSet 预演确认：tool_result 才包含 Bridge 生成的 previewId，
            // tool_call 事件只有输入参数。
            if (!replayingTaskEvents && string.Equals(item.Kind, "tool_result", StringComparison.Ordinal))
            {
                TryPromptPreviewConfirmation(item.Raw);
            }
        }

        private async void TryPromptPreviewConfirmation(JObject raw)
        {
            AiPreviewObservation observation = previewCoordinator.Observe(raw, autoApproveMode);
            if (observation.Kind == AiPreviewObservationKind.None
                || observation.Kind == AiPreviewObservationKind.AlreadyPresented)
            {
                return;
            }

            if (observation.Kind == AiPreviewObservationKind.Applied)
            {
                gooseClient?.LogFrontendAnalysisEvent("preview.applied", new JObject
                {
                    ["previewId"] = observation.PreviewId,
                    ["resultType"] = observation.ResultType
                });
                return;
            }
            if (observation.Kind == AiPreviewObservationKind.Rejected)
            {
                gooseClient?.LogFrontendAnalysisEvent("preview.decided", new JObject
                {
                    ["previewId"] = observation.PreviewId,
                    ["decision"] = "rejected",
                    ["source"] = "bridge"
                });
                return;
            }
            if (observation.Kind == AiPreviewObservationKind.Confirmed)
            {
                gooseClient?.LogFrontendAnalysisEvent("preview.decided", new JObject
                {
                    ["previewId"] = observation.PreviewId,
                    ["decision"] = "confirmed",
                    ["source"] = autoApproveMode ? "auto_approve" : "bridge"
                });
                return;
            }

            gooseClient?.LogFrontendAnalysisEvent("preview.created", new JObject
            {
                ["previewId"] = observation.PreviewId,
                ["status"] = "awaiting_confirmation",
                ["autoApproveMode"] = autoApproveMode
            });

            if (observation.Kind == AiPreviewObservationKind.AutoApprovalMismatch)
            {
                gooseClient?.LogFrontendAnalysisEvent("preview.state_mismatch", new JObject
                {
                    ["previewId"] = observation.PreviewId,
                    ["message"] = "自动批准模式下返回了未确认预演。"
                });
                return;
            }

            gooseClient?.LogFrontendAnalysisEvent("preview.presented", new JObject
            {
                ["previewId"] = observation.PreviewId,
                ["changeCount"] = observation.Changes?.Count ?? 0,
                ["messageCount"] = observation.Messages?.Count ?? 0
            });
            Stopwatch confirmationStopwatch = Stopwatch.StartNew();
            DialogResult result = ShowPreviewApprovalDialog(
                observation.PreviewId,
                observation.Changes,
                observation.Messages);
            confirmationStopwatch.Stop();
            gooseClient?.LogFrontendAnalysisEvent("preview.decided", new JObject
            {
                ["previewId"] = observation.PreviewId,
                ["decision"] = result == DialogResult.Yes ? "confirmed" : "rejected",
                ["source"] = "user",
                ["waitMs"] = confirmationStopwatch.ElapsedMilliseconds
            });
            try
            {
                if (result == DialogResult.Yes)
                {
                    await previewClient.ConfirmAsync(observation.PreviewId).ConfigureAwait(true);
                }
                else
                {
                    await previewClient.RejectAsync(observation.PreviewId).ConfigureAwait(true);
                    AppendConversation("系统", "已取消本次变更。", UiPalette.Warning);
                }
            }
            catch (Exception ex)
            {
                string action = result == DialogResult.Yes ? "确认" : "取消";
                AppendConversation("错误", action + "预演失败：" + ex.Message, UiPalette.Danger);
            }
        }

        private static string CheckGooseCore(PlatformRuntime runtime, GooseConfig config)
        {
            StringBuilder builder = new StringBuilder();
            string gooseExecutable = ResolveGooseExecutablePath(config.GooseExecutablePath);
            builder.AppendLine($"EW-AI 可执行文件：{gooseExecutable}");
            builder.AppendLine(RunProcessCapture(gooseExecutable, "--version", AppDomain.CurrentDomain.BaseDirectory, 10000));
            builder.AppendLine(RunProcessCapture(gooseExecutable, "info -v", AppDomain.CurrentDomain.BaseDirectory, 15000));
            try
            {
                GooseConfig resolvedConfig = new GooseConfig
                {
                    GooseExecutablePath = gooseExecutable,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    McpUri = config.McpUri,
                    SessionName = config.SessionName,
                    Provider = config.Provider,
                    Model = config.Model,
                    ModelServiceId = config.ModelServiceId,
                    ModelServices = GooseConfigStorage.CloneModelServices(config.ModelServices),
                    Temperature = config.Temperature,
                    MaxTurns = config.MaxTurns,
                    MaxOutputTokens = config.MaxOutputTokens,
                    ToolProfile = config.ToolProfile,
                    AutoApproveMode = config.AutoApproveMode
                };
                using (GooseAcpClient client = new GooseAcpClient(runtime, resolvedConfig))
                using (CancellationTokenSource cts = new CancellationTokenSource(30000))
                {
                    client.InitializeAsync(cts.Token).GetAwaiter().GetResult();
                }
                builder.AppendLine("EW-AI ACP 初始化：成功");
            }
            catch (Exception ex)
            {
                builder.AppendLine("EW-AI ACP 初始化：失败");
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
            if (File.Exists(GooseRuntimeEnvironment.MachineGooseExecutablePath))
            {
                resolvedPath = GooseRuntimeEnvironment.MachineGooseExecutablePath;
                return true;
            }
            return false;
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
            conversationCoordinator.DisposeClients();
            gooseClient = null;
        }
    }
}
