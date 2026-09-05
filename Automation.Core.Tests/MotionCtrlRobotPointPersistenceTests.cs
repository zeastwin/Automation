using System;
// 模块：核心测试 / 机器人点位持久化。
// 职责范围：固化控制器点表与平台点位的提交、补偿回滚和不一致状态语义。

using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class MotionCtrlRobotPointPersistenceTests
    {
        [TestMethod]
        public void SaveStationPoint_DeviceAndPersistenceSucceed_CommitsCurrentContract()
        {
            using (var directory = new TemporaryDirectory())
            {
                PlatformRuntime runtime = CreateRuntimeWithStation(directory.FullPath, out DataPos configuredPoint);
                var stationRuntime = new ScriptedMotionStation(MotionStationResult.Success);
                MotionCtrl motion = CreateMotion(runtime, stationRuntime, new CapturingLogger());
                DataPos candidate = CloneWithCoordinates(configuredPoint, 11, 12, 13, 14, 15, 16);

                MotionStationResult result = motion.SaveStationPoint(0, candidate);

                Assert.AreEqual(MotionStationResult.Success, result);
                Assert.AreEqual(1, stationRuntime.SavedPoints.Count);
                AssertCoordinates(configuredPoint, 11, 12, 13, 14, 15, 16);

                var reloaded = new StationDefinitionStore();
                Assert.IsTrue(reloaded.Load(runtime.Paths.ConfigPath, out string loadError), loadError);
                DataPos persisted = reloaded.Items[0].ListDataPos[configuredPoint.Index];
                AssertCoordinates(persisted, 11, 12, 13, 14, 15, 16);
            }
        }

        [TestMethod]
        public void SaveStationPoint_UncertainDeviceFailure_RestoresPreviousControllerPoint()
        {
            using (var directory = new TemporaryDirectory())
            {
                PlatformRuntime runtime = CreateRuntimeWithStation(directory.FullPath, out DataPos configuredPoint);
                var stationRuntime = new ScriptedMotionStation(
                    MotionStationResult.ReceiveFailed,
                    MotionStationResult.Success);
                MotionCtrl motion = CreateMotion(runtime, stationRuntime, new CapturingLogger());
                DataPos candidate = CloneWithCoordinates(configuredPoint, 21, 22, 23, 24, 25, 26);

                MotionStationResult result = motion.SaveStationPoint(0, candidate);

                Assert.AreEqual(MotionStationResult.ReceiveFailed, result);
                Assert.AreEqual(2, stationRuntime.SavedPoints.Count);
                AssertCoordinates(stationRuntime.SavedPoints[0], 21, 22, 23, 24, 25, 26);
                AssertCoordinates(stationRuntime.SavedPoints[1], 1, 2, 3, 4, 5, 6);
                AssertCoordinates(configuredPoint, 1, 2, 3, 4, 5, 6);

                var reloaded = new StationDefinitionStore();
                Assert.IsTrue(reloaded.Load(runtime.Paths.ConfigPath, out string loadError), loadError);
                AssertCoordinates(
                    reloaded.Items[0].ListDataPos[configuredPoint.Index],
                    1, 2, 3, 4, 5, 6);
            }
        }

        [TestMethod]
        public void SaveStationPoint_CompensationFailure_ReturnsExplicitInconsistentState()
        {
            using (var directory = new TemporaryDirectory())
            {
                PlatformRuntime runtime = CreateRuntimeWithStation(directory.FullPath, out DataPos configuredPoint);
                var stationRuntime = new ScriptedMotionStation(
                    MotionStationResult.Timeout,
                    MotionStationResult.CommandRejected);
                var logger = new CapturingLogger();
                MotionCtrl motion = CreateMotion(runtime, stationRuntime, logger);
                DataPos candidate = CloneWithCoordinates(configuredPoint, 31, 32, 33, 34, 35, 36);

                MotionStationResult result = motion.SaveStationPoint(0, candidate);

                Assert.AreEqual(MotionStationResult.InconsistentState, result);
                Assert.AreEqual(2, stationRuntime.SavedPoints.Count);
                AssertCoordinates(stationRuntime.SavedPoints[0], 31, 32, 33, 34, 35, 36);
                AssertCoordinates(stationRuntime.SavedPoints[1], 1, 2, 3, 4, 5, 6);
                AssertCoordinates(configuredPoint, 1, 2, 3, 4, 5, 6);
                StringAssert.Contains(logger.LastMessage, "补偿回滚失败");
            }
        }

        [TestMethod]
        public void SaveStationPoint_DeviceWriteDoesNotHoldConfigurationLock()
        {
            using (var directory = new TemporaryDirectory())
            using (var deviceWriteEntered = new ManualResetEventSlim(false))
            using (var allowDeviceWrite = new ManualResetEventSlim(false))
            using (var configurationLockEntered = new ManualResetEventSlim(false))
            using (var allowConfigurationLockExit = new ManualResetEventSlim(false))
            {
                PlatformRuntime runtime = CreateRuntimeWithStation(
                    directory.FullPath,
                    out DataPos configuredPoint);
                DataStation configuration = runtime.Stores.Stations.Items[0];
                var stationRuntime = new ScriptedMotionStation(MotionStationResult.Success)
                {
                    BeforeSave = () =>
                    {
                        deviceWriteEntered.Set();
                        allowDeviceWrite.Wait(5000);
                    }
                };
                MotionCtrl motion = CreateMotion(runtime, stationRuntime, new CapturingLogger());
                DataPos candidate = CloneWithCoordinates(
                    configuredPoint,
                    41, 42, 43, 44, 45, 46);
                Task<MotionStationResult> saveTask = null;
                Task configurationTask = null;
                try
                {
                    saveTask = Task.Run(() => motion.SaveStationPoint(0, candidate));
                    Assert.IsTrue(deviceWriteEntered.Wait(1000), "设备写入阶段未按预期进入。");

                    configurationTask = Task.Run(() =>
                    {
                        lock (configuration)
                        {
                            configurationLockEntered.Set();
                            allowConfigurationLockExit.Wait(5000);
                        }
                    });

                    Assert.IsTrue(
                        configurationLockEntered.Wait(1000),
                        "设备写入期间仍持有配置锁，存在 configuration→station 锁嵌套。");
                }
                finally
                {
                    allowDeviceWrite.Set();
                    allowConfigurationLockExit.Set();
                    configurationTask?.Wait(2000);
                }

                Assert.IsNotNull(saveTask);
                Assert.IsTrue(saveTask.Wait(2000), "点位保存未能结束。");
                Assert.AreEqual(MotionStationResult.Success, saveTask.Result);
            }
        }

        [TestMethod]
        public void SaveStationPoint_轴工站允许保存二百到三百九十九点位()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                var station = new DataStation(false)
                {
                    Name = "六轴轴工站",
                    Type = StationType.Axis
                };
                station.ListDataPos[250] = new DataPos(250)
                {
                    Name = "轴工站扩展点",
                    IsTaught = true
                };
                station.dicDataPos["轴工站扩展点"] = station.ListDataPos[250];
                Assert.IsTrue(runtime.Stores.Stations.TryCommit(
                    runtime.Paths.ConfigPath,
                    new[] { station },
                    out string saveError), saveError);
                var stationRuntime = new ScriptedMotionStation(MotionStationResult.Success);
                MotionCtrl motion = CreateMotion(runtime, stationRuntime, new CapturingLogger());

                MotionStationResult result = motion.SaveStationPoint(
                    0,
                    (DataPos)runtime.Stores.Stations.Items[0].ListDataPos[250].Clone());

                Assert.AreEqual(MotionStationResult.Success, result);
                Assert.AreEqual(1, stationRuntime.SavedPoints.Count);
            }
        }

        [TestMethod]
        public void SaveStationPoint_机器人工站拒绝二百及以上点位且不写控制器()
        {
            using (var directory = new TemporaryDirectory())
            {
                PlatformRuntime runtime = CreateRuntimeWithStation(
                    directory.FullPath,
                    out _);
                DataPos invalid = runtime.Stores.Stations.Items[0].ListDataPos[250];
                invalid.Name = "越界机器人点";
                invalid.IsTaught = true;
                var stationRuntime = new ScriptedMotionStation(MotionStationResult.Success);
                MotionCtrl motion = CreateMotion(runtime, stationRuntime, new CapturingLogger());

                MotionStationResult result = motion.SaveStationPoint(0, invalid);

                Assert.AreEqual(MotionStationResult.InvalidParameter, result);
                Assert.AreEqual(0, stationRuntime.SavedPoints.Count);
            }
        }

        private static PlatformRuntime CreateRuntimeWithStation(
            string path,
            out DataPos configuredPoint)
        {
            var runtime = new PlatformRuntime(path);
            var station = new DataStation(false)
            {
                Name = "机器人六轴工站",
                Type = StationType.Epson,
                CommunicationName = "EPSON命令",
                PointFromRobot = false
            };
            station.ListDataPos[18] = new DataPos(18)
            {
                Name = "取料位",
                IsTaught = true,
                X = 1,
                Y = 2,
                Z = 3,
                U = 4,
                V = 5,
                W = 6
            };
            station.dicDataPos["取料位"] = station.ListDataPos[18];
            Assert.IsTrue(
                runtime.Stores.Stations.TryCommit(
                    runtime.Paths.ConfigPath,
                    new[] { station },
                    out string saveError),
                saveError);
            configuredPoint = runtime.Stores.Stations.Items[0].ListDataPos[18];
            return runtime;
        }

        private static MotionCtrl CreateMotion(
            PlatformRuntime runtime,
            IMotionStation stationRuntime,
            ILogger logger)
        {
            var motion = new MotionCtrl(
                runtime.Stores.Values,
                runtime.Stores.Cards,
                runtime.Stores.Stations,
                runtime.Communication,
                runtime.Stores.Communication,
                runtime.Paths,
                runtime.Safety,
                runtime.Readiness,
                logger);
            FieldInfo stationsField = typeof(MotionCtrl).GetField(
                "stations",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(stationsField);
            var stations = stationsField.GetValue(motion) as Dictionary<short, IMotionStation>;
            Assert.IsNotNull(stations);
            stations[0] = stationRuntime;
            return motion;
        }

        private static DataPos CloneWithCoordinates(
            DataPos source,
            double x,
            double y,
            double z,
            double u,
            double v,
            double w)
        {
            var candidate = (DataPos)source.Clone();
            candidate.X = x;
            candidate.Y = y;
            candidate.Z = z;
            candidate.U = u;
            candidate.V = v;
            candidate.W = w;
            candidate.IsTaught = true;
            return candidate;
        }

        private static void AssertCoordinates(
            DataPos point,
            double x,
            double y,
            double z,
            double u,
            double v,
            double w)
        {
            Assert.IsNotNull(point);
            Assert.AreEqual(x, point.X);
            Assert.AreEqual(y, point.Y);
            Assert.AreEqual(z, point.Z);
            Assert.AreEqual(u, point.U);
            Assert.AreEqual(v, point.V);
            Assert.AreEqual(w, point.W);
        }

        private sealed class ScriptedMotionStation : IMotionStation
        {
            private readonly Queue<MotionStationResult> saveResults;

            public ScriptedMotionStation(params MotionStationResult[] saveResults)
            {
                this.saveResults = new Queue<MotionStationResult>(
                    saveResults ?? Array.Empty<MotionStationResult>());
            }

            public List<DataPos> SavedPoints { get; } = new List<DataPos>();

            public Action BeforeSave { get; set; }

            public MotionStationResult Initialize()
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult Release()
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult Home(short axis = -1, bool wait = true, bool group = false)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult SetSpeed(
                double velocity,
                double acceleration,
                double deceleration,
                short axis = -1,
                StationSpeedType type = StationSpeedType.Joint)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveToPoint(
                DataPos point,
                StationMoveMode mode,
                bool[] disabledAxes = null,
                short tool = 0)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveOffset(
                int basePointIndex,
                IReadOnlyList<double> offsets,
                StationMoveMode mode = StationMoveMode.Go)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult AxisMotion(
                short axis,
                double offset,
                StationAxisMoveMode mode = StationAxisMoveMode.Relative,
                short tool = 0)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult WaitMoveFinish(
                bool isHome = false,
                int axis = -1,
                int timeoutMs = 120000)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult GetCurrentPosition(short tool, out DataPos position)
            {
                position = null;
                return MotionStationResult.Success;
            }

            public MotionStationResult SavePoint(DataPos point)
            {
                BeforeSave?.Invoke();
                SavedPoints.Add(point == null ? null : (DataPos)point.Clone());
                return saveResults.Count == 0
                    ? MotionStationResult.Success
                    : saveResults.Dequeue();
            }

            public MotionStationResult CreateTray(
                int trayId,
                int rowCount,
                int columnCount,
                IReadOnlyList<DataPos> referencePoints)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveTrayPoint(
                int trayId,
                int position,
                DataPos calculatedPoint)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult ClearContinuousPath() => MotionStationResult.Success;
            public MotionStationResult AddContinuousLine(DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddContinuousArc(DataPos start, DataPos middle, DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddContinuousArcCenterRadius(DataPos target, DataPos center, double radius, int circle, bool counterClockwise) => MotionStationResult.Success;
            public MotionStationResult StartContinuousMove() => MotionStationResult.Success;

            public MotionStationResult Stop(bool emergency = false)
            {
                return MotionStationResult.Success;
            }

            public MotionStationStatus GetStatus()
            {
                return new MotionStationStatus();
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public string LastMessage { get; private set; } = string.Empty;

            public void Log(string message, LogLevel level)
            {
                LastMessage = message ?? string.Empty;
            }
        }
    }
}
