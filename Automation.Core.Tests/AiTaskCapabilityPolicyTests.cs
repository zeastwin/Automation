using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AiTaskCapabilityPolicyTests
    {
        [TestMethod]
        public void ValidRequest_GrantsExactlyOneExecutionCapability()
        {
            AiTaskDecisionValidation result = Validate("run_stage", AutomationToolProfiles.ProcessReview);

            Assert.AreEqual(AiTaskDecisionKind.RunStage, result.Kind);
            Assert.AreEqual(AutomationToolProfiles.ProcessReview, result.Stage.Profile);
            Assert.AreEqual("检查当前流程事实", result.Stage.Objective);
        }

        [TestMethod]
        public void MissingStructuredDecision_IsInvalidAndHasNoFallback()
        {
            AiTaskDecisionValidation result = AiTaskCapabilityPolicy.Validate(
                null,
                "修改流程",
                AutomationToolProfiles.Editor,
                false,
                new AiDynamicTaskState());

            Assert.AreEqual(AiTaskDecisionKind.Invalid, result.Kind);
            Assert.IsNull(result.Stage);
            StringAssert.Contains(result.Message, "没有提交");
        }

        [TestMethod]
        public void Diagnostic_DoesNotDowngradeMutationToReview()
        {
            AiTaskDecisionValidation result = Validate(
                "run_stage",
                AutomationToolProfiles.ProcessEdit,
                AutomationToolProfiles.Diagnostic);

            Assert.AreEqual(AiTaskDecisionKind.Invalid, result.Kind);
            Assert.IsNull(result.Stage);
            StringAssert.Contains(result.Message, "不允许");
        }

        [TestMethod]
        public void HighRiskSideEffectCapability_RequiresAuthorizationQuoteFromCurrentMessage()
        {
            var decision = new TaskCapabilityDecisionDefinition
            {
                Version = 1,
                Action = "run_stage",
                Capability = AutomationToolProfiles.RuntimeControl,
                Objective = "启动流程",
                AuthorizationQuote = "之前已经同意",
                Message = string.Empty
            };

            AiTaskDecisionValidation result = AiTaskCapabilityPolicy.Validate(
                decision,
                "检查当前流程",
                AutomationToolProfiles.Editor,
                false,
                new AiDynamicTaskState());

            Assert.AreEqual(AiTaskDecisionKind.Invalid, result.Kind);
            StringAssert.Contains(result.Message, "当前用户消息");
        }

        [TestMethod]
        public void OrdinaryProcessMutation_DoesNotRequireLiteralAuthorizationQuote()
        {
            var decision = new TaskCapabilityDecisionDefinition
            {
                Version = 1,
                Action = "run_stage",
                Capability = AutomationToolProfiles.ProcessEdit,
                Objective = "完善当前上料流程",
                Basis = TaskDecisionBases.DirectUserChange
            };

            AiTaskDecisionValidation result = AiTaskCapabilityPolicy.Validate(
                decision,
                "帮我把当前上料流程完善一下",
                AutomationToolProfiles.Editor,
                false,
                new AiDynamicTaskState());

            Assert.AreEqual(AiTaskDecisionKind.RunStage, result.Kind);
        }

        [TestMethod]
        public void ReadOnlyCapability_DoesNotRequireAuthorizationQuote()
        {
            var decision = new TaskCapabilityDecisionDefinition
            {
                Version = 1,
                Action = "run_stage",
                Capability = AutomationToolProfiles.ProcessReview,
                Objective = "检查流程",
                AuthorizationQuote = string.Empty,
                Message = string.Empty
            };

            Assert.AreEqual(
                AiTaskDecisionKind.RunStage,
                AiTaskCapabilityPolicy.Validate(
                    decision,
                    "检查当前流程",
                    AutomationToolProfiles.Editor,
                    false,
                    new AiDynamicTaskState()).Kind);
        }

        [TestMethod]
        public void PlatformConfiguration_RequiresFullPermission()
        {
            AiTaskDecisionValidation blocked = Validate(
                "run_stage",
                AutomationToolProfiles.PlatformConfiguration,
                AutomationToolProfiles.Editor,
                fullPermission: false);
            AiTaskDecisionValidation allowed = Validate(
                "run_stage",
                AutomationToolProfiles.PlatformConfiguration,
                AutomationToolProfiles.Editor,
                fullPermission: true);

            Assert.AreEqual(AiTaskDecisionKind.Invalid, blocked.Kind);
            Assert.AreEqual(AiTaskDecisionKind.RunStage, allowed.Kind);
        }

        [TestMethod]
        public void ReviewWithoutCurrentFacts_CannotRequestSideEffectCapability()
        {
            var state = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                state,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ProcessReview, Objective = "检查" },
                new AiTurnEvidence(true, false, false, false, false, false, false, 0));

            AiTaskDecisionValidation result = Validate(
                "run_stage",
                AutomationToolProfiles.ProcessEdit,
                state: state);

            Assert.AreEqual(AiTaskDecisionKind.Invalid, result.Kind);
            StringAssert.Contains(result.Message, "当前状态读取证据");
        }

        [TestMethod]
        public void ReviewWithCurrentFacts_CanRequestMutation()
        {
            var state = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                state,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ProcessReview, Objective = "检查" },
                new AiTurnEvidence(true, true, false, false, false, false, false, 0));

            Assert.AreEqual(
                AiTaskDecisionKind.RunStage,
                Validate("run_stage", AutomationToolProfiles.ProcessEdit, state: state).Kind);
        }

        [TestMethod]
        public void RuntimeAfterProcessMutation_RequiresCommittedEvidence()
        {
            var blockedState = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                blockedState,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ProcessEdit, Objective = "修改" },
                new AiTurnEvidence(
                    true, true, false, false, false, false, false, 0,
                    mutationAttempted: true));
            var allowedState = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                allowedState,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ProcessEdit, Objective = "修改" },
                new AiTurnEvidence(
                    true, true, true, true, false, false, false, 0,
                    mutationAttempted: true));

            Assert.AreEqual(
                AiTaskDecisionKind.Invalid,
                Validate("run_stage", AutomationToolProfiles.RuntimeControl, state: blockedState).Kind);
            Assert.AreEqual(
                AiTaskDecisionKind.RunStage,
                Validate("run_stage", AutomationToolProfiles.RuntimeControl, state: allowedState).Kind);
        }

        [TestMethod]
        public void PartialMutationFailure_BlocksFurtherSideEffectsButAllowsReview()
        {
            var state = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                state,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ResourceEdit, Objective = "新增报警" },
                new AiTurnEvidence(true, true, true, false, false, false, false, 1, false, true));

            Assert.AreEqual(
                AiTaskDecisionKind.Invalid,
                Validate("run_stage", AutomationToolProfiles.RuntimeControl, state: state).Kind);
            Assert.AreEqual(
                AiTaskDecisionKind.RunStage,
                Validate("run_stage", AutomationToolProfiles.ProcessReview, state: state).Kind);
        }

        [TestMethod]
        public void SourceWrite_MakesCurrentRuntimeIneligibleForAnotherStage()
        {
            var state = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                state,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.SourceDevelopment, Objective = "修改源码" },
                new AiTurnEvidence(false, false, false, false, false, false, true, 0));

            AiTaskDecisionValidation result = Validate(
                "run_stage", AutomationToolProfiles.ProcessReview, state: state);

            Assert.AreEqual(AiTaskDecisionKind.Invalid, result.Kind);
            StringAssert.Contains(result.Message, "运行实例已过期");
        }

        [TestMethod]
        public void SourceReadOnlyStage_DoesNotExpireCurrentRuntime()
        {
            var state = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                state,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.SourceDevelopment, Objective = "只读定位" },
                new AiTurnEvidence(true, true, false, false, false, false, false, 0));

            Assert.AreEqual(
                AiTaskDecisionKind.RunStage,
                Validate("run_stage", AutomationToolProfiles.ProcessReview, state: state).Kind);
        }

        [TestMethod]
        public void RecoveredSideEffectFreePreviewFailure_DoesNotBecomeUnsafePartialMutation()
        {
            var state = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                state,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ProcessEdit, Objective = "修改" },
                new AiTurnEvidence(true, true, true, true, false, false, false, 1, false, false));

            Assert.IsFalse(state.UnsafePartialMutation);
            Assert.IsFalse(state.UncommittedMutation);
            Assert.AreEqual(
                AiTaskDecisionKind.RunStage,
                Validate("run_stage", AutomationToolProfiles.RuntimeControl, state: state).Kind);
        }

        [TestMethod]
        public void MutationProfileWithoutWriteAttempt_DoesNotPoisonLaterStages()
        {
            var state = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                state,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ProcessCreate, Objective = "读取契约" },
                new AiTurnEvidence(true, true, false, false, false, false, false, 0));

            Assert.IsFalse(state.UncommittedMutation);
            Assert.IsNull(state.LastMutationProfile);
            Assert.AreEqual(
                AiTaskDecisionKind.RunStage,
                Validate("run_stage", AutomationToolProfiles.ProcessReview, state: state).Kind);
        }

        [TestMethod]
        public void CommittedProcessSkeleton_CanContinueInProcessEdit()
        {
            var state = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                state,
                new AiTaskCapabilityStage
                {
                    Profile = AutomationToolProfiles.ProcessCreate,
                    Objective = "创建不可运行的流程骨架"
                },
                new AiTurnEvidence(
                    true, true, true, true, false, false, false, 0,
                    designKnowledgeReadSucceeded: true,
                    mutationAttempted: true,
                    previewCreated: true,
                    verificationSucceeded: true));

            AiTaskDecisionValidation result = Validate(
                "run_stage",
                AutomationToolProfiles.ProcessEdit,
                state: state);

            Assert.AreEqual(AiTaskDecisionKind.RunStage, result.Kind);
            Assert.IsFalse(state.UncommittedMutation);
            Assert.IsFalse(state.UnsafePartialMutation);
        }

        [TestMethod]
        public void ContinuedStage_MergesEvidenceBeforeSingleFinalRecord()
        {
            AiTurnEvidence firstTurn = new AiTurnEvidence(
                true, true, false, false, false, false, false, 1,
                previewCreated: true);
            AiTurnEvidence secondTurn = new AiTurnEvidence(
                true, true, true, true, false, false, false, 0,
                mutationAttempted: true,
                verificationSucceeded: true);
            AiTurnEvidence merged = AiTurnEvidence.Merge(firstTurn, secondTurn);
            var state = new AiDynamicTaskState();

            AiTaskCapabilityPolicy.RecordStage(
                state,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ProcessEdit, Objective = "继续并提交" },
                merged);

            Assert.AreEqual(1, state.CompletedStageCount);
            Assert.IsTrue(merged.PreviewCreated);
            Assert.IsTrue(merged.ChangeSetCommitted);
            Assert.IsTrue(merged.VerificationSucceeded);
            Assert.AreEqual(1, merged.ToolFailureCount);
            Assert.IsFalse(state.UncommittedMutation);
        }

        [TestMethod]
        public void ReviewWithoutProvenDefect_CannotAuthorizeFindingBasedEdit()
        {
            var state = new AiDynamicTaskState
            {
                LastReviewHandoff = new ReviewHandoffDefinition
                {
                    Status = ReviewHandoffStatuses.NoDefect,
                    Summary = "没有证明缺陷"
                }
            };
            var decision = new TaskCapabilityDecisionDefinition
            {
                Version = 1,
                Action = "run_stage",
                Capability = AutomationToolProfiles.ProcessEdit,
                Objective = "按评审修复",
                Basis = TaskDecisionBases.ProvenReviewFinding,
                FindingIds = new System.Collections.Generic.List<string> { "F1" }
            };

            AiTaskDecisionValidation result = AiTaskCapabilityPolicy.Validate(
                decision, "按刚才结论修复", AutomationToolProfiles.Editor, false, state);

            Assert.AreEqual(AiTaskDecisionKind.Invalid, result.Kind);
            StringAssert.Contains(result.Message, "proven_defect");
        }

        [TestMethod]
        public void ProvenReviewFinding_AllowsOnlyMatchingFindingIds()
        {
            var handoff = new ReviewHandoffDefinition
            {
                Status = ReviewHandoffStatuses.ProvenDefect,
                Summary = "证明一个缺陷",
                Findings = new System.Collections.Generic.List<ReviewFindingDefinition>
                {
                    new ReviewFindingDefinition
                    {
                        Id = "F1",
                        Summary = "跳转目标不存在",
                        Category = ReviewFindingCategories.StructuralDefect,
                        Repairability = ReviewFindingRepairability.SafeWithoutExternalFacts,
                        TargetIds = new System.Collections.Generic.List<string> { "op-1" },
                        Evidence = "Readiness 返回未解析目标",
                        EvidenceFactRefs = new System.Collections.Generic.List<string>
                        {
                            ReviewFactReference.Build("op-1", "operation.reachable")
                        },
                        MinimalChange = "替换该跳转目标"
                    }
                },
                VerifiedFacts = new System.Collections.Generic.List<ReviewVerifiedFactDefinition>
                {
                    Fact("op-1", "跳转指令", "operation.reachable", "false")
                }
            };
            var state = new AiDynamicTaskState { LastReviewHandoff = handoff };
            var decision = new TaskCapabilityDecisionDefinition
            {
                Version = 1,
                Action = "run_stage",
                Capability = AutomationToolProfiles.ProcessEdit,
                Objective = "修复 F1",
                Basis = TaskDecisionBases.ProvenReviewFinding,
                FindingIds = new System.Collections.Generic.List<string> { "F1" }
            };

            Assert.AreEqual(
                AiTaskDecisionKind.RunStage,
                AiTaskCapabilityPolicy.Validate(
                    decision, "按刚才结论修复", AutomationToolProfiles.Editor, false, state).Kind);
            decision.FindingIds[0] = "F2";
            Assert.AreEqual(
                AiTaskDecisionKind.Invalid,
                AiTaskCapabilityPolicy.Validate(
                    decision, "按刚才结论修复", AutomationToolProfiles.Editor, false, state).Kind);
        }

        [TestMethod]
        public void ProcessReviewDecision_RequiresStructuredHandoff()
        {
            AiTaskDecisionValidation missing = AiTaskCapabilityPolicy.Validate(
                new TaskCapabilityDecisionDefinition
                {
                    Version = 1,
                    Action = "finish",
                    Message = "检查完成"
                },
                "检查流程", AutomationToolProfiles.Editor, false,
                new AiDynamicTaskState(), AutomationToolProfiles.ProcessReview);

            Assert.AreEqual(AiTaskDecisionKind.Invalid, missing.Kind);
            StringAssert.Contains(missing.Message, "reviewHandoff");
        }

        [TestMethod]
        public void ProvenReviewFinding_MustReferenceHostVerifiedFact()
        {
            var handoff = new ReviewHandoffDefinition
            {
                Status = ReviewHandoffStatuses.ProvenDefect,
                Summary = "证明一个缺陷",
                Findings = new System.Collections.Generic.List<ReviewFindingDefinition>
                {
                    new ReviewFindingDefinition
                    {
                        Id = "F1",
                        Summary = "不可达指令",
                        Category = ReviewFindingCategories.StructuralDefect,
                        Repairability = ReviewFindingRepairability.SafeWithoutExternalFacts,
                        TargetIds = new System.Collections.Generic.List<string> { "op-1" },
                        Evidence = "流程图显示不可达",
                        EvidenceFactRefs = new System.Collections.Generic.List<string>
                        {
                            ReviewFactReference.Build("op-1", "operation.reachable")
                        },
                        MinimalChange = "修正入向跳转"
                    }
                },
                VerifiedFacts = new System.Collections.Generic.List<ReviewVerifiedFactDefinition>
                {
                    Fact("op-2", "其他指令", "operation.reachable", "false")
                }
            };

            string error = AiTaskCapabilityPolicy.ValidateReviewHandoff(
                handoff, AutomationToolProfiles.ProcessReview);

            StringAssert.Contains(error, "不存在的宿主机械事实");
        }

        [TestMethod]
        public void ProvenReviewFinding_ThatNeedsUserChoice_CannotAuthorizeEdit()
        {
            var handoff = new ReviewHandoffDefinition
            {
                Status = ReviewHandoffStatuses.ProvenDefect,
                Summary = "缺陷已证明但修复目标待选择",
                Findings = new System.Collections.Generic.List<ReviewFindingDefinition>
                {
                    new ReviewFindingDefinition
                    {
                        Id = "F1",
                        Summary = "跳转目标无效",
                        Category = ReviewFindingCategories.StructuralDefect,
                        Repairability = ReviewFindingRepairability.RequiresUserChoice,
                        TargetIds = new System.Collections.Generic.List<string> { "op-1" },
                        Evidence = "流程图返回无效目标",
                        EvidenceFactRefs = new System.Collections.Generic.List<string>
                        {
                            ReviewFactReference.Build("op-1", "operation.invalid")
                        },
                        MinimalChange = "选择一个有效目标"
                    }
                },
                VerifiedFacts = new System.Collections.Generic.List<ReviewVerifiedFactDefinition>
                {
                    Fact("op-1", "跳转指令", "operation.invalid", "true")
                }
            };
            var state = new AiDynamicTaskState { LastReviewHandoff = handoff };
            var decision = new TaskCapabilityDecisionDefinition
            {
                Version = 1,
                Action = "run_stage",
                Capability = AutomationToolProfiles.ProcessEdit,
                Objective = "修复 F1",
                Basis = TaskDecisionBases.ProvenReviewFinding,
                FindingIds = new System.Collections.Generic.List<string> { "F1" }
            };

            AiTaskDecisionValidation result = AiTaskCapabilityPolicy.Validate(
                decision, "修复已证明的问题", AutomationToolProfiles.Editor, false, state);

            Assert.AreEqual(AiTaskDecisionKind.Invalid, result.Kind);
            StringAssert.Contains(result.Message, "仍需要用户选择");
        }

        [TestMethod]
        public void ProcessDesign_MustReadKnowledgeBeforeFinishOrNextStage()
        {
            var blocked = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                blocked,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ProcessDesign, Objective = "设计扫码流程" },
                new AiTurnEvidence(false, false, false, false, false, false, false, 0));
            var allowed = new AiDynamicTaskState();
            AiTaskCapabilityPolicy.RecordStage(
                allowed,
                new AiTaskCapabilityStage { Profile = AutomationToolProfiles.ProcessDesign, Objective = "设计扫码流程" },
                new AiTurnEvidence(true, false, false, false, false, false, false, 0, true, false));

            Assert.AreEqual(
                AiTaskDecisionKind.Invalid,
                Validate("finish", "", state: blocked, message: "设计完成").Kind);
            Assert.AreEqual(
                AiTaskDecisionKind.Invalid,
                Validate("run_stage", AutomationToolProfiles.ProcessCreate, state: blocked).Kind);
            Assert.AreEqual(
                AiTaskDecisionKind.Finish,
                Validate("finish", "", state: allowed, message: "设计完成").Kind);
        }

        [TestMethod]
        public void FinishAndAskUser_DoNotGrantCapabilities()
        {
            AiTaskDecisionValidation finish = Validate("finish", "", message: "任务完成");
            AiTaskDecisionValidation ask = Validate("ask_user", "", message: "请提供流程名称");

            Assert.AreEqual(AiTaskDecisionKind.Finish, finish.Kind);
            Assert.IsNull(finish.Stage);
            Assert.AreEqual(AiTaskDecisionKind.AskUser, ask.Kind);
            Assert.IsNull(ask.Stage);
        }

        [TestMethod]
        public void AcceptedFinishMessage_WinsOverWorkerProcessSummary()
        {
            string result = AiConversationCoordinator.BuildFinalAssistantOutput(
                new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>(
                        AutomationToolProfiles.ProcessReview,
                        "这是工具调用前的重复过程总结")
                },
                "这是经过校验的最终答复");

            Assert.AreEqual("这是经过校验的最终答复", result);
        }

        [TestMethod]
        public void ReviewVerifiedFacts_AreRenderedAheadOfModelExplanation()
        {
            var handoff = new ReviewHandoffDefinition
            {
                Status = ReviewHandoffStatuses.ConfigurationGap,
                Summary = "配置尚未完成",
                VerifiedFacts = new System.Collections.Generic.List<ReviewVerifiedFactDefinition>
                {
                    Fact("proc-1", "扫码流程", "proc.procIndex", "0"),
                    Fact("proc-1", "扫码流程", "proc.isValid", "true"),
                    Fact("proc-1", "扫码流程", "proc.runnable", "false"),
                    Fact("proc-1", "扫码流程", "proc.placeholderWarningCount", "12"),
                    Fact("proc-1", "扫码流程", "proc.runBlockerCount", "14"),
                    Fact("proc-1", "扫码流程", "proc.nonPlaceholderBlockerCount", "2")
                }
            };

            string result = AiConversationCoordinator.BuildTrustedReviewOutput(
                "模型解释误写为13处占位。",
                handoff);

            StringAssert.StartsWith(result, "【机械验证事实（以此为准）】");
            StringAssert.Contains(result, "占位警告=12");
            StringAssert.Contains(result, "运行阻塞=14");
            Assert.IsTrue(result.IndexOf("占位警告=12", System.StringComparison.Ordinal)
                < result.IndexOf("13处占位", System.StringComparison.Ordinal));
        }

        [TestMethod]
        public void ContextRollover_UsesBusinessBoundaryPressureSignals()
        {
            Assert.IsFalse(AiConversationCoordinator.ShouldRequestTrustedContextRollover(
                50000, 1, 128000, 8192));
            Assert.IsTrue(AiConversationCoordinator.ShouldRequestTrustedContextRollover(
                63000, 1, 128000, 8192));
            Assert.IsTrue(AiConversationCoordinator.ShouldRequestTrustedContextRollover(
                50000, 40 * 1024, 128000, 8192));
            Assert.IsFalse(AiConversationCoordinator.ShouldRequestTrustedContextRollover(
                1, 512 * 1024, 128000, 8192));
        }

        [TestMethod]
        public void RepeatedMaxTokensWithoutToolProgress_StopsInsteadOfThirdReasoningTurn()
        {
            Assert.IsFalse(AiConversationCoordinator.ShouldStopStageContinuation(
                "max_tokens", 1, 0));
            Assert.IsFalse(AiConversationCoordinator.ShouldStopStageContinuation(
                "max_tokens", 2, 1));
            Assert.IsTrue(AiConversationCoordinator.ShouldStopStageContinuation(
                "max_tokens", 2, 0));
            Assert.IsFalse(AiConversationCoordinator.ShouldStopStageContinuation(
                "end_turn", 3, 0));

            string prompt = AiConversationCoordinator.BuildStageContinuationPrompt(
                AutomationToolProfiles.ProcessCreate,
                "max_tokens");
            StringAssert.Contains(prompt, "停止展开分析");
            StringAssert.Contains(prompt, "先用autoStart=false和config.placeholder预演安全骨架");
            StringAssert.Contains(prompt, "申请ProcessEdit分段补齐");
        }

        [TestMethod]
        public void ProcessReview_DeterminedConclusionRequiresHostFact()
        {
            string blocked = AiTaskCapabilityPolicy.ValidateReviewHandoff(
                new ReviewHandoffDefinition
                {
                    Status = ReviewHandoffStatuses.ConfigurationGap,
                    Summary = "配置缺口"
                },
                AutomationToolProfiles.ProcessReview);
            string unresolved = AiTaskCapabilityPolicy.ValidateReviewHandoff(
                new ReviewHandoffDefinition
                {
                    Status = ReviewHandoffStatuses.Unresolved,
                    Summary = "目标不存在，尚无机械事实"
                },
                AutomationToolProfiles.ProcessReview);

            StringAssert.Contains(blocked, "宿主机械事实");
            Assert.IsNull(unresolved);
            StringAssert.StartsWith(
                AiConversationCoordinator.BuildTrustedReviewOutput(
                    "【机械验证事实】模型自称已验证。",
                    new ReviewHandoffDefinition { Status = ReviewHandoffStatuses.Unresolved }),
                "【证据状态】");
        }

        [TestMethod]
        public void Trajectory_FlagsContextAndModelLatency_AndMarksRecoveredToolError()
        {
            AiTrajectoryEvaluation slowReview = AiTrajectoryBudgetPolicy.Evaluate(
                AutomationToolProfiles.ProcessReview,
                10,
                0,
                46822,
                61156,
                128000,
                79675,
                new[] { "get_proc_overview", "get_flow_graph", "validate_proc" });
            AiTrajectoryEvaluation recovered = AiTrajectoryBudgetPolicy.Evaluate(
                AutomationToolProfiles.ProcessCreate,
                8,
                1,
                41023,
                12000,
                128000,
                1000,
                new[] { "get_operation_guide" });

            Assert.AreEqual("review", slowReview.Status);
            Assert.IsTrue(slowReview.Reasons.Any(reason => reason.StartsWith("input_context_pressure", System.StringComparison.Ordinal)));
            Assert.IsTrue(slowReview.Reasons.Any(reason => reason.StartsWith("unattributed_ms", System.StringComparison.Ordinal)));
            Assert.AreEqual("recovered", recovered.Status);
            CollectionAssert.Contains(recovered.Reasons.ToArray(), "tool_failures_recovered=1");
        }

        [TestMethod]
        public void FinishWithExecutionFields_IsRejectedInsteadOfSilentlyIgnoringThem()
        {
            AiTaskDecisionValidation result = AiTaskCapabilityPolicy.Validate(
                new TaskCapabilityDecisionDefinition
                {
                    Version = 1,
                    Action = "finish",
                    Capability = AutomationToolProfiles.ProcessReview,
                    Objective = "顺便检查流程",
                    Message = "任务完成",
                    AuthorizationQuote = string.Empty,
                    RequiresUserConfirmationAfter = false
                },
                "请回答问题",
                AutomationToolProfiles.Editor,
                false,
                new AiDynamicTaskState());

            Assert.AreEqual(AiTaskDecisionKind.Invalid, result.Kind);
            StringAssert.Contains(result.Message, "不能同时携带");
        }

        [TestMethod]
        public void CurrentStateEvidence_ExcludesGuidesAndSchemas()
        {
            Assert.IsFalse(GooseAcpClient.IsCurrentStateEvidenceTool("get_process_design_guide"));
            Assert.IsFalse(GooseAcpClient.IsCurrentStateEvidenceTool("get_operation_schema"));
            Assert.IsFalse(GooseAcpClient.IsCurrentStateEvidenceTool("apply_change_set"));
            Assert.IsTrue(GooseAcpClient.IsCurrentStateEvidenceTool("get_proc_detail"));
            Assert.IsFalse(GooseAcpClient.IsMutationAttemptTool("get_proc_detail"));
            Assert.IsTrue(GooseAcpClient.IsMutationAttemptTool("apply_change_set"));
        }

        private static AiTaskDecisionValidation Validate(
            string action,
            string capability,
            string permission = AutomationToolProfiles.Editor,
            bool fullPermission = false,
            AiDynamicTaskState state = null,
            string message = "")
        {
            return AiTaskCapabilityPolicy.Validate(
                new TaskCapabilityDecisionDefinition
                {
                    Version = 1,
                    Action = action,
                    Capability = capability,
                    Objective = string.Equals(action, "run_stage", System.StringComparison.Ordinal)
                        ? "检查当前流程事实"
                        : string.Empty,
                    Message = message,
                    AuthorizationQuote = AiTaskCapabilityPolicy.RequiresExplicitAuthorizationQuote(capability)
                        ? "执行"
                        : string.Empty,
                    Basis = string.Equals(capability, AutomationToolProfiles.ProcessEdit, System.StringComparison.Ordinal)
                        ? TaskDecisionBases.DirectUserChange
                        : string.Empty,
                    RequiresUserConfirmationAfter = false
                },
                "请执行这个任务",
                permission,
                fullPermission,
                state ?? new AiDynamicTaskState());
        }

        private static ReviewVerifiedFactDefinition Fact(
            string subjectId,
            string subjectName,
            string key,
            string value)
        {
            return new ReviewVerifiedFactDefinition
            {
                SubjectId = subjectId,
                SubjectName = subjectName,
                Key = key,
                Value = value,
                SourceTool = "validate_proc",
                ToolCallId = "call-1",
                EvidencePath = "/data/value",
                EvidenceSha256 = new string('a', 64)
            };
        }
    }
}
