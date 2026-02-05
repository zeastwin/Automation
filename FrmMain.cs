using Automation.MotionControl;
using csLTDMC;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
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
        public FrmAiAssistant frmAiAssistant;
        public FrmAccountManager frmAccountManager = new FrmAccountManager();
        public MotionCtrl motion = new MotionCtrl();
        private EngineSnapshot[] snapshotCache = Array.Empty<EngineSnapshot>();
        private bool[] snapshotDirty = Array.Empty<bool>();
        private readonly object snapshotLock = new object();
        private readonly object saveFileLock = new object();
        private System.Windows.Forms.Timer snapshotTimer;
        private CancellationTokenSource axisMonitorCts;
        private Task axisMonitorTask;
        private volatile bool axisMonitorFaulted;

        public FrmMain()
        {
            InitializeComponent();
            SF.cardStore = new CardConfigStore();
            SF.valueStore = new ValueConfigStore();
            SF.dataStructStore = new DataStructStore();
            SF.trayPointStore = new TrayPointStore();
            SF.alarmInfoStore = new AlarmInfoStore();
            SF.comm = new CommunicationHub();
            SF.plcStore = new PlcConfigStore();
            SF.mainfrm = this;
            EngineContext engineContext = new EngineContext
            {
                Procs = new List<Proc>(),
                ValueStore = SF.valueStore,
                DataStructStore = SF.dataStructStore,
                TrayPointStore = SF.trayPointStore,
                CardStore = SF.cardStore,
                Motion = motion,
                Comm = SF.comm,
                PlcStore = SF.plcStore,
                AlarmInfoStore = SF.alarmInfoStore,
                IoMap = frmIO.DicIO,
                Stations = frmCard.dataStation,
                SocketInfos = frmComunication.socketInfos,
                SerialPortInfos = frmComunication.serialPortInfos,
                CustomFunc = customFunc,
                AxisStateBitGetter = TryGetAxisStateBit
            };
            dataRun = new ProcessEngine(engineContext);
            dataRun.PermissionChecker = key => SF.HasPermission(key);
            ILogger uiLogger = new FrmInfoLogger(frmInfo);
            ILogger fileLogger = new LocalFileLogger(@"D:\AutomationLogs\ProcessLog");
            dataRun.Logger = new CompositeLogger(uiLogger, fileLogger);
            dataRun.AlarmHandler = new WinFormsAlarmHandler(this);
            dataRun.UiInvoker = this;
            dataRun.SnapshotChanged += CacheSnapshot;
            SF.procStore = new ProcessEngineStore(dataRun);
            SF.frmMenu = frmMenu;
            SF.frmProc = frmProc;
            SF.frmDataGrid = frmDataGrid;
            SF.frmPropertyGrid = frmPropertyGrid;
            SF.frmToolBar = frmToolBar;
            SF.frmValue = frmValue;
            SF.frmValueDebug = frmValueDebug;
            SF.frmIO = frmIO;
            SF.frmCard = frmCard;
            SF.DR = dataRun;
            SF.frmControl = frmControl;
            SF.frmStation = frmStation;
            SF.frmdataStruct = frmdataStruct;
            SF.motion = motion;
            SF.frmIODebug = frmIODebug;
            SF.frmComunication = frmComunication;
            SF.frmState = frmState;
            SF.customFunc = customFunc;
            SF.frmAlarmConfig = frmAlarmConfig;
            SF.frmSearch = frmSearch;
            SF.frmSearch4Value = frmSearch4Value;
            SF.frmInfo = frmInfo;
            SF.frmPlc = frmPlc;
            SF.frmAccountManager = frmAccountManager;
            if (SF.AiFlowEnabled)
            {
                frmAiAssistant = new FrmAiAssistant();
                SF.frmAiAssistant = frmAiAssistant;
            }
            else
            {
                SF.frmAiAssistant = null;
            }

            StartPosition = FormStartPosition.CenterScreen;

            loadFillForm(MenuPanel, SF.frmMenu);
            loadFillForm(treeView_panel, SF.frmProc);
            loadFillForm(DataGrid_panel, SF.frmDataGrid);
            loadFillForm(propertyGrid_panel, SF.frmPropertyGrid);
            loadFillForm(ToolBar_panel, SF.frmToolBar);
            loadFillForm(state_panel, SF.frmState);
            loadFillForm(panel_Info, SF.frmInfo);
            StartSnapshotTimer();
            SF.RefreshPermissionUi();
            UpdateTitleWithUser();
            Shown += (s, e) => SF.RefreshPermissionUi();
        }

        

        private void FrmMain_Load(object sender, EventArgs e)
        {
            SF.frmValue.RefreshDic();
            SF.cardStore.Load(SF.ConfigPath);
            SF.frmIO.RefreshIOMap();
            SF.frmCard.RefreshStationList();
            SF.dataStructStore.Load(SF.ConfigPath);
            SF.frmdataStruct.RefreshDataSturctList();
            SF.frmIODebug.RefreshIODebugMap();
            SF.frmComunication.RefreshSocketMap();
            SF.frmComunication.RefreshSerialPortInfo();
            SF.frmAlarmConfig.RefreshAlarmInfo();
            SF.frmIODebug.RefleshIODebug();
            SF.plcStore.Load(SF.ConfigPath);
            if (SF.DR?.Context != null)
            {
                SF.DR.Context.Stations = SF.frmCard.dataStation;
                SF.DR.Context.SocketInfos = SF.frmComunication.socketInfos;
                SF.DR.Context.SerialPortInfos = SF.frmComunication.serialPortInfos;
                SF.DR.Context.IoMap = SF.frmIO.DicIO;
                SF.DR.Context.PlcStore = SF.plcStore;
            }
            //初始化运动控制相关
            SF.motion.InitCardType();
            bool cardInitOk = SF.motion.InitCard();
            if (cardInitOk)
            {
                SF.motion.DownLoadConfig();
                SF.motion.SetAllAxisSevonOn();
                SF.motion.SetAllAxisEquiv();
                Monitor();
            }
            if (SF.SecurityLocked)
            {
                SF.StopAllProcs("账户系统锁定，禁止自动启动流程。");
                return;
            }
            if (SF.frmProc?.procsList != null && SF.frmProc.procsList.Count > 0)
            {
                for (int i = 0; i < SF.frmProc.procsList.Count; i++)
                {
                    Proc proc = SF.DR?.Context?.Procs != null && i >= 0 && i < SF.DR.Context.Procs.Count
                        ? SF.DR.Context.Procs[i]
                        : SF.frmProc.procsList[i];
                    if (proc?.head?.AutoStart != true)
                    {
                        continue;
                    }
                    if (proc?.head?.Disable == true)
                    {
                        continue;
                    }
                    EngineSnapshot snapshot = SF.DR.GetSnapshot(i);
                    if (snapshot != null && snapshot.State != ProcRunState.Stopped)
                    {
                        continue;
                    }
                    SF.DR.StartProcAuto(null, i);
                }
            }
        }
        
        public List<Dictionary<int, char[]>> StateDic = new List<Dictionary<int, char[]>>();
        public object StateDicLock { get; } = new object();

    
        public void Monitor()
        {
            ReflshDgv();
            axisMonitorFaulted = false;
            axisMonitorCts?.Cancel();
            axisMonitorCts = new CancellationTokenSource();
            CancellationToken token = axisMonitorCts.Token;
            axisMonitorTask = Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (SF.isModify != ModifyKind.ControlCard && SF.frmCard != null && !SF.frmCard.IsNewCard)
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
                                    uint number = csLTDMC.LTDMC.dmc_axis_io_status((ushort)i, (ushort)j);
                                    char[] state = Convert.ToString(number, 2).PadLeft(16, '0').ToCharArray();
                                    lock (StateDicLock)
                                    {
                                        if (i >= StateDic.Count)
                                        {
                                            continue;
                                        }
                                        Dictionary<int, char[]> axisStates = StateDic[i];
                                        if (axisStates == null)
                                        {
                                            axisStates = new Dictionary<int, char[]>();
                                            StateDic[i] = axisStates;
                                        }
                                        axisStates[j] = state;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        axisMonitorFaulted = true;
                        HandleAxisMonitorFailure(ex);
                        break;
                    }
                    if (token.WaitHandle.WaitOne(10))
                    {
                        break;
                    }
                }
            }, token);
        }

        public void UpdateTitleWithUser()
        {
            Text = "Automation";
        }
       
        public void ReflshDgv()
        {
            lock (StateDicLock)
            {
                StateDic.Clear();
                for (int i = 0; i < SF.cardStore.GetControlCardCount(); i++)
                {
                    Dictionary<int, char[]> dictionary1 = new Dictionary<int, char[]>();
                    StateDic.Add(dictionary1);
                }
            }
        }

        private void HandleAxisMonitorFailure(Exception ex)
        {
            string message = $"轴IO监视线程异常:{ex.Message}";
            StopAllProcsForSafety(message);
            TryStopMotion();
            lock (StateDicLock)
            {
                StateDic.Clear();
            }
        }

        private void StopAllProcsForSafety(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                dataRun?.Logger?.Log(reason, LogLevel.Error);
                if (frmInfo != null && !frmInfo.IsDisposed)
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
                dataRun?.Logger?.Log(message, LogLevel.Error);
                if (frmInfo != null && !frmInfo.IsDisposed)
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

        public bool TryGetAxisStateBit(ushort cardNum, ushort axis, int bitIndex)
        {
            if (bitIndex <= 0)
            {
                return false;
            }
            if (axisMonitorFaulted || axisMonitorCts == null || axisMonitorCts.IsCancellationRequested)
            {
                return false;
            }
            lock (StateDicLock)
            {
                if (cardNum >= StateDic.Count)
                {
                    return false;
                }
                Dictionary<int, char[]> axisStates = StateDic[cardNum];
                if (axisStates == null || !axisStates.TryGetValue(axis, out char[] state) || state == null)
                {
                    return false;
                }
                if (state.Length < bitIndex)
                {
                    return false;
                }
                return state[state.Length - bitIndex] == '1';
            }
        }

        private void CacheSnapshot(EngineSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }
            lock (snapshotLock)
            {
                EnsureSnapshotCapacity(snapshot.ProcIndex);
                snapshotCache[snapshot.ProcIndex] = snapshot;
                snapshotDirty[snapshot.ProcIndex] = true;
            }
        }

        private void EnsureSnapshotCapacity(int procIndex)
        {
            if (procIndex < 0)
            {
                return;
            }
            if (procIndex < snapshotCache.Length)
            {
                return;
            }
            int newSize = snapshotCache.Length == 0 ? 1 : snapshotCache.Length;
            while (newSize <= procIndex)
            {
                newSize *= 2;
            }
            Array.Resize(ref snapshotCache, newSize);
            Array.Resize(ref snapshotDirty, newSize);
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
                for (int i = 0; i < snapshotDirty.Length; i++)
                {
                    if (!snapshotDirty[i])
                    {
                        continue;
                    }
                    if (pending == null)
                    {
                        pending = new List<EngineSnapshot>();
                    }
                    pending.Add(snapshotCache[i]);
                    snapshotDirty[i] = false;
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
        }

        private void UpdateHighlightFromCache()
        {
            if (SF.frmDataGrid == null || SF.frmProc == null)
            {
                return;
            }
            int selectedProc = SF.frmProc.SelectedProcNum;
            EngineSnapshot snapshot = null;
            lock (snapshotLock)
            {
                if (selectedProc >= 0 && selectedProc < snapshotCache.Length)
                {
                    snapshot = snapshotCache[selectedProc];
                }
            }
            if (snapshot == null && SF.DR != null && selectedProc >= 0)
            {
                snapshot = SF.DR.GetSnapshot(selectedProc);
            }
            SF.frmDataGrid.UpdateHighlight(snapshot);
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
            if (procNum < 0 || procNum >= SF.frmProc.procsList.Count || procNum >= SF.frmProc.proc_treeView.Nodes.Count)
            {
                return;
            }

            string stateText;
            switch (snapshot.State)
            {
                case ProcRunState.Stopped:
                    stateText = "停止";
                    break;
                case ProcRunState.Paused:
                    stateText = "暂停";
                    break;
                case ProcRunState.SingleStep:
                    stateText = "单步";
                    break;
                case ProcRunState.Running:
                    stateText = "运行";
                    break;
                case ProcRunState.Alarming:
                    stateText = "报警中";
                    break;
                default:
                    stateText = "未知";
                    break;
            }

            string result = $"|{stateText}";
            if (snapshot.IsBreakpoint)
            {
                result += "|断点";
            }
            void ApplyProcText()
            {
                string procName = snapshot.ProcName;
                if (string.IsNullOrEmpty(procName) && procNum < SF.frmProc.procsList.Count)
                {
                    procName = SF.frmProc.procsList[procNum].head.Name;
                }
                TreeNode node = SF.frmProc.proc_treeView.Nodes[procNum];
                bool isDisabled = procNum >= 0
                    && procNum < SF.frmProc.procsList.Count
                    && SF.frmProc.procsList[procNum]?.head?.Disable == true;
                string disabledTag = isDisabled ? "[禁用]" : string.Empty;
                node.Text = disabledTag + procName + result;
                if (isDisabled)
                {
                    node.ForeColor = Color.Gainsboro;
                }
                else
                {
                    switch (snapshot.State)
                    {
                        case ProcRunState.Running:
                            node.ForeColor = Color.ForestGreen;
                            break;
                        case ProcRunState.Paused:
                            node.ForeColor = Color.Goldenrod;
                            break;
                        case ProcRunState.SingleStep:
                            node.ForeColor = Color.DodgerBlue;
                            break;
                        case ProcRunState.Alarming:
                            node.ForeColor = Color.Red;
                            break;
                        case ProcRunState.Stopped:
                            node.ForeColor = Color.Black;
                            break;
                        default:
                            node.ForeColor = Color.Black;
                            break;
                    }
                }
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

        public T ReadJson<T>(string FilePath, string Name)
        {
            string strFilePath = Path.Combine(FilePath, Name + ".json");
            if (!File.Exists(strFilePath))
            {
                return default(T);
            }

            try
            {
                using (FileStream stream = new FileStream(strFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader r = new StreamReader(stream))
                {
                    string json = r.ReadToEnd();
                    var settings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All
                    };
                    return JsonConvert.DeserializeObject<T>(json, settings);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return default(T);
            }
        }

        public bool SaveAsJson<T>(string FilePath, string Name, T t)
        {
            string strFilePath = Path.Combine(FilePath, Name + ".json");
            string directory = Path.GetDirectoryName(strFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            string output = JsonConvert.SerializeObject(t, settings);
            Exception lastError = null;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                string tempPath = strFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    lock (saveFileLock)
                    {
                        File.WriteAllText(tempPath, output);
                        if (File.Exists(strFilePath))
                        {
                            FileAttributes attributes = File.GetAttributes(strFilePath);
                            if ((attributes & FileAttributes.ReadOnly) != 0)
                            {
                                File.SetAttributes(strFilePath, attributes & ~FileAttributes.ReadOnly);
                            }
                            File.Replace(tempPath, strFilePath, null, true);
                        }
                        else
                        {
                            File.Move(tempPath, strFilePath);
                        }
                    }
                    return true;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                    break;
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try
                        {
                            File.Delete(tempPath);
                        }
                        catch
                        {
                        }
                    }
                }

                Thread.Sleep(60 * (attempt + 1));
            }

            string reason = $"保存配置失败：{strFilePath}\r\n{lastError?.Message}";
            dataRun?.Logger?.Log(reason, LogLevel.Error);
            if (frmInfo != null && !frmInfo.IsDisposed)
            {
                frmInfo.PrintInfo(reason, FrmInfo.Level.Error);
            }
            MessageBox.Show(reason);
            return false;
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
                if (opIndex < 0 && SF.frmDataGrid.dataGridView1.CurrentCell != null)
                {
                    opIndex = SF.frmDataGrid.dataGridView1.CurrentCell.RowIndex;
                    SF.frmDataGrid.iSelectedRow = opIndex;
                }

                if (procIndex < 0 || stepIndex < 0 || opIndex < 0 || opIndex >= SF.frmDataGrid.dataGridView1.Rows.Count)
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
            if (e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult result = MessageBox.Show("确认退出程序？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
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

            StopAllProcsForSafety("系统关闭，停止所有流程。");
            WaitForAllProcsStopped(2000);
            try
            {
                SF.comm?.Dispose();
            }
            catch (Exception ex)
            {
                dataRun?.Logger?.Log($"关闭通讯失败:{ex.Message}", LogLevel.Error);
            }
            TryStopMotion();
            dataRun?.Dispose();

            if (SF.frmProc != null && SF.frmProc.isStopPointDirty)
            {
                if (!Directory.Exists(SF.workPath))
                {
                    Directory.CreateDirectory(SF.workPath);
                }
                for (int i = 0; i < SF.frmProc.procsList.Count; i++)
                {
                    SaveAsJson(SF.workPath, i.ToString(), SF.frmProc.procsList[i]);
                }
            }
            SF.valueStore.Save(SF.ConfigPath);
            SF.dataStructStore.Save(SF.ConfigPath);
            SF.alarmInfoStore.Save(SF.ConfigPath);
            if (snapshotTimer != null)
            {
                snapshotTimer.Stop();
                snapshotTimer.Tick -= SnapshotTimer_Tick;
                snapshotTimer.Dispose();
                snapshotTimer = null;
            }
        }
    }

    public sealed class WinFormsAlarmHandler : IAlarmHandler
    {
        private readonly Control invoker;

        public WinFormsAlarmHandler(Control invoker)
        {
            this.invoker = invoker;
        }

        public Task<AlarmDecision> HandleAsync(AlarmContext context)
        {
            var tcs = new TaskCompletionSource<AlarmDecision>();
            if (context == null)
            {
                tcs.TrySetResult(AlarmDecision.Stop);
                return tcs.Task;
            }

            void ShowDialog()
            {
                string title = $"发生报警:{context.ProcIndex}---{context.StepIndex}---{context.OpIndex}";
                switch (context.AlarmType)
                {
                    case "弹框确定":
                        new Message(title, context.Note, () => tcs.TrySetResult(AlarmDecision.Goto1), context.Btn1, false);
                        break;
                    case "弹框确定与否":
                        new Message(title, context.Note,
                            () => tcs.TrySetResult(AlarmDecision.Goto1),
                            () => tcs.TrySetResult(AlarmDecision.Goto2),
                            context.Btn1, context.Btn2, false);
                        break;
                    case "弹框确定与否与取消":
                        new Message(title, context.Note,
                            () => tcs.TrySetResult(AlarmDecision.Goto1),
                            () => tcs.TrySetResult(AlarmDecision.Goto2),
                            () => tcs.TrySetResult(AlarmDecision.Goto3),
                            context.Btn1, context.Btn2, context.Btn3, false);
                        break;
                    default:
                        tcs.TrySetResult(AlarmDecision.Stop);
                        break;
                }
            }

            if (invoker == null || invoker.IsDisposed)
            {
                tcs.TrySetResult(AlarmDecision.Stop);
                return tcs.Task;
            }
            if (invoker.InvokeRequired)
            {
                invoker.BeginInvoke((Action)ShowDialog);
            }
            else
            {
                ShowDialog();
            }

            return tcs.Task;
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
