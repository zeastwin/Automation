using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Automation.Bridge
{
    internal sealed class AutomationBridgeHost : IDisposable
    {
        internal const string DefaultPipeName = "AutomationBridgePipe";
        internal const string DefaultPipePath = @"\\.\pipe\AutomationBridgePipe";

        private readonly FrmMain owner;
        private readonly AutomationBridgeService service;
        private readonly object stateLock = new object();
        private CancellationTokenSource listenCts;
        private Task listenTask;
        private NamedPipeServerStream waitingServer;
        private bool started;

        public AutomationBridgeHost(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            service = new AutomationBridgeService(owner);
        }

        public void Start()
        {
            lock (stateLock)
            {
                if (started)
                {
                    return;
                }

                listenCts = new CancellationTokenSource();
                listenTask = Task.Run(() => ListenLoop(listenCts.Token));
                started = true;
                ReportInfo($"Automation Bridge 已启动：{DefaultPipePath}");
            }
        }

        public void Stop()
        {
            Task taskToWait = null;
            lock (stateLock)
            {
                if (!started)
                {
                    return;
                }

                started = false;
                taskToWait = listenTask;
                listenTask = null;

                try
                {
                    listenCts?.Cancel();
                }
                catch
                {
                }

                try
                {
                    waitingServer?.Dispose();
                }
                catch
                {
                }

                waitingServer = null;
                listenCts?.Dispose();
                listenCts = null;
            }

            if (taskToWait != null)
            {
                try
                {
                    taskToWait.Wait(1000);
                }
                catch
                {
                }
            }
        }

        private void ListenLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = CreateServer();
                    lock (stateLock)
                    {
                        if (!started)
                        {
                            server.Dispose();
                            return;
                        }
                        waitingServer = server;
                    }

                    server.WaitForConnection();
                    lock (stateLock)
                    {
                        if (ReferenceEquals(waitingServer, server))
                        {
                            waitingServer = null;
                        }
                    }

                    _ = Task.Run(() => ProcessClientAsync(server));
                    server = null;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested || !started)
                {
                    break;
                }
                catch (IOException) when (cancellationToken.IsCancellationRequested || !started)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ReportError($"Automation Bridge 监听异常：{ex.Message}");
                    break;
                }
                finally
                {
                    if (server != null)
                    {
                        try
                        {
                            server.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private async Task ProcessClientAsync(NamedPipeServerStream server)
        {
            using (server)
            using (var reader = new StreamReader(server, Encoding.UTF8, false, 4096, true))
            using (var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
            {
                PipeResponseMessage response;
                try
                {
                    string requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(requestLine))
                    {
                        response = PipeResponseMessage.FromBridgeResponse(
                            null,
                            AutomationBridgeResponse.Error(400, "EMPTY_REQUEST", "Pipe 请求为空。"));
                    }
                    else
                    {
                        PipeRequestMessage request = JsonConvert.DeserializeObject<PipeRequestMessage>(requestLine);
                        if (request == null)
                        {
                            response = PipeResponseMessage.FromBridgeResponse(
                                null,
                                AutomationBridgeResponse.Error(400, "INVALID_REQUEST", "Pipe 请求反序列化失败。"));
                        }
                        else
                        {
                            AutomationBridgeResponse bridgeResponse = service.Handle(
                                request.Method,
                                request.Path,
                                request.BodyJson);
                            response = PipeResponseMessage.FromBridgeResponse(request.RequestId, bridgeResponse);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    response = PipeResponseMessage.FromBridgeResponse(
                        null,
                        AutomationBridgeResponse.Error(400, "INVALID_REQUEST", "Pipe 请求 JSON 非法。", ex.Message));
                }
                catch (Exception ex)
                {
                    response = PipeResponseMessage.FromBridgeResponse(
                        null,
                        AutomationBridgeResponse.Error(500, "UNHANDLED_EXCEPTION", "Pipe 请求处理异常。", ex.Message));
                }

                try
                {
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(response)).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private static NamedPipeServerStream CreateServer()
        {
            return new NamedPipeServerStream(
                DefaultPipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        private void ReportInfo(string message)
        {
            try
            {
                owner.BeginInvoke((Action)(() => SF.frmInfo?.PrintInfo(message, FrmInfo.Level.Normal)));
            }
            catch
            {
            }
        }

        private void ReportError(string message)
        {
            try
            {
                owner.BeginInvoke((Action)(() => SF.frmInfo?.PrintInfo(message, FrmInfo.Level.Error)));
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal sealed class AutomationBridgeResponse
    {
        private AutomationBridgeResponse(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
        }

        public int StatusCode { get; }

        public string Body { get; }

        public static AutomationBridgeResponse Ok(string body)
        {
            return new AutomationBridgeResponse(200, body);
        }

        public static AutomationBridgeResponse Error(int statusCode, string code, string message, string details = null)
        {
            return new AutomationBridgeResponse(
                statusCode,
                AutomationBridgeService.BuildErrorBody(code, message, details));
        }
    }

    internal sealed class PipeRequestMessage
    {
        public string RequestId { get; set; }

        public string Method { get; set; }

        public string Path { get; set; }

        public string BodyJson { get; set; }
    }

    internal sealed class PipeResponseMessage
    {
        public string RequestId { get; set; }

        public int StatusCode { get; set; }

        public string BodyJson { get; set; }

        public static PipeResponseMessage FromBridgeResponse(string requestId, AutomationBridgeResponse response)
        {
            return new PipeResponseMessage
            {
                RequestId = requestId ?? string.Empty,
                StatusCode = response?.StatusCode ?? 500,
                BodyJson = response?.Body ?? string.Empty
            };
        }
    }
}
