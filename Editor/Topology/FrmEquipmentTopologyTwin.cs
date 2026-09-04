using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Automation.DeviceSdk;
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

// 模块：编辑器 / 设备拓扑孪生。
// 职责范围：承载 WebView 编辑器、桥接配置 Store 与只读实时信号，不执行设备动作。

namespace Automation
{
    /// <summary>
    /// 设备拓扑孪生的独立编辑页面。
    /// </summary>
    public sealed partial class FrmEquipmentTopologyTwin : Form
    {
        private const string PageResourceName = "Automation.Assets.EquipmentTopology.index.html";

        private static readonly JsonSerializer CamelCaseSerializer = JsonSerializer.Create(
            new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Include
            });

        private readonly WebView2 webView;
        private readonly Panel fallbackPanel;
        private readonly Label fallbackMessage;
        private readonly System.Windows.Forms.Timer runtimeRefreshTimer =
            new System.Windows.Forms.Timer { Interval = 500 };
        private CoreWebView2 coreWebView;
        private bool pageReady;
        private string lastRuntimeSignature = string.Empty;
        private CancellationTokenSource inferenceCancellation;
        private bool inferenceRunning;

        public FrmEquipmentTopologyTwin()
        {
            Text = "设备拓扑孪生";
            MinimumSize = new Size(1180, 720);
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

            Load += HandleLoad;
            VisibleChanged += HandleVisibleChanged;
            runtimeRefreshTimer.Tick += RuntimeRefreshTimer_Tick;
        }

        private async void HandleLoad(object sender, EventArgs e)
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
                DisableWebPage("设备拓扑孪生页面初始化失败：" + ex.Message, ex);
            }
        }

        private void HandleVisibleChanged(object sender, EventArgs e)
        {
            if (Visible && pageReady)
            {
                SendBootstrap();
                runtimeRefreshTimer.Start();
            }
            else
            {
                runtimeRefreshTimer.Stop();
            }
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
                        SendBootstrap();
                        runtimeRefreshTimer.Start();
                        break;
                    case "refresh":
                        SendBootstrap();
                        break;
                    case "save":
                        SaveDefinition(request["definition"]);
                        break;
                    case "requestInference":
                        await RunRuleInferenceAsync(request["definition"]).ConfigureAwait(true);
                        break;
                    case "refineWithAi":
                        await RunAiRefinementAsync(request["definition"]).ConfigureAwait(true);
                        break;
                }
            }
            catch (Exception ex)
            {
                ReportPageError("设备拓扑孪生页面交互失败：" + ex.Message, ex);
            }
        }

        private void SaveDefinition(JToken token)
        {
            if (!Workspace.Runtime.Accounts.Authorize(
                PlatformPermissionCodes.PlatformEditorOpen,
                "保存设备拓扑孪生配置",
                out string permissionError))
            {
                PostSaveResult(false, permissionError, null);
                return;
            }

            EquipmentTopologyDefinition definition;
            try
            {
                definition = token?.ToObject<EquipmentTopologyDefinition>();
            }
            catch (JsonException ex)
            {
                PostSaveResult(false, "页面提交的拓扑配置格式无效：" + ex.Message, null);
                return;
            }

            if (!Workspace.Runtime.Stores.Topology.TryCommit(
                Workspace.Runtime.Paths.ConfigPath,
                definition,
                out string error))
            {
                PostSaveResult(false, error, null);
                return;
            }

            EquipmentTopologyDefinition saved = Workspace.Runtime.Stores.Topology.CreateSnapshot();
            PostSaveResult(true, "设备拓扑孪生配置已保存。", saved);
            Workspace.Info?.PrintInfo("设备拓扑孪生配置已保存。", FrmInfo.Level.Normal);
        }

        private async Task RunRuleInferenceAsync(JToken token)
        {
            if (!TryBeginInference(false, out CancellationToken cancellationToken))
            {
                return;
            }
            try
            {
                EquipmentTopologyDefinition definition = ParseAndValidateDraft(token);
                List<Proc> processes = Workspace.Runtime.Stores.Processes.CreateSnapshot();
                PostInferenceState(true, "rules", "正在解析指令类型、参数与控制流…");
                TopologyRuleInferenceResult result = await Task.Run(
                    () => TopologyRuleInferenceService.Generate(definition, processes),
                    cancellationToken).ConfigureAwait(true);
                PostInferenceResult("rules", result.CandidateDefinition, result.BuildSummary(),
                    result.Facts.Count, result.NewNodeIds.Count, result.NewBindingIds.Count,
                    result.NewRelationIds.Count, 0);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Workspace.Info?.PrintInfo("拓扑规则反推失败：" + ex, FrmInfo.Level.Error);
                PostNotice("error", "规则反推失败", ex.Message);
            }
            finally
            {
                EndInference();
            }
        }

        private async Task RunAiRefinementAsync(JToken token)
        {
            if (!TryBeginInference(true, out CancellationToken cancellationToken))
            {
                return;
            }
            TopologyRuleInferenceResult ruleResult = null;
            try
            {
                EquipmentTopologyDefinition definition = ParseAndValidateDraft(token);
                List<Proc> processes = Workspace.Runtime.Stores.Processes.CreateSnapshot();
                PostInferenceState(true, "rules", "先重新生成确定性规则证据…");
                ruleResult = await Task.Run(
                    () => TopologyRuleInferenceService.Generate(definition, processes),
                    cancellationToken).ConfigureAwait(true);
                TopologyAiRefinementResult refined = await TopologyAiRefinementService.RefineAsync(
                    Workspace.Main,
                    ruleResult,
                    cancellationToken,
                    message => PostInferenceState(true, "ai", message)).ConfigureAwait(true);
                PostInferenceResult("ai", refined.CandidateDefinition, refined.BuildSummary(),
                    ruleResult.Facts.Count, refined.AddedNodeCount, refined.AddedBindingCount,
                    refined.AddedRelationCount, refined.RejectedProposalCount);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Workspace.Info?.PrintInfo("拓扑 AI 精修失败：" + ex, FrmInfo.Level.Error);
                if (ruleResult != null)
                {
                    PostInferenceResult("rules", ruleResult.CandidateDefinition,
                        "AI 精修未完成，已保留规则初版。" + ruleResult.BuildSummary(),
                        ruleResult.Facts.Count, ruleResult.NewNodeIds.Count,
                        ruleResult.NewBindingIds.Count, ruleResult.NewRelationIds.Count, 0);
                }
                PostNotice("error", "AI 精修失败", ex.Message);
            }
            finally
            {
                EndInference();
            }
        }

        private bool TryBeginInference(bool requireAi, out CancellationToken cancellationToken)
        {
            cancellationToken = CancellationToken.None;
            if (inferenceRunning)
            {
                PostNotice("info", "推断正在进行", "请等待当前规则扫描或 AI 精修完成。");
                return false;
            }
            if (!Workspace.Runtime.Accounts.Authorize(
                PlatformPermissionCodes.PlatformEditorOpen,
                "生成设备拓扑候选",
                out string editorPermissionError))
            {
                PostNotice("error", "没有编辑权限", editorPermissionError);
                return false;
            }
            if (requireAi && !Workspace.Runtime.Accounts.Authorize(
                PlatformPermissionCodes.PlatformAiUse,
                "使用 AI 精修设备拓扑候选",
                out string aiPermissionError))
            {
                PostNotice("error", "AI 权限不足", aiPermissionError);
                return false;
            }
            inferenceRunning = true;
            inferenceCancellation = new CancellationTokenSource();
            cancellationToken = inferenceCancellation.Token;
            return true;
        }

        private EquipmentTopologyDefinition ParseAndValidateDraft(JToken token)
        {
            EquipmentTopologyDefinition definition;
            try
            {
                definition = token?.ToObject<EquipmentTopologyDefinition>()
                    ?? Workspace.Runtime.Stores.Topology.CreateSnapshot();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("当前拓扑草稿格式无效：" + ex.Message, ex);
            }
            if (!EquipmentTopologyStore.TryValidateDefinition(definition, out string error))
            {
                throw new InvalidOperationException("请先修正当前拓扑草稿：" + error);
            }
            return definition;
        }

        private void EndInference()
        {
            inferenceRunning = false;
            inferenceCancellation?.Dispose();
            inferenceCancellation = null;
            PostInferenceState(false, string.Empty, "就绪");
        }

        private void PostInferenceState(bool running, string stage, string message)
        {
            PostMessage(new JObject
            {
                ["type"] = "inferenceState",
                ["running"] = running,
                ["stage"] = stage ?? string.Empty,
                ["message"] = message ?? string.Empty
            });
        }

        private void PostInferenceResult(
            string stage,
            EquipmentTopologyDefinition definition,
            string summary,
            int factCount,
            int nodeCount,
            int bindingCount,
            int relationCount,
            int rejectedCount)
        {
            PostMessage(new JObject
            {
                ["type"] = "inferenceResult",
                ["stage"] = stage,
                ["definition"] = ToToken(definition),
                ["summary"] = summary ?? string.Empty,
                ["factCount"] = factCount,
                ["addedNodeCount"] = nodeCount,
                ["addedBindingCount"] = bindingCount,
                ["addedRelationCount"] = relationCount,
                ["rejectedProposalCount"] = rejectedCount
            });
        }

        private void SendBootstrap()
        {
            if (!pageReady)
            {
                return;
            }
            EquipmentTopologyDefinition definition = Workspace.Runtime.Stores.Topology.CreateSnapshot();
            PostMessage(new JObject
            {
                ["type"] = "bootstrap",
                ["definition"] = ToToken(definition),
                ["catalog"] = BuildResourceCatalog(),
                ["canEdit"] = Workspace.Runtime.Accounts.CheckPermission(
                    PlatformPermissionCodes.PlatformEditorOpen, out _),
                ["safetyNotice"] = "拓扑孪生用于理解、呈现和诊断，不替代 PLC、运动控制器或硬接线安全联锁。"
            });
            PublishRuntimeState(true);
        }

        private JArray BuildResourceCatalog()
        {
            var resources = new JArray();
            foreach (DataStation station in Workspace.Runtime.Stores.Stations.Items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                int taughtCount = (station.ListDataPos ?? new List<DataPos>())
                    .Count(point => point != null && point.IsMotionReady);
                resources.Add(new JObject
                {
                    ["kind"] = "station",
                    ["resourceRef"] = station.Name,
                    ["label"] = station.Name,
                    ["subtitle"] = $"工站 · {taughtCount} 个已示教点位",
                    ["defaultNodeKind"] = "station",
                    ["searchText"] = station.Name
                });
                foreach (DataPos point in (station.ListDataPos ?? new List<DataPos>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                    .OrderBy(item => item.Index))
                {
                    resources.Add(new JObject
                    {
                        ["kind"] = "motionPoint",
                        ["resourceRef"] = station.Name + "/" + point.Name,
                        ["label"] = point.Name,
                        ["subtitle"] = station.Name + " · " + point.TeachingStateDisplay,
                        ["defaultNodeKind"] = "buffer",
                        ["state"] = point.TeachingState,
                        ["searchText"] = station.Name + " " + point.Name
                    });
                }
            }

            HashSet<string> outputNames = new HashSet<string>(
                Workspace.Runtime.Stores.IoConfiguration.OutputNames,
                StringComparer.Ordinal);
            foreach (string name in Workspace.Runtime.Stores.IoConfiguration.AllNames
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!Workspace.Runtime.Stores.IoConfiguration.ByName.TryGetValue(name, out IO io)
                    || io == null)
                {
                    continue;
                }
                bool output = outputNames.Contains(name);
                resources.Add(new JObject
                {
                    ["kind"] = output ? "ioOutput" : "ioInput",
                    ["resourceRef"] = name,
                    ["label"] = name,
                    ["subtitle"] = (output ? "输出" : "输入")
                        + (string.IsNullOrWhiteSpace(io.UsedType) ? string.Empty : " · " + io.UsedType),
                    ["defaultNodeKind"] = output ? "actuator" : "sensor",
                    ["effectLevel"] = io.EffectLevel ?? string.Empty,
                    ["note"] = io.Note ?? string.Empty,
                    ["searchText"] = string.Join(" ", new[] { name, io.UsedType, io.Note }
                        .Where(value => !string.IsNullOrWhiteSpace(value)))
                });
            }
            return resources;
        }

        private void RuntimeRefreshTimer_Tick(object sender, EventArgs e)
        {
            PublishRuntimeState(false);
        }

        private void PublishRuntimeState(bool force)
        {
            if (!pageReady || !Visible)
            {
                return;
            }
            EquipmentStateHistoryService history = Workspace.Runtime.StateHistory;
            if (history != null)
            {
                long revision = history.Revision;
                string historySignature = "history:" + revision;
                if (!force && string.Equals(historySignature, lastRuntimeSignature, StringComparison.Ordinal))
                {
                    return;
                }
                lastRuntimeSignature = historySignature;
                EquipmentStateHistoryWindow window = history.GetRecentWindow(500);
                PostMessage(new JObject
                {
                    ["type"] = "stateHistory",
                    ["window"] = ToToken(window),
                    ["currentSnapshot"] = ToToken(history.GetCurrentSnapshot()),
                    ["perceptionRunning"] = Workspace.Runtime.StatePerception?.IsRunning == true,
                    ["observationError"] = Workspace.Runtime.StatePerception?.LastObservationError
                        ?? history.LastPersistenceError
                        ?? string.Empty
                });
                return;
            }

            // 初始化降级时保留旧的只读缓存展示，但明确不把它冒充为可回放状态历史。
            List<IO> signals = Workspace.Runtime.Stores.IoConfiguration.ByName.Values
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToList();
            string signature = string.Join("|", signals.Select(item => item.Name + ":" + item.Status));
            if (!force && string.Equals(signature, lastRuntimeSignature, StringComparison.Ordinal))
            {
                return;
            }
            lastRuntimeSignature = signature;
            PostMessage(new JObject
            {
                ["type"] = "runtime",
                ["timeUtc"] = DateTime.UtcNow.ToString("O"),
                ["signals"] = new JArray(signals.Select(item => new JObject
                {
                    ["sourceKind"] = "io",
                    ["resourceRef"] = item.Name,
                    ["value"] = item.Status
                }))
            });
        }

        private void PostSaveResult(
            bool success,
            string message,
            EquipmentTopologyDefinition definition)
        {
            var payload = new JObject
            {
                ["type"] = "saveResult",
                ["success"] = success,
                ["message"] = message ?? string.Empty
            };
            if (definition != null)
            {
                payload["definition"] = ToToken(definition);
            }
            PostMessage(payload);
        }

        private void PostNotice(string tone, string title, string message)
        {
            PostMessage(new JObject
            {
                ["type"] = "notice",
                ["tone"] = tone,
                ["title"] = title,
                ["message"] = message
            });
        }

        private void PostMessage(JObject message)
        {
            if (!pageReady || coreWebView == null)
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

        private static JToken ToToken(object value)
        {
            return value == null ? JValue.CreateNull() : JToken.FromObject(value, CamelCaseSerializer);
        }

        private static string ReadPageHtml()
        {
            Assembly assembly = typeof(FrmEquipmentTopologyTwin).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(PageResourceName)
                ?? throw new InvalidOperationException("设备拓扑孪生页面资源缺失。"))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        private void DisableWebPage(string message, Exception exception)
        {
            runtimeRefreshTimer.Stop();
            webView.Visible = false;
            fallbackPanel.Visible = true;
            fallbackMessage.Text = message;
            Workspace.Info?.PrintInfo(message, FrmInfo.Level.Error);
        }

        private void ReportPageError(string message, Exception exception)
        {
            Workspace.Info?.PrintInfo(message, FrmInfo.Level.Error);
            PostNotice("error", "页面交互失败", message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inferenceCancellation?.Cancel();
                inferenceCancellation?.Dispose();
                inferenceCancellation = null;
                runtimeRefreshTimer.Stop();
                runtimeRefreshTimer.Dispose();
                if (coreWebView != null)
                {
                    coreWebView.WebMessageReceived -= WebView_WebMessageReceived;
                }
                webView.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
