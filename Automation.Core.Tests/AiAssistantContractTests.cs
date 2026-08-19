using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
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
        public void SourceReview_NormalizesCatalogNames()
        {
            MethodInfo normalize = typeof(GooseAcpClient).GetMethod(
                "NormalizeExtensionToolName",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(normalize);
            Assert.AreEqual("read_file", normalize.Invoke(null, new object[] { "developer__read_file" }));
            Assert.AreEqual("tree", normalize.Invoke(null, new object[] { "developer/tree" }));
            Assert.AreEqual("read", normalize.Invoke(null, new object[] { "developer.read" }));
            Assert.AreEqual("tree", normalize.Invoke(null, new object[] { "developer:tree" }));
        }

        [TestMethod]
        public void CapabilitySurface_ValidatesActualStableControlSchema()
        {
            var tools = new JArray(new JObject
            {
                ["name"] = "automation__request_capability",
                ["inputSchema"] = BuildStableControlSchema(false)
            });

            JObject result = GooseAcpClient.ValidateCapabilityControlToolCatalog(tools);
            JObject reordered = GooseAcpClient.ValidateCapabilityControlToolCatalog(
                new JArray(new JObject
                {
                    ["name"] = "automation__request_capability",
                    ["inputSchema"] = BuildStableControlSchema(true)
                }));

            Assert.AreEqual("automation__request_capability", result["toolName"]?.Value<string>());
            Assert.IsTrue(result["schemaBytes"]?.Value<int>() > 0);
            Assert.AreEqual(64, result["schemaSha256"]?.Value<string>()?.Length);
            Assert.AreEqual(result["schemaSha256"]?.Value<string>(),
                reordered["schemaSha256"]?.Value<string>());
        }

        [TestMethod]
        public void CapabilitySurface_RejectsStaleProfileSpecificControlSchema()
        {
            var tools = new JArray(new JObject
            {
                ["name"] = "request_capability",
                ["input_schema"] = new JObject
                {
                    ["oneOf"] = new JArray(
                        BuildDecisionSchemaBranch("run_stage", false),
                        BuildDecisionSchemaBranch("finish", false),
                        BuildDecisionSchemaBranch("ask_user", false))
                }
            });

            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                GooseAcpClient.ValidateCapabilityControlToolCatalog(tools));

            StringAssert.Contains(error.Message, "findingIds");
        }

        [TestMethod]
        public void ProcessReview_OmittedHandoffFallsBackWithoutAnotherModelTurn()
        {
            GooseConfig config = GooseConfigStorage.CreateDefaultConfig();
            config.ToolProfile = AutomationToolProfiles.ProcessReview;
            using (var client = new GooseAcpClient(new PlatformRuntime(), config))
            {
                ReviewHandoffDefinition handoff = client.PrepareReviewHandoffForCompletion(
                    null,
                    "当前证据不足，无法证明缺陷。");

                Assert.IsNotNull(handoff);
                Assert.AreEqual(ReviewHandoffStatuses.Unresolved, handoff.Status);
                Assert.AreEqual(0, handoff.Findings.Count);
            }
        }

        [TestMethod]
        public void ProcessReview_AggregatedInspectionPreservesNestedEvidencePaths()
        {
            GooseConfig config = GooseConfigStorage.CreateDefaultConfig();
            config.ToolProfile = AutomationToolProfiles.ProcessReview;
            using (var client = new GooseAcpClient(new PlatformRuntime(), config))
            {
                var inspection = new JObject
                {
                    ["overview"] = new JObject
                    {
                        ["procIndex"] = 1,
                        ["procId"] = "proc-1",
                        ["name"] = "扫码流程",
                        ["runnable"] = false,
                        ["steps"] = new JArray()
                    },
                    ["validation"] = new JObject
                    {
                        ["procIndex"] = 1,
                        ["procId"] = "proc-1",
                        ["procName"] = "扫码流程",
                        ["isValid"] = true,
                        ["runnable"] = false,
                        ["runBlockers"] = new JArray("仍是配置占位")
                    },
                    ["flowGraph"] = new JObject
                    {
                        ["nodes"] = new JArray(new JObject
                        {
                            ["kind"] = "placeholder",
                            ["opId"] = "op-1",
                            ["label"] = "扫码",
                            ["reachable"] = true,
                            ["invalid"] = false
                        }),
                        ["edges"] = new JArray(
                            new JObject
                            {
                                ["sourceId"] = "op:op-1",
                                ["targetId"] = "op:op-success",
                                ["sourceField"] = "PlannedSuccessGoto",
                                ["planned"] = true
                            },
                            new JObject
                            {
                                ["sourceId"] = "op:op-1",
                                ["targetId"] = "op:op-failure",
                                ["sourceField"] = "PlannedFailureGoto",
                                ["planned"] = true
                            }),
                        ["diagnostics"] = new JArray()
                    },
                    ["operationDetails"] = new JObject
                    {
                        ["operations"] = new JArray(new JObject
                        {
                            ["opId"] = "op-1",
                            ["name"] = "扫码",
                            ["operaType"] = "配置占位",
                            ["fields"] = new JObject { ["Reason"] = "协议待确认" }
                        })
                    }
                };
                string raw = new JObject
                {
                    ["ok"] = true,
                    ["type"] = "proc.inspection",
                    ["data"] = inspection
                }.ToString(Formatting.None);
                MethodInfo capture = typeof(GooseAcpClient).GetMethod(
                    "CaptureReviewVerifiedFactsLocked",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo attach = typeof(GooseAcpClient).GetMethod(
                    "AttachReviewVerifiedFactsLocked",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(capture);
                Assert.IsNotNull(attach);
                capture.Invoke(client, new object[] { "inspect_process", "call-1", inspection, raw });
                var handoff = new ReviewHandoffDefinition
                {
                    Status = ReviewHandoffStatuses.Unresolved,
                    Summary = "等待协议事实",
                    Findings = new System.Collections.Generic.List<ReviewFindingDefinition>
                    {
                        new ReviewFindingDefinition
                        {
                            EvidenceFactRefs = new System.Collections.Generic.List<string>
                            {
                                ReviewFactReference.Build("op-1", "operation.operaType"),
                                ReviewFactReference.Build("op-1", "operation.placeholder"),
                                ReviewFactReference.Build("op-1", "operation.plannedOutgoingCount"),
                                ReviewFactReference.Build("op-1", "operation.plannedTarget.PlannedSuccessGoto")
                            }
                        }
                    }
                };

                attach.Invoke(client, new object[] { handoff });

                ReviewVerifiedFactDefinition opFact = handoff.VerifiedFacts
                    .Single(item => item.SubjectId == "op-1" && item.Key == "operation.operaType");
                ReviewVerifiedFactDefinition placeholderFact = handoff.VerifiedFacts
                    .Single(item => item.SubjectId == "op-1" && item.Key == "operation.placeholder");
                ReviewVerifiedFactDefinition plannedCountFact = handoff.VerifiedFacts
                    .Single(item => item.SubjectId == "op-1" && item.Key == "operation.plannedOutgoingCount");
                ReviewVerifiedFactDefinition plannedSuccessFact = handoff.VerifiedFacts
                    .Single(item => item.SubjectId == "op-1"
                        && item.Key == "operation.plannedTarget.PlannedSuccessGoto");
                Assert.AreEqual("inspect_process", opFact.SourceTool);
                Assert.AreEqual("/data/operationDetails/operations/0/operaType", opFact.EvidencePath);
                Assert.AreEqual("true", placeholderFact.Value);
                Assert.AreEqual("2", plannedCountFact.Value);
                Assert.AreEqual("op:op-success", plannedSuccessFact.Value);
            }
        }

        [TestMethod]
        public void DeveloperWrite_RequiresEditorProfileAndSourceDevelopmentCapability()
        {
            // 非 Editor 权限外壳一律拒绝写文件。
            Assert.IsTrue(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Diagnostic, AutomationToolProfiles.SourceDevelopment, "write"));
            Assert.IsTrue(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.RuntimeDiagnostic, AutomationToolProfiles.SourceDevelopment, "edit"));
            // Editor 外壳下只有源码开发能力可以写文件。
            Assert.IsTrue(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Editor, AutomationToolProfiles.SourceReview, "write"));
            Assert.IsTrue(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Editor, AutomationToolProfiles.ProcessReview, "edit"));
            Assert.IsFalse(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Editor, AutomationToolProfiles.SourceDevelopment, "write"));
            // 读取工具不受任何拦截。
            Assert.IsFalse(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Diagnostic, AutomationToolProfiles.SourceDevelopment, "read"));
            Assert.IsFalse(FrmAiAssistant.IsDeveloperWriteBlockedByCapability(
                AutomationToolProfiles.Editor, AutomationToolProfiles.SourceReview, "read"));
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

        private static JObject BuildDecisionSchemaBranch(string action, bool includeFindingIds)
        {
            var properties = new JObject
            {
                ["action"] = new JObject { ["enum"] = new JArray(action) }
            };
            if (includeFindingIds)
            {
                properties["findingIds"] = new JObject();
            }
            else
            {
                properties["reviewHandoff"] = new JObject
                {
                    ["properties"] = new JObject
                    {
                        ["findings"] = new JObject
                        {
                            ["items"] = new JObject
                            {
                                ["properties"] = new JObject
                                {
                                    ["evidenceFactRefs"] = new JObject()
                                }
                            }
                        }
                    }
                };
            }
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = properties
            };
        }

        private static JObject BuildStableControlSchema(bool reversePropertyOrder)
        {
            var branches = new JArray(BuildDecisionSchemaBranch("run_stage", true));
            return reversePropertyOrder
                ? new JObject { ["oneOf"] = branches, ["type"] = "object" }
                : new JObject { ["type"] = "object", ["oneOf"] = branches };
        }
    }
}
