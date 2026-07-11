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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public sealed class FrmAiAssistant : Form
    {
        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel();
        private WebView2 webViewConversation;
        private bool webViewEventsAttached;
        private bool webDocumentReady;
        // 标记当前是否正在流式输出 assistant 文本（assistant_chunk），用于在同一渲染段累积而非每 chunk 新建 div。
        private bool streamingAssistant;
        // 流式段累积的 Markdown 源码，流式期间实时转 HTML 渲染，段结束时做最终渲染。
        private readonly StringBuilder streamingMarkdown = new StringBuilder();
        // 当前流式段对应的临时 div id（每段递增），用于流式渲染与最终 HTML 替换定位。
        private string streamingDivId;
        private int streamingSegmentIndex;
        private bool streamingThought;
        private readonly StringBuilder streamingThoughtMarkdown = new StringBuilder();
        private string streamingThoughtDivId;
        private int streamingThoughtSegmentIndex;
        // 串行化 WebView2 脚本执行，保证 HTML 追加/替换顺序与事件顺序一致。
        private Task pendingScriptTask = Task.CompletedTask;
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
        private string toolProfile = GooseConfigStorage.DefaultToolProfile;
        private GooseConfig appliedConfig;
        private readonly HashSet<string> promptedPreviewIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object clientLock = new object();

        private GooseAcpClient gooseClient;
        private CancellationTokenSource promptCts;
        private bool sending;
        private bool fullPermissionMode = false;
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
            txtPrompt.Enabled = true;
            txtPrompt.ReadOnly = sending;
            txtGooseExecutable.ReadOnly = sending;
            txtWorkingDirectory.ReadOnly = sending;
            txtMcpUri.ReadOnly = sending;
            txtSessionName.ReadOnly = sending;
            cboProvider.Enabled = !sending;
            cboModel.Enabled = !sending;
            nudMaxTurns.Enabled = !sending;
            PushWebAppState();
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
        private const string BaseConversationHtml = @"<!DOCTYPE html>
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
.brand{display:flex;align-items:center;gap:10px;min-width:0;}
.brand-mark{width:28px;height:28px;border-radius:8px;background:#172033;color:#fff;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:12px;letter-spacing:.2px;}
.brand-title{font-weight:650;color:#172033;line-height:1.1;}
.brand-subtitle{font-size:12px;color:#7b8798;margin-top:1px;}
.top-actions{display:flex;align-items:center;gap:8px;}
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
.chat-area{flex:1;min-height:0;overflow-y:auto;}
#messages{
    max-width:1120px;
    margin:0 auto;
    padding:6px 10px;
}
.msg{
    display:flex;
    flex-direction:column;
    gap:2px;
    margin:0 0 6px;
}
.msg.user{align-items:flex-end;}
.msg.assistant,.msg.error{align-items:flex-start;}
.msg .role{
    font-size:11px;
    color:#7b8798;
    line-height:1.2;
    padding:0 4px;
}
.msg-head{width:100%;display:flex;align-items:center;gap:8px;padding:0 4px;}
.msg.user .msg-head{justify-content:flex-end;}
.msg-head .role{padding:0;}
.copy-message{border:0;background:transparent;color:#8a96a7;font:11px ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;cursor:pointer;padding:1px 5px;border-radius:5px;opacity:.35;}
.msg:hover .copy-message,.copy-message:focus{opacity:1;}
.copy-message:hover{color:#1f5f99;background:#e8f1fa;}
.msg.user .role{text-align:right;}
.msg .content{
    max-width:92%;
    word-break:break-word;
    overflow-wrap:anywhere;
    -webkit-user-select:text;
    user-select:text;
}
.msg.user .content{
    max-width:72%;
    color:#102033;
    background:#dceeff;
    border:1px solid #bad9f6;
    border-radius:14px 14px 4px 14px;
    padding:5px 8px;
}
.msg.assistant .content{
    color:#182434;
    background:#ffffff;
    border:1px solid #dfe6ef;
    border-radius:8px;
    padding:5px 8px;
    box-shadow:0 2px 8px rgba(31,45,61,.05);
}
.msg.error .content{
    color:#8f1d1d;
    background:#fff5f5;
    border:1px solid #f0caca;
    border-radius:8px;
    padding:5px 8px;
}
.content>*:first-child{margin-top:0;}
.content>*:last-child{margin-bottom:0;}
p{margin:2px 0;}
ul,ol{margin:2px 0;padding-left:19px;}
li{margin:1px 0;}
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
    margin:6px 0;
    border-collapse:collapse;
    table-layout:fixed;
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
    white-space:pre-wrap;
    word-wrap:break-word;
}
code{
    color:#26374d;
    background:#eef2f7;
    border:1px solid #d8e0ea;
    border-radius:5px;
    padding:1px 4px;
    font:13px/1.4 Consolas,""Cascadia Mono"",monospace;
}
pre code{background:transparent;border:0;padding:0;}
img{max-width:100%;border-radius:8px;}
hr{border:none;border-top:1px solid #dfe6ef;margin:8px 0;}
.thinking-box{
    max-height:min(42vh,320px);
    overflow-y:auto;
    background:#ffffff;
    border:1px solid #d7e1ee;
    border-radius:8px;
    margin:4px 0 8px;
    padding:0;
    box-shadow:0 3px 10px rgba(31,45,61,.04);
}
.thinking-box.collapsed{max-height:32px;overflow:hidden;}
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
.tool-call,.tool-result{
    margin:4px 8px;
    padding:6px 8px;
    border-radius:6px;
    font:12px/1.4 Consolas,""Cascadia Mono"",monospace;
    white-space:pre-wrap;
    overflow-wrap:anywhere;
}
.tool-call{color:#5a3a10;background:#fff8ec;border:1px solid #f0dfbd;}
.tool-result{color:#485465;background:#f7f9fc;border:1px solid #e2e8f0;}
.streaming-segment{
    padding:6px 10px;
    color:#334155;
}
.composer-wrap{padding:8px 14px 10px;background:#f5f7fb;}
.composer{max-width:1120px;margin:0 auto;background:#fff;border:1px solid #e2e7ef;border-radius:16px;min-height:76px;box-shadow:0 6px 16px rgba(16,24,40,.06);position:relative;padding:10px 50px 34px 12px;}
.prompt-input{width:100%;height:30px;max-height:100px;border:0;outline:none;resize:none;overflow-y:auto;background:transparent;color:#172033;font:14px/1.45 ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;}
.prompt-input::placeholder{color:#b6bcc5;}
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
.modal-foot{display:flex;justify-content:space-between;gap:10px;padding:14px 20px;border-top:1px solid #edf1f6;background:#fff;}
.foot-left,.foot-right{display:flex;gap:10px;}
.text-button,.primary-button{height:36px;border-radius:9px;padding:0 14px;font:13px ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;cursor:pointer;}
.text-button{border:1px solid #d8e0ea;background:#fff;color:#35445a;}
.text-button:hover{background:#f3f7fb;}
.primary-button{border:1px solid #246fb5;background:#246fb5;color:#fff;}
.primary-button:hover{background:#1f63a3;}
.toast{position:fixed;right:20px;bottom:20px;max-width:420px;background:#172033;color:#fff;border-radius:11px;padding:10px 13px;box-shadow:0 12px 30px rgba(15,23,42,.24);display:none;z-index:40;font-size:13px;}
.toast.show{display:block;}
@media (max-width:720px){.settings-grid{grid-template-columns:1fr}.composer-wrap{padding:10px}.topbar{padding:0 12px}.brand-subtitle{display:none}}
</style>
<script>
var appState={sending:false,canAccess:false,canEditConfig:false,config:{},providerOptions:[],modelOptions:[]};
var quickSettingPending=false;
function post(type,payload){if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(Object.assign({type:type},payload||{}));}}
function byId(id){return document.getElementById(id);}
function toggleThinkingBox(id){
    var el=document.getElementById(id);
    if(el){el.classList.toggle('collapsed');if(el.classList.contains('collapsed')){el.scrollTop=0;}}
}
function onThinkingBoxScroll(box){
    if(box.classList.contains('collapsed')){return;}
    var atBottom=box.scrollTop+box.clientHeight>=box.scrollHeight-30;
    if(atBottom){box.classList.remove('user-scrolled');}else{box.classList.add('user-scrolled');}
}
function scrollMessagesToBottom(){
    var m=byId('messagesScroll');
    if(!m){return;}
    var run=function(){m.scrollTop=m.scrollHeight;};
    run();
    window.requestAnimationFrame(run);
    window.setTimeout(run,80);
}
function scrollThinkingBoxToBottom(boxId){
    var box=document.getElementById(boxId);
    if(box&&!box.classList.contains('collapsed')&&!box.classList.contains('user-scrolled')){
        var run=function(){box.scrollTop=box.scrollHeight;};
        run();
        window.requestAnimationFrame(run);
        window.setTimeout(run,80);
    }
    scrollMessagesToBottom();
}
function setOptions(select,items,value){
    select.innerHTML='';
    (items||[]).forEach(function(item){var opt=document.createElement('option');opt.value=item;opt.textContent=item;select.appendChild(opt);});
    if(value && Array.prototype.some.call(select.options,function(o){return o.value===value;})){select.value=value;}
    else if(select.options.length){select.selectedIndex=0;}
}
function collectConfig(){
    return {
        gooseExecutablePath:byId('cfgGoose').value,
        workingDirectory:byId('cfgWorkdir').value,
        mcpUri:byId('cfgMcp').value,
        sessionName:byId('cfgSession').value,
        provider:byId('cfgProvider').value,
        model:byId('cfgModel').value,
        apiKey:byId('cfgApiKey').value,
        maxTurns:parseInt(byId('cfgTurns').value||'1',10),
        toolProfile:(appState.config||{}).toolProfile||'Diagnostic',
        fullPermissionMode:!!(appState.config||{}).fullPermissionMode
    };
}
function fillConfig(){
    var c=appState.config||{};
    byId('cfgGoose').value=c.gooseExecutablePath||'';
    byId('cfgWorkdir').value=c.workingDirectory||'';
    byId('cfgMcp').value=c.mcpUri||'';
    byId('cfgSession').value=c.sessionName||'';
    byId('cfgTurns').value=c.maxTurns||20;
    setOptions(byId('cfgProvider'),appState.providerOptions||[],c.provider||'deepseek');
    setOptions(byId('cfgModel'),appState.modelOptions||[],c.model||'deepseek-chat');
    byId('cfgApiKey').value='';
    byId('cfgApiKey').placeholder=c.hasApiKey?'本机已保存，留空则保持不变':'输入 API Key（仅保存在本机）';
}
function refreshToolbar(){
    var c=appState.config||{};
    var profile=c.toolProfile||'Diagnostic';
    byId('toolDiagnostic').classList.toggle('active',profile==='Diagnostic');
    byId('toolEditor').classList.toggle('active',profile==='Editor');
    byId('fullPermissionButton').classList.toggle('active',!!c.fullPermissionMode);
    byId('fullPermissionButton').setAttribute('aria-pressed',c.fullPermissionMode?'true':'false');
    var lock=!appState.canEditConfig||appState.sending||quickSettingPending;
    ['toolDiagnostic','toolEditor','fullPermissionButton'].forEach(function(id){byId(id).disabled=lock;});
}
function setToolProfile(profile){
    if(quickSettingPending||appState.sending||(appState.config||{}).toolProfile===profile){return;}
    quickSettingPending=true;appState.config.toolProfile=profile;refreshToolbar();post('setToolProfile',{profile:profile});
}
function toggleFullPermission(){
    if(quickSettingPending||appState.sending){return;}
    quickSettingPending=true;appState.config.fullPermissionMode=!appState.config.fullPermissionMode;refreshToolbar();post('setFullPermission',{enabled:!!appState.config.fullPermissionMode});
}
function automationSetState(state){
    appState=state||appState;
    quickSettingPending=false;
    var status=byId('statusText');
    if(status){status.textContent=appState.sending?'生成中':'就绪';}
    byId('promptInput').disabled=!appState.canAccess||appState.sending;
    refreshSendButton();
    byId('resetButton').disabled=!appState.canAccess||appState.sending;
    byId('configButton').disabled=false;
    fillConfig();
    refreshToolbar();
    var lock=!appState.canEditConfig||appState.sending;
    ['cfgGoose','cfgWorkdir','cfgMcp','cfgSession','cfgProvider','cfgModel','cfgApiKey','cfgTurns','saveConfig','clearApiKey','restorePrompt'].forEach(function(id){var el=byId(id);if(el){el.disabled=lock;}});
    byId('reloadConfig').disabled=appState.sending;
    byId('checkConfig').disabled=appState.sending||!appState.canAccess;
}
function refreshSendButton(){
    var send=byId('sendButton');
    var input=byId('promptInput');
    if(!send||!input){return;}
    var canSend=appState.canAccess&&!appState.sending&&input.value.trim().length>0;
    send.classList.toggle('stop',!!appState.sending);
    send.classList.toggle('ready',canSend);
    send.disabled=!appState.sending&&!canSend;
    send.title=appState.sending?'停止':'发送';
    send.setAttribute('aria-label',appState.sending?'停止':'发送');
}
function openConfig(){fillConfig();byId('configOverlay').classList.add('open');}
function closeConfig(){byId('configOverlay').classList.remove('open');}
function showToast(text){var t=byId('toast');t.textContent=text;t.classList.add('show');clearTimeout(window.toastTimer);window.toastTimer=setTimeout(function(){t.classList.remove('show');},3200);}
function copyMessage(button){
    var msg=button.closest('.msg');
    var content=msg&&msg.querySelector('.content');
    if(!content){return;}
    post('copyText',{text:content.innerText||content.textContent||''});
}
function sendPrompt(){
    if(appState.sending){post('stop');return;}
    var input=byId('promptInput');
    var text=input.value.trim();
    if(!text){return;}
    post('send',{prompt:text});
    input.value='';
    autoGrowPrompt();
}
function autoGrowPrompt(){
    var input=byId('promptInput');
    input.style.height='38px';
    input.style.height=Math.min(120,Math.max(38,input.scrollHeight))+'px';
    refreshSendButton();
}
document.addEventListener('DOMContentLoaded',function(){
    byId('configButton').addEventListener('click',openConfig);
    byId('toolDiagnostic').addEventListener('click',function(){setToolProfile('Diagnostic');});
    byId('toolEditor').addEventListener('click',function(){setToolProfile('Editor');});
    byId('fullPermissionButton').addEventListener('click',toggleFullPermission);
    byId('resetButton').addEventListener('click',function(){post('reset');});
    byId('sendButton').addEventListener('click',sendPrompt);
    byId('promptInput').addEventListener('input',autoGrowPrompt);
    byId('promptInput').addEventListener('keydown',function(e){if(e.key==='Enter'&&!e.shiftKey&&!e.altKey){e.preventDefault();sendPrompt();}});
    byId('closeConfig').addEventListener('click',closeConfig);
    byId('cancelConfig').addEventListener('click',closeConfig);
    byId('saveConfig').addEventListener('click',function(){post('saveConfig',{config:collectConfig()});});
    byId('reloadConfig').addEventListener('click',function(){post('reloadConfig');});
    byId('checkConfig').addEventListener('click',function(){post('checkConfig',{config:collectConfig()});});
    byId('clearApiKey').addEventListener('click',function(){post('clearApiKey',{provider:byId('cfgProvider').value});});
    byId('restorePrompt').addEventListener('click',function(){post('restorePrompt');});
    byId('cfgProvider').addEventListener('change',function(){post('providerChanged',{provider:this.value,config:collectConfig()});});
    byId('configOverlay').addEventListener('click',function(e){if(e.target===this){closeConfig();}});
    post('ready');
});
</script>
</head>
<body>
<div class=""app-shell"">
  <header class=""topbar"">
    <div class=""brand""><div class=""brand-mark"">AI</div><div><div class=""brand-title"">EW-AI 助手</div><div class=""brand-subtitle"" id=""statusText"">就绪</div></div></div>
    <div class=""top-actions"">
      <div class=""tool-mode"" role=""group"" aria-label=""AI工具模式""><button class=""toolbar-option"" id=""toolDiagnostic"" title=""只读查询和流程诊断"">诊断</button><button class=""toolbar-option"" id=""toolEditor"" title=""包含诊断能力并允许预演和修改"">编辑</button></div>
      <button class=""permission-toggle"" id=""fullPermissionButton"" aria-pressed=""false"" title=""开启后自动批准工具调用和预演，并允许访问当前用户目录所在磁盘"">完全权限</button>
      <button class=""icon-button"" id=""resetButton"" title=""重置会话"" aria-label=""重置会话""><svg viewBox=""0 0 24 24""><path d=""M3 12a9 9 0 1 0 3-6.7""/><path d=""M3 4v6h6""/></svg></button>
      <button class=""icon-button"" id=""configButton"" title=""配置"" aria-label=""配置""><svg viewBox=""0 0 24 24""><path d=""M12 15.5a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7Z""/><path d=""M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9l-.1-.1A2 2 0 1 1 7 4.2l.1.1A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1A2 2 0 1 1 19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.1a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z""/></svg></button>
    </div>
  </header>
  <main class=""chat-area scrollable"" id=""messagesScroll""><div id=""messages""></div></main>
  <footer class=""composer-wrap""><div class=""composer""><textarea id=""promptInput"" class=""prompt-input"" placeholder=""要求后续变更""></textarea><button id=""sendButton"" class=""send-button"" title=""发送"" aria-label=""发送""><span class=""circle""><svg class=""arrow-icon"" viewBox=""0 0 24 24""><path d=""M12 5 5.5 11.5l1.6 1.6 3.8-3.8V20h2.2V9.3l3.8 3.8 1.6-1.6L12 5Z""/></svg><span class=""stop-icon""></span></span></button></div></footer>
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
        <div class=""field""><label>Provider</label><select id=""cfgProvider""></select></div>
        <div class=""field""><label>模型</label><select id=""cfgModel""></select></div>
        <div class=""field field-wide""><label>API Key（使用 Windows 当前用户加密，仅保存在本机）</label><input id=""cfgApiKey"" type=""password"" autocomplete=""new-password""></div>
      </div>
    </div>
    <div class=""modal-foot""><div class=""foot-left""><button class=""text-button"" id=""reloadConfig"">重载</button><button class=""text-button"" id=""checkConfig"">检查 AI 组件</button><button class=""text-button"" id=""clearApiKey"">清除本机密钥</button><button class=""text-button"" id=""restorePrompt"">恢复上一版 Prompt</button></div><div class=""foot-right""><button class=""text-button"" id=""cancelConfig"">取消</button><button class=""primary-button"" id=""saveConfig"">保存配置</button></div></div>
  </section>
</div>
<div class=""toast"" id=""toast""></div>
</body>
</html>";

        private void FrmAiAssistant_FormClosing(object sender, FormClosingEventArgs e)
        {
            DisposeGooseClient();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeGooseClient();
                promptCts?.Dispose();
                webViewConversation?.Dispose();
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

            RefreshModelOptions("deepseek", "deepseek-chat");
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
                    return new[] { "deepseek-chat", "deepseek-reasoner" };
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
            cboProvider.Text = string.IsNullOrWhiteSpace(config.Provider) ? "deepseek" : config.Provider;
            RefreshModelOptions(cboProvider.Text, string.IsNullOrWhiteSpace(config.Model) ? "deepseek-chat" : config.Model);
            nudMaxTurns.Value = Math.Max(nudMaxTurns.Minimum, Math.Min(nudMaxTurns.Maximum, config.MaxTurns));
            toolProfile = config.ToolProfile;
            fullPermissionMode = config.FullPermissionMode;
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
                case "stop":
                    if (sending)
                    {
                        StopCurrentPrompt();
                    }
                    break;
                case "reset":
                    BtnResetSession_Click(sender, EventArgs.Empty);
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
                    SaveWebConfig(requestedProfile == "Diagnostic" ? "已切换到诊断模式。" : "已切换到编辑模式。", false);
                    break;
                case "setFullPermission":
                    if (sending)
                    {
                        PushWebAppState();
                        break;
                    }
                    fullPermissionMode = message["enabled"]?.Value<bool?>() ?? false;
                    SaveWebConfig(fullPermissionMode ? "完全权限已开启，请谨慎操作。" : "完全权限已关闭。", false);
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
                case "restorePrompt":
                    if (MessageBox.Show(
                        "将恢复最近一次 System Prompt 备份，并重置当前 EW-AI 会话。是否继续？",
                        "恢复 System Prompt",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        break;
                    }
                    if (GooseRuntimeProvisioner.TryRestoreLatestBackup(out string restoreMessage))
                    {
                        DisposeGooseClient();
                    }
                    ShowWebToast(restoreMessage);
                    break;
                case "checkConfig":
                    ApplyWebConfig(message["config"] as JObject);
                    await CheckWebConfigAsync().ConfigureAwait(true);
                    break;
            }
        }

        private JObject BuildWebAppState()
        {
            string providerText = string.IsNullOrWhiteSpace(cboProvider.Text) ? "deepseek" : cboProvider.Text;
            string modelText = string.IsNullOrWhiteSpace(cboModel.Text) ? "deepseek-chat" : cboModel.Text;
            string normalizedProvider = NormalizeGooseOverride(providerText);
            return new JObject
            {
                ["sending"] = sending,
                ["canAccess"] = true,
                ["canEditConfig"] = true,
                ["config"] = new JObject
                {
                    ["gooseExecutablePath"] = txtGooseExecutable.Text,
                    ["workingDirectory"] = txtWorkingDirectory.Text,
                    ["mcpUri"] = txtMcpUri.Text,
                    ["sessionName"] = txtSessionName.Text,
                    ["provider"] = providerText,
                    ["model"] = modelText,
                    ["hasApiKey"] = !string.IsNullOrWhiteSpace(normalizedProvider)
                        && AiProviderSecretStorage.HasSecret(normalizedProvider),
                    ["maxTurns"] = (int)nudMaxTurns.Value,
                    ["toolProfile"] = toolProfile,
                    ["fullPermissionMode"] = fullPermissionMode
                },
                ["providerOptions"] = BuildComboOptions(cboProvider, providerText),
                ["modelOptions"] = BuildComboOptions(cboModel, modelText)
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

            string provider = config["provider"]?.Value<string>() ?? "deepseek";
            string model = config["model"]?.Value<string>() ?? "deepseek-chat";
            cboProvider.Text = string.IsNullOrWhiteSpace(provider) ? "deepseek" : provider;
            RefreshModelOptions(NormalizeGooseOverride(cboProvider.Text), model);
            cboModel.Text = string.IsNullOrWhiteSpace(model) ? "deepseek-chat" : model;

            int maxTurns = config["maxTurns"]?.Value<int?>() ?? GooseConfigStorage.DefaultMaxTurns;
            nudMaxTurns.Value = Math.Max(nudMaxTurns.Minimum, Math.Min(nudMaxTurns.Maximum, maxTurns));
            toolProfile = config["toolProfile"]?.Value<string>() ?? GooseConfigStorage.DefaultToolProfile;
            fullPermissionMode = config["fullPermissionMode"]?.Value<bool?>() ?? false;
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
                    fullPermissionMode = oldConfig.FullPermissionMode;
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
                || oldConfig.MaxTurns != config.MaxTurns
                || oldConfig.FullPermissionMode != config.FullPermissionMode;
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
                    }
                    else
                    {
                        await AutomationMcpServerManager.SetToolProfileAsync(config.McpUri, config.ToolProfile)
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
                        fullPermissionMode = oldConfig.FullPermissionMode;
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
                    fullPermissionMode = oldConfig.FullPermissionMode;
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
                MaxTurns = (int)nudMaxTurns.Value,
                ToolProfile = toolProfile,
                FullPermissionMode = fullPermissionMode
            };

            if (TryResolveGooseExecutablePath(config.GooseExecutablePath, out string resolvedGoosePath))
            {
                config.GooseExecutablePath = resolvedGoosePath;
                txtGooseExecutable.Text = resolvedGoosePath;
            }

            if (!string.IsNullOrWhiteSpace(config.Provider)
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
                MaxTurns = config.MaxTurns,
                ToolProfile = config.ToolProfile,
                FullPermissionMode = config.FullPermissionMode
            };
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            if (sending)
            {
                StopCurrentPrompt();
                return;
            }

            string prompt = txtPrompt.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }
            if (!TryBuildConfig(out GooseConfig config, out string error))
            {
                ShowWebToast("配置无效：" + error);
                PushWebAppState();
                return;
            }

            AppendConversation("用户", prompt, Color.FromArgb(22, 72, 130));
            // 每次发送对话强制重置 thinking-box 滚动状态并滚到底部，覆盖用户上滑设置，确保用户看到最新回复。
            EnqueueScript("document.querySelectorAll('.thinking-box').forEach(function(b){b.classList.remove('user-scrolled');if(!b.classList.contains('collapsed')){b.scrollTop=b.scrollHeight;}});if(window.scrollMessagesToBottom){scrollMessagesToBottom();}");
            txtPrompt.Clear();
            sending = true;
            promptCts?.Dispose();
            promptCts = new CancellationTokenSource();
            ApplyPermissions();

            try
            {
                GooseAcpClient client = EnsureGooseClient(config);
                await client.PromptAsync(prompt, promptCts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                AppendConversation("系统", "已停止本轮生成。", Color.DarkOrange);
            }
            catch (Exception ex)
            {
                AppendConversation("错误", ex.Message, Color.Red);
            }
            finally
            {
                // AI 纯文字回复不调工具时，不会有后续非流式事件触发收尾，必须在此兜底。
                FinishStreaming();
                FinishThoughtStreaming();
                CollapseThinkingBox();
                sending = false;
                ApplyPermissions();
            }
        }

        private void StopCurrentPrompt()
        {
            promptCts?.Cancel();
            lock (clientLock)
            {
                gooseClient?.Cancel();
            }
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
            streamingThought = false;
            streamingThoughtMarkdown.Clear();
            streamingThoughtDivId = null;
            streamingThoughtSegmentIndex = 0;
            currentThinkingBoxId = null;
            pendingScriptTask = Task.CompletedTask;
            // 生成唯一会话名避免 Goose 加载磁盘上的同名历史上下文
            txtSessionName.Text = "automation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            ResetConversationHtml();
            PushWebAppState();
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

        #region 自定义审核对话框

        // 预演确认对话框：以表格形式展示变更详情（操作类型/位置/对象/字段/原值/新值），让用户清晰看到改了什么。
        private DialogResult ShowPreviewApprovalDialog(string previewId, JArray changes, JArray messages)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "EW-AI 预演确认";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Width = 880;
                dlg.Height = 540;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = Color.White;
                dlg.Font = new Font("微软雅黑", 9F);

                // 标题栏
                Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(30, 104, 74) };
                headerPanel.Controls.Add(new Label
                {
                    Text = "  EW-AI 预演确认 — 请审核以下变更",
                    Font = new Font("微软雅黑", 11F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                });

                // 信息行
                Panel infoPanel = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(12, 4, 12, 4) };
                infoPanel.Controls.Add(new Label
                {
                    Text = $"PreviewId: {previewId}    变更数量: {changes?.Count ?? 0}",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.FromArgb(90, 98, 108),
                    Font = new Font("Consolas", 8.5F)
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
                    BorderStyle = BorderStyle.None,
                    ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                    {
                        BackColor = Color.FromArgb(240, 243, 247),
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
                    GridColor = Color.FromArgb(220, 225, 230)
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
                                location = $"重写{change["rewrittenCount"]?.Value<int>() ?? 0}/回退{change["fallbackCount"]?.Value<int>() ?? 0}/清空{change["clearedCount"]?.Value<int>() ?? 0}";
                                break;
                        }

                        int rowIndex = dgv.Rows.Add(type, location, obj, field, oldVal, newVal);
                        dgv.Rows[rowIndex].DefaultCellStyle.BackColor = rowColor;
                    }
                }

                // 消息区
                TextBox txtMessages = new TextBox
                {
                    Dock = DockStyle.Bottom,
                    Height = 70,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    ReadOnly = true,
                    BackColor = Color.FromArgb(250, 251, 252),
                    Font = new Font("微软雅黑", 8.5F),
                    BorderStyle = BorderStyle.FixedSingle
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
                Button btnConfirm = new Button
                {
                    Text = "✓ 确认提交",
                    DialogResult = DialogResult.Yes,
                    Size = new Size(120, 32),
                    BackColor = Color.FromArgb(35, 134, 54),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                    Anchor = AnchorStyles.Right
                };
                btnConfirm.FlatAppearance.BorderSize = 0;
                btnPanel.Controls.Add(btnConfirm);
                btnPanel.Controls.Add(btnReject);

                // 按顺序添加控件（WinForms docking: 后添加的先停靠）
                dlg.Controls.Add(dgv);
                dlg.Controls.Add(txtMessages);
                dlg.Controls.Add(btnPanel);
                dlg.Controls.Add(infoPanel);
                dlg.Controls.Add(headerPanel);

                dlg.Resize += (s, e) =>
                {
                    btnConfirm.Location = new Point(btnPanel.Width - btnConfirm.Width - 16, 7);
                    btnReject.Location = new Point(btnConfirm.Left - btnReject.Width - 10, 7);
                };
                // 初始定位按钮
                dlg.Shown += (s, e) =>
                {
                    btnConfirm.Location = new Point(btnPanel.Width - btnConfirm.Width - 16, 7);
                    btnReject.Location = new Point(btnConfirm.Left - btnReject.Width - 10, 7);
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
                return (JObject)Invoke(new Func<JObject, JObject>(HandlePermissionRequest), request);
            }

            string title = request["toolCall"]?["title"]?.Value<string>()
                ?? request["toolCall"]?["name"]?.Value<string>()
                ?? "EW-AI 权限请求";

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

            string toolName = request["toolCall"]?["name"]?.Value<string>() ?? "";
            JObject arguments = request["toolCall"]?["arguments"] as JObject;
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

            if (string.Equals(item.Kind, "assistant", StringComparison.Ordinal))
            {
                // 流式刚结束，内容已在流式 div 中完整渲染，跳过重复的 assistant 事件。
                if (!justFinishedStreaming)
                {
                    AppendConversation("EW-AI", item.Text, Color.FromArgb(30, 104, 74));
                }
            }
            else if (string.Equals(item.Kind, "tool_call", StringComparison.Ordinal))
            {
                AppendToolEntry("call", item.Text, Color.FromArgb(96, 62, 14));
            }
            else if (string.Equals(item.Kind, "tool_result", StringComparison.Ordinal))
            {
                AppendToolEntry("result", item.Text, Color.Gray);
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
            bool isCall = string.Equals(marker, "call", StringComparison.Ordinal);
            string cls = isCall ? "tool-call" : "tool-result";
            string display = isCall ? "工具调用" : "工具返回";
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
            string html = "<div class=\"thinking-box\" id=\"" + boxId + "\" onscroll=\"onThinkingBoxScroll(this)\">"
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
            string js = "var box=document.getElementById('" + currentThinkingBoxId + "');if(box){box.scrollTop=0;box.classList.add('collapsed');}";
            EnqueueScript(js);
            currentThinkingBoxId = null;
        }

        // 流式追加正式助手文本：直接渲染到对话区，避免完成后再复制一份结果。
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
                string time = DateTime.Now.ToString("HH:mm:ss");
                string html = "<div class=\"msg assistant\"><div class=\"msg-head\"><span class=\"role\">EW-AI " + HtmlEncode(time) + "</span>"
                    + "<button class=\"copy-message\" type=\"button\" onclick=\"copyMessage(this)\" title=\"复制本条文字\">复制</button></div>"
                    + "<div class=\"content\"><div class=\"streaming-segment\" id=\"" + streamingDivId + "\">"
                    + StreamingTextToHtml(streamingMarkdown.ToString()) + "</div></div></div>";
                EnqueueAppendHtml(html);
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
            streamingAssistant = false;
            streamingDivId = null;
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
            if ((DateTime.Now - lastThoughtRender).TotalMilliseconds >= 50)
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
            else if (role == "EW-AI")
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
            string html = "<div class=\"" + cls + "\"><div class=\"msg-head\"><span class=\"role\">" + HtmlEncode(role) + " " + HtmlEncode(time) + "</span>"
                + "<button class=\"copy-message\" type=\"button\" onclick=\"copyMessage(this)\" title=\"复制本条文字\">复制</button></div>"
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
            string js = "document.getElementById('messages').insertAdjacentHTML('beforeend'," + htmlJson + ");if(window.scrollMessagesToBottom){scrollMessagesToBottom();}";
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
            string[] lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
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

                string normalizedLine = NormalizeHeadingMarkers(line);
                if (TryGetTableSeparatorColumnCount(normalizedLine, out int columnCount)
                    && !LastOutputIsPipeTableRow(output))
                {
                    if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[output.Count - 1]))
                    {
                        output.Add(string.Empty);
                    }
                    output.Add(BuildEmptyTableHeader(columnCount));
                }
                output.Add(normalizedLine);
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

            // 正常模式：弹出自定义审核对话框，让用户确认预演结果。
            JArray changes = FindFirstArray(resultObj, "changes") as JArray;
            JArray messages = FindFirstArray(resultObj, "messages") as JArray;
            DialogResult result = ShowPreviewApprovalDialog(previewId, changes, messages);
            if (result == DialogResult.Yes)
            {
                // 必须在继续工具调用前完成确认，避免 AI 抢先提交而命中“预演尚未确认”。
                ConfirmPreviewAsync(previewId).GetAwaiter().GetResult();
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
                    MaxTurns = config.MaxTurns,
                    ToolProfile = config.ToolProfile,
                    FullPermissionMode = config.FullPermissionMode
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
