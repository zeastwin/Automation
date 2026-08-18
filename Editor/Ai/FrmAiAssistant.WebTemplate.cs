// 模块：编辑器 / AI。
// 职责范围：AI 前台、ACP 会话、预演确认与对话渲染。
// 文件职责：维护 AI WebView 的 HTML、CSS 与脚本模板。

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
            return BaseConversationHtmlTemplate
                .Replace("__CHICK_AVATAR__", ChickAvatarDataUri)
                .Replace("__FLOW_VISUAL_CSS__", FlowVisualizationCss);
        }

        // 流程结构独立放大页：在 FrmFlowZoom 的最大化窗口内整屏横跨显示，缩放与适应宽度由页面自身提供。
        internal static string BuildFlowZoomPageHtml(string flowCardsHtml)
        {
            string template = @"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
*{box-sizing:border-box;}
html,body{height:100%;}
body{
    margin:0;
    color:#172033;
    background:linear-gradient(165deg,#f8fafd 0%,#f1f5fa 55%,#edf2f8 100%);
    font:14px/1.5 ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;
    overflow:hidden;
    display:flex;
    flex-direction:column;
    border:1px solid #c9d4e2;
}
.zoom-toolbar{
    flex:0 0 auto;
    display:flex;
    align-items:center;
    gap:8px;
    padding:8px 14px;
    background:rgba(255,255,255,.92);
    border-bottom:1px solid #e5ebf3;
}
.zoom-title{font-size:14px;font-weight:650;color:#172033;margin-right:auto;}
.zoom-group{display:flex;align-items:center;gap:4px;}
.icon-button{width:30px;height:30px;border:0;border-radius:8px;background:transparent;color:#526071;display:inline-flex;align-items:center;justify-content:center;cursor:pointer;}
.icon-button svg{width:17px;height:17px;stroke:currentColor;stroke-width:2;fill:none;stroke-linecap:round;stroke-linejoin:round;}
.icon-button:hover{background:#eef3f9;color:#1f5f99;}
.icon-button:active{transform:scale(.94);}
.zoom-scale{min-width:48px;text-align:center;font-size:12px;color:#526071;font-variant-numeric:tabular-nums;}
.text-button{height:30px;border:1px solid #d8e0ea;border-radius:8px;background:#fff;color:#35445a;padding:0 12px;font:12px ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;cursor:pointer;}
.text-button:hover{background:#f3f7fb;}
.text-button:active{transform:scale(.97);}
#flowZoomStage{
    flex:1 1 auto;
    overflow:hidden;
    position:relative;
    min-height:0;
    background:
        linear-gradient(rgba(248,250,253,.55),rgba(248,250,253,.55)),
        repeating-linear-gradient(45deg,#f7fafc 0,#f7fafc 14px,#f2f6fa 14px,#f2f6fa 28px);
    cursor:grab;
    user-select:none;
}
#flowZoomStage.panning{cursor:grabbing;}
#canvasInner{
    position:absolute;
    left:0;
    top:0;
}
#canvasInner .automation-flow-visual{width:max-content;margin:0;}
#canvasInner .flow-track{overflow:visible!important;}
#canvasInner .flow-step{flex-basis:clamp(340px,30vw,560px);}
.zoom-hint{font-size:11px;color:#9aa7b8;margin-right:6px;white-space:nowrap;}
__FLOW_VISUAL_CSS__
</style>
<script>
var flowZoomScale=1,flowPanX=0,flowPanY=0;
function post(type,payload){if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(Object.assign({type:type},payload||{}));}}
function clampFlowScale(scale){return Math.min(4,Math.max(.15,scale));}
// 应用画布视图：缩放用 CSS zoom（布局级缩放，浏览器按最终尺寸重新排版，文字矢量清晰），
// 平移用 left/top（不受内部 zoom 影响）。不得用 transform:scale()+will-change，那会把内容
// 提升为合成器纹理位图，放大时位图拉伸发糊。
function applyCanvasTransform(){
    var inner=document.getElementById('canvasInner');
    if(inner){
        inner.style.zoom=flowZoomScale;
        inner.style.left=flowPanX+'px';
        inner.style.top=flowPanY+'px';
    }
    var label=document.getElementById('zoomScaleLabel');
    if(label){label.textContent=Math.round(flowZoomScale*100)+'%';}
}
// 锚点缩放：保持画布中 (cx,cy) 点在缩放前后位于同一屏幕位置，滚轮缩放因此以鼠标为中心。
function flowZoomAt(cx,cy,nextScale){
    nextScale=clampFlowScale(nextScale);
    var ratio=nextScale/flowZoomScale;
    flowPanX=cx-(cx-flowPanX)*ratio;
    flowPanY=cy-(cy-flowPanY)*ratio;
    flowZoomScale=nextScale;
    applyCanvasTransform();
}
// 以画布中心为锚的绝对缩放（工具栏按钮使用）。
function applyFlowZoom(scale){
    var stage=document.getElementById('flowZoomStage');
    if(!stage){return;}
    flowZoomAt(stage.clientWidth/2,stage.clientHeight/2,scale);
}
// 基准内容尺寸（zoom=1 时的布局尺寸）：用屏幕矩形除以当前缩放反推。
function canvasBaseSize(){
    var stage=document.getElementById('flowZoomStage');
    var visual=stage&&stage.querySelector('.automation-flow-visual');
    if(!visual){return null;}
    var rect=visual.getBoundingClientRect();
    var scale=flowZoomScale||1;
    return {width:rect.width/scale,height:rect.height/scale};
}
// 复位视图：完整可见（适应宽高取小者，上限 1.5）并居中；双击与""复位视图""按钮共用。
function flowZoomFit(capScale){
    var stage=document.getElementById('flowZoomStage');
    var size=canvasBaseSize();
    if(!stage||!size||!size.width){return;}
    var scale=Math.min((stage.clientWidth-56)/size.width,(stage.clientHeight-56)/size.height);
    if(capScale&&scale>capScale){scale=capScale;}
    flowZoomScale=clampFlowScale(scale);
    flowPanX=(stage.clientWidth-size.width*flowZoomScale)/2;
    flowPanY=(stage.clientHeight-size.height*flowZoomScale)/2;
    applyCanvasTransform();
}
// 原始大小：100% 并居中。
function flowZoomActual(){
    var stage=document.getElementById('flowZoomStage');
    var size=canvasBaseSize();
    if(!stage||!size){return;}
    flowZoomScale=1;
    flowPanX=(stage.clientWidth-size.width)/2;
    flowPanY=(stage.clientHeight-size.height)/2;
    applyCanvasTransform();
}
document.addEventListener('DOMContentLoaded',function(){
    var stage=document.getElementById('flowZoomStage');
    document.getElementById('zoomIn').addEventListener('click',function(){applyFlowZoom(flowZoomScale*1.25);});
    document.getElementById('zoomOut').addEventListener('click',function(){applyFlowZoom(flowZoomScale/1.25);});
    document.getElementById('zoomReset').addEventListener('click',flowZoomActual);
    document.getElementById('zoomFit').addEventListener('click',function(){flowZoomFit(1.5);});
    document.getElementById('zoomClose').addEventListener('click',function(){post('closeFlowZoom');});
    document.addEventListener('keydown',function(e){if(e.key==='Escape'){post('closeFlowZoom');}});
    // 画布滚轮缩放：以鼠标位置为锚。
    stage.addEventListener('wheel',function(e){
        e.preventDefault();
        var rect=stage.getBoundingClientRect();
        flowZoomAt(e.clientX-rect.left,e.clientY-rect.top,flowZoomScale*Math.exp(-e.deltaY*0.0016));
    },{passive:false});
    // 画布按住拖动平移。
    var panning=false,panStartX=0,panStartY=0,panOriginX=0,panOriginY=0;
    stage.addEventListener('mousedown',function(e){
        if(e.button!==0){return;}
        panning=true;
        stage.classList.add('panning');
        panStartX=e.clientX;panStartY=e.clientY;
        panOriginX=flowPanX;panOriginY=flowPanY;
        e.preventDefault();
    });
    window.addEventListener('mousemove',function(e){
        if(!panning){return;}
        flowPanX=panOriginX+(e.clientX-panStartX);
        flowPanY=panOriginY+(e.clientY-panStartY);
        applyCanvasTransform();
    });
    window.addEventListener('mouseup',function(){
        if(panning){panning=false;stage.classList.remove('panning');}
    });
    // 双击复位视图。
    stage.addEventListener('dblclick',function(){flowZoomFit(1.5);});
    // 无边框窗口：按住工具栏空白处拖动整窗（Win32 标题栏拖动循环）。
    var toolbar=document.querySelector('.zoom-toolbar');
    if(toolbar){
        toolbar.addEventListener('mousedown',function(e){
            if(e.button!==0){return;}
            var node=e.target;
            while(node&&node!==toolbar){
                if(node.tagName==='BUTTON'||node.classList.contains('zoom-scale')){return;}
                node=node.parentElement;
            }
            if(node===toolbar){post('dragFlowZoom');}
        });
    }
    window.requestAnimationFrame(function(){flowZoomFit(1.5);});
});
</script>
</head>
<body>
<div class=""zoom-toolbar"">
  <span class=""zoom-title"">流程结构</span>
  <span class=""zoom-hint"">滚轮缩放 · 按住拖动 · 双击复位</span>
  <div class=""zoom-group"">
    <button class=""icon-button"" id=""zoomOut"" title=""缩小"" aria-label=""缩小""><svg viewBox=""0 0 24 24""><circle cx=""11"" cy=""11"" r=""7""/><path d=""m21 21-4.3-4.3""/><path d=""M8 11h6""/></svg></button>
    <span class=""zoom-scale"" id=""zoomScaleLabel"">100%</span>
    <button class=""icon-button"" id=""zoomIn"" title=""放大"" aria-label=""放大""><svg viewBox=""0 0 24 24""><circle cx=""11"" cy=""11"" r=""7""/><path d=""m21 21-4.3-4.3""/><path d=""M11 8v6""/><path d=""M8 11h6""/></svg></button>
    <button class=""text-button"" id=""zoomFit"" type=""button"">复位视图</button>
    <button class=""text-button"" id=""zoomReset"" type=""button"">原始大小</button>
    <button class=""icon-button"" id=""zoomClose"" title=""关闭"" aria-label=""关闭""><svg viewBox=""0 0 24 24""><path d=""M18 6 6 18""/><path d=""M6 6l12 12""/></svg></button>
  </div>
</div>
<div id=""flowZoomStage""><div id=""canvasInner"">__FLOW_CARDS__</div></div>
</body>
</html>";
            return template
                .Replace("__FLOW_VISUAL_CSS__", FlowVisualizationCss)
                .Replace("__FLOW_CARDS__", flowCardsHtml ?? string.Empty);
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

        // 流程结构卡片样式：对话内卡片与独立放大大页面共用，避免两份样式漂移。
        private const string FlowVisualizationCss = @"
.automation-flow-visual{
    border:1px solid #d8e2ee;
    border-radius:12px;
    background:rgba(248,250,252,.92);
    overflow:hidden;
    box-shadow:0 2px 10px rgba(31,45,61,.05);
}
.flow-visual-title{
    display:flex;
    align-items:center;
    justify-content:space-between;
    gap:8px;
    padding:7px 12px;
    color:#203047;
    background:linear-gradient(180deg,#f0f6fb,#eaf2f9);
    border-bottom:1px solid #d8e2ee;
    font-size:13px;
    font-weight:650;
}
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
.flow-process{padding:8px 10px 10px;}
.flow-process + .flow-process{border-top:1px solid #dfe7f1;}
.flow-process-head{display:flex;align-items:center;gap:6px;flex-wrap:wrap;margin-bottom:7px;}
.flow-process-name{font-weight:650;color:#102033;}
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
";

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
    background:linear-gradient(165deg,#f8fafd 0%,#f1f5fa 55%,#edf2f8 100%);
    background-attachment:fixed;
    font:14px/1.5 ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;
    overflow:hidden;
}
.scrollable::-webkit-scrollbar,.thinking-box::-webkit-scrollbar{width:8px;height:8px;}
.scrollable::-webkit-scrollbar-thumb,.thinking-box::-webkit-scrollbar-thumb{background:#c5d0e0;border-radius:8px;border:2px solid #f4f6fa;}
.scrollable::-webkit-scrollbar-track,.thinking-box::-webkit-scrollbar-track{background:transparent;}
.app-shell{height:100%;display:flex;flex-direction:column;background:transparent;}
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
    max-width:1320px;
    margin:0 auto;
    padding:5px 10px;
}
.task-home{position:relative;min-height:100%;max-width:1320px;margin:0 auto;padding:18px 18px 120px;}
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
    margin:0 0 11px;
    animation:message-in .12s ease-out both;
}
@keyframes message-in{
    from{opacity:.72;transform:translateY(3px);}
    to{opacity:1;transform:translateY(0);}
}
.msg.user{align-items:flex-end;}
.msg.assistant{align-items:flex-start;margin-left:4px;}
.msg.error{align-items:flex-start;margin-left:12px;}
.msg-head{display:flex;align-items:center;gap:5px;padding:0 2px;min-height:22px;}
.msg.user .msg-head{justify-content:flex-end;}
.msg-time{font-size:10.5px;color:#8b96a8;line-height:1.2;letter-spacing:.2px;}
.avatar{width:24px;height:24px;border-radius:8px;display:inline-flex;align-items:center;justify-content:center;flex:0 0 24px;color:#fff;font-size:9px;font-weight:700;letter-spacing:.2px;box-shadow:0 1px 4px rgba(31,45,61,.10);}
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
    color:#13293f;
    background:linear-gradient(145deg,#e9f4ff 0%,#d7ebfd 100%);
    border:1px solid rgba(36,111,181,.16);
    border-radius:15px 5px 15px 15px;
    padding:6px 11px;
    box-shadow:0 4px 14px rgba(36,111,181,.10);
    white-space:pre-wrap;
}
.msg.assistant .content{
    max-width:calc(98% - 14px);
    margin-left:14px;
    margin-right:2px;
    color:#1b2839;
    background:transparent;
    border:0;
    border-radius:0;
    padding:0;
    font-size:15px;
    line-height:1.62;
}
.msg.assistant.final-reveal{isolation:isolate;}
.msg.assistant.final-reveal .msg-head{
    animation:final-answer-head-in .34s cubic-bezier(.22,1,.36,1) both;
}
.msg.assistant.final-reveal .content{
    position:relative;
    overflow:hidden;
    transform-origin:18% 0;
    will-change:transform,opacity,box-shadow;
    animation:final-answer-card-in .56s cubic-bezier(.22,1,.36,1) both;
}
.msg.assistant.final-reveal .content::before{
    content:"""";
    position:absolute;
    z-index:2;
    left:0;
    top:13%;
    width:2px;
    height:74%;
    border-radius:0 2px 2px 0;
    pointer-events:none;
    background:linear-gradient(180deg,rgba(76,154,222,0),rgba(76,154,222,.72),rgba(76,154,222,0));
    transform:scaleY(0);
    transform-origin:center;
    animation:final-answer-accent .62s .08s cubic-bezier(.22,1,.36,1) both;
}
.msg.assistant.final-reveal .content::after{
    content:"""";
    position:absolute;
    z-index:1;
    inset:0;
    pointer-events:none;
    background:linear-gradient(108deg,transparent 16%,rgba(124,183,232,.10) 43%,rgba(255,255,255,.7) 50%,rgba(124,183,232,.08) 57%,transparent 84%);
    transform:translate3d(-112%,0,0);
    animation:final-answer-sheen .72s .04s cubic-bezier(.22,1,.36,1) both;
}
.msg.assistant.final-reveal .final-reveal-block{
    position:relative;
    z-index:0;
    opacity:0;
    transform:translate3d(0,5px,0);
    animation:final-answer-block-in .34s cubic-bezier(.22,1,.36,1) forwards;
    animation-delay:calc(110ms + var(--final-reveal-index,0) * 36ms);
}
@keyframes final-answer-head-in{
    from{opacity:0;transform:translate3d(-3px,4px,0);}
    to{opacity:1;transform:translate3d(0,0,0);}
}
@keyframes final-answer-card-in{
    0%{opacity:0;transform:translate3d(0,10px,0) scale(.992);}
    100%{opacity:1;transform:translate3d(0,0,0) scale(1);}
}
@keyframes final-answer-accent{
    0%{opacity:0;transform:scaleY(0);}
    42%{opacity:1;transform:scaleY(1);}
    100%{opacity:0;transform:scaleY(.72);}
}
@keyframes final-answer-sheen{
    0%{opacity:0;transform:translate3d(-112%,0,0);}
    28%{opacity:1;}
    100%{opacity:0;transform:translate3d(112%,0,0);}
}
@keyframes final-answer-block-in{
    from{opacity:0;transform:translate3d(0,5px,0);}
    to{opacity:1;transform:translate3d(0,0,0);}
}
.msg.error .content{
    color:#8f1d1d;
    background:#fff5f5;
    border:1px solid #f0caca;
    border-radius:12px 4px 12px 12px;
    padding:5px 10px;
}
.content>*:first-child{margin-top:0;}
.content>*:last-child{margin-bottom:0;}
a{color:#1f6fb2;text-decoration:none;}
a:hover{text-decoration:underline;}
p{margin:.4em 0;}
ul,ol{margin:.4em 0;padding-left:1.45em;}
li{margin:.18em 0;}
li + li{margin-top:.34em;}
li>p{margin:.2em 0;}
.msg.assistant .content p{margin:.42em 0;}
.msg.assistant .content ul,.msg.assistant .content ol{margin:.42em 0 .62em;padding-left:1.45em;}
.msg.assistant .content li{margin:.18em 0;}
.msg.assistant .content li + li{margin-top:.34em;}
.msg.assistant .content li>p{margin:.2em 0;}
.merged-part + .merged-part{margin-top:.52em;}
__FLOW_VISUAL_CSS__
.automation-flow-visual{margin:8px 0 4px;}
.flow-expand-button{
    width:24px;
    height:24px;
    flex:0 0 24px;
    margin-left:auto;
    border:1px solid #cfdce9;
    border-radius:6px;
    background:#fff;
    color:#526071;
    cursor:pointer;
    display:inline-flex;
    align-items:center;
    justify-content:center;
    padding:0;
    transition:color .14s ease,background .14s ease,border-color .14s ease;
}
.flow-expand-button svg{width:14px;height:14px;stroke:currentColor;stroke-width:2;fill:none;stroke-linecap:round;stroke-linejoin:round;pointer-events:none;}
.flow-expand-button:hover{color:#1f5f99;background:#eef5fc;border-color:#9cc4ea;}
.flow-expand-button:active{transform:scale(.94);}
blockquote{
    margin:8px 0;
    padding:7px 12px;
    color:#48586e;
    background:#f2f7fc;
    border:0;
    border-left:3px solid #7fb3e0;
    border-radius:4px 8px 8px 4px;
}
h1,h2,h3{
    color:#101f31;
    line-height:1.3;
    font-weight:650;
    margin:.85em 0 .3em;
}
h1{font-size:21px;}
h2{font-size:18px;padding-bottom:3px;border-bottom:1px solid #e3eaf3;}
h3{font-size:16px;}
table{
    width:100%;
    margin:8px 0 10px;
    border-collapse:separate;
    border-spacing:0;
    table-layout:auto;
    border:1px solid #d9e3ef;
    border-radius:10px;
    overflow:hidden;
    background:rgba(255,255,255,.72);
    font-size:13.5px;
}
th,td{
    border:0;
    border-right:1px solid #e6ecf4;
    border-bottom:1px solid #e6ecf4;
    padding:7px 10px;
    vertical-align:top;
    text-align:left;
    word-break:break-word;
    overflow-wrap:anywhere;
}
th:last-child,td:last-child{border-right:0;}
tbody tr:last-child td{border-bottom:0;}
th{
    color:#28425f;
    background:#eff5fb;
    font-weight:650;
}
tr:nth-child(even) td{background:#f7fbfe;}
pre{
    margin:8px 0;
    padding:12px 14px;
    color:#e7edf6;
    background:#222b3a;
    border:1px solid rgba(255,255,255,.05);
    border-radius:10px;
    overflow-x:auto;
    white-space:pre;
    overflow-wrap:normal;
    word-break:normal;
    tab-size:4;
    box-shadow:inset 0 1px 0 rgba(255,255,255,.04),0 2px 8px rgba(20,28,42,.12);
}
code{
    color:#1d5c9c;
    background:#e9f2fb;
    border:1px solid #d3e4f3;
    border-radius:5px;
    padding:1px 5px;
    font:13px/1.4 Consolas,""Cascadia Mono"",monospace;
}
pre code{color:inherit;background:transparent;border:0;padding:0;font-variant-ligatures:none;}
img{max-width:100%;border-radius:8px;}
hr{border:none;border-top:1px solid #dfe6ef;margin:8px 0;}
.thinking-box{
    width:100%;
    max-height:calc(100vh - 150px);
    overflow-y:auto;
    background:rgba(255,255,255,.82);
    border:1px solid #e2eaf4;
    border-radius:10px;
    margin:4px 0 8px;
    padding:0;
    box-shadow:0 4px 18px rgba(31,45,61,.06);
    backdrop-filter:blur(10px);
}
.thinking-box.collapsed{max-height:32px;overflow:hidden;}
.thinking-box .reasoning-text{margin:6px 10px;color:#445268;font-size:13px;line-height:1.55;}
.thinking-box .toggle-bar{
    position:sticky;
    top:0;
    z-index:1;
    color:#526071;
    background:#f4f7fb;
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
    margin:2px 6px;
    padding:2px 8px;
    border-radius:6px;
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
    font-size:15px;
    line-height:1.62;
}
.stream-markdown>*:first-child{margin-top:0;}
.stream-markdown>*:last-child{margin-bottom:0;}
.stream-markdown p{margin:.42em 0;}
.stream-markdown ul,.stream-markdown ol{margin:.42em 0 .62em;padding-left:1.45em;}
.stream-markdown li{margin:.18em 0;}
.stream-markdown li + li{margin-top:.34em;}
.stream-markdown li>p{margin:.2em 0;}
.stream-raw{white-space:pre-wrap;}
.streaming-segment.is-streaming::after{
    content:"";
    display:inline-block;
    width:5px;
    height:13px;
    margin-left:2px;
    vertical-align:-2px;
    border-radius:2px;
    background:#246fb5;
    animation:cursor-blink .8s infinite;
}
@keyframes cursor-blink{50%{opacity:.18;}}
.composer-wrap{padding:8px 14px 10px;background:linear-gradient(to top,rgba(241,245,250,.98) 62%,rgba(241,245,250,0));}
.composer{max-width:1320px;margin:0 auto;background:rgba(255,255,255,.92);border:1px solid #e2e7ef;border-radius:16px;min-height:92px;box-shadow:0 6px 16px rgba(16,24,40,.06);position:relative;padding:10px 50px 34px 12px;backdrop-filter:blur(14px);transition:border-color .18s ease,box-shadow .18s ease;}
.composer:focus-within{border-color:#9cc4ea;box-shadow:0 8px 22px rgba(16,24,40,.08),0 0 0 3px rgba(36,111,181,.10);}
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
.test-list{display:flex;flex-direction:column;gap:12px;}
.test-item{padding:12px;border:1px solid #dde5ef;border-radius:10px;background:#fff;}
.test-item:hover{border-color:#a8c4e1;background:#f8fbff;}
.test-head{display:grid;grid-template-columns:22px 1fr;gap:9px;align-items:start;cursor:pointer;}
.test-head input{margin-top:3px;}
.test-name{font-size:13px;font-weight:650;color:#25354a;}
.test-desc{margin-top:3px;font-size:12px;line-height:1.5;color:#748195;}
.test-customized{margin-left:7px;color:#1f6fb2;font-size:11px;font-weight:500;}
.test-prompts{display:flex;flex-direction:column;gap:7px;margin:10px 0 0 31px;}
.test-prompt-row{display:grid;grid-template-columns:34px minmax(0,1fr) 30px;gap:7px;align-items:start;}
.test-turn-label{padding-top:8px;font-size:11px;color:#758398;}
.test-prompt{box-sizing:border-box;width:100%;min-height:58px;max-height:150px;resize:vertical;border:1px solid #d8e0ea;border-radius:8px;padding:7px 9px;background:#fff;color:#24354a;font:12px/1.5 ""Segoe UI"",""Microsoft YaHei"",Arial,sans-serif;}
.test-prompt:focus{outline:none;border-color:#5797cf;box-shadow:0 0 0 2px rgba(57,132,198,.12);}
.test-remove-prompt{width:30px;height:30px;border:1px solid #d8e0ea;border-radius:7px;background:#fff;color:#7d8898;cursor:pointer;}
.test-remove-prompt:hover:not(:disabled){color:#ad3540;border-color:#e7a7ad;background:#fff7f8;}
.test-remove-prompt:disabled{opacity:.35;cursor:default;}
.test-add-prompt{align-self:flex-start;margin-left:41px;border:0;background:transparent;color:#2477b8;font-size:12px;cursor:pointer;padding:5px 0;}
.test-warning{margin-bottom:10px;padding:9px 11px;border-radius:8px;background:#fff1f1;color:#9b3540;font-size:12px;line-height:1.5;}
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
@media (prefers-reduced-motion:reduce){
    .msg,
    .msg.assistant.final-reveal .msg-head,
    .msg.assistant.final-reveal .content,
    .msg.assistant.final-reveal .content::before,
    .msg.assistant.final-reveal .content::after,
    .msg.assistant.final-reveal .final-reveal-block{
        animation:none!important;
        opacity:1!important;
        transform:none!important;
    }
    .streaming-segment.is-streaming::after{animation:none;}
}
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
// 智能跟随：只有视口本身就在底部附近时才滚动，用户上翻阅读历史时不打断。
function scrollMessagesToBottom(force){
    if(messageScrollFrame){window.cancelAnimationFrame(messageScrollFrame);}
    messageScrollFrame=window.requestAnimationFrame(function(){
        messageScrollFrame=0;
        var m=byId('messagesScroll');
        if(!m){return;}
        if(force||m.scrollHeight-m.scrollTop-m.clientHeight<140){m.scrollTop=m.scrollHeight;}
    });
}
function commonPrefix(left,right){
    var limit=Math.min(left.length,right.length),index=0;
    while(index<limit&&left.charAt(index)===right.charAt(index)){index++;}
    return left.slice(0,index);
}
// 流式打字机：宿主按节流帧推送目标文本（稳定 Markdown + 未完成尾部），
// 前端 18ms 一帧逐字消耗尾部增量，落后时按剩余量加大步长平滑追赶。
var streamTypewriters={};
function ensureStreamSlots(id){
    var el=document.getElementById(id);
    if(!el){return null;}
    var md=el.querySelector(':scope > .stream-markdown');
    var raw=el.querySelector(':scope > .stream-raw');
    if(!md||!raw){
        el.innerHTML='<div class=""stream-markdown""></div><span class=""stream-raw""></span>';
        md=el.querySelector(':scope > .stream-markdown');
        raw=el.querySelector(':scope > .stream-raw');
    }
    return {el:el,md:md,raw:raw};
}
function followStreamScroll(el){
    if(!el||!el.isConnected){return;}
    var box=el.closest('.thinking-box');
    if(box&&!box.classList.contains('collapsed')&&box.scrollHeight-box.scrollTop-box.clientHeight<140){
        box.scrollTop=box.scrollHeight;
    }
    var m=byId('messagesScroll');
    if(m&&m.scrollHeight-m.scrollTop-m.clientHeight<140){m.scrollTop=m.scrollHeight;}
}
function updateStreamSegment(id,stableSource,markdownHtml,rawTarget,instant){
    var slots=ensureStreamSlots(id);
    if(!slots){return false;}
    var state=streamTypewriters[id];
    if(!state){
        state=streamTypewriters[id]={displayed:'',target:'',frame:0,pendingFinal:null,pendingPromote:null,stableSource:null};
    }
    if(state.stableSource!==stableSource){
        slots.md.innerHTML=markdownHtml||'';
        state.stableSource=stableSource;
    }
    state.target=rawTarget||'';
    if(instant){state.displayed=state.target;}
    if(state.target.indexOf(state.displayed)!==0){
        state.displayed=commonPrefix(state.displayed,state.target);
    }
    slots.raw.textContent=state.displayed;
    slots.el.classList.add('is-streaming');
    if(!state.frame&&state.displayed.length<state.target.length){
        state.frame=window.setTimeout(function(){runStreamTypewriter(id);},18);
    }else if(!state.frame){
        settleStreamSegment(id,state,slots);
    }
    return true;
}
function runStreamTypewriter(id){
    var state=streamTypewriters[id];
    if(!state){return;}
    state.frame=0;
    var target=state.target||'';
    var displayed=state.displayed||'';
    if(target.indexOf(displayed)!==0){displayed=commonPrefix(displayed,target);}
    var remaining=target.slice(displayed.length);
    var slots=ensureStreamSlots(id);
    if(!slots){delete streamTypewriters[id];return;}
    if(!remaining){
        state.displayed=displayed;
        slots.raw.textContent=displayed;
        settleStreamSegment(id,state,slots);
        return;
    }
    var chars=Array.from(remaining);
    var count=chars.length>120?3:chars.length>48?2:1;
    displayed+=chars.slice(0,count).join('');
    state.displayed=displayed;
    slots.raw.textContent=displayed;
    followStreamScroll(slots.el);
    state.frame=window.setTimeout(function(){runStreamTypewriter(id);},18);
}
// 打字机追平目标后按待办收尾：优先提升为正式气泡（内含完整最终 HTML），否则应用段内最终 Markdown。
function settleStreamSegment(id,state,slots){
    if(state.pendingPromote!==null){
        var messageHtml=state.pendingPromote;
        state.pendingPromote=null;
        delete streamTypewriters[id];
        promoteStreamSegmentNow(id,messageHtml);
        return;
    }
    if(state.pendingFinal!==null){
        var finalHtml=state.pendingFinal;
        state.pendingFinal=null;
        delete streamTypewriters[id];
        slots.el.classList.remove('is-streaming');
        slots.el.innerHTML=finalHtml;
        followStreamScroll(slots.el);
    }
}
function finalizeStreamSegment(id,stableSource,markdownHtml,rawTarget,finalHtml,instant){
    var state=streamTypewriters[id];
    if(!state){
        var slots=ensureStreamSlots(id);
        if(slots){
            slots.el.classList.remove('is-streaming');
            slots.el.innerHTML=finalHtml;
            followStreamScroll(slots.el);
        }
        return;
    }
    state.pendingFinal=finalHtml;
    updateStreamSegment(id,stableSource,markdownHtml,rawTarget,instant);
}
function promoteStreamSegment(id,messageHtml){
    var state=streamTypewriters[id];
    if(!state){
        promoteStreamSegmentNow(id,messageHtml);
        return;
    }
    state.pendingPromote=messageHtml;
    if(!state.frame){
        var slots=ensureStreamSlots(id);
        if(!slots){
            delete streamTypewriters[id];
            promoteStreamSegmentNow(id,messageHtml);
            return;
        }
        settleStreamSegment(id,state,slots);
    }
}
function promoteStreamSegmentNow(id,messageHtml){
    var el=document.getElementById(id);
    var box=el&&el.closest('.thinking-box');
    if(el){el.remove();}
    var messages=document.getElementById('messages'),finalMessage=null;
    if(messages){messages.insertAdjacentHTML('beforeend',messageHtml);finalMessage=messages.lastElementChild;}
    if(box&&box.querySelectorAll(':scope > :not(.toggle-bar)').length===0){box.remove();}
    if(window.revealFinalAnswer){revealFinalAnswer(finalMessage);}
    if(window.scrollMessagesToBottom){scrollMessagesToBottom();}
}
// 流程结构放大查看：WebView 视口被限制在助手面板内，真正的大页面由宿主打开独立最大化窗口（FrmFlowZoom）。
function openFlowZoom(button){
    var visual=button.closest('.automation-flow-visual');
    if(!visual){return;}
    var clone=visual.cloneNode(true);
    var cloneButton=clone.querySelector('.flow-expand-button');
    if(cloneButton){cloneButton.remove();}
    post('openFlowZoom',{html:clone.outerHTML});
}
function revealFinalAnswer(message){
    if(!message){return;}
    var content=message.querySelector('.content');
    if(!content||message.dataset.finalReveal==='done'){return;}
    message.dataset.finalReveal='done';
    if(window.matchMedia&&window.matchMedia('(prefers-reduced-motion: reduce)').matches){
        scrollMessagesToBottom();
        return;
    }
    var blocks=Array.prototype.slice.call(content.children,0,8);
    blocks.forEach(function(block,index){
        block.classList.add('final-reveal-block');
        block.style.setProperty('--final-reveal-index',String(Math.min(index,6)));
    });
    message.classList.add('final-reveal');
    scrollMessagesToBottom();
    window.setTimeout(function(){
        if(!message.isConnected){return;}
        message.classList.remove('final-reveal');
        blocks.forEach(function(block){
            block.classList.remove('final-reveal-block');
            block.style.removeProperty('--final-reveal-index');
        });
        scrollMessagesToBottom();
    },900);
}
function scrollThinkingBoxToBottom(boxId){
    var box=document.getElementById(boxId);
    if(!box||box.classList.contains('collapsed')){return;}
    if(box._scrollFrame){window.cancelAnimationFrame(box._scrollFrame);}
    box._scrollFrame=window.requestAnimationFrame(function(){
        box._scrollFrame=0;
        if(!box.isConnected||box.classList.contains('collapsed')){return;}
        resizeThinkingBox(box);
        if(box.scrollHeight-box.scrollTop-box.clientHeight<140){box.scrollTop=box.scrollHeight;}
        var messages=byId('messagesScroll');
        if(messages&&messages.scrollHeight-messages.scrollTop-messages.clientHeight<140){messages.scrollTop=messages.scrollHeight;}
    });
}
var forcedStreamScrollFrame=0;
var forcedStreamScrollActive=false;
function runForcedStreamScroll(){
    if(!forcedStreamScrollActive){forcedStreamScrollFrame=0;return;}
    document.querySelectorAll('.thinking-box:not(.collapsed)').forEach(function(box){
        if(box.scrollHeight-box.scrollTop-box.clientHeight<140){box.scrollTop=box.scrollHeight;}
    });
    var messages=byId('messagesScroll');
    if(messages&&messages.scrollHeight-messages.scrollTop-messages.clientHeight<140){messages.scrollTop=messages.scrollHeight;}
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
        temperature:parseFloat(byId('cfgTemperature').value||'0.3'),
        apiKey:byId('cfgApiKey').value,
        maxTurns:parseInt(byId('cfgTurns').value||'1',10),
        maxOutputTokens:parseInt(byId('cfgOutputTokens').value||'16384',10),
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
    byId('cfgOutputTokens').value=c.maxOutputTokens||16384;
    byId('cfgTemperature').value=typeof c.temperature==='number'?c.temperature:0.3;
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
function refreshStandardTestRows(card){
    var rows=card.querySelectorAll('.test-prompt-row');
    Array.prototype.forEach.call(rows,function(row,index){
        row.querySelector('.test-turn-label').textContent='第'+(index+1)+'轮';
        row.querySelector('.test-remove-prompt').disabled=rows.length<=1;
    });
}
function appendStandardTestPrompt(card,value){
    var prompts=card.querySelector('.test-prompts');
    var row=document.createElement('div');row.className='test-prompt-row';
    var turn=document.createElement('span');turn.className='test-turn-label';
    var textarea=document.createElement('textarea');textarea.className='test-prompt';textarea.maxLength=4000;textarea.value=value||'';
    var remove=document.createElement('button');remove.type='button';remove.className='test-remove-prompt';remove.title='删除这一轮';remove.textContent='×';
    remove.addEventListener('click',function(){row.remove();refreshStandardTestRows(card);});
    row.appendChild(turn);row.appendChild(textarea);row.appendChild(remove);prompts.appendChild(row);refreshStandardTestRows(card);
}
function renderStandardTests(){
    var host=byId('testList');host.innerHTML='';
    if(appState.testScenarioWarning){var warning=document.createElement('div');warning.className='test-warning';warning.textContent=appState.testScenarioWarning;host.appendChild(warning);}
    (appState.testScenarios||[]).forEach(function(item,index){
        var card=document.createElement('div');card.className='test-item';card.dataset.id=item.id;
        var label=document.createElement('label');label.className='test-head';
        var input=document.createElement('input');input.className='test-selector';input.type='checkbox';input.value=item.id;input.checked=index<2;
        var body=document.createElement('div');
        var name=document.createElement('div');name.className='test-name';name.textContent=item.name;
        if(item.customized){var customized=document.createElement('span');customized.className='test-customized';customized.textContent='已自定义';name.appendChild(customized);}
        var desc=document.createElement('div');desc.className='test-desc';desc.textContent=item.description;
        body.appendChild(name);body.appendChild(desc);label.appendChild(input);label.appendChild(body);card.appendChild(label);
        var prompts=document.createElement('div');prompts.className='test-prompts';card.appendChild(prompts);
        (item.prompts||['']).forEach(function(prompt){appendStandardTestPrompt(card,prompt);});
        var add=document.createElement('button');add.type='button';add.className='test-add-prompt';add.textContent='+ 增加一轮';
        add.addEventListener('click',function(){if(card.querySelectorAll('.test-prompt-row').length>=12){showToast('每个场景最多12轮。');return;}appendStandardTestPrompt(card,'');});
        card.appendChild(add);host.appendChild(card);
    });
}
function collectStandardTestScenarios(selectedOnly){
    return Array.prototype.reduce.call(byId('testList').querySelectorAll('.test-item'),function(result,card){
        var selected=card.querySelector('.test-selector').checked;
        if(selectedOnly&&!selected){return result;}
        var prompts=Array.prototype.map.call(card.querySelectorAll('.test-prompt'),function(item){return item.value.trim();});
        result.push({id:card.dataset.id,prompts:prompts});return result;
    },[]);
}
function runStandardTests(){
    var scenarios=collectStandardTestScenarios(true);
    if(!scenarios.length){showToast('请至少选择一个测试场景。');return;}
    if(scenarios.some(function(item){return item.prompts.some(function(prompt){return !prompt;});})){showToast('测试语句不能为空。');return;}
    closeStandardTests();
    post('runStandardTests',{scenarios:scenarios,separateConversations:byId('separateTestConversations').checked});
}
function saveStandardTestPrompts(){
    var scenarios=collectStandardTestScenarios(false);
    if(scenarios.some(function(item){return item.prompts.some(function(prompt){return !prompt;});})){showToast('测试语句不能为空。');return;}
    post('saveStandardTestPrompts',{scenarios:scenarios});
}
function resetStandardTestPrompts(){if(window.confirm('确定恢复全部内置测试语句吗？已保存的自定义语句将被覆盖。')){post('resetStandardTestPrompts');}}
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
    scrollMessagesToBottom(true);
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
    byId('saveTestPrompts').addEventListener('click',saveStandardTestPrompts);
    byId('resetTestPrompts').addEventListener('click',resetStandardTestPrompts);
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
  <section class=""config-modal"" style=""width:min(820px,96vw)"">
    <div class=""modal-head""><div><div class=""modal-title"">标准测试</div><div class=""modal-desc"">选择场景并直接编辑每轮用户语句；夹具和验收规则仍使用该标准场景的定义。</div></div><button class=""icon-button"" id=""closeTests"" title=""关闭""><svg viewBox=""0 0 24 24""><path d=""M18 6 6 18""/><path d=""M6 6l12 12""/></svg></button></div>
    <div class=""modal-body scrollable""><div class=""test-list"" id=""testList""></div><div class=""test-options""><label><input type=""checkbox"" id=""separateTestConversations"" checked> 每个场景使用独立新对话</label>这些测试可能创建或修改流程配置。未开启自动批准时，仍需人工确认每次预演。</div></div>
    <div class=""modal-foot""><div class=""foot-left""><button class=""text-button"" id=""resetTestPrompts"">恢复默认语句</button><button class=""text-button"" id=""saveTestPrompts"">保存语句</button></div><div class=""foot-right""><button class=""text-button"" id=""cancelTests"">取消</button><button class=""primary-button"" id=""runTests"">开始测试</button></div></div>
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
