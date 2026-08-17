using System.Reflection;
using Automation.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;
// 模块：MCP / 工具 Profile。
// 职责范围：权限外壳、任务工具过滤与输入Schema收窄；任务工具名统一来自AutomationToolProfiles。
// 排查入口：工具缺失、越权或退役工具复现时运行 --verify-profile，并核对共享名称契约与本文件Schema。

namespace Automation.McpServer
{
    internal static class McpToolProfile
    {
        // Editor/Diagnostic 共享平台知识与读取能力；RuntimeDiagnostic 使用独立的现场取证最小集合。
        private static readonly HashSet<string> KnowledgeAndReadTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_platform_development_context", "get_process_design_guide",
            "list_procs", "search_proc_catalog", "resolve_proc_target", "list_authoring_resources",
            "resolve_operation_capability",
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
            "preview_change_set", "apply_change_set", "discard_change_set_preview",
            "run_proc_test",
            "start_proc", "stop_proc", "pause_proc", "resume_proc",
            "set_variable_by_name", "set_variable_by_index",
            "add_variable", "update_variable", "delete_variable",
            "upsert_data_struct", "delete_data_struct",
            "set_alarm", "delete_alarm", "plan_motion_points"
        };

        private static readonly HashSet<string> FullPermissionTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_migration_configuration",
            "preview_motion_io_configuration", "preview_io_debug_configuration",
            "preview_plc_configuration", "preview_communication_configuration",
            "apply_migration_configuration", "discard_migration_configuration",
            "validate_platform_configuration"
        };

        public static IReadOnlyList<McpServerTool> CreateTools(string profile, bool fullPermissionEnabled = false)
        {
            profile = AutomationToolProfiles.Normalize(profile);
            var enabled = new HashSet<string>(StringComparer.Ordinal);
            if (AutomationToolProfiles.IsTaskProfile(profile))
            {
                enabled.UnionWith(AutomationToolProfiles.GetTaskToolNames(profile));
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
                    ApplyChangeActionDiscriminator(tool, profile);
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
                    ApplyToolStringEnum(tool, "detail", "compact", "full");
                }
                else if (string.Equals(toolName, "request_capability", StringComparison.Ordinal))
                {
                    ApplyTaskCapabilityDecisionSchema(tool);
                }
                else if (string.Equals(toolName, "submit_review_handoff", StringComparison.Ordinal))
                {
                    ApplyReviewHandoffSubmissionSchema(tool);
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
                else if (string.Equals(toolName, "list_authoring_resources", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "limitPerType", 1, 100);
                    ApplyAuthoringResourceListSchema(tool);
                }
                else if (string.Equals(toolName, "plan_motion_points", StringComparison.Ordinal))
                {
                    ApplyToolNumericRange(tool, "stationIndex", 0, int.MaxValue);
                    ApplyStringArraySchema(tool, "pointNames", null, 20);
                    ApplyPlainTextArrayItems(tool, "pointNames", 100, rejectWildcard: true);
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

        private static void ApplyTaskCapabilityDecisionSchema(McpServerTool tool)
        {
            JsonObject root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject
                ?? throw new InvalidOperationException("request_capability 参数Schema不是对象。");
            JsonObject decision = FindObjectSchemaWithProperties(
                    root, "version", "action", "capability", "objective", "authorizationQuote",
                    "basis", "findingIds")
                ?? throw new InvalidOperationException("request_capability 缺少decision结构。");
            root["additionalProperties"] = false;
            JsonObject properties = decision["properties"] as JsonObject
                ?? throw new InvalidOperationException("request_capability 决定Schema缺少字段定义。");
            string[] runStageFields =
            {
                "version", "action", "capability", "objective", "authorizationQuote",
                "basis", "findingIds"
            };
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
            string? description = decision["description"]?.GetValue<string>();
            decision.Clear();
            decision["type"] = "object";
            if (!string.IsNullOrWhiteSpace(description)) decision["description"] = description;
            decision["oneOf"] = new JsonArray(runStage);
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplyReviewHandoffSubmissionSchema(McpServerTool tool)
        {
            JsonObject root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject
                ?? throw new InvalidOperationException("submit_review_handoff 参数Schema不是对象。");
            root["additionalProperties"] = false;
            RemoveGeneratedInputProperty(root, "verifiedFacts");
            JsonObject handoffObject = FindObjectSchemaWithProperties(
                    root, "status", "summary", "findings")
                ?? throw new InvalidOperationException("submit_review_handoff 缺少handoff结构。");
            ApplyReviewHandoffConstraints(handoffObject);
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

        private static void ApplyReviewHandoffConstraints(JsonObject handoffObject)
        {
            if (handoffObject["properties"] is not JsonObject handoffProperties) return;
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

        private static void ApplyAuthoringResourceListSchema(McpServerTool tool)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject rootProperties
                || rootProperties["requests"] is not JsonObject requests)
            {
                throw new InvalidOperationException("list_authoring_resources 参数Schema缺少requests。");
            }
            requests["minItems"] = 1;
            requests["maxItems"] = 9;
            requests["uniqueItems"] = true;
            JsonObject? item = FindObjectSchemaWithProperties(
                requests, "type", "nameLike", "offset");
            if (item?["properties"] is not JsonObject properties)
                throw new InvalidOperationException("list_authoring_resources 缺少资源类别结构。");
            item["additionalProperties"] = false;
            item["required"] = new JsonArray("type");
            if (properties["type"] is JsonObject type)
                type["enum"] = new JsonArray(
                    "motion", "io_input", "io_output", "variable", "communication",
                    "plc", "alarm", "process", "data_struct");
            if (properties["nameLike"] is JsonObject nameLike)
            {
                nameLike["minLength"] = 1;
                nameLike["maxLength"] = 100;
            }
            if (properties["offset"] is JsonObject offset)
                offset["minimum"] = 0;
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

        private static void ApplyChangeActionDiscriminator(McpServerTool tool, string profile)
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

            ApplyPositionSchema(positionSchema);
            ApplySemanticOperationDiscriminator(root, operationSchema);
            string semanticOperationReference = GetOrCreateSchemaReference(
                root, operationSchema, "semanticOperation");
            ApplyVariableChangeSchema(root);

            bool createProfile = string.Equals(
                profile, AutomationToolProfiles.ProcessCreate, StringComparison.Ordinal);
            string[] actionTypes = createProfile
                ? ChangeSetActionTypes.SupportedTypes.Split('、')
                    .Where(value => !string.Equals(value, "process.delete", StringComparison.Ordinal)
                        && !string.Equals(value, "process.delete_all", StringComparison.Ordinal))
                    .ToArray()
                : ChangeSetActionTypes.SupportedTypes.Split('、');
            if (actionProperties["type"] is not JsonObject typeSchema)
                throw new InvalidOperationException("preview_change_set 动作Schema缺少type。");
            typeSchema["enum"] = new JsonArray(
                actionTypes.Select(value => JsonValue.Create(value)).ToArray());
            typeSchema["description"] = "原子动作类型；按x-fieldsByType提供对应载荷。";
            actionProperties["operation"] = new JsonObject
            {
                ["$ref"] = semanticOperationReference
            };
            actionSchema["required"] = new JsonArray("type");
            actionSchema["additionalProperties"] = false;
            actionSchema["x-localKeyScope"] = "current_change_set";
            actionSchema["x-localKeyContract"] = "局部key只在当前changeSet内有效；已提交对象使用apply结果中的稳定ID。";
            actionSchema["x-fieldsByType"] = createProfile
                ? new JsonObject
                {
                    ["process.create"] = "required:process",
                    ["process.update"] = "required:targetProcess,process",
                    ["step.append"] = "required:targetProcess,step",
                    ["step.insert"] = "required:targetProcess,position,step",
                    ["step.update"] = "required:targetProcess,targetStep,step",
                    ["step.delete"] = "required:targetProcess,targetStep",
                    ["step.move"] = "required:targetProcess,targetStep,position",
                    ["operation.append"] = "required:targetProcess,targetStep,operation",
                    ["operation.insert"] = "required:targetProcess,targetStep,position,operation",
                    ["operation.update"] = "required:targetProcess,targetOperation,operation;targetStep:required with targetOperation.key, optional with opId",
                    ["operation.replace"] = "required:targetProcess,targetOperation.opId,operation;optional:targetStep",
                    ["operation.delete"] = "required:targetProcess,targetOperation;targetStep:required with targetOperation.key, optional with opId",
                    ["operation.move"] = "required:targetProcess,targetOperation,position;targetStep:required with targetOperation.key, optional with opId"
                }
                : new JsonObject
                {
                    ["process.create"] = "required:process",
                    ["process.update"] = "required:targetProcess,process",
                    ["process.delete"] = "required:targetProcess",
                    ["process.delete_all"] = "required:none",
                    ["step.append"] = "required:targetProcess,step",
                    ["step.insert"] = "required:targetProcess,position,step",
                    ["step.update"] = "required:targetProcess,targetStep,step",
                    ["step.delete"] = "required:targetProcess,targetStep",
                    ["step.move"] = "required:targetProcess,targetStep,position",
                    ["operation.append"] = "required:targetProcess,targetStep,operation",
                    ["operation.insert"] = "required:targetProcess,targetStep,position,operation",
                    ["operation.update"] = "required:targetProcess,targetOperation,operation;targetStep:required with targetOperation.key, optional with opId",
                    ["operation.replace"] = "required:targetProcess,targetOperation.opId,operation;optional:targetStep",
                    ["operation.delete"] = "required:targetProcess,targetOperation;targetStep:required with targetOperation.key, optional with opId",
                    ["operation.move"] = "required:targetProcess,targetOperation,position;targetStep:required with targetOperation.key, optional with opId"
                };
            if (createProfile)
            {
                changeSetSchema["x-processCreateModes"] = new JsonObject
                {
                    ["initial"] = "省略authoringLeaseId；必须且只能创建一个流程，使用流程/步骤局部key在本阶段追加安全骨架。",
                    ["continuation"] = "传首次apply返回的authoringLeaseId；不得再次创建或删除流程，所有targetProcess和流程变量ownerProcess必须使用凭据中的稳定procId，可分阶段增删改步骤和指令。"
                };
                if (rootProperties["authoringLeaseId"] is JsonObject leaseSchema)
                {
                    leaseSchema["minLength"] = 32;
                    leaseSchema["maxLength"] = 32;
                    leaseSchema["pattern"] = "^[0-9a-f]{32}$";
                }
            }
            else
            {
                rootProperties.Remove("authoringLeaseId");
            }
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
        }

        private static void ApplySemanticOperationDiscriminator(
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

            if (operationProperties["kind"] is not JsonObject kindSchema)
                throw new InvalidOperationException("语义指令Schema缺少kind字段。");
            kindSchema["enum"] = new JsonArray(
                SemanticOperationKinds.SupportedKinds.Split('、')
                    .Select(value => JsonValue.Create(value)).ToArray());
            kindSchema["description"] = "语义指令类型；按x-fieldsByKind提供对应字段。";
            operationSchema["required"] = new JsonArray("kind");
            operationSchema["additionalProperties"] = false;
            operationSchema["x-symbolicTargetScope"] = "operation_id_or_change_set_key";
            operationSchema["x-commonOptionalFields"] = "opId,key,name";
            operationSchema["x-fieldsByKind"] = new JsonObject
            {
                ["variable.set"] = "required:variable,value",
                ["string.clear"] = "required:variable",
                ["number.zero"] = "required:variable",
                ["variable.copy"] = "required:sourceVariable,targetVariable",
                ["variable.add"] = "required:variable,amount",
                ["variable.compute"] = "required:sourceVariable,operator,outputVariable;optional:operandValue,operandVariable",
                ["wait"] = "required:milliseconds",
                ["flow.goto"] = "optional:target",
                ["flow.end"] = "required:none",
                ["branch.number_compare"] = "required:variable,comparison,compareValue;optional:whenTrue,whenFalse",
                ["branch.number_range"] = "required:variable,min,max;optional:includeBounds,whenTrue,whenFalse",
                ["branch.io"] = "required:conditions;optional:conditionLogic,whenTrue,whenFalse",
                ["alarm.raise"] = "required:message;optional:buttonText,target",
                ["popup.message"] = "required:message;optional:buttonText,autoCloseMs,target",
                ["popup.variable"] = "required:variable;optional:buttonText,autoCloseMs,target",
                ["config.placeholder"] = "required:message;optional:whenTrue,whenFalse",
                ["io.write"] = "required:outputs",
                ["io.wait"] = "required:conditions,timeoutMs;optional:onFailure",
                ["process.control"] = "optional:process,action,afterMs",
                ["process.wait"] = "optional:process,expectedState,timeoutMs,afterMs",
                ["native.operation"] = "required:operaType,fields;optional:clearFields"
            };

        }

        private static string GetOrCreateSchemaReference(
            JsonObject root,
            JsonObject schema,
            string fallbackName)
        {
            JsonObject definitions;
            if (root["$defs"] is JsonObject existingDefinitions)
            {
                definitions = existingDefinitions;
                foreach (KeyValuePair<string, JsonNode?> definition in definitions)
                {
                    if (ReferenceEquals(definition.Value, schema))
                        return "#/$defs/" + definition.Key;
                }
            }
            else
            {
                definitions = new JsonObject();
                root["$defs"] = definitions;
            }
            definitions[fallbackName] = schema.DeepClone();
            return "#/$defs/" + fallbackName;
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

        private static void RequireNestedFields(
            JsonObject branch,
            string propertyName,
            params string[] requiredFields)
        {
            JsonObject nested = FindNestedObjectSchema(branch, propertyName, requiredFields);
            nested["required"] = new JsonArray(
                requiredFields.Select(field => JsonValue.Create(field)).ToArray());
            nested["additionalProperties"] = false;
        }

        private static void RestrictSelectorToLocalKey(
            JsonObject branch,
            string propertyName,
            params string[] retiredSelectorFields)
        {
            JsonObject nested = FindNestedObjectSchema(branch, propertyName, "key");
            if (nested["properties"] is not JsonObject properties)
                throw new InvalidOperationException($"ProcessCreate Schema 缺少 {propertyName}.properties。");
            foreach (string field in retiredSelectorFields) properties.Remove(field);
            nested["required"] = new JsonArray { "key" };
            nested["additionalProperties"] = false;
        }

        private static JsonObject FindNestedObjectSchema(
            JsonObject branch,
            string propertyName,
            params string[] expectedFields)
        {
            if (branch["properties"] is not JsonObject properties
                || properties[propertyName] is not JsonNode propertySchema)
            {
                throw new InvalidOperationException($"ProcessCreate Schema 缺少字段：{propertyName}");
            }
            return FindObjectSchemaWithProperties(propertySchema, expectedFields)
                ?? throw new InvalidOperationException(
                    $"ProcessCreate Schema 无法定位 {propertyName} 对象定义。");
        }

        public static IReadOnlyList<McpServerTool> CreateEditorTools()
        {
            return CreateTools("Editor");
        }
    }

    internal sealed class DynamicMcpToolRegistry
    {
        private readonly object syncRoot = new object();
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, McpServerTool>> toolsByProfile;
        private readonly IReadOnlyDictionary<string, McpServerTool> fullPermissionOnlyTools;
        private string profile = string.Empty;
        private bool fullPermissionEnabled;

        public DynamicMcpToolRegistry(string initialProfile)
        {
            toolsByProfile = AutomationToolProfiles.All.ToDictionary(
                item => item,
                item => (IReadOnlyDictionary<string, McpServerTool>)McpToolProfile.CreateTools(item)
                    .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal),
                StringComparer.Ordinal);
            IReadOnlyList<McpServerTool> fullPermissionEditorTools =
                McpToolProfile.CreateTools(AutomationToolProfiles.Editor, true);
            fullPermissionOnlyTools = fullPermissionEditorTools
                .Where(tool => !toolsByProfile[AutomationToolProfiles.Editor]
                    .ContainsKey(tool.ProtocolTool.Name))
                .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);
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
                return GetToolsUnsafe();
            }
        }

        private IReadOnlyList<McpServerTool> GetToolsUnsafe()
        {
            IEnumerable<McpServerTool> enabledTools = toolsByProfile[profile].Values;
            if (fullPermissionEnabled)
            {
                enabledTools = enabledTools.Concat(fullPermissionOnlyTools.Values);
            }
            return enabledTools
                .OrderBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal).ToList();
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
            AutomationMcpRuntime.SetToolProfile(normalized);
        }

        public McpServerTool GetEnabledTool(string name)
        {
            return GetEnabledTool(name, out _);
        }

        public McpServerTool GetEnabledTool(string name, out string invocationProfile)
        {
            lock (syncRoot)
            {
                invocationProfile = profile;
                McpServerTool? tool = GetToolsUnsafe().FirstOrDefault(item =>
                    string.Equals(item.ProtocolTool.Name, name, StringComparison.Ordinal));
                return tool ?? throw new InvalidOperationException(
                    $"当前{invocationProfile}模式未开放工具:{name}");
            }
        }
    }
}
