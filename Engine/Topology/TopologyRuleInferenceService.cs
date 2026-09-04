using Automation.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// 模块：引擎 / 设备拓扑推断。
// 职责范围：仅依据指令实际类型、参数和确定性控制流生成可追溯候选，不解释名称、不保存配置。

namespace Automation
{
    internal sealed class TopologyInferenceFact
    {
        public string FactId { get; set; }
        public string RuleId { get; set; }
        public string ProcId { get; set; }
        public string StepId { get; set; }
        public string OpId { get; set; }
        public string OperationType { get; set; }
        public string ParameterPath { get; set; }
        public string ParameterValue { get; set; }
        public string ControlFlowRole { get; set; }
        public string SubjectKind { get; set; }
        public string SubjectRef { get; set; }
        public string Predicate { get; set; }
        public string ObjectKind { get; set; }
        public string ObjectRef { get; set; }
        public double Confidence { get; set; }
        public bool EligibleForTopology { get; set; }
    }

    internal sealed class TopologyRuleInferenceResult
    {
        public EquipmentTopologyDefinition CandidateDefinition { get; set; }
        public List<TopologyInferenceFact> Facts { get; } = new List<TopologyInferenceFact>();
        public List<string> NewNodeIds { get; } = new List<string>();
        public List<string> NewRelationIds { get; } = new List<string>();
        public List<string> NewBindingIds { get; } = new List<string>();
        public int ScannedProcessCount { get; set; }
        public int ScannedOperationCount { get; set; }
        public int DisabledOperationCount { get; set; }
        public int UnreachableOperationCount { get; set; }
        public int AuxiliaryOperationCount { get; set; }

        public string BuildSummary()
        {
            return $"扫描 {ScannedProcessCount} 个流程、{ScannedOperationCount} 条指令；"
                + $"新增 {NewNodeIds.Count} 个节点、{NewBindingIds.Count} 条状态绑定、{NewRelationIds.Count} 条关系候选。"
                + $"已排除禁用 {DisabledOperationCount} 条、不可达 {UnreachableOperationCount} 条、辅助指令 {AuxiliaryOperationCount} 条。";
        }
    }

    /// <summary>
    /// 流程到拓扑的确定性规则投影。结果始终停留在候选层，由页面人工确认后才能保存。
    /// </summary>
    internal static class TopologyRuleInferenceService
    {
        private const int NearbyFeedbackDistance = 4;

        public static TopologyRuleInferenceResult Generate(
            EquipmentTopologyDefinition current,
            IReadOnlyList<Proc> processes)
        {
            var result = new TopologyRuleInferenceResult
            {
                CandidateDefinition = ObjectGraphCloner.Clone(current)
                    ?? new EquipmentTopologyDefinition()
            };
            NormalizeCollections(result.CandidateDefinition);
            processes = processes ?? Array.Empty<Proc>();
            result.ScannedProcessCount = processes.Count;

            for (int procIndex = 0; procIndex < processes.Count; procIndex++)
            {
                Proc proc = processes[procIndex];
                if (proc == null)
                {
                    continue;
                }
                ProcessFlowGraphSnapshot graph = ProcessFlowGraphService.BuildProcess(processes, procIndex);
                Dictionary<string, FlowGraphNode> graphNodes = graph.Nodes
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.OpId))
                    .GroupBy(item => item.OpId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

                for (int stepIndex = 0; stepIndex < (proc.steps?.Count ?? 0); stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    var recentOutputs = new List<ResourceUse>();
                    for (int opIndex = 0; opIndex < (step?.Ops?.Count ?? 0); opIndex++)
                    {
                        OperationType operation = step.Ops[opIndex];
                        if (operation == null)
                        {
                            continue;
                        }
                        result.ScannedOperationCount++;
                        string procId = StableId(proc.head?.Id, $"proc-index-{procIndex}");
                        string stepId = StableId(step.Id, $"step-index-{procIndex}-{stepIndex}");
                        string opId = StableId(operation.Id, $"op-index-{procIndex}-{stepIndex}-{opIndex}");
                        graphNodes.TryGetValue(opId, out FlowGraphNode graphNode);
                        string role = ClassifyRole(operation, graphNode, graph, opId);
                        bool eligible = IsEligible(role);
                        if (string.Equals(role, "disabled", StringComparison.Ordinal)) result.DisabledOperationCount++;
                        if (string.Equals(role, "unreachable", StringComparison.Ordinal)) result.UnreachableOperationCount++;
                        if (string.Equals(role, "auxiliary", StringComparison.Ordinal)) result.AuxiliaryOperationCount++;

                        var context = new RuleContext(result, procId, stepId, opId, operation, role, eligible, opIndex);
                        List<ResourceUse> outputs = ApplyOperationRules(context);
                        if (outputs.Count > 0)
                        {
                            recentOutputs = outputs;
                        }
                        else if (operation is IoCheck && eligible)
                        {
                            AddNearbyFeedbackRelations(context, recentOutputs);
                        }
                        if (recentOutputs.Count > 0
                            && opIndex - recentOutputs[0].OperationIndex > NearbyFeedbackDistance)
                        {
                            recentOutputs.Clear();
                        }
                    }
                }
                AddControlFlowFacts(result, graph, procId: StableId(proc.head?.Id, $"proc-index-{procIndex}"));
            }

            if (!EquipmentTopologyStore.TryValidateDefinition(result.CandidateDefinition, out string error))
            {
                throw new InvalidOperationException("规则推断生成了无效候选：" + error);
            }
            return result;
        }

        private static void AddControlFlowFacts(
            TopologyRuleInferenceResult result,
            ProcessFlowGraphSnapshot graph,
            string procId)
        {
            Dictionary<string, FlowGraphNode> nodes = graph.Nodes
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (FlowGraphEdge edge in graph.Edges.Where(item => item != null
                && (item.AlarmPath || item.Loop || !string.IsNullOrWhiteSpace(item.SourceField))))
            {
                if (!nodes.TryGetValue(edge.SourceId ?? string.Empty, out FlowGraphNode source)
                    || !nodes.TryGetValue(edge.TargetId ?? string.Empty, out FlowGraphNode target)
                    || string.IsNullOrWhiteSpace(source.OpId)
                    || string.IsNullOrWhiteSpace(target.OpId))
                {
                    continue;
                }
                bool eligible = !source.Disabled && source.Reachable
                    && !target.Disabled && target.Reachable;
                string ruleId = edge.AlarmPath
                    ? "control.alarm_route"
                    : edge.Loop ? "control.retry_loop" : "control.branch_route";
                string role = edge.AlarmPath
                    ? "recovery_path"
                    : edge.Loop ? "retry_path" : "guard_path";
                string predicate = edge.AlarmPath
                    ? "alarm_routes_to"
                    : edge.Loop ? "retries_to" : "branches_to";
                result.Facts.Add(new TopologyInferenceFact
                {
                    FactId = "fact-" + Hash($"{procId}|{edge.Id}|{edge.SourceId}|{edge.TargetId}|{edge.SourceField}|control-flow"),
                    RuleId = ruleId,
                    ProcId = procId,
                    StepId = source.StepId,
                    OpId = source.OpId,
                    OperationType = source.OperaType,
                    ParameterPath = edge.SourceField ?? string.Empty,
                    ParameterValue = edge.ConfiguredTargetId ?? edge.TargetId,
                    ControlFlowRole = role,
                    SubjectKind = "operation",
                    SubjectRef = source.OpId,
                    Predicate = predicate,
                    ObjectKind = "operation",
                    ObjectRef = target.OpId,
                    Confidence = 1d,
                    EligibleForTopology = eligible
                });
            }
        }

        private static List<ResourceUse> ApplyOperationRules(RuleContext context)
        {
            var outputs = new List<ResourceUse>();
            if (context.Operation is IoOperate ioOperate)
            {
                int index = 0;
                foreach (IoOutParam item in ioOperate.IoParams ?? new OperationTypePartial.CustomList<IoOutParam>())
                {
                    TopologyInferenceFact fact = AddIoFact(
                        context, "io.output.write", $"IoParams[{index}]", item?.IoName,
                        item?.TargetState == true, "writes");
                    if (fact != null && context.Eligible)
                    {
                        EquipmentTopologyNode node = EnsureResourceNode(context, fact, "ioOutput", "actuator", 0.98d);
                        AddBooleanBinding(context, node, fact, "输出状态", "IO 输出被写为配置值", 0.98d);
                        outputs.Add(new ResourceUse(node, fact, context.OperationIndex));
                    }
                    index++;
                }
            }
            else if (context.Operation is IoCheck ioCheck)
            {
                int index = 0;
                foreach (IoCheckParam item in ioCheck.IoParams ?? new OperationTypePartial.CustomList<IoCheckParam>())
                {
                    TopologyInferenceFact fact = AddIoFact(
                        context, "io.input.check", $"IoParams[{index}]", item?.IoName,
                        item?.ExpectedState == true, "expects");
                    if (fact != null && context.Eligible)
                    {
                        EquipmentTopologyNode node = EnsureResourceNode(context, fact, "ioInput", "sensor", 0.98d);
                        AddBooleanBinding(context, node, fact, "检测满足", "输入值等于检测配置时条件满足", 0.98d);
                    }
                    index++;
                }
            }
            else if (context.Operation is IoGroup ioGroup)
            {
                var groupOutputs = new List<ResourceUse>();
                var groupInputs = new List<ResourceUse>();
                int index = 0;
                foreach (IoOutParam item in ioGroup.OutIoParams ?? new OperationTypePartial.CustomList<IoOutParam>())
                {
                    TopologyInferenceFact fact = AddIoFact(
                        context, "io.group.output", $"OutIoParams[{index}]", item?.IoName,
                        item?.TargetState == true, "writes");
                    if (fact != null && context.Eligible)
                    {
                        EquipmentTopologyNode node = EnsureResourceNode(context, fact, "ioOutput", "actuator", 0.99d);
                        AddBooleanBinding(context, node, fact, "输出状态", "IO 组把输出写为配置值", 0.99d);
                        var use = new ResourceUse(node, fact, context.OperationIndex);
                        groupOutputs.Add(use);
                        outputs.Add(use);
                    }
                    index++;
                }
                index = 0;
                foreach (IoCheckParam item in ioGroup.CheckIoParams ?? new OperationTypePartial.CustomList<IoCheckParam>())
                {
                    TopologyInferenceFact fact = AddIoFact(
                        context, "io.group.check", $"CheckIoParams[{index}]", item?.IoName,
                        item?.ExpectedState == true, "expects");
                    if (fact != null && context.Eligible)
                    {
                        EquipmentTopologyNode node = EnsureResourceNode(context, fact, "ioInput", "sensor", 0.99d);
                        AddBooleanBinding(context, node, fact, "检测满足", "IO 组输入值等于配置时条件满足", 0.99d);
                        groupInputs.Add(new ResourceUse(node, fact, context.OperationIndex));
                    }
                    index++;
                }
                foreach (ResourceUse input in groupInputs.Take(10))
                {
                    foreach (ResourceUse output in groupOutputs.Take(10))
                    {
                        EnsureRelation(context, input.Node, output.Node, "state", "observes",
                            "同组状态反馈候选", "同一 IO 组中共同配置，具体机械对应关系待确认",
                            0.82d, input.Fact, output.Fact);
                    }
                }
            }
            else if (context.Operation is IoLogicGoto logicGoto)
            {
                int index = 0;
                foreach (IoLogicGotoParam item in logicGoto.IoParams ?? new OperationTypePartial.CustomList<IoLogicGotoParam>())
                {
                    TopologyInferenceFact fact = AddIoFact(
                        context, "io.input.branch", $"IoParams[{index}]", item?.IoName,
                        item?.Target == true, "branches_when");
                    if (fact != null)
                    {
                        fact.ObjectKind = "logic";
                        fact.ObjectRef = item?.Logic ?? string.Empty;
                        if (context.Eligible)
                        {
                            EquipmentTopologyNode node = EnsureResourceNode(context, fact, "ioInput", "sensor", 0.98d);
                            AddBooleanBinding(context, node, fact, "分支条件满足", "输入值满足逻辑跳转配置", 0.96d);
                        }
                    }
                    index++;
                }
            }
            else if (context.Operation is StationRunPos stationRun)
            {
                AddStationRunFacts(context, stationRun.StationName, stationRun.PosName, stationRun.PosIndex);
            }
            else if (context.Operation is HomeRun homeRun)
            {
                TopologyInferenceFact fact = AddFact(context, "motion.station.home", "StationName",
                    homeRun.StationName, "station", homeRun.StationName, "homes", "mode",
                    homeRun.StationHomeType, 0.99d);
                if (fact != null && context.Eligible)
                {
                    EnsureResourceNode(context, fact, "station", "station", 0.99d);
                }
            }
            return outputs;
        }

        private static void AddStationRunFacts(
            RuleContext context,
            string stationName,
            string pointName,
            int pointIndex)
        {
            TopologyInferenceFact stationFact = AddFact(context, "motion.station.use", "StationName",
                stationName, "station", stationName, "moves_to", "motionPoint",
                string.IsNullOrWhiteSpace(pointName) ? "#index:" + pointIndex : pointName, 0.99d);
            if (stationFact == null || !context.Eligible)
            {
                return;
            }
            EquipmentTopologyNode station = EnsureResourceNode(context, stationFact, "station", "station", 0.99d);
            if (string.IsNullOrWhiteSpace(pointName))
            {
                return;
            }
            string pointRef = stationName.Trim() + "/" + pointName.Trim();
            TopologyInferenceFact pointFact = AddFact(context, "motion.point.use", "PosName",
                pointName, "motionPoint", pointRef, "belongs_to", "station", stationName, 0.99d);
            EquipmentTopologyNode point = EnsureResourceNode(context, pointFact, "motionPoint", "buffer", 0.99d);
            EnsureRelation(context, station, point, "physical", "contains", "包含运动点位",
                "工站走点指令同时精确引用该工站与点位", 0.99d, stationFact, pointFact);
        }

        private static void AddNearbyFeedbackRelations(RuleContext context, List<ResourceUse> recentOutputs)
        {
            if (recentOutputs == null || recentOutputs.Count == 0
                || context.OperationIndex - recentOutputs[0].OperationIndex > NearbyFeedbackDistance)
            {
                return;
            }
            List<TopologyInferenceFact> inputFacts = context.Result.Facts
                .Where(item => string.Equals(item.OpId, context.OpId, StringComparison.Ordinal)
                    && string.Equals(item.SubjectKind, "ioInput", StringComparison.Ordinal)
                    && item.EligibleForTopology)
                .ToList();
            foreach (TopologyInferenceFact inputFact in inputFacts.Take(10))
            {
                EquipmentTopologyNode input = FindResourceNode(context.Definition, "ioInput", inputFact.SubjectRef);
                foreach (ResourceUse output in recentOutputs.Take(10))
                {
                    EnsureRelation(context, input, output.Node, "state", "observes",
                        "相邻状态反馈候选", "输出后在相邻指令中检测，物理对应关系待确认",
                        0.68d, inputFact, output.Fact);
                }
            }
        }

        private static TopologyInferenceFact AddIoFact(
            RuleContext context,
            string ruleId,
            string parameterRoot,
            string ioName,
            bool value,
            string predicate)
        {
            string kind = predicate == "writes" ? "ioOutput" : "ioInput";
            return AddFact(context, ruleId, parameterRoot + ".IoName",
                ioName, kind, ioName, predicate, "boolean", value ? "true" : "false", 1d);
        }

        private static TopologyInferenceFact AddFact(
            RuleContext context,
            string ruleId,
            string parameterPath,
            string parameterValue,
            string subjectKind,
            string subjectRef,
            string predicate,
            string objectKind,
            string objectRef,
            double confidence)
        {
            if (string.IsNullOrWhiteSpace(subjectRef))
            {
                return null;
            }
            var fact = new TopologyInferenceFact
            {
                FactId = "fact-" + Hash($"{context.ProcId}|{context.StepId}|{context.OpId}|{ruleId}|{parameterPath}"),
                RuleId = ruleId,
                ProcId = context.ProcId,
                StepId = context.StepId,
                OpId = context.OpId,
                OperationType = context.Operation.GetType().Name,
                ParameterPath = parameterPath,
                ParameterValue = parameterValue ?? string.Empty,
                ControlFlowRole = context.Role,
                SubjectKind = subjectKind,
                SubjectRef = subjectRef.Trim(),
                Predicate = predicate,
                ObjectKind = objectKind,
                ObjectRef = objectRef ?? string.Empty,
                Confidence = confidence,
                EligibleForTopology = context.Eligible
            };
            context.Result.Facts.Add(fact);
            return fact;
        }

        private static EquipmentTopologyNode EnsureResourceNode(
            RuleContext context,
            TopologyInferenceFact fact,
            string resourceKind,
            string nodeKind,
            double confidence)
        {
            EquipmentTopologyNode existing = FindResourceNode(context.Definition, resourceKind, fact.SubjectRef);
            EquipmentTopologyEvidence evidence = ToEvidence(fact, "规则识别了精确资源引用");
            if (existing != null)
            {
                AddEvidence(existing.Evidence, evidence);
                return existing;
            }
            int number = context.Definition.Nodes.Count;
            var node = new EquipmentTopologyNode
            {
                Id = "node-" + Hash(resourceKind + "|" + fact.SubjectRef),
                Label = fact.SubjectRef,
                Kind = nodeKind,
                Zone = "流程推断",
                Description = "由指令类型与参数生成的候选对象，业务角色待确认。",
                ResourceKind = resourceKind,
                ResourceRef = fact.SubjectRef,
                X = 170 + number % 5 * 220,
                Y = 140 + number / 5 * 150,
                ReviewState = "candidate",
                Confidence = confidence,
                Evidence = new List<EquipmentTopologyEvidence> { evidence }
            };
            context.Definition.Nodes.Add(node);
            context.Result.NewNodeIds.Add(node.Id);
            return node;
        }

        private static void AddBooleanBinding(
            RuleContext context,
            EquipmentTopologyNode node,
            TopologyInferenceFact fact,
            string stateName,
            string meaning,
            double confidence)
        {
            if (node == null || fact == null)
            {
                return;
            }
            string expected = fact.ObjectRef;
            EquipmentTopologyStateBinding existing = node.StateBindings.FirstOrDefault(item =>
                item != null
                && string.Equals(item.SourceKind, "io", StringComparison.Ordinal)
                && string.Equals(item.ResourceRef, fact.SubjectRef, StringComparison.Ordinal)
                && string.Equals(item.Operator, "equals", StringComparison.Ordinal)
                && string.Equals(item.ExpectedValue, expected, StringComparison.OrdinalIgnoreCase));
            EquipmentTopologyEvidence evidence = ToEvidence(fact, meaning);
            if (existing != null)
            {
                AddEvidence(existing.Evidence, evidence);
                return;
            }
            var binding = new EquipmentTopologyStateBinding
            {
                Id = "binding-" + Hash(node.Id + "|io|" + fact.SubjectRef + "|equals|" + expected),
                StateName = stateName,
                SourceKind = "io",
                ResourceRef = fact.SubjectRef,
                Operator = "equals",
                ExpectedValue = expected,
                Meaning = meaning,
                Priority = 10,
                ReviewState = "candidate",
                Confidence = confidence,
                Evidence = new List<EquipmentTopologyEvidence> { evidence }
            };
            node.StateBindings.Add(binding);
            context.Result.NewBindingIds.Add(binding.Id);
        }

        private static void EnsureRelation(
            RuleContext context,
            EquipmentTopologyNode source,
            EquipmentTopologyNode target,
            string layer,
            string kind,
            string label,
            string description,
            double confidence,
            params TopologyInferenceFact[] facts)
        {
            if (source == null || target == null || source.Id == target.Id)
            {
                return;
            }
            EquipmentTopologyRelation existing = context.Definition.Relations.FirstOrDefault(item =>
                item != null
                && string.Equals(item.SourceNodeId, source.Id, StringComparison.Ordinal)
                && string.Equals(item.TargetNodeId, target.Id, StringComparison.Ordinal)
                && string.Equals(item.Layer, layer, StringComparison.Ordinal)
                && string.Equals(item.Kind, kind, StringComparison.Ordinal));
            if (existing != null)
            {
                foreach (TopologyInferenceFact fact in facts.Where(item => item != null))
                {
                    AddEvidence(existing.Evidence, ToEvidence(fact, description));
                }
                return;
            }
            var relation = new EquipmentTopologyRelation
            {
                Id = "relation-" + Hash(source.Id + "|" + target.Id + "|" + layer + "|" + kind),
                SourceNodeId = source.Id,
                TargetNodeId = target.Id,
                Layer = layer,
                Kind = kind,
                Label = label,
                Description = description,
                ReviewState = "candidate",
                Confidence = confidence,
                Evidence = facts.Where(item => item != null)
                    .Select(item => ToEvidence(item, description))
                    .GroupBy(item => item.SourceRef, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList()
            };
            context.Definition.Relations.Add(relation);
            context.Result.NewRelationIds.Add(relation.Id);
        }

        private static string ClassifyRole(
            OperationType operation,
            FlowGraphNode node,
            ProcessFlowGraphSnapshot graph,
            string opId)
        {
            if (node?.Disabled == true || operation.Disable)
            {
                return "disabled";
            }
            if (node != null && !node.Reachable)
            {
                return "unreachable";
            }
            string nodeId = "op:" + opId;
            bool alarmTarget = graph.Edges.Any(edge => edge.AlarmPath
                && string.Equals(edge.TargetId, nodeId, StringComparison.Ordinal));
            bool loop = graph.Edges.Any(edge => edge.Loop
                && (string.Equals(edge.SourceId, nodeId, StringComparison.Ordinal)
                    || string.Equals(edge.TargetId, nodeId, StringComparison.Ordinal)));
            if (alarmTarget)
            {
                return "recovery";
            }
            if (loop)
            {
                return "retry";
            }
            if (operation is Goto || operation is ParamGoto || operation is IoLogicGoto)
            {
                return "guard";
            }
            if (operation is Delay || operation is PopupDialog || operation is CycleTimeProbe
                || operation is EndProcess || operation is ConfigurationPlaceholder)
            {
                return "auxiliary";
            }
            return "main_action";
        }

        private static bool IsEligible(string role)
        {
            return !string.Equals(role, "disabled", StringComparison.Ordinal)
                && !string.Equals(role, "unreachable", StringComparison.Ordinal)
                && !string.Equals(role, "auxiliary", StringComparison.Ordinal);
        }

        private static EquipmentTopologyNode FindResourceNode(
            EquipmentTopologyDefinition definition,
            string resourceKind,
            string resourceRef)
        {
            return definition.Nodes.FirstOrDefault(item => item != null
                && string.Equals(item.ResourceKind, resourceKind, StringComparison.Ordinal)
                && string.Equals(item.ResourceRef, resourceRef, StringComparison.Ordinal));
        }

        private static EquipmentTopologyEvidence ToEvidence(TopologyInferenceFact fact, string detail)
        {
            return new EquipmentTopologyEvidence
            {
                SourceType = "rule",
                SourceRef = fact.FactId,
                OperationType = fact.OperationType,
                ParameterPath = fact.ParameterPath,
                Detail = $"{detail}；控制流角色={fact.ControlFlowRole}；参数值={fact.ParameterValue}"
            };
        }

        private static void AddEvidence(
            List<EquipmentTopologyEvidence> target,
            EquipmentTopologyEvidence evidence)
        {
            if (target == null || evidence == null || target.Count >= 50)
            {
                return;
            }
            if (!target.Any(item => item != null
                && string.Equals(item.SourceRef, evidence.SourceRef, StringComparison.Ordinal)
                && string.Equals(item.ParameterPath, evidence.ParameterPath, StringComparison.Ordinal)))
            {
                target.Add(evidence);
            }
        }

        private static void NormalizeCollections(EquipmentTopologyDefinition definition)
        {
            definition.Nodes = definition.Nodes ?? new List<EquipmentTopologyNode>();
            definition.Relations = definition.Relations ?? new List<EquipmentTopologyRelation>();
            foreach (EquipmentTopologyNode node in definition.Nodes.Where(item => item != null))
            {
                node.Evidence = node.Evidence ?? new List<EquipmentTopologyEvidence>();
                node.StateBindings = node.StateBindings ?? new List<EquipmentTopologyStateBinding>();
                foreach (EquipmentTopologyStateBinding binding in node.StateBindings.Where(item => item != null))
                {
                    binding.Evidence = binding.Evidence ?? new List<EquipmentTopologyEvidence>();
                }
            }
            foreach (EquipmentTopologyRelation relation in definition.Relations.Where(item => item != null))
            {
                relation.Evidence = relation.Evidence ?? new List<EquipmentTopologyEvidence>();
            }
        }

        private static string StableId(Guid? id, string fallback)
        {
            return id.HasValue && id.Value != Guid.Empty ? id.Value.ToString("D") : fallback;
        }

        internal static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return string.Concat(bytes.Take(10).Select(item => item.ToString("x2")));
            }
        }

        private sealed class RuleContext
        {
            public RuleContext(
                TopologyRuleInferenceResult result,
                string procId,
                string stepId,
                string opId,
                OperationType operation,
                string role,
                bool eligible,
                int operationIndex)
            {
                Result = result;
                ProcId = procId;
                StepId = stepId;
                OpId = opId;
                Operation = operation;
                Role = role;
                Eligible = eligible;
                OperationIndex = operationIndex;
            }

            public TopologyRuleInferenceResult Result { get; }
            public EquipmentTopologyDefinition Definition => Result.CandidateDefinition;
            public string ProcId { get; }
            public string StepId { get; }
            public string OpId { get; }
            public OperationType Operation { get; }
            public string Role { get; }
            public bool Eligible { get; }
            public int OperationIndex { get; }
        }

        private sealed class ResourceUse
        {
            public ResourceUse(
                EquipmentTopologyNode node,
                TopologyInferenceFact fact,
                int operationIndex)
            {
                Node = node;
                Fact = fact;
                OperationIndex = operationIndex;
            }

            public EquipmentTopologyNode Node { get; }
            public TopologyInferenceFact Fact { get; }
            public int OperationIndex { get; }
        }
    }
}
