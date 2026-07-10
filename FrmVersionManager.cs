using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    /// <summary>
    /// 使用 WebView2 呈现的运行时版本管理模块。
    /// </summary>
    public sealed class FrmVersionManager : Form
    {
        private readonly WebView2 webView = new WebView2();
        private ConfigurationVersionLayer currentLayer = ConfigurationVersionLayer.Process;
        private string selectedCommitId;
        private bool compareWithPrevious;

        public FrmVersionManager()
        {
            Text = "版本管理";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(980, 650);
            Size = new Size(1280, 820);
            BackColor = Color.FromArgb(242, 245, 248);
            webView.Dock = DockStyle.Fill;
            Controls.Add(webView);
            Load += FrmVersionManager_Load;
        }

        private async void FrmVersionManager_Load(object sender, EventArgs e)
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
                webView.NavigateToString(BuildHtml());
            }
            catch (Exception ex)
            {
                MessageBox.Show("版本管理页面初始化失败：" + ex.Message, "版本管理", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                JObject request = JObject.Parse(e.TryGetWebMessageAsString());
                string action = request["action"]?.Value<string>();
                switch (action)
                {
                    case "ready":
                        PushState();
                        break;
                    case "layer":
                        currentLayer = string.Equals(request["layer"]?.Value<string>(), "equipment", StringComparison.OrdinalIgnoreCase)
                            ? ConfigurationVersionLayer.Equipment
                            : ConfigurationVersionLayer.Process;
                        selectedCommitId = null;
                        PushState();
                        break;
                    case "select":
                        selectedCommitId = request["commitId"]?.Value<string>();
                        PushState();
                        break;
                    case "compare":
                        compareWithPrevious = string.Equals(request["mode"]?.Value<string>(), "previous", StringComparison.OrdinalIgnoreCase);
                        PushState();
                        break;
                    case "snapshot":
                        CreateSnapshot(request["note"]?.Value<string>());
                        break;
                    case "restore":
                        RestoreSelected();
                        break;
                }
            }
            catch (Exception ex)
            {
                PushToast("操作失败：" + ex.Message, true);
            }
        }

        private void CreateSnapshot(string note)
        {
            string permission = currentLayer == ConfigurationVersionLayer.Process
                ? PermissionKeys.VersionProcessManage
                : PermissionKeys.VersionEquipmentManage;
            if (!SF.EnsurePermission(permission, "创建版本快照"))
            {
                return;
            }
            if (!SF.versionService.CreateManualSnapshot(currentLayer, note, SF.userSession?.Account?.UserName, out string error))
            {
                PushToast(error, true);
                return;
            }
            PushToast("快照已创建。", false);
            PushState();
        }

        private void RestoreSelected()
        {
            string permission = currentLayer == ConfigurationVersionLayer.Process
                ? PermissionKeys.VersionProcessManage
                : PermissionKeys.VersionEquipmentManage;
            if (!SF.EnsurePermission(permission, "还原版本"))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(selectedCommitId))
            {
                PushToast("请先选择一个版本。", true);
                return;
            }
            string layerName = currentLayer == ConfigurationVersionLayer.Process ? "工艺层" : "设备层";
            string extra = currentLayer == ConfigurationVersionLayer.Equipment ? "\r\n设备层还原后必须重启程序，重启前禁止启动流程。" : "";
            if (MessageBox.Show("确认还原 " + layerName + " 到版本 " + selectedCommitId.Substring(0, Math.Min(8, selectedCommitId.Length)) + "？\r\n还原前会自动创建保护点。" + extra,
                "确认还原", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            bool success = SF.versionService.Restore(currentLayer, selectedCommitId, SF.userSession?.Account?.UserName,
                () => SF.mainfrm.AreAllProcessesStopped(),
                () => SF.mainfrm.ReloadProcessVersionedConfiguration(),
                () => SF.mainfrm.RequireRestartAfterEquipmentRestore(),
                out string error);
            if (!success)
            {
                PushToast(error, true);
                return;
            }
            PushToast(currentLayer == ConfigurationVersionLayer.Process ? "工艺层已还原并重新加载。" : "设备层已还原，必须重启程序后才可继续运行。", false);
            PushState();
        }

        private void PushState()
        {
            if (webView.CoreWebView2 == null || SF.versionService == null)
            {
                return;
            }
            string permission = currentLayer == ConfigurationVersionLayer.Process
                ? PermissionKeys.VersionProcessManage
                : PermissionKeys.VersionEquipmentManage;
            IReadOnlyList<ConfigurationVersionRecord> history = SF.versionService.GetHistory(currentLayer, out bool dirty, out string historyError);
            if (string.IsNullOrWhiteSpace(selectedCommitId) && history.Count > 0)
            {
                selectedCommitId = history[0].CommitId;
            }
            List<ConfigurationVersionDiffEntry> diff = new List<ConfigurationVersionDiffEntry>();
            string diffError = null;
            if (!string.IsNullOrWhiteSpace(selectedCommitId))
            {
                diff = SF.versionService.GetStructuredDiff(currentLayer, selectedCommitId, compareWithPrevious, out diffError).ToList();
            }
            JObject state = new JObject
            {
                ["layer"] = currentLayer == ConfigurationVersionLayer.Process ? "process" : "equipment",
                ["canManage"] = SF.HasPermission(permission),
                ["dirty"] = dirty,
                ["mustRestart"] = SF.VersionRestartRequired,
                ["selectedCommitId"] = selectedCommitId,
                ["compareMode"] = compareWithPrevious ? "previous" : "current",
                ["historyError"] = historyError,
                ["diffError"] = diffError,
                ["history"] = JArray.FromObject(history.Select(item => new
                {
                    commitId = item.CommitId,
                    message = item.Message,
                    author = item.Author,
                    time = item.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    type = item.SnapshotType
                })),
                ["diff"] = JArray.FromObject(diff)
            };
            ExecuteScript("setState(" + state.ToString(Formatting.None) + ");");
        }

        private void PushToast(string message, bool isError)
        {
            ExecuteScript("showToast(" + JsonConvert.SerializeObject(message ?? string.Empty) + "," + (isError ? "true" : "false") + ");");
        }

        private void ExecuteScript(string script)
        {
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.ExecuteScriptAsync(script);
            }
        }

        private static string BuildHtml()
        {
            return """
<!doctype html><html lang='zh-CN'><head><meta charset='utf-8'><style>
*{box-sizing:border-box}body{margin:0;background:#f3f6f8;color:#19324a;font:14px 'Microsoft YaHei UI','Microsoft YaHei',sans-serif}button,input{font:inherit}button{cursor:pointer}.shell{height:100vh;display:flex;flex-direction:column}.top{height:72px;background:#fff;border-bottom:1px solid #dbe4ec;display:flex;align-items:center;padding:0 28px;gap:26px}.brand{font-size:19px;font-weight:700;color:#17334a}.brand small{display:block;margin-top:4px;font-size:12px;color:#7990a2;font-weight:400}.tabs{display:flex;align-self:stretch}.tab{min-width:126px;border:0;border-bottom:3px solid transparent;background:transparent;color:#5b7182}.tab.active{border-bottom-color:#159b8c;color:#0b6e66;font-weight:700}.restart{margin-left:auto;color:#a54d1d;background:#fff2e9;border:1px solid #f0c7ad;padding:7px 10px;border-radius:5px}.main{min-height:0;flex:1;display:grid;grid-template-columns:420px minmax(0,1fr);gap:16px;padding:16px}.panel{background:#fff;border:1px solid #dce5ec;border-radius:7px;min-height:0}.versions{display:flex;flex-direction:column}.toolbar{padding:16px;border-bottom:1px solid #e4ebf0}.title{font-weight:700;font-size:16px}.hint{margin-top:6px;color:#6d8292;font-size:12px}.actions{display:flex;margin-top:14px;gap:8px}.note{min-width:0;flex:1;height:34px;border:1px solid #cbd8e2;border-radius:4px;padding:0 9px}.primary{height:34px;background:#137d73;color:#fff;border:1px solid #137d73;border-radius:4px;padding:0 12px}.primary:disabled{background:#b7c7ce;border-color:#b7c7ce;cursor:not-allowed}.dirty{display:inline-block;margin-top:10px;color:#b64724;font-size:12px}.clean{display:inline-block;margin-top:10px;color:#32785d;font-size:12px}.history{overflow:auto;min-height:0;flex:1}.version{padding:13px 16px;border-bottom:1px solid #edf1f4;cursor:pointer}.version:hover{background:#f7fbfc}.version.selected{background:#e9f7f5;border-left:3px solid #159b8c;padding-left:13px}.version-main{display:flex;gap:8px;align-items:center}.badge{font-size:11px;color:#166e66;background:#d8f1ed;border-radius:3px;padding:2px 5px;white-space:nowrap}.message{font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.meta{margin-top:7px;color:#718595;font-size:12px;display:flex;justify-content:space-between;gap:8px}.detail{display:flex;flex-direction:column;min-width:0}.detail-head{padding:18px 20px;border-bottom:1px solid #e4ebf0;display:flex;gap:12px;align-items:center}.detail-head strong{font-size:16px}.seg{margin-left:auto;display:flex;border:1px solid #cbd8e2;border-radius:4px;overflow:hidden}.seg button{border:0;border-right:1px solid #cbd8e2;background:#fff;padding:7px 10px;color:#5d7485}.seg button:last-child{border-right:0}.seg button.active{background:#e9f7f5;color:#0b6e66;font-weight:700}.restore{background:#c2512c;border:1px solid #c2512c;color:#fff;border-radius:4px;height:34px;padding:0 13px}.restore:disabled{background:#cbd4d9;border-color:#cbd4d9;cursor:not-allowed}.diff{overflow:auto;padding:0 20px 20px;min-height:0;flex:1}.empty{padding:48px 10px;color:#7890a0;text-align:center}.row{display:grid;grid-template-columns:92px 92px minmax(160px,1.2fr) minmax(160px,1fr) minmax(160px,1fr);border-bottom:1px solid #e8eef2;align-items:start}.cell{padding:12px 9px;word-break:break-word;line-height:1.45}.headrow{position:sticky;top:0;background:#f6f9fb;font-weight:700;color:#537084}.change{font-weight:700}.change.新增{color:#178056}.change.删除{color:#bd4d4a}.change.修改{color:#a16a05}.toast{position:fixed;right:22px;bottom:20px;background:#23465e;color:#fff;border-radius:5px;padding:10px 14px;box-shadow:0 4px 16px #0003;display:none;z-index:3}.toast.error{background:#a33f3f}@media(max-width:900px){.main{grid-template-columns:1fr}.versions{max-height:290px}.top{padding:0 14px;gap:10px}.brand{font-size:16px}.tab{min-width:92px}.row{grid-template-columns:80px 76px minmax(140px,1fr) minmax(140px,1fr) minmax(140px,1fr)}}
</style></head><body><div class='shell'><header class='top'><div class='brand'>版本管理<small>运行时配置历史</small></div><div class='tabs'><button class='tab' id='processTab' onclick="post('layer',{layer:'process'})">工艺层</button><button class='tab' id='equipmentTab' onclick="post('layer',{layer:'equipment'})">设备层</button></div><span class='restart' id='restart' hidden>设备配置已还原，必须重启</span></header><main class='main'><section class='panel versions'><div class='toolbar'><div class='title' id='layerTitle'></div><div class='hint' id='layerHint'></div><div class='actions'><input id='note' class='note' maxlength='80' placeholder='快照备注（可选）'><button class='primary' id='snapshot' onclick="post('snapshot',{note:byId('note').value})">创建快照</button></div><span id='dirty'></span></div><div class='history' id='history'></div></section><section class='panel detail'><div class='detail-head'><div><strong>结构化差异</strong><div class='hint' id='compareHint'></div></div><div class='seg'><button id='currentMode' onclick="post('compare',{mode:'current'})">与当前配置</button><button id='previousMode' onclick="post('compare',{mode:'previous'})">与上一版本</button></div><button class='restore' id='restore' onclick="post('restore')">还原此版本</button></div><div class='diff' id='diff'></div></section></main></div><div class='toast' id='toast'></div><script>var state={};function byId(id){return document.getElementById(id)}function post(action,data){window.chrome.webview.postMessage(JSON.stringify(Object.assign({action:action},data||{})))}function esc(v){return String(v==null?'':v).replace(/[&<>"']/g,function(c){return({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[c]})}function setState(s){state=s||{};var process=state.layer==='process';byId('processTab').classList.toggle('active',process);byId('equipmentTab').classList.toggle('active',!process);byId('layerTitle').textContent=process?'工艺层版本':'设备层版本';byId('layerHint').textContent=process?'流程、变量与数据结构。还原后立即重新加载。':'除账户外的运行时设备配置。还原后必须重启。';byId('restart').hidden=!state.mustRestart;byId('snapshot').disabled=!state.canManage;byId('note').disabled=!state.canManage;byId('restore').disabled=!state.canManage||!state.selectedCommitId;byId('dirty').className=state.dirty?'dirty':'clean';byId('dirty').textContent=state.dirty?'当前配置存在未快照变更':'当前配置已与最新快照一致';byId('currentMode').classList.toggle('active',state.compareMode==='current');byId('previousMode').classList.toggle('active',state.compareMode==='previous');byId('compareHint').textContent=state.compareMode==='previous'?'所选版本与其上一版本':'所选版本与当前配置';renderHistory();renderDiff();if(state.historyError)showToast(state.historyError,true);if(state.diffError)showToast(state.diffError,true)}function renderHistory(){var a=state.history||[];byId('history').innerHTML=a.length?a.map(function(v){return '<div class="version '+(v.commitId===state.selectedCommitId?'selected':'')+'" onclick="post(\'select\',{commitId:\''+esc(v.commitId)+'\'})"><div class="version-main"><span class="badge">'+esc(v.type)+'</span><span class="message">'+esc(v.message)+'</span></div><div class="meta"><span>'+esc(v.author||'系统')+'</span><span>'+esc(v.time)+'</span></div><div class="meta"><span>'+esc((v.commitId||'').slice(0,12))+'</span></div></div>'}).join(''):'<div class="empty">暂无版本记录</div>'}function renderDiff(){var a=state.diff||[];if(!a.length){byId('diff').innerHTML='<div class="empty">没有结构化差异</div>';return}var h='<div class="row headrow"><div class="cell">类别</div><div class="cell">变更</div><div class="cell">目标 / 字段</div><div class="cell">原值</div><div class="cell">新值</div></div>';byId('diff').innerHTML=h+a.map(function(v){return '<div class="row"><div class="cell">'+esc(v.Category)+'</div><div class="cell change '+esc(v.ChangeType)+'">'+esc(v.ChangeType)+'</div><div class="cell">'+esc(v.Target)+'<br><span class="hint">'+esc(v.FieldPath)+'</span></div><div class="cell">'+esc(v.Before)+'</div><div class="cell">'+esc(v.After)+'</div></div>'}).join('')}function showToast(message,error){var t=byId('toast');t.textContent=message;t.className='toast '+(error?'error':'');t.style.display='block';clearTimeout(window.toastTimer);window.toastTimer=setTimeout(function(){t.style.display='none'},3800)}post('ready')</script></body></html>
""";
        }
    }
}
