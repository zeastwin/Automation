using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using static Automation.OperationTypePartial;

namespace Automation.P0Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class P0RegressionTests
    {
        [TearDown]
        public void TearDown()
        {
            SF.SetUserSession(null);
            SF.ClearSecurityLock();
        }

        [Test]
        public void P0_01_AppConfigMissing_ShouldCreateDefault()
        {
            using (var scope = new AppConfigScope())
            {
                scope.DeleteConfig();

                bool ok = AppConfigStorage.TryLoad(out AppConfig config, out string error);

                Assert.That(ok, Is.True, error);
                Assert.That(config, Is.Not.Null);
                Assert.That(config.CommMaxMessageQueueSize, Is.EqualTo(AppConfigStorage.DefaultCommMaxMessageQueueSize));
                Assert.That(File.Exists(scope.ConfigPath), Is.True);

                string json = File.ReadAllText(scope.ConfigPath, Encoding.UTF8);
                StringAssert.Contains(AppConfigStorage.CommMaxMessageQueueSizeKey, json);
                StringAssert.Contains(AppConfigStorage.DefaultCommMaxMessageQueueSize.ToString(), json);
            }
        }

        [Test]
        public void P0_02_AppConfigInvalidType_ShouldFailLoad()
        {
            using (var scope = new AppConfigScope())
            {
                scope.WriteRaw("{\"CommMaxMessageQueueSize\":\"abc\"}");

                bool ok = AppConfigStorage.TryLoad(out _, out string error);

                Assert.That(ok, Is.False);
                StringAssert.Contains("队列长度类型无效", error);
            }
        }

        [Test]
        public void P0_03_AccountConfigWithoutEncryptPrefix_ShouldBeInvalid()
        {
            using (var dir = new TempDirectory())
            {
                string accountFile = Path.Combine(dir.Path, "Account.json");
                File.WriteAllText(accountFile, "{\"Accounts\":[]}", Encoding.UTF8);

                AccountStore store = new AccountStore();
                AccountLoadResult result = store.Load(dir.Path, "system", "software_123", out string error);

                Assert.That(result, Is.EqualTo(AccountLoadResult.Invalid));
                Assert.That(store.IsConfigValid, Is.False);
                StringAssert.Contains("账户配置格式无效", error);
            }
        }

        [Test]
        public void P0_04_WorkIndexContinuity_ShouldReportGapAndNonZeroStart()
        {
            using (var dir = new TempDirectory())
            {
                File.WriteAllText(Path.Combine(dir.Path, "1.json"), "{}", Encoding.UTF8);
                File.WriteAllText(Path.Combine(dir.Path, "3.json"), "{}", Encoding.UTF8);

                Dictionary<int, string> indexMap = FrmProc.BuildProcFileIndexMap(dir.Path, out int maxIndex);
                List<string> errors = FrmProc.ValidateProcFileContinuity(indexMap, maxIndex);

                Assert.That(errors.Any(item => item.Contains("流程文件索引必须从0开始")), Is.True);
                Assert.That(errors.Any(item => item.Contains("流程文件缺失：0.json")), Is.True);
                Assert.That(errors.Any(item => item.Contains("流程文件缺失：2.json")), Is.True);
            }
        }

        [Test]
        public void P0_05_GotoValidation_ShouldRejectCrossProcAndOutOfRange()
        {
            Proc proc = BuildProc(
                new Goto { DefaultGoto = "1-0-0" },
                new Goto { DefaultGoto = "0-99-0" });

            List<string> errors = FrmProc.ValidateProcGotoTargets(0, proc);

            Assert.That(errors.Any(item => item.Contains("跳转地址跨流程")), Is.True);
            Assert.That(errors.Any(item => item.Contains("跳转地址步骤越界")), Is.True);
        }

        [Test]
        public void P0_06_StartPauseResumeStop_ShouldSwitchStatesCorrectly()
        {
            Proc proc = BuildProc(
                new Delay { timeMiniSecond = "800" },
                new Delay { timeMiniSecond = "1500" });
            ProcessEngine engine = CreateEngine(new List<Proc> { proc });

            try
            {
                engine.StartProc(proc, 0);
                Assert.That(WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.Running), Is.True);

                engine.Pause(0);
                Assert.That(WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.Paused), Is.True);

                engine.Resume(0);
                Assert.That(WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.Running), Is.True);

                engine.Stop(0);
                Assert.That(WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.Stopped), Is.True);
            }
            finally
            {
                engine.Stop(0);
                engine.Dispose();
            }
        }

        [Test]
        public void P0_07_SingleStep_ShouldReturnToSingleStepAfterOneOp()
        {
            Proc proc = BuildProc(
                new Delay { timeMiniSecond = "300" },
                new Delay { timeMiniSecond = "300" });
            ProcessEngine engine = CreateEngine(new List<Proc> { proc });

            try
            {
                engine.StartProcAt(proc, 0, 0, 0, ProcRunState.SingleStep);
                Assert.That(WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.SingleStep), Is.True);

                engine.Step(0);

                Assert.That(WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.Running), Is.True);
                Assert.That(WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.SingleStep), Is.True);
            }
            finally
            {
                engine.Stop(0);
                engine.Dispose();
            }
        }

        [Test]
        public void P0_08_RunSingleOpOnce_ShouldStopAfterTargetOperation()
        {
            Proc proc = BuildProc(
                new Delay { timeMiniSecond = "200" },
                new Delay { timeMiniSecond = "1200" });
            ProcessEngine engine = CreateEngine(new List<Proc> { proc });

            try
            {
                EngineSnapshot before = engine.GetSnapshot(0);
                engine.RunSingleOpOnce(proc, 0, 0, 0);

                Assert.That(
                    WaitSnapshot(
                        engine,
                        0,
                        snapshot => snapshot != null
                            && snapshot.UpdateTicks > (before?.UpdateTicks ?? 0)
                            && snapshot.State != ProcRunState.Stopped),
                    Is.True);
                Assert.That(
                    WaitSnapshot(
                        engine,
                        0,
                        snapshot => snapshot != null
                            && snapshot.UpdateTicks > (before?.UpdateTicks ?? 0)
                            && snapshot.State == ProcRunState.Stopped
                            && snapshot.StepIndex == 0
                            && snapshot.OpIndex == 0),
                    Is.True);
                EngineSnapshot snapshot = engine.GetSnapshot(0);
                Assert.That(snapshot.StepIndex, Is.EqualTo(0));
                Assert.That(snapshot.OpIndex, Is.EqualTo(0));
            }
            finally
            {
                engine.Stop(0);
                engine.Dispose();
            }
        }

        [Test]
        public void P0_09_RestartWhenWorkerStuck_ShouldRaiseStopTimeoutAlarm()
        {
            const string functionName = "BlockingFunction";
            CustomFunc customFunc = BuildBlockingCustomFunc(functionName, 4500);
            Proc proc = BuildProc(new CallCustomFunc { Name = functionName });
            ProcessEngine engine = CreateEngine(new List<Proc> { proc }, customFunc: customFunc);

            try
            {
                engine.StartProc(proc, 0);
                Assert.That(WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.Running), Is.True);

                engine.StartProc(proc, 0);

                Assert.That(
                    WaitSnapshot(
                        engine,
                        0,
                        snapshot => snapshot.IsAlarm && !string.IsNullOrWhiteSpace(snapshot.AlarmMessage) && snapshot.AlarmMessage.Contains("停止超时"),
                        7000),
                    Is.True);
            }
            finally
            {
                engine.Stop(0);
                engine.Dispose();
            }
        }

        [Test]
        public void P0_10_ExecuteOperationErrorPaths_ShouldMarkAlarm()
        {
            ProcessEngine engine = CreateEngine(new List<Proc>());

            ProcHandle nullOpHandle = CreateHandle();
            bool nullResult = engine.ExecuteOperation(nullOpHandle, null);
            Assert.That(nullResult, Is.False);
            Assert.That(nullOpHandle.isAlarm, Is.True);
            StringAssert.Contains("操作为空", nullOpHandle.alarmMsg);

            ProcHandle unknownOpHandle = CreateHandle();
            bool unknownResult = engine.ExecuteOperation(unknownOpHandle, new object());
            Assert.That(unknownResult, Is.False);
            Assert.That(unknownOpHandle.isAlarm, Is.True);
            StringAssert.Contains("操作类型不支持", unknownOpHandle.alarmMsg);

            ProcHandle exceptionHandle = CreateHandle();
            bool exceptionResult = engine.ExecuteOperation(exceptionHandle, new CallCustomFunc { Name = "Missing" });
            Assert.That(exceptionResult, Is.False);
            Assert.That(exceptionHandle.isAlarm, Is.True);
            StringAssert.Contains("自定义函数未初始化", exceptionHandle.alarmMsg);

            engine.Dispose();
        }

        [Test]
        public void P0_11_IoGroupOutputFailure_ShouldIncludeStagePrefix()
        {
            Dictionary<string, IO> ioMap = new Dictionary<string, IO>(StringComparer.OrdinalIgnoreCase)
            {
                ["O1"] = new IO { Name = "O1", IOType = "通用输出" },
                ["I1"] = new IO { Name = "I1", IOType = "通用输入" }
            };
            ProcessEngine engine = CreateEngine(new List<Proc>(), ioMap: ioMap);
            IoGroup ioGroup = new IoGroup
            {
                OutIoParams = new CustomList<IoOutParam>
                {
                    new IoOutParam { IOName = "O1", value = true, delayBefore = -1, delayAfter = -1 }
                },
                CheckIoParams = new CustomList<IoCheckParam>
                {
                    new IoCheckParam { IOName = "I1", value = true }
                },
                timeOutC = new TimeOutC { TimeOut = 50 }
            };

            ProcHandle handle = CreateHandle();
            bool ok = engine.ExecuteOperation(handle, ioGroup);

            Assert.That(ok, Is.False);
            Assert.That(handle.isAlarm, Is.True);
            StringAssert.Contains("IO组执行失败(输出阶段)", handle.alarmMsg);
            StringAssert.Contains("运动控制未初始化", handle.alarmMsg);

            engine.Dispose();
        }

        [Test]
        public void P0_12_ReceiveDispatcher_ShouldRejectWhenPendingQueueIsFull()
        {
            Type dispatcherType = typeof(CommunicationHub).Assembly.GetType("Automation.ReceiveDispatcher", true);
            object dispatcher = Activator.CreateInstance(dispatcherType, 2);
            MethodInfo waitNextAsync = dispatcherType.GetMethod("WaitNextAsync", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo close = dispatcherType.GetMethod("Close", BindingFlags.Instance | BindingFlags.Public);

            waitNextAsync.Invoke(dispatcher, new object[] { 10000, CancellationToken.None });
            waitNextAsync.Invoke(dispatcher, new object[] { 10000, CancellationToken.None });

            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() =>
                waitNextAsync.Invoke(dispatcher, new object[] { 10000, CancellationToken.None }));

            Assert.That(ex?.InnerException, Is.TypeOf<InvalidOperationException>());
            StringAssert.Contains("接收等待队列已满", ex.InnerException.Message);

            close.Invoke(dispatcher, new object[] { "test" });
        }

        [Test]
        public void P0_13_ParseFrameSettings_ShouldEnforceStrictModeAndDelimiter()
        {
            MethodInfo parseFrameSettings = typeof(CommunicationHub).GetMethod(
                "ParseFrameSettings",
                BindingFlags.Static | BindingFlags.NonPublic);

            object rawSettings = parseFrameSettings.Invoke(null, new object[] { "Raw", "\\n", "utf-8", "RawCase" });
            PropertyInfo modeProperty = rawSettings.GetType().GetProperty("Mode");
            PropertyInfo delimiterProperty = rawSettings.GetType().GetProperty("Delimiter");

            Assert.That(modeProperty.GetValue(rawSettings).ToString(), Is.EqualTo("Raw"));
            Assert.That(delimiterProperty.GetValue(rawSettings), Is.Null);

            object delimiterSettings = parseFrameSettings.Invoke(null, new object[] { "Delimiter", "\\r\\n", "utf-8", "DelimiterCase" });
            byte[] delimiter = (byte[])delimiterProperty.GetValue(delimiterSettings);

            Assert.That(modeProperty.GetValue(delimiterSettings).ToString(), Is.EqualTo("Delimiter"));
            Assert.That(delimiter, Is.Not.Null);
            Assert.That(delimiter.Length, Is.EqualTo(2));
            Assert.That(delimiter[0], Is.EqualTo((byte)'\r'));
            Assert.That(delimiter[1], Is.EqualTo((byte)'\n'));

            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() =>
                parseFrameSettings.Invoke(null, new object[] { "InvalidMode", "\\n", "utf-8", "InvalidCase" }));
            Assert.That(ex?.InnerException, Is.TypeOf<InvalidOperationException>());
            StringAssert.Contains("帧模式无效", ex.InnerException.Message);
        }

        [Test]
        public void P0_14_PlcReadWrite_WhenVariableMissing_ShouldFailSafely()
        {
            ValueConfigStore valueStore = new ValueConfigStore();
            PlcConfigStore plcStore = new PlcConfigStore();
            ProcessEngine engine = CreateEngine(new List<Proc>(), valueStore: valueStore, plcStore: plcStore);

            PlcReadWrite operation = new PlcReadWrite
            {
                PlcName = "PLC1",
                DataType = "Float",
                DataOps = "读PLC",
                PlcAddress = "DB1.DBW0",
                Quantity = 1,
                ValueName = "不存在变量"
            };

            ProcHandle handle = CreateHandle();
            bool ok = engine.RunPlcReadWrite(handle, operation);

            Assert.That(ok, Is.False);
            Assert.That(handle.isAlarm, Is.True);
            StringAssert.Contains("变量不存在", handle.alarmMsg);

            engine.Dispose();
        }

        [Test]
        public void P0_15_DefaultRolePermissions_ShouldMatchCatalogDefinition()
        {
            UserSession systemAdmin = new UserSession(
                new UserAccount
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "system",
                    Role = UserRole.SystemAdmin,
                    Permissions = new List<string>(PermissionCatalog.GetDefaultPermissions(UserRole.SystemAdmin))
                },
                false);

            UserSession admin = new UserSession(
                new UserAccount
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "admin",
                    Role = UserRole.Admin,
                    Permissions = new List<string>(PermissionCatalog.GetDefaultPermissions(UserRole.Admin))
                },
                false);

            UserSession op = new UserSession(
                new UserAccount
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = "op",
                    Role = UserRole.Operator,
                    Permissions = new List<string>(PermissionCatalog.GetDefaultPermissions(UserRole.Operator))
                },
                false);

            Assert.That(systemAdmin.HasPermission(PermissionKeys.AccountManage), Is.True);
            Assert.That(admin.HasPermission(PermissionKeys.AccountManage), Is.False);
            Assert.That(op.HasPermission(PermissionKeys.ProcessRun), Is.True);
            Assert.That(op.HasPermission(PermissionKeys.ProcessEdit), Is.False);
        }

        [Test]
        public void P0_16_LockedMode_ShouldAllowOnlyDefaultRecoveryLogin()
        {
            using (var dir = new TempDirectory())
            {
                File.WriteAllText(Path.Combine(dir.Path, "Account.json"), "{}", Encoding.UTF8);

                AccountStore accountStore = new AccountStore();
                AccountLoadResult loadResult = accountStore.Load(dir.Path, "system", "software_123", out _);
                Assert.That(loadResult, Is.EqualTo(AccountLoadResult.Invalid));

                UserLoginStore loginStore = new UserLoginStore(accountStore);

                bool wrongLoginOk = loginStore.TryLogin("admin", "123456", out string wrongError);
                Assert.That(wrongLoginOk, Is.False);
                StringAssert.Contains("锁定模式仅允许默认系统管理员登录", wrongError);

                bool recoveryLoginOk = loginStore.TryLogin("system", "software_123", out string recoveryError);
                Assert.That(recoveryLoginOk, Is.True, recoveryError);
                Assert.That(SF.userSession, Is.Not.Null);
                Assert.That(SF.userSession.IsRecovery, Is.True);
                Assert.That(SF.userSession.HasPermission(PermissionKeys.AccountManage), Is.True);
                Assert.That(SF.userSession.HasPermission(PermissionKeys.ProcessRun), Is.False);
            }
        }

        private static ProcHandle CreateHandle()
        {
            return new ProcHandle
            {
                procNum = 0,
                stepNum = 0,
                opsNum = 0,
                procName = "P0",
                procId = Guid.NewGuid(),
                State = ProcRunState.Running,
                CancellationToken = CancellationToken.None
            };
        }

        private static Proc BuildProc(params OperationType[] operations)
        {
            List<OperationType> ops = new List<OperationType>(operations ?? Array.Empty<OperationType>());
            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i] == null)
                {
                    continue;
                }
                ops[i].Num = i;
                if (ops[i].Id == Guid.Empty)
                {
                    ops[i].Id = Guid.NewGuid();
                }
            }

            return new Proc
            {
                head = new ProcHead
                {
                    Name = "P0流程",
                    Disable = false
                },
                steps = new List<Step>
                {
                    new Step
                    {
                        Id = Guid.NewGuid(),
                        Name = "步骤0",
                        Ops = ops
                    }
                }
            };
        }

        private static ProcessEngine CreateEngine(
            IList<Proc> procs,
            ValueConfigStore valueStore = null,
            Dictionary<string, IO> ioMap = null,
            PlcConfigStore plcStore = null,
            CustomFunc customFunc = null)
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
                PlcStore = plcStore,
                AlarmInfoStore = new AlarmInfoStore(),
                IoMap = ioMap,
                Stations = new List<DataStation>(),
                SocketInfos = new List<SocketInfo>(),
                SerialPortInfos = new List<SerialPortInfo>(),
                CustomFunc = customFunc,
                AxisStateBitGetter = null
            };

            return new ProcessEngine(context)
            {
                Logger = new TestLogger()
            };
        }

        private static CustomFunc BuildBlockingCustomFunc(string functionName, int sleepMs)
        {
            CustomFunc customFunc = new CustomFunc();

            FieldInfo mapField = typeof(CustomFunc).GetField("functionMap", BindingFlags.Instance | BindingFlags.NonPublic);
            var map = new Dictionary<string, CustomFunc.FunctionDelegate>(StringComparer.Ordinal)
            {
                [functionName] = () => Thread.Sleep(sleepMs)
            };
            mapField.SetValue(customFunc, map);

            customFunc.funcName.Clear();
            customFunc.funcName.Add(functionName);

            return customFunc;
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

        private sealed class TestLogger : ILogger
        {
            public readonly List<string> Messages = new List<string>();

            public void Log(string message, LogLevel level)
            {
                lock (Messages)
                {
                    Messages.Add($"[{level}] {message}");
                }
            }
        }

        private sealed class TempDirectory : IDisposable
        {
            public TempDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "automation-p0-tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Path))
                    {
                        Directory.Delete(Path, true);
                    }
                }
                catch
                {
                }
            }
        }

        private sealed class AppConfigScope : IDisposable
        {
            private readonly string backupPath;
            private readonly bool hasOriginal;

            public AppConfigScope()
            {
                ConfigPath = AppConfigStorage.ConfigPath;
                string directory = System.IO.Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                backupPath = ConfigPath + ".p0bak." + Guid.NewGuid().ToString("N");
                if (File.Exists(ConfigPath))
                {
                    File.Copy(ConfigPath, backupPath, true);
                    hasOriginal = true;
                }
            }

            public string ConfigPath { get; }

            public void DeleteConfig()
            {
                if (File.Exists(ConfigPath))
                {
                    File.Delete(ConfigPath);
                }
            }

            public void WriteRaw(string json)
            {
                string directory = System.IO.Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            }

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
