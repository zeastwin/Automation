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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
    public sealed class PiRpcEvent
    {
        public DateTime Time { get; set; }

        public string Kind { get; set; }

        public string Text { get; set; }

        public JObject Raw { get; set; }
    }

    public sealed class AiFileAttachment
    {
        public AiFileAttachment(
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

    /// <summary>
    /// EW-AI 与 Pi 子进程（pi --mode rpc）的 JSONL 客户端。隐藏启动进程，
    /// stdin/stdout 按 \n 分隔收发 JSON；平台工具由模型经 Pi 内置 bash 调用
    /// Automation.ToolCli.exe，不经过本类转发。
    /// </summary>
    public sealed class PiRpcClient : IDisposable
    {
        private const int CommandTimeoutMs = 30000;
        private const long MaxLogFileBytes = 5L * 1024L * 1024L;

        private static readonly string executionLogRoot = Path.Combine(@"D:\AutomationLogs", "AIExecution");
        private static readonly string structuredExecutionLogRoot = Path.Combine(executionLogRoot, "Structured");
        private static readonly Mutex executionLogMutex = new Mutex(false, "AutomationAIExecutionAuditLog");

        private readonly AiConfig config;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> pendingRequests =
            new ConcurrentDictionary<string, TaskCompletionSource<JObject>>(StringComparer.Ordinal);
        private readonly object writeLock = new object();
        private readonly object executionLock = new object();
        private readonly string auditSessionId = Guid.NewGuid().ToString("N");
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
        private string currentPromptId;
        private DateTime currentPromptStartedUtc;
        private int currentPromptToolCallCount;
        private int currentPromptToolErrorCount;
        private int currentAnalysisSequence;
        private int currentParallelGroup;
        private int currentMaxConcurrentTools;
        private long currentPreviewWaitMs;
        private DateTime lastAnalysisToolStartedUtc;
        private DateTime currentFirstModelActivityUtc;
        private JToken currentTurnUsage;
        private bool agentStreaming;
        private TaskCompletionSource<JObject> promptCompletion;
        private bool disposed;

        private readonly PlatformRuntime runtime;

        public PiRpcClient(PlatformRuntime runtime, AiConfig config,
            string restoredConversationContext = null)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.restoredConversationContext = restoredConversationContext;
        }

        public event Action<PiRpcEvent> EventReceived;

        public string SessionId => sessionId;

        /// <summary>
        /// 完全权限（迁移/平台配置工具）开关，仅 Editor Profile 会话生效，
        /// 由前台在创建客户端时赋值；经 AUTOMATION_TOOL_FULL_PERMISSION 注入子进程。
        /// </summary>
        public bool FullPermissionEnabled { get; set; }

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

        private bool IsRuntimeDiagnostic => string.Equals(
            config.ToolProfile, "RuntimeDiagnostic", StringComparison.Ordinal);

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
                // Pi 进程在两轮对话之间退出（崩溃/超时），EnsureSession 会重建会话。
                // 新会话不携带之前的对话历史，必须提示用户，否则用户以为 AI 还记得上下文。
                string message = "⚠️ Pi 进程已退出并重建会话，之前对话上下文已丢失。如果之前的对话涉及方案选择，请重新说明。";
                LogExecution("session_recreated", message, null);
                Report("exit", message, null);
            }

            await NewSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task NewSessionAsync(CancellationToken cancellationToken)
        {
            EnsureProcessStarted();
            JObject result = await SendCommandAsync("new_session", null, CommandTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
            if (result["data"]?["cancelled"]?.Value<bool>() == true)
            {
                throw new InvalidOperationException("Pi 新建会话被扩展取消。");
            }

            JObject state = await SendCommandAsync("get_state", null, CommandTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
            sessionId = state["data"]?["sessionId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("Pi 未返回 sessionId。");
            }
            Report("lifecycle", $"EW-AI 会话已创建：{sessionId}", state);
        }

        public async Task<JObject> PromptAsync(
            string prompt,
            IReadOnlyList<AiFileAttachment> fileAttachments,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt) && (fileAttachments == null || fileAttachments.Count == 0))
            {
                throw new InvalidOperationException("提示词不能为空。");
            }

            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            if (fileAttachments != null)
            {
                foreach (AiFileAttachment file in fileAttachments)
                {
                    if (file == null || file.Data == null || file.Data.Length == 0
                        || string.IsNullOrWhiteSpace(file.MimeType)
                        || !string.IsNullOrWhiteSpace(file.Error))
                    {
                        throw new InvalidOperationException(file?.Error ?? "文件附件无效。");
                    }
                    if (file.IsImage)
                    {
                        AiModelServiceConfig modelService = AiConfigStorage.FindModelService(config);
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
                foreach (AiFileAttachment file in fileAttachments.Where(item => item != null && !item.IsImage))
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
            JArray images = null;
            if (fileAttachments != null)
            {
                foreach (AiFileAttachment file in fileAttachments.Where(item => item != null && item.IsImage))
                {
                    if (images == null)
                    {
                        images = new JArray();
                    }
                    images.Add(new JObject
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
                currentPreviewWaitMs = 0;
                lastAnalysisToolStartedUtc = default(DateTime);
                currentFirstModelActivityUtc = default(DateTime);
                currentTurnUsage = null;
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
                ["provider"] = AiConfigStorage.FindModelService(config) == null
                    ? config.Provider ?? string.Empty : "openai-compatible",
                ["model"] = AiConfigStorage.FindModelService(config)?.Model
                    ?? config.Model ?? string.Empty,
                ["modelServiceId"] = config.ModelServiceId ?? string.Empty,
                ["agent"] = new JObject
                {
                    ["name"] = "pi"
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
                var promptParams = new JObject
                {
                    ["message"] = finalPrompt
                };
                if (images != null)
                {
                    promptParams["images"] = images;
                }
                // 流式期间的追问按 followUp 排队，等当前运行结束后再投递。
                if (agentStreaming)
                {
                    promptParams["streamingBehavior"] = "followUp";
                }

                // prompt 的 response 只表示受理；本轮完成以 agent_settled 事件为准。
                var settled = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
                promptCompletion = settled;
                JObject acceptance;
                try
                {
                    acceptance = await SendCommandAsync("prompt", promptParams, CommandTimeoutMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    promptCompletion = null;
                    throw;
                }
                if (acceptance["success"]?.Value<bool>() != true)
                {
                    promptCompletion = null;
                    throw new InvalidOperationException(
                        "Pi 拒绝了本轮提示词：" + (acceptance["error"]?.Value<string>() ?? "未知原因"));
                }

                using (cancellationToken.Register(() =>
                {
                    Cancel();
                    settled.TrySetCanceled(cancellationToken);
                }))
                {
                    promptResult = await settled.Task.ConfigureAwait(false);
                }
                promptCompletion = null;

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
                promptCompletion = null;
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

        public void Cancel()
        {
            if (stdin == null)
            {
                return;
            }

            try
            {
                JObject command = new JObject
                {
                    ["id"] = Interlocked.Increment(ref nextRequestId).ToString(CultureInfo.InvariantCulture),
                    ["type"] = "abort"
                };
                WriteJsonLine(command);
                Report("lifecycle", "已向 Pi 发送取消请求。", command);
            }
            catch (Exception ex)
            {
                LogFile($"Pi 取消请求写入失败 err={ex.Message}", LogLevel.Error);
            }
        }

        private void EnsureProcessStarted()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PiRpcClient));
            }
            if (process != null && !process.HasExited)
            {
                return;
            }
            if (!PiRuntimeEnvironment.TryValidate(config.AgentExecutablePath, out string runtimeError))
            {
                throw new InvalidOperationException(runtimeError);
            }
            if (!PiContextProvisioner.IsManagedContextAvailable)
            {
                throw new InvalidOperationException("EW-AI 受管上下文未通过启动校验，当前会话不可用。");
            }

            string sessionWorkingDirectory = ResolveWorkingDirectory();
            string toolCliPath = ToolCliPackageLocator.ResolveToolCliExecutablePath();
            if (string.IsNullOrWhiteSpace(toolCliPath))
            {
                throw new InvalidOperationException(
                    "未找到完整的 Automation.ToolCli 运行包，必须同时包含 exe、dll、deps.json 和 runtimeconfig.json。");
            }

            // 运行诊断会话使用独立配置目录（不含平台集成上下文 APPEND_SYSTEM.md），
            // 且只挂载 ToolCli 机制 Skill；编辑会话挂载机制与流程编写两个 Skill。
            string agentDirectory = IsRuntimeDiagnostic
                ? PiContextProvisioner.DiagnosticAgentDirectory
                : PiContextProvisioner.AgentDirectory;
            Directory.CreateDirectory(agentDirectory);

            var arguments = new StringBuilder();
            arguments.Append("--mode rpc");
            string sessionName = string.IsNullOrWhiteSpace(config.SessionName)
                ? "automation"
                : config.SessionName.Trim();
            arguments.Append(" --name ").Append(QuoteArgument(sessionName));
            arguments.Append(" --no-skills");
            // --skill 路径参数一律使用正斜杠。
            arguments.Append(" --skill ").Append(QuoteArgument(ToForwardSlashes(
                Path.Combine(PiContextProvisioner.ToolsCliSkillDirectory, "SKILL.md"))));
            if (!IsRuntimeDiagnostic)
            {
                arguments.Append(" --skill ").Append(QuoteArgument(ToForwardSlashes(
                    Path.Combine(PiContextProvisioner.ProcessAuthoringSkillDirectory, "SKILL.md"))));
            }

            AiModelServiceConfig modelService = AiConfigStorage.FindModelService(config);
            string configuredProvider;
            string configuredModel;
            if (modelService != null)
            {
                // 自定义 OpenAI 兼容服务写入 PI_CODING_AGENT_DIR 下的 models.json，
                // provider 名使用服务 Id；密钥经环境变量插值，不落明文。
                WriteModelsJson(agentDirectory, modelService);
                configuredProvider = modelService.Id.Trim();
                configuredModel = modelService.Model?.Trim();
            }
            else
            {
                configuredProvider = config.Provider?.Trim();
                configuredModel = config.Model?.Trim();
            }
            if (!string.IsNullOrWhiteSpace(configuredProvider))
            {
                arguments.Append(" --provider ").Append(QuoteArgument(configuredProvider));
            }
            if (!string.IsNullOrWhiteSpace(configuredModel))
            {
                arguments.Append(" --model ").Append(QuoteArgument(configuredModel));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = config.AgentExecutablePath,
                Arguments = arguments.ToString(),
                WorkingDirectory = sessionWorkingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.EnvironmentVariables["PATH"] = PiRuntimeEnvironment.MachineGitCommandPath + Path.PathSeparator
                + (startInfo.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
            // 用户级 Pi 配置（%USERPROFILE%\.pi\agent）可能带有本机 provider/扩展，
            // 必须重定向到平台受管目录，只影响当前 EW-AI 子进程。
            startInfo.EnvironmentVariables["PI_CODING_AGENT_DIR"] = agentDirectory;
            startInfo.EnvironmentVariables["AUTOMATION_TOOLCLI_PATH"] = toolCliPath;
            startInfo.EnvironmentVariables["AUTOMATION_TOOL_PROFILE"] = config.ToolProfile;
            if (FullPermissionEnabled
                && string.Equals(config.ToolProfile, "Editor", StringComparison.Ordinal))
            {
                startInfo.EnvironmentVariables["AUTOMATION_TOOL_FULL_PERMISSION"] = "1";
            }
            else
            {
                startInfo.EnvironmentVariables.Remove("AUTOMATION_TOOL_FULL_PERMISSION");
            }

            if (modelService != null)
            {
                string serviceSecretKey = AiProviderSecretStorage.GetModelServiceSecretKey(modelService.Id);
                if (AiProviderSecretStorage.TryGetSecret(serviceSecretKey, out string serviceSecret, out string serviceSecretError))
                {
                    startInfo.EnvironmentVariables["AUTOMATION_MODEL_SERVICE_API_KEY"] = serviceSecret;
                }
                else if (modelService.RequiresApiKey)
                {
                    throw new InvalidOperationException(serviceSecretError);
                }
                else
                {
                    // 无鉴权的本地服务仍需占位值，Pi 才会把该模型视为可用。
                    startInfo.EnvironmentVariables["AUTOMATION_MODEL_SERVICE_API_KEY"] = "none";
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
                LogFile($"Pi 进程启动失败：exe={config.AgentExecutablePath}", LogLevel.Error);
                throw new InvalidOperationException("EW-AI Pi 进程启动失败。");
            }

            // .NET Framework 的 ProcessStartInfo 不支持 StandardInputEncoding，
            // process.StandardInput 默认用系统代码页（中文 Windows 为 GBK）。
            // Pi RPC JSONL over stdio 要求 UTF-8，故基于 BaseStream 自建 UTF-8 StreamWriter，
            // 不带 BOM、换行固定 \n（Pi 按严格 JSONL 分帧）。
            stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            Task.Run(() => ReadStdoutLoop(process.StandardOutput));
            Task.Run(() => ReadStderrLoop(process.StandardError));
            StringBuilder startupInfo = new StringBuilder();
            startupInfo.Append("Pi 进程启动 exe=").Append(config.AgentExecutablePath);
            startupInfo.Append(" cwd=").Append(sessionWorkingDirectory);
            startupInfo.Append(" sessionName=").Append(sessionName);
            startupInfo.Append(" agentDir=").Append(agentDirectory);
            startupInfo.Append(" toolCliPath=").Append(toolCliPath);
            startupInfo.Append(" toolProfile=").Append(config.ToolProfile);
            startupInfo.Append(" fullPermission=").Append(FullPermissionEnabled);
            if (!string.IsNullOrWhiteSpace(configuredProvider))
            {
                startupInfo.Append(" provider=").Append(configuredProvider);
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
            startupInfo.Append(" maxOutputTokens=").Append(config.MaxOutputTokens);
            LogFile(startupInfo.ToString(), LogLevel.Normal);
            Report("lifecycle", $"EW-AI Pi 进程已启动：{config.AgentExecutablePath} {startInfo.Arguments}", null);
        }

        private void WriteModelsJson(string agentDirectory, AiModelServiceConfig modelService)
        {
            var modelsJson = new JObject
            {
                ["providers"] = new JObject
                {
                    [modelService.Id.Trim()] = new JObject
                    {
                        ["baseUrl"] = modelService.BaseUrl.Trim(),
                        ["api"] = "openai-completions",
                        ["apiKey"] = "$AUTOMATION_MODEL_SERVICE_API_KEY",
                        ["models"] = new JArray
                        {
                            new JObject
                            {
                                ["id"] = modelService.Model.Trim(),
                                ["name"] = modelService.Name,
                                ["input"] = modelService.SupportsVision
                                    ? new JArray("text", "image")
                                    : new JArray("text"),
                                ["contextWindow"] = modelService.ContextLimit ?? 128000,
                                ["maxTokens"] = config.MaxOutputTokens
                            }
                        }
                    }
                }
            };
            string path = Path.Combine(agentDirectory, "models.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, modelsJson.ToString(Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string ToForwardSlashes(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            string message = "EW-AI Pi 进程已退出。";
            try
            {
                message = $"EW-AI Pi 进程已退出，退出码 {process?.ExitCode ?? -1}。";
            }
            catch
            {
            }
            LogFile(message, LogLevel.Error);
            Report("exit", message, null);
            sessionId = null;
            agentStreaming = false;
            promptCompletion?.TrySetException(new InvalidOperationException(message));
            foreach (var item in pendingRequests)
            {
                item.Value.TrySetException(new InvalidOperationException(message));
            }
            pendingRequests.Clear();
        }

        private async Task<JObject> SendCommandAsync(string type, JObject parameters, int timeoutMs, CancellationToken cancellationToken)
        {
            EnsureProcessStarted();
            string id = Interlocked.Increment(ref nextRequestId).ToString(CultureInfo.InvariantCulture);
            var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pendingRequests.TryAdd(id, tcs))
            {
                throw new InvalidOperationException($"Pi 请求 ID 冲突：{id}");
            }

            JObject command = new JObject
            {
                ["id"] = id,
                ["type"] = type
            };
            if (parameters != null)
            {
                foreach (JProperty property in parameters.Properties())
                {
                    command[property.Name] = property.Value;
                }
            }

            try
            {
                WriteJsonLine(command);
            }
            catch (Exception ex)
            {
                LogFile($"Pi 写入失败 id={id} type={type} err={ex.Message}", LogLevel.Error);
                pendingRequests.TryRemove(id, out _);
                throw;
            }
            LogFile($"Pi-> 命令 id={id} type={type}", parameters, LogLevel.Normal);
            Report("request", $"{type} 命令已发送。", command);

            Task delayTask = Task.Delay(timeoutMs, cancellationToken);
            Task completed = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);
            if (completed == tcs.Task)
            {
                return await tcs.Task.ConfigureAwait(false);
            }

            pendingRequests.TryRemove(id, out _);
            if (cancellationToken.IsCancellationRequested)
            {
                LogFile($"Pi 命令取消 id={id} type={type}", LogLevel.Normal);
                throw new OperationCanceledException(cancellationToken);
            }
            LogFile($"Pi 命令超时 id={id} type={type} timeoutMs={timeoutMs}", LogLevel.Error);
            throw new TimeoutException($"EW-AI Pi 命令超时：{type}");
        }

        private void WriteJsonLine(JObject message)
        {
            string text = message.ToString(Formatting.None);
            lock (writeLock)
            {
                if (stdin == null)
                {
                    throw new InvalidOperationException("EW-AI Pi stdin 未初始化。");
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
                    // .NET StreamReader.ReadLine 只按 \r\n / \n / \r 分行，不会像 Node readline
                    // 那样把 JSON 字符串内合法的 U+2028/U+2029 误判为换行，符合 Pi JSONL 分帧约定。
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogFile($"Pi 读取 stdout 失败 err={ex.Message}", LogLevel.Error);
                    Report("error", $"读取 EW-AI Pi 输出失败：{ex.Message}", null);
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

                HandleJsonLine(line);
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
                    LogFile($"Pi 读取 stderr 失败 err={ex.Message}", LogLevel.Error);
                    return;
                }

                if (line == null)
                {
                    return;
                }
                if (!string.IsNullOrWhiteSpace(line))
                {
                    LogFile($"Pi stderr: {line}", LogLevel.Normal);
                    Report("stderr", line, null);
                }
            }
        }

        private void HandleJsonLine(string line)
        {
            JObject message;
            try
            {
                message = JObject.Parse(line);
            }
            catch (Exception ex)
            {
                LogFile($"Pi stdout 非 JSON err={ex.Message} line={line}", LogLevel.Error);
                Report("error", $"EW-AI Pi 输出不是合法 JSON：{ex.Message}", null);
                return;
            }

            string type = message["type"]?.Value<string>();
            if (string.Equals(type, "response", StringComparison.Ordinal))
            {
                HandleResponse(message);
                return;
            }
            if (string.Equals(type, "extension_ui_request", StringComparison.Ordinal))
            {
                HandleExtensionUiRequest(message);
                return;
            }

            HandleEvent(type, message);
        }

        private void HandleResponse(JObject message)
        {
            string id = message["id"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(id)
                || !pendingRequests.TryRemove(id, out TaskCompletionSource<JObject> tcs))
            {
                LogFile("Pi<- 未关联响应", message, LogLevel.Normal);
                return;
            }

            if (message["success"]?.Value<bool>() == false)
            {
                string errorMessage = message["error"]?.Value<string>() ?? "EW-AI Pi 返回错误。";
                LogFile($"Pi<- 错误响应 id={id} message={errorMessage}", message, LogLevel.Error);
                tcs.TrySetResult(message);
                return;
            }

            LogFile($"Pi<- 响应 id={id}", message["data"], LogLevel.Normal);
            tcs.TrySetResult(message);
        }

        // 当前链路不安装 Pi 扩展；个别内置流程若发起交互式 UI 请求，统一取消，
        // 避免代理线程等待一个永远不会到来的应答。
        private void HandleExtensionUiRequest(JObject message)
        {
            string id = message["id"]?.Value<string>();
            string method = message["method"]?.Value<string>();
            LogFile($"Pi<- 扩展 UI 请求 method={method ?? "(空)"}", message, LogLevel.Normal);
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }
            bool expectsResponse = string.Equals(method, "select", StringComparison.Ordinal)
                || string.Equals(method, "confirm", StringComparison.Ordinal)
                || string.Equals(method, "input", StringComparison.Ordinal)
                || string.Equals(method, "editor", StringComparison.Ordinal);
            if (!expectsResponse)
            {
                return;
            }
            try
            {
                WriteJsonLine(new JObject
                {
                    ["type"] = "extension_ui_response",
                    ["id"] = id,
                    ["cancelled"] = true
                });
            }
            catch (Exception ex)
            {
                LogFile($"Pi 扩展 UI 应答写入失败 err={ex.Message}", LogLevel.Error);
            }
        }

        private void HandleEvent(string type, JObject message)
        {
            switch (type)
            {
                case "agent_start":
                    agentStreaming = true;
                    return;
                case "agent_settled":
                    agentStreaming = false;
                    SettlePrompt();
                    return;
                case "message_update":
                    HandleMessageUpdate(message);
                    return;
                case "message_end":
                    CaptureAssistantUsage(message);
                    return;
                case "tool_execution_start":
                    HandleToolExecutionStart(message);
                    return;
                case "tool_execution_end":
                    HandleToolExecutionEnd(message);
                    return;
                case "compaction_start":
                    ReportStatusMessage("正在压缩上下文…", message);
                    return;
                case "compaction_end":
                    if (message["aborted"]?.Value<bool>() == true)
                    {
                        ReportStatusMessage("上下文压缩已取消。", message);
                    }
                    else if (message["result"] == null || message["result"].Type == JTokenType.Null)
                    {
                        string compactionError = message["errorMessage"]?.Value<string>();
                        ReportStatusMessage(string.IsNullOrWhiteSpace(compactionError)
                            ? "上下文压缩失败。"
                            : "上下文压缩失败：" + compactionError, message);
                    }
                    else
                    {
                        ReportStatusMessage("上下文压缩完成。", message);
                    }
                    return;
                case "auto_retry_start":
                    ReportStatusMessage("模型请求失败，正在重试…", message);
                    return;
                case "auto_retry_end":
                    if (message["success"]?.Value<bool>() == false)
                    {
                        string finalError = message["finalError"]?.Value<string>() ?? "模型请求重试后仍失败。";
                        LogFile($"Pi<- 自动重试最终失败：{finalError}", message, LogLevel.Error);
                        Cancel();
                        promptCompletion?.TrySetException(new InvalidOperationException(finalError));
                    }
                    return;
                case "extension_error":
                    ReportStatusMessage("EW-AI 扩展错误：" + (message["error"]?.Value<string>() ?? "未知错误"), message);
                    return;
                default:
                    return;
            }
        }

        private void SettlePrompt()
        {
            TaskCompletionSource<JObject> completion = promptCompletion;
            if (completion == null)
            {
                return;
            }
            var result = new JObject
            {
                ["stopReason"] = "end_turn",
                ["sessionId"] = sessionId ?? string.Empty
            };
            JToken usage;
            lock (executionLock)
            {
                usage = currentTurnUsage?.DeepClone();
            }
            if (usage != null)
            {
                result["usage"] = usage;
            }
            completion.TrySetResult(result);
        }

        private void HandleMessageUpdate(JObject message)
        {
            JObject delta = message["assistantMessageEvent"] as JObject;
            string deltaType = delta?["type"]?.Value<string>();
            string deltaText = delta?["delta"]?.Value<string>();
            if (string.IsNullOrEmpty(deltaText))
            {
                return;
            }
            if (string.Equals(deltaType, "text_delta", StringComparison.Ordinal))
            {
                MarkFirstModelActivity();
                lock (executionLock)
                {
                    FlushReasoningTraceSegmentLocked("thought_segment", currentThoughtTraceSegment);
                    assistantResponse.Append(deltaText);
                    currentAssistantTraceSegment.Append(deltaText);
                }
                Report("assistant_chunk", deltaText, message);
            }
            else if (string.Equals(deltaType, "thinking_delta", StringComparison.Ordinal))
            {
                MarkFirstModelActivity();
                lock (executionLock)
                {
                    FlushReasoningTraceSegmentLocked("assistant_segment", currentAssistantTraceSegment);
                    currentThoughtTraceSegment.Append(deltaText);
                }
                Report("assistant_thought", deltaText, message);
            }
        }

        private void CaptureAssistantUsage(JObject message)
        {
            JToken usage = message["message"]?["usage"];
            if (usage == null || usage.Type == JTokenType.Null)
            {
                return;
            }
            if (!string.Equals(message["message"]?["role"]?.Value<string>(), "assistant", StringComparison.Ordinal))
            {
                return;
            }
            lock (executionLock)
            {
                currentTurnUsage = usage.DeepClone();
            }
        }

        private void HandleToolExecutionStart(JObject message)
        {
            lock (executionLock)
            {
                currentPromptToolCallCount++;
            }
            string callId = message["toolCallId"]?.Value<string>();
            string toolName = message["toolName"]?.Value<string>() ?? string.Empty;
            JObject args = message["args"] as JObject;
            string displayName = ResolveToolDisplayName(toolName, args);
            MarkFirstModelActivity();
            AppendReasoningTraceEvent("tool_call", displayName, message);
            RecordAnalysisToolStarted(callId, toolName, args, message);
            LogExecution("tool_call", displayName, message);
            Report("tool_call", displayName, message);
        }

        private void HandleToolExecutionEnd(JObject message)
        {
            string callId = message["toolCallId"]?.Value<string>();
            bool isError = message["isError"]?.Value<bool>() == true;
            string resultText = ExtractResultText(message);
            string summary;
            if (isError)
            {
                lock (executionLock)
                {
                    currentPromptToolErrorCount++;
                }
                summary = string.IsNullOrWhiteSpace(resultText)
                    ? "× 工具调用失败，Pi 未提供错误内容"
                    : "× " + SummarizeToolResultText(resultText).TrimStart('✓', ' ');
            }
            else
            {
                summary = SummarizeToolResultText(resultText);
            }
            RecordAnalysisToolFinished(callId, message, isError);
            AppendReasoningTraceEvent(isError ? "tool_error" : "tool_result", summary, message);
            // Raw 保留完整原始事件：前台从 result.content[].text 解析 previewId 等结构化内容。
            Report("tool_result", summary, message);
        }

        private void ReportStatusMessage(string text, JObject raw)
        {
            LogExecution("status_message", text, raw);
            Report("status_message", text, raw);
        }

        // Pi 内置工具与平台工具（经 bash 的 cli call 调用）→ 中文显示名映射。
        private static readonly Dictionary<string, string> toolDisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"bash", "执行命令"},
            {"read", "读取文件"},
            {"edit", "编辑文件"},
            {"write", "写入文件"},
            {"grep", "搜索内容"},
            {"find", "查找文件"},
            {"ls", "列出目录"},
            {"list_procs", "列出所有流程"},
            {"search_proc_catalog", "搜索流程目录"},
            {"get_proc_overview", "获取流程概览"},
            {"get_proc_detail", "获取流程详情"},
            {"get_op_detail", "获取指令详情"},
            {"get_op_details", "批量获取指令详情"},
            {"get_step_detail", "获取步骤详情"},
            {"get_operation_references", "获取指令跳转关系"},
            {"get_proc_references", "获取流程引用"},
            {"trace_resource", "追踪资源引用"},
            {"search_ops", "搜索指令"},
            {"list_operation_types", "列出指令类型"},
            {"get_operation_schema", "获取指令Schema"},
            {"get_operation_guide", "获取指令调用说明"},
            {"op_meta", "获取指令元数据"},
            {"get_reference_catalog", "获取引用目录"},
            {"get_semantic_operation_schema", "获取语义指令契约"},
            {"get_native_operation_schemas", "获取原生指令结构"},
            {"preview_change_set", "预演流程变更"},
            {"apply_change_set", "提交流程变更"},
            {"discard_change_set_preview", "丢弃流程变更预演"},
            {"get_runtime_snapshot", "获取运行时快照"},
            {"get_info_log_tail", "读取运行日志"},
            {"diagnose_proc", "诊断流程"},
            {"validate_proc", "校验流程"},
            {"run_proc_test", "有界测试流程"},
            {"start_proc", "启动流程"},
            {"stop_proc", "停止流程"},
            {"pause_proc", "暂停流程"},
            {"resume_proc", "继续流程"},
            {"get_snapshot", "获取平台快照"},
            {"list_variables", "列出变量"},
            {"search_variables", "搜索变量"},
            {"list_io", "列出 IO"},
            {"search_io", "搜索 IO"},
            {"list_alarms", "列出报警"},
            {"list_resources", "列出资源"}
        };

        // 从 bash 命令文本中提取平台工具名：`cli call <name>`。
        private static readonly Regex cliCallPattern = new Regex(
            @"\bcli\s+call\s+([A-Za-z0-9_]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string ExtractPlatformToolName(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }
            Match match = cliCallPattern.Match(command);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ResolveToolDisplayName(string toolName, JObject args)
        {
            if (string.Equals(toolName, "bash", StringComparison.Ordinal))
            {
                string platformTool = ExtractPlatformToolName(args?["command"]?.Value<string>());
                if (!string.IsNullOrEmpty(platformTool)
                    && toolDisplayNames.TryGetValue(platformTool, out string platformDisplay))
                {
                    return platformDisplay;
                }
            }
            if (!string.IsNullOrEmpty(toolName) && toolDisplayNames.TryGetValue(toolName, out string display))
            {
                return display;
            }
            return string.IsNullOrWhiteSpace(toolName) ? "调用工具" : toolName;
        }

        private static string ExtractResultText(JObject message)
        {
            JToken content = message?["result"]?["content"];
            if (!(content is JArray array) || array.Count == 0)
            {
                return null;
            }
            var builder = new StringBuilder();
            foreach (JToken entry in array)
            {
                JToken text = entry?["text"];
                if (text != null && text.Type == JTokenType.String)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append('\n');
                    }
                    builder.Append(text.Value<string>());
                }
            }
            return builder.Length == 0 ? null : builder.ToString();
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
                    return $"✓ {type}";
                }
                return raw.Length > 80 ? raw.Substring(0, 80) + " …" : raw;
            }
            catch
            {
                return raw.Length > 80 ? raw.Substring(0, 80) + " …" : raw;
            }
        }

        private void RecordAnalysisToolStarted(string callId, string toolName, JObject args, JObject raw)
        {
            DateTime startedUtc = DateTime.UtcNow;
            string platformTool = string.Equals(toolName, "bash", StringComparison.Ordinal)
                ? ExtractPlatformToolName(args?["command"]?.Value<string>())
                : null;
            string analysisToolName = platformTool ?? toolName ?? string.Empty;
            JObject argsSummary = AiAnalysisLogger.SummarizePayload(
                (JToken)args ?? JValue.CreateNull(), 12 * 1024);
            string signature = analysisToolName + ":" + (argsSummary["sha256"]?.Value<string>() ?? string.Empty);
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
                        ToolName = analysisToolName,
                        IsPlatformTool = !string.IsNullOrEmpty(platformTool),
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
                ["tool"] = analysisToolName,
                ["parallelGroup"] = parallelGroup,
                ["activeAtStart"] = activeAtStart,
                ["attempt"] = attempt,
                ["args"] = argsSummary
            }, startedUtc);
        }

        private void RecordAnalysisToolFinished(string callId, JObject message, bool transportFailed)
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
                        ToolName = message?["toolName"]?.Value<string>() ?? string.Empty,
                        IsPlatformTool = false,
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
            }

            long durationMs = Math.Max(0L, (long)(finishedUtc - state.StartedUtc).TotalMilliseconds);
            string rawResult = ExtractResultText(message);
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
            if (businessFailed && !transportFailed)
            {
                lock (executionLock)
                {
                    currentPromptToolErrorCount++;
                }
            }
            string status = transportFailed ? "transport_error" : businessFailed ? "business_error" : "ok";
            string stage = transportFailed ? "pi" : businessFailed ? "business" : string.Empty;
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
                    ["reachedBridge"] = transportFailed
                        ? (bool?)null
                        : state.IsPlatformTool ? (bool?)true : null,
                    ["sideEffects"] = resultObject?["recovery"]?["sideEffects"]?.Value<string>() ?? "unknown"
                }
            };
            if (!string.IsNullOrWhiteSpace(stage))
            {
                data["stage"] = stage;
            }
            if (resultValue.Type != JTokenType.Null)
            {
                data["result"] = AiAnalysisLogger.SummarizePayload(resultValue, resultBudget);
            }
            if (!string.Equals(status, "ok", StringComparison.Ordinal))
            {
                data["error"] = new JObject
                {
                    ["code"] = resultObject?["errorCode"]?.Value<string>()
                        ?? (transportFailed ? "PI_TOOL_EXECUTION_FAILED" : string.Empty),
                    ["message"] = resultObject?["message"]?.Value<string>()
                        ?? (transportFailed
                            ? (string.IsNullOrWhiteSpace(rawResult) ? "Pi 报告工具执行失败。" : rawResult)
                            : "工具返回业务失败。"),
                    ["recovery"] = resultObject?["recovery"]?.DeepClone()
                };
            }
            WriteAnalysisEvent("tool.finished", data, finishedUtc);
        }

        public static bool IsKnownTextOnlyImageConfiguration(string provider, string model)
        {
            string normalizedProvider = (provider ?? string.Empty).Trim();
            string normalizedModel = (model ?? string.Empty).Trim();
            return string.Equals(normalizedProvider, "deepseek", StringComparison.OrdinalIgnoreCase)
                || normalizedModel.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildPrompt(string prompt)
        {
            string context;
            if (IsRuntimeDiagnostic)
            {
                context = "当前 Automation 工具模式：RuntimeDiagnostic。当前会话只开放运行现场取证工具，不具备平台开发、流程运行或配置写入能力。";
            }
            else if (string.Equals(config.ToolProfile, "Diagnostic", StringComparison.Ordinal))
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
                string integrationVersionPath = Path.Combine(
                    PiContextProvisioner.AgentDirectory,
                    ".automation-context-version");
                return new JObject
                {
                    ["managedAvailable"] = PiContextProvisioner.IsManagedContextAvailable,
                    ["automation"] = BuildManagedFileAnalysis(
                        PiContextProvisioner.AppendSystemPromptPath,
                        integrationVersionPath,
                        PiContextProvisioner.IntegrationContextVersion),
                    ["toolsCliSkill"] = BuildSkillAnalysis(
                        PiContextProvisioner.ToolsCliSkillDirectory,
                        ".automation-tools-cli-skill-version",
                        PiContextProvisioner.ToolsCliSkillVersion),
                    ["processAuthoringSkill"] = BuildSkillAnalysis(
                        PiContextProvisioner.ProcessAuthoringSkillDirectory,
                        ".automation-skill-version",
                        PiContextProvisioner.ProcessAuthoringSkillVersion)
                };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["managedAvailable"] = PiContextProvisioner.IsManagedContextAvailable,
                    ["inspectionError"] = ex.Message
                };
            }
        }

        private static JObject BuildSkillAnalysis(string skillDirectory, string versionFileName, int bundledVersion)
        {
            JObject analysis = BuildManagedFileAnalysis(
                Path.Combine(skillDirectory, "SKILL.md"),
                Path.Combine(skillDirectory, versionFileName),
                bundledVersion);
            analysis["deployed"] = File.Exists(Path.Combine(skillDirectory, "SKILL.md"));
            return analysis;
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
        private string BuildSelectionContext()
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

        private JObject BuildSelectionAnalysis()
        {
            try
            {
                PlatformEditorSelection editorSelection = runtime.EditorUi?.GetSelection();
                List<Proc> processes = runtime.Stores.Processes.Items;
                if (editorSelection == null || processes == null)
                {
                    return new JObject { ["hasSelection"] = false };
                }
                int procIndex = editorSelection.ProcIndex;
                if (procIndex < 0 || procIndex >= processes.Count)
                {
                    return new JObject { ["hasSelection"] = false };
                }

                Proc proc = processes[procIndex];
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

                int stepIndex = editorSelection.StepIndex;
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

                    int opIndex = editorSelection.OperationIndex;
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

        // 编辑会话工作目录取工程根目录；运行诊断沿用 HMI 工作目录，不加载流程编写路由。
        private string ResolveWorkingDirectory()
        {
            if (!HmiDevelopmentSourceLocator.TryResolve(
                AppDomain.CurrentDomain.BaseDirectory,
                out HmiDevelopmentSource source,
                out string error))
            {
                throw new DirectoryNotFoundException(error);
            }
            if (IsRuntimeDiagnostic)
            {
                return source.SourceDirectory;
            }
            return source.ProjectRoot;
        }

        private void Report(string kind, string text, JObject raw)
        {
            try
            {
                EventReceived?.Invoke(new PiRpcEvent
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
                ["source"] = "pi",
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
                    ["source"] = "pi",
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
                    ["agentSessionId"] = sessionId ?? string.Empty,
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
                ["agentSessionId"] = sessionId ?? string.Empty,
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
                    string contentType = parentObject?["type"]?.Value<string>();
                    bool isAttachmentContent = string.Equals(contentType, "image", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(name, "data", StringComparison.OrdinalIgnoreCase);
                    if (isAttachmentContent)
                    {
                        int dataLength = property.Value?.Type == JTokenType.String
                            ? property.Value.Value<string>()?.Length ?? 0
                            : 0;
                        property.Value = $"[图片数据已省略，Base64长度={dataLength}]";
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
                AppendLogField(builder, "Pi 会话", record["agentSessionId"]);
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
                ?? record["callId"]?.Value<string>();
            string toolName = record["toolName"]?.Value<string>();

            var structured = new JObject
            {
                ["schemaVersion"] = 1,
                ["eventId"] = Guid.NewGuid().ToString("N"),
                ["timeUtc"] = timeUtc.ToString("O"),
                ["source"] = record["source"]?.Value<string>() ?? string.Empty,
                ["eventName"] = kind,
                ["auditSessionId"] = record["auditSessionId"]?.Value<string>() ?? string.Empty,
                ["agentSessionId"] = record["agentSessionId"]?.Value<string>() ?? string.Empty,
                ["promptId"] = record["promptId"]?.Value<string>() ?? string.Empty
            };
            AddStructuredString(structured, "toolCallId", toolCallId);
            AddStructuredString(structured, "toolName", toolName);
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

            public bool IsPlatformTool { get; set; }

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
            LogFile("Pi Dispose 开始", LogLevel.Normal);
            try
            {
                Cancel();
            }
            catch
            {
            }

            promptCompletion?.TrySetCanceled();
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
