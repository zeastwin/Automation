using System;
// 模块：引擎 / 结构编辑。
// 职责范围：执行流程与指令结构变换、跳转重写、发布门禁和变量生命周期处理。

using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 流程编辑门禁。运行状态判断与提示统一从这里产生。
    /// </summary>
    public sealed class ProcessEditingPolicy
    {
        private readonly PlatformRuntime runtime;

        internal ProcessEditingPolicy(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool CanEditProcess(int procIndex)
        {
            if (runtime.Maintenance.Active)
            {
                Warn(runtime.Maintenance.Reason, "系统正在执行配置维护");
                return false;
            }
            if (runtime.Safety.IsLocked)
            {
                runtime.EditorUi?.ShowMessage(runtime.Safety.LockReason, "系统已安全锁定", true);
                return false;
            }
            if (procIndex < 0 || procIndex >= runtime.Stores.Processes.Items.Count)
            {
                return false;
            }
            if (runtime.ProcessEngine == null)
            {
                Warn("流程引擎未初始化，禁止编辑流程。", "流程编辑不可用");
                return false;
            }
            return true;
        }

        public bool CanEditStructure()
        {
            if (runtime.Maintenance.Active)
            {
                Warn(runtime.Maintenance.Reason, "系统正在执行配置维护");
                return false;
            }
            for (int i = 0; i < runtime.Stores.Processes.Items.Count; i++)
            {
                EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(i);
                if (snapshot != null && !snapshot.State.IsInactive())
                {
                    Warn("流程列表的新增、删除、复制或重排会改变procIndex，仅允许在全部流程处于就绪或停止后操作。流程内部的参数和步骤/指令编辑不受此门禁影响。",
                        "流程结构不可编辑");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 变量定义使用原子快照发布，新增、删除、改名、类型及作用域调整
        /// 不会再暴露半张变量表，因此不复用会改变 procIndex 的流程列表门禁。
        /// 运行流程若继续访问已删除或身份发生变化的变量，会由稳定ID校验明确失败。
        /// </summary>
        public bool CanEditVariableConfiguration()
        {
            if (runtime.Maintenance.Active)
            {
                Warn(runtime.Maintenance.Reason, "系统正在执行配置维护");
                return false;
            }
            if (runtime.Safety.IsLocked)
            {
                runtime.EditorUi?.ShowMessage(
                    runtime.Safety.LockReason,
                    "系统已安全锁定",
                    true);
                return false;
            }
            return true;
        }

        public bool CanApplyVariableConfiguration(
            IEnumerable<DicValue> currentVariables,
            IEnumerable<DicValue> candidateVariables,
            out string error)
        {
            error = null;
            if (!CanEditVariableConfiguration())
            {
                error = runtime.Maintenance.Active
                    ? runtime.Maintenance.Reason ?? "系统正在执行配置维护。"
                    : runtime.Safety.IsLocked
                        ? runtime.Safety.LockReason ?? "系统已安全锁定。"
                        : "变量配置当前不可编辑。";
                return false;
            }

            var candidatesById = (candidateVariables ?? Enumerable.Empty<DicValue>())
                .Where(value => value != null && value.Id != Guid.Empty)
                .GroupBy(value => value.Id)
                .ToDictionary(group => group.Key, group => group.First());
            List<DicValue> affected = (currentVariables ?? Enumerable.Empty<DicValue>())
                .Where(value => value != null && value.Id != Guid.Empty)
                .Where(value =>
                    !candidatesById.TryGetValue(value.Id, out DicValue candidate)
                    || !HasSameRuntimeContract(value, candidate))
                .ToList();
            if (affected.Count == 0)
            {
                return true;
            }

            var affectedNames = new HashSet<string>(
                affected.Select(value => value.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);
            var affectedIndexes = new HashSet<int>(
                affected.Select(value => value.Index));
            var affectedOwnerIds = new HashSet<Guid>(
                affected.Where(ValueConfigStore.IsProcessValue)
                    .Select(value => value.OwnerProcId ?? Guid.Empty)
                    .Where(id => id != Guid.Empty));
            var activeProcessNames = new List<string>();
            for (int procIndex = 0; procIndex < runtime.Stores.Processes.Items.Count; procIndex++)
            {
                EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
                if (snapshot == null || snapshot.State.IsInactive())
                {
                    continue;
                }
                Proc process = runtime.Stores.Processes.Items[procIndex];
                Guid processId = process?.head?.Id ?? Guid.Empty;
                bool privateVariableMayBeUsed = affectedOwnerIds.Contains(processId);
                bool hasKnownReference = process?.steps != null
                    && process.steps.Where(step => step?.Ops != null)
                        .SelectMany(step => step.Ops)
                        .Where(operation => operation != null)
                        .SelectMany(VariableReferenceCatalog.Enumerate)
                        .Any(reference =>
                            reference.Kind == VariableReferenceKind.Name
                                ? affectedNames.Contains(reference.Value)
                                : int.TryParse(reference.Value, out int index)
                                    && affectedIndexes.Contains(index));
                if (privateVariableMayBeUsed || hasKnownReference)
                {
                    activeProcessNames.Add(
                        process?.head?.Name
                        ?? snapshot.ProcName
                        ?? $"流程{procIndex}");
                }
            }
            if (activeProcessNames.Count == 0)
            {
                return true;
            }

            string variableNames = string.Join(
                "、",
                affected.Select(value => value.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .Take(5));
            string processNames = string.Join(
                "、",
                activeProcessNames.Distinct(StringComparer.Ordinal).Take(5));
            error = $"变量[{variableNames}]正在被运行中的流程[{processNames}]引用。"
                + "请只停止受影响流程后再修改变量定义，其他流程无需停止；当前值调试仍可在线修改。";
            return false;
        }

        private static bool HasSameRuntimeContract(DicValue current, DicValue candidate)
        {
            return current != null
                && candidate != null
                && current.Id == candidate.Id
                && current.Index == candidate.Index
                && string.Equals(current.Name, candidate.Name, StringComparison.Ordinal)
                && string.Equals(current.Type, candidate.Type, StringComparison.Ordinal)
                && string.Equals(current.Scope, candidate.Scope, StringComparison.Ordinal)
                && current.OwnerProcId == candidate.OwnerProcId;
        }

        private void Warn(string message, string title)
        {
            runtime.EditorUi?.ShowMessage(message, title, false);
        }
    }

    /// <summary>
    /// 将编辑态流程验证、克隆并发布到运行引擎。
    /// </summary>
    public sealed class ProcessPublicationService
    {
        private readonly PlatformRuntime runtime;

        internal ProcessPublicationService(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool Publish(int procIndex)
        {
            if (runtime.Maintenance.Active)
            {
                runtime.EditorUi?.ShowMessage(runtime.Maintenance.Reason,
                    "系统正在执行配置维护", false);
                return false;
            }
            List<Proc> processes = runtime.Stores.Processes.Items;
            if (runtime.ProcessEngine == null)
            {
                runtime.EditorUi?.ShowMessage("流程引擎未初始化，无法发布。", "流程发布失败", true);
                return false;
            }
            if (procIndex < 0 || procIndex >= processes.Count)
            {
                runtime.EditorUi?.ShowMessage("流程索引无效，无法发布。", "流程发布失败", true);
                return false;
            }

            Proc draft = processes[procIndex];
            var errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(
                procIndex, draft, errors, runtime.CreateProcessValidationContext());
            if (errors.Count > 0)
            {
                return Fail("流程发布失败：\r\n" + string.Join("\r\n", errors.Distinct()));
            }
            Proc runtimeProcess = ObjectGraphCloner.Clone(draft);
            if (!runtime.ProcessEngine.PublishProc(procIndex, runtimeProcess, out string error))
            {
                return Fail(string.IsNullOrWhiteSpace(error) ? "流程发布失败" : $"流程发布失败：{error}");
            }
            return true;
        }

        private bool Fail(string message)
        {
            runtime.EditorUi?.ShowMessage(message, "流程发布失败", true);
            runtime.EditorUi?.WriteInfo(message, LogLevel.Error);
            return false;
        }
    }
}
