using System;
using System.Diagnostics;

namespace Automation
{
    public partial class ProcessEngine
    {
        public bool RunIoOperate(ProcHandle evt, IoOperate ioOperate)
        {
            if (ioOperate == null || ioOperate.IoParams == null || ioOperate.IoParams.Count == 0)
            {
                MarkAlarm(evt, "IO操作参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context?.Motion == null)
            {
                MarkAlarm(evt, "运动控制未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            int time;
            foreach (IoOutParam ioParam in ioOperate.IoParams)
            {
                time = ioParam.delayBefore;
                if (time <= 0 && !string.IsNullOrEmpty(ioParam.delayBeforeV))
                {
                    time = (int)Context.ValueStore.GetValueByName(ioParam.delayBeforeV).GetDValue();
                }
                Delay(time, evt);
                if (Context?.IoMap == null || !Context.IoMap.TryGetValue(ioParam.IOName, out IO io) || io == null)
                {
                    MarkAlarm(evt, $"IO映射不存在:{ioParam.IOName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.Motion.SetIO(io, ioParam.value))
                {
                    MarkAlarm(evt, $"IO输出失败:{ioParam.IOName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                time = ioParam.delayAfter;
                if (time <= 0 && !string.IsNullOrEmpty(ioParam.delayAfterV))
                {
                    time = (int)Context.ValueStore.GetValueByName(ioParam.delayAfterV).GetDValue();
                }
                Delay(time, evt);
            }
            return true;
        }
        public bool RunIoCheck(ProcHandle evt, IoCheck ioCheck)
        {
            bool value = false;
            //    int time;
            double timeOut;

            if (ioCheck == null || ioCheck.IoParams == null || ioCheck.IoParams.Count == 0)
            {
                MarkAlarm(evt, "IO检测参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            timeOut = ioCheck.timeOutC.TimeOut;
            if (timeOut <= 0 && !string.IsNullOrEmpty(ioCheck.timeOutC.TimeOutValue))
            {
                timeOut = (int)Context.ValueStore.GetValueByName(ioCheck.timeOutC.TimeOutValue).GetDValue();
            }
            if (timeOut <= 0)
            {
                MarkAlarm(evt, "IO检测超时配置无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (evt.State != ProcRunState.Stopped
                && !evt.CancellationToken.IsCancellationRequested)
            {
                if (stopwatch.ElapsedMilliseconds < timeOut)
                {
                    bool isCheckOff = true;
                    foreach (IoCheckParam ioParam in ioCheck.IoParams)
                    {
                        if (Context?.IoMap == null || !Context.IoMap.TryGetValue(ioParam.IOName, out IO io) || io == null)
                        {
                            MarkAlarm(evt, $"IO映射不存在:{ioParam.IOName}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        bool ok;
                        if (io.IOType == "通用输入")
                        {
                            ok = Context.Motion != null && Context.Motion.GetInIO(io, ref value);
                        }
                        else if (io.IOType == "通用输出")
                        {
                            ok = Context.Motion != null && Context.Motion.GetOutIO(io, ref value);
                        }
                        else
                        {
                            MarkAlarm(evt, $"IO类型无效:{ioParam.IOName}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        if (!ok)
                        {
                            MarkAlarm(evt, $"IO读取失败:{ioParam.IOName}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        if (value != ioParam.value)
                        {
                            isCheckOff = false;
                            Delay(2, evt);
                            break;
                        }               
                    }
                    if (isCheckOff)
                    {
                        break;
                    }
                }
                else
                {
                    MarkAlarm(evt, "检测超时");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            return true;
        }

        public bool RunIoGroup(ProcHandle evt, IoGroup ioGroup)
        {
            if (ioGroup == null)
            {
                MarkAlarm(evt, "IO组参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (ioGroup.OutIoParams == null || ioGroup.OutIoParams.Count == 0)
            {
                MarkAlarm(evt, "IO组输出参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (ioGroup.CheckIoParams == null || ioGroup.CheckIoParams.Count == 0)
            {
                MarkAlarm(evt, "IO组检测参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context?.IoMap == null)
            {
                MarkAlarm(evt, "IO映射未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            foreach (IoOutParam ioParam in ioGroup.OutIoParams)
            {
                if (ioParam == null || string.IsNullOrWhiteSpace(ioParam.IOName))
                {
                    MarkAlarm(evt, "IO组输出名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.IoMap.TryGetValue(ioParam.IOName, out IO io) || io == null)
                {
                    MarkAlarm(evt, $"IO映射不存在:{ioParam.IOName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (io.IOType != "通用输出")
                {
                    MarkAlarm(evt, $"IO组输出仅支持通用输出:{ioParam.IOName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            foreach (IoCheckParam ioParam in ioGroup.CheckIoParams)
            {
                if (ioParam == null || string.IsNullOrWhiteSpace(ioParam.IOName))
                {
                    MarkAlarm(evt, "IO组检测名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.IoMap.TryGetValue(ioParam.IOName, out IO io) || io == null)
                {
                    MarkAlarm(evt, $"IO映射不存在:{ioParam.IOName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (io.IOType != "通用输入")
                {
                    MarkAlarm(evt, $"IO组检测仅支持通用输入:{ioParam.IOName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }

            try
            {
                RunIoOperate(evt, new IoOperate
                {
                    IoParams = ioGroup.OutIoParams
                });
            }
            catch (Exception ex)
            {
                MarkAlarm(evt, $"IO组执行失败(输出阶段):{ex.Message}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            try
            {
                return RunIoCheck(evt, new IoCheck
                {
                    IoParams = ioGroup.CheckIoParams,
                    timeOutC = ioGroup.timeOutC ?? new TimeOutC()
                });
            }
            catch (Exception ex)
            {
                MarkAlarm(evt, $"IO组执行失败(检测阶段):{ex.Message}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
        }

        public bool RunIoLogicGoto(ProcHandle evt, IoLogicGoto ioLogicGoto)
        {
            if (ioLogicGoto == null)
            {
                MarkAlarm(evt, "IO逻辑跳转参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (ioLogicGoto.IoParams == null || ioLogicGoto.IoParams.Count == 0)
            {
                MarkAlarm(evt, "IO逻辑跳转参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (ioLogicGoto.InvalidDelayMs < 0)
            {
                MarkAlarm(evt, $"失效延时无效:{ioLogicGoto.InvalidDelayMs}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            bool EvaluateLogic()
            {
                bool isFirst = true;
                bool output = false;
                foreach (IoLogicGotoParam ioParam in ioLogicGoto.IoParams)
                {
                    if (string.IsNullOrWhiteSpace(ioParam.IOName))
                    {
                        throw CreateAlarmException(evt, "IO名称为空");
                    }
                    if (Context?.IoMap == null || !Context.IoMap.TryGetValue(ioParam.IOName, out IO io) || io == null)
                    {
                        MarkAlarm(evt, $"IO映射不存在:{ioParam.IOName}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    bool value = false;
                    bool ok;
                    if (io.IOType == "通用输入")
                    {
                        ok = Context.Motion != null && Context.Motion.GetInIO(io, ref value);
                    }
                    else if (io.IOType == "通用输出")
                    {
                        ok = Context.Motion != null && Context.Motion.GetOutIO(io, ref value);
                    }
                    else
                    {
                        MarkAlarm(evt, $"IO类型无效:{ioParam.IOName}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!ok)
                    {
                        MarkAlarm(evt, $"IO读取失败:{ioParam.IOName}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    bool match = value == ioParam.Target;
                    if (isFirst)
                    {
                        output = match;
                        isFirst = false;
                        continue;
                    }

                    if (ioParam.Logic == "与")
                    {
                        output = output && match;
                    }
                    else if (ioParam.Logic == "或")
                    {
                        output = output || match;
                    }
                    else
                    {
                        throw CreateAlarmException(evt, $"逻辑无效:{ioParam.Logic}");
                    }
                }
                return output;
            }

            bool result = EvaluateLogic();
            if (!result && ioLogicGoto.InvalidDelayMs > 0)
            {
                Delay(ioLogicGoto.InvalidDelayMs, evt);
                result = EvaluateLogic();
            }

            string gotoTarget = result ? ioLogicGoto.TrueGoto : ioLogicGoto.FalseGoto;
            if (!TryExecuteGoto(gotoTarget, evt, out string gotoError))
            {
                MarkAlarm(evt, gotoError);
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            evt.isGoto = true;
            return true;
        }
    }
}
