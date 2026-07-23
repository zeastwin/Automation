// 模块：编辑器 / 诊断。
// 职责范围：运行日志、状态、流程图、断点、性能和事故诊断页面。

using Automation.Protocol;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    /// <summary>
    /// WebView2 只呈现统一图快照，流程拓扑仍由 ProcessFlowGraphService 确定性生成。
    /// </summary>
    public sealed class FrmProcessFlow : Form
    {
        private const string PageResourceName = "Automation.Assets.ProcessFlow.index.html";
        private readonly FrmMain owner;
        private readonly WebView2 webView;
        private readonly Panel fallbackPanel;
        private readonly Label fallbackMessage;
        private CoreWebView2 coreWebView;
        private ProcessFlowGraphSnapshot snapshot;
        private int? currentProcIndex;
        private string selectedNodeId;
        private int buildGeneration;
        private int runtimePushScheduled;
        private CancellationTokenSource buildCancellation;
        private readonly Dictionary<string, CachedGraphSnapshot> graphCache =
            new Dictionary<string, CachedGraphSnapshot>(StringComparer.Ordinal);
        private readonly SemaphoreSlim graphPrewarmGate =
            new SemaphoreSlim(1, 1);
        private Task webInitializationTask;
        private bool pageReady;
        private bool webAvailable = true;

        private sealed class CachedGraphSnapshot
        {
            public long Revision { get; set; }
            public ProcessFlowGraphSnapshot Snapshot { get; set; }
        }

        public FrmProcessFlow(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            BackColor = UiPalette.Surface;
            MinimumSize = new Size(760, 520);

            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Automation",
                        "WebView2")
                }
            };

            fallbackMessage = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(48),
                Font = new Font("Microsoft YaHei UI", 11F),
                ForeColor = UiPalette.TextSecondary,
                TextAlign = ContentAlignment.MiddleCenter
            };
            var fallbackBack = new Button
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                Text = "返回指令列表",
                FlatStyle = FlatStyle.Flat,
                BackColor = UiPalette.Brand,
                ForeColor = UiPalette.TextInverse,
                Font = new Font("Microsoft YaHei UI", 10F),
                Cursor = Cursors.Hand
            };
            fallbackBack.FlatAppearance.BorderSize = 0;
            fallbackBack.Click += (sender, args) => owner.ShowEditorWorkspace();
            fallbackPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(80),
                BackColor = UiPalette.Surface,
                Visible = false
            };
            fallbackPanel.Controls.Add(fallbackMessage);
            fallbackPanel.Controls.Add(fallbackBack);
            Controls.Add(fallbackPanel);
            Controls.Add(webView);

            Load += FrmProcessFlow_Load;
            owner.dataRun.SnapshotChanged += RuntimeSnapshotChanged;
            FormClosed += (sender, args) =>
            {
                owner.dataRun.SnapshotChanged -= RuntimeSnapshotChanged;
                buildCancellation?.Cancel();
                buildCancellation?.Dispose();
                if (coreWebView != null)
                {
                    try
                    {
                        coreWebView.WebMessageReceived -= WebView_WebMessageReceived;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.Runtime.InteropServices.InvalidComObjectException)
                    {
                    }
                    coreWebView = null;
                }
            };
        }

        public void ShowProjectGraph()
        {
            currentProcIndex = null;
            selectedNodeId = null;
            RefreshCurrentGraph();
        }

        public void ShowProcessGraph(int procIndex)
        {
            currentProcIndex = procIndex;
            selectedNodeId = null;
            RefreshCurrentGraph();
        }

        public async void RefreshCurrentGraph(bool force = false)
        {
            if (IsDisposed)
            {
                return;
            }
            int? procIndex = currentProcIndex;
            string cacheKey = BuildGraphCacheKey(procIndex);
            long revision = GetGraphRevision(procIndex);
            if (!force
                && graphCache.TryGetValue(cacheKey, out CachedGraphSnapshot cached)
                && cached.Revision == revision)
            {
                snapshot = cached.Snapshot;
                PushGraphState();
                return;
            }
            int generation = Interlocked.Increment(ref buildGeneration);
            buildCancellation?.Cancel();
            buildCancellation?.Dispose();
            buildCancellation = new CancellationTokenSource();
            CancellationToken token = buildCancellation.Token;
            List<Proc> processes = owner.frmProc.procsList.Select(ObjectGraphCloner.Clone).ToList();
            ProcessDefinitionValidationContext validationContext = owner.Runtime.CreateProcessValidationContext();
            var runtimes = owner.dataRun.GetSnapshots().ToDictionary(item => item.ProcIndex);
            if (snapshot == null)
            {
                PostMessage(new JObject
                {
                    ["type"] = "loading",
                    ["scope"] = procIndex.HasValue ? "process" : "project"
                });
            }
            try
            {
                ProcessFlowGraphSnapshot built = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    Func<int, EngineSnapshot> provider = index =>
                        runtimes.TryGetValue(index, out EngineSnapshot runtime) ? runtime : null;
                    return procIndex.HasValue
                        ? ProcessFlowGraphService.BuildProcess(processes, procIndex.Value, provider)
                        : ProcessFlowGraphService.BuildProject(
                            processes, provider, validationContext, owner.Runtime.Stores.Values);
                }, token);
                if (token.IsCancellationRequested || generation != buildGeneration || IsDisposed)
                {
                    return;
                }
                snapshot = built;
                graphCache[cacheKey] = new CachedGraphSnapshot
                {
                    Revision = revision,
                    Snapshot = built
                };
                if (graphCache.Count > 32)
                {
                    string staleKey = graphCache.Keys
                        .FirstOrDefault(key => !string.Equals(
                            key,
                            cacheKey,
                            StringComparison.Ordinal));
                    if (staleKey != null)
                    {
                        graphCache.Remove(staleKey);
                    }
                }
                PushGraphState();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ReportGraphError("流程图生成失败：" + ex.Message, ex);
            }
        }

        private async void FrmProcessFlow_Load(object sender, EventArgs e)
        {
            await EnsureWebViewInitializedAsync();
        }

        internal void Prewarm()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            CreateControl();
            webView.CreateControl();
            _ = EnsureWebViewInitializedAsync();
            RefreshCurrentGraph();
        }

        internal async void PrewarmProcessGraph(int procIndex)
        {
            await graphPrewarmGate.WaitAsync();
            try
            {
                if (IsDisposed || Disposing
                    || procIndex < 0
                    || procIndex >= owner.Runtime.Stores.Processes.Items.Count)
                {
                    return;
                }
                Proc process = ObjectGraphCloner.Clone(
                    owner.Runtime.Stores.Processes.Items[procIndex]);
                Guid id = process?.head?.Id ?? Guid.Empty;
                long revision = owner.Runtime.Stores.Processes.GetRevision(id);
                var processes = new List<Proc>(
                    Enumerable.Repeat<Proc>(null, procIndex + 1));
                processes[procIndex] = process;
                ProcessFlowGraphSnapshot built = await Task.Run(() =>
                    ProcessFlowGraphService.BuildProcess(
                        processes,
                        procIndex,
                        null));
                if (IsDisposed
                    || revision != owner.Runtime.Stores.Processes.GetRevision(id))
                {
                    return;
                }
                string key = id == Guid.Empty
                    ? "process-index:" + procIndex
                    : "process:" + id.ToString("N");
                graphCache[key] = new CachedGraphSnapshot
                {
                    Revision = revision,
                    Snapshot = built
                };
            }
            catch (Exception ex)
            {
                owner.dataRun?.Logger?.Log(
                    $"流程图局部缓存预热失败:{ex}",
                    LogLevel.Error);
            }
            finally
            {
                graphPrewarmGate.Release();
            }
        }

        private Task EnsureWebViewInitializedAsync()
        {
            if (webInitializationTask == null)
            {
                webInitializationTask = InitializeWebViewAsync();
            }
            return webInitializationTask;
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                if (coreWebView != null || IsDisposed)
                {
                    return;
                }
                coreWebView = webView.CoreWebView2;
                coreWebView.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView.Settings.AreDevToolsEnabled = false;
                coreWebView.Settings.AreBrowserAcceleratorKeysEnabled = true;
                coreWebView.WebMessageReceived += WebView_WebMessageReceived;
                coreWebView.NavigateToString(ReadPageHtml());
            }
            catch (Exception ex)
            {
                DisableWebPage("流程图页面初始化失败：" + ex.Message, ex);
            }
        }

        private string BuildGraphCacheKey(int? procIndex)
        {
            if (!procIndex.HasValue)
            {
                return "project";
            }
            Proc process = procIndex.Value >= 0
                && procIndex.Value < owner.Runtime.Stores.Processes.Items.Count
                    ? owner.Runtime.Stores.Processes.Items[procIndex.Value]
                    : null;
            Guid id = process?.head?.Id ?? Guid.Empty;
            return id == Guid.Empty
                ? "process-index:" + procIndex.Value
                : "process:" + id.ToString("N");
        }

        private long GetGraphRevision(int? procIndex)
        {
            if (!procIndex.HasValue)
            {
                unchecked
                {
                    return owner.Runtime.Stores.Processes.Version * 397
                        ^ owner.Runtime.Stores.Values.ConfigurationVersion;
                }
            }
            Proc process = procIndex.Value >= 0
                && procIndex.Value < owner.Runtime.Stores.Processes.Items.Count
                    ? owner.Runtime.Stores.Processes.Items[procIndex.Value]
                    : null;
            return owner.Runtime.Stores.Processes.GetRevision(
                process?.head?.Id ?? Guid.Empty);
        }

        private void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                JObject request = JObject.Parse(e.TryGetWebMessageAsString());
                string action = request["action"]?.Value<string>() ?? string.Empty;
                switch (action)
                {
                    case "ready":
                        pageReady = true;
                        PushGraphState();
                        break;
                    case "project":
                        ShowProjectGraph();
                        break;
                    case "openProcess":
                        int? procIndex = request["procIndex"]?.Value<int?>();
                        if (procIndex.HasValue)
                        {
                            ShowProcessGraph(procIndex.Value);
                        }
                        break;
                    case "openOperation":
                        NavigateToOperation(request);
                        break;
                    case "select":
                        selectedNodeId = request["nodeId"]?.Value<string>();
                        break;
                    case "back":
                        owner.ShowEditorWorkspace();
                        break;
                    case "ai":
                        PrepareAiPrompt();
                        break;
                    case "refresh":
                        RefreshCurrentGraph(true);
                        break;
                }
            }
            catch (Exception ex)
            {
                ReportGraphError("流程图交互失败：" + ex.Message, ex);
            }
        }

        private void NavigateToOperation(JObject request)
        {
            if (!Guid.TryParse(request["procId"]?.Value<string>(), out Guid procId)
                || !Guid.TryParse(request["stepId"]?.Value<string>(), out Guid stepId)
                || !Guid.TryParse(request["opId"]?.Value<string>(), out Guid opId))
            {
                PostToast("节点身份不完整，无法定位指令。", true);
                return;
            }
            if (!owner.NavigateToFlowOperation(procId, stepId, opId))
            {
                PostToast("当前配置中已无法解析该指令，流程图已刷新。", true);
                RefreshCurrentGraph();
            }
        }

        private void RuntimeSnapshotChanged(EngineSnapshot runtime)
        {
            if (IsDisposed || !IsHandleCreated || Interlocked.Exchange(ref runtimePushScheduled, 1) != 0)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    Interlocked.Exchange(ref runtimePushScheduled, 0);
                    PushRuntimeState();
                }));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref runtimePushScheduled, 0);
            }
        }

        private void PushGraphState()
        {
            if (!pageReady || snapshot == null)
            {
                return;
            }
            JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
            var message = new JObject
            {
                ["type"] = "graph",
                ["snapshot"] = JObject.FromObject(snapshot, serializer),
                ["runtimes"] = BuildRuntimeArray()
            };
            PostMessage(message);
        }

        private void PushRuntimeState()
        {
            if (!pageReady || snapshot == null)
            {
                return;
            }
            PostMessage(new JObject
            {
                ["type"] = "runtime",
                ["runtimes"] = BuildRuntimeArray()
            });
        }

        private JArray BuildRuntimeArray()
        {
            var result = new JArray();
            foreach (EngineSnapshot runtime in owner.dataRun.GetSnapshots())
            {
                result.Add(new JObject
                {
                    ["procIndex"] = runtime.ProcIndex,
                    ["procId"] = runtime.ProcId.ToString("D"),
                    ["state"] = runtime.State.ToString(),
                    ["stepIndex"] = runtime.StepIndex,
                    ["opIndex"] = runtime.OpIndex,
                    ["isAlarm"] = runtime.IsAlarm,
                    ["publishedRevision"] = runtime.PublishedRevision,
                    ["appliedRevision"] = runtime.AppliedRevision
                });
            }
            return result;
        }

        private void PrepareAiPrompt()
        {
            if (snapshot == null)
            {
                return;
            }
            string selected = string.IsNullOrWhiteSpace(selectedNodeId) ? "无特定节点" : selectedNodeId;
            string request = snapshot.Scope == "project"
                ? $"请调用 get_flow_graph，scope=Project，读取当前项目总览。结合确定性图快照解释主流程、跨流程启动/停止/等待关系、循环和已验证诊断。当前关注节点：{selected}。本次只做只读解释。"
                : $"请调用 get_flow_graph，scope=Process，procIndex={snapshot.ProcIndex}，读取流程“{snapshot.Name}”（procId={snapshot.ProcId}）的当前明细。解释主路径、分支、循环、报警路径和已验证诊断。当前关注节点：{selected}。本次只做只读解释。";
            owner.ShowAiAssistantWithPrompt(request);
        }

        private void PostToast(string message, bool error)
        {
            PostMessage(new JObject
            {
                ["type"] = "toast",
                ["message"] = message,
                ["error"] = error
            });
        }

        private void PostMessage(JObject message)
        {
            if (!webAvailable || !pageReady || coreWebView == null)
            {
                return;
            }
            try
            {
                coreWebView.PostWebMessageAsJson(message.ToString(Formatting.None));
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.Runtime.InteropServices.InvalidComObjectException)
            {
            }
        }

        private void ReportGraphError(string message, Exception exception)
        {
            PostMessage(new JObject
            {
                ["type"] = "error",
                ["message"] = message
            });
            owner.dataRun?.Logger?.Log(message + Environment.NewLine + exception, LogLevel.Error);
            if (owner.frmInfo != null && !owner.frmInfo.IsDisposed)
            {
                owner.frmInfo.PrintInfo(message, FrmInfo.Level.Error);
            }
        }

        private void DisableWebPage(string message, Exception exception)
        {
            webAvailable = false;
            pageReady = false;
            webView.Visible = false;
            fallbackMessage.Text = "流程图暂不可用\r\n\r\n" + message;
            fallbackPanel.Visible = true;
            fallbackPanel.BringToFront();
            owner.dataRun?.Logger?.Log(message + Environment.NewLine + exception, LogLevel.Error);
            if (owner.frmInfo != null && !owner.frmInfo.IsDisposed)
            {
                owner.frmInfo.PrintInfo(message, FrmInfo.Level.Error);
            }
        }

        private static string ReadPageHtml()
        {
            Assembly assembly = typeof(FrmProcessFlow).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(PageResourceName)
                ?? throw new InvalidOperationException("流程图页面资源缺失。"))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
