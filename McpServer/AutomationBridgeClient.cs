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

        // ===================== 工具方法（40 个） =====================
        // 每个方法对应一个 Bridge 端点，参数即工具结构化参数，不再通过 action+params 路由。
        // op_meta 和 list_resources 仍为合并工具（通过 action 分发），其余为独立工具。

        // ---------- proc_query 拆分（7 个） ----------

        public Task<string> ListProcsAsync(bool? includeStepSummary = null)
        {
            JsonObject payload = new JsonObject();
            if (includeStepSummary.HasValue) payload["includeStepSummary"] = includeStepSummary.Value;
            return PostAsync("/bridge/proc/list", payload);
        }

        public Task<string> GetProcOverviewAsync(int procIndex)
        {
            JsonObject payload = new JsonObject { ["procIndex"] = procIndex };
            return PostAsync("/bridge/proc/overview", payload);
        }

        public Task<string> GetProcDetailAsync(int procIndex)
        {
            JsonObject payload = new JsonObject { ["procIndex"] = procIndex };
            return PostAsync("/bridge/proc/detail", payload);
        }

        public Task<string> GetOpDetailAsync(int procIndex, int stepIndex, int opIndex)
        {
            JsonObject payload = new JsonObject
            {
                ["procIndex"] = procIndex,
                ["stepIndex"] = stepIndex,
                ["opIndex"] = opIndex
            };
            return PostAsync("/bridge/proc/op_detail", payload);
        }

        public Task<string> GetStepDetailAsync(int procIndex, int stepIndex)
        {
            JsonObject payload = new JsonObject
            {
                ["procIndex"] = procIndex,
                ["stepIndex"] = stepIndex
            };
            return PostAsync("/bridge/proc/step_detail", payload);
        }

        public Task<string> SearchOpsAsync(int? procIndex, string? operaType, string? keyword)
        {
            JsonObject payload = new JsonObject();
            if (procIndex.HasValue) payload["procIndex"] = procIndex.Value;
            if (!string.IsNullOrEmpty(operaType)) payload["operaType"] = operaType;
            if (!string.IsNullOrEmpty(keyword)) payload["keyword"] = keyword;
            return PostAsync("/bridge/proc/search", payload);
        }

        public Task<string> GetSnapshotAsync(int? procIndex)
        {
            JsonObject payload = new JsonObject();
            if (procIndex.HasValue) payload["procIndex"] = procIndex.Value;
            return PostAsync("/bridge/proc/snapshot", payload);
        }

        // ---------- proc_diagnose 拆分（3 个） ----------

        public Task<string> GetInfoLogTailAsync(int? maxCount)
        {
            JsonObject payload = new JsonObject();
            if (maxCount.HasValue) payload["maxCount"] = maxCount.Value;
            return PostAsync("/bridge/proc/log_tail", payload);
        }

        public Task<string> DiagnoseProcAsync(int procIndex)
        {
            JsonObject payload = new JsonObject { ["procIndex"] = procIndex };
            return PostAsync("/bridge/proc/diagnose", payload);
        }

        public Task<string> ValidateProcAsync(int procIndex)
        {
            JsonObject payload = new JsonObject { ["procIndex"] = procIndex };
            return PostAsync("/bridge/proc/validate", payload);
        }

        // ---------- intent 拆分（3 个） ----------

        public Task<string> ListIntentTemplatesAsync(string? patchAction)
        {
            JsonObject payload = new JsonObject();
            if (!string.IsNullOrEmpty(patchAction)) payload["patchAction"] = patchAction;
            return PostAsync("/bridge/intent/list_templates", payload);
        }

        public Task<string> GetIntentTemplateAsync(string? templateId, string? patchAction)
        {
            JsonObject payload = new JsonObject();
            if (!string.IsNullOrEmpty(templateId)) payload["templateId"] = templateId;
            if (!string.IsNullOrEmpty(patchAction)) payload["patchAction"] = patchAction;
            return PostAsync("/bridge/intent/get_template", payload);
        }

        public Task<string> BuildPatchFromIntentAsync(string intentJson)
        {
            JsonObject payload = new JsonObject { ["intentJson"] = intentJson };
            return PostAsync("/bridge/intent/build_patch", payload);
        }

        // ---------- patch 拆分（4 个，patch_contract 由 MCP 层静态返回不走 Bridge） ----------

        public Task<string> PreviewIntentAsync(string intentJson)
        {
            JsonObject payload = new JsonObject { ["intentJson"] = intentJson };
            return PostAsync("/bridge/patch/preview_intent", payload);
        }

        public Task<string> ApplyIntentAsync(string intentJson, string previewId)
        {
            ValidatePreviewId(previewId);
            JsonObject payload = new JsonObject
            {
                ["intentJson"] = intentJson,
                ["previewId"] = previewId
            };
            return PostAsync("/bridge/patch/apply_intent", payload);
        }

        public Task<string> PreviewPatchAsync(string patchJson)
        {
            // patchJson 是完整 Patch JSON 字符串，解析后作为请求体直接发送
            //（ExecutePatch 期望 procIndex/baseProcId/actions 在顶层）。
            JsonNode? parsed = string.IsNullOrEmpty(patchJson) ? null : JsonNode.Parse(patchJson);
            JsonObject payload = parsed as JsonObject ?? new JsonObject();
            return PostAsync("/bridge/patch/preview_patch", payload);
        }

        public Task<string> ApplyPatchAsync(string patchJson, string previewId)
        {
            ValidatePreviewId(previewId);
            JsonNode? parsed = string.IsNullOrEmpty(patchJson) ? null : JsonNode.Parse(patchJson);
            JsonObject payload = parsed as JsonObject ?? new JsonObject();
            payload["previewId"] = previewId;
            return PostAsync("/bridge/patch/apply_patch", payload);
        }

        // ---------- proc_manage 拆分（4 个，previewId 为空预演，非空提交） ----------

        public Task<string> CreateProcAsync(string name, bool? autoStart, bool? disable, string? previewId)
        {
            JsonObject payload = new JsonObject { ["name"] = name };
            if (autoStart.HasValue) payload["autoStart"] = autoStart.Value;
            if (disable.HasValue) payload["disable"] = disable.Value;
            AddPreviewIdIfPresent(payload, previewId);
            return PostAsync("/bridge/proc/create", payload);
        }

        public Task<string> DeleteProcsAsync(int[] procIndexes, string? previewId)
        {
            JsonObject payload = new JsonObject();
            JsonArray arr = new JsonArray();
            foreach (int i in procIndexes) arr.Add(i);
            payload["procIndexes"] = arr;
            AddPreviewIdIfPresent(payload, previewId);
            return PostAsync("/bridge/proc/delete", payload);
        }

        public Task<string> ReorderProcAsync(int procIndex, int targetIndex, string? previewId)
        {
            JsonObject payload = new JsonObject
            {
                ["procIndex"] = procIndex,
                ["targetIndex"] = targetIndex
            };
            AddPreviewIdIfPresent(payload, previewId);
            return PostAsync("/bridge/proc/reorder", payload);
        }

        public Task<string> CopyProcAsync(int procIndex, string? newName, string? previewId)
        {
            JsonObject payload = new JsonObject { ["procIndex"] = procIndex };
            if (!string.IsNullOrEmpty(newName)) payload["newName"] = newName;
            AddPreviewIdIfPresent(payload, previewId);
            return PostAsync("/bridge/proc/copy", payload);
        }

        // ---------- control_proc 拆分（4 个） ----------

        public Task<string> StartProcAsync(int procIndex)
        {
            JsonObject payload = new JsonObject { ["procIndex"] = procIndex };
            return PostAsync("/bridge/proc/start", payload);
        }

        public Task<string> StopProcAsync(int procIndex)
        {
            JsonObject payload = new JsonObject { ["procIndex"] = procIndex };
            return PostAsync("/bridge/proc/stop", payload);
        }

        public Task<string> PauseProcAsync(int procIndex)
        {
            JsonObject payload = new JsonObject { ["procIndex"] = procIndex };
            return PostAsync("/bridge/proc/pause", payload);
        }

        public Task<string> ResumeProcAsync(int procIndex)
        {
            JsonObject payload = new JsonObject { ["procIndex"] = procIndex };
            return PostAsync("/bridge/proc/resume", payload);
        }

        // ---------- variable 拆分（5 个） ----------

        public Task<string> ListVariablesAsync(string? type, string? nameLike, int? offset, int? limit)
        {
            JsonObject payload = new JsonObject();
            if (!string.IsNullOrEmpty(type)) payload["type"] = type;
            if (!string.IsNullOrEmpty(nameLike)) payload["nameLike"] = nameLike;
            if (offset.HasValue) payload["offset"] = offset.Value;
            if (limit.HasValue) payload["limit"] = limit.Value;
            return PostAsync("/bridge/variable/list", payload);
        }

        public Task<string> GetVariableAsync(string? name, int? index)
        {
            JsonObject payload = new JsonObject();
            if (!string.IsNullOrEmpty(name)) payload["name"] = name;
            if (index.HasValue) payload["index"] = index.Value;
            return PostAsync("/bridge/variable/get", payload);
        }

        public Task<string> SearchVariablesAsync(string keyword, string? type, string? valueLike, int? limit)
        {
            JsonObject payload = new JsonObject { ["keyword"] = keyword };
            if (!string.IsNullOrEmpty(type)) payload["type"] = type;
            if (!string.IsNullOrEmpty(valueLike)) payload["valueLike"] = valueLike;
            if (limit.HasValue) payload["limit"] = limit.Value;
            return PostAsync("/bridge/variable/search", payload);
        }

        public Task<string> SetVariableAsync(string value, string? name, int? index)
        {
            JsonObject payload = new JsonObject { ["value"] = value };
            if (!string.IsNullOrEmpty(name)) payload["name"] = name;
            if (index.HasValue) payload["index"] = index.Value;
            return PostAsync("/bridge/variable/set", payload);
        }

        public Task<string> DeleteVariableAsync(int index)
        {
            JsonObject payload = new JsonObject { ["index"] = index };
            return PostAsync("/bridge/variable/delete", payload);
        }

        public Task<string> AddVariableAsync(string name, string type, string? value, string? note, int? index)
        {
            JsonObject payload = new JsonObject { ["name"] = name, ["type"] = type };
            if (value != null) payload["value"] = value;
            if (note != null) payload["note"] = note;
            if (index.HasValue) payload["index"] = index.Value;
            return PostAsync("/bridge/variable/add", payload);
        }

        // ---------- station 拆分（5 个） ----------

        public Task<string> ListStationsAsync()
        {
            JsonObject payload = new JsonObject();
            return PostAsync("/bridge/station/list", payload);
        }

        public Task<string> GetStationAsync(int stationIndex)
        {
            JsonObject payload = new JsonObject { ["stationIndex"] = stationIndex };
            return PostAsync("/bridge/station/get", payload);
        }

        public Task<string> AddStationAsync(string name, double? vel)
        {
            JsonObject payload = new JsonObject { ["name"] = name };
            if (vel.HasValue) payload["vel"] = vel.Value;
            return PostAsync("/bridge/station/add", payload);
        }

        public Task<string> DeleteStationAsync(int stationIndex)
        {
            JsonObject payload = new JsonObject { ["stationIndex"] = stationIndex };
            return PostAsync("/bridge/station/delete", payload);
        }

        public Task<string> UpdateStationAsync(int stationIndex, string? name, double? vel)
        {
            JsonObject payload = new JsonObject { ["stationIndex"] = stationIndex };
            if (name != null) payload["name"] = name;
            if (vel.HasValue) payload["vel"] = vel.Value;
            return PostAsync("/bridge/station/update", payload);
        }

        // ---------- point 拆分（3 个） ----------

        public Task<string> ListPointsAsync(int stationIndex)
        {
            JsonObject payload = new JsonObject { ["stationIndex"] = stationIndex };
            return PostAsync("/bridge/point/list", payload);
        }

        public Task<string> GetPointAsync(int stationIndex, int index)
        {
            JsonObject payload = new JsonObject
            {
                ["stationIndex"] = stationIndex,
                ["index"] = index
            };
            return PostAsync("/bridge/point/get", payload);
        }

        public Task<string> SetPointAsync(int stationIndex, int index, string? name, double? x, double? y, double? z, double? u, double? v, double? w)
        {
            JsonObject payload = new JsonObject
            {
                ["stationIndex"] = stationIndex,
                ["index"] = index
            };
            if (name != null) payload["name"] = name;
            if (x.HasValue) payload["x"] = x.Value;
            if (y.HasValue) payload["y"] = y.Value;
            if (z.HasValue) payload["z"] = z.Value;
            if (u.HasValue) payload["u"] = u.Value;
            if (v.HasValue) payload["v"] = v.Value;
            if (w.HasValue) payload["w"] = w.Value;
            return PostAsync("/bridge/point/set", payload);
        }

        public Task<string> DeletePointAsync(int stationIndex, int index)
        {
            JsonObject payload = new JsonObject
            {
                ["stationIndex"] = stationIndex,
                ["index"] = index
            };
            return PostAsync("/bridge/point/delete", payload);
        }

        // ---------- alarm 拆分（4 个） ----------

        public Task<string> ListAlarmsAsync(bool? includeEmpty, string? categoryLike, string? nameLike,
            int? offset, int? limit)
        {
            JsonObject payload = new JsonObject();
            if (includeEmpty.HasValue) payload["includeEmpty"] = includeEmpty.Value;
            if (!string.IsNullOrEmpty(categoryLike)) payload["categoryLike"] = categoryLike;
            if (!string.IsNullOrEmpty(nameLike)) payload["nameLike"] = nameLike;
            if (offset.HasValue) payload["offset"] = offset.Value;
            if (limit.HasValue) payload["limit"] = limit.Value;
            return PostAsync("/bridge/alarm/list", payload);
        }

        public Task<string> ListAlarmsAsync(bool? includeEmpty, string? categoryLike, string? nameLike)
        {
            return ListAlarmsAsync(includeEmpty, categoryLike, nameLike, null, null);
        }

        public Task<string> GetAlarmAsync(int index)
        {
            JsonObject payload = new JsonObject { ["index"] = index };
            return PostAsync("/bridge/alarm/get", payload);
        }

        public Task<string> SetAlarmAsync(int index, string name, string note, string? category, string? btn1,
            string? btn2, string? btn3, bool? allowOverwrite)
        {
            JsonObject payload = new JsonObject
            {
                ["index"] = index,
                ["name"] = name,
                ["note"] = note
            };
            if (category != null) payload["category"] = category;
            if (btn1 != null) payload["btn1"] = btn1;
            if (btn2 != null) payload["btn2"] = btn2;
            if (btn3 != null) payload["btn3"] = btn3;
            if (allowOverwrite.HasValue) payload["allowOverwrite"] = allowOverwrite.Value;
            return PostAsync("/bridge/alarm/set", payload);
        }

        public Task<string> SetAlarmAsync(int index, string name, string note, string? category,
            string? btn1, string? btn2, string? btn3)
        {
            return SetAlarmAsync(index, name, note, category, btn1, btn2, btn3, null);
        }

        public Task<string> DeleteAlarmAsync(int index)
        {
            JsonObject payload = new JsonObject { ["index"] = index };
            return PostAsync("/bridge/alarm/delete", payload);
        }

        // ---------- data_struct 拆分（4 个） ----------

        public Task<string> ListDataStructsAsync()
        {
            JsonObject payload = new JsonObject();
            return PostAsync("/bridge/data_struct/list", payload);
        }

        public Task<string> GetDataStructAsync(string name)
        {
            JsonObject payload = new JsonObject { ["name"] = name };
            return PostAsync("/bridge/data_struct/get", payload);
        }

        public Task<string> SearchDataStructsAsync(string name, string? itemNameLike, string? strValueLike, double? numValueMin, double? numValueMax, int? limit)
        {
            JsonObject payload = new JsonObject { ["name"] = name };
            if (!string.IsNullOrEmpty(itemNameLike)) payload["itemNameLike"] = itemNameLike;
            if (!string.IsNullOrEmpty(strValueLike)) payload["strValueLike"] = strValueLike;
            if (numValueMin.HasValue) payload["numValueMin"] = numValueMin.Value;
            if (numValueMax.HasValue) payload["numValueMax"] = numValueMax.Value;
            if (limit.HasValue) payload["limit"] = limit.Value;
            return PostAsync("/bridge/data_struct/search", payload);
        }

        public Task<string> SetDataStructFieldAsync(string name, int itemIndex, int fieldIndex, string value)
        {
            JsonObject payload = new JsonObject
            {
                ["name"] = name,
                ["itemIndex"] = itemIndex,
                ["fieldIndex"] = fieldIndex,
                ["value"] = value
            };
            return PostAsync("/bridge/data_struct/set_field", payload);
        }

        // ---------- io 拆分（4 个） ----------

        public Task<string> ListIoAsync(string? type, string? nameLike, int? limit)
        {
            JsonObject payload = new JsonObject();
            if (!string.IsNullOrEmpty(type)) payload["type"] = type;
            if (!string.IsNullOrEmpty(nameLike)) payload["nameLike"] = nameLike;
            if (limit.HasValue) payload["limit"] = limit.Value;
            return PostAsync("/bridge/io/list", payload);
        }

        public Task<string> GetIoAsync(string name)
        {
            JsonObject payload = new JsonObject { ["name"] = name };
            return PostAsync("/bridge/io/get", payload);
        }

        public Task<string> SearchIoAsync(string keyword, string? type, int? cardNum, int? limit)
        {
            JsonObject payload = new JsonObject { ["keyword"] = keyword };
            if (!string.IsNullOrEmpty(type)) payload["type"] = type;
            if (cardNum.HasValue) payload["cardNum"] = cardNum.Value;
            if (limit.HasValue) payload["limit"] = limit.Value;
            return PostAsync("/bridge/io/search", payload);
        }

        public Task<string> GetIoStateAsync(string name)
        {
            JsonObject payload = new JsonObject { ["name"] = name };
            return PostAsync("/bridge/io/state", payload);
        }

        // ---------- 合并工具（保留 2 个） ----------

        public Task<string> OpMetaAsync(string action, JsonNode? parameters = null)
        {
            JsonObject payload = new JsonObject { ["action"] = action };
            if (parameters != null) payload["params"] = parameters;
            return PostAsync("/bridge/op/meta", payload);
        }

        public Task<string> ListResourcesAsync(string action, JsonNode? parameters = null)
        {
            JsonObject payload = new JsonObject { ["action"] = action };
            if (parameters != null) payload["params"] = parameters;
            return PostAsync("/bridge/resources", payload);
        }

        private Task<string> PostAsync(string path, object payload)
        {
            string json = JsonSerializer.Serialize(payload, jsonOptions);
            return SendAsync("POST", path, json);
        }

        private static void AddPreviewIdIfPresent(JsonObject payload, string? previewId)
        {
            if (previewId == null || previewId.Length == 0)
            {
                return;
            }

            ValidatePreviewId(previewId);
            payload["previewId"] = previewId;
        }

        private static void ValidatePreviewId(string previewId)
        {
            if (string.IsNullOrWhiteSpace(previewId))
            {
                throw new ArgumentException("字段 previewId 不能为空；预演阶段请不要传 previewId，提交阶段必须传预演返回的 previewId。", nameof(previewId));
            }

            if (!Guid.TryParseExact(previewId, "N", out _))
            {
                throw new ArgumentException($"字段 previewId 不是合法预演编号；预演阶段请不要传 previewId，提交阶段必须传 preview 返回的 32 位 previewId。当前值：{previewId}", nameof(previewId));
            }
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

                if (!string.IsNullOrWhiteSpace(response.BodyJson))
                {
                    return response.BodyJson;
                }

                return BuildBridgeError(
                    "BRIDGE_ERROR",
                    $"Bridge 调用失败：{path}",
                    response.StatusCode,
                    BuildStageDetails(stage, path, requestId));
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
