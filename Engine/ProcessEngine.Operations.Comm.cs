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
        public bool RunTcpOps(ProcHandle evt, TcpOps tcpOps)
        {
            foreach (var op in tcpOps.Params)
            {
                SocketInfo socketInfo = Context.SocketInfos.FirstOrDefault(sc => sc.Name == op.Name);
                if (socketInfo == null)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"TCP配置不存在:{op.Name}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                if (op.Ops == "启动")
                {
                    _ = Context.Comm.StartTcpAsync(socketInfo);
                }
                else
                {
                    _ = Context.Comm.StopTcpAsync(op.Name);

                }

            }
            return true;

        }

        public bool RunWaitTcp(ProcHandle evt, WaitTcp waitTcp)
        {
            foreach (var op in waitTcp.Params)
            {
                if (op.TimeOut <= 0)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"等待TCP连接超时配置无效:{op.Name}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!evt.CancellationToken.IsCancellationRequested)
                {
                    if (stopwatch.ElapsedMilliseconds > op.TimeOut)
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"等待TCP连接超时:{op.Name}";
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }
                    if (Context.Comm.IsTcpActive(op.Name))
                    {
                        return true;
                    }
                    Delay(5, evt);
                }
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
            }
            return true;
        }

        public bool RunSendTcpMsg(ProcHandle evt, SendTcpMsg sendTcpMsg)
        {
            if (sendTcpMsg.TimeOut <= 0)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"TCP发送超时配置无效:{sendTcpMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }
            Task<bool> sendTask = Context.Comm.SendTcpAsync(sendTcpMsg.ID, Context.ValueStore.get_Str_ValueByName(sendTcpMsg.Msg), sendTcpMsg.isConVert, evt.CancellationToken);
            Task completed = Task.WhenAny(sendTask, Task.Delay(sendTcpMsg.TimeOut, evt.CancellationToken)).GetAwaiter().GetResult();
            if (!ReferenceEquals(completed, sendTask))
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"TCP发送超时:{sendTcpMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            bool success = sendTask.GetAwaiter().GetResult();
            if (!success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"TCP发送失败:{sendTcpMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            return true;
        }

        public bool RunReceoveTcpMsg(ProcHandle evt, ReceoveTcpMsg receoveTcpMsg)
        {
            if (!Context.Comm.IsTcpActive(receoveTcpMsg.ID))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"TCP未连接:{receoveTcpMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            if (receoveTcpMsg.TImeOut <= 0)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"TCP接收超时配置无效:{receoveTcpMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            Context.Comm.ClearTcpMessages(receoveTcpMsg.ID);
            if (Context.Comm.TryReceiveTcp(receoveTcpMsg.ID, receoveTcpMsg.TImeOut, evt.CancellationToken, out string msg))
            {
                if (receoveTcpMsg.isConVert && int.TryParse(msg, out int number))
                {
                    msg = Convert.ToString(number, 16).ToUpper();
                }
                if (!Context.ValueStore.setValueByName(receoveTcpMsg.MsgSaveValue, msg))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存TCP接收变量失败:{receoveTcpMsg.MsgSaveValue}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                return true;
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }
            evt.isAlarm = true;
            evt.alarmMsg = $"TCP接收超时:{receoveTcpMsg.ID}";
            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
        }
        public bool RunSendSerialPortMsg(ProcHandle evt, SendSerialPortMsg sendSerialPortMsg)
        {
            if (sendSerialPortMsg.TimeOut <= 0)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"串口发送超时配置无效:{sendSerialPortMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }
            Task<bool> sendTask = Context.Comm.SendSerialAsync(sendSerialPortMsg.ID, Context.ValueStore.get_Str_ValueByName(sendSerialPortMsg.Msg), sendSerialPortMsg.isConVert, evt.CancellationToken);
            Task completed = Task.WhenAny(sendTask, Task.Delay(sendSerialPortMsg.TimeOut, evt.CancellationToken)).GetAwaiter().GetResult();
            if (!ReferenceEquals(completed, sendTask))
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"串口发送超时:{sendSerialPortMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            bool success = sendTask.GetAwaiter().GetResult();
            if (!success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"串口发送失败:{sendSerialPortMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            return true;
        }
        public bool RunReceoveSerialPortMsg(ProcHandle evt, ReceoveSerialPortMsg receoveSerialPortMsg)
        {
            if (!Context.Comm.IsSerialOpen(receoveSerialPortMsg.ID))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"串口未打开:{receoveSerialPortMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            if (receoveSerialPortMsg.TImeOut <= 0)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"串口接收超时配置无效:{receoveSerialPortMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            Context.Comm.ClearSerialMessages(receoveSerialPortMsg.ID);
            if (Context.Comm.TryReceiveSerial(receoveSerialPortMsg.ID, receoveSerialPortMsg.TImeOut, evt.CancellationToken, out string msg))
            {
                if (int.TryParse(msg, out int number))
                {
                    msg = Convert.ToString(number, 16).ToUpper();
                }
                if (!Context.ValueStore.setValueByName(receoveSerialPortMsg.MsgSaveValue, msg))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存串口接收变量失败:{receoveSerialPortMsg.MsgSaveValue}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                return true;
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }
            evt.isAlarm = true;
            evt.alarmMsg = $"串口接收超时:{receoveSerialPortMsg.ID}";
            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
        }

        public bool RunSendReceoveCommMsg(ProcHandle evt, SendReceoveCommMsg sendReceoveCommMsg)
        {
            if (sendReceoveCommMsg == null || Context.Comm == null || string.IsNullOrEmpty(sendReceoveCommMsg.ID))
            {
                evt.isAlarm = true;
                evt.alarmMsg = "通讯参数无效";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            if (sendReceoveCommMsg.TimeOut <= 0)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"通讯超时配置无效:{sendReceoveCommMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }

            string sendValue = Context.ValueStore.get_Str_ValueByName(sendReceoveCommMsg.SendMsg);
            string commType = sendReceoveCommMsg.CommType ?? "TCP";

            if (string.Equals(commType, "TCP", StringComparison.OrdinalIgnoreCase))
            {
                if (!Context.Comm.IsTcpActive(sendReceoveCommMsg.ID))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"TCP未连接:{sendReceoveCommMsg.ID}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                Context.Comm.ClearTcpMessages(sendReceoveCommMsg.ID);
                Task<bool> sendTask = Context.Comm.SendTcpAsync(sendReceoveCommMsg.ID, sendValue, sendReceoveCommMsg.SendConvert, evt.CancellationToken);
                Task completed = Task.WhenAny(sendTask, Task.Delay(sendReceoveCommMsg.TimeOut, evt.CancellationToken)).GetAwaiter().GetResult();
                if (!ReferenceEquals(completed, sendTask))
                {
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                    evt.isAlarm = true;
                    evt.alarmMsg = $"TCP发送超时:{sendReceoveCommMsg.ID}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                bool sendSuccess = sendTask.GetAwaiter().GetResult();
                if (!sendSuccess)
                {
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                    evt.isAlarm = true;
                    evt.alarmMsg = $"TCP发送失败:{sendReceoveCommMsg.ID}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }

                if (Context.Comm.TryReceiveTcp(sendReceoveCommMsg.ID, sendReceoveCommMsg.TimeOut, evt.CancellationToken, out string msg))
                {
                    if (sendReceoveCommMsg.ReceiveConvert && int.TryParse(msg, out int number))
                    {
                        msg = Convert.ToString(number, 16).ToUpper();
                    }
                    if (!string.IsNullOrEmpty(sendReceoveCommMsg.ReceiveSaveValue))
                    {
                        if (!Context.ValueStore.setValueByName(sendReceoveCommMsg.ReceiveSaveValue, msg))
                        {
                            evt.isAlarm = true;
                            evt.alarmMsg = $"保存TCP接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}";
                            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                        }
                    }
                    return true;
                }
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"TCP接收超时:{sendReceoveCommMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }

            if (string.Equals(commType, "串口", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(commType, "Serial", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(commType, "SerialPort", StringComparison.OrdinalIgnoreCase))
            {
                if (!Context.Comm.IsSerialOpen(sendReceoveCommMsg.ID))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"串口未打开:{sendReceoveCommMsg.ID}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                Context.Comm.ClearSerialMessages(sendReceoveCommMsg.ID);
                Task<bool> sendTask = Context.Comm.SendSerialAsync(sendReceoveCommMsg.ID, sendValue, sendReceoveCommMsg.SendConvert, evt.CancellationToken);
                Task completed = Task.WhenAny(sendTask, Task.Delay(sendReceoveCommMsg.TimeOut, evt.CancellationToken)).GetAwaiter().GetResult();
                if (!ReferenceEquals(completed, sendTask))
                {
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                    evt.isAlarm = true;
                    evt.alarmMsg = $"串口发送超时:{sendReceoveCommMsg.ID}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                bool sendSuccess = sendTask.GetAwaiter().GetResult();
                if (!sendSuccess)
                {
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                    evt.isAlarm = true;
                    evt.alarmMsg = $"串口发送失败:{sendReceoveCommMsg.ID}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }

                if (Context.Comm.TryReceiveSerial(sendReceoveCommMsg.ID, sendReceoveCommMsg.TimeOut, evt.CancellationToken, out string msg))
                {
                    if (sendReceoveCommMsg.ReceiveConvert && int.TryParse(msg, out int number))
                    {
                        msg = Convert.ToString(number, 16).ToUpper();
                    }
                    if (!string.IsNullOrEmpty(sendReceoveCommMsg.ReceiveSaveValue))
                    {
                        if (!Context.ValueStore.setValueByName(sendReceoveCommMsg.ReceiveSaveValue, msg))
                        {
                            evt.isAlarm = true;
                            evt.alarmMsg = $"保存串口接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}";
                            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                        }
                    }
                    return true;
                }
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"串口接收超时:{sendReceoveCommMsg.ID}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }

            evt.isAlarm = true;
            evt.alarmMsg = $"通讯类型不支持:{sendReceoveCommMsg.CommType}";
            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
        }

        public bool RunSerialPortOps(ProcHandle evt, SerialPortOps serialPortOps)
        {
            foreach (var op in serialPortOps.Params)
            {
                SerialPortInfo serialPortInfo = Context.SerialPortInfos.FirstOrDefault(sc => sc.Name == op.Name);
                if (serialPortInfo == null)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"串口配置不存在:{op.Name}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                if (op.Ops == "启动")
                {
                    _ = Context.Comm.StartSerialAsync(serialPortInfo);
                }
                else
                {
                    _ = Context.Comm.StopSerialAsync(op.Name);
                }

            }
            return true;

        }

        public bool RunWaitSerialPort(ProcHandle evt, WaitSerialPort waitSerialPort)
        {
            foreach (var op in waitSerialPort.Params)
            {
                if (op.TimeOut <= 0)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"等待串口连接超时配置无效:{op.Name}";
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!evt.CancellationToken.IsCancellationRequested
                    && stopwatch.ElapsedMilliseconds < op.TimeOut)
                {
                    if (Context.Comm.IsSerialOpen(op.Name))
                    {
                        return true;
                    }
                    Delay(5, evt);
                }
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"等待串口连接超时:{op.Name}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            return true;
        }

    }
}
