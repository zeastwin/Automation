using System;
using System.IO;

namespace Automation
{
    public sealed partial class FrmAiAssistant
    {
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
    <div class=""topbar-left""><div class=""brand""><div class=""brand-mark"">EW</div><div><div class=""brand-title"">EW-AI 助手</div><div class=""brand-subtitle"" id=""statusText"">就绪</div></div></div><span class=""home-divider"" aria-hidden=""true""></span><button class=""icon-button home-button"" id=""newSessionButton"" title=""返回任务列表"" aria-label=""返回任务列表"" aria-pressed=""false""><svg viewBox=""0 0 24 24"" aria-hidden=""true""><path d=""M19 12H5""/><path d=""m11 18-6-6 6-6""/></svg></button></div>
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
    }
}
