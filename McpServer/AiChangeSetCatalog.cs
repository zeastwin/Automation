using Automation.Protocol;
using System.Text.Json;

namespace Automation.McpServer
{
    internal static class AiChangeSetCatalog
    {
        public static string Validate(AiChangeSet changeSet)
        {
            if (changeSet == null) return "changeSet 不能为空。";
            if (changeSet.Version != 2) return "changeSet.version 必须为2。";
            if (JsonSerializer.SerializeToUtf8Bytes(changeSet).Length > 65536) return "changeSet 超过64KB。";
            string deletionError = ValidateProcessDeletion(changeSet.DeleteProcesses);
            if (deletionError != null) return deletionError;
            if ((changeSet.Processes?.Count ?? 0) > 3) return "单次最多定义3个流程。";
            int steps = changeSet.Processes?.Sum(process => process?.Steps?.Count ?? 0) ?? 0;
            if (changeSet.Processes?.Any(process => (process?.Steps?.Count ?? 0) > 10) == true)
                return "单流程最多10个步骤。";
            int operations = changeSet.Processes?.Sum(process =>
                process?.Steps?.Sum(step => step?.Operations?.Count ?? 0) ?? 0) ?? 0;
            if (operations > 20) return "单次变更集最多20条指令。";
            if (steps == 0 && operations > 0) return "指令必须属于步骤。";
            foreach (ProcessDefinition process in changeSet.Processes ?? new List<ProcessDefinition>())
            {
                if (process == null || string.IsNullOrWhiteSpace(process.Name)) return "processes[].name 不能为空。";
                string action = string.IsNullOrWhiteSpace(process.Action) ? "create" : process.Action.Trim();
                if (action != "create" && action != "replace") return "processes[].action 只能是create或replace。";
                bool hasTargetId = !string.IsNullOrWhiteSpace(process.TargetProcId);
                bool hasTargetName = !string.IsNullOrWhiteSpace(process.TargetName);
                if (action == "replace" && hasTargetId == hasTargetName)
                    return $"流程[{process.Name}]替换时targetProcId与targetName必须且只能提供一个。";
                if (action == "create" && (hasTargetId || hasTargetName))
                    return $"流程[{process.Name}]创建时不得提供targetProcId或targetName。";
                if ((process.Steps?.Count ?? 0) == 0) return $"流程[{process.Name}]至少包含一个步骤。";
                foreach (StepDefinition step in process.Steps!)
                {
                    if (step == null || string.IsNullOrWhiteSpace(step.Key) || string.IsNullOrWhiteSpace(step.Name))
                        return $"流程[{process.Name}]的步骤 key/name 不能为空。";
                    foreach (SemanticOperation operation in step.Operations ?? new List<SemanticOperation>())
                    {
                        string error = ValidateOperation(operation);
                        if (error != null) return $"流程[{process.Name}]步骤[{step.Key}]：{error}";
                    }
                }
            }
            return null!;
        }

        private static string ValidateProcessDeletion(ProcessDeleteSelection selection)
        {
            if (selection == null) return null!;
            string mode = selection.Mode?.Trim() ?? string.Empty;
            bool hasNames = (selection.Names?.Count ?? 0) > 0;
            bool hasProcIds = (selection.ProcIds?.Count ?? 0) > 0;
            if (mode == "all")
            {
                return hasNames || hasProcIds
                    ? "deleteProcesses.mode=all 时不得提供 names 或 procIds。"
                    : null!;
            }
            if (mode == "selected")
            {
                return !hasNames && !hasProcIds
                    ? "deleteProcesses.mode=selected 时必须提供 names 或 procIds。"
                    : null!;
            }
            return "deleteProcesses.mode 只能是 all 或 selected。";
        }

        private static string ValidateOperation(SemanticOperation operation)
        {
            if (operation == null || string.IsNullOrWhiteSpace(operation.Kind)) return "指令 kind 不能为空。";
            switch (operation.Kind)
            {
                case "variable.set":
                    if (string.IsNullOrWhiteSpace(operation.Variable) || operation.Value == null)
                        return "variable.set 必须提供 variable/value。";
                    return null!;
                case "variable.add":
                    if (string.IsNullOrWhiteSpace(operation.Variable) || !operation.Amount.HasValue)
                        return "variable.add 必须提供 variable/amount。";
                    return null!;
                case "wait":
                    if (!operation.Milliseconds.HasValue || operation.Milliseconds < 0 || operation.Milliseconds > 86400000)
                        return "wait.milliseconds 必须在0..86400000之间。";
                    return null!;
                case "flow.goto":
                    return ValidateTarget(operation.Target, "flow.goto.target");
                case "branch.number_range":
                    if (string.IsNullOrWhiteSpace(operation.Variable) || !operation.Min.HasValue || !operation.Max.HasValue)
                        return "branch.number_range 必须提供 variable/min/max。";
                    return ValidateTarget(operation.WhenTrue, "branch.number_range.whenTrue")
                        ?? ValidateTarget(operation.WhenFalse, "branch.number_range.whenFalse");
                case "popup.message":
                    if (string.IsNullOrWhiteSpace(operation.Message)) return "popup.message.message 不能为空。";
                    if (ContainsPlaceholderSyntax(operation.Message))
                        return "popup.message 是固定文本，不支持 {变量名} 插值；显示变量当前值请使用 popup.variable。";
                    return operation.Target == null ? null! : ValidateTarget(operation.Target, "popup.message.target");
                case "popup.variable":
                    if (string.IsNullOrWhiteSpace(operation.Variable)) return "popup.variable.variable 不能为空。";
                    return operation.Target == null ? null! : ValidateTarget(operation.Target, "popup.variable.target");
                case "io.write":
                    if (string.IsNullOrWhiteSpace(operation.Io) || !operation.State.HasValue)
                        return "io.write 必须提供 io/state。";
                    return null!;
                case "io.wait":
                    if (string.IsNullOrWhiteSpace(operation.Io) || !operation.State.HasValue || !operation.TimeoutMs.HasValue)
                        return "io.wait 必须提供 io/state/timeoutMs。";
                    return null!;
                case "process.control":
                    if (string.IsNullOrWhiteSpace(operation.Process) || string.IsNullOrWhiteSpace(operation.Action))
                        return "process.control 必须提供 process/action。";
                    return null!;
                case "process.wait":
                    if (string.IsNullOrWhiteSpace(operation.Process) || string.IsNullOrWhiteSpace(operation.ExpectedState)
                        || !operation.TimeoutMs.HasValue)
                        return "process.wait 必须提供 process/expectedState/timeoutMs。";
                    return null!;
                case "native.operation":
                    if (string.IsNullOrWhiteSpace(operation.OperaType) || operation.Fields == null)
                        return "native.operation 必须提供精确 operaType 和 fields 对象。";
                    return null!;
                default:
                    return $"不支持的语义指令：{operation.Kind}。支持的 kind：{SemanticOperationKinds.SupportedKinds}；目标语义不明确时先调用 get_change_capabilities。";
            }
        }

        private static string ValidateTarget(OperationTarget target, string path)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.Step) || target.Operation < 0)
                return $"{path} 必须提供 step 和非负 operation。";
            return null!;
        }

        private static bool ContainsPlaceholderSyntax(string value)
        {
            int opening = value?.IndexOf('{') ?? -1;
            while (opening >= 0 && opening + 1 < value!.Length)
            {
                int closing = value.IndexOf('}', opening + 1);
                if (closing > opening + 1) return true;
                opening = value.IndexOf('{', opening + 1);
            }
            return false;
        }
    }
}
