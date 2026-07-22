using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Automation.Protocol;
// 模块：MCP / Automation 工具入口。
// 职责范围：提供强类型参数、短描述和 Bridge 调用；工具是否公开以 McpToolProfile 为准。
// 排查入口：模型看不到工具查 Profile；参数错误查 DTO/Schema；业务错误查 Bridge 结构化 recovery。

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
            "Automation 源码开发任务的按需知识入口。仅当用户明确要求修改 HMI、调用平台公开 API 或编写自定义函数时使用；流程、变量、IO、通讯等平台配置任务不需要该上下文。已知开发目标直接传对应 topic。"
            + "响应会返回当前运行项目的精确 HMI 源码目录、公开 API 入口和隔离编译命令。验证不执行候选代码，也不覆盖当前 Debug 程序。")]
        public static string GetPlatformDevelopmentContext(
            [Description("主题：hmi/platform-api/custom-function；仅目标不明确时使用 catalog")] string topic)
        {
            string result = PlatformDevelopmentContextCatalog.Get(topic);
            ToolCallLogger.Log(nameof(GetPlatformDevelopmentContext), new { topic }, result);
            return result;
        }

        [McpServerTool(Name = "get_process_design_guide"), Description(
            "按当前复杂流程设计目标读取精确主题。适用于创建、重构或评审包含机械反馈、完整控制流、子流程事务、通讯重试、异常恢复或自定义函数边界的流程。"
            + "主题路由中mechanical对应IO、气缸、真空和运动反馈，review对应设计前、中、后审查。"
            + "简单赋值、固定延时、单IO操作和已知对象的字段级编辑不需要本指南。返回Automation流程设计方法，不提供具体字段、资源名称或运行参数；这些事实仍从当前Schema、Guide和资源工具读取。")]
        public static string GetProcessDesignGuide(
            [Description("流程设计主题数组，只传当前任务涉及的主题：architecture=目标与层级；mechanical=IO、气缸、真空和运动反馈；control-flow=分支、循环与终止；transaction=外部事务；recovery=超时与失败恢复；custom-function=函数边界；templates=常见流程骨架；review=设计前、中、后审查")] string[] topics)
        {
            string result = ProcessDesignGuideCatalog.Get(topics);
            ToolCallLogger.Log(nameof(GetProcessDesignGuide), new { topics }, result);
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
            + "服务端先计算流程体积：不超过100条指令且序列化详情不超过64KB时，返回完整详情"
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

        [McpServerTool(Name = "get_flow_graph"), Description(
            "读取平台确定性流程图。Project 返回流程间启动、停止、等待和动态目标关系；Process 返回单流程的顺序、分支、报警、结束、回环、不可达与无效目标。"
            + "节点和边来自当前已提交配置及运行行为契约，AI只能据此解释，不能把推测当成真实连线。"
            + "Process 必须提供 procIndex；Project 省略 procIndex。超出模型上下文单对象边界时返回实测体积、步骤目录和局部读取入口。")]
        public static async Task<string> GetFlowGraph(
            [Description("图范围：Project=项目总览，Process=单流程明细")] FlowGraphScope scope,
            [Description("Process 范围必填的流程索引；Project 范围省略")] int? procIndex = null)
        {
            if (scope == FlowGraphScope.Process && !procIndex.HasValue)
            {
                throw new ArgumentException("Process 范围必须提供 procIndex。", nameof(procIndex));
            }
            if (scope == FlowGraphScope.Project && procIndex.HasValue)
            {
                throw new ArgumentException("Project 范围不接受 procIndex。", nameof(procIndex));
            }
            return await ExecuteAsync(
                toolName: nameof(GetFlowGraph),
                args: new { scope, procIndex },
                action: client => client.GetFlowGraphAsync(scope, procIndex)).ConfigureAwait(false);
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
            + "返回每条指令当前实际的 stepIndex、stepId、opIndex、可写fields和执行流向；合计超过64KB时减少opIds重试。")]
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
            + "小步骤返回完整fields；结果超过64KB时改为轻量指令目录并给出opId，再用get_op_details精确读取。"
            + "大步骤目录分页返回，默认100条；使用nextOpOffset继续。若只需若干已知指令，优先使用 get_op_details。")]
        public static async Task<string> GetStepDetail(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex,
            [Description("步骤索引")] int stepIndex,
            [Description("大步骤轻量目录分页起点，默认0")] int? opOffset = null,
            [Description("大步骤轻量目录每页数量1..100，默认100")] int? opLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetStepDetail),
                args: new { procIndex, stepIndex, opOffset, opLimit },
                action: client => client.GetStepDetailAsync(
                    procIndex, stepIndex, opOffset, opLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "search_ops"), Description(
            "按条件分页搜索指令。procIndex为空时搜索全部流程；keyword匹配指令名和字段摘要。"
            + "默认返回50条、最多100条，并给出稳定opId供get_op_details精读。")]
        public static async Task<string> SearchOps(
            [Description("流程索引，为空则搜索全部流程")] int? procIndex = null,
            [Description("指令类型过滤，如 IO检测/延时")] string? operaType = null,
            [Description("关键词，匹配指令名和字段摘要")] string? keyword = null,
            [Description("命中结果分页起点，默认0")] int? offset = null,
            [Description("每页数量1..100，默认50")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchOps),
                args: new { procIndex, operaType, keyword, offset, limit },
                action: client => client.SearchOpsAsync(
                    procIndex, operaType, keyword, offset, limit)).ConfigureAwait(false);
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
            + "变量结果同时覆盖名称引用和索引引用，并返回归属流程及各引用流程的访问状态。可自动识别资源类型，同名资源属于多类时会同时查询并标记ambiguous。")]
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
            "分页查找哪些流程、步骤、指令和字段使用了指定变量，同时匹配唯一名称和全局索引引用，返回变量作用域、归属流程和各引用的访问状态；不返回流程全文。")]
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
            [Description("本批最多返回问题数1..100，默认50")] int? findingLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(AuditProcBatch),
                args: new { procOffset, procLimit, findingLimit },
                action: client => client.AuditProcBatchAsync(procOffset, procLimit, findingLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "diagnose_issue"), Description(
            "根据现场症状和流程位置生成分页诊断证据包，自动组合运行快照、严格结构校验、目标前后指令和运行黑匣子事件。"
            + "黑匣子默认返回40条、最多100条，使用nextEvidenceOffset继续；只读，不修改配置或运行状态。黑匣子事实与evidenceLimits应一并用于区分已验证事实和证据缺口。")]
        public static async Task<string> DiagnoseIssue(
            [Description("流程索引")] int procIndex,
            [Description("现场症状，最长300字符")] string? symptom = null,
            [Description("可选步骤索引；为空时使用运行快照当前位置")] int? stepIndex = null,
            [Description("可选指令索引；为空时使用运行快照当前位置")] int? opIndex = null,
            [Description("黑匣子证据分页起点，默认0；继续读取时使用nextEvidenceOffset")] int? evidenceOffset = null,
            [Description("本页黑匣子事件数1..100，默认40")] int? evidenceLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(DiagnoseIssue),
                args: new { procIndex, symptom, stepIndex, opIndex, evidenceOffset, evidenceLimit },
                action: client => client.DiagnoseIssueAsync(
                    procIndex, symptom, stepIndex, opIndex, evidenceOffset, evidenceLimit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_snapshot"), Description(
            "读取运行快照（流程状态/当前位置/报警/安全锁定）。"
            + "procIndex 为空时分页返回流程快照，默认50条、最多100条；使用nextOffset继续。用于了解当前运行状态。")]
        public static async Task<string> GetSnapshot(
            [Description("流程索引；为空时分页返回项目快照")] int? procIndex = null,
            [Description("项目快照分页起点，默认0；指定procIndex时省略")] int? offset = null,
            [Description("项目快照每页数量1..100，默认50；指定procIndex时省略")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetSnapshot),
                args: new { procIndex, offset, limit },
                action: client => client.GetSnapshotAsync(procIndex, offset, limit)).ConfigureAwait(false);
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
            + "返回真实terminationReason、outcome、是否观察到运行、位置变化、是否由测试器停止及本轮runtimeEvidence黑匣子时间线，由调用方结合用户目标判断结果。本次测试结果不授权再次启动；start_proc只用于用户明确要求持续运行的场景。")]
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

        [McpServerTool(Name = "get_info_log_tail"), Description(
            "读取运行信息页最近日志（排查报警/Bridge调用失败/流程运行异常）。"
            + "maxCount 范围1..100、默认30；服务端按64KB结果预算截断并返回省略数量。")]
        public static async Task<string> GetInfoLogTail(
            [Description("返回日志条数上限，范围1..100，默认30")] int? maxCount = null)
        {
            return await ExecuteAsync(
                toolName: nameof(GetInfoLogTail),
                args: new { maxCount },
                action: client => client.GetInfoLogTailAsync(maxCount)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "diagnose_proc"), Description(
            "诊断流程结构与运行风险（禁用/空步骤指令/未知指令类型/跳转错误/报警/断点）。"
            + "含运行时状态；问题分页返回，默认50条、最多100条。")]
        public static async Task<string> DiagnoseProc(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex,
            [Description("问题分页起点，默认0；继续读取时使用nextFindingOffset")] int? findingOffset = null,
            [Description("本页问题数1..100，默认50")] int? findingLimit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(DiagnoseProc),
                args: new { procIndex, findingOffset, findingLimit },
                action: client => client.DiagnoseProcAsync(
                    procIndex, findingOffset, findingLimit)).ConfigureAwait(false);
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
            "列出全部已配置变量（含稳定ID、作用域、归属流程、当前值、备注和引用影响）。"
            + "支持类型、作用域、归属流程、名称模糊匹配和分页；默认100条、每页最多100条。")]
        public static async Task<string> ListVariables(
            [Description("类型过滤：double 或 string")] string? type = null,
            [Description("名称模糊匹配关键词")] string? nameLike = null,
            [Description("作用域过滤：public、process 或 system")] string? scope = null,
            [Description("归属流程稳定ID过滤，仅用于 process 作用域")] string? ownerProcId = null,
            [Description("分页偏移，默认 0")] int? offset = null,
            [Description("分页上限1..100，默认100")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListVariables),
                args: new { type, nameLike, scope, ownerProcId, offset, limit },
                action: client => client.ListVariablesAsync(type, nameLike, scope, ownerProcId, offset, limit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_variable_by_name"), Description(
            "按全平台唯一名称读取一个变量，私有变量也无需提供所属流程；返回稳定ID、作用域、归属、索引和当前值。")]
        public static async Task<string> GetVariableByName(
            [Description("变量精确名称")] string name)
        {
            return await ExecuteAsync(
                toolName: nameof(GetVariableByName),
                args: new { name },
                action: client => client.GetVariableAsync(name, null)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_variable_by_index"), Description(
            "按全局唯一槽位索引读取一个变量，私有变量也无需提供所属流程；返回稳定ID、作用域、归属和当前值。")]
        public static async Task<string> GetVariableByIndex(
            [Description("变量槽位索引，范围" + VariableIndexContract.ValueIndexRange + "；"
                + VariableIndexContract.SystemValueIndexRange + "为系统变量区")] int index)
        {
            return await ExecuteAsync(
                toolName: nameof(GetVariableByIndex),
                args: new { index },
                action: client => client.GetVariableAsync(null, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "set_variable_by_name"), Description(
            "按全平台唯一名称修改变量当前值，不要求所属流程，不写配置文件；公共、私有和系统变量均可使用。")]
        public static async Task<string> SetVariableByName(
            [Description("变量精确名称")] string name,
            [Description("新当前值；double 类型填写数字文本")] string value)
        {
            return await ExecuteAsync(
                toolName: nameof(SetVariableByName),
                args: new { name, value },
                action: client => client.SetVariableAsync(value, name, null)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "set_variable_by_index"), Description(
            "按全局唯一槽位索引修改变量当前值，不要求所属流程，不写配置文件；公共、私有和系统变量均可使用。")]
        public static async Task<string> SetVariableByIndex(
            [Description("变量槽位索引，范围" + VariableIndexContract.ValueIndexRange + "；"
                + VariableIndexContract.SystemValueIndexRange + "为系统变量区")] int index,
            [Description("新当前值；double 类型填写数字文本")] string value)
        {
            return await ExecuteAsync(
                toolName: nameof(SetVariableByIndex),
                args: new { index, value },
                action: client => client.SetVariableAsync(value, null, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "delete_variable"), Description(
            "按精确名称删除普通变量区的一个变量，其他变量索引不移动，且要求所有流程已停止。"
            + "系统变量区配置对 AI 只读，不能通过此工具删除。")]
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
            + "name 和 scope 必填，名称全局唯一。scope=process 时 ownerProcId 必填；scope=public 时不携带 ownerProcId。"
            + "type 可为 double 或 string，默认 double；"
            + "value（可选，当前值，double类型必须是数字，默认\"0\"）；note（可选，备注）；"
            + "index（可选，只能指定普通变量槽位" + VariableIndexContract.NormalValueIndexRange
            + "；省略时自动分配第一个普通变量空槽位）。系统变量区配置对 AI 只读。"
            + "名称重复或槽位被占用时返回错误。创建后自动持久化并刷新界面。"
            + "每次只创建一个变量；需要多个变量时逐个调用。")]
        public static async Task<string> AddVariable(
            [Description("变量名（全局唯一）")] string name,
            [Description("作用域：public 或 process")] string scope,
            [Description("私有变量归属流程稳定ID；scope=process 时必填")] string? ownerProcId = null,
            [Description("类型：double 或 string，默认 double")] string? type = "double",
            [Description("当前值（double 类型必须是数字）")] string? value = null,
            [Description("备注")] string? note = null,
            [Description("指定普通变量槽位索引，范围" + VariableIndexContract.NormalValueIndexRange
                + "；不填则自动分配")] int? index = null)
        {
            if (index.HasValue
                && (index.Value < 0 || index.Value >= VariableIndexContract.NormalValueCapacity))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index.Value,
                    $"add_variable 的 index 必须位于普通变量区 {VariableIndexContract.NormalValueIndexRange}。");
            }
            return await ExecuteAsync(
                toolName: nameof(AddVariable),
                args: new { name, scope, ownerProcId, type, value, note, index },
                action: client => client.AddVariableAsync(
                    name, scope, ownerProcId, type ?? "double", value, note, index)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "update_variable"), Description(
            "修改一个现有变量的配置，要求所有流程已停止。"
            + "按当前精确名称定位；只提供需要变更的字段。value修改当前值；"
            + "可通过 scope、ownerProcId 和 index 移动作用域、归属流程或普通变量槽位，稳定ID和当前值保持不变。"
            + "只修改当前值且不保存配置时使用set_variable_by_name。"
            + "系统变量区配置对 AI 只读，不能通过此工具修改。")]
        public static async Task<string> UpdateVariable(
            [Description("当前变量精确名称")] string name,
            [Description("新名称；不修改则省略")] string? newName = null,
            [Description("新类型：double 或 string；不修改则省略")] string? type = null,
            [Description("新当前值；不修改则省略")] string? value = null,
            [Description("新备注；传空字符串可清空，不修改则省略")] string? note = null,
            [Description("新作用域：public 或 process；不修改则省略")] string? scope = null,
            [Description("新归属流程稳定ID；目标scope=process时必填")] string? ownerProcId = null,
            [Description("新普通变量槽位索引；不修改则省略")] int? index = null)
        {
            return await ExecuteAsync(
                toolName: nameof(UpdateVariable),
                args: new { name, newName, type, value, note, scope, ownerProcId, index },
                action: client => client.UpdateVariableAsync(
                    name, newName, type, value, note, scope, ownerProcId, index)).ConfigureAwait(false);
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
            "完全权限下的配置快照读取入口。domain为motion_io、io_debug、plc或communication；返回definition，结构与对应preview工具的definition参数一致，可直接修改后预演。")]
        public static async Task<string> GetMigrationConfiguration(
            [Description("配置领域：motion_io/io_debug/plc/communication")] string domain)
        {
            return await ExecuteAsync(
                toolName: nameof(GetMigrationConfiguration),
                args: new { domain },
                action: client => client.GetMigrationConfigurationAsync(domain)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "preview_motion_io_configuration"), Description(
            "预演控制卡、轴与IO映射的完整目标配置。这些对象存在索引耦合，因此同一事务保存；仅完全权限开放。")]
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
            "预演IO调试界面的输入、输出和三组关联显示配置。所有名称必须引用现有IO；仅完全权限开放。")]
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
            "预演PLC设备及其映射的完整目标配置。映射变量必须已存在；仅完全权限开放。")]
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
            "预演TCP与串口的完整目标配置，两份配置同一事务保存；仅完全权限开放。")]
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
            "提交一个已由前台确认的冻结配置预演，只接收previewId。完全权限开关不等于自动批准。")]
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
            "分页列出 IO 目录（含名称/卡号/模块/索引/类型/电平和备注摘要），默认50条、最多100条。"
            + "IO 类型为\"通用输入\"或\"通用输出\"；精确完整配置使用get_io。")]
        public static async Task<string> ListIo(
            [Description("类型过滤：通用输入 或 通用输出")] string? type = null,
            [Description("名称模糊匹配关键词")] string? nameLike = null,
            [Description("分页起点，默认0")] int? offset = null,
            [Description("每页数量1..100，默认50")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ListIo),
                args: new { type, nameLike, offset, limit },
                action: client => client.ListIoAsync(type, nameLike, offset, limit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_io"), Description(
            "按精确名称读取单个 IO 配置信息；名称已知时直接使用本工具。返回ioType/usedType/effectLevel/note等配置事实；它们不自动定义机构的安全位或工作位，部件目标与原位/动位反馈关系以明确设备契约为准。")]
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
            + "keyword按普通文本匹配IO名称；省略、空字符串或*分页返回全部IO，默认50条、最多100条。")]
        public static async Task<string> SearchIo(
            [Description("名称关键词；省略、空字符串或*表示全部")] string? keyword = null,
            [Description("类型过滤：通用输入 或 通用输出")] string? type = null,
            [Description("卡号过滤")] int? cardNum = null,
            [Description("分页起点，默认0")] int? offset = null,
            [Description("每页数量1..100，默认50")] int? limit = null)
        {
            return await ExecuteAsync(
                toolName: nameof(SearchIo),
                args: new { keyword, type, cardNum, offset, limit },
                action: client => client.SearchIoAsync(
                    keyword, type, cardNum, offset, limit)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_io_state"), Description(
            "读取单个 IO 的运行时逻辑状态。true表示该精确IO逻辑激活，false表示逻辑未激活，null表示读取失败；它不统一表示电气高低电平、安全位或工作位。"
            + "通用输入读取传感器条件，通用输出读取当前输出逻辑状态；机构语义由部件目标及对应反馈关系确定。需硬件已就绪。")]
        public static async Task<string> GetIoState(
            [Description("IO 名称")] string name)
        {
            return await ExecuteAsync(
                toolName: nameof(GetIoState),
                args: new { name },
                action: client => client.GetIoStateAsync(name)).ConfigureAwait(false);
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
