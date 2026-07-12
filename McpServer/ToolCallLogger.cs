using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Automation.McpServer
{
    /// <summary>
    /// MCP 工具调用日志落盘，便于复盘用户问题、AI行为和工具结果。
    /// </summary>
    internal static class ToolCallLogger
    {
        private const long MaxLogFileBytes = 5L * 1024L * 1024L;
        private static readonly object SyncRoot = new object();
        private static readonly Mutex ExecutionLogMutex = new Mutex(false, "AutomationAIExecutionAuditLog");
        private static readonly JsonSerializerOptions PrettyJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
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
            WriteRecord(new
            {
                time = DateTime.Now.ToString("O"),
                source = "mcp",
                kind = "tool_call",
                callId,
                toolName = toolName ?? string.Empty,
                args = FormatJson(args)
            });
            return callId;
        }

        public static void Complete(string callId, string toolName, object? args, string result,
            string? error = null, long durationMs = 0)
        {
            WriteRecord(new
            {
                time = DateTime.Now.ToString("O"),
                source = "mcp",
                kind = string.IsNullOrWhiteSpace(error) ? "tool_completed" : "tool_failed",
                callId = callId ?? string.Empty,
                toolName = toolName ?? string.Empty,
                durationMs,
                args = FormatJson(args),
                result = string.IsNullOrWhiteSpace(error) ? FormatJson(result) : string.Empty,
                error = error ?? string.Empty
            });
        }

        public static void Log(string toolName, object? args, string result, string? error = null)
        {
            string callId = Begin(toolName, args);
            Complete(callId, toolName, args, result, error);
        }

        private static void WriteRecord(object record)
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
                    int index = 0;
                    string path;
                    while (true)
                    {
                        string suffix = index == 0 ? string.Empty : $"_{index:000}";
                        path = Path.Combine(root, datePrefix + suffix + ".log");
                        if (!File.Exists(path)
                            || new FileInfo(path).Length + Encoding.UTF8.GetByteCount(content) <= MaxLogFileBytes)
                        {
                            break;
                        }
                        index++;
                    }

                    File.AppendAllText(path, content, new UTF8Encoding(false));
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
            AppendJsonSection(builder, "参数", root, "args");
            AppendJsonSection(builder, "结果", root, "result");
            AppendField(builder, "异常", root, "error");
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

        private static string FormatJson(object? value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            try
            {
                string text = value is string stringValue
                    ? stringValue
                    : JsonSerializer.Serialize(value, PrettyJsonOptions);
                JsonNode? node = JsonNode.Parse(text);
                if (node == null)
                {
                    return text;
                }

                RedactSensitiveValues(node);
                return node.ToJsonString(PrettyJsonOptions);
            }
            catch
            {
                return value.ToString() ?? string.Empty;
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
    }
}
