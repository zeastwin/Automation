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
            int structIndex = int.Parse(setDataStructItem.StructIndex);
            int itemIndex = int.Parse(setDataStructItem.ItemIndex);
            for (int i = 0; i < int.Parse(setDataStructItem.Count); i++)
            {
                int valueIndex = int.Parse(setDataStructItem.Params[i].valueIndex);
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
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            int structIndex = int.Parse(getDataStructItem.StructIndex);
            int itemIndex = int.Parse(getDataStructItem.ItemIndex);
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
                for (int i = 0; i < getDataStructItem.Params.Count; i++)
                {
                    int valueIndex = int.Parse(getDataStructItem.Params[i].valueIndex);
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
            if (copyDataStructItem.IsAllValue)
            {
                if (!Context.DataStructStore.TryCopyItemAll(int.Parse(copyDataStructItem.SourceStructIndex), int.Parse(copyDataStructItem.SourceItemIndex), int.Parse(copyDataStructItem.TargetStructIndex), int.Parse(copyDataStructItem.TargetItemIndex)))
                {
                    MarkAlarm(evt, $"复制数据结构失败:源{copyDataStructItem.SourceStructIndex}-{copyDataStructItem.SourceItemIndex},目标{copyDataStructItem.TargetStructIndex}-{copyDataStructItem.TargetItemIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else
            {
                for (int i = 0; i < copyDataStructItem.Params.Count; i++)
                {
                    if (!Context.DataStructStore.TryGetItemValueByIndex(int.Parse(copyDataStructItem.SourceStructIndex), int.Parse(copyDataStructItem.SourceItemIndex), int.Parse(copyDataStructItem.Params[i].SourcevalueIndex), out object obj, out DataStructValueType valueType))
                    {
                        MarkAlarm(evt, $"读取数据结构失败:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourcevalueIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    if (obj == null)
                    {
                        MarkAlarm(evt, $"数据结构值为空:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourcevalueIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    if (!Context.DataStructStore.TrySetItemValueByIndex(int.Parse(copyDataStructItem.TargetStructIndex), int.Parse(copyDataStructItem.TargetItemIndex), int.Parse(copyDataStructItem.Params[i].Targetvalue), obj.ToString(), valueType))
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
                        num = double.Parse(insertDataStructItem.Params[i].Value);
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
            if (!Context.DataStructStore.TryInsertItem(int.Parse(insertDataStructItem.TargetStructIndex), int.Parse(insertDataStructItem.TargetItemIndex), dataStructItem))
            {
                MarkAlarm(evt, $"插入数据结构失败:结构{insertDataStructItem.TargetStructIndex},项{insertDataStructItem.TargetItemIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            return true;
        }


        public bool RunDelDataStructItem(ProcHandle evt, DelDataStructItem delDataStructItem)
        {
            int structIndex = int.Parse(delDataStructItem.TargetStructIndex);
            int itemIndex = int.Parse(delDataStructItem.TargetItemIndex);
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
            if (findDataStructItem.Type == "名称等于key")
            {
                if (!Context.DataStructStore.TryFindItemByName(int.Parse(findDataStructItem.TargetStructIndex), findDataStructItem.key, out string value))
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
                if (!Context.DataStructStore.TryFindItemByStringValue(int.Parse(findDataStructItem.TargetStructIndex), findDataStructItem.key, out string value))
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
                if (!Context.DataStructStore.TryFindItemByNumberValue(int.Parse(findDataStructItem.TargetStructIndex), keyValue, out double value))
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
            if (!Context.ValueStore.setValueByName(getDataStructCount.StructCount, Context.DataStructStore.Count, source))
            {
                MarkAlarm(evt, $"保存变量失败:{getDataStructCount.StructCount}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!Context.ValueStore.setValueByName(getDataStructCount.ItemCount, Context.DataStructStore.GetItemCount(int.Parse(getDataStructCount.TargetStructIndex)), source))
            {
                MarkAlarm(evt, $"保存变量失败:{getDataStructCount.ItemCount}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            return true;
        }
    }
}
