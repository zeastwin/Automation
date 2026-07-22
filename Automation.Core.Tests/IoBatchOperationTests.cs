using System;
// 模块：核心测试 / IO 批量指令。
// 职责范围：验证语义编译、运行时批量输出和跨卡失败前不产生部分写入。

using System.Collections.Generic;
using System.ComponentModel;
using Automation.MotionControl;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class IoBatchOperationTests
    {
        [TestMethod]
        public void RunIoOperate_UsesOneBatchAndRejectsCrossCardBeforeWriting()
        {
            var probe = new BatchIoProbe();
            IO io0 = CreateOutput("Out0", 0, 0);
            IO io1 = CreateOutput("Out1", 0, 1);
            var context = new EngineContext
            {
                Io = probe,
                IoMap = new Dictionary<string, IO>(StringComparer.Ordinal)
                {
                    [io0.Name] = io0,
                    [io1.Name] = io1
                }
            };
            var operation = new IoOperate
            {
                IoParams = new OperationTypePartial.CustomList<IoOutParam>
                {
                    new IoOutParam { IoName = io0.Name, TargetState = true },
                    new IoOutParam { IoName = io1.Name, TargetState = false }
                }
            };

            using (var engine = new ProcessEngine(context))
            {
                Assert.IsTrue(engine.RunIoOperate(new ProcHandle(), operation));
                Assert.AreEqual(1, probe.BatchCalls);
                Assert.AreEqual(0, probe.SingleCalls);
                Assert.AreEqual(2, probe.LastCommands.Count);

                io1.CardNum = 1;
                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    engine.RunIoOperate(new ProcHandle(), operation));
                Assert.AreEqual(1, probe.BatchCalls);
            }
        }

        [TestMethod]
        public void SemanticIoOperations_EnforceInputOutputAndSameCardContracts()
        {
            IO io0 = CreateOutput("Out0", 0, 0);
            IO io1 = CreateOutput("Out1", 0, 1);
            var resources = new Dictionary<string, AiIoResource>(StringComparer.Ordinal)
            {
                [io0.Name] = new AiIoResource
                {
                    IoType = io0.IOType,
                    CardNum = 0,
                    IoIndex = "0"
                },
                [io1.Name] = new AiIoResource
                {
                    IoType = io1.IOType,
                    CardNum = 0,
                    IoIndex = "1"
                },
                ["In0"] = new AiIoResource
                {
                    IoType = "通用输入",
                    CardNum = 0,
                    IoIndex = "2"
                }
            };
            var compileContext = new AiOperationCompileContext(
                0,
                new Dictionary<string, DicValue>(StringComparer.Ordinal),
                new AiResourceSnapshot(resources));
            var write = new SemanticOperation
            {
                Kind = "io.write",
                Outputs = new List<IoOutputState>
                {
                    new IoOutputState { Io = io0.Name, State = true },
                    new IoOutputState { Io = io1.Name, State = false }
                }
            };

            var compiled = (IoOperate)AiOperationCompilerRegistry
                .Get("io.write").Compile(write, compileContext);
            Assert.AreEqual(2, compiled.IoParams.Count);

            var outputBranch = new SemanticOperation
            {
                Kind = "branch.io",
                Conditions = new List<IoStateCondition>
                {
                    new IoStateCondition { Io = io0.Name, State = true }
                }
            };
            InvalidOperationException branchError = Assert.ThrowsExactly<InvalidOperationException>(
                () => AiOperationCompilerRegistry.Get("branch.io")
                    .Compile(outputBranch, compileContext));
            StringAssert.Contains(branchError.Message, "只能引用通用输入");

            var inputWait = new SemanticOperation
            {
                Kind = "io.wait",
                TimeoutMs = 1000,
                Conditions = new List<IoStateCondition>
                {
                    new IoStateCondition { Io = "In0", State = true }
                }
            };
            Assert.IsInstanceOfType(
                AiOperationCompilerRegistry.Get("io.wait").Compile(inputWait, compileContext),
                typeof(IoCheck));

            resources[io1.Name].CardNum = 1;
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                AiOperationCompilerRegistry.Get("io.write").Compile(write, compileContext));
        }

        [TestMethod]
        public void IoLogicName_UsesInputIoConverter()
        {
            PropertyDescriptor property = TypeDescriptor
                .GetProperties(typeof(IoLogicGotoParam))[nameof(IoLogicGotoParam.IoName)];

            Assert.AreEqual(typeof(OperationTypePartial.IoInItem), property.Converter.GetType());
        }

        private static IO CreateOutput(string name, int cardNumber, int ioIndex)
        {
            return new IO
            {
                Name = name,
                CardNum = cardNumber,
                IOIndex = ioIndex.ToString(),
                IOType = "通用输出"
            };
        }

        private sealed class BatchIoProbe : IIoRuntime
        {
            public int SingleCalls { get; private set; }
            public int BatchCalls { get; private set; }
            public IReadOnlyList<IoOutputCommand> LastCommands { get; private set; }

            public bool SetIO(IO io, bool isOpen)
            {
                SingleCalls++;
                return true;
            }

            public bool SetOutputs(IReadOnlyList<IoOutputCommand> commands)
            {
                BatchCalls++;
                LastCommands = commands;
                return true;
            }

            public bool GetOutIO(IO io, ref bool value) => true;

            public bool GetInIO(IO io, ref bool value) => true;
        }
    }
}
