using System.Collections.Generic;
using System.ComponentModel;

namespace Automation.Protocol
{
    public static class ReviewHandoffStatuses
    {
        public const string ProvenDefect = "proven_defect";
        public const string ConfigurationGap = "configuration_gap";
        public const string Unresolved = "unresolved";
        public const string NoDefect = "no_defect";
    }

    public static class TaskDecisionBases
    {
        public const string DirectUserChange = "direct_user_change";
        public const string ProvenReviewFinding = "proven_review_finding";
    }

    public static class ReviewFindingCategories
    {
        public const string StructuralDefect = "structural_defect";
        public const string RuntimeDefect = "runtime_defect";
        public const string SafetyDefect = "safety_defect";
        public const string SupportedCategories =
            StructuralDefect + "、" + RuntimeDefect + "、" + SafetyDefect;
    }

    public static class ReviewFindingRepairability
    {
        public const string SafeWithoutExternalFacts = "safe_without_external_facts";
        public const string RequiresUserChoice = "requires_user_choice";
        public const string SupportedValues =
            SafeWithoutExternalFacts + "、" + RequiresUserChoice;
    }

    public static class ReviewFactReference
    {
        public static string Build(string subjectId, string key) =>
            (subjectId ?? string.Empty).Trim() + "::" + (key ?? string.Empty).Trim();
    }

    public sealed class ReviewFindingDefinition
    {
        [Description("本次评审内稳定且唯一的短标识，后续修改必须逐项引用。")]
        public string Id { get; set; }

        [Description("经过事实证据证明的问题，不得把待确认设计、命名猜测或通用建议写成缺陷。")]
        public string Summary { get; set; }

        [Description("已证明缺陷类别：" + ReviewFindingCategories.SupportedCategories + "。外部配置缺口、设计假设和运行未知不得放入findings。")]
        public string Category { get; set; }

        [Description("最小修复是否还需要外部事实：" + ReviewFindingRepairability.SupportedValues + "。")]
        public string Repairability { get; set; }

        [Description("问题涉及的稳定 procId、stepId、opId 或资源精确名称。")]
        public List<string> TargetIds { get; set; }

        [Description("直接支持结论的当前配置、引用、Readiness 或运行证据。")]
        public string Evidence { get; set; }

        [Description("至少一项宿主机械事实引用，格式为subjectId::key。稳定key包括proc.isValid/proc.runnable/proc.runBlockerCount、operation.reachable/operation.invalid/operation.graphDiagnostic.<code>/operation.incomingGotoCount、variable.usageCount等。只能引用本阶段成功工具结果中由宿主附加的verifiedFacts。")]
        public List<string> EvidenceFactRefs { get; set; }

        [Description("只修复该 finding 所需的最小改动边界。")]
        public string MinimalChange { get; set; }
    }

    public sealed class ReviewVerifiedFactDefinition
    {
        [Description("事实所属对象的稳定标识，例如procId；没有稳定标识时使用明确的对象键。")]
        public string SubjectId { get; set; }

        [Description("事实所属对象的当前显示名称。")]
        public string SubjectName { get; set; }

        [Description("稳定事实键，例如proc.warningCount或proc.runnable。")]
        public string Key { get; set; }

        [Description("从成功工具结果机械提取的规范化值。")]
        public string Value { get; set; }

        [Description("产生事实的Automation工具名。")]
        public string SourceTool { get; set; }

        [Description("本次调用内的工具调用标识。")]
        public string ToolCallId { get; set; }

        [Description("值在原始成功结果中的JSON Pointer。")]
        public string EvidencePath { get; set; }

        [Description("原始成功工具结果的SHA-256，用于核对证据快照。")]
        public string EvidenceSha256 { get; set; }
    }

    public sealed class ReviewHandoffDefinition
    {
        [Description("评审结论：proven_defect、configuration_gap、unresolved、no_defect。只有 proven_defect 能授权按评审 finding 进入修改。")]
        public string Status { get; set; }

        [Description("简洁说明已检查范围、结论和仍缺少的证据。")]
        public string Summary { get; set; }

        [Description("status=proven_defect 时至少一项；其他状态不得提供假定缺陷。")]
        public List<ReviewFindingDefinition> Findings { get; set; }

        [Description("由Automation宿主从本阶段成功工具结果机械附加；模型输入Schema不开放此字段。")]
        public List<ReviewVerifiedFactDefinition> VerifiedFacts { get; set; }
    }

    /// <summary>
    /// 无副作用协调模型每轮提交的单步决定。它只表达下一步意图，不授予权限。
    /// </summary>
    public sealed class TaskCapabilityDecisionDefinition
    {
        public int Version { get; set; } = 1;

        [Description("单步决定：run_stage、finish或ask_user。当前交付已经完成、用户允许占位或仅存在后续可选完善项时使用finish；只有缺少信息导致当前交付无法安全继续时使用ask_user。")]
        public string Action { get; set; }

        [Description("action=run_stage时必填的单个能力包。用户目标是创建流程时直接选择ProcessCreate，它可读取设计知识、发现资源并创建完整简单流程或安全骨架；复杂骨架提交后用ProcessEdit按稳定ID分阶段补齐。ProcessDesign只用于纯方案输出或写入前必须先让用户做设计取舍的阶段。")]
        public string Capability { get; set; }

        [Description("action=run_stage时必填的当前阶段目标，不提交后续完整计划。")]
        public string Objective { get; set; }

        [Description("action=finish或ask_user时必填；这是唯一持久化给用户的本轮模型输出。允许的占位、incomplete或可选后续工作不妨碍finish，应在finish中列明；只有当前任务无法继续才ask_user。ProcessReview的精确校验数量由宿主从工具证据另行渲染，message解释影响即可，不重复手算。")]
        public string Message { get; set; }

        [Description("只有运行控制、平台配置或源码修改能力需要；必须逐字来自当前用户消息，历史消息不能授权。")]
        public string AuthorizationQuote { get; set; }

        [Description("当前阶段完成后是否必须暂停并等待用户再次发言。")]
        public bool RequiresUserConfirmationAfter { get; set; }

        [Description("申请 ProcessEdit 时必填：direct_user_change 表示当前用户明确指定修改，也用于继续补齐本次已提交的流程骨架；proven_review_finding 表示只落实已证明的评审 finding。")]
        public string Basis { get; set; }

        [Description("basis=proven_review_finding 时必填，必须逐项引用最近可信 reviewHandoff 中的 finding id。")]
        public List<string> FindingIds { get; set; }

        [Description("ProcessReview 阶段结束时必填的结构化可信交接；普通协调决定不得伪造或改写。")]
        public ReviewHandoffDefinition ReviewHandoff { get; set; }
    }
}
