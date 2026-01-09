using System;
using System.Collections.Generic;

namespace Automation.Kernel
{
    public enum ProcessState
    {
        Stopped = 0,
        Paused = 1,
        SingleStep = 2,
        Running = 3,
        Alarming = 4,
        Unknown = 99
    }

    public sealed class ProcessStatus
    {
        public int ProcessIndex { get; set; }
        public string ProcessName { get; set; }
        public int StepIndex { get; set; }
        public int OpIndex { get; set; }
        public ProcessState State { get; set; }
        public bool IsBreakpoint { get; set; }
        public string LastError { get; set; }
    }

    public interface IKernelScheduler
    {
        event Action<ProcessStatus> StatusChanged;
        event Action<ProcessStatus> Faulted;

        void Start(int processIndex);
        void StartAt(int processIndex, int stepIndex, int opIndex, ProcessState initialState);
        void Stop(int processIndex);
        void Pause(int processIndex);
        void Resume(int processIndex);
        void Step(int processIndex);
        IReadOnlyList<ProcessStatus> GetStatuses();
    }
}
