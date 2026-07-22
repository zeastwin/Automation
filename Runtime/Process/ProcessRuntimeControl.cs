using System;
// 模块：运行时 / 流程适配。
// 职责范围：向宿主和编辑器投影流程控制与流程、变量存储端口。

using System.Collections.Generic;

namespace Automation
{
    public interface IProcessRuntimeControl
    {
        bool StartProc(int procIndex);
        bool Pause(int procIndex);
        bool Resume(int procIndex);
        bool Stop(int procIndex);
    }

    public sealed class ProcessRuntimeControl : IProcessRuntimeControl
    {
        private readonly ProcessEngine engine;

        public ProcessRuntimeControl(ProcessEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public bool StartProc(int procIndex)
        {
            if (!TryGetProc(procIndex, out Proc proc))
            {
                return false;
            }
            return engine.StartProc(proc, procIndex);
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
