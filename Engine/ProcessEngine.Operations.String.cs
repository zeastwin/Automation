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
        public bool RunStringFormat(ProcHandle evt, StringFormat stringFormat)
        {
            if (stringFormat == null || stringFormat.Params == null || stringFormat.Params.Count == 0)
            {
                MarkAlarm(evt, "字符串格式化参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (stringFormat.Format == null)
            {
                MarkAlarm(evt, "格式化模板为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            ValueConfigStore valueStore = Context?.ValueStore;
            string source = evt?.GetOperationSource();
            List<string> values = new List<string>();
            foreach (var item in stringFormat.Params)
            {
                if (!ValueRef.TryCreate(item.ValueSourceIndex, null, item.ValueSourceName, null, false, "源变量", out ValueRef sourceRef, out string sourceError))
                {
                    throw CreateAlarmException(evt, sourceError);
                }
                if (!sourceRef.TryResolveValue(valueStore, "源变量", evt.procId, out DicValue sourceItem, out string sourceResolveError))
                {
                    throw CreateAlarmException(evt, sourceResolveError);
                }
                values.Add(sourceItem.Value ?? string.Empty);
            }
            try
            {
                string formattedStr = string.Format(stringFormat.Format, values.ToArray());

                if (!ValueRef.TryCreate(stringFormat.OutputValueIndex, null, stringFormat.OutputValueName, null, false, "存储变量", out ValueRef outputRef, out string outputError))
                {
                    throw CreateAlarmException(evt, outputError);
                }
                if (!outputRef.TryResolveValue(valueStore, "存储变量", evt.procId, out DicValue outputItem, out string outputResolveError))
                {
                    throw CreateAlarmException(evt, outputResolveError);
                }
                if (!valueStore.SetValueByIndexForProcess(outputItem.Index, formattedStr, evt.procId, source))
                {
                    string outputName = string.IsNullOrWhiteSpace(outputItem.Name) ? $"索引{outputItem.Index}" : outputItem.Name;
                    throw CreateAlarmException(evt, $"格式化结果保存失败:{outputName}");
                }
               
            }
            catch (Exception ex)
            {
                MarkAlarm(evt, ex.Message);
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            return true;

        }

        public bool RunSplit(ProcHandle evt, Split split)
        {
            ValueConfigStore valueStore = Context?.ValueStore;
            string source = evt?.GetOperationSource();
            if (split == null)
            {
                MarkAlarm(evt, "字符串分割参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!ValueRef.TryCreate(split.SourceValueIndex, null, split.SourceValue, null, false, "源变量", out ValueRef sourceRef, out string sourceError))
            {
                throw CreateAlarmException(evt, sourceError);
            }
            if (!sourceRef.TryResolveValue(valueStore, "源变量", evt.procId, out DicValue sourceItem, out string sourceResolveError))
            {
                throw CreateAlarmException(evt, sourceResolveError);
            }
            string SourceValue = sourceItem.Value ?? string.Empty;

            string[] splitArray = SourceValue.Split(split.SplitMark);

            int startIndex = split.StartIndex;
            if (startIndex < 0)
            {
                MarkAlarm(evt, $"分割起始索引无效:{split.StartIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            int SaveIndex = 0;
            if (!ValueRef.TryCreate(split.OutputIndex, null, split.Output, null, false, "结果变量", out ValueRef outputRef, out string outputError))
            {
                throw CreateAlarmException(evt, outputError);
            }
            if (!outputRef.TryResolveValue(valueStore, "结果变量", evt.procId, out DicValue outputItem, out string outputResolveError))
            {
                throw CreateAlarmException(evt, outputResolveError);
            }
            SaveIndex = outputItem.Index;
            int count = split.Count ?? splitArray.Length;
            if (count < 0)
            {
                MarkAlarm(evt, $"分割数量无效:{count}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (startIndex >= splitArray.Length)
            {
                MarkAlarm(evt, $"分割起始索引越界:{startIndex}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (startIndex + count > splitArray.Length)
            {
                MarkAlarm(evt, $"分割数量越界:起始{startIndex},数量{count}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            for (int i = SaveIndex; i < SaveIndex + count; i++)
            {
                if (!valueStore.SetValueByIndexForProcess(i, splitArray[startIndex + i - SaveIndex], evt.procId, source))
                {
                    MarkAlarm(evt, $"保存变量失败:索引{i}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            return true;

        }
        public bool RunReplace(ProcHandle evt, Replace replace)
        {
            ValueConfigStore valueStore = Context?.ValueStore;
            string source = evt?.GetOperationSource();
            if (replace == null)
            {
                MarkAlarm(evt, "字符串替换参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!ValueRef.TryCreate(replace.SourceValueIndex, null, replace.SourceValue, null, false, "源变量", out ValueRef sourceRef, out string sourceError))
            {
                throw CreateAlarmException(evt, sourceError);
            }
            if (!sourceRef.TryResolveValue(valueStore, "源变量", evt.procId, out DicValue sourceItem, out string sourceResolveError))
            {
                throw CreateAlarmException(evt, sourceResolveError);
            }
            string SourceValue = sourceItem.Value;
            if (string.IsNullOrEmpty(SourceValue))
            {
                throw CreateAlarmException(evt, "找不到源变量");
            }

            string ResolveTextValue(string literal, string index, string name, string label)
            {
                bool hasLiteral = !string.IsNullOrEmpty(literal);
                bool hasRef = !string.IsNullOrEmpty(index) || !string.IsNullOrEmpty(name);
                if (hasLiteral && hasRef)
                {
                    throw CreateAlarmException(evt, $"{label}配置冲突");
                }
                if (hasLiteral)
                {
                    return literal;
                }
                if (!ValueRef.TryCreate(index, null, name, null, false, label, out ValueRef valueRef, out string refError))
                {
                    throw CreateAlarmException(evt, refError);
                }
                if (!valueRef.TryResolveValue(valueStore, label, evt.procId, out DicValue valueItem, out string resolveError))
                {
                    throw CreateAlarmException(evt, resolveError);
                }
                return valueItem.Value;
            }

            string replaceStr = ResolveTextValue(replace.ReplaceStr, replace.ReplaceStrIndex, replace.ReplaceStrV, "被替换字符");
            if (string.IsNullOrEmpty(replaceStr))
            {
                throw CreateAlarmException(evt, "找不到被替换字符");
            }

            string newStr = ResolveTextValue(replace.NewStr, replace.NewStrIndex, replace.NewStrV, "新字符");
            if (string.IsNullOrEmpty(newStr))
            {
                throw CreateAlarmException(evt, "找不到新字符");
            }

            string str = "";

            if (replace.ReplaceType == "替换指定字符")
            {
                str = SourceValue.Replace(replaceStr, newStr);
            }
            else if (replace.ReplaceType == "替换指定区间")
            {
                int startIndex = replace.StartIndex;
                if (startIndex < 0)
                {
                    MarkAlarm(evt, $"替换起始索引无效:{replace.StartIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!replace.Count.HasValue || replace.Count.Value < 0)
                {
                    MarkAlarm(evt, $"替换长度无效:{replace.Count}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                int count = replace.Count.Value;
                if (startIndex > SourceValue.Length || startIndex + count > SourceValue.Length)
                {
                    MarkAlarm(evt, $"替换区间越界:起始{startIndex},长度{count}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                string beforeSubstring = SourceValue.Substring(0, startIndex);
                string afterSubstring = SourceValue.Substring(startIndex + count);

                str = beforeSubstring + newStr + afterSubstring;
            }

            if (!ValueRef.TryCreate(replace.OutputIndex, null, replace.Output, null, false, "结果变量", out ValueRef outputRef, out string outputError))
            {
                throw CreateAlarmException(evt, outputError);
            }
            if (!outputRef.TryResolveValue(valueStore, "结果变量", evt.procId, out DicValue outputItem, out string outputResolveError))
            {
                throw CreateAlarmException(evt, outputResolveError);
            }
            if (!valueStore.SetValueByIndexForProcess(outputItem.Index, str, evt.procId, source))
            {
                string outputName = string.IsNullOrWhiteSpace(outputItem.Name) ? $"索引{outputItem.Index}" : outputItem.Name;
                throw CreateAlarmException(evt, $"保存变量失败:{outputName}");
            }
            return true;

        }
    }
}
