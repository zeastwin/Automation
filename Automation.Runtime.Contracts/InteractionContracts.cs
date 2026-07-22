using System.Threading;
using System.Threading.Tasks;
// 模块：运行时契约 / 人机交互端口。
// 职责范围：让无 UI 的流程引擎请求报警、日志和弹窗交互，不引用 WinForms 实现。
// 排查入口：交互未响应时检查 CancellationToken、UI 适配器是否就绪及请求是否已完成，勿在引擎层直接找窗体。

namespace Automation
{
    /// <summary>报警交互模式；决定是否停止、自动处理或等待前台确认。</summary>
    public enum AlarmTypeKind
    {
        Stop = 0,
        Ignore = 1,
        AutoHandle = 2,
        Confirm = 3,
        ConfirmYesNo = 4,
        ConfirmYesNoCancel = 5
    }

    /// <summary>报警或弹窗的结构化处理结果，由引擎解释为停止、忽略或跳转分支。</summary>
    public enum AlarmDecision
    {
        Stop = 0,
        Ignore = 1,
        Goto1 = 2,
        Goto2 = 3,
        Goto3 = 4
    }

    /// <summary>运行时最小日志级别契约。</summary>
    public enum LogLevel
    {
        Error = 0,
        Normal = 1
    }

    /// <summary>无 UI 运行模块使用的日志端口。</summary>
    public interface ILogger
    {
        void Log(string message, LogLevel level);
    }

    /// <summary>流程报警决策端口；实现负责把请求交给适当的人机交互层。</summary>
    public interface IAlarmHandler
    {
        Task<AlarmDecision> HandleAsync(AlarmContext context);
    }

    /// <summary>流程弹窗端口；关闭或停机通过 CancellationToken 结束等待。</summary>
    public interface IProcessPopupService
    {
        Task<AlarmDecision> ShowAsync(ProcessPopupRequest request, CancellationToken cancellationToken);
    }

    /// <summary>流程发往前台的弹窗请求，不包含任何 WinForms 控件。</summary>
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

    /// <summary>报警发生位置和配置快照，用于决策与日志关联。</summary>
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
