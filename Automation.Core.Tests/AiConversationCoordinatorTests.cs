using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Automation.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AiConversationCoordinatorTests
    {
        [TestMethod]
        public void EnsureActive_CreatesFromHomeReusesActiveAndHonorsForceNew()
        {
            var coordinator = new AiConversationCoordinator();

            AiTaskRuntime first = coordinator.EnsureActive(false);
            AiTaskRuntime reused = coordinator.EnsureActive(false);
            AiTaskRuntime second = coordinator.EnsureActive(true);

            Assert.IsNotNull(first);
            Assert.AreSame(first, reused);
            Assert.AreNotSame(first, second);
            Assert.AreSame(second, coordinator.ActiveRuntime);
            Assert.IsFalse(coordinator.TaskHomeVisible);
        }

        [TestMethod]
        public void StageArtifact_CapturesStableIdsBindingsAndValidationFacts()
        {
            AiStageArtifact artifact = AiStageArtifact.Capture(
                AiStageArtifact.Empty,
                "apply_change_set",
                "change_set.apply",
                new JObject
                {
                    ["status"] = "committed",
                    ["configurationSaved"] = true,
                    ["createdObjects"] = new JArray(new JObject
                    {
                        ["procId"] = "11111111-1111-1111-1111-111111111111",
                        ["procIndex"] = 3
                    })
                });
            artifact = AiStageArtifact.Capture(
                artifact,
                "list_authoring_resources",
                "project.authoring_resources",
                new JObject
                {
                    ["results"] = new JArray(new JObject
                    {
                        ["type"] = "io_input",
                        ["total"] = 1,
                        ["items"] = new JArray(new JObject
                        {
                            ["resourceRef"] = "io_input:0:0:0",
                            ["name"] = "搬运气缸到位感应1",
                            ["ioType"] = "通用输入"
                        })
                    })
                });

            JObject compact = JObject.Parse(artifact.ToCompactJson());
            Assert.IsTrue(artifact.HasFacts);
            Assert.AreEqual(
                "11111111-1111-1111-1111-111111111111",
                compact["changeSetApply"]?["createdObjects"]?[0]?["procId"]?.Value<string>());
            Assert.AreEqual(
                "搬运气缸到位感应1",
                compact["authoringResources"]?[0]?["items"]?[0]?["name"]?.Value<string>());
            Assert.AreEqual(
                "io_input:0:0:0",
                compact["authoringResources"]?[0]?["items"]?[0]?["resourceRef"]?.Value<string>());
        }

        [TestMethod]
        public void Cancel_RecordsExplicitSourceOnRuntime()
        {
            var coordinator = new AiConversationCoordinator();
            AiTaskRuntime runtime = coordinator.EnsureActive(false);
            runtime.Cancellation = new System.Threading.CancellationTokenSource();

            coordinator.Cancel(runtime, "user_stop");

            Assert.AreEqual("user_stop", runtime.CancellationSource);
            Assert.IsTrue(runtime.Cancellation.IsCancellationRequested);
            runtime.Cancellation.Dispose();
        }

        [TestMethod]
        public async Task ExecuteDynamicTaskAsync_CoordinatorDirectTerminalCarriesCoordinatorClient()
        {
            var coordinator = new AiConversationCoordinator();
            AiTaskRuntime runtime = coordinator.EnsureActive(false);
            runtime.Cancellation = new System.Threading.CancellationTokenSource();
            var config = GooseConfigStorage.CreateDefaultConfig();
            // 指向不存在的可执行文件：PromptAsync 在启动任何进程前即失败，
            // 任务在协调轮直接进入终态，等价于"协调轮直接回复用户"的完成路径。
            config.GooseExecutablePath = Path.Combine(
                Path.GetTempPath(), "automation_no_goose_" + Guid.NewGuid().ToString("N") + ".exe");
            var createdClients = new List<GooseAcpClient>();
            try
            {
                AiTaskExecutionResult result = await coordinator.ExecuteDynamicTaskAsync(
                    runtime,
                    "你好",
                    Array.Empty<GooseFileAttachment>(),
                    AutomationToolProfiles.Editor,
                    false,
                    stage =>
                    {
                        var client = new GooseAcpClient(new PlatformRuntime(), config);
                        createdClients.Add(client);
                        return Task.FromResult(client);
                    });

                // 协调轮直接终态：只创建过协调 client，未进入任何工作能力阶段。
                Assert.AreEqual(1, createdClients.Count);
                Assert.AreEqual(AiTaskExecutionStatus.Failed, result.Status);
                // 契约：协调阶段直接终态时结果必须携带实际使用的协调 client；
                // 传 lastWorkerClient(null) 会让前台取不到最终回复，出现"发送后无回复"。
                Assert.IsNotNull(result.Client);
                Assert.AreSame(createdClients[0], result.Client);
            }
            finally
            {
                foreach (GooseAcpClient client in createdClients)
                {
                    client.Dispose();
                }
            }
        }

        [TestMethod]
        public void RestoredContext_IncludesBoundedTrustedFactsWithObservationBoundary()
        {
            var conversation = new AiConversation
            {
                TrustedFactsJson = "{\"authoringResources\":[{\"type\":\"motion\",\"items\":[]}]}",
                TrustedFactsObservedAt = new System.DateTime(2026, 8, 17, 14, 46, 0)
            };

            string restored = AiConversationCoordinator.BuildRestoredContext(conversation);

            StringAssert.Contains(restored, "此前工具机械观察");
            StringAssert.Contains(restored, "用户明确配置已变化时必须重新读取");
            StringAssert.Contains(restored, "authoringResources");
        }
    }
}
