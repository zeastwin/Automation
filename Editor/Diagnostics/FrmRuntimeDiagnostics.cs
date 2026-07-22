// 模块：编辑器 / 诊断。
// 职责范围：运行日志、状态、流程图、断点、性能和事故诊断页面。

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    /// 面向设备开发、调试和维护人员的只读智能诊断中心。
    /// WebView2 负责呈现，运行事实仍来自确定性的流程快照和黑匣子记录器。
    /// </summary>
    public sealed class FrmRuntimeDiagnostics : Form
    {
        private const string PageResourceName = "Automation.Assets.RuntimeDiagnostics.index.html";

        private readonly FrmMain owner;
        private readonly WebView2 webView;
        private readonly Panel fallbackPanel;
        private readonly Label fallbackMessage;
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private CoreWebView2 coreWebView;
        private GooseAcpClient client;
        private CancellationTokenSource analysisCts;
        private bool pageReady;
        private bool webAvailable = true;
        private bool analysisRunning;
        private bool fullTimeline;
        private bool reportHasContent;
        private int selectedProcIndex = -1;
        private int lastEvidenceProcIndex = -1;
        private long lastEvidenceRevision = -1;
        private bool lastFullTimeline;
        private string lastProcessSignature = string.Empty;

        public FrmRuntimeDiagnostics(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            if (!owner.RuntimeDiagnosticsEnabled)
            {
                throw new InvalidOperationException("智能诊断中心已在程序设置中停用。");
            }
            Text = "智能诊断中心";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1080, 720);
            Size = new Size(1440, 900);
            BackColor = UiPalette.Background;
            Font = new Font("Microsoft YaHei UI", 9F);
            UiBranding.Apply(this);

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
            fallbackPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(80),
                BackColor = UiPalette.Background,
                Visible = false
            };
            fallbackPanel.Controls.Add(fallbackMessage);
            Controls.Add(fallbackPanel);
            Controls.Add(webView);

            Load += FrmRuntimeDiagnostics_Load;
            Shown += HandleShown;
            FormClosing += HandleFormClosing;
            refreshTimer.Tick += RefreshTimer_Tick;
        }

        private async void FrmRuntimeDiagnostics_Load(object sender, EventArgs e)
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                coreWebView = webView.CoreWebView2;
                coreWebView.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView.Settings.AreDevToolsEnabled = false;
                coreWebView.Settings.AreBrowserAcceleratorKeysEnabled = true;
                coreWebView.WebMessageReceived += WebView_WebMessageReceived;
                coreWebView.NavigateToString(ReadPageHtml());
            }
            catch (Exception ex)
            {
                DisableWebPage("智能诊断页面初始化失败：" + ex.Message, ex);
            }
        }

        private void HandleShown(object sender, EventArgs e)
        {
            RefreshProcesses(true);
            RefreshEvidence(true);
            refreshTimer.Start();
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshProcesses();
            RefreshEvidence();
        }

        private async void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                JObject request = JObject.Parse(e.TryGetWebMessageAsString());
                string action = request["action"]?.Value<string>() ?? string.Empty;
                switch (action)
                {
                    case "ready":
                        pageReady = true;
                        RefreshProcesses(true);
                        RefreshEvidence(true);
                        PostAnalysisState(false, "就绪", "neutral", false);
                        break;
                    case "selectProcess":
                        if (!analysisRunning && request["procIndex"]?.Value<int?>() is int procIndex)
                        {
                            selectedProcIndex = procIndex;
                            lastEvidenceProcIndex = -1;
                            RefreshProcesses(true);
                            RefreshEvidence(true);
                        }
                        break;
                    case "setFullTimeline":
                        fullTimeline = request["enabled"]?.Value<bool?>() == true;
                        RefreshEvidence(true);
                        break;
                    case "analyze":
                        await StartAnalysisAsync(request["symptom"]?.Value<string>()).ConfigureAwait(true);
                        break;
                    case "cancel":
                        CancelAnalysis();
                        break;
                }
            }
            catch (Exception ex)
            {
                ReportPageError("智能诊断页面交互失败：" + ex.Message, ex);
            }
        }

        private void RefreshProcesses(bool force = false)
        {
            IReadOnlyList<EngineSnapshot> snapshots = owner.dataRun?.GetSnapshots() ?? Array.Empty<EngineSnapshot>();
            List<DiagnosticProcessItem> items = snapshots.Select(snapshot => new DiagnosticProcessItem
            {
                Index = snapshot.ProcIndex,
                Name = string.IsNullOrWhiteSpace(snapshot.ProcName) ? $"流程{snapshot.ProcIndex}" : snapshot.ProcName,
                State = snapshot.State,
                AlarmMessage = snapshot.AlarmMessage ?? string.Empty
            }).ToList();
            string signature = string.Join("|", items.Select(item => item.Signature));
            bool selectionChanged = false;
            if (!items.Any(item => item.Index == selectedProcIndex))
            {
                DiagnosticProcessItem target = items.FirstOrDefault(item => item.State == ProcRunState.Alarming)
                    ?? items.FirstOrDefault();
                int nextIndex = target?.Index ?? -1;
                selectionChanged = selectedProcIndex != nextIndex;
                selectedProcIndex = nextIndex;
            }
            if (!force && !selectionChanged && string.Equals(signature, lastProcessSignature, StringComparison.Ordinal))
            {
                return;
            }
            lastProcessSignature = signature;
            if (selectionChanged) lastEvidenceProcIndex = -1;

            var processes = new JArray(items.Select(item => new JObject
            {
                ["index"] = item.Index,
                ["name"] = item.Name,
                ["state"] = item.State.ToString(),
                ["stateText"] = LocalizeState(item.State),
                ["stateTone"] = GetStateTone(item.State),
                ["alarmMessage"] = item.AlarmMessage
            }));
            PostMessage(new JObject
            {
                ["type"] = "processes",
                ["processes"] = processes,
                ["selectedProcIndex"] = selectedProcIndex,
                ["analysisRunning"] = analysisRunning
            });
        }

        private void RefreshEvidence(bool force = false)
        {
            if (selectedProcIndex < 0)
            {
                lastEvidenceProcIndex = -1;
                PostMessage(new JObject
                {
                    ["type"] = "timeline",
                    ["procIndex"] = -1,
                    ["events"] = new JArray(),
                    ["capturedEventCount"] = 0,
                    ["rawRelevantEventCount"] = 0,
                    ["emptyMessage"] = "当前没有可诊断流程。"
                });
                return;
            }

            RuntimeBlackBoxRecorder recorder = owner.RuntimeBlackBoxRecorder;
            long revision = recorder?.Revision ?? -1;
            if (!force
                && revision == lastEvidenceRevision
                && selectedProcIndex == lastEvidenceProcIndex
                && fullTimeline == lastFullTimeline)
            {
                return;
            }

            JObject evidence = recorder?.BuildTimelinePackage(selectedProcIndex, fullTimeline)
                ?? RuntimeBlackBoxRecorder.BuildUnavailableEvidencePackage(selectedProcIndex);
            lastEvidenceRevision = revision;
            lastEvidenceProcIndex = selectedProcIndex;
            lastFullTimeline = fullTimeline;
            var events = new JArray();
            foreach (JObject item in (evidence["events"] as JArray ?? new JArray()).OfType<JObject>())
            {
                DateTime.TryParse(item["timeUtc"]?.Value<string>(), out DateTime time);
                string eventName = item["eventName"]?.Value<string>() ?? string.Empty;
                events.Add(new JObject
                {
                    ["time"] = time == default(DateTime) ? string.Empty : time.ToLocalTime().ToString("HH:mm:ss.fff"),
                    ["eventName"] = eventName,
                    ["eventText"] = LocalizeEvent(eventName),
                    ["tone"] = GetEventTone(eventName, item["outcome"]?.Value<string>()),
                    ["position"] = BuildPosition(item),
                    ["detail"] = BuildDetail(item)
                });
            }
            PostMessage(new JObject
            {
                ["type"] = "timeline",
                ["procIndex"] = selectedProcIndex,
                ["events"] = events,
                ["capturedEventCount"] = evidence["capturedEventCount"]?.Value<int?>() ?? events.Count,
                ["rawRelevantEventCount"] = evidence["rawRelevantEventCount"]?.Value<int?>() ?? events.Count,
                ["incidentId"] = evidence["incidentId"]?.Value<string>() ?? string.Empty,
                ["selectionMode"] = evidence["selectionMode"]?.Value<string>() ?? string.Empty,
                ["windowStartUtc"] = ToLocalDisplayTime(evidence["windowStartUtc"]?.Value<string>()),
                ["windowEndUtc"] = ToLocalDisplayTime(evidence["windowEndUtc"]?.Value<string>()),
                ["fullTimeline"] = fullTimeline,
                ["completePostFailureWindow"] = evidence["completePostFailureWindow"],
                ["emptyMessage"] = recorder == null
                    ? "运行黑匣子尚未初始化。"
                    : "尚无运行事件。黑匣子会在流程状态、变量、通讯和报警变化时形成证据。"
            });
        }

        private async Task StartAnalysisAsync(string symptom)
        {
            if (analysisRunning || selectedProcIndex < 0) return;
            int procIndex = selectedProcIndex;
            analysisRunning = true;
            reportHasContent = false;
            analysisCts = new CancellationTokenSource();
            PostAnalysisState(true, "正在准备只读诊断…", "working", true);
            RefreshProcesses(true);
            try
            {
                owner.EnsureAiInfrastructureStarted();
                if (!GooseConfigStorage.TryLoad(out GooseConfig stored, out string configError))
                {
                    throw new InvalidOperationException(configError);
                }
                string diagnosticMcpUri = await owner.McpServerManager
                    .EnsureRuntimeDiagnosticStartedAsync().ConfigureAwait(true);
                GooseConfig config = CreateDiagnosticConfig(stored, diagnosticMcpUri);

                client = new GooseAcpClient(owner.Runtime, config);
                client.PermissionRequestHandler = HandlePermissionRequest;
                client.EventReceived += HandleAcpEvent;
                PostAnalysisState(true, "正在分析…", "working", false);
                await client.PromptAsync(
                    BuildDiagnosticPrompt(procIndex, symptom),
                    Array.Empty<GooseFileAttachment>(),
                    analysisCts.Token).ConfigureAwait(true);
                string finalResponse = client.LastAssistantResponse;
                if (!reportHasContent && !string.IsNullOrWhiteSpace(finalResponse))
                {
                    PostReportChunk(finalResponse);
                }
                PostAnalysisState(false, "分析完成", "success", false);
                RefreshEvidence(true);
            }
            catch (OperationCanceledException)
            {
                if (!reportHasContent) PostReportChunk("本次分析已停止。");
                PostAnalysisState(false, "已停止", "neutral", false);
            }
            catch (Exception ex)
            {
                PostReportChunk((reportHasContent ? "\n\n" : string.Empty) + "分析失败：" + ex.Message);
                PostAnalysisState(false, "分析失败", "danger", false);
                owner.dataRun?.Logger?.Log("智能诊断分析失败：" + ex, LogLevel.Error);
            }
            finally
            {
                if (client != null)
                {
                    client.EventReceived -= HandleAcpEvent;
                    client.Dispose();
                    client = null;
                }
                analysisCts?.Dispose();
                analysisCts = null;
                analysisRunning = false;
                RefreshProcesses(true);
            }
        }

        private static GooseConfig CreateDiagnosticConfig(GooseConfig source, string diagnosticMcpUri)
        {
            return new GooseConfig
            {
                GooseExecutablePath = source.GooseExecutablePath,
                WorkingDirectory = source.WorkingDirectory,
                McpUri = diagnosticMcpUri,
                SessionName = "runtime_diagnostics_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Provider = source.Provider,
                Model = source.Model,
                ModelServiceId = source.ModelServiceId,
                ModelServices = GooseConfigStorage.CloneModelServices(source.ModelServices),
                Temperature = source.Temperature,
                MaxTurns = source.MaxTurns,
                MaxOutputTokens = source.MaxOutputTokens,
                AutoApproveMode = false,
                ToolProfile = "RuntimeDiagnostic"
            };
        }

        private static string BuildDiagnosticPrompt(int procIndex, string symptom)
        {
            string symptomText = string.IsNullOrWhiteSpace(symptom) ? "用户未补充现场症状" : symptom.Trim();
            return $@"这是智能诊断中心发起的运行态只读取证任务。
目标流程 procIndex={procIndex}。
现场症状：{symptomText}

请先调用 diagnose_issue 获取运行快照、结构校验、当前指令上下文和黑匣子时间线；只有证据需要时再调用其他只读工具核对当前 IO、变量、PLC、通讯、报警或资源引用。

当前授权范围仅包含读取和解释，配置、变量值、流程状态及设备动作保持不变。诊断报告使用以下结构：
1. 已确认事实：只写工具结果直接证明的事实；
2. 关键时间线：列出与异常相关的状态变化；
3. 可能原因：逐项注明支持证据、反证和不确定性；
4. 工程师检查项：给出人工取证顺序和需要核对的对象；
5. 证据缺口：明确当前黑匣子未采集或无法证明的内容。

本报告用于工程师定位根因，不提供自动恢复、重新启动、变量写入或配置修改方案。";
        }

        private void HandleAcpEvent(GooseAcpEvent item)
        {
            if (item == null || IsDisposed) return;
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<GooseAcpEvent>(HandleAcpEvent), item);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            if (string.Equals(item.Kind, "assistant_chunk", StringComparison.Ordinal))
            {
                PostReportChunk(item.Text ?? string.Empty);
            }
            else if (string.Equals(item.Kind, "tool_call", StringComparison.Ordinal))
            {
                PostAnalysisState(true, "取证：" + (item.Text ?? "读取现场事实"), "working", false);
            }
            else if (string.Equals(item.Kind, "error", StringComparison.Ordinal)
                || string.Equals(item.Kind, "stderr", StringComparison.Ordinal)
                || string.Equals(item.Kind, "exit", StringComparison.Ordinal))
            {
                PostAnalysisState(true, item.Text ?? "AI 运行异常", "danger", false);
            }
        }

        private static JObject HandlePermissionRequest(JObject request)
        {
            JToken toolCall = request?["toolCall"];
            string extensionName = toolCall?["_meta"]?["goose"]?["toolCall"]?["extensionName"]?.Value<string>()
                ?? toolCall?["extensionName"]?.Value<string>()
                ?? string.Empty;
            string toolName = toolCall?["_meta"]?["goose"]?["toolCall"]?["toolName"]?.Value<string>()
                ?? toolCall?["name"]?.Value<string>()
                ?? string.Empty;
            bool automationTool = string.Equals(extensionName, "automation", StringComparison.OrdinalIgnoreCase)
                || toolName.IndexOf("automation__", StringComparison.OrdinalIgnoreCase) >= 0;
            return new JObject
            {
                ["outcome"] = new JObject
                {
                    ["outcome"] = automationTool ? "allowed" : "cancelled"
                }
            };
        }

        private void PostAnalysisState(bool running, string status, string tone, bool clearReport)
        {
            PostMessage(new JObject
            {
                ["type"] = "analysisState",
                ["running"] = running,
                ["status"] = status ?? string.Empty,
                ["tone"] = tone ?? "neutral",
                ["clearReport"] = clearReport,
                ["canAnalyze"] = selectedProcIndex >= 0
            });
        }

        private void PostReportChunk(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            reportHasContent = true;
            PostMessage(new JObject
            {
                ["type"] = "reportChunk",
                ["text"] = text
            });
        }

        private void PostMessage(JObject message)
        {
            if (!webAvailable || !pageReady || coreWebView == null || IsDisposed) return;
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

        private void CancelAnalysis()
        {
            client?.Cancel();
            analysisCts?.Cancel();
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            refreshTimer.Stop();
            CancelAnalysis();
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
        }

        private void ReportPageError(string message, Exception exception)
        {
            PostMessage(new JObject
            {
                ["type"] = "pageError",
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
            fallbackMessage.Text = "智能诊断中心暂不可用\r\n\r\n" + message;
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
            Assembly assembly = typeof(FrmRuntimeDiagnostics).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(PageResourceName)
                ?? throw new InvalidOperationException("智能诊断页面资源缺失。"))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        private static string BuildPosition(JObject item)
        {
            string resource = item["resourceName"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(resource)) return resource;
            int? step = item["stepIndex"]?.Value<int?>();
            int? op = item["opIndex"]?.Value<int?>();
            if (step.HasValue && op.HasValue) return $"{step}-{op}";
            return item["source"]?.Value<string>() ?? "—";
        }

        private static string BuildDetail(JObject item)
        {
            string message = item["message"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(message)) return message;
            string oldValue = item["oldValue"]?.Value<string>();
            string newValue = item["newValue"]?.Value<string>();
            if (oldValue != null || newValue != null) return $"{oldValue ?? "—"} → {newValue ?? "—"}";
            string state = item["state"]?.Value<string>();
            return string.IsNullOrWhiteSpace(state) ? item["outcome"]?.Value<string>() ?? string.Empty : state;
        }

        private static string ToLocalDisplayTime(string value)
        {
            return DateTime.TryParse(value, out DateTime parsed)
                ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : string.Empty;
        }

        private static string LocalizeEvent(string eventName)
        {
            switch (eventName)
            {
                case "process.started": return "流程启动";
                case "process.position.changed": return "流程位置变化";
                case "operation.failed": return "指令失败";
                case "process.completed": return "流程结束";
                case "variable.changed": return "变量变化";
                case "plc.runtime": return "PLC 运行事件";
                case "communication.frames_dropped": return "通讯丢帧";
                case "simulation.connection.faulted": return "仿真连接故障";
                default: return eventName ?? string.Empty;
            }
        }

        private static string GetEventTone(string eventName, string outcome)
        {
            if (string.Equals(eventName, "operation.failed", StringComparison.Ordinal)
                || string.Equals(outcome, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(outcome, "alarm", StringComparison.OrdinalIgnoreCase)) return "danger";
            if (string.Equals(eventName, "process.started", StringComparison.Ordinal)
                || string.Equals(eventName, "process.completed", StringComparison.Ordinal)) return "success";
            if (string.Equals(eventName, "communication.frames_dropped", StringComparison.Ordinal)
                || string.Equals(eventName, "simulation.connection.faulted", StringComparison.Ordinal)) return "warning";
            return "neutral";
        }

        private static string LocalizeState(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Running: return "运行中";
                case ProcRunState.Paused: return "已暂停";
                case ProcRunState.Alarming: return "报警";
                case ProcRunState.Stopping: return "停止中";
                case ProcRunState.SingleStep: return "单步";
                default: return "已停止";
            }
        }

        private static string GetStateTone(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Running: return "success";
                case ProcRunState.Alarming: return "danger";
                case ProcRunState.Paused:
                case ProcRunState.Stopping: return "warning";
                default: return "neutral";
            }
        }

        private sealed class DiagnosticProcessItem
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public ProcRunState State { get; set; }
            public string AlarmMessage { get; set; }
            public string Signature => $"{Index}:{Name}:{State}:{AlarmMessage}";
        }
    }
}
