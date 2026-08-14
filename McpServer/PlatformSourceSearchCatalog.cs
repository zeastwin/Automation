using System.Text.Json;

namespace Automation.McpServer
{
    /// <summary>
    /// 平台源码只读检索。只允许环境变量声明的源码根目录和固定文本扩展名，避免把 Shell 开给只读能力。
    /// </summary>
    internal static class PlatformSourceSearchCatalog
    {
        private const string PlatformSourceRootEnvironmentVariable = "AUTOMATION_PLATFORM_SOURCE_ROOT";
        private const int MaximumQueryLength = 200;
        private const int MaximumFilesScanned = 10000;
        private const int MaximumLineCharacters = 600;

        private static readonly HashSet<string> supportedExtensions = new HashSet<string>(
            new[]
            {
                ".cs", ".csproj", ".props", ".targets", ".json", ".md",
                ".js", ".html", ".css", ".ps1", ".xml", ".config", ".yaml", ".yml"
            },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> excludedDirectoryNames = new HashSet<string>(
            new[]
            {
                "bin", "obj", ".git", ".vs", ".codex", ".agents", ".codex-build",
                "packages", "node_modules", "TestResults", "publish", "Artifacts"
            },
            StringComparer.OrdinalIgnoreCase);

        public static string Search(
            string query,
            string relativeDirectory,
            string fileExtension,
            int maxResults)
        {
            string literal = (query ?? string.Empty).Trim();
            if (literal.Length == 0 || literal.Length > MaximumQueryLength)
                return Error("SOURCE_SEARCH_QUERY_INVALID", $"query 长度必须在 1..{MaximumQueryLength}。", "none");
            if (maxResults < 1 || maxResults > 100)
                return Error("SOURCE_SEARCH_LIMIT_INVALID", "maxResults 必须在 1..100。", "none");

            string extension = NormalizeExtension(fileExtension);
            if (!supportedExtensions.Contains(extension))
            {
                return Error(
                    "SOURCE_SEARCH_EXTENSION_INVALID",
                    "fileExtension 不受支持。",
                    "none",
                    new { allowedExtensions = supportedExtensions.OrderBy(value => value, StringComparer.Ordinal).ToArray() });
            }

            string? root = Environment.GetEnvironmentVariable(PlatformSourceRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return Error("SOURCE_ROOT_UNAVAILABLE", "平台源码根目录不可用。", "none");

            string normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
            string relative = (relativeDirectory ?? string.Empty).Trim()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string searchDirectory;
            try
            {
                searchDirectory = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
            }
            catch (Exception ex)
            {
                return Error("SOURCE_SEARCH_PATH_INVALID", "relativeDirectory 无效：" + ex.Message, "none");
            }
            if (!IsWithinRoot(searchDirectory, normalizedRoot) || !Directory.Exists(searchDirectory))
                return Error("SOURCE_SEARCH_PATH_OUTSIDE_ROOT", "relativeDirectory 不存在或超出平台源码根目录。", "none");

            var matches = new List<object>();
            int scannedFiles = 0;
            bool truncated = false;
            try
            {
                foreach (string file in EnumerateFiles(searchDirectory, extension))
                {
                    if (++scannedFiles > MaximumFilesScanned)
                    {
                        truncated = true;
                        break;
                    }
                    int lineNumber = 0;
                    foreach (string line in File.ReadLines(file))
                    {
                        lineNumber++;
                        if (line.IndexOf(literal, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        matches.Add(new
                        {
                            path = Path.GetRelativePath(root, file),
                            line = lineNumber,
                            text = line.Length <= MaximumLineCharacters
                                ? line
                                : line.Substring(0, MaximumLineCharacters) + " …"
                        });
                        if (matches.Count >= maxResults)
                        {
                            truncated = true;
                            break;
                        }
                    }
                    if (matches.Count >= maxResults) break;
                }
            }
            catch (Exception ex)
            {
                return Error("SOURCE_SEARCH_READ_FAILED", "源码检索失败：" + ex.Message, "none");
            }

            return JsonSerializer.Serialize(new
            {
                ok = true,
                type = "source.search",
                data = new
                {
                    query = literal,
                    relativeDirectory = Path.GetRelativePath(root, searchDirectory),
                    fileExtension = extension,
                    scannedFiles,
                    matchCount = matches.Count,
                    truncated,
                    matches
                },
                recovery = new { sideEffects = "none", safeToRetry = true }
            });
        }

        private static IEnumerable<string> EnumerateFiles(string root, string extension)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string file in Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
                foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    var info = new DirectoryInfo(child);
                    if (excludedDirectoryNames.Contains(info.Name)) continue;
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(child);
                }
            }
        }

        private static string NormalizeExtension(string value)
        {
            string extension = (value ?? string.Empty).Trim();
            if (!extension.StartsWith(".", StringComparison.Ordinal)) extension = "." + extension;
            return extension.ToLowerInvariant();
        }

        private static bool IsWithinRoot(string path, string rootWithSeparator)
        {
            string fullPath = Path.GetFullPath(path);
            string rootWithoutSeparator = rootWithSeparator.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, rootWithoutSeparator, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }

        private static string Error(string errorCode, string message, string sideEffects, object? details = null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                type = "mcp.error",
                errorCode,
                message,
                details,
                recovery = new { sideEffects, safeToRetry = true }
            });
        }
    }
}
