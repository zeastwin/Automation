using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

// 模块：持久化 / 设备拓扑孪生。
// 职责范围：严格校验并原子保存设备拓扑配置，不执行设备动作或推断安全结论。

namespace Automation
{
    /// <summary>
    /// 当前设备拓扑孪生配置的内存单一事实源。
    /// </summary>
    public sealed class EquipmentTopologyStore
    {
        private const string FileName = "EquipmentTopology";
        private const int MaxNodeCount = 2000;
        private const int MaxRelationCount = 5000;
        private const int MaxBindingsPerNode = 100;
        private const int MaxSkillsPerNode = 100;
        private const int MaxEvidencePerRelation = 50;

        private static readonly HashSet<string> NodeKinds = NewSet(
            "station", "mechanism", "actuator", "sensor", "workpiece", "fixture", "safety", "buffer");
        private static readonly HashSet<string> RelationLayers = NewSet(
            "physical", "state", "interlock", "recovery");
        private static readonly HashSet<string> RelationKinds = NewSet(
            "contains", "installed_on", "moves_with", "transfers_to", "drives", "observes",
            "requires", "blocks", "recovers_to");
        private static readonly HashSet<string> ReviewStates = NewSet(
            "confirmed", "candidate", "conflict");
        private static readonly HashSet<string> BindingSourceKinds = NewSet(
            "io", "variable", "axis", "runtime");
        private static readonly HashSet<string> BindingOperators = NewSet(
            "equals", "not_equals", "greater_than", "less_than", "active", "inactive");
        private static readonly HashSet<string> IoBindingOperators = NewSet(
            "equals", "not_equals", "active", "inactive");

        private readonly object syncRoot = new object();
        private EquipmentTopologyDefinition current = CreateEmpty();
        private long version;

        public long Version => Interlocked.Read(ref version);

        public bool Load(string configPath, out string error)
        {
            error = null;
            Directory.CreateDirectory(configPath);
            string filePath = Path.Combine(configPath, FileName + ".json");
            if (!File.Exists(filePath))
            {
                return TryCommit(configPath, CreateEmpty(), out error);
            }

            EquipmentTopologyDefinition loaded =
                AtomicJsonFileStore.Read<EquipmentTopologyDefinition>(configPath, FileName);
            if (loaded == null)
            {
                error = "设备拓扑孪生配置主文件及备份均无法读取。";
                return false;
            }
            if (!TryValidateDefinition(loaded, out error))
            {
                error = "设备拓扑孪生配置无效：" + error;
                return false;
            }
            ReplaceCurrent(loaded);
            return true;
        }

        public EquipmentTopologyDefinition CreateSnapshot()
        {
            lock (syncRoot)
            {
                return ObjectGraphCloner.Clone(current);
            }
        }

        public bool TryCommit(
            string configPath,
            EquipmentTopologyDefinition definition,
            out string error)
        {
            error = null;
            if (definition == null)
            {
                error = "设备拓扑孪生配置为空。";
                return false;
            }

            EquipmentTopologyDefinition candidate = ObjectGraphCloner.Clone(definition);
            if (!TryValidateDefinition(candidate, out error))
            {
                return false;
            }

            lock (syncRoot)
            {
                candidate.Revision = Math.Max(0, current?.Revision ?? 0) + 1;
                candidate.UpdatedAtUtc = DateTime.UtcNow;
                if (!AtomicJsonFileStore.Save(configPath, FileName, candidate))
                {
                    error = "设备拓扑孪生配置保存失败，正式内存未修改。";
                    return false;
                }
                current = candidate;
                Interlocked.Increment(ref version);
            }
            return true;
        }

        private void ReplaceCurrent(EquipmentTopologyDefinition definition)
        {
            lock (syncRoot)
            {
                current = ObjectGraphCloner.Clone(definition);
                Interlocked.Increment(ref version);
            }
        }

        internal static bool TryValidateDefinition(EquipmentTopologyDefinition definition, out string error)
        {
            error = null;
            if (definition.SchemaVersion != EquipmentTopologyDefinition.CurrentSchemaVersion)
            {
                error = $"SchemaVersion 必须为 {EquipmentTopologyDefinition.CurrentSchemaVersion}。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                error = "拓扑名称不能为空。";
                return false;
            }
            if (definition.Nodes == null || definition.Relations == null)
            {
                error = "节点或关系集合不能为空。";
                return false;
            }
            if (definition.Nodes.Count > MaxNodeCount || definition.Relations.Count > MaxRelationCount)
            {
                error = $"节点最多 {MaxNodeCount} 个，关系最多 {MaxRelationCount} 条。";
                return false;
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < definition.Nodes.Count; i++)
            {
                EquipmentTopologyNode node = definition.Nodes[i];
                if (node == null)
                {
                    error = $"第 {i + 1} 个节点为空。";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(node.Id) || !nodeIds.Add(node.Id))
                {
                    error = $"第 {i + 1} 个节点标识为空或重复。";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(node.Label))
                {
                    error = $"节点“{node.Id}”名称不能为空。";
                    return false;
                }
                if (!NodeKinds.Contains(node.Kind ?? string.Empty))
                {
                    error = $"节点“{node.Label}”类型无效：{node.Kind}";
                    return false;
                }
                if (string.Equals(node.ResourceKind, "vision", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"节点“{node.Label}”当前版本不支持视觉资源。";
                    return false;
                }
                if (!IsFinite(node.X) || !IsFinite(node.Y)
                    || Math.Abs(node.X) > 100000 || Math.Abs(node.Y) > 100000)
                {
                    error = $"节点“{node.Label}”画布坐标无效。";
                    return false;
                }
                if (node.StateBindings == null || node.StateBindings.Count > MaxBindingsPerNode)
                {
                    error = $"节点“{node.Label}”状态绑定集合无效或超过 {MaxBindingsPerNode} 条。";
                    return false;
                }
                if (node.Skills == null || node.Skills.Count > MaxSkillsPerNode)
                {
                    error = $"节点“{node.Label}”技能绑定集合无效或超过 {MaxSkillsPerNode} 条。";
                    return false;
                }
                if (!ReviewStates.Contains(node.ReviewState ?? string.Empty)
                    || !IsFinite(node.Confidence)
                    || node.Confidence < 0 || node.Confidence > 1
                    || node.Evidence == null || node.Evidence.Count > MaxEvidencePerRelation)
                {
                    error = $"节点“{node.Label}”审核状态、置信度或证据集合无效。";
                    return false;
                }
                if (!TryValidateBindings(node, out error))
                {
                    return false;
                }
                if (!TryValidateSkills(node, out error))
                {
                    return false;
                }
            }

            var relationIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < definition.Relations.Count; i++)
            {
                EquipmentTopologyRelation relation = definition.Relations[i];
                if (relation == null)
                {
                    error = $"第 {i + 1} 条关系为空。";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(relation.Id) || !relationIds.Add(relation.Id))
                {
                    error = $"第 {i + 1} 条关系标识为空或重复。";
                    return false;
                }
                if (!nodeIds.Contains(relation.SourceNodeId ?? string.Empty)
                    || !nodeIds.Contains(relation.TargetNodeId ?? string.Empty))
                {
                    error = $"关系“{relation.Id}”引用了不存在的节点。";
                    return false;
                }
                if (string.Equals(relation.SourceNodeId, relation.TargetNodeId, StringComparison.Ordinal))
                {
                    error = $"关系“{relation.Id}”不能连接节点自身。";
                    return false;
                }
                if (!RelationLayers.Contains(relation.Layer ?? string.Empty)
                    || !RelationKinds.Contains(relation.Kind ?? string.Empty)
                    || !ReviewStates.Contains(relation.ReviewState ?? string.Empty))
                {
                    error = $"关系“{relation.Id}”的层、类型或审核状态无效。";
                    return false;
                }
                if (!IsFinite(relation.Confidence)
                    || relation.Confidence < 0 || relation.Confidence > 1)
                {
                    error = $"关系“{relation.Id}”置信度必须在 0 到 1 之间。";
                    return false;
                }
                if (relation.Evidence == null || relation.Evidence.Count > MaxEvidencePerRelation)
                {
                    error = $"关系“{relation.Id}”证据集合无效或超过 {MaxEvidencePerRelation} 条。";
                    return false;
                }
            }
            foreach (EquipmentTopologyRelation relation in definition.Relations)
            {
                if (!string.IsNullOrWhiteSpace(relation.ConflictsWithId)
                    && (!relationIds.Contains(relation.ConflictsWithId)
                        || string.Equals(relation.Id, relation.ConflictsWithId, StringComparison.Ordinal)))
                {
                    error = $"关系“{relation.Id}”的冲突引用不存在或指向自身。";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 只有仍处于候选态、且每条证据都带有确定性规则来源的对象，才允许规则重扫接管其生命周期。
        /// 空证据、混合来源和缺少来源引用都按人工或未知来源保守保留。
        /// </summary>
        internal static bool IsRuleManagedCandidate(
            string reviewState,
            IEnumerable<EquipmentTopologyEvidence> evidence)
        {
            if (!string.Equals(reviewState, "candidate", StringComparison.Ordinal)
                || evidence == null)
            {
                return false;
            }

            bool found = false;
            foreach (EquipmentTopologyEvidence item in evidence)
            {
                if (item == null
                    || !string.Equals(item.SourceType, "rule", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(item.SourceRef)
                    || !item.SourceRef.StartsWith("fact-", StringComparison.Ordinal))
                {
                    return false;
                }
                found = true;
            }
            return found;
        }

        private static bool TryValidateBindings(EquipmentTopologyNode node, out string error)
        {
            error = null;
            var bindingIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < node.StateBindings.Count; i++)
            {
                EquipmentTopologyStateBinding binding = node.StateBindings[i];
                if (binding == null
                    || string.IsNullOrWhiteSpace(binding.Id)
                    || !bindingIds.Add(binding.Id))
                {
                    error = $"节点“{node.Label}”第 {i + 1} 条状态绑定为空、无标识或标识重复。";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(binding.StateName)
                    || string.IsNullOrWhiteSpace(binding.ResourceRef)
                    || string.IsNullOrWhiteSpace(binding.ExpectedValue)
                    || string.IsNullOrWhiteSpace(binding.Meaning)
                    || !BindingSourceKinds.Contains(binding.SourceKind ?? string.Empty)
                    || !BindingOperators.Contains(binding.Operator ?? string.Empty))
                {
                    error = $"节点“{node.Label}”状态绑定“{binding.Id}”字段不完整或类型无效。";
                    return false;
                }
                if (string.Equals(binding.SourceKind, "io", StringComparison.Ordinal)
                    && (!IoBindingOperators.Contains(binding.Operator ?? string.Empty)
                        || !TryParseIoBoolean(binding.ExpectedValue, out _)))
                {
                    error = $"节点“{node.Label}”状态绑定“{binding.Id}”的 IO 比较方式或布尔期望值无效。"
                        + "IO 只支持 equals/not_equals/active/inactive，期望值必须是明确的布尔值。";
                    return false;
                }
                if (!ReviewStates.Contains(binding.ReviewState ?? string.Empty)
                    || !IsFinite(binding.Confidence)
                    || binding.Confidence < 0 || binding.Confidence > 1
                    || binding.Evidence == null || binding.Evidence.Count > MaxEvidencePerRelation)
                {
                    error = $"节点“{node.Label}”状态绑定“{binding.Id}”审核状态、置信度或证据集合无效。";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 解析拓扑 IO 状态绑定允许的显式布尔值。未知文字必须失败，不能静默解释为 false。
        /// </summary>
        internal static bool TryParseIoBoolean(string value, out bool result)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "1", StringComparison.Ordinal)
                || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "active", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "高", StringComparison.Ordinal)
                || string.Equals(normalized, "开", StringComparison.Ordinal))
            {
                result = true;
                return true;
            }
            if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "0", StringComparison.Ordinal)
                || string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "inactive", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "低", StringComparison.Ordinal)
                || string.Equals(normalized, "关", StringComparison.Ordinal))
            {
                result = false;
                return true;
            }
            result = false;
            return false;
        }

        private static bool TryValidateSkills(EquipmentTopologyNode node, out string error)
        {
            error = null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < node.Skills.Count; index++)
            {
                EquipmentTopologySkillBinding skill = node.Skills[index];
                if (skill == null || string.IsNullOrWhiteSpace(skill.Id) || !ids.Add(skill.Id))
                {
                    error = $"节点“{node.Label}”第 {index + 1} 条技能为空、无标识或标识重复。";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(skill.Name)
                    || !string.Equals(skill.ActionKind, "process_operation", StringComparison.Ordinal)
                    || !Guid.TryParse(skill.ProcessId, out Guid processId) || processId == Guid.Empty
                    || !Guid.TryParse(skill.OperationId, out Guid operationId) || operationId == Guid.Empty
                    || !Automation.Protocol.MachineExecutionModes.IsSupported(skill.ExecutionMode)
                    || string.IsNullOrWhiteSpace(skill.Objective)
                    || string.IsNullOrWhiteSpace(skill.ExpectedOutcome)
                    || skill.Preconditions == null || skill.Preconditions.Count > 30
                    || skill.Preconditions.Any(string.IsNullOrWhiteSpace))
                {
                    error = $"节点“{node.Label}”技能“{skill.Id}”的流程指令绑定、模式或动作语义不完整。";
                    return false;
                }
                if (!ReviewStates.Contains(skill.ReviewState ?? string.Empty)
                    || !IsFinite(skill.Confidence)
                    || skill.Confidence < 0 || skill.Confidence > 1
                    || skill.Evidence == null || skill.Evidence.Count > MaxEvidencePerRelation)
                {
                    error = $"节点“{node.Label}”技能“{skill.Id}”审核状态、置信度或证据集合无效。";
                    return false;
                }
            }
            return true;
        }

        private static EquipmentTopologyDefinition CreateEmpty()
        {
            return new EquipmentTopologyDefinition
            {
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        private static HashSet<string> NewSet(params string[] values)
        {
            return new HashSet<string>(values ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
