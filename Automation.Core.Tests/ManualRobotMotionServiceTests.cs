using Automation.DeviceSdk;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Automation.Core.Tests
{
    [TestClass]
    public class ManualRobotMotionServiceTests
    {
        [TestMethod]
        public void MoveStationAxis_通过门禁后按三点零相对语义下发并释放整站资源()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });

                Assert.IsTrue(service.TryMoveStationAxis(2, 4, -10d, 30d, true));
                Assert.AreEqual(2, motion.SpeedCalls.Count);
                Assert.AreEqual((short)4, motion.Axis);
                Assert.AreEqual(-10d, motion.Offset);
                Assert.AreEqual(StationAxisMoveMode.Relative, motion.AxisMode);
                Assert.AreEqual(1, motion.WaitCalls);
                Assert.IsTrue(engine.TryAcquireManualStationMotionResource(2, out string error), error);
                engine.ReleaseManualStationMotionResource(2);
            }
        }

        [TestMethod]
        public void MoveStationAxis_复位未完成时拒绝且不触发运行时副作用()
        {
            var runtime = new PlatformRuntime();
            Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                ValueConfigStore.SystemValueStartIndex,
                "复位状态", "double", "0", string.Empty));
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });

                Assert.IsFalse(service.TryMoveStationAxis(0, 0, 1d, 20d, true));
                Assert.AreEqual(0, motion.SpeedCalls.Count);
                Assert.AreEqual(0, motion.MoveAxisCalls);
                Assert.AreEqual(0, motion.StopCalls);
            }
        }

        [TestMethod]
        public void MoveStationAxis_运动配置故障时返回原始原因且无设备副作用()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            runtime.Readiness.MotionConfigFaulted = true;
            runtime.Readiness.MotionConfigFaultReason = "工站引用了不存在的卡轴";
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                string rejected = null;
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });
                service.CommandRejected += (sender, args) => rejected = args.Message;

                Assert.IsFalse(service.TryMoveStationAxis(0, 0, 1d, 20d, true));
                Assert.AreEqual(0, motion.SpeedCalls.Count);
                Assert.AreEqual(0, motion.MoveAxisCalls);
                StringAssert.Contains(rejected, "工站引用了不存在的卡轴");
            }
        }

        [TestMethod]
        public void MoveStationAxis_连续模式保持整站资源直到明确停止()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });

                Assert.IsTrue(service.TryMoveStationAxis(
                    1, 2, 1000d, 30d, false, 0, true));
                Assert.AreEqual(0, motion.WaitCalls);
                Assert.IsFalse(engine.TryAcquireManualStationMotionResource(1, out _));

                Assert.IsTrue(service.TryStopStation(1));
                Assert.IsTrue(engine.TryAcquireManualStationMotionResource(1, out string error), error);
                engine.ReleaseManualStationMotionResource(1);
            }
        }

        [TestMethod]
        public void StopStation_停止失败时锁定安全并保留整站资源()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime
            {
                StopResult = MotionStationResult.CommandRejected,
                LastError = "控制器拒绝停止"
            };
            string securityError = null;
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                Assert.IsTrue(engine.TryAcquireManualStationMotionResource(1, out string acquireError), acquireError);
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false,
                    message => securityError = message);

                Assert.IsFalse(service.TryStopStation(1));
                StringAssert.Contains(securityError, "控制器拒绝停止");
                Assert.IsFalse(engine.TryAcquireManualStationMotionResource(1, out _));

                motion.StopResult = MotionStationResult.Success;
                Assert.IsTrue(service.TryStopStation(1));
            }
        }

        [TestMethod]
        public void TeachStationPoint_在整站资源内读取六维位置并同步保存()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime
            {
                CurrentPosition = new DataPos(-1)
                {
                    X = 1,
                    Y = 2,
                    Z = 3,
                    U = 4,
                    V = 5,
                    W = 6,
                    Pose = new short[] { 1, 1, 0, 1 }
                }
            };
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });
                var configured = new DataPos(42) { Name = "取料点", IsTaught = false };

                Assert.IsTrue(service.TryTeachStationPoint(0, configured, out DataPos taught));
                Assert.AreEqual(42, taught.Index);
                Assert.AreEqual("取料点", taught.Name);
                CollectionAssert.AreEqual(
                    new[] { 1d, 2d, 3d, 4d, 5d, 6d },
                    motion.SavedPoint.GetAllValues());
                Assert.AreEqual(true, motion.SavedPoint.IsTaught);
                CollectionAssert.AreEqual(new short[] { 1, 1, 0, 1 }, motion.SavedPoint.Pose);
                Assert.IsTrue(engine.TryAcquireManualStationMotionResource(0, out string error), error);
                engine.ReleaseManualStationMotionResource(0);
            }
        }

        [TestMethod]
        public void SaveStationPoint_把已编辑点位原样同步并释放整站资源()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });
                var edited = new DataPos(18)
                {
                    Name = "放料点",
                    IsTaught = true,
                    X = 11,
                    Y = 12,
                    Z = 13,
                    U = 14,
                    V = 15,
                    W = 16
                };

                Assert.IsTrue(service.TrySaveStationPoint(2, edited));
                Assert.AreEqual(1, motion.SaveCalls);
                Assert.AreEqual(18, motion.SavedPoint.Index);
                Assert.AreEqual("放料点", motion.SavedPoint.Name);
                CollectionAssert.AreEqual(edited.GetAllValues(), motion.SavedPoint.GetAllValues());
                Assert.IsTrue(engine.TryAcquireManualStationMotionResource(2, out string error), error);
                engine.ReleaseManualStationMotionResource(2);
            }
        }

        [TestMethod]
        public void MoveStationToPoint_轴工站允许使用二百到三百九十九点位()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });
                var point = new DataPos(250) { Name = "轴工站扩展点", IsTaught = true };

                Assert.IsTrue(service.TryMoveStationToPoint(0, point, 20d, wait: true));
                Assert.AreEqual(1, motion.MovePointCalls);
            }
        }

        [TestMethod]
        public void MoveStationToPoint_机器人工站仍拒绝二百及以上点位()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });
                var point = new DataPos(200) { Name = "越界机器人点", IsTaught = true };

                Assert.IsFalse(service.TryMoveStationToPoint(1, point, 20d, wait: true));
                Assert.AreEqual(0, motion.MovePointCalls);
            }
        }

        [TestMethod]
        public void MoveStationToPoint_轴工站异步动作占用全部绑定轴且停止后整组释放()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            using (var waitEntered = new ManualResetEventSlim(false))
            using (var releaseWait = new ManualResetEventSlim(false))
            using (var waitReturned = new ManualResetEventSlim(false))
            {
                var motion = new RecordingMotionRuntime
                {
                    WaitEntered = waitEntered,
                    ReleaseWait = releaseWait,
                    WaitReturned = waitReturned
                };
                using (ProcessEngine engine = CreateEngine(runtime, motion))
                {
                    var service = new ManualMotionService(
                        motion, engine, runtime.Stores.Values, () => false, _ => { });
                    var point = new DataPos(8) { Name = "轴工站GO点", IsTaught = true };

                    Assert.IsTrue(service.TryMoveStationToPoint(
                        0, point, 30d, StationMoveMode.Go, false));
                    Assert.IsTrue(waitEntered.Wait(TimeSpan.FromSeconds(1)), "异步工站监控未启动。");
                    Assert.IsFalse(engine.TryAcquireManualStationMotionResource(0, out _));
                    Assert.IsFalse(engine.TryAcquireManualMotionResource(0, 0, out _));
                    Assert.IsFalse(engine.TryAcquireManualMotionResource(0, 1, out _));

                    Assert.IsTrue(service.TryStopStation(0));
                    Assert.IsTrue(engine.TryAcquireManualStationMotionResource(
                        0, out string stationError), stationError);
                    Assert.IsTrue(engine.TryAcquireManualMotionResource(
                        0, 0, out string axis0Error), axis0Error);
                    Assert.IsTrue(engine.TryAcquireManualMotionResource(
                        0, 1, out string axis1Error), axis1Error);
                    engine.ReleaseManualStationMotionResource(0);
                    engine.ReleaseManualMotionResource(0, 0);
                    engine.ReleaseManualMotionResource(0, 1);

                    releaseWait.Set();
                    Assert.IsTrue(waitReturned.Wait(TimeSpan.FromSeconds(1)), "旧工站监控未退出等待。");
                }
            }
        }

        [TestMethod]
        public void MoveStationAxis_轴工站单通道动作仍占用全站绑定轴()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });

                Assert.IsTrue(service.TryMoveStationAxis(
                    0, 1, 2.5d, 25d, false, 0, true));
                Assert.AreEqual(1, motion.MoveAxisCalls);
                Assert.IsFalse(engine.TryAcquireManualMotionResource(0, 0, out _));
                Assert.IsFalse(engine.TryAcquireManualMotionResource(0, 1, out _));

                Assert.IsTrue(service.TryStopStation(0));
                Assert.IsTrue(engine.TryAcquireManualMotionResource(
                    0, 0, out string axis0Error), axis0Error);
                Assert.IsTrue(engine.TryAcquireManualMotionResource(
                    0, 1, out string axis1Error), axis1Error);
                engine.ReleaseManualMotionResource(0, 0);
                engine.ReleaseManualMotionResource(0, 1);
            }
        }

        [TestMethod]
        public void HomeStation_轴工站按通道调用并在完成后释放整组资源()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });

                Assert.IsTrue(service.TryHomeStation(0, 1, false, true));
                Assert.AreEqual(1, motion.HomeCalls);
                Assert.AreEqual((short)1, motion.LastHomeAxis);
                Assert.IsTrue(engine.TryAcquireManualMotionResource(
                    0, 0, out string axis0Error), axis0Error);
                Assert.IsTrue(engine.TryAcquireManualMotionResource(
                    0, 1, out string axis1Error), axis1Error);
                engine.ReleaseManualMotionResource(0, 0);
                engine.ReleaseManualMotionResource(0, 1);
            }
        }

        [TestMethod]
        public void MoveStationToPoint_轴工站命令失败且停止成功时释放整组资源()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime
            {
                MovePointResult = MotionStationResult.CommandRejected
            };
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });
                var point = new DataPos(9) { Name = "失败点", IsTaught = true };

                Assert.IsFalse(service.TryMoveStationToPoint(
                    0, point, 30d, StationMoveMode.Go, false));
                Assert.AreEqual(1, motion.StopCalls);
                Assert.IsTrue(engine.TryAcquireManualStationMotionResource(
                    0, out string stationError), stationError);
                Assert.IsTrue(engine.TryAcquireManualMotionResource(
                    0, 0, out string axis0Error), axis0Error);
                Assert.IsTrue(engine.TryAcquireManualMotionResource(
                    0, 1, out string axis1Error), axis1Error);
                engine.ReleaseManualStationMotionResource(0);
                engine.ReleaseManualMotionResource(0, 0);
                engine.ReleaseManualMotionResource(0, 1);
            }
        }

        [TestMethod]
        public void ManualStationResources_Move坐标系冲突时原子失败且Go仍可占用独立轴()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                engine.Context.Stations[1] = CreateAxisStation("第二轴工站", 3, 0, 2);

                Assert.IsTrue(engine.TryAcquireManualStationMotionResources(
                    0, true, out IDisposable firstMove, out string firstError), firstError);
                using (firstMove)
                {
                    Assert.IsFalse(engine.TryAcquireManualStationMotionResources(
                        1, true, out IDisposable failedMove, out string coordinateError));
                    Assert.IsNull(failedMove);
                    StringAssert.Contains(coordinateError, "坐标系3");

                    Assert.IsTrue(engine.TryAcquireManualStationMotionResources(
                        1, false, out IDisposable secondGo, out string goError), goError);
                    secondGo.Dispose();
                }

                Assert.IsTrue(engine.TryAcquireManualStationMotionResources(
                    1, true, out IDisposable releasedMove, out string releasedError), releasedError);
                releasedMove.Dispose();
            }
        }

        [TestMethod]
        public void ManualStationResources_配置维护期间拒绝且不留下部分占用()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime();
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                Assert.IsTrue(runtime.Maintenance.TryBegin(
                    "测试运动配置提交", out IDisposable maintenance, out string beginError), beginError);
                using (maintenance)
                {
                    Assert.IsFalse(engine.TryAcquireManualStationMotionResources(
                        0, false, out IDisposable rejected, out string error));
                    Assert.IsNull(rejected);
                    StringAssert.Contains(error, "配置维护");
                }

                Assert.IsTrue(engine.TryAcquireManualStationMotionResource(
                    0, out string stationError), stationError);
                Assert.IsTrue(engine.TryAcquireManualMotionResource(
                    0, 0, out string axis0Error), axis0Error);
                Assert.IsTrue(engine.TryAcquireManualMotionResource(
                    0, 1, out string axis1Error), axis1Error);
                engine.ReleaseManualStationMotionResource(0);
                engine.ReleaseManualMotionResource(0, 0);
                engine.ReleaseManualMotionResource(0, 1);
            }
        }

        [TestMethod]
        public void TeachAndSaveStationPoint_轴工站按四百点容量校验()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime
            {
                CurrentPosition = new DataPos(-1) { X = 12, Y = 34 }
            };
            using (ProcessEngine engine = CreateEngine(runtime, motion))
            {
                var service = new ManualMotionService(
                    motion, engine, runtime.Stores.Values, () => false, _ => { });
                var point = new DataPos(399) { Name = "轴工站末点", IsTaught = true };

                Assert.IsTrue(service.TryTeachStationPoint(0, point, out DataPos taught));
                Assert.AreEqual(399, taught.Index);
                Assert.AreEqual(12d, taught.X);
                Assert.IsTrue(service.TrySaveStationPoint(0, taught));
                Assert.AreEqual(2, motion.SaveCalls);
            }
        }

        [TestMethod]
        public void StopStation_主动停止后旧监控结果不得释放后续占用或触发安全锁()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            using (var waitEntered = new ManualResetEventSlim(false))
            using (var releaseWait = new ManualResetEventSlim(false))
            using (var waitReturned = new ManualResetEventSlim(false))
            {
                var motion = new RecordingMotionRuntime
                {
                    WaitEntered = waitEntered,
                    ReleaseWait = releaseWait,
                    WaitReturned = waitReturned,
                    WaitResult = MotionStationResult.CommandRejected
                };
                string securityError = null;
                using (ProcessEngine engine = CreateEngine(runtime, motion))
                {
                    var service = new ManualMotionService(
                        motion, engine, runtime.Stores.Values, () => false,
                        message => securityError = message);

                    Assert.IsTrue(service.TryMoveStationAxis(1, 0, 10d, 20d));
                    Assert.IsTrue(waitEntered.Wait(TimeSpan.FromSeconds(1)), "异步运动监控未启动。");
                    Assert.IsTrue(service.TryStopStation(1));

                    Assert.IsTrue(engine.TryAcquireManualStationMotionResource(
                        1, out string acquireError), acquireError);
                    releaseWait.Set();
                    Assert.IsTrue(waitReturned.Wait(TimeSpan.FromSeconds(1)), "旧监控未退出等待。");
                    Thread.Sleep(50);

                    Assert.IsNull(securityError, "主动停止后的旧监控结果不应再次报警。");
                    Assert.IsFalse(engine.TryAcquireManualStationMotionResource(1, out _),
                        "旧监控不得释放后续占用的整站资源。");
                    engine.ReleaseManualStationMotionResource(1);
                }
            }
        }

        [TestMethod]
        public void AxisMonitor_停止旧动作并启动新Jog后旧完成结果不得释放新资源()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime { AxisInPosResult = true };
            var monitorContext = new QueuedSynchronizationContext();
            SynchronizationContext previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(monitorContext);
            try
            {
                string securityError = null;
                using (ProcessEngine engine = CreateEngine(runtime, motion))
                {
                    var service = new ManualMotionService(
                        motion, engine, runtime.Stores.Values, () => false,
                        message => securityError = message);
                    service.ConfigureAxis(0, 1,
                        new ManualMotionParameters(0, 10, 10, 10, 0, 0, 1));

                    Assert.IsTrue(service.TryMove(0, 1, 5, 0, false));
                    Assert.AreEqual(1, monitorContext.PendingCount,
                        "旧动作监控应等待独立的异步观察回合。");
                    Assert.IsTrue(service.TryStop(0, 1, 0));
                    Assert.IsTrue(service.TryJog(0, 1, 1));

                    monitorContext.RunNext();

                    Assert.IsNull(securityError);
                    Assert.AreEqual(1, motion.AxisStopCalls,
                        "旧完成结果不得再次停止新Jog。");
                    Assert.IsFalse(engine.TryAcquireManualMotionResource(0, 1, out _),
                        "旧完成结果不得释放新Jog的轴资源。");
                    Assert.IsTrue(service.TryStop(0, 1, 0));
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        [TestMethod]
        public void AxisMonitor_停止旧动作并启动新Jog后旧超时不得误停新动作()
        {
            PlatformRuntime runtime = CreateReadyRuntime();
            var motion = new RecordingMotionRuntime
            {
                AxisInPosException = new TimeoutException("模拟旧动作监控超时")
            };
            var monitorContext = new QueuedSynchronizationContext();
            SynchronizationContext previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(monitorContext);
            try
            {
                string securityError = null;
                using (ProcessEngine engine = CreateEngine(runtime, motion))
                {
                    var service = new ManualMotionService(
                        motion, engine, runtime.Stores.Values, () => false,
                        message => securityError = message);
                    service.ConfigureAxis(0, 1,
                        new ManualMotionParameters(0, 10, 10, 10, 0, 0, 1));

                    Assert.IsTrue(service.TryMove(0, 1, 5, 0, false));
                    Assert.AreEqual(1, monitorContext.PendingCount,
                        "旧动作监控应等待独立的异步观察回合。");
                    Assert.IsTrue(service.TryStop(0, 1, 0));
                    Assert.IsTrue(service.TryJog(0, 1, 1));

                    monitorContext.RunNext();

                    Assert.IsNull(securityError, "失效的旧监控不得触发安全锁。");
                    Assert.AreEqual(1, motion.AxisStopCalls,
                        "失效的旧监控不得停止新Jog。");
                    Assert.IsFalse(engine.TryAcquireManualMotionResource(0, 1, out _),
                        "失效的旧监控不得释放新Jog的轴资源。");
                    Assert.IsTrue(service.TryStop(0, 1, 0));
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        private static PlatformRuntime CreateReadyRuntime()
        {
            var runtime = new PlatformRuntime();
            Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                ValueConfigStore.SystemValueStartIndex,
                "复位状态", "double", ((double)ResetStatus.ResetCompleted).ToString(), string.Empty));
            return runtime;
        }

        private static ProcessEngine CreateEngine(PlatformRuntime runtime, IMotionRuntime motion)
        {
            return new ProcessEngine(new EngineContext
            {
                Procs = new List<Proc>(),
                ValueStore = runtime.Stores.Values,
                Motion = motion,
                Stations = new List<DataStation>
                {
                    CreateAxisStation("轴工站", 3, 0, 0, 1),
                    new DataStation(true) { Name = "机器人1", Type = StationType.Epson },
                    new DataStation(true) { Name = "机器人2", Type = StationType.Inovance }
                },
                Maintenance = runtime.Maintenance,
                Safety = runtime.Safety,
                Readiness = runtime.Readiness,
                Paths = runtime.Paths
            });
        }

        private static DataStation CreateAxisStation(
            string name,
            ushort coordinateSystem,
            ushort card,
            params ushort[] axes)
        {
            var station = new DataStation(true)
            {
                Name = name,
                Type = StationType.Axis,
                CoordinateSystem = coordinateSystem
            };
            for (int i = 0; i < axes.Length && i < station.dataAxis.axisConfigs.Count; i++)
            {
                AxisConfig configuration = station.dataAxis.axisConfigs[i];
                configuration.CardNum = card.ToString();
                configuration.AxisName = $"轴{axes[i]}";
                configuration.axis = new Axis
                {
                    AxisNum = axes[i],
                    AxisName = configuration.AxisName
                };
            }
            return station;
        }

        private sealed class QueuedSynchronizationContext : SynchronizationContext
        {
            private readonly object syncRoot = new object();
            private readonly Queue<Action> callbacks = new Queue<Action>();

            public int PendingCount
            {
                get
                {
                    lock (syncRoot)
                    {
                        return callbacks.Count;
                    }
                }
            }

            public override void Post(SendOrPostCallback callback, object state)
            {
                lock (syncRoot)
                {
                    callbacks.Enqueue(() => callback(state));
                }
            }

            public void RunNext()
            {
                Action callback;
                lock (syncRoot)
                {
                    if (callbacks.Count == 0)
                    {
                        throw new InvalidOperationException("没有待执行的异步监控回调。");
                    }
                    callback = callbacks.Dequeue();
                }
                callback();
            }
        }

        private sealed class RecordingMotionRuntime : IMotionRuntime
        {
            private sealed class EmptyLease : IDisposable
            {
                public void Dispose() { }
            }

            public bool IsCardInitialized => true;
            public int StationCount => 3;
            public List<(short Axis, StationSpeedType Type)> SpeedCalls { get; } =
                new List<(short Axis, StationSpeedType Type)>();
            public short Axis { get; private set; }
            public double Offset { get; private set; }
            public StationAxisMoveMode AxisMode { get; private set; }
            public int MoveAxisCalls { get; private set; }
            public int MovePointCalls { get; private set; }
            public MotionStationResult MovePointResult { get; set; } = MotionStationResult.Success;
            public int HomeCalls { get; private set; }
            public short LastHomeAxis { get; private set; }
            public int WaitCalls { get; private set; }
            public int StopCalls { get; private set; }
            public MotionStationResult StopResult { get; set; } = MotionStationResult.Success;
            public MotionStationResult WaitResult { get; set; } = MotionStationResult.Success;
            public string LastError { get; set; } = string.Empty;
            public DataPos CurrentPosition { get; set; } = new DataPos(-1);
            public DataPos SavedPoint { get; private set; }
            public int SaveCalls { get; private set; }
            public ManualResetEventSlim WaitEntered { get; set; }
            public ManualResetEventSlim ReleaseWait { get; set; }
            public ManualResetEventSlim WaitReturned { get; set; }
            public bool AxisInPosResult { get; set; } = true;
            public Exception AxisInPosException { get; set; }
            public int AxisStopCalls { get; private set; }
            public int JogCalls { get; private set; }

            public MotionStationResult SetStationSpeed(short station, double velocity,
                double acceleration, double deceleration, short axis = -1,
                StationSpeedType type = StationSpeedType.Joint)
            {
                SpeedCalls.Add((axis, type));
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationAxis(short station, short axis, double offset,
                StationAxisMoveMode mode = StationAxisMoveMode.Relative, short tool = 0)
            {
                Axis = axis;
                Offset = offset;
                AxisMode = mode;
                MoveAxisCalls++;
                return MotionStationResult.Success;
            }

            public MotionStationResult WaitStationMotion(short station, bool isHome = false,
                int axis = -1, int timeoutMs = 120000)
            {
                WaitCalls++;
                WaitEntered?.Set();
                ReleaseWait?.Wait(TimeSpan.FromSeconds(1));
                WaitReturned?.Set();
                return WaitResult;
            }

            public MotionStationResult StopStation(short station, bool emergency = false)
            {
                StopCalls++;
                return StopResult;
            }

            public MotionStationStatus GetStationStatus(short station) => new MotionStationStatus
            {
                State = MotionStationState.Idle,
                LastError = LastError
            };

            public IDisposable ValidateAxesForCommand(IReadOnlyCollection<AxisCommandRequest> requests) =>
                new EmptyLease();
            public void InitCardType() { }
            public bool InitCard() => true;
            public MotionStationResult InitializeStations() => MotionStationResult.Success;
            public MotionStationResult ReleaseStations() => MotionStationResult.Success;
            public MotionStationResult HomeStation(short station, short axis = -1,
                bool wait = true, bool group = false)
            {
                HomeCalls++;
                LastHomeAxis = axis;
                return MotionStationResult.Success;
            }
            public MotionStationResult MoveStationToPoint(short station, DataPos point,
                StationMoveMode mode = StationMoveMode.Go, bool[] disabledAxes = null,
                short tool = 0)
            {
                MovePointCalls++;
                return MovePointResult;
            }
            public MotionStationResult MoveStationOffset(short station, int basePointIndex,
                IReadOnlyList<double> offsets, StationMoveMode mode = StationMoveMode.Go) =>
                MotionStationResult.Success;
            public MotionStationResult GetStationPosition(short station, short tool,
                out DataPos position)
            {
                position = (DataPos)CurrentPosition.Clone();
                return MotionStationResult.Success;
            }
            public MotionStationResult SaveStationPoint(short station, DataPos point)
            {
                SaveCalls++;
                SavedPoint = (DataPos)point.Clone();
                return MotionStationResult.Success;
            }
            public MotionStationResult CreateStationTray(short station, int trayId,
                int rowCount, int columnCount, IReadOnlyList<DataPos> referencePoints) =>
                MotionStationResult.Success;
            public MotionStationResult MoveStationTrayPoint(short station, int trayId,
                int position, DataPos calculatedPoint) => MotionStationResult.Success;
            public MotionStationResult ClearStationContinuousPath(short station) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousLine(short station, DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousArc(short station, DataPos start, DataPos middle, DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousArcCenterRadius(short station, DataPos target, DataPos center, double radius, int circle, bool counterClockwise) => MotionStationResult.Success;
            public MotionStationResult StartStationContinuousMove(short station) => MotionStationResult.Success;
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
            public void MoveContinuousPath(ContinuousPathMoveRequest request) { }
            public bool IsContinuousPathDone(ushort card, ushort coordinateSystem) => true;
            public void StopContinuousPath(ushort card, ushort coordinateSystem, ushort stopMode) { }
            public void Jog(ushort card, ushort axis, ushort direction)
            {
                JogCalls++;
            }
            public void StopOneAxis(ushort card, ushort axis, ushort stopMode)
            {
                AxisStopCalls++;
            }
            public void StopConnect() { }
            public bool HomeStatus(ushort card, ushort axis) => true;
            public bool GetInPos(ushort card, ushort axis)
            {
                if (AxisInPosException != null)
                {
                    throw AxisInPosException;
                }
                return AxisInPosResult;
            }
            public bool GetAxisSevon(ushort card, ushort axis) => true;
            public void SetAxisSevon(ushort card, ushort axis, bool isSevon) { }
            public void DownLoadConfig() { }
            public void SetAllAxisSevonOn() { }
            public void SetAllAxisEquiv() { }
            public void ResetAxisAlarm(ushort card, ushort axis) { }
            public double GetAxisCurSpeed(ushort card, ushort axis) => 0;
            public uint GetAxisIoStatus(ushort card, ushort axis) => 0;
            public ushort GetAxisAlarmCode(ushort card, ushort axis) => 0;
        }
    }
}
