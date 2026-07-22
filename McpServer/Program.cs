using System.Diagnostics;
using System.Drawing;
using System.Reflection;
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
                allowToolProfileChanges = options.AllowToolProfileChanges,
                toolCount = toolRegistry.GetTools().Count
            }));
            app.MapPost("/tool-profile", (ToolProfileRequest request) =>
            {
                if (!options.AllowToolProfileChanges)
                {
                    return Results.Json(
                        new { ok = false, message = "当前MCP实例使用固定工具Profile。" },
                        statusCode: StatusCodes.Status403Forbidden);
                }
                try
                {
                    toolRegistry.SetConfiguration(request.Profile, request.FullPermissionEnabled);
                    return Results.Json(new
                    {
                        ok = true,
                        toolProfile = toolRegistry.Profile,
                        fullPermissionEnabled = toolRegistry.FullPermissionEnabled,
                        allowToolProfileChanges = options.AllowToolProfileChanges,
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
            HashSet<string> profiledToolNames = editorTools
                .Concat(McpToolProfile.CreateTools("Diagnostic"))
                .Concat(McpToolProfile.CreateTools("RuntimeDiagnostic"))
                .Concat(McpToolProfile.CreateTools("Editor", true))
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
            string[] unprofiledDeclarations = typeof(AutomationMcpTools)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !profiledToolNames.Contains(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (unprofiledDeclarations.Length > 0)
            {
                throw new InvalidOperationException("MCP源码包含未归属任何Profile的工具声明："
                    + string.Join(", ", unprofiledDeclarations));
            }
            string[] names = editorTools
                .Select(tool => tool.ProtocolTool.Name)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] required =
            {
                "list_operation_types", "get_native_operation_schemas", "get_semantic_operation_schema", "get_process_design_guide", "preview_change_set",
                "get_flow_graph",
                "get_operation_guide", "apply_change_set", "discard_change_set_preview", "validate_proc",
                "wait_for_proc_state", "run_proc_test", "get_communication",
                "list_plc_devices", "get_plc_device", "set_alarm", "delete_alarm",
                "list_data_structs", "get_data_struct", "search_data_struct_items",
                "upsert_data_struct", "delete_data_struct", "get_operation_context", "get_info_log_tail",
                "list_variables", "get_variable_by_name", "get_variable_by_index",
                "set_variable_by_name", "set_variable_by_index",
                "find_variable_usages", "trace_resource",
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
            McpServerTool flowGraphTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_flow_graph", StringComparison.Ordinal));
            JsonObject? flowGraphSchema = JsonNode.Parse(flowGraphTool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            JsonObject? flowGraphProperties = flowGraphSchema?["properties"] as JsonObject;
            JsonObject? flowGraphScope = flowGraphProperties?["scope"] as JsonObject;
            if (flowGraphScope?["enum"] is not JsonArray flowScopes
                || !flowScopes.Any(value => value?.GetValue<string>() == nameof(FlowGraphScope.Project))
                || !flowScopes.Any(value => value?.GetValue<string>() == nameof(FlowGraphScope.Process))
                || flowGraphProperties?["procIndex"] == null)
            {
                throw new InvalidOperationException("get_flow_graph 未公开强类型 scope 和流程选择参数。");
            }
            McpServerTool listVariablesTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "list_variables", StringComparison.Ordinal));
            JsonObject? listVariablesProperties = (JsonNode.Parse(
                listVariablesTool.ProtocolTool.InputSchema.GetRawText()) as JsonObject)?["properties"] as JsonObject;
            JsonArray? listVariableScopes = (listVariablesProperties?["scope"] as JsonObject)?["enum"] as JsonArray;
            if (listVariableScopes == null
                || !new[]
                {
                    VariableScopeContract.Public,
                    VariableScopeContract.Process,
                    VariableScopeContract.System
                }.All(expected => listVariableScopes.Any(item => item?.GetValue<string>() == expected)))
            {
                throw new InvalidOperationException("list_variables 的作用域过滤Schema不完整。");
            }
            McpServerTool previewChangeSetTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "preview_change_set", StringComparison.Ordinal));
            JsonObject? variableChangeSchema = FindSchemaByProperties(
                JsonNode.Parse(previewChangeSetTool.ProtocolTool.InputSchema.GetRawText()),
                "name", "scope", "ownerProcess", "policy");
            JsonArray? changeSetVariableScopes =
                (variableChangeSchema?["properties"]?["scope"] as JsonObject)?["enum"] as JsonArray;
            JsonArray? variableRequired = variableChangeSchema?["required"] as JsonArray;
            JsonObject? processSelectorSchema = FindSchemaByProperties(
                JsonNode.Parse(previewChangeSetTool.ProtocolTool.InputSchema.GetRawText()),
                "procId", "name", "key");
            if (changeSetVariableScopes == null
                || !new[]
                {
                    VariableScopeContract.Public,
                    VariableScopeContract.Process,
                    VariableScopeContract.System
                }.All(expected => changeSetVariableScopes.Any(item => item?.GetValue<string>() == expected))
                || variableRequired == null
                || !new[] { "name", "scope", "type", "policy" }
                    .All(field => variableRequired.Any(item => item?.GetValue<string>() == field))
                || variableChangeSchema?["allOf"] is not JsonArray
                || processSelectorSchema?["oneOf"] is not JsonArray selectorBranches
                || selectorBranches.Count != 3)
            {
                throw new InvalidOperationException("preview_change_set 的变量作用域及owner条件Schema不完整。");
            }
            McpServerTool addVariableTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "add_variable", StringComparison.Ordinal));
            JsonObject? addVariableSchema = JsonNode.Parse(addVariableTool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            JsonObject? addVariableProperties = addVariableSchema?["properties"] as JsonObject;
            JsonObject? addVariableIndexSchema = addVariableProperties?["index"] as JsonObject;
            JsonObject? addVariableScopeSchema = addVariableProperties?["scope"] as JsonObject;
            if (addVariableIndexSchema?["minimum"]?.GetValue<int>() != 0
                || addVariableIndexSchema?["maximum"]?.GetValue<int>() != VariableIndexContract.MaximumNormalValueIndex
                || addVariableScopeSchema?["enum"] is not JsonArray addScopes
                || !addScopes.Any(value => value?.GetValue<string>() == VariableScopeContract.Public)
                || !addScopes.Any(value => value?.GetValue<string>() == VariableScopeContract.Process)
                || addVariableProperties?["ownerProcId"] == null
                || !(addVariableTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "系统变量区配置对 AI 只读", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("add_variable 未严格限制为普通变量区配置写入。");
            }
            McpServerTool updateVariableTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "update_variable", StringComparison.Ordinal));
            JsonObject? updateVariableSchema = JsonNode.Parse(updateVariableTool.ProtocolTool.InputSchema.GetRawText()) as JsonObject;
            JsonObject? updateVariableProperties = updateVariableSchema?["properties"] as JsonObject;
            if (updateVariableProperties?["value"] == null
                || updateVariableProperties?["scope"] == null
                || updateVariableProperties?["ownerProcId"] == null
                || updateVariableProperties?["index"] == null
                || updateVariableProperties.ContainsKey("initialValue")
                || updateVariableProperties.ContainsKey("applyInitialValueToRuntime")
                || updateVariableProperties.ContainsKey("configValue")
                || updateVariableProperties.ContainsKey("runtimeValue")
                || !(updateVariableTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "value修改当前值", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("update_variable 未使用单一当前值契约。");
            }
            McpServerTool deleteVariableTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "delete_variable", StringComparison.Ordinal));
            McpServerTool getVariableByNameTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_variable_by_name", StringComparison.Ordinal));
            McpServerTool getVariableByIndexTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_variable_by_index", StringComparison.Ordinal));
            McpServerTool setVariableByNameTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "set_variable_by_name", StringComparison.Ordinal));
            McpServerTool setVariableByIndexTool = editorTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "set_variable_by_index", StringComparison.Ordinal));
            JsonObject? getByNameProperties = (JsonNode.Parse(
                getVariableByNameTool.ProtocolTool.InputSchema.GetRawText()) as JsonObject)?["properties"] as JsonObject;
            JsonObject? getByIndexProperties = (JsonNode.Parse(
                getVariableByIndexTool.ProtocolTool.InputSchema.GetRawText()) as JsonObject)?["properties"] as JsonObject;
            JsonObject? setByNameProperties = (JsonNode.Parse(
                setVariableByNameTool.ProtocolTool.InputSchema.GetRawText()) as JsonObject)?["properties"] as JsonObject;
            JsonObject? setByIndexProperties = (JsonNode.Parse(
                setVariableByIndexTool.ProtocolTool.InputSchema.GetRawText()) as JsonObject)?["properties"] as JsonObject;
            if (!(updateVariableTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "系统变量区配置对 AI 只读", StringComparison.Ordinal)
                || !(deleteVariableTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "系统变量区配置对 AI 只读", StringComparison.Ordinal)
                || getByNameProperties?["name"] == null
                || getByNameProperties.ContainsKey("ownerProcId")
                || getByIndexProperties?["index"] == null
                || (getByIndexProperties["index"] as JsonObject)?["minimum"]?.GetValue<int>() != 0
                || (getByIndexProperties["index"] as JsonObject)?["maximum"]?.GetValue<int>()
                    != VariableIndexContract.MaximumValueIndex
                || getByIndexProperties.ContainsKey("ownerProcId")
                || setByNameProperties?["name"] == null
                || setByNameProperties?["value"] == null
                || setByNameProperties.ContainsKey("ownerProcId")
                || setByIndexProperties?["index"] == null
                || setByIndexProperties?["value"] == null
                || (setByIndexProperties["index"] as JsonObject)?["minimum"]?.GetValue<int>() != 0
                || (setByIndexProperties["index"] as JsonObject)?["maximum"]?.GetValue<int>()
                    != VariableIndexContract.MaximumValueIndex
                || setByIndexProperties.ContainsKey("ownerProcId")
                || !(getVariableByNameTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "私有变量也无需提供所属流程", StringComparison.Ordinal)
                || !(getVariableByIndexTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "私有变量也无需提供所属流程", StringComparison.Ordinal)
                || !(setVariableByNameTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "公共、私有和系统变量均可使用", StringComparison.Ordinal)
                || !(setVariableByIndexTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "公共、私有和系统变量均可使用", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("变量管理边界或按唯一名称/索引直接读写私有变量的契约不完整。");
            }
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
                "search_operation_fields", "find_references",
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
                "replacePreviewId", "operation.replace", "afterKey", "current_change_set",
                "branch.io", "conditions", "conditionLogic", "onFailure",
                "IO运行时逻辑目标值", "不统一表示安全位或工作位", "系统变量区配置只读"
            };
            schemaIssues.AddRange(requiredSchemaTerms
                .Where(term => !previewSchema.Contains(term, StringComparison.Ordinal)
                    && !previewSchema.Contains(
                        System.Text.Json.JsonSerializer.Serialize(term).Trim('"'),
                        StringComparison.Ordinal))
                .Select(term => "缺少 " + term));
            string[] retiredSchemaTerms =
            {
                "draftId", "expectedOperationCount", "后续阶段可继续使用"
            };
            schemaIssues.AddRange(retiredSchemaTerms
                .Where(term => previewSchema.Contains(term, StringComparison.Ordinal)
                    || previewSchema.Contains(
                        System.Text.Json.JsonSerializer.Serialize(term).Trim('"'),
                        StringComparison.Ordinal))
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
            int previewSchemaBytes = Encoding.UTF8.GetByteCount(
                previewTool.ProtocolTool.InputSchema.GetRawText());
            if (previewSchemaBytes > 64 * 1024)
            {
                throw new InvalidOperationException(
                    $"preview_change_set 参数Schema为{previewSchemaBytes}字节，超过64KB上下文预算。");
            }
            VerifyPreviewChangeSetDiscriminatedUnions(previewTool.ProtocolTool.InputSchema.GetRawText());
            VerifyDiagnosticPagingSchemas();
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
            McpServerTool processDesignTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_process_design_guide", StringComparison.Ordinal));
            McpServerTool getIoTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_io", StringComparison.Ordinal));
            McpServerTool getIoStateTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_io_state", StringComparison.Ordinal));
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
                || !(semanticSchemaTool.ProtocolTool.Description ?? string.Empty).Contains("保存必填项", StringComparison.Ordinal)
                || !(processDesignTool.ProtocolTool.Description ?? string.Empty).Contains("简单赋值", StringComparison.Ordinal)
                || !(processDesignTool.ProtocolTool.Description ?? string.Empty).Contains("不提供具体字段", StringComparison.Ordinal)
                || !(processDesignTool.ProtocolTool.Description ?? string.Empty).Contains("mechanical对应IO、气缸、真空和运动反馈", StringComparison.Ordinal)
                || !(processDesignTool.ProtocolTool.Description ?? string.Empty).Contains("review对应设计前、中、后审查", StringComparison.Ordinal)
                || !(getIoTool.ProtocolTool.Description ?? string.Empty).Contains("不自动定义机构的安全位或工作位", StringComparison.Ordinal)
                || !(getIoStateTool.ProtocolTool.Description ?? string.Empty).Contains("运行时逻辑状态", StringComparison.Ordinal)
                || !(getIoStateTool.ProtocolTool.Description ?? string.Empty).Contains("不统一表示电气高低电平、安全位或工作位", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("预演生命周期或流程验证工具职责未完整公开。");
            }
            string semanticSchema = semanticSchemaTool.ProtocolTool.InputSchema.GetRawText();
            string nativeSchema = nativeSchemaTool.ProtocolTool.InputSchema.GetRawText();
            string processDesignSchema = processDesignTool.ProtocolTool.InputSchema.GetRawText();
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
            if (!processDesignSchema.Contains("\"minItems\":1", StringComparison.Ordinal)
                || !processDesignSchema.Contains("\"uniqueItems\":true", StringComparison.Ordinal)
                || processDesignSchema.Contains("\"maxItems\"", StringComparison.Ordinal)
                || ProcessDesignGuideCatalog.SupportedTopics.Any(topic =>
                    !processDesignSchema.Contains(topic, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("流程设计指南参数未完整公开精确主题，或含无依据的数量上限。");
            }
            string processDesignGuide = ProcessDesignGuideCatalog.Get(ProcessDesignGuideCatalog.SupportedTopics);
            string[] processDesignPollution =
            {
                "1HSG", "extracted_data.json", "VariableChanges", "穴位1-BC码", "MES启用标记"
            };
            JsonObject? processDesignRoot = JsonNode.Parse(processDesignGuide) as JsonObject;
            JsonArray? processDesignSections = processDesignRoot?["sections"] as JsonArray;
            string processDesignMarkdown = processDesignSections == null
                ? string.Empty
                : string.Join("\n", processDesignSections
                    .Select(section => section?["markdown"]?.GetValue<string>() ?? string.Empty));
            if (processDesignRoot?["ok"]?.GetValue<bool>() != true
                || processDesignSections?.Count != ProcessDesignGuideCatalog.SupportedTopics.Length
                || !processDesignMarkdown.Contains("检查前置条件", StringComparison.Ordinal)
                || !processDesignMarkdown.Contains("异常路径", StringComparison.Ordinal)
                || !processDesignMarkdown.Contains("单电磁阀气缸缩回到原位", StringComparison.Ordinal)
                || !processDesignMarkdown.Contains("原位输入为 `true`，动位输入为 `false`", StringComparison.Ordinal)
                || processDesignPollution.Any(term => processDesignMarkdown.Contains(term, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("流程设计指南资源缺失核心闭环或仍含项目专用内容。");
            }
            IReadOnlyList<McpServerTool> diagnosticTools = McpToolProfile.CreateTools("Diagnostic");
            string[] diagnosticNames = diagnosticTools.Select(tool => tool.ProtocolTool.Name).ToArray();
            string[] forbiddenDiagnosticNames =
            {
                "preview_change_set", "apply_change_set", "discard_change_set_preview",
                "run_proc_test", "start_proc", "stop_proc", "pause_proc", "resume_proc",
                "set_variable_by_name", "set_variable_by_index",
                "add_variable", "update_variable", "delete_variable",
                "upsert_data_struct", "delete_data_struct", "set_alarm", "delete_alarm",
                "get_migration_configuration",
                "preview_motion_io_configuration", "preview_io_debug_configuration",
                "preview_plc_configuration", "preview_communication_configuration",
                "apply_migration_configuration", "discard_migration_configuration",
                "validate_platform_configuration"
            };
            if (!diagnosticNames.Contains("audit_proc_batch", StringComparer.Ordinal)
                || !diagnosticNames.Contains("get_native_operation_schemas", StringComparer.Ordinal)
                || !diagnosticNames.Contains("get_operation_guide", StringComparer.Ordinal)
                || !diagnosticNames.Contains("get_process_design_guide", StringComparer.Ordinal)
                || !diagnosticNames.Contains("get_flow_graph", StringComparer.Ordinal)
                || forbiddenDiagnosticNames.Any(name => diagnosticNames.Contains(name, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException("Diagnostic Profile 工具边界错误。");
            }
            McpServerTool diagnoseIssueTool = diagnosticTools.Single(tool =>
                string.Equals(tool.ProtocolTool.Name, "diagnose_issue", StringComparison.Ordinal));
            if (!(diagnoseIssueTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "运行黑匣子", StringComparison.Ordinal)
                || !(diagnoseIssueTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "evidenceLimits", StringComparison.Ordinal)
                || !(diagnoseIssueTool.ProtocolTool.Description ?? string.Empty).Contains(
                    "只读", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("diagnose_issue 缺少黑匣子证据边界或只读契约。");
            }
            string[] runtimeDiagnosticNames = McpToolProfile.CreateTools("RuntimeDiagnostic")
                .Select(tool => tool.ProtocolTool.Name).ToArray();
            string[] expectedRuntimeDiagnosticNames =
            {
                "diagnose_issue", "get_snapshot", "get_info_log_tail",
                "get_operation_context", "get_step_detail", "get_flow_graph",
                "get_operation_references", "trace_resource",
                "get_variable_by_name", "get_variable_by_index",
                "get_io", "search_io", "get_io_state",
                "get_communication", "list_plc_devices", "get_plc_device",
                "search_alarms", "get_alarm"
            };
            if (!runtimeDiagnosticNames.SequenceEqual(
                expectedRuntimeDiagnosticNames.OrderBy(name => name, StringComparer.Ordinal),
                StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "RuntimeDiagnostic Profile 必须严格等于运行现场取证工具集合。");
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
            string invalidIoWait = AiChangeSetCatalog.Validate(new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        Operation = new SemanticOperation
                        {
                            Kind = "io.wait",
                            TimeoutMs = 1000
                        }
                    }
                }
            });
            string validIoWait = AiChangeSetCatalog.Validate(new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        Operation = new SemanticOperation
                        {
                            Kind = "io.wait",
                            Conditions = new List<IoStateCondition>
                            {
                                new IoStateCondition { Io = "气缸原位", State = true },
                                new IoStateCondition { Io = "气缸动位", State = false }
                            },
                            TimeoutMs = 1000,
                            OnFailure = new OperationTarget { OperationKey = "recovery" }
                        }
                    }
                }
            });
            string validIoWrite = AiChangeSetCatalog.Validate(new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        Operation = new SemanticOperation
                        {
                            Kind = "io.write",
                            Outputs = new List<IoOutputState>
                            {
                                new IoOutputState { Io = "电磁阀A", State = true },
                                new IoOutputState { Io = "电磁阀B", State = false }
                            }
                        }
                    }
                }
            });
            if (!(invalidIoWait ?? string.Empty).Contains("conditions", StringComparison.Ordinal)
                || validIoWait != null)
            {
                throw new InvalidOperationException("io.wait联合条件与失败目标的本地校验错误。");
            }
            if (validIoWrite != null)
            {
                throw new InvalidOperationException("io.write同卡批量输出的本地校验错误。");
            }
            Console.WriteLine(
                $"Editor Profile 校验通过，共 {names.Length} 个工具；preview_change_set Schema {previewSchemaBytes}字节；V2 写入链完整，旧写入链未暴露。");
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
                if (properties["operation"] is JsonObject operationReference)
                {
                    JsonObject operationSchema = ResolveLocalSchemaReference(root, operationReference);
                    VerifySemanticOperationUnion(root, operationSchema, type);
                    operationUnionCount++;
                }
            }
            if (!actualActionTypes.SetEquals(expectedActionTypes))
                throw new InvalidOperationException("ChangeAction判别联合与SupportedTypes不一致。");
            if (operationUnionCount != 4)
                throw new InvalidOperationException($"ChangeAction内嵌SemanticOperation联合数量错误：实际{operationUnionCount}，期望4。");
        }

        private static void VerifySemanticOperationUnion(
            JsonObject root,
            JsonObject operationSchema,
            string actionType)
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
            var kindBranches = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
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
                kindBranches[kind] = branch;
            }
            if (!actualKinds.SetEquals(expectedKinds))
                throw new InvalidOperationException("SemanticOperation判别联合与SupportedKinds不一致。");

            JsonObject ioWait = kindProperties["io.wait"];
            if (!ioWait.ContainsKey("conditions") || !ioWait.ContainsKey("onFailure")
                || ioWait.ContainsKey("io") || ioWait.ContainsKey("state")
                || ioWait.ContainsKey("beforeMs") || ioWait.ContainsKey("afterMs"))
                throw new InvalidOperationException("io.wait分支必须只使用conditions/timeoutMs/onFailure表达IO等待。");
            VerifyIoConditionsSchema(root, ioWait["conditions"] as JsonObject, "io.wait.conditions");
            JsonObject ioBranch = kindProperties["branch.io"];
            if (!ioBranch.ContainsKey("conditions") || !ioBranch.ContainsKey("conditionLogic")
                || !ioBranch.ContainsKey("whenTrue") || !ioBranch.ContainsKey("whenFalse"))
                throw new InvalidOperationException("branch.io分支缺少联合条件或双分支字段。");
            VerifyIoConditionsSchema(root, ioBranch["conditions"] as JsonObject, "branch.io.conditions");
            JsonObject conditionLogic = ResolveLocalSchemaReference(
                root,
                ioBranch["conditionLogic"] as JsonObject
                    ?? throw new InvalidOperationException("branch.io.conditionLogic缺少Schema。"));
            if (conditionLogic["enum"] is not JsonArray logicValues
                || !logicValues.Any(value => string.Equals(value?.GetValue<string>(), "all", StringComparison.Ordinal))
                || !logicValues.Any(value => string.Equals(value?.GetValue<string>(), "any", StringComparison.Ordinal)))
                throw new InvalidOperationException("branch.io.conditionLogic未限制为all/any。");
            if (kindBranches["io.wait"]["required"] is not JsonArray waitRequired
                || !waitRequired.Any(value => string.Equals(value?.GetValue<string>(), "conditions", StringComparison.Ordinal))
                || !waitRequired.Any(value => string.Equals(value?.GetValue<string>(), "timeoutMs", StringComparison.Ordinal)))
                throw new InvalidOperationException("io.wait未把conditions/timeoutMs声明为必填。");
            JsonObject ioWrite = kindProperties["io.write"];
            if (!ioWrite.ContainsKey("outputs") || ioWrite.ContainsKey("io") || ioWrite.ContainsKey("state")
                || ioWrite.ContainsKey("beforeMs") || ioWrite.ContainsKey("afterMs"))
                throw new InvalidOperationException("io.write分支必须只使用outputs表达同卡批量输出。");
            VerifyIoConditionsSchema(root, ioWrite["outputs"] as JsonObject, "io.write.outputs");

            JsonObject native = kindProperties["native.operation"];
            JsonObject fieldsSchema = ResolveLocalSchemaReference(
                root,
                native["fields"] as JsonObject
                    ?? throw new InvalidOperationException("native.operation.fields缺少Schema。"));
            if (fieldsSchema["additionalProperties"] is JsonValue additional
                    && additional.TryGetValue(out bool allowsFields) && !allowsFields)
                throw new InvalidOperationException("native.operation.fields未保留动态原生字段。");
        }

        private static void VerifyIoConditionsSchema(
            JsonObject root,
            JsonObject? conditions,
            string path)
        {
            conditions = conditions == null ? null : ResolveLocalSchemaReference(root, conditions);
            if (conditions == null || conditions["minItems"]?.GetValue<int>() != 1
                || conditions["items"] is not JsonObject item
                || item["additionalProperties"]?.GetValue<bool>() != false
                || item["required"] is not JsonArray required
                || !required.Any(value => string.Equals(value?.GetValue<string>(), "io", StringComparison.Ordinal))
                || !required.Any(value => string.Equals(value?.GetValue<string>(), "state", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"{path}未声明非空、闭合且强类型的io/state条件项。");
            }
        }

        private static JsonObject ResolveLocalSchemaReference(JsonObject root, JsonObject schema)
        {
            string? reference = schema["$ref"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(reference))
            {
                return schema;
            }
            const string prefix = "#/$defs/";
            if (!reference.StartsWith(prefix, StringComparison.Ordinal)
                || root["$defs"] is not JsonObject definitions
                || definitions[reference.Substring(prefix.Length)] is not JsonObject resolved)
            {
                throw new InvalidOperationException("参数Schema包含无法解析的本地引用：" + reference);
            }
            return resolved;
        }

        private static void VerifyDiagnosticPagingSchemas()
        {
            IReadOnlyList<McpServerTool> tools = McpToolProfile.CreateTools("Editor")
                .Concat(McpToolProfile.CreateTools("Diagnostic"))
                .Concat(McpToolProfile.CreateTools("RuntimeDiagnostic"))
                .GroupBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            VerifyNumericRange(tools, "get_snapshot", "offset", 0, int.MaxValue);
            VerifyNumericRange(tools, "get_snapshot", "limit", 1, 100);
            VerifyNumericRange(tools, "get_step_detail", "opOffset", 0, int.MaxValue);
            VerifyNumericRange(tools, "get_step_detail", "opLimit", 1, 100);
            VerifyNumericRange(tools, "get_info_log_tail", "maxCount", 1, 100);
            VerifyNumericRange(tools, "diagnose_proc", "findingOffset", 0, int.MaxValue);
            VerifyNumericRange(tools, "diagnose_proc", "findingLimit", 1, 100);
            VerifyNumericRange(tools, "diagnose_issue", "evidenceOffset", 0, int.MaxValue);
            VerifyNumericRange(tools, "diagnose_issue", "evidenceLimit", 1, 100);
            VerifyNumericRange(tools, "list_variables", "offset", 0, int.MaxValue);
            VerifyNumericRange(tools, "list_variables", "limit", 1, 100);
            VerifyNumericRange(tools, "search_ops", "offset", 0, int.MaxValue);
            VerifyNumericRange(tools, "search_ops", "limit", 1, 100);
            VerifyNumericRange(tools, "list_io", "offset", 0, int.MaxValue);
            VerifyNumericRange(tools, "list_io", "limit", 1, 100);
            VerifyNumericRange(tools, "search_io", "offset", 0, int.MaxValue);
            VerifyNumericRange(tools, "search_io", "limit", 1, 100);
            VerifyNumericRange(tools, "audit_proc_batch", "procOffset", 0, int.MaxValue);
            VerifyNumericRange(tools, "audit_proc_batch", "procLimit", 1, 50);
            VerifyNumericRange(tools, "audit_proc_batch", "findingLimit", 1, 100);
        }

        private static void VerifyNumericRange(
            IReadOnlyList<McpServerTool> tools,
            string toolName,
            string propertyName,
            int expectedMinimum,
            int expectedMaximum)
        {
            McpServerTool tool = tools.Single(item =>
                string.Equals(item.ProtocolTool.Name, toolName, StringComparison.Ordinal));
            JsonObject root = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) as JsonObject
                ?? throw new InvalidOperationException($"{toolName} 参数Schema不是对象。");
            JsonObject property = root["properties"]?[propertyName] as JsonObject
                ?? throw new InvalidOperationException($"{toolName} 参数Schema缺少{propertyName}。");
            if (property["minimum"]?.GetValue<int>() != expectedMinimum
                || property["maximum"]?.GetValue<int>() != expectedMaximum)
            {
                throw new InvalidOperationException(
                    $"{toolName}.{propertyName} 分页范围未结构化为{expectedMinimum}..{expectedMaximum}。");
            }
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

        private static JsonObject? FindSchemaByProperties(JsonNode? node, params string[] propertyNames)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"] is JsonObject properties
                    && propertyNames.All(properties.ContainsKey))
                {
                    return obj;
                }
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    JsonObject? found = FindSchemaByProperties(property.Value, propertyNames);
                    if (found != null) return found;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    JsonObject? found = FindSchemaByProperties(item, propertyNames);
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
