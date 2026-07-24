using System;
// 模块：运行时 / 编辑协作。
// 职责范围：管理编辑会话、历史、剪贴板、联合提交以及编辑器 UI 适配边界。

using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    public sealed class ProcessVariableConfigurationCommitResult
    {
        internal ProcessVariableConfigurationCommitResult(
            bool succeeded,
            string message,
            string detail,
            bool postCommitFailure,
            bool rollbackIncomplete)
        {
            Succeeded = succeeded;
            Message = message;
            Detail = detail;
            PostCommitFailure = postCommitFailure;
            RollbackIncomplete = rollbackIncomplete;
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public string Detail { get; }
        public bool PostCommitFailure { get; }
        public bool RollbackIncomplete { get; }
    }

    /// <summary>
    /// 统一协调流程结构与变量配置的联合提交、界面刷新和撤销记录。
    /// </summary>
    public sealed class ProcessVariableConfigurationService
    {
        private readonly PlatformRuntime runtime;

        internal ProcessVariableConfigurationService(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool TryCommit(
            IList<Proc> processes,
            IDictionary<string, DicValue> variables,
            string historyDescription,
            out string error,
            bool recordHistory = true)
        {
            ProcessVariableConfigurationCommitResult result = Commit(
                processes,
                variables,
                historyDescription,
                recordHistory,
                false,
                null,
                null);
            error = result.Message;
            return result.Succeeded;
        }

        public ProcessVariableConfigurationCommitResult CommitChangeSet(
            IList<Proc> processes,
            IDictionary<string, DicValue> variables,
            IReadOnlyDictionary<Guid, string> runtimeValueOverrides)
        {
            if (runtime.Editor.ActiveSession != null)
            {
                return Failed(
                    $"当前存在未完成的编辑会话：{runtime.Editor.ActiveSession.Name}。"
                    + "请先保存或取消，再提交 AI 变更集。");
            }
            return Commit(
                processes,
                variables,
                "语义变更集",
                false,
                true,
                runtimeValueOverrides,
                "ChangeSet变量值提交");
        }

        private ProcessVariableConfigurationCommitResult Commit(
            IList<Proc> processes,
            IDictionary<string, DicValue> variables,
            string actionName,
            bool recordHistory,
            bool clearHistory,
            IReadOnlyDictionary<Guid, string> runtimeValueOverrides,
            string runtimeValueSource)
        {
            if (!runtime.ProcessEditing.CanEditStructure())
                return Failed("流程结构当前不可编辑。");
            if (processes == null || variables == null)
                return Failed("流程或变量提交数据为空。");
            if (recordHistory && !runtime.Editor.History.IsReplaying
                && string.IsNullOrWhiteSpace(actionName))
                return Failed("流程结构提交的历史说明为空。");

            List<Proc> oldProcesses = runtime.Stores.Processes.CreateSnapshot();
            Dictionary<string, DicValue> oldVariables = runtime.Stores.Values.BuildSaveData();
            List<Proc> committedProcesses = processes.Select(ObjectGraphCloner.Clone).ToList();
            Dictionary<string, DicValue> committedVariables = variables.ToDictionary(
                item => item.Key,
                item => ObjectGraphCloner.Clone(item.Value),
                StringComparer.Ordinal);

            if (!ProcessVariableConfigurationTransaction.Commit(
                runtime.Paths.ConfigPath,
                committedProcesses,
                committedVariables,
                out string commitError,
                out bool rollbackFailed))
            {
                if (rollbackFailed) runtime.Safety.Lock(commitError);
                return Failed(commitError, null, false, rollbackFailed);
            }

            try
            {
                RefreshCommittedState(
                    committedProcesses,
                    committedVariables,
                    runtimeValueOverrides,
                    runtimeValueSource);
                if (recordHistory && !runtime.Editor.History.IsReplaying)
                {
                    runtime.Editor.History.Record(
                        actionName,
                        delegate(out string historyError)
                        {
                            return TryCommit(
                                oldProcesses,
                                oldVariables,
                                actionName,
                                out historyError,
                                false);
                        },
                        delegate(out string historyError)
                        {
                            return TryCommit(
                                committedProcesses,
                                committedVariables,
                                actionName,
                                out historyError,
                                false);
                        });
                }
                else if (clearHistory && !runtime.Editor.History.IsReplaying)
                {
                    runtime.Editor.History.Clear();
                }
                return new ProcessVariableConfigurationCommitResult(
                    true, null, null, false, false);
            }
            catch (Exception ex)
            {
                bool diskRestored = ProcessVariableConfigurationTransaction.Commit(
                    runtime.Paths.ConfigPath,
                    oldProcesses,
                    oldVariables,
                    out string restoreError,
                    out bool restoreRollbackFailed);
                bool memoryRestored = true;
                try
                {
                    RefreshCommittedState(oldProcesses, oldVariables, null, null);
                }
                catch
                {
                    memoryRestored = false;
                }

                bool rollbackIncomplete = !diskRestored || !memoryRestored || restoreRollbackFailed;
                string error = $"{actionName}提交后刷新失败：{ex.Message}";
                if (rollbackIncomplete)
                {
                    error += $"；回滚不完整：{restoreError}";
                    runtime.Safety.Lock(error);
                }
                return Failed(error, ex.Message, true, rollbackIncomplete);
            }
        }

        private void RefreshCommittedState(
            IList<Proc> processes,
            IDictionary<string, DicValue> variables,
            IReadOnlyDictionary<Guid, string> runtimeValueOverrides,
            string runtimeValueSource)
        {
            if (runtime.EditorUi?.IsReady != true)
            {
                throw new InvalidOperationException("平台编辑器刷新边界尚未初始化。");
            }
            ValueConfigStore.ValidateProcessOwners(variables.Values, processes);
            runtime.Stores.Values.ReplaceConfiguration(variables);
            foreach (KeyValuePair<Guid, string> valueOverride in
                runtimeValueOverrides ?? new Dictionary<Guid, string>())
            {
                DicValue target = variables.Values.FirstOrDefault(value =>
                    value != null && value.Id == valueOverride.Key);
                if (target == null
                    || !runtime.Stores.Values.setValueByName(
                        target.Name,
                        valueOverride.Value,
                        runtimeValueSource))
                {
                    throw new InvalidOperationException(
                        $"变量当前值提交失败：{target?.Name ?? valueOverride.Key.ToString("D")}");
                }
            }
            // 流程加载的校验上下文会读取变量 Store，因此先切换本次同一事务的变量快照。
            runtime.EditorUi.RefreshProcesses();
            runtime.EditorUi.RefreshVariables();
        }

        private static ProcessVariableConfigurationCommitResult Failed(
            string message,
            string detail = null,
            bool postCommitFailure = false,
            bool rollbackIncomplete = false)
        {
            return new ProcessVariableConfigurationCommitResult(
                false,
                message,
                detail,
                postCommitFailure,
                rollbackIncomplete);
        }
    }
}
