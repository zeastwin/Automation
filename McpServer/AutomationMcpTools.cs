using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Automation.McpServer
{
    [McpServerToolType]
    public static class AutomationMcpTools
    {
        [McpServerTool(Name = "list_procs"), Description(
            "列出所有流程的基础信息（procIndex/procId/name/autoStart/disable/state/stepCount）。"
            + "意图定位第一步，不要假设流程名唯一。"
            + "用户口语\"N号流程\"即 procIndex=N（索引从0开始，\"3号流程\"=procIndex=3，不是第3个流程）。"
            + "可选返回每个步骤的摘要（stepId/name/disable/opCount）。")]
        public static async Task<string> ListProcs(
            [Description("是否包含每个步骤的摘要信息，默认 false")] bool? includeStepSummary = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListProcs),
                args: new { includeStepSummary },
                action: client => client.ListProcsAsync(includeStepSummary)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "search_proc_catalog"), Description(
            "分页搜索流程目录，只返回轻量摘要。适合数百或数千流程时先定位目标；默认50条、最多100条，不读取指令详情。")]
        public static async Task<string> SearchProcCatalog(
            [Description("流程名称或流程编号关键词")] string? keyword = null,
            [Description("分页起点，默认0")] int? offset = null,
            [Description("每页数量1..100，默认50")] int? limit = null,
            [Description("是否附带步骤摘要；大量流程时建议false")] bool? includeStepSummary = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchProcCatalog),
                args: new { keyword, offset, limit, includeStepSummary },
                action: client => client.SearchProcCatalogAsync(keyword, offset, limit, includeStepSummary)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_proc_overview"), Description(
            "读取流程摘要视图（步骤/指令/摘要/稳定标识）。"
            + "比 get_proc_detail 轻量，适合快速了解流程结构。")]
        public static async Task<string> GetProcOverview(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetProcOverview),
                args: new { procIndex },
                action: client => client.GetProcOverviewAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_proc_detail"), Description(
            "读取流程完整详情（head/steps/ops/fields，含 isJump/flow/gotoWarnings）。"
            + "修改流程前必须先调用本工具。返回的 flow 字段标注每条指令执行后的流向（opIndex+1 或跳转目标），"
            + "gotoWarnings 列出越界的跳转目标。")]
        public static async Task<string> GetProcDetail(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetProcDetail),
                args: new { procIndex },
                action: client => client.GetProcDetailAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_op_detail"), Description(
            "读取单条指令详情（字段值/执行流向 flow/跳转有效性 gotoIssues）。"
            + "用于细粒度检查某条指令的字段配置和跳转目标。")]
        public static async Task<string> GetOpDetail(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex,
            [Description("步骤索引")] int stepIndex,
            [Description("指令索引")] int opIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetOpDetail),
                args: new { procIndex, stepIndex, opIndex },
                action: client => client.GetOpDetailAsync(procIndex, stepIndex, opIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_step_detail"), Description(
            "读取单步骤完整指令列表（含每条指令 flow）。"
            + "用于查看某个步骤下所有指令的执行流向。")]
        public static async Task<string> GetStepDetail(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex,
            [Description("步骤索引")] int stepIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetStepDetail),
                args: new { procIndex, stepIndex },
                action: client => client.GetStepDetailAsync(procIndex, stepIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "search_ops"), Description(
            "按条件搜索指令。procIndex 为空搜全部流程，最多 200 条；keyword 匹配指令名和字段摘要。"
            + "用于跨流程查找特定类型的指令。")]
        public static async Task<string> SearchOps(
            [Description("流程索引，为空则搜索全部流程")] int? procIndex = null,
            [Description("指令类型过滤，如 IO检测/延时")] string? operaType = null,
            [Description("关键词，匹配指令名和字段摘要")] string? keyword = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchOps),
                args: new { procIndex, operaType, keyword },
                action: client => client.SearchOpsAsync(procIndex, operaType, keyword)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "trace_resource"), Description(
            "按项目中的业务资源名称自动识别类型并追踪使用位置。优先用于“这个变量/IO/工站/PLC/数据结构/报警在哪里用过”，无需记referenceType；名称同时属于多类资源时会同时查询并标记ambiguous。")]
        public static async Task<string> TraceResource(
            [Description("资源精确名称；报警使用编号文本，例如12")] string name,
            [Description("可选类型:auto/variable/io/station/plc/dataStruct/alarm，默认auto")] string? resourceKind = null,
            [Description("流程扫描起点，默认0")] int? procOffset = null,
            [Description("本批扫描流程数1..50，默认20")] int? procLimit = null,
            [Description("本批最多返回命中数1..100，默认50")] int? resultLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(TraceResource),
                args: new { name, resourceKind, procOffset, procLimit, resultLimit },
                action: client => client.TraceResourceAsync(name, resourceKind, procOffset, procLimit, resultLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "search_operation_fields"), Description(
            "在指令的全部可见字段中分页搜索文本或精确值，适合查自定义字符串、表达式、备注及未声明为资源引用的历史字段。可按字段名和指令类型收窄，返回精确位置，不返回流程全文。")]
        public static async Task<string> SearchOperationFields(
            [Description("搜索内容，最长200字符")] string query,
            [Description("contains或exact，默认contains")] string? matchMode = null,
            [Description("可选精确字段名，如AlarmInfoID、Goto1")] string? fieldName = null,
            [Description("可选精确指令类型")] string? operaType = null,
            [Description("流程扫描起点，默认0")] int? procOffset = null,
            [Description("本批扫描流程数1..50，默认20")] int? procLimit = null,
            [Description("本批最多返回命中数1..100，默认50")] int? resultLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchOperationFields),
                args: new { query, matchMode, fieldName, operaType, procOffset, procLimit, resultLimit },
                action: client => client.SearchOperationFieldsAsync(query, matchMode, fieldName, operaType,
                    procOffset, procLimit, resultLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "find_references"), Description(
            "跨流程精确查找资源引用。用于回答“哪些流程使用了这个变量/IO/报警/工站”。按流程分批扫描，禁止全量返回。referenceType常用值：value、io.input、io.output、io.all、alarm.infoId、station、plc.device、dataStruct、proc。返回精确到流程/步骤/指令/字段的位置和下一批游标。")]
        public static async Task<string> FindReferences(
            [Description("引用类型，例如变量使用value")] string referenceType,
            [Description("要查找的引用值，必须精确匹配，例如变量名")] string value,
            [Description("可选：只检查指定字段名")] string? fieldName = null,
            [Description("流程扫描起点，默认0；继续扫描时使用上次nextProcOffset")] int? procOffset = null,
            [Description("本批扫描流程数1..50，默认20")] int? procLimit = null,
            [Description("本批最多返回命中数1..100，默认50")] int? resultLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(FindReferences),
                args: new { referenceType, value, fieldName, procOffset, procLimit, resultLimit },
                action: client => client.FindReferencesAsync(referenceType, value, fieldName, procOffset, procLimit, resultLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "find_variable_usages"), Description(
            "分页查找哪些流程、步骤、指令和字段使用了指定变量。只做精确变量引用匹配，不返回流程全文；使用nextProcOffset继续扫描后续流程。")]
        public static async Task<string> FindVariableUsages(
            [Description("变量名称，必须使用变量表中的精确名称")] string variableName,
            [Description("流程扫描起点，默认0")] int? procOffset = null,
            [Description("本批扫描流程数1..50，默认20")] int? procLimit = null,
            [Description("本批最多返回命中数1..100，默认50")] int? resultLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(FindVariableUsages),
                args: new { variableName, procOffset, procLimit, resultLimit },
                action: client => client.FindReferencesAsync("value", variableName, null,
                    procOffset, procLimit, resultLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_operation_context"), Description(
            "读取故障指令附近的小窗口。只返回目标指令完整字段，邻近指令仅返回摘要，适合排查顺序执行、旁路和跳转问题，避免读取整个流程。")]
        public static async Task<string> GetOperationContext(
            [Description("流程索引")] int procIndex,
            [Description("步骤索引")] int stepIndex,
            [Description("目标指令索引")] int opIndex,
            [Description("前后指令数量0..10，默认2")] int? radius = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetOperationContext),
                args: new { procIndex, stepIndex, opIndex, radius },
                action: client => client.GetOperationContextAsync(procIndex, stepIndex, opIndex, radius)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "audit_proc_batch"), Description(
            "分批体检大量流程，只返回问题位置，不返回流程全文。检查空流程、空步骤、禁用步骤/指令、空指令类型和无效跳转。使用nextProcOffset继续下一批。")]
        public static async Task<string> AuditProcBatch(
            [Description("流程扫描起点，默认0")] int? procOffset = null,
            [Description("本批扫描流程数1..50，默认20")] int? procLimit = null,
            [Description("本批最多返回问题数1..200，默认100")] int? findingLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(AuditProcBatch),
                args: new { procOffset, procLimit, findingLimit },
                action: client => client.AuditProcBatchAsync(procOffset, procLimit, findingLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "analyze_flow_graph"), Description(
            "分析单个流程的执行流语义，不返回流程全文。检查无效跳转、无条件跳转后的疑似死代码，以及报警策略与AlarmInfoID/Goto1/2/3不匹配。")]
        public static async Task<string> AnalyzeFlowGraph(
            [Description("流程索引")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(AnalyzeFlowGraph),
                args: new { procIndex },
                action: client => client.AnalyzeFlowAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "diagnose_issue"), Description(
            "根据现场症状和流程位置生成有上限的诊断证据包，自动组合运行快照、目标前后指令和执行流问题。只读，不修改配置。")]
        public static async Task<string> DiagnoseIssue(
            [Description("流程索引")] int procIndex,
            [Description("现场症状，最长300字符")] string? symptom = null,
            [Description("可选步骤索引；为空时使用运行快照当前位置")] int? stepIndex = null,
            [Description("可选指令索引；为空时使用运行快照当前位置")] int? opIndex = null)
        {
            return await ExecuteAsync(
                toolName: nameof(DiagnoseIssue),
                args: new { procIndex, symptom, stepIndex, opIndex },
                action: client => client.DiagnoseIssueAsync(procIndex, symptom, stepIndex, opIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_snapshot"), Description(
            "读取运行快照（流程状态/当前位置/报警/安全锁定）。"
            + "procIndex 为空返回全部流程快照。用于了解当前运行状态。"
            + "publishedRevision是最近发布版本，appliedRevision是运行线程已应用版本；hasPendingUpdate=true表示仍在等待安全指令边界。")]
        public static async Task<string> GetSnapshot(
            [Description("流程索引，为空返回全部流程快照")] int? procIndex = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetSnapshot),
                args: new { procIndex },
                action: client => client.GetSnapshotAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_operation_types"), Description(
            "列出平台实际注册的指令类型和名称。仅在用户或源数据未给出精确指令类型、需要发现可用类型时调用；已知operaType时直接调用get_operation_schema。")]
        public static async Task<string> ListOperationTypes()
        {
            return await ExecuteAsync(
                toolName: nameof(ListOperationTypes),
                args: new { },
                action: client => client.OpMetaAsync("list_types", new JsonObject())).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_operation_schema"), Description(
            "结构化读取单个指令类型或现有指令实例的字段Schema。无需先读取完整指令类型目录。新建时传已知operaType；编辑实例时传procIndex、stepId、opId。")]
        public static async Task<string> GetOperationSchema(
            [Description("新建指令时传精确指令类型")] string? operaType = null,
            [Description("读取现有实例时传流程索引")] int? procIndex = null,
            [Description("读取现有实例时传真实stepId")] string? stepId = null,
            [Description("读取现有实例时传真实opId")] string? opId = null)
        {
            var parameters = new JsonObject();
            if (!string.IsNullOrEmpty(operaType)) parameters["operaType"] = operaType;
            if (procIndex.HasValue) parameters["procIndex"] = procIndex.Value;
            if (!string.IsNullOrEmpty(stepId)) parameters["stepId"] = stepId;
            if (!string.IsNullOrEmpty(opId)) parameters["opId"] = opId;
            return await ExecuteAsync(
                toolName: nameof(GetOperationSchema),
                args: new { operaType, procIndex, stepId, opId },
                action: client => client.OpMetaAsync("schema", parameters)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_patch_action_schema"), Description(
            "按单个Patch动作类型返回所需字段和安全约束，替代一次返回整份patch_contract。")]
        public static Task<string> GetPatchActionSchema(
            [Description("动作类型：update_proc_head_fields/update_step_fields/update_operation_fields/append_step/insert_step/delete_step/move_step/append_operation/insert_operation/delete_operation/move_operation")] string actionType)
        {
            string result = BuildPatchActionSchema(actionType);
            ToolCallLogger.Log(nameof(GetPatchActionSchema), new { actionType }, result);
            return Task.FromResult(result);
        }

        [McpServerTool(Name = "get_operation_guide"), Description(
            "按精确operaType读取单个指令类型的用途、约束和常见误用。")]
        public static async Task<string> GetOperationGuide(
            [Description("精确指令类型，例如IO检测、逻辑判断、工站运行")] string operaType)
        {
            if (!UsesBehaviorContractGuide(operaType))
            {
                string legacyResult = GetOperationGuideByType(operaType);
                ToolCallLogger.Log(nameof(GetOperationGuide), new { operaType }, legacyResult);
                return legacyResult;
            }
            var parameters = new JsonObject { ["operaType"] = operaType };
            return await ExecuteAsync(
                toolName: nameof(GetOperationGuide),
                args: new { operaType },
                action: client => client.OpMetaAsync("guide", parameters)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "op_meta"), Description(
            "指令元信息统一工具。通过 action 指定具体操作，通过 parameters 传 JSON 参数。" +
            "\naction 可选值及对应 parameters 字段：" +
            "\n- list_types: 仅在指令类型未知时列出全部指令类型。parameters: 无。" +
            "\n- schema: 读取指令可编辑 Schema（优先按现有指令实例定位；新建指令时可只传 operaType；不要在未读 Schema 情况下猜字段名/枚举值）。parameters: {procIndex:int?, stepId:string?, opId:string?, operaType:string?}。" +
            "\n- guide: 按精确operaType读取单个指令类型的用途、关键字段、约束和常见误用。parameters: {operaType:string}。" +
            "\n- reference_catalog: 仅在当前Schema包含资源引用字段且候选值未知时，读取变量/IO/工站/通讯/PLC/报警编号等目录。parameters: {procIndex:int?}。" +
            "\n示例：op_meta(action=\"schema\", parameters=\"{\\\"operaType\\\":\\\"IO检测\\\"}\")")]
        public static async Task<string> OpMeta(
            [Description("操作类型：list_types/schema/guide/reference_catalog")] string action,
            [Description("参数 JSON 字符串，不同 action 需要不同参数，详见工具描述")] string? parameters = null)
        {
            if (action == "guide")
            {
                JsonObject parsed = ParseParameters(parameters);
                string? operaType = parsed["operaType"]?.GetValue<string>();
                if (!UsesBehaviorContractGuide(operaType))
                {
                    return GetOperationGuideByType(operaType);
                }
            }
            return await ExecuteAsync(
                toolName: nameof(OpMeta),
                args: new { action, parameters },
                action: client => client.OpMetaAsync(action, ParseParameters(parameters))).ConfigureAwait(false);
        }

        private static bool UsesBehaviorContractGuide(string? operaType)
        {
            switch (operaType?.Trim())
            {
                case "逻辑判断":
                case "IO逻辑跳转":
                case "跳转":
                case "弹框":
                    return true;
                default:
                    return false;
            }
        }

        // 43 种指令类型的调用说明，基于执行代码（ProcessEngine.Operations.*.cs）和字段定义（OperationType.cs）编写。
        // AI 仅在当前指令语义或约束不明确时按类型读取本指南，避免把全部模块送入上下文。
        private const string OperationGuideJson = """
{
  "_流程编号语义": {
    "purpose": "明确用户口语中\"N号流程\"与 procIndex 的对应关系，避免定位偏差",
    "keyFields": {
      "规则": "用户口语中的\"N号流程\"即 procIndex=N，索引从0开始",
      "示例": "\"3号流程\" = procIndex=3（不是第3个流程procIndex=2）",
      "list_procs返回": "返回列表中的 procIndex 字段就是用户口中的\"流程号\"，直接对应"
    },
    "constraints": "不要把\"N号流程\"理解为\"第N个流程\"从而误算为 procIndex=N-1；N号就是 procIndex=N",
    "commonMistakes": "常见错误：把\"3号流程\"理解为第3个流程(procIndex=2)，实际应是 procIndex=3；定位时务必以 list_procs 返回的 procIndex 为准，与用户口中的流程号一一对应"
  },
  "_流程级操作": {
    "purpose": "对整个流程的增删复制重排，区别于 patch 级操作（preview_patch/apply_patch 只能修改已有流程的步骤和指令，不能新增/删除整个流程）",
    "keyFields": {
      "create_proc": "新增空流程。工具：create_proc（不传 previewId 预演）→ create_proc（传 previewId 提交）。典型场景：新建流程来唤醒/启动其他流程、创建独立控制流程。新增后可通过 preview_patch 添加步骤和指令",
      "copy_proc": "复制现有流程为新流程（含全部步骤和指令）。工具：copy_proc（不传 previewId 预演）→ copy_proc（传 previewId 提交）。适合基于现有流程改造",
      "delete_procs": "批量删除流程。工具：delete_procs（不传 previewId 预演）→ delete_procs（传 previewId 提交）。提交要求全部流程Stopped，AI不得调用stop_proc",
      "reorder_proc": "重排流程位置。工具：reorder_proc（不传 previewId 预演）→ reorder_proc（传 previewId 提交）。提交要求全部流程Stopped，AI不得调用stop_proc"
    },
    "constraints": "四类流程级操作都需要 预演→确认 previewId→提交 三步；删除和重排要求全部流程Stopped，AI不得为提交而调用stop_proc；流程名不能与现有流程重复",
    "commonMistakes": "最常见错误：需要新增流程时误用 preview_patch——preview_patch 的 actions 只支持 update_proc_head_fields/update_step_fields/update_operation_fields/append_step/insert_step/delete_step/move_step/append_operation/insert_operation/delete_operation/move_operation，不支持新增整个流程。新增流程必须用 create_proc；若需基于现有流程改造，用 copy_proc 复制后再 preview_patch 修改"
  },
  "_通用字段说明": {
    "purpose": "所有指令继承自 OperationType 基类，以下字段对全部指令通用",
    "keyFields": {
      "Name": "指令显示名称，仅用于编辑识别与日志，不影响执行",
      "OperaType": "指令类型标识（只读），决定引擎执行分支，创建时由构造函数自动设置",
      "AlarmType": "异常处理策略，取值：报警停止/报警忽略/自动处理/弹框确定/弹框确定与否/弹框确定与否与取消",
      "AlarmInfoID": "报警信息库编号，仅 AlarmType 为弹框类时生效；字段类型是 string，必须按 JSON 字符串传，例如 \"0\"，不能传数字 0",
      "Goto1/Goto2/Goto3": "报警分支跳转目标，格式为 procIndex-stepIndex-opIndex（见_跳转编码说明）；AlarmType=自动处理仅用 Goto1；弹框确定与否用 Goto1/Goto2；弹框确定与否与取消用全部三个",
      "Disable": "true 时该指令被跳过执行",
      "isStopPoint": "true 时运行到该指令进入断点"
    },
    "constraints": "AlarmType 与 Goto1/2/3 联动：报警停止/报警忽略时不显示任何 Goto；自动处理仅 Goto1；弹框类按按钮数量显示。指令默认按 opIndex 顺序往下执行：执行完当前指令后自动执行下一条（opIndex+1），除非遇到跳转类指令（逻辑判断/IO逻辑跳转/跳转）改变了执行流。不写跳转指令就会默认往下执行。",
    "commonMistakes": "AlarmType 选了弹框类但未填 AlarmInfoID 或对应 Goto 会导致跳转失败；AlarmInfoID 看起来像编号但仍是 string 字段，写 0 会被 Bridge 拒绝，必须写 \"0\"；Goto 格式必须是 procIndex-stepIndex-opIndex 三段式；忽略默认顺序执行会导致旁路 bug——相邻的非跳转类指令会依次执行，分析流程时务必检查跳转目标执行完后是否会被后续指令旁路；通过 Patch 删除、插入或移动步骤/指令时 Bridge 会按目标指令 ID 自动重写同流程跳转，预演结果会报告重写数量；目标已被删除时必须根据预演提示明确修复"
  },
  "_跳转编码说明": {
    "purpose": "所有跳转类字段（Goto1/Goto2/Goto3、goto1/goto2、TrueGoto/FalseGoto、Goto、DefaultGoto、PopupGoto1/2/3）的统一编码格式",
    "keyFields": {
      "格式": "procIndex-stepIndex-opIndex，三段用连字符-分隔，全部必填",
      "示例": "3-0-1 表示流程索引3、步骤0、指令1",
      "procIndex约束": "必须等于当前流程索引，不支持跨流程跳转；不一致会报\"流程索引不一致\"",
      "空值行为": "跳转类指令（逻辑判断/IO逻辑跳转）的 goto 字段为空时报\"跳转位置为空\"并报警，不会自动跳到下一条"
    },
    "constraints": "编辑器通过下拉框选择有效目标，值由系统自动生成为 procIndex-stepIndex-opIndex 格式；AI 填写时必须包含三段，不可省略 procIndex",
    "commonMistakes": "常见错误：写成 stepIndex-opIndex（两段，缺 procIndex）会导致解析失败；把空 goto2 当作\"不跳转/正常结束\"会导致报警；跨流程填 procIndex 会导致\"流程索引不一致\""
  },
  "自定义方法": {
    "purpose": "调用预先在自定义函数库中注册的方法",
    "keyFields": {"Name": "方法名，必须与 CustomFunc 注册的方法名完全一致"},
    "constraints": "Name 必填；运行时调用 Context.CustomFunc.RunFunc(Name)",
    "commonMistakes": "Name 写错或未注册会报\"找不到自定义函数\"；该方法无参数无返回值"
  },
  "IO操作": {
    "purpose": "批量输出IO点状态（开/关），每项支持前后延时",
    "keyFields": {"IOCount": "子项数量(1-6)", "IoParams": "IO输出项列表，每项含 IOName(输出点名)、value(开/关)、delayBefore/delayBeforeV、delayAfter/delayAfterV"},
    "constraints": "IOName 必填且必须是\"通用输出\"类型；delayBefore<=0 时读 delayBeforeV 变量；delayAfter<=0 时读 delayAfterV 变量；列表为空时报错",
    "commonMistakes": "IOName 填输入点会报\"IO输出失败\"；value 是 bool(true=开,false=关)不是字符串"
  },
  "IO检测": {
    "purpose": "检测输入IO点是否达到期望状态，所有项为\"与\"关系，超时未满足报警",
    "keyFields": {"IOCount": "子项数量(1-6)", "timeOutC": "超时参数组，含 TimeOut(固定ms) 和 TimeOutValue(变量名)", "IoParams": "检测项列表，每项含 IOName(输入/输出点名) 和 value(期望状态)"},
    "constraints": "IOName 必填；timeOutC.TimeOut<=0 时读 TimeOutValue；两者都<=0 报\"超时配置无效\"；循环检测直到全部满足或超时",
    "commonMistakes": "IOName 必须是\"通用输入\"或\"通用输出\"；多项任一不满足即继续等待；value 是 bool"
  },
  "IO组": {
    "purpose": "组合指令：先执行输出动作，再检测输入状态",
    "keyFields": {"OutIOCount": "输出子项数量", "CheckIOCount": "检测子项数量", "OutIoParams": "输出IO列表(同 IO操作)", "CheckIoParams": "检测IO列表(同 IO检测)", "timeOutC": "检测阶段超时参数组"},
    "constraints": "OutIoParams 必须全为\"通用输出\"；CheckIoParams 必须全为\"通用输入\"；类型不符直接报错；两阶段均不能为空",
    "commonMistakes": "输出和检测的IO类型不能混；timeOutC 仅作用于检测阶段"
  },
  "IO逻辑跳转": {
    "purpose": "根据多个IO状态逻辑组合结果跳转",
    "keyFields": {"IOCount": "IO条件数量", "InvalidDelayMs": "首次判断失败后的重试延时(ms)", "IoParams": "条件项列表，每项含 IOName、Target(目标bool)、Logic(与/或)", "TrueGoto": "逻辑为真时跳转目标(procIndex-stepIndex-opIndex)", "FalseGoto": "逻辑为假时跳转目标(procIndex-stepIndex-opIndex)"},
    "constraints": "IOName 必填；Logic 仅\"与\"/\"或\"；InvalidDelayMs<0 报错；第一项的 Logic 不生效(默认 isFirst)；失败且 InvalidDelayMs>0 时延时后重试一次；TrueGoto 和 FalseGoto 均必填，格式 procIndex-stepIndex-opIndex",
    "commonMistakes": "Logic 不能填\"且\"/\"或\"以外的值；第一项 Logic 字段会被忽略；TrueGoto/FalseGoto 留空会报\"跳转位置为空\"并报警，不是\"不跳转\"；格式必须是三段式 procIndex-stepIndex-opIndex"
  },
  "流程操作": {
    "purpose": "启动或停止其他流程",
    "keyFields": {"ProcCount": "子项数量", "procParams": "流程项列表，每项含 ProcName(流程名)、ProcValue(流程变量)、value(运行/停止)、delayAfter/delayAfterV"},
    "constraints": "ProcName 非空时优先，否则读 ProcValue 变量；value 仅\"运行\"/\"停止\"；运行要求目标流程处于 Stopped，停止要求非 Stopped",
    "commonMistakes": "启动未停止的流程会报\"流程未停止\"；停止已停止的流程会报\"流程已停止\""
  },
  "等待流程状态": {
    "purpose": "等待其他流程达到指定状态(运行/停止)",
    "keyFields": {"ProcCount": "子项数量", "Params": "等待项列表，每项含 ProcName/ProcValue、value(运行/停止)", "timeOutC": "超时参数组", "delayAfter": "完成后附加延时(ms)", "delayAfterV": "附加延时变量名(delayAfter<=0 时读取)"},
    "constraints": "timeOut<=0 时读 TimeOutValue；两者均<=0 报\"超时配置无效\"；所有项同时满足才通过",
    "commonMistakes": "value 必须是\"运行\"或\"停止\"；找不到流程会报错"
  },
  "跳转": {
    "purpose": "根据变量值匹配跳转目标(类似 switch)",
    "keyFields": {"ValueIndex/ValueName": "待匹配变量(必填)", "Count": "匹配分支数量", "Params": "分支列表，每项含 MatchValue/MatchValueIndex/MatchValueV(匹配值)、Goto(跳转目标，格式 procIndex-stepIndex-opIndex)", "DefaultGoto": "未匹配时的默认跳转(格式 procIndex-stepIndex-opIndex)"},
    "constraints": "MatchValue(固定值)与 MatchValueIndex/MatchValueV(变量)互斥，同时填报\"匹配值配置冲突\"；待匹配变量值为空报错；命中即跳转；全未命中用 DefaultGoto；DefaultGoto 为空时不跳转（继续执行下一条指令）",
    "commonMistakes": "匹配值固定值和变量只能填一个；Goto 和 DefaultGoto 格式必须是 procIndex-stepIndex-opIndex 三段式；DefaultGoto 为空是合法的（表示不跳转，继续下一条）"
  },
  "逻辑判断": {
    "purpose": "多条件数值/字符判断后跳转",
    "keyFields": {"goto1": "条件成立跳转目标(格式 procIndex-stepIndex-opIndex，必填)", "goto2": "条件不成立跳转目标(格式 procIndex-stepIndex-opIndex，必填)", "failDelay": "失败时重试延时(ms，字符串)", "Count": "条件分支数量", "Params": "条件项列表，含 ValueIndex/ValueName(判断变量)、JudgeMode、Up/Down、keyString、equal、Operator"},
    "constraints": "JudgeMode 仅\"值在区间左\"/\"值在区间右\"/\"值在区间内\"/\"等于特征字符\"；前三种按数值解析(用 Up/Down)，第四种按字符(用 keyString)；Operator 仅\"且\"/\"或\"；第一项 Operator 忽略；goto1 和 goto2 均必填，为空会报\"跳转位置为空\"",
    "commonMistakes": "JudgeMode 选\"等于特征字符\"时 Up/Down 不生效；数值模式变量不能解析为 double 会报错；goto2 为空不是\"不跳转/正常结束\"而是报警——若条件不成立时需继续执行下一条，应将 goto2 指向下一条指令的位置；格式必须三段式 procIndex-stepIndex-opIndex，不可省略 procIndex"
  },
  "延时": {
    "purpose": "流程暂停指定时长",
    "keyFields": {"timeMiniSecond": "固定延时时长(ms，字符串)", "timeMiniSecondV": "延时变量名"},
    "constraints": "timeMiniSecond 非空时优先(需非负整数)；为空时读 timeMiniSecondV 变量值(需非负整数)；两者都空则延时0",
    "commonMistakes": "timeMiniSecond 是字符串字段不是数字；负值或非数字会报\"延时时间无效\""
  },
  "弹框": {
    "purpose": "弹出对话框供用户选择，支持报警灯/蜂鸣器联动和延时自动关闭",
    "keyFields": {"PopupType": "弹框样式：弹是/弹是与否/弹是与否与取消", "InfoType": "提示信息来源：自定义提示信息/变量类型/报警信息库", "PopupMessage": "固定提示文本", "PopupMessageValue": "提示变量名", "PopupAlarmInfoID": "报警信息编号；字段类型是 string，必须按 JSON 字符串传，例如 \"0\"", "Btn1Text/Btn2Text/Btn3Text": "按钮文本", "PopupGoto1/2/3": "跳转目标(格式 procIndex-stepIndex-opIndex)", "DelayClose/DelayCloseTimeMs": "延时自动关闭", "AlarmLightEnable": "启用报警灯", "BuzzerIo/RedLightIo/YellowLightIo/GreenLightIo": "报警灯IO"},
    "constraints": "PopupType 决定按钮数量；InfoType 决定提示文本来源；DelayClose=true 时 DelayCloseTimeMs 必须>0；AlarmLightEnable=启用 时蜂鸣器/灯IO才生效；PopupGoto 格式 procIndex-stepIndex-opIndex",
    "commonMistakes": "InfoType=报警信息库时按钮文本从报警信息读取，自定义的 Btn*Text 会被忽略；PopupGoto 格式必须三段式，不可省略 procIndex"
  },
  "获取变量": {
    "purpose": "批量复制变量值(源→存储)，支持二级索引嵌套",
    "keyFields": {"Count": "子项数量", "Params": "复制项列表，每项含 ValueSourceIndex/ValueSourceName(源) 和 ValueSaveIndex/ValueSaveName(存储)"},
    "constraints": "源变量和存储变量都必须能解析；按项逐一复制 Value 字段",
    "commonMistakes": "源/存储变量任一不存在都会报错；索引和名称二选一"
  },
  "修改变量": {
    "purpose": "对源变量进行运算后保存到结果变量",
    "keyFields": {"ModifyType": "修改模式：替换/叠加/乘法/除法/求余/绝对值", "ValueSourceIndex/ValueSourceName": "源变量", "ChangeValue/ChangeValueIndex/ChangeValueName": "修改值(固定值或变量二选一)", "OutputValueIndex/OutputValueName": "结果保存变量"},
    "constraints": "ChangeValue(固定)与 ChangeValueIndex/Name(变量)互斥；除\"替换\"和\"绝对值\"外变量必须是 double；\"绝对值\"模式不需要修改值",
    "commonMistakes": "运算类模式变量类型不是 double 会报\"变量类型不匹配\"；修改值固定值和变量不能同时填"
  },
  "数据拼接": {
    "purpose": "按 string.Format 模板拼接多个变量值",
    "keyFields": {"Format": "拼接格式模板(如 \"{0}-{1}\")", "OutputValueIndex/OutputValueName": "结果保存变量", "Count": "源变量数量", "Params": "源变量列表"},
    "constraints": "Format 不能为 null(可空串)；按 Params 顺序填充占位符；源变量值 null 转空字符串",
    "commonMistakes": "占位符数量应与 Params 数量匹配；使用 .NET string.Format 语法"
  },
  "字符串分割": {
    "purpose": "按分隔符分割字符串，将结果连续写入多个变量",
    "keyFields": {"SplitMark": "分隔符(char)", "startIndex": "分割结果起始下标(字符串)", "Count": "提取数量(字符串，空则取全部)", "SourceValueIndex/SourceValue": "源变量", "OutputIndex/Output": "结果起始保存变量"},
    "constraints": "startIndex 需非负整数；Count 为空时取全部分段；从 OutputIndex 开始连续写入 Count 个变量；越界报错",
    "commonMistakes": "结果写入从 Output 变量索引开始连续 Count 个变量；SplitMark 是单个 char"
  },
  "字符串替换": {
    "purpose": "替换字符串内容(按字符或按区间)",
    "keyFields": {"ReplaceType": "替换类型：替换指定字符/替换指定区间", "ReplaceStr/ReplaceStrIndex/ReplaceStrV": "被替换字符串", "NewStr/NewStrIndex/NewStrV": "新字符串", "StartIndex/Count": "区间模式下的起始位置和长度", "SourceValueIndex/SourceValue": "源变量", "OutputIndex/Output": "结果保存变量"},
    "constraints": "ReplaceType=替换指定字符时用 ReplaceStr 系列；=替换指定区间时用 StartIndex/Count；固定值与变量互斥",
    "commonMistakes": "模式选错会导致字段不显示；被替换字符/新字符任一为空报错"
  },
  "设置结构体数据项": {
    "purpose": "设置结构体中某数据项的多个字段值",
    "keyFields": {"StructIndex": "结构体索引", "ItemIndex": "数据项索引", "Count": "设置的字段数量", "Params": "字段列表，每项含 valueIndex(字段索引) 和 value(固定值)"},
    "constraints": "StructIndex/ItemIndex/Count 必须为有效整数；value 是字符串固定值不是变量名",
    "commonMistakes": "value 是字符串固定值；Count 不能超过 Params 实际数量"
  },
  "获取结构体数据项": {
    "purpose": "读取结构体数据项字段值并写入变量",
    "keyFields": {"IsAllItem": "true 时批量读取全部字段", "StartValue": "批量模式结果起始保存变量", "StructIndex": "结构体索引", "ItemIndex": "数据项索引", "Count": "非批量模式的读取数量", "Params": "字段列表"},
    "constraints": "IsAllItem=true 时按 StartValue 起始连续写全部字段；false 时按 Params 逐项读取",
    "commonMistakes": "批量模式与非批量模式字段使用不同"
  },
  "复制结构体数据项": {
    "purpose": "复制结构体数据项(整项或部分字段)",
    "keyFields": {"IsAllValue": "true 时整体复制", "SourceStructIndex/SourceItemIndex": "源索引", "TargetStructIndex/TargetItemIndex": "目标索引", "Count": "字段数量", "Params": "字段列表"},
    "constraints": "IsAllValue=true 时仅用四个索引整体复制；false 时按 Params 逐字段复制",
    "commonMistakes": "目标字段索引字段名是 Targetvalue(注意拼写)"
  },
  "插入结构体数据项": {
    "purpose": "在指定位置插入新数据项",
    "keyFields": {"Name": "新数据项名称", "TargetStructIndex": "目标结构体索引", "TargetItemIndex": "目标数据项索引", "Count": "字段数量", "Params": "字段列表，每项含 Type(double/string)、ValueItem(变量名)、Value(固定值)"},
    "constraints": "Type 仅\"double\"/\"string\"；ValueItem 非空时优先于 Value",
    "commonMistakes": "ValueItem 优先于 Value；数值类型 Value 不能解析会报错"
  },
  "删除结构体数据项": {
    "purpose": "删除结构体中指定数据项",
    "keyFields": {"TargetStructIndex": "结构体索引", "TargetItemIndex": "数据项索引(有特殊含义)"},
    "constraints": "TargetItemIndex>=255 删末尾项；<=-1 删首项；其他值删指定位置",
    "commonMistakes": "TargetItemIndex 有特殊语义：255 和 -1 不是普通索引"
  },
  "查找结构体数据项": {
    "purpose": "按关键字查找数据项并返回结果",
    "keyFields": {"TargetStructIndex": "结构体索引", "Type": "查找类型：名称等于key/字符串等于key/数值等于key", "key": "查找关键字", "save": "结果保存变量名"},
    "constraints": "Type=数值等于key 时 key 必须能 double.Parse",
    "commonMistakes": "Type 与 key 内容类型要匹配；save 是变量名不是索引"
  },
  "获取结构体数量": {
    "purpose": "获取结构体总数和指定结构体的项数",
    "keyFields": {"TargetStructIndex": "目标结构体索引", "StructCount": "结构体总数保存变量", "ItemCount": "项数保存变量"},
    "constraints": "StructCount 和 ItemCount 都必填",
    "commonMistakes": "两个保存变量都要填"
  },
  "网口通讯操作": {
    "purpose": "启动或断开 TCP 连接",
    "keyFields": {"Count": "子项数量", "Params": "操作列表，每项含 Name(TCP对象名) 和 Ops(启动/断开)"},
    "constraints": "Name 必须是已配置的 TCP 对象；Ops 仅\"启动\"/\"断开\"",
    "commonMistakes": "Ops 不能填其他值；Name 不存在会报\"TCP配置不存在\""
  },
  "等待网口连接": {
    "purpose": "等待 TCP 连接建立成功",
    "keyFields": {"Count": "子项数量", "Params": "等待列表，每项含 Name 和 TimeOut(超时ms)"},
    "constraints": "Name 必须已配置；TimeOut 必须>0",
    "commonMistakes": "TimeOut 是每项内的字段；超时未连接报\"等待TCP连接超时\""
  },
  "发送TCP通讯消息": {
    "purpose": "发送 TCP 消息",
    "keyFields": {"ID": "TCP对象标识", "Msg": "发送内容来源变量名", "isConVert": "true 按16进制发送", "TimeOut": "超时(ms，默认3000)"},
    "constraints": "ID 必填；TimeOut 必须>0；Msg 是变量名(运行时取值)",
    "commonMistakes": "Msg 是变量名不是直接文本"
  },
  "接收TCP通讯消息": {
    "purpose": "接收 TCP 消息并写入变量",
    "keyFields": {"ID": "TCP对象标识", "MsgSaveValue": "接收结果保存变量名", "isConVert": "true 按16进制解析", "TImeOut": "超时(ms，默认3000，注意大小写拼写)"},
    "constraints": "必须已连接；TImeOut 必须>0",
    "commonMistakes": "字段名是 TImeOut(首字母大写 I)不是 Timeout"
  },
  "串口通讯操作": {
    "purpose": "启动或断开串口连接",
    "keyFields": {"Count": "子项数量", "Params": "操作列表，每项含 Name(串口对象名) 和 Ops(启动/断开)"},
    "constraints": "Name 必须是已配置的串口对象；Ops 仅\"启动\"/\"断开\"",
    "commonMistakes": "Ops 不能填其他值"
  },
  "等待串口连接": {
    "purpose": "等待串口连接建立成功",
    "keyFields": {"Count": "子项数量", "Params": "等待列表，每项含 Name 和 TimeOut(超时ms)"},
    "constraints": "Name 必须已配置；TimeOut 必须>0",
    "commonMistakes": "TimeOut<=0 报\"超时配置无效\""
  },
  "发送串口通讯消息": {
    "purpose": "发送串口消息",
    "keyFields": {"ID": "串口对象标识", "Msg": "发送内容来源变量名", "isConVert": "true 按16进制发送", "TimeOut": "超时(ms，默认3000)"},
    "constraints": "ID 必填；TimeOut 必须>0；Msg 是变量名",
    "commonMistakes": "Msg 是变量名不是直接文本"
  },
  "接收串口通讯消息": {
    "purpose": "接收串口消息并写入变量",
    "keyFields": {"ID": "串口对象标识", "MsgSaveValue": "接收结果保存变量名", "isConVert": "true 按16进制解析", "TImeOut": "超时(ms，默认3000，注意拼写)"},
    "constraints": "必须已打开；TImeOut 必须>0",
    "commonMistakes": "字段名是 TImeOut(首字母大写 I)不是 Timeout"
  },
  "发送与接收": {
    "purpose": "一次性完成发送+接收(TCP 或串口)",
    "keyFields": {"CommType": "通讯类型：TCP/串口", "ID": "通讯对象标识", "SendMsg": "发送内容来源变量名", "SendConvert": "true 按16进制发送", "ReceiveSaveValue": "接收结果保存变量名(可空仅发送)", "ReceiveConvert": "true 按16进制解析", "TimeOut": "超时(ms，默认3000)"},
    "constraints": "CommType 仅\"TCP\"/\"串口\"；TimeOut 必须>0；ReceiveSaveValue 可空",
    "commonMistakes": "CommType 决定 ID 可选范围"
  },
  "PLC读写": {
    "purpose": "读写 PLC 数据",
    "keyFields": {"PlcName": "PLC设备名", "DataType": "数据类型：String/Boolean/Byte/UShort/Short/UInt/Int/Float/Double", "DataOps": "读写方向：读PLC/写PLC/读写", "PlcAddress": "PLC首地址", "WriteConst": "写入常量(写时优先)", "ValueName": "本地变量名(读写均用)", "Quantity": "数据数量(>0)"},
    "constraints": "Quantity 必须>0；写PLC时 WriteConst 非空优先，否则读 ValueName；读PLC时 ValueName 必填；\"读写\"模式先写后读",
    "commonMistakes": "写PLC时 WriteConst 和 ValueName 同时填会优先用 WriteConst"
  },
  "创建料盘": {
    "purpose": "根据四角参考点生成料盘网格点阵",
    "keyFields": {"StationName": "工站名", "TrayId": "料盘ID(>=0)", "RowCount/ColCount": "行/列数(>0)", "PX1": "左上参考点", "PX2": "右上参考点", "PY1": "左下参考点", "PY2": "右下参考点"},
    "constraints": "RowCount/ColCount 必须>0；四个角点必须是工站已有点位且6轴数据完整",
    "commonMistakes": "四个角点必须都已存在于工站；点位轴数量不是6会报\"参考点轴数量异常\""
  },
  "走料盘点": {
    "purpose": "移动到料盘指定位置",
    "keyFields": {"StationName": "工站名", "TrayId": "料盘号(固定)", "TrayIdValueIndex/TrayIdValueName": "料盘号变量(与 TrayId 二选一)", "TrayPos": "料盘位置(固定，>0)", "TrayPosValueIndex/TrayPosValueName": "料盘位置变量(与 TrayPos 二选一)", "isUnWait": "true 不等待运动完成"},
    "constraints": "TrayId 和 TrayId 变量二选一(变量非空时 TrayId 必须=0)；TrayPos 同理；TrayId>=0，TrayPos>0",
    "commonMistakes": "固定值非0时不能再填变量字段"
  },
  "回原": {
    "purpose": "工站回零",
    "keyFields": {"StationName": "工站名", "StationIndex": "工站索引(!=-1 时优先，默认-1)", "StationHomeType": "回原模式：所有轴同步回/轴按优先顺序回", "isUnWait": "true 不等待完成"},
    "constraints": "StationIndex!=-1 时按索引取工站；回零前轴必须已到位",
    "commonMistakes": "StationHomeType 仅两个取值；轴未到位报\"轴未到位，禁止回零\""
  },
  "工站走点": {
    "purpose": "移动到工站预设点位",
    "keyFields": {"StationName": "工站名", "PosName": "点位名", "PosIndex": "点位索引(!=-1 时优先)", "isUnWait": "true 不等待", "isCheckInPos": "true 校验到位精度(0.01)", "timeOut/timeOutV": "超时(默认120000)及变量", "IsDisableAxis": "无禁用/有禁用", "Axis1-6": "禁用标志(true=禁用该轴)", "ChangeVel": "不改变/改变速度", "Vel/VelV": "速度(0-100)", "Acc/AccV": "加速度(0-100)", "Dec/DecV": "减速度(0-100)"},
    "constraints": "速度/加减速度范围 0-100(百分比)；Axis=true 的轴被跳过；timeOut<=0 读 timeOutV",
    "commonMistakes": "Axis=true 是\"禁用\"不是\"启用\"；速度值是百分比不是实际速度"
  },
  "点位修改": {
    "purpose": "修改工站点位坐标(覆盖或叠加)",
    "keyFields": {"StationName": "工站名", "RefPosName": "参考点：自定义坐标/当前位置/具体点位名", "TargetPosName": "待修改的目标点位", "ModifyType": "修改方式：叠加/替换", "CustomX/Y/Z/U/V/W": "自定义坐标"},
    "constraints": "RefPosName=自定义坐标 时用 Custom* 字段；ModifyType=替换 覆盖，=叠加 累加",
    "commonMistakes": "替换是覆盖目标点坐标；叠加是在目标点基础上加参考点值"
  },
  "获取工站位置": {
    "purpose": "获取当前位置或点位坐标，保存到点位或变量",
    "keyFields": {"StationName": "工站名", "SourceType": "获取方式：当前位置/指定点位", "SourcePosName": "指定点位名", "SaveType": "保存方式：保存到点位/保存到变量", "TargetPosName": "保存目标点位", "OutputXIndex/XName/Y.../W...": "各轴保存变量"},
    "constraints": "SourceType=指定点位 时 SourcePosName 必填；SaveType=保存到变量 时至少配置一个轴 Output 字段",
    "commonMistakes": "保存到变量时所有轴 Output 字段都未配置会报\"保存变量未配置\""
  },
  "偏移量": {
    "purpose": "按相对距离移动(各轴独立设距离)",
    "keyFields": {"StationName": "工站名", "isUnWait": "true 不等待", "isCheckInPos": "true 校验到位", "timeOut/timeOutV": "超时(默认120000)", "Axis1-6": "各轴相对距离(=0 时读 AxisV 变量)", "Axis1V-6V": "各轴距离变量名", "ChangeVel": "不改变/改变速度", "Vel/VelV": "速度(0-100)", "Acc/AccV": "加速度(0-100)", "Dec/DecV": "减速度(0-100)"},
    "constraints": "每轴 Axis 值=0 时读对应 AxisV 变量；速度/加减速度范围 0-100",
    "commonMistakes": "Axis=0 且 AxisV 未配会报错；距离是相对量不是绝对坐标"
  },
  "设置速度": {
    "purpose": "修改工站或单轴的运行速度参数",
    "keyFields": {"StationName": "工站名", "StationIndex": "工站索引(默认-1)", "SetAxisObj": "设置对象：工站 或 具体轴名", "Vel/VelV": "速度(0-100)", "Acc/AccV": "加速度(0-100)", "Dec/DecV": "减速度(0-100)"},
    "constraints": "SetAxisObj=工站 时修改全部轴；Vel=0 时读 VelV；速度范围 0-100",
    "commonMistakes": "速度值是百分比(0-100)不是实际速度"
  },
  "停止运动": {
    "purpose": "停止工站运动(整站或按轴)",
    "keyFields": {"StationName": "工站名", "isAllStop": "true 整站停止", "Axis1-6": "轴停止标志(isAllStop=false 时，true=停止该轴)"},
    "constraints": "isAllStop=true 时停止所有轴；=false 时按 Axis=true 停止对应轴",
    "commonMistakes": "Axis1-6 仅在 isAllStop=false 时生效"
  },
  "等待运动": {
    "purpose": "等待工站运动停止(到位或回零完成)",
    "keyFields": {"StationName": "工站名", "StationIndex": "工站索引(默认-1)", "isWaitHome": "true 等待回零完成，false 等待到位", "timeOut/timeOutV": "超时(默认120000)及变量"},
    "constraints": "isWaitHome=true 时检查 HomeStatus；=false 时检查 GetInPos；timeOut<=0 读 timeOutV",
    "commonMistakes": "timeOut 默认 120000；等待回零和等待到位是两种不同判定逻辑"
  }
}
""";

        private static string GetOperationGuideByType(string? operaType)
        {
            if (string.IsNullOrWhiteSpace(operaType))
            {
                return "{\"ok\":false,\"errorCode\":\"OPERA_TYPE_REQUIRED\",\"message\":\"必须提供单个operaType，禁止读取全部指令指南\"}";
            }
            JsonObject root = JsonNode.Parse(OperationGuideJson)?.AsObject()
                ?? throw new InvalidOperationException("指令指南格式无效");
            JsonNode? guide = root.FirstOrDefault(item =>
                string.Equals(item.Key, operaType.Trim(), StringComparison.Ordinal)).Value;
            if (guide != null)
            {
                return new JsonObject
                {
                    ["ok"] = true,
                    ["operaType"] = operaType.Trim(),
                    ["guide"] = guide.DeepClone()
                }.ToJsonString();
            }
            string[] candidates = root.Select(item => item.Key)
                .Where(key => !key.StartsWith("_", StringComparison.Ordinal)
                    && key.Contains(operaType.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToArray();
            return new JsonObject
            {
                ["ok"] = false,
                ["errorCode"] = "OPERATION_GUIDE_NOT_FOUND",
                ["message"] = $"未找到指令类型:{operaType.Trim()}",
                ["candidates"] = new JsonArray(candidates
                    .Select(item => (JsonNode?)JsonValue.Create(item)).ToArray())
            }.ToJsonString();
        }

        private static string BuildPatchActionSchema(string actionType)
        {
            string normalized = (actionType ?? string.Empty).Trim();
            string required;
            string rule;
            switch (normalized)
            {
                case "update_proc_head_fields":
                    required = "type,fieldChanges"; rule = "fieldChanges仅允许Name/AutoStart/Disable"; break;
                case "update_step_fields":
                    required = "type,stepId,fieldChanges"; rule = "fieldChanges仅允许Name/Disable"; break;
                case "update_operation_fields":
                    required = "type,stepId,opId,expectedOperaType,fieldChanges"; rule = "字段必须来自get_operation_schema"; break;
                case "append_step":
                    required = "type,name"; rule = "追加到流程末尾"; break;
                case "insert_step":
                    required = "type,insertIndex,name"; rule = "insertIndex允许0..stepCount"; break;
                case "delete_step":
                    required = "type,stepId"; rule = "提交前检查跳转影响"; break;
                case "move_step":
                    required = "type,stepId,targetIndex"; rule = "targetIndex是移除源项后的最终索引"; break;
                case "append_operation":
                    required = "type,stepId,operaType,fieldValues?"; rule = "operaType和fieldValues必须来自Schema"; break;
                case "insert_operation":
                    required = "type,stepId,insertIndex,operaType,fieldValues?"; rule = "insertIndex允许0..opCount"; break;
                case "delete_operation":
                    required = "type,stepId,opId,expectedOperaType"; rule = "提交前检查跳转影响"; break;
                case "move_operation":
                    required = "type,stepId,opId,targetStepId,targetIndex,expectedOperaType"; rule = "targetIndex是移除源项后的最终索引"; break;
                default:
                    return new JsonObject
                    {
                        ["ok"] = false,
                        ["errorCode"] = "PATCH_ACTION_NOT_SUPPORTED",
                        ["message"] = $"不支持的Patch动作:{normalized}"
                    }.ToJsonString();
            }
            return new JsonObject
            {
                ["ok"] = true,
                ["actionType"] = normalized,
                ["requiredFields"] = required,
                ["rule"] = rule,
                ["common"] = "顶层Patch必须包含procIndex/baseProcId/actions；先preview_patch，确认后携带同一previewId调用apply_patch"
            }.ToJsonString();
        }

        [McpServerTool(Name = "get_info_log_tail"), Description(
            "读取运行信息页最近日志（排查报警/Bridge调用失败/流程运行异常）。"
            + "maxCount 范围 1..200，默认 50。")]
        public static async Task<string> GetInfoLogTail(
            [Description("返回日志条数上限，范围 1..200，默认 50")] int? maxCount = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetInfoLogTail),
                args: new { maxCount },
                action: client => client.GetInfoLogTailAsync(maxCount)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "diagnose_proc"), Description(
            "诊断流程结构与运行风险（禁用/空步骤指令/未知指令类型/跳转错误/报警/断点）。"
            + "含运行时状态，适合完整诊断。")]
        public static async Task<string> DiagnoseProc(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(DiagnoseProc),
                args: new { procIndex },
                action: client => client.DiagnoseProcAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "validate_proc"), Description(
            "轻量结构验证（聚焦跳转目标有效性/空步骤指令/禁用项，不含运行时状态）。"
            + "适合修改前后快速检查。")]
        public static async Task<string> ValidateProc(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(ValidateProc),
                args: new { procIndex },
                action: client => client.ValidateProcAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_intent_templates"), Description(
            "列出本地可用的中间意图 JSON 模板。"
            + "生成写入意图前应先读模板，不要依赖提示词长示例。"
            + "可选按 patchAction 过滤（如 update_operation_fields）。")]
        public static async Task<string> ListIntentTemplates(
            [Description("按 patchAction 过滤，如 update_operation_fields")] string? patchAction = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListIntentTemplates),
                args: new { patchAction },
                action: client => client.ListIntentTemplatesAsync(patchAction)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_intent_template"), Description(
            "读取单个意图模板。可按 templateId 精确读取，或按 patchAction 获取。"
            + "返回的 intentShape 描述该意图的必填字段结构。")]
        public static async Task<string> GetIntentTemplate(
            [Description("模板 ID，精确读取单个模板")] string? templateId = null,
            [Description("按 patchAction 获取模板，如 update_operation_fields")] string? patchAction = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetIntentTemplate),
                args: new { templateId, patchAction },
                action: client => client.GetIntentTemplateAsync(templateId, patchAction)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "build_patch_from_intent"), Description(
            "把结构化中间意图对象转换成标准 patchJson。"
            + "适合先用模板约束输出，再由本地统一组装 Patch。"
            + "intent 必须符合 get_intent_template 返回的 intentShape；baseProcId可省略，由Bridge按procIndex补齐。")]
        public static async Task<string> BuildPatchFromIntent(
            [Description("结构化中间意图对象，必须符合 get_intent_template 返回的 intentShape")] JsonElement intent)
        {
            return await ExecuteAsync(
                toolName: nameof(BuildPatchFromIntent),
                args: new { intent },
                action: client => client.BuildPatchFromIntentAsync(intent)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "preview_intent"), Description(
            "使用结构化中间意图对象直接预演（内部先把意图转标准 Patch 再 preview，不需要模型自行组装patchJson）。"
            + "只需提供procIndex，baseProcId由Bridge读取当前流程自动补齐。"
            + "返回 previewId 和 patchHash，提交前需由 Automation 前台确认 previewId。"
            + "完全权限模式下预演会自动确认，AI 拿到 previewId 后直接再调 apply_intent 提交即可。")]
        public static async Task<string> PreviewIntent(
            [Description("结构化中间意图对象；baseProcId可省略")] JsonElement intent)
        {
            return await ExecuteAsync(
                toolName: nameof(PreviewIntent),
                args: new { intent },
                action: client => client.PreviewIntentAsync(intent)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "apply_intent"), Description(
            "使用结构化中间意图对象直接提交。必须携带前台已确认的previewId，且意图内容必须与预演完全一致。"
            + "正式提交只允许目标流程处于Stopped；提交前先用get_snapshot确认。非Stopped时不要调用本工具、不得调用stop_proc，必须告知用户并等待操作员停止流程。"
            + "被PROC_NOT_STOPPED拒绝时没有停止流程、没有保存文件、没有发布热更新，状态未改变前禁止重复提交。"
            + "被SOURCE_VALIDATION_FAILED拒绝时应读取details逐项修正Hmi源码；该失败没有保存流程且预演仍有效，修正后使用同一previewId重试，不要重新生成流程Patch。"
            + "完全权限模式下预演自动确认，直接传入预演返回的 previewId 即可；禁止传字符串 null/undefined。")]
        public static async Task<string> ApplyIntent(
            [Description("结构化中间意图对象，必须与预演完全一致；baseProcId可省略")] JsonElement intent,
            [Description("预演阶段返回且已确认的 previewId")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(ApplyIntent),
                args: new { intent, previewId },
                action: client => client.ApplyIntentAsync(intent, previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "preview_patch"), Description(
            "预演结构化 Patch，不会落盘。"
            + "patchJson 必须是完整 JSON 对象，至少含 procIndex/baseProcId/actions。"
            + "update_proc_head_fields/update_step_fields/update_operation_fields 使用fieldChanges；append/insert_operation使用fieldValues。"
            + "同一流程内需要互相跳转的多条新指令应放在同一个actions数组中，Bridge会在全部动作完成后统一校验前向跳转。"
            + "提交前必须先调用本工具。返回 previewId 和 patchHash，需由 Automation 前台确认 previewId。"
            + "完全权限模式下预演会自动确认，AI 拿到 previewId 后直接再调 apply_patch 提交即可。")]
        public static async Task<string> PreviewPatch(
            [Description("Patch JSON 字符串，至少含 procIndex/baseProcId/actions")] string patchJson)
        {
            return await ExecuteAsync(
                toolName: nameof(PreviewPatch),
                args: new { patchJson },
                action: client => client.PreviewPatchAsync(patchJson)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "apply_patch"), Description(
            "应用结构化 Patch 触发保存发布。"
            + "必须携带前台已确认的 previewId，patchJson 必须与预演完全一致。"
            + "正式提交只允许目标流程处于Stopped；提交前先用get_snapshot确认。非Stopped时不得调用stop_proc，必须等待操作员停止。"
            + "被PROC_NOT_STOPPED拒绝时无任何保存/发布副作用，状态未改变前禁止重复提交。"
            + "被SOURCE_VALIDATION_FAILED拒绝时应读取details逐项修正Hmi源码；该失败没有保存流程且预演仍有效，修正后使用同一previewId重试，不要重新生成Patch。"
            + "完全权限模式下预演自动确认，直接传入预演返回的 previewId 即可；禁止传字符串 null/undefined。")]
        public static async Task<string> ApplyPatch(
            [Description("Patch JSON 字符串，必须与预演完全一致")] string patchJson,
            [Description("预演阶段返回且已确认的 previewId")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(ApplyPatch),
                args: new { patchJson, previewId },
                action: client => client.ApplyPatchAsync(patchJson, previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "patch_contract"), Description(
            "返回 Patch 调用约束与示例（domainConcepts/workflow/preferredWritePath/patchActions/patchShape/rules）。"
            + "首次编写 patch 前应读取本契约，了解 patchJson 结构和操作分类。"
            + "直接由 MCP 层静态返回，不走 Bridge。")]
        public static Task<string> PatchContract()
        {
            return Task.FromResult(GetPatchContract());
        }

        [McpServerTool(Name = "create_proc"), Description(
            "新增空流程。两阶段操作：预演阶段省略 previewId；提交阶段传入预演返回的 previewId。禁止传字符串 null/undefined。"
            + "新增流程含一个默认步骤，后续用 preview_patch 添加步骤指令。流程名不能重复。"
            + "预演返回的 targetIndex 只是预计位置，流程尚不存在；提交成功后必须调用 list_procs 获取真实 procIndex，禁止据此直接读取流程详情。"
            + "典型场景：新建流程来唤醒/启动其他流程、创建独立控制流程。"
            + "完全权限模式下预演会自动确认，AI 拿到 previewId 后直接再调本工具并传入 previewId 即可提交。")]
        public static async Task<string> CreateProc(
            [Description("流程名称（不能与现有流程重复）")] string name,
            [Description("是否自启动，默认 false")] bool? autoStart = null,
            [Description("是否禁用，默认 false")] bool? disable = null,
            [Description("提交阶段必填：预演阶段返回的 previewId；预演阶段请省略本参数，不要传字符串 null/undefined")] string? previewId = null)
        {
            return await ExecuteAsync(
                toolName: nameof(CreateProc),
                args: new { name, autoStart, disable, previewId },
                action: client => client.CreateProcAsync(name, autoStart, disable, previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "delete_procs"), Description(
            "批量删除流程。两阶段操作：预演阶段省略 previewId；提交阶段传入预演返回的 previewId。禁止传字符串 null/undefined。"
            + "删除会改变流程索引，正式提交要求全部流程均为Stopped。存在非Stopped流程时不得调用stop_proc，必须告知用户并等待操作员停止全部流程；状态未改变前禁止重复提交。"
            + "完全权限模式下预演会自动确认，AI 拿到 previewId 后直接再调本工具并传入 previewId 即可提交。")]
        public static async Task<string> DeleteProcs(
            [Description("待删除流程索引数组")] int[] procIndexes,
            [Description("提交阶段必填：预演阶段返回的 previewId；预演阶段请省略本参数，不要传字符串 null/undefined")] string? previewId = null)
        {
            return await ExecuteAsync(
                toolName: nameof(DeleteProcs),
                args: new { procIndexes, previewId },
                action: client => client.DeleteProcsAsync(procIndexes, previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "reorder_proc"), Description(
            "重排流程位置。两阶段操作：预演阶段省略 previewId；提交阶段传入预演返回的 previewId。禁止传字符串 null/undefined。"
            + "targetIndex 是移动后的最终索引。重排会改变流程索引，正式提交要求全部流程均为Stopped；AI不得调用stop_proc，必须等待操作员停止全部流程。"
            + "完全权限模式下预演会自动确认，AI 拿到 previewId 后直接再调本工具并传入 previewId 即可提交。")]
        public static async Task<string> ReorderProc(
            [Description("待移动的流程索引")] int procIndex,
            [Description("移动后的最终索引")] int targetIndex,
            [Description("提交阶段必填：预演阶段返回的 previewId；预演阶段请省略本参数，不要传字符串 null/undefined")] string? previewId = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ReorderProc),
                args: new { procIndex, targetIndex, previewId },
                action: client => client.ReorderProcAsync(procIndex, targetIndex, previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "copy_proc"), Description(
            "复制现有流程为新流程（含全部步骤和指令）。两阶段操作：预演阶段省略 previewId；提交阶段传入预演返回的 previewId。禁止传字符串 null/undefined。"
            + "适合基于现有流程改造。newName 为空时自动追加 _副本。"
            + "完全权限模式下预演会自动确认，AI 拿到 previewId 后直接再调本工具并传入 previewId 即可提交。")]
        public static async Task<string> CopyProc(
            [Description("源流程索引")] int procIndex,
            [Description("新流程名称，为空自动追加 _副本")] string? newName = null,
            [Description("提交阶段必填：预演阶段返回的 previewId；预演阶段请省略本参数，不要传字符串 null/undefined")] string? previewId = null)
        {
            return await ExecuteAsync(
                toolName: nameof(CopyProc),
                args: new { procIndex, newName, previewId },
                action: client => client.CopyProcAsync(procIndex, newName, previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "start_proc"), Description(
            "启动流程。不需要预演确认，直接发送命令。"
            + "要求流程处于 Stopped 状态。")]
        public static async Task<string> StartProc(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(StartProc),
                args: new { procIndex },
                action: client => client.StartProcAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "stop_proc"), Description(
            "停止流程。不需要预演确认，直接发送命令。"
            + "要求流程非 Stopped 状态。")]
        public static async Task<string> StopProc(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(StopProc),
                args: new { procIndex },
                action: client => client.StopProcAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "pause_proc"), Description(
            "暂停流程。不需要预演确认，直接发送命令。"
            + "要求流程处于 Running 状态。")]
        public static async Task<string> PauseProc(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(PauseProc),
                args: new { procIndex },
                action: client => client.PauseProcAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "resume_proc"), Description(
            "恢复暂停的流程。不需要预演确认，直接发送命令。"
            + "要求流程处于 Paused 状态。")]
        public static async Task<string> ResumeProc(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(ResumeProc),
                args: new { procIndex },
                action: client => client.ResumeProcAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_variables"), Description(
            "列出全部已配置变量（含运行时值/配置值/备注/变更历史）。"
            + "支持类型过滤和名称模糊匹配、分页。变量类型为 double 或 string。")]
        public static async Task<string> ListVariables(
            [Description("类型过滤：double 或 string")] string? type = null,
            [Description("名称模糊匹配关键词")] string? nameLike = null,
            [Description("分页偏移，默认 0")] int? offset = null,
            [Description("分页上限，默认 1000")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListVariables),
                args: new { type, nameLike, offset, limit },
                action: client => client.ListVariablesAsync(type, nameLike, offset, limit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_variable"), Description(
            "读取单个变量完整信息。必须提供 name 或 index 之一。")]
        public static async Task<string> GetVariable(
            [Description("变量名")] string? name = null,
            [Description("变量索引")] int? index = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetVariable),
                args: new { name, index },
                action: client => client.GetVariableAsync(name, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "search_variables"), Description(
            "按名称关键词/类型/运行时值内容搜索变量。"
            + "keyword 匹配变量名。")]
        public static async Task<string> SearchVariables(
            [Description("名称关键词")] string keyword,
            [Description("类型过滤：double 或 string")] string? type = null,
            [Description("运行时值内容模糊匹配")] string? valueLike = null,
            [Description("返回上限")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchVariables),
                args: new { keyword, type, valueLike, limit },
                action: client => client.SearchVariablesAsync(keyword, type, valueLike, limit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "set_variable"), Description(
            "修改变量运行时值（不写入配置文件，重启后恢复配置值）。"
            + "double 类型校验数字格式；修改触发 ValueChanged 事件，运行中流程能感知。"
            + "必须提供 name 或 index 之一。")]
        public static async Task<string> SetVariable(
            [Description("新值（始终传字符串，double 类型会校验数字格式）")] string value,
            [Description("变量名")] string? name = null,
            [Description("变量索引")] int? index = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SetVariable),
                args: new { value, name, index },
                action: client => client.SetVariableAsync(value, name, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "delete_variable"), Description(
            "清空指定索引变量槽位（Name/Value/Note 全部重置，保留槽位，不移动其他变量索引）。"
            + "需 ProcessEdit 权限。")]
        public static async Task<string> DeleteVariable(
            [Description("变量索引")] int index)
        {
            return await ExecuteAsync(
                toolName: nameof(DeleteVariable),
                args: new { index },
                action: client => client.DeleteVariableAsync(index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "add_variable"), Description(
            "在变量表中创建新变量。需 ProcessEdit 权限。"
            + "参数：name（必填，变量名，全局唯一）；type（可选，\"double\"或\"string\"，默认double）；"
            + "value（可选，初始值，double类型必须是数字，默认\"0\"）；note（可选，备注）；"
            + "index（可选，指定槽位位置[0,1000)，默认自动找第一个空槽位）。"
            + "名称重复或槽位被占用时返回错误。创建后自动持久化并刷新界面。"
            + "批量创建时请多次调用（如生成 IO1~IO10 需调用 10 次）。")]
        public static async Task<string> AddVariable(
            [Description("变量名（全局唯一）")] string name,
            [Description("类型：double 或 string，默认 double")] string? type = "double",
            [Description("初始值（double 类型必须是数字）")] string? value = null,
            [Description("备注")] string? note = null,
            [Description("指定槽位索引，不填则自动分配")] int? index = null)
        {
            return await ExecuteAsync(
                toolName: nameof(AddVariable),
                args: new { name, type, value, note, index },
                action: client => client.AddVariableAsync(name, type ?? "double", value, note, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_stations"), Description(
            "列出当前所有工站（工站是机械臂运动控制的逻辑分组，包含轴配置和点位列表）。"
            + "返回工站索引、名称、速度、点位数量。需 ProcessAccess 权限。")]
        public static async Task<string> ListStations()
        {
            return await ExecuteAsync(
                toolName: nameof(ListStations),
                args: new { },
                action: client => client.ListStationsAsync()).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_station"), Description(
            "获取指定工站的详情，包括轴配置信息和所有有名点位列表。"
            + "参数：stationIndex（工站索引）。需 ProcessAccess 权限。")]
        public static async Task<string> GetStation(
            [Description("工站索引")] int stationIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetStation),
                args: new { stationIndex },
                action: client => client.GetStationAsync(stationIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "add_station"), Description(
            "创建新工站。需 ProcessEdit 权限。参数：name（工站名称，必填）；vel（运行速度，可选，默认1）。"
            + "创建后自动持久化。工站包含 400 个点位槽位（初始为空）。"
            + "工站名重复时返回错误。")]
        public static async Task<string> AddStation(
            [Description("工站名称（全局唯一）")] string name,
            [Description("运行速度，可选，默认 1")] double? vel = null)
        {
            return await ExecuteAsync(
                toolName: nameof(AddStation),
                args: new { name, vel },
                action: client => client.AddStationAsync(name, vel)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "delete_station"), Description(
            "删除指定工站。需 ProcessEdit 权限。参数：stationIndex（工站索引）。"
            + "删除后后续工站索引会前移。删除后自动持久化并刷新界面。")]
        public static async Task<string> DeleteStation(
            [Description("工站索引")] int stationIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(DeleteStation),
                args: new { stationIndex },
                action: client => client.DeleteStationAsync(stationIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "update_station"), Description(
            "修改工站名称或速度。需 ProcessEdit 权限。"
            + "参数：stationIndex（必填）；name（新名称，可选）；vel（新速度，可选）。"
            + "至少提供 name 或 vel 之一，工站名重复时返回错误。修改后自动持久化。")]
        public static async Task<string> UpdateStation(
            [Description("工站索引")] int stationIndex,
            [Description("新名称，可选")] string? name = null,
            [Description("新速度，可选")] double? vel = null)
        {
            return await ExecuteAsync(
                toolName: nameof(UpdateStation),
                args: new { stationIndex, name, vel },
                action: client => client.UpdateStationAsync(stationIndex, name, vel)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_points"), Description(
            "列出工站下所有已命名的点位（示教点）。参数：stationIndex（工站索引）。"
            + "返回点位索引、名称和坐标 X/Y/Z/U/V/W。需 ProcessAccess 权限。")]
        public static async Task<string> ListPoints(
            [Description("工站索引")] int stationIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(ListPoints),
                args: new { stationIndex },
                action: client => client.ListPointsAsync(stationIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_point"), Description(
            "获取工站下指定点位的详情。参数：stationIndex（工站索引）；index（点位索引[0,400)）。"
            + "需 ProcessAccess 权限。")]
        public static async Task<string> GetPoint(
            [Description("工站索引")] int stationIndex,
            [Description("点位索引 [0,400)")] int index)
        {
            return await ExecuteAsync(
                toolName: nameof(GetPoint),
                args: new { stationIndex, index },
                action: client => client.GetPointAsync(stationIndex, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "set_point"), Description(
            "修改点位坐标和名称（相当于软件示教）。需 ProcessEdit 权限。"
            + "参数：stationIndex（必填）；index（点位索引[0,400)，必填）；name（新名称，可选）；"
            + "x/y/z/u/v/w（坐标值，每个可选，未传的保持不变）。"
            + "修改名称时会同步更新工站的有名点位字典，工站内点位名唯一。修改后自动持久化。")]
        public static async Task<string> SetPoint(
            [Description("工站索引")] int stationIndex,
            [Description("点位索引 [0,400)")] int index,
            [Description("新名称，可选")] string? name = null,
            [Description("X 坐标，可选")] double? x = null,
            [Description("Y 坐标，可选")] double? y = null,
            [Description("Z 坐标，可选")] double? z = null,
            [Description("U 坐标，可选")] double? u = null,
            [Description("V 坐标，可选")] double? v = null,
            [Description("W 坐标，可选")] double? w = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SetPoint),
                args: new { stationIndex, index, name, x, y, z, u, v, w },
                action: client => client.SetPointAsync(stationIndex, index, name, x, y, z, u, v, w)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "delete_point"), Description(
            "清空指定工站下某个点位的数据。需 ProcessEdit 权限。"
            + "点位表是固定槽位（每工站 400 个），delete 仅清空指定点位的数据（名称置空、坐标 X/Y/Z/U/V/W 归零），"
            + "index 保持不变。点位本身已为空时返回错误。删除后自动持久化并刷新界面。")]
        public static async Task<string> DeletePoint(
            [Description("工站索引")] int stationIndex,
            [Description("点位索引 [0,400)")] int index)
        {
            return await ExecuteAsync(
                toolName: nameof(DeletePoint),
                args: new { stationIndex, index },
                action: client => client.DeletePointAsync(stationIndex, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_data_structs"), Description(
            "列出全部数据结构名称及各自数据项数量。"
            + "数据结构用于存储结构化数据（如产品配方、坐标表等）。")]
        public static async Task<string> ListDataStructs()
        {
            return await ExecuteAsync(
                toolName: nameof(ListDataStructs),
                args: new { },
                action: client => client.ListDataStructsAsync()).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_data_struct"), Description(
            "读取数据结构详情（含所有 item 的所有字段：名称/类型/值）。"
            + "Number 字段返回 numValue，Text 字段返回 strValue。")]
        public static async Task<string> GetDataStruct(
            [Description("数据结构名称")] string name)
        {
            return await ExecuteAsync(
                toolName: nameof(GetDataStruct),
                args: new { name },
                action: client => client.GetDataStructAsync(name)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "search_data_structs"), Description(
            "在指定数据结构内搜索数据项。"
            + "可按 item 名称/字符串字段值/数值字段范围过滤。")]
        public static async Task<string> SearchDataStructs(
            [Description("数据结构名称")] string name,
            [Description("item 名称模糊匹配")] string? itemNameLike = null,
            [Description("字符串字段值模糊匹配")] string? strValueLike = null,
            [Description("数值字段下界（含）")] double? numValueMin = null,
            [Description("数值字段上界（含）")] double? numValueMax = null,
            [Description("返回上限")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchDataStructs),
                args: new { name, itemNameLike, strValueLike, numValueMin, numValueMax, limit },
                action: client => client.SearchDataStructsAsync(name, itemNameLike, strValueLike, numValueMin, numValueMax, limit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "set_data_struct_field"), Description(
            "修改数据结构中某个 item 的某个字段值。"
            + "需 ProcessEdit 权限；字段类型由系统自动判断 Number/Text，value 始终传字符串。")]
        public static async Task<string> SetDataStructField(
            [Description("数据结构名称")] string name,
            [Description("数据项索引")] int itemIndex,
            [Description("字段索引")] int fieldIndex,
            [Description("新值（始终传字符串）")] string value)
        {
            return await ExecuteAsync(
                toolName: nameof(SetDataStructField),
                args: new { name, itemIndex, fieldIndex, value },
                action: client => client.SetDataStructFieldAsync(name, itemIndex, fieldIndex, value)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_io"), Description(
            "列出全部 IO 配置（含名称/卡号/模块/索引/类型/电平/备注）。"
            + "IO 类型为\"通用输入\"或\"通用输出\"。")]
        public static async Task<string> ListIo(
            [Description("类型过滤：通用输入 或 通用输出")] string? type = null,
            [Description("名称模糊匹配关键词")] string? nameLike = null,
            [Description("返回上限")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListIo),
                args: new { type, nameLike, limit },
                action: client => client.ListIoAsync(type, nameLike, limit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_io"), Description(
            "读取单个 IO 配置信息。")]
        public static async Task<string> GetIo(
            [Description("IO 名称")] string name)
        {
            return await ExecuteAsync(
                toolName: nameof(GetIo),
                args: new { name },
                action: client => client.GetIoAsync(name)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "search_io"), Description(
            "按名称关键词/类型/卡号搜索 IO。"
            + "keyword 匹配 IO 名称。")]
        public static async Task<string> SearchIo(
            [Description("名称关键词")] string keyword,
            [Description("类型过滤：通用输入 或 通用输出")] string? type = null,
            [Description("卡号过滤")] int? cardNum = null,
            [Description("返回上限")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchIo),
                args: new { keyword, type, cardNum, limit },
                action: client => client.SearchIoAsync(keyword, type, cardNum, limit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_io_state"), Description(
            "读取单个 IO 实时电平状态。"
            + "true=高电平，false=低电平，null=读取失败。"
            + "通用输入通过硬件读取，通用输出读取当前输出状态。需硬件已就绪。")]
        public static async Task<string> GetIoState(
            [Description("IO 名称")] string name)
        {
            return await ExecuteAsync(
                toolName: nameof(GetIoState),
                args: new { name },
                action: client => client.GetIoStateAsync(name)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_alarms"), Description(
            "列出报警信息清单。默认只返回已配置名称的报警，可选返回空槽位、按类别/名称过滤。"
            + "报警表固定 1000 个槽位（index 0..999），set_alarm 填充槽位，delete_alarm 清空槽位。"
            + "参数：includeEmpty（可选，默认 false，true 时返回全部 1000 个槽位含空槽位）；"
            + "categoryLike（可选，按类别模糊匹配）；nameLike（可选，按名称模糊匹配）。")]
        public static async Task<string> ListAlarms(
            [Description("是否包含空槽位，默认 false")] bool? includeEmpty = null,
            [Description("按类别模糊匹配")] string? categoryLike = null,
            [Description("按名称模糊匹配")] string? nameLike = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListAlarms),
                args: new { includeEmpty, categoryLike, nameLike },
                action: client => client.ListAlarmsAsync(includeEmpty, categoryLike, nameLike)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "search_alarms"), Description(
            "分页搜索报警配置，适合大量报警数据。默认只返回已配置项，每次最多100条，返回total/offset/limit/hasMore/items。禁止为查找单项而读取全部1000槽位。")]
        public static async Task<string> SearchAlarms(
            [Description("是否包含空槽位，默认false")] bool? includeEmpty = null,
            [Description("报警分类模糊匹配")] string? categoryLike = null,
            [Description("报警名称模糊匹配")] string? nameLike = null,
            [Description("分页起点，默认0")] int? offset = null,
            [Description("每页数量1..100，默认50")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchAlarms),
                args: new { includeEmpty, categoryLike, nameLike, offset, limit },
                action: client => client.ListAlarmsAsync(includeEmpty, categoryLike, nameLike, offset, limit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_alarm"), Description(
            "读取单个报警信息详情。返回 index/name/category/btn1/btn2/btn3/note 字段。"
            + "用于查看指定槽位的报警配置。")]
        public static async Task<string> GetAlarm(
            [Description("报警槽位索引 [0,1000)")] int index)
        {
            return await ExecuteAsync(
                toolName: nameof(GetAlarm),
                args: new { index },
                action: client => client.GetAlarmAsync(index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "set_alarm"), Description(
            "创建或更新报警信息（覆盖写入指定槽位）。需 ProcessEdit 权限。"
            + "报警表固定 1000 个槽位，index 指定槽位位置；如槽位已有数据则覆盖。"
            + "业务约束：name 与 note 必须同时填写且非空白（与界面校验一致）。"
            + "参数：index（必填，槽位 [0,1000)）；name（必填，报警名称）；note（必填，报警信息内容）；"
            + "category（可选，报警类别）；btn1/btn2/btn3（可选，弹框按钮提示文本，对应确定/否/取消）。"
            + "创建后自动持久化并刷新界面。批量创建时请多次调用。")]
        public static async Task<string> SetAlarm(
            [Description("报警槽位索引 [0,1000)")] int index,
            [Description("报警名称（必填，与 note 同时填写）")] string name,
            [Description("报警信息内容（必填，与 name 同时填写）")] string note,
            [Description("报警类别")] string? category = null,
            [Description("按钮1提示（对应\"确定\"）")] string? btn1 = null,
            [Description("按钮2提示（对应\"否\"）")] string? btn2 = null,
            [Description("按钮3提示（对应\"取消\"）")] string? btn3 = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SetAlarm),
                args: new { index, name, note, category, btn1, btn2, btn3 },
                action: client => client.SetAlarmAsync(index, name, note, category, btn1, btn2, btn3)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "delete_alarm"), Description(
            "清空指定槽位的报警信息。需 ProcessEdit 权限。"
            + "报警表固定 1000 个槽位，delete 仅清空指定槽位的数据（name/note/category/btn1/btn2/btn3 置空），"
            + "index 保持不变。槽位本身为空时返回错误。删除后自动持久化并刷新界面。")]
        public static async Task<string> DeleteAlarm(
            [Description("报警槽位索引 [0,1000)")] int index)
        {
            return await ExecuteAsync(
                toolName: nameof(DeleteAlarm),
                args: new { index },
                action: client => client.DeleteAlarmAsync(index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_resources"), Description(
            "资源清单查询统一工具。通过 action 指定具体资源类型，通过 parameters 传 JSON 参数。" +
            "\naction 可选值及对应 parameters 字段：" +
            "\n- alarms: 列出报警配置清单（含名称/分类/按钮文本/备注；默认只返回已配置名称的报警）。parameters: {includeEmpty:bool?, categoryLike:string?, nameLike:string?}。" +
            "\n- plc: 列出 PLC 设备清单（含名称/协议/IP/端口/CPU 类型等；可选返回每个设备的映射表）。parameters: {includeMaps:bool?}。" +
            "\n- cards: 列出控制卡及轴配置清单（每张卡含卡类型/轴输入输出数量，及各轴名称/轴号/脉冲当量/回原参数/运动参数）。parameters: {includeAxes:bool? 默认true}。" +
            "\n- tray_points: 查询料盘缓存点位（TrayPointStore 是运行时缓存非持久化，需提供 stationName 和 trayId；未提供参数返回提示，提供后返回缓存点位坐标 行/列/X/Y/Z/U/V/W）。parameters: {stationName:string?, trayId:int?}。" +
            "\n- communications: 列出通讯配置清单（含 TCP 通道和串口通道两部分，每个通道含配置信息及当前运行状态）。parameters: {includeStatus:bool? 默认true}。" +
            "\n示例：list_resources(action=\"alarms\", parameters=\"{\\\"nameLike\\\":\\\"缺料\\\"}\")")]
        public static async Task<string> ListResources(
            [Description("操作类型：alarms/plc/cards/tray_points/communications")] string action,
            [Description("参数 JSON 字符串，不同 action 需要不同参数，详见工具描述")] string? parameters = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListResources),
                args: new { action, parameters },
                action: client => client.ListResourcesAsync(action, ParseParameters(parameters))).ConfigureAwait(false);
        }

        // Patch 调用约束与示例，由 patch_contract 工具直接静态返回（不走 Bridge）。
        public static string GetPatchContract()
        {
            const string contract = """
{
  "domainConcepts": {
    "程序": "指整个 Automation 应用，包含所有流程、变量表、IO配置、通讯配置等。不是单个流程。AI 无法直接修改程序本身，只能通过修改流程、控制流程运行来操作。",
    "流程(Proc)": "Automation 中独立的执行单元，每个流程有自己的名称、自启动/禁用属性。一个程序可包含多个流程。流程由步骤组成。procIndex 是流程在列表中的位置索引（从0开始），procId 是流程的唯一 Guid 标识。",
    "步骤(Step)": "流程内的逻辑分组，包含名称和禁用属性。一个流程可有多个步骤。步骤由指令组成。stepId 是步骤的唯一 Guid 标识。步骤用于组织相关指令、标记流程执行阶段。",
    "指令(Operation)": "最小执行单元，具体的自动化动作（如 IO检测、等待、GOTO跳转、赋值等）。每个指令有 operaType（类型）和对应的字段参数。opId 是指令的唯一标识。已知operaType时直接用get_operation_schema读取该类型字段；类型未知时才用list_operation_types发现可用类型。",
    "层次关系": "程序 > 流程(Proc) > 步骤(Step) > 指令(Operation)",
    "运行时状态": "流程运行状态：Stopped(停止)、Paused(暂停)、Running(运行)、Alarming(报警)。只有 Stopped 状态的流程才能修改结构。"
  },
  "workflow": [
    "⚠️ 操作分三类，切勿混淆：",
    "A. 流程级操作（新增/删除/复制/重排整个流程）：用 create_proc/delete_procs/reorder_proc/copy_proc（不传 previewId 预演 → 传 previewId 提交）。preview_patch 不支持新增流程！",
    "B. Patch 级操作（修改已有流程的步骤/指令）：用 preview_patch/apply_patch，actions 见 patchActions 字段",
    "C. 运行控制（启动/停止/暂停/恢复）：用 start_proc/stop_proc/pause_proc/resume_proc 直接执行，无需预演",
    "",
    "1. list_procs 或 get_proc_overview 定位目标流程",
    "2. get_proc_detail 读取完整结构（含 flow 字段标注执行流向、gotoWarnings 标注越界跳转）",
    "3. 已知operaType时直接用get_operation_schema读取该类型字段；仅在语义或约束不明确时读取该类型get_operation_guide，仅在Schema包含资源引用且候选值未知时读取get_reference_catalog",
    "4. list_intent_templates / get_intent_template 读取中间意图模板（若未找到模板，改用 preview_patch 直接构建）",
    "5. preview_intent 预演，或 build_patch_from_intent 后再 preview_patch",
    "6. Automation 前台确认 previewId 后，apply_intent 或 apply_patch 携带同一个 previewId 提交",
    "7. 正式提交前用get_snapshot确认目标流程为Stopped；非Stopped时不得调用stop_proc，不得重复提交，应告知用户并等待操作员停止流程",
    "细颗粒度读取：get_operation_detail 查单条指令、get_step_detail 查单步骤、search_operations 按类型/关键词搜索指令",
    "结构验证：validate_proc 修改前后快速检查跳转目标有效性和空步骤/指令，diagnose_proc 含运行时状态的完整诊断"
  ],
  "preferredWritePath": [
    "新增流程：create_proc（不传 previewId 预演）-> 等待确认 previewId -> create_proc（携带 previewId 提交）-> (可选) preview_patch 添加步骤/指令",
    "基于现有流程改造：copy_proc 复制（不传 previewId 预演）-> copy_proc（携带 previewId 提交）-> preview_patch 修改 -> apply_patch 提交",
    "优先使用中间意图：get_intent_template -> preview_intent -> 等待确认 previewId -> apply_intent",
    "仅在已经有标准 patchJson 时再直接调用 preview_patch -> 等待确认 previewId -> apply_patch",
    "流程运行控制：start_proc/stop_proc/pause_proc/resume_proc 直接执行，不需要预演确认"
  ],
  "patchActions": [
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
  "procManagementActions": [
    "create_proc",
    "delete_procs",
    "reorder_proc",
    "copy_proc"
  ],
  "controlActions": [
    "start",
    "stop",
    "pause",
    "resume"
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
    "字段值必须匹配 get_operation_schema.fields[].jsonType/valueShape；referenceType 字段若 dataType 是 string，编号也必须写成 JSON 字符串，例如 AlarmInfoID 用 \"0\" 而不是 0",
    "不要在未读取 schema 的情况下猜字段名或枚举值",
    "actions[] 中的动作键必须使用 type，禁止使用旧字段 action",
    "update_*_fields 必须使用 fieldChanges，insert/append_operation 需要初始字段时必须使用 fieldValues，禁止使用旧字段 fields",
    "apply_patch 必须复用原始 patch，不能把 preview_patch 返回结果里的 changes 直接当作 actions 提交",
    "preview_intent/preview_patch 会返回 previewId 和 patchHash，提交必须携带 Automation 前台已确认的 previewId",
    "未预演、预演未确认、Patch 与预演不一致、流程版本变化都会导致 apply 失败",
    "delete/move/insert 会触发 Automation Bridge 自动重写同流程内的跳转地址",
    "move_step/move_operation 的 targetIndex 表示移除源项后的最终索引",
    "apply_patch 前必须先调用 preview_patch 并等待 Automation 前台确认",
    "流程结构操作（create_proc/delete_procs/reorder_proc/copy_proc）需先预演（不传 previewId）再提交（传 previewId），提交时携带已确认的 previewId",
    "apply_intent/apply_patch仅允许目标流程Stopped；PROC_NOT_STOPPED表示本次无保存、无发布、无停机副作用，AI不得调用stop_proc或立即重试",
    "delete_procs/reorder_proc提交要求全部流程Stopped；PROC_STRUCTURE_NOT_STOPPED时AI不得调用stop_proc，必须等待操作员处理",
    "start_proc/stop_proc/pause_proc/resume_proc 不需要预演确认，直接发送命令",
    "reorder_proc 的 targetIndex 是移动后的最终索引，必须在当前流程数量范围内"
  ]
}
""";
            ToolCallLogger.Log(nameof(GetPatchContract), new { }, contract);
            return contract;
        }

        // 把 parameters 字符串解析为 JsonObject，供保留的合并工具（op_meta/list_resources）透传给 Bridge 的 params 字段。
        private static JsonObject ParseParameters(string? parameters)
        {
            if (string.IsNullOrEmpty(parameters)) return new JsonObject();
            try
            {
                JsonNode? node = JsonNode.Parse(parameters);
                if (node is JsonObject obj)
                {
                    return obj;
                }

                throw new ArgumentException("parameters 必须是 JSON 对象。");
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("parameters 不是合法 JSON。", ex);
            }
        }

        private static async Task<string> ExecuteAsync(string toolName, object args, Func<AutomationBridgeClient, Task<string>> action)
        {
            string callId = ToolCallLogger.Begin(toolName, args);
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string result = await action(AutomationMcpRuntime.GetBridgeClient()).ConfigureAwait(false);
                stopwatch.Stop();
                ToolCallLogger.Complete(callId, toolName, args, result, durationMs: stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                string error = $$"""
{"ok":false,"type":"mcp.error","errorCode":"TOOL_EXCEPTION","message":"{{toolName}} 调用异常","exceptionType":"{{Escape(ex.GetType().Name)}}","details":"{{Escape(ex.Message)}}"}
""";
                ToolCallLogger.Complete(callId, toolName, args, string.Empty, ex.ToString(), stopwatch.ElapsedMilliseconds);
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
