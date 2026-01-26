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
            ValueConfigStore valueStore = Context?.ValueStore;
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            List<string> values = new List<string>();
            foreach (var item in stringFormat.Params)
            {
                if (!ValueRef.TryCreate(item.ValueSourceIndex, null, item.ValueSourceName, null, false, "源变量", out ValueRef sourceRef, out string sourceError))
                {
                    throw CreateAlarmException(evt, sourceError);
                }
                if (!sourceRef.TryResolveValue(valueStore, "源变量", out DicValue sourceItem, out string sourceResolveError))
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
                if (!outputRef.TryResolveValue(valueStore, "存储变量", out DicValue outputItem, out string outputResolveError))
                {
                    throw CreateAlarmException(evt, outputResolveError);
                }
                if (!valueStore.setValueByIndex(outputItem.Index, formattedStr, source))
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
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            if (!ValueRef.TryCreate(split.SourceValueIndex, null, split.SourceValue, null, false, "源变量", out ValueRef sourceRef, out string sourceError))
            {
                throw CreateAlarmException(evt, sourceError);
            }
            if (!sourceRef.TryResolveValue(valueStore, "源变量", out DicValue sourceItem, out string sourceResolveError))
            {
                throw CreateAlarmException(evt, sourceResolveError);
            }
            string SourceValue = sourceItem.Value ?? string.Empty;

            string[] splitArray = SourceValue.Split(split.SplitMark);

            int Startindex = int.Parse(split.startIndex);

            int SaveIndex = 0;
            if (!ValueRef.TryCreate(split.OutputIndex, null, split.Output, null, false, "结果变量", out ValueRef outputRef, out string outputError))
            {
                throw CreateAlarmException(evt, outputError);
            }
            if (!outputRef.TryResolveValue(valueStore, "结果变量", out DicValue outputItem, out string outputResolveError))
            {
                throw CreateAlarmException(evt, outputResolveError);
            }
            SaveIndex = outputItem.Index;
            int Count = 0;
            if(!string.IsNullOrEmpty(split.Count))
            {
                Count = int.Parse(split.Count);
            }
            else
            {
                Count = splitArray.Length;
            }
            for (int i = SaveIndex; i < SaveIndex + Count; i++)
            {
                if (!valueStore.setValueByIndex(i, splitArray[Startindex + i - SaveIndex], source))
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
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            if (!ValueRef.TryCreate(replace.SourceValueIndex, null, replace.SourceValue, null, false, "源变量", out ValueRef sourceRef, out string sourceError))
            {
                throw CreateAlarmException(evt, sourceError);
            }
            if (!sourceRef.TryResolveValue(valueStore, "源变量", out DicValue sourceItem, out string sourceResolveError))
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
                if (!valueRef.TryResolveValue(valueStore, label, out DicValue valueItem, out string resolveError))
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
                string beforeSubstring = SourceValue.Substring(0, int.Parse(replace.StartIndex));
                string afterSubstring = SourceValue.Substring(int.Parse(replace.StartIndex) + int.Parse(replace.Count));

                str = beforeSubstring + newStr + afterSubstring;
            }

            if (!ValueRef.TryCreate(replace.OutputIndex, null, replace.Output, null, false, "结果变量", out ValueRef outputRef, out string outputError))
            {
                throw CreateAlarmException(evt, outputError);
            }
            if (!outputRef.TryResolveValue(valueStore, "结果变量", out DicValue outputItem, out string outputResolveError))
            {
                throw CreateAlarmException(evt, outputResolveError);
            }
            if (!valueStore.setValueByIndex(outputItem.Index, str, source))
            {
                string outputName = string.IsNullOrWhiteSpace(outputItem.Name) ? $"索引{outputItem.Index}" : outputItem.Name;
                throw CreateAlarmException(evt, $"保存变量失败:{outputName}");
            }
            return true;

        }
    }
}
