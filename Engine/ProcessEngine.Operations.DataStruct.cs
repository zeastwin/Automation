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
                    evt.isAlarm = true;
                    evt.alarmMsg = $"设置数据结构失败:结构{structIndex},项{itemIndex},值{valueIndex}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
            }
            return true;
        }

        public bool RunGetDataStructItem(ProcHandle evt, GetDataStructItem getDataStructItem)
        {

            int structIndex = int.Parse(getDataStructItem.StructIndex);
            int itemIndex = int.Parse(getDataStructItem.ItemIndex);
            if (getDataStructItem.IsAllItem)
            {
                int startIndex = Context.ValueStore.GetValueByName(getDataStructItem.StartValue).Index;
                int count = Context.DataStructStore.GetItemValueCount(structIndex, itemIndex);
                for (int i = 0; i < count; i++)
                {
                    if (!Context.DataStructStore.TryGetItemValueByIndex(structIndex, itemIndex, i, out object obj))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"读取数据结构失败:结构{structIndex},项{itemIndex},值{i}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }
                    if (!Context.ValueStore.setValueByIndex(startIndex + i, obj))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存变量失败:索引{startIndex + i}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                        evt.isAlarm = true;
                        evt.alarmMsg = $"读取数据结构失败:结构{structIndex},项{itemIndex},值{valueIndex}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }

                    string valueName = "";
                    if (!string.IsNullOrEmpty(getDataStructItem.Params[i].ValueIndex))
                        valueName = Context.ValueStore.GetValueByIndex(int.Parse(getDataStructItem.Params[i].ValueIndex)).Value.ToString();
                    else
                        valueName = Context.ValueStore.GetValueByName(getDataStructItem.Params[i].ValueName).Value.ToString();

                    if (!Context.ValueStore.setValueByName(valueName, obj))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存变量失败:{valueName}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                    evt.isAlarm = true;
                    evt.alarmMsg = $"复制数据结构失败:源{copyDataStructItem.SourceStructIndex}-{copyDataStructItem.SourceItemIndex},目标{copyDataStructItem.TargetStructIndex}-{copyDataStructItem.TargetItemIndex}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
            }
            else
            {
                for (int i = 0; i < copyDataStructItem.Params.Count; i++)
                {
                    if (!Context.DataStructStore.TryGetItemValueByIndex(int.Parse(copyDataStructItem.SourceStructIndex), int.Parse(copyDataStructItem.SourceItemIndex), int.Parse(copyDataStructItem.Params[i].SourcevalueIndex), out object obj, out DataStructValueType valueType))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"读取数据结构失败:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourcevalueIndex}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }

                    if (obj == null)
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"数据结构值为空:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourcevalueIndex}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }

                    if (!Context.DataStructStore.TrySetItemValueByIndex(int.Parse(copyDataStructItem.TargetStructIndex), int.Parse(copyDataStructItem.TargetItemIndex), int.Parse(copyDataStructItem.Params[i].Targetvalue), obj.ToString(), valueType))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"设置数据结构失败:结构{copyDataStructItem.TargetStructIndex},项{copyDataStructItem.TargetItemIndex},值{copyDataStructItem.Params[i].Targetvalue}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }

                }
            }
            return true;
        }
        public bool RunInsertDataStructItem(ProcHandle evt, InsertDataStructItem insertDataStructItem)
        {
            DataStructItem dataStructItem = new DataStructItem
            {
                Name = insertDataStructItem.Name
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
                }
                else
                {
                    string str = "";
                    if (insertDataStructItem.Params[i].ValueItem != null)
                        str = Context.ValueStore.get_Str_ValueByName(insertDataStructItem.Params[i].ValueItem);
                    else
                        str = insertDataStructItem.Params[i].Value.ToString();
                    dataStructItem.str[i] = str;
                }
            }
            if (!Context.DataStructStore.TryInsertItem(int.Parse(insertDataStructItem.TargetStructIndex), int.Parse(insertDataStructItem.TargetItemIndex), dataStructItem))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"插入数据结构失败:结构{insertDataStructItem.TargetStructIndex},项{insertDataStructItem.TargetItemIndex}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                evt.isAlarm = true;
                evt.alarmMsg = $"删除数据结构失败:结构{structIndex},项{itemIndex}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            return true;
        }

        public bool RunFindDataStructItem(ProcHandle evt, FindDataStructItem findDataStructItem)
        {
            if (findDataStructItem.Type == "名称等于key")
            {
                if (!Context.DataStructStore.TryFindItemByName(int.Parse(findDataStructItem.TargetStructIndex), findDataStructItem.key, out string value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.key}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                if (!Context.ValueStore.setValueByName(findDataStructItem.save, value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:{findDataStructItem.save}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
            }
            else if (findDataStructItem.Type == "字符串等于key")
            {
                if (!Context.DataStructStore.TryFindItemByStringValue(int.Parse(findDataStructItem.TargetStructIndex), findDataStructItem.key, out string value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.key}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                if (!Context.ValueStore.setValueByName(findDataStructItem.save, value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:{findDataStructItem.save}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
            }
            else if (findDataStructItem.Type == "数值等于key")
            {
                if (!double.TryParse(findDataStructItem.key, out double keyValue))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"查找数值key无效:{findDataStructItem.key}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                if (!Context.DataStructStore.TryFindItemByNumberValue(int.Parse(findDataStructItem.TargetStructIndex), keyValue, out double value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.key}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                if (!Context.ValueStore.setValueByName(findDataStructItem.save, value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:{findDataStructItem.save}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
            }
            return true;
        }

        public bool RunGetDataStructCount(ProcHandle evt, GetDataStructCount getDataStructCount)
        {
            if (!Context.ValueStore.setValueByName(getDataStructCount.StructCount, Context.DataStructStore.Count))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"保存变量失败:{getDataStructCount.StructCount}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            if (!Context.ValueStore.setValueByName(getDataStructCount.ItemCount, Context.DataStructStore.GetItemCount(int.Parse(getDataStructCount.TargetStructIndex))))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"保存变量失败:{getDataStructCount.ItemCount}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }

            return true;
        }
    }
}
