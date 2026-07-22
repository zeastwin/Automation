using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Automation.McpServer
{
    internal static class ProcessDesignFaqCatalog
    {
        private const string ResourceName = "Automation.McpServer.Guides.ProcessDesignFaq.md";

        public static readonly string[] SupportedTopics =
        {
            "loop"
        };

        public static string Get(string[]? topics = null)
        {
            string[] normalized = (topics ?? Array.Empty<string>())
                .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
                .ToArray();

            string source;
            using (Stream? stream = typeof(ProcessDesignFaqCatalog).Assembly
                .GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    return Error(
                        "PROCESS_DESIGN_FAQ_UNAVAILABLE",
                        "流程设计 FAQ 内嵌资源不存在。");
                }
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                source = reader.ReadToEnd();
            }

            if (normalized.Length == 0)
            {
                return Ok(source, Array.Empty<object>());
            }

            if (normalized.Any(string.IsNullOrEmpty))
            {
                return Error(
                    "PROCESS_DESIGN_FAQ_TOPIC_REQUIRED",
                    "topics 不能为空字符串。");
            }

            string[] duplicates = normalized
                .GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicates.Length > 0)
            {
                return Error(
                    "PROCESS_DESIGN_FAQ_TOPIC_DUPLICATED",
                    "topics 包含重复主题：" + string.Join("、", duplicates) + "。");
            }

            string[] invalid = normalized
                .Where(value => !SupportedTopics.Contains(value, StringComparer.Ordinal))
                .ToArray();
            if (invalid.Length > 0)
            {
                return Error(
                    "PROCESS_DESIGN_FAQ_TOPIC_INVALID",
                    "topics 包含不支持的主题：" + string.Join("、", invalid) + "。");
            }

            var sections = new List<object>();
            foreach (string topic in normalized)
            {
                if (!TryExtract(source, topic, out string markdown))
                {
                    return Error(
                        "PROCESS_DESIGN_FAQ_SECTION_INVALID",
                        "流程设计 FAQ 缺少完整主题区块：" + topic + "。");
                }
                sections.Add(new { topic, markdown });
            }

            return Ok(source, sections);
        }

        private static string Ok(string source, object sections)
        {
            string sourceSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
            return JsonSerializer.Serialize(new
            {
                ok = true,
                type = "process.design_faq",
                source = "Automation AI 流程设计 FAQ",
                sourceSha256,
                sections
            });
        }

        private static bool TryExtract(string source, string topic, out string markdown)
        {
            string startMarker = "<!-- faq:" + topic + ":start -->";
            string endMarker = "<!-- faq:" + topic + ":end -->";
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                markdown = string.Empty;
                return false;
            }
            start += startMarker.Length;
            int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            if (end < 0)
            {
                markdown = string.Empty;
                return false;
            }
            markdown = source.Substring(start, end - start).Trim();
            return markdown.Length > 0;
        }

        private static string Error(string errorCode, string message)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                type = "mcp.error",
                errorCode,
                message,
                allowedTopics = SupportedTopics
            });
        }
    }
}
