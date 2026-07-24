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
        public const int IntegrationContextVersion = 45;
        public const int CliIntegrationContextVersion = 4;
        public static int ProcessAuthoringSkillVersion { get; } = ReadBundledProcessAuthoringSkillVersion();
        public static int McpCliSkillVersion { get; } = ReadBundledMcpCliSkillVersion();
        public const string ProcessAuthoringSkillName = "automation-process-authoring";
        public const string McpCliSkillName = "automation-mcp-cli";
        private const string PromptResourceName = "Automation.Assets.Goose.system.md";
        private const string IntegrationContextResourceName = "Automation.Assets.Goose.automation.md";
        private const string CliIntegrationContextResourceName = "Automation.Assets.Goose.automation-cli.md";
        private const string ProcessAuthoringSkillResourceName =
            "Automation.Assets.Goose.Skills.automation-process-authoring.SKILL.md";
        private const string ProcessAuthoringSkillVersionResourceName =
            "Automation.Assets.Goose.Skills.automation-process-authoring.skill-version";
        private const string McpCliSkillResourceName =
            "Automation.Assets.Goose.Skills.automation-mcp-cli.SKILL.md";
        private const string McpCliSkillVersionResourceName =
            "Automation.Assets.Goose.Skills.automation-mcp-cli.skill-version";
        private const string VersionFileName = ".automation-system-prompt-version";
        private const string IntegrationContextVersionFileName = ".automation-context-version";
        private const string CliIntegrationContextVersionFileName = ".automation-cli-context-version";
        private const string ProcessAuthoringSkillVersionFileName = ".automation-skill-version";
        private const string McpCliSkillVersionFileName = ".automation-cli-skill-version";

        public static bool IsManagedContextAvailable { get; private set; }

        public static bool IsProcessAuthoringSkillAvailable { get; private set; }

        public static string ProcessAuthoringSkillPath { get; private set; }

        public static bool IsMcpCliSkillAvailable { get; private set; }

        public static string McpCliSkillPath { get; private set; }

        public static string PromptPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Block", "goose", "config", "prompts", "system.md");

        public static string BackupDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Backups", "GooseSystemPrompt");

        public static string IntegrationContextPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Goose", "automation.md");

        public static string CliIntegrationContextPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Goose", "automation-cli.md");

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
                int installedVersion = ReadInstalledVersion(versionPath, "System Prompt");

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
                int installedContextVersion = ReadInstalledVersion(
                    contextVersionPath,
                    "Automation 专用上下文");
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

                string cliContextVersionPath = Path.Combine(contextDirectory, CliIntegrationContextVersionFileName);
                int installedCliContextVersion = ReadInstalledVersion(
                    cliContextVersionPath,
                    "Automation Cli 专用上下文");
                if (installedCliContextVersion > CliIntegrationContextVersion)
                {
                    if (!File.Exists(CliIntegrationContextPath))
                    {
                        throw new InvalidDataException("Automation Cli 专用上下文版本标记存在，但上下文文件不存在：" + CliIntegrationContextPath);
                    }
                    messages.Add($"本机 Automation Cli 专用上下文版本 {installedCliContextVersion} 高于程序内置版本 {CliIntegrationContextVersion}，已保留本机版本。");
                }
                else if (!File.Exists(CliIntegrationContextPath) || installedCliContextVersion != CliIntegrationContextVersion)
                {
                    WriteEmbeddedResource(CliIntegrationContextResourceName, CliIntegrationContextPath);
                    File.WriteAllText(cliContextVersionPath, CliIntegrationContextVersion.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
                    messages.Add($"Automation Cli 专用上下文已更新到版本 {CliIntegrationContextVersion}。");
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

        public static bool TryEnsureProcessAuthoringSkill(
            string projectWorkingDirectory,
            out string message)
        {
            message = null;
            IsProcessAuthoringSkillAvailable = false;
            ProcessAuthoringSkillPath = null;
            try
            {
                if (string.IsNullOrWhiteSpace(projectWorkingDirectory))
                {
                    throw new ArgumentException("Goose 项目工作目录为空。", nameof(projectWorkingDirectory));
                }

                string projectDirectory = Path.GetFullPath(projectWorkingDirectory);
                if (!Directory.Exists(projectDirectory))
                {
                    throw new DirectoryNotFoundException("Goose 项目工作目录不存在：" + projectDirectory);
                }

                string skillPath = Path.Combine(
                    projectDirectory,
                    ".agents",
                    "skills",
                    ProcessAuthoringSkillName,
                    "SKILL.md");
                string skillDirectory = Path.GetDirectoryName(skillPath);
                Directory.CreateDirectory(skillDirectory);
                string versionPath = Path.Combine(skillDirectory, ProcessAuthoringSkillVersionFileName);
                int installedVersion = ReadInstalledVersion(
                    versionPath,
                    "Automation 流程编写 Skill");

                if (installedVersion > ProcessAuthoringSkillVersion)
                {
                    if (!File.Exists(skillPath))
                    {
                        throw new InvalidDataException(
                            "Automation 流程编写 Skill 版本标记存在，但 SKILL.md 不存在：" + skillPath);
                    }
                    message = $"本机 Automation 流程编写 Skill 版本 {installedVersion} 高于程序内置版本 {ProcessAuthoringSkillVersion}，已保留本机版本。";
                }
                else if (!File.Exists(skillPath) || installedVersion != ProcessAuthoringSkillVersion)
                {
                    WriteEmbeddedResource(ProcessAuthoringSkillResourceName, skillPath);
                    File.WriteAllText(
                        versionPath,
                        ProcessAuthoringSkillVersion.ToString(CultureInfo.InvariantCulture),
                        new UTF8Encoding(false));
                    message = $"Automation 流程编写 Skill 已更新到版本 {ProcessAuthoringSkillVersion}。";
                }

                ValidateProcessAuthoringSkill(skillPath);
                ProcessAuthoringSkillPath = skillPath;
                IsProcessAuthoringSkillAvailable = true;
                return true;
            }
            catch (Exception ex)
            {
                message = "Automation 流程编写 Skill 部署或校验失败：" + ex.Message;
                return false;
            }
        }

        public static string GetProcessAuthoringSkillVersionPath()
        {
            if (string.IsNullOrWhiteSpace(ProcessAuthoringSkillPath))
            {
                return null;
            }
            return Path.Combine(
                Path.GetDirectoryName(ProcessAuthoringSkillPath),
                ProcessAuthoringSkillVersionFileName);
        }

        /// <summary>
        /// 部署 MCP CLI 机制 Skill 到 Goose 项目工作目录的 .agents/skills/，
        /// 仅 Cli 工具接入模式的编辑会话使用；流程编写方法仍由 automation-process-authoring 承担。
        /// </summary>
        public static bool TryEnsureMcpCliSkill(
            string projectWorkingDirectory,
            out string message)
        {
            message = null;
            IsMcpCliSkillAvailable = false;
            McpCliSkillPath = null;
            try
            {
                if (string.IsNullOrWhiteSpace(projectWorkingDirectory))
                {
                    throw new ArgumentException("Goose 项目工作目录为空。", nameof(projectWorkingDirectory));
                }

                string projectDirectory = Path.GetFullPath(projectWorkingDirectory);
                if (!Directory.Exists(projectDirectory))
                {
                    throw new DirectoryNotFoundException("Goose 项目工作目录不存在：" + projectDirectory);
                }

                string skillPath = Path.Combine(
                    projectDirectory,
                    ".agents",
                    "skills",
                    McpCliSkillName,
                    "SKILL.md");
                string skillDirectory = Path.GetDirectoryName(skillPath);
                Directory.CreateDirectory(skillDirectory);
                string versionPath = Path.Combine(skillDirectory, McpCliSkillVersionFileName);
                int installedVersion = ReadInstalledVersion(
                    versionPath,
                    "Automation MCP CLI Skill");

                if (installedVersion > McpCliSkillVersion)
                {
                    if (!File.Exists(skillPath))
                    {
                        throw new InvalidDataException(
                            "Automation MCP CLI Skill 版本标记存在，但 SKILL.md 不存在：" + skillPath);
                    }
                    message = $"本机 Automation MCP CLI Skill 版本 {installedVersion} 高于程序内置版本 {McpCliSkillVersion}，已保留本机版本。";
                }
                else if (!File.Exists(skillPath) || installedVersion != McpCliSkillVersion)
                {
                    WriteEmbeddedResource(McpCliSkillResourceName, skillPath);
                    File.WriteAllText(
                        versionPath,
                        McpCliSkillVersion.ToString(CultureInfo.InvariantCulture),
                        new UTF8Encoding(false));
                    message = $"Automation MCP CLI Skill 已更新到版本 {McpCliSkillVersion}。";
                }

                ValidateMcpCliSkill(skillPath);
                McpCliSkillPath = skillPath;
                IsMcpCliSkillAvailable = true;
                return true;
            }
            catch (Exception ex)
            {
                message = "Automation MCP CLI Skill 部署或校验失败：" + ex.Message;
                return false;
            }
        }

        private static int ReadInstalledVersion(string versionPath, string artifactName)
        {
            if (!File.Exists(versionPath)) return 0;
            string text = File.ReadAllText(versionPath, Encoding.UTF8).Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int version) || version <= 0)
            {
                throw new InvalidDataException(artifactName + " 版本标记格式无效：" + versionPath);
            }
            return version;
        }

        private static int ReadBundledProcessAuthoringSkillVersion()
        {
            Stream source = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(ProcessAuthoringSkillVersionResourceName);
            if (source == null)
            {
                string fallbackPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets",
                    "Goose",
                    "Skills",
                    ProcessAuthoringSkillName,
                    ProcessAuthoringSkillVersionFileName);
                if (!File.Exists(fallbackPath))
                {
                    throw new InvalidOperationException(
                        "程序内置流程编写 Skill 版本资源不存在：" + fallbackPath);
                }
                source = new FileStream(fallbackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            using (source)
            using (var reader = new StreamReader(source, Encoding.UTF8, true, 128, false))
            {
                string text = reader.ReadToEnd().Trim();
                if (!int.TryParse(
                    text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int version)
                    || version <= 0)
                {
                    throw new InvalidDataException("程序内置流程编写 Skill 版本格式无效。");
                }
                return version;
            }
        }

        private static int ReadBundledMcpCliSkillVersion()
        {
            Stream source = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(McpCliSkillVersionResourceName);
            if (source == null)
            {
                string fallbackPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets",
                    "Goose",
                    "Skills",
                    McpCliSkillName,
                    McpCliSkillVersionFileName);
                if (!File.Exists(fallbackPath))
                {
                    throw new InvalidOperationException(
                        "程序内置 MCP CLI Skill 版本资源不存在：" + fallbackPath);
                }
                source = new FileStream(fallbackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            using (source)
            using (var reader = new StreamReader(source, Encoding.UTF8, true, 128, false))
            {
                string text = reader.ReadToEnd().Trim();
                if (!int.TryParse(
                    text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int version)
                    || version <= 0)
                {
                    throw new InvalidDataException("程序内置 MCP CLI Skill 版本格式无效。");
                }
                return version;
            }
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
                "load_skill",
                "automation-process-authoring",
                "get_semantic_operation_schema",
                "get_native_operation_schemas",
                "get_process_design_guide",
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

            string cliIntegrationContext = File.ReadAllText(CliIntegrationContextPath, Encoding.UTF8);
            string[] cliContextAnchors =
            {
                "load_skill",
                "automation-process-authoring",
                "automation-mcp-cli",
                "AUTOMATION_MCP_CLI_PATH",
                "cli list",
                "cli schema",
                "cli call",
                "get_semantic_operation_schema",
                "get_native_operation_schemas",
                "get_process_design_guide",
                "preview_change_set",
                "apply_change_set",
                "preview_only",
                "run_proc_test"
            };
            string missingCliContextAnchor = Array.Find(cliContextAnchors,
                anchor => cliIntegrationContext.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingCliContextAnchor != null)
            {
                throw new InvalidDataException("Automation Cli 专用上下文缺少当前链路入口：" + missingCliContextAnchor);
            }
            string cliRetiredRoute = Array.Find(retiredRoutes,
                route => cliIntegrationContext.IndexOf(route, StringComparison.Ordinal) >= 0);
            if (cliRetiredRoute != null)
            {
                throw new InvalidDataException("Automation Cli 专用上下文仍引用旧写入链：" + cliRetiredRoute);
            }
        }

        private static void ValidateProcessAuthoringSkill(string skillPath)
        {
            string skill = File.ReadAllText(skillPath, Encoding.UTF8);
            string[] anchors =
            {
                "name: automation-process-authoring",
                "description:",
                "# Automation 流程编写",
                "get_process_design_guide",
                "get_semantic_operation_schema",
                "get_native_operation_schemas",
                "preview_change_set",
                "apply_change_set",
                "validate_proc",
                "run_proc_test"
            };
            string missingAnchor = Array.Find(
                anchors,
                anchor => skill.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingAnchor != null)
            {
                throw new InvalidDataException(
                    "Automation 流程编写 Skill 缺少当前工作流入口：" + missingAnchor);
            }
            if (skill.IndexOf("[TODO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidDataException("Automation 流程编写 Skill 仍包含未完成模板标记。");
            }

            string[] retiredRoutes =
            {
                "preview_intent", "apply_intent", "preview_patch", "create_proc_batch"
            };
            string retiredRoute = Array.Find(
                retiredRoutes,
                route => skill.IndexOf(route, StringComparison.Ordinal) >= 0);
            if (retiredRoute != null)
            {
                throw new InvalidDataException(
                    "Automation 流程编写 Skill 仍引用旧写入链：" + retiredRoute);
            }
        }

        private static void ValidateMcpCliSkill(string skillPath)
        {
            string skill = File.ReadAllText(skillPath, Encoding.UTF8);
            string[] anchors =
            {
                "name: automation-mcp-cli",
                "description:",
                "AUTOMATION_MCP_CLI_PATH",
                "cli list",
                "cli schema",
                "cli call",
                "preview_change_set",
                "apply_change_set"
            };
            string missingAnchor = Array.Find(
                anchors,
                anchor => skill.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingAnchor != null)
            {
                throw new InvalidDataException(
                    "Automation MCP CLI Skill 缺少当前调用机制入口：" + missingAnchor);
            }
            if (skill.IndexOf("[TODO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidDataException("Automation MCP CLI Skill 仍包含未完成模板标记。");
            }

            string[] retiredRoutes =
            {
                "preview_intent", "apply_intent", "preview_patch", "create_proc_batch"
            };
            string retiredRoute = Array.Find(
                retiredRoutes,
                route => skill.IndexOf(route, StringComparison.Ordinal) >= 0);
            if (retiredRoute != null)
            {
                throw new InvalidDataException(
                    "Automation MCP CLI Skill 仍引用旧写入链：" + retiredRoute);
            }
        }

        private static void WriteEmbeddedResource(string resourceName, string destination)
        {
            string directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (source == null)
            {
                string fallbackPath;
                if (string.Equals(resourceName, PromptResourceName, StringComparison.Ordinal))
                {
                    fallbackPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Assets",
                        "Goose",
                        "system.md");
                }
                else if (string.Equals(resourceName, IntegrationContextResourceName, StringComparison.Ordinal))
                {
                    fallbackPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Assets",
                        "Goose",
                        "automation.md");
                }
                else if (string.Equals(resourceName, CliIntegrationContextResourceName, StringComparison.Ordinal))
                {
                    fallbackPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Assets",
                        "Goose",
                        "automation-cli.md");
                }
                else if (string.Equals(resourceName, McpCliSkillResourceName, StringComparison.Ordinal))
                {
                    fallbackPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Assets",
                        "Goose",
                        "Skills",
                        McpCliSkillName,
                        "SKILL.md");
                }
                else if (string.Equals(resourceName, ProcessAuthoringSkillResourceName, StringComparison.Ordinal))
                {
                    fallbackPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Assets",
                        "Goose",
                        "Skills",
                        ProcessAuthoringSkillName,
                        "SKILL.md");
                }
                else
                {
                    throw new InvalidOperationException("未知的 Goose 受管资源：" + resourceName);
                }
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
