using Automation.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static Automation.OperationTypePartial;

namespace Automation
{
    public sealed class AiChangeSetCompileResult
    {
        public List<Proc> Processes { get; internal set; }

        public Dictionary<string, DicValue> Variables { get; internal set; }

        public JArray Changes { get; internal set; }

        public int DeletedProcessCount { get; internal set; }

        public int CreatedProcessCount { get; internal set; }

        public int ReplacedProcessCount { get; internal set; }

        public int ChangedVariableCount { get; internal set; }

        public int OperationCount { get; internal set; }

        public JArray ProcessAnalyses { get; internal set; }

        public int AtomicActionCount { get; internal set; }

        public JObject CreatedObjects { get; internal set; }

        public string ReadinessStatus { get; internal set; }

        public bool Runnable { get; internal set; }

        public JArray ConfigurationWarnings { get; internal set; }

        public JArray RunBlockers { get; internal set; }

        public JArray StageIssues { get; internal set; }
    }

    /// <summary>
    /// 把 AI 的业务语义变更编译为平台现有 Proc/Step/OperationType 模型。
    /// 编译过程不读 PropertyGrid，也不依赖窗体当前选中项。
    /// </summary>
    public static class AiChangeSetCompiler
    {
        public const int MaxNameLength = 64;

        private static readonly HashSet<string> ProtectedVariables = new HashSet<string>(StringComparer.Ordinal)
        {
            "复位状态",
            "系统状态"
        };

        public static IReadOnlyCollection<string> OperationKinds => AiOperationCompilerRegistry.Kinds;

        public static AiChangeSetCompileResult Compile(
            AiChangeSet changeSet,
            IList<Proc> currentProcesses,
            IDictionary<string, DicValue> currentVariables,
            AiResourceSnapshot resources = null)
        {
            if (changeSet == null)
            {
                throw new InvalidOperationException("changeSet 不能为空。");
            }
            if (changeSet.Version != 2)
            {
                throw new InvalidOperationException($"changeSet.version 必须为 2，当前为 {changeSet.Version}。");
            }

            int atomicActionCount = changeSet.Actions?.Count ?? 0;
            changeSet = ExpandAtomicActions(changeSet, currentProcesses);

            List<Proc> processes = (currentProcesses ?? Array.Empty<Proc>())
                .Select(ObjectGraphCloner.Clone).ToList();
            Dictionary<string, DicValue> variables = CloneVariables(currentVariables);
            var changes = new JArray();

            int deletedCount = ApplyProcessDeletion(changeSet.DeleteProcesses, processes, changes);
            if (deletedCount > 0)
            {
                ProcessEditingService.AdaptGotoProcIndexes(processes, 0);
            }
            int variableCount = ApplyVariableChanges(changeSet.Variables, variables, changes);
            int operationCount = 0;
            int replacedCount;
            var createdObjects = new JObject
            {
                ["processes"] = new JArray(),
                ["steps"] = new JArray(),
                ["operations"] = new JArray()
            };
            int createdCount = ApplyProcessDefinitions(
                changeSet.Processes, processes, variables, resources, changes, ref operationCount,
                createdObjects, out replacedCount);

            if (deletedCount == 0 && variableCount == 0 && createdCount == 0 && replacedCount == 0)
            {
                throw new InvalidOperationException("changeSet 不包含任何变更。");
            }

            IReadOnlyCollection<string> tcpNames = GetReferenceValues(resources, "comm.tcp");
            IReadOnlyCollection<string> serialNames = GetReferenceValues(resources, "comm.serial");
            IReadOnlyCollection<string> alarmInfoIds = GetReferenceValues(resources, "alarm.infoId");
            IReadOnlyCollection<string> plcNames = GetReferenceValues(resources, "plc.device");
            var validationContext = new ProcessDefinitionValidationContext(
                variables.Keys, tcpNames, serialNames, alarmInfoIds, plcNames, variables);
            for (int procIndex = 0; procIndex < processes.Count; procIndex++)
            {
                var errors = new List<string>();
                ProcessDefinitionService.NormalizeProc(
                    procIndex, processes[procIndex], errors, validationContext);
                errors.AddRange(ProcessDefinitionService.ValidateProcGotoTargets(procIndex, processes[procIndex]));
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"流程[{processes[procIndex]?.head?.Name ?? procIndex.ToString()}]校验失败："
                        + string.Join("；", errors.Distinct()));
                }
            }
            JArray processAnalyses = BuildChangedProcessAnalyses(
                processes, changes, validationContext);
            JArray configurationWarnings = FlattenReadinessMessages(processAnalyses, "warnings");
            JArray runBlockers = FlattenReadinessMessages(processAnalyses, "runBlockers");
            JArray previewChanges = atomicActionCount == 0
                ? changes
                : BuildAtomicPreviewChanges(changes);
            JArray stageIssues = BuildStageIssues(previewChanges);
            string readinessStatus = processAnalyses.OfType<JObject>().Any(item =>
                    string.Equals(item["readinessStatus"]?.Value<string>(), "invalid", StringComparison.Ordinal))
                ? "invalid"
                : processAnalyses.OfType<JObject>().Any(item =>
                    !string.Equals(item["readinessStatus"]?.Value<string>(), "ready", StringComparison.Ordinal))
                    ? "incomplete"
                    : "ready";

            return new AiChangeSetCompileResult
            {
                Processes = processes,
                Variables = variables,
                Changes = previewChanges,
                DeletedProcessCount = deletedCount,
                CreatedProcessCount = createdCount,
                ReplacedProcessCount = replacedCount,
                ChangedVariableCount = variableCount,
                OperationCount = operationCount,
                ProcessAnalyses = processAnalyses,
                AtomicActionCount = atomicActionCount,
                CreatedObjects = createdObjects,
                ReadinessStatus = readinessStatus,
                Runnable = processAnalyses.OfType<JObject>().All(item => item["runnable"]?.Value<bool>() == true),
                ConfigurationWarnings = configurationWarnings,
                RunBlockers = runBlockers,
                StageIssues = stageIssues
            };
        }

        private static JArray BuildStageIssues(JArray changes)
        {
            var result = new JArray();
            foreach (JObject change in changes?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                int invalidatedGotoCount = change["invalidatedGotoCount"]?.Value<int>() ?? 0;
                if (invalidatedGotoCount <= 0) continue;
                result.Add(new JObject
                {
                    ["category"] = "current_stage_invalidated_reference",
                    ["procId"] = change["procId"]?.DeepClone(),
                    ["procIndex"] = change["procIndex"]?.DeepClone(),
                    ["name"] = change["name"]?.DeepClone(),
                    ["count"] = invalidatedGotoCount,
                    ["message"] = $"本阶段使 {invalidatedGotoCount} 个既有跳转目标失效。"
                });
            }
            return result;
        }

        private static JArray BuildAtomicPreviewChanges(JArray compiledChanges)
        {
            var result = (JArray)compiledChanges.DeepClone();
            foreach (JObject change in result.OfType<JObject>().Where(item =>
                string.Equals(item["type"]?.Value<string>(), "process.replace", StringComparison.Ordinal)))
            {
                change["type"] = "process.modify";
            }
            return result;
        }

        private static AiChangeSet ExpandAtomicActions(AiChangeSet source, IList<Proc> currentProcesses)
        {
            if ((source.Actions?.Count ?? 0) == 0)
            {
                return source;
            }
            if (source.DeleteProcesses != null || (source.Variables?.Count ?? 0) > 0
                || (source.Processes?.Count ?? 0) > 0)
            {
                throw new InvalidOperationException(
                    "changeSet.actions 不得与旧的 deleteProcesses、variables 或 processes 混用。");
            }

            var states = (currentProcesses ?? Array.Empty<Proc>())
                .Select(proc => new AtomicProcessState(proc)).ToList();
            var deletedIds = new HashSet<Guid>();
            for (int actionIndex = 0; actionIndex < source.Actions.Count; actionIndex++)
            {
                ChangeSetAction action = source.Actions[actionIndex]
                    ?? throw new InvalidOperationException($"actions[{actionIndex}] 不能为空。");
                string type = RequiredText(action.Type, $"actions[{actionIndex}].type");
                string path = $"actions[{actionIndex}]({type})";
                switch (type)
                {
                    case "process.create":
                        EnsureActionShape(action, path, allowProcess: true);
                        AddProcessState(states, action.Process, path);
                        break;
                    case "process.update":
                        EnsureActionShape(action, path, allowTargetProcess: true, allowProcess: true);
                        UpdateProcessState(ResolveProcessState(states, action.TargetProcess, path),
                            action.Process, path);
                        break;
                    case "process.delete":
                        EnsureActionShape(action, path, allowTargetProcess: true);
                        DeleteProcessState(ResolveProcessState(states, action.TargetProcess, path), deletedIds, path);
                        break;
                    case "process.delete_all":
                        EnsureActionShape(action, path);
                        foreach (AtomicProcessState state in states.Where(item => !item.Deleted).ToList())
                        {
                            DeleteProcessState(state, deletedIds, path);
                        }
                        break;
                    case "step.append":
                    case "step.insert":
                        EnsureActionShape(action, path, allowTargetProcess: true,
                            allowPosition: type == "step.insert", allowStep: true);
                        InsertStep(ResolveProcessState(states, action.TargetProcess, path), action.Step,
                            type == "step.append" ? null : action.Position, path);
                        break;
                    case "step.update":
                        EnsureActionShape(action, path, allowTargetProcess: true, allowTargetStep: true,
                            allowStep: true);
                        UpdateStep(ResolveProcessState(states, action.TargetProcess, path),
                            action.TargetStep, action.Step, path);
                        break;
                    case "step.delete":
                        EnsureActionShape(action, path, allowTargetProcess: true, allowTargetStep: true);
                        DeleteStep(ResolveProcessState(states, action.TargetProcess, path),
                            action.TargetStep, path);
                        break;
                    case "step.move":
                        EnsureActionShape(action, path, allowTargetProcess: true, allowTargetStep: true,
                            allowPosition: true);
                        MoveStep(ResolveProcessState(states, action.TargetProcess, path),
                            action.TargetStep, action.Position, path);
                        break;
                    case "operation.append":
                    case "operation.insert":
                        EnsureActionShape(action, path, allowTargetProcess: true, allowTargetStep: true,
                            allowPosition: type == "operation.insert", allowOperation: true);
                        InsertOperation(ResolveProcessState(states, action.TargetProcess, path),
                            action.TargetStep, action.Operation,
                            type == "operation.append" ? null : action.Position, path);
                        break;
                    case "operation.update":
                        EnsureActionShape(action, path, allowTargetProcess: true, allowTargetStep: true,
                            allowTargetOperation: true, allowOperation: true);
                        UpdateOperation(ResolveProcessState(states, action.TargetProcess, path),
                            action.TargetStep, action.TargetOperation, action.Operation, path);
                        break;
                    case "operation.replace":
                        EnsureActionShape(action, path, allowTargetProcess: true, allowTargetStep: true,
                            allowTargetOperation: true, allowOperation: true);
                        ReplaceOperation(ResolveProcessState(states, action.TargetProcess, path),
                            action.TargetStep, action.TargetOperation, action.Operation, path);
                        break;
                    case "operation.delete":
                        EnsureActionShape(action, path, allowTargetProcess: true, allowTargetStep: true,
                            allowTargetOperation: true);
                        DeleteOperation(ResolveProcessState(states, action.TargetProcess, path),
                            action.TargetStep, action.TargetOperation, path);
                        break;
                    case "operation.move":
                        EnsureActionShape(action, path, allowTargetProcess: true, allowTargetStep: true,
                            allowTargetOperation: true, allowPosition: true);
                        MoveOperation(ResolveProcessState(states, action.TargetProcess, path),
                            action.TargetStep, action.TargetOperation, action.Position, path);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"{path}.type 不受支持。允许值：{ChangeSetActionTypes.SupportedTypes}。");
                }
            }

            List<ProcessDefinition> processDefinitions = states
                .Where(state => state.Touched && !state.Deleted)
                .Select(state => state.Definition).ToList();
            if (processDefinitions.Count == 0 && deletedIds.Count == 0)
            {
                throw new InvalidOperationException("changeSet.actions 未产生有效变更。");
            }
            return new AiChangeSet
            {
                Version = 2,
                Title = source.Title,
                DeleteProcesses = deletedIds.Count == 0 ? null : new ProcessDeleteSelection
                {
                    Mode = "selected",
                    ProcIds = deletedIds.Select(id => id.ToString("D")).ToList()
                },
                Processes = processDefinitions
            };
        }

        private static void EnsureActionShape(ChangeSetAction action, string path,
            bool allowTargetProcess = false, bool allowTargetStep = false,
            bool allowTargetOperation = false, bool allowPosition = false,
            bool allowProcess = false,
            bool allowStep = false, bool allowOperation = false)
        {
            if (!allowTargetProcess && action.TargetProcess != null) throw UnexpectedActionField(path, "targetProcess");
            if (!allowTargetStep && action.TargetStep != null) throw UnexpectedActionField(path, "targetStep");
            if (!allowTargetOperation && action.TargetOperation != null) throw UnexpectedActionField(path, "targetOperation");
            if (!allowPosition && action.Position != null) throw UnexpectedActionField(path, "position");
            if (!allowProcess && action.Process != null) throw UnexpectedActionField(path, "process");
            if (!allowStep && action.Step != null) throw UnexpectedActionField(path, "step");
            if (!allowOperation && action.Operation != null) throw UnexpectedActionField(path, "operation");
        }

        private static InvalidOperationException UnexpectedActionField(string path, string field)
        {
            return new InvalidOperationException($"{path} 不允许提供 {field}。");
        }

        private static void AddProcessState(List<AtomicProcessState> states,
            ProcessActionValue value, string path)
        {
            if (value == null) throw new InvalidOperationException($"{path}.process 必填。");
            string key = string.IsNullOrWhiteSpace(value.Key)
                ? "p" + Guid.NewGuid().ToString("N").Substring(0, 31)
                : ValidateLocalKey(value.Key, $"{path}.process.key");
            string name = RequiredName(value.Name, $"{path}.process.name");
            if (states.Any(state => !state.Deleted && string.Equals(state.Key, key, StringComparison.Ordinal)))
                throw new InvalidOperationException($"{path} 流程 key 重复：{key}");
            if (states.Any(state => !state.Deleted
                && string.Equals(state.Definition.Name, name, StringComparison.Ordinal)))
                throw new InvalidOperationException($"{path} 流程名称已存在：{name}");
            states.Add(new AtomicProcessState(key, new ProcessDefinition
            {
                Key = key,
                Action = "create",
                Name = name,
                AutoStart = value.AutoStart,
                Disable = value.Disable,
                Steps = new List<StepDefinition>()
            }));
        }

        private static AtomicProcessState ResolveProcessState(List<AtomicProcessState> states,
            ProcessSelector selector, string path)
        {
            if (selector == null) throw new InvalidOperationException($"{path}.targetProcess 必填。");
            int count = (!string.IsNullOrWhiteSpace(selector.ProcId) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(selector.Name) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(selector.Key) ? 1 : 0);
            if (count != 1)
                throw new InvalidOperationException($"{path}.targetProcess 必须且只能使用 procId、name 或 key。");
            IEnumerable<AtomicProcessState> matches = states.Where(state => !state.Deleted);
            string display;
            if (!string.IsNullOrWhiteSpace(selector.ProcId))
            {
                Guid id = ParseStableId(selector.ProcId, $"{path}.targetProcess.procId");
                matches = matches.Where(state => state.Original?.head?.Id == id);
                display = id.ToString("D");
            }
            else if (!string.IsNullOrWhiteSpace(selector.Name))
            {
                display = RequiredName(selector.Name, $"{path}.targetProcess.name");
                matches = matches.Where(state => string.Equals(state.Definition.Name, display, StringComparison.Ordinal));
            }
            else
            {
                display = ValidateLocalKey(selector.Key, $"{path}.targetProcess.key");
                matches = matches.Where(state => string.Equals(state.Key, display, StringComparison.Ordinal));
            }
            List<AtomicProcessState> result = matches.ToList();
            if (result.Count != 1)
                throw new InvalidOperationException(result.Count == 0
                    ? $"{path} 未找到目标流程：{display}"
                    : $"{path} 目标流程不唯一：{display}");
            result[0].Touch();
            return result[0];
        }

        private static void UpdateProcessState(AtomicProcessState state,
            ProcessActionValue value, string path)
        {
            if (value == null) throw new InvalidOperationException($"{path}.process 必填。");
            if (!string.IsNullOrWhiteSpace(value.Key))
                throw new InvalidOperationException($"{path}.process.key 只用于 process.create。");
            if (!string.IsNullOrWhiteSpace(value.Name))
                state.Definition.Name = RequiredName(value.Name, $"{path}.process.name");
            if (value.AutoStart.HasValue) state.Definition.AutoStart = value.AutoStart;
            if (value.Disable.HasValue) state.Definition.Disable = value.Disable;
            if (string.IsNullOrWhiteSpace(value.Name) && !value.AutoStart.HasValue && !value.Disable.HasValue)
                throw new InvalidOperationException($"{path}.process 至少提供一个要更新的字段。");
        }

        private static void DeleteProcessState(AtomicProcessState state,
            HashSet<Guid> deletedIds, string path)
        {
            if (state.Original == null)
                throw new InvalidOperationException($"{path} 不能删除同一阶段内刚创建的流程。");
            state.Deleted = true;
            state.Touched = false;
            deletedIds.Add(state.Original.head.Id);
        }

        private static void InsertStep(AtomicProcessState state, StepActionValue value,
            ChangePosition position, string path)
        {
            if (value == null) throw new InvalidOperationException($"{path}.step 必填。");
            string key = string.IsNullOrWhiteSpace(value.Key)
                ? "s" + Guid.NewGuid().ToString("N").Substring(0, 31)
                : ValidateLocalKey(value.Key, $"{path}.step.key");
            if (state.Definition.Steps.Any(step => string.Equals(step.Key, key, StringComparison.Ordinal)))
                throw new InvalidOperationException($"{path} 步骤 key 重复：{key}");
            var step = new StepDefinition
            {
                Key = key,
                Name = RequiredName(value.Name, $"{path}.step.name"),
                Disable = value.Disable,
                Operations = new List<SemanticOperation>()
            };
            int index = position == null ? state.Definition.Steps.Count
                : ResolvePosition(state.Definition.Steps, position,
                    item => item.StepId, item => item.Key, path);
            state.Definition.Steps.Insert(index, step);
        }

        private static StepDefinition ResolveStep(AtomicProcessState state,
            StepSelector selector, string path)
        {
            if (selector == null) throw new InvalidOperationException($"{path}.targetStep 必填。");
            int count = (!string.IsNullOrWhiteSpace(selector.StepId) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(selector.Key) ? 1 : 0);
            if (count != 1)
                throw new InvalidOperationException($"{path}.targetStep 必须且只能使用 stepId 或 key。");
            StepDefinition step;
            if (!string.IsNullOrWhiteSpace(selector.StepId))
            {
                Guid id = ParseStableId(selector.StepId, $"{path}.targetStep.stepId");
                step = state.Definition.Steps.SingleOrDefault(item =>
                    Guid.TryParse(item.StepId, out Guid itemId) && itemId == id);
            }
            else
            {
                string key = ValidateLocalKey(selector.Key, $"{path}.targetStep.key");
                step = state.Definition.Steps.SingleOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
            }
            return step ?? throw new InvalidOperationException($"{path} 未找到目标步骤。");
        }

        private static void UpdateStep(AtomicProcessState state, StepSelector selector,
            StepActionValue value, string path)
        {
            if (value == null) throw new InvalidOperationException($"{path}.step 必填。");
            if (!string.IsNullOrWhiteSpace(value.Key))
                throw new InvalidOperationException($"{path}.step.key 只用于新增步骤。");
            StepDefinition step = ResolveStep(state, selector, path);
            if (!string.IsNullOrWhiteSpace(value.Name)) step.Name = RequiredName(value.Name, $"{path}.step.name");
            if (value.Disable.HasValue) step.Disable = value.Disable;
            if (string.IsNullOrWhiteSpace(value.Name) && !value.Disable.HasValue)
                throw new InvalidOperationException($"{path}.step 至少提供一个要更新的字段。");
        }

        private static void DeleteStep(AtomicProcessState state, StepSelector selector, string path)
        {
            StepDefinition step = ResolveStep(state, selector, path);
            state.Definition.Steps.Remove(step);
        }

        private static void MoveStep(AtomicProcessState state, StepSelector selector,
            ChangePosition position, string path)
        {
            StepDefinition step = ResolveStep(state, selector, path);
            state.Definition.Steps.Remove(step);
            int index = ResolvePosition(state.Definition.Steps, position,
                item => item.StepId, item => item.Key, path);
            state.Definition.Steps.Insert(index, step);
        }

        private static void InsertOperation(AtomicProcessState state, StepSelector stepSelector,
            SemanticOperation operation, ChangePosition position, string path)
        {
            StepDefinition step = ResolveStep(state, stepSelector, path);
            SemanticOperation value = PrepareNewOperation(operation, path);
            if ((step.Operations ?? new List<SemanticOperation>())
                .Any(item => !string.IsNullOrWhiteSpace(item.Key)
                    && string.Equals(item.Key, value.Key, StringComparison.Ordinal)))
                throw new InvalidOperationException($"{path} 指令 key 重复：{value.Key}");
            int index = position == null ? step.Operations.Count
                : ResolvePosition(step.Operations, position, item => item.OpId, item => item.Key, path);
            step.Operations.Insert(index, value);
        }

        private static SemanticOperation PrepareNewOperation(SemanticOperation operation, string path)
        {
            if (operation == null) throw new InvalidOperationException($"{path}.operation 必填。");
            if (!string.IsNullOrWhiteSpace(operation.OpId))
                throw new InvalidOperationException($"{path}.operation.opId 只用于现有指令定位。");
            operation.Key = string.IsNullOrWhiteSpace(operation.Key)
                ? "o" + Guid.NewGuid().ToString("N").Substring(0, 31)
                : ValidateLocalKey(operation.Key, $"{path}.operation.key");
            if (string.IsNullOrWhiteSpace(operation.Kind))
                throw new InvalidOperationException($"{path}.operation.kind 必填。");
            return operation;
        }

        private static SemanticOperation ResolveOperation(StepDefinition step,
            OperationSelector selector, string path)
        {
            if (selector == null) throw new InvalidOperationException($"{path}.targetOperation 必填。");
            int count = (!string.IsNullOrWhiteSpace(selector.OpId) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(selector.Key) ? 1 : 0);
            if (count != 1)
                throw new InvalidOperationException($"{path}.targetOperation 必须且只能使用 opId 或 key。");
            SemanticOperation operation;
            if (!string.IsNullOrWhiteSpace(selector.OpId))
            {
                Guid id = ParseStableId(selector.OpId, $"{path}.targetOperation.opId");
                operation = step.Operations.SingleOrDefault(item =>
                    Guid.TryParse(item.OpId, out Guid itemId) && itemId == id);
            }
            else
            {
                string key = ValidateLocalKey(selector.Key, $"{path}.targetOperation.key");
                operation = step.Operations.SingleOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
            }
            return operation ?? throw new InvalidOperationException($"{path} 未找到目标指令。");
        }

        private static StepDefinition ResolveOperationStep(AtomicProcessState state,
            StepSelector stepSelector, OperationSelector operationSelector, string path)
        {
            if (stepSelector != null)
                return ResolveStep(state, stepSelector, path);
            if (operationSelector == null || string.IsNullOrWhiteSpace(operationSelector.OpId)
                || !string.IsNullOrWhiteSpace(operationSelector.Key))
                throw new InvalidOperationException(
                    $"{path}.targetStep 必填；仅 targetOperation.opId 可由Bridge反查所属步骤。");

            Guid operationId = ParseStableId(
                operationSelector.OpId, $"{path}.targetOperation.opId");
            List<StepDefinition> matches = state.Definition.Steps
                .Where(step => (step.Operations ?? new List<SemanticOperation>()).Any(operation =>
                    Guid.TryParse(operation.OpId, out Guid itemId) && itemId == operationId))
                .ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException(matches.Count == 0
                    ? $"{path} 未找到 targetOperation.opId 所属步骤：{operationId:D}"
                    : $"{path} targetOperation.opId 在多个步骤中重复：{operationId:D}");
            return matches[0];
        }

        private static void UpdateOperation(AtomicProcessState state, StepSelector stepSelector,
            OperationSelector operationSelector, SemanticOperation replacement, string path)
        {
            StepDefinition step = ResolveOperationStep(state, stepSelector, operationSelector, path);
            SemanticOperation current = ResolveOperation(step, operationSelector, path);
            if (replacement == null || string.IsNullOrWhiteSpace(replacement.Kind))
                throw new InvalidOperationException($"{path}.operation.kind 必填。");
            if (!string.IsNullOrWhiteSpace(replacement.OpId))
                throw new InvalidOperationException($"{path}.operation.opId 由 targetOperation 决定，不得重复提供。");
            replacement.OpId = current.OpId;
            replacement.Key = current.Key;
            step.Operations[step.Operations.IndexOf(current)] = replacement;
        }

        private static void DeleteOperation(AtomicProcessState state, StepSelector stepSelector,
            OperationSelector operationSelector, string path)
        {
            StepDefinition step = ResolveOperationStep(state, stepSelector, operationSelector, path);
            step.Operations.Remove(ResolveOperation(step, operationSelector, path));
        }

        private static void ReplaceOperation(AtomicProcessState state, StepSelector stepSelector,
            OperationSelector operationSelector, SemanticOperation replacement, string path)
        {
            if (operationSelector == null || string.IsNullOrWhiteSpace(operationSelector.OpId)
                || !string.IsNullOrWhiteSpace(operationSelector.Key))
            {
                throw new InvalidOperationException(
                    $"{path}.targetOperation 必须使用已提交指令的稳定 opId。当前阶段新指令直接修改其定义。");
            }
            StepDefinition step = ResolveOperationStep(state, stepSelector, operationSelector, path);
            SemanticOperation current = ResolveOperation(step, operationSelector, path);
            if (replacement == null || string.IsNullOrWhiteSpace(replacement.Kind))
                throw new InvalidOperationException($"{path}.operation.kind 必填。");
            if (!string.IsNullOrWhiteSpace(replacement.OpId))
                throw new InvalidOperationException($"{path}.operation.opId 由 targetOperation 决定，不得重复提供。");
            if (!string.IsNullOrWhiteSpace(replacement.Key))
                throw new InvalidOperationException($"{path}.operation.key 由原指令继承，不得重新指定。");
            if ((replacement.ClearFields?.Count ?? 0) > 0)
                throw new InvalidOperationException($"{path}.operation.clearFields 只用于同类型 operation.update。");
            replacement.OpId = current.OpId;
            replacement.Key = current.Key;
            step.Operations[step.Operations.IndexOf(current)] = replacement;
            step.ReplaceOperationIds ??= new List<string>();
            if (!step.ReplaceOperationIds.Contains(current.OpId, StringComparer.OrdinalIgnoreCase))
            {
                step.ReplaceOperationIds.Add(current.OpId);
            }
        }

        private static void MoveOperation(AtomicProcessState state, StepSelector stepSelector,
            OperationSelector operationSelector, ChangePosition position, string path)
        {
            StepDefinition step = ResolveOperationStep(state, stepSelector, operationSelector, path);
            SemanticOperation operation = ResolveOperation(step, operationSelector, path);
            step.Operations.Remove(operation);
            int index = ResolvePosition(step.Operations, position, item => item.OpId, item => item.Key, path);
            step.Operations.Insert(index, operation);
        }

        private static int ResolvePosition<T>(IList<T> items, ChangePosition position,
            Func<T, string> getId, Func<T, string> getKey, string path)
        {
            if (position == null) throw new InvalidOperationException($"{path}.position 必填。");
            int selectorCount = (!string.IsNullOrWhiteSpace(position.BeforeId) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(position.BeforeKey) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(position.AfterId) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(position.AfterKey) ? 1 : 0);
            if (selectorCount != 1)
                throw new InvalidOperationException(
                    $"{path}.position 必须且只能提供 beforeId、beforeKey、afterId 或 afterKey。");
            bool after = !string.IsNullOrWhiteSpace(position.AfterId)
                || !string.IsNullOrWhiteSpace(position.AfterKey);
            string idText = position.BeforeId ?? position.AfterId;
            string keyText = position.BeforeKey ?? position.AfterKey;
            int index;
            if (!string.IsNullOrWhiteSpace(idText))
            {
                Guid id = ParseStableId(idText, $"{path}.position");
                index = items.Select((item, itemIndex) => new { item, itemIndex })
                    .Where(value => Guid.TryParse(getId(value.item), out Guid itemId) && itemId == id)
                    .Select(value => value.itemIndex).DefaultIfEmpty(-1).Single();
            }
            else
            {
                string key = ValidateLocalKey(keyText, $"{path}.position");
                index = items.Select((item, itemIndex) => new { item, itemIndex })
                    .Where(value => string.Equals(getKey(value.item), key, StringComparison.Ordinal))
                    .Select(value => value.itemIndex).DefaultIfEmpty(-1).Single();
            }
            if (index < 0) throw new InvalidOperationException($"{path}.position 未找到锚点对象。");
            return after ? index + 1 : index;
        }

        private sealed class AtomicProcessState
        {
            public AtomicProcessState(Proc original)
            {
                Original = original ?? throw new ArgumentNullException(nameof(original));
                Definition = CreateDefinition(original);
            }

            public AtomicProcessState(string key, ProcessDefinition definition)
            {
                Key = key;
                Definition = definition;
                Touched = true;
            }

            public string Key { get; }

            public Proc Original { get; }

            public ProcessDefinition Definition { get; }

            public bool Touched { get; set; }

            public bool Deleted { get; set; }

            public void Touch()
            {
                if (Deleted) throw new InvalidOperationException("目标流程已在本阶段删除。");
                Touched = true;
            }

            private static ProcessDefinition CreateDefinition(Proc proc)
            {
                return new ProcessDefinition
                {
                    Action = "replace",
                    TargetProcId = proc.head.Id.ToString("D"),
                    Name = proc.head.Name,
                    Steps = (proc.steps ?? new List<Step>()).Select(step => new StepDefinition
                    {
                        StepId = step.Id.ToString("D"),
                        Key = string.IsNullOrWhiteSpace(step.AiKey)
                            ? "s" + step.Id.ToString("N").Substring(0, 31)
                            : step.AiKey,
                        Name = step.Name,
                        Operations = (step.Ops ?? new List<OperationType>()).Select(operation =>
                            new SemanticOperation
                            {
                                OpId = operation.Id.ToString("D"),
                                Key = operation.AiKey
                            }).ToList()
                    }).ToList()
                };
            }
        }

        private static IReadOnlyCollection<string> GetReferenceValues(
            AiResourceSnapshot resources, string referenceType)
        {
            if (resources?.References != null
                && resources.References.TryGetValue(referenceType, out IReadOnlyCollection<string> values))
            {
                return values;
            }
            return Array.Empty<string>();
        }

        public static string ComputeStateHash(IList<Proc> processes, IDictionary<string, DicValue> variables)
        {
            var variableState = new JArray();
            if (variables != null)
            {
                foreach (KeyValuePair<string, DicValue> item in variables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    variableState.Add(new JObject
                    {
                        ["name"] = item.Key,
                        ["index"] = item.Value?.Index ?? -1,
                        ["type"] = item.Value?.Type ?? string.Empty,
                        ["configValue"] = item.Value?.ConfigValue ?? string.Empty,
                        ["note"] = item.Value?.Note ?? string.Empty
                    });
                }
            }
            var state = new JObject
            {
                ["processes"] = processes == null ? new JArray() : JArray.FromObject(processes),
                // 运行值不属于配置版本；仅配置值、类型、索引和说明参与预演并发校验。
                ["variables"] = variableState
            };
            byte[] bytes = Encoding.UTF8.GetBytes(state.ToString(Formatting.None));
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static Dictionary<string, DicValue> CloneVariables(IDictionary<string, DicValue> source)
        {
            var result = new Dictionary<string, DicValue>(StringComparer.Ordinal);
            if (source == null)
            {
                return result;
            }
            foreach (KeyValuePair<string, DicValue> item in source)
            {
                if (!string.IsNullOrWhiteSpace(item.Key) && item.Value != null)
                {
                    result[item.Key] = ObjectGraphCloner.Clone(item.Value);
                }
            }
            return result;
        }

        private static int ApplyProcessDeletion(
            ProcessDeleteSelection selection,
            List<Proc> processes,
            JArray changes)
        {
            if (selection == null)
            {
                return 0;
            }
            string mode = RequiredText(selection.Mode, "deleteProcesses.mode");
            var deleteIndexes = new HashSet<int>();
            if (string.Equals(mode, "all", StringComparison.Ordinal))
            {
                for (int i = 0; i < processes.Count; i++) deleteIndexes.Add(i);
                if ((selection.Names?.Count ?? 0) > 0 || (selection.ProcIds?.Count ?? 0) > 0)
                {
                    throw new InvalidOperationException("deleteProcesses.mode=all 时不得再提供 names 或 procIds。");
                }
            }
            else if (string.Equals(mode, "selected", StringComparison.Ordinal))
            {
                foreach (string name in selection.Names ?? new List<string>())
                {
                    string exactName = RequiredText(name, "deleteProcesses.names[]");
                    List<int> matches = processes.Select((proc, index) => new { proc, index })
                        .Where(item => string.Equals(item.proc?.head?.Name, exactName, StringComparison.Ordinal))
                        .Select(item => item.index).ToList();
                    if (matches.Count == 0)
                    {
                        throw new InvalidOperationException($"待删除流程不存在：{exactName}");
                    }
                    foreach (int index in matches) deleteIndexes.Add(index);
                }
                foreach (string procIdText in selection.ProcIds ?? new List<string>())
                {
                    if (!Guid.TryParse(procIdText, out Guid procId) || procId == Guid.Empty)
                    {
                        throw new InvalidOperationException($"deleteProcesses.procIds 包含非法流程 ID：{procIdText}");
                    }
                    int index = processes.FindIndex(proc => proc?.head?.Id == procId);
                    if (index < 0)
                    {
                        throw new InvalidOperationException($"待删除流程 ID 不存在：{procId:D}");
                    }
                    deleteIndexes.Add(index);
                }
                if (deleteIndexes.Count == 0)
                {
                    throw new InvalidOperationException("deleteProcesses.mode=selected 时必须提供 names 或 procIds。");
                }
            }
            else
            {
                throw new InvalidOperationException("deleteProcesses.mode 只能是 all 或 selected。");
            }

            foreach (int index in deleteIndexes.OrderByDescending(value => value))
            {
                Proc proc = processes[index];
                changes.Add(new JObject
                {
                    ["type"] = "process.delete",
                    ["procId"] = proc?.head?.Id.ToString("D") ?? string.Empty,
                    ["name"] = proc?.head?.Name ?? string.Empty
                });
                processes.RemoveAt(index);
            }
            return deleteIndexes.Count;
        }

        private static int ApplyVariableChanges(
            IList<VariableChange> definitions,
            Dictionary<string, DicValue> variables,
            JArray changes)
        {
            int changed = 0;
            var declaredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (VariableChange definition in definitions ?? Array.Empty<VariableChange>())
            {
                if (definition == null) throw new InvalidOperationException("variables 不得包含 null。");
                string name = RequiredName(definition.Name, "variables[].name");
                if (!declaredNames.Add(name))
                {
                    throw new InvalidOperationException($"changeSet.variables 包含重复声明：{name}");
                }
                string policy = string.IsNullOrWhiteSpace(definition.Policy) ? "reuse" : definition.Policy.Trim();
                if (ProtectedVariables.Contains(name)
                    && !string.Equals(policy, "reuse", StringComparison.Ordinal)
                    && !string.Equals(policy, "require", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"系统保留变量[{name}]只允许 reuse 或 require 策略。");
                }
                string type = string.IsNullOrWhiteSpace(definition.Type) ? "double" : definition.Type.Trim();
                if (!string.Equals(type, "double", StringComparison.Ordinal)
                    && !string.Equals(type, "string", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"变量[{name}] type 只能是 double 或 string。");
                }
                string initialValue = definition.InitialValue
                    ?? (string.Equals(type, "double", StringComparison.Ordinal) ? "0" : string.Empty);
                if (string.Equals(type, "double", StringComparison.Ordinal)
                    && !double.TryParse(initialValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    && !double.TryParse(initialValue, out _))
                {
                    throw new InvalidOperationException($"变量[{name}]的 initialValue 不是有效数字。");
                }

                bool exists = variables.TryGetValue(name, out DicValue existing);
                if (exists) EnsureVariableType(existing, type, name);
                if (string.Equals(policy, "require", StringComparison.Ordinal))
                {
                    if (!exists) throw new InvalidOperationException($"要求复用的变量不存在：{name}");
                    continue;
                }
                if (string.Equals(policy, "reuse", StringComparison.Ordinal))
                {
                    if (exists)
                    {
                        continue;
                    }
                }
                else if (string.Equals(policy, "create", StringComparison.Ordinal))
                {
                    if (exists) throw new InvalidOperationException($"变量已存在，create 策略禁止覆盖：{name}");
                }
                else if (string.Equals(policy, "update", StringComparison.Ordinal))
                {
                    if (!exists) throw new InvalidOperationException($"变量不存在，update 策略无法更新：{name}");
                }
                else if (!string.Equals(policy, "replace", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"变量[{name}] policy 只能是 reuse/create/update/replace/require。");
                }

                int index = exists ? existing.Index : FindFreeVariableIndex(variables);
                variables[name] = new DicValue
                {
                    Index = index,
                    Name = name,
                    Type = type,
                    ConfigValue = initialValue,
                    Value = initialValue,
                    Note = definition.Note ?? string.Empty
                };
                changed++;
                changes.Add(new JObject
                {
                    ["type"] = exists ? "variable.update" : "variable.create",
                    ["name"] = name,
                    ["valueType"] = type,
                    ["oldValue"] = exists ? existing.ConfigValue : null,
                    ["newValue"] = initialValue,
                    ["policy"] = policy
                });
            }
            return changed;
        }

        private static int ApplyProcessDefinitions(
            IList<ProcessDefinition> definitions,
            List<Proc> processes,
            Dictionary<string, DicValue> variables,
            AiResourceSnapshot resources,
            JArray changes,
            ref int operationCount,
            JObject createdObjects,
            out int replaced)
        {
            int created = 0;
            replaced = 0;
            var replacedProcessIds = new HashSet<Guid>();
            foreach (ProcessDefinition definition in definitions ?? Array.Empty<ProcessDefinition>())
            {
                if (definition == null) throw new InvalidOperationException("processes 不得包含 null。");
                string action = string.IsNullOrWhiteSpace(definition.Action) ? "create" : definition.Action.Trim();
                if (!string.Equals(action, "create", StringComparison.Ordinal)
                    && !string.Equals(action, "replace", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("processes[].action 只能是 create 或 replace。");
                }
                string processName = RequiredName(definition.Name, "processes[].name");
                int procIndex;
                Proc replacedProcess = null;
                if (string.Equals(action, "replace", StringComparison.Ordinal))
                {
                    procIndex = ResolveReplacementProcessIndex(definition, processes);
                    replacedProcess = processes[procIndex];
                    if (!replacedProcessIds.Add(replacedProcess.head.Id))
                    {
                        throw new InvalidOperationException($"同一变更集不得重复替换流程：{replacedProcess.head.Name}");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(definition.TargetProcId)
                        || !string.IsNullOrWhiteSpace(definition.TargetName))
                    {
                        throw new InvalidOperationException("processes[].action=create 时不得提供 targetProcId 或 targetName。");
                    }
                    procIndex = processes.Count;
                }
                if (processes.Select((proc, index) => new { proc, index })
                    .Any(item => item.index != procIndex
                        && string.Equals(item.proc?.head?.Name, processName, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException($"流程名称已存在：{processName}");
                }
                var proc = new Proc
                {
                    head = new ProcHead
                    {
                        Id = replacedProcess?.head?.Id ?? Guid.NewGuid(),
                        Name = processName,
                        AutoStart = definition.AutoStart ?? replacedProcess?.head?.AutoStart ?? false,
                        Disable = definition.Disable ?? replacedProcess?.head?.Disable ?? false
                    },
                    steps = new List<Step>()
                };
                var stepIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
                var operationIdLocations = new Dictionary<Guid, OperationReferenceLocation>();
                var operationKeyLocations = new Dictionary<string, OperationReferenceLocation>(StringComparer.Ordinal);
                Dictionary<Guid, Step> existingSteps = BuildExistingStepMap(replacedProcess);
                Dictionary<Guid, OperationType> existingOperations = BuildExistingOperationMap(replacedProcess);
                var usedStepIds = new HashSet<Guid>();
                var usedOperationIds = new HashSet<Guid>();
                IList<StepDefinition> stepDefinitions = definition.Steps ?? new List<StepDefinition>();
                foreach (StepDefinition stepDefinition in stepDefinitions)
                {
                    if (stepDefinition == null) throw new InvalidOperationException($"流程[{processName}]包含 null 步骤。");
                    string key = RequiredText(stepDefinition.Key, $"流程[{processName}]步骤 key");
                    if (key.Length > 32 || !IsValidKey(key))
                    {
                        throw new InvalidOperationException($"步骤 key[{key}]只能包含英文字母、数字、下划线和短横线，且必须以字母开头、最长32字符。");
                    }
                    if (stepIndexes.ContainsKey(key))
                    {
                        throw new InvalidOperationException($"流程[{processName}]步骤 key 重复：{key}");
                    }
                    stepIndexes.Add(key, proc.steps.Count);
                    Step existingStep = ResolveExistingStep(
                        stepDefinition, replacedProcess, existingSteps, usedStepIds, processName);
                    int actualCount = stepDefinition.Operations?.Count ?? 0;
                    List<string> expectedOperaTypes = stepDefinition.ExpectedOperaTypes;
                    if (expectedOperaTypes != null)
                    {
                        if (expectedOperaTypes.Any(string.IsNullOrWhiteSpace))
                        {
                            throw new InvalidOperationException(
                                $"流程[{processName}]步骤[{key}] expectedOperaTypes 不能包含空类型。");
                        }
                        if (expectedOperaTypes.Count != actualCount)
                        {
                            throw new InvalidOperationException(
                                $"流程[{processName}]步骤[{key}] expectedOperaTypes 必须与 operations 数量一致。");
                        }
                    }
                    int stepIndex = proc.steps.Count;
                    proc.steps.Add(new Step
                    {
                        Id = existingStep?.Id ?? Guid.NewGuid(),
                        AiKey = key,
                        Name = string.IsNullOrWhiteSpace(stepDefinition.Name)
                            ? existingStep?.Name ?? throw new InvalidOperationException(
                                $"流程[{processName}]新步骤[{key}]必须提供 name。")
                            : RequiredName(stepDefinition.Name, $"流程[{processName}]步骤 name"),
                        Disable = stepDefinition.Disable ?? existingStep?.Disable ?? false,
                        Ops = new List<OperationType>()
                    });
                    Step compiledStep = proc.steps[stepIndex];
                    if (existingStep == null)
                    {
                        ((JArray)createdObjects["steps"]).Add(new JObject
                        {
                            ["procId"] = proc.head.Id.ToString("D"),
                            ["processKey"] = definition.Key,
                            ["key"] = key,
                            ["stepId"] = compiledStep.Id.ToString("D"),
                            ["name"] = compiledStep.Name
                        });
                    }
                    for (int operationIndex = 0; operationIndex < actualCount; operationIndex++)
                    {
                        SemanticOperation semantic = stepDefinition.Operations[operationIndex]
                            ?? throw new InvalidOperationException(
                                $"流程[{processName}]步骤[{key}]包含 null 指令。");
                        if (!string.IsNullOrWhiteSpace(semantic.OpId))
                        {
                            Guid operationId = ParseStableId(semantic.OpId,
                                $"流程[{processName}]步骤[{key}]指令 opId");
                            if (replacedProcess == null || !existingOperations.ContainsKey(operationId))
                            {
                                throw new InvalidOperationException(
                                    $"流程[{processName}]未找到要保留的指令：{operationId:D}");
                            }
                            if (!usedOperationIds.Add(operationId))
                            {
                                throw new InvalidOperationException(
                                    $"流程[{processName}]重复使用指令 opId：{operationId:D}");
                            }
                            operationIdLocations.Add(operationId,
                                new OperationReferenceLocation(stepIndex, operationIndex));
                        }
                        if (!string.IsNullOrWhiteSpace(semantic.Key))
                        {
                            string operationKey = ValidateLocalKey(semantic.Key,
                                $"流程[{processName}]步骤[{key}]指令 key");
                            string stepKeyMap = AiOperationCompileContext.BuildOperationKeyForStepKey(
                                key, operationKey);
                            string stepIdMap = AiOperationCompileContext.BuildOperationKeyForStepId(
                                compiledStep.Id, operationKey);
                            if (operationKeyLocations.ContainsKey(stepKeyMap)
                                || operationKeyLocations.ContainsKey(stepIdMap))
                            {
                                throw new InvalidOperationException(
                                    $"流程[{processName}]步骤[{key}]指令 key 重复：{operationKey}");
                            }
                            var location = new OperationReferenceLocation(stepIndex, operationIndex);
                            operationKeyLocations.Add(stepKeyMap, location);
                            operationKeyLocations.Add(stepIdMap, location);
                        }
                    }
                }

                for (int stepIndex = 0; stepIndex < stepDefinitions.Count; stepIndex++)
                {
                    StepDefinition stepDefinition = stepDefinitions[stepIndex];
                    Step step = proc.steps[stepIndex];
                    int semanticIndex = 0;
                    foreach (SemanticOperation semantic in stepDefinition.Operations ?? Enumerable.Empty<SemanticOperation>())
                    {
                        OperationType existingOperation = null;
                        if (!string.IsNullOrWhiteSpace(semantic.OpId))
                        {
                            Guid operationId = ParseStableId(semantic.OpId,
                                $"流程[{processName}]步骤[{stepDefinition.Key}]指令 opId");
                            existingOperation = existingOperations[operationId];
                        }
                        bool reuseExisting = existingOperation != null && IsExistingOperationReference(semantic);
                        bool replaceExisting = existingOperation != null
                            && (stepDefinition.ReplaceOperationIds ?? new List<string>())
                                .Contains(semantic.OpId, StringComparer.OrdinalIgnoreCase);
                        string actualOperaType = reuseExisting
                            ? existingOperation.OperaType
                            : semantic?.OperaType;
                        if (stepDefinition.ExpectedOperaTypes != null
                            && !reuseExisting
                            && !string.Equals(semantic?.Kind, "native.operation", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"流程[{processName}]要求保留精确指令类型，所有指令必须使用 native.operation。");
                        }
                        if (stepDefinition.ExpectedOperaTypes != null
                            && !string.Equals(actualOperaType,
                                stepDefinition.ExpectedOperaTypes[semanticIndex], StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"流程[{processName}]步骤[{stepDefinition.Key}]第 {semanticIndex} 条指令类型必须为"
                                + $"[{stepDefinition.ExpectedOperaTypes[semanticIndex]}]，实际为[{actualOperaType ?? "空"}]。");
                        }
                        operationCount++;
                        if (reuseExisting)
                        {
                            step.Ops.Add(ObjectGraphCloner.Clone(existingOperation));
                        }
                        else
                        {
                            OperationType compiled = CompileOperation(
                                semantic, processName, procIndex,
                                variables, resources, operationIdLocations, operationKeyLocations,
                                step.Id, stepDefinition.Key, existingOperation, replaceExisting);
                            if (existingOperation != null)
                            {
                                compiled.Id = existingOperation.Id;
                                if (string.IsNullOrWhiteSpace(semantic.Name))
                                {
                                    compiled.Name = existingOperation.Name;
                                }
                            }
                            step.Ops.Add(compiled);
                            if (existingOperation == null && !string.IsNullOrWhiteSpace(semantic.Key))
                            {
                                ((JArray)createdObjects["operations"]).Add(new JObject
                                {
                                    ["procId"] = proc.head.Id.ToString("D"),
                                    ["processKey"] = definition.Key,
                                    ["stepId"] = step.Id.ToString("D"),
                                    ["stepKey"] = string.IsNullOrWhiteSpace(stepDefinition.StepId)
                                        ? stepDefinition.Key
                                        : null,
                                    ["key"] = semantic.Key,
                                    ["opId"] = compiled.Id.ToString("D"),
                                    ["name"] = compiled.Name,
                                    ["operaType"] = compiled.OperaType
                                });
                            }
                        }
                        semanticIndex++;
                    }
                }
                GotoRewriteResult gotoRewrite = replacedProcess == null
                    ? new GotoRewriteResult()
                    : ProcessEditingService.RewriteGotoTargets(replacedProcess, proc, procIndex);
                ProcessEditingService.RenumberOperations(proc);
                ProcessDefinitionService.ResolvePendingGotoTargets(procIndex, proc);

                if (replacedProcess == null)
                {
                    processes.Add(proc);
                    created++;
                    if (!string.IsNullOrWhiteSpace(definition.Key))
                    {
                        ((JArray)createdObjects["processes"]).Add(new JObject
                        {
                            ["key"] = definition.Key,
                            ["procId"] = proc.head.Id.ToString("D"),
                            ["procIndex"] = procIndex,
                            ["name"] = processName
                        });
                    }
                    changes.Add(new JObject
                    {
                        ["type"] = "process.create",
                        ["procId"] = proc.head.Id.ToString("D"),
                        ["name"] = processName,
                        ["procIndex"] = procIndex,
                        ["stepCount"] = proc.steps.Count,
                        ["operationCount"] = proc.steps.Sum(step => step?.Ops?.Count ?? 0),
                        ["rewrittenGotoCount"] = 0
                    });
                }
                else
                {
                    processes[procIndex] = proc;
                    replaced++;
                    changes.Add(new JObject
                    {
                        ["type"] = "process.replace",
                        ["procId"] = proc.head.Id.ToString("D"),
                        ["oldName"] = replacedProcess.head?.Name ?? string.Empty,
                        ["name"] = processName,
                        ["procIndex"] = procIndex,
                        ["stepCount"] = proc.steps.Count,
                        ["operationCount"] = proc.steps.Sum(step => step?.Ops?.Count ?? 0),
                        ["rewrittenGotoCount"] = gotoRewrite.RewrittenCount,
                        ["invalidatedGotoCount"] = gotoRewrite.InvalidatedCount
                    });
                }
            }
            return created;
        }

        private static Dictionary<Guid, Step> BuildExistingStepMap(Proc proc)
        {
            return proc?.steps?.Where(step => step != null && step.Id != Guid.Empty)
                .ToDictionary(step => step.Id) ?? new Dictionary<Guid, Step>();
        }

        private static Dictionary<Guid, OperationType> BuildExistingOperationMap(Proc proc)
        {
            return proc?.steps?.Where(step => step?.Ops != null)
                .SelectMany(step => step.Ops)
                .Where(operation => operation != null && operation.Id != Guid.Empty)
                .ToDictionary(operation => operation.Id) ?? new Dictionary<Guid, OperationType>();
        }

        private static Step ResolveExistingStep(StepDefinition definition, Proc replacedProcess,
            Dictionary<Guid, Step> existingSteps, HashSet<Guid> usedStepIds, string processName)
        {
            if (string.IsNullOrWhiteSpace(definition.StepId))
            {
                return null;
            }
            Guid stepId = ParseStableId(definition.StepId, $"流程[{processName}]步骤 stepId");
            if (replacedProcess == null || !existingSteps.TryGetValue(stepId, out Step existingStep))
            {
                throw new InvalidOperationException($"流程[{processName}]未找到要保留的步骤：{stepId:D}");
            }
            if (!usedStepIds.Add(stepId))
            {
                throw new InvalidOperationException($"流程[{processName}]重复使用步骤 stepId：{stepId:D}");
            }
            return existingStep;
        }

        private static Guid ParseStableId(string value, string path)
        {
            if (!Guid.TryParse(value, out Guid id) || id == Guid.Empty)
            {
                throw new InvalidOperationException($"{path} 不是有效 Guid：{value}");
            }
            return id;
        }

        private static string ValidateLocalKey(string value, string path)
        {
            string key = RequiredText(value, path);
            if (key.Length > 32 || !IsValidKey(key))
            {
                throw new InvalidOperationException(
                    $"{path}[{key}]只能包含英文字母、数字、下划线和短横线，且必须以字母开头、最长32字符。");
            }
            return key;
        }

        private static bool IsExistingOperationReference(SemanticOperation operation)
        {
            return operation != null
                && !string.IsNullOrWhiteSpace(operation.OpId)
                && string.IsNullOrWhiteSpace(operation.Kind)
                && string.IsNullOrWhiteSpace(operation.OperaType)
                && operation.Fields == null
                && string.IsNullOrWhiteSpace(operation.Name);
        }

        private static JArray BuildChangedProcessAnalyses(
            List<Proc> processes, JArray changes,
            ProcessDefinitionValidationContext validationContext)
        {
            var analyses = new JArray();
            foreach (JObject change in changes.OfType<JObject>().Where(item =>
                string.Equals(item["type"]?.Value<string>(), "process.create", StringComparison.Ordinal)
                || string.Equals(item["type"]?.Value<string>(), "process.replace", StringComparison.Ordinal)))
            {
                int procIndex = change["procIndex"]?.Value<int>() ?? -1;
                if (procIndex < 0 || procIndex >= processes.Count)
                {
                    continue;
                }
                ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                    procIndex, processes[procIndex], processes, validationContext);
                change["readinessStatus"] = readiness.ReadinessStatus;
                change["runnable"] = readiness.Runnable;
                var item = new JObject
                {
                    ["procIndex"] = procIndex,
                    ["procId"] = processes[procIndex]?.head?.Id.ToString("D") ?? string.Empty,
                    ["name"] = processes[procIndex]?.head?.Name ?? string.Empty,
                    ["readinessStatus"] = readiness.ReadinessStatus,
                    ["runnable"] = readiness.Runnable,
                    ["warnings"] = new JArray(readiness.Warnings),
                    ["runBlockers"] = new JArray(readiness.RunBlockers)
                };
                analyses.Add(item);
            }
            return analyses;
        }

        private static JArray FlattenReadinessMessages(JArray analyses, string field)
        {
            var result = new JArray();
            foreach (JObject analysis in analyses.OfType<JObject>())
            {
                foreach (string message in analysis[field]?.Values<string>() ?? Enumerable.Empty<string>())
                {
                    result.Add(new JObject
                    {
                        ["procIndex"] = analysis["procIndex"],
                        ["procId"] = analysis["procId"],
                        ["name"] = analysis["name"],
                        ["message"] = message
                    });
                }
            }
            return result;
        }

        private static int ResolveReplacementProcessIndex(ProcessDefinition definition, List<Proc> processes)
        {
            bool hasId = !string.IsNullOrWhiteSpace(definition.TargetProcId);
            bool hasName = !string.IsNullOrWhiteSpace(definition.TargetName);
            if (hasId == hasName)
            {
                throw new InvalidOperationException("processes[].action=replace 时 targetProcId 与 targetName 必须且只能提供一个。");
            }
            if (hasId)
            {
                if (!Guid.TryParse(definition.TargetProcId, out Guid procId) || procId == Guid.Empty)
                {
                    throw new InvalidOperationException($"processes[].targetProcId 不是有效流程 ID：{definition.TargetProcId}");
                }
                int index = processes.FindIndex(proc => proc?.head?.Id == procId);
                if (index < 0) throw new InvalidOperationException($"待替换流程 ID 不存在：{procId:D}");
                return index;
            }

            string targetName = RequiredName(definition.TargetName, "processes[].targetName");
            List<int> matches = processes.Select((proc, index) => new { proc, index })
                .Where(item => string.Equals(item.proc?.head?.Name, targetName, StringComparison.Ordinal))
                .Select(item => item.index).ToList();
            if (matches.Count == 0) throw new InvalidOperationException($"待替换流程不存在：{targetName}");
            if (matches.Count > 1) throw new InvalidOperationException($"待替换流程名称不唯一，请改用 targetProcId：{targetName}");
            return matches[0];
        }

        private static OperationType CompileOperation(
            SemanticOperation semantic,
            string processName,
            int procIndex,
            Dictionary<string, DicValue> variables,
            AiResourceSnapshot resources,
            IReadOnlyDictionary<Guid, OperationReferenceLocation> operationIdLocations,
            IReadOnlyDictionary<string, OperationReferenceLocation> operationKeyLocations,
            Guid currentStepId,
            string currentStepKey,
            OperationType existingOperation = null,
            bool replaceExisting = false)
        {
            if (semantic == null) throw new InvalidOperationException($"流程[{processName}]包含 null 指令。");
            string kind = RequiredText(semantic.Kind, $"流程[{processName}]指令 kind");
            if ((semantic.ClearFields?.Count ?? 0) > 0
                && (existingOperation == null || !string.Equals(kind, "native.operation", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "clearFields 仅允许 operation.update 现有 native.operation 指令时使用。");
            }
            IAiOperationCompiler compiler = AiOperationCompilerRegistry.Get(kind);
            var context = new AiOperationCompileContext(
                procIndex, variables, resources,
                operationIdLocations, operationKeyLocations,
                currentStepId, currentStepKey);
            OperationType operation = existingOperation != null && !replaceExisting
                && string.Equals(kind, "native.operation", StringComparison.Ordinal)
                ? StructuredOperationCompiler.CompilePatch(
                    existingOperation, semantic.OperaType, semantic.Fields,
                    semantic.ClearFields, context)
                : compiler.Compile(semantic, context);
            if (existingOperation != null && !replaceExisting
                && !string.Equals(operation.OperaType, existingOperation.OperaType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"operation.update 必须保持原指令类型[{existingOperation.OperaType}]，"
                    + $"当前定义会生成[{operation.OperaType}]；改变类型请使用 operation.replace。");
            }

            operation.Id = Guid.NewGuid();
            operation.AiKey = string.IsNullOrWhiteSpace(semantic.Key)
                ? existingOperation?.AiKey
                : semantic.Key.Trim();
            operation.Name = string.IsNullOrWhiteSpace(semantic.Name)
                ? (string.Equals(kind, "native.operation", StringComparison.Ordinal) ? operation.OperaType : compiler.DefaultName)
                : semantic.Name.Trim();
            if (operation.Name.Length > MaxNameLength)
            {
                throw new InvalidOperationException($"指令名称最长 {MaxNameLength} 个字符。");
            }
            return operation;
        }

        private static void EnsureVariableType(DicValue variable, string type, string name)
        {
            if (!string.Equals(variable?.Type, type, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"变量[{name}]类型不匹配：现有 {variable?.Type ?? "空"}，要求 {type}。");
            }
        }

        private static int FindFreeVariableIndex(Dictionary<string, DicValue> variables)
        {
            var occupied = new HashSet<int>(variables.Values.Where(value => value != null).Select(value => value.Index));
            for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
            {
                if (!occupied.Contains(i)) return i;
            }
            throw new InvalidOperationException($"变量表已满，容量为 {ValueConfigStore.ValueCapacity}。");
        }

        private static string RequiredName(string value, string path)
        {
            string result = RequiredText(value, path);
            if (result.Length > MaxNameLength)
            {
                throw new InvalidOperationException($"{path} 最长 {MaxNameLength} 个字符。");
            }
            return result;
        }

        private static string RequiredText(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{path} 不能为空。");
            return value.Trim();
        }

        private static bool IsValidKey(string value)
        {
            if (string.IsNullOrEmpty(value) || !char.IsLetter(value[0])) return false;
            return value.All(character => char.IsLetterOrDigit(character) || character == '_' || character == '-');
        }

    }
}
