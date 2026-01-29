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
using System.Windows.Forms;
using Automation.MotionControl;
using static Automation.FrmProc;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
using static Automation.FrmCard;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;
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
            if (!int.TryParse(setDataStructItem.StructIndex, out int structIndex))
            {
                MarkAlarm(evt, $"数据结构索引无效:{setDataStructItem.StructIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!int.TryParse(setDataStructItem.ItemIndex, out int itemIndex))
            {
                MarkAlarm(evt, $"数据结构项索引无效:{setDataStructItem.ItemIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!int.TryParse(setDataStructItem.Count, out int count) || count < 0)
            {
                MarkAlarm(evt, $"数据结构数量无效:{setDataStructItem.Count}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            for (int i = 0; i < count; i++)
            {
                if (i >= setDataStructItem.Params.Count)
                {
                    MarkAlarm(evt, "数据结构参数数量不足");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!int.TryParse(setDataStructItem.Params[i].valueIndex, out int valueIndex))
                {
                    MarkAlarm(evt, $"数据结构值索引无效:{setDataStructItem.Params[i].valueIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.DataStructStore.TrySetItemValueByIndex(structIndex, itemIndex, valueIndex, setDataStructItem.Params[i].value))
                {
                    MarkAlarm(evt, $"设置数据结构失败:结构{structIndex},项{itemIndex},值{valueIndex}");
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
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            if (!int.TryParse(getDataStructItem.StructIndex, out int structIndex))
            {
                MarkAlarm(evt, $"数据结构索引无效:{getDataStructItem.StructIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!int.TryParse(getDataStructItem.ItemIndex, out int itemIndex))
            {
                MarkAlarm(evt, $"数据结构项索引无效:{getDataStructItem.ItemIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (getDataStructItem.IsAllItem)
            {
                int startIndex = valueStore.GetValueByName(getDataStructItem.StartValue).Index;
                int count = Context.DataStructStore.GetItemValueCount(structIndex, itemIndex);
                for (int i = 0; i < count; i++)
                {
                    if (!Context.DataStructStore.TryGetItemValueByIndex(structIndex, itemIndex, i, out object obj))
                    {
                        MarkAlarm(evt, $"读取数据结构失败:结构{structIndex},项{itemIndex},值{i}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!valueStore.setValueByIndex(startIndex + i, obj, source))
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
                    if (!int.TryParse(getDataStructItem.Params[i].valueIndex, out int valueIndex))
                    {
                        MarkAlarm(evt, $"数据结构值索引无效:{getDataStructItem.Params[i].valueIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!Context.DataStructStore.TryGetItemValueByIndex(structIndex, itemIndex, valueIndex, out object obj))
                    {
                        MarkAlarm(evt, $"读取数据结构失败:结构{structIndex},项{itemIndex},值{valueIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    if (!ValueRef.TryCreate(getDataStructItem.Params[i].ValueIndex, null, getDataStructItem.Params[i].ValueName, null, false, "变量名称", out ValueRef nameRef, out string nameError))
                    {
                        throw CreateAlarmException(evt, nameError);
                    }
                    if (!nameRef.TryResolveValue(valueStore, "变量名称", out DicValue nameItem, out string nameResolveError))
                    {
                        throw CreateAlarmException(evt, nameResolveError);
                    }
                    string valueName = nameItem.Value;
                    if (!valueStore.setValueByName(valueName, obj, source))
                    {
                        MarkAlarm(evt, $"保存变量失败:{valueName}");
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
                if (!int.TryParse(copyDataStructItem.SourceStructIndex, out int sourceStructIndex)
                    || !int.TryParse(copyDataStructItem.SourceItemIndex, out int sourceItemIndex)
                    || !int.TryParse(copyDataStructItem.TargetStructIndex, out int targetStructIndex)
                    || !int.TryParse(copyDataStructItem.TargetItemIndex, out int targetItemIndex))
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
                    if (!int.TryParse(copyDataStructItem.SourceStructIndex, out int sourceStructIndex)
                        || !int.TryParse(copyDataStructItem.SourceItemIndex, out int sourceItemIndex)
                        || !int.TryParse(copyDataStructItem.Params[i].SourcevalueIndex, out int sourceValueIndex))
                    {
                        MarkAlarm(evt, "数据结构复制索引无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!Context.DataStructStore.TryGetItemValueByIndex(sourceStructIndex, sourceItemIndex, sourceValueIndex, out object obj, out DataStructValueType valueType))
                    {
                        MarkAlarm(evt, $"读取数据结构失败:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourcevalueIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    if (obj == null)
                    {
                        MarkAlarm(evt, $"数据结构值为空:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourcevalueIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    if (!int.TryParse(copyDataStructItem.TargetStructIndex, out int targetStructIndex)
                        || !int.TryParse(copyDataStructItem.TargetItemIndex, out int targetItemIndex)
                        || !int.TryParse(copyDataStructItem.Params[i].Targetvalue, out int targetValueIndex))
                    {
                        MarkAlarm(evt, "数据结构复制索引无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!Context.DataStructStore.TrySetItemValueByIndex(targetStructIndex, targetItemIndex, targetValueIndex, obj.ToString(), valueType))
                    {
                        MarkAlarm(evt, $"设置数据结构失败:结构{copyDataStructItem.TargetStructIndex},项{copyDataStructItem.TargetItemIndex},值{copyDataStructItem.Params[i].Targetvalue}");
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
                Name = insertDataStructItem.Name ?? string.Empty,
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
                    if (insertDataStructItem.Params[i].ValueItem != null)
                        num = Context.ValueStore.get_D_ValueByName(insertDataStructItem.Params[i].ValueItem);
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
                    if (insertDataStructItem.Params[i].ValueItem != null)
                        str = Context.ValueStore.get_Str_ValueByName(insertDataStructItem.Params[i].ValueItem);
                    else
                        str = insertDataStructItem.Params[i].Value.ToString();
                    dataStructItem.str[i] = str;
                    dataStructItem.FieldTypes[i] = DataStructValueType.Text;
                    dataStructItem.FieldNames[i] = $"字段{i}";
                }
            }
            if (!int.TryParse(insertDataStructItem.TargetStructIndex, out int targetStructIndex)
                || !int.TryParse(insertDataStructItem.TargetItemIndex, out int targetItemIndex))
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
            if (!int.TryParse(delDataStructItem.TargetStructIndex, out int structIndex)
                || !int.TryParse(delDataStructItem.TargetItemIndex, out int itemIndex))
            {
                MarkAlarm(evt, "数据结构删除索引无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            bool success;
            if (itemIndex >= 255)
            {
                success = Context.DataStructStore.TryRemoveLastItem(structIndex);
            }
            else if (itemIndex <= -1)
            {
                success = Context.DataStructStore.TryRemoveFirstItem(structIndex);
            }
            else
            {
                success = Context.DataStructStore.TryRemoveItemAt(structIndex, itemIndex);
            }
            if (!success)
            {
                MarkAlarm(evt, $"删除数据结构失败:结构{structIndex},项{itemIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            return true;
        }

        public bool RunFindDataStructItem(ProcHandle evt, FindDataStructItem findDataStructItem)
        {
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            if (findDataStructItem == null)
            {
                MarkAlarm(evt, "数据结构查找参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (findDataStructItem.Type == "名称等于key")
            {
                if (!int.TryParse(findDataStructItem.TargetStructIndex, out int targetStructIndex))
                {
                    MarkAlarm(evt, "数据结构查找索引无效");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.DataStructStore.TryFindItemByName(targetStructIndex, findDataStructItem.key, out string value))
                {
                    MarkAlarm(evt, $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.key}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.ValueStore.setValueByName(findDataStructItem.save, value, source))
                {
                    MarkAlarm(evt, $"保存变量失败:{findDataStructItem.save}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else if (findDataStructItem.Type == "字符串等于key")
            {
                if (!int.TryParse(findDataStructItem.TargetStructIndex, out int targetStructIndex))
                {
                    MarkAlarm(evt, "数据结构查找索引无效");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.DataStructStore.TryFindItemByStringValue(targetStructIndex, findDataStructItem.key, out string value))
                {
                    MarkAlarm(evt, $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.key}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.ValueStore.setValueByName(findDataStructItem.save, value, source))
                {
                    MarkAlarm(evt, $"保存变量失败:{findDataStructItem.save}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else if (findDataStructItem.Type == "数值等于key")
            {
                if (!double.TryParse(findDataStructItem.key, out double keyValue))
                {
                    MarkAlarm(evt, $"查找数值key无效:{findDataStructItem.key}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!int.TryParse(findDataStructItem.TargetStructIndex, out int targetStructIndex))
                {
                    MarkAlarm(evt, "数据结构查找索引无效");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.DataStructStore.TryFindItemByNumberValue(targetStructIndex, keyValue, out double value))
                {
                    MarkAlarm(evt, $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.key}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.ValueStore.setValueByName(findDataStructItem.save, value, source))
                {
                    MarkAlarm(evt, $"保存变量失败:{findDataStructItem.save}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            return true;
        }

        public bool RunGetDataStructCount(ProcHandle evt, GetDataStructCount getDataStructCount)
        {
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            if (getDataStructCount == null)
            {
                MarkAlarm(evt, "数据结构计数参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!Context.ValueStore.setValueByName(getDataStructCount.StructCount, Context.DataStructStore.Count, source))
            {
                MarkAlarm(evt, $"保存变量失败:{getDataStructCount.StructCount}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!int.TryParse(getDataStructCount.TargetStructIndex, out int targetStructIndex))
            {
                MarkAlarm(evt, "数据结构计数索引无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!Context.ValueStore.setValueByName(getDataStructCount.ItemCount, Context.DataStructStore.GetItemCount(targetStructIndex), source))
            {
                MarkAlarm(evt, $"保存变量失败:{getDataStructCount.ItemCount}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            return true;
        }
    }
}
