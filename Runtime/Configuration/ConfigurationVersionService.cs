using LibGit2Sharp;
// 模块：运行时 / 配置协调。
// 职责范围：处理应用配置、序列化边界、配置版本和 HMI 开发源码定位。

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Automation
{
    public sealed class ConfigurationVersionRecord
    {
        public string CommitId { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string SnapshotType { get; set; }
    }

    public sealed class ConfigurationVersionDiffEntry
    {
        public string Category { get; set; }
        public string Target { get; set; }
        public string FieldPath { get; set; }
        public string ChangeType { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
        public string Title { get; set; }
        public string Location { get; set; }
        public string FieldName { get; set; }
        public string DetailBefore { get; set; }
        public string DetailAfter { get; set; }
        public List<ConfigurationVersionFieldDiff> Details { get; set; }
    }

    public sealed class ConfigurationVersionFieldDiff
    {
        public string ChangeType { get; set; }
        public string FieldName { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
    }

    /// <summary>
    /// 运行时项目配置的私有 Git 版本服务。流程、设备、运行值和 HMI 源码共享一条历史主链。
    /// </summary>
    public sealed class ConfigurationVersionService
    {
        private const string SnapshotDirectoryName = "Snapshot";
        private const string VersionDirectoryName = ".AutomationVersions";
        private const string VersionRepositoryName = "Configuration";
        private const string RestoreDirectoryName = "Restore";
        private const string RestoreTransactionFileName = "restore-transaction.json";
        private const int MaxVisibleSnapshots = 100;
        private const int HistoryCompactionThreshold = 120;
        private const string CompactionDirectoryPrefix = ".Configuration-compact-";
        private const string CompactionBackupPrefix = ".Configuration-backup-";
        private static readonly HashSet<string> ManagedRootConfigFiles =
            new HashSet<string>(new[]
            {
                "AlarmInfo.json",
                "AppConfig.json",
                "card.json",
                "DataStation.json",
                "DataStruct.json",
                "GooseConfig.json",
                "IODebugMap.json",
                "IOMap.json",
                "PlcConfig.json",
                "SerialPortInfo.json",
                "SocketInfo.json",
                "value.json",
                "value_debug.json"
            }, StringComparer.OrdinalIgnoreCase);
        private readonly string configPath;
        private readonly string hmiSourceRoot;
        private readonly string hmiSourceError;
        private readonly PlatformRuntime runtime;
        private readonly object syncRoot = new object();
        private static readonly object nativeLibraryLock = new object();
        private static bool nativeLibraryConfigured;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public ConfigurationVersionService(string configPath, PlatformRuntime runtime)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("配置目录不能为空。", nameof(configPath));
            }
            this.configPath = Path.GetFullPath(configPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (HmiDevelopmentSourceLocator.TryResolve(
                this.configPath,
                out HmiDevelopmentSource hmiSource,
                out string sourceError))
            {
                hmiSourceRoot = hmiSource.SourceDirectory;
                hmiSourceError = null;
            }
            else
            {
                hmiSourceRoot = null;
                hmiSourceError = sourceError;
            }
            RecoverInterruptedVersionOperations();
        }

        public bool CreateManualSnapshot(string note, string userName, out string error)
        {
            if (!runtime.Maintenance.TryBegin(
                "正在创建项目版本",
                out IDisposable maintenanceLease,
                out error))
            {
                return false;
            }
            using (maintenanceLease)
            {
                lock (syncRoot)
                {
                    try
                    {
                        bool captured;
                        using (Repository repository = OpenRepository())
                        {
                            string message = string.IsNullOrWhiteSpace(note) ? "手动快照" : "手动快照：" + note.Trim();
                            PersistRuntimeBackedConfiguration();
                            captured = CaptureManualSnapshot(
                                repository,
                                message,
                                userName,
                                out error);
                        }
                        if (captured
                            && !TryCompactHistory(
                                false,
                                out string maintenanceError))
                        {
                            error = "快照已创建；" + maintenanceError;
                        }
                        return captured;
                    }
                    catch (Exception ex)
                    {
                        error = "创建手动快照失败：" + ex.Message;
                        return false;
                    }
                }
            }
        }

        public IReadOnlyList<ConfigurationVersionRecord> GetHistory(out bool currentDirty, out string error)
        {
            lock (syncRoot)
            {
                currentDirty = false;
                error = null;
                List<ConfigurationVersionRecord> result = new List<ConfigurationVersionRecord>();
                try
                {
                    string maintenanceWarning = null;
                    if (!TryCompactHistory(
                        false,
                        out maintenanceWarning))
                    {
                        LogVersionMaintenanceFailure(
                            maintenanceWarning);
                    }
                    using (Repository repository = OpenRepository())
                    {
                        HashSet<string> deleted = ReadDeletedSnapshotIds(repository);
                        IReadOnlyList<SnapshotCommit> snapshots =
                            GetVisibleSnapshots(repository, deleted);
                        SnapshotCommit latest = snapshots.FirstOrDefault();
                        currentDirty = latest == null
                            ? GetManagedRelativePaths().Any()
                            : !AreFileSetsEquivalent(
                                ReadSnapshotFiles(latest.Commit.Tree),
                                ReadCurrentFiles());
                        foreach (SnapshotCommit snapshot in snapshots)
                        {
                            Commit commit = snapshot.Commit;
                            result.Add(new ConfigurationVersionRecord
                            {
                                CommitId = commit.Sha,
                                Message = commit.MessageShort,
                                Author = commit.Author.Name,
                                CreatedAt = commit.Author.When,
                                SnapshotType = IsRestoreProtectionSnapshot(commit.MessageShort)
                                    ? "还原前保护点"
                                    : IsRestoreResultSnapshot(commit.MessageShort)
                                        ? "还原结果"
                                        : "手动快照"
                            });
                        }
                    }
                    error = maintenanceWarning;
                    return result;
                }
                catch (Exception ex)
                {
                    error = "读取版本历史失败：" + ex.Message;
                    return result;
                }
            }
        }

        public bool DeleteSnapshot(string commitId, out string error)
        {
            lock (syncRoot)
            {
                try
                {
                    bool deletedSuccessfully;
                    using (Repository repository = OpenRepository())
                    {
                        Commit commit = repository.Lookup<Commit>(commitId);
                        SnapshotCommit snapshot = FindVisibleSnapshot(repository, commitId);
                        if (commit == null || snapshot == null)
                        {
                            error = "快照不存在于版本历史中。";
                            return false;
                        }
                        HashSet<string> deleted = ReadDeletedSnapshotIds(repository);
                        deleted.Add(commit.Sha);
                        WriteDeletedSnapshotIds(repository, deleted);
                        error = null;
                        deletedSuccessfully = true;
                    }
                    if (deletedSuccessfully
                        && !TryCompactHistory(
                            true,
                            out string maintenanceError))
                    {
                        error = "快照已从列表删除；" + maintenanceError;
                    }
                    return deletedSuccessfully;
                }
                catch (Exception ex)
                {
                    error = "删除快照失败：" + ex.Message;
                    return false;
                }
            }
        }

        public IReadOnlyList<ConfigurationVersionDiffEntry> GetStructuredDiff(string commitId, bool compareWithPrevious, out string error)
        {
            error = null;
            lock (syncRoot)
            {
                try
                {
                    using (Repository repository = OpenRepository())
                    {
                        List<SnapshotCommit> snapshots = GetVisibleSnapshots(repository).ToList();
                        SnapshotCommit selected = snapshots.FirstOrDefault(item =>
                            string.Equals(item.Commit.Sha, commitId, StringComparison.OrdinalIgnoreCase));
                        if (selected == null)
                        {
                            throw new InvalidOperationException("找不到选中的版本。");
                        }

                        Dictionary<string, string> before = ReadSnapshotFiles(selected.Commit.Tree);
                        Dictionary<string, string> after;
                        if (compareWithPrevious)
                        {
                            SnapshotCommit previous = snapshots
                                .SkipWhile(item => item.Commit.Sha != selected.Commit.Sha)
                                .Skip(1)
                                .FirstOrDefault();
                            Dictionary<string, string> previousFiles = previous == null
                                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                : ReadSnapshotFiles(previous.Commit.Tree);
                            return BuildStructuredDiff(previousFiles, before);
                        }

                        after = ReadCurrentFiles();
                        return BuildStructuredDiff(before, after);
                    }
                }
                catch (Exception ex)
                {
                    error = "读取结构化差异失败：" + ex.Message;
                    return new List<ConfigurationVersionDiffEntry>();
                }
            }
        }

        public bool Restore(
            string commitId,
            Func<bool> allStopped,
            Action markRestartRequired,
            out string error)
        {
            if (!runtime.Maintenance.TryBegin("正在还原项目配置", out IDisposable maintenanceLease, out error))
            {
                return false;
            }
            using (maintenanceLease)
            {
                lock (syncRoot)
                {
                    string operationRoot = null;
                    bool backupComplete = false;
                    bool replacementStarted = false;
                    bool versionHistoryChanged = false;
                    bool preserveOperationForRecovery = false;
                    RestoreTransactionState transactionState = null;
                    try
                    {
                        if (allStopped == null || !allStopped())
                        {
                            error = "存在未停止、暂停或报警中的流程，拒绝还原版本。";
                            return false;
                        }
                        // 保护点必须包含停机时刻的持久化运行值，不能只复制上一次落盘值。
                        PersistRuntimeBackedConfiguration();

                        using (Repository repository = OpenRepository())
                        {
                            SnapshotCommit selected = FindVisibleSnapshot(repository, commitId);
                            if (selected == null)
                            {
                                error = "找不到选中的版本。";
                                return false;
                            }

                            Dictionary<string, string> selectedFiles =
                                ReadSnapshotFiles(selected.Commit.Tree);
                            Dictionary<string, string> currentFiles =
                                ReadCurrentFiles();
                            if (AreFileSetsEquivalent(selectedFiles, currentFiles))
                            {
                                error = "当前配置已经与所选快照一致，无需还原。";
                                return false;
                            }

                            operationRoot = Path.Combine(
                                GetVersionRoot(),
                                RestoreDirectoryName,
                                Guid.NewGuid().ToString("N"));
                            string staging = Path.Combine(operationRoot, "staging");
                            string backup = Path.Combine(operationRoot, "backup");
                            Directory.CreateDirectory(staging);
                            Directory.CreateDirectory(backup);
                            transactionState = new RestoreTransactionState
                            {
                                Status = "Preparing",
                                TargetCommitId = selected.Commit.Sha,
                                CreatedAt = DateTimeOffset.Now
                            };
                            WriteRestoreTransactionState(
                                operationRoot,
                                transactionState);
                            MaterializeSnapshot(selected.Commit.Tree, staging);
                            ValidateStaging(staging);

                            SnapshotCommit latest = GetVisibleSnapshots(repository).FirstOrDefault();
                            bool currentAlreadyProtected = latest != null
                                && AreFileSetsEquivalent(
                                    ReadSnapshotFiles(latest.Commit.Tree),
                                    currentFiles);
                            if (!currentAlreadyProtected
                                && !CreateProtectionSnapshot(
                                    repository,
                                    "还原前保护点："
                                        + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                    out string protectionError))
                            {
                                error = "创建还原前保护点失败：" + protectionError;
                                return false;
                            }
                            versionHistoryChanged =
                                !currentAlreadyProtected;

                            CopyCurrentConfiguration(backup);
                            backupComplete = true;
                            transactionState.Status = "BackupReady";
                            WriteRestoreTransactionState(
                                operationRoot,
                                transactionState);
                            replacementStarted = true;
                            transactionState.Status = "Replacing";
                            WriteRestoreTransactionState(
                                operationRoot,
                                transactionState);
                            ReplaceConfiguration(staging);

                            if (markRestartRequired == null)
                            {
                                throw new InvalidOperationException("版本还原重启锁定入口未配置。");
                            }
                            // 版本还原会替换一组相互引用的配置。无论所属分类，
                            // 都保持磁盘快照为唯一权威，并通过完整重启重建运行时对象图。
                            markRestartRequired();

                            transactionState.Status = "Completed";
                            WriteRestoreTransactionState(
                                operationRoot,
                                transactionState);

                            if (!CreateRestoreResultSnapshot(
                                    repository,
                                    selected.Commit,
                                    out string auditError))
                            {
                                LogVersionMaintenanceFailure(
                                    "记录还原结果失败："
                                    + auditError);
                                error = "配置已还原，但记录还原结果失败："
                                    + auditError;
                            }
                            else
                            {
                                versionHistoryChanged = true;
                                error = null;
                            }
                            try
                            {
                                runtime.Editor.History.Clear();
                            }
                            catch (Exception historyError)
                            {
                                LogVersionMaintenanceFailure(
                                    "清理编辑历史失败："
                                    + historyError.Message);
                            }
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        string restoreError = null;
                        try
                        {
                            if (backupComplete
                                && replacementStarted
                                && !string.IsNullOrWhiteSpace(operationRoot))
                            {
                                string backup = Path.Combine(operationRoot, "backup");
                                if (!Directory.Exists(backup))
                                {
                                    throw new DirectoryNotFoundException(
                                        "还原事务备份目录丢失："
                                        + backup);
                                }
                                ReplaceConfiguration(backup);
                                if (transactionState != null)
                                {
                                    transactionState.Status =
                                        "Completed";
                                    WriteRestoreTransactionState(
                                        operationRoot,
                                        transactionState);
                                }
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            restoreError = rollbackEx.Message;
                            preserveOperationForRecovery = true;
                        }

                        string reason = "版本还原失败：" + ex.Message;
                        if (!string.IsNullOrWhiteSpace(restoreError))
                        {
                            reason += "；恢复原文件失败：" + restoreError;
                        }
                        runtime.Safety.Lock(reason);
                        error = reason;
                        return false;
                    }
                    finally
                    {
                        if (!string.IsNullOrWhiteSpace(operationRoot)
                            && !preserveOperationForRecovery)
                        {
                            TryDeleteDirectoryTree(operationRoot);
                        }
                        if (versionHistoryChanged
                            && !TryCompactHistory(
                                false,
                                out string maintenanceError))
                        {
                            LogVersionMaintenanceFailure(
                                maintenanceError);
                        }
                    }
                }
            }
        }

        private Repository OpenRepository()
        {
            EnsureNativeLibraryDirectory();
            string root = GetVersionRoot();
            Directory.CreateDirectory(root);
            if (!Repository.IsValid(root))
            {
                Repository.Init(root);
            }
            Repository repository = new Repository(root);
            repository.Config.Set("core.autocrlf", false, ConfigurationLevel.Local);
            repository.Config.Set("core.filemode", false, ConfigurationLevel.Local);
            return repository;
        }

        private bool TryCompactHistory(bool force, out string error)
        {
            error = null;
            string root = GetVersionRoot();
            if (!Repository.IsValid(root))
            {
                return true;
            }

            string parent = Directory.GetParent(root)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                error = "版本仓库父目录无效。";
                return false;
            }
            string operationId = Guid.NewGuid().ToString("N");
            string compactRoot = Path.Combine(
                parent,
                CompactionDirectoryPrefix + operationId);
            string backupRoot = Path.Combine(
                parent,
                CompactionBackupPrefix + operationId);
            bool compactedRepositoryReady = false;

            try
            {
                EnsureNativeLibraryDirectory();
                using (Repository sourceRepository = new Repository(root))
                {
                    List<Commit> physicalSnapshots =
                        ReadFirstParentHistory(sourceRepository)
                            .Where(commit =>
                                IsVersionSnapshot(commit.MessageShort))
                            .ToList();
                    HashSet<string> deleted =
                        ReadDeletedSnapshotIds(sourceRepository);
                    if (!force
                        && physicalSnapshots.Count
                            < HistoryCompactionThreshold)
                    {
                        return true;
                    }

                    List<Commit> retained = physicalSnapshots
                        .Take(MaxVisibleSnapshots)
                        .Where(commit => !deleted.Contains(commit.Sha))
                        .ToList();

                    Repository.Init(compactRoot);
                    using (Repository compactRepository =
                        new Repository(compactRoot))
                    {
                        compactRepository.Config.Set(
                            "core.autocrlf",
                            false,
                            ConfigurationLevel.Local);
                        compactRepository.Config.Set(
                            "core.filemode",
                            false,
                            ConfigurationLevel.Local);

                        foreach (Commit original in retained
                            .AsEnumerable()
                            .Reverse())
                        {
                            string snapshotRoot = Path.Combine(
                                compactRoot,
                                SnapshotDirectoryName);
                            if (Directory.Exists(snapshotRoot))
                            {
                                DeleteDirectoryTree(snapshotRoot);
                            }
                            Directory.CreateDirectory(snapshotRoot);
                            MaterializeSnapshot(
                                original.Tree,
                                snapshotRoot);
                            Commands.Stage(
                                compactRepository,
                                SnapshotDirectoryName);
                            compactRepository.Commit(
                                original.Message,
                                original.Author,
                                original.Committer,
                                new CommitOptions
                                {
                                    AllowEmptyCommit = true
                                });
                        }

                        List<Commit> rebuilt =
                            ReadFirstParentHistory(
                                compactRepository).ToList();
                        ValidateCompactedHistory(
                            retained,
                            rebuilt);
                    }
                }

                compactedRepositoryReady = true;
                SwapCompactedRepository(
                    root,
                    compactRoot,
                    backupRoot);
                compactedRepositoryReady = false;
                if (Directory.Exists(backupRoot))
                {
                    DeleteDirectoryTree(backupRoot);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "版本历史自动裁剪失败：" + ex.Message;
                return false;
            }
            finally
            {
                if (Directory.Exists(compactRoot)
                    && (!compactedRepositoryReady
                        || Repository.IsValid(root)))
                {
                    TryDeleteDirectoryTree(compactRoot);
                }
            }
        }

        private void ValidateCompactedHistory(
            IReadOnlyList<Commit> retained,
            IReadOnlyList<Commit> rebuilt)
        {
            if (retained.Count != rebuilt.Count)
            {
                throw new InvalidDataException(
                    "裁剪后的版本数量与保留计划不一致。");
            }
            for (int index = 0; index < retained.Count; index++)
            {
                Commit source = retained[index];
                Commit target = rebuilt[index];
                if (!string.Equals(
                    source.Message,
                    target.Message,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "裁剪后的版本说明校验失败，位置："
                        + (index + 1));
                }
                if (source.Author.When != target.Author.When)
                {
                    throw new InvalidDataException(
                        "裁剪后的版本时间校验失败，位置："
                        + (index + 1));
                }
                Dictionary<string, string> sourceFiles =
                    ReadSnapshotFiles(source.Tree);
                Dictionary<string, string> targetFiles =
                    ReadSnapshotFiles(target.Tree);
                if (!AreFileSetsExactlyEqual(sourceFiles, targetFiles))
                {
                    string firstMismatch = sourceFiles.Keys
                        .Union(targetFiles.Keys, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault(path =>
                            !sourceFiles.TryGetValue(path, out string sourceText)
                            || !targetFiles.TryGetValue(path, out string targetText)
                            || !string.Equals(
                                sourceText,
                                targetText,
                                StringComparison.Ordinal));
                    throw new InvalidDataException(
                        "裁剪后的版本文件校验失败，位置："
                        + (index + 1)
                        + "，文件："
                        + (firstMismatch ?? "未知"));
                }
            }
        }

        private static void SwapCompactedRepository(
            string root,
            string compactRoot,
            string backupRoot)
        {
            bool originalMoved = false;
            try
            {
                Directory.Move(root, backupRoot);
                originalMoved = true;
                Directory.Move(compactRoot, root);
            }
            catch
            {
                if (originalMoved
                    && !Directory.Exists(root)
                    && Directory.Exists(backupRoot))
                {
                    Directory.Move(backupRoot, root);
                }
                throw;
            }
        }

        private static void DeleteDirectoryTree(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(
                path,
                "*",
                SearchOption.AllDirectories))
            {
                FileAttributes attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(
                        file,
                        attributes & ~FileAttributes.ReadOnly);
                }
            }
            Directory.Delete(path, true);
        }

        private static void TryDeleteDirectoryTree(string path)
        {
            try
            {
                DeleteDirectoryTree(path);
            }
            catch
            {
            }
        }

        private void LogVersionMaintenanceFailure(string message)
        {
            runtime.ProcessEngine?.Logger?.Log(
                message,
                LogLevel.Error);
        }

        private void RecoverInterruptedVersionOperations()
        {
            RecoverInterruptedCompaction();
            RecoverInterruptedRestore();
        }

        private void RecoverInterruptedCompaction()
        {
            string root = GetVersionRoot();
            string parent = Directory.GetParent(root)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || !Directory.Exists(parent))
            {
                return;
            }

            List<string> backups = Directory.GetDirectories(
                    parent,
                    CompactionBackupPrefix + "*",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(
                    path => Directory.GetLastWriteTimeUtc(path))
                .ToList();
            List<string> compactRoots = Directory.GetDirectories(
                    parent,
                    CompactionDirectoryPrefix + "*",
                    SearchOption.TopDirectoryOnly)
                .ToList();
            if (backups.Count == 0
                && compactRoots.Count == 0)
            {
                return;
            }

            EnsureNativeLibraryDirectory();
            if (!Directory.Exists(root))
            {
                string recoverableBackup = backups.FirstOrDefault(
                    Repository.IsValid);
                if (string.IsNullOrWhiteSpace(recoverableBackup))
                {
                    throw new InvalidDataException(
                        "检测到未完成的历史裁剪，但找不到可恢复的原仓库。");
                }
                Directory.Move(recoverableBackup, root);
                backups.Remove(recoverableBackup);
            }
            if (!Repository.IsValid(root))
            {
                throw new InvalidDataException(
                    "版本仓库损坏，拒绝自动清理历史裁剪临时目录。");
            }

            foreach (string path in backups.Concat(compactRoots))
            {
                DeleteDirectoryTree(path);
            }
        }

        private void RecoverInterruptedRestore()
        {
            string restoreRoot = Path.Combine(
                GetVersionRoot(),
                RestoreDirectoryName);
            if (!Directory.Exists(restoreRoot))
            {
                return;
            }

            foreach (string operationRoot in Directory.GetDirectories(
                restoreRoot,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                string statePath = Path.Combine(
                    operationRoot,
                    RestoreTransactionFileName);
                if (!File.Exists(statePath))
                {
                    throw new InvalidDataException(
                        "发现缺少事务状态的还原残留目录："
                        + operationRoot);
                }
                RestoreTransactionState state =
                    JsonConvert.DeserializeObject<RestoreTransactionState>(
                        File.ReadAllText(statePath, Encoding.UTF8));
                if (state == null
                    || string.IsNullOrWhiteSpace(state.Status))
                {
                    throw new InvalidDataException(
                        "还原事务状态文件格式错误："
                        + statePath);
                }

                if (string.Equals(
                        state.Status,
                        "BackupReady",
                        StringComparison.Ordinal)
                    || string.Equals(
                        state.Status,
                        "Replacing",
                        StringComparison.Ordinal))
                {
                    string backup = Path.Combine(
                        operationRoot,
                        "backup");
                    if (!Directory.Exists(backup))
                    {
                        throw new DirectoryNotFoundException(
                            "还原事务缺少完整备份："
                            + operationRoot);
                    }
                    ValidateStaging(backup);
                    ReplaceConfiguration(backup);
                }
                else if (!string.Equals(
                             state.Status,
                             "Preparing",
                             StringComparison.Ordinal)
                         && !string.Equals(
                             state.Status,
                             "Completed",
                             StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "无法识别的还原事务状态："
                        + state.Status);
                }

                DeleteDirectoryTree(operationRoot);
            }
            if (!Directory.EnumerateFileSystemEntries(
                restoreRoot).Any())
            {
                Directory.Delete(restoreRoot);
            }
        }

        private static void WriteRestoreTransactionState(
            string operationRoot,
            RestoreTransactionState state)
        {
            string path = Path.Combine(
                operationRoot,
                RestoreTransactionFileName);
            string temp = path
                + "."
                + Guid.NewGuid().ToString("N")
                + ".tmp";
            File.WriteAllText(
                temp,
                JsonConvert.SerializeObject(
                    state,
                    Formatting.Indented),
                new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temp, path, null, true);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private static void EnsureNativeLibraryDirectory()
        {
            lock (nativeLibraryLock)
            {
                if (nativeLibraryConfigured)
                {
                    return;
                }
                string architecture = Environment.Is64BitProcess ? "x64" : "x86";
                string nativePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "win32", architecture);
                if (!Directory.Exists(nativePath))
                {
                    throw new InvalidOperationException("未找到 LibGit2Sharp 原生库目录：" + nativePath);
                }
                if (!SetDllDirectory(nativePath))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "注册 LibGit2Sharp 原生库目录失败。");
                }
                nativeLibraryConfigured = true;
            }
        }

        private sealed class SnapshotCommit
        {
            public Commit Commit { get; set; }
        }

        private sealed class RestoreTransactionState
        {
            public string Status { get; set; }

            public string TargetCommitId { get; set; }

            public DateTimeOffset CreatedAt { get; set; }
        }

        private static IReadOnlyList<SnapshotCommit> GetVisibleSnapshots(
            Repository repository)
        {
            return GetVisibleSnapshots(
                repository,
                ReadDeletedSnapshotIds(repository));
        }

        private static IReadOnlyList<SnapshotCommit> GetVisibleSnapshots(
            Repository repository,
            ISet<string> deleted)
        {
            if (repository.Head?.Tip == null)
            {
                return new List<SnapshotCommit>();
            }
            return ReadFirstParentHistory(repository)
                .Where(commit => IsVersionSnapshot(commit.MessageShort))
                .Take(MaxVisibleSnapshots)
                .Where(commit => !deleted.Contains(commit.Sha))
                .Select(commit => new SnapshotCommit { Commit = commit })
                .ToList();
        }

        private static IEnumerable<Commit> ReadFirstParentHistory(
            Repository repository)
        {
            Commit current = repository?.Head?.Tip;
            while (current != null)
            {
                yield return current;
                current = current.Parents.FirstOrDefault();
            }
        }

        private static SnapshotCommit FindVisibleSnapshot(
            Repository repository,
            string commitId)
        {
            if (string.IsNullOrWhiteSpace(commitId))
            {
                return null;
            }
            return GetVisibleSnapshots(repository).FirstOrDefault(item =>
                string.Equals(
                    item.Commit.Sha,
                    commitId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsManualSnapshot(string message)
        {
            return string.Equals(message, "手动快照", StringComparison.Ordinal)
                || (message?.StartsWith("手动快照：", StringComparison.Ordinal) ?? false);
        }

        private static bool IsVersionSnapshot(string message)
        {
            return IsManualSnapshot(message)
                || IsRestoreProtectionSnapshot(message)
                || IsRestoreResultSnapshot(message);
        }

        private static bool IsRestoreProtectionSnapshot(string message)
        {
            return message?.StartsWith("还原前保护点：", StringComparison.Ordinal) ?? false;
        }

        private static bool IsRestoreResultSnapshot(string message)
        {
            return message?.StartsWith("还原结果：", StringComparison.Ordinal) ?? false;
        }

        private static HashSet<string> ReadDeletedSnapshotIds(Repository repository)
        {
            string path = Path.Combine(repository.Info.Path, "automation-deleted-snapshots.json");
            if (!File.Exists(path))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            string json = File.ReadAllText(path, Encoding.UTF8);
            List<string> values = JsonConvert.DeserializeObject<List<string>>(json);
            if (values == null || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length != 40 || !value.All(Uri.IsHexDigit)))
            {
                throw new InvalidDataException("已删除快照索引格式错误。");
            }
            return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        }

        private static void WriteDeletedSnapshotIds(Repository repository, IEnumerable<string> values)
        {
            string path = Path.Combine(repository.Info.Path, "automation-deleted-snapshots.json");
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase), Formatting.Indented), Encoding.UTF8);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null, true);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private string GetVersionRoot()
        {
            return Path.Combine(
                configPath,
                VersionDirectoryName,
                VersionRepositoryName);
        }

        private bool CaptureManualSnapshot(
            Repository repository,
            string message,
            string userName,
            out string error)
        {
            try
            {
                SnapshotCommit latest =
                    GetVisibleSnapshots(repository).FirstOrDefault();
                if (latest != null
                    && AreFileSetsEquivalent(
                        ReadSnapshotFiles(latest.Commit.Tree),
                        ReadCurrentFiles()))
                {
                    error = "当前项目配置与最新快照一致，无需重复创建。";
                    return false;
                }
                MirrorCurrentToSnapshot();
                Commands.Stage(repository, SnapshotDirectoryName);
                Signature signature = CreateSignature(userName);
                repository.Commit(
                    message,
                    signature,
                    signature,
                    new CommitOptions { AllowEmptyCommit = true });
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private bool CreateProtectionSnapshot(
            Repository repository,
            string message,
            out string error)
        {
            try
            {
                MirrorCurrentToSnapshot();
                Commands.Stage(repository, SnapshotDirectoryName);
                Signature signature = CreateSignature(null);
                // 保护点按普通父提交进入 HEAD，保证它与手动快照使用同一时间顺序。
                // 保护点与其他版本一起受“最近 100 个、达到 120 个后裁剪”规则约束。
                repository.Commit(
                    message,
                    signature,
                    signature,
                    new CommitOptions { AllowEmptyCommit = true });
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private bool CreateRestoreResultSnapshot(
            Repository repository,
            Commit targetCommit,
            out string error)
        {
            try
            {
                if (targetCommit == null)
                {
                    throw new ArgumentNullException(
                        nameof(targetCommit));
                }
                MirrorCurrentToSnapshot();
                Commands.Stage(
                    repository,
                    SnapshotDirectoryName);
                Signature signature = CreateSignature(null);
                repository.Commit(
                    "还原结果："
                        + GetRestoreTargetDescription(
                            targetCommit.MessageShort),
                    signature,
                    signature,
                    new CommitOptions
                    {
                        AllowEmptyCommit = true
                    });
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private Signature CreateSignature(string userName)
        {
            string name = string.IsNullOrWhiteSpace(userName) ? Environment.UserName : userName.Trim();
            string emailName = new string(name.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrWhiteSpace(emailName))
            {
                emailName = "automation";
            }
            return new Signature(name, emailName + "@automation.local", DateTimeOffset.Now);
        }

        private void PersistRuntimeBackedConfiguration()
        {
            if (runtime.Stores.Values != null
                && !runtime.Stores.Values.Save(configPath))
            {
                throw new IOException("保存变量定义和持久化运行值失败。");
            }
            if (runtime.Stores.DataStructures != null
                && !runtime.Stores.DataStructures.Save(configPath))
            {
                throw new IOException("保存数据结构配置失败。");
            }
            if (runtime.Stores.Alarms != null
                && !runtime.Stores.Alarms.Save(configPath))
            {
                throw new IOException("保存报警配置失败。");
            }
        }

        private void MirrorCurrentToSnapshot()
        {
            string snapshotRoot = Path.Combine(
                GetVersionRoot(),
                SnapshotDirectoryName);
            if (Directory.Exists(snapshotRoot))
            {
                Directory.Delete(snapshotRoot, true);
            }
            Directory.CreateDirectory(snapshotRoot);
            foreach (string relativePath in GetManagedRelativePaths())
            {
                string source = GetManagedFilePath(relativePath);
                if (!File.Exists(source))
                {
                    continue;
                }
                string target = Path.Combine(snapshotRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (IsHmiRelativePath(relativePath))
                {
                    File.WriteAllText(
                        target,
                        NormalizeSourceText(File.ReadAllText(source, Encoding.UTF8)),
                        new UTF8Encoding(false));
                }
                else
                {
                    File.Copy(source, target, true);
                }
            }
        }

        private Dictionary<string, string> ReadCurrentFiles()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relativePath in GetManagedRelativePaths())
            {
                string fullPath = GetManagedFilePath(relativePath);
                result[relativePath] = File.ReadAllText(fullPath, Encoding.UTF8);
            }
            return result;
        }

        private static bool AreFileSetsEquivalent(
            IDictionary<string, string> beforeFiles,
            IDictionary<string, string> afterFiles)
        {
            if (beforeFiles == null || afterFiles == null
                || beforeFiles.Count != afterFiles.Count)
            {
                return false;
            }
            foreach (string path in beforeFiles.Keys)
            {
                if (!afterFiles.TryGetValue(path, out string afterText)
                    || !AreTextsEquivalent(path, beforeFiles[path], afterText))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AreFileSetsExactlyEqual(
            IDictionary<string, string> beforeFiles,
            IDictionary<string, string> afterFiles)
        {
            return beforeFiles != null
                && afterFiles != null
                && beforeFiles.Count == afterFiles.Count
                && beforeFiles.All(item =>
                    afterFiles.TryGetValue(
                        item.Key,
                        out string afterText)
                    && string.Equals(
                        item.Value,
                        afterText,
                        StringComparison.Ordinal));
        }

        private static string GetRestoreTargetDescription(string message)
        {
            string description = message ?? string.Empty;
            const string prefix = "还原结果：";
            while (description.StartsWith(
                prefix,
                StringComparison.Ordinal))
            {
                description = description.Substring(prefix.Length);
            }
            return string.IsNullOrWhiteSpace(description)
                ? "未命名版本"
                : description;
        }

        private static bool AreTextsEquivalent(
            string path,
            string beforeText,
            string afterText)
        {
            if (ReferenceEquals(beforeText, afterText))
            {
                return true;
            }
            if (beforeText == null || afterText == null)
            {
                return false;
            }
            if (IsHmiRelativePath(path))
            {
                return string.Equals(
                    NormalizeSourceText(beforeText),
                    NormalizeSourceText(afterText),
                    StringComparison.Ordinal);
            }
            if (string.Equals(
                Path.GetExtension(path),
                ".json",
                StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    JToken before = JToken.Parse(beforeText);
                    JToken after = JToken.Parse(afterText);
                    if (AreJsonTokensEquivalent(before, after))
                    {
                        return true;
                    }
                    if (!path.StartsWith(
                            "Work" + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Dictionary<string, string> beforeFields =
                            FlattenBusinessJson(before);
                        Dictionary<string, string> afterFields =
                            FlattenBusinessJson(after);
                        return beforeFields.Count == afterFields.Count
                            && beforeFields.All(item =>
                                afterFields.TryGetValue(
                                    item.Key,
                                    out string value)
                                && string.Equals(
                                    item.Value,
                                    value,
                                    StringComparison.Ordinal));
                    }
                    return false;
                }
                catch (JsonReaderException)
                {
                    // 格式错误必须在差异中暴露，后续严格校验会阻止还原。
                }
            }
            return string.Equals(beforeText, afterText, StringComparison.Ordinal);
        }

        private static bool AreJsonTokensEquivalent(JToken before, JToken after)
        {
            if (before == null || after == null)
            {
                return before == null && after == null;
            }
            if (before.Type != after.Type)
            {
                return false;
            }
            if (before is JObject beforeObject
                && after is JObject afterObject)
            {
                List<JProperty> beforeProperties =
                    beforeObject.Properties().ToList();
                List<JProperty> afterProperties =
                    afterObject.Properties().ToList();
                return beforeProperties.Count == afterProperties.Count
                    && beforeProperties.All(property =>
                        afterObject.TryGetValue(
                            property.Name,
                            StringComparison.Ordinal,
                            out JToken afterValue)
                        && AreJsonTokensEquivalent(
                            property.Value,
                            afterValue));
            }
            if (before is JArray beforeArray
                && after is JArray afterArray)
            {
                return beforeArray.Count == afterArray.Count
                    && beforeArray
                        .Zip(
                            afterArray,
                            (oldValue, newValue) =>
                                AreJsonTokensEquivalent(
                                    oldValue,
                                    newValue))
                        .All(value => value);
            }
            return JToken.DeepEquals(before, after);
        }

        private static string NormalizeSourceText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }
            if (text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static IReadOnlyList<ConfigurationVersionDiffEntry> ConsolidateBusinessDiffs(IReadOnlyList<ConfigurationVersionDiffEntry> entries)
        {
            Dictionary<string, List<ConfigurationVersionDiffEntry>> groups = new Dictionary<string, List<ConfigurationVersionDiffEntry>>(StringComparer.Ordinal);
            foreach (ConfigurationVersionDiffEntry entry in entries)
            {
                if (!IsBusinessObjectChange(entry))
                {
                    continue;
                }
                string key = entry.Category + "\u001f" + entry.Title + "\u001f" + entry.Location + "\u001f" + entry.Target;
                if (!groups.TryGetValue(key, out List<ConfigurationVersionDiffEntry> group))
                {
                    group = new List<ConfigurationVersionDiffEntry>();
                    groups.Add(key, group);
                }
                group.Add(entry);
            }

            List<ConfigurationVersionDiffEntry> result = new List<ConfigurationVersionDiffEntry>();
            foreach (ConfigurationVersionDiffEntry entry in entries)
            {
                if (!IsBusinessObjectChange(entry))
                {
                    result.Add(entry);
                    continue;
                }
                string key = entry.Category + "\u001f" + entry.Title + "\u001f" + entry.Location + "\u001f" + entry.Target;
                List<ConfigurationVersionDiffEntry> group = groups[key];
                if (!ReferenceEquals(entry, group[0]))
                {
                    continue;
                }
                entry.Details = group.Select(item => new ConfigurationVersionFieldDiff
                {
                    ChangeType = item.ChangeType,
                    FieldName = item.FieldName,
                    Before = item.Before,
                    After = item.After
                }).ToList();
                entry.ChangeType = GetConsolidatedChangeType(group);
                entry.FieldName = "共 " + entry.Details.Count + " 项字段变更";
                entry.Before = "双击查看详情";
                entry.After = string.Empty;
                result.Add(entry);
            }
            return result;
        }

        private static bool IsBusinessObjectChange(ConfigurationVersionDiffEntry entry)
        {
            return entry != null && (entry.Category == "流程" || entry.Category == "步骤" || entry.Category == "指令" || entry.Category == "变量");
        }

        private static string GetConsolidatedChangeType(IEnumerable<ConfigurationVersionDiffEntry> entries)
        {
            List<string> types = entries.Select(item => item.ChangeType).Distinct(StringComparer.Ordinal).ToList();
            if (types.Count == 1)
            {
                return types[0];
            }
            return "修改";
        }

        private Dictionary<string, string> ReadSnapshotFiles(Tree tree)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TreeEntry snapshot = tree[SnapshotDirectoryName];
            if (snapshot?.Target is Tree snapshotTree)
            {
                ReadTree(snapshotTree, string.Empty, result);
            }
            return result;
        }

        private static void ReadTree(Tree tree, string prefix, IDictionary<string, string> target)
        {
            foreach (TreeEntry entry in tree)
            {
                string path = string.IsNullOrEmpty(prefix) ? entry.Name : prefix + "/" + entry.Name;
                if (entry.Target is Tree child)
                {
                    ReadTree(child, path, target);
                    continue;
                }
                Blob blob = entry.Target as Blob;
                if (blob == null)
                {
                    continue;
                }
                using (Stream stream = blob.GetContentStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    target[path.Replace('/', Path.DirectorySeparatorChar)] = reader.ReadToEnd();
                }
            }
        }

        private void MaterializeSnapshot(Tree tree, string destination)
        {
            TreeEntry snapshot = tree[SnapshotDirectoryName];
            if (!(snapshot?.Target is Tree snapshotTree))
            {
                throw new InvalidOperationException("版本中不包含配置镜像。");
            }
            MaterializeTree(snapshotTree, destination);
        }

        private static void MaterializeTree(Tree tree, string destination)
        {
            foreach (TreeEntry entry in tree)
            {
                string target = Path.Combine(destination, entry.Name);
                if (entry.Target is Tree child)
                {
                    Directory.CreateDirectory(target);
                    MaterializeTree(child, target);
                    continue;
                }
                Blob blob = entry.Target as Blob;
                if (blob == null)
                {
                    throw new InvalidOperationException("版本包含不支持的 Git 对象。");
                }
                using (Stream input = blob.GetContentStream())
                using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }
            }
        }

        private sealed class IndexedBusinessItem
        {
            public string Key { get; set; }
            public int Index { get; set; }
            public JObject Data { get; set; }
        }

        private sealed class VariableBusinessItem
        {
            public string Key { get; set; }
            public int Index { get; set; }
            public JObject Data { get; set; }
        }

        private IReadOnlyList<ConfigurationVersionDiffEntry> BuildStructuredDiff(
            IDictionary<string, string> beforeFiles,
            IDictionary<string, string> afterFiles)
        {
            List<ConfigurationVersionDiffEntry> result = new List<ConfigurationVersionDiffEntry>();
            BuildProcessDiff(beforeFiles, afterFiles, result);
            BuildEquipmentDiff(beforeFiles, afterFiles, result);
            return ConsolidateBusinessDiffs(result);
        }

        private static void BuildProcessDiff(IDictionary<string, string> beforeFiles, IDictionary<string, string> afterFiles, ICollection<ConfigurationVersionDiffEntry> result)
        {
            IEnumerable<string> flowPaths = beforeFiles.Keys.Union(afterFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(path => path.StartsWith("Work" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            foreach (string path in flowPaths)
            {
                beforeFiles.TryGetValue(path, out string beforeText);
                afterFiles.TryGetValue(path, out string afterText);
                if (AreTextsEquivalent(path, beforeText, afterText))
                {
                    continue;
                }
                JObject before = beforeText == null ? null : JObject.Parse(beforeText);
                JObject after = afterText == null ? null : JObject.Parse(afterText);
                string beforeName = before?["head"]?["Name"]?.Value<string>();
                string afterName = after?["head"]?["Name"]?.Value<string>();
                string flowName = afterName ?? beforeName ?? Path.GetFileNameWithoutExtension(path);
                if (before == null || after == null)
                {
                    JObject existing = after ?? before;
                    int stepCount = ReadCollection(existing["steps"], "流程步骤").Count;
                    result.Add(NewBusinessDiff("流程", before == null ? "新增" : "删除", "流程「" + flowName + "」", "流程", "流程", before == null ? "不存在" : stepCount + " 个步骤", after == null ? "不存在" : stepCount + " 个步骤", path));
                    continue;
                }

                string flowTarget = "flow:" + path;
                AddObjectPropertyChanges(result, "流程", "流程「" + flowName + "」", "流程", flowTarget, before["head"] as JObject, after["head"] as JObject,
                    new HashSet<string>(new[] { "$type", "Id" }, StringComparer.Ordinal));
                CompareSteps(flowName, flowTarget, before, after, result);
            }

            beforeFiles.TryGetValue("value.json", out string beforeVariables);
            afterFiles.TryGetValue("value.json", out string afterVariables);
            if (!AreTextsEquivalent("value.json", beforeVariables, afterVariables))
            {
                CompareVariables(beforeVariables, afterVariables, result);
            }

            beforeFiles.TryGetValue("DataStruct.json", out string beforeStructs);
            afterFiles.TryGetValue("DataStruct.json", out string afterStructs);
            if (!AreTextsEquivalent("DataStruct.json", beforeStructs, afterStructs))
            {
                AddGenericFileChanges("DataStruct.json", beforeStructs, afterStructs, result);
            }

            foreach (string path in beforeFiles.Keys.Union(afterFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(IsHmiRelativePath)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                beforeFiles.TryGetValue(path, out string beforeText);
                afterFiles.TryGetValue(path, out string afterText);
                if (!AreTextsEquivalent(path, beforeText, afterText))
                {
                    AddHmiSourceChange(path, beforeText, afterText, result);
                }
            }
        }

        private static void AddHmiSourceChange(string path, string beforeText, string afterText, ICollection<ConfigurationVersionDiffEntry> result)
        {
            beforeText = beforeText == null ? null : NormalizeSourceText(beforeText);
            afterText = afterText == null ? null : NormalizeSourceText(afterText);
            string changeType = beforeText == null ? "新增" : afterText == null ? "删除" : "修改";
            ConfigurationVersionDiffEntry entry = NewBusinessDiff(
                "HMI 代码",
                changeType,
                "HMI 代码「" + Path.GetFileName(path) + "」",
                "HMI 应用",
                "文件内容",
                GetSourceSummary(beforeText),
                GetSourceSummary(afterText),
                path);
            entry.DetailBefore = beforeText;
            entry.DetailAfter = afterText;
            result.Add(entry);
        }

        private static string GetSourceSummary(string text)
        {
            if (text == null)
            {
                return "不存在";
            }
            int lineCount = text.Length == 0 ? 0 : text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
            return lineCount + " 行";
        }

        private static void CompareSteps(
            string flowName,
            string flowTarget,
            JObject beforeFlow,
            JObject afterFlow,
            ICollection<ConfigurationVersionDiffEntry> result)
        {
            List<IndexedBusinessItem> beforeSteps = BuildIndexedItems(ReadCollection(beforeFlow["steps"], "流程步骤"), "步骤");
            List<IndexedBusinessItem> afterSteps = BuildIndexedItems(ReadCollection(afterFlow["steps"], "流程步骤"), "步骤");
            Dictionary<string, IndexedBusinessItem> beforeMap = beforeSteps.ToDictionary(item => item.Key, StringComparer.Ordinal);
            Dictionary<string, IndexedBusinessItem> afterMap = afterSteps.ToDictionary(item => item.Key, StringComparer.Ordinal);
            foreach (string key in beforeMap.Keys.Union(afterMap.Keys, StringComparer.Ordinal))
            {
                beforeMap.TryGetValue(key, out IndexedBusinessItem beforeStep);
                afterMap.TryGetValue(key, out IndexedBusinessItem afterStep);
                JObject data = afterStep?.Data ?? beforeStep.Data;
                string stepName = data["Name"]?.Value<string>() ?? "未命名步骤";
                string location = "流程「" + flowName + "」 / 步骤「" + stepName + "」";
                string stepTarget = flowTarget + "/step:" + key;
                if (beforeStep == null || afterStep == null)
                {
                    int operationCount = ReadCollection(data["Ops"], "步骤指令").Count;
                    string changeType = beforeStep == null ? "新增" : "删除";
                    result.Add(NewBusinessDiff("步骤", changeType, "步骤「" + stepName + "」", "流程「" + flowName + "」", "步骤", beforeStep == null ? "不存在" : operationCount + " 条指令", afterStep == null ? "不存在" : operationCount + " 条指令", stepTarget));
                    List<IndexedBusinessItem> operations = BuildIndexedItems(ReadCollection(data["Ops"], "步骤指令"), "指令");
                    foreach (IndexedBusinessItem operationItem in operations)
                    {
                        JObject operation = operationItem.Data;
                        string operationName = GetOperationName(operation);
                        result.Add(NewBusinessDiff("指令", changeType, "指令「" + operationName + "」", location, "指令", beforeStep == null ? "不存在" : GetOperationSummary(operation), afterStep == null ? "不存在" : GetOperationSummary(operation), stepTarget + "/op:" + operationItem.Key));
                    }
                    continue;
                }
                if (beforeStep.Index != afterStep.Index)
                {
                    result.Add(NewBusinessDiff("步骤", "移动", "步骤「" + stepName + "」", "流程「" + flowName + "」", "顺序", "第 " + (beforeStep.Index + 1) + " 步", "第 " + (afterStep.Index + 1) + " 步", stepTarget));
                }
                AddObjectPropertyChanges(result, "步骤", "步骤「" + stepName + "」", "流程「" + flowName + "」", stepTarget, beforeStep.Data, afterStep.Data,
                    new HashSet<string>(new[] { "$type", "Id", "Ops" }, StringComparer.Ordinal));
                CompareOperations(flowName, stepName, stepTarget, beforeStep.Data, afterStep.Data, result);
            }
        }

        private static void CompareOperations(
            string flowName,
            string stepName,
            string stepTarget,
            JObject beforeStep,
            JObject afterStep,
            ICollection<ConfigurationVersionDiffEntry> result)
        {
            List<IndexedBusinessItem> beforeOperations = BuildIndexedItems(ReadCollection(beforeStep["Ops"], "步骤指令"), "指令");
            List<IndexedBusinessItem> afterOperations = BuildIndexedItems(ReadCollection(afterStep["Ops"], "步骤指令"), "指令");
            Dictionary<string, IndexedBusinessItem> beforeMap = beforeOperations.ToDictionary(item => item.Key, StringComparer.Ordinal);
            Dictionary<string, IndexedBusinessItem> afterMap = afterOperations.ToDictionary(item => item.Key, StringComparer.Ordinal);
            foreach (string key in beforeMap.Keys.Union(afterMap.Keys, StringComparer.Ordinal))
            {
                beforeMap.TryGetValue(key, out IndexedBusinessItem beforeOperation);
                afterMap.TryGetValue(key, out IndexedBusinessItem afterOperation);
                JObject data = afterOperation?.Data ?? beforeOperation.Data;
                string operationName = GetOperationName(data);
                string location = "流程「" + flowName + "」 / 步骤「" + stepName + "」";
                string operationTarget = stepTarget + "/op:" + key;
                if (beforeOperation == null || afterOperation == null)
                {
                    result.Add(NewBusinessDiff("指令", beforeOperation == null ? "新增" : "删除", "指令「" + operationName + "」", location, "指令",
                        beforeOperation == null ? "不存在" : GetOperationSummary(data), afterOperation == null ? "不存在" : GetOperationSummary(data), operationTarget));
                    continue;
                }
                if (beforeOperation.Index != afterOperation.Index)
                {
                    result.Add(NewBusinessDiff("指令", "移动", "指令「" + operationName + "」", location, "顺序",
                        "第 " + (beforeOperation.Index + 1) + " 条", "第 " + (afterOperation.Index + 1) + " 条", operationTarget));
                }
                AddObjectPropertyChanges(result, "指令", "指令「" + operationName + "」", location, operationTarget, beforeOperation.Data, afterOperation.Data,
                    new HashSet<string>(new[] { "$type", "Id", "Num" }, StringComparer.Ordinal));
            }
        }

        private static void CompareVariables(string beforeText, string afterText, ICollection<ConfigurationVersionDiffEntry> result)
        {
            Dictionary<string, VariableBusinessItem> before =
                ReadVariables(beforeText);
            Dictionary<string, VariableBusinessItem> after =
                ReadVariables(afterText);
            foreach (string key in before.Keys
                .Union(after.Keys, StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal))
            {
                before.TryGetValue(key, out VariableBusinessItem beforeItem);
                after.TryGetValue(key, out VariableBusinessItem afterItem);
                JObject beforeValue = beforeItem?.Data;
                JObject afterValue = afterItem?.Data;
                JObject data = afterValue ?? beforeValue;
                string name = data["Name"]?.Value<string>() ?? "未命名变量";
                int index = afterItem?.Index ?? beforeItem.Index;
                string target = "variable:" + key;
                if (beforeValue == null || afterValue == null)
                {
                    result.Add(NewBusinessDiff("变量", beforeValue == null ? "新增" : "删除", name, "变量索引 " + index, "变量",
                        beforeValue == null ? "不存在" : GetVariableSummary(beforeValue), afterValue == null ? "不存在" : GetVariableSummary(afterValue), target));
                    continue;
                }
                AddObjectPropertyChanges(result, "变量", name, "变量索引 " + index, target, beforeValue, afterValue,
                    new HashSet<string>(new[] { "$type", "Id" }, StringComparer.Ordinal));
            }
        }

        private static void BuildEquipmentDiff(IDictionary<string, string> beforeFiles, IDictionary<string, string> afterFiles, ICollection<ConfigurationVersionDiffEntry> result)
        {
            foreach (string path in beforeFiles.Keys.Union(afterFiles.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                if (IsProcessRelativePath(path))
                {
                    continue;
                }
                beforeFiles.TryGetValue(path, out string beforeText);
                afterFiles.TryGetValue(path, out string afterText);
                if (!AreTextsEquivalent(path, beforeText, afterText))
                {
                    AddGenericFileChanges(path, beforeText, afterText, result);
                }
            }
        }

        private static void AddGenericFileChanges(string path, string beforeText, string afterText, ICollection<ConfigurationVersionDiffEntry> result)
        {
            string category = GetCategory(path);
            if (beforeText == null || afterText == null)
            {
                JToken existing = JToken.Parse(afterText ?? beforeText);
                string summary = GetBusinessCollectionCount(existing) + " 个配置项";
                result.Add(NewBusinessDiff(category, beforeText == null ? "新增" : "删除", category + "配置", path, "配置文件",
                    beforeText == null ? "不存在" : summary, afterText == null ? "不存在" : summary, path));
                return;
            }
            Dictionary<string, string> before = FlattenBusinessJson(JToken.Parse(beforeText));
            Dictionary<string, string> after = FlattenBusinessJson(JToken.Parse(afterText));
            foreach (string field in before.Keys.Union(after.Keys, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
            {
                before.TryGetValue(field, out string oldValue);
                after.TryGetValue(field, out string newValue);
                if (oldValue == newValue)
                {
                    continue;
                }
                result.Add(NewBusinessDiff(category, oldValue == null ? "新增" : newValue == null ? "删除" : "修改", category + "配置", path,
                    FriendlyJsonPath(field), oldValue ?? "不存在", newValue ?? "不存在", path));
            }
        }

        private static void AddObjectPropertyChanges(
            ICollection<ConfigurationVersionDiffEntry> result,
            string category,
            string title,
            string location,
            string target,
            JObject before,
            JObject after,
            ISet<string> excluded)
        {
            if (before == null || after == null)
            {
                throw new InvalidDataException(category + "数据格式错误。");
            }
            IEnumerable<string> names = before.Properties().Select(item => item.Name).Union(after.Properties().Select(item => item.Name), StringComparer.Ordinal);
            foreach (string name in names.Where(item => !excluded.Contains(item)).OrderBy(item => item, StringComparer.Ordinal))
            {
                JToken oldToken = before[name];
                JToken newToken = after[name];
                if (JToken.DeepEquals(oldToken, newToken))
                {
                    continue;
                }
                if ((oldToken is JObject || oldToken is JArray) && (newToken is JObject || newToken is JArray))
                {
                    Dictionary<string, string> beforeFields = FlattenBusinessJson(oldToken);
                    Dictionary<string, string> afterFields = FlattenBusinessJson(newToken);
                    foreach (string field in beforeFields.Keys.Union(afterFields.Keys, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
                    {
                        beforeFields.TryGetValue(field, out string beforeValue);
                        afterFields.TryGetValue(field, out string afterValue);
                        if (beforeValue == afterValue) continue;
                        result.Add(NewBusinessDiff(category, beforeValue == null ? "新增" : afterValue == null ? "删除" : "修改", title, location,
                            GetObjectFieldDisplayName(before, after, name, field), beforeValue ?? "不存在", afterValue ?? "不存在", target));
                    }
                    continue;
                }
                result.Add(NewBusinessDiff(category, oldToken == null ? "新增" : newToken == null ? "删除" : "修改", title, location,
                    FriendlyFieldName(name), FormatBusinessValue(oldToken), FormatBusinessValue(newToken), target));
            }
        }

        private static List<IndexedBusinessItem> BuildIndexedItems(JArray values, string label)
        {
            List<IndexedBusinessItem> result = new List<IndexedBusinessItem>();
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                JObject data = values[i] as JObject ?? throw new InvalidDataException(label + "第 " + i + " 项不是对象。");
                string idText = data["Id"]?.Value<string>();
                string key = Guid.TryParse(idText, out Guid id) && id != Guid.Empty ? id.ToString("D") : "index:" + i;
                if (!keys.Add(key))
                {
                    throw new InvalidDataException(label + "存在重复标识：" + key);
                }
                result.Add(new IndexedBusinessItem { Key = key, Index = i, Data = data });
            }
            return result;
        }

        private static JArray ReadCollection(JToken token, string label)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return new JArray();
            }
            if (token is JArray array)
            {
                return array;
            }
            if (token is JObject wrapper && wrapper["$values"] is JArray values)
            {
                return values;
            }
            throw new InvalidDataException(label + "不是合法集合。");
        }

        private static Dictionary<string, VariableBusinessItem> ReadVariables(string text)
        {
            Dictionary<string, VariableBusinessItem> result =
                new Dictionary<string, VariableBusinessItem>(
                    StringComparer.Ordinal);
            HashSet<int> indexes = new HashSet<int>();
            if (text == null)
            {
                return result;
            }
            JObject root = JObject.Parse(text);
            foreach (JProperty property in root.Properties().Where(item => item.Name != "$type"))
            {
                JObject value = property.Value as JObject ?? throw new InvalidDataException("变量「" + property.Name + "」格式错误。");
                JToken indexToken = value["Index"] ?? throw new InvalidDataException("变量「" + property.Name + "」缺少索引。");
                if (indexToken.Type != JTokenType.Integer)
                {
                    throw new InvalidDataException("变量「" + property.Name + "」索引不是整数。");
                }
                int index = indexToken.Value<int>();
                if (!indexes.Add(index))
                {
                    throw new InvalidDataException("变量索引重复：" + index);
                }
                string idText = value["Id"]?.Value<string>();
                string key = Guid.TryParse(idText, out Guid id)
                    && id != Guid.Empty
                    ? id.ToString("D")
                    : "index:" + index;
                if (result.ContainsKey(key))
                {
                    throw new InvalidDataException("变量标识重复：" + key);
                }
                result.Add(
                    key,
                    new VariableBusinessItem
                    {
                        Key = key,
                        Index = index,
                        Data = value
                    });
            }
            return result;
        }

        private static Dictionary<string, string> FlattenBusinessJson(JToken root)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            FlattenBusinessJson(root, string.Empty, result);
            return result;
        }

        private static void FlattenBusinessJson(JToken token, string path, IDictionary<string, string> result)
        {
            if (token is JObject obj)
            {
                if (obj["$values"] is JArray values)
                {
                    FlattenBusinessJson(values, path, result);
                    return;
                }
                List<JProperty> properties = obj.Properties().Where(item => item.Name != "$type").ToList();
                if (properties.Count == 0)
                {
                    result[string.IsNullOrEmpty(path) ? "配置" : path] = "空";
                }
                foreach (JProperty property in properties)
                {
                    FlattenBusinessJson(property.Value, string.IsNullOrEmpty(path) ? property.Name : path + "." + property.Name, result);
                }
                return;
            }
            if (token is JArray array)
            {
                if (array.Count == 0)
                {
                    result[string.IsNullOrEmpty(path) ? "配置" : path] = "无项目";
                }
                for (int i = 0; i < array.Count; i++)
                {
                    FlattenBusinessJson(
                        array[i],
                        path + "[" + GetStableArrayItemKey(array, i) + "]",
                        result);
                }
                return;
            }
            result[string.IsNullOrEmpty(path) ? "配置" : path] = FormatBusinessValue(token);
        }

        private static string GetStableArrayItemKey(JArray array, int index)
        {
            JObject item = array[index] as JObject;
            if (item != null)
            {
                string id = item["Id"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(id)
                    && array.OfType<JObject>().Count(candidate =>
                        string.Equals(
                            candidate["Id"]?.Value<string>(),
                            id,
                            StringComparison.Ordinal)) == 1)
                {
                    return "Id=" + id;
                }
                string name = item["Name"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(name)
                    && array.OfType<JObject>().Count(candidate =>
                        string.Equals(
                            candidate["Name"]?.Value<string>(),
                            name,
                            StringComparison.Ordinal)) == 1)
                {
                    return "Name=" + name;
                }
            }
            return index.ToString();
        }

        private static ConfigurationVersionDiffEntry NewBusinessDiff(string category, string changeType, string title, string location,
            string fieldName, string before, string after, string target)
        {
            return new ConfigurationVersionDiffEntry
            {
                Category = category,
                ChangeType = changeType,
                Title = title,
                Location = location,
                FieldName = fieldName,
                Before = TrimValue(before),
                After = TrimValue(after),
                Target = target,
                FieldPath = fieldName
            };
        }

        private static string GetOperationName(JObject operation)
        {
            return operation["Name"]?.Value<string>() ?? operation["OperaType"]?.Value<string>() ?? "未命名指令";
        }

        private static string GetOperationSummary(JObject operation)
        {
            string type = operation["OperaType"]?.Value<string>() ?? "未知类型";
            return type + (operation["Disable"]?.Value<bool>() == true ? " · 已禁用" : string.Empty);
        }

        private static string GetVariableSummary(JObject variable)
        {
            return (variable["Type"]?.Value<string>() ?? "未知类型") + " · 值 " + FormatBusinessValue(variable["Value"]);
        }

        private static int GetBusinessCollectionCount(JToken token)
        {
            if (token is JArray array) return array.Count;
            if (token is JObject obj && obj["$values"] is JArray values) return values.Count;
            if (token is JObject dictionary) return dictionary.Properties().Count(item => item.Name != "$type");
            return 1;
        }

        private static string FormatBusinessValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "未设置";
            if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "是" : "否";
            if (token is JValue) return token.ToString();
            if (token is JObject obj && obj["$values"] is JArray wrapped) return wrapped.Count + " 项";
            if (token is JArray array) return array.Count + " 项";
            if (token is JObject businessObject)
            {
                string text = string.Join("；", businessObject.Properties().Where(item => item.Name != "$type").Take(4)
                    .Select(item => FriendlyFieldName(item.Name) + "：" + FormatBusinessValue(item.Value)));
                return string.IsNullOrEmpty(text) ? "空" : text;
            }
            return token.ToString(Formatting.None);
        }

        private static string FriendlyJsonPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "配置";
            string[] segments = path.Split('.');
            return string.Join(" / ", segments.Select(segment =>
            {
                int bracket = segment.IndexOf('[');
                string name = bracket >= 0 ? segment.Substring(0, bracket) : segment;
                string suffix = bracket >= 0 ? segment.Substring(bracket).Replace("[", "第 ").Replace("]", " 项") : string.Empty;
                return FriendlyFieldName(name) + suffix;
            }));
        }

        /// <summary>
        /// 按属性编辑器实际显示的 DisplayName 生成嵌套参数名称，避免把内部 JSON 路径暴露给用户。
        /// </summary>
        private static string GetObjectFieldDisplayName(JObject beforeRoot, JObject afterRoot, string rootField, string nestedPath)
        {
            JToken root = beforeRoot[rootField] ?? afterRoot[rootField];
            List<string> labels = new List<string>
            {
                GetPropertyDisplayName(beforeRoot, afterRoot, rootField)
            };

            foreach (string segment in nestedPath.Split('.'))
            {
                int position = 0;
                int bracket = segment.IndexOf('[');
                string propertyName = bracket >= 0 ? segment.Substring(0, bracket) : segment;
                if (!string.IsNullOrEmpty(propertyName))
                {
                    labels.Add(GetPropertyDisplayName(root, root, propertyName));
                    root = GetPropertyToken(root, propertyName);
                    position = propertyName.Length;
                }

                while (position < segment.Length)
                {
                    int open = segment.IndexOf('[', position);
                    if (open < 0) break;
                    int close = segment.IndexOf(']', open + 1);
                    if (close < 0 || !int.TryParse(segment.Substring(open + 1, close - open - 1), out int index))
                    {
                        return string.Join(" / ", labels);
                    }
                    labels[labels.Count - 1] += "（第 " + (index + 1) + " 项）";
                    root = GetArrayItem(root, index);
                    position = close + 1;
                }
            }
            return string.Join(" / ", labels);
        }

        private static JToken GetPropertyToken(JToken token, string propertyName)
        {
            JObject obj = token as JObject;
            return obj == null ? null : obj[propertyName];
        }

        private static JToken GetArrayItem(JToken token, int index)
        {
            JArray array = token as JArray;
            if (array == null && token is JObject wrapper)
            {
                array = wrapper["$values"] as JArray;
            }
            return array != null && index >= 0 && index < array.Count ? array[index] : null;
        }

        private static string GetPropertyDisplayName(JToken before, JToken after, string propertyName)
        {
            Type type = GetBusinessType(before) ?? GetBusinessType(after);
            PropertyInfo property = type?.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            DisplayNameAttribute displayName = property?.GetCustomAttributes(typeof(DisplayNameAttribute), true)
                .OfType<DisplayNameAttribute>().FirstOrDefault();
            return displayName == null || string.IsNullOrWhiteSpace(displayName.DisplayName)
                ? FriendlyFieldName(propertyName)
                : displayName.DisplayName;
        }

        private static Type GetBusinessType(JToken token)
        {
            string typeName = (token as JObject)?["$type"]?.Value<string>();
            return string.IsNullOrWhiteSpace(typeName) ? null : Type.GetType(typeName, false);
        }

        private static string FriendlyFieldName(string name)
        {
            switch (name)
            {
                case "Name": return "名称";
                case "Type": return "类型";
                case "Value": return "初始值";
                case "Note": return "备注";
                case "Disable": return "禁用";
                case "AutoStart": return "自动启动";
                case "OperaType": return "指令类型";
                case "AlarmType": return "报警类型";
                case "AlarmInfoId": return "报警信息";
                case "IsBreakpoint": return "断点";
                case "Goto1": return "确定跳转";
                case "Goto2": return "否跳转";
                case "Goto3": return "取消跳转";
                case "Index": return "索引";
                case "isMark": return "标记";
                default: return name;
            }
        }

        private void ValidateStaging(string staging)
        {
            foreach (string file in Directory.GetFiles(staging, "*.json", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                JToken.Parse(text);
            }

            string work = Path.Combine(staging, "Work");
            if (Directory.Exists(work))
            {
                foreach (string file in Directory.GetFiles(work, "*.json", SearchOption.TopDirectoryOnly))
                {
                    JsonConvert.DeserializeObject<Proc>(
                        File.ReadAllText(file, Encoding.UTF8),
                        ProcessDefinitionService.CreateStrictJsonSettings());
                }
            }
            string valuePath = Path.Combine(staging, "value.json");
            if (File.Exists(valuePath))
            {
                JsonConvert.DeserializeObject<Dictionary<string, DicValue>>(File.ReadAllText(valuePath, Encoding.UTF8), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto, SerializationBinder = AutomationConfigSerializationBinder.Instance, ObjectCreationHandling = ObjectCreationHandling.Replace });
            }
            string structPath = Path.Combine(staging, "DataStruct.json");
            if (File.Exists(structPath))
            {
                JsonConvert.DeserializeObject<List<DataStruct>>(File.ReadAllText(structPath, Encoding.UTF8), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto, SerializationBinder = AutomationConfigSerializationBinder.Instance, ObjectCreationHandling = ObjectCreationHandling.Replace });
            }

            string hmiPath = Path.Combine(staging, "Hmi");
            if (!Directory.Exists(hmiPath) || !Directory.GetFiles(hmiPath, "*.cs", SearchOption.AllDirectories).Any())
            {
                throw new InvalidDataException("项目版本缺少 HMI 源码，拒绝还原。");
            }
        }

        private void CopyCurrentConfiguration(string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string relativePath in GetManagedRelativePaths())
            {
                string source = GetManagedFilePath(relativePath);
                string target = Path.Combine(destination, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target, true);
            }
        }

        private void ReplaceConfiguration(string sourceRoot)
        {
            HashSet<string> existing = new HashSet<string>(
                GetManagedRelativePaths(),
                StringComparer.OrdinalIgnoreCase);
            foreach (string relativePath in existing)
            {
                string path = GetManagedFilePath(relativePath);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            foreach (string source in Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = GetRelativePath(sourceRoot, source);
                if (!IsSnapshotManagedRelativePath(relativePath))
                {
                    throw new InvalidOperationException("版本包含未受控文件：" + relativePath);
                }
                string target = GetManagedFilePath(relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(source, temp, true);
                if (File.Exists(target))
                {
                    File.Replace(temp, target, null, true);
                }
                else
                {
                    File.Move(temp, target);
                }
            }
        }

        private IEnumerable<string> GetManagedRelativePaths()
        {
            if (Directory.Exists(configPath))
            {
                foreach (string fileName in ManagedRootConfigFiles.OrderBy(
                    item => item,
                    StringComparer.OrdinalIgnoreCase))
                {
                    string path = Path.Combine(configPath, fileName);
                    if (File.Exists(path))
                    {
                        yield return fileName;
                    }
                }

                string workRoot = Path.Combine(configPath, "Work");
                if (Directory.Exists(workRoot))
                {
                    foreach (string path in Directory.GetFiles(
                        workRoot,
                        "*.json",
                        SearchOption.TopDirectoryOnly).OrderBy(
                            item => item,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        yield return "Work"
                            + Path.DirectorySeparatorChar
                            + Path.GetFileName(path);
                    }
                }
            }

            EnsureHmiSourceRoot();
            foreach (string path in Directory.GetFiles(
                hmiSourceRoot,
                "*.*",
                SearchOption.AllDirectories).OrderBy(
                    item => item,
                    StringComparer.OrdinalIgnoreCase))
            {
                if (!IsHmiSourceFile(path))
                {
                    continue;
                }
                yield return "Hmi" + Path.DirectorySeparatorChar + GetRelativePath(hmiSourceRoot, path);
            }

        }

        private string GetManagedFilePath(string relativePath)
        {
            if (IsHmiRelativePath(relativePath))
            {
                EnsureHmiSourceRoot();
                string suffix = relativePath.Substring("Hmi".Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.GetFullPath(Path.Combine(hmiSourceRoot, suffix));
                string root = AppendDirectorySeparator(Path.GetFullPath(hmiSourceRoot));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("HMI 源码路径越界:" + relativePath);
                }
                return target;
            }
            string configTarget = Path.GetFullPath(Path.Combine(configPath, relativePath));
            string configRoot = AppendDirectorySeparator(configPath);
            if (!configTarget.StartsWith(configRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("配置路径越界：" + relativePath);
            }
            return configTarget;
        }

        private static bool IsSnapshotManagedRelativePath(string relativePath)
        {
            return IsManagedRelativePath(relativePath)
                || (IsHmiRelativePath(relativePath)
                    && IsHmiSourceFile(relativePath));
        }

        private static bool IsHmiRelativePath(string relativePath)
        {
            string normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            return normalized.StartsWith("Hmi" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHmiSourceFile(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".resx", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureHmiSourceRoot()
        {
            if (string.IsNullOrWhiteSpace(hmiSourceRoot) || !Directory.Exists(hmiSourceRoot))
            {
                throw new DirectoryNotFoundException(
                    "未找到 HMI 源码目录，项目版本管理不能继续。"
                    + (string.IsNullOrWhiteSpace(hmiSourceError)
                        ? string.Empty
                        : " " + hmiSourceError));
            }
        }

        private static bool IsManagedRelativePath(string relativePath)
        {
            string normalized = relativePath
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            if (normalized.IndexOf(Path.DirectorySeparatorChar) < 0)
            {
                return ManagedRootConfigFiles.Contains(normalized);
            }
            if (!normalized.StartsWith(
                    "Work" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string suffix = normalized.Substring(
                ("Work" + Path.DirectorySeparatorChar).Length);
            return suffix.IndexOf(Path.DirectorySeparatorChar) < 0
                && suffix.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProcessRelativePath(string path)
        {
            string normalized = path
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            return normalized.StartsWith(
                    "Work" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "value.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "DataStruct.json", StringComparison.OrdinalIgnoreCase)
                || IsHmiRelativePath(normalized);
        }

        private static string GetCategory(string path)
        {
            string file = Path.GetFileName(path);
            if (path.StartsWith("Work" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "流程";
            if (string.Equals(file, "value.json", StringComparison.OrdinalIgnoreCase)) return "变量";
            if (string.Equals(file, "value_debug.json", StringComparison.OrdinalIgnoreCase)) return "变量调试值";
            if (IsHmiRelativePath(path)) return "HMI 代码";
            if (string.Equals(file, "DataStruct.json", StringComparison.OrdinalIgnoreCase)) return "数据结构";
            if (string.Equals(file, "DataStation.json", StringComparison.OrdinalIgnoreCase)) return "工站点位";
            if (string.Equals(file, "IOMap.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file, "IODebugMap.json", StringComparison.OrdinalIgnoreCase)) return "IO";
            if (string.Equals(file, "card.json", StringComparison.OrdinalIgnoreCase)) return "控制卡";
            if (string.Equals(file, "AlarmInfo.json", StringComparison.OrdinalIgnoreCase)) return "报警";
            if (file.IndexOf("Plc", StringComparison.OrdinalIgnoreCase) >= 0) return "PLC";
            if (file.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) >= 0 || file.IndexOf("Serial", StringComparison.OrdinalIgnoreCase) >= 0) return "通讯";
            if (string.Equals(file, "AppConfig.json", StringComparison.OrdinalIgnoreCase)) return "应用配置";
            if (string.Equals(file, "GooseConfig.json", StringComparison.OrdinalIgnoreCase)) return "AI 配置";
            return "设备配置";
        }

        private static string TrimValue(string value)
        {
            if (value == null) return null;
            return value.Length <= 200 ? value : value.Substring(0, 200) + "...";
        }

        private static string GetRelativePath(string root, string path)
        {
            Uri rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
            Uri pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
        }
    }
}
