using System.Reflection;
using Automation.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;
// 模块：MCP / 工具 Profile。
// 职责范围：这是 Editor、Diagnostic 与 RuntimeDiagnostic 对外工具集合的权威来源。
// 排查入口：工具缺失、越权或退役工具复现时运行 --verify-profile，并核对本文件集合而非 Markdown。

namespace Automation.McpServer
{
    internal static class McpToolProfile
    {
        // Editor/Diagnostic 共享平台知识与读取能力；RuntimeDiagnostic 使用独立的现场取证最小集合。
        private static readonly HashSet<string> KnowledgeAndReadTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_platform_development_context", "get_process_design_guide",
            "list_procs", "search_proc_catalog", "get_proc_overview", "get_proc_detail", "get_flow_graph", "get_step_detail",
            "get_op_detail", "get_op_details",
            "get_operation_references", "get_proc_references", "trace_resource", "find_variable_usages",
            "list_operation_types", "get_native_operation_schemas",
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
            "set_alarm", "delete_alarm"
        };

        private static readonly HashSet<string> EditorDiagnosticTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_operation_context", "get_info_log_tail"
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
            var enabled = new HashSet<string>(StringComparer.Ordinal);
            if (string.Equals(profile, "RuntimeDiagnostic", StringComparison.OrdinalIgnoreCase))
            {
                enabled.UnionWith(RuntimeDiagnosticTools);
            }
            else if (string.Equals(profile, "Diagnostic", StringComparison.OrdinalIgnoreCase))
            {
                enabled.UnionWith(KnowledgeAndReadTools);
                enabled.UnionWith(DiagnosticAnalysisTools);
            }
            else if (string.Equals(profile, "Editor", StringComparison.OrdinalIgnoreCase))
            {
                enabled.UnionWith(KnowledgeAndReadTools);
                enabled.UnionWith(EditorDiagnosticTools);
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
                else if (string.Equals(toolName, "get_semantic_operation_schema", StringComparison.Ordinal))
                {
                    ApplySemanticKindSchema(tool);
                }
                else if (string.Equals(toolName, "get_native_operation_schemas", StringComparison.Ordinal))
                {
                    ApplyStringArraySchema(tool, "operaTypes", null);
                }
                else if (string.Equals(toolName, "get_process_design_guide", StringComparison.Ordinal))
                {
                    ApplyStringArraySchema(tool, "topics", ProcessDesignGuideCatalog.SupportedTopics);
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
                    ApplyToolNumericRange(tool, "findingLimit", 1, 100);
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

            ApplyPositionSchema(positionSchema);
            ApplyNumericRange(operationSchema, "milliseconds", 0, 86400000);
            ApplyNumericRange(operationSchema, "autoCloseMs", 1, 3600000);
            ApplyNumericRange(operationSchema, "afterMs", 0, 3600000);
            ApplyNumericRange(operationSchema, "timeoutMs", 1, 86400000);
            ApplyIoConditionsSchema(operationSchema);
            ApplyIoOutputsSchema(operationSchema);
            ApplyStringEnum(operationSchema, "conditionLogic", "all", "any");
            ApplyVariableChangeScopeSchema(root);

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
                SemanticShape(operationProperties, "branch.io", new[] { "conditions" },
                    "conditionLogic", "whenTrue", "whenFalse"),
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

        private static void ApplyVariableChangeScopeSchema(JsonObject root)
        {
            JsonObject? variableSchema = FindVariableChangeSchema(root);
            if (variableSchema?["properties"] is not JsonObject properties
                || properties["scope"] is not JsonObject scopeSchema)
            {
                throw new InvalidOperationException("preview_change_set 生成Schema缺少变量scope定义。");
            }
            scopeSchema["enum"] = new JsonArray(
                VariableScopeContract.Public,
                VariableScopeContract.Process,
                VariableScopeContract.System);
            JsonArray required = variableSchema["required"] as JsonArray ?? new JsonArray();
            foreach (string requiredField in new[] { "name", "scope", "type", "policy" })
            {
                if (!required.Any(item => string.Equals(item?.GetValue<string>(), requiredField, StringComparison.Ordinal)))
                {
                    required.Add(requiredField);
                }
            }
            variableSchema["required"] = required;
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
        private readonly HashSet<string> editorToolNames;
        private readonly HashSet<string> diagnosticToolNames;
        private readonly HashSet<string> runtimeDiagnosticToolNames;
        private readonly HashSet<string> fullPermissionToolNames;
        private string profile = string.Empty;
        private bool fullPermissionEnabled;

        public DynamicMcpToolRegistry(string initialProfile)
        {
            IReadOnlyList<McpServerTool> editorTools = McpToolProfile.CreateEditorTools();
            IReadOnlyList<McpServerTool> diagnosticTools = McpToolProfile.CreateTools("Diagnostic");
            IReadOnlyList<McpServerTool> runtimeDiagnosticTools =
                McpToolProfile.CreateTools("RuntimeDiagnostic");
            IReadOnlyList<McpServerTool> fullPermissionEditorTools = McpToolProfile.CreateTools("Editor", true);
            allTools = editorTools.Concat(diagnosticTools).Concat(runtimeDiagnosticTools)
                .Concat(fullPermissionEditorTools)
                .GroupBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            editorToolNames = editorTools
                .Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
            diagnosticToolNames = diagnosticTools
                .Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
            runtimeDiagnosticToolNames = runtimeDiagnosticTools
                .Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
            fullPermissionToolNames = fullPermissionEditorTools.Select(tool => tool.ProtocolTool.Name)
                .Where(name => !editorToolNames.Contains(name))
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
                HashSet<string> enabledNames;
                if (string.Equals(profile, "Editor", StringComparison.Ordinal))
                    enabledNames = new HashSet<string>(editorToolNames, StringComparer.Ordinal);
                else if (string.Equals(profile, "RuntimeDiagnostic", StringComparison.Ordinal))
                    enabledNames = new HashSet<string>(runtimeDiagnosticToolNames, StringComparer.Ordinal);
                else
                    enabledNames = new HashSet<string>(diagnosticToolNames, StringComparer.Ordinal);
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
            string normalized;
            if (string.Equals(value, "Diagnostic", StringComparison.Ordinal)) normalized = "Diagnostic";
            else if (string.Equals(value, "Editor", StringComparison.Ordinal)) normalized = "Editor";
            else if (string.Equals(value, "RuntimeDiagnostic", StringComparison.Ordinal)) normalized = "RuntimeDiagnostic";
            else throw new InvalidDataException($"MCP工具模式不支持:{value}");
            if (enableFullPermission && !string.Equals(normalized, "Editor", StringComparison.Ordinal))
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
