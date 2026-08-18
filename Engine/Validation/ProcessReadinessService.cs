using System;
// 模块：引擎 / 校验。
// 职责范围：分别执行配置可保存性与流程可运行性检查。
// 排查入口：启动被拒绝时以 RunBlockers 为准；Warnings 只表示可保存但仍需完善的事实。

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Automation.Protocol;
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
            ProcessDefinitionValidationContext validationContext = null,
            ValueConfigStore valueStore = null)
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

            foreach (PauseValueParam pause in proc.head?.PauseValueParams ?? new CustomList<PauseValueParam>())
            {
                string pauseError = "变量名称为空。";
                if (pause == null || !TryResolveVariable(
                    pause.ValueName, null, proc.head.Id, validationContext, valueStore, out _, out pauseError))
                {
                    incomplete = true;
                    blockers.Add("流程暂停变量不可用：" + (pauseError ?? "变量名称为空。"));
                }
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
                        string reason = GetPlaceholderReason(operation);
                        warnings.Add($"步骤 {stepIndex} 指令 {operationIndex} [{operation.Name}] 是待完善占位：{reason}");
                        blockers.Add($"步骤 {stepIndex} 指令 {operationIndex} 仍是配置占位。");
                    }
                    if (!step.Disable && !operation.Disable)
                    {
                        string location = $"步骤 {stepIndex} 指令 {operationIndex} [{operation.Name}]";
                        if (AddIncompleteOperationBlockers(operation, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddCommunicationRetryBlockers(
                            proc.head.Id, operation, validationContext, valueStore,
                            location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddCycleTimeProbeBlockers(
                            proc.head.Id, operation, validationContext, valueStore, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddVariableReferenceBlockers(
                            proc.head.Id, operation, validationContext, valueStore, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddContinuousVariableBlockers(
                            proc.head.Id, operation, validationContext, valueStore, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddModifyValueBlockers(operation, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddProcessReferenceBlockers(
                            proc, operation, allProcesses, validationContext, valueStore,
                            location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddAlarmReferenceBlockers(
                            operation, validationContext, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddMotionPointBlockers(
                            operation, validationContext, location, blockers))
                        {
                            incomplete = true;
                        }
                        if (AddDataStructReferenceBlockers(
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

            AddActuatorMotionTimingWarnings(proc, warnings);

            if (enabledOperationCount == 0)
            {
                blockers.Add("流程没有启用的可执行指令。");
            }
            IReadOnlyList<string> gotoErrors = ProcessDefinitionService.ValidateProcGotoTargets(procIndex, proc);
            if (gotoErrors.Count > 0) invalid = true;
            blockers.AddRange(gotoErrors);
            if (gotoErrors.Count == 0
                && allProcesses != null
                && procIndex >= 0
                && procIndex < allProcesses.Count)
            {
                IReadOnlyList<Proc> graphProcesses = allProcesses as IReadOnlyList<Proc>
                    ?? allProcesses.ToList();
                ProcessFlowGraphSnapshot graph = ProcessFlowGraphService.BuildProcess(
                    graphProcesses, procIndex);
                foreach (FlowGraphDiagnostic diagnostic in graph.Diagnostics.Where(item =>
                    string.Equals(item.Code, "UNREACHABLE_OPERATION", StringComparison.Ordinal)))
                {
                    incomplete = true;
                    string message = "控制流存在入口不可达指令：" + diagnostic.Message;
                    warnings.Add(message);
                    blockers.Add(message);
                }
            }
            return Build(warnings, blockers, invalid ? "invalid" : incomplete ? "incomplete" : "ready");
        }

        // 机构激活态与轴运动的相邻关系只做警告不下发阻断：夹爪/真空夹持随行移动是合法工艺，
        // 推料/顶升类机构不复位就移动则可能撞击；机械上无法单侧判定时把时序决策交给模型与用户确认。
        // 按步骤与指令的线性顺序近似执行序，分支和跳转下的精确顺序不在此推导。
        private static void AddActuatorMotionTimingWarnings(Proc proc, ICollection<string> warnings)
        {
            var activeOutputs = new Dictionary<string, string>(StringComparer.Ordinal);
            var warnedOutputs = new HashSet<string>(StringComparer.Ordinal);
            for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                if (step == null || step.Disable || step.Ops == null) continue;
                for (int operationIndex = 0; operationIndex < step.Ops.Count; operationIndex++)
                {
                    OperationType operation = step.Ops[operationIndex];
                    if (operation == null || operation.Disable) continue;
                    string location = $"步骤 {stepIndex} 指令 {operationIndex} [{operation.Name}]";
                    foreach (IoOutParam output in EnumerateOutputWrites(operation))
                    {
                        if (string.IsNullOrWhiteSpace(output.IoName)) continue;
                        if (output.TargetState)
                        {
                            activeOutputs[output.IoName] = location;
                            warnedOutputs.Remove(output.IoName);
                        }
                        else
                        {
                            activeOutputs.Remove(output.IoName);
                        }
                    }
                    if (!IsAxisMotionOperation(operation)) continue;
                    foreach (KeyValuePair<string, string> active in activeOutputs)
                    {
                        if (!warnedOutputs.Add(active.Key)) continue;
                        warnings.Add(
                            $"{location} 在输出IO [{active.Key}] 保持激活（{active.Value} 置 true）时执行轴运动：" +
                            "若该机构需复位后才能移动（防撞击时序），在运动前写回 false 或等待复位反馈；" +
                            "若为夹持随行移动则属正常取放，在答复中标注该假设即可。");
                    }
                }
            }
        }

        private static IEnumerable<IoOutParam> EnumerateOutputWrites(OperationType operation)
        {
            if (operation is IoOperate ioOperate && ioOperate.IoParams != null)
            {
                foreach (IoOutParam item in ioOperate.IoParams)
                {
                    yield return item;
                }
            }
            else if (operation is IoGroup ioGroup && ioGroup.OutIoParams != null)
            {
                foreach (IoOutParam item in ioGroup.OutIoParams)
                {
                    yield return item;
                }
            }
        }

        private static bool IsAxisMotionOperation(OperationType operation)
        {
            switch (operation.OperaType)
            {
                case "工站走点":
                case "走料盘点":
                case "偏移量":
                case "回原":
                    return true;
                default:
                    return false;
            }
        }

        private static bool AddVariableReferenceBlockers(
            Guid procId,
            OperationType operation,
            ProcessDefinitionValidationContext validationContext,
            ValueConfigStore valueStore,
            string location,
            ICollection<string> blockers)
        {
            bool incomplete = false;
            foreach (VariableReferenceRecord reference in VariableReferenceCatalog.Enumerate(operation))
            {
                string name = reference.Kind == VariableReferenceKind.Name ? reference.Value : null;
                int? index = null;
                if (reference.Kind == VariableReferenceKind.Index)
                {
                    if (!int.TryParse(reference.Value,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int parsedIndex))
                    {
                        blockers.Add($"{location} 的 {reference.Path} 变量索引无效：{reference.Value}。");
                        incomplete = true;
                        continue;
                    }
                    index = parsedIndex;
                }
                if (TryResolveVariable(
                    name, index, procId, validationContext, valueStore,
                    out DicValue resolvedVariable, out string error))
                {
                    if (reference.IsIndirect)
                    {
                        if (!int.TryParse(
                            resolvedVariable.Value,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out int targetIndex))
                        {
                            blockers.Add(
                                $"{location} 的 {reference.Path} 二级索引当前值无效：{resolvedVariable.Value}。");
                            incomplete = true;
                        }
                        else if (!TryResolveVariable(
                            null, targetIndex, procId, validationContext, valueStore,
                            out _, out string targetError))
                        {
                            blockers.Add(
                                $"{location} 的 {reference.Path} 二级索引目标{targetError}");
                            incomplete = true;
                        }
                    }
                    continue;
                }
                blockers.Add($"{location} 的 {reference.Path} {error}");
                incomplete = true;
            }
            return incomplete;
        }

        private static bool AddMotionPointBlockers(
            OperationType operation,
            ProcessDefinitionValidationContext validationContext,
            string location,
            ICollection<string> blockers)
        {
            List<DataStation> stations = validationContext?.Runtime?.Stores.Stations.Items;
            if (stations == null || operation == null) return false;

            string stationName = null;
            if (operation is StationRunPos runPos) stationName = runPos.StationName;
            else if (operation is CreateTray tray) stationName = tray.StationName;
            else if (operation is ModifyStationPos modify) stationName = modify.StationName;
            else if (operation is GetStationPos getPos) stationName = getPos.StationName;
            else return false;

            DataStation station = stations.FirstOrDefault(item => item != null
                && string.Equals(item.Name, stationName, StringComparison.Ordinal));
            if (station == null)
            {
                // 工站不能由流程创建，名称未命中即转写错误；附相近工站名帮助一轮修正。
                string[] similar = stations
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                    .Select(item => item.Name)
                    .Where(name => !string.IsNullOrEmpty(stationName)
                        && (name.IndexOf(stationName, StringComparison.OrdinalIgnoreCase) >= 0
                            || stationName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(3)
                    .ToArray();
                blockers.Add(similar.Length > 0
                    ? $"{location} 引用的工站不存在：{stationName ?? string.Empty}。相近工站：{string.Join("、", similar)}。"
                    : $"{location} 引用的工站不存在：{stationName ?? string.Empty}。");
                return true;
            }

            bool incomplete = false;
            DataPos FindPoint(string name) => station.ListDataPos?.FirstOrDefault(item =>
                item != null && string.Equals(item.Name, name, StringComparison.Ordinal));
            void RequirePoint(string name, string field, bool requireTaught)
            {
                DataPos point = FindPoint(name);
                if (point == null)
                {
                    blockers.Add($"{location} 的{field}不存在：{name ?? string.Empty}。");
                    incomplete = true;
                    return;
                }
                if (requireTaught && point.IsTaught == false)
                {
                    blockers.Add($"{location} 的{field}尚未人工示教坐标：{point.Name}。");
                    incomplete = true;
                }
            }

            if (operation is StationRunPos stationRunPos)
            {
                DataPos point = stationRunPos.PosIndex >= 0
                    && station.ListDataPos != null
                    && stationRunPos.PosIndex < station.ListDataPos.Count
                        ? station.ListDataPos[stationRunPos.PosIndex]
                        : FindPoint(stationRunPos.PosName);
                if (point == null || string.IsNullOrWhiteSpace(point.Name))
                {
                    blockers.Add($"{location} 的运动目标点位不存在：{stationRunPos.PosName ?? string.Empty}。");
                    incomplete = true;
                }
                else if (point.IsTaught == false)
                {
                    blockers.Add($"{location} 的运动目标点位尚未人工示教坐标：{point.Name}。");
                    incomplete = true;
                }
            }
            else if (operation is CreateTray createTray)
            {
                RequirePoint(createTray.PX1, "左上参考点", true);
                RequirePoint(createTray.PX2, "右上参考点", true);
                RequirePoint(createTray.PY1, "左下参考点", true);
                RequirePoint(createTray.PY2, "右下参考点", true);
            }
            else if (operation is ModifyStationPos modifyStationPos)
            {
                if (!string.Equals(modifyStationPos.RefPosName, "当前位置", StringComparison.Ordinal)
                    && !string.Equals(modifyStationPos.RefPosName, "自定义坐标", StringComparison.Ordinal))
                {
                    RequirePoint(modifyStationPos.RefPosName, "参考点位", true);
                }
                // 覆盖可把待示教目标写成真实坐标；叠加必须先有可信基准坐标。
                RequirePoint(modifyStationPos.TargetPosName, "目标点位",
                    !string.Equals(modifyStationPos.ModifyType, "替换", StringComparison.Ordinal));
            }
            else if (operation is GetStationPos getStationPos)
            {
                if (string.Equals(getStationPos.SourceType, "指定点位", StringComparison.Ordinal))
                {
                    RequirePoint(getStationPos.SourcePosName, "来源点位", true);
                }
                if (string.Equals(getStationPos.SaveType, "保存到点位", StringComparison.Ordinal))
                {
                    // 目标槽位只需已规划；本指令会从真实来源写入坐标并转为已示教。
                    RequirePoint(getStationPos.TargetPosName, "保存目标点位", false);
                }
            }
            return incomplete;
        }

        // 数据结构不能由流程变更集创建：结构体名称未命中属于悬空引用，
        // 在预演 runBlockers 与启动闸门拦截并附相近名称；名称为空且索引有效时按索引模式跳过。
        private static bool AddDataStructReferenceBlockers(
            OperationType operation,
            ProcessDefinitionValidationContext validationContext,
            string location,
            ICollection<string> blockers)
        {
            if (operation == null) return false;
            List<string> structNames = validationContext?.Runtime?.Stores.DataStructures?.GetStructNames();
            if (structNames == null || structNames.Count == 0) return false;

            bool incomplete = false;
            void RequireStruct(string name, int index, string field)
            {
                if (index >= 0 || string.IsNullOrWhiteSpace(name)) return;
                if (structNames.Contains(name, StringComparer.Ordinal)) return;
                string[] similar = structNames
                    .Where(item => !string.IsNullOrWhiteSpace(item)
                        && (item.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(3)
                    .ToArray();
                blockers.Add(similar.Length > 0
                    ? $"{location} 引用的{field}不存在：{name}。相近结构体：{string.Join("、", similar)}。"
                    : $"{location} 引用的{field}不存在：{name}。");
                incomplete = true;
            }

            switch (operation)
            {
                case SetDataStructItem setStruct:
                    RequireStruct(setStruct.StructName, setStruct.StructIndex, "结构体");
                    break;
                case GetDataStructItem getStruct:
                    RequireStruct(getStruct.StructName, getStruct.StructIndex, "结构体");
                    break;
                case CopyDataStructItem copyStruct:
                    RequireStruct(copyStruct.SourceStructName, copyStruct.SourceStructIndex, "源结构体");
                    RequireStruct(copyStruct.TargetStructName, copyStruct.TargetStructIndex, "目标结构体");
                    break;
                case InsertDataStructItem insertStruct:
                    RequireStruct(insertStruct.TargetStructName, insertStruct.TargetStructIndex, "目标结构体");
                    break;
                case DelDataStructItem delStruct:
                    RequireStruct(delStruct.TargetStructName, delStruct.TargetStructIndex, "目标结构体");
                    break;
                case FindDataStructItem findStruct:
                    RequireStruct(findStruct.TargetStructName, findStruct.TargetStructIndex, "目标结构体");
                    break;
                case GetDataStructCount countStruct:
                    RequireStruct(countStruct.TargetStructName, countStruct.TargetStructIndex, "目标结构体");
                    break;
            }
            return incomplete;
        }

        private static bool AddCommunicationRetryBlockers(
            Guid procId,
            OperationType operation,
            ProcessDefinitionValidationContext validationContext,
            ValueConfigStore valueStore,
            string location,
            ICollection<string> blockers)
        {
            if (!(operation is CommunicationOperationType communication))
            {
                return false;
            }
            if (communication.RetryCount < 0 || communication.RetryCount > 10
                || communication.RetryIntervalMs < 0 || communication.RetryIntervalMs > 60000)
            {
                blockers.Add($"{location} 的通信重试次数或重试间隔越界。");
                return true;
            }
            if (communication.RetryCount == 0
                || !(communication is ResponseCommunicationOperationType response)
                || !response.ShouldEvaluateResponseConditions)
            {
                return false;
            }

            bool incomplete = false;
            if (response.ResponseConditions != null && response.ResponseConditions.Count > 20)
            {
                blockers.Add($"{location} 的通信结果判定条件不能超过20条。");
                incomplete = true;
            }
            int conditionIndex = 0;
            foreach (CommunicationResponseCondition condition in response.ResponseConditions
                ?? new CustomList<CommunicationResponseCondition>())
            {
                conditionIndex++;
                string conditionLocation = $"{location} 的结果判定{conditionIndex}";
                string error = null;
                if (condition == null
                    || !TryResolveVariable(condition.SourceVariableName, null, procId,
                        validationContext, valueStore, out DicValue variable, out error))
                {
                    blockers.Add($"{conditionLocation}来源变量{error ?? "为空"}");
                    incomplete = true;
                    continue;
                }
                if (!IsCommunicationResultVariable(
                    operation, variable, procId, validationContext, valueStore))
                {
                    blockers.Add($"{conditionLocation}来源变量不是本指令的接收或PLC读取结果：{condition.SourceVariableName}。");
                    incomplete = true;
                }
                bool numeric = condition.JudgeMode == "值在区间左"
                    || condition.JudgeMode == "值在区间右"
                    || condition.JudgeMode == "值在区间内";
                bool text = condition.JudgeMode == "等于特征字符"
                    || condition.JudgeMode == "包含特征字符";
                bool supported = condition.JudgeMode == "非空"
                    || condition.JudgeMode == "字段存在" || numeric || text;
                if (!supported)
                {
                    blockers.Add($"{conditionLocation}判断模式无效：{condition.JudgeMode}。");
                    incomplete = true;
                }
                if (!string.IsNullOrWhiteSpace(condition.JsonFieldPath)
                    && !string.Equals(variable.Type, "string",
                        StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add($"{conditionLocation}使用JSON字段路径时来源变量必须是string。");
                    incomplete = true;
                }
                if (condition.JudgeMode == "字段存在"
                    && string.IsNullOrWhiteSpace(condition.JsonFieldPath))
                {
                    blockers.Add($"{conditionLocation}使用字段存在时必须配置JSON字段路径。");
                    incomplete = true;
                }
                if (numeric && string.IsNullOrWhiteSpace(condition.JsonFieldPath)
                    && !string.Equals(variable.Type, "double",
                        StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add($"{conditionLocation}进行数值判断时来源变量必须是double，或配置string JSON字段路径。");
                    incomplete = true;
                }
                if (numeric
                    && (double.IsNaN(condition.Down) || double.IsInfinity(condition.Down)
                        || condition.JudgeMode == "值在区间内"
                        && (double.IsNaN(condition.Up) || double.IsInfinity(condition.Up)
                            || condition.Up < condition.Down)))
                {
                    blockers.Add($"{conditionLocation}数值区间上下限无效。");
                    incomplete = true;
                }
                if (text && condition.ExpectedText == null)
                {
                    blockers.Add($"{conditionLocation}缺少期望文本。");
                    incomplete = true;
                }
                if (conditionIndex > 1
                    && condition.Operator != "且" && condition.Operator != "或")
                {
                    blockers.Add($"{conditionLocation}运算符只能是且或或。");
                    incomplete = true;
                }
            }
            return incomplete;
        }

        private static bool IsCommunicationResultVariable(
            OperationType operation,
            DicValue variable,
            Guid procId,
            ProcessDefinitionValidationContext validationContext,
            ValueConfigStore valueStore)
        {
            if (variable == null) return false;
            if (operation is ReceiveTcpMsg tcp)
                return string.Equals(variable.Name, tcp.MsgSaveValue, StringComparison.Ordinal);
            if (operation is ReceiveSerialPortMsg serial)
                return string.Equals(variable.Name, serial.MsgSaveValue, StringComparison.Ordinal);
            if (operation is SendReceiveCommMsg request)
                return string.Equals(variable.Name, request.ReceiveSaveValue, StringComparison.Ordinal);
            if (!(operation is PlcReadWrite plc) || plc.Action != PlcAccessAction.Read)
                return false;
            if (plc.Mode == PlcAccessMode.Items)
            {
                return (plc.ReadItems ?? new CustomList<PlcReadItem>()).Any(item =>
                    item != null && string.Equals(item.VariableName, variable.Name, StringComparison.Ordinal));
            }
            string firstName = plc.ReadBatch?.FirstVariableName;
            int count = plc.ReadBatch?.ElementCount ?? 0;
            return count > 0
                && TryResolveVariable(firstName, null, procId, validationContext, valueStore,
                    out DicValue first, out _)
                && variable.Index >= first.Index
                && variable.Index < first.Index + count;
        }

        private static bool AddCycleTimeProbeBlockers(
            Guid procId,
            OperationType operation,
            ProcessDefinitionValidationContext validationContext,
            ValueConfigStore valueStore,
            string location,
            ICollection<string> blockers)
        {
            if (!(operation is CycleTimeProbe probe)) return false;
            bool incomplete = false;
            foreach (KeyValuePair<string, string> result in new[]
            {
                new KeyValuePair<string, string>("分段耗时变量", probe.SegmentSecondsVariableName),
                new KeyValuePair<string, string>("累计耗时变量", probe.CycleSecondsVariableName)
            })
            {
                if (string.IsNullOrWhiteSpace(result.Value)) continue;
                if (!TryResolveVariable(result.Value, null, procId, validationContext, valueStore,
                    out DicValue variable, out string error))
                {
                    blockers.Add($"{location} 的{result.Key}{error}");
                    incomplete = true;
                }
                else if (!string.Equals(variable.Type, "double", StringComparison.Ordinal))
                {
                    blockers.Add($"{location} 的{result.Key}必须是double：{result.Value}。");
                    incomplete = true;
                }
            }
            return incomplete;
        }

        private static bool AddContinuousVariableBlockers(
            Guid procId,
            OperationType operation,
            ProcessDefinitionValidationContext validationContext,
            ValueConfigStore valueStore,
            string location,
            ICollection<string> blockers)
        {
            if (!(operation is PlcReadWrite plc)) return false;
            string firstName = null;
            int count = 0;
            if (plc.Action == PlcAccessAction.Read && plc.Mode == PlcAccessMode.ContinuousBatch)
            {
                firstName = plc.ReadBatch?.FirstVariableName;
                count = plc.ReadBatch?.ElementCount ?? 0;
            }
            else if (plc.Action == PlcAccessAction.Write
                && plc.Mode == PlcAccessMode.ContinuousBatch
                && plc.WriteBatch?.Source == PlcValueSource.Variable)
            {
                firstName = plc.WriteBatch.FirstVariableName;
                count = plc.WriteBatch.ElementCount;
            }
            if (string.IsNullOrWhiteSpace(firstName) || count < 2
                || !TryResolveVariable(
                    firstName, null, procId, validationContext, valueStore,
                    out DicValue firstVariable, out _))
            {
                return false;
            }
            bool incomplete = false;
            for (int offset = 1; offset < count; offset++)
            {
                int index = firstVariable.Index + offset;
                if (TryResolveVariable(
                    null, index, procId, validationContext, valueStore, out _, out string error))
                {
                    continue;
                }
                blockers.Add($"{location} 从变量[{firstName}]开始的第{offset + 1}个连续变量{error}");
                incomplete = true;
            }
            return incomplete;
        }

        private static bool TryResolveVariable(
            string name,
            int? index,
            Guid procId,
            ProcessDefinitionValidationContext validationContext,
            ValueConfigStore valueStore,
            out DicValue value,
            out string error)
        {
            value = null;
            error = null;
            bool exists;
            bool accessible;
            if (validationContext != null)
            {
                if (index.HasValue)
                {
                    value = validationContext.VariableDefinitions.Values.FirstOrDefault(item =>
                        item != null && item.Index == index.Value);
                    exists = value != null;
                    accessible = exists && ValueConfigStore.CanProcessAccess(value, procId);
                }
                else
                {
                    exists = !string.IsNullOrWhiteSpace(name)
                        && validationContext.VariableDefinitions.TryGetValue(name, out value);
                    accessible = exists && ValueConfigStore.CanProcessAccess(value, procId);
                }
            }
            else if (index.HasValue)
            {
                ValueConfigStore store = valueStore;
                exists = store != null
                    && store.TryGetValueByIndex(index.Value, out value);
                accessible = exists && ValueConfigStore.CanProcessAccess(value, procId);
            }
            else
            {
                ValueConfigStore store = valueStore;
                exists = !string.IsNullOrWhiteSpace(name)
                    && store != null
                    && store.TryGetValueByName(name, out value);
                accessible = exists && ValueConfigStore.CanProcessAccess(value, procId);
            }
            string target = index.HasValue ? "索引" + index.Value : name ?? string.Empty;
            if (!exists)
            {
                // 与工站/数据结构 blocker 一致：变量未命中时附相近候选，模型一轮纠正。
                IEnumerable<string> knownNames = validationContext != null
                    ? validationContext.VariableDefinitions.Keys
                    : valueStore?.BuildSaveData()?.Keys;
                var ranked = AiOperationCompileContext.RankNameCandidates(name ?? string.Empty, knownNames);
                error = ranked.Count > 0
                    ? $"引用的变量不存在：{target}。相近变量：{string.Join("、", ranked.Select(item => item.Name))}。"
                    : $"引用的变量不存在：{target}。";
                return false;
            }
            if (!accessible)
            {
                error = $"引用了其他流程的私有变量：{target}。";
                return false;
            }
            return true;
        }

        public static bool IsPlaceholder(OperationType operation)
        {
            return operation is ConfigurationPlaceholder
                || operation != null
                    && !string.IsNullOrWhiteSpace(operation.Note)
                    && operation.Note.StartsWith(PlaceholderNotePrefix, StringComparison.Ordinal);
        }

        public static string GetPlaceholderReason(OperationType operation)
        {
            if (operation is ConfigurationPlaceholder placeholder)
                return string.IsNullOrWhiteSpace(placeholder.Reason) ? "未说明原因" : placeholder.Reason.Trim();
            if (!IsPlaceholder(operation)) return string.Empty;
            return operation.Note.Substring(PlaceholderNotePrefix.Length).Trim();
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
                    bool configured = value != null;
                    if (value is string text) configured = !string.IsNullOrWhiteSpace(text);
                    else if (value is System.Collections.ICollection collection) configured = collection.Count > 0;
                    if (configured) continue;
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
            if (modify.ClearOutput)
            {
                if (!string.Equals(modify.ModifyType, "替换", StringComparison.Ordinal)
                    || hasLiteral || hasReference || modify.NegateSource || modify.NegateOperand)
                {
                    blockers.Add($"{location} 的清空变量只能使用替换模式，且不得配置修改值或取反。" );
                    incomplete = true;
                }
            }
            else if (hasLiteral == hasReference)
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
            if (!modify.ClearOutput && numericMode && hasLiteral
                && !double.TryParse(modify.ChangeValue,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out number)
                && !double.TryParse(modify.ChangeValue, out number))
            {
                blockers.Add($"{location} 的固定修改值不是有效数字。");
                incomplete = true;
            }
            else if (!modify.ClearOutput && (modify.ModifyType == "除法" || modify.ModifyType == "求余")
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
            if (OperationBehaviorCatalog.IsFieldRequired(operation, nameof(OperationType.AlarmInfoId))
                && !string.IsNullOrWhiteSpace(operation.AlarmInfoId))
            {
                references.Add(new KeyValuePair<string, string>(
                    nameof(OperationType.AlarmInfoId), operation.AlarmInfoId));
            }
            if (operation is PopupDialog popup
                && OperationBehaviorCatalog.IsFieldRequired(operation, nameof(PopupDialog.PopupAlarmInfoId))
                && !string.IsNullOrWhiteSpace(popup.PopupAlarmInfoId))
            {
                references.Add(new KeyValuePair<string, string>(
                    nameof(PopupDialog.PopupAlarmInfoId), popup.PopupAlarmInfoId));
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
                    exists = validationContext?.Runtime?.Stores.Alarms != null
                        && validationContext.Runtime.Stores.Alarms.TryGetByIndex(alarmIndex, out AlarmInfo alarm)
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
                bool browsable = property.GetCustomAttribute<System.ComponentModel.BrowsableAttribute>()?.Browsable ?? true;
                if (obj is IPropertyVisibilityProvider visibilityProvider
                    && !visibilityProvider.IsPropertyVisible(property.Name, browsable))
                {
                    continue;
                }
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
            ProcessDefinitionValidationContext validationContext,
            ValueConfigStore valueStore,
            string location, List<string> blockers)
        {
            var processesByName = (allProcesses ?? Array.Empty<Proc>())
                .Where(item => item?.head != null && !string.IsNullOrWhiteSpace(item.head.Name))
                .GroupBy(item => item.head.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            bool incomplete = false;
            if (operation is ProcOps controls)
            {
                foreach (ProcParam item in controls.Params ?? new CustomList<ProcParam>())
                {
                    if (item == null || (item.TargetState != "运行" && item.TargetState != "停止"))
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
                        && string.Equals(item.TargetState, "运行", StringComparison.Ordinal))
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
                        && string.Equals(item.TargetState, "运行", StringComparison.Ordinal))
                    {
                        blockers.Add($"{location} 不能启动当前流程自身。");
                        incomplete = true;
                    }
                }
            }
            else if (operation is WaitProc waits)
            {
                var modes = new HashSet<string>(StringComparer.Ordinal)
                {
                    WaitProc.WaitReadyMode,
                    WaitProc.StateJumpMode,
                    WaitProc.GetStateMode
                };
                if (!modes.Contains(waits.WorkMode ?? string.Empty))
                {
                    blockers.Add($"{location} 的工作模式无效：{waits.WorkMode ?? "空"}。");
                    incomplete = true;
                }
                if (waits.WorkMode == WaitProc.WaitReadyMode)
                {
                    if ((waits.Params?.Count ?? 0) == 0)
                    {
                        blockers.Add($"{location} 尚未配置等待的目标流程。");
                        incomplete = true;
                    }
                    if (waits.Timeout == null
                        || waits.Timeout.TimeoutMs <= 0
                            && string.IsNullOrWhiteSpace(waits.Timeout.TimeoutVariableName))
                    {
                        blockers.Add($"{location} 尚未配置有效等待超时。");
                        incomplete = true;
                    }
                    foreach (WaitProcParam item in waits.Params ?? new CustomList<WaitProcParam>())
                    {
                        if (item == null
                            || item.TargetState != "运行" && item.TargetState != "就绪")
                        {
                            blockers.Add($"{location} 的等待状态只允许“运行”或“就绪”。");
                            incomplete = true;
                        }
                        if (string.IsNullOrWhiteSpace(item?.ProcName))
                        {
                            if (string.IsNullOrWhiteSpace(item?.ProcValue))
                            {
                                blockers.Add($"{location} 尚未配置等待的目标流程。");
                                incomplete = true;
                            }
                            else if (TryResolveVariable(
                                item.ProcValue, null, current?.head?.Id ?? Guid.Empty,
                                validationContext, valueStore, out DicValue processNameVariable, out _)
                                && !string.Equals(
                                    processNameVariable.Type, "string", StringComparison.OrdinalIgnoreCase))
                            {
                                blockers.Add($"{location} 的流程变量必须是string：{item.ProcValue}。");
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
                else if (waits.WorkMode == WaitProc.StateJumpMode
                    || waits.WorkMode == WaitProc.GetStateMode)
                {
                    if (string.IsNullOrWhiteSpace(waits.TargetProcName))
                    {
                        if (string.IsNullOrWhiteSpace(waits.TargetProcValue))
                        {
                            blockers.Add($"{location} 尚未配置目标流程。");
                            incomplete = true;
                        }
                        else if (TryResolveVariable(
                            waits.TargetProcValue, null, current?.head?.Id ?? Guid.Empty,
                            validationContext, valueStore, out DicValue processNameVariable, out _)
                            && !string.Equals(
                                processNameVariable.Type, "string", StringComparison.OrdinalIgnoreCase))
                        {
                            blockers.Add($"{location} 的目标流程变量必须是string：{waits.TargetProcValue}。");
                            incomplete = true;
                        }
                    }
                    else if (allProcesses != null && !processesByName.ContainsKey(waits.TargetProcName))
                    {
                        blockers.Add($"{location} 的目标流程不存在：{waits.TargetProcName}。");
                        incomplete = true;
                    }
                    if (waits.WorkMode == WaitProc.GetStateMode)
                    {
                        if (string.IsNullOrWhiteSpace(waits.StateVariableName))
                        {
                            blockers.Add($"{location} 尚未配置状态变量。");
                            incomplete = true;
                        }
                        else if (TryResolveVariable(
                            waits.StateVariableName, null, current?.head?.Id ?? Guid.Empty,
                            validationContext, valueStore, out DicValue stateVariable, out _)
                            && !string.Equals(stateVariable.Type, "double", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(stateVariable.Type, "string", StringComparison.OrdinalIgnoreCase))
                        {
                            blockers.Add($"{location} 的状态变量必须是double或string：{waits.StateVariableName}。");
                            incomplete = true;
                        }
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
