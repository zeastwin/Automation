using System;
// 模块：引擎 / 执行。
// 职责范围：负责运行绑定、调度、状态管理以及各类流程指令的确定性执行。

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
            StringFormatRuntimeBinding binding =
                stringFormat.RuntimeBinding as StringFormatRuntimeBinding;
            string bindError = null;
            if (binding == null
                && !(evt?.Proc != null
                    ? ProcessRuntimeBinder.TryBind(
                        evt.Proc, evt.procNum, valueStore, out bindError)
                    : ProcessRuntimeBinder.TryBindStandalone(
                        evt?.procId ?? Guid.Empty,
                        valueStore, stringFormat, out bindError)))
            {
                throw CreateAlarmException(evt, bindError ?? "字符串格式化运行计划未编译");
            }
            binding = binding
                ?? stringFormat.RuntimeBinding as StringFormatRuntimeBinding;
            if (binding == null || binding.Sources.Length != stringFormat.Params.Count)
            {
                throw CreateAlarmException(evt, "字符串格式化运行计划未编译");
            }
            var values = new string[binding.Sources.Length];
            for (int i = 0; i < binding.Sources.Length; i++)
            {
                if (!binding.Sources[i].TryResolveValue(
                        valueStore, "源变量", evt.procId,
                        out DicValue sourceItem, out string sourceResolveError))
                {
                    throw CreateAlarmException(evt, sourceResolveError);
                }
                values[i] = sourceItem.Value ?? string.Empty;
            }
            try
            {
                string formattedStr = string.Format(stringFormat.Format, values);
                if (!binding.Output.TryResolveValue(
                        valueStore, "存储变量", evt.procId,
                        out DicValue outputItem, out string outputResolveError))
                {
                    throw CreateAlarmException(evt, outputResolveError);
                }
                if (!valueStore.SetResolvedValueForProcess(
                        outputItem, formattedStr, evt.procId, source))
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
            SplitRuntimeBinding binding = split.RuntimeBinding as SplitRuntimeBinding;
            string bindError = null;
            if (binding == null
                && !(evt?.Proc != null
                    ? ProcessRuntimeBinder.TryBind(
                        evt.Proc, evt.procNum, valueStore, out bindError)
                    : ProcessRuntimeBinder.TryBindStandalone(
                        evt?.procId ?? Guid.Empty,
                        valueStore, split, out bindError)))
            {
                throw CreateAlarmException(evt, bindError ?? "字符串分割运行计划未编译");
            }
            binding = binding ?? split.RuntimeBinding as SplitRuntimeBinding;
            if (binding == null)
            {
                throw CreateAlarmException(evt, "字符串分割运行计划未编译");
            }
            if (!binding.Source.TryResolveValue(
                    valueStore, "源变量", evt.procId,
                    out DicValue sourceItem, out string sourceResolveError))
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

            if (!binding.Output.TryResolveValue(
                    valueStore, "结果变量", evt.procId,
                    out DicValue outputItem, out string outputResolveError))
            {
                throw CreateAlarmException(evt, outputResolveError);
            }
            int SaveIndex = outputItem.Index;
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
            ReplaceRuntimeBinding binding =
                replace.RuntimeBinding as ReplaceRuntimeBinding;
            string bindError = null;
            if (binding == null
                && !(evt?.Proc != null
                    ? ProcessRuntimeBinder.TryBind(
                        evt.Proc, evt.procNum, valueStore, out bindError)
                    : ProcessRuntimeBinder.TryBindStandalone(
                        evt?.procId ?? Guid.Empty,
                        valueStore, replace, out bindError)))
            {
                throw CreateAlarmException(evt, bindError ?? "字符串替换运行计划未编译");
            }
            binding = binding ?? replace.RuntimeBinding as ReplaceRuntimeBinding;
            if (binding == null)
            {
                throw CreateAlarmException(evt, "字符串替换运行计划未编译");
            }
            if (!binding.Source.TryResolveValue(
                    valueStore, "源变量", evt.procId,
                    out DicValue sourceItem, out string sourceResolveError))
            {
                throw CreateAlarmException(evt, sourceResolveError);
            }
            string SourceValue = sourceItem.Value;
            if (string.IsNullOrEmpty(SourceValue))
            {
                throw CreateAlarmException(evt, "找不到源变量");
            }

            string replaceStr = binding.ReplaceText;
            if (!binding.UsesLiteralReplaceText)
            {
                if (!binding.ReplaceTextSource.TryResolveValue(
                        valueStore, "被替换字符", evt.procId,
                        out DicValue replaceTextItem, out string replaceTextError))
                {
                    throw CreateAlarmException(evt, replaceTextError);
                }
                replaceStr = replaceTextItem.Value;
            }
            if (string.IsNullOrEmpty(replaceStr))
            {
                throw CreateAlarmException(evt, "找不到被替换字符");
            }

            string newStr = binding.NewText;
            if (!binding.UsesLiteralNewText)
            {
                if (!binding.NewTextSource.TryResolveValue(
                        valueStore, "新字符", evt.procId,
                        out DicValue newTextItem, out string newTextError))
                {
                    throw CreateAlarmException(evt, newTextError);
                }
                newStr = newTextItem.Value;
            }
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

            if (!binding.Output.TryResolveValue(
                    valueStore, "结果变量", evt.procId,
                    out DicValue outputItem, out string outputResolveError))
            {
                throw CreateAlarmException(evt, outputResolveError);
            }
            if (!valueStore.SetResolvedValueForProcess(
                    outputItem, str, evt.procId, source))
            {
                string outputName = string.IsNullOrWhiteSpace(outputItem.Name) ? $"索引{outputItem.Index}" : outputItem.Name;
                throw CreateAlarmException(evt, $"保存变量失败:{outputName}");
            }
            return true;

        }
    }
}
