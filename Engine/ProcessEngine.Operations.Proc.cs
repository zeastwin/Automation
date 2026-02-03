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
            if (procOps == null || procOps.procParams == null || procOps.procParams.Count == 0)
            {
                MarkAlarm(evt, "流程操作参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            IList<Proc> procs = Context?.Procs;
            if (procs == null)
            {
                MarkAlarm(evt, "流程列表为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            foreach (procParam procParam in procOps.procParams)
            {
                Proc proc = null;
                if(!string.IsNullOrEmpty(procParam.ProcName))
                proc = procs.FirstOrDefault(sc => sc.head.Name.ToString() == procParam.ProcName);
                else if (!string.IsNullOrEmpty(procParam.ProcValue))
                {
                    proc = procs.FirstOrDefault(sc => sc.head.Name.ToString() == Context.ValueStore.GetValueByName(procParam.ProcValue).GetCValue());
                }
                if(proc == null)
                {
                    MarkAlarm(evt, "找不到流程");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (procParam.value == "运行")
                {
                    int index = procs.IndexOf(proc);
                    if (index < 0)
                    {
                        MarkAlarm(evt, "流程索引无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    ProcRunState targetState = GetProcState(index);
                    if (targetState != ProcRunState.Stopped)
                    {
                        MarkAlarm(evt, $"流程未停止:{proc?.head?.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!CheckPermission(PermissionKeys.ProcessRun, "流程联动运行", evt))
                    {
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    StartProcAuto(proc, index);
                }
                else
                {
                    int index = procs.IndexOf(proc);
                    if (index < 0)
                    {
                        MarkAlarm(evt, "流程索引无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    ProcRunState targetState = GetProcState(index);
                    if (targetState == ProcRunState.Stopped)
                    {
                        MarkAlarm(evt, $"流程已停止:{proc?.head?.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
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
            if (waitProc == null || waitProc.Params == null || waitProc.Params.Count == 0)
            {
                MarkAlarm(evt, "等待流程参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            IList<Proc> procs = Context?.Procs;
            if (procs == null)
            {
                MarkAlarm(evt, "流程列表为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            timeOut = waitProc.timeOutC.TimeOut;
            if (timeOut <= 0 && !string.IsNullOrEmpty(waitProc.timeOutC.TimeOutValue))
            {
                timeOut = (int)Context.ValueStore.GetValueByName(waitProc.timeOutC.TimeOutValue).GetDValue();
            }
            if (timeOut <= 0)
            {
                MarkAlarm(evt, "等待流程超时配置无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                    MarkAlarm(evt, "等待超时");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                bool isWaitOff = true;

                foreach (WaitProcParam procParam in waitProc.Params)
                {
                    Proc proc = null;
                    if (!string.IsNullOrEmpty(procParam.ProcName))
                        proc = procs.FirstOrDefault(sc => sc.head.Name.ToString() == procParam.ProcName);
                    else if (!string.IsNullOrEmpty(procParam.ProcValue))
                    {
                        proc = procs.FirstOrDefault(sc => sc.head.Name.ToString() == Context.ValueStore.GetValueByName(procParam.ProcValue).GetCValue());
                    }
                    if (proc == null)
                    {
                        MarkAlarm(evt, "找不到流程");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    int index = procs.IndexOf(proc);
                    if (index < 0)
                    {
                        MarkAlarm(evt, "流程索引无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
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
            if (gotoParam == null)
            {
                MarkAlarm(evt, "跳转参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (gotoParam.Params != null && gotoParam.Params.Count > 0)
            {
                ValueConfigStore valueStore = Context?.ValueStore;
                if (!ValueRef.TryCreate(gotoParam.ValueIndex, gotoParam.ValueIndex2Index, gotoParam.ValueName, gotoParam.ValueName2Index, false, "跳转变量", out ValueRef sourceRef, out string sourceError))
                {
                    throw CreateAlarmException(evt, sourceError);
                }
                if (!sourceRef.TryResolveValue(valueStore, "跳转变量", out DicValue sourceItem, out string sourceResolveError))
                {
                    throw CreateAlarmException(evt, sourceResolveError);
                }
                string value = sourceItem.Value;
                if (string.IsNullOrEmpty(value))
                {
                    throw CreateAlarmException(evt, "匹配不到变量");
                }
                foreach (var item in gotoParam.Params)
                {
                    string itemValue = null;
                    bool hasMatchLiteral = !string.IsNullOrEmpty(item.MatchValue);
                    bool hasMatchRef = !string.IsNullOrEmpty(item.MatchValueIndex)
                        || !string.IsNullOrEmpty(item.MatchValueV);
                    if (hasMatchLiteral && hasMatchRef)
                    {
                        throw CreateAlarmException(evt, "匹配值配置冲突");
                    }
                    if (hasMatchLiteral)
                    {
                        itemValue = item.MatchValue;
                    }
                    else
                    {
                        if (!ValueRef.TryCreate(item.MatchValueIndex, null, item.MatchValueV, null, false, "匹配值", out ValueRef matchRef, out string matchError))
                        {
                            throw CreateAlarmException(evt, matchError);
                        }
                        if (!matchRef.TryResolveValue(valueStore, "匹配值", out DicValue matchItem, out string matchResolveError))
                        {
                            throw CreateAlarmException(evt, matchResolveError);
                        }
                        itemValue = matchItem.Value;
                    }
                    if (itemValue == null)
                    {
                        throw CreateAlarmException(evt, "匹配值为空");
                    }
                    if (value == itemValue)
                    {
                        if (!TryExecuteGoto(item.Goto, evt, out string gotoError))
                        {
                            MarkAlarm(evt, gotoError);
                            throw CreateAlarmException(evt, evt?.alarmMsg);
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
                    MarkAlarm(evt, defaultGotoError);
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                evt.isGoto = true;
                return true;
            }
            MarkAlarm(evt, "跳转失败：未匹配到跳转条件且默认跳转为空");
            throw CreateAlarmException(evt, evt?.alarmMsg);
        }
        public bool RunParamGoto(ProcHandle evt, ParamGoto paramGoto)
        {
            if (paramGoto == null || paramGoto.Params == null || paramGoto.Params.Count == 0)
            {
                MarkAlarm(evt, "逻辑判断参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (paramGoto.Params != null)
            {
                bool isFirst = true;
                bool outPut = true;
                ValueConfigStore valueStore = Context?.ValueStore;
                foreach (var item in paramGoto.Params)
                {
                    bool isNumericJudge = item.JudgeMode != "等于特征字符";
                    double numericValue = 0;
                    string textValue = null;
                    if (!ValueRef.TryCreate(item.ValueIndex, item.ValueIndex2Index, item.ValueName, item.ValueName2Index, false, "判断变量", out ValueRef valueRef, out string valueError))
                    {
                        throw CreateAlarmException(evt, valueError);
                    }
                    if (!valueRef.TryResolveValue(valueStore, "判断变量", out DicValue valueItem, out string valueResolveError))
                    {
                        throw CreateAlarmException(evt, valueResolveError);
                    }
                    if (isNumericJudge)
                    {
                        try
                        {
                            numericValue = valueItem.GetDValue();
                        }
                        catch (Exception ex)
                        {
                            throw CreateAlarmException(evt, ex.Message);
                        }
                    }
                    else
                    {
                        try
                        {
                            textValue = valueItem.GetCValue();
                        }
                        catch (Exception ex)
                        {
                            throw CreateAlarmException(evt, ex.Message);
                        }
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
                    MarkAlarm(evt, gotoError);
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                evt.isGoto = true;
            }
            return true;
        }
        public bool RunDelay(ProcHandle evt, Delay delay)
        {
            int time = 0;
            if (delay == null)
            {
                MarkAlarm(evt, "延时参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!string.IsNullOrEmpty(delay.timeMiniSecond))
            {
                if (!int.TryParse(delay.timeMiniSecond, out time) || time < 0)
                {
                    MarkAlarm(evt, $"延时时间无效:{delay.timeMiniSecond}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else if (!string.IsNullOrEmpty(delay.timeMiniSecondV))
            {
                string valueText = Context.ValueStore.GetValueByName(delay.timeMiniSecondV).Value;
                if (!int.TryParse(valueText, out time) || time < 0)
                {
                    MarkAlarm(evt, $"延时变量无效:{delay.timeMiniSecondV}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
         
            Delay(time,evt);
            return true;
        }
    }
}
