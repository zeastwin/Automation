using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace Automation.Bridge
{
    /// <summary>
    /// 前台预演确认使用的最小 Bridge 客户端。
    /// </summary>
    internal sealed class AutomationBridgePreviewClient
    {
        public Task ConfirmAsync(string previewId)
        {
            return SendDecisionAsync("/bridge/previews/confirm", previewId);
        }

        public Task RejectAsync(string previewId)
        {
            return SendDecisionAsync("/bridge/previews/reject", previewId);
        }

        private static Task SendDecisionAsync(string path, string previewId)
        {
            if (string.IsNullOrWhiteSpace(previewId))
                throw new ArgumentException("预演标识为空。", nameof(previewId));
            return Task.Run(() =>
            {
                var envelope = new JObject
                {
                    ["requestId"] = Guid.NewGuid().ToString("N"),
                    ["method"] = "POST",
                    ["path"] = path,
                    ["bodyJson"] = new JObject { ["previewId"] = previewId }
                        .ToString(Formatting.None)
                };
                using (var pipe = new NamedPipeClientStream(
                    ".", AutomationBridgeHost.DefaultPipeName, PipeDirection.InOut))
                {
                    pipe.Connect(30000);
                    WriteMessage(pipe, envelope.ToString(Formatting.None));
                    JObject response = JObject.Parse(ReadMessage(pipe));
                    int statusCode = ReadInt(response, "statusCode", "StatusCode") ?? 500;
                    string bodyJson = ReadString(response, "bodyJson", "BodyJson") ?? string.Empty;
                    if (statusCode < 200 || statusCode >= 300)
                        throw new InvalidOperationException(bodyJson);
                }
            });
        }

        private static void WriteMessage(Stream stream, string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] length = BitConverter.GetBytes(payload.Length);
            stream.Write(length, 0, length.Length);
            if (payload.Length > 0) stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private static string ReadMessage(Stream stream)
        {
            int length = BitConverter.ToInt32(ReadExactly(stream, sizeof(int)), 0);
            if (length < 0) throw new InvalidDataException("Bridge 响应长度非法。");
            return length == 0 ? string.Empty : Encoding.UTF8.GetString(ReadExactly(stream, length));
        }

        private static int? ReadInt(JObject source, params string[] names)
        {
            JToken value = ReadToken(source, names);
            return value?.Type == JTokenType.Integer ? value.Value<int>() : (int?)null;
        }

        private static string ReadString(JObject source, params string[] names)
        {
            JToken value = ReadToken(source, names);
            return value?.Type == JTokenType.String ? value.Value<string>() : null;
        }

        private static JToken ReadToken(JObject source, IEnumerable<string> names)
        {
            if (source == null || names == null) return null;
            foreach (string name in names)
            {
                if (source.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken value))
                    return value;
            }
            return null;
        }

        private static byte[] ReadExactly(Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0) throw new EndOfStreamException("Bridge 连接提前关闭。");
                offset += read;
            }
            return buffer;
        }
    }
}
