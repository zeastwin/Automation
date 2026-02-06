using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

                SocketInfo socketInfo = Context.SocketInfos.FirstOrDefault(sc => sc != null && sc.Name == op.Name);
                if (socketInfo == null)
                {
                    MarkAlarm(evt, $"TCP配置不存在:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

                if (string.Equals(op.Ops, "启动", StringComparison.Ordinal))
                {
                    Context.Comm.StartTcpAsync(socketInfo).GetAwaiter().GetResult();
                    TcpStatus status = Context.Comm.GetTcpStatus(op.Name);
                    bool isServer = string.Equals(socketInfo.Type, "Server", StringComparison.Ordinal);
                    bool started = isServer ? status.IsServer && status.IsRunning : !status.IsServer && status.IsRunning;
                    if (!started)
                    {
                        MarkAlarm(evt, $"TCP启动失败:{op.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    continue;
                }

                if (string.Equals(op.Ops, "断开", StringComparison.Ordinal))
                {
                    Context.Comm.StopTcpAsync(op.Name).GetAwaiter().GetResult();
                    TcpStatus status = Context.Comm.GetTcpStatus(op.Name);
                    if (status.IsRunning)
                    {
                        MarkAlarm(evt, $"TCP断开失败:{op.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    continue;
                }

                MarkAlarm(evt, $"TCP操作类型无效:{op.Ops}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
            if (Context.SocketInfos == null)
            {
                MarkAlarm(evt, "TCP配置为空");
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
                if (!Context.SocketInfos.Any(info => info != null && info.Name == op.Name))
                {
                    MarkAlarm(evt, $"TCP配置不存在:{op.Name}");
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
            if (Context?.Comm == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (sendTcpMsg == null || string.IsNullOrWhiteSpace(sendTcpMsg.ID))
            {
                MarkAlarm(evt, "TCP发送参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (sendTcpMsg.TimeOut <= 0)
            {
                MarkAlarm(evt, $"TCP发送超时配置无效:{sendTcpMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }

            string sendValue = Context.ValueStore.get_Str_ValueByName(sendTcpMsg.Msg);
            Task<bool> sendTask = Context.Comm.SendTcpAsync(sendTcpMsg.ID, sendValue, sendTcpMsg.isConVert, evt.CancellationToken);
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
            if (Context?.Comm == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (receoveTcpMsg == null || string.IsNullOrWhiteSpace(receoveTcpMsg.ID))
            {
                MarkAlarm(evt, "TCP接收参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
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

            CommReceiveResult receiveResult = Context.Comm.ReceiveTcpAsync(receoveTcpMsg.ID, receoveTcpMsg.TImeOut, evt.CancellationToken).GetAwaiter().GetResult();
            if (!receiveResult.Success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }

                string error = string.IsNullOrWhiteSpace(receiveResult.ErrorMessage)
                    ? $"TCP接收失败:{receoveTcpMsg.ID}"
                    : $"TCP接收失败:{receoveTcpMsg.ID}，{receiveResult.ErrorMessage}";
                MarkAlarm(evt, error);
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            string msg = receoveTcpMsg.isConVert ? receiveResult.MessageHex : receiveResult.MessageText;
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            if (!Context.ValueStore.setValueByName(receoveTcpMsg.MsgSaveValue, msg, source))
            {
                MarkAlarm(evt, $"保存TCP接收变量失败:{receoveTcpMsg.MsgSaveValue}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            return true;
        }

        public bool RunSendSerialPortMsg(ProcHandle evt, SendSerialPortMsg sendSerialPortMsg)
        {
            if (Context?.Comm == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (sendSerialPortMsg == null || string.IsNullOrWhiteSpace(sendSerialPortMsg.ID))
            {
                MarkAlarm(evt, "串口发送参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (sendSerialPortMsg.TimeOut <= 0)
            {
                MarkAlarm(evt, $"串口发送超时配置无效:{sendSerialPortMsg.ID}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }

            string sendValue = Context.ValueStore.get_Str_ValueByName(sendSerialPortMsg.Msg);
            Task<bool> sendTask = Context.Comm.SendSerialAsync(sendSerialPortMsg.ID, sendValue, sendSerialPortMsg.isConVert, evt.CancellationToken);
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
            if (Context?.Comm == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (receoveSerialPortMsg == null || string.IsNullOrWhiteSpace(receoveSerialPortMsg.ID))
            {
                MarkAlarm(evt, "串口接收参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
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

            CommReceiveResult receiveResult = Context.Comm.ReceiveSerialAsync(receoveSerialPortMsg.ID, receoveSerialPortMsg.TImeOut, evt.CancellationToken).GetAwaiter().GetResult();
            if (!receiveResult.Success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }

                string error = string.IsNullOrWhiteSpace(receiveResult.ErrorMessage)
                    ? $"串口接收失败:{receoveSerialPortMsg.ID}"
                    : $"串口接收失败:{receoveSerialPortMsg.ID}，{receiveResult.ErrorMessage}";
                MarkAlarm(evt, error);
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            string msg = receoveSerialPortMsg.isConVert ? receiveResult.MessageHex : receiveResult.MessageText;
            string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            if (!Context.ValueStore.setValueByName(receoveSerialPortMsg.MsgSaveValue, msg, source))
            {
                MarkAlarm(evt, $"保存串口接收变量失败:{receoveSerialPortMsg.MsgSaveValue}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            return true;
        }

        public bool RunSendReceoveCommMsg(ProcHandle evt, SendReceoveCommMsg sendReceoveCommMsg)
        {
            if (sendReceoveCommMsg == null || Context?.Comm == null || string.IsNullOrWhiteSpace(sendReceoveCommMsg.ID))
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
            if (string.IsNullOrWhiteSpace(sendReceoveCommMsg.CommType))
            {
                MarkAlarm(evt, "通讯类型为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            string sendValue = Context.ValueStore.get_Str_ValueByName(sendReceoveCommMsg.SendMsg);

            if (string.Equals(sendReceoveCommMsg.CommType, "TCP", StringComparison.Ordinal))
            {
                if (!Context.Comm.IsTcpActive(sendReceoveCommMsg.ID))
                {
                    MarkAlarm(evt, $"TCP未连接:{sendReceoveCommMsg.ID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(evt.CancellationToken))
                {
                    Task<CommReceiveResult> receiveTask = Context.Comm.ReceiveTcpAsync(sendReceoveCommMsg.ID, sendReceoveCommMsg.TimeOut, linkedCts.Token);
                    Task<bool> sendTask = Context.Comm.SendTcpAsync(sendReceoveCommMsg.ID, sendValue, sendReceoveCommMsg.SendConvert, linkedCts.Token);

                    Task sendCompleted = Task.WhenAny(sendTask, Task.Delay(sendReceoveCommMsg.TimeOut, linkedCts.Token)).GetAwaiter().GetResult();
                    if (!ReferenceEquals(sendCompleted, sendTask))
                    {
                        linkedCts.Cancel();
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
                        linkedCts.Cancel();
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return true;
                        }
                        MarkAlarm(evt, $"TCP发送失败:{sendReceoveCommMsg.ID}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    Task receiveCompleted = Task.WhenAny(receiveTask, Task.Delay(sendReceoveCommMsg.TimeOut, linkedCts.Token)).GetAwaiter().GetResult();
                    if (!ReferenceEquals(receiveCompleted, receiveTask))
                    {
                        linkedCts.Cancel();
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return true;
                        }
                        MarkAlarm(evt, $"TCP接收超时:{sendReceoveCommMsg.ID}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    CommReceiveResult receiveResult = receiveTask.GetAwaiter().GetResult();
                    if (!receiveResult.Success)
                    {
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return true;
                        }
                        string receiveError = string.IsNullOrWhiteSpace(receiveResult.ErrorMessage) ? "未知错误" : receiveResult.ErrorMessage;
                        MarkAlarm(evt, $"TCP接收失败:{sendReceoveCommMsg.ID}，{receiveError}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    string msg = sendReceoveCommMsg.ReceiveConvert ? receiveResult.MessageHex : receiveResult.MessageText;
                    if (!string.IsNullOrEmpty(sendReceoveCommMsg.ReceiveSaveValue))
                    {
                        string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
                        if (!Context.ValueStore.setValueByName(sendReceoveCommMsg.ReceiveSaveValue, msg, source))
                        {
                            MarkAlarm(evt, $"保存TCP接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                    }
                }

                return true;
            }

            if (string.Equals(sendReceoveCommMsg.CommType, "串口", StringComparison.Ordinal))
            {
                if (!Context.Comm.IsSerialOpen(sendReceoveCommMsg.ID))
                {
                    MarkAlarm(evt, $"串口未打开:{sendReceoveCommMsg.ID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(evt.CancellationToken))
                {
                    Task<CommReceiveResult> receiveTask = Context.Comm.ReceiveSerialAsync(sendReceoveCommMsg.ID, sendReceoveCommMsg.TimeOut, linkedCts.Token);
                    Task<bool> sendTask = Context.Comm.SendSerialAsync(sendReceoveCommMsg.ID, sendValue, sendReceoveCommMsg.SendConvert, linkedCts.Token);

                    Task sendCompleted = Task.WhenAny(sendTask, Task.Delay(sendReceoveCommMsg.TimeOut, linkedCts.Token)).GetAwaiter().GetResult();
                    if (!ReferenceEquals(sendCompleted, sendTask))
                    {
                        linkedCts.Cancel();
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
                        linkedCts.Cancel();
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return true;
                        }
                        MarkAlarm(evt, $"串口发送失败:{sendReceoveCommMsg.ID}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    Task receiveCompleted = Task.WhenAny(receiveTask, Task.Delay(sendReceoveCommMsg.TimeOut, linkedCts.Token)).GetAwaiter().GetResult();
                    if (!ReferenceEquals(receiveCompleted, receiveTask))
                    {
                        linkedCts.Cancel();
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return true;
                        }
                        MarkAlarm(evt, $"串口接收超时:{sendReceoveCommMsg.ID}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    CommReceiveResult receiveResult = receiveTask.GetAwaiter().GetResult();
                    if (!receiveResult.Success)
                    {
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return true;
                        }
                        string receiveError = string.IsNullOrWhiteSpace(receiveResult.ErrorMessage) ? "未知错误" : receiveResult.ErrorMessage;
                        MarkAlarm(evt, $"串口接收失败:{sendReceoveCommMsg.ID}，{receiveError}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }

                    string msg = sendReceoveCommMsg.ReceiveConvert ? receiveResult.MessageHex : receiveResult.MessageText;
                    if (!string.IsNullOrEmpty(sendReceoveCommMsg.ReceiveSaveValue))
                    {
                        string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
                        if (!Context.ValueStore.setValueByName(sendReceoveCommMsg.ReceiveSaveValue, msg, source))
                        {
                            MarkAlarm(evt, $"保存串口接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                    }
                }

                return true;
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

                SerialPortInfo serialPortInfo = Context.SerialPortInfos.FirstOrDefault(sc => sc != null && sc.Name == op.Name);
                if (serialPortInfo == null)
                {
                    MarkAlarm(evt, $"串口配置不存在:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

                if (string.Equals(op.Ops, "启动", StringComparison.Ordinal))
                {
                    Context.Comm.StartSerialAsync(serialPortInfo).GetAwaiter().GetResult();
                    if (!Context.Comm.IsSerialOpen(op.Name))
                    {
                        MarkAlarm(evt, $"串口启动失败:{op.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    continue;
                }

                if (string.Equals(op.Ops, "断开", StringComparison.Ordinal))
                {
                    Context.Comm.StopSerialAsync(op.Name).GetAwaiter().GetResult();
                    if (Context.Comm.IsSerialOpen(op.Name))
                    {
                        MarkAlarm(evt, $"串口断开失败:{op.Name}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    continue;
                }

                MarkAlarm(evt, $"串口操作类型无效:{op.Ops}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
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
                if (Context.SerialPortInfos == null || !Context.SerialPortInfos.Any(info => info != null && info.Name == op.Name))
                {
                    MarkAlarm(evt, $"串口配置不存在:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!evt.CancellationToken.IsCancellationRequested && stopwatch.ElapsedMilliseconds < op.TimeOut)
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

                if (Context.Comm.IsSerialOpen(op.Name))
                {
                    continue;
                }

                MarkAlarm(evt, $"等待串口连接超时:{op.Name}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            return true;
        }
    }
}
