using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    /// <summary>
    /// 使用 WebView2 呈现的运行时版本管理模块。
    /// </summary>
    public sealed partial class FrmVersionManager : Form
    {
        private readonly WebView2 webView = new WebView2
        {
            // WebView2 用户数据目录放到 LocalAppData，避免在程序目录下生成缓存文件夹。
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Automation", "WebView2")
            }
        };
        private ConfigurationVersionLayer currentLayer = ConfigurationVersionLayer.Process;
        private string selectedCommitId;
        private bool compareWithPrevious;
        private bool operationInProgress;
        private int stateRequestVersion;

        private sealed class VersionPageState
        {
            public string SelectedCommitId { get; set; }
            public JObject Payload { get; set; }
        }

        public FrmVersionManager()
        {
            Text = "版本管理";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 680);
            Size = new Size(1320, 840);
            BackColor = UiPalette.InputFocused;
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

        private async void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                JObject request = JObject.Parse(e.TryGetWebMessageAsString());
                string action = request["action"]?.Value<string>();
                switch (action)
                {
                    case "ready":
                        await PushStateAsync();
                        break;
                    case "layer":
                        currentLayer = string.Equals(request["layer"]?.Value<string>(), "equipment", StringComparison.OrdinalIgnoreCase)
                            ? ConfigurationVersionLayer.Equipment
                            : ConfigurationVersionLayer.Process;
                        selectedCommitId = null;
                        await PushStateAsync();
                        break;
                    case "select":
                        selectedCommitId = request["commitId"]?.Value<string>();
                        await PushStateAsync();
                        break;
                    case "compare":
                        compareWithPrevious = string.Equals(request["mode"]?.Value<string>(), "previous", StringComparison.OrdinalIgnoreCase);
                        await PushStateAsync();
                        break;
                    case "snapshot":
                        await CreateSnapshotAsync(request["note"]?.Value<string>());
                        break;
                    case "restore":
                        await RestoreSelectedAsync();
                        break;
                    case "deleteSnapshot":
                        await DeleteSelectedSnapshotAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                PushToast("操作失败：" + ex.Message, true);
            }
        }

        private async Task CreateSnapshotAsync(string note)
        {
            if (!TryBeginOperation())
            {
                return;
            }
            try
            {
                ConfigurationVersionLayer layer = currentLayer;
                string error = null;
                bool success = await Task.Run(() =>
                    Workspace.Runtime.VersionService.CreateManualSnapshot(layer, note, null, out error));
                if (!success)
                {
                    PushToast(error, true);
                    return;
                }
                selectedCommitId = null;
                PushToast("快照已创建。", false);
                await PushStateAsync();
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task DeleteSelectedSnapshotAsync()
        {
            if (string.IsNullOrWhiteSpace(selectedCommitId))
            {
                PushToast("请先选择一个快照。", true);
                return;
            }
            string shortId = selectedCommitId.Substring(0, Math.Min(8, selectedCommitId.Length));
            if (MessageBox.Show("确认删除快照 " + shortId + "？\r\n删除后，该快照将不再显示，也不能用于对比或还原。",
                "删除快照", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            if (!TryBeginOperation())
            {
                return;
            }
            try
            {
                ConfigurationVersionLayer layer = currentLayer;
                string commitId = selectedCommitId;
                string error = null;
                bool success = await Task.Run(() =>
                    Workspace.Runtime.VersionService.DeleteSnapshot(layer, commitId, out error));
                if (!success)
                {
                    PushToast(error, true);
                    return;
                }
                selectedCommitId = null;
                PushToast("快照已删除。", false);
                await PushStateAsync();
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task RestoreSelectedAsync()
        {
            if (string.IsNullOrWhiteSpace(selectedCommitId))
            {
                PushToast("请先选择一个快照。", true);
                return;
            }
            string layerName = currentLayer == ConfigurationVersionLayer.Process ? "工艺层" : "设备层";
            string extra = currentLayer == ConfigurationVersionLayer.Equipment ? "\r\n设备层还原后必须重启程序，重启前禁止操作设备。" : string.Empty;
            if (MessageBox.Show("确认将" + layerName + "还原到所选快照？\r\n此操作不会自动创建任何保护点，请确认已手动保存当前版本。" + extra,
                "确认还原", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            if (!TryBeginOperation())
            {
                return;
            }
            try
            {
                ConfigurationVersionLayer layer = currentLayer;
                string commitId = selectedCommitId;
                string error = null;
                bool success = await Task.Run(() => Workspace.Runtime.VersionService.Restore(layer, commitId,
                    () => (bool)Workspace.Main.Invoke(new Func<bool>(Workspace.Main.AreAllProcessesStopped)),
                    () => Workspace.Main.Invoke(new Action(Workspace.Main.ReloadProcessVersionedConfiguration)),
                    () => Workspace.Main.Invoke(new Action(Workspace.Main.RequireRestartAfterEquipmentRestore)),
                    out error));
                if (!success)
                {
                    PushToast(error, true);
                    return;
                }
                PushToast(layer == ConfigurationVersionLayer.Process
                    ? "工艺层已还原并重新加载。"
                    : "设备层已还原，必须重启程序后才能继续操作。", false);
                await PushStateAsync();
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task PushStateAsync()
        {
            if (webView.CoreWebView2 == null || Workspace.Runtime.VersionService == null)
            {
                return;
            }
            ConfigurationVersionLayer layer = currentLayer;
            string commitId = selectedCommitId;
            bool comparePrevious = compareWithPrevious;
            int requestVersion = Interlocked.Increment(ref stateRequestVersion);
            VersionPageState result = await Task.Run(() => BuildPageState(layer, commitId, comparePrevious));
            if (IsDisposed || Disposing || requestVersion != Volatile.Read(ref stateRequestVersion)
                || layer != currentLayer || comparePrevious != compareWithPrevious)
            {
                return;
            }
            selectedCommitId = result.SelectedCommitId;
            ExecuteScript("setState(" + result.Payload.ToString(Formatting.None) + ");");
        }

        private VersionPageState BuildPageState(
            ConfigurationVersionLayer layer,
            string selectedCommit,
            bool comparePrevious)
        {
            IReadOnlyList<ConfigurationVersionRecord> history = Workspace.Runtime.VersionService.GetHistory(
                layer, out bool dirty, out string historyError);
            if (!history.Any(item => item.CommitId == selectedCommit))
            {
                selectedCommit = history.FirstOrDefault()?.CommitId;
            }
            List<ConfigurationVersionDiffEntry> diff = new List<ConfigurationVersionDiffEntry>();
            string diffError = null;
            if (!string.IsNullOrWhiteSpace(selectedCommit))
            {
                diff = Workspace.Runtime.VersionService.GetStructuredDiff(
                    layer, selectedCommit, comparePrevious, out diffError).ToList();
            }
            List<ConfigurationVersionDiffEntry> variableDiff = diff.Where(item => item.Category == "变量").ToList();
            List<ConfigurationVersionDiffEntry> dataStructDiff = diff.Where(item => item.Category == "数据结构").ToList();
            List<ConfigurationVersionDiffEntry> mainDiff = diff.Where(item => item.Category != "变量" && item.Category != "数据结构").ToList();
            JObject state = new JObject
            {
                ["layer"] = layer == ConfigurationVersionLayer.Process ? "process" : "equipment",
                ["dirty"] = dirty,
                ["mustRestart"] = Workspace.Runtime.Readiness.VersionRestartRequired,
                ["selectedCommitId"] = selectedCommit,
                ["compareMode"] = comparePrevious ? "previous" : "current",
                ["historyError"] = historyError,
                ["diffError"] = diffError,
                ["history"] = JArray.FromObject(history.Select(item => new
                {
                    commitId = item.CommitId,
                    message = item.Message,
                    author = item.Author,
                    time = item.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                })),
                ["diff"] = JArray.FromObject(mainDiff),
                ["variableDiff"] = JArray.FromObject(variableDiff),
                ["dataStructDiff"] = JArray.FromObject(dataStructDiff),
                ["summary"] = new JObject
                {
                    ["total"] = diff.Count,
                    ["operations"] = diff.Count(item => item.Category == "指令"),
                    ["steps"] = diff.Count(item => item.Category == "步骤"),
                    ["variables"] = variableDiff.Count,
                    ["dataStructs"] = dataStructDiff.Count,
                    ["stations"] = diff.Count(item => item.Category == "工站点位"),
                    ["ios"] = diff.Count(item => item.Category == "IO"),
                    ["cards"] = diff.Count(item => item.Category == "控制卡"),
                    ["alarms"] = diff.Count(item => item.Category == "报警")
                }
            };
            return new VersionPageState
            {
                SelectedCommitId = selectedCommit,
                Payload = state
            };
        }

        private bool TryBeginOperation()
        {
            if (operationInProgress)
            {
                PushToast("版本操作正在执行，请稍候。", true);
                return false;
            }
            operationInProgress = true;
            UseWaitCursor = true;
            webView.Enabled = false;
            return true;
        }

        private void EndOperation()
        {
            operationInProgress = false;
            if (IsDisposed || Disposing)
            {
                return;
            }
            UseWaitCursor = false;
            if (!webView.IsDisposed)
            {
                webView.Enabled = true;
            }
        }

        private void PushToast(string message, bool isError)
        {
            if (IsDisposed || Disposing || webView.IsDisposed)
            {
                return;
            }
            ExecuteScript("showToast(" + JsonConvert.SerializeObject(message ?? string.Empty) + "," + (isError ? "true" : "false") + ");");
        }

        private void ExecuteScript(string script)
        {
            webView.CoreWebView2?.ExecuteScriptAsync(script);
        }

        private static string BuildHtml()
        {
            return """
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<style>
*{box-sizing:border-box}html,body{height:100%}body{margin:0;background:#f4f6f8;color:#243746;font:14px/1.5 "Microsoft YaHei UI","Microsoft YaHei",sans-serif;overflow:hidden}button,input,select{font:inherit}button{cursor:pointer}.shell{height:100%;display:flex;flex-direction:column}.topbar{height:64px;flex:none;display:flex;align-items:center;gap:18px;padding:0 22px;background:#fff;border-bottom:1px solid #dce4e9}.brand{font-size:18px;font-weight:700;color:#172f40;white-space:nowrap}.layer-tabs{display:flex;height:100%}.layer-tab{min-width:104px;border:0;border-bottom:3px solid transparent;background:transparent;color:#697d8a}.layer-tab.active{border-color:#16877c;color:#0c6b63;font-weight:700}.branch-area{margin-left:auto;display:flex;align-items:center;gap:8px}.branch-label{font-size:12px;color:#7a8e9a}.branch-select{height:34px;min-width:168px;border:1px solid #cbd7de;border-radius:5px;background:#fff;padding:0 30px 0 9px;color:#274051}.icon-button{width:34px;height:34px;border:1px solid #cbd7de;border-radius:5px;background:#fff;color:#47606f;font-size:20px;line-height:1}.icon-button:hover{border-color:#16877c;color:#0d756b}.restart{color:#a74b24;background:#fff1e8;border:1px solid #edc5af;border-radius:4px;padding:6px 9px;font-size:12px}.workspace{flex:1;min-height:0;display:grid;grid-template-columns:332px minmax(0,1fr)}.sidebar{min-height:0;background:#fff;border-right:1px solid #dce4e9;display:flex;flex-direction:column}.snapshot-create{padding:16px;border-bottom:1px solid #e5ebef}.section-title{font-weight:700;color:#223a4b}.section-hint{font-size:12px;color:#7b8f9b;margin-top:3px}.create-row{display:flex;gap:7px;margin-top:12px}.note-input{height:34px;min-width:0;flex:1;border:1px solid #cbd7de;border-radius:5px;padding:0 9px}.primary{height:34px;border:1px solid #147b72;border-radius:5px;background:#147b72;color:#fff;padding:0 12px}.primary:hover{background:#0f6962}.status-line{margin-top:10px;font-size:12px}.status-line.dirty{color:#b04d28}.status-line.clean{color:#30745a}.history-head{height:45px;flex:none;display:flex;align-items:center;justify-content:space-between;padding:0 16px;border-bottom:1px solid #e8edf0}.history-count{font-size:12px;color:#7a8d99}.history{min-height:0;overflow:auto}.version{padding:12px 16px;border-bottom:1px solid #edf1f3;cursor:pointer}.version:hover{background:#f7fafb}.version.selected{background:#e8f5f3;border-left:3px solid #16877c;padding-left:13px}.version-note{font-weight:600;color:#243b4b;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.version-meta{display:flex;justify-content:space-between;gap:8px;color:#7b8d98;font-size:12px;margin-top:6px}.empty{padding:48px 20px;text-align:center;color:#80929d}.content{min-width:0;min-height:0;display:flex;flex-direction:column}.content-toolbar{flex:none;padding:14px 18px;background:#fff;border-bottom:1px solid #dce4e9}.toolbar-row{display:flex;align-items:center;gap:10px}.compare-title{font-weight:700}.compare-sub{font-size:12px;color:#7d909c}.segmented{display:flex;margin-left:auto;border:1px solid #cbd7de;border-radius:5px;overflow:hidden}.segmented button{height:32px;border:0;border-right:1px solid #cbd7de;background:#fff;color:#627783;padding:0 10px}.segmented button:last-child{border-right:0}.segmented button.active{background:#e8f5f3;color:#0c6b63;font-weight:700}.secondary,.danger{height:33px;border-radius:5px;background:#fff;padding:0 11px}.secondary{border:1px solid #bfcdd5;color:#385465}.secondary:hover{border-color:#16877c;color:#0d756b}.danger{border:1px solid #e0b7ab;color:#a6402a}.danger:hover{background:#fff4f0}.danger:disabled,.secondary:disabled{opacity:.45;cursor:default}.summary{display:grid;grid-template-columns:repeat(5,minmax(92px,1fr));gap:10px;margin-top:13px}.metric{border-left:3px solid #9eafb9;padding:3px 10px}.metric.operations{border-color:#2c8b80}.metric.steps{border-color:#477fae}.metric.variables{border-color:#b07834}.metric.structures{border-color:#7667a5}.metric-value{font-size:20px;font-weight:700;color:#1d3646}.metric-label{font-size:12px;color:#768994}.changes{min-height:0;overflow:auto;padding:16px 18px 26px}.group{background:#fff;border:1px solid #dce5ea;border-radius:6px;margin-bottom:12px;overflow:hidden}.group-head{height:42px;display:flex;align-items:center;gap:8px;padding:0 13px;background:#f7f9fa;border-bottom:1px solid #e4eaee;font-weight:700}.group-count{font-size:12px;color:#758894;font-weight:400}.change-row{display:grid;grid-template-columns:76px minmax(170px,1.1fr) minmax(140px,.8fr) minmax(190px,1fr);gap:12px;padding:12px 13px;border-bottom:1px solid #edf1f3;align-items:start}.change-row:last-child{border-bottom:0}.badge{display:inline-flex;justify-content:center;min-width:50px;border-radius:3px;padding:2px 6px;font-size:12px;font-weight:700}.badge.新增{background:#e4f4ea;color:#28744d}.badge.删除{background:#fbe9e6;color:#a34436}.badge.修改{background:#fff2dc;color:#93611d}.badge.移动{background:#e7eef8;color:#426d9a}.change-title{font-weight:600;color:#263e4e}.change-location,.field-name{font-size:12px;color:#778b97;margin-top:3px}.value-pair{display:grid;grid-template-columns:minmax(0,1fr) 16px minmax(0,1fr);gap:5px;align-items:start}.value{word-break:break-word;color:#425b6a}.arrow{color:#9aabb4;text-align:center}.variables-button{position:relative}.count-dot{display:inline-flex;min-width:19px;height:19px;align-items:center;justify-content:center;margin-left:5px;border-radius:10px;background:#b07834;color:#fff;font-size:11px}.modal-layer{position:fixed;inset:0;display:none;align-items:center;justify-content:center;background:rgba(26,43,55,.42);z-index:20}.modal-layer.open{display:flex}.dialog{width:min(940px,calc(100vw - 48px));max-height:calc(100vh - 64px);background:#fff;border-radius:7px;box-shadow:0 14px 42px rgba(20,38,49,.24);display:flex;flex-direction:column;overflow:hidden}.dialog.small{width:430px}.dialog-head{height:54px;flex:none;display:flex;align-items:center;padding:0 18px;border-bottom:1px solid #e0e7eb}.dialog-title{font-size:16px;font-weight:700}.close{margin-left:auto;width:32px;height:32px;border:0;background:transparent;color:#6f828e;font-size:22px}.dialog-tools{padding:12px 18px;border-bottom:1px solid #e5ebee}.search{width:100%;height:34px;border:1px solid #cbd7de;border-radius:5px;padding:0 10px}.variable-list{min-height:0;overflow:auto}.variable-row{display:grid;grid-template-columns:66px 150px 90px 110px minmax(130px,1fr) 24px minmax(130px,1fr);gap:10px;padding:11px 18px;border-bottom:1px solid #edf1f3;align-items:start}.variable-row.head{position:sticky;top:0;background:#f7f9fa;font-size:12px;color:#627783;font-weight:700;z-index:1}.branch-form{padding:18px}.branch-input{width:100%;height:36px;border:1px solid #cbd7de;border-radius:5px;padding:0 10px}.dialog-actions{display:flex;justify-content:flex-end;gap:8px;padding:13px 18px;border-top:1px solid #e2e8ec}.toast{position:fixed;right:22px;bottom:20px;display:none;z-index:40;background:#24485e;color:#fff;border-radius:5px;padding:10px 14px;box-shadow:0 5px 18px rgba(0,0,0,.2)}.toast.error{background:#a6433c}@media(max-width:1050px){.workspace{grid-template-columns:290px minmax(0,1fr)}.summary{grid-template-columns:repeat(2,1fr)}.change-row{grid-template-columns:70px minmax(150px,1fr) minmax(180px,1fr)}.change-row>div:nth-child(3){display:none}.branch-label{display:none}}
</style>
<style>
.secondary,.danger,.segmented button{white-space:nowrap}
@media(max-width:1200px){
  .toolbar-row{flex-wrap:wrap}
  .toolbar-row>div:first-child{flex:1 0 100%;min-width:0}
  .segmented{margin-left:0}
  .summary{grid-template-columns:repeat(3,minmax(92px,1fr))}
}
@media(max-width:900px){
  .topbar{height:56px;padding:0 14px;gap:8px}
  .layer-tab{min-width:82px}
  .workspace{grid-template-columns:250px minmax(0,1fr)}
  .snapshot-create{padding:12px}
  .content-toolbar{padding:10px 12px}
  .summary{grid-template-columns:repeat(2,minmax(92px,1fr))}
}
</style>
</head>
<body>
<div class="shell">
  <header class="topbar">
    <div class="brand">版本管理</div>
    <div class="layer-tabs">
      <button class="layer-tab" id="processTab" onclick="post('layer',{layer:'process'})">工艺层</button>
      <button class="layer-tab" id="equipmentTab" onclick="post('layer',{layer:'equipment'})">设备层</button>
    </div>
    <span class="restart" id="restart" hidden>设备配置已还原，请重启程序</span>
  </header>
  <div class="workspace">
    <aside class="sidebar">
      <div class="snapshot-create">
        <div class="section-title">保存当前版本</div>
        <div class="section-hint">快照保存配置与纳入版本的源码，不保存流程运行位置、后台任务或设备实时状态</div>
        <div class="create-row"><input class="note-input" id="note" maxlength="80" placeholder="填写本次修改说明"><button class="primary" onclick="post('snapshot',{note:byId('note').value})">创建快照</button></div>
        <div class="status-line" id="dirtyState"></div>
      </div>
      <div class="history-head"><span class="section-title">快照历史</span><span class="history-count" id="historyCount"></span></div>
      <div class="history" id="history"></div>
    </aside>
    <main class="content">
      <div class="content-toolbar">
        <div class="toolbar-row">
          <div><div class="compare-title" id="compareTitle">改动项对比</div><div class="compare-sub" id="compareHint"></div></div>
          <div class="segmented"><button id="currentMode" onclick="post('compare',{mode:'current'})">与当前配置</button><button id="previousMode" onclick="post('compare',{mode:'previous'})">与上一快照</button></div>
          <button class="secondary variables-button process-only" id="variableButton" onclick="openVariables()">变量变更<span class="count-dot" id="variableCount">0</span></button>
          <button class="secondary variables-button process-only" id="dataStructButton" onclick="openDataStructs()">数据结构变更<span class="count-dot" id="dataStructCount">0</span></button>
          <button class="danger" id="deleteButton" onclick="post('deleteSnapshot')">删除快照</button>
          <button class="primary" id="restoreButton" onclick="post('restore')">还原此快照</button>
        </div>
        <div class="summary">
          <div class="metric"><div class="metric-value" id="totalMetric">0</div><div class="metric-label">全部改动</div></div>
          <div class="metric operations"><div class="metric-value" id="metricOne">0</div><div class="metric-label" id="metricOneLabel">指令改动</div></div>
          <div class="metric steps"><div class="metric-value" id="metricTwo">0</div><div class="metric-label" id="metricTwoLabel">步骤改动</div></div>
          <div class="metric variables"><div class="metric-value" id="metricThree">0</div><div class="metric-label" id="metricThreeLabel">变量改动</div></div>
          <div class="metric structures"><div class="metric-value" id="metricFour">0</div><div class="metric-label" id="metricFourLabel">数据结构改动</div></div>
        </div>
      </div>
      <div class="changes" id="changes"></div>
    </main>
  </div>
</div>
<div class="modal-layer" id="variableModal"><div class="dialog"><div class="dialog-head"><div class="dialog-title">变量变更明细</div><button class="close" onclick="closeModal('variableModal')">×</button></div><div class="dialog-tools"><input class="search" id="variableSearch" placeholder="按变量名称、索引或字段筛选" oninput="renderVariables()"></div><div class="variable-list" id="variableList"></div></div></div>
<div class="modal-layer" id="dataStructModal"><div class="dialog"><div class="dialog-head"><div class="dialog-title">数据结构变更明细</div><button class="close" onclick="closeModal('dataStructModal')">×</button></div><div class="dialog-tools"><input class="search" id="dataStructSearch" placeholder="按配置路径、字段或内容筛选" oninput="renderDataStructs()"></div><div class="variable-list" id="dataStructList"></div></div></div>
<div class="modal-layer" id="detailModal"><div class="dialog"><div class="dialog-head"><div class="dialog-title">改动详情</div><button class="close" onclick="closeModal('detailModal')">×</button></div><div class="variable-list" id="detailContent"></div></div></div>
<div class="toast" id="toast"></div>
<script>
var state={history:[],diff:[],variableDiff:[],dataStructDiff:[],summary:{}};
function byId(id){return document.getElementById(id)}
function post(action,data){window.chrome.webview.postMessage(JSON.stringify(Object.assign({action:action},data||{})))}
function esc(value){return String(value==null?'':value).replace(/[&<>"']/g,function(c){return({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[c]})}
function setState(next){state=next||{};var process=state.layer==='process';var layerName=process?'工艺层':'设备层';byId('processTab').classList.toggle('active',process);byId('equipmentTab').classList.toggle('active',!process);document.querySelectorAll('.process-only').forEach(function(element){element.hidden=!process});byId('compareTitle').textContent=layerName+' · 改动项对比';byId('currentMode').textContent='与当前'+layerName+'配置';byId('restart').hidden=!state.mustRestart;renderHistory();renderSummary();renderChanges();renderVariables();renderDataStructs();byId('dirtyState').className='status-line '+(state.dirty?'dirty':'clean');byId('dirtyState').textContent=state.dirty?layerName+'当前配置有尚未纳入快照的改动':layerName+'当前配置与最新快照一致';byId('currentMode').classList.toggle('active',state.compareMode==='current');byId('previousMode').classList.toggle('active',state.compareMode==='previous');byId('compareHint').textContent=state.selectedCommitId?(state.compareMode==='previous'?'所选快照相对上一手动快照的变化':'所选快照与当前'+layerName+'配置之间的变化'):'请先创建或选择'+layerName+'快照';var disabled=!state.selectedCommitId;byId('restoreButton').disabled=disabled;byId('deleteButton').disabled=disabled;if(state.historyError)showToast(state.historyError,true);if(state.diffError)showToast(state.diffError,true)}
function closeModal(id){byId(id).classList.remove('open')}
function renderHistory(){var items=state.history||[];byId('historyCount').textContent=items.length+' 个';byId('history').innerHTML=items.length?items.map(function(v){return '<div class="version '+(v.commitId===state.selectedCommitId?'selected':'')+'" onclick="post(\'select\',{commitId:\''+esc(v.commitId)+'\'})"><div class="version-note">'+esc(v.message.replace(/^手动快照[：:]?\s*/,'' )||'未填写说明')+'</div><div class="version-meta"><span>'+esc(v.time)+'</span><span>'+esc(v.commitId.slice(0,8))+'</span></div></div>'}).join(''):'<div class="empty">当前还没有手动快照<br>填写说明后创建第一个快照</div>'}
function renderSummary(){var s=state.summary||{},process=state.layer==='process';byId('totalMetric').textContent=s.total||0;var metrics=process?[[s.operations,'指令改动'],[s.steps,'步骤改动'],[s.variables,'变量改动'],[s.dataStructs,'数据结构改动']]:[[s.stations,'工站点位改动'],[s.ios,'IO 改动'],[s.cards,'控制卡改动'],[s.alarms,'报警改动']];['One','Two','Three','Four'].forEach(function(name,index){byId('metric'+name).textContent=metrics[index][0]||0;byId('metric'+name+'Label').textContent=metrics[index][1]});byId('variableCount').textContent=s.variables||0;byId('variableButton').disabled=!(s.variables>0);byId('dataStructCount').textContent=s.dataStructs||0;byId('dataStructButton').disabled=!(s.dataStructs>0)}
function renderChanges(){var items=state.diff||[];if(!state.selectedCommitId){byId('changes').innerHTML='<div class="empty">创建快照后即可查看改动项对比</div>';return}if(!items.length){byId('changes').innerHTML='<div class="empty">除变量外没有其他改动</div>';return}var groups={};items.forEach(function(item){(groups[item.Category]||(groups[item.Category]=[])).push(item)});byId('changes').innerHTML=Object.keys(groups).map(function(category){var rows=groups[category];return '<section class="group"><div class="group-head">'+esc(category)+'<span class="group-count">'+rows.length+' 项改动</span></div>'+rows.map(function(item){return renderChangeRow(item,items.indexOf(item))}).join('')+'</section>'}).join('')}
function renderChangeRow(item,index){var details=(item.Details||[]).length;var summary=details?'<div class="value" style="color:#0c6b63">双击查看 '+details+' 项字段详情</div>':'<div class="value-pair"><div class="value">'+esc(item.Before)+'</div><div class="arrow">→</div><div class="value">'+esc(item.After)+'</div></div>';return '<div class="change-row" ondblclick="openChangeDetail('+index+')"><div><span class="badge '+esc(item.ChangeType)+'">'+esc(item.ChangeType)+'</span></div><div><div class="change-title">'+esc(item.Title)+'</div><div class="change-location">'+esc(item.Location)+'</div></div><div><div class="field-name">'+(details?'变更摘要':'变更字段')+'</div><div>'+esc(item.FieldName)+'</div></div><div>'+summary+'</div></div>'}
function renderSourceDiff(before,after){var oldLines=String(before||'').split(/\r?\n/),newLines=String(after||'').split(/\r?\n/),n=oldLines.length,m=newLines.length;if(n*m>160000)return '<pre style="margin:0;padding:16px;white-space:pre-wrap;font:13px/1.55 Consolas,monospace;background:#fff8f7;color:#9f3328">- '+esc(before)+'</pre><pre style="margin:0;padding:16px;white-space:pre-wrap;font:13px/1.55 Consolas,monospace;background:#f3fff4;color:#23723a">+ '+esc(after)+'</pre>';var table=Array.from({length:n+1},function(){return new Array(m+1).fill(0)}),i,j;for(i=n-1;i>=0;i--)for(j=m-1;j>=0;j--)table[i][j]=oldLines[i]===newLines[j]?table[i+1][j+1]+1:Math.max(table[i+1][j],table[i][j+1]);var rows=[],oldNo=1,newNo=1;i=0;j=0;while(i<n||j<m){if(i<n&&j<m&&oldLines[i]===newLines[j]){rows.push(sourceDiffRow(' ',oldNo++,newNo++,oldLines[i++],'#fff','#526976'));continue}if(j<m&&(i===n||table[i][j+1]>=table[i+1][j])){rows.push(sourceDiffRow('+','',newNo++,newLines[j++],'#e8f5df','#22683b'));continue}rows.push(sourceDiffRow('-',oldNo++,'',oldLines[i++],'#fde8e6','#aa3630'))}return '<div style="padding:14px 18px;background:#f7f9fa">'+rows.join('')+'</div>'}
function sourceDiffRow(mark,oldNo,newNo,text,bg,color){return '<div style="display:grid;grid-template-columns:18px 44px 44px minmax(0,1fr);min-height:21px;padding:1px 8px;background:'+bg+';font:13px/1.5 Consolas,\'Microsoft YaHei UI\',monospace;color:'+color+'"><span>'+mark+'</span><span style="color:#8a9aa4;text-align:right;padding-right:8px">'+oldNo+'</span><span style="color:#8a9aa4;text-align:right;padding-right:8px">'+newNo+'</span><span style="white-space:pre-wrap;word-break:break-all">'+esc(text)+'</span></div>'}
function renderBusinessDetail(item,before,after){var details=item.Details||[];var header='<div style="padding:22px 24px 16px"><div style="font-size:17px;font-weight:700;color:#263e4e">'+esc(item.Title)+'</div><div style="margin-top:5px;color:#718692">'+esc(item.Location)+'</div></div>';if(!details.length)return header+'<div style="padding:0 24px 26px"><div style="padding:12px 14px;border:1px solid #dce5ea;border-radius:6px;background:#f7f9fa"><div style="font-size:12px;color:#738793">变更字段</div><div style="margin-top:3px;font-weight:600;color:#284759">'+esc(item.FieldName)+'</div></div><div style="display:grid;grid-template-columns:minmax(0,1fr) 36px minmax(0,1fr);gap:10px;align-items:stretch;margin-top:16px"><div><div style="margin:0 0 7px 2px;font-size:12px;color:#a04639;font-weight:700">修改前</div><div style="min-height:72px;padding:13px 14px;border:1px solid #f0c9c3;border-radius:6px;background:#fff5f3;color:#873d34;white-space:pre-wrap;word-break:break-word">'+esc(before)+'</div></div><div style="display:flex;align-items:center;justify-content:center;color:#8ca0aa;font-size:22px">→</div><div><div style="margin:0 0 7px 2px;font-size:12px;color:#28744d;font-weight:700">修改后</div><div style="min-height:72px;padding:13px 14px;border:1px solid #c7e5d1;border-radius:6px;background:#f3fbf5;color:#24633e;white-space:pre-wrap;word-break:break-word">'+esc(after)+'</div></div></div></div>';var rows=details.map(function(detail){return '<div style="display:grid;grid-template-columns:64px minmax(220px,1.2fr) minmax(150px,1fr) 22px minmax(150px,1fr);gap:10px;align-items:start;padding:12px 14px;border-bottom:1px solid #e5ebee"><div><span class="badge '+esc(detail.ChangeType)+'">'+esc(detail.ChangeType)+'</span></div><div style="font-weight:600;color:#284759;word-break:break-word">'+esc(detail.FieldName)+'</div><div style="padding:7px 9px;border-radius:4px;background:#fff5f3;color:#873d34;white-space:pre-wrap;word-break:break-word">'+esc(detail.Before)+'</div><div style="padding-top:5px;text-align:center;color:#8ca0aa">→</div><div style="padding:7px 9px;border-radius:4px;background:#f3fbf5;color:#24633e;white-space:pre-wrap;word-break:break-word">'+esc(detail.After)+'</div></div>'}).join('');return header+'<div style="margin:0 24px 24px;border:1px solid #dce5ea;border-radius:6px;overflow:hidden"><div style="display:grid;grid-template-columns:64px minmax(220px,1.2fr) minmax(150px,1fr) 22px minmax(150px,1fr);gap:10px;padding:10px 14px;background:#f7f9fa;color:#718692;font-size:12px;font-weight:700"><div>变更</div><div>字段</div><div>修改前</div><div></div><div>修改后</div></div>'+rows+'</div>'}
function openChangeDetail(index){var item=(state.diff||[])[index];if(!item)return;var before=item.DetailBefore||item.Before,after=item.DetailAfter||item.After;var source=item.DetailBefore!=null||item.DetailAfter!=null;byId('detailContent').innerHTML=source?renderSourceDiff(before,after):renderBusinessDetail(item,before,after);byId('detailModal').classList.add('open')}
function openVariables(){if(!(state.variableDiff||[]).length)return;byId('variableSearch').value='';renderVariables();byId('variableModal').classList.add('open')}
function renderVariables(){var target=byId('variableList');if(!target)return;var query=(byId('variableSearch')?byId('variableSearch').value:'').trim().toLowerCase();var items=(state.variableDiff||[]).filter(function(item){return !query||(item.Title+' '+item.Location+' '+item.FieldName).toLowerCase().indexOf(query)>=0});var head='<div class="variable-row head"><div>变更</div><div>变量</div><div>位置</div><div>字段</div><div>原值</div><div></div><div>新值</div></div>';target.innerHTML=head+(items.length?items.map(function(item){return '<div class="variable-row"><div><span class="badge '+esc(item.ChangeType)+'">'+esc(item.ChangeType)+'</span></div><div>'+esc(item.Title)+'</div><div>'+esc(item.Location.replace('变量索引 ', '#'))+'</div><div>'+esc(item.FieldName)+'</div><div class="value">'+esc(item.Before)+'</div><div class="arrow">→</div><div class="value">'+esc(item.After)+'</div></div>'}).join(''):'<div class="empty">没有匹配的变量改动</div>')}
function openDataStructs(){if(!(state.dataStructDiff||[]).length)return;byId('dataStructSearch').value='';renderDataStructs();byId('dataStructModal').classList.add('open')}
function renderDataStructs(){var target=byId('dataStructList');if(!target)return;var query=(byId('dataStructSearch')?byId('dataStructSearch').value:'').trim().toLowerCase();var items=(state.dataStructDiff||[]).filter(function(item){return !query||(item.Title+' '+item.Location+' '+item.FieldName+' '+item.Before+' '+item.After).toLowerCase().indexOf(query)>=0});var head='<div class="variable-row head"><div>变更</div><div>数据结构</div><div>配置位置</div><div>字段</div><div>原值</div><div></div><div>新值</div></div>';target.innerHTML=head+(items.length?items.map(function(item){return '<div class="variable-row"><div><span class="badge '+esc(item.ChangeType)+'">'+esc(item.ChangeType)+'</span></div><div>'+esc(item.Title)+'</div><div>'+esc(item.Location)+'</div><div>'+esc(item.FieldName)+'</div><div class="value">'+esc(item.Before)+'</div><div class="arrow">→</div><div class="value">'+esc(item.After)+'</div></div>'}).join(''):'<div class="empty">没有匹配的数据结构改动</div>')}
function showToast(message,error){var toast=byId('toast');toast.textContent=message;toast.className='toast '+(error?'error':'');toast.style.display='block';clearTimeout(window.toastTimer);window.toastTimer=setTimeout(function(){toast.style.display='none'},3600)}
document.addEventListener('keydown',function(event){if(event.key==='Escape'){closeModal('variableModal');closeModal('dataStructModal');closeModal('detailModal')}});post('ready');
</script>
</body>
</html>
""";
        }
    }
}
