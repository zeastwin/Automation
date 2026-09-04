using Microsoft.VisualStudio.TestTools.UnitTesting;
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
            Assert.AreEqual(1, result.NewRelationIds.Count);
            CollectionAssert.AreEquivalent(
                new[] { "DO_VACUUM_01", "DI_VACUUM_OK_01" },
                result.CandidateDefinition.Nodes.Select(item => item.ResourceRef).ToArray());
            Assert.IsFalse(result.CandidateDefinition.Nodes.Any(item =>
                item.Label.Contains("错误名称") || item.ResourceRef == "DO_DISABLED"));
            Assert.IsTrue(result.CandidateDefinition.Nodes.All(item => item.ReviewState == "candidate"));
            Assert.IsTrue(result.Facts.All(item => item.OperationType == "IoOperate"
                || item.OperationType == "IoCheck"));
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
            Assert.IsTrue(refined.CandidateDefinition.Nodes.Any(item =>
                item.Label == "真空执行机构候选" && item.ReviewState == "candidate"));
            Assert.IsFalse(refined.CandidateDefinition.Nodes.Any(item => item.Label == "伪造对象"));
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
    }
}
