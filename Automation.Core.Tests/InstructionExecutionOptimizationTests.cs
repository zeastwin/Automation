using System;
// 模块：核心测试 / 指令执行优化。
// 职责范围：固化运行绑定、变量原子修改、字符串执行和自定义函数缓存的行为等价性。

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class InstructionExecutionOptimizationTests
    {
        [TestMethod]
        public void ValueOperations_KeepCopyAndAtomicAliasSemantics()
        {
            var runtime = new PlatformRuntime();
            ValueConfigStore values = runtime.Stores.Values;
            Assert.IsTrue(values.TrySetValue(0, "源数值", "double", "1", string.Empty));
            Assert.IsTrue(values.TrySetValue(1, "复制结果", "double", "0", string.Empty));

            using (var engine = new ProcessEngine(new EngineContext
            {
                ValueStore = values
            }))
            {
                var getValue = new GetValue
                {
                    Params = new OperationTypePartial.CustomList<GetValueParam>
                    {
                        new GetValueParam
                        {
                            ValueSourceIndex = "0",
                            ValueSaveIndex = "1"
                        }
                    }
                };
                Assert.IsTrue(engine.RunGetValue(new ProcHandle(), getValue));
                Assert.AreEqual(1d, values.get_D_ValueByIndex(1), 0.000001);

                var modify = new ModifyValue
                {
                    ModifyType = "叠加",
                    ValueSourceIndex = "0",
                    ChangeValue = "1",
                    OutputValueIndex = "0"
                };
                for (int i = 0; i < 1000; i++)
                {
                    Assert.IsTrue(engine.RunModifyValue(new ProcHandle(), modify));
                }
                Assert.AreEqual(1001d, values.get_D_ValueByIndex(0), 0.000001);
            }
        }

        [TestMethod]
        public void StringOperations_KeepFormatSplitAndReplaceResults()
        {
            var runtime = new PlatformRuntime();
            ValueConfigStore values = runtime.Stores.Values;
            Assert.IsTrue(values.TrySetValue(0, "文本源", "string", "a,b", string.Empty));
            Assert.IsTrue(values.TrySetValue(1, "格式结果", "string", string.Empty, string.Empty));
            Assert.IsTrue(values.TrySetValue(2, "替换结果", "string", string.Empty, string.Empty));
            Assert.IsTrue(values.TrySetValue(3, "分割一", "string", string.Empty, string.Empty));
            Assert.IsTrue(values.TrySetValue(4, "分割二", "string", string.Empty, string.Empty));

            using (var engine = new ProcessEngine(new EngineContext
            {
                ValueStore = values
            }))
            {
                var format = new StringFormat
                {
                    Format = "值:{0}",
                    OutputValueIndex = "1",
                    Params = new OperationTypePartial.CustomList<StringFormatParam>
                    {
                        new StringFormatParam { ValueSourceIndex = "0" }
                    }
                };
                Assert.IsTrue(engine.RunStringFormat(new ProcHandle(), format));
                Assert.AreEqual("值:a,b", values.GetValueByIndex(1).Value);

                var replace = new Replace
                {
                    ReplaceType = "替换指定字符",
                    SourceValueIndex = "0",
                    ReplaceStr = ",",
                    NewStr = "|",
                    OutputIndex = "2"
                };
                Assert.IsTrue(engine.RunReplace(new ProcHandle(), replace));
                Assert.AreEqual("a|b", values.GetValueByIndex(2).Value);

                var split = new Split
                {
                    SourceValueIndex = "0",
                    OutputIndex = "3",
                    SplitMark = ',',
                    StartIndex = 0,
                    Count = 2
                };
                Assert.IsTrue(engine.RunSplit(new ProcHandle(), split));
                Assert.AreEqual("a", values.GetValueByIndex(3).Value);
                Assert.AreEqual("b", values.GetValueByIndex(4).Value);
            }
        }

        [TestMethod]
        public void CustomFunction_CachedDelegateStillInvokesRegisteredBody()
        {
            int invocationCount = 0;
            var functions = new CustomFunc();
            functions.RegisterFunction("计数", () => invocationCount++);
            var operation = new CallCustomFunc { FunctionName = "计数" };

            using (var engine = new ProcessEngine(new EngineContext
            {
                CustomFunc = functions
            }))
            {
                Assert.IsTrue(engine.RunCustomFunc(new ProcHandle(), operation));
                Assert.IsTrue(engine.RunCustomFunc(new ProcHandle(), operation));
            }

            Assert.AreEqual(2, invocationCount);
            Assert.IsInstanceOfType(
                operation.RuntimeBinding, typeof(CustomFunc.FunctionDelegate));
        }

        [TestMethod]
        public void ParamGoto_PrecompiledConditionKeepsNumericBranchSemantics()
        {
            var runtime = new PlatformRuntime();
            Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                0, "判断值", "double", "5", string.Empty));
            var operation = new ParamGoto
            {
                TrueGoto = "0-0-1",
                FalseGoto = "0-0-2",
                Params = new OperationTypePartial.CustomList<ParamGotoParam>
                {
                    new ParamGotoParam
                    {
                        ValueIndex = "0",
                        JudgeMode = "值在区间内",
                        Down = 1,
                        Up = 10,
                        IncludeBoundary = true
                    }
                }
            };
            Proc process = CreateProcess(operation, new EndProcess(), new EndProcess());
            var handle = new ProcHandle
            {
                Proc = process,
                procId = process.head.Id,
                procNum = 0,
                stepNum = 0,
                opsNum = 0
            };

            using (var engine = new ProcessEngine(new EngineContext
            {
                Procs = new[] { process },
                ValueStore = runtime.Stores.Values
            }))
            {
                Assert.IsTrue(engine.RunParamGoto(handle, operation));
            }

            Assert.IsTrue(handle.isGoto);
            Assert.AreEqual(0, handle.stepNum);
            Assert.AreEqual(1, handle.opsNum);
        }

        [TestMethod]
        public void SingleDataStructWrite_WhenValueInvalid_DoesNotChangeField()
        {
            var runtime = new PlatformRuntime();
            DataStructStore store = runtime.Stores.DataStructures;
            Assert.IsTrue(store.AddStruct("结构", out string error), error);
            Assert.IsTrue(store.CreateItem(
                0, "项", 0, out int itemIndex, out error), error);
            Assert.IsTrue(store.AddField(
                0, itemIndex, "字段", DataStructValueType.Number,
                "5", 0, out _, out error), error);

            using (var engine = new ProcessEngine(new EngineContext
            {
                DataStructStore = store,
                ValueStore = runtime.Stores.Values
            }))
            {
                var operation = new SetDataStructItem
                {
                    StructIndex = 0,
                    ItemIndex = 0,
                    Params = new OperationTypePartial.CustomList<SetDataStructItemParam>
                    {
                        new SetDataStructItemParam
                        {
                            FieldIndex = 0,
                            Value = "不是数值"
                        }
                    }
                };
                Assert.ThrowsExactly<InvalidOperationException>(
                    () => engine.RunSetDataStructItem(new ProcHandle(), operation));
            }

            Assert.IsTrue(store.TryGetItemValueByIndex(0, 0, 0, out object value));
            Assert.AreEqual(5d, (double)value, 0.000001);
        }

        [TestMethod]
        public void NumericModify_RuntimeTypedPathKeepsConcurrentAliasAtomicity()
        {
            const int workerCount = 8;
            const int iterations = 2000;
            var runtime = new PlatformRuntime();
            ValueConfigStore values = runtime.Stores.Values;
            Assert.IsTrue(values.TrySetValue(
                0, "并发计数", "double", "0", string.Empty));
            var operation = new ModifyValue
            {
                ModifyType = "叠加",
                ValueSourceIndex = "0",
                ChangeValue = "1",
                OutputValueIndex = "0"
            };

            using (var engine = new ProcessEngine(new EngineContext
            {
                ValueStore = values,
                DataStructStore = runtime.Stores.DataStructures
            }))
            {
                Assert.IsTrue(engine.RunModifyValue(
                    new ProcHandle(), operation));
                Assert.IsTrue(values.SetValueByIndexForProcess(
                    0, 0d, Guid.Empty));
                Parallel.For(0, workerCount, _ =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        Assert.IsTrue(engine.RunModifyValue(
                            new ProcHandle(), operation));
                    }
                });
            }

            Assert.AreEqual(
                workerCount * iterations,
                values.get_D_ValueByIndex(0),
                0.000001);
        }

        [TestMethod]
        public void DataStructRuntimeBinding_AfterFieldRenameDoesNotWriteWrongTarget()
        {
            var runtime = new PlatformRuntime();
            DataStructStore store = runtime.Stores.DataStructures;
            Assert.IsTrue(store.AddStruct("结构", out string error), error);
            Assert.IsTrue(store.CreateItem(
                0, "项", 0, out int itemIndex, out error), error);
            Assert.IsTrue(store.AddField(
                0, itemIndex, "原字段", DataStructValueType.Number,
                "1", 0, out _, out error), error);
            Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                0, "写入源", "double", "2", string.Empty));
            var operation = new SetDataStructItem
            {
                StructName = "结构",
                ItemName = "项",
                Params = new OperationTypePartial.CustomList<SetDataStructItemParam>
                {
                    new SetDataStructItemParam
                    {
                        FieldName = "原字段",
                        ValueName = "写入源"
                    }
                }
            };

            using (var engine = new ProcessEngine(new EngineContext
            {
                ValueStore = runtime.Stores.Values,
                DataStructStore = store
            }))
            {
                Assert.IsTrue(engine.RunSetDataStructItem(
                    new ProcHandle(), operation));
                Assert.IsTrue(store.RenameField(
                    0, 0, 0, "新字段", out error), error);
                Assert.IsTrue(runtime.Stores.Values.SetValueByIndexForProcess(
                    0, 3d, Guid.Empty));

                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    engine.RunSetDataStructItem(
                        new ProcHandle(), operation));
            }

            Assert.IsTrue(store.TryGetItemValueByIndex(
                0, 0, 0, out object value));
            Assert.AreEqual(2d, (double)value, 0.000001);
        }

        private static Proc CreateProcess(params OperationType[] operations)
        {
            var process = new Proc
            {
                head = new ProcHead
                {
                    Id = Guid.NewGuid(),
                    Name = "指令优化回归流程"
                }
            };
            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = "测试步骤"
            };
            foreach (OperationType operation in operations)
            {
                operation.Id = Guid.NewGuid();
                step.Ops.Add(operation);
            }
            process.steps.Add(step);
            return process;
        }
    }
}
