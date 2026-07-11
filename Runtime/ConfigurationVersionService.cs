using LibGit2Sharp;
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
    /// 运行时配置的私有 Git 版本服务。工艺层同时保存流程配置和 HMI 源码镜像。
    /// </summary>
    public sealed class ConfigurationVersionService
    {
        private const string SnapshotDirectoryName = "Snapshot";
        private const string VersionDirectoryName = ".AutomationVersions";
        private readonly string configPath;
        private readonly string hmiSourceRoot;
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
            hmiSourceRoot = ResolveHmiSourceRoot();
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
                            error = "快照不存在于版本历史中。";
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
                string source = GetManagedFilePath(layer, relativePath);
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
                string fullPath = GetManagedFilePath(layer, relativePath);
                result[relativePath] = File.ReadAllText(fullPath, Encoding.UTF8);
            }
            return result;
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

            foreach (string path in beforeFiles.Keys.Union(afterFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(IsHmiRelativePath)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                beforeFiles.TryGetValue(path, out string beforeText);
                afterFiles.TryGetValue(path, out string afterText);
                if (beforeText != afterText)
                {
                    AddHmiSourceChange(path, beforeText, afterText, result);
                }
            }
        }

        private static void AddHmiSourceChange(string path, string beforeText, string afterText, ICollection<ConfigurationVersionDiffEntry> result)
        {
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
                            GetObjectFieldDisplayName(before, after, name, field), beforeValue ?? "不存在", afterValue ?? "不存在", null));
                    }
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
                    JsonConvert.DeserializeObject<Proc>(File.ReadAllText(file, Encoding.UTF8), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All, SerializationBinder = AutomationConfigSerializationBinder.Instance, ObjectCreationHandling = ObjectCreationHandling.Replace });
                }
            }
            string valuePath = Path.Combine(staging, "value.json");
            if (File.Exists(valuePath))
            {
                JsonConvert.DeserializeObject<Dictionary<string, DicValue>>(File.ReadAllText(valuePath, Encoding.UTF8), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All, SerializationBinder = AutomationConfigSerializationBinder.Instance, ObjectCreationHandling = ObjectCreationHandling.Replace });
            }
            string structPath = Path.Combine(staging, "DataStruct.json");
            if (File.Exists(structPath))
            {
                JsonConvert.DeserializeObject<List<DataStruct>>(File.ReadAllText(structPath, Encoding.UTF8), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All, SerializationBinder = AutomationConfigSerializationBinder.Instance, ObjectCreationHandling = ObjectCreationHandling.Replace });
            }

            string hmiPath = Path.Combine(staging, "Hmi");
            if (!Directory.Exists(hmiPath) || !Directory.GetFiles(hmiPath, "*.cs", SearchOption.AllDirectories).Any())
            {
                throw new InvalidDataException("工艺层版本缺少 HMI 源码，拒绝还原。");
            }
        }

        private void CopyCurrentLayer(ConfigurationVersionLayer layer, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string relativePath in GetManagedRelativePaths(layer))
            {
                string source = GetManagedFilePath(layer, relativePath);
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
                string path = GetManagedFilePath(layer, relativePath);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            foreach (string source in Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = GetRelativePath(sourceRoot, source);
                if (!IsSnapshotManagedRelativePath(layer, relativePath))
                {
                    throw new InvalidOperationException("版本包含未受控文件:" + relativePath);
                }
                string target = GetManagedFilePath(layer, relativePath);
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
            if (Directory.Exists(configPath))
            {
                foreach (string path in Directory.GetFiles(configPath, "*.json", SearchOption.AllDirectories))
                {
                    string relative = GetRelativePath(configPath, path);
                    if (IsManagedRelativePath(layer, relative))
                    {
                        yield return relative;
                    }
                }
            }

            if (layer != ConfigurationVersionLayer.Process)
            {
                yield break;
            }

            EnsureHmiSourceRoot();
            foreach (string path in Directory.GetFiles(hmiSourceRoot, "*.*", SearchOption.AllDirectories))
            {
                if (!IsHmiSourceFile(path))
                {
                    continue;
                }
                yield return "Hmi" + Path.DirectorySeparatorChar + GetRelativePath(hmiSourceRoot, path);
            }

        }

        private string GetManagedFilePath(ConfigurationVersionLayer layer, string relativePath)
        {
            if (layer == ConfigurationVersionLayer.Process && IsHmiRelativePath(relativePath))
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
            return Path.Combine(configPath, relativePath);
        }

        private static bool IsSnapshotManagedRelativePath(ConfigurationVersionLayer layer, string relativePath)
        {
            return IsManagedRelativePath(layer, relativePath)
                || (layer == ConfigurationVersionLayer.Process && IsHmiRelativePath(relativePath) && IsHmiSourceFile(relativePath));
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
                throw new DirectoryNotFoundException("未找到 Hmi 源码目录，工艺层版本管理不能继续。");
            }
        }

        private string ResolveHmiSourceRoot()
        {
            return FindHmiSourceRoot(configPath) ?? FindHmiSourceRoot(AppDomain.CurrentDomain.BaseDirectory);
        }

        private static string FindHmiSourceRoot(string startPath)
        {
            string current = startPath;
            for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
            {
                string projectFile = Path.Combine(current, "Automation.csproj");
                string candidate = Path.Combine(current, "Hmi");
                if (File.Exists(projectFile) && Directory.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
                DirectoryInfo parent = Directory.GetParent(current);
                current = parent?.FullName;
            }
            return null;
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
                if (IsHmiRelativePath(path)) return "HMI 代码";
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
