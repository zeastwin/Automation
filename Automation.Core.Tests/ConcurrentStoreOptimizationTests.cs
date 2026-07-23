using System;
// 模块：核心测试 / Store并发优化。
// 职责范围：验证变量原子修改、变更序号、数据结构批量快照和字段锁顺序。

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class ConcurrentStoreOptimizationTests
    {
        [TestMethod]
        public void VariableConcurrentModify_RemainsAtomicAndMetadataUsesLatestSequence()
        {
            const int workerCount = 8;
            const int iterations = 500;
            var runtime = new PlatformRuntime();
            ValueConfigStore store = runtime.Stores.Values;
            Assert.IsTrue(store.TrySetValue(
                30, "并发计数", "double", "0", string.Empty));
            store.SetMonitorEnabled(true);
            store.SetMonitorFlag(30, true);

            var sequences = new ConcurrentBag<long>();
            store.ValueChanged += (_, args) => sequences.Add(args.Sequence);

            Parallel.For(0, workerCount, _ =>
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    bool updated = store.TryModifyValueByIndex(
                        30,
                        current => (double.Parse(
                            current, CultureInfo.InvariantCulture) + 1)
                            .ToString("G17", CultureInfo.InvariantCulture),
                        out string error,
                        "并发测试");
                    if (!updated)
                    {
                        throw new InvalidOperationException(error);
                    }
                }
            });

            DicValue value = store.GetValueByIndex(30);
            Assert.AreEqual(
                workerCount * iterations, value.GetDValue(), 0.000001);
            Assert.AreEqual(workerCount * iterations, sequences.Count);
            Assert.AreEqual(workerCount * iterations, sequences.Distinct().Count());
            Assert.AreEqual(value.ChangeSequence, value.LastChangedSequence);
            Assert.AreEqual(value.Value, value.LastChangedNewValue);
        }

        [TestMethod]
        public void DataStructBatchRead_NeverObservesPartialBatchWrite()
        {
            const int iterations = 20000;
            DataStructStore store = CreateTwoFieldStore();
            var errors = new ConcurrentQueue<string>();
            int writerFinished = 0;

            Task writer = Task.Run(() =>
            {
                for (int iteration = 1; iteration <= iterations; iteration++)
                {
                    string text = iteration.ToString(CultureInfo.InvariantCulture);
                    if (!store.TrySetItemValuesByIndex(
                            0, 0,
                            new[]
                            {
                                new DataStructFieldValueUpdate
                                {
                                    FieldIndex = 0,
                                    Value = text,
                                    ExpectedType = DataStructValueType.Number
                                },
                                new DataStructFieldValueUpdate
                                {
                                    FieldIndex = 1,
                                    Value = text,
                                    ExpectedType = DataStructValueType.Number
                                }
                            },
                            out string error))
                    {
                        errors.Enqueue(error);
                        break;
                    }
                }
                Volatile.Write(ref writerFinished, 1);
            });

            Task reader = Task.Run(() =>
            {
                int readsAfterWriter = 0;
                while (Volatile.Read(ref writerFinished) == 0
                    || readsAfterWriter++ < 100)
                {
                    if (!store.TryGetItemValuesByIndex(
                            0, 0, new[] { 0, 1 },
                            out List<DataStructFieldValueSnapshot> values,
                            out string error))
                    {
                        errors.Enqueue(error);
                        break;
                    }
                    if ((double)values[0].Value != (double)values[1].Value)
                    {
                        errors.Enqueue(
                            $"观察到部分提交:{values[0].Value}/{values[1].Value}");
                        break;
                    }
                }
            });

            Assert.IsTrue(
                Task.WaitAll(new[] { writer, reader }, TimeSpan.FromSeconds(10)),
                "批量读写未在限定时间内完成。");
            Assert.AreEqual(
                0, errors.Count,
                string.Join(Environment.NewLine, errors.ToArray()));
        }

        [TestMethod]
        public void DataStructOppositeBatchOrder_DoesNotDeadlock()
        {
            const int iterations = 5000;
            DataStructStore store = CreateTwoFieldStore();

            Task first = Task.Run(() =>
                RunBatchWriter(store, iterations, 0, 1));
            Task second = Task.Run(() =>
                RunBatchWriter(store, iterations, 1, 0));

            Assert.IsTrue(
                Task.WaitAll(new[] { first, second }, TimeSpan.FromSeconds(10)),
                "相反字段顺序的批量写入发生死锁。");
        }

        [TestMethod]
        public void DataStructSchemaPublish_DoesNotLoseConcurrentRuntimeValue()
        {
            const int iterations = 2000;
            DataStructStore store = CreateTwoFieldStore();
            var errors = new ConcurrentQueue<string>();

            Task writer = Task.Run(() =>
            {
                for (int iteration = 1; iteration <= iterations; iteration++)
                {
                    if (!store.TrySetItemValueByIndex(
                            0, 0, 0,
                            iteration.ToString(CultureInfo.InvariantCulture)))
                    {
                        errors.Enqueue($"运行值写入失败:{iteration}");
                        return;
                    }
                }
            });
            Task schemaEditor = Task.Run(() =>
            {
                for (int iteration = 0; iteration < 100; iteration++)
                {
                    if (!store.RenameField(
                            0, 0, 0, $"字段甲{iteration}", out string error))
                    {
                        errors.Enqueue(error);
                        return;
                    }
                }
            });

            Assert.IsTrue(
                Task.WaitAll(new[] { writer, schemaEditor }, TimeSpan.FromSeconds(10)),
                "运行写入和Schema发布未在限定时间内完成。");
            Assert.AreEqual(
                0, errors.Count,
                string.Join(Environment.NewLine, errors.ToArray()));
            Assert.IsTrue(store.TrySetItemValueByIndex(
                0, 0, 0, iterations.ToString(CultureInfo.InvariantCulture)));
            Assert.IsTrue(store.TryGetItemValueByIndex(
                0, 0, 0, out object finalValue));
            Assert.AreEqual(iterations, (double)finalValue, 0.000001);
        }

        private static DataStructStore CreateTwoFieldStore()
        {
            var runtime = new PlatformRuntime();
            DataStructStore store = runtime.Stores.DataStructures;
            Assert.IsTrue(store.AddStruct("并发结构", out string error), error);
            Assert.IsTrue(store.CreateItem(
                0, "共享项", 0, out int itemIndex, out error), error);
            Assert.IsTrue(store.AddField(
                0, itemIndex, "字段甲", DataStructValueType.Number,
                "0", 0, out _, out error), error);
            Assert.IsTrue(store.AddField(
                0, itemIndex, "字段乙", DataStructValueType.Number,
                "0", 1, out _, out error), error);
            return store;
        }

        private static void RunBatchWriter(
            DataStructStore store,
            int iterations,
            int firstField,
            int secondField)
        {
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                string text = iteration.ToString(CultureInfo.InvariantCulture);
                if (!store.TrySetItemValuesByIndex(
                        0, 0,
                        new[]
                        {
                            new DataStructFieldValueUpdate
                            {
                                FieldIndex = firstField,
                                Value = text
                            },
                            new DataStructFieldValueUpdate
                            {
                                FieldIndex = secondField,
                                Value = text
                            }
                        },
                        out string error))
                {
                    throw new InvalidOperationException(error);
                }
            }
        }
    }
}
