using System;
// 模块：引擎 / 结构编辑。
// 职责范围：执行流程与指令结构变换、跳转重写、发布门禁和变量生命周期处理。

using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 指令级结构编辑的统一入口。负责草稿克隆、跳转重算和流程原子提交。
    /// </summary>
    public sealed class OperationEditingService
    {
        private readonly PlatformRuntime runtime;

        internal OperationEditingService(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool TryDelete(int procIndex, int stepIndex, IEnumerable<int> operationIndexes, out string error)
        {
            if (!TryGetTarget(procIndex, stepIndex, out Proc current, out Step step, out error))
                return false;

            List<int> indexes = (operationIndexes ?? Enumerable.Empty<int>())
                .Distinct().OrderBy(index => index).ToList();
            if (indexes.Count == 0)
            {
                error = "未选择需要删除的指令。";
                return false;
            }
            if (indexes.Any(index => index < 0 || index >= step.Ops.Count))
            {
                error = "删除指令的索引已失效。";
                return false;
            }

            Proc draft = ObjectGraphCloner.Clone(current);
            for (int i = indexes.Count - 1; i >= 0; i--)
                draft.steps[stepIndex].Ops.RemoveAt(indexes[i]);
            ProcessEditingService.RewriteGotoTargets(current, draft, procIndex);
            return ProcessEditingService.TryCommitProcDraft(
                runtime, procIndex, draft, out error, "删除指令");
        }

        public bool TryPaste(
            int procIndex,
            int stepIndex,
            int insertIndex,
            IEnumerable<OperationType> source,
            out int insertedCount,
            out string error)
        {
            insertedCount = 0;
            if (!TryGetTarget(procIndex, stepIndex, out Proc current, out Step step, out error))
                return false;
            if (insertIndex < 0 || insertIndex > step.Ops.Count)
            {
                error = "当前指令索引无效，无法粘贴。";
                return false;
            }

            List<OperationType> operations = OperationClipboardService.PrepareForPaste(source, procIndex);
            if (operations == null || operations.Count == 0)
            {
                error = "剪贴板中没有可粘贴的指令。";
                return false;
            }

            Proc draft = ObjectGraphCloner.Clone(current);
            draft.steps[stepIndex].Ops.InsertRange(insertIndex, operations);
            ProcessEditingService.RewriteGotoTargets(current, draft, procIndex);
            if (!ProcessEditingService.TryCommitProcDraft(
                runtime, procIndex, draft, out error, "粘贴指令"))
                return false;
            insertedCount = operations.Count;
            return true;
        }

        public bool TryMove(int procIndex, int stepIndex, int sourceIndex, int targetIndex, out string error)
        {
            if (!TryGetTarget(procIndex, stepIndex, out Proc current, out Step step, out error))
                return false;
            if (sourceIndex < 0 || sourceIndex >= step.Ops.Count
                || targetIndex < 0 || targetIndex >= step.Ops.Count)
            {
                error = "移动指令的索引已失效。";
                return false;
            }
            if (sourceIndex == targetIndex) return true;

            Proc draft = ObjectGraphCloner.Clone(current);
            OperationType operation = draft.steps[stepIndex].Ops[sourceIndex];
            draft.steps[stepIndex].Ops.RemoveAt(sourceIndex);
            draft.steps[stepIndex].Ops.Insert(targetIndex, operation);
            ProcessEditingService.RewriteGotoTargets(current, draft, procIndex);
            return ProcessEditingService.TryCommitProcDraft(
                runtime, procIndex, draft, out error, "移动指令");
        }

        public bool TryToggleBreakpoint(int procIndex, int stepIndex, int operationIndex, out string error)
        {
            if (!TryGetOperationDraft(procIndex, stepIndex, operationIndex,
                out Proc draft, out OperationType operation, out error))
                return false;
            operation.IsBreakpoint = !operation.IsBreakpoint;
            return ProcessEditingService.TryCommitProcDraft(
                runtime, procIndex, draft, out error, "修改指令断点");
        }

        public bool TryToggleDisabled(int procIndex, int stepIndex, int operationIndex, out string error)
        {
            if (!TryGetOperationDraft(procIndex, stepIndex, operationIndex,
                out Proc draft, out OperationType operation, out error))
                return false;
            operation.Disable = !operation.Disable;
            return ProcessEditingService.TryCommitProcDraft(
                runtime, procIndex, draft, out error, "切换指令禁用状态");
        }

        public bool TrySave(
            int procIndex,
            int stepIndex,
            int selectedOperationIndex,
            bool add,
            OperationType operationDraft,
            out int savedOperationIndex,
            out string error)
        {
            savedOperationIndex = -1;
            if (operationDraft == null)
            {
                error = "指令草稿为空。";
                return false;
            }
            if (!TryGetTarget(procIndex, stepIndex, out Proc current, out Step step, out error))
                return false;

            Proc draft = ObjectGraphCloner.Clone(current);
            OperationType savedOperation = ObjectGraphCloner.Clone(operationDraft);
            if (add)
            {
                savedOperation.Id = Guid.NewGuid();
                savedOperationIndex = selectedOperationIndex < 0 ? step.Ops.Count : selectedOperationIndex + 1;
                if (savedOperationIndex < 0 || savedOperationIndex > step.Ops.Count)
                {
                    error = "新增指令的插入位置已失效。";
                    return false;
                }
                draft.steps[stepIndex].Ops.Insert(savedOperationIndex, savedOperation);
            }
            else
            {
                if (selectedOperationIndex < 0 || selectedOperationIndex >= step.Ops.Count)
                {
                    error = "指令索引已失效。";
                    return false;
                }
                OperationType original = step.Ops[selectedOperationIndex];
                savedOperation.Id = original?.Id != Guid.Empty ? original.Id : Guid.NewGuid();
                draft.steps[stepIndex].Ops[selectedOperationIndex] = savedOperation;
                savedOperationIndex = selectedOperationIndex;
            }

            ProcessEditingService.RewriteGotoTargets(current, draft, procIndex);
            if (add)
            {
                ProcessEditingService.RewriteAddedOperationGotoTargetsFromPreviousLayout(
                    savedOperation, current, draft, procIndex);
            }
            return ProcessEditingService.TryCommitProcDraft(
                runtime, procIndex, draft, out error, add ? "新增指令" : "修改指令");
        }

        private bool TryGetOperationDraft(
            int procIndex,
            int stepIndex,
            int operationIndex,
            out Proc draft,
            out OperationType operation,
            out string error)
        {
            draft = null;
            operation = null;
            if (!TryGetTarget(procIndex, stepIndex, out Proc current, out Step step, out error))
                return false;
            if (operationIndex < 0 || operationIndex >= step.Ops.Count)
            {
                error = "指令索引已失效。";
                return false;
            }
            draft = ObjectGraphCloner.Clone(current);
            operation = draft.steps[stepIndex].Ops[operationIndex];
            if (operation != null) return true;
            error = "指令为空，无法编辑。";
            return false;
        }

        private bool TryGetTarget(
            int procIndex,
            int stepIndex,
            out Proc process,
            out Step step,
            out string error)
        {
            process = null;
            step = null;
            error = null;
            if (procIndex < 0 || procIndex >= runtime.Stores.Processes.Items.Count)
            {
                error = "流程索引无效。";
                return false;
            }
            process = runtime.Stores.Processes.Items[procIndex];
            if (stepIndex < 0 || stepIndex >= (process?.steps?.Count ?? 0))
            {
                error = "步骤索引无效。";
                return false;
            }
            step = process.steps[stepIndex];
            if (step?.Ops == null)
            {
                error = "步骤指令列表为空。";
                return false;
            }
            if (runtime.ProcessEditing.CanEditProcess(procIndex)) return true;
            error = "流程当前不可编辑。";
            return false;
        }
    }
}
