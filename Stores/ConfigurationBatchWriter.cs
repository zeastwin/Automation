using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Automation
{
    public sealed class ConfigurationBatchWriter : IDisposable
    {
        private readonly string configPath;
        private readonly string transactionPath;
        private readonly List<Entry> entries = new List<Entry>();

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
            this.configPath = Path.GetFullPath(configPath ?? throw new ArgumentNullException(nameof(configPath)));
            Directory.CreateDirectory(this.configPath);
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
                foreach (Entry entry in entries)
                {
                    entry.TargetExisted = File.Exists(entry.TargetPath);
                    if (entry.TargetExisted)
                    {
                        File.Copy(entry.TargetPath, entry.BackupPath, true);
                        File.Replace(entry.StagedPath, entry.TargetPath, null);
                    }
                    else
                    {
                        File.Move(entry.StagedPath, entry.TargetPath);
                    }
                    entry.Replaced = true;
                }
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
