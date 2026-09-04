using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

// 模块：运行时 / Machine Agent。
// 职责范围：独立部署、版本化和校验 Machine Agent System Prompt，不读写原 AI 助手 Prompt。

namespace Automation
{
    internal static class MachineAgentPromptProvisioner
    {
        internal const int SystemPromptVersion = 6;
        internal const string PromptResourceName = "Automation.Assets.Goose.machine-agent-system.md";
        private const string VersionFileName = ".machine-agent-system-prompt-version";

        internal static bool IsAvailable { get; private set; }

        internal static string PromptPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "MachineAgent", "Goose", "prompts", "system.md");

        internal static string VersionPath => Path.Combine(
            Path.GetDirectoryName(PromptPath), VersionFileName);

        private static string BackupDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Backups", "MachineAgentSystemPrompt");

        internal static bool TryEnsure(out string message)
        {
            bool succeeded = TryEnsureAtPath(
                PromptPath,
                VersionPath,
                BackupDirectory,
                out message);
            IsAvailable = succeeded;
            return succeeded;
        }

        internal static bool TryEnsureAtPath(
            string promptPath,
            string versionPath,
            string backupDirectory,
            out string message)
        {
            message = null;
            try
            {
                if (string.IsNullOrWhiteSpace(promptPath)
                    || string.IsNullOrWhiteSpace(versionPath)
                    || string.IsNullOrWhiteSpace(backupDirectory))
                {
                    throw new ArgumentException("Machine Agent Prompt 部署路径不能为空。");
                }

                string promptDirectory = Path.GetDirectoryName(Path.GetFullPath(promptPath));
                Directory.CreateDirectory(promptDirectory);
                int installedVersion = ReadInstalledVersion(versionPath);
                if (installedVersion > SystemPromptVersion)
                {
                    if (!File.Exists(promptPath))
                    {
                        throw new InvalidDataException(
                            "Machine Agent System Prompt 版本标记存在，但提示词文件不存在：" + promptPath);
                    }
                    ValidatePrompt(File.ReadAllText(promptPath, Encoding.UTF8));
                    message = $"本机 Machine Agent System Prompt 版本 {installedVersion} 高于程序内置版本 {SystemPromptVersion}，已保留本机版本。";
                    return true;
                }

                if (!File.Exists(promptPath)
                    || installedVersion != SystemPromptVersion
                    || !ManagedFileMatchesResource(promptPath))
                {
                    if (File.Exists(promptPath))
                    {
                        Directory.CreateDirectory(backupDirectory);
                        string backupPath = Path.Combine(
                            backupDirectory,
                            $"machine_agent_system_{DateTime.Now:yyyyMMdd_HHmmss_fff}_v{installedVersion}.md");
                        File.Copy(promptPath, backupPath, false);
                    }
                    WriteEmbeddedResource(promptPath);
                    File.WriteAllText(
                        versionPath,
                        SystemPromptVersion.ToString(CultureInfo.InvariantCulture),
                        new UTF8Encoding(false));
                    message = $"Machine Agent System Prompt 已同步到版本 {SystemPromptVersion}。";
                }

                ValidatePrompt(File.ReadAllText(promptPath, Encoding.UTF8));
                return true;
            }
            catch (Exception ex)
            {
                message = "Machine Agent System Prompt 部署或校验失败：" + ex.Message;
                return false;
            }
        }

        internal static void ValidatePrompt(string prompt)
        {
            string text = prompt ?? string.Empty;
            string[] requiredAnchors =
            {
                "You are the Machine Agent",
                "# Extensions",
                "extension_tool_limits",
                "# Machine Agent Contract",
                "confirmed equipment topology",
                "globally ordered equipment state history",
                "nodeWindow.hasMore",
                "relationWindow.hasMore",
                "perception.lastSuccessfulObservationAtUtc",
                "preview_process_entry_execution",
                "preview_process_stop",
                "confirmed `skillId`",
                "context.topology.nodes[].skills",
                "never choose or override its mode",
                "single_operation",
                "continue_flow",
                "naming conventions are not physical evidence or restart rules",
                "no direct execution"
            };
            string missing = Array.Find(requiredAnchors,
                anchor => text.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missing != null)
            {
                throw new InvalidDataException("Machine Agent System Prompt 缺少隔离契约：" + missing);
            }

            string[] forbiddenAnchors =
            {
                "You are a general-purpose AI agent called EW-AI",
                "automation-process-authoring",
                "automation-process-review",
                "preview_change_set",
                "apply_change_set",
                "request_capability",
                "Ready",
                "Finish"
            };
            string forbidden = Array.Find(forbiddenAnchors,
                anchor => text.IndexOf(anchor, StringComparison.Ordinal) >= 0);
            if (forbidden != null)
            {
                throw new InvalidDataException("Machine Agent System Prompt 混入原 AI 助手契约：" + forbidden);
            }
        }

        private static int ReadInstalledVersion(string versionPath)
        {
            if (!File.Exists(versionPath)) return 0;
            string text = File.ReadAllText(versionPath, Encoding.UTF8).Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int version)
                || version <= 0)
            {
                throw new InvalidDataException(
                    "Machine Agent System Prompt 版本标记格式无效：" + versionPath);
            }
            return version;
        }

        private static bool ManagedFileMatchesResource(string promptPath)
        {
            using (Stream expected = OpenResource())
            using (var actual = new FileStream(promptPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (expected.CanSeek && expected.Length != actual.Length) return false;
                var expectedBuffer = new byte[8192];
                var actualBuffer = new byte[8192];
                while (true)
                {
                    int expectedCount = expected.Read(expectedBuffer, 0, expectedBuffer.Length);
                    int actualCount = actual.Read(actualBuffer, 0, actualBuffer.Length);
                    if (expectedCount != actualCount) return false;
                    if (expectedCount == 0) return true;
                    for (int index = 0; index < expectedCount; index++)
                    {
                        if (expectedBuffer[index] != actualBuffer[index]) return false;
                    }
                }
            }
        }

        private static Stream OpenResource()
        {
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(PromptResourceName);
            if (source != null) return source;
            string fallbackPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "Goose", "machine-agent-system.md");
            if (!File.Exists(fallbackPath))
            {
                throw new InvalidOperationException(
                    "程序内置 Machine Agent System Prompt 及随程序文件均不存在：" + fallbackPath);
            }
            return new FileStream(fallbackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        private static void WriteEmbeddedResource(string promptPath)
        {
            string temporary = promptPath + ".tmp";
            using (Stream source = OpenResource())
            using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
                target.Flush(true);
            }
            if (File.Exists(promptPath)) File.Replace(temporary, promptPath, null);
            else File.Move(temporary, promptPath);
        }
    }
}
