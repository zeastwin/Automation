using System;
// 模块：通讯运行时。
// 职责范围：管理 TCP/串口通道、原始数据接收、事务等待和通讯配置模型。
// 排查入口：用 Log 区分生命周期/发送/接收错误，用 FramesDropped 判断积压；同名通道由 lifecycleGate 串行变更。

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

    public enum TcpConnectionState
    {
        Stopped,
        Listening,
        Connecting,
        Connected,
        Reconnecting,
        Faulted
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

    public sealed class CommFramesDroppedEventArgs : EventArgs
    {
        public CommFramesDroppedEventArgs(CommChannelKind kind, string name, long droppedFrames)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            DroppedFrames = droppedFrames;
        }

        public CommChannelKind Kind { get; }
        public string Name { get; }
        public long DroppedFrames { get; }
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
        public static readonly TcpStatus Empty = new TcpStatus(
            false, false, false, 0, 0, TcpConnectionState.Stopped, string.Empty);

        public TcpStatus(bool isServer, bool isStarted, bool isConnected, int clientCount,
            long droppedFrames, TcpConnectionState connectionState, string lastError)
        {
            IsServer = isServer;
            IsStarted = isStarted;
            IsConnected = isConnected;
            ClientCount = clientCount;
            DroppedFrames = droppedFrames;
            ConnectionState = connectionState;
            LastError = lastError ?? string.Empty;
        }

        public bool IsServer { get; }
        public bool IsStarted { get; }
        public bool IsConnected { get; }
        public int ClientCount { get; }
        public long DroppedFrames { get; }
        public TcpConnectionState ConnectionState { get; }
        public string LastError { get; }
    }

    public sealed class SerialStatus
    {
        public static readonly SerialStatus Empty = new SerialStatus(false, 0);

        public SerialStatus(bool isOpen, long droppedFrames)
        {
            IsOpen = isOpen;
            DroppedFrames = droppedFrames;
        }

        public bool IsOpen { get; }
        public long DroppedFrames { get; }
    }

    internal static class RuntimeConfig
    {
        private static readonly int pendingReceiverLimit = LoadPendingReceiverLimit();

        public static int PendingReceiverLimit => pendingReceiverLimit;

        private static int LoadPendingReceiverLimit()
        {
            if (!AppConfigStorage.TryGetCached(out AppConfig config, out string error))
            {
                throw new InvalidOperationException(error);
            }
            return config.CommMaxMessageQueueSize;
        }
    }

    internal sealed class ParsedSocketInfo
    {
        public string Name { get; set; }
        public bool IsServer { get; set; }
        public IPAddress LocalAddress { get; set; }
        public int LocalPort { get; set; }
        public IPAddress RemoteAddress { get; set; }
        public bool AcceptAnyRemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public bool AutoReconnect { get; set; }
        public int ConnectTimeoutMs { get; set; }
        public Encoding Encoding { get; set; }

        public string ListenerKey => $"{LocalAddress}:{LocalPort}";
    }

    internal sealed class ParsedSerialInfo
    {
        public string Name { get; set; }
        public string PortName { get; set; }
        public int BitRate { get; set; }
        public Parity Parity { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }
        public Encoding Encoding { get; set; }
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

    internal sealed class ReceiveDispatcher
    {
        private const long MaxPendingBytes = 16L * 1024 * 1024;
        private readonly object sync = new object();
        private readonly Dictionary<long, Waiter> waiters = new Dictionary<long, Waiter>();
        private readonly Queue<long> waiterOrder = new Queue<long>();
        private readonly Queue<ReceivedFrame> pendingFrames = new Queue<ReceivedFrame>();
        private readonly int pendingLimit;
        private readonly Action<long> framesDropped;
        private long waiterId;
        private bool closed;
        private string closeReason;
        private long droppedFrames;
        private long pendingBytes;
        private long lastReportedDroppedFrames;

        public long DroppedFrames => Interlocked.Read(ref droppedFrames);

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

        public ReceiveDispatcher(int pendingLimit, Action<long> framesDropped = null)
        {
            if (pendingLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pendingLimit), $"等待队列上限无效:{pendingLimit}");
            }
            this.pendingLimit = pendingLimit;
            this.framesDropped = framesDropped;
        }

        public Task<ReceivedFrame> WaitNextAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), $"接收超时无效:{timeoutMs}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                if (closed)
                {
                    throw new InvalidOperationException(closeReason ?? "通讯通道已关闭");
                }
                if (pendingFrames.Count > 0)
                {
                    ReceivedFrame pending = pendingFrames.Dequeue();
                    pendingBytes -= pending.Payload?.Length ?? 0;
                    return Task.FromResult(pending);
                }
                if (waiters.Count >= pendingLimit)
                {
                    throw new InvalidOperationException($"接收等待队列已满:{pendingLimit}");
                }

                long id = Interlocked.Increment(ref waiterId);
                var source = new TaskCompletionSource<ReceivedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
                var waiter = new Waiter(id, source);
                waiters.Add(id, waiter);
                waiterOrder.Enqueue(id);
                waiter.TimeoutTimer = new Timer(_ => CancelWaiter(id, new TimeoutException("接收超时")),
                    null, timeoutMs, Timeout.Infinite);
                if (cancellationToken.CanBeCanceled)
                {
                    waiter.CancellationRegistration = cancellationToken.Register(() =>
                        CancelWaiter(id, new OperationCanceledException("接收已取消", cancellationToken)));
                }
                return source.Task;
            }
        }

        public void Publish(ReceivedFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            Waiter target = null;
            long reportDroppedFrames = 0;
            lock (sync)
            {
                if (closed)
                {
                    return;
                }
                while (waiterOrder.Count > 0)
                {
                    long id = waiterOrder.Dequeue();
                    if (waiters.TryGetValue(id, out target))
                    {
                        waiters.Remove(id);
                        break;
                    }
                }
                if (target == null)
                {
                    int frameBytes = frame.Payload?.Length ?? 0;
                    while (pendingFrames.Count > 0
                        && (pendingFrames.Count >= pendingLimit || pendingBytes + frameBytes > MaxPendingBytes))
                    {
                        ReceivedFrame dropped = pendingFrames.Dequeue();
                        pendingBytes -= dropped.Payload?.Length ?? 0;
                        Interlocked.Increment(ref droppedFrames);
                    }
                    pendingFrames.Enqueue(frame);
                    pendingBytes += frameBytes;
                    long totalDropped = Interlocked.Read(ref droppedFrames);
                    if (totalDropped > 0
                        && (lastReportedDroppedFrames == 0
                            || totalDropped - lastReportedDroppedFrames >= pendingLimit))
                    {
                        lastReportedDroppedFrames = totalDropped;
                        reportDroppedFrames = totalDropped;
                    }
                }
            }
            if (target == null)
            {
                if (reportDroppedFrames > 0)
                {
                    try
                    {
                        framesDropped?.Invoke(reportDroppedFrames);
                    }
                    catch
                    {
                        // 丢帧观察者不能破坏通讯接收线程。
                    }
                }
                return;
            }
            target.Dispose();
            target.Source.TrySetResult(frame);
        }

        public void Close(string reason)
        {
            List<Waiter> closingWaiters;
            lock (sync)
            {
                if (closed)
                {
                    return;
                }
                closed = true;
                closeReason = string.IsNullOrWhiteSpace(reason) ? "通讯通道已关闭" : reason;
                closingWaiters = waiters.Values.ToList();
                waiters.Clear();
                waiterOrder.Clear();
                pendingFrames.Clear();
                pendingBytes = 0;
            }
            foreach (Waiter waiter in closingWaiters)
            {
                waiter.Dispose();
                waiter.Source.TrySetException(new InvalidOperationException(closeReason));
            }
        }

        private void CancelWaiter(long id, Exception ex)
        {
            Waiter waiter;
            lock (sync)
            {
                if (!waiters.TryGetValue(id, out waiter))
                {
                    return;
                }
                waiters.Remove(id);
            }
            waiter.Dispose();
            waiter.Source.TrySetException(ex);
        }

        public void ClearPending()
        {
            lock (sync)
            {
                pendingFrames.Clear();
                pendingBytes = 0;
            }
        }
    }

    internal sealed class TcpServerListener
    {
        private readonly object sync = new object();
        private readonly SemaphoreSlim lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly IPAddress address;
        private readonly int port;
        private readonly Action<CommLogEventArgs> log;
        private readonly List<TcpChannel> channels = new List<TcpChannel>();
        private TcpListener listener;
        private CancellationTokenSource cts;
        private Task acceptTask;
        private bool isRunning;

        public TcpServerListener(IPAddress address, int port, Action<CommLogEventArgs> log)
        {
            this.address = address;
            this.port = port;
            this.log = log;
        }

        public async Task RegisterAsync(TcpChannel channel, CancellationToken cancellationToken)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (sync)
                {
                    if (!channels.Contains(channel)) channels.Add(channel);
                }
                if (isRunning) return;

                var source = new CancellationTokenSource();
                var candidate = new TcpListener(address, port);
                try
                {
                    candidate.Start();
                }
                catch
                {
                    lock (sync) channels.Remove(channel);
                    source.Dispose();
                    throw;
                }

                listener = candidate;
                cts = source;
                isRunning = true;
                acceptTask = AcceptLoopAsync(candidate, source.Token);
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public async Task UnregisterAsync(TcpChannel channel)
        {
            Task taskToWait = null;
            CancellationTokenSource sourceToDispose = null;
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                bool empty;
                lock (sync)
                {
                    channels.Remove(channel);
                    empty = channels.Count == 0;
                }
                if (!empty || !isRunning) return;

                isRunning = false;
                sourceToDispose = cts;
                cts = null;
                sourceToDispose?.Cancel();
                try { listener?.Stop(); } catch { }
                listener = null;
                taskToWait = acceptTask;
                acceptTask = null;
            }
            finally
            {
                lifecycleGate.Release();
            }

            await WaitBackgroundTaskAsync(taskToWait).ConfigureAwait(false);
            sourceToDispose?.Dispose();
        }

        public async Task StopAsync()
        {
            Task taskToWait;
            CancellationTokenSource sourceToDispose;
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                isRunning = false;
                sourceToDispose = cts;
                cts = null;
                sourceToDispose?.Cancel();
                try { listener?.Stop(); } catch { }
                listener = null;
                taskToWait = acceptTask;
                acceptTask = null;
                lock (sync) channels.Clear();
            }
            finally
            {
                lifecycleGate.Release();
            }

            await WaitBackgroundTaskAsync(taskToWait).ConfigureAwait(false);
            sourceToDispose?.Dispose();
        }

        private async Task AcceptLoopAsync(TcpListener activeListener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient accepted;
                try
                {
                    accepted = await activeListener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        await HandleFaultedListenerAsync(activeListener, ex).ConfigureAwait(false);
                    }
                    break;
                }

                IPEndPoint remoteEndPoint = accepted.Client.RemoteEndPoint as IPEndPoint;
                TcpChannel target = SelectTarget(remoteEndPoint);
                if (target == null || !target.TryAttachAcceptedClient(accepted, remoteEndPoint))
                {
                    string remote = remoteEndPoint?.ToString() ?? "未知端点";
                    log?.Invoke(new CommLogEventArgs(CommChannelKind.TcpServer, CommDirection.Error,
                        $"{address}:{port}", $"拒绝未匹配或无空闲逻辑通道的客户端:{remote}", null, remote, null));
                    TcpChannel.SafeCloseClient(accepted);
                }
            }
        }

        private async Task HandleFaultedListenerAsync(TcpListener activeListener, Exception exception)
        {
            CancellationTokenSource sourceToDispose = null;
            TcpChannel[] affectedChannels = Array.Empty<TcpChannel>();
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(listener, activeListener)) return;
                isRunning = false;
                sourceToDispose = cts;
                cts = null;
                sourceToDispose?.Cancel();
                try { activeListener.Stop(); } catch { }
                listener = null;
                acceptTask = null;
                lock (sync) affectedChannels = channels.ToArray();
                foreach (TcpChannel channel in affectedChannels) channel.HandleListenerFault(exception);
            }
            finally
            {
                lifecycleGate.Release();
            }
            sourceToDispose?.Dispose();
            log?.Invoke(new CommLogEventArgs(CommChannelKind.TcpServer, CommDirection.Error,
                $"{address}:{port}", $"TCP服务监听异常:{exception.Message}", null, null, exception));
        }

        private TcpChannel SelectTarget(IPEndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null) return null;
            TcpChannel[] snapshot;
            lock (sync) snapshot = channels.ToArray();
            return snapshot
                .Where(channel => channel.CanAcceptRemote(remoteEndPoint))
                .OrderByDescending(channel => channel.GetRemoteMatchSpecificity(remoteEndPoint))
                .ThenBy(channel => channel.Name, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static async Task WaitBackgroundTaskAsync(Task task)
        {
            if (task == null) return;
            Task completed = await Task.WhenAny(task, Task.Delay(1000)).ConfigureAwait(false);
            if (ReferenceEquals(completed, task))
            {
                try { await task.ConfigureAwait(false); } catch { }
                return;
            }
            _ = task.ContinueWith(completedTask => _ = completedTask.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    internal sealed class TcpChannel
    {
        private readonly ParsedSocketInfo info;
        private readonly Action<CommLogEventArgs> log;
        private readonly Func<bool> loggingEnabled;
        private readonly ReceiveDispatcher dispatcher;
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly object stateLock = new object();
        private readonly object clientLock = new object();
        private readonly List<TcpClient> clients = new List<TcpClient>();
        private readonly ConcurrentDictionary<TcpClient, Task> readTasks = new ConcurrentDictionary<TcpClient, Task>();

        private CancellationTokenSource cts;
        private TcpClient client;
        private Task clientWorkerTask;
        private TcpServerListener serverListener;
        private bool isStarted;
        private bool isConnected;
        private TcpConnectionState connectionState = TcpConnectionState.Stopped;
        private string lastError = string.Empty;

        public TcpChannel(ParsedSocketInfo info, Action<CommLogEventArgs> log, Func<bool> loggingEnabled,
            Action<CommFramesDroppedEventArgs> framesDropped, int pendingLimit)
        {
            this.info = info;
            this.log = log;
            this.loggingEnabled = loggingEnabled;
            dispatcher = new ReceiveDispatcher(pendingLimit,
                count => framesDropped?.Invoke(new CommFramesDroppedEventArgs(Kind, info.Name, count)));
        }

        public string Name => info.Name;
        public bool IsStarted
        {
            get { lock (stateLock) return isStarted; }
        }
        public bool IsServer => info.IsServer;
        public CommChannelKind Kind => IsServer ? CommChannelKind.TcpServer : CommChannelKind.TcpClient;
        public Encoding Encoding => info.Encoding;

        public bool Matches(ParsedSocketInfo target)
        {
            if (target == null) return false;
            return string.Equals(info.Name, target.Name, StringComparison.Ordinal)
                && info.IsServer == target.IsServer
                && Equals(info.LocalAddress, target.LocalAddress)
                && info.LocalPort == target.LocalPort
                && Equals(info.RemoteAddress, target.RemoteAddress)
                && info.AcceptAnyRemoteAddress == target.AcceptAnyRemoteAddress
                && info.RemotePort == target.RemotePort
                && info.AutoReconnect == target.AutoReconnect
                && info.ConnectTimeoutMs == target.ConnectTimeoutMs
                && string.Equals(info.Encoding.WebName, target.Encoding.WebName, StringComparison.OrdinalIgnoreCase);
        }

        public TcpStatus GetStatus()
        {
            int clientCount;
            lock (clientLock)
            {
                clientCount = IsServer ? clients.Count : (client == null ? 0 : 1);
            }
            lock (stateLock)
            {
                return new TcpStatus(IsServer, isStarted, isConnected, clientCount,
                    dispatcher.DroppedFrames, connectionState, lastError);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken, TcpServerListener sharedListener)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsStarted) return;

            var source = new CancellationTokenSource();
            cts = source;
            if (IsServer)
            {
                if (sharedListener == null) throw new InvalidOperationException("TCP服务端缺少共享监听器");
                SetState(true, false, TcpConnectionState.Listening, string.Empty);
                serverListener = sharedListener;
                try
                {
                    await sharedListener.RegisterAsync(this, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch
                {
                    serverListener = null;
                    cts = null;
                    source.Dispose();
                    SetState(false, false, TcpConnectionState.Faulted, "TCP服务监听启动失败");
                    throw;
                }
            }

            SetState(true, false, TcpConnectionState.Connecting, string.Empty);
            var firstAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            clientWorkerTask = RunClientLoopAsync(source.Token, firstAttempt);
            using (cancellationToken.Register(() => firstAttempt.TrySetCanceled()))
            {
                bool connected;
                try
                {
                    connected = await firstAttempt.Task.ConfigureAwait(false);
                }
                catch
                {
                    await StopAsync().ConfigureAwait(false);
                    throw;
                }
                if (!connected && !info.AutoReconnect)
                {
                    string error = GetStatus().LastError;
                    await StopAsync().ConfigureAwait(false);
                    throw new InvalidOperationException(error);
                }
            }
        }

        public async Task StopAsync()
        {
            CancellationTokenSource currentCts = cts;
            cts = null;
            currentCts?.Cancel();

            TcpServerListener currentListener = serverListener;
            serverListener = null;
            if (currentListener != null)
            {
                await currentListener.UnregisterAsync(this).ConfigureAwait(false);
            }

            List<TcpClient> clientsToClose;
            lock (clientLock)
            {
                clientsToClose = clients.ToList();
                clients.Clear();
                if (client != null && !clientsToClose.Contains(client)) clientsToClose.Add(client);
                client = null;
            }
            foreach (TcpClient target in clientsToClose) SafeCloseClient(target);

            dispatcher.Close("TCP通道已关闭");
            List<Task> backgroundTasks = readTasks.Values.ToList();
            if (clientWorkerTask != null) backgroundTasks.Add(clientWorkerTask);
            await WaitBackgroundTasksAsync(backgroundTasks).ConfigureAwait(false);
            clientWorkerTask = null;
            currentCts?.Dispose();
            SetState(false, false, TcpConnectionState.Stopped, string.Empty);
        }

        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (!GetStatus().IsConnected) throw new InvalidOperationException("TCP未连接");

            await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsServer)
                {
                    List<TcpClient> targetClients;
                    lock (clientLock) targetClients = clients.ToList();
                    if (targetClients.Count == 0) throw new InvalidOperationException("TCP服务端无在线客户端");

                    var brokenClients = new List<TcpClient>();
                    int sendCount = 0;
                    foreach (TcpClient tcpClient in targetClients)
                    {
                        try
                        {
                            NetworkStream stream = tcpClient.GetStream();
                            await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
                            sendCount++;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            brokenClients.Add(tcpClient);
                        }
                    }
                    RemoveAndCloseClients(brokenClients);
                    if (sendCount == 0) throw new InvalidOperationException("TCP服务端发送失败：无可用客户端");
                    return;
                }

                TcpClient targetClient;
                lock (clientLock) targetClient = client;
                if (targetClient == null) throw new InvalidOperationException("TCP客户端未连接");
                try
                {
                    NetworkStream clientStream = targetClient.GetStream();
                    await clientStream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    RemoveAndCloseClients(new[] { targetClient });
                    throw;
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

        public void ClearPendingMessages()
        {
            dispatcher.ClearPending();
        }

        public string DecodeText(byte[] payload)
        {
            return Encoding.GetString(payload ?? Array.Empty<byte>());
        }

        public string GetRemoteEndPointForLog()
        {
            lock (clientLock)
            {
                if (IsServer && clients.Count != 1) return null;
                TcpClient target = IsServer ? clients.FirstOrDefault() : client;
                try { return target?.Client?.RemoteEndPoint?.ToString(); } catch { return null; }
            }
        }

        public bool CanAcceptRemote(IPEndPoint remoteEndPoint)
        {
            if (!IsServer || remoteEndPoint == null || !IsStarted || !MatchesRemote(remoteEndPoint)) return false;
            if (info.AcceptAnyRemoteAddress) return true;
            lock (clientLock) return clients.Count == 0;
        }

        public int GetRemoteMatchSpecificity(IPEndPoint remoteEndPoint)
        {
            if (!MatchesRemote(remoteEndPoint)) return -1;
            int score = info.AcceptAnyRemoteAddress ? 0 : 2;
            if (info.RemotePort > 0) score++;
            return score;
        }

        public bool TryAttachAcceptedClient(TcpClient accepted, IPEndPoint remoteEndPoint)
        {
            if (accepted == null || !CanAcceptRemote(remoteEndPoint)) return false;
            lock (clientLock)
            {
                if (!info.AcceptAnyRemoteAddress && clients.Count > 0) return false;
                clients.Add(accepted);
            }
            SetState(true, true, TcpConnectionState.Connected, string.Empty);
            TrackReadTask(accepted, ReadLoopAsync(accepted, cts?.Token ?? CancellationToken.None));
            return true;
        }

        public void HandleListenerFault(Exception exception)
        {
            RemoveAndCloseClients(GetClientSnapshot());
            dispatcher.Close("TCP服务监听异常");
            SetState(false, false, TcpConnectionState.Faulted, exception?.Message ?? "TCP服务监听异常");
        }

        private bool MatchesRemote(IPEndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null) return false;
            bool addressMatches = info.AcceptAnyRemoteAddress || Equals(info.RemoteAddress, remoteEndPoint.Address);
            return addressMatches && (info.RemotePort == 0 || info.RemotePort == remoteEndPoint.Port);
        }

        private async Task RunClientLoopAsync(CancellationToken token, TaskCompletionSource<bool> firstAttempt)
        {
            int failedAttempts = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    SetState(true, false,
                        failedAttempts == 0 ? TcpConnectionState.Connecting : TcpConnectionState.Reconnecting,
                        failedAttempts == 0 ? string.Empty : GetStatus().LastError);
                    TcpClient connectedClient;
                    try
                    {
                        connectedClient = await ConnectClientAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        failedAttempts++;
                        string message = $"TCP连接失败:{info.Name} {ex.GetBaseException().Message}";
                        SetState(info.AutoReconnect, false,
                            info.AutoReconnect ? TcpConnectionState.Reconnecting : TcpConnectionState.Faulted,
                            message);
                        log?.Invoke(new CommLogEventArgs(Kind, CommDirection.Error, info.Name, message,
                            null, $"{info.RemoteAddress}:{info.RemotePort}", ex));
                        firstAttempt.TrySetResult(false);
                        if (!info.AutoReconnect) return;
                        await Task.Delay(GetReconnectDelayMs(failedAttempts), token).ConfigureAwait(false);
                        continue;
                    }

                    lock (clientLock)
                    {
                        client = connectedClient;
                        clients.Clear();
                        clients.Add(connectedClient);
                    }
                    failedAttempts = 0;
                    SetState(true, true, TcpConnectionState.Connected, string.Empty);
                    firstAttempt.TrySetResult(true);
                    await ReadLoopAsync(connectedClient, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) break;

                    string disconnected = $"TCP连接已断开:{info.Name}";
                    if (!info.AutoReconnect)
                    {
                        SetState(false, false, TcpConnectionState.Faulted, disconnected);
                        return;
                    }
                    failedAttempts = 1;
                    SetState(true, false, TcpConnectionState.Reconnecting, disconnected);
                    await Task.Delay(GetReconnectDelayMs(failedAttempts), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                firstAttempt.TrySetCanceled();
                if (token.IsCancellationRequested)
                {
                    SetState(false, false, TcpConnectionState.Stopped, string.Empty);
                }
            }
        }

        private async Task<TcpClient> ConnectClientAsync(CancellationToken token)
        {
            var candidate = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                if (info.LocalPort > 0)
                {
                    candidate.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                }
                candidate.Client.Bind(new IPEndPoint(info.LocalAddress, info.LocalPort));
                Task connectTask = candidate.ConnectAsync(info.RemoteAddress, info.RemotePort);
                Task timeoutTask = Task.Delay(info.ConnectTimeoutMs, token);
                Task completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (!ReferenceEquals(completed, connectTask))
                {
                    SafeCloseClient(candidate);
                    _ = connectTask.ContinueWith(task => _ = task.Exception,
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException($"{info.Name}连接超时");
                }
                await connectTask.ConfigureAwait(false);
                return candidate;
            }
            catch
            {
                SafeCloseClient(candidate);
                throw;
            }
        }

        private void TrackReadTask(TcpClient targetClient, Task task)
        {
            readTasks[targetClient] = task;
            _ = task.ContinueWith(completedTask =>
            {
                readTasks.TryRemove(targetClient, out _);
                _ = completedTask.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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
                RemoveAndCloseClients(new[] { targetClient });
                return;
            }

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

                byte[] payload = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, payload, 0, bytesRead);
                dispatcher.Publish(new ReceivedFrame(payload, remoteEndPoint));
                if (loggingEnabled?.Invoke() == true)
                {
                    string text = DecodeText(payload);
                    string hex = CommunicationHub.BytesToHex(payload);
                    log?.Invoke(new CommLogEventArgs(Kind, CommDirection.Receive, info.Name, text, hex, remoteEndPoint, null));
                }
            }

            RemoveAndCloseClients(new[] { targetClient });
        }

        private List<TcpClient> GetClientSnapshot()
        {
            lock (clientLock) return clients.ToList();
        }

        private void RemoveAndCloseClients(IEnumerable<TcpClient> targets)
        {
            if (targets == null) return;
            List<TcpClient> snapshot = targets.Where(item => item != null).Distinct().ToList();
            lock (clientLock)
            {
                foreach (TcpClient target in snapshot)
                {
                    clients.Remove(target);
                    if (ReferenceEquals(client, target)) client = null;
                }
            }
            foreach (TcpClient target in snapshot) SafeCloseClient(target);
            int remaining;
            lock (clientLock) remaining = clients.Count;
            bool started = IsStarted;
            if (IsServer)
            {
                SetState(started, remaining > 0,
                    !started ? TcpConnectionState.Stopped
                    : remaining > 0 ? TcpConnectionState.Connected : TcpConnectionState.Listening,
                    string.Empty);
                return;
            }

            SetState(started, false,
                !started ? TcpConnectionState.Stopped
                : info.AutoReconnect ? TcpConnectionState.Reconnecting : TcpConnectionState.Faulted,
                started ? $"TCP连接已断开:{info.Name}" : string.Empty);
        }

        private void SetState(bool started, bool connected, TcpConnectionState state, string error)
        {
            lock (stateLock)
            {
                isStarted = started;
                isConnected = connected;
                connectionState = state;
                lastError = error ?? string.Empty;
            }
        }

        private static int GetReconnectDelayMs(int failedAttempts)
        {
            if (failedAttempts <= 1) return 1000;
            if (failedAttempts == 2) return 2000;
            if (failedAttempts == 3) return 5000;
            if (failedAttempts == 4) return 10000;
            return 30000;
        }

        private static async Task WaitBackgroundTasksAsync(IReadOnlyCollection<Task> tasks)
        {
            if (tasks == null || tasks.Count == 0) return;
            Task allTasks = Task.WhenAll(tasks);
            Task completed = await Task.WhenAny(allTasks, Task.Delay(1000)).ConfigureAwait(false);
            if (ReferenceEquals(completed, allTasks))
            {
                try { await allTasks.ConfigureAwait(false); } catch { }
                return;
            }
            _ = allTasks.ContinueWith(task => _ = task.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        internal static void SafeCloseClient(TcpClient tcpClient)
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
        private readonly ParsedSerialInfo info;
        private readonly Action<CommLogEventArgs> log;
        private readonly Func<bool> loggingEnabled;
        private readonly ReceiveDispatcher dispatcher;
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly object sync = new object();

        private CancellationTokenSource cts;
        private SerialPort serialPort;
        private Task readTask;
        private volatile bool isOpen;

        public SerialPortChannel(ParsedSerialInfo info, Action<CommLogEventArgs> log, Func<bool> loggingEnabled,
            Action<CommFramesDroppedEventArgs> framesDropped, int pendingLimit)
        {
            this.info = info;
            this.log = log;
            this.loggingEnabled = loggingEnabled;
            dispatcher = new ReceiveDispatcher(pendingLimit,
                count => framesDropped?.Invoke(new CommFramesDroppedEventArgs(
                    CommChannelKind.SerialPort, info.Name, count)));
        }

        public bool IsOpen => isOpen;
        public Encoding Encoding => info.Encoding;
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
                && string.Equals(info.Encoding.WebName, target.Encoding.WebName, StringComparison.OrdinalIgnoreCase);
        }

        public SerialStatus GetStatus()
        {
            return new SerialStatus(isOpen, dispatcher.DroppedFrames);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isOpen)
            {
                return Task.CompletedTask;
            }

            cts = new CancellationTokenSource();
            lock (sync)
            {
                serialPort = new SerialPort(info.PortName, info.BitRate, info.Parity, info.DataBits, info.StopBits)
                {
                    Encoding = info.Encoding
                };
                serialPort.Open();
                isOpen = true;
            }

            readTask = ReadLoopAsync(cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            CancellationTokenSource currentCts = cts;
            cts = null;
            currentCts?.Cancel();

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
            currentCts?.Dispose();
            Task currentReadTask = readTask;
            readTask = null;
            if (currentReadTask != null)
            {
                Task completed = await Task.WhenAny(currentReadTask, Task.Delay(1000)).ConfigureAwait(false);
                if (ReferenceEquals(completed, currentReadTask))
                {
                    await currentReadTask.ConfigureAwait(false);
                }
                else
                {
                    _ = currentReadTask.ContinueWith(task => _ = task.Exception,
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                }
            }
        }

        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
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

                await port.BaseStream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
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

        public void ClearPendingMessages()
        {
            dispatcher.ClearPending();
        }

        public string DecodeText(byte[] payload)
        {
            return Encoding.GetString(payload ?? Array.Empty<byte>());
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
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

                byte[] payload = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, payload, 0, bytesRead);
                dispatcher.Publish(new ReceivedFrame(payload, info.PortName));
                if (loggingEnabled?.Invoke() == true)
                {
                    string text = DecodeText(payload);
                    string hex = CommunicationHub.BytesToHex(payload);
                    log?.Invoke(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Receive, info.Name, text, hex, info.PortName, null));
                }
            }

            lock (sync)
            {
                try
                {
                    serialPort?.Close();
                }
                catch
                {
                }
                serialPort?.Dispose();
                serialPort = null;
                isOpen = false;
            }
            dispatcher.Close("串口已关闭");
        }

    }

    public sealed class CommunicationHub : IDisposable
    {
        private const int MaxMessageBytes = 1024 * 1024;
        private sealed class TransactionLease : IDisposable
        {
            private SemaphoreSlim gate;

            public TransactionLease(SemaphoreSlim gate)
            {
                this.gate = gate;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref gate, null)?.Release();
            }
        }

        private readonly ConcurrentDictionary<string, SemaphoreSlim> transactionGates =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> lifecycleGates =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TcpChannel> tcpChannels = new ConcurrentDictionary<string, TcpChannel>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TcpServerListener> tcpServerListeners =
            new ConcurrentDictionary<string, TcpServerListener>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SerialPortChannel> serialChannels = new ConcurrentDictionary<string, SerialPortChannel>(StringComparer.OrdinalIgnoreCase);
        private int disposed;

        public event EventHandler<CommLogEventArgs> Log;
        public event EventHandler<CommFramesDroppedEventArgs> FramesDropped;

        public async Task StartTcpAsync(SocketInfo info, int connectTimeoutMs = 0,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ParsedSocketInfo parsed = ParseSocketInfo(info, connectTimeoutMs);
            // 同名通道的启停共用 lifecycleGate，防止并发重连把新通道误删或留下两个接收循环。
            SemaphoreSlim lifecycleGate = lifecycleGates.GetOrAdd("TCP:" + parsed.Name, _ => new SemaphoreSlim(1, 1));
            await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {

                if (tcpChannels.TryGetValue(parsed.Name, out TcpChannel existing))
                {
                    if (existing.Matches(parsed) && existing.IsStarted)
                    {
                        return;
                    }

                    await existing.StopAsync().ConfigureAwait(false);
                }

                TcpChannel channel = new TcpChannel(parsed, RaiseLog, () => Log != null,
                    RaiseFramesDropped, RuntimeConfig.PendingReceiverLimit);
                tcpChannels[parsed.Name] = channel;

                try
                {
                    TcpServerListener sharedListener = null;
                    if (parsed.IsServer)
                    {
                        sharedListener = tcpServerListeners.GetOrAdd(parsed.ListenerKey,
                            _ => new TcpServerListener(parsed.LocalAddress, parsed.LocalPort, RaiseLog));
                    }
                    await channel.StartAsync(cancellationToken, sharedListener).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await channel.StopAsync().ConfigureAwait(false);
                    tcpChannels.TryRemove(parsed.Name, out _);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("TCP启动已取消", ex, cancellationToken);
                    }
                    string message = GetBaseErrorMessage(ex);
                    RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Error, parsed.Name, message, null, null, ex));
                    throw new InvalidOperationException($"TCP启动失败:{parsed.Name} {message}", ex);
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public async Task StopTcpAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            SemaphoreSlim lifecycleGate = lifecycleGates.GetOrAdd("TCP:" + name, _ => new SemaphoreSlim(1, 1));
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (tcpChannels.TryRemove(name, out TcpChannel channel))
                {
                    await channel.StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                lifecycleGate.Release();
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
            if (!status.IsStarted || !status.IsConnected)
            {
                return false;
            }

            return !status.IsServer || status.ClientCount > 0;
        }

        public async Task<bool> SendTcpAsync(string name, string message, bool convertHex, CancellationToken cancellationToken = default)
        {
            try
            {
                using (IDisposable transaction = await EnterTcpTransactionAsync(name, cancellationToken).ConfigureAwait(false))
                {
                    return await SendTcpCoreAsync(name, message, convertHex, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private async Task<bool> SendTcpCoreAsync(string name, string message, bool convertHex, CancellationToken cancellationToken)
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
                await channel.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                if (Log != null)
                {
                    string text = convertHex ? BytesToHex(payload) : channel.DecodeText(payload);
                    string hex = BytesToHex(payload);
                    RaiseLog(new CommLogEventArgs(channel.Kind, CommDirection.Send, name, text, hex,
                        channel.GetRemoteEndPointForLog(), null));
                }
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

        public async Task<IDisposable> EnterTcpTransactionAsync(string name, CancellationToken cancellationToken)
        {
            return await EnterTransactionAsync("TCP:" + (name ?? string.Empty), cancellationToken).ConfigureAwait(false);
        }

        public async Task<IDisposable> EnterSerialTransactionAsync(string name, CancellationToken cancellationToken)
        {
            return await EnterTransactionAsync("SERIAL:" + (name ?? string.Empty), cancellationToken).ConfigureAwait(false);
        }

        public async Task<CommReceiveResult> SendReceiveTcpAsync(string name, string message, bool convertHex,
            int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (timeoutMs <= 0)
            {
                return CommReceiveResult.CreateFailure($"TCP请求超时配置无效:{timeoutMs}");
            }
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(timeoutMs);
                IDisposable transaction;
                try
                {
                    transaction = await EnterTcpTransactionAsync(name, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return CommReceiveResult.CreateFailure(cancellationToken.IsCancellationRequested
                        ? "TCP请求已取消" : "TCP请求超时");
                }
                using (transaction)
                {
                if (tcpChannels.TryGetValue(name ?? string.Empty, out TcpChannel requestChannel)
                    && requestChannel.IsServer && requestChannel.GetStatus().ClientCount != 1)
                {
                    return CommReceiveResult.CreateFailure("TCP服务端请求-响应模式要求恰好一个在线客户端");
                }
                ClearTcpMessages(name);
                Task<CommReceiveResult> receiveTask = ReceiveTcpCoreAsync(name, timeoutMs, timeoutCts.Token);
                bool sent = await SendTcpCoreAsync(name, message, convertHex, timeoutCts.Token).ConfigureAwait(false);
                if (!sent)
                {
                    bool timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
                    timeoutCts.Cancel();
                    await receiveTask.ConfigureAwait(false);
                    return CommReceiveResult.CreateFailure(cancellationToken.IsCancellationRequested
                        ? "TCP请求已取消"
                        : timedOut ? "TCP请求超时" : "TCP发送失败");
                }
                return await receiveTask.ConfigureAwait(false);
                }
            }
        }

        public async Task<CommReceiveResult> SendReceiveSerialAsync(string name, string message, bool convertHex,
            int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (timeoutMs <= 0)
            {
                return CommReceiveResult.CreateFailure($"串口请求超时配置无效:{timeoutMs}");
            }
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(timeoutMs);
                IDisposable transaction;
                try
                {
                    transaction = await EnterSerialTransactionAsync(name, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return CommReceiveResult.CreateFailure(cancellationToken.IsCancellationRequested
                        ? "串口请求已取消" : "串口请求超时");
                }
                using (transaction)
                {
                ClearSerialMessages(name);
                Task<CommReceiveResult> receiveTask = ReceiveSerialCoreAsync(name, timeoutMs, timeoutCts.Token);
                bool sent = await SendSerialCoreAsync(name, message, convertHex, timeoutCts.Token).ConfigureAwait(false);
                if (!sent)
                {
                    bool timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
                    timeoutCts.Cancel();
                    await receiveTask.ConfigureAwait(false);
                    return CommReceiveResult.CreateFailure(cancellationToken.IsCancellationRequested
                        ? "串口请求已取消"
                        : timedOut ? "串口请求超时" : "串口发送失败");
                }
                return await receiveTask.ConfigureAwait(false);
                }
            }
        }

        private async Task<IDisposable> EnterTransactionAsync(string key, CancellationToken cancellationToken)
        {
            SemaphoreSlim gate = transactionGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new TransactionLease(gate);
        }

        public async Task<CommReceiveResult> ReceiveTcpAsync(string name, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (timeoutMs <= 0)
            {
                return CommReceiveResult.CreateFailure($"TCP接收超时配置无效:{timeoutMs}");
            }
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            try
            {
                timeoutCts.CancelAfter(timeoutMs);
                using (IDisposable transaction = await EnterTcpTransactionAsync(name, timeoutCts.Token).ConfigureAwait(false))
                {
                    return await ReceiveTcpCoreAsync(name, timeoutMs, timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return CommReceiveResult.CreateFailure(cancellationToken.IsCancellationRequested
                    ? "TCP接收已取消" : "TCP接收超时");
            }
        }

        private async Task<CommReceiveResult> ReceiveTcpCoreAsync(string name, int timeoutMs, CancellationToken cancellationToken)
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
            if (!string.IsNullOrWhiteSpace(name) && tcpChannels.TryGetValue(name, out TcpChannel channel))
            {
                channel.ClearPendingMessages();
            }
        }

        public async Task StartSerialAsync(SerialPortInfo info, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ParsedSerialInfo parsed = ParseSerialInfo(info);
            // 串口和 TCP 使用相同生命周期规则；配置不变且已运行时 Start 是幂等操作。
            SemaphoreSlim lifecycleGate = lifecycleGates.GetOrAdd("SERIAL:" + parsed.Name, _ => new SemaphoreSlim(1, 1));
            await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {

                if (serialChannels.TryGetValue(parsed.Name, out SerialPortChannel existing))
                {
                    if (existing.Matches(parsed) && existing.IsOpen)
                    {
                        return;
                    }

                    await existing.StopAsync().ConfigureAwait(false);
                }

                SerialPortChannel channel = new SerialPortChannel(parsed, RaiseLog, () => Log != null,
                    RaiseFramesDropped, RuntimeConfig.PendingReceiverLimit);
                serialChannels[parsed.Name] = channel;

                try
                {
                    await channel.StartAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await channel.StopAsync().ConfigureAwait(false);
                    serialChannels.TryRemove(parsed.Name, out _);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("串口启动已取消", ex, cancellationToken);
                    }
                    string message = GetBaseErrorMessage(ex);
                    RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Error, parsed.Name, message, null, parsed.PortName, ex));
                    throw new InvalidOperationException($"串口启动失败:{parsed.Name} {message}", ex);
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public async Task StopSerialAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            SemaphoreSlim lifecycleGate = lifecycleGates.GetOrAdd("SERIAL:" + name, _ => new SemaphoreSlim(1, 1));
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (serialChannels.TryRemove(name, out SerialPortChannel channel))
                {
                    await channel.StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                lifecycleGate.Release();
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
            try
            {
                using (IDisposable transaction = await EnterSerialTransactionAsync(name, cancellationToken).ConfigureAwait(false))
                {
                    return await SendSerialCoreAsync(name, message, convertHex, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private async Task<bool> SendSerialCoreAsync(string name, string message, bool convertHex, CancellationToken cancellationToken)
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
                await channel.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                if (Log != null)
                {
                    string text = convertHex ? BytesToHex(payload) : channel.DecodeText(payload);
                    string hex = BytesToHex(payload);
                    RaiseLog(new CommLogEventArgs(CommChannelKind.SerialPort, CommDirection.Send, name, text, hex, channel.PortName, null));
                }
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
            if (timeoutMs <= 0)
            {
                return CommReceiveResult.CreateFailure($"串口接收超时配置无效:{timeoutMs}");
            }
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            try
            {
                timeoutCts.CancelAfter(timeoutMs);
                using (IDisposable transaction = await EnterSerialTransactionAsync(name, timeoutCts.Token).ConfigureAwait(false))
                {
                    return await ReceiveSerialCoreAsync(name, timeoutMs, timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return CommReceiveResult.CreateFailure(cancellationToken.IsCancellationRequested
                    ? "串口接收已取消" : "串口接收超时");
            }
        }

        private async Task<CommReceiveResult> ReceiveSerialCoreAsync(string name, int timeoutMs, CancellationToken cancellationToken)
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
            if (!string.IsNullOrWhiteSpace(name) && serialChannels.TryGetValue(name, out SerialPortChannel channel))
            {
                channel.ClearPendingMessages();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            Task[] tcpStops = tcpChannels.Values.Select(channel => channel.StopAsync()).ToArray();
            Task.WhenAll(tcpStops).GetAwaiter().GetResult();
            tcpChannels.Clear();
            Task[] listenerStops = tcpServerListeners.Values.Select(listener => listener.StopAsync()).ToArray();
            Task.WhenAll(listenerStops).GetAwaiter().GetResult();
            tcpServerListeners.Clear();

            Task[] serialStops = serialChannels.Values.Select(channel => channel.StopAsync()).ToArray();
            Task.WhenAll(serialStops).GetAwaiter().GetResult();
            serialChannels.Clear();
            transactionGates.Clear();
            lifecycleGates.Clear();
            Log = null;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(CommunicationHub));
            }
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
            EventHandler<CommLogEventArgs> handlers = Log;
            if (handlers == null)
            {
                return;
            }
            foreach (EventHandler<CommLogEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch
                {
                    // 日志观察者不能破坏通讯收发线程。
                }
            }
        }

        private void RaiseFramesDropped(CommFramesDroppedEventArgs args)
        {
            EventHandler<CommFramesDroppedEventArgs> handlers = FramesDropped;
            if (handlers == null)
            {
                return;
            }
            foreach (EventHandler<CommFramesDroppedEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch
                {
                    // 丢帧观察者不能破坏通讯接收线程。
                }
            }
        }

        public static bool TryValidateSocketInfo(SocketInfo info, out string error)
        {
            try
            {
                ParseSocketInfo(info, info?.ConnectTimeoutMs ?? 0);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = GetBaseErrorMessage(ex);
                return false;
            }
        }

        public static bool TryValidateSerialPortInfo(SerialPortInfo info, out string error)
        {
            try
            {
                ParseSerialInfo(info);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = GetBaseErrorMessage(ex);
                return false;
            }
        }

        private static string GetBaseErrorMessage(Exception ex)
        {
            Exception baseException = ex?.GetBaseException();
            if (baseException == null || string.IsNullOrWhiteSpace(baseException.Message))
            {
                return ex?.Message ?? "未知错误";
            }
            return baseException.Message;
        }

        private static byte[] BuildSendPayload(string message, bool convertHex, Encoding encoding)
        {
            if (convertHex)
            {
                return ParseHexPayload(message);
            }
            string text = message ?? string.Empty;
            int byteCount = encoding.GetByteCount(text);
            if (byteCount > MaxMessageBytes)
            {
                throw new InvalidOperationException($"发送消息长度超限:{byteCount}");
            }
            return encoding.GetBytes(text);
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
                if (compact.Length > MaxMessageBytes * 2)
                {
                    throw new FormatException($"十六进制发送长度超限:{compact.Length / 2}");
                }
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
            string hex = compact.ToString();
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = (byte)((ParseHexNibble(hex[i * 2]) << 4) | ParseHexNibble(hex[i * 2 + 1]));
            }

            return result;
        }

        private static int ParseHexNibble(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            return value - 'A' + 10;
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
            if (string.IsNullOrWhiteSpace(info.LocalAddress)
                || !IPAddress.TryParse(info.LocalAddress, out IPAddress localAddress))
            {
                throw new InvalidOperationException($"TCP本地地址无效:{info.Name}-{info.LocalAddress}");
            }
            if (localAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new InvalidOperationException($"TCP本地地址仅支持IPv4:{info.Name}-{info.LocalAddress}");
            }
            if (info.LocalPort < 0 || info.LocalPort > 65535 || (isServer && info.LocalPort == 0))
            {
                throw new InvalidOperationException($"TCP本地端口无效:{info.Name}-{info.LocalPort}");
            }

            bool acceptAnyRemoteAddress = isServer
                && string.Equals(info.RemoteAddress, "*", StringComparison.Ordinal);
            IPAddress remoteAddress = null;
            if (!acceptAnyRemoteAddress
                && (string.IsNullOrWhiteSpace(info.RemoteAddress)
                    || !IPAddress.TryParse(info.RemoteAddress, out remoteAddress)))
            {
                throw new InvalidOperationException($"TCP远端地址无效:{info.Name}-{info.RemoteAddress}");
            }
            if (remoteAddress != null && remoteAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new InvalidOperationException($"TCP远端地址仅支持IPv4:{info.Name}-{info.RemoteAddress}");
            }
            if (info.RemotePort < 0 || info.RemotePort > 65535 || (!isServer && info.RemotePort == 0))
            {
                throw new InvalidOperationException($"TCP远端端口无效:{info.Name}-{info.RemotePort}");
            }

            int timeout = connectTimeoutMs > 0 ? connectTimeoutMs : info.ConnectTimeoutMs;
            if (timeout <= 0)
            {
                throw new InvalidOperationException($"TCP连接超时配置无效:{info.Name}-{timeout}");
            }

            Encoding encoding = ParseEncoding(info.EncodingName, info.Name);

            return new ParsedSocketInfo
            {
                Name = info.Name,
                IsServer = isServer,
                LocalAddress = localAddress,
                LocalPort = info.LocalPort,
                RemoteAddress = remoteAddress,
                AcceptAnyRemoteAddress = acceptAnyRemoteAddress,
                RemotePort = info.RemotePort,
                AutoReconnect = !isServer && info.AutoReconnect,
                ConnectTimeoutMs = timeout,
                Encoding = encoding
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

            Encoding encoding = ParseEncoding(info.EncodingName, info.Name);

            return new ParsedSerialInfo
            {
                Name = info.Name,
                PortName = info.Port,
                BitRate = bitRate,
                Parity = parity,
                DataBits = dataBits,
                StopBits = stopBits,
                Encoding = encoding
            };
        }

        private static Encoding ParseEncoding(string encodingName, string name)
        {
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

            return encoding;
        }
    }
}
