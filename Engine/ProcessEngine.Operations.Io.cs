using System;
using System.Collections.Generic;
using System.Diagnostics;
using Automation.MotionControl;

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
            if (Context?.Io == null)
            {
                MarkAlarm(evt, "运动控制未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            var commands = new List<IoOutputCommand>(ioOperate.IoParams.Count);
            var ioIndexes = new HashSet<int>();
            int? cardNum = null;
            foreach (IoOutParam ioParam in ioOperate.IoParams)
            {
                if (ioParam == null || string.IsNullOrWhiteSpace(ioParam.IoName))
                {
                    MarkAlarm(evt, "IO输出名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (Context?.IoMap == null || !Context.IoMap.TryGetValue(ioParam.IoName, out IO io) || io == null)
                {
                    MarkAlarm(evt, $"IO映射不存在:{ioParam.IoName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (io.IOType != "通用输出")
                {
                    MarkAlarm(evt, $"IO操作仅支持通用输出:{ioParam.IoName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (cardNum.HasValue && cardNum.Value != io.CardNum)
                {
                    MarkAlarm(evt, "同一条IO操作的全部输出必须位于同一张控制卡");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                cardNum = io.CardNum;
                if (!int.TryParse(io.IOIndex, out int ioIndex) || ioIndex < 0 || ioIndex > 31)
                {
                    MarkAlarm(evt, $"IO输出索引超出批量写入端口范围:{ioParam.IoName}-{io.IOIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!ioIndexes.Add(ioIndex))
                {
                    MarkAlarm(evt, $"IO操作包含重复输出索引:{ioParam.IoName}-{io.IOIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                commands.Add(new IoOutputCommand(io, ioParam.TargetState));
            }
            if (!Context.Io.SetOutputs(commands))
            {
                MarkAlarm(evt, $"IO批量输出失败:卡{cardNum}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
            timeOut = ioCheck.Timeout.TimeoutMs;
            if (timeOut <= 0 && !string.IsNullOrEmpty(ioCheck.Timeout.TimeoutVariableName))
            {
                timeOut = (int)Context.ValueStore.GetValueByNameForProcess(ioCheck.Timeout.TimeoutVariableName, evt.procId).GetDValue();
            }
            if (timeOut <= 0)
            {
                MarkAlarm(evt, "IO检测超时配置无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!evt.CancellationToken.IsCancellationRequested)
            {
                if (stopwatch.ElapsedMilliseconds < timeOut)
                {
                    bool isCheckOff = true;
                    foreach (IoCheckParam ioParam in ioCheck.IoParams)
                    {
                        if (Context?.IoMap == null || !Context.IoMap.TryGetValue(ioParam.IoName, out IO io) || io == null)
                        {
                            MarkAlarm(evt, $"IO映射不存在:{ioParam.IoName}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        bool ok;
                        if (io.IOType == "通用输入")
                        {
                            ok = Context.Io != null && Context.Io.GetInIO(io, ref value);
                        }
                        else if (io.IOType == "通用输出")
                        {
                            ok = Context.Io != null && Context.Io.GetOutIO(io, ref value);
                        }
                        else
                        {
                            MarkAlarm(evt, $"IO类型无效:{ioParam.IoName}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        if (!ok)
                        {
                            MarkAlarm(evt, $"IO读取失败:{ioParam.IoName}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        if (value != ioParam.ExpectedState)
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
                if (ioParam == null || string.IsNullOrWhiteSpace(ioParam.IoName))
                {
                    MarkAlarm(evt, "IO组输出名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.IoMap.TryGetValue(ioParam.IoName, out IO io) || io == null)
                {
                    MarkAlarm(evt, $"IO映射不存在:{ioParam.IoName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (io.IOType != "通用输出")
                {
                    MarkAlarm(evt, $"IO组输出仅支持通用输出:{ioParam.IoName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            foreach (IoCheckParam ioParam in ioGroup.CheckIoParams)
            {
                if (ioParam == null || string.IsNullOrWhiteSpace(ioParam.IoName))
                {
                    MarkAlarm(evt, "IO组检测名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.IoMap.TryGetValue(ioParam.IoName, out IO io) || io == null)
                {
                    MarkAlarm(evt, $"IO映射不存在:{ioParam.IoName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (io.IOType != "通用输入")
                {
                    MarkAlarm(evt, $"IO组检测仅支持通用输入:{ioParam.IoName}");
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
                    Timeout = ioGroup.Timeout ?? new TimeoutSetting()
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
            BranchRuntimeBinding binding = ioLogicGoto.RuntimeBinding as BranchRuntimeBinding;
            string bindError = null;
            if (binding == null && (evt?.Proc == null
                || !ProcessRuntimeBinder.TryBind(
                    evt.Proc, evt.procNum, Context?.ValueStore, out bindError)))
            {
                throw CreateAlarmException(evt, bindError ?? "IO逻辑跳转运行计划未编译");
            }
            binding = binding ?? ioLogicGoto.RuntimeBinding as BranchRuntimeBinding;
            if (binding == null)
            {
                throw CreateAlarmException(evt, "IO逻辑跳转运行计划未编译");
            }

            bool EvaluateLogic()
            {
                bool isFirst = true;
                bool output = false;
                foreach (IoLogicGotoParam ioParam in ioLogicGoto.IoParams)
                {
                    if (string.IsNullOrWhiteSpace(ioParam.IoName))
                    {
                        throw CreateAlarmException(evt, "IO名称为空");
                    }
                    if (Context?.IoMap == null || !Context.IoMap.TryGetValue(ioParam.IoName, out IO io) || io == null)
                    {
                        MarkAlarm(evt, $"IO映射不存在:{ioParam.IoName}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!string.Equals(io.IOType, "通用输入", StringComparison.Ordinal))
                    {
                        MarkAlarm(evt, $"IO逻辑跳转仅支持通用输入:{ioParam.IoName}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    bool value = false;
                    bool ok = Context.Io != null && Context.Io.GetInIO(io, ref value);
                    if (!ok)
                    {
                        MarkAlarm(evt, $"IO读取失败:{ioParam.IoName}");
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

            RuntimeGotoTarget gotoTarget = result ? binding.TrueTarget : binding.FalseTarget;
            if (!gotoTarget.TryApply(evt, out string gotoError))
            {
                MarkAlarm(evt, gotoError);
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            evt.isGoto = true;
            return true;
        }
    }
}
