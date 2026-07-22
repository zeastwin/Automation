using Newtonsoft.Json;
// 模块：持久化 / 基础设施。
// 职责范围：提供 JSON 原子读写和跨文件批量提交能力。

using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Automation
{
    /// <summary>
    /// JSON 配置的耐久化单文件存储：临时文件完整落盘后原子替换，并保留上一版备份。
    /// </summary>
    public static class AtomicJsonFileStore
    {
        private static readonly object SaveLock = new object();
        private static readonly LocalFileLogger Logger =
            new LocalFileLogger(@"D:\AutomationLogs\Configuration");
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // 只为多态对象写入 $type；Color 等已知字段类型不写冗余类型元数据。
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = AutomationConfigSerializationBinder.Instance,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public static T Read<T>(string directory, string name, JsonSerializerSettings settings = null)
        {
            string targetPath = BuildPath(directory, name);
            string backupPath = targetPath + ".bak";
            JsonSerializerSettings serializerSettings = settings ?? Settings;
            if (TryRead(targetPath, serializerSettings, out T value, out Exception primaryError))
            {
                return value;
            }
            if (TryRead(backupPath, serializerSettings, out value, out Exception backupError))
            {
                LogError($"主配置损坏，已从备份读取：{targetPath}；{primaryError?.Message}");
                return value;
            }
            if (primaryError != null || backupError != null)
            {
                LogError($"配置读取失败：{targetPath}；primary={primaryError?.Message}；backup={backupError?.Message}");
            }
            return default(T);
        }

        public static bool Save<T>(string directory, string name, T value, JsonSerializerSettings settings = null)
        {
            string targetPath = BuildPath(directory, name);
            string backupPath = targetPath + ".bak";
            string parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }
            string output = JsonConvert.SerializeObject(value, settings ?? Settings);
            Exception lastError = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    lock (SaveLock)
                    {
                        WriteDurable(tempPath, output);
                        if (File.Exists(targetPath))
                        {
                            FileAttributes attributes = File.GetAttributes(targetPath);
                            if ((attributes & FileAttributes.ReadOnly) != 0)
                            {
                                File.SetAttributes(targetPath, attributes & ~FileAttributes.ReadOnly);
                            }
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                            }
                            File.Replace(tempPath, targetPath, backupPath, true);
                        }
                        else
                        {
                            File.Move(tempPath, targetPath);
                        }
                    }
                    return true;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                    break;
                }
                finally
                {
                    TryDelete(tempPath);
                }
                Thread.Sleep(60 * (attempt + 1));
            }
            LogError($"保存配置失败：{targetPath}；{lastError?.Message}");
            return false;
        }

        public static void WriteDurable(string path, string content)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content ?? string.Empty);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private static bool TryRead<T>(string path, JsonSerializerSettings settings, out T value, out Exception error)
        {
            value = default(T);
            error = null;
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    value = JsonConvert.DeserializeObject<T>(reader.ReadToEnd(), settings);
                    return !ReferenceEquals(value, null);
                }
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static string BuildPath(string directory, string name)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name)
                || Path.GetFileName(name) != name)
            {
                throw new ArgumentException("JSON 配置路径或名称无效。");
            }
            return Path.Combine(directory, name + ".json");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void LogError(string message)
        {
            Logger.Log(message, LogLevel.Error);
        }
    }
}
