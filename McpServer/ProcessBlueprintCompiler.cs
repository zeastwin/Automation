using Automation.Protocol;

namespace Automation.McpServer
{
    /// <summary>把新流程声明确定性展开为既有 ChangeSet V2，Bridge 无需增加第二条写入链。</summary>
    internal static class ProcessBlueprintCompiler
    {
        private const string ProcessKey = "process";

        public static AiChangeSet Compile(ProcessBlueprintDefinition blueprint)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            IReadOnlyList<ToolContractIssue> preflightIssues = CollectPreflightIssues(blueprint);
            if (preflightIssues.Count > 0)
                throw new ToolContractValidationException("BLUEPRINT_INVALID", preflightIssues);
            if (blueprint.Process == null || string.IsNullOrWhiteSpace(blueprint.Process.Name))
                throw new ArgumentException("blueprint.process.name 不能为空。", nameof(blueprint));
            if (blueprint.Steps == null || blueprint.Steps.Count == 0)
                throw new ArgumentException("blueprint.steps 至少包含一个步骤。", nameof(blueprint));

            PrepareRetryMechanics(blueprint);

            var stepKeys = new HashSet<string>(StringComparer.Ordinal);
            var operationKeys = new HashSet<string>(StringComparer.Ordinal);
            var actions = new List<ChangeSetAction>
            {
                new ChangeSetAction
                {
                    Type = "process.create",
                    Process = new ProcessActionValue
                    {
                        Key = ProcessKey,
                        Name = blueprint.Process.Name.Trim(),
                        AutoStart = blueprint.Process.AutoStart,
                        Disable = blueprint.Process.Disable
                    }
                }
            };

            for (int stepIndex = 0; stepIndex < blueprint.Steps.Count; stepIndex++)
            {
                ProcessBlueprintStep step = blueprint.Steps[stepIndex]
                    ?? throw new ArgumentException($"blueprint.steps[{stepIndex}] 不能为空。", nameof(blueprint));
                if (string.IsNullOrWhiteSpace(step.Name))
                    throw new ArgumentException($"blueprint.steps[{stepIndex}].name 不能为空。", nameof(blueprint));
                if (step.Operations == null || step.Operations.Count == 0)
                    throw new ArgumentException($"blueprint.steps[{stepIndex}].operations 至少包含一条指令。", nameof(blueprint));

                string stepKey = ResolveKey(step.Key, "step_" + (stepIndex + 1), stepKeys,
                    $"blueprint.steps[{stepIndex}].key");
                step.Key = stepKey;
                actions.Add(new ChangeSetAction
                {
                    Type = "step.append",
                    TargetProcess = new ProcessSelector { Key = ProcessKey },
                    Step = new StepActionValue
                    {
                        Key = stepKey,
                        Name = step.Name.Trim(),
                        Disable = step.Disable
                    }
                });

                for (int operationIndex = 0; operationIndex < step.Operations.Count; operationIndex++)
                {
                    SemanticOperation operation = step.Operations[operationIndex]
                        ?? throw new ArgumentException(
                            $"blueprint.steps[{stepIndex}].operations[{operationIndex}] 不能为空。",
                            nameof(blueprint));
                    operation.Key = ResolveKey(
                        operation.Key,
                        $"op_{stepIndex + 1}_{operationIndex + 1}",
                        operationKeys,
                        $"blueprint.steps[{stepIndex}].operations[{operationIndex}].key");
                    actions.Add(new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = ProcessKey },
                        TargetStep = new StepSelector { Key = stepKey },
                        Operation = operation
                    });
                }
            }

            NormalizeAndValidateTargets(blueprint);
            ValidateLoopStateInitialization(blueprint);
            ValidateRetryPolicies(blueprint);

            var variables = (blueprint.Variables ?? new List<ProcessBlueprintVariable>())
                .Select((item, index) => CompileVariable(item, index)).ToList();
            var result = new AiChangeSet
            {
                Version = 2,
                Title = string.IsNullOrWhiteSpace(blueprint.Title)
                    ? "创建流程：" + blueprint.Process.Name.Trim()
                    : blueprint.Title.Trim(),
                Actions = actions,
                Variables = variables
            };
            string validationError = AiChangeSetCatalog.Validate(result);
            if (validationError != null) throw new ArgumentException(validationError, nameof(blueprint));
            return result;
        }

        private static IReadOnlyList<ToolContractIssue> CollectPreflightIssues(
            ProcessBlueprintDefinition blueprint)
        {
            var issues = new List<ToolContractIssue>();
            void Add(string path, string rule, string message, string repair) => issues.Add(
                new ToolContractIssue
                {
                    Path = path,
                    Rule = rule,
                    Message = message,
                    SuggestedRepair = repair
                });
            if (blueprint.Process == null || string.IsNullOrWhiteSpace(blueprint.Process.Name))
                Add("$.blueprint.process.name", "required", "流程名称不能为空。", "填写本次新流程的明确名称。");
            if (blueprint.Steps == null || blueprint.Steps.Count == 0)
                Add("$.blueprint.steps", "min_items", "至少需要一个步骤。", "加入一个有名称且包含指令的步骤。");

            var operationKeys = new HashSet<string>(StringComparer.Ordinal);
            var duplicateOperationKeys = new HashSet<string>(StringComparer.Ordinal);
            List<ProcessBlueprintStep> steps = blueprint.Steps ?? new List<ProcessBlueprintStep>();
            for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                ProcessBlueprintStep step = steps[stepIndex];
                if (step == null)
                {
                    Add($"$.blueprint.steps[{stepIndex}]", "required", "步骤不能为空。", "移除空项或填写步骤对象。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(step.Name))
                    Add($"$.blueprint.steps[{stepIndex}].name", "required", "步骤名称不能为空。", "填写该业务阶段的名称。");
                if (step.Operations == null || step.Operations.Count == 0)
                {
                    Add($"$.blueprint.steps[{stepIndex}].operations", "min_items", "步骤至少需要一条指令。", "未知动作使用config.placeholder保留结构。");
                    continue;
                }
                for (int operationIndex = 0; operationIndex < step.Operations.Count; operationIndex++)
                {
                    SemanticOperation operation = step.Operations[operationIndex];
                    if (operation == null)
                    {
                        Add($"$.blueprint.steps[{stepIndex}].operations[{operationIndex}]", "required", "指令不能为空。", "移除空项或填写语义指令。");
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(operation.Key)
                        && !operationKeys.Add(operation.Key.Trim()))
                        duplicateOperationKeys.Add(operation.Key.Trim());
                }
            }
            foreach (string duplicate in duplicateOperationKeys)
                Add("$.blueprint.steps[].operations[].key", "unique", $"指令key重复：{duplicate}。", "为每条被引用指令使用唯一局部key。");

            var declaredVariables = (blueprint.Variables ?? new List<ProcessBlueprintVariable>())
                .Where(variable => !string.IsNullOrWhiteSpace(variable?.Name))
                .GroupBy(variable => variable.Name.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var retryDecisions = new HashSet<string>(StringComparer.Ordinal);
            List<ProcessBlueprintRetryPolicy> retries = blueprint.Retries
                ?? new List<ProcessBlueprintRetryPolicy>();
            for (int index = 0; index < retries.Count; index++)
            {
                ProcessBlueprintRetryPolicy policy = retries[index];
                if (policy == null)
                {
                    Add($"$.blueprint.retries[{index}]", "required", "重试策略不能为空。", "移除空项或填写完整重试策略。");
                    continue;
                }
                string entry = (policy.EntryOperationKey ?? string.Empty).Trim();
                string decision = (policy.RetryDecisionOperationKey ?? string.Empty).Trim();
                if (entry.Length == 0 || !operationKeys.Contains(entry))
                    Add($"$.blueprint.retries[{index}].entryOperationKey", "existing_key", $"重试入口不存在：{entry}。", "引用steps[].operations[]中真实且唯一的key。");
                if (decision.Length == 0 || !operationKeys.Contains(decision))
                    Add($"$.blueprint.retries[{index}].retryDecisionOperationKey", "existing_key", $"重试判断不存在：{decision}。", "引用带whenFalse失败出口的判断指令key。");
                else if (!retryDecisions.Add(decision))
                    Add($"$.blueprint.retries[{index}].retryDecisionOperationKey", "unique", $"重试判断重复声明：{decision}。", "一个判断指令只声明一个重试策略。");
                if (policy.MaxAttempts < 1 || policy.MaxAttempts > 100)
                    Add($"$.blueprint.retries[{index}].maxAttempts", "range", "总尝试次数必须在1..100。", "填写包含首次尝试的总次数。");
                string[] resets = NormalizeRetryVariables(policy.ResetVariables);
                string[] clears = NormalizeRetryVariables(policy.ClearVariables);
                foreach (string overlap in resets.Intersect(clears, StringComparer.Ordinal))
                    Add($"$.blueprint.retries[{index}]", "exclusive", $"变量[{overlap}]不能同时复位和清空。", "double状态放resetVariables，string缓存放clearVariables。");
                foreach (string variable in resets)
                {
                    if (!declaredVariables.TryGetValue(variable, out ProcessBlueprintVariable? value)
                        || value == null
                        || (!string.IsNullOrWhiteSpace(value.Type)
                            && !string.Equals(value.Type, VariableChangeContract.DoubleType, StringComparison.Ordinal)))
                    {
                        Add($"$.blueprint.retries[{index}].resetVariables", "double_variable", $"复位变量[{variable}]必须声明为double。", "在blueprint.variables中声明double业务状态变量。");
                    }
                }
                foreach (string variable in clears)
                {
                    if (!declaredVariables.TryGetValue(variable, out ProcessBlueprintVariable? value)
                        || value == null
                        || !string.Equals(value.Type, VariableChangeContract.StringType, StringComparison.Ordinal))
                    {
                        Add($"$.blueprint.retries[{index}].clearVariables", "string_variable", $"清理变量[{variable}]必须声明为string。", "在blueprint.variables中声明string结果缓存。");
                    }
                }
            }
            return issues;
        }

        private static void NormalizeAndValidateTargets(ProcessBlueprintDefinition blueprint)
        {
            var steps = blueprint.Steps.ToDictionary(step => step.Key, StringComparer.Ordinal);
            foreach (ProcessBlueprintStep sourceStep in blueprint.Steps)
            {
                foreach (SemanticOperation operation in sourceStep.Operations)
                {
                    foreach (OperationTarget target in EnumerateTargets(operation))
                    {
                        if (target == null) continue;
                        if (!string.IsNullOrWhiteSpace(target.OperationId)
                            || !string.IsNullOrWhiteSpace(target.StepId))
                        {
                            throw new ArgumentException(
                                "Blueprint跳转只能引用本次蓝图的stepKey/operationKey，不能引用已提交ID。",
                                nameof(blueprint));
                        }

                        string? stepKey = target.StepKey?.Trim();
                        string? operationKey = target.OperationKey?.Trim();
                        string? entryMode = target.EntryMode?.Trim();
                        if (string.IsNullOrWhiteSpace(stepKey))
                        {
                            if (string.IsNullOrWhiteSpace(operationKey))
                                throw new ArgumentException("Blueprint当前步骤内跳转必须填写operationKey。", nameof(blueprint));
                            if (!string.IsNullOrWhiteSpace(entryMode))
                                throw new ArgumentException("Blueprint当前步骤内跳转不接受entryMode。", nameof(blueprint));
                            continue;
                        }
                        if (!steps.TryGetValue(stepKey, out ProcessBlueprintStep? targetStep)
                            || targetStep == null)
                            throw new ArgumentException($"Blueprint跳转目标步骤不存在：{stepKey}。", nameof(blueprint));

                        string firstOperationKey = targetStep.Operations[0].Key;
                        if (string.IsNullOrWhiteSpace(operationKey))
                        {
                            if (!string.IsNullOrWhiteSpace(entryMode)
                                && !string.Equals(entryMode, "first", StringComparison.Ordinal))
                            {
                                throw new ArgumentException(
                                    $"Blueprint跨步骤只提供stepKey时entryMode只能为first：{stepKey}。",
                                    nameof(blueprint));
                            }
                            target.OperationKey = firstOperationKey;
                            target.EntryMode = null;
                            continue;
                        }

                        bool pointsToFirst = string.Equals(
                            operationKey, firstOperationKey, StringComparison.Ordinal);
                        if (!pointsToFirst
                            && !string.Equals(entryMode, "operation", StringComparison.Ordinal))
                        {
                            throw new ArgumentException(
                                $"Blueprint跨步骤跳转默认必须进入步骤[{stepKey}]首指令[{firstOperationKey}]；"
                                + $"确需进入中段[{operationKey}]时显式填写entryMode=operation。",
                                nameof(blueprint));
                        }
                        if (pointsToFirst && !string.IsNullOrWhiteSpace(entryMode)
                            && !string.Equals(entryMode, "first", StringComparison.Ordinal)
                            && !string.Equals(entryMode, "operation", StringComparison.Ordinal))
                        {
                            throw new ArgumentException("Blueprint entryMode只能是first或operation。", nameof(blueprint));
                        }
                        target.EntryMode = null;
                    }
                }
            }
        }

        private static void ValidateLoopStateInitialization(ProcessBlueprintDefinition blueprint)
        {
            var entries = blueprint.Steps
                .SelectMany(step => step.Operations.Select(operation => new BlueprintOperationEntry
                {
                    Step = step,
                    Operation = operation
                }))
                .ToList();
            List<SemanticOperation> operations = entries.Select(entry => entry.Operation).ToList();
            var positions = entries
                .Select((entry, index) => new { entry.Operation.Key, Index = index })
                .ToDictionary(item => item.Key, item => item.Index, StringComparer.Ordinal);
            var declaredVariables = new HashSet<string>(
                (blueprint.Variables ?? new List<ProcessBlueprintVariable>())
                    .Where(variable => !string.IsNullOrWhiteSpace(variable?.Name))
                    .Select(variable => variable.Name.Trim()),
                StringComparer.Ordinal);

            for (int sourceIndex = 0; sourceIndex < operations.Count; sourceIndex++)
            {
                foreach (OperationTarget target in EnumerateTargets(operations[sourceIndex]))
                {
                    if (target == null
                        || string.IsNullOrWhiteSpace(target.OperationKey)
                        || !positions.TryGetValue(target.OperationKey, out int targetIndex)
                        || targetIndex > sourceIndex)
                    {
                        continue;
                    }

                    SemanticOperation loopEntry = operations[targetIndex];
                    if (!string.Equals(loopEntry.Kind, "variable.add", StringComparison.Ordinal)
                        || string.IsNullOrWhiteSpace(loopEntry.Variable)
                        || !declaredVariables.Contains(loopEntry.Variable.Trim()))
                    {
                        continue;
                    }
                    string variable = loopEntry.Variable.Trim();
                    int resetIndex = -1;
                    for (int candidateIndex = 0; candidateIndex < targetIndex; candidateIndex++)
                    {
                        var candidate = entries[candidateIndex];
                        if (candidate.Step.Disable != true
                            && string.Equals(candidate.Operation.Kind, "variable.set", StringComparison.Ordinal)
                            && string.Equals(candidate.Operation.Variable?.Trim(), variable, StringComparison.Ordinal)
                            && double.TryParse(candidate.Operation.Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out double resetValue)
                            && resetValue == 0d)
                        {
                            resetIndex = candidateIndex;
                            break;
                        }
                    }
                    bool resetCanBeBypassed = resetIndex > 0 && entries.Take(resetIndex).Any(entry =>
                        entry.Step.Disable != true
                        && (string.Equals(entry.Operation.Kind, "flow.end", StringComparison.Ordinal)
                            || string.Equals(entry.Operation.Kind, "native.operation", StringComparison.Ordinal)
                            || EnumerateTargets(entry.Operation).Any(target => target != null)));
                    if (resetIndex < 0 || resetCanBeBypassed)
                    {
                        throw new ArgumentException(
                            $"Blueprint回环累加变量[{variable}]必须在不可绕过的前置路径使用variable.set value=0复位，"
                            + "且复位前不能出现跳转、结束或未证明顺序性的原生指令，避免多次运行沿用上次计数。",
                            nameof(blueprint));
                    }
                }
            }
        }

        private static void ValidateRetryPolicies(ProcessBlueprintDefinition blueprint)
        {
            var entries = blueprint.Steps
                .SelectMany(step => step.Operations.Select(operation => new BlueprintOperationEntry
                {
                    Step = step,
                    Operation = operation
                }))
                .ToList();
            var positions = entries
                .Select((entry, index) => new { entry.Operation.Key, Index = index })
                .ToDictionary(item => item.Key, item => item.Index, StringComparer.Ordinal);
            var operations = entries.ToDictionary(entry => entry.Operation.Key, entry => entry.Operation, StringComparer.Ordinal);
            var variables = (blueprint.Variables ?? new List<ProcessBlueprintVariable>())
                .Where(variable => !string.IsNullOrWhiteSpace(variable?.Name))
                .GroupBy(variable => variable.Name.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            List<ProcessBlueprintRetryPolicy> policies = blueprint.Retries
                ?? new List<ProcessBlueprintRetryPolicy>();
            var policiesByDecision = new Dictionary<string, ProcessBlueprintRetryPolicy>(StringComparer.Ordinal);

            for (int index = 0; index < policies.Count; index++)
            {
                ProcessBlueprintRetryPolicy policy = policies[index]
                    ?? throw new ArgumentException($"blueprint.retries[{index}] 不能为空。", nameof(blueprint));
                string entryKey = RequireRetryText(policy.EntryOperationKey, $"blueprint.retries[{index}].entryOperationKey");
                string decisionKey = RequireRetryText(policy.RetryDecisionOperationKey, $"blueprint.retries[{index}].retryDecisionOperationKey");
                string counterVariable = RequireRetryText(policy.CounterVariable, $"blueprint.retries[{index}].counterVariable");
                if (policiesByDecision.ContainsKey(decisionKey))
                    throw new ArgumentException($"Blueprint重试判断重复声明：{decisionKey}。", nameof(blueprint));
                policiesByDecision.Add(decisionKey, policy);
                if (policy.MaxAttempts < 1 || policy.MaxAttempts > 100)
                    throw new ArgumentException($"Blueprint重试总尝试次数必须在1..100：{policy.MaxAttempts}。", nameof(blueprint));
                if (!positions.TryGetValue(entryKey, out int entryIndex))
                    throw new ArgumentException($"Blueprint重试入口不存在：{entryKey}。", nameof(blueprint));
                if (!positions.TryGetValue(decisionKey, out int decisionIndex))
                    throw new ArgumentException($"Blueprint重试判断不存在：{decisionKey}。", nameof(blueprint));
                if (entryIndex >= decisionIndex)
                    throw new ArgumentException($"Blueprint重试入口必须位于判断之前：{entryKey} -> {decisionKey}。", nameof(blueprint));
                if (!variables.TryGetValue(counterVariable, out ProcessBlueprintVariable? counter)
                    || counter == null
                    || !string.Equals(counter.Scope?.Trim(), VariableScopeContract.Process, StringComparison.Ordinal)
                    || (!string.IsNullOrWhiteSpace(counter.Type)
                        && !string.Equals(counter.Type.Trim(), VariableChangeContract.DoubleType, StringComparison.Ordinal)))
                {
                    throw new ArgumentException(
                        $"Blueprint重试计数变量[{counterVariable}]必须在variables中声明为process作用域double。",
                        nameof(blueprint));
                }

                SemanticOperation decision = operations[decisionKey];
                if (!string.Equals(decision.Kind, "branch.number_compare", StringComparison.Ordinal)
                    || !string.Equals(decision.Variable?.Trim(), counterVariable, StringComparison.Ordinal)
                    || !string.Equals(decision.Comparison, "lt", StringComparison.Ordinal)
                    || decision.CompareValue != policy.MaxAttempts)
                {
                    throw new ArgumentException(
                        $"Blueprint重试判断[{decisionKey}]必须表达 {counterVariable} < {policy.MaxAttempts}。",
                        nameof(blueprint));
                }
                string? failureKey = decision.WhenFalse?.OperationKey?.Trim();
                if (!TargetPointsTo(decision.WhenTrue, entryKey)
                    || string.IsNullOrWhiteSpace(failureKey)
                    || !positions.TryGetValue(failureKey, out int failureIndex)
                    || failureIndex <= decisionIndex)
                {
                    throw new ArgumentException(
                        $"Blueprint重试判断[{decisionKey}]成立分支必须回到每次尝试入口[{entryKey}]，不成立分支必须前进到判断之后的失败出口。",
                        nameof(blueprint));
                }

                int incrementCount = entries.Skip(entryIndex).Take(decisionIndex - entryIndex)
                    .Count(entry => string.Equals(entry.Operation.Kind, "variable.add", StringComparison.Ordinal)
                        && string.Equals(entry.Operation.Variable?.Trim(), counterVariable, StringComparison.Ordinal)
                        && entry.Operation.Amount == 1d);
                if (incrementCount != 1)
                    throw new ArgumentException($"Blueprint每次尝试必须且只能将计数变量[{counterVariable}]加1。", nameof(blueprint));
                if (!HasUnbypassableCounterReset(entries, entryIndex, counterVariable))
                    throw new ArgumentException($"Blueprint重试计数变量[{counterVariable}]必须在首次尝试入口前不可绕过地复位为0。", nameof(blueprint));

                string[] resetVariables = NormalizeRetryVariables(policy.ResetVariables);
                string[] clearVariables = NormalizeRetryVariables(policy.ClearVariables);
                if (resetVariables.Intersect(clearVariables, StringComparer.Ordinal).Any())
                    throw new ArgumentException("Blueprint同一变量不能同时声明为重试复位变量和清理变量。", nameof(blueprint));
                ValidateAttemptPrefix(entries, entryIndex, resetVariables, clearVariables, entryKey);
            }

            foreach (var entry in entries)
            {
                SemanticOperation operation = entry.Operation;
                if (!string.Equals(operation.Kind, "branch.number_compare", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(operation.Variable)) continue;
                bool hasBackwardTarget = EnumerateTargets(operation).Any(target =>
                    target != null
                    && !string.IsNullOrWhiteSpace(target.OperationKey)
                    && positions.TryGetValue(target.OperationKey, out int targetIndex)
                    && targetIndex <= positions[operation.Key]);
                bool incrementsCounterBefore = entries.Take(positions[operation.Key]).Any(candidate =>
                    string.Equals(candidate.Operation.Kind, "variable.add", StringComparison.Ordinal)
                    && string.Equals(candidate.Operation.Variable?.Trim(), operation.Variable.Trim(), StringComparison.Ordinal));
                if (hasBackwardTarget && incrementsCounterBefore && !policiesByDecision.ContainsKey(operation.Key))
                {
                    throw new ArgumentException(
                        $"Blueprint带计数器的重试回环[{operation.Key}]必须在retries中声明标准重试契约。",
                        nameof(blueprint));
                }
            }
        }

        private static void PrepareRetryMechanics(ProcessBlueprintDefinition blueprint)
        {
            List<ProcessBlueprintRetryPolicy> policies = blueprint.Retries
                ?? new List<ProcessBlueprintRetryPolicy>();
            if (policies.Count == 0) return;
            blueprint.Variables ??= new List<ProcessBlueprintVariable>();
            var usedOperationKeys = new HashSet<string>(
                blueprint.Steps.SelectMany(step => step?.Operations ?? new List<SemanticOperation>())
                    .Where(operation => !string.IsNullOrWhiteSpace(operation?.Key))
                    .Select(operation => operation.Key.Trim()),
                StringComparer.Ordinal);
            var usedVariableNames = new HashSet<string>(
                blueprint.Variables.Where(variable => !string.IsNullOrWhiteSpace(variable?.Name))
                    .Select(variable => variable.Name.Trim()),
                StringComparer.Ordinal);

            for (int policyIndex = 0; policyIndex < policies.Count; policyIndex++)
            {
                ProcessBlueprintRetryPolicy policy = policies[policyIndex]
                    ?? throw new ArgumentException($"blueprint.retries[{policyIndex}] 不能为空。", "blueprint");
                string entryKey = RequireRetryText(
                    policy.EntryOperationKey, $"blueprint.retries[{policyIndex}].entryOperationKey");
                string decisionKey = RequireRetryText(
                    policy.RetryDecisionOperationKey, $"blueprint.retries[{policyIndex}].retryDecisionOperationKey");
                if (policy.MaxAttempts < 1 || policy.MaxAttempts > 100)
                    throw new ArgumentException(
                        $"Blueprint重试总尝试次数必须在1..100：{policy.MaxAttempts}。", "blueprint");

                (ProcessBlueprintStep Step, int Index)? entryLocation = FindBlueprintOperation(blueprint, entryKey);
                (ProcessBlueprintStep Step, int Index)? decisionLocation = FindBlueprintOperation(blueprint, decisionKey);
                if (!entryLocation.HasValue)
                    throw new ArgumentException($"Blueprint重试入口不存在：{entryKey}。", "blueprint");
                if (!decisionLocation.HasValue)
                    throw new ArgumentException($"Blueprint重试判断不存在：{decisionKey}。", "blueprint");
                int entryPosition = FlattenedOperationPosition(blueprint, entryKey);
                int decisionPosition = FlattenedOperationPosition(blueprint, decisionKey);
                if (entryPosition >= decisionPosition)
                    throw new ArgumentException($"Blueprint重试入口必须位于判断之前：{entryKey} -> {decisionKey}。", "blueprint");

                string counterVariable = CreateUniqueVariableName(
                    usedVariableNames, $"__automation_retry_{policyIndex + 1}");
                policy.CounterVariable = counterVariable;
                blueprint.Variables.Add(new ProcessBlueprintVariable
                {
                    Name = counterVariable,
                    Scope = VariableScopeContract.Process,
                    Type = VariableChangeContract.DoubleType,
                    Value = "0",
                    Note = "Automation Blueprint 编译器生成的内部重试计数器",
                    Policy = VariableChangeContract.CreatePolicy
                });

                ProcessBlueprintStep entryStep = entryLocation.Value.Step;
                int insertionIndex = entryLocation.Value.Index;
                string counterResetKey = CreateUniqueOperationKey(
                    usedOperationKeys, $"retry_{policyIndex + 1}_counter_reset");
                entryStep.Operations.Insert(insertionIndex++, new SemanticOperation
                {
                    Key = counterResetKey,
                    Kind = "variable.set",
                    Variable = counterVariable,
                    Value = "0"
                });

                string effectiveEntryKey = entryKey;
                foreach (string variable in NormalizeRetryVariables(policy.ResetVariables))
                {
                    string resetKey = CreateUniqueOperationKey(
                        usedOperationKeys, $"retry_{policyIndex + 1}_reset");
                    if (string.Equals(effectiveEntryKey, entryKey, StringComparison.Ordinal))
                        effectiveEntryKey = resetKey;
                    entryStep.Operations.Insert(insertionIndex++, new SemanticOperation
                    {
                        Key = resetKey,
                        Kind = "variable.set",
                        Variable = variable,
                        Value = "0"
                    });
                }
                foreach (string variable in NormalizeRetryVariables(policy.ClearVariables))
                {
                    string clearKey = CreateUniqueOperationKey(
                        usedOperationKeys, $"retry_{policyIndex + 1}_clear");
                    if (string.Equals(effectiveEntryKey, entryKey, StringComparison.Ordinal))
                        effectiveEntryKey = clearKey;
                    entryStep.Operations.Insert(insertionIndex++, new SemanticOperation
                    {
                        Key = clearKey,
                        Kind = "variable.clear",
                        Variable = variable
                    });
                }
                policy.EntryOperationKey = effectiveEntryKey;

                decisionLocation = FindBlueprintOperation(blueprint, decisionKey);
                ProcessBlueprintStep decisionStep = decisionLocation!.Value.Step;
                int decisionIndex = decisionLocation.Value.Index;
                decisionStep.Operations.Insert(decisionIndex, new SemanticOperation
                {
                    Key = CreateUniqueOperationKey(
                        usedOperationKeys, $"retry_{policyIndex + 1}_increment"),
                    Kind = "variable.add",
                    Variable = counterVariable,
                    Amount = 1d
                });
                SemanticOperation decision = decisionStep.Operations[decisionIndex + 1];
                if (decision.WhenFalse == null)
                    throw new ArgumentException(
                        $"Blueprint重试判断[{decisionKey}]必须提供耗尽后的whenFalse失败出口。", "blueprint");
                decision.Kind = "branch.number_compare";
                decision.Variable = counterVariable;
                decision.Comparison = "lt";
                decision.CompareValue = policy.MaxAttempts;
                decision.WhenTrue = new OperationTarget { OperationKey = effectiveEntryKey };
            }
        }

        private static (ProcessBlueprintStep Step, int Index)? FindBlueprintOperation(
            ProcessBlueprintDefinition blueprint,
            string operationKey)
        {
            foreach (ProcessBlueprintStep step in blueprint.Steps)
            {
                if (step == null) continue;
                int index = step.Operations?.FindIndex(operation => string.Equals(
                    operation?.Key?.Trim(), operationKey, StringComparison.Ordinal)) ?? -1;
                if (index >= 0) return (step, index);
            }
            return null;
        }

        private static int FlattenedOperationPosition(
            ProcessBlueprintDefinition blueprint,
            string operationKey)
        {
            int position = 0;
            foreach (ProcessBlueprintStep step in blueprint.Steps)
            {
                foreach (SemanticOperation operation in step?.Operations ?? new List<SemanticOperation>())
                {
                    if (string.Equals(operation?.Key?.Trim(), operationKey, StringComparison.Ordinal))
                        return position;
                    position++;
                }
            }
            return -1;
        }

        private static string CreateUniqueVariableName(ISet<string> used, string prefix)
        {
            string value = prefix;
            int suffix = 2;
            while (!used.Add(value)) value = prefix + "_" + suffix++;
            return value;
        }

        private static string CreateUniqueOperationKey(ISet<string> used, string prefix)
        {
            string value = prefix.Length <= 32 ? prefix : prefix.Substring(0, 32);
            int suffix = 2;
            while (!used.Add(value))
            {
                string tail = "_" + suffix++;
                value = prefix.Substring(0, Math.Min(prefix.Length, 32 - tail.Length)) + tail;
            }
            return value;
        }

        private static void ValidateAttemptPrefix(
            IReadOnlyList<BlueprintOperationEntry> entries,
            int entryIndex,
            IReadOnlyCollection<string> resetVariables,
            IReadOnlyCollection<string> clearVariables,
            string entryKey)
        {
            var pendingResets = new HashSet<string>(resetVariables, StringComparer.Ordinal);
            var pendingClears = new HashSet<string>(clearVariables, StringComparer.Ordinal);
            for (int index = entryIndex; index < entries.Count; index++)
            {
                SemanticOperation operation = entries[index].Operation;
                bool consumed = false;
                string? variableName = operation.Variable?.Trim();
                if (string.Equals(operation.Kind, "variable.set", StringComparison.Ordinal)
                    && variableName != null
                    && pendingResets.Contains(variableName)
                    && double.TryParse(operation.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double resetValue)
                    && resetValue == 0d)
                {
                    pendingResets.Remove(variableName);
                    consumed = true;
                }
                else if (string.Equals(operation.Kind, "variable.clear", StringComparison.Ordinal)
                    && pendingClears.Remove(operation.Variable?.Trim() ?? string.Empty))
                {
                    consumed = true;
                }
                if (!consumed) break;
            }
            if (pendingResets.Count > 0 || pendingClears.Count > 0)
            {
                throw new ArgumentException(
                    $"Blueprint每次尝试入口[{entryKey}]必须连续完成状态复位和结果清理；"
                    + $"缺少reset=[{string.Join(",", pendingResets)}]、clear=[{string.Join(",", pendingClears)}]。",
                    "blueprint");
            }
        }

        private static bool HasUnbypassableCounterReset(
            IReadOnlyList<BlueprintOperationEntry> entries,
            int entryIndex,
            string counterVariable)
        {
            int resetIndex = -1;
            for (int index = 0; index < entryIndex; index++)
            {
                SemanticOperation operation = entries[index].Operation;
                if (string.Equals(operation.Kind, "variable.set", StringComparison.Ordinal)
                    && string.Equals(operation.Variable?.Trim(), counterVariable, StringComparison.Ordinal)
                    && double.TryParse(operation.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double resetValue)
                    && resetValue == 0d)
                {
                    resetIndex = index;
                }
            }
            if (resetIndex < 0) return false;
            return !entries.Take(resetIndex).Any(entry =>
                entry.Step.Disable != true
                && (string.Equals(entry.Operation.Kind, "flow.end", StringComparison.Ordinal)
                    || string.Equals(entry.Operation.Kind, "native.operation", StringComparison.Ordinal)
                    || EnumerateTargets(entry.Operation).Any(target => target != null)));
        }

        private static string[] NormalizeRetryVariables(IEnumerable<string> variables)
        {
            return (variables ?? Enumerable.Empty<string>())
                .Select(variable => (variable ?? string.Empty).Trim())
                .Where(variable => variable.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string RequireRetryText(string value, string path)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0) throw new ArgumentException(path + " 不能为空。", "blueprint");
            return normalized;
        }

        private static bool TargetPointsTo(OperationTarget target, string operationKey) =>
            target != null && string.Equals(target.OperationKey?.Trim(), operationKey, StringComparison.Ordinal);

        private sealed class BlueprintOperationEntry
        {
            public ProcessBlueprintStep Step { get; set; } = null!;
            public SemanticOperation Operation { get; set; } = null!;
        }

        private static IEnumerable<OperationTarget> EnumerateTargets(SemanticOperation operation)
        {
            if (operation == null) yield break;
            if (operation.Target != null) yield return operation.Target;
            if (operation.WhenTrue != null) yield return operation.WhenTrue;
            if (operation.WhenFalse != null) yield return operation.WhenFalse;
            if (operation.OnFailure != null) yield return operation.OnFailure;
        }

        private static VariableChange CompileVariable(ProcessBlueprintVariable value, int index)
        {
            if (value == null)
                throw new ArgumentException($"blueprint.variables[{index}] 不能为空。", "blueprint");
            string? scope = value.Scope?.Trim();
            return new VariableChange
            {
                Name = value.Name?.Trim(),
                Scope = scope,
                OwnerProcess = string.Equals(scope, VariableScopeContract.Process, StringComparison.Ordinal)
                    ? new ProcessSelector { Key = ProcessKey }
                    : null,
                Index = value.Index,
                Type = value.Type,
                Value = value.Value,
                Note = value.Note,
                Policy = value.Policy
            };
        }

        private static string ResolveKey(
            string requested,
            string generated,
            ISet<string> used,
            string path)
        {
            string value = string.IsNullOrWhiteSpace(requested) ? generated : requested.Trim();
            if (value.Length > 32 || !char.IsLetter(value[0])
                || value.Any(character => !char.IsLetterOrDigit(character) && character != '_' && character != '-'))
                throw new ArgumentException($"{path} 必须匹配 ^[A-Za-z][A-Za-z0-9_-]{{0,31}}$。", "blueprint");
            if (!used.Add(value)) throw new ArgumentException($"{path} 重复：{value}。", "blueprint");
            return value;
        }
    }
}
