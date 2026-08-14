using System.Reflection;
using Automation.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;
// 模块：MCP / 工具 Profile。
// 职责范围：这是权限外壳与任务级最小能力包的对外工具集合权威来源。
// 排查入口：工具缺失、越权或退役工具复现时运行 --verify-profile，并核对本文件集合而非 Markdown。

namespace Automation.McpServer
{
    internal static class McpToolProfile
    {
        // Editor/Diagnostic 共享平台知识与读取能力；RuntimeDiagnostic 使用独立的现场取证最小集合。
        private static readonly HashSet<string> KnowledgeAndReadTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_platform_development_context", "get_process_design_guide",
            "list_procs", "search_proc_catalog", "resolve_proc_target", "discover_project_resources",
            "resolve_authoring_inputs", "resolve_operation_capability",
            "get_proc_overview", "get_proc_detail", "get_flow_graph", "get_step_detail",
            "get_op_detail", "get_op_details",
            "get_operation_references", "get_proc_references", "trace_resource", "find_variable_usages",
            "list_operation_types", "get_native_operation_schemas",
            "get_native_operation_field_contract",
            "get_semantic_operation_schema", "get_operation_guide",
            "get_snapshot", "validate_proc",
            "wait_for_proc_state",
            "list_variables", "get_variable_by_name", "get_variable_by_index",
            "list_stations", "get_station", "list_points", "get_point",
            "list_data_structs", "get_data_struct", "search_data_struct_items",
            "get_io", "search_io", "get_io_state",
            "get_communication",
            "list_plc_devices", "get_plc_device",
            "search_alarms", "get_alarm"
        };

        private static readonly HashSet<string> DiagnosticAnalysisTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "search_ops", "diagnose_issue", "get_operation_schema",
            "search_operation_fields", "find_references",
            "get_operation_context", "audit_proc_batch",
            "get_info_log_tail", "diagnose_proc",
            "list_io"
        };

        // 运行诊断中心只获取现场根因分析所需事实，不加载平台开发、流程设计、Schema、批量审计或控制工具。
        private static readonly HashSet<string> RuntimeDiagnosticTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "diagnose_issue", "get_snapshot", "get_info_log_tail",
            "get_operation_context", "get_step_detail", "get_flow_graph",
            "get_operation_references", "trace_resource",
            "get_variable_by_name", "get_variable_by_index",
            "get_io", "search_io", "get_io_state",
            "get_communication", "list_plc_devices", "get_plc_device",
            "search_alarms", "get_alarm"
        };

        private static readonly HashSet<string> EditorMutationTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "preview_change_set", "preview_process_blueprint", "apply_change_set", "discard_change_set_preview",
            "run_proc_test",
            "start_proc", "stop_proc", "pause_proc", "resume_proc",
            "set_variable_by_name", "set_variable_by_index",
            "add_variable", "update_variable", "delete_variable",
            "upsert_data_struct", "delete_data_struct",
            "set_alarm", "delete_alarm"
        };

        private static readonly HashSet<string> FullPermissionTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_migration_configuration",
            "preview_motion_io_configuration", "preview_io_debug_configuration",
            "preview_plc_configuration", "preview_communication_configuration",
            "apply_migration_configuration", "discard_migration_configuration",
            "validate_platform_configuration"
        };

        private static readonly IReadOnlyDictionary<string, HashSet<string>> TaskToolProfiles =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                [AutomationToolProfiles.TaskCoordinator] = ToolSet("request_capability"),
                [AutomationToolProfiles.ProcessDesign] = ToolSet("get_process_design_guide"),
                [AutomationToolProfiles.ProcessReview] = ToolSet(
                    "resolve_proc_target", "discover_project_resources", "get_proc_overview", "get_flow_graph",
                    "get_step_detail", "get_op_details", "search_ops",
                    "get_operation_references", "get_proc_references", "trace_resource", "find_variable_usages",
                    "get_operation_context", "audit_proc_batch", "validate_proc",
                    "get_operation_schema", "get_operation_guide", "get_semantic_operation_schema",
                    "get_native_operation_schemas", "get_variable_by_name", "get_variable_by_index",
                    "get_station", "get_point", "get_data_struct", "search_data_struct_items",
                    "get_io", "get_communication", "get_plc_device", "get_alarm"),
                [AutomationToolProfiles.ProcessCreate] = ToolSet(
                    "get_process_design_guide", "resolve_proc_target", "resolve_authoring_inputs",
                    "resolve_operation_capability", "get_semantic_operation_schema",
                    "get_native_operation_schemas", "preview_process_blueprint",
                    "apply_change_set", "discard_change_set_preview", "validate_proc"),
                [AutomationToolProfiles.ProcessEdit] = ToolSet(
                    "resolve_proc_target", "resolve_authoring_inputs", "resolve_operation_capability",
                    "get_proc_detail", "get_flow_graph", "get_step_detail", "get_op_details",
                    "get_operation_references", "get_operation_context", "get_native_operation_field_contract",
                    "get_operation_guide", "get_semantic_operation_schema", "get_native_operation_schemas",
                    "preview_change_set", "apply_change_set",
                    "discard_change_set_preview", "validate_proc"),
                [AutomationToolProfiles.ResourceEdit] = ToolSet(
                    "list_variables", "get_variable_by_name", "get_variable_by_index", "find_variable_usages",
                    "add_variable", "update_variable", "delete_variable", "list_data_structs", "get_data_struct",
                    "search_data_struct_items", "upsert_data_struct", "delete_data_struct", "search_alarms",
                    "get_alarm", "set_alarm", "delete_alarm"),
                [AutomationToolProfiles.RuntimeControl] = ToolSet(
                    "get_snapshot", "wait_for_proc_state", "get_proc_overview", "get_flow_graph",
                    "get_step_detail", "get_operation_context", "get_operation_references", "trace_resource",
                    "get_info_log_tail", "diagnose_proc", "validate_proc", "get_variable_by_name",
                    "get_variable_by_index", "get_io", "search_io", "get_io_state", "get_communication",
                    "get_plc_device", "search_alarms", "get_alarm", "run_proc_test", "start_proc", "stop_proc",
                    "pause_proc", "resume_proc"),
                [AutomationToolProfiles.SourceReview] = ToolSet(
                    "get_platform_development_context", "search_platform_source"),
                [AutomationToolProfiles.SourceDevelopment] = ToolSet(
                    "get_platform_development_context", "search_platform_source"),
                [AutomationToolProfiles.PlatformConfiguration] = ToolSet(
                    "get_migration_configuration", "preview_motion_io_configuration", "preview_io_debug_configuration",
                    "preview_plc_configuration", "preview_communication_configuration", "apply_migration_configuration",
                    "discard_migration_configuration", "validate_platform_configuration", "get_station", "get_point",
                    "get_io", "search_io", "get_communication", "get_plc_device")
            };

        public static IReadOnlyList<McpServerTool> CreateTools(string profile, bool fullPermissionEnabled = false)
        {
            profile = AutomationToolProfiles.Normalize(profile);
            var enabled = new HashSet<string>(StringComparer.Ordinal);
            if (TaskToolProfiles.TryGetValue(profile, out HashSet<string>? taskTools))
            {
                enabled.UnionWith(taskTools);
                if (AutomationToolProfiles.IsExecutionProfile(profile))
                    enabled.Add("request_capability");
            }
            else if (string.Equals(profile, AutomationToolProfiles.RuntimeDiagnostic, StringComparison.Ordinal))
            {
                enabled.UnionWith(RuntimeDiagnosticTools);
            }
            else if (string.Equals(profile, AutomationToolProfiles.Diagnostic, StringComparison.Ordinal))
            {
                enabled.UnionWith(KnowledgeAndReadTools);
                enabled.UnionWith(DiagnosticAnalysisTools);
            }
            else if (string.Equals(profile, AutomationToolProfiles.Editor, StringComparison.Ordinal))
            {
                enabled.UnionWith(KnowledgeAndReadTools);
                enabled.UnionWith(DiagnosticAnalysisTools);
                enabled.UnionWith(EditorMutationTools);
                if (fullPermissionEnabled)
                {
                    enabled.UnionWith(FullPermissionTools);
                }
            }
            else
            {
                throw new InvalidDataException($"MCP工具模式不支持:{profile}");
            }
            var tools = new List<McpServerTool>();
            foreach (MethodInfo method in typeof(AutomationMcpTools).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                McpServerToolAttribute? attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                string? toolName = attribute?.Name;
                if (string.IsNullOrEmpty(toolName) || !enabled.Contains(toolName))
                {
                    continue;
                }
                McpServerTool tool = McpServerTool.Create(method, (object)null!);
                if (string.Equals(toolName, "preview_change_set", StringComparison.Ordinal))
                {
                    ApplyChangeActionDiscriminator(tool);
                }
                else if (string.Equals(toolName, "preview_process_blueprint", StringComparison.Ordinal))
                {
                    ApplyProcessBlueprintSchema(tool);
                }
                else if (string.Equals(toolName, "get_semantic_operation_schema", StringComparison.Ordinal))
                {
                    ApplySemanticKindSchema(tool);
                }
                else if (string.Equals(toolName, "get_native_operation_schemas", StringComparison.Ordinal))
                {
                    ApplyStringArraySchema(tool, "operaTypes", null);
                }
                else if (string.Equals(toolName, "get_native_operation_field_contract", StringComparison.Ordinal))
                {
                    ApplyStringArraySchema(tool, "fieldNames", null, 12);
                }
                else if (string.Equals(toolName, "get_process_design_guide", StringComparison.Ordinal))
                {
                    ApplyStringArraySchema(tool, "topics", ProcessDesignGuideCatalog.SupportedTopics);
                }
                else if (string.Equals(toolName, "request_capability", StringComparison.Ordinal))
                {
                    ApplyTaskCapabilityDecisionSchema(tool, profile);
                }
                else if (string.Equals(toolName, "search_platform_source", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "maxResults", 1, 100);
                    ApplyToolStringEnum(tool, "fileExtension",
                        ".cs", ".csproj", ".props", ".targets", ".json", ".md",
                        ".js", ".html", ".css", ".ps1", ".xml", ".config", ".yaml", ".yml");
                }
                else if (string.Equals(toolName, "add_variable", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "index", 0, VariableIndexContract.MaximumNormalValueIndex);
                    ApplyToolStringEnum(tool, "scope", VariableScopeContract.Public, VariableScopeContract.Process);
                }
                else if (string.Equals(toolName, "update_variable", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "index", 0, VariableIndexContract.MaximumNormalValueIndex);
                    ApplyToolStringEnum(tool, "scope", VariableScopeContract.Public, VariableScopeContract.Process);
                }
                else if (string.Equals(toolName, "list_variables", StringComparison.Ordinal))
                {
                    ApplyToolStringEnum(tool, "scope",
                        VariableScopeContract.Public,
                        VariableScopeContract.Process,
                        VariableScopeContract.System);
                    ApplyToolNumericRange(tool, "offset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "limit", 1, 100);
                }
                else if (string.Equals(toolName, "get_variable_by_index", StringComparison.Ordinal)
                    || string.Equals(toolName, "set_variable_by_index", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "index", 0, VariableIndexContract.MaximumValueIndex);
                }
                else if (string.Equals(toolName, "get_snapshot", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "offset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "limit", 1, 100);
                }
                else if (string.Equals(toolName, "get_step_detail", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "opOffset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "opLimit", 1, 100);
                }
                else if (string.Equals(toolName, "get_info_log_tail", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "maxCount", 1, 100);
                }
                else if (string.Equals(toolName, "diagnose_proc", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "findingOffset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "findingLimit", 1, 100);
                }
                else if (string.Equals(toolName, "diagnose_issue", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "evidenceOffset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "evidenceLimit", 1, 100);
                }
                else if (string.Equals(toolName, "search_ops", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "offset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "limit", 1, 100);
                }
                else if (string.Equals(toolName, "resolve_proc_target", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "limitPerKeyword", 1, 20);
                    ApplyStringArraySchema(tool, "keywords", null, 6);
                    ApplyPlainTextArrayItems(tool, "keywords", 100, rejectWildcard: true);
                }
                else if (string.Equals(toolName, "discover_project_resources", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "limitPerQuery", 1, 20);
                    ApplyProjectResourceDiscoverySchema(tool);
                }
                else if (string.Equals(toolName, "resolve_authoring_inputs", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "limitPerRequirement", 1, 10);
                    ApplyAuthoringInputResolutionSchema(tool);
                }
                else if (string.Equals(toolName, "resolve_operation_capability", StringComparison.Ordinal))
                {
                    ApplyOperationCapabilityResolutionSchema(tool);
                }
                else if (string.Equals(toolName, "list_io", StringComparison.Ordinal)
                    || string.Equals(toolName, "search_io", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "offset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "limit", 1, 100);
                }
                else if (string.Equals(toolName, "get_operation_references", StringComparison.Ordinal)
                    || string.Equals(toolName, "get_proc_references", StringComparison.Ordinal)
                    || string.Equals(toolName, "trace_resource", StringComparison.Ordinal)
                    || string.Equals(toolName, "search_operation_fields", StringComparison.Ordinal)
                    || string.Equals(toolName, "find_references", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "procOffset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "procLimit", 1, 50);
                    ApplyToolNumericRange(tool, "resultLimit", 1, 100);
                }
                else if (string.Equals(toolName, "audit_proc_batch", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "procOffset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "procLimit", 1, 50);
                    ApplyToolNumericRange(tool, "findingOffset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "findingLimit", 1, 300);
                }
                else if (string.Equals(toolName, "search_alarms", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "offset", 0, int.MaxValue);
                    ApplyToolNumericRange(tool, "limit", 1, 100);
                }
                tools.Add(tool);
            }
            if (tools.Count == 0)
            {
                throw new InvalidOperationException($"MCP工具Profile未注册任何工具:{profile}");
            }
            return tools.OrderBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal).ToList();
        }

        private static void ApplyTaskCapabilityDecisionSchema(McpServerTool tool, string profile)
        {
            JsonObject root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject
                ?? throw new InvalidOperationException("request_capability 参数Schema不是对象。");
            JsonObject decision = FindObjectSchemaWithProperties(
                    root, "version", "action", "capability", "objective", "message", "authorizationQuote",
                    "requiresUserConfirmationAfter", "basis", "findingIds", "reviewHandoff")
                ?? throw new InvalidOperationException("request_capability 缺少decision结构。");
            root["additionalProperties"] = false;
            JsonObject properties = decision["properties"] as JsonObject
                ?? throw new InvalidOperationException("request_capability 决定Schema缺少字段定义。");
            bool reviewProfile = string.Equals(
                profile, AutomationToolProfiles.ProcessReview, StringComparison.Ordinal);
            string[] runStageFields = reviewProfile
                ? new[] { "version", "action", "capability", "objective", "authorizationQuote", "requiresUserConfirmationAfter", "basis", "findingIds", "reviewHandoff" }
                : new[] { "version", "action", "capability", "objective", "authorizationQuote", "requiresUserConfirmationAfter", "basis" };
            string[] completionFields = reviewProfile
                ? new[] { "version", "action", "message", "reviewHandoff" }
                : new[] { "version", "action", "message" };
            JsonObject runStage = CreateTaskDecisionBranch(
                properties,
                "run_stage",
                runStageFields,
                new[] { "version", "action", "capability", "objective" });
            if (runStage["properties"] is JsonObject runProperties)
            {
                if (runProperties["capability"] is JsonObject capability)
                    capability["enum"] = new JsonArray(
                        AutomationToolProfiles.ExecutionProfiles
                            .Select(value => JsonValue.Create(value)).ToArray());
                if (runProperties["objective"] is JsonObject objective)
                {
                    objective["minLength"] = 1;
                    objective["maxLength"] = 500;
                }
                if (runProperties["authorizationQuote"] is JsonObject authorizationQuote)
                    authorizationQuote["maxLength"] = 300;
                if (runProperties["basis"] is JsonObject basis)
                    basis["enum"] = new JsonArray(
                        TaskDecisionBases.DirectUserChange,
                        TaskDecisionBases.ProvenReviewFinding);
                if (runProperties["findingIds"] is JsonObject findingIds)
                {
                    findingIds["minItems"] = 1;
                    findingIds["maxItems"] = 50;
                    findingIds["uniqueItems"] = true;
                }
            }
            JsonObject finish = CreateTaskDecisionBranch(
                properties,
                "finish",
                completionFields,
                new[] { "version", "action", "message" });
            JsonObject askUser = CreateTaskDecisionBranch(
                properties,
                "ask_user",
                completionFields,
                new[] { "version", "action", "message" });
            foreach (JsonObject branch in new[] { finish, askUser })
            {
                if (branch["properties"]?["message"] is JsonObject message)
                {
                    message["minLength"] = 1;
                    message["maxLength"] = 1000;
                }
            }
            if (reviewProfile)
            {
                foreach (JsonObject branch in new[] { runStage, finish, askUser })
                    ApplyReviewHandoffConstraints(branch);
            }
            RemoveGeneratedInputProperty(root, "verifiedFacts");
            string? description = decision["description"]?.GetValue<string>();
            decision.Clear();
            decision["type"] = "object";
            if (!string.IsNullOrWhiteSpace(description)) decision["description"] = description;
            decision["oneOf"] = new JsonArray(runStage, finish, askUser);
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void RemoveGeneratedInputProperty(JsonNode? node, string propertyName)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"] is JsonObject properties)
                {
                    properties.Remove(propertyName);
                }
                if (obj["required"] is JsonArray required)
                {
                    int requiredIndex = required.Select((item, index) => new { item, index })
                        .Where(entry => string.Equals(
                            entry.item?.GetValue<string>(), propertyName, StringComparison.Ordinal))
                        .Select(entry => entry.index)
                        .DefaultIfEmpty(-1)
                        .First();
                    if (requiredIndex >= 0) required.RemoveAt(requiredIndex);
                }
                foreach (KeyValuePair<string, JsonNode?> property in obj.ToList())
                {
                    RemoveGeneratedInputProperty(property.Value, propertyName);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    RemoveGeneratedInputProperty(item, propertyName);
                }
            }
        }

        private static void ApplyReviewHandoffConstraints(JsonObject branch)
        {
            JsonObject? handoff = branch["properties"]?["reviewHandoff"] as JsonObject;
            if (handoff == null) return;
            JsonObject? handoffObject = FindObjectSchemaWithProperties(
                handoff, "status", "summary", "findings");
            if (handoffObject?["properties"] is not JsonObject handoffProperties) return;
            // verifiedFacts 由宿主从本阶段成功工具结果机械附加，不允许模型提交或改写。
            handoffProperties.Remove("verifiedFacts");
            if (handoffObject["required"] is JsonArray required)
            {
                int requiredIndex = required.Select((item, index) => new { item, index })
                    .Where(entry => string.Equals(
                        entry.item?.GetValue<string>(), "verifiedFacts", StringComparison.Ordinal))
                    .Select(entry => entry.index)
                    .DefaultIfEmpty(-1)
                    .First();
                if (requiredIndex >= 0) required.RemoveAt(requiredIndex);
            }
            if (handoffProperties["status"] is JsonObject status)
                status["enum"] = new JsonArray(
                    ReviewHandoffStatuses.ProvenDefect,
                    ReviewHandoffStatuses.ConfigurationGap,
                    ReviewHandoffStatuses.Unresolved,
                    ReviewHandoffStatuses.NoDefect);
            if (handoffProperties["summary"] is JsonObject summary)
            {
                summary["minLength"] = 1;
                summary["maxLength"] = 2000;
            }
            if (handoffProperties["findings"] is JsonObject findings)
            {
                findings["maxItems"] = 50;
                findings["uniqueItems"] = true;
            }
            JsonObject? findingObject = FindObjectSchemaWithProperties(
                handoffObject, "id", "summary", "category", "repairability", "targetIds",
                "evidence", "evidenceFactRefs", "minimalChange");
            if (findingObject?["properties"] is JsonObject findingProperties)
            {
                findingObject["additionalProperties"] = false;
                findingObject["required"] = new JsonArray(
                    "id", "summary", "category", "repairability", "targetIds",
                    "evidence", "evidenceFactRefs", "minimalChange");
                if (findingProperties["category"] is JsonObject category)
                    category["enum"] = new JsonArray(
                        ReviewFindingCategories.StructuralDefect,
                        ReviewFindingCategories.RuntimeDefect,
                        ReviewFindingCategories.SafetyDefect);
                if (findingProperties["repairability"] is JsonObject repairability)
                    repairability["enum"] = new JsonArray(
                        ReviewFindingRepairability.SafeWithoutExternalFacts,
                        ReviewFindingRepairability.RequiresUserChoice);
                foreach (string arrayName in new[] { "targetIds", "evidenceFactRefs" })
                {
                    if (findingProperties[arrayName] is JsonObject array)
                    {
                        array["minItems"] = 1;
                        array["maxItems"] = 20;
                        array["uniqueItems"] = true;
                    }
                }
            }
        }

        private static void ApplyProjectResourceDiscoverySchema(McpServerTool tool)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject rootProperties
                || rootProperties["queries"] is not JsonObject queries)
            {
                throw new InvalidOperationException("discover_project_resources 参数Schema缺少queries。");
            }
            queries["minItems"] = 1;
            queries["maxItems"] = 12;
            queries["uniqueItems"] = true;
            JsonObject? queryObject = FindObjectSchemaWithProperties(
                queries, "kind", "keywords", "ioType", "stationIndex");
            if (queryObject?["properties"] is not JsonObject queryProperties)
                throw new InvalidOperationException("discover_project_resources 参数Schema缺少查询项结构。");
            queryObject["additionalProperties"] = false;
            queryObject["required"] = new JsonArray("kind", "keywords");
            if (queryProperties["kind"] is JsonObject kind)
                kind["enum"] = new JsonArray(
                    "process", "io", "variable", "station", "point", "data_struct",
                    "alarm", "communication", "plc");
            if (queryProperties["keywords"] is JsonObject keywords)
            {
                keywords["minItems"] = 1;
                keywords["maxItems"] = 6;
                keywords["uniqueItems"] = true;
                if (keywords["items"] is JsonObject keyword)
                {
                    keyword["minLength"] = 1;
                    keyword["maxLength"] = 100;
                    keyword["pattern"] = "^(?!\\*$).*\\S.*$";
                }
            }
            if (queryProperties["ioType"] is JsonObject ioType)
                ioType["enum"] = new JsonArray("通用输入", "通用输出");
            if (queryProperties["stationIndex"] is JsonObject stationIndex)
                stationIndex["minimum"] = 0;
            queryObject["allOf"] = new JsonArray
            {
                new JsonObject
                {
                    ["if"] = new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            ["kind"] = new JsonObject { ["const"] = "io" }
                        },
                        ["required"] = new JsonArray("kind")
                    },
                    ["else"] = new JsonObject
                    {
                        ["not"] = new JsonObject { ["required"] = new JsonArray("ioType") }
                    }
                },
                new JsonObject
                {
                    ["if"] = new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            ["kind"] = new JsonObject { ["const"] = "point" }
                        },
                        ["required"] = new JsonArray("kind")
                    },
                    ["then"] = new JsonObject { ["required"] = new JsonArray("stationIndex") },
                    ["else"] = new JsonObject
                    {
                        ["not"] = new JsonObject { ["required"] = new JsonArray("stationIndex") }
                    }
                }
            };
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyAuthoringInputResolutionSchema(McpServerTool tool)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject rootProperties
                || rootProperties["requirements"] is not JsonObject requirements)
            {
                throw new InvalidOperationException("resolve_authoring_inputs 参数Schema缺少requirements。");
            }
            requirements["minItems"] = 1;
            requirements["maxItems"] = 20;
            JsonObject? item = FindObjectSchemaWithProperties(
                requirements, "key", "kind", "names", "purpose", "ioType",
                "requiredType", "requiredScope", "ownerProcId", "stationIndex");
            if (item?["properties"] is not JsonObject properties)
                throw new InvalidOperationException("resolve_authoring_inputs 缺少绑定意图结构。");
            item["additionalProperties"] = false;
            item["required"] = new JsonArray("key", "kind", "names", "purpose");
            if (properties["key"] is JsonObject key)
            {
                key["minLength"] = 1;
                key["maxLength"] = 80;
            }
            if (properties["kind"] is JsonObject kind)
                kind["enum"] = new JsonArray(
                    "process", "io", "variable", "station", "point", "data_struct",
                    "alarm", "communication", "plc");
            if (properties["names"] is JsonObject names)
            {
                names["minItems"] = 1;
                names["maxItems"] = 4;
                names["uniqueItems"] = true;
                if (names["items"] is JsonObject name)
                {
                    name["minLength"] = 1;
                    name["maxLength"] = 100;
                    name["pattern"] = "^(?!\\*$).*\\S.*$";
                }
            }
            if (properties["purpose"] is JsonObject purpose)
            {
                purpose["minLength"] = 1;
                purpose["maxLength"] = 200;
            }
            if (properties["ioType"] is JsonObject ioType)
                ioType["enum"] = new JsonArray("通用输入", "通用输出");
            if (properties["requiredType"] is JsonObject requiredType)
                requiredType["enum"] = new JsonArray("double", "string");
            if (properties["requiredScope"] is JsonObject requiredScope)
                requiredScope["enum"] = new JsonArray(
                    VariableScopeContract.Public,
                    VariableScopeContract.Process,
                    VariableScopeContract.System);
            if (properties["stationIndex"] is JsonObject stationIndex)
                stationIndex["minimum"] = 0;
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyOperationCapabilityResolutionSchema(McpServerTool tool)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject rootProperties
                || rootProperties["intents"] is not JsonObject intents)
            {
                throw new InvalidOperationException("resolve_operation_capability 参数Schema缺少intents。");
            }
            intents["minItems"] = 1;
            intents["maxItems"] = 12;
            JsonObject? item = FindObjectSchemaWithProperties(intents, "key", "intent");
            if (item?["properties"] is not JsonObject properties)
                throw new InvalidOperationException("resolve_operation_capability 缺少动作意图结构。");
            item["additionalProperties"] = false;
            item["required"] = new JsonArray("key", "intent");
            foreach (string propertyName in new[] { "key", "intent" })
            {
                if (properties[propertyName] is JsonObject property)
                {
                    property["minLength"] = 1;
                    property["maxLength"] = propertyName == "key" ? 80 : 200;
                }
            }
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyPlainTextArrayItems(
            McpServerTool tool,
            string propertyName,
            int maximumLength,
            bool rejectWildcard)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject properties
                || properties[propertyName] is not JsonObject arraySchema
                || arraySchema["items"] is not JsonObject itemSchema)
            {
                throw new InvalidOperationException($"{tool.ProtocolTool.Name} 参数Schema缺少文本数组：{propertyName}");
            }
            itemSchema["minLength"] = 1;
            itemSchema["maxLength"] = maximumLength;
            itemSchema["pattern"] = rejectWildcard ? "^(?!\\*$).*\\S.*$" : "^.*\\S.*$";
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static JsonObject CreateTaskDecisionBranch(
            JsonObject sourceProperties,
            string actionName,
            IReadOnlyCollection<string> propertyNames,
            IReadOnlyCollection<string> requiredNames)
        {
            var branchProperties = new JsonObject();
            foreach (string name in propertyNames)
            {
                branchProperties[name] = sourceProperties[name]?.DeepClone();
            }
            if (branchProperties["version"] is JsonObject version)
                version["enum"] = new JsonArray(1);
            if (branchProperties["action"] is JsonObject action)
                action["enum"] = new JsonArray(actionName);
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = branchProperties,
                ["required"] = new JsonArray(
                    requiredNames.Select(name => JsonValue.Create(name)).ToArray())
            };
        }

        private static JsonObject? FindObjectSchemaWithProperties(
            JsonNode? node,
            params string[] propertyNames)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"] is JsonObject properties
                    && propertyNames.All(properties.ContainsKey))
                    return obj;
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    JsonObject? found = FindObjectSchemaWithProperties(property.Value, propertyNames);
                    if (found != null) return found;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    JsonObject? found = FindObjectSchemaWithProperties(item, propertyNames);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static HashSet<string> ToolSet(params string[] names)
        {
            return new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        private static void ApplySemanticKindSchema(McpServerTool tool)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject properties
                || properties["kind"] is not JsonObject kindSchema)
            {
                throw new InvalidOperationException($"{tool.ProtocolTool.Name} 参数Schema缺少字段：kind");
            }
            kindSchema["enum"] = new JsonArray(
                SemanticOperationKinds.SupportedKinds.Split('、')
                    .Select(value => JsonValue.Create(value)).ToArray());
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyStringArraySchema(
            McpServerTool tool,
            string propertyName,
            IEnumerable<string>? allowedValues,
            int? maximumItems = null)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject properties
                || properties[propertyName] is not JsonObject arraySchema)
            {
                throw new InvalidOperationException($"{tool.ProtocolTool.Name} 参数Schema缺少字段：{propertyName}");
            }
            arraySchema["minItems"] = 1;
            if (maximumItems.HasValue) arraySchema["maxItems"] = maximumItems.Value;
            arraySchema["uniqueItems"] = true;
            if (allowedValues != null)
            {
                if (arraySchema["items"] is not JsonObject itemSchema)
                {
                    itemSchema = new JsonObject { ["type"] = "string" };
                    arraySchema["items"] = itemSchema;
                }
                itemSchema["enum"] = new JsonArray(
                    allowedValues.Select(value => JsonValue.Create(value)).ToArray());
            }
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyToolNumericRange(
            McpServerTool tool, string propertyName, int minimum, int maximum)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject properties
                || properties[propertyName] is not JsonObject propertySchema)
            {
                throw new InvalidOperationException($"{tool.ProtocolTool.Name} 参数Schema缺少字段：{propertyName}");
            }
            propertySchema["minimum"] = minimum;
            propertySchema["maximum"] = maximum;
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyToolStringEnum(
            McpServerTool tool, string propertyName, params string[] allowedValues)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject properties
                || properties[propertyName] is not JsonObject propertySchema)
            {
                throw new InvalidOperationException($"{tool.ProtocolTool.Name} 参数Schema缺少字段：{propertyName}");
            }
            propertySchema["enum"] = new JsonArray(
                allowedValues.Select(value => JsonValue.Create(value)).ToArray());
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyChangeActionDiscriminator(McpServerTool tool)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            JsonObject? actionSchema = FindChangeActionSchema(root);
            JsonObject? operationSchema = FindSemanticOperationSchema(root);
            JsonObject? positionSchema = FindPositionSchema(root);
            if (root == null || actionSchema == null || operationSchema == null || positionSchema == null)
                throw new InvalidOperationException("preview_change_set 生成Schema缺少动作或语义指令定义。");

            if (root["properties"] is not JsonObject rootProperties
                || rootProperties["changeSet"] is not JsonObject changeSetSchema)
            {
                throw new InvalidOperationException("preview_change_set 生成Schema缺少changeSet参数定义。");
            }
            root["additionalProperties"] = false;
            changeSetSchema["additionalProperties"] = false;

            if (actionSchema["properties"] is not JsonObject actionProperties
                || operationSchema["properties"] is not JsonObject operationProperties)
            {
                throw new InvalidOperationException("preview_change_set 生成Schema缺少动作或语义指令字段。");
            }

            // entryMode 只解决 Blueprint 的跨步骤默认入口；普通 ChangeSet 必须提交精确目标。
            RemoveGeneratedInputProperty(operationSchema, "entryMode");

            ApplyPositionSchema(positionSchema);
            JsonObject definitions = ApplySemanticOperationDiscriminator(root, operationSchema);
            ApplyVariableChangeSchema(root);

            // 动作分支最后生成，确保其中的 operation 载荷复制的是已经闭合的语义判别联合。
            actionSchema["oneOf"] = new JsonArray
            {
                ActionShape(actionProperties, "process.create", new[] { "process" }),
                ActionShape(actionProperties, "process.update", new[] { "targetProcess", "process" }),
                ActionShape(actionProperties, "process.delete", new[] { "targetProcess" }),
                ActionShape(actionProperties, "process.delete_all", Array.Empty<string>()),
                ActionShape(actionProperties, "step.append", new[] { "targetProcess", "step" }),
                ActionShape(actionProperties, "step.insert", new[] { "targetProcess", "position", "step" }),
                ActionShape(actionProperties, "step.update", new[] { "targetProcess", "targetStep", "step" }),
                ActionShape(actionProperties, "step.delete", new[] { "targetProcess", "targetStep" }),
                ActionShape(actionProperties, "step.move", new[] { "targetProcess", "targetStep", "position" }),
                ActionShape(actionProperties, "operation.append", new[] { "targetProcess", "targetStep", "operation" }),
                ActionShape(actionProperties, "operation.insert", new[] { "targetProcess", "targetStep", "position", "operation" }),
                ActionShape(actionProperties, "operation.update", new[] { "targetProcess", "targetOperation", "operation" }, "targetStep"),
                ActionShape(actionProperties, "operation.replace", new[] { "targetProcess", "targetOperation", "operation" }, "targetStep"),
                ActionShape(actionProperties, "operation.delete", new[] { "targetProcess", "targetOperation" }, "targetStep"),
                ActionShape(actionProperties, "operation.move", new[] { "targetProcess", "targetOperation", "position" }, "targetStep")
            };
            ReplaceUnionPropertyWithReference(
                actionSchema,
                "operation",
                "#/$defs/semanticOperation");
            CompactRepeatedUnionProperties(actionSchema, definitions, "action", "type", "operation");
            actionSchema["x-localKeyScope"] = "current_change_set";
            actionSchema.Remove("properties");
            actionSchema.Remove("required");
            actionSchema.Remove("additionalProperties");
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyProcessBlueprintSchema(McpServerTool tool)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            JsonObject? operationSchema = FindSemanticOperationSchema(root);
            JsonObject? targetSchema = FindObjectSchemaWithProperties(
                operationSchema, "operationId", "stepId", "stepKey", "operationKey");
            if (root?["properties"] is not JsonObject rootProperties
                || rootProperties["blueprint"] is not JsonObject blueprintSchema
                || blueprintSchema["properties"] is not JsonObject blueprintProperties
                || operationSchema == null
                || targetSchema == null)
            {
                throw new InvalidOperationException("preview_process_blueprint 生成Schema缺少蓝图或语义指令定义。");
            }

            root["additionalProperties"] = false;
            blueprintSchema["additionalProperties"] = false;
            blueprintSchema["required"] = new JsonArray("process", "steps");
            if (blueprintProperties["steps"] is JsonObject stepsArray)
                stepsArray["minItems"] = 1;

            JsonObject? retrySchema = CloseBlueprintObject(
                root,
                new[] { "entryOperationKey", "counterVariable", "maxAttempts", "retryDecisionOperationKey", "resetVariables", "clearVariables" },
                new[] { "entryOperationKey", "maxAttempts", "retryDecisionOperationKey" });
            if (retrySchema?["properties"] is JsonObject retryProperties)
            {
                // counterVariable 仅供编译器内部填充，不进入模型输入契约。
                retryProperties.Remove("counterVariable");
                foreach (string propertyName in new[] { "entryOperationKey", "retryDecisionOperationKey" })
                {
                    if (retryProperties[propertyName] is JsonObject keySchema)
                        keySchema["pattern"] = "^[A-Za-z][A-Za-z0-9_-]{0,31}$";
                }
                if (retryProperties["maxAttempts"] is JsonObject maxAttemptsSchema)
                {
                    maxAttemptsSchema["minimum"] = 1;
                    maxAttemptsSchema["maximum"] = 100;
                }
                foreach (string propertyName in new[] { "resetVariables", "clearVariables" })
                {
                    if (retryProperties[propertyName] is JsonObject arraySchema)
                    {
                        arraySchema["minItems"] = 1;
                        arraySchema["uniqueItems"] = true;
                    }
                }
            }

            CloseBlueprintObject(root, new[] { "name", "autoStart", "disable" }, new[] { "name" });
            JsonObject? blueprintOperationsArray = null;
            JsonObject? stepSchema = CloseBlueprintObject(
                root,
                new[] { "key", "name", "disable", "operations" },
                new[] { "name", "operations" });
            if (stepSchema?["properties"] is JsonObject stepProperties)
            {
                if (stepProperties["key"] is JsonObject keySchema)
                    keySchema["pattern"] = "^[A-Za-z][A-Za-z0-9_-]{0,31}$";
                if (stepProperties["operations"] is JsonObject operationsArray)
                {
                    operationsArray["minItems"] = 1;
                    blueprintOperationsArray = operationsArray;
                }
            }

            JsonObject? variableSchema = CloseBlueprintObject(
                root,
                new[] { "name", "scope", "index", "type", "value", "note", "policy" },
                new[] { "name", "scope" });
            if (variableSchema?["properties"] is JsonObject variableProperties)
            {
                if (variableProperties["name"] is JsonObject nameSchema)
                {
                    nameSchema["minLength"] = 1;
                    nameSchema["pattern"] = "\\S";
                }
                if (variableProperties["scope"] is JsonObject scopeSchema)
                    scopeSchema["enum"] = new JsonArray(
                        VariableScopeContract.Public, VariableScopeContract.Process, VariableScopeContract.System);
                if (variableProperties["type"] is JsonObject typeSchema)
                    typeSchema["enum"] = new JsonArray(
                        VariableChangeContract.DoubleType, VariableChangeContract.StringType);
                if (variableProperties["policy"] is JsonObject policySchema)
                    policySchema["enum"] = new JsonArray(
                        VariableChangeContract.ReusePolicy, VariableChangeContract.CreatePolicy,
                        VariableChangeContract.UpdatePolicy, VariableChangeContract.ReplacePolicy,
                        VariableChangeContract.RequirePolicy);
                if (variableProperties["index"] is JsonObject indexSchema)
                {
                    indexSchema["minimum"] = 0;
                    indexSchema["maximum"] = VariableIndexContract.MaximumNormalValueIndex;
                }
            }

            ApplyBlueprintTargetSchema(targetSchema);
            ApplySemanticOperationDiscriminator(root, operationSchema);
            if (blueprintOperationsArray != null)
            {
                blueprintOperationsArray["items"] = new JsonObject
                {
                    ["$ref"] = "#/$defs/semanticOperation"
                };
            }
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyBlueprintTargetSchema(JsonObject targetSchema)
        {
            JsonObject properties = targetSchema["properties"] as JsonObject
                ?? throw new InvalidOperationException("蓝图跳转目标Schema缺少字段定义。");
            properties.Remove("operationId");
            properties.Remove("stepId");
            targetSchema["additionalProperties"] = false;
            targetSchema.Remove("required");
            targetSchema["anyOf"] = new JsonArray
            {
                new JsonObject { ["required"] = new JsonArray("operationKey") },
                new JsonObject { ["required"] = new JsonArray("stepKey") }
            };
            targetSchema["description"] =
                "蓝图只能引用本次blueprint内的局部key。当前步骤内填写operationKey；跨步骤优先只填stepKey并自动进入该步骤首指令。确需跳入目标步骤中段时同时填写operationKey与entryMode=operation。";
            if (properties["operationKey"] is JsonObject operationKey)
            {
                operationKey["minLength"] = 1;
                operationKey["pattern"] = "^[A-Za-z][A-Za-z0-9_-]{0,31}$";
                operationKey["description"] =
                    "目标指令的局部key，取自steps[].operations[].key；不是步骤key，也不能引用已提交指令。";
            }
            if (properties["stepKey"] is JsonObject stepKey)
            {
                stepKey["minLength"] = 1;
                stepKey["pattern"] = "^[A-Za-z][A-Za-z0-9_-]{0,31}$";
                stepKey["description"] =
                    "跨步骤时填写目标步骤局部key；省略operationKey时自动进入该步骤首指令。当前步骤内定位时省略。";
            }
            if (properties["entryMode"] is JsonObject entryMode)
            {
                entryMode["enum"] = new JsonArray("first", "operation");
                entryMode["description"] =
                    "跨步骤入口模式；通常省略。只有明确跳入目标步骤中段时填写operation并同时提供operationKey。";
            }
        }

        private static JsonObject? CloseBlueprintObject(
            JsonNode? node,
            IReadOnlyCollection<string> propertyNames,
            IReadOnlyCollection<string> requiredNames)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"] is JsonObject properties
                    && propertyNames.All(properties.ContainsKey)
                    && properties.Count == propertyNames.Count)
                {
                    obj["additionalProperties"] = false;
                    obj["required"] = new JsonArray(
                        requiredNames.Select(name => JsonValue.Create(name)).ToArray());
                    return obj;
                }
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    JsonObject? found = CloseBlueprintObject(property.Value, propertyNames, requiredNames);
                    if (found != null) return found;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    JsonObject? found = CloseBlueprintObject(item, propertyNames, requiredNames);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static JsonObject ApplySemanticOperationDiscriminator(
            JsonObject root,
            JsonObject operationSchema)
        {
            if (operationSchema["properties"] is not JsonObject operationProperties)
            {
                throw new InvalidOperationException("语义指令Schema缺少字段定义。");
            }
            ApplyNumericRange(operationSchema, "milliseconds", 0, 86400000);
            ApplyNumericRange(operationSchema, "autoCloseMs", 1, 3600000);
            ApplyNumericRange(operationSchema, "afterMs", 0, 3600000);
            ApplyNumericRange(operationSchema, "timeoutMs", 1, 86400000);
            ApplyIoConditionsSchema(operationSchema);
            ApplyIoOutputsSchema(operationSchema);
            ApplyStringEnum(operationSchema, "conditionLogic", "all", "any");
            if (operationProperties["key"] is JsonObject keySchema)
                keySchema["pattern"] = "^[A-Za-z][A-Za-z0-9_-]{0,31}$";

            operationSchema["oneOf"] = new JsonArray
            {
                SemanticShape(operationProperties, "variable.set", new[] { "variable", "value" }),
                SemanticShape(operationProperties, "variable.clear", new[] { "variable" }),
                SemanticShape(operationProperties, "variable.copy", new[] { "sourceVariable", "targetVariable" }),
                SemanticShape(operationProperties, "variable.add", new[] { "variable", "amount" }),
                SemanticShape(operationProperties, "variable.compute", new[] { "sourceVariable", "operator", "outputVariable" },
                    "operandValue", "operandVariable"),
                SemanticShape(operationProperties, "wait", new[] { "milliseconds" }),
                SemanticShape(operationProperties, "flow.goto", Array.Empty<string>(), "target"),
                SemanticShape(operationProperties, "flow.end", Array.Empty<string>()),
                SemanticShape(operationProperties, "branch.number_compare", new[] { "variable", "comparison", "compareValue" },
                    "whenTrue", "whenFalse"),
                SemanticShape(operationProperties, "branch.number_range", new[] { "variable", "min", "max" },
                    "includeBounds", "whenTrue", "whenFalse"),
                SemanticShape(operationProperties, "branch.io", new[] { "conditions" },
                    "conditionLogic", "whenTrue", "whenFalse"),
                SemanticShape(operationProperties, "alarm.raise", new[] { "message" },
                    "buttonText", "target"),
                SemanticShape(operationProperties, "popup.message", new[] { "message" },
                    "buttonText", "autoCloseMs", "target"),
                SemanticShape(operationProperties, "popup.variable", new[] { "variable" },
                    "buttonText", "autoCloseMs", "target"),
                SemanticShape(operationProperties, "config.placeholder", new[] { "message" }),
                SemanticShape(operationProperties, "io.write", new[] { "outputs" }),
                SemanticShape(operationProperties, "io.wait", new[] { "conditions", "timeoutMs" }, "onFailure"),
                SemanticShape(operationProperties, "process.control", Array.Empty<string>(), "process", "action", "afterMs"),
                SemanticShape(operationProperties, "process.wait", Array.Empty<string>(), "process", "expectedState", "timeoutMs", "afterMs"),
                SemanticShape(operationProperties, "native.operation", new[] { "operaType", "fields" }, "clearFields")
            };
            operationSchema["x-symbolicTargetScope"] = "operation_id_or_change_set_key";
            operationSchema.Remove("properties");
            operationSchema.Remove("required");
            operationSchema.Remove("additionalProperties");

            JsonObject definitions = GetOrCreateDefinitions(root);
            CompactRepeatedUnionProperties(operationSchema, definitions, "semantic", "kind");
            definitions["semanticOperation"] = operationSchema.DeepClone();
            return definitions;
        }

        private static JsonObject GetOrCreateDefinitions(JsonObject root)
        {
            if (root["$defs"] is JsonObject definitions)
            {
                return definitions;
            }

            definitions = new JsonObject();
            root["$defs"] = definitions;
            return definitions;
        }

        private static void ReplaceUnionPropertyWithReference(
            JsonObject unionSchema,
            string propertyName,
            string reference)
        {
            if (unionSchema["oneOf"] is not JsonArray branches)
            {
                throw new InvalidOperationException("判别联合缺少oneOf：" + propertyName);
            }

            foreach (JsonNode? node in branches)
            {
                if (node is JsonObject branch
                    && branch["properties"] is JsonObject properties
                    && properties.ContainsKey(propertyName))
                {
                    properties[propertyName] = new JsonObject { ["$ref"] = reference };
                }
            }
        }

        private static void CompactRepeatedUnionProperties(
            JsonObject unionSchema,
            JsonObject definitions,
            string definitionPrefix,
            params string[] excludedProperties)
        {
            if (unionSchema["oneOf"] is not JsonArray branches)
            {
                throw new InvalidOperationException("判别联合缺少oneOf：" + definitionPrefix);
            }

            var excluded = new HashSet<string>(excludedProperties ?? Array.Empty<string>(), StringComparer.Ordinal);
            var occurrences = new Dictionary<string, List<Tuple<JsonObject, string, JsonNode>>>(StringComparer.Ordinal);
            foreach (JsonNode? node in branches)
            {
                if (node is not JsonObject branch || branch["properties"] is not JsonObject properties)
                {
                    continue;
                }
                foreach (KeyValuePair<string, JsonNode?> property in properties)
                {
                    if (excluded.Contains(property.Key) || property.Value == null)
                    {
                        continue;
                    }
                    string schemaIdentity = property.Key + "\n" + property.Value.ToJsonString();
                    if (!occurrences.TryGetValue(
                        schemaIdentity,
                        out List<Tuple<JsonObject, string, JsonNode>>? values))
                    {
                        values = new List<Tuple<JsonObject, string, JsonNode>>();
                        occurrences[schemaIdentity] = values;
                    }
                    values.Add(Tuple.Create(properties, property.Key, property.Value));
                }
            }

            var definitionNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (List<Tuple<JsonObject, string, JsonNode>> values in occurrences.Values
                .Where(items => items.Count > 1))
            {
                string fieldName = values[0].Item2;
                string baseName = definitionPrefix + ToDefinitionName(fieldName);
                definitionNameCounts.TryGetValue(baseName, out int nameIndex);
                definitionNameCounts[baseName] = nameIndex + 1;
                string definitionName = nameIndex == 0 ? baseName : baseName + (nameIndex + 1);
                definitions[definitionName] = values[0].Item3.DeepClone();
                string reference = "#/$defs/" + definitionName;
                foreach (Tuple<JsonObject, string, JsonNode> occurrence in values)
                {
                    occurrence.Item1[occurrence.Item2] = new JsonObject { ["$ref"] = reference };
                }
            }
        }

        private static string ToDefinitionName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Value";
            }

            var builder = new System.Text.StringBuilder(value.Length);
            bool uppercaseNext = true;
            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    uppercaseNext = true;
                    continue;
                }
                builder.Append(uppercaseNext ? char.ToUpperInvariant(character) : character);
                uppercaseNext = false;
            }
            return builder.Length == 0 ? "Value" : builder.ToString();
        }

        private static void ApplyVariableChangeSchema(JsonObject root)
        {
            JsonObject? variableSchema = FindVariableChangeSchema(root);
            if (variableSchema?["properties"] is not JsonObject properties
                || properties["name"] is not JsonObject nameSchema
                || properties["scope"] is not JsonObject scopeSchema
                || properties["type"] is not JsonObject typeSchema
                || properties["policy"] is not JsonObject policySchema)
            {
                throw new InvalidOperationException("preview_change_set 生成Schema缺少变量声明定义。");
            }
            nameSchema["minLength"] = 1;
            nameSchema["pattern"] = "\\S";
            scopeSchema["enum"] = new JsonArray(
                VariableScopeContract.Public,
                VariableScopeContract.Process,
                VariableScopeContract.System);
            typeSchema["enum"] = new JsonArray(
                VariableChangeContract.DoubleType,
                VariableChangeContract.StringType);
            policySchema["enum"] = new JsonArray(
                VariableChangeContract.ReusePolicy,
                VariableChangeContract.CreatePolicy,
                VariableChangeContract.UpdatePolicy,
                VariableChangeContract.ReplacePolicy,
                VariableChangeContract.RequirePolicy);
            variableSchema["required"] = new JsonArray("name", "scope");
            variableSchema["additionalProperties"] = false;
            variableSchema["allOf"] = new JsonArray
            {
                new JsonObject
                {
                    ["if"] = new JsonObject
                    {
                        ["properties"] = new JsonObject { ["scope"] = new JsonObject { ["const"] = VariableScopeContract.Process } },
                        ["required"] = new JsonArray("scope")
                    },
                    ["then"] = new JsonObject { ["required"] = new JsonArray("ownerProcess") }
                },
                new JsonObject
                {
                    ["if"] = new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            ["scope"] = new JsonObject
                            {
                                ["enum"] = new JsonArray(VariableScopeContract.Public, VariableScopeContract.System)
                            }
                        },
                        ["required"] = new JsonArray("scope")
                    },
                    ["then"] = new JsonObject
                    {
                        ["not"] = new JsonObject { ["required"] = new JsonArray("ownerProcess") }
                    }
                }
            };
            IReadOnlyList<JsonObject> processSelectorSchemas = FindProcessSelectorSchemas(root);
            if (processSelectorSchemas.Count == 0)
            {
                throw new InvalidOperationException("preview_change_set 生成Schema缺少流程选择器定义。");
            }
            foreach (JsonObject processSelectorSchema in processSelectorSchemas)
            {
                processSelectorSchema.Remove("required");
                processSelectorSchema["oneOf"] = new JsonArray(
                    new JsonObject { ["required"] = new JsonArray("procId") },
                    new JsonObject { ["required"] = new JsonArray("name") },
                    new JsonObject { ["required"] = new JsonArray("key") });
                processSelectorSchema["additionalProperties"] = false;
            }
        }

        private static IReadOnlyList<JsonObject> FindProcessSelectorSchemas(JsonNode? node)
        {
            var results = new List<JsonObject>();
            CollectProcessSelectorSchemas(node, results);
            return results;
        }

        private static void CollectProcessSelectorSchemas(JsonNode? node, ICollection<JsonObject> results)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"] is JsonObject properties
                    && properties.ContainsKey("procId")
                    && properties.ContainsKey("name")
                    && properties.ContainsKey("key"))
                {
                    results.Add(obj);
                }
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    CollectProcessSelectorSchemas(property.Value, results);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    CollectProcessSelectorSchemas(item, results);
                }
            }
        }

        private static JsonObject? FindVariableChangeSchema(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"] is JsonObject properties
                    && properties.ContainsKey("policy")
                    && properties.ContainsKey("name")
                    && properties.ContainsKey("scope")
                    && properties.ContainsKey("ownerProcess"))
                {
                    return obj;
                }
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    JsonObject? found = FindVariableChangeSchema(property.Value);
                    if (found != null) return found;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    JsonObject? found = FindVariableChangeSchema(item);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static void ApplyNumericRange(
            JsonObject operationSchema, string fieldName, int minimum, int maximum)
        {
            if (operationSchema["properties"] is not JsonObject properties
                || properties[fieldName] is not JsonObject fieldSchema)
            {
                throw new InvalidOperationException($"preview_change_set 参数Schema缺少字段：{fieldName}");
            }
            fieldSchema["minimum"] = minimum;
            fieldSchema["maximum"] = maximum;
        }

        private static void ApplyIoConditionsSchema(JsonObject operationSchema)
        {
            if (operationSchema["properties"] is not JsonObject properties
                || properties["conditions"] is not JsonObject conditionsSchema
                || conditionsSchema["items"] is not JsonObject itemSchema)
            {
                throw new InvalidOperationException("preview_change_set 参数Schema缺少conditions字段定义。");
            }
            conditionsSchema["minItems"] = 1;
            itemSchema["required"] = new JsonArray("io", "state");
            itemSchema["additionalProperties"] = false;
        }

        private static void ApplyIoOutputsSchema(JsonObject operationSchema)
        {
            if (operationSchema["properties"] is not JsonObject properties
                || properties["outputs"] is not JsonObject outputsSchema
                || outputsSchema["items"] is not JsonObject itemSchema)
            {
                throw new InvalidOperationException("preview_change_set 参数Schema缺少outputs字段定义。");
            }
            outputsSchema["minItems"] = 1;
            itemSchema["required"] = new JsonArray("io", "state");
            itemSchema["additionalProperties"] = false;
        }

        private static void ApplyStringEnum(JsonObject operationSchema, string fieldName, params string[] values)
        {
            if (operationSchema["properties"] is not JsonObject properties
                || properties[fieldName] is not JsonObject fieldSchema)
            {
                throw new InvalidOperationException($"preview_change_set 参数Schema缺少字段：{fieldName}");
            }
            fieldSchema["enum"] = new JsonArray(values.Select(value => JsonValue.Create(value)).ToArray());
        }

        private static JsonObject? FindChangeActionSchema(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"] is JsonObject properties
                    && properties.ContainsKey("type")
                    && properties.ContainsKey("targetProcess")
                    && properties.ContainsKey("operation"))
                    return obj;
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    JsonObject? found = FindChangeActionSchema(property.Value);
                    if (found != null) return found;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    JsonObject? found = FindChangeActionSchema(item);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static JsonObject? FindSemanticOperationSchema(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"] is JsonObject properties
                    && properties.ContainsKey("kind")
                    && properties.ContainsKey("sourceVariable")
                    && properties.ContainsKey("operaType"))
                    return obj;
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    JsonObject? found = FindSemanticOperationSchema(property.Value);
                    if (found != null) return found;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    JsonObject? found = FindSemanticOperationSchema(item);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static JsonObject? FindPositionSchema(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"] is JsonObject properties
                    && properties.ContainsKey("beforeId")
                    && properties.ContainsKey("beforeKey")
                    && properties.ContainsKey("afterId")
                    && properties.ContainsKey("afterKey"))
                    return obj;
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    JsonObject? found = FindPositionSchema(property.Value);
                    if (found != null) return found;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    JsonObject? found = FindPositionSchema(item);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static void ApplyPositionSchema(JsonObject positionSchema)
        {
            positionSchema["oneOf"] = new JsonArray
            {
                RequiredFieldShape("beforeId", "现有对象Guid"),
                RequiredFieldShape("beforeKey", "当前ChangeSet局部key"),
                RequiredFieldShape("afterId", "现有对象Guid"),
                RequiredFieldShape("afterKey", "当前ChangeSet局部key")
            };
            if (positionSchema["properties"] is not JsonObject properties) return;
            foreach (string field in new[] { "beforeId", "afterId" })
            {
                if (properties[field] is JsonObject schema)
                {
                    schema["format"] = "uuid";
                    schema["minLength"] = 36;
                    schema["maxLength"] = 36;
                }
            }
            foreach (string field in new[] { "beforeKey", "afterKey" })
            {
                if (properties[field] is JsonObject schema)
                {
                    schema["pattern"] = "^[A-Za-z][A-Za-z0-9_-]{0,31}$";
                }
            }
        }

        private static JsonObject RequiredFieldShape(string field, string title)
        {
            return new JsonObject
            {
                ["title"] = title,
                ["required"] = new JsonArray(field)
            };
        }

        private static JsonObject ActionShape(
            JsonObject sourceProperties, string type, string[] requiredPayload, params string[] optionalPayload)
        {
            return ClosedDiscriminatorShape(
                sourceProperties, "type", type, requiredPayload, optionalPayload);
        }

        private static JsonObject SemanticShape(
            JsonObject sourceProperties, string kind, string[] requiredPayload, params string[] optionalPayload)
        {
            string[] commonOptionalFields = { "opId", "key", "name" };
            return ClosedDiscriminatorShape(sourceProperties, "kind", kind, requiredPayload,
                commonOptionalFields.Concat(optionalPayload).ToArray());
        }

        private static JsonObject ClosedDiscriminatorShape(
            JsonObject sourceProperties,
            string discriminatorField,
            string discriminatorValue,
            IEnumerable<string> requiredPayload,
            IEnumerable<string> optionalPayload)
        {
            var branchProperties = new JsonObject();
            foreach (string field in new[] { discriminatorField }
                .Concat(requiredPayload)
                .Concat(optionalPayload)
                .Distinct(StringComparer.Ordinal))
            {
                if (!sourceProperties.TryGetPropertyValue(field, out JsonNode? sourceSchema)
                    || sourceSchema == null)
                {
                    throw new InvalidOperationException(
                        $"preview_change_set 参数Schema缺少字段：{field}");
                }
                branchProperties[field] = sourceSchema.DeepClone();
            }
            if (branchProperties[discriminatorField] is not JsonObject discriminatorSchema)
            {
                throw new InvalidOperationException(
                    $"preview_change_set 参数Schema的判别字段无效：{discriminatorField}");
            }
            discriminatorSchema["const"] = discriminatorValue;

            var required = new JsonArray { discriminatorField };
            foreach (string field in requiredPayload.Distinct(StringComparer.Ordinal)) required.Add(field);
            return new JsonObject
            {
                ["title"] = discriminatorValue,
                ["type"] = "object",
                ["properties"] = branchProperties,
                ["required"] = required,
                ["additionalProperties"] = false
            };
        }

        public static IReadOnlyList<McpServerTool> CreateEditorTools()
        {
            return CreateTools("Editor");
        }
    }

    internal sealed class DynamicMcpToolRegistry
    {
        private readonly object syncRoot = new object();
        private readonly IReadOnlyDictionary<string, McpServerTool> allTools;
        private readonly IReadOnlyDictionary<string, HashSet<string>> profileToolNames;
        private readonly HashSet<string> fullPermissionToolNames;
        private string profile = string.Empty;
        private bool fullPermissionEnabled;

        public DynamicMcpToolRegistry(string initialProfile)
        {
            var profileTools = AutomationToolProfiles.All.ToDictionary(
                item => item,
                item => McpToolProfile.CreateTools(item),
                StringComparer.Ordinal);
            IReadOnlyList<McpServerTool> editorTools = profileTools[AutomationToolProfiles.Editor];
            IReadOnlyList<McpServerTool> fullPermissionEditorTools =
                McpToolProfile.CreateTools(AutomationToolProfiles.Editor, true);
            allTools = profileTools.Values.SelectMany(item => item)
                .Concat(fullPermissionEditorTools)
                .GroupBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            profileToolNames = profileTools.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
            fullPermissionToolNames = fullPermissionEditorTools.Select(tool => tool.ProtocolTool.Name)
                .Where(name => !profileToolNames[AutomationToolProfiles.Editor].Contains(name))
                .ToHashSet(StringComparer.Ordinal);
            SetProfile(initialProfile);
        }

        public string Profile
        {
            get { lock (syncRoot) return profile; }
        }

        public bool FullPermissionEnabled
        {
            get { lock (syncRoot) return fullPermissionEnabled; }
        }

        public IReadOnlyList<McpServerTool> GetTools()
        {
            lock (syncRoot)
            {
                var enabledNames = new HashSet<string>(profileToolNames[profile], StringComparer.Ordinal);
                if (fullPermissionEnabled)
                {
                    enabledNames.UnionWith(fullPermissionToolNames);
                }
                return allTools.Values.Where(tool => enabledNames.Contains(tool.ProtocolTool.Name))
                    .OrderBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal).ToList();
            }
        }

        public void SetProfile(string value)
        {
            SetConfiguration(value, false);
        }

        public void SetConfiguration(string value, bool enableFullPermission)
        {
            string normalized = AutomationToolProfiles.Normalize(value);
            if (enableFullPermission
                && !string.Equals(normalized, AutomationToolProfiles.Editor, StringComparison.Ordinal))
            {
                throw new InvalidDataException("完全权限只能在Editor模式下开启。");
            }
            lock (syncRoot)
            {
                profile = normalized;
                fullPermissionEnabled = enableFullPermission;
            }
        }

        public McpServerTool GetEnabledTool(string name)
        {
            McpServerTool? tool = GetTools().FirstOrDefault(item =>
                string.Equals(item.ProtocolTool.Name, name, StringComparison.Ordinal));
            return tool ?? throw new InvalidOperationException($"当前{Profile}模式未开放工具:{name}");
        }
    }
}
