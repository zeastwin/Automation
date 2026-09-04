using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Automation.Protocol;
using Newtonsoft.Json.Linq;

// 模块：核心测试 / Machine Agent Prompt 隔离。
// 职责范围：验证专属 System Prompt、版本同步和宿主 Prompt 旁路不退化到原 AI 助手链路。

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class MachineAgentPromptIsolationTests
    {
        [TestMethod]
        public void EmbeddedSystemPrompt_ContainsMachineContractWithoutProcessAgentRoutes()
        {
            string prompt = ReadEmbedded(MachineAgentPromptProvisioner.PromptResourceName);

            StringAssert.Contains(prompt, "You are the Machine Agent");
            StringAssert.Contains(prompt, "confirmed equipment topology");
            StringAssert.Contains(prompt, "globally ordered equipment state history");
            StringAssert.Contains(prompt, "nodeWindow.hasMore");
            StringAssert.Contains(prompt, "relationWindow.hasMore");
            StringAssert.Contains(prompt, "currentState.updatedAtUtc");
            StringAssert.Contains(prompt, "perception.lastSuccessfulObservationAtUtc");
            StringAssert.Contains(prompt, "remains unchanged for more than five seconds is still current");
            StringAssert.Contains(prompt, "preview_process_entry_execution");
            StringAssert.Contains(prompt, "preview_process_stop");
            StringAssert.Contains(prompt, "freezes the current `runId`");
            StringAssert.Contains(prompt, "confirmed `skillId`");
            StringAssert.Contains(prompt, "context.topology.nodes[].skills");
            StringAssert.Contains(prompt, "never choose or override its mode");
            StringAssert.Contains(prompt, "single_operation");
            StringAssert.Contains(prompt, "continue_flow");
            Assert.IsFalse(prompt.Contains("EW-AI", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("automation-process-authoring", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("automation-process-review", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("preview_change_set", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("apply_change_set", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("request_capability", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("Ready/Running/Finish", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("nine-state", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(prompt.Contains("Ready", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("Finish", StringComparison.Ordinal));
        }

        [TestMethod]
        public void MachineAgentExtensionSurface_RejectsEveryExtensionExceptAutomation()
        {
            GooseAcpClient.ValidateMachineAgentExtensionNames(new[] { "automation" });

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                GooseAcpClient.ValidateMachineAgentExtensionNames(
                    new[] { "automation", "developer" }));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                GooseAcpClient.ValidateMachineAgentExtensionNames(Array.Empty<string>()));
        }

        [TestMethod]
        public void MachineAgentPermission_RequiresExactAutomationOwnerAndWhitelistedTool()
        {
            Assert.AreEqual("allowed", PermissionOutcome("automation", "get_machine_context"));
            Assert.AreEqual("allowed", PermissionOutcome(null, "automation__get_machine_context"));
            Assert.AreEqual("cancelled", PermissionOutcome("fooautomation", "fooautomation__get_machine_context"));
            Assert.AreEqual("cancelled", PermissionOutcome(null, "fooautomation__get_machine_context"));
            Assert.AreEqual("cancelled", PermissionOutcome("automation", "start_proc"));
            Assert.AreEqual("cancelled", PermissionOutcome("automation", "request_capability"));
        }

        [TestMethod]
        public void MachineAgentConfig_IsAnIsolatedCloneOfSharedModelSettings()
        {
            GooseConfig shared = GooseConfigStorage.CreateDefaultConfig();
            shared.McpUri = "http://127.0.0.1:8081";
            shared.ToolProfile = AutomationToolProfiles.Editor;
            shared.AutoApproveMode = true;
            shared.ModelServices.Add(new AiModelServiceConfig
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = "共享模型",
                BaseUrl = "https://example.invalid/v1",
                Model = "shared-model",
                RequiresApiKey = true
            });
            AiModelServiceConfig sharedService = shared.ModelServices[0];

            GooseConfig machine = FrmMachineAgent.CreateMachineAgentConfig(
                shared, "http://127.0.0.1:19091");

            Assert.AreEqual("http://127.0.0.1:8081", shared.McpUri);
            Assert.AreEqual(AutomationToolProfiles.Editor, shared.ToolProfile);
            Assert.IsTrue(shared.AutoApproveMode);
            Assert.AreEqual("http://127.0.0.1:19091", machine.McpUri);
            Assert.AreEqual(AutomationToolProfiles.MachineAgent, machine.ToolProfile);
            Assert.IsFalse(machine.AutoApproveMode);
            Assert.AreNotSame(shared.ModelServices, machine.ModelServices);
            Assert.AreNotSame(sharedService, machine.ModelServices[0]);
        }

        [TestMethod]
        public void MachineAgentSourcePaths_OnlyReadCachedSharedConfigAndShutdownBeforeMcp()
        {
            string repositoryRoot = FindRepositoryRoot();
            string agentSource = File.ReadAllText(Path.Combine(
                repositoryRoot, "Editor", "MachineAgent", "FrmMachineAgent.Agent.cs"), Encoding.UTF8);
            string topologySource = File.ReadAllText(Path.Combine(
                repositoryRoot, "Editor", "Topology", "TopologyAiRefinementService.cs"), Encoding.UTF8);
            string lifecycleSource = File.ReadAllText(Path.Combine(
                repositoryRoot, "Editor", "Shell", "FrmMain.Lifecycle.cs"), Encoding.UTF8);

            Assert.IsFalse(agentSource.Contains("GooseConfigStorage.TryLoad", StringComparison.Ordinal));
            StringAssert.Contains(agentSource, "GooseConfigStorage.TryGetCached");
            Assert.IsFalse(topologySource.Contains("GooseConfigStorage.TryLoad", StringComparison.Ordinal));
            StringAssert.Contains(topologySource, "GooseConfigStorage.TryGetCached");

            int infrastructureStart = lifecycleSource.IndexOf(
                "internal bool TryEnsureMachineAgentInfrastructureStarted", StringComparison.Ordinal);
            int infrastructureEnd = lifecycleSource.IndexOf(
                "private void ReportAiInfrastructureUnavailable", infrastructureStart, StringComparison.Ordinal);
            Assert.IsTrue(infrastructureStart >= 0 && infrastructureEnd > infrastructureStart);
            string infrastructure = lifecycleSource.Substring(
                infrastructureStart, infrastructureEnd - infrastructureStart);
            Assert.IsFalse(infrastructure.Contains("GooseConfigStorage.TryLoad", StringComparison.Ordinal));
            StringAssert.Contains(infrastructure, "GooseConfigStorage.TryGetCached");

            int machineDispose = lifecycleSource.IndexOf(
                "editorWorkspace?.MachineAgent?.DisposeGooseClient()", StringComparison.Ordinal);
            int mcpDispose = lifecycleSource.IndexOf(
                "关闭MCP Server", machineDispose, StringComparison.Ordinal);
            Assert.IsTrue(machineDispose >= 0 && mcpDispose > machineDispose,
                "Machine Agent Goose 必须在 MCP 与 Bridge 之前释放。");
        }

        [TestMethod]
        public void PromptProvisioning_SameVersionContentDriftIsResynchronized()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "automation-machine-agent-prompt-tests",
                Guid.NewGuid().ToString("N"));
            string promptPath = Path.Combine(root, "prompts", "system.md");
            string versionPath = Path.Combine(root, "prompts", ".machine-agent-system-prompt-version");
            string backupPath = Path.Combine(root, "backups");
            try
            {
                Assert.IsTrue(MachineAgentPromptProvisioner.TryEnsureAtPath(
                    promptPath, versionPath, backupPath, out string firstMessage), firstMessage);
                Assert.AreEqual(
                    MachineAgentPromptProvisioner.SystemPromptVersion.ToString(),
                    File.ReadAllText(versionPath, Encoding.UTF8).Trim());

                File.WriteAllText(promptPath, "同版本漂移内容", new UTF8Encoding(false));

                Assert.IsTrue(MachineAgentPromptProvisioner.TryEnsureAtPath(
                    promptPath, versionPath, backupPath, out string secondMessage), secondMessage);
                string restored = File.ReadAllText(promptPath, Encoding.UTF8);
                StringAssert.Contains(restored, "You are the Machine Agent");
                Assert.AreEqual(1, Directory.GetFiles(backupPath, "*.md").Length);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void HostPromptBuilder_ForMachineAgentPassesOnlyStructuredUserRequest()
        {
            GooseConfig config = GooseConfigStorage.CreateDefaultConfig();
            config.ToolProfile = AutomationToolProfiles.MachineAgent;
            config.TaskCapabilityNotice = "不应进入 Machine Agent 用户消息";
            using (var client = new GooseAcpClient(new PlatformRuntime(), config))
            {
                MethodInfo method = typeof(GooseAcpClient).GetMethod(
                    "BuildPrompt", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(method);
                const string request = "{\"type\":\"machine_agent.user_request.v1\",\"request\":\"检查设备\"}";
                string result = method.Invoke(client, new object[] { request }) as string;
                Assert.AreEqual(request, result);
                Assert.IsFalse(result.Contains("当前能力", StringComparison.Ordinal));
                Assert.IsFalse(result.Contains("不应进入", StringComparison.Ordinal));
            }
        }

        private static string ReadEmbedded(string resourceName)
        {
            using (Stream stream = typeof(MachineAgentPromptProvisioner).Assembly
                .GetManifestResourceStream(resourceName))
            {
                Assert.IsNotNull(stream, resourceName);
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static string PermissionOutcome(string extensionName, string toolName)
        {
            var toolCall = new JObject
            {
                ["name"] = toolName ?? string.Empty
            };
            if (extensionName != null) toolCall["extensionName"] = extensionName;
            JObject result = FrmMachineAgent.HandleMachinePermissionRequest(new JObject
            {
                ["toolCall"] = toolCall
            });
            return result["outcome"]?["outcome"]?.Value<string>();
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Automation.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("无法从测试输出目录定位 Automation.sln。");
        }
    }
}
