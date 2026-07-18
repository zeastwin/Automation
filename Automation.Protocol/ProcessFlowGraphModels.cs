using System;
using System.Collections.Generic;

namespace Automation.Protocol
{
    public enum FlowGraphScope
    {
        Project,
        Process
    }

    public sealed class ProcessFlowGraphSnapshot
    {
        public int GraphVersion { get; set; } = 1;
        public string Scope { get; set; }
        public int? ProcIndex { get; set; }
        public string ProcId { get; set; }
        public string Name { get; set; }
        public long PublishedRevision { get; set; }
        public long AppliedRevision { get; set; }
        public bool RuntimeRevisionMatches { get; set; } = true;
        public List<FlowGraphGroup> Groups { get; set; } = new List<FlowGraphGroup>();
        public List<FlowGraphNode> Nodes { get; set; } = new List<FlowGraphNode>();
        public List<FlowGraphEdge> Edges { get; set; } = new List<FlowGraphEdge>();
        public List<FlowGraphDiagnostic> Diagnostics { get; set; } = new List<FlowGraphDiagnostic>();
        public int DisabledNodeCount { get; set; }
        public int DisabledEdgeCount { get; set; }
        public int AlarmEdgeCount { get; set; }
    }

    public sealed class FlowGraphGroup
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public int ProcIndex { get; set; }
        public string ProcId { get; set; }
        public int StepIndex { get; set; }
        public string StepId { get; set; }
        public bool Disabled { get; set; }
    }

    public sealed class FlowGraphNode
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Label { get; set; }
        public string Summary { get; set; }
        public string GroupId { get; set; }
        public int? ProcIndex { get; set; }
        public string ProcId { get; set; }
        public int? StepIndex { get; set; }
        public string StepId { get; set; }
        public int? OpIndex { get; set; }
        public string OpId { get; set; }
        public string OperaType { get; set; }
        public string RuntimeState { get; set; }
        public string ReadinessStatus { get; set; }
        public bool AutoStart { get; set; }
        public bool Disabled { get; set; }
        public bool Reachable { get; set; }
        public bool Invalid { get; set; }
        public bool Dynamic { get; set; }
    }

    public sealed class FlowGraphEdge
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string SourceId { get; set; }
        public string TargetId { get; set; }
        public string ConfiguredTargetId { get; set; }
        public string Label { get; set; }
        public string SourceField { get; set; }
        public bool AlarmPath { get; set; }
        public bool Disabled { get; set; }
        public bool Invalid { get; set; }
        public bool Dynamic { get; set; }
        public bool Loop { get; set; }
    }

    public sealed class FlowGraphDiagnostic
    {
        public string Code { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
        public string NodeId { get; set; }
        public string EdgeId { get; set; }
    }
}
