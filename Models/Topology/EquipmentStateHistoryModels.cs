using System;
using System.Collections.Generic;

// 模块：设备拓扑孪生 / 状态历史契约。
// 职责范围：描述按时间排序的语义状态事实及节点投影，不承载设备控制或安全联锁。

namespace Automation
{
    public static class EquipmentStateEventTypes
    {
        public const string SignalChanged = "signal.changed";
        public const string SignalQualityChanged = "signal.quality.changed";
        public const string NodeStateChanged = "node.state.changed";
        public const string TopologyChanged = "topology.changed";
        public const string ProcessStarted = "process.started";
        public const string ProcessPositionChanged = "process.position.changed";
        public const string ProcessOperationFailed = "process.operation.failed";
        public const string ProcessCompleted = "process.completed";
        public const string MachineActionStarted = "machine.action.started";
        public const string MachineActionCompleted = "machine.action.completed";
        public const string MachineActionFailed = "machine.action.failed";
        public const string MachineActionOutcomeObserved = "machine.action.outcome.observed";
    }

    public static class EquipmentStateAspects
    {
        public const string Commanded = "commanded_state";
        public const string Observed = "observed_state";
        public const string Estimated = "estimated_state";
        public const string Topology = "topology";
    }

    public static class EquipmentStateQualities
    {
        public const string Good = "good";
        public const string Unknown = "unknown";
        public const string Stale = "stale";
        public const string Retired = "retired";
    }

    /// <summary>
    /// 状态历史的权威事实。Sequence 是单调递增的全局顺序；节点视图只是由这些事实计算出的投影。
    /// </summary>
    public sealed class EquipmentStateHistoryEvent
    {
        public long Sequence { get; set; }
        public DateTime ObservedAtUtc { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
        public long TopologyRevision { get; set; }
        public string EventType { get; set; }
        public string NodeId { get; set; }
        public string NodeLabel { get; set; }
        public string Aspect { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Meaning { get; set; }
        public string Quality { get; set; }
        public double Confidence { get; set; }
        public string SourceKind { get; set; }
        public string ResourceRef { get; set; }
        public string BindingId { get; set; }
        public string RunId { get; set; }
        public string ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string StepId { get; set; }
        public int? StepIndex { get; set; }
        public string OperationId { get; set; }
        public int? OperationIndex { get; set; }
        public string OperationType { get; set; }
        public string OperationName { get; set; }
        public string ProcessState { get; set; }
        public string Outcome { get; set; }
        public string TerminationReason { get; set; }
        public string PreviewId { get; set; }
        public string SkillId { get; set; }
        public string ActionId { get; set; }
        public string ExpectedOutcome { get; set; }
        public long? CausedBySequence { get; set; }
    }

    /// <summary>某一时间点的节点语义状态投影。</summary>
    public sealed class EquipmentNodeStateProjection
    {
        public string NodeId { get; set; }
        public string NodeLabel { get; set; }
        public string StateName { get; set; }
        public string Meaning { get; set; }
        public string Quality { get; set; }
        public double Confidence { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public long Sequence { get; set; }
        public long TopologyRevision { get; set; }
        public string SourceKind { get; set; }
        public string ResourceRef { get; set; }
        public string BindingId { get; set; }
    }

    /// <summary>
    /// 感知器当前持有的节点运行态。StateChangedAtUtc 表示语义状态最后变化时间，
    /// LastSuccessfulObservationAtUtc 表示最近一次成功读取现场信号的时间，两者不可混用。
    /// </summary>
    public sealed class EquipmentNodePerceptionState
    {
        public string NodeId { get; set; }
        public string NodeLabel { get; set; }
        public string StateName { get; set; }
        public string Meaning { get; set; }
        public string Quality { get; set; }
        public double Confidence { get; set; }
        public DateTime StateChangedAtUtc { get; set; }
        public DateTime LastSuccessfulObservationAtUtc { get; set; }
        public long Sequence { get; set; }
        public long TopologyRevision { get; set; }
        public string SourceKind { get; set; }
        public string ResourceRef { get; set; }
        public string BindingId { get; set; }
    }

    /// <summary>
    /// 感知器的线程安全运行时快照。它用于判断现场观测是否新鲜，不替代事件时间线。
    /// </summary>
    public sealed class EquipmentPerceptionSnapshot
    {
        public EquipmentPerceptionSnapshot()
        {
            NodeStates = new List<EquipmentNodePerceptionState>();
        }

        public DateTime CapturedAtUtc { get; set; }
        public long TopologyRevision { get; set; }
        public bool IsRunning { get; set; }
        public string LastObservationError { get; set; }
        public List<EquipmentNodePerceptionState> NodeStates { get; set; }
    }

    /// <summary>可作为回放起点的完整节点状态快照。</summary>
    public sealed class EquipmentStateSnapshot
    {
        public EquipmentStateSnapshot()
        {
            NodeStates = new List<EquipmentNodeStateProjection>();
        }

        public long Sequence { get; set; }
        public DateTime TimeUtc { get; set; }
        public long TopologyRevision { get; set; }
        public List<EquipmentNodeStateProjection> NodeStates { get; set; }
    }

    /// <summary>
    /// 时间线查询结果。Baseline 是 Events 第一条事实之前的状态，保证页面和 AI 可确定性回放。
    /// Truncated 只表示符合本次查询的可用事实多于 Events；保留期之前的缺口由
    /// EarliestAvailableSequence 与调用方游标共同判断。
    /// </summary>
    public sealed class EquipmentStateHistoryWindow
    {
        public EquipmentStateHistoryWindow()
        {
            Baseline = new EquipmentStateSnapshot();
            Events = new List<EquipmentStateHistoryEvent>();
            SequenceGaps = new List<EquipmentStateSequenceGap>();
        }

        public long EarliestAvailableSequence { get; set; }
        public long LatestSequence { get; set; }
        public bool Truncated { get; set; }
        /// <summary>Baseline 是否由连续、无缺口的历史事实构成。</summary>
        public bool BaselineComplete { get; set; } = true;
        /// <summary>从 Baseline 到本页最后一条事实之间已知缺失的 sequence 范围。</summary>
        public List<EquipmentStateSequenceGap> SequenceGaps { get; set; }
        public bool SequenceGapsTruncated { get; set; }
        public EquipmentStateSnapshot Baseline { get; set; }
        public List<EquipmentStateHistoryEvent> Events { get; set; }
    }

    /// <summary>持久化恢复后可机械确认缺失的连续 sequence 范围。</summary>
    public sealed class EquipmentStateSequenceGap
    {
        public long FirstMissingSequence { get; set; }
        public long LastMissingSequence { get; set; }
    }
}
