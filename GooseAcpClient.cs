using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
    public sealed class GooseAcpEvent
    {
        public DateTime Time { get; set; }

        public string Kind { get; set; }

        public string Text { get; set; }

        public JObject Raw { get; set; }
    }

    public sealed class GooseAcpClient : IDisposable
    {
        private const int InitializeTimeoutMs = 30000;
        private const int SessionTimeoutMs = 30000;

        private static readonly string executionLogRoot = Path.Combine(@"D:\AutomationLogs", "AIExecution");
        private static readonly Mutex executionLogMutex = new Mutex(false, "AutomationAIExecutionAuditLog");

        private readonly GooseConfig config;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> pendingRequests =
            new ConcurrentDictionary<string, TaskCompletionSource<JObject>>(StringComparer.Ordinal);
        private readonly object writeLock = new object();
        private readonly object executionLock = new object();
        private readonly string auditSessionId = Guid.NewGuid().ToString("N");
        private readonly StringBuilder assistantResponse = new StringBuilder();
        private int nextRequestId;
        private Process process;
        private StreamWriter stdin;
        private string sessionId;
        private string currentPromptId;
        private bool disposed;

        public GooseAcpClient(GooseConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public event Action<GooseAcpEvent> EventReceived;

        public Func<JObject, JObject> PermissionRequestHandler { get; set; }

        public string SessionId => sessionId;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            EnsureProcessStarted();
            JObject result = await SendRequestAsync("initialize", new JObject
            {
                ["protocolVersion"] = 1,
                ["clientInfo"] = new JObject
                {
                    ["name"] = "Automation",
                    ["version"] = typeof(GooseAcpClient).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                },
                ["clientCapabilities"] = new JObject
                {
                    ["fs"] = new JObject
                    {
                        ["readTextFile"] = false,
                        ["writeTextFile"] = false
                    },
                    ["terminal"] = false
                }
            }, InitializeTimeoutMs, cancellationToken).ConfigureAwait(false);

            Report("lifecycle", "EW-AI ACP 初始化完成。", result);
        }

        public async Task NewSessionAsync(CancellationToken cancellationToken)
        {
            EnsureProcessStarted();
            string sessionWorkingDirectory = ResolveWorkingDirectory();
            if (config.FullPermissionMode)
            {
                string userProfileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string rootDirectory = Path.GetPathRoot(userProfileDirectory);
                if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
                {
                    throw new InvalidOperationException("完全权限模式无法确定当前用户目录所在的文件系统根目录：" + userProfileDirectory);
                }
                sessionWorkingDirectory = rootDirectory;
            }
            JObject result = await SendRequestAsync("session/new", new JObject
            {
                ["cwd"] = sessionWorkingDirectory,
                ["mcpServers"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "automation",
                        ["type"] = "http",
                        ["url"] = config.McpUri,
                        // ACP session/new 的 McpServer HTTP 变体要求 headers 字段（即使为空数组），
                        // 缺失会导致 "data did not match any variant of untagged enum McpServer" 反序列化错误。
                        ["headers"] = new JArray()
                    }
                },
                ["_meta"] = new JObject
                {
                    ["sessionName"] = config.SessionName ?? string.Empty,
                    ["provider"] = config.Provider ?? string.Empty,
                    ["model"] = config.Model ?? string.Empty,
                    ["maxTurns"] = config.MaxTurns
                }
            }, SessionTimeoutMs, cancellationToken).ConfigureAwait(false);

            sessionId = ReadSessionId(result);
            Report("lifecycle", $"EW-AI 会话已创建：{sessionId}", result);
        }

        public async Task EnsureSessionAsync(CancellationToken cancellationToken)
        {
            bool sessionLost = false;
            if (process == null || process.HasExited)
            {
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    sessionLost = true;
                }
                sessionId = null;
            }
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            if (sessionLost)
            {
                // Goose 进程在两轮对话之间退出（崩溃/超时），EnsureSession 会重建会话。
                // 新会话不携带之前的对话历史，必须提示用户，否则用户以为 AI 还记得上下文。
                Report("exit", "⚠️ Goose 进程已退出并重建会话，之前对话上下文已丢失。如果之前的对话涉及方案选择，请重新说明。", null);
            }

            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await NewSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<JObject> PromptAsync(string prompt, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new InvalidOperationException("提示词不能为空。");
            }

            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            string finalPrompt = BuildPrompt(prompt);
            lock (executionLock)
            {
                currentPromptId = Guid.NewGuid().ToString("N");
                assistantResponse.Clear();
            }
            LogExecution("user_prompt", prompt, null);
            try
            {
                JObject result = await SendRequestAsync("session/prompt", new JObject
                {
                    ["sessionId"] = sessionId,
                    ["prompt"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "text",
                            ["text"] = finalPrompt
                        }
                    }
                }, 0, cancellationToken).ConfigureAwait(false);

                LogExecution("prompt_completed", result["stopReason"]?.Value<string>() ?? "unknown", result);
                Report("lifecycle", $"EW-AI 本轮结束：{result["stopReason"]?.Value<string>() ?? "unknown"}", result);
                return result;
            }
            catch (Exception ex)
            {
                LogExecution("prompt_failed", ex.Message, null);
                throw;
            }
            finally
            {
                string response;
                lock (executionLock)
                {
                    response = assistantResponse.ToString();
                }
                LogExecution("assistant_response", response, null);
            }
        }

        /// <summary>
        /// 在当前 Goose 会话内重新挂载 Automation MCP，使 Goose 重新读取工具清单。
        /// 返回 false 表示当前尚未创建会话，后续新会话会直接使用最新 MCP 配置。
        /// </summary>
        public async Task<bool> ReloadAutomationExtensionAsync(string mcpUri, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(mcpUri))
            {
                throw new InvalidOperationException("MCP地址不能为空。");
            }

            string activeSessionId = sessionId;
            if (string.IsNullOrWhiteSpace(activeSessionId))
            {
                return false;
            }
            if (process == null || process.HasExited)
            {
                throw new InvalidOperationException("Goose进程已退出，无法在原会话内刷新工具。");
            }

            Exception removeError = null;
            try
            {
                await SendRequestAsync("_goose/unstable/session/extensions/remove", new JObject
                {
                    ["sessionId"] = activeSessionId,
                    ["name"] = "automation"
                }, SessionTimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 即使扩展此前不存在也继续尝试挂载；若 Goose 不支持会话扩展接口，add 同样会失败并统一报错。
                removeError = ex;
            }

            try
            {
                await SendRequestAsync("_goose/unstable/session/extensions/add", new JObject
                {
                    ["sessionId"] = activeSessionId,
                    ["extension"] = new JObject
                    {
                        ["type"] = "mcp",
                        ["server"] = new JObject
                        {
                            ["name"] = "automation",
                            ["type"] = "http",
                            ["url"] = mcpUri.Trim(),
                            ["headers"] = new JArray()
                        }
                    }
                }, SessionTimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception addError)
            {
                string detail = removeError == null
                    ? addError.Message
                    : $"卸载失败：{removeError.Message}；挂载失败：{addError.Message}";
                throw new InvalidOperationException(
                    "当前 Goose 版本不支持会话内工具热切换，或 Automation MCP 挂载失败。原对话未被重置。" + detail,
                    addError);
            }

            if (!string.Equals(sessionId, activeSessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("工具刷新期间 Goose 会话发生变化，已拒绝继续使用不确定状态。");
            }

            Report("lifecycle", $"Automation MCP 已在当前会话内重新挂载：{activeSessionId}", null);
            return true;
        }

        public void Cancel()
        {
            if (string.IsNullOrWhiteSpace(sessionId) || stdin == null)
            {
                return;
            }

            JObject notification = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "session/cancel",
                ["params"] = new JObject
                {
                    ["sessionId"] = sessionId
                }
            };
            WriteJsonRpc(notification);
            Report("lifecycle", "已向 Goose 发送取消请求。", notification);
        }

        private void EnsureProcessStarted()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GooseAcpClient));
            }
            if (process != null && !process.HasExited)
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = config.GooseExecutablePath,
                // ACP 默认不加载 builtin 扩展；显式启用 Goose 原生 Developer，
                // 提供文件读取、代码修改和终端执行能力。
                Arguments = "acp --with-builtin developer",
                WorkingDirectory = ResolveWorkingDirectory(),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            const string machineGitCommandPath = @"D:\AutomationTools\Git\cmd";
            if (!File.Exists(Path.Combine(machineGitCommandPath, "git.exe")))
            {
                throw new InvalidOperationException("未找到固定的 Git 运行环境：D:\\AutomationTools\\Git\\cmd\\git.exe");
            }
            startInfo.EnvironmentVariables["PATH"] = machineGitCommandPath + Path.PathSeparator
                + (startInfo.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(config.Provider))
            {
                startInfo.EnvironmentVariables["GOOSE_PROVIDER"] = config.Provider.Trim();
            }
            if (!string.IsNullOrWhiteSpace(config.Model))
            {
                startInfo.EnvironmentVariables["GOOSE_MODEL"] = config.Model.Trim();
            }
            if (!string.IsNullOrWhiteSpace(config.Provider))
            {
                if (!AiProviderSecretStorage.TryGetEnvironmentVariableName(config.Provider, out string secretVariable))
                {
                    throw new InvalidOperationException("当前 Provider 未配置严格的 API Key 环境变量映射：" + config.Provider);
                }
                if (!string.IsNullOrWhiteSpace(secretVariable))
                {
                    if (!AiProviderSecretStorage.TryGetSecret(config.Provider, out string secret, out string secretError))
                    {
                        throw new InvalidOperationException(secretError);
                    }
                    startInfo.EnvironmentVariables[secretVariable] = secret;
                }
            }

            process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.Exited += Process_Exited;
            if (!process.Start())
            {
                LogFile($"ACP 进程启动失败：exe={config.GooseExecutablePath}", LogLevel.Error);
                throw new InvalidOperationException("EW-AI ACP 进程启动失败。");
            }

            // .NET Framework 的 ProcessStartInfo 不支持 StandardInputEncoding，
            // process.StandardInput 默认用系统代码页（中文 Windows 为 GBK）。
            // ACP JSON-RPC over stdio 要求 UTF-8，故基于 BaseStream 自建 UTF-8 StreamWriter，
            // 不带 BOM；否则中文提示词写入后 Goose 按 UTF-8 读取会报
            // "stream did not contain valid UTF-8" 并崩溃退出。
            stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
            stdin.AutoFlush = true;
            Task.Run(() => ReadStdoutLoop(process.StandardOutput));
            Task.Run(() => ReadStderrLoop(process.StandardError));
            StringBuilder startupInfo = new StringBuilder();
            startupInfo.Append("ACP 进程启动 exe=").Append(config.GooseExecutablePath);
            startupInfo.Append(" cwd=").Append(ResolveWorkingDirectory());
            startupInfo.Append(" mcpUri=").Append(config.McpUri);
            startupInfo.Append(" sessionName=").Append(config.SessionName);
            if (!string.IsNullOrWhiteSpace(config.Provider))
            {
                startupInfo.Append(" provider=").Append(config.Provider);
            }
            if (!string.IsNullOrWhiteSpace(config.Model))
            {
                startupInfo.Append(" model=").Append(config.Model);
            }
            startupInfo.Append(" maxTurns=").Append(config.MaxTurns);
            LogFile(startupInfo.ToString(), LogLevel.Normal);
            Report("lifecycle", $"EW-AI ACP 进程已启动：{config.GooseExecutablePath} acp --with-builtin developer", null);
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            string message = "EW-AI ACP 进程已退出。";
            try
            {
                message = $"EW-AI ACP 进程已退出，退出码 {process?.ExitCode ?? -1}。";
            }
            catch
            {
            }
            LogFile(message, LogLevel.Error);
            Report("exit", message, null);
            sessionId = null;
            foreach (var item in pendingRequests)
            {
                item.Value.TrySetException(new InvalidOperationException(message));
            }
            pendingRequests.Clear();
        }

        private async Task<JObject> SendRequestAsync(string method, JObject parameters, int timeoutMs, CancellationToken cancellationToken)
        {
            EnsureProcessStarted();
            string id = Interlocked.Increment(ref nextRequestId).ToString();
            var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pendingRequests.TryAdd(id, tcs))
            {
                throw new InvalidOperationException($"ACP 请求 ID 冲突：{id}");
            }

            JObject request = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters ?? new JObject()
            };

            try
            {
                WriteJsonRpc(request);
            }
            catch (Exception ex)
            {
                LogFile($"ACP 写入失败 id={id} method={method} err={ex.Message}", LogLevel.Error);
                pendingRequests.TryRemove(id, out _);
                throw;
            }
            if (!string.Equals(method, "session/prompt", StringComparison.Ordinal))
            {
                LogFile($"ACP-> 请求 id={id} method={method}", parameters, LogLevel.Normal);
            }
            Report("request", $"{method} 请求已发送。", request);

            Task delayTask = timeoutMs > 0
                ? Task.Delay(timeoutMs, cancellationToken)
                : Task.Delay(Timeout.Infinite, cancellationToken);
            Task completed = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);
            if (completed == tcs.Task)
            {
                return await tcs.Task.ConfigureAwait(false);
            }

            pendingRequests.TryRemove(id, out _);
            if (cancellationToken.IsCancellationRequested)
            {
                LogFile($"ACP 请求取消 id={id} method={method}", LogLevel.Normal);
                throw new OperationCanceledException(cancellationToken);
            }
            LogFile($"ACP 请求超时 id={id} method={method} timeoutMs={timeoutMs}", LogLevel.Error);
            throw new TimeoutException($"EW-AI ACP 请求超时：{method}");
        }

        private void WriteJsonRpc(JObject message)
        {
            string text = message.ToString(Formatting.None);
            lock (writeLock)
            {
                if (stdin == null)
                {
                    throw new InvalidOperationException("EW-AI ACP stdin 未初始化。");
                }
                stdin.WriteLine(text);
                stdin.Flush();
            }
        }

        private async Task ReadStdoutLoop(StreamReader reader)
        {
            while (!disposed)
            {
                string line;
                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogFile($"ACP 读取 stdout 失败 err={ex.Message}", LogLevel.Error);
                    Report("error", $"读取 EW-AI ACP 输出失败：{ex.Message}", null);
                    return;
                }

                if (line == null)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                HandleJsonRpcLine(line);
            }
        }

        private async Task ReadStderrLoop(StreamReader reader)
        {
            while (!disposed)
            {
                string line;
                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogFile($"ACP 读取 stderr 失败 err={ex.Message}", LogLevel.Error);
                    return;
                }

                if (line == null)
                {
                    return;
                }
                if (!string.IsNullOrWhiteSpace(line))
                {
                    LogFile($"ACP stderr: {line}", LogLevel.Normal);
                    Report("stderr", line, null);
                }
            }
        }

        private void HandleJsonRpcLine(string line)
        {
            JObject message;
            try
            {
                message = JObject.Parse(line);
            }
            catch (Exception ex)
            {
                LogFile($"ACP stdout 非 JSON err={ex.Message} line={line}", LogLevel.Error);
                Report("error", $"EW-AI ACP 输出不是合法 JSON：{ex.Message}", null);
                return;
            }

            JToken idToken = message["id"];
            string method = message["method"]?.Value<string>();
            if (idToken != null && idToken.Type != JTokenType.Null && string.IsNullOrWhiteSpace(method))
            {
                HandleResponse(idToken.ToString(), message);
                return;
            }

            if (idToken != null && idToken.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(method))
            {
                HandleServerRequest(idToken.ToString(), method, message);
                return;
            }

            HandleNotification(method, message);
        }

        private void HandleResponse(string id, JObject message)
        {
            if (!pendingRequests.TryRemove(id, out TaskCompletionSource<JObject> tcs))
            {
                LogFile($"ACP 收到未知响应 id={id}", message, LogLevel.Normal);
                Report("response", $"收到未知 ACP 响应：{id}", message);
                return;
            }

            if (message["error"] is JObject error)
            {
                string errorMessage = error["message"]?.Value<string>() ?? "EW-AI ACP 返回错误。";
                // 排查 invalid params 等错误的关键入口：完整记录 error 对象（含 code/data）。
                LogFile($"ACP<- 错误响应 id={id} message={errorMessage}", error, LogLevel.Error);
                tcs.TrySetException(new InvalidOperationException(errorMessage));
                Report("error", errorMessage, message);
                return;
            }

            JObject result = message["result"] as JObject ?? new JObject();
            LogFile($"ACP<- 响应 id={id}", result, LogLevel.Normal);
            tcs.TrySetResult(result);
            Report("response", $"ACP 响应完成：{id}", message);
        }

        private void HandleServerRequest(string id, string method, JObject message)
        {
            LogFile($"ACP<- 服务端请求 id={id} method={method}", message["params"], LogLevel.Normal);
            Report("request", $"EW-AI 请求 Automation 处理：{method}", message);
            JObject result = null;
            if (string.Equals(method, "session/request_permission", StringComparison.Ordinal))
            {
                result = HandlePermissionRequest(message["params"] as JObject ?? new JObject());
            }

            if (result == null)
            {
                LogFile($"ACP-> 拒绝服务端请求 id={id} method={method}（未开放）", LogLevel.Error);
                JObject response = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new JObject
                    {
                        ["code"] = -32601,
                        ["message"] = $"Automation 未开放 ACP 客户端方法：{method}"
                    }
                };
                WriteJsonRpc(response);
                return;
            }

            LogFile($"ACP-> 服务端请求响应 id={id} method={method}", result, LogLevel.Normal);
            WriteJsonRpc(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["result"] = result
            });
        }

        private JObject HandlePermissionRequest(JObject request)
        {
            try
            {
                if (PermissionRequestHandler != null)
                {
                    JObject result = PermissionRequestHandler(request);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                LogFile($"ACP 权限请求处理失败 err={ex.Message}", request, LogLevel.Error);
                Report("error", $"权限请求处理失败：{ex.Message}", request);
            }

            return new JObject
            {
                ["outcome"] = new JObject
                {
                    ["outcome"] = "cancelled"
                }
            };
        }

        // 高频低价值的 session/update 类型：不落盘也不转发 UI，避免刷屏。
        // 注意：agent_message_chunk 不在此列，它是 AI 的流式回复文本，必须转发 UI。
        private static readonly HashSet<string> noisyUpdateKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "usage_update",
            "available_commands_update",
            "session_info_update"
        };

        private void HandleNotification(string method, JObject message)
        {
            if (string.Equals(method, "session/update", StringComparison.Ordinal))
            {
                JObject parameters = message["params"] as JObject;
                string updateKind = FindFirstString(parameters, "sessionUpdate", "type", "kind");
                // token 计数 / 命令列表等高频低价值通知不落盘也不转发 UI，避免刷屏。
                if (!string.IsNullOrEmpty(updateKind) && noisyUpdateKinds.Contains(updateKind))
                {
                    return;
                }

                // agent_message_chunk：AI 流式回复文本，不落盘（避免 token 刷屏），但转发 UI 打字机显示。
                if (string.Equals(updateKind, "agent_message_chunk", StringComparison.Ordinal))
                {
                    string chunkText = ExtractText(parameters);
                    if (!string.IsNullOrWhiteSpace(chunkText))
                    {
                        lock (executionLock)
                        {
                            assistantResponse.Append(chunkText);
                        }
                        Report("assistant_chunk", chunkText, message);
                    }
                    return;
                }

                // agent_thought_chunk 是 ACP 明确区分出的推理文本；与正式 assistant 消息分开转发。
                if (string.Equals(updateKind, "agent_thought_chunk", StringComparison.Ordinal))
                {
                    string thoughtText = ExtractText(parameters);
                    if (!string.IsNullOrWhiteSpace(thoughtText))
                    {
                        Report("assistant_thought", thoughtText, message);
                    }
                    return;
                }

                // tool_call：工具调用发起，显示中文工具名；完整 rawInput 进日志。
                if (string.Equals(updateKind, "tool_call", StringComparison.Ordinal))
                {
                    string title = FindFirstString(parameters, "title", "name") ?? "调用工具";
                    string displayName = ResolveToolDisplayName(parameters, title);
                    Report("tool_call", displayName, message);
                    return;
                }

                // tool_call_update：细分进度描述与完成响应。
                if (string.Equals(updateKind, "tool_call_update", StringComparison.Ordinal))
                {
                    string status = FindFirstString(parameters, "status");
                    // 进度描述（非完成）：仅落盘，不转发 UI。
                    if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        LogFile("ACP<- 通知 session/update kind=tool_call_update (progress)", parameters, LogLevel.Normal);
                        return;
                    }
                    // 完成响应只提取摘要给 UI；完整参数和结果由 MCP 统一审计，避免重复落盘。
                    string summary = ExtractToolResultSummary(parameters);
                    Report("tool_result", summary, message);
                    return;
                }

                string text = ExtractText(parameters);
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = string.IsNullOrWhiteSpace(updateKind) ? "收到 session/update。" : $"收到 session/update：{updateKind}";
                }
                LogFile($"ACP<- 通知 session/update kind={updateKind ?? "(空)"}", parameters, LogLevel.Normal);
                Report(NormalizeUpdateKind(updateKind), text, message);
                return;
            }

            LogFile($"ACP<- 通知 method={method ?? "(空)"}", message["params"], LogLevel.Normal);
            Report("notification", string.IsNullOrWhiteSpace(method) ? "收到 ACP 通知。" : $"收到 ACP 通知：{method}", message);
        }

        // 工具名（toolName）→ 中文显示名映射，让对话区显示中文而非英文工具标题。
        private static readonly Dictionary<string, string> toolDisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"automation__list_procs", "列出所有流程"},
            {"automation__get_proc_overview", "获取流程概览"},
            {"automation__get_proc_detail", "获取流程详情"},
            {"automation__list_operation_types", "列出指令类型"},
            {"automation__get_operation_schema", "获取指令Schema"},
            {"automation__get_operation_guide", "获取指令调用说明"},
            {"automation__get_reference_catalog", "获取引用目录"},
            {"automation__list_intent_templates", "列出意图模板"},
            {"automation__get_intent_template", "获取意图模板"},
            {"automation__build_patch_from_intent", "构建补丁"},
            {"automation__preview_intent", "预览意图"},
            {"automation__apply_intent", "提交意图"},
            {"automation__preview_patch", "预览补丁"},
            {"automation__apply_patch", "提交补丁"},
            {"automation__get_runtime_snapshot", "获取运行时快照"},
            {"automation__get_info_log_tail", "读取运行日志"},
            {"automation__diagnose_proc", "诊断流程"},
            {"automation__get_patch_contract", "获取调用约束"},
            {"automation__create_proc", "创建流程"},
            {"automation__apply_create_proc", "提交流程创建"},
            {"automation__delete_procs", "批量删除流程"},
            {"automation__apply_delete_procs", "提交流程删除"},
            {"automation__reorder_proc", "重排流程"},
            {"automation__apply_reorder_proc", "提交流程重排"},
            {"automation__copy_proc", "复制流程"},
            {"automation__apply_copy_proc", "提交流程复制"},
            {"automation__control_proc", "控制流程运行"}
        };

        // 工具返回 type → 中文摘要名映射。
        private static readonly Dictionary<string, string> resultTypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"proc.list", "流程列表"},
            {"proc.overview", "流程概览"},
            {"proc.detail", "流程详情"},
            {"proc.diagnose", "诊断结果"},
            {"runtime.snapshot", "运行时快照"},
            {"reference.catalog", "引用目录"},
            {"operation.types", "指令类型"},
            {"operation.schema", "指令Schema"},
            {"intent.catalog", "意图模板列表"},
            {"intent.template", "意图模板"},
            {"intent.patch", "意图补丁"},
            {"intent.preview", "意图预演"},
            {"intent.apply", "意图提交"},
            {"preview.confirm", "预演确认"},
            {"patch.preview", "补丁预演"},
            {"patch.apply", "补丁提交"},
            {"proc.manage.preview", "流程结构预演"},
            {"proc.manage.apply", "流程结构提交"},
            {"proc.control", "流程控制"}
        };

        // 优先用 toolName 映射中文显示名，无映射则回退到 title。
        private static string ResolveToolDisplayName(JObject parameters, string fallbackTitle)
        {
            JToken update = parameters["update"] ?? parameters;
            string toolName = update["_meta"]?["goose"]?["toolCall"]?["toolName"]?.Value<string>();
            if (!string.IsNullOrEmpty(toolName) && toolDisplayNames.TryGetValue(toolName, out string cn))
            {
                return cn;
            }
            return fallbackTitle;
        }

        // 从 tool_call_update 完成响应提取摘要，避免在 UI 显示完整 JSON。
        private static string ExtractToolResultSummary(JObject parameters)
        {
            JToken update = parameters["update"] ?? parameters;
            JToken content = update["content"];
            string raw = null;
            if (content is JArray arr && arr.Count > 0)
            {
                JToken first = arr[0];
                JToken textToken = first["text"];
                if (textToken == null && first["content"] != null)
                {
                    textToken = first["content"]["text"];
                }
                if (textToken != null && textToken.Type == JTokenType.String)
                {
                    raw = textToken.Value<string>();
                }
            }
            return SummarizeToolResultText(raw);
        }

        // 尝试从工具返回 JSON 提取类型与数量摘要，失败则截断。
        private static string SummarizeToolResultText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "✓ 工具返回结果";
            }
            try
            {
                JObject obj = JObject.Parse(raw);
                string type = obj["type"]?.Value<string>();
                // type 中文化
                if (!string.IsNullOrEmpty(type) && resultTypeNames.TryGetValue(type, out string cnType))
                {
                    type = cnType;
                }
                JToken data = obj["data"];
                if (!string.IsNullOrEmpty(type) && data is JObject dataObj)
                {
                    JToken items = dataObj["items"];
                    if (items is JArray itemArr)
                    {
                        return $"✓ {type}（{itemArr.Count} 项）";
                    }
                    JToken procName = dataObj["procName"];
                    if (procName != null && procName.Type == JTokenType.String)
                    {
                        return $"✓ {type}（{procName.Value<string>()}）";
                    }
                    JToken findings = dataObj["findings"];
                    if (findings is JArray findArr)
                    {
                        return $"✓ {type}（{findArr.Count} 条诊断）";
                    }
                    return $"✓ {type}";
                }
                return raw.Length > 80 ? raw.Substring(0, 80) + " …" : raw;
            }
            catch
            {
                return raw.Length > 80 ? raw.Substring(0, 80) + " …" : raw;
            }
        }

        private static string NormalizeUpdateKind(string updateKind)
        {
            if (string.IsNullOrWhiteSpace(updateKind))
            {
                return "update";
            }
            // 流式 token 片段单独标记，UI 在同一行追加（打字机效果），避免每个 chunk 占一行。
            if (string.Equals(updateKind, "agent_message_chunk", StringComparison.OrdinalIgnoreCase))
            {
                return "assistant_chunk";
            }
            if (updateKind.IndexOf("agent_message", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "assistant";
            }
            if (updateKind.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "tool";
            }
            if (updateKind.IndexOf("plan", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "plan";
            }
            return "update";
        }

        private string BuildPrompt(string prompt)
        {
            string context = BuildSelectionContext();
            return context + "\n\n用户请求：\n" + prompt.Trim();
        }

        /// <summary>
        /// 构建当前用户选中流程/步骤的背景信息，附加到 prompt 中，
        /// 让 AI 知道用户正在关注哪个流程，避免反复询问或定位偏差。
        /// </summary>
        private static string BuildSelectionContext()
        {
            try
            {
                if (SF.frmProc == null || SF.frmProc.IsDisposed)
                {
                    return string.Empty;
                }
                int procIndex = SF.frmProc.SelectedProcNum;
                if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
                {
                    return "\n\n当前用户未选中任何流程。";
                }

                Proc proc = SF.frmProc.procsList[procIndex];
                string procName = proc.head?.Name ?? "(未命名)";
                int stepCount = proc.steps?.Count ?? 0;

                StringBuilder sb = new StringBuilder();
                sb.Append("\n\n当前用户选中的流程背景信息（仅供参考定位，用户可能只是浏览该流程，不一定是要改动它；用户未明确指定时不要假设目标流程）：\n");
                sb.Append($"- procIndex={procIndex}，流程名称=\"{procName}\"，共 {stepCount} 个步骤\n");

                int stepIndex = SF.frmProc.SelectedStepNum;
                if (stepIndex >= 0 && stepIndex < stepCount)
                {
                    Step step = proc.steps[stepIndex];
                    string stepName = step?.Name ?? "(未命名)";
                    int opCount = step?.Ops?.Count ?? 0;
                    sb.Append($"- 选中步骤索引={stepIndex}，步骤名称=\"{stepName}\"，共 {opCount} 条指令\n");
                }
                sb.Append("注意：用户口语中的\"N号流程\"即 procIndex=N。选中状态仅表示用户正在浏览该流程，不等于用户要求改动它；实际改动目标以用户明确指定的为准。");
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ReadSessionId(JObject result)
        {
            string value = result["sessionId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("EW-AI ACP 未返回 sessionId。");
            }
            return value;
        }

        // Goose 进程工作目录始终跟随当前程序所在目录（AppDomain.CurrentDomain.BaseDirectory），
        // 不使用 GooseConfig.json 里持久化的 WorkingDirectory 值。
        // 原因：程序部署位置不固定，持久化的旧路径会导致 Goose 从错误目录启动，
        // 向上遍历时可能发现其他项目的 AGENTS.md，造成 AI 上下文污染。
        private string ResolveWorkingDirectory()
        {
            string workingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!Directory.Exists(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
            }
            return workingDirectory;
        }

        private static string ExtractText(JToken token)
        {
            if (token == null)
            {
                return string.Empty;
            }

            string directText = FindFirstString(token, "text");
            if (!string.IsNullOrWhiteSpace(directText))
            {
                return directText;
            }

            string title = FindFirstString(token, "title", "name", "status", "message");
            return title ?? string.Empty;
        }

        private static string FindFirstString(JToken token, params string[] names)
        {
            if (token == null || names == null || names.Length == 0)
            {
                return null;
            }

            if (token is JObject obj)
            {
                foreach (string name in names)
                {
                    if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken value)
                        && value != null
                        && value.Type == JTokenType.String)
                    {
                        return value.Value<string>();
                    }
                }

                foreach (JProperty property in obj.Properties())
                {
                    string nested = FindFirstString(property.Value, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    string nested = FindFirstString(item, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private void Report(string kind, string text, JObject raw)
        {
            try
            {
                EventReceived?.Invoke(new GooseAcpEvent
                {
                    Time = DateTime.Now,
                    Kind = kind ?? string.Empty,
                    Text = text ?? string.Empty,
                    Raw = raw == null ? null : (JObject)raw.DeepClone()
                });
            }
            catch
            {
            }
        }

        private static void LogFile(string message, LogLevel level)
        {
            WriteExecutionRecord(new JObject
            {
                ["time"] = DateTime.Now.ToString("O"),
                ["source"] = "acp",
                ["kind"] = level == LogLevel.Error ? "diagnostic_error" : "diagnostic",
                ["text"] = message ?? string.Empty
            });
        }

        private static void LogFile(string message, JToken json, LogLevel level)
        {
            try
            {
                JToken safeRaw = json?.DeepClone();
                RedactSensitiveValues(safeRaw);
                var record = new JObject
                {
                    ["time"] = DateTime.Now.ToString("O"),
                    ["source"] = "acp",
                    ["kind"] = level == LogLevel.Error ? "diagnostic_error" : "diagnostic",
                    ["text"] = message ?? string.Empty
                };
                if (safeRaw != null)
                {
                    record["raw"] = safeRaw;
                }
                WriteExecutionRecord(record);
            }
            catch
            {
            }
        }

        private void LogExecution(string kind, string text, JToken raw)
        {
            try
            {
                string promptId;
                lock (executionLock)
                {
                    promptId = currentPromptId ?? string.Empty;
                }
                var record = new JObject
                {
                    ["time"] = DateTime.Now.ToString("O"),
                    ["auditSessionId"] = auditSessionId,
                    ["gooseSessionId"] = sessionId ?? string.Empty,
                    ["promptId"] = promptId,
                    ["kind"] = kind ?? string.Empty,
                    ["text"] = text ?? string.Empty
                };
                if (raw != null)
                {
                    JToken safeRaw = raw.DeepClone();
                    RedactSensitiveValues(safeRaw);
                    record["raw"] = safeRaw;
                }
                record["source"] = "assistant";
                WriteExecutionRecord(record);
            }
            catch
            {
            }
        }

        private static void RedactSensitiveValues(JToken token)
        {
            if (!(token is JContainer container))
            {
                return;
            }
            foreach (JToken child in container.Children().ToList())
            {
                if (child is JProperty property)
                {
                    string name = property.Name ?? string.Empty;
                    if (name.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("apiKey", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(name, "headers", StringComparison.OrdinalIgnoreCase))
                    {
                        property.Value = "***";
                        continue;
                    }
                }
                RedactSensitiveValues(child);
            }
        }

        private static void WriteExecutionRecord(JObject record)
        {
            bool lockTaken = false;
            try
            {
                Directory.CreateDirectory(executionLogRoot);
                lockTaken = executionLogMutex.WaitOne(TimeSpan.FromSeconds(2));
                if (!lockTaken)
                {
                    return;
                }
                string path = Path.Combine(executionLogRoot, DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl");
                File.AppendAllText(path, record.ToString(Formatting.None) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }
            catch
            {
            }
            finally
            {
                if (lockTaken)
                {
                    try
                    {
                        executionLogMutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void Dispose()
        {
            disposed = true;
            LogFile("ACP Dispose 开始", LogLevel.Normal);
            try
            {
                Cancel();
            }
            catch
            {
            }

            foreach (var item in pendingRequests)
            {
                item.Value.TrySetCanceled();
            }
            pendingRequests.Clear();

            try
            {
                stdin?.Dispose();
            }
            catch
            {
            }

            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
            }

            try
            {
                process?.Dispose();
            }
            catch
            {
            }

            stdin = null;
            process = null;
            sessionId = null;
        }
    }
}
