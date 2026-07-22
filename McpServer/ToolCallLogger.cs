using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
// 模块：MCP / 工具调用审计。
// 职责范围：记录工具开始、结束、耗时与脱敏摘要，供 turnId/seq 串联 AI 执行链。
// 排查入口：优先看 D:\AutomationLogs\AIExecution\Analysis；完整报文仅用于底层取证。

namespace Automation.McpServer
{
    /// <summary>
    /// MCP 工具调用日志落盘，便于复盘用户问题、AI行为和工具结果。
    /// </summary>
    internal static class ToolCallLogger
    {
        private const long MaxLogFileBytes = 5L * 1024L * 1024L;
        private const int MaxPayloadBytes = 64 * 1024;
        private const int StructuredSchemaVersion = 1;
        private static readonly object SyncRoot = new object();
        private static readonly AsyncLocal<string?> CurrentCallId = new AsyncLocal<string?>();
        private static readonly Mutex ExecutionLogMutex = new Mutex(false, "AutomationAIExecutionAuditLog");
        private static readonly JsonSerializerOptions PrettyJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private static readonly JsonSerializerOptions CompactJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private static string logRoot = Path.Combine(@"D:\AutomationLogs", "AIExecution");

        public static void Configure(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return;
            }

            lock (SyncRoot)
            {
                logRoot = rootPath;
            }
        }

        public static string Begin(string toolName, object? args)
        {
            string callId = Guid.NewGuid().ToString("N");
            CurrentCallId.Value = callId;
            PayloadSnapshot argsSnapshot = CreatePayloadSnapshot(args);
            DateTime now = DateTime.UtcNow;
            WriteRecord(new
            {
                time = now.ToLocalTime().ToString("O"),
                source = "mcp",
                kind = "tool_call",
                callId,
                toolName = toolName ?? string.Empty,
                args = argsSnapshot.Text
            }, new
            {
                schemaVersion = StructuredSchemaVersion,
                timeUtc = now.ToString("O"),
                source = "mcp",
                eventName = "tool.started",
                eventId = Guid.NewGuid().ToString("N"),
                mcpCallId = callId,
                toolName = toolName ?? string.Empty,
                durationMs = 0L,
                transportStatus = "started",
                businessStatus = "pending",
                errorCode = string.Empty,
                payloadBytes = argsSnapshot.OriginalBytes,
                payloadTruncated = argsSnapshot.Truncated,
                args = argsSnapshot.Value
            });
            return callId;
        }

        public static void Complete(string callId, string toolName, object? args, string result,
            string? error = null, long durationMs = 0)
        {
            PayloadSnapshot argsSnapshot = CreatePayloadSnapshot(args);
            PayloadSnapshot resultSnapshot = CreatePayloadSnapshot(result);
            PayloadSnapshot errorSnapshot = CreatePayloadSnapshot(error);
            bool transportSucceeded = string.IsNullOrWhiteSpace(error);
            (bool BusinessFailed, string ErrorCode) business = InspectBusinessResult(result);
            string businessStatus = transportSucceeded
                ? (business.BusinessFailed ? "failed" : "success")
                : "failed";
            PayloadSnapshot completedPayload = transportSucceeded ? resultSnapshot : errorSnapshot;
            DateTime now = DateTime.UtcNow;
            WriteRecord(new
            {
                time = now.ToLocalTime().ToString("O"),
                source = "mcp",
                kind = string.IsNullOrWhiteSpace(error) ? "tool_completed" : "tool_failed",
                callId = callId ?? string.Empty,
                toolName = toolName ?? string.Empty,
                durationMs,
                args = argsSnapshot.Text,
                result = transportSucceeded ? resultSnapshot.Text : string.Empty,
                error = transportSucceeded ? string.Empty : errorSnapshot.Text
            }, new
            {
                schemaVersion = StructuredSchemaVersion,
                timeUtc = now.ToString("O"),
                source = "mcp",
                eventName = !transportSucceeded
                    ? "tool.failed"
                    : business.BusinessFailed ? "tool.business_failed" : "tool.completed",
                eventId = Guid.NewGuid().ToString("N"),
                mcpCallId = callId ?? string.Empty,
                toolName = toolName ?? string.Empty,
                durationMs,
                transportStatus = transportSucceeded ? "success" : "failed",
                businessStatus,
                errorCode = business.ErrorCode,
                payloadBytes = completedPayload.OriginalBytes,
                payloadSha256 = completedPayload.Sha256,
                payloadTruncated = completedPayload.Truncated,
                result = transportSucceeded ? resultSnapshot.Value : null,
                error = transportSucceeded ? null : errorSnapshot.Value
            });
            if (string.Equals(CurrentCallId.Value, callId, StringComparison.Ordinal))
            {
                CurrentCallId.Value = null;
            }
        }

        public static void LogBridgeDispatch(string bridgeRequestId, string method, string path)
        {
            string mcpCallId = CurrentCallId.Value ?? string.Empty;
            DateTime now = DateTime.UtcNow;
            WriteRecord(new
            {
                time = now.ToLocalTime().ToString("O"),
                source = "mcp",
                kind = "bridge_dispatch",
                callId = mcpCallId,
                toolName = path ?? string.Empty
            }, new
            {
                schemaVersion = StructuredSchemaVersion,
                timeUtc = now.ToString("O"),
                source = "mcp",
                eventName = "bridge.dispatched",
                eventId = Guid.NewGuid().ToString("N"),
                correlationId = string.IsNullOrWhiteSpace(mcpCallId) ? bridgeRequestId : mcpCallId,
                mcpCallId,
                bridgeRequestId = bridgeRequestId ?? string.Empty,
                method = method ?? string.Empty,
                path = path ?? string.Empty,
                transportStatus = "started",
                businessStatus = "pending"
            });
        }

        public static void Log(string toolName, object? args, string result, string? error = null)
        {
            string callId = Begin(toolName, args);
            Complete(callId, toolName, args, result, error);
        }

        public static void LogInvocationFailure(
            string toolName, object? args, Exception exception, long durationMs)
        {
            string stackTrace = exception?.ToString() ?? string.Empty;
            if (stackTrace.Contains(
                "Automation.McpServer.AutomationMcpTools.ExecuteAsync", StringComparison.Ordinal))
            {
                // 工具方法已经进入统一执行边界，异常已由 ExecuteAsync 记录。
                return;
            }
            PayloadSnapshot argsSnapshot = CreatePayloadSnapshot(args);
            PayloadSnapshot errorSnapshot = CreatePayloadSnapshot(exception?.Message);
            PayloadSnapshot stackSnapshot = CreatePayloadSnapshot(stackTrace);
            DateTime now = DateTime.UtcNow;
            string callId = Guid.NewGuid().ToString("N");
            WriteRecord(new
            {
                time = now.ToLocalTime().ToString("O"),
                source = "mcp",
                kind = "tool_invocation_failed",
                callId,
                toolName = toolName ?? string.Empty,
                durationMs,
                stage = "dispatch_or_parameter_binding",
                args = argsSnapshot.Text,
                exceptionType = exception?.GetType().FullName ?? string.Empty,
                error = errorSnapshot.Text,
                stackTrace = stackSnapshot.Text
            }, new
            {
                schemaVersion = StructuredSchemaVersion,
                timeUtc = now.ToString("O"),
                source = "mcp",
                eventName = "tool.invocation_failed",
                eventId = Guid.NewGuid().ToString("N"),
                mcpCallId = callId,
                toolName = toolName ?? string.Empty,
                durationMs,
                transportStatus = "failed",
                businessStatus = "failed",
                errorCode = "TOOL_INVOCATION_FAILED",
                payloadBytes = errorSnapshot.OriginalBytes,
                payloadSha256 = errorSnapshot.Sha256,
                payloadTruncated = errorSnapshot.Truncated,
                stage = "dispatch_or_parameter_binding",
                exceptionType = exception?.GetType().FullName ?? string.Empty,
                args = argsSnapshot.Value,
                error = errorSnapshot.Value,
                stackTrace = stackSnapshot.Value
            });
        }

        private static void WriteRecord(object record, object? structuredRecord = null)
        {
            try
            {
                string root;
                lock (SyncRoot)
                {
                    root = logRoot;
                }

                string content = FormatRecord(record);
                bool lockTaken = false;
                try
                {
                    lockTaken = ExecutionLogMutex.WaitOne(TimeSpan.FromSeconds(2));
                    if (!lockTaken)
                    {
                        return;
                    }

                    Directory.CreateDirectory(root);
                    string datePrefix = DateTime.Now.ToString("yyyy-MM-dd");
                    string path = FindRollingPath(root, datePrefix, ".log", content, false);
                    File.AppendAllText(path, content, new UTF8Encoding(false));

                    if (ShouldWriteStructured(structuredRecord))
                    {
                        string structuredContent = JsonSerializer.Serialize(
                            structuredRecord, CompactJsonOptions) + Environment.NewLine;
                        string structuredRoot = Path.Combine(root, "structured");
                        Directory.CreateDirectory(structuredRoot);
                        string structuredPath = FindRollingPath(
                            structuredRoot, datePrefix, ".jsonl", structuredContent, true);
                        File.AppendAllText(structuredPath, structuredContent, new UTF8Encoding(false));
                    }
                }
                catch (AbandonedMutexException)
                {
                    // 互斥体所属进程异常退出时，日志失败不应影响工具调用。
                }
                finally
                {
                    if (lockTaken)
                    {
                        ExecutionLogMutex.ReleaseMutex();
                    }
                }
            }
            catch
            {
                // 日志失败不影响主流程。
            }
        }

        private static string FindRollingPath(string root, string datePrefix, string extension,
            string content, bool alwaysUseIndex)
        {
            int contentBytes = Encoding.UTF8.GetByteCount(content);
            int index = 0;
            while (true)
            {
                string suffix = !alwaysUseIndex && index == 0 ? string.Empty : $"_{index:000}";
                string path = Path.Combine(root, datePrefix + suffix + extension);
                if (!File.Exists(path)
                    || new FileInfo(path).Length + contentBytes <= MaxLogFileBytes)
                {
                    return path;
                }
                index++;
            }
        }

        private static bool ShouldWriteStructured(object? record)
        {
            if (record == null)
            {
                return false;
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(record, CompactJsonOptions));
                if (!document.RootElement.TryGetProperty("eventName", out JsonElement eventName)
                    || eventName.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
                string value = eventName.GetString() ?? string.Empty;
                return value.EndsWith("failed", StringComparison.Ordinal)
                    || string.Equals(value, "tool.business_failed", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string FormatRecord(object record)
        {
            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(record, PrettyJsonOptions));
            JsonElement root = document.RootElement;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(new string('=', 100));
            AppendField(builder, "时间", root, "time");
            AppendField(builder, "来源", root, "source");
            AppendField(builder, "类型", root, "kind");
            AppendField(builder, "调用 ID", root, "callId");
            AppendField(builder, "工具", root, "toolName");
            AppendField(builder, "耗时", root, "durationMs", "毫秒");
            AppendField(builder, "失败阶段", root, "stage");
            AppendField(builder, "异常类型", root, "exceptionType");
            AppendJsonSection(builder, "参数", root, "args");
            AppendJsonSection(builder, "结果", root, "result");
            AppendField(builder, "异常", root, "error");
            AppendField(builder, "异常堆栈", root, "stackTrace");
            builder.AppendLine();
            return builder.ToString();
        }

        private static void AppendField(StringBuilder builder, string label, JsonElement root,
            string propertyName, string suffix = "")
        {
            if (!root.TryGetProperty(propertyName, out JsonElement value)
                || value.ValueKind == JsonValueKind.Null
                || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
            {
                return;
            }

            string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
            builder.Append(label).Append('：').Append(text).AppendLine(suffix);
        }

        private static void AppendJsonSection(StringBuilder builder, string label, JsonElement root,
            string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement value)
                || value.ValueKind == JsonValueKind.Null
                || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
            {
                return;
            }

            builder.AppendLine(label + "：");
            if (value.ValueKind == JsonValueKind.String)
            {
                builder.AppendLine(value.GetString() ?? string.Empty);
            }
            else
            {
                builder.AppendLine(JsonSerializer.Serialize(value, PrettyJsonOptions));
            }
        }

        private static PayloadSnapshot CreatePayloadSnapshot(object? value)
        {
            if (value == null)
            {
                return new PayloadSnapshot(string.Empty, null, 0, false, ComputeSha256(string.Empty));
            }

            string text;
            JsonNode? structuredValue = null;
            try
            {
                text = value is string stringValue
                    ? stringValue
                    : JsonSerializer.Serialize(value, PrettyJsonOptions);
                JsonNode? node = JsonNode.Parse(text);
                if (node != null)
                {
                    RedactSensitiveValues(node);
                    text = node.ToJsonString(PrettyJsonOptions);
                    structuredValue = node;
                }
            }
            catch
            {
                text = value.ToString() ?? string.Empty;
            }

            int originalBytes = Encoding.UTF8.GetByteCount(text);
            if (originalBytes <= MaxPayloadBytes)
            {
                return new PayloadSnapshot(text, structuredValue ?? text, originalBytes, false, ComputeSha256(text));
            }

            string truncatedText = TruncateUtf8(text, MaxPayloadBytes);
            return new PayloadSnapshot(truncatedText, truncatedText, originalBytes, true, ComputeSha256(text));
        }

        private static string ComputeSha256(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string TruncateUtf8(string text, int maxBytes)
        {
            const string marker = "\n...[truncated]";
            int markerBytes = Encoding.UTF8.GetByteCount(marker);
            int textBudget = Math.Max(0, maxBytes - markerBytes);
            byte[] buffer = new byte[textBudget];
            Encoder encoder = Encoding.UTF8.GetEncoder();
            encoder.Convert(text.AsSpan(), buffer.AsSpan(), false,
                out _, out int bytesUsed, out _);
            return Encoding.UTF8.GetString(buffer, 0, bytesUsed) + marker;
        }

        private static (bool BusinessFailed, string ErrorCode) InspectBusinessResult(string result)
        {
            if (string.IsNullOrWhiteSpace(result))
            {
                return (false, string.Empty);
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(result);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("ok", out JsonElement ok)
                    || ok.ValueKind != JsonValueKind.False)
                {
                    return (false, string.Empty);
                }

                string errorCode = root.TryGetProperty("errorCode", out JsonElement code)
                    && code.ValueKind == JsonValueKind.String
                    ? code.GetString() ?? string.Empty
                    : string.Empty;
                return (true, errorCode);
            }
            catch
            {
                // 非 JSON 结果仍属于有效工具结果，不能据此判定业务失败。
                return (false, string.Empty);
            }
        }

        private static void RedactSensitiveValues(JsonNode node)
        {
            if (node is JsonObject jsonObject)
            {
                foreach (KeyValuePair<string, JsonNode?> property in jsonObject.ToList())
                {
                    string name = property.Key ?? string.Empty;
                    if (name.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("apiKey", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(name, "headers", StringComparison.OrdinalIgnoreCase))
                    {
                        jsonObject[name] = "***";
                    }
                    else if (property.Value != null)
                    {
                        RedactSensitiveValues(property.Value);
                    }
                }
            }
            else if (node is JsonArray jsonArray)
            {
                foreach (JsonNode? item in jsonArray)
                {
                    if (item != null)
                    {
                        RedactSensitiveValues(item);
                    }
                }
            }
        }

        private sealed class PayloadSnapshot
        {
            public PayloadSnapshot(string text, object? value, int originalBytes, bool truncated, string sha256)
            {
                Text = text;
                Value = value;
                OriginalBytes = originalBytes;
                Truncated = truncated;
                Sha256 = sha256;
            }

            public string Text { get; }

            public object? Value { get; }

            public int OriginalBytes { get; }

            public bool Truncated { get; }

            public string Sha256 { get; }
        }
    }
}
