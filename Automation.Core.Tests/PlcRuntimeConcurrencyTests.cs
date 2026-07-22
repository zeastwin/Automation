using System;
// 模块：核心测试 / PLC运行时并发。
// 职责范围：验证同一PLC会话的流程并发、映射并发、重连隔离和物理端点配置约束。

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class PlcRuntimeConcurrencyTests
    {
        [TestMethod]
        public void DirectReads_OnSameDevice_AreSerialized()
        {
            using (var directory = new TemporaryDirectory())
            {
                var platform = new PlatformRuntime(directory.FullPath);
                var adapter = new BlockingPlcAdapter();
                using (var runtime = CreateRuntime(platform, _ => adapter))
                {
                    Initialize(runtime);
                    adapter.BlockIo();

                    Task<bool> first = Task.Run(() =>
                        runtime.TryRead("PLC1", CreateReadRequest(), out _, out _));
                    Assert.IsTrue(adapter.IoEntered.Wait(TimeSpan.FromSeconds(2)), "首个读取未进入适配器。" );

                    var secondSubmitted = new ManualResetEventSlim();
                    Task<bool> second = Task.Run(() =>
                    {
                        secondSubmitted.Set();
                        return runtime.TryRead("PLC1", CreateReadRequest(), out _, out _);
                    });
                    Assert.IsTrue(secondSubmitted.Wait(TimeSpan.FromSeconds(2)), "第二个读取未提交。" );
                    Thread.Sleep(100);

                    Assert.AreEqual(1, adapter.ReadCalls, "第二个流程绕过了设备会话串行边界。" );
                    Assert.AreEqual(1, adapter.MaximumConcurrentIo);

                    adapter.ReleaseIo();
                    Assert.IsTrue(Task.WaitAll(new Task[] { first, second }, 5000), "并发读取未在限定时间内结束。" );
                    Assert.IsTrue(first.Result);
                    Assert.IsTrue(second.Result);
                    Assert.AreEqual(2, adapter.ReadCalls);
                    Assert.AreEqual(1, adapter.MaximumConcurrentIo);
                }
                platform.PlcRuntime.Dispose();
            }
        }

        [TestMethod]
        public void MappingAndDirectRead_ShareOneIoBoundary()
        {
            using (var directory = new TemporaryDirectory())
            {
                var platform = new PlatformRuntime(directory.FullPath);
                var adapter = new BlockingPlcAdapter();
                using (var runtime = CreateRuntime(platform, _ => adapter))
                {
                    Initialize(runtime, 60000);
                    adapter.BlockIo();
                    Assert.IsTrue(runtime.TryStartMapping("PLC1", out string startError), startError);
                    Assert.IsTrue(adapter.IoEntered.Wait(TimeSpan.FromSeconds(2)), "映射扫描未进入适配器。" );

                    Task<bool> direct = Task.Run(() =>
                        runtime.TryRead("PLC1", CreateReadRequest(), out _, out _));
                    Thread.Sleep(100);

                    Assert.AreEqual(1, adapter.MaximumConcurrentIo);
                    Assert.AreEqual(0, adapter.ReadCalls, "流程读取不应越过正在执行的映射批次。" );

                    adapter.ReleaseIo();
                    Assert.IsTrue(direct.Wait(5000), "流程读取未在映射批次结束后继续执行。" );
                    Assert.IsTrue(direct.Result);
                    Assert.AreEqual(1, adapter.MaximumConcurrentIo);
                    Assert.IsTrue(runtime.TryStopMapping("PLC1", out string stopError), stopError);
                }
                platform.PlcRuntime.Dispose();
            }
        }

        [TestMethod]
        public void Reinitialize_WaitsForInFlightReadBeforeReplacingAdapter()
        {
            using (var directory = new TemporaryDirectory())
            {
                var platform = new PlatformRuntime(directory.FullPath);
                var firstAdapter = new BlockingPlcAdapter();
                var secondAdapter = new BlockingPlcAdapter();
                int created = 0;
                using (var runtime = CreateRuntime(platform, _ =>
                    Interlocked.Increment(ref created) == 1 ? firstAdapter : secondAdapter))
                {
                    Initialize(runtime);
                    firstAdapter.BlockIo();
                    Task<bool> read = Task.Run(() =>
                        runtime.TryRead("PLC1", CreateReadRequest(), out _, out _));
                    Assert.IsTrue(firstAdapter.IoEntered.Wait(TimeSpan.FromSeconds(2)), "读取未进入首个适配器。" );

                    Task<bool> reinitialize = Task.Run(() =>
                        runtime.TryReinitialize("PLC1", out _));
                    Thread.Sleep(100);

                    Assert.IsFalse(reinitialize.IsCompleted, "重新初始化不应越过正在执行的读取。" );
                    Assert.AreEqual(0, firstAdapter.DisposeCalls);
                    Assert.IsFalse(firstAdapter.DisposedWhileActive);

                    firstAdapter.ReleaseIo();
                    Assert.IsTrue(Task.WaitAll(new Task[] { read, reinitialize }, 5000), "读取或重新初始化超时。" );
                    Assert.IsTrue(read.Result);
                    Assert.IsTrue(reinitialize.Result);
                    Assert.AreEqual(1, firstAdapter.DisposeCalls);
                    Assert.IsFalse(firstAdapter.DisposedWhileActive);
                    Assert.AreEqual(1, secondAdapter.ConnectCalls);
                }
                platform.PlcRuntime.Dispose();
            }
        }

        [TestMethod]
        public void Configuration_RejectsDuplicateEndpointAndInvalidReceiveTimeout()
        {
            PlcConfiguration configuration = CreateConfiguration();
            PlcDeviceConfig duplicate = PlcModelClone.Clone(configuration.Devices[0]);
            duplicate.Name = "PLC2";
            configuration.Devices.Add(duplicate);

            Assert.IsFalse(PlcConfigStore.Validate(configuration, null, out string duplicateError));
            StringAssert.Contains(duplicateError, "PLC物理端点重复");

            duplicate.UnitId = 2;
            Assert.IsTrue(PlcConfigStore.Validate(configuration, null, out string validError), validError);

            duplicate.ReceiveTimeoutMs = 99;
            Assert.IsFalse(PlcConfigStore.Validate(configuration, null, out string timeoutError));
            StringAssert.Contains(timeoutError, "接收超时必须为100..60000ms");
        }

        [TestMethod]
        public void Load_PersistsDefaultReceiveTimeoutWhenFieldIsMissing()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = Path.Combine(directory.FullPath, PlcConfigStore.ConfigFileName);
                JObject json = JObject.FromObject(CreateConfiguration(), JsonSerializer.CreateDefault());
                foreach (JObject device in (JArray)json["Devices"])
                {
                    device.Remove("ReceiveTimeoutMs");
                }
                File.WriteAllText(path, json.ToString());

                var store = new PlcConfigStore();
                Assert.IsTrue(store.Load(directory.FullPath, null, out string error), error);
                Assert.AreEqual(10000, store.GetSnapshot().Devices[0].ReceiveTimeoutMs);
                StringAssert.Contains(File.ReadAllText(path), "\"receiveTimeoutMs\": 10000");
            }
        }

        private static PlcRuntimeService CreateRuntime(
            PlatformRuntime platform,
            Func<PlcDeviceConfig, IPlcAdapter> factory)
        {
            return new PlcRuntimeService(platform.Stores.Plc, platform.Stores.Values, factory);
        }

        private static void Initialize(PlcRuntimeService runtime, int scanIntervalMs = 50)
        {
            PlcConfiguration configuration = CreateConfiguration();
            configuration.Devices[0].ScanIntervalMs = scanIntervalMs;
            Assert.IsTrue(runtime.ReloadConfiguration(configuration, false, out string reloadError), reloadError);
            Assert.IsTrue(runtime.TryReinitialize("PLC1", out string initializeError), initializeError);
        }

        private static PlcConfiguration CreateConfiguration()
        {
            return new PlcConfiguration
            {
                Devices = new List<PlcDeviceConfig>
                {
                    new PlcDeviceConfig
                    {
                        Name = "PLC1",
                        IpAddress = "127.0.0.1",
                        Port = 502,
                        UnitId = 1,
                        AutoConnect = false,
                        ReceiveTimeoutMs = 10000,
                        Mappings = new List<PlcMapConfig>()
                    }
                }
            };
        }

        private static PlcMapConfig CreateReadRequest()
        {
            return new PlcMapConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "并发读取",
                Area = PlcArea.HoldingRegister,
                StartAddress = 0,
                DataType = PlcDataType.UShort,
                Direction = PlcMapDirection.ReadFromPlc,
                ElementCount = 1,
                VariableNames = new List<string>()
            };
        }

        private sealed class BlockingPlcAdapter : IPlcAdapter
        {
            private readonly ManualResetEventSlim releaseIo = new ManualResetEventSlim(true);
            private int activeIo;
            private int maximumConcurrentIo;
            private int readCalls;
            private int connectCalls;
            private int disposeCalls;
            private int disposedWhileActive;

            public ManualResetEventSlim IoEntered { get; } = new ManualResetEventSlim();
            public int MaximumConcurrentIo => Volatile.Read(ref maximumConcurrentIo);
            public int ReadCalls => Volatile.Read(ref readCalls);
            public int ConnectCalls => Volatile.Read(ref connectCalls);
            public int DisposeCalls => Volatile.Read(ref disposeCalls);
            public bool DisposedWhileActive => Volatile.Read(ref disposedWhileActive) != 0;

            public void BlockIo()
            {
                IoEntered.Reset();
                releaseIo.Reset();
            }

            public void ReleaseIo()
            {
                releaseIo.Set();
            }

            public bool Connect(out string error)
            {
                Interlocked.Increment(ref connectCalls);
                error = null;
                return true;
            }

            public void Close()
            {
                if (Volatile.Read(ref activeIo) != 0) Interlocked.Exchange(ref disposedWhileActive, 1);
            }

            public bool Read(PlcMapConfig map, out object[] values, out string error)
            {
                Interlocked.Increment(ref readCalls);
                bool success = ExecuteIo(out error);
                values = success ? new object[] { (ushort)1 } : null;
                return success;
            }

            public bool ReadBatch(IReadOnlyList<PlcMapConfig> maps,
                out IReadOnlyDictionary<string, object[]> values, out string error)
            {
                bool success = ExecuteIo(out error);
                var result = new Dictionary<string, object[]>(StringComparer.Ordinal);
                if (success)
                {
                    foreach (PlcMapConfig map in maps)
                    {
                        result.Add(map.Id, new object[] { (ushort)1 });
                    }
                }
                values = result;
                return success;
            }

            public bool Write(PlcMapConfig map, IReadOnlyList<object> values, out string error)
            {
                return ExecuteIo(out error);
            }

            public void Dispose()
            {
                if (Volatile.Read(ref activeIo) != 0) Interlocked.Exchange(ref disposedWhileActive, 1);
                Interlocked.Increment(ref disposeCalls);
                releaseIo.Set();
            }

            private bool ExecuteIo(out string error)
            {
                int current = Interlocked.Increment(ref activeIo);
                UpdateMaximum(current);
                IoEntered.Set();
                try
                {
                    if (!releaseIo.Wait(TimeSpan.FromSeconds(5)))
                    {
                        error = "测试适配器等待释放超时。";
                        return false;
                    }
                    error = null;
                    return true;
                }
                finally
                {
                    Interlocked.Decrement(ref activeIo);
                }
            }

            private void UpdateMaximum(int value)
            {
                while (true)
                {
                    int current = Volatile.Read(ref maximumConcurrentIo);
                    if (current >= value) return;
                    if (Interlocked.CompareExchange(ref maximumConcurrentIo, value, current) == current) return;
                }
            }
        }
    }
}
