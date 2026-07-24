// 模块：编辑器 / 流程。
// 职责范围：流程导航、指令表、对象选择、搜索和导航。

using System;

namespace Automation
{
    /// <summary>
    /// 流程导航、指令表和检查器共享的编辑选择。索引用于当前显示，稳定 ID 用于刷新后恢复。
    /// </summary>
    internal sealed class ProcessEditorSelectionState
    {
        public int ProcIndex { get; private set; } = -1;
        public int StepIndex { get; private set; } = -1;
        public int OperationIndex { get; private set; } = -1;
        public Guid ProcId { get; private set; }
        public Guid StepId { get; private set; }
        public Guid OperationId { get; private set; }

        public void SelectProcess(int procIndex, Guid procId)
        {
            if (ProcIndex == procIndex && ProcId == procId) return;
            ProcIndex = procIndex;
            ProcId = procIndex >= 0 ? procId : Guid.Empty;
            StepIndex = -1;
            StepId = Guid.Empty;
            OperationIndex = -1;
            OperationId = Guid.Empty;
        }

        public void SelectStep(int stepIndex, Guid stepId)
        {
            if (StepIndex == stepIndex && StepId == stepId) return;
            StepIndex = stepIndex;
            StepId = stepIndex >= 0 ? stepId : Guid.Empty;
            OperationIndex = -1;
            OperationId = Guid.Empty;
        }

        public void SelectOperation(int operationIndex, Guid operationId)
        {
            OperationIndex = operationIndex;
            OperationId = operationIndex >= 0 ? operationId : Guid.Empty;
        }

        public void Clear()
        {
            SelectProcess(-1, Guid.Empty);
        }
    }
}
