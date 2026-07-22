using System;
// 模块：引擎 / 执行。
// 职责范围：负责运行绑定、调度、状态管理以及各类流程指令的确定性执行。

using System.Diagnostics;
using System.Linq;
using System.Threading;

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
            if (Context?.Comm == null)
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

                if (!TryGetSocketConfig(op.Name, out SocketInfo socketInfo))
                {
                    MarkAlarm(evt, $"TCP配置不存在:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

                if (string.Equals(op.Ops, "启动", StringComparison.Ordinal))
                {
                    Context.Comm.StartTcpAsync(socketInfo, 0, evt.CancellationToken).GetAwaiter().GetResult();
                    TcpStatus status = Context.Comm.GetTcpStatus(op.Name);
                    bool isServer = string.Equals(socketInfo.Type, "Server", StringComparison.Ordinal);
                    bool started = isServer ? status.IsServer && status.IsStarted : !status.IsServer && status.IsStarted;
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
                    if (status.IsStarted)
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
            foreach (var op in waitTcp.Params)
            {
                if (string.IsNullOrWhiteSpace(op.Name))
                {
                    MarkAlarm(evt, "TCP对象名称为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (op.TimeoutMs <= 0)
                {
                    MarkAlarm(evt, $"等待TCP连接超时配置无效:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!TryGetSocketConfig(op.Name, out _))
                {
                    MarkAlarm(evt, $"TCP配置不存在:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!evt.CancellationToken.IsCancellationRequested)
                {
                    if (stopwatch.ElapsedMilliseconds > op.TimeoutMs)
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
            if (sendTcpMsg == null || string.IsNullOrWhiteSpace(sendTcpMsg.ConnectionName))
            {
                MarkAlarm(evt, "TCP发送参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (sendTcpMsg.TimeoutMs <= 0)
            {
                MarkAlarm(evt, $"TCP发送超时配置无效:{sendTcpMsg.ConnectionName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }

            string sendValue = Context.ValueStore.GetValueByNameForProcess(
                sendTcpMsg.Msg, evt.procId).GetCValue();
            bool success;
            bool timedOut;
            using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(evt.CancellationToken))
            {
                timeoutCts.CancelAfter(sendTcpMsg.TimeoutMs);
                success = ExecuteRetryableCommunicationCall(evt, "TCP发送异常",
                    () => Context.Comm.SendTcpAsync(sendTcpMsg.ConnectionName, sendValue,
                        sendTcpMsg.UseHexEncoding, timeoutCts.Token).GetAwaiter().GetResult());
                timedOut = timeoutCts.IsCancellationRequested && !evt.CancellationToken.IsCancellationRequested;
            }
            if (!success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                throw CreateRetryableCommunicationException(evt,
                    timedOut ? $"TCP发送超时:{sendTcpMsg.ConnectionName}" : $"TCP发送失败:{sendTcpMsg.ConnectionName}");
            }
            return true;
        }

        public bool RunReceiveTcpMsg(ProcHandle evt, ReceiveTcpMsg receoveTcpMsg)
        {
            if (Context?.Comm == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (receoveTcpMsg == null || string.IsNullOrWhiteSpace(receoveTcpMsg.ConnectionName))
            {
                MarkAlarm(evt, "TCP接收参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!Context.Comm.IsTcpActive(receoveTcpMsg.ConnectionName))
            {
                throw CreateRetryableCommunicationException(
                    evt, $"TCP未连接:{receoveTcpMsg.ConnectionName}");
            }
            if (receoveTcpMsg.TimeoutMs <= 0)
            {
                MarkAlarm(evt, $"TCP接收超时配置无效:{receoveTcpMsg.ConnectionName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            CommReceiveResult receiveResult = ExecuteRetryableCommunicationCall(evt, "TCP接收异常",
                () => Context.Comm.ReceiveTcpAsync(receoveTcpMsg.ConnectionName,
                    receoveTcpMsg.TimeoutMs, evt.CancellationToken).GetAwaiter().GetResult());
            if (!receiveResult.Success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }

                string error = string.IsNullOrWhiteSpace(receiveResult.ErrorMessage)
                    ? $"TCP接收失败:{receoveTcpMsg.ConnectionName}"
                    : $"TCP接收失败:{receoveTcpMsg.ConnectionName}，{receiveResult.ErrorMessage}";
                throw CreateRetryableCommunicationException(evt, error);
            }

            string msg = receoveTcpMsg.UseHexEncoding ? receiveResult.MessageHex : receiveResult.MessageText;
            string source = evt?.GetOperationSource();
            if (!Context.ValueStore.SetValueByNameForProcess(receoveTcpMsg.MsgSaveValue, msg, evt.procId, source))
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
            if (sendSerialPortMsg == null || string.IsNullOrWhiteSpace(sendSerialPortMsg.ConnectionName))
            {
                MarkAlarm(evt, "串口发送参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (sendSerialPortMsg.TimeoutMs <= 0)
            {
                MarkAlarm(evt, $"串口发送超时配置无效:{sendSerialPortMsg.ConnectionName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return true;
            }

            string sendValue = Context.ValueStore.GetValueByNameForProcess(
                sendSerialPortMsg.Msg, evt.procId).GetCValue();
            bool success;
            bool timedOut;
            using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(evt.CancellationToken))
            {
                timeoutCts.CancelAfter(sendSerialPortMsg.TimeoutMs);
                success = ExecuteRetryableCommunicationCall(evt, "串口发送异常",
                    () => Context.Comm.SendSerialAsync(sendSerialPortMsg.ConnectionName, sendValue,
                        sendSerialPortMsg.UseHexEncoding, timeoutCts.Token).GetAwaiter().GetResult());
                timedOut = timeoutCts.IsCancellationRequested && !evt.CancellationToken.IsCancellationRequested;
            }
            if (!success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }
                throw CreateRetryableCommunicationException(evt,
                    timedOut ? $"串口发送超时:{sendSerialPortMsg.ConnectionName}" : $"串口发送失败:{sendSerialPortMsg.ConnectionName}");
            }
            return true;
        }

        public bool RunReceiveSerialPortMsg(ProcHandle evt, ReceiveSerialPortMsg receoveSerialPortMsg)
        {
            if (Context?.Comm == null)
            {
                MarkAlarm(evt, "通讯未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (receoveSerialPortMsg == null || string.IsNullOrWhiteSpace(receoveSerialPortMsg.ConnectionName))
            {
                MarkAlarm(evt, "串口接收参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!Context.Comm.IsSerialOpen(receoveSerialPortMsg.ConnectionName))
            {
                throw CreateRetryableCommunicationException(
                    evt, $"串口未打开:{receoveSerialPortMsg.ConnectionName}");
            }
            if (receoveSerialPortMsg.TimeoutMs <= 0)
            {
                MarkAlarm(evt, $"串口接收超时配置无效:{receoveSerialPortMsg.ConnectionName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            CommReceiveResult receiveResult = ExecuteRetryableCommunicationCall(evt, "串口接收异常",
                () => Context.Comm.ReceiveSerialAsync(receoveSerialPortMsg.ConnectionName,
                    receoveSerialPortMsg.TimeoutMs, evt.CancellationToken).GetAwaiter().GetResult());
            if (!receiveResult.Success)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }

                string error = string.IsNullOrWhiteSpace(receiveResult.ErrorMessage)
                    ? $"串口接收失败:{receoveSerialPortMsg.ConnectionName}"
                    : $"串口接收失败:{receoveSerialPortMsg.ConnectionName}，{receiveResult.ErrorMessage}";
                throw CreateRetryableCommunicationException(evt, error);
            }

            string msg = receoveSerialPortMsg.UseHexEncoding ? receiveResult.MessageHex : receiveResult.MessageText;
            string source = evt?.GetOperationSource();
            if (!Context.ValueStore.SetValueByNameForProcess(receoveSerialPortMsg.MsgSaveValue, msg, evt.procId, source))
            {
                MarkAlarm(evt, $"保存串口接收变量失败:{receoveSerialPortMsg.MsgSaveValue}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            return true;
        }

        public bool RunSendReceiveCommMsg(ProcHandle evt, SendReceiveCommMsg sendReceoveCommMsg)
        {
            if (sendReceoveCommMsg == null || Context?.Comm == null || string.IsNullOrWhiteSpace(sendReceoveCommMsg.ConnectionName))
            {
                MarkAlarm(evt, "通讯参数无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (sendReceoveCommMsg.TimeoutMs <= 0)
            {
                MarkAlarm(evt, $"通讯超时配置无效:{sendReceoveCommMsg.ConnectionName}");
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

            string sendValue = Context.ValueStore.GetValueByNameForProcess(
                sendReceoveCommMsg.SendMsg, evt.procId).GetCValue();

            if (string.Equals(sendReceoveCommMsg.CommType, "TCP", StringComparison.Ordinal))
            {
                if (!Context.Comm.IsTcpActive(sendReceoveCommMsg.ConnectionName))
                {
                    throw CreateRetryableCommunicationException(
                        evt, $"TCP未连接:{sendReceoveCommMsg.ConnectionName}");
                }

                CommReceiveResult receiveResult = ExecuteRetryableCommunicationCall(evt, "TCP请求异常",
                    () => Context.Comm.SendReceiveTcpAsync(
                        sendReceoveCommMsg.ConnectionName, sendValue, sendReceoveCommMsg.SendConvert,
                        sendReceoveCommMsg.TimeoutMs, evt.CancellationToken).GetAwaiter().GetResult());
                if (!receiveResult.Success)
                {
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                    string receiveError = string.IsNullOrWhiteSpace(receiveResult.ErrorMessage) ? "未知错误" : receiveResult.ErrorMessage;
                    throw CreateRetryableCommunicationException(
                        evt, $"TCP请求失败:{sendReceoveCommMsg.ConnectionName}，{receiveError}");
                }

                string msg = sendReceoveCommMsg.ReceiveConvert ? receiveResult.MessageHex : receiveResult.MessageText;
                if (!string.IsNullOrEmpty(sendReceoveCommMsg.ReceiveSaveValue))
                {
                    string source = evt?.GetOperationSource();
                    if (!Context.ValueStore.SetValueByNameForProcess(sendReceoveCommMsg.ReceiveSaveValue, msg, evt.procId, source))
                    {
                        MarkAlarm(evt, $"保存TCP接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }

                return true;
            }

            if (string.Equals(sendReceoveCommMsg.CommType, "串口", StringComparison.Ordinal))
            {
                if (!Context.Comm.IsSerialOpen(sendReceoveCommMsg.ConnectionName))
                {
                    throw CreateRetryableCommunicationException(
                        evt, $"串口未打开:{sendReceoveCommMsg.ConnectionName}");
                }

                CommReceiveResult receiveResult = ExecuteRetryableCommunicationCall(evt, "串口请求异常",
                    () => Context.Comm.SendReceiveSerialAsync(
                        sendReceoveCommMsg.ConnectionName, sendValue, sendReceoveCommMsg.SendConvert,
                        sendReceoveCommMsg.TimeoutMs, evt.CancellationToken).GetAwaiter().GetResult());
                if (!receiveResult.Success)
                {
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                    string receiveError = string.IsNullOrWhiteSpace(receiveResult.ErrorMessage) ? "未知错误" : receiveResult.ErrorMessage;
                    throw CreateRetryableCommunicationException(
                        evt, $"串口请求失败:{sendReceoveCommMsg.ConnectionName}，{receiveError}");
                }

                string msg = sendReceoveCommMsg.ReceiveConvert ? receiveResult.MessageHex : receiveResult.MessageText;
                if (!string.IsNullOrEmpty(sendReceoveCommMsg.ReceiveSaveValue))
                {
                    string source = evt?.GetOperationSource();
                    if (!Context.ValueStore.SetValueByNameForProcess(sendReceoveCommMsg.ReceiveSaveValue, msg, evt.procId, source))
                    {
                        MarkAlarm(evt, $"保存串口接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
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
            if (Context?.Comm == null)
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

                if (!TryGetSerialConfig(op.Name, out SerialPortInfo serialPortInfo))
                {
                    MarkAlarm(evt, $"串口配置不存在:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

                if (string.Equals(op.Ops, "启动", StringComparison.Ordinal))
                {
                    Context.Comm.StartSerialAsync(serialPortInfo, evt.CancellationToken).GetAwaiter().GetResult();
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
                if (op.TimeoutMs <= 0)
                {
                    MarkAlarm(evt, $"等待串口连接超时配置无效:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!TryGetSerialConfig(op.Name, out _))
                {
                    MarkAlarm(evt, $"串口配置不存在:{op.Name}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!evt.CancellationToken.IsCancellationRequested && stopwatch.ElapsedMilliseconds < op.TimeoutMs)
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

        private bool TryGetSocketConfig(string name, out SocketInfo info)
        {
            if (Context?.CommunicationStore != null)
            {
                return Context.CommunicationStore.TryGetSocket(name, out info);
            }
            info = Context?.SocketInfos?.FirstOrDefault(item => item != null
                && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            return info != null;
        }

        private bool TryGetSerialConfig(string name, out SerialPortInfo info)
        {
            if (Context?.CommunicationStore != null)
            {
                return Context.CommunicationStore.TryGetSerial(name, out info);
            }
            info = Context?.SerialPortInfos?.FirstOrDefault(item => item != null
                && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            return info != null;
        }
    }
}
