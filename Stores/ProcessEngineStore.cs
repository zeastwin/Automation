using System;
using System.Collections.Generic;

namespace Automation
{
    public interface IProcessEngineStore
    {
        int GetProcCount();
        bool TryGetProcName(int procIndex, out string procName);
        bool TryGetProcState(int procIndex, out ProcRunState state);
        bool TryGetSnapshot(int procIndex, out EngineSnapshot snapshot);
        bool StartProc(int procIndex);
        bool StartProcAt(int procIndex, int stepIndex, int opIndex, ProcRunState startState);
        bool RunSingleOpOnce(int procIndex, int stepIndex, int opIndex);
        bool Pause(int procIndex);
        bool Resume(int procIndex);
        bool Step(int procIndex);
        bool Stop(int procIndex);
    }

    public sealed class ProcessEngineStore : IProcessEngineStore
    {
        private readonly ProcessEngine engine;

        public ProcessEngineStore(ProcessEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public int GetProcCount()
        {
            return engine.Context?.Procs?.Count ?? 0;
        }

        public bool TryGetProcName(int procIndex, out string procName)
        {
            procName = null;
            if (!TryGetProc(procIndex, out Proc proc))
            {
                return false;
            }
            procName = proc.head?.Name ?? string.Empty;
            return true;
        }

        public bool TryGetProcState(int procIndex, out ProcRunState state)
        {
            state = ProcRunState.Stopped;
            if (!IsValidProcIndex(procIndex))
            {
                return false;
            }
            EngineSnapshot snapshot = engine.GetSnapshot(procIndex);
            if (snapshot == null)
            {
                return false;
            }
            state = snapshot.State;
            return true;
        }

        public bool TryGetSnapshot(int procIndex, out EngineSnapshot snapshot)
        {
            snapshot = null;
            if (!IsValidProcIndex(procIndex))
            {
                return false;
            }
            snapshot = engine.GetSnapshot(procIndex);
            return snapshot != null;
        }

        public bool StartProc(int procIndex)
        {
            if (!TryGetProc(procIndex, out Proc proc))
            {
                return false;
            }
            engine.StartProc(proc, procIndex);
            return true;
        }

        public bool StartProcAt(int procIndex, int stepIndex, int opIndex, ProcRunState startState)
        {
            if (!TryGetProc(procIndex, out Proc proc))
            {
                return false;
            }
            engine.StartProcAt(proc, procIndex, stepIndex, opIndex, startState);
            return true;
        }

        public bool RunSingleOpOnce(int procIndex, int stepIndex, int opIndex)
        {
            if (!TryGetProc(procIndex, out Proc proc))
            {
                return false;
            }
            engine.RunSingleOpOnce(proc, procIndex, stepIndex, opIndex);
            return true;
        }

        public bool Pause(int procIndex)
        {
            if (!IsValidProcIndex(procIndex))
            {
                return false;
            }
            engine.Pause(procIndex);
            return true;
        }

        public bool Resume(int procIndex)
        {
            if (!IsValidProcIndex(procIndex))
            {
                return false;
            }
            engine.Resume(procIndex);
            return true;
        }

        public bool Step(int procIndex)
        {
            if (!IsValidProcIndex(procIndex))
            {
                return false;
            }
            engine.Step(procIndex);
            return true;
        }

        public bool Stop(int procIndex)
        {
            if (!IsValidProcIndex(procIndex))
            {
                return false;
            }
            engine.Stop(procIndex);
            return true;
        }

        private bool TryGetProc(int procIndex, out Proc proc)
        {
            proc = null;
            if (procIndex < 0)
            {
                return false;
            }
            IList<Proc> procs = engine.Context?.Procs;
            if (procs == null || procIndex >= procs.Count)
            {
                return false;
            }
            proc = procs[procIndex];
            return proc != null;
        }

        private bool IsValidProcIndex(int procIndex)
        {
            if (procIndex < 0)
            {
                return false;
            }
            IList<Proc> procs = engine.Context?.Procs;
            return procs != null && procIndex < procs.Count;
        }
    }
}
