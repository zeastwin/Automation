using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// 模块：MCP / 可用流程规范库。
// 职责范围：只读取 catalog.json 已登记的可用规范；来源证据和审核中间结果不进入运行时返回。

namespace Automation.McpServer
{
    internal static class ProcessKnowledgeCatalog
    {
        private const string CatalogResourceName =
            "Automation.McpServer.ProcessKnowledge.catalog.json";
        private const string BlockResourcePrefix =
            "Automation.McpServer.ProcessKnowledge.Blocks.";

        public static ProcessKnowledgeSelection Get(IEnumerable<string> requestedTopics)
        {
            string catalogSource = ReadResource(CatalogResourceName);
            ProcessKnowledgeDocument? catalog = JsonSerializer.Deserialize<ProcessKnowledgeDocument>(
                catalogSource,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (catalog == null
                || !string.Equals(
                    catalog.DocumentType,
                    "Automation.UsableProcessKnowledgeCatalog",
                    StringComparison.Ordinal)
                || catalog.SchemaVersion != 1)
            {
                throw new InvalidDataException("可用流程规范目录类型或版本无效。");
            }

            var topics = requestedTopics.ToHashSet(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selected = new List<ProcessKnowledgeBlock>();
            foreach (ProcessKnowledgeEntry entry in catalog.Blocks)
            {
                ValidateEntry(entry, ids, files);
                if (!entry.Topics.Any(topics.Contains))
                {
                    continue;
                }

                string markdown = ReadResource(BlockResourcePrefix + entry.File);
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    throw new InvalidDataException("可用流程规范正文为空：" + entry.PatternId);
                }
                selected.Add(new ProcessKnowledgeBlock(
                    entry.PatternId,
                    entry.Title,
                    entry.Summary,
                    entry.Topics,
                    entry.Capabilities,
                    entry.DeviceTypes,
                    entry.ProcessTypes,
                    entry.RiskTags,
                    markdown.Trim(),
                    Hash(markdown)));
            }

            return new ProcessKnowledgeSelection(Hash(catalogSource), selected);
        }

        private static void ValidateEntry(
            ProcessKnowledgeEntry entry,
            HashSet<string> ids,
            HashSet<string> files)
        {
            if (string.IsNullOrWhiteSpace(entry.PatternId)
                || string.IsNullOrWhiteSpace(entry.Title)
                || string.IsNullOrWhiteSpace(entry.Summary)
                || string.IsNullOrWhiteSpace(entry.File)
                || entry.Topics.Length == 0
                || entry.Capabilities.Length == 0
                || !ids.Add(entry.PatternId)
                || !files.Add(entry.File))
            {
                throw new InvalidDataException("可用流程规范目录包含缺失字段或重复项。");
            }
            string[] invalidTopics = entry.Topics
                .Where(topic => !ProcessDesignGuideCatalog.SupportedTopics.Contains(
                    topic,
                    StringComparer.Ordinal))
                .ToArray();
            if (invalidTopics.Length > 0)
            {
                throw new InvalidDataException(
                    "知识目录条目 " + entry.PatternId + " 的 topics 含未支持主题："
                    + string.Join("、", invalidTopics)
                    + "；与请求主题无关，需修正 catalog.json 或 SupportedTopics。");
            }
        }

        private static string ReadResource(string resourceName)
        {
            using Stream? stream = typeof(ProcessKnowledgeCatalog).Assembly
                .GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidDataException("可用流程规范资源不存在：" + resourceName);
            }
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }

        private static string Hash(string source)
        {
            return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        }

        private sealed class ProcessKnowledgeDocument
        {
            public string DocumentType { get; set; } = string.Empty;
            public int SchemaVersion { get; set; }
            public ProcessKnowledgeEntry[] Blocks { get; set; } = Array.Empty<ProcessKnowledgeEntry>();
        }

        private sealed class ProcessKnowledgeEntry
        {
            public string PatternId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
            public string[] Topics { get; set; } = Array.Empty<string>();
            public string[] Capabilities { get; set; } = Array.Empty<string>();
            public string[] DeviceTypes { get; set; } = Array.Empty<string>();
            public string[] ProcessTypes { get; set; } = Array.Empty<string>();
            public string[] RiskTags { get; set; } = Array.Empty<string>();
            public string File { get; set; } = string.Empty;
        }
    }

    internal sealed record ProcessKnowledgeSelection(
        string CatalogSha256,
        IReadOnlyList<ProcessKnowledgeBlock> Blocks);

    internal sealed record ProcessKnowledgeBlock(
        string PatternId,
        string Title,
        string Summary,
        string[] Topics,
        string[] Capabilities,
        string[] DeviceTypes,
        string[] ProcessTypes,
        string[] RiskTags,
        string Markdown,
        string ContentSha256);
}
