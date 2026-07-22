using Newtonsoft.Json;
// 模块：运行时 / 诊断。
// 职责范围：记录并投影断点、性能、审计、异常、日志缓冲和运行黑匣子事实。

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Automation
{
    /// <summary>
    /// 结构化审计日志旁路。现有文本日志继续保留；这里提供稳定、逐行可解析的 JSONL 事实源。
    /// </summary>
    internal static class StructuredAuditLogger
    {
        private const long MaxFileBytes = 5L * 1024L * 1024L;
        private const int MaxPayloadBytes = 64 * 1024;
        private static readonly object SyncRoot = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string Root = Path.Combine(@"D:\AutomationLogs", "Structured");

        public static void Write(string streamName, JObject record)
        {
            if (record == null)
            {
                return;
            }

            try
            {
                JObject safeRecord = (JObject)record.DeepClone();
                safeRecord["schemaVersion"] = safeRecord["schemaVersion"] ?? 1;
                safeRecord["timeUtc"] = safeRecord["timeUtc"] ?? DateTime.UtcNow.ToString("O");
                safeRecord["eventId"] = safeRecord["eventId"] ?? Guid.NewGuid().ToString("N");
                SensitiveDataRedactor.Redact(safeRecord);
                LimitPayload(safeRecord);

                string line = safeRecord.ToString(Formatting.None) + Environment.NewLine;
                lock (SyncRoot)
                {
                    string stream = NormalizeStreamName(streamName);
                    string directory = Path.Combine(Root, stream);
                    Directory.CreateDirectory(directory);
                    string path = ResolvePath(directory, DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        Utf8NoBom.GetByteCount(line));
                    File.AppendAllText(path, line, Utf8NoBom);
                }
            }
            catch
            {
                // 审计旁路故障不得影响平台主流程；现有文本日志仍是兜底。
            }
        }

        private static string ResolvePath(string directory, string datePrefix, int incomingBytes)
        {
            int index = 0;
            while (true)
            {
                string suffix = index == 0 ? string.Empty : "_" + index.ToString("000");
                string path = Path.Combine(directory, datePrefix + suffix + ".jsonl");
                if (!File.Exists(path) || new FileInfo(path).Length + incomingBytes <= MaxFileBytes)
                {
                    return path;
                }
                index++;
            }
        }

        private static string NormalizeStreamName(string value)
        {
            string stream = string.IsNullOrWhiteSpace(value) ? "General" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                stream = stream.Replace(invalid, '_');
            }
            return stream;
        }

        private static void LimitPayload(JObject record)
        {
            JToken payload = record["payload"];
            if (payload == null)
            {
                return;
            }

            string payloadText = payload.ToString(Formatting.None);
            int payloadBytes = Utf8NoBom.GetByteCount(payloadText);
            record["payloadBytes"] = payloadBytes;
            if (payloadBytes <= MaxPayloadBytes)
            {
                record["payloadTruncated"] = false;
                return;
            }

            int charLimit = Math.Min(payloadText.Length, MaxPayloadBytes / 2);
            record["payload"] = payloadText.Substring(0, charLimit);
            record["payloadTruncated"] = true;
        }

    }
}
