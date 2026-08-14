// 模块：编辑器 / AI。
// 职责范围：AI 前台、ACP 会话、预演确认与对话渲染。
// 排查入口：按子进程启动、JSON-RPC initialize/session/prompt、MCP 调用和取消四段定位，完整证据看 AIExecution 日志。

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
using Automation.Protocol;

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
        private static readonly string[] capabilityBuiltinExtensionNames =
            { "developer", "skills", "tom" };

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
        private readonly StringBuilder finalAssistantResponse = new StringBuilder();
        private readonly StringBuilder currentAssistantTraceSegment = new StringBuilder();
        private readonly StringBuilder currentThoughtTraceSegment = new StringBuilder();
        private readonly Dictionary<string, AnalysisToolCallState> activeAnalysisToolCalls =
            new Dictionary<string, AnalysisToolCallState>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> analysisToolAttempts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> currentPromptToolNames =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly List<AnalysisTimeInterval> analysisToolIntervals =
            new List<AnalysisTimeInterval>();
        private readonly Dictionary<string, JObject> availableGooseExtensions =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ReviewVerifiedFactDefinition>> latestReviewFactsBySubject =
            new Dictionary<string, List<ReviewVerifiedFactDefinition>>(StringComparer.Ordinal);
        private readonly Dictionary<int, string> latestReviewProcIdsByIndex =
            new Dictionary<int, string>();
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
        private long currentPromptToolResultBytes;
        private bool currentPromptAutomationToolSucceeded;
        private bool currentPromptCurrentStateReadSucceeded;
        private bool currentPromptConfigurationSaved;
        private bool currentPromptChangeSetCommitted;
        private bool currentPromptMigrationCommitted;
        private bool currentPromptPreviewRejected;
        private bool currentPromptSourceFilesChanged;
        private bool currentPromptDesignKnowledgeReadSucceeded;
        private bool currentPromptUnsafeMutationFailure;
        private bool currentPromptSourceMutationUncertain;
        private bool currentPromptMutationAttempted;
        private bool currentPromptPreviewCreated;
        private bool currentPromptVerificationSucceeded;
        private bool currentPromptDecisionTerminationSent;
        private long cumulativeInputTokens;
        private long lastPromptInputTokens;
        private long lastTurnToolResultBytes;
        private TaskCapabilityDecisionDefinition lastSubmittedTaskDecision;
        private int currentPromptTaskDecisionCount;
        private AiTurnEvidence lastTurnEvidence = AiTurnEvidence.Empty;
        private AiStageArtifact currentPromptStageArtifact = AiStageArtifact.Empty;
        private AiStageArtifact lastTurnArtifact = AiStageArtifact.Empty;
        private int currentAnalysisSequence;
        private int currentParallelGroup;
        private int currentMaxConcurrentTools;
        private int currentParameterFailureCount;
        private long currentPreviewWaitMs;
        private DateTime lastAnalysisToolStartedUtc;
        private DateTime currentFirstModelActivityUtc;
        private DateTime currentFirstPreviewAttemptUtc;
        private DateTime currentFirstSuccessfulPreviewUtc;
        private int currentToolFailuresAtFirstSuccessfulPreview;
        private int currentAuthoringInputResolutionCalls;
        private int currentOperationCapabilityResolutionCalls;
        private bool supportsImagePrompt;
        private bool capabilityStateInvalid;
        private bool disposed;

        private readonly PlatformRuntime runtime;

        public GooseAcpClient(PlatformRuntime runtime, GooseConfig config,
            string restoredConversationContext = null)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.restoredConversationContext = restoredConversationContext;
        }

        public event Action<GooseAcpEvent> EventReceived;

        public Func<JObject, JObject> PermissionRequestHandler { get; set; }

        public string SessionId => sessionId;

        public string ToolProfile => config.ToolProfile;

        public bool HasLiveSession => !capabilityStateInvalid
            && !string.IsNullOrWhiteSpace(sessionId)
            && process != null
            && !process.HasExited;

        internal AiTurnEvidence LastTurnEvidence
        {
            get
            {
                lock (executionLock) return lastTurnEvidence;
            }
        }

        internal AiStageArtifact LastTurnArtifact
        {
            get
            {
                lock (executionLock) return lastTurnArtifact;
            }
        }

        internal TaskCapabilityDecisionDefinition LastSubmittedTaskDecision
        {
            get
            {
                lock (executionLock) return lastSubmittedTaskDecision;
            }
        }

        internal int LastTaskDecisionSubmissionCount
        {
            get
            {
                lock (executionLock) return currentPromptTaskDecisionCount;
            }
        }

        internal bool HasLockedTaskDecision
        {
            get
            {
                lock (executionLock) return lastSubmittedTaskDecision != null;
            }
        }

        internal long CumulativeInputTokens
        {
            get
            {
                lock (executionLock) return cumulativeInputTokens;
            }
        }

        internal long LastPromptInputTokens
        {
            get
            {
                lock (executionLock) return lastPromptInputTokens;
            }
        }

        internal long LastTurnToolResultBytes
        {
            get
            {
                lock (executionLock) return lastTurnToolResultBytes;
            }
        }

        internal int LastTurnToolCallCount
        {
            get
            {
                lock (executionLock) return currentPromptToolCallCount;
            }
        }

        internal int ContextWindowTokens
        {
            get
            {
                AiModelServiceConfig service = GooseConfigStorage.FindModelService(config);
                return service?.ContextLimit is int configured && configured > 0
                    ? configured
                    : 128000;
            }
        }

        internal int ReservedOutputTokens => Math.Max(8192, config.MaxOutputTokens);

        public string LastAssistantResponse
        {
            get
            {
                lock (executionLock)
                {
                    return BuildFinalAssistantResponseLocked();
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
            capabilityStateInvalid = false;
            try
            {
                await ReconcileBuiltinExtensionsAsync(config.ToolProfile, cancellationToken)
                    .ConfigureAwait(false);
                await VerifyCapabilitySurfaceAsync(config.ToolProfile, cancellationToken)
                    .ConfigureAwait(false);
                LogVerifiedCapabilitySurface(config.ToolProfile);
            }
            catch (Exception ex)
            {
                capabilityStateInvalid = true;
                LogCapabilitySurfaceFailure(config.ToolProfile, ex);
                throw;
            }
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
                currentPromptToolResultBytes = 0L;
                lastPromptInputTokens = 0L;
                currentPromptAutomationToolSucceeded = false;
                currentPromptCurrentStateReadSucceeded = false;
                currentPromptConfigurationSaved = false;
                currentPromptChangeSetCommitted = false;
                currentPromptMigrationCommitted = false;
                currentPromptPreviewRejected = false;
                currentPromptSourceFilesChanged = false;
                currentPromptDesignKnowledgeReadSucceeded = false;
                currentPromptUnsafeMutationFailure = false;
                currentPromptSourceMutationUncertain = false;
                currentPromptMutationAttempted = false;
                currentPromptPreviewCreated = false;
                currentPromptVerificationSucceeded = false;
                currentPromptDecisionTerminationSent = false;
                lastSubmittedTaskDecision = null;
                currentPromptTaskDecisionCount = 0;
                lastTurnEvidence = AiTurnEvidence.Empty;
                currentPromptStageArtifact = AiStageArtifact.Empty;
                lastTurnArtifact = AiStageArtifact.Empty;
                currentAnalysisSequence = 0;
                currentParallelGroup = 0;
                currentMaxConcurrentTools = 0;
                currentParameterFailureCount = 0;
                currentPreviewWaitMs = 0;
                lastAnalysisToolStartedUtc = default(DateTime);
                currentFirstModelActivityUtc = default(DateTime);
                currentFirstPreviewAttemptUtc = default(DateTime);
                currentFirstSuccessfulPreviewUtc = default(DateTime);
                currentToolFailuresAtFirstSuccessfulPreview = 0;
                currentAuthoringInputResolutionCalls = 0;
                currentOperationCapabilityResolutionCalls = 0;
                activeAnalysisToolCalls.Clear();
                analysisToolAttempts.Clear();
                currentPromptToolNames.Clear();
                analysisToolIntervals.Clear();
                assistantResponse.Clear();
                finalAssistantResponse.Clear();
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

                long inputTokens = promptResult?["usage"]?["inputTokens"]?.Value<long?>()
                    ?? promptResult?["usage"]?["input_tokens"]?.Value<long?>()
                    ?? 0L;
                lock (executionLock)
                {
                    lastPromptInputTokens = Math.Max(0L, inputTokens);
                    cumulativeInputTokens += lastPromptInputTokens;
                }

                LogExecution("prompt_completed", promptResult["stopReason"]?.Value<string>() ?? "unknown", promptResult);
                Report("lifecycle", $"EW-AI 本轮结束：{promptResult["stopReason"]?.Value<string>() ?? "unknown"}", promptResult);
                return promptResult;
            }
            catch (Exception ex)
            {
                bool terminatedByDecision;
                lock (executionLock)
                    terminatedByDecision = currentPromptDecisionTerminationSent
                        && lastSubmittedTaskDecision != null;
                if (terminatedByDecision && !cancellationToken.IsCancellationRequested)
                {
                    promptResult = new JObject
                    {
                        ["stopReason"] = "capability_decision_locked",
                        ["usage"] = new JObject()
                    };
                    LogExecution("prompt_completed", "capability_decision_locked", promptResult);
                    return promptResult;
                }
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
                    response = BuildFinalAssistantResponseLocked();
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
                    lastTurnEvidence = BuildTurnEvidenceLocked();
                    lastTurnArtifact = currentPromptStageArtifact;
                    lastTurnToolResultBytes = currentPromptToolResultBytes;
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
        /// 在当前 Goose 原生会话内重新挂载 Automation MCP。会话历史和 sessionId 保持不变。
        /// 返回 false 表示会话尚未创建，session/new 会直接使用更新后的地址。
        /// </summary>
        public async Task<bool> ReloadAutomationExtensionAsync(string mcpUri, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(mcpUri))
                throw new InvalidOperationException("MCP地址不能为空。");

            string activeSessionId = sessionId;
            if (string.IsNullOrWhiteSpace(activeSessionId)) return false;
            if (!HasLiveSession)
                throw new InvalidOperationException("Goose进程或会话已失效，无法原地刷新工具。");

            string previousMcpUri = config.McpUri;
            Exception removeError = null;
            try
            {
                await RemoveSessionExtensionAsync("automation", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                removeError = ex;
            }

            try
            {
                await AddAutomationExtensionAsync(mcpUri, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception addError)
            {
                Exception rollbackError = null;
                if (removeError == null && !string.IsNullOrWhiteSpace(previousMcpUri))
                {
                    try
                    {
                        await AddAutomationExtensionAsync(previousMcpUri, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        rollbackError = ex;
                    }
                }
                string detail = removeError == null
                    ? addError.Message
                    : $"卸载失败：{removeError.Message}；挂载失败：{addError.Message}";
                if (rollbackError != null)
                    detail += "；恢复旧工具失败：" + rollbackError.Message;
                throw new InvalidOperationException(
                    "Goose 会话内 Automation MCP 切换失败，已停止当前任务且不会静默重建会话。" + detail,
                    addError);
            }

            if (removeError != null)
            {
                throw new InvalidOperationException(
                    "Automation MCP 新端点已挂载，但旧扩展未能确认卸载；当前工具面不可信，已停止任务。"
                    + removeError.Message,
                    removeError);
            }
            EnsureSessionIdentity(activeSessionId);
            Report("lifecycle", $"Automation MCP 已在原会话内重新挂载：{activeSessionId}", null);
            return true;
        }

        public bool CanSwitchTaskCapabilityInPlace(string targetProfile)
        {
            AutomationToolProfiles.Normalize(targetProfile);
            if (capabilityStateInvalid) return false;
            if (string.IsNullOrWhiteSpace(sessionId))
                return process == null || !process.HasExited;
            return HasLiveSession;
        }

        public async Task ConfigureTaskCapabilityAsync(
            string targetProfile,
            string mcpUri,
            string notice,
            CancellationToken cancellationToken)
        {
            string normalized = AutomationToolProfiles.Normalize(targetProfile);
            bool enteringProcessReview = string.Equals(
                    normalized, AutomationToolProfiles.ProcessReview, StringComparison.Ordinal)
                && !string.Equals(
                    config.ToolProfile, AutomationToolProfiles.ProcessReview, StringComparison.Ordinal);
            if (enteringProcessReview)
            {
                lock (executionLock)
                {
                    latestReviewFactsBySubject.Clear();
                    latestReviewProcIdsByIndex.Clear();
                }
            }
            if (!CanSwitchTaskCapabilityInPlace(normalized))
                throw new InvalidOperationException("当前 Goose 原生会话已经失效，不能继续热切换能力包。");

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                config.ToolProfile = normalized;
                config.McpUri = mcpUri;
                config.TaskCapabilityNotice = notice;
                return;
            }

            string activeSessionId = sessionId;
            bool endpointChanged = !string.Equals(config.McpUri, mcpUri, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(config.ToolProfile, normalized, StringComparison.Ordinal);
            try
            {
                if (endpointChanged)
                    await ReloadAutomationExtensionAsync(mcpUri, cancellationToken).ConfigureAwait(false);
                await ReconcileBuiltinExtensionsAsync(normalized, cancellationToken).ConfigureAwait(false);
                await VerifyCapabilitySurfaceAsync(normalized, cancellationToken).ConfigureAwait(false);
                EnsureSessionIdentity(activeSessionId);
                config.ToolProfile = normalized;
                config.McpUri = mcpUri;
                config.TaskCapabilityNotice = notice;
                LogVerifiedCapabilitySurface(normalized);
            }
            catch (Exception ex)
            {
                capabilityStateInvalid = true;
                LogCapabilitySurfaceFailure(normalized, ex);
                throw;
            }

            Report("lifecycle", $"原生会话能力已切换：{normalized}，sessionId={activeSessionId}", null);
        }

        private void LogVerifiedCapabilitySurface(string profile)
        {
            AiAnalysisLogger.Write(new JObject
            {
                ["event"] = "capability.surface.verified",
                ["auditSessionId"] = auditSessionId,
                ["gooseSessionId"] = sessionId ?? string.Empty,
                ["profile"] = profile ?? string.Empty,
                ["mcpUri"] = config.McpUri ?? string.Empty
            });
        }

        private void LogCapabilitySurfaceFailure(string profile, Exception error)
        {
            AiAnalysisLogger.Write(new JObject
            {
                ["event"] = "capability.surface.failed",
                ["auditSessionId"] = auditSessionId,
                ["gooseSessionId"] = sessionId ?? string.Empty,
                ["profile"] = profile ?? string.Empty,
                ["mcpUri"] = config.McpUri ?? string.Empty,
                ["error"] = error?.Message ?? string.Empty
            });
        }

        private async Task AddAutomationExtensionAsync(
            string mcpUri,
            CancellationToken cancellationToken)
        {
            await AddSessionExtensionAsync(new JObject
            {
                ["type"] = "mcp",
                ["server"] = new JObject
                {
                    ["name"] = "automation",
                    ["type"] = "http",
                    ["url"] = mcpUri.Trim(),
                    ["headers"] = new JArray()
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task ReconcileBuiltinExtensionsAsync(
            string profile,
            CancellationToken cancellationToken)
        {
            string normalized = AutomationToolProfiles.Normalize(profile);
            bool guidanceEnabled = !string.Equals(
                    normalized, AutomationToolProfiles.TaskCoordinator, StringComparison.Ordinal)
                && !string.Equals(
                    normalized, AutomationToolProfiles.RuntimeDiagnostic, StringComparison.Ordinal);
            bool developerEnabled = AutomationToolProfiles.UsesDeveloperTools(normalized);
            var desiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (guidanceEnabled)
            {
                desiredNames.Add("skills");
                desiredNames.Add("tom");
            }
            if (developerEnabled) desiredNames.Add("developer");

            Dictionary<string, JObject> active = await GetSessionExtensionsAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (string managedName in capabilityBuiltinExtensionNames)
            {
                bool desired = desiredNames.Contains(managedName);
                if (!active.TryGetValue(managedName, out JObject current))
                {
                    if (desired)
                    {
                        JObject definition = await GetAvailableExtensionAsync(
                            managedName, cancellationToken).ConfigureAwait(false);
                        ApplyCapabilityToolFilter(definition, managedName, normalized);
                        await AddSessionExtensionAsync(definition, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    continue;
                }

                if (!desired)
                {
                    await RemoveSessionExtensionAsync(managedName, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(managedName, "developer", StringComparison.OrdinalIgnoreCase)
                    && !HasExpectedDeveloperFilter(current, normalized))
                {
                    await RemoveSessionExtensionAsync(managedName, cancellationToken)
                        .ConfigureAwait(false);
                    JObject definition = await GetAvailableExtensionAsync(
                        managedName, cancellationToken).ConfigureAwait(false);
                    ApplyCapabilityToolFilter(definition, managedName, normalized);
                    await AddSessionExtensionAsync(definition, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        private async Task<JObject> GetAvailableExtensionAsync(
            string name,
            CancellationToken cancellationToken)
        {
            if (!availableGooseExtensions.TryGetValue(name, out JObject cached))
            {
                JObject result = await SendRequestAsync(
                    "_goose/unstable/extensions/available",
                    new JObject(),
                    SessionTimeoutMs,
                    cancellationToken).ConfigureAwait(false);
                foreach (JToken item in result["extensions"] as JArray ?? new JArray())
                {
                    JObject extension = ExtractExtension(item);
                    string extensionName = ReadExtensionName(extension);
                    if (!string.IsNullOrWhiteSpace(extensionName))
                        availableGooseExtensions[extensionName] = extension;
                }
                availableGooseExtensions.TryGetValue(name, out cached);
            }
            if (cached == null)
                throw new InvalidOperationException($"Goose 未公布内置扩展：{name}。");
            return (JObject)cached.DeepClone();
        }

        private async Task<Dictionary<string, JObject>> GetSessionExtensionsAsync(
            CancellationToken cancellationToken)
        {
            JObject result = await SendRequestAsync(
                "_goose/unstable/session/extensions/list",
                new JObject { ["sessionId"] = sessionId },
                SessionTimeoutMs,
                cancellationToken).ConfigureAwait(false);
            var extensions = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken item in result["extensions"] as JArray ?? new JArray())
            {
                JObject extension = ExtractExtension(item);
                string name = ReadExtensionName(extension);
                if (!string.IsNullOrWhiteSpace(name)) extensions[name] = extension;
            }
            return extensions;
        }

        private async Task AddSessionExtensionAsync(
            JObject extension,
            CancellationToken cancellationToken)
        {
            await SendRequestAsync(
                "_goose/unstable/session/extensions/add",
                new JObject
                {
                    ["sessionId"] = sessionId,
                    ["extension"] = extension
                },
                SessionTimeoutMs,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task RemoveSessionExtensionAsync(
            string name,
            CancellationToken cancellationToken)
        {
            await SendRequestAsync(
                "_goose/unstable/session/extensions/remove",
                new JObject
                {
                    ["sessionId"] = sessionId,
                    ["name"] = name
                },
                SessionTimeoutMs,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task VerifyCapabilitySurfaceAsync(
            string profile,
            CancellationToken cancellationToken)
        {
            string normalized = AutomationToolProfiles.Normalize(profile);
            Dictionary<string, JObject> extensions = await GetSessionExtensionsAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!extensions.ContainsKey("automation"))
                throw new InvalidOperationException("Goose 能力切换后未发现 Automation MCP 扩展。");

            bool expectsGuidance = !string.Equals(
                    normalized, AutomationToolProfiles.TaskCoordinator, StringComparison.Ordinal)
                && !string.Equals(
                    normalized, AutomationToolProfiles.RuntimeDiagnostic, StringComparison.Ordinal);
            if (extensions.ContainsKey("skills") != expectsGuidance
                || extensions.ContainsKey("tom") != expectsGuidance)
            {
                throw new InvalidOperationException("Goose 指导扩展与当前能力包不一致。");
            }

            bool expectsDeveloper = AutomationToolProfiles.UsesDeveloperTools(normalized);
            if (extensions.ContainsKey("developer") != expectsDeveloper)
                throw new InvalidOperationException("Goose Developer 扩展与当前能力包不一致。");
            if (expectsDeveloper
                && !HasExpectedDeveloperFilter(extensions["developer"], normalized))
                throw new InvalidOperationException("Goose Developer 工具白名单与当前能力包不一致。");

            HashSet<string> automationTools = await GetExtensionToolNamesAsync(
                "automation", cancellationToken).ConfigureAwait(false);
            bool hasControlTool = automationTools
                .Any(name => string.Equals(name, "request_capability", StringComparison.Ordinal)
                    || name.EndsWith("__request_capability", StringComparison.Ordinal));
            if (!hasControlTool)
                throw new InvalidOperationException("当前 Automation 工具面缺少 request_capability 控制工具。");

            if (expectsDeveloper)
            {
                HashSet<string> developerTools = await GetExtensionToolNamesAsync(
                    "developer", cancellationToken).ConfigureAwait(false);
                var normalizedNames = new HashSet<string>(
                    developerTools.Select(NormalizeExtensionToolName),
                    StringComparer.OrdinalIgnoreCase);
                AiAnalysisLogger.Write(new JObject
                {
                    ["event"] = "capability.surface.snapshot",
                    ["auditSessionId"] = auditSessionId,
                    ["gooseSessionId"] = sessionId ?? string.Empty,
                    ["profile"] = normalized,
                    ["extension"] = "developer",
                    ["configuredTools"] = extensions["developer"]?["availableTools"]?.DeepClone()
                        ?? extensions["developer"]?["available_tools"]?.DeepClone()
                        ?? new JArray(),
                    ["rawToolNames"] = new JArray(developerTools.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)),
                    ["normalizedToolNames"] = new JArray(normalizedNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                });
                if (string.Equals(normalized, AutomationToolProfiles.SourceReview, StringComparison.Ordinal))
                {
                    string[] missing = new[] { "read", "tree" }
                        .Where(name => !normalizedNames.Contains(name))
                        .ToArray();
                    if (missing.Length > 0)
                    {
                        throw new InvalidOperationException(
                            "源码只读能力缺少 Developer 工具：" + string.Join(",", missing)
                            + "；实际目录：" + string.Join(",", developerTools.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));
                    }
                    if (normalizedNames.Any(name => !string.Equals(name, "read", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(name, "tree", StringComparison.OrdinalIgnoreCase)))
                    {
                        AiAnalysisLogger.Write(new JObject
                        {
                            ["event"] = "capability.surface.catalog_unfiltered",
                            ["auditSessionId"] = auditSessionId,
                            ["gooseSessionId"] = sessionId ?? string.Empty,
                            ["profile"] = normalized,
                            ["extension"] = "developer",
                            ["note"] = "tools/list 返回扩展目录；有效权限继续由 available_tools 与客户端权限闸门共同限制。",
                            ["extraCatalogTools"] = new JArray(normalizedNames
                                .Where(name => !string.Equals(name, "read", StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(name, "tree", StringComparison.OrdinalIgnoreCase))
                                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                        });
                    }
                }
            }
        }

        private static string NormalizeExtensionToolName(string name)
        {
            string value = (name ?? string.Empty).Trim();
            int doubleUnderscore = value.LastIndexOf("__", StringComparison.Ordinal);
            int slash = value.LastIndexOf('/');
            int dot = value.LastIndexOf('.');
            int colon = value.LastIndexOf(':');
            int separator = Math.Max(doubleUnderscore, Math.Max(slash, Math.Max(dot, colon)));
            if (separator < 0) return value;
            return value.Substring(separator + (separator == doubleUnderscore ? 2 : 1));
        }

        private async Task<HashSet<string>> GetExtensionToolNamesAsync(
            string extensionName,
            CancellationToken cancellationToken)
        {
            JObject result = await SendRequestAsync(
                "_goose/unstable/tools/list",
                new JObject
                {
                    ["sessionId"] = sessionId,
                    ["extensionName"] = extensionName
                },
                SessionTimeoutMs,
                cancellationToken).ConfigureAwait(false);
            return new HashSet<string>(
                (result["tools"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .Select(item => item["name"]?.Value<string>() ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
        }

        private static JObject ExtractExtension(JToken item)
        {
            JObject value = item as JObject ?? new JObject();
            return value["extension"] as JObject ?? value;
        }

        private static string ReadExtensionName(JObject extension)
        {
            return extension?["name"]?.Value<string>()
                ?? extension?["server"]?["name"]?.Value<string>();
        }

        private static void ApplyCapabilityToolFilter(
            JObject extension,
            string name,
            string profile)
        {
            if (!string.Equals(name, "developer", StringComparison.OrdinalIgnoreCase)) return;
            extension.Remove("available_tools");
            extension.Remove("availableTools");
            if (string.Equals(profile, AutomationToolProfiles.SourceReview, StringComparison.Ordinal))
                extension["available_tools"] = new JArray("read", "tree");
        }

        private static bool HasExpectedDeveloperFilter(JObject extension, string profile)
        {
            JArray configured = extension?["availableTools"] as JArray
                ?? extension?["available_tools"] as JArray;
            if (!string.Equals(profile, AutomationToolProfiles.SourceReview, StringComparison.Ordinal))
                return configured == null || configured.Count == 0;
            var names = new HashSet<string>(
                (configured ?? new JArray()).Values<string>(),
                StringComparer.OrdinalIgnoreCase);
            return names.SetEquals(new[] { "read", "tree" });
        }

        private void EnsureSessionIdentity(string expectedSessionId)
        {
            if (!string.Equals(sessionId, expectedSessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("能力切换期间 Goose sessionId 发生变化，已拒绝继续。");
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

            bool runtimeDiagnostic = string.Equals(
                config.ToolProfile, AutomationToolProfiles.RuntimeDiagnostic, StringComparison.Ordinal);
            string sessionWorkingDirectory = ResolveWorkingDirectory();
            string skillProvisionMessage = null;
            if (!runtimeDiagnostic
                && !GooseRuntimeProvisioner.TryEnsureProcessSkills(
                    sessionWorkingDirectory,
                    out skillProvisionMessage))
            {
                throw new InvalidOperationException(skillProvisionMessage);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = config.GooseExecutablePath,
                // 所有业务能力均在 session/new 后通过 Goose 会话扩展接口动态挂载；
                // 进程启动参数不再固化某个能力包，保证切换时保留同一个原生会话。
                Arguments = "acp",
                WorkingDirectory = sessionWorkingDirectory,
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
            string developerShellPath = runtimeDiagnostic ? null : ResolveGooseDeveloperShellPath();
            if (!string.IsNullOrWhiteSpace(developerShellPath))
            {
                startInfo.EnvironmentVariables["GOOSE_SHELL"] = developerShellPath;
            }
            // Hmi 是客户可修改目录，不从其中加载平台内部规范。
            // Automation 专用上下文由 TOM 注入编辑会话；运行诊断会话不加载流程编写路由。
            startInfo.EnvironmentVariables["CONTEXT_FILE_NAMES"] = "[]";
            if (!File.Exists(GooseRuntimeProvisioner.IntegrationContextPath))
            {
                throw new FileNotFoundException("Automation 专用 Goose 上下文不存在。",
                    GooseRuntimeProvisioner.IntegrationContextPath);
            }
            if (!runtimeDiagnostic)
            {
                startInfo.EnvironmentVariables["GOOSE_MOIM_MESSAGE_FILE"] =
                    GooseRuntimeProvisioner.IntegrationContextPath;
            }
            else
            {
                startInfo.EnvironmentVariables.Remove("GOOSE_MOIM_MESSAGE_FILE");
                startInfo.EnvironmentVariables.Remove("GOOSE_MOIM_MESSAGE_TEXT");
            }

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
            startupInfo.Append(" cwd=").Append(sessionWorkingDirectory);
            startupInfo.Append(" mcpUri=").Append(config.McpUri);
            startupInfo.Append(" sessionName=").Append(runtimeSessionName);
            startupInfo.Append(" developerShell=").Append(developerShellPath ?? "cmd");
            if (!runtimeDiagnostic)
            {
                startupInfo.Append(" builtinExtensions=dynamic-session-scope");
                startupInfo.Append(" automationContextInjection=dynamic-tom");
                startupInfo.Append(" processAuthoringSkill=")
                    .Append(GooseRuntimeProvisioner.ProcessAuthoringSkillPath);
                startupInfo.Append(" processReviewSkill=")
                    .Append(GooseRuntimeProvisioner.ProcessReviewSkillPath);
            }
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
            if (!string.IsNullOrWhiteSpace(skillProvisionMessage))
            {
                LogFile(skillProvisionMessage, LogLevel.Normal);
            }
            Report("lifecycle", $"EW-AI ACP 进程已启动：{config.GooseExecutablePath} {startInfo.Arguments}", null);
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
                    // ACP 会把 Markdown 空行作为独立文本片段发送；只过滤真正的空字符串。
                    if (!string.IsNullOrEmpty(chunkText))
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
                    if (!string.IsNullOrEmpty(thoughtText))
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
            {"automation__resolve_proc_target", "定位流程目标"},
            {"automation__discover_project_resources", "批量发现项目资源"},
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
            {"automation__get_native_operation_field_contract", "获取原生字段契约"},
            {"automation__get_operation_guide", "获取指令调用说明"},
            {"automation__get_process_design_guide", "获取流程设计指南"},
            {"automation__get_platform_development_context", "获取平台开发上下文"},
            {"automation__search_platform_source", "受限检索平台源码"},
            {"automation__request_capability", "申请任务能力"},
            {"automation__op_meta", "获取指令元数据"},
            {"automation__get_reference_catalog", "获取引用目录"},
            {"automation__get_semantic_operation_schema", "获取语义指令契约"},
            {"automation__preview_change_set", "预演流程变更"},
            {"automation__preview_process_blueprint", "预演新流程蓝图"},
            {"automation__apply_change_set", "提交流程变更"},
            {"automation__discard_change_set_preview", "丢弃流程变更预演"},
            {"automation__get_runtime_snapshot", "获取运行时快照"},
            {"automation__get_info_log_tail", "读取运行日志"},
            {"automation__diagnose_proc", "诊断流程"},
            {"automation__validate_proc", "校验流程"},
            {"automation__run_proc_test", "有界测试流程"},
            {"automation__start_proc", "启动流程"},
            {"automation__stop_proc", "停止流程"},
            {"automation__pause_proc", "暂停流程"},
            {"automation__resume_proc", "继续流程"},
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
            {"preview.confirm", "预演确认"},
            {"change_set.preview", "流程变更预演"},
            {"change_set.apply", "流程变更提交"},
            {"change_set.native_field_contract", "原生字段契约"},
            {"migration.preview", "平台配置迁移预演"},
            {"migration.apply", "平台配置迁移提交"},
            {"proc.control", "流程控制"},
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
                if (!string.IsNullOrWhiteSpace(toolName)) currentPromptToolNames.Add(toolName);
                if (IsPreviewTool(toolName) && currentFirstPreviewAttemptUtc == default(DateTime))
                    currentFirstPreviewAttemptUtc = startedUtc;
                if (string.Equals(toolName, "resolve_authoring_inputs", StringComparison.Ordinal))
                    currentAuthoringInputResolutionCalls++;
                if (string.Equals(toolName, "resolve_operation_capability", StringComparison.Ordinal))
                    currentOperationCapabilityResolutionCalls++;
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
                CaptureFinalAssistantResponseBeforeToolLocked(toolName);
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
            string transportMessage = transportFailed
                ? FindFirstString(parameters, "message", "error", "text")
                : null;
            string transportCode = transportFailed
                && transportMessage?.IndexOf("未开放工具", StringComparison.Ordinal) >= 0
                    ? "TOOL_NOT_AVAILABLE"
                    : transportFailed ? "ACP_TOOL_CALL_FAILED" : string.Empty;
            string reportedSideEffects = parameterGenerationFailed
                ? "none"
                : transportCode == "TOOL_NOT_AVAILABLE"
                    ? "none"
                    : resultObject?["recovery"]?["sideEffects"]?.Value<string>() ?? "unknown";
            if (!string.IsNullOrEmpty(rawResult))
            {
                lock (executionLock)
                {
                    currentPromptToolResultBytes += Encoding.UTF8.GetByteCount(rawResult);
                }
            }
            if (businessFailed)
            {
                lock (executionLock)
                {
                    currentPromptToolErrorCount++;
                }
            }
            bool terminateDecisionTurn = false;
            if (!parameterGenerationFailed && IsMutationAttemptTool(state.ToolName))
            {
                lock (executionLock) currentPromptMutationAttempted = true;
            }
            if (!parameterGenerationFailed && !transportFailed && !businessFailed && state.IsAutomationMcp)
            {
                JObject resultData = resultObject?["data"] as JObject;
                string resultType = resultObject?["type"]?.Value<string>() ?? string.Empty;
                lock (executionLock)
                {
                    currentPromptAutomationToolSucceeded = true;
                    if (IsCurrentStateEvidenceTool(state.ToolName))
                    {
                        currentPromptCurrentStateReadSucceeded = true;
                        if (currentPromptConfigurationSaved
                            || currentPromptChangeSetCommitted
                            || currentPromptMigrationCommitted)
                            currentPromptVerificationSucceeded = true;
                    }
                    if (string.Equals(state.ToolName, "get_process_design_guide", StringComparison.Ordinal))
                        currentPromptDesignKnowledgeReadSucceeded = true;
                    if (string.Equals(config.ToolProfile, AutomationToolProfiles.ProcessReview, StringComparison.Ordinal))
                    {
                        CaptureReviewObjectIdentityLocked(state.ToolName, resultData);
                        CaptureReviewVerifiedFactsLocked(
                            state.ToolName,
                            state.ToolCallId,
                            resultData,
                            rawResult);
                    }
                    if (resultData?["configurationSaved"]?.Value<bool>() == true)
                        currentPromptConfigurationSaved = true;
                    if (string.Equals(resultType, "change_set.apply", StringComparison.Ordinal)
                        && string.Equals(resultData?["status"]?.Value<string>(), "committed", StringComparison.Ordinal)
                        && resultData?["configurationSaved"]?.Value<bool>() == true)
                        currentPromptChangeSetCommitted = true;
                    if (string.Equals(resultType, "migration.apply", StringComparison.Ordinal)
                        && resultData?["committed"]?.Value<bool>() == true
                        && resultData?["configurationSaved"]?.Value<bool>() == true)
                        currentPromptMigrationCommitted = true;
                    if (string.Equals(resultType, "change_set.preview", StringComparison.Ordinal)
                        || string.Equals(resultType, "process_blueprint.preview", StringComparison.Ordinal)
                        || string.Equals(resultType, "migration.preview", StringComparison.Ordinal))
                    {
                        currentPromptPreviewCreated = true;
                        if (currentFirstSuccessfulPreviewUtc == default(DateTime))
                        {
                            currentFirstSuccessfulPreviewUtc = finishedUtc;
                            currentToolFailuresAtFirstSuccessfulPreview = currentPromptToolErrorCount;
                        }
                    }
                    currentPromptStageArtifact = AiStageArtifact.Capture(
                        currentPromptStageArtifact,
                        state.ToolName,
                        resultType,
                        resultData);
                    if (string.Equals(resultType, "preview.reject", StringComparison.Ordinal)
                        && resultData?["rejected"]?.Value<bool>() == true)
                        currentPromptPreviewRejected = true;
                    if (string.Equals(resultType, "task_decision.submit", StringComparison.Ordinal)
                        && resultData != null)
                    {
                        currentPromptTaskDecisionCount++;
                        if (lastSubmittedTaskDecision == null)
                        {
                            lastSubmittedTaskDecision = resultData.ToObject<TaskCapabilityDecisionDefinition>();
                            AttachReviewVerifiedFactsLocked(lastSubmittedTaskDecision);
                            if (!currentPromptDecisionTerminationSent)
                            {
                                currentPromptDecisionTerminationSent = true;
                                terminateDecisionTurn = true;
                            }
                        }
                    }
                }
            }
            else if (!parameterGenerationFailed && !transportFailed && !businessFailed
                && IsDeveloperWriteToolName(state.ToolName))
            {
                lock (executionLock) currentPromptSourceFilesChanged = true;
            }
            if (!parameterGenerationFailed
                && string.Equals(config.ToolProfile, AutomationToolProfiles.SourceDevelopment, StringComparison.Ordinal)
                && (IsDeveloperShellToolName(state.ToolName)
                    || (IsDeveloperWriteToolName(state.ToolName) && (transportFailed || businessFailed))))
            {
                lock (executionLock) currentPromptSourceMutationUncertain = true;
            }
            if (!parameterGenerationFailed
                && (transportFailed || businessFailed)
                && AiTaskCapabilityPolicy.IsConfigurationMutation(config.ToolProfile)
                && IsMutationAttemptTool(state.ToolName)
                && !string.Equals(reportedSideEffects, "none", StringComparison.OrdinalIgnoreCase))
            {
                lock (executionLock) currentPromptUnsafeMutationFailure = true;
            }
            string status = parameterGenerationFailed
                ? "not_dispatched"
                : transportFailed ? "transport_error"
                : businessFailed ? "business_error" : "ok";
            string stage = parameterGenerationFailed
                ? "provider.arguments"
                : transportFailed ? "acp" : businessFailed ? "business" : string.Empty;
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
                    ["sideEffects"] = reportedSideEffects
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
            if (terminateDecisionTurn)
            {
                WriteAnalysisEvent("capability.decision.termination_requested", new JObject
                {
                    ["tool"] = state.ToolName,
                    ["reason"] = "first_valid_submission_locked"
                }, finishedUtc);
                Cancel();
            }
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
            switch (config.ToolProfile)
            {
                case AutomationToolProfiles.TaskCoordinator:
                    context = "当前能力：任务动态协调。你没有读取、开发、配置写入或运行工具。根据当前用户目标和最近一次工作阶段的真实结果，只调用一次 request_capability。完整流程创建直接申请 ProcessCreate；ProcessDesign 只用于纯方案或写入前必须先由用户取舍的设计。已按用户要求完成且允许保留占位时使用 finish，不因可选后续完善而 ask_user。第一条成功决定会立即锁定；调用后立即结束本轮，不要输出计划或总结。";
                    break;
                case AutomationToolProfiles.RuntimeDiagnostic:
                    context = "当前能力：运行现场取证。只根据现场工具返回的事实诊断，不执行运行控制或配置写入。";
                    break;
                case AutomationToolProfiles.ProcessDesign:
                    context = "当前能力：流程方案设计。仅处理纯方案或写入前必须让用户取舍的设计；按需读取已审核设计知识，输出可供后续落地的结构和未决项，本轮没有项目扫描或写入工具。";
                    break;
                case AutomationToolProfiles.ProcessReview:
                    context = "当前能力：流程只读审查。主动获取完成结论所需的精确流程、引用和资源事实；严格区分事实、推断和证据缺口。";
                    break;
                case AutomationToolProfiles.ProcessCreate:
                    context = "当前能力：新建流程。本能力同时承担必要设计、设计知识读取、资源发现和创建，不先申请 ProcessDesign。优先使用单个 Process Blueprint 预演完整新流程，经前台确认后提交；未知资源或动作保留为可见占位并保持 incomplete。";
                    break;
                case AutomationToolProfiles.ProcessEdit:
                    context = "当前能力：修改既有流程。评审交接已有targetIds时优先用get_op_details精读目标，不重复读取完整流程；同类型局部更新可直接复用返回的可写fields，字段形状不明确时使用get_native_operation_field_contract，避免同时读取全量原生契约和UI Schema。随后用 ChangeSet V2 预演，经前台确认后提交。";
                    break;
                case AutomationToolProfiles.ResourceEdit:
                    context = "当前能力：编辑独立项目资源。只修改用户明确要求的变量、数据结构或报警配置，并使用精确名称、索引或现有资源事实。";
                    break;
                case AutomationToolProfiles.RuntimeControl:
                    context = "当前能力：流程运行控制。先验证启动条件和当前状态，只执行用户明确要求的启动、停止、暂停、恢复或有界测试。";
                    break;
                case AutomationToolProfiles.SourceDevelopment:
                    context = "当前能力：Automation 源码开发。先用 search_platform_source、read、tree 做只读定位，确认目标后再使用开发工具修改和验证仓库代码；平台上下文仅在需要精确内部契约时按需读取。纯读取不要使用shell，因为shell无法被执行器证明无间接写入并会要求重建。若调查后无需修改，直接提交finish或ask_user，不得声称源码已变化。";
                    break;
                case AutomationToolProfiles.SourceReview:
                    context = "当前能力：Automation 源码只读检查。优先使用 search_platform_source 受限检索，可以读取源码并按需获取平台开发上下文，但不得执行 shell、写入、编辑或删除文件。";
                    break;
                case AutomationToolProfiles.PlatformConfiguration:
                    context = "当前能力：平台级配置迁移。只使用迁移预演、确认和应用链，并在提交后验证平台配置。";
                    break;
                case AutomationToolProfiles.Diagnostic:
                    context = "当前 Automation 权限模式：Diagnostic。只开放读取和诊断工具，不具备运行控制或配置写入能力。";
                    break;
                default:
                    context = "当前 Automation 权限模式：Editor。开放读取、诊断、配置写入和运行控制工具。";
                    break;
            }
            if (!string.IsNullOrWhiteSpace(config.TaskCapabilityNotice))
            {
                context += "\n" + config.TaskCapabilityNotice.Trim();
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
                JObject authoringSkillAnalysis;
                if (string.IsNullOrWhiteSpace(GooseRuntimeProvisioner.ProcessAuthoringSkillPath))
                {
                    authoringSkillAnalysis = new JObject
                    {
                        ["deployed"] = GooseRuntimeProvisioner.IsProcessAuthoringSkillAvailable,
                        ["bundledVersion"] = GooseRuntimeProvisioner.ProcessAuthoringSkillVersion,
                        ["exists"] = false
                    };
                }
                else
                {
                    authoringSkillAnalysis = BuildManagedFileAnalysis(
                        GooseRuntimeProvisioner.ProcessAuthoringSkillPath,
                        GooseRuntimeProvisioner.GetProcessAuthoringSkillVersionPath(),
                        GooseRuntimeProvisioner.ProcessAuthoringSkillVersion);
                    authoringSkillAnalysis["deployed"] = GooseRuntimeProvisioner.IsProcessAuthoringSkillAvailable;
                }
                authoringSkillAnalysis["extension"] = "skills";
                authoringSkillAnalysis["loadEvidence"] = "tool.finished: load_skill, status=ok";
                JObject reviewSkillAnalysis;
                if (string.IsNullOrWhiteSpace(GooseRuntimeProvisioner.ProcessReviewSkillPath))
                {
                    reviewSkillAnalysis = new JObject
                    {
                        ["deployed"] = GooseRuntimeProvisioner.IsProcessReviewSkillAvailable,
                        ["bundledVersion"] = GooseRuntimeProvisioner.ProcessReviewSkillVersion,
                        ["exists"] = false
                    };
                }
                else
                {
                    reviewSkillAnalysis = BuildManagedFileAnalysis(
                        GooseRuntimeProvisioner.ProcessReviewSkillPath,
                        GooseRuntimeProvisioner.GetProcessReviewSkillVersionPath(),
                        GooseRuntimeProvisioner.ProcessReviewSkillVersion);
                    reviewSkillAnalysis["deployed"] = GooseRuntimeProvisioner.IsProcessReviewSkillAvailable;
                }
                reviewSkillAnalysis["extension"] = "skills";
                reviewSkillAnalysis["loadEvidence"] = "tool.finished: load_skill, status=ok";
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
                        GooseRuntimeProvisioner.IntegrationContextVersion),
                    ["processAuthoringSkill"] = authoringSkillAnalysis,
                    ["processReviewSkill"] = reviewSkillAnalysis
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

        private static string ReadSessionId(JObject result)
        {
            string value = result["sessionId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("EW-AI ACP 未返回 sessionId。");
            }
            return value;
        }

        // Editor 从工程根目录的 .agents/skills 发现项目 Skill；运行诊断沿用 HMI
        // 工作目录，不加载流程编写 Skill。HMI 精确源码路径也可由开发 Context 返回。
        private string ResolveWorkingDirectory()
        {
            if (!HmiDevelopmentSourceLocator.TryResolve(
                AppDomain.CurrentDomain.BaseDirectory,
                out HmiDevelopmentSource source,
                out string error))
            {
                throw new DirectoryNotFoundException(error);
            }
            if (string.Equals(config.ToolProfile, "RuntimeDiagnostic", StringComparison.Ordinal))
            {
                return source.SourceDirectory;
            }
            return source.ProjectRoot;
        }

        private static string ExtractText(JToken token)
        {
            if (token == null)
            {
                return string.Empty;
            }

            string directText = FindFirstString(token, "text");
            if (directText != null)
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
                    if (nested != null)
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
                    if (nested != null)
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
            int assistantResponseChars)
        {
            long toolAggregateMs = analysisToolIntervals.Sum(interval => interval.DurationMs);
            long toolWallMs = CalculateIntervalUnionMs(analysisToolIntervals);
            long unattributedMs = Math.Max(0L, totalDurationMs - toolWallMs - currentPreviewWaitMs);
            int retryCount = analysisToolAttempts.Values.Sum(count => Math.Max(0, count - 1));
            AiTrajectoryEvaluation trajectory = AiTrajectoryBudgetPolicy.Evaluate(
                config.ToolProfile,
                currentPromptToolCallCount,
                currentPromptToolErrorCount,
                currentPromptToolResultBytes,
                promptResult?["usage"]?["inputTokens"]?.Value<long?>()
                    ?? promptResult?["usage"]?["input_tokens"]?.Value<long?>()
                    ?? 0L,
                ContextWindowTokens,
                unattributedMs,
                currentPromptToolNames);
            AiTurnEvidence evidence = BuildTurnEvidenceLocked();
            var result = new JObject
            {
                ["status"] = promptException == null ? "completed" : "failed",
                ["stopReason"] = promptResult?["stopReason"]?.Value<string>() ?? string.Empty,
                ["durationMs"] = totalDurationMs,
                ["firstActivityMs"] = currentFirstModelActivityUtc == default(DateTime)
                    ? (long?)null
                    : Math.Max(0L, (long)(currentFirstModelActivityUtc - currentPromptStartedUtc).TotalMilliseconds),
                ["firstPreviewAttemptMs"] = currentFirstPreviewAttemptUtc == default(DateTime)
                    ? (long?)null
                    : Math.Max(0L, (long)(currentFirstPreviewAttemptUtc - currentPromptStartedUtc).TotalMilliseconds),
                ["firstSuccessfulPreviewMs"] = currentFirstSuccessfulPreviewUtc == default(DateTime)
                    ? (long?)null
                    : Math.Max(0L, (long)(currentFirstSuccessfulPreviewUtc - currentPromptStartedUtc).TotalMilliseconds),
                ["toolFailuresAtFirstSuccessfulPreview"] = currentFirstSuccessfulPreviewUtc == default(DateTime)
                    ? (int?)null : currentToolFailuresAtFirstSuccessfulPreview,
                ["authoringInputResolutionCalls"] = currentAuthoringInputResolutionCalls,
                ["operationCapabilityResolutionCalls"] = currentOperationCapabilityResolutionCalls,
                ["toolCallCount"] = currentPromptToolCallCount,
                ["toolFailureCount"] = currentPromptToolErrorCount,
                ["toolResultBytes"] = currentPromptToolResultBytes,
                ["parameterFailureCount"] = currentParameterFailureCount,
                ["retryCount"] = retryCount,
                ["maxConcurrentTools"] = currentMaxConcurrentTools,
                ["toolAggregateMs"] = toolAggregateMs,
                ["toolWallMs"] = toolWallMs,
                ["confirmationWaitMs"] = currentPreviewWaitMs,
                ["unattributedMs"] = unattributedMs,
                ["assistantResponseChars"] = assistantResponseChars,
                ["decisionMessageChars"] = lastSubmittedTaskDecision?.Message?.Length ?? 0,
                ["persistedCandidateChars"] = !string.IsNullOrWhiteSpace(lastSubmittedTaskDecision?.Message)
                    ? lastSubmittedTaskDecision.Message.Length
                    : assistantResponseChars,
                ["decisionLocked"] = lastSubmittedTaskDecision != null,
                ["terminationRequested"] = currentPromptDecisionTerminationSent,
                ["terminationObserved"] = string.Equals(
                    promptResult?["stopReason"]?.Value<string>(),
                    "capability_decision_locked",
                    StringComparison.Ordinal),
                ["terminationOutcome"] = !currentPromptDecisionTerminationSent
                    ? "not_requested"
                    : string.Equals(
                        promptResult?["stopReason"]?.Value<string>(),
                        "capability_decision_locked",
                        StringComparison.Ordinal)
                        ? "cancel_observed"
                        : "turn_already_ended",
                ["unfinishedToolCount"] = activeAnalysisToolCalls.Count,
                ["trajectory"] = new JObject
                {
                    ["status"] = trajectory.Status,
                    ["toolCallLimit"] = trajectory.ToolCallLimit,
                    ["toolResultByteLimit"] = trajectory.ToolResultByteLimit,
                    ["contextPressureTokenLimit"] = trajectory.ContextPressureTokenLimit,
                    ["unattributedMsLimit"] = trajectory.UnattributedMsLimit,
                    ["reasons"] = new JArray(trajectory.Reasons)
                },
                ["stageEvidence"] = new JObject
                {
                    ["automationToolSucceeded"] = evidence.AutomationToolSucceeded,
                    ["currentStateReadSucceeded"] = evidence.CurrentStateReadSucceeded,
                    ["configurationSaved"] = evidence.ConfigurationSaved,
                    ["changeSetCommitted"] = evidence.ChangeSetCommitted,
                    ["migrationCommitted"] = evidence.MigrationCommitted,
                    ["previewCreated"] = evidence.PreviewCreated,
                    ["mutationAttempted"] = evidence.MutationAttempted,
                    ["verificationSucceeded"] = evidence.VerificationSucceeded,
                    ["previewRejected"] = evidence.PreviewRejected,
                    ["designKnowledgeReadSucceeded"] = evidence.DesignKnowledgeReadSucceeded,
                    ["unsafeMutationFailure"] = evidence.UnsafeMutationFailure,
                    ["sourceMutationUncertain"] = evidence.SourceMutationUncertain
                }
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

        private AiTurnEvidence BuildTurnEvidenceLocked()
        {
            return new AiTurnEvidence(
                currentPromptAutomationToolSucceeded,
                currentPromptCurrentStateReadSucceeded,
                currentPromptConfigurationSaved,
                currentPromptChangeSetCommitted,
                currentPromptMigrationCommitted,
                currentPromptPreviewRejected,
                currentPromptSourceFilesChanged,
                currentPromptToolErrorCount,
                currentPromptDesignKnowledgeReadSucceeded,
                currentPromptUnsafeMutationFailure,
                currentPromptSourceMutationUncertain,
                currentPromptMutationAttempted,
                currentPromptPreviewCreated,
                currentPromptVerificationSucceeded);
        }

        private void CaptureFinalAssistantResponseBeforeToolLocked(string toolName)
        {
            string candidate = assistantResponse.ToString().Trim();
            if (string.Equals(toolName, "request_capability", StringComparison.Ordinal)
                && finalAssistantResponse.Length == 0
                && candidate.Length > 0)
            {
                finalAssistantResponse.Append(candidate);
            }
            // 工具调用前的普通 assistant 文本属于过程说明；只有 request_capability 前最后一段可作为最终输出。
            assistantResponse.Clear();
        }

        private string BuildFinalAssistantResponseLocked()
        {
            if (finalAssistantResponse.Length > 0)
                return finalAssistantResponse.ToString();
            return assistantResponse.ToString().Trim();
        }

        private static bool IsDeveloperWriteToolName(string toolName)
        {
            string value = (toolName ?? string.Empty).Trim();
            int separator = Math.Max(value.LastIndexOf("__", StringComparison.Ordinal),
                Math.Max(value.LastIndexOf('/'), value.LastIndexOf('.')));
            if (separator >= 0)
                value = value.Substring(separator + (value[separator] == '_' ? 2 : 1));
            return string.Equals(value, "write", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "edit", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPreviewTool(string toolName)
        {
            return string.Equals(toolName, "preview_change_set", StringComparison.Ordinal)
                || string.Equals(toolName, "preview_process_blueprint", StringComparison.Ordinal)
                || (toolName ?? string.Empty).StartsWith("preview_", StringComparison.Ordinal)
                    && (toolName ?? string.Empty).EndsWith("_configuration", StringComparison.Ordinal);
        }

        private static bool IsDeveloperShellToolName(string toolName)
        {
            string value = (toolName ?? string.Empty).Trim();
            int separator = Math.Max(value.LastIndexOf("__", StringComparison.Ordinal),
                Math.Max(value.LastIndexOf('/'), value.LastIndexOf('.')));
            if (separator >= 0)
                value = value.Substring(separator + (value[separator] == '_' ? 2 : 1));
            return string.Equals(value, "shell", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsCurrentStateEvidenceTool(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return false;
            switch (toolName)
            {
                case "get_process_design_guide":
                case "get_operation_schema":
                case "get_operation_guide":
                case "get_semantic_operation_schema":
                case "get_native_operation_schemas":
                case "get_native_operation_field_contract":
                case "preview_change_set":
                case "preview_process_blueprint":
                case "apply_change_set":
                case "discard_change_set_preview":
                case "add_variable":
                case "update_variable":
                case "delete_variable":
                case "upsert_data_struct":
                case "delete_data_struct":
                case "set_alarm":
                case "delete_alarm":
                case "preview_motion_io_configuration":
                case "preview_io_debug_configuration":
                case "preview_plc_configuration":
                case "preview_communication_configuration":
                case "apply_migration_configuration":
                case "discard_migration_configuration":
                case "request_capability":
                    return false;
                default:
                    return true;
            }
        }

        private void CaptureReviewObjectIdentityLocked(string toolName, JObject resultData)
        {
            if (resultData == null
                || (!string.Equals(toolName, "get_proc_overview", StringComparison.Ordinal)
                    && !string.Equals(toolName, "get_proc_detail", StringComparison.Ordinal)
                    && !string.Equals(toolName, "validate_proc", StringComparison.Ordinal)
                    && !string.Equals(toolName, "get_op_details", StringComparison.Ordinal)))
            {
                return;
            }
            int? procIndex = resultData["procIndex"]?.Value<int?>();
            string procId = resultData["procId"]?.Value<string>();
            if (procIndex.HasValue && !string.IsNullOrWhiteSpace(procId))
            {
                latestReviewProcIdsByIndex[procIndex.Value] = procId.Trim();
            }
        }

        private void CaptureReviewVerifiedFactsLocked(
            string toolName,
            string toolCallId,
            JObject resultData,
            string rawResult)
        {
            if (resultData == null) return;
            switch (toolName ?? string.Empty)
            {
                case "get_proc_overview":
                    CaptureProcOverviewFactsLocked(toolCallId, resultData, rawResult);
                    break;
                case "validate_proc":
                    CaptureValidateProcFactsLocked(toolCallId, resultData, rawResult);
                    break;
                case "get_flow_graph":
                    CaptureFlowGraphFactsLocked(toolCallId, resultData, rawResult);
                    break;
                case "get_operation_references":
                    CaptureOperationReferenceFactsLocked(toolCallId, resultData, rawResult);
                    break;
                case "find_variable_usages":
                    CaptureVariableUsageFactsLocked(toolCallId, resultData, rawResult);
                    break;
                case "get_op_details":
                    CaptureOperationDetailsFactsLocked(toolCallId, resultData, rawResult);
                    break;
                case "get_proc_references":
                    CaptureProcReferenceFactsLocked(toolCallId, resultData, rawResult);
                    break;
                case "audit_proc_batch":
                    CaptureAuditFactsLocked(toolCallId, resultData, rawResult);
                    break;
            }
        }

        private void CaptureProcOverviewFactsLocked(
            string toolCallId,
            JObject resultData,
            string rawResult)
        {
            int? procIndex = resultData["procIndex"]?.Value<int?>();
            string procId = resultData["procId"]?.Value<string>();
            if (!procIndex.HasValue || string.IsNullOrWhiteSpace(procId)) return;
            string procName = resultData["name"]?.Value<string>() ?? procId;
            string hash = AiAnalysisLogger.FingerprintText(rawResult)["sha256"]?.Value<string>() ?? string.Empty;
            var facts = new List<ReviewVerifiedFactDefinition>();
            AddReviewFact(facts, procId, procName, "proc.procIndex", NormalizeFactValue(resultData["procIndex"]),
                toolCallId, "/data/procIndex", hash, "get_proc_overview");
            AddReviewFact(facts, procId, procName, "proc.autoStart", NormalizeFactValue(resultData["autoStart"]),
                toolCallId, "/data/autoStart", hash, "get_proc_overview");
            AddReviewFact(facts, procId, procName, "proc.disabled", NormalizeFactValue(resultData["disable"]),
                toolCallId, "/data/disable", hash, "get_proc_overview");
            AddReviewFact(facts, procId, procName, "proc.state", NormalizeFactValue(resultData["state"]),
                toolCallId, "/data/state", hash, "get_proc_overview");
            AddReviewFact(facts, procId, procName, "proc.readinessStatus", NormalizeFactValue(resultData["readinessStatus"]),
                toolCallId, "/data/readinessStatus", hash, "get_proc_overview");
            AddReviewFact(facts, procId, procName, "proc.runnable", NormalizeFactValue(resultData["runnable"]),
                toolCallId, "/data/runnable", hash, "get_proc_overview");
            AddReviewFact(facts, procId, procName, "proc.stepCount", NormalizeFactValue(resultData["stepCount"]),
                toolCallId, "/data/stepCount", hash, "get_proc_overview");
            AddReviewFact(facts, procId, procName, "proc.operationCount", NormalizeFactValue(resultData["operationCount"]),
                toolCallId, "/data/operationCount", hash, "get_proc_overview");
            AddReviewFact(facts, procId, procName, "proc.warningCount",
                ((resultData["warnings"] as JArray)?.Count ?? 0).ToString(CultureInfo.InvariantCulture),
                toolCallId, "/data/warnings", hash, "get_proc_overview");
            AddReviewFact(facts, procId, procName, "proc.runBlockerCount",
                ((resultData["runBlockers"] as JArray)?.Count ?? 0).ToString(CultureInfo.InvariantCulture),
                toolCallId, "/data/runBlockers", hash, "get_proc_overview");
            JArray steps = resultData["steps"] as JArray ?? new JArray();
            for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                if (!(steps[stepIndex] is JObject step)) continue;
                string stepId = step["stepId"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(stepId))
                {
                    string stepName = step["name"]?.Value<string>() ?? stepId;
                    AddReviewFact(facts, stepId, stepName, "step.disabled", NormalizeFactValue(step["disable"]),
                        toolCallId, $"/data/steps/{stepIndex}/disable", hash, "get_proc_overview");
                    AddReviewFact(facts, stepId, stepName, "step.operationCount", NormalizeFactValue(step["opCount"]),
                        toolCallId, $"/data/steps/{stepIndex}/opCount", hash, "get_proc_overview");
                }
                CaptureOperationDirectoryFacts(facts, step["ops"] as JArray, toolCallId,
                    $"/data/steps/{stepIndex}/ops", hash, "get_proc_overview", stepId);
            }
            MergeReviewFactsLocked(facts);
        }

        private void CaptureValidateProcFactsLocked(
            string toolCallId,
            JObject resultData,
            string rawResult)
        {
            int? procIndex = resultData["procIndex"]?.Value<int?>();
            if (!procIndex.HasValue) return;
            string subjectId = resultData["procId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(subjectId))
            {
                subjectId = latestReviewProcIdsByIndex.TryGetValue(procIndex.Value, out string procId)
                    ? procId
                    : "procIndex:" + procIndex.Value.ToString(CultureInfo.InvariantCulture);
            }
            string subjectName = resultData["procName"]?.Value<string>() ?? subjectId;
            string evidenceSha256 = AiAnalysisLogger.FingerprintText(rawResult)["sha256"]?.Value<string>()
                ?? string.Empty;
            var facts = new List<ReviewVerifiedFactDefinition>();
            AddReviewFact(facts, subjectId, subjectName, "proc.procIndex",
                procIndex.Value.ToString(CultureInfo.InvariantCulture), toolCallId,
                "/data/procIndex", evidenceSha256, "validate_proc");
            AddReviewFact(facts, subjectId, subjectName, "proc.isValid",
                NormalizeFactValue(resultData["isValid"]), toolCallId,
                "/data/isValid", evidenceSha256, "validate_proc");
            AddReviewFact(facts, subjectId, subjectName, "proc.readinessStatus",
                NormalizeFactValue(resultData["readinessStatus"]), toolCallId,
                "/data/readinessStatus", evidenceSha256, "validate_proc");
            AddReviewFact(facts, subjectId, subjectName, "proc.runnable",
                NormalizeFactValue(resultData["runnable"]), toolCallId,
                "/data/runnable", evidenceSha256, "validate_proc");
            int blockerCount = (resultData["runBlockers"] as JArray)?.Count ?? 0;
            int placeholderBlockerCount = (resultData["runBlockers"] as JArray)?
                .Values<string>()
                .Count(message => (message ?? string.Empty).IndexOf("配置占位", StringComparison.Ordinal) >= 0)
                ?? 0;
            int warningCount = resultData["warningCount"]?.Value<int?>()
                ?? (resultData["warnings"] as JArray)?.Count
                ?? 0;
            int placeholderWarningCount = (resultData["warnings"] as JArray)?
                .OfType<JObject>()
                .Select(item => item["message"]?.Value<string>() ?? string.Empty)
                .Count(message => message.IndexOf("占位", StringComparison.Ordinal) >= 0)
                ?? 0;
            AddReviewFact(facts, subjectId, subjectName, "proc.warningCount",
                warningCount.ToString(CultureInfo.InvariantCulture), toolCallId,
                "/data/warningCount", evidenceSha256, "validate_proc");
            AddReviewFact(facts, subjectId, subjectName, "proc.placeholderWarningCount",
                placeholderWarningCount.ToString(CultureInfo.InvariantCulture), toolCallId,
                "/data/warnings", evidenceSha256, "validate_proc");
            AddReviewFact(facts, subjectId, subjectName, "proc.runBlockerCount",
                blockerCount.ToString(CultureInfo.InvariantCulture), toolCallId,
                "/data/runBlockers", evidenceSha256, "validate_proc");
            AddReviewFact(facts, subjectId, subjectName, "proc.nonPlaceholderBlockerCount",
                Math.Max(0, blockerCount - placeholderBlockerCount).ToString(CultureInfo.InvariantCulture),
                toolCallId, "/data/runBlockers", evidenceSha256, "validate_proc");
            MergeReviewFactsLocked(facts);
        }

        private void CaptureFlowGraphFactsLocked(
            string toolCallId,
            JObject resultData,
            string rawResult)
        {
            string evidenceSha256 = AiAnalysisLogger.FingerprintText(rawResult)["sha256"]?.Value<string>()
                ?? string.Empty;
            var facts = new List<ReviewVerifiedFactDefinition>();
            JArray nodes = resultData["nodes"] as JArray ?? new JArray();
            for (int index = 0; index < nodes.Count; index++)
            {
                if (!(nodes[index] is JObject node)
                    || !string.Equals(node["kind"]?.Value<string>(), "operation", StringComparison.Ordinal))
                    continue;
                string opId = node["opId"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(opId)) continue;
                string name = node["label"]?.Value<string>() ?? opId;
                AddReviewFact(facts, opId, name, "operation.reachable",
                    NormalizeFactValue(node["reachable"]), toolCallId,
                    $"/data/nodes/{index}/reachable", evidenceSha256, "get_flow_graph");
                AddReviewFact(facts, opId, name, "operation.invalid",
                    NormalizeFactValue(node["invalid"]), toolCallId,
                    $"/data/nodes/{index}/invalid", evidenceSha256, "get_flow_graph");
            }
            JArray diagnostics = resultData["diagnostics"] as JArray ?? new JArray();
            for (int index = 0; index < diagnostics.Count; index++)
            {
                if (!(diagnostics[index] is JObject diagnostic)) continue;
                string nodeId = diagnostic["nodeId"]?.Value<string>();
                string code = diagnostic["code"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(nodeId)
                    || !nodeId.StartsWith("op:", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(code)) continue;
                string opId = nodeId.Substring("op:".Length);
                string name = nodes.OfType<JObject>().FirstOrDefault(node =>
                    string.Equals(node["opId"]?.Value<string>(), opId, StringComparison.Ordinal))?["label"]?.Value<string>()
                    ?? opId;
                AddReviewFact(facts, opId, name, "operation.graphDiagnostic." + code,
                    "true", toolCallId, $"/data/diagnostics/{index}", evidenceSha256, "get_flow_graph");
            }
            MergeReviewFactsLocked(facts);
        }

        private void CaptureOperationReferenceFactsLocked(
            string toolCallId,
            JObject resultData,
            string rawResult)
        {
            JObject target = resultData["target"] as JObject;
            string opId = target?["opId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(opId)) return;
            string name = target["opName"]?.Value<string>() ?? opId;
            string evidenceSha256 = AiAnalysisLogger.FingerprintText(rawResult)["sha256"]?.Value<string>()
                ?? string.Empty;
            var facts = new List<ReviewVerifiedFactDefinition>();
            AddReviewFact(facts, opId, name, "operation.incomingGotoCount",
                NormalizeFactValue(resultData["incomingGotoCountInBatch"]), toolCallId,
                "/data/incomingGotoCountInBatch", evidenceSha256, "get_operation_references");
            AddReviewFact(facts, opId, name, "operation.incomingGotoTruncated",
                NormalizeFactValue(resultData["truncatedIncoming"]), toolCallId,
                "/data/truncatedIncoming", evidenceSha256, "get_operation_references");
            MergeReviewFactsLocked(facts);
        }

        private void CaptureVariableUsageFactsLocked(
            string toolCallId,
            JObject resultData,
            string rawResult)
        {
            JObject variable = resultData["variable"] as JObject;
            string variableId = variable?["variableId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(variableId)) return;
            string name = variable["name"]?.Value<string>() ?? variableId;
            string evidenceSha256 = AiAnalysisLogger.FingerprintText(rawResult)["sha256"]?.Value<string>()
                ?? string.Empty;
            var facts = new List<ReviewVerifiedFactDefinition>();
            AddReviewFact(facts, variableId, name, "variable.usageCount",
                NormalizeFactValue(resultData["matchCountInBatch"]), toolCallId,
                "/data/matchCountInBatch", evidenceSha256, "find_variable_usages");
            AddReviewFact(facts, variableId, name, "variable.usagesTruncated",
                NormalizeFactValue(resultData["truncatedMatches"]), toolCallId,
                "/data/truncatedMatches", evidenceSha256, "find_variable_usages");
            AddReviewFact(facts, variableId, name, "variable.scope",
                NormalizeFactValue(variable["scope"]), toolCallId,
                "/data/variable/scope", evidenceSha256, "find_variable_usages");
            AddReviewFact(facts, variableId, name, "variable.ownerProcId",
                NormalizeFactValue(variable["ownerProcId"]), toolCallId,
                "/data/variable/ownerProcId", evidenceSha256, "find_variable_usages");
            JArray matches = resultData["matches"] as JArray ?? new JArray();
            for (int index = 0; index < matches.Count; index++)
            {
                if (!(matches[index] is JObject match)) continue;
                string opId = match["opId"]?.Value<string>();
                string field = match["field"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(opId) || string.IsNullOrWhiteSpace(field)) continue;
                string opName = match["opName"]?.Value<string>() ?? opId;
                AddReviewFact(facts, opId, opName, "operation.field." + SanitizeFactKeySegment(field),
                    CompactFactValue(match["value"]), toolCallId,
                    $"/data/matches/{index}/value", evidenceSha256, "find_variable_usages");
                AddReviewFact(facts, opId, opName, "operation.operaType",
                    NormalizeFactValue(match["operaType"]), toolCallId,
                    $"/data/matches/{index}/operaType", evidenceSha256, "find_variable_usages");
            }
            MergeReviewFactsLocked(facts);
        }

        private void CaptureOperationDetailsFactsLocked(
            string toolCallId,
            JObject resultData,
            string rawResult)
        {
            string hash = AiAnalysisLogger.FingerprintText(rawResult)["sha256"]?.Value<string>() ?? string.Empty;
            var facts = new List<ReviewVerifiedFactDefinition>();
            JArray operations = resultData["operations"] as JArray ?? new JArray();
            for (int index = 0; index < operations.Count; index++)
            {
                if (!(operations[index] is JObject operation)) continue;
                string opId = operation["opId"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(opId)) continue;
                string name = operation["name"]?.Value<string>() ?? opId;
                string path = $"/data/operations/{index}";
                AddReviewFact(facts, opId, name, "operation.operaType", NormalizeFactValue(operation["operaType"]),
                    toolCallId, path + "/operaType", hash, "get_op_details");
                AddReviewFact(facts, opId, name, "operation.disabled", NormalizeFactValue(operation["disable"]),
                    toolCallId, path + "/disable", hash, "get_op_details");
                AddReviewFact(facts, opId, name, "operation.stepId", NormalizeFactValue(operation["stepId"]),
                    toolCallId, path + "/stepId", hash, "get_op_details");
                AddReviewFact(facts, opId, name, "operation.stepIndex", NormalizeFactValue(operation["stepIndex"]),
                    toolCallId, path + "/stepIndex", hash, "get_op_details");
                AddReviewFact(facts, opId, name, "operation.opIndex", NormalizeFactValue(operation["opIndex"]),
                    toolCallId, path + "/opIndex", hash, "get_op_details");
                AddReviewFact(facts, opId, name, "operation.flow", CompactFactValue(operation["flow"]),
                    toolCallId, path + "/flow", hash, "get_op_details");
                if (operation["fields"] is JObject fields)
                {
                    foreach (JProperty field in fields.Properties())
                    {
                        AddReviewFact(facts, opId, name,
                            "operation.field." + SanitizeFactKeySegment(field.Name),
                            CompactFactValue(field.Value), toolCallId,
                            path + "/fields/" + field.Name.Replace("~", "~0").Replace("/", "~1"),
                            hash, "get_op_details");
                    }
                }
            }
            MergeReviewFactsLocked(facts);
        }

        private void CaptureProcReferenceFactsLocked(
            string toolCallId,
            JObject resultData,
            string rawResult)
        {
            JObject target = resultData["target"] as JObject;
            string procId = target?["procId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(procId)) return;
            string name = target["procName"]?.Value<string>() ?? procId;
            string hash = AiAnalysisLogger.FingerprintText(rawResult)["sha256"]?.Value<string>() ?? string.Empty;
            var facts = new List<ReviewVerifiedFactDefinition>();
            AddReviewFact(facts, procId, name, "proc.referenceCountInBatch",
                NormalizeFactValue(resultData["referenceCountInBatch"]), toolCallId,
                "/data/referenceCountInBatch", hash, "get_proc_references");
            AddReviewFact(facts, procId, name, "proc.referencesTruncated",
                NormalizeFactValue(resultData["truncatedReferences"]), toolCallId,
                "/data/truncatedReferences", hash, "get_proc_references");
            AddReviewFact(facts, procId, name, "proc.referenceScanHasMore",
                NormalizeFactValue(resultData["hasMoreProcs"]), toolCallId,
                "/data/hasMoreProcs", hash, "get_proc_references");
            MergeReviewFactsLocked(facts);
        }

        private void CaptureAuditFactsLocked(
            string toolCallId,
            JObject resultData,
            string rawResult)
        {
            string revision = resultData["indexRevision"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(revision)) return;
            string subjectId = "audit:" + revision;
            string hash = AiAnalysisLogger.FingerprintText(rawResult)["sha256"]?.Value<string>() ?? string.Empty;
            var facts = new List<ReviewVerifiedFactDefinition>();
            AddReviewFact(facts, subjectId, "流程批量审计", "audit.findingCountInBatch",
                NormalizeFactValue(resultData["findingCountInBatch"]), toolCallId,
                "/data/findingCountInBatch", hash, "audit_proc_batch");
            AddReviewFact(facts, subjectId, "流程批量审计", "audit.returnedFindingCount",
                NormalizeFactValue(resultData["returnedFindingCount"]), toolCallId,
                "/data/returnedFindingCount", hash, "audit_proc_batch");
            AddReviewFact(facts, subjectId, "流程批量审计", "audit.hasMoreFindings",
                NormalizeFactValue(resultData["hasMoreFindings"]), toolCallId,
                "/data/hasMoreFindings", hash, "audit_proc_batch");
            AddReviewFact(facts, subjectId, "流程批量审计", "audit.hasMoreProcs",
                NormalizeFactValue(resultData["hasMoreProcs"]), toolCallId,
                "/data/hasMoreProcs", hash, "audit_proc_batch");
            AddReviewFact(facts, subjectId, "流程批量审计", "audit.procRange",
                CompactFactValue(resultData["procRange"]), toolCallId,
                "/data/procRange", hash, "audit_proc_batch");
            MergeReviewFactsLocked(facts);
        }

        private static void CaptureOperationDirectoryFacts(
            ICollection<ReviewVerifiedFactDefinition> facts,
            JArray operations,
            string toolCallId,
            string basePath,
            string evidenceSha256,
            string sourceTool,
            string stepId)
        {
            if (operations == null) return;
            for (int index = 0; index < operations.Count; index++)
            {
                if (!(operations[index] is JObject operation)) continue;
                string opId = operation["opId"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(opId)) continue;
                string name = operation["name"]?.Value<string>() ?? opId;
                AddReviewFact(facts, opId, name, "operation.operaType", NormalizeFactValue(operation["operaType"]),
                    toolCallId, $"{basePath}/{index}/operaType", evidenceSha256, sourceTool);
                AddReviewFact(facts, opId, name, "operation.disabled", NormalizeFactValue(operation["disable"]),
                    toolCallId, $"{basePath}/{index}/disable", evidenceSha256, sourceTool);
                AddReviewFact(facts, opId, name, "operation.stepId", stepId,
                    toolCallId, $"{basePath}/{index}/opId", evidenceSha256, sourceTool);
                AddReviewFact(facts, opId, name, "operation.opIndex", NormalizeFactValue(operation["opIndex"]),
                    toolCallId, $"{basePath}/{index}/opIndex", evidenceSha256, sourceTool);
            }
        }

        private static string CompactFactValue(JToken value)
        {
            string normalized = NormalizeFactValue(value);
            return normalized.Length <= 512 ? normalized : normalized.Substring(0, 512) + "…";
        }

        private static string SanitizeFactKeySegment(string value)
        {
            string normalized = new string((value ?? string.Empty).Trim()
                .Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-'
                    ? character : '_')
                .ToArray());
            return normalized.Length == 0 ? "unknown" : normalized;
        }

        private static void AddReviewFact(
            ICollection<ReviewVerifiedFactDefinition> facts,
            string subjectId,
            string subjectName,
            string key,
            string value,
            string toolCallId,
            string evidencePath,
            string evidenceSha256,
            string sourceTool)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            facts.Add(new ReviewVerifiedFactDefinition
            {
                SubjectId = subjectId,
                SubjectName = subjectName,
                Key = key,
                Value = value,
                SourceTool = sourceTool ?? string.Empty,
                ToolCallId = toolCallId ?? string.Empty,
                EvidencePath = evidencePath,
                EvidenceSha256 = evidenceSha256
            });
        }

        private void MergeReviewFactsLocked(IEnumerable<ReviewVerifiedFactDefinition> facts)
        {
            foreach (ReviewVerifiedFactDefinition fact in facts ?? Enumerable.Empty<ReviewVerifiedFactDefinition>())
            {
                if (!latestReviewFactsBySubject.TryGetValue(fact.SubjectId, out List<ReviewVerifiedFactDefinition> current))
                {
                    current = new List<ReviewVerifiedFactDefinition>();
                    latestReviewFactsBySubject[fact.SubjectId] = current;
                }
                int existingIndex = current.FindIndex(item =>
                    string.Equals(item.Key, fact.Key, StringComparison.Ordinal));
                if (existingIndex >= 0) current[existingIndex] = fact;
                else current.Add(fact);
            }
        }

        private static string NormalizeFactValue(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return string.Empty;
            if (value.Type == JTokenType.Boolean)
                return value.Value<bool>() ? "true" : "false";
            return value.ToString(Formatting.None).Trim('"');
        }

        private void AttachReviewVerifiedFactsLocked(TaskCapabilityDecisionDefinition decision)
        {
            if (!string.Equals(config.ToolProfile, AutomationToolProfiles.ProcessReview, StringComparison.Ordinal)
                || decision?.ReviewHandoff == null)
            {
                return;
            }
            var referencedFactIds = new HashSet<string>(
                (decision.ReviewHandoff.Findings ?? new List<ReviewFindingDefinition>())
                    .SelectMany(finding => finding?.EvidenceFactRefs ?? new List<string>())
                    .Where(reference => !string.IsNullOrWhiteSpace(reference)),
                StringComparer.Ordinal);
            List<ReviewVerifiedFactDefinition> allFacts = latestReviewFactsBySubject
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .SelectMany(item => item.Value)
                .ToList();
            List<ReviewVerifiedFactDefinition> orderedFacts = allFacts
                .Where(fact => referencedFactIds.Contains(ReviewFactReference.Build(fact.SubjectId, fact.Key)))
                .Concat(allFacts.Where(fact => (fact.Key ?? string.Empty).StartsWith("proc.", StringComparison.Ordinal)
                    || (fact.Key ?? string.Empty).StartsWith("audit.", StringComparison.Ordinal)))
                .GroupBy(fact => ReviewFactReference.Build(fact.SubjectId, fact.Key), StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(fact => referencedFactIds.Contains(
                    ReviewFactReference.Build(fact.SubjectId, fact.Key)))
                .ThenBy(fact => fact.SubjectId, StringComparer.Ordinal)
                .ThenBy(fact => fact.Key, StringComparer.Ordinal)
                .Take(100)
                .ToList();
            decision.ReviewHandoff.VerifiedFacts = orderedFacts
                .Select(fact => new ReviewVerifiedFactDefinition
                {
                    SubjectId = fact.SubjectId,
                    SubjectName = fact.SubjectName,
                    Key = fact.Key,
                    Value = fact.Value,
                    SourceTool = fact.SourceTool,
                    ToolCallId = fact.ToolCallId,
                    EvidencePath = fact.EvidencePath,
                    EvidenceSha256 = fact.EvidenceSha256
                })
                .ToList();
        }

        internal static bool IsMutationAttemptTool(string toolName)
        {
            switch ((toolName ?? string.Empty).Trim())
            {
                case "apply_change_set":
                case "add_variable":
                case "update_variable":
                case "delete_variable":
                case "upsert_data_struct":
                case "delete_data_struct":
                case "set_alarm":
                case "delete_alarm":
                case "apply_migration_configuration":
                    return true;
                default:
                    return false;
            }
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
                    if (SensitiveDataRedactor.IsSensitiveName(name))
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
