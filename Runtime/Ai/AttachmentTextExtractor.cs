using System;
// 模块：运行时 / AI 集成。
// 职责范围：管理 AI 会话、配置、ACP/MCP 进程、受管运行环境和分析记录。

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Automation
{
    internal sealed class AttachmentPreparationResult
    {
        public string MimeType { get; set; }

        public string TypeLabel { get; set; }

        public bool IsImage { get; set; }

        public string ExtractedText { get; set; }

        public string Error { get; set; }
    }

    internal static class AttachmentTextExtractor
    {
        private const int MaxExtractedCharacters = 100000;

        public static AttachmentPreparationResult Prepare(string fileName, byte[] data)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            var result = new AttachmentPreparationResult();
            try
            {
                switch (extension)
                {
                    case ".png":
                        result.MimeType = "image/png";
                        result.TypeLabel = "PNG 图片";
                        result.IsImage = true;
                        return result;
                    case ".jpg":
                    case ".jpeg":
                        result.MimeType = "image/jpeg";
                        result.TypeLabel = "JPEG 图片";
                        result.IsImage = true;
                        return result;
                    case ".gif":
                        result.MimeType = "image/gif";
                        result.TypeLabel = "GIF 图片";
                        result.IsImage = true;
                        return result;
                    case ".bmp":
                        result.MimeType = "image/bmp";
                        result.TypeLabel = "BMP 图片";
                        result.IsImage = true;
                        return result;
                    case ".webp":
                        result.MimeType = "image/webp";
                        result.TypeLabel = "WEBP 图片";
                        result.IsImage = true;
                        return result;
                    case ".csv":
                        return PrepareText(data, "text/csv", "CSV 表格");
                    case ".tsv":
                        return PrepareText(data, "text/tab-separated-values", "TSV 表格");
                    case ".json":
                        return PrepareText(data, "application/json", "JSON 文件");
                    case ".xml":
                        return PrepareText(data, "application/xml", "XML 文件");
                    case ".yaml":
                    case ".yml":
                        return PrepareText(data, "application/yaml", "YAML 文件");
                    case ".md":
                        return PrepareText(data, "text/markdown", "Markdown 文件");
                    case ".html":
                    case ".htm":
                        return PrepareText(data, "text/html", "HTML 文件");
                    case ".txt":
                    case ".log":
                    case ".ini":
                    case ".conf":
                    case ".cfg":
                    case ".env":
                    case ".cs":
                    case ".c":
                    case ".cpp":
                    case ".h":
                    case ".java":
                    case ".py":
                    case ".js":
                    case ".ts":
                    case ".css":
                    case ".sql":
                    case ".bat":
                    case ".cmd":
                    case ".ps1":
                    case ".sh":
                        return PrepareText(data, "text/plain", "文本文件");
                    case ".docx":
                        result.MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                        result.TypeLabel = "Word 文档";
                        result.ExtractedText = ExtractWord(data);
                        break;
                    case ".xlsx":
                        result.MimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                        result.TypeLabel = "Excel 工作簿";
                        result.ExtractedText = ExtractExcel(data);
                        break;
                    case ".pptx":
                        result.MimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                        result.TypeLabel = "PowerPoint 演示文稿";
                        result.ExtractedText = ExtractPowerPoint(data);
                        break;
                    case ".doc":
                    case ".xls":
                    case ".ppt":
                        result.MimeType = "application/octet-stream";
                        result.TypeLabel = "旧版 Office 文件";
                        result.Error = "暂不支持旧版 Office 二进制格式，请另存为 DOCX、XLSX 或 PPTX 后上传。";
                        return result;
                    default:
                        result.MimeType = "application/octet-stream";
                        result.TypeLabel = "不支持的文件";
                        result.Error = $"暂不支持 {extension} 文件分析。";
                        return result;
                }

                if (string.IsNullOrWhiteSpace(result.ExtractedText))
                {
                    result.Error = "文件中未提取到可分析的文字。";
                }
                else if (result.ExtractedText.Length > MaxExtractedCharacters)
                {
                    result.ExtractedText = null;
                    result.Error = $"文件提取文本超过 {MaxExtractedCharacters} 字符，请拆分或缩小文件后重试。";
                }
            }
            catch (Exception ex)
            {
                result.ExtractedText = null;
                result.Error = "文件解析失败：" + ex.Message;
            }
            return result;
        }

        private static AttachmentPreparationResult PrepareText(byte[] data, string mimeType, string typeLabel)
        {
            var result = new AttachmentPreparationResult
            {
                MimeType = mimeType,
                TypeLabel = typeLabel
            };
            try
            {
                result.ExtractedText = DecodeText(data);
                if (string.IsNullOrWhiteSpace(result.ExtractedText))
                {
                    result.Error = "文件中没有可分析的文字。";
                }
                else if (result.ExtractedText.Length > MaxExtractedCharacters)
                {
                    result.ExtractedText = null;
                    result.Error = $"文件文本超过 {MaxExtractedCharacters} 字符，请拆分或缩小文件后重试。";
                }
            }
            catch (DecoderFallbackException)
            {
                result.Error = "文本编码不受支持，仅接受 UTF-8、UTF-16 或 GB18030。";
            }
            return result;
        }

        private static string DecodeText(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                return new UTF8Encoding(false, true).GetString(data, 3, data.Length - 3);
            }
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                return new UnicodeEncoding(false, true, true).GetString(data, 2, data.Length - 2);
            }
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                return new UnicodeEncoding(true, true, true).GetString(data, 2, data.Length - 2);
            }
            try
            {
                return new UTF8Encoding(false, true).GetString(data);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                    .GetString(data);
            }
        }

        private static string ExtractWord(byte[] data)
        {
            using (var stream = new MemoryStream(data, false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                ZipArchiveEntry entry = archive.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("DOCX 缺少 word/document.xml。");
                XDocument document = LoadXml(entry);
                var builder = new StringBuilder();
                foreach (XElement paragraph in document.Descendants().Where(item => item.Name.LocalName == "p"))
                {
                    string line = string.Concat(paragraph.Descendants()
                        .Where(item => item.Name.LocalName == "t")
                        .Select(item => item.Value));
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        builder.AppendLine(line);
                        EnsureTextLimit(builder);
                    }
                }
                return builder.ToString().Trim();
            }
        }

        private static string ExtractPowerPoint(byte[] data)
        {
            using (var stream = new MemoryStream(data, false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                List<ZipArchiveEntry> slides = archive.Entries
                    .Where(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                        && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => ReadTrailingNumber(entry.Name, "slide"))
                    .ToList();
                if (slides.Count == 0)
                {
                    throw new InvalidDataException("PPTX 中未找到幻灯片。");
                }

                var builder = new StringBuilder();
                for (int i = 0; i < slides.Count; i++)
                {
                    XDocument slide = LoadXml(slides[i]);
                    builder.Append("\n\n[第 ").Append(i + 1).AppendLine(" 页]");
                    foreach (string text in slide.Descendants()
                        .Where(item => item.Name.LocalName == "t")
                        .Select(item => item.Value)
                        .Where(value => !string.IsNullOrWhiteSpace(value)))
                    {
                        builder.AppendLine(text);
                        EnsureTextLimit(builder);
                    }
                }
                return builder.ToString().Trim();
            }
        }

        private static string ExtractExcel(byte[] data)
        {
            using (var stream = new MemoryStream(data, false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                var sharedStrings = new List<string>();
                ZipArchiveEntry sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
                if (sharedEntry != null)
                {
                    XDocument shared = LoadXml(sharedEntry);
                    sharedStrings.AddRange(shared.Descendants()
                        .Where(item => item.Name.LocalName == "si")
                        .Select(item => string.Concat(item.Descendants()
                            .Where(text => text.Name.LocalName == "t")
                            .Select(text => text.Value))));
                }

                List<ZipArchiveEntry> sheets = archive.Entries
                    .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                        && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => ReadTrailingNumber(entry.Name, "sheet"))
                    .ToList();
                if (sheets.Count == 0)
                {
                    throw new InvalidDataException("XLSX 中未找到工作表。");
                }

                var builder = new StringBuilder();
                for (int i = 0; i < sheets.Count; i++)
                {
                    XDocument sheet = LoadXml(sheets[i]);
                    builder.Append("\n\n[工作表 ").Append(i + 1).AppendLine("]");
                    foreach (XElement row in sheet.Descendants().Where(item => item.Name.LocalName == "row"))
                    {
                        var values = new List<string>();
                        foreach (XElement cell in row.Elements().Where(item => item.Name.LocalName == "c"))
                        {
                            string type = cell.Attribute("t")?.Value;
                            string value;
                            if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
                            {
                                value = string.Concat(cell.Descendants()
                                    .Where(item => item.Name.LocalName == "t")
                                    .Select(item => item.Value));
                            }
                            else
                            {
                                value = cell.Elements().FirstOrDefault(item => item.Name.LocalName == "v")?.Value
                                    ?? string.Empty;
                                if (string.Equals(type, "s", StringComparison.Ordinal)
                                    && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                                    && index >= 0 && index < sharedStrings.Count)
                                {
                                    value = sharedStrings[index];
                                }
                            }
                            values.Add(value);
                        }
                        if (values.Any(value => !string.IsNullOrEmpty(value)))
                        {
                            builder.AppendLine(string.Join("\t", values));
                            EnsureTextLimit(builder);
                        }
                    }
                }
                return builder.ToString().Trim();
            }
        }

        private static XDocument LoadXml(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            {
                return XDocument.Load(stream, LoadOptions.None);
            }
        }

        private static int ReadTrailingNumber(string fileName, string prefix)
        {
            string name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            string number = name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(prefix.Length)
                : string.Empty;
            return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                ? value
                : int.MaxValue;
        }

        private static void EnsureTextLimit(StringBuilder builder)
        {
            if (builder.Length > MaxExtractedCharacters)
            {
                throw new InvalidDataException($"文件提取文本超过 {MaxExtractedCharacters} 字符，请拆分或缩小文件后重试。");
            }
        }
    }
}
