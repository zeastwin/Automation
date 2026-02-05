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
        Alarming = 4
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
        ProcAlarm = 1,
        Ready = 2,
        Working = 3,
        Paused = 4,
        PopupAlarm = 5
    }
    public class ProcHandle
    {
        public int procNum;
        public int stepNum;
        public int opsNum;

        public string procName;
        public Guid procId;

        //流程状态
        public ProcRunState State = ProcRunState.Stopped;
        //断点标志
        public bool isBreakpoint;
        //标志是否发生了跳转
        public bool isGoto;
        //标志是否发生了报警
        public bool isAlarm;
        //自定义报警信息
        public string alarmMsg;
        public bool singleOpOnce;
        public int singleOpStep;
        public int singleOpOp;
        public CancellationToken CancellationToken { get; set; }
        internal ProcessControl Control { get; set; }
        public ConcurrentBag<Task> RunningTasks { get; } = new ConcurrentBag<Task>();
        public bool PauseBySignal { get; set; }
        public Proc Proc { get; set; }

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
            ProcRunState startState, bool singleOpOnce, bool autoStep)
        {
            Type = type;
            ProcIndex = procIndex;
            Proc = proc;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            StartState = startState;
            SingleOpOnce = singleOpOnce;
            AutoStep = autoStep;
        }

        public EngineCommandType Type { get; }
        public int ProcIndex { get; }
        public Proc Proc { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public ProcRunState StartState { get; }
        public bool SingleOpOnce { get; }
        public bool AutoStep { get; }
        internal long Generation { get; set; }

        public static EngineCommand Start(int procIndex, Proc proc, int stepIndex, int opIndex, ProcRunState startState)
        {
            return new EngineCommand(EngineCommandType.StartAt, procIndex, proc, stepIndex, opIndex, startState, false, false);
        }

        public static EngineCommand RunSingleOpOnce(int procIndex, Proc proc, int stepIndex, int opIndex)
        {
            return new EngineCommand(EngineCommandType.RunSingleOpOnce, procIndex, proc, stepIndex, opIndex,
                ProcRunState.SingleStep, true, true);
        }

        public static EngineCommand Pause(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Pause, procIndex, null, 0, 0, ProcRunState.Paused, false, false);
        }

        public static EngineCommand Resume(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Resume, procIndex, null, 0, 0, ProcRunState.Running, false, false);
        }

        public static EngineCommand Step(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Step, procIndex, null, 0, 0, ProcRunState.SingleStep, false, false);
        }

        public static EngineCommand Stop(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Stop, procIndex, null, 0, 0, ProcRunState.Stopped, false, false);
        }
    }
    public sealed class EngineSnapshot
    {
        public EngineSnapshot(int procIndex, Guid procId, string procName, ProcRunState state, int stepIndex, int opIndex,
            bool isBreakpoint, bool isAlarm, string alarmMessage, DateTime updateTime, long updateTicks)
        {
            ProcIndex = procIndex;
            ProcId = procId;
            ProcName = procName;
            State = state;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            IsBreakpoint = isBreakpoint;
            IsAlarm = isAlarm;
            AlarmMessage = alarmMessage;
            UpdateTime = updateTime;
            UpdateTicks = updateTicks;
        }

        public int ProcIndex { get; }
        public Guid ProcId { get; }
        public string ProcName { get; }
        public ProcRunState State { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public bool IsBreakpoint { get; }
        public bool IsAlarm { get; }
        public string AlarmMessage { get; }
        public DateTime UpdateTime { get; }
        public long UpdateTicks { get; }
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
        public MotionCtrl Motion { get; set; }
        public CommunicationHub Comm { get; set; }
        public PlcConfigStore PlcStore { get; set; }
        public AlarmInfoStore AlarmInfoStore { get; set; }
        public IDictionary<string, IO> IoMap { get; set; }
        public IList<DataStation> Stations { get; set; }
        public IList<SocketInfo> SocketInfos { get; set; }
        public IList<SerialPortInfo> SerialPortInfos { get; set; }
        public CustomFunc CustomFunc { get; set; }
        public Func<ushort, ushort, int, bool> AxisStateBitGetter { get; set; }
    }
}
