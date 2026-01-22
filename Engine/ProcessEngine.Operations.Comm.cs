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
            if (tcpOps == null || tcpOps.Params == null)
            {
                MarkAlarm(evt, "TCP操作参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context?.Comm == null || Context.SocketInfos == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            foreach (var op in tcpOps.Params)
            {
                if (string.IsNullOrWhiteSpace(op.Name))
                {
                    MarkAlarm(evt, "TCP对象名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                SocketInfo socketInfo = Context.SocketInfos.FirstOrDefault(sc => sc.Name == op.Name);
                if (socketInfo == null)
                {
                    MarkAlarm(evt, $"TCP配置不存在:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (op.Ops == "启动")
                {
                    Context.Comm.StartTcpAsync(socketInfo).GetAwaiter().GetResult();
                    if (!Context.Comm.IsTcpActive(op.Name))
                    {
                        MarkAlarm(evt, $"TCP启动失败:{op.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                else if (op.Ops == "断开")
                {
                    Context.Comm.StopTcpAsync(op.Name).GetAwaiter().GetResult();
                    if (Context.Comm.IsTcpActive(op.Name))
                    {
                        MarkAlarm(evt, $"TCP断开失败:{op.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                else
                {
                    MarkAlarm(evt, $"TCP操作类型无效:{op.Ops}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

            }
            return true;

        }

        public bool RunWaitTcp(ProcHandle evt, WaitTcp waitTcp)
        {
            if (waitTcp == null || waitTcp.Params == null)
            {
                MarkAlarm(evt, "等待TCP参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context?.Comm == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            foreach (var op in waitTcp.Params)
            {
                if (string.IsNullOrWhiteSpace(op.Name))
                {
                    MarkAlarm(evt, "TCP对象名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (op.TimeOut <= 0)
                {
                    MarkAlarm(evt, $"等待TCP连接超时配置无效:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!evt.CancellationToken.IsCancellationRequested)
                {
                    if (stopwatch.ElapsedMilliseconds > op.TimeOut)
                    {
                        MarkAlarm(evt, $"等待TCP连接超时:{op.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (Context.Comm.IsTcpActive(op.Name))
                    {
                        break;
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
                MarkAlarm(evt, $"TCP发送超时配置无效:{sendTcpMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                MarkAlarm(evt, $"TCP发送超时:{sendTcpMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            bool success = sendTask.GetAwaiter().GetResult();
            if (!success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                MarkAlarm(evt, $"TCP发送失败:{sendTcpMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            return true;
        }

        public bool RunReceoveTcpMsg(ProcHandle evt, ReceoveTcpMsg receoveTcpMsg)
        {
            if (!Context.Comm.IsTcpActive(receoveTcpMsg.ID))
            {
                MarkAlarm(evt, $"TCP未连接:{receoveTcpMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (receoveTcpMsg.TImeOut <= 0)
            {
                MarkAlarm(evt, $"TCP接收超时配置无效:{receoveTcpMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                    MarkAlarm(evt, $"保存TCP接收变量失败:{receoveTcpMsg.MsgSaveValue}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                return true;
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }
            MarkAlarm(evt, $"TCP接收超时:{receoveTcpMsg.ID}");
            throw CreateAlarmException(evt, evt?.alarmMsg);
        }
        public bool RunSendSerialPortMsg(ProcHandle evt, SendSerialPortMsg sendSerialPortMsg)
        {
            if (sendSerialPortMsg.TimeOut <= 0)
            {
                MarkAlarm(evt, $"串口发送超时配置无效:{sendSerialPortMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                MarkAlarm(evt, $"串口发送超时:{sendSerialPortMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            bool success = sendTask.GetAwaiter().GetResult();
            if (!success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                MarkAlarm(evt, $"串口发送失败:{sendSerialPortMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            return true;
        }
        public bool RunReceoveSerialPortMsg(ProcHandle evt, ReceoveSerialPortMsg receoveSerialPortMsg)
        {
            if (!Context.Comm.IsSerialOpen(receoveSerialPortMsg.ID))
            {
                MarkAlarm(evt, $"串口未打开:{receoveSerialPortMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (receoveSerialPortMsg.TImeOut <= 0)
            {
                MarkAlarm(evt, $"串口接收超时配置无效:{receoveSerialPortMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                    MarkAlarm(evt, $"保存串口接收变量失败:{receoveSerialPortMsg.MsgSaveValue}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                return true;
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }
            MarkAlarm(evt, $"串口接收超时:{receoveSerialPortMsg.ID}");
            throw CreateAlarmException(evt, evt?.alarmMsg);
        }

        public bool RunSendReceoveCommMsg(ProcHandle evt, SendReceoveCommMsg sendReceoveCommMsg)
        {
            if (sendReceoveCommMsg == null || Context.Comm == null || string.IsNullOrEmpty(sendReceoveCommMsg.ID))
            {
                MarkAlarm(evt, "通讯参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (sendReceoveCommMsg.TimeOut <= 0)
            {
                MarkAlarm(evt, $"通讯超时配置无效:{sendReceoveCommMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                    MarkAlarm(evt, $"TCP未连接:{sendReceoveCommMsg.ID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
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
                    MarkAlarm(evt, $"TCP发送超时:{sendReceoveCommMsg.ID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                bool sendSuccess = sendTask.GetAwaiter().GetResult();
                if (!sendSuccess)
                {
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                    MarkAlarm(evt, $"TCP发送失败:{sendReceoveCommMsg.ID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
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
                            MarkAlarm(evt, $"保存TCP接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                    }
                    return true;
                }
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                MarkAlarm(evt, $"TCP接收超时:{sendReceoveCommMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            if (string.Equals(commType, "串口", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(commType, "Serial", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(commType, "SerialPort", StringComparison.OrdinalIgnoreCase))
            {
                if (!Context.Comm.IsSerialOpen(sendReceoveCommMsg.ID))
                {
                    MarkAlarm(evt, $"串口未打开:{sendReceoveCommMsg.ID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
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
                    MarkAlarm(evt, $"串口发送超时:{sendReceoveCommMsg.ID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                bool sendSuccess = sendTask.GetAwaiter().GetResult();
                if (!sendSuccess)
                {
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                    MarkAlarm(evt, $"串口发送失败:{sendReceoveCommMsg.ID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
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
                            MarkAlarm(evt, $"保存串口接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                    }
                    return true;
                }
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                MarkAlarm(evt, $"串口接收超时:{sendReceoveCommMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            MarkAlarm(evt, $"通讯类型不支持:{sendReceoveCommMsg.CommType}");
            throw CreateAlarmException(evt, evt?.alarmMsg);
        }

        public bool RunSerialPortOps(ProcHandle evt, SerialPortOps serialPortOps)
        {
            if (serialPortOps == null || serialPortOps.Params == null)
            {
                MarkAlarm(evt, "串口操作参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context?.Comm == null || Context.SerialPortInfos == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            foreach (var op in serialPortOps.Params)
            {
                if (string.IsNullOrWhiteSpace(op.Name))
                {
                    MarkAlarm(evt, "串口对象名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                SerialPortInfo serialPortInfo = Context.SerialPortInfos.FirstOrDefault(sc => sc.Name == op.Name);
                if (serialPortInfo == null)
                {
                    MarkAlarm(evt, $"串口配置不存在:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (op.Ops == "启动")
                {
                    Context.Comm.StartSerialAsync(serialPortInfo).GetAwaiter().GetResult();
                    if (!Context.Comm.IsSerialOpen(op.Name))
                    {
                        MarkAlarm(evt, $"串口启动失败:{op.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                else if (op.Ops == "断开")
                {
                    Context.Comm.StopSerialAsync(op.Name).GetAwaiter().GetResult();
                    if (Context.Comm.IsSerialOpen(op.Name))
                    {
                        MarkAlarm(evt, $"串口断开失败:{op.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                else
                {
                    MarkAlarm(evt, $"串口操作类型无效:{op.Ops}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

            }
            return true;

        }

        public bool RunWaitSerialPort(ProcHandle evt, WaitSerialPort waitSerialPort)
        {
            if (waitSerialPort == null || waitSerialPort.Params == null)
            {
                MarkAlarm(evt, "等待串口参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context?.Comm == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            foreach (var op in waitSerialPort.Params)
            {
                if (string.IsNullOrWhiteSpace(op.Name))
                {
                    MarkAlarm(evt, "串口对象名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (op.TimeOut <= 0)
                {
                    MarkAlarm(evt, $"等待串口连接超时配置无效:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!evt.CancellationToken.IsCancellationRequested
                    && stopwatch.ElapsedMilliseconds < op.TimeOut)
                {
                    if (Context.Comm.IsSerialOpen(op.Name))
                    {
                        break;
                    }
                    Delay(5, evt);
                }
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                MarkAlarm(evt, $"等待串口连接超时:{op.Name}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            return true;
        }

    }
}
