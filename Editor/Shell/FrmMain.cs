// 模块：编辑器 / 外壳。
// 职责范围：页面装配、菜单、工具栏、导航、生命周期和程序设置。
// 导航提示：内核由宿主创建；本窗体只装配编辑页面。启动/关闭看 Lifecycle，页面切换看 Navigation。

using Automation.Bridge;
using Automation.MotionControl;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmMain : Form
    {
        public FrmDataGrid frmDataGrid = new FrmDataGrid();
        public FrmMenu frmMenu = new FrmMenu();
        public FrmProc frmProc;
        public FrmInspector frmInspector = new FrmInspector();
        public FrmToolBar frmToolBar = new FrmToolBar();
        public FrmValue frmValue = new FrmValue();
        public FrmValueDebug frmValueDebug = new FrmValueDebug();
        public FrmAiAssistant frmAiAssistant = new FrmAiAssistant();
        public Panel ai_panel;
        private WorkspacePageHost workspacePageHost;
        private Panel editorWorkspacePage;
        private readonly Stack<EditorNavigationLocation> editorBackHistory = new Stack<EditorNavigationLocation>();
        private readonly Stack<EditorNavigationLocation> editorForwardHistory = new Stack<EditorNavigationLocation>();
        private EditorNavigationLocation currentEditorLocation;
        private EditorNavigationMouseMessageFilter editorNavigationMouseMessageFilter;
        private bool editorNavigationChanging;
        private FrmProcessFlow frmProcessFlow;
        private FrmDataBreakpoints frmDataBreakpoints;
        private FrmRuntimeDiagnostics frmRuntimeDiagnostics;
        private FrmPerformanceAnalysis frmPerformanceAnalysis;
        private bool flowGraphUnavailable;
        public FrmIO frmIO;
        public FrmCard frmCard;
        public ProcessEngine dataRun;
        public CustomFunc customFunc;
        public FrmControl frmControl = new FrmControl();
        public FrmStation frmStation = new FrmStation();
        public FrmDataStruct frmdataStruct = new FrmDataStruct();
        public FrmIODebug frmIODebug = new FrmIODebug();
        public FrmCommunication frmCommunication = new FrmCommunication();
        public FrmState frmState = new FrmState();
        public FrmAlarmConfig frmAlarmConfig = new FrmAlarmConfig();
        public FrmSearch frmSearch = new FrmSearch();
        public FrmSearch4Value frmSearch4Value = new FrmSearch4Value();
        public FrmInfo frmInfo = new FrmInfo();
        public FrmPlc frmPlc = new FrmPlc();
        public IMotionRuntime motion;
        public IIoRuntime io;
        private readonly Dictionary<Guid, EngineSnapshot> snapshotCache = new Dictionary<Guid, EngineSnapshot>();
        private readonly HashSet<Guid> snapshotDirty = new HashSet<Guid>();
        private readonly object snapshotLock = new object();
        private System.Windows.Forms.Timer snapshotTimer;
        private readonly EditorWorkspace editorWorkspace;
        public PlatformRuntime Runtime { get; }
        private readonly int uiThreadId;
        private readonly Control uiDispatcher;
        private readonly UiWarmupCoordinator uiWarmupCoordinator;
        private readonly AutomationBridgeHost automationBridgeHost;
        private readonly ProcessTraceAuditSink processTraceAuditSink;
        private readonly DataBreakpointService dataBreakpointService;
        private RuntimeBlackBoxRecorder runtimeBlackBoxRecorder;
        private readonly AutomationMcpServerManager automationMcpServerManager = new AutomationMcpServerManager();
        private bool platformInitializationStarted;
        private bool platformInitialized;
        private bool allowFinalClose;
        private int aiInfrastructureStartState;
        private int shutdownStarted;
        private const int MinimumWorkspaceWidthWithAi = 1000;
        private const int MinimumAiPanelWidth = 320;
        private FormWindowState previousWindowState;
        private bool centerAfterRestorePending;
        private volatile bool runtimeDiagnosticsEnabled;

        internal bool HideOnUserClose { get; set; }
        internal bool IsPlatformInitialized => platformInitialized;
        internal bool RuntimeDiagnosticsEnabled => runtimeDiagnosticsEnabled;

        private sealed class EditorNavigationLocation
        {
            public Guid ProcId { get; set; }
            public Guid StepId { get; set; }
            public Guid OpId { get; set; }

            public bool SameAs(EditorNavigationLocation other)
            {
                return other != null
                    && ProcId == other.ProcId
                    && StepId == other.StepId
                    && OpId == other.OpId;
            }
        }

        private sealed class EditorNavigationMouseMessageFilter : IMessageFilter
        {
            private const int WmKeyDown = 0x0100;
            private const int WmLButtonDown = 0x0201;
            private const int WmRButtonDown = 0x0204;
            private const int WmXButtonDown = 0x020B;
            private const int WmXButtonUp = 0x020C;
            private const int XButton1 = 1;
            private const int XButton2 = 2;
            private readonly FrmMain owner;
            private int handledButton;

            public EditorNavigationMouseMessageFilter(FrmMain owner)
            {
                this.owner = owner;
            }

            public bool PreFilterMessage(ref System.Windows.Forms.Message message)
            {
                if (message.Msg == WmKeyDown
                    || message.Msg == WmLButtonDown
                    || message.Msg == WmRButtonDown
                    || message.Msg == WmXButtonDown)
                {
                    owner.NotifyEditorInteraction();
                }
                if (message.Msg == WmXButtonDown)
                {
                    int button = (int)((message.WParam.ToInt64() >> 16) & 0xffff);
                    bool handled = button == XButton1
                        ? owner.NavigateEditorBack()
                        : button == XButton2 && owner.NavigateEditorForward();
                    if (handled)
                    {
                        handledButton = button;
                        return true;
                    }
                }
                else if (message.Msg == WmXButtonUp)
                {
                    int button = (int)((message.WParam.ToInt64() >> 16) & 0xffff);
                    if (handledButton == button)
                    {
                        handledButton = 0;
                        return true;
                    }
                }
                return false;
            }
        }

        public FrmMain()
            : this(new PlatformRuntime())
        {
        }

        internal FrmMain(PlatformRuntime runtime)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            ProcessDefinitionRepository processDefinitionRepository = Runtime.Stores.Processes;
            IoConfigurationStore ioConfigurationStore = Runtime.Stores.IoConfiguration;
            StationDefinitionStore stationDefinitionStore = Runtime.Stores.Stations;
            frmProc = new FrmProc(processDefinitionRepository);
            frmIO = new FrmIO(ioConfigurationStore);
            frmCard = new FrmCard(stationDefinitionStore);
            editorWorkspace = new EditorWorkspace(this, Runtime);
            customFunc = Runtime.CustomFunctions;
            uiThreadId = Thread.CurrentThread.ManagedThreadId;
            UiBranding.Apply(this);
            InitializeComponent();
            uiWarmupCoordinator = new UiWarmupCoordinator(this);
            InitializeWorkspacePageHost();
            uiDispatcher = new Control();
            _ = uiDispatcher.Handle;
            Runtime.EditorUi = new WinFormsPlatformEditorUiAdapter(this);
            ILogger uiLogger = new FrmInfoLogger(frmInfo);
            WinFormsProcessInteractionCoordinator interaction = Runtime.ProcessInteraction;
            if (interaction == null)
            {
                interaction = new WinFormsProcessInteractionCoordinator(Runtime);
                Runtime.ProcessInteraction = interaction;
            }
            if (Runtime.ProcessEngine == null)
            {
                ILogger fileLogger = new LocalFileLogger(@"D:\AutomationLogs\ProcessLog");
                PlatformRuntimeComposition composition = PlatformRuntimeComposer.Compose(
                    Runtime,
                    interaction,
                    interaction,
                    new CompositeLogger(uiLogger, fileLogger));
                dataRun = composition.ProcessEngine;
                motion = composition.Motion;
                io = composition.Io;
                interaction.AttachEngine(dataRun);
            }
            else
            {
                dataRun = Runtime.ProcessEngine;
                motion = Runtime.Motion;
                io = Runtime.Io;
                dataRun.Logger = new CompositeLogger(uiLogger, dataRun.Logger);
            }
            Runtime.Devices.Faulted += HandleDeviceFault;
            dataRun.SnapshotChanged += CacheSnapshot;
            dataBreakpointService = new DataBreakpointService(Runtime.Stores.Values, dataRun);
            dataBreakpointService.BreakpointHit += HandleDataBreakpointHit;
            processTraceAuditSink = new ProcessTraceAuditSink(dataRun);
            Runtime.PlcRuntime.RuntimeEvent += HandlePlcRuntimeEvent;
            Runtime.Communication.FramesDropped += HandleCommunicationFramesDropped;
            Runtime.ManualMotion.CommandRejected += HandleManualMotionRejected;
            frmInspector.AttachEditActions(frmToolBar.btnSave, frmToolBar.btnCancel);
            automationBridgeHost = new AutomationBridgeHost(this);

            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Normal;
            Rectangle startupWorkingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(
                Math.Max(1, Math.Min(Width, startupWorkingArea.Width * 9 / 10)),
                Math.Max(1, Math.Min(Height, startupWorkingArea.Height * 9 / 10)));
            previousWindowState = WindowState;
            Resize += FrmMain_Resize;

            loadFillForm(MenuPanel, frmMenu);
            bool diagnosticsEnabled = AppConfigStorage.TryGetCached(out AppConfig appConfig, out _)
                && appConfig.EnableRuntimeDiagnostics;
            ApplyRuntimeDiagnosticsConfiguration(diagnosticsEnabled);
            loadFillForm(treeView_panel, frmProc);
            loadFillForm(DataGrid_panel, frmDataGrid);
            loadFillForm(inspector_panel, frmInspector);
            loadFillForm(ToolBar_panel, frmToolBar);
            loadFillForm(state_panel, frmState);
            loadFillForm(panel_Info, frmInfo);
            frmProc.proc_treeView.AfterSelect += EditorTreeSelectionChanged;
            frmDataGrid.dataGridView1.MouseUp += EditorOperationListMouseUp;
            frmDataGrid.dataGridView1.KeyUp += EditorOperationListKeyUp;
            editorNavigationMouseMessageFilter = new EditorNavigationMouseMessageFilter(this);
            Application.AddMessageFilter(editorNavigationMouseMessageFilter);
            Disposed += (sender, args) =>
            {
                if (editorNavigationMouseMessageFilter != null)
                {
                    Application.RemoveMessageFilter(editorNavigationMouseMessageFilter);
                    editorNavigationMouseMessageFilter = null;
                }
                uiWarmupCoordinator.Dispose();
            };

            // AI 助手面板挂到主窗体第一层，右侧全高停靠。
            // 这样 MenuPanel/state_panel/main_panel 都会让出右侧区域，AI 页面不再被顶部菜单和底部状态栏夹住。
            ai_panel = new Panel { Dock = DockStyle.Right, Width = 0, Visible = false, BackColor = UiPalette.SurfaceStrong };
            Controls.Add(ai_panel);
            Controls.SetChildIndex(ai_panel, Controls.Count - 1);
            main_panel.AutoScroll = true;
            ClientSizeChanged += FrmMain_ClientSizeChanged;

            frmAiAssistant.TopLevel = false;
            frmAiAssistant.FormBorderStyle = FormBorderStyle.None;
            frmAiAssistant.Dock = DockStyle.Fill;
            ai_panel.Controls.Add(frmAiAssistant);

            StartSnapshotTimer();
            Text = "Automation - 平台";
            Shown += FrmMain_Shown;
            Activated += FrmMain_Activated;
        }

        private void HandleManualMotionRejected(object sender, ManualMotionRejectedEventArgs e)
        {
            Action showMessage = () =>
            {
                if (!IsDisposed)
                {
                    MessageBox.Show(this, e.Message, e.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            if (InvokeRequired)
            {
                BeginInvoke(showMessage);
                return;
            }
            showMessage();
        }

        private void FrmMain_ClientSizeChanged(object sender, EventArgs e)
        {
            if (ai_panel != null && ai_panel.Visible)
            {
                UpdateAiPanelWidth();
            }
        }

        private void FrmMain_Resize(object sender, EventArgs e)
        {
            FormWindowState currentWindowState = WindowState;
            bool restoredFromMaximized = previousWindowState == FormWindowState.Maximized
                && currentWindowState == FormWindowState.Normal;
            previousWindowState = currentWindowState;
            if (!restoredFromMaximized || centerAfterRestorePending || IsDisposed)
            {
                return;
            }

            centerAfterRestorePending = true;
            BeginInvoke((Action)(() =>
            {
                centerAfterRestorePending = false;
                if (IsDisposed || WindowState != FormWindowState.Normal)
                {
                    return;
                }
                Rectangle workingArea = Screen.FromControl(this).WorkingArea;
                int centeredX = workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2);
                int centeredY = workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2);
                Location = new Point(centeredX, centeredY);
            }));
        }

        internal void UpdateAiPanelWidth()
        {
            if (ai_panel == null)
            {
                return;
            }

            int minimumAiWidth = Math.Min(MinimumAiPanelWidth, Math.Max(0, ClientSize.Width / 2));
            int preferredWidth = ClientSize.Width * 2 / 5;
            int maximumWidth = Math.Max(minimumAiWidth, ClientSize.Width - MinimumWorkspaceWidthWithAi);
            ai_panel.Width = Math.Min(Math.Max(minimumAiWidth, preferredWidth), maximumWidth);
        }

        public AutomationMcpServerManager McpServerManager => automationMcpServerManager;

        internal RuntimeBlackBoxRecorder RuntimeBlackBoxRecorder => runtimeBlackBoxRecorder;

        public void ShowRuntimeDiagnostics()
        {
            if (!RuntimeDiagnosticsEnabled)
            {
                throw new InvalidOperationException("智能诊断中心已在程序设置中停用。");
            }
            if (frmRuntimeDiagnostics == null || frmRuntimeDiagnostics.IsDisposed)
            {
                frmRuntimeDiagnostics = new FrmRuntimeDiagnostics(this);
                frmRuntimeDiagnostics.FormClosed += (sender, args) => frmRuntimeDiagnostics = null;
            }
            if (!frmRuntimeDiagnostics.Visible)
            {
                frmRuntimeDiagnostics.Show();
            }
            if (frmRuntimeDiagnostics.WindowState == FormWindowState.Minimized)
            {
                frmRuntimeDiagnostics.WindowState = FormWindowState.Normal;
            }
            frmRuntimeDiagnostics.BringToFront();
            frmRuntimeDiagnostics.Activate();
        }

        public void ShowPerformanceAnalysis()
        {
            if (frmPerformanceAnalysis == null || frmPerformanceAnalysis.IsDisposed)
            {
                frmPerformanceAnalysis = new FrmPerformanceAnalysis(this);
                frmPerformanceAnalysis.FormClosed += (sender, args) => frmPerformanceAnalysis = null;
            }
            if (!frmPerformanceAnalysis.Visible) frmPerformanceAnalysis.Show();
            if (frmPerformanceAnalysis.WindowState == FormWindowState.Minimized)
            {
                frmPerformanceAnalysis.WindowState = FormWindowState.Normal;
            }
            frmPerformanceAnalysis.BringToFront();
            frmPerformanceAnalysis.Activate();
        }

        internal void ApplyRuntimeDiagnosticsConfiguration(bool configuredEnabled)
        {
            bool enabled = configuredEnabled;
            if (enabled && runtimeBlackBoxRecorder == null)
            {
                try
                {
                    runtimeBlackBoxRecorder = new RuntimeBlackBoxRecorder(dataRun, Runtime.Stores.Values);
                    Runtime.RuntimeBlackBoxRecorder = runtimeBlackBoxRecorder;
                }
                catch (Exception ex)
                {
                    enabled = false;
                    dataRun?.Logger?.Log("智能诊断黑匣子初始化失败：" + ex.Message, LogLevel.Error);
                }
            }
            runtimeDiagnosticsEnabled = enabled;
            frmMenu?.SetRuntimeDiagnosticsEnabled(enabled);
            if (enabled) return;

            try
            {
                frmRuntimeDiagnostics?.Close();
            }
            catch (InvalidOperationException)
            {
            }
            automationMcpServerManager.StopRuntimeDiagnostic();
            RuntimeBlackBoxRecorder recorder = runtimeBlackBoxRecorder;
            runtimeBlackBoxRecorder = null;
            Runtime.RuntimeBlackBoxRecorder = null;
            try
            {
                recorder?.Dispose();
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log("关闭智能诊断黑匣子失败：" + ex.Message, LogLevel.Error);
            }
        }

        

        public void Monitor()
        {
            Runtime.Devices?.StartAxisMonitor();
        }

        public void ResetAxisRuntimeState()
        {
            Runtime.Devices?.ClearAxisRuntimeState();
        }

        private void HandleDeviceFault(string message)
        {
            runtimeBlackBoxRecorder?.RecordExternalEvent(
                "device.runtime.faulted", "motion", message, true);
            void RefreshAvailability()
            {
                frmControl?.RefreshMotionControlAvailability();
            }
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((Action)RefreshAvailability);
                return;
            }
            RefreshAvailability();
        }

        private void TryStopMotion()
        {
            Runtime.Devices?.Stop();
        }

        private void CacheSnapshot(EngineSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }
            EngineSnapshot previous = null;
            lock (snapshotLock)
            {
                if (snapshot.ProcId == Guid.Empty)
                {
                    return;
                }
                snapshotCache.TryGetValue(snapshot.ProcId, out previous);
                snapshotCache[snapshot.ProcId] = snapshot;
                bool treeVisualChanged = previous == null
                    || previous.ProcIndex != snapshot.ProcIndex
                    || previous.State != snapshot.State
                    || previous.StepIndex != snapshot.StepIndex
                    || previous.IsBreakpoint != snapshot.IsBreakpoint
                    || !string.Equals(previous.ProcName, snapshot.ProcName, StringComparison.Ordinal);
                if (treeVisualChanged)
                {
                    snapshotDirty.Add(snapshot.ProcId);
                }
            }
        }

        private void StartSnapshotTimer()
        {
            if (snapshotTimer != null)
            {
                return;
            }
            snapshotTimer = new System.Windows.Forms.Timer();
            snapshotTimer.Interval = 200;
            snapshotTimer.Tick += SnapshotTimer_Tick;
            snapshotTimer.Start();
        }

        private void SnapshotTimer_Tick(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }
            List<EngineSnapshot> pending = null;
            lock (snapshotLock)
            {
                if (snapshotDirty.Count > 0)
                {
                    pending = new List<EngineSnapshot>(snapshotDirty.Count);
                    foreach (Guid procId in snapshotDirty)
                    {
                        if (snapshotCache.TryGetValue(procId, out EngineSnapshot snapshot))
                        {
                            pending.Add(snapshot);
                        }
                    }
                    snapshotDirty.Clear();
                }
            }
            if (pending != null)
            {
                // 节点属性变更产生的局部无效区域会由消息队列自动合并。
                // 不对整棵树 BeginUpdate/EndUpdate，否则每轮快照都会触发全树重绘。
                foreach (EngineSnapshot snapshot in pending)
                {
                    UpdateProcText(snapshot);
                }
            }
            UpdateHighlightFromCache();
        }

        private void UpdateHighlightFromCache()
        {
            if (frmDataGrid == null || frmProc == null)
            {
                return;
            }
            int selectedProc = frmProc.SelectedProcNum;
            EngineSnapshot snapshot = null;
            Guid procId = Guid.Empty;
            if (frmProc.procsList != null && selectedProc >= 0 && selectedProc < frmProc.procsList.Count)
            {
                procId = frmProc.procsList[selectedProc]?.head?.Id ?? Guid.Empty;
            }
            if (procId != Guid.Empty)
            {
                lock (snapshotLock)
                {
                    snapshotCache.TryGetValue(procId, out snapshot);
                }
            }
            if (snapshot == null && Runtime.ProcessEngine != null && selectedProc >= 0)
            {
                EngineSnapshot direct = Runtime.ProcessEngine.GetSnapshot(selectedProc);
                if (direct != null && (procId == Guid.Empty || direct.ProcId == procId))
                {
                    snapshot = direct;
                }
            }
            frmDataGrid.UpdateHighlight(snapshot);
        }

        internal void AttachInitializedPlatform()
        {
            if (platformInitialized)
            {
                return;
            }
            if (Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                throw new InvalidOperationException("平台编辑器必须在创建它的 UI 线程附加运行时。");
            }
            platformInitializationStarted = true;
            frmProc.RefreshProcListFromStore();
            frmIO.RefreshIODgv();
            frmCard.RefreshStationTree();
            frmdataStruct.RefreshDataSturctList();
            frmIODebug.RefreshIODebugMapFromStore();
            frmCommunication.RefreshSocketMap();
            frmCommunication.RefreshSerialPortInfo();
            frmAlarmConfig.RefreshAlarmInfoFromStore();
            frmIODebug.RefleshIODebug();
            frmControl?.RefreshMotionControlAvailability();
            platformInitialized = true;
            QueueEditorCachePrewarm();
        }

        private void QueueEditorCachePrewarm()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            frmSearch?.PrewarmIndex();
            QueueProcessFlowHostPrewarm();
        }

        private void QueueProcessFlowHostPrewarm()
        {
            uiWarmupCoordinator.Schedule("process-flow-host", 20, () =>
            {
                if (IsDisposed || Disposing || !platformInitialized)
                {
                    return;
                }
                if (!flowGraphUnavailable
                    && (frmProcessFlow == null || frmProcessFlow.IsDisposed))
                {
                    try
                    {
                        frmProcessFlow = new FrmProcessFlow(this);
                        frmProcessFlow.Prewarm();
                    }
                    catch (Exception ex)
                    {
                        flowGraphUnavailable = true;
                        string message = "流程图预热失败，首次打开时将不可用：" + ex.Message;
                        dataRun?.Logger?.Log(message, LogLevel.Error);
                        frmInfo?.PrintInfo(message, FrmInfo.Level.Error);
                    }
                }
                if (frmProcessFlow != null && !frmProcessFlow.IsDisposed)
                {
                    PrewarmProcessFlowGraphs();
                }
            });
        }

        private void NotifyEditorInteraction()
        {
            uiWarmupCoordinator.NotifyInteraction();
        }

        private void UpdateProcText(EngineSnapshot snapshot)
        {
            if (frmProc?.proc_treeView == null || frmProc.procsList == null)
            {
                return;
            }
            if (snapshot == null)
            {
                return;
            }
            int procNum = snapshot.ProcIndex;
            TreeNode targetNode = null;
            if (snapshot.ProcId != Guid.Empty
                && frmProc.TryGetProcNode(snapshot.ProcId, out TreeNode mappedNode, out int mappedIndex))
            {
                procNum = mappedIndex;
                targetNode = mappedNode;
            }
            else
            {
                if (procNum < 0 || procNum >= frmProc.procsList.Count || procNum >= frmProc.proc_treeView.Nodes.Count)
                {
                    return;
                }
                targetNode = frmProc.proc_treeView.Nodes[procNum];
            }
            if (procNum < 0 || procNum >= frmProc.procsList.Count || targetNode == null)
            {
                return;
            }

            void ApplyProcText()
            {
                bool isDisabled = procNum >= 0
                    && procNum < frmProc.procsList.Count
                    && frmProc.procsList[procNum]?.head?.Disable == true;
                Proc proc = frmProc.procsList[procNum];
                string nextText = frmProc.BuildProcNodeTextWithState(procNum, proc, snapshot);
                Color nextColor;
                if (isDisabled)
                {
                    nextColor = UiPalette.TextMuted;
                }
                else
                {
                    switch (snapshot.State)
                    {
                        case ProcRunState.Running:
                            nextColor = UiPalette.Success;
                            break;
                        case ProcRunState.Paused:
                            nextColor = UiPalette.Warning;
                            break;
                        case ProcRunState.SingleStep:
                            nextColor = UiPalette.Focus;
                            break;
                        case ProcRunState.Alarming:
                            nextColor = UiPalette.Danger;
                            break;
                        case ProcRunState.Pausing:
                            nextColor = UiPalette.Warning;
                            break;
                        case ProcRunState.Stopping:
                            nextColor = UiPalette.Danger;
                            break;
                        case ProcRunState.Stopped:
                            nextColor = UiPalette.TextPrimary;
                            break;
                        case ProcRunState.Ready:
                            nextColor = UiPalette.Success;
                            break;
                        default:
                            nextColor = UiPalette.TextPrimary;
                            break;
                    }
                }
                if (!string.Equals(targetNode.Text, nextText, StringComparison.Ordinal))
                {
                    targetNode.Text = nextText;
                }
                if (targetNode.ForeColor != nextColor)
                {
                    targetNode.ForeColor = nextColor;
                }
                frmProc.UpdateProcStateIcons(procNum, snapshot);
            }
            if (frmProc.proc_treeView.InvokeRequired)
            {
                frmProc.proc_treeView.BeginInvoke((Action)ApplyProcText);
            }
            else
            {
                ApplyProcText();
            }

            if (frmToolBar?.btnPause != null && procNum == frmProc.SelectedProcNum)
            {
                void ApplyPauseState()
                {
                    frmToolBar.ApplyProcessRunState(snapshot.State);
                }
                if (frmToolBar.btnPause.InvokeRequired)
                {
                    frmToolBar.btnPause.BeginInvoke((Action)ApplyPauseState);
                }
                else
                {
                    ApplyPauseState();
                }
            }
        }

        public void loadFillForm(Panel panel, System.Windows.Forms.Form frm)
        {
            if (frm != null && panel != null)
            {
                panel.SuspendLayout();
                try
                {
                    frm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                    frm.ShowIcon = false;
                    frm.ShowInTaskbar = false;
                    frm.TopLevel = false;
                    frm.Dock = DockStyle.Fill;
                    panel.Controls.Add(frm);
                    frm.BringToFront();
                    frm.Show();
                    frm.Focus();
                }
                finally
                {
                    panel.ResumeLayout(true);
                }
            }
        }

        public bool AreAllProcessesStopped()
        {
            if (frmProc?.procsList == null || Runtime.ProcessEngine == null)
            {
                return false;
            }
            for (int i = 0; i < frmProc.procsList.Count; i++)
            {
                EngineSnapshot snapshot = Runtime.ProcessEngine.GetSnapshot(i);
                if (snapshot == null || !snapshot.State.IsInactive())
                {
                    return false;
                }
            }
            return true;
        }

        public void RequireRestartAfterVersionRestore()
        {
            Runtime.Readiness.VersionRestartRequired = true;
            Runtime.Safety.StopAllProcesses("配置版本已还原，必须重启程序后才能继续运行。");
        }

        private void InitializeWorkspacePageHost()
        {
            Controls.Remove(state_panel);
            editorWorkspacePage = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiPalette.Background
            };
            editorWorkspacePage.Controls.Add(DataGrid_panel);
            editorWorkspacePage.Controls.Add(panel_Info);
            editorWorkspacePage.Controls.Add(inspector_panel);
            editorWorkspacePage.Controls.Add(processTreeSplitter);
            editorWorkspacePage.Controls.Add(treeView_panel);
            editorWorkspacePage.Controls.Add(ToolBar_panel);
            editorWorkspacePage.Controls.Add(state_panel);

            workspacePageHost = new WorkspacePageHost
            {
                Dock = DockStyle.Fill,
                BackColor = UiPalette.Background
            };
            main_panel.Controls.Add(workspacePageHost);
            workspacePageHost.ShowPage(editorWorkspacePage);
        }

        public void ShowEditorWorkspace()
        {
            workspacePageHost.ShowPage(editorWorkspacePage);
        }

        internal void UpdateEditorWorkspaceLayout(Action update)
        {
            if (update == null)
            {
                return;
            }
            SuspendLayout();
            editorWorkspacePage.SuspendLayout();
            try
            {
                update();
            }
            finally
            {
                editorWorkspacePage.ResumeLayout(true);
                ResumeLayout(true);
            }
        }

        public bool ShowAiAssistantWithPrompt(string prompt)
        {
            EnsureAiInfrastructureStarted();
            frmMenu.ShowAiAssistant();
            return frmAiAssistant != null && !frmAiAssistant.IsDisposed
                && frmAiAssistant.PreparePrompt(prompt);
        }

        public void RequireRestartAfterMotionConfigurationChange()
        {
            Runtime.Readiness.MotionConfigRestartRequired = true;
            frmControl?.RefreshMotionControlAvailability();
            const string message = "运动设备配置已保存，重启程序后生效；重启前轴运动不可用，MES、通讯及其他非运动流程可继续运行。";
            dataRun?.Logger?.Log(message, LogLevel.Normal);
            if (frmInfo != null && !frmInfo.IsDisposed)
            {
                frmInfo.PrintInfo(message, FrmInfo.Level.Normal);
            }
        }

        private void FrmMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (Runtime.Editor.TryHandleHistoryShortcut(this, e))
            {
                return;
            }
            if (e.KeyCode == Keys.F && e.Control)
            {
                if(editorWorkspace.CurrentPage == 0)
                {
                    frmToolBar.btnSearch.PerformClick();
                }
                e.Handled = true; // 防止其他控件处理该键按下事件
            }
            if (e.KeyCode == Keys.D && e.Control)
            {
                if (editorWorkspace.CurrentPage != 0)
                {
                    if (frmInfo != null && !frmInfo.IsDisposed)
                    {
                        frmInfo.PrintInfo("快捷键：仅支持在流程界面设为启动点。", FrmInfo.Level.Error);
                    }
                    e.Handled = true;
                    return;
                }

                if (frmProc == null || frmDataGrid == null || Runtime.ProcessEngine == null)
                {
                    if (frmInfo != null && !frmInfo.IsDisposed)
                    {
                        frmInfo.PrintInfo("快捷键：流程组件未就绪，无法设为启动点。", FrmInfo.Level.Error);
                    }
                    e.Handled = true;
                    return;
                }

                int procIndex = frmProc.SelectedProcNum;
                int stepIndex = frmProc.SelectedStepNum;
                int opIndex = frmDataGrid.iSelectedRow;
                if (opIndex < 0 && frmDataGrid.dataGridView1.CurrentIndex >= 0)
                {
                    opIndex = frmDataGrid.dataGridView1.CurrentIndex;
                    frmDataGrid.iSelectedRow = opIndex;
                }

                if (procIndex < 0 || stepIndex < 0 || opIndex < 0 || opIndex >= frmDataGrid.dataGridView1.OperationCount)
                {
                    if (frmInfo != null && !frmInfo.IsDisposed)
                    {
                        frmInfo.PrintInfo("快捷键：未选择指令，无法设为启动点。", FrmInfo.Level.Error);
                    }
                    e.Handled = true;
                    return;
                }

                string opName = null;
                if (frmProc?.procsList != null && procIndex >= 0 && procIndex < frmProc.procsList.Count)
                {
                    Step step = frmProc.procsList[procIndex]?.steps?[stepIndex];
                    if (step?.Ops != null && opIndex >= 0 && opIndex < step.Ops.Count)
                    {
                        opName = step.Ops[opIndex]?.Name;
                    }
                }
                if (string.IsNullOrWhiteSpace(opName))
                {
                    opName = "未命名";
                }

                bool started = Runtime.ProcessEngine.TrySetDebugStartPoint(
                    null,
                    procIndex,
                    stepIndex,
                    opIndex,
                    out string stateError);
                if (!started)
                {
                    if (frmInfo != null && !frmInfo.IsDisposed)
                    {
                        frmInfo.PrintInfo($"快捷键：设置启动点失败：{stateError}", FrmInfo.Level.Error);
                    }
                    e.Handled = true;
                    return;
                }

                if (frmToolBar != null && !frmToolBar.IsDisposed)
                {
                    frmToolBar.ApplyProcessRunState(ProcRunState.SingleStep);
                }

                if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo($"快捷键：{procIndex}-{stepIndex}-{opIndex} {opName} 设为启动点", FrmInfo.Level.Normal);
                }
                e.Handled = true;
            }
        }

        private void HandlePlcRuntimeEvent(object sender, PlcRuntimeEventArgs e)
        {
            if (e == null) return;
            runtimeBlackBoxRecorder?.RecordExternalEvent(
                "plc.runtime", e.DeviceName, e.Message, e.IsAlarm);
            void Report()
            {
                dataRun?.Logger?.Log($"PLC[{e.DeviceName}] {e.Message}", e.IsAlarm ? LogLevel.Error : LogLevel.Normal);
            }
            if (IsHandleCreated && InvokeRequired) BeginInvoke((Action)Report);
            else Report();
        }

        private void HandleDataBreakpointHit(object sender, DataBreakpointHit hit)
        {
            if (hit == null || Volatile.Read(ref shutdownStarted) != 0)
            {
                return;
            }
            void Report()
            {
                string message = hit.BuildSummary();
                if (dataRun?.Logger != null)
                {
                    dataRun.Logger.Log(message, LogLevel.Normal);
                }
                else if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo(message, FrmInfo.Level.Normal);
                }
            }
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((Action)Report);
            }
            else
            {
                Report();
            }
        }

        private void HandleCommunicationFramesDropped(object sender, CommFramesDroppedEventArgs e)
        {
            if (e == null)
            {
                return;
            }
            string message = $"通讯接收队列发生丢帧：{e.Kind}[{e.Name}]累计丢弃{e.DroppedFrames}帧。请检查消费速度、触发序号和队列容量。";
            runtimeBlackBoxRecorder?.RecordExternalEvent(
                "communication.frames_dropped", $"{e.Kind}:{e.Name}", message, true);
            dataRun?.Logger?.Log(message, LogLevel.Error);
        }
    }

}
