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
        [McpServerTool(Name = "request_capability"), Description(
            "申请切换到一个下一能力包。该工具不读取平台、不修改配置、不运行流程；完成当前工作或需要用户补充时不要调用，直接正常回复。不能自行扩展用户的副作用授权。")]
        public static string RequestCapability(
            [Description("能力切换请求。action固定为run_stage并填写capability/objective；仅运行控制、平台配置和源码修改还必须在authorizationQuote逐字引用当前用户消息中的授权片段。第一条成功请求会被执行器锁定，调用后立即结束本轮")]
            TaskCapabilityDecisionDefinition decision)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            string result = JsonSerializer.Serialize(new
            {
                ok = true,
                type = "task_decision.submit",
                data = decision
            });
            ToolCallLogger.Log(nameof(RequestCapability), new { decision }, result);
            return result;
        }

        [McpServerTool(Name = "submit_review_handoff"), Description(
            "仅在ProcessReview中提交可供后续修复引用的结构化评审交接。宿主会把finding与本阶段成功读取的机械事实绑定；该工具不结束当前模型轮，提交后继续用正常回复结束，或另行申请下一能力。普通评审文字不必调用，未提交时宿主按unresolved保存。")]
        public static string SubmitReviewHandoff(
            [Description("结构化评审结论。只有proven_defect携带findings；每个finding必须引用本阶段工具结果中的机械事实键")]
            ReviewHandoffDefinition handoff)
        {
            if (handoff == null) throw new ArgumentNullException(nameof(handoff));
            string result = JsonSerializer.Serialize(new
            {
                ok = true,
                type = "review_handoff.submit",
                data = handoff
            });
            ToolCallLogger.Log(nameof(SubmitReviewHandoff), new { handoff }, result);
            return result;
        }

        [McpServerTool(Name = "search_platform_source"), Description(
            "在 Automation 平台源码根目录内执行只读字面量检索。路径会被限制在平台源码根目录，跳过bin/obj/.git等生成或管理目录，不执行Shell、不接受正则表达式。用于源码审查和修改前定位；返回相对路径、行号、匹配行和截断标志。")]
        public static string SearchPlatformSource(
            [Description("必填字面量，忽略大小写；长度1..200，不是正则表达式")] string query,
            [Description("相对平台源码根目录的可选子目录；空字符串表示全仓库，不能使用..越界")] string relativeDirectory = "",
            [Description("只搜索一种受支持扩展名，例如.cs或.md")] string fileExtension = ".cs",
            [Description("最多返回匹配行数，范围1..100")] int maxResults = 50)
        {
            string result = PlatformSourceSearchCatalog.Search(
                query,
                relativeDirectory,
                fileExtension,
                maxResults);
            ToolCallLogger.Log(nameof(SearchPlatformSource), new
            {
                query,
                relativeDirectory,
                fileExtension,
                maxResults
            }, result);
            return result;
        }

        [McpServerTool(Name = "get_native_operation_schemas"), Description(
            "按精确原生operaType批量读取递归字段契约，供operation.kind=native.operation使用。返回common公共契约与各类型差量，合并后填写。")]
        public static async Task<string> GetOperationSchemas(
            [Description("精确原生指令类型数组，例如 跳转、延时、修改变量")] string[] operaTypes)
        {
            return await ExecuteAsync(
                toolName: nameof(GetOperationSchemas),
                args: new { operaTypes },
                action: client => client.GetNativeOperationContractsAsync(operaTypes)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "get_native_operation_field_contract"), Description(
            "按一个精确原生operaType读取少量顶层字段的可写契约。既有同类型指令做operation.update时优先使用本工具，"
            + "避免读取整类原生契约或UI Schema；返回字段JSON形状、引用类型和对应运行规则。改变指令类型或需要完整递归结构时再使用get_native_operation_schemas。")]
        public static async Task<string> GetNativeOperationFieldContract(
            [Description("现有指令的精确原生类型，例如逻辑判断、修改变量")] string operaType,
            [Description("1..12个区分大小写的顶层可写字段名，例如FalseGoto")] string[] fieldNames)
        {
            return await ExecuteAsync(
                toolName: nameof(GetNativeOperationFieldContract),
                args: new { operaType, fieldNames },
                action: async client =>
                {
                    string normalizedType = (operaType ?? string.Empty).Trim();
                    if (normalizedType.Length == 0)
                        throw new ArgumentException("operaType 不能为空。", nameof(operaType));
                    string[] normalizedFields = (fieldNames ?? Array.Empty<string>())
                        .Select(value => (value ?? string.Empty).Trim())
                        .Where(value => value.Length > 0)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (normalizedFields.Length < 1 || normalizedFields.Length > 12)
                        throw new ArgumentException("fieldNames 必须包含1..12个非空唯一字段。", nameof(fieldNames));
                    string raw = await client.GetNativeOperationContractsAsync(new[] { normalizedType })
                        .ConfigureAwait(false);
                    JsonObject root = ParseBridgeResponse(raw);
                    EnsureBridgeSuccess(root);
                    JsonObject data = root["data"] as JsonObject
                        ?? throw new InvalidOperationException("原生指令契约缺少data。");
                    JsonObject common = data["common"] as JsonObject ?? new JsonObject();
                    JsonObject contract = data["contracts"]?[normalizedType] as JsonObject
                        ?? throw new ArgumentException($"未找到原生指令契约：{normalizedType}。", nameof(operaType));
                    JsonObject commonFields = common["fields"] as JsonObject ?? new JsonObject();
                    JsonObject typeFields = contract["fields"] as JsonObject ?? new JsonObject();
                    JsonObject commonRules = common["behavior"]?["fieldRules"] as JsonObject ?? new JsonObject();
                    JsonObject typeRules = contract["behavior"]?["fieldRules"] as JsonObject ?? new JsonObject();
                    var selectedFields = new JsonObject();
                    var selectedRules = new JsonObject();
                    foreach (string fieldName in normalizedFields)
                    {
                        JsonNode? field = typeFields[fieldName] ?? commonFields[fieldName];
                        if (field == null)
                            throw new ArgumentException(
                                $"原生指令[{normalizedType}]没有顶层可写字段：{fieldName}。", nameof(fieldNames));
                        selectedFields[fieldName] = field.DeepClone();
                        JsonNode? rule = typeRules[fieldName] ?? commonRules[fieldName];
                        if (rule != null) selectedRules[fieldName] = rule.DeepClone();
                    }
                    return JsonSerializer.Serialize(new JsonObject
                    {
                        ["ok"] = true,
                        ["type"] = "change_set.native_field_contract",
                        ["data"] = new JsonObject
                        {
                            ["operaType"] = normalizedType,
                            ["writeKind"] = "native.operation",
                            ["updateSemantics"] = "operation.update fields是同类型顶层局部补丁",
                            ["fields"] = selectedFields,
                            ["fieldRules"] = selectedRules
                        }
                    });
                }).ConfigureAwait(false);
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
            "预演一个可独立保存、原子提交的ChangeSet V2阶段。每个action只表达一个type；process.create不能内嵌steps/operations，新步骤使用独立step.append。ProcessCreate中只能创建一个新流程并追加其步骤/指令，必须用当前阶段局部key精确串联；简单流程可一次预演，复杂流程可先提交autoStart=false的安全功能块后再续建。未知动作使用config.placeholder并保持incomplete，不得伪造成runnable。返回previewId、变化摘要、就绪事实、警告、阻塞和合法迁移。")]
        public static async Task<string> PreviewChangeSet(
            [Description("当前完整原子阶段；actions与variables整体预演。阶段依赖的新变量也在variables中提交")] AtomicChangeSetDefinition changeSet,
            [Description("仅用于修正尚未apply的活动预演；新changeSet完整替代旧阶段，省略的旧动作不会保留。已apply后不要传此参数，改用稳定ID开始新阶段")] string? replacePreviewId = null)
        {
            return await ExecuteAsync(
                toolName: nameof(PreviewChangeSet),
                args: new { changeSet, replacePreviewId },
                action: client =>
                {
                    if (changeSet == null) throw new ArgumentNullException(nameof(changeSet));
                    if ((changeSet.Actions?.Count ?? 0) == 0 && (changeSet.Variables?.Count ?? 0) == 0)
                        throw new ArgumentException("changeSet 至少包含一个动作或变量声明。", nameof(changeSet));
                    if (string.Equals(
                        AutomationMcpRuntime.CurrentToolProfile,
                        AutomationToolProfiles.ProcessCreate,
                        StringComparison.Ordinal))
                    {
                        ValidateProcessCreateChangeSet(changeSet);
                    }
                    var compiledInput = new AiChangeSet
                    {
                        Version = 2,
                        Title = changeSet.Title,
                        Actions = changeSet.Actions,
                        Variables = changeSet.Variables
                    };
                    string validationError = AiChangeSetCatalog.Validate(compiledInput);
                    if (validationError != null)
                        throw new ArgumentException(validationError, nameof(changeSet));
                    return client.PreviewChangeSetAsync(compiledInput, replacePreviewId);
                }).ConfigureAwait(false);
        }

        internal static void ValidateProcessCreateChangeSet(AtomicChangeSetDefinition changeSet)
        {
            IReadOnlyList<ChangeSetAction> actions = changeSet.Actions ?? new List<ChangeSetAction>();
            ChangeSetAction? createAction = actions.FirstOrDefault(action =>
                string.Equals(action?.Type, "process.create", StringComparison.Ordinal));
            if (createAction == null
                || actions.Count(action => string.Equals(
                    action?.Type, "process.create", StringComparison.Ordinal)) != 1)
            {
                throw new ArgumentException(
                    "ProcessCreate 的每个阶段必须且只能包含一个 process.create。",
                    nameof(changeSet));
            }

            string processKey = createAction.Process?.Key?.Trim() ?? string.Empty;
            if (processKey.Length == 0)
            {
                throw new ArgumentException(
                    "ProcessCreate 请为 process.create.process.key 提供局部 key，后续步骤、指令和流程变量都用它引用新流程。",
                    nameof(changeSet));
            }

            var stepKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < actions.Count; index++)
            {
                ChangeSetAction action = actions[index]
                    ?? throw new ArgumentException($"actions[{index}] 不能为 null。", nameof(changeSet));
                string path = $"actions[{index}]";
                switch (action.Type)
                {
                    case "process.create":
                        break;
                    case "step.append":
                        RequireOnlyLocalKey(action.TargetProcess, processKey, $"{path}.targetProcess");
                        string stepKey = action.Step?.Key?.Trim() ?? string.Empty;
                        if (stepKey.Length == 0)
                            throw new ArgumentException($"{path}.step.key 必填，用于当前阶段的后续指令定位。", nameof(changeSet));
                        if (!stepKeys.Add(stepKey))
                            throw new ArgumentException($"{path}.step.key 重复：{stepKey}。", nameof(changeSet));
                        break;
                    case "operation.append":
                        RequireOnlyLocalKey(action.TargetProcess, processKey, $"{path}.targetProcess");
                        string targetStepKey = action.TargetStep?.Key?.Trim() ?? string.Empty;
                        if (targetStepKey.Length == 0
                            || !string.IsNullOrWhiteSpace(action.TargetStep?.StepId))
                        {
                            throw new ArgumentException(
                                $"{path}.targetStep 只能使用当前阶段新步骤的 key。",
                                nameof(changeSet));
                        }
                        if (!stepKeys.Contains(targetStepKey))
                            throw new ArgumentException(
                                $"{path}.targetStep.key={targetStepKey} 必须引用前面已 step.append 的局部 key。",
                                nameof(changeSet));
                        break;
                    default:
                        throw new ArgumentException(
                            $"ProcessCreate 不接受 {path}.type={action.Type}；只使用 process.create、step.append 和 operation.append。已提交流程的后续修改转入 ProcessEdit。",
                            nameof(changeSet));
                }
            }

            IReadOnlyList<VariableChange> variables = changeSet.Variables ?? new List<VariableChange>();
            for (int index = 0; index < variables.Count; index++)
            {
                VariableChange variable = variables[index]
                    ?? throw new ArgumentException($"variables[{index}] 不能为 null。", nameof(changeSet));
                if (string.Equals(variable.Scope, "process", StringComparison.OrdinalIgnoreCase))
                {
                    RequireOnlyLocalKey(variable.OwnerProcess, processKey, $"variables[{index}].ownerProcess");
                }
            }
        }

        private static void RequireOnlyLocalKey(ProcessSelector? selector, string expectedKey, string path)
        {
            string actualKey = selector?.Key?.Trim() ?? string.Empty;
            if (!string.Equals(actualKey, expectedKey, StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(selector?.ProcId)
                || !string.IsNullOrWhiteSpace(selector?.Name))
            {
                throw new ArgumentException(
                    $"{path} 只能提供 key={expectedKey}，不得指向现有流程。",
                    "changeSet");
            }
        }

        [McpServerTool(Name = "apply_change_set"), Description(
            "提交一个已由前台确认的冻结V2预演，只接收previewId。成功结果返回configurationSaved、稳定对象身份、受影响流程、变量处理和就绪事实。")]
        public static async Task<string> ApplyChangeSet(
            [Description("preview_change_set 返回且已由前台确认的32位 previewId")] string previewId)
        {
            string result = await ExecuteAsync(
                toolName: nameof(ApplyChangeSet),
                args: new { previewId },
                action: client => client.ApplyChangeSetAsync(previewId)).ConfigureAwait(false);
            return CompactChangeSetApplyResult(result);
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
            "Automation复杂流程设计的唯一按需知识入口。通常只按目标选择一个主主题；默认compact直接返回当前功能块的短规则、可执行阶段、完成证据和失败恢复，只有需要完整设计背景时才使用full。core通用不变量会自动返回。"
            + "同时返回从旧项目证据中完成审核和归纳的可用规范；候选、审核过程和废弃内容不会进入运行时返回。"
            + "简单赋值、单字段编辑不需要调用。具体字段、资源、运行行为和启动条件仍以当前Schema、Behavior、资源工具和Readiness为准。")]
        public static string GetProcessDesignGuide(
            [Description("主题数组，通常只传一个主主题：core自动加入；lifecycle=复位/启动；orchestration=主调度/子流程；interlock=门禁/光栅/前置条件；actuator=IO/气缸/真空；motion=轴/工站运动；pick-place=取料/放料/分流；transfer=输送/载具/升降/料仓；identify=扫码/RFID；transaction=MES/通讯事务；monitoring=持续监控/状态呈现；recovery=寻料/重入/恢复；custom-function=函数边界；review=设计审查")] string[] topics,
            [Description("返回粒度：compact（默认，直接用于当前功能块）或full（完整背景与知识正文）")] string? detail = null)
        {
            string result = ProcessDesignGuideCatalog.Get(topics, detail);
            ToolCallLogger.Log(nameof(GetProcessDesignGuide), new { topics, detail }, result);
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

        [McpServerTool(Name = "resolve_proc_target"), Description(
            "用1..6个普通文本线索一次定位流程，按每个线索查询后去重返回候选。"
            + "适合用户名称与项目名称不完全一致时使用；不接受空字符串或*，不读取指令详情。")]
        public static async Task<string> ResolveProcTarget(
            [Description("流程名称、编号或简称线索，1..6项；按任一线索匹配")] string[] keywords,
            [Description("每个线索最多返回1..20项，默认10")] int? limitPerKeyword = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ResolveProcTarget),
                args: new { keywords, limitPerKeyword },
                action: async client =>
                {
                    string[] normalizedKeywords = NormalizeDiscoveryKeywords(keywords, "keywords");
                    int limit = limitPerKeyword ?? 10;
                    if (limit < 1 || limit > 20)
                        throw new ArgumentException(
                            "limitPerKeyword 必须在1..20范围内。", nameof(limitPerKeyword));
                    var queries = new JsonArray();
                    var candidates = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                    foreach (string keyword in normalizedKeywords)
                    {
                        JsonObject response = ParseBridgeResponse(await client.SearchProcCatalogAsync(
                            keyword, 0, limit, false).ConfigureAwait(false));
                        EnsureBridgeSuccess(response);
                        JsonObject data = response["data"] as JsonObject ?? new JsonObject();
                        JsonArray items = data["items"] as JsonArray ?? new JsonArray();
                        queries.Add(new JsonObject
                        {
                            ["keyword"] = keyword,
                            ["total"] = data["total"]?.DeepClone(),
                            ["returned"] = items.Count
                        });
                        foreach (JsonObject item in items.OfType<JsonObject>())
                        {
                            string key = item["procId"]?.GetValue<string>()
                                ?? "index:" + (item["procIndex"]?.ToJsonString() ?? string.Empty);
                            candidates[key] = (JsonObject)item.DeepClone();
                        }
                    }
                    return JsonSerializer.Serialize(new JsonObject
                    {
                        ["ok"] = true,
                        ["type"] = "proc.resolve",
                        ["data"] = new JsonObject
                        {
                            ["queryMode"] = "any_keyword_contains",
                            ["queries"] = queries,
                            ["matchedCount"] = candidates.Count,
                            ["items"] = ToJsonArray(candidates.Values)
                        }
                    });
                }).ConfigureAwait(false);
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

        [McpServerTool(Name = "inspect_process"), Description(
            "一次读取单个流程评审最常用的结构摘要、就绪校验、确定性流程图和外部引用；不需要先分别试探四个工具。"
            + "默认返回紧凑结构证据；只有结论依赖具体字段值时才设置includeOperationDetails=true，且流程不超过25条指令时附带全部字段详情。"
            + "已聚合返回的事实不要重复读取；只有发现具体证据缺口时再使用细粒度工具。")]
        public static async Task<string> InspectProcess(
            [Description("流程索引（用户口语\"N号流程\"=procIndex=N）")] int procIndex,
            [Description("流程不超过25条指令时是否附带全部指令字段；默认false，只有字段级结论确实需要时开启")] bool includeOperationDetails = false)
        {
            return await ExecuteAsync(
                toolName: nameof(InspectProcess),
                args: new { procIndex, includeOperationDetails },
                action: async client =>
                {
                    Task<string> overviewTask = client.GetProcOverviewAsync(procIndex);
                    Task<string> validationTask = client.ValidateProcAsync(procIndex);
                    Task<string> flowGraphTask = client.GetFlowGraphAsync(FlowGraphScope.Process, procIndex);
                    Task<string> referencesTask = client.GetProcReferencesAsync(procIndex, 0, 20, 50);
                    await Task.WhenAll(overviewTask, validationTask, flowGraphTask, referencesTask)
                        .ConfigureAwait(false);

                    JsonObject overview = ParseBridgeResponse(await overviewTask.ConfigureAwait(false));
                    JsonObject validation = ParseBridgeResponse(await validationTask.ConfigureAwait(false));
                    JsonObject flowGraph = ParseBridgeResponse(await flowGraphTask.ConfigureAwait(false));
                    JsonObject references = ParseBridgeResponse(await referencesTask.ConfigureAwait(false));
                    EnsureBridgeSuccess(overview);
                    EnsureBridgeSuccess(validation);
                    EnsureBridgeSuccess(flowGraph);
                    EnsureBridgeSuccess(references);

                    JsonObject overviewData = overview["data"] as JsonObject ?? new JsonObject();
                    string[] opIds = (overviewData["steps"] as JsonArray ?? new JsonArray())
                        .OfType<JsonObject>()
                        .SelectMany(step => (step["ops"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                        .Select(operation => operation["opId"]?.GetValue<string>() ?? string.Empty)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    JsonNode? operationDetails = null;
                    bool detailsOmitted = !includeOperationDetails || opIds.Length > 25;
                    if (includeOperationDetails && opIds.Length is > 0 and <= 25)
                    {
                        JsonObject details = ParseBridgeResponse(
                            await client.GetOpDetailsAsync(procIndex, opIds).ConfigureAwait(false));
                        EnsureBridgeSuccess(details);
                        operationDetails = details["data"]?.DeepClone();
                    }

                    return JsonSerializer.Serialize(new JsonObject
                    {
                        ["ok"] = true,
                        ["type"] = "proc.inspection",
                        ["data"] = new JsonObject
                        {
                            ["procIndex"] = procIndex,
                            ["overview"] = overviewData.DeepClone(),
                            ["validation"] = validation["data"]?.DeepClone(),
                            ["flowGraph"] = flowGraph["data"]?.DeepClone(),
                            ["references"] = references["data"]?.DeepClone(),
                            ["operationDetails"] = operationDetails,
                            ["detailsOmitted"] = detailsOmitted,
                            ["detailReason"] = detailsOmitted
                                ? includeOperationDetails
                                    ? "流程超过25条指令；按overview中的opId选择缺口后调用get_op_details。"
                                    : "调用方选择不读取字段详情。"
                                : null
                        }
                    });
                }).ConfigureAwait(false);
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
            "按精确名称读取一个 TCP 或串口通讯对象的配置和当前状态。TCP返回本地/远端端点、自动重连配置及启动与连接状态；已知通讯名时直接调用，不需要先列出全部通讯；同名跨类型时再指定kind。")]
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
            "无损分批体检大量流程，只返回原始问题位置和完整批次汇总，不返回流程全文。检查空流程、空步骤、禁用步骤/指令、空指令类型和无效跳转。"
            + "同一流程批次先用nextFindingOffset和indexRevision读完finding分页，再用nextProcOffset继续下一批；未读完不得声称完整覆盖。")]
        public static async Task<string> AuditProcBatch(
            [Description("流程扫描起点，默认0")] int? procOffset = null,
            [Description("本批扫描流程数1..50，默认20")] int? procLimit = null,
            [Description("当前流程批次内的问题分页起点，默认0；继续读取时使用nextFindingOffset")] int? findingOffset = null,
            [Description("本页原始问题数1..300，默认100")] int? findingLimit = null,
            [Description("续读时必须传首页返回的indexRevision；配置变化时拒绝混合不同快照，首页留空")] string? expectedIndexRevision = null)
        {
            return await ExecuteAsync(
                toolName: nameof(AuditProcBatch),
                args: new { procOffset, procLimit, findingOffset, findingLimit, expectedIndexRevision },
                action: client => client.AuditProcBatchAsync(
                    procOffset, procLimit, findingOffset, findingLimit, expectedIndexRevision)).ConfigureAwait(false);
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
            "读取运行快照（流程状态/当前位置/报警/安全锁定/本次runId/父runId/最新CT探针样本）。"
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
            [Description("目标状态，默认 Ready/Stopped/Alarming；可选 Ready/Stopped/Running/Paused/SingleStep/Alarming/Pausing/Stopping")] string[]? states = null,
            [Description("等待超时100..60000ms，默认30000ms")] int? timeoutMs = null)
        {
            return await ExecuteAsync(
                toolName: nameof(WaitForProcState),
                args: new { procIndex, states, timeoutMs },
                action: client => client.WaitForProcStateAsync(procIndex, states, timeoutMs)).ConfigureAwait(false);
        }

        [McpServerTool(Name = "run_proc_test"), Description(
            "仅用于用户本轮明确要求测试或试运行的场景；只要求创建或修改配置时，以预演和validate_proc作为完成证据。独立执行一次有边界的流程测试：直接传入Ready或Stopped流程，本工具负责启动、观察和安全停止；已经运行的流程不会被接管。观察窗口500..15000ms，自然结束则直接返回。"
            + "返回真实terminationReason、outcome、是否观察到运行、位置变化、是否由测试器停止及本轮runtimeEvidence黑匣子时间线，由调用方结合用户目标判断结果。本次测试结果不授权再次启动；start_proc只用于用户明确要求持续运行的场景。")]
        public static async Task<string> RunProcTest(
            [Description("处于Ready或Stopped的流程索引；优先使用 apply_change_set.affectedProcesses 返回值")] int procIndex,
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
            "按精确原生operaType读取同一行为契约源。coverage=specialized时返回已建模的运行行为、字段联动和失败条件；coverage=unknown时不提供控制流结论。语义kind的行为由语义Schema返回；固定业务报警直接使用alarm.raise，不要把中文意图“报警”猜成原生operaType。")]
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
            "启动流程并让它按自身生命周期持续运行。不需要配置预演，要求流程处于Ready或Stopped且通过运行就绪闸门；用于用户明确要求启动或持续运行的场景。"
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
            "按用户运行控制意图停止一个活动流程，直接发送命令且无需配置预演。配置提交遇到运行中流程时由操作员决定是否停止。")]
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
            + "支持类型、作用域、归属流程、名称模糊匹配和分页；名称线索已知时使用nameLike，只有确需全量清单时才省略。默认100条、每页最多100条。")]
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
            "预演TCP与串口的完整目标配置。TCP本地端点表示绑定或监听，远端端点表示连接目标或Server会话筛选条件；两份配置同一事务保存，仅完全权限开放。")]
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
            + "名称线索已知时使用nameLike，只有确需全量清单时才省略。IO 类型为\"通用输入\"或\"通用输出\"；精确完整配置使用get_io。")]
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

        [McpServerTool(Name = "discover_project_resources"), Description(
            "批量发现流程设计或修改所需的项目资源。一次接受多个类别和多个名称线索，按查询分组返回去重候选与明确的0命中；"
            + "每组返回resolutionStatus和bindingAllowed；只有exact可作为已确认绑定，candidate/ambiguous只是候选，必须占位或询问用户。"
            + "避免分别用search_proc_catalog/search_io/search_alarms反复试词。空字符串和*不代表全量，必须提供真实线索。")]
        public static async Task<string> DiscoverProjectResources(
            [Description("1..12组资源查询；kind支持process/io/variable/station/point/data_struct/alarm/communication/plc")]
            ProjectResourceDiscoveryQuery[] queries,
            [Description("每组最多返回1..20项，默认10")]
            int? limitPerQuery = null)
        {
            return await ExecuteAsync(
                toolName: nameof(DiscoverProjectResources),
                args: new { queries, limitPerQuery },
                action: async client =>
                {
                    if (queries == null || queries.Length < 1 || queries.Length > 12)
                        throw new ArgumentException("queries 必须包含1..12组资源查询。", nameof(queries));
                    int limit = limitPerQuery ?? 10;
                    if (limit < 1 || limit > 20)
                        throw new ArgumentException("limitPerQuery 必须在1..20范围内。", nameof(limitPerQuery));
                    ProjectResourceDiscoveryQuery[] normalized = queries
                        .Select((query, index) => NormalizeDiscoveryQuery(query, index))
                        .ToArray();
                    if (normalized.Sum(query => query.Keywords.Count) > 24)
                        throw new ArgumentException("queries 的关键词总数不能超过24。", nameof(queries));
                    var results = new JsonArray();
                    foreach (ProjectResourceDiscoveryQuery query in normalized)
                    {
                        JsonArray items = await DiscoverResourceItemsAsync(client, query, limit)
                            .ConfigureAwait(false);
                        JsonObject resolution = BuildDiscoveryResolution(query, items);
                        results.Add(new JsonObject
                        {
                            ["kind"] = query.Kind,
                            ["keywords"] = new JsonArray(query.Keywords
                                .Select(keyword => JsonValue.Create(keyword))
                                .ToArray()),
                            ["ioType"] = string.IsNullOrWhiteSpace(query.IoType) ? null : query.IoType,
                            ["stationIndex"] = query.StationIndex,
                            ["queryMode"] = "any_keyword_contains",
                            ["matchedCount"] = items.Count,
                            ["resolutionStatus"] = resolution["status"]?.DeepClone(),
                            ["bindingAllowed"] = resolution["bindingAllowed"]?.DeepClone(),
                            ["exactMatchNames"] = resolution["exactMatchNames"]?.DeepClone(),
                            ["resolutionNote"] = resolution["note"]?.DeepClone(),
                            ["items"] = items
                        });
                    }
                    return JsonSerializer.Serialize(new JsonObject
                    {
                        ["ok"] = true,
                        ["type"] = "project.resource.discovery",
                        ["data"] = new JsonObject
                        {
                            ["queryCount"] = results.Count,
                            ["results"] = results
                        }
                    });
                }).ConfigureAwait(false);
        }

        [McpServerTool(Name = "resolve_authoring_inputs"), Description(
            "按当前功能块的用途批量解析流程写入输入。每个requirement只表示一个资源绑定，names是该资源的精确名称或别名；"
            + "返回唯一精确命中、变量类型/作用域兼容性、bindingAllowed以及可直接用于后续 ChangeSet 的事实。"
            + "聚合结果已包含详情时不要再逐项读取；不兼容、模糊或缺失项应声明新变量、保留config.placeholder或询问用户。")]
        public static async Task<string> ResolveAuthoringInputs(
            [Description("1..20个独立绑定意图；每项key唯一，kind支持process/io/variable/station/point/data_struct/alarm/communication/plc")]
            AuthoringInputRequirement[] requirements,
            [Description("每项最多返回1..10个候选，默认5")]
            int? limitPerRequirement = null)
        {
            return await ExecuteAsync(
                toolName: nameof(ResolveAuthoringInputs),
                args: new { requirements, limitPerRequirement },
                action: async client =>
                {
                    if (requirements == null || requirements.Length < 1 || requirements.Length > 20)
                        throw new ArgumentException("requirements 必须包含1..20个绑定意图。", nameof(requirements));
                    int limit = limitPerRequirement ?? 5;
                    if (limit < 1 || limit > 10)
                        throw new ArgumentException("limitPerRequirement 必须在1..10范围内。", nameof(limitPerRequirement));
                    AuthoringInputRequirement[] normalized = requirements
                        .Select((requirement, index) => NormalizeAuthoringInputRequirement(requirement, index))
                        .ToArray();
                    string[] duplicatedKeys = normalized
                        .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .Where(group => group.Count() > 1)
                        .Select(group => group.Key)
                        .ToArray();
                    if (duplicatedKeys.Length > 0)
                        throw new ArgumentException(
                            "requirements.key 必须唯一，重复：" + string.Join("、", duplicatedKeys) + "。",
                            nameof(requirements));
                    if (normalized.Sum(item => item.Names.Count) > 40)
                        throw new ArgumentException("requirements 的名称或别名总数不能超过40。", nameof(requirements));

                    var results = new JsonArray();
                    foreach (AuthoringInputRequirement requirement in normalized)
                    {
                        var query = new ProjectResourceDiscoveryQuery
                        {
                            Kind = requirement.Kind,
                            Keywords = requirement.Names,
                            IoType = requirement.IoType,
                            StationIndex = requirement.StationIndex
                        };
                        JsonArray items = await DiscoverResourceItemsAsync(client, query, limit)
                            .ConfigureAwait(false);
                        JsonObject resolution = BuildDiscoveryResolution(query, items);
                        JsonObject compatibility = BuildAuthoringInputCompatibility(requirement, items, resolution);
                        results.Add(new JsonObject
                        {
                            ["key"] = requirement.Key,
                            ["kind"] = requirement.Kind,
                            ["purpose"] = requirement.Purpose,
                            ["names"] = new JsonArray(requirement.Names
                                .Select(name => JsonValue.Create(name)).ToArray()),
                            ["requiredType"] = NullIfEmpty(requirement.RequiredType),
                            ["requiredScope"] = NullIfEmpty(requirement.RequiredScope),
                            ["ownerProcId"] = NullIfEmpty(requirement.OwnerProcId),
                            ["stationIndex"] = requirement.StationIndex,
                            ["resolutionStatus"] = resolution["status"]?.DeepClone(),
                            ["compatibilityStatus"] = compatibility["status"]?.DeepClone(),
                            ["bindingAllowed"] = compatibility["bindingAllowed"]?.DeepClone(),
                            ["selected"] = compatibility["selected"]?.DeepClone(),
                            ["note"] = compatibility["note"]?.DeepClone(),
                            ["candidates"] = items
                        });
                    }
                    return JsonSerializer.Serialize(new JsonObject
                    {
                        ["ok"] = true,
                        ["type"] = "project.authoring_inputs",
                        ["data"] = new JsonObject
                        {
                            ["requirementCount"] = results.Count,
                            ["results"] = results,
                            ["nextStep"] = "仅对bindingAllowed=false且会改变业务行为的未决项继续取证；其余结果可直接进入当前功能块预演。"
                        }
                    });
                }).ConfigureAwait(false);
        }

        [McpServerTool(Name = "resolve_operation_capability"), Description(
            "把自然语言业务动作解析为平台真实可用的语义kind与已注册原生operaType候选。"
            + "调用方不应把业务描述直接传给get_operation_schema；先使用本工具，再按返回的nextContractTool读取确有需要的精确字段契约。")]
        public static async Task<string> ResolveOperationCapability(
            [Description("1..12个独立业务动作意图，每项key唯一")]
            OperationCapabilityIntent[] intents)
        {
            return await ExecuteAsync(
                toolName: nameof(ResolveOperationCapability),
                args: new { intents },
                action: async client =>
                {
                    OperationCapabilityIntent[] normalized = NormalizeOperationCapabilityIntents(intents);
                    JsonObject response = ParseBridgeResponse(
                        await client.OpMetaAsync("list_types", new JsonObject()).ConfigureAwait(false));
                    EnsureBridgeSuccess(response);
                    JsonArray registered = response["data"]?["items"] as JsonArray ?? new JsonArray();
                    var results = new JsonArray();
                    foreach (OperationCapabilityIntent intent in normalized)
                    {
                        string[] semanticCandidates = ResolveSemanticCandidates(intent.Intent);
                        JsonArray nativeCandidates = semanticCandidates.Length > 0
                            ? new JsonArray()
                            : RankNativeOperationCandidates(registered, intent.Intent);
                        bool exact = semanticCandidates.Length == 1 && nativeCandidates.Count == 0
                            || semanticCandidates.Length == 0 && nativeCandidates.Count == 1;
                        bool resolved = semanticCandidates.Length > 0 || nativeCandidates.Count > 0;
                        results.Add(new JsonObject
                        {
                            ["key"] = intent.Key,
                            ["intent"] = intent.Intent,
                            ["semanticCandidates"] = new JsonArray(semanticCandidates
                                .Select(value => JsonValue.Create(value)).ToArray()),
                            ["nativeCandidates"] = nativeCandidates,
                            ["resolutionStatus"] = exact ? "exact" : resolved ? "candidate" : "missing",
                            ["contractReadAllowed"] = exact,
                            ["recommendedFallback"] = resolved ? null : "config.placeholder",
                            ["fallbackCapabilities"] = resolved ? null : new JsonObject
                            {
                                ["plannedBranches"] = true,
                                ["rule"] = "占位只替代未决动作或结果条件；已知分支、回跳、计数和出口继续使用ChangeSet语义指令表达。"
                            },
                            ["nextContractTool"] = semanticCandidates.Length == 1
                                ? "get_semantic_operation_schema"
                                : nativeCandidates.Count == 1
                                    ? "get_native_operation_schemas"
                                    : null
                        });
                    }
                    return JsonSerializer.Serialize(new JsonObject
                    {
                        ["ok"] = true,
                        ["type"] = "operation.capability_resolution",
                        ["data"] = new JsonObject
                        {
                            ["results"] = results,
                            ["semanticKindCount"] = SemanticOperationKinds.SupportedKinds.Split('、').Length,
                            ["registeredNativeTypeCount"] = registered.Count,
                            ["rule"] = "只有返回的精确kind或operaType可以进入契约读取；没有候选时使用config.placeholder，不猜测类型名。占位不吞并已知重试、分支、清理或成功/耗尽出口。"
                        }
                    });
                }).ConfigureAwait(false);
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

        private static ProjectResourceDiscoveryQuery NormalizeDiscoveryQuery(
            ProjectResourceDiscoveryQuery query,
            int index)
        {
            if (query == null)
                throw new ArgumentException($"queries[{index}] 不能为空。", nameof(query));
            string kind = (query.Kind ?? string.Empty).Trim();
            if (!ProjectResourceDiscoveryKinds.SupportedKinds.Split('、')
                .Contains(kind, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"queries[{index}].kind 不支持：{kind}。", nameof(query));
            }
            string[] keywords = NormalizeDiscoveryKeywords(
                query.Keywords?.ToArray(), $"queries[{index}].keywords");
            string? ioType = string.IsNullOrWhiteSpace(query.IoType) ? null : query.IoType.Trim();
            if (!string.Equals(kind, "io", StringComparison.Ordinal) && ioType != null)
                throw new ArgumentException($"queries[{index}].ioType 只用于kind=io。", nameof(query));
            if (ioType != null
                && !string.Equals(ioType, "通用输入", StringComparison.Ordinal)
                && !string.Equals(ioType, "通用输出", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"queries[{index}].ioType 只能是通用输入或通用输出。", nameof(query));
            }
            int? stationIndex = query.StationIndex;
            if (string.Equals(kind, "point", StringComparison.Ordinal))
            {
                if (!stationIndex.HasValue || stationIndex.Value < 0)
                    throw new ArgumentException($"queries[{index}].stationIndex 在kind=point时必须是非负整数。", nameof(query));
            }
            else if (stationIndex.HasValue)
            {
                throw new ArgumentException($"queries[{index}].stationIndex 只用于kind=point。", nameof(query));
            }
            return new ProjectResourceDiscoveryQuery
            {
                Kind = kind,
                Keywords = keywords.ToList(),
                IoType = ioType,
                StationIndex = stationIndex
            };
        }

        private static AuthoringInputRequirement NormalizeAuthoringInputRequirement(
            AuthoringInputRequirement requirement,
            int index)
        {
            if (requirement == null)
                throw new ArgumentException($"requirements[{index}] 不能为空。", nameof(requirement));
            string key = (requirement.Key ?? string.Empty).Trim();
            if (key.Length < 1 || key.Length > 80)
                throw new ArgumentException($"requirements[{index}].key 长度必须为1..80。", nameof(requirement));
            string kind = (requirement.Kind ?? string.Empty).Trim();
            if (!ProjectResourceDiscoveryKinds.SupportedKinds.Split('、')
                .Contains(kind, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"requirements[{index}].kind 不支持：{kind}。", nameof(requirement));
            }
            string[] names = NormalizeDiscoveryKeywords(
                requirement.Names?.ToArray(), $"requirements[{index}].names");
            if (names.Length > 4)
                throw new ArgumentException($"requirements[{index}].names 最多4项。", nameof(requirement));
            string purpose = (requirement.Purpose ?? string.Empty).Trim();
            if (purpose.Length < 1 || purpose.Length > 200)
                throw new ArgumentException($"requirements[{index}].purpose 长度必须为1..200。", nameof(requirement));
            string? ioType = NullIfWhiteSpace(requirement.IoType);
            string? requiredType = NullIfWhiteSpace(requirement.RequiredType);
            string? requiredScope = NullIfWhiteSpace(requirement.RequiredScope);
            string? ownerProcId = NullIfWhiteSpace(requirement.OwnerProcId);
            int? stationIndex = requirement.StationIndex;
            if (!string.Equals(kind, "io", StringComparison.Ordinal) && ioType != null)
                throw new ArgumentException($"requirements[{index}].ioType 只用于kind=io。", nameof(requirement));
            if (ioType != null && ioType != "通用输入" && ioType != "通用输出")
                throw new ArgumentException($"requirements[{index}].ioType 只能是通用输入或通用输出。", nameof(requirement));
            if (!string.Equals(kind, "variable", StringComparison.Ordinal)
                && (requiredType != null || requiredScope != null || ownerProcId != null))
            {
                throw new ArgumentException(
                    $"requirements[{index}] 的requiredType/requiredScope/ownerProcId只用于kind=variable。",
                    nameof(requirement));
            }
            if (requiredType != null && !VariableChangeContract.IsValidType(requiredType))
                throw new ArgumentException($"requirements[{index}].requiredType 只能是double或string。", nameof(requirement));
            if (requiredScope != null && !VariableScopeContract.IsValid(requiredScope))
                throw new ArgumentException(
                    $"requirements[{index}].requiredScope 只能是{VariableScopeContract.SupportedScopes}。",
                    nameof(requirement));
            if (ownerProcId != null && !Guid.TryParse(ownerProcId, out _))
                throw new ArgumentException($"requirements[{index}].ownerProcId 必须是GUID。", nameof(requirement));
            if (ownerProcId != null && requiredScope != VariableScopeContract.Process)
                throw new ArgumentException(
                    $"requirements[{index}].ownerProcId 只在requiredScope=process时使用。",
                    nameof(requirement));
            if (string.Equals(kind, "point", StringComparison.Ordinal))
            {
                if (!stationIndex.HasValue || stationIndex.Value < 0)
                    throw new ArgumentException(
                        $"requirements[{index}].stationIndex 在kind=point时必须是非负整数。",
                        nameof(requirement));
            }
            else if (stationIndex.HasValue)
            {
                throw new ArgumentException(
                    $"requirements[{index}].stationIndex 只用于kind=point。",
                    nameof(requirement));
            }
            return new AuthoringInputRequirement
            {
                Key = key,
                Kind = kind,
                Names = names.ToList(),
                Purpose = purpose,
                IoType = ioType,
                RequiredType = requiredType,
                RequiredScope = requiredScope,
                OwnerProcId = ownerProcId,
                StationIndex = stationIndex
            };
        }

        private static JsonObject BuildAuthoringInputCompatibility(
            AuthoringInputRequirement requirement,
            JsonArray items,
            JsonObject resolution)
        {
            string resolutionStatus = resolution["status"]?.GetValue<string>()
                ?? ProjectResourceResolutionStatuses.Missing;
            JsonObject? selected = items.OfType<JsonObject>()
                .FirstOrDefault(item => requirement.Names.Any(name => string.Equals(
                    item["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase)));
            if (!string.Equals(
                    resolutionStatus, ProjectResourceResolutionStatuses.Exact, StringComparison.Ordinal)
                || selected == null)
            {
                return new JsonObject
                {
                    ["status"] = "unresolved",
                    ["bindingAllowed"] = false,
                    ["selected"] = null,
                    ["note"] = resolution["note"]?.DeepClone()
                };
            }

            var mismatches = new List<string>();
            if (string.Equals(requirement.Kind, "variable", StringComparison.Ordinal))
            {
                string actualType = selected["type"]?.GetValue<string>() ?? string.Empty;
                string actualScope = selected["scope"]?.GetValue<string>() ?? string.Empty;
                string actualOwner = selected["ownerProcId"]?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrEmpty(requirement.RequiredType)
                    && !string.Equals(requirement.RequiredType, actualType, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add($"type要求{requirement.RequiredType}，实际{actualType}");
                }
                if (!string.IsNullOrEmpty(requirement.RequiredScope)
                    && !string.Equals(requirement.RequiredScope, actualScope, StringComparison.Ordinal))
                {
                    mismatches.Add($"scope要求{requirement.RequiredScope}，实际{actualScope}");
                }
                if (!string.IsNullOrEmpty(requirement.OwnerProcId)
                    && !string.Equals(requirement.OwnerProcId, actualOwner, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add($"ownerProcId要求{requirement.OwnerProcId}，实际{actualOwner}");
                }
            }
            bool compatible = mismatches.Count == 0;
            return new JsonObject
            {
                ["status"] = compatible ? "compatible" : "incompatible",
                ["bindingAllowed"] = compatible,
                ["selected"] = selected.DeepClone(),
                ["note"] = compatible
                    ? "唯一精确命中且满足当前用途约束，可直接绑定。"
                    : "唯一精确命中但不兼容：" + string.Join("；", mismatches) + "。不得通过修改现有资源来迁就内部实现。"
            };
        }

        private static OperationCapabilityIntent[] NormalizeOperationCapabilityIntents(
            OperationCapabilityIntent[] intents)
        {
            if (intents == null || intents.Length < 1 || intents.Length > 12)
                throw new ArgumentException("intents 必须包含1..12个业务动作意图。", nameof(intents));
            OperationCapabilityIntent[] normalized = intents.Select((item, index) =>
            {
                string key = (item?.Key ?? string.Empty).Trim();
                string intent = (item?.Intent ?? string.Empty).Trim();
                if (key.Length < 1 || key.Length > 80)
                    throw new ArgumentException($"intents[{index}].key 长度必须为1..80。", nameof(intents));
                if (intent.Length < 1 || intent.Length > 200)
                    throw new ArgumentException($"intents[{index}].intent 长度必须为1..200。", nameof(intents));
                return new OperationCapabilityIntent { Key = key, Intent = intent };
            }).ToArray();
            string[] duplicates = normalized.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            if (duplicates.Length > 0)
                throw new ArgumentException("intents.key 必须唯一，重复：" + string.Join("、", duplicates) + "。", nameof(intents));
            return normalized;
        }

        internal static string[] ResolveSemanticCandidates(string intent)
        {
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            string normalized = NormalizeCapabilityIntent(intent);
            AddCandidateWhenContains(candidates, normalized, "清空", "variable.clear");
            AddCandidateWhenContains(candidates, normalized, "赋值|设置变量|写变量", "variable.set");
            AddCandidateWhenContains(candidates, normalized, "复制变量|拷贝变量", "variable.copy");
            if ((normalized.IndexOf("复制", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.IndexOf("拷贝", StringComparison.OrdinalIgnoreCase) >= 0)
                && normalized.IndexOf("变量", StringComparison.OrdinalIgnoreCase) >= 0)
                candidates.Add("variable.copy");
            if (normalized.IndexOf("结构体", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("数据结构", StringComparison.OrdinalIgnoreCase) >= 0)
                candidates.Remove("variable.copy");
            AddCandidateWhenContains(candidates, normalized, "累加|计数", "variable.add");
            AddCandidateWhenContains(candidates, normalized, "计算|运算", "variable.compute");
            AddCandidateWhenContains(candidates, normalized, "延时|等待时间|节拍", "wait");
            AddCandidateWhenContains(candidates, normalized, "跳转|转到", "flow.goto");
            AddCandidateWhenContains(candidates, normalized, "结束流程|正常结束", "flow.end");
            AddCandidateWhenContains(candidates, normalized, "数值比较|次数判断|大于|小于|等于", "branch.number_compare");
            AddCandidateWhenContains(candidates, normalized, "数值范围|区间", "branch.number_range");
            AddCandidateWhenContains(candidates, normalized, "IO判断|输入判断|信号判断", "branch.io");
            AddCandidateWhenContains(candidates, normalized, "报警|故障提示", "alarm.raise");
            AddCandidateWhenContains(candidates, normalized, "弹框|普通提示", "popup.message");
            AddCandidateWhenContains(candidates, normalized, "写IO|输出信号|置位输出|复位输出", "io.write");
            AddCandidateWhenContains(candidates, normalized, "等待IO|等待输入|等待到位", "io.wait");
            if (normalized.IndexOf("等待", StringComparison.OrdinalIgnoreCase) >= 0
                && (normalized.IndexOf("信号", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.IndexOf("输入", StringComparison.OrdinalIgnoreCase) >= 0))
                candidates.Add("io.wait");
            AddCandidateWhenContains(candidates, normalized, "启动子流程|停止子流程|控制流程", "process.control");
            AddCandidateWhenContains(candidates, normalized, "等待子流程|等待流程", "process.wait");
            return candidates.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private static string NormalizeCapabilityIntent(string intent)
        {
            string value = (intent ?? string.Empty).Trim();
            foreach (string filler in new[] { " ", "\t", "\r", "\n", "工件", "物料", "载具", "设备" })
                value = value.Replace(filler, string.Empty, StringComparison.OrdinalIgnoreCase);
            return value;
        }

        private static void AddCandidateWhenContains(
            ISet<string> candidates,
            string intent,
            string aliases,
            string kind)
        {
            if (aliases.Split('|').Any(alias => intent.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0))
                candidates.Add(kind);
        }

        internal static JsonArray RankNativeOperationCandidates(JsonArray registered, string intent)
        {
            string normalized = intent.Replace("操作", string.Empty).Replace("动作", string.Empty).Trim();
            IEnumerable<JsonObject> candidates = registered.OfType<JsonObject>()
                .Where(item =>
                {
                    string type = item["operaType"]?.GetValue<string>() ?? string.Empty;
                    string name = item["name"]?.GetValue<string>() ?? string.Empty;
                    return normalized.Length > 0
                        && ((!string.IsNullOrWhiteSpace(type)
                                && (type.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                                    || intent.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0))
                            || (!string.IsNullOrWhiteSpace(name)
                                && (name.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                                    || intent.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)));
                })
                .Take(8);
            return ToJsonArray(candidates);
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static JsonNode? NullIfEmpty(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : JsonValue.Create(value);
        }

        private static string[] NormalizeDiscoveryKeywords(string[]? keywords, string path)
        {
            string[] normalized = (keywords ?? Array.Empty<string>())
                .Select(item => (item ?? string.Empty).Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (normalized.Length < 1 || normalized.Length > 6)
                throw new ArgumentException($"{path} 必须包含1..6个非空普通文本线索。", path);
            if (normalized.Any(item => string.Equals(item, "*", StringComparison.Ordinal)))
                throw new ArgumentException($"{path} 不接受*；需要全量目录时使用对应list工具。", path);
            return normalized;
        }

        private static async Task<JsonArray> DiscoverResourceItemsAsync(
            AutomationBridgeClient client,
            ProjectResourceDiscoveryQuery query,
            int limit)
        {
            var matches = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            if (string.Equals(query.Kind, "station", StringComparison.Ordinal)
                || string.Equals(query.Kind, "point", StringComparison.Ordinal)
                || string.Equals(query.Kind, "data_struct", StringComparison.Ordinal))
            {
                string raw = string.Equals(query.Kind, "station", StringComparison.Ordinal)
                    ? await client.ListStationsAsync().ConfigureAwait(false)
                    : string.Equals(query.Kind, "point", StringComparison.Ordinal)
                        ? await client.ListPointsAsync(query.StationIndex!.Value).ConfigureAwait(false)
                        : await client.ListDataStructsAsync().ConfigureAwait(false);
                JsonObject response = ParseBridgeResponse(raw);
                EnsureBridgeSuccess(response);
                JsonArray catalog = response["data"]?["items"] as JsonArray ?? new JsonArray();
                foreach (JsonObject item in catalog.OfType<JsonObject>()
                    .Where(item => MatchesAnyKeyword(item, query.Keywords)).Take(limit))
                {
                    JsonObject resolvedItem = item;
                    string name = item["name"]?.GetValue<string>() ?? string.Empty;
                    if (string.Equals(query.Kind, "data_struct", StringComparison.Ordinal)
                        && query.Keywords.Any(keyword => string.Equals(
                            keyword, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        JsonObject detail = ParseBridgeResponse(
                            await client.GetDataStructAsync(name).ConfigureAwait(false));
                        EnsureBridgeSuccess(detail);
                        resolvedItem = detail["data"] as JsonObject ?? item;
                    }
                    AddDiscoveryMatch(matches, resolvedItem, query.Kind);
                }
                return ToJsonArray(matches.Values);
            }
            if (string.Equals(query.Kind, "communication", StringComparison.Ordinal)
                || string.Equals(query.Kind, "plc", StringComparison.Ordinal))
            {
                string action = string.Equals(query.Kind, "plc", StringComparison.Ordinal)
                    ? "plc" : "communications";
                JsonObject parameters = string.Equals(query.Kind, "plc", StringComparison.Ordinal)
                    ? new JsonObject { ["includeMaps"] = false }
                    : new JsonObject { ["includeStatus"] = false };
                JsonObject response = ParseBridgeResponse(await client.ListResourcesAsync(action, parameters)
                    .ConfigureAwait(false));
                EnsureBridgeSuccess(response);
                JsonObject data = response["data"] as JsonObject ?? new JsonObject();
                IEnumerable<JsonObject> catalog = string.Equals(query.Kind, "plc", StringComparison.Ordinal)
                    ? (data["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                    : (data["tcp"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                        .Concat((data["serial"] as JsonArray ?? new JsonArray()).OfType<JsonObject>());
                foreach (JsonObject item in catalog.Where(item => MatchesAnyKeyword(item, query.Keywords)))
                {
                    AddDiscoveryMatch(matches, item, query.Kind);
                    if (matches.Count >= limit) break;
                }
                return ToJsonArray(matches.Values);
            }

            foreach (string keyword in query.Keywords)
            {
                JsonObject response;
                switch (query.Kind)
                {
                    case "process":
                        response = ParseBridgeResponse(await client.SearchProcCatalogAsync(
                            keyword, 0, limit, false).ConfigureAwait(false));
                        break;
                    case "io":
                        response = ParseBridgeResponse(await client.SearchIoAsync(
                            keyword, query.IoType, null, 0, limit).ConfigureAwait(false));
                        break;
                    case "variable":
                        response = ParseBridgeResponse(await client.ListVariablesAsync(
                            null, keyword, null, null, 0, limit).ConfigureAwait(false));
                        break;
                    case "alarm":
                        response = ParseBridgeResponse(await client.ListAlarmsAsync(
                            false, null, keyword, 0, limit).ConfigureAwait(false));
                        break;
                    default:
                        throw new InvalidOperationException("未实现的资源发现类别：" + query.Kind);
                }
                EnsureBridgeSuccess(response);
                JsonArray items = response["data"]?["items"] as JsonArray ?? new JsonArray();
                foreach (JsonObject item in items.OfType<JsonObject>())
                {
                    AddDiscoveryMatch(matches, item, query.Kind);
                    if (matches.Count >= limit) break;
                }
                if (matches.Count >= limit) break;
            }
            return ToJsonArray(matches.Values);
        }

        private static bool MatchesAnyKeyword(
            JsonObject item,
            IReadOnlyCollection<string> keywords)
        {
            string name = item?["name"]?.GetValue<string>() ?? string.Empty;
            return keywords.Any(keyword =>
                name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static JsonObject BuildDiscoveryResolution(
            ProjectResourceDiscoveryQuery query,
            JsonArray items)
        {
            string[] exactNames = (items ?? new JsonArray())
                .OfType<JsonObject>()
                .Select(item => item["name"]?.GetValue<string>() ?? string.Empty)
                .Where(name => name.Length > 0 && query.Keywords.Any(keyword =>
                    string.Equals(name, keyword, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            int count = items?.Count ?? 0;
            string status = count == 0
                ? ProjectResourceResolutionStatuses.Missing
                : exactNames.Length == 1
                    ? ProjectResourceResolutionStatuses.Exact
                    : count == 1
                        ? ProjectResourceResolutionStatuses.Candidate
                        : ProjectResourceResolutionStatuses.Ambiguous;
            bool bindingAllowed = string.Equals(
                status, ProjectResourceResolutionStatuses.Exact, StringComparison.Ordinal);
            string note;
            switch (status)
            {
                case ProjectResourceResolutionStatuses.Exact:
                    note = "唯一精确名称命中，可按该精确名称绑定。";
                    break;
                case ProjectResourceResolutionStatuses.Missing:
                    note = "没有命中资源；写入时保留可见占位，不能猜测。";
                    break;
                case ProjectResourceResolutionStatuses.Candidate:
                    note = "仅有一个模糊候选，但不是精确名称证据；写入时保留可见占位或询问用户。";
                    break;
                default:
                    note = "存在多个候选且没有唯一精确命中；不得自行选择，写入时保留可见占位或询问用户。";
                    break;
            }
            return new JsonObject
            {
                ["status"] = status,
                ["bindingAllowed"] = bindingAllowed,
                ["exactMatchNames"] = new JsonArray(exactNames
                    .Select(name => (JsonNode?)JsonValue.Create(name))
                    .ToArray()),
                ["note"] = note
            };
        }

        private static void AddDiscoveryMatch(
            IDictionary<string, JsonObject> matches,
            JsonObject item,
            string kind)
        {
            string key = item["procId"]?.GetValue<string>()
                ?? item["name"]?.GetValue<string>()
                ?? item["index"]?.ToJsonString()
                ?? item["procIndex"]?.ToJsonString()
                ?? Guid.NewGuid().ToString("N");
            matches[kind + ":" + key] = (JsonObject)item.DeepClone();
        }

        private static JsonObject ParseBridgeResponse(string raw)
        {
            return JsonNode.Parse(raw ?? string.Empty) as JsonObject
                ?? throw new InvalidOperationException("Bridge 返回的不是JSON对象。");
        }

        internal static string CompactChangeSetApplyResult(string? raw)
        {
            try
            {
                JsonObject? response = JsonNode.Parse(raw ?? string.Empty) as JsonObject;
                if (response?["ok"]?.GetValue<bool>() != true
                    || !string.Equals(
                        response["type"]?.GetValue<string>(),
                        "change_set.apply",
                        StringComparison.Ordinal)
                    || response["data"] is not JsonObject data)
                {
                    return raw ?? string.Empty;
                }

                var compactData = new JsonObject();
                CopyFields(data, compactData,
                    "previewId", "configurationSaved", "status", "summary",
                    "readinessStatus", "runnable");
                compactData["variableResolutions"] = CompactObjectArray(
                    data["variableResolutions"],
                    "variableId", "name", "valueType", "outcome", "changed", "index",
                    "scope", "ownerProcId", "ownerProcName");
                compactData["affectedProcesses"] = CompactObjectArray(
                    data["affectedProcesses"],
                    "procIndex", "procId", "name", "changeType", "readinessStatus", "runnable");
                var created = new JsonObject();
                if (data["createdObjects"] is JsonObject createdObjects)
                {
                    created["processes"] = CompactObjectArray(
                        createdObjects["processes"], "key", "procId", "procIndex", "name");
                    created["steps"] = CompactObjectArray(
                        createdObjects["steps"],
                        "procId", "processKey", "key", "stepId", "name");
                    created["operations"] = CompactObjectArray(
                        createdObjects["operations"],
                        "procId", "processKey", "stepId", "stepKey", "key", "opId", "name", "operaType");
                    created["variables"] = CompactObjectArray(
                        createdObjects["variables"],
                        "variableId", "name", "valueType", "outcome", "index", "scope", "ownerProcId");
                }
                compactData["createdObjects"] = created;
                compactData["warnings"] = CompactObjectArray(
                    data["warnings"], "procIndex", "procId", "message");
                compactData["runBlockers"] = CompactObjectArray(
                    data["runBlockers"], "procIndex", "procId", "message");
                return new JsonObject
                {
                    ["ok"] = true,
                    ["type"] = "change_set.apply",
                    ["data"] = compactData
                }.ToJsonString();
            }
            catch (JsonException)
            {
                return raw ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                return raw ?? string.Empty;
            }
        }

        private static JsonArray CompactObjectArray(JsonNode? source, params string[] fields)
        {
            var result = new JsonArray();
            if (source is not JsonArray sourceArray) return result;
            foreach (JsonObject item in sourceArray.OfType<JsonObject>())
            {
                var compact = new JsonObject();
                CopyFields(item, compact, fields);
                result.Add(compact);
            }
            return result;
        }

        private static void CopyFields(JsonObject source, JsonObject target, params string[] fields)
        {
            foreach (string field in fields)
            {
                if (source.TryGetPropertyValue(field, out JsonNode? value) && value != null)
                    target[field] = value.DeepClone();
            }
        }

        private static void EnsureBridgeSuccess(JsonObject response)
        {
            if (response?["ok"]?.GetValue<bool>() == true) return;
            string code = response?["errorCode"]?.GetValue<string>() ?? "BRIDGE_ERROR";
            string message = response?["message"]?.GetValue<string>() ?? "Bridge 查询失败。";
            throw new ResourceDiscoveryBridgeException(
                code + ": " + message,
                response?.ToJsonString() ?? JsonSerializer.Serialize(new
                {
                    ok = false,
                    type = "mcp.error",
                    errorCode = code,
                    message
                }));
        }

        private static JsonArray ToJsonArray(IEnumerable<JsonObject> items)
        {
            var array = new JsonArray();
            foreach (JsonObject item in items ?? Enumerable.Empty<JsonObject>())
            {
                array.Add(item?.DeepClone());
            }
            return array;
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
                if (ex is ToolContractValidationException validationError)
                {
                    string result = JsonSerializer.Serialize(new
                    {
                        ok = false,
                        type = "mcp.error",
                        errorCode = validationError.ErrorCode,
                        message = validationError.Message,
                        issues = validationError.Issues.Select(issue => new
                        {
                            path = issue.Path,
                            rule = issue.Rule,
                            message = issue.Message,
                            suggestedRepair = issue.SuggestedRepair
                        }),
                        recovery = new
                        {
                            sideEffects = "none",
                            safeToRetry = true,
                            retryScope = "same_function_block"
                        }
                    });
                    ToolCallLogger.Complete(
                        callId, toolName, args, result, durationMs: stopwatch.ElapsedMilliseconds);
                    return result;
                }
                if (ex is ResourceDiscoveryBridgeException bridgeError)
                {
                    ToolCallLogger.Complete(
                        callId, toolName, args, bridgeError.Response,
                        durationMs: stopwatch.ElapsedMilliseconds);
                    return bridgeError.Response;
                }
                if (ex is ArgumentException argumentError)
                {
                    string result = JsonSerializer.Serialize(new
                    {
                        ok = false,
                        type = "mcp.error",
                        errorCode = "INVALID_ARGUMENT",
                        message = argumentError.Message,
                        issues = new[]
                        {
                            new
                            {
                                path = string.IsNullOrWhiteSpace(argumentError.ParamName)
                                    ? "$" : argumentError.ParamName,
                                rule = "tool_argument_contract",
                                message = argumentError.Message,
                                suggestedRepair = "保持当前业务目标不变，按该路径和消息修正参数后重试同一功能块。"
                            }
                        },
                        recovery = new
                        {
                            sideEffects = "none",
                            safeToRetry = true,
                            retryScope = "same_function_block"
                        }
                    });
                    ToolCallLogger.Complete(
                        callId, toolName, args, result, durationMs: stopwatch.ElapsedMilliseconds);
                    return result;
                }
                ToolCallLogger.Complete(callId, toolName, args, string.Empty, ex.ToString(), stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        private sealed class ResourceDiscoveryBridgeException : InvalidOperationException
        {
            public ResourceDiscoveryBridgeException(string message, string response)
                : base(message)
            {
                Response = response;
            }

            public string Response { get; }
        }

    }
}
