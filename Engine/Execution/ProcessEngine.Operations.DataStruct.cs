using System;
// 模块：引擎 / 执行。
// 职责范围：负责运行绑定、调度、状态管理以及各类流程指令的确定性执行。

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Automation
{
    public partial class ProcessEngine
    {
        private DataStructStore GetDataStructStore(ProcHandle evt)
        {
            DataStructStore store = Context?.DataStructStore;
            if (store == null)
            {
                throw CreateAlarmException(evt, "数据结构存储未初始化");
            }
            return store;
        }

        private int ResolveDataStructIndex(ProcHandle evt, bool useName, int index,
            string name, string role)
        {
            DataStructStore store = GetDataStructStore(evt);
            if (!store.TryResolveStructIndex(useName, index, name,
                    out int resolvedIndex, out string error))
            {
                throw CreateAlarmException(evt, $"{role}{error}");
            }
            return resolvedIndex;
        }

        private int ResolveDataStructItemIndex(ProcHandle evt, int structIndex,
            bool useName, int index, string name, string role)
        {
            DataStructStore store = GetDataStructStore(evt);
            if (!store.TryResolveItemIndex(structIndex, useName, index, name,
                    out int resolvedIndex, out string error))
            {
                throw CreateAlarmException(evt, $"{role}{error}");
            }
            return resolvedIndex;
        }

        private int ResolveDataStructFieldIndex(ProcHandle evt, int structIndex,
            int itemIndex, bool useName, int index, string name, string role)
        {
            DataStructStore store = GetDataStructStore(evt);
            if (!store.TryResolveFieldIndex(structIndex, itemIndex, useName, index, name,
                    out int resolvedIndex, out string error))
            {
                throw CreateAlarmException(evt, $"{role}{error}");
            }
            return resolvedIndex;
        }

        private static string FormatDataStructValue(object value)
        {
            return value is double number
                ? number.ToString("G17", CultureInfo.InvariantCulture)
                : value?.ToString();
        }

        private static bool TrySelectDataStructAddressMode(
            string name,
            int index,
            string label,
            out bool useName,
            out string error)
        {
            bool hasName = !string.IsNullOrWhiteSpace(name);
            bool hasIndex = index >= 0;
            useName = hasName;
            if (hasName && hasIndex)
            {
                error = $"{label}名称与索引不能同时配置";
                return false;
            }
            if (!hasName && !hasIndex)
            {
                error = $"{label}尚未配置";
                return false;
            }
            error = null;
            return true;
        }

        public bool RunSetDataStructItem(ProcHandle evt, SetDataStructItem setDataStructItem)
        {
            if (setDataStructItem == null || setDataStructItem.Params == null)
            {
                MarkAlarm(evt, "数据结构设置参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            SetDataStructRuntimeBinding binding =
                setDataStructItem.RuntimeBinding as SetDataStructRuntimeBinding;
            string bindError = null;
            if (binding == null
                && !(evt?.Proc != null
                    ? ProcessRuntimeBinder.TryBind(
                        evt.Proc,
                        evt.procNum,
                        Context?.ValueStore,
                        Context?.DataStructStore,
                        out bindError)
                    : ProcessRuntimeBinder.TryBindStandalone(
                        evt?.procId ?? Guid.Empty,
                        Context?.ValueStore,
                        Context?.DataStructStore,
                        setDataStructItem,
                        out bindError)))
            {
                throw CreateAlarmException(
                    evt,
                    bindError ?? "数据结构设置运行计划未编译");
            }
            binding = binding
                ?? setDataStructItem.RuntimeBinding as SetDataStructRuntimeBinding;
            if (binding == null
                || binding.Values.Length != setDataStructItem.Params.Count)
            {
                throw CreateAlarmException(evt, "数据结构设置运行计划未编译");
            }
            DataStructStore dataStructStore = GetDataStructStore(evt);
            if (TryExecuteSetDataStructBinding(
                    evt, binding, dataStructStore, out string executionError))
            {
                return true;
            }
            if (HasExpiredDataStructBinding(dataStructStore, binding.Values))
            {
                if (!ProcessRuntimeBinder.TryBindStandalone(
                        evt?.procId ?? Guid.Empty,
                        Context?.ValueStore,
                        dataStructStore,
                        setDataStructItem,
                        out string rebindError))
                {
                    executionError = rebindError;
                }
                else if (setDataStructItem.RuntimeBinding
                        is SetDataStructRuntimeBinding refreshed
                    && refreshed.Values.Length == setDataStructItem.Params.Count
                    && TryExecuteSetDataStructBinding(
                        evt,
                        refreshed,
                        dataStructStore,
                        out executionError))
                {
                    return true;
                }
            }
            throw CreateAlarmException(
                evt, $"设置数据结构失败:{executionError}");
        }

        private bool TryExecuteSetDataStructBinding(
            ProcHandle evt,
            SetDataStructRuntimeBinding binding,
            DataStructStore dataStructStore,
            out string error)
        {
            error = null;
            if (binding.Values.Length == 1)
            {
                if (!TryPrepareDataStructRuntimeValue(
                        evt,
                        binding.Values[0],
                        0,
                        out DataStructFieldRuntimeValue value,
                        out error))
                {
                    return false;
                }
                return value.Binding.ValueType == DataStructValueType.Number
                    ? dataStructStore.TrySetBoundFieldNumber(
                        value.Binding, value.Number, out error)
                    : dataStructStore.TrySetBoundFieldText(
                        value.Binding, value.Text, out error);
            }
            var values = new DataStructFieldRuntimeValue[binding.Values.Length];
            for (int i = 0; i < binding.Values.Length; i++)
            {
                if (!TryPrepareDataStructRuntimeValue(
                        evt,
                        binding.Values[i],
                        i,
                        out values[i],
                        out error))
                {
                    return false;
                }
            }
            return dataStructStore.TrySetBoundFieldValues(values, out error);
        }

        private static bool HasExpiredDataStructBinding(
            DataStructStore store,
            IReadOnlyList<SetDataStructValueRuntimeBinding> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (!store.IsRuntimeFieldBindingCurrent(values[i]?.Target))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryPrepareDataStructRuntimeValue(
            ProcHandle evt,
            SetDataStructValueRuntimeBinding binding,
            int parameterIndex,
            out DataStructFieldRuntimeValue value,
            out string error)
        {
            value = default;
            error = null;
            if (binding?.Target == null)
            {
                error = $"字段[{parameterIndex}]运行绑定为空";
                return false;
            }
            if (binding.Target.ValueType == DataStructValueType.Number)
            {
                double number;
                if (binding.UsesLiteralValue)
                {
                    number = binding.LiteralNumber;
                }
                else
                {
                    if (!binding.ValueSource.TryResolveValue(
                            Context?.ValueStore,
                            $"写入值[{parameterIndex}]",
                            evt?.procId ?? Guid.Empty,
                            out DicValue source,
                            out error))
                    {
                        return false;
                    }
                    if (string.Equals(
                            source.Type, "double",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        number = source.GetDValue();
                    }
                    else if (!double.TryParse(
                            source.Value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out number)
                        || double.IsNaN(number)
                        || double.IsInfinity(number))
                    {
                        error = $"写入值[{parameterIndex}]数值无效:{source.Value}";
                        return false;
                    }
                }
                value = DataStructFieldRuntimeValue.FromNumber(
                    binding.Target, number);
                return true;
            }

            string text;
            if (binding.UsesLiteralValue)
            {
                text = binding.LiteralText;
            }
            else
            {
                if (!binding.ValueSource.TryResolveValue(
                        Context?.ValueStore,
                        $"写入值[{parameterIndex}]",
                        evt?.procId ?? Guid.Empty,
                        out DicValue source,
                        out error))
                {
                    return false;
                }
                text = source.Value;
            }
            if (text == null)
            {
                error = $"写入值[{parameterIndex}]文本值为空";
                return false;
            }
            value = DataStructFieldRuntimeValue.FromText(
                binding.Target, text);
            return true;
        }

        public bool RunGetDataStructItem(ProcHandle evt, GetDataStructItem getDataStructItem)
        {
            ValueConfigStore valueStore = Context?.ValueStore;
            if (valueStore == null)
            {
                throw CreateAlarmException(evt, "变量库未初始化");
            }
            if (getDataStructItem == null)
            {
                MarkAlarm(evt, "数据结构读取参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            GetDataStructRuntimeBinding binding =
                getDataStructItem.RuntimeBinding as GetDataStructRuntimeBinding;
            string bindError = null;
            if (binding == null
                && !(evt?.Proc != null
                    ? ProcessRuntimeBinder.TryBind(
                        evt.Proc,
                        evt.procNum,
                        valueStore,
                        Context?.DataStructStore,
                        out bindError)
                    : ProcessRuntimeBinder.TryBindStandalone(
                        evt?.procId ?? Guid.Empty,
                        valueStore,
                        Context?.DataStructStore,
                        getDataStructItem,
                        out bindError)))
            {
                throw CreateAlarmException(
                    evt, bindError ?? "数据结构读取运行计划未编译");
            }
            binding = binding
                ?? getDataStructItem.RuntimeBinding as GetDataStructRuntimeBinding;
            if (binding == null)
            {
                throw CreateAlarmException(evt, "数据结构读取运行计划未编译");
            }
            string source = evt?.GetOperationSource();
            DataStructStore dataStructStore = GetDataStructStore(evt);
            if (getDataStructItem.IsAllItem)
            {
                if (!binding.FirstOutput.TryResolveValue(
                        valueStore, "首个结果变量", evt.procId,
                        out DicValue firstResult, out string firstResultError))
                {
                    throw CreateAlarmException(evt,
                        firstResultError);
                }
                if (!dataStructStore.TryGetBoundFieldValues(
                        binding.Fields,
                        out object[] fieldValues,
                        out string readError))
                {
                    throw CreateAlarmException(evt,
                        $"读取数据结构失败:{readError}");
                }
                for (int i = 0; i < fieldValues.Length; i++)
                {
                    if (!valueStore.TryGetValueByIndexForProcess(
                            firstResult.Index + i, evt.procId, out _))
                    {
                        throw CreateAlarmException(evt,
                            $"连续结果变量不存在或当前流程无权访问:索引{firstResult.Index + i}");
                    }
                }
                for (int i = 0; i < fieldValues.Length; i++)
                {
                    if (!valueStore.SetValueByIndexForProcess(
                            firstResult.Index + i, fieldValues[i], evt.procId, source))
                    {
                        throw CreateAlarmException(evt,
                            $"保存变量失败:索引{firstResult.Index + i}");
                    }
                }
            }
            else
            {
                if (getDataStructItem.Params == null)
                {
                    MarkAlarm(evt, "数据结构读取参数为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (getDataStructItem.Params.Count == 1)
                {
                    if (binding.Outputs.Length != 1
                        || binding.Fields.Length != 1)
                    {
                        throw CreateAlarmException(
                            evt, "数据结构读取运行计划未编译");
                    }
                    if (!binding.Outputs[0].TryResolveValue(
                            valueStore, "结果变量", evt.procId,
                            out DicValue outputItem, out string outputResolveError))
                    {
                        throw CreateAlarmException(evt, outputResolveError);
                    }
                    if (!dataStructStore.TryGetBoundFieldValue(
                            binding.Fields[0],
                            out object outputValue,
                            out string singleReadError))
                    {
                        throw CreateAlarmException(evt,
                            $"读取数据结构失败:{singleReadError}");
                    }
                    if (!valueStore.SetResolvedValueForProcess(
                            outputItem, outputValue, evt.procId, source))
                    {
                        string outputName = string.IsNullOrWhiteSpace(outputItem.Name)
                            ? $"索引{outputItem.Index}"
                            : outputItem.Name;
                        throw CreateAlarmException(evt, $"保存变量失败:{outputName}");
                    }
                    return true;
                }
                var outputItems = new List<DicValue>();
                if (binding.Outputs.Length != getDataStructItem.Params.Count
                    || binding.Fields.Length != getDataStructItem.Params.Count)
                {
                    throw CreateAlarmException(evt, "数据结构读取运行计划未编译");
                }
                for (int i = 0; i < getDataStructItem.Params.Count; i++)
                {
                    if (!binding.Outputs[i].TryResolveValue(
                            valueStore, "结果变量", evt.procId,
                            out DicValue outputItem, out string outputResolveError))
                    {
                        throw CreateAlarmException(evt, outputResolveError);
                    }
                    outputItems.Add(outputItem);
                }
                if (!dataStructStore.TryGetBoundFieldValues(
                        binding.Fields,
                        out object[] outputValues,
                        out string readError))
                {
                    throw CreateAlarmException(evt,
                        $"读取数据结构失败:{readError}");
                }
                for (int i = 0; i < outputValues.Length; i++)
                {
                    if (!valueStore.SetResolvedValueForProcess(
                            outputItems[i], outputValues[i], evt.procId, source))
                    {
                        string outputName = string.IsNullOrWhiteSpace(outputItems[i].Name)
                            ? $"索引{outputItems[i].Index}"
                            : outputItems[i].Name;
                        throw CreateAlarmException(evt, $"保存变量失败:{outputName}");
                    }
                }
            }
            return true;
        }
        public bool RunCopyDataStructItem(ProcHandle evt, CopyDataStructItem copyDataStructItem)
        {
            if (copyDataStructItem == null)
            {
                MarkAlarm(evt, "数据结构复制参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            DataStructStore dataStructStore = GetDataStructStore(evt);
            if (!TrySelectDataStructAddressMode(
                    copyDataStructItem.SourceStructName,
                    copyDataStructItem.SourceStructIndex,
                    "源结构体",
                    out bool useSourceStructName,
                    out string addressError)
                || !TrySelectDataStructAddressMode(
                    copyDataStructItem.SourceItemName,
                    copyDataStructItem.SourceItemIndex,
                    "源数据项",
                    out bool useSourceItemName,
                    out addressError)
                || !TrySelectDataStructAddressMode(
                    copyDataStructItem.TargetStructName,
                    copyDataStructItem.TargetStructIndex,
                    "目标结构体",
                    out bool useTargetStructName,
                    out addressError)
                || !TrySelectDataStructAddressMode(
                    copyDataStructItem.TargetItemName,
                    copyDataStructItem.TargetItemIndex,
                    "目标数据项",
                    out bool useTargetItemName,
                    out addressError))
            {
                throw CreateAlarmException(
                    evt,
                    $"复制数据结构失败:{addressError}");
            }
            int sourceStructIndex = ResolveDataStructIndex(evt,
                useSourceStructName,
                copyDataStructItem.SourceStructIndex,
                copyDataStructItem.SourceStructName,
                "复制数据结构失败:源");
            int sourceItemIndex = ResolveDataStructItemIndex(evt, sourceStructIndex,
                useSourceItemName,
                copyDataStructItem.SourceItemIndex,
                copyDataStructItem.SourceItemName,
                "复制数据结构失败:源");
            int targetStructIndex = ResolveDataStructIndex(evt,
                useTargetStructName,
                copyDataStructItem.TargetStructIndex,
                copyDataStructItem.TargetStructName,
                "复制数据结构失败:目标");
            int targetItemIndex = ResolveDataStructItemIndex(evt, targetStructIndex,
                useTargetItemName,
                copyDataStructItem.TargetItemIndex,
                copyDataStructItem.TargetItemName,
                "复制数据结构失败:目标");
            if (copyDataStructItem.IsAllValue)
            {
                if (!dataStructStore.TryCopyItemAll(
                        sourceStructIndex, sourceItemIndex, targetStructIndex, targetItemIndex))
                {
                    throw CreateAlarmException(evt,
                        $"复制数据结构失败:源{sourceStructIndex}-{sourceItemIndex},目标{targetStructIndex}-{targetItemIndex}");
                }
            }
            else
            {
                if (copyDataStructItem.Params == null)
                {
                    MarkAlarm(evt, "数据结构复制参数为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                var fieldMappings = new List<KeyValuePair<int, int>>();
                for (int i = 0; i < copyDataStructItem.Params.Count; i++)
                {
                    CopyDataStructItemParam parameter = copyDataStructItem.Params[i];
                    if (parameter == null)
                    {
                        throw CreateAlarmException(evt, $"复制数据结构失败:参数{i}为空");
                    }
                    if (!TrySelectDataStructAddressMode(
                            parameter.SourceFieldName,
                            parameter.SourceFieldIndex,
                            $"源字段[{i}]",
                            out bool useSourceFieldName,
                            out addressError)
                        || !TrySelectDataStructAddressMode(
                            parameter.TargetFieldName,
                            parameter.TargetFieldIndex,
                            $"目标字段[{i}]",
                            out bool useTargetFieldName,
                            out addressError))
                    {
                        throw CreateAlarmException(
                            evt,
                            $"复制数据结构失败:{addressError}");
                    }
                    int sourceFieldIndex = ResolveDataStructFieldIndex(evt,
                        sourceStructIndex, sourceItemIndex,
                        useSourceFieldName,
                        parameter.SourceFieldIndex,
                        parameter.SourceFieldName,
                        "复制数据结构失败:源");
                    int targetFieldIndex = ResolveDataStructFieldIndex(evt,
                        targetStructIndex, targetItemIndex,
                        useTargetFieldName,
                        parameter.TargetFieldIndex,
                        parameter.TargetFieldName,
                        "复制数据结构失败:目标");
                    fieldMappings.Add(new KeyValuePair<int, int>(
                        sourceFieldIndex, targetFieldIndex));
                }
                List<int> sourceFieldIndexes = fieldMappings
                    .Select(mapping => mapping.Key)
                    .ToList();
                if (!dataStructStore.TryGetItemValuesByIndex(
                        sourceStructIndex, sourceItemIndex, sourceFieldIndexes,
                        out List<DataStructFieldValueSnapshot> sourceValues,
                        out string readError))
                {
                    throw CreateAlarmException(evt,
                        $"读取数据结构失败:结构{sourceStructIndex},项{sourceItemIndex},{readError}");
                }
                var updates = new List<DataStructFieldValueUpdate>(sourceValues.Count);
                for (int i = 0; i < sourceValues.Count; i++)
                {
                    DataStructFieldValueSnapshot sourceValue = sourceValues[i];
                    if (sourceValue.Value == null)
                    {
                        throw CreateAlarmException(evt,
                            $"数据结构值为空:结构{sourceStructIndex},项{sourceItemIndex},字段{sourceValue.FieldIndex}");
                    }
                    updates.Add(new DataStructFieldValueUpdate
                    {
                        FieldIndex = fieldMappings[i].Value,
                        Value = FormatDataStructValue(sourceValue.Value),
                        ExpectedType = sourceValue.ValueType
                    });
                }
                if (!dataStructStore.TrySetItemValuesByIndex(
                        targetStructIndex, targetItemIndex, updates, out string updateError))
                {
                    throw CreateAlarmException(evt,
                        $"设置数据结构失败:结构{targetStructIndex},项{targetItemIndex},{updateError}");
                }
            }
            return true;
        }
        public bool RunInsertDataStructItem(ProcHandle evt, InsertDataStructItem insertDataStructItem)
        {
            if (insertDataStructItem == null || insertDataStructItem.Params == null)
            {
                MarkAlarm(evt, "数据结构插入参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            InsertDataStructRuntimeBinding binding =
                insertDataStructItem.RuntimeBinding as InsertDataStructRuntimeBinding;
            string bindError = null;
            if (binding == null
                && !(evt?.Proc != null
                    ? ProcessRuntimeBinder.TryBind(
                        evt.Proc,
                        evt.procNum,
                        Context?.ValueStore,
                        Context?.DataStructStore,
                        out bindError)
                    : ProcessRuntimeBinder.TryBindStandalone(
                        evt?.procId ?? Guid.Empty,
                        Context?.ValueStore,
                        Context?.DataStructStore,
                        insertDataStructItem,
                        out bindError)))
            {
                throw CreateAlarmException(
                    evt,
                    bindError ?? "数据结构插入运行计划未编译");
            }
            binding = binding
                ?? insertDataStructItem.RuntimeBinding as InsertDataStructRuntimeBinding;
            if (binding == null
                || binding.UsesLiteralValues.Length != insertDataStructItem.Params.Count
                || binding.ValueSources.Length != insertDataStructItem.Params.Count)
            {
                throw CreateAlarmException(evt, "数据结构插入运行计划未编译");
            }
            var values = new string[insertDataStructItem.Params.Count];
            for (int i = 0; i < insertDataStructItem.Params.Count; i++)
            {
                if (binding.UsesLiteralValues[i])
                {
                    values[i] = insertDataStructItem.Params[i].Value;
                    continue;
                }
                if (!binding.ValueSources[i].TryResolveValue(
                        Context?.ValueStore,
                        $"数据来源[{i}]",
                        evt?.procId ?? Guid.Empty,
                        out DicValue value,
                        out string resolveError))
                {
                    throw CreateAlarmException(evt, resolveError);
                }
                values[i] = value.Value;
            }
            if (!TrySelectDataStructAddressMode(
                    insertDataStructItem.TargetStructName,
                    insertDataStructItem.TargetStructIndex,
                    "目标结构体",
                    out bool useTargetStructName,
                    out string addressError))
            {
                throw CreateAlarmException(
                    evt,
                    $"插入数据结构失败:{addressError}");
            }
            DataStructItem dataStructItem = new DataStructItem
            {
                Name = insertDataStructItem.ItemName ?? string.Empty,
                FieldNames = new Dictionary<int, string>(),
                FieldTypes = new Dictionary<int, DataStructValueType>(),
                str = new Dictionary<int, string>(),
                num = new Dictionary<int, double>()
            };
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < insertDataStructItem.Params.Count; i++)
            {
                InsertDataStructItemParam parameter = insertDataStructItem.Params[i];
                if (parameter == null)
                {
                    throw CreateAlarmException(evt, $"数据结构插入参数{i}为空");
                }
                string fieldName = parameter.FieldName?.Trim();
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    throw CreateAlarmException(
                        evt,
                        $"数据结构插入字段名称不能为空:参数{i}");
                }
                if (!fieldNames.Add(fieldName))
                {
                    throw CreateAlarmException(
                        evt,
                        $"数据结构插入字段名称重复:{fieldName}");
                }
                if (string.Equals(parameter.Type, "double", StringComparison.Ordinal))
                {
                    if (!double.TryParse(values[i], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double num)
                        || double.IsNaN(num) || double.IsInfinity(num))
                    {
                        throw CreateAlarmException(evt,
                            $"数据结构插入数值无效:{values[i]}");
                    }
                    dataStructItem.num[i] = num;
                    dataStructItem.FieldTypes[i] = DataStructValueType.Number;
                    dataStructItem.FieldNames[i] = fieldName;
                }
                else if (string.Equals(parameter.Type, "string", StringComparison.Ordinal))
                {
                    if (values[i] == null)
                    {
                        throw CreateAlarmException(evt, "数据结构插入文本值不能为空");
                    }
                    dataStructItem.str[i] = values[i];
                    dataStructItem.FieldTypes[i] = DataStructValueType.Text;
                    dataStructItem.FieldNames[i] = fieldName;
                }
                else
                {
                    throw CreateAlarmException(evt,
                        $"数据结构插入类型无效:{parameter.Type}");
                }
            }
            int targetStructIndex = ResolveDataStructIndex(evt,
                useTargetStructName,
                insertDataStructItem.TargetStructIndex,
                insertDataStructItem.TargetStructName,
                "插入数据结构失败:");
            int targetItemIndex = insertDataStructItem.TargetItemIndex;
            if (targetItemIndex < 0)
            {
                throw CreateAlarmException(evt,
                    $"数据结构插入位置无效:{targetItemIndex}");
            }
            if (!GetDataStructStore(evt).TryInsertItem(
                    targetStructIndex, targetItemIndex, dataStructItem))
            {
                throw CreateAlarmException(evt,
                    $"插入数据结构失败:结构{targetStructIndex},位置{targetItemIndex}");
            }
            return true;
        }


        public bool RunDelDataStructItem(ProcHandle evt, DelDataStructItem delDataStructItem)
        {
            if (delDataStructItem == null)
            {
                MarkAlarm(evt, "数据结构删除参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!TrySelectDataStructAddressMode(
                    delDataStructItem.TargetStructName,
                    delDataStructItem.TargetStructIndex,
                    "目标结构体",
                    out bool useTargetStructName,
                    out string addressError)
                || !TrySelectDataStructAddressMode(
                    delDataStructItem.TargetItemName,
                    delDataStructItem.TargetItemIndex,
                    "目标数据项",
                    out bool useTargetItemName,
                    out addressError))
            {
                throw CreateAlarmException(
                    evt,
                    $"删除数据结构失败:{addressError}");
            }
            int structIndex = ResolveDataStructIndex(evt,
                useTargetStructName,
                delDataStructItem.TargetStructIndex,
                delDataStructItem.TargetStructName,
                "删除数据结构失败:");
            int itemIndex = ResolveDataStructItemIndex(evt, structIndex,
                useTargetItemName,
                delDataStructItem.TargetItemIndex,
                delDataStructItem.TargetItemName,
                "删除数据结构失败:");
            bool success = GetDataStructStore(evt).TryRemoveItemAt(structIndex, itemIndex);
            if (!success)
            {
                throw CreateAlarmException(evt,
                    $"删除数据结构失败:结构{structIndex},项{itemIndex}");
            }
            return true;
        }

        public bool RunFindDataStructItem(ProcHandle evt, FindDataStructItem findDataStructItem)
        {
            string source = evt?.GetOperationSource();
            if (findDataStructItem == null)
            {
                MarkAlarm(evt, "数据结构查找参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            DataStructStore dataStructStore = GetDataStructStore(evt);
            ValueConfigStore valueStore = Context?.ValueStore;
            if (valueStore == null)
            {
                throw CreateAlarmException(evt, "变量库未初始化");
            }
            if (!TrySelectDataStructAddressMode(
                    findDataStructItem.TargetStructName,
                    findDataStructItem.TargetStructIndex,
                    "目标结构体",
                    out bool useTargetStructName,
                    out string addressError))
            {
                throw CreateAlarmException(
                    evt,
                    $"查找数据结构失败:{addressError}");
            }
            int targetStructIndex = ResolveDataStructIndex(evt,
                useTargetStructName,
                findDataStructItem.TargetStructIndex,
                findDataStructItem.TargetStructName,
                "查找数据结构失败:");
            object result;
            if (string.Equals(findDataStructItem.Type, "名称等于key", StringComparison.Ordinal))
            {
                if (!dataStructStore.TryFindItemByName(
                        targetStructIndex, findDataStructItem.Key, out string value))
                {
                    throw CreateAlarmException(evt,
                        $"查找数据结构失败:结构{targetStructIndex},key{findDataStructItem.Key}");
                }
                result = value;
            }
            else if (string.Equals(findDataStructItem.Type, "字符串等于key", StringComparison.Ordinal))
            {
                if (!dataStructStore.TryFindItemByStringValue(
                        targetStructIndex, findDataStructItem.Key, out string value))
                {
                    throw CreateAlarmException(evt,
                        $"查找数据结构失败:结构{targetStructIndex},key{findDataStructItem.Key}");
                }
                result = value;
            }
            else if (string.Equals(findDataStructItem.Type, "数值等于key", StringComparison.Ordinal))
            {
                if (!double.TryParse(findDataStructItem.Key, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double keyValue)
                    || double.IsNaN(keyValue) || double.IsInfinity(keyValue))
                {
                    throw CreateAlarmException(evt,
                        $"查找数值key无效:{findDataStructItem.Key}");
                }
                if (!dataStructStore.TryFindItemByNumberValue(
                        targetStructIndex, keyValue, out double value))
                {
                    throw CreateAlarmException(evt,
                        $"查找数据结构失败:结构{targetStructIndex},key{findDataStructItem.Key}");
                }
                result = value;
            }
            else
            {
                throw CreateAlarmException(evt,
                    $"数据结构查找类型无效:{findDataStructItem.Type}");
            }
            if (!valueStore.SetValueByNameForProcess(
                    findDataStructItem.ResultVariableName, result, evt.procId, source))
            {
                throw CreateAlarmException(evt,
                    $"保存变量失败:{findDataStructItem.ResultVariableName}");
            }
            return true;
        }

        public bool RunGetDataStructCount(ProcHandle evt, GetDataStructCount getDataStructCount)
        {
            string source = evt?.GetOperationSource();
            if (getDataStructCount == null)
            {
                MarkAlarm(evt, "数据结构计数参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            DataStructStore dataStructStore = GetDataStructStore(evt);
            ValueConfigStore valueStore = Context?.ValueStore;
            if (valueStore == null)
            {
                throw CreateAlarmException(evt, "变量库未初始化");
            }
            if (!TrySelectDataStructAddressMode(
                    getDataStructCount.TargetStructName,
                    getDataStructCount.TargetStructIndex,
                    "目标结构体",
                    out bool useTargetStructName,
                    out string addressError))
            {
                throw CreateAlarmException(
                    evt,
                    $"数据结构计数失败:{addressError}");
            }
            int targetStructIndex = ResolveDataStructIndex(evt,
                useTargetStructName,
                getDataStructCount.TargetStructIndex,
                getDataStructCount.TargetStructName,
                "数据结构计数失败:");
            if (!valueStore.TryGetValueByNameForProcess(
                    getDataStructCount.StructCountVariableName, evt.procId, out DicValue structCountVariable)
                || !valueStore.TryGetValueByNameForProcess(
                    getDataStructCount.ItemCountVariableName, evt.procId, out DicValue itemCountVariable))
            {
                throw CreateAlarmException(evt, "数据结构计数结果变量不存在或当前流程无权访问");
            }
            if (!valueStore.SetValueByIndexForProcess(
                    structCountVariable.Index, dataStructStore.Count, evt.procId, source)
                || !valueStore.SetValueByIndexForProcess(
                    itemCountVariable.Index, dataStructStore.GetItemCount(targetStructIndex), evt.procId, source))
            {
                throw CreateAlarmException(evt, "保存数据结构计数变量失败");
            }

            return true;
        }
    }
}
