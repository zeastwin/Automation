using Automation.Protocol;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DrawingColor = Microsoft.Msagl.Drawing.Color;
using DrawingGraph = Microsoft.Msagl.Drawing.Graph;
using DrawingNode = Microsoft.Msagl.Drawing.Node;
using WinColor = System.Drawing.Color;
using WinLabel = System.Windows.Forms.Label;

namespace Automation
{
    /// <summary>
    /// 流程图只投影统一图快照，不在界面层推断控制流。
    /// </summary>
    public sealed class FrmProcessFlow : Form
    {
        private readonly FrmMain owner;
        private readonly GViewer viewer;
        private readonly WinLabel breadcrumb;
        private readonly WinLabel status;
        private readonly TextBox searchBox;
        private readonly CheckBox showDisabled;
        private readonly CheckBox showAlarm;
        private readonly TextBox details;
        private readonly Button projectButton;
        private ProcessFlowGraphSnapshot snapshot;
        private int? currentProcIndex;
        private string selectedNodeId;
        private int buildGeneration;
        private CancellationTokenSource buildCancellation;
        private bool graphAvailable = true;

        public FrmProcessFlow(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            BackColor = WinColor.FromArgb(246, 249, 251);

            var commandBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 46,
                Padding = new Padding(8, 7, 8, 5),
                WrapContents = false,
                BackColor = WinColor.White
            };
            Button editorButton = CreateButton("返回指令列表", (sender, args) => owner.ShowEditorWorkspace());
            projectButton = CreateButton("项目总览", (sender, args) => ShowProjectGraph());
            Button fitButton = CreateButton("适应窗口", (sender, args) => FitGraph());
            Button zoomInButton = CreateButton("放大", (sender, args) => viewer.ZoomF *= 1.2);
            Button zoomOutButton = CreateButton("缩小", (sender, args) => viewer.ZoomF /= 1.2);
            Button aiButton = CreateButton("AI 解读当前视图", (sender, args) => PrepareAiPrompt());
            searchBox = new TextBox { Width = 190, Margin = new Padding(12, 4, 3, 3) };
            Button searchButton = CreateButton("搜索", (sender, args) => SearchNode());
            showDisabled = new CheckBox { Text = "显示禁用项", AutoSize = true, Margin = new Padding(12, 7, 3, 3) };
            showAlarm = new CheckBox { Text = "显示报警分支", AutoSize = true, Margin = new Padding(8, 7, 3, 3) };
            showDisabled.CheckedChanged += (sender, args) => RenderGraph();
            showAlarm.CheckedChanged += (sender, args) => RenderGraph();
            commandBar.Controls.AddRange(new Control[]
            {
                editorButton, projectButton, fitButton, zoomInButton, zoomOutButton,
                searchBox, searchButton, showDisabled, showAlarm, aiButton
            });

            var heading = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = WinColor.FromArgb(246, 249, 251) };
            breadcrumb = new WinLabel
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(12, 7, 8, 0),
                Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold),
                ForeColor = WinColor.FromArgb(42, 61, 73)
            };
            status = new WinLabel
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 1, 8, 0),
                ForeColor = WinColor.FromArgb(91, 106, 116)
            };
            heading.Controls.Add(status);
            heading.Controls.Add(breadcrumb);

            viewer = new GViewer
            {
                Dock = DockStyle.Fill,
                AsyncLayout = true,
                BackColor = WinColor.White,
                NavigationVisible = false,
                ToolBarIsVisible = false
            };
            viewer.MouseClick += Viewer_MouseClick;
            viewer.MouseDoubleClick += Viewer_MouseDoubleClick;

            details = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = WinColor.FromArgb(250, 252, 253),
                Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F),
                Text = "选择节点可查看稳定身份与诊断信息。"
            };
            var detailsPanel = new Panel { Dock = DockStyle.Right, Width = 285, Padding = new Padding(12), BackColor = details.BackColor };
            detailsPanel.Controls.Add(details);
            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = BackColor };
            content.Controls.Add(viewer);
            content.Controls.Add(detailsPanel);
            Controls.Add(content);
            Controls.Add(heading);
            Controls.Add(commandBar);

            owner.dataRun.SnapshotChanged += RuntimeSnapshotChanged;
            FormClosed += (sender, args) =>
            {
                owner.dataRun.SnapshotChanged -= RuntimeSnapshotChanged;
                buildCancellation?.Cancel();
                buildCancellation?.Dispose();
            };
        }

        public void ShowProjectGraph()
        {
            currentProcIndex = null;
            projectButton.Enabled = false;
            RefreshCurrentGraph();
        }

        public void ShowProcessGraph(int procIndex)
        {
            currentProcIndex = procIndex;
            projectButton.Enabled = true;
            RefreshCurrentGraph();
        }

        public async void RefreshCurrentGraph()
        {
            if (!graphAvailable || IsDisposed)
            {
                return;
            }
            int generation = Interlocked.Increment(ref buildGeneration);
            buildCancellation?.Cancel();
            buildCancellation?.Dispose();
            buildCancellation = new CancellationTokenSource();
            CancellationToken token = buildCancellation.Token;
            List<Proc> processes = owner.frmProc.procsList.Select(ObjectGraphCloner.Clone).ToList();
            int? procIndex = currentProcIndex;
            var runtime = owner.dataRun.GetSnapshots().ToDictionary(item => item.ProcIndex);
            breadcrumb.Text = procIndex.HasValue ? "项目总览  /  正在生成流程明细…" : "正在生成项目总览…";
            status.Text = "正在根据当前配置生成确定性控制流图…";
            try
            {
                ProcessFlowGraphSnapshot built = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    Func<int, EngineSnapshot> provider = index => runtime.TryGetValue(index, out EngineSnapshot item) ? item : null;
                    return procIndex.HasValue
                        ? ProcessFlowGraphService.BuildProcess(processes, procIndex.Value, provider)
                        : ProcessFlowGraphService.BuildProject(processes, provider);
                }, token);
                if (token.IsCancellationRequested || generation != buildGeneration || IsDisposed)
                {
                    return;
                }
                snapshot = built;
                selectedNodeId = null;
                RenderGraph();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                DisableGraph("流程图生成失败：" + ex.Message);
            }
        }

        private void RenderGraph()
        {
            if (snapshot == null || IsDisposed)
            {
                return;
            }
            try
            {
                HashSet<string> diagnosticNodes = new HashSet<string>(snapshot.Diagnostics
                    .Where(item => !string.IsNullOrWhiteSpace(item.NodeId)
                        && !string.Equals(item.Severity, "info", StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.NodeId), StringComparer.Ordinal);
                List<FlowGraphNode> visibleNodes = snapshot.Nodes.Where(node =>
                    (!node.Disabled || showDisabled.Checked || node.Invalid || diagnosticNodes.Contains(node.Id))).ToList();
                HashSet<string> visibleIds = new HashSet<string>(visibleNodes.Select(node => node.Id), StringComparer.Ordinal);
                List<FlowGraphEdge> visibleEdges = snapshot.Edges.Where(edge =>
                    visibleIds.Contains(edge.SourceId)
                    && visibleIds.Contains(edge.TargetId)
                    && (!edge.Disabled || showDisabled.Checked || edge.Invalid)
                    && (!edge.AlarmPath || showAlarm.Checked || edge.Invalid)).ToList();

                var graph = new DrawingGraph("process-flow");
                graph.Attr.LayerDirection = snapshot.Scope == "project" ? LayerDirection.LR : LayerDirection.TB;
                graph.Attr.LayerSeparation = 45;
                graph.Attr.NodeSeparation = 24;
                var nodeMap = new Dictionary<string, DrawingNode>(StringComparer.Ordinal);
                foreach (FlowGraphNode item in visibleNodes)
                {
                    DrawingNode node = graph.AddNode(item.Id);
                    node.LabelText = BuildNodeLabel(item);
                    node.UserData = item;
                    ApplyBaseNodeStyle(node, item);
                    nodeMap[item.Id] = node;
                }
                foreach (FlowGraphGroup item in snapshot.Groups)
                {
                    DrawingNode[] members = visibleNodes.Where(node => node.GroupId == item.Id)
                        .Select(node => nodeMap[node.Id]).ToArray();
                    if (members.Length == 0)
                    {
                        continue;
                    }
                    var subgraph = new Subgraph(item.Id) { LabelText = item.Label };
                    subgraph.Attr.Color = item.Disabled ? DrawingColor.LightGray : new DrawingColor(130, 153, 169);
                    foreach (DrawingNode member in members) subgraph.AddNode(member);
                    graph.RootSubgraph.AddSubgraph(subgraph);
                }
                foreach (FlowGraphEdge item in visibleEdges)
                {
                    Edge edge = graph.AddEdge(item.SourceId, item.Label ?? string.Empty, item.TargetId);
                    edge.UserData = item;
                    ApplyEdgeStyle(edge, item);
                }
                viewer.Graph = graph;
                UpdateHeading(visibleNodes.Count, visibleEdges.Count);
                ApplyRuntimeHighlight(owner.dataRun.GetSnapshots());
            }
            catch (Exception ex)
            {
                DisableGraph("流程图布局初始化失败：" + ex.Message);
            }
        }

        private void UpdateHeading(int visibleNodeCount, int visibleEdgeCount)
        {
            breadcrumb.Text = snapshot.Scope == "project"
                ? "项目总览"
                : $"项目总览  /  {snapshot.Name}（流程 {snapshot.ProcIndex}）";
            HashSet<string> diagnosticNodes = new HashSet<string>(snapshot.Diagnostics
                .Where(item => !string.IsNullOrWhiteSpace(item.NodeId)
                    && !string.Equals(item.Severity, "info", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.NodeId), StringComparer.Ordinal);
            int hiddenDisabled = showDisabled.Checked ? 0 : snapshot.Nodes.Count(node =>
                node.Disabled && !node.Invalid && !diagnosticNodes.Contains(node.Id))
                + snapshot.Edges.Count(edge => edge.Disabled && !edge.Invalid);
            int hiddenAlarm = showAlarm.Checked ? 0 : snapshot.Edges.Count(edge => edge.AlarmPath && !edge.Invalid);
            string revision = snapshot.Scope == "process" && !snapshot.RuntimeRevisionMatches
                ? "；当前实例运行旧版本，未映射运行指令"
                : string.Empty;
            status.Text = $"显示 {visibleNodeCount} 个节点、{visibleEdgeCount} 条连线；隐藏禁用项 {hiddenDisabled}，隐藏报警分支 {hiddenAlarm}；诊断 {snapshot.Diagnostics.Count}{revision}";
            status.ForeColor = revision.Length > 0 || snapshot.Diagnostics.Any(item => item.Severity == "error")
                ? WinColor.FromArgb(177, 65, 54)
                : WinColor.FromArgb(91, 106, 116);
        }

        private static void ApplyBaseNodeStyle(DrawingNode node, FlowGraphNode item)
        {
            node.Attr.Color = new DrawingColor(79, 104, 119);
            node.Attr.FillColor = new DrawingColor(241, 247, 250);
            node.Attr.Shape = Shape.Box;
            node.Attr.XRadius = 5;
            node.Attr.YRadius = 5;
            if (item.Kind == "start")
            {
                node.Attr.Shape = Shape.Circle;
                node.Attr.FillColor = new DrawingColor(220, 245, 229);
            }
            else if (item.Kind == "end")
            {
                node.Attr.Shape = Shape.DoubleCircle;
                node.Attr.FillColor = new DrawingColor(231, 237, 241);
            }
            else if (item.Kind == "process")
            {
                node.Attr.Shape = Shape.Box;
                node.Attr.FillColor = item.AutoStart ? new DrawingColor(223, 241, 255) : new DrawingColor(241, 247, 250);
            }
            if (!item.Reachable && !item.Disabled)
            {
                node.Attr.FillColor = new DrawingColor(255, 245, 214);
            }
            if (item.Disabled)
            {
                node.Attr.Color = DrawingColor.Gray;
                node.Attr.FillColor = new DrawingColor(237, 237, 237);
                node.Attr.AddStyle(Style.Dashed);
            }
            if (item.Dynamic)
            {
                node.Attr.FillColor = new DrawingColor(245, 235, 255);
                node.Attr.AddStyle(Style.Dashed);
            }
            if (item.Invalid)
            {
                node.Attr.Color = DrawingColor.Red;
                node.Attr.FillColor = new DrawingColor(255, 230, 228);
                node.Attr.LineWidth = 2;
            }
        }

        private static string BuildNodeLabel(FlowGraphNode item)
        {
            if (item.Kind == "process")
            {
                string state = string.Equals(item.RuntimeState, ProcRunState.Stopped.ToString(), StringComparison.Ordinal)
                    ? string.Empty
                    : " · " + item.RuntimeState;
                return (item.Label ?? item.Id) + (item.AutoStart ? "\n自启动" : string.Empty) + state;
            }
            if (item.Kind == "operation" && !string.IsNullOrWhiteSpace(item.OperaType)
                && !string.Equals(item.Label, item.OperaType, StringComparison.Ordinal))
            {
                return (item.Label ?? item.Id) + "\n[" + item.OperaType + "]";
            }
            if ((item.Dynamic || item.Invalid) && !string.IsNullOrWhiteSpace(item.Summary))
            {
                return (item.Label ?? item.Id) + "\n" + item.Summary;
            }
            return item.Label ?? item.Id;
        }

        private static void ApplyEdgeStyle(Edge edge, FlowGraphEdge item)
        {
            edge.Attr.Color = new DrawingColor(105, 126, 139);
            if (item.Kind == "processStart") edge.Attr.Color = new DrawingColor(45, 139, 83);
            if (item.Kind == "processStop") edge.Attr.Color = new DrawingColor(185, 62, 52);
            if (item.Kind == "processWait") edge.Attr.Color = new DrawingColor(191, 126, 35);
            if (item.Kind == "branch") edge.Attr.Color = new DrawingColor(43, 111, 170);
            if (item.AlarmPath)
            {
                edge.Attr.Color = new DrawingColor(193, 79, 55);
                edge.Attr.AddStyle(Style.Dashed);
            }
            if (item.Dynamic) edge.Attr.AddStyle(Style.Dashed);
            if (item.Disabled)
            {
                edge.Attr.Color = DrawingColor.Gray;
                edge.Attr.AddStyle(Style.Dashed);
            }
            if (item.Loop) edge.Attr.LineWidth = 2;
            if (item.Invalid)
            {
                edge.Attr.Color = DrawingColor.Red;
                edge.Attr.LineWidth = 2;
            }
        }

        private void RuntimeSnapshotChanged(EngineSnapshot runtime)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)(() => ApplyRuntimeHighlight(owner.dataRun.GetSnapshots())));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ApplyRuntimeHighlight(IReadOnlyList<EngineSnapshot> runtimes)
        {
            if (viewer.Graph == null || snapshot == null)
            {
                return;
            }
            foreach (FlowGraphNode item in snapshot.Nodes)
            {
                DrawingNode drawingNode = viewer.Graph.FindNode(item.Id);
                if (drawingNode != null) ApplyBaseNodeStyle(drawingNode, item);
            }
            if (snapshot.Scope == "project")
            {
                foreach (EngineSnapshot runtime in runtimes.Where(item => item.State != ProcRunState.Stopped))
                {
                    FlowGraphNode item = snapshot.Nodes.FirstOrDefault(node => node.Kind == "process" && node.ProcId == runtime.ProcId.ToString("D"));
                    HighlightRuntimeNode(item, runtime);
                }
            }
            else
            {
                EngineSnapshot runtime = runtimes.FirstOrDefault(item => item.ProcIndex == snapshot.ProcIndex);
                bool runtimeMatches = runtime == null
                    || runtime.ProcId.ToString("D") == snapshot.ProcId
                    && runtime.PublishedRevision == runtime.AppliedRevision;
                if (runtime != null && runtimeMatches && runtime.State != ProcRunState.Stopped)
                {
                    FlowGraphNode item = snapshot.Nodes.FirstOrDefault(node => node.StepIndex == runtime.StepIndex && node.OpIndex == runtime.OpIndex);
                    HighlightRuntimeNode(item, runtime);
                }
                snapshot.PublishedRevision = runtime?.PublishedRevision ?? snapshot.PublishedRevision;
                snapshot.AppliedRevision = runtime?.AppliedRevision ?? snapshot.AppliedRevision;
                snapshot.RuntimeRevisionMatches = runtimeMatches;
                UpdateHeading(viewer.Graph.NodeCount, viewer.Graph.EdgeCount);
            }
            viewer.Invalidate();
        }

        private void HighlightRuntimeNode(FlowGraphNode item, EngineSnapshot runtime)
        {
            DrawingNode node = item == null ? null : viewer.Graph.FindNode(item.Id);
            if (node == null)
            {
                return;
            }
            node.Attr.LineWidth = 3;
            node.Attr.Color = runtime.IsAlarm ? DrawingColor.Red : new DrawingColor(31, 126, 174);
            node.Attr.FillColor = runtime.IsAlarm
                ? new DrawingColor(255, 218, 214)
                : runtime.State == ProcRunState.Paused
                    ? new DrawingColor(255, 240, 196)
                    : new DrawingColor(204, 235, 255);
        }

        private void Viewer_MouseClick(object sender, MouseEventArgs e)
        {
            if (viewer.ObjectUnderMouseCursor?.DrawingObject?.UserData is FlowGraphNode node)
            {
                selectedNodeId = node.Id;
                ShowNodeDetails(node);
            }
        }

        private void Viewer_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (!(viewer.ObjectUnderMouseCursor?.DrawingObject?.UserData is FlowGraphNode node))
            {
                return;
            }
            if (node.Kind == "process" && node.ProcIndex.HasValue)
            {
                ShowProcessGraph(node.ProcIndex.Value);
                return;
            }
            if (node.Kind == "operation"
                && Guid.TryParse(node.ProcId, out Guid procId)
                && Guid.TryParse(node.StepId, out Guid stepId)
                && Guid.TryParse(node.OpId, out Guid opId)
                && !owner.NavigateToFlowOperation(procId, stepId, opId))
            {
                MessageBox.Show(this, "当前配置中已无法解析该指令，流程图将刷新。", "流程图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshCurrentGraph();
            }
        }

        private void ShowNodeDetails(FlowGraphNode node)
        {
            IEnumerable<FlowGraphDiagnostic> nodeDiagnostics = snapshot.Diagnostics.Where(item => item.NodeId == node.Id);
            details.Text = $"{node.Label}\r\n\r\n类型：{node.Kind}\r\n说明：{node.Summary}\r\n流程：{node.ProcId}\r\n步骤：{node.StepId}\r\n指令：{node.OpId}\r\n原生类型：{node.OperaType}\r\n运行状态：{node.RuntimeState}\r\n就绪状态：{node.ReadinessStatus}\r\n自启动：{node.AutoStart}\r\n禁用：{node.Disabled}\r\n可达：{node.Reachable}\r\n\r\n诊断：\r\n"
                + string.Join("\r\n", nodeDiagnostics.Select(item => $"[{item.Severity}] {item.Message}"));
        }

        private void SearchNode()
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(searchBox.Text))
            {
                return;
            }
            string term = searchBox.Text.Trim();
            FlowGraphNode match = snapshot.Nodes.FirstOrDefault(node =>
                (node.Label?.IndexOf(term, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (node.Summary?.IndexOf(term, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (node.OperaType?.IndexOf(term, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            if (match == null)
            {
                status.Text = "未找到匹配节点：" + term;
                return;
            }
            DrawingNode drawingNode = viewer.Graph?.FindNode(match.Id);
            if (drawingNode == null)
            {
                status.Text = "匹配节点当前被过滤，请先开启对应显示选项。";
                return;
            }
            selectedNodeId = match.Id;
            ShowNodeDetails(match);
            object entity = viewer.Entities.FirstOrDefault(item => item.DrawingObject == drawingNode);
            if (entity != null) viewer.CenterToGroup(new[] { entity });
        }

        private void FitGraph()
        {
            if (viewer.Graph?.GeometryGraph != null)
            {
                viewer.ShowBBox(viewer.Graph.GeometryGraph.BoundingBox);
            }
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

        private void DisableGraph(string message)
        {
            graphAvailable = false;
            status.Text = message;
            status.ForeColor = WinColor.FromArgb(177, 65, 54);
            viewer.Enabled = false;
            owner.dataRun?.Logger?.Log(message, LogLevel.Error);
            if (owner.frmInfo != null && !owner.frmInfo.IsDisposed)
            {
                owner.frmInfo.PrintInfo(message, FrmInfo.Level.Error);
            }
        }

        private static Button CreateButton(string text, EventHandler handler)
        {
            var button = new Button
            {
                AutoSize = true,
                Height = 30,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = WinColor.White,
                ForeColor = WinColor.FromArgb(48, 67, 78),
                Margin = new Padding(3, 1, 3, 3)
            };
            button.FlatAppearance.BorderColor = WinColor.FromArgb(205, 216, 223);
            button.Click += handler;
            return button;
        }
    }
}
