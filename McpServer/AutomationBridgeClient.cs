using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                PropertyNameCaseInsensitive = true,
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

        public Task<string> ListIntentTemplatesAsync(string? patchAction)
        {
            return PostAsync("/bridge/intents/catalog", new
            {
                patchAction
            });
        }

        public Task<string> GetIntentTemplateAsync(string? templateId, string? patchAction)
        {
            return PostAsync("/bridge/intents/template", new
            {
                templateId,
                patchAction
            });
        }

        public Task<string> BuildPatchFromIntentAsync(string intentJson)
        {
            return SendAsync("POST", "/bridge/intents/build-patch", JsonSerializer.Serialize(new
            {
                intentJson
            }, jsonOptions));
        }

        public Task<string> PreviewIntentAsync(string intentJson)
        {
            return SendAsync("POST", "/bridge/intents/preview", JsonSerializer.Serialize(new
            {
                intentJson
            }, jsonOptions));
        }

        public Task<string> ApplyIntentAsync(string intentJson, string previewId)
        {
            return SendAsync("POST", "/bridge/intents/apply", JsonSerializer.Serialize(new
            {
                intentJson,
                previewId
            }, jsonOptions));
        }

        public Task<string> PreviewPatchAsync(string patchJson)
        {
            return SendAsync("POST", "/bridge/patch/preview", patchJson);
        }

        public Task<string> ApplyPatchAsync(string patchJson, string previewId)
        {
            JsonNode? node = JsonNode.Parse(patchJson);
            if (node is not JsonObject obj)
            {
                return Task.FromResult(BuildBridgeError("INVALID_ARGUMENT", "patchJson 必须是 JSON 对象。"));
            }
            obj["previewId"] = previewId;
            return SendAsync("POST", "/bridge/patch/apply", obj.ToJsonString(jsonOptions));
        }

        public Task<string> GetRuntimeSnapshotAsync(int? procIndex)
        {
            return PostAsync("/bridge/runtime/snapshot", new
            {
                procIndex
            });
        }

        public Task<string> GetInfoLogTailAsync(int maxCount)
        {
            return PostAsync("/bridge/info-log/tail", new
            {
                maxCount
            });
        }

        public Task<string> DiagnoseProcAsync(int procIndex)
        {
            return PostAsync("/bridge/procs/diagnose", new
            {
                procIndex
            });
        }

        public Task<string> GetOperationDetailAsync(int procIndex, int stepIndex, int opIndex)
        {
            return PostAsync("/bridge/operations/detail", new
            {
                procIndex,
                stepIndex,
                opIndex
            });
        }

        public Task<string> GetStepDetailAsync(int procIndex, int stepIndex)
        {
            return PostAsync("/bridge/steps/detail", new
            {
                procIndex,
                stepIndex
            });
        }

        public Task<string> SearchOperationsAsync(int? procIndex, string? operaType, string? keyword)
        {
            return PostAsync("/bridge/operations/search", new
            {
                procIndex,
                operaType,
                keyword
            });
        }

        public Task<string> ValidateProcAsync(int procIndex)
        {
            return PostAsync("/bridge/procs/validate", new
            {
                procIndex
            });
        }

        public Task<string> ManageProcPreviewAsync(string action, string payloadJson)
        {
            JsonNode? node = JsonNode.Parse(payloadJson);
            if (node is not JsonObject obj)
            {
                return Task.FromResult(BuildBridgeError("INVALID_ARGUMENT", "payloadJson 必须是 JSON 对象。"));
            }
            obj["action"] = action;
            return SendAsync("POST", "/bridge/procs/manage/preview", obj.ToJsonString(jsonOptions));
        }

        public Task<string> ManageProcApplyAsync(string action, string payloadJson, string previewId)
        {
            JsonNode? node = JsonNode.Parse(payloadJson);
            if (node is not JsonObject obj)
            {
                return Task.FromResult(BuildBridgeError("INVALID_ARGUMENT", "payloadJson 必须是 JSON 对象。"));
            }
            obj["action"] = action;
            obj["previewId"] = previewId;
            return SendAsync("POST", "/bridge/procs/manage/apply", obj.ToJsonString(jsonOptions));
        }

        public Task<string> ControlProcAsync(int procIndex, string action)
        {
            return PostAsync("/bridge/procs/control", new
            {
                procIndex,
                action
            });
        }

        // ===================== 资源查询与操作扩展 =====================

        public Task<string> ListVariablesAsync(string? type, string? nameLike, int? offset, int? limit)
        {
            return PostAsync("/bridge/variables/list", new
            {
                type,
                nameLike,
                offset,
                limit
            });
        }

        public Task<string> GetVariableAsync(string? name, int? index)
        {
            return PostAsync("/bridge/variables/get", new
            {
                name,
                index
            });
        }

        public Task<string> SearchVariablesAsync(string keyword, string? type, string? valueLike, int? limit)
        {
            return PostAsync("/bridge/variables/search", new
            {
                keyword,
                type,
                valueLike,
                limit
            });
        }

        public Task<string> SetVariableAsync(string? name, int? index, string value)
        {
            return PostAsync("/bridge/variables/set", new
            {
                name,
                index,
                value
            });
        }

        public Task<string> DeleteVariableAsync(int index)
        {
            return PostAsync("/bridge/variables/delete", new
            {
                index
            });
        }

        public Task<string> ListDataStructsAsync()
        {
            return PostAsync("/bridge/data-structs/list", new { });
        }

        public Task<string> GetDataStructAsync(string name)
        {
            return PostAsync("/bridge/data-structs/get", new
            {
                name
            });
        }

        public Task<string> SearchDataStructsAsync(string name, string? itemNameLike, string? strValueLike, double? numValueMin, double? numValueMax, int? limit)
        {
            return PostAsync("/bridge/data-structs/search", new
            {
                name,
                itemNameLike,
                strValueLike,
                numValueMin,
                numValueMax,
                limit
            });
        }

        public Task<string> SetDataStructFieldAsync(string name, int itemIndex, int fieldIndex, string value)
        {
            return PostAsync("/bridge/data-structs/set-field", new
            {
                name,
                itemIndex,
                fieldIndex,
                value
            });
        }

        public Task<string> ListIoAsync(string? type, string? nameLike, int? limit)
        {
            return PostAsync("/bridge/io/list", new
            {
                type,
                nameLike,
                limit
            });
        }

        public Task<string> GetIoAsync(string name)
        {
            return PostAsync("/bridge/io/get", new
            {
                name
            });
        }

        public Task<string> SearchIoAsync(string keyword, string? type, int? cardNum, int? limit)
        {
            return PostAsync("/bridge/io/search", new
            {
                keyword,
                type,
                cardNum,
                limit
            });
        }

        public Task<string> GetIoStateAsync(string name)
        {
            return PostAsync("/bridge/io/state", new
            {
                name
            });
        }

        public Task<string> ListAlarmsAsync(bool? includeEmpty, string? categoryLike, string? nameLike)
        {
            return PostAsync("/bridge/alarms/list", new
            {
                includeEmpty,
                categoryLike,
                nameLike
            });
        }

        public Task<string> ListPlcDevicesAsync(bool? includeMaps)
        {
            return PostAsync("/bridge/plc/devices", new
            {
                includeMaps
            });
        }

        public Task<string> ListCardsAsync(bool? includeAxes)
        {
            return PostAsync("/bridge/cards/list", new
            {
                includeAxes
            });
        }

        public Task<string> ListTrayPointsAsync(string? stationName, int? trayId)
        {
            return PostAsync("/bridge/tray-points/list", new
            {
                stationName,
                trayId
            });
        }

        public Task<string> ListCommunicationsAsync(bool? includeStatus)
        {
            return PostAsync("/bridge/communications/list", new
            {
                includeStatus
            });
        }

        private Task<string> PostAsync(string path, object payload)
        {
            string json = JsonSerializer.Serialize(payload, jsonOptions);
            return SendAsync("POST", path, json);
        }

        private async Task<string> SendAsync(string method, string path, string payloadJson)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(options.BridgeTimeoutMs));
            string stage = "connect";
            string requestId = Guid.NewGuid().ToString("N");
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    options.BridgePipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
                stage = "serialize_request";
                string request = JsonSerializer.Serialize(new PipeRequestMessage
                {
                    RequestId = requestId,
                    Method = method,
                    Path = path,
                    BodyJson = payloadJson ?? "{}"
                }, jsonOptions);

                stage = "write_request";
                await WriteMessageAsync(pipe, request, cts.Token).ConfigureAwait(false);

                stage = "read_response";
                string? responseText = await ReadMessageAsync(pipe, cts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(responseText))
                {
                    return BuildBridgeError("EMPTY_RESPONSE", $"Bridge 返回空响应：{path}", details: BuildStageDetails(stage, path, requestId));
                }

                stage = "deserialize_response";
                PipeResponseMessage? response = JsonSerializer.Deserialize<PipeResponseMessage>(responseText, jsonOptions);
                if (response == null)
                {
                    return BuildBridgeError("INVALID_RESPONSE", $"Bridge 响应反序列化失败：{path}", details: BuildStageDetails(stage, path, requestId));
                }

                if (!string.IsNullOrWhiteSpace(response.RequestId)
                    && !string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
                {
                    return BuildBridgeError("INVALID_RESPONSE", $"Bridge 响应 requestId 不匹配：{path}", details: BuildStageDetails(stage, path, requestId));
                }

                if (response.StatusCode >= 200 && response.StatusCode < 300)
                {
                    return string.IsNullOrWhiteSpace(response.BodyJson)
                        ? BuildBridgeError("EMPTY_RESPONSE", $"Bridge 返回空响应：{path}", details: BuildStageDetails(stage, path, requestId))
                        : response.BodyJson;
                }

                return BuildBridgeError(
                    "BRIDGE_ERROR",
                    $"Bridge 调用失败：{path}",
                    response.StatusCode,
                    $"stage={stage}; requestId={requestId}; bridgeResponse={response.BodyJson}");
            }
            catch (OperationCanceledException)
            {
                return BuildBridgeError(
                    "TIMEOUT",
                    $"Bridge 调用超时：{path}",
                    details: $"stage={stage}; requestId={requestId}; pipeName={options.BridgePipeName}; timeoutMs={options.BridgeTimeoutMs}");
            }
            catch (Exception ex)
            {
                return BuildBridgeError(
                    "REQUEST_ERROR",
                    $"Bridge 调用异常：{path}",
                    details: $"{BuildStageDetails(stage, path, requestId)}; error={ex.Message}");
            }
        }

        private static async Task WriteMessageAsync(Stream stream, string text, CancellationToken cancellationToken)
        {
            byte[] payloadBuffer = Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] lengthBuffer = BitConverter.GetBytes(payloadBuffer.Length);
            await stream.WriteAsync(lengthBuffer.AsMemory(0, lengthBuffer.Length), cancellationToken).ConfigureAwait(false);
            if (payloadBuffer.Length > 0)
            {
                await stream.WriteAsync(payloadBuffer.AsMemory(0, payloadBuffer.Length), cancellationToken).ConfigureAwait(false);
            }
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] lengthBuffer = await ReadExactlyAsync(stream, sizeof(int), cancellationToken).ConfigureAwait(false);
            int length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length < 0)
            {
                throw new InvalidDataException("Bridge 响应长度非法。");
            }
            if (length == 0)
            {
                return string.Empty;
            }

            byte[] payloadBuffer = await ReadExactlyAsync(stream, length, cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(payloadBuffer);
        }

        private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Bridge 连接在读取响应时提前关闭。");
                }
                offset += read;
            }
            return buffer;
        }

        private string BuildStageDetails(string stage, string path, string requestId)
        {
            return $"stage={stage}; requestId={requestId}; pipeName={options.BridgePipeName}; path={path}";
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
