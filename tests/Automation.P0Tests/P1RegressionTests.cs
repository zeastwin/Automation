using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using System.Windows.Forms;

namespace Automation.P0Tests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public sealed class P1RegressionTests
    {
        [TearDown]
        public void TearDown()
        {
            SF.SetUserSession(null);
            SF.ClearSecurityLock();
        }

        [Test]
        public void P1_01_HotPublishWithSameIds_ShouldSwitchToNewProcWithoutAlarm()
        {
            Guid stepId = Guid.NewGuid();
            Guid op1Id = Guid.NewGuid();
            Guid op2Id = Guid.NewGuid();
            Guid oldProcId = Guid.NewGuid();
            Guid newProcId = Guid.NewGuid();
            Proc oldProc = BuildTwoDelayProc(oldProcId, stepId, op1Id, op2Id, 1200, 1200, "旧流程");
            Proc newProc = BuildTwoDelayProc(newProcId, stepId, op1Id, op2Id, 10, 10, "新流程");
            CollectLogger logger = new CollectLogger();
            ProcessEngine engine = CreateEngine(new List<Proc> { oldProc }, logger);

            try
            {
                engine.StartProc(oldProc, 0);
                Assert.That(
                    WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.Running && snapshot.OpIndex == 0),
                    Is.True);

                bool publishOk = engine.PublishProc(0, newProc, out string publishError);
                Assert.That(publishOk, Is.True, publishError);

                Assert.That(
                    WaitSnapshot(engine, 0, snapshot => snapshot.ProcId == newProcId, 3000),
                    Is.True);
                Assert.That(
                    WaitSnapshot(engine, 0, snapshot => snapshot.State == ProcRunState.Stopped && !snapshot.IsAlarm && snapshot.ProcId == newProcId, 8000),
                    Is.True);
                Assert.That(logger.Contains("热更新失败"), Is.False);
            }
            finally
            {
                engine.Stop(0);
                engine.Dispose();
            }
        }

        [Test]
        public void P1_02_HotPublishWithMissingStepId_ShouldAlarmAndStop()
        {
            Guid oldStepId = Guid.NewGuid();
            Guid oldOp1Id = Guid.NewGuid();
            Guid oldOp2Id = Guid.NewGuid();
            Proc oldProc = BuildTwoDelayProc(Guid.NewGuid(), oldStepId, oldOp1Id, oldOp2Id, 1200, 1200, "旧流程");
            Proc newProc = BuildTwoDelayProc(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10, 10, "坏流程");
            CollectLogger logger = new CollectLogger();
            ProcessEngine engine = CreateEngine(new List<Proc> { oldProc }, logger);

            try
            {
                bool publishOk = engine.PublishProc(0, newProc, out string publishError);
                Assert.That(publishOk, Is.True, publishError);

                ProcessControl control = new ProcessControl();
                control.SetRunning();
                ProcHandle handle = new ProcHandle
                {
                    procNum = 0,
                    stepNum = 0,
                    opsNum = 0,
                    procName = oldProc.head?.Name,
                    procId = oldProc.head?.Id ?? Guid.Empty,
                    Proc = oldProc,
                    State = ProcRunState.Running,
                    CancellationToken = control.CancellationToken,
                    Control = control
                };

                MethodInfo applyPendingUpdate = typeof(ProcessEngine).GetMethod(
                    "TryApplyPendingProcUpdate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                bool applyResult = (bool)applyPendingUpdate.Invoke(engine, new object[] { handle, control });

                Assert.That(applyResult, Is.False);
                Assert.That(handle.isAlarm, Is.True);
                StringAssert.Contains("热更新失败", handle.alarmMsg);
                Assert.That(control.IsStopRequested, Is.True);
                Assert.That(logger.Contains("热更新失败"), Is.True);
            }
            finally
            {
                engine.Stop(0);
                engine.Dispose();
            }
        }

        [Test]
        public void P1_03_SnapshotThrottle_ShouldSkipSameUpdateWithinWindow()
        {
            Proc proc = BuildSingleDelayProc(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10, "节流流程");
            ProcessEngine engine = CreateEngine(new List<Proc> { proc }, new CollectLogger());
            MethodInfo updateSnapshot = typeof(ProcessEngine).GetMethod(
                "UpdateSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic);

            try
            {
                engine.SnapshotThrottleMilliseconds = 300;
                object[] args =
                {
                    0, proc.head.Id, proc.head.Name, ProcRunState.Running, 0, 0, false, false, null, false
                };

                updateSnapshot.Invoke(engine, args);
                EngineSnapshot first = engine.GetSnapshot(0);
                Thread.Sleep(30);
                updateSnapshot.Invoke(engine, args);
                EngineSnapshot second = engine.GetSnapshot(0);

                Assert.That(second.UpdateTicks, Is.EqualTo(first.UpdateTicks));
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Test]
        public void P1_04_SnapshotThrottle_ShouldAllowUpdateAfterWindow()
        {
            Proc proc = BuildSingleDelayProc(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10, "节流流程");
            ProcessEngine engine = CreateEngine(new List<Proc> { proc }, new CollectLogger());
            MethodInfo updateSnapshot = typeof(ProcessEngine).GetMethod(
                "UpdateSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic);

            try
            {
                engine.SnapshotThrottleMilliseconds = 150;
                object[] args =
                {
                    0, proc.head.Id, proc.head.Name, ProcRunState.Running, 0, 0, false, false, null, false
                };

                updateSnapshot.Invoke(engine, args);
                EngineSnapshot first = engine.GetSnapshot(0);
                Thread.Sleep(220);
                updateSnapshot.Invoke(engine, args);
                EngineSnapshot second = engine.GetSnapshot(0);

                Assert.That(second.UpdateTicks, Is.GreaterThan(first.UpdateTicks));
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Test]
        public void P1_05_InfoFlush_ShouldRespectBatchSize256()
        {
            FrmInfo form = new FrmInfo();
            try
            {
                for (int i = 1; i <= 300; i++)
                {
                    form.PrintInfo($"M{i}", FrmInfo.Level.Normal);
                }

                InvokeFormPrivate(form, "InfoFlushTimer_Tick", null, EventArgs.Empty);

                int pendingCount = GetPrivateQueueCount(form, "pendingInfoQueue");
                Assert.That(pendingCount, Is.EqualTo(44));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Test]
        public void P1_06_InfoBuffer_ShouldKeepLatest200Entries()
        {
            FrmInfo form = new FrmInfo();
            try
            {
                for (int i = 1; i <= 300; i++)
                {
                    form.PrintInfo($"M{i}", FrmInfo.Level.Normal);
                }

                InvokeFormPrivate(form, "InfoFlushTimer_Tick", null, EventArgs.Empty);
                InvokeFormPrivate(form, "InfoFlushTimer_Tick", null, EventArgs.Empty);

                object buffer = GetPrivateField(form, "infoLogBuffer");
                Type bufferType = buffer.GetType();
                int count = (int)bufferType.GetProperty("Count").GetValue(buffer);
                object first = bufferType.GetProperty("Item").GetValue(buffer, new object[] { 0 });
                object last = bufferType.GetProperty("Item").GetValue(buffer, new object[] { count - 1 });
                string firstMessage = (string)first.GetType().GetProperty("Message").GetValue(first);
                string lastMessage = (string)last.GetType().GetProperty("Message").GetValue(last);

                Assert.That(count, Is.EqualTo(200));
                StringAssert.Contains("M101", firstMessage);
                StringAssert.Contains("M300", lastMessage);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Test]
        public void P1_07_InfoAutoScrollTimer_ShouldResumeAfterIdle20s()
        {
            FrmInfo form = new FrmInfo();
            try
            {
                SetPrivateField(form, "infoAutoScrollPausedByUser", true);
                SetPrivateField(form, "infoLastInteractionUtc", DateTime.UtcNow.AddMilliseconds(-21000));

                InvokeFormPrivate(form, "InfoAutoScrollTimer_Tick", null, EventArgs.Empty);

                bool paused = (bool)GetPrivateField(form, "infoAutoScrollPausedByUser");
                Assert.That(paused, Is.False);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Test]
        public void P1_08_ReceiveDispatcherClose_ShouldFailPendingWaiters()
        {
            Type dispatcherType = typeof(CommunicationHub).Assembly.GetType("Automation.ReceiveDispatcher", true);
            object dispatcher = Activator.CreateInstance(dispatcherType, 8);
            MethodInfo waitNextAsync = dispatcherType.GetMethod("WaitNextAsync", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo close = dispatcherType.GetMethod("Close", BindingFlags.Instance | BindingFlags.Public);

            Task pending = (Task)waitNextAsync.Invoke(dispatcher, new object[] { 5000, CancellationToken.None });
            close.Invoke(dispatcher, new object[] { "手动关闭" });

            Exception inner = WaitTaskException(pending, 2000);
            Assert.That(inner, Is.TypeOf<InvalidOperationException>());
            StringAssert.Contains("手动关闭", inner.Message);
        }

        [Test]
        public void P1_09_ProcessControl_RequestStopAfterDispose_ShouldNotThrow()
        {
            ProcessControl control = new ProcessControl();
            control.Dispose();

            Assert.DoesNotThrow(() => control.RequestStop());
            Assert.DoesNotThrow(() => control.RequestStep());
            Assert.DoesNotThrow(() => control.SetRunning());
            Assert.DoesNotThrow(() => control.SetPaused());
        }

        [Test]
        public void P1_10_InfoClear_ShouldEmptyPendingQueueAndRingBuffer()
        {
            FrmInfo form = new FrmInfo();
            try
            {
                for (int i = 1; i <= 20; i++)
                {
                    form.PrintInfo($"B{i}", FrmInfo.Level.Normal);
                }
                InvokeFormPrivate(form, "InfoFlushTimer_Tick", null, EventArgs.Empty);
                for (int i = 1; i <= 5; i++)
                {
                    form.PrintInfo($"Q{i}", FrmInfo.Level.Normal);
                }
                SetPrivateField(form, "infoAutoScrollPausedByUser", true);

                MethodInfo clearMethod = typeof(FrmInfo).GetMethod("ClearInfoEntries", BindingFlags.Instance | BindingFlags.NonPublic);
                clearMethod.Invoke(form, null);

                int pendingCount = GetPrivateQueueCount(form, "pendingInfoQueue");
                object buffer = GetPrivateField(form, "infoLogBuffer");
                int bufferCount = (int)buffer.GetType().GetProperty("Count").GetValue(buffer);
                bool paused = (bool)GetPrivateField(form, "infoAutoScrollPausedByUser");

                Assert.That(pendingCount, Is.EqualTo(0));
                Assert.That(bufferCount, Is.EqualTo(0));
                Assert.That(paused, Is.False);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Test]
        public void P1_11_MainFormClosing_ShouldDisposeEngineAndReleaseTimer()
        {
            string oldConfigPath = SF.ConfigPath;
            string oldWorkPath = SF.workPath;
            AccountStore oldAccountStore = SF.accountStore;
            IUserContextStore oldUserContextStore = SF.userContextStore;
            IUserLoginStore oldUserLoginStore = SF.userLoginStore;
            UserSession oldUserSession = SF.userSession;
            try
            {
                using (TempDirectoryScope scope = new TempDirectoryScope())
                {
                    SF.ConfigPath = scope.ConfigPath;
                    SF.workPath = scope.WorkPath;
                    EnsureAppConfigCacheInitialized();
                    SF.accountStore = new AccountStore();
                    SF.userContextStore = new UserContextStore(() => SF.userSession, () => SF.SecurityLocked);
                    SF.userLoginStore = new UserLoginStore(SF.accountStore);
                    AccountLoadResult loadResult = SF.accountStore.Load(SF.ConfigPath, "system", "software_123", out string loadError);
                    Assert.That(loadResult, Is.Not.EqualTo(AccountLoadResult.Invalid), loadError);
                    bool loginOk = SF.userLoginStore.TryLogin("system", "software_123", out string loginError);
                    Assert.That(loginOk, Is.True, loginError);

                    FrmMain form = null;
                    try
                    {
                        form = new FrmMain();
                        Proc proc = BuildSingleDelayProc(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2000, "关闭流程测试");
                        form.frmProc.procsList.Clear();
                        form.frmProc.procsList.Add(proc);
                        form.dataRun.Context.Procs = new List<Proc> { proc };
                        form.dataRun.StartProc(proc, 0);
                        Assert.That(
                            WaitSnapshot(form.dataRun, 0, snapshot => snapshot.State == ProcRunState.Running),
                            Is.True);

                        FormClosingEventArgs args = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
                        MethodInfo closingMethod = typeof(FrmMain).GetMethod("FrmMain_FormClosing", BindingFlags.Instance | BindingFlags.NonPublic);
                        Assert.DoesNotThrow(() => closingMethod.Invoke(form, new object[] { form, args }));
                        Assert.That(args.Cancel, Is.False);

                        FieldInfo disposedField = typeof(ProcessEngine).GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic);
                        int disposed = (int)disposedField.GetValue(form.dataRun);
                        Assert.That(disposed, Is.EqualTo(1));

                        FieldInfo timerField = typeof(FrmMain).GetField("snapshotTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                        object snapshotTimer = timerField.GetValue(form);
                        Assert.That(snapshotTimer, Is.Null);
                    }
                    finally
                    {
                        if (form != null)
                        {
                            form.Dispose();
                        }
                    }
                }
            }
            finally
            {
                SF.ConfigPath = oldConfigPath;
                SF.workPath = oldWorkPath;
                SF.accountStore = oldAccountStore;
                SF.userContextStore = oldUserContextStore;
                SF.userLoginStore = oldUserLoginStore;
                SF.SetUserSession(oldUserSession);
                SF.ClearSecurityLock();
            }
        }

        private static Exception WaitTaskException(Task task, int timeoutMs)
        {
            try
            {
                bool completed = task.Wait(timeoutMs);
                if (!completed)
                {
                    return new TimeoutException("等待任务完成超时");
                }
                return null;
            }
            catch (AggregateException ex)
            {
                return ex.InnerExceptions.FirstOrDefault() ?? ex;
            }
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }

        private static void InvokeFormPrivate(FrmInfo form, string methodName, object sender, EventArgs e)
        {
            MethodInfo method = typeof(FrmInfo).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(form, new object[] { sender, e });
        }

        private static int GetPrivateQueueCount(FrmInfo form, string fieldName)
        {
            object queue = GetPrivateField(form, fieldName);
            return (int)queue.GetType().GetProperty("Count").GetValue(queue);
        }

        private static void EnsureAppConfigCacheInitialized()
        {
            if (AppConfigStorage.TryGetCached(out _, out _))
            {
                return;
            }
            FieldInfo cacheField = typeof(AppConfigStorage).GetField("cachedConfig", BindingFlags.Static | BindingFlags.NonPublic);
            cacheField.SetValue(
                null,
                new AppConfig
                {
                    CommMaxMessageQueueSize = AppConfigStorage.DefaultCommMaxMessageQueueSize
                });
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

        private static Proc BuildTwoDelayProc(
            Guid procId,
            Guid stepId,
            Guid op1Id,
            Guid op2Id,
            int delay1Ms,
            int delay2Ms,
            string name)
        {
            Delay op1 = new Delay
            {
                Id = op1Id,
                Num = 0,
                timeMiniSecond = delay1Ms.ToString()
            };
            Delay op2 = new Delay
            {
                Id = op2Id,
                Num = 1,
                timeMiniSecond = delay2Ms.ToString()
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
                        Ops = new List<OperationType> { op1, op2 }
                    }
                }
            };
        }

        private static ProcessEngine CreateEngine(IList<Proc> procs, CollectLogger logger)
        {
            EngineContext context = new EngineContext
            {
                Procs = procs ?? new List<Proc>(),
                ValueStore = new ValueConfigStore(),
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
                Logger = logger
            };
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

        private sealed class CollectLogger : ILogger
        {
            private readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();

            public void Log(string message, LogLevel level)
            {
                messages.Enqueue($"[{level}] {message}");
            }

            public bool Contains(string keyword)
            {
                return messages.Any(item => item != null && item.Contains(keyword));
            }
        }

        private sealed class TempDirectoryScope : IDisposable
        {
            public TempDirectoryScope()
            {
                RootPath = Path.Combine(Path.GetTempPath(), "automation-p1-tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(RootPath);
                ConfigPath = Path.Combine(RootPath, "Config") + Path.DirectorySeparatorChar;
                WorkPath = Path.Combine(ConfigPath, "Work") + Path.DirectorySeparatorChar;
            }

            public string RootPath { get; }
            public string ConfigPath { get; }
            public string WorkPath { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(RootPath))
                    {
                        Directory.Delete(RootPath, true);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
