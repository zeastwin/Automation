using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
    public enum CommChannelKind
    {
        TcpClient,
        TcpServer,
        SerialPort
    }

    public enum CommDirection
    {
        Send,
        Receive,
        Error
    }

    public sealed class CommLogEventArgs : EventArgs
    {
        public CommLogEventArgs(CommChannelKind kind, CommDirection direction, string name, string message, string remoteEndPoint, Exception exception)
        {
            Kind = kind;
            Direction = direction;
            Name = name;
            Message = message;
            RemoteEndPoint = remoteEndPoint;
            Exception = exception;
            Timestamp = DateTime.Now;
        }

        public CommChannelKind Kind { get; }
        public CommDirection Direction { get; }
        public string Name { get; }
        public string Message { get; }
        public string RemoteEndPoint { get; }
        public Exception Exception { get; }
        public DateTime Timestamp { get; }
    }

    public sealed class TcpStatus
    {
        public static readonly TcpStatus Empty = new TcpStatus(false, false, 0);

        public TcpStatus(bool isServer, bool isRunning, int clientCount)
        {
            IsServer = isServer;
            IsRunning = isRunning;
            ClientCount = clientCount;
        }

        public bool IsServer { get; }
        public bool IsRunning { get; }
        public int ClientCount { get; }
    }

    public sealed class SerialStatus
    {
        public static readonly SerialStatus Empty = new SerialStatus(false);

        public SerialStatus(bool isOpen)
        {
            IsOpen = isOpen;
        }

        public bool IsOpen { get; }
    }

    public sealed class CommunicationHub : IDisposable
    {
        private readonly ConcurrentDictionary<string, TcpChannel> _tcpChannels = new ConcurrentDictionary<string, TcpChannel>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SerialPortChannel> _serialChannels = new ConcurrentDictionary<string, SerialPortChannel>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<CommLogEventArgs> Log;

        public async Task StartTcpAsync(SocketInfo info, int connectTimeoutMs = 5000)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.Name))
            {
                return;
            }

            if (_tcpChannels.TryGetValue(info.Name, out TcpChannel existing))
            {
                if (existing.Matches(info) && existing.IsRunning)
                {
                    return;
                }

                await existing.StopAsync().ConfigureAwait(false);
            }

            TcpChannel channel = new TcpChannel(info, RaiseLog);
            _tcpChannels[info.Name] = channel;

            try
            {
                await channel.StartAsync(connectTimeoutMs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _tcpChannels.TryRemove(info.Name, out _);
                RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Error, info.Name, ex.Message, null, ex));
            }
        }

        public async Task StopTcpAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (_tcpChannels.TryRemove(name, out TcpChannel channel))
            {
                await channel.StopAsync().ConfigureAwait(false);
            }
        }

        public TcpStatus GetTcpStatus(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return TcpStatus.Empty;
            }

            if (_tcpChannels.TryGetValue(name, out TcpChannel channel))
            {
                return channel.GetStatus();
            }

            return TcpStatus.Empty;
        }

        public bool IsTcpActive(string name)
        {
            return GetTcpStatus(name).IsRunning;
        }

        public async Task<bool> SendTcpAsync(string name, string message, bool convertHex, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.TcpClient, CommDirection.Error, name, "TCP名称为空", null, null));
                return false;
            }

            if (!_tcpChannels.TryGetValue(name, out TcpChannel channel))
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.TcpClient, CommDirection.Error, name, "TCP通道未启动", null, null));
                return false;
            }

            string sendMessage = convertHex ? ConvertDecimalToHex(message) : (message ?? string.Empty);

            try
            {
                await channel.SendAsync(sendMessage, cancellationToken).ConfigureAwait(false);
                RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Send, name, sendMessage, null, null));
                return true;
            }
            catch (OperationCanceledException)
            {
                RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Error, name, "TCP发送已取消", null, null));
                return false;
            }
            catch (Exception ex)
            {
                RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Error, name, ex.Message, null, ex));
                return false;
            }
        }

        public bool TryReceiveTcp(string name, int timeoutMs, out string message)
        {
            message = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (_tcpChannels.TryGetValue(name, out TcpChannel channel))
            {
                return channel.TryReceive(timeoutMs, out message);
            }

            return false;
        }

        public void ClearTcpMessages(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (_tcpChannels.TryGetValue(name, out TcpChannel channel))
            {
                channel.ClearMessages();
            }
        }

        public async Task StartSerialAsync(SerialPortInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.Name))
            {
                return;
            }

            if (_serialChannels.TryGetValue(info.Name, out SerialPortChannel existing))
            {
                if (existing.Matches(info) && existing.IsOpen)
                {
                    return;
                }

                await existing.StopAsync().ConfigureAwait(false);
            }

            SerialPortChannel channel = new SerialPortChannel(info, RaiseLog);
            _serialChannels[info.Name] = channel;

            try
            {
                await channel.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _serialChannels.TryRemove(info.Name, out _);
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, info.Name, ex.Message, info.Port, ex));
            }
        }

        public async Task StopSerialAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (_serialChannels.TryRemove(name, out SerialPortChannel channel))
            {
                await channel.StopAsync().ConfigureAwait(false);
            }
        }

        public SerialStatus GetSerialStatus(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return SerialStatus.Empty;
            }

            if (_serialChannels.TryGetValue(name, out SerialPortChannel channel))
            {
                return channel.GetStatus();
            }

            return SerialStatus.Empty;
        }

        public bool IsSerialOpen(string name)
        {
            return GetSerialStatus(name).IsOpen;
        }

        public async Task<bool> SendSerialAsync(string name, string message, bool convertHex, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, "串口名称为空", null, null));
                return false;
            }

            if (!_serialChannels.TryGetValue(name, out SerialPortChannel channel))
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, "串口未打开", null, null));
                return false;
            }

            string sendMessage = convertHex ? ConvertDecimalToHex(message) : (message ?? string.Empty);

            try
            {
                await channel.SendAsync(sendMessage, cancellationToken).ConfigureAwait(false);
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Send, name, sendMessage, channel.PortName, null));
                return true;
            }
            catch (OperationCanceledException)
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, "串口发送已取消", channel.PortName, null));
                return false;
            }
            catch (Exception ex)
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, ex.Message, channel.PortName, ex));
                return false;
            }
        }

        public bool TryReceiveSerial(string name, int timeoutMs, out string message)
        {
            message = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (_serialChannels.TryGetValue(name, out SerialPortChannel channel))
            {
                return channel.TryReceive(timeoutMs, out message);
            }

            return false;
        }

        public void ClearSerialMessages(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (_serialChannels.TryGetValue(name, out SerialPortChannel channel))
            {
                channel.ClearMessages();
            }
        }

        public void Dispose()
        {
            foreach (var item in _tcpChannels)
            {
                item.Value.StopAsync().GetAwaiter().GetResult();
            }

            _tcpChannels.Clear();

            foreach (var item in _serialChannels)
            {
                item.Value.StopAsync().GetAwaiter().GetResult();
            }

            _serialChannels.Clear();
        }

        private void RaiseLog(CommLogEventArgs args)
        {
            Log?.Invoke(this, args);
        }

        private static string ConvertDecimalToHex(string message)
        {
            if (int.TryParse(message, out int number))
            {
                return Convert.ToString(number, 16).ToUpper();
            }

            return message ?? string.Empty;
        }
    }

    internal sealed class TcpChannel
    {
        private readonly SocketInfo _info;
        private readonly Action<CommLogEventArgs> _log;
        private readonly BlockingCollection<string> _messages = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private readonly object _clientLock = new object();
        private readonly Encoding _encoding = Encoding.UTF8;
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;
        private TcpClient _client;
        private TcpListener _listener;
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private volatile bool _isRunning;

        public TcpChannel(SocketInfo info, Action<CommLogEventArgs> log)
        {
            _info = info;
            _log = log;
        }

        public bool IsRunning => _isRunning;
        public bool IsServer => string.Equals(_info.Type, "Server", StringComparison.OrdinalIgnoreCase);
        public CommChannelKind Kind => IsServer ? CommChannelKind.TcpServer : CommChannelKind.TcpClient;

        public bool Matches(SocketInfo info)
        {
            if (info == null)
            {
                return false;
            }

            return string.Equals(_info.Name, info.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_info.Type, info.Type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_info.Address, info.Address, StringComparison.OrdinalIgnoreCase)
                && _info.Port == info.Port;
        }

        public TcpStatus GetStatus()
        {
            int clientCount = 0;
            if (IsServer)
            {
                lock (_clientLock)
                {
                    clientCount = _clients.Count;
                }
            }
            else
            {
                clientCount = _isRunning ? 1 : 0;
            }

            return new TcpStatus(IsServer, _isRunning, clientCount);
        }

        public async Task StartAsync(int connectTimeoutMs)
        {
            if (_isRunning)
            {
                return;
            }

            ClearMessages();
            _cts = new CancellationTokenSource();

            if (IsServer)
            {
                _listener = new TcpListener(IPAddress.Parse(_info.Address), _info.Port);
                _listener.Start();
                _isRunning = true;
                _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            }
            else
            {
                _client = new TcpClient();
                Task connectTask = _client.ConnectAsync(IPAddress.Parse(_info.Address), _info.Port);

                if (connectTimeoutMs > 0)
                {
                    Task timeoutTask = Task.Delay(connectTimeoutMs, _cts.Token);
                    Task completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                    if (completed != connectTask)
                    {
                        throw new TimeoutException($"{_info.Name} 连接超时");
                    }
                }

                await connectTask.ConfigureAwait(false);
                _isRunning = true;
                _ = Task.Run(() => ReadLoopAsync(_client, _cts.Token));
            }
        }

        public Task StopAsync()
        {
            _cts?.Cancel();

            if (IsServer)
            {
                try
                {
                    _listener?.Stop();
                }
                catch
                {
                }

                lock (_clientLock)
                {
                    foreach (TcpClient client in _clients.ToList())
                    {
                        try
                        {
                            client.Close();
                        }
                        catch
                        {
                        }
                    }
                    _clients.Clear();
                }
            }
            else
            {
                try
                {
                    _client?.Close();
                }
                catch
                {
                }
            }

            _isRunning = false;
            return Task.CompletedTask;
        }

        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            if (!_isRunning)
            {
                throw new InvalidOperationException("TCP 未连接");
            }

            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_isRunning)
                {
                    throw new InvalidOperationException("TCP 未连接");
                }

                byte[] buffer = _encoding.GetBytes(message ?? string.Empty);

                if (IsServer)
                {
                    List<TcpClient> clients;
                    lock (_clientLock)
                    {
                        clients = _clients.ToList();
                    }

                    List<TcpClient> broken = null;

                    foreach (TcpClient client in clients)
                    {
                        try
                        {
                            NetworkStream stream = client.GetStream();
                            await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            if (broken == null)
                            {
                                broken = new List<TcpClient>();
                            }
                            broken.Add(client);
                        }
                    }

                    if (broken != null)
                    {
                        lock (_clientLock)
                        {
                            foreach (TcpClient client in broken)
                            {
                                _clients.Remove(client);
                            }
                        }
                    }
                }
                else
                {
                    if (_client == null)
                    {
                        throw new InvalidOperationException("TCP 客户端未初始化");
                    }

                    NetworkStream stream = _client.GetStream();
                    await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public bool TryReceive(int timeoutMs, out string message)
        {
            message = null;
            if (timeoutMs < 0)
            {
                timeoutMs = 0;
            }

            try
            {
                return _messages.TryTake(out message, timeoutMs);
            }
            catch
            {
                return false;
            }
        }

        public void ClearMessages()
        {
            while (_messages.TryTake(out _))
            {
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = null;

                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        _log?.Invoke(new CommLogEventArgs(Kind, CommDirection.Error, _info.Name, ex.Message, null, ex));
                    }
                    break;
                }

                if (client == null)
                {
                    continue;
                }

                lock (_clientLock)
                {
                    _clients.Add(client);
                }

                _ = Task.Run(() => ReadLoopAsync(client, token));
            }
        }

        private async Task ReadLoopAsync(TcpClient client, CancellationToken token)
        {
            string remoteEndPoint = null;
            try
            {
                remoteEndPoint = client.Client.RemoteEndPoint?.ToString();
            }
            catch
            {
                remoteEndPoint = null;
            }

            NetworkStream stream = null;
            try
            {
                stream = client.GetStream();
            }
            catch
            {
                return;
            }

            byte[] buffer = new byte[4096];

            while (!token.IsCancellationRequested)
            {
                int bytesRead = 0;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        _log?.Invoke(new CommLogEventArgs(Kind, CommDirection.Error, _info.Name, ex.Message, remoteEndPoint, ex));
                    }
                    break;
                }

                if (bytesRead <= 0)
                {
                    break;
                }

                string received = _encoding.GetString(buffer, 0, bytesRead);
                _messages.Add(received);
                _log?.Invoke(new CommLogEventArgs(Kind, CommDirection.Receive, _info.Name, received, remoteEndPoint, null));
            }

            if (!IsServer)
            {
                _isRunning = false;
            }

            lock (_clientLock)
            {
                _clients.Remove(client);
            }
        }
    }

    internal sealed class SerialPortChannel
    {
        private readonly SerialPortInfo _info;
        private readonly Action<CommLogEventArgs> _log;
        private readonly BlockingCollection<string> _messages = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts;
        private SerialPort _serialPort;
        private volatile bool _isOpen;

        public SerialPortChannel(SerialPortInfo info, Action<CommLogEventArgs> log)
        {
            _info = info;
            _log = log;
        }

        public bool IsOpen => _isOpen;
        public string PortName => _info?.Port;

        public bool Matches(SerialPortInfo info)
        {
            if (info == null)
            {
                return false;
            }

            return string.Equals(_info.Name, info.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_info.Port, info.Port, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_info.BitRate, info.BitRate, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_info.CheckBit, info.CheckBit, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_info.DataBit, info.DataBit, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_info.StopBit, info.StopBit, StringComparison.OrdinalIgnoreCase);
        }

        public SerialStatus GetStatus()
        {
            return new SerialStatus(_isOpen);
        }

        public Task StartAsync()
        {
            if (_isOpen)
            {
                return Task.CompletedTask;
            }

            ClearMessages();
            _cts = new CancellationTokenSource();

            lock (_sync)
            {
                _serialPort = new SerialPort(_info.Port,
                    int.Parse(_info.BitRate),
                    (Parity)Enum.Parse(typeof(Parity), _info.CheckBit),
                    int.Parse(_info.DataBit),
                    (StopBits)Enum.Parse(typeof(StopBits), _info.StopBit));

                _serialPort.Open();
                _isOpen = true;
            }

            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _cts?.Cancel();

            lock (_sync)
            {
                try
                {
                    if (_serialPort != null && _serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                }
                catch
                {
                }
                finally
                {
                    _serialPort?.Dispose();
                    _serialPort = null;
                }

                _isOpen = false;
            }

            return Task.CompletedTask;
        }

        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                SerialPort port;
                string newLine;
                Encoding encoding;
                lock (_sync)
                {
                    port = _serialPort;
                    if (port == null || !port.IsOpen)
                    {
                        throw new InvalidOperationException("串口未打开");
                    }
                    newLine = port.NewLine;
                    encoding = port.Encoding;
                }

                string payload = (message ?? string.Empty) + newLine;
                byte[] buffer = encoding.GetBytes(payload);
                await port.BaseStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public bool TryReceive(int timeoutMs, out string message)
        {
            message = null;
            if (timeoutMs < 0)
            {
                timeoutMs = 0;
            }

            try
            {
                return _messages.TryTake(out message, timeoutMs);
            }
            catch
            {
                return false;
            }
        }

        public void ClearMessages()
        {
            while (_messages.TryTake(out _))
            {
            }
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            byte[] buffer = new byte[1024];

            while (!token.IsCancellationRequested)
            {
                int bytesRead = 0;
                try
                {
                    if (_serialPort == null)
                    {
                        break;
                    }

                    bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        _log?.Invoke(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, _info.Name, ex.Message, _info.Port, ex));
                    }
                    break;
                }

                if (bytesRead <= 0)
                {
                    continue;
                }

                string received;
                lock (_sync)
                {
                    if (_serialPort == null)
                    {
                        break;
                    }
                    received = _serialPort.Encoding.GetString(buffer, 0, bytesRead);
                }

                _messages.Add(received);
                _log?.Invoke(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Receive, _info.Name, received, _info.Port, null));
            }

            _isOpen = false;
        }
    }
}
