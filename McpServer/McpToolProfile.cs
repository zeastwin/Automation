using System.Reflection;
using Automation.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Automation.McpServer
{
    internal static class McpToolProfile
    {
        // 知识和只读定位能力是所有模式的基础；模式只决定是否追加深度诊断或配置写入能力。
        private static readonly HashSet<string> KnowledgeAndReadTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_platform_development_context",
            "list_procs", "search_proc_catalog", "get_proc_overview", "get_proc_detail", "get_step_detail",
            "get_op_detail", "get_op_details",
            "get_operation_references", "get_proc_references", "trace_resource",
            "list_operation_types", "get_native_operation_schemas",
            "get_semantic_operation_schema", "get_operation_guide",
            "get_snapshot", "validate_proc",
            "wait_for_proc_state",
            "list_variables", "get_variable_by_name", "get_variable_by_index",
            "list_stations", "get_station", "list_points", "get_point",
            "get_data_struct", "search_data_struct_items",
            "get_io", "search_io", "get_io_state",
            "get_communication",
            "list_plc_devices", "get_plc_device",
            "search_alarms", "get_alarm"
        };

        private static readonly HashSet<string> DiagnosticAnalysisTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "search_ops", "diagnose_issue", "get_operation_schema",
            "search_operation_fields", "find_references", "find_variable_usages",
            "get_operation_context", "audit_proc_batch",
            "get_info_log_tail", "diagnose_proc",
            "list_data_structs", "list_io"
        };

        private static readonly HashSet<string> EditorMutationTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "preview_change_set", "apply_change_set", "discard_change_set_preview",
            "run_proc_test",
            "start_proc", "stop_proc", "pause_proc", "resume_proc",
            "set_variable_by_name", "set_variable_by_index",
            "add_variable", "update_variable", "delete_variable",
            "set_alarm", "delete_alarm"
        };

        private static readonly HashSet<string> EditorDiagnosticTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_operation_context", "get_info_log_tail"
        };

        public static IReadOnlyList<McpServerTool> CreateTools(string profile)
        {
            bool diagnostic;
            if (string.Equals(profile, "Diagnostic", StringComparison.OrdinalIgnoreCase)) diagnostic = true;
            else if (string.Equals(profile, "Editor", StringComparison.OrdinalIgnoreCase)) diagnostic = false;
            else throw new InvalidDataException($"MCP工具模式不支持:{profile}");
            var enabled = new HashSet<string>(KnowledgeAndReadTools, StringComparer.Ordinal);
            if (diagnostic)
            {
                enabled.UnionWith(DiagnosticAnalysisTools);
            }
            else
            {
                enabled.UnionWith(EditorDiagnosticTools);
                enabled.UnionWith(EditorMutationTools);
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
                else if (string.Equals(toolName, "get_semantic_operation_schema", StringComparison.Ordinal))
                {
                    ApplySemanticKindSchema(tool);
                }
                else if (string.Equals(toolName, "get_native_operation_schemas", StringComparison.Ordinal))
                {
                    ApplyStringArraySchema(tool, "operaTypes", null);
                }
                tools.Add(tool);
            }
            if (tools.Count == 0)
            {
                throw new InvalidOperationException($"MCP工具Profile未注册任何工具:{profile}");
            }
            return tools.OrderBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal).ToList();
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
            McpServerTool tool, string propertyName, IEnumerable<string>? allowedValues)
        {
            JsonObject? root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            if (root?["properties"] is not JsonObject properties
                || properties[propertyName] is not JsonObject arraySchema)
            {
                throw new InvalidOperationException($"{tool.ProtocolTool.Name} 参数Schema缺少字段：{propertyName}");
            }
            arraySchema["minItems"] = 1;
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

            ApplyPositionSchema(positionSchema);
            ApplyNumericRange(operationSchema, "milliseconds", 0, 86400000);
            ApplyNumericRange(operationSchema, "autoCloseMs", 1, 3600000);
            ApplyNumericRange(operationSchema, "beforeMs", 0, 3600000);
            ApplyNumericRange(operationSchema, "afterMs", 0, 3600000);
            ApplyNumericRange(operationSchema, "timeoutMs", 1, 86400000);

            operationSchema["oneOf"] = new JsonArray
            {
                SemanticShape(operationProperties, "variable.set", new[] { "variable", "value" }),
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
                SemanticShape(operationProperties, "popup.message", new[] { "message" },
                    "buttonText", "autoCloseMs", "target"),
                SemanticShape(operationProperties, "popup.variable", new[] { "variable" },
                    "buttonText", "autoCloseMs", "target"),
                SemanticShape(operationProperties, "config.placeholder", new[] { "message" }),
                SemanticShape(operationProperties, "io.write", new[] { "io", "state" }, "beforeMs", "afterMs"),
                SemanticShape(operationProperties, "io.wait", new[] { "io", "state", "timeoutMs" }),
                SemanticShape(operationProperties, "process.control", Array.Empty<string>(), "process", "action", "afterMs"),
                SemanticShape(operationProperties, "process.wait", Array.Empty<string>(), "process", "expectedState", "timeoutMs", "afterMs"),
                SemanticShape(operationProperties, "native.operation", new[] { "operaType", "fields" }, "clearFields")
            };
            operationSchema["x-symbolicTargetScope"] = "operation_id_or_change_set_key";
            operationSchema.Remove("properties");
            operationSchema.Remove("required");
            operationSchema.Remove("additionalProperties");

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
            actionSchema["x-localKeyScope"] = "current_change_set";
            actionSchema.Remove("properties");
            actionSchema.Remove("required");
            actionSchema.Remove("additionalProperties");
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(root);
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
        private readonly HashSet<string> editorToolNames;
        private readonly HashSet<string> diagnosticToolNames;
        private string profile = string.Empty;

        public DynamicMcpToolRegistry(string initialProfile)
        {
            IReadOnlyList<McpServerTool> editorTools = McpToolProfile.CreateEditorTools();
            IReadOnlyList<McpServerTool> diagnosticTools = McpToolProfile.CreateTools("Diagnostic");
            allTools = editorTools.Concat(diagnosticTools)
                .GroupBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            editorToolNames = editorTools
                .Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
            diagnosticToolNames = diagnosticTools
                .Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
            SetProfile(initialProfile);
        }

        public string Profile
        {
            get { lock (syncRoot) return profile; }
        }

        public IReadOnlyList<McpServerTool> GetTools()
        {
            lock (syncRoot)
            {
                HashSet<string> enabledNames = string.Equals(profile, "Editor", StringComparison.Ordinal)
                    ? editorToolNames
                    : diagnosticToolNames;
                return allTools.Values.Where(tool => enabledNames.Contains(tool.ProtocolTool.Name))
                    .OrderBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal).ToList();
            }
        }

        public void SetProfile(string value)
        {
            string normalized;
            if (string.Equals(value, "Diagnostic", StringComparison.Ordinal)) normalized = "Diagnostic";
            else if (string.Equals(value, "Editor", StringComparison.Ordinal)) normalized = "Editor";
            else throw new InvalidDataException($"MCP工具模式不支持:{value}");
            lock (syncRoot) profile = normalized;
        }

        public McpServerTool GetEnabledTool(string name)
        {
            McpServerTool? tool = GetTools().FirstOrDefault(item =>
                string.Equals(item.ProtocolTool.Name, name, StringComparison.Ordinal));
            return tool ?? throw new InvalidOperationException($"当前{Profile}模式未开放工具:{name}");
        }
    }
}
