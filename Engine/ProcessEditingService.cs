using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Automation
{
    public sealed class GotoRewriteResult
    {
        public int RewrittenCount { get; internal set; }

        public int InvalidatedCount { get; internal set; }
    }

    /// <summary>
    /// 流程结构编辑的共享实现。手工界面和 AI Bridge 必须复用本服务，禁止各自维护索引重排逻辑。
    /// </summary>
    public static class ProcessEditingService
    {
        public static GotoRewriteResult RewriteGotoTargets(Proc before, Proc after, int procIndex)
        {
            var result = new GotoRewriteResult();
            Dictionary<Guid, OperationLocation> newLocations = BuildOperationLocationMap(after);
            Dictionary<Guid, OperationType> originalOperations = BuildOperationMap(before);
            if (after?.steps == null)
            {
                return result;
            }

            foreach (Step step in after.steps)
            {
                if (step?.Ops == null)
                {
                    continue;
                }
                foreach (OperationType operation in step.Ops)
                {
                    if (operation == null || operation.Id == Guid.Empty
                        || !originalOperations.TryGetValue(operation.Id, out OperationType originalOperation))
                    {
                        // 新增指令的跳转地址以最终草稿为准，不得按旧流程索引重写。
                        continue;
                    }
                    RewriteGotoTargetsRecursive(operation, originalOperation, before, procIndex, newLocations, result);
                }
            }
            RenumberOperations(after);
            return result;
        }

        /// <summary>
        /// 将手工新增指令中基于插入前列表选择的跳转地址，按目标指令 ID 重定位到插入后的地址。
        /// AI ChangeSet 中新增指令继续使用最终草稿语义；本方法仅用于手工新增编辑会话。
        /// </summary>
        public static GotoRewriteResult RewriteAddedOperationGotoTargetsFromPreviousLayout(
            OperationType addedOperation,
            Proc before,
            Proc after,
            int procIndex)
        {
            var result = new GotoRewriteResult();
            if (addedOperation == null || before == null || after == null)
            {
                return result;
            }

            Dictionary<Guid, OperationLocation> newLocations = BuildOperationLocationMap(after);
            RewriteGotoTargetsRecursive(
                addedOperation,
                addedOperation,
                before,
                procIndex,
                newLocations,
                result);
            return result;
        }

        public static bool TryCommitProcDraft(int procIndex, Proc draft, out string error)
        {
            error = null;
            if (SF.frmProc?.procsList == null || SF.mainfrm == null || SF.DR == null)
            {
                error = "流程编辑依赖尚未初始化。";
                return false;
            }
            if (SF.SecurityLocked)
            {
                error = $"系统已安全锁定：{SF.SecurityLockReason}";
                return false;
            }
            if (SF.MaintenanceActive)
            {
                error = string.IsNullOrWhiteSpace(SF.MaintenanceReason)
                    ? "系统正在执行配置维护，禁止提交流程编辑。"
                    : $"系统正在执行配置维护：{SF.MaintenanceReason}";
                return false;
            }
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count || draft == null)
            {
                error = $"流程草稿或索引无效：{procIndex}。";
                return false;
            }

            Proc original = ObjectGraphCloner.Clone(SF.frmProc.procsList[procIndex]);
            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(procIndex, draft, errors);
            if (draft.head != null && draft.head.Id != Guid.Empty
                && SF.frmProc.procsList.Where((proc, index) => index != procIndex)
                    .Any(proc => proc?.head?.Id == draft.head.Id))
            {
                errors.Add($"流程{procIndex}的ID与其他流程重复：{draft.head.Id:D}");
            }
            errors.AddRange(ProcessDefinitionService.ValidateProcGotoTargets(procIndex, draft));
            if (errors.Count > 0)
            {
                error = string.Join("\r\n", errors.Distinct());
                return false;
            }

            Proc runtimeDraft = ObjectGraphCloner.Clone(draft);
            if (!SF.DR.ValidateProcUpdate(procIndex, runtimeDraft, out error))
            {
                return false;
            }
            if (!AtomicJsonFileStore.Save(SF.workPath, procIndex.ToString(), draft))
            {
                error = "流程文件保存失败，正式内存未修改。";
                return false;
            }

            SF.frmProc.procsList[procIndex] = draft;
            if (SF.DR.PublishProc(procIndex, runtimeDraft, out string publishError))
            {
                SF.frmProc.RefreshProcView(procIndex);
                return true;
            }

            SF.frmProc.procsList[procIndex] = original;
            bool fileRestored = AtomicJsonFileStore.Save(SF.workPath, procIndex.ToString(), original);
            bool runtimeRestored = SF.DR.PublishProc(procIndex, ObjectGraphCloner.Clone(original), out string rollbackPublishError);
            SF.frmProc.RefreshProcView(procIndex);
            if (!fileRestored || !runtimeRestored)
            {
                string rollbackError = $"流程{procIndex}发布失败且回滚不完整：fileRestored={fileRestored}, "
                    + $"runtimeRestored={runtimeRestored}, publishError={publishError}, rollbackError={rollbackPublishError}";
                SF.SetSecurityLock(rollbackError);
                error = rollbackError;
                return false;
            }
            error = $"流程发布失败，磁盘、内存和运行时已恢复：{publishError}";
            return false;
        }

        public static void RenumberOperations(Proc proc)
        {
            if (proc?.steps == null)
            {
                return;
            }
            foreach (Step step in proc.steps)
            {
                if (step?.Ops == null)
                {
                    continue;
                }
                for (int i = 0; i < step.Ops.Count; i++)
                {
                    if (step.Ops[i] != null)
                    {
                        step.Ops[i].Num = i;
                    }
                }
            }
        }

        public static void AdaptGotoProcIndex(object root, int procIndex)
        {
            if (root == null || procIndex < 0)
            {
                return;
            }
            if (root is Proc proc)
            {
                AdaptGotoProcIndex(proc.steps, procIndex);
                return;
            }
            if (root is Step step)
            {
                AdaptGotoProcIndex(step.Ops, procIndex);
                return;
            }
            if (root is IEnumerable rootItems && !(root is string))
            {
                foreach (object item in rootItems)
                {
                    AdaptGotoProcIndex(item, procIndex);
                }
                return;
            }
            foreach (PropertyInfo property in root.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead)
                {
                    continue;
                }
                if (property.PropertyType == typeof(string) && property.CanWrite
                    && property.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string value = property.GetValue(root) as string;
                    if (!string.IsNullOrWhiteSpace(value)
                        && ProcessDefinitionService.TryParseGotoKey(value, out _, out int stepIndex, out int opIndex))
                    {
                        property.SetValue(root, $"{procIndex}-{stepIndex}-{opIndex}");
                    }
                }
                object valueObject = property.GetValue(root);
                if (valueObject is IEnumerable enumerable && !(valueObject is string))
                {
                    foreach (object item in enumerable)
                    {
                        AdaptGotoProcIndex(item, procIndex);
                    }
                }
            }
        }

        public static void AdaptGotoProcIndexes(IList<Proc> processes, int StartIndex)
        {
            if (processes == null || processes.Count == 0)
            {
                return;
            }
            if (StartIndex < 0 || StartIndex > processes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(StartIndex), "流程重排起始索引超出范围");
            }
            for (int i = StartIndex; i < processes.Count; i++)
            {
                AdaptGotoProcIndex(processes[i], i);
            }
        }

        private static void RewriteGotoTargetsRecursive(
            object currentObject,
            object originalObject,
            Proc before,
            int procIndex,
            Dictionary<Guid, OperationLocation> newLocations,
            GotoRewriteResult result)
        {
            if (currentObject == null)
            {
                return;
            }
            foreach (PropertyInfo property in currentObject.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead)
                {
                    continue;
                }
                if (property.PropertyType == typeof(string)
                    && property.CanWrite
                    && property.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string currentValue = property.GetValue(currentObject) as string;
                    string originalValue = originalObject == null || originalObject.GetType() != currentObject.GetType()
                        ? null
                        : property.GetValue(originalObject) as string;
                    RewriteSingleGotoTarget(currentObject, property, currentValue, originalValue,
                        before, procIndex, newLocations, result);
                }
                object value = property.GetValue(currentObject);
                if (value is IEnumerable enumerable && !(value is string))
                {
                    List<object> originalItems = new List<object>();
                    if (originalObject != null && originalObject.GetType() == currentObject.GetType()
                        && property.GetValue(originalObject) is IEnumerable originalEnumerable
                        && !(originalEnumerable is string))
                    {
                        originalItems.AddRange(originalEnumerable.Cast<object>());
                    }
                    int itemIndex = 0;
                    foreach (object item in enumerable)
                    {
                        object originalItem = itemIndex < originalItems.Count ? originalItems[itemIndex] : null;
                        RewriteGotoTargetsRecursive(item, originalItem, before, procIndex, newLocations, result);
                        itemIndex++;
                    }
                }
            }
        }

        private static void RewriteSingleGotoTarget(
            object currentObject,
            PropertyInfo property,
            string currentValue,
            string originalValue,
            Proc before,
            int procIndex,
            Dictionary<Guid, OperationLocation> newLocations,
            GotoRewriteResult result)
        {
            if (string.IsNullOrWhiteSpace(currentValue)
                || !string.Equals(currentValue, originalValue, StringComparison.Ordinal)
                || !ProcessDefinitionService.TryParseGotoKey(originalValue, out int gotoProc, out int gotoStep, out int gotoOp)
                || gotoProc != procIndex)
            {
                return;
            }

            if (TryResolveTargetOperationId(before, gotoStep, gotoOp, out Guid targetId)
                && newLocations.TryGetValue(targetId, out OperationLocation target))
            {
                string newValue = $"{procIndex}-{target.StepIndex}-{target.OpIndex}";
                if (!string.Equals(newValue, currentValue, StringComparison.Ordinal))
                {
                    property.SetValue(currentObject, newValue);
                    result.RewrittenCount++;
                }
                return;
            }

            // 不清空、不选择附近指令。保留原地址并标记为草稿，后续阶段再明确修复跳转。
            property.SetValue(currentObject, ProcessDefinitionService.DeletedGotoPrefix + currentValue);
            result.InvalidatedCount++;
        }

        private static Dictionary<Guid, OperationType> BuildOperationMap(Proc proc)
        {
            var result = new Dictionary<Guid, OperationType>();
            if (proc?.steps == null)
            {
                return result;
            }
            foreach (Step step in proc.steps)
            {
                if (step?.Ops == null)
                {
                    continue;
                }
                foreach (OperationType operation in step.Ops)
                {
                    if (operation != null && operation.Id != Guid.Empty)
                    {
                        result[operation.Id] = operation;
                    }
                }
            }
            return result;
        }

        private static bool TryResolveTargetOperationId(Proc proc, int stepIndex, int opIndex, out Guid id)
        {
            id = Guid.Empty;
            if (proc?.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                return false;
            }
            List<OperationType> operations = proc.steps[stepIndex]?.Ops;
            if (operations == null || opIndex < 0 || opIndex >= operations.Count || operations[opIndex] == null)
            {
                return false;
            }
            id = operations[opIndex].Id;
            return id != Guid.Empty;
        }

        private static Dictionary<Guid, OperationLocation> BuildOperationLocationMap(Proc proc)
        {
            var result = new Dictionary<Guid, OperationLocation>();
            if (proc?.steps == null)
            {
                return result;
            }
            for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
            {
                List<OperationType> operations = proc.steps[stepIndex]?.Ops;
                if (operations == null)
                {
                    continue;
                }
                for (int opIndex = 0; opIndex < operations.Count; opIndex++)
                {
                    OperationType operation = operations[opIndex];
                    if (operation?.Id != Guid.Empty)
                    {
                        result[operation.Id] = new OperationLocation(stepIndex, opIndex);
                    }
                }
            }
            return result;
        }

        private sealed class OperationLocation
        {
            public OperationLocation(int stepIndex, int opIndex)
            {
                StepIndex = stepIndex;
                OpIndex = opIndex;
            }

            public int StepIndex { get; }

            public int OpIndex { get; }
        }
    }
}
