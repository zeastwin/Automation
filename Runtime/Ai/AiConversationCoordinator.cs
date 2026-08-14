using System;
// 模块：运行时 / AI 集成。
// 职责范围：管理 AI 会话、配置、ACP/MCP 进程、受管运行环境和分析记录。
// 状态所有权：会话、活动任务、取消和持久化都在此；FrmAiAssistant 只投影状态并处理用户交互。

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Automation.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation
{
    internal sealed class AiTurnEvidence
    {
        public static AiTurnEvidence Empty { get; } = new AiTurnEvidence(
            false, false, false, false, false, false, false, 0, false, false, false,
            false, false, false);

        public AiTurnEvidence(
            bool automationToolSucceeded,
            bool currentStateReadSucceeded,
            bool configurationSaved,
            bool changeSetCommitted,
            bool migrationCommitted,
            bool previewRejected,
            bool sourceFilesChanged,
            int toolFailureCount,
            bool designKnowledgeReadSucceeded = false,
            bool unsafeMutationFailure = false,
            bool sourceMutationUncertain = false,
            bool mutationAttempted = false,
            bool previewCreated = false,
            bool verificationSucceeded = false)
        {
            AutomationToolSucceeded = automationToolSucceeded;
            CurrentStateReadSucceeded = currentStateReadSucceeded;
            ConfigurationSaved = configurationSaved;
            ChangeSetCommitted = changeSetCommitted;
            MigrationCommitted = migrationCommitted;
            PreviewRejected = previewRejected;
            SourceFilesChanged = sourceFilesChanged;
            ToolFailureCount = toolFailureCount;
            DesignKnowledgeReadSucceeded = designKnowledgeReadSucceeded;
            UnsafeMutationFailure = unsafeMutationFailure;
            SourceMutationUncertain = sourceMutationUncertain;
            MutationAttempted = mutationAttempted;
            PreviewCreated = previewCreated;
            VerificationSucceeded = verificationSucceeded;
        }

        public bool AutomationToolSucceeded { get; }
        public bool CurrentStateReadSucceeded { get; }
        public bool ConfigurationSaved { get; }
        public bool ChangeSetCommitted { get; }
        public bool MigrationCommitted { get; }
        public bool PreviewRejected { get; }
        public bool SourceFilesChanged { get; }
        public int ToolFailureCount { get; }
        public bool DesignKnowledgeReadSucceeded { get; }
        public bool UnsafeMutationFailure { get; }
        public bool SourceMutationUncertain { get; }
        public bool MutationAttempted { get; }
        public bool PreviewCreated { get; }
        public bool VerificationSucceeded { get; }

        public static AiTurnEvidence Merge(AiTurnEvidence left, AiTurnEvidence right)
        {
            left = left ?? Empty;
            right = right ?? Empty;
            return new AiTurnEvidence(
                left.AutomationToolSucceeded || right.AutomationToolSucceeded,
                left.CurrentStateReadSucceeded || right.CurrentStateReadSucceeded,
                left.ConfigurationSaved || right.ConfigurationSaved,
                left.ChangeSetCommitted || right.ChangeSetCommitted,
                left.MigrationCommitted || right.MigrationCommitted,
                left.PreviewRejected || right.PreviewRejected,
                left.SourceFilesChanged || right.SourceFilesChanged,
                left.ToolFailureCount + right.ToolFailureCount,
                left.DesignKnowledgeReadSucceeded || right.DesignKnowledgeReadSucceeded,
                left.UnsafeMutationFailure || right.UnsafeMutationFailure,
                left.SourceMutationUncertain || right.SourceMutationUncertain,
                left.MutationAttempted || right.MutationAttempted,
                left.PreviewCreated || right.PreviewCreated,
                left.VerificationSucceeded || right.VerificationSucceeded);
        }
    }

    /// <summary>
    /// 从成功工具结果机械提取的阶段产物。只用于原生会话丢失后的可信恢复，不包含模型推断。
    /// </summary>
    internal sealed class AiStageArtifact
    {
        private readonly JObject facts;

        private AiStageArtifact(JObject facts)
        {
            this.facts = facts ?? new JObject();
        }

        public static AiStageArtifact Empty => new AiStageArtifact(new JObject());

        public bool HasFacts => facts.HasValues;

        public static AiStageArtifact Merge(AiStageArtifact left, AiStageArtifact right)
        {
            var merged = (JObject)(left?.facts?.DeepClone() ?? new JObject());
            foreach (JProperty property in right?.facts?.Properties() ?? Enumerable.Empty<JProperty>())
                merged[property.Name] = property.Value?.DeepClone();
            return new AiStageArtifact(merged);
        }

        public static AiStageArtifact Capture(
            AiStageArtifact current,
            string toolName,
            string resultType,
            JObject data)
        {
            var next = (JObject)(current?.facts?.DeepClone() ?? new JObject());
            if (data == null) return new AiStageArtifact(next);
            if (string.Equals(resultType, "change_set.apply", StringComparison.Ordinal))
            {
                next["changeSetApply"] = Select(data,
                    "status", "configurationSaved", "affectedProcesses", "createdObjects", "readiness");
            }
            else if (string.Equals(resultType, "project.authoring_inputs", StringComparison.Ordinal))
            {
                next["authoringInputs"] = SelectAuthoringInputs(data["results"] as JArray);
            }
            else if (string.Equals(resultType, "operation.capability_resolution", StringComparison.Ordinal))
            {
                next["operationCapabilities"] = SelectOperationCapabilities(data["results"] as JArray);
            }
            else if (string.Equals(toolName, "resolve_proc_target", StringComparison.Ordinal))
            {
                next["processTarget"] = Select(data,
                    "resolutionStatus", "bindingAllowed", "selected", "exactMatchNames");
            }
            else if (string.Equals(toolName, "validate_proc", StringComparison.Ordinal))
            {
                next["validation"] = Select(data,
                    "procId", "procIndex", "name", "isValid", "runnable", "readiness",
                    "placeholderWarningCount", "runBlockerCount", "nonPlaceholderBlockerCount");
            }
            return new AiStageArtifact(next);
        }

        public string ToCompactJson()
        {
            return facts.ToString(Formatting.None);
        }

        private static JObject Select(JObject source, params string[] names)
        {
            var selected = new JObject();
            foreach (string name in names)
            {
                if (source[name] != null) selected[name] = source[name].DeepClone();
            }
            return selected;
        }

        private static JArray SelectAuthoringInputs(JArray results)
        {
            var selected = new JArray();
            foreach (JObject item in (results ?? new JArray()).OfType<JObject>().Take(20))
            {
                var value = Select(item,
                    "key", "kind", "purpose", "resolutionStatus", "compatibilityStatus",
                    "bindingAllowed", "selected", "note");
                selected.Add(value);
            }
            return selected;
        }

        private static JArray SelectOperationCapabilities(JArray results)
        {
            var selected = new JArray();
            foreach (JObject item in (results ?? new JArray()).OfType<JObject>().Take(12))
            {
                selected.Add(Select(item,
                    "key", "intent", "semanticCandidates", "nativeCandidates",
                    "resolutionStatus", "recommendedFallback", "nextContractTool"));
            }
            return selected;
        }
    }

    /// <summary>
    /// AI 会话、任务运行时和持久化的权威状态。窗体只负责展示当前状态和转发用户动作。
    /// </summary>
    internal sealed class AiConversationCoordinator
    {
        private readonly object clientLock = new object();

        public List<AiConversation> Conversations { get; } = new List<AiConversation>();
        public Dictionary<string, AiTaskRuntime> TaskRuntimes { get; } =
            new Dictionary<string, AiTaskRuntime>(StringComparer.Ordinal);
        public AiConversation ActiveConversation { get; private set; }
        public bool TaskHomeVisible { get; private set; } = true;

        public AiTaskRuntime ActiveRuntime => ActiveConversation != null
            && TaskRuntimes.TryGetValue(ActiveConversation.Id, out AiTaskRuntime runtime)
                ? runtime
                : null;

        public bool HasRunningTasks => TaskRuntimes.Values.Any(runtime => runtime.Running);

        public bool TryBeginTask(
            AiTaskRuntime runtime,
            string enteredPrompt,
            IReadOnlyList<GooseFileAttachment> attachments,
            out string prompt,
            out string conversationText,
            out IReadOnlyList<GooseFileAttachment> preparedAttachments,
            out string error)
        {
            prompt = (enteredPrompt ?? string.Empty).Trim();
            conversationText = null;
            preparedAttachments = (attachments ?? Array.Empty<GooseFileAttachment>()).ToList();
            error = null;
            if (string.IsNullOrWhiteSpace(prompt) && preparedAttachments.Count == 0)
            {
                error = "请输入内容或添加附件。";
                return false;
            }
            if (runtime == null)
            {
                error = "AI 任务运行时不存在。";
                return false;
            }
            if (runtime.Running)
            {
                error = "AI 任务正在运行。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(prompt)) prompt = "请分析我上传的文件。";

            conversationText = prompt;
            if (preparedAttachments.Count > 0)
            {
                conversationText += "\n\n📎 附件：" + string.Join(
                    "、",
                    preparedAttachments.Select(item => item.FileName));
            }
            DateTime startedAt = DateTime.Now;
            runtime.Conversation.Messages.Add(new AiConversationMessage
            {
                Role = "user",
                Text = conversationText,
                Time = startedAt
            });
            if (runtime.Conversation.Messages.Count == 1)
            {
                runtime.Conversation.Title = conversationText.Length > 24
                    ? conversationText.Substring(0, 24) + "…"
                    : conversationText;
            }
            runtime.Conversation.UpdatedAt = startedAt;
            runtime.PendingEvents.Clear();
            runtime.Running = true;
            runtime.Status = "进行中";
            runtime.Cancellation?.Dispose();
            runtime.Cancellation = new CancellationTokenSource();
            runtime.RecoveryContext = BuildRestoredContext(runtime.Conversation);
            return true;
        }

        public async Task<AiTaskExecutionResult> ExecuteTaskAsync(
            AiTaskRuntime runtime,
            string prompt,
            IReadOnlyList<GooseFileAttachment> attachments,
            Func<GooseAcpClient> clientFactory)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (clientFactory == null) throw new ArgumentNullException(nameof(clientFactory));
            GooseAcpClient client = null;
            try
            {
                client = clientFactory();
                await client.PromptAsync(
                    prompt,
                    attachments,
                    runtime.Cancellation.Token).ConfigureAwait(false);
                runtime.Status = "已完成";
                return BuildTaskResult(AiTaskExecutionStatus.Completed, runtime, client, null);
            }
            catch (OperationCanceledException)
            {
                RequestTrustedContextRolloverIfNeeded(runtime, client);
                runtime.Status = "已停止";
                return BuildTaskResult(AiTaskExecutionStatus.Cancelled, runtime, client, null);
            }
            catch (Exception ex)
            {
                RequestTrustedContextRolloverIfNeeded(runtime, client);
                runtime.Status = "失败";
                return BuildTaskResult(AiTaskExecutionStatus.Failed, runtime, client, ex.Message);
            }
            finally
            {
                runtime.Running = false;
                runtime.Cancellation?.Dispose();
                runtime.Cancellation = null;
            }
        }

        public async Task<AiTaskExecutionResult> ExecuteDynamicTaskAsync(
            AiTaskRuntime runtime,
            string originalRequest,
            IReadOnlyList<GooseFileAttachment> attachments,
            string permissionProfile,
            bool fullPermissionEnabled,
            Func<AiTaskCapabilityStage, Task<GooseAcpClient>> clientResolver)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (string.IsNullOrWhiteSpace(originalRequest))
                throw new ArgumentException("用户请求不能为空。", nameof(originalRequest));
            if (clientResolver == null) throw new ArgumentNullException(nameof(clientResolver));

            GooseAcpClient client = null;
            GooseAcpClient lastWorkerClient = null;
            GooseAcpClient previousClient = null;
            var completedProfiles = new List<string>();
            var completedOutputs = new List<KeyValuePair<string, string>>();
            var state = new AiDynamicTaskState { LastReviewHandoff = runtime.LastReviewHandoff };
            string stopReason = null;
            string finalMessage = null;
            string nextPrompt = BuildInitialCoordinationPrompt(
                originalRequest,
                permissionProfile,
                fullPermissionEnabled,
                runtime.LastReviewHandoff);
            var currentStage = new AiTaskCapabilityStage
            {
                Index = 0,
                Profile = AutomationToolProfiles.TaskCoordinator,
                Objective = "根据用户目标申请第一个能力包、结束或询问用户。"
            };
            bool attachmentsSent = false;
            bool stageStarted = false;
            bool stageFinalized = false;
            int stageContinuationCount = 0;
            int invalidDecisionCount = 0;
            AiTurnEvidence stageEvidence = AiTurnEvidence.Empty;
            AiStageArtifact stageArtifact = AiStageArtifact.Empty;
            string stageOutput = string.Empty;
            string decisionSubmittingProfile = null;
            try
            {
                while (true)
                {
                    runtime.Cancellation.Token.ThrowIfCancellationRequested();
                    runtime.StageTransitioning = true;
                    try
                    {
                        client = await clientResolver(currentStage).ConfigureAwait(false);
                    }
                    finally
                    {
                        runtime.StageTransitioning = false;
                    }
                    bool clientChanged = previousClient != null && !ReferenceEquals(previousClient, client);
                    bool coordinatorStage = string.Equals(
                        currentStage.Profile, AutomationToolProfiles.TaskCoordinator, StringComparison.Ordinal);
                    bool shouldSendAttachments = !attachmentsSent;
                    IReadOnlyList<GooseFileAttachment> promptAttachments = shouldSendAttachments
                        ? attachments
                        : Array.Empty<GooseFileAttachment>();

                    if (!coordinatorStage && !stageStarted)
                    {
                        stageStarted = true;
                        AiAnalysisLogger.Write(new Newtonsoft.Json.Linq.JObject
                        {
                            ["event"] = "capability.stage.started",
                            ["conversationId"] = runtime.Conversation?.Id ?? string.Empty,
                            ["stageIndex"] = currentStage.Index,
                            ["profile"] = currentStage.Profile,
                            ["clientRebuilt"] = clientChanged
                        });
                    }

                    Newtonsoft.Json.Linq.JObject promptResult = await client.PromptAsync(
                        nextPrompt,
                        promptAttachments,
                        runtime.Cancellation.Token).ConfigureAwait(false);
                    attachmentsSent = true;
                    previousClient = client;

                    if (!coordinatorStage && !stageFinalized)
                    {
                        stageEvidence = AiTurnEvidence.Merge(stageEvidence, client.LastTurnEvidence);
                        stageArtifact = AiStageArtifact.Merge(stageArtifact, client.LastTurnArtifact);
                        string latestOutput = client.LastAssistantResponse;
                        if (!string.IsNullOrWhiteSpace(latestOutput)) stageOutput = latestOutput;
                        lastWorkerClient = client;
                    }

                    TaskCapabilityDecisionDefinition decision = client.LastSubmittedTaskDecision;
                    bool hasDecision = client.LastTaskDecisionSubmissionCount > 0;
                    if (!hasDecision && !coordinatorStage && !stageFinalized)
                    {
                        stageContinuationCount++;
                        string modelStopReason = promptResult?["stopReason"]?.ToString() ?? "unknown";
                        int lastTurnToolCallCount = client.LastTurnToolCallCount;
                        if (ShouldStopStageContinuation(
                            modelStopReason,
                            stageContinuationCount,
                            lastTurnToolCallCount))
                        {
                            AiAnalysisLogger.Write(new Newtonsoft.Json.Linq.JObject
                            {
                                ["event"] = "capability.stage.stalled",
                                ["conversationId"] = runtime.Conversation?.Id ?? string.Empty,
                                ["stageIndex"] = currentStage.Index,
                                ["profile"] = currentStage.Profile,
                                ["continuation"] = stageContinuationCount,
                                ["stopReason"] = modelStopReason,
                                ["lastTurnToolCallCount"] = lastTurnToolCallCount,
                                ["reason"] = "repeated_max_tokens_without_tool_progress"
                            });
                            throw new InvalidOperationException(
                                $"{currentStage.Profile} 再次达到输出上限且本轮没有工具进展，已停止自动续写，避免继续空转。" );
                        }
                        if (stageContinuationCount <= 3)
                        {
                            AiAnalysisLogger.Write(new Newtonsoft.Json.Linq.JObject
                            {
                                ["event"] = "capability.stage.continuing",
                                ["conversationId"] = runtime.Conversation?.Id ?? string.Empty,
                                ["stageIndex"] = currentStage.Index,
                                ["profile"] = currentStage.Profile,
                                ["continuation"] = stageContinuationCount,
                                ["stopReason"] = modelStopReason,
                                ["lastTurnToolCallCount"] = lastTurnToolCallCount
                            });
                            nextPrompt = BuildStageContinuationPrompt(
                                currentStage.Profile,
                                modelStopReason);
                            continue;
                        }
                        throw new InvalidOperationException(
                            $"{currentStage.Profile} 连续 3 次续写仍未提交 request_capability，阶段未记为完成。" );
                    }

                    if (hasDecision && !coordinatorStage && !stageFinalized)
                    {
                        stageFinalized = true;
                        decisionSubmittingProfile = currentStage.Profile;
                        completedProfiles.Add(currentStage.Profile);
                        completedOutputs.Add(new KeyValuePair<string, string>(
                            currentStage.Profile,
                            stageOutput ?? string.Empty));
                        runtime.RecoveryContext = BuildStageRecoveryContext(
                            originalRequest,
                            currentStage,
                            stageEvidence,
                            stageArtifact,
                            stageOutput);
                        AiTaskCapabilityPolicy.RecordStage(state, currentStage, stageEvidence);
                        AiAnalysisLogger.Write(new Newtonsoft.Json.Linq.JObject
                        {
                            ["event"] = "capability.stage.completed",
                            ["conversationId"] = runtime.Conversation?.Id ?? string.Empty,
                            ["stageIndex"] = currentStage.Index,
                            ["profile"] = currentStage.Profile,
                            ["currentStateReadSucceeded"] = stageEvidence.CurrentStateReadSucceeded,
                            ["configurationSaved"] = stageEvidence.ConfigurationSaved,
                            ["changeSetCommitted"] = stageEvidence.ChangeSetCommitted,
                            ["migrationCommitted"] = stageEvidence.MigrationCommitted,
                            ["previewCreated"] = stageEvidence.PreviewCreated,
                            ["mutationAttempted"] = stageEvidence.MutationAttempted,
                            ["verificationSucceeded"] = stageEvidence.VerificationSucceeded,
                            ["previewRejected"] = stageEvidence.PreviewRejected,
                            ["sourceFilesChanged"] = stageEvidence.SourceFilesChanged,
                            ["sourceMutationUncertain"] = stageEvidence.SourceMutationUncertain,
                            ["designKnowledgeReadSucceeded"] = stageEvidence.DesignKnowledgeReadSucceeded,
                            ["unsafeMutationFailure"] = stageEvidence.UnsafeMutationFailure,
                            ["toolFailureCount"] = stageEvidence.ToolFailureCount
                        });
                        if (currentStage.RequiresUserContinuationBeforeNext)
                        {
                            RequestTrustedContextRolloverIfNeeded(runtime, client);
                            stopReason = $"{currentStage.Profile} 已完成；用户要求先查看或确认该阶段结果，后续能力需等待新的用户消息。";
                            break;
                        }
                        if (stageEvidence.PreviewRejected)
                        {
                            RequestTrustedContextRolloverIfNeeded(runtime, client);
                            stopReason = $"{currentStage.Profile} 的预演被用户拒绝，当前任务已停止。";
                            break;
                        }
                        if (stageEvidence.SourceFilesChanged)
                        {
                            RequestTrustedContextRolloverIfNeeded(runtime, client);
                            stopReason = "源码已经修改；必须重新构建并让 Automation 加载新版本后再继续其他能力。";
                            break;
                        }
                        if (stageEvidence.SourceMutationUncertain)
                        {
                            RequestTrustedContextRolloverIfNeeded(runtime, client);
                            stopReason = "源码阶段执行过可间接写入的 Shell；当前实例无法证明仍对应磁盘源码，必须重新构建并加载后再继续。";
                            break;
                        }
                    }

                    AiTaskDecisionValidation validation = hasDecision
                        ? AiTaskCapabilityPolicy.Validate(
                            decision,
                            originalRequest,
                            permissionProfile,
                            fullPermissionEnabled,
                            state,
                            decisionSubmittingProfile)
                        : AiTaskDecisionValidation.Invalid(
                            "本轮没有调用 request_capability。");
                    AiAnalysisLogger.Write(new Newtonsoft.Json.Linq.JObject
                    {
                        ["event"] = "capability.requested",
                        ["conversationId"] = runtime.Conversation?.Id ?? string.Empty,
                        ["stageIndex"] = state.CompletedStageCount,
                        ["action"] = decision?.Action ?? string.Empty,
                        ["profile"] = decision?.Capability ?? string.Empty,
                        ["submissionCount"] = client.LastTaskDecisionSubmissionCount,
                        ["additionalDecisionsIgnored"] = Math.Max(0, client.LastTaskDecisionSubmissionCount - 1),
                        ["validation"] = validation.Kind.ToString(),
                        ["message"] = validation.Message ?? string.Empty
                    });
                    if (validation.Kind == AiTaskDecisionKind.Invalid)
                    {
                        invalidDecisionCount++;
                        if (invalidDecisionCount >= 2)
                            throw new InvalidOperationException(
                                "动态能力申请连续两次不合法，未继续执行：" + validation.Message);
                        nextPrompt = "你的上一条 request_capability 未通过代码校验：\n"
                            + validation.Message
                            + "\n当前工作阶段已经结束。不要输出说明，也不要重复业务工具；只重新调用一次 request_capability。";
                        currentStage = new AiTaskCapabilityStage
                        {
                            Index = state.CompletedStageCount,
                            Profile = AutomationToolProfiles.TaskCoordinator,
                            Objective = "纠正上一条未通过代码校验的能力决定。"
                        };
                        continue;
                    }
                    invalidDecisionCount = 0;
                    if (validation.ReviewHandoff != null)
                    {
                        state.LastReviewHandoff = validation.ReviewHandoff;
                        runtime.LastReviewHandoff = validation.ReviewHandoff;
                        runtime.Conversation.ReviewHandoff = validation.ReviewHandoff;
                    }
                    if (validation.Kind == AiTaskDecisionKind.Finish)
                    {
                        RequestTrustedContextRolloverIfNeeded(runtime, client);
                        finalMessage = BuildTrustedReviewOutput(
                            validation.Message,
                            validation.ReviewHandoff);
                        break;
                    }
                    if (validation.Kind == AiTaskDecisionKind.AskUser)
                    {
                        RequestTrustedContextRolloverIfNeeded(runtime, client);
                        finalMessage = validation.Message;
                        stopReason = "需要用户补充信息后才能继续：" + validation.Message;
                        break;
                    }
                    currentStage = validation.Stage;
                    nextPrompt = BuildDynamicStagePrompt(
                        originalRequest,
                        currentStage);
                    stageStarted = false;
                    stageFinalized = false;
                    stageContinuationCount = 0;
                    stageEvidence = AiTurnEvidence.Empty;
                    stageArtifact = AiStageArtifact.Empty;
                    stageOutput = string.Empty;
                    decisionSubmittingProfile = null;
                }
                runtime.Status = string.IsNullOrWhiteSpace(stopReason) ? "已完成" : "部分完成";
                string assistantText = BuildFinalAssistantOutput(completedOutputs, finalMessage);
                return BuildTaskResult(
                    AiTaskExecutionStatus.Completed,
                    runtime,
                    lastWorkerClient,
                    null,
                    completedProfiles,
                    stopReason,
                    assistantText,
                    finalMessage);
            }
            catch (OperationCanceledException)
            {
                RequestTrustedContextRolloverIfNeeded(runtime, client ?? lastWorkerClient);
                runtime.Status = "已停止";
                return BuildTaskResult(
                    AiTaskExecutionStatus.Cancelled,
                    runtime,
                    lastWorkerClient,
                    null,
                    completedProfiles,
                    stopReason,
                    BuildFinalAssistantOutput(completedOutputs, null));
            }
            catch (Exception ex)
            {
                RequestTrustedContextRolloverIfNeeded(runtime, client ?? lastWorkerClient);
                runtime.Status = "失败";
                return BuildTaskResult(
                    AiTaskExecutionStatus.Failed,
                    runtime,
                    lastWorkerClient,
                    ex.Message,
                    completedProfiles,
                    stopReason,
                    BuildFinalAssistantOutput(completedOutputs, null));
            }
            finally
            {
                runtime.StageTransitioning = false;
                runtime.Running = false;
                runtime.Cancellation?.Dispose();
                runtime.Cancellation = null;
            }
        }

        public void CompleteTask(
            AiTaskRuntime runtime,
            string assistantText,
            string visualizationJson)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                runtime.Conversation.Messages.Add(new AiConversationMessage
                {
                    Role = "assistant",
                    Text = assistantText,
                    Time = DateTime.Now,
                    VisualizationJson = visualizationJson
                });
            }
            runtime.Conversation.UpdatedAt = DateTime.Now;
            runtime.RecoveryContext = BuildRestoredContext(runtime.Conversation);
            runtime.PendingEvents.Clear();
        }

        public bool TryLoad(out string error)
        {
            error = null;
            try
            {
                Conversations.Clear();
                Conversations.AddRange(AiConversationStorage.Load());
            }
            catch (Exception ex)
            {
                Conversations.Clear();
                error = ex.Message;
            }

            DisposeClients();
            TaskRuntimes.Clear();
            foreach (AiConversation conversation in Conversations)
            {
                TaskRuntimes[conversation.Id] = new AiTaskRuntime
                {
                    Conversation = conversation,
                    Status = "已完成",
                    RestoredContext = BuildRestoredContext(conversation),
                    LastReviewHandoff = conversation.ReviewHandoff
                };
            }
            ActiveConversation = null;
            TaskHomeVisible = true;
            return error == null;
        }

        public AiTaskRuntime StartNew()
        {
            DateTime now = DateTime.Now;
            var conversation = new AiConversation
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "新对话",
                CreatedAt = now,
                UpdatedAt = now
            };
            var runtime = new AiTaskRuntime
            {
                Conversation = conversation,
                Status = "等待开始"
            };
            Conversations.Insert(0, conversation);
            TaskRuntimes[conversation.Id] = runtime;
            ActiveConversation = conversation;
            TaskHomeVisible = false;
            TrimOldConversations();
            return runtime;
        }

        public AiTaskRuntime EnsureActive(bool forceNew)
        {
            AiTaskRuntime activeRuntime = ActiveRuntime;
            if (forceNew || TaskHomeVisible || ActiveConversation == null || activeRuntime == null)
            {
                return StartNew();
            }
            return activeRuntime;
        }

        public bool TryDeleteActive(out string error)
        {
            error = null;
            if (ActiveConversation == null)
            {
                error = "当前没有可删除的会话。";
                return false;
            }
            AiTaskRuntime activeRuntime = ActiveRuntime;
            if (activeRuntime?.Running == true)
            {
                error = "当前会话仍在运行。";
                return false;
            }

            string deletedId = ActiveConversation.Id;
            if (TaskRuntimes.TryGetValue(deletedId, out AiTaskRuntime runtime))
            {
                DisposeRuntime(runtime);
                TaskRuntimes.Remove(deletedId);
            }
            Conversations.RemoveAll(item => string.Equals(item.Id, deletedId, StringComparison.Ordinal));
            ActiveConversation = null;
            TaskHomeVisible = true;
            return true;
        }

        public bool TrySwitch(string conversationId, out AiTaskRuntime runtime, out string error)
        {
            runtime = null;
            error = null;
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                error = "会话标识为空。";
                return false;
            }
            AiConversation target = Conversations.FirstOrDefault(item =>
                string.Equals(item.Id, conversationId, StringComparison.Ordinal));
            if (target == null)
            {
                error = "未找到该历史会话。";
                return false;
            }
            ActiveConversation = target;
            if (!TaskRuntimes.TryGetValue(target.Id, out runtime))
            {
                runtime = new AiTaskRuntime
                {
                    Conversation = target,
                    Status = "已完成",
                    RestoredContext = BuildRestoredContext(target),
                    LastReviewHandoff = target.ReviewHandoff
                };
                TaskRuntimes[target.Id] = runtime;
            }
            TaskHomeVisible = false;
            return true;
        }

        public void ShowHome()
        {
            ActiveConversation = null;
            TaskHomeVisible = true;
        }

        public bool TrySave(out string error)
        {
            error = null;
            try
            {
                Conversations.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
                TrimOldConversations();
                AiConversationStorage.Save(Conversations);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public GooseAcpClient GetOrCreateClient(
            AiTaskRuntime runtime,
            Func<GooseAcpClient> factory)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (clientLock)
            {
                if (runtime.Client == null) runtime.Client = factory();
                return runtime.Client;
            }
        }

        public void ResetClientForCapability(AiTaskRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (runtime.Running && !runtime.StageTransitioning)
                throw new InvalidOperationException("AI 任务运行中不能切换能力包。");
            lock (clientLock)
            {
                // 正常阶段切换不进入这里；只有会话丢失或配置重置时才注入恢复上下文。
                // 任务内优先使用最近阶段的机械证据，普通用户轮次使用持久化最终消息。
                runtime.RestoredContext = runtime.StageTransitioning
                    && !string.IsNullOrWhiteSpace(runtime.RecoveryContext)
                    ? runtime.RecoveryContext
                    : BuildRestoredContext(runtime.Conversation);
                runtime.Client?.Dispose();
                runtime.Client = null;
            }
        }

        public void DisposeClients()
        {
            lock (clientLock)
            {
                foreach (AiTaskRuntime runtime in TaskRuntimes.Values)
                {
                    DisposeRuntime(runtime);
                }
            }
        }

        public void Cancel(AiTaskRuntime runtime)
        {
            if (runtime == null) return;
            runtime.Cancellation?.Cancel();
            lock (clientLock)
            {
                runtime.Client?.Cancel();
            }
        }

        private void TrimOldConversations()
        {
            while (Conversations.Count > AiConversationStorage.MaxConversationCount)
            {
                AiConversation oldest = Conversations
                    .OrderBy(item => item.UpdatedAt)
                    .FirstOrDefault(item => !TaskRuntimes.TryGetValue(item.Id, out AiTaskRuntime runtime)
                        || !runtime.Running);
                if (oldest == null)
                {
                    // 所有超额会话均在运行时暂不剪裁，避免历史数量上限中断正在执行的任务。
                    break;
                }
                Conversations.Remove(oldest);
                if (TaskRuntimes.TryGetValue(oldest.Id, out AiTaskRuntime runtime))
                {
                    DisposeRuntime(runtime);
                    TaskRuntimes.Remove(oldest.Id);
                }
            }
        }

        private static string BuildRestoredContext(AiConversation conversation)
        {
            var builder = new StringBuilder();
            foreach (AiConversationMessage message in (conversation?.Messages
                    ?? new List<AiConversationMessage>())
                .Reverse<AiConversationMessage>()
                .Take(8)
                .Reverse())
            {
                builder.Append(message.Role == "user" ? "用户：" : "EW-AI：");
                builder.AppendLine(message.Text);
            }
            return Tail(builder.ToString(), 12000);
        }

        private static AiTaskExecutionResult BuildTaskResult(
            AiTaskExecutionStatus status,
            AiTaskRuntime runtime,
            GooseAcpClient client,
            string error,
            IReadOnlyList<string> completedStageProfiles = null,
            string stageStopReason = null,
            string assistantText = null,
            string coordinatorMessage = null)
        {
            return new AiTaskExecutionResult
            {
                Status = status,
                Client = client,
                Error = error,
                Events = runtime.PendingEvents.ToList(),
                CompletedStageProfiles = completedStageProfiles ?? Array.Empty<string>(),
                StageStopReason = stageStopReason,
                AssistantText = assistantText,
                CoordinatorMessage = coordinatorMessage
            };
        }

        internal static string BuildFinalAssistantOutput(
            IReadOnlyList<KeyValuePair<string, string>> outputs,
            string coordinatorMessage)
        {
            if (!string.IsNullOrWhiteSpace(coordinatorMessage))
                return coordinatorMessage.Trim();
            string finalStageOutput = outputs?
                .Reverse()
                .Select(output => output.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(finalStageOutput))
                return finalStageOutput.Trim();
            return string.Empty;
        }

        internal static bool ShouldStopStageContinuation(
            string modelStopReason,
            int continuationCount,
            int lastTurnToolCallCount)
        {
            return string.Equals(modelStopReason, "max_tokens", StringComparison.OrdinalIgnoreCase)
                && continuationCount > 1
                && lastTurnToolCallCount == 0;
        }

        internal static string BuildStageContinuationPrompt(
            string profile,
            string modelStopReason)
        {
            string recovery = string.Equals(
                modelStopReason, "max_tokens", StringComparison.OrdinalIgnoreCase)
                ? "上一轮达到输出上限。停止展开分析、复述结果或比较更多候选，立即执行下一个可验证动作。"
                : "继续当前能力阶段，不要重做已成功的工具调用。";
            if (string.Equals(profile, AutomationToolProfiles.ProcessCreate, StringComparison.Ordinal))
            {
                recovery += "复杂新建流程无需一次生成全部实现：先用autoStart=false和config.placeholder预演安全骨架，提交并验证后申请ProcessEdit分段补齐；允许incomplete时不要因资源歧义空转。";
            }
            return recovery
                + "基于当前原生会话已有工具结果完成剩余工作；若不能安全继续则直接ask_user或finish。"
                + "结束时必须只调用一次request_capability，不要再输出阶段总结。";
        }

        internal static string BuildTrustedReviewOutput(
            string modelMessage,
            ReviewHandoffDefinition handoff)
        {
            List<ReviewVerifiedFactDefinition> facts = handoff?.VerifiedFacts
                ?? new List<ReviewVerifiedFactDefinition>();
            var referencedFacts = new HashSet<string>(
                (handoff?.Findings ?? new List<ReviewFindingDefinition>())
                    .SelectMany(finding => finding?.EvidenceFactRefs ?? new List<string>())
                    .Where(reference => !string.IsNullOrWhiteSpace(reference)),
                StringComparer.Ordinal);
            List<ReviewVerifiedFactDefinition> renderedFacts = (referencedFacts.Count > 0
                    ? facts.Where(fact => referencedFacts.Contains(
                        ReviewFactReference.Build(fact.SubjectId, fact.Key)))
                    : facts.Where(fact => (fact.Key ?? string.Empty).StartsWith(
                        "proc.", StringComparison.Ordinal)))
                .ToList();
            var builder = new StringBuilder();
            if (renderedFacts.Count == 0)
            {
                builder.AppendLine("【证据状态】");
                builder.AppendLine("本轮没有形成宿主机械事实；以下内容只能作为解释或待验证判断，不能作为已证明缺陷依据。");
                if (!string.IsNullOrWhiteSpace(modelMessage))
                {
                    builder.AppendLine();
                    builder.Append(modelMessage.Trim());
                }
                return builder.ToString().Trim();
            }
            builder.AppendLine("【机械验证事实（以此为准）】");
            builder.AppendLine("以下值直接来自本次成功工具结果；若后文模型解释中的精确数量与这里冲突，以本区为准。");
            foreach (IGrouping<string, ReviewVerifiedFactDefinition> group in renderedFacts
                .GroupBy(item => item.SubjectId, StringComparer.Ordinal)
                .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                ReviewVerifiedFactDefinition first = group.First();
                var byKey = group.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                builder.Append("- ").Append(first.SubjectName).Append("（").Append(first.SubjectId).Append("）");
                AppendVerifiedFact(builder, byKey, "proc.procIndex", "procIndex");
                AppendVerifiedFact(builder, byKey, "proc.isValid", "结构有效");
                AppendVerifiedFact(builder, byKey, "proc.readinessStatus", "就绪状态");
                AppendVerifiedFact(builder, byKey, "proc.runnable", "可运行");
                AppendVerifiedFact(builder, byKey, "proc.placeholderWarningCount", "占位警告");
                AppendVerifiedFact(builder, byKey, "proc.runBlockerCount", "运行阻塞");
                AppendVerifiedFact(builder, byKey, "proc.nonPlaceholderBlockerCount", "非占位阻塞");
                var renderedKeys = new HashSet<string>(new[]
                {
                    "proc.procIndex", "proc.isValid", "proc.readinessStatus", "proc.runnable",
                    "proc.placeholderWarningCount", "proc.runBlockerCount", "proc.nonPlaceholderBlockerCount"
                }, StringComparer.Ordinal);
                foreach (ReviewVerifiedFactDefinition fact in group
                    .Where(item => !renderedKeys.Contains(item.Key))
                    .OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    builder.Append("；").Append(fact.Key).Append('=').Append(fact.Value);
                }
                builder.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(modelMessage))
            {
                builder.AppendLine();
                builder.AppendLine("【评审解释】");
                builder.Append(modelMessage.Trim());
            }
            return builder.ToString().Trim();
        }

        private static void AppendVerifiedFact(
            StringBuilder builder,
            IReadOnlyDictionary<string, string> facts,
            string key,
            string label)
        {
            if (facts.TryGetValue(key, out string value))
            {
                builder.Append("；").Append(label).Append('=').Append(value);
            }
        }

        private static string BuildInitialCoordinationPrompt(
            string originalRequest,
            string permissionProfile,
            bool fullPermissionEnabled,
            ReviewHandoffDefinition lastReviewHandoff)
        {
            string handoff = lastReviewHandoff == null
                ? "无。"
                : $"status={lastReviewHandoff.Status}; summary={lastReviewHandoff.Summary}; findingIds="
                    + string.Join(",", (lastReviewHandoff.Findings ?? new List<ReviewFindingDefinition>())
                        .Select(item => item.Id));
            return "只通过 request_capability 提交一个下一步决定，不输出计划或重复总结。\n"
                + "完整用户目标：\n" + originalRequest.Trim()
                + "\n\n用户权限外壳：" + permissionProfile
                + "；完全权限：" + (fullPermissionEnabled ? "已开启" : "未开启")
                + "。"
                + "\n最近一次结构化评审交接：" + handoff
                + "\n权限、授权引用、ProcessEdit basis 和合法迁移以 request_capability Schema 及代码校验为准；权限不足时使用 ask_user 或 finish。";
        }

        private static string BuildDynamicStagePrompt(
            string originalRequest,
            AiTaskCapabilityStage stage)
        {
            string completionInstruction = stage.RequiresUserContinuationBeforeNext
                ? "\n完成后调用 request_capability 的 finish，把给用户查看的阶段结果只放在 message；执行器随后暂停，不申请下一能力。"
                : "\n完成工具工作后只调用一次 request_capability：需要继续就申请一个能力，全部完成就把最终用户答复放在 finish.message，需要信息就放在 ask_user.message。调用前后不要另写重复总结。";
            return "完整用户目标：\n" + originalRequest.Trim()
                + "\n\n当前获批能力：" + stage.Profile
                + "\n当前阶段目标：" + stage.Objective
                + "\n当前 Goose 原生会话已经保留此前对话、工具结果和附件，不重复注入上一阶段摘要。"
                + "\n只完成当前阶段，不猜测或提前执行后续能力。"
                + completionInstruction;
        }

        private static string BuildStageRecoveryContext(
            string originalRequest,
            AiTaskCapabilityStage stage,
            AiTurnEvidence evidence,
            AiStageArtifact artifact,
            string stageOutput)
        {
            return "以下内容仅用于 Goose 原生会话意外丢失后的可信恢复，不代表新的用户授权。"
                + "\n完整用户目标：\n" + originalRequest.Trim()
                + "\n最近完成阶段：" + stage.Profile
                + "\n机械证据：currentStateRead=" + (evidence?.CurrentStateReadSucceeded ?? false)
                + ", configurationSaved=" + (evidence?.ConfigurationSaved ?? false)
                + ", changeSetCommitted=" + (evidence?.ChangeSetCommitted ?? false)
                + ", migrationCommitted=" + (evidence?.MigrationCommitted ?? false)
                + ", previewCreated=" + (evidence?.PreviewCreated ?? false)
                + ", mutationAttempted=" + (evidence?.MutationAttempted ?? false)
                + ", verificationSucceeded=" + (evidence?.VerificationSucceeded ?? false)
                + ", designKnowledgeRead=" + (evidence?.DesignKnowledgeReadSucceeded ?? false)
                + ", unsafeMutationFailure=" + (evidence?.UnsafeMutationFailure ?? false)
                + ", sourceMutationUncertain=" + (evidence?.SourceMutationUncertain ?? false)
                + ", toolFailureCount=" + (evidence?.ToolFailureCount ?? 0)
                + (artifact?.HasFacts == true
                    ? "\n机械阶段产物（稳定ID、绑定与验证事实）：\n" + Tail(artifact.ToCompactJson(), 12000)
                    : string.Empty)
                + "\n最近阶段最终输出（其中推断不自动成为事实）：\n"
                + Tail((stageOutput ?? string.Empty).Trim(), 6000);
        }

        private static void RequestTrustedContextRolloverIfNeeded(
            AiTaskRuntime runtime,
            GooseAcpClient client)
        {
            if (runtime == null || client == null
                || runtime.TrustedContextRolloverRequested)
                return;
            if (!ShouldRequestTrustedContextRollover(
                client.LastPromptInputTokens,
                client.LastTurnToolResultBytes,
                client.ContextWindowTokens,
                client.ReservedOutputTokens)) return;
            long contextPressureThreshold = CalculateContextPressureThreshold(
                client.ContextWindowTokens,
                client.ReservedOutputTokens);
            runtime.TrustedContextRolloverRequested = true;
            AiAnalysisLogger.Write(new Newtonsoft.Json.Linq.JObject
            {
                ["event"] = "context.rollover.requested",
                ["conversationId"] = runtime.Conversation?.Id ?? string.Empty,
                ["gooseSessionId"] = client.SessionId ?? string.Empty,
                ["cumulativeInputTokens"] = client.CumulativeInputTokens,
                ["lastPromptInputTokens"] = client.LastPromptInputTokens,
                ["lastTurnToolResultBytes"] = client.LastTurnToolResultBytes,
                ["estimatedToolResultTokens"] = EstimateTokensFromUtf8Bytes(client.LastTurnToolResultBytes),
                ["contextWindowTokens"] = client.ContextWindowTokens,
                ["reservedOutputTokens"] = client.ReservedOutputTokens,
                ["thresholds"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["contextPressureTokens"] = contextPressureThreshold,
                    ["contextPressureRatio"] = 0.55d
                },
                ["state"] = "deferred_until_next_user_request"
            });
        }

        internal static bool ShouldRequestTrustedContextRollover(
            long lastPromptInputTokens,
            long lastTurnToolResultBytes,
            int contextWindowTokens,
            int reservedOutputTokens)
        {
            long threshold = CalculateContextPressureThreshold(
                contextWindowTokens,
                reservedOutputTokens);
            if (lastPromptInputTokens >= threshold) return true;
            long estimatedToolTokens = EstimateTokensFromUtf8Bytes(lastTurnToolResultBytes);
            return lastPromptInputTokens >= contextWindowTokens * 35L / 100L
                && lastPromptInputTokens + estimatedToolTokens >= threshold;
        }

        private static long CalculateContextPressureThreshold(
            int contextWindowTokens,
            int reservedOutputTokens)
        {
            long context = Math.Max(32768, contextWindowTokens);
            long reserve = Math.Max(8192, reservedOutputTokens);
            return Math.Max(24576, context * 55L / 100L - reserve);
        }

        private static long EstimateTokensFromUtf8Bytes(long bytes)
        {
            if (bytes <= 0) return 0;
            return (bytes + 2L) / 3L;
        }

        private static string Tail(string value, int maxCharacters)
        {
            string text = value ?? string.Empty;
            if (text.Length <= maxCharacters) return text;
            return "…（前文已压缩）\n" + text.Substring(text.Length - maxCharacters, maxCharacters);
        }

        private static void DisposeRuntime(AiTaskRuntime runtime)
        {
            if (runtime == null) return;
            try
            {
                runtime.Cancellation?.Cancel();
                runtime.Client?.Dispose();
            }
            catch
            {
            }
            runtime.Client = null;
            runtime.Cancellation?.Dispose();
            runtime.Cancellation = null;
            runtime.Running = false;
        }
    }

    internal enum AiTaskExecutionStatus
    {
        Completed,
        Cancelled,
        Failed
    }

    internal sealed class AiTaskExecutionResult
    {
        public AiTaskExecutionStatus Status { get; internal set; }
        public GooseAcpClient Client { get; internal set; }
        public string Error { get; internal set; }
        public IReadOnlyList<GooseAcpEvent> Events { get; internal set; }
        public IReadOnlyList<string> CompletedStageProfiles { get; internal set; }
        public string StageStopReason { get; internal set; }
        public string AssistantText { get; internal set; }
        public string CoordinatorMessage { get; internal set; }
    }

    internal sealed class AiTaskRuntime
    {
        public AiConversation Conversation { get; set; }
        public GooseAcpClient Client { get; set; }
        public CancellationTokenSource Cancellation { get; set; }
        public List<GooseAcpEvent> PendingEvents { get; } = new List<GooseAcpEvent>();
        public bool Running { get; set; }
        public string Status { get; set; } = "已完成";
        public string RestoredContext { get; set; }
        public string CapabilityProfile { get; set; }
        public string CapabilityMcpUri { get; set; }
        public int CapabilityStageIndex { get; set; } = -1;
        public bool StageTransitioning { get; set; }
        public string RecoveryContext { get; set; }
        public bool TrustedContextRolloverRequested { get; set; }
        public ReviewHandoffDefinition LastReviewHandoff { get; set; }
    }
}
