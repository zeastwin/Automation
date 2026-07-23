// 模块：编辑器 / 变量。
// 职责范围：变量配置、运行值调试、变量选择和提交规则。

using System;
using System.Collections.Generic;
using System.Linq;
using Automation.Protocol;

namespace Automation
{
    /// <summary>
    /// 变量编辑页的应用服务。窗体只负责采集输入、确认危险改动和刷新显示，
    /// 变量校验、作用域投影、引用统计及配置提交统一在此完成。
    /// </summary>
    internal sealed class VariableEditorService
    {
        public const string AllScopes = "all";

        private readonly PlatformRuntime runtime;
        private readonly Func<IEnumerable<Proc>> processProvider;

        public VariableEditorService(PlatformRuntime runtime, Func<IEnumerable<Proc>> processProvider)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
        }

        public VariableScopeCatalog BuildScopeCatalog()
        {
            List<Proc> processes = (processProvider() ?? Enumerable.Empty<Proc>())
                .Where(proc => proc?.head != null && proc.head.Id != Guid.Empty)
                .OrderBy(proc => proc.head.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var names = processes.ToDictionary(
                proc => proc.head.Id,
                proc => proc.head.Name ?? proc.head.Id.ToString("D"));
            var options = new List<VariableScopeOption>
            {
                new VariableScopeOption
                {
                    Key = VariableScopeContract.Public,
                    Text = "公共变量",
                    Scope = VariableScopeContract.Public
                }
            };
            options.AddRange(processes.Select(proc => new VariableScopeOption
            {
                Key = BuildScopeOptionKey(VariableScopeContract.Process, proc.head.Id),
                Text = proc.head.Name ?? proc.head.Id.ToString("D"),
                Scope = VariableScopeContract.Process,
                OwnerProcId = proc.head.Id
            }));
            return new VariableScopeCatalog(names, options);
        }

        public bool ShouldDisplay(
            int slotIndex,
            DicValue variable,
            string selectedScope,
            Guid? selectedOwnerProcId,
            string searchText,
            IReadOnlyDictionary<Guid, string> processNames)
        {
            bool searching = !string.IsNullOrEmpty(searchText);
            bool allScopesSelected = string.Equals(selectedScope, AllScopes, StringComparison.Ordinal);
            if (variable == null)
            {
                if (searching) return false;
                if (allScopesSelected) return true;
                if (string.Equals(selectedScope, VariableScopeContract.Process, StringComparison.Ordinal))
                {
                    return false;
                }
                bool systemScopeSelected = string.Equals(
                    selectedScope, VariableScopeContract.System, StringComparison.Ordinal);
                return systemScopeSelected == ValueConfigStore.IsSystemValueIndex(slotIndex);
            }
            if (searching)
            {
                string ownerName = ResolveOwnerProcessName(variable.OwnerProcId, processNames);
                return variable.Index.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                    || (variable.Name ?? string.Empty).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                    || (variable.Type ?? string.Empty).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                    || (variable.Note ?? string.Empty).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                    || ownerName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                    || (variable.Scope ?? string.Empty).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return allScopesSelected
                || string.Equals(selectedScope, VariableScopeContract.System, StringComparison.Ordinal)
                && ValueConfigStore.IsSystemValueIndex(slotIndex)
                || string.Equals(variable.Scope, selectedScope, StringComparison.Ordinal)
                && (!string.Equals(selectedScope, VariableScopeContract.Process, StringComparison.Ordinal)
                    || variable.OwnerProcId == selectedOwnerProcId);
        }

        public string FormatScope(DicValue value, IReadOnlyDictionary<Guid, string> processNames)
        {
            if (value == null) return "未配置";
            if (string.Equals(value.Scope, VariableScopeContract.Process, StringComparison.Ordinal))
            {
                string ownerName = ResolveOwnerProcessName(value.OwnerProcId, processNames);
                return string.IsNullOrWhiteSpace(ownerName) ? "所属流程缺失" : ownerName;
            }
            return string.Equals(value.Scope, VariableScopeContract.System, StringComparison.Ordinal)
                ? "系统"
                : "公共";
        }

        public bool ValidateClipboardData(VariableClipboardData data, out string error)
        {
            error = null;
            if (data == null || string.IsNullOrEmpty(data.Name))
            {
                error = "名称为空";
                return false;
            }
            if (data.Type != "double" && data.Type != "string")
            {
                error = "类型无效";
                return false;
            }
            if (string.IsNullOrEmpty(data.Value))
            {
                error = "值为空";
                return false;
            }
            if (data.Type == "double" && !double.TryParse(data.Value, out _))
            {
                error = "值不是有效数字";
                return false;
            }
            return true;
        }

        public string BuildAvailableCopyName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) return null;
            string name = baseName.Trim();
            for (int suffix = 1; suffix < 100000; suffix++)
            {
                string candidate = $"{name}{suffix}";
                if (!runtime.Stores.Values.TryGetValueByName(candidate, out _)) return candidate;
            }
            return null;
        }

        public int CountUsages(DicValue variable)
        {
            if (variable == null) return 0;
            return (processProvider() ?? Enumerable.Empty<Proc>())
                .Where(proc => proc?.steps != null)
                .SelectMany(proc => proc.steps)
                .Where(step => step?.Ops != null)
                .SelectMany(step => step.Ops)
                .Sum(operation => VariableReferenceCatalog.Enumerate(operation).Count(reference =>
                    reference.Kind == VariableReferenceKind.Name
                        ? string.Equals(reference.Value, variable.Name, StringComparison.Ordinal)
                        : int.TryParse(reference.Value, out int index) && index == variable.Index));
        }

        public bool TrySetRuntimeValue(DicValue variable, string value, out string error)
        {
            error = null;
            if (variable == null)
            {
                error = "变量不存在。";
                return false;
            }
            if (runtime.Stores.Values.setValueByIndex(variable.Index, value, "变量页设置当前值")) return true;
            error = $"变量[{variable.Name}]当前值无效。";
            return false;
        }

        public bool TryCommitRow(VariableRowUpdate update, out string error)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            error = null;
            Dictionary<string, DicValue> draft = runtime.Stores.Values.BuildSaveData();
            DicValue current = draft.Values.FirstOrDefault(value => value?.Index == update.Index);
            if (draft.TryGetValue(update.Name, out DicValue sameName) && sameName.Index != update.Index)
            {
                error = $"变量名已存在：{update.Name}";
                return false;
            }
            if (current != null) draft.Remove(current.Name);
            DicValue updated = current == null ? new DicValue() : ObjectGraphCloner.Clone(current);
            if (updated.Id == Guid.Empty) updated.Id = Guid.NewGuid();
            updated.Index = update.Index;
            updated.Name = update.Name;
            updated.Type = update.Type;
            updated.Scope = ValueConfigStore.IsSystemValueIndex(update.Index)
                ? VariableScopeContract.System
                : update.Scope;
            updated.OwnerProcId = ValueConfigStore.IsSystemValueIndex(update.Index) ? null : update.OwnerProcId;
            if (current == null || update.SetCurrentValue) updated.Value = update.CurrentValue;
            updated.Note = update.Note;
            draft[update.Name] = updated;
            return runtime.Stores.Values.TryCommitConfiguration(
                runtime.Paths.ConfigPath,
                draft,
                out error,
                update.SetCurrentValue
                    ? new Dictionary<string, string>(StringComparer.Ordinal) { [update.Name] = update.CurrentValue }
                    : null,
                update.SetCurrentValue ? "变量页保存当前值" : null,
                update.HistoryDescription);
        }

        public bool TryClearVariables(IEnumerable<DicValue> variables, out string error)
        {
            Dictionary<string, DicValue> draft = runtime.Stores.Values.BuildSaveData();
            foreach (DicValue variable in variables ?? Enumerable.Empty<DicValue>())
            {
                if (variable != null) draft.Remove(variable.Name);
            }
            return runtime.Stores.Values.TryCommitConfiguration(
                runtime.Paths.ConfigPath, draft, out error, historyDescription: "清空变量");
        }

        public static string BuildScopeOptionKey(string scope, Guid? ownerProcId)
        {
            return string.Equals(scope, VariableScopeContract.Process, StringComparison.Ordinal)
                ? $"{VariableScopeContract.Process}:{ownerProcId?.ToString("D") ?? string.Empty}"
                : VariableScopeContract.Public;
        }

        private static string ResolveOwnerProcessName(
            Guid? ownerProcId, IReadOnlyDictionary<Guid, string> processNames)
        {
            return ownerProcId.HasValue
                && processNames != null
                && processNames.TryGetValue(ownerProcId.Value, out string processName)
                    ? processName
                    : string.Empty;
        }
    }

    internal sealed class VariableScopeCatalog
    {
        public VariableScopeCatalog(
            IReadOnlyDictionary<Guid, string> processNames,
            IReadOnlyList<VariableScopeOption> options)
        {
            ProcessNames = processNames;
            Options = options;
        }

        public IReadOnlyDictionary<Guid, string> ProcessNames { get; }
        public IReadOnlyList<VariableScopeOption> Options { get; }
    }

    internal sealed class VariableScopeOption
    {
        public string Key { get; set; }
        public string Text { get; set; }
        public string Scope { get; set; }
        public Guid? OwnerProcId { get; set; }
    }

    internal sealed class VariableClipboardData
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string Note { get; set; }
    }

    internal sealed class VariableRowUpdate
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string CurrentValue { get; set; }
        public string Note { get; set; }
        public string Scope { get; set; }
        public Guid? OwnerProcId { get; set; }
        public bool SetCurrentValue { get; set; }
        public string HistoryDescription { get; set; }
    }
}
