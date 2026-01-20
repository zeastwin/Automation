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
        public bool RunIoOperate(ProcHandle evt, IoOperate ioOperate)
        {
            int time;
            foreach (IoOutParam ioParam in ioOperate.IoParams)
            {
                time = ioParam.delayBefore;
                if (time <= 0 && !string.IsNullOrEmpty(ioParam.delayBeforeV))
                {
                    time = (int)Context.ValueStore.GetValueByName(ioParam.delayBeforeV).GetDValue();
                }
                Delay(time, evt);
                if (!Context.Motion.SetIO(Context.IoMap[ioParam.IOName], ioParam.value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"IO输出失败:{ioParam.IOName}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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

            timeOut = ioCheck.timeOutC.TimeOut;
            if (timeOut <= 0 && !string.IsNullOrEmpty(ioCheck.timeOutC.TimeOutValue))
            {
                timeOut = (int)Context.ValueStore.GetValueByName(ioCheck.timeOutC.TimeOutValue).GetDValue();
            }
            if (timeOut <= 0)
            {
                evt.isAlarm = true;
                evt.alarmMsg = "IO检测超时配置无效";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }

            //time = ioParam.delayBefore;
            //if (time <= 0 && ioParam.delayBeforeV != "")
            //{
            //    time = (int)Context.ValueStore.GetValueByName(ioParam.delayBeforeV).GetDValue();
            //}
            //Delay(time, evt);
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (evt.State != ProcRunState.Stopped
                && !evt.CancellationToken.IsCancellationRequested)
            {
                if (stopwatch.ElapsedMilliseconds < timeOut)
                {
                    bool isCheckOff = true;
                    foreach (IoCheckParam ioParam in ioCheck.IoParams)
                    {
                        if (Context.IoMap[ioParam.IOName].IOType == "通用输入")
                        {
                            Context.Motion.GetInIO(Context.IoMap[ioParam.IOName], ref value);

                        }
                        else
                        {
                            Context.Motion.GetOutIO(Context.IoMap[ioParam.IOName], ref value);

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
                    evt.isAlarm = true;
                    evt.alarmMsg = "检测超时";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
            }
            //time = ioParam.delayAfter;
            //if (time <= 0 && ioParam.delayAfterV != "")
            //{
            //    time = (int)Context.ValueStore.GetValueByName(ioParam.delayAfterV).GetDValue();
            //}
            //Delay(time, evt);

            return true;
        }
    }
}
