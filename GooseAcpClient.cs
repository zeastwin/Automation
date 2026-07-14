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

    public sealed class GooseFileAttachment
    {
        public GooseFileAttachment(
            string id,
            string fileName,
            string mimeType,
            string typeLabel,
            bool isImage,
            byte[] data,
            string extractedText,
            string error)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            MimeType = mimeType ?? throw new ArgumentNullException(nameof(mimeType));
            TypeLabel = typeLabel ?? throw new ArgumentNullException(nameof(typeLabel));
            IsImage = isImage;
            Data = data ?? throw new ArgumentNullException(nameof(data));
            ExtractedText = extractedText;
            Error = error;
        }

        public string Id { get; }

        public string FileName { get; }

        public string MimeType { get; }

        public string TypeLabel { get; }

        public bool IsImage { get; }

        public byte[] Data { get; }

        public string ExtractedText { get; }

        public string Error { get; }
    }

    public sealed class GooseAcpClient : IDisposable
    {
        private const int InitializeTimeoutMs = 30000;
        private const int SessionTimeoutMs = 30000;
        private const long MaxLogFileBytes = 5L * 1024L * 1024L;

        private static readonly string executionLogRoot = Path.Combine(@"D:\AutomationLogs", "AIExecution");
        private static readonly Mutex executionLogMutex = new Mutex(false, "AutomationAIExecutionAuditLog");

        private readonly GooseConfig config;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> pendingRequests =
            new ConcurrentDictionary<string, TaskCompletionSource<JObject>>(StringComparer.Ordinal);
        private readonly object writeLock = new object();
        private readonly object executionLock = new object();
        private readonly string auditSessionId = Guid.NewGuid().ToString("N");
        // 每个 ACP 进程使用独立会话名，避免 Goose 恢复旧会话历史污染新的用户请求。
        private readonly string runtimeSessionName = "automation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        private readonly StringBuilder assistantResponse = new StringBuilder();
        private readonly StringBuilder assistantThought = new StringBuilder();
        private readonly StringBuilder currentAssistantTraceSegment = new StringBuilder();
        private readonly StringBuilder currentThoughtTraceSegment = new StringBuilder();
        private readonly JArray reasoningTrace = new JArray();
        private string restoredConversationContext;
        private int nextRequestId;
        private Process process;
        private StreamWriter stdin;
        private string sessionId;
        private string currentPromptId;
        private bool supportsImagePrompt;
        private bool disposed;

        public GooseAcpClient(GooseConfig config, string restoredConversationContext = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.restoredConversationContext = restoredConversationContext;
        }

        public event Action<GooseAcpEvent> EventReceived;

        public Func<JObject, JObject> PermissionRequestHandler { get; set; }

        public string SessionId => sessionId;

        public string LastAssistantResponse
        {
            get
            {
                lock (executionLock)
                {
                    return assistantResponse.ToString();
                }
            }
        }

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

            supportsImagePrompt = result["agentCapabilities"]?["promptCapabilities"]?["image"]?.Value<bool>() ?? false;
            Report("lifecycle", "EW-AI ACP 初始化完成。", result);
        }

        public async Task NewSessionAsync(CancellationToken cancellationToken)
        {
            EnsureProcessStarted();
            string sessionWorkingDirectory = ResolveWorkingDirectory();
            JObject sessionMeta = new JObject
            {
                ["sessionName"] = runtimeSessionName,
                ["maxTurns"] = config.MaxTurns
            };
            if (!string.IsNullOrWhiteSpace(config.Provider))
            {
                sessionMeta["provider"] = string.Equals(config.Provider.Trim(), "deepseek", StringComparison.OrdinalIgnoreCase)
                    ? "custom_deepseek"
                    : config.Provider.Trim();
            }
            if (!string.IsNullOrWhiteSpace(config.Model))
            {
                sessionMeta["model"] = config.Model.Trim();
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
                ["_meta"] = sessionMeta
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
                string message = "⚠️ Goose 进程已退出并重建会话，之前对话上下文已丢失。如果之前的对话涉及方案选择，请重新说明。";
                LogExecution("session_recreated", message, null);
                Report("exit", message, null);
            }

            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await NewSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<JObject> PromptAsync(
            string prompt,
            IReadOnlyList<GooseFileAttachment> fileAttachments,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt) && (fileAttachments == null || fileAttachments.Count == 0))
            {
                throw new InvalidOperationException("提示词不能为空。");
            }

            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            if (fileAttachments != null)
            {
                foreach (GooseFileAttachment file in fileAttachments)
                {
                    if (file == null || file.Data == null || file.Data.Length == 0
                        || string.IsNullOrWhiteSpace(file.MimeType)
                        || !string.IsNullOrWhiteSpace(file.Error))
                    {
                        throw new InvalidOperationException(file?.Error ?? "文件附件无效。");
                    }
                    if (file.IsImage)
                    {
                        if (IsKnownTextOnlyImageConfiguration(config.Provider, config.Model))
                        {
                            throw new InvalidOperationException(
                                $"当前模型 {config.Provider}/{config.Model} 只支持文本，不能分析图片。请移除图片或切换到支持视觉的模型。");
                        }
                        if (!supportsImagePrompt)
                        {
                            throw new InvalidOperationException("当前 Goose 未声明图片输入能力，请升级 Goose 或改用支持图片分析的模型。");
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(file.ExtractedText))
                    {
                        throw new InvalidOperationException($"文件 {file.FileName} 没有可分析的文本内容。");
                    }
                }
            }
            var finalPromptBuilder = new StringBuilder(BuildPrompt(prompt));
            if (fileAttachments != null)
            {
                foreach (GooseFileAttachment file in fileAttachments.Where(item => item != null && !item.IsImage))
                {
                    finalPromptBuilder.Append("\n\n===== 附件开始：")
                        .Append(file.FileName)
                        .Append("（")
                        .Append(file.TypeLabel)
                        .AppendLine("） =====");
                    finalPromptBuilder.AppendLine(file.ExtractedText);
                    finalPromptBuilder.Append("===== 附件结束：").Append(file.FileName).Append(" =====");
                }
            }
            string finalPrompt = finalPromptBuilder.ToString();
            var promptContent = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = finalPrompt
                }
            };
            if (fileAttachments != null)
            {
                foreach (GooseFileAttachment file in fileAttachments.Where(item => item != null && item.IsImage))
                {
                    promptContent.Add(new JObject
                    {
                        ["type"] = "image",
                        ["mimeType"] = file.MimeType,
                        ["data"] = Convert.ToBase64String(file.Data)
                    });
                }
            }
            lock (executionLock)
            {
                currentPromptId = Guid.NewGuid().ToString("N");
                assistantResponse.Clear();
                assistantThought.Clear();
                currentAssistantTraceSegment.Clear();
                currentThoughtTraceSegment.Clear();
                reasoningTrace.Clear();
            }
            LogExecution("user_prompt", prompt, new JObject
            {
                ["effectivePrompt"] = finalPrompt
            });
            try
            {
                JObject result = await SendRequestAsync("session/prompt", new JObject
                {
                    ["sessionId"] = sessionId,
                    ["prompt"] = promptContent
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
                JArray trace;
                lock (executionLock)
                {
                    FlushReasoningTraceSegmentLocked("assistant_segment", currentAssistantTraceSegment);
                    FlushReasoningTraceSegmentLocked("thought_segment", currentThoughtTraceSegment);
                    MarkFinalAssistantTraceSegmentLocked();
                    response = assistantResponse.ToString();
                    trace = (JArray)reasoningTrace.DeepClone();
                }
                LogExecution("assistant_response", response, null);
                if (trace.Count > 0)
                {
                    LogExecution("reasoning_trace", $"本轮共记录 {trace.Count} 个推理与工具事件。", new JObject
                    {
                        ["events"] = trace
                    });
                }

                string thought;
                lock (executionLock)
                {
                    thought = assistantThought.ToString();
                }
                if (!string.IsNullOrWhiteSpace(thought))
                {
                    LogExecution("assistant_thought", thought, null);
                }
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
            // Goose 会把 Developer Shell 输出严格按 UTF-8 解码。统一通过随程序发布的
            // UTF-8 适配器启动 PowerShell，避免系统代码页把中文不可逆地解码成乱码。
            string developerShellPath = ResolveGooseDeveloperShellPath();
            if (!string.IsNullOrWhiteSpace(developerShellPath))
            {
                startInfo.EnvironmentVariables["GOOSE_SHELL"] = developerShellPath;
            }
            // Hmi 是客户可修改目录，不从其中加载平台内部规范。
            // Automation 专用上下文由程序内嵌资源部署到受管目录，仅注入当前 EW-AI 进程。
            startInfo.EnvironmentVariables["CONTEXT_FILE_NAMES"] = "[]";
            if (!File.Exists(GooseRuntimeProvisioner.IntegrationContextPath))
            {
                throw new FileNotFoundException("Automation 专用 Goose 上下文不存在。",
                    GooseRuntimeProvisioner.IntegrationContextPath);
            }
            startInfo.EnvironmentVariables["GOOSE_MOIM_MESSAGE_FILE"] = GooseRuntimeProvisioner.IntegrationContextPath;

            string configuredProvider = config.Provider?.Trim();
            bool useDeepSeekProvider = string.Equals(configuredProvider, "deepseek", StringComparison.OrdinalIgnoreCase);
            string effectiveProvider = useDeepSeekProvider ? "custom_deepseek" : configuredProvider;
            if (useDeepSeekProvider)
            {
                GooseConfigStorage.RemoveManagedDeepSeekGooseConfiguration();
            }
            if (!string.IsNullOrWhiteSpace(configuredProvider))
            {
                startInfo.EnvironmentVariables["GOOSE_PROVIDER"] = effectiveProvider;
            }
            if (!string.IsNullOrWhiteSpace(config.Model))
            {
                startInfo.EnvironmentVariables["GOOSE_MODEL"] = config.Model.Trim();
            }
            startInfo.EnvironmentVariables["GOOSE_MAX_TOKENS"] =
                config.MaxOutputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            startupInfo.Append(" sessionName=").Append(runtimeSessionName);
            startupInfo.Append(" developerShell=").Append(developerShellPath ?? "cmd");
            if (!string.IsNullOrWhiteSpace(config.Provider))
            {
                startupInfo.Append(" provider=").Append(effectiveProvider);
            }
            if (!string.IsNullOrWhiteSpace(config.Model))
            {
                startupInfo.Append(" model=").Append(config.Model);
            }
            startupInfo.Append(" maxTurns=").Append(config.MaxTurns);
            startupInfo.Append(" maxOutputTokens=").Append(config.MaxOutputTokens);
            LogFile(startupInfo.ToString(), LogLevel.Normal);
            Report("lifecycle", $"EW-AI ACP 进程已启动：{config.GooseExecutablePath} acp --with-builtin developer", null);
        }

        private static string ResolveGooseDeveloperShellPath()
        {
            string adapterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "GooseShell", "pwsh.exe");
            if (!File.Exists(adapterPath))
            {
                throw new FileNotFoundException("EW-AI UTF-8 Shell 适配器不存在。", adapterPath);
            }
            return adapterPath;
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
            LogFile($"ACP-> 请求 id={id} method={method}", parameters, LogLevel.Normal);
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
                string errorData = error["data"]?.Type == JTokenType.String
                    ? error["data"].Value<string>()
                    : error["data"]?.ToString(Formatting.None);
                string detailedMessage = string.IsNullOrWhiteSpace(errorData)
                    ? errorMessage
                    : errorMessage + "：" + errorData;
                // 排查 invalid params 等错误的关键入口：完整记录 error 对象（含 code/data）。
                LogFile($"ACP<- 错误响应 id={id} message={detailedMessage}", error, LogLevel.Error);
                tcs.TrySetException(new InvalidOperationException(detailedMessage));
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
                            FlushReasoningTraceSegmentLocked("thought_segment", currentThoughtTraceSegment);
                            assistantResponse.Append(chunkText);
                            currentAssistantTraceSegment.Append(chunkText);
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
                        lock (executionLock)
                        {
                            FlushReasoningTraceSegmentLocked("assistant_segment", currentAssistantTraceSegment);
                            assistantThought.Append(thoughtText);
                            currentThoughtTraceSegment.Append(thoughtText);
                        }
                        Report("assistant_thought", thoughtText, message);
                    }
                    return;
                }

                // tool_call：工具调用发起，显示中文工具名；完整 rawInput 进日志。
                if (string.Equals(updateKind, "tool_call", StringComparison.Ordinal))
                {
                    string title = FindFirstString(parameters, "title", "name") ?? "调用工具";
                    string displayName = string.Equals(title, "error", StringComparison.OrdinalIgnoreCase)
                        ? "工具参数生成失败"
                        : ResolveToolDisplayName(parameters, title);
                    AppendReasoningTraceEvent("tool_call", displayName, message);
                    LogExecution("tool_call", displayName, message);
                    Report("tool_call", displayName, message);
                    return;
                }

                // tool_call_update：细分进度描述与完成响应。
                if (string.Equals(updateKind, "tool_call_update", StringComparison.Ordinal))
                {
                    string status = FindFirstString(parameters, "status");
                    if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        string detail = FindFirstString(parameters, "message", "error");
                        string failureSummary = string.IsNullOrWhiteSpace(detail)
                            ? "× 工具请求未形成有效调用，未到达 MCP"
                            : "× " + detail;
                        AppendReasoningTraceEvent("tool_error", failureSummary, message);
                        LogFile("ACP<- 工具调用失败", parameters, LogLevel.Error);
                        Report("tool_result", failureSummary, message);
                        return;
                    }
                    // 进度描述（非完成）：仅落盘，不转发 UI。
                    if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        LogFile("ACP<- 通知 session/update kind=tool_call_update (progress)", parameters, LogLevel.Normal);
                        return;
                    }
                    // 完成响应只提取摘要给 UI；完整参数和结果由 MCP 统一审计，避免重复落盘。
                    string summary = ExtractToolResultSummary(parameters);
                    AppendReasoningTraceEvent("tool_result", summary, message);
                    Report("tool_result", summary, message);
                    return;
                }

                string text = ExtractText(parameters);
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = string.IsNullOrWhiteSpace(updateKind) ? "收到 session/update。" : $"收到 session/update：{updateKind}";
                }
                if (string.Equals(updateKind, "agent_message", StringComparison.Ordinal))
                {
                    lock (executionLock)
                    {
                        FlushReasoningTraceSegmentLocked("thought_segment", currentThoughtTraceSegment);
                        assistantResponse.Append(text);
                        currentAssistantTraceSegment.Append(text);
                    }
                }
                else if (string.Equals(updateKind, "agent_thought", StringComparison.Ordinal))
                {
                    lock (executionLock)
                    {
                        FlushReasoningTraceSegmentLocked("assistant_segment", currentAssistantTraceSegment);
                        assistantThought.Append(text);
                        currentThoughtTraceSegment.Append(text);
                    }
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
            {"automation__search_proc_catalog", "搜索流程目录"},
            {"automation__get_proc_overview", "获取流程概览"},
            {"automation__get_proc_detail", "获取流程详情"},
            {"automation__get_op_detail", "获取指令详情"},
            {"automation__get_op_details", "批量获取指令详情"},
            {"automation__get_step_detail", "获取步骤详情"},
            {"automation__get_operation_references", "获取指令跳转关系"},
            {"automation__get_proc_references", "获取流程引用"},
            {"automation__trace_resource", "追踪资源引用"},
            {"automation__search_ops", "搜索指令"},
            {"automation__list_operation_types", "列出指令类型"},
            {"automation__get_operation_schema", "获取指令Schema"},
            {"automation__get_operation_guide", "获取指令调用说明"},
            {"automation__op_meta", "获取指令元数据"},
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
            {"automation__create_proc_batch", "批量创建完整流程"},
            {"automation__apply_create_proc", "提交流程创建"},
            {"automation__delete_procs", "批量删除流程"},
            {"automation__apply_delete_procs", "提交流程删除"},
            {"automation__reorder_proc", "重排流程"},
            {"automation__apply_reorder_proc", "提交流程重排"},
            {"automation__copy_proc", "复制流程"},
            {"automation__apply_copy_proc", "提交流程复制"},
            {"automation__control_proc", "控制流程运行"},
            {"automation__get_snapshot", "获取平台快照"},
            {"automation__list_variables", "列出变量"},
            {"automation__search_variables", "搜索变量"},
            {"automation__list_io", "列出 IO"},
            {"automation__search_io", "搜索 IO"},
            {"automation__list_alarms", "列出报警"},
            {"automation__list_resources", "列出资源"}
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
            {"proc.control", "流程控制"},
            {"proc.create_batch", "完整流程变更集"},
            {"op.meta", "指令元数据"},
            {"io.list", "IO 列表"},
            {"variable.list", "变量列表"},
            {"resource.list", "资源列表"}
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

        public static bool IsKnownTextOnlyImageConfiguration(string provider, string model)
        {
            string normalizedProvider = (provider ?? string.Empty).Trim();
            string normalizedModel = (model ?? string.Empty).Trim();
            return string.Equals(normalizedProvider, "deepseek", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedProvider, "custom_deepseek", StringComparison.OrdinalIgnoreCase)
                || normalizedModel.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildPrompt(string prompt)
        {
            string context;
            if (string.Equals(config.ToolProfile, "Diagnostic", StringComparison.Ordinal))
            {
                context = "当前 Automation 工具模式：Diagnostic（只读诊断）。本会话未开放流程启动、停止、测试或配置变更工具；用户要求执行这些动作时，应明确回复“当前模式不允许运行或变更，请切换到编辑模式”，不得改用其他工具模拟。";
            }
            else
            {
                context = "当前 Automation 工具模式：Editor。只能使用本会话实际开放的工具执行流程控制和配置变更。";
            }
            context += BuildSelectionContext();
            string restoredContext = restoredConversationContext;
            restoredConversationContext = null;
            if (!string.IsNullOrWhiteSpace(restoredContext))
            {
                context += "\n\n以下是用户切回本会话时恢复的既有对话。它只属于当前会话，请延续其中的上下文：\n"
                    + restoredContext.Trim();
            }
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

        // Developer 工具只面向自动化项目的 Hmi 源码。优先定位包含 Automation.csproj
        // 的源码根目录；发布包若要开放代码修改，必须在程序目录携带 Hmi 目录。
        private string ResolveWorkingDirectory()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string projectFile = Path.Combine(directory.FullName, "Automation.csproj");
                string sourceDirectory = Path.Combine(directory.FullName, "Hmi");
                if (File.Exists(projectFile) && Directory.Exists(sourceDirectory))
                {
                    return sourceDirectory;
                }
                directory = directory.Parent;
            }

            string deployedHmiDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Hmi");
            if (!Directory.Exists(deployedHmiDirectory))
            {
                throw new DirectoryNotFoundException(
                    "未找到 EW-AI 可编辑的 Hmi 源码目录。开发环境需保留 Automation.csproj/Hmi，发布包需在程序目录携带 Hmi。平台内核目录不会开放给 EW-AI。");
            }
            return deployedHmiDirectory;
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

        private void AppendReasoningTraceEvent(string kind, string text, JObject raw)
        {
            lock (executionLock)
            {
                FlushReasoningTraceSegmentLocked("assistant_segment", currentAssistantTraceSegment);
                FlushReasoningTraceSegmentLocked("thought_segment", currentThoughtTraceSegment);
                var traceEvent = new JObject
                {
                    ["time"] = DateTime.Now.ToString("O"),
                    ["kind"] = kind ?? string.Empty,
                    ["text"] = text ?? string.Empty
                };
                if (raw != null)
                {
                    traceEvent["raw"] = raw.DeepClone();
                }
                reasoningTrace.Add(traceEvent);
            }
        }

        private void FlushReasoningTraceSegmentLocked(string kind, StringBuilder segment)
        {
            if (segment == null || segment.Length == 0)
            {
                return;
            }
            reasoningTrace.Add(new JObject
            {
                ["time"] = DateTime.Now.ToString("O"),
                ["kind"] = kind ?? string.Empty,
                ["text"] = segment.ToString()
            });
            segment.Clear();
        }

        private void MarkFinalAssistantTraceSegmentLocked()
        {
            JObject finalSegment = reasoningTrace
                .OfType<JObject>()
                .LastOrDefault(item => string.Equals(
                    item["kind"]?.Value<string>(),
                    "assistant_segment",
                    StringComparison.Ordinal));
            foreach (JObject item in reasoningTrace.OfType<JObject>())
            {
                if (string.Equals(item["kind"]?.Value<string>(), "assistant_segment", StringComparison.Ordinal))
                {
                    item["kind"] = ReferenceEquals(item, finalSegment) ? "final_answer" : "reasoning_segment";
                }
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
                    JObject parentObject = property.Parent as JObject;
                    JObject contentObject = parentObject;
                    if (contentObject?["type"] == null
                        && contentObject?.Parent is JProperty resourceProperty
                        && string.Equals(resourceProperty.Name, "resource", StringComparison.OrdinalIgnoreCase))
                    {
                        contentObject = resourceProperty.Parent as JObject;
                    }
                    string contentType = contentObject?["type"]?.Value<string>();
                    bool isAttachmentContent = string.Equals(contentType, "image", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(name, "data", StringComparison.OrdinalIgnoreCase);
                    bool isEmbeddedFileContent = string.Equals(contentType, "resource", StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(name, "blob", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, "text", StringComparison.OrdinalIgnoreCase));
                    if (isAttachmentContent || isEmbeddedFileContent)
                    {
                        int dataLength = property.Value?.Type == JTokenType.String
                            ? property.Value.Value<string>()?.Length ?? 0
                            : 0;
                        property.Value = isAttachmentContent
                            ? $"[图片数据已省略，Base64长度={dataLength}]"
                            : $"[文件内容已省略，长度={dataLength}]";
                        continue;
                    }
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
                lockTaken = executionLogMutex.WaitOne(TimeSpan.FromSeconds(2));
                if (!lockTaken)
                {
                    return;
                }
                Directory.CreateDirectory(executionLogRoot);

                StringBuilder builder = new StringBuilder();
                builder.AppendLine(new string('=', 100));
                builder.Append("时间：").AppendLine(record["time"]?.Value<string>() ?? DateTime.Now.ToString("O"));
                builder.Append("来源：").AppendLine(record["source"]?.Value<string>() ?? string.Empty);
                builder.Append("类型：").AppendLine(record["kind"]?.Value<string>() ?? string.Empty);
                AppendLogField(builder, "审计会话", record["auditSessionId"]);
                AppendLogField(builder, "Goose 会话", record["gooseSessionId"]);
                AppendLogField(builder, "Prompt ID", record["promptId"]);
                AppendLogField(builder, "调用 ID", record["callId"]);
                AppendLogField(builder, "工具", record["toolName"]);
                AppendLogField(builder, "耗时", record["durationMs"], "毫秒");
                builder.AppendLine("内容：");
                builder.AppendLine(record["text"]?.Value<string>() ?? string.Empty);
                AppendJsonSection(builder, "参数", record["args"]);
                AppendJsonSection(builder, "结果", record["result"]);
                AppendLogField(builder, "异常", record["error"]);
                AppendJsonSection(builder, "原始数据", record["raw"]);
                builder.AppendLine();

                string content = builder.ToString();
                string datePrefix = DateTime.Now.ToString("yyyy-MM-dd");
                int index = 0;
                string path;
                while (true)
                {
                    string suffix = index == 0 ? string.Empty : $"_{index:000}";
                    path = Path.Combine(executionLogRoot, datePrefix + suffix + ".log");
                    if (!File.Exists(path)
                        || new FileInfo(path).Length + Encoding.UTF8.GetByteCount(content) <= MaxLogFileBytes)
                    {
                        break;
                    }
                    index++;
                }
                using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
                {
                    writer.Write(content);
                }
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

        private static void AppendLogField(StringBuilder builder, string label, JToken value)
        {
            string text = value?.Value<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.Append(label).Append('：').AppendLine(text);
            }
        }

        private static void AppendLogField(StringBuilder builder, string label, JToken value, string suffix)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return;
            }

            builder.Append(label).Append('：').Append(value).AppendLine(suffix ?? string.Empty);
        }

        private static void AppendJsonSection(StringBuilder builder, string label, JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return;
            }

            builder.AppendLine(label + "：");
            if (value.Type == JTokenType.String)
            {
                string text = value.Value<string>() ?? string.Empty;
                try
                {
                    builder.AppendLine(JToken.Parse(text).ToString(Formatting.Indented));
                }
                catch
                {
                    builder.AppendLine(text);
                }
            }
            else
            {
                builder.AppendLine(value.ToString(Formatting.Indented));
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
