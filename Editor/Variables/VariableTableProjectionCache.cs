// 模块：编辑器 / 变量。
// 职责范围：把固定变量槽位预计算为可直接切换的作用域索引投影。

using System;
using System.Collections.Generic;
using Automation.Protocol;

namespace Automation
{
    /// <summary>
    /// 变量表只持有一套虚拟行。切换作用域时替换槽位索引投影，
    /// 避免对固定容量的 DataGridView 行逐个修改 Visible。
    /// </summary>
    internal sealed class VariableTableProjectionCache
    {
        private static readonly int[] EmptyProjection = new int[0];

        private readonly int[] allSlots;
        private readonly int[] publicSlots;
        private readonly int[] systemSlots;
        private readonly Dictionary<Guid, int[]> processSlots;

        private VariableTableProjectionCache(
            int[] allSlots,
            int[] publicSlots,
            int[] systemSlots,
            Dictionary<Guid, int[]> processSlots)
        {
            this.allSlots = allSlots;
            this.publicSlots = publicSlots;
            this.systemSlots = systemSlots;
            this.processSlots = processSlots;
        }

        public static VariableTableProjectionCache Build(IReadOnlyList<DicValue> variables)
        {
            var all = new List<int>(ValueConfigStore.ValueCapacity);
            var publicValues = new List<int>(VariableIndexContract.NormalValueCapacity);
            var systemValues = new List<int>(VariableIndexContract.SystemValueCapacity);
            var processValues = new Dictionary<Guid, List<int>>();

            for (int slotIndex = 0; slotIndex < ValueConfigStore.ValueCapacity; slotIndex++)
            {
                all.Add(slotIndex);
                DicValue variable = variables != null && slotIndex < variables.Count
                    ? variables[slotIndex]
                    : null;
                bool systemSlot = ValueConfigStore.IsSystemValueIndex(slotIndex);
                if (variable == null)
                {
                    if (systemSlot)
                    {
                        systemValues.Add(slotIndex);
                    }
                    else
                    {
                        publicValues.Add(slotIndex);
                    }
                    continue;
                }

                if (systemSlot
                    || string.Equals(variable.Scope, VariableScopeContract.System, StringComparison.Ordinal))
                {
                    systemValues.Add(slotIndex);
                }
                if (string.Equals(variable.Scope, VariableScopeContract.Public, StringComparison.Ordinal))
                {
                    publicValues.Add(slotIndex);
                }
                if (string.Equals(variable.Scope, VariableScopeContract.Process, StringComparison.Ordinal)
                    && variable.OwnerProcId.HasValue)
                {
                    if (!processValues.TryGetValue(variable.OwnerProcId.Value, out List<int> ownedSlots))
                    {
                        ownedSlots = new List<int>();
                        processValues.Add(variable.OwnerProcId.Value, ownedSlots);
                    }
                    ownedSlots.Add(slotIndex);
                }
            }

            var frozenProcessValues = new Dictionary<Guid, int[]>();
            foreach (KeyValuePair<Guid, List<int>> pair in processValues)
            {
                frozenProcessValues.Add(pair.Key, pair.Value.ToArray());
            }
            return new VariableTableProjectionCache(
                all.ToArray(),
                publicValues.ToArray(),
                systemValues.ToArray(),
                frozenProcessValues);
        }

        public IReadOnlyList<int> GetSlots(string scope, Guid? ownerProcId)
        {
            if (string.Equals(scope, VariableEditorService.AllScopes, StringComparison.Ordinal))
            {
                return allSlots;
            }
            if (string.Equals(scope, VariableScopeContract.Public, StringComparison.Ordinal))
            {
                return publicSlots;
            }
            if (string.Equals(scope, VariableScopeContract.System, StringComparison.Ordinal))
            {
                return systemSlots;
            }
            if (string.Equals(scope, VariableScopeContract.Process, StringComparison.Ordinal)
                && ownerProcId.HasValue
                && processSlots.TryGetValue(ownerProcId.Value, out int[] ownedSlots))
            {
                return ownedSlots;
            }
            return EmptyProjection;
        }

        public static IReadOnlyList<int> Search(
            IReadOnlyList<DicValue> variables,
            string searchText,
            IReadOnlyDictionary<Guid, string> processNames)
        {
            if (string.IsNullOrEmpty(searchText))
            {
                return EmptyProjection;
            }

            var result = new List<int>();
            for (int slotIndex = 0; slotIndex < ValueConfigStore.ValueCapacity; slotIndex++)
            {
                DicValue variable = variables != null && slotIndex < variables.Count
                    ? variables[slotIndex]
                    : null;
                if (variable == null)
                {
                    continue;
                }
                string ownerName = variable.OwnerProcId.HasValue
                    && processNames != null
                    && processNames.TryGetValue(variable.OwnerProcId.Value, out string processName)
                        ? processName ?? string.Empty
                        : string.Empty;
                if (Contains(variable.Index.ToString(), searchText)
                    || Contains(variable.Name, searchText)
                    || Contains(variable.Type, searchText)
                    || Contains(variable.Note, searchText)
                    || Contains(ownerName, searchText)
                    || Contains(variable.Scope, searchText))
                {
                    result.Add(slotIndex);
                }
            }
            return result;
        }

        private static bool Contains(string source, string searchText)
        {
            return (source ?? string.Empty).IndexOf(
                searchText,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
