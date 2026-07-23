using System;
// 模块：核心测试 / 数据结构指令。
// 职责范围：固化索引与名称双寻址、稀疏字段、批量写入原子性和查找报警契约。

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class DataStructOperationTests
    {
        [TestMethod]
        public void SetAndGet_SupportExplicitNameAddressingAndSparseFields()
        {
            PlatformRuntime runtime = CreateRuntimeWithSparseStruct();
            DataStructStore store = runtime.Stores.DataStructures;
            ValueConfigStore values = runtime.Stores.Values;
            Assert.IsTrue(values.TrySetValue(10, "结果0", "double", "0", string.Empty));
            Assert.IsTrue(values.TrySetValue(11, "结果1", "double", "0", string.Empty));

            using (var engine = CreateEngine(store, values))
            {
                var setByName = new SetDataStructItem
                {
                    StructName = "测试结构",
                    ItemName = "测试项",
                    Params = new OperationTypePartial.CustomList<SetDataStructItemParam>
                    {
                        new SetDataStructItemParam
                        {
                            FieldName = "字段二",
                            Value = "8.5"
                        }
                    }
                };
                Assert.IsTrue(engine.RunSetDataStructItem(new ProcHandle(), setByName));
                Assert.IsTrue(store.TryGetItemValueByIndex(0, 0, 2, out object namedValue));
                Assert.AreEqual(8.5, (double)namedValue, 0.000001);

                var setByIndex = new SetDataStructItem
                {
                    StructIndex = 0,
                    ItemIndex = 0,
                    Params = new OperationTypePartial.CustomList<SetDataStructItemParam>
                    {
                        new SetDataStructItemParam
                        {
                            FieldIndex = 0,
                            Value = "3.25"
                        }
                    }
                };
                Assert.IsTrue(engine.RunSetDataStructItem(new ProcHandle(), setByIndex));

                var getAll = new GetDataStructItem
                {
                    UseNameAddressing = true,
                    StructName = "测试结构",
                    ItemName = "测试项",
                    IsAllItem = true,
                    FirstResultVariableName = "结果0"
                };
                Assert.IsTrue(engine.RunGetDataStructItem(new ProcHandle(), getAll));
                Assert.AreEqual(3.25, values.get_D_ValueByIndex(10), 0.000001);
                Assert.AreEqual(8.5, values.get_D_ValueByIndex(11), 0.000001);
            }
        }

        [TestMethod]
        public void Set_WriteValueSupportsAllVariableReferenceKinds()
        {
            PlatformRuntime runtime = CreateRuntimeWithSparseStruct();
            DataStructStore store = runtime.Stores.DataStructures;
            ValueConfigStore values = runtime.Stores.Values;
            Assert.IsTrue(values.TrySetValue(
                30, "写入源", "double", "7.25", string.Empty));
            Assert.IsTrue(values.TrySetValue(
                31, "写入指针", "double", "30", string.Empty));

            using (var engine = CreateEngine(store, values))
            {
                void Run(SetDataStructItemParam parameter)
                {
                    parameter.FieldName = "字段零";
                    var operation = new SetDataStructItem
                    {
                        StructName = "测试结构",
                        ItemName = "测试项",
                        Params = new OperationTypePartial.CustomList<SetDataStructItemParam>
                        {
                            parameter
                        }
                    };
                    Assert.IsTrue(engine.RunSetDataStructItem(
                        new ProcHandle(), operation));
                    Assert.IsTrue(store.TryGetItemValueByIndex(
                        0, 0, 0, out object actual));
                    Assert.AreEqual(7.25, (double)actual, 0.000001);
                }

                Run(new SetDataStructItemParam { ValueName = "写入源" });
                Run(new SetDataStructItemParam { ValueIndex = "30" });
                Run(new SetDataStructItemParam { ValueName2Index = "写入指针" });
                Run(new SetDataStructItemParam { ValueIndex2Index = "31" });
            }
        }

        [TestMethod]
        public void Set_WhenLaterVariableSourceIsInvalid_DoesNotPartiallyWrite()
        {
            PlatformRuntime runtime = CreateRuntimeWithSparseStruct();
            DataStructStore store = runtime.Stores.DataStructures;
            using (var engine = CreateEngine(store, runtime.Stores.Values))
            {
                var operation = new SetDataStructItem
                {
                    StructName = "测试结构",
                    ItemName = "测试项",
                    Params = new OperationTypePartial.CustomList<SetDataStructItemParam>
                    {
                        new SetDataStructItemParam
                        {
                            FieldName = "字段零",
                            Value = "100"
                        },
                        new SetDataStructItemParam
                        {
                            FieldName = "字段二",
                            ValueName = "不存在的变量"
                        }
                    }
                };

                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    engine.RunSetDataStructItem(new ProcHandle(), operation));
                Assert.IsTrue(store.TryGetItemValueByIndex(
                    0, 0, 0, out object first));
                Assert.IsTrue(store.TryGetItemValueByIndex(
                    0, 0, 2, out object second));
                Assert.AreEqual(1.0, (double)first, 0.000001);
                Assert.AreEqual(2.0, (double)second, 0.000001);
            }
        }

        [TestMethod]
        public void Set_WhenLaterValueIsInvalid_DoesNotPartiallyWriteEarlierField()
        {
            PlatformRuntime runtime = CreateRuntimeWithSparseStruct();
            DataStructStore store = runtime.Stores.DataStructures;
            using (var engine = CreateEngine(store, runtime.Stores.Values))
            {
                var operation = new SetDataStructItem
                {
                    StructIndex = 0,
                    ItemIndex = 0,
                    Params = new OperationTypePartial.CustomList<SetDataStructItemParam>
                    {
                        new SetDataStructItemParam { FieldIndex = 0, Value = "100" },
                        new SetDataStructItemParam { FieldIndex = 2, Value = "不是数值" }
                    }
                };

                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    engine.RunSetDataStructItem(new ProcHandle(), operation));
                Assert.IsTrue(store.TryGetItemValueByIndex(0, 0, 0, out object first));
                Assert.IsTrue(store.TryGetItemValueByIndex(0, 0, 2, out object second));
                Assert.AreEqual(1.0, (double)first, 0.000001);
                Assert.AreEqual(2.0, (double)second, 0.000001);
            }
        }

        [TestMethod]
        public void RuntimeSet_DoesNotPersistUntilExplicitSave()
        {
            string configPath = Path.Combine(Path.GetTempPath(),
                $"Automation-DataStruct-{Guid.NewGuid():N}");
            Directory.CreateDirectory(configPath);
            try
            {
                PlatformRuntime runtime = CreateRuntimeWithSparseStruct();
                DataStructStore store = runtime.Stores.DataStructures;
                Assert.IsTrue(store.Save(configPath));
                string filePath = Path.Combine(configPath, "DataStruct.json");
                string persistedBeforeRun = File.ReadAllText(filePath);

                using (var engine = CreateEngine(store, runtime.Stores.Values))
                {
                    var operation = new SetDataStructItem
                    {
                        StructIndex = 0,
                        ItemIndex = 0,
                        Params = new OperationTypePartial.CustomList<SetDataStructItemParam>
                        {
                            new SetDataStructItemParam { FieldIndex = 0, Value = "9.5" }
                        }
                    };
                    Assert.IsTrue(engine.RunSetDataStructItem(new ProcHandle(), operation));
                }

                Assert.AreEqual(persistedBeforeRun, File.ReadAllText(filePath),
                    "运行指令更新值时不应触发磁盘写入");
                Assert.IsTrue(store.TryGetItemValueByIndex(0, 0, 0, out object currentValue));
                Assert.AreEqual(9.5, (double)currentValue, 0.000001);

                Assert.IsTrue(store.Save(configPath));
                Assert.AreNotEqual(persistedBeforeRun, File.ReadAllText(filePath),
                    "手工编辑使用的显式保存入口应写入当前值");
            }
            finally
            {
                Directory.Delete(configPath, true);
            }
        }

        [TestMethod]
        public void ConcurrentIndexWrites_OnSameItemRemainConsistent()
        {
            const int workerCount = 32;
            const int iterations = 500;
            var runtime = new PlatformRuntime();
            DataStructStore store = runtime.Stores.DataStructures;
            Assert.IsTrue(store.AddStruct("并发结构", out string error), error);
            Assert.IsTrue(store.CreateItem(0, "共享项", 0,
                out int itemIndex, out error), error);
            for (int fieldIndex = 0; fieldIndex < workerCount; fieldIndex++)
            {
                Assert.IsTrue(store.AddField(0, itemIndex, $"字段{fieldIndex}",
                    DataStructValueType.Number, "0", fieldIndex,
                    out _, out error), error);
            }

            Parallel.For(0, workerCount, fieldIndex =>
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    if (!store.TryResolveStructIndex(false, 0, null,
                            out int structIndex, out string resolutionError))
                    {
                        throw new InvalidOperationException($"并发结构解析失败:{resolutionError}");
                    }
                    if (!store.TryResolveItemIndex(structIndex, false, itemIndex, null,
                            out int resolvedItemIndex, out resolutionError))
                    {
                        throw new InvalidOperationException($"并发数据项解析失败:{resolutionError}");
                    }
                    if (!store.TryResolveFieldIndex(structIndex, resolvedItemIndex,
                            false, fieldIndex, null, out int resolvedFieldIndex,
                            out resolutionError))
                    {
                        throw new InvalidOperationException($"并发字段解析失败:{resolutionError}");
                    }
                    string value = (fieldIndex * 1000 + iteration).ToString();
                    if (!store.TrySetItemValueByIndex(
                            structIndex, resolvedItemIndex, resolvedFieldIndex, value))
                    {
                        throw new InvalidOperationException($"并发写入失败:{fieldIndex}");
                    }
                }
            });

            for (int fieldIndex = 0; fieldIndex < workerCount; fieldIndex++)
            {
                Assert.IsTrue(store.TryGetItemValueByIndex(
                    0, itemIndex, fieldIndex, out object value));
                Assert.AreEqual(fieldIndex * 1000 + iterations - 1,
                    (double)value, 0.000001);
            }
        }

        [TestMethod]
        public void Find_WhenNoMatch_SetsAlarmAndThrows()
        {
            PlatformRuntime runtime = CreateRuntimeWithSparseStruct();
            Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                20, "查找结果", "string", "未修改", string.Empty));
            using (var engine = CreateEngine(
                runtime.Stores.DataStructures, runtime.Stores.Values))
            {
                var handle = new ProcHandle();
                var operation = new FindDataStructItem
                {
                    UseStructNameAddressing = true,
                    TargetStructName = "测试结构",
                    Type = "名称等于key",
                    Key = "不存在的数据项",
                    ResultVariableName = "查找结果"
                };

                InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
                    () => engine.RunFindDataStructItem(handle, operation));
                StringAssert.Contains(error.Message, "查找数据结构失败");
                StringAssert.Contains(handle.alarmMsg, "查找数据结构失败");
                Assert.AreEqual("未修改", runtime.Stores.Values.GetValueByIndex(20).GetCValue());
            }
        }

        [TestMethod]
        public void FindByValue_ReturnsFirstMatchingValue()
        {
            PlatformRuntime runtime = CreateRuntimeWithSparseStruct();
            DataStructStore store = runtime.Stores.DataStructures;
            Assert.IsTrue(store.AddField(0, 0, "文本字段", DataStructValueType.Text,
                "命中值", 3, out _, out string error), error);

            Assert.IsTrue(store.TryFindItemByStringValue(0, "命中值", out string text));
            Assert.AreEqual("命中值", text);
            Assert.IsTrue(store.TryFindItemByNumberValue(0, 2, out double number));
            Assert.AreEqual(2, number, 0.000001);
        }

        [TestMethod]
        public void Readiness_WhenIndexTargetIsDeletedThenRecreated_DoesNotRewriteOperation()
        {
            PlatformRuntime runtime = CreateRuntimeWithSparseStruct();
            var operation = new SetDataStructItem
            {
                StructIndex = 0,
                ItemIndex = 0,
                Params = new OperationTypePartial.CustomList<SetDataStructItemParam>
                {
                    new SetDataStructItemParam { FieldIndex = 0, Value = "5" }
                }
            };
            Proc process = CreateProcess(operation);

            ProcessReadinessAnalysis ready = ProcessReadinessService.Analyze(
                0, process, new[] { process }, runtime.CreateProcessValidationContext(),
                runtime.Stores.Values);
            Assert.IsTrue(ready.Runnable);

            Assert.IsTrue(runtime.Stores.DataStructures.TryRemoveItemAt(0, 0));
            ProcessReadinessAnalysis missing = ProcessReadinessService.Analyze(
                0, process, new[] { process }, runtime.CreateProcessValidationContext(),
                runtime.Stores.Values);
            Assert.IsFalse(missing.Runnable);
            Assert.IsTrue(missing.RunBlockers.Any(item => item.Contains("数据项索引无效:0")));
            Assert.AreEqual(0, operation.StructIndex);
            Assert.AreEqual(0, operation.ItemIndex);
            Assert.AreEqual(0, operation.Params[0].FieldIndex);

            int itemIndex;
            string error;
            Assert.IsTrue(runtime.Stores.DataStructures.CreateItem(
                0, "重建项", 0, out itemIndex, out error), error);
            int fieldIndex;
            Assert.IsTrue(runtime.Stores.DataStructures.AddField(
                0, itemIndex, "重建字段", DataStructValueType.Number,
                "0", 0, out fieldIndex, out error), error);
            ProcessReadinessAnalysis recreated = ProcessReadinessService.Analyze(
                0, process, new[] { process }, runtime.CreateProcessValidationContext(),
                runtime.Stores.Values);
            Assert.IsTrue(recreated.Runnable);
        }

        [TestMethod]
        public void NativeContract_ExposesCompactAddressAndWriteValueAlternatives()
        {
            JObject contract = StructuredOperationCompiler.BuildContract("设置结构体数据项");
            var fields = (JObject)contract["fields"];
            Assert.IsNull(fields["UseNameAddressing"]);
            Assert.IsNotNull(fields[nameof(SetDataStructItem.StructIndex)]);
            Assert.IsNotNull(fields[nameof(SetDataStructItem.ItemIndex)]);
            Assert.IsNotNull(fields[nameof(SetDataStructItem.StructName)]);
            Assert.IsNotNull(fields[nameof(SetDataStructItem.ItemName)]);
            var defaults = new SetDataStructItem();
            Assert.AreEqual(-1, defaults.StructIndex);
            Assert.AreEqual(-1, defaults.ItemIndex);

            JObject itemFields =
                fields[nameof(SetDataStructItem.Params)]?["items"]?["fields"] as JObject;
            Assert.IsNotNull(itemFields);
            Assert.IsNotNull(itemFields[nameof(SetDataStructItemParam.FieldName)]);
            Assert.IsNotNull(itemFields[nameof(SetDataStructItemParam.FieldIndex)]);
            Assert.IsNotNull(itemFields[nameof(SetDataStructItemParam.Value)]);
            Assert.IsNotNull(itemFields[nameof(SetDataStructItemParam.ValueIndex)]);
            Assert.IsNotNull(itemFields[nameof(SetDataStructItemParam.ValueIndex2Index)]);
            Assert.IsNotNull(itemFields[nameof(SetDataStructItemParam.ValueName)]);
            Assert.IsNotNull(itemFields[nameof(SetDataStructItemParam.ValueName2Index)]);

            var behavior = (JObject)contract["behavior"];
            Assert.AreEqual(OperationBehaviorCatalog.ContractVersion,
                behavior["contractVersion"]?.Value<int>());
            var fieldRules = (JObject)behavior["fieldRules"];
            Assert.IsNotNull(fieldRules[nameof(SetDataStructItem.Params)]?["requiredForRun"]);
            StringAssert.Contains(
                behavior["constraints"]?.ToString() ?? string.Empty,
                "ValueName2Index");
        }

        private static PlatformRuntime CreateRuntimeWithSparseStruct()
        {
            var runtime = new PlatformRuntime();
            DataStructStore store = runtime.Stores.DataStructures;
            string error;
            Assert.IsTrue(store.AddStruct("测试结构", out error), error);
            Assert.IsTrue(store.CreateItem(0, "测试项", 0, out int itemIndex, out error), error);
            Assert.IsTrue(store.AddField(0, itemIndex, "字段零", DataStructValueType.Number,
                "1", 0, out _, out error), error);
            Assert.IsTrue(store.AddField(0, itemIndex, "字段二", DataStructValueType.Number,
                "2", 2, out _, out error), error);
            return runtime;
        }

        private static ProcessEngine CreateEngine(
            DataStructStore dataStructStore, ValueConfigStore valueStore)
        {
            return new ProcessEngine(new EngineContext
            {
                DataStructStore = dataStructStore,
                ValueStore = valueStore
            });
        }

        private static Proc CreateProcess(OperationType operation)
        {
            var process = new Proc
            {
                head = new ProcHead
                {
                    Id = Guid.NewGuid(),
                    Name = "数据结构回归流程"
                }
            };
            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = "数据结构操作"
            };
            operation.Id = Guid.NewGuid();
            step.Ops.Add(operation);
            step.Ops.Add(new EndProcess { Id = Guid.NewGuid() });
            process.steps.Add(step);
            return process;
        }
    }
}
