using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Automation.Protocol;

namespace Automation.McpServer
{
    [McpServerToolType]
    public static class AutomationMcpTools
    {
        [McpServerTool(Name = "get_native_operation_schemas"), Description(
            "按当前配置阶段实际使用的精确原生operaType批量读取递归字段契约，供operation.kind=native.operation使用。返回common公共契约与各类型差量，合并后填写；critical优先列出运行必填及业务跳转。已在会话中验证且契约未变化的类型可复用；后续阶段出现新类型时再读取。适用于精确复刻或语义kind无法表达的指令，资源候选按字段需要另行查询。")]
        public static async Task<string> GetOperationSchemas(
            [Description("精确原生指令类型数组，例如 跳转、延时、修改变量")] string[] operaTypes)
        {
            return await ExecuteAsync(
                toolName: nameof(GetOperationSchemas),
                args: new { operaTypes },
                action: client => client.GetNativeOperationContractsAsync(operaTypes)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_semantic_operation_schema"), Description(
            "读取一个精确语义kind的保存必填项、运行必填项和行为契约。基础字段已在preview_change_set参数Schema中公开，仅在当前选定kind需要补充行为细节时调用。")]
        public static async Task<string> GetSemanticOperationSchema(
            [Description("一个精确语义kind，取值来自preview_change_set参数Schema中的支持列表")] string kind)
        {
            return await ExecuteAsync(
                toolName: nameof(GetSemanticOperationSchema),
                args: new { kind },
                action: client => client.GetSemanticOperationContractsAsync(new[] { kind })).ConfigureAwait(false);
        }

        [McpServerTool(Name = "preview_change_set"), Description(
            "预演一个可独立保存、原子提交的ChangeSet V2配置阶段。现有对象使用稳定ID，当前阶段的新对象使用局部key；插入和移动使用锚点定位。"
            + "当前流程阶段依赖的新变量通过variables逐项声明，与actions同事务预演；独立变量维护使用单变量工具。"
            + "指令字段遵循所选语义或精确原生Schema。返回configurationSaved、objectState、localKeyScope、variableResolutions、配置就绪事实和合法状态迁移；提交前的新对象仍为preview_only。"
            + "新预演不继承旧预演的动作或局部key；replacePreviewId只标识被完整修正版替换的活动预演，不能代替完整changeSet参数。")]
        public static async Task<string> PreviewChangeSet(
            [Description("当前原子阶段；actions按依赖顺序执行，variables逐项声明同阶段依赖变量，两者整体预演")] AtomicChangeSetDefinition changeSet,
            [Description("可选；显式指定被完整修正版替换的未提交previewId。无论是否省略，新changeSet都必须自包含，不继承旧预演动作或局部key")] string? replacePreviewId = null)
        {
            if (changeSet == null) throw new ArgumentNullException(nameof(changeSet));
            if ((changeSet.Actions?.Count ?? 0) == 0 && (changeSet.Variables?.Count ?? 0) == 0)
                throw new ArgumentException("changeSet 至少包含一个动作或变量声明。", nameof(changeSet));
            var compiledInput = new AiChangeSet
            {
                Version = 2,
                Title = changeSet.Title,
                Actions = changeSet.Actions,
                Variables = changeSet.Variables
            };
            string validationError = AiChangeSetCatalog.Validate(compiledInput);
            return await ExecuteAsync(
                toolName: nameof(PreviewChangeSet),
                args: new { changeSet, replacePreviewId },
                action: client =>
                {
                    if (validationError != null)
                        throw new ArgumentException(validationError, nameof(changeSet));
                    return client.PreviewChangeSetAsync(compiledInput, replacePreviewId);
                }).ConfigureAwait(false);
        }

        [McpServerTool(Name = "apply_change_set"), Description(
            "提交一个已由前台确认的冻结V2预演，只接收previewId。平台在事务内校验版本并保存，正式提交要求所有流程为Stopped；成功结果以configurationSaved=true确认已保存，并通过createdObjects、affectedProcesses和variableResolutions返回稳定身份与变量处理事实。")]
        public static async Task<string> ApplyChangeSet(
            [Description("preview_change_set 返回且已由前台确认的32位 previewId")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(ApplyChangeSet),
                args: new { previewId },
                action: client => client.ApplyChangeSetAsync(previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "discard_change_set_preview"), Description(
            "结束一个尚未提交的冻结ChangeSet V2预演，不修改配置。用于释放待改写或不再需要的预演；已提交阶段不受影响。")]
        public static async Task<string> DiscardChangeSetPreview(
            [Description("preview_change_set 返回且尚未apply的32位 previewId")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(DiscardChangeSetPreview),
                args: new { previewId },
                action: client => client.DiscardChangeSetPreviewAsync(previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_platform_development_context"), Description(
            "Automation 源码开发任务的按需知识入口。仅当用户明确要求修改 HMI、调用平台公开 API 或编写自定义函数时使用；流程、变量、IO、通讯等平台配置任务不需要该上下文。已知开发目标直接传对应 topic。")]
        public static string GetPlatformDevelopmentContext(
            [Description("主题：hmi/platform-api/custom-function；仅目标不明确时使用 catalog")] string topic)
        {
            string result = PlatformDevelopmentContextCatalog.Get(topic);
            ToolCallLogger.Log(nameof(GetPlatformDevelopmentContext), new { topic }, result);
            return result;
        }

        [McpServerTool(Name = "list_procs"), Description(
            "列出所有流程的基础信息（procIndex/procId/name/autoStart/disable/state/stepCount）。"
            + "同名流程通过procId或procIndex区分。"
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
            "读取已提交流程的摘要视图（步骤/指令摘要/稳定标识/当前就绪状态）。参数使用提交结果affectedProcesses中的procIndex；preview_only的plannedProcIndex尚不属于可读流程。"
            + "比 get_proc_detail 轻量，适合快速了解结构和runnable/runBlockers；摘要不会返回全部原生字段，不能证明字段级一致性。")]
        public static async Task<string> GetProcOverview(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(GetProcOverview),
                args: new { procIndex },
                action: client => client.GetProcOverviewAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_proc_detail"), Description(
            "读取已提交流程。参数使用提交结果affectedProcesses中的procIndex；preview_only对象在apply后才成为可读流程。"
            + "服务端先计算流程体积：不超过100条指令且序列化详情不超过256KB时，返回完整详情"
            + "（head/steps/ops/fields，含 isJump/flow/gotoWarnings）；超限时只返回流程规模和轻量步骤目录。"
            + "需要核对、复现或转换已有对象的字段值时，以本工具返回的fields作为字段级证据；get_proc_overview只适合结构摘要。"
            + "超限结果会给出适合继续读取的步骤目录，可按目标改用get_step_detail或get_op_details。"
            + "返回的 flow 字段标注每条指令执行后的流向（opIndex+1 或跳转目标），"
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
            + "fields严格使用native.operation可写结构，可按需取其中字段直接用于operation.update；"
            + "仅用于细粒度检查一条已知指令；解释完整流程应改用 get_proc_detail，避免手工组合多组索引。")]
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

        [McpServerTool(Name = "get_op_details"), Description(
            "按明确的 opId 有限批量读取指令详情，单次最多25条。"
            + "适合从同一流程摘要中选择若干唯一opId后一次读取。"
            + "返回每条指令当前实际的 stepIndex、stepId、opIndex、可写fields和执行流向。")]
        public static async Task<string> GetOpDetails(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex,
            [Description("1到25个唯一指令Guid；必须来自该流程的 get_proc_overview、get_proc_detail 或 get_step_detail 返回值")] string[] opIds)
        {
            return await ExecuteAsync(
                toolName: nameof(GetOpDetails),
                args: new { procIndex, opIds },
                action: client => client.GetOpDetailsAsync(procIndex, opIds)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_step_detail"), Description(
            "读取单步骤完整指令列表（含每条指令 flow）。"
            + "用于查看一个明确步骤；若只需若干已知指令，优先使用 get_op_details。")]
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

        [McpServerTool(Name = "get_operation_references"), Description(
            "查询一条明确指令的完整跳转关系。以稳定opId定位目标，返回目标指令所有出向跳转，以及跨流程分页扫描得到的全部入向跳转；"
            + "不会受邻近读取窗口限制，适合发现相隔很远或位于其他步骤的跳转来源。")]
        public static async Task<string> GetOperationReferences(
            [Description("目标流程索引")] int procIndex,
            [Description("目标指令Guid，必须来自流程读取结果")] string opId,
            [Description("扫描来源流程起点，默认0；继续扫描时使用nextProcOffset")] int? procOffset = null,
            [Description("本批扫描流程数1..50，默认20")] int? procLimit = null,
            [Description("本批最多返回入向跳转数1..100，默认50")] int? resultLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetOperationReferences),
                args: new { procIndex, opId, procOffset, procLimit, resultLimit },
                action: client => client.GetOperationReferencesAsync(
                    procIndex, opId, procOffset, procLimit, resultLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_proc_references"), Description(
            "查询一条明确流程被哪些指令引用。返回流程操作/等待流程等直接流程引用，以及所有跳入该流程的地址引用；按来源流程分页，不返回流程全文。")]
        public static async Task<string> GetProcReferences(
            [Description("目标流程索引")] int procIndex,
            [Description("扫描来源流程起点，默认0；继续扫描时使用nextProcOffset")] int? procOffset = null,
            [Description("本批扫描流程数1..50，默认20")] int? procLimit = null,
            [Description("本批最多返回引用数1..100，默认50")] int? resultLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetProcReferences),
                args: new { procIndex, procOffset, procLimit, resultLimit },
                action: client => client.GetProcReferencesAsync(
                    procIndex, procOffset, procLimit, resultLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "trace_resource"), Description(
            "按项目中的业务资源名称追踪使用位置，递归检查指令参数列表。优先用于“这个变量/IO/TCP或串口通讯/工站/PLC/数据结构/报警在哪里用过”；"
            + "可自动识别资源类型，同名资源属于多类时会同时查询并标记ambiguous。")]
        public static async Task<string> TraceResource(
            [Description("资源精确名称；报警使用编号文本，例如12")] string name,
            [Description("可选类型:auto/variable/io/communication/tcp/serial/station/plc/dataStruct/alarm，默认auto")] string? resourceKind = null,
            [Description("流程扫描起点，默认0")] int? procOffset = null,
            [Description("本批扫描流程数1..50，默认20")] int? procLimit = null,
            [Description("本批最多返回命中数1..100，默认50")] int? resultLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(TraceResource),
                args: new { name, resourceKind, procOffset, procLimit, resultLimit },
                action: client => client.TraceResourceAsync(name, resourceKind, procOffset, procLimit, resultLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_communication"), Description(
            "按精确名称读取一个 TCP 或串口通讯对象的配置和当前状态。已知通讯名时直接调用，不需要先列出全部通讯；同名跨类型时再指定kind。")]
        public static async Task<string> GetCommunication(
            [Description("通讯对象精确名称")] string name,
            [Description("可选 tcp 或 serial；名称唯一时省略")] string? kind = null,
            [Description("是否包含当前运行状态，默认true")] bool? includeStatus = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetCommunication),
                args: new { name, kind, includeStatus },
                action: client => client.GetCommunicationAsync(name, kind, includeStatus)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_plc_devices"), Description(
            "列出PLC设备目录和当前运行状态。仅在设备名称未知、需要发现候选设备时调用；不会返回映射明细。")]
        public static async Task<string> ListPlcDevices()
        {
            return await ExecuteAsync(
                toolName: nameof(ListPlcDevices),
                args: new { },
                action: client => client.ListPlcDevicesAsync()).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_plc_device"), Description(
            "按精确名称读取一个PLC设备的配置和当前状态。已知DeviceName时直接调用；PLC读写只需设备配置，分析PLC映射控制或现有映射时再包含映射明细。")]
        public static async Task<string> GetPlcDevice(
            [Description("PLC设备精确名称，对应PLC指令的DeviceName")] string name,
            [Description("是否包含该设备的映射明细，默认false")] bool? includeMaps = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetPlcDevice),
                args: new { name, includeMaps },
                action: client => client.GetPlcDeviceAsync(name, includeMaps)).ConfigureAwait(false);
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
            "跨流程精确查找资源引用。用于回答“哪些流程使用了这个变量/IO/报警/工站”。服务端按流程分页，返回精确到流程/步骤/指令/字段的位置和下一批游标。referenceType常用值：value、io.input、io.output、io.all、alarm.infoId、station、plc.device、dataStruct、proc。")]
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
            "读取故障指令附近的小窗口。只返回目标指令完整字段，邻近指令仅返回摘要，适合排查局部顺序执行。"
            + "完整跳转关系由get_operation_references按目标opId返回。")]
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

        [McpServerTool(Name = "diagnose_issue"), Description(
            "根据现场症状和流程位置生成有上限的诊断证据包，自动组合运行快照、严格结构校验和目标前后指令。只读，不修改配置。")]
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

        [McpServerTool(Name = "wait_for_proc_state"), Description(
            "在 Bridge 内长轮询等待单个流程到达目标状态；只报告是否到达、是否超时和真实快照，不推测流程能否自然结束。"
            + "需要等待状态变化时一次调用本工具即可；到达 Alarming 或超时后可按需读取诊断信息。")]
        public static async Task<string> WaitForProcState(
            [Description("流程索引；优先使用 apply_change_set.affectedProcesses 返回值")] int procIndex,
            [Description("目标状态，默认 Stopped/Alarming；可选 Stopped/Running/Paused/Alarming/Stopping")] string[]? states = null,
            [Description("等待超时100..60000ms，默认30000ms")] int? timeoutMs = null)
        {
            return await ExecuteAsync(
                toolName: nameof(WaitForProcState),
                args: new { procIndex, states, timeoutMs },
                action: client => client.WaitForProcStateAsync(procIndex, states, timeoutMs)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "run_proc_test"), Description(
            "仅用于用户本轮明确要求测试或试运行的场景；只要求创建或修改配置时，以预演和validate_proc作为完成证据。独立执行一次有边界的流程测试：直接传入Stopped流程，本工具负责启动、观察和安全停止；已经运行的流程不会被接管。观察窗口500..15000ms，自然结束则直接返回。"
            + "只返回真实terminationReason、outcome、是否观察到运行、位置变化和是否由测试器停止，由调用方结合用户目标判断结果。本次测试结果不授权再次启动；start_proc只用于用户明确要求持续运行的场景。")]
        public static async Task<string> RunProcTest(
            [Description("处于Stopped的流程索引；优先使用 apply_change_set.affectedProcesses 返回值")] int procIndex,
            [Description("观察窗口500..15000ms，默认5000ms")] int? durationMs = null)
        {
            return await ExecuteAsync(
                toolName: nameof(RunProcTest),
                args: new { procIndex, durationMs },
                action: client => client.RunProcTestAsync(procIndex, durationMs)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "list_operation_types"), Description(
            "列出平台实际注册的原生operaType，用于native.operation的类型发现。已知精确operaType时直接读取其原生Schema。")]
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
            "按精确原生operaType读取同一行为契约源。coverage=specialized时返回已建模的运行行为、字段联动和失败条件；coverage=unknown时不提供控制流结论。语义kind的行为由语义Schema返回。")]
        public static async Task<string> GetOperationGuide(
            [Description("精确指令类型，例如IO检测、逻辑判断、工站运行")] string operaType)
        {
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
            return await ExecuteAsync(
                toolName: nameof(OpMeta),
                args: new { action, parameters },
                action: client => client.OpMetaAsync(action, ParseParameters(parameters))).ConfigureAwait(false);
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
            + "当前存在待审核预演时禁止继续创建新预演，必须等待用户确认或拒绝。"
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
            + "fieldValues/fieldChanges 必须严格保持 get_operation_schema 返回的 JSON 类型：number 必须传数值且禁止加引号，boolean 必须传 true/false，禁止把字符串数字当作数值。"
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

        [McpServerTool(Name = "create_proc_batch"), Description(
            "一次构建完整新流程（流程+步骤+全部指令），仅做一次预演和前台确认。"
            + "definition 必须直接传JSON对象，禁止传包含JSON文本的字符串；必须包含name/steps，steps包含name/operations，operations包含operaType和可选fieldValues。"
            + "预演阶段省略 previewId；确认后使用完全相同的 definition 和 previewId 再调用一次提交。"
            + "Bridge会分配ID、严格校验并原子保存。")]
        public static async Task<string> CreateProcBatch(
            [Description("完整流程定义对象，必须直接传对象而不是JSON字符串")] CreateProcBatchDefinition definition,
            [Description("提交阶段必填：预演返回的 previewId；预演阶段省略")] string? previewId = null)
        {
            string? validationError = CreateProcBatchDefinitionValidator.Validate(definition);
            if (validationError != null)
            {
                string result = JsonSerializer.Serialize(new
                {
                    ok = false,
                    type = "mcp.error",
                    errorCode = "BATCH_DEFINITION_INVALID",
                    message = validationError
                });
                ToolCallLogger.Log(nameof(CreateProcBatch), new { definition, previewId }, result);
                return result;
            }
            return await ExecuteAsync(
                toolName: nameof(CreateProcBatch),
                args: new { definition, previewId },
                action: client => client.CreateProcBatchAsync(definition, previewId)).ConfigureAwait(false);
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
            "启动流程并让它按自身生命周期持续运行。不需要配置预演，要求流程处于Stopped且通过运行就绪闸门；用于用户明确要求启动或持续运行的场景。"
            + "创建、修改后的有边界试运行、观察后停止和终止原因验证由run_proc_test一次完成。")]
        public static async Task<string> StartProc(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex)
        {
            return await ExecuteAsync(
                toolName: nameof(StartProc),
                args: new { procIndex },
                action: client => client.StartProcAsync(procIndex)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "stop_proc"), Description(
            "按用户运行控制意图停止一个非Stopped流程，直接发送命令且无需配置预演。配置提交遇到运行中流程时由操作员决定是否停止。")]
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

        [McpServerTool(Name = "get_variable_by_name"), Description(
            "按精确名称读取一个变量，返回索引、类型、运行值、配置初始值、备注和变更历史。")]
        public static async Task<string> GetVariableByName(
            [Description("变量精确名称")] string name)
        {
            return await ExecuteAsync(
                toolName: nameof(GetVariableByName),
                args: new { name },
                action: client => client.GetVariableAsync(name, null)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_variable_by_index"), Description(
            "按固定槽位索引读取一个变量，返回名称、类型、运行值、配置初始值、备注和变更历史。")]
        public static async Task<string> GetVariableByIndex(
            [Description("变量槽位索引，范围0..999")] int index)
        {
            return await ExecuteAsync(
                toolName: nameof(GetVariableByIndex),
                args: new { index },
                action: client => client.GetVariableAsync(null, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "set_variable_by_name"), Description(
            "按精确名称修改一个变量的运行时值，不写配置文件；double 类型会校验数字格式。")]
        public static async Task<string> SetVariableByName(
            [Description("变量精确名称")] string name,
            [Description("新运行值；double 类型填写数字文本")] string value)
        {
            return await ExecuteAsync(
                toolName: nameof(SetVariableByName),
                args: new { name, value },
                action: client => client.SetVariableAsync(value, name, null)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "set_variable_by_index"), Description(
            "按固定槽位索引修改一个变量的运行时值，不写配置文件；double 类型会校验数字格式。")]
        public static async Task<string> SetVariableByIndex(
            [Description("变量槽位索引，范围0..999")] int index,
            [Description("新运行值；double 类型填写数字文本")] string value)
        {
            return await ExecuteAsync(
                toolName: nameof(SetVariableByIndex),
                args: new { index, value },
                action: client => client.SetVariableAsync(value, null, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "delete_variable"), Description(
            "按精确名称删除一个变量，其他变量索引不移动。系统保留变量不能删除，且要求所有流程已停止。")]
        public static async Task<string> DeleteVariable(
            [Description("要删除的变量精确名称")] string name)
        {
            return await ExecuteAsync(
                toolName: nameof(DeleteVariable),
                args: new { name },
                action: client => client.DeleteVariableAsync(name)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "add_variable"), Description(
            "创建一个新变量，要求所有流程已停止。"
            + "参数：name（必填，变量名，全局唯一）；type（可选，\"double\"或\"string\"，默认double）；"
            + "initialValue（可选，配置初始值，double类型必须是数字，默认\"0\"）；note（可选，备注）；"
            + "index（可选，指定槽位位置[0,1000)，默认自动找第一个空槽位）。"
            + "名称重复或槽位被占用时返回错误。创建后自动持久化并刷新界面。"
            + "每次只创建一个变量；需要多个变量时逐个调用。")]
        public static async Task<string> AddVariable(
            [Description("变量名（全局唯一）")] string name,
            [Description("类型：double 或 string，默认 double")] string? type = "double",
            [Description("配置初始值（double 类型必须是数字）")] string? initialValue = null,
            [Description("备注")] string? note = null,
            [Description("指定槽位索引，不填则自动分配")] int? index = null)
        {
            return await ExecuteAsync(
                toolName: nameof(AddVariable),
                args: new { name, type, initialValue, note, index },
                action: client => client.AddVariableAsync(name, type ?? "double", initialValue, note, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "update_variable"), Description(
            "修改一个现有变量的配置，要求所有流程已停止。"
            + "按当前精确名称定位；只提供需要变更的字段。initialValue只修改配置初始值，不改变当前运行值。"
            + "系统保留变量不能修改。")]
        public static async Task<string> UpdateVariable(
            [Description("当前变量精确名称")] string name,
            [Description("新名称；不修改则省略")] string? newName = null,
            [Description("新类型：double 或 string；不修改则省略")] string? type = null,
            [Description("新配置初始值；不修改则省略")] string? initialValue = null,
            [Description("新备注；传空字符串可清空，不修改则省略")] string? note = null)
        {
            return await ExecuteAsync(
                toolName: nameof(UpdateVariable),
                args: new { name, newName, type, initialValue, note },
                action: client => client.UpdateVariableAsync(
                    name, newName, type, initialValue, note)).ConfigureAwait(false);
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

        [McpServerTool(Name = "search_data_struct_items"), Description(
            "在一个已知数据结构内搜索其数据项。"
            + "可按 item 名称/字符串字段值/数值字段范围过滤。")]
        public static async Task<string> SearchDataStructItems(
            [Description("已验证的数据结构精确名称")] string name,
            [Description("item 名称模糊匹配")] string? itemNameLike = null,
            [Description("字符串字段值模糊匹配")] string? strValueLike = null,
            [Description("数值字段下界（含）")] double? numValueMin = null,
            [Description("数值字段上界（含）")] double? numValueMax = null,
            [Description("返回上限")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchDataStructItems),
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

        [McpServerTool(Name = "upsert_data_struct"), Description(
            "新增或完整更新一个数据结构。只替换同名数据结构，不替换整张数据结构表；字段索引在该数据项内必须唯一，type为Text或Number。")]
        public static async Task<string> UpsertDataStruct(
            [Description("一个完整的数据结构定义；同名存在时更新，不存在时新增")] DataStructDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return await ExecuteAsync(
                toolName: nameof(UpsertDataStruct),
                args: definition,
                action: client => client.UpsertDataStructAsync(definition)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "delete_data_struct"), Description(
            "删除一个精确名称的数据结构；只影响该对象，不替换整张数据结构表。")]
        public static async Task<string> DeleteDataStruct(
            [Description("数据结构精确名称")] string name)
        {
            return await ExecuteAsync(
                toolName: nameof(DeleteDataStruct),
                args: new { name },
                action: client => client.DeleteDataStructAsync(name)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_migration_configuration"), Description(
            "迁移能力包的配置快照读取入口。domain为motion_io、io_debug、plc或communication；返回definition，结构与对应preview工具的definition参数一致，可直接修改后预演。")]
        public static async Task<string> GetMigrationConfiguration(
            [Description("配置领域：motion_io/io_debug/plc/communication")] string domain)
        {
            return await ExecuteAsync(
                toolName: nameof(GetMigrationConfiguration),
                args: new { domain },
                action: client => client.GetMigrationConfigurationAsync(domain)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "preview_motion_io_configuration"), Description(
            "预演控制卡、轴与IO映射的完整目标配置。这些对象存在索引耦合，因此同一事务保存；仅迁移能力包开放。")]
        public static async Task<string> PreviewMotionIoConfiguration(
            [Description("控制卡和IO映射的完整目标配置")] MotionIoMigrationDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return await ExecuteAsync(
                toolName: nameof(PreviewMotionIoConfiguration),
                args: definition,
                action: client => client.PreviewMotionIoConfigurationAsync(definition)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "preview_io_debug_configuration"), Description(
            "预演IO调试界面的输入、输出和三组关联显示配置。所有名称必须引用现有IO；仅迁移能力包开放。")]
        public static async Task<string> PreviewIoDebugConfiguration(
            [Description("IO调试显示和关联配置")] IoDebugMigrationDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return await ExecuteAsync(
                toolName: nameof(PreviewIoDebugConfiguration),
                args: definition,
                action: client => client.PreviewIoDebugConfigurationAsync(definition)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "preview_plc_configuration"), Description(
            "预演PLC设备及其映射的完整目标配置。映射变量必须已存在；仅迁移能力包开放。")]
        public static async Task<string> PreviewPlcConfiguration(
            [Description("PLC设备和地址映射配置")] PlcMigrationDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return await ExecuteAsync(
                toolName: nameof(PreviewPlcConfiguration),
                args: definition,
                action: client => client.PreviewPlcConfigurationAsync(definition)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "preview_communication_configuration"), Description(
            "预演TCP与串口的完整目标配置，两份配置同一事务保存；仅迁移能力包开放。")]
        public static async Task<string> PreviewCommunicationConfiguration(
            [Description("TCP和串口配置")] CommunicationMigrationDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return await ExecuteAsync(
                toolName: nameof(PreviewCommunicationConfiguration),
                args: definition,
                action: client => client.PreviewCommunicationConfigurationAsync(definition)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "apply_migration_configuration"), Description(
            "提交一个已由前台确认的冻结迁移配置预演，只接收previewId。迁移能力开关不等于自动确认。")]
        public static async Task<string> ApplyMigrationConfiguration(
            [Description("迁移配置预演返回的32位previewId")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(ApplyMigrationConfiguration),
                args: new { previewId },
                action: client => client.ApplyMigrationConfigurationAsync(previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "discard_migration_configuration"), Description(
            "结束一个未提交的迁移配置预演，不修改配置。")]
        public static async Task<string> DiscardMigrationConfiguration(
            [Description("迁移配置预演返回的32位previewId")] string previewId)
        {
            return await ExecuteAsync(
                toolName: nameof(DiscardMigrationConfiguration),
                args: new { previewId },
                action: client => client.DiscardMigrationConfigurationAsync(previewId)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "validate_platform_configuration"), Description(
            "对迁移涉及的控制卡、IO、IO调试、PLC和通讯配置执行确定性校验，返回各领域事实，不推测业务正确性。")]
        public static async Task<string> ValidatePlatformConfiguration()
        {
            return await ExecuteAsync(
                toolName: nameof(ValidatePlatformConfiguration),
                args: new { },
                action: client => client.ValidatePlatformConfigurationAsync()).ConfigureAwait(false);
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
            "按精确名称读取单个 IO 配置信息；名称已知时直接使用本工具。")]
        public static async Task<string> GetIo(
            [Description("IO 名称")] string name)
        {
            return await ExecuteAsync(
                toolName: nameof(GetIo),
                args: new { name },
                action: client => client.GetIoAsync(name)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "search_io"), Description(
            "目标名称未知时按名称关键词/类型/卡号发现 IO；名称已经确定时使用 get_io。"
            + "keyword 按普通文本匹配 IO 名称；省略、空字符串或*返回全部 IO。")]
        public static async Task<string> SearchIo(
            [Description("名称关键词；省略、空字符串或*表示全部")] string? keyword = null,
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
            "分页搜索报警配置，默认返回已配置项，每次最多100条，并返回total/offset/limit/hasMore/items。精确槽位使用get_alarm。")]
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
            "创建或更新报警信息。需 ProcessEdit 权限且所有流程已停止。"
            + "报警表固定 1000 个槽位，index 指定槽位位置；同名可直接更新，替换其他报警时必须显式设置allowOverwrite=true。"
            + "name与note构成完整报警资源；成功后立即持久化并刷新界面。该资源工具不属于ChangeSet预演。")]
        public static async Task<string> SetAlarm(
            [Description("报警槽位索引 [0,1000)")] int index,
            [Description("报警名称（必填，与 note 同时填写）")] string name,
            [Description("报警信息内容（必填，与 name 同时填写）")] string note,
            [Description("报警类别")] string? category = null,
            [Description("按钮1提示（对应\"确定\"）")] string? btn1 = null,
            [Description("按钮2提示（对应\"否\"）")] string? btn2 = null,
            [Description("按钮3提示（对应\"取消\"）")] string? btn3 = null,
            [Description("槽位被其他报警占用时是否明确允许替换，默认false")] bool? allowOverwrite = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SetAlarm),
                args: new { index, name, note, category, btn1, btn2, btn3, allowOverwrite },
                action: client => client.SetAlarmAsync(
                    index, name, note, category, btn1, btn2, btn3, allowOverwrite)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "delete_alarm"), Description(
            "清空指定槽位的报警信息。需 ProcessEdit 权限且所有流程已停止。"
            + "槽位索引保持不变，空槽位返回错误；成功后立即持久化并刷新界面。该资源工具不属于ChangeSet预演。")]
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
    "2. get_proc_detail 先由服务端计算体积；小型流程返回完整结构，超限则按步骤目录改用get_step_detail或get_op_details读取目标范围",
    "3. 已知operaType时直接用get_operation_schema读取该类型字段；仅在语义或约束不明确时读取该类型get_operation_guide，仅在Schema包含资源引用且候选值未知时读取get_reference_catalog",
    "4. list_intent_templates / get_intent_template 读取中间意图模板（若未找到模板，改用 preview_patch 直接构建）",
    "5. preview_intent 预演，或 build_patch_from_intent 后再 preview_patch",
    "6. Automation 前台确认 previewId 后，apply_intent 或 apply_patch 携带同一个 previewId 提交",
    "7. 正式提交前用get_snapshot确认目标流程为Stopped；非Stopped时不得调用stop_proc，不得重复提交，应告知用户并等待操作员停止流程",
    "细颗粒度读取：get_op_detail查单条已知指令、get_op_details按opId批量读取最多25条、get_step_detail查单步骤、search_ops按类型/关键词搜索指令",
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

        private static async Task<string> ExecuteAsync(string toolName, object args,
            Func<AutomationBridgeClient, Task<string>> action)
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
                if (ex is ArgumentException)
                {
                    string result = JsonSerializer.Serialize(new
                    {
                        ok = false,
                        type = "mcp.error",
                        errorCode = "INVALID_ARGUMENT",
                        message = ex.Message
                    });
                    ToolCallLogger.Complete(
                        callId, toolName, args, result, durationMs: stopwatch.ElapsedMilliseconds);
                    return result;
                }
                ToolCallLogger.Complete(callId, toolName, args, string.Empty, ex.ToString(), stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

    }
}
