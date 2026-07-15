using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Automation.MotionControl;
using static Automation.FrmProc;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
using static Automation.FrmCard;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Numerics;

namespace Automation
{
    public enum ProcRunState
    {
        Stopped = 0,
        Paused = 1,
        SingleStep = 2,
        Running = 3,
        Alarming = 4,
        Pausing = 5,
        Stopping = 6
    }

    public enum ProcTerminationReason
    {
        None = 0,
        Completed = 1,
        StopRequested = 2,
        Alarm = 3,
        Disabled = 4,
        TestWindowElapsed = 5,
        Restarted = 6,
        EngineDisposed = 7
    }

    //复位状态（变量表：复位状态）
    public enum ResetStatus
    {
        NotReset = 0,
        Resetting = 1,
        ResetCompleted = 2
    }

    //系统状态（变量表：系统状态）
    public enum SystemStatus
    {
        Uninitialized = 0,
        Paused = 1,
        Ready = 2,
        Working = 3,
        ProcAlarm = 4,
        PopupAlarm = 5
    }
    public class ProcHandle
    {
        private int state = (int)ProcRunState.Stopped;
        private int breakpointFlag;
        private int gotoFlag;
        private int pauseBySignalFlag;
        private int completionRequestedFlag;
        private string alarmMessage;
        private long appliedRevision;
        private int cooperativeOperationCount;
        private long cooperativeSliceStartTimestamp;

        public int procNum;
        public int stepNum;
        public int opsNum;

        public string procName;
        public Guid procId;

        // 工作线程和命令线程共享的唯一逻辑状态；对外读取统一使用 EngineSnapshot。
        public ProcRunState State
        {
            get => (ProcRunState)Volatile.Read(ref state);
            internal set
            {
                if (value < ProcRunState.Stopped || value > ProcRunState.Stopping)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                Volatile.Write(ref state, (int)value);
            }
        }
        public bool isBreakpoint
        {
            get => Volatile.Read(ref breakpointFlag) == 1;
            internal set => Volatile.Write(ref breakpointFlag, value ? 1 : 0);
        }
        public bool isGoto
        {
            get => Volatile.Read(ref gotoFlag) == 1;
            internal set => Volatile.Write(ref gotoFlag, value ? 1 : 0);
        }
        // 报警文本是报警结果的唯一数据源，避免布尔标志与文本互相矛盾。
        public bool HasAlarm => !string.IsNullOrWhiteSpace(alarmMsg);
        public string alarmMsg
        {
            get => Volatile.Read(ref alarmMessage);
            internal set => Volatile.Write(ref alarmMessage, value);
        }
        public bool IsSingleOperation { get; internal set; }
        // 取消状态直接来自 ProcessControl，不在句柄中重复保存令牌。
        public CancellationToken CancellationToken => Control?.CancellationToken ?? System.Threading.CancellationToken.None;
        internal ProcessControl Control { get; set; }
        public ConcurrentBag<Task> RunningTasks { get; } = new ConcurrentBag<Task>();
        internal ConcurrentDictionary<long, byte> OwnedAxes { get; } = new ConcurrentDictionary<long, byte>();
        public bool PauseBySignal
        {
            get => Volatile.Read(ref pauseBySignalFlag) == 1;
            internal set => Volatile.Write(ref pauseBySignalFlag, value ? 1 : 0);
        }
        public Proc Proc { get; set; }
        public long AppliedRevision
        {
            get => Interlocked.Read(ref appliedRevision);
            internal set => Interlocked.Exchange(ref appliedRevision, value);
        }
        public bool CompletionRequested
        {
            get => Volatile.Read(ref completionRequestedFlag) == 1;
            internal set => Volatile.Write(ref completionRequestedFlag, value ? 1 : 0);
        }

        public ProcTerminationReason TerminationReason { get; internal set; }

        internal int CooperativeOperationCount
        {
            get => cooperativeOperationCount;
            set => cooperativeOperationCount = value;
        }

        internal long CooperativeSliceStartTimestamp
        {
            get => cooperativeSliceStartTimestamp;
            set => cooperativeSliceStartTimestamp = value;
        }

    }
    public enum EngineCommandType
    {
        StartAt = 1,
        RunSingleOpOnce = 2,
        Pause = 3,
        Resume = 4,
        Step = 5,
        Stop = 6
    }

    public sealed class EngineCommand
    {
        private EngineCommand(EngineCommandType type, int procIndex, Proc proc, int stepIndex, int opIndex,
            ProcRunState startState)
        {
            Type = type;
            ProcIndex = procIndex;
            Proc = proc;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            StartState = startState;
        }

        public EngineCommandType Type { get; }
        public int ProcIndex { get; }
        public Proc Proc { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public ProcRunState StartState { get; }
        internal long Generation { get; set; }
        internal TaskCompletionSource<bool> Completion { get; } =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public static EngineCommand Start(int procIndex, Proc proc, int stepIndex, int opIndex, ProcRunState startState)
        {
            return new EngineCommand(EngineCommandType.StartAt, procIndex, proc, stepIndex, opIndex, startState);
        }

        public static EngineCommand RunSingleOpOnce(int procIndex, Proc proc, int stepIndex, int opIndex)
        {
            return new EngineCommand(EngineCommandType.RunSingleOpOnce, procIndex, proc, stepIndex, opIndex,
                ProcRunState.SingleStep);
        }

        public static EngineCommand Pause(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Pause, procIndex, null, 0, 0, ProcRunState.Paused);
        }

        public static EngineCommand Resume(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Resume, procIndex, null, 0, 0, ProcRunState.Running);
        }

        public static EngineCommand Step(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Step, procIndex, null, 0, 0, ProcRunState.SingleStep);
        }

        public static EngineCommand Stop(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Stop, procIndex, null, 0, 0, ProcRunState.Stopped);
        }
    }
    public sealed class EngineSnapshot
    {
        // 不可变只读模型，供 UI、HMI、Bridge 和其他线程安全观察流程状态。
        public EngineSnapshot(int procIndex, Guid procId, string procName, ProcRunState state, int stepIndex, int opIndex,
            bool isBreakpoint, string alarmMessage, DateTime updateTime, long updateTicks,
            long publishedRevision = 0, long appliedRevision = 0)
            : this(procIndex, procId, procName, state, stepIndex, opIndex, isBreakpoint, alarmMessage,
                updateTime, updateTicks, publishedRevision, appliedRevision, ProcTerminationReason.None)
        {
        }

        public EngineSnapshot(int procIndex, Guid procId, string procName, ProcRunState state, int stepIndex, int opIndex,
            bool isBreakpoint, string alarmMessage, DateTime updateTime, long updateTicks,
            long publishedRevision, long appliedRevision, ProcTerminationReason terminationReason)
        {
            ProcIndex = procIndex;
            ProcId = procId;
            ProcName = procName;
            State = state;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            IsBreakpoint = isBreakpoint;
            AlarmMessage = alarmMessage;
            UpdateTime = updateTime;
            UpdateTicks = updateTicks;
            PublishedRevision = publishedRevision;
            AppliedRevision = appliedRevision;
            TerminationReason = terminationReason;
        }

        public int ProcIndex { get; }
        public Guid ProcId { get; }
        public string ProcName { get; }
        public ProcRunState State { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public bool IsBreakpoint { get; }
        public bool IsAlarm => !string.IsNullOrWhiteSpace(AlarmMessage);
        public string AlarmMessage { get; }
        public DateTime UpdateTime { get; }
        public long UpdateTicks { get; }
        public long PublishedRevision { get; }
        public long AppliedRevision { get; }
        public bool HasPendingUpdate => PublishedRevision > AppliedRevision;
        public ProcTerminationReason TerminationReason { get; }
    }

    public sealed class OperationTraceEntry
    {
        public DateTime Timestamp { get; set; }
        public string Phase { get; set; }
        public int ProcIndex { get; set; }
        public Guid ProcId { get; set; }
        public int StepIndex { get; set; }
        public int OpIndex { get; set; }
        public Guid OperationId { get; set; }
        public string OperationType { get; set; }
        public string OperationName { get; set; }
        public bool IsAlarm { get; set; }
        public string AlarmMessage { get; set; }
        public long ElapsedMs { get; set; }
    }

    public enum AlarmTypeKind
    {
        Stop = 0,
        Ignore = 1,
        AutoHandle = 2,
        Confirm = 3,
        ConfirmYesNo = 4,
        ConfirmYesNoCancel = 5
    }

    public enum AlarmDecision
    {
        Stop = 0,
        Ignore = 1,
        Goto1 = 2,
        Goto2 = 3,
        Goto3 = 4
    }

    public enum LogLevel
    {
        Error = 0,
        Normal = 1
    }

    public interface ILogger
    {
        void Log(string message, LogLevel level);
    }

    public interface IAlarmHandler
    {
        Task<AlarmDecision> HandleAsync(AlarmContext context);
    }

    public sealed class AlarmContext
    {
        public AlarmContext(int procIndex, int stepIndex, int opIndex, string alarmType, string alarmMessage,
            string note, string btn1, string btn2, string btn3)
        {
            ProcIndex = procIndex;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            AlarmType = alarmType;
            AlarmMessage = alarmMessage;
            Note = note;
            Btn1 = btn1;
            Btn2 = btn2;
            Btn3 = btn3;
        }

        public int ProcIndex { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public string AlarmType { get; }
        public string AlarmMessage { get; }
        public string Note { get; }
        public string Btn1 { get; }
        public string Btn2 { get; }
        public string Btn3 { get; }
    }

    public sealed class EngineContext
    {
        public IList<Proc> Procs { get; set; }
        public ValueConfigStore ValueStore { get; set; }
        public DataStructStore DataStructStore { get; set; }
        public TrayPointStore TrayPointStore { get; set; }
        public CardConfigStore CardStore { get; set; }
        public IMotionRuntime Motion { get; set; }
        public IIoRuntime Io { get; set; }
        public CommunicationHub Comm { get; set; }
        public CommunicationConfigStore CommunicationStore { get; set; }
        public PlcRuntimeService PlcRuntime { get; set; }
        public AlarmInfoStore AlarmInfoStore { get; set; }
        public IDictionary<string, IO> IoMap { get; set; }
        public IList<DataStation> Stations { get; set; }
        public IList<SocketInfo> SocketInfos { get; set; }
        public IList<SerialPortInfo> SerialPortInfos { get; set; }
        public CustomFunc CustomFunc { get; set; }
        public AxisStatusCache AxisStatuses { get; set; }
        public AxisMotionParameterStore AxisMotionParameters { get; set; }
    }
}
