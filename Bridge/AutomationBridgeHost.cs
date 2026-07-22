using Newtonsoft.Json;
// 模块：Bridge / 宿主。
// 职责范围：管理 Named Pipe 监听、连接生命周期和请求分发。
// 排查入口：先区分管道连接、长度帧、JSON 反序列化和 Service handler 四个阶段；异常证据写入 Bridge 日志。

using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Automation.Bridge
{
    internal sealed class AutomationBridgeHost : IDisposable
    {
        private const int MaxRequestBytes = 1024 * 1024;
        private const int MaxResponseBytes = 8 * 1024 * 1024;
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

                    NamedPipeServerStream acceptedServer = server;
                    _ = Task.Run(() => ProcessClientAsync(acceptedServer));
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
            {
                PipeResponseMessage response;
                string requestPath = string.Empty;
                string requestMethod = string.Empty;
                string requestId = string.Empty;
                string requestBody = string.Empty;
                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    string requestText = await ReadMessageAsync(server).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(requestText))
                    {
                        response = PipeResponseMessage.FromBridgeResponse(
                            null,
                            AutomationBridgeResponse.Error(400, "EMPTY_REQUEST", "Pipe 请求为空。"));
                    }
                    else
                    {
                        PipeRequestMessage request = JsonConvert.DeserializeObject<PipeRequestMessage>(requestText);
                        if (request == null)
                        {
                            response = PipeResponseMessage.FromBridgeResponse(
                                null,
                                AutomationBridgeResponse.Error(400, "INVALID_REQUEST", "Pipe 请求反序列化失败。"));
                        }
                        else
                        {
                            requestId = request.RequestId ?? string.Empty;
                            requestMethod = request.Method ?? string.Empty;
                            requestPath = request.Path ?? string.Empty;
                            requestBody = request.BodyJson ?? string.Empty;
                            // 不向 FrmInfo 转发每请求的 INFO 日志，避免 AI 助手一跑就刷屏；异常仍由 ReportError 上报。
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
                    ReportError($"Automation Bridge 请求处理异常：{requestMethod} {requestPath}，{ex.Message}");
                }

                stopwatch.Stop();
                WriteStructuredRequestEvent(requestId, requestMethod, requestPath, requestBody, response,
                    stopwatch.ElapsedMilliseconds);

                try
                {
                    string responseText = JsonConvert.SerializeObject(response);
                    await WriteMessageAsync(server, responseText).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ReportError($"Automation Bridge 响应发送失败：{requestMethod} {requestPath}，{ex.Message}");
                }
            }
        }

        private static void WriteStructuredRequestEvent(string requestId, string method, string path,
            string requestBody, PipeResponseMessage response, long durationMs)
        {
            try
            {
                int statusCode = response?.StatusCode ?? 500;
                JObject responseObject = TryParseObject(response?.BodyJson);
                bool? ok = responseObject?["ok"]?.Value<bool?>();
                string errorCode = responseObject?["errorCode"]?.Value<string>()
                    ?? responseObject?["error"]?["code"]?.Value<string>()
                    ?? string.Empty;
                string previewId = responseObject?["data"]?["previewId"]?.Value<string>()
                    ?? responseObject?["previewId"]?.Value<string>()
                    ?? string.Empty;
                string transportStatus = statusCode >= 200 && statusCode < 500 ? "success" : "failed";
                string businessStatus = statusCode >= 400 || ok == false ? "failed" : "success";

                StructuredAuditLogger.Write("Bridge", new JObject
                {
                    ["source"] = "bridge",
                    ["eventName"] = businessStatus == "success" ? "bridge.request.completed" : "bridge.request.failed",
                    ["correlationId"] = requestId ?? string.Empty,
                    ["bridgeRequestId"] = requestId ?? string.Empty,
                    ["method"] = method ?? string.Empty,
                    ["path"] = path ?? string.Empty,
                    ["durationMs"] = durationMs,
                    ["statusCode"] = statusCode,
                    ["transportStatus"] = transportStatus,
                    ["businessStatus"] = businessStatus,
                    ["errorCode"] = errorCode,
                    ["previewId"] = previewId,
                    ["payload"] = new JObject
                    {
                        ["request"] = TryParseToken(requestBody),
                        ["response"] = responseObject ?? TryParseToken(response?.BodyJson)
                    }
                });
            }
            catch
            {
            }
        }

        private static JObject TryParseObject(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            try
            {
                return JObject.Parse(value);
            }
            catch
            {
                return null;
            }
        }

        private static JToken TryParseToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return JValue.CreateNull();
            }
            try
            {
                return JToken.Parse(value);
            }
            catch
            {
                return value;
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

        private static async Task<string> ReadMessageAsync(Stream stream)
        {
            byte[] lengthBuffer = await ReadExactlyAsync(stream, sizeof(int)).ConfigureAwait(false);
            int length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length < 0 || length > MaxRequestBytes)
            {
                throw new InvalidDataException($"Pipe 请求长度非法或超过 {MaxRequestBytes / 1024} KB 上限。");
            }
            if (length == 0)
            {
                return string.Empty;
            }

            byte[] payloadBuffer = await ReadExactlyAsync(stream, length).ConfigureAwait(false);
            return Encoding.UTF8.GetString(payloadBuffer);
        }

        private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Pipe 连接在读取报文时提前关闭。");
                }
                offset += read;
            }
            return buffer;
        }

        private static async Task WriteMessageAsync(Stream stream, string text)
        {
            byte[] payloadBuffer = Encoding.UTF8.GetBytes(text ?? string.Empty);
            if (payloadBuffer.Length > MaxResponseBytes)
            {
                throw new InvalidDataException($"Pipe 响应超过 {MaxResponseBytes / 1024 / 1024} MB 上限。");
            }
            byte[] lengthBuffer = BitConverter.GetBytes(payloadBuffer.Length);
            await stream.WriteAsync(lengthBuffer, 0, lengthBuffer.Length).ConfigureAwait(false);
            if (payloadBuffer.Length > 0)
            {
                await stream.WriteAsync(payloadBuffer, 0, payloadBuffer.Length).ConfigureAwait(false);
            }
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private void ReportInfo(string message)
        {
            try
            {
                owner.BeginInvoke((Action)(() => owner.frmInfo?.PrintInfo(message, FrmInfo.Level.Normal)));
            }
            catch
            {
            }
        }

        private void ReportError(string message)
        {
            try
            {
                owner.BeginInvoke((Action)(() => owner.frmInfo?.PrintInfo(message, FrmInfo.Level.Error)));
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
