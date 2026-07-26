using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AiAssistantContractTests
    {
        [TestMethod]
        public void DeveloperWrite_RequiresEditorProfile()
        {
            Assert.IsTrue(FrmAiAssistant.IsDeveloperWriteBlockedByProfile("Diagnostic", "write"));
            Assert.IsTrue(FrmAiAssistant.IsDeveloperWriteBlockedByProfile("Diagnostic", "edit"));
            Assert.IsTrue(FrmAiAssistant.IsDeveloperWriteBlockedByProfile("RuntimeDiagnostic", "write"));
            Assert.IsFalse(FrmAiAssistant.IsDeveloperWriteBlockedByProfile("Editor", "write"));
            Assert.IsFalse(FrmAiAssistant.IsDeveloperWriteBlockedByProfile("Diagnostic", "read"));
        }

        [TestMethod]
        public void PreviewObservation_AcceptsCurrentDirectContract()
        {
            var coordinator = new AiPreviewConfirmationCoordinator();
            JObject raw = BuildToolResult(
                "change_set.preview",
                new JObject
                {
                    ["previewId"] = "0123456789abcdef0123456789abcdef",
                    ["confirmed"] = false,
                    ["status"] = "awaiting_confirmation",
                    ["changes"] = new JArray(new JObject { ["type"] = "process.create" }),
                    ["messages"] = new JArray("创建一个流程")
                });

            AiPreviewObservation first = coordinator.Observe(raw, false);
            AiPreviewObservation repeated = coordinator.Observe(raw, false);

            Assert.AreEqual(AiPreviewObservationKind.AwaitingConfirmation, first.Kind);
            Assert.AreEqual(1, first.Changes.Count);
            Assert.AreEqual(1, first.Messages.Count);
            Assert.AreEqual(AiPreviewObservationKind.AlreadyPresented, repeated.Kind);
        }

        [TestMethod]
        public void PreviewObservation_RejectsRetiredNestedShape()
        {
            var coordinator = new AiPreviewConfirmationCoordinator();
            JObject raw = BuildToolResult(
                "change_set.preview",
                new JObject
                {
                    ["previewId"] = "0123456789abcdef0123456789abcdef",
                    ["preview"] = new JObject { ["confirmed"] = false },
                    ["mode"] = "preview",
                    ["result"] = new JObject
                    {
                        ["changes"] = new JArray(),
                        ["messages"] = new JArray()
                    }
                });

            Assert.AreEqual(AiPreviewObservationKind.None, coordinator.Observe(raw, false).Kind);
        }

        [TestMethod]
        public void PreviewObservation_RequiresCommittedSavedApply()
        {
            var coordinator = new AiPreviewConfirmationCoordinator();
            JObject applied = BuildToolResult(
                "change_set.apply",
                new JObject
                {
                    ["previewId"] = "0123456789abcdef0123456789abcdef",
                    ["status"] = "committed",
                    ["configurationSaved"] = true
                });
            JObject incomplete = BuildToolResult(
                "change_set.apply",
                new JObject
                {
                    ["previewId"] = "fedcba9876543210fedcba9876543210",
                    ["status"] = "committed"
                });

            Assert.AreEqual(AiPreviewObservationKind.Applied, coordinator.Observe(applied, false).Kind);
            Assert.AreEqual(AiPreviewObservationKind.None, coordinator.Observe(incomplete, false).Kind);
        }

        private static JObject BuildToolResult(string type, JObject data)
        {
            string text = new JObject
            {
                ["ok"] = true,
                ["type"] = type,
                ["data"] = data
            }.ToString(Formatting.None);
            return new JObject
            {
                ["params"] = new JObject
                {
                    ["update"] = new JObject
                    {
                        ["content"] = new JArray(new JObject { ["text"] = text })
                    }
                }
            };
        }
    }
}
