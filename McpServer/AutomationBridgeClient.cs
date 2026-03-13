using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automation.McpServer
{
    internal sealed class AutomationBridgeClient : IDisposable
    {
        private readonly AutomationMcpOptions options;
        private readonly JsonSerializerOptions jsonOptions;

        public AutomationBridgeClient(AutomationMcpOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };
        }

        public Task<string> ListProcsAsync(bool includeStepSummary)
        {
            return PostAsync("/bridge/procs/list", new
            {
                includeStepSummary
            });
        }

        public Task<string> GetProcOverviewAsync(int procIndex)
        {
            return PostAsync("/bridge/procs/overview", new
            {
                procIndex
            });
        }

        public Task<string> GetProcDetailAsync(int procIndex)
        {
            return PostAsync("/bridge/procs/detail", new
            {
                procIndex
            });
        }

        public Task<string> ListOperationTypesAsync()
        {
            return PostAsync("/bridge/operations/types", new { });
        }

        public Task<string> GetOperationSchemaAsync(int? procIndex, string? stepId, string? opId, string? operaType)
        {
            return PostAsync("/bridge/operations/schema", new
            {
                procIndex,
                stepId,
                opId,
                operaType
            });
        }

        public Task<string> GetReferenceCatalogAsync(int? procIndex)
        {
            return PostAsync("/bridge/references/catalog", new
            {
                procIndex
            });
        }

        public Task<string> PreviewPatchAsync(string patchJson)
        {
            return SendAsync("POST", "/bridge/patch/preview", patchJson);
        }

        public Task<string> ApplyPatchAsync(string patchJson)
        {
            return SendAsync("POST", "/bridge/patch/apply", patchJson);
        }

        private Task<string> PostAsync(string path, object payload)
        {
            string json = JsonSerializer.Serialize(payload, jsonOptions);
            return SendAsync("POST", path, json);
        }

        private async Task<string> SendAsync(string method, string path, string payloadJson)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(options.BridgeTimeoutMs));
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    options.BridgePipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true)
                {
                    AutoFlush = true
                };
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);

                string requestId = Guid.NewGuid().ToString("N");
                string request = JsonSerializer.Serialize(new PipeRequestMessage
                {
                    RequestId = requestId,
                    Method = method,
                    Path = path,
                    BodyJson = payloadJson ?? "{}"
                }, jsonOptions);

                await writer.WriteLineAsync(request).ConfigureAwait(false);
                string? responseLine = await reader.ReadLineAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    return BuildBridgeError("EMPTY_RESPONSE", $"Bridge 返回空响应：{path}");
                }

                PipeResponseMessage? response = JsonSerializer.Deserialize<PipeResponseMessage>(responseLine, jsonOptions);
                if (response == null)
                {
                    return BuildBridgeError("INVALID_RESPONSE", $"Bridge 响应反序列化失败：{path}");
                }

                if (!string.IsNullOrWhiteSpace(response.RequestId)
                    && !string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
                {
                    return BuildBridgeError("INVALID_RESPONSE", $"Bridge 响应 requestId 不匹配：{path}");
                }

                if (response.StatusCode >= 200 && response.StatusCode < 300)
                {
                    return string.IsNullOrWhiteSpace(response.BodyJson)
                        ? BuildBridgeError("EMPTY_RESPONSE", $"Bridge 返回空响应：{path}")
                        : response.BodyJson;
                }

                return BuildBridgeError(
                    "BRIDGE_ERROR",
                    $"Bridge 调用失败：{path}",
                    response.StatusCode,
                    response.BodyJson);
            }
            catch (OperationCanceledException)
            {
                return BuildBridgeError(
                    "TIMEOUT",
                    $"Bridge 调用超时：{path}",
                    details: $"timeoutMs={options.BridgeTimeoutMs}");
            }
            catch (Exception ex)
            {
                return BuildBridgeError(
                    "REQUEST_ERROR",
                    $"Bridge 调用异常：{path}",
                    details: ex.Message);
            }
        }

        private string BuildBridgeError(string code, string message, int? statusCode = null, string? details = null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                type = "bridge.error",
                errorCode = code,
                message,
                statusCode,
                details
            }, jsonOptions);
        }

        public void Dispose()
        {
        }

        private sealed class PipeRequestMessage
        {
            public string RequestId { get; set; } = string.Empty;

            public string Method { get; set; } = string.Empty;

            public string Path { get; set; } = string.Empty;

            public string BodyJson { get; set; } = string.Empty;
        }

        private sealed class PipeResponseMessage
        {
            public string RequestId { get; set; } = string.Empty;

            public int StatusCode { get; set; }

            public string BodyJson { get; set; } = string.Empty;
        }
    }
}
