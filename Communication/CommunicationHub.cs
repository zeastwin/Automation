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
        public CommLogEventArgs(
            CommChannelKind kind,
            CommDirection direction,
            string name,
            string message,
            string messageHex,
            string remoteEndPoint,
            Exception exception)
        {
            Kind = kind;
            Direction = direction;
            Name = name;
            Message = message;
            MessageHex = messageHex;
            RemoteEndPoint = remoteEndPoint;
            Exception = exception;
            Timestamp = DateTime.Now;
        }

        public CommChannelKind Kind { get; }
        public CommDirection Direction { get; }
        public string Name { get; }
        public string Message { get; }
        public string MessageHex { get; }
        public string RemoteEndPoint { get; }
        public Exception Exception { get; }
        public DateTime Timestamp { get; }
    }

    public sealed class CommReceiveResult
    {
        private CommReceiveResult(bool success, string messageText, string messageHex, string remoteEndPoint, string errorMessage)
        {
            Success = success;
            MessageText = messageText;
            MessageHex = messageHex;
            RemoteEndPoint = remoteEndPoint;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }
        public string MessageText { get; }
        public string MessageHex { get; }
        public string RemoteEndPoint { get; }
        public string ErrorMessage { get; }

        internal static CommReceiveResult CreateSuccess(string messageText, string messageHex, string remoteEndPoint)
        {
            return new CommReceiveResult(true, messageText, messageHex, remoteEndPoint, null);
        }

        internal static CommReceiveResult CreateFailure(string errorMessage)
        {
            return new CommReceiveResult(false, null, null, null, errorMessage);
        }
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

    internal static class RuntimeConfig
    {
        private static readonly int pendingReceiverLimit = LoadPendingReceiverLimit();

        public static int PendingReceiverLimit => pendingReceiverLimit;

        private static int LoadPendingReceiverLimit()
        {
            if (!AppConfigStorage.TryLoad(out AppConfig config, out string error))
            {
                throw new InvalidOperationException(error);
            }
            return config.CommMaxMessageQueueSize;
        }
    }

    internal enum CommFrameMode
    {
        Delimiter,
        Raw
    }

    internal sealed class CommFrameSettings
    {
        public CommFrameSettings(CommFrameMode mode, byte[] delimiter, Encoding encoding)
        {
            Mode = mode;
            Delimiter = delimiter;
            Encoding = encoding;
        }

        public CommFrameMode Mode { get; }
        public byte[] Delimiter { get; }
        public Encoding Encoding { get; }
    }

    internal sealed class ParsedSocketInfo
    {
        public string Name { get; set; }
        public bool IsServer { get; set; }
        public IPAddress Address { get; set; }
        public int Port { get; set; }
        public int ConnectTimeoutMs { get; set; }
        public CommFrameSettings FrameSettings { get; set; }
    }

    internal sealed class ParsedSerialInfo
    {
        public string Name { get; set; }
        public string PortName { get; set; }
        public int BitRate { get; set; }
        public Parity Parity { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }
        public CommFrameSettings FrameSettings { get; set; }
    }

    internal sealed class ReceivedFrame
    {
        public ReceivedFrame(byte[] payload, string remoteEndPoint)
        {
            Payload = payload;
            RemoteEndPoint = remoteEndPoint;
        }

        public byte[] Payload { get; }
        public string RemoteEndPoint { get; }
    }

    internal sealed class FrameDecoder
    {
        private readonly CommFrameMode mode;
        private readonly byte[] delimiter;
        private readonly int maxFrameBytes;
        private readonly List<byte> buffer = new List<byte>(4096);

        public FrameDecoder(CommFrameSettings settings, int maxFrameBytes)
        {
            mode = settings.Mode;
            delimiter = settings.Delimiter;
            this.maxFrameBytes = maxFrameBytes;
        }

        public IList<byte[]> Push(byte[] data, int count)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (count < 0 || count > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"接收长度无效:{count}");
            }

            if (count == 0)
            {
                return Array.Empty<byte[]>();
            }

            if (mode == CommFrameMode.Raw)
            {
                byte[] raw = new byte[count];
                Buffer.BlockCopy(data, 0, raw, 0, count);
                return new[] { raw };
            }

            buffer.AddRange(data.Take(count));
            if (buffer.Count > maxFrameBytes)
            {
                throw new InvalidOperationException($"接收缓冲超限:{buffer.Count}");
            }

            List<byte[]> frames = new List<byte[]>();
            while (true)
            {
                int index = IndexOf(buffer, delimiter);
                if (index < 0)
                {
                    break;
                }

                byte[] frame = new byte[index];
                if (index > 0)
                {
                    buffer.CopyTo(0, frame, 0, index);
                }
                frames.Add(frame);
                buffer.RemoveRange(0, index + delimiter.Length);
            }

            return frames;
        }

        private static int IndexOf(List<byte> source, byte[] pattern)
        {
            if (source == null || pattern == null || pattern.Length == 0 || source.Count < pattern.Length)
            {
                return -1;
            }

            int max = source.Count - pattern.Length;
            for (int i = 0; i <= max; i++)
            {
                bool matched = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    internal sealed class ReceiveDispatcher
    {
        private readonly ConcurrentDictionary<long, Waiter> waiters = new ConcurrentDictionary<long, Waiter>();
        private readonly int pendingLimit;
        private long waiterId;
        private volatile bool closed;
        private string closeReason;

        private sealed class Waiter : IDisposable
        {
            public Waiter(long id, TaskCompletionSource<ReceivedFrame> source)
            {
                Id = id;
                Source = source;
            }

            public long Id { get; }
            public TaskCompletionSource<ReceivedFrame> Source { get; }
            public Timer TimeoutTimer { get; set; }
            public CancellationTokenRegistration CancellationRegistration { get; set; }

            public void Dispose()
            {
                TimeoutTimer?.Dispose();
                CancellationRegistration.Dispose();
            }
        }

        public ReceiveDispatcher(int pendingLimit)
        {
            if (pendingLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pendingLimit), $"等待队列上限无效:{pendingLimit}");
            }
            this.pendingLimit = pendingLimit;
        }

        public Task<ReceivedFrame> WaitNextAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), $"接收超时无效:{timeoutMs}");
            }

            if (closed)
            {
                throw new InvalidOperationException(closeReason ?? "通讯通道已关闭");
            }

            if (waiters.Count >= pendingLimit)
            {
                throw new InvalidOperationException($"接收等待队列已满:{pendingLimit}");
            }

            long id = Interlocked.Increment(ref waiterId);
            TaskCompletionSource<ReceivedFrame> source = new TaskCompletionSource<ReceivedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            Waiter waiter = new Waiter(id, source);

            if (!waiters.TryAdd(id, waiter))
            {
                throw new InvalidOperationException("创建接收等待失败");
            }

            waiter.TimeoutTimer = new Timer(_ =>
            {
                CancelWaiter(id, new TimeoutException("接收超时"));
            }, null, timeoutMs, Timeout.Infinite);

            if (cancellationToken.CanBeCanceled)
            {
                waiter.CancellationRegistration = cancellationToken.Register(() =>
                {
                    CancelWaiter(id, new OperationCanceledException("接收已取消", cancellationToken));
                });
            }

            if (closed)
            {
                CancelWaiter(id, new InvalidOperationException(closeReason ?? "通讯通道已关闭"));
            }

            return source.Task;
        }

        public void Publish(ReceivedFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            foreach (KeyValuePair<long, Waiter> pair in waiters.ToArray())
            {
                if (!waiters.TryRemove(pair.Key, out Waiter waiter))
                {
                    continue;
                }

                waiter.Dispose();
                waiter.Source.TrySetResult(frame);
            }
        }

        public void Close(string reason)
        {
            closed = true;
            closeReason = string.IsNullOrWhiteSpace(reason) ? "通讯通道已关闭" : reason;

            foreach (KeyValuePair<long, Waiter> pair in waiters.ToArray())
            {
                if (!waiters.TryRemove(pair.Key, out Waiter waiter))
                {
                    continue;
                }

                waiter.Dispose();
                waiter.Source.TrySetException(new InvalidOperationException(closeReason));
            }
        }

        private void CancelWaiter(long id, Exception ex)
        {
            if (!waiters.TryRemove(id, out Waiter waiter))
            {
                return;
            }

            waiter.Dispose();
            waiter.Source.TrySetException(ex);
        }
    }

    internal sealed class TcpChannel
    {
        private const int MaxFrameBytes = 1024 * 1024;

        private readonly ParsedSocketInfo info;
        private readonly Action<CommLogEventArgs> log;
        private readonly ReceiveDispatcher dispatcher;
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly object clientLock = new object();
        private readonly List<TcpClient> clients = new List<TcpClient>();

        private CancellationTokenSource cts;
        private TcpClient client;
        private TcpListener listener;
        private volatile bool isRunning;

        public TcpChannel(ParsedSocketInfo info, Action<CommLogEventArgs> log, int pendingLimit)
        {
            this.info = info;
            this.log = log;
            dispatcher = new ReceiveDispatcher(pendingLimit);
        }

        public bool IsRunning => isRunning;
        public bool IsServer => info.IsServer;
        public CommChannelKind Kind => IsServer ? CommChannelKind.TcpServer : CommChannelKind.TcpClient;
        public Encoding Encoding => info.FrameSettings.Encoding;
        public bool UseDelimiter => info.FrameSettings.Mode == CommFrameMode.Delimiter;
        public byte[] Delimiter => info.FrameSettings.Delimiter;

        public bool Matches(ParsedSocketInfo target)
        {
            if (target == null)
            {
                return false;
            }

            return string.Equals(info.Name, target.Name, StringComparison.Ordinal)
                && info.IsServer == target.IsServer
                && Equals(info.Address, target.Address)
                && info.Port == target.Port
                && info.ConnectTimeoutMs == target.ConnectTimeoutMs
                && info.FrameSettings.Mode == target.FrameSettings.Mode
                && string.Equals(info.FrameSettings.Encoding.WebName, target.FrameSettings.Encoding.WebName, StringComparison.OrdinalIgnoreCase)
                && AreSameDelimiter(info.FrameSettings.Delimiter, target.FrameSettings.Delimiter);
        }

        public TcpStatus GetStatus()
        {
            int clientCount = 0;
            if (IsServer)
            {
                lock (clientLock)
                {
                    clientCount = clients.Count;
                }
            }
            else
            {
                clientCount = isRunning ? 1 : 0;
            }

            return new TcpStatus(IsServer, isRunning, clientCount);
        }

        public async Task StartAsync()
        {
            if (isRunning)
            {
                return;
            }

            cts = new CancellationTokenSource();
            if (IsServer)
            {
                listener = new TcpListener(info.Address, info.Port);
                listener.Start();
                isRunning = true;
                _ = Task.Run(() => AcceptLoopAsync(cts.Token));
                return;
            }

            client = new TcpClient();
            CancellationToken token = cts.Token;
            try
            {
                Task connectTask = client.ConnectAsync(info.Address, info.Port);
                Task timeoutTask = Task.Delay(info.ConnectTimeoutMs, token);
                Task completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (!ReferenceEquals(completed, connectTask))
                {
                    if (token.IsCancellationRequested)
                    {
                        SafeCloseClient(client);
                        client = null;
                        throw new OperationCanceledException("TCP连接已取消", token);
                    }

                    SafeCloseClient(client);
                    client = null;
                    throw new TimeoutException($"{info.Name}连接超时");
                }

                await connectTask.ConfigureAwait(false);
            }
            catch
            {
                SafeCloseClient(client);
                client = null;
                throw;
            }
            isRunning = true;
            _ = Task.Run(() => ReadLoopAsync(client, cts.Token));
        }

        public Task StopAsync()
        {
            cts?.Cancel();

            if (IsServer)
            {
                try
                {
                    listener?.Stop();
                }
                catch
                {
                }

                lock (clientLock)
                {
                    foreach (TcpClient tcpClient in clients.ToList())
                    {
                        SafeCloseClient(tcpClient);
                    }
                    clients.Clear();
                }
            }
            else
            {
                SafeCloseClient(client);
                client = null;
            }

            isRunning = false;
            dispatcher.Close("TCP通道已关闭");
            return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] payload, bool appendDelimiter, CancellationToken cancellationToken)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (!isRunning)
            {
                throw new InvalidOperationException("TCP未连接");
            }

            byte[] packet = appendDelimiter ? AppendDelimiter(payload) : payload;

            await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!isRunning)
                {
                    throw new InvalidOperationException("TCP未连接");
                }

                if (IsServer)
                {
                    List<TcpClient> targetClients;
                    lock (clientLock)
                    {
                        targetClients = clients.ToList();
                    }

                    if (targetClients.Count == 0)
                    {
                        throw new InvalidOperationException("TCP服务端无在线客户端");
                    }

                    List<TcpClient> brokenClients = null;
                    int sendCount = 0;
                    foreach (TcpClient tcpClient in targetClients)
                    {
                        try
                        {
                            NetworkStream stream = tcpClient.GetStream();
                            await stream.WriteAsync(packet, 0, packet.Length, cancellationToken).ConfigureAwait(false);
                            sendCount++;
                        }
                        catch
                        {
                            if (brokenClients == null)
                            {
                                brokenClients = new List<TcpClient>();
                            }
                            brokenClients.Add(tcpClient);
                        }
                    }

                    if (brokenClients != null)
                    {
                        lock (clientLock)
                        {
                            foreach (TcpClient broken in brokenClients)
                            {
                                clients.Remove(broken);
                                SafeCloseClient(broken);
                            }
                        }
                    }

                    if (sendCount == 0)
                    {
                        throw new InvalidOperationException("TCP服务端发送失败：无可用客户端");
                    }
                }
                else
                {
                    if (client == null)
                    {
                        throw new InvalidOperationException("TCP客户端未初始化");
                    }

                    NetworkStream stream = client.GetStream();
                    await stream.WriteAsync(packet, 0, packet.Length, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                sendGate.Release();
            }
        }

        public Task<ReceivedFrame> WaitReceiveAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            return dispatcher.WaitNextAsync(timeoutMs, cancellationToken);
        }

        public string DecodeText(byte[] payload)
        {
            return Encoding.GetString(payload ?? Array.Empty<byte>());
        }

        private byte[] AppendDelimiter(byte[] payload)
        {
            if (!UseDelimiter)
            {
                return payload;
            }

            if (payload.Length >= Delimiter.Length && payload.Skip(payload.Length - Delimiter.Length).SequenceEqual(Delimiter))
            {
                return payload;
            }

            byte[] result = new byte[payload.Length + Delimiter.Length];
            Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
            Buffer.BlockCopy(Delimiter, 0, result, payload.Length, Delimiter.Length);
            return result;
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient accepted = null;
                try
                {
                    accepted = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        log?.Invoke(new CommLogEventArgs(Kind, CommDirection.Error, info.Name, ex.Message, null, null, ex));
                    }
                    break;
                }

                if (accepted == null)
                {
                    continue;
                }

                lock (clientLock)
                {
                    clients.Add(accepted);
                }

                _ = Task.Run(() => ReadLoopAsync(accepted, token));
            }
        }

        private async Task ReadLoopAsync(TcpClient targetClient, CancellationToken token)
        {
            string remoteEndPoint = null;
            try
            {
                remoteEndPoint = targetClient?.Client?.RemoteEndPoint?.ToString();
            }
            catch
            {
                remoteEndPoint = null;
            }

            NetworkStream stream;
            try
            {
                stream = targetClient.GetStream();
            }
            catch
            {
                return;
            }

            FrameDecoder decoder = new FrameDecoder(info.FrameSettings, MaxFrameBytes);
            byte[] buffer = new byte[4096];

            while (!token.IsCancellationRequested)
            {
                int bytesRead;
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
                        log?.Invoke(new CommLogEventArgs(Kind, CommDirection.Error, info.Name, ex.Message, null, remoteEndPoint, ex));
                    }
                    break;
                }

                if (bytesRead <= 0)
                {
                    break;
                }

                IList<byte[]> frames;
                try
                {
                    frames = decoder.Push(buffer, bytesRead);
                }
                catch (Exception ex)
                {
                    log?.Invoke(new CommLogEventArgs(Kind, CommDirection.Error, info.Name, ex.Message, null, remoteEndPoint, ex));
                    break;
                }

                foreach (byte[] frame in frames)
                {
                    dispatcher.Publish(new ReceivedFrame(frame, remoteEndPoint));
                    string text = DecodeText(frame);
                    string hex = CommunicationHub.BytesToHex(frame);
                    log?.Invoke(new CommLogEventArgs(Kind, CommDirection.Receive, info.Name, text, hex, remoteEndPoint, null));
                }
            }

            lock (clientLock)
            {
                clients.Remove(targetClient);
            }

            SafeCloseClient(targetClient);

            if (!IsServer)
            {
                isRunning = false;
                dispatcher.Close("TCP连接已断开");
            }
        }

        private static bool AreSameDelimiter(byte[] left, byte[] right)
        {
            if (left == null && right == null)
            {
                return true;
            }
            if (left == null || right == null)
            {
                return false;
            }
            return left.SequenceEqual(right);
        }

        private static void SafeCloseClient(TcpClient tcpClient)
        {
            if (tcpClient == null)
            {
                return;
            }

            try
            {
                tcpClient.Close();
            }
            catch
            {
            }
        }
    }

    internal sealed class SerialPortChannel
    {
        private const int MaxFrameBytes = 1024 * 1024;

        private readonly ParsedSerialInfo info;
        private readonly Action<CommLogEventArgs> log;
        private readonly ReceiveDispatcher dispatcher;
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly object sync = new object();

        private CancellationTokenSource cts;
        private SerialPort serialPort;
        private volatile bool isOpen;

        public SerialPortChannel(ParsedSerialInfo info, Action<CommLogEventArgs> log, int pendingLimit)
        {
            this.info = info;
            this.log = log;
            dispatcher = new ReceiveDispatcher(pendingLimit);
        }

        public bool IsOpen => isOpen;
        public Encoding Encoding => info.FrameSettings.Encoding;
        public bool UseDelimiter => info.FrameSettings.Mode == CommFrameMode.Delimiter;
        public byte[] Delimiter => info.FrameSettings.Delimiter;
        public string PortName => info.PortName;

        public bool Matches(ParsedSerialInfo target)
        {
            if (target == null)
            {
                return false;
            }

            return string.Equals(info.Name, target.Name, StringComparison.Ordinal)
                && string.Equals(info.PortName, target.PortName, StringComparison.OrdinalIgnoreCase)
                && info.BitRate == target.BitRate
                && info.Parity == target.Parity
                && info.DataBits == target.DataBits
                && info.StopBits == target.StopBits
                && info.FrameSettings.Mode == target.FrameSettings.Mode
                && string.Equals(info.FrameSettings.Encoding.WebName, target.FrameSettings.Encoding.WebName, StringComparison.OrdinalIgnoreCase)
                && AreSameDelimiter(info.FrameSettings.Delimiter, target.FrameSettings.Delimiter);
        }

        public SerialStatus GetStatus()
        {
            return new SerialStatus(isOpen);
        }

        public Task StartAsync()
        {
            if (isOpen)
            {
                return Task.CompletedTask;
            }

            cts = new CancellationTokenSource();
            lock (sync)
            {
                serialPort = new SerialPort(info.PortName, info.BitRate, info.Parity, info.DataBits, info.StopBits)
                {
                    Encoding = info.FrameSettings.Encoding
                };
                serialPort.Open();
                isOpen = true;
            }

            _ = Task.Run(() => ReadLoopAsync(cts.Token));
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            cts?.Cancel();

            lock (sync)
            {
                try
                {
                    if (serialPort != null && serialPort.IsOpen)
                    {
                        serialPort.Close();
                    }
                }
                catch
                {
                }
                finally
                {
                    serialPort?.Dispose();
                    serialPort = null;
                }

                isOpen = false;
            }

            dispatcher.Close("串口已关闭");
            return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] payload, bool appendDelimiter, CancellationToken cancellationToken)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                SerialPort port;
                lock (sync)
                {
                    port = serialPort;
                    if (port == null || !port.IsOpen)
                    {
                        throw new InvalidOperationException("串口未打开");
                    }
                }

                byte[] packet = appendDelimiter ? AppendDelimiter(payload) : payload;
                await port.BaseStream.WriteAsync(packet, 0, packet.Length, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                sendGate.Release();
            }
        }

        public Task<ReceivedFrame> WaitReceiveAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            return dispatcher.WaitNextAsync(timeoutMs, cancellationToken);
        }

        public string DecodeText(byte[] payload)
        {
            return Encoding.GetString(payload ?? Array.Empty<byte>());
        }

        private byte[] AppendDelimiter(byte[] payload)
        {
            if (!UseDelimiter)
            {
                return payload;
            }

            if (payload.Length >= Delimiter.Length && payload.Skip(payload.Length - Delimiter.Length).SequenceEqual(Delimiter))
            {
                return payload;
            }

            byte[] result = new byte[payload.Length + Delimiter.Length];
            Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
            Buffer.BlockCopy(Delimiter, 0, result, payload.Length, Delimiter.Length);
            return result;
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            FrameDecoder decoder = new FrameDecoder(info.FrameSettings, MaxFrameBytes);
            byte[] buffer = new byte[1024];

            while (!token.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    SerialPort port;
                    lock (sync)
                    {
                        port = serialPort;
                        if (port == null)
                        {
                            break;
                        }
                    }

                    bytesRead = await port.BaseStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        log?.Invoke(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, info.Name, ex.Message, null, info.PortName, ex));
                    }
                    break;
                }

                if (bytesRead <= 0)
                {
                    break;
                }

                IList<byte[]> frames;
                try
                {
                    frames = decoder.Push(buffer, bytesRead);
                }
                catch (Exception ex)
                {
                    log?.Invoke(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, info.Name, ex.Message, null, info.PortName, ex));
                    break;
                }

                foreach (byte[] frame in frames)
                {
                    dispatcher.Publish(new ReceivedFrame(frame, info.PortName));
                    string text = DecodeText(frame);
                    string hex = CommunicationHub.BytesToHex(frame);
                    log?.Invoke(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Receive, info.Name, text, hex, info.PortName, null));
                }
            }

            isOpen = false;
            dispatcher.Close("串口已关闭");
        }

        private static bool AreSameDelimiter(byte[] left, byte[] right)
        {
            if (left == null && right == null)
            {
                return true;
            }
            if (left == null || right == null)
            {
                return false;
            }
            return left.SequenceEqual(right);
        }
    }

    public sealed class CommunicationHub : IDisposable
    {
        private readonly ConcurrentDictionary<string, TcpChannel> tcpChannels = new ConcurrentDictionary<string, TcpChannel>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SerialPortChannel> serialChannels = new ConcurrentDictionary<string, SerialPortChannel>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<CommLogEventArgs> Log;

        public async Task StartTcpAsync(SocketInfo info, int connectTimeoutMs = 5000)
        {
            ParsedSocketInfo parsed;
            try
            {
                parsed = ParseSocketInfo(info, connectTimeoutMs);
            }
            catch (Exception ex)
            {
                string name = info?.Name ?? string.Empty;
                RaiseLog(new CommLogEventArgs(CommChannelKind.TcpClient, CommDirection.Error, name, ex.Message, null, null, ex));
                return;
            }

            if (tcpChannels.TryGetValue(parsed.Name, out TcpChannel existing))
            {
                if (existing.Matches(parsed) && existing.IsRunning)
                {
                    return;
                }

                await existing.StopAsync().ConfigureAwait(false);
            }

            TcpChannel channel = new TcpChannel(parsed, RaiseLog, RuntimeConfig.PendingReceiverLimit);
            tcpChannels[parsed.Name] = channel;

            try
            {
                await channel.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                tcpChannels.TryRemove(parsed.Name, out _);
                RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Error, parsed.Name, ex.Message, null, null, ex));
            }
        }

        public async Task StopTcpAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (tcpChannels.TryRemove(name, out TcpChannel channel))
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

            if (tcpChannels.TryGetValue(name, out TcpChannel channel))
            {
                return channel.GetStatus();
            }

            return TcpStatus.Empty;
        }

        public bool IsTcpActive(string name)
        {
            TcpStatus status = GetTcpStatus(name);
            if (!status.IsRunning)
            {
                return false;
            }

            if (!status.IsServer)
            {
                return true;
            }

            return status.ClientCount > 0;
        }

        public async Task<bool> SendTcpAsync(string name, string message, bool convertHex, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.TcpClient, CommDirection.Error, name, "TCP名称为空", null, null, null));
                return false;
            }

            if (!tcpChannels.TryGetValue(name, out TcpChannel channel))
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.TcpClient, CommDirection.Error, name, "TCP通道未启动", null, null, null));
                return false;
            }

            try
            {
                byte[] payload = BuildSendPayload(message, convertHex, channel.Encoding);
                bool appendDelimiter = !convertHex && channel.UseDelimiter;
                await channel.SendAsync(payload, appendDelimiter, cancellationToken).ConfigureAwait(false);
                string text = convertHex ? BytesToHex(payload) : channel.DecodeText(payload);
                string hex = BytesToHex(payload);
                RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Send, name, text, hex, null, null));
                return true;
            }
            catch (OperationCanceledException)
            {
                RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Error, name, "TCP发送已取消", null, null, null));
                return false;
            }
            catch (Exception ex)
            {
                RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Error, name, ex.Message, null, null, ex));
                return false;
            }
        }

        public async Task<CommReceiveResult> ReceiveTcpAsync(string name, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                string error = "TCP名称为空";
                RaiseLog(new CommLogEventArgs(CommChannelKind.TcpClient, CommDirection.Error, name, error, null, null, null));
                return CommReceiveResult.CreateFailure(error);
            }

            if (timeoutMs <= 0)
            {
                string error = $"TCP接收超时配置无效:{timeoutMs}";
                RaiseLog(new CommLogEventArgs(CommChannelKind.TcpClient, CommDirection.Error, name, error, null, null, null));
                return CommReceiveResult.CreateFailure(error);
            }

            if (!tcpChannels.TryGetValue(name, out TcpChannel channel))
            {
                string error = "TCP通道未启动";
                RaiseLog(new CommLogEventArgs(CommChannelKind.TcpClient, CommDirection.Error, name, error, null, null, null));
                return CommReceiveResult.CreateFailure(error);
            }

            try
            {
                ReceivedFrame frame = await channel.WaitReceiveAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
                string text = channel.DecodeText(frame.Payload);
                string hex = BytesToHex(frame.Payload);
                return CommReceiveResult.CreateSuccess(text, hex, frame.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                return CommReceiveResult.CreateFailure("TCP接收已取消");
            }
            catch (TimeoutException)
            {
                return CommReceiveResult.CreateFailure("TCP接收超时");
            }
            catch (Exception ex)
            {
                RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Error, name, ex.Message, null, null, ex));
                return CommReceiveResult.CreateFailure(ex.Message);
            }
        }

        public bool TryReceiveTcp(string name, int timeoutMs, out string message)
        {
            CommReceiveResult result = ReceiveTcpAsync(name, timeoutMs).GetAwaiter().GetResult();
            if (result.Success)
            {
                message = result.MessageText;
                return true;
            }

            message = null;
            return false;
        }

        public bool TryReceiveTcp(string name, int timeoutMs, CancellationToken cancellationToken, out string message)
        {
            CommReceiveResult result = ReceiveTcpAsync(name, timeoutMs, cancellationToken).GetAwaiter().GetResult();
            if (result.Success)
            {
                message = result.MessageText;
                return true;
            }

            message = null;
            return false;
        }

        public void ClearTcpMessages(string name)
        {
            // 新实现无全局消息队列，保留空实现以避免误用导致抛错。
        }

        public async Task StartSerialAsync(SerialPortInfo info)
        {
            ParsedSerialInfo parsed;
            try
            {
                parsed = ParseSerialInfo(info);
            }
            catch (Exception ex)
            {
                string name = info?.Name ?? string.Empty;
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, ex.Message, null, info?.Port, ex));
                return;
            }

            if (serialChannels.TryGetValue(parsed.Name, out SerialPortChannel existing))
            {
                if (existing.Matches(parsed) && existing.IsOpen)
                {
                    return;
                }

                await existing.StopAsync().ConfigureAwait(false);
            }

            SerialPortChannel channel = new SerialPortChannel(parsed, RaiseLog, RuntimeConfig.PendingReceiverLimit);
            serialChannels[parsed.Name] = channel;

            try
            {
                await channel.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                serialChannels.TryRemove(parsed.Name, out _);
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, parsed.Name, ex.Message, null, parsed.PortName, ex));
            }
        }

        public async Task StopSerialAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (serialChannels.TryRemove(name, out SerialPortChannel channel))
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

            if (serialChannels.TryGetValue(name, out SerialPortChannel channel))
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
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, "串口名称为空", null, null, null));
                return false;
            }

            if (!serialChannels.TryGetValue(name, out SerialPortChannel channel))
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, "串口未打开", null, null, null));
                return false;
            }

            try
            {
                byte[] payload = BuildSendPayload(message, convertHex, channel.Encoding);
                bool appendDelimiter = !convertHex && channel.UseDelimiter;
                await channel.SendAsync(payload, appendDelimiter, cancellationToken).ConfigureAwait(false);
                string text = convertHex ? BytesToHex(payload) : channel.DecodeText(payload);
                string hex = BytesToHex(payload);
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Send, name, text, hex, channel.PortName, null));
                return true;
            }
            catch (OperationCanceledException)
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, "串口发送已取消", null, channel.PortName, null));
                return false;
            }
            catch (Exception ex)
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, ex.Message, null, channel.PortName, ex));
                return false;
            }
        }

        public async Task<CommReceiveResult> ReceiveSerialAsync(string name, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                string error = "串口名称为空";
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, error, null, null, null));
                return CommReceiveResult.CreateFailure(error);
            }

            if (timeoutMs <= 0)
            {
                string error = $"串口接收超时配置无效:{timeoutMs}";
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, error, null, null, null));
                return CommReceiveResult.CreateFailure(error);
            }

            if (!serialChannels.TryGetValue(name, out SerialPortChannel channel))
            {
                string error = "串口未打开";
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, error, null, null, null));
                return CommReceiveResult.CreateFailure(error);
            }

            try
            {
                ReceivedFrame frame = await channel.WaitReceiveAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
                string text = channel.DecodeText(frame.Payload);
                string hex = BytesToHex(frame.Payload);
                return CommReceiveResult.CreateSuccess(text, hex, frame.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                return CommReceiveResult.CreateFailure("串口接收已取消");
            }
            catch (TimeoutException)
            {
                return CommReceiveResult.CreateFailure("串口接收超时");
            }
            catch (Exception ex)
            {
                RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, name, ex.Message, null, channel.PortName, ex));
                return CommReceiveResult.CreateFailure(ex.Message);
            }
        }

        public bool TryReceiveSerial(string name, int timeoutMs, out string message)
        {
            CommReceiveResult result = ReceiveSerialAsync(name, timeoutMs).GetAwaiter().GetResult();
            if (result.Success)
            {
                message = result.MessageText;
                return true;
            }

            message = null;
            return false;
        }

        public bool TryReceiveSerial(string name, int timeoutMs, CancellationToken cancellationToken, out string message)
        {
            CommReceiveResult result = ReceiveSerialAsync(name, timeoutMs, cancellationToken).GetAwaiter().GetResult();
            if (result.Success)
            {
                message = result.MessageText;
                return true;
            }

            message = null;
            return false;
        }

        public void ClearSerialMessages(string name)
        {
            // 新实现无全局消息队列，保留空实现以避免误用导致抛错。
        }

        public void Dispose()
        {
            foreach (KeyValuePair<string, TcpChannel> item in tcpChannels.ToArray())
            {
                item.Value.StopAsync().GetAwaiter().GetResult();
            }
            tcpChannels.Clear();

            foreach (KeyValuePair<string, SerialPortChannel> item in serialChannels.ToArray())
            {
                item.Value.StopAsync().GetAwaiter().GetResult();
            }
            serialChannels.Clear();
        }

        internal static string BytesToHex(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(payload.Length * 3);
            for (int i = 0; i < payload.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(payload[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private void RaiseLog(CommLogEventArgs args)
        {
            Log?.Invoke(this, args);
        }

        private static byte[] BuildSendPayload(string message, bool convertHex, Encoding encoding)
        {
            if (convertHex)
            {
                return ParseHexPayload(message);
            }

            return encoding.GetBytes(message ?? string.Empty);
        }

        private static byte[] ParseHexPayload(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new FormatException("十六进制发送内容为空");
            }

            StringBuilder compact = new StringBuilder();
            for (int i = 0; i < message.Length; i++)
            {
                char c = message[i];
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                bool isHex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    throw new FormatException($"十六进制发送内容包含非法字符:{c}");
                }

                compact.Append(char.ToUpperInvariant(c));
            }

            if (compact.Length == 0)
            {
                throw new FormatException("十六进制发送内容为空");
            }

            if (compact.Length % 2 != 0)
            {
                throw new FormatException("十六进制发送长度必须为偶数");
            }

            byte[] result = new byte[compact.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                string part = compact.ToString(i * 2, 2);
                result[i] = Convert.ToByte(part, 16);
            }

            return result;
        }

        private static ParsedSocketInfo ParseSocketInfo(SocketInfo info, int connectTimeoutMs)
        {
            if (info == null)
            {
                throw new InvalidOperationException("TCP配置为空");
            }
            if (string.IsNullOrWhiteSpace(info.Name))
            {
                throw new InvalidOperationException("TCP名称为空");
            }
            if (string.IsNullOrWhiteSpace(info.Type))
            {
                throw new InvalidOperationException($"TCP类型为空:{info.Name}");
            }
            bool isServer;
            if (string.Equals(info.Type, "Server", StringComparison.Ordinal))
            {
                isServer = true;
            }
            else if (string.Equals(info.Type, "Client", StringComparison.Ordinal))
            {
                isServer = false;
            }
            else
            {
                throw new InvalidOperationException($"TCP类型无效:{info.Name}-{info.Type}");
            }
            if (string.IsNullOrWhiteSpace(info.Address) || !IPAddress.TryParse(info.Address, out IPAddress address))
            {
                throw new InvalidOperationException($"TCP地址无效:{info.Name}-{info.Address}");
            }
            if (info.Port <= 0 || info.Port > 65535)
            {
                throw new InvalidOperationException($"TCP端口无效:{info.Name}-{info.Port}");
            }

            int timeout = connectTimeoutMs > 0 ? connectTimeoutMs : info.ConnectTimeoutMs;
            if (timeout <= 0)
            {
                throw new InvalidOperationException($"TCP连接超时配置无效:{info.Name}-{timeout}");
            }

            CommFrameSettings frameSettings = ParseFrameSettings(info.FrameMode, info.FrameDelimiter, info.EncodingName, info.Name);

            return new ParsedSocketInfo
            {
                Name = info.Name,
                IsServer = isServer,
                Address = address,
                Port = info.Port,
                ConnectTimeoutMs = timeout,
                FrameSettings = frameSettings
            };
        }

        private static ParsedSerialInfo ParseSerialInfo(SerialPortInfo info)
        {
            if (info == null)
            {
                throw new InvalidOperationException("串口配置为空");
            }
            if (string.IsNullOrWhiteSpace(info.Name))
            {
                throw new InvalidOperationException("串口名称为空");
            }
            if (string.IsNullOrWhiteSpace(info.Port))
            {
                throw new InvalidOperationException($"串口号为空:{info.Name}");
            }
            if (!int.TryParse(info.BitRate, out int bitRate) || bitRate <= 0)
            {
                throw new InvalidOperationException($"波特率无效:{info.Name}-{info.BitRate}");
            }
            if (!int.TryParse(info.DataBit, out int dataBits) || dataBits < 5 || dataBits > 8)
            {
                throw new InvalidOperationException($"数据位无效:{info.Name}-{info.DataBit}");
            }
            if (!Enum.TryParse(info.CheckBit, false, out Parity parity))
            {
                throw new InvalidOperationException($"校验位无效:{info.Name}-{info.CheckBit}");
            }
            if (!Enum.TryParse(info.StopBit, false, out StopBits stopBits))
            {
                throw new InvalidOperationException($"停止位无效:{info.Name}-{info.StopBit}");
            }

            CommFrameSettings frameSettings = ParseFrameSettings(info.FrameMode, info.FrameDelimiter, info.EncodingName, info.Name);

            return new ParsedSerialInfo
            {
                Name = info.Name,
                PortName = info.Port,
                BitRate = bitRate,
                Parity = parity,
                DataBits = dataBits,
                StopBits = stopBits,
                FrameSettings = frameSettings
            };
        }

        private static CommFrameSettings ParseFrameSettings(string frameMode, string frameDelimiter, string encodingName, string name)
        {
            if (string.IsNullOrWhiteSpace(frameMode))
            {
                throw new InvalidOperationException($"帧模式为空:{name}");
            }

            CommFrameMode mode;
            if (string.Equals(frameMode, "Delimiter", StringComparison.Ordinal))
            {
                mode = CommFrameMode.Delimiter;
            }
            else if (string.Equals(frameMode, "Raw", StringComparison.Ordinal))
            {
                mode = CommFrameMode.Raw;
            }
            else
            {
                throw new InvalidOperationException($"帧模式无效:{name}-{frameMode}");
            }

            if (string.IsNullOrWhiteSpace(encodingName))
            {
                throw new InvalidOperationException($"编码为空:{name}");
            }

            Encoding encoding;
            try
            {
                encoding = Encoding.GetEncoding(encodingName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"编码无效:{name}-{encodingName}，{ex.Message}");
            }

            byte[] delimiter = null;
            if (mode == CommFrameMode.Delimiter)
            {
                delimiter = ParseDelimiter(frameDelimiter, name);
            }

            return new CommFrameSettings(mode, delimiter, encoding);
        }

        private static byte[] ParseDelimiter(string delimiter, string name)
        {
            if (string.IsNullOrWhiteSpace(delimiter))
            {
                throw new InvalidOperationException($"分隔符为空:{name}");
            }

            if (string.Equals(delimiter, "\\n", StringComparison.Ordinal))
            {
                return new[] { (byte)'\n' };
            }
            if (string.Equals(delimiter, "\\r", StringComparison.Ordinal))
            {
                return new[] { (byte)'\r' };
            }
            if (string.Equals(delimiter, "\\r\\n", StringComparison.Ordinal))
            {
                return new[] { (byte)'\r', (byte)'\n' };
            }

            throw new InvalidOperationException($"分隔符无效:{name}-{delimiter}，仅支持\\n/\\r/\\r\\n");
        }
    }
}
