using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Automation
{
    public static class GooseRuntimeProvisioner
    {
        public const int SystemPromptVersion = 19;
        public const int IntegrationContextVersion = 39;
        private const string PromptResourceName = "Automation.Assets.Goose.system.md";
        private const string IntegrationContextResourceName = "Automation.Assets.Goose.automation.md";
        private const string VersionFileName = ".automation-system-prompt-version";
        private const string IntegrationContextVersionFileName = ".automation-context-version";

        public static bool IsManagedContextAvailable { get; private set; }

        public static string PromptPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Block", "goose", "config", "prompts", "system.md");

        public static string BackupDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Backups", "GooseSystemPrompt");

        public static string IntegrationContextPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Goose", "automation.md");

        public static bool TryEnsureManagedContext(out string message)
        {
            message = null;
            IsManagedContextAvailable = false;
            try
            {
                var messages = new System.Collections.Generic.List<string>();
                string promptDirectory = Path.GetDirectoryName(PromptPath);
                Directory.CreateDirectory(promptDirectory);
                string versionPath = Path.Combine(promptDirectory, VersionFileName);
                int installedVersion = ReadInstalledVersion(versionPath);

                if (installedVersion > SystemPromptVersion)
                {
                    if (!File.Exists(PromptPath))
                    {
                        throw new InvalidDataException("System Prompt 版本标记存在，但提示词文件不存在：" + PromptPath);
                    }
                    messages.Add($"本机 System Prompt 版本 {installedVersion} 高于程序内置版本 {SystemPromptVersion}，已保留本机版本。");
                }
                else if (!File.Exists(PromptPath) || installedVersion != SystemPromptVersion)
                {
                    if (File.Exists(PromptPath))
                    {
                        BackupCurrentPrompt(installedVersion);
                    }
                    WriteEmbeddedResource(PromptResourceName, PromptPath);
                    File.WriteAllText(versionPath, SystemPromptVersion.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
                    messages.Add($"System Prompt 已更新到版本 {SystemPromptVersion}。");
                }

                string contextDirectory = Path.GetDirectoryName(IntegrationContextPath);
                Directory.CreateDirectory(contextDirectory);
                string contextVersionPath = Path.Combine(contextDirectory, IntegrationContextVersionFileName);
                int installedContextVersion = ReadInstalledVersion(contextVersionPath);
                if (installedContextVersion > IntegrationContextVersion)
                {
                    if (!File.Exists(IntegrationContextPath))
                    {
                        throw new InvalidDataException("Automation 专用上下文版本标记存在，但上下文文件不存在：" + IntegrationContextPath);
                    }
                    messages.Add($"本机 Automation 专用上下文版本 {installedContextVersion} 高于程序内置版本 {IntegrationContextVersion}，已保留本机版本。");
                }
                else if (!File.Exists(IntegrationContextPath) || installedContextVersion != IntegrationContextVersion)
                {
                    WriteEmbeddedResource(IntegrationContextResourceName, IntegrationContextPath);
                    File.WriteAllText(contextVersionPath, IntegrationContextVersion.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
                    messages.Add($"Automation 专用上下文已更新到版本 {IntegrationContextVersion}。");
                }

                ValidateManagedPromptFiles(installedVersion <= SystemPromptVersion);
                IsManagedContextAvailable = true;
                message = messages.Count == 0 ? null : string.Join(Environment.NewLine, messages);
                return true;
            }
            catch (Exception ex)
            {
                message = "EW-AI 受管上下文部署或校验失败，本次已禁用 EW-AI：" + ex.Message;
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

        private static void ValidateManagedPromptFiles(bool requireCurrentPromptIdentity)
        {
            string systemPrompt = File.ReadAllText(PromptPath, Encoding.UTF8);
            string[] systemAnchors =
            {
                "{% if moim_system_prompt_block is defined %}",
                "# Extensions",
                "extension_tool_limits",
                "# Response Guidelines",
                "# EW-AI Customization"
            };
            string missingSystemAnchor = Array.Find(systemAnchors,
                anchor => systemPrompt.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingSystemAnchor != null)
            {
                throw new InvalidDataException("System Prompt 缺少官方基底或 EW-AI 区块：" + missingSystemAnchor);
            }
            if (requireCurrentPromptIdentity
                && systemPrompt.IndexOf("You are a general-purpose AI agent called EW-AI", StringComparison.Ordinal) < 0)
            {
                throw new InvalidDataException("System Prompt 缺少当前 EW-AI 身份定义。");
            }

            string integrationContext = File.ReadAllText(IntegrationContextPath, Encoding.UTF8);
            string[] contextAnchors =
            {
                "get_semantic_operation_schema",
                "get_native_operation_schemas",
                "preview_change_set",
                "apply_change_set",
                "preview_only",
                "run_proc_test"
            };
            string missingContextAnchor = Array.Find(contextAnchors,
                anchor => integrationContext.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingContextAnchor != null)
            {
                throw new InvalidDataException("Automation 专用上下文缺少当前链路入口：" + missingContextAnchor);
            }

            string[] retiredRoutes =
            {
                "preview_intent", "apply_intent", "preview_patch", "apply_patch", "create_proc_batch"
            };
            string retiredRoute = Array.Find(retiredRoutes,
                route => integrationContext.IndexOf(route, StringComparison.Ordinal) >= 0);
            if (retiredRoute != null)
            {
                throw new InvalidDataException("Automation 专用上下文仍引用旧写入链：" + retiredRoute);
            }
        }

        private static void WriteEmbeddedResource(string resourceName, string destination)
        {
            string directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (source == null)
            {
                string fileName = resourceName.EndsWith("automation.md", StringComparison.Ordinal)
                    ? "automation.md"
                    : "system.md";
                string fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Goose", fileName);
                if (File.Exists(fallbackPath))
                {
                    source = new FileStream(fallbackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
                else
                {
                    throw new InvalidOperationException(
                        "程序内置 Goose 资源及随程序文件均不存在：" + resourceName + "；" + fallbackPath);
                }
            }
            using (source)
            {
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
