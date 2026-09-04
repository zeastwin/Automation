using Automation.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// 模块：编辑器 / 设备拓扑 AI 精修。
// 职责范围：把规则事实交给现有 AI，严格校验候选提案；不调用平台工具、不保存配置、不控制设备。

namespace Automation
{
    internal sealed class TopologyAiRefinementResult
    {
        public EquipmentTopologyDefinition CandidateDefinition { get; set; }
        public int AddedNodeCount { get; set; }
        public int AddedRelationCount { get; set; }
        public int AddedBindingCount { get; set; }
        public int AddedSkillCount { get; set; }
        public int RejectedProposalCount { get; set; }
        public List<string> RejectionReasons { get; } = new List<string>();
        public string Summary { get; set; }

        public string BuildSummary()
        {
            string prefix = string.IsNullOrWhiteSpace(Summary)
                ? "AI 已基于规则证据生成精修候选。"
                : Summary.Trim();
            return prefix + $" 新增 {AddedNodeCount} 个节点、{AddedBindingCount} 条状态语义、"
                + $"{AddedSkillCount} 个节点技能、{AddedRelationCount} 条关系；"
                + $"拒绝 {RejectedProposalCount} 条不合规提案。"
                + BuildRejectionSummary();
        }

        private string BuildRejectionSummary()
        {
            if (RejectionReasons.Count == 0) return string.Empty;
            string details = string.Join("；", RejectionReasons.Take(5));
            return " 拒绝原因：" + details
                + (RejectionReasons.Count > 5 ? $"；另有 {RejectionReasons.Count - 5} 类原因。" : "。");
        }
    }

    internal static class TopologyAiRefinementService
    {
        private const int MaxFactsInPrompt = 800;
        private const int MaxProposals = 200;
        private static readonly HashSet<string> NodeKinds = NewSet(
            "station", "mechanism", "actuator", "sensor", "workpiece", "fixture", "safety", "buffer");
        private static readonly HashSet<string> Layers = NewSet(
            "physical", "state", "interlock", "recovery");
        private static readonly HashSet<string> RelationKinds = NewSet(
            "contains", "installed_on", "moves_with", "transfers_to", "drives", "observes",
            "requires", "blocks", "recovers_to");
        // 当前实时感知只编译 IO；AI 不生成尚无采集器的其他状态绑定。
        private static readonly HashSet<string> SourceKinds = NewSet("io");
        private static readonly HashSet<string> Operators = NewSet(
            "equals", "not_equals", "active", "inactive");
        private static readonly HashSet<string> ReviewStates = NewSet("candidate", "conflict");
        private static readonly HashSet<string> SkillExecutionModes = NewSet(
            MachineExecutionModes.SingleOperation);

        public static async Task<TopologyAiRefinementResult> RefineAsync(
            FrmMain owner,
            TopologyRuleInferenceResult ruleResult,
            CancellationToken cancellationToken,
            Action<string> progress)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (ruleResult?.CandidateDefinition == null) throw new ArgumentNullException(nameof(ruleResult));
            progress?.Invoke("正在启动受限 AI 精修会话…");
            if (!owner.TryEnsureMachineAgentInfrastructureStarted(out string infrastructureError))
            {
                throw new InvalidOperationException(infrastructureError);
            }
            if (!GooseRuntimeProvisioner.IsManagedContextAvailable)
            {
                throw new InvalidOperationException("AI 受管上下文尚未由平台启动阶段完成部署。");
            }
            if (!GooseConfigStorage.TryGetCached(out GooseConfig stored, out string configError))
            {
                throw new InvalidOperationException(configError);
            }
            string mcpUri = await owner.McpServerManager
                .EnsureTaskCapabilityStartedAsync(AutomationToolProfiles.TaskCoordinator, cancellationToken)
                .ConfigureAwait(true);
            GooseConfig config = CreateConfig(stored, mcpUri);
            progress?.Invoke("AI 正在核对状态语义、机构关系与恢复证据…");
            string response;
            using (var client = new GooseAcpClient(owner.Runtime, config))
            {
                client.PermissionRequestHandler = DenyAllTools;
                await client.PromptAsync(
                    BuildPrompt(ruleResult),
                    Array.Empty<GooseFileAttachment>(),
                    cancellationToken,
                    "设备拓扑证据精修")
                    .ConfigureAwait(true);
                response = client.LastAssistantResponse;
            }
            progress?.Invoke("正在机械校验 AI 提案与证据引用…");
            return ApplyResponse(ruleResult, response);
        }

        internal static TopologyAiRefinementResult ApplyResponse(
            TopologyRuleInferenceResult ruleResult,
            string response)
        {
            JObject root = ParseJsonObject(response);
            var result = new TopologyAiRefinementResult
            {
                CandidateDefinition = ObjectGraphCloner.Clone(ruleResult.CandidateDefinition),
                Summary = root["summary"]?.Value<string>() ?? string.Empty
            };
            Dictionary<string, TopologyInferenceFact> facts = ruleResult.Facts
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.FactId))
                .GroupBy(item => item.FactId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var localNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
            JArray proposals = root["proposals"] as JArray
                ?? throw new InvalidOperationException("AI 返回缺少 proposals 数组。");
            int proposalIndex = 0;
            foreach (JToken proposalToken in proposals.Take(MaxProposals))
            {
                string action = proposalToken is JObject proposalObject
                    && proposalObject["action"]?.Type == JTokenType.String
                        ? proposalObject["action"].Value<string>()
                        : "<unknown>";
                try
                {
                    JObject proposal = proposalToken as JObject
                        ?? throw new InvalidOperationException("提案必须是 JSON 对象。");
                    action = RequiredString(proposal, "action");
                    List<TopologyInferenceFact> evidenceFacts = ResolveFacts(proposal, facts);
                    switch (action)
                    {
                        case "node.add":
                            ApplyNode(result, proposal, evidenceFacts, localNodeIds);
                            break;
                        case "relation.add":
                            ApplyRelation(result, proposal, evidenceFacts, localNodeIds);
                            break;
                        case "stateBinding.add":
                            ApplyBinding(result, proposal, evidenceFacts, localNodeIds);
                            break;
                        case "skill.add":
                            ApplySkill(result, proposal, evidenceFacts, localNodeIds);
                            break;
                        default:
                            throw new InvalidOperationException("action 不在白名单中：" + action);
                    }
                }
                catch (Exception ex)
                {
                    result.RejectedProposalCount++;
                    AddRejectionReason(result, proposalIndex, action, ex.Message);
                }
                proposalIndex++;
            }
            int overflow = Math.Max(0, proposals.Count - MaxProposals);
            result.RejectedProposalCount += overflow;
            if (overflow > 0)
            {
                result.RejectionReasons.Add($"proposals[{MaxProposals}..]：超过 {MaxProposals} 条提案上限，忽略 {overflow} 条");
            }
            if (!EquipmentTopologyStore.TryValidateDefinition(result.CandidateDefinition, out string error))
            {
                throw new InvalidOperationException("AI 精修候选未通过拓扑校验：" + error);
            }
            return result;
        }

        private static void AddRejectionReason(
            TopologyAiRefinementResult result,
            int proposalIndex,
            string action,
            string reason)
        {
            if (result.RejectionReasons.Count >= 50) return;
            string normalizedAction = string.IsNullOrWhiteSpace(action) ? "<unknown>" : action.Trim();
            string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "未提供拒绝原因" : reason.Trim();
            result.RejectionReasons.Add($"proposals[{proposalIndex}] {normalizedAction}：{Limit(normalizedReason, 300)}");
        }

        private static void ApplyNode(
            TopologyAiRefinementResult result,
            JObject proposal,
            List<TopologyInferenceFact> facts,
            Dictionary<string, string> localNodeIds)
        {
            string key = RequiredString(proposal, "key");
            if (localNodeIds.ContainsKey(key))
            {
                throw new InvalidOperationException("节点局部 key 重复。");
            }
            string label = Limit(RequiredString(proposal, "label"), 160);
            string kind = RequiredEnum(proposal, "kind", NodeKinds);
            string resourceKind = Limit(proposal["resourceKind"]?.Value<string>(), 80);
            string resourceRef = Limit(proposal["resourceRef"]?.Value<string>(), 240);
            if (!string.IsNullOrWhiteSpace(resourceRef)
                && !facts.Any(item => string.Equals(item.SubjectRef, resourceRef, StringComparison.Ordinal)
                    || string.Equals(item.ObjectRef, resourceRef, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("resourceRef 未被引用事实证明。");
            }
            EquipmentTopologyNode duplicate = result.CandidateDefinition.Nodes.FirstOrDefault(item => item != null
                && string.Equals(item.Kind, kind, StringComparison.Ordinal)
                && string.Equals(item.Label, label, StringComparison.Ordinal)
                && string.Equals(item.ResourceKind ?? string.Empty, resourceKind ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(item.ResourceRef ?? string.Empty, resourceRef ?? string.Empty, StringComparison.Ordinal));
            if (duplicate != null)
            {
                localNodeIds[key] = duplicate.Id;
                return;
            }
            int number = result.CandidateDefinition.Nodes.Count;
            var node = new EquipmentTopologyNode
            {
                Id = "ai-node-" + Guid.NewGuid().ToString("N"),
                Label = label,
                Kind = kind,
                Zone = Limit(proposal["zone"]?.Value<string>(), 100),
                Description = Limit(proposal["description"]?.Value<string>(), 600),
                ResourceKind = resourceKind,
                ResourceRef = resourceRef,
                X = 210 + number % 5 * 220,
                Y = 170 + number / 5 * 150,
                ReviewState = RequiredReviewState(proposal),
                Confidence = ReadConfidence(proposal),
                Evidence = BuildEvidence(facts, "AI 提议新增语义对象")
            };
            result.CandidateDefinition.Nodes.Add(node);
            localNodeIds[key] = node.Id;
            result.AddedNodeCount++;
        }

        private static void ApplyRelation(
            TopologyAiRefinementResult result,
            JObject proposal,
            List<TopologyInferenceFact> facts,
            Dictionary<string, string> localNodeIds)
        {
            string sourceId = ResolveNodeId(result.CandidateDefinition, localNodeIds,
                RequiredString(proposal, "sourceRef"));
            string targetId = ResolveNodeId(result.CandidateDefinition, localNodeIds,
                RequiredString(proposal, "targetRef"));
            if (string.Equals(sourceId, targetId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("关系不能连接节点自身。");
            }
            EquipmentTopologyNode sourceNode = result.CandidateDefinition.Nodes
                .First(item => string.Equals(item.Id, sourceId, StringComparison.Ordinal));
            EquipmentTopologyNode targetNode = result.CandidateDefinition.Nodes
                .First(item => string.Equals(item.Id, targetId, StringComparison.Ordinal));
            if (!FactsSupportNode(sourceNode, facts) || !FactsSupportNode(targetNode, facts))
            {
                throw new InvalidOperationException("relation.add 的证据没有同时证明关系两端节点。");
            }
            string layer = RequiredEnum(proposal, "layer", Layers);
            string kind = RequiredEnum(proposal, "kind", RelationKinds);
            string condition = Limit(proposal["condition"]?.Value<string>(), 300);
            EquipmentTopologyRelation duplicate = result.CandidateDefinition.Relations.FirstOrDefault(item => item != null
                && string.Equals(item.SourceNodeId, sourceId, StringComparison.Ordinal)
                && string.Equals(item.TargetNodeId, targetId, StringComparison.Ordinal)
                && string.Equals(item.Layer, layer, StringComparison.Ordinal)
                && string.Equals(item.Kind, kind, StringComparison.Ordinal)
                && string.Equals(item.Condition ?? string.Empty, condition ?? string.Empty, StringComparison.Ordinal));
            if (duplicate != null)
            {
                return;
            }
            var relation = new EquipmentTopologyRelation
            {
                Id = "ai-relation-" + Guid.NewGuid().ToString("N"),
                SourceNodeId = sourceId,
                TargetNodeId = targetId,
                Layer = layer,
                Kind = kind,
                Label = Limit(proposal["label"]?.Value<string>(), 160),
                Condition = condition,
                Description = Limit(proposal["description"]?.Value<string>(), 600),
                ReviewState = RequiredReviewState(proposal),
                Confidence = ReadConfidence(proposal),
                ConflictsWithId = ResolveOptionalRelationId(result.CandidateDefinition,
                    proposal["conflictsWithId"]?.Value<string>()),
                Evidence = BuildEvidence(facts, "AI 提议新增拓扑关系")
            };
            result.CandidateDefinition.Relations.Add(relation);
            result.AddedRelationCount++;
        }

        private static void ApplyBinding(
            TopologyAiRefinementResult result,
            JObject proposal,
            List<TopologyInferenceFact> facts,
            Dictionary<string, string> localNodeIds)
        {
            string nodeId = ResolveNodeId(result.CandidateDefinition, localNodeIds,
                RequiredString(proposal, "targetRef"));
            EquipmentTopologyNode node = result.CandidateDefinition.Nodes.First(item => item.Id == nodeId);
            string sourceKind = RequiredEnum(proposal, "sourceKind", SourceKinds);
            string resourceRef = Limit(RequiredString(proposal, "resourceRef"), 240);
            if (!facts.Any(item => string.Equals(item.SubjectRef, resourceRef, StringComparison.Ordinal)
                || string.Equals(item.ObjectRef, resourceRef, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("状态绑定资源未被引用事实证明。");
            }
            if (!FactsSupportNode(node, facts))
            {
                throw new InvalidOperationException("stateBinding.add 的目标节点与引用证据没有可证明的资源关联。");
            }
            string op = RequiredEnum(proposal, "operator", Operators);
            string expected = Limit(RequiredString(proposal, "expectedValue"), 120);
            if (!EquipmentTopologyStore.TryParseIoBoolean(expected, out _))
            {
                throw new InvalidOperationException("stateBinding.add 的 IO expectedValue 不是明确布尔值。");
            }
            EquipmentTopologyStateBinding duplicate = node.StateBindings.FirstOrDefault(item => item != null
                && string.Equals(item.SourceKind, sourceKind, StringComparison.Ordinal)
                && string.Equals(item.ResourceRef, resourceRef, StringComparison.Ordinal)
                && string.Equals(item.Operator, op, StringComparison.Ordinal)
                && string.Equals(item.ExpectedValue, expected, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
            {
                return;
            }
            var binding = new EquipmentTopologyStateBinding
            {
                Id = "ai-binding-" + Guid.NewGuid().ToString("N"),
                StateName = Limit(RequiredString(proposal, "stateName"), 160),
                SourceKind = sourceKind,
                ResourceRef = resourceRef,
                Operator = op,
                ExpectedValue = expected,
                Meaning = Limit(RequiredString(proposal, "meaning"), 600),
                Priority = Math.Max(-1000, Math.Min(1000, proposal["priority"]?.Value<int?>() ?? 20)),
                ReviewState = RequiredReviewState(proposal),
                Confidence = ReadConfidence(proposal),
                Evidence = BuildEvidence(facts, "AI 提议新增状态语义")
            };
            node.StateBindings.Add(binding);
            result.AddedBindingCount++;
        }

        private static void ApplySkill(
            TopologyAiRefinementResult result,
            JObject proposal,
            List<TopologyInferenceFact> facts,
            Dictionary<string, string> localNodeIds)
        {
            string nodeId = ResolveNodeId(result.CandidateDefinition, localNodeIds,
                RequiredString(proposal, "targetRef"));
            EquipmentTopologyNode node = result.CandidateDefinition.Nodes.First(item => item.Id == nodeId);
            string processId = Limit(RequiredString(proposal, "processId"), 36);
            string operationId = Limit(RequiredString(proposal, "operationId"), 36);
            if (!Guid.TryParse(processId, out Guid parsedProcessId) || parsedProcessId == Guid.Empty
                || !Guid.TryParse(operationId, out Guid parsedOperationId) || parsedOperationId == Guid.Empty)
            {
                throw new InvalidOperationException("skill.add 必须使用规则事实中的稳定流程和指令 ID。");
            }
            processId = parsedProcessId.ToString("D");
            operationId = parsedOperationId.ToString("D");
            List<TopologyInferenceFact> operationFacts = facts
                .Where(fact => FactSupportsOperation(fact, processId, operationId))
                .ToList();
            if (operationFacts.Count == 0)
            {
                throw new InvalidOperationException("skill.add 的流程或指令未被引用证据证明。");
            }
            if (!FactsSupportNode(node, operationFacts))
            {
                throw new InvalidOperationException("skill.add 的目标节点与引用证据没有可证明的资源关联。");
            }

            string executionMode = RequiredEnum(proposal, "executionMode", SkillExecutionModes);
            List<string> preconditions = ReadRequiredStringArray(proposal, "preconditions", 30, 300);
            EquipmentTopologySkillBinding duplicate = node.Skills.FirstOrDefault(item => item != null
                && string.Equals(item.ProcessId, processId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.OperationId, operationId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ExecutionMode, executionMode, StringComparison.Ordinal));
            if (duplicate != null) return;

            node.Skills.Add(new EquipmentTopologySkillBinding
            {
                Id = "ai-skill-" + Guid.NewGuid().ToString("N"),
                Name = Limit(RequiredString(proposal, "name"), 160),
                Description = Limit(proposal["description"]?.Value<string>(), 600),
                ActionKind = "process_operation",
                ProcessId = processId,
                OperationId = operationId,
                ExecutionMode = executionMode,
                Objective = Limit(RequiredString(proposal, "objective"), 600),
                ExpectedOutcome = Limit(RequiredString(proposal, "expectedOutcome"), 600),
                Preconditions = preconditions,
                ReviewState = RequiredReviewState(proposal),
                Confidence = ReadConfidence(proposal),
                Evidence = BuildEvidence(operationFacts, "AI 提议绑定既有流程指令技能")
            });
            result.AddedSkillCount++;
        }

        private static bool FactSupportsOperation(
            TopologyInferenceFact fact,
            string processId,
            string operationId)
        {
            return fact != null
                && string.Equals(fact.ProcId, processId, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(fact.OpId, operationId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fact.SubjectKind, "operation", StringComparison.Ordinal)
                    && (string.Equals(fact.SubjectRef, operationId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fact.ObjectRef, operationId, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool FactsSupportNode(
            EquipmentTopologyNode node,
            IReadOnlyCollection<TopologyInferenceFact> facts)
        {
            if (!string.IsNullOrWhiteSpace(node.ResourceRef)
                && facts.Any(fact => string.Equals(fact.SubjectRef, node.ResourceRef, StringComparison.Ordinal)
                    || string.Equals(fact.ObjectRef, node.ResourceRef, StringComparison.Ordinal)))
            {
                return true;
            }
            var factIds = new HashSet<string>(facts.Select(item => item.FactId), StringComparer.Ordinal);
            return (node.Evidence ?? new List<EquipmentTopologyEvidence>()).Any(item =>
                item != null && factIds.Contains(item.SourceRef ?? string.Empty));
        }

        private static List<string> ReadRequiredStringArray(
            JObject value,
            string name,
            int maximumCount,
            int maximumLength)
        {
            JArray array = value[name] as JArray
                ?? throw new InvalidOperationException(name + " 必须是字符串数组。");
            List<string> result = array.Values<string>()
                .Select(item => Limit(item, maximumLength)?.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (result.Count == 0 || result.Count > maximumCount || result.Count != array.Count)
            {
                throw new InvalidOperationException($"{name} 必须包含 1 到 {maximumCount} 条非空且不重复的文字条件。");
            }
            return result;
        }

        private static List<TopologyInferenceFact> ResolveFacts(
            JObject proposal,
            IReadOnlyDictionary<string, TopologyInferenceFact> allFacts)
        {
            JArray ids = proposal["evidenceIds"] as JArray
                ?? throw new InvalidOperationException("提案缺少 evidenceIds。");
            var result = new List<TopologyInferenceFact>();
            foreach (string id in ids.Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Distinct())
            {
                if (!allFacts.TryGetValue(id, out TopologyInferenceFact fact)
                    || !fact.EligibleForTopology)
                {
                    throw new InvalidOperationException("提案引用了未知或已排除的证据。");
                }
                result.Add(fact);
            }
            if (result.Count == 0 || result.Count > 20)
            {
                throw new InvalidOperationException("每个提案必须引用 1 到 20 条有效规则证据。");
            }
            return result;
        }

        private static List<EquipmentTopologyEvidence> BuildEvidence(
            IEnumerable<TopologyInferenceFact> facts,
            string detail)
        {
            return facts.Select(fact => new EquipmentTopologyEvidence
            {
                SourceType = "ai_refinement",
                SourceRef = fact.FactId,
                OperationType = fact.OperationType,
                ParameterPath = fact.ParameterPath,
                Detail = detail + "；原始规则=" + fact.RuleId + "；控制流角色=" + fact.ControlFlowRole
            }).Take(50).ToList();
        }

        private static GooseConfig CreateConfig(GooseConfig source, string mcpUri)
        {
            return new GooseConfig
            {
                GooseExecutablePath = source.GooseExecutablePath,
                WorkingDirectory = source.WorkingDirectory,
                McpUri = mcpUri,
                SessionName = "topology_refinement_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Provider = source.Provider,
                Model = source.Model,
                ModelServiceId = source.ModelServiceId,
                ModelServices = GooseConfigStorage.CloneModelServices(source.ModelServices),
                Temperature = source.Temperature,
                MaxTurns = source.MaxTurns,
                MaxOutputTokens = source.MaxOutputTokens,
                ThinkingEffort = source.ThinkingEffort,
                AutoApproveMode = false,
                ToolProfile = AutomationToolProfiles.TaskCoordinator,
                TaskCapabilityNotice = "设备拓扑 AI 精修为纯计算任务，所有工具调用均被拒绝。"
            };
        }

        private static JObject DenyAllTools(JObject request)
        {
            return new JObject
            {
                ["outcome"] = new JObject { ["outcome"] = "cancelled" }
            };
        }

        private static string BuildPrompt(TopologyRuleInferenceResult ruleResult)
        {
            EquipmentTopologyDefinition definition = ruleResult.CandidateDefinition;
            List<TopologyInferenceFact> eligibleFacts = ruleResult.Facts
                .Where(item => item.EligibleForTopology)
                .ToList();
            List<TopologyInferenceFact> controlFacts = eligibleFacts
                .Where(item => string.Equals(item.SubjectKind, "operation", StringComparison.Ordinal))
                .Take(200)
                .ToList();
            List<TopologyInferenceFact> facts = controlFacts.Concat(eligibleFacts
                    .Where(item => !string.Equals(item.SubjectKind, "operation", StringComparison.Ordinal)))
                .Take(MaxFactsInPrompt)
                .ToList();
            var input = new JObject
            {
                ["contract"] = "equipment-topology-refinement-v1",
                ["rulesSummary"] = ruleResult.BuildSummary(),
                ["factsTruncated"] = eligibleFacts.Count > facts.Count,
                ["nodes"] = new JArray(definition.Nodes.Take(1000).Select(node => new JObject
                {
                    ["id"] = node.Id,
                    ["label"] = node.Label,
                    ["kind"] = node.Kind,
                    ["resourceKind"] = node.ResourceKind,
                    ["resourceRef"] = node.ResourceRef,
                    ["reviewState"] = node.ReviewState,
                    ["stateBindings"] = JToken.FromObject(node.StateBindings ?? new List<EquipmentTopologyStateBinding>()),
                    ["skills"] = new JArray((node.Skills ?? new List<EquipmentTopologySkillBinding>())
                        .Where(skill => skill != null).Take(100).Select(skill => new JObject
                        {
                            ["id"] = skill.Id,
                            ["processId"] = skill.ProcessId,
                            ["operationId"] = skill.OperationId,
                            ["executionMode"] = skill.ExecutionMode,
                            ["reviewState"] = skill.ReviewState
                        }))
                })),
                ["relations"] = new JArray(definition.Relations.Take(2000).Select(relation => new JObject
                {
                    ["id"] = relation.Id,
                    ["sourceNodeId"] = relation.SourceNodeId,
                    ["targetNodeId"] = relation.TargetNodeId,
                    ["layer"] = relation.Layer,
                    ["kind"] = relation.Kind,
                    ["reviewState"] = relation.ReviewState,
                    ["confidence"] = relation.Confidence
                })),
                ["facts"] = JArray.FromObject(facts)
            };
            return @"你是设备拓扑证据精修器。本次是纯计算任务，不得调用任何工具。
只能使用输入中的 facts；不得依据流程名、步骤名、指令显示名或常识单独生成结论。资源名虽来自真实参数，也只能作为候选语义线索。
禁用、不可达、辅助指令证据不得用于提案。所有输出都是 candidate 或 conflict，绝不能声称已确认、可安全运行或可直接恢复设备。
优先完成四类价值：把 true/false 补成有意义但待确认的机构状态；识别输出、反馈传感器与机构间候选关系；把报警路径、循环重试中的证据组织为 recovery/interlock 候选；证据能同时证明目标节点、稳定流程ID和稳定指令ID时，可提议绑定已有指令的节点技能。证据不足就不提案。
skill.add 只允许 single_operation，且只是待人工确认的配置候选，不会执行设备控制。前置条件必须来自事实能够支持的保守描述，不能声称已机械验证。

只返回一个 JSON 对象，不要 Markdown、解释或代码围栏：
{
  ""summary"":""一句中文摘要"",
  ""proposals"":[
    {""action"":""node.add"",""key"":""局部key"",""label"":""名称"",""kind"":""mechanism|actuator|sensor|station|workpiece|fixture|safety|buffer"",""zone"":"""",""description"":"""",""resourceKind"":""可空"",""resourceRef"":""可空且非空时必须精确来自证据"",""reviewState"":""candidate|conflict"",""confidence"":0.0,""evidenceIds"":[""fact-id""]},
    {""action"":""relation.add"",""sourceRef"":""已有节点id或局部key"",""targetRef"":""已有节点id或局部key"",""layer"":""physical|state|interlock|recovery"",""kind"":""contains|installed_on|moves_with|transfers_to|drives|observes|requires|blocks|recovers_to"",""label"":"""",""condition"":"""",""description"":"""",""reviewState"":""candidate|conflict"",""confidence"":0.0,""conflictsWithId"":""可空已有关系id"",""evidenceIds"":[""fact-id""]},
    {""action"":""stateBinding.add"",""targetRef"":""已有节点id或局部key"",""stateName"":"""",""sourceKind"":""io"",""resourceRef"":""必须精确来自证据"",""operator"":""equals|not_equals|active|inactive"",""expectedValue"":""明确布尔值"",""meaning"":"""",""priority"":20,""reviewState"":""candidate|conflict"",""confidence"":0.0,""evidenceIds"":[""fact-id""]},
    {""action"":""skill.add"",""targetRef"":""已有节点id或局部key"",""name"":"""",""description"":"""",""processId"":""证据中的稳定流程Guid"",""operationId"":""证据中的稳定指令Guid"",""executionMode"":""single_operation"",""objective"":"""",""expectedOutcome"":"""",""preconditions"":[""条件""],""reviewState"":""candidate|conflict"",""confidence"":0.0,""evidenceIds"":[""fact-id""]}
  ]
}
confidence 最高 0.95。不要修改或删除已有项；不要重复已有候选。输入如下：
" + input.ToString(Formatting.None);
        }

        private static JObject ParseJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("AI 未返回精修结果。");
            }
            string value = text.Trim();
            int start = value.IndexOf('{');
            int end = value.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                throw new InvalidOperationException("AI 返回不是 JSON 对象。");
            }
            try
            {
                return JObject.Parse(value.Substring(start, end - start + 1));
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("AI 返回 JSON 无法解析：" + ex.Message, ex);
            }
        }

        private static string ResolveNodeId(
            EquipmentTopologyDefinition definition,
            IReadOnlyDictionary<string, string> localIds,
            string reference)
        {
            if (definition.Nodes.Any(item => item != null
                && string.Equals(item.Id, reference, StringComparison.Ordinal)))
            {
                return reference;
            }
            if (localIds.TryGetValue(reference, out string id))
            {
                return id;
            }
            throw new InvalidOperationException("提案引用了不存在的节点：" + reference);
        }

        private static string ResolveOptionalRelationId(
            EquipmentTopologyDefinition definition,
            string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return definition.Relations.Any(item => item != null
                && string.Equals(item.Id, id, StringComparison.Ordinal)) ? id : null;
        }

        private static string RequiredString(JObject value, string name)
        {
            string result = value[name]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                throw new InvalidOperationException("提案字段为空：" + name);
            }
            return result;
        }

        private static string RequiredEnum(JObject value, string name, HashSet<string> allowed)
        {
            string result = RequiredString(value, name);
            if (!allowed.Contains(result))
            {
                throw new InvalidOperationException("提案枚举值无效：" + name);
            }
            return result;
        }

        private static string RequiredReviewState(JObject value)
        {
            string state = value["reviewState"]?.Value<string>() ?? "candidate";
            return ReviewStates.Contains(state) ? state : "candidate";
        }

        private static double ReadConfidence(JObject value)
        {
            double confidence = value["confidence"]?.Value<double?>() ?? 0.5d;
            if (double.IsNaN(confidence) || double.IsInfinity(confidence)) return 0.5d;
            return Math.Max(0d, Math.Min(0.95d, confidence));
        }

        private static string Limit(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
        }

        private static HashSet<string> NewSet(params string[] values)
        {
            return new HashSet<string>(values ?? Array.Empty<string>(), StringComparer.Ordinal);
        }
    }
}
