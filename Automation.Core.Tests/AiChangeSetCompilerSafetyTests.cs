using System;
using System.Collections.Generic;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AiChangeSetCompilerSafetyTests
    {
        [TestMethod]
        public void PlaceholderOperation_RejectsUpdateAndRequiresReplace()
        {
            Proc process = TestProcessFactory.CreateEndingProcess("占位保护");
            var placeholder = new PopupDialog
            {
                Id = Guid.NewGuid(),
                Name = "待确定扫码动作",
                Note = ProcessReadinessService.PlaceholderNotePrefix + "扫码设备和协议待确认",
                PopupMessage = "待确认"
            };
            process.steps[0].Ops.Insert(0, placeholder);
            string procId = process.head.Id.ToString("D");
            string opId = placeholder.Id.ToString("D");
            var update = new AiChangeSet
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
            var runtime = new PlatformRuntime();
            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                AiChangeSetCompiler.Compile(
                    runtime,
                    update,
                    new[] { process },
                    new Dictionary<string, DicValue>(StringComparer.Ordinal)));
            StringAssert.Contains(error.Message, "占位指令");
            StringAssert.Contains(error.Message, "operation.replace");

            update.Actions[0].Type = "operation.replace";
            update.Actions[0].Operation = new SemanticOperation { Kind = "flow.end" };
            AiChangeSetCompileResult replaced = AiChangeSetCompiler.Compile(
                runtime,
                update,
                new[] { process },
                new Dictionary<string, DicValue>(StringComparer.Ordinal));

            Assert.IsInstanceOfType(replaced.Processes[0].steps[0].Ops[0], typeof(EndProcess));
            Assert.AreEqual(placeholder.Id, replaced.Processes[0].steps[0].Ops[0].Id);
        }

        [TestMethod]
        public void CreatedProcess_RejectsReachableBranchThatSkipsActiveOperation()
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

            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                AiChangeSetCompiler.Compile(
                    new PlatformRuntime(),
                    changeSet,
                    Array.Empty<Proc>(),
                    new Dictionary<string, DicValue>(StringComparer.Ordinal)));

            StringAssert.Contains(error.Message, "不可达");
            StringAssert.Contains(error.Message, "报警");
        }
    }
}
