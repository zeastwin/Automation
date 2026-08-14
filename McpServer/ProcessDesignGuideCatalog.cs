using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
// 模块：MCP / 流程设计 Guide。
// 职责范围：按主题读取内嵌流程设计知识；字段 Schema 和运行行为仍由各自权威代码生成。
// 排查入口：Guide 缺失检查 EmbeddedResource 名称和哈希，字段不符应查 Protocol/行为 Catalog。

namespace Automation.McpServer
{
    internal static class ProcessDesignGuideCatalog
    {
        private const string ResourceName = "Automation.McpServer.Guides.ProcessDesignGuide.md";

        public static readonly string[] SupportedTopics =
        {
            "core",
            "lifecycle",
            "orchestration",
            "interlock",
            "actuator",
            "motion",
            "pick-place",
            "transfer",
            "identify",
            "transaction",
            "monitoring",
            "recovery",
            "custom-function",
            "review"
        };

        public static string Get(string[] topics, string? detail = null)
        {
            string[] normalized = (topics ?? Array.Empty<string>())
                .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
                .ToArray();
            string normalizedDetail = string.IsNullOrWhiteSpace(detail)
                ? "compact"
                : detail.Trim().ToLowerInvariant();
            if (!string.Equals(normalizedDetail, "compact", StringComparison.Ordinal)
                && !string.Equals(normalizedDetail, "full", StringComparison.Ordinal))
            {
                return Error(
                    "PROCESS_DESIGN_DETAIL_INVALID",
                    "detail 只能是 compact 或 full。");
            }
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

            string[] selectedTopics = normalized.Contains("core", StringComparer.Ordinal)
                ? normalized
                : new[] { "core" }.Concat(normalized).ToArray();
            var sections = new List<object>();
            foreach (string topic in selectedTopics)
            {
                string sourceTopic = string.Equals(normalizedDetail, "compact", StringComparison.Ordinal)
                    && string.Equals(topic, "core", StringComparison.Ordinal)
                        ? "core-compact"
                        : topic;
                if (!TryExtract(source, sourceTopic, out string markdown))
                {
                    return Error(
                        "PROCESS_DESIGN_SECTION_INVALID",
                        "流程设计指南缺少完整主题区块：" + topic + "。");
                }
                sections.Add(new
                {
                    topic,
                    format = string.Equals(sourceTopic, "core-compact", StringComparison.Ordinal)
                        ? "compact" : "full",
                    markdown
                });
            }

            ProcessKnowledgeSelection knowledge;
            try
            {
                knowledge = ProcessKnowledgeCatalog.Get(normalized);
            }
            catch (InvalidDataException ex)
            {
                return Error(
                    "PROCESS_KNOWLEDGE_INVALID",
                    ex.Message);
            }

            string sourceSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
            IEnumerable<object> knowledgeBlocks = string.Equals(
                normalizedDetail, "full", StringComparison.Ordinal)
                ? knowledge.Blocks.Select(block => (object)new
                {
                    patternId = block.PatternId,
                    title = block.Title,
                    summary = block.Summary,
                    topics = block.Topics,
                    capabilities = block.Capabilities,
                    deviceTypes = block.DeviceTypes,
                    processTypes = block.ProcessTypes,
                    riskTags = block.RiskTags,
                    markdown = block.Markdown,
                    contentSha256 = block.ContentSha256
                })
                : knowledge.Blocks.Select(block => (object)new
                {
                    patternId = block.PatternId,
                    title = block.Title,
                    summary = block.Summary,
                    observableGoal = ExtractKnowledgeSection(block.Markdown, "可观察目标"),
                    recommendedStages = ExtractKnowledgeSection(block.Markdown, "参考阶段"),
                    completionEvidence = ExtractKnowledgeSection(block.Markdown, "完成证据"),
                    failureAndRecovery = ExtractKnowledgeSection(block.Markdown, "失败、超时与恢复"),
                    riskTags = block.RiskTags,
                    contentSha256 = block.ContentSha256
                });
            return JsonSerializer.Serialize(new
            {
                ok = true,
                type = "process.design_guide",
                detail = normalizedDetail,
                source = "Automation AI 流程设计知识",
                sourceSha256,
                requestedTopics = normalized,
                includedCore = true,
                sections,
                usableKnowledgeCatalogSha256 = knowledge.CatalogSha256,
                knowledgeBlocks,
                authority = new
                {
                    fields = "当前语义或原生Schema",
                    runtimeBehavior = "当前行为契约和Guide",
                    resources = "当前资源工具返回",
                    readiness = "当前readiness和运行闸门",
                    referencePolicy = "只返回已完成甄别的可用规范；旧项目证据和审核中间结果不进入运行时上下文"
                }
            });
        }

        private static string ExtractKnowledgeSection(string markdown, string heading)
        {
            if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(heading))
                return string.Empty;
            string marker = "## " + heading;
            int start = markdown.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += marker.Length;
            int end = markdown.IndexOf("\n## ", start, StringComparison.Ordinal);
            if (end < 0) end = markdown.Length;
            return markdown.Substring(start, end - start).Trim();
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
                allowedDetails = new[] { "compact", "full" },
                allowedTopics = SupportedTopics
            });
        }
    }
}
