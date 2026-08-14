using System;
using System.Collections.Generic;
using System.Linq;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AiChangeSetCompilerSafetyTests
    {
        [TestMethod]
        public void PlaceholderOperation_AllowsPlaceholderUpdateButRequiresReplaceForRealAction()
        {
            Proc process = TestProcessFactory.CreateEndingProcess("占位保护");
            var placeholder = new ConfigurationPlaceholder
            {
                Id = Guid.NewGuid(),
                Name = "待确定扫码动作",
                Reason = "扫码设备和协议待确认",
                Note = ProcessReadinessService.PlaceholderNotePrefix + "扫码设备和协议待确认"
            };
            process.steps[0].Ops.Insert(0, placeholder);
            string procId = process.head.Id.ToString("D");
            string opId = placeholder.Id.ToString("D");
            var placeholderUpdate = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.update",
                        TargetProcess = new ProcessSelector { ProcId = procId },
                        TargetOperation = new OperationSelector { OpId = opId },
                        Operation = new SemanticOperation
                        {
                            Kind = "config.placeholder",
                            Name = "待确定扫码与判定",
                            Message = "扫码设备、协议和结果判定待确认"
                        }
                    }
                }
            };
            var runtime = new PlatformRuntime();
            AiChangeSetCompileResult updated = AiChangeSetCompiler.Compile(
                runtime,
                placeholderUpdate,
                new[] { process },
                new Dictionary<string, DicValue>(StringComparer.Ordinal));
            var updatedPlaceholder = updated.Processes[0].steps[0].Ops[0] as ConfigurationPlaceholder;
            Assert.IsNotNull(updatedPlaceholder);
            Assert.AreEqual(placeholder.Id, updatedPlaceholder.Id);
            Assert.AreEqual("待确定扫码与判定", updatedPlaceholder.Name);
            StringAssert.Contains(updatedPlaceholder.Reason, "结果判定待确认");

            var updateToRealAction = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.update",
                        TargetProcess = new ProcessSelector { ProcId = procId },
                        TargetOperation = new OperationSelector { OpId = opId },
                        Operation = new SemanticOperation
                        {
                            Kind = "popup.message",
                            Message = "扫码完成"
                        }
                    }
                }
            };
            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                AiChangeSetCompiler.Compile(
                    runtime,
                    updateToRealAction,
                    new[] { process },
                    new Dictionary<string, DicValue>(StringComparer.Ordinal)));
            StringAssert.Contains(error.Message, "占位指令");
            StringAssert.Contains(error.Message, "operation.replace");

            updateToRealAction.Actions[0].Type = "operation.replace";
            updateToRealAction.Actions[0].Operation = new SemanticOperation { Kind = "flow.end" };
            AiChangeSetCompileResult replaced = AiChangeSetCompiler.Compile(
                runtime,
                updateToRealAction,
                new[] { process },
                new Dictionary<string, DicValue>(StringComparer.Ordinal));

            Assert.IsInstanceOfType(replaced.Processes[0].steps[0].Ops[0], typeof(EndProcess));
            Assert.AreEqual(placeholder.Id, replaced.Processes[0].steps[0].Ops[0].Id);
        }

        [TestMethod]
        public void DisabledPlaceholder_CannotMakeDraftAppearRunnable()
        {
            Proc process = TestProcessFactory.CreateEndingProcess("禁用占位保护");
            process.steps[0].Ops.Insert(0, new ConfigurationPlaceholder
            {
                Id = Guid.NewGuid(),
                Reason = "设备待配置",
                Disable = true
            });

            ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                0, process, new List<Proc> { process });

            Assert.AreEqual("incomplete", readiness.ReadinessStatus);
            Assert.IsFalse(readiness.Runnable);
            Assert.IsTrue(readiness.RunBlockers.Any(item => item.Contains("配置占位")));
        }

        [TestMethod]
        public void PlaceholderOperation_PreservesPlannedBranchesWithoutPretendingToRun()
        {
            var changeSet = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "process.create",
                        Process = new ProcessActionValue { Key = "process", Name = "占位骨架" }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        Step = new StepActionValue { Key = "work", Name = "执行" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "work" },
                        Operation = new SemanticOperation
                        {
                            Key = "scan",
                            Kind = "config.placeholder",
                            Message = "扫码设备和协议待确认",
                            WhenTrue = new OperationTarget { OperationKey = "success_end" },
                            WhenFalse = new OperationTarget { OperationKey = "alarm" }
                        }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        Step = new StepActionValue { Key = "success", Name = "成功" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "success" },
                        Operation = new SemanticOperation { Key = "success_end", Kind = "flow.end" }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        Step = new StepActionValue { Key = "failure", Name = "失败" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "failure" },
                        Operation = new SemanticOperation { Key = "alarm", Kind = "alarm.raise", Message = "扫码失败" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "failure" },
                        Operation = new SemanticOperation { Key = "failure_end", Kind = "flow.end" }
                    }
                }
            };

            AiChangeSetCompileResult result = AiChangeSetCompiler.Compile(
                new PlatformRuntime(), changeSet, Array.Empty<Proc>(),
                new Dictionary<string, DicValue>(StringComparer.Ordinal));
            OperationType placeholder = result.Processes[0].steps[0].Ops[0];
            ProcessFlowGraphSnapshot graph = ProcessFlowGraphService.BuildProcess(result.Processes, 0);
            ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                0, result.Processes[0], result.Processes);

            Assert.IsInstanceOfType(placeholder, typeof(ConfigurationPlaceholder));
            Assert.IsFalse(placeholder is PopupDialog);
            Assert.AreEqual(2, graph.Edges.Count(edge => edge.Planned));
            Assert.IsFalse(graph.Diagnostics.Any(item => item.Code == "UNREACHABLE_OPERATION"));
            Assert.IsFalse(readiness.Runnable);
            Assert.IsTrue(readiness.RunBlockers.Any(item => item.Contains("配置占位")));
        }

        [TestMethod]
        public void DuplicateCrossStepOperationKey_RequiresStepSelector()
        {
            var changeSet = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "process.create",
                        Process = new ProcessActionValue { Key = "process", Name = "歧义跳转" }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        Step = new StepActionValue { Key = "source", Name = "来源" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "source" },
                        Operation = new SemanticOperation
                        {
                            Key = "jump", Kind = "flow.goto",
                            Target = new OperationTarget { OperationKey = "end" }
                        }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        Step = new StepActionValue { Key = "first", Name = "出口一" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "first" },
                        Operation = new SemanticOperation { Key = "end", Kind = "flow.end" }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        Step = new StepActionValue { Key = "second", Name = "出口二" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "second" },
                        Operation = new SemanticOperation { Key = "end", Kind = "flow.end" }
                    }
                }
            };

            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                AiChangeSetCompiler.Compile(
                    new PlatformRuntime(), changeSet, Array.Empty<Proc>(),
                    new Dictionary<string, DicValue>(StringComparer.Ordinal)));

            StringAssert.Contains(error.Message, "多个步骤中同名");
            StringAssert.Contains(error.Message, "stepId 或 stepKey");
        }

        [TestMethod]
        public void CreatedProcess_UnreachableOperationCanBeSavedButBlocksRun()
        {
            var changeSet = new AiChangeSet
            {
                Version = 2,
                Variables = new List<VariableChange>
                {
                    new VariableChange
                    {
                        Name = "判断标记",
                        Scope = VariableScopeContract.Process,
                        OwnerProcess = new ProcessSelector { Key = "process" },
                        Type = VariableChangeContract.DoubleType,
                        Value = "0",
                        Policy = VariableChangeContract.CreatePolicy
                    }
                },
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "process.create",
                        Process = new ProcessActionValue { Key = "process", Name = "不可达检查" }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        Step = new StepActionValue { Key = "source", Name = "判断" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "source" },
                        Operation = new SemanticOperation
                        {
                            Key = "branch",
                            Kind = "branch.number_compare",
                            Variable = "判断标记",
                            Comparison = "eq",
                            CompareValue = 1,
                            WhenTrue = new OperationTarget { StepKey = "target", OperationKey = "end" },
                            WhenFalse = new OperationTarget { StepKey = "target", OperationKey = "end" }
                        }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        Step = new StepActionValue { Key = "target", Name = "失败出口" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "target" },
                        Operation = new SemanticOperation
                        {
                            Key = "alarm",
                            Kind = "config.placeholder",
                            Message = "报警资源待确认"
                        }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        TargetStep = new StepSelector { Key = "target" },
                        Operation = new SemanticOperation { Key = "end", Kind = "flow.end" }
                    }
                }
            };

            AiChangeSetCompileResult result = AiChangeSetCompiler.Compile(
                new PlatformRuntime(),
                changeSet,
                Array.Empty<Proc>(),
                new Dictionary<string, DicValue>(StringComparer.Ordinal));

            Assert.AreEqual("incomplete", result.ReadinessStatus);
            Assert.IsFalse(result.Runnable);
            Assert.IsTrue(result.ConfigurationWarnings.OfType<Newtonsoft.Json.Linq.JObject>()
                .Any(item => (item["message"]?.ToString() ?? string.Empty).Contains("不可达")));
            Assert.IsTrue(result.RunBlockers.OfType<Newtonsoft.Json.Linq.JObject>()
                .Any(item => (item["message"]?.ToString() ?? string.Empty).Contains("不可达")));
        }

        [TestMethod]
        public void CreatedProcess_ExplicitRetryPath_IsReachableWithoutDelay()
        {
            var changeSet = new AiChangeSet
            {
                Version = 2,
                Variables = new List<VariableChange>
                {
                    ProcessVariable("扫码成功", VariableChangeContract.DoubleType),
                    ProcessVariable("扫码结果", VariableChangeContract.StringType),
                    ProcessVariable("扫码尝试次数", VariableChangeContract.DoubleType)
                },
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "process.create",
                        Process = new ProcessActionValue { Key = "process", Name = "显式重试闭环" }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "process" },
                        Step = new StepActionValue { Key = "retry", Name = "扫码重试" }
                    },
                    Append("counter_reset", new SemanticOperation
                    {
                        Kind = "variable.set", Variable = "扫码尝试次数", Value = "0"
                    }),
                    Append("success_reset", new SemanticOperation
                    {
                        Kind = "variable.set", Variable = "扫码成功", Value = "0"
                    }),
                    Append("result_clear", new SemanticOperation
                    {
                        Kind = "variable.clear", Variable = "扫码结果"
                    }),
                    Append("attempt", new SemanticOperation
                    {
                        Kind = "config.placeholder", Message = "扫码动作待配置"
                    }),
                    Append("outcome", new SemanticOperation
                    {
                        Kind = "branch.number_compare",
                        Variable = "扫码成功",
                        Comparison = "eq",
                        CompareValue = 1,
                        WhenTrue = new OperationTarget { OperationKey = "success_end" },
                        WhenFalse = new OperationTarget { OperationKey = "increment" }
                    }),
                    Append("increment", new SemanticOperation
                    {
                        Kind = "variable.add", Variable = "扫码尝试次数", Amount = 1
                    }),
                    Append("retry_decision", new SemanticOperation
                    {
                        Kind = "branch.number_compare",
                        Variable = "扫码尝试次数",
                        Comparison = "lt",
                        CompareValue = 4,
                        WhenTrue = new OperationTarget { OperationKey = "success_reset" },
                        WhenFalse = new OperationTarget { OperationKey = "failed" }
                    }),
                    Append("success_end", new SemanticOperation { Kind = "flow.end" }),
                    Append("failed", new SemanticOperation
                    {
                        Kind = "alarm.raise",
                        Message = "扫码失败",
                        Target = new OperationTarget { OperationKey = "failed_end" }
                    }),
                    Append("failed_end", new SemanticOperation { Kind = "flow.end" })
                }
            };

            AiChangeSetCompileResult result = AiChangeSetCompiler.Compile(
                new PlatformRuntime(),
                changeSet,
                Array.Empty<Proc>(),
                new Dictionary<string, DicValue>(StringComparer.Ordinal));
            ProcessFlowGraphSnapshot graph = ProcessFlowGraphService.BuildProcess(result.Processes, 0);

            Assert.IsFalse(graph.Diagnostics.Any(item =>
                string.Equals(item.Code, "UNREACHABLE_OPERATION", StringComparison.Ordinal)));
            Assert.IsFalse(result.Processes[0].steps.SelectMany(step => step.Ops)
                .Any(operation => operation is Delay),
                "显式重试闭环不能依赖伪造固定延时连接节点。");
        }

        private static VariableChange ProcessVariable(string name, string type) => new VariableChange
        {
            Name = name,
            Scope = VariableScopeContract.Process,
            OwnerProcess = new ProcessSelector { Key = "process" },
            Type = type,
            Value = string.Equals(type, VariableChangeContract.DoubleType, StringComparison.Ordinal)
                ? "0" : string.Empty,
            Policy = VariableChangeContract.CreatePolicy
        };

        private static ChangeSetAction Append(string key, SemanticOperation operation)
        {
            operation.Key = key;
            return new ChangeSetAction
            {
                Type = "operation.append",
                TargetProcess = new ProcessSelector { Key = "process" },
                TargetStep = new StepSelector { Key = "retry" },
                Operation = operation
            };
        }
    }
}
