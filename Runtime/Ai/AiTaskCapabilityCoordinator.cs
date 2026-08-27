using Automation.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 校验协调模型提交的单步决定。模型理解用户意图；代码只守住结构、权限和合法迁移。
    /// </summary>
    internal static class AiTaskCapabilityPolicy
    {
        public static AiTaskDecisionValidation Validate(
            TaskCapabilityDecisionDefinition decision,
            string currentUserRequest,
            string permissionProfile,
            bool fullPermissionEnabled,
            AiDynamicTaskState state)
        {
            if (decision == null) return AiTaskDecisionValidation.Invalid("协调模型没有提交结构化决定。");
            if (decision.Version != 1) return AiTaskDecisionValidation.Invalid("任务决定 version 必须为 1。");
            string action = (decision.Action ?? string.Empty).Trim();
            if (!string.Equals(action, "run_stage", StringComparison.Ordinal))
                return AiTaskDecisionValidation.Invalid(
                    "request_capability 只接受 action=run_stage；完成或需要用户补充时直接正常回复。" );
            if (state == null) return AiTaskDecisionValidation.Invalid("动态任务状态不存在。");
            if (string.IsNullOrWhiteSpace(decision.Objective))
                return AiTaskDecisionValidation.Invalid("run_stage 决定必须提供当前阶段目标。");
            if (decision.Objective.Trim().Length > 500)
                return AiTaskDecisionValidation.Invalid("当前阶段目标不能超过 500 个字符。");

            string capability;
            try
            {
                capability = AutomationToolProfiles.Normalize(decision.Capability);
            }
            catch (Exception ex)
            {
                return AiTaskDecisionValidation.Invalid(ex.Message);
            }
            if (!AutomationToolProfiles.IsExecutionProfile(capability))
                return AiTaskDecisionValidation.Invalid(
                    $"{capability} 不是可执行能力包。可申请的能力包："
                    + string.Join("、", AutomationToolProfiles.ExecutionProfiles)
                    + "。");
            string editBasisError = ValidateProcessEditBasis(decision, capability, state);
            if (editBasisError != null) return AiTaskDecisionValidation.Invalid(editBasisError);
            if (RequiresExplicitAuthorizationQuote(capability))
            {
                string quote = (decision.AuthorizationQuote ?? string.Empty).Trim();
                // 最小长度防止过短子串（如单字）形式上命中却无法证明授权语义。
                if (string.IsNullOrWhiteSpace(quote)
                    || quote.Length < 4
                    || string.IsNullOrWhiteSpace(currentUserRequest)
                    || currentUserRequest.IndexOf(quote, StringComparison.Ordinal) < 0)
                {
                    return AiTaskDecisionValidation.Invalid(
                        $"{capability} 是高风险副作用能力，authorizationQuote 必须逐字来自当前用户消息且不少于4个字符。");
                }
            }

            bool diagnostic = string.Equals(
                permissionProfile, AutomationToolProfiles.Diagnostic, StringComparison.OrdinalIgnoreCase);
            if (diagnostic && (IsConfigurationMutation(capability)
                || string.Equals(capability, AutomationToolProfiles.RuntimeControl, StringComparison.Ordinal)
                || string.Equals(capability, AutomationToolProfiles.SourceDevelopment, StringComparison.Ordinal)))
            {
                return AiTaskDecisionValidation.Invalid(
                    $"当前只读诊断权限不允许执行 {capability}。可申请的只读能力："
                    + $"{AutomationToolProfiles.ProcessDesign}/{AutomationToolProfiles.ProcessReview}/{AutomationToolProfiles.SourceReview}"
                    + "；资源配置（工站/轴/点位/IO/通讯）的只读查询在 ProcessReview 能力内。");
            }
            if (string.Equals(capability, AutomationToolProfiles.PlatformConfiguration, StringComparison.Ordinal)
                && !fullPermissionEnabled)
            {
                return AiTaskDecisionValidation.Invalid("PlatformConfiguration 需要用户明确开启完全权限。");
            }
            if (state.SourceFilesChanged)
                return AiTaskDecisionValidation.Invalid("源码已经改变，当前运行实例已过期，不能再调度其他能力包。");
            if (state.SourceMutationUncertain)
                return AiTaskDecisionValidation.Invalid("源码阶段执行过 Shell，当前运行实例是否对应磁盘源码不确定，不能再调度其他能力包。");
            if (state.PreviewRejected && IsSideEffectCapability(capability))
                return AiTaskDecisionValidation.Invalid("用户已经拒绝上一预演，当前任务不能继续申请副作用能力。");
            if (state.UnsafePartialMutation && IsSideEffectCapability(capability))
                return AiTaskDecisionValidation.Invalid("前一配置阶段存在失败或部分写入，不能继续写入或运行。");
            if (state.UncommittedMutation && IsSideEffectCapability(capability))
                return AiTaskDecisionValidation.Invalid("前一配置阶段没有取得已保存提交证据，不能继续写入或运行。");

            string transitionError = ValidateEvidenceTransition(state, capability);
            if (transitionError != null) return AiTaskDecisionValidation.Invalid(transitionError);

            return AiTaskDecisionValidation.Run(new AiTaskCapabilityStage
            {
                Index = state.CompletedStageCount,
                Profile = capability,
                Objective = decision.Objective.Trim()
            });
        }

        public static void RecordStage(
            AiDynamicTaskState state,
            AiTaskCapabilityStage stage,
            AiTurnEvidence evidence)
        {
            if (state == null || stage == null) return;
            evidence = evidence ?? AiTurnEvidence.Empty;
            state.CompletedStageCount++;
            state.PreviousStage = stage;
            state.PreviousEvidence = evidence;
            state.PreviewRejected |= evidence.PreviewRejected;
            state.SourceFilesChanged |= evidence.SourceFilesChanged;
            state.SourceMutationUncertain |= evidence.SourceMutationUncertain;
            if (IsConfigurationMutation(stage.Profile))
            {
                state.UnsafePartialMutation |= evidence.UnsafeMutationFailure;
                if (evidence.MutationAttempted)
                {
                    state.LastMutationProfile = stage.Profile;
                    state.LastMutationEvidence = evidence;
                    state.UncommittedMutation |= !HasSavedMutationEvidence(stage.Profile, evidence);
                }
            }
        }

        public static bool IsConfigurationMutation(string profile)
        {
            return string.Equals(profile, AutomationToolProfiles.ProcessCreate, StringComparison.Ordinal)
                || string.Equals(profile, AutomationToolProfiles.ProcessEdit, StringComparison.Ordinal)
                || string.Equals(profile, AutomationToolProfiles.ResourceEdit, StringComparison.Ordinal)
                || string.Equals(profile, AutomationToolProfiles.PlatformConfiguration, StringComparison.Ordinal);
        }

        internal static bool IsSideEffectCapability(string profile)
        {
            return IsConfigurationMutation(profile)
                || string.Equals(profile, AutomationToolProfiles.RuntimeControl, StringComparison.Ordinal)
                || string.Equals(profile, AutomationToolProfiles.SourceDevelopment, StringComparison.Ordinal);
        }

        internal static bool RequiresExplicitAuthorizationQuote(string profile)
        {
            return string.Equals(profile, AutomationToolProfiles.RuntimeControl, StringComparison.Ordinal)
                || string.Equals(profile, AutomationToolProfiles.SourceDevelopment, StringComparison.Ordinal)
                || string.Equals(profile, AutomationToolProfiles.PlatformConfiguration, StringComparison.Ordinal);
        }

        internal static string ValidateReviewHandoff(
            ReviewHandoffDefinition handoff,
            string submittingProfile)
        {
            bool reviewStage = string.Equals(
                submittingProfile, AutomationToolProfiles.ProcessReview, StringComparison.Ordinal);
            if (!reviewStage)
                return handoff == null ? null : "只有刚完成的 ProcessReview 阶段可以提交 reviewHandoff。";
            if (handoff == null) return "ProcessReview 结束时必须提交结构化 reviewHandoff。";
            string status = (handoff.Status ?? string.Empty).Trim();
            if (!new[]
                {
                    ReviewHandoffStatuses.ProvenDefect,
                    ReviewHandoffStatuses.ConfigurationGap,
                    ReviewHandoffStatuses.Unresolved,
                    ReviewHandoffStatuses.NoDefect
                }.Contains(status, StringComparer.Ordinal))
                return "reviewHandoff.status 不合法。合法值："
                    + ReviewHandoffStatuses.ProvenDefect + "、"
                    + ReviewHandoffStatuses.ConfigurationGap + "、"
                    + ReviewHandoffStatuses.Unresolved + "、"
                    + ReviewHandoffStatuses.NoDefect + "。";
            if (string.IsNullOrWhiteSpace(handoff.Summary))
                return "reviewHandoff.summary 不能为空。";
            List<ReviewFindingDefinition> findings = handoff.Findings ?? new List<ReviewFindingDefinition>();
            if (status == ReviewHandoffStatuses.ProvenDefect && findings.Count == 0)
                return "proven_defect 至少需要一个有直接证据的 finding。";
            if (status != ReviewHandoffStatuses.ProvenDefect && findings.Count > 0)
                return "只有 proven_defect 可以携带缺陷 findings。";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var verifiedFactRefs = new HashSet<string>(
                (handoff.VerifiedFacts ?? new List<ReviewVerifiedFactDefinition>())
                    .Select(fact => ReviewFactReference.Build(fact?.SubjectId, fact?.Key)),
                StringComparer.Ordinal);
            foreach (ReviewFindingDefinition finding in findings)
            {
                string id = (finding?.Id ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id))
                    return $"reviewHandoff.findings 第 {ids.Count + 1} 项的 id 为空。";
                if (!ids.Add(id))
                    return $"reviewHandoff.findings 的 id 重复：{id}。";
                if (string.IsNullOrWhiteSpace(finding.Summary))
                    return $"reviewHandoff finding[{id}] 的 summary 不能为空。";
                if (!ReviewFindingCategories.SupportedCategories.Split('、')
                        .Contains(finding.Category, StringComparer.Ordinal))
                    return $"reviewHandoff finding[{id}] 的 category 不合法：{finding.Category}。合法值："
                        + ReviewFindingCategories.SupportedCategories + "。";
                if (!ReviewFindingRepairability.SupportedValues.Split('、')
                        .Contains(finding.Repairability, StringComparer.Ordinal))
                    return $"reviewHandoff finding[{id}] 的 repairability 不合法：{finding.Repairability}。合法值："
                        + ReviewFindingRepairability.SupportedValues + "。";
                if (string.IsNullOrWhiteSpace(finding.Evidence))
                    return $"reviewHandoff finding[{id}] 的 evidence 不能为空。";
                if (string.IsNullOrWhiteSpace(finding.MinimalChange))
                    return $"reviewHandoff finding[{id}] 的 minimalChange 不能为空。";
                if ((finding.TargetIds?.Count ?? 0) == 0
                    || finding.TargetIds.Any(string.IsNullOrWhiteSpace))
                    return $"reviewHandoff finding[{id}] 的 targetIds 必须至少一项且不能含空值。";
                if ((finding.EvidenceFactRefs?.Count ?? 0) == 0
                    || finding.EvidenceFactRefs.Any(string.IsNullOrWhiteSpace))
                    return $"reviewHandoff finding[{id}] 的 evidenceFactRefs 必须至少一项且不能含空值。";
                string unknownFact = finding.EvidenceFactRefs.FirstOrDefault(reference =>
                    !verifiedFactRefs.Contains(reference));
                if (unknownFact != null)
                    return $"reviewHandoff finding[{id}] 引用了不存在的宿主机械事实：{unknownFact}。"
                        + BuildAvailableFactRefHint(handoff.VerifiedFacts, unknownFact);
                var referencedSubjects = new HashSet<string>(finding.EvidenceFactRefs.Select(reference =>
                    reference.Substring(0, reference.LastIndexOf("::", StringComparison.Ordinal))),
                    StringComparer.Ordinal);
                if (!finding.TargetIds.Any(target => referencedSubjects.Contains(target)))
                    return $"reviewHandoff finding[{id}] 的目标没有与宿主机械事实主体建立联系。";
            }
            List<ReviewVerifiedFactDefinition> verifiedFacts = handoff.VerifiedFacts
                ?? new List<ReviewVerifiedFactDefinition>();
            if (verifiedFacts.Count > 100)
                return "reviewHandoff.verifiedFacts 超过宿主允许的100项上限。";
            var factKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ReviewVerifiedFactDefinition fact in verifiedFacts)
            {
                string identity = (fact?.SubjectId ?? string.Empty).Trim()
                    + "\n" + (fact?.Key ?? string.Empty).Trim();
                if (fact == null
                    || string.IsNullOrWhiteSpace(fact.SubjectId)
                    || string.IsNullOrWhiteSpace(fact.SubjectName)
                    || string.IsNullOrWhiteSpace(fact.Key)
                    || string.IsNullOrWhiteSpace(fact.Value)
                    || string.IsNullOrWhiteSpace(fact.SourceTool)
                    || string.IsNullOrWhiteSpace(fact.ToolCallId)
                    || string.IsNullOrWhiteSpace(fact.EvidencePath)
                    || string.IsNullOrWhiteSpace(fact.EvidenceSha256)
                    || !factKeys.Add(identity))
                {
                    return "reviewHandoff.verifiedFacts 包含无效或重复的宿主证据。";
                }
            }
            if (status != ReviewHandoffStatuses.Unresolved && verifiedFacts.Count == 0)
                return "ProcessReview 形成确定结论前必须取得至少一项宿主机械事实；证据不足时使用unresolved。";
            return null;
        }

        /// <summary>
        /// 引用不存在的事实键时，在错误消息中附上当前可用键候选（优先同一主体），
        /// 让模型一轮修正而不是再调工具盲试。
        /// </summary>
        private static string BuildAvailableFactRefHint(
            List<ReviewVerifiedFactDefinition> verifiedFacts,
            string unknownReference)
        {
            if (verifiedFacts == null || verifiedFacts.Count == 0) return string.Empty;
            string subjectId = unknownReference?.Contains("::") == true
                ? unknownReference.Substring(0, unknownReference.IndexOf("::", StringComparison.Ordinal))
                : string.Empty;
            List<string> candidates = verifiedFacts
                .Select(fact => ReviewFactReference.Build(fact?.SubjectId, fact?.Key))
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(reference =>
                    subjectId.Length > 0 && reference.StartsWith(subjectId + "::", StringComparison.Ordinal))
                .ThenBy(reference => reference, StringComparer.Ordinal)
                .Take(20)
                .ToList();
            if (candidates.Count == 0) return string.Empty;
            return " 当前可用的事实键（已按该主体优先，最多20条）："
                + string.Join("；", candidates)
                + "。只能引用以上键。";
        }

        private static string ValidateProcessEditBasis(
            TaskCapabilityDecisionDefinition decision,
            string capability,
            AiDynamicTaskState state)
        {
            if (!string.Equals(capability, AutomationToolProfiles.ProcessEdit, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(decision.Basis) || (decision.FindingIds?.Count ?? 0) > 0)
                    return "basis/findingIds 只用于申请 ProcessEdit。";
                return null;
            }
            string basis = (decision.Basis ?? string.Empty).Trim();
            if (basis == TaskDecisionBases.DirectUserChange)
                return (decision.FindingIds?.Count ?? 0) == 0
                    ? null
                    : "direct_user_change 不得携带 findingIds。";
            if (basis != TaskDecisionBases.ProvenReviewFinding)
                return "申请 ProcessEdit 必须声明 basis=direct_user_change 或 proven_review_finding。";
            ReviewHandoffDefinition handoff = state?.LastReviewHandoff;
            if (!string.Equals(handoff?.Status, ReviewHandoffStatuses.ProvenDefect, StringComparison.Ordinal))
                return "没有 proven_defect 的可信评审交接，不能按评审结论进入 ProcessEdit。";
            string[] requested = (decision.FindingIds ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (requested.Length == 0) return "proven_review_finding 必须提供 findingIds。";
            var available = new HashSet<string>(
                (handoff.Findings ?? new List<ReviewFindingDefinition>()).Select(item => item.Id),
                StringComparer.Ordinal);
            string unknown = requested.FirstOrDefault(id => !available.Contains(id));
            if (unknown != null)
            {
                List<string> availableIds = (handoff.Findings ?? new List<ReviewFindingDefinition>())
                    .Select(item => item.Id)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
                return $"findingIds 引用了不存在的可信 finding：{unknown}。"
                    + (availableIds.Count == 0
                        ? "当前评审交接没有任何可信 finding。"
                        : "可引用的 finding id：" + string.Join("；", availableIds) + "。");
            }
            ReviewFindingDefinition unresolved = (handoff.Findings ?? new List<ReviewFindingDefinition>())
                .FirstOrDefault(item => requested.Contains(item.Id, StringComparer.Ordinal)
                    && !string.Equals(
                        item.Repairability,
                        ReviewFindingRepairability.SafeWithoutExternalFacts,
                        StringComparison.Ordinal));
            return unresolved == null
                ? null
                : $"finding[{unresolved.Id}] 的最小修复仍需要用户选择或外部事实，不能直接进入 ProcessEdit。";
        }

        private static bool HasSavedMutationEvidence(string profile, AiTurnEvidence evidence)
        {
            if (evidence == null || evidence.UnsafeMutationFailure) return false;
            if (string.Equals(profile, AutomationToolProfiles.ProcessCreate, StringComparison.Ordinal)
                || string.Equals(profile, AutomationToolProfiles.ProcessEdit, StringComparison.Ordinal))
                return evidence.ChangeSetCommitted;
            if (string.Equals(profile, AutomationToolProfiles.PlatformConfiguration, StringComparison.Ordinal))
                return evidence.MigrationCommitted;
            return evidence.ConfigurationSaved;
        }

        private static string ValidateEvidenceTransition(AiDynamicTaskState state, string nextCapability)
        {
            if (state.PreviousStage != null
                && string.Equals(state.PreviousStage.Profile, AutomationToolProfiles.ProcessReview, StringComparison.Ordinal)
                && IsSideEffectCapability(nextCapability)
                && !(state.PreviousEvidence?.CurrentStateReadSucceeded ?? false))
            {
                return "只读审查没有取得当前状态读取证据，不能进入副作用能力。";
            }
            if (!string.Equals(nextCapability, AutomationToolProfiles.RuntimeControl, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(state.LastMutationProfile))
                return null;

            AiTurnEvidence evidence = state.LastMutationEvidence ?? AiTurnEvidence.Empty;
            if (evidence.UnsafeMutationFailure)
                return "最近配置阶段存在可能产生副作用的失败，禁止进入运行控制。";
            if (string.Equals(state.LastMutationProfile, AutomationToolProfiles.ProcessCreate, StringComparison.Ordinal)
                || string.Equals(state.LastMutationProfile, AutomationToolProfiles.ProcessEdit, StringComparison.Ordinal))
                return evidence.ChangeSetCommitted ? null : "最近流程写入没有已保存的 ChangeSet 提交证据，禁止运行。";
            if (string.Equals(state.LastMutationProfile, AutomationToolProfiles.PlatformConfiguration, StringComparison.Ordinal))
                return evidence.MigrationCommitted ? null : "最近平台配置没有已保存的迁移提交证据，禁止运行。";
            return evidence.ConfigurationSaved ? null : "最近资源修改没有配置已保存证据，禁止运行。";
        }

    }

    internal sealed class AiDynamicTaskState
    {
        public int CompletedStageCount { get; set; }
        public AiTaskCapabilityStage PreviousStage { get; set; }
        public AiTurnEvidence PreviousEvidence { get; set; }
        public string LastMutationProfile { get; set; }
        public AiTurnEvidence LastMutationEvidence { get; set; }
        public bool UnsafePartialMutation { get; set; }
        public bool UncommittedMutation { get; set; }
        public bool PreviewRejected { get; set; }
        public bool SourceFilesChanged { get; set; }
        public bool SourceMutationUncertain { get; set; }
        public ReviewHandoffDefinition LastReviewHandoff { get; set; }
    }

    internal enum AiTaskDecisionKind
    {
        Invalid,
        RunStage
    }

    internal sealed class AiTaskDecisionValidation
    {
        public AiTaskDecisionKind Kind { get; private set; }
        public AiTaskCapabilityStage Stage { get; private set; }
        public string Message { get; private set; }

        public static AiTaskDecisionValidation Invalid(string message) =>
            new AiTaskDecisionValidation { Kind = AiTaskDecisionKind.Invalid, Message = message };
        public static AiTaskDecisionValidation Run(AiTaskCapabilityStage stage) =>
            new AiTaskDecisionValidation { Kind = AiTaskDecisionKind.RunStage, Stage = stage };
    }

    internal sealed class AiTaskCapabilityStage
    {
        public int Index { get; set; }
        public string Profile { get; set; }
        public string Objective { get; set; }
    }

    /// <summary>
    /// 轨迹观测只生成诊断信号，不中断模型。真正的越权由能力包、确认状态机和代码闸门机械阻断。
    /// </summary>
    internal static class AiTrajectoryObservationPolicy
    {
        public static AiTrajectoryObservation Evaluate(
            string profile,
            int toolCalls,
            int toolFailures,
            long toolResultBytes,
            long modelSegmentBytes,
            long inputTokens,
            int contextWindowTokens,
            long unattributedMs,
            IReadOnlyCollection<string> toolNames)
        {
            int callLimit;
            long byteLimit;
            long modelSegmentByteLimit;
            long unattributedLimit;
            switch (profile)
            {
                case AutomationToolProfiles.TaskCoordinator:
                    callLimit = 1;
                    byteLimit = 16 * 1024;
                    modelSegmentByteLimit = 12 * 1024;
                    unattributedLimit = 15000;
                    break;
                case AutomationToolProfiles.ProcessDesign:
                    callLimit = 3;
                    byteLimit = 64 * 1024;
                    modelSegmentByteLimit = 32 * 1024;
                    unattributedLimit = 30000;
                    break;
                case AutomationToolProfiles.ProcessReview:
                    callLimit = 12;
                    byteLimit = 128 * 1024;
                    modelSegmentByteLimit = 32 * 1024;
                    unattributedLimit = 30000;
                    break;
                case AutomationToolProfiles.ProcessCreate:
                case AutomationToolProfiles.ProcessEdit:
                case AutomationToolProfiles.ResourceEdit:
                case AutomationToolProfiles.PlatformConfiguration:
                    callLimit = 14;
                    byteLimit = 256 * 1024;
                    modelSegmentByteLimit = 48 * 1024;
                    unattributedLimit = 45000;
                    break;
                case AutomationToolProfiles.RuntimeControl:
                    callLimit = 15;
                    byteLimit = 256 * 1024;
                    modelSegmentByteLimit = 48 * 1024;
                    unattributedLimit = 30000;
                    break;
                case AutomationToolProfiles.SourceReview:
                case AutomationToolProfiles.SourceDevelopment:
                    callLimit = 30;
                    byteLimit = 1024 * 1024;
                    modelSegmentByteLimit = 128 * 1024;
                    unattributedLimit = 60000;
                    break;
                default:
                    callLimit = 30;
                    byteLimit = 1024 * 1024;
                    modelSegmentByteLimit = 128 * 1024;
                    unattributedLimit = 60000;
                    break;
            }
            var reasons = new List<string>();
            var recoveryReasons = new List<string>();
            if (toolCalls > callLimit) reasons.Add($"tool_calls>{callLimit}");
            if (toolResultBytes > byteLimit) reasons.Add($"tool_result_bytes>{byteLimit}");
            if (modelSegmentBytes > modelSegmentByteLimit)
                reasons.Add($"model_segment_bytes>{modelSegmentByteLimit}");
            if (toolFailures > 2) reasons.Add("tool_failures>2");
            else if (toolFailures > 0) recoveryReasons.Add($"tool_failures_recovered={toolFailures}");
            long contextPressureLimit = Math.Max(
                16384L,
                Math.Max(32768, contextWindowTokens) * 45L / 100L);
            if (inputTokens >= contextPressureLimit)
                reasons.Add($"input_context_pressure>={contextPressureLimit}");
            if (unattributedMs > unattributedLimit)
                reasons.Add($"unattributed_ms>{unattributedLimit}");
            var names = new HashSet<string>(toolNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (string.Equals(profile, AutomationToolProfiles.ProcessReview, StringComparison.Ordinal))
            {
                if (names.Contains("diagnose_proc") || names.Contains("diagnose_issue")
                    || names.Contains("get_snapshot") || names.Contains("get_info_log_tail"))
                    reasons.Add("static_review_used_runtime_diagnostics");
                if (names.Contains("get_proc_detail") && names.Contains("get_proc_overview"))
                    reasons.Add("duplicate_proc_evidence_reads");
            }
            return new AiTrajectoryObservation
            {
                Status = reasons.Count > 0 ? "review" : recoveryReasons.Count > 0 ? "recovered" : "pass",
                ToolCallLimit = callLimit,
                ToolResultByteLimit = byteLimit,
                ModelSegmentByteLimit = modelSegmentByteLimit,
                ContextPressureTokenLimit = contextPressureLimit,
                UnattributedMsLimit = unattributedLimit,
                Reasons = reasons.Concat(recoveryReasons).ToArray()
            };
        }
    }

    internal sealed class AiTrajectoryObservation
    {
        public string Status { get; set; }
        public int ToolCallLimit { get; set; }
        public long ToolResultByteLimit { get; set; }
        public long ModelSegmentByteLimit { get; set; }
        public long ContextPressureTokenLimit { get; set; }
        public long UnattributedMsLimit { get; set; }
        public IReadOnlyList<string> Reasons { get; set; }
    }
}
