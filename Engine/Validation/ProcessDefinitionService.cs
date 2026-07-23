using System;
// 模块：引擎 / 校验。
// 职责范围：分别执行配置可保存性与流程可运行性检查。
// 边界说明：本服务判断结构和可保存性；缺少运行资源等启动条件交给 ProcessReadinessService。

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using static Automation.OperationTypePartial;

namespace Automation
{
    public sealed class ProcessDefinitionValidationContext
    {
        public ProcessDefinitionValidationContext(
            IEnumerable<string> variableNames,
            IEnumerable<string> tcpNames,
            IEnumerable<string> serialNames,
            IEnumerable<string> alarmInfoIds = null,
            IEnumerable<string> plcNames = null,
            IEnumerable<KeyValuePair<string, DicValue>> variableDefinitions = null,
            PlatformRuntime runtime = null)
        {
            VariableNames = new HashSet<string>(variableNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            TcpNames = new HashSet<string>(tcpNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            SerialNames = new HashSet<string>(serialNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            AlarmInfoIds = new HashSet<string>(alarmInfoIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            HasAlarmInfoCatalog = alarmInfoIds != null;
            PlcNames = new HashSet<string>(plcNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            HasPlcCatalog = plcNames != null;
            VariableDefinitions = (variableDefinitions ?? Array.Empty<KeyValuePair<string, DicValue>>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value != null)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            Runtime = runtime;
        }

        public IReadOnlyCollection<string> VariableNames { get; }

        public IReadOnlyCollection<string> TcpNames { get; }

        public IReadOnlyCollection<string> SerialNames { get; }

        public IReadOnlyCollection<string> AlarmInfoIds { get; }

        public bool HasAlarmInfoCatalog { get; }

        public IReadOnlyCollection<string> PlcNames { get; }

        public bool HasPlcCatalog { get; }

        public IReadOnlyDictionary<string, DicValue> VariableDefinitions { get; }

        public PlatformRuntime Runtime { get; }

        public bool TryGetVariableForProcess(string name, Guid procId, out DicValue value)
        {
            value = null;
            return !string.IsNullOrWhiteSpace(name)
                && VariableDefinitions.TryGetValue(name, out value)
                && ValueConfigStore.CanProcessAccess(value, procId);
        }

        public bool TryGetVariableForProcess(int index, Guid procId, out DicValue value)
        {
            value = VariableDefinitions.Values.FirstOrDefault(item => item != null && item.Index == index);
            return value != null && ValueConfigStore.CanProcessAccess(value, procId);
        }
    }

    public static class ProcessDefinitionService
    {
        public static JsonSerializerSettings CreateStrictJsonSettings()
        {
            return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = AutomationConfigSerializationBinder.Instance,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                MissingMemberHandling = MissingMemberHandling.Error
            };
        }

        internal const string DeletedGotoPrefix = "#DELETED-GOTO#";
        internal const string PendingGotoPrefix = "#PENDING-GOTO#";

        public static string BuildPendingGoto(string operationMapKey)
        {
            return PendingGotoPrefix + Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(operationMapKey ?? string.Empty));
        }

        public static int ResolvePendingGotoTargets(int procIndex, Proc proc)
        {
            if (proc?.steps == null) return 0;
            var locations = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                if (step?.Ops == null) continue;
                for (int operationIndex = 0; operationIndex < step.Ops.Count; operationIndex++)
                {
                    OperationType operation = step.Ops[operationIndex];
                    if (operation == null || string.IsNullOrWhiteSpace(operation.AiKey)) continue;
                    string address = $"{procIndex}-{stepIndex}-{operationIndex}";
                    if (step.Id != Guid.Empty)
                    {
                        locations[AiOperationCompileContext.BuildOperationKeyForStepId(
                            step.Id, operation.AiKey)] = address;
                    }
                    if (!string.IsNullOrWhiteSpace(step.AiKey))
                    {
                        locations[AiOperationCompileContext.BuildOperationKeyForStepKey(
                            step.AiKey, operation.AiKey)] = address;
                    }
                }
            }

            int resolved = 0;
            foreach (Step step in proc.steps)
            {
                foreach (OperationType operation in step?.Ops ?? Enumerable.Empty<OperationType>())
                {
                    resolved += ResolvePendingGotoTargets(operation, locations);
                }
            }
            return resolved;
        }

        private static int ResolvePendingGotoTargets(
            object obj, IReadOnlyDictionary<string, string> locations)
        {
            if (obj == null) return 0;
            int resolved = 0;
            foreach (PropertyInfo property in obj.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length > 0) continue;
                object value = property.GetValue(obj);
                if (property.PropertyType == typeof(string)
                    && property.CanWrite
                    && property.GetCustomAttribute<MarkedGotoAttribute>() != null
                    && value is string text
                    && TryReadPendingGoto(text, out string mapKey)
                    && locations.TryGetValue(mapKey, out string address))
                {
                    property.SetValue(obj, address);
                    resolved++;
                    continue;
                }
                if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    foreach (object item in enumerable)
                    {
                        resolved += ResolvePendingGotoTargets(item, locations);
                    }
                }
            }
            return resolved;
        }

        internal static bool TryReadPendingGoto(string value, out string operationMapKey)
        {
            operationMapKey = null;
            if (string.IsNullOrWhiteSpace(value)
                || !value.StartsWith(PendingGotoPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            try
            {
                operationMapKey = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(value.Substring(PendingGotoPrefix.Length)));
                return !string.IsNullOrWhiteSpace(operationMapKey);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static void NormalizeProc(int procIndex, Proc proc, List<string> errors,
            ProcessDefinitionValidationContext validationContext = null)
        {
            if (proc == null)
            {
                errors.Add($"流程{procIndex}为空");
                return;
            }
            if (proc.head == null)
            {
                proc.head = new ProcHead();
                errors.Add($"流程{procIndex}头信息缺失");
            }
            if (proc.head.Id == Guid.Empty)
            {
                errors.Add($"流程{procIndex}缺少稳定ID");
            }
            if (string.IsNullOrWhiteSpace(proc.head.Name))
            {
                errors.Add($"流程{procIndex}名称为空");
            }
            if (proc.head.PauseIoParams == null)
            {
                proc.head.PauseIoParams = new CustomList<PauseIoParam>();
            }
            if (proc.head.PauseValueParams == null)
            {
                proc.head.PauseValueParams = new CustomList<PauseValueParam>();
            }
            if (proc.steps == null)
            {
                proc.steps = new List<Step>();
                errors.Add($"流程{procIndex}步骤列表缺失");
            }
            var stepIds = new HashSet<Guid>();
            var operationIds = new HashSet<Guid>();
            for (int i = 0; i < proc.steps.Count; i++)
            {
                if (proc.steps[i] == null)
                {
                    proc.steps[i] = new Step();
                    errors.Add($"流程{procIndex}步骤{i}为空");
                }
                Step step = proc.steps[i];
                if (step.Id == Guid.Empty)
                {
                    errors.Add($"流程{procIndex}步骤{i}缺少稳定ID");
                }
                else if (!stepIds.Add(step.Id))
                {
                    errors.Add($"流程{procIndex}步骤{i}的ID重复：{step.Id:D}");
                }
                if (string.IsNullOrWhiteSpace(step.Name))
                {
                    errors.Add($"流程{procIndex}步骤{i}名称为空");
                }
                if (step.Ops == null)
                {
                    step.Ops = new List<OperationType>();
                    errors.Add($"流程{procIndex}步骤{i}指令列表缺失");
                }
                for (int j = 0; j < step.Ops.Count; j++)
                {
                    if (step.Ops[j] == null)
                    {
                        step.Ops[j] = new OperationType
                        {
                            Name = "空指令",
                            OperaType = "无效指令",
                            Disable = true
                        };
                        errors.Add($"流程{procIndex}步骤{i}指令{j}为空");
                    }
                    if (step.Ops[j].Id == Guid.Empty)
                    {
                        errors.Add($"流程{procIndex}步骤{i}指令{j}缺少稳定ID");
                    }
                    else if (!operationIds.Add(step.Ops[j].Id))
                    {
                        errors.Add($"流程{procIndex}步骤{i}指令{j}的ID重复：{step.Ops[j].Id:D}");
                    }
                    step.Ops[j].Num = j;
                }
            }
            for (int i = 0; i < proc.steps.Count; i++)
            {
                Step step = proc.steps[i];
                for (int j = 0; j < step.Ops.Count; j++)
                {
                    ValidateGotoTargets(step.Ops[j], step.Ops[j], procIndex, proc, errors, $"流程{procIndex}步骤{i}指令{j}");
                }
            }
        }

        public static IReadOnlyList<string> ValidateOperationRuntimeConfiguration(
            OperationType operation, string location,
            ProcessDefinitionValidationContext validationContext = null)
        {
            var errors = new List<string>();
            ValidateCommunicationOperation(operation, errors, location, validationContext);
            ValidatePlcOperation(operation, errors, location, validationContext);
            ValidateDataStructOperation(operation, errors, location, validationContext);
            ValidateCoordinatedStationMotion(operation, errors, location, validationContext);
            ValidateIoOutputOperation(operation, errors, location, validationContext);
            ValidateIoLogicGoto(operation, errors, location, validationContext);
            return errors;
        }

        private static void ValidateDataStructOperation(OperationType operation,
            List<string> errors, string location,
            ProcessDefinitionValidationContext validationContext)
        {
            DataStructStore store = validationContext?.Runtime?.Stores.DataStructures;
            if (operation == null || operation.Disable || store == null)
            {
                return;
            }

            bool ResolveStruct(bool useName, int index, string name,
                string role, out int resolvedIndex)
            {
                if (store.TryResolveStructIndex(
                        useName, index, name, out resolvedIndex, out string error))
                {
                    return true;
                }
                errors.Add($"{location} 的{role}{error}。");
                return false;
            }

            bool ResolveItem(int structIndex, bool useName, int index, string name,
                string role, out int resolvedIndex)
            {
                if (store.TryResolveItemIndex(
                        structIndex, useName, index, name, out resolvedIndex, out string error))
                {
                    return true;
                }
                errors.Add($"{location} 的{role}{error}。");
                return false;
            }

            void ResolveField(int structIndex, int itemIndex, bool useName,
                int index, string name, string role)
            {
                if (!store.TryResolveFieldIndex(
                        structIndex, itemIndex, useName, index, name, out _, out string error))
                {
                    errors.Add($"{location} 的{role}{error}。");
                }
            }

            bool SelectAddressMode(
                string name,
                int index,
                string role,
                out bool useName)
            {
                bool hasName = !string.IsNullOrWhiteSpace(name);
                bool hasIndex = index >= 0;
                useName = hasName;
                if (hasName == hasIndex)
                {
                    errors.Add(hasName
                        ? $"{location} 的{role}名称与索引不能同时配置。"
                        : $"{location} 的{role}尚未配置。");
                    return false;
                }
                return true;
            }

            if (operation is SetDataStructItem set)
            {
                if (!SelectAddressMode(
                        set.StructName,
                        set.StructIndex,
                        "目标结构体",
                        out bool useStructName)
                    || !ResolveStruct(useStructName, set.StructIndex,
                        set.StructName, "目标", out int structIndex)
                    || !SelectAddressMode(
                        set.ItemName,
                        set.ItemIndex,
                        "目标数据项",
                        out bool useItemName)
                    || !ResolveItem(structIndex, useItemName,
                        set.ItemIndex, set.ItemName, "目标", out int itemIndex))
                {
                    return;
                }
                if (set.Params == null || set.Params.Count == 0)
                {
                    errors.Add($"{location} 的数据结构设置参数为空。");
                    return;
                }
                for (int i = 0; i < set.Params.Count; i++)
                {
                    SetDataStructItemParam parameter = set.Params[i];
                    if (parameter == null)
                    {
                        errors.Add($"{location} 的 Params[{i}] 为空。");
                        continue;
                    }
                    if (SelectAddressMode(
                        parameter.FieldName,
                        parameter.FieldIndex,
                        $"Params[{i}] 字段",
                        out bool useFieldName))
                    {
                        ResolveField(
                            structIndex,
                            itemIndex,
                            useFieldName,
                            parameter.FieldIndex,
                            parameter.FieldName,
                            $"Params[{i}] ");
                    }
                    bool hasLiteral = !string.IsNullOrEmpty(parameter.Value);
                    bool hasReference = !string.IsNullOrEmpty(parameter.ValueIndex)
                        || !string.IsNullOrEmpty(parameter.ValueIndex2Index)
                        || !string.IsNullOrEmpty(parameter.ValueName)
                        || !string.IsNullOrEmpty(parameter.ValueName2Index);
                    if (hasLiteral == hasReference)
                    {
                        errors.Add(hasLiteral
                            ? $"{location} 的 Params[{i}] 固定写入值与变量来源不能同时配置。"
                            : $"{location} 的 Params[{i}] 尚未配置写入值。");
                    }
                    else if (hasReference
                        && !ValueRef.TryCreate(
                            parameter.ValueIndex,
                            parameter.ValueIndex2Index,
                            parameter.ValueName,
                            parameter.ValueName2Index,
                            false,
                            $"Params[{i}] 写入值",
                            out _,
                            out string valueError))
                    {
                        errors.Add($"{location}：{valueError}。");
                    }
                }
                return;
            }

            if (operation is GetDataStructItem get)
            {
                if (!SelectAddressMode(
                        get.StructName,
                        get.StructIndex,
                        "目标结构体",
                        out bool useStructName)
                    || !ResolveStruct(useStructName, get.StructIndex,
                        get.StructName, "目标", out int structIndex)
                    || !SelectAddressMode(
                        get.ItemName,
                        get.ItemIndex,
                        "目标数据项",
                        out bool useItemName)
                    || !ResolveItem(structIndex, useItemName,
                        get.ItemIndex, get.ItemName, "目标", out int itemIndex))
                {
                    return;
                }
                if (get.IsAllItem)
                {
                    return;
                }
                if (get.Params == null)
                {
                    errors.Add($"{location} 的数据结构读取参数为空。");
                    return;
                }
                for (int i = 0; i < get.Params.Count; i++)
                {
                    GetDataStructItemParam parameter = get.Params[i];
                    if (parameter == null)
                    {
                        errors.Add($"{location} 的 Params[{i}] 为空。");
                        continue;
                    }
                    if (SelectAddressMode(
                        parameter.FieldName,
                        parameter.FieldIndex,
                        $"Params[{i}] 字段",
                        out bool useFieldName))
                    {
                        ResolveField(structIndex, itemIndex, useFieldName,
                            parameter.FieldIndex, parameter.FieldName, $"Params[{i}] ");
                    }
                }
                return;
            }

            if (operation is CopyDataStructItem copy)
            {
                if (!SelectAddressMode(
                        copy.SourceStructName,
                        copy.SourceStructIndex,
                        "源结构体",
                        out bool useSourceStructName)
                    || !ResolveStruct(useSourceStructName,
                        copy.SourceStructIndex, copy.SourceStructName,
                        "源", out int sourceStructIndex)
                    || !SelectAddressMode(
                        copy.SourceItemName,
                        copy.SourceItemIndex,
                        "源数据项",
                        out bool useSourceItemName)
                    || !ResolveItem(sourceStructIndex, useSourceItemName,
                        copy.SourceItemIndex, copy.SourceItemName,
                        "源", out int sourceItemIndex)
                    || !SelectAddressMode(
                        copy.TargetStructName,
                        copy.TargetStructIndex,
                        "目标结构体",
                        out bool useTargetStructName)
                    || !ResolveStruct(useTargetStructName,
                        copy.TargetStructIndex, copy.TargetStructName,
                        "目标", out int targetStructIndex)
                    || !SelectAddressMode(
                        copy.TargetItemName,
                        copy.TargetItemIndex,
                        "目标数据项",
                        out bool useTargetItemName)
                    || !ResolveItem(targetStructIndex, useTargetItemName,
                        copy.TargetItemIndex, copy.TargetItemName,
                        "目标", out int targetItemIndex))
                {
                    return;
                }
                if (copy.IsAllValue)
                {
                    return;
                }
                if (copy.Params == null)
                {
                    errors.Add($"{location} 的数据结构复制参数为空。");
                    return;
                }
                for (int i = 0; i < copy.Params.Count; i++)
                {
                    CopyDataStructItemParam parameter = copy.Params[i];
                    if (parameter == null)
                    {
                        errors.Add($"{location} 的 Params[{i}] 为空。");
                        continue;
                    }
                    if (SelectAddressMode(
                        parameter.SourceFieldName,
                        parameter.SourceFieldIndex,
                        $"Params[{i}] 源字段",
                        out bool useSourceFieldName))
                    {
                        ResolveField(sourceStructIndex, sourceItemIndex,
                            useSourceFieldName,
                            parameter.SourceFieldIndex, parameter.SourceFieldName,
                            $"Params[{i}] 源");
                    }
                    if (SelectAddressMode(
                        parameter.TargetFieldName,
                        parameter.TargetFieldIndex,
                        $"Params[{i}] 目标字段",
                        out bool useTargetFieldName))
                    {
                        ResolveField(targetStructIndex, targetItemIndex,
                            useTargetFieldName,
                            parameter.TargetFieldIndex, parameter.TargetFieldName,
                            $"Params[{i}] 目标");
                    }
                }
                return;
            }

            if (operation is InsertDataStructItem insert)
            {
                if (!SelectAddressMode(
                        insert.TargetStructName,
                        insert.TargetStructIndex,
                        "目标结构体",
                        out bool useTargetStructName)
                    || !ResolveStruct(useTargetStructName,
                        insert.TargetStructIndex, insert.TargetStructName,
                        "目标", out int structIndex))
                {
                    return;
                }
                int itemCount = store.GetItemCount(structIndex);
                if (insert.TargetItemIndex < 0 || insert.TargetItemIndex > itemCount)
                {
                    errors.Add($"{location} 的插入位置超出范围0..{itemCount}：{insert.TargetItemIndex}。");
                }
                if (string.IsNullOrWhiteSpace(insert.ItemName))
                {
                    errors.Add($"{location} 的新数据项名称不能为空。");
                }
                if (insert.Params == null || insert.Params.Count == 0)
                {
                    errors.Add($"{location} 的数据结构插入参数为空。");
                    return;
                }
                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < insert.Params.Count; i++)
                {
                    InsertDataStructItemParam parameter = insert.Params[i];
                    if (parameter == null)
                    {
                        errors.Add($"{location} 的 Params[{i}] 为空。");
                        continue;
                    }
                    string fieldName = parameter.FieldName?.Trim();
                    if (string.IsNullOrWhiteSpace(fieldName))
                    {
                        errors.Add($"{location} 的 Params[{i}] 字段名称不能为空。");
                    }
                    else if (!fieldNames.Add(fieldName))
                    {
                        errors.Add($"{location} 的 Params[{i}] 字段名称重复：{fieldName}。");
                    }
                    if (!string.Equals(parameter.Type, "double", StringComparison.Ordinal)
                        && !string.Equals(parameter.Type, "string", StringComparison.Ordinal))
                    {
                        errors.Add($"{location} 的 Params[{i}] 数据类型无效：{parameter.Type}。");
                    }
                    bool hasLiteral = parameter.Value != null;
                    bool hasReference = !string.IsNullOrEmpty(parameter.ValueIndex)
                        || !string.IsNullOrEmpty(parameter.ValueIndex2Index)
                        || !string.IsNullOrEmpty(parameter.ValueName)
                        || !string.IsNullOrEmpty(parameter.ValueName2Index);
                    if (hasLiteral == hasReference)
                    {
                        errors.Add(hasLiteral
                            ? $"{location} 的 Params[{i}] 固定数据值与变量来源不能同时配置。"
                            : $"{location} 的 Params[{i}] 尚未配置数据来源。");
                    }
                    else if (hasReference
                        && !ValueRef.TryCreate(
                            parameter.ValueIndex,
                            parameter.ValueIndex2Index,
                            parameter.ValueName,
                            parameter.ValueName2Index,
                            false,
                            $"Params[{i}] 数据来源",
                            out _,
                            out string valueError))
                    {
                        errors.Add($"{location}：{valueError}。");
                    }
                }
                return;
            }

            if (operation is DelDataStructItem delete)
            {
                if (SelectAddressMode(
                        delete.TargetStructName,
                        delete.TargetStructIndex,
                        "目标结构体",
                        out bool useTargetStructName)
                    && ResolveStruct(useTargetStructName,
                        delete.TargetStructIndex, delete.TargetStructName,
                        "目标", out int structIndex))
                {
                    if (SelectAddressMode(
                        delete.TargetItemName,
                        delete.TargetItemIndex,
                        "目标数据项",
                        out bool useTargetItemName))
                    {
                        ResolveItem(structIndex, useTargetItemName,
                            delete.TargetItemIndex, delete.TargetItemName,
                            "目标", out _);
                    }
                }
                return;
            }

            if (operation is FindDataStructItem find)
            {
                if (SelectAddressMode(
                    find.TargetStructName,
                    find.TargetStructIndex,
                    "目标结构体",
                    out bool useTargetStructName))
                {
                    ResolveStruct(useTargetStructName,
                        find.TargetStructIndex, find.TargetStructName,
                        "目标", out _);
                }
                if (!string.Equals(find.Type, "名称等于key", StringComparison.Ordinal)
                    && !string.Equals(find.Type, "字符串等于key", StringComparison.Ordinal)
                    && !string.Equals(find.Type, "数值等于key", StringComparison.Ordinal))
                {
                    errors.Add($"{location} 的数据结构查找类型无效：{find.Type}。");
                }
                if (string.Equals(find.Type, "数值等于key", StringComparison.Ordinal)
                    && (!double.TryParse(find.Key, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number)))
                {
                    errors.Add($"{location} 的数据结构数值查找关键字无效：{find.Key}。");
                }
                return;
            }

            if (operation is GetDataStructCount count)
            {
                if (SelectAddressMode(
                    count.TargetStructName,
                    count.TargetStructIndex,
                    "目标结构体",
                    out bool useTargetStructName))
                {
                    ResolveStruct(useTargetStructName,
                        count.TargetStructIndex, count.TargetStructName,
                        "目标", out _);
                }
            }
        }

        private static void ValidateIoLogicGoto(
            OperationType operation,
            List<string> errors,
            string location,
            ProcessDefinitionValidationContext validationContext)
        {
            if (!(operation is IoLogicGoto ioLogicGoto)
                || ioLogicGoto.IoParams == null
                || validationContext?.Runtime?.Stores.IoConfiguration.ByName == null)
            {
                return;
            }
            foreach (IoLogicGotoParam condition in ioLogicGoto.IoParams)
            {
                if (condition == null || string.IsNullOrWhiteSpace(condition.IoName)
                    || !validationContext.Runtime.Stores.IoConfiguration.ByName.TryGetValue(condition.IoName, out IO io)
                    || io == null)
                {
                    errors.Add($"{location} 的输入IO不存在：{condition?.IoName ?? string.Empty}。");
                    continue;
                }
                if (!string.Equals(io.IOType, "通用输入", StringComparison.Ordinal))
                {
                    errors.Add($"{location} 的IO逻辑跳转只能引用通用输入：{condition.IoName}。");
                }
            }
        }

        private static void ValidateIoOutputOperation(OperationType operation, List<string> errors,
            string location, ProcessDefinitionValidationContext validationContext)
        {
            CustomList<IoOutParam> outputs = operation is IoOperate ioOperate
                ? ioOperate.IoParams
                : (operation is IoGroup ioGroup ? ioGroup.OutIoParams : null);
            if (outputs == null || outputs.Count == 0
                || validationContext?.Runtime?.Stores.IoConfiguration.ByName == null)
            {
                return;
            }

            int? cardNum = null;
            var indexes = new HashSet<int>();
            foreach (IoOutParam output in outputs)
            {
                if (output == null || string.IsNullOrWhiteSpace(output.IoName)
                    || !validationContext.Runtime.Stores.IoConfiguration.ByName.TryGetValue(output.IoName, out IO io) || io == null)
                {
                    errors.Add($"{location} 的输出IO不存在：{output?.IoName ?? string.Empty}。");
                    continue;
                }
                if (io.IOType != "通用输出")
                {
                    errors.Add($"{location} 只能引用通用输出：{output.IoName}。");
                }
                if (cardNum.HasValue && cardNum.Value != io.CardNum)
                {
                    errors.Add($"{location} 的全部输出IO必须位于同一张控制卡。");
                }
                cardNum = io.CardNum;
                if (!int.TryParse(io.IOIndex, out int index) || index < 0 || index > 31)
                {
                    errors.Add($"{location} 的输出IO端口索引必须在0..31内：{output.IoName}-{io.IOIndex}。");
                }
                else if (!indexes.Add(index))
                {
                    errors.Add($"{location} 包含重复输出索引：{output.IoName}-{io.IOIndex}。");
                }
            }
        }

        private static void ValidateCoordinatedStationMotion(OperationType operation, List<string> errors,
            string location, ProcessDefinitionValidationContext validationContext)
        {
            string stationName;
            StationRunPos stationRunPos = operation as StationRunPos;
            if (stationRunPos != null)
            {
                stationName = stationRunPos.StationName;
            }
            else if (operation is StationRunRel stationRunRel)
            {
                stationName = stationRunRel.StationName;
            }
            else
            {
                return;
            }
            DataStation station = validationContext?.Runtime?.Stores.Stations.Items
                .FirstOrDefault(item => item != null && string.Equals(item.Name, stationName, StringComparison.Ordinal));
            if (station == null)
            {
                return;
            }
            if (station.CoordinateSystem > 1)
            {
                errors.Add($"{location} 引用工站[{stationName}]的坐标系无效:{station.CoordinateSystem}。");
            }
            List<AxisConfig> axisConfigs = station.dataAxis?.axisConfigs;
            if (axisConfigs == null)
            {
                return;
            }
            var cards = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < axisConfigs.Count && i < 6; i++)
            {
                AxisConfig axis = axisConfigs[i];
                if (axis == null || axis.AxisName == "-1")
                {
                    continue;
                }
                if (stationRunPos != null && stationRunPos.IsDisableAxis == "有禁用"
                    && stationRunPos.GetAllValues()[i])
                {
                    continue;
                }
                cards.Add(axis.CardNum ?? string.Empty);
            }
            if (cards.Count > 1)
            {
                errors.Add($"{location} 的协调直线运动参与轴必须位于同一张控制卡。");
            }
        }

        private static void ValidatePlcOperation(OperationType operation, List<string> errors,
            string location, ProcessDefinitionValidationContext validationContext)
        {
            if (operation == null || operation.Disable) return;

            bool HasPlc(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return false;
                if (validationContext?.HasPlcCatalog == true)
                    return validationContext.PlcNames.Contains(name);
                return (validationContext?.Runtime?.Stores.Plc.GetSnapshot().Devices ?? new List<PlcDeviceConfig>())
                    .Any(item => item != null && string.Equals(item.Name, name, StringComparison.Ordinal));
            }
            bool TryGetVariable(string name, out DicValue value)
            {
                value = null;
                if (string.IsNullOrWhiteSpace(name)) return false;
                if (validationContext != null && validationContext.VariableDefinitions.Count > 0)
                    return validationContext.VariableDefinitions.TryGetValue(name, out value);
                return validationContext?.Runtime?.Stores.Values.TryGetValueByName(name, out value) == true;
            }
            bool ValidateVariable(string name, PlcDataType dataType, HashSet<string> unique, string field)
            {
                if (string.IsNullOrWhiteSpace(name) || !unique.Add(name))
                {
                    errors.Add($"{location} 的 {field} 为空或重复。");
                    return false;
                }
                if (!TryGetVariable(name, out DicValue value))
                {
                    // 变量可晚于流程结构补齐；启动闸门负责报告 missing/incomplete。
                    return true;
                }
                string expected = dataType == PlcDataType.String ? "string" : "double";
                if (!string.Equals(value.Type, expected, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{location} 的变量[{name}]必须是{expected}类型。");
                    return false;
                }
                return true;
            }

            string deviceName;
            if (operation is PlcMappingControl mapping)
            {
                deviceName = mapping.DeviceName;
                if (!Enum.IsDefined(typeof(PlcMappingAction), mapping.Action))
                    errors.Add($"{location} 的PLC映射控制动作无效。");
                if (!string.IsNullOrWhiteSpace(deviceName) && !HasPlc(deviceName))
                    errors.Add($"{location} 引用的PLC设备尚未配置：{deviceName ?? string.Empty}。");
                return;
            }
            if (!(operation is PlcReadWrite readWrite)) return;

            deviceName = readWrite.DeviceName;
            if (!string.IsNullOrWhiteSpace(deviceName) && !HasPlc(deviceName))
                errors.Add($"{location} 引用的PLC设备尚未配置：{deviceName ?? string.Empty}。");
            if (readWrite.ModelVersion != PlcReadWrite.CurrentModelVersion)
            {
                errors.Add($"{location} 的PLC读写模型版本无效，必须为{PlcReadWrite.CurrentModelVersion}。");
                return;
            }
            if (!Enum.IsDefined(typeof(PlcAccessAction), readWrite.Action)
                || !Enum.IsDefined(typeof(PlcAccessMode), readWrite.Mode))
            {
                errors.Add($"{location} 的PLC读写枚举参数无效。");
                return;
            }

            if (readWrite.Action == PlcAccessAction.Read && readWrite.Mode == PlcAccessMode.Items)
            {
                if (readWrite.ReadItems == null
                    || readWrite.ReadItems.Count < 1
                    || readWrite.ReadItems.Count > 100)
                {
                    errors.Add($"{location} 的PLC按项读取项数量必须为1..100。");
                    return;
                }
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < readWrite.ReadItems.Count; index++)
                {
                    PlcReadItem item = readWrite.ReadItems[index];
                    if (item == null)
                    {
                        errors.Add($"{location} 的PLC读取项{index + 1}为空。");
                        continue;
                    }
                    if (!PlcDirectOperationValidator.TryValidateAddress(
                        item.Area, item.StartAddress, item.DataType, 1,
                        item.StringByteLength, false, out string itemError))
                    {
                        errors.Add($"{location} 的PLC读取项{index + 1}：{itemError}");
                    }
                    ValidateVariable(item.VariableName, item.DataType, unique, $"ReadItems[{index}].VariableName");
                }
                return;
            }

            if (readWrite.Action == PlcAccessAction.Write && readWrite.Mode == PlcAccessMode.Items)
            {
                if (readWrite.WriteItems == null
                    || readWrite.WriteItems.Count < 1
                    || readWrite.WriteItems.Count > 100)
                {
                    errors.Add($"{location} 的PLC按项写入项数量必须为1..100。");
                    return;
                }
                for (int index = 0; index < readWrite.WriteItems.Count; index++)
                {
                    PlcWriteItem item = readWrite.WriteItems[index];
                    if (item == null)
                    {
                        errors.Add($"{location} 的PLC写入项{index + 1}为空。");
                        continue;
                    }
                    if (!Enum.IsDefined(typeof(PlcValueSource), item.Source))
                    {
                        errors.Add($"{location} 的PLC写入项{index + 1}数据来源无效。");
                        continue;
                    }
                    if (!PlcDirectOperationValidator.TryValidateAddress(
                        item.Area, item.StartAddress, item.DataType, 1,
                        item.StringByteLength, true, out string itemError))
                    {
                        errors.Add($"{location} 的PLC写入项{index + 1}：{itemError}");
                    }
                    if (item.Source == PlcValueSource.Variable)
                    {
                        ValidateVariable(item.VariableName, item.DataType,
                            new HashSet<string>(StringComparer.Ordinal), $"WriteItems[{index}].VariableName");
                    }
                    else
                    {
                        string constantError = null;
                        if (item.ConstantValue == null
                            || !HslModbusAdapter.TryNormalizeValues(item.DataType,
                                new object[] { item.ConstantValue }, out _, out constantError))
                        {
                            errors.Add($"{location} 的PLC写入项{index + 1}固定值无效：{constantError ?? "固定值为空。"}");
                        }
                    }
                }
                return;
            }

            if (readWrite.Action == PlcAccessAction.Read)
            {
                PlcReadBatch batch = readWrite.ReadBatch;
                if (batch == null)
                {
                    errors.Add($"{location} 尚未配置连续批量读取参数。");
                    return;
                }
                if (!PlcDirectOperationValidator.TryValidateAddress(
                    batch.Area, batch.StartAddress, batch.DataType, batch.ElementCount,
                    batch.StringByteLength, false, out string addressError))
                {
                    errors.Add($"{location} 的PLC连续读取参数无效：{addressError}");
                    return;
                }
                ValidateConsecutiveVariables(batch.FirstVariableName, batch.ElementCount,
                    batch.DataType, "首保存变量");
                return;
            }

            PlcWriteBatch writeBatch = readWrite.WriteBatch;
            if (writeBatch == null)
            {
                errors.Add($"{location} 尚未配置连续批量写入参数。");
                return;
            }
            if (!Enum.IsDefined(typeof(PlcValueSource), writeBatch.Source))
            {
                errors.Add($"{location} 的PLC连续写入数据来源无效。");
                return;
            }
            if (!PlcDirectOperationValidator.TryValidateAddress(
                writeBatch.Area, writeBatch.StartAddress, writeBatch.DataType,
                writeBatch.ElementCount, writeBatch.StringByteLength, true, out string writeAddressError))
            {
                errors.Add($"{location} 的PLC连续写入参数无效：{writeAddressError}");
                return;
            }
            if (writeBatch.Source == PlcValueSource.Variable)
            {
                ValidateConsecutiveVariables(writeBatch.FirstVariableName, writeBatch.ElementCount,
                    writeBatch.DataType, "首来源变量");
            }
            else
            {
                string batchConstantError = null;
                if (writeBatch.ConstantValue == null
                    || !HslModbusAdapter.TryNormalizeValues(writeBatch.DataType,
                        Enumerable.Repeat<object>(writeBatch.ConstantValue, writeBatch.ElementCount).ToArray(),
                        out _, out batchConstantError))
                {
                    errors.Add($"{location} 的PLC连续写入固定值无效：{batchConstantError ?? "固定值为空。"}");
                }
            }

            void ValidateConsecutiveVariables(
                string firstVariableName, int count, PlcDataType dataType, string fieldName)
            {
                if (!TryGetVariable(firstVariableName, out DicValue firstVariable))
                {
                    return;
                }
                var continuousVariables = new HashSet<string>(StringComparer.Ordinal);
                for (int offset = 0; offset < count; offset++)
                {
                    DicValue variable = null;
                    if (validationContext != null && validationContext.VariableDefinitions.Count > 0)
                    {
                        variable = validationContext.VariableDefinitions.Values.FirstOrDefault(
                            item => item != null && item.Index == firstVariable.Index + offset);
                    }
                    else
                    {
                        validationContext?.Runtime?.Stores.Values.TryGetValueByIndex(firstVariable.Index + offset, out variable);
                    }
                    if (variable == null)
                    {
                        continue;
                    }
                    ValidateVariable(variable.Name, dataType, continuousVariables, $"连续变量[{offset}]");
                }
            }
        }

        private static void ValidateCommunicationOperation(OperationType operation, List<string> errors,
            string location, ProcessDefinitionValidationContext validationContext)
        {
            if (operation == null || operation.Disable)
            {
                return;
            }

            bool HasValue(string name) => !string.IsNullOrWhiteSpace(name);
            bool HasTcp(string name) => !string.IsNullOrWhiteSpace(name)
                && (validationContext != null
                    ? validationContext.TcpNames.Contains(name)
                    : validationContext?.Runtime?.Stores.Communication.TryGetSocket(name, out _) == true);
            bool HasSerial(string name) => !string.IsNullOrWhiteSpace(name)
                && (validationContext != null
                    ? validationContext.SerialNames.Contains(name)
                    : validationContext?.Runtime?.Stores.Communication.TryGetSerial(name, out _) == true);

            if (operation is TcpOps tcpOps)
            {
                if (tcpOps.Params == null || tcpOps.Params.Count == 0)
                {
                    errors.Add($"{location} TCP操作参数为空");
                    return;
                }
                foreach (TcpOpsParam item in tcpOps.Params)
                {
                    if (item == null || !HasTcp(item.Name)
                        || (item.Ops != "启动" && item.Ops != "断开"))
                    {
                        errors.Add($"{location} TCP操作配置无效");
                    }
                }
                return;
            }
            if (operation is WaitTcp waitTcp)
            {
                if (waitTcp.Params == null || waitTcp.Params.Count == 0
                    || waitTcp.Params.Any(item => item == null || !HasTcp(item.Name) || item.TimeoutMs <= 0))
                {
                    errors.Add($"{location} 等待TCP配置无效");
                }
                return;
            }
            if (operation is SendTcpMsg sendTcp)
            {
                if (!HasTcp(sendTcp.ConnectionName))
                {
                    errors.Add($"{location} TCP对象不存在：{sendTcp.ConnectionName ?? string.Empty}");
                }
                if (!HasValue(sendTcp.Msg))
                {
                    errors.Add($"{location} TCP发送变量不存在：{sendTcp.Msg ?? string.Empty}");
                }
                if (sendTcp.TimeoutMs <= 0)
                {
                    errors.Add($"{location} TCP发送超时必须大于0：{sendTcp.TimeoutMs}");
                }
                return;
            }
            if (operation is ReceiveTcpMsg receiveTcp)
            {
                if (!HasTcp(receiveTcp.ConnectionName))
                {
                    errors.Add($"{location} TCP对象不存在：{receiveTcp.ConnectionName ?? string.Empty}");
                }
                if (!HasValue(receiveTcp.MsgSaveValue))
                {
                    errors.Add($"{location} TCP接收保存变量不存在：{receiveTcp.MsgSaveValue ?? string.Empty}");
                }
                if (receiveTcp.TimeoutMs <= 0)
                {
                    errors.Add($"{location} TCP接收超时必须大于0：{receiveTcp.TimeoutMs}");
                }
                return;
            }
            if (operation is SerialPortOps serialOps)
            {
                if (serialOps.Params == null || serialOps.Params.Count == 0
                    || serialOps.Params.Any(item => item == null || !HasSerial(item.Name)
                        || (item.Ops != "启动" && item.Ops != "断开")))
                {
                    errors.Add($"{location} 串口操作配置无效");
                }
                return;
            }
            if (operation is WaitSerialPort waitSerial)
            {
                if (waitSerial.Params == null || waitSerial.Params.Count == 0
                    || waitSerial.Params.Any(item => item == null || !HasSerial(item.Name) || item.TimeoutMs <= 0))
                {
                    errors.Add($"{location} 等待串口配置无效");
                }
                return;
            }
            if (operation is SendSerialPortMsg sendSerial)
            {
                if (!HasSerial(sendSerial.ConnectionName) || !HasValue(sendSerial.Msg) || sendSerial.TimeoutMs <= 0)
                {
                    errors.Add($"{location} 串口发送配置无效");
                }
                return;
            }
            if (operation is ReceiveSerialPortMsg receiveSerial)
            {
                if (!HasSerial(receiveSerial.ConnectionName) || !HasValue(receiveSerial.MsgSaveValue) || receiveSerial.TimeoutMs <= 0)
                {
                    errors.Add($"{location} 串口接收配置无效");
                }
                return;
            }
            if (operation is SendReceiveCommMsg request)
            {
                bool validChannel = request.CommType == "TCP"
                    ? HasTcp(request.ConnectionName)
                    : request.CommType == "串口" && HasSerial(request.ConnectionName);
                if (!validChannel || !HasValue(request.SendMsg) || request.TimeoutMs <= 0
                    || (!string.IsNullOrWhiteSpace(request.ReceiveSaveValue) && !HasValue(request.ReceiveSaveValue)))
                {
                    errors.Add($"{location} 通讯请求响应配置无效");
                }
            }
        }

        public static Dictionary<int, string> BuildProcFileIndexMap(string path, out int maxIndex)
        {
            Dictionary<int, string> indexMap = new Dictionary<int, string>();
            maxIndex = -1;
            foreach (string file in Directory.EnumerateFiles(path, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(name, out int index))
                {
                    continue;
                }
                indexMap[index] = file;
                if (index > maxIndex)
                {
                    maxIndex = index;
                }
            }
            return indexMap;
        }

        public static List<string> ValidateProcFileContinuity(Dictionary<int, string> indexMap, int maxIndex)
        {
            List<string> errors = new List<string>();
            if (indexMap == null || indexMap.Count == 0)
            {
                return errors;
            }
            if (!indexMap.ContainsKey(0))
            {
                errors.Add("流程文件索引必须从0开始。");
            }
            for (int i = 0; i <= maxIndex; i++)
            {
                if (!indexMap.ContainsKey(i))
                {
                    errors.Add($"流程文件缺失：{i}.json");
                }
            }
            return errors;
        }

        public static List<string> ValidateProcGotoTargets(int procIndex, Proc proc)
        {
            List<string> errors = new List<string>();
            if (proc?.steps == null)
            {
                return errors;
            }
            for (int i = 0; i < proc.steps.Count; i++)
            {
                Step step = proc.steps[i];
                if (step?.Ops == null)
                {
                    continue;
                }
                for (int j = 0; j < step.Ops.Count; j++)
                {
                    OperationType op = step.Ops[j];
                    if (op == null)
                    {
                        continue;
                    }
                    ValidateGotoTargets(op, op, procIndex, proc, errors, $"流程{procIndex}步骤{i}指令{j}");
                }
            }
            return errors;
        }

        public static bool TryValidateOperationGoto(OperationType operation, int procIndex, Proc proc, out string error)
        {
            var errors = new List<string>();
            if (operation != null)
            {
                ValidateGotoTargets(operation, operation, procIndex, proc, errors, "指令");
            }
            error = errors.FirstOrDefault();
            return error == null;
        }

        private static void ValidateGotoTargets(object obj, OperationType rootOperation, int procIndex, Proc proc, List<string> errors, string context)
        {
            foreach (var propertyInfo in obj.GetType().GetProperties())
            {
                if (propertyInfo.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                bool browsable = propertyInfo.GetCustomAttribute<System.ComponentModel.BrowsableAttribute>()?.Browsable ?? true;
                if (obj is IPropertyVisibilityProvider visibilityProvider
                    && !visibilityProvider.IsPropertyVisible(propertyInfo.Name, browsable))
                {
                    continue;
                }
                if (propertyInfo.PropertyType == typeof(string) && propertyInfo.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string value = propertyInfo.GetValue(obj) as string;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        // 配置阶段允许先保存不完整跳转；启动闸门会给出明确阻断原因。
                    }
                    else
                    {
                        if (value.StartsWith(DeletedGotoPrefix, StringComparison.Ordinal))
                        {
                            // 删除目标后的跳转保留为草稿，后续阶段可以再指定新目标。
                        }
                        else if (value.StartsWith(PendingGotoPrefix, StringComparison.Ordinal))
                        {
                            // 目标可在后续 ChangeSet 中补齐，当前保留为待解析引用。
                        }
                        else if (!TryParseGotoKey(value, out int gotoProc, out int gotoStep, out int gotoOp))
                        {
                            errors.Add($"{context}跳转地址格式错误：{value}。只能填写三段式数字地址 procIndex-stepIndex-opIndex，例如 0-2-3；界面显示文字不能作为地址。");
                        }
                        else if (gotoProc != procIndex)
                        {
                            errors.Add($"{context}跳转地址跨流程：{value}");
                        }
                        else if (!TryValidateGotoRange(proc, procIndex, gotoStep, gotoOp, out string rangeError))
                        {
                            errors.Add($"{context} {rangeError}");
                        }
                    }
                }

                var propertyValue = propertyInfo.GetValue(obj);
                if (propertyValue is System.Collections.IEnumerable enumerable && !(propertyValue is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null)
                        {
                            continue;
                        }
                        ValidateGotoTargets(item, rootOperation, procIndex, proc, errors, context);
                    }
                }
            }
        }

        private static bool TryValidateGotoRange(Proc proc, int procIndex, int stepIndex, int opIndex, out string error)
        {
            error = null;
            if (proc?.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                error = $"跳转地址步骤越界：{procIndex}-{stepIndex}-{opIndex}";
                return false;
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex < 0 || opIndex >= step.Ops.Count)
            {
                error = $"跳转地址指令越界：{procIndex}-{stepIndex}-{opIndex}";
                return false;
            }
            return true;
        }

        public static bool TryParseGotoKey(string value, out int procIndex, out int stepIndex, out int opIndex)
        {
            procIndex = -1;
            stepIndex = -1;
            opIndex = -1;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            string[] parts = value.Split('-');
            if (parts.Length != 3)
            {
                return false;
            }
            return int.TryParse(parts[0], out procIndex)
                && int.TryParse(parts[1], out stepIndex)
                && int.TryParse(parts[2], out opIndex);
        }


    }
}
