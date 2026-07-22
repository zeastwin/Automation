using Automation.Protocol;
// 模块：MCP / ChangeSet 参数校验。
// 职责范围：在进入 Bridge 前执行 DTO 级结构校验，不负责资源、流程状态或运行语义判断。
// 排查入口：参数被拒绝先看这里的字段错误；资源与提交状态错误继续查 Bridge 和编译器。

namespace Automation.McpServer
{
    internal static class AiChangeSetCatalog
    {
        public static string Validate(AiChangeSet changeSet)
        {
            if (changeSet == null) return "changeSet 不能为空。";
            if (changeSet.Version != 2) return "changeSet.version 必须为2。";
            List<ChangeSetAction> actions = changeSet.Actions ?? new List<ChangeSetAction>();
            for (int index = 0; index < actions.Count; index++)
            {
                ChangeSetAction action = actions[index];
                if (action == null || string.IsNullOrWhiteSpace(action.Type))
                    return $"actions[{index}].type 不能为空。";
                if (!ChangeSetActionTypes.SupportedTypes.Split('、').Contains(action.Type, StringComparer.Ordinal))
                    return $"actions[{index}].type 不受支持：{action.Type}。";
                if (action.Operation != null)
                {
                    string operationError = ValidateOperation(action.Operation);
                    if (operationError != null) return $"actions[{index}].operation：{operationError}";
                }
            }
            return VariableChangeContract.Validate(changeSet.Variables);
        }

        private static string ValidateOperation(SemanticOperation operation)
        {
            if (operation == null) return "指令不能为空。";
            if (string.IsNullOrWhiteSpace(operation.Kind))
            {
                return !string.IsNullOrWhiteSpace(operation.OpId)
                    ? null!
                    : "指令必须提供 opId 复用现有指令，或提供 kind 定义新指令。";
            }
            if ((operation.ClearFields?.Count ?? 0) > 0
                && !string.Equals(operation.Kind, "native.operation", StringComparison.Ordinal))
                return "clearFields 仅用于 operation.update 的 native.operation。";
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
                case "variable.compute":
                    if (string.IsNullOrWhiteSpace(operation.SourceVariable)
                        || string.IsNullOrWhiteSpace(operation.OutputVariable)
                        || string.IsNullOrWhiteSpace(operation.Operator))
                        return "variable.compute 必须提供 sourceVariable/operator/outputVariable。";
                    bool hasOperandValue = operation.OperandValue.HasValue;
                    bool hasOperandVariable = !string.IsNullOrWhiteSpace(operation.OperandVariable);
                    if (operation.Operator == "absolute")
                        return hasOperandValue || hasOperandVariable
                            ? "variable.compute.operator=absolute 时不得提供操作数。"
                            : null!;
                    if (!new[] { "add", "subtract", "multiply", "divide", "modulo" }
                        .Contains(operation.Operator, StringComparer.Ordinal))
                        return "variable.compute.operator 只能是 add/subtract/multiply/divide/modulo/absolute。";
                    if (hasOperandValue == hasOperandVariable)
                        return "variable.compute 必须且只能提供 operandValue 或 operandVariable。";
                    if ((operation.Operator == "divide" || operation.Operator == "modulo")
                        && operation.OperandValue == 0d)
                        return "variable.compute 的除数或求余操作数不能为0。";
                    return null!;
                case "wait":
                    if (!operation.Milliseconds.HasValue || operation.Milliseconds < 0 || operation.Milliseconds > 86400000)
                        return "wait.milliseconds 必须在0..86400000之间。";
                    return null!;
                case "flow.goto":
                    return operation.Target == null ? null! : ValidateTarget(operation.Target, "flow.goto.target");
                case "flow.end":
                    return null!;
                case "branch.number_range":
                    if (string.IsNullOrWhiteSpace(operation.Variable) || !operation.Min.HasValue || !operation.Max.HasValue)
                        return "branch.number_range 必须提供 variable/min/max。";
                    return operation.WhenTrue == null
                        ? operation.WhenFalse == null ? null! : ValidateTarget(operation.WhenFalse, "branch.number_range.whenFalse")
                        : ValidateTarget(operation.WhenTrue, "branch.number_range.whenTrue")
                            ?? (operation.WhenFalse == null ? null! : ValidateTarget(operation.WhenFalse, "branch.number_range.whenFalse"));
                case "branch.number_compare":
                    if (string.IsNullOrWhiteSpace(operation.Variable)
                        || string.IsNullOrWhiteSpace(operation.Comparison)
                        || !operation.CompareValue.HasValue)
                        return "branch.number_compare 必须提供 variable/comparison/compareValue。";
                    if (!new[] { "gt", "gte", "lt", "lte", "eq", "ne" }
                        .Contains(operation.Comparison, StringComparer.Ordinal))
                        return "branch.number_compare.comparison 只能是 gt/gte/lt/lte/eq/ne。";
                    return operation.WhenTrue == null
                        ? operation.WhenFalse == null ? null! : ValidateTarget(operation.WhenFalse, "branch.number_compare.whenFalse")
                        : ValidateTarget(operation.WhenTrue, "branch.number_compare.whenTrue")
                            ?? (operation.WhenFalse == null ? null! : ValidateTarget(operation.WhenFalse, "branch.number_compare.whenFalse"));
                case "branch.io":
                    string branchConditionError = ValidateIoConditions(operation.Conditions, "branch.io.conditions");
                    if (branchConditionError != null) return branchConditionError;
                    if (!string.IsNullOrWhiteSpace(operation.ConditionLogic)
                        && !new[] { "all", "any" }.Contains(operation.ConditionLogic, StringComparer.Ordinal))
                        return "branch.io.conditionLogic 只能是 all 或 any。";
                    return operation.WhenTrue == null
                        ? operation.WhenFalse == null ? null! : ValidateTarget(operation.WhenFalse, "branch.io.whenFalse")
                        : ValidateTarget(operation.WhenTrue, "branch.io.whenTrue")
                            ?? (operation.WhenFalse == null ? null! : ValidateTarget(operation.WhenFalse, "branch.io.whenFalse"));
                case "popup.message":
                    if (string.IsNullOrWhiteSpace(operation.Message)) return "popup.message.message 不能为空。";
                    if (ContainsPlaceholderSyntax(operation.Message))
                        return "popup.message 是固定文本，不支持 {变量名} 插值；显示变量当前值请使用 popup.variable。";
                    return operation.Target == null ? null! : ValidateTarget(operation.Target, "popup.message.target");
                case "popup.variable":
                    if (string.IsNullOrWhiteSpace(operation.Variable)) return "popup.variable.variable 不能为空。";
                    return operation.Target == null ? null! : ValidateTarget(operation.Target, "popup.variable.target");
                case "config.placeholder":
                    return string.IsNullOrWhiteSpace(operation.Message)
                        ? "config.placeholder.message 不能为空。"
                        : null!;
                case "io.write":
                    return ValidateIoOutputs(operation.Outputs, "io.write.outputs");
                case "io.wait":
                    string waitConditionError = ValidateIoConditions(operation.Conditions, "io.wait.conditions");
                    if (waitConditionError != null) return waitConditionError;
                    if (!operation.TimeoutMs.HasValue || operation.TimeoutMs < 1 || operation.TimeoutMs > 86400000)
                        return "io.wait.timeoutMs 必须在1..86400000之间。";
                    return operation.OnFailure == null
                        ? null!
                        : ValidateTarget(operation.OnFailure, "io.wait.onFailure");
                case "process.control":
                    return null!;
                case "process.wait":
                    return null!;
                case "native.operation":
                    if (string.IsNullOrWhiteSpace(operation.OperaType) || operation.Fields == null)
                        return "native.operation 必须提供精确 operaType 和 fields 对象。";
                    return null!;
                default:
                    return $"不支持的语义指令：{operation.Kind}。支持的 kind：{SemanticOperationKinds.SupportedKinds}；原生类型按需读取 get_native_operation_schemas。";
            }
        }

        private static string ValidateTarget(OperationTarget target, string path)
        {
            if (target == null) return $"{path} 必填。";
            int selectorCount = (!string.IsNullOrWhiteSpace(target.OperationId) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(target.OperationKey) ? 1 : 0);
            if (selectorCount != 1)
                return $"{path} 必须且只能使用 operationId 或 operationKey。";
            int stepSelectorCount = (!string.IsNullOrWhiteSpace(target.StepId) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(target.StepKey) ? 1 : 0);
            if (!string.IsNullOrWhiteSpace(target.OperationId) && stepSelectorCount != 0)
                return $"{path} 使用 operationId 时不得提供 stepId 或 stepKey。";
            if (!string.IsNullOrWhiteSpace(target.OperationKey) && stepSelectorCount > 1)
                return $"{path} 使用 operationKey 时 stepId 和 stepKey 不能同时提供。";
            return null!;
        }

        private static string ValidateIoConditions(IReadOnlyCollection<IoStateCondition> conditions, string path)
        {
            if (conditions == null || conditions.Count == 0)
                return $"{path} 至少包含一个IO条件。";
            var names = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (IoStateCondition condition in conditions)
            {
                if (condition == null || string.IsNullOrWhiteSpace(condition.Io) || !condition.State.HasValue)
                    return $"{path}[{index}] 必须提供 io/state。";
                if (!names.Add(condition.Io.Trim()))
                    return $"{path} 包含重复IO：{condition.Io.Trim()}。";
                index++;
            }
            return null!;
        }

        private static string ValidateIoOutputs(IReadOnlyCollection<IoOutputState> outputs, string path)
        {
            if (outputs == null || outputs.Count == 0)
                return $"{path} 至少包含一个输出IO。";
            var names = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (IoOutputState output in outputs)
            {
                if (output == null || string.IsNullOrWhiteSpace(output.Io) || !output.State.HasValue)
                    return $"{path}[{index}] 必须提供 io/state。";
                if (!names.Add(output.Io.Trim()))
                    return $"{path} 包含重复IO：{output.Io.Trim()}。";
                index++;
            }
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
