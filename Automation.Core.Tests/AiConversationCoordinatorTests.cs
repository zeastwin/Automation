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
                "resolve_authoring_inputs",
                "project.authoring_inputs",
                new JObject
                {
                    ["results"] = new JArray(new JObject
                    {
                        ["key"] = "scanResult",
                        ["kind"] = "variable",
                        ["bindingAllowed"] = true,
                        ["selected"] = new JObject
                        {
                            ["name"] = "扫码结果",
                            ["type"] = "string",
                            ["scope"] = "process"
                        }
                    })
                });

            JObject compact = JObject.Parse(artifact.ToCompactJson());
            Assert.IsTrue(artifact.HasFacts);
            Assert.AreEqual(
                "11111111-1111-1111-1111-111111111111",
                compact["changeSetApply"]?["createdObjects"]?[0]?["procId"]?.Value<string>());
            Assert.AreEqual(
                "扫码结果",
                compact["authoringInputs"]?[0]?["selected"]?["name"]?.Value<string>());
        }
    }
}
