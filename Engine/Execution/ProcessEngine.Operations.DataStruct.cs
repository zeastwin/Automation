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

        public bool RunSetDataStructItem(ProcHandle evt, SetDataStructItem setDataStructItem)
        {
            if (setDataStructItem == null || setDataStructItem.Params == null)
            {
                MarkAlarm(evt, "数据结构设置参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            int structIndex = ResolveDataStructIndex(evt,
                setDataStructItem.UseNameAddressing,
                setDataStructItem.StructIndex,
                setDataStructItem.StructName,
                "设置数据结构失败:");
            int itemIndex = ResolveDataStructItemIndex(evt, structIndex,
                setDataStructItem.UseNameAddressing,
                setDataStructItem.ItemIndex,
                setDataStructItem.ItemName,
                "设置数据结构失败:");
            if (setDataStructItem.Params.Count == 1)
            {
                SetDataStructItemParam parameter = setDataStructItem.Params[0];
                if (parameter == null)
                {
                    throw CreateAlarmException(evt, "设置数据结构失败:参数0为空");
                }
                int fieldIndex = ResolveDataStructFieldIndex(
                    evt, structIndex, itemIndex,
                    setDataStructItem.UseNameAddressing,
                    parameter.FieldIndex,
                    parameter.FieldName,
                    "设置数据结构失败:");
                if (!GetDataStructStore(evt).TrySetItemValueByIndex(
                        structIndex, itemIndex, fieldIndex, parameter.Value))
                {
                    throw CreateAlarmException(evt,
                        $"设置数据结构失败:结构{structIndex},项{itemIndex},字段不存在、类型不匹配或值无效:{fieldIndex}");
                }
                return true;
            }
            var updates = new List<DataStructFieldValueUpdate>();
            for (int i = 0; i < setDataStructItem.Params.Count; i++)
            {
                SetDataStructItemParam parameter = setDataStructItem.Params[i];
                if (parameter == null)
                {
                    throw CreateAlarmException(evt, $"设置数据结构失败:参数{i}为空");
                }
                int fieldIndex = ResolveDataStructFieldIndex(evt, structIndex, itemIndex,
                    setDataStructItem.UseNameAddressing,
                    parameter.FieldIndex,
                    parameter.FieldName,
                    "设置数据结构失败:");
                updates.Add(new DataStructFieldValueUpdate
                {
                    FieldIndex = fieldIndex,
                    Value = parameter.Value
                });
            }
            if (!GetDataStructStore(evt).TrySetItemValuesByIndex(
                    structIndex, itemIndex, updates, out string updateError))
            {
                throw CreateAlarmException(evt,
                    $"设置数据结构失败:结构{structIndex},项{itemIndex},{updateError}");
            }
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
                        evt.Proc, evt.procNum, valueStore, out bindError)
                    : ProcessRuntimeBinder.TryBindStandalone(
                        evt?.procId ?? Guid.Empty,
                        valueStore, getDataStructItem, out bindError)))
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
            int structIndex = ResolveDataStructIndex(evt,
                getDataStructItem.UseNameAddressing,
                getDataStructItem.StructIndex,
                getDataStructItem.StructName,
                "读取数据结构失败:");
            int itemIndex = ResolveDataStructItemIndex(evt, structIndex,
                getDataStructItem.UseNameAddressing,
                getDataStructItem.ItemIndex,
                getDataStructItem.ItemName,
                "读取数据结构失败:");
            if (getDataStructItem.IsAllItem)
            {
                if (!binding.FirstOutput.TryResolveValue(
                        valueStore, "首个结果变量", evt.procId,
                        out DicValue firstResult, out string firstResultError))
                {
                    throw CreateAlarmException(evt,
                        firstResultError);
                }
                List<int> fieldIndexes = dataStructStore.GetItemValueIndexes(structIndex, itemIndex);
                if (!dataStructStore.TryGetItemValuesByIndex(
                        structIndex, itemIndex, fieldIndexes,
                        out List<DataStructFieldValueSnapshot> fieldValues,
                        out string readError))
                {
                    throw CreateAlarmException(evt,
                        $"读取数据结构失败:结构{structIndex},项{itemIndex},{readError}");
                }
                for (int i = 0; i < fieldIndexes.Count; i++)
                {
                    if (!valueStore.TryGetValueByIndexForProcess(
                            firstResult.Index + i, evt.procId, out _))
                    {
                        throw CreateAlarmException(evt,
                            $"连续结果变量不存在或当前流程无权访问:索引{firstResult.Index + i}");
                    }
                }
                for (int i = 0; i < fieldValues.Count; i++)
                {
                    if (!valueStore.SetValueByIndexForProcess(
                            firstResult.Index + i, fieldValues[i].Value, evt.procId, source))
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
                    GetDataStructItemParam parameter = getDataStructItem.Params[0];
                    if (parameter == null)
                    {
                        throw CreateAlarmException(evt, "读取数据结构失败:参数0为空");
                    }
                    int fieldIndex = ResolveDataStructFieldIndex(
                        evt, structIndex, itemIndex,
                        getDataStructItem.UseNameAddressing,
                        parameter.FieldIndex,
                        parameter.FieldName,
                        "读取数据结构失败:");
                    if (binding.Outputs.Length != 1)
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
                    if (!dataStructStore.TryGetItemValueByIndex(
                            structIndex, itemIndex, fieldIndex, out object outputValue))
                    {
                        throw CreateAlarmException(evt,
                            $"读取数据结构失败:结构{structIndex},项{itemIndex},字段{fieldIndex}");
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
                var fieldIndexes = new List<int>();
                if (binding.Outputs.Length != getDataStructItem.Params.Count)
                {
                    throw CreateAlarmException(evt, "数据结构读取运行计划未编译");
                }
                for (int i = 0; i < getDataStructItem.Params.Count; i++)
                {
                    GetDataStructItemParam parameter = getDataStructItem.Params[i];
                    if (parameter == null)
                    {
                        throw CreateAlarmException(evt, $"读取数据结构失败:参数{i}为空");
                    }
                    int fieldIndex = ResolveDataStructFieldIndex(evt, structIndex, itemIndex,
                        getDataStructItem.UseNameAddressing,
                        parameter.FieldIndex,
                        parameter.FieldName,
                        "读取数据结构失败:");
                    if (!binding.Outputs[i].TryResolveValue(
                            valueStore, "结果变量", evt.procId,
                            out DicValue outputItem, out string outputResolveError))
                    {
                        throw CreateAlarmException(evt, outputResolveError);
                    }
                    fieldIndexes.Add(fieldIndex);
                    outputItems.Add(outputItem);
                }
                if (!dataStructStore.TryGetItemValuesByIndex(
                        structIndex, itemIndex, fieldIndexes,
                        out List<DataStructFieldValueSnapshot> outputValues,
                        out string readError))
                {
                    throw CreateAlarmException(evt,
                        $"读取数据结构失败:结构{structIndex},项{itemIndex},{readError}");
                }
                for (int i = 0; i < outputValues.Count; i++)
                {
                    if (!valueStore.SetResolvedValueForProcess(
                            outputItems[i], outputValues[i].Value, evt.procId, source))
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
            int sourceStructIndex = ResolveDataStructIndex(evt,
                copyDataStructItem.UseSourceNameAddressing,
                copyDataStructItem.SourceStructIndex,
                copyDataStructItem.SourceStructName,
                "复制数据结构失败:源");
            int sourceItemIndex = ResolveDataStructItemIndex(evt, sourceStructIndex,
                copyDataStructItem.UseSourceNameAddressing,
                copyDataStructItem.SourceItemIndex,
                copyDataStructItem.SourceItemName,
                "复制数据结构失败:源");
            int targetStructIndex = ResolveDataStructIndex(evt,
                copyDataStructItem.UseTargetNameAddressing,
                copyDataStructItem.TargetStructIndex,
                copyDataStructItem.TargetStructName,
                "复制数据结构失败:目标");
            int targetItemIndex = ResolveDataStructItemIndex(evt, targetStructIndex,
                copyDataStructItem.UseTargetNameAddressing,
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
                    int sourceFieldIndex = ResolveDataStructFieldIndex(evt,
                        sourceStructIndex, sourceItemIndex,
                        copyDataStructItem.UseSourceNameAddressing,
                        parameter.SourceFieldIndex,
                        parameter.SourceFieldName,
                        "复制数据结构失败:源");
                    int targetFieldIndex = ResolveDataStructFieldIndex(evt,
                        targetStructIndex, targetItemIndex,
                        copyDataStructItem.UseTargetNameAddressing,
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
            DataStructItem dataStructItem = new DataStructItem
            {
                Name = insertDataStructItem.ItemName ?? string.Empty,
                FieldNames = new Dictionary<int, string>(),
                FieldTypes = new Dictionary<int, DataStructValueType>(),
                str = new Dictionary<int, string>(),
                num = new Dictionary<int, double>()
            };
            for (int i = 0; i < insertDataStructItem.Params.Count; i++)
            {
                InsertDataStructItemParam parameter = insertDataStructItem.Params[i];
                if (parameter == null)
                {
                    throw CreateAlarmException(evt, $"数据结构插入参数{i}为空");
                }
                if (string.Equals(parameter.Type, "double", StringComparison.Ordinal))
                {
                    double num;
                    if (!string.IsNullOrWhiteSpace(parameter.ValueVariableName))
                    {
                        if (Context?.ValueStore == null
                            || !Context.ValueStore.TryGetValueByNameForProcess(
                                parameter.ValueVariableName, evt.procId, out DicValue variable))
                        {
                            throw CreateAlarmException(evt,
                                $"数据结构插入变量不存在或当前流程无权访问:{parameter.ValueVariableName}");
                        }
                        num = variable.GetDValue();
                    }
                    else
                    {
                        if (!double.TryParse(parameter.Value, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out num)
                            || double.IsNaN(num) || double.IsInfinity(num))
                        {
                            throw CreateAlarmException(evt,
                                $"数据结构插入数值无效:{parameter.Value}");
                        }
                    }
                    if (double.IsNaN(num) || double.IsInfinity(num))
                    {
                        throw CreateAlarmException(evt,
                            $"数据结构插入数值必须是有限数:{parameter.ValueVariableName}");
                    }
                    dataStructItem.num[i] = num;
                    dataStructItem.FieldTypes[i] = DataStructValueType.Number;
                    dataStructItem.FieldNames[i] = $"字段{i}";
                }
                else if (string.Equals(parameter.Type, "string", StringComparison.Ordinal))
                {
                    string str;
                    if (!string.IsNullOrWhiteSpace(parameter.ValueVariableName))
                    {
                        if (Context?.ValueStore == null
                            || !Context.ValueStore.TryGetValueByNameForProcess(
                                parameter.ValueVariableName, evt.procId, out DicValue variable))
                        {
                            throw CreateAlarmException(evt,
                                $"数据结构插入变量不存在或当前流程无权访问:{parameter.ValueVariableName}");
                        }
                        str = variable.GetCValue();
                    }
                    else
                    {
                        if (parameter.Value == null)
                        {
                            throw CreateAlarmException(evt, "数据结构插入文本值不能为空");
                        }
                        str = parameter.Value;
                    }
                    dataStructItem.str[i] = str;
                    dataStructItem.FieldTypes[i] = DataStructValueType.Text;
                    dataStructItem.FieldNames[i] = $"字段{i}";
                }
                else
                {
                    throw CreateAlarmException(evt,
                        $"数据结构插入类型无效:{parameter.Type}");
                }
            }
            int targetStructIndex = ResolveDataStructIndex(evt,
                insertDataStructItem.UseStructNameAddressing,
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
            int structIndex = ResolveDataStructIndex(evt,
                delDataStructItem.UseNameAddressing,
                delDataStructItem.TargetStructIndex,
                delDataStructItem.TargetStructName,
                "删除数据结构失败:");
            int itemIndex = ResolveDataStructItemIndex(evt, structIndex,
                delDataStructItem.UseNameAddressing,
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
            int targetStructIndex = ResolveDataStructIndex(evt,
                findDataStructItem.UseStructNameAddressing,
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
            int targetStructIndex = ResolveDataStructIndex(evt,
                getDataStructCount.UseStructNameAddressing,
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
