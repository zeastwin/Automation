using System;
using System.Collections.Generic;

namespace Automation.Kernel
{
    public sealed class KernelSchedulerAdapter : IKernelScheduler
    {
        private readonly DataRun _dataRun;

        public event Action<ProcessStatus> StatusChanged;
        public event Action<ProcessStatus> Faulted;

        public KernelSchedulerAdapter(DataRun dataRun)
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

            if (SF.frmProc != null && processIndex == SF.frmProc.SelectedProcNum)
            {
                _dataRun.StartProc(proc);
            }
            else
            {
                _dataRun.StartProcAuto(proc, processIndex);
            }

            ProcHandle handle = GetHandle(processIndex);
            if (handle == null)
            {
                PublishFault(processIndex, "Process handle not found");
                return;
            }

            handle.m_evtRun.Set();
            handle.m_evtTik.Set();
            handle.m_evtTok.Set();
            handle.State = ProcRunState.Running;
            handle.isBreakpoint = false;
            _dataRun.SetProcText(processIndex, handle.State, handle.isBreakpoint);

            PublishStatus(processIndex);
        }

        public void Stop(int processIndex)
        {
            ProcHandle handle = GetHandle(processIndex);
            if (handle == null)
            {
                PublishFault(processIndex, "Process handle not found");
                return;
            }

            handle.isThStop = true;
            handle.State = ProcRunState.Stopped;
            handle.isBreakpoint = false;
            handle.m_evtRun.Set();
            handle.m_evtTik.Set();
            handle.m_evtTok.Set();
            _dataRun.SetProcText(processIndex, handle.State, handle.isBreakpoint);

            PublishStatus(processIndex);
        }

        public void Pause(int processIndex)
        {
            ProcHandle handle = GetHandle(processIndex);
            if (handle == null)
            {
                PublishFault(processIndex, "Process handle not found");
                return;
            }

            if (handle.State == ProcRunState.Running || handle.State == ProcRunState.Alarming)
            {
                handle.m_evtRun.Reset();
                handle.m_evtTik.Reset();
                handle.m_evtTok.Set();
                handle.State = ProcRunState.Paused;
                handle.isBreakpoint = false;
                _dataRun.SetProcText(processIndex, handle.State, handle.isBreakpoint);
            }

            PublishStatus(processIndex);
        }

        public void Resume(int processIndex)
        {
            ProcHandle handle = GetHandle(processIndex);
            if (handle == null)
            {
                PublishFault(processIndex, "Process handle not found");
                return;
            }

            if (handle.State == ProcRunState.Paused || handle.State == ProcRunState.SingleStep)
            {
                handle.m_evtRun.Set();
                handle.m_evtTik.Set();
                handle.m_evtTok.Set();
                handle.State = ProcRunState.Running;
                handle.isBreakpoint = false;
                _dataRun.SetProcText(processIndex, handle.State, handle.isBreakpoint);
            }

            PublishStatus(processIndex);
        }

        public void Step(int processIndex)
        {
            ProcHandle handle = GetHandle(processIndex);
            if (handle == null)
            {
                PublishFault(processIndex, "Process handle not found");
                return;
            }

            if (handle.State == ProcRunState.Paused || handle.State == ProcRunState.SingleStep)
            {
                handle.State = ProcRunState.SingleStep;
                handle.isBreakpoint = false;
                _dataRun.SetProcText(processIndex, handle.State, handle.isBreakpoint);
                handle.m_evtRun.Set();
                handle.m_evtTok.Reset();
                handle.m_evtTik.Set();
                SF.Delay(10);
                handle.m_evtTik.Reset();
                handle.m_evtTok.Set();
            }

            PublishStatus(processIndex);
        }

        public IReadOnlyList<ProcessStatus> GetStatuses()
        {
            if (_dataRun.ProcHandles == null)
            {
                return Array.Empty<ProcessStatus>();
            }

            int count = _dataRun.ProcHandles.Length;
            if (SF.frmProc?.procsList != null)
            {
                count = Math.Min(count, SF.frmProc.procsList.Count);
            }

            List<ProcessStatus> statuses = new List<ProcessStatus>(count);
            for (int i = 0; i < count; i++)
            {
                statuses.Add(BuildStatus(i));
            }
            return statuses;
        }

        private void PublishStatus(int processIndex)
        {
            ProcHandle handle = GetHandle(processIndex);
            ProcessStatus status = BuildStatus(processIndex);
            StatusChanged?.Invoke(status);
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

        private ProcHandle GetHandle(int processIndex)
        {
            if (_dataRun.ProcHandles == null)
            {
                return null;
            }
            if (processIndex < 0 || processIndex >= _dataRun.ProcHandles.Length)
            {
                return null;
            }
            return _dataRun.ProcHandles[processIndex];
        }

        private Proc GetProc(int processIndex)
        {
            if (SF.frmProc?.procsList == null)
            {
                return null;
            }
            if (processIndex < 0 || processIndex >= SF.frmProc.procsList.Count)
            {
                return null;
            }
            return SF.frmProc.procsList[processIndex];
        }

        private ProcessStatus BuildStatus(int processIndex)
        {
            ProcHandle handle = GetHandle(processIndex);
            string processName = handle?.procName;
            if (string.IsNullOrEmpty(processName) && SF.frmProc?.procsList != null && processIndex >= 0 && processIndex < SF.frmProc.procsList.Count)
            {
                processName = SF.frmProc.procsList[processIndex]?.head?.Name;
            }

            return new ProcessStatus
            {
                ProcessIndex = processIndex,
                ProcessName = processName,
                StepIndex = handle?.stepNum ?? -1,
                OpIndex = handle?.opsNum ?? -1,
                State = handle == null ? ProcessState.Unknown : MapState(handle.State),
                IsBreakpoint = handle?.isBreakpoint ?? false,
                LastError = handle?.alarmMsg
            };
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
