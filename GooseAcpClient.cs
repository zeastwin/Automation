using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private static readonly string structuredExecutionLogRoot = Path.Combine(executionLogRoot, "Structured");
        private static readonly Mutex executionLogMutex = new Mutex(false, "AutomationAIExecutionAuditLog");

        private readonly GooseConfig config;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> pendingRequests =
            new ConcurrentDictionary<string, TaskCompletionSource<JObject>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> parameterGenerationFailureCalls =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly object writeLock = new object();
        private readonly object executionLock = new object();
        private readonly string auditSessionId = Guid.NewGuid().ToString("N");
        // 每个 ACP 进程使用独立会话名，避免 Goose 恢复旧会话历史污染新的用户请求。
        private readonly string runtimeSessionName = "automation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        private readonly StringBuilder assistantResponse = new StringBuilder();
        private readonly StringBuilder currentAssistantTraceSegment = new StringBuilder();
        private readonly StringBuilder currentThoughtTraceSegment = new StringBuilder();
        private readonly Dictionary<string, AnalysisToolCallState> activeAnalysisToolCalls =
            new Dictionary<string, AnalysisToolCallState>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> analysisToolAttempts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<AnalysisTimeInterval> analysisToolIntervals =
            new List<AnalysisTimeInterval>();
        private string restoredConversationContext;
        private int nextRequestId;
        private Process process;
        private StreamWriter stdin;
        private string sessionId;
        private string gooseAgentName;
        private string gooseAgentVersion;
        private string currentPromptId;
        private DateTime currentPromptStartedUtc;
        private int currentPromptToolCallCount;
        private int currentPromptToolErrorCount;
        private int currentAnalysisSequence;
        private int currentParallelGroup;
        private int currentMaxConcurrentTools;
        private int currentParameterFailureCount;
        private long currentPreviewWaitMs;
        private DateTime lastAnalysisToolStartedUtc;
        private DateTime currentFirstModelActivityUtc;
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
            gooseAgentName = result["agentInfo"]?["name"]?.Value<string>() ?? string.Empty;
            gooseAgentVersion = result["agentInfo"]?["version"]?.Value<string>() ?? string.Empty;
            Report("lifecycle", "EW-AI ACP 初始化完成。", result);
        }

        public async Task NewSessionAsync(CancellationToken cancellationToken)
        {
            EnsureProcessStarted();
            string sessionWorkingDirectory = ResolveWorkingDirectory();
            AiModelServiceConfig modelService = GooseConfigStorage.FindModelService(config);
            string configuredProvider = modelService == null ? config.Provider?.Trim() : "openai";
            string configuredModel = modelService == null ? config.Model?.Trim() : modelService.Model?.Trim();
            JObject sessionMeta = new JObject
            {
                ["sessionName"] = runtimeSessionName,
                ["maxTurns"] = config.MaxTurns
            };
            if (!string.IsNullOrWhiteSpace(configuredProvider))
            {
                sessionMeta["provider"] = string.Equals(configuredProvider, "deepseek", StringComparison.OrdinalIgnoreCase)
                    ? "custom_deepseek"
                    : configuredProvider;
            }
            if (!string.IsNullOrWhiteSpace(configuredModel))
            {
                sessionMeta["model"] = configuredModel;
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
                        AiModelServiceConfig modelService = GooseConfigStorage.FindModelService(config);
                        bool textOnly = modelService != null
                            ? !modelService.SupportsVision
                            : IsKnownTextOnlyImageConfiguration(config.Provider, config.Model);
                        if (textOnly)
                        {
                            string modelLabel = modelService == null
                                ? config.Provider + "/" + config.Model
                                : modelService.Name + "/" + modelService.Model;
                            throw new InvalidOperationException(
                                $"当前模型 {modelLabel} 只支持文本，不能分析图片。请移除图片或切换到支持视觉的模型。");
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
                currentPromptStartedUtc = DateTime.UtcNow;
                currentPromptToolCallCount = 0;
                currentPromptToolErrorCount = 0;
                currentAnalysisSequence = 0;
                currentParallelGroup = 0;
                currentMaxConcurrentTools = 0;
                currentParameterFailureCount = 0;
                currentPreviewWaitMs = 0;
                lastAnalysisToolStartedUtc = default(DateTime);
                currentFirstModelActivityUtc = default(DateTime);
                activeAnalysisToolCalls.Clear();
                analysisToolAttempts.Clear();
                analysisToolIntervals.Clear();
                assistantResponse.Clear();
                currentAssistantTraceSegment.Clear();
                currentThoughtTraceSegment.Clear();
            }
            WriteAnalysisEvent("turn.started", new JObject
            {
                ["request"] = AiAnalysisLogger.SummarizePayload(new JValue(prompt), 8 * 1024),
                ["toolProfile"] = config.ToolProfile ?? string.Empty,
                ["provider"] = GooseConfigStorage.FindModelService(config) == null
                    ? config.Provider ?? string.Empty : "openai",
                ["model"] = GooseConfigStorage.FindModelService(config)?.Model
                    ?? config.Model ?? string.Empty,
                ["modelServiceId"] = config.ModelServiceId ?? string.Empty,
                ["goose"] = new JObject
                {
                    ["name"] = gooseAgentName ?? string.Empty,
                    ["version"] = gooseAgentVersion ?? string.Empty
                },
                ["attachmentCount"] = fileAttachments?.Count ?? 0,
                ["effectivePromptBytes"] = Encoding.UTF8.GetByteCount(finalPrompt),
                ["context"] = BuildManagedContextAnalysis(),
                ["selection"] = BuildSelectionAnalysis()
            });
            LogExecution("user_prompt", prompt, new JObject
            {
                ["effectivePromptBytes"] = Encoding.UTF8.GetByteCount(finalPrompt),
                ["attachmentCount"] = fileAttachments?.Count ?? 0
            });
            JObject promptResult = null;
            Exception promptException = null;
            try
            {
                promptResult = await SendRequestAsync("session/prompt", new JObject
                {
                    ["sessionId"] = sessionId,
                    ["prompt"] = promptContent
                }, 0, cancellationToken).ConfigureAwait(false);

                LogExecution("prompt_completed", promptResult["stopReason"]?.Value<string>() ?? "unknown", promptResult);
                Report("lifecycle", $"EW-AI 本轮结束：{promptResult["stopReason"]?.Value<string>() ?? "unknown"}", promptResult);
                return promptResult;
            }
            catch (Exception ex)
            {
                promptException = ex;
                LogExecution("prompt_failed", ex.Message, null);
                throw;
            }
            finally
            {
                string response;
                lock (executionLock)
                {
                    FlushReasoningTraceSegmentLocked("assistant_segment", currentAssistantTraceSegment, "final");
                    FlushReasoningTraceSegmentLocked("thought_segment", currentThoughtTraceSegment, "reasoning");
                    response = assistantResponse.ToString();
                }
                LogExecution("assistant_response", response, null);
                long totalDurationMs;
                int toolCallCount;
                int toolErrorCount;
                lock (executionLock)
                {
                    totalDurationMs = Math.Max(0L, (long)(DateTime.UtcNow - currentPromptStartedUtc).TotalMilliseconds);
                    toolCallCount = currentPromptToolCallCount;
                    toolErrorCount = currentPromptToolErrorCount;
                }
                LogExecution("turn.summary", "本轮执行摘要。", new JObject
                {
                    ["totalDurationMs"] = totalDurationMs,
                    ["toolCallCount"] = toolCallCount,
                    ["toolErrorCount"] = toolErrorCount
                });
                JObject turnFinished;
                lock (executionLock)
                {
                    turnFinished = BuildTurnFinishedAnalysisLocked(
                        promptResult,
                        promptException,
                        totalDurationMs,
                        response.Length);
                }
                WriteAnalysisEvent(promptException == null ? "turn.completed" : "turn.failed", turnFinished);
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
            if (!GooseRuntimeEnvironment.TryValidate(config.GooseExecutablePath, out string runtimeError))
            {
                throw new InvalidOperationException(runtimeError);
            }
            if (!GooseRuntimeProvisioner.IsManagedContextAvailable)
            {
                throw new InvalidOperationException("EW-AI 受管上下文未通过启动校验，当前会话不可用。");
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

            startInfo.EnvironmentVariables["PATH"] = GooseRuntimeEnvironment.MachineGitCommandPath + Path.PathSeparator
                + (startInfo.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
            using (Process hostProcess = Process.GetCurrentProcess())
            {
                startInfo.EnvironmentVariables["AUTOMATION_HOST_PROCESS_ID"] =
                    hostProcess.Id.ToString(CultureInfo.InvariantCulture);
                string hostExecutablePath;
                try
                {
                    hostExecutablePath = hostProcess.MainModule?.FileName;
                }
                catch
                {
                    hostExecutablePath = null;
                }
                startInfo.EnvironmentVariables["AUTOMATION_HOST_EXECUTABLE"] =
                    string.IsNullOrWhiteSpace(hostExecutablePath)
                        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Automation.exe")
                        : hostExecutablePath;
            }
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

            AiModelServiceConfig modelService = GooseConfigStorage.FindModelService(config);
            string configuredProvider = modelService == null ? config.Provider?.Trim() : "openai";
            string configuredModel = modelService == null ? config.Model?.Trim() : modelService.Model?.Trim();
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
            if (!string.IsNullOrWhiteSpace(configuredModel))
            {
                startInfo.EnvironmentVariables["GOOSE_MODEL"] = configuredModel;
            }
            startInfo.EnvironmentVariables["GOOSE_MAX_TOKENS"] =
                config.MaxOutputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
            startInfo.EnvironmentVariables["GOOSE_TEMPERATURE"] =
                config.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (modelService != null)
            {
                // 自定义服务只覆盖当前 EW-AI 子进程，避免污染 Goose 全局配置和其他应用。
                // OPENAI_HOST 的优先级高于 OPENAI_BASE_URL，必须移除父进程可能继承的旧值。
                startInfo.EnvironmentVariables.Remove("OPENAI_HOST");
                startInfo.EnvironmentVariables.Remove("OPENAI_BASE_PATH");
                startInfo.EnvironmentVariables["OPENAI_BASE_URL"] = modelService.BaseUrl.Trim();
                startInfo.EnvironmentVariables.Remove("OPENAI_API_KEY");
                string serviceSecretKey = AiProviderSecretStorage.GetModelServiceSecretKey(modelService.Id);
                if (AiProviderSecretStorage.TryGetSecret(serviceSecretKey, out string serviceSecret, out string serviceSecretError))
                {
                    startInfo.EnvironmentVariables["OPENAI_API_KEY"] = serviceSecret;
                }
                else if (modelService.RequiresApiKey)
                {
                    throw new InvalidOperationException(serviceSecretError);
                }
                if (modelService.ContextLimit.HasValue)
                {
                    startInfo.EnvironmentVariables["GOOSE_PREDEFINED_MODELS"] = new JArray
                    {
                        new JObject
                        {
                            ["name"] = configuredModel,
                            ["provider"] = "openai",
                            ["context_limit"] = modelService.ContextLimit.Value
                        }
                    }.ToString(Formatting.None);
                }
            }
            else if (!string.IsNullOrWhiteSpace(config.Provider))
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
            if (!string.IsNullOrWhiteSpace(configuredProvider))
            {
                startupInfo.Append(" provider=").Append(effectiveProvider);
            }
            if (!string.IsNullOrWhiteSpace(configuredModel))
            {
                startupInfo.Append(" model=").Append(configuredModel);
            }
            if (modelService != null)
            {
                startupInfo.Append(" modelService=").Append(modelService.Name);
                startupInfo.Append(" baseUrl=").Append(modelService.BaseUrl);
            }
            startupInfo.Append(" maxTurns=").Append(config.MaxTurns);
            startupInfo.Append(" maxOutputTokens=").Append(config.MaxOutputTokens);
            startupInfo.Append(" temperature=").Append(config.Temperature);
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
                        MarkFirstModelActivity();
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
                        MarkFirstModelActivity();
                        lock (executionLock)
                        {
                            FlushReasoningTraceSegmentLocked("assistant_segment", currentAssistantTraceSegment);
                            currentThoughtTraceSegment.Append(thoughtText);
                        }
                        Report("assistant_thought", thoughtText, message);
                    }
                    return;
                }

                // tool_call：工具调用发起，显示中文工具名；完整 rawInput 进日志。
                if (string.Equals(updateKind, "tool_call", StringComparison.Ordinal))
                {
                    lock (executionLock)
                    {
                        currentPromptToolCallCount++;
                    }
                    string title = FindFirstString(parameters, "title", "name") ?? "调用工具";
                    bool parameterGenerationFailed =
                        string.Equals(title, "error", StringComparison.OrdinalIgnoreCase);
                    string callId = FindFirstString(parameters, "toolCallId");
                    if (parameterGenerationFailed && !string.IsNullOrWhiteSpace(callId))
                    {
                        parameterGenerationFailureCalls[callId] = 0;
                    }
                    string displayName = parameterGenerationFailed
                        ? "模型工具参数未形成"
                        : ResolveToolDisplayName(parameters, title);
                    MarkFirstModelActivity();
                    AppendReasoningTraceEvent("tool_call", displayName, message);
                    RecordAnalysisToolStarted(callId, parameters, parameterGenerationFailed);
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
                        lock (executionLock)
                        {
                            currentPromptToolErrorCount++;
                        }
                        string callId = FindFirstString(parameters, "toolCallId");
                        bool parameterGenerationFailed = !string.IsNullOrWhiteSpace(callId)
                            && parameterGenerationFailureCalls.TryRemove(callId, out _);
                        string detail = FindFirstString(parameters, "message", "error", "text");
                    string failureSummary = parameterGenerationFailed
                            ? "× 模型未形成可调度的工具名称或参数，请求未到达 MCP"
                            : string.IsNullOrWhiteSpace(detail)
                                ? "× 工具调用失败，ACP 未提供错误内容"
                                : "× " + detail;
                        RecordAnalysisToolFinished(callId, parameters, true, parameterGenerationFailed);
                        AppendReasoningTraceEvent("tool_error", failureSummary, message);
                        var diagnostic = (JObject)parameters.DeepClone();
                        diagnostic["automationDiagnostic"] = parameterGenerationFailed
                            ? new JObject
                            {
                                ["category"] = "provider_tool_arguments_not_formed",
                                ["errorCode"] = "PROVIDER_TOOL_ARGUMENTS_NOT_FORMED",
                                ["message"] = "模型未形成可调度的工具名称或参数。",
                                ["requestReachedMcp"] = false,
                                ["sideEffects"] = "none"
                            }
                            : new JObject
                            {
                                ["category"] = "acp_tool_call_failed",
                                ["requestReachedMcp"] = null,
                                ["sideEffects"] = "unknown"
                            };
                        LogFile(parameterGenerationFailed
                            ? "ACP<- 模型工具参数未形成"
                            : "ACP<- 工具调用失败", diagnostic, LogLevel.Error);
                        Report("tool_result", failureSummary, message);
                        return;
                    }
                    // 进度描述（非完成）不进入 UI 或分析日志，避免同一调用重复刷屏。
                    if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                    string completedCallId = FindFirstString(parameters, "toolCallId");
                    if (!string.IsNullOrWhiteSpace(completedCallId))
                    {
                        parameterGenerationFailureCalls.TryRemove(completedCallId, out _);
                    }
                    // 完成响应只提取摘要给 UI；完整参数和结果由 MCP 统一审计，避免重复落盘。
                    string summary = ExtractToolResultSummary(parameters);
                    RecordAnalysisToolFinished(completedCallId, parameters, false, false);
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
                    MarkFirstModelActivity();
                    lock (executionLock)
                    {
                        FlushReasoningTraceSegmentLocked("thought_segment", currentThoughtTraceSegment);
                        assistantResponse.Append(text);
                        currentAssistantTraceSegment.Append(text);
                    }
                }
                else if (string.Equals(updateKind, "agent_thought", StringComparison.Ordinal))
                {
                    MarkFirstModelActivity();
                    lock (executionLock)
                    {
                        FlushReasoningTraceSegmentLocked("assistant_segment", currentAssistantTraceSegment);
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

        private static string ExtractRawToolResultText(JObject parameters)
        {
            JToken update = parameters?["update"] ?? parameters;
            JToken content = update?["content"];
            if (!(content is JArray array) || array.Count == 0)
            {
                return null;
            }

            JToken first = array[0];
            JToken text = first?["text"] ?? first?["content"]?["text"];
            return text?.Type == JTokenType.String ? text.Value<string>() : null;
        }

        private void RecordAnalysisToolStarted(string callId, JObject parameters, bool parameterGenerationFailed)
        {
            DateTime startedUtc = DateTime.UtcNow;
            JToken update = parameters?["update"] ?? parameters;
            string rawToolName = update?["_meta"]?["goose"]?["toolCall"]?["toolName"]?.Value<string>()
                ?? FindFirstString(parameters, "toolName")
                ?? string.Empty;
            string toolName = AiAnalysisLogger.NormalizeToolName(rawToolName);
            JToken rawInput = update?["rawInput"] ?? JValue.CreateNull();
            JObject args = AiAnalysisLogger.SummarizePayload(rawInput, 12 * 1024);
            string signature = toolName + ":" + (args["sha256"]?.Value<string>() ?? string.Empty);
            int parallelGroup;
            int attempt;
            int activeAtStart;

            lock (executionLock)
            {
                if (activeAnalysisToolCalls.Count == 0
                    || lastAnalysisToolStartedUtc == default(DateTime)
                    || (startedUtc - lastAnalysisToolStartedUtc).TotalMilliseconds > 500)
                {
                    currentParallelGroup++;
                }
                lastAnalysisToolStartedUtc = startedUtc;
                parallelGroup = currentParallelGroup;
                analysisToolAttempts.TryGetValue(signature, out int previousAttempts);
                attempt = previousAttempts + 1;
                analysisToolAttempts[signature] = attempt;
                activeAtStart = activeAnalysisToolCalls.Count;
                if (!string.IsNullOrWhiteSpace(callId))
                {
                    activeAnalysisToolCalls[callId] = new AnalysisToolCallState
                    {
                        ToolCallId = callId,
                        ToolName = toolName,
                        IsAutomationMcp = rawToolName.StartsWith("automation__", StringComparison.Ordinal),
                        StartedUtc = startedUtc,
                        ParallelGroup = parallelGroup,
                        Attempt = attempt
                    };
                }
                currentMaxConcurrentTools = Math.Max(currentMaxConcurrentTools, activeAtStart + 1);
            }

            WriteAnalysisEvent("tool.started", new JObject
            {
                ["toolCallId"] = callId ?? string.Empty,
                ["tool"] = toolName,
                ["parallelGroup"] = parallelGroup,
                ["activeAtStart"] = activeAtStart,
                ["attempt"] = attempt,
                ["parameterState"] = parameterGenerationFailed ? "not_formed" : "formed",
                ["args"] = args
            }, startedUtc);
        }

        private void RecordAnalysisToolFinished(
            string callId,
            JObject parameters,
            bool transportFailed,
            bool parameterGenerationFailed)
        {
            DateTime finishedUtc = DateTime.UtcNow;
            AnalysisToolCallState state;
            lock (executionLock)
            {
                if (string.IsNullOrWhiteSpace(callId)
                    || !activeAnalysisToolCalls.TryGetValue(callId, out state))
                {
                    state = new AnalysisToolCallState
                    {
                        ToolCallId = callId ?? string.Empty,
                        ToolName = AiAnalysisLogger.NormalizeToolName(FindFirstString(parameters, "toolName")),
                        IsAutomationMcp = (FindFirstString(parameters, "toolName") ?? string.Empty)
                            .StartsWith("automation__", StringComparison.Ordinal),
                        StartedUtc = finishedUtc,
                        ParallelGroup = currentParallelGroup,
                        Attempt = 1
                    };
                }
                else
                {
                    activeAnalysisToolCalls.Remove(callId);
                }
                analysisToolIntervals.Add(new AnalysisTimeInterval(state.StartedUtc, finishedUtc));
                if (parameterGenerationFailed)
                {
                    currentParameterFailureCount++;
                }
            }

            long durationMs = Math.Max(0L, (long)(finishedUtc - state.StartedUtc).TotalMilliseconds);
            string rawResult = parameterGenerationFailed ? null : ExtractRawToolResultText(parameters);
            JToken resultValue = JValue.CreateNull();
            JObject resultObject = null;
            if (!string.IsNullOrWhiteSpace(rawResult))
            {
                try
                {
                    resultValue = JToken.Parse(rawResult);
                    resultObject = resultValue as JObject;
                }
                catch
                {
                    resultValue = new JValue(rawResult);
                }
            }

            bool businessFailed = resultObject?["ok"]?.Type == JTokenType.Boolean
                && resultObject["ok"].Value<bool>() == false;
            if (businessFailed)
            {
                lock (executionLock)
                {
                    currentPromptToolErrorCount++;
                }
            }
            string status = parameterGenerationFailed
                ? "not_dispatched"
                : transportFailed ? "transport_error"
                : businessFailed ? "business_error" : "ok";
            string stage = parameterGenerationFailed
                ? "provider.arguments"
                : transportFailed ? "acp" : businessFailed ? "business" : string.Empty;
            string transportMessage = transportFailed
                ? FindFirstString(parameters, "message", "error", "text")
                : null;
            string transportCode = transportFailed
                && transportMessage?.IndexOf("未开放工具", StringComparison.Ordinal) >= 0
                    ? "TOOL_NOT_AVAILABLE"
                    : transportFailed ? "ACP_TOOL_CALL_FAILED" : string.Empty;
            int resultBudget = string.Equals(status, "ok", StringComparison.Ordinal) ? 4 * 1024 : 8 * 1024;
            var data = new JObject
            {
                ["toolCallId"] = state.ToolCallId,
                ["tool"] = state.ToolName,
                ["parallelGroup"] = state.ParallelGroup,
                ["attempt"] = state.Attempt,
                ["status"] = status,
                ["durationMs"] = durationMs,
                ["route"] = new JObject
                {
                    ["reachedMcp"] = parameterGenerationFailed
                        ? false
                        : state.IsAutomationMcp && !transportFailed ? (bool?)true : null,
                    ["sideEffects"] = parameterGenerationFailed
                        ? "none"
                        : transportCode == "TOOL_NOT_AVAILABLE"
                            ? "none"
                            : resultObject?["recovery"]?["sideEffects"]?.Value<string>() ?? "unknown"
                }
            };
            if (!string.IsNullOrWhiteSpace(stage))
            {
                data["stage"] = stage;
            }
            if (!parameterGenerationFailed && resultValue.Type != JTokenType.Null)
            {
                data["result"] = AiAnalysisLogger.SummarizePayload(resultValue, resultBudget);
            }
            if (!string.Equals(status, "ok", StringComparison.Ordinal))
            {
                data["error"] = new JObject
                {
                    ["code"] = parameterGenerationFailed
                        ? "PROVIDER_TOOL_ARGUMENTS_NOT_FORMED"
                        : resultObject?["errorCode"]?.Value<string>() ?? transportCode,
                    ["message"] = parameterGenerationFailed
                        ? "模型未形成可调度的工具名称或参数。"
                        : resultObject?["message"]?.Value<string>()
                            ?? transportMessage
                            ?? "ACP 未提供工具失败详情。",
                    ["recovery"] = parameterGenerationFailed
                        ? new JObject
                        {
                            ["reason"] = "provider_output_missing_tool_name_or_arguments",
                            ["retryableWhen"] = "model_forms_a_valid_tool_call",
                            ["sideEffects"] = "none"
                        }
                        : resultObject?["recovery"]?.DeepClone()
                            ?? (transportFailed
                                ? new JObject
                                {
                                    ["reason"] = transportCode == "TOOL_NOT_AVAILABLE"
                                        ? "requested_tool_not_exposed_by_current_profile"
                                        : "acp_tool_call_failed",
                                    ["retryableWhen"] = transportCode == "TOOL_NOT_AVAILABLE"
                                        ? "use_a_tool_published_by_the_current_profile"
                                        : "acp_returns_a_dispatchable_tool_result",
                                    ["sideEffects"] = transportCode == "TOOL_NOT_AVAILABLE"
                                        ? "none"
                                        : "unknown"
                                }
                                : null)
                };
            }
            WriteAnalysisEvent("tool.finished", data, finishedUtc);
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
                context = "当前 Automation 工具模式：Diagnostic。当前会话只开放读取和诊断工具，不具备运行控制或配置写入能力。";
            }
            else
            {
                context = "当前 Automation 工具模式：Editor。当前会话开放读取、诊断、配置写入和运行控制工具。";
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

        private static JObject BuildManagedContextAnalysis()
        {
            try
            {
                string promptVersionPath = Path.Combine(
                    Path.GetDirectoryName(GooseRuntimeProvisioner.PromptPath),
                    ".automation-system-prompt-version");
                string integrationVersionPath = Path.Combine(
                    Path.GetDirectoryName(GooseRuntimeProvisioner.IntegrationContextPath),
                    ".automation-context-version");
                return new JObject
                {
                    ["managedAvailable"] = GooseRuntimeProvisioner.IsManagedContextAvailable,
                    ["system"] = BuildManagedFileAnalysis(
                        GooseRuntimeProvisioner.PromptPath,
                        promptVersionPath,
                        GooseRuntimeProvisioner.SystemPromptVersion),
                    ["automation"] = BuildManagedFileAnalysis(
                        GooseRuntimeProvisioner.IntegrationContextPath,
                        integrationVersionPath,
                        GooseRuntimeProvisioner.IntegrationContextVersion)
                };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["managedAvailable"] = GooseRuntimeProvisioner.IsManagedContextAvailable,
                    ["inspectionError"] = ex.Message
                };
            }
        }

        private static JObject BuildManagedFileAnalysis(string path, string versionPath, int bundledVersion)
        {
            var result = new JObject
            {
                ["bundledVersion"] = bundledVersion,
                ["exists"] = File.Exists(path)
            };
            if (File.Exists(versionPath)
                && int.TryParse(File.ReadAllText(versionPath, Encoding.UTF8).Trim(), out int effectiveVersion))
            {
                result["effectiveVersion"] = effectiveVersion;
            }
            if (File.Exists(path))
            {
                JObject fingerprint = AiAnalysisLogger.FingerprintText(File.ReadAllText(path, Encoding.UTF8));
                result["bytes"] = fingerprint["bytes"];
                result["sha256"] = fingerprint["sha256"];
            }
            return result;
        }

        /// <summary>
        /// 构建当前用户选中流程/步骤/指令的背景信息，附加到 prompt 中。
        /// 只展开到用户实际选中的最深层级，避免把未选中的下级对象误传给 AI。
        /// </summary>
        private static string BuildSelectionContext()
        {
            JObject selection = BuildSelectionAnalysis();
            if (selection?["hasSelection"]?.Value<bool?>() != true)
            {
                return "\n\n当前用户未选中任何流程。";
            }
            selection.Remove("hasSelection");
            return "\n\n当前用户在流程编辑器中的选中对象（仅用于定位，不等于用户要求改动；实际目标仍以用户请求为准）：\n"
                + selection.ToString(Formatting.None)
                + "\n用户口语中的\"N号流程\"即 procIndex=N。";
        }

        private static JObject BuildSelectionAnalysis()
        {
            try
            {
                if (SF.frmProc == null || SF.frmProc.IsDisposed)
                {
                    return new JObject { ["hasSelection"] = false };
                }
                int procIndex = SF.frmProc.SelectedProcNum;
                if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
                {
                    return new JObject { ["hasSelection"] = false };
                }

                Proc proc = SF.frmProc.procsList[procIndex];
                int stepCount = proc.steps?.Count ?? 0;
                var selection = new JObject
                {
                    ["hasSelection"] = true,
                    ["process"] = new JObject
                    {
                        ["procIndex"] = procIndex,
                        ["procId"] = proc.head?.Id.ToString("D"),
                        ["name"] = proc.head?.Name ?? string.Empty,
                        ["stepCount"] = stepCount
                    }
                };

                int stepIndex = SF.frmProc.SelectedStepNum;
                if (stepIndex >= 0 && stepIndex < stepCount)
                {
                    Step step = proc.steps[stepIndex];
                    int opCount = step?.Ops?.Count ?? 0;
                    selection["step"] = new JObject
                    {
                        ["stepIndex"] = stepIndex,
                        ["stepId"] = step?.Id.ToString("D"),
                        ["name"] = step?.Name ?? string.Empty,
                        ["operationCount"] = opCount
                    };

                    int opIndex = SF.frmDataGrid?.iSelectedRow ?? -1;
                    if (opIndex >= 0 && opIndex < opCount)
                    {
                        OperationType operation = step.Ops[opIndex];
                        selection["operation"] = new JObject
                        {
                            ["opIndex"] = opIndex,
                            ["opId"] = operation?.Id.ToString("D"),
                            ["name"] = operation?.Name ?? string.Empty,
                            ["operaType"] = operation?.OperaType ?? string.Empty
                        };
                    }
                }
                return selection;
            }
            catch
            {
                return new JObject { ["hasSelection"] = false };
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

        // Developer 工具的工作目录使用当前运行项目的 HMI 源码目录。
        private string ResolveWorkingDirectory()
        {
            if (!HmiDevelopmentSourceLocator.TryResolve(
                AppDomain.CurrentDomain.BaseDirectory,
                out HmiDevelopmentSource source,
                out string error))
            {
                throw new DirectoryNotFoundException(error);
            }
            return source.SourceDirectory;
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
            }
        }

        private void FlushReasoningTraceSegmentLocked(string kind, StringBuilder segment, string channel = null)
        {
            if (segment == null || segment.Length == 0)
            {
                return;
            }
            string text = segment.ToString();
            WriteAnalysisEventLocked("model.segment", new JObject
            {
                ["channel"] = channel ?? (string.Equals(kind, "thought_segment", StringComparison.Ordinal)
                    ? "reasoning"
                    : "analysis"),
                ["text"] = AiAnalysisLogger.SummarizePayload(new JValue(text), 4 * 1024)
            });
            segment.Clear();
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
                JToken safeRaw = null;
                if (level == LogLevel.Error)
                {
                    safeRaw = json?.DeepClone();
                    RedactSensitiveValues(safeRaw);
                }
                var record = new JObject
                {
                    ["time"] = DateTime.Now.ToString("O"),
                    ["source"] = "acp",
                    ["kind"] = level == LogLevel.Error ? "diagnostic_error" : "diagnostic",
                    ["text"] = message ?? string.Empty
                };
                if (level == LogLevel.Error && safeRaw != null)
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

        public void LogFrontendAnalysisEvent(string eventName, JObject data)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return;
            }
            lock (executionLock)
            {
                long waitMs = data?["waitMs"]?.Value<long?>() ?? 0L;
                if (waitMs > 0)
                {
                    currentPreviewWaitMs += waitMs;
                }
                WriteAnalysisEventLocked(eventName, data);
            }
        }

        private void MarkFirstModelActivity()
        {
            lock (executionLock)
            {
                if (currentFirstModelActivityUtc == default(DateTime))
                {
                    currentFirstModelActivityUtc = DateTime.UtcNow;
                }
            }
        }

        private void WriteAnalysisEvent(string eventName, JObject data, DateTime? eventUtc = null)
        {
            lock (executionLock)
            {
                WriteAnalysisEventLocked(eventName, data, eventUtc);
            }
        }

        private void WriteAnalysisEventLocked(string eventName, JObject data, DateTime? eventUtc = null)
        {
            DateTime timestampUtc = (eventUtc ?? DateTime.UtcNow).ToUniversalTime();
            var record = new JObject
            {
                ["event"] = eventName ?? string.Empty,
                ["tsUtc"] = timestampUtc.ToString("O"),
                ["seq"] = ++currentAnalysisSequence,
                ["auditSessionId"] = auditSessionId,
                ["gooseSessionId"] = sessionId ?? string.Empty,
                ["turnId"] = currentPromptId ?? string.Empty,
                ["elapsedMs"] = currentPromptStartedUtc == default(DateTime)
                    ? 0L
                    : Math.Max(0L, (long)(timestampUtc - currentPromptStartedUtc).TotalMilliseconds)
            };
            if (data != null)
            {
                foreach (JProperty property in data.Properties())
                {
                    record[property.Name] = property.Value?.DeepClone();
                }
            }
            AiAnalysisLogger.Write(record);
        }

        private JObject BuildTurnFinishedAnalysisLocked(
            JObject promptResult,
            Exception promptException,
            long totalDurationMs,
            int visibleResponseChars)
        {
            long toolAggregateMs = analysisToolIntervals.Sum(interval => interval.DurationMs);
            long toolWallMs = CalculateIntervalUnionMs(analysisToolIntervals);
            long unattributedMs = Math.Max(0L, totalDurationMs - toolWallMs - currentPreviewWaitMs);
            int retryCount = analysisToolAttempts.Values.Sum(count => Math.Max(0, count - 1));
            var result = new JObject
            {
                ["status"] = promptException == null ? "completed" : "failed",
                ["stopReason"] = promptResult?["stopReason"]?.Value<string>() ?? string.Empty,
                ["durationMs"] = totalDurationMs,
                ["firstActivityMs"] = currentFirstModelActivityUtc == default(DateTime)
                    ? (long?)null
                    : Math.Max(0L, (long)(currentFirstModelActivityUtc - currentPromptStartedUtc).TotalMilliseconds),
                ["toolCallCount"] = currentPromptToolCallCount,
                ["toolFailureCount"] = currentPromptToolErrorCount,
                ["parameterFailureCount"] = currentParameterFailureCount,
                ["retryCount"] = retryCount,
                ["maxConcurrentTools"] = currentMaxConcurrentTools,
                ["toolAggregateMs"] = toolAggregateMs,
                ["toolWallMs"] = toolWallMs,
                ["confirmationWaitMs"] = currentPreviewWaitMs,
                ["unattributedMs"] = unattributedMs,
                ["visibleResponseChars"] = visibleResponseChars,
                ["unfinishedToolCount"] = activeAnalysisToolCalls.Count
            };
            JToken usage = promptResult?["usage"];
            if (usage != null)
            {
                result["usage"] = usage.DeepClone();
            }
            if (promptException != null)
            {
                result["error"] = new JObject
                {
                    ["type"] = promptException.GetType().FullName,
                    ["message"] = promptException.Message
                };
            }
            return result;
        }

        private static long CalculateIntervalUnionMs(IEnumerable<AnalysisTimeInterval> intervals)
        {
            List<AnalysisTimeInterval> ordered = intervals
                .Where(interval => interval != null)
                .OrderBy(interval => interval.StartUtc)
                .ToList();
            if (ordered.Count == 0)
            {
                return 0L;
            }

            DateTime start = ordered[0].StartUtc;
            DateTime end = ordered[0].EndUtc;
            long totalMs = 0L;
            for (int i = 1; i < ordered.Count; i++)
            {
                AnalysisTimeInterval interval = ordered[i];
                if (interval.StartUtc <= end)
                {
                    if (interval.EndUtc > end)
                    {
                        end = interval.EndUtc;
                    }
                    continue;
                }
                totalMs += Math.Max(0L, (long)(end - start).TotalMilliseconds);
                start = interval.StartUtc;
                end = interval.EndUtc;
            }
            totalMs += Math.Max(0L, (long)(end - start).TotalMilliseconds);
            return totalMs;
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

                string kind = record["kind"]?.Value<string>() ?? string.Empty;
                if (string.Equals(kind, "diagnostic_error", StringComparison.Ordinal)
                    || string.Equals(kind, "prompt_failed", StringComparison.Ordinal))
                {
                    JObject structuredRecord = CreateStructuredExecutionRecord(record);
                    string structuredContent = structuredRecord.ToString(Formatting.None) + Environment.NewLine;
                    Directory.CreateDirectory(structuredExecutionLogRoot);
                    int structuredIndex = 0;
                    string structuredPath;
                    while (true)
                    {
                        structuredPath = Path.Combine(
                            structuredExecutionLogRoot,
                            $"{datePrefix}_{structuredIndex:000}.jsonl");
                        if (!File.Exists(structuredPath)
                            || new FileInfo(structuredPath).Length + Encoding.UTF8.GetByteCount(structuredContent) <= MaxLogFileBytes)
                        {
                            break;
                        }
                        structuredIndex++;
                    }
                    using (StreamWriter writer = new StreamWriter(structuredPath, true, new UTF8Encoding(false)))
                    {
                        writer.Write(structuredContent);
                    }
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

        private static JObject CreateStructuredExecutionRecord(JObject record)
        {
            string kind = record["kind"]?.Value<string>() ?? string.Empty;
            DateTime timeUtc;
            if (!DateTime.TryParse(
                record["time"]?.Value<string>(),
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out timeUtc))
            {
                timeUtc = DateTime.UtcNow;
            }
            else
            {
                timeUtc = timeUtc.ToUniversalTime();
            }

            JToken raw = record["raw"];
            string toolCallId = record["toolCallId"]?.Value<string>()
                ?? record["callId"]?.Value<string>()
                ?? FindFirstString(raw, "toolCallId");
            string toolName = record["toolName"]?.Value<string>()
                ?? FindFirstString(raw, "toolName");
            string status = record["status"]?.Value<string>()
                ?? FindFirstString(raw, "status");

            var structured = new JObject
            {
                ["schemaVersion"] = 1,
                ["eventId"] = Guid.NewGuid().ToString("N"),
                ["timeUtc"] = timeUtc.ToString("O"),
                ["source"] = record["source"]?.Value<string>() ?? string.Empty,
                ["eventName"] = kind,
                ["auditSessionId"] = record["auditSessionId"]?.Value<string>() ?? string.Empty,
                ["gooseSessionId"] = record["gooseSessionId"]?.Value<string>() ?? string.Empty,
                ["promptId"] = record["promptId"]?.Value<string>() ?? string.Empty
            };
            AddStructuredString(structured, "toolCallId", toolCallId);
            AddStructuredString(structured, "toolName", toolName);
            AddStructuredString(structured, "status", status);
            AddStructuredString(structured, "text", record["text"]?.Value<string>());

            if (record["durationMs"] != null)
            {
                structured["durationMs"] = record["durationMs"].DeepClone();
            }
            if (record["args"] != null)
            {
                structured["args"] = record["args"].DeepClone();
            }
            if (record["result"] != null)
            {
                structured["result"] = record["result"].DeepClone();
            }
            if (record["error"] != null)
            {
                structured["error"] = record["error"].DeepClone();
            }

            if (raw != null)
            {
                structured["raw"] = raw.DeepClone();
            }

            return structured;
        }

        private static void AddStructuredString(JObject target, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target[name] = value;
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

        private sealed class AnalysisToolCallState
        {
            public string ToolCallId { get; set; }

            public string ToolName { get; set; }

            public bool IsAutomationMcp { get; set; }

            public DateTime StartedUtc { get; set; }

            public int ParallelGroup { get; set; }

            public int Attempt { get; set; }
        }

        private sealed class AnalysisTimeInterval
        {
            public AnalysisTimeInterval(DateTime startUtc, DateTime endUtc)
            {
                StartUtc = startUtc;
                EndUtc = endUtc < startUtc ? startUtc : endUtc;
            }

            public DateTime StartUtc { get; }

            public DateTime EndUtc { get; }

            public long DurationMs => Math.Max(0L, (long)(EndUtc - StartUtc).TotalMilliseconds);
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
