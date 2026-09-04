using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

// 模块：核心测试 / 设备拓扑推断。
// 职责范围：固化类型参数规则、候选幂等性和 AI 证据白名单边界。

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class TopologyInferenceTests
    {
        [TestMethod]
        public void ReviewState_MissingValueFailsClosedAndMissingJsonFieldIsRejected()
        {
            Assert.AreEqual("candidate", new EquipmentTopologyNode().ReviewState);
            Assert.AreEqual("candidate", new EquipmentTopologyRelation().ReviewState);
            Assert.AreEqual("candidate", new EquipmentTopologyStateBinding().ReviewState);
            Assert.AreEqual("candidate", new EquipmentTopologySkillBinding().ReviewState);

            Assert.ThrowsExactly<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<EquipmentTopologyNode>("{\"id\":\"node-without-review\"}"));
        }

        [TestMethod]
        public void Store_IoBindingOnlyAcceptsImplementedBooleanContract()
        {
            var definition = new EquipmentTopologyDefinition();
            var binding = new EquipmentTopologyStateBinding
            {
                Id = "binding-io-contract",
                StateName = "测试状态",
                SourceKind = "io",
                ResourceRef = "DI_TEST",
                Operator = "greater_than",
                ExpectedValue = "true",
                Meaning = "测试"
            };
            definition.Nodes.Add(new EquipmentTopologyNode
            {
                Id = "node-io-contract",
                Label = "测试节点",
                Kind = "sensor",
                StateBindings = { binding }
            });

            Assert.IsFalse(EquipmentTopologyStore.TryValidateDefinition(definition, out string operatorError));
            StringAssert.Contains(operatorError, "比较方式");

            binding.Operator = "equals";
            binding.ExpectedValue = "任意文字";
            Assert.IsFalse(EquipmentTopologyStore.TryValidateDefinition(definition, out string valueError));
            StringAssert.Contains(valueError, "明确的布尔值");

            binding.ExpectedValue = "off";
            Assert.IsTrue(EquipmentTopologyStore.TryValidateDefinition(definition, out string acceptedError), acceptedError);
        }

        [TestMethod]
        public void Store_RejectsVisionResourceAndStateBindingInCurrentContract()
        {
            var definition = new EquipmentTopologyDefinition();
            var node = new EquipmentTopologyNode
            {
                Id = "unsupported-resource",
                Label = "不支持的资源",
                Kind = "sensor",
                ResourceKind = "vision"
            };
            definition.Nodes.Add(node);

            Assert.IsFalse(EquipmentTopologyStore.TryValidateDefinition(definition, out string resourceError));
            StringAssert.Contains(resourceError, "当前版本不支持视觉资源");

            node.ResourceKind = string.Empty;
            node.StateBindings.Add(new EquipmentTopologyStateBinding
            {
                Id = "unsupported-binding",
                StateName = "检测完成",
                SourceKind = "vision",
                ResourceRef = "unsupported-source",
                Operator = "equals",
                ExpectedValue = "true",
                Meaning = "不应保存"
            });

            Assert.IsFalse(EquipmentTopologyStore.TryValidateDefinition(definition, out string bindingError));
            StringAssert.Contains(bindingError, "字段不完整或类型无效");
        }

        [TestMethod]
        public void Generate_UsesTypedParametersAndExcludesDisabledOperations()
        {
            Proc process = CreateIoProcess();

            TopologyRuleInferenceResult result = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(),
                new[] { process });

            Assert.AreEqual(3, result.ScannedOperationCount);
            Assert.AreEqual(1, result.DisabledOperationCount);
            Assert.AreEqual(2, result.NewNodeIds.Count);
            Assert.AreEqual(2, result.NewBindingIds.Count);
            Assert.AreEqual(1, result.NewSkillIds.Count);
            Assert.AreEqual(1, result.NewRelationIds.Count);
            CollectionAssert.AreEquivalent(
                new[] { "DO_VACUUM_01", "DI_VACUUM_OK_01" },
                result.CandidateDefinition.Nodes.Select(item => item.ResourceRef).ToArray());
            Assert.IsFalse(result.CandidateDefinition.Nodes.Any(item =>
                item.Label.Contains("错误名称") || item.ResourceRef == "DO_DISABLED"));
            Assert.IsTrue(result.CandidateDefinition.Nodes.All(item => item.ReviewState == "candidate"));
            Assert.IsTrue(result.Facts.All(item => item.OperationType == "IoOperate"
                || item.OperationType == "IoCheck"));
            EquipmentTopologySkillBinding skill = result.CandidateDefinition.Nodes
                .Single(item => item.ResourceRef == "DO_VACUUM_01").Skills.Single();
            Assert.AreEqual(process.head.Id.ToString("D"), skill.ProcessId);
            Assert.AreEqual(process.steps[0].Ops[0].Id.ToString("D"), skill.OperationId);
            Assert.AreEqual("candidate", skill.ReviewState);
        }

        [TestMethod]
        public void Generate_RepeatedScanDoesNotDuplicateCandidates()
        {
            Proc process = CreateIoProcess();
            TopologyRuleInferenceResult first = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(),
                new[] { process });

            TopologyRuleInferenceResult second = TopologyRuleInferenceService.Generate(
                first.CandidateDefinition,
                new[] { process });

            Assert.AreEqual(0, second.NewNodeIds.Count);
            Assert.AreEqual(0, second.NewBindingIds.Count);
            Assert.AreEqual(0, second.NewSkillIds.Count);
            Assert.AreEqual(0, second.NewRelationIds.Count);
            Assert.AreEqual(2, second.CandidateDefinition.Nodes.Count);
            Assert.AreEqual(1, second.CandidateDefinition.Relations.Count);
        }

        [TestMethod]
        public void ApplyResponse_RejectsUnknownEvidenceButAcceptsWhitelistedFact()
        {
            TopologyRuleInferenceResult rules = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(),
                new[] { CreateIoProcess() });
            string factId = rules.Facts.First(item => item.EligibleForTopology).FactId;
            var response = new JObject
            {
                ["summary"] = "测试精修",
                ["proposals"] = new JArray
                {
                    new JObject
                    {
                        ["action"] = "node.add",
                        ["key"] = "vacuum_mechanism",
                        ["label"] = "真空执行机构候选",
                        ["kind"] = "mechanism",
                        ["reviewState"] = "candidate",
                        ["confidence"] = 0.8,
                        ["evidenceIds"] = new JArray(factId)
                    },
                    new JObject
                    {
                        ["action"] = "node.add",
                        ["key"] = "fabricated",
                        ["label"] = "伪造对象",
                        ["kind"] = "mechanism",
                        ["reviewState"] = "candidate",
                        ["confidence"] = 0.9,
                        ["evidenceIds"] = new JArray("fact-not-exists")
                    }
                }
            };

            TopologyAiRefinementResult refined = TopologyAiRefinementService.ApplyResponse(
                rules,
                response.ToString());

            Assert.AreEqual(1, refined.AddedNodeCount);
            Assert.AreEqual(1, refined.RejectedProposalCount);
            Assert.AreEqual(1, refined.RejectionReasons.Count);
            StringAssert.Contains(refined.RejectionReasons[0], "未知或已排除的证据");
            StringAssert.Contains(refined.BuildSummary(), "未知或已排除的证据");
            Assert.IsTrue(refined.CandidateDefinition.Nodes.Any(item =>
                item.Label == "真空执行机构候选" && item.ReviewState == "candidate"));
            Assert.IsFalse(refined.CandidateDefinition.Nodes.Any(item => item.Label == "伪造对象"));
        }

        [TestMethod]
        public void ApplyResponse_RejectsEvidenceUnrelatedToBindingTargetOrRelationEndpoint()
        {
            TopologyRuleInferenceResult rules = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(), new[] { CreateIoProcess() });
            TopologyInferenceFact outputFact = rules.Facts.Single(item =>
                item.RuleId == "io.output.write" && item.EligibleForTopology);
            EquipmentTopologyNode outputNode = rules.CandidateDefinition.Nodes.Single(item =>
                item.ResourceRef == "DO_VACUUM_01");
            EquipmentTopologyNode inputNode = rules.CandidateDefinition.Nodes.Single(item =>
                item.ResourceRef == "DI_VACUUM_OK_01");
            var response = new JObject
            {
                ["summary"] = "越界证据测试",
                ["proposals"] = new JArray
                {
                    new JObject
                    {
                        ["action"] = "stateBinding.add",
                        ["targetRef"] = inputNode.Id,
                        ["sourceKind"] = "io",
                        ["resourceRef"] = outputNode.ResourceRef,
                        ["operator"] = "equals",
                        ["expectedValue"] = "true",
                        ["stateName"] = "错误绑定",
                        ["meaning"] = "证据与目标节点无关",
                        ["reviewState"] = "candidate",
                        ["confidence"] = 0.8,
                        ["evidenceIds"] = new JArray(outputFact.FactId)
                    },
                    new JObject
                    {
                        ["action"] = "relation.add",
                        ["sourceRef"] = outputNode.Id,
                        ["targetRef"] = inputNode.Id,
                        ["layer"] = "state",
                        ["kind"] = "feedback_of",
                        ["label"] = "错误关系",
                        ["reviewState"] = "candidate",
                        ["confidence"] = 0.8,
                        ["evidenceIds"] = new JArray(outputFact.FactId)
                    }
                }
            };

            TopologyAiRefinementResult refined = TopologyAiRefinementService.ApplyResponse(
                rules, response.ToString());

            Assert.AreEqual(2, refined.RejectedProposalCount);
            Assert.IsTrue(refined.RejectionReasons.Any(item => item.Contains("目标节点")));
            Assert.IsTrue(refined.RejectionReasons.Any(item => item.Contains("两端节点")));
            Assert.IsFalse(refined.CandidateDefinition.Nodes.Single(item => item.Id == inputNode.Id)
                .StateBindings.Any(item => item.StateName == "错误绑定"));
            Assert.IsFalse(refined.CandidateDefinition.Relations.Any(item => item.Label == "错误关系"));
        }

        [TestMethod]
        public void Generate_ExplicitPhysicalOperationsCreateSkillsAndIncompleteParametersDoNotGuess()
        {
            var relative = new StationRunRel
            {
                Id = Guid.NewGuid(), Name = "显示名不得参与推断", StationName = "ST_REL", Axis1 = 12.5
            };
            var tray = new TrayRunPos
            {
                Id = Guid.NewGuid(), Name = "任意名称", StationName = "ST_TRAY", TrayId = 0, TrayPos = 2
            };
            var modify = new ModifyStationPos
            {
                Id = Guid.NewGuid(), Name = "任意名称", StationName = "ST_EDIT",
                RefPosName = "当前位置", TargetPosName = "P_TARGET", ModifyType = "叠加"
            };
            var velocity = new SetStationVel
            {
                Id = Guid.NewGuid(), Name = "任意名称", StationName = "ST_VEL", SetAxisObj = "工站",
                Vel = 50, Acc = 40, Dec = 30
            };
            var stop = new StationStop
            {
                Id = Guid.NewGuid(), Name = "任意名称", StationName = "ST_STOP", StopEntireStation = true
            };
            var plc = new PlcMappingControl
            {
                Id = Guid.NewGuid(), Name = "任意名称", DeviceName = "PLC_MAIN"
            };
            var incomplete = new StationRunRel
            {
                Id = Guid.NewGuid(), Name = "FAKE_STATION_FROM_NAME", StationName = "ST_INCOMPLETE"
            };
            Proc process = CreateProcess(relative, tray, modify, velocity, stop, plc, incomplete);

            TopologyRuleInferenceResult result = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(), new[] { process });

            string[] expectedOperationIds =
            {
                relative.Id.ToString("D"), tray.Id.ToString("D"), modify.Id.ToString("D"),
                velocity.Id.ToString("D"), stop.Id.ToString("D"), plc.Id.ToString("D")
            };
            CollectionAssert.AreEquivalent(expectedOperationIds,
                result.CandidateDefinition.Nodes.SelectMany(item => item.Skills)
                    .Select(item => item.OperationId).ToArray());
            CollectionAssert.IsSubsetOf(
                new[] { "ST_REL", "ST_TRAY", "ST_EDIT", "ST_EDIT/P_TARGET", "ST_VEL", "ST_STOP", "PLC_MAIN" },
                result.CandidateDefinition.Nodes.Select(item => item.ResourceRef).ToArray());
            Assert.IsFalse(result.CandidateDefinition.Nodes.Any(item =>
                item.ResourceRef == "ST_INCOMPLETE" || item.ResourceRef == "FAKE_STATION_FROM_NAME"));
            Assert.IsFalse(result.Facts.Any(item => item.OpId == incomplete.Id.ToString("D")));
            Assert.IsTrue(result.CandidateDefinition.Nodes.SelectMany(item => item.Evidence)
                .All(item => item.SourceType == "rule" && !string.IsNullOrWhiteSpace(item.SourceRef)));
        }

        [TestMethod]
        public void Generate_RescanPrunesUnsupportedRuleCandidatesAndPreservesConfirmedOrUnknownSource()
        {
            TopologyRuleInferenceResult first = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(), new[] { CreateIoProcess() });
            EquipmentTopologyNode confirmed = first.CandidateDefinition.Nodes
                .Single(item => item.ResourceRef == "DO_VACUUM_01");
            confirmed.ReviewState = "confirmed";
            foreach (EquipmentTopologyStateBinding binding in confirmed.StateBindings)
                binding.ReviewState = "confirmed";
            first.CandidateDefinition.Nodes.Add(new EquipmentTopologyNode
            {
                Id = "manual-candidate",
                Label = "人工候选",
                Kind = "mechanism",
                ReviewState = "candidate",
                Confidence = 0.8
            });

            TopologyRuleInferenceResult rescanned = TopologyRuleInferenceService.Generate(
                first.CandidateDefinition, Array.Empty<Proc>());

            CollectionAssert.AreEquivalent(new[] { "DO_VACUUM_01", null },
                rescanned.CandidateDefinition.Nodes.Select(item => item.ResourceRef).ToArray());
            Assert.IsTrue(rescanned.CandidateDefinition.Nodes.Any(item => item.Id == "manual-candidate"));
            Assert.AreEqual(1, rescanned.RemovedNodeCount);
            Assert.AreEqual(1, rescanned.RemovedBindingCount);
            Assert.AreEqual(1, rescanned.RemovedRelationCount);
            Assert.AreEqual(1, rescanned.RemovedSkillCount);
        }

        [TestMethod]
        public void Generate_RescanPreservesManuallyDetachedRuleCandidateWithoutIdCollision()
        {
            Proc process = CreateIoProcess();
            TopologyRuleInferenceResult first = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(), new[] { process });
            EquipmentTopologyNode edited = first.CandidateDefinition.Nodes.Single(item =>
                item.ResourceRef == "DO_VACUUM_01");
            string generatedNodeId = edited.Id;
            string previousNodeId = edited.Id;
            edited.Id = "node-manual-detached";
            edited.ResourceRef = "DO_USER_EDIT";
            edited.Evidence.Add(ManualEditEvidence());
            foreach (EquipmentTopologyStateBinding binding in edited.StateBindings)
            {
                binding.Id = "binding-manual-" + Guid.NewGuid().ToString("N");
                binding.Evidence.Add(ManualEditEvidence());
            }
            foreach (EquipmentTopologySkillBinding skill in edited.Skills)
            {
                skill.Id = "skill-manual-" + Guid.NewGuid().ToString("N");
                skill.Evidence.Add(ManualEditEvidence());
            }
            foreach (EquipmentTopologyRelation relation in first.CandidateDefinition.Relations.Where(item =>
                item.SourceNodeId == previousNodeId || item.TargetNodeId == previousNodeId))
            {
                if (relation.SourceNodeId == previousNodeId) relation.SourceNodeId = edited.Id;
                if (relation.TargetNodeId == previousNodeId) relation.TargetNodeId = edited.Id;
                relation.Id = "relation-manual-" + Guid.NewGuid().ToString("N");
                relation.Evidence.Add(ManualEditEvidence());
            }

            TopologyRuleInferenceResult rescanned = TopologyRuleInferenceService.Generate(
                first.CandidateDefinition, new[] { process });

            Assert.IsTrue(rescanned.CandidateDefinition.Nodes.Any(item =>
                item.Id == "node-manual-detached" && item.ResourceRef == "DO_USER_EDIT"));
            Assert.IsTrue(rescanned.CandidateDefinition.Nodes.Any(item =>
                item.Id == generatedNodeId && item.ResourceRef == "DO_VACUUM_01"));
            Assert.AreEqual(rescanned.CandidateDefinition.Nodes.Count,
                rescanned.CandidateDefinition.Nodes.Select(item => item.Id).Distinct().Count());
            Assert.AreEqual(rescanned.CandidateDefinition.Nodes.SelectMany(item => item.StateBindings).Count(),
                rescanned.CandidateDefinition.Nodes.SelectMany(item => item.StateBindings)
                    .Select(item => item.Id).Distinct().Count());
            Assert.AreEqual(rescanned.CandidateDefinition.Nodes.SelectMany(item => item.Skills).Count(),
                rescanned.CandidateDefinition.Nodes.SelectMany(item => item.Skills)
                    .Select(item => item.Id).Distinct().Count());
            Assert.AreEqual(rescanned.CandidateDefinition.Relations.Count,
                rescanned.CandidateDefinition.Relations.Select(item => item.Id).Distinct().Count());
        }

        [TestMethod]
        public void ApplyResponse_AllowsEvidenceBoundSingleOperationSkillAndReportsRejectedMode()
        {
            TopologyRuleInferenceResult rules = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(), new[] { CreateIoProcess() });
            TopologyInferenceFact fact = rules.Facts.Single(item =>
                item.RuleId == "io.input.check" && item.EligibleForTopology);
            EquipmentTopologyNode node = rules.CandidateDefinition.Nodes
                .Single(item => item.ResourceRef == fact.SubjectRef);
            JObject valid = BuildSkillProposal(node.Id, fact, MachineExecutionModes.SingleOperation);
            JObject invalid = BuildSkillProposal(node.Id, fact, MachineExecutionModes.ContinueFlow);

            TopologyAiRefinementResult refined = TopologyAiRefinementService.ApplyResponse(rules,
                new JObject
                {
                    ["summary"] = "技能候选测试",
                    ["proposals"] = new JArray(valid, invalid)
                }.ToString());

            Assert.AreEqual(1, refined.AddedSkillCount);
            Assert.AreEqual(1, refined.RejectedProposalCount);
            StringAssert.Contains(refined.RejectionReasons.Single(), "executionMode");
            EquipmentTopologySkillBinding skill = refined.CandidateDefinition.Nodes
                .Single(item => item.Id == node.Id).Skills.Single();
            Assert.AreEqual(fact.ProcId, skill.ProcessId);
            Assert.AreEqual(fact.OpId, skill.OperationId);
            Assert.AreEqual("candidate", skill.ReviewState);
            Assert.AreEqual("ai_refinement", skill.Evidence.Single().SourceType);
        }

        [TestMethod]
        public void Generate_EmitsExplicitEvidenceForAlarmRecoveryRoute()
        {
            var source = new IoCheck
            {
                Id = Guid.NewGuid(),
                AlarmType = "自动处理",
                Goto1 = "0-0-2",
                IoParams = new OperationTypePartial.CustomList<IoCheckParam>
                {
                    new IoCheckParam { IoName = "DI_ACTION_OK", ExpectedState = true }
                }
            };
            var normal = new IoOperate
            {
                Id = Guid.NewGuid(),
                IoParams = new OperationTypePartial.CustomList<IoOutParam>
                {
                    new IoOutParam { IoName = "DO_NORMAL", TargetState = true }
                }
            };
            var recovery = new IoOperate
            {
                Id = Guid.NewGuid(),
                IoParams = new OperationTypePartial.CustomList<IoOutParam>
                {
                    new IoOutParam { IoName = "DO_RECOVERY", TargetState = false }
                }
            };
            var process = new Proc
            {
                head = new ProcHead { Id = Guid.NewGuid() },
                steps =
                {
                    new Step
                    {
                        Id = Guid.NewGuid(),
                        Ops = { source, normal, recovery }
                    }
                }
            };

            TopologyRuleInferenceResult result = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(),
                new[] { process });

            TopologyInferenceFact route = result.Facts.Single(item =>
                item.RuleId == "control.alarm_route"
                && item.SubjectRef == source.Id.ToString("D")
                && item.ObjectRef == recovery.Id.ToString("D"));
            Assert.AreEqual("recovery_path", route.ControlFlowRole);
            Assert.IsTrue(route.EligibleForTopology);
            Assert.IsTrue(result.Facts.Any(item => item.OpId == recovery.Id.ToString("D")
                && item.ControlFlowRole == "recovery"));
        }

        [TestMethod]
        public void Generate_ControlFlowFromAuxiliaryInstructionIsNotEligibleForTopology()
        {
            var delay = new Delay
            {
                Id = Guid.NewGuid(),
                DelayMs = 10,
                AlarmType = "自动处理",
                Goto1 = "0-0-1"
            };
            var output = new IoOperate
            {
                Id = Guid.NewGuid(),
                IoParams = new OperationTypePartial.CustomList<IoOutParam>
                {
                    new IoOutParam { IoName = "DO_AFTER_DELAY", TargetState = true }
                }
            };

            TopologyRuleInferenceResult result = TopologyRuleInferenceService.Generate(
                new EquipmentTopologyDefinition(), new[] { CreateProcess(delay, output) });

            TopologyInferenceFact route = result.Facts.Single(item =>
                item.OpId == delay.Id.ToString("D") && item.RuleId == "control.alarm_route");
            Assert.AreEqual(1, result.AuxiliaryOperationCount);
            Assert.IsFalse(route.EligibleForTopology);
        }

        private static Proc CreateIoProcess()
        {
            return new Proc
            {
                head = new ProcHead { Id = Guid.NewGuid(), Name = "流程显示名不得参与推断" },
                steps =
                {
                    new Step
                    {
                        Id = Guid.NewGuid(),
                        Name = "步骤显示名不得参与推断",
                        Ops =
                        {
                            new IoOperate
                            {
                                Id = Guid.NewGuid(),
                                Name = "错误名称：夹爪松开",
                                IoParams = new OperationTypePartial.CustomList<IoOutParam>
                                {
                                    new IoOutParam { IoName = "DO_VACUUM_01", TargetState = true }
                                }
                            },
                            new IoCheck
                            {
                                Id = Guid.NewGuid(),
                                Name = "错误名称：任意检测",
                                IoParams = new OperationTypePartial.CustomList<IoCheckParam>
                                {
                                    new IoCheckParam { IoName = "DI_VACUUM_OK_01", ExpectedState = true }
                                }
                            },
                            new IoOperate
                            {
                                Id = Guid.NewGuid(),
                                Disable = true,
                                IoParams = new OperationTypePartial.CustomList<IoOutParam>
                                {
                                    new IoOutParam { IoName = "DO_DISABLED", TargetState = true }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static Proc CreateProcess(params OperationType[] operations)
        {
            var process = new Proc
            {
                head = new ProcHead { Id = Guid.NewGuid(), Name = "名称不得参与推断" }
            };
            var step = new Step { Id = Guid.NewGuid(), Name = "名称不得参与推断" };
            foreach (OperationType operation in operations ?? Array.Empty<OperationType>())
                step.Ops.Add(operation);
            process.steps.Add(step);
            return process;
        }

        private static EquipmentTopologyEvidence ManualEditEvidence()
        {
            return new EquipmentTopologyEvidence
            {
                SourceType = "manual_edit",
                SourceRef = "topology-editor",
                Detail = "人工修改后脱离规则重扫管理"
            };
        }

        private static JObject BuildSkillProposal(
            string nodeId,
            TopologyInferenceFact fact,
            string executionMode)
        {
            return new JObject
            {
                ["action"] = "skill.add",
                ["targetRef"] = nodeId,
                ["name"] = "检查输入状态",
                ["description"] = "只绑定已有指令，不执行控制",
                ["processId"] = fact.ProcId,
                ["operationId"] = fact.OpId,
                ["executionMode"] = executionMode,
                ["objective"] = "读取既有输入检查指令",
                ["expectedOutcome"] = "指令按已有参数完成检查",
                ["preconditions"] = new JArray("目标节点与规则证据一致"),
                ["reviewState"] = "candidate",
                ["confidence"] = 0.8,
                ["evidenceIds"] = new JArray(fact.FactId)
            };
        }
    }
}
