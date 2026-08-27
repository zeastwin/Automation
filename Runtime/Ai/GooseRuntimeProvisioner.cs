using System;
// 模块：运行时 / AI 集成。
// 职责范围：管理 AI 会话、配置、ACP/MCP 进程、受管运行环境和分析记录。

using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Automation
{
    public static class GooseRuntimeProvisioner
    {
        public const int SystemPromptVersion = 21;
        public const int IntegrationContextVersion = 75;
        public const string ProcessAuthoringSkillName = "automation-process-authoring";
        public const string ProcessReviewSkillName = "automation-process-review";
        public static int ProcessAuthoringSkillVersion { get; } = ReadBundledSkillVersion(
            ProcessAuthoringSkillVersionResourceName,
            "Automation 流程编写 Skill");
        public static int ProcessReviewSkillVersion { get; } = ReadBundledSkillVersion(
            ProcessReviewSkillVersionResourceName,
            "Automation 流程评审 Skill");
        private const string PromptResourceName = "Automation.Assets.Goose.system.md";
        private const string IntegrationContextResourceName = "Automation.Assets.Goose.automation.md";
        private const string ProcessAuthoringSkillResourceName =
            "Automation.Assets.Goose.Skills.automation-process-authoring.SKILL.md";
        private const string ProcessAuthoringSkillVersionResourceName =
            "Automation.Assets.Goose.Skills.automation-process-authoring.skill-version";
        private const string ProcessReviewSkillResourceName =
            "Automation.Assets.Goose.Skills.automation-process-review.SKILL.md";
        private const string ProcessReviewSkillVersionResourceName =
            "Automation.Assets.Goose.Skills.automation-process-review.skill-version";
        private const string VersionFileName = ".automation-system-prompt-version";
        private const string IntegrationContextVersionFileName = ".automation-context-version";
        private const string ProcessAuthoringSkillVersionFileName = ".automation-skill-version";

        public static bool IsManagedContextAvailable { get; private set; }

        public static bool IsProcessAuthoringSkillAvailable { get; private set; }

        public static string ProcessAuthoringSkillPath { get; private set; }

        public static bool IsProcessReviewSkillAvailable { get; private set; }

        public static string ProcessReviewSkillPath { get; private set; }

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
                int installedVersion = ReadInstalledVersion(versionPath, "System Prompt");

                if (installedVersion > SystemPromptVersion)
                {
                    if (!File.Exists(PromptPath))
                    {
                        throw new InvalidDataException("System Prompt 版本标记存在，但提示词文件不存在：" + PromptPath);
                    }
                    messages.Add($"本机 System Prompt 版本 {installedVersion} 高于程序内置版本 {SystemPromptVersion}，已保留本机版本。");
                }
                else if (!File.Exists(PromptPath)
                    || installedVersion != SystemPromptVersion
                    || !ManagedFileMatchesResource(PromptResourceName, PromptPath))
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
                else if (!File.Exists(IntegrationContextPath)
                    || installedContextVersion != IntegrationContextVersion
                    || !ManagedFileMatchesResource(IntegrationContextResourceName, IntegrationContextPath))
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

        public static bool TryEnsureProcessSkills(
            string projectWorkingDirectory,
            out string message)
        {
            message = null;
            IsProcessAuthoringSkillAvailable = false;
            ProcessAuthoringSkillPath = null;
            IsProcessReviewSkillAvailable = false;
            ProcessReviewSkillPath = null;
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

                var messages = new System.Collections.Generic.List<string>();
                ProcessAuthoringSkillPath = EnsureManagedSkill(
                    projectDirectory,
                    ProcessAuthoringSkillName,
                    "Automation 流程编写 Skill",
                    ProcessAuthoringSkillResourceName,
                    ProcessAuthoringSkillVersion,
                    ValidateProcessAuthoringSkill,
                    messages);
                ProcessReviewSkillPath = EnsureManagedSkill(
                    projectDirectory,
                    ProcessReviewSkillName,
                    "Automation 流程评审 Skill",
                    ProcessReviewSkillResourceName,
                    ProcessReviewSkillVersion,
                    ValidateProcessReviewSkill,
                    messages);
                IsProcessAuthoringSkillAvailable = true;
                IsProcessReviewSkillAvailable = true;
                message = messages.Count == 0 ? null : string.Join(Environment.NewLine, messages);
                return true;
            }
            catch (Exception ex)
            {
                IsProcessAuthoringSkillAvailable = false;
                ProcessAuthoringSkillPath = null;
                IsProcessReviewSkillAvailable = false;
                ProcessReviewSkillPath = null;
                message = "Automation 流程 Skill 部署或校验失败：" + ex.Message;
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

        public static string GetProcessReviewSkillVersionPath()
        {
            if (string.IsNullOrWhiteSpace(ProcessReviewSkillPath))
            {
                return null;
            }
            return Path.Combine(
                Path.GetDirectoryName(ProcessReviewSkillPath),
                ProcessAuthoringSkillVersionFileName);
        }

        private static string EnsureManagedSkill(
            string projectDirectory,
            string skillName,
            string artifactName,
            string resourceName,
            int bundledVersion,
            Action<string> validator,
            System.Collections.Generic.ICollection<string> messages)
        {
            string skillPath = Path.Combine(
                projectDirectory,
                ".agents",
                "skills",
                skillName,
                "SKILL.md");
            string skillDirectory = Path.GetDirectoryName(skillPath);
            Directory.CreateDirectory(skillDirectory);
            string versionPath = Path.Combine(skillDirectory, ProcessAuthoringSkillVersionFileName);
            int installedVersion = ReadInstalledVersion(versionPath, artifactName);

            if (installedVersion > bundledVersion)
            {
                if (!File.Exists(skillPath))
                {
                    throw new InvalidDataException(
                        artifactName + " 版本标记存在，但 SKILL.md 不存在：" + skillPath);
                }
                messages.Add($"本机 {artifactName} 版本 {installedVersion} 高于程序内置版本 {bundledVersion}，已保留本机版本。");
            }
            else if (!File.Exists(skillPath)
                || installedVersion != bundledVersion
                || !ManagedFileMatchesResource(resourceName, skillPath))
            {
                WriteEmbeddedResource(resourceName, skillPath);
                File.WriteAllText(
                    versionPath,
                    bundledVersion.ToString(CultureInfo.InvariantCulture),
                    new UTF8Encoding(false));
                messages.Add($"{artifactName} 已同步到版本 {bundledVersion}。");
            }

            validator(skillPath);
            return skillPath;
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

        private static int ReadBundledSkillVersion(string resourceName, string artifactName)
        {
            Stream source = OpenManagedResource(resourceName);
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
                    throw new InvalidDataException("程序内置" + artifactName + "版本格式无效。");
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
                "# EW-AI Customization",
                "Clearly distinguish verified facts, inferences, and unresolved information",
                "For industrial runtime safety"
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
                "平台全景",
                "Proc→Step→OperationType",
                "自动化配置与运行软件",
                "常驻所有能力面",
                "automation-process-authoring",
                "automation-process-review",
                "diagnose_issue",
                "get_platform_development_context",
                "get_process_design_guide",
                "list_authoring_resources",
                "只要求“设计、方案、结构或怎么写”",
                "不预猜名称"
            };
            string missingContextAnchor = Array.Find(contextAnchors,
                anchor => integrationContext.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingContextAnchor != null)
            {
                throw new InvalidDataException("Automation 专用上下文缺少当前链路入口：" + missingContextAnchor);
            }

            string[] retiredRoutes =
            {
                "preview_intent", "apply_intent", "preview_patch", "apply_patch", "create_proc_batch",
                "resolve_authoring_inputs", "discover_project_resources"
            };
            string retiredRoute = Array.Find(retiredRoutes,
                route => integrationContext.IndexOf(route, StringComparison.Ordinal) >= 0);
            if (retiredRoute != null)
            {
                throw new InvalidDataException("Automation 专用上下文仍引用旧写入链：" + retiredRoute);
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
                "list_authoring_resources",
                "resolve_operation_capability",
                "preview_change_set",
                "apply_change_set",
                "validate_proc",
                "config.placeholder",
                "operation.replace"
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
                "preview_intent", "apply_intent", "preview_patch", "create_proc_batch",
                "preview_process_blueprint", "blueprintEvidence", "retries[]",
                "resolve_authoring_inputs", "discover_project_resources"
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

        private static void ValidateProcessReviewSkill(string skillPath)
        {
            string skill = File.ReadAllText(skillPath, Encoding.UTF8);
            string[] anchors =
            {
                "name: automation-process-review",
                "description:",
                "# Automation 流程评审",
                "inspect_process",
                "audit_proc_batch",
                "get_operation_context",
                "indexRevision",
                "submit_review_handoff",
                "占位 `message`",
                "automation-process-authoring"
            };
            string missingAnchor = Array.Find(
                anchors,
                anchor => skill.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingAnchor != null)
            {
                throw new InvalidDataException(
                    "Automation 流程评审 Skill 缺少当前工作流入口：" + missingAnchor);
            }
            if (skill.IndexOf("[TODO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidDataException("Automation 流程评审 Skill 仍包含未完成模板标记。");
            }
        }

        private static bool ManagedFileMatchesResource(string resourceName, string filePath)
        {
            if (!File.Exists(filePath)) return false;
            using (Stream expected = OpenManagedResource(resourceName))
            using (var actual = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
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
                    for (int i = 0; i < expectedCount; i++)
                    {
                        if (expectedBuffer[i] != actualBuffer[i]) return false;
                    }
                }
            }
        }

        private static Stream OpenManagedResource(string resourceName)
        {
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (source != null) return source;

            string fallbackPath = ResolveManagedResourceFallbackPath(resourceName);
            if (!File.Exists(fallbackPath))
            {
                throw new InvalidOperationException(
                    "程序内置 Goose 资源及随程序文件均不存在：" + resourceName + "；" + fallbackPath);
            }
            return new FileStream(fallbackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        private static string ResolveManagedResourceFallbackPath(string resourceName)
        {
            if (string.Equals(resourceName, PromptResourceName, StringComparison.Ordinal))
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Goose", "system.md");
            }
            if (string.Equals(resourceName, IntegrationContextResourceName, StringComparison.Ordinal))
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Goose", "automation.md");
            }
            if (string.Equals(resourceName, ProcessAuthoringSkillResourceName, StringComparison.Ordinal)
                || string.Equals(resourceName, ProcessAuthoringSkillVersionResourceName, StringComparison.Ordinal))
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets",
                    "Goose",
                    "Skills",
                    ProcessAuthoringSkillName,
                    string.Equals(resourceName, ProcessAuthoringSkillResourceName, StringComparison.Ordinal)
                        ? "SKILL.md"
                        : ProcessAuthoringSkillVersionFileName);
            }
            if (string.Equals(resourceName, ProcessReviewSkillResourceName, StringComparison.Ordinal)
                || string.Equals(resourceName, ProcessReviewSkillVersionResourceName, StringComparison.Ordinal))
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets",
                    "Goose",
                    "Skills",
                    ProcessReviewSkillName,
                    string.Equals(resourceName, ProcessReviewSkillResourceName, StringComparison.Ordinal)
                        ? "SKILL.md"
                        : ProcessAuthoringSkillVersionFileName);
            }
            throw new InvalidOperationException("未知的 Goose 受管资源：" + resourceName);
        }

        private static void WriteEmbeddedResource(string resourceName, string destination)
        {
            string directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            Stream source = OpenManagedResource(resourceName);
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
