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
                        MarkAlarm(evt, $"格式化结果保存失败:索引{stringFormat.OutputValueIndex}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                else if (!string.IsNullOrEmpty(stringFormat.OutputValueName))
                {
                    if (!Context.ValueStore.setValueByName(stringFormat.OutputValueName, formattedStr))
                    {
                        MarkAlarm(evt, $"格式化结果保存失败:{stringFormat.OutputValueName}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
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
                    MarkAlarm(evt, $"保存变量失败:索引{i}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
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
                MarkAlarm(evt, "找不到源变量");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                MarkAlarm(evt, "找不到被替换字符");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                MarkAlarm(evt, "找不到新字符");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                    MarkAlarm(evt, $"保存变量失败:索引{replace.OutputIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else if (!string.IsNullOrEmpty(replace.Output))
            {
                if (!Context.ValueStore.setValueByName(replace.Output, str))
                {
                    MarkAlarm(evt, $"保存变量失败:{replace.Output}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else
            {
                MarkAlarm(evt, "找不到保存变量");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            return true;

        }
    }
}
