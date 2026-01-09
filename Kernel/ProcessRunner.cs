using System;
using System.Threading;

namespace Automation.Kernel
{
    public sealed class ProcessRunner
    {
        private readonly object _syncRoot = new object();
        private readonly DataRun _dataRun;
        private readonly Proc _proc;
        private readonly int _processIndex;
        private Thread _thread;
        private ProcHandle _handle;

        public event Action<ProcessRunner> Exited;

        public ProcessRunner(DataRun dataRun, Proc proc, int processIndex)
        {
            _dataRun = dataRun ?? throw new ArgumentNullException(nameof(dataRun));
            _proc = proc ?? throw new ArgumentNullException(nameof(proc));
            _processIndex = processIndex;
        }

        public int ProcessIndex => _processIndex;

        public ProcHandle Handle => _handle;

        public bool IsRunning => _thread != null && _thread.IsAlive;

        public void Start()
        {
            StartAt(0, 0, ProcRunState.Running);
        }

        public void StartAt(int stepIndex, int opIndex, ProcRunState initialState)
        {
            ProcHandle handle = CreateHandle(stepIndex, opIndex, initialState);
            StartInternal(handle);
        }

        public void RequestStop()
        {
            ProcHandle handle = _handle;
            if (handle == null)
            {
                return;
            }

            handle.isThStop = true;
            handle.State = ProcRunState.Stopped;
            handle.isBreakpoint = false;
            handle.Sync.ForceWakeForStop();
            _dataRun.SetProcText(_processIndex, handle.State, handle.isBreakpoint);
        }

        public void Pause()
        {
            ProcHandle handle = _handle;
            if (handle == null)
            {
                return;
            }

            if (handle.State == ProcRunState.Running || handle.State == ProcRunState.Alarming)
            {
                handle.Sync.EnterBreakpoint();
                handle.State = ProcRunState.Paused;
                handle.isBreakpoint = false;
                _dataRun.SetProcText(_processIndex, handle.State, handle.isBreakpoint);
            }
        }

        public void Resume()
        {
            ProcHandle handle = _handle;
            if (handle == null)
            {
                return;
            }

            if (handle.State == ProcRunState.Paused || handle.State == ProcRunState.SingleStep)
            {
                handle.Sync.Continue();
                handle.State = ProcRunState.Running;
                handle.isBreakpoint = false;
                _dataRun.SetProcText(_processIndex, handle.State, handle.isBreakpoint);
            }
        }

        public void StepOnce()
        {
            ProcHandle handle = _handle;
            if (handle == null)
            {
                return;
            }

            if (handle.State == ProcRunState.Paused || handle.State == ProcRunState.SingleStep)
            {
                handle.State = ProcRunState.SingleStep;
                handle.isBreakpoint = false;
                _dataRun.SetProcText(_processIndex, handle.State, handle.isBreakpoint);
                handle.Sync.StepOnce();
            }
        }

        private ProcHandle CreateHandle(int stepIndex, int opIndex, ProcRunState initialState)
        {
            ProcHandle handle = new ProcHandle
            {
                procNum = _processIndex,
                stepNum = stepIndex,
                opsNum = opIndex,
                isThStop = false,
                isBreakpoint = false,
                procName = _proc.head.Name,
                State = initialState
            };

            if (initialState == ProcRunState.Paused || initialState == ProcRunState.SingleStep)
            {
                handle.Sync.EnterBreakpoint();
            }
            else
            {
                handle.Sync.Continue();
            }

            return handle;
        }

        private void StartInternal(ProcHandle handle)
        {
            lock (_syncRoot)
            {
                if (_thread != null && _thread.IsAlive)
                {
                    return;
                }

                _dataRun.EnsureCapacity(_processIndex + 1);
                _handle = handle;
                _dataRun.ProcHandles[_processIndex] = handle;
                if (handle.State == ProcRunState.Stopped)
                {
                    handle.State = ProcRunState.Running;
                    handle.Sync.Continue();
                }
                _dataRun.SetProcText(_processIndex, handle.State, handle.isBreakpoint);

                _thread = new Thread(() => RunProc(handle));
                _dataRun.threads[_processIndex] = _thread;
                _thread.Start();
            }
        }

        private void RunProc(ProcHandle handle)
        {
            try
            {
                _dataRun.RunProc(_proc, handle);
            }
            finally
            {
                Cleanup(handle);
            }
        }

        private void Cleanup(ProcHandle handle)
        {
            lock (_syncRoot)
            {
                if (!ReferenceEquals(_handle, handle))
                {
                    return;
                }

                Thread currentThread = _thread;
                if (_dataRun.threads.Length > _processIndex && ReferenceEquals(_dataRun.threads[_processIndex], currentThread))
                {
                    _dataRun.threads[_processIndex] = null;
                }
                if (_dataRun.ProcHandles.Length > _processIndex && ReferenceEquals(_dataRun.ProcHandles[_processIndex], handle))
                {
                    _dataRun.ProcHandles[_processIndex] = null;
                }
                _handle = null;
                _thread = null;
            }

            Exited?.Invoke(this);
        }
    }
}
