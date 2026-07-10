using LibGit2Sharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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

    public sealed class ConfigurationVersionDiffEntry
    {
        public string Category { get; set; }
        public string Target { get; set; }
        public string FieldPath { get; set; }
        public string ChangeType { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
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
        private readonly HashSet<string> aiProtectedTurns = new HashSet<string>(StringComparer.Ordinal);
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

        public bool EnsureInitialBaseline(ConfigurationVersionLayer layer, out string error)
        {
            lock (syncRoot)
            {
                try
                {
                    using (Repository repository = OpenRepository(layer))
                    {
                        if (repository.Head.Tip != null)
                        {
                            error = null;
                            return true;
                        }
                        return Capture(repository, layer, "初始基线", "系统", true, out error);
                    }
                }
                catch (Exception ex)
                {
                    error = "创建初始基线失败：" + ex.Message;
                    return false;
                }
            }
        }

        public bool CreateManualSnapshot(ConfigurationVersionLayer layer, string note, string userName, out string error)
        {
            lock (syncRoot)
            {
                try
                {
                    using (Repository repository = OpenRepository(layer))
                    {
                        if (repository.Head.Tip == null && !Capture(repository, layer, "初始基线", "系统", true, out error))
                        {
                            return false;
                        }
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

        public bool EnsureAiProtection(ConfigurationVersionLayer layer, string turnId, string userName, out string error)
        {
            if (string.IsNullOrWhiteSpace(turnId))
            {
                error = "AI 变更批次标识为空，拒绝写入配置。";
                return false;
            }

            lock (syncRoot)
            {
                string key = layer + ":" + turnId;
                if (aiProtectedTurns.Contains(key))
                {
                    error = null;
                    return true;
                }

                try
                {
                    using (Repository repository = OpenRepository(layer))
                    {
                        if (repository.Head.Tip == null && !Capture(repository, layer, "初始基线", "系统", true, out error))
                        {
                            return false;
                        }
                        if (!Capture(repository, layer, "AI 保护点：" + turnId, userName, true, out error))
                        {
                            return false;
                        }
                        aiProtectedTurns.Add(key);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    error = "创建 AI 保护点失败：" + ex.Message;
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
                        if (repository.Head.Tip == null)
                        {
                            if (!Capture(repository, layer, "初始基线", "系统", true, out error))
                            {
                                return result;
                            }
                        }
                        MirrorCurrentToSnapshot(layer);
                        currentDirty = repository.RetrieveStatus().IsDirty;
                        foreach (Commit commit in repository.Commits)
                        {
                            result.Add(new ConfigurationVersionRecord
                            {
                                CommitId = commit.Sha,
                                Message = commit.MessageShort,
                                Author = commit.Author.Name,
                                CreatedAt = commit.Author.When,
                                SnapshotType = GetSnapshotType(commit.MessageShort)
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
                        if (selected == null)
                        {
                            throw new InvalidOperationException("找不到选中的版本。");
                        }

                        Dictionary<string, string> before = ReadSnapshotFiles(selected.Tree);
                        Dictionary<string, string> after;
                        if (compareWithPrevious)
                        {
                            Commit previous = selected.Parents.FirstOrDefault();
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

        public bool Restore(ConfigurationVersionLayer layer, string commitId, string userName, Func<bool> allStopped, Action reloadProcess, Action markRestartRequired, out string error)
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
                        if (selected == null)
                        {
                            error = "找不到选中的版本。";
                            return false;
                        }
                        if (repository.Head.Tip == null && !Capture(repository, layer, "初始基线", "系统", true, out error))
                        {
                            return false;
                        }
                        if (!Capture(repository, layer, "还原前保护点", userName, true, out error))
                        {
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

                        if (!Capture(repository, layer, "回滚至版本 " + selected.Sha.Substring(0, 8), userName, true, out error))
                        {
                            throw new InvalidOperationException(error);
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

        private IReadOnlyList<ConfigurationVersionDiffEntry> BuildStructuredDiff(ConfigurationVersionLayer layer, IDictionary<string, string> beforeFiles, IDictionary<string, string> afterFiles)
        {
            List<ConfigurationVersionDiffEntry> result = new List<ConfigurationVersionDiffEntry>();
            foreach (string path in beforeFiles.Keys.Union(afterFiles.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                beforeFiles.TryGetValue(path, out string beforeText);
                afterFiles.TryGetValue(path, out string afterText);
                if (beforeText == afterText)
                {
                    continue;
                }
                if (beforeText == null || afterText == null)
                {
                    result.Add(CreateDiff(layer, path, "$", beforeText == null ? "新增" : "删除", beforeText, afterText));
                    continue;
                }
                JToken before = JToken.Parse(beforeText);
                JToken after = JToken.Parse(afterText);
                Dictionary<string, string> beforeValues = FlattenJson(before);
                Dictionary<string, string> afterValues = FlattenJson(after);
                foreach (string field in beforeValues.Keys.Union(afterValues.Keys, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
                {
                    beforeValues.TryGetValue(field, out string oldValue);
                    afterValues.TryGetValue(field, out string newValue);
                    if (oldValue == newValue)
                    {
                        continue;
                    }
                    string type = oldValue == null ? "新增" : newValue == null ? "删除" : "修改";
                    result.Add(CreateDiff(layer, path, field, type, oldValue, newValue));
                }
            }
            return result;
        }

        private static ConfigurationVersionDiffEntry CreateDiff(ConfigurationVersionLayer layer, string path, string field, string changeType, string before, string after)
        {
            return new ConfigurationVersionDiffEntry
            {
                Category = GetCategory(layer, path),
                Target = path,
                FieldPath = field,
                ChangeType = changeType,
                Before = TrimValue(before),
                After = TrimValue(after)
            };
        }

        private static Dictionary<string, string> FlattenJson(JToken root)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            FlattenJson(root, "$", result);
            return result;
        }

        private static void FlattenJson(JToken token, string path, IDictionary<string, string> result)
        {
            if (token is JObject obj)
            {
                if (!obj.Properties().Any())
                {
                    result[path] = "{}";
                }
                foreach (JProperty property in obj.Properties().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    FlattenJson(property.Value, path + "." + property.Name, result);
                }
                return;
            }
            if (token is JArray array)
            {
                if (!array.Any())
                {
                    result[path] = "[]";
                }
                for (int i = 0; i < array.Count; i++)
                {
                    FlattenJson(array[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", result);
                }
                return;
            }
            result[path] = token.ToString(Formatting.None);
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

        public ConfigurationVersionLayer? GetLayerForPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }
            string fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(configPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            string relative = GetRelativePath(configPath, fullPath);
            if (IsManagedRelativePath(ConfigurationVersionLayer.Process, relative))
            {
                return ConfigurationVersionLayer.Process;
            }
            if (IsManagedRelativePath(ConfigurationVersionLayer.Equipment, relative))
            {
                return ConfigurationVersionLayer.Equipment;
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

        private static string GetSnapshotType(string message)
        {
            if (message.StartsWith("AI 保护点", StringComparison.Ordinal)) return "AI 保护点";
            if (message.StartsWith("还原前保护点", StringComparison.Ordinal)) return "还原前保护点";
            if (message.StartsWith("回滚至版本", StringComparison.Ordinal)) return "回滚";
            if (message.StartsWith("初始基线", StringComparison.Ordinal)) return "初始基线";
            return "手动快照";
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
