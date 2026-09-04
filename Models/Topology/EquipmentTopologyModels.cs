using System;
using System.Collections.Generic;

// 模块：设备拓扑孪生 / 配置模型。
// 职责范围：描述具体设备对象、状态绑定、关系和证据，不承载设备动作或安全联锁执行。

namespace Automation
{
    /// <summary>
    /// 设备拓扑孪生的当前配置契约。该配置用于理解、呈现和诊断，不能替代控制器中的硬安全逻辑。
    /// </summary>
    public sealed class EquipmentTopologyDefinition
    {
        public const int CurrentSchemaVersion = 1;

        public EquipmentTopologyDefinition()
        {
            SchemaVersion = CurrentSchemaVersion;
            Name = "设备拓扑孪生";
            Nodes = new List<EquipmentTopologyNode>();
            Relations = new List<EquipmentTopologyRelation>();
        }

        public int SchemaVersion { get; set; }
        public long Revision { get; set; }
        public string Name { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<EquipmentTopologyNode> Nodes { get; set; }
        public List<EquipmentTopologyRelation> Relations { get; set; }
    }

    /// <summary>
    /// 现场中的一个具体对象，例如某个工位、气缸、吸嘴、夹爪、传感器或工件。
    /// Kind 只影响展示和关系约束，不是可复用的零部件类型库。
    /// </summary>
    public sealed class EquipmentTopologyNode
    {
        public EquipmentTopologyNode()
        {
            ReviewState = "confirmed";
            Confidence = 1d;
            Evidence = new List<EquipmentTopologyEvidence>();
            StateBindings = new List<EquipmentTopologyStateBinding>();
        }

        public string Id { get; set; }
        public string Label { get; set; }
        public string Kind { get; set; }
        public string Zone { get; set; }
        public string Description { get; set; }
        public string ResourceKind { get; set; }
        public string ResourceRef { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool LayoutPinned { get; set; }
        public string ReviewState { get; set; }
        public double Confidence { get; set; }
        public List<EquipmentTopologyEvidence> Evidence { get; set; }
        public List<EquipmentTopologyStateBinding> StateBindings { get; set; }
    }

    /// <summary>
    /// 把一个真实信号值映射为当前具体节点的业务状态。
    /// 例如：真空阀输出=true 对当前吸嘴表示“吸取中”。
    /// </summary>
    public sealed class EquipmentTopologyStateBinding
    {
        public EquipmentTopologyStateBinding()
        {
            ReviewState = "confirmed";
            Confidence = 1d;
            Evidence = new List<EquipmentTopologyEvidence>();
        }

        public string Id { get; set; }
        public string StateName { get; set; }
        public string SourceKind { get; set; }
        public string ResourceRef { get; set; }
        public string Operator { get; set; }
        public string ExpectedValue { get; set; }
        public string Meaning { get; set; }
        public int Priority { get; set; }
        public string ReviewState { get; set; }
        public double Confidence { get; set; }
        public List<EquipmentTopologyEvidence> Evidence { get; set; }
    }

    /// <summary>
    /// 两个具体节点间的一条语义关系。候选关系只有经人工确认后才成为已确认事实。
    /// </summary>
    public sealed class EquipmentTopologyRelation
    {
        public EquipmentTopologyRelation()
        {
            Evidence = new List<EquipmentTopologyEvidence>();
        }

        public string Id { get; set; }
        public string SourceNodeId { get; set; }
        public string TargetNodeId { get; set; }
        public string Layer { get; set; }
        public string Kind { get; set; }
        public string Label { get; set; }
        public string Condition { get; set; }
        public string Description { get; set; }
        public string ReviewState { get; set; }
        public double Confidence { get; set; }
        public string ConflictsWithId { get; set; }
        public List<EquipmentTopologyEvidence> Evidence { get; set; }
    }

    /// <summary>
    /// 关系推断的原始依据。OperationType 与 ParameterPath 为后续基于指令类型和参数的强推断预留。
    /// </summary>
    public sealed class EquipmentTopologyEvidence
    {
        public string SourceType { get; set; }
        public string SourceRef { get; set; }
        public string OperationType { get; set; }
        public string ParameterPath { get; set; }
        public string Detail { get; set; }
    }
}
