using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automation.McpServer
{
    public sealed class CreateProcBatchDefinition
    {
        [JsonPropertyName("name"), Description("流程名称，不能与现有流程重复，最长64字符")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("autoStart"), Description("是否自启动，默认false")]
        public bool? AutoStart { get; set; }

        [JsonPropertyName("disable"), Description("是否禁用，默认false")]
        public bool? Disable { get; set; }

        [JsonPropertyName("steps"), Description("步骤数组，必须包含1到10个步骤")]
        public List<CreateProcBatchStep> Steps { get; set; } = new List<CreateProcBatchStep>();
    }

    public sealed class CreateProcBatchStep
    {
        [JsonPropertyName("name"), Description("步骤名称，最长64字符")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("disable"), Description("是否禁用，默认false")]
        public bool? Disable { get; set; }

        [JsonPropertyName("operations"), Description("指令数组；单步骤和单次变更均最多20条")]
        public List<CreateProcBatchOperation> Operations { get; set; } = new List<CreateProcBatchOperation>();
    }

    public sealed class CreateProcBatchOperation
    {
        [JsonPropertyName("operaType"), Description("精确指令类型，创建前读取对应指令Schema")]
        public string OperaType { get; set; } = string.Empty;

        [JsonPropertyName("fieldValues"), Description("可选字段初值对象；键和值严格匹配指令Schema，最大8KB")]
        public Dictionary<string, JsonElement>? FieldValues { get; set; }
    }

    internal static class CreateProcBatchDefinitionValidator
    {
        private const int MaxDefinitionBytes = 64 * 1024;
        private const int MaxFieldValuesBytes = 8 * 1024;
        private const int MaxSteps = 10;
        private const int MaxOperations = 20;
        private const int MaxNameLength = 64;

        public static string? Validate(CreateProcBatchDefinition definition)
        {
            if (definition == null) return "definition 必须是对象。";
            if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > MaxNameLength)
                return $"definition.name 必填且最长{MaxNameLength}字符。";
            if (definition.Steps == null || definition.Steps.Count < 1 || definition.Steps.Count > MaxSteps)
                return $"definition.steps 数量必须在1..{MaxSteps}之间。";

            int operationCount = 0;
            for (int stepIndex = 0; stepIndex < definition.Steps.Count; stepIndex++)
            {
                CreateProcBatchStep step = definition.Steps[stepIndex];
                if (step == null) return $"definition.steps[{stepIndex}] 必须是对象。";
                if (string.IsNullOrWhiteSpace(step.Name) || step.Name.Length > MaxNameLength)
                    return $"definition.steps[{stepIndex}].name 必填且最长{MaxNameLength}字符。";
                if (step.Operations == null || step.Operations.Count > MaxOperations)
                    return $"definition.steps[{stepIndex}].operations 最多{MaxOperations}条。";
                operationCount += step.Operations.Count;
                if (operationCount > MaxOperations) return $"definition 单次累计最多{MaxOperations}条指令。";

                for (int operationIndex = 0; operationIndex < step.Operations.Count; operationIndex++)
                {
                    CreateProcBatchOperation operation = step.Operations[operationIndex];
                    if (operation == null) return $"definition.steps[{stepIndex}].operations[{operationIndex}] 必须是对象。";
                    if (string.IsNullOrWhiteSpace(operation.OperaType))
                        return $"definition.steps[{stepIndex}].operations[{operationIndex}].operaType 必填。";
                    if (operation.FieldValues != null
                        && Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(operation.FieldValues)) > MaxFieldValuesBytes)
                        return $"definition.steps[{stepIndex}].operations[{operationIndex}].fieldValues 超过{MaxFieldValuesBytes / 1024}KB。";
                }
            }

            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(definition)) > MaxDefinitionBytes)
                return $"definition 超过{MaxDefinitionBytes / 1024}KB。";
            return null;
        }
    }
}
