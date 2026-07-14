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
            "search_variables", "get_variable",
            "list_stations", "get_station", "list_points", "get_point",
            "get_data_struct", "search_data_structs",
            "get_io", "search_io", "get_io_state",
            "get_communication",
            "search_alarms", "get_alarm"
        };

        private static readonly HashSet<string> DiagnosticAnalysisTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "search_ops", "diagnose_issue", "get_operation_schema",
            "search_operation_fields", "find_references", "find_variable_usages",
            "get_operation_context", "audit_proc_batch",
            "analyze_flow_graph", "get_info_log_tail", "diagnose_proc",
            "list_variables", "list_data_structs", "list_io"
        };

        private static readonly HashSet<string> EditorMutationTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "preview_change_set", "apply_change_set", "discard_change_set_preview",
            "run_proc_test",
            "start_proc", "stop_proc", "pause_proc", "resume_proc",
            "set_variable", "set_alarm", "delete_alarm"
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
            if (root == null || actionSchema == null || operationSchema == null)
                throw new InvalidOperationException("preview_change_set 生成Schema缺少动作或语义指令定义。");

            actionSchema["oneOf"] = new JsonArray
            {
                ActionShape("variable.change", "variable"),
                ActionShape("process.create", "process"),
                ActionShape("process.update", "targetProcess", "process"),
                ActionShape("process.delete", "targetProcess"),
                ActionShape("process.delete_all"),
                ActionShape("step.append", "targetProcess", "step"),
                ActionShape("step.insert", "targetProcess", "position", "step"),
                ActionShape("step.update", "targetProcess", "targetStep", "step"),
                ActionShape("step.delete", "targetProcess", "targetStep"),
                ActionShape("step.move", "targetProcess", "targetStep", "position"),
                ActionShape("operation.append", "targetProcess", "targetStep", "operation"),
                ActionShape("operation.insert", "targetProcess", "targetStep", "position", "operation"),
                ActionShape("operation.update", "targetProcess", "targetOperation", "operation"),
                ActionShape("operation.delete", "targetProcess", "targetOperation"),
                ActionShape("operation.move", "targetProcess", "targetOperation", "position")
            };
            actionSchema["x-localKeyScope"] = "current_change_set";
            operationSchema["oneOf"] = new JsonArray
            {
                SemanticShape("variable.set", "variable", "value"),
                SemanticShape("variable.add", "variable", "amount"),
                SemanticShape("variable.compute", "sourceVariable", "operator", "outputVariable"),
                SemanticShape("wait", "milliseconds"),
                SemanticShape("flow.goto"),
                SemanticShape("flow.end"),
                SemanticShape("branch.number_compare", "variable", "comparison", "compareValue"),
                SemanticShape("branch.number_range", "variable", "min", "max"),
                SemanticShape("popup.message", "message"),
                SemanticShape("popup.variable", "variable"),
                SemanticShape("config.placeholder", "message"),
                SemanticShape("io.write", "io", "state"),
                SemanticShape("io.wait", "io", "state", "timeoutMs"),
                SemanticShape("process.control"),
                SemanticShape("process.wait"),
                SemanticShape("native.operation", "operaType", "fields")
            };
            operationSchema["x-symbolicTargetScope"] = "operation_id_or_change_set_key";
            ApplyNumericRange(operationSchema, "milliseconds", 0, 86400000);
            ApplyNumericRange(operationSchema, "autoCloseMs", 1, 3600000);
            ApplyNumericRange(operationSchema, "beforeMs", 0, 3600000);
            ApplyNumericRange(operationSchema, "afterMs", 0, 3600000);
            ApplyNumericRange(operationSchema, "timeoutMs", 1, 86400000);
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

        private static JsonObject ActionShape(string type, params string[] requiredPayload)
        {
            var required = new JsonArray { "type" };
            foreach (string field in requiredPayload) required.Add(field);
            return new JsonObject
            {
                ["title"] = type,
                ["properties"] = new JsonObject
                {
                    ["type"] = new JsonObject { ["const"] = type }
                },
                ["required"] = required
            };
        }

        private static JsonObject SemanticShape(string kind, params string[] requiredPayload)
        {
            var required = new JsonArray { "kind" };
            foreach (string field in requiredPayload) required.Add(field);
            return new JsonObject
            {
                ["title"] = kind,
                ["properties"] = new JsonObject
                {
                    ["kind"] = new JsonObject { ["const"] = kind }
                },
                ["required"] = required
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
