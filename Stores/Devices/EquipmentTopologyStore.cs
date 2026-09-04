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
            "io", "variable", "axis", "vision", "runtime");
        private static readonly HashSet<string> BindingOperators = NewSet(
            "equals", "not_equals", "greater_than", "less_than", "active", "inactive");

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
