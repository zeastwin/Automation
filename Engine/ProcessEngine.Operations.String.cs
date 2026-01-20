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
            List<string> values = new List<string>();
            foreach (var item in stringFormat.Params)
            {
                //==============================================GetSourceValue=====================================//
                string SourceValue = "";
                if (!string.IsNullOrEmpty(item.ValueSourceIndex))
                    SourceValue = Context.ValueStore.GetValueByIndex(int.Parse(item.ValueSourceIndex)).Value.ToString();
                else if (!string.IsNullOrEmpty(item.ValueSourceName))
                {
                    SourceValue = Context.ValueStore.GetValueByName(item.ValueSourceName).Value.ToString();
                }
                values.Add(SourceValue);
            }
            try
            {
                string formattedStr = string.Format(stringFormat.Format, values.ToArray());

                if (!string.IsNullOrEmpty(stringFormat.OutputValueIndex))
                {
                    if (!Context.ValueStore.setValueByIndex(int.Parse(stringFormat.OutputValueIndex), formattedStr))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"格式化结果保存失败:索引{stringFormat.OutputValueIndex}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }
                }
                else if (!string.IsNullOrEmpty(stringFormat.OutputValueName))
                {
                    if (!Context.ValueStore.setValueByName(stringFormat.OutputValueName, formattedStr))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"格式化结果保存失败:{stringFormat.OutputValueName}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }
                }
               
            }
            catch (Exception ex)
            {
                evt.isAlarm = true;
                evt.alarmMsg = ex.Message;
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            return true;

        }

        public bool RunSplit(ProcHandle evt, Split split)
        {
            string SourceValue = "";
            if (!string.IsNullOrEmpty(split.SourceValueIndex))
                SourceValue = Context.ValueStore.GetValueByIndex(int.Parse(split.SourceValueIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(split.SourceValue))
            {
                SourceValue = Context.ValueStore.GetValueByName(split.SourceValue).Value.ToString();
            }

            string[] splitArray = SourceValue.Split(split.SplitMark);

            int Startindex = int.Parse(split.startIndex);

            int SaveIndex = 0;
            if (!string.IsNullOrEmpty(split.OutputIndex))
                SaveIndex = int.Parse(split.OutputIndex);
            else if (!string.IsNullOrEmpty(split.Output))
            {
                SaveIndex = Context.ValueStore.GetValueByName(split.Output).Index;
            }
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
                if (!Context.ValueStore.setValueByIndex(i, splitArray[Startindex + i - SaveIndex]))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:索引{i}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
            }
            return true;

        }
        public bool RunReplace(ProcHandle evt, Replace replace)
        {
            string SourceValue = "";
            if (!string.IsNullOrEmpty(replace.SourceValueIndex))
                SourceValue = Context.ValueStore.GetValueByIndex(int.Parse(replace.SourceValueIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(replace.SourceValue))
            {
                SourceValue = Context.ValueStore.GetValueByName(replace.SourceValue).Value.ToString();
            }
            if (SourceValue == "")
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到源变量";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
        
            string replaceStr = "";
            if (!string.IsNullOrEmpty(replace.ReplaceStr))
            {
                replaceStr = replace.ReplaceStr;
            }
            else if (!string.IsNullOrEmpty(replace.ReplaceStrIndex))
                replaceStr = Context.ValueStore.GetValueByIndex(int.Parse(replace.ReplaceStrIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(replace.ReplaceStrV))
            {
                replaceStr = Context.ValueStore.GetValueByName(replace.ReplaceStrV).Value.ToString();
            }
            if (replaceStr == "")
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到被替换字符";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }

            string newStr = "";
            if (!string.IsNullOrEmpty(replace.NewStr))
            {
                newStr = replace.NewStr;
            }
            else if (!string.IsNullOrEmpty(replace.NewStrIndex))
                newStr = Context.ValueStore.GetValueByIndex(int.Parse(replace.NewStrIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(replace.NewStrV))
            {
                newStr = Context.ValueStore.GetValueByName(replace.NewStrV).Value.ToString();
            }
            if (newStr == "")
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到新字符";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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

            if (!string.IsNullOrEmpty(replace.OutputIndex))
            {
                if (!Context.ValueStore.setValueByIndex(int.Parse(replace.OutputIndex),str))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:索引{replace.OutputIndex}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
            }
            else if (!string.IsNullOrEmpty(replace.Output))
            {
                if (!Context.ValueStore.setValueByName(replace.Output, str))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:{replace.Output}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
            }
            else
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到保存变量";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            return true;

        }
    }
}
