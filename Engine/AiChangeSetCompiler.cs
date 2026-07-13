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
            int createdCount = ApplyProcessDefinitions(
                changeSet.Processes, processes, variables, resources, changes, ref operationCount,
                out replacedCount);

            if (deletedCount == 0 && variableCount == 0 && createdCount == 0 && replacedCount == 0)
            {
                throw new InvalidOperationException("changeSet 不包含任何变更。");
            }

            IReadOnlyCollection<string> tcpNames = GetReferenceValues(resources, "comm.tcp");
            IReadOnlyCollection<string> serialNames = GetReferenceValues(resources, "comm.serial");
            var validationContext = new ProcessDefinitionValidationContext(
                variables.Keys, tcpNames, serialNames);
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
            ValidateProcessOperationReferences(processes, changes);
            JArray processAnalyses = BuildChangedProcessAnalyses(processes, changes);

            return new AiChangeSetCompileResult
            {
                Processes = processes,
                Variables = variables,
                Changes = changes,
                DeletedProcessCount = deletedCount,
                CreatedProcessCount = createdCount,
                ReplacedProcessCount = replacedCount,
                ChangedVariableCount = variableCount,
                OperationCount = operationCount,
                ProcessAnalyses = processAnalyses
            };
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
                if ((definition.Steps?.Count ?? 0) < 1)
                {
                    throw new InvalidOperationException($"流程[{processName}]至少包含一个步骤。");
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
                var stepOperationCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                var operationIdLocations = new Dictionary<Guid, OperationReferenceLocation>();
                var operationKeyLocations = new Dictionary<string, OperationReferenceLocation>(StringComparer.Ordinal);
                Dictionary<Guid, Step> existingSteps = BuildExistingStepMap(replacedProcess);
                Dictionary<Guid, OperationType> existingOperations = BuildExistingOperationMap(replacedProcess);
                var usedStepIds = new HashSet<Guid>();
                var usedOperationIds = new HashSet<Guid>();
                foreach (StepDefinition stepDefinition in definition.Steps)
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
                    stepOperationCounts.Add(key, actualCount);
                    int stepIndex = proc.steps.Count;
                    proc.steps.Add(new Step
                    {
                        Id = existingStep?.Id ?? Guid.NewGuid(),
                        Name = string.IsNullOrWhiteSpace(stepDefinition.Name)
                            ? existingStep?.Name ?? throw new InvalidOperationException(
                                $"流程[{processName}]新步骤[{key}]必须提供 name。")
                            : RequiredName(stepDefinition.Name, $"流程[{processName}]步骤 name"),
                        Disable = stepDefinition.Disable ?? existingStep?.Disable ?? false,
                        Ops = new List<OperationType>()
                    });
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
                            string mapKey = AiOperationCompileContext.BuildOperationKey(key, operationKey);
                            if (operationKeyLocations.ContainsKey(mapKey))
                            {
                                throw new InvalidOperationException(
                                    $"流程[{processName}]步骤[{key}]指令 key 重复：{operationKey}");
                            }
                            operationKeyLocations.Add(mapKey,
                                new OperationReferenceLocation(stepIndex, operationIndex));
                        }
                    }
                }

                for (int stepIndex = 0; stepIndex < definition.Steps.Count; stepIndex++)
                {
                    StepDefinition stepDefinition = definition.Steps[stepIndex];
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
                                semantic, processName, procIndex, stepIndexes, stepOperationCounts,
                                variables, resources, operationIdLocations, operationKeyLocations);
                            if (existingOperation != null)
                            {
                                compiled.Id = existingOperation.Id;
                                if (string.IsNullOrWhiteSpace(semantic.Name))
                                {
                                    compiled.Name = existingOperation.Name;
                                }
                            }
                            step.Ops.Add(compiled);
                        }
                        semanticIndex++;
                    }
                }
                GotoRewriteResult gotoRewrite = replacedProcess == null
                    ? new GotoRewriteResult()
                    : ProcessEditingService.RewriteGotoTargets(replacedProcess, proc, procIndex);
                ProcessEditingService.RenumberOperations(proc);

                if (replacedProcess == null)
                {
                    processes.Add(proc);
                    created++;
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

        private static JArray BuildChangedProcessAnalyses(List<Proc> processes, JArray changes)
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
                ProcessFlowAnalysis analysis = ProcessFlowAnalyzer.Analyze(procIndex, processes[procIndex]);
                change["potentiallyUnbounded"] = analysis.PotentiallyUnbounded;
                var item = new JObject
                {
                    ["procIndex"] = procIndex,
                    ["procId"] = processes[procIndex]?.head?.Id.ToString("D") ?? string.Empty,
                    ["name"] = processes[procIndex]?.head?.Name ?? string.Empty,
                    ["potentiallyUnbounded"] = analysis.PotentiallyUnbounded,
                    ["cycleLocations"] = new JArray(analysis.CycleLocations)
                };
                analyses.Add(item);
            }
            return analyses;
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
            Dictionary<string, int> stepIndexes,
            Dictionary<string, int> stepOperationCounts,
            Dictionary<string, DicValue> variables,
            AiResourceSnapshot resources,
            IReadOnlyDictionary<Guid, OperationReferenceLocation> operationIdLocations,
            IReadOnlyDictionary<string, OperationReferenceLocation> operationKeyLocations)
        {
            if (semantic == null) throw new InvalidOperationException($"流程[{processName}]包含 null 指令。");
            string kind = RequiredText(semantic.Kind, $"流程[{processName}]指令 kind");
            IAiOperationCompiler compiler = AiOperationCompilerRegistry.Get(kind);
            OperationType operation = compiler.Compile(semantic, new AiOperationCompileContext(
                procIndex, stepIndexes, stepOperationCounts, variables, resources,
                operationIdLocations, operationKeyLocations));

            operation.Id = Guid.NewGuid();
            operation.Name = string.IsNullOrWhiteSpace(semantic.Name)
                ? (string.Equals(kind, "native.operation", StringComparison.Ordinal) ? operation.OperaType : compiler.DefaultName)
                : semantic.Name.Trim();
            if (operation.Name.Length > MaxNameLength)
            {
                throw new InvalidOperationException($"指令名称最长 {MaxNameLength} 个字符。");
            }
            return operation;
        }

        private static void ValidateProcessOperationReferences(List<Proc> processes, JArray changes)
        {
            var names = new HashSet<string>(processes
                .Where(proc => proc?.head != null && !string.IsNullOrWhiteSpace(proc.head.Name))
                .Select(proc => proc.head.Name), StringComparer.Ordinal);
            var changedProcessIds = new HashSet<Guid>(changes.OfType<JObject>()
                .Where(change => string.Equals(change["type"]?.Value<string>(), "process.create", StringComparison.Ordinal)
                    || string.Equals(change["type"]?.Value<string>(), "process.replace", StringComparison.Ordinal))
                .Select(change => Guid.TryParse(change["procId"]?.Value<string>(), out Guid id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty));
            for (int procIndex = 0; procIndex < processes.Count; procIndex++)
            {
                Proc proc = processes[procIndex];
                if (proc?.head == null || !changedProcessIds.Contains(proc.head.Id)) continue;
                foreach (OperationType operation in proc?.steps?
                    .Where(step => step?.Ops != null).SelectMany(step => step.Ops)
                    ?? Enumerable.Empty<OperationType>())
                {
                    if (operation is ProcOps controls)
                    {
                        foreach (procParam item in controls.procParams ?? new CustomList<procParam>())
                        {
                            if (string.IsNullOrWhiteSpace(item?.ProcName) && !string.IsNullOrWhiteSpace(item?.ProcValue)) continue;
                            if (!names.Contains(item?.ProcName ?? string.Empty))
                            {
                                throw new InvalidOperationException($"流程[{proc.head.Name}]引用的目标流程不存在：{item?.ProcName}");
                            }
                            if (string.Equals(item.ProcName, proc.head.Name, StringComparison.Ordinal)
                                && string.Equals(item.value, "运行", StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException($"流程[{proc.head.Name}]禁止启动自身。");
                            }
                        }
                    }
                    else if (operation is WaitProc waits)
                    {
                        foreach (WaitProcParam item in waits.Params ?? new CustomList<WaitProcParam>())
                        {
                            if (string.IsNullOrWhiteSpace(item?.ProcName) && !string.IsNullOrWhiteSpace(item?.ProcValue)) continue;
                            if (!names.Contains(item?.ProcName ?? string.Empty))
                            {
                                throw new InvalidOperationException($"流程[{proc.head.Name}]等待的目标流程不存在：{item?.ProcName}");
                            }
                        }
                    }
                }
            }
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
