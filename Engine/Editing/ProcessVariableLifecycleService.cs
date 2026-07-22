using System;
// 模块：引擎 / 结构编辑。
// 职责范围：执行流程与指令结构变换、跳转重写、发布门禁和变量生命周期处理。

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Automation.Protocol;
using static Automation.OperationTypePartial;

namespace Automation
{
    public sealed class VariableCopyMapping
    {
        public Guid SourceVariableId { get; set; }
        public Guid VariableId { get; set; }
        public string SourceName { get; set; }
        public string Name { get; set; }
        public int SourceIndex { get; set; }
        public int Index { get; set; }
    }

    public sealed class ProcessVariableCopyResult
    {
        public List<VariableCopyMapping> Mappings { get; } = new List<VariableCopyMapping>();
        public List<string> Warnings { get; } = new List<string>();
        public bool HasUnresolvedReferences => Warnings.Count > 0;
    }

    /// <summary>
    /// 维护流程生命周期与私有变量的同阶段配置语义。
    /// </summary>
    public static class ProcessVariableLifecycleService
    {
        private const int MaximumVariableNameLength = 64;

        public static int RemoveOwnedVariables(
            IDictionary<string, DicValue> variables, IEnumerable<Guid> ownerProcIds)
        {
            if (variables == null) return 0;
            var owners = new HashSet<Guid>((ownerProcIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty));
            List<string> names = variables.Values
                .Where(value => value != null
                    && ValueConfigStore.IsProcessValue(value)
                    && value.OwnerProcId.HasValue
                    && owners.Contains(value.OwnerProcId.Value))
                .Select(value => value.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
            foreach (string name in names) variables.Remove(name);
            return names.Count;
        }

        public static int ConvertOwnedVariablesToPublic(
            IDictionary<string, DicValue> variables, IEnumerable<Guid> ownerProcIds)
        {
            if (variables == null) return 0;
            var owners = new HashSet<Guid>((ownerProcIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty));
            List<DicValue> ownedVariables = variables.Values
                .Where(value => value != null
                    && ValueConfigStore.IsProcessValue(value)
                    && value.OwnerProcId.HasValue
                    && owners.Contains(value.OwnerProcId.Value))
                .ToList();
            foreach (DicValue variable in ownedVariables)
            {
                variable.Scope = VariableScopeContract.Public;
                variable.OwnerProcId = null;
            }
            return ownedVariables.Count;
        }

        public static ProcessVariableCopyResult CopyPrivateVariables(
            Guid sourceProcId,
            Guid targetProcId,
            Proc copiedProcess,
            IDictionary<string, DicValue> variables)
        {
            if (sourceProcId == Guid.Empty || targetProcId == Guid.Empty)
                throw new InvalidOperationException("复制流程的源或目标稳定ID无效。");
            if (copiedProcess == null || variables == null)
                throw new InvalidOperationException("复制流程或变量配置为空。");

            var result = new ProcessVariableCopyResult();
            List<DicValue> sourceVariables = variables.Values
                .Where(value => value != null
                    && ValueConfigStore.IsProcessValue(value)
                    && value.OwnerProcId == sourceProcId)
                .OrderBy(value => value.Index)
                .ToList();
            var usedNames = new HashSet<string>(
                variables.Keys.Where(name => !string.IsNullOrEmpty(name)), StringComparer.Ordinal);
            var usedIndexes = new HashSet<int>(variables.Values
                .Where(value => value != null).Select(value => value.Index));
            var nameMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var indexMap = new Dictionary<int, int>();
            var copiedBySourceId = new Dictionary<Guid, DicValue>();

            foreach (DicValue source in sourceVariables)
            {
                int index = FindFirstFreeNormalIndex(usedIndexes);
                string name = BuildCopyName(source.Name, usedNames);
                DicValue copy = ObjectGraphCloner.Clone(source);
                copy.Id = Guid.NewGuid();
                copy.Name = name;
                copy.Index = index;
                copy.Scope = VariableScopeContract.Process;
                copy.OwnerProcId = targetProcId;
                copy.LastChangedAt = default(DateTime);
                copy.LastChangedBy = string.Empty;
                copy.LastChangedOldValue = string.Empty;
                copy.LastChangedNewValue = string.Empty;
                variables.Add(name, copy);
                usedNames.Add(name);
                usedIndexes.Add(index);
                nameMap[source.Name] = name;
                indexMap[source.Index] = index;
                copiedBySourceId[source.Id] = copy;
                result.Mappings.Add(new VariableCopyMapping
                {
                    SourceVariableId = source.Id,
                    VariableId = copy.Id,
                    SourceName = source.Name,
                    Name = name,
                    SourceIndex = source.Index,
                    Index = index
                });
            }

            RewritePauseVariables(copiedProcess, nameMap);
            foreach (Step step in copiedProcess.steps ?? new List<Step>())
            {
                foreach (OperationType operation in step?.Ops ?? new List<OperationType>())
                {
                    foreach (VariableReferenceRecord reference in VariableReferenceCatalog.Enumerate(operation))
                    {
                        if (IsContinuousReference(reference))
                        {
                            bool pointsToCopiedVariable = reference.Kind == VariableReferenceKind.Name
                                ? nameMap.ContainsKey(reference.Value)
                                : int.TryParse(reference.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int continuousIndex) && indexMap.ContainsKey(continuousIndex);
                            if (pointsToCopiedVariable)
                            {
                                result.Warnings.Add($"指令[{operation.Name}]的连续变量引用[{reference.Path}]保留原文本，副本流程当前不可运行。");
                            }
                            continue;
                        }

                        string replacement = null;
                        DicValue indirectSource = null;
                        if (reference.Kind == VariableReferenceKind.Name
                            && nameMap.TryGetValue(reference.Value, out string mappedName))
                        {
                            replacement = mappedName;
                            DicValue source = sourceVariables.FirstOrDefault(value =>
                                string.Equals(value.Name, reference.Value, StringComparison.Ordinal));
                            if (source != null) copiedBySourceId.TryGetValue(source.Id, out indirectSource);
                        }
                        else if (reference.Kind == VariableReferenceKind.Index
                            && int.TryParse(reference.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int oldIndex)
                            && indexMap.TryGetValue(oldIndex, out int mappedIndex))
                        {
                            replacement = mappedIndex.ToString(CultureInfo.InvariantCulture);
                            DicValue source = sourceVariables.FirstOrDefault(value => value.Index == oldIndex);
                            if (source != null) copiedBySourceId.TryGetValue(source.Id, out indirectSource);
                        }
                        if (replacement == null)
                        {
                            if (reference.IsIndirect)
                            {
                                result.Warnings.Add(
                                    $"指令[{operation.Name}]的动态变量引用[{reference.Path}]保留原文本；副本会按其当前目标重新检查作用域。");
                            }
                            continue;
                        }

                        if (!reference.TrySetValue(replacement))
                        {
                            result.Warnings.Add($"指令[{operation.Name}]的变量引用[{reference.Path}]无法自动改写，副本流程当前不可运行。");
                            continue;
                        }
                        if (reference.IsIndirect && indirectSource != null)
                        {
                            bool hasTargetIndex = int.TryParse(
                                indirectSource.Value, NumberStyles.None,
                                CultureInfo.InvariantCulture, out int targetIndex);
                            if (hasTargetIndex
                                && indexMap.TryGetValue(targetIndex, out int copiedTargetIndex))
                            {
                                indirectSource.Value = copiedTargetIndex.ToString(CultureInfo.InvariantCulture);
                            }
                            else if (!hasTargetIndex || !variables.Values.Any(value =>
                                value != null && value.Index == targetIndex
                                    && !ValueConfigStore.IsProcessValue(value)))
                            {
                                result.Warnings.Add(
                                    $"指令[{operation.Name}]的动态变量引用[{reference.Path}]目标无法可靠转换，副本流程当前不可运行。");
                            }
                        }
                    }
                }
            }
            return result;
        }

        private static void RewritePauseVariables(Proc process, IReadOnlyDictionary<string, string> nameMap)
        {
            foreach (PauseValueParam pause in process?.head?.PauseValueParams
                ?? new CustomList<PauseValueParam>())
            {
                if (pause != null && nameMap.TryGetValue(pause.ValueName ?? string.Empty, out string name))
                {
                    pause.ValueName = name;
                }
            }
        }

        private static bool IsContinuousReference(VariableReferenceRecord reference)
        {
            string path = reference?.Path ?? string.Empty;
            return path.IndexOf("FirstVariable", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("FirstResultVariable", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int FindFirstFreeNormalIndex(ISet<int> usedIndexes)
        {
            for (int index = 0; index < ValueConfigStore.NormalValueCapacity; index++)
            {
                if (!usedIndexes.Contains(index)) return index;
            }
            throw new InvalidOperationException("普通变量区已满，无法复制流程私有变量。");
        }

        private static string BuildCopyName(string sourceName, ISet<string> usedNames)
        {
            string source = string.IsNullOrWhiteSpace(sourceName) ? "变量" : sourceName.Trim();
            for (int number = 1; ; number++)
            {
                string suffix = number == 1 ? "_副本" : "_副本" + number.ToString(CultureInfo.InvariantCulture);
                int baseLength = Math.Max(1, MaximumVariableNameLength - suffix.Length);
                string basis = source.Length > baseLength ? source.Substring(0, baseLength) : source;
                string candidate = basis + suffix;
                if (!usedNames.Contains(candidate)) return candidate;
            }
        }
    }
}
