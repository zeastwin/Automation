using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
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

    public interface IProcessPopupService
    {
        Task<AlarmDecision> ShowAsync(ProcessPopupRequest request, CancellationToken cancellationToken);
    }

    public sealed class ProcessPopupRequest
    {
        public int ProcIndex { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Button1 { get; set; }
        public string Button2 { get; set; }
        public string Button3 { get; set; }
        public int ButtonCount { get; set; }
        public int? AutoCloseMilliseconds { get; set; }
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
}
