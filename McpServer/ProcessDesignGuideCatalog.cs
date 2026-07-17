using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Automation.McpServer
{
    internal static class ProcessDesignGuideCatalog
    {
        private const string ResourceName = "Automation.McpServer.Guides.ProcessDesignGuide.md";

        public static readonly string[] SupportedTopics =
        {
            "architecture",
            "mechanical",
            "control-flow",
            "transaction",
            "recovery",
            "custom-function",
            "templates",
            "review"
        };

        public static string Get(string[] topics)
        {
            string[] normalized = (topics ?? Array.Empty<string>())
                .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
                .ToArray();
            if (normalized.Length == 0 || normalized.Any(string.IsNullOrEmpty))
            {
                return Error(
                    "PROCESS_DESIGN_TOPIC_REQUIRED",
                    "topics 至少包含一个流程设计主题。");
            }

            string[] duplicates = normalized
                .GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicates.Length > 0)
            {
                return Error(
                    "PROCESS_DESIGN_TOPIC_DUPLICATED",
                    "topics 包含重复主题：" + string.Join("、", duplicates) + "。");
            }

            string[] invalid = normalized
                .Where(value => !SupportedTopics.Contains(value, StringComparer.Ordinal))
                .ToArray();
            if (invalid.Length > 0)
            {
                return Error(
                    "PROCESS_DESIGN_TOPIC_INVALID",
                    "topics 包含不支持的主题：" + string.Join("、", invalid) + "。");
            }

            string source;
            using (Stream? stream = typeof(ProcessDesignGuideCatalog).Assembly
                .GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    return Error(
                        "PROCESS_DESIGN_GUIDE_UNAVAILABLE",
                        "流程设计指南内嵌资源不存在。");
                }
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                source = reader.ReadToEnd();
            }

            var sections = new List<object>();
            foreach (string topic in normalized)
            {
                if (!TryExtract(source, topic, out string markdown))
                {
                    return Error(
                        "PROCESS_DESIGN_SECTION_INVALID",
                        "流程设计指南缺少完整主题区块：" + topic + "。");
                }
                sections.Add(new { topic, markdown });
            }

            string sourceSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
            return JsonSerializer.Serialize(new
            {
                ok = true,
                type = "process.design_guide",
                source = "Automation AI 流程设计指南",
                sourceSha256,
                sections,
                authority = new
                {
                    fields = "当前语义或原生Schema",
                    runtimeBehavior = "当前行为契约和Guide",
                    resources = "当前资源工具返回",
                    readiness = "当前readiness和运行闸门"
                }
            });
        }

        private static bool TryExtract(string source, string topic, out string markdown)
        {
            string startMarker = "<!-- process-design:" + topic + ":start -->";
            string endMarker = "<!-- process-design:" + topic + ":end -->";
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
