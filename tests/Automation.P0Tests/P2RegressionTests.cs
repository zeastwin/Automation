using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Automation.P0Tests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public sealed class P2RegressionTests
    {
        [TearDown]
        public void TearDown()
        {
            SF.SetUserSession(null);
            SF.ClearSecurityLock();
        }

        [Test]
        public void P2_01_AppConfigTryGetCached_BeforeInit_ShouldFail()
        {
            ResetAppConfigCacheForTest();

            bool ok = AppConfigStorage.TryGetCached(out _, out string error);

            Assert.That(ok, Is.False);
            StringAssert.Contains("程序参数缓存未初始化", error);
        }

        [Test]
        public void P2_02_AppConfigTrySave_InvalidQueueSize_ShouldReject()
        {
            using (var scope = new AppConfigScope())
            {
                bool ok = AppConfigStorage.TrySave(
                    new AppConfig { CommMaxMessageQueueSize = 0 },
                    out string error);

                Assert.That(ok, Is.False);
                StringAssert.Contains("队列长度配置无效", error);
            }
        }

        [Test]
        public void P2_03_AppConfigTrySave_ShouldRefreshCacheAndReturnClone()
        {
            using (var scope = new AppConfigScope())
            {
                bool saveOk = AppConfigStorage.TrySave(
                    new AppConfig { CommMaxMessageQueueSize = 1234 },
                    out string saveError);
                Assert.That(saveOk, Is.True, saveError);

                bool getOk1 = AppConfigStorage.TryGetCached(out AppConfig cached1, out string getError1);
                Assert.That(getOk1, Is.True, getError1);
                Assert.That(cached1.CommMaxMessageQueueSize, Is.EqualTo(1234));

                cached1.CommMaxMessageQueueSize = 9;

                bool getOk2 = AppConfigStorage.TryGetCached(out AppConfig cached2, out string getError2);
                Assert.That(getOk2, Is.True, getError2);
                Assert.That(cached2.CommMaxMessageQueueSize, Is.EqualTo(1234));

                string fileText = File.ReadAllText(scope.ConfigPath, Encoding.UTF8);
                StringAssert.Contains("1234", fileText);
            }
        }

        [Test]
        public void P2_04_ValueTrySetValue_DuplicateName_ShouldFail()
        {
            ValueConfigStore store = new ValueConfigStore();

            bool first = store.TrySetValue(0, "V_DUP", "double", "1", string.Empty, "P2");
            bool second = store.TrySetValue(1, "V_DUP", "double", "2", string.Empty, "P2");

            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
            Assert.That(store.TryGetValueByIndex(1, out _), Is.False);
        }

        [Test]
        public void P2_05_ValueTryModifyValueByIndex_DoubleInvalid_ShouldFailAndKeepOld()
        {
            ValueConfigStore store = new ValueConfigStore();
            store.TrySetValue(2, "V_D", "double", "10", string.Empty, "P2");

            bool ok = store.TryModifyValueByIndex(2, _ => "abc", out string error, "P2");

            Assert.That(ok, Is.False);
            StringAssert.Contains("不是有效数字", error);
            Assert.That(store.GetValueByIndex(2).Value, Is.EqualTo("10"));
        }

        [Test]
        public void P2_06_ValueTryModifyValueByIndex_WhenUpdaterThrows_ShouldReturnExceptionMessage()
        {
            ValueConfigStore store = new ValueConfigStore();
            store.TrySetValue(3, "V_THROW", "double", "5", string.Empty, "P2");

            bool ok = store.TryModifyValueByIndex(
                3,
                _ => throw new InvalidOperationException("boom"),
                out string error,
                "P2");

            Assert.That(ok, Is.False);
            Assert.That(error, Is.EqualTo("boom"));
            Assert.That(store.GetValueByIndex(3).Value, Is.EqualTo("5"));
        }

        [Test]
        public void P2_07_ValueSetValueByName_Unknown_ShouldReturnFalse()
        {
            ValueConfigStore store = new ValueConfigStore();

            bool ok = store.setValueByName("NOT_EXISTS", "1", "P2");

            Assert.That(ok, Is.False);
        }

        [Test]
        public void P2_08_ValueChangedEvent_WhenMonitorDisabled_ShouldNotRaise()
        {
            ValueConfigStore store = new ValueConfigStore();
            store.TrySetValue(4, "V_EVENT_OFF", "double", "1", string.Empty, "P2");

            int raised = 0;
            store.ValueChanged += (_, __) => raised++;
            store.SetMonitorFlag(4, true);
            store.SetMonitorEnabled(false);

            bool ok = store.setValueByIndex(4, 2, "P2");

            Assert.That(ok, Is.True);
            Assert.That(raised, Is.EqualTo(0));
        }

        [Test]
        public void P2_09_ValueChangedEvent_WhenMonitorEnabled_ShouldRaiseWithSource()
        {
            ValueConfigStore store = new ValueConfigStore();
            store.TrySetValue(5, "V_EVENT_ON", "double", "1", string.Empty, "P2");
            store.SetMonitorFlag(5, true);
            store.SetMonitorEnabled(true);

            ValueChangedEventArgs captured = null;
            int raised = 0;
            store.ValueChanged += (_, args) =>
            {
                raised++;
                captured = args;
            };

            bool ok = store.setValueByIndex(5, 2, "P2Source");

            Assert.That(ok, Is.True);
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.Name, Is.EqualTo("V_EVENT_ON"));
            Assert.That(captured.OldValue, Is.EqualTo("1"));
            Assert.That(captured.NewValue, Is.EqualTo("2"));
            Assert.That(captured.Source, Is.EqualTo("P2Source"));
        }

        [Test]
        public void P2_10_AccountEncryptDecrypt_ShouldRoundTrip()
        {
            string sourceJson = "{\"Accounts\":[]}";
            MethodInfo encrypt = typeof(AccountStore).GetMethod("EncryptAccountJson", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo decrypt = typeof(AccountStore).GetMethod("TryDecryptAccountJson", BindingFlags.Static | BindingFlags.NonPublic);

            string encrypted = (string)encrypt.Invoke(null, new object[] { sourceJson });
            object[] decryptArgs = { encrypted, null, null };
            bool ok = (bool)decrypt.Invoke(null, decryptArgs);

            Assert.That(encrypted.StartsWith("ENC:", StringComparison.Ordinal), Is.True);
            Assert.That(ok, Is.True);
            Assert.That((string)decryptArgs[1], Is.EqualTo(sourceJson));
            Assert.That((string)decryptArgs[2], Is.Null);
        }

        [Test]
        public void P2_11_AccountDecrypt_WhenPayloadTampered_ShouldFailIntegrityCheck()
        {
            MethodInfo encrypt = typeof(AccountStore).GetMethod("EncryptAccountJson", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo decrypt = typeof(AccountStore).GetMethod("TryDecryptAccountJson", BindingFlags.Static | BindingFlags.NonPublic);
            string encrypted = (string)encrypt.Invoke(null, new object[] { "{\"Accounts\":[]}" });

            byte[] payload = Convert.FromBase64String(encrypted.Substring("ENC:".Length));
            payload[payload.Length - 1] ^= 0x01;
            string tampered = "ENC:" + Convert.ToBase64String(payload);

            object[] decryptArgs = { tampered, null, null };
            bool ok = (bool)decrypt.Invoke(null, decryptArgs);

            Assert.That(ok, Is.False);
            StringAssert.Contains("校验失败", (string)decryptArgs[2]);
        }

        [Test]
        public void P2_12_PublishProc_InvalidInput_ShouldReject()
        {
            ProcessEngine engine = CreateEngine(new List<Proc>());

            try
            {
                bool ok1 = engine.PublishProc(-1, new Proc(), out string error1);
                bool ok2 = engine.PublishProc(0, null, out string error2);

                Assert.That(ok1, Is.False);
                Assert.That(ok2, Is.False);
                StringAssert.Contains("流程索引无效", error1);
                StringAssert.Contains("流程为空", error2);
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Test]
        public void P2_13_PlcValidateDevices_InvalidProtocol_ShouldFail()
        {
            List<PlcDevice> devices = new List<PlcDevice>
            {
                new PlcDevice
                {
                    Name = "PLC1",
                    Protocol = "Unknown",
                    Ip = "127.0.0.1",
                    Port = 102,
                    TimeoutMs = 3000,
                    CpuType = "S71200",
                    Rack = 0,
                    Slot = 1
                }
            };

            bool ok = PlcConfigStore.ValidateDevices(devices, out string error);

            Assert.That(ok, Is.False);
            StringAssert.Contains("PLC协议不支持", error);
        }

        [Test]
        public void P2_14_PlcValidateDevices_ModbusUnitIdOutOfRange_ShouldFail()
        {
            List<PlcDevice> devices = new List<PlcDevice>
            {
                new PlcDevice
                {
                    Name = "PLC2",
                    Protocol = "ModbusTcp",
                    Ip = "127.0.0.1",
                    Port = 502,
                    TimeoutMs = 3000,
                    UnitId = 300
                }
            };

            bool ok = PlcConfigStore.ValidateDevices(devices, out string error);

            Assert.That(ok, Is.False);
            StringAssert.Contains("PLC站号无效", error);
        }

        [Test]
        public void P2_15_PlcValidateMaps_ShouldEnforceReadWriteValueRules()
        {
            List<PlcMapItem> readMaps = new List<PlcMapItem>
            {
                new PlcMapItem
                {
                    PlcName = "PLC1",
                    DataType = "Float",
                    Direction = "读PLC",
                    PlcAddress = "DB1.DBW0",
                    Quantity = 1,
                    ValueName = string.Empty,
                    WriteConst = string.Empty
                }
            };
            bool readOk = PlcConfigStore.ValidateMaps(readMaps, out string readError);

            List<PlcMapItem> writeMaps = new List<PlcMapItem>
            {
                new PlcMapItem
                {
                    PlcName = "PLC1",
                    DataType = "Float",
                    Direction = "写PLC",
                    PlcAddress = "DB1.DBW0",
                    Quantity = 1,
                    ValueName = string.Empty,
                    WriteConst = string.Empty
                }
            };
            bool writeOk = PlcConfigStore.ValidateMaps(writeMaps, out string writeError);

            Assert.That(readOk, Is.False);
            Assert.That(writeOk, Is.False);
            StringAssert.Contains("PLC读操作变量不能为空", readError);
            StringAssert.Contains("PLC写操作需配置变量或常量", writeError);
        }

        [Test]
        public void P2_16_PublishProc_WhenStopped_ShouldUpdateSnapshotToNewProc()
        {
            Proc oldProc = BuildSingleDelayProc(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100, "旧流程");
            Proc newProc = BuildSingleDelayProc(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100, "新流程");
            ProcessEngine engine = CreateEngine(new List<Proc> { oldProc });

            try
            {
                bool publishOk = engine.PublishProc(0, newProc, out string publishError);
                Assert.That(publishOk, Is.True, publishError);

                EngineSnapshot snapshot = engine.GetSnapshot(0);
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot.State, Is.EqualTo(ProcRunState.Stopped));
                Assert.That(snapshot.ProcId, Is.EqualTo(newProc.head.Id));
                Assert.That(snapshot.ProcName, Is.EqualTo(newProc.head.Name));
                Assert.That(snapshot.StepIndex, Is.EqualTo(-1));
                Assert.That(snapshot.OpIndex, Is.EqualTo(-1));
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Test]
        public void P2_17_StabilitySoakProfile_ShouldCompleteRepeatedRunsWithoutDeadlock()
        {
            Proc proc = BuildSingleDelayProc(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10, "长稳加速流程");
            ProcessEngine engine = CreateEngine(new List<Proc> { proc });

            try
            {
                const int totalCycles = 30;
                int cycles = 0;
                for (int i = 0; i < totalCycles; i++)
                {
                    EngineSnapshot before = engine.GetSnapshot(0);
                    long beforeTicks = before?.UpdateTicks ?? 0;
                    engine.StartProc(proc, 0);

                    Assert.That(
                        WaitSnapshot(
                            engine,
                            0,
                            snapshot => snapshot != null
                                && snapshot.UpdateTicks > beforeTicks),
                        Is.True);
                    Assert.That(
                        WaitSnapshot(
                            engine,
                            0,
                            snapshot => snapshot != null
                                && snapshot.UpdateTicks > beforeTicks
                                && snapshot.State == ProcRunState.Stopped
                                && !snapshot.IsAlarm,
                            3000),
                        Is.True);
                    cycles++;
                }

                Assert.That(cycles, Is.EqualTo(totalCycles));
            }
            finally
            {
                engine.Stop(0);
                engine.Dispose();
            }
        }

        [Test]
        public void P2_18_FaultInjectionProfile_ShouldAlarmAndExitRunningState()
        {
            Proc tcpFaultProc = BuildProcWithSingleOp(
                new SendTcpMsg
                {
                    ID = "TCP_FAULT",
                    Msg = "UNUSED",
                    TimeOut = 1000
                },
                "通讯抖动注入");
            ProcessEngine tcpEngine = CreateEngine(new List<Proc> { tcpFaultProc });
            try
            {
                tcpEngine.StartProc(tcpFaultProc, 0);
                Assert.That(
                    WaitSnapshot(
                        tcpEngine,
                        0,
                        snapshot => snapshot != null
                            && snapshot.IsAlarm
                            && snapshot.State != ProcRunState.Running
                            && !string.IsNullOrWhiteSpace(snapshot.AlarmMessage)
                            && snapshot.AlarmMessage.Contains("通讯未初始化"),
                        3000),
                    Is.True);
            }
            finally
            {
                tcpEngine.Stop(0);
                tcpEngine.Dispose();
            }

            ValueConfigStore valueStore = new ValueConfigStore();
            valueStore.TrySetValue(0, "PLC_VALUE", "double", "1", string.Empty, "P2");
            Proc plcFaultProc = BuildProcWithSingleOp(
                new PlcReadWrite
                {
                    PlcName = "PLC_MISSING",
                    DataType = "Float",
                    DataOps = "读PLC",
                    PlcAddress = "DB1.DBW0",
                    Quantity = 1,
                    ValueName = "PLC_VALUE"
                },
                "PLC抖动注入");
            ProcessEngine plcEngine = CreateEngine(new List<Proc> { plcFaultProc }, valueStore);
            try
            {
                plcEngine.StartProc(plcFaultProc, 0);
                Assert.That(
                    WaitSnapshot(
                        plcEngine,
                        0,
                        snapshot => snapshot != null
                            && snapshot.IsAlarm
                            && snapshot.State != ProcRunState.Running
                            && !string.IsNullOrWhiteSpace(snapshot.AlarmMessage)
                            && snapshot.AlarmMessage.Contains("PLC读取失败"),
                        3000),
                    Is.True);
            }
            finally
            {
                plcEngine.Stop(0);
                plcEngine.Dispose();
            }
        }

        [Test]
        public void P2_19_MemoryDriftProfile_ShouldStayWithinBudget()
        {
            ForceFullGc();
            long baselineManaged = GC.GetTotalMemory(true);
            long baselinePrivate = Process.GetCurrentProcess().PrivateMemorySize64;

            Proc proc = BuildSingleDelayProc(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, "内存漂移流程");
            List<long> sampledPrivate = new List<long>();

            for (int i = 0; i < 40; i++)
            {
                ProcessEngine engine = CreateEngine(new List<Proc> { proc });
                try
                {
                    EngineSnapshot before = engine.GetSnapshot(0);
                    long beforeTicks = before?.UpdateTicks ?? 0;
                    engine.StartProc(proc, 0);
                    Assert.That(
                        WaitSnapshot(
                            engine,
                            0,
                            snapshot => snapshot != null
                                && snapshot.UpdateTicks > beforeTicks
                                && snapshot.State == ProcRunState.Stopped
                                && !snapshot.IsAlarm,
                            3000),
                        Is.True);
                }
                finally
                {
                    engine.Dispose();
                }

                if (i % 5 == 0)
                {
                    ForceFullGc();
                    sampledPrivate.Add(Process.GetCurrentProcess().PrivateMemorySize64);
                }
            }

            ForceFullGc();
            long finalManaged = GC.GetTotalMemory(true);
            long finalPrivate = Process.GetCurrentProcess().PrivateMemorySize64;

            long managedGrowth = finalManaged - baselineManaged;
            long privateGrowth = finalPrivate - baselinePrivate;
            long privateSampleDrift = sampledPrivate.Count == 0 ? 0 : sampledPrivate.Max() - sampledPrivate.Min();

            Assert.That(managedGrowth, Is.LessThan(16L * 1024 * 1024));
            Assert.That(privateGrowth, Is.LessThan(96L * 1024 * 1024));
            Assert.That(privateSampleDrift, Is.LessThan(96L * 1024 * 1024));
        }

        [Test]
        public void P2_20_HandleAndThreadProfile_ShouldNotShowContinuousLeakTrend()
        {
            Process process = Process.GetCurrentProcess();
            ForceFullGc();
            int baselineHandleCount = process.HandleCount;
            int baselineThreadCount = process.Threads.Count;

            for (int i = 0; i < 30; i++)
            {
                using (FrmInfo form = new FrmInfo())
                {
                    MethodInfo loadMethod = typeof(FrmInfo).GetMethod("FrmInfo_Load", BindingFlags.Instance | BindingFlags.NonPublic);
                    loadMethod.Invoke(form, new object[] { form, EventArgs.Empty });
                    form.PrintInfo($"H{i}", FrmInfo.Level.Normal);
                    MethodInfo flushMethod = typeof(FrmInfo).GetMethod("InfoFlushTimer_Tick", BindingFlags.Instance | BindingFlags.NonPublic);
                    flushMethod.Invoke(form, new object[] { form, EventArgs.Empty });
                }
            }

            ForceFullGc();
            process.Refresh();
            int finalHandleCount = process.HandleCount;
            int finalThreadCount = process.Threads.Count;

            Assert.That(finalHandleCount - baselineHandleCount, Is.LessThan(80));
            Assert.That(finalThreadCount - baselineThreadCount, Is.LessThan(12));
        }

        private static void ResetAppConfigCacheForTest()
        {
            FieldInfo cacheField = typeof(AppConfigStorage).GetField("cachedConfig", BindingFlags.Static | BindingFlags.NonPublic);
            cacheField.SetValue(null, null);
        }

        private static bool WaitSnapshot(
            ProcessEngine engine,
            int procIndex,
            Func<EngineSnapshot, bool> predicate,
            int timeoutMs = 5000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow <= deadline)
            {
                EngineSnapshot snapshot = engine.GetSnapshot(procIndex);
                if (snapshot != null && predicate(snapshot))
                {
                    return true;
                }
                Thread.Sleep(20);
            }
            return false;
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(50);
        }

        private static Proc BuildProcWithSingleOp(OperationType operation, string name)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            if (operation.Id == Guid.Empty)
            {
                operation.Id = Guid.NewGuid();
            }
            operation.Num = 0;

            return new Proc
            {
                head = new ProcHead
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Disable = false
                },
                steps = new List<Step>
                {
                    new Step
                    {
                        Id = Guid.NewGuid(),
                        Name = "步骤0",
                        Ops = new List<OperationType> { operation }
                    }
                }
            };
        }

        private static Proc BuildSingleDelayProc(Guid procId, Guid stepId, Guid opId, int delayMs, string name)
        {
            Delay delay = new Delay
            {
                Id = opId,
                Num = 0,
                timeMiniSecond = delayMs.ToString()
            };
            return new Proc
            {
                head = new ProcHead
                {
                    Id = procId,
                    Name = name,
                    Disable = false
                },
                steps = new List<Step>
                {
                    new Step
                    {
                        Id = stepId,
                        Name = "步骤0",
                        Ops = new List<OperationType> { delay }
                    }
                }
            };
        }

        private static ProcessEngine CreateEngine(IList<Proc> procs, ValueConfigStore valueStore = null)
        {
            EngineContext context = new EngineContext
            {
                Procs = procs ?? new List<Proc>(),
                ValueStore = valueStore ?? new ValueConfigStore(),
                DataStructStore = new DataStructStore(),
                TrayPointStore = new TrayPointStore(),
                CardStore = new CardConfigStore(),
                Motion = null,
                Comm = null,
                PlcStore = new PlcConfigStore(),
                AlarmInfoStore = new AlarmInfoStore(),
                IoMap = new Dictionary<string, IO>(),
                Stations = new List<DataStation>(),
                SocketInfos = new List<SocketInfo>(),
                SerialPortInfos = new List<SerialPortInfo>(),
                CustomFunc = new CustomFunc(),
                AxisStateBitGetter = null
            };

            return new ProcessEngine(context)
            {
                Logger = new SilentLogger()
            };
        }

        private sealed class SilentLogger : ILogger
        {
            public void Log(string message, LogLevel level)
            {
            }
        }

        private sealed class AppConfigScope : IDisposable
        {
            private readonly string backupPath;
            private readonly bool hasOriginal;

            public AppConfigScope()
            {
                ConfigPath = AppConfigStorage.ConfigPath;
                string directory = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                backupPath = ConfigPath + ".p2bak." + Guid.NewGuid().ToString("N");
                if (File.Exists(ConfigPath))
                {
                    File.Copy(ConfigPath, backupPath, true);
                    hasOriginal = true;
                }
            }

            public string ConfigPath { get; }

            public void Dispose()
            {
                try
                {
                    if (hasOriginal)
                    {
                        File.Copy(backupPath, ConfigPath, true);
                        File.Delete(backupPath);
                    }
                    else if (File.Exists(ConfigPath))
                    {
                        File.Delete(ConfigPath);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
