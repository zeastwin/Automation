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
            ValueConfigStore valueStore = Context?.ValueStore;
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            if (getValue == null || getValue.Params == null || getValue.Params.Count == 0)
            {
                MarkAlarm(evt, "获取变量参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            foreach (var item in getValue.Params)
            {
                if (!ValueRef.TryCreate(item.ValueSourceIndex, item.ValueSourceIndex2Index, item.ValueSourceName, item.ValueSourceName2Index, false, "源变量", out ValueRef sourceRef, out string sourceError))
                {
                    throw CreateAlarmException(evt, sourceError);
                }
                if (!sourceRef.TryResolveValue(valueStore, "源变量", out DicValue sourceItem, out string sourceResolveError))
                {
                    throw CreateAlarmException(evt, sourceResolveError);
                }
                string value = sourceItem.Value;

                if (!ValueRef.TryCreate(item.ValueSaveIndex, item.ValueSaveIndex2Index, item.ValueSaveName, item.ValueSaveName2Index, false, "存储变量", out ValueRef saveRef, out string saveError))
                {
                    throw CreateAlarmException(evt, saveError);
                }
                if (!saveRef.TryResolveValue(valueStore, "存储变量", out DicValue saveItem, out string saveResolveError))
                {
                    throw CreateAlarmException(evt, saveResolveError);
                }
                if (!valueStore.setValueByIndex(saveItem.Index, value, source))
                {
                    string saveName = string.IsNullOrWhiteSpace(saveItem.Name) ? $"索引{saveItem.Index}" : saveItem.Name;
                    throw CreateAlarmException(evt, $"保存变量失败:{saveName}");
                }
            }
            return true;
        }

        public bool RunModifyValue(ProcHandle evt, ModifyValue ops)
        {
            ValueConfigStore valueStore = Context?.ValueStore;
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            if (!ValueRef.TryCreate(ops.ValueSourceIndex, ops.ValueSourceIndex2Index, ops.ValueSourceName, ops.ValueSourceName2Index, false, "源变量", out ValueRef sourceRef, out string sourceError))
            {
                throw CreateAlarmException(evt, sourceError);
            }
            if (!sourceRef.TryResolveValue(valueStore, "源变量", out DicValue sourceItem, out string sourceResolveError))
            {
                throw CreateAlarmException(evt, sourceResolveError);
            }
            string sourceValue = sourceItem.Value;
            if (string.IsNullOrEmpty(sourceValue))
            {
                throw CreateAlarmException(evt, "找不到源变量");
            }
            int sourceIndex = sourceItem.Index;

            string changeValue = null;
            int changeIndex = -1;
            DicValue changeItem = null;
            bool hasChangeLiteral = !string.IsNullOrEmpty(ops.ChangeValue);
            bool hasChangeRef = !string.IsNullOrEmpty(ops.ChangeValueIndex)
                || !string.IsNullOrEmpty(ops.ChangeValueIndex2Index)
                || !string.IsNullOrEmpty(ops.ChangeValueName)
                || !string.IsNullOrEmpty(ops.ChangeValueName2Index);
            if (hasChangeLiteral && hasChangeRef)
            {
                throw CreateAlarmException(evt, "修改值配置冲突");
            }
            if (hasChangeLiteral)
            {
                changeValue = ops.ChangeValue;
            }
            else
            {
                if (!ValueRef.TryCreate(ops.ChangeValueIndex, ops.ChangeValueIndex2Index, ops.ChangeValueName, ops.ChangeValueName2Index, false, "修改值", out ValueRef changeRef, out string changeError))
                {
                    throw CreateAlarmException(evt, changeError);
                }
                if (!changeRef.TryResolveValue(valueStore, "修改值", out changeItem, out string changeResolveError))
                {
                    throw CreateAlarmException(evt, changeResolveError);
                }
                changeValue = changeItem.Value;
                changeIndex = changeItem.Index;
            }
            if (string.IsNullOrEmpty(changeValue))
            {
                throw CreateAlarmException(evt, "找不到修改变量");
            }

            bool needNumeric = ops.ModifyType == "叠加"
                || ops.ModifyType == "乘法"
                || ops.ModifyType == "除法"
                || ops.ModifyType == "求余"
                || ops.ModifyType == "绝对值";
            if (needNumeric)
            {
                if (!string.Equals(sourceItem.Type, "double", StringComparison.OrdinalIgnoreCase))
                {
                    string sourceName = string.IsNullOrWhiteSpace(sourceItem.Name) ? $"索引{sourceItem.Index}" : sourceItem.Name;
                    throw CreateAlarmException(evt, $"变量类型不匹配:{sourceName}");
                }
                if (ops.ModifyType != "绝对值")
                {
                    if (changeItem != null)
                    {
                        if (!string.Equals(changeItem.Type, "double", StringComparison.OrdinalIgnoreCase))
                        {
                            string changeName = string.IsNullOrWhiteSpace(changeItem.Name) ? $"索引{changeItem.Index}" : changeItem.Name;
                            throw CreateAlarmException(evt, $"变量类型不匹配:{changeName}");
                        }
                    }
                    else if (!double.TryParse(changeValue, out _))
                    {
                        throw CreateAlarmException(evt, "修改值不是有效数字");
                    }
                }
            }

            if (!ValueRef.TryCreate(ops.OutputValueIndex, ops.OutputValueIndex2Index, ops.OutputValueName, ops.OutputValueName2Index, false, "结果变量", out ValueRef outputRef, out string outputError))
            {
                throw CreateAlarmException(evt, outputError);
            }
            if (!outputRef.TryResolveValue(valueStore, "结果变量", out DicValue outputItem, out string outputResolveError))
            {
                throw CreateAlarmException(evt, outputResolveError);
            }
            int outputIndex = outputItem.Index;
            if (needNumeric && !string.Equals(outputItem.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                string outputName = string.IsNullOrWhiteSpace(outputItem.Name) ? $"索引{outputItem.Index}" : outputItem.Name;
                throw CreateAlarmException(evt, $"变量类型不匹配:{outputName}");
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
                if (!valueStore.TryModifyValueByIndex(outputIndex, current =>
                {
                    string actualSource = sourceIndex == outputIndex ? current : sourceValue;
                    string actualChange = changeIndex == outputIndex ? current : changeValue;
                    return CalculateOutput(actualSource, actualChange);
                }, out string modifyError, source))
                {
                    throw CreateAlarmException(evt, string.IsNullOrWhiteSpace(modifyError) ? "保存变量失败" : modifyError);
                }
                return true;
            }

            string output = CalculateOutput(sourceValue, changeValue);
            if (outputIndex >= 0)
            {
                if (!valueStore.setValueByIndex(outputIndex, output, source))
                {
                    string outputName = string.IsNullOrWhiteSpace(outputItem.Name) ? $"索引{outputIndex}" : outputItem.Name;
                    throw CreateAlarmException(evt, $"保存变量失败:{outputName}");
                }
                return true;
            }
            return true;

        }
    }
}
