using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Automation
{
    /// <summary>
    /// 把平台受管的 Pi 集成上下文与 Skill 从程序集资源（或程序目录副本）部署到
    /// %APPDATA%\Automation\Pi\ 下，供 EW-AI 子进程经 PI_CODING_AGENT_DIR 与 --skill 加载。
    /// 双通道（Manifest 优先、程序目录副本回退）均失败时只禁用 EW-AI 并明确报警。
    /// </summary>
    public static class PiContextProvisioner
    {
        public const int IntegrationContextVersion = 5;
        public static int ToolsCliSkillVersion { get; } = ReadBundledSkillVersion(
            ToolsCliSkillVersionResourceName, ToolsCliSkillName, ToolsCliSkillVersionFileName, "ToolCli 机制 Skill");
        public static int ProcessAuthoringSkillVersion { get; } = ReadBundledSkillVersion(
            ProcessAuthoringSkillVersionResourceName, ProcessAuthoringSkillName, ProcessAuthoringSkillVersionFileName, "流程编写 Skill");
        public const string ToolsCliSkillName = "automation-tools-cli";
        public const string ProcessAuthoringSkillName = "automation-process-authoring";
        private const string IntegrationContextResourceName = "Automation.Assets.Pi.automation-cli.md";
        private const string ToolsCliSkillResourceName =
            "Automation.Assets.Pi.Skills.automation-tools-cli.SKILL.md";
        private const string ToolsCliSkillVersionResourceName =
            "Automation.Assets.Pi.Skills.automation-tools-cli.skill-version";
        private const string ProcessAuthoringSkillResourceName =
            "Automation.Assets.Pi.Skills.automation-process-authoring.SKILL.md";
        private const string ProcessAuthoringSkillVersionResourceName =
            "Automation.Assets.Pi.Skills.automation-process-authoring.skill-version";
        private const string IntegrationContextVersionFileName = ".automation-context-version";
        private const string ToolsCliSkillVersionFileName = ".automation-tools-cli-skill-version";
        private const string ProcessAuthoringSkillVersionFileName = ".automation-skill-version";

        public static bool IsManagedContextAvailable { get; private set; }

        /// <summary>编辑会话的 Pi 配置目录（PI_CODING_AGENT_DIR），内含部署的 APPEND_SYSTEM.md。</summary>
        public static string AgentDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Pi", "agent");

        /// <summary>运行诊断会话使用独立的 Pi 配置目录，不加载平台集成上下文。</summary>
        public static string DiagnosticAgentDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Pi", "agent-diagnostic");

        public static string AppendSystemPromptPath => Path.Combine(AgentDirectory, "APPEND_SYSTEM.md");

        public static string SkillsRootDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "Pi", "skills");

        public static string ToolsCliSkillDirectory => Path.Combine(SkillsRootDirectory, ToolsCliSkillName);

        public static string ProcessAuthoringSkillDirectory => Path.Combine(SkillsRootDirectory, ProcessAuthoringSkillName);

        public static bool TryEnsureManagedContext(out string message)
        {
            message = null;
            IsManagedContextAvailable = false;
            try
            {
                var messages = new System.Collections.Generic.List<string>();
                DeployVersionedArtifact(
                    IntegrationContextResourceName,
                    AppendSystemPromptPath,
                    Path.Combine(AgentDirectory, IntegrationContextVersionFileName),
                    IntegrationContextVersion,
                    "Automation 集成上下文",
                    Path.Combine("Assets", "Pi", "automation-cli.md"),
                    messages);
                DeployVersionedArtifact(
                    ToolsCliSkillResourceName,
                    Path.Combine(ToolsCliSkillDirectory, "SKILL.md"),
                    Path.Combine(ToolsCliSkillDirectory, ToolsCliSkillVersionFileName),
                    ToolsCliSkillVersion,
                    "Automation ToolCli 机制 Skill",
                    Path.Combine("Assets", "Pi", "Skills", ToolsCliSkillName, "SKILL.md"),
                    messages);
                DeployVersionedArtifact(
                    ProcessAuthoringSkillResourceName,
                    Path.Combine(ProcessAuthoringSkillDirectory, "SKILL.md"),
                    Path.Combine(ProcessAuthoringSkillDirectory, ProcessAuthoringSkillVersionFileName),
                    ProcessAuthoringSkillVersion,
                    "Automation 流程编写 Skill",
                    Path.Combine("Assets", "Pi", "Skills", ProcessAuthoringSkillName, "SKILL.md"),
                    messages);

                ValidateManagedFiles();
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

        private static void DeployVersionedArtifact(
            string resourceName,
            string destinationPath,
            string versionPath,
            int bundledVersion,
            string artifactName,
            string outputRelativePath,
            System.Collections.Generic.List<string> messages)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            int installedVersion = ReadInstalledVersion(versionPath, artifactName);
            if (installedVersion > bundledVersion)
            {
                if (!File.Exists(destinationPath))
                {
                    throw new InvalidDataException(artifactName + " 版本标记存在，但文件不存在：" + destinationPath);
                }
                messages.Add($"本机{artifactName}版本 {installedVersion} 高于程序内置版本 {bundledVersion}，已保留本机版本。");
                return;
            }
            if (File.Exists(destinationPath) && installedVersion == bundledVersion)
            {
                return;
            }
            WriteEmbeddedResource(resourceName, destinationPath, outputRelativePath);
            File.WriteAllText(versionPath, bundledVersion.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
            messages.Add($"{artifactName}已更新到版本 {bundledVersion}。");
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

        private static int ReadBundledSkillVersion(string resourceName, string skillName, string versionFileName, string artifactName)
        {
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (source == null)
            {
                string fallbackPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets", "Pi", "Skills", skillName, versionFileName);
                if (!File.Exists(fallbackPath))
                {
                    throw new InvalidOperationException(
                        "程序内置" + artifactName + "版本资源不存在：" + fallbackPath);
                }
                source = new FileStream(fallbackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            using (source)
            using (var reader = new StreamReader(source, Encoding.UTF8, true, 128, false))
            {
                string text = reader.ReadToEnd().Trim();
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int version)
                    || version <= 0)
                {
                    throw new InvalidDataException("程序内置" + artifactName + "版本格式无效。");
                }
                return version;
            }
        }

        private static void ValidateManagedFiles()
        {
            string integrationContext = File.ReadAllText(AppendSystemPromptPath, Encoding.UTF8);
            string[] contextAnchors =
            {
                "automation-tools-cli",
                "automation-process-authoring",
                "AUTOMATION_TOOLCLI_PATH",
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
            string missingContextAnchor = Array.Find(contextAnchors,
                anchor => integrationContext.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingContextAnchor != null)
            {
                throw new InvalidDataException("Automation 集成上下文缺少当前链路入口：" + missingContextAnchor);
            }
            ValidateNoRetiredContent(integrationContext, "Automation 集成上下文");

            string toolsCliSkill = File.ReadAllText(
                Path.Combine(ToolsCliSkillDirectory, "SKILL.md"), Encoding.UTF8);
            string[] toolsCliAnchors =
            {
                "name: automation-tools-cli",
                "description:",
                "AUTOMATION_TOOLCLI_PATH",
                "AUTOMATION_TOOL_PROFILE",
                "cli list",
                "cli schema",
                "cli call",
                "preview_change_set",
                "apply_change_set"
            };
            string missingToolsCliAnchor = Array.Find(toolsCliAnchors,
                anchor => toolsCliSkill.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingToolsCliAnchor != null)
            {
                throw new InvalidDataException("Automation ToolCli 机制 Skill 缺少当前调用机制入口：" + missingToolsCliAnchor);
            }
            ValidateNoRetiredContent(toolsCliSkill, "Automation ToolCli 机制 Skill");

            string processAuthoringSkill = File.ReadAllText(
                Path.Combine(ProcessAuthoringSkillDirectory, "SKILL.md"), Encoding.UTF8);
            string[] processAuthoringAnchors =
            {
                "name: automation-process-authoring",
                "description:",
                "get_process_design_guide",
                "get_semantic_operation_schema",
                "get_native_operation_schemas",
                "preview_change_set",
                "apply_change_set",
                "validate_proc",
                "run_proc_test"
            };
            string missingProcessAuthoringAnchor = Array.Find(processAuthoringAnchors,
                anchor => processAuthoringSkill.IndexOf(anchor, StringComparison.Ordinal) < 0);
            if (missingProcessAuthoringAnchor != null)
            {
                throw new InvalidDataException("Automation 流程编写 Skill 缺少当前工作流入口：" + missingProcessAuthoringAnchor);
            }
            ValidateNoRetiredContent(processAuthoringSkill, "Automation 流程编写 Skill");
        }

        private static void ValidateNoRetiredContent(string content, string artifactName)
        {
            string[] retiredRoutes =
            {
                "preview_intent", "apply_intent", "preview_patch", "apply_patch", "create_proc_batch"
            };
            string retiredRoute = Array.Find(retiredRoutes,
                route => content.IndexOf(route, StringComparison.Ordinal) >= 0);
            if (retiredRoute != null)
            {
                throw new InvalidDataException(artifactName + "仍引用旧写入链：" + retiredRoute);
            }
            string[] retiredMechanisms = { "GOOSE_", "load_skill", "AUTOMATION_MCP" };
            string retiredMechanism = Array.Find(retiredMechanisms,
                item => content.IndexOf(item, StringComparison.Ordinal) >= 0);
            if (retiredMechanism != null)
            {
                throw new InvalidDataException(artifactName + "仍引用已退役机制：" + retiredMechanism);
            }
            if (content.IndexOf("[TODO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidDataException(artifactName + "仍包含未完成模板标记。");
            }
        }

        private static void WriteEmbeddedResource(string resourceName, string destination, string outputRelativePath)
        {
            string directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (source == null)
            {
                string fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, outputRelativePath);
                if (File.Exists(fallbackPath))
                {
                    source = new FileStream(fallbackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
                else
                {
                    throw new InvalidOperationException(
                        "程序内置 Pi 受管资源及随程序文件均不存在：" + resourceName + "；" + fallbackPath);
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
