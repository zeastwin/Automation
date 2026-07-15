using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using static Automation.OperationTypePartial;

namespace Automation
{
    public sealed class ProcessReadinessAnalysis
    {
        public string ReadinessStatus { get; internal set; }

        public bool Runnable { get; internal set; }

        public IReadOnlyList<string> Warnings { get; internal set; } = Array.Empty<string>();

        public IReadOnlyList<string> RunBlockers { get; internal set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 区分“配置可保存”和“流程可运行”。空流程、空步骤和占位指令允许保存，启动时统一拦截。
    /// </summary>
    public static class ProcessReadinessService
    {
        public const string PlaceholderNotePrefix = "EW-AI:CONFIG_PLACEHOLDER:";

        public static ProcessReadinessAnalysis Analyze(
            int procIndex, Proc proc, IList<Proc> allProcesses = null,
            ProcessDefinitionValidationContext validationContext = null)
        {
            var warnings = new List<string>();
            var blockers = new List<string>();
            if (proc == null)
            {
                blockers.Add("流程对象为空。");
                return Build(warnings, blockers, "invalid");
            }

            bool incomplete = false;
            bool invalid = proc.head == null || proc.head.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(proc.head.Name);
            if (invalid) blockers.Add("流程头信息、名称或稳定ID无效。");

            if (proc.head?.Disable == true)
            {
                blockers.Add("流程已禁用。");
            }

            if (proc.steps == null || proc.steps.Count == 0)
            {
                warnings.Add("流程尚未添加步骤。");
                blockers.Add("流程没有可执行步骤。");
                return Build(warnings, blockers, invalid ? "invalid" : "incomplete");
            }

            int enabledOperationCount = 0;
            for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                if (step == null)
                {
                    invalid = true;
                    blockers.Add($"步骤 {stepIndex} 为空。");
                    continue;
                }

                if (step.Ops == null)
                {
                    invalid = true;
                    blockers.Add($"步骤 {stepIndex} [{step.Name}] 指令列表缺失。");
                    continue;
                }
                List<OperationType> operations = step.Ops;
                if (operations.Count == 0)
                {
                    incomplete = true;
                    warnings.Add($"步骤 {stepIndex} [{step.Name}] 尚未添加指令。");
                    if (!step.Disable)
                    {
                        blockers.Add($"启用步骤 {stepIndex} [{step.Name}] 没有可执行指令。");
                    }
                    continue;
                }

                for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
                {
                    OperationType operation = operations[operationIndex];
                    if (operation == null)
                    {
                        invalid = true;
                        blockers.Add($"步骤 {stepIndex} 指令 {operationIndex} 为空。");
                        continue;
                    }
                    if (!step.Disable && !operation.Disable)
                    {
                        enabledOperationCount++;
                    }
                    if (IsPlaceholder(operation))
                    {
                        incomplete = true;
                        string reason = operation.Note.Substring(PlaceholderNotePrefix.Length).Trim();
                        warnings.Add($"步骤 {stepIndex} 指令 {operationIndex} [{operation.Name}] 是待完善占位：{reason}");
                        if (!step.Disable && !operation.Disable)
                        {
                            blockers.Add($"步骤 {stepIndex} 指令 {operationIndex} 仍是配置占位。");
                        }
                    }
                    if (!step.Disable && !operation.Disable)
                    {
                        string location = $"步骤 {stepIndex} 指令 {operationIndex} [{operation.Name}]";
                        if (AddIncompleteOperationBlockers(operation, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddModifyValueBlockers(operation, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddProcessReferenceBlockers(
                            proc, operation, allProcesses, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddAlarmReferenceBlockers(
                            operation, validationContext, location, blockers))
                        {
                            incomplete = true;
                        }
                        IReadOnlyList<string> runtimeErrors =
                            ProcessDefinitionService.ValidateOperationRuntimeConfiguration(
                                operation, location, validationContext);
                        if (runtimeErrors.Count > 0)
                        {
                            blockers.AddRange(runtimeErrors);
                            incomplete = true;
                        }
                    }
                }
            }

            if (enabledOperationCount == 0)
            {
                blockers.Add("流程没有启用的可执行指令。");
            }
            IReadOnlyList<string> gotoErrors = ProcessDefinitionService.ValidateProcGotoTargets(procIndex, proc);
            if (gotoErrors.Count > 0) invalid = true;
            blockers.AddRange(gotoErrors);
            return Build(warnings, blockers, invalid ? "invalid" : incomplete ? "incomplete" : "ready");
        }

        public static bool IsPlaceholder(OperationType operation)
        {
            return operation != null
                && !string.IsNullOrWhiteSpace(operation.Note)
                && operation.Note.StartsWith(PlaceholderNotePrefix, StringComparison.Ordinal);
        }

        private static bool AddIncompleteOperationBlockers(
            OperationType operation, string location, List<string> blockers)
        {
            bool incomplete = false;
            JObject contract = OperationBehaviorCatalog.BuildContract(operation);
            if (contract?["fieldRules"] is JObject rules)
            {
                foreach (JProperty rule in rules.Properties())
                {
                    if (!OperationBehaviorCatalog.IsFieldRequired(operation, rule.Name)) continue;
                    PropertyInfo property = operation.GetType().GetProperty(rule.Name);
                    object value = property?.GetValue(operation);
                    if (value != null && !string.IsNullOrWhiteSpace(value.ToString())) continue;
                    blockers.Add($"{location} 的运行必填字段 {rule.Name} 尚未配置。");
                    incomplete = true;
                }
            }

            int pendingGotoCount = CountPendingGotos(operation);
            if (pendingGotoCount > 0)
            {
                blockers.Add($"{location} 还有 {pendingGotoCount} 个跳转目标尚未解析。");
                incomplete = true;
            }

            if (operation is Goto jump)
            {
                if ((jump.Params == null || jump.Params.Count == 0)
                    && string.IsNullOrWhiteSpace(jump.DefaultGoto))
                {
                    blockers.Add($"{location} 尚未配置跳转目标。");
                    incomplete = true;
                }
                if (jump.Params != null && jump.Params.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(jump.ValueIndex)
                        && string.IsNullOrWhiteSpace(jump.ValueName))
                    {
                        blockers.Add($"{location} 尚未配置条件跳转的数据源。");
                        incomplete = true;
                    }
                    for (int index = 0; index < jump.Params.Count; index++)
                    {
                        GotoParam item = jump.Params[index];
                        bool hasLiteral = !string.IsNullOrWhiteSpace(item?.MatchValue);
                        bool hasReference = !string.IsNullOrWhiteSpace(item?.MatchValueIndex)
                            || !string.IsNullOrWhiteSpace(item?.MatchValueV);
                        if (!hasLiteral && !hasReference)
                        {
                            blockers.Add($"{location} 的分支 {index} 尚未配置匹配值。");
                            incomplete = true;
                        }
                        if (string.IsNullOrWhiteSpace(item?.Goto))
                        {
                            blockers.Add($"{location} 的分支 {index} 尚未配置跳转目标。");
                            incomplete = true;
                        }
                    }
                }
            }
            return incomplete;
        }

        private static bool AddModifyValueBlockers(
            OperationType operation, string location, List<string> blockers)
        {
            if (!(operation is ModifyValue modify)) return false;
            bool incomplete = false;
            var modes = new HashSet<string>(StringComparer.Ordinal)
            {
                "替换", "叠加", "乘法", "除法", "求余", "绝对值"
            };
            if (!modes.Contains(modify.ModifyType ?? string.Empty))
            {
                blockers.Add($"{location} 的修改模式无效：{modify.ModifyType ?? "空"}。");
                incomplete = true;
            }
            if (!ValueRef.TryCreate(modify.ValueSourceIndex, modify.ValueSourceIndex2Index,
                modify.ValueSourceName, modify.ValueSourceName2Index, false,
                "源变量", out _, out string sourceError))
            {
                blockers.Add($"{location}：{sourceError}");
                incomplete = true;
            }

            bool hasLiteral = !string.IsNullOrEmpty(modify.ChangeValue);
            bool hasReference = !string.IsNullOrEmpty(modify.ChangeValueIndex)
                || !string.IsNullOrEmpty(modify.ChangeValueIndex2Index)
                || !string.IsNullOrEmpty(modify.ChangeValueName)
                || !string.IsNullOrEmpty(modify.ChangeValueName2Index);
            if (hasLiteral == hasReference)
            {
                blockers.Add(hasLiteral
                    ? $"{location} 的固定修改值与修改值变量不能同时配置。"
                    : $"{location} 尚未配置修改值或修改值变量。");
                incomplete = true;
            }
            else if (hasReference && !ValueRef.TryCreate(
                modify.ChangeValueIndex, modify.ChangeValueIndex2Index,
                modify.ChangeValueName, modify.ChangeValueName2Index, false,
                "修改值", out _, out string changeError))
            {
                blockers.Add($"{location}：{changeError}");
                incomplete = true;
            }

            bool numericMode = modify.ModifyType == "叠加"
                || modify.ModifyType == "乘法"
                || modify.ModifyType == "除法"
                || modify.ModifyType == "求余";
            double number = 0;
            if (numericMode && hasLiteral
                && !double.TryParse(modify.ChangeValue,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out number)
                && !double.TryParse(modify.ChangeValue, out number))
            {
                blockers.Add($"{location} 的固定修改值不是有效数字。");
                incomplete = true;
            }
            else if ((modify.ModifyType == "除法" || modify.ModifyType == "求余")
                && hasLiteral && number == 0d)
            {
                blockers.Add($"{location} 的除数或求余操作数不能为0。");
                incomplete = true;
            }

            if (!ValueRef.TryCreate(modify.OutputValueIndex, modify.OutputValueIndex2Index,
                modify.OutputValueName, modify.OutputValueName2Index, false,
                "结果变量", out _, out string outputError))
            {
                blockers.Add($"{location}：{outputError}");
                incomplete = true;
            }
            return incomplete;
        }

        private static bool AddAlarmReferenceBlockers(
            OperationType operation, ProcessDefinitionValidationContext validationContext,
            string location, List<string> blockers)
        {
            var references = new List<KeyValuePair<string, string>>();
            if (OperationBehaviorCatalog.IsFieldRequired(operation, nameof(OperationType.AlarmInfoID))
                && !string.IsNullOrWhiteSpace(operation.AlarmInfoID))
            {
                references.Add(new KeyValuePair<string, string>(
                    nameof(OperationType.AlarmInfoID), operation.AlarmInfoID));
            }
            if (operation is PopupDialog popup
                && OperationBehaviorCatalog.IsFieldRequired(operation, nameof(PopupDialog.PopupAlarmInfoID))
                && !string.IsNullOrWhiteSpace(popup.PopupAlarmInfoID))
            {
                references.Add(new KeyValuePair<string, string>(
                    nameof(PopupDialog.PopupAlarmInfoID), popup.PopupAlarmInfoID));
            }

            bool incomplete = false;
            foreach (KeyValuePair<string, string> reference in references)
            {
                string alarmInfoId = reference.Value.Trim();
                if (!int.TryParse(alarmInfoId, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int alarmIndex)
                    || alarmIndex < 0
                    || alarmIndex >= AlarmInfoStore.AlarmCapacity)
                {
                    blockers.Add(
                        $"{location} 的 {reference.Key} 必须是 [0, {AlarmInfoStore.AlarmCapacity}) 范围内的报警信息编号。");
                    incomplete = true;
                    continue;
                }

                bool exists;
                if (validationContext?.HasAlarmInfoCatalog == true)
                {
                    exists = validationContext.AlarmInfoIds.Contains(alarmInfoId);
                }
                else
                {
                    exists = SF.alarmInfoStore != null
                        && SF.alarmInfoStore.TryGetByIndex(alarmIndex, out AlarmInfo alarm)
                        && alarm != null
                        && !string.IsNullOrWhiteSpace(alarm.Name);
                }

                if (exists) continue;
                blockers.Add($"{location} 的 {reference.Key} 引用的报警信息尚未配置：{alarmInfoId}。");
                incomplete = true;
            }
            return incomplete;
        }

        private static int CountPendingGotos(object obj)
        {
            if (obj == null) return 0;
            int count = 0;
            foreach (PropertyInfo property in obj.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length > 0) continue;
                object value = property.GetValue(obj);
                if (property.PropertyType == typeof(string)
                    && property.GetCustomAttribute<MarkedGotoAttribute>() != null
                    && value is string text
                    && (text.StartsWith(ProcessDefinitionService.PendingGotoPrefix, StringComparison.Ordinal)
                        || text.StartsWith(ProcessDefinitionService.DeletedGotoPrefix, StringComparison.Ordinal)))
                {
                    count++;
                }
                else if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    foreach (object item in enumerable) count += CountPendingGotos(item);
                }
            }
            return count;
        }

        private static bool AddProcessReferenceBlockers(
            Proc current, OperationType operation, IList<Proc> allProcesses,
            string location, List<string> blockers)
        {
            var processesByName = (allProcesses ?? Array.Empty<Proc>())
                .Where(item => item?.head != null && !string.IsNullOrWhiteSpace(item.head.Name))
                .GroupBy(item => item.head.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            bool incomplete = false;
            if (operation is ProcOps controls)
            {
                foreach (procParam item in controls.procParams ?? new CustomList<procParam>())
                {
                    if (item == null || (item.value != "运行" && item.value != "停止"))
                    {
                        blockers.Add($"{location} 尚未配置流程动作。");
                        incomplete = true;
                    }
                    if (string.IsNullOrWhiteSpace(item?.ProcName))
                    {
                        if (string.IsNullOrWhiteSpace(item?.ProcValue))
                        {
                            blockers.Add($"{location} 尚未配置目标流程字段 ProcName 或 ProcValue；value 仅表示运行/停止动作。");
                            incomplete = true;
                        }
                        continue;
                    }
                    if (!processesByName.TryGetValue(item.ProcName, out Proc targetProcess))
                    {
                        blockers.Add($"{location} 引用的目标流程不存在：{item.ProcName}。");
                        incomplete = true;
                    }
                    else if (targetProcess != null
                        && string.Equals(item.value, "运行", StringComparison.Ordinal))
                    {
                        bool targetHasExecutableOperation = targetProcess.head?.Disable != true
                            && (targetProcess.steps ?? new List<Step>()).Any(step => step != null
                                && !step.Disable
                                && (step.Ops ?? new List<OperationType>()).Any(targetOperation =>
                                    targetOperation != null
                                    && !targetOperation.Disable
                                    && !IsPlaceholder(targetOperation)));
                        if (!targetHasExecutableOperation)
                        {
                            blockers.Add($"{location} 引用的目标流程没有启用的可执行指令：{item.ProcName}。");
                            incomplete = true;
                        }
                    }
                    if (string.Equals(item.ProcName, current?.head?.Name, StringComparison.Ordinal)
                        && string.Equals(item.value, "运行", StringComparison.Ordinal))
                    {
                        blockers.Add($"{location} 不能启动当前流程自身。");
                        incomplete = true;
                    }
                }
            }
            else if (operation is WaitProc waits)
            {
                if (waits.timeOutC == null
                    || waits.timeOutC.TimeOut <= 0
                        && string.IsNullOrWhiteSpace(waits.timeOutC.TimeOutValue))
                {
                    blockers.Add($"{location} 尚未配置有效等待超时。");
                    incomplete = true;
                }
                foreach (WaitProcParam item in waits.Params ?? new CustomList<WaitProcParam>())
                {
                    if (item == null || (item.value != "运行" && item.value != "停止"))
                    {
                        blockers.Add($"{location} 尚未配置等待状态。");
                        incomplete = true;
                    }
                    if (string.IsNullOrWhiteSpace(item?.ProcName))
                    {
                        if (string.IsNullOrWhiteSpace(item?.ProcValue))
                        {
                            blockers.Add($"{location} 尚未配置等待的目标流程。");
                            incomplete = true;
                        }
                        continue;
                    }
                    if (allProcesses != null && !processesByName.ContainsKey(item.ProcName))
                    {
                        blockers.Add($"{location} 等待的目标流程不存在：{item.ProcName}。");
                        incomplete = true;
                    }
                }
            }
            return incomplete;
        }

        private static ProcessReadinessAnalysis Build(
            List<string> warnings, List<string> blockers, string readinessStatus)
        {
            string[] distinctWarnings = warnings.Distinct(StringComparer.Ordinal).ToArray();
            string[] distinctBlockers = blockers.Distinct(StringComparer.Ordinal).ToArray();
            return new ProcessReadinessAnalysis
            {
                ReadinessStatus = readinessStatus,
                Runnable = distinctBlockers.Length == 0,
                Warnings = distinctWarnings,
                RunBlockers = distinctBlockers
            };
        }
    }
}
