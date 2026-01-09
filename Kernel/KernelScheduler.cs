using System;
using System.Collections.Generic;

namespace Automation.Kernel
{
    public sealed class KernelScheduler : IKernelScheduler
    {
        private readonly object _syncRoot = new object();
        private readonly DataRun _dataRun;
        private readonly Dictionary<int, ProcessRunner> _runners = new Dictionary<int, ProcessRunner>();

        public event Action<ProcessStatus> StatusChanged;
        public event Action<ProcessStatus> Faulted;

        public KernelScheduler(DataRun dataRun)
        {
            _dataRun = dataRun ?? throw new ArgumentNullException(nameof(dataRun));
        }

        public void Start(int processIndex)
        {
            Proc proc = GetProc(processIndex);
            if (proc == null)
            {
                PublishFault(processIndex, "Process not found");
                return;
            }

            ProcessRunner runner = GetOrCreateRunner(processIndex, proc);
            runner.Start();
            PublishStatus(processIndex);
        }

        public void StartAt(int processIndex, int stepIndex, int opIndex, ProcessState initialState)
        {
            Proc proc = GetProc(processIndex);
            if (proc == null)
            {
                PublishFault(processIndex, "Process not found");
                return;
            }

            ProcessRunner runner = GetOrCreateRunner(processIndex, proc);
            if (runner.IsRunning)
            {
                runner.RequestStop();
                runner = new ProcessRunner(_dataRun, proc, processIndex);
                runner.Exited += OnRunnerExited;
                lock (_syncRoot)
                {
                    _runners[processIndex] = runner;
                }
            }
            runner.StartAt(stepIndex, opIndex, MapState(initialState));
            PublishStatus(processIndex);
        }

        public void Stop(int processIndex)
        {
            if (!TryGetRunner(processIndex, out ProcessRunner runner))
            {
                ProcHandle handle = GetHandle(processIndex);
                if (handle != null)
                {
                    handle.isThStop = true;
                    handle.State = ProcRunState.Stopped;
                    handle.isBreakpoint = false;
                    handle.m_evtRun.Set();
                    handle.m_evtTik.Set();
                    handle.m_evtTok.Set();
                    _dataRun.SetProcText(processIndex, handle.State, handle.isBreakpoint);
                }
                PublishStatus(processIndex);
                return;
            }

            runner.RequestStop();
            PublishStatus(processIndex);
        }

        public void Pause(int processIndex)
        {
            if (TryGetRunner(processIndex, out ProcessRunner runner))
            {
                runner.Pause();
            }
            PublishStatus(processIndex);
        }

        public void Resume(int processIndex)
        {
            if (TryGetRunner(processIndex, out ProcessRunner runner))
            {
                runner.Resume();
            }
            PublishStatus(processIndex);
        }

        public void Step(int processIndex)
        {
            if (TryGetRunner(processIndex, out ProcessRunner runner))
            {
                runner.StepOnce();
            }
            PublishStatus(processIndex);
        }

        public IReadOnlyList<ProcessStatus> GetStatuses()
        {
            int count = GetProcessCount();
            List<ProcessStatus> statuses = new List<ProcessStatus>(count);
            for (int i = 0; i < count; i++)
            {
                statuses.Add(BuildStatus(i));
            }
            return statuses;
        }

        private ProcessRunner GetOrCreateRunner(int processIndex, Proc proc)
        {
            lock (_syncRoot)
            {
                if (!_runners.TryGetValue(processIndex, out ProcessRunner runner))
                {
                    runner = new ProcessRunner(_dataRun, proc, processIndex);
                    runner.Exited += OnRunnerExited;
                    _runners[processIndex] = runner;
                }
                return runner;
            }
        }

        private bool TryGetRunner(int processIndex, out ProcessRunner runner)
        {
            lock (_syncRoot)
            {
                return _runners.TryGetValue(processIndex, out runner);
            }
        }

        private void OnRunnerExited(ProcessRunner runner)
        {
            lock (_syncRoot)
            {
                if (_runners.TryGetValue(runner.ProcessIndex, out ProcessRunner current)
                    && ReferenceEquals(current, runner))
                {
                    _runners.Remove(runner.ProcessIndex);
                }
            }

            PublishStatus(runner.ProcessIndex);
        }

        private void PublishStatus(int processIndex)
        {
            ProcessStatus status = BuildStatus(processIndex);
            StatusChanged?.Invoke(status);

            ProcHandle handle = GetHandle(processIndex);
            if (handle != null && (handle.State == ProcRunState.Alarming || handle.isAlarm))
            {
                Faulted?.Invoke(status);
            }
        }

        private void PublishFault(int processIndex, string message)
        {
            ProcessStatus status = new ProcessStatus
            {
                ProcessIndex = processIndex,
                State = ProcessState.Unknown,
                LastError = message
            };
            Faulted?.Invoke(status);
            StatusChanged?.Invoke(status);
        }

        private ProcessStatus BuildStatus(int processIndex)
        {
            ProcHandle handle = GetHandle(processIndex);
            string processName = handle?.procName;
            if (string.IsNullOrEmpty(processName))
            {
                Proc proc = GetProc(processIndex);
                processName = proc?.head?.Name;
            }

            if (handle == null)
            {
                return new ProcessStatus
                {
                    ProcessIndex = processIndex,
                    ProcessName = processName,
                    StepIndex = -1,
                    OpIndex = -1,
                    State = ProcessState.Stopped,
                    IsBreakpoint = false,
                    LastError = null
                };
            }

            return new ProcessStatus
            {
                ProcessIndex = processIndex,
                ProcessName = processName,
                StepIndex = handle.stepNum,
                OpIndex = handle.opsNum,
                State = MapState(handle.State),
                IsBreakpoint = handle.isBreakpoint,
                LastError = handle.alarmMsg
            };
        }

        private ProcHandle GetHandle(int processIndex)
        {
            ProcHandle[] handles = _dataRun.ProcHandles;
            if (handles == null || processIndex < 0 || processIndex >= handles.Length)
            {
                return null;
            }
            return handles[processIndex];
        }

        private Proc GetProc(int processIndex)
        {
            if (SF.frmProc?.procsList == null || processIndex < 0 || processIndex >= SF.frmProc.procsList.Count)
            {
                return null;
            }
            return SF.frmProc.procsList[processIndex];
        }

        private int GetProcessCount()
        {
            if (SF.frmProc?.procsList == null)
            {
                return 0;
            }
            return SF.frmProc.procsList.Count;
        }

        private static ProcRunState MapState(ProcessState state)
        {
            switch (state)
            {
                case ProcessState.Stopped:
                    return ProcRunState.Stopped;
                case ProcessState.Paused:
                    return ProcRunState.Paused;
                case ProcessState.SingleStep:
                    return ProcRunState.SingleStep;
                case ProcessState.Running:
                    return ProcRunState.Running;
                case ProcessState.Alarming:
                    return ProcRunState.Alarming;
                default:
                    return ProcRunState.Stopped;
            }
        }

        private static ProcessState MapState(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Stopped:
                    return ProcessState.Stopped;
                case ProcRunState.Paused:
                    return ProcessState.Paused;
                case ProcRunState.SingleStep:
                    return ProcessState.SingleStep;
                case ProcRunState.Running:
                    return ProcessState.Running;
                case ProcRunState.Alarming:
                    return ProcessState.Alarming;
                default:
                    return ProcessState.Unknown;
            }
        }
    }
}
