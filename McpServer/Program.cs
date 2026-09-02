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
// 模块：MCP / 独立进程入口。
// 职责范围：解析配置、校验工具 Profile、启动本机 Streamable HTTP 服务和可选托盘。
// 排查入口：进程启动失败先看命令行与监听地址；工具异常使用 --verify-profile，再检查 Bridge 连通性。

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
                try
                {
                    VerifyEditorProfile();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("MCP Profile 自检失败：" + ex);
                    Environment.ExitCode = 1;
                }
                return;
            }

            var builder = WebApplication.CreateBuilder(args);
            AutomationMcpOptions options = AutomationMcpOptions.Load(builder.Configuration, AppContext.BaseDirectory);
            ToolCallLogger.Configure(options.LogRoot);
            AutomationMcpRuntime.Initialize(options);

            // 启动期校验知识目录：条目主题未登记、区块缺失等契约问题在部署时暴露，
            // 不等到运行期让每次 get_process_design_guide 失败。
            try
            {
                string knowledgeValidation = ProcessDesignGuideCatalog.Get(
                    ProcessDesignGuideCatalog.SupportedTopics
                        .Where(topic => !string.Equals(topic, "core", StringComparison.Ordinal))
                        .ToArray(),
                    null);
                JsonObject? validationRoot = JsonNode.Parse(knowledgeValidation) as JsonObject;
                if (validationRoot?["ok"]?.GetValue<bool>() != true)
                {
                    string errorCode = validationRoot?["errorCode"]?.GetValue<string>()
                        ?? "PROCESS_KNOWLEDGE_STARTUP_VALIDATION_FAILED";
                    string message = validationRoot?["message"]?.GetValue<string>()
                        ?? "知识目录返回了无效的启动校验结果。";
                    throw new InvalidDataException(errorCode + "：" + message);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MCP 知识目录自检失败：" + ex.Message);
                Environment.ExitCode = 2;
                return;
            }

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
                            McpServerTool tool = toolRegistry.GetEnabledTool(
                                toolName, out string invocationProfile);
                            using (AutomationMcpRuntime.BeginToolInvocation(invocationProfile))
                            {
                                return await tool.InvokeAsync(request, cancellationToken)
                                    .ConfigureAwait(false);
                            }
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
                .Concat(AutomationToolProfiles.TaskProfiles.SelectMany(
                    profile => McpToolProfile.CreateTools(profile)))
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
                "list_operation_types", "resolve_operation_capability", "list_authoring_resources", "get_native_operation_schemas", "get_native_operation_field_contract", "get_semantic_operation_schema", "get_process_design_guide", "preview_change_set",
                "get_flow_graph",
                "get_operation_guide", "apply_change_set", "discard_change_set_preview", "validate_proc",
                "wait_for_proc_state", "run_proc_test", "get_communication",
                "list_plc_devices", "get_plc_device", "set_alarm", "delete_alarm",
                "list_data_structs", "get_data_struct", "search_data_struct_items",
                "upsert_data_struct", "delete_data_struct", "get_operation_context", "get_info_log_tail",
                "list_variables", "get_variable_by_name", "get_variable_by_index",
                "set_variable_by_name", "set_variable_by_index",
                "find_variable_usages", "trace_resource",
                "add_variable", "update_variable", "delete_variable", "plan_motion_points"
            };
            string[] retired =
            {
                "preview_intent", "apply_intent", "preview_patch", "apply_patch",
                "create_proc", "create_proc_batch",
                "list_intent_templates", "get_intent_template", "build_patch_from_intent",
                "patch_contract", "get_patch_action_schema",
                "delete_procs", "reorder_proc", "copy_proc",
                "add_station", "update_station", "delete_station", "set_point",
                "delete_point", "set_data_struct_field",
                "get_change_capabilities", "get_operation_contracts", "get_native_operation_contract",
                "get_operation_schemas",
                "get_semantic_operation_schemas",
                "search_data_structs",
                "get_variable", "set_variable", "search_variables",
                "begin_change_set_draft", "append_change_set_draft", "get_change_set_draft",
                "stage_changes", "get_staged_changes", "preview_staged_changes", "discard_staged_changes",
                "preview_process_blueprint", "discover_project_resources", "resolve_authoring_inputs"
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
            JsonObject? variableChangeProperties = variableChangeSchema?["properties"] as JsonObject;
            JsonObject? variableNameSchema = variableChangeProperties?["name"] as JsonObject;
            JsonArray? changeSetVariableScopes =
                (variableChangeProperties?["scope"] as JsonObject)?["enum"] as JsonArray;
            JsonArray? variableTypes =
                (variableChangeProperties?["type"] as JsonObject)?["enum"] as JsonArray;
            JsonArray? variablePolicies =
                (variableChangeProperties?["policy"] as JsonObject)?["enum"] as JsonArray;
            JsonArray? variableRequired = variableChangeSchema?["required"] as JsonArray;
            var requiredVariableFields = variableRequired?
                .Select(item => item?.GetValue<string>())
                .Where(field => field != null)
                .ToHashSet(StringComparer.Ordinal);
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
                || variableNameSchema?["minLength"]?.GetValue<int>() != 1
                || variableNameSchema?["pattern"]?.GetValue<string>() != "\\S"
                || variableTypes == null
                || !new[] { VariableChangeContract.DoubleType, VariableChangeContract.StringType }
                    .All(expected => variableTypes.Any(item => item?.GetValue<string>() == expected))
                || variablePolicies == null
                || !new[]
                {
                    VariableChangeContract.ReusePolicy,
                    VariableChangeContract.CreatePolicy,
                    VariableChangeContract.UpdatePolicy,
                    VariableChangeContract.ReplacePolicy,
                    VariableChangeContract.RequirePolicy
                }.All(expected => variablePolicies.Any(item => item?.GetValue<string>() == expected))
                || requiredVariableFields == null
                || !requiredVariableFields.SetEquals(new[] { "name", "scope" })
                || variableChangeSchema?["additionalProperties"]?.GetValue<bool>() != false
                || variableChangeSchema?["allOf"] is not JsonArray
                || processSelectorSchema?["oneOf"] is not JsonArray selectorBranches
                || selectorBranches.Count != 3)
            {
                throw new InvalidOperationException(
                    "preview_change_set 的变量必填项、枚举、名称或owner条件Schema不完整。");
            }
            string defaultVariableError = VariableChangeContract.Validate(new[]
            {
                new VariableChange
                {
                    Name = "测试变量",
                    Scope = VariableScopeContract.Public
                }
            });
            string blankVariableNameError = VariableChangeContract.Validate(new[]
            {
                new VariableChange
                {
                    Name = "   ",
                    Scope = VariableScopeContract.Public
                }
            });
            if (defaultVariableError != null || blankVariableNameError == null)
            {
                throw new InvalidOperationException(
                    "ChangeSet 变量默认值或非空名称校验与公开Schema不一致。");
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
                || addVariableProperties?["ownerProcId"] == null)
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
                || updateVariableProperties.ContainsKey("runtimeValue"))
            {
                throw new InvalidOperationException("update_variable 未使用单一当前值契约。");
            }
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
            if (getByNameProperties?["name"] == null
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
                || setByIndexProperties.ContainsKey("ownerProcId"))
            {
                throw new InvalidOperationException("变量管理边界或按唯一名称/索引直接读写私有变量的契约不完整。");
            }
            string[] retiredRoutingTerms =
            {
                "preview_intent", "apply_intent", "preview_patch", "apply_patch", "create_proc", "create_proc_batch"
            };
            string[] pollutedSchemas = editorTools
                .Where(tool => retiredRoutingTerms.Any(term =>
                    tool.ProtocolTool.InputSchema.ToString().Contains(term, StringComparison.Ordinal)))
                .Select(tool => tool.ProtocolTool.Name)
                .ToArray();
            if (pollutedSchemas.Length > 0)
            {
                throw new InvalidOperationException("Editor Profile 参数Schema含旧链或歧义表达："
                    + string.Join(", ", pollutedSchemas));
            }
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
                "targetProcess", "targetOperation", "position", "x-fieldsByType", "x-fieldsByKind",
                "variable.compute", "branch.number_compare", "minimum", "maximum", "kind",
                "replacePreviewId", "operation.replace", "afterKey", "current_change_set",
                "branch.io", "conditions", "conditionLogic", "onFailure"
            };
            schemaIssues.AddRange(requiredSchemaTerms
                .Where(term => !previewSchema.Contains(term, StringComparison.Ordinal)
                    && !previewSchema.Contains(
                        System.Text.Json.JsonSerializer.Serialize(term).Trim('"'),
                        StringComparison.Ordinal))
                .Select(term => "缺少 " + term));
            string[] retiredSchemaTerms =
            {
                "draftId", "expectedOperationCount"
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
            VerifyPreviewChangeSetCompactContract(previewTool.ProtocolTool.InputSchema.GetRawText());
            VerifyCompactChangeSetApplyResult();
            VerifyCompactChangeSetPreviewResult();
            VerifyDiagnosticPagingSchemas();
            McpServerTool nativeSchemaTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_native_operation_schemas", StringComparison.Ordinal));
            McpServerTool semanticSchemaTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_semantic_operation_schema", StringComparison.Ordinal));
            McpServerTool processDesignTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "get_process_design_guide", StringComparison.Ordinal));
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
                || !processDesignSchema.Contains("compact", StringComparison.Ordinal)
                || !processDesignSchema.Contains("full", StringComparison.Ordinal)
                || ProcessDesignGuideCatalog.SupportedTopics.Any(topic =>
                    !processDesignSchema.Contains(topic, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("流程设计指南参数未完整公开精确主题，或含无依据的数量上限。");
            }
            string processDesignGuide = ProcessDesignGuideCatalog.Get(
                ProcessDesignGuideCatalog.SupportedTopics, "full");
            string[] processDesignPollution =
            {
                "extracted_data.json", "VariableChanges", "穴位1-BC码", "MES启用标记"
            };
            string[] processDesignRequiredTerms =
            {
                "命令不等于完成", "功能块不是持久化对象", "1HSG下料", "取放块", "扫码",
                "最多总尝试次数为 `1 + N`", "attemptCount < maxAttempts"
            };
            JsonObject? processDesignRoot = JsonNode.Parse(processDesignGuide) as JsonObject;
            JsonArray? processDesignSections = processDesignRoot?["sections"] as JsonArray;
            JsonArray? processKnowledgeBlocks = processDesignRoot?["knowledgeBlocks"] as JsonArray;
            JsonArray? functionalBlocks = processDesignRoot?["goalCoverage"]?["functionalBlocks"] as JsonArray;
            JsonArray? resourceRequests = processDesignRoot?["goalCoverage"]?["resourceRequests"] as JsonArray;
            JsonObject? evidenceGapPolicy = processDesignRoot?["goalCoverage"]?["evidenceGapPolicy"] as JsonObject;
            string processDesignMarkdown = processDesignSections == null
                ? string.Empty
                : string.Join("\n", processDesignSections
                    .Select(section => section?["markdown"]?.GetValue<string>() ?? string.Empty));
            string processKnowledgeMarkdown = processKnowledgeBlocks == null
                ? string.Empty
                : string.Join("\n", processKnowledgeBlocks
                    .Select(block => block?["markdown"]?.GetValue<string>() ?? string.Empty));
            string[] requiredKnowledgeIds =
            {
                "identify.read-and-bind-carrier",
                "transfer.station-handoff",
                "dispensing.service-material-path",
                "dispensing.calibrate-needle-offset",
                "transaction.submit-trace-record"
            };
            string[] forbiddenKnowledgeStates =
            {
                "candidate", "needs-review", "deprecated"
            };
            if (processDesignRoot?["ok"]?.GetValue<bool>() != true
                || processDesignRoot?["includedCore"]?.GetValue<bool>() != true
                || processDesignSections?.Count != ProcessDesignGuideCatalog.SupportedTopics.Length
                // 知识库随审核增长：只要求必需规范全部在场（下方逐项检查），
                // 不再断言块数恰等于当前清单，避免新增规范必须改自检。
                || processKnowledgeBlocks == null
                || processKnowledgeBlocks.Count < requiredKnowledgeIds.Length
                || functionalBlocks?.Count != 4
                || resourceRequests == null
                || !resourceRequests.Any(request => string.Equals(
                    request?["type"]?.GetValue<string>(), "motion", StringComparison.Ordinal))
                || !(processDesignRoot?["goalCoverage"]?["proofBoundary"]?.GetValue<string>()
                    ?? string.Empty).Contains("不证明用户业务目标", StringComparison.Ordinal)
                || !(evidenceGapPolicy?["missingFact"]?.GetValue<string>()
                    ?? string.Empty).Contains("不能据此判定该功能不需要", StringComparison.Ordinal)
                || !(evidenceGapPolicy?["alternativeMechanism"]?.GetValue<string>()
                    ?? string.Empty).Contains("config.placeholder", StringComparison.Ordinal)
                || !(evidenceGapPolicy?["completionClaim"]?.GetValue<string>()
                    ?? string.Empty).Contains("不得用可编译", StringComparison.Ordinal)
                || !functionalBlocks.Any(block => string.Equals(
                    block?["topic"]?.GetValue<string>(), "pick-place", StringComparison.Ordinal)
                    && (block?["slots"] as JsonArray)?.Count == 5)
                || requiredKnowledgeIds.Any(patternId => !processKnowledgeBlocks.Any(block =>
                    string.Equals(
                        block?["patternId"]?.GetValue<string>(),
                        patternId,
                        StringComparison.Ordinal)))
                || forbiddenKnowledgeStates.Any(state =>
                    processKnowledgeMarkdown.Contains(state, StringComparison.OrdinalIgnoreCase))
                || processDesignRequiredTerms.Any(term =>
                    !processDesignMarkdown.Contains(term, StringComparison.Ordinal))
                || processDesignPollution.Any(term => processDesignMarkdown.Contains(term, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("流程设计知识缺失核心不变量、功能块、甄别来源或仍含项目参数。");
            }
            string identifyCompactGuide = ProcessDesignGuideCatalog.Get(new[] { "identify" });
            string identifyFullGuide = ProcessDesignGuideCatalog.Get(new[] { "identify" }, "full");
            JsonObject? identifyOnlyRoot = JsonNode.Parse(identifyCompactGuide) as JsonObject;
            JsonArray? identifyOnlySections = identifyOnlyRoot?["sections"] as JsonArray;
            JsonArray? identifyKnowledgeBlocks = identifyOnlyRoot?["knowledgeBlocks"] as JsonArray;
            if (identifyOnlySections?.Count != 2
                || identifyOnlyRoot?["detail"]?.GetValue<string>() != "compact"
                || identifyOnlySections[0]?["topic"]?.GetValue<string>() != "core"
                || identifyOnlySections[1]?["topic"]?.GetValue<string>() != "identify"
                || identifyOnlySections[1]?["format"]?.GetValue<string>() != "compact"
                || !(identifyOnlySections[0]?["markdown"]?.GetValue<string>() ?? string.Empty)
                    .Contains("最短可靠路径", StringComparison.Ordinal)
                || identifyKnowledgeBlocks == null
                || !identifyKnowledgeBlocks.Any(block => string.Equals(
                    block?["patternId"]?.GetValue<string>(),
                    "identify.read-and-bind-carrier",
                    StringComparison.Ordinal))
                || identifyKnowledgeBlocks.Any(block => block?["markdown"] != null)
                || !identifyKnowledgeBlocks.Any(block =>
                    !string.IsNullOrWhiteSpace(block?["recommendedStages"]?.GetValue<string>()))
                || !identifyKnowledgeBlocks.Any(block =>
                    !string.IsNullOrWhiteSpace(block?["antiPatterns"]?.GetValue<string>()))
                || Encoding.UTF8.GetByteCount(identifyCompactGuide)
                    >= Encoding.UTF8.GetByteCount(identifyFullGuide))
            {
                throw new InvalidOperationException("流程设计知识未默认返回紧凑core、可执行阶段、反模式或可用规范。");
            }
            foreach (string topic in ProcessDesignGuideCatalog.SupportedTopics.Where(topic =>
                !string.Equals(topic, "core", StringComparison.Ordinal)))
            {
                string compactGuide = ProcessDesignGuideCatalog.Get(new[] { topic });
                string fullGuide = ProcessDesignGuideCatalog.Get(new[] { topic }, "full");
                if ((JsonNode.Parse(compactGuide) as JsonObject)?["ok"]?.GetValue<bool>() != true
                    || (JsonNode.Parse(fullGuide) as JsonObject)?["ok"]?.GetValue<bool>() != true
                    || Encoding.UTF8.GetByteCount(compactGuide) >= Encoding.UTF8.GetByteCount(fullGuide))
                {
                    throw new InvalidOperationException(
                        "流程设计主题未保持 compact 小于 full 的投影边界：" + topic + "。");
                }
            }
            // 设备框架简索引与 patternIds 钻取：compact 索引只带设备画像（框架会持续增多），
            // 钻取才返回功能单元表；未知 patternId 必须结构化拒绝而不是静默空结果。
            string compositionCompactGuide = ProcessDesignGuideCatalog.Get(new[] { "composition" });
            JsonObject? compositionRoot = JsonNode.Parse(compositionCompactGuide) as JsonObject;
            JsonArray? compositionBlocks = compositionRoot?["knowledgeBlocks"] as JsonArray;
            JsonObject? dispensingIndex = compositionBlocks?
                .FirstOrDefault(block => string.Equals(
                    block?["patternId"]?.GetValue<string>(),
                    "device-frame.dispensing-station",
                    StringComparison.Ordinal)) as JsonObject;
            if (compositionBlocks == null
                || dispensingIndex == null
                || string.IsNullOrWhiteSpace(dispensingIndex["deviceProfile"]?.GetValue<string>())
                || dispensingIndex.ContainsKey("functionalUnits")
                || dispensingIndex.ContainsKey("antiPatterns")
                || dispensingIndex.ContainsKey("goldenExample")
                || dispensingIndex.ContainsKey("markdown"))
            {
                throw new InvalidOperationException("composition 索引必须是设备框架简索引（设备画像），不带功能单元表。");
            }
            string frameDrillGuide = ProcessDesignGuideCatalog.Get(
                new[] { "composition" },
                null,
                new[] { "device-frame.reinspect" });
            JsonObject? frameDrillRoot = JsonNode.Parse(frameDrillGuide) as JsonObject;
            JsonArray? frameDrillBlocks = frameDrillRoot?["knowledgeBlocks"] as JsonArray;
            if (frameDrillRoot?["ok"]?.GetValue<bool>() != true
                || frameDrillBlocks?.Count != 1
                || frameDrillBlocks[0]?["patternId"]?.GetValue<string>() != "device-frame.reinspect"
                || string.IsNullOrWhiteSpace(frameDrillBlocks[0]?["functionalUnits"]?.GetValue<string>())
                || string.IsNullOrWhiteSpace(frameDrillBlocks[0]?["buildOrder"]?.GetValue<string>())
                || string.IsNullOrWhiteSpace(frameDrillBlocks[0]?["antiPatterns"]?.GetValue<string>())
                || string.IsNullOrWhiteSpace(frameDrillBlocks[0]?["goldenExample"]?.GetValue<string>()))
            {
                throw new InvalidOperationException("patternIds 钻取未返回目标设备框架的功能单元表、搭建顺序、反模式与黄金样例。");
            }
            string dataDesignGuide = ProcessDesignGuideCatalog.Get(
                new[] { "orchestration" },
                null,
                new[] { "variables.design", "data-struct.design" });
            JsonArray? dataDesignBlocks = (JsonNode.Parse(dataDesignGuide) as JsonObject)?["knowledgeBlocks"]
                as JsonArray;
            if (dataDesignBlocks?.Count != 2
                || !dataDesignBlocks.Any(block =>
                    !string.IsNullOrWhiteSpace(block?["quantitativeConventions"]?.GetValue<string>()))
                || !dataDesignBlocks.Any(block =>
                    !string.IsNullOrWhiteSpace(block?["capacityAndPersistenceConventions"]?.GetValue<string>())))
            {
                throw new InvalidOperationException("compact 知识投影缺少定量参考惯例或容量与周期惯例。");
            }
            string unknownPatternGuide = ProcessDesignGuideCatalog.Get(
                new[] { "composition" },
                null,
                new[] { "device-frame.not-exist" });
            JsonObject? unknownPatternRoot = JsonNode.Parse(unknownPatternGuide) as JsonObject;
            if (unknownPatternRoot?["ok"]?.GetValue<bool>() != false
                || unknownPatternRoot?["errorCode"]?.GetValue<string>() != "PROCESS_KNOWLEDGE_PATTERN_INVALID")
            {
                throw new InvalidOperationException("未知 patternId 未被结构化拒绝。");
            }
            IReadOnlyList<McpServerTool> diagnosticTools = McpToolProfile.CreateTools("Diagnostic");
            string[] diagnosticNames = diagnosticTools.Select(tool => tool.ProtocolTool.Name).ToArray();
            string[] forbiddenDiagnosticNames =
            {
                "preview_change_set", "preview_process_blueprint", "apply_change_set", "discard_change_set_preview",
                "run_proc_test", "start_proc", "stop_proc", "pause_proc", "resume_proc",
                "set_variable_by_name", "set_variable_by_index",
                "add_variable", "update_variable", "delete_variable",
                "upsert_data_struct", "delete_data_struct", "set_alarm", "delete_alarm",
                "plan_motion_points",
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
            string[] editorMissingDiagnosticTools = diagnosticNames
                .Where(name => !names.Contains(name, StringComparer.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (editorMissingDiagnosticTools.Length > 0)
            {
                throw new InvalidOperationException(
                    "Editor Profile 必须完整包含 Diagnostic 能力："
                    + string.Join(", ", editorMissingDiagnosticTools));
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
                    { "definition", "tcp", "serial", "localAddress", "localPort", "remoteAddress", "remotePort", "autoReconnect", "encodingName" }
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
            string previewChangeSetSchema = previewChangeSetTool.ProtocolTool.InputSchema.GetRawText();
            if (previewChangeSetSchema.Contains("deleteProcesses", StringComparison.Ordinal)
                || previewChangeSetSchema.Contains("\"processes\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "preview_change_set Schema 意外暴露旧流程写入字段，流程结构只能通过 actions 表达。");
            }
            VerifyTaskCapabilityProfiles();
            Console.WriteLine(
                $"Editor Profile 校验通过，共 {names.Length} 个工具；preview_change_set Schema {previewSchemaBytes}字节；V2 写入链完整，旧写入链未暴露。");
        }

        private static void VerifyTaskCapabilityProfiles()
        {
            VerifyOperationCapabilityResolver();
            string[] coordinator = ToolNames(AutomationToolProfiles.TaskCoordinator);
            if (!coordinator.SequenceEqual(new[] { "get_device_summary", "request_capability" }, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    "TaskCoordinator 必须只开放设备自我画像（get_device_summary）和结构化单步决定提交工具。");
            string coordinatorSchema = McpToolProfile.CreateTools(AutomationToolProfiles.TaskCoordinator)
                .Single(tool => string.Equals(
                    tool.ProtocolTool.Name, "request_capability", StringComparison.Ordinal))
                .ProtocolTool.InputSchema.GetRawText();
            int coordinatorSchemaBytes = Encoding.UTF8.GetByteCount(coordinatorSchema);
            JsonNode? coordinatorSchemaNode = JsonNode.Parse(coordinatorSchema);
            JsonObject? runStageBranch = FindDecisionBranch(coordinatorSchemaNode, "run_stage");
            JsonObject? finishBranch = FindDecisionBranch(coordinatorSchemaNode, "finish");
            JsonObject? askUserBranch = FindDecisionBranch(coordinatorSchemaNode, "ask_user");
            string[] missingCoordinatorTerms = AutomationToolProfiles.ExecutionProfiles
                .Where(profile => !coordinatorSchema.Contains(profile, StringComparison.Ordinal))
                .ToArray();
            if (missingCoordinatorTerms.Length > 0
                || !coordinatorSchema.Contains("run_stage", StringComparison.Ordinal)
                || !coordinatorSchema.Contains("\"oneOf\"", StringComparison.Ordinal)
                || !coordinatorSchema.Contains("\"additionalProperties\":false", StringComparison.Ordinal)
                || runStageBranch == null
                || finishBranch != null
                || askUserBranch != null
                || runStageBranch?["properties"]?["message"] != null
                || !coordinatorSchema.Contains("findingIds", StringComparison.Ordinal)
                || runStageBranch?["properties"]?["reviewHandoff"] != null
                || coordinatorSchema.Contains("evidenceFactRefs", StringComparison.Ordinal)
                || coordinatorSchema.Contains("requiresUserConfirmationAfter", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "TaskCoordinator 稳定控制Schema未闭合、缺少能力枚举或仍包含完成/评审包装："
                    + string.Join(",", missingCoordinatorTerms));

            string[] design = ToolNames(AutomationToolProfiles.ProcessDesign);
            if (!design.SequenceEqual(
                    new[] { "get_process_design_guide", "request_capability" },
                    StringComparer.Ordinal))
                throw new InvalidOperationException("ProcessDesign 必须只开放按需设计知识入口和能力申请工具。");

            string[] source = ToolNames(AutomationToolProfiles.SourceDevelopment);
            if (!source.SequenceEqual(
                    new[] { "get_platform_development_context", "request_capability", "search_platform_source" },
                    StringComparer.Ordinal))
                throw new InvalidOperationException("SourceDevelopment 必须只附加平台开发上下文、受限源码检索和能力申请工具。");
            string[] sourceReview = ToolNames(AutomationToolProfiles.SourceReview);
            if (!sourceReview.SequenceEqual(source, StringComparer.Ordinal))
                throw new InvalidOperationException("SourceReview 与 SourceDevelopment 必须共享平台上下文入口并由Developer写权限区分。");
            McpServerTool sourceSearchTool = McpToolProfile.CreateTools(AutomationToolProfiles.SourceReview)
                .Single(tool => string.Equals(
                    tool.ProtocolTool.Name, "search_platform_source", StringComparison.Ordinal));
            string sourceSearchSchema = sourceSearchTool.ProtocolTool.InputSchema.GetRawText();
            if (!sourceSearchSchema.Contains("\"maximum\":100", StringComparison.Ordinal)
                || !sourceSearchSchema.Contains("\".cs\"", StringComparison.Ordinal)
                || sourceSearchSchema.Contains("regex", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("受限源码检索的结果上限或扩展名白名单Schema不完整。");
            }

            string[] create = ToolNames(AutomationToolProfiles.ProcessCreate);
            if (!create.Contains("preview_change_set", StringComparer.Ordinal)
                || !create.Contains("apply_change_set", StringComparer.Ordinal)
                || !create.Contains("inspect_process", StringComparer.Ordinal)
                || create.Contains("resolve_proc_target", StringComparer.Ordinal)
                || !create.Contains("list_authoring_resources", StringComparer.Ordinal)
                || !create.Contains("resolve_operation_capability", StringComparer.Ordinal)
                || create.Contains("resolve_authoring_inputs", StringComparer.Ordinal)
                || create.Contains("get_operation_schema", StringComparer.Ordinal)
                || create.Contains("search_proc_catalog", StringComparer.Ordinal)
                || create.Contains("search_io", StringComparer.Ordinal)
                || create.Contains("search_alarms", StringComparer.Ordinal)
                || create.Contains("start_proc", StringComparer.Ordinal)
                || create.Contains("get_platform_development_context", StringComparer.Ordinal))
                throw new InvalidOperationException("ProcessCreate 工具边界错误。");

            string[] edit = ToolNames(AutomationToolProfiles.ProcessEdit);
            if (!edit.Contains("preview_change_set", StringComparer.Ordinal)
                || !edit.Contains("resolve_proc_target", StringComparer.Ordinal)
                || !edit.Contains("get_process_design_guide", StringComparer.Ordinal)
                || !edit.Contains("list_authoring_resources", StringComparer.Ordinal)
                || !edit.Contains("resolve_operation_capability", StringComparer.Ordinal)
                || !edit.Contains("inspect_process", StringComparer.Ordinal)
                || !edit.Contains("get_op_details", StringComparer.Ordinal)
                || edit.Contains("resolve_authoring_inputs", StringComparer.Ordinal)
                || edit.Contains("get_proc_detail", StringComparer.Ordinal)
                || edit.Contains("get_flow_graph", StringComparer.Ordinal)
                || edit.Contains("get_step_detail", StringComparer.Ordinal)
                || !edit.Contains("get_native_operation_field_contract", StringComparer.Ordinal)
                || edit.Contains("get_operation_schema", StringComparer.Ordinal)
                || edit.Contains("search_proc_catalog", StringComparer.Ordinal)
                || edit.Contains("start_proc", StringComparer.Ordinal)
                || edit.Length > 18)
                throw new InvalidOperationException("ProcessEdit 工具边界错误。");

            string[] review = ToolNames(AutomationToolProfiles.ProcessReview);
            if (!review.Contains("resolve_proc_target", StringComparer.Ordinal)
                || review.Contains("list_authoring_resources", StringComparer.Ordinal)
                || !review.Contains("inspect_process", StringComparer.Ordinal)
                || !review.Contains("get_op_details", StringComparer.Ordinal)
                || !review.Contains("submit_review_handoff", StringComparer.Ordinal)
                || review.Contains("validate_proc", StringComparer.Ordinal)
                || review.Contains("get_proc_overview", StringComparer.Ordinal)
                || review.Contains("get_flow_graph", StringComparer.Ordinal)
                || review.Contains("get_proc_references", StringComparer.Ordinal)
                || review.Contains("search_proc_catalog", StringComparer.Ordinal)
                || review.Contains("search_io", StringComparer.Ordinal)
                || review.Contains("search_alarms", StringComparer.Ordinal)
                || review.Contains("diagnose_proc", StringComparer.Ordinal)
                || review.Contains("diagnose_issue", StringComparer.Ordinal)
                || review.Contains("get_snapshot", StringComparer.Ordinal)
                || review.Contains("get_proc_detail", StringComparer.Ordinal)
                || review.Contains("get_op_detail", StringComparer.Ordinal)
                || review.Length > 22)
                throw new InvalidOperationException(
                    "ProcessReview 必须优先使用单次聚合检查，并移除重复的基础读取入口；"
                    + "资源域只读查询（工站/轴/点位/IO/通讯）不算重复入口。");
            string reviewDecisionSchema = McpToolProfile.CreateTools(AutomationToolProfiles.ProcessReview)
                .Single(tool => string.Equals(
                    tool.ProtocolTool.Name, "request_capability", StringComparison.Ordinal))
                .ProtocolTool.InputSchema.GetRawText();
            string reviewHandoffSchema = McpToolProfile.CreateTools(AutomationToolProfiles.ProcessReview)
                .Single(tool => string.Equals(
                    tool.ProtocolTool.Name, "submit_review_handoff", StringComparison.Ordinal))
                .ProtocolTool.InputSchema.GetRawText();
            JsonObject? reviewHandoffObject = FindSchemaByProperties(
                JsonNode.Parse(reviewHandoffSchema), "status", "summary", "findings");
            if (!string.Equals(reviewDecisionSchema, coordinatorSchema, StringComparison.Ordinal)
                || !reviewHandoffSchema.Contains("evidenceFactRefs", StringComparison.Ordinal)
                || !reviewHandoffSchema.Contains(ReviewFindingCategories.StructuralDefect, StringComparison.Ordinal)
                || reviewHandoffObject == null
                || reviewHandoffObject["properties"]?["verifiedFacts"] != null)
            {
                throw new InvalidOperationException(
                    "ProcessReview 控制Schema或独立评审交接Schema不符合契约。");
            }

            string[] control = ToolNames(AutomationToolProfiles.RuntimeControl);
            if (!control.Contains("start_proc", StringComparer.Ordinal)
                || !control.Contains("validate_proc", StringComparer.Ordinal)
                || control.Contains("preview_change_set", StringComparer.Ordinal)
                || control.Contains("apply_change_set", StringComparer.Ordinal))
                throw new InvalidOperationException("RuntimeControl 工具边界错误。");

            string[] resources = ToolNames(AutomationToolProfiles.ResourceEdit);
            if (!resources.Contains("list_authoring_resources", StringComparer.Ordinal)
                || !resources.Contains("plan_motion_points", StringComparer.Ordinal)
                || !resources.Contains("add_variable", StringComparer.Ordinal)
                || !resources.Contains("upsert_data_struct", StringComparer.Ordinal)
                || !resources.Contains("set_alarm", StringComparer.Ordinal)
                || !resources.Contains("update_io_note", StringComparer.Ordinal)
                || resources.Contains("preview_change_set", StringComparer.Ordinal)
                || resources.Contains("start_proc", StringComparer.Ordinal))
                throw new InvalidOperationException("ResourceEdit 工具边界错误。");
            string planPointSchema = McpToolProfile.CreateTools(AutomationToolProfiles.ResourceEdit)
                .Single(tool => string.Equals(
                    tool.ProtocolTool.Name, "plan_motion_points", StringComparison.Ordinal))
                .ProtocolTool.InputSchema.GetRawText();
            if (!planPointSchema.Contains("\"maxItems\":20", StringComparison.Ordinal)
                || !planPointSchema.Contains("\"maxLength\":100", StringComparison.Ordinal)
                || !planPointSchema.Contains("\"minimum\":0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("点位规划工具的批量、名称或工站索引Schema边界不完整。");
            }

            string[] platform = ToolNames(AutomationToolProfiles.PlatformConfiguration);
            if (!platform.Contains("get_migration_configuration", StringComparer.Ordinal)
                || !platform.Contains("apply_migration_configuration", StringComparer.Ordinal)
                || platform.Contains("preview_change_set", StringComparer.Ordinal))
                throw new InvalidOperationException("PlatformConfiguration 工具边界错误。");

            McpServerTool createPreviewTool = McpToolProfile.CreateTools(AutomationToolProfiles.ProcessCreate)
                .Single(tool => string.Equals(
                    tool.ProtocolTool.Name, "preview_change_set", StringComparison.Ordinal));
            string createPreviewSchema = createPreviewTool.ProtocolTool.InputSchema.GetRawText();
            int createPreviewSchemaBytes = Encoding.UTF8.GetByteCount(createPreviewSchema);
            VerifyPreviewChangeSetCompactContract(
                createPreviewSchema,
                ChangeSetActionTypes.SupportedTypes.Split('、')
                    .Where(type => !string.Equals(type, "process.delete", StringComparison.Ordinal)
                        && !string.Equals(type, "process.delete_all", StringComparison.Ordinal))
                    .ToArray());
            var dynamicRegistry = new DynamicMcpToolRegistry(AutomationToolProfiles.ProcessCreate);
            string activeCreateSchema = dynamicRegistry.GetEnabledTool("preview_change_set")
                .ProtocolTool.InputSchema.GetRawText();
            if (!string.Equals(activeCreateSchema, createPreviewSchema, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "动态工具注册表未返回 ProcessCreate 的专用 preview_change_set Schema。");
            }
            using (AutomationMcpRuntime.BeginToolInvocation(AutomationToolProfiles.ProcessCreate))
            {
                dynamicRegistry.SetProfile(AutomationToolProfiles.ProcessEdit);
                if (!string.Equals(
                    AutomationMcpRuntime.CurrentToolProfile,
                    AutomationToolProfiles.ProcessCreate,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "工具调用期间的 Profile 快照被并发切换污染。");
                }
            }
            if (createPreviewSchema.Contains("preview_process_blueprint", StringComparison.Ordinal)
                || createPreviewSchema.Contains("retries", StringComparison.Ordinal)
                || createPreviewSchema.Contains("entryMode", StringComparison.Ordinal)
                || !createPreviewSchema.Contains("authoringLeaseId", StringComparison.Ordinal)
                || !createPreviewSchema.Contains("x-processCreateModes", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ProcessCreate ChangeSet Schema 的增量创建契约不完整或仍暴露已退役字段。");
            }
            VerifyProcessCreateChangeSetBoundary();
            VerifyAuthoringResourceRequestContract();

            Console.WriteLine(
                $"任务能力包校验通过：Coordinator={coordinator.Length}, Design={design.Length}, Review={ToolNames(AutomationToolProfiles.ProcessReview).Length}, "
                + $"Create={create.Length}, Edit={edit.Length}, Resource={resources.Length}, Runtime={control.Length}, Source={source.Length}, "
                + $"Platform={platform.Length}；Control Schema={coordinatorSchemaBytes}字节；"
                + $"Create ChangeSet Schema={createPreviewSchemaBytes}字节。");
        }

        private static void VerifyProcessCreateChangeSetBoundary()
        {
            var valid = new AtomicChangeSetDefinition
            {
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "process.create",
                        Process = new ProcessActionValue
                        {
                            Key = "new_process",
                            Name = "创建边界自检",
                            AutoStart = false
                        }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "new_process" },
                        Step = new StepActionValue { Key = "main", Name = "主步骤" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "new_process" },
                        TargetStep = new StepSelector { Key = "main" },
                        Operation = new SemanticOperation { Kind = "flow.end" }
                    }
                }
            };
            AutomationMcpTools.ValidateProcessCreateChangeSet(valid);

            string createdProcId = Guid.NewGuid().ToString("D");
            var authoringLease = new ProcessAuthoringLease(
                new string('a', 32), createdProcId, "创建边界自检");
            var continuation = new AtomicChangeSetDefinition
            {
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { ProcId = createdProcId },
                        Step = new StepActionValue { Key = "next", Name = "后续步骤" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { ProcId = createdProcId },
                        TargetStep = new StepSelector { Key = "next" },
                        Operation = new SemanticOperation { Kind = "flow.end" }
                    }
                }
            };
            AutomationMcpTools.ValidateProcessCreateChangeSet(continuation, authoringLease);

            continuation.Actions[0].TargetProcess = new ProcessSelector { ProcId = Guid.NewGuid().ToString("D") };
            try
            {
                AutomationMcpTools.ValidateProcessCreateChangeSet(continuation, authoringLease);
                throw new InvalidOperationException("ProcessCreate 续建边界自检未拒绝其他流程。");
            }
            catch (ArgumentException)
            {
                // 期望路径：续建凭据只能写入首次创建返回的稳定流程。
            }

            var existingProcessEdit = new AtomicChangeSetDefinition
            {
                Actions = new List<ChangeSetAction>(valid.Actions)
                {
                    new ChangeSetAction
                    {
                        Type = "process.update",
                        TargetProcess = new ProcessSelector { Name = "现有流程" },
                        Process = new ProcessActionValue { Disable = true }
                    }
                }
            };
            try
            {
                AutomationMcpTools.ValidateProcessCreateChangeSet(existingProcessEdit);
                throw new InvalidOperationException("ProcessCreate 边界自检未拒绝既有流程修改。");
            }
            catch (ArgumentException)
            {
                // 期望路径：创建能力不得越界修改已提交流程。
            }

            // 追加修正边界：目标只能用稳定 procId，且不得再次创建/删除流程。
            var amendment = new AtomicChangeSetDefinition
            {
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { ProcId = createdProcId },
                        TargetStep = new StepSelector { StepId = Guid.NewGuid().ToString("D") },
                        Operation = new SemanticOperation { Kind = "flow.end" }
                    }
                }
            };
            AutomationMcpTools.ValidateProcessCreateAmendment(amendment);
            amendment.Actions.Add(new ChangeSetAction
            {
                Type = "process.create",
                Process = new ProcessActionValue { Key = "another", Name = "越界创建" }
            });
            try
            {
                AutomationMcpTools.ValidateProcessCreateAmendment(amendment);
                throw new InvalidOperationException("ProcessCreate 追加修正边界自检未拒绝再次创建流程。");
            }
            catch (ArgumentException)
            {
                // 期望路径：追加修正只完善被修正预演中已创建的流程。
            }
            amendment.Actions.RemoveAt(amendment.Actions.Count - 1);
            amendment.Actions[0].TargetProcess = new ProcessSelector { Key = createdProcId };
            try
            {
                AutomationMcpTools.ValidateProcessCreateAmendment(amendment);
                throw new InvalidOperationException("ProcessCreate 追加修正边界自检未拒绝局部 key 目标。");
            }
            catch (ArgumentException)
            {
                // 期望路径：局部 key 不跨预演，追加修正必须使用稳定 ID。
            }

            const string initialPreviewId = "11111111111111111111111111111111";
            ProcessAuthoringLeaseRegistry.BindInitialPreview(initialPreviewId);
            if (!ProcessAuthoringLeaseRegistry.IsInitialPreview(initialPreviewId))
                throw new InvalidOperationException("ProcessCreate 首阶段预演未被标记为可签发创建工作区凭据。");
            string applied = new JsonObject
            {
                ["ok"] = true,
                ["type"] = "change_set.apply",
                ["data"] = new JsonObject
                {
                    ["createdObjects"] = new JsonObject
                    {
                        ["processes"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["procId"] = createdProcId,
                                ["name"] = "创建边界自检"
                            }
                        }
                    }
                }
            }.ToJsonString();
            ProcessAuthoringLease? issuedLease =
                ProcessAuthoringLeaseRegistry.RegisterCreatedProcess(applied);
            if (issuedLease == null || issuedLease.ProcId != createdProcId)
                throw new InvalidOperationException("ProcessCreate 提交后没有签发目标收窄的创建工作区凭据。");
            const string continuationPreviewId = "22222222222222222222222222222222";
            ProcessAuthoringLeaseRegistry.BindPreview(continuationPreviewId, issuedLease);
            if (!ReferenceEquals(
                    ProcessAuthoringLeaseRegistry.GetPreviewLease(continuationPreviewId),
                    issuedLease))
                throw new InvalidOperationException("ProcessCreate 续建预演没有绑定原创建工作区。");
            string attached = ProcessAuthoringLeaseRegistry.AttachToApplyResult(applied, issuedLease);
            string? attachedLeaseId = (JsonNode.Parse(attached) as JsonObject)?["data"]?["authoringLease"]?["leaseId"]
                ?.GetValue<string>();
            if (!string.Equals(attachedLeaseId, issuedLease.LeaseId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ProcessCreate 提交结果缺少可继续使用的authoringLease。");
            }
            ProcessAuthoringLeaseRegistry.CompletePreview(initialPreviewId);
            ProcessAuthoringLeaseRegistry.CompletePreview(continuationPreviewId);
        }

        private static void VerifyAuthoringResourceRequestContract()
        {
            var request = new AuthoringResourceListRequest
            {
                Type = "motion"
            };
            AuthoringResourceListRequest normalized =
                AutomationMcpTools.NormalizeAuthoringResourceRequest(request, 0);
            if (!string.Equals(normalized.Type, "motion", StringComparison.Ordinal)
                || normalized.NameLike != null)
            {
                throw new InvalidOperationException("作者资源目录错误要求先提供名称过滤。");
            }
            string ioRef = AuthoringResourceRefs.ForIo("通用输入", 0, 0, "1");
            if (!string.Equals(ioRef, "io_input:0:0:1", StringComparison.Ordinal))
                throw new InvalidOperationException("作者资源目录没有生成稳定的类型化IO引用。");
            JsonArray projectedIo = AutomationMcpTools.ProjectAuthoringResourceItems(
                "io_input",
                new JsonArray(new JsonObject
                {
                    ["index"] = 1,
                    ["name"] = "到位感应",
                    ["cardNum"] = 0,
                    ["module"] = 0,
                    ["ioIndex"] = "1",
                    ["ioType"] = "通用输入",
                    ["referenceImpact"] = new JsonObject { ["total"] = 99 }
                }));
            if (!string.Equals(
                    projectedIo[0]?["resourceRef"]?.GetValue<string>(),
                    ioRef,
                    StringComparison.Ordinal)
                || !string.Equals(
                    projectedIo[0]?["binding"]?["value"]?.GetValue<string>(),
                    ioRef,
                    StringComparison.Ordinal)
                || projectedIo[0]?["referenceImpact"] != null)
            {
                throw new InvalidOperationException("作者资源目录没有返回可直接绑定的紧凑IO投影。");
            }
            JsonArray projectedVariable = AutomationMcpTools.ProjectAuthoringResourceItems(
                "variable",
                new JsonArray(new JsonObject
                {
                    ["variableId"] = "11111111-1111-1111-1111-111111111111",
                    ["name"] = "状态",
                    ["type"] = "double",
                    ["scope"] = "public",
                    ["value"] = "123",
                    ["referenceImpact"] = new JsonObject { ["total"] = 99 }
                }));
            if (projectedVariable[0]?["value"] != null
                || projectedVariable[0]?["referenceImpact"] != null
                || string.IsNullOrWhiteSpace(projectedVariable[0]?["resourceRef"]?.GetValue<string>()))
            {
                throw new InvalidOperationException("作者资源目录仍混入变量运行值或引用影响。");
            }
            JsonObject evidenceBoundaries =
                AutomationMcpTools.BuildAuthoringResourceEvidenceBoundaries();
            if (!(evidenceBoundaries["missingFact"]?.GetValue<string>() ?? string.Empty)
                    .Contains("不能据此判定该功能不需要", StringComparison.Ordinal)
                || !(evidenceBoundaries["ioState"]?.GetValue<string>() ?? string.Empty)
                    .Contains("不证明机构已到达相反终态", StringComparison.Ordinal)
                || !(evidenceBoundaries["goalPreservation"]?.GetValue<string>() ?? string.Empty)
                    .Contains("config.placeholder", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("作者资源目录缺少防止静默目标降级的证据边界。");
            }
            var motionStation = new JsonObject { ["name"] = "搬运工站" };
            JsonObject? missingTarget = AutomationMcpTools.BuildMotionAuthoringGap(
                motionStation, "motion_station:0", 0, 0);
            if (!string.Equals(
                    missingTarget?["code"]?.GetValue<string>(),
                    "MOTION_NAMED_TARGET_MISSING",
                    StringComparison.Ordinal)
                || !(missingTarget?["impact"]?.GetValue<string>() ?? string.Empty)
                    .Contains("不证明当前目标不需要", StringComparison.Ordinal)
                || !(missingTarget?["impact"]?.GetValue<string>() ?? string.Empty)
                    .Contains("规划有业务含义的点位名", StringComparison.Ordinal)
                || (missingTarget?["nextOptions"] as JsonArray)?.Count != 3
                || AutomationMcpTools.BuildMotionAuthoringGap(
                    motionStation, "motion_station:0", 1, 1) != null)
            {
                throw new InvalidOperationException("无命名点位的运动工站没有返回准确、非阻断的作者缺口。");
            }
            JsonObject? teachingRequired = AutomationMcpTools.BuildMotionAuthoringGap(
                motionStation, "motion_station:0", 2, 1);
            if (!string.Equals(
                    teachingRequired?["code"]?.GetValue<string>(),
                    "MOTION_POINT_TEACHING_REQUIRED",
                    StringComparison.Ordinal)
                || !(teachingRequired?["impact"]?.GetValue<string>() ?? string.Empty)
                    .Contains("保持incomplete", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("规划点位没有明确返回人工示教与启动阻塞边界。");
            }
            request.Type = "station";
            try
            {
                AutomationMcpTools.NormalizeAuthoringResourceRequest(request, 0);
                throw new InvalidOperationException("作者资源目录接受了已退役的拆分工站类别。");
            }
            catch (ArgumentException)
            {
                // 期望路径：运动资源由motion一次聚合工站、轴和点位。
            }
        }

        private static void VerifyOperationCapabilityResolver()
        {
            string[] wait = AutomationMcpTools.ResolveSemanticCandidates("等待工件到位信号");
            string[] copy = AutomationMcpTools.ResolveSemanticCandidates("复制扫码结果字符串到载具编号变量");
            string[] cylinderOut = AutomationMcpTools.ResolveSemanticCandidates("气缸伸出动作");
            string[] cylinderFeedback = AutomationMcpTools.ResolveSemanticCandidates("等待气缸到位反馈");
            string[] axisFeedback = AutomationMcpTools.ResolveSemanticCandidates("等待轴到位");
            if (!wait.SequenceEqual(new[] { "io.wait" }, StringComparer.Ordinal)
                || !copy.SequenceEqual(new[] { "variable.copy" }, StringComparer.Ordinal)
                || !cylinderOut.SequenceEqual(new[] { "io.write" }, StringComparer.Ordinal)
                || !cylinderFeedback.SequenceEqual(new[] { "io.wait" }, StringComparer.Ordinal)
                || axisFeedback.Length != 0)
            {
                throw new InvalidOperationException("常见业务措辞未能直接解析为平台语义能力。");
            }
            JsonArray native = AutomationMcpTools.RankNativeOperationCandidates(
                new JsonArray
                {
                    new JsonObject
                    {
                        ["operaType"] = "工站走点",
                        ["name"] = "工站走点",
                        ["intentAliases"] = new JsonArray("移动到取料位", "移动到放料位")
                    }
                },
                "移动到取料位");
            if (native.Count != 1
                || native[0]?["operaType"]?.GetValue<string>() != "工站走点")
            {
                throw new InvalidOperationException("运动业务措辞未能映射到权威原生动作别名。");
            }
            JsonObject exactSemantic = AutomationMcpTools.BuildOperationCapabilityResolutionItem(
                "clear-count", "清零执行次数", new[] { "number.zero" }, new JsonArray());
            if (!string.Equals(
                    exactSemantic["resolutionStatus"]?.GetValue<string>(), "exact", StringComparison.Ordinal)
                || !string.Equals(
                    exactSemantic["resolutionScope"]?.GetValue<string>(), "operation_kind_only", StringComparison.Ordinal)
                || !string.Equals(
                    exactSemantic["resourceBindingValidation"]?.GetValue<string>(), "not_performed", StringComparison.Ordinal)
                || !string.Equals(
                    exactSemantic["contractRef"]?.GetValue<string>(),
                    "semantic.number.zero",
                    StringComparison.Ordinal)
                || exactSemantic["contractIncluded"]?.GetValue<bool>() != true
                || exactSemantic.ContainsKey("nextContractTool")
                || exactSemantic.ContainsKey("contractReadAllowed"))
            {
                throw new InvalidOperationException("唯一语义能力必须声明同结果已包含精确契约，且不得残留二次读取Schema提示。");
            }
            JsonObject exactNative = AutomationMcpTools.BuildOperationCapabilityResolutionItem(
                "scan", "调用扫码枪读取条码", Array.Empty<string>(), new JsonArray
                {
                    new JsonObject { ["operaType"] = "扫码读取", ["name"] = "扫码枪读取" }
                });
            if (!string.Equals(
                    exactNative["resolved"]?["representation"]?.GetValue<string>(), "native", StringComparison.Ordinal)
                || !string.Equals(
                    exactNative["contractRef"]?.GetValue<string>(), "native.扫码读取", StringComparison.Ordinal)
                || exactNative["contractIncluded"]?.GetValue<bool>() != true)
            {
                throw new InvalidOperationException("唯一原生能力必须返回可直接写入的解析身份和同结果契约引用。");
            }

            var registered = new JsonArray
            {
                new JsonObject { ["operaType"] = "延时", ["name"] = string.Empty },
                new JsonObject { ["operaType"] = "流程结束", ["name"] = string.Empty }
            };
            JsonArray unrelated = AutomationMcpTools.RankNativeOperationCandidates(
                registered, "调用扫码枪读取条码");
            if (unrelated.Count != 0)
                throw new InvalidOperationException("原生能力解析不得用空名称匹配所有注册指令。");

            // 换述措辞必须通过共享二元组命中别名，避免模型猜类型名试错。
            JsonArray paraphrased = AutomationMcpTools.RankNativeOperationCandidates(
                new JsonArray
                {
                    new JsonObject
                    {
                        ["operaType"] = "工站走点",
                        ["name"] = "工站走点",
                        ["intentAliases"] = new JsonArray("移动到点位", "运动到点位")
                    }
                },
                "将工站运动到指定命名点位");
            if (paraphrased.Count != 1
                || paraphrased[0]?["operaType"]?.GetValue<string>() != "工站走点")
            {
                throw new InvalidOperationException("换述的运动意图未能按共享二元组命中权威原生别名。");
            }
            JsonObject missing = AutomationMcpTools.BuildOperationCapabilityResolutionItem(
                "move", "抓取工件放到料盒", Array.Empty<string>(), new JsonArray(),
                new JsonArray("工站走点", "偏移量"));
            if (!string.Equals(
                    missing["resolutionStatus"]?.GetValue<string>(), "missing", StringComparison.Ordinal)
                || (missing["nearbyTypes"] as JsonArray)?.Count != 2
                || !(missing["recommendedFallback"]?.GetValue<string>() ?? string.Empty)
                    .Contains("list_operation_types", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("missing 结果必须附相近注册类型，不得裸推荐占位。");
            }
        }

        private static string[] ToolNames(string profile)
        {
            return McpToolProfile.CreateTools(profile)
                .Select(tool => tool.ProtocolTool.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        private static void VerifyPreviewChangeSetCompactContract(
            string schemaJson,
            IReadOnlyCollection<string>? expectedTypes = null)
        {
            if ((schemaJson ?? string.Empty).Contains("\"entryMode\"", StringComparison.Ordinal))
                throw new InvalidOperationException("preview_change_set 不得暴露已退役的 entryMode。");
            JsonObject root = JsonNode.Parse(schemaJson ?? string.Empty) as JsonObject
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
                ?? throw new InvalidOperationException("preview_change_set 未找到ChangeAction契约。");
            EnsureClosedBranch(actionSchema, "ChangeAction");
            JsonObject actionProperties = actionSchema["properties"] as JsonObject
                ?? throw new InvalidOperationException("ChangeAction缺少properties。");
            string[] expectedActionTypes = expectedTypes?.ToArray()
                ?? ChangeSetActionTypes.SupportedTypes.Split('、');
            JsonArray actionTypeEnum = actionProperties["type"]?["enum"] as JsonArray
                ?? throw new InvalidOperationException("ChangeAction.type缺少枚举。");
            var actualActionTypes = new HashSet<string>(
                actionTypeEnum.Select(item => item?.GetValue<string>() ?? string.Empty),
                StringComparer.Ordinal);
            if (!actualActionTypes.SetEquals(expectedActionTypes))
                throw new InvalidOperationException("ChangeAction类型枚举与当前 Profile 的动作集不一致。");
            JsonObject fieldsByType = actionSchema["x-fieldsByType"] as JsonObject
                ?? throw new InvalidOperationException("ChangeAction缺少x-fieldsByType。");
            if (expectedActionTypes.Any(type => !fieldsByType.ContainsKey(type)))
                throw new InvalidOperationException("ChangeAction字段映射不完整。");
            JsonObject operationReference = actionProperties["operation"] as JsonObject
                ?? throw new InvalidOperationException("ChangeAction缺少operation引用。");
            VerifySemanticOperationContract(
                root,
                ResolveLocalSchemaReference(root, operationReference));
        }

        private static void VerifySemanticOperationContract(
            JsonObject root,
            JsonObject operationSchema)
        {
            if (!string.Equals(operationSchema["x-symbolicTargetScope"]?.GetValue<string>(),
                    "operation_id_or_change_set_key", StringComparison.Ordinal))
                throw new InvalidOperationException("SemanticOperation缺少符号目标作用域。");
            EnsureClosedBranch(operationSchema, "SemanticOperation");
            JsonObject properties = operationSchema["properties"] as JsonObject
                ?? throw new InvalidOperationException("SemanticOperation缺少properties。");
            string[] expectedKinds = SemanticOperationKinds.SupportedKinds.Split('、');
            JsonArray kindEnum = properties["kind"]?["enum"] as JsonArray
                ?? throw new InvalidOperationException("SemanticOperation.kind缺少枚举。");
            var actualKinds = new HashSet<string>(
                kindEnum.Select(item => item?.GetValue<string>() ?? string.Empty),
                StringComparer.Ordinal);
            if (!actualKinds.SetEquals(expectedKinds))
                throw new InvalidOperationException("SemanticOperation类型枚举与SupportedKinds不一致。");
            JsonObject fieldsByKind = operationSchema["x-fieldsByKind"] as JsonObject
                ?? throw new InvalidOperationException("SemanticOperation缺少x-fieldsByKind。");
            if (expectedKinds.Any(kind => !fieldsByKind.ContainsKey(kind)))
                throw new InvalidOperationException("SemanticOperation字段映射不完整。");
            foreach (string commonField in new[] { "opId", "key", "name" })
            {
                if (!properties.ContainsKey(commonField))
                    throw new InvalidOperationException($"SemanticOperation缺少公共字段{commonField}。");
            }
            foreach (string requiredField in new[]
                { "conditions", "onFailure", "conditionLogic", "whenTrue", "whenFalse", "outputs", "timeoutMs" })
            {
                if (!properties.ContainsKey(requiredField))
                    throw new InvalidOperationException($"SemanticOperation缺少字段{requiredField}。");
            }
            VerifyIoConditionsSchema(root, properties["conditions"] as JsonObject, "io.conditions");
            VerifyIoConditionsSchema(root, properties["outputs"] as JsonObject, "io.outputs");
            JsonObject conditionLogic = ResolveLocalSchemaReference(
                root,
                properties["conditionLogic"] as JsonObject
                    ?? throw new InvalidOperationException("branch.io.conditionLogic缺少Schema。"));
            if (conditionLogic["enum"] is not JsonArray logicValues
                || !logicValues.Any(value => string.Equals(value?.GetValue<string>(), "all", StringComparison.Ordinal))
                || !logicValues.Any(value => string.Equals(value?.GetValue<string>(), "any", StringComparison.Ordinal)))
                throw new InvalidOperationException("branch.io.conditionLogic未限制为all/any。");
            JsonObject fieldsSchema = ResolveLocalSchemaReference(
                root,
                properties["fields"] as JsonObject
                    ?? throw new InvalidOperationException("native.operation.fields缺少Schema。"));
            if (fieldsSchema["additionalProperties"] is JsonValue additional
                    && additional.TryGetValue(out bool allowsFields) && !allowsFields)
                throw new InvalidOperationException("native.operation.fields未保留动态原生字段。");
        }

        private static void VerifyCompactChangeSetPreviewResult()
        {
            string raw = new JsonObject
            {
                ["ok"] = true,
                ["type"] = "change_set.preview",
                ["data"] = new JsonObject
                {
                    ["previewId"] = "preview-1",
                    ["confirmed"] = true,
                    ["status"] = "confirmed",
                    ["nextStep"] = "提交",
                    ["allowedTransitions"] = new JsonArray("apply", "discard"),
                    ["readinessStatus"] = "incomplete",
                    ["runnable"] = false,
                    ["changes"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "process.delete",
                        ["procId"] = "proc-1",
                        ["name"] = "示例流程"
                    }),
                    ["messages"] = new JsonArray("本次将删除 1 个流程。"),
                    ["createdObjects"] = new JsonObject
                    {
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["stepKey"] = "step",
                            ["key"] = "end",
                            ["opId"] = "op-1",
                            ["name"] = "结束",
                            ["operaType"] = "End",
                            ["redundant"] = "discard"
                        })
                    },
                    ["pendingItems"] = new JsonArray(new JsonObject
                    {
                        ["category"] = "planned_point",
                        ["opId"] = "op-2",
                        ["pointName"] = "取料位",
                        ["repair"] = "manual_teaching",
                        ["redundant"] = "discard"
                    }),
                    ["processSnapshot"] = new JsonArray(new JsonObject
                    {
                        ["procId"] = "proc-1",
                        ["totalOps"] = 1,
                        ["steps"] = new JsonArray()
                    })
                }
            }.ToJsonString();
            string compact = AutomationMcpTools.CompactChangeSetPreviewResult(raw);
            JsonObject response = JsonNode.Parse(compact) as JsonObject
                ?? throw new InvalidOperationException("preview_change_set 紧凑结果不是JSON对象。");
            JsonObject data = response["data"] as JsonObject
                ?? throw new InvalidOperationException("preview_change_set 紧凑结果缺少data。");
            JsonObject operation = data["createdObjects"]?["operations"]?[0] as JsonObject
                ?? throw new InvalidOperationException("preview_change_set 紧凑结果丢失新建指令身份。");
            JsonObject? pendingItem = data["pendingItems"]?[0] as JsonObject;
            if (data["previewId"]?.GetValue<string>() != "preview-1"
                || data["status"]?.GetValue<string>() != "confirmed"
                || (data["allowedTransitions"] as JsonArray)?.Count != 2
                || data["changes"] is not JsonArray changes
                || changes.Count != 1
                || (changes[0] as JsonObject)?["procId"]?.GetValue<string>() != "proc-1"
                || data["messages"] is not JsonArray messages
                || messages.Count != 1
                || operation["opId"]?.GetValue<string>() != "op-1"
                || operation.ContainsKey("redundant")
                || pendingItem?["pointName"]?.GetValue<string>() != "取料位"
                || pendingItem.ContainsKey("redundant")
                || data.ContainsKey("processSnapshot"))
            {
                throw new InvalidOperationException(
                    "preview_change_set 紧凑结果丢失确认状态、变化明细、摘要文案、稳定身份或待补齐清单，或仍携带重复的流程快照。");
            }
            // 失败结果（含 bindingRepair 等修复候选）必须原样透传，不得被压缩丢弃。
            string failure = new JsonObject
            {
                ["ok"] = false,
                ["type"] = "change_set.preview",
                ["errorCode"] = "CHANGE_SET_VALIDATION_FAILED",
                ["bindingRepair"] = new JsonObject { ["candidates"] = new JsonArray("op-1") }
            }.ToJsonString();
            if (!string.Equals(
                    AutomationMcpTools.CompactChangeSetPreviewResult(failure),
                    failure,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("preview_change_set 失败结果必须原样透传。");
            }
        }

        private static void VerifyCompactChangeSetApplyResult()
        {
            string raw = new JsonObject
            {
                ["ok"] = true,
                ["type"] = "change_set.apply",
                ["data"] = new JsonObject
                {
                    ["previewId"] = "preview-1",
                    ["status"] = "committed",
                    ["configurationSaved"] = true,
                    ["readinessStatus"] = "ready",
                    ["runnable"] = true,
                    ["createdObjects"] = new JsonObject
                    {
                        ["processes"] = new JsonArray(new JsonObject
                        {
                            ["key"] = "process",
                            ["procId"] = "proc-1",
                            ["procIndex"] = 0,
                            ["name"] = "流程",
                            ["redundant"] = "discard"
                        }),
                        ["steps"] = new JsonArray(new JsonObject
                        {
                            ["key"] = "step",
                            ["stepId"] = "step-1",
                            ["procId"] = "proc-1",
                            ["name"] = "步骤"
                        }),
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["stepKey"] = "step",
                            ["key"] = "end",
                            ["opId"] = "op-1",
                            ["name"] = "结束",
                            ["operaType"] = "End",
                            ["procId"] = "proc-1",
                            ["stepId"] = "step-1"
                        })
                    },
                    ["runBlockers"] = new JsonArray(),
                    ["pendingItems"] = new JsonArray(new JsonObject
                    {
                        ["category"] = "goto_target",
                        ["procId"] = "proc-1",
                        ["opId"] = "op-2",
                        ["field"] = "defaultGoto",
                        ["repair"] = "operation.update",
                        ["redundant"] = "discard"
                    }),
                    ["processSnapshot"] = new JsonArray(new JsonObject
                    {
                        ["procId"] = "proc-1",
                        ["procIndex"] = 0,
                        ["name"] = "流程",
                        ["totalSteps"] = 1,
                        ["totalOps"] = 1,
                        ["steps"] = new JsonArray(),
                        ["opsOmitted"] = false
                    })
                }
            }.ToJsonString();
            string compact = AutomationMcpTools.CompactChangeSetApplyResult(raw);
            JsonObject response = JsonNode.Parse(compact) as JsonObject
                ?? throw new InvalidOperationException("apply_change_set 紧凑结果不是JSON对象。");
            JsonObject data = response["data"] as JsonObject
                ?? throw new InvalidOperationException("apply_change_set 紧凑结果缺少data。");
            JsonObject operation = data["createdObjects"]?["operations"]?[0] as JsonObject
                ?? throw new InvalidOperationException("apply_change_set 紧凑结果丢失新建指令身份。");
            JsonObject process = data["createdObjects"]?["processes"]?[0] as JsonObject
                ?? throw new InvalidOperationException("apply_change_set 紧凑结果丢失新建流程身份。");
            JsonObject? pendingItem = data["pendingItems"]?[0] as JsonObject;
            if (pendingItem?["category"]?.GetValue<string>() != "goto_target"
                || pendingItem?["opId"]?.GetValue<string>() != "op-2"
                || pendingItem.ContainsKey("redundant")
                || data["processSnapshot"]?[0]?["procId"]?.GetValue<string>() != "proc-1")
            {
                throw new InvalidOperationException(
                    "apply_change_set 紧凑结果丢失待补齐清单或流程结构回显。");
            }
            if (data["configurationSaved"]?.GetValue<bool>() != true
                || data["readinessStatus"]?.GetValue<string>() != "ready"
                || operation["opId"]?.GetValue<string>() != "op-1"
                || operation["procId"]?.GetValue<string>() != "proc-1"
                || operation["stepId"]?.GetValue<string>() != "step-1"
                || process.ContainsKey("redundant"))
            {
                throw new InvalidOperationException(
                    "apply_change_set 紧凑结果丢失提交事实、稳定身份或父级关联。");
            }
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
            VerifyNumericRange(tools, "resolve_proc_target", "limitPerKeyword", 1, 20);
            VerifyNumericRange(tools, "list_authoring_resources", "limitPerType", 1, 100);
            string resolverSchema = tools.Single(tool => string.Equals(
                tool.ProtocolTool.Name, "resolve_proc_target", StringComparison.Ordinal))
                .ProtocolTool.InputSchema.GetRawText();
            JsonObject? resolverKeywords = (JsonNode.Parse(resolverSchema) as JsonObject)?["properties"]?["keywords"]
                as JsonObject;
            if (resolverKeywords?["minItems"]?.GetValue<int>() != 1
                || resolverKeywords?["maxItems"]?.GetValue<int>() != 6
                || resolverKeywords?["items"]?["pattern"]?.GetValue<string>()
                    != "^(?!\\*$).*\\S.*$")
            {
                throw new InvalidOperationException("流程目标聚合定位Schema缺少关键词数量或普通文本约束。");
            }
            string authoringResourceSchema = tools.Single(tool => string.Equals(
                tool.ProtocolTool.Name, "list_authoring_resources", StringComparison.Ordinal))
                .ProtocolTool.InputSchema.GetRawText();
            if (!authoringResourceSchema.Contains("\"minItems\":1", StringComparison.Ordinal)
                || !authoringResourceSchema.Contains("\"maxItems\":9", StringComparison.Ordinal)
                || !authoringResourceSchema.Contains("\"motion\"", StringComparison.Ordinal)
                || !authoringResourceSchema.Contains("\"io_input\"", StringComparison.Ordinal)
                || !authoringResourceSchema.Contains("\"io_output\"", StringComparison.Ordinal)
                || !authoringResourceSchema.Contains("nameLike", StringComparison.Ordinal)
                || !authoringResourceSchema.Contains("offset", StringComparison.Ordinal)
                || authoringResourceSchema.Contains("names", StringComparison.Ordinal)
                || authoringResourceSchema.Contains("keywords", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("作者资源目录Schema没有落实按类别先枚举、名称过滤可选的契约。");
            }
            string operationResolutionSchema = tools.Single(tool => string.Equals(
                tool.ProtocolTool.Name, "resolve_operation_capability", StringComparison.Ordinal))
                .ProtocolTool.InputSchema.GetRawText();
            if (!operationResolutionSchema.Contains("\"maxItems\":12", StringComparison.Ordinal)
                || !operationResolutionSchema.Contains("\"required\":[\"key\",\"intent\"]", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("指令能力解析Schema缺少动作意图边界。");
            }
            string fieldContractSchema = tools.Single(tool => string.Equals(
                tool.ProtocolTool.Name, "get_native_operation_field_contract", StringComparison.Ordinal))
                .ProtocolTool.InputSchema.GetRawText();
            if (!fieldContractSchema.Contains("\"minItems\":1", StringComparison.Ordinal)
                || !fieldContractSchema.Contains("\"maxItems\":12", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("原生字段契约工具缺少字段数量约束。");
            }
            VerifyNumericRange(tools, "list_io", "offset", 0, int.MaxValue);
            VerifyNumericRange(tools, "list_io", "limit", 1, 100);
            VerifyNumericRange(tools, "search_io", "offset", 0, int.MaxValue);
            VerifyNumericRange(tools, "search_io", "limit", 1, 100);
            VerifyNumericRange(tools, "audit_proc_batch", "procOffset", 0, int.MaxValue);
            VerifyNumericRange(tools, "audit_proc_batch", "procLimit", 1, 50);
            VerifyNumericRange(tools, "audit_proc_batch", "findingOffset", 0, int.MaxValue);
            VerifyNumericRange(tools, "audit_proc_batch", "findingLimit", 1, 300);
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

        private static JsonObject? FindDecisionBranch(JsonNode? node, string actionName)
        {
            if (node is JsonObject obj)
            {
                if (obj["properties"]?["action"]?["enum"] is JsonArray actions
                    && actions.Any(value => string.Equals(
                        value?.GetValue<string>(), actionName, StringComparison.Ordinal)))
                {
                    return obj;
                }
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    JsonObject? found = FindDecisionBranch(property.Value, actionName);
                    if (found != null) return found;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    JsonObject? found = FindDecisionBranch(item, actionName);
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
