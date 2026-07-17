using Automation.Bridge;
using Automation.MotionControl;
using Automation.Simulation;
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
        public FrmProc frmProc = new FrmProc();
        public FrmPropertyGrid frmPropertyGrid = new FrmPropertyGrid();
        public FrmToolBar frmToolBar = new FrmToolBar();
        public FrmValue frmValue = new FrmValue();
        public FrmValueDebug frmValueDebug = new FrmValueDebug();
        public FrmAiAssistant frmAiAssistant = new FrmAiAssistant();
        public Panel ai_panel;
        private WorkspacePageHost workspacePageHost;
        private Panel editorWorkspacePage;
        public FrmIO  frmIO = new FrmIO();
        public FrmCard  frmCard = new FrmCard();
        public ProcessEngine dataRun;
        public CustomFunc customFunc = new CustomFunc();
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
        private SimulationGatewayClient simulationGateway;
        private readonly Dictionary<Guid, EngineSnapshot> snapshotCache = new Dictionary<Guid, EngineSnapshot>();
        private readonly HashSet<Guid> snapshotDirty = new HashSet<Guid>();
        private readonly object snapshotLock = new object();
        private const string ResetStatusValueName = "复位状态";
        private const string SystemStatusValueName = "系统状态";
        private System.Windows.Forms.Timer snapshotTimer;
        private CancellationTokenSource axisMonitorCts;
        private Task axisMonitorTask;
        private volatile bool systemStatusReady;
        private volatile bool systemStatusFaulted;
        private bool processInteractionUiReady;
        private bool autoStartTriggeredForCurrentReset;
        private int popupAlarmCount;
        private readonly object popupLock = new object();
        private readonly Dictionary<int, List<ProcPopupItem>> procPopups = new Dictionary<int, List<ProcPopupItem>>();
        private readonly Queue<ProcPopupRequest> pendingProcPopups = new Queue<ProcPopupRequest>();
        private readonly int uiThreadId;
        private readonly Control uiDispatcher;
        private readonly AutomationBridgeHost automationBridgeHost;
        private readonly ProcessTraceAuditSink processTraceAuditSink;
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

        internal bool HideOnUserClose { get; set; }
        internal bool IsPlatformInitialized => platformInitialized;

        private sealed class ProcPopupItem
        {
            public ProcPopupItem(Message dialog, Action closeAction)
            {
                Dialog = dialog;
                CloseAction = closeAction;
            }

            public Message Dialog { get; }
            public Action CloseAction { get; }
        }

        public FrmMain()
        {
            uiThreadId = Thread.CurrentThread.ManagedThreadId;
            UiBranding.Apply(this);
            InitializeComponent();
            InitializeWorkspacePageHost();
            uiDispatcher = new Control();
            _ = uiDispatcher.Handle;
            if (AutomationRuntimeOptions.Current.IsSimulation)
            {
                simulationGateway = new SimulationGatewayClient();
                simulationGateway.Faulted += HandleSimulationGatewayFault;
                motion = simulationGateway;
                io = simulationGateway;
            }
            else
            {
                var hardwareMotion = new MotionCtrl();
                motion = hardwareMotion;
                io = hardwareMotion;
            }
            SF.cardStore = new CardConfigStore();
            SF.valueStore = new ValueConfigStore();
            SF.dataStructStore = new DataStructStore();
            SF.trayPointStore = new TrayPointStore();
            SF.alarmInfoStore = new AlarmInfoStore();
            SF.communicationStore = new CommunicationConfigStore();
            SF.comm = new CommunicationHub();
            SF.plcStore = new PlcConfigStore();
            SF.plcRuntime = new PlcRuntimeService(SF.plcStore, SF.valueStore);
            SF.mainfrm = this;
            SF.versionService = new ConfigurationVersionService(SF.ConfigPath);
            EngineContext engineContext = new EngineContext
            {
                Procs = new List<Proc>(),
                ValueStore = SF.valueStore,
                DataStructStore = SF.dataStructStore,
                TrayPointStore = SF.trayPointStore,
                CardStore = SF.cardStore,
                Motion = motion,
                Io = io,
                Comm = SF.comm,
                CommunicationStore = SF.communicationStore,
                PlcRuntime = SF.plcRuntime,
                AlarmInfoStore = SF.alarmInfoStore,
                IoMap = frmIO.DicIO,
                Stations = frmCard.dataStation,
                SocketInfos = new List<SocketInfo>(),
                SerialPortInfos = new List<SerialPortInfo>(),
                CustomFunc = customFunc,
                AxisStatuses = new AxisStatusCache(),
                AxisMotionParameters = new AxisMotionParameterStore()
            };
            dataRun = new ProcessEngine(engineContext);
            ILogger uiLogger = new FrmInfoLogger(frmInfo);
            ILogger fileLogger = new LocalFileLogger(@"D:\AutomationLogs\ProcessLog");
            dataRun.Logger = new CompositeLogger(uiLogger, fileLogger);
            dataRun.AlarmHandler = new WinFormsAlarmHandler(this);
            dataRun.UiInvoker = this;
            dataRun.SnapshotChanged += CacheSnapshot;
            processTraceAuditSink = new ProcessTraceAuditSink(dataRun);
            SF.plcRuntime.RuntimeEvent += HandlePlcRuntimeEvent;
            SF.procStore = new ProcessEngineStore(dataRun);
            SF.frmMenu = frmMenu;
            SF.frmProc = frmProc;
            SF.frmDataGrid = frmDataGrid;
            SF.frmPropertyGrid = frmPropertyGrid;
            SF.frmToolBar = frmToolBar;
            SF.frmValue = frmValue;
            SF.frmValueDebug = frmValueDebug;
            SF.frmAiAssistant = frmAiAssistant;
            SF.frmIO = frmIO;
            SF.frmCard = frmCard;
            SF.DR = dataRun;
            SF.frmControl = frmControl;
            SF.frmStation = frmStation;
            SF.frmdataStruct = frmdataStruct;
            SF.motion = motion;
            SF.io = io;
            SF.frmIODebug = frmIODebug;
            SF.frmComunication = frmComunication;
            SF.frmState = frmState;
            SF.customFunc = customFunc;
            SF.frmAlarmConfig = frmAlarmConfig;
            SF.frmSearch = frmSearch;
            SF.frmSearch4Value = frmSearch4Value;
            SF.frmInfo = frmInfo;
            SF.frmPlc = frmPlc;
            automationBridgeHost = new AutomationBridgeHost(this);

            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            previousWindowState = WindowState;
            Resize += FrmMain_Resize;

            loadFillForm(MenuPanel, SF.frmMenu);
            loadFillForm(treeView_panel, SF.frmProc);
            loadFillForm(DataGrid_panel, SF.frmDataGrid);
            loadFillForm(propertyGrid_panel, SF.frmPropertyGrid);
            loadFillForm(ToolBar_panel, SF.frmToolBar);
            loadFillForm(state_panel, SF.frmState);
            loadFillForm(panel_Info, SF.frmInfo);

            // AI 助手面板挂到主窗体第一层，右侧全高停靠。
            // 这样 MenuPanel/state_panel/main_panel 都会让出右侧区域，AI 页面不再被顶部菜单和底部状态栏夹住。
            ai_panel = new Panel { Dock = DockStyle.Right, Width = 0, Visible = false, BackColor = Color.White };
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

        

        private void FrmMain_Load(object sender, EventArgs e)
        {
            InitializePlatform();
        }

        private void FrmMain_Shown(object sender, EventArgs e)
        {
            Text = AutomationRuntimeOptions.Current.IsSimulation
                ? "Automation - 仿真模式（未连接实机）"
                : "Automation";
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
                if (!ConfigurationBatchWriter.RecoverPendingTransactions(SF.ConfigPath, out string transactionError))
                {
                    SF.SetSecurityLock(transactionError);
                    SF.DR?.Logger?.Log($"配置事务恢复未完成，平台继续初始化并保持安全锁定：{transactionError}", LogLevel.Error);
                }
                if (!AiConfigurationTransaction.RecoverPendingTransactions(SF.ConfigPath, out string changeSetTransactionError))
                {
                    SF.SetSecurityLock(changeSetTransactionError);
                    SF.DR?.Logger?.Log($"ChangeSet事务恢复未完成，平台继续初始化并保持安全锁定：{changeSetTransactionError}", LogLevel.Error);
                }
                SF.frmValue.RefreshDic();
                EnsureSystemStatusVariables();
                InitializeSystemStatusValues();
                systemStatusReady = true;
                UpdateSystemStatusValue();
                if (!SF.cardStore.Load(SF.ConfigPath))
                {
                    SF.DR?.Logger?.Log("轴配置加载校验失败；MES/通讯流程仍可运行，运动指令将继续执行运行时门禁。", LogLevel.Error);
                }
                SF.frmIO.RefreshIOMap();
                SF.frmCard.RefreshStationList();
                SF.dataStructStore.Load(SF.ConfigPath);
                SF.frmdataStruct.RefreshDataSturctList();
                SF.frmIODebug.RefreshIODebugMap();
                if (!SF.communicationStore.Load(SF.ConfigPath, out string communicationError))
                {
                    SF.SetSecurityLock(communicationError);
                }
                SF.frmComunication.RefreshSocketMap();
                SF.frmComunication.RefreshSerialPortInfo();
                SF.frmAlarmConfig.RefreshAlarmInfo();
                SF.frmIODebug.RefleshIODebug();
                if (!SF.plcStore.Load(SF.ConfigPath, SF.valueStore, out string plcConfigError))
                {
                    SF.DR?.Logger?.Log(plcConfigError, LogLevel.Error);
                }
                if (!SF.plcRuntime.Initialize(out string plcRuntimeError))
                {
                    SF.DR?.Logger?.Log(plcRuntimeError, LogLevel.Error);
                }
                if (SF.DR?.Context != null)
                {
                    SF.DR.Context.Stations = SF.frmCard.dataStation;
                    SF.DR.Context.SocketInfos = SF.communicationStore.GetSocketSnapshot().ToList();
                    SF.DR.Context.SerialPortInfos = SF.communicationStore.GetSerialSnapshot().ToList();
                    SF.DR.Context.IoMap = SF.frmIO.DicIO;
                    SF.DR.Context.PlcRuntime = SF.plcRuntime;
                }
                SF.frmProc.RefreshProcList();
                if (AutomationRuntimeOptions.Current.IsSimulation)
                {
                    try
                    {
                        // 模拟器是可选辅助工具；未启动时快速返回，不能拖慢 HMI 与平台编辑器启动。
                        simulationGateway.Connect(250);
                        simulationGateway.ApplyEndpointMappings(SF.DR.Context);
                        Monitor();
                        Text = "Automation - 仿真模式（未连接实机）";
                        dataRun?.Logger?.Log($"仿真模式已就绪，使用正式配置目录:{SF.ConfigPath}", LogLevel.Normal);
                    }
                    catch (Exception ex)
                    {
                        string message = $"模拟器不可用:{ex.Message}";
                        Text = "Automation - 仿真模式（模拟器未连接）";
                        dataRun?.Logger?.Log(message, LogLevel.Error);
                    }
                }
                else
                {
                    int configuredCardCount = SF.cardStore?.GetControlCardCount() ?? 0;
                    if (configuredCardCount == 0)
                    {
                        dataRun?.Logger?.Log("未配置运动控制卡，已跳过运动控制卡初始化。", LogLevel.Normal);
                    }
                    else
                    {
                        try
                        {
                            SF.motion.InitCardType();
                            bool cardInitOk = SF.motion.InitCard();
                            if (cardInitOk)
                            {
                                SF.motion.DownLoadConfig();
                                SF.motion.SetAllAxisSevonOn();
                                SF.motion.SetAllAxisEquiv();
                                Monitor();
                            }
                            else
                            {
                                dataRun?.Logger?.Log("运动控制卡初始化失败，运动操作已禁用。", LogLevel.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            dataRun?.Logger?.Log($"运动控制卡初始化异常，运动操作已禁用:{ex.Message}", LogLevel.Error);
                        }
                    }
                }
                SF.frmControl?.RefreshMotionControlAvailability();
                platformInitialized = true;
                if (SF.SecurityLocked)
                {
                    string lockReason = string.IsNullOrWhiteSpace(SF.SecurityLockReason)
                        ? "系统处于安全锁定模式，禁止自动启动流程。"
                        : $"系统处于安全锁定模式，禁止自动启动流程。锁定原因：{SF.SecurityLockReason}";
                    SF.StopAllProcs(lockReason);
                }
            }
            catch (Exception ex)
            {
                StopAllProcsForSafety($"平台初始化失败:{ex.Message}");
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
        
        private async void StartMcpServerOnStartup()
        {
            string baseUri = GooseConfigStorage.CreateDefaultConfig().McpUri;
            string toolProfile = GooseConfigStorage.CreateDefaultConfig().ToolProfile;
            if (GooseConfigStorage.TryLoad(out GooseConfig config, out string loadError))
            {
                baseUri = config.McpUri;
                toolProfile = config.ToolProfile;
            }
            else if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo($"MCP Server：EW-AI 配置读取失败，使用默认 MCP 地址。{loadError}", FrmInfo.Level.Error);
            }

            try
            {
                string result = await automationMcpServerManager.EnsureStartedAsync(baseUri, toolProfile).ConfigureAwait(true);
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("MCP Server：" + result, FrmInfo.Level.Normal);
                }
            }
            catch (Exception ex)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("MCP Server 启动失败：" + ex.Message, FrmInfo.Level.Error);
                }
            }
        }

    
        public void Monitor()
        {
            ResetAxisRuntimeState();
            axisMonitorCts?.Cancel();
            axisMonitorCts = new CancellationTokenSource();
            CancellationToken token = axisMonitorCts.Token;
            axisMonitorTask = Task.Run(() =>
            {
                int pollCycle = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!SF.MotionConfigRestartRequired
                            && !(SF.ActiveEditSession?.Draft is FrmCard.CardHead)
                            && SF.frmCard != null)
                        {
                            if (SF.cardStore == null)
                            {
                                throw new InvalidOperationException("运动卡配置未初始化");
                            }
                            int cardCount = SF.cardStore.GetControlCardCount();
                            for (int i = 0; i < cardCount; i++)
                            {
                                int axisCount = SF.cardStore.GetAxisCount(i);
                                for (int j = 0; j < axisCount; j++)
                                {
                                    ushort card = (ushort)i;
                                    ushort axis = (ushort)j;
                                    uint ioStatus = motion.GetAxisIoStatus(card, axis);
                                    dataRun.Context.AxisStatuses.UpdateIo(card, axis, ioStatus);
                                    if (pollCycle % 10 == 0)
                                    {
                                        bool isStopped = motion.GetInPos(card, axis);
                                        bool isHomed = motion.HomeStatus(card, axis);
                                        bool servoOn = motion.GetAxisSevon(card, axis);
                                        double position = motion.GetAxisPos(card, axis);
                                        double speed = motion.GetAxisCurSpeed(card, axis);
                                        ushort alarmCode = (ioStatus & 1u) == 0
                                            ? (ushort)0
                                            : motion.GetAxisAlarmCode(card, axis);
                                        dataRun.Context.AxisStatuses.UpdateDetails(
                                            card, axis, isStopped, isHomed, servoOn, position, speed, alarmCode);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        HandleAxisMonitorFailure(ex);
                        break;
                    }
                    pollCycle = pollCycle == int.MaxValue ? 0 : pollCycle + 1;
                    if (token.WaitHandle.WaitOne(10))
                    {
                        break;
                    }
                }
            }, token);
        }

        public void ResetAxisRuntimeState()
        {
            dataRun?.Context?.AxisStatuses?.Clear();
            dataRun?.Context?.AxisMotionParameters?.Clear();
        }

        private void HandleAxisMonitorFailure(Exception ex)
        {
            string message = $"轴IO监视线程异常:{ex.Message}";
            StopAllProcsForSafety(message);
            TryStopMotion();
            dataRun?.Context?.AxisStatuses?.Clear();
            if (IsHandleCreated)
            {
                BeginInvoke((Action)(() => SF.frmControl?.RefreshMotionControlAvailability()));
            }
        }

        private void HandleSimulationGatewayFault(string reason)
        {
            string message = $"仿真连接故障:{reason}";
            Action stopAction = () =>
            {
                dataRun?.Context?.AxisStatuses?.Clear();
                StopAllProcsForSafety(message);
                SF.frmControl?.RefreshMotionControlAvailability();
            };
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(stopAction);
                return;
            }
            stopAction();
        }

        private void StopAllProcsForSafety(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                // Logger(FrmInfoLogger) 已会将消息转发到 FrmInfo.PrintInfo，
                // 这里不再直接调 PrintInfo，避免重复输出两条相同日志。
                dataRun?.Logger?.Log(reason, LogLevel.Error);
                if (dataRun?.Logger == null && frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo(reason, FrmInfo.Level.Error);
                }
            }
            if (SF.DR == null)
            {
                return;
            }
            int count = SF.frmProc?.procsList?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                SF.DR.Stop(i);
            }
        }

        private void TryStopMotion()
        {
            try
            {
                motion?.StopConnect();
            }
            catch (Exception ex)
            {
                string message = $"停止运动控制失败:{ex.Message}";
                // Logger 已会转发到 FrmInfo，仅在 Logger 为空时直接 PrintInfo。
                dataRun?.Logger?.Log(message, LogLevel.Error);
                if (dataRun?.Logger == null && frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo(message, FrmInfo.Level.Error);
                }
            }
        }

        private void WaitForAllProcsStopped(int timeoutMs)
        {
            if (SF.DR == null)
            {
                return;
            }
            int count = SF.frmProc?.procsList?.Count ?? 0;
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
                    EngineSnapshot snapshot = SF.DR.GetSnapshot(i);
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
                snapshotDirty.Add(snapshot.ProcId);
            }
            if (snapshot.State == ProcRunState.Stopped
                && (previous == null || previous.State != ProcRunState.Stopped))
            {
                CloseProcPopups(snapshot.ProcIndex);
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
                foreach (EngineSnapshot snapshot in pending)
                {
                    UpdateProcText(snapshot);
                }
            }
            UpdateHighlightFromCache();
            UpdateSystemStatusValue();
            TryStartAutoProcessesAfterReset();
        }

        private sealed class ProcPopupRequest
        {
            public int ProcIndex { get; set; }
            public Func<Message> CreateDialog { get; set; }
            public Action<Exception> Failed { get; set; }
            public Action Canceled { get; set; }
            public Message Dialog { get; set; }
        }

        internal void NotifyProcessInteractionUiReady()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                if (uiDispatcher == null || uiDispatcher.IsDisposed || !uiDispatcher.IsHandleCreated)
                {
                    return;
                }
                try
                {
                    uiDispatcher.BeginInvoke((Action)NotifyProcessInteractionUiReady);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            processInteractionUiReady = true;
            ShowPendingProcPopups();
            TryStartAutoProcessesAfterReset();
        }

        private void TryStartAutoProcessesAfterReset()
        {
            if (!processInteractionUiReady || !platformInitialized
                || SF.DR == null || !SF.DR.TryValidateStartGate(out _))
            {
                autoStartTriggeredForCurrentReset = false;
                return;
            }
            if (autoStartTriggeredForCurrentReset)
            {
                return;
            }
            autoStartTriggeredForCurrentReset = true;
            if (SF.frmProc?.procsList == null)
            {
                return;
            }
            for (int i = 0; i < SF.frmProc.procsList.Count; i++)
            {
                Proc proc = SF.DR.Context?.Procs != null && i < SF.DR.Context.Procs.Count
                    ? SF.DR.Context.Procs[i]
                    : SF.frmProc.procsList[i];
                if (proc?.head?.AutoStart != true || proc.head.Disable)
                {
                    continue;
                }
                EngineSnapshot snapshot = SF.DR.GetSnapshot(i);
                if (snapshot == null || snapshot.State == ProcRunState.Stopped)
                {
                    SF.DR.StartProcAuto(proc, i);
                }
            }
        }

        private void UpdateHighlightFromCache()
        {
            if (SF.frmDataGrid == null || SF.frmProc == null)
            {
                return;
            }
            int selectedProc = SF.frmProc.SelectedProcNum;
            EngineSnapshot snapshot = null;
            Guid procId = Guid.Empty;
            if (SF.frmProc.procsList != null && selectedProc >= 0 && selectedProc < SF.frmProc.procsList.Count)
            {
                procId = SF.frmProc.procsList[selectedProc]?.head?.Id ?? Guid.Empty;
            }
            if (procId != Guid.Empty)
            {
                lock (snapshotLock)
                {
                    snapshotCache.TryGetValue(procId, out snapshot);
                }
            }
            if (snapshot == null && SF.DR != null && selectedProc >= 0)
            {
                EngineSnapshot direct = SF.DR.GetSnapshot(selectedProc);
                if (direct != null && (procId == Guid.Empty || direct.ProcId == procId))
                {
                    snapshot = direct;
                }
            }
            SF.frmDataGrid.UpdateHighlight(snapshot);
        }

        private void UpdateSystemStatusValue()
        {
            if (!systemStatusReady || systemStatusFaulted)
            {
                return;
            }
            if (SF.valueStore == null)
            {
                FailSystemStatus("变量库未初始化，无法更新系统状态。");
                return;
            }
            if (SF.DR == null)
            {
                FailSystemStatus("流程引擎未初始化，无法更新系统状态。");
                return;
            }
            if (!SF.valueStore.TryGetValueByName(ResetStatusValueName, out DicValue resetValue) || resetValue == null)
            {
                FailSystemStatus($"缺少变量：{ResetStatusValueName}");
                return;
            }
            if (resetValue.Type != "double")
            {
                FailSystemStatus($"变量“{ResetStatusValueName}”类型不是double。");
                return;
            }
            if (!double.TryParse(resetValue.Value, out double resetRaw))
            {
                FailSystemStatus($"变量“{ResetStatusValueName}”数值无效:{resetValue.Value}");
                return;
            }
            if (resetRaw != 0d && resetRaw != 1d && resetRaw != 2d)
            {
                FailSystemStatus($"变量“{ResetStatusValueName}”取值超出定义:{resetRaw}");
                return;
            }
            ResetStatus resetStatus = (ResetStatus)(int)resetRaw;

            if (!SF.valueStore.TryGetValueByName(SystemStatusValueName, out DicValue systemValue) || systemValue == null)
            {
                FailSystemStatus($"缺少变量：{SystemStatusValueName}");
                return;
            }
            if (systemValue.Type != "double")
            {
                FailSystemStatus($"变量“{SystemStatusValueName}”类型不是double。");
                return;
            }

            SystemStatus targetStatus = CalculateSystemStatus(resetStatus);
            double targetValue = (double)targetStatus;
            if (double.TryParse(systemValue.Value, out double currentValue) && currentValue == targetValue)
            {
                return;
            }
            if (!SF.valueStore.setValueByName(SystemStatusValueName, targetValue, "系统状态自动更新"))
            {
                FailSystemStatus($"写入变量“{SystemStatusValueName}”失败。");
            }
        }

        private void EnsureSystemStatusVariables()
        {
            bool createdAny = EnsureSystemValue(ResetStatusValueName, "系统保留变量：复位状态");
            if (!systemStatusFaulted)
            {
                createdAny = EnsureSystemValue(SystemStatusValueName, "系统保留变量：系统状态") || createdAny;
            }
            if (!createdAny || systemStatusFaulted)
            {
                return;
            }
            SF.valueStore.Save(SF.ConfigPath);
            SF.frmValue?.FreshFrmValue();
        }

        private bool EnsureSystemValue(string valueName, string note)
        {
            if (SF.valueStore == null)
            {
                FailSystemStatus("变量库未初始化，无法补齐系统状态变量。");
                return false;
            }
            if (SF.valueStore.TryGetValueByName(valueName, out DicValue existingValue) && existingValue != null)
            {
                return false;
            }
            for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
            {
                if (SF.valueStore.TryGetValueByIndex(i, out _))
                {
                    continue;
                }
                if (!SF.valueStore.TrySetValue(i, valueName, "double", "0", note, "系统保留变量初始化"))
                {
                    FailSystemStatus($"创建系统保留变量失败：{valueName}");
                    return false;
                }
                dataRun?.Logger?.Log($"已补齐系统保留变量：{valueName}", LogLevel.Normal);
                return true;
            }
            FailSystemStatus($"变量表已满，无法创建系统保留变量：{valueName}");
            return false;
        }

        private void InitializeSystemStatusValues()
        {
            if (SF.valueStore == null)
            {
                FailSystemStatus("变量库未初始化，无法初始化复位状态。");
                return;
            }
            if (!SF.valueStore.TryGetValueByName(ResetStatusValueName, out DicValue resetValue) || resetValue == null)
            {
                FailSystemStatus($"缺少变量：{ResetStatusValueName}");
                return;
            }
            if (resetValue.Type != "double")
            {
                FailSystemStatus($"变量“{ResetStatusValueName}”类型不是double。");
                return;
            }
            if (!SF.valueStore.setValueByName(ResetStatusValueName, 0d, "复位状态初始化"))
            {
                FailSystemStatus($"写入变量“{ResetStatusValueName}”失败。");
            }
        }

        private SystemStatus CalculateSystemStatus(ResetStatus resetStatus)
        {
            bool hasRunning = false;
            bool hasPaused = false;
            bool hasAlarm = false;

            IReadOnlyList<EngineSnapshot> snapshots = SF.DR.GetSnapshots();
            if (snapshots != null)
            {
                foreach (EngineSnapshot snapshot in snapshots)
                {
                    if (snapshot == null)
                    {
                        continue;
                    }
                    bool isSystemProc = IsSystemProc(snapshot);
                    switch (snapshot.State)
                    {
                        case ProcRunState.Alarming:
                            hasAlarm = true;
                            break;
                        case ProcRunState.Paused:
                            hasPaused = true;
                            break;
                        case ProcRunState.Pausing:
                        case ProcRunState.Stopping:
                        case ProcRunState.Running:
                        case ProcRunState.SingleStep:
                            if (!isSystemProc)
                            {
                                hasRunning = true;
                            }
                            break;
                    }
                }
            }

            if (Volatile.Read(ref popupAlarmCount) > 0)
            {
                return SystemStatus.PopupAlarm;
            }
            if (hasAlarm)
            {
                return SystemStatus.ProcAlarm;
            }
            if (hasPaused)
            {
                return SystemStatus.Paused;
            }
            if (hasRunning)
            {
                return SystemStatus.Working;
            }
            if (resetStatus == ResetStatus.ResetCompleted)
            {
                return SystemStatus.Ready;
            }
            return SystemStatus.Uninitialized;
        }

        private bool IsSystemProc(EngineSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            string procName = snapshot.ProcName;
            if (string.IsNullOrWhiteSpace(procName))
            {
                int index = snapshot.ProcIndex;
                if (index >= 0 && SF.DR?.Context?.Procs != null && index < SF.DR.Context.Procs.Count)
                {
                    procName = SF.DR.Context.Procs[index]?.head?.Name;
                }
            }
            if (string.IsNullOrWhiteSpace(procName))
            {
                int index = snapshot.ProcIndex;
                if (index >= 0 && SF.frmProc?.procsList != null && index < SF.frmProc.procsList.Count)
                {
                    procName = SF.frmProc.procsList[index]?.head?.Name;
                }
            }
            return !string.IsNullOrEmpty(procName) && procName.StartsWith("系统", StringComparison.Ordinal);
        }

        private void FailSystemStatus(string message)
        {
            if (systemStatusFaulted)
            {
                return;
            }
            systemStatusFaulted = true;
            SF.SetSecurityLock(message);
        }

        internal void OnPopupAlarmStarted()
        {
            Interlocked.Increment(ref popupAlarmCount);
        }

        internal void OnPopupAlarmCompleted()
        {
            int current = Interlocked.Decrement(ref popupAlarmCount);
            if (current < 0)
            {
                Interlocked.Exchange(ref popupAlarmCount, 0);
            }
        }

        internal static bool IsPopupAlarmType(string alarmType)
        {
            return string.Equals(alarmType, "弹框确定", StringComparison.Ordinal)
                || string.Equals(alarmType, "弹框确定与否", StringComparison.Ordinal)
                || string.Equals(alarmType, "弹框确定与否与取消", StringComparison.Ordinal);
        }

        internal void RegisterProcPopup(int procIndex, Message dialog, Action closeAction)
        {
            if (procIndex < 0 || dialog == null || closeAction == null)
            {
                return;
            }
            ProcPopupItem item = new ProcPopupItem(dialog, closeAction);
            lock (popupLock)
            {
                if (!procPopups.TryGetValue(procIndex, out List<ProcPopupItem> list))
                {
                    list = new List<ProcPopupItem>();
                    procPopups[procIndex] = list;
                }
                list.Add(item);
            }
            dialog.FormClosed += (sender, args) => UnregisterProcPopup(procIndex, item);
        }

        internal void EnqueueProcPopup(
            int procIndex,
            Func<Message> createDialog,
            Action<Exception> failed,
            Action canceled)
        {
            if (procIndex < 0 || createDialog == null)
            {
                failed?.Invoke(new InvalidOperationException("流程弹框请求无效。"));
                return;
            }
            var request = new ProcPopupRequest
            {
                ProcIndex = procIndex,
                CreateDialog = createDialog,
                Failed = failed,
                Canceled = canceled
            };
            void EnqueueOnUiThread()
            {
                if (IsDisposed || Disposing || Volatile.Read(ref shutdownStarted) != 0)
                {
                    InvokePopupCallback(request.Canceled, "取消已关闭平台的流程弹框");
                    return;
                }
                if (!processInteractionUiReady)
                {
                    lock (popupLock)
                    {
                        pendingProcPopups.Enqueue(request);
                    }
                    return;
                }
                ShowProcPopup(request);
            }
            if (Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                if (uiDispatcher == null || uiDispatcher.IsDisposed || !uiDispatcher.IsHandleCreated)
                {
                    InvokePopupFailure(request, new InvalidOperationException("流程弹框 UI 调度句柄尚未创建。"));
                    return;
                }
                try
                {
                    uiDispatcher.BeginInvoke((Action)EnqueueOnUiThread);
                }
                catch (Exception ex)
                {
                    InvokePopupFailure(request, ex);
                }
            }
            else
            {
                EnqueueOnUiThread();
            }
        }

        private void ShowPendingProcPopups()
        {
            if (!processInteractionUiReady || IsDisposed || Disposing
                || Volatile.Read(ref shutdownStarted) != 0)
            {
                return;
            }
            if (Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                if (uiDispatcher == null || uiDispatcher.IsDisposed || !uiDispatcher.IsHandleCreated)
                {
                    return;
                }
                try
                {
                    uiDispatcher.BeginInvoke((Action)ShowPendingProcPopups);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            List<ProcPopupRequest> requests;
            lock (popupLock)
            {
                requests = pendingProcPopups.ToList();
                pendingProcPopups.Clear();
            }
            foreach (ProcPopupRequest request in requests)
            {
                ShowProcPopup(request);
            }
        }

        private void ShowProcPopup(ProcPopupRequest request)
        {
            try
            {
                Message dialog = request.CreateDialog();
                if (dialog == null)
                {
                    throw new InvalidOperationException("流程弹框工厂未返回窗体。");
                }
                request.Dialog = dialog;
                int visiblePopupCount;
                lock (popupLock)
                {
                    visiblePopupCount = procPopups.Values.Sum(list => list.Count);
                }
                RegisterProcPopup(request.ProcIndex, dialog, () =>
                {
                    InvokePopupCallback(request.Canceled, "关闭流程弹框");
                    if (!dialog.IsDisposed && !dialog.Disposing)
                    {
                        dialog.btnCanel();
                    }
                });
                dialog.PresentDeferred(false);
                if (visiblePopupCount > 0)
                {
                    Rectangle workingArea = Screen.FromControl(dialog).WorkingArea;
                    int offset = (visiblePopupCount % 6) * 32;
                    int left = Math.Min(workingArea.Right - dialog.Width,
                        Math.Max(workingArea.Left, dialog.Left + offset));
                    int top = Math.Min(workingArea.Bottom - dialog.Height,
                        Math.Max(workingArea.Top, dialog.Top + offset));
                    dialog.Location = new Point(left, top);
                }
            }
            catch (Exception ex)
            {
                if (request.Dialog != null && !request.Dialog.IsDisposed)
                {
                    try
                    {
                        request.Dialog.Close();
                        request.Dialog.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        dataRun?.Logger?.Log($"流程弹框清理失败:{cleanupException.Message}", LogLevel.Error);
                    }
                }
                InvokePopupFailure(request, ex);
            }
        }

        private void InvokePopupFailure(ProcPopupRequest request, Exception exception)
        {
            if (exception != null)
            {
                dataRun?.Logger?.Log($"流程弹框显示失败:{exception}", LogLevel.Error);
            }
            try
            {
                request?.Failed?.Invoke(exception ?? new InvalidOperationException("流程弹框失败。"));
            }
            catch (Exception callbackError)
            {
                dataRun?.Logger?.Log($"流程弹框失败回调异常:{callbackError.Message}", LogLevel.Error);
            }
        }

        private void InvokePopupCallback(Action callback, string action)
        {
            try
            {
                callback?.Invoke();
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"{action}回调异常:{ex.Message}", LogLevel.Error);
            }
        }

        private void UnregisterProcPopup(int procIndex, ProcPopupItem item)
        {
            lock (popupLock)
            {
                if (!procPopups.TryGetValue(procIndex, out List<ProcPopupItem> list))
                {
                    return;
                }
                list.Remove(item);
                if (list.Count == 0)
                {
                    procPopups.Remove(procIndex);
                }
            }
        }

        private void CloseProcPopups(int procIndex)
        {
            List<ProcPopupItem> items = null;
            List<ProcPopupRequest> canceledRequests = null;
            lock (popupLock)
            {
                if (procPopups.TryGetValue(procIndex, out List<ProcPopupItem> list))
                {
                    items = new List<ProcPopupItem>(list);
                }
                if (pendingProcPopups.Count > 0)
                {
                    canceledRequests = new List<ProcPopupRequest>();
                    var retained = new Queue<ProcPopupRequest>();
                    while (pendingProcPopups.Count > 0)
                    {
                        ProcPopupRequest request = pendingProcPopups.Dequeue();
                        if (request.ProcIndex == procIndex) canceledRequests.Add(request);
                        else retained.Enqueue(request);
                    }
                    while (retained.Count > 0) pendingProcPopups.Enqueue(retained.Dequeue());
                }
            }
            foreach (ProcPopupRequest request in canceledRequests ?? new List<ProcPopupRequest>())
            {
                InvokePopupCallback(request.Canceled, "取消排队中的流程弹框");
            }
            foreach (ProcPopupItem item in items ?? new List<ProcPopupItem>())
            {
                InvokePopupCallback(item?.CloseAction, "关闭活动流程弹框");
            }
        }

        private void CloseAllProcPopups()
        {
            List<int> procIndexes;
            lock (popupLock)
            {
                procIndexes = new List<int>(procPopups.Keys);
            }
            foreach (int procIndex in procIndexes)
            {
                CloseProcPopups(procIndex);
            }
            List<ProcPopupRequest> remaining;
            lock (popupLock)
            {
                remaining = pendingProcPopups.ToList();
                pendingProcPopups.Clear();
            }
            foreach (ProcPopupRequest request in remaining)
            {
                InvokePopupCallback(request.Canceled, "取消全部排队流程弹框");
            }
        }

        private void UpdateProcText(EngineSnapshot snapshot)
        {
            if (SF.frmProc?.proc_treeView == null || SF.frmProc.procsList == null)
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
                && SF.frmProc.TryGetProcNode(snapshot.ProcId, out TreeNode mappedNode, out int mappedIndex))
            {
                procNum = mappedIndex;
                targetNode = mappedNode;
            }
            else
            {
                if (procNum < 0 || procNum >= SF.frmProc.procsList.Count || procNum >= SF.frmProc.proc_treeView.Nodes.Count)
                {
                    return;
                }
                targetNode = SF.frmProc.proc_treeView.Nodes[procNum];
            }
            if (procNum < 0 || procNum >= SF.frmProc.procsList.Count || targetNode == null)
            {
                return;
            }

            void ApplyProcText()
            {
                bool isDisabled = procNum >= 0
                    && procNum < SF.frmProc.procsList.Count
                    && SF.frmProc.procsList[procNum]?.head?.Disable == true;
                Proc proc = SF.frmProc.procsList[procNum];
                string nextText = SF.frmProc.BuildProcNodeTextWithState(procNum, proc, snapshot);
                Color nextColor;
                if (isDisabled)
                {
                    nextColor = Color.Gainsboro;
                }
                else
                {
                    switch (snapshot.State)
                    {
                        case ProcRunState.Running:
                            nextColor = Color.ForestGreen;
                            break;
                        case ProcRunState.Paused:
                            nextColor = Color.Goldenrod;
                            break;
                        case ProcRunState.SingleStep:
                            nextColor = Color.DodgerBlue;
                            break;
                        case ProcRunState.Alarming:
                            nextColor = Color.Red;
                            break;
                        case ProcRunState.Pausing:
                            nextColor = Color.DarkOrange;
                            break;
                        case ProcRunState.Stopping:
                            nextColor = Color.DarkRed;
                            break;
                        case ProcRunState.Stopped:
                            nextColor = Color.Black;
                            break;
                        default:
                            nextColor = Color.Black;
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
                SF.frmProc.UpdateProcStateIcons(procNum, snapshot);
            }
            if (SF.frmProc.proc_treeView.InvokeRequired)
            {
                SF.frmProc.proc_treeView.BeginInvoke((Action)ApplyProcText);
            }
            else
            {
                ApplyProcText();
            }

            if (SF.frmToolBar?.btnPause != null && procNum == SF.frmProc.SelectedProcNum)
            {
                string buttonText = (snapshot.State == ProcRunState.Running || snapshot.State == ProcRunState.Alarming) ? "暂停" : "继续";
                bool allowResume = snapshot.State != ProcRunState.Paused;
                bool allowSingleStep = snapshot.State == ProcRunState.SingleStep;
                void ApplyPauseText()
                {
                    SF.frmToolBar.btnPause.Text = buttonText;
                    SF.frmToolBar.btnPause.Enabled = allowResume;
                    SF.frmToolBar.SingleRun.Enabled = allowSingleStep;
                }
                if (SF.frmToolBar.btnPause.InvokeRequired)
                {
                    SF.frmToolBar.btnPause.BeginInvoke((Action)ApplyPauseText);
                }
                else
                {
                    ApplyPauseText();
                }
            }
        }

        public void loadFillForm(Panel panel, System.Windows.Forms.Form frm)
        {
            if (frm != null && panel != null)
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
        }

        public bool AreAllProcessesStopped()
        {
            if (SF.frmProc?.procsList == null || SF.DR == null)
            {
                return false;
            }
            for (int i = 0; i < SF.frmProc.procsList.Count; i++)
            {
                EngineSnapshot snapshot = SF.DR.GetSnapshot(i);
                if (snapshot == null || snapshot.State != ProcRunState.Stopped)
                {
                    return false;
                }
            }
            return true;
        }

        public void ReloadProcessVersionedConfiguration()
        {
            SF.valueStore.Load(SF.ConfigPath);
            SF.frmValue.RefreshDic();
            SF.dataStructStore.Load(SF.ConfigPath);
            SF.frmdataStruct.RefreshDataSturctList();
            SF.frmProc.RefreshProcList();
            if (SF.DR?.Context != null)
            {
                SF.DR.Context.ValueStore = SF.valueStore;
                SF.DR.Context.DataStructStore = SF.dataStructStore;
            }
        }

        public void RequireRestartAfterEquipmentRestore()
        {
            SF.VersionRestartRequired = true;
            SF.StopAllProcs("设备配置已还原，必须重启程序后才能继续运行。");
        }

        private void InitializeWorkspacePageHost()
        {
            Controls.Remove(state_panel);
            editorWorkspacePage = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(246, 249, 251)
            };
            editorWorkspacePage.Controls.Add(DataGrid_panel);
            editorWorkspacePage.Controls.Add(panel_Info);
            editorWorkspacePage.Controls.Add(propertyGrid_panel);
            editorWorkspacePage.Controls.Add(treeView_panel);
            editorWorkspacePage.Controls.Add(ToolBar_panel);
            editorWorkspacePage.Controls.Add(state_panel);

            workspacePageHost = new WorkspacePageHost
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(246, 249, 251)
            };
            main_panel.Controls.Add(workspacePageHost);
            workspacePageHost.ShowPage(editorWorkspacePage);
        }

        public void ShowEditorWorkspace()
        {
            workspacePageHost.ShowPage(editorWorkspacePage);
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

        public void RequireRestartAfterMotionConfigurationChange()
        {
            SF.MotionConfigRestartRequired = true;
            SF.frmControl?.RefreshMotionControlAvailability();
            const string message = "运动设备配置已保存，重启程序后生效；重启前轴运动不可用，MES、通讯及其他非运动流程可继续运行。";
            dataRun?.Logger?.Log(message, LogLevel.Normal);
            if (frmInfo != null && !frmInfo.IsDisposed)
            {
                frmInfo.PrintInfo(message, FrmInfo.Level.Normal);
            }
        }

        private void FrmMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F && e.Control)
            {
                if(SF.curPage == 0)
                {
                    SF.frmToolBar.btnSearch.PerformClick();
                }
                e.Handled = true; // 防止其他控件处理该键按下事件
            }
            if (e.KeyCode == Keys.D && e.Control)
            {
                if (SF.curPage != 0)
                {
                    if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                    {
                        SF.frmInfo.PrintInfo("快捷键：仅支持在流程界面设为启动点。", FrmInfo.Level.Error);
                    }
                    e.Handled = true;
                    return;
                }

                if (SF.frmProc == null || SF.frmDataGrid == null || SF.DR == null)
                {
                    if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                    {
                        SF.frmInfo.PrintInfo("快捷键：流程组件未就绪，无法设为启动点。", FrmInfo.Level.Error);
                    }
                    e.Handled = true;
                    return;
                }

                int procIndex = SF.frmProc.SelectedProcNum;
                int stepIndex = SF.frmProc.SelectedStepNum;
                int opIndex = SF.frmDataGrid.iSelectedRow;
                if (opIndex < 0 && SF.frmDataGrid.dataGridView1.CurrentIndex >= 0)
                {
                    opIndex = SF.frmDataGrid.dataGridView1.CurrentIndex;
                    SF.frmDataGrid.iSelectedRow = opIndex;
                }

                if (procIndex < 0 || stepIndex < 0 || opIndex < 0 || opIndex >= SF.frmDataGrid.dataGridView1.OperationCount)
                {
                    if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                    {
                        SF.frmInfo.PrintInfo("快捷键：未选择指令，无法设为启动点。", FrmInfo.Level.Error);
                    }
                    e.Handled = true;
                    return;
                }

                string opName = null;
                if (SF.frmProc?.procsList != null && procIndex >= 0 && procIndex < SF.frmProc.procsList.Count)
                {
                    Step step = SF.frmProc.procsList[procIndex]?.steps?[stepIndex];
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
                EngineSnapshot startSnapshot = SF.DR.GetSnapshot(procIndex);
                if (startSnapshot != null && startSnapshot.State == ProcRunState.Paused)
                {
                    startState = ProcRunState.Paused;
                }

                SF.DR.Stop(procIndex);
                SF.DR.StartProcAt(
                    null,
                    procIndex,
                    stepIndex,
                    opIndex,
                    startState);

                if (SF.frmToolBar != null && !SF.frmToolBar.IsDisposed)
                {
                    SF.frmToolBar.btnPause.Text = "继续";
                    SF.frmToolBar.btnPause.Enabled = startState != ProcRunState.Paused;
                    SF.frmToolBar.SingleRun.Enabled = startState == ProcRunState.SingleStep;
                }

                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo($"快捷键：{procIndex}-{stepIndex}-{opIndex} {opName} 设为启动点", FrmInfo.Level.Normal);
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

            processInteractionUiReady = false;
            StopAllProcsForSafety("系统关闭，停止所有流程。");
            // 必须先释放 Goose 客户端：Kill Goose 进程后，后台读取线程不再调用
            // HandlePermissionRequest 的同步 Invoke，避免与已阻塞的 UI 线程形成死锁导致程序关闭卡住。
            try
            {
                SF.frmAiAssistant?.DisposeGooseClient();
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

            axisMonitorCts?.Cancel();
            if (axisMonitorTask != null)
            {
                try
                {
                    axisMonitorTask.Wait(1000);
                }
                catch (Exception ex)
                {
                    dataRun?.Logger?.Log($"等待轴IO监视线程退出失败:{ex.Message}", LogLevel.Error);
                }
            }

            WaitForAllProcsStopped(2000);
            // 关闭所有残留的报警弹框，避免弹框持有引用导致 UI 线程阻塞。
            try
            {
                CloseAllProcPopups();
            }
            catch
            {
                // 关闭弹框失败不应阻塞程序退出
            }

            try
            {
                SF.valueStore?.Save(SF.ConfigPath);
                SF.dataStructStore?.Save(SF.ConfigPath);
                SF.alarmInfoStore?.Save(SF.ConfigPath);
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"保存运行配置失败:{ex.Message}", LogLevel.Error);
            }

            try
            {
                if (SF.plcRuntime != null)
                {
                    SF.plcRuntime.RuntimeEvent -= HandlePlcRuntimeEvent;
                    SF.plcRuntime.Dispose();
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
                if (SF.comm != null)
                {
                    Task commDisposeTask = Task.Run(() =>
                    {
                        try { SF.comm.Dispose(); }
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
            axisMonitorCts?.Dispose();
            axisMonitorCts = null;
            uiDispatcher?.Dispose();
            platformInitialized = false;
        }

        private void HandlePlcRuntimeEvent(object sender, PlcRuntimeEventArgs e)
        {
            if (e == null) return;
            void Report()
            {
                dataRun?.Logger?.Log($"PLC[{e.DeviceName}] {e.Message}", e.IsAlarm ? LogLevel.Error : LogLevel.Normal);
            }
            if (IsHandleCreated && InvokeRequired) BeginInvoke((Action)Report);
            else Report();
        }
    }

    public sealed class WinFormsAlarmHandler : IAlarmHandler
    {
        private readonly FrmMain owner;

        public WinFormsAlarmHandler(FrmMain owner)
        {
            this.owner = owner;
        }

        public Task<AlarmDecision> HandleAsync(AlarmContext context)
        {
            var tcs = new TaskCompletionSource<AlarmDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (context == null)
            {
                tcs.TrySetResult(AlarmDecision.Stop);
                return tcs.Task;
            }

            Message CreateDialog()
            {
                string title = $"发生报警:{context.ProcIndex}---{context.StepIndex}---{context.OpIndex}";
                Message dialog = null;
                switch (context.AlarmType)
                {
                    case "弹框确定":
                        dialog = new Message(title, context.Note, () => tcs.TrySetResult(AlarmDecision.Goto1),
                            context.Btn1, false, false);
                        break;
                    case "弹框确定与否":
                        dialog = new Message(title, context.Note,
                            () => tcs.TrySetResult(AlarmDecision.Goto1),
                            () => tcs.TrySetResult(AlarmDecision.Goto2),
                            context.Btn1, context.Btn2, false, false);
                        break;
                    case "弹框确定与否与取消":
                        dialog = new Message(title, context.Note,
                            () => tcs.TrySetResult(AlarmDecision.Goto1),
                            () => tcs.TrySetResult(AlarmDecision.Goto2),
                            () => tcs.TrySetResult(AlarmDecision.Goto3),
                            context.Btn1, context.Btn2, context.Btn3, false, false);
                        break;
                    default:
                        throw new InvalidOperationException($"不支持的流程报警弹框类型:{context.AlarmType}");
                }
                return dialog;
            }

            if (owner == null || owner.IsDisposed)
            {
                tcs.TrySetResult(AlarmDecision.Stop);
                return tcs.Task;
            }
            bool trackPopup = FrmMain.IsPopupAlarmType(context.AlarmType);
            if (trackPopup)
            {
                owner?.OnPopupAlarmStarted();
                tcs.Task.ContinueWith(_ => owner?.OnPopupAlarmCompleted(), TaskScheduler.Default);
            }
            owner.EnqueueProcPopup(
                context.ProcIndex,
                CreateDialog,
                ex => tcs.TrySetException(ex ?? new InvalidOperationException("流程报警弹框创建失败。")),
                () => tcs.TrySetResult(AlarmDecision.Stop));

            return tcs.Task;
        }
    }

    internal sealed class WorkspacePageHost : Panel
    {
        private Control activePage;

        public WorkspacePageHost()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
        }

        public void ShowPage(Control page)
        {
            if (page == null || page.IsDisposed || ReferenceEquals(activePage, page))
            {
                return;
            }
            SuspendLayout();
            try
            {
                if (!Controls.Contains(page))
                {
                    Controls.Add(page);
                }
                page.Dock = DockStyle.Fill;
                page.Visible = true;
                page.BringToFront();
                if (activePage != null && !activePage.IsDisposed)
                {
                    activePage.Visible = false;
                }
                activePage = page;
            }
            finally
            {
                ResumeLayout(true);
            }
            Invalidate(true);
        }
    }

    public sealed class FrmInfoLogger : ILogger
    {
        private readonly FrmInfo info;

        public FrmInfoLogger(FrmInfo info)
        {
            this.info = info;
        }

        public void Log(string message, LogLevel level)
        {
            if (info == null || info.IsDisposed)
            {
                return;
            }
            FrmInfo.Level uiLevel = level == LogLevel.Error ? FrmInfo.Level.Error : FrmInfo.Level.Normal;
            info.PrintInfo(message, uiLevel);
        }
    }
}
