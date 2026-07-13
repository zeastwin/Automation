using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// ChangeSet 专用的流程目录与变量文件联合事务。
    /// 提交标记最后落盘；启动时对无提交标记的事务恢复旧版本。
    /// </summary>
    public static class AiConfigurationTransaction
    {
        private const string TransactionPrefix = ".change-set-transaction-";
        private const string ManifestFileName = "manifest.json";
        private const string CommittedFileName = ".committed";

        private sealed class Manifest
        {
            public bool WorkExisted { get; set; }

            public bool ValueExisted { get; set; }
        }

        public static bool Commit(
            string configPath,
            IList<Proc> processes,
            IDictionary<string, DicValue> variables,
            out string error,
            out bool rollbackFailed)
        {
            error = null;
            rollbackFailed = false;
            if (processes == null || variables == null)
            {
                error = "流程或变量事务数据为空。";
                return false;
            }
            string root = Path.GetFullPath(configPath ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(root))
            {
                error = "配置目录无效。";
                return false;
            }
            Directory.CreateDirectory(root);
            foreach (string existingTransaction in Directory.EnumerateDirectories(root, TransactionPrefix + "*").ToList())
            {
                if (File.Exists(Path.Combine(existingTransaction, CommittedFileName)))
                {
                    TryDeleteDirectory(existingTransaction);
                    continue;
                }
                error = "存在尚未恢复的 ChangeSet 配置事务，禁止开始新提交。";
                return false;
            }

            string transactionPath = Path.Combine(root, TransactionPrefix + Guid.NewGuid().ToString("N"));
            string activeWorkPath = Path.Combine(root, "Work");
            string activeValuePath = Path.Combine(root, "value.json");
            string newWorkPath = Path.Combine(transactionPath, "Work.new");
            string oldWorkPath = Path.Combine(transactionPath, "Work.old");
            string newValuePath = Path.Combine(transactionPath, "value.new.json");
            string oldValuePath = Path.Combine(transactionPath, "value.old.json");
            var manifest = new Manifest
            {
                WorkExisted = Directory.Exists(activeWorkPath),
                ValueExisted = File.Exists(activeValuePath)
            };

            try
            {
                Directory.CreateDirectory(transactionPath);
                Directory.CreateDirectory(newWorkPath);
                List<Proc> stagedProcesses = processes.Select(ObjectGraphCloner.Clone).ToList();
                ProcessEditingService.AdaptGotoProcIndexes(stagedProcesses, 0);
                for (int index = 0; index < stagedProcesses.Count; index++)
                {
                    if (!AtomicJsonFileStore.Save(newWorkPath, index.ToString(), stagedProcesses[index]))
                    {
                        throw new IOException($"流程{index}写入 ChangeSet 暂存区失败。");
                    }
                }
                AtomicJsonFileStore.WriteDurable(Path.Combine(newWorkPath, ".complete"), "complete");
                if (!AtomicJsonFileStore.Save(transactionPath, "value.new", variables))
                {
                    throw new IOException("变量配置写入 ChangeSet 暂存区失败。");
                }
                AtomicJsonFileStore.WriteDurable(
                    Path.Combine(transactionPath, ManifestFileName),
                    JsonConvert.SerializeObject(manifest));

                if (manifest.WorkExisted) Directory.Move(activeWorkPath, oldWorkPath);
                Directory.Move(newWorkPath, activeWorkPath);
                if (manifest.ValueExisted) File.Move(activeValuePath, oldValuePath);
                File.Move(newValuePath, activeValuePath);
                AtomicJsonFileStore.WriteDurable(Path.Combine(transactionPath, CommittedFileName), "committed");
                TryDeleteDirectory(transactionPath);
                return true;
            }
            catch (Exception ex)
            {
                error = $"ChangeSet 配置事务失败：{ex.Message}";
                if (!TryRollback(root, transactionPath, manifest, out string rollbackError))
                {
                    rollbackFailed = true;
                    error += $"；回滚失败：{rollbackError}";
                }
                return false;
            }
        }

        public static bool RecoverPendingTransactions(string configPath, out string error)
        {
            error = null;
            try
            {
                string root = Path.GetFullPath(configPath ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar);
                if (!Directory.Exists(root)) return true;
                foreach (string transactionPath in Directory.EnumerateDirectories(root, TransactionPrefix + "*"))
                {
                    if (File.Exists(Path.Combine(transactionPath, CommittedFileName)))
                    {
                        TryDeleteDirectory(transactionPath);
                        continue;
                    }
                    string manifestPath = Path.Combine(transactionPath, ManifestFileName);
                    if (!File.Exists(manifestPath))
                    {
                        // 清单写入前不会移动正式配置，暂存目录可直接清理。
                        TryDeleteDirectory(transactionPath);
                        continue;
                    }
                    Manifest manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath));
                    if (manifest == null)
                    {
                        error = $"ChangeSet 事务清单无效：{manifestPath}";
                        return false;
                    }
                    if (!TryRollback(root, transactionPath, manifest, out error))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"恢复 ChangeSet 配置事务失败：{ex.Message}";
                return false;
            }
        }

        private static bool TryRollback(
            string root,
            string transactionPath,
            Manifest manifest,
            out string error)
        {
            error = null;
            try
            {
                string activeWorkPath = Path.Combine(root, "Work");
                string activeValuePath = Path.Combine(root, "value.json");
                string newWorkPath = Path.Combine(transactionPath, "Work.new");
                string oldWorkPath = Path.Combine(transactionPath, "Work.old");
                string newValuePath = Path.Combine(transactionPath, "value.new.json");
                string oldValuePath = Path.Combine(transactionPath, "value.old.json");

                if (Directory.Exists(oldWorkPath))
                {
                    if (Directory.Exists(activeWorkPath)) Directory.Delete(activeWorkPath, true);
                    Directory.Move(oldWorkPath, activeWorkPath);
                }
                else if (!manifest.WorkExisted && !Directory.Exists(newWorkPath) && Directory.Exists(activeWorkPath))
                {
                    Directory.Delete(activeWorkPath, true);
                }

                if (File.Exists(oldValuePath))
                {
                    if (File.Exists(activeValuePath)) File.Delete(activeValuePath);
                    File.Move(oldValuePath, activeValuePath);
                }
                else if (!manifest.ValueExisted && !File.Exists(newValuePath) && File.Exists(activeValuePath))
                {
                    File.Delete(activeValuePath);
                }

                TryDeleteDirectory(transactionPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
                // 已提交事务保留清理由下次启动完成，不影响正式配置一致性。
            }
        }
    }
}
