using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Automation.McpServer
{
    [McpServerToolType]
    public static class AutomationMcpTools
    {
        [McpServerTool, Description(
            "列出 Automation 当前全部流程的基础信息。用于意图定位的第一步；不要假设流程名称唯一。")]
        public static async Task<string> ListProcs(
            [Description("是否附带步骤级摘要。true 会返回更多定位信息，但响应更长。")] bool includeStepSummary = false)
        {
            return await ExecuteAsync(
                toolName: nameof(ListProcs),
                args: new { includeStepSummary },
                action: client => client.ListProcsAsync(includeStepSummary)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取指定流程的摘要视图，返回步骤、指令、摘要信息与稳定标识。适合在细化修改目标前使用。")]
        public static async Task<string> GetProcOverview(
            [Description("流程索引，从 0 开始。")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetProcOverview),
                args: new { procIndex },
                action: client => client.GetProcOverviewAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取指定流程的完整详情视图，返回 head、steps、ops、fields 与稳定标识。修改前必须先读取。")]
        public static async Task<string> GetProcDetail(
            [Description("流程索引，从 0 开始。")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetProcDetail),
                args: new { procIndex },
                action: client => client.GetProcDetailAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "列出 Automation 当前支持的全部指令类型。插入新指令前应先调用。")]
        public static async Task<string> ListOperationTypes()
        {
            return await ExecuteAsync(
                toolName: nameof(ListOperationTypes),
                args: new { },
                action: client => client.ListOperationTypesAsync()).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取指令可编辑 Schema。优先按现有指令实例定位；新建指令时可只传 operaType。不要在未读取 Schema 的情况下猜字段名或枚举值。")]
        public static async Task<string> GetOperationSchema(
            [Description("流程索引。读取现有指令实例时必填；按指令类型查询时可为空。")] int? procIndex = null,
            [Description("步骤 ID。读取现有指令实例时必填；按指令类型查询时可为空。")] string? stepId = null,
            [Description("指令 ID。读取现有指令实例时必填；按指令类型查询时可为空。")] string? opId = null,
            [Description("指令类型。新建指令前按类型查询时必填，必须使用 list_operation_types 返回的 operaType，例如 IO检测。")] string? operaType = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetOperationSchema),
                args: new { procIndex, stepId, opId, operaType },
                action: client => client.GetOperationSchemaAsync(procIndex, stepId, opId, operaType)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取当前可引用对象目录，例如变量、IO、工站、通讯、PLC、报警编号等。修改引用字段前应先读取。")]
        public static async Task<string> GetReferenceCatalog(
            [Description("可选流程索引。某些目录可能按流程裁剪。")] int? procIndex = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetReferenceCatalog),
                args: new { procIndex },
                action: client => client.GetReferenceCatalogAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "预演结构化 Patch，不会落盘。patchJson 必须是完整 JSON 对象，至少包含 procIndex、baseProcId、actions。提交前必须先调用。")]
        public static async Task<string> PreviewPatch(
            [Description("结构化 Patch JSON。示例：{\"procIndex\":0,\"baseProcId\":\"guid\",\"actions\":[...]}")] string patchJson)
        {
            return await ExecuteAsync(
                toolName: nameof(PreviewPatch),
                args: new { patchJson },
                action: client => client.PreviewPatchAsync(patchJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "应用结构化 Patch，触发保存与发布。只接受与 preview_patch 相同的完整 Patch JSON。调用前必须确认 preview 已通过。")]
        public static async Task<string> ApplyPatch(
            [Description("结构化 Patch JSON。必须与预演通过的 patch 保持一致。")] string patchJson)
        {
            return await ExecuteAsync(
                toolName: nameof(ApplyPatch),
                args: new { patchJson },
                action: client => client.ApplyPatchAsync(patchJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "返回 Automation MCP 的调用约束与 Patch 示例，用于提示 LLM 先读后改、先预演后提交。")]
        public static string GetPatchContract()
        {
            const string contract = """
{
  "workflow": [
    "1. list_procs 或 get_proc_overview 定位目标流程",
    "2. get_proc_detail 读取完整结构",
    "3. get_operation_schema / get_reference_catalog 获取字段约束与候选值",
    "4. preview_patch 预演",
    "5. apply_patch 提交"
  ],
  "supportedActions": [
    "update_proc_head_fields",
    "update_step_fields",
    "update_operation_fields",
    "append_step",
    "append_operation"
  ],
  "patchShape": {
    "procIndex": 0,
    "baseProcId": "guid",
    "actions": [
      {
        "type": "update_operation_fields",
        "stepId": "guid",
        "opId": "guid",
        "expectedOperaType": "IO检测",
        "fieldChanges": {
          "timeOutC_TimeOut": 5000
        }
      }
    ]
  },
  "rules": [
    "不要直接改原始流程 JSON 文件",
    "不要假设流程名、步骤名、指令名唯一",
    "字段名必须使用 get_proc_detail.fields 或 get_operation_schema.fields.key 返回的精确键名",
    "不要在未读取 schema 的情况下猜字段名或枚举值",
    "apply_patch 前必须先调用 preview_patch"
  ]
}
""";
            ToolCallLogger.Log(nameof(GetPatchContract), new { }, contract);
            return contract;
        }

        private static async Task<string> ExecuteAsync(string toolName, object args, Func<AutomationBridgeClient, Task<string>> action)
        {
            try
            {
                string result = await action(AutomationMcpRuntime.GetBridgeClient()).ConfigureAwait(false);
                ToolCallLogger.Log(toolName, args, result);
                return result;
            }
            catch (Exception ex)
            {
                string error = $$"""
{"ok":false,"type":"mcp.error","message":"{{toolName}} 调用异常","details":"{{Escape(ex.Message)}}"}
""";
                ToolCallLogger.Log(toolName, args, string.Empty, ex.Message);
                return error;
            }
        }

        private static string Escape(string text)
        {
            return (text ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
        }
    }
}
