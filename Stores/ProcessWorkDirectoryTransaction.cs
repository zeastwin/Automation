using System;
using System.Collections.Generic;
using System.IO;

namespace Automation
{
    /// <summary>
    /// 连续索引流程文件的目录级事务与断电恢复。
    /// </summary>
    public static class ProcessWorkDirectoryTransaction
    {
        private static readonly object transactionLock = new object();

        public static List<Proc> Load(
            string workPath,
            ProcessDefinitionValidationContext validationContext,
            out List<string> errors,
            out string recoveryMessage)
        {
            lock (transactionLock)
            {
                errors = new List<string>();
                recoveryMessage = null;
                string workDir = (workPath ?? string.Empty).TrimEnd('\\');
                if (string.IsNullOrWhiteSpace(workDir))
                {
                    errors.Add("流程配置目录无效。");
                    return new List<Proc>();
                }
                if (!RecoverIfNeededCore(workDir, out recoveryMessage))
                {
                    errors.Add(recoveryMessage);
                }

                Directory.CreateDirectory(workDir);
                Dictionary<int, string> indexMap =
                    ProcessDefinitionService.BuildProcFileIndexMap(workDir, out int maxIndex);
                errors.AddRange(ProcessDefinitionService.ValidateProcFileContinuity(indexMap, maxIndex));

                var processes = new List<Proc>();
                var procIds = new HashSet<Guid>();
                for (int i = 0; i <= maxIndex; i++)
                {
                    Proc process = null;
                    if (indexMap.ContainsKey(i))
                    {
                        process = AtomicJsonFileStore.Read<Proc>(
                            workDir,
                            i.ToString(),
                            ProcessDefinitionService.CreateStrictJsonSettings());
                    }
                    if (process == null)
                    {
                        errors.Add($"流程文件加载失败：{i}.json");
                        process = new Proc();
                    }
                    ProcessDefinitionService.NormalizeProc(i, process, errors, validationContext);
                    if (process.head?.Id != Guid.Empty && !procIds.Add(process.head.Id))
                    {
                        errors.Add($"流程{i}的ID重复：{process.head.Id:D}");
                    }
                    processes.Add(process);
                }
                return processes;
            }
        }

        public static bool Rebuild(string workPath, IList<Proc> processes, int StartIndex,
            out string error, out bool rollbackFailed)
        {
            lock (transactionLock)
            {
                return RebuildCore(workPath, processes, StartIndex, out error, out rollbackFailed);
            }
        }

        private static bool RebuildCore(string workPath, IList<Proc> processes, int StartIndex,
            out string error, out bool rollbackFailed)
        {
            error = null;
            rollbackFailed = false;
            string workDir = (workPath ?? string.Empty).TrimEnd('\\');
            string configDir = Path.GetDirectoryName(workDir);
            if (string.IsNullOrWhiteSpace(configDir) || processes == null)
            {
                error = "流程目录或流程数据无效。";
                return false;
            }
            if (!RecoverIfNeededCore(workDir, out string recoveryMessage))
            {
                error = recoveryMessage;
                return false;
            }
            Directory.CreateDirectory(workDir);
            string tempDir = Path.Combine(configDir, "Work_tmp");
            string backupDir = Path.Combine(configDir, "Work_bak");
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);
                ProcessEditingService.AdaptGotoProcIndexes(processes, StartIndex);
                for (int i = 0; i < processes.Count; i++)
                {
                    if (!AtomicJsonFileStore.Save(tempDir, i.ToString(), processes[i]))
                    {
                        error = $"流程{i}写入临时配置失败，原配置未修改。";
                        Directory.Delete(tempDir, true);
                        return false;
                    }
                }
                AtomicJsonFileStore.WriteDurable(Path.Combine(tempDir, ".complete"), "complete");
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }
                if (Directory.Exists(workDir))
                {
                    Directory.Move(workDir, backupDir);
                }
                Directory.Move(tempDir, workDir);
                return true;
            }
            catch (Exception ex)
            {
                error = $"流程目录事务失败：{ex.Message}";
                if (!Directory.Exists(workDir) && Directory.Exists(backupDir))
                {
                    try
                    {
                        Directory.Move(backupDir, workDir);
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackFailed = true;
                        error += $"；目录回滚失败：{rollbackException.Message}";
                    }
                }
                return false;
            }
        }

        public static bool RecoverIfNeeded(string workPath, out string message)
        {
            lock (transactionLock)
            {
                return RecoverIfNeededCore(workPath, out message);
            }
        }

        private static bool RecoverIfNeededCore(string workPath, out string message)
        {
            message = null;
            string workDir = (workPath ?? string.Empty).TrimEnd('\\');
            string configDir = Path.GetDirectoryName(workDir);
            if (string.IsNullOrWhiteSpace(configDir))
            {
                message = "流程配置目录无效。";
                return false;
            }
            string backupDir = Path.Combine(configDir, "Work_bak");
            string tempDir = Path.Combine(configDir, "Work_tmp");
            try
            {
                if (!Directory.Exists(workDir) && Directory.Exists(backupDir))
                {
                    Directory.Move(backupDir, workDir);
                    message = "检测到流程目录交换未完成，已恢复上一版 Work_bak。";
                }
                else if (!Directory.Exists(workDir) && Directory.Exists(tempDir)
                    && File.Exists(Path.Combine(tempDir, ".complete")))
                {
                    Directory.Move(tempDir, workDir);
                    message = "检测到首次流程目录提交中断，已启用完整的 Work_tmp。";
                }
                if (Directory.Exists(workDir) && Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                return true;
            }
            catch (Exception ex)
            {
                message = $"流程配置目录自动恢复失败：{ex.Message}";
                return false;
            }
        }
    }
}
