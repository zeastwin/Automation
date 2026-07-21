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
using System.Threading.Tasks;
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
        public FrmComunication frmComunication = new FrmComunication();
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
            ProcessDefinitionStore processDefinitionStore = Runtime.Stores.Processes;
            IoConfigurationStore ioConfigurationStore = Runtime.Stores.IoConfiguration;
            StationDefinitionStore stationDefinitionStore = Runtime.Stores.Stations;
            frmProc = new FrmProc(processDefinitionStore);
            frmIO = new FrmIO(ioConfigurationStore);
            frmCard = new FrmCard(stationDefinitionStore);
            editorWorkspace = new EditorWorkspace(this, Runtime);
            customFunc = Runtime.CustomFunctions;
            uiThreadId = Thread.CurrentThread.ManagedThreadId;
            UiBranding.Apply(this);
            InitializeComponent();
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
            WindowState = FormWindowState.Maximized;
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
            frmAiAssistant.Show();

            StartSnapshotTimer();
            Text = "Automation - 平台";
            Shown += FrmMain_Shown;
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

        

        private void FrmMain_Load(object sender, EventArgs e)
        {
            InitializePlatform();
        }

        private void FrmMain_Shown(object sender, EventArgs e)
        {
            EnsureAiInfrastructureStarted();
        }

        internal void InitializePlatform()
        {
            if (platformInitialized)
            {
                return;
            }
            if (platformInitializationStarted)
            {
                throw new InvalidOperationException("平台初始化已开始但尚未成功完成，禁止重复初始化。");
            }
            if (Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                throw new InvalidOperationException("平台必须在创建 FrmMain 的 UI 线程初始化。");
            }
            platformInitializationStarted = true;
            try
            {
                PlatformRuntimeInitializationResult initialization =
                    PlatformRuntimeInitializer.Initialize(Runtime);
                AttachInitializedPlatform(initialization);
            }
            catch (Exception ex)
            {
                Runtime.Safety.StopAllProcesses($"平台初始化失败:{ex.Message}");
                TryStopMotion();
                throw;
            }
        }

        internal void EnsureAiInfrastructureStarted()
        {
            if (Interlocked.CompareExchange(ref aiInfrastructureStartState, 1, 0) != 0)
            {
                return;
            }
            try
            {
                if (!GooseConfigStorage.TryLoad(out GooseConfig aiConfig, out string aiConfigError))
                {
                    ReportAiInfrastructureUnavailable("EW-AI 配置不可用：" + aiConfigError);
                    return;
                }
                if (!GooseRuntimeEnvironment.TryValidate(aiConfig.GooseExecutablePath, out string runtimeError))
                {
                    ReportAiInfrastructureUnavailable(runtimeError);
                    return;
                }
                if (!GooseConfigStorage.TryApplyStartupSafetyDefaults(out string aiSafetyError))
                {
                    ReportAiInfrastructureUnavailable(aiSafetyError);
                    return;
                }
                if (!GooseRuntimeProvisioner.IsManagedContextAvailable
                    && !GooseRuntimeProvisioner.TryEnsureManagedContext(out string contextError))
                {
                    ReportAiInfrastructureUnavailable(contextError);
                    return;
                }
                automationBridgeHost.Start();
                StartMcpServerOnStartup();
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"AI 基础设施启动失败:{ex.Message}", LogLevel.Error);
                if (frmInfo != null && !frmInfo.IsDisposed && dataRun?.Logger == null)
                {
                    frmInfo.PrintInfo($"AI 基础设施启动失败:{ex.Message}", FrmInfo.Level.Error);
                }
            }
        }

        private void ReportAiInfrastructureUnavailable(string message)
        {
            string scopedMessage = "AI 基础设施未启动：" + message;
            dataRun?.Logger?.Log(scopedMessage, LogLevel.Error);
            if (frmInfo != null && !frmInfo.IsDisposed && dataRun?.Logger == null)
            {
                frmInfo.PrintInfo(scopedMessage, FrmInfo.Level.Error);
            }
        }
        
        private async void StartMcpServerOnStartup()
        {
            string baseUri = GooseConfigStorage.CreateDefaultConfig().McpUri;
            string toolProfile = GooseConfigStorage.CreateDefaultConfig().ToolProfile;
            if (GooseConfigStorage.TryLoad(out GooseConfig config, out string loadError))
            {
                baseUri = config.McpUri;
                toolProfile = config.ToolProfile;
            }
            else if (frmInfo != null && !frmInfo.IsDisposed)
            {
                frmInfo.PrintInfo($"MCP Server：EW-AI 配置读取失败，使用默认 MCP 地址。{loadError}", FrmInfo.Level.Error);
            }

            try
            {
                string result = await automationMcpServerManager.EnsureStartedAsync(baseUri, toolProfile).ConfigureAwait(true);
                if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo("MCP Server：" + result, FrmInfo.Level.Normal);
                }
            }
            catch (Exception ex)
            {
                if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo("MCP Server 启动失败：" + ex.Message, FrmInfo.Level.Error);
                }
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

        private void WaitForAllProcsStopped(int timeoutMs)
        {
            if (Runtime.ProcessEngine == null)
            {
                return;
            }
            int count = frmProc?.procsList?.Count ?? 0;
            if (count <= 0)
            {
                return;
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                bool allStopped = true;
                for (int i = 0; i < count; i++)
                {
                    EngineSnapshot snapshot = Runtime.ProcessEngine.GetSnapshot(i);
                    if (snapshot != null && snapshot.State != ProcRunState.Stopped)
                    {
                        allStopped = false;
                        break;
                    }
                }
                if (allStopped)
                {
                    return;
                }
                Thread.Sleep(20);
            }
            dataRun?.Logger?.Log("等待流程停止超时，继续关闭。", LogLevel.Error);
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
                TreeView processTree = frmProc?.proc_treeView;
                bool suspendTreeDrawing = processTree != null
                    && !processTree.IsDisposed
                    && processTree.IsHandleCreated;
                if (suspendTreeDrawing)
                {
                    processTree.BeginUpdate();
                }
                try
                {
                    foreach (EngineSnapshot snapshot in pending)
                    {
                        UpdateProcText(snapshot);
                    }
                }
                finally
                {
                    if (suspendTreeDrawing && !processTree.IsDisposed)
                    {
                        processTree.EndUpdate();
                    }
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

        internal void AttachInitializedPlatform(PlatformRuntimeInitializationResult initialization)
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
            frmValue.RefreshFromStore();
            frmIO.RefreshIODgv();
            frmCard.RefreshStationTree();
            frmdataStruct.RefreshDataSturctList();
            frmIODebug.RefreshIODebugMapFromStore();
            frmComunication.RefreshSocketMap();
            frmComunication.RefreshSerialPortInfo();
            frmAlarmConfig.RefreshAlarmInfoFromStore();
            frmIODebug.RefleshIODebug();
            if (!string.IsNullOrWhiteSpace(initialization?.Device?.WindowTitle))
            {
                Text = initialization.Device.WindowTitle;
            }
            frmControl?.RefreshMotionControlAvailability();
            platformInitialized = true;
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
                    nextColor = UiPalette.DisabledSoft;
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
                bool continueAction = snapshot.State != ProcRunState.Running && snapshot.State != ProcRunState.Alarming;
                bool allowResume = snapshot.State != ProcRunState.Paused;
                bool allowSingleStep = snapshot.State == ProcRunState.SingleStep;
                void ApplyPauseState()
                {
                    frmToolBar.SetPauseButtonAction(continueAction);
                    frmToolBar.btnPause.Enabled = allowResume;
                    frmToolBar.SingleRun.Enabled = allowSingleStep;
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
                if (snapshot == null || snapshot.State != ProcRunState.Stopped)
                {
                    return false;
                }
            }
            return true;
        }

        public void ReloadProcessVersionedConfiguration()
        {
            frmProc.RefreshProcList();
            frmValue.RefreshDic();
            Runtime.Stores.DataStructures.Load(Runtime.Paths.ConfigPath);
            frmdataStruct.RefreshDataSturctList();
            if (Runtime.ProcessEngine?.Context != null)
            {
                Runtime.ProcessEngine.Context.ValueStore = Runtime.Stores.Values;
                Runtime.ProcessEngine.Context.DataStructStore = Runtime.Stores.DataStructures;
            }
        }

        public void RequireRestartAfterEquipmentRestore()
        {
            Runtime.Readiness.VersionRestartRequired = true;
            Runtime.Safety.StopAllProcesses("设备配置已还原，必须重启程序后才能继续运行。");
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

        private void EditorTreeSelectionChanged(object sender, TreeViewEventArgs e)
        {
            if (e.Action != TreeViewAction.Unknown)
            {
                RecordCurrentEditorLocation();
            }
        }

        private void EditorOperationListMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left
                && frmDataGrid.dataGridView1.IndexFromPoint(e.Location) >= 0)
            {
                RecordCurrentEditorLocation();
            }
        }

        private void EditorOperationListKeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    RecordCurrentEditorLocation();
                    break;
            }
        }

        private void RecordCurrentEditorLocation()
        {
            if (editorNavigationChanging)
            {
                return;
            }
            EditorNavigationLocation location = CaptureCurrentEditorLocation();
            if (location == null)
            {
                return;
            }
            if (currentEditorLocation == null)
            {
                currentEditorLocation = location;
                UpdateEditorNavigationActions();
                return;
            }
            if (currentEditorLocation.SameAs(location))
            {
                return;
            }
            PushUnique(editorBackHistory, currentEditorLocation);
            currentEditorLocation = location;
            editorForwardHistory.Clear();
            UpdateEditorNavigationActions();
        }

        private EditorNavigationLocation CaptureCurrentEditorLocation()
        {
            int procIndex = frmProc?.SelectedProcNum ?? -1;
            if (frmProc?.procsList == null
                || procIndex < 0
                || procIndex >= frmProc.procsList.Count)
            {
                return null;
            }
            Proc proc = frmProc.procsList[procIndex];
            Guid procId = proc?.head?.Id ?? Guid.Empty;
            if (procId == Guid.Empty)
            {
                return null;
            }
            int stepIndex = frmProc.SelectedStepNum;
            Step step = stepIndex >= 0 && stepIndex < (proc.steps?.Count ?? 0)
                ? proc.steps[stepIndex]
                : null;
            OperationType operation = null;
            int operationIndex = frmDataGrid?.dataGridView1?.CurrentIndex ?? -1;
            if (step != null
                && operationIndex >= 0
                && operationIndex < (step.Ops?.Count ?? 0))
            {
                operation = step.Ops[operationIndex];
            }
            return new EditorNavigationLocation
            {
                ProcId = procId,
                StepId = step?.Id ?? Guid.Empty,
                OpId = operation?.Id ?? Guid.Empty
            };
        }

        private static void PushUnique(
            Stack<EditorNavigationLocation> history,
            EditorNavigationLocation location)
        {
            if (location != null && (history.Count == 0 || !history.Peek().SameAs(location)))
            {
                history.Push(location);
            }
        }

        internal bool NavigateEditorBack()
        {
            return NavigateEditorHistory(editorBackHistory, editorForwardHistory);
        }

        internal bool NavigateEditorForward()
        {
            return NavigateEditorHistory(editorForwardHistory, editorBackHistory);
        }

        private bool NavigateEditorHistory(
            Stack<EditorNavigationLocation> sourceHistory,
            Stack<EditorNavigationLocation> destinationHistory)
        {
            if (!CanUseEditorNavigation())
            {
                return false;
            }
            EditorNavigationLocation source = CaptureCurrentEditorLocation() ?? currentEditorLocation;
            while (sourceHistory.Count > 0)
            {
                EditorNavigationLocation target = sourceHistory.Pop();
                if (target == null || target.SameAs(source))
                {
                    continue;
                }
                if (!TryNavigateToEditorLocation(target))
                {
                    continue;
                }
                PushUnique(destinationHistory, source);
                currentEditorLocation = CaptureCurrentEditorLocation() ?? target;
                UpdateEditorNavigationActions();
                return true;
            }
            UpdateEditorNavigationActions();
            return false;
        }

        private bool CanUseEditorNavigation()
        {
            return !IsDisposed
                && !Disposing
                && Runtime.Editor.ActiveSession == null
                && workspacePageHost != null
                && ReferenceEquals(workspacePageHost.ActivePage, editorWorkspacePage)
                && Form.ActiveForm == this;
        }

        private bool NavigateToEditorLocation(EditorNavigationLocation target, bool recordSource)
        {
            if (target == null || Runtime.Editor.ActiveSession != null)
            {
                return false;
            }
            EditorNavigationLocation source = CaptureCurrentEditorLocation() ?? currentEditorLocation;
            if (!TryNavigateToEditorLocation(target))
            {
                return false;
            }
            if (recordSource && source != null && !source.SameAs(target))
            {
                PushUnique(editorBackHistory, source);
                editorForwardHistory.Clear();
            }
            currentEditorLocation = CaptureCurrentEditorLocation() ?? target;
            UpdateEditorNavigationActions();
            return true;
        }

        private bool TryNavigateToEditorLocation(EditorNavigationLocation target)
        {
            if (!TryResolveEditorLocation(target, out int procIndex, out int stepIndex))
            {
                return false;
            }
            editorNavigationChanging = true;
            try
            {
                ShowEditorWorkspace();
                frmProc.SelectAiContext(procIndex, stepIndex);
                if (target.OpId != Guid.Empty)
                {
                    if (!frmDataGrid.SelectOperationForNavigation(target.OpId))
                    {
                        return false;
                    }
                    frmDataGrid.dataGridView1.Focus();
                }
                else
                {
                    frmDataGrid.iSelectedRow = -1;
                    frmDataGrid.dataGridView1.ClearSelection();
                    frmProc.proc_treeView.Focus();
                }
                return true;
            }
            finally
            {
                editorNavigationChanging = false;
            }
        }

        private bool TryResolveEditorLocation(
            EditorNavigationLocation location,
            out int procIndex,
            out int stepIndex)
        {
            procIndex = -1;
            stepIndex = -1;
            if (location == null || location.ProcId == Guid.Empty || frmProc?.procsList == null)
            {
                return false;
            }
            procIndex = frmProc.procsList.FindIndex(proc => proc?.head?.Id == location.ProcId);
            if (procIndex < 0)
            {
                return false;
            }
            if (location.StepId == Guid.Empty)
            {
                return location.OpId == Guid.Empty;
            }
            Proc proc = frmProc.procsList[procIndex];
            stepIndex = proc.steps?.FindIndex(step => step?.Id == location.StepId) ?? -1;
            if (stepIndex < 0)
            {
                return false;
            }
            return location.OpId == Guid.Empty
                || proc.steps[stepIndex].Ops?.Any(operation => operation?.Id == location.OpId) == true;
        }

        private void UpdateEditorNavigationActions()
        {
            bool navigationEnabled = Runtime.Editor.ActiveSession == null;
            frmToolBar?.SetNavigationAvailability(
                navigationEnabled && editorBackHistory.Count > 0,
                navigationEnabled && editorForwardHistory.Count > 0);
        }

        internal void RefreshEditorNavigationActions()
        {
            UpdateEditorNavigationActions();
        }

        public void ShowWorkspacePage(Form page)
        {
            if (page == null || page.IsDisposed)
            {
                return;
            }
            if (!workspacePageHost.Controls.Contains(page))
            {
                page.FormBorderStyle = FormBorderStyle.None;
                page.ShowIcon = false;
                page.ShowInTaskbar = false;
                page.TopLevel = false;
                page.Dock = DockStyle.Fill;
                workspacePageHost.Controls.Add(page);
                page.Show();
            }
            workspacePageHost.ShowPage(page);
            page.Focus();
        }

        public void ShowProcessFlowGraph()
        {
            if (flowGraphUnavailable)
            {
                MessageBox.Show(this, "流程图模块当前不可用，请查看运行信息中的报警。", "流程图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                if (frmProcessFlow == null || frmProcessFlow.IsDisposed)
                {
                    frmProcessFlow = new FrmProcessFlow(this);
                }
                ShowWorkspacePage(frmProcessFlow);
                frmProcessFlow.ShowProjectGraph();
            }
            catch (Exception ex)
            {
                flowGraphUnavailable = true;
                string message = "流程图模块初始化失败，平台其他功能继续可用：" + ex.Message;
                dataRun?.Logger?.Log(message, LogLevel.Error);
                frmInfo?.PrintInfo(message, FrmInfo.Level.Error);
                MessageBox.Show(this, message, "流程图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void RefreshProcessFlowGraph()
        {
            if (frmProcessFlow != null && !frmProcessFlow.IsDisposed && frmProcessFlow.Visible)
            {
                frmProcessFlow.RefreshCurrentGraph();
            }
        }

        public void ShowDataBreakpoints()
        {
            if (frmDataBreakpoints == null || frmDataBreakpoints.IsDisposed)
            {
                frmDataBreakpoints = new FrmDataBreakpoints(this, dataBreakpointService);
                frmDataBreakpoints.FormClosed += (sender, args) => frmDataBreakpoints = null;
            }
            if (!frmDataBreakpoints.Visible)
            {
                frmDataBreakpoints.Show(this);
            }
            if (frmDataBreakpoints.WindowState == FormWindowState.Minimized)
            {
                frmDataBreakpoints.WindowState = FormWindowState.Normal;
            }
            frmDataBreakpoints.BringToFront();
            frmDataBreakpoints.Activate();
        }

        public bool NavigateToFlowOperation(Guid procId, Guid stepId, Guid opId)
        {
            return NavigateToEditorLocation(new EditorNavigationLocation
            {
                ProcId = procId,
                StepId = stepId,
                OpId = opId
            }, true);
        }

        internal bool NavigateToDataBreakpointTrigger(DataBreakpointHit hit, out string error)
        {
            error = null;
            if (hit == null)
            {
                error = "断点命中数据为空。";
                return false;
            }
            if (hit.TriggerProcId == Guid.Empty)
            {
                error = $"触发源来自“{hit.TriggerDescription}”，没有可定位的流程指令位置。";
                return false;
            }
            bool navigated = NavigateToEditorLocation(new EditorNavigationLocation
            {
                ProcId = hit.TriggerProcId,
                StepId = hit.TriggerStepId,
                OpId = hit.TriggerOperationId
            }, true);
            if (!navigated)
            {
                error = "触发源对应的流程、步骤或指令已经不存在，无法定位。";
                return false;
            }
            Activate();
            return true;
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

                ProcRunState startState = ProcRunState.SingleStep;
                EngineSnapshot startSnapshot = Runtime.ProcessEngine.GetSnapshot(procIndex);
                if (startSnapshot != null && startSnapshot.State == ProcRunState.Paused)
                {
                    startState = ProcRunState.Paused;
                }

                Runtime.ProcessEngine.Stop(procIndex);
                Runtime.ProcessEngine.StartProcAt(
                    null,
                    procIndex,
                    stepIndex,
                    opIndex,
                    startState);

                if (frmToolBar != null && !frmToolBar.IsDisposed)
                {
                    frmToolBar.SetPauseButtonAction(true);
                    frmToolBar.btnPause.Enabled = startState != ProcRunState.Paused;
                    frmToolBar.SingleRun.Enabled = startState == ProcRunState.SingleStep;
                }

                if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo($"快捷键：{procIndex}-{stepIndex}-{opIndex} {opName} 设为启动点", FrmInfo.Level.Normal);
                }
                e.Handled = true;
            }
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (allowFinalClose)
            {
                return;
            }
            if (HideOnUserClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            if (e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult result = MessageBox.Show("确认退出程序？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            ShutdownPlatform();
            allowFinalClose = true;
        }

        internal void AllowFinalClose()
        {
            allowFinalClose = true;
        }

        internal void ShutdownPlatform()
        {
            if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
            {
                return;
            }

            if (Runtime.ManualMotion != null)
            {
                Runtime.ManualMotion.CommandRejected -= HandleManualMotionRejected;
            }
            try
            {
                frmDataBreakpoints?.Close();
                dataBreakpointService.BreakpointHit -= HandleDataBreakpointHit;
                dataBreakpointService.Dispose();
            }
            catch
            {
                // 调试窗口或会话断点释放失败不应阻塞平台安全关闭。
            }
            Runtime.Safety.StopAllProcesses("系统关闭，停止所有流程。");
            try
            {
                frmRuntimeDiagnostics?.Close();
                frmPerformanceAnalysis?.Close();
            }
            catch
            {
            }
            // 必须先释放 Goose 客户端：Kill Goose 进程后，后台读取线程不再调用
            // HandlePermissionRequest 的同步 Invoke，避免与已阻塞的 UI 线程形成死锁导致程序关闭卡住。
            try
            {
                frmAiAssistant?.DisposeGooseClient();
            }
            catch
            {
            }
            try
            {
                automationMcpServerManager?.Dispose();
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"关闭 MCP Server 失败:{ex.Message}", LogLevel.Error);
            }
            try
            {
                automationBridgeHost?.Stop();
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"关闭 Bridge Host 失败:{ex.Message}", LogLevel.Error);
            }

            Runtime.Devices?.Stop();
            WaitForAllProcsStopped(2000);
            Runtime.ProcessInteraction?.CloseAll();

            try
            {
                Runtime.Stores.Values?.Save(Runtime.Paths.ConfigPath);
                Runtime.Stores.DataStructures?.Save(Runtime.Paths.ConfigPath);
                Runtime.Stores.Alarms?.Save(Runtime.Paths.ConfigPath);
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"保存运行配置失败:{ex.Message}", LogLevel.Error);
            }

            try
            {
                if (Runtime.PlcRuntime != null)
                {
                    Runtime.PlcRuntime.RuntimeEvent -= HandlePlcRuntimeEvent;
                    Runtime.PlcRuntime.Dispose();
                }
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"关闭PLC运行时失败:{ex.Message}", LogLevel.Error);
            }
            try
            {
                // comm.Dispose 内部使用 GetAwaiter().GetResult() 同步等待通道关闭，
                // 可能因 TCP 连接未响应而阻塞 UI 线程。放到线程池并加 3 秒超时保护。
                if (Runtime.Communication != null)
                {
                    Runtime.Communication.FramesDropped -= HandleCommunicationFramesDropped;
                    Task commDisposeTask = Task.Run(() =>
                    {
                        try { Runtime.Communication.Dispose(); }
                        catch { }
                    });
                    if (!commDisposeTask.Wait(3000))
                    {
                        dataRun?.Logger?.Log("关闭通讯超时，继续关闭程序。", LogLevel.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"关闭通讯失败:{ex.Message}", LogLevel.Error);
            }
            TryStopMotion();
            try
            {
                Runtime.SystemStatus?.Dispose();
                Runtime.SystemStatus = null;
                Runtime.ProcessInteraction?.Dispose();
                Runtime.ProcessInteraction = null;
                if (Runtime.Devices != null)
                {
                    Runtime.Devices.Faulted -= HandleDeviceFault;
                    Runtime.Devices.Dispose();
                }
                runtimeBlackBoxRecorder?.Dispose();
                runtimeBlackBoxRecorder = null;
                Runtime.RuntimeBlackBoxRecorder = null;
                processTraceAuditSink?.Dispose();
                if (dataRun != null)
                {
                    dataRun.SnapshotChanged -= CacheSnapshot;
                }
                dataRun?.Dispose();
            }
            catch (Exception ex)
            {
                // 引擎释放失败不应阻塞程序关闭，记录后继续。
                System.Diagnostics.Debug.WriteLine($"引擎释放失败:{ex.Message}");
            }
            if (snapshotTimer != null)
            {
                snapshotTimer.Stop();
                snapshotTimer.Tick -= SnapshotTimer_Tick;
                snapshotTimer.Dispose();
                snapshotTimer = null;
            }
            uiDispatcher?.Dispose();
            platformInitialized = false;
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
