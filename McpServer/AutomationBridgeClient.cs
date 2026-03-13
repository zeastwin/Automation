using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automation.McpServer
{
    internal sealed class AutomationBridgeClient : IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly AutomationMcpOptions options;
        private readonly JsonSerializerOptions jsonOptions;

        public AutomationBridgeClient(AutomationMcpOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
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
            return PostRawJsonAsync("/bridge/patch/preview", patchJson);
        }

        public Task<string> ApplyPatchAsync(string patchJson)
        {
            return PostRawJsonAsync("/bridge/patch/apply", patchJson);
        }

        private Task<string> PostAsync(string relativePath, object payload)
        {
            string json = JsonSerializer.Serialize(payload, jsonOptions);
            return PostRawJsonAsync(relativePath, json);
        }

        private async Task<string> PostRawJsonAsync(string relativePath, string payloadJson)
        {
            string url = options.BridgeBaseUrl + relativePath;
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payloadJson ?? "{}", Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(options.BridgeApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BridgeApiKey);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(options.BridgeTimeoutMs));
            try
            {
                using HttpResponseMessage response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token).ConfigureAwait(false);

                string responseText = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return string.IsNullOrWhiteSpace(responseText)
                        ? BuildBridgeError("EMPTY_RESPONSE", $"Bridge 返回空响应：{relativePath}")
                        : responseText;
                }

                return BuildBridgeError(
                    "HTTP_ERROR",
                    $"Bridge 调用失败：{relativePath}",
                    (int)response.StatusCode,
                    responseText);
            }
            catch (OperationCanceledException)
            {
                return BuildBridgeError(
                    "TIMEOUT",
                    $"Bridge 调用超时：{relativePath}",
                    details: $"timeoutMs={options.BridgeTimeoutMs}");
            }
            catch (Exception ex)
            {
                return BuildBridgeError(
                    "REQUEST_ERROR",
                    $"Bridge 调用异常：{relativePath}",
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
            httpClient.Dispose();
        }
    }
}
