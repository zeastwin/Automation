using System;
// 模块：核心测试 / 流程引擎观测。
// 职责范围：验证位置变化批量发布、低频性能心跳和快速流程最终状态不丢失。

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class ProcessEngineObservationTests
    {
        [TestMethod]
        public void RunningProcess_WhenPositionDoesNotChange_DoesNotRepeatFastSnapshots()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(
                    new Delay { Id = Guid.NewGuid(), Name = "进入长等待", DelayMs = 0 },
                    new Delay { Id = Guid.NewGuid(), Name = "长等待", DelayMs = 5000 },
                    new EndProcess { Id = Guid.NewGuid() });
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Assert.AreEqual(ProcRunState.Ready, engine.GetSnapshot(0).State);
                    engine.SnapshotThrottleMilliseconds = 20;
                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForPosition(engine, ProcRunState.Running, 0, 1, TimeSpan.FromSeconds(3));
                    Assert.AreEqual(1, engine.ActiveAgentCount);

                    long stableTicks = engine.GetSnapshot(0).UpdateTicks;
                    Thread.Sleep(200);

                    Assert.AreEqual(
                        stableTicks,
                        engine.GetSnapshot(0).UpdateTicks,
                        "位置未变化时不应按20ms刷新周期重复创建快照。");

                    WaitForSnapshotAfter(engine, stableTicks, TimeSpan.FromSeconds(2));
                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                    Assert.AreEqual(ProcTerminationReason.StopRequested,
                        engine.GetSnapshot(0).TerminationReason);
                    WaitForActiveAgentCount(engine, 0, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void FastProcess_WhenCompletingBeforeThrottle_PreservesLifecycleAndFinalSnapshot()
        {
            using (var directory = new TemporaryDirectory())
            using (var readyPublished = new ManualResetEventSlim(false))
            {
                Proc process = CreateProcess(new EndProcess { Id = Guid.NewGuid() });
                var runtime = new PlatformRuntime(directory.FullPath);
                int startedCount = 0;
                int completedCount = 0;
                ProcessRunAuditSnapshot completed = default(ProcessRunAuditSnapshot);
                using (var engine = CreateEngine(runtime, process))
                {
                    engine.SnapshotThrottleMilliseconds = 100;
                    engine.ProcessStarted += _ => Interlocked.Increment(ref startedCount);
                    engine.ProcessCompleted += snapshot =>
                    {
                        completed = snapshot;
                        Interlocked.Increment(ref completedCount);
                    };
                    engine.SnapshotChanged += snapshot =>
                    {
                        if (snapshot?.State == ProcRunState.Ready
                            && snapshot.TerminationReason == ProcTerminationReason.Completed)
                        {
                            readyPublished.Set();
                        }
                    };

                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForState(engine, ProcRunState.Ready, TimeSpan.FromSeconds(3));
                    WaitForCount(ref completedCount, 1, TimeSpan.FromSeconds(3));

                    Assert.AreEqual(1, Volatile.Read(ref startedCount));
                    Assert.AreEqual(1, Volatile.Read(ref completedCount));
                    Assert.AreEqual(1L, completed.OperationCount);
                    Assert.IsTrue(readyPublished.Wait(TimeSpan.FromSeconds(2)),
                        "快速流程的最终就绪快照必须在节流窗口后仍能发布。");
                    WaitForActiveAgentCount(engine, 0, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void SingleOperation_NormalCompletion_EmitsExplicitTargetCompletionEvidence()
        {
            using (var directory = new TemporaryDirectory())
            using (var completedSignal = new ManualResetEventSlim(false))
            {
                Proc process = CreateProcess(new Delay
                {
                    Id = Guid.NewGuid(),
                    Name = "单指令正常完成",
                    DelayMs = 1
                });
                var runtime = new PlatformRuntime(directory.FullPath);
                ProcessRunAuditSnapshot completed = default(ProcessRunAuditSnapshot);
                using (var engine = CreateEngine(runtime, process))
                {
                    engine.ProcessCompleted += snapshot =>
                    {
                        completed = snapshot;
                        completedSignal.Set();
                    };

                    Assert.IsTrue(engine.RunSingleOpOnce(process, 0, 0, 0));
                    Assert.IsTrue(completedSignal.Wait(TimeSpan.FromSeconds(3)));

                    Assert.IsTrue(completed.IsSingleOperation);
                    Assert.IsTrue(completed.SingleOperationTargetCompleted,
                        "只有目标指令正常返回并由工作线程自停时才应形成完成证据。");
                    Assert.AreEqual(ProcTerminationReason.StopRequested,
                        completed.TerminationReason);
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void SingleOperation_ExternalStop_DoesNotPretendTargetCompleted()
        {
            using (var directory = new TemporaryDirectory())
            using (var completedSignal = new ManualResetEventSlim(false))
            {
                Proc process = CreateProcess(new Delay
                {
                    Id = Guid.NewGuid(),
                    Name = "等待外部停止",
                    DelayMs = 30000
                });
                var runtime = new PlatformRuntime(directory.FullPath);
                ProcessRunAuditSnapshot completed = default(ProcessRunAuditSnapshot);
                using (var engine = CreateEngine(runtime, process))
                {
                    engine.ProcessCompleted += snapshot =>
                    {
                        completed = snapshot;
                        completedSignal.Set();
                    };

                    Assert.IsTrue(engine.RunSingleOpOnce(process, 0, 0, 0));
                    WaitForPosition(engine, ProcRunState.Running, 0, 0, TimeSpan.FromSeconds(3));
                    EngineSnapshot active = engine.GetSnapshot(0);
                    ProcessStopRequestResult stop = engine.Stop(
                        0, active.RunId, Guid.NewGuid());

                    Assert.IsTrue(stop.Accepted);
                    Assert.IsTrue(completedSignal.Wait(TimeSpan.FromSeconds(3)));
                    Assert.IsTrue(completed.IsSingleOperation);
                    Assert.IsFalse(completed.SingleOperationTargetCompleted,
                        "外部停止抢先发生时，Delay 的取消返回不得冒充目标指令正常完成。");
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void ExactStop_SameRunStopping_CanReassertAndPreservesAttemptCorrelation()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(new Delay
                {
                    Id = Guid.NewGuid(),
                    Name = "停止重申",
                    DelayMs = 30000
                });
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Guid firstAttempt = Guid.NewGuid();
                    Guid secondAttempt = Guid.NewGuid();
                    ProcessStopRequestResult reasserted = default(ProcessStopRequestResult);
                    var received = new List<ProcessRunStopRequestedSnapshot>();
                    engine.ProcessStopRequested += snapshot =>
                    {
                        lock (received)
                        {
                            received.Add(snapshot);
                        }
                        if (snapshot.AttemptId == firstAttempt)
                        {
                            // 事件在本实例锁内同步发出；Monitor 可重入，可精确验证 Stopping 重申。
                            reasserted = engine.Stop(0, snapshot.RunId, secondAttempt);
                        }
                    };

                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForPosition(engine, ProcRunState.Running, 0, 0, TimeSpan.FromSeconds(3));
                    EngineSnapshot active = engine.GetSnapshot(0);
                    ProcessStopRequestResult accepted = engine.Stop(
                        0, active.RunId, firstAttempt);

                    Assert.AreEqual(ProcessStopRequestStatus.Accepted, accepted.Status);
                    Assert.AreEqual(ProcessStopRequestStatus.Reasserted, reasserted.Status);
                    Assert.AreEqual(secondAttempt, reasserted.AttemptId);
                    lock (received)
                    {
                        Assert.IsTrue(received.Any(item =>
                            item.AttemptId == firstAttempt && !item.IsReassertion));
                        Assert.IsTrue(received.Any(item =>
                            item.AttemptId == secondAttempt && item.IsReassertion));
                    }
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void StartLifecycleEvent_PrecedesFirstPositionSnapshot()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(new Delay
                {
                    Id = Guid.NewGuid(),
                    Name = "启动顺序",
                    DelayMs = 30000
                });
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    engine.SnapshotThrottleMilliseconds = 0;
                    var order = new List<string>();
                    engine.ProcessStarted += _ => order.Add("started");
                    engine.SnapshotChanged += snapshot =>
                    {
                        if (snapshot != null && !snapshot.State.IsInactive())
                        {
                            order.Add("position");
                        }
                    };

                    Assert.IsTrue(engine.StartProc(process, 0));
                    Assert.IsTrue(order.Count >= 2);
                    Assert.AreEqual("started", order[0]);
                    Assert.AreEqual("position", order[1]);
                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void ExactStop_WhenDriverBlocks_CancelsFirstAndDoesNotHoldGlobalPublishLock()
        {
            using (var directory = new TemporaryDirectory())
            using (var stopEntered = new ManualResetEventSlim(false))
            using (var releaseStop = new ManualResetEventSlim(false))
            {
                Proc first = CreateProcess(new Delay
                {
                    Id = Guid.NewGuid(),
                    Name = "阻塞驱动停止",
                    DelayMs = 30000
                });
                Proc second = CreateProcess(new Delay
                {
                    Id = Guid.NewGuid(),
                    Name = "其他流程",
                    DelayMs = 1
                });
                var runtime = new PlatformRuntime(directory.FullPath);
                var motion = new BlockingStopMotionRuntime(stopEntered, releaseStop);
                using (var engine = new ProcessEngine(new EngineContext
                {
                    Procs = new List<Proc> { first, second },
                    Motion = motion,
                    Maintenance = runtime.Maintenance,
                    Safety = runtime.Safety,
                    Readiness = runtime.Readiness,
                    Paths = runtime.Paths
                }))
                {
                    Assert.IsTrue(engine.StartProc(first, 0));
                    WaitForPosition(engine, ProcRunState.Running, 0, 0, TimeSpan.FromSeconds(3));
                    ProcHandle handle = GetCurrentHandle(engine, 0);
                    Assert.IsNotNull(handle);
                    Assert.IsTrue(engine.TryAcquireMotionResource(
                        handle, 0, 0, out string acquireError), acquireError);
                    motion.CancellationObserved = () => handle.CancellationToken.IsCancellationRequested;

                    EngineSnapshot active = engine.GetSnapshot(0);
                    Task<ProcessStopRequestResult> stopTask = Task.Run(() =>
                        engine.Stop(0, active.RunId, Guid.NewGuid()));
                    Assert.IsTrue(stopEntered.Wait(TimeSpan.FromSeconds(2)),
                        "测试驱动没有进入阻塞停止调用。");
                    Assert.IsTrue(motion.WasCancellationRequested,
                        "进入外部驱动 StopOneAxis 前必须已经取消工作线程。");
                    Assert.IsTrue(stopTask.Wait(TimeSpan.FromSeconds(1)),
                        "停止请求的原子接受不得等待阻塞的外部驱动调用。");
                    Assert.AreEqual(ProcessStopRequestStatus.Accepted, stopTask.Result.Status);

                    Task<ProcessStopRequestResult> reassertTask = Task.Run(() =>
                        engine.Stop(0, active.RunId, Guid.NewGuid()));
                    Assert.IsTrue(reassertTask.Wait(TimeSpan.FromSeconds(1)),
                        "同一 Stopping 实例的停止重申不得被前一次驱动调用阻塞。");
                    Assert.AreEqual(ProcessStopRequestStatus.Reasserted,
                        reassertTask.Result.Status);

                    Proc replacement = CreateProcess(new EndProcess { Id = Guid.NewGuid() });
                    Task<bool> publishTask = Task.Run(() =>
                        engine.PublishProc(1, replacement, out _));
                    Assert.IsTrue(publishTask.Wait(TimeSpan.FromSeconds(1)),
                        "一个流程的阻塞驱动停止不得占用全局流程发布锁。");
                    Assert.IsTrue(publishTask.Result);

                    releaseStop.Set();
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        private static ProcessEngine CreateEngine(PlatformRuntime runtime, Proc process)
        {
            return new ProcessEngine(new EngineContext
            {
                Procs = new List<Proc> { process },
                Maintenance = runtime.Maintenance,
                Safety = runtime.Safety,
                Readiness = runtime.Readiness,
                Paths = runtime.Paths
            });
        }

        private static ProcHandle GetCurrentHandle(ProcessEngine engine, int procIndex)
        {
            var agentsField = typeof(ProcessEngine).GetField(
                "agents", BindingFlags.Instance | BindingFlags.NonPublic);
            var agents = agentsField?.GetValue(engine) as ProcAgent[];
            ProcAgent agent = agents != null && procIndex >= 0 && procIndex < agents.Length
                ? agents[procIndex]
                : null;
            if (agent == null)
            {
                return null;
            }
            FieldInfo currentField = typeof(ProcAgent).GetField(
                "current", BindingFlags.Instance | BindingFlags.NonPublic);
            object current = currentField?.GetValue(agent);
            FieldInfo handleField = current?.GetType().GetField(
                "Handle", BindingFlags.Instance | BindingFlags.Public);
            return handleField?.GetValue(current) as ProcHandle;
        }

        private sealed class BlockingStopMotionRuntime : IMotionRuntime
        {
            private readonly ManualResetEventSlim stopEntered;
            private readonly ManualResetEventSlim releaseStop;

            public BlockingStopMotionRuntime(
                ManualResetEventSlim stopEntered,
                ManualResetEventSlim releaseStop)
            {
                this.stopEntered = stopEntered;
                this.releaseStop = releaseStop;
            }

            public Func<bool> CancellationObserved { get; set; }
            public bool WasCancellationRequested { get; private set; }
            public bool IsCardInitialized => true;
            public int StationCount => 0;

            public void StopOneAxis(ushort card, ushort axis, ushort stopMode)
            {
                WasCancellationRequested = CancellationObserved?.Invoke() == true;
                stopEntered.Set();
                releaseStop.Wait(TimeSpan.FromSeconds(3));
            }

            public IDisposable ValidateAxesForCommand(
                IReadOnlyCollection<AxisCommandRequest> requests)
            {
                return new NoopDisposable();
            }

            public void InitCardType() { }
            public bool InitCard() => true;
            public MotionStationResult InitializeStations() => MotionStationResult.Success;
            public MotionStationResult ReleaseStations() => MotionStationResult.Success;
            public MotionStationStatus GetStationStatus(short station) => new MotionStationStatus();
            public MotionStationResult SetStationSpeed(short station, double velocity,
                double acceleration, double deceleration, short axis = -1,
                StationSpeedType type = StationSpeedType.Joint) => MotionStationResult.Success;
            public MotionStationResult HomeStation(short station, short axis = -1,
                bool wait = true, bool group = false) => MotionStationResult.Success;
            public MotionStationResult MoveStationToPoint(short station, DataPos point,
                StationMoveMode mode = StationMoveMode.Go, bool[] disabledAxes = null,
                short tool = 0) => MotionStationResult.Success;
            public MotionStationResult MoveStationOffset(short station, int basePointIndex,
                IReadOnlyList<double> offsets, StationMoveMode mode = StationMoveMode.Go) =>
                MotionStationResult.Success;
            public MotionStationResult MoveStationAxis(short station, short axis, double offset,
                StationAxisMoveMode mode = StationAxisMoveMode.Relative, short tool = 0) =>
                MotionStationResult.Success;
            public MotionStationResult WaitStationMotion(short station, bool isHome = false,
                int axis = -1, int timeoutMs = 120000) => MotionStationResult.Success;
            public MotionStationResult GetStationPosition(short station, short tool,
                out DataPos position)
            {
                position = new DataPos(-1);
                return MotionStationResult.Success;
            }
            public MotionStationResult SaveStationPoint(short station, DataPos point) =>
                MotionStationResult.Success;
            public MotionStationResult CreateStationTray(short station, int trayId,
                int rowCount, int columnCount, IReadOnlyList<DataPos> referencePoints) =>
                MotionStationResult.Success;
            public MotionStationResult MoveStationTrayPoint(short station, int trayId,
                int position, DataPos calculatedPoint) => MotionStationResult.Success;
            public MotionStationResult StopStation(short station, bool emergency = false) =>
                MotionStationResult.Success;
            public MotionStationResult StopAllStations(bool emergency = false) =>
                MotionStationResult.Success;
            public void SettHomeParam(ushort card, ushort axis, ushort dir, ushort speed, ushort homeMode) { }
            public void StartHome(ushort card, ushort axis) { }
            public void CleanPos(ushort card, ushort axis) { }
            public double GetAxisPos(ushort card, ushort axis) => 0;
            public void SetMovParam(ushort card, ushort axis, double minVel, double maxVel,
                double acc, double dec, double stopVel, double sPara, int equiv) { }
            public void Mov(ushort card, ushort axis, double distance, ushort positionMode, bool wait) { }
            public void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request) { }
            public bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem) => true;
            public void StopCoordinatedLinear(ushort card, ushort coordinateSystem, ushort stopMode) { }
            public void Jog(ushort card, ushort axis, ushort direction) { }
            public void StopConnect() { }
            public bool HomeStatus(ushort card, ushort axis) => true;
            public bool GetInPos(ushort card, ushort axis) => true;
            public bool GetAxisSevon(ushort card, ushort axis) => true;
            public void SetAxisSevon(ushort card, ushort axis, bool isSevon) { }
            public void DownLoadConfig() { }
            public void SetAllAxisSevonOn() { }
            public void SetAllAxisEquiv() { }
            public void ResetAxisAlarm(ushort card, ushort axis) { }
            public double GetAxisCurSpeed(ushort card, ushort axis) => 0;
            public uint GetAxisIoStatus(ushort card, ushort axis) => 0;
            public ushort GetAxisAlarmCode(ushort card, ushort axis) => 0;

            private sealed class NoopDisposable : IDisposable
            {
                public void Dispose() { }
            }
        }

        private static Proc CreateProcess(params OperationType[] operations)
        {
            var process = new Proc
            {
                head = new ProcHead { Name = "流程观测回归" }
            };
            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = "观测步骤"
            };
            step.Ops.AddRange(operations);
            process.steps.Add(step);
            return process;
        }

        private static void WaitForState(ProcessEngine engine, ProcRunState state, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.GetSnapshot(0).State == state)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"等待流程状态超时：{state}。");
        }

        private static void WaitForPosition(
            ProcessEngine engine,
            ProcRunState state,
            int stepIndex,
            int operationIndex,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                EngineSnapshot snapshot = engine.GetSnapshot(0);
                if (snapshot.State == state
                    && snapshot.StepIndex == stepIndex
                    && snapshot.OpIndex == operationIndex)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"等待流程位置超时：{state} {stepIndex}-{operationIndex}。");
        }

        private static void WaitForSnapshotAfter(ProcessEngine engine, long updateTicks, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.GetSnapshot(0).UpdateTicks > updateTicks)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail("等待性能心跳快照超时。");
        }

        private static void WaitForActiveAgentCount(ProcessEngine engine, int expected, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.ActiveAgentCount == expected)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"等待活动流程数量超时：{expected}；当前：{engine.ActiveAgentCount}。");
        }

        private static void WaitForCount(ref int value, int expected, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Volatile.Read(ref value) == expected)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"等待事件数量超时：{expected}；当前：{Volatile.Read(ref value)}。");
        }
    }
}
