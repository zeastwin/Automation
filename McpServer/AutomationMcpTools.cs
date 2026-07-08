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
            "列出本地可用的中间意图 JSON 模板。模型在生成写入意图前应先读取模板，而不是依赖提示词中的长示例。")]
        public static async Task<string> ListIntentTemplates(
            [Description("可选 Patch 动作名过滤，例如 update_operation_fields。")] string? patchAction = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListIntentTemplates),
                args: new { patchAction },
                action: client => client.ListIntentTemplatesAsync(patchAction)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取单个中间意图 JSON 模板。可按 templateId 精确读取，也可按 patchAction 获取对应模板。")]
        public static async Task<string> GetIntentTemplate(
            [Description("模板 ID，例如 update_operation_field。")] string? templateId = null,
            [Description("Patch 动作名，例如 update_operation_fields。")] string? patchAction = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetIntentTemplate),
                args: new { templateId, patchAction },
                action: client => client.GetIntentTemplateAsync(templateId, patchAction)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "把中间意图 JSON 转换成标准 patchJson。适合先用模板约束输出，再由本地统一组装 Patch。")]
        public static async Task<string> BuildPatchFromIntent(
            [Description("中间意图 JSON。必须符合 get_intent_template 返回的 intentShape。")] string intentJson)
        {
            return await ExecuteAsync(
                toolName: nameof(BuildPatchFromIntent),
                args: new { intentJson },
                action: client => client.BuildPatchFromIntentAsync(intentJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "使用中间意图 JSON 直接预演，不需要模型自行组装 patchJson。内部会先把意图转换成标准 Patch，再执行 preview。")]
        public static async Task<string> PreviewIntent(
            [Description("中间意图 JSON。必须符合 get_intent_template 返回的 intentShape。")] string intentJson)
        {
            return await ExecuteAsync(
                toolName: nameof(PreviewIntent),
                args: new { intentJson },
                action: client => client.PreviewIntentAsync(intentJson)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "使用中间意图 JSON 直接提交。必须携带 Automation 前台已确认的 previewId，且意图内容必须与预演时完全一致。")]
        public static async Task<string> ApplyIntent(
            [Description("中间意图 JSON。必须与预演通过的意图保持一致。")] string intentJson,
            [Description("Automation 前台确认预演后返回或提示的 previewId。")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(ApplyIntent),
                args: new { intentJson, previewId },
                action: client => client.ApplyIntentAsync(intentJson, previewId)).ConfigureAwait(false);
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
            "应用结构化 Patch，触发保存与发布。必须携带 Automation 前台已确认的 previewId，且 patchJson 必须与预演时完全一致。")]
        public static async Task<string> ApplyPatch(
            [Description("结构化 Patch JSON。必须与预演通过的 patch 保持一致。")] string patchJson,
            [Description("Automation 前台确认预演后返回或提示的 previewId。")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(ApplyPatch),
                args: new { patchJson, previewId },
                action: client => client.ApplyPatchAsync(patchJson, previewId)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取 Automation 当前运行快照，包括流程状态、当前位置、报警状态、选中项和安全锁定状态。")]
        public static async Task<string> GetRuntimeSnapshot(
            [Description("可选流程索引。从 0 开始；为空时返回全部流程快照。")] int? procIndex = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetRuntimeSnapshot),
                args: new { procIndex },
                action: client => client.GetRuntimeSnapshotAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "读取 Automation 运行信息页最近日志。用于排查报警、Bridge/MCP 调用失败和流程运行异常。")]
        public static async Task<string> GetInfoLogTail(
            [Description("返回条数，范围 1..200。")] int maxCount = 50)
        {
            return await ExecuteAsync(
                toolName: nameof(GetInfoLogTail),
                args: new { maxCount },
                action: client => client.GetInfoLogTailAsync(maxCount)).ConfigureAwait(false);
        }

        [McpServerTool, Description(
            "诊断指定流程的结构与运行风险，包括禁用、空步骤/指令、未知指令类型、跳转错误、报警和断点。")]
        public static async Task<string> DiagnoseProc(
            [Description("流程索引，从 0 开始。")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(DiagnoseProc),
                args: new { procIndex },
                action: client => client.DiagnoseProcAsync(procIndex)).ConfigureAwait(false);
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
    "4. list_intent_templates / get_intent_template 读取中间意图模板",
    "5. preview_intent 预演，或 build_patch_from_intent 后再 preview_patch",
    "6. Automation 前台确认 previewId 后，apply_intent 或 apply_patch 携带同一个 previewId 提交"
  ],
  "preferredWritePath": [
    "优先使用中间意图：get_intent_template -> preview_intent -> 等待 Automation 确认 previewId -> apply_intent",
    "仅在已经有标准 patchJson 时再直接调用 preview_patch -> 等待 Automation 确认 previewId -> apply_patch"
  ],
  "supportedActions": [
    "update_proc_head_fields",
    "update_step_fields",
    "update_operation_fields",
    "append_step",
    "insert_step",
    "delete_step",
    "move_step",
    "append_operation",
    "insert_operation",
    "delete_operation",
    "move_operation"
  ],
  "patchShape": {
    "procIndex": 0,
    "baseProcId": "guid",
    "actions": [
      {
        "type": "move_operation",
        "stepId": "guid",
        "opId": "guid",
        "targetStepId": "guid",
        "targetIndex": 2,
        "expectedOperaType": "IO检测"
      },
      {
        "type": "insert_operation",
        "stepId": "guid",
        "insertIndex": 3,
        "operaType": "IO检测",
        "fieldValues": {
          "timeOutC_TimeOut": 5000
        }
      }
    ]
  },
  "rules": [
    "优先使用中间意图工具，减少模型直接拼装 patchJson 的自由度",
    "不要直接改原始流程 JSON 文件",
    "不要假设流程名、步骤名、指令名唯一",
    "字段名必须使用 get_proc_detail.fields 或 get_operation_schema.fields.key 返回的精确键名",
    "不要在未读取 schema 的情况下猜字段名或枚举值",
    "actions[] 中的动作键必须使用 type，禁止使用旧字段 action",
    "update_*_fields 必须使用 fieldChanges，insert/append_operation 需要初始字段时必须使用 fieldValues，禁止使用旧字段 fields",
    "apply_patch 必须复用原始 patch，不能把 preview_patch 返回结果里的 changes 直接当作 actions 提交",
    "preview_intent/preview_patch 会返回 previewId 和 patchHash，提交必须携带 Automation 前台已确认的 previewId",
    "未预演、预演未确认、Patch 与预演不一致、流程版本变化都会导致 apply 失败",
    "delete/move/insert 会触发 Automation Bridge 自动重写同流程内的跳转地址",
    "move_step/move_operation 的 targetIndex 表示移除源项后的最终索引",
    "apply_patch 前必须先调用 preview_patch 并等待 Automation 前台确认"
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
