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
        public bool RunProcOps(ProcHandle evt, ProcOps procOps)
        {
            bool value = false;
            foreach (procParam procParam in procOps.procParams)
            {
                Proc proc = null;
                if(!string.IsNullOrEmpty(procParam.ProcName))
                proc = Context.Procs.FirstOrDefault(sc => sc.head.Name.ToString() == procParam.ProcName);
                else if (!string.IsNullOrEmpty(procParam.ProcValue))
                {
                    proc = Context.Procs.FirstOrDefault(sc => sc.head.Name.ToString() == Context.ValueStore.GetValueByName(procParam.ProcValue).GetCValue());
                }
                if(proc == null)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = "找不到流程";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                if (procParam.value == "运行")
                {
                    int index = Context.Procs.IndexOf(proc);
                    ProcRunState targetState = GetProcState(index);
                    if (targetState != ProcRunState.Stopped)
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"流程未停止:{proc?.head?.Name}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }
                    StartProcAuto(proc, index);
                }
                else
                {
                    int index = Context.Procs.IndexOf(proc);
                    ProcRunState targetState = GetProcState(index);
                    if (targetState == ProcRunState.Stopped)
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"流程已停止:{proc?.head?.Name}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }
                    Stop(index);
                }
                Delay(procParam.delayAfter, evt);
            }
            return true;
        }
        public bool RunWaitProc(ProcHandle evt, WaitProc waitProc)
        {
            bool value = false;
            int timeOut;
            timeOut = waitProc.timeOutC.TimeOut;
            if (timeOut <= 0 && !string.IsNullOrEmpty(waitProc.timeOutC.TimeOutValue))
            {
                timeOut = (int)Context.ValueStore.GetValueByName(waitProc.timeOutC.TimeOutValue).GetDValue();
            }
            if (timeOut <= 0)
            {
                evt.isAlarm = true;
                evt.alarmMsg = "等待流程超时配置无效";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            int DelayAfter;
            DelayAfter = waitProc.delayAfter;
            if (DelayAfter <= 0 && !string.IsNullOrEmpty(waitProc.delayAfterV))
            {
                DelayAfter = (int)Context.ValueStore.GetValueByName(waitProc.delayAfterV).GetDValue();
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (evt.State != ProcRunState.Stopped
                && !evt.CancellationToken.IsCancellationRequested)
            {
                if (stopwatch.ElapsedMilliseconds > timeOut)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = "等待超时";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                bool isWaitOff = true;

                foreach (WaitProcParam procParam in waitProc.Params)
                {
                    Proc proc = null;
                    if (!string.IsNullOrEmpty(procParam.ProcName))
                        proc = Context.Procs.FirstOrDefault(sc => sc.head.Name.ToString() == procParam.ProcName);
                    else if (!string.IsNullOrEmpty(procParam.ProcValue))
                    {
                        proc = Context.Procs.FirstOrDefault(sc => sc.head.Name.ToString() == Context.ValueStore.GetValueByName(procParam.ProcValue).GetCValue());
                    }
                    if (proc == null)
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = "找不到流程";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }
                    int index = Context.Procs.IndexOf(proc);
                    if (procParam.value == "运行")
                    {
                        if (GetProcState(index) == ProcRunState.Stopped)
                        {
                            isWaitOff = false;
                            break;
                        }
                    }
                    else if (procParam.value == "停止")
                    {
                        if (GetProcState(index) != ProcRunState.Stopped)
                        {
                            isWaitOff = false;
                            break;
                        }
                    }
                }
                if (isWaitOff)
                {
                    break;
                }
                Delay(5, evt);
            }
            Delay(DelayAfter, evt);
            return true;
        }
        public bool RunGoto(ProcHandle evt, Goto gotoParam)
        {
            if (gotoParam.Params != null)
            {
                string value = "";
                if (!string.IsNullOrEmpty(gotoParam.ValueIndex))
                    value = Context.ValueStore.GetValueByIndex(int.Parse(gotoParam.ValueIndex)).Value.ToString();
                else if (!string.IsNullOrEmpty(gotoParam.ValueIndex2Index))
                {
                    string index = Context.ValueStore.GetValueByIndex(int.Parse(gotoParam.ValueIndex2Index)).Value.ToString();
                    value = Context.ValueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(gotoParam.ValueName))
                {
                    value = Context.ValueStore.GetValueByName(gotoParam.ValueName).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(gotoParam.ValueName2Index))
                {
                    string index = Context.ValueStore.GetValueByName(gotoParam.ValueName2Index).Value.ToString();
                    value = Context.ValueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                if(value == "")
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = "匹配不到变量";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                foreach (var item in gotoParam.Params)
                {
                    string itemValue = "";
                    if (!string.IsNullOrEmpty(item.MatchValue))
                        itemValue = item.MatchValue;
                    else if (!string.IsNullOrEmpty(item.MatchValueIndex))
                    {
                        itemValue = Context.ValueStore.GetValueByIndex(int.Parse(item.MatchValueIndex)).Value.ToString();
                    }
                    else if (!string.IsNullOrEmpty(item.MatchValueV))
                    {
                        itemValue = Context.ValueStore.GetValueByName(item.MatchValueV).Value.ToString();
                    }
                    if (value == itemValue)
                    {
                        if (!TryExecuteGoto(item.Goto, evt, out string gotoError))
                        {
                            evt.isAlarm = true;
                            evt.alarmMsg = gotoError;
                            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                        }
                        evt.isGoto = true;
                        return true;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(gotoParam.DefaultGoto))
            {
                if (!TryExecuteGoto(gotoParam.DefaultGoto, evt, out string defaultGotoError))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = defaultGotoError;
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                evt.isGoto = true;
                return true;
            }
            evt.isAlarm = true;
            evt.alarmMsg = "跳转失败：未匹配到跳转条件且默认跳转为空";
            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
        }
        public bool RunParamGoto(ProcHandle evt, ParamGoto paramGoto)
        {
            if (paramGoto.Params != null)
            {
                bool isFirst = true;
                bool outPut = true;
                foreach (var item in paramGoto.Params)
                {
                    bool isNumericJudge = item.JudgeMode != "等于特征字符";
                    double numericValue = 0;
                    string textValue = null;
                    bool hasValueSource = false;
                    if (isNumericJudge)
                    {
                        if (!string.IsNullOrEmpty(item.ValueIndex))
                        {
                            numericValue = Context.ValueStore.GetValueByIndex(int.Parse(item.ValueIndex)).GetDValue();
                            hasValueSource = true;
                        }
                        else if (!string.IsNullOrEmpty(item.ValueIndex2Index))
                        {
                            string index = Context.ValueStore.GetValueByIndex(int.Parse(item.ValueIndex2Index)).Value.ToString();
                            numericValue = Context.ValueStore.GetValueByIndex(int.Parse(index)).GetDValue();
                            hasValueSource = true;
                        }
                        else if (!string.IsNullOrEmpty(item.ValueName))
                        {
                            numericValue = Context.ValueStore.GetValueByName(item.ValueName).GetDValue();
                            hasValueSource = true;
                        }
                        else if (!string.IsNullOrEmpty(item.ValueName2Index))
                        {
                            string index = Context.ValueStore.GetValueByName(item.ValueName2Index).Value.ToString();
                            numericValue = Context.ValueStore.GetValueByIndex(int.Parse(index)).GetDValue();
                            hasValueSource = true;
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(item.ValueIndex))
                        {
                            textValue = Context.ValueStore.GetValueByIndex(int.Parse(item.ValueIndex)).GetCValue();
                            hasValueSource = true;
                        }
                        else if (!string.IsNullOrEmpty(item.ValueIndex2Index))
                        {
                            string index = Context.ValueStore.GetValueByIndex(int.Parse(item.ValueIndex2Index)).Value.ToString();
                            textValue = Context.ValueStore.GetValueByIndex(int.Parse(index)).GetCValue();
                            hasValueSource = true;
                        }
                        else if (!string.IsNullOrEmpty(item.ValueName))
                        {
                            textValue = Context.ValueStore.GetValueByName(item.ValueName).GetCValue();
                            hasValueSource = true;
                        }
                        else if (!string.IsNullOrEmpty(item.ValueName2Index))
                        {
                            string index = Context.ValueStore.GetValueByName(item.ValueName2Index).Value.ToString();
                            textValue = Context.ValueStore.GetValueByIndex(int.Parse(index)).GetCValue();
                            hasValueSource = true;
                        }
                    }
                    if (!hasValueSource)
                    {
                        throw new InvalidOperationException("找不到判断变量");
                    }
                    bool tempValue = false;
                    if (item.JudgeMode == "值在区间左")
                    {
                        if (item.equal)
                        {
                            tempValue = item.Down >= numericValue ? true : false;
                        }
                        else
                        {
                            tempValue = item.Down > numericValue ? true : false;
                        }
                    }
                    else if (item.JudgeMode == "值在区间右")
                    {
                        if (item.equal)
                        {
                            tempValue = item.Down <= numericValue ? true : false;
                        }
                        else
                        {
                            tempValue = item.Down < numericValue ? true : false;
                        }
                    }
                    else if (item.JudgeMode == "值在区间内")
                    {
                        if (item.equal)
                        {
                            tempValue = item.Down <= numericValue && numericValue <= item.Up ? true : false;
                        }
                        else
                        {
                            tempValue = item.Down < numericValue && numericValue < item.Up ? true : false;
                        }
                    }
                    else if (item.JudgeMode == "等于特征字符")
                    {
                        tempValue = textValue == item.keyString ? true : false;
                    }
                    if (isFirst)
                    {
                        outPut = tempValue;
                        isFirst = false;
                    }
                    else
                    {
                        if (item.Operator == "且")
                        {
                            outPut = tempValue && outPut;
                        }
                        else
                        {
                            outPut = tempValue || outPut;
                        }
                    }
                }
                string gotoTarget = outPut ? paramGoto.goto1 : paramGoto.goto2;
                if (!TryExecuteGoto(gotoTarget, evt, out string gotoError))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = gotoError;
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                evt.isGoto = true;
            }
            return true;
        }
        public bool RunDelay(ProcHandle evt, Delay delay)
        {
            int time = 0;
            if (!string.IsNullOrEmpty(delay.timeMiniSecond))
                time = int.Parse(delay.timeMiniSecond);
            else if (!string.IsNullOrEmpty(delay.timeMiniSecondV))
            {
                time = int.Parse(Context.ValueStore.GetValueByName(delay.timeMiniSecondV).Value);
            }
         
            Delay(time,evt);
            return true;
        }
    }
}
