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
using System.Threading.Tasks;
using System.Windows.Forms;

// 模块：编辑器 / Machine Agent。
// 职责范围：承载独立 Machine Agent 对话、设备运行总览、状态时间线、受控执行预演和拓扑子模块。

namespace Automation
{
    /// <summary>
    /// Machine Agent 的一级工作台。设备拓扑孪生是该工作台下的子模块，
    /// 后续受控执行能力也应在此处接入，不能复用 Process Agent 的流程编写身份。
    /// </summary>
    public sealed partial class FrmMachineAgent : Form
    {
        private const string PageResourceName = "Automation.Assets.MachineAgent.index.html";
        private const int RefreshIntervalMilliseconds = 500;

        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel();
        private readonly Panel navigationPanel = new Panel();
        private readonly Panel contentHost = new Panel();
        private readonly Label perceptionStatusLabel = new Label();
        private readonly Button agentButton;
        private readonly Button overviewButton;
        private readonly Button timelineButton;
        private readonly Button topologyButton;
        private readonly WebView2 dashboardWebView;
        private readonly Panel fallbackPanel = new Panel();
        private readonly Label fallbackMessage = new Label();
        private readonly Timer refreshTimer = new Timer { Interval = RefreshIntervalMilliseconds };
        private CoreWebView2 dashboardCore;
        private FrmEquipmentTopologyTwin topologyPage;
        private Button activeNavigationButton;
        private bool dashboardReady;
        private string dashboardView = "agent";
        private string lastDashboardSignature = string.Empty;

        public FrmMachineAgent()
        {
            Text = "Machine Agent";
            MinimumSize = new Size(1180, 720);
            BackColor = UiPalette.Background;
            Font = new Font("Microsoft YaHei UI", 10F);
            UiBranding.Apply(this);

            dashboardWebView = new WebView2
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Automation",
                        "WebView2")
                }
            };

            agentButton = CreateNavigationButton("智能交互");
            overviewButton = CreateNavigationButton("运行总览");
            timelineButton = CreateNavigationButton("状态时间线");
            topologyButton = CreateNavigationButton("拓扑与状态");

            BuildLayout();
            agentButton.Click += (sender, args) => ShowDashboard("agent", agentButton);
            overviewButton.Click += (sender, args) => ShowDashboard("overview", overviewButton);
            timelineButton.Click += (sender, args) => ShowDashboard("timeline", timelineButton);
            topologyButton.Click += (sender, args) => ShowTopology();
            refreshTimer.Tick += RefreshTimer_Tick;
            Load += HandleLoad;
            VisibleChanged += HandleVisibleChanged;
        }

        internal bool IsDashboardLoaded => dashboardReady;

        private void BuildLayout()
        {
            rootLayout.Dock = DockStyle.Fill;
            rootLayout.Margin = Padding.Empty;
            rootLayout.Padding = Padding.Empty;
            rootLayout.ColumnCount = 1;
            rootLayout.RowCount = 2;
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            navigationPanel.Dock = DockStyle.Fill;
            navigationPanel.Margin = Padding.Empty;
            navigationPanel.Padding = new Padding(18, 12, 18, 12);
            navigationPanel.BackColor = UiPalette.SurfaceStrong;

            Label productMark = new Label
            {
                AutoSize = false,
                Location = new Point(20, 14),
                Size = new Size(166, 44),
                ForeColor = UiPalette.TextPrimary,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                Text = "Machine Agent",
                TextAlign = ContentAlignment.MiddleLeft
            };

            agentButton.Location = new Point(202, 15);
            overviewButton.Location = new Point(342, 15);
            timelineButton.Location = new Point(482, 15);
            topologyButton.Location = new Point(622, 15);

            perceptionStatusLabel.AutoSize = false;
            perceptionStatusLabel.Location = new Point(920, 15);
            perceptionStatusLabel.Size = new Size(188, 44);
            perceptionStatusLabel.ForeColor = UiPalette.TextMuted;
            perceptionStatusLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            perceptionStatusLabel.Text = "●  状态感知待连接";
            perceptionStatusLabel.TextAlign = ContentAlignment.MiddleCenter;

            Panel divider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = UiPalette.Stroke
            };

            navigationPanel.Resize += (sender, args) =>
            {
                perceptionStatusLabel.Left = Math.Max(780, navigationPanel.ClientSize.Width - perceptionStatusLabel.Width - 20);
            };
            navigationPanel.Controls.Add(productMark);
            navigationPanel.Controls.Add(agentButton);
            navigationPanel.Controls.Add(overviewButton);
            navigationPanel.Controls.Add(timelineButton);
            navigationPanel.Controls.Add(topologyButton);
            navigationPanel.Controls.Add(perceptionStatusLabel);
            navigationPanel.Controls.Add(divider);

            contentHost.Dock = DockStyle.Fill;
            contentHost.Margin = Padding.Empty;
            contentHost.BackColor = UiPalette.Background;

            fallbackMessage.Dock = DockStyle.Fill;
            fallbackMessage.Padding = new Padding(48);
            fallbackMessage.Font = new Font("Microsoft YaHei UI", 12F);
            fallbackMessage.ForeColor = UiPalette.TextSecondary;
            fallbackMessage.TextAlign = ContentAlignment.MiddleCenter;
            fallbackPanel.Dock = DockStyle.Fill;
            fallbackPanel.Padding = new Padding(80);
            fallbackPanel.BackColor = UiPalette.Background;
            fallbackPanel.Visible = false;
            fallbackPanel.Controls.Add(fallbackMessage);

            contentHost.Controls.Add(fallbackPanel);
            contentHost.Controls.Add(dashboardWebView);
            rootLayout.Controls.Add(navigationPanel, 0, 0);
            rootLayout.Controls.Add(contentHost, 0, 1);
            Controls.Add(rootLayout);
            SetActiveNavigation(agentButton);
        }

        private static Button CreateNavigationButton(string title)
        {
            var button = new Button
            {
                AutoSize = false,
                Size = new Size(132, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = UiPalette.SurfaceStrong,
                ForeColor = UiPalette.TextSecondary,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                Text = title,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoft;
            button.FlatAppearance.MouseDownBackColor = UiPalette.Selection;
            return button;
        }

        private async void HandleLoad(object sender, EventArgs e)
        {
            try
            {
                await dashboardWebView.EnsureCoreWebView2Async();
                dashboardCore = dashboardWebView.CoreWebView2;
                dashboardCore.Settings.AreDefaultContextMenusEnabled = false;
                dashboardCore.Settings.AreDevToolsEnabled = false;
                dashboardCore.Settings.AreBrowserAcceleratorKeysEnabled = true;
                dashboardCore.WebMessageReceived += Dashboard_WebMessageReceived;
                dashboardCore.NavigateToString(ReadPageHtml());
            }
            catch (Exception ex)
            {
                DisableDashboard("Machine Agent 工作台初始化失败：" + ex.Message);
            }
        }

        private void HandleVisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                refreshTimer.Start();
                PublishDashboard(true);
            }
            else
            {
                refreshTimer.Stop();
            }
        }

        private async void Dashboard_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                JObject request = JObject.Parse(e.TryGetWebMessageAsString());
                string action = request["action"]?.Value<string>() ?? string.Empty;
                switch (action)
                {
                    case "ready":
                        dashboardReady = true;
                        InitializeAgentSurface();
                        PublishDashboard(true);
                        break;
                    case "refresh":
                        PublishDashboard(true);
                        break;
                    case "openTopology":
                        ShowTopology();
                        break;
                    case "showTimeline":
                        ShowDashboard("timeline", timelineButton);
                        break;
                    case "showOverview":
                        ShowDashboard("overview", overviewButton);
                        break;
                    case "showAgent":
                        ShowDashboard("agent", agentButton);
                        break;
                    case "sendAgentMessage":
                        await SendAgentMessageAsync(request["text"]?.Value<string>()).ConfigureAwait(true);
                        break;
                    case "cancelAgent":
                        CancelAgentRequest();
                        break;
                    case "executePreview":
                        ExecuteAgentPreview(request["previewId"]?.Value<string>());
                        break;
                    case "discardPreview":
                        DiscardAgentPreview(request["previewId"]?.Value<string>());
                        break;
                }
            }
            catch (Exception ex)
            {
                Workspace.Info?.PrintInfo(
                    "Machine Agent 页面交互失败：" + ex.Message,
                    FrmInfo.Level.Error);
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            UpdatePerceptionStatus();
            if (dashboardWebView.Visible)
            {
                PublishDashboard(false);
            }
        }

        private void ShowDashboard(string view, Button navigationButton)
        {
            dashboardView = string.Equals(view, "timeline", StringComparison.Ordinal)
                ? "timeline"
                : string.Equals(view, "agent", StringComparison.Ordinal) ? "agent" : "overview";
            SetActiveNavigation(navigationButton);
            if (topologyPage != null && !topologyPage.IsDisposed)
            {
                topologyPage.Visible = false;
            }
            fallbackPanel.Visible = false;
            dashboardWebView.Visible = true;
            dashboardWebView.BringToFront();
            PostMessage(new JObject
            {
                ["type"] = "view",
                ["view"] = dashboardView
            });
            PublishDashboard(true);
        }

        private void ShowTopology()
        {
            SetActiveNavigation(topologyButton);
            dashboardWebView.Visible = false;
            fallbackPanel.Visible = false;
            topologyPage = Workspace.GetOrCreateEquipmentTopologyTwin();
            if (!contentHost.Controls.Contains(topologyPage))
            {
                topologyPage.Hide();
                topologyPage.Parent?.Controls.Remove(topologyPage);
                topologyPage.FormBorderStyle = FormBorderStyle.None;
                topologyPage.ShowIcon = false;
                topologyPage.ShowInTaskbar = false;
                topologyPage.TopLevel = false;
                topologyPage.Dock = DockStyle.Fill;
                contentHost.Controls.Add(topologyPage);
            }
            topologyPage.Show();
            topologyPage.BringToFront();
            topologyPage.Focus();
        }

        private void SetActiveNavigation(Button button)
        {
            if (activeNavigationButton != null)
            {
                activeNavigationButton.BackColor = UiPalette.SurfaceStrong;
                activeNavigationButton.ForeColor = UiPalette.TextSecondary;
                activeNavigationButton.FlatAppearance.BorderSize = 0;
            }
            activeNavigationButton = button;
            if (activeNavigationButton != null)
            {
                activeNavigationButton.BackColor = UiPalette.BrandSoft;
                activeNavigationButton.ForeColor = UiPalette.Brand;
                activeNavigationButton.FlatAppearance.BorderColor = UiPalette.Selection;
                activeNavigationButton.FlatAppearance.BorderSize = 1;
            }
        }

        private void PublishDashboard(bool force)
        {
            if (!dashboardReady || dashboardCore == null || !Visible)
            {
                return;
            }

            EquipmentTopologyDefinition topology = Workspace.Runtime.Stores.Topology.CreateSnapshot();
            EquipmentStateHistoryService history = Workspace.Runtime.StateHistory;
            long stateRevision = history?.Revision ?? 0;
            IReadOnlyList<EngineSnapshot> snapshots = Workspace.Runtime.ProcessEngine?.GetSnapshots()
                ?? new List<EngineSnapshot>();
            string processSignature = string.Join("|", snapshots.Select(item => item == null
                ? "null"
                : item.ProcId + ":" + item.State + ":" + item.StepIndex + ":" + item.OpIndex
                    + ":" + item.PublishedRevision + ":" + item.AlarmMessage));
            string signature = dashboardView + ":" + topology.Revision + ":" + stateRevision
                + ":" + processSignature + ":" + (Workspace.Runtime.StatePerception?.LastObservationError ?? string.Empty);
            if (!force && string.Equals(signature, lastDashboardSignature, StringComparison.Ordinal))
            {
                return;
            }
            lastDashboardSignature = signature;
            UpdatePerceptionStatus();

            EquipmentStateSnapshot currentState = history?.GetCurrentSnapshot()
                ?? new EquipmentStateSnapshot();
            EquipmentStateHistoryWindow historyWindow = history?.GetRecentWindow(120)
                ?? new EquipmentStateHistoryWindow();
            Dictionary<string, EquipmentNodeStateProjection> statesByNode =
                (currentState.NodeStates ?? new List<EquipmentNodeStateProjection>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                .GroupBy(item => item.NodeId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Sequence).First(),
                    StringComparer.Ordinal);

            List<EquipmentTopologyNode> nodes = topology.Nodes ?? new List<EquipmentTopologyNode>();
            List<EquipmentTopologyRelation> relations = topology.Relations ?? new List<EquipmentTopologyRelation>();
            int confirmed = nodes.Count(item => string.Equals(item?.ReviewState, "confirmed", StringComparison.Ordinal))
                + relations.Count(item => string.Equals(item?.ReviewState, "confirmed", StringComparison.Ordinal));
            int candidates = nodes.Count(item => string.Equals(item?.ReviewState, "candidate", StringComparison.Ordinal))
                + relations.Count(item => string.Equals(item?.ReviewState, "candidate", StringComparison.Ordinal));
            int conflicts = nodes.Count(item => string.Equals(item?.ReviewState, "conflict", StringComparison.Ordinal))
                + relations.Count(item => string.Equals(item?.ReviewState, "conflict", StringComparison.Ordinal));
            int bindings = nodes.Sum(item => item?.StateBindings?.Count ?? 0);
            int skills = nodes.Sum(item => item?.Skills?.Count ?? 0);
            int activeProcesses = snapshots.Count(item => item != null && !item.State.IsInactive());
            int alarmProcesses = snapshots.Count(item => item?.IsAlarm == true);
            int uncertainStates = statesByNode.Values.Count(item => !string.Equals(
                item.Quality, EquipmentStateQualities.Good, StringComparison.Ordinal));

            var payload = new JObject
            {
                ["type"] = "bootstrap",
                ["view"] = dashboardView,
                ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["observeOnly"] = false,
                ["controlMode"] = "preview_confirm_execute",
                ["perception"] = new JObject
                {
                    ["running"] = Workspace.Runtime.StatePerception?.IsRunning == true,
                    ["error"] = Workspace.Runtime.StatePerception?.LastObservationError
                        ?? history?.LastPersistenceError
                        ?? string.Empty,
                    ["latestSequence"] = stateRevision
                },
                ["topology"] = new JObject
                {
                    ["name"] = topology.Name ?? "设备拓扑孪生",
                    ["revision"] = topology.Revision,
                    ["nodes"] = nodes.Count,
                    ["relations"] = relations.Count,
                    ["bindings"] = bindings,
                    ["skills"] = skills,
                    ["confirmed"] = confirmed,
                    ["candidates"] = candidates,
                    ["conflicts"] = conflicts,
                    ["updatedAtUtc"] = topology.UpdatedAtUtc == default(DateTime)
                        ? string.Empty
                        : topology.UpdatedAtUtc.ToString("O")
                },
                ["summary"] = new JObject
                {
                    ["processes"] = snapshots.Count,
                    ["activeProcesses"] = activeProcesses,
                    ["alarmProcesses"] = alarmProcesses,
                    ["knownNodeStates"] = statesByNode.Count,
                    ["uncertainStates"] = uncertainStates
                },
                ["processes"] = BuildProcesses(snapshots),
                ["nodes"] = BuildNodes(nodes, statesByNode),
                ["events"] = BuildEvents(historyWindow.Events),
                ["insights"] = BuildInsights(nodes.Count, bindings, skills, candidates, conflicts,
                    activeProcesses, alarmProcesses, uncertainStates,
                    Workspace.Runtime.StatePerception?.IsRunning == true,
                    Workspace.Runtime.StatePerception?.LastObservationError)
            };
            PostMessage(payload);
        }

        private JArray BuildProcesses(IReadOnlyList<EngineSnapshot> snapshots)
        {
            var result = new JArray();
            foreach (EngineSnapshot snapshot in snapshots.Where(item => item != null)
                .OrderByDescending(item => !item.State.IsInactive())
                .ThenBy(item => item.ProcIndex))
            {
                Proc proc = snapshot.ProcIndex >= 0 && snapshot.ProcIndex < Workspace.ProcessDefinitions.Count
                    ? Workspace.ProcessDefinitions[snapshot.ProcIndex]
                    : null;
                string location = "尚未运行";
                string operationType = string.Empty;
                if (proc != null && snapshot.StepIndex >= 0 && snapshot.StepIndex < proc.steps.Count)
                {
                    Step step = proc.steps[snapshot.StepIndex];
                    location = step?.Name ?? "步骤 " + snapshot.StepIndex;
                    if (step != null && snapshot.OpIndex >= 0 && snapshot.OpIndex < step.Ops.Count)
                    {
                        OperationType operation = step.Ops[snapshot.OpIndex];
                        operationType = operation?.OperaType ?? string.Empty;
                        location += " / " + (operation?.Name ?? operationType ?? "指令 " + snapshot.OpIndex);
                    }
                }
                result.Add(new JObject
                {
                    ["procId"] = snapshot.ProcId == Guid.Empty ? string.Empty : snapshot.ProcId.ToString("D"),
                    ["name"] = snapshot.ProcName ?? proc?.head?.Name ?? "未命名流程",
                    ["state"] = snapshot.State.ToString(),
                    ["stateTone"] = snapshot.IsAlarm ? "danger"
                        : snapshot.State == ProcRunState.Paused ? "warning"
                        : snapshot.State.IsInactive() ? "idle" : "running",
                    ["location"] = location,
                    ["operationType"] = operationType,
                    ["alarm"] = snapshot.AlarmMessage ?? string.Empty,
                    ["runId"] = snapshot.RunId == Guid.Empty ? string.Empty : snapshot.RunId.ToString("D")
                });
            }
            return result;
        }

        private static JArray BuildNodes(
            IEnumerable<EquipmentTopologyNode> nodes,
            IReadOnlyDictionary<string, EquipmentNodeStateProjection> statesByNode)
        {
            var result = new JArray();
            foreach (EquipmentTopologyNode node in nodes.Where(item => item != null)
                .OrderBy(item => string.Equals(item.ReviewState, "confirmed", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .Take(40))
            {
                statesByNode.TryGetValue(node.Id ?? string.Empty, out EquipmentNodeStateProjection state);
                result.Add(new JObject
                {
                    ["id"] = node.Id ?? string.Empty,
                    ["label"] = node.Label ?? "未命名节点",
                    ["kind"] = node.Kind ?? "mechanism",
                    ["zone"] = node.Zone ?? string.Empty,
                    ["reviewState"] = node.ReviewState ?? "candidate",
                    ["state"] = state?.StateName ?? "尚无状态",
                    ["meaning"] = state?.Meaning ?? "尚未收到已绑定状态事实",
                    ["quality"] = state?.Quality ?? EquipmentStateQualities.Unknown,
                    ["confidence"] = state?.Confidence ?? 0d,
                    ["updatedAtUtc"] = state == null ? string.Empty : state.UpdatedAtUtc.ToString("O")
                });
            }
            return result;
        }

        private static JArray BuildEvents(IEnumerable<EquipmentStateHistoryEvent> events)
        {
            var result = new JArray();
            foreach (EquipmentStateHistoryEvent item in (events ?? Enumerable.Empty<EquipmentStateHistoryEvent>())
                .Where(value => value != null)
                .OrderByDescending(value => value.Sequence)
                .Take(120))
            {
                result.Add(new JObject
                {
                    ["sequence"] = item.Sequence,
                    ["timeUtc"] = item.ObservedAtUtc.ToString("O"),
                    ["eventType"] = item.EventType ?? string.Empty,
                    ["nodeId"] = item.NodeId ?? string.Empty,
                    ["nodeLabel"] = item.NodeLabel ?? string.Empty,
                    ["oldValue"] = item.OldValue ?? string.Empty,
                    ["newValue"] = item.NewValue ?? string.Empty,
                    ["meaning"] = item.Meaning ?? string.Empty,
                    ["quality"] = item.Quality ?? EquipmentStateQualities.Unknown,
                    ["sourceKind"] = item.SourceKind ?? string.Empty,
                    ["resourceRef"] = item.ResourceRef ?? string.Empty,
                    ["processId"] = item.ProcessId ?? string.Empty,
                    ["processName"] = item.ProcessName ?? string.Empty,
                    ["stepIndex"] = item.StepIndex,
                    ["operationId"] = item.OperationId ?? string.Empty,
                    ["operationIndex"] = item.OperationIndex,
                    ["operationType"] = item.OperationType ?? string.Empty,
                    ["operationName"] = item.OperationName ?? string.Empty,
                    ["processState"] = item.ProcessState ?? string.Empty,
                    ["outcome"] = item.Outcome ?? string.Empty,
                    ["terminationReason"] = item.TerminationReason ?? string.Empty,
                    ["skillId"] = item.SkillId ?? string.Empty,
                    ["actionId"] = item.ActionId ?? string.Empty,
                    ["expectedOutcome"] = item.ExpectedOutcome ?? string.Empty
                });
            }
            return result;
        }

        private static JArray BuildInsights(
            int nodeCount,
            int bindingCount,
            int skillCount,
            int candidates,
            int conflicts,
            int activeProcesses,
            int alarmProcesses,
            int uncertainStates,
            bool perceptionRunning,
            string perceptionError)
        {
            var result = new JArray();
            if (nodeCount == 0)
            {
                result.Add(Insight("拓扑模型尚未建立", "进入“拓扑与状态”，先从现有流程按类型和参数生成候选结构。", "warning"));
            }
            else if (bindingCount == 0)
            {
                result.Add(Insight("节点尚无状态绑定", "拓扑已有结构，但 Machine Agent 还不能用真实信号判断节点状态。", "warning"));
            }
            if (nodeCount > 0 && skillCount == 0)
            {
                result.Add(Insight("尚未建立节点技能", "在拓扑节点中绑定工程师已编写的真实流程指令，Machine Agent 才能形成对象级动作预演。", "info"));
            }
            if (candidates > 0)
            {
                result.Add(Insight("存在 " + candidates + " 个待确认对象", "候选拓扑不会自动升级为设备事实，需要工程师确认。", "info"));
            }
            if (conflicts > 0)
            {
                result.Add(Insight("存在 " + conflicts + " 个拓扑冲突", "冲突证据会阻止后续设备控制能力建立。", "danger"));
            }
            if (!perceptionRunning)
            {
                result.Add(Insight("状态感知未运行", string.IsNullOrWhiteSpace(perceptionError)
                    ? "当前只能查看持久化的历史事实。"
                    : perceptionError, "danger"));
            }
            if (alarmProcesses > 0)
            {
                result.Add(Insight(alarmProcesses + " 个流程处于报警", "请先核对报警流程和最近状态事件。", "danger"));
            }
            else if (activeProcesses > 0)
            {
                result.Add(Insight(activeProcesses + " 个流程正在运行", "运行总览正在读取现有 ProcessEngine 快照。", "success"));
            }
            if (uncertainStates > 0)
            {
                result.Add(Insight(uncertainStates + " 个节点状态证据不足", "未知或现场读取中断的状态不能作为后续控制依据。", "warning"));
            }
            if (result.Count == 0)
            {
                result.Add(Insight("设备观察基础正常", "拓扑、状态感知和运行快照均已连接。", "success"));
            }
            return result;
        }

        private static JObject Insight(string title, string detail, string tone)
        {
            return new JObject
            {
                ["title"] = title,
                ["detail"] = detail,
                ["tone"] = tone
            };
        }

        private void UpdatePerceptionStatus()
        {
            EquipmentStatePerceptionService perception = Workspace.Runtime.StatePerception;
            bool running = perception?.IsRunning == true;
            bool degraded = running && !string.IsNullOrWhiteSpace(perception.LastObservationError);
            perceptionStatusLabel.Text = !running
                ? "●  状态感知 OFFLINE"
                : degraded ? "●  状态感知 DEGRADED" : "●  状态感知 ONLINE";
            perceptionStatusLabel.ForeColor = !running
                ? UiPalette.Danger
                : degraded ? UiPalette.Warning : UiPalette.Success;
        }

        private void PostMessage(JObject message)
        {
            if (!dashboardReady || dashboardCore == null)
            {
                return;
            }
            try
            {
                dashboardCore.PostWebMessageAsJson(message.ToString(Formatting.None));
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.Runtime.InteropServices.InvalidComObjectException)
            {
            }
        }

        private static string ReadPageHtml()
        {
            Assembly assembly = typeof(FrmMachineAgent).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(PageResourceName)
                ?? throw new InvalidOperationException("Machine Agent 页面资源缺失。"))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        private void DisableDashboard(string message)
        {
            refreshTimer.Stop();
            dashboardWebView.Visible = false;
            fallbackMessage.Text = message;
            fallbackPanel.Visible = true;
            fallbackPanel.BringToFront();
            Workspace.Info?.PrintInfo(message, FrmInfo.Level.Error);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
                if (dashboardCore != null)
                {
                    dashboardCore.WebMessageReceived -= Dashboard_WebMessageReceived;
                }
                dashboardWebView.Dispose();
                DisposeAgentResources();
            }
            base.Dispose(disposing);
        }
    }
}
