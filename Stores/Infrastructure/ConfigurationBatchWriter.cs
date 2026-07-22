using Newtonsoft.Json;
// 模块：持久化 / 基础设施。
// 职责范围：提供 JSON 原子读写和跨文件批量提交能力。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Automation
{
    public sealed class ConfigurationBatchWriter : IDisposable
    {
        private readonly string configPath;
        private readonly string transactionPath;
        private readonly List<Entry> entries = new List<Entry>();
        private const string ManifestFileName = "manifest.json";
        private const string CommittedFileName = ".committed";

        private sealed class Manifest
        {
            public List<ManifestEntry> Entries { get; set; } = new List<ManifestEntry>();
        }

        private sealed class ManifestEntry
        {
            public string FileName { get; set; }
            public bool TargetExisted { get; set; }
        }

        private sealed class Entry
        {
            public string TargetPath;
            public string StagedPath;
            public string BackupPath;
            public bool TargetExisted;
            public bool Replaced;
        }

        public ConfigurationBatchWriter(string configPath)
        {
            this.configPath = Path.GetFullPath(configPath ?? throw new ArgumentNullException(nameof(configPath)))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Directory.CreateDirectory(this.configPath);
            if (!RecoverPendingTransactions(this.configPath, out string recoveryError))
            {
                throw new InvalidDataException(recoveryError);
            }
            transactionPath = Path.Combine(this.configPath, ".transaction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transactionPath);
        }

        public void AddJson(string fileName, object value, JsonSerializerSettings settings = null)
        {
            if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            {
                throw new ArgumentException("配置文件名无效。", nameof(fileName));
            }
            string targetPath = Path.GetFullPath(Path.Combine(configPath, fileName));
            if (!targetPath.StartsWith(configPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("配置文件超出配置目录。");
            }
            if (entries.Any(item => string.Equals(item.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"批量配置中存在重复文件：{fileName}");
            }
            string stagedPath = Path.Combine(transactionPath, fileName + ".new");
            AtomicJsonFileStore.WriteDurable(
                stagedPath,
                JsonConvert.SerializeObject(value, settings ?? new JsonSerializerSettings()));
            entries.Add(new Entry
            {
                TargetPath = targetPath,
                StagedPath = stagedPath,
                BackupPath = Path.Combine(transactionPath, fileName + ".bak")
            });
        }

        public void Commit()
        {
            try
            {
                var manifest = new Manifest();
                foreach (Entry entry in entries)
                {
                    entry.TargetExisted = File.Exists(entry.TargetPath);
                    manifest.Entries.Add(new ManifestEntry
                    {
                        FileName = Path.GetFileName(entry.TargetPath),
                        TargetExisted = entry.TargetExisted
                    });
                }
                AtomicJsonFileStore.WriteDurable(
                    Path.Combine(transactionPath, ManifestFileName),
                    JsonConvert.SerializeObject(manifest));

                foreach (Entry entry in entries)
                {
                    if (entry.TargetExisted)
                    {
                        string backupTempPath = entry.BackupPath + ".tmp";
                        File.Copy(entry.TargetPath, backupTempPath, false);
                        FlushFile(backupTempPath);
                        File.Move(backupTempPath, entry.BackupPath);
                        File.Replace(entry.StagedPath, entry.TargetPath, null);
                    }
                    else
                    {
                        File.Move(entry.StagedPath, entry.TargetPath);
                    }
                    entry.Replaced = true;
                }
                AtomicJsonFileStore.WriteDurable(Path.Combine(transactionPath, CommittedFileName), "committed");
            }
            catch
            {
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    Entry entry = entries[i];
                    if (!entry.Replaced)
                    {
                        continue;
                    }
                    if (entry.TargetExisted)
                    {
                        File.Copy(entry.BackupPath, entry.TargetPath, true);
                    }
                    else if (File.Exists(entry.TargetPath))
                    {
                        File.Delete(entry.TargetPath);
                    }
                }
                throw;
            }
        }

        public static bool RecoverPendingTransactions(string configPath, out string error)
        {
            error = null;
            try
            {
                string root = Path.GetFullPath(configPath ?? throw new ArgumentNullException(nameof(configPath)))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(root))
                {
                    return true;
                }
                foreach (string directory in Directory.EnumerateDirectories(root, ".transaction-*"))
                {
                    string committedPath = Path.Combine(directory, CommittedFileName);
                    if (File.Exists(committedPath))
                    {
                        Directory.Delete(directory, true);
                        continue;
                    }

                    string manifestPath = Path.Combine(directory, ManifestFileName);
                    if (!File.Exists(manifestPath))
                    {
                        error = $"发现无事务清单的配置暂存目录：{directory}";
                        return false;
                    }
                    Manifest manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
                    if (manifest?.Entries == null)
                    {
                        error = $"配置事务清单无效：{manifestPath}";
                        return false;
                    }
                    foreach (ManifestEntry item in manifest.Entries)
                    {
                        if (item == null || string.IsNullOrWhiteSpace(item.FileName)
                            || Path.GetFileName(item.FileName) != item.FileName)
                        {
                            error = $"配置事务清单包含非法文件名：{manifestPath}";
                            return false;
                        }
                        string targetPath = Path.Combine(root, item.FileName);
                        string backupPath = Path.Combine(directory, item.FileName + ".bak");
                        string stagedPath = Path.Combine(directory, item.FileName + ".new");
                        if (item.TargetExisted)
                        {
                            if (!File.Exists(backupPath))
                            {
                                // 备份尚未产生，说明该文件还没有进入替换阶段。
                                continue;
                            }
                            RestoreFile(backupPath, targetPath);
                        }
                        else if (!File.Exists(stagedPath) && File.Exists(targetPath))
                        {
                            // 暂存文件已被 Move，恢复到事务前“不存在”的状态。
                            File.Delete(targetPath);
                        }
                    }
                    Directory.Delete(directory, true);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"恢复未完成的配置事务失败：{ex.Message}";
                return false;
            }
        }

        private static void RestoreFile(string sourcePath, string targetPath)
        {
            string tempPath = targetPath + ".recover-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(sourcePath, tempPath, false);
                FlushFile(tempPath);
                if (File.Exists(targetPath))
                {
                    File.Replace(tempPath, targetPath, null, true);
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void FlushFile(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                stream.Flush(true);
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(transactionPath))
                {
                    Directory.Delete(transactionPath, true);
                }
            }
            catch
            {
            }
        }
    }
}
