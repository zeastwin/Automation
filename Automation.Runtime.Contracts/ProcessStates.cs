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

    // 变量表中的复位状态。
    public enum ResetStatus
    {
        NotReset = 0,
        Resetting = 1,
        ResetCompleted = 2
    }

    // 变量表中的系统状态。
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
