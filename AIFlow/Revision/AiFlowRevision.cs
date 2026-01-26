using System;
using System.Collections.Generic;
using System.IO;
using Automation;
using Newtonsoft.Json;

namespace Automation.AIFlow
{
    public sealed class AiFlowRevisionInfo
    {
        public string Id { get; set; }
        public string CreatedAt { get; set; }
        public string Note { get; set; }
    }

    public static class AiFlowRevision
    {
        public static bool SaveRevision(string workPath, string note, out string revisionId, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            revisionId = null;
            if (string.IsNullOrWhiteSpace(workPath))
            {
                issues.Add(new AiFlowIssue("REV_PATH_EMPTY", "Work 路径为空", "revision"));
                return false;
            }
            string expectedWork;
            try
            {
                expectedWork = Path.GetFullPath(SF.workPath ?? string.Empty)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("REV_PATH_INVALID", $"Config 路径无效:{ex.Message}", "revision"));
                return false;
            }
            if (string.IsNullOrWhiteSpace(expectedWork))
            {
                issues.Add(new AiFlowIssue("REV_PATH_INVALID", "Config 路径为空", "revision"));
                return false;
            }
            string normalizedWork;
            try
            {
                normalizedWork = Path.GetFullPath(workPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("REV_PATH_INVALID", $"Work 路径无效:{ex.Message}", "revision"));
                return false;
            }
            if (!string.Equals(normalizedWork, expectedWork, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new AiFlowIssue("REV_PATH_FORBIDDEN", $"只允许使用 Config\\\\Work 路径:{expectedWork}", "revision"));
                return false;
            }

            string workDir = TrimPath(workPath);
            if (!Directory.Exists(workDir))
            {
                issues.Add(new AiFlowIssue("REV_WORK_MISSING", $"Work 目录不存在:{workDir}", "revision"));
                return false;
            }

            string revRoot = GetRevisionRoot(workDir);
            Directory.CreateDirectory(revRoot);
            revisionId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string revDir = Path.Combine(revRoot, revisionId);
            Directory.CreateDirectory(revDir);

            try
            {
                CopyDirectory(workDir, revDir);
                var info = new AiFlowRevisionInfo
                {
                    Id = revisionId,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Note = note
                };
                string metaFile = Path.Combine(revDir, "revision.json");
                File.WriteAllText(metaFile, JsonConvert.SerializeObject(info, Formatting.Indented));
                return true;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("REV_SAVE_FAIL", ex.Message, "revision"));
                return false;
            }
        }

        public static bool Rollback(string workPath, string revisionId, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (string.IsNullOrWhiteSpace(workPath))
            {
                issues.Add(new AiFlowIssue("REV_PATH_EMPTY", "Work 路径为空", "rollback"));
                return false;
            }
            if (string.IsNullOrWhiteSpace(revisionId))
            {
                issues.Add(new AiFlowIssue("REV_ID_EMPTY", "revisionId 为空", "rollback"));
                return false;
            }
            string expectedWork;
            try
            {
                expectedWork = Path.GetFullPath(SF.workPath ?? string.Empty)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("REV_PATH_INVALID", $"Config 路径无效:{ex.Message}", "rollback"));
                return false;
            }
            if (string.IsNullOrWhiteSpace(expectedWork))
            {
                issues.Add(new AiFlowIssue("REV_PATH_INVALID", "Config 路径为空", "rollback"));
                return false;
            }
            string normalizedWork;
            try
            {
                normalizedWork = Path.GetFullPath(workPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("REV_PATH_INVALID", $"Work 路径无效:{ex.Message}", "rollback"));
                return false;
            }
            if (!string.Equals(normalizedWork, expectedWork, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new AiFlowIssue("REV_PATH_FORBIDDEN", $"只允许使用 Config\\\\Work 路径:{expectedWork}", "rollback"));
                return false;
            }

            string workDir = TrimPath(workPath);
            string revRoot = GetRevisionRoot(workDir);
            string revDir = Path.Combine(revRoot, revisionId);
            if (!Directory.Exists(revDir))
            {
                issues.Add(new AiFlowIssue("REV_NOT_FOUND", $"Revision 不存在:{revisionId}", "rollback"));
                return false;
            }

            string configDir = Path.GetDirectoryName(workDir);
            if (string.IsNullOrWhiteSpace(configDir))
            {
                issues.Add(new AiFlowIssue("REV_PATH_INVALID", "Work 路径无效", "rollback"));
                return false;
            }

            string tempDir = Path.Combine(configDir, "Work_tmp");
            string backupDir = Path.Combine(configDir, "Work_bak");

            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);
                CopyDirectory(revDir, tempDir);

                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }
                if (Directory.Exists(workDir))
                {
                    Directory.Move(workDir, backupDir);
                }
                Directory.Move(tempDir, workDir);
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!Directory.Exists(workDir) && Directory.Exists(backupDir))
                {
                    try
                    {
                        Directory.Move(backupDir, workDir);
                    }
                    catch
                    {
                    }
                }
                issues.Add(new AiFlowIssue("REV_ROLLBACK_FAIL", ex.Message, "rollback"));
                return false;
            }
        }

        private static string GetRevisionRoot(string workDir)
        {
            string configDir = Path.GetDirectoryName(workDir);
            return Path.Combine(configDir ?? string.Empty, "Work_revisions");
        }

        private static string TrimPath(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void CopyDirectory(string source, string target)
        {
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string rel = dir.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destDir = Path.Combine(target, rel);
                Directory.CreateDirectory(destDir);
            }
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destFile = Path.Combine(target, rel);
                string destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                File.Copy(file, destFile, true);
            }
        }
    }
}
