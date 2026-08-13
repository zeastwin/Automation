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
            if (blueprint.Process == null || string.IsNullOrWhiteSpace(blueprint.Process.Name))
                throw new ArgumentException("blueprint.process.name 不能为空。", nameof(blueprint));
            if (blueprint.Steps == null || blueprint.Steps.Count == 0)
                throw new ArgumentException("blueprint.steps 至少包含一个步骤。", nameof(blueprint));

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
