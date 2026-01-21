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
        public bool RunGetValue(ProcHandle evt, GetValue getValue)
        {
            foreach (var item in getValue.Params)
            {
                //==============================================Get=====================================//
                string value = "";
                if (!string.IsNullOrEmpty(item.ValueSourceIndex))
                    value = Context.ValueStore.GetValueByIndex(int.Parse(item.ValueSourceIndex)).Value.ToString();
                else if (!string.IsNullOrEmpty(item.ValueSourceIndex2Index))
                {
                    string index = Context.ValueStore.GetValueByIndex(int.Parse(item.ValueSourceIndex2Index)).Value.ToString();
                    value = Context.ValueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(item.ValueSourceName))
                {
                    value = Context.ValueStore.GetValueByName(item.ValueSourceName).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(item.ValueSourceName2Index))
                {
                    string index = Context.ValueStore.GetValueByName(item.ValueSourceName2Index).Value.ToString();
                    value = Context.ValueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                //==============================================Save=====================================//
                if (!string.IsNullOrEmpty(item.ValueSaveIndex))
                {
                    if (!Context.ValueStore.setValueByIndex(int.Parse(item.ValueSaveIndex), value))
                    {
                        MarkAlarm(evt, $"保存变量失败:索引{item.ValueSaveIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                else if (!string.IsNullOrEmpty(item.ValueSaveIndex2Index))
                {
                    string index = Context.ValueStore.GetValueByIndex(int.Parse(item.ValueSaveIndex2Index)).Value.ToString();
                    if (!Context.ValueStore.setValueByIndex(int.Parse(index), value))
                    {
                        MarkAlarm(evt, $"保存变量失败:索引{index}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                else if (!string.IsNullOrEmpty(item.ValueSaveName))
                {
                    if (!Context.ValueStore.setValueByName(item.ValueSaveName, value))
                    {
                        MarkAlarm(evt, $"保存变量失败:{item.ValueSaveName}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                else if (!string.IsNullOrEmpty(item.ValueSaveName2Index))
                {
                    string index = Context.ValueStore.GetValueByName(item.ValueSaveName2Index).Value.ToString();
                    if (!Context.ValueStore.setValueByIndex(int.Parse(index), value))
                    {
                        MarkAlarm(evt, $"保存变量失败:索引{index}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                
            }
            return true;
        }

        public bool RunModifyValue(ProcHandle evt, ModifyValue ops)
        {
            //==============================================GetSourceValue=====================================//
            string sourceValue = null;
            int sourceIndex = -1;
            DicValue sourceItem = null;
            if (!string.IsNullOrEmpty(ops.ValueSourceIndex))
            {
                sourceIndex = int.Parse(ops.ValueSourceIndex);
                sourceItem = Context.ValueStore.GetValueByIndex(sourceIndex);
                sourceValue = sourceItem.Value;
            }
            else if (!string.IsNullOrEmpty(ops.ValueSourceIndex2Index))
            {
                string index = Context.ValueStore.GetValueByIndex(int.Parse(ops.ValueSourceIndex2Index)).Value;
                sourceIndex = int.Parse(index);
                sourceItem = Context.ValueStore.GetValueByIndex(sourceIndex);
                sourceValue = sourceItem.Value;
            }
            else if (!string.IsNullOrEmpty(ops.ValueSourceName))
            {
                sourceItem = Context.ValueStore.GetValueByName(ops.ValueSourceName);
                sourceIndex = sourceItem.Index;
                sourceValue = sourceItem.Value;
            }
            else if (!string.IsNullOrEmpty(ops.ValueSourceName2Index))
            {
                string index = Context.ValueStore.GetValueByName(ops.ValueSourceName2Index).Value;
                sourceIndex = int.Parse(index);
                sourceItem = Context.ValueStore.GetValueByIndex(sourceIndex);
                sourceValue = sourceItem.Value;
            }
            if (string.IsNullOrEmpty(sourceValue))
            {
                MarkAlarm(evt, "找不到源变量");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            //==============================================GetChangeValue=====================================//
            string changeValue = null;
            int changeIndex = -1;
            DicValue changeItem = null;
            if (!string.IsNullOrEmpty(ops.ChangeValue))
            {
                changeValue = ops.ChangeValue;
            }
            else if (!string.IsNullOrEmpty(ops.ChangeValueIndex))
            {
                changeIndex = int.Parse(ops.ChangeValueIndex);
                changeItem = Context.ValueStore.GetValueByIndex(changeIndex);
                changeValue = changeItem.Value;
            }
            else if (!string.IsNullOrEmpty(ops.ChangeValueIndex2Index))
            {
                string index = Context.ValueStore.GetValueByIndex(int.Parse(ops.ChangeValueIndex2Index)).Value;
                changeIndex = int.Parse(index);
                changeItem = Context.ValueStore.GetValueByIndex(changeIndex);
                changeValue = changeItem.Value;
            }
            else if (!string.IsNullOrEmpty(ops.ChangeValueName))
            {
                changeItem = Context.ValueStore.GetValueByName(ops.ChangeValueName);
                changeIndex = changeItem.Index;
                changeValue = changeItem.Value;
            }
            else if (!string.IsNullOrEmpty(ops.ChangeValueName2Index))
            {
                string index = Context.ValueStore.GetValueByName(ops.ChangeValueName2Index).Value;
                changeIndex = int.Parse(index);
                changeItem = Context.ValueStore.GetValueByIndex(changeIndex);
                changeValue = changeItem.Value;
            }
            if (string.IsNullOrEmpty(changeValue))
            {
                MarkAlarm(evt, "找不到修改变量");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            bool needNumeric = ops.ModifyType == "叠加"
                || ops.ModifyType == "乘法"
                || ops.ModifyType == "除法"
                || ops.ModifyType == "求余"
                || ops.ModifyType == "绝对值";
            if (needNumeric)
            {
                if (sourceItem != null && !string.Equals(sourceItem.Type, "double", StringComparison.OrdinalIgnoreCase))
                {
                    MarkAlarm(evt, $"变量类型不匹配:{sourceItem.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (ops.ModifyType != "绝对值" && changeItem != null
                    && !string.Equals(changeItem.Type, "double", StringComparison.OrdinalIgnoreCase))
                {
                    MarkAlarm(evt, $"变量类型不匹配:{changeItem.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }

            int outputIndex = -1;
            DicValue outputItem = null;
            if (!string.IsNullOrEmpty(ops.OutputValueIndex))
            {
                outputIndex = int.Parse(ops.OutputValueIndex);
                outputItem = Context.ValueStore.GetValueByIndex(outputIndex);
            }
            else if (!string.IsNullOrEmpty(ops.OutputValueIndex2Index))
            {
                string index = Context.ValueStore.GetValueByIndex(int.Parse(ops.OutputValueIndex2Index)).Value.ToString();
                outputIndex = int.Parse(index);
                outputItem = Context.ValueStore.GetValueByIndex(outputIndex);
            }
            else if (!string.IsNullOrEmpty(ops.OutputValueName))
            {
                outputItem = Context.ValueStore.GetValueByName(ops.OutputValueName);
                outputIndex = outputItem.Index;
            }
            else if (!string.IsNullOrEmpty(ops.OutputValueName2Index))
            {
                string index = Context.ValueStore.GetValueByName(ops.OutputValueName2Index).Value.ToString();
                outputIndex = int.Parse(index);
                outputItem = Context.ValueStore.GetValueByIndex(outputIndex);
            }
            if (needNumeric && outputItem != null
                && !string.Equals(outputItem.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                MarkAlarm(evt, $"变量类型不匹配:{outputItem.Name}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            string CalculateOutput(string sourceText, string changeText)
            {
                if (ops.ModifyType == "替换")
                {
                    return changeText;
                }

                double sourceNumber = double.Parse(sourceText);
                double changeNumber = 0;
                if (ops.ModifyType != "绝对值")
                {
                    changeNumber = double.Parse(changeText);
                }

                if (ops.ModifyType == "叠加")
                {
                    double sourceR = ops.sourceR ? -1 : 1;
                    double changeR = ops.ChangeR ? -1 : 1;

                    return (sourceR * sourceNumber + changeR * changeNumber).ToString();
                }
                if (ops.ModifyType == "乘法")
                {
                    double sourceR = ops.sourceR ? -1 : 1;
                    double changeR = ops.ChangeR ? -1 : 1;

                    return (sourceR * sourceNumber * changeR * changeNumber).ToString();
                }
                if (ops.ModifyType == "除法")
                {
                    double sourceR = ops.sourceR ? -1 : 1;
                    double changeR = ops.ChangeR ? -1 : 1;

                    return ((sourceR * sourceNumber) / (changeR * changeNumber)).ToString();
                }
                if (ops.ModifyType == "求余")
                {
                    return (sourceNumber % changeNumber).ToString();
                }

                return Math.Abs(sourceNumber).ToString();
            }

            bool useOutputLock = outputIndex >= 0 && (outputIndex == sourceIndex || outputIndex == changeIndex);
            if (useOutputLock)
            {
                if (!Context.ValueStore.TryModifyValueByIndex(outputIndex, current =>
                {
                    string actualSource = sourceIndex == outputIndex ? current : sourceValue;
                    string actualChange = changeIndex == outputIndex ? current : changeValue;
                    return CalculateOutput(actualSource, actualChange);
                }, out string modifyError))
                {
                    MarkAlarm(evt, string.IsNullOrWhiteSpace(modifyError) ? "保存变量失败" : modifyError);
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                return true;
            }

            string output = CalculateOutput(sourceValue, changeValue);
            if (outputIndex >= 0)
            {
                if (!Context.ValueStore.setValueByIndex(outputIndex, output))
                {
                    MarkAlarm(evt, string.IsNullOrEmpty(outputItem?.Name)
                        ? $"保存变量失败:索引{outputIndex}"
                        : $"保存变量失败:{outputItem.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                return true;
            }

            //==============================================OutputValue=====================================//
            if (!string.IsNullOrEmpty(ops.OutputValueName))
            {
                if (!Context.ValueStore.setValueByName(ops.OutputValueName, output))
                {
                    MarkAlarm(evt, $"保存变量失败:{ops.OutputValueName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else if (!string.IsNullOrEmpty(ops.OutputValueName2Index))
            {
                string index = Context.ValueStore.GetValueByName(ops.OutputValueName2Index).Value.ToString();
                if (!Context.ValueStore.setValueByIndex(int.Parse(index), output))
                {
                    MarkAlarm(evt, $"保存变量失败:索引{index}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            return true;

        }
    }
}
