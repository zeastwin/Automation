using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

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

            coordinator.Cancel(runtime, "standard_test_user_stop");

            Assert.AreEqual("standard_test_user_stop", runtime.CancellationSource);
            Assert.IsTrue(runtime.Cancellation.IsCancellationRequested);
            runtime.Cancellation.Dispose();
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
