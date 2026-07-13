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
                "list_operation_types", "get_operation_schemas", "preview_change_set",
                "apply_change_set", "validate_proc", "wait_for_proc_state", "run_proc_test"
            };
            string[] retired =
            {
                "preview_intent", "apply_intent", "preview_patch", "apply_patch",
                "create_proc", "create_proc_batch",
                "add_station", "update_station", "delete_station", "set_point",
                "delete_point", "set_data_struct_field", "set_alarm", "delete_alarm",
                "get_change_capabilities", "get_operation_contracts", "get_native_operation_contract",
                "begin_change_set_draft", "append_change_set_draft", "get_change_set_draft",
                "stage_changes", "get_staged_changes", "preview_staged_changes", "discard_staged_changes"
            };
            string? missing = required.FirstOrDefault(name => !names.Contains(name, StringComparer.Ordinal));
            if (missing != null) throw new InvalidOperationException($"Editor Profile 缺少工具：{missing}");
            string? exposed = retired.FirstOrDefault(name => names.Contains(name, StringComparer.Ordinal));
            if (exposed != null) throw new InvalidOperationException($"Editor Profile 意外暴露旧写入工具：{exposed}");
            if (names.Contains("audit_proc_batch", StringComparer.Ordinal))
                throw new InvalidOperationException("Editor Profile 不应固定暴露细粒度审计工具。");
            McpServerTool previewTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "preview_change_set", StringComparison.Ordinal));
            string previewSchema = previewTool.ProtocolTool.InputSchema.ToString();
            if (!previewSchema.Contains("expectedOperaTypes", StringComparison.Ordinal)
                || !previewSchema.Contains("operations", StringComparison.Ordinal)
                || !previewSchema.Contains("operaType", StringComparison.Ordinal)
                || previewSchema.Contains("draftId", StringComparison.Ordinal)
                || previewSchema.Contains("expectedOperationCount", StringComparison.Ordinal)
                || previewSchema.Contains("kind", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("完整变更Schema泄漏草稿协议或缺少原生指令定义。");
            }
            McpServerTool runTestTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "run_proc_test", StringComparison.Ordinal));
            McpServerTool startTool = editorTools.First(tool =>
                string.Equals(tool.ProtocolTool.Name, "start_proc", StringComparison.Ordinal));
            if (!(runTestTool.ProtocolTool.Description ?? string.Empty).Contains("禁止先调用start_proc", StringComparison.Ordinal)
                || !(startTool.ProtocolTool.Description ?? string.Empty).Contains("直接调用run_proc_test", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("流程启动与有边界测试的工具职责未双向公开。");
            }
            string[] diagnosticNames = McpToolProfile.CreateTools("Diagnostic")
                .Select(tool => tool.ProtocolTool.Name).ToArray();
            string[] editorWriteNames =
            {
                "get_operation_schemas", "preview_change_set", "apply_change_set"
            };
            if (!diagnosticNames.Contains("audit_proc_batch", StringComparer.Ordinal)
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
