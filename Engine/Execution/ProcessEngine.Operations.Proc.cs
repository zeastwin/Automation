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
        public bool RunProcOps(ProcHandle evt, ProcOps procOps)
        {
            if (procOps == null || procOps.Params == null || procOps.Params.Count == 0)
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
            foreach (ProcParam procParam in procOps.Params)
            {
                Proc proc = null;
                if(!string.IsNullOrEmpty(procParam.ProcName))
                proc = procs.FirstOrDefault(sc => sc.head.Name.ToString() == procParam.ProcName);
                else if (!string.IsNullOrEmpty(procParam.ProcValue))
                {
                    proc = procs.FirstOrDefault(sc => sc.head.Name.ToString() == Context.ValueStore.GetValueByNameForProcess(procParam.ProcValue, evt.procId).GetCValue());
                }
                if(proc == null)
                {
                    MarkAlarm(evt, "找不到流程");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (procParam.TargetState == "运行")
                {
                    int index = procs.IndexOf(proc);
                    if (index < 0)
                    {
                        MarkAlarm(evt, "流程索引无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (index == evt.procNum)
                    {
                        MarkAlarm(evt, "禁止流程启动自身");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    ProcRunState targetState = GetProcState(index);
                    if (!targetState.IsInactive())
                    {
                        MarkAlarm(evt, $"流程尚未结束:{proc?.head?.Name}，当前状态:{targetState}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!StartProcAuto(proc, index))
                    {
                        MarkAlarm(evt, $"启动流程失败:{proc?.head?.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
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
                    if (targetState.IsInactive())
                    {
                        MarkAlarm(evt, $"流程未在运行:{proc?.head?.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    Stop(index);
                }
                Delay(procParam.DelayAfterMs, evt);
            }
            return true;
        }
        public bool RunWaitProc(ProcHandle evt, WaitProc waitProc)
        {
            if (waitProc == null)
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
            if (string.Equals(waitProc.WorkMode, WaitProc.WaitReadyMode, StringComparison.Ordinal)
                && (waitProc.Params == null || waitProc.Params.Count == 0))
            {
                MarkAlarm(evt, "等待流程参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            WaitProcRuntimeBinding binding = waitProc.RuntimeBinding as WaitProcRuntimeBinding;
            string bindError = null;
            if (binding == null && (evt?.Proc == null
                || !ProcessRuntimeBinder.TryBind(
                    evt.Proc, evt.procNum, Context?.ValueStore, out bindError)))
            {
                MarkAlarm(evt, bindError ?? "等待流程运行计划未编译");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            binding = binding ?? waitProc.RuntimeBinding as WaitProcRuntimeBinding;
            if (binding == null)
            {
                MarkAlarm(evt, "等待流程运行计划未编译");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            if (string.Equals(waitProc.WorkMode, WaitProc.StateJumpMode, StringComparison.Ordinal))
            {
                ProcRunState actualState = GetWaitProcTargetState(
                    evt, waitProc.TargetProcName, waitProc.TargetProcValue, procs);
                RuntimeGotoTarget target;
                switch (actualState)
                {
                    case ProcRunState.Ready:
                        target = binding.ReadyTarget;
                        break;
                    case ProcRunState.Alarming:
                    case ProcRunState.Stopped:
                        target = binding.AbnormalTarget;
                        break;
                    case ProcRunState.Running:
                    case ProcRunState.Paused:
                    case ProcRunState.SingleStep:
                    case ProcRunState.Pausing:
                    case ProcRunState.Stopping:
                        target = binding.RunningTarget;
                        break;
                    default:
                        MarkAlarm(evt, $"目标流程状态无效:{actualState}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!target.TryApply(evt, out string gotoError))
                {
                    MarkAlarm(evt, gotoError);
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                evt.isGoto = true;
                return true;
            }

            if (string.Equals(waitProc.WorkMode, WaitProc.GetStateMode, StringComparison.Ordinal))
            {
                ProcRunState actualState = GetWaitProcTargetState(
                    evt, waitProc.TargetProcName, waitProc.TargetProcValue, procs);
                if (!binding.StateOutput.TryResolveValue(
                    Context.ValueStore, "流程状态变量", evt.procId,
                    out DicValue output, out string resolveError))
                {
                    MarkAlarm(evt, resolveError);
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                object value;
                if (string.Equals(output.Type, "double", StringComparison.OrdinalIgnoreCase))
                {
                    value = (double)(int)actualState;
                }
                else if (string.Equals(output.Type, "string", StringComparison.OrdinalIgnoreCase))
                {
                    value = actualState.ToString();
                }
                else
                {
                    MarkAlarm(evt, $"流程状态变量类型必须是double或string:{output.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.ValueStore.SetValueByIndexForProcess(
                    output.Index, value, evt.procId, evt.GetOperationSource()))
                {
                    MarkAlarm(evt, $"写入流程状态变量失败:{output.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                return true;
            }

            if (!string.Equals(waitProc.WorkMode, WaitProc.WaitReadyMode, StringComparison.Ordinal))
            {
                MarkAlarm(evt, $"等待流程工作模式无效:{waitProc.WorkMode ?? "空"}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (waitProc.Timeout == null)
            {
                MarkAlarm(evt, "等待流程超时配置为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            int timeOut = waitProc.Timeout.TimeoutMs;
            if (timeOut <= 0 && !string.IsNullOrEmpty(waitProc.Timeout.TimeoutVariableName))
            {
                timeOut = (int)Context.ValueStore.GetValueByNameForProcess(waitProc.Timeout.TimeoutVariableName, evt.procId).GetDValue();
            }
            if (timeOut <= 0)
            {
                MarkAlarm(evt, "等待流程超时配置无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            int DelayAfter;
            DelayAfter = waitProc.DelayAfterMs;
            if (DelayAfter <= 0 && !string.IsNullOrEmpty(waitProc.DelayAfterVariableName))
            {
                DelayAfter = (int)Context.ValueStore.GetValueByNameForProcess(waitProc.DelayAfterVariableName, evt.procId).GetDValue();
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!evt.CancellationToken.IsCancellationRequested)
            {
                if (stopwatch.ElapsedMilliseconds > timeOut)
                {
                    MarkAlarm(evt, "等待超时");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                bool isWaitOff = true;

                foreach (WaitProcParam procParam in waitProc.Params)
                {
                    if (procParam == null
                        || procParam.TargetState != "运行" && procParam.TargetState != "就绪")
                    {
                        MarkAlarm(evt, "等待流程只允许等待运行或就绪状态");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    ProcRunState actualState = GetWaitProcTargetState(
                        evt, procParam.ProcName, procParam.ProcValue, procs);
                    bool matched = procParam.TargetState == "运行"
                        ? actualState == ProcRunState.Running
                        : actualState == ProcRunState.Ready;
                    if (!matched)
                    {
                        isWaitOff = false;
                        break;
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

        private ProcRunState GetWaitProcTargetState(
            ProcHandle evt, string procName, string procValue, IList<Proc> procs)
        {
            Proc proc = null;
            if (!string.IsNullOrEmpty(procName))
            {
                proc = procs.FirstOrDefault(sc => sc?.head != null
                    && string.Equals(sc.head.Name, procName, StringComparison.Ordinal));
            }
            else if (!string.IsNullOrEmpty(procValue))
            {
                string dynamicProcName = Context.ValueStore
                    .GetValueByNameForProcess(procValue, evt.procId).GetCValue();
                proc = procs.FirstOrDefault(sc => sc?.head != null
                    && string.Equals(sc.head.Name, dynamicProcName, StringComparison.Ordinal));
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
            return GetProcState(index);
        }
        public bool RunGoto(ProcHandle evt, Goto gotoParam)
        {
            if (gotoParam == null)
            {
                MarkAlarm(evt, "跳转参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            GotoRuntimeBinding binding = gotoParam.RuntimeBinding as GotoRuntimeBinding;
            string bindError = null;
            if (binding == null && (evt?.Proc == null
                || !ProcessRuntimeBinder.TryBind(
                    evt.Proc, evt.procNum, Context?.ValueStore, out bindError)))
            {
                throw CreateAlarmException(evt, bindError ?? "跳转运行计划未编译");
            }
            binding = binding ?? gotoParam.RuntimeBinding as GotoRuntimeBinding;
            if (binding == null)
            {
                throw CreateAlarmException(evt, "跳转运行计划未编译");
            }
            if (binding.Cases.Length > 0)
            {
                ValueConfigStore valueStore = Context?.ValueStore;
                if (!binding.Source.TryResolveValue(valueStore, "跳转变量", evt.procId, out DicValue sourceItem, out string sourceResolveError))
                {
                    throw CreateAlarmException(evt, sourceResolveError);
                }
                string value = sourceItem.Value;
                if (string.IsNullOrEmpty(value))
                {
                    throw CreateAlarmException(evt, "匹配不到变量");
                }
                foreach (GotoCaseRuntimeBinding item in binding.Cases)
                {
                    string itemValue = item.Literal;
                    if (item.UsesValueRef)
                    {
                        if (!item.ValueRef.TryResolveValue(valueStore, "匹配值", evt.procId, out DicValue matchItem, out string matchResolveError))
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
                        if (!item.Target.TryApply(evt, out string gotoError))
                        {
                            MarkAlarm(evt, gotoError);
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        evt.isGoto = true;
                        return true;
                    }
                }
            }
            if (binding.HasDefaultTarget)
            {
                if (!binding.DefaultTarget.TryApply(evt, out string defaultGotoError))
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
            if (paramGoto.InvalidDelayMs < 0)
            {
                MarkAlarm(evt, $"失败延时不能为负数:{paramGoto.InvalidDelayMs}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            ParamGotoRuntimeBinding binding = paramGoto.RuntimeBinding as ParamGotoRuntimeBinding;
            string bindError = null;
            if (binding == null && (evt?.Proc == null
                || !ProcessRuntimeBinder.TryBind(
                    evt.Proc, evt.procNum, Context?.ValueStore, out bindError)))
            {
                throw CreateAlarmException(evt, bindError ?? "逻辑判断运行计划未编译");
            }
            binding = binding ?? paramGoto.RuntimeBinding as ParamGotoRuntimeBinding;
            if (binding == null || binding.Conditions.Length != paramGoto.Params.Count)
            {
                throw CreateAlarmException(evt, "逻辑判断运行计划未编译");
            }
            if (paramGoto.Params != null)
            {
                bool isFirst = true;
                bool outPut = true;
                ValueConfigStore valueStore = Context?.ValueStore;
                for (int conditionIndex = 0; conditionIndex < paramGoto.Params.Count; conditionIndex++)
                {
                    ParamGotoParam item = paramGoto.Params[conditionIndex];
                    bool isNumericJudge = item.JudgeMode != "等于特征字符";
                    double numericValue = 0;
                    string textValue = null;
                    if (!binding.Conditions[conditionIndex].TryResolveValue(valueStore, "判断变量", evt.procId, out DicValue valueItem, out string valueResolveError))
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
                        if (item.IncludeBoundary)
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
                        if (item.IncludeBoundary)
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
                        if (item.IncludeBoundary)
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
                        tempValue = textValue == item.ExpectedText ? true : false;
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
                if (!outPut && paramGoto.InvalidDelayMs > 0)
                {
                    Delay(paramGoto.InvalidDelayMs, evt);
                }
                RuntimeGotoTarget gotoTarget = outPut ? binding.TrueTarget : binding.FalseTarget;
                if (!gotoTarget.TryApply(evt, out string gotoError))
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
            bool hasFixedDelay = delay.DelayMs.HasValue;
            bool hasVariableDelay = !string.IsNullOrWhiteSpace(delay.DelayVariableName);
            if (hasFixedDelay && hasVariableDelay)
            {
                MarkAlarm(evt, "固定延时与延时变量不能同时配置");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (hasFixedDelay)
            {
                time = delay.DelayMs.Value;
            }
            else if (hasVariableDelay)
            {
                string valueText = Context.ValueStore.GetValueByNameForProcess(delay.DelayVariableName, evt.procId).Value;
                if (!int.TryParse(valueText, out time) || time < 0)
                {
                    MarkAlarm(evt, $"延时变量无效:{delay.DelayVariableName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            if (time < 0)
            {
                MarkAlarm(evt, $"延时时间不能为负数:{time}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            Delay(time,evt);
            return true;
        }
    }
}
