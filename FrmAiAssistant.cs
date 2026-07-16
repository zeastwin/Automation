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
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public sealed class FrmAiAssistant : Form
    {
        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel();
        private WebView2 webViewConversation;
        private bool webViewClosing;
        private bool webViewEventsAttached;
        private bool webDocumentReady;
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
        private readonly HashSet<string> promptedPreviewIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object clientLock = new object();

        private sealed class AiTaskRuntime
        {
            public AiConversation Conversation { get; set; }
            public GooseAcpClient Client { get; set; }
            public CancellationTokenSource Cancellation { get; set; }
            public List<GooseAcpEvent> PendingEvents { get; } = new List<GooseAcpEvent>();
            public bool Running { get; set; }
            public string Status { get; set; } = "已完成";
            public string RestoredContext { get; set; }
        }

        private GooseAcpClient gooseClient;
        private CancellationTokenSource promptCts;
        private bool sending;
        private bool standardTestRunning;
        private bool standardTestStopRequested;
        private bool autoApproveMode = false;
        private bool fullPermissionEnabled;
        private const int MaxFileAttachmentCount = 4;
        private const long MaxFileAttachmentBytes = 10L * 1024L * 1024L;
        private readonly List<GooseFileAttachment> pendingFileAttachments = new List<GooseFileAttachment>();
        private readonly Dictionary<string, string> fileAttachmentPreviews = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<AiConversation> conversations = new List<AiConversation>();
        private readonly Dictionary<string, AiTaskRuntime> taskRuntimes =
            new Dictionary<string, AiTaskRuntime>(StringComparer.Ordinal);
        private AiConversation activeConversation;
        private string restoredConversationContext;
        private bool taskHomeVisible = true;
        private bool replayingTaskEvents;

        private bool HasRunningTasks => taskRuntimes.Values.Any(item => item.Running) || standardTestRunning;
        private AiTaskRuntime ActiveTaskRuntime => activeConversation != null
            && taskRuntimes.TryGetValue(activeConversation.Id, out AiTaskRuntime runtime) ? runtime : null;
        private bool IsActiveTaskRunning => !taskHomeVisible && ActiveTaskRuntime?.Running == true;

        // Bridge 服务在生成预演记录时读取此属性，若为 true 则直接标记预演为已确认，
        // 避免 TryPromptPreviewConfirmation 通过 HTTP 回调 Bridge 确认导致 UI 线程死锁。
        public bool IsAutoApproveMode => autoApproveMode;

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
                MessageBox.Show(ex.Message, "WebView2 初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // AI 助手整页 HTML：顶部工具、对话、输入框、配置弹层全部由 WebView2 承载。
        private static readonly string ChickAvatarDataUri = LoadChickAvatarDataUri();
        private static readonly string BaseConversationHtml = BuildBaseConversationHtml();

        private static string BuildBaseConversationHtml()
        {
            return BaseConversationHtmlTemplate.Replace("__CHICK_AVATAR__", ChickAvatarDataUri);
        }

        private static string LoadChickAvatarDataUri()
        {
            try
            {
                string avatarPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "AutomationChick.png");
                if (File.Exists(avatarPath))
                {
                    return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(avatarPath));
                }
            }
            catch
            {
                // 图标加载失败时回退为空地址，不影响 AI 页面启动。
            }
            return string.Empty;
        }

        private const string BaseConversationHtmlTemplate = @"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
*{box-sizing:border-box;}
html,body{height:100%;}
body{
    margin:0;
    color:#172033;
    background:#f5f7fb;
    font:14px/1.5 ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;
    overflow:hidden;
}
.scrollable::-webkit-scrollbar,.thinking-box::-webkit-scrollbar{width:8px;height:8px;}
.scrollable::-webkit-scrollbar-thumb,.thinking-box::-webkit-scrollbar-thumb{background:#c9d3e2;border-radius:8px;border:2px solid #f5f7fb;}
.scrollable::-webkit-scrollbar-track,.thinking-box::-webkit-scrollbar-track{background:#eef2f7;}
.app-shell{height:100%;display:flex;flex-direction:column;background:#f5f7fb;}
.topbar{height:48px;display:flex;align-items:center;justify-content:space-between;padding:0 14px;background:rgba(255,255,255,.92);border-bottom:1px solid #e5ebf3;}
.topbar-left{display:flex;align-items:center;min-width:0;}
.brand{display:flex;align-items:center;gap:10px;min-width:0;}
.brand-mark{width:30px;height:30px;border-radius:8px;background:#172033;color:#fff;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:11px;letter-spacing:.2px;}
.brand-title{font-weight:650;color:#172033;line-height:1.1;}
.brand-subtitle{font-size:12px;color:#7b8798;margin-top:1px;}
.top-actions{display:flex;align-items:center;gap:8px;min-width:0;}
.tool-mode{display:flex;align-items:center;padding:2px;border:1px solid #dbe3ed;border-radius:9px;background:#f5f7fa;}
.toolbar-option,.permission-toggle{height:28px;border:0;border-radius:7px;padding:0 10px;background:transparent;color:#526071;font:12px ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;cursor:pointer;white-space:nowrap;}
.toolbar-option:hover,.permission-toggle:hover{background:#e9f0f7;color:#1f5f99;}
.toolbar-option.active{background:#fff;color:#195e9d;box-shadow:0 1px 4px rgba(30,64,100,.16);font-weight:650;}
.permission-toggle{border:1px solid #dbe3ed;background:#fff;}
.permission-toggle.active{border-color:#df9b46;background:#fff4e6;color:#9a4f00;font-weight:650;box-shadow:0 0 0 2px rgba(223,155,70,.12);}
.toolbar-option:disabled,.permission-toggle:disabled{opacity:.46;cursor:default;}
.icon-button{width:30px;height:30px;border:0;border-radius:8px;background:transparent;color:#526071;display:flex;align-items:center;justify-content:center;cursor:pointer;}
.icon-button svg{width:17px;height:17px;stroke:currentColor;stroke-width:2;fill:none;stroke-linecap:round;stroke-linejoin:round;}
.icon-button:hover{background:#eef3f9;color:#1f5f99;}
.icon-button:disabled{opacity:.42;cursor:default;}
.home-button{width:30px;height:30px;border:0;border-radius:8px;background:transparent;color:#66717f;box-shadow:none;padding:2px;}
.home-button svg{width:25px;height:25px;stroke-width:1.6;}
.home-button.active{background:#f1f3f6;color:#596675;}
.home-button:hover,.home-button.active:hover{background:#eaf3fa;color:#246b9f;}
.home-divider{width:1px;height:20px;margin:0 10px 0 14px;background:#dfe5ec;flex:0 0 1px;}
.topbar-button{height:30px;border:1px solid #d6e0eb;background:#fff;box-shadow:0 1px 2px rgba(30,64,100,.06);}
.toolbar-option.topbar-button:hover,.icon-button.topbar-button:hover{border-color:#b9cbe0;background:#f4f8fc;color:#195e9d;}
.topbar-icon-button{width:32px;padding:0;}
#deleteSessionButton.topbar-button:hover{border-color:#e9beb9;background:#fff4f3;color:#b13b32;}
#fullPermissionButton.active{border-color:#df9b46;background:#fff4e6;color:#9a4f00;box-shadow:0 0 0 2px rgba(223,155,70,.12);}
.chat-area{flex:1;min-height:0;overflow-y:auto;}
#messages{
    max-width:1120px;
    margin:0 auto;
    padding:5px 10px;
}
.task-home{position:relative;min-height:100%;max-width:1120px;margin:0 auto;padding:18px 18px 120px;}
.task-home.hidden,#messages.hidden{display:none;}
.task-home-title{font-size:14px;color:#667085;margin-bottom:8px;}
.task-list{display:flex;flex-direction:column;gap:2px;}
.task-item{width:100%;min-height:34px;border:0;border-radius:8px;background:transparent;padding:6px 8px;display:flex;align-items:center;gap:12px;text-align:left;cursor:pointer;color:#263448;font:14px ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;}
.task-item:hover{background:#eef3f9;}
.task-item-title{min-width:0;flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.task-item-status{flex:0 0 auto;font-size:12px;color:#8a96a7;}
.task-item-status.running{color:#246fb5;}
.task-item-status.failed{color:#b13b32;}
.task-home-empty{padding:8px;color:#9aa4b2;font-size:13px;}
.task-view-all{border:0;background:transparent;color:#8a96a7;padding:7px 8px;text-align:left;font:12px ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;cursor:pointer;}
.task-view-all:hover{color:#246fb5;}
.task-home-watermark{position:absolute;width:48px;height:48px;left:50%;top:52%;transform:translate(-50%,-50%);opacity:.16;filter:grayscale(1);pointer-events:none;}
.msg{
    display:flex;
    flex-direction:column;
    gap:1px;
    margin:0 0 4px;
}
.msg.user{align-items:flex-end;}
.msg.assistant{align-items:flex-start;margin-left:4px;}
.msg.error{align-items:flex-start;margin-left:12px;}
.msg-head{display:flex;align-items:center;gap:5px;padding:0 2px;min-height:22px;}
.msg.user .msg-head{justify-content:flex-end;}
.msg-time{font-size:10px;color:#7b8798;line-height:1.2;}
.avatar{width:22px;height:22px;border-radius:6px;display:inline-flex;align-items:center;justify-content:center;flex:0 0 22px;color:#fff;font-size:9px;font-weight:700;letter-spacing:.2px;}
.avatar-image{display:block;object-fit:contain;background:transparent;}
.system-avatar{background:#9a4f00;}
.copy-message{width:20px;height:20px;border:0;background:transparent;color:#8a96a7;cursor:pointer;padding:3px;border-radius:5px;opacity:.35;display:inline-flex;align-items:center;justify-content:center;}
.copy-message svg{width:14px;height:14px;stroke:currentColor;stroke-width:1.8;fill:none;stroke-linecap:round;stroke-linejoin:round;pointer-events:none;}
.msg:hover .copy-message,.copy-message:focus{opacity:1;}
.copy-message:hover{color:#1f5f99;background:#e8f1fa;}
.msg .content{
    max-width:92%;
    margin-left:0;
    word-break:break-word;
    overflow-wrap:anywhere;
    -webkit-user-select:text;
    user-select:text;
}
.msg.user .content{
    max-width:72%;
    margin-left:0;
    margin-right:12px;
    color:#102033;
    background:#dceeff;
    border:1px solid #bad9f6;
    border-radius:14px 14px 4px 14px;
    padding:2px 6px;
}
.msg.assistant .content{
    max-width:calc(92% - 30px);
    margin-left:30px;
    color:#182434;
    background:#ffffff;
    border:1px solid #dfe6ef;
    border-radius:8px;
    padding:6px 9px;
    line-height:1.55;
    box-shadow:0 2px 8px rgba(31,45,61,.05);
}
.typing-glint{position:fixed;z-index:60;width:4px;height:17px;border-radius:4px;pointer-events:none;background:rgba(255,255,255,.9);box-shadow:0 0 4px 1px rgba(255,255,255,.85),0 0 3px rgba(109,174,235,.28);transform:translate(-1px,-1px);transition:opacity .16s ease;}
.msg.error .content{
    color:#8f1d1d;
    background:#fff5f5;
    border:1px solid #f0caca;
    border-radius:8px;
    padding:2px 6px;
}
.content>*:first-child{margin-top:0;}
.content>*:last-child{margin-bottom:0;}
p{margin:1px 0;}
ul,ol{margin:1px 0;padding-left:19px;}
li{margin:1px 0;}
.msg.assistant .content p{margin:5px 0;}
.msg.assistant .content ul,.msg.assistant .content ol{margin:4px 0 7px;padding-left:21px;}
.msg.assistant .content li{margin:2px 0;}
.msg.assistant .content li + li{margin-top:4px;}
.msg.assistant .content li>p{margin:2px 0;}
.merged-part + .merged-part{margin-top:2px;}
.automation-flow-visual{
    margin:8px 0 4px;
    border:1px solid #d8e2ee;
    border-radius:10px;
    background:#f8fafc;
    overflow:hidden;
}
.flow-visual-title{
    display:flex;
    align-items:center;
    justify-content:space-between;
    gap:8px;
    padding:7px 10px;
    color:#203047;
    background:#eef4fa;
    border-bottom:1px solid #d8e2ee;
    font-size:13px;
    font-weight:650;
}
.flow-process{padding:8px 10px 10px;}
.flow-process + .flow-process{border-top:1px solid #dfe7f1;}
.flow-process-head{display:flex;align-items:center;gap:6px;flex-wrap:wrap;margin-bottom:7px;}
.flow-process-name{font-weight:650;color:#102033;}
.flow-badge{
    display:inline-flex;
    align-items:center;
    height:20px;
    padding:0 7px;
    border-radius:10px;
    color:#526071;
    background:#e9eef5;
    font-size:11px;
    white-space:nowrap;
}
.flow-badge.loop{color:#8a4b08;background:#fff0d9;}
.flow-track{
    display:flex;
    align-items:stretch;
    gap:0;
    overflow-x:auto;
    padding:1px 1px 5px;
}
.flow-track.single-step{overflow-x:hidden;}
.flow-step{
    flex:0 0 clamp(300px,44vw,480px);
    min-width:300px;
    max-width:480px;
    border:1px solid #cfdae7;
    border-radius:8px;
    background:#fff;
    box-shadow:0 1px 4px rgba(31,45,61,.05);
}
.flow-track.single-step .flow-step{flex:1 1 100%;min-width:0;max-width:none;}
.flow-step-head{padding:6px 8px;border-bottom:1px solid #e3e9f1;background:#f7f9fc;border-radius:8px 8px 0 0;}
.flow-step-index{color:#6f7f92;font-size:11px;margin-right:5px;}
.flow-step-name{color:#203047;font-weight:650;}
.flow-step-key{color:#7b8798;font:11px Consolas,""Cascadia Mono"",monospace;margin-left:5px;}
.flow-ops{padding:3px 7px 6px;}
.flow-op{padding:5px 1px;border-bottom:1px dashed #e4e9f0;}
.flow-op:last-child{border-bottom:0;}
.flow-op-line{display:flex;align-items:flex-start;gap:5px;}
.flow-op-index{flex:0 0 18px;color:#7b8798;font-size:11px;line-height:20px;text-align:center;}
.flow-op-text{min-width:0;color:#27364a;line-height:20px;overflow-wrap:anywhere;word-break:break-word;}
.flow-paths{display:flex;gap:4px;flex-wrap:wrap;margin:3px 0 0 23px;}
.flow-path{
    display:inline-flex;
    align-items:center;
    min-height:19px;
    padding:1px 6px;
    border-radius:9px;
    color:#285f46;
    background:#e7f5ed;
    font-size:11px;
}
.flow-path.false{color:#87500e;background:#fff1dc;}
.flow-arrow{flex:0 0 30px;align-self:center;color:#7f91a7;text-align:center;font-size:20px;}
.flow-empty{padding:8px;color:#7b8798;text-align:center;}
blockquote{
    margin:6px 0;
    padding:6px 10px;
    color:#526071;
    background:#f7f9fc;
    border-left:4px solid #9fb6d3;
    border-radius:6px;
}
h1,h2,h3{
    color:#102033;
    line-height:1.28;
    font-weight:650;
    margin:10px 0 6px;
}
h1{font-size:22px;}
h2{font-size:18px;padding-bottom:4px;border-bottom:1px solid #e5ebf3;}
h3{font-size:16px;}
table{
    width:100%;
    margin:7px 0 10px;
    border-collapse:collapse;
    table-layout:auto;
    border:1px solid #d5deea;
    border-radius:6px;
    overflow:hidden;
}
th,td{
    border:1px solid #d5deea;
    padding:5px 8px;
    vertical-align:top;
    word-break:break-word;
    overflow-wrap:anywhere;
}
th{
    color:#203047;
    background:#edf3fa;
    font-weight:650;
}
tr:nth-child(even) td{background:#fbfdff;}
pre{
    margin:6px 0;
    padding:8px 10px;
    color:#101828;
    background:#f1f5f9;
    border:1px solid #d9e2ec;
    border-radius:8px;
    overflow-x:auto;
    white-space:pre;
    overflow-wrap:normal;
    word-break:normal;
    tab-size:4;
}
code{
    color:#26374d;
    background:#eef2f7;
    border:1px solid #d8e0ea;
    border-radius:5px;
    padding:1px 4px;
    font:13px/1.4 Consolas,""Cascadia Mono"",monospace;
}
pre code{background:transparent;border:0;padding:0;font-variant-ligatures:none;}
img{max-width:100%;border-radius:8px;}
hr{border:none;border-top:1px solid #dfe6ef;margin:8px 0;}
.thinking-box{
    width:100%;
    max-height:calc(100vh - 150px);
    overflow-y:auto;
    background:#ffffff;
    border:1px solid #d7e1ee;
    border-radius:8px;
    margin:4px 0 8px;
    padding:0;
    box-shadow:0 3px 10px rgba(31,45,61,.04);
}
.thinking-box.collapsed{max-height:32px;overflow:hidden;}
.thinking-box .reasoning-text{margin:6px 10px;color:#445268;font-size:13px;line-height:1.55;}
.thinking-box .toggle-bar{
    position:sticky;
    top:0;
    z-index:1;
    color:#526071;
    background:#f7f9fc;
    font-size:12px;
    cursor:pointer;
    padding:7px 10px;
    user-select:none;
    border-bottom:1px solid #e3eaf3;
}
.thinking-box.collapsed .toggle-bar{border-bottom:none;}
.thinking-box .toggle-bar:hover{color:#1f5f99;background:#f1f6fb;}
.thinking-box .toggle-bar::before{content:'▼ ';font-size:10px;}
.thinking-box.collapsed .toggle-bar::before{content:'▶ ';font-size:10px;}
.tool-call,.tool-result,.tool-business-failure,.tool-error{
    min-height:22px;
    margin:1px 6px;
    padding:2px 6px;
    border-radius:4px;
    display:flex;
    align-items:center;
    gap:6px;
    font:12px/1.35 ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;
}
.analysis-segment{padding:7px 8px;color:#445269;font-size:13px;line-height:1.65;border-bottom:1px solid #edf1f6;}
.analysis-segment>*:first-child{margin-top:0;}
.analysis-segment>*:last-child{margin-bottom:0;}
.tool-call{color:#69430f;background:#fffaf1;}
.tool-result{color:#405069;background:#f7f9fc;}
.tool-business-failure{color:#805b12;background:#fff9e8;}
.tool-error{color:#9b2c2c;background:#fff5f5;}
.tool-entry-label{flex:0 0 auto;color:#7b8798;font-size:11px;}
.tool-entry-text{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-family:Consolas,""Cascadia Mono"",monospace;}
.tool-entry-count{flex:0 0 auto;margin-left:auto;padding:0 5px;border-radius:8px;background:#e7edf5;color:#526071;font-size:10px;line-height:16px;}
.streaming-segment{
    padding:6px 10px;
    color:#334155;
}
.composer-wrap{padding:8px 14px 10px;background:#f5f7fb;}
.composer{max-width:1120px;margin:0 auto;background:#fff;border:1px solid #e2e7ef;border-radius:16px;min-height:92px;box-shadow:0 6px 16px rgba(16,24,40,.06);position:relative;padding:10px 50px 34px 12px;}
.composer.drag-over{border-color:#5b9bd5;box-shadow:0 0 0 3px rgba(91,155,213,.16),0 6px 16px rgba(16,24,40,.06);}
.prompt-input{width:100%;height:48px;min-height:48px;max-height:140px;border:0;padding:0;outline:none;resize:none;overflow-y:hidden;background:transparent;color:#172033;font:14px/1.45 ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;transition:height .12s ease;}
.prompt-input::placeholder{color:#b6bcc5;}
.attachment-list{display:flex;flex-wrap:wrap;gap:7px;margin:0 0 7px;max-height:138px;overflow-y:auto;}
.attachment-list:empty{display:none;}
.attachment-card{position:relative;display:flex;align-items:center;width:310px;height:58px;padding:7px 30px 7px 7px;border:1px solid #d9e1eb;border-radius:10px;background:#fff;color:#28364a;overflow:hidden;}
.attachment-card.error{border-color:#e2a39d;background:#fff8f7;}
.attachment-card.image-card{width:58px;padding:0;flex:0 0 58px;}
.attachment-preview{width:100%;height:100%;object-fit:cover;border-radius:9px;display:block;}
.attachment-icon{width:40px;height:40px;flex:0 0 40px;border-radius:9px;background:#ef762f;color:#fff;display:flex;align-items:center;justify-content:center;font-size:11px;font-weight:700;}
.attachment-meta{min-width:0;margin-left:9px;}
.attachment-name{font-size:12px;font-weight:650;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.attachment-type{font-size:11px;color:#7b8798;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.attachment-error{font-size:10px;color:#b13b32;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.attachment-remove{position:absolute;right:5px;top:5px;width:19px;height:19px;border:0;border-radius:50%;background:#1f2329;color:#fff;cursor:pointer;font-size:14px;line-height:17px;padding:0;z-index:1;}
.attachment-remove:hover{background:#b13b32;}
.attachment-warning{position:absolute;left:4px;bottom:4px;width:17px;height:17px;border-radius:50%;background:#b13b32;color:#fff;font-size:11px;font-weight:700;text-align:center;line-height:17px;}
.attach-button{position:absolute;left:10px;bottom:9px;width:28px;height:28px;border:0;border-radius:8px;background:transparent;color:#526071;cursor:pointer;font:24px/24px ""Segoe UI"",Arial,sans-serif;padding:0;}
.attach-button:hover{background:#eef3f9;color:#1f5f99;}
.attach-button:disabled{opacity:.42;cursor:default;}
.send-button{position:absolute;right:7px;bottom:7px;width:30px;height:30px;border:0;background:transparent;padding:0;cursor:pointer;display:flex;align-items:center;justify-content:center;}
.send-button .circle{width:26px;height:26px;border-radius:50%;background:#8e929a;display:flex;align-items:center;justify-content:center;color:#fff;}
.send-button:hover .circle{background:#848890;}
.send-button:active .circle{background:#70747c;}
.send-button:disabled{opacity:.45;cursor:default;}
.send-button.ready .circle{background:#246fb5;box-shadow:0 2px 6px rgba(36,111,181,.32);}
.send-button.ready:hover .circle{background:#1f63a3;}
.send-button.ready:active .circle{background:#195486;}
.send-button svg{width:16px;height:16px;fill:#fff;display:block;}
.send-button .stop-icon{display:none;width:9px;height:9px;border-radius:1px;background:#fff;}
.send-button.stop .arrow-icon{display:none;}
.send-button.stop .stop-icon{display:block;}
.modal-backdrop{position:fixed;inset:0;background:rgba(15,23,42,.28);display:none;align-items:center;justify-content:center;padding:24px;z-index:30;}
.modal-backdrop.open{display:flex;}
.config-modal{width:min(860px,96vw);max-height:88vh;overflow:hidden;background:#fff;border:1px solid #e1e7ef;border-radius:18px;box-shadow:0 24px 70px rgba(15,23,42,.22);display:flex;flex-direction:column;}
.modal-head{display:flex;align-items:center;justify-content:space-between;padding:18px 20px;border-bottom:1px solid #edf1f6;}
.modal-title{font-size:17px;font-weight:650;color:#172033;}
.modal-desc{font-size:12px;color:#7b8798;margin-top:2px;}
.modal-body{padding:18px 20px;overflow-y:auto;background:#fbfcfe;}
.settings-grid{display:grid;grid-template-columns:1fr 1fr;gap:14px;}
.field{display:flex;flex-direction:column;gap:6px;}
.field label{font-size:12px;color:#526071;}
.field-hint{margin-left:6px;font-size:11px;color:#9aa4b2;}
.field input[readonly]{background:#f1f4f8;color:#6b7685;cursor:default;}
.field input,.field select{height:36px;border:1px solid #d8e0ea;border-radius:9px;background:#fff;color:#172033;padding:0 10px;font:13px ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;outline:none;}
.field input:focus,.field select:focus{border-color:#75a7da;box-shadow:0 0 0 3px rgba(117,167,218,.18);}
.field-wide{grid-column:1 / -1;}
.field-line{display:grid;grid-template-columns:1fr auto;gap:8px;align-items:center;}
.field-line .text-button{height:36px;white-space:nowrap;}
.check-line{display:flex;align-items:center;gap:8px;min-height:36px;color:#35445a;font-size:12px;}
.check-line input{width:16px;height:16px;margin:0;}
.service-summary{min-height:38px;padding:9px 11px;border:1px solid #dde5ef;border-radius:9px;background:#f4f7fb;color:#526071;font-size:12px;line-height:1.55;}
#modelServiceOverlay{z-index:35;}
.test-list{display:flex;flex-direction:column;gap:9px;}
.test-item{display:grid;grid-template-columns:22px 1fr;gap:9px;align-items:start;padding:11px 12px;border:1px solid #dde5ef;border-radius:10px;background:#fff;cursor:pointer;}
.test-item:hover{border-color:#a8c4e1;background:#f8fbff;}
.test-item input{margin-top:3px;}
.test-name{font-size:13px;font-weight:650;color:#25354a;}
.test-desc{margin-top:3px;font-size:12px;line-height:1.5;color:#748195;}
.test-options{margin-top:13px;padding:11px 12px;border-radius:9px;background:#fff6e8;color:#76501d;font-size:12px;line-height:1.55;}
.test-options label{display:flex;align-items:center;gap:7px;margin-bottom:4px;color:#35445a;}
.modal-foot{display:flex;justify-content:space-between;gap:10px;padding:14px 20px;border-top:1px solid #edf1f6;background:#fff;}
.foot-left,.foot-right{display:flex;gap:10px;}
.text-button,.primary-button{height:36px;border-radius:9px;padding:0 14px;font:13px ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;cursor:pointer;}
.text-button{border:1px solid #d8e0ea;background:#fff;color:#35445a;}
.text-button:hover{background:#f3f7fb;}
.primary-button{border:1px solid #246fb5;background:#246fb5;color:#fff;}
.primary-button:hover{background:#1f63a3;}
.toast{position:fixed;right:20px;top:60px;max-width:420px;background:#172033;color:#fff;border-radius:11px;padding:10px 13px;box-shadow:0 12px 30px rgba(15,23,42,.24);display:none;z-index:40;font-size:13px;}
.toast.show{display:block;}
@media (max-width:720px){.settings-grid{grid-template-columns:1fr}.composer-wrap{padding:10px}.topbar{padding:0 12px}.brand-subtitle{display:none}}
</style>
<script>
var appState={sending:false,taskHomeVisible:true,canAccess:false,canEditConfig:false,config:{},providerOptions:[],modelOptions:[],modelServices:[],conversations:[],activeConversationId:'',attachments:[]};
var quickSettingPending=false;
var dropReadCount=0;
var lastAiActivityAt=Date.now();
var showAllTasks=false;
function post(type,payload){if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(Object.assign({type:type},payload||{}));}}
function byId(id){return document.getElementById(id);}
function formatSessionTime(value){
    if(!value){return '';}
    var time=new Date(value);
    if(isNaN(time.getTime())){return '';}
    return time.toLocaleString('zh-CN',{month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',hour12:false});
}
function renderTaskHome(){
    var home=byId('taskHome'),messages=byId('messages'),list=byId('taskList');
    if(!home||!messages||!list){return;}
    var visible=!!appState.taskHomeVisible;
    home.classList.toggle('hidden',!visible);
    messages.classList.toggle('hidden',visible);
    if(!visible){return;}
    list.innerHTML='';
    var items=appState.conversations||[];
    if(!items.length){var empty=document.createElement('div');empty.className='task-home-empty';empty.textContent='在下方输入需求即可创建新任务';list.appendChild(empty);return;}
    var visibleItems=showAllTasks?items:items.slice(0,10);
    visibleItems.forEach(function(item){
        var button=document.createElement('button');button.type='button';button.className='task-item';
        var title=document.createElement('span');title.className='task-item-title';title.textContent=item.title||'未命名任务';
        var status=document.createElement('span');status.className='task-item-status '+(item.status||'completed');status.textContent=item.statusText||formatSessionTime(item.updatedAt);
        button.appendChild(title);button.appendChild(status);
        button.addEventListener('click',function(){post('openTask',{id:item.id});});
        list.appendChild(button);
    });
    if(!showAllTasks&&items.length>10){
        var viewAll=document.createElement('button');viewAll.type='button';viewAll.className='task-view-all';
        viewAll.textContent='查看全部';
        viewAll.addEventListener('click',function(){showAllTasks=true;renderTaskHome();});
        list.appendChild(viewAll);
    }
}
function toggleThinkingBox(id){
    var el=document.getElementById(id);
    if(el){el.classList.toggle('collapsed');if(el.classList.contains('collapsed')){el.style.maxHeight='32px';el.scrollTop=0;}else{el.style.maxHeight='';scrollThinkingBoxToBottom(id);}}
}
function resizeThinkingBox(box){
    if(!box||box.classList.contains('collapsed')){return;}
    var chat=byId('messagesScroll');
    if(!chat){return;}
    var available=Math.max(120,Math.min(chat.clientHeight-16,Math.floor(chat.clientHeight*.68)));
    box.style.maxHeight=available+'px';
}
var messageScrollFrame=0;
function scrollMessagesToBottom(){
    if(messageScrollFrame){window.cancelAnimationFrame(messageScrollFrame);}
    messageScrollFrame=window.requestAnimationFrame(function(){
        messageScrollFrame=0;
        var m=byId('messagesScroll');
        if(m){m.scrollTop=m.scrollHeight;}
    });
}
function revealFinalAnswer(message){
    if(!message){return;}
    var content=message.querySelector('.content');
    if(!content){return;}
    if(window.matchMedia&&window.matchMedia('(prefers-reduced-motion: reduce)').matches){
        return;
    }
    var walker=document.createTreeWalker(content,NodeFilter.SHOW_TEXT);
    var nodes=[],node,total=0;
    while((node=walker.nextNode())){var value=node.nodeValue||'';if(value.length){nodes.push({node:node,text:value});total+=value.length;node.nodeValue='';}}
    if(!total){return;}
    var glint=document.createElement('span');glint.className='typing-glint';document.body.appendChild(glint);
    function moveGlint(){
        var last=null;for(var i=nodes.length-1;i>=0;i--){if((nodes[i].node.nodeValue||'').length){last=nodes[i].node;break;}}
        if(!last){glint.style.opacity='0';return;}
        var length=last.nodeValue.length,range=document.createRange();range.setStart(last,Math.max(0,length-1));range.setEnd(last,length);
        var rect=range.getBoundingClientRect();if(rect.width||rect.height){glint.style.left=(rect.right-2)+'px';glint.style.top=rect.top+'px';glint.style.height=Math.max(14,rect.height)+'px';glint.style.opacity='1';}
    }
    var duration=Math.max(1200,Math.min(4500,total*15));
    var started=performance.now(),shown=0;
    function frame(now){
        var target=Math.min(total,Math.floor(total*(now-started)/duration));
        if(target>shown){var remaining=target;for(var i=0;i<nodes.length;i++){var item=nodes[i],length=item.text.length;if(remaining>=length){item.node.nodeValue=item.text;remaining-=length;}else{item.node.nodeValue=item.text.slice(0,Math.max(0,remaining));remaining=0;}if(!remaining){for(var j=i+1;j<nodes.length;j++){nodes[j].node.nodeValue='';}break;}}shown=target;moveGlint();scrollMessagesToBottom();}
        if(target<total){window.requestAnimationFrame(frame);}else{nodes.forEach(function(item){item.node.nodeValue=item.text;});moveGlint();glint.style.opacity='0';window.setTimeout(function(){glint.remove();},180);scrollMessagesToBottom();}
    }
    window.requestAnimationFrame(frame);
}
function scrollThinkingBoxToBottom(boxId){
    var box=document.getElementById(boxId);
    if(!box||box.classList.contains('collapsed')){return;}
    if(box._scrollFrame){window.cancelAnimationFrame(box._scrollFrame);}
    box._scrollFrame=window.requestAnimationFrame(function(){
        box._scrollFrame=0;
        if(!box.isConnected||box.classList.contains('collapsed')){return;}
        resizeThinkingBox(box);
        box.scrollTop=box.scrollHeight;
        var messages=byId('messagesScroll');
        if(messages){messages.scrollTop=messages.scrollHeight;}
    });
}
var forcedStreamScrollFrame=0;
var forcedStreamScrollActive=false;
function runForcedStreamScroll(){
    if(!forcedStreamScrollActive){forcedStreamScrollFrame=0;return;}
    document.querySelectorAll('.thinking-box:not(.collapsed)').forEach(function(box){
        box.scrollTop=box.scrollHeight;
    });
    var messages=byId('messagesScroll');
    if(messages){messages.scrollTop=messages.scrollHeight;}
    forcedStreamScrollFrame=window.requestAnimationFrame(runForcedStreamScroll);
}
function startForcedStreamScroll(){
    forcedStreamScrollActive=true;
    if(!forcedStreamScrollFrame){runForcedStreamScroll();}
}
function stopForcedStreamScroll(){
    forcedStreamScrollActive=false;
    if(forcedStreamScrollFrame){window.cancelAnimationFrame(forcedStreamScrollFrame);forcedStreamScrollFrame=0;}
    document.querySelectorAll('.thinking-box:not(.collapsed)').forEach(function(box){
        resizeThinkingBox(box);
        box.scrollTop=box.scrollHeight;
    });
    scrollMessagesToBottom();
}
function setOptions(select,items,value){
    select.innerHTML='';
    (items||[]).forEach(function(item){var opt=document.createElement('option');opt.value=item;opt.textContent=item;select.appendChild(opt);});
    if(value && Array.prototype.some.call(select.options,function(o){return o.value===value;})){select.value=value;}
    else if(select.options.length){select.selectedIndex=0;}
}
function setModelServiceOptions(select,items,value){
    select.innerHTML='';
    var builtIn=document.createElement('option');builtIn.value='';builtIn.textContent='内置 Provider';select.appendChild(builtIn);
    (items||[]).forEach(function(item){var opt=document.createElement('option');opt.value=item.id;opt.textContent=item.name+' · '+item.model;select.appendChild(opt);});
    select.value=value||'';
    if(select.value!==(value||'')){select.value='';}
}
function selectedModelService(){
    var id=byId('cfgModelService').value;
    return (appState.modelServices||[]).find(function(item){return item.id===id;})||null;
}
function refreshModelServiceState(){
    var custom=!!selectedModelService();
    var lock=!appState.canEditConfig||appState.sending;
    ['cfgProvider','cfgModel','cfgApiKey','clearApiKey'].forEach(function(id){var el=byId(id);if(el){el.disabled=lock||custom;}});
    var summary=byId('modelServiceSummary');
    if(summary){
        var service=selectedModelService();
        summary.textContent=service
            ? service.name+'｜'+service.baseUrl+'｜'+service.model+(service.contextLimit?'｜上下文 '+service.contextLimit:'')
            : '使用下方内置 Provider、模型和 API Key。';
    }
}
function collectConfig(){
    return {
        gooseExecutablePath:byId('cfgGoose').value,
        workingDirectory:byId('cfgWorkdir').value,
        mcpUri:byId('cfgMcp').value,
        sessionName:byId('cfgSession').value,
        provider:byId('cfgProvider').value,
        model:byId('cfgModel').value,
        modelServiceId:byId('cfgModelService').value,
        temperature:parseFloat(byId('cfgTemperature').value||'0.7'),
        apiKey:byId('cfgApiKey').value,
        maxTurns:parseInt(byId('cfgTurns').value||'1',10),
        maxOutputTokens:parseInt(byId('cfgOutputTokens').value||'8192',10),
        toolProfile:(appState.config||{}).toolProfile||'Diagnostic',
        autoApproveMode:!!(appState.config||{}).autoApproveMode
    };
}
function fillConfig(){
    var c=appState.config||{};
    byId('cfgGoose').value=c.gooseExecutablePath||'';
    byId('cfgWorkdir').value=c.workingDirectory||'';
    byId('cfgMcp').value=c.mcpUri||'';
    byId('cfgSession').value=c.sessionName||'';
    byId('cfgTurns').value=c.maxTurns||20;
    byId('cfgOutputTokens').value=c.maxOutputTokens||8192;
    byId('cfgTemperature').value=typeof c.temperature==='number'?c.temperature:0.7;
    setModelServiceOptions(byId('cfgModelService'),appState.modelServices||[],c.modelServiceId||'');
    setOptions(byId('cfgProvider'),appState.providerOptions||[],c.provider||'deepseek');
    setOptions(byId('cfgModel'),appState.modelOptions||[],c.model||'deepseek-v4-pro');
    byId('cfgApiKey').value='';
    byId('cfgApiKey').placeholder=c.hasApiKey?'本机已保存，留空则保持不变':'输入 API Key（仅保存在本机）';
    refreshModelServiceState();
}
function refreshToolbar(){
    var c=appState.config||{};
    var profile=c.toolProfile||'Diagnostic';
    byId('toolDiagnostic').classList.toggle('active',profile==='Diagnostic');
    byId('toolEditor').classList.toggle('active',profile==='Editor');
    byId('fullPermissionButton').classList.toggle('active',!!c.fullPermissionEnabled);
    byId('fullPermissionButton').setAttribute('aria-pressed',c.fullPermissionEnabled?'true':'false');
    byId('autoApproveButton').classList.toggle('active',!!c.autoApproveMode);
    byId('autoApproveButton').setAttribute('aria-pressed',c.autoApproveMode?'true':'false');
    var lock=!appState.canEditConfig||appState.sending||quickSettingPending;
    ['toolDiagnostic','toolEditor','autoApproveButton'].forEach(function(id){byId(id).disabled=lock;});
    byId('fullPermissionButton').disabled=lock||profile!=='Editor';
}
function setToolProfile(profile){
    if(quickSettingPending||appState.sending||(appState.config||{}).toolProfile===profile){return;}
    quickSettingPending=true;appState.config.toolProfile=profile;refreshToolbar();post('setToolProfile',{profile:profile});
}
function toggleFullPermission(){
    var c=appState.config||{};
    if(quickSettingPending||appState.sending||(c.toolProfile||'Diagnostic')!=='Editor'){return;}
    quickSettingPending=true;c.fullPermissionEnabled=!c.fullPermissionEnabled;refreshToolbar();post('setFullPermission',{enabled:!!c.fullPermissionEnabled});
}
function toggleAutoApprove(){
    if(quickSettingPending||appState.sending){return;}
    quickSettingPending=true;appState.config.autoApproveMode=!appState.config.autoApproveMode;refreshToolbar();post('setAutoApprove',{enabled:!!appState.config.autoApproveMode});
}
function automationSetState(state){
    appState=state||appState;
    if(appState.sending){lastAiActivityAt=Date.now();}
    quickSettingPending=false;
    var status=byId('statusText');
    if(status){status.textContent=appState.sending?'生成中':'就绪';}
    byId('promptInput').disabled=!appState.canAccess||appState.sending;
    refreshAttachments();
    refreshSendButton();
    byId('attachButton').disabled=!appState.canAccess||appState.sending;
    byId('resetButton').disabled=!appState.canAccess||appState.sending;
    byId('standardTestButton').disabled=!appState.canAccess||appState.sending;
    renderTaskHome();
    byId('newSessionButton').disabled=false;
    byId('newSessionButton').classList.toggle('active',!!appState.taskHomeVisible);
    byId('newSessionButton').setAttribute('aria-pressed',appState.taskHomeVisible?'true':'false');
    byId('deleteSessionButton').disabled=appState.sending||!appState.activeConversationId;
    byId('configButton').disabled=false;
    fillConfig();
    refreshToolbar();
    var lock=!appState.canEditConfig||appState.sending;
    ['cfgGoose','cfgWorkdir','cfgMcp','cfgSession','cfgModelService','cfgProvider','cfgModel','cfgApiKey','cfgTurns','cfgOutputTokens','cfgTemperature','saveConfig','clearApiKey','manageModelServices'].forEach(function(id){var el=byId(id);if(el){el.disabled=lock;}});
    refreshModelServiceState();
    byId('reloadConfig').disabled=appState.sending;
    byId('checkConfig').disabled=appState.sending||!appState.canAccess;
    if(byId('modelServiceOverlay').classList.contains('open')){renderModelServicePicker((appState.config||{}).modelServiceId||'');}
}
function refreshSendButton(){
    var send=byId('sendButton');
    var input=byId('promptInput');
    if(!send||!input){return;}
    var hasAttachmentError=(appState.attachments||[]).some(function(item){return !!item.error;});
    var canSend=appState.canAccess&&!appState.sending&&dropReadCount===0
        &&!hasAttachmentError&&(input.value.trim().length>0||(appState.attachments||[]).length>0);
    send.classList.toggle('stop',!!appState.sending);
    send.classList.toggle('ready',canSend);
    send.disabled=!appState.sending&&!canSend;
    send.title=appState.sending?'停止':(hasAttachmentError?'请先移除无法分析的附件':'发送');
    send.setAttribute('aria-label',appState.sending?'停止':'发送');
}
function readDroppedFile(file){
    if(!file.size||file.size>10*1024*1024){
        showToast('文件大小必须大于0且不超过10 MB。');
        return;
    }
    dropReadCount++;
    refreshSendButton();
    var reader=new FileReader();
    reader.onload=function(){
        try{
            var encoded=String(reader.result||'');
            var comma=encoded.indexOf(',');
            if(comma<0){showToast('文件读取失败。');return;}
            post('dropFile',{name:file.name,mimeType:file.type||'',size:file.size,data:encoded.substring(comma+1)});
        }finally{
            dropReadCount--;
            refreshSendButton();
        }
    };
    reader.onerror=function(){dropReadCount--;refreshSendButton();showToast('文件读取失败。');};
    reader.readAsDataURL(file);
}
function handleComposerDrop(event){
    event.preventDefault();
    var composer=byId('composer');
    composer.classList.remove('drag-over');
    if(appState.sending){return;}
    Array.prototype.forEach.call((event.dataTransfer&&event.dataTransfer.files)||[],readDroppedFile);
}
function refreshAttachments(){
    var host=byId('attachmentList');
    if(!host){return;}
    host.innerHTML='';
    (appState.attachments||[]).forEach(function(item){
        var card=document.createElement('div');
        card.className='attachment-card'+(item.preview?' image-card':'')+(item.error?' error':'');
        card.title=item.error||item.name||'文件';
        if(item.preview){
            var image=document.createElement('img');
            image.className='attachment-preview';
            image.src=item.preview;
            image.alt=item.name||'图片';
            card.appendChild(image);
        }else{
            var icon=document.createElement('div');
            icon.className='attachment-icon';
            var extension=(item.name||'FILE').split('.').pop().toUpperCase();
            icon.textContent=extension.substring(0,4)||'FILE';
            card.appendChild(icon);
            var meta=document.createElement('div');
            meta.className='attachment-meta';
            var name=document.createElement('div');
            name.className='attachment-name';
            name.textContent=item.name||'文件';
            var type=document.createElement('div');
            type.className='attachment-type';
            type.textContent=item.typeLabel||'文件';
            meta.appendChild(name);
            meta.appendChild(type);
            if(item.error){
                var error=document.createElement('div');
                error.className='attachment-error';
                error.textContent=item.error;
                meta.appendChild(error);
            }
            card.appendChild(meta);
        }
        var remove=document.createElement('button');
        remove.className='attachment-remove';
        remove.type='button';
        remove.textContent='×';
        remove.title='移除文件';
        remove.setAttribute('aria-label','移除文件');
        remove.addEventListener('click',function(){post('removeFile',{id:item.id});});
        card.appendChild(remove);
        if(item.preview&&item.error){
            var warning=document.createElement('div');
            warning.className='attachment-warning';
            warning.textContent='!';
            card.appendChild(warning);
        }
        host.appendChild(card);
    });
}
function openConfig(){fillConfig();byId('configOverlay').classList.add('open');}
function closeConfig(){byId('configOverlay').classList.remove('open');}
function findModelService(id){return (appState.modelServices||[]).find(function(item){return item.id===id;})||null;}
function fillModelServiceEditor(id){
    var item=findModelService(id);
    byId('svcId').value=item?item.id:'';
    byId('svcName').value=item?item.name:'';
    byId('svcBaseUrl').value=item?item.baseUrl:'http://127.0.0.1:8080/v1';
    byId('svcModel').value=item?item.model:'';
    byId('svcContextLimit').value=item&&item.contextLimit?item.contextLimit:'';
    byId('svcSupportsVision').checked=!!(item&&item.supportsVision);
    byId('svcRequiresApiKey').checked=!!(item&&item.requiresApiKey);
    byId('svcApiKey').value='';
    byId('svcApiKey').placeholder=item&&item.hasApiKey?'本机已保存，留空则保持不变':'可选；仅使用 Windows 当前用户加密保存';
    var lock=!appState.canEditConfig||appState.sending;
    ['svcPicker','newModelService','svcName','svcBaseUrl','svcModel','svcContextLimit','svcSupportsVision','svcRequiresApiKey','svcApiKey','saveModelService'].forEach(function(controlId){byId(controlId).disabled=lock;});
    byId('deleteModelService').disabled=lock||!item;
    byId('clearModelServiceApiKey').disabled=lock||!item||!item.hasApiKey;
}
function renderModelServicePicker(preferredId){
    var picker=byId('svcPicker');picker.innerHTML='';
    (appState.modelServices||[]).forEach(function(item){var opt=document.createElement('option');opt.value=item.id;opt.textContent=item.name+' · '+item.model;picker.appendChild(opt);});
    var id=preferredId||byId('cfgModelService').value;
    if(id&&findModelService(id)){picker.value=id;fillModelServiceEditor(id);}
    else{picker.selectedIndex=-1;fillModelServiceEditor('');}
}
function openModelServices(){renderModelServicePicker();byId('modelServiceOverlay').classList.add('open');}
function closeModelServices(){byId('modelServiceOverlay').classList.remove('open');}
function collectModelService(){
    var context=parseInt(byId('svcContextLimit').value||'0',10);
    return {id:byId('svcId').value,name:byId('svcName').value,baseUrl:byId('svcBaseUrl').value,
        model:byId('svcModel').value,contextLimit:context>0?context:null,
        supportsVision:byId('svcSupportsVision').checked,requiresApiKey:byId('svcRequiresApiKey').checked,
        apiKey:byId('svcApiKey').value};
}
function openStandardTests(){renderStandardTests();byId('testOverlay').classList.add('open');}
function closeStandardTests(){byId('testOverlay').classList.remove('open');}
function renderStandardTests(){
    var host=byId('testList');host.innerHTML='';
    (appState.testScenarios||[]).forEach(function(item,index){
        var label=document.createElement('label');label.className='test-item';
        var input=document.createElement('input');input.type='checkbox';input.value=item.id;input.checked=index<2;
        var body=document.createElement('div');
        var name=document.createElement('div');name.className='test-name';name.textContent=item.name+(item.turnCount>1?'（'+item.turnCount+'轮）':'');
        var desc=document.createElement('div');desc.className='test-desc';desc.textContent=item.description;
        body.appendChild(name);body.appendChild(desc);label.appendChild(input);label.appendChild(body);host.appendChild(label);
    });
}
function runStandardTests(){
    var ids=Array.prototype.map.call(byId('testList').querySelectorAll('input:checked'),function(item){return item.value;});
    if(!ids.length){showToast('请至少选择一个测试场景。');return;}
    closeStandardTests();
    post('runStandardTests',{ids:ids,separateConversations:byId('separateTestConversations').checked});
}
function showToast(text){var t=byId('toast');t.textContent=text;t.classList.add('show');clearTimeout(window.toastTimer);window.toastTimer=setTimeout(function(){t.classList.remove('show');},3200);}
function copyMessage(button){
    var msg=button.closest('.msg');
    var content=msg&&msg.querySelector('.content');
    if(!content){return;}
    post('copyText',{text:content.innerText||content.textContent||''});
}
function copySelectedText(event){
    var selected='';
    var active=document.activeElement;
    if(active&&(active.tagName==='TEXTAREA'||active.tagName==='INPUT')
        &&typeof active.selectionStart==='number'&&typeof active.selectionEnd==='number'){
        selected=active.value.substring(active.selectionStart,active.selectionEnd);
    }else if(window.getSelection){
        selected=window.getSelection().toString();
    }
    if(!selected){return;}
    event.preventDefault();
    post('copyText',{text:selected});
}
function sendPrompt(){
    if(appState.sending){post('stop');return;}
    if(dropReadCount>0){showToast('文件仍在读取，请稍候。');return;}
    var input=byId('promptInput');
    var text=input.value.trim();
    if(!text&&(appState.attachments||[]).length===0){return;}
    post('send',{prompt:text});
    input.value='';
    autoGrowPrompt();
}
function autoGrowPrompt(){
    var input=byId('promptInput');
    var baseHeight=48,maxHeight=140;
    input.style.height=baseHeight+'px';
    var requiredHeight=input.scrollHeight;
    input.style.height=(requiredHeight>baseHeight+4?Math.min(maxHeight,requiredHeight):baseHeight)+'px';
    input.style.overflowY=requiredHeight>maxHeight?'auto':'hidden';
    refreshSendButton();
}
document.addEventListener('DOMContentLoaded',function(){
    var composer=byId('composer');
    composer.addEventListener('dragenter',function(e){
        var hasFiles=e.dataTransfer&&((e.dataTransfer.files&&e.dataTransfer.files.length>0)
            ||Array.prototype.indexOf.call(e.dataTransfer.types||[],'Files')>=0);
        if(appState.sending||!hasFiles){return;}
        e.preventDefault();composer.classList.add('drag-over');
    });
    composer.addEventListener('dragover',function(e){
        if(!appState.sending){e.preventDefault();}
    });
    composer.addEventListener('dragleave',function(e){
        if(e.relatedTarget&&!composer.contains(e.relatedTarget)){composer.classList.remove('drag-over');}
    });
    composer.addEventListener('drop',handleComposerDrop);
    byId('configButton').addEventListener('click',openConfig);
    byId('standardTestButton').addEventListener('click',openStandardTests);
    byId('toolDiagnostic').addEventListener('click',function(){setToolProfile('Diagnostic');});
    byId('toolEditor').addEventListener('click',function(){setToolProfile('Editor');});
    byId('fullPermissionButton').addEventListener('click',toggleFullPermission);
    byId('autoApproveButton').addEventListener('click',toggleAutoApprove);
    byId('resetButton').addEventListener('click',function(){post('reset');});
    byId('newSessionButton').addEventListener('click',function(){post('showTaskHome');});
    byId('deleteSessionButton').addEventListener('click',function(){
        if(appState.sending||!appState.activeConversationId){return;}
        if(window.confirm('确定删除当前对话吗？删除后无法恢复。')){post('deleteSession');}
    });
    document.addEventListener('keydown',function(e){
        if((e.ctrlKey||e.metaKey)&&String(e.key||'').toLowerCase()==='c'){copySelectedText(e);}
    },true);
    byId('attachButton').addEventListener('click',function(){post('chooseFile');});
    byId('sendButton').addEventListener('click',sendPrompt);
    byId('promptInput').addEventListener('input',autoGrowPrompt);
    byId('promptInput').addEventListener('keydown',function(e){if(e.key==='Enter'&&!e.shiftKey&&!e.altKey){e.preventDefault();sendPrompt();}});
    byId('closeConfig').addEventListener('click',closeConfig);
    byId('cancelConfig').addEventListener('click',closeConfig);
    byId('saveConfig').addEventListener('click',function(){post('saveConfig',{config:collectConfig()});});
    byId('reloadConfig').addEventListener('click',function(){post('reloadConfig');});
    byId('checkConfig').addEventListener('click',function(){post('checkConfig',{config:collectConfig()});});
    byId('clearApiKey').addEventListener('click',function(){post('clearApiKey',{provider:byId('cfgProvider').value});});
    byId('cfgProvider').addEventListener('change',function(){post('providerChanged',{provider:this.value,config:collectConfig()});});
    byId('cfgModelService').addEventListener('change',refreshModelServiceState);
    byId('manageModelServices').addEventListener('click',openModelServices);
    byId('configOverlay').addEventListener('click',function(e){if(e.target===this){closeConfig();}});
    byId('closeModelServices').addEventListener('click',closeModelServices);
    byId('doneModelServices').addEventListener('click',closeModelServices);
    byId('newModelService').addEventListener('click',function(){byId('svcPicker').selectedIndex=-1;fillModelServiceEditor('');});
    byId('svcPicker').addEventListener('change',function(){fillModelServiceEditor(this.value);});
    byId('saveModelService').addEventListener('click',function(){post('saveModelService',{config:collectConfig(),service:collectModelService()});});
    byId('deleteModelService').addEventListener('click',function(){var id=byId('svcId').value;if(id&&window.confirm('确定删除该自定义模型服务吗？')){post('deleteModelService',{config:collectConfig(),id:id});}});
    byId('clearModelServiceApiKey').addEventListener('click',function(){var id=byId('svcId').value;if(id){post('clearModelServiceApiKey',{id:id});}});
    byId('modelServiceOverlay').addEventListener('click',function(e){if(e.target===this){closeModelServices();}});
    byId('closeTests').addEventListener('click',closeStandardTests);
    byId('cancelTests').addEventListener('click',closeStandardTests);
    byId('runTests').addEventListener('click',runStandardTests);
    byId('testOverlay').addEventListener('click',function(e){if(e.target===this){closeStandardTests();}});
    post('ready');
});
window.setInterval(function(){
    if(!appState.sending){return;}
    var status=byId('statusText');
    if(!status){return;}
    var seconds=Math.max(0,Math.floor((Date.now()-lastAiActivityAt)/1000));
    status.textContent=seconds<10?'生成中':'模型处理中 · 已等待 '+seconds+' 秒';
},1000);
window.addEventListener('resize',function(){document.querySelectorAll('.thinking-box:not(.collapsed)').forEach(resizeThinkingBox);});
</script>
</head>
<body>
<div class=""app-shell"">
  <header class=""topbar"">
    <div class=""topbar-left""><div class=""brand""><div class=""brand-mark"">EW</div><div><div class=""brand-title"">EW-AI 助手</div><div class=""brand-subtitle"" id=""statusText"">就绪</div></div></div><span class=""home-divider"" aria-hidden=""true""></span><button class=""icon-button home-button"" id=""newSessionButton"" title=""任务主页"" aria-label=""任务主页"" aria-pressed=""false""><svg viewBox=""0 0 24 24"" aria-hidden=""true""><path d=""M3.5 11.2 12 4l8.5 7.2""/><path d=""M5.8 10.2v9.3h12.4v-9.3""/><path d=""M9.3 19.5v-5.7h5.4v5.7""/></svg></button></div>
    <div class=""top-actions"">
      <button class=""toolbar-option topbar-button"" id=""standardTestButton"" title=""选择并连续运行标准测试场景"">标准测试</button>
      <button class=""toolbar-option topbar-button"" id=""fullPermissionButton"" aria-pressed=""false"" title=""加载控制卡、IO、PLC、通讯及平台配置等完整工程工具"">完全权限</button>
      <button class=""icon-button topbar-button topbar-icon-button"" id=""deleteSessionButton"" title=""删除当前对话"" aria-label=""删除当前对话""><svg viewBox=""0 0 24 24""><path d=""M4 7h16""/><path d=""M9 7V4h6v3""/><path d=""M7 7l1 13h8l1-13""/><path d=""M10 11v5""/><path d=""M14 11v5""/></svg></button>
      <div class=""tool-mode"" role=""group"" aria-label=""AI工具模式""><button class=""toolbar-option"" id=""toolDiagnostic"" title=""只读查询和流程诊断"">诊断</button><button class=""toolbar-option"" id=""toolEditor"" title=""读取、诊断、配置编辑和运行控制"">编辑</button></div>
      <button class=""permission-toggle"" id=""autoApproveButton"" aria-pressed=""false"" title=""开启后自动批准工具调用和预演确认，请谨慎操作"">自动批准</button>
      <button class=""icon-button"" id=""resetButton"" title=""重置会话"" aria-label=""重置会话""><svg viewBox=""0 0 24 24""><path d=""M3 12a9 9 0 1 0 3-6.7""/><path d=""M3 4v6h6""/></svg></button>
      <button class=""icon-button"" id=""configButton"" title=""配置"" aria-label=""配置""><svg viewBox=""0 0 24 24""><path d=""M12 15.5a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7Z""/><path d=""M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9l-.1-.1A2 2 0 1 1 7 4.2l.1.1A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1A2 2 0 1 1 19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.1a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z""/></svg></button>
    </div>
  </header>
  <main class=""chat-area scrollable"" id=""messagesScroll""><section class=""task-home"" id=""taskHome""><div class=""task-home-title"">任务</div><div class=""task-list"" id=""taskList""></div><img class=""task-home-watermark"" src=""__CHICK_AVATAR__"" alt=""""></section><div class=""hidden"" id=""messages""></div></main>
  <footer class=""composer-wrap""><div id=""composer"" class=""composer""><div id=""attachmentList"" class=""attachment-list"" aria-label=""待发送文件""></div><textarea id=""promptInput"" class=""prompt-input"" placeholder=""要求后续变更""></textarea><button id=""attachButton"" class=""attach-button"" type=""button"" title=""添加文件"" aria-label=""添加文件"">+</button><button id=""sendButton"" class=""send-button"" title=""发送"" aria-label=""发送""><span class=""circle""><svg class=""arrow-icon"" viewBox=""0 0 24 24""><path d=""M12 5 5.5 11.5l1.6 1.6 3.8-3.8V20h2.2V9.3l3.8 3.8 1.6-1.6L12 5Z""/></svg><span class=""stop-icon""></span></span></button></div></footer>
</div>
<div class=""modal-backdrop"" id=""testOverlay"">
  <section class=""config-modal"" style=""width:min(680px,96vw)"">
    <div class=""modal-head""><div><div class=""modal-title"">标准测试</div><div class=""modal-desc"">选择场景后将按真实聊天链路逐轮发送，仍遵守当前权限与预演确认。</div></div><button class=""icon-button"" id=""closeTests"" title=""关闭""><svg viewBox=""0 0 24 24""><path d=""M18 6 6 18""/><path d=""M6 6l12 12""/></svg></button></div>
    <div class=""modal-body scrollable""><div class=""test-list"" id=""testList""></div><div class=""test-options""><label><input type=""checkbox"" id=""separateTestConversations"" checked> 每个场景使用独立新对话</label>这些测试可能创建或修改流程配置。未开启自动批准时，仍需人工确认每次预演。</div></div>
    <div class=""modal-foot""><div></div><div class=""foot-right""><button class=""text-button"" id=""cancelTests"">取消</button><button class=""primary-button"" id=""runTests"">开始测试</button></div></div>
  </section>
</div>
<div class=""modal-backdrop"" id=""configOverlay"">
  <section class=""config-modal"">
    <div class=""modal-head""><div><div class=""modal-title"">AI 助手配置</div><div class=""modal-desc"">选择常用 AI 服务、模型并配置本机 API Key。</div></div><button class=""icon-button"" id=""closeConfig"" title=""关闭""><svg viewBox=""0 0 24 24""><path d=""M18 6 6 18""/><path d=""M6 6l12 12""/></svg></button></div>
    <div class=""modal-body scrollable"">
      <div class=""settings-grid"">
        <div class=""field field-wide""><label>AI 运行组件路径</label><input id=""cfgGoose"" autocomplete=""off""></div>
        <div class=""field""><label>工作目录<span class=""field-hint"">自动跟随程序目录</span></label><input id=""cfgWorkdir"" readonly autocomplete=""off""></div>
        <div class=""field""><label>MCP 地址</label><input id=""cfgMcp"" autocomplete=""off""></div>
        <div class=""field""><label>会话名</label><input id=""cfgSession"" autocomplete=""off""></div>
        <div class=""field""><label>最大轮次</label><input id=""cfgTurns"" type=""number"" min=""1"" max=""200""></div>
        <div class=""field""><label>单次输出 Token</label><input id=""cfgOutputTokens"" type=""number"" min=""1024"" max=""65536"" step=""1024""></div>
        <div class=""field""><label>温度</label><input id=""cfgTemperature"" type=""number"" min=""0"" max=""1"" step=""0.05""></div>
        <div class=""field field-wide""><label>模型来源</label><div class=""field-line""><select id=""cfgModelService""></select><button class=""text-button"" id=""manageModelServices"" type=""button"">管理自定义服务</button></div><div class=""service-summary"" id=""modelServiceSummary""></div></div>
        <div class=""field""><label>Provider</label><select id=""cfgProvider""></select></div>
        <div class=""field""><label>模型</label><select id=""cfgModel""></select></div>
        <div class=""field field-wide""><label>API Key（使用 Windows 当前用户加密，仅保存在本机）</label><input id=""cfgApiKey"" type=""password"" autocomplete=""new-password""></div>
      </div>
    </div>
    <div class=""modal-foot""><div class=""foot-left""><button class=""text-button"" id=""reloadConfig"">重载</button><button class=""text-button"" id=""checkConfig"">检查 AI 组件</button><button class=""text-button"" id=""clearApiKey"">清除本机密钥</button></div><div class=""foot-right""><button class=""text-button"" id=""cancelConfig"">取消</button><button class=""primary-button"" id=""saveConfig"">保存配置</button></div></div>
  </section>
</div>
<div class=""modal-backdrop"" id=""modelServiceOverlay""><section class=""config-modal"" style=""width:min(760px,96vw)""><div class=""modal-head""><div><div class=""modal-title"">自定义模型服务</div><div class=""modal-desc"">配置 llama.cpp、vLLM、LM Studio 等 OpenAI 兼容服务；配置只注入当前 EW-AI 进程。</div></div><button class=""icon-button"" id=""closeModelServices"" title=""关闭""><svg viewBox=""0 0 24 24""><path d=""M18 6 6 18""/><path d=""M6 6l12 12""/></svg></button></div>
  <div class=""modal-body scrollable""><div class=""settings-grid""><div class=""field field-wide""><label>已配置服务</label><div class=""field-line""><select id=""svcPicker""></select><button class=""text-button"" id=""newModelService"" type=""button"">新增</button></div></div><input id=""svcId"" type=""hidden""><div class=""field""><label>服务名称</label><input id=""svcName"" autocomplete=""off"" placeholder=""例如：车间 llama.cpp""></div><div class=""field""><label>模型 ID</label><input id=""svcModel"" autocomplete=""off"" placeholder=""服务 /v1/models 返回的 id""></div><div class=""field field-wide""><label>OpenAI Base URL</label><input id=""svcBaseUrl"" autocomplete=""off"" placeholder=""http://172.16.50.172:8080/v1""></div><div class=""field""><label>上下文长度<span class=""field-hint"">留空则由 Goose 判断</span></label><input id=""svcContextLimit"" type=""number"" min=""1"" step=""1024"" placeholder=""例如 131072""></div><div class=""field""><label>模型能力</label><label class=""check-line""><input id=""svcSupportsVision"" type=""checkbox"">支持图片输入</label></div><div class=""field""><label>鉴权</label><label class=""check-line""><input id=""svcRequiresApiKey"" type=""checkbox"">服务要求 API Key</label></div><div class=""field field-wide""><label>API Key（Windows 当前用户加密）</label><input id=""svcApiKey"" type=""password"" autocomplete=""new-password""></div></div></div>
  <div class=""modal-foot""><div class=""foot-left""><button class=""text-button"" id=""clearModelServiceApiKey"">清除密钥</button><button class=""text-button"" id=""deleteModelService"">删除服务</button></div><div class=""foot-right""><button class=""text-button"" id=""doneModelServices"">完成</button><button class=""primary-button"" id=""saveModelService"">保存并选中</button></div></div></section></div>
<div class=""toast"" id=""toast""></div>
</body>
</html>";

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
                promptCts?.Dispose();
                webViewConversation?.Dispose();
                webViewConversation = null;
            }
            base.Dispose(disposing);
        }

        private void InitializeLayout()
        {
            SuspendLayout();
            Text = "EW-AI 助手";
            BackColor = Color.FromArgb(245, 247, 251);
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
                        graphics.Clear(Color.White);
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
            appliedConfig = CloneGooseConfig(config);
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
                ["taskHomeVisible"] = taskHomeVisible,
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
                ["activeConversationId"] = taskHomeVisible ? string.Empty : activeConversation?.Id ?? string.Empty,
                ["conversations"] = new JArray(conversations
                    .OrderByDescending(item => item.UpdatedAt)
                    .Select(item => new JObject
                    {
                        ["id"] = item.Id,
                        ["title"] = item.Title,
                        ["updatedAt"] = item.UpdatedAt,
                        ["status"] = taskRuntimes.TryGetValue(item.Id, out AiTaskRuntime runtime)
                            ? runtime.Running ? "running" : string.Equals(runtime.Status, "失败", StringComparison.Ordinal) ? "failed" : "completed"
                            : "completed",
                        ["statusText"] = taskRuntimes.TryGetValue(item.Id, out AiTaskRuntime taskRuntime) && taskRuntime.Running
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
            GooseConfig oldConfig = CloneGooseConfig(appliedConfig);
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
            GooseAcpClient activeClient;
            bool sessionToolsReloaded = false;
            lock (clientLock)
            {
                activeClient = gooseClient;
            }
            if (uriChanged || profileChanged)
            {
                try
                {
                    if (SF.mainfrm?.McpServerManager == null)
                    {
                        throw new InvalidOperationException("MCP Server管理器未初始化");
                    }
                    if (uriChanged)
                    {
                        await SF.mainfrm.McpServerManager.EnsureStartedAsync(config.McpUri, config.ToolProfile)
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
            GooseAcpClient activeClient;
            lock (clientLock)
            {
                activeClient = gooseClient;
            }
            try
            {
                if (SF.mainfrm?.McpServerManager == null)
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
            string result = await Task.Run(() => CheckGooseCore(config)).ConfigureAwait(true);
            AppendConversation("系统", result, Color.FromArgb(56, 66, 88));
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

        private static GooseConfig CloneGooseConfig(GooseConfig config)
        {
            if (config == null)
            {
                return null;
            }
            return new GooseConfig
            {
                GooseExecutablePath = config.GooseExecutablePath,
                WorkingDirectory = config.WorkingDirectory,
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

            if (taskHomeVisible || activeConversation == null)
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
            string prompt = (enteredPrompt ?? string.Empty).Trim();
            fileAttachments = fileAttachments ?? new List<GooseFileAttachment>();

            if (string.IsNullOrWhiteSpace(prompt) && fileAttachments.Count == 0)
            {
                return false;
            }
            if (runtime == null || runtime.Running)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = "请分析我上传的文件。";
            }
            if (!TryBuildConfig(out GooseConfig config, out string error))
            {
                ShowWebToast("配置无效：" + error);
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

            string conversationText = prompt;
            if (fileAttachments.Count > 0)
            {
                conversationText += "\n\n📎 附件：" + string.Join("、", fileAttachments.Select(item => item.FileName));
            }
            // 新建或切换任务时 WebView 正在重载，导航完成后会从任务历史统一渲染；
            // 此时不能提前追加气泡，否则同一条用户消息会显示两次。
            if (webDocumentReady)
            {
                AppendConversation("用户", conversationText, Color.FromArgb(22, 72, 130));
            }
            DateTime startedAt = DateTime.Now;
            runtime.Conversation.Messages.Add(new AiConversationMessage
            {
                Role = "user",
                Text = conversationText,
                Time = startedAt
            });
            if (runtime.Conversation.Messages.Count == 1)
            {
                runtime.Conversation.Title = conversationText.Length > 24
                    ? conversationText.Substring(0, 24) + "…"
                    : conversationText;
            }
            runtime.Conversation.UpdatedAt = startedAt;
            runtime.PendingEvents.Clear();
            runtime.Running = true;
            runtime.Status = "进行中";
            SaveConversationHistory();
            // 生成期间持续强制跟随消息区与思考框底部，不允许增量脚本或用户上滑打断信息流。
            EnqueueScript("if(window.startForcedStreamScroll){startForcedStreamScroll();}");
            sending = true;
            latestAssistantSegmentText = null;
            latestAssistantSegmentDivId = null;
            flowVisualizationCallIds.Clear();
            flowVisualizationProcesses.RemoveAll();
            promptStartedAt = DateTime.Now;
            runtime.Cancellation?.Dispose();
            runtime.Cancellation = new CancellationTokenSource();
            promptCts = runtime.Cancellation;
            ApplyPermissions();

            try
            {
                GooseAcpClient client = EnsureTaskClient(runtime, config);
                await client.PromptAsync(prompt, fileAttachments, runtime.Cancellation.Token).ConfigureAwait(true);
                DateTime completedAt = DateTime.Now;
                HashSet<string> sentAttachmentIds = new HashSet<string>(fileAttachments.Select(item => item.Id), StringComparer.Ordinal);
                pendingFileAttachments.RemoveAll(item => sentAttachmentIds.Contains(item.Id));
                foreach (string attachmentId in sentAttachmentIds)
                {
                    fileAttachmentPreviews.Remove(attachmentId);
                }
                bool isActive = !taskHomeVisible && ReferenceEquals(activeConversation, runtime.Conversation);
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
                    ? PromoteLatestAssistantSegment(client.LastAssistantResponse, visualizationJson)
                    : ExtractLatestAssistantText(runtime.PendingEvents, client.LastAssistantResponse);
                if (!string.IsNullOrWhiteSpace(assistantText))
                {
                    runtime.Conversation.Messages.Add(new AiConversationMessage
                    {
                        Role = "assistant",
                        Text = assistantText,
                        Time = DateTime.Now,
                        VisualizationJson = visualizationJson
                    });
                    runtime.Conversation.UpdatedAt = DateTime.Now;
                }
                runtime.Conversation.UpdatedAt = DateTime.Now;
                runtime.Status = "已完成";
                runtime.PendingEvents.Clear();
                SaveConversationHistory();
                return true;
            }
            catch (OperationCanceledException)
            {
                runtime.Status = "已停止";
                if (!taskHomeVisible && ReferenceEquals(activeConversation, runtime.Conversation))
                {
                    AppendConversation("系统", "已停止本轮生成。", Color.DarkOrange);
                    if (restoreComposerOnFailure)
                    {
                        RestoreComposerAfterFailedSend(enteredPrompt);
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                runtime.Status = "失败";
                if (!taskHomeVisible && ReferenceEquals(activeConversation, runtime.Conversation))
                {
                    AppendConversation("错误", ex.Message, Color.Red);
                    if (restoreComposerOnFailure)
                    {
                        RestoreComposerAfterFailedSend(enteredPrompt);
                    }
                }
                return false;
            }
            finally
            {
                runtime.Running = false;
                runtime.Cancellation?.Dispose();
                runtime.Cancellation = null;
                if (!taskHomeVisible && ReferenceEquals(activeConversation, runtime.Conversation))
                {
                    FinishStreaming();
                    FinishThoughtStreaming();
                    CollapseThinkingBox();
                    EnqueueScript("if(window.stopForcedStreamScroll){stopForcedStreamScroll();}");
                    promptCts = null;
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
            runtime?.Cancellation?.Cancel();
            lock (clientLock)
            {
                runtime?.Client?.Cancel();
            }
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
                    AppendConversation("系统", "标准测试：" + scenario.Name, Color.FromArgb(86, 102, 122));

                    if (!AiStandardTestSuite.Prepare(
                        scenario, out AiStandardTestFixtureState fixture, out string prepareError))
                    {
                        failedScenarios++;
                        AiAnalysisLogger.Write(new JObject
                        {
                            ["event"] = "standard_test.preparation_failed",
                            ["conversationId"] = activeConversation?.Id ?? string.Empty,
                            ["scenarioId"] = scenario.Id,
                            ["message"] = prepareError ?? string.Empty
                        });
                        AppendConversation("系统",
                            "### 未执行 · " + scenario.Name + "\n\n- ✗ 测试准备失败：" + prepareError,
                            Color.FromArgb(178, 68, 68));
                        continue;
                    }
                    AppendConversation("系统",
                        string.IsNullOrWhiteSpace(fixture.SelectedProcessName)
                            ? "测试环境已清理：仅移除名称以“标准测试_”开头的测试对象。"
                            : "测试夹具已准备并选中流程：" + fixture.SelectedProcessName,
                        Color.FromArgb(86, 102, 122));

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
                        AiStandardTestEvaluation evaluation = AiStandardTestSuite.Evaluate(scenario, fixture);
                        if (evaluation.Passed) passedScenarios++;
                        else failedScenarios++;
                        AiAnalysisLogger.Write(new JObject
                        {
                            ["event"] = "standard_test.evaluated",
                            ["conversationId"] = activeConversation?.Id ?? string.Empty,
                            ["scenarioId"] = scenario.Id,
                            ["passed"] = evaluation.Passed,
                            ["checks"] = new JArray(evaluation.Details)
                        });
                        AppendConversation("系统", evaluation.ToMarkdown(scenario.Name),
                            evaluation.Passed
                                ? Color.FromArgb(54, 128, 84)
                                : Color.FromArgb(178, 68, 68));
                    }

                    if (separateConversations && activeConversation != null)
                    {
                        activeConversation.Title = "标准测试 · " + scenario.Name;
                        activeConversation.UpdatedAt = DateTime.Now;
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

        private void LoadConversationHistory()
        {
            try
            {
                conversations.Clear();
                conversations.AddRange(AiConversationStorage.Load());
            }
            catch (Exception ex)
            {
                conversations.Clear();
                MessageBox.Show("AI 会话历史读取失败，已用空历史继续启动：" + ex.Message,
                    "EW-AI 会话历史", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            taskRuntimes.Clear();
            foreach (AiConversation conversation in conversations)
            {
                taskRuntimes[conversation.Id] = new AiTaskRuntime
                {
                    Conversation = conversation,
                    Status = "已完成",
                    RestoredContext = BuildRestoredConversationContext(conversation)
                };
            }
            activeConversation = null;
            restoredConversationContext = null;
            taskHomeVisible = true;
        }

        private static AiConversation CreateConversation()
        {
            DateTime now = DateTime.Now;
            return new AiConversation
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "新对话",
                CreatedAt = now,
                UpdatedAt = now
            };
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
            activeConversation = CreateConversation();
            conversations.Insert(0, activeConversation);
            taskRuntimes[activeConversation.Id] = new AiTaskRuntime
            {
                Conversation = activeConversation,
                Status = "等待开始"
            };
            while (conversations.Count > AiConversationStorage.MaxConversationCount)
            {
                conversations.Remove(conversations.OrderBy(item => item.UpdatedAt).First());
            }
            SaveConversationHistory();
            restoredConversationContext = null;
            taskHomeVisible = false;
            ResetConversationViewState();
        }

        private void DeleteCurrentConversation()
        {
            if (activeConversation == null || ActiveTaskRuntime?.Running == true)
            {
                return;
            }

            string deletedId = activeConversation.Id;
            if (taskRuntimes.TryGetValue(deletedId, out AiTaskRuntime runtime))
            {
                runtime.Client?.Dispose();
                runtime.Cancellation?.Dispose();
                taskRuntimes.Remove(deletedId);
            }
            conversations.RemoveAll(item => string.Equals(item.Id, deletedId, StringComparison.Ordinal));
            activeConversation = null;
            restoredConversationContext = null;
            taskHomeVisible = true;
            ResetConversationViewState();
            SaveConversationHistory();
            ShowWebToast("当前对话已删除。");
        }

        private void SwitchConversation(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return;
            }
            AiConversation target = conversations.FirstOrDefault(item =>
                string.Equals(item.Id, conversationId, StringComparison.Ordinal));
            if (target == null)
            {
                ShowWebToast("未找到该历史会话。");
                PushWebAppState();
                return;
            }
            activeConversation = target;
            if (!taskRuntimes.TryGetValue(target.Id, out AiTaskRuntime runtime))
            {
                runtime = new AiTaskRuntime
                {
                    Conversation = target,
                    Status = "已完成",
                    RestoredContext = BuildRestoredConversationContext(target)
                };
                taskRuntimes[target.Id] = runtime;
            }
            gooseClient = runtime.Client;
            promptCts = runtime.Cancellation;
            restoredConversationContext = runtime.RestoredContext;
            sending = runtime.Running;
            taskHomeVisible = false;
            ResetConversationViewState();
        }

        private void ShowTaskHome()
        {
            taskHomeVisible = true;
            activeConversation = null;
            gooseClient = null;
            promptCts = null;
            sending = false;
            ResetConversationViewState();
        }

        private void ResetConversationSessionState()
        {
            DisposeGooseClient();
            ResetConversationViewState();
            txtSessionName.Text = "automation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        }

        private void ResetConversationViewState()
        {
            pendingFileAttachments.Clear();
            fileAttachmentPreviews.Clear();
            promptedPreviewIds.Clear();
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

        private static string BuildRestoredConversationContext(AiConversation conversation)
        {
            var builder = new StringBuilder();
            foreach (AiConversationMessage message in conversation.Messages)
            {
                builder.Append(message.Role == "user" ? "用户：" : "EW-AI：");
                builder.AppendLine(message.Text);
            }
            return builder.ToString();
        }

        private void SaveConversationHistory()
        {
            try
            {
                conversations.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
                while (conversations.Count > AiConversationStorage.MaxConversationCount)
                {
                    conversations.RemoveAt(conversations.Count - 1);
                }
                AiConversationStorage.Save(conversations);
                PushWebAppState();
            }
            catch (Exception ex)
            {
                ShowWebToast("会话历史保存失败：" + ex.Message);
            }
        }

        private void RenderActiveConversation()
        {
            if (activeConversation == null)
            {
                return;
            }
            foreach (AiConversationMessage message in activeConversation.Messages)
            {
                AppendConversation(message.Role == "user" ? "用户" : "EW-AI", message.Text,
                    message.Role == "user" ? Color.FromArgb(22, 72, 130) : Color.FromArgb(30, 104, 74),
                    message.Time, message.VisualizationJson);
            }
        }

        private void RenderActiveTaskView()
        {
            if (taskHomeVisible || activeConversation == null)
            {
                return;
            }
            RenderActiveConversation();
            if (!taskRuntimes.TryGetValue(activeConversation.Id, out AiTaskRuntime runtime)
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
            webDocumentReady = false;
            webViewConversation.CoreWebView2.NavigateToString(BaseConversationHtml);
        }

        private GooseAcpClient EnsureTaskClient(AiTaskRuntime runtime, GooseConfig config)
        {
            lock (clientLock)
            {
                if (runtime.Client != null)
                {
                    return runtime.Client;
                }

                runtime.Client = new GooseAcpClient(config, runtime.RestoredContext);
                runtime.RestoredContext = null;
                runtime.Client.EventReceived += item => TaskClient_EventReceived(runtime, item);
                runtime.Client.PermissionRequestHandler = HandlePermissionRequest;
                if (ReferenceEquals(ActiveTaskRuntime, runtime))
                {
                    gooseClient = runtime.Client;
                    restoredConversationContext = null;
                }
                return runtime.Client;
            }
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
            bool isActive = !taskHomeVisible && ReferenceEquals(activeConversation, runtime.Conversation);
            if (isActive)
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

        #region 自定义审核对话框

        // 预演确认对话框：以表格形式展示变更详情（操作类型/位置/对象/字段/原值/新值），让用户清晰看到改了什么。
        private DialogResult ShowPreviewApprovalDialog(string previewId, JArray changes, JArray messages)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "EW-AI 预演确认";
                dlg.StartPosition = FormStartPosition.CenterParent;
                bool hasChanges = changes != null && changes.Count > 0;
                dlg.Width = 820;
                dlg.Height = hasChanges ? 520 : 330;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.BackColor = Color.FromArgb(246, 248, 251);
                dlg.Font = new Font("微软雅黑", 9F);

                // 标题区：使用浅色层级，避免大色块压迫内容。
                Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.White, Padding = new Padding(18, 10, 18, 8) };
                headerPanel.Controls.Add(new Label
                {
                    Text = "确认本次预演",
                    Font = new Font("微软雅黑", 14F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(25, 39, 58),
                    Dock = DockStyle.Top,
                    Height = 30,
                    TextAlign = ContentAlignment.MiddleLeft
                });
                headerPanel.Controls.Add(new Label
                {
                    Text = hasChanges ? "请检查变更明细，确认后才会提交。" : "请确认以下操作，确认后才会提交。",
                    Font = new Font("微软雅黑", 9F),
                    ForeColor = Color.FromArgb(100, 113, 132),
                    Dock = DockStyle.Bottom,
                    Height = 22,
                    TextAlign = ContentAlignment.MiddleLeft
                });

                // 信息行
                Panel infoPanel = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(18, 8, 18, 6), BackColor = Color.FromArgb(246, 248, 251) };
                infoPanel.Controls.Add(new Label
                {
                    Text = $"预演编号  {previewId}      变更  {changes?.Count ?? 0} 项",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.FromArgb(82, 96, 116),
                    Font = new Font("Consolas", 9F)
                });

                // 变更表格
                DataGridView dgv = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    ReadOnly = true,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    BackgroundColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                    {
                        BackColor = Color.FromArgb(237, 242, 247),
                        Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                        Alignment = DataGridViewContentAlignment.MiddleCenter
                    },
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Font = new Font("微软雅黑", 9F),
                        Alignment = DataGridViewContentAlignment.MiddleLeft,
                        WrapMode = DataGridViewTriState.True
                    },
                    RowHeadersVisible = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    GridColor = Color.FromArgb(226, 232, 240),
                    EnableHeadersVisualStyles = false,
                    ColumnHeadersHeight = 34,
                    RowTemplate = { Height = 32 }
                };
                dgv.Columns.Add("colType", "操作类型");
                dgv.Columns.Add("colLocation", "位置");
                dgv.Columns.Add("colObject", "对象");
                dgv.Columns.Add("colField", "字段");
                dgv.Columns.Add("colOld", "原值");
                dgv.Columns.Add("colNew", "新值");
                dgv.Columns["colOld"].DefaultCellStyle.ForeColor = Color.FromArgb(180, 80, 80);
                dgv.Columns["colNew"].DefaultCellStyle.ForeColor = Color.FromArgb(30, 120, 50);

                if (changes != null)
                {
                    foreach (JToken change in changes)
                    {
                        string type = change["type"]?.Value<string>() ?? "";
                        string location = "";
                        string obj = "";
                        string field = "—";
                        string oldVal = "—";
                        string newVal = "—";
                        Color rowColor = Color.White;

                        switch (type)
                        {
                            case "field_change":
                                type = "修改字段";
                                location = change["target"]?.Value<string>() ?? "";
                                field = change["field"]?.Value<string>() ?? "";
                                oldVal = FormatJsonValue(change["oldValue"]);
                                newVal = FormatJsonValue(change["newValue"]);
                                rowColor = Color.FromArgb(255, 248, 235);
                                break;
                            case "insert_step":
                            case "append_step":
                                type = "新增步骤";
                                location = $"步骤{change["stepIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                rowColor = Color.FromArgb(235, 255, 235);
                                break;
                            case "delete_step":
                                type = "删除步骤";
                                location = $"步骤{change["oldStepIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                rowColor = Color.FromArgb(255, 235, 235);
                                break;
                            case "move_step":
                                type = "移动步骤";
                                location = $"{change["oldStepIndex"]?.Value<int>() ?? 0} → {change["newStepIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                rowColor = Color.FromArgb(235, 245, 255);
                                break;
                            case "insert_operation":
                            case "append_operation":
                                type = "新增指令";
                                location = $"步骤{change["stepIndex"]?.Value<int>() ?? 0}/指令{change["opIndex"]?.Value<int>() ?? 0}";
                                obj = $"{change["name"]?.Value<string>() ?? ""}({change["operaType"]?.Value<string>() ?? ""})";
                                rowColor = Color.FromArgb(235, 255, 235);
                                break;
                            case "delete_operation":
                                type = "删除指令";
                                location = $"步骤{change["oldStepIndex"]?.Value<int>() ?? 0}/指令{change["oldOpIndex"]?.Value<int>() ?? 0}";
                                obj = $"{change["name"]?.Value<string>() ?? ""}({change["operaType"]?.Value<string>() ?? ""})";
                                rowColor = Color.FromArgb(255, 235, 235);
                                break;
                            case "move_operation":
                                type = "移动指令";
                                location = $"{change["oldStepIndex"]?.Value<int>() ?? 0}-{change["oldOpIndex"]?.Value<int>() ?? 0} → {change["newStepIndex"]?.Value<int>() ?? 0}-{change["newOpIndex"]?.Value<int>() ?? 0}";
                                obj = $"{change["name"]?.Value<string>() ?? ""}({change["operaType"]?.Value<string>() ?? ""})";
                                rowColor = Color.FromArgb(235, 245, 255);
                                break;
                            case "goto_rewrite":
                                type = "跳转重写";
                                location = $"重写{change["rewrittenCount"]?.Value<int>() ?? 0}/失效{change["invalidatedCount"]?.Value<int>() ?? 0}";
                                break;
                            case "process.delete":
                                type = "删除流程";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = "流程";
                                oldVal = obj;
                                newVal = "已删除";
                                rowColor = Color.FromArgb(255, 235, 235);
                                break;
                            case "process.create":
                                type = "创建流程";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = "结构";
                                newVal = $"{change["stepCount"]?.Value<int>() ?? 0}步骤 / {change["operationCount"]?.Value<int>() ?? 0}指令";
                                rowColor = Color.FromArgb(235, 255, 235);
                                break;
                            case "process.replace":
                                type = "替换流程";
                                location = $"流程{change["procIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = "完整结构";
                                oldVal = change["oldName"]?.Value<string>() ?? "";
                                newVal = $"{change["stepCount"]?.Value<int>() ?? 0}步骤 / {change["operationCount"]?.Value<int>() ?? 0}指令";
                                rowColor = Color.FromArgb(255, 248, 235);
                                break;
                            case "process.modify":
                                type = "修改流程";
                                location = $"流程{change["procIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = "动作完成后结构";
                                newVal = $"{change["stepCount"]?.Value<int>() ?? 0}步骤 / {change["operationCount"]?.Value<int>() ?? 0}指令";
                                rowColor = Color.FromArgb(255, 248, 235);
                                break;
                            case "variable.create":
                                type = "创建变量";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = change["valueType"]?.Value<string>() ?? "";
                                newVal = FormatJsonValue(change["newValue"]);
                                rowColor = Color.FromArgb(235, 255, 235);
                                break;
                            case "variable.update":
                                type = "更新变量";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = change["valueType"]?.Value<string>() ?? "";
                                oldVal = FormatJsonValue(change["oldValue"]);
                                newVal = FormatJsonValue(change["newValue"]);
                                rowColor = Color.FromArgb(255, 248, 235);
                                break;
                        }

                        int rowIndex = dgv.Rows.Add(type, location, obj, field, oldVal, newVal);
                        dgv.Rows[rowIndex].DefaultCellStyle.BackColor = rowColor;
                    }
                }

                // 消息区
                TextBox txtMessages = new TextBox
                {
                    Dock = hasChanges ? DockStyle.Bottom : DockStyle.Fill,
                    Height = hasChanges ? 82 : 0,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    ReadOnly = true,
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(37, 52, 73),
                    Font = new Font("微软雅黑", 10F),
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(10)
                };
                if (messages != null && messages.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var msg in messages)
                    {
                        sb.AppendLine(msg.ToString());
                    }
                    txtMessages.Text = sb.ToString().TrimEnd();
                }

                // 按钮区
                Panel btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Color.White };
                Button btnReject = new Button
                {
                    Text = "取消",
                    DialogResult = DialogResult.No,
                    Size = new Size(96, 36),
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(63, 76, 95),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 9F),
                    Anchor = AnchorStyles.Right
                };
                btnReject.FlatAppearance.BorderColor = Color.FromArgb(204, 213, 224);
                Button btnConfirm = new Button
                {
                    Text = "确认并继续",
                    DialogResult = DialogResult.Yes,
                    Size = new Size(128, 36),
                    BackColor = Color.FromArgb(31, 111, 82),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                    Anchor = AnchorStyles.Right
                };
                btnConfirm.FlatAppearance.BorderSize = 0;
                btnPanel.Controls.Add(btnConfirm);
                btnPanel.Controls.Add(btnReject);

                // 按顺序添加控件（WinForms docking: 后添加的先停靠）
                if (hasChanges)
                {
                    dlg.Controls.Add(dgv);
                }
                dlg.Controls.Add(txtMessages);
                dlg.Controls.Add(btnPanel);
                dlg.Controls.Add(infoPanel);
                dlg.Controls.Add(headerPanel);

                dlg.Resize += (s, e) =>
                {
                    btnConfirm.Location = new Point(btnPanel.Width - btnConfirm.Width - 18, 13);
                    btnReject.Location = new Point(btnConfirm.Left - btnReject.Width - 10, 13);
                };
                // 初始定位按钮
                dlg.Shown += (s, e) =>
                {
                    btnConfirm.Location = new Point(btnPanel.Width - btnConfirm.Width - 18, 13);
                    btnReject.Location = new Point(btnConfirm.Left - btnReject.Width - 10, 13);
                    dlg.BringToFront();
                    dlg.Activate();
                };

                return dlg.ShowDialog(this);
            }
        }

        // 工具调用权限对话框：展示工具名和参数，让用户决定是否允许执行。
        private DialogResult ShowPermissionApprovalDialog(string toolName, string toolTitle, JObject arguments)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "EW-AI 工具调用确认";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Width = 600;
                dlg.Height = 400;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = Color.White;
                dlg.Font = new Font("微软雅黑", 9F);

                // 标题栏
                Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(50, 90, 160) };
                headerPanel.Controls.Add(new Label
                {
                    Text = "  EW-AI 请求执行工具",
                    Font = new Font("微软雅黑", 11F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                });

                // 工具信息
                Panel infoPanel = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(12, 8, 12, 8) };
                Label lblToolName = new Label
                {
                    Text = $"工具：{toolName}",
                    Font = new Font("Consolas", 10F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(40, 50, 70),
                    Dock = DockStyle.Top,
                    Height = 22
                };
                Label lblToolTitle = new Label
                {
                    Text = string.IsNullOrWhiteSpace(toolTitle) ? "" : $"说明：{toolTitle}",
                    Font = new Font("微软雅黑", 9F),
                    ForeColor = Color.FromArgb(90, 98, 108),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                infoPanel.Controls.Add(lblToolTitle);
                infoPanel.Controls.Add(lblToolName);

                // 参数表格
                DataGridView dgv = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    ReadOnly = true,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    BackgroundColor = Color.White,
                    BorderStyle = BorderStyle.None,
                    ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                    {
                        BackColor = Color.FromArgb(240, 243, 247),
                        Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                        Alignment = DataGridViewContentAlignment.MiddleCenter
                    },
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Font = new Font("Consolas", 9F),
                        WrapMode = DataGridViewTriState.True
                    },
                    RowHeadersVisible = false,
                    GridColor = Color.FromArgb(220, 225, 230)
                };
                dgv.Columns.Add("colKey", "参数名");
                dgv.Columns.Add("colVal", "值");
                dgv.Columns["colKey"].DefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);

                if (arguments != null)
                {
                    FlattenArguments(dgv, arguments, "");
                }

                // 按钮区
                Panel btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Color.FromArgb(244, 247, 250) };
                Button btnReject = new Button
                {
                    Text = "✗ 拒绝",
                    DialogResult = DialogResult.No,
                    Size = new Size(100, 32),
                    BackColor = Color.FromArgb(208, 60, 60),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 9F),
                    Anchor = AnchorStyles.Right
                };
                btnReject.FlatAppearance.BorderSize = 0;
                Button btnAllow = new Button
                {
                    Text = "✓ 允许执行",
                    DialogResult = DialogResult.Yes,
                    Size = new Size(120, 32),
                    BackColor = Color.FromArgb(35, 134, 54),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                    Anchor = AnchorStyles.Right
                };
                btnAllow.FlatAppearance.BorderSize = 0;
                btnPanel.Controls.Add(btnAllow);
                btnPanel.Controls.Add(btnReject);

                dlg.Controls.Add(dgv);
                dlg.Controls.Add(btnPanel);
                dlg.Controls.Add(infoPanel);
                dlg.Controls.Add(headerPanel);

                dlg.Resize += (s, e) =>
                {
                    btnAllow.Location = new Point(btnPanel.Width - btnAllow.Width - 16, 7);
                    btnReject.Location = new Point(btnAllow.Left - btnReject.Width - 10, 7);
                };
                dlg.Shown += (s, e) =>
                {
                    btnAllow.Location = new Point(btnPanel.Width - btnAllow.Width - 16, 7);
                    btnReject.Location = new Point(btnAllow.Left - btnReject.Width - 10, 7);
                };

                return dlg.ShowDialog(this);
            }
        }

        // 将嵌套的 JSON 参数展平到 DataGridView 中（递归）。
        private static void FlattenArguments(DataGridView dgv, JToken token, string prefix)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    string key = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + "." + prop.Name;
                    if (prop.Value is JObject || prop.Value is JArray)
                    {
                        FlattenArguments(dgv, prop.Value, key);
                    }
                    else
                    {
                        dgv.Rows.Add(key, FormatJsonValue(prop.Value));
                    }
                }
            }
            else if (token is JArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    FlattenArguments(dgv, arr[i], $"{prefix}[{i}]");
                }
            }
            else
            {
                dgv.Rows.Add(prefix, FormatJsonValue(token));
            }
        }

        // 格式化 JSON 值为可读字符串。
        private static string FormatJsonValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "—";
            if (token.Type == JTokenType.String) return token.Value<string>() ?? "";
            if (token.Type == JTokenType.Integer) return token.Value<long>().ToString();
            if (token.Type == JTokenType.Float) return token.Value<double>().ToString("G");
            if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "true" : "false";
            return token.ToString(Newtonsoft.Json.Formatting.None);
        }

        #endregion

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
                    Color.DarkRed);
                return BuildPermissionCancelled();
            }

            if (autoApproveMode)
            {
                // 自动批准模式：把工具调用信息显示到聊天区，让用户看到批准了什么
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
                AppendConversation("工具", item.Text, Color.FromArgb(96, 62, 14));
            }
            else if (string.Equals(item.Kind, "error", StringComparison.Ordinal)
                || string.Equals(item.Kind, "stderr", StringComparison.Ordinal)
                || string.Equals(item.Kind, "exit", StringComparison.Ordinal))
            {
                AppendConversation("系统", item.Text, Color.DarkRed);
            }

            // ChangeSet 预演确认：tool_result 才包含 Bridge 生成的 previewId，
            // tool_call 事件只有输入参数。
            if (!replayingTaskEvents && string.Equals(item.Kind, "tool_result", StringComparison.Ordinal))
            {
                TryPromptPreviewConfirmation(item.Raw);
            }
        }

        // 流程读取直接使用工具返回；提交结果按 affectedProcesses 回读当前内存中的正式对象。
        private void CaptureFlowVisualizationEvent(GooseAcpEvent item)
        {
            JObject update = item?.Raw?["params"]?["update"] as JObject
                ?? item?.Raw?["params"] as JObject;
            string callId = update?["toolCallId"]?.Value<string>();
            if (string.Equals(item?.Kind, "tool_call", StringComparison.Ordinal))
            {
                JObject toolCall = update?["_meta"]?["goose"]?["toolCall"] as JObject;
                string extensionName = toolCall?["extensionName"]?.Value<string>();
                string toolName = toolCall?["toolName"]?.Value<string>();
                int toolPrefixSeparator = toolName?.LastIndexOf("__", StringComparison.Ordinal) ?? -1;
                if (toolPrefixSeparator >= 0)
                {
                    toolName = toolName.Substring(toolPrefixSeparator + 2);
                }
                bool capturesFlow = string.Equals(extensionName, "automation", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(toolName, "get_proc_overview", StringComparison.Ordinal)
                        || string.Equals(toolName, "get_proc_detail", StringComparison.Ordinal)
                        || string.Equals(toolName, "apply_change_set", StringComparison.Ordinal));
                if (string.IsNullOrWhiteSpace(callId) || !capturesFlow)
                {
                    return;
                }
                flowVisualizationCallIds.Add(callId);
                return;
            }
            if (!string.Equals(item?.Kind, "tool_result", StringComparison.Ordinal))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(callId) || !flowVisualizationCallIds.Remove(callId))
            {
                return;
            }

            try
            {
                string resultText = ExtractToolResultText(item.Raw);
                JObject result = string.IsNullOrWhiteSpace(resultText) ? null : JObject.Parse(resultText);
                if (result?["ok"]?.Value<bool>() != true || result["data"] is not JObject data)
                {
                    return;
                }

                string resultType = result["type"]?.Value<string>();
                if (string.Equals(resultType, "proc.overview", StringComparison.Ordinal)
                    || string.Equals(resultType, "proc.detail", StringComparison.Ordinal))
                {
                    UpsertFlowVisualizationProcess(BuildReadFlowVisualization(data));
                    return;
                }

                if (!string.Equals(resultType, "change_set.apply", StringComparison.Ordinal)
                    || data["committed"]?.Value<bool>() != true)
                {
                    return;
                }

                flowVisualizationProcesses.RemoveAll();
                foreach (JObject affected in (data["affectedProcesses"] as JArray ?? new JArray())
                    .OfType<JObject>())
                {
                    int procIndex = affected["procIndex"]?.Value<int?>() ?? -1;
                    JObject process = BuildCommittedFlowVisualization(procIndex);
                    if (process != null)
                    {
                        UpsertFlowVisualizationProcess(process);
                    }
                }
            }
            catch (Exception ex)
            {
                // 只有 Automation 的流程工具进入这里；其返回形状异常需要记录，但不得中断工具主链路。
                SF.DR?.Logger?.Log($"AI流程可视化解析失败:{ex}", LogLevel.Error);
            }
        }

        private static JObject BuildCommittedFlowVisualization(int procIndex)
        {
            if (SF.frmProc == null || procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                return null;
            }
            Proc proc = SF.frmProc.procsList[procIndex];
            if (proc == null)
            {
                return null;
            }
            var steps = new JArray();
            foreach (Step step in proc.steps ?? new List<Step>())
            {
                var operations = new JArray();
                foreach (OperationType operation in step?.Ops ?? new List<OperationType>())
                {
                    if (operation == null) continue;
                    operations.Add(new JObject
                    {
                        ["kind"] = "platform.operation",
                        ["name"] = operation.Name ?? string.Empty,
                        ["operaType"] = operation.OperaType ?? string.Empty,
                        ["summary"] = operation.OperaType ?? string.Empty
                    });
                }
                steps.Add(new JObject
                {
                    ["name"] = step?.Name ?? "未命名步骤",
                    ["operations"] = operations
                });
            }
            return new JObject
            {
                ["action"] = "committed",
                ["procIndex"] = procIndex,
                ["name"] = proc.head?.Name ?? "未命名流程",
                ["state"] = SF.DR?.GetSnapshot(procIndex)?.State.ToString() ?? string.Empty,
                ["steps"] = steps
            };
        }

        private void UpsertFlowVisualizationProcess(JObject process)
        {
            if (process == null)
            {
                return;
            }
            int? procIndex = process["procIndex"]?.Value<int?>();
            string processName = process["name"]?.Value<string>();
            for (int i = 0; i < flowVisualizationProcesses.Count; i++)
            {
                if (flowVisualizationProcesses[i] is not JObject existing)
                {
                    continue;
                }
                bool sameIndex = procIndex.HasValue && existing["procIndex"]?.Value<int?>() == procIndex;
                bool sameInspectedName = string.Equals(process["action"]?.Value<string>(), "inspect", StringComparison.Ordinal)
                    && string.Equals(existing["action"]?.Value<string>(), "inspect", StringComparison.Ordinal)
                    && string.Equals(existing["name"]?.Value<string>(), processName, StringComparison.Ordinal);
                if (sameIndex || sameInspectedName)
                {
                    flowVisualizationProcesses[i] = process.DeepClone();
                    return;
                }
            }
            flowVisualizationProcesses.Add(process.DeepClone());
        }

        private static JObject BuildReadFlowVisualization(JObject data)
        {
            var process = new JObject
            {
                ["action"] = "inspect",
                ["procIndex"] = data["procIndex"]?.DeepClone(),
                ["name"] = data["name"]?.Value<string>() ?? "未命名流程",
                ["state"] = data["state"]?.Value<string>() ?? string.Empty
            };
            var visualSteps = new JArray();
            foreach (JObject step in (data["steps"] as JArray ?? new JArray()).OfType<JObject>())
            {
                int stepIndex = step["stepIndex"]?.Value<int?>() ?? visualSteps.Count;
                var visualOperations = new JArray();
                foreach (JObject operation in (step["ops"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    visualOperations.Add(new JObject
                    {
                        ["kind"] = "platform.operation",
                        ["name"] = operation["name"]?.Value<string>() ?? string.Empty,
                        ["operaType"] = operation["operaType"]?.Value<string>() ?? string.Empty,
                        ["summary"] = operation["summary"]?.Value<string>()
                            ?? operation["operaType"]?.Value<string>()
                            ?? string.Empty
                    });
                }
                visualSteps.Add(new JObject
                {
                    ["key"] = "步骤" + stepIndex,
                    ["name"] = step["name"]?.Value<string>() ?? "未命名步骤",
                    ["operations"] = visualOperations
                });
            }
            process["steps"] = visualSteps;
            return process;
        }


        // 工具调用/结果紧凑单行显示；连续相同项合并计数，避免重复占用纵向空间。
        private void AppendToolEntry(string marker, string text, JObject raw)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed)
            {
                return;
            }
            bool isCall = string.Equals(marker, "call", StringComparison.Ordinal);
            string normalizedText = string.IsNullOrWhiteSpace(text) ? "无摘要" : LocalizeAutomationToolText(text.Trim());
            bool isSystemError = !isCall && (normalizedText.StartsWith("×", StringComparison.Ordinal)
                || string.Equals(
                    raw?["params"]?["update"]?["status"]?.Value<string>(),
                    "failed",
                    StringComparison.OrdinalIgnoreCase));
            bool isBusinessFailure = false;
            string resultLabel = isSystemError ? "异常" : "结果";
            if (!isCall)
            {
                try
                {
                    JObject result = JObject.Parse(ExtractToolResultText(raw) ?? string.Empty);
                    bool failed = result["ok"]?.Value<bool?>() == false;
                    if (failed)
                    {
                        string code = result["errorCode"]?.Value<string>() ?? "工具调用失败";
                        string detail = result["recovery"]?["validationError"]?.Value<string>()
                            ?? result["message"]?.Value<string>();
                        normalizedText = string.IsNullOrWhiteSpace(detail)
                            ? code
                            : code + " · " + detail;
                        isSystemError = IsSystemToolFailure(code);
                        isBusinessFailure = !isSystemError;
                        resultLabel = isSystemError ? "异常" : ResolveBusinessFailureLabel(code);
                    }
                    else
                    {
                        // MCP/Bridge 已正常返回结果；不能沿用 ACP 摘要中的“×”误报系统异常。
                        isSystemError = false;
                        isBusinessFailure = false;
                        resultLabel = "结果";
                    }
                }
                catch (JsonReaderException)
                {
                }
            }
            string cls = isCall
                ? "tool-call"
                : isSystemError
                    ? "tool-error"
                    : isBusinessFailure ? "tool-business-failure" : "tool-result";
            string display = isCall ? "调用" : resultLabel;
            string callId = raw?["params"]?["update"]?["toolCallId"]?.Value<string>()
                ?? raw?["params"]?["toolCallId"]?.Value<string>()
                ?? string.Empty;
            string html = "<div class=\"tool-entry " + cls + "\" title=\"" + HtmlEncode(normalizedText) + "\">"
                + "<span class=\"tool-entry-label\">" + display + "</span>"
                + "<span class=\"tool-entry-text\">" + HtmlEncode(normalizedText) + "</span>"
                + "<span class=\"tool-entry-count\" style=\"display:none\"></span></div>";
            string boxId = EnsureThinkingBox();
            string htmlJson = JsonConvert.SerializeObject(html);
            string signatureJson = JsonConvert.SerializeObject(marker + "\n" + normalizedText);
            string callIdJson = JsonConvert.SerializeObject(callId);
            string js = "var box=document.getElementById('" + boxId + "');if(box){var callId=" + callIdJson + ";var sig=" + signatureJson
                + ";var paired=null;if(callId){var entries=box.querySelectorAll('.tool-entry');for(var i=entries.length-1;i>=0;i--){if(entries[i].dataset.callId===callId){paired=entries[i];break;}}}"
                + "if(paired&&" + (isCall ? "false" : "true") + "){paired.classList.remove('tool-call');paired.classList.add('" + cls + "');paired.title=paired.title+' | '+"
                + JsonConvert.SerializeObject(normalizedText) + ";var label=paired.querySelector('.tool-entry-label');if(label){label.textContent='" + (isSystemError || isBusinessFailure ? resultLabel : "完成") + "';}var value=paired.querySelector('.tool-entry-text');if(value){value.textContent=value.textContent+'  →  '+"
                + JsonConvert.SerializeObject(normalizedText) + ";}}else{var entries2=box.querySelectorAll('.tool-entry');var last=entries2.length?entries2[entries2.length-1]:null;if(last&&last.dataset.signature===sig){var count=parseInt(last.dataset.count||'1',10)+1;last.dataset.count=count;var badge=last.querySelector('.tool-entry-count');if(badge){badge.textContent='×'+count;badge.style.display='inline-block';}}else{box.insertAdjacentHTML('beforeend',"
                + htmlJson + ");var added=box.lastElementChild;if(added){added.dataset.signature=sig;added.dataset.count='1';added.dataset.callId=callId;}}}scrollThinkingBoxToBottom('" + boxId + "');}";
            EnqueueScript(js);
        }

        private static bool IsSystemToolFailure(string errorCode)
        {
            if (string.IsNullOrWhiteSpace(errorCode))
            {
                return false;
            }
            return errorCode.StartsWith("MCP_", StringComparison.OrdinalIgnoreCase)
                || errorCode.StartsWith("TRANSPORT_", StringComparison.OrdinalIgnoreCase)
                || errorCode.StartsWith("INTERNAL_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "UNHANDLED_EXCEPTION", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "TOOL_INVOCATION_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "INVALID_RESPONSE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "EMPTY_RESPONSE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "BRIDGE_ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "REQUEST_ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "TIMEOUT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "BRIDGE_NOT_READY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "BRIDGE_STOPPING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "RUNTIME_NOT_READY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "STORE_UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "PROCS_UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "SAVE_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "COMMIT_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "CHANGE_SET_COMMIT_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "CHANGE_SET_ROLLBACK_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "VARIABLE_COMMIT_FAILED", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveBusinessFailureLabel(string errorCode)
        {
            if (!string.IsNullOrWhiteSpace(errorCode)
                && (errorCode.EndsWith("_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(errorCode, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)))
            {
                return "未找到";
            }
            if (!string.IsNullOrWhiteSpace(errorCode)
                && (errorCode.IndexOf("INVALID", StringComparison.OrdinalIgnoreCase) >= 0
                    || errorCode.IndexOf("VALIDATE", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "未通过";
            }
            return "未完成";
        }

        private static string LocalizeAutomationToolText(string value)
        {
            return (value ?? string.Empty)
                .Replace("get_native_operation_schemas", "获取原生指令结构")
                .Replace("get_semantic_operation_schema", "获取语义指令结构")
                .Replace("preview_change_set", "预演变更阶段")
                .Replace("apply_change_set", "提交配置阶段")
                .Replace("discard_change_set_preview", "结束变更预演")
                .Replace("get_proc_overview", "获取流程概览")
                .Replace("get_proc_detail", "获取流程详情")
                .Replace("list_procs", "列出流程")
                .Replace("get_variable_by_name", "按名称获取变量")
                .Replace("get_variable_by_index", "按索引获取变量")
                .Replace("set_variable_by_name", "按名称设置变量")
                .Replace("set_variable_by_index", "按索引设置变量")
                .Replace("list_variables", "读取变量表")
                .Replace("add_variable", "新增变量")
                .Replace("update_variable", "修改变量")
                .Replace("delete_variable", "删除变量")
                .Replace("get_variable", "获取变量")
                .Replace("search_variables", "搜索变量")
                .Replace("search_data_struct_items", "搜索数据结构项")
                .Replace("get_data_struct", "获取数据结构")
                .Replace("search_alarms", "搜索报警配置")
                .Replace("get_alarm", "获取报警配置")
                .Replace("get_snapshot", "获取运行快照")
                .Replace("run_proc_test", "限时测试流程")
                .Replace("wait_for_proc_state", "等待流程状态")
                .Replace("start_proc", "启动流程")
                .Replace("stop_proc", "停止流程");
        }

        private static string BuildAutomationFlowCardsHtml(string visualizationJson)
        {
            if (string.IsNullOrWhiteSpace(visualizationJson))
            {
                return string.Empty;
            }
            try
            {
                JArray processes = JArray.Parse(visualizationJson);
                if (processes.Count == 0)
                {
                    return string.Empty;
                }

                var html = new StringBuilder();
                html.Append("<div class=\"automation-flow-visual\"><div class=\"flow-visual-title\"><span>流程结构</span><span class=\"flow-badge\">")
                    .Append(processes.Count).Append(" 个流程</span></div>");
                foreach (JObject process in processes.OfType<JObject>())
                {
                    AppendProcessFlowHtml(html, process);
                }
                html.Append("</div>");
                return html.ToString();
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static void AppendProcessFlowHtml(StringBuilder html, JObject process)
        {
            string processName = process["name"]?.Value<string>() ?? "未命名流程";
            string action = process["action"]?.Value<string>();
            JArray steps = process["steps"] as JArray ?? new JArray();
            int operationCount = steps.OfType<JObject>()
                .Sum(step => (step["operations"] as JArray)?.Count ?? 0);
            bool hasBackEdge = HasBackEdge(steps);
            string actionText;
            switch (action)
            {
                case "replace": actionText = "替换"; break;
                case "inspect": actionText = "现有"; break;
                case "committed": actionText = "已提交"; break;
                default: actionText = "新建"; break;
            }

            html.Append("<div class=\"flow-process\"><div class=\"flow-process-head\"><span class=\"flow-process-name\">")
                .Append(HtmlEncode(processName)).Append("</span><span class=\"flow-badge\">")
                .Append(actionText)
                .Append("</span><span class=\"flow-badge\">").Append(steps.Count).Append(" 步骤 · ")
                .Append(operationCount).Append(" 指令</span>");
            if (hasBackEdge)
            {
                html.Append("<span class=\"flow-badge loop\">含回环</span>");
            }
            html.Append("</div>");
            if (steps.Count == 0)
            {
                html.Append("<div class=\"flow-empty\">没有步骤</div></div>");
                return;
            }

            html.Append(steps.Count == 1
                ? "<div class=\"flow-track single-step\">"
                : "<div class=\"flow-track\">");
            int stepIndex = 0;
            foreach (JObject step in steps.OfType<JObject>())
            {
                if (stepIndex > 0)
                {
                    html.Append("<div class=\"flow-arrow\">→</div>");
                }
                AppendStepFlowHtml(html, step, stepIndex++);
            }
            html.Append("</div></div>");
        }

        private static void AppendStepFlowHtml(StringBuilder html, JObject step, int stepIndex)
        {
            string stepName = step["name"]?.Value<string>() ?? "未命名步骤";
            string stepKey = step["key"]?.Value<string>() ?? string.Empty;
            JArray operations = step["operations"] as JArray ?? new JArray();
            html.Append("<div class=\"flow-step\"><div class=\"flow-step-head\"><span class=\"flow-step-index\">")
                .Append(stepIndex + 1).Append("</span><span class=\"flow-step-name\">")
                .Append(HtmlEncode(stepName)).Append("</span>");
            if (!string.IsNullOrWhiteSpace(stepKey))
            {
                html.Append("<span class=\"flow-step-key\">").Append(HtmlEncode(stepKey)).Append("</span>");
            }
            html.Append("</div><div class=\"flow-ops\">");
            if (operations.Count == 0)
            {
                html.Append("<div class=\"flow-empty\">没有指令</div>");
            }
            int operationIndex = 0;
            foreach (JObject operation in operations.OfType<JObject>())
            {
                AppendOperationFlowHtml(html, operation, operationIndex++);
            }
            html.Append("</div></div>");
        }

        private static void AppendOperationFlowHtml(StringBuilder html, JObject operation, int operationIndex)
        {
            string kind = operation["kind"]?.Value<string>()
                ?? (operation["operaType"] == null ? "unknown" : "platform.operation");
            string name = operation["name"]?.Value<string>();
            string summary = BuildOperationSummary(operation, kind);
            html.Append("<div class=\"flow-op\" title=\"").Append(HtmlEncode(kind))
                .Append("\"><div class=\"flow-op-line\"><span class=\"flow-op-index\">")
                .Append(operationIndex + 1).Append("</span><span class=\"flow-op-text\">");
            if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, summary, StringComparison.Ordinal))
            {
                html.Append(HtmlEncode(name)).Append(" · ");
            }
            html.Append(HtmlEncode(summary)).Append("</span></div>");

            if (operation["whenTrue"] is JObject || operation["whenFalse"] is JObject)
            {
                AppendFlowPath(html, "满足", operation["whenTrue"] as JObject, false);
                AppendFlowPath(html, "不满足", operation["whenFalse"] as JObject, true);
            }
            else if (string.Equals(kind, "flow.goto", StringComparison.Ordinal))
            {
                AppendFlowPath(html, "跳转", operation["target"] as JObject, true);
            }
            html.Append("</div>");
        }

        private static string BuildOperationSummary(JObject operation, string kind)
        {
            string explicitSummary = operation["summary"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(explicitSummary))
            {
                return explicitSummary;
            }
            switch (kind)
            {
                case "wait":
                    return $"等待 {operation["milliseconds"]?.ToString(Formatting.None) ?? "?"} ms";
                case "variable.add":
                    return $"{operation["variable"]?.Value<string>() ?? "变量"} + {operation["amount"]?.ToString(Formatting.None) ?? "?"}";
                case "variable.set":
                    return $"{operation["variable"]?.Value<string>() ?? "变量"} = {operation["value"]?.Value<string>() ?? ""}";
                case "branch.number_range":
                    string left = operation["includeBounds"]?.Value<bool>() == false ? "(" : "[";
                    string right = operation["includeBounds"]?.Value<bool>() == false ? ")" : "]";
                    return $"判断 {operation["variable"]?.Value<string>() ?? "变量"} ∈ {left}{operation["min"]?.ToString(Formatting.None) ?? "?"}, {operation["max"]?.ToString(Formatting.None) ?? "?"}{right}";
                case "popup.message":
                    return $"显示固定文本“{operation["message"]?.Value<string>() ?? ""}”{BuildAutoCloseSuffix(operation)}";
                case "popup.variable":
                    return $"显示变量“{operation["variable"]?.Value<string>() ?? ""}”当前值{BuildAutoCloseSuffix(operation)}";
                case "flow.goto":
                    return "跳转到指定位置";
                case "flow.end":
                    return "正常结束当前流程";
                case "io.write":
                    return $"输出 {operation["io"]?.Value<string>() ?? "IO"} = {operation["state"]?.Value<bool>()}";
                case "io.wait":
                    return $"等待 {operation["io"]?.Value<string>() ?? "IO"} = {operation["state"]?.Value<bool>()}";
                case "process.control":
                    return $"{operation["action"]?.Value<string>() ?? "控制"}流程 {operation["process"]?.Value<string>() ?? ""}";
                case "process.wait":
                    return $"等待流程 {operation["process"]?.Value<string>() ?? ""} 到达 {operation["expectedState"]?.Value<string>() ?? "目标状态"}";
                default:
                    return operation["operaType"]?.Value<string>()
                        ?? (string.IsNullOrWhiteSpace(operation["name"]?.Value<string>())
                            ? kind : operation["name"]?.Value<string>());
            }
        }

        private static string BuildAutoCloseSuffix(JObject operation)
        {
            int? autoCloseMs = operation["autoCloseMs"]?.Value<int?>();
            return autoCloseMs > 0 ? $" · {autoCloseMs} ms后关闭" : string.Empty;
        }

        private static void AppendFlowPath(StringBuilder html, string label, JObject target, bool alternative)
        {
            if (target == null)
            {
                return;
            }
            string step = target["step"]?.Value<string>() ?? "?";
            int operation = target["operation"]?.Value<int?>() ?? 0;
            html.Append("<div class=\"flow-paths\"><span class=\"flow-path")
                .Append(alternative ? " false" : string.Empty).Append("\">")
                .Append(HtmlEncode(label)).Append(" → ").Append(HtmlEncode(step))
                .Append(" · ").Append(operation + 1).Append("</span></div>");
        }

        private static bool HasBackEdge(JArray steps)
        {
            var indexes = steps.OfType<JObject>()
                .Select((step, index) => new { Key = step["key"]?.Value<string>(), Index = index })
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .ToDictionary(item => item.Key, item => item.Index, StringComparer.Ordinal);
            int currentIndex = 0;
            foreach (JObject step in steps.OfType<JObject>())
            {
                foreach (JObject operation in (step["operations"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    foreach (string property in new[] { "target", "whenTrue", "whenFalse" })
                    {
                        string targetStep = operation[property]?["step"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(targetStep)
                            && indexes.TryGetValue(targetStep, out int targetIndex)
                            && targetIndex <= currentIndex)
                        {
                            return true;
                        }
                    }
                }
                currentIndex++;
            }
            return false;
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
                + "<div class=\"toggle-bar\" onclick=\"toggleThinkingBox('" + boxId + "')\">思考过程（推理与工具，点击折叠/展开）</div>"
                + "</div>";
            EnqueueAppendHtml(html);
            return boxId;
        }

        // 将 HTML 追加到当前思维链窗口内部，并自动滚动到底部。
        private void AppendToThinkingBox(string html)
        {
            string boxId = EnsureThinkingBox();
            string htmlJson = JsonConvert.SerializeObject(html);
            string js = "var box=document.getElementById('" + boxId + "');if(box){box.insertAdjacentHTML('beforeend'," + htmlJson + ");scrollThinkingBoxToBottom('" + boxId + "');}";
            EnqueueScript(js);
        }

        // 折叠当前思维链窗口（PromptAsync 结束后调用）。
        private void CollapseThinkingBox()
        {
            if (currentThinkingBoxId == null)
            {
                return;
            }
            TimeSpan elapsed = promptStartedAt == default(DateTime) ? TimeSpan.Zero : DateTime.Now - promptStartedAt;
            string elapsedText = elapsed.TotalMinutes >= 1
                ? $"{(int)elapsed.TotalMinutes}分{elapsed.Seconds}秒"
                : $"{Math.Max(1, (int)Math.Ceiling(elapsed.TotalSeconds))}秒";
            string js = "var box=document.getElementById('" + currentThinkingBoxId + "');if(box){box.style.maxHeight='32px';box.scrollTop=0;box.classList.add('collapsed');var bar=box.querySelector('.toggle-bar');if(bar){bar.textContent='思考完成 · 已处理 "
                + elapsedText + "';}}";
            EnqueueScript(js);
            currentThinkingBoxId = null;
        }

        // ACP 的 agent_message_chunk 未提前区分分析段和最终段，生成期间先放在稳定的思考窗口；
        // 后续紧接工具调用时原地保留，轮次结束时才把最后一段转为最终回答。
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
                AppendToThinkingBox("<div class=\"analysis-segment streaming-segment\" id=\""
                    + streamingDivId + "\">" + StreamingTextToHtml(streamingMarkdown.ToString()) + "</div>");
            }
            else
            {
                streamingMarkdown.Append(text);
                // 上一帧尚未完成时跳过中间帧，最终帧仍由 FinishStreaming 提交，
                // 避免长回答反复全量渲染 Markdown 造成 WebView2 脚本队列积压。
                if ((DateTime.Now - lastStreamRender).TotalMilliseconds >= 100
                    && pendingScriptTask.IsCompleted)
                {
                    lastStreamRender = DateTime.Now;
                    UpdateStreamingPreview();
                }
            }
        }

        // 结束正式助手流式段：用最终 Markdown 替换对话区中的临时 HTML。
        private void FinishStreaming()
        {
            if (!streamingAssistant)
            {
                return;
            }
            string renderedHtml = MarkdownToHtml(streamingMarkdown.ToString());
            string htmlJson = JsonConvert.SerializeObject(renderedHtml);
            string idJson = JsonConvert.SerializeObject(streamingDivId);
            string js = "var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson + ";}if(window.scrollMessagesToBottom){scrollMessagesToBottom();}";
            EnqueueScript(js);
            latestAssistantSegmentText = streamingMarkdown.ToString();
            latestAssistantSegmentDivId = streamingDivId;
            streamingAssistant = false;
            streamingDivId = null;
        }

        private void DemoteLatestAssistantSegmentToThinking()
        {
            if (string.IsNullOrWhiteSpace(latestAssistantSegmentText))
            {
                return;
            }
            latestAssistantSegmentText = null;
            latestAssistantSegmentDivId = null;
        }

        private string PromoteLatestAssistantSegment(string fallbackText, string visualizationJson)
        {
            string finalText = string.IsNullOrWhiteSpace(latestAssistantSegmentText)
                ? fallbackText
                : latestAssistantSegmentText;
            if (!string.IsNullOrWhiteSpace(latestAssistantSegmentDivId))
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                string finalHtml = BuildAutomationFlowCardsHtml(visualizationJson) + MarkdownToHtml(finalText);
                string messageHtml = "<div class=\"msg assistant\"><div class=\"msg-head\">"
                    + "<img class=\"avatar avatar-image\" src=\"" + ChickAvatarDataUri
                    + "\" alt=\"AI\" title=\"EW-AI " + HtmlEncode(time) + "\">"
                    + "<span class=\"msg-time\">" + HtmlEncode(time) + "</span>"
                    + CopyButtonHtml + "</div><div class=\"content\">" + finalHtml + "</div></div>";
                EnqueueScript("var el=document.getElementById("
                    + JsonConvert.SerializeObject(latestAssistantSegmentDivId)
                    + ");var box=el&&el.closest('.thinking-box');if(el){el.remove();}"
                    + "var messages=document.getElementById('messages'),finalMessage=null;if(messages){messages.insertAdjacentHTML('beforeend',"
                    + JsonConvert.SerializeObject(messageHtml) + ");finalMessage=messages.lastElementChild;}"
                    + "if(box&&box.querySelectorAll(':scope > :not(.toggle-bar)').length===0){box.remove();}"
                    + "if(window.revealFinalAnswer){revealFinalAnswer(finalMessage);}"
                    + "if(window.scrollMessagesToBottom){scrollMessagesToBottom();}");
            }
            else if (!string.IsNullOrWhiteSpace(finalText))
            {
                AppendConversation("EW-AI", finalText, Color.FromArgb(30, 104, 74), null, visualizationJson);
                EnqueueScript("var messages=document.getElementById('messages');if(window.revealFinalAnswer&&messages){revealFinalAnswer(messages.lastElementChild);}");
            }
            latestAssistantSegmentText = null;
            latestAssistantSegmentDivId = null;
            return finalText;
        }

        // 协议显式标记的推理片段仅显示在思维窗口，不混入最终回答。
        private void AppendThoughtText(string text)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed || string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!streamingThought)
            {
                streamingThoughtSegmentIndex++;
                streamingThoughtDivId = "thinking-streaming-" + streamingThoughtSegmentIndex;
                streamingThoughtMarkdown.Clear();
                streamingThoughtMarkdown.Append(text);
                streamingThought = true;
                lastThoughtRender = DateTime.Now;
                AppendToThinkingBox("<div class=\"streaming-segment\" id=\"" + streamingThoughtDivId + "\">"
                    + StreamingTextToHtml(streamingThoughtMarkdown.ToString()) + "</div>");
                return;
            }

            streamingThoughtMarkdown.Append(text);
            if ((DateTime.Now - lastThoughtRender).TotalMilliseconds >= 100
                && pendingScriptTask.IsCompleted)
            {
                lastThoughtRender = DateTime.Now;
                UpdateThoughtPreview();
            }
        }

        private void FinishThoughtStreaming()
        {
            if (!streamingThought)
            {
                return;
            }

            string htmlJson = JsonConvert.SerializeObject(MarkdownToHtml(streamingThoughtMarkdown.ToString()));
            string idJson = JsonConvert.SerializeObject(streamingThoughtDivId);
            string boxId = currentThinkingBoxId ?? string.Empty;
            EnqueueScript("var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson
                + ";}scrollThinkingBoxToBottom('" + boxId + "');");
            streamingThought = false;
            streamingThoughtDivId = null;
        }

        private void UpdateThoughtPreview()
        {
            if (string.IsNullOrEmpty(streamingThoughtDivId))
            {
                return;
            }

            string htmlJson = JsonConvert.SerializeObject(StreamingTextToHtml(streamingThoughtMarkdown.ToString()));
            string idJson = JsonConvert.SerializeObject(streamingThoughtDivId);
            string boxId = currentThinkingBoxId ?? string.Empty;
            EnqueueScript("var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson
                + ";}scrollThinkingBoxToBottom('" + boxId + "');");
        }

        // 追加对话消息：根据 role/color 决定 CSS 类，用户消息纯文本转义，Goose 消息走 Markdown→HTML。
        private void AppendConversation(string role, string text, Color color, DateTime? messageTime = null,
            string visualizationJson = null)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed)
            {
                return;
            }
            string time = (messageTime ?? DateTime.Now).ToString("HH:mm:ss");
            string cls;
            string contentHtml;
            string avatarHtml = string.Empty;
            if (role == "用户")
            {
                cls = "msg user";
                contentHtml = HtmlEncode(text);
            }
            else if (role == "EW-AI")
            {
                cls = "msg assistant";
                contentHtml = BuildAutomationFlowCardsHtml(visualizationJson) + MarkdownToHtml(text);
                avatarHtml = "<img class=\"avatar avatar-image\" src=\"" + ChickAvatarDataUri + "\" alt=\"AI\" title=\"EW-AI " + HtmlEncode(time) + "\">";
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
                avatarHtml = "<span class=\"avatar system-avatar\" title=\"" + HtmlEncode(role) + " " + HtmlEncode(time) + "\">!</span>";
            }
            string roleHtml = string.Empty;
            string html = "<div class=\"" + cls + "\"><div class=\"msg-head\">" + avatarHtml + roleHtml + "<span class=\"msg-time\">" + HtmlEncode(time) + "</span>"
                + CopyButtonHtml + "</div>"
                + "<div class=\"content\">" + contentHtml + "</div></div>";
            EnqueueAppendHtml(html);
        }

        // 更新对话区正式回复的流式 HTML，并滚动到最新结果。
        private void UpdateStreamingPreview()
        {
            if (string.IsNullOrEmpty(streamingDivId))
            {
                return;
            }
            string html = StreamingTextToHtml(streamingMarkdown.ToString());
            string htmlJson = JsonConvert.SerializeObject(html);
            string idJson = JsonConvert.SerializeObject(streamingDivId);
            string js = "var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson + ";}if(window.scrollMessagesToBottom){scrollMessagesToBottom();}";
            EnqueueScript(js);
        }

        // 向 #messages 容器追加 HTML 片段并滚动到底部。
        private void EnqueueAppendHtml(string html)
        {
            string htmlJson = JsonConvert.SerializeObject(html);
            string js = "var box=document.getElementById('messages');"
                + "if(box){var tpl=document.createElement('template');tpl.innerHTML=" + htmlJson + ";"
                + "var incoming=tpl.content.firstElementChild;var last=box.lastElementChild;"
                + "if(incoming&&last&&incoming.classList.contains('assistant')&&last.classList.contains('assistant')){"
                + "var source=incoming.querySelector('.content');var target=last.querySelector('.content');"
                + "if(source&&target){var part=document.createElement('div');part.className='merged-part';part.innerHTML=source.innerHTML;target.appendChild(part);}"
                + "}else if(incoming){box.appendChild(incoming);}}"
                + "if(window.scrollMessagesToBottom){scrollMessagesToBottom();}";
            EnqueueScript(js);
        }

        // 串行化 ExecuteScriptAsync：通过 ContinueWith 链保证脚本按入队顺序执行（状态修改在调用前同步完成，脚本内 HTML 已捕获）。
        private void EnqueueScript(string js)
        {
            if (webViewClosing || IsDisposed || Disposing)
            {
                return;
            }
            WebView2 localWebView = webViewConversation;
            var localCoreWebView = localWebView?.CoreWebView2;
            if (localWebView == null || localWebView.IsDisposed || localCoreWebView == null)
            {
                return;
            }
            try
            {
                pendingScriptTask = (pendingScriptTask ?? Task.CompletedTask).ContinueWith(
                    async _ =>
                    {
                        try
                        {
                            if (!webViewClosing && !localWebView.IsDisposed)
                            {
                                await localCoreWebView.ExecuteScriptAsync(js);
                            }
                        }
                        catch
                        {
                            // WebView 关闭或单条脚本失败时终止本次渲染，不影响窗体退出。
                        }
                    },
                    TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
            }
            catch (InvalidOperationException)
            {
                // 窗体关闭期间 UI 同步上下文可能已终止，不再接受新脚本。
            }
        }

        // HTML 转义文本（< > & 等）。
        private static string HtmlEncode(string text)
        {
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }

        // 完整消息交给 Markdig。只预处理两类可确定的模型格式错误：粘连标题和缺失表头的表格。
        private static string MarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return string.Empty;
            }

            try
            {
                return Markdown.ToHtml(NormalizeMarkdownForRendering(markdown), markdownPipeline);
            }
            catch
            {
                return "<pre>" + HtmlEncode(markdown) + "</pre>";
            }
        }

        private static string NormalizeMarkdownForRendering(string markdown)
        {
            string normalizedMarkdown = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            normalizedMarkdown = Regex.Replace(normalizedMarkdown, @"(?m)(?<!^)(```|~~~)", "\n$1");
            normalizedMarkdown = UnwrapBareMarkdownFence(normalizedMarkdown);
            string[] lines = normalizedMarkdown.Split('\n');
            var output = new List<string>();
            bool inCodeFence = false;

            foreach (string line in lines)
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    inCodeFence = !inCodeFence;
                    output.Add(line);
                    continue;
                }

                if (inCodeFence)
                {
                    output.Add(line);
                    continue;
                }

                string normalizedLine = NormalizeMarkdownBlockMarkers(line);
                normalizedLine = NormalizeHeadingMarkers(normalizedLine);
                foreach (string logicalLine in normalizedLine.Split('\n'))
                {
                    foreach (string expandedLine in ExpandMalformedHeadingBody(logicalLine))
                    {
                        if (TryGetTableSeparatorColumnCount(expandedLine, out int columnCount)
                            && !LastOutputIsPipeTableRow(output))
                        {
                            if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[output.Count - 1]))
                            {
                                output.Add(string.Empty);
                            }
                            output.Add(BuildEmptyTableHeader(columnCount));
                        }
                        output.Add(expandedLine);
                    }
                }
            }

            return string.Join("\n", output);
        }

        // 只修复能够判定为块标记的粘连：行尾粗体标题、列表标记和水平分隔线。
        // 普通句子中的行内粗体保持原样，代码围栏内容在调用本方法前已经排除。
        private static string NormalizeMarkdownBlockMarkers(string line)
        {
            string normalized = line ?? string.Empty;
            normalized = Regex.Replace(normalized, @"(?m)(?<=\S)---\s*$", "\n\n---");
            normalized = Regex.Replace(normalized, @"(?<=\S)(?=- \*\*)", "\n");
            normalized = Regex.Replace(normalized, @"(?<=\S)(?=\d+[\.、]\s+\S)", "\n");
            normalized = Regex.Replace(
                normalized,
                @"(?<=\S)(?=\*\*[^*\n]{1,80}\*\*(?:\s*$|\s+[—–]\s*))",
                "\n\n");
            normalized = Regex.Replace(normalized, @"(?m)^(\s*\d+[\.、])(?=[^\s\d])", "$1 ");
            normalized = Regex.Replace(normalized, @"(?m)^(\s*)-(?=[\p{L}])", "$1- ");
            return normalized;
        }

        private static string UnwrapBareMarkdownFence(string markdown)
        {
            string[] lines = (markdown ?? string.Empty).Split('\n');
            var output = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string marker = lines[i].Trim();
                if (marker != "```" && marker != "~~~")
                {
                    output.Add(lines[i]);
                    continue;
                }

                int closingIndex = -1;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (string.Equals(lines[j].Trim(), marker, StringComparison.Ordinal))
                    {
                        closingIndex = j;
                        break;
                    }
                }
                if (closingIndex < 0)
                {
                    output.Add(lines[i]);
                    continue;
                }

                bool containsMarkdown = lines
                    .Skip(i + 1)
                    .Take(closingIndex - i - 1)
                    .Any(line => line.TrimStart().StartsWith("#", StringComparison.Ordinal)
                        || line.TrimStart().StartsWith("|", StringComparison.Ordinal)
                        || line.TrimStart().StartsWith("- ", StringComparison.Ordinal)
                        || Regex.IsMatch(line, @"^\s*\d+[\.、]\s+"));
                if (!containsMarkdown)
                {
                    output.Add(lines[i]);
                    continue;
                }

                for (int j = i + 1; j < closingIndex; j++)
                {
                    output.Add(lines[j]);
                }
                i = closingIndex;
            }
            return string.Join("\n", output);
        }

        // 模型偶尔会把“正文###标题”拼在同一行，或省略标题标记后的空格。
        // 仅处理二至六级 # 标记，避免把单个 # 用途的普通文本改写为标题。
        private static string NormalizeHeadingMarkers(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }

            var result = new StringBuilder();
            for (int i = 0; i < line.Length;)
            {
                if (line[i] != '#')
                {
                    result.Append(line[i]);
                    i++;
                    continue;
                }

                int count = 0;
                while (i + count < line.Length && line[i + count] == '#')
                {
                    count++;
                }

                int next = i + count;
                bool isHeading = count >= 2 && count <= 6 && next < line.Length && line[next] != '#';
                if (!isHeading)
                {
                    result.Append(line, i, count);
                    i = next;
                    continue;
                }

                if (result.Length > 0 && result[result.Length - 1] != '\n')
                {
                    result.Append('\n');
                }
                result.Append('#', count);
                if (!char.IsWhiteSpace(line[next]))
                {
                    result.Append(' ');
                }
                i = next;
            }
            return result.ToString();
        }

        // 模型可能把标题后的列表项或表头继续写在同一行，拆开后再交给 Markdown 渲染器。
        private static IEnumerable<string> ExpandMalformedHeadingBody(string line)
        {
            string trimmed = (line ?? string.Empty).TrimStart();
            if (!Regex.IsMatch(trimmed, @"^#{2,6}\s"))
            {
                yield return line;
                yield break;
            }

            Match headingMatch = Regex.Match(line ?? string.Empty, @"^\s*#{2,6}\s+");
            int headingEnd = headingMatch.Success ? headingMatch.Length : 0;
            int bodyIndex = line.IndexOf("- **", headingEnd, StringComparison.Ordinal);
            int pipeIndex = line.IndexOf('|');
            int strongIndex = line.IndexOf("**", headingEnd, StringComparison.Ordinal);
            if (strongIndex > headingEnd && char.IsWhiteSpace(line[strongIndex - 1]))
            {
                strongIndex = -1;
            }
            if (pipeIndex >= 0 && line.IndexOf('|', pipeIndex + 1) < 0)
            {
                pipeIndex = -1;
            }
            if (bodyIndex < 0 || (pipeIndex >= 0 && pipeIndex < bodyIndex))
            {
                bodyIndex = pipeIndex;
            }
            if (bodyIndex < 0 || (strongIndex >= 0 && strongIndex < bodyIndex))
            {
                bodyIndex = strongIndex;
            }
            if (bodyIndex <= 0)
            {
                yield return line;
                yield break;
            }

            yield return line.Substring(0, bodyIndex).TrimEnd();
            yield return string.Empty;
            yield return line.Substring(bodyIndex).TrimStart();
        }

        private static bool TryGetTableSeparatorColumnCount(string line, out int columnCount)
        {
            columnCount = 0;
            string trimmed = (line ?? string.Empty).Trim();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal) || !trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                return false;
            }

            string[] columns = trimmed.Substring(1, trimmed.Length - 2).Split('|');
            if (columns.Length < 2)
            {
                return false;
            }

            foreach (string column in columns)
            {
                string value = column.Trim().Trim(':');
                if (value.Length < 3)
                {
                    return false;
                }
                foreach (char valueChar in value)
                {
                    if (valueChar != '-')
                    {
                        return false;
                    }
                }
            }

            columnCount = columns.Length;
            return true;
        }

        private static bool LastOutputIsPipeTableRow(List<string> output)
        {
            if (output.Count == 0)
            {
                return false;
            }

            string previous = output[output.Count - 1].Trim();
            return previous.StartsWith("|", StringComparison.Ordinal) && previous.EndsWith("|", StringComparison.Ordinal);
        }

        private static string BuildEmptyTableHeader(int columnCount)
        {
            var builder = new StringBuilder("|");
            for (int i = 0; i < columnCount; i++)
            {
                builder.Append(" |");
            }
            return builder.ToString();
        }

        // 流式片段可能尚未闭合 Markdown 块，只做安全文本预览，结束后再由 MarkdownToHtml 正式渲染。
        private static string StreamingTextToHtml(string text)
        {
            return HtmlEncode(text).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");
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

        private async void TryPromptPreviewConfirmation(JObject raw)
        {
            // 从预演工具的字符串结果中提取 previewId；普通工具结果没有该字段。
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

            // Bridge 的预演类型不只有 intent.preview / patch.preview，流程结构操作还会
            // 以 proc.create / proc.delete / proc.reorder / proc.copy 返回。以服务端统一的
            // previewId + confirmed=false 判定“待前台确认的预演”，避免每新增一种类型就漏弹审核窗口。
            JObject resultData = resultObj["data"] as JObject;
            string previewId = resultData?["previewId"]?.Value<string>();
            string resultType = resultObj["type"]?.Value<string>() ?? string.Empty;
            bool confirmed = resultData?["confirmed"]?.Value<bool?>()
                ?? resultData?["preview"]?["confirmed"]?.Value<bool?>()
                ?? false;
            bool committed = resultData?["committed"]?.Value<bool?>() == true;
            bool rejected = resultData?["rejected"]?.Value<bool?>() == true
                || string.Equals(resultType, "preview.reject", StringComparison.Ordinal);
            string mode = resultData?["mode"]?.Value<string>()
                ?? resultData?["apply"]?["mode"]?.Value<string>()
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(previewId))
            {
                return;
            }

            if (committed || string.Equals(mode, "apply", StringComparison.Ordinal))
            {
                gooseClient?.LogFrontendAnalysisEvent("preview.applied", new JObject
                {
                    ["previewId"] = previewId,
                    ["resultType"] = resultType
                });
                return;
            }
            if (rejected)
            {
                gooseClient?.LogFrontendAnalysisEvent("preview.decided", new JObject
                {
                    ["previewId"] = previewId,
                    ["decision"] = "rejected",
                    ["source"] = "bridge"
                });
                return;
            }
            if (confirmed)
            {
                gooseClient?.LogFrontendAnalysisEvent("preview.decided", new JObject
                {
                    ["previewId"] = previewId,
                    ["decision"] = "confirmed",
                    ["source"] = autoApproveMode ? "auto_approve" : "bridge"
                });
                return;
            }

            if (promptedPreviewIds.Contains(previewId))
            {
                return;
            }
            promptedPreviewIds.Add(previewId);
            gooseClient?.LogFrontendAnalysisEvent("preview.created", new JObject
            {
                ["previewId"] = previewId,
                ["status"] = "awaiting_confirmation",
                ["autoApproveMode"] = autoApproveMode
            });

            // 自动批准模式下，预演记录已由 Bridge 直接标记为确认；无需弹窗或追加聊天消息。
            if (autoApproveMode)
            {
                gooseClient?.LogFrontendAnalysisEvent("preview.state_mismatch", new JObject
                {
                    ["previewId"] = previewId,
                    ["message"] = "自动批准模式下返回了未确认预演。"
                });
                return;
            }

            // 正常模式：弹出自定义审核对话框，让用户确认预演结果。
            JArray changes = FindFirstArray(resultObj, "changes") as JArray;
            JArray messages = FindFirstArray(resultObj, "messages") as JArray;
            gooseClient?.LogFrontendAnalysisEvent("preview.presented", new JObject
            {
                ["previewId"] = previewId,
                ["changeCount"] = changes?.Count ?? 0,
                ["messageCount"] = messages?.Count ?? 0
            });
            Stopwatch confirmationStopwatch = Stopwatch.StartNew();
            DialogResult result = ShowPreviewApprovalDialog(previewId, changes, messages);
            confirmationStopwatch.Stop();
            gooseClient?.LogFrontendAnalysisEvent("preview.decided", new JObject
            {
                ["previewId"] = previewId,
                ["decision"] = result == DialogResult.Yes ? "confirmed" : "rejected",
                ["source"] = "user",
                ["waitMs"] = confirmationStopwatch.ElapsedMilliseconds
            });
            if (result == DialogResult.Yes)
            {
                await ConfirmPreviewAsync(previewId).ConfigureAwait(true);
            }
            else
            {
                await RejectPreviewAsync(previewId).ConfigureAwait(true);
                AppendConversation("系统", "已取消本次变更。", Color.DarkOrange);
            }
        }

        // Bridge 确认请求不占用 UI 线程，确认完成后再更新前台状态。
        private async Task ConfirmPreviewAsync(string previewId)
        {
            try
            {
                await SendBridgeRequestAsync("POST", "/bridge/previews/confirm", new JObject
                {
                    ["previewId"] = previewId
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                BeginInvoke((Action)(() => AppendConversation("错误", "确认预演失败：" + ex.Message, Color.Red)));
            }
        }

        private async Task RejectPreviewAsync(string previewId)
        {
            try
            {
                await SendBridgeRequestAsync("POST", "/bridge/previews/reject", new JObject
                {
                    ["previewId"] = previewId
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                BeginInvoke((Action)(() => AppendConversation("错误", "取消预演失败：" + ex.Message, Color.Red)));
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
                using (GooseAcpClient client = new GooseAcpClient(resolvedConfig))
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
            const string machineGoosePath = @"D:\AutomationTools\Goose\goose.exe";
            if (File.Exists(machineGoosePath))
            {
                resolvedPath = machineGoosePath;
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
            lock (clientLock)
            {
                foreach (AiTaskRuntime runtime in taskRuntimes.Values)
                {
                    try
                    {
                        runtime.Cancellation?.Cancel();
                        runtime.Client?.Dispose();
                    }
                    catch
                    {
                    }
                    runtime.Client = null;
                    runtime.Cancellation?.Dispose();
                    runtime.Cancellation = null;
                    runtime.Running = false;
                }
                gooseClient = null;
                promptCts = null;
            }
        }
    }
}
