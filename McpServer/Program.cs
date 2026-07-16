using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Automation.Protocol;

namespace Automation.McpServer
{
    internal static class Program
    {
        [STAThread]
        private static async Task Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (args.Any(value => string.Equals(value, "--verify-profile", StringComparison.Ordinal)))
            {
                VerifyEditorProfile();
                return;
            }

            var builder = WebApplication.CreateBuilder(args);
            AutomationMcpOptions options = AutomationMcpOptions.Load(builder.Configuration, AppContext.BaseDirectory);
            ToolCallLogger.Configure(options.LogRoot);
            AutomationMcpRuntime.Initialize(options);

            var toolRegistry = new DynamicMcpToolRegistry(options.ToolProfile);
            builder.Services.AddSingleton(toolRegistry);
            builder.Services
                .AddMcpServer(serverOptions =>
                {
                    serverOptions.Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities
                    {
                        Tools = new ModelContextProtocol.Protocol.ToolsCapability { ListChanged = false }
                    };
                    serverOptions.Handlers.ListToolsHandler = (request, cancellationToken) =>
                        ValueTask.FromResult(new ModelContextProtocol.Protocol.ListToolsResult
                        {
                            Tools = toolRegistry.GetTools().Select(tool => tool.ProtocolTool).ToList()
                        });
                    serverOptions.Handlers.CallToolHandler = async (request, cancellationToken) =>
                    {
                        string toolName = request.Params?.Name ?? string.Empty;
                        object? arguments = request.Params?.Arguments;
                        var stopwatch = Stopwatch.StartNew();
                        try
                        {
                            return await toolRegistry.GetEnabledTool(toolName)
                                .InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            stopwatch.Stop();
                            ToolCallLogger.LogInvocationFailure(
                                toolName, arguments, ex, stopwatch.ElapsedMilliseconds);
                            throw;
                        }
                    };
                })
                .WithHttpTransport(options =>
                {
                    options.Stateless = true;
                });

            var app = builder.Build();
            app.MapMcp();
            app.MapGet("/info", () => Results.Json(new
            {
                name = "Automation MCP Server",
                listenUrl = options.ListenUrl,
                listenHost = options.ListenHost,
                listenPort = options.ListenPort,
                bridgePipeName = options.BridgePipeName,
                bridgePipePath = @"\\.\pipe\" + options.BridgePipeName,
                transport = "streamable-http",
                stateless = true,
                toolProfile = toolRegistry.Profile,
                fullPermissionEnabled = toolRegistry.FullPermissionEnabled,
                toolCount = toolRegistry.GetTools().Count
            }));
            app.MapPost("/tool-profile", (ToolProfileRequest request) =>
            {
                try
                {
                    toolRegistry.SetConfiguration(request.Profile, request.FullPermissionEnabled);
                    return Results.Json(new
                    {
                        ok = true,
                        toolProfile = toolRegistry.Profile,
                        fullPermissionEnabled = toolRegistry.FullPermissionEnabled,
                        toolCount = toolRegistry.GetTools().Count
                    });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, message = ex.Message });
                }
            });
            app.MapGet("/healthz", () => Results.Json(new
            {
                ok = true,
                listenUrl = options.ListenUrl,
                listenHost = options.ListenHost,
                listenPort = options.ListenPort,
                bridgePipeName = options.BridgePipeName,
                bridgeTimeoutMs = options.BridgeTimeoutMs
            }));

            Task runTask = app.RunAsync(options.ListenUrl);
            if (!options.EnableTrayIcon)
            {
                await runTask.ConfigureAwait(false);
                return;
            }

            var exitSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var tray = new McpTrayContext(restart => exitSignal.TrySetResult(restart)))
            {
                Application.Run(tray);
            }

            if (!exitSignal.Task.IsCompleted)
            {
                exitSignal.TrySetResult(false);
            }

            bool restartRequested = await exitSignal.Task.ConfigureAwait(false);
            try
            {
                await app.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止 Automation MCP Server 失败：{ex.Message}");
            }

            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Automation MCP Server 运行异常：{ex.Message}");
            }

            if (restartRequested)
            {
                RestartCurrentProcess(args);
            }
        }

        private static void VerifyEditorProfile()
        {
            IReadOnlyList<McpServerTool> editorTools = McpToolProfile.CreateEditorTools();
            string[] names = editorTools
                .Select(tool => tool.ProtocolTool.Name)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] required =
            {
                "list_operation_types", "get_native_operation_schemas", "get_semantic_operation_schema", "preview_change_set",
                "get_operation_guide", "apply_change_set", "discard_change_set_preview", "validate_proc",
                "wait_for_proc_state", "run_proc_test", "get_communication",
                "list_plc_devices", "get_plc_device", "set_alarm", "delete_alarm",
                "list_data_structs", "get_data_struct", "search_data_struct_items",
                "upsert_data_struct", "delete_data_struct", "get_operation_context", "get_info_log_tail",
                "list_variables", "get_variable_by_name", "get_variable_by_index",
                "set_variable_by_name", "set_variable_by_index",
                "add_variable", "update_variable", "delete_variable"
            };
            string[] retired =
            {
                "preview_intent", "apply_intent", "preview_patch", "apply_patch",
                "create_proc", "create_proc_batch",
                "list_intent_templates", "get_intent_template", "build_patch_from_intent",
                "patch_contract", "get_patch_action_schema",
                "delete_procs", "reorder_proc", "copy_proc",
                "get_operation_schema",
                "add_station", "update_station", "delete_station", "set_point",
                "delete_point", "set_data_struct_field",
                "get_change_capabilities", "get_operation_contracts", "get_native_operation_contract",
                "get_operation_schemas",
                "get_semantic_operation_schemas",
                "search_data_structs",
                "get_variable", "set_variable", "search_variables",
                "begin_change_set_draft", "append_change_set_draft", "get_change_set_draft",
                "stage_changes", "get_staged_changes", "preview_staged_changes", "discard_staged_changes"
            };
            string? missing = required.FirstOrDefault(name => !names.Contains(name, StringComparer.Ordinal));
            if (missing != null) throw new InvalidOperationException($"Editor Profile 缺少工具：{missing}");
            string? exposed = retired.FirstOrDefault(name => names.Contains(name, StringComparer.Ordinal));
            if (exposed != null) throw new InvalidOperationException($"Editor Profile 意外暴露旧写入工具：{exposed}");
            string[] retiredRoutingTerms =
            {
                "preview_intent", "apply_intent", "preview_patch", "apply_patch", "create_proc", "create_proc_batch"
            };
            string[] ambiguousRoutingTerms =
            {
                "AI不得", "请告知用户", "fix_change_set_and_retry", "后续阶段可继续使用"
            };
            string[] pollutedDescriptions = editorTools
                .Where(tool => retiredRoutingTerms.Concat(ambiguousRoutingTerms).Any(term =>
                    (tool.ProtocolTool.Description ?? string.Empty).Contains(term, StringComparison.Ordinal)))
                .Select(tool => tool.ProtocolTool.Name)
                .ToArray();
            if (pollutedDescriptions.Length > 0)
            {
                throw new InvalidOperationException("Editor Profile 工具描述含旧链或歧义表达："
                    + string.Join(", ", pollutedDescriptions));
            }
            string[] pollutedSchemas = editorTools
                .Where(tool => retiredRoutingTerms.Concat(ambiguousRoutingTerms).Any(term =>
                    tool.ProtocolTool.InputSchema.ToString().Contains(term, StringComparison.Ordinal)))
                .Select(tool => tool.ProtocolTool.Name)
                .ToArray();
            if (pollutedSchemas.Length > 0)
            {
                throw new InvalidOperationException("Editor Profile 参数Schema含旧链或歧义表达："
                    + string.Join(", ", pollutedSchemas));
            }
            string[] editorOnlyDiagnosticTools =
            {
                "search_ops", "diagnose_issue", "get_operation_schema",
                "search_operation_fields", "find_references", "find_variable_usages",
                "audit_proc_batch", "diagnose_proc", "list_io"
            };
            string? exposedDiagnostic = editorOnlyDiagnosticTools.FirstOrDefault(name =>
                names.Contains(name, StringComparer.Ordinal));
            if (exposedDiagnostic != null)
                throw new InvalidOperationException($"Editor Profile 意外暴露诊断工具：{exposedDiagnostic}");
            McpServerTool previewTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "preview_change_set", StringComparison.Ordinal));
            string previewSchema = previewTool.ProtocolTool.InputSchema.ToString();
            if (previewSchema.Contains("variable.change", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("preview_change_set 不应把变量声明伪装成原子动作类型 variable.change。");
            }
            var schemaIssues = new List<string>();
            string[] requiredSchemaTerms =
            {
                "actions", "variables", "reuse/create/update/replace/require",
                "targetProcess", "targetOperation", "position", "oneOf",
                "variable.compute", "branch.number_compare", "minimum", "maximum", "kind",
                "replacePreviewId", "operation.replace", "afterKey", "current_change_set"
            };
            schemaIssues.AddRange(requiredSchemaTerms
                .Where(term => !previewSchema.Contains(term, StringComparison.Ordinal))
                .Select(term => "缺少 " + term));
            string[] retiredSchemaTerms =
            {
                "draftId", "expectedOperationCount", "后续阶段可继续使用"
            };
            schemaIssues.AddRange(retiredSchemaTerms
                .Where(term => previewSchema.Contains(term, StringComparison.Ordinal))
                .Select(term => "仍包含 " + term));
            if (!previewSchema.Contains("current_change_set", StringComparison.Ordinal)
                || !previewSchema.Contains("operation_id_or_change_set_key", StringComparison.Ordinal))
            {
                schemaIssues.Add("局部key或符号目标作用域未结构化声明");
            }
            if (schemaIssues.Count > 0)
            {
                throw new InvalidOperationException("原子动作Schema契约不完整："
                    + string.Join("；", schemaIssues));
            }
            VerifyPreviewChangeSetDiscriminatedUnions(previewTool.ProtocolTool.InputSchema.GetRawText());
            McpServerTool runTestTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "run_proc_test", StringComparison.Ordinal));
            McpServerTool startTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "start_proc", StringComparison.Ordinal));
            McpServerTool discardPreviewTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "discard_change_set_preview", StringComparison.Ordinal));
            McpServerTool nativeSchemaTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_native_operation_schemas", StringComparison.Ordinal));
            McpServerTool semanticSchemaTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_semantic_operation_schema", StringComparison.Ordinal));
            if (!(runTestTool.ProtocolTool.Description ?? string.Empty).Contains("明确要求测试或试运行", StringComparison.Ordinal)
                || !(runTestTool.ProtocolTool.Description ?? string.Empty).Contains("负责启动、观察和安全停止", StringComparison.Ordinal)
                || !(startTool.ProtocolTool.Description ?? string.Empty).Contains("由run_proc_test一次完成", StringComparison.Ordinal)
                || !(discardPreviewTool.ProtocolTool.Description ?? string.Empty).Contains("不修改配置", StringComparison.Ordinal)
                || !(previewTool.ProtocolTool.Description ?? string.Empty).Contains("preview_only", StringComparison.Ordinal)
                || !(previewTool.ProtocolTool.Description ?? string.Empty).Contains("configurationSaved", StringComparison.Ordinal)
                || !(previewTool.ProtocolTool.Description ?? string.Empty).Contains("localKeyScope", StringComparison.Ordinal)
                || !(previewTool.ProtocolTool.Description ?? string.Empty).Contains("variableResolutions", StringComparison.Ordinal)
                || !(previewTool.ProtocolTool.Description ?? string.Empty).Contains("replacePreviewId", StringComparison.Ordinal)
                || !(nativeSchemaTool.ProtocolTool.Description ?? string.Empty).Contains("native.operation", StringComparison.Ordinal)
                || !(semanticSchemaTool.ProtocolTool.Description ?? string.Empty).Contains("保存必填项", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("预演生命周期或流程验证工具职责未完整公开。");
            }
            string semanticSchema = semanticSchemaTool.ProtocolTool.InputSchema.GetRawText();
            string nativeSchema = nativeSchemaTool.ProtocolTool.InputSchema.GetRawText();
            string[] semanticKinds = SemanticOperationKinds.SupportedKinds.Split('、');
            if (semanticSchema.Contains("\"minItems\"", StringComparison.Ordinal)
                || semanticSchema.Contains("\"maxItems\"", StringComparison.Ordinal)
                || semanticKinds.Any(kind => !semanticSchema.Contains(kind, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("单语义Schema参数未完整公开支持类型，或仍暴露批量数组约束。");
            }
            if (!nativeSchema.Contains("\"minItems\":1", StringComparison.Ordinal)
                || !nativeSchema.Contains("\"uniqueItems\":true", StringComparison.Ordinal)
                || nativeSchema.Contains("\"maxItems\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("原生Schema参数仍含无依据的数量上限或缺少基础数组约束。");
            }
            string[] diagnosticNames = McpToolProfile.CreateTools("Diagnostic")
                .Select(tool => tool.ProtocolTool.Name).ToArray();
            string[] editorWriteNames =
            {
                "preview_change_set", "apply_change_set", "discard_change_set_preview"
            };
            if (!diagnosticNames.Contains("audit_proc_batch", StringComparer.Ordinal)
                || !diagnosticNames.Contains("get_native_operation_schemas", StringComparer.Ordinal)
                || !diagnosticNames.Contains("get_operation_guide", StringComparer.Ordinal)
                || editorWriteNames.Any(name => diagnosticNames.Contains(name, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException("Diagnostic Profile 工具边界错误。");
            }
            string[] fullPermissionNames = McpToolProfile.CreateTools("Editor", true)
                .Select(tool => tool.ProtocolTool.Name).ToArray();
            string[] expectedFullPermissionTools =
            {
                "get_migration_configuration",
                "preview_motion_io_configuration", "preview_io_debug_configuration",
                "preview_plc_configuration", "preview_communication_configuration",
                "apply_migration_configuration", "discard_migration_configuration",
                "validate_platform_configuration"
            };
            string? fullPermissionExposedByDefault = expectedFullPermissionTools.FirstOrDefault(name =>
                names.Contains(name, StringComparer.Ordinal));
            if (fullPermissionExposedByDefault != null)
            {
                throw new InvalidOperationException($"Editor默认模式意外暴露完全权限工具：{fullPermissionExposedByDefault}");
            }
            string? fullPermissionMissing = expectedFullPermissionTools.FirstOrDefault(name =>
                !fullPermissionNames.Contains(name, StringComparer.Ordinal));
            if (fullPermissionMissing != null)
            {
                throw new InvalidOperationException($"Editor完全权限缺少工具：{fullPermissionMissing}");
            }
            var migrationSchemaTerms = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["preview_motion_io_configuration"] = new[]
                    { "definition", "controlCards", "axes", "ioMappings", "cardIndex" },
                ["preview_io_debug_configuration"] = new[]
                    { "definition", "inputNames", "outputNames", "group1", "group3" },
                ["preview_plc_configuration"] = new[]
                    { "definition", "devices", "mappings", "direction", "variableNames" },
                ["preview_communication_configuration"] = new[]
                    { "definition", "tcp", "serial", "frameMode", "encodingName" }
            };
            foreach (KeyValuePair<string, string[]> contract in migrationSchemaTerms)
            {
                McpServerTool migrationTool = McpToolProfile.CreateTools("Editor", true).First(tool =>
                    string.Equals(tool.ProtocolTool.Name, contract.Key, StringComparison.Ordinal));
                string schema = migrationTool.ProtocolTool.InputSchema.GetRawText();
                string? missingTerm = contract.Value.FirstOrDefault(term =>
                    !schema.Contains(term, StringComparison.Ordinal));
                if (missingTerm != null)
                {
                    throw new InvalidOperationException($"完全权限工具{contract.Key}缺少强类型字段：{missingTerm}");
                }
            }
            string invalidDeletion = AiChangeSetCatalog.Validate(new AiChangeSet
            {
                Version = 2,
                DeleteProcesses = new ProcessDeleteSelection
                {
                    Mode = "byNames",
                    Names = new List<string> { "测试流程" }
                }
            });
            if (!string.Equals(invalidDeletion, "deleteProcesses.mode 只能是 all 或 selected。", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ChangeSet 删除模式本地校验未严格限制 all/selected。");
            }
            string invalidSelected = AiChangeSetCatalog.Validate(new AiChangeSet
            {
                Version = 2,
                DeleteProcesses = new ProcessDeleteSelection { Mode = "selected" }
            });
            string validAll = AiChangeSetCatalog.Validate(new AiChangeSet
            {
                Version = 2,
                DeleteProcesses = new ProcessDeleteSelection { Mode = "all" }
            });
            if (!string.Equals(invalidSelected,
                    "deleteProcesses.mode=selected 时必须提供 names 或 procIds。", StringComparison.Ordinal)
                || validAll != null)
            {
                throw new InvalidOperationException("ChangeSet 删除选择器组合校验错误。");
            }
            Console.WriteLine($"Editor Profile 校验通过，共 {names.Length} 个工具；V2 写入链完整，旧写入链未暴露。");
        }

        private static void VerifyPreviewChangeSetDiscriminatedUnions(string schemaJson)
        {
            JsonObject root = JsonNode.Parse(schemaJson) as JsonObject
                ?? throw new InvalidOperationException("preview_change_set 参数Schema不是JSON对象。");
            EnsureClosedBranch(root, "preview_change_set根参数");
            JsonObject rootProperties = root["properties"] as JsonObject
                ?? throw new InvalidOperationException("preview_change_set根参数缺少properties。");
            JsonObject changeSet = rootProperties["changeSet"] as JsonObject
                ?? throw new InvalidOperationException("preview_change_set缺少changeSet参数定义。");
            EnsureClosedBranch(changeSet, "preview_change_set.changeSet");
            if (!rootProperties.ContainsKey("replacePreviewId")
                || changeSet["properties"] is JsonObject changeSetProperties
                    && changeSetProperties.ContainsKey("replacePreviewId"))
            {
                throw new InvalidOperationException("replacePreviewId必须只存在于preview_change_set根参数。");
            }
            JsonObject actionSchema = FindSchemaByMarker(root, "x-localKeyScope", "current_change_set")
                ?? throw new InvalidOperationException("preview_change_set 未找到ChangeAction判别联合。");
            JsonArray actionBranches = actionSchema["oneOf"] as JsonArray
                ?? throw new InvalidOperationException("ChangeAction Schema缺少oneOf。");
            string[] expectedActionTypes = ChangeSetActionTypes.SupportedTypes.Split('、');
            if (actionBranches.Count != expectedActionTypes.Length)
                throw new InvalidOperationException($"ChangeAction分支数量错误：实际{actionBranches.Count}，期望{expectedActionTypes.Length}。");

            var actualActionTypes = new HashSet<string>(StringComparer.Ordinal);
            int operationUnionCount = 0;
            foreach (JsonNode? node in actionBranches)
            {
                JsonObject branch = node as JsonObject
                    ?? throw new InvalidOperationException("ChangeAction oneOf包含非对象分支。");
                EnsureClosedBranch(branch, "ChangeAction");
                JsonObject properties = branch["properties"] as JsonObject
                    ?? throw new InvalidOperationException("ChangeAction分支缺少properties。");
                string type = ReadDiscriminator(properties, "type", "ChangeAction");
                if (!actualActionTypes.Add(type))
                    throw new InvalidOperationException($"ChangeAction分支重复：{type}");
                if (properties["operation"] is JsonObject operationSchema)
                {
                    VerifySemanticOperationUnion(operationSchema, type);
                    operationUnionCount++;
                }
            }
            if (!actualActionTypes.SetEquals(expectedActionTypes))
                throw new InvalidOperationException("ChangeAction判别联合与SupportedTypes不一致。");
            if (operationUnionCount != 4)
                throw new InvalidOperationException($"ChangeAction内嵌SemanticOperation联合数量错误：实际{operationUnionCount}，期望4。");
        }

        private static void VerifySemanticOperationUnion(JsonObject operationSchema, string actionType)
        {
            if (!string.Equals(operationSchema["x-symbolicTargetScope"]?.GetValue<string>(),
                    "operation_id_or_change_set_key", StringComparison.Ordinal))
                throw new InvalidOperationException($"{actionType}.operation缺少符号目标作用域。");
            JsonArray branches = operationSchema["oneOf"] as JsonArray
                ?? throw new InvalidOperationException($"{actionType}.operation缺少oneOf。");
            string[] expectedKinds = SemanticOperationKinds.SupportedKinds.Split('、');
            if (branches.Count != expectedKinds.Length)
                throw new InvalidOperationException($"{actionType}.operation分支数量错误：实际{branches.Count}，期望{expectedKinds.Length}。");

            var actualKinds = new HashSet<string>(StringComparer.Ordinal);
            var kindProperties = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonNode? node in branches)
            {
                JsonObject branch = node as JsonObject
                    ?? throw new InvalidOperationException($"{actionType}.operation oneOf包含非对象分支。");
                EnsureClosedBranch(branch, $"{actionType}.operation");
                JsonObject properties = branch["properties"] as JsonObject
                    ?? throw new InvalidOperationException($"{actionType}.operation分支缺少properties。");
                string kind = ReadDiscriminator(properties, "kind", $"{actionType}.operation");
                if (!actualKinds.Add(kind))
                    throw new InvalidOperationException($"SemanticOperation分支重复：{kind}");
                foreach (string commonField in new[] { "opId", "key", "name" })
                {
                    if (!properties.ContainsKey(commonField))
                        throw new InvalidOperationException($"{kind}分支缺少公共字段{commonField}。");
                }
                kindProperties[kind] = properties;
            }
            if (!actualKinds.SetEquals(expectedKinds))
                throw new InvalidOperationException("SemanticOperation判别联合与SupportedKinds不一致。");

            JsonObject ioWait = kindProperties["io.wait"];
            if (ioWait.ContainsKey("beforeMs") || ioWait.ContainsKey("afterMs"))
                throw new InvalidOperationException("io.wait分支不得包含beforeMs或afterMs。");
            JsonObject ioWrite = kindProperties["io.write"];
            if (!ioWrite.ContainsKey("beforeMs") || !ioWrite.ContainsKey("afterMs"))
                throw new InvalidOperationException("io.write分支必须包含beforeMs和afterMs。");

            JsonObject native = kindProperties["native.operation"];
            if (native["fields"] is not JsonObject fieldsSchema
                || fieldsSchema["additionalProperties"] is JsonValue additional
                    && additional.TryGetValue(out bool allowsFields) && !allowsFields)
                throw new InvalidOperationException("native.operation.fields未保留动态原生字段。");
        }

        private static void EnsureClosedBranch(JsonObject branch, string unionName)
        {
            if (branch["additionalProperties"] is not JsonValue value
                || !value.TryGetValue(out bool allowed) || allowed)
                throw new InvalidOperationException($"{unionName}分支必须显式设置additionalProperties=false。");
        }

        private static string ReadDiscriminator(JsonObject properties, string fieldName, string unionName)
        {
            if (properties[fieldName] is not JsonObject field
                || field["const"] is not JsonValue value
                || !value.TryGetValue(out string? discriminator)
                || string.IsNullOrEmpty(discriminator))
                throw new InvalidOperationException($"{unionName}分支缺少{fieldName}.const判别值。");
            return discriminator;
        }

        private static JsonObject? FindSchemaByMarker(JsonNode? node, string markerName, string markerValue)
        {
            if (node is JsonObject obj)
            {
                if (string.Equals(obj[markerName]?.GetValue<string>(), markerValue, StringComparison.Ordinal))
                    return obj;
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    JsonObject? found = FindSchemaByMarker(property.Value, markerName, markerValue);
                    if (found != null) return found;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    JsonObject? found = FindSchemaByMarker(item, markerName, markerValue);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static void RestartCurrentProcess(string[] args)
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Environment.CurrentDirectory
                };
                foreach (string arg in args ?? Array.Empty<string>())
                {
                    startInfo.ArgumentList.Add(arg);
                }
                Process.Start(startInfo);
            }
            catch
            {
                // 忽略重启失败，当前实例会继续退出。
            }
        }
    }

    internal sealed class ToolProfileRequest
    {
        public string Profile { get; set; } = string.Empty;

        public bool FullPermissionEnabled { get; set; }
    }

    internal sealed class McpTrayContext : ApplicationContext
    {
        private readonly NotifyIcon notifyIcon;
        private readonly Icon trayIcon;
        private readonly ContextMenuStrip menu;
        private readonly Action<bool> exitCallback;
        private bool exitHandled;

        public McpTrayContext(Action<bool> exitCallback)
        {
            this.exitCallback = exitCallback;
            menu = BuildMenu();
            trayIcon = LoadTrayIcon();
            notifyIcon = new NotifyIcon
            {
                Icon = trayIcon,
                Text = "Automation MCP Server",
                ContextMenuStrip = menu,
                Visible = true
            };
        }

        private ContextMenuStrip BuildMenu()
        {
            var menuStrip = new ContextMenuStrip();
            var restartItem = new ToolStripMenuItem("重启 MCP");
            restartItem.Click += (_, __) => RequestExit(true);

            var exitItem = new ToolStripMenuItem("退出 MCP");
            exitItem.Click += (_, __) => RequestExit(false);

            menuStrip.Items.Add(restartItem);
            menuStrip.Items.Add(new ToolStripSeparator());
            menuStrip.Items.Add(exitItem);
            return menuStrip;
        }

        private void RequestExit(bool restart)
        {
            if (exitHandled)
            {
                return;
            }
            exitHandled = true;
            notifyIcon.Visible = false;
            exitCallback?.Invoke(restart);
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                menu.Dispose();
                trayIcon.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Icon LoadTrayIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Tray", "gear.ico");
                if (File.Exists(iconPath))
                {
                    return new Icon(iconPath);
                }
            }
            catch
            {
                // 忽略图标加载异常，回退系统图标。
            }

            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
