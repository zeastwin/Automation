// 模块：运行时契约 / 流程状态。
// 职责范围：定义引擎、宿主与 UI 共同使用的流程状态、终止原因和运行日志端口。
// 排查入口：停止原因看 ProcTerminationReason，运行状态看 ProcRunState；二者含义不同，不要互相替代。

namespace Automation
{
    /// <summary>流程实例当前状态；Stopping/Pausing 是尚未完成的过渡态。</summary>
    public enum ProcRunState
    {
        Stopped = 0,
        Paused = 1,
        SingleStep = 2,
        Running = 3,
        Alarming = 4,
        Pausing = 5,
        Stopping = 6,
        Ready = 7
    }

    /// <summary>流程状态机的公共判定，避免各层把“就绪”和“停止”重新合并成同一显示状态。</summary>
    public static class ProcRunStateExtensions
    {
        /// <summary>就绪和停止都没有活动执行实例，可以编辑或再次启动。</summary>
        public static bool IsInactive(this ProcRunState state)
        {
            return state == ProcRunState.Ready || state == ProcRunState.Stopped;
        }
    }

    /// <summary>流程实例结束原因，用于运行摘要和故障复盘，不代替当前运行状态。</summary>
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

    /// <summary>变量表中“复位状态”的固定数值契约。</summary>
    public enum ResetStatus
    {
        NotReset = 0,
        Resetting = 1,
        ResetCompleted = 2
    }

    /// <summary>变量表中“系统状态”的固定数值契约。</summary>
    public enum SystemStatus
    {
        Uninitialized = 0,
        Paused = 1,
        Ready = 2,
        Working = 3,
        ProcAlarm = 4,
        PopupAlarm = 5
    }
}
