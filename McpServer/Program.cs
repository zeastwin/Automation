using System.Diagnostics;
using System.Drawing;
using System.Text;
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
                    serverOptions.Handlers.CallToolHandler = (request, cancellationToken) =>
                        toolRegistry.GetEnabledTool(request.Params?.Name ?? string.Empty)
                            .InvokeAsync(request, cancellationToken);
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
                toolCount = toolRegistry.GetTools().Count
            }));
            app.MapPost("/tool-profile", (ToolProfileRequest request) =>
            {
                try
                {
                    toolRegistry.SetProfile(request.Profile);
                    return Results.Json(new
                    {
                        ok = true,
                        toolProfile = toolRegistry.Profile,
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
                "wait_for_proc_state", "run_proc_test", "get_communication", "set_alarm", "delete_alarm"
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
            if (names.Contains("audit_proc_batch", StringComparer.Ordinal))
                throw new InvalidOperationException("Editor Profile 不应固定暴露细粒度审计工具。");
            McpServerTool previewTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "preview_change_set", StringComparison.Ordinal));
            string previewSchema = previewTool.ProtocolTool.InputSchema.ToString();
            var schemaIssues = new List<string>();
            string[] requiredSchemaTerms =
            {
                "actions", "targetProcess", "targetOperation", "position", "oneOf",
                "variable.compute", "branch.number_compare", "minimum", "maximum", "kind"
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
            if (!(runTestTool.ProtocolTool.Description ?? string.Empty).Contains("负责启动、观察和安全停止", StringComparison.Ordinal)
                || !(startTool.ProtocolTool.Description ?? string.Empty).Contains("由run_proc_test一次完成", StringComparison.Ordinal)
                || !(discardPreviewTool.ProtocolTool.Description ?? string.Empty).Contains("不修改配置", StringComparison.Ordinal)
                || !(previewTool.ProtocolTool.Description ?? string.Empty).Contains("preview_only", StringComparison.Ordinal)
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
