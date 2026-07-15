using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Automation
{
    /// <summary>
    /// 面向 AI 链路复盘的紧凑事实流。底层完整报文仍由原审计日志保存，
    /// 此处只记录能够直接按会话和轮次还原行为的事件。
    /// </summary>
    internal static class AiAnalysisLogger
    {
        private const long MaxFileBytes = 5L * 1024L * 1024L;
        private const int QueueCapacity = 2048;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string Root = Path.Combine(@"D:\AutomationLogs", "AIExecution", "Analysis");
        private static readonly BlockingCollection<JObject> Queue =
            new BlockingCollection<JObject>(new ConcurrentQueue<JObject>(), QueueCapacity);
        private static readonly Thread WriterThread;
        private static long droppedEventCount;
        private static long writerErrorCount;

        static AiAnalysisLogger()
        {
            WriterThread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "AI分析日志写入"
            };
            WriterThread.Start();
            AppDomain.CurrentDomain.ProcessExit += (sender, args) => Shutdown();
        }

        public static void Write(JObject record)
        {
            if (record == null)
            {
                return;
            }

            try
            {
                record["v"] = 2;
                record["tsUtc"] = record["tsUtc"] ?? DateTime.UtcNow.ToString("O");
                record["eventId"] = record["eventId"] ?? Guid.NewGuid().ToString("N");
                long dropped = Interlocked.Exchange(ref droppedEventCount, 0L);
                long errors = Interlocked.Exchange(ref writerErrorCount, 0L);
                if (dropped > 0)
                {
                    record["droppedEventsBefore"] = dropped;
                }
                if (errors > 0)
                {
                    record["writerErrorsBefore"] = errors;
                }
                if (!Queue.TryAdd(record))
                {
                    Interlocked.Add(ref droppedEventCount, dropped + 1L);
                    Interlocked.Add(ref writerErrorCount, errors);
                }
            }
            catch
            {
                Interlocked.Increment(ref droppedEventCount);
            }
        }

        private static void WriteLoop()
        {
            foreach (JObject record in Queue.GetConsumingEnumerable())
            {
                try
                {
                    RedactSensitiveValues(record);
                    string line = record.ToString(Formatting.None) + Environment.NewLine;
                    Directory.CreateDirectory(Root);
                    string path = ResolvePath(DateTime.UtcNow.ToString("yyyy-MM-dd"), Utf8NoBom.GetByteCount(line));
                    File.AppendAllText(path, line, Utf8NoBom);
                }
                catch
                {
                    Interlocked.Increment(ref writerErrorCount);
                }
            }
        }

        private static void Shutdown()
        {
            try
            {
                if (!Queue.IsAddingCompleted)
                {
                    Queue.CompleteAdding();
                }
                if (!ReferenceEquals(Thread.CurrentThread, WriterThread))
                {
                    WriterThread.Join(TimeSpan.FromSeconds(2));
                }
            }
            catch
            {
            }
        }

        public static JObject SummarizePayload(JToken value, int maxBytes)
        {
            JToken safeValue = value?.DeepClone() ?? JValue.CreateNull();
            RedactSensitiveValues(safeValue);
            RemoveEmptyValues(safeValue);
            string text = safeValue.ToString(Formatting.None);
            int bytes = Utf8NoBom.GetByteCount(text);
            var summary = new JObject
            {
                ["bytes"] = bytes,
                ["sha256"] = ComputeSha256(text),
                ["truncated"] = bytes > maxBytes
            };

            JObject shape = BuildShapeSummary(safeValue);
            if (shape.Count > 0)
            {
                summary["shape"] = shape;
            }

            if (bytes <= maxBytes)
            {
                summary["value"] = safeValue;
            }
            else
            {
                summary["preview"] = TruncateUtf8(text, maxBytes);
            }
            return summary;
        }

        public static JObject FingerprintText(string text)
        {
            string value = text ?? string.Empty;
            return new JObject
            {
                ["bytes"] = Utf8NoBom.GetByteCount(value),
                ["sha256"] = ComputeSha256(value)
            };
        }

        public static string NormalizeToolName(string toolName)
        {
            const string prefix = "automation__";
            string value = (toolName ?? string.Empty).Trim();
            return value.StartsWith(prefix, StringComparison.Ordinal)
                ? value.Substring(prefix.Length)
                : value;
        }

        private static JObject BuildShapeSummary(JToken value)
        {
            var shape = new JObject();
            if (!(value is JObject obj))
            {
                if (value is JArray array)
                {
                    shape["itemCount"] = array.Count;
                }
                return shape;
            }

            string resultType = obj["type"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(resultType))
            {
                shape["type"] = resultType;
            }
            if (obj["ok"]?.Type == JTokenType.Boolean)
            {
                shape["ok"] = obj["ok"].Value<bool>();
            }
            string errorCode = obj["errorCode"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(errorCode))
            {
                shape["errorCode"] = errorCode;
            }

            JObject data = obj["data"] as JObject;
            string previewId = data?["previewId"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(previewId))
            {
                shape["previewId"] = previewId;
            }
            AddArrayCount(shape, "items", data?["items"] as JArray);
            AddArrayCount(shape, "changes", data?["changes"] as JArray ?? obj["changes"] as JArray);
            AddArrayCount(shape, "createdObjects", data?["createdObjects"] as JArray);
            AddArrayCount(shape, "affectedProcesses", data?["affectedProcesses"] as JArray);

            JArray actions = obj["changeSet"]?["actions"] as JArray
                ?? obj["actions"] as JArray;
            if (actions != null)
            {
                shape["actionCount"] = actions.Count;
                var actionTypes = new JObject();
                foreach (IGrouping<string, JToken> group in actions.GroupBy(
                    item => item?["type"]?.Value<string>() ?? "(unknown)",
                    StringComparer.Ordinal))
                {
                    actionTypes[group.Key] = group.Count();
                }
                shape["actionTypes"] = actionTypes;
            }
            return shape;
        }

        private static void AddArrayCount(JObject target, string name, JArray value)
        {
            if (value != null)
            {
                target[name + "Count"] = value.Count;
            }
        }

        private static void RemoveEmptyValues(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties().ToList())
                {
                    RemoveEmptyValues(property.Value);
                    if (property.Value.Type == JTokenType.Null
                        || (property.Value.Type == JTokenType.String
                            && string.IsNullOrWhiteSpace(property.Value.Value<string>()))
                        || (property.Value is JArray array && array.Count == 0)
                        || (property.Value is JObject child && child.Count == 0))
                    {
                        property.Remove();
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken item in array.ToList())
                {
                    RemoveEmptyValues(item);
                }
            }
        }

        private static string ComputeSha256(string text)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Utf8NoBom.GetBytes(text ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string TruncateUtf8(string text, int maxBytes)
        {
            const string marker = "…[truncated]";
            int budget = Math.Max(0, maxBytes - Utf8NoBom.GetByteCount(marker));
            byte[] source = Utf8NoBom.GetBytes(text ?? string.Empty);
            if (source.Length <= budget)
            {
                return text ?? string.Empty;
            }

            int count = budget;
            while (count > 0 && (source[count] & 0xC0) == 0x80)
            {
                count--;
            }
            return Utf8NoBom.GetString(source, 0, count) + marker;
        }

        private static string ResolvePath(string datePrefix, int incomingBytes)
        {
            int index = 0;
            while (true)
            {
                string path = Path.Combine(Root, datePrefix + "_" + index.ToString("000") + ".jsonl");
                if (!File.Exists(path) || new FileInfo(path).Length + incomingBytes <= MaxFileBytes)
                {
                    return path;
                }
                index++;
            }
        }

        private static void RedactSensitiveValues(JToken token)
        {
            if (!(token is JContainer container))
            {
                return;
            }

            foreach (JToken child in container.Children().ToList())
            {
                if (child is JProperty property && IsSensitiveName(property.Name))
                {
                    property.Value = "***";
                    continue;
                }
                RedactSensitiveValues(child);
            }
        }

        private static bool IsSensitiveName(string name)
        {
            return (name ?? string.Empty).IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                || (name ?? string.Empty).IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || (name ?? string.Empty).IndexOf("apiKey", StringComparison.OrdinalIgnoreCase) >= 0
                || (name ?? string.Empty).IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(name, "headers", StringComparison.OrdinalIgnoreCase);
        }
    }
}
