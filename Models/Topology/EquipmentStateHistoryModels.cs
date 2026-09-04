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
        public string OperationId { get; set; }
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
    /// </summary>
    public sealed class EquipmentStateHistoryWindow
    {
        public EquipmentStateHistoryWindow()
        {
            Baseline = new EquipmentStateSnapshot();
            Events = new List<EquipmentStateHistoryEvent>();
        }

        public long EarliestAvailableSequence { get; set; }
        public long LatestSequence { get; set; }
        public bool Truncated { get; set; }
        public EquipmentStateSnapshot Baseline { get; set; }
        public List<EquipmentStateHistoryEvent> Events { get; set; }
    }
}
