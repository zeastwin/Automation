using System;
// 模块：核心测试 / 机器人流程运动。
// 职责范围：固化机器人六轴工站的点位读写与料盘运行接线；测试不连接硬件。

using System.Collections.Generic;
using System.Linq;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class RobotStationProcessOperationTests
    {
        [TestMethod]
        public void ModifyStationPoint_CurrentRobotPosition_UsesStationRuntimeAndSavesPoint()
        {
            DataStation station = CreateRobotStation();
            DataPos target = AddPoint(station, 12, "目标点", 0, 0, 0, 0, 0, 0, false);
            var motion = new RecordingMotionRuntime
            {
                CurrentPosition = CreatePoint(-1, "当前位置", 11, 12, 13, 14, 15, 16),
            };

            using (ProcessEngine engine = CreateEngine(station, motion, new TrayPointStore()))
            {
                bool completed = engine.RunModifyStationPos(
                    new ProcHandle(),
                    new ModifyStationPos
                    {
                        StationName = station.Name,
                        RefPosName = "当前位置",
                        TargetPosName = target.Name,
                        ModifyType = "替换"
                    });

                Assert.IsTrue(completed);
            }

            Assert.AreEqual(1, motion.GetStationPositionCalls);
            Assert.AreEqual(0, motion.GetAxisPositionCalls,
                "机器人工站当前位置不得落回单轴卡读取路径。");
            Assert.AreEqual(1, motion.SaveStationPointCalls);
            AssertPoint(motion.SavedPoint, 12, "目标点", 11, 12, 13, 14, 15, 16);
        }

        [TestMethod]
        public void GetStationPoint_SpecifiedPoint_ReadsLocalPointTableWithoutRobotQuery()
        {
            DataStation station = CreateRobotStation();
            DataPos source = AddPoint(station, 21, "来源点", 1, 2, 3, 4, 5, 6, true);
            source.Pose = new short[] { 2, 1, 0, 1 };
            DataPos target = AddPoint(station, 22, "保存点", 9, 9, 9, 9, 9, 9, true);
            var motion = new RecordingMotionRuntime
            {
                CurrentPosition = CreatePoint(-1, "不应读取", 101, 102, 103, 104, 105, 106)
            };

            using (ProcessEngine engine = CreateEngine(station, motion, new TrayPointStore()))
            {
                bool completed = engine.RunGetStationPos(
                    new ProcHandle(),
                    new GetStationPos
                    {
                        StationName = station.Name,
                        SourceType = "指定点位",
                        SourcePosName = source.Name,
                        SaveType = "保存到点位",
                        TargetPosName = target.Name
                    });

                Assert.IsTrue(completed);
            }

            Assert.AreEqual(0, motion.GetStationPositionCalls,
                "3.0 的指定点语义是读取平台点表，不能误发机器人 GET_POS。");
            Assert.AreEqual(1, motion.SaveStationPointCalls);
            AssertPoint(motion.SavedPoint, 22, "保存点", 1, 2, 3, 4, 5, 6);
            CollectionAssert.AreEqual(source.Pose, motion.SavedPoint.Pose);
        }

        [TestMethod]
        public void GetStationPoint_CurrentPosition_ReadsRobotAndPreservesPoseWhenSaving()
        {
            DataStation station = CreateRobotStation();
            DataPos target = AddPoint(station, 31, "保存点", 0, 0, 0, 0, 0, 0, false);
            DataPos current = CreatePoint(-1, "当前位置", 31, 32, 33, 34, 35, 36);
            current.Pose = new short[] { 0, 1, 1, 0 };
            var motion = new RecordingMotionRuntime { CurrentPosition = current };

            using (ProcessEngine engine = CreateEngine(station, motion, new TrayPointStore()))
            {
                bool completed = engine.RunGetStationPos(
                    new ProcHandle(),
                    new GetStationPos
                    {
                        StationName = station.Name,
                        SourceType = "当前位置",
                        SaveType = "保存到点位",
                        TargetPosName = target.Name
                    });

                Assert.IsTrue(completed);
            }

            Assert.AreEqual(1, motion.GetStationPositionCalls);
            Assert.AreEqual(1, motion.SaveStationPointCalls);
            AssertPoint(motion.SavedPoint, 31, "保存点", 31, 32, 33, 34, 35, 36);
            CollectionAssert.AreEqual(current.Pose, motion.SavedPoint.Pose);
        }

        [TestMethod]
        public void CreateTray_ControllerFailure_DoesNotPublishPlatformCache()
        {
            DataStation station = CreateRobotStation();
            AddPoint(station, 1, "左上", 0, 0, 0, 0, 0, 0, true);
            AddPoint(station, 2, "右上", 10, 0, 0, 0, 0, 0, true);
            AddPoint(station, 3, "左下", 0, 10, 0, 0, 0, 0, true);
            AddPoint(station, 4, "右下", 10, 10, 0, 0, 0, 0, true);
            var trayStore = new TrayPointStore();
            var motion = new RecordingMotionRuntime
            {
                CreateStationTrayResult = MotionStationResult.CommandRejected
            };

            using (ProcessEngine engine = CreateEngine(station, motion, trayStore))
            {
                InvalidOperationException failure = null;
                try
                {
                    engine.RunCreateTray(
                        new ProcHandle(),
                        new CreateTray
                        {
                            StationName = station.Name,
                            TrayId = 7,
                            RowCount = 2,
                            ColCount = 2,
                            PX1 = "左上",
                            PX2 = "右上",
                            PY1 = "左下",
                            PY2 = "右下"
                        });
                }
                catch (InvalidOperationException ex)
                {
                    failure = ex;
                }
                Assert.IsNotNull(failure);
            }

            Assert.AreEqual(1, motion.CreateStationTrayCalls);
            Assert.AreEqual(7, motion.CreatedTrayId);
            Assert.AreEqual(2, motion.CreatedRowCount);
            Assert.AreEqual(2, motion.CreatedColumnCount);
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3, 4 },
                motion.CreatedReferencePointIndexes.ToArray());
            Assert.IsFalse(trayStore.TryGet(station.Name, 7, out _),
                "控制器创建料盘失败时，平台缓存不得表现为已创建成功。");
        }

        [TestMethod]
        public void RunTrayPoint_RobotUsesControllerPalletWithZeroBasedPosition()
        {
            DataStation station = CreateRobotStation();
            var trayStore = new TrayPointStore();
            Assert.IsTrue(trayStore.TrySave(
                new TrayPointGrid(
                    station.Name,
                    8,
                    1,
                    2,
                    new List<TrayPoint>
                    {
                        new TrayPoint(1, 1, 1, 1, 2, 3, 4, 5, 6),
                        new TrayPoint(2, 1, 2, 11, 12, 13, 14, 15, 16)
                    }),
                out string cacheError), cacheError);
            var motion = new RecordingMotionRuntime();

            using (ProcessEngine engine = CreateEngine(station, motion, trayStore))
            {
                bool completed = engine.RunTrayRunPos(
                    new ProcHandle(),
                    new TrayRunPos
                    {
                        StationName = station.Name,
                        TrayId = 8,
                        TrayPos = 2,
                        ContinueWithoutWaiting = true
                    });

                Assert.IsTrue(completed);
            }

            Assert.AreEqual(1, motion.MoveStationTrayPointCalls);
            Assert.AreEqual(8, motion.MovedTrayId);
            Assert.AreEqual(1, motion.MovedTrayPosition,
                "流程料盘位置沿用面向操作员的一基编号，控制器调用沿用 3.0 的零基编号。");
            AssertPoint(motion.CalculatedTrayPoint, -1, "料盘点", 11, 12, 13, 14, 15, 16);
            Assert.AreEqual(0, motion.MoveStationToPointCalls,
                "机器人的料盘动作必须保留控制器原生料盘语义。");
            Assert.AreEqual(0, motion.WaitStationMotionCalls);
        }

        private static ProcessEngine CreateEngine(
            DataStation station,
            IMotionRuntime motion,
            TrayPointStore trayStore)
        {
            return new ProcessEngine(new EngineContext
            {
                Procs = new List<Proc>(),
                Stations = new List<DataStation> { station },
                Motion = motion,
                TrayPointStore = trayStore
            });
        }

        private static DataStation CreateRobotStation()
        {
            return new DataStation(false)
            {
                Name = "机器人六轴工站",
                Type = StationType.Epson,
                CommunicationName = "EPSON命令",
                PointFromRobot = false
            };
        }

        private static DataPos AddPoint(
            DataStation station,
            int index,
            string name,
            double x,
            double y,
            double z,
            double u,
            double v,
            double w,
            bool taught)
        {
            DataPos point = CreatePoint(index, name, x, y, z, u, v, w);
            point.IsTaught = taught;
            station.ListDataPos[index] = point;
            station.dicDataPos[name] = point;
            return point;
        }

        private static DataPos CreatePoint(
            int index,
            string name,
            double x,
            double y,
            double z,
            double u,
            double v,
            double w)
        {
            return new DataPos(index)
            {
                Name = name,
                IsTaught = true,
                X = x,
                Y = y,
                Z = z,
                U = u,
                V = v,
                W = w
            };
        }

        private static void AssertPoint(
            DataPos point,
            int expectedIndex,
            string expectedName,
            double x,
            double y,
            double z,
            double u,
            double v,
            double w)
        {
            Assert.IsNotNull(point);
            Assert.AreEqual(expectedIndex, point.Index);
            Assert.AreEqual(expectedName, point.Name);
            CollectionAssert.AreEqual(
                new[] { x, y, z, u, v, w },
                point.GetAllValues().ToArray());
            Assert.AreEqual(true, point.IsTaught);
        }

        private sealed class RecordingMotionRuntime : IMotionRuntime
        {
            private sealed class EmptyLease : IDisposable
            {
                public void Dispose()
                {
                }
            }

            public bool IsCardInitialized => true;
            public int StationCount => 1;
            public DataPos CurrentPosition { get; set; } = new DataPos(-1);
            public MotionStationResult GetStationPositionResult { get; set; } = MotionStationResult.Success;
            public MotionStationResult SaveStationPointResult { get; set; } = MotionStationResult.Success;
            public MotionStationResult CreateStationTrayResult { get; set; } = MotionStationResult.Success;
            public MotionStationResult MoveStationTrayPointResult { get; set; } = MotionStationResult.Success;
            public int GetStationPositionCalls { get; private set; }
            public int GetAxisPositionCalls { get; private set; }
            public int SaveStationPointCalls { get; private set; }
            public int CreateStationTrayCalls { get; private set; }
            public int MoveStationTrayPointCalls { get; private set; }
            public int MoveStationToPointCalls { get; private set; }
            public int WaitStationMotionCalls { get; private set; }
            public DataPos SavedPoint { get; private set; }
            public int CreatedTrayId { get; private set; }
            public int CreatedRowCount { get; private set; }
            public int CreatedColumnCount { get; private set; }
            public List<int> CreatedReferencePointIndexes { get; } = new List<int>();
            public int MovedTrayId { get; private set; }
            public int MovedTrayPosition { get; private set; }
            public DataPos CalculatedTrayPoint { get; private set; }

            public void InitCardType()
            {
            }

            public bool InitCard()
            {
                return true;
            }

            public MotionStationResult InitializeStations()
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult ReleaseStations()
            {
                return MotionStationResult.Success;
            }

            public MotionStationStatus GetStationStatus(short station)
            {
                return new MotionStationStatus();
            }

            public MotionStationResult SetStationSpeed(
                short station,
                double velocity,
                double acceleration,
                double deceleration,
                short axis = -1,
                StationSpeedType type = StationSpeedType.Joint)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult HomeStation(
                short station,
                short axis = -1,
                bool wait = true,
                bool group = false)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationToPoint(
                short station,
                DataPos point,
                StationMoveMode mode = StationMoveMode.Go,
                bool[] disabledAxes = null,
                short tool = 0)
            {
                MoveStationToPointCalls++;
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationOffset(
                short station,
                int basePointIndex,
                IReadOnlyList<double> offsets,
                StationMoveMode mode = StationMoveMode.Go)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationAxis(
                short station,
                short axis,
                double offset,
                StationAxisMoveMode mode = StationAxisMoveMode.Relative,
                short tool = 0)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult WaitStationMotion(
                short station,
                bool isHome = false,
                int axis = -1,
                int timeoutMs = 120000)
            {
                WaitStationMotionCalls++;
                return MotionStationResult.Success;
            }

            public MotionStationResult GetStationPosition(short station, short tool, out DataPos position)
            {
                GetStationPositionCalls++;
                position = CurrentPosition == null ? null : (DataPos)CurrentPosition.Clone();
                return GetStationPositionResult;
            }

            public MotionStationResult SaveStationPoint(short station, DataPos point)
            {
                SaveStationPointCalls++;
                SavedPoint = point == null ? null : (DataPos)point.Clone();
                return SaveStationPointResult;
            }

            public MotionStationResult CreateStationTray(
                short station,
                int trayId,
                int rowCount,
                int columnCount,
                IReadOnlyList<DataPos> referencePoints)
            {
                CreateStationTrayCalls++;
                CreatedTrayId = trayId;
                CreatedRowCount = rowCount;
                CreatedColumnCount = columnCount;
                CreatedReferencePointIndexes.Clear();
                if (referencePoints != null)
                {
                    foreach (DataPos point in referencePoints)
                    {
                        CreatedReferencePointIndexes.Add(point?.Index ?? -1);
                    }
                }
                return CreateStationTrayResult;
            }

            public MotionStationResult MoveStationTrayPoint(
                short station,
                int trayId,
                int position,
                DataPos calculatedPoint)
            {
                MoveStationTrayPointCalls++;
                MovedTrayId = trayId;
                MovedTrayPosition = position;
                CalculatedTrayPoint = calculatedPoint == null
                    ? null
                    : (DataPos)calculatedPoint.Clone();
                return MoveStationTrayPointResult;
            }

            public MotionStationResult ClearStationContinuousPath(short station) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousLine(short station, DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousArc(short station, DataPos start, DataPos middle, DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousArcCenterRadius(short station, DataPos target, DataPos center, double radius, int circle, bool counterClockwise) => MotionStationResult.Success;
            public MotionStationResult StartStationContinuousMove(short station) => MotionStationResult.Success;

            public MotionStationResult StopStation(short station, bool emergency = false)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult StopAllStations(bool emergency = false)
            {
                return MotionStationResult.Success;
            }

            public void SettHomeParam(ushort card, ushort axis, ushort dir, ushort speed, ushort homeMode)
            {
            }

            public void StartHome(ushort card, ushort axis)
            {
            }

            public void CleanPos(ushort card, ushort axis)
            {
            }

            public double GetAxisPos(ushort card, ushort axis)
            {
                GetAxisPositionCalls++;
                return 0;
            }

            public void SetMovParam(
                ushort card,
                ushort axis,
                double minVel,
                double maxVel,
                double acc,
                double dec,
                double stopVel,
                double sPara,
                int equiv)
            {
            }

            public void Mov(ushort card, ushort axis, double distance, ushort positionMode, bool wait)
            {
            }

            public void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request)
            {
            }

            public bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem)
            {
                return true;
            }

            public void StopCoordinatedLinear(ushort card, ushort coordinateSystem, ushort stopMode)
            {
            }

            public void MoveContinuousPath(ContinuousPathMoveRequest request) { }
            public bool IsContinuousPathDone(ushort card, ushort coordinateSystem) => true;
            public void StopContinuousPath(ushort card, ushort coordinateSystem, ushort stopMode) { }

            public void Jog(ushort card, ushort axis, ushort direction)
            {
            }

            public void StopOneAxis(ushort card, ushort axis, ushort stopMode)
            {
            }

            public void StopConnect()
            {
            }

            public bool HomeStatus(ushort card, ushort axis)
            {
                return true;
            }

            public bool GetInPos(ushort card, ushort axis)
            {
                return true;
            }

            public bool GetAxisSevon(ushort card, ushort axis)
            {
                return true;
            }

            public void SetAxisSevon(ushort card, ushort axis, bool isSevon)
            {
            }

            public void DownLoadConfig()
            {
            }

            public void SetAllAxisSevonOn()
            {
            }

            public void SetAllAxisEquiv()
            {
            }

            public void ResetAxisAlarm(ushort card, ushort axis)
            {
            }

            public double GetAxisCurSpeed(ushort card, ushort axis)
            {
                return 0;
            }

            public uint GetAxisIoStatus(ushort card, ushort axis)
            {
                return 0;
            }

            public ushort GetAxisAlarmCode(ushort card, ushort axis)
            {
                return 0;
            }

            public IDisposable ValidateAxesForCommand(IReadOnlyCollection<AxisCommandRequest> requests)
            {
                return new EmptyLease();
            }
        }
    }
}
