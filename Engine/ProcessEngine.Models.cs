using System;
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
    public class ProcHandle
    {
        public int procNum;
        public int stepNum;
        public int opsNum;

        public string procName;

        //流程状态
        public ProcRunState State = ProcRunState.Stopped;
        //断点标志
        public bool isBreakpoint;
        //线程终止标志位
        public bool isThStop;
        //标志是否发生了跳转
        public bool isGoto;
        //标志是否发生了报警
        public bool isAlarm;
        //自定义报警信息
        public string alarmMsg;
        public bool singleOpOnce;
        public int singleOpStep;
        public int singleOpOp;

    }
    public sealed class EngineSnapshot
    {
        public EngineSnapshot(int procIndex, string procName, ProcRunState state, int stepIndex, int opIndex,
            bool isBreakpoint, bool isAlarm, string alarmMessage, DateTime updateTime)
        {
            ProcIndex = procIndex;
            ProcName = procName;
            State = state;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            IsBreakpoint = isBreakpoint;
            IsAlarm = isAlarm;
            AlarmMessage = alarmMessage;
            UpdateTime = updateTime;
        }

        public int ProcIndex { get; }
        public string ProcName { get; }
        public ProcRunState State { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public bool IsBreakpoint { get; }
        public bool IsAlarm { get; }
        public string AlarmMessage { get; }
        public DateTime UpdateTime { get; }
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
        public CardConfigStore CardStore { get; set; }
        public MotionCtrl Motion { get; set; }
        public CommunicationHub Comm { get; set; }
        public AlarmInfoStore AlarmInfoStore { get; set; }
        public IDictionary<string, IO> IoMap { get; set; }
        public IList<DataStation> Stations { get; set; }
        public IList<SocketInfo> SocketInfos { get; set; }
        public IList<SerialPortInfo> SerialPortInfos { get; set; }
        public CustomFunc CustomFunc { get; set; }
        public Func<ushort, ushort, int, bool> AxisStateBitGetter { get; set; }
    }
}
