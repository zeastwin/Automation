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
            string source = evt?.GetOperationSource();
            if (getValue == null || getValue.Params == null || getValue.Params.Count == 0)
            {
                MarkAlarm(evt, "获取变量参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            GetValueRuntimeBinding binding = getValue.RuntimeBinding as GetValueRuntimeBinding;
            string bindError = null;
            if (binding == null && (evt?.Proc == null
                || !ProcessRuntimeBinder.TryBind(
                    evt.Proc, evt.procNum, Context?.ValueStore, out bindError)))
            {
                throw CreateAlarmException(evt, bindError ?? "获取变量运行计划未编译");
            }
            binding = binding ?? getValue.RuntimeBinding as GetValueRuntimeBinding;
            if (binding == null || binding.Sources.Length != getValue.Params.Count)
            {
                throw CreateAlarmException(evt, "获取变量运行计划未编译");
            }
            for (int paramIndex = 0; paramIndex < getValue.Params.Count; paramIndex++)
            {
                if (!binding.Sources[paramIndex].TryResolveValue(valueStore, "源变量", evt.procId, out DicValue sourceItem, out string sourceResolveError))
                {
                    throw CreateAlarmException(evt, sourceResolveError);
                }
                string value = sourceItem.Value;

                if (!binding.Destinations[paramIndex].TryResolveValue(valueStore, "存储变量", evt.procId, out DicValue saveItem, out string saveResolveError))
                {
                    throw CreateAlarmException(evt, saveResolveError);
                }
                if (!valueStore.SetValueByIndexForProcess(saveItem.Index, value, evt.procId, source))
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
            string source = evt?.GetOperationSource();
            ModifyValueRuntimeBinding binding = ops?.RuntimeBinding as ModifyValueRuntimeBinding;
            string bindError = null;
            if (binding == null && (evt?.Proc == null
                || !ProcessRuntimeBinder.TryBind(
                    evt.Proc, evt.procNum, Context?.ValueStore, out bindError)))
            {
                throw CreateAlarmException(evt, bindError ?? "修改变量运行计划未编译");
            }
            binding = binding ?? ops?.RuntimeBinding as ModifyValueRuntimeBinding;
            if (binding == null)
            {
                throw CreateAlarmException(evt, "修改变量运行计划未编译");
            }
            if (!binding.Source.TryResolveValue(valueStore, "源变量", evt.procId, out DicValue sourceItem, out string sourceResolveError))
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
            if (binding.UsesLiteralChangeValue)
            {
                changeValue = ops.ChangeValue;
            }
            else
            {
                if (!binding.ChangeValue.TryResolveValue(valueStore, "修改值", evt.procId, out changeItem, out string changeResolveError))
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

            if (!binding.Output.TryResolveValue(valueStore, "结果变量", evt.procId, out DicValue outputItem, out string outputResolveError))
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
                    double negateSource = ops.NegateSource ? -1 : 1;
                    double operandSign = ops.NegateOperand ? -1 : 1;

                    return (negateSource * sourceNumber + operandSign * changeNumber).ToString();
                }
                if (ops.ModifyType == "乘法")
                {
                    double negateSource = ops.NegateSource ? -1 : 1;
                    double operandSign = ops.NegateOperand ? -1 : 1;

                    return (negateSource * sourceNumber * operandSign * changeNumber).ToString();
                }
                if (ops.ModifyType == "除法")
                {
                    double negateSource = ops.NegateSource ? -1 : 1;
                    double operandSign = ops.NegateOperand ? -1 : 1;

                    return ((negateSource * sourceNumber) / (operandSign * changeNumber)).ToString();
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
                if (!valueStore.SetValueByIndexForProcess(outputIndex, output, evt.procId, source))
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
