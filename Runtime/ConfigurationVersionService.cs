using LibGit2Sharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Automation
{
    public enum ConfigurationVersionLayer
    {
        Process,
        Equipment
    }

    public sealed class ConfigurationVersionRecord
    {
        public string CommitId { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string SnapshotType { get; set; }
    }

    public sealed class ConfigurationVersionBranchRecord
    {
        public string Name { get; set; }
        public bool IsCurrent { get; set; }
        public int SnapshotCount { get; set; }
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
    }

    /// <summary>
    /// 运行时配置的私有 Git 版本服务。仓库仅保存 Config 的镜像，绝不触碰开发源码仓库。
    /// </summary>
    public sealed class ConfigurationVersionService
    {
        private const string SnapshotDirectoryName = "Snapshot";
        private const string VersionDirectoryName = ".AutomationVersions";
        private readonly string configPath;
        private readonly object syncRoot = new object();
        private static readonly object nativeLibraryLock = new object();
        private static bool nativeLibraryConfigured;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public ConfigurationVersionService(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("配置目录不能为空。", nameof(configPath));
            }
            this.configPath = Path.GetFullPath(configPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public bool CreateManualSnapshot(ConfigurationVersionLayer layer, string note, string userName, out string error)
        {
            lock (syncRoot)
            {
                try
                {
                    using (Repository repository = OpenRepository(layer))
                    {
                        string message = string.IsNullOrWhiteSpace(note) ? "手动快照" : "手动快照：" + note.Trim();
                        return Capture(repository, layer, message, userName, true, out error);
                    }
                }
                catch (Exception ex)
                {
                    error = "创建手动快照失败：" + ex.Message;
                    return false;
                }
            }
        }

        public IReadOnlyList<ConfigurationVersionRecord> GetHistory(ConfigurationVersionLayer layer, out bool currentDirty, out string error)
        {
            lock (syncRoot)
            {
                currentDirty = false;
                error = null;
                List<ConfigurationVersionRecord> result = new List<ConfigurationVersionRecord>();
                try
                {
                    using (Repository repository = OpenRepository(layer))
                    {
                        MirrorCurrentToSnapshot(layer);
                        currentDirty = repository.RetrieveStatus().IsDirty;
                        HashSet<string> deleted = ReadDeletedSnapshotIds(repository);
                        foreach (Commit commit in GetVisibleCommits(repository, deleted))
                        {
                            result.Add(new ConfigurationVersionRecord
                            {
                                CommitId = commit.Sha,
                                Message = commit.MessageShort,
                                Author = commit.Author.Name,
                                CreatedAt = commit.Author.When,
                                SnapshotType = "手动快照"
                            });
                        }
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    error = "读取版本历史失败：" + ex.Message;
                    return result;
                }
            }
        }

        public IReadOnlyList<ConfigurationVersionBranchRecord> GetBranches(ConfigurationVersionLayer layer, out string currentBranch, out string error)
        {
            lock (syncRoot)
            {
                currentBranch = null;
                error = null;
                List<ConfigurationVersionBranchRecord> result = new List<ConfigurationVersionBranchRecord>();
                try
                {
                    using (Repository repository = OpenRepository(layer))
                    {
                        HashSet<string> deleted = ReadDeletedSnapshotIds(repository);
                        currentBranch = repository.Head?.FriendlyName;
                        foreach (Branch branch in repository.Branches.Where(item => !item.IsRemote))
                        {
                            result.Add(new ConfigurationVersionBranchRecord
                            {
                                Name = branch.FriendlyName,
                                IsCurrent = branch.IsCurrentRepositoryHead,
                                SnapshotCount = branch.Commits.Count(commit => IsManualSnapshot(commit.MessageShort) && !deleted.Contains(commit.Sha))
                            });
                        }
                        return result.OrderByDescending(item => item.IsCurrent).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    }
                }
                catch (Exception ex)
                {
                    error = "读取分支失败：" + ex.Message;
                    return result;
                }
            }
        }

        public bool CreateBranch(ConfigurationVersionLayer layer, string branchName, string startCommitId, out string error)
        {
            lock (syncRoot)
            {
                try
                {
                    ValidateBranchName(branchName);
                    using (Repository repository = OpenRepository(layer))
                    {
                        if (repository.Branches[branchName] != null)
                        {
                            error = "分支已存在：" + branchName;
                            return false;
                        }
                        HashSet<string> deleted = ReadDeletedSnapshotIds(repository);
                        Commit start = string.IsNullOrWhiteSpace(startCommitId)
                            ? GetVisibleCommits(repository, deleted).FirstOrDefault()
                            : repository.Lookup<Commit>(startCommitId);
                        if (start == null || deleted.Contains(start.Sha) || !IsManualSnapshot(start.MessageShort))
                        {
                            error = "请先创建或选择一个有效的手动快照。";
                            return false;
                        }
                        repository.Branches.Add(branchName, start);
                        error = null;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    error = "创建分支失败：" + ex.Message;
                    return false;
                }
            }
        }

        public bool SwitchBranch(ConfigurationVersionLayer layer, string branchName, out string error)
        {
            lock (syncRoot)
            {
                try
                {
                    ValidateBranchName(branchName);
                    using (Repository repository = OpenRepository(layer))
                    {
                        Branch branch = repository.Branches[branchName];
                        if (branch == null || branch.IsRemote)
                        {
                            error = "分支不存在：" + branchName;
                            return false;
                        }
                        if (!branch.IsCurrentRepositoryHead)
                        {
                            Commands.Checkout(repository, branch, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
                        }
                        MirrorCurrentToSnapshot(layer);
                        error = null;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    error = "切换分支失败：" + ex.Message;
                    return false;
                }
            }
        }

        public bool DeleteSnapshot(ConfigurationVersionLayer layer, string commitId, out string error)
        {
            lock (syncRoot)
            {
                try
                {
                    using (Repository repository = OpenRepository(layer))
                    {
                        Commit commit = repository.Lookup<Commit>(commitId);
                        if (commit == null || !repository.Head.Commits.Any(item => item.Sha == commit.Sha) || !IsManualSnapshot(commit.MessageShort))
                        {
                            error = "快照不存在于当前分支。";
                            return false;
                        }
                        HashSet<string> deleted = ReadDeletedSnapshotIds(repository);
                        deleted.Add(commit.Sha);
                        WriteDeletedSnapshotIds(repository, deleted);
                        error = null;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    error = "删除快照失败：" + ex.Message;
                    return false;
                }
            }
        }

        public IReadOnlyList<ConfigurationVersionDiffEntry> GetStructuredDiff(ConfigurationVersionLayer layer, string commitId, bool compareWithPrevious, out string error)
        {
            error = null;
            lock (syncRoot)
            {
                try
                {
                    using (Repository repository = OpenRepository(layer))
                    {
                        Commit selected = repository.Lookup<Commit>(commitId);
                        HashSet<string> deleted = ReadDeletedSnapshotIds(repository);
                        if (selected == null || deleted.Contains(selected.Sha) || !IsManualSnapshot(selected.MessageShort))
                        {
                            throw new InvalidOperationException("找不到选中的版本。");
                        }

                        Dictionary<string, string> before = ReadSnapshotFiles(selected.Tree);
                        Dictionary<string, string> after;
                        if (compareWithPrevious)
                        {
                            Commit previous = GetVisibleCommits(repository, deleted)
                                .SkipWhile(item => item.Sha != selected.Sha)
                                .Skip(1)
                                .FirstOrDefault();
                            Dictionary<string, string> previousFiles = previous == null
                                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                : ReadSnapshotFiles(previous.Tree);
                            return BuildStructuredDiff(layer, previousFiles, before);
                        }

                        after = ReadCurrentFiles(layer);
                        return BuildStructuredDiff(layer, before, after);
                    }
                }
                catch (Exception ex)
                {
                    error = "读取结构化差异失败：" + ex.Message;
                    return new List<ConfigurationVersionDiffEntry>();
                }
            }
        }

        public bool Restore(ConfigurationVersionLayer layer, string commitId, Func<bool> allStopped, Action reloadProcess, Action markRestartRequired, out string error)
        {
            lock (syncRoot)
            {
                string operationRoot = null;
                try
                {
                    if (allStopped == null || !allStopped())
                    {
                        error = "存在未停止、暂停或报警中的流程，拒绝还原版本。";
                        return false;
                    }

                    using (Repository repository = OpenRepository(layer))
                    {
                        Commit selected = repository.Lookup<Commit>(commitId);
                        HashSet<string> deleted = ReadDeletedSnapshotIds(repository);
                        if (selected == null || deleted.Contains(selected.Sha)
                            || !repository.Head.Commits.Any(item => item.Sha == selected.Sha)
                            || !IsManualSnapshot(selected.MessageShort))
                        {
                            error = "找不到选中的版本。";
                            return false;
                        }

                        operationRoot = Path.Combine(GetVersionRoot(layer), "Restore", Guid.NewGuid().ToString("N"));
                        string staging = Path.Combine(operationRoot, "staging");
                        string backup = Path.Combine(operationRoot, "backup");
                        Directory.CreateDirectory(staging);
                        Directory.CreateDirectory(backup);
                        MaterializeSnapshot(selected.Tree, staging);
                        ValidateStaging(layer, staging);
                        CopyCurrentLayer(layer, backup);
                        ReplaceLayer(layer, staging);

                        if (layer == ConfigurationVersionLayer.Process)
                        {
                            if (reloadProcess == null)
                            {
                                throw new InvalidOperationException("工艺层重载入口未配置。");
                            }
                            reloadProcess();
                            if (SF.ProcConfigFaulted)
                            {
                                throw new InvalidOperationException("工艺层重新加载失败。");
                            }
                        }
                        else
                        {
                            if (markRestartRequired == null)
                            {
                                throw new InvalidOperationException("设备层重启锁定入口未配置。");
                            }
                            markRestartRequired();
                        }

                        error = null;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    string restoreError = null;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(operationRoot))
                        {
                            string backup = Path.Combine(operationRoot, "backup");
                            if (Directory.Exists(backup))
                            {
                                ReplaceLayer(layer, backup);
                            }
                        }
                    }
                    catch (Exception rollbackEx)
                    {
                        restoreError = rollbackEx.Message;
                    }

                    string reason = "版本还原失败：" + ex.Message;
                    if (!string.IsNullOrWhiteSpace(restoreError))
                    {
                        reason += "；恢复原文件失败：" + restoreError;
                    }
                    SF.SetSecurityLock(reason);
                    error = reason;
                    return false;
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(operationRoot))
                    {
                        try { Directory.Delete(operationRoot, true); } catch { }
                    }
                }
            }
        }

        private Repository OpenRepository(ConfigurationVersionLayer layer)
        {
            EnsureNativeLibraryDirectory();
            string root = GetVersionRoot(layer);
            Directory.CreateDirectory(root);
            if (!Repository.IsValid(root))
            {
                Repository.Init(root);
            }
            return new Repository(root);
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

        private static IEnumerable<Commit> GetVisibleCommits(Repository repository, ISet<string> deleted)
        {
            if (repository.Head?.Tip == null)
            {
                return Enumerable.Empty<Commit>();
            }
            return repository.Head.Commits.Where(commit => IsManualSnapshot(commit.MessageShort) && !deleted.Contains(commit.Sha));
        }

        private static bool IsManualSnapshot(string message)
        {
            return string.Equals(message, "手动快照", StringComparison.Ordinal)
                || (message?.StartsWith("手动快照：", StringComparison.Ordinal) ?? false);
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

        private static void ValidateBranchName(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName) || branchName.Length > 64 || !string.Equals(branchName, branchName.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("分支名称长度必须为 1-64 个字符，且首尾不能有空格。");
            }
            if (branchName.StartsWith(".", StringComparison.Ordinal) || branchName.StartsWith("/", StringComparison.Ordinal)
                || branchName.EndsWith(".", StringComparison.Ordinal) || branchName.EndsWith("/", StringComparison.Ordinal)
                || branchName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                || branchName.Contains("..") || branchName.Contains("//") || branchName.Contains("@{"))
            {
                throw new ArgumentException("分支名称格式无效。");
            }
            foreach (char value in branchName)
            {
                if (!char.IsLetterOrDigit(value) && value != '-' && value != '_' && value != '.' && value != '/')
                {
                    throw new ArgumentException("分支名称只能包含文字、数字、-、_、. 和 /。");
                }
            }
        }

        private string GetVersionRoot(ConfigurationVersionLayer layer)
        {
            return Path.Combine(configPath, VersionDirectoryName, layer == ConfigurationVersionLayer.Process ? "Process" : "Equipment");
        }

        private bool Capture(Repository repository, ConfigurationVersionLayer layer, string message, string userName, bool allowEmpty, out string error)
        {
            try
            {
                MirrorCurrentToSnapshot(layer);
                Commands.Stage(repository, SnapshotDirectoryName);
                Signature signature = CreateSignature(userName);
                repository.Commit(message, signature, signature, new CommitOptions { AllowEmptyCommit = allowEmpty });
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

        private void MirrorCurrentToSnapshot(ConfigurationVersionLayer layer)
        {
            string snapshotRoot = Path.Combine(GetVersionRoot(layer), SnapshotDirectoryName);
            if (Directory.Exists(snapshotRoot))
            {
                Directory.Delete(snapshotRoot, true);
            }
            Directory.CreateDirectory(snapshotRoot);
            foreach (string relativePath in GetManagedRelativePaths(layer))
            {
                string source = Path.Combine(configPath, relativePath);
                if (!File.Exists(source))
                {
                    continue;
                }
                string target = Path.Combine(snapshotRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target, true);
            }
        }

        private Dictionary<string, string> ReadCurrentFiles(ConfigurationVersionLayer layer)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relativePath in GetManagedRelativePaths(layer))
            {
                string fullPath = Path.Combine(configPath, relativePath);
                result[relativePath] = File.ReadAllText(fullPath, Encoding.UTF8);
            }
            return result;
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

        private IReadOnlyList<ConfigurationVersionDiffEntry> BuildStructuredDiff(ConfigurationVersionLayer layer, IDictionary<string, string> beforeFiles, IDictionary<string, string> afterFiles)
        {
            List<ConfigurationVersionDiffEntry> result = new List<ConfigurationVersionDiffEntry>();
            if (layer == ConfigurationVersionLayer.Process)
            {
                BuildProcessDiff(beforeFiles, afterFiles, result);
            }
            else
            {
                BuildEquipmentDiff(beforeFiles, afterFiles, result);
            }
            return result;
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
                if (beforeText == afterText)
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

                AddObjectPropertyChanges(result, "流程", "流程「" + flowName + "」", "流程", before["head"] as JObject, after["head"] as JObject,
                    new HashSet<string>(new[] { "$type", "Id" }, StringComparer.Ordinal));
                CompareSteps(flowName, before, after, result);
            }

            beforeFiles.TryGetValue("value.json", out string beforeVariables);
            afterFiles.TryGetValue("value.json", out string afterVariables);
            if (beforeVariables != afterVariables)
            {
                CompareVariables(beforeVariables, afterVariables, result);
            }

            beforeFiles.TryGetValue("DataStruct.json", out string beforeStructs);
            afterFiles.TryGetValue("DataStruct.json", out string afterStructs);
            if (beforeStructs != afterStructs)
            {
                AddGenericFileChanges(ConfigurationVersionLayer.Process, "DataStruct.json", beforeStructs, afterStructs, result);
            }
        }

        private static void CompareSteps(string flowName, JObject beforeFlow, JObject afterFlow, ICollection<ConfigurationVersionDiffEntry> result)
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
                if (beforeStep == null || afterStep == null)
                {
                    int operationCount = ReadCollection(data["Ops"], "步骤指令").Count;
                    string changeType = beforeStep == null ? "新增" : "删除";
                    result.Add(NewBusinessDiff("步骤", changeType, "步骤「" + stepName + "」", "流程「" + flowName + "」", "步骤", beforeStep == null ? "不存在" : operationCount + " 条指令", afterStep == null ? "不存在" : operationCount + " 条指令", null));
                    foreach (JObject operation in ReadCollection(data["Ops"], "步骤指令").OfType<JObject>())
                    {
                        string operationName = GetOperationName(operation);
                        result.Add(NewBusinessDiff("指令", changeType, "指令「" + operationName + "」", location, "指令", beforeStep == null ? "不存在" : GetOperationSummary(operation), afterStep == null ? "不存在" : GetOperationSummary(operation), null));
                    }
                    continue;
                }
                if (beforeStep.Index != afterStep.Index)
                {
                    result.Add(NewBusinessDiff("步骤", "移动", "步骤「" + stepName + "」", "流程「" + flowName + "」", "顺序", "第 " + (beforeStep.Index + 1) + " 步", "第 " + (afterStep.Index + 1) + " 步", null));
                }
                AddObjectPropertyChanges(result, "步骤", "步骤「" + stepName + "」", "流程「" + flowName + "」", beforeStep.Data, afterStep.Data,
                    new HashSet<string>(new[] { "$type", "Id", "Ops" }, StringComparer.Ordinal));
                CompareOperations(flowName, stepName, beforeStep.Data, afterStep.Data, result);
            }
        }

        private static void CompareOperations(string flowName, string stepName, JObject beforeStep, JObject afterStep, ICollection<ConfigurationVersionDiffEntry> result)
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
                if (beforeOperation == null || afterOperation == null)
                {
                    result.Add(NewBusinessDiff("指令", beforeOperation == null ? "新增" : "删除", "指令「" + operationName + "」", location, "指令",
                        beforeOperation == null ? "不存在" : GetOperationSummary(data), afterOperation == null ? "不存在" : GetOperationSummary(data), null));
                    continue;
                }
                if (beforeOperation.Index != afterOperation.Index)
                {
                    result.Add(NewBusinessDiff("指令", "移动", "指令「" + operationName + "」", location, "顺序",
                        "第 " + (beforeOperation.Index + 1) + " 条", "第 " + (afterOperation.Index + 1) + " 条", null));
                }
                AddObjectPropertyChanges(result, "指令", "指令「" + operationName + "」", location, beforeOperation.Data, afterOperation.Data,
                    new HashSet<string>(new[] { "$type", "Id", "Num" }, StringComparer.Ordinal));
            }
        }

        private static void CompareVariables(string beforeText, string afterText, ICollection<ConfigurationVersionDiffEntry> result)
        {
            Dictionary<int, JObject> before = ReadVariables(beforeText);
            Dictionary<int, JObject> after = ReadVariables(afterText);
            foreach (int index in before.Keys.Union(after.Keys).OrderBy(value => value))
            {
                before.TryGetValue(index, out JObject beforeValue);
                after.TryGetValue(index, out JObject afterValue);
                JObject data = afterValue ?? beforeValue;
                string name = data["Name"]?.Value<string>() ?? "未命名变量";
                if (beforeValue == null || afterValue == null)
                {
                    result.Add(NewBusinessDiff("变量", beforeValue == null ? "新增" : "删除", name, "变量索引 " + index, "变量",
                        beforeValue == null ? "不存在" : GetVariableSummary(beforeValue), afterValue == null ? "不存在" : GetVariableSummary(afterValue), "value.json"));
                    continue;
                }
                AddObjectPropertyChanges(result, "变量", name, "变量索引 " + index, beforeValue, afterValue,
                    new HashSet<string>(new[] { "$type", "Index" }, StringComparer.Ordinal));
            }
        }

        private static void BuildEquipmentDiff(IDictionary<string, string> beforeFiles, IDictionary<string, string> afterFiles, ICollection<ConfigurationVersionDiffEntry> result)
        {
            foreach (string path in beforeFiles.Keys.Union(afterFiles.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                beforeFiles.TryGetValue(path, out string beforeText);
                afterFiles.TryGetValue(path, out string afterText);
                if (beforeText != afterText)
                {
                    AddGenericFileChanges(ConfigurationVersionLayer.Equipment, path, beforeText, afterText, result);
                }
            }
        }

        private static void AddGenericFileChanges(ConfigurationVersionLayer layer, string path, string beforeText, string afterText, ICollection<ConfigurationVersionDiffEntry> result)
        {
            string category = GetCategory(layer, path);
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

        private static void AddObjectPropertyChanges(ICollection<ConfigurationVersionDiffEntry> result, string category, string title, string location,
            JObject before, JObject after, ISet<string> excluded)
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
                result.Add(NewBusinessDiff(category, oldToken == null ? "新增" : newToken == null ? "删除" : "修改", title, location,
                    FriendlyFieldName(name), FormatBusinessValue(oldToken), FormatBusinessValue(newToken), null));
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

        private static Dictionary<int, JObject> ReadVariables(string text)
        {
            Dictionary<int, JObject> result = new Dictionary<int, JObject>();
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
                if (result.ContainsKey(index))
                {
                    throw new InvalidDataException("变量索引重复：" + index);
                }
                result.Add(index, value);
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
                    FlattenBusinessJson(array[i], path + "[" + i + "]", result);
                }
                return;
            }
            result[string.IsNullOrEmpty(path) ? "配置" : path] = FormatBusinessValue(token);
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
                case "AlarmInfoID": return "报警信息";
                case "isStopPoint": return "断点";
                case "Goto1": return "确定跳转";
                case "Goto2": return "否跳转";
                case "Goto3": return "取消跳转";
                case "Index": return "索引";
                case "isMark": return "标记";
                default: return name;
            }
        }

        private void ValidateStaging(ConfigurationVersionLayer layer, string staging)
        {
            foreach (string file in Directory.GetFiles(staging, "*.json", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                JToken.Parse(text);
            }

            if (layer != ConfigurationVersionLayer.Process)
            {
                return;
            }
            string work = Path.Combine(staging, "Work");
            if (Directory.Exists(work))
            {
                foreach (string file in Directory.GetFiles(work, "*.json", SearchOption.TopDirectoryOnly))
                {
                    JsonConvert.DeserializeObject<Proc>(File.ReadAllText(file, Encoding.UTF8), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All, ObjectCreationHandling = ObjectCreationHandling.Replace });
                }
            }
            string valuePath = Path.Combine(staging, "value.json");
            if (File.Exists(valuePath))
            {
                JsonConvert.DeserializeObject<Dictionary<string, DicValue>>(File.ReadAllText(valuePath, Encoding.UTF8), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All, ObjectCreationHandling = ObjectCreationHandling.Replace });
            }
            string structPath = Path.Combine(staging, "DataStruct.json");
            if (File.Exists(structPath))
            {
                JsonConvert.DeserializeObject<List<DataStruct>>(File.ReadAllText(structPath, Encoding.UTF8), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All, ObjectCreationHandling = ObjectCreationHandling.Replace });
            }
        }

        private void CopyCurrentLayer(ConfigurationVersionLayer layer, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string relativePath in GetManagedRelativePaths(layer))
            {
                string source = Path.Combine(configPath, relativePath);
                string target = Path.Combine(destination, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target, true);
            }
        }

        private void ReplaceLayer(ConfigurationVersionLayer layer, string sourceRoot)
        {
            HashSet<string> existing = new HashSet<string>(GetManagedRelativePaths(layer), StringComparer.OrdinalIgnoreCase);
            foreach (string relativePath in existing)
            {
                string path = Path.Combine(configPath, relativePath);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            foreach (string source in Directory.GetFiles(sourceRoot, "*.json", SearchOption.AllDirectories))
            {
                string relativePath = GetRelativePath(sourceRoot, source);
                string target = Path.Combine(configPath, relativePath);
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

        private IEnumerable<string> GetManagedRelativePaths(ConfigurationVersionLayer layer)
        {
            if (!Directory.Exists(configPath))
            {
                yield break;
            }
            foreach (string path in Directory.GetFiles(configPath, "*.json", SearchOption.AllDirectories))
            {
                string relative = GetRelativePath(configPath, path);
                if (IsManagedRelativePath(layer, relative))
                {
                    yield return relative;
                }
            }
        }

        private static bool IsManagedRelativePath(ConfigurationVersionLayer layer, string relativePath)
        {
            string normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (normalized.StartsWith(VersionDirectoryName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Logs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Cache" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || normalized.IndexOf("\\Logs\\", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("\\Cache\\", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("\\Temp\\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            bool isProcess = normalized.StartsWith("Work" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "value.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "DataStruct.json", StringComparison.OrdinalIgnoreCase);
            if (layer == ConfigurationVersionLayer.Process)
            {
                return isProcess;
            }
            return !isProcess && !string.Equals(normalized, "Account.json", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetCategory(ConfigurationVersionLayer layer, string path)
        {
            string file = Path.GetFileName(path);
            if (layer == ConfigurationVersionLayer.Process)
            {
                if (path.StartsWith("Work" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "流程";
                if (string.Equals(file, "value.json", StringComparison.OrdinalIgnoreCase)) return "变量";
                return "数据结构";
            }
            if (string.Equals(file, "DataStation.json", StringComparison.OrdinalIgnoreCase)) return "工站点位";
            if (string.Equals(file, "IOMap.json", StringComparison.OrdinalIgnoreCase)) return "IO";
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
