using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Automation
{
    public static class GooseRuntimeProvisioner
    {
        public const int SystemPromptVersion = 10;
        private const string PromptResourceName = "Automation.Assets.Goose.system.md";
        private const string VersionFileName = ".automation-system-prompt-version";

        public static string PromptPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Block", "goose", "config", "prompts", "system.md");

        public static string BackupDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Backups", "GooseSystemPrompt");

        public static bool TryEnsureSystemPrompt(out string message)
        {
            message = null;
            try
            {
                string promptDirectory = Path.GetDirectoryName(PromptPath);
                Directory.CreateDirectory(promptDirectory);
                string versionPath = Path.Combine(promptDirectory, VersionFileName);
                int installedVersion = ReadInstalledVersion(versionPath);

                if (installedVersion > SystemPromptVersion)
                {
                    message = $"本机 System Prompt 版本 {installedVersion} 高于程序内置版本 {SystemPromptVersion}，已保留本机版本。";
                    return true;
                }
                if (File.Exists(PromptPath) && installedVersion == SystemPromptVersion)
                {
                    return true;
                }

                if (File.Exists(PromptPath))
                {
                    BackupCurrentPrompt(installedVersion);
                }
                WriteEmbeddedPrompt(PromptPath);
                File.WriteAllText(versionPath, SystemPromptVersion.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
                message = $"System Prompt 已更新到版本 {SystemPromptVersion}。";
                return true;
            }
            catch (Exception ex)
            {
                message = "System Prompt 部署失败，已禁止启动 EW-AI：" + ex.Message;
                return false;
            }
        }

        public static bool TryRestoreLatestBackup(out string message)
        {
            message = null;
            try
            {
                if (!Directory.Exists(BackupDirectory))
                {
                    message = "没有可恢复的 System Prompt 备份。";
                    return false;
                }
                string[] backups = Directory.GetFiles(BackupDirectory, "system_*.md");
                if (backups.Length == 0)
                {
                    message = "没有可恢复的 System Prompt 备份。";
                    return false;
                }
                Array.Sort(backups, StringComparer.OrdinalIgnoreCase);
                string latest = backups[backups.Length - 1];
                if (File.Exists(PromptPath))
                {
                    BackupCurrentPrompt(ReadInstalledVersion(Path.Combine(Path.GetDirectoryName(PromptPath), VersionFileName)));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(PromptPath));
                File.Copy(latest, PromptPath, true);
                File.WriteAllText(
                    Path.Combine(Path.GetDirectoryName(PromptPath), VersionFileName),
                    SystemPromptVersion.ToString(CultureInfo.InvariantCulture),
                    new UTF8Encoding(false));
                message = "已恢复备份：" + latest + "。恢复内容会保留到内置 Prompt 版本升级。";
                return true;
            }
            catch (Exception ex)
            {
                message = "System Prompt 恢复失败：" + ex.Message;
                return false;
            }
        }

        private static int ReadInstalledVersion(string versionPath)
        {
            if (!File.Exists(versionPath)) return 0;
            string text = File.ReadAllText(versionPath, Encoding.UTF8).Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int version) || version <= 0)
            {
                throw new InvalidDataException("System Prompt 版本标记格式无效：" + versionPath);
            }
            return version;
        }

        private static void BackupCurrentPrompt(int version)
        {
            Directory.CreateDirectory(BackupDirectory);
            string path = Path.Combine(BackupDirectory,
                $"system_{DateTime.Now:yyyyMMdd_HHmmss_fff}_v{version}.md");
            File.Copy(PromptPath, path, false);
        }

        private static void WriteEmbeddedPrompt(string destination)
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(PromptResourceName))
            {
                if (source == null) throw new InvalidOperationException("程序内置 System Prompt 资源不存在。");
                string temporary = destination + ".tmp";
                using (FileStream target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(target);
                    target.Flush(true);
                }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
        }
    }
}
