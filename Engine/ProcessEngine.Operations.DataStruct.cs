using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Automation.MotionControl;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Numerics;

namespace Automation
{
    public partial class ProcessEngine
    {
        public bool RunSetDataStructItem(ProcHandle evt, SetDataStructItem setDataStructItem)
        {
            if (setDataStructItem == null || setDataStructItem.Params == null)
            {
                MarkAlarm(evt, "数据结构设置参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            int structIndex = setDataStructItem.StructIndex;
            if (structIndex < 0)
            {
                MarkAlarm(evt, $"数据结构索引无效:{setDataStructItem.StructIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            int itemIndex = setDataStructItem.ItemIndex;
            if (itemIndex < 0)
            {
                MarkAlarm(evt, $"数据结构项索引无效:{setDataStructItem.ItemIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            for (int i = 0; i < setDataStructItem.Params.Count; i++)
            {
                int fieldIndex = setDataStructItem.Params[i].FieldIndex;
                if (fieldIndex < 0)
                {
                    MarkAlarm(evt, $"数据结构值索引无效:{setDataStructItem.Params[i].FieldIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.DataStructStore.TrySetItemValueByIndex(structIndex, itemIndex, fieldIndex, setDataStructItem.Params[i].Value))
                {
                    MarkAlarm(evt, $"设置数据结构失败:结构{structIndex},项{itemIndex},值{fieldIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
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
            string source = evt?.GetOperationSource();
            int structIndex = getDataStructItem.StructIndex;
            if (structIndex < 0)
            {
                MarkAlarm(evt, $"数据结构索引无效:{getDataStructItem.StructIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            int itemIndex = getDataStructItem.ItemIndex;
            if (itemIndex < 0)
            {
                MarkAlarm(evt, $"数据结构项索引无效:{getDataStructItem.ItemIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (getDataStructItem.IsAllItem)
            {
                int startIndex = valueStore.GetValueByNameForProcess(getDataStructItem.FirstResultVariableName, evt.procId).Index;
                int count = Context.DataStructStore.GetItemValueCount(structIndex, itemIndex);
                for (int i = 0; i < count; i++)
                {
                    if (!Context.DataStructStore.TryGetItemValueByIndex(structIndex, itemIndex, i, out object obj))
                    {
                        MarkAlarm(evt, $"读取数据结构失败:结构{structIndex},项{itemIndex},值{i}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!valueStore.SetValueByIndexForProcess(startIndex + i, obj, evt.procId, source))
                    {
                        MarkAlarm(evt, $"保存变量失败:索引{startIndex + i}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
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
                for (int i = 0; i < getDataStructItem.Params.Count; i++)
                {
                    int fieldIndex = getDataStructItem.Params[i].FieldIndex;
                    if (fieldIndex < 0)
                    {
                        MarkAlarm(evt, $"数据结构值索引无效:{getDataStructItem.Params[i].FieldIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!Context.DataStructStore.TryGetItemValueByIndex(structIndex, itemIndex, fieldIndex, out object obj))
                    {
                        MarkAlarm(evt, $"读取数据结构失败:结构{structIndex},项{itemIndex},值{fieldIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    if (!ValueRef.TryCreate(getDataStructItem.Params[i].OutputValueIndex, null, getDataStructItem.Params[i].OutputValueName, null, false, "结果变量", out ValueRef outputRef, out string outputError))
                    {
                        throw CreateAlarmException(evt, outputError);
                    }
                    if (!outputRef.TryResolveValue(valueStore, "结果变量", evt.procId, out DicValue outputItem, out string outputResolveError))
                    {
                        throw CreateAlarmException(evt, outputResolveError);
                    }
                    if (!valueStore.SetValueByIndexForProcess(outputItem.Index, obj, evt.procId, source))
                    {
                        string outputName = string.IsNullOrWhiteSpace(outputItem.Name)
                            ? $"索引{outputItem.Index}"
                            : outputItem.Name;
                        MarkAlarm(evt, $"保存变量失败:{outputName}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
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
            if (copyDataStructItem.IsAllValue)
            {
                int sourceStructIndex = copyDataStructItem.SourceStructIndex;
                int sourceItemIndex = copyDataStructItem.SourceItemIndex;
                int targetStructIndex = copyDataStructItem.TargetStructIndex;
                int targetItemIndex = copyDataStructItem.TargetItemIndex;
                if (sourceStructIndex < 0 || sourceItemIndex < 0
                    || targetStructIndex < 0 || targetItemIndex < 0)
                {
                    MarkAlarm(evt, "数据结构复制索引无效");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.DataStructStore.TryCopyItemAll(sourceStructIndex, sourceItemIndex, targetStructIndex, targetItemIndex))
                {
                    MarkAlarm(evt, $"复制数据结构失败:源{copyDataStructItem.SourceStructIndex}-{copyDataStructItem.SourceItemIndex},目标{copyDataStructItem.TargetStructIndex}-{copyDataStructItem.TargetItemIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else
            {
                if (copyDataStructItem.Params == null)
                {
                    MarkAlarm(evt, "数据结构复制参数为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                for (int i = 0; i < copyDataStructItem.Params.Count; i++)
                {
                    int sourceStructIndex = copyDataStructItem.SourceStructIndex;
                    int sourceItemIndex = copyDataStructItem.SourceItemIndex;
                    int sourceValueIndex = copyDataStructItem.Params[i].SourceFieldIndex;
                    if (sourceStructIndex < 0 || sourceItemIndex < 0 || sourceValueIndex < 0)
                    {
                        MarkAlarm(evt, "数据结构复制索引无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!Context.DataStructStore.TryGetItemValueByIndex(sourceStructIndex, sourceItemIndex, sourceValueIndex, out object obj, out DataStructValueType valueType))
                    {
                        MarkAlarm(evt, $"读取数据结构失败:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourceFieldIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    if (obj == null)
                    {
                        MarkAlarm(evt, $"数据结构值为空:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourceFieldIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    int targetStructIndex = copyDataStructItem.TargetStructIndex;
                    int targetItemIndex = copyDataStructItem.TargetItemIndex;
                    int targetValueIndex = copyDataStructItem.Params[i].TargetFieldIndex;
                    if (targetStructIndex < 0 || targetItemIndex < 0 || targetValueIndex < 0)
                    {
                        MarkAlarm(evt, "数据结构复制索引无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!Context.DataStructStore.TrySetItemValueByIndex(targetStructIndex, targetItemIndex, targetValueIndex, obj.ToString(), valueType))
                    {
                        MarkAlarm(evt, $"设置数据结构失败:结构{copyDataStructItem.TargetStructIndex},项{copyDataStructItem.TargetItemIndex},值{copyDataStructItem.Params[i].TargetFieldIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

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
                if (insertDataStructItem.Params[i].Type == "double")
                {
                    double num = -1;
                    if (insertDataStructItem.Params[i].ValueVariableName != null)
                        num = Context.ValueStore.GetValueByNameForProcess(
                            insertDataStructItem.Params[i].ValueVariableName, evt.procId).GetDValue();
                    else
                        if (!double.TryParse(insertDataStructItem.Params[i].Value, out num))
                        {
                            MarkAlarm(evt, $"数据结构插入数值无效:{insertDataStructItem.Params[i].Value}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                    dataStructItem.num[i] = num;
                    dataStructItem.FieldTypes[i] = DataStructValueType.Number;
                    dataStructItem.FieldNames[i] = $"字段{i}";
                }
                else
                {
                    string str = "";
                    if (insertDataStructItem.Params[i].ValueVariableName != null)
                        str = Context.ValueStore.GetValueByNameForProcess(
                            insertDataStructItem.Params[i].ValueVariableName, evt.procId).GetCValue();
                    else
                        str = insertDataStructItem.Params[i].Value.ToString();
                    dataStructItem.str[i] = str;
                    dataStructItem.FieldTypes[i] = DataStructValueType.Text;
                    dataStructItem.FieldNames[i] = $"字段{i}";
                }
            }
            int targetStructIndex = insertDataStructItem.TargetStructIndex;
            int targetItemIndex = insertDataStructItem.TargetItemIndex;
            if (targetStructIndex < 0 || targetItemIndex < 0)
            {
                MarkAlarm(evt, "数据结构插入索引无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!Context.DataStructStore.TryInsertItem(targetStructIndex, targetItemIndex, dataStructItem))
            {
                MarkAlarm(evt, $"插入数据结构失败:结构{insertDataStructItem.TargetStructIndex},项{insertDataStructItem.TargetItemIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
            int structIndex = delDataStructItem.TargetStructIndex;
            int itemIndex = delDataStructItem.TargetItemIndex;
            if (structIndex < 0 || itemIndex < 0)
            {
                MarkAlarm(evt, "数据结构删除索引无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            bool success = Context.DataStructStore.TryRemoveItemAt(structIndex, itemIndex);
            if (!success)
            {
                MarkAlarm(evt, $"删除数据结构失败:结构{structIndex},项{itemIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
            if (findDataStructItem.Type == "名称等于key")
            {
                int targetStructIndex = findDataStructItem.TargetStructIndex;
                if (targetStructIndex < 0)
                {
                    MarkAlarm(evt, "数据结构查找索引无效");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.DataStructStore.TryFindItemByName(targetStructIndex, findDataStructItem.Key, out string value))
                {
                    MarkAlarm(evt, $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.Key}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.ValueStore.SetValueByNameForProcess(findDataStructItem.ResultVariableName, value, evt.procId, source))
                {
                    MarkAlarm(evt, $"保存变量失败:{findDataStructItem.ResultVariableName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else if (findDataStructItem.Type == "字符串等于key")
            {
                int targetStructIndex = findDataStructItem.TargetStructIndex;
                if (targetStructIndex < 0)
                {
                    MarkAlarm(evt, "数据结构查找索引无效");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.DataStructStore.TryFindItemByStringValue(targetStructIndex, findDataStructItem.Key, out string value))
                {
                    MarkAlarm(evt, $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.Key}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.ValueStore.SetValueByNameForProcess(findDataStructItem.ResultVariableName, value, evt.procId, source))
                {
                    MarkAlarm(evt, $"保存变量失败:{findDataStructItem.ResultVariableName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else if (findDataStructItem.Type == "数值等于key")
            {
                if (!double.TryParse(findDataStructItem.Key, out double keyValue))
                {
                    MarkAlarm(evt, $"查找数值key无效:{findDataStructItem.Key}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                int targetStructIndex = findDataStructItem.TargetStructIndex;
                if (targetStructIndex < 0)
                {
                    MarkAlarm(evt, "数据结构查找索引无效");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.DataStructStore.TryFindItemByNumberValue(targetStructIndex, keyValue, out double value))
                {
                    MarkAlarm(evt, $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.Key}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.ValueStore.SetValueByNameForProcess(findDataStructItem.ResultVariableName, value, evt.procId, source))
                {
                    MarkAlarm(evt, $"保存变量失败:{findDataStructItem.ResultVariableName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
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
            if (!Context.ValueStore.SetValueByNameForProcess(getDataStructCount.StructCountVariableName, Context.DataStructStore.Count, evt.procId, source))
            {
                MarkAlarm(evt, $"保存变量失败:{getDataStructCount.StructCountVariableName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            int targetStructIndex = getDataStructCount.TargetStructIndex;
            if (targetStructIndex < 0)
            {
                MarkAlarm(evt, "数据结构计数索引无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!Context.ValueStore.SetValueByNameForProcess(getDataStructCount.ItemCountVariableName, Context.DataStructStore.GetItemCount(targetStructIndex), evt.procId, source))
            {
                MarkAlarm(evt, $"保存变量失败:{getDataStructCount.ItemCountVariableName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            return true;
        }
    }
}
