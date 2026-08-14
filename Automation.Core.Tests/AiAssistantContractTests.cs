using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using Automation.Protocol;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AiAssistantContractTests
    {
        [TestMethod]
        public void GooseDefaults_UseAgentOutputBudgetAndLowVarianceSampling()
        {
            GooseConfig config = GooseConfigStorage.CreateDefaultConfig();

            Assert.AreEqual(16384, config.MaxOutputTokens);
            Assert.AreEqual(0.3d, config.Temperature, 0.000001d);
        }

        [TestMethod]
        public void SourceReview_UsesReadOnlyDeveloperToolFilter()
        {
            MethodInfo method = typeof(GooseAcpClient).GetMethod(
                "HasExpectedDeveloperFilter",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            var readOnly = new JObject
            {
                ["available_tools"] = new JArray("read", "tree")
            };
            var unrestricted = new JObject();

            Assert.IsTrue((bool)method.Invoke(null, new object[]
            {
                readOnly,
                AutomationToolProfiles.SourceReview
            }));
            Assert.IsFalse((bool)method.Invoke(null, new object[]
            {
                readOnly,
                AutomationToolProfiles.SourceDevelopment
            }));
            Assert.IsTrue((bool)method.Invoke(null, new object[]
            {
                unrestricted,
                AutomationToolProfiles.SourceDevelopment
            }));
        }

        [TestMethod]
        public void SourceReview_NormalizesCatalogNamesAndBlocksNonReadDeveloperTools()
        {
            MethodInfo normalize = typeof(GooseAcpClient).GetMethod(
                "NormalizeExtensionToolName",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(normalize);
            Assert.AreEqual("read", normalize.Invoke(null, new object[] { "developer__read" }));
            Assert.AreEqual("tree", normalize.Invoke(null, new object[] { "developer/tree" }));
            Assert.AreEqual("read", normalize.Invoke(null, new object[] { "developer.read" }));
            Assert.AreEqual("tree", normalize.Invoke(null, new object[] { "developer:tree" }));

            Assert.IsFalse(FrmAiAssistant.IsDeveloperToolBlockedByCapability(
                AutomationToolProfiles.SourceReview, "developer__read"));
            Assert.IsFalse(FrmAiAssistant.IsDeveloperToolBlockedByCapability(
                AutomationToolProfiles.SourceReview, "developer/tree"));
            Assert.IsTrue(FrmAiAssistant.IsDeveloperToolBlockedByCapability(
                AutomationToolProfiles.SourceReview, "developer__shell"));
            Assert.IsTrue(FrmAiAssistant.IsDeveloperToolBlockedByCapability(
                AutomationToolProfiles.SourceReview, "developer.analyze"));
            Assert.IsFalse(FrmAiAssistant.IsDeveloperToolBlockedByCapability(
                AutomationToolProfiles.SourceReview, "automation__search_platform_source"));
        }

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

        [TestMethod]
        public void DeveloperWrite_RequiresSourceDevelopmentCapability()
        {
            Assert.IsTrue(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Editor, AutomationToolProfiles.SourceReview, "write"));
            Assert.IsTrue(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Editor, AutomationToolProfiles.ProcessReview, "edit"));
            Assert.IsFalse(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Editor, AutomationToolProfiles.SourceDevelopment, "write"));
            Assert.IsFalse(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Editor, AutomationToolProfiles.SourceReview, "read"));
        }

        [TestMethod]
        public void PreviewObservation_SupportsMigrationPreviewAndApply()
        {
            var coordinator = new AiPreviewConfirmationCoordinator();
            JObject preview = BuildToolResult(
                "migration.preview",
                new JObject
                {
                    ["previewId"] = "1123456789abcdef0123456789abcdef",
                    ["confirmed"] = false,
                    ["committed"] = false,
                    ["changes"] = new JArray(new JObject { ["type"] = "configuration.replace" }),
                    ["messages"] = new JArray("替换PLC配置")
                });
            JObject applied = BuildToolResult(
                "migration.apply",
                new JObject
                {
                    ["previewId"] = "1123456789abcdef0123456789abcdef",
                    ["committed"] = true,
                    ["configurationSaved"] = true
                });

            Assert.AreEqual(
                AiPreviewObservationKind.AwaitingConfirmation,
                coordinator.Observe(preview, false).Kind);
            Assert.AreEqual(AiPreviewObservationKind.Applied, coordinator.Observe(applied, false).Kind);
        }

        [TestMethod]
        public void AcpTextExtraction_PreservesNestedMarkdownBlankLines()
        {
            var parameters = new JObject
            {
                ["sessionUpdate"] = "agent_message_chunk",
                ["update"] = new JObject
                {
                    ["content"] = new JArray(new JObject
                    {
                        ["type"] = "text",
                        ["text"] = "\n\n"
                    })
                }
            };
            MethodInfo extractText = typeof(GooseAcpClient).GetMethod(
                "ExtractText",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(extractText);
            Assert.AreEqual("\n\n", extractText.Invoke(null, new object[] { parameters }));
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
