using System;
// 模块：核心测试 / 轴工站流程运动。
// 职责范围：验证自动流程直接复用六轴工站运行时，不旁路到原始控制卡命令。

using System.Collections.Generic;
using System.Linq;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AxisStationProcessOperationTests
    {
        [TestMethod]
        public void RunPosition_UsesStationMoveAndPreservesDisabledAxisResourcesAndCheck()
        {
            using (Fixture fixture = CreateFixture())
            {
                fixture.Station.CoordinateSystem = 7;
                DataPos point = AddPoint(
                    fixture.Station,
                    3,
                    "生产点",
                    10,
                    20,
                    30,
                    40,
                    50,
                    60);
                var operation = new StationRunPos
                {
                    Name = "轴工站走点",
                    StationName = fixture.Station.Name,
                    PosIndex = point.Index,
                    IsDisableAxis = "有禁用",
                    Axis2 = true,
                    ChangeVel = "改变速度",
                    Vel = 80,
                    Acc = 70,
                    Dec = 60,
                    ContinueWithoutWaiting = false,
                    CheckInPosition = true,
                    TimeoutMs = 1234
                };
                var handle = new ProcHandle();

                Assert.IsTrue(fixture.Engine.RunStationRunPos(handle, operation));

                Assert.AreEqual(1, fixture.Motion.MoveToPointCalls);
                Assert.AreEqual(StationMoveMode.Move, fixture.Motion.LastPointMoveMode);
                AssertPoint(point, fixture.Motion.LastPoint);
                CollectionAssert.AreEqual(
                    new[] { false, true, true, true, true, true },
                    fixture.Motion.LastDisabledAxes);
                Assert.AreEqual(1, fixture.Motion.WaitCalls);
                Assert.AreEqual(1234, fixture.Motion.LastWaitTimeout);
                Assert.AreEqual(1, fixture.Motion.GetStationPositionCalls);
                AssertSpeed(fixture.Motion.LastSpeed, 80, 70, 60);
                AssertReservedResources(handle, new ushort[] { 0 }, 7);
                AssertNoRawAxisMove(fixture.Motion);
            }
        }

        [TestMethod]
        public void RunRelative_UsesMinimumParticipatingSpeedAndStationPositionInEngineeringUnits()
        {
            using (Fixture fixture = CreateFixture())
            {
                fixture.Engine.Context.AxisMotionParameters.Set(0, 0, 80, 70, 60);
                fixture.Engine.Context.AxisMotionParameters.Set(0, 1, 25, 35, 45);
                fixture.Motion.CurrentPosition = CreatePoint(
                    -1,
                    "当前位置",
                    100,
                    200,
                    300,
                    400,
                    500,
                    600);
                var operation = new StationRunRel
                {
                    Name = "轴工站偏移",
                    StationName = fixture.Station.Name,
                    Axis1 = 1,
                    Axis2 = 2,
                    Axis3 = 3,
                    Axis4 = 4,
                    Axis5 = 5,
                    Axis6 = 6,
                    ContinueWithoutWaiting = false,
                    CheckInPosition = true,
                    TimeoutMs = 2345
                };
                var handle = new ProcHandle();

                Assert.IsTrue(fixture.Engine.RunStationRunRel(handle, operation));

                Assert.AreEqual(1, fixture.Motion.MoveOffsetCalls);
                Assert.AreEqual(StationMoveMode.Move, fixture.Motion.LastOffsetMoveMode);
                CollectionAssert.AreEqual(
                    new[] { 1d, 2d, 3d, 4d, 5d, 6d },
                    fixture.Motion.LastOffsets.ToArray());
                Assert.AreEqual(1, fixture.Motion.WaitCalls);
                Assert.AreEqual(2345, fixture.Motion.LastWaitTimeout);
                Assert.AreEqual(2, fixture.Motion.GetStationPositionCalls);
                AssertSpeed(fixture.Motion.LastSpeed, 25, 35, 45);
                AssertReservedResources(handle, new ushort[] { 0, 1 });
                AssertNoRawAxisMove(fixture.Motion);
            }
        }

        [TestMethod]
        public void RunTrayPoint_UsesStationTrayMoveAndKeepsNoWaitSemantics()
        {
            using (Fixture fixture = CreateFixture())
            {
                fixture.Engine.Context.AxisMotionParameters.Set(0, 0, 55, 65, 75);
                fixture.Engine.Context.AxisMotionParameters.Set(0, 1, 35, 45, 50);
                Assert.IsTrue(
                    fixture.Trays.TrySave(
                        new TrayPointGrid(
                            fixture.Station.Name,
                            8,
                            1,
                            1,
                            new List<TrayPoint>
                            {
                                new TrayPoint(1, 1, 1, 11, 12, 13, 14, 15, 16)
                            }),
                        out string saveError),
                    saveError);
                var operation = new TrayRunPos
                {
                    Name = "轴工站料盘点",
                    StationName = fixture.Station.Name,
                    TrayId = 8,
                    TrayPos = 1,
                    ContinueWithoutWaiting = true
                };
                var handle = new ProcHandle();

                Assert.IsTrue(fixture.Engine.RunTrayRunPos(handle, operation));

                Assert.AreEqual(1, fixture.Motion.MoveTrayPointCalls);
                Assert.AreEqual(8, fixture.Motion.LastTrayId);
                Assert.AreEqual(0, fixture.Motion.LastTrayPosition);
                CollectionAssert.AreEqual(
                    new[] { 11d, 12d, 13d, 14d, 15d, 16d },
                    fixture.Motion.LastTrayPoint.GetAllValues().ToArray());
                Assert.AreEqual(0, fixture.Motion.WaitCalls);
                AssertSpeed(fixture.Motion.LastSpeed, 35, 45, 50);
                AssertReservedResources(handle, new ushort[] { 0, 1 });
                AssertNoRawAxisMove(fixture.Motion);
            }
        }

        private static Fixture CreateFixture()
        {
            var cardStore = new CardConfigStore();
            var card = new Card();
            var controlCard = new ControlCard
            {
                cardHead = new CardHead
                {
                    AxisCount = 2,
                    InputCount = 16,
                    OutputCount = 16
                }
            };
            for (int axisIndex = 0; axisIndex < 2; axisIndex++)
            {
                controlCard.axis.Add(new Axis
                {
                    AxisName = "A" + axisIndex,
                    AxisNum = axisIndex,
                    PulseToMM = 1000 + axisIndex,
                    HomeMethod = -1,
                    HomeSpeed = "10",
                    SpeedMax = 100 + axisIndex,
                    AccMax = 0.2 + axisIndex,
                    DecMax = 0.3 + axisIndex
                });
            }
            card.controlCards.Add(controlCard);
            cardStore.SetCard(card);

            var station = new DataStation(false)
            {
                Name = "轴六轴工站",
                Type = StationType.Axis,
                CoordinateSystem = 1
            };
            for (int channel = 0; channel < 2; channel++)
            {
                station.dataAxis.axisConfigs[channel].CardNum = "0";
                station.dataAxis.axisConfigs[channel].AxisName = "A" + channel;
                station.dataAxis.axisConfigs[channel].axis = controlCard.axis[channel];
            }

            var motion = new RecordingMotionRuntime();
            var trays = new TrayPointStore();
            var engine = new ProcessEngine(new EngineContext
            {
                Procs = new List<Proc>(),
                Stations = new List<DataStation> { station },
                Motion = motion,
                CardStore = cardStore,
                TrayPointStore = trays
            });
            return new Fixture(engine, station, motion, trays);
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
            double w)
        {
            DataPos point = CreatePoint(index, name, x, y, z, u, v, w);
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
                Enabled = true,
                X = x,
                Y = y,
                Z = z,
                U = u,
                V = v,
                W = w
            };
        }

        private static void AssertPoint(DataPos expected, DataPos actual)
        {
            Assert.IsNotNull(actual);
            Assert.AreEqual(expected.Index, actual.Index);
            Assert.AreEqual(expected.Name, actual.Name);
            CollectionAssert.AreEqual(
                expected.GetAllValues().ToArray(),
                actual.GetAllValues().ToArray());
        }

        private static void AssertSpeed(
            RecordedStationSpeed actual,
            double velocity,
            double acceleration,
            double deceleration)
        {
            Assert.IsNotNull(actual);
            Assert.AreEqual((short)0, actual.Station);
            Assert.AreEqual(velocity, actual.Velocity);
            Assert.AreEqual(acceleration, actual.Acceleration);
            Assert.AreEqual(deceleration, actual.Deceleration);
            Assert.AreEqual((short)-1, actual.Axis);
            Assert.AreEqual(StationSpeedType.Move, actual.Type);
        }

        private static void AssertReservedResources(
            ProcHandle handle,
            IReadOnlyList<ushort> axes,
            ushort coordinateSystem = 1)
        {
            Assert.IsTrue(handle.OwnedAxes.ContainsKey(
                ProcessEngine.BuildStationMotionResourceKey(0)));
            foreach (ushort axis in axes)
            {
                Assert.IsTrue(handle.OwnedAxes.ContainsKey(
                    ProcessEngine.BuildMotionResourceKey(0, axis)));
            }
            Assert.AreEqual(axes.Count + 1, handle.OwnedAxes.Count);
            Assert.IsTrue(handle.OwnedCoordinateSystems.ContainsKey(coordinateSystem));
        }

        private static void AssertNoRawAxisMove(RecordingMotionRuntime motion)
        {
            Assert.AreEqual(0, motion.RawPositionReads);
            Assert.AreEqual(0, motion.RawMoveCalls);
            Assert.AreEqual(0, motion.RawCoordinatedMoveCalls);
            Assert.AreEqual(0, motion.RawValidationCalls);
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture(
                ProcessEngine engine,
                DataStation station,
                RecordingMotionRuntime motion,
                TrayPointStore trays)
            {
                Engine = engine;
                Station = station;
                Motion = motion;
                Trays = trays;
            }

            public ProcessEngine Engine { get; }

            public DataStation Station { get; }

            public RecordingMotionRuntime Motion { get; }

            public TrayPointStore Trays { get; }

            public void Dispose()
            {
                Engine.Dispose();
            }
        }

        private sealed class RecordedStationSpeed
        {
            public short Station { get; set; }

            public double Velocity { get; set; }

            public double Acceleration { get; set; }

            public double Deceleration { get; set; }

            public short Axis { get; set; }

            public StationSpeedType Type { get; set; }
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

            public DataPos CurrentPosition { get; set; } = CreatePoint(
                -1,
                "当前位置",
                0,
                0,
                0,
                0,
                0,
                0);

            public RecordedStationSpeed LastSpeed { get; private set; }

            public int MoveToPointCalls { get; private set; }

            public DataPos LastPoint { get; private set; }

            public StationMoveMode LastPointMoveMode { get; private set; }

            public bool[] LastDisabledAxes { get; private set; }

            public int MoveOffsetCalls { get; private set; }

            public IReadOnlyList<double> LastOffsets { get; private set; }

            public StationMoveMode LastOffsetMoveMode { get; private set; }

            public int MoveTrayPointCalls { get; private set; }

            public int LastTrayId { get; private set; }

            public int LastTrayPosition { get; private set; }

            public DataPos LastTrayPoint { get; private set; }

            public int WaitCalls { get; private set; }

            public int LastWaitTimeout { get; private set; }

            public int GetStationPositionCalls { get; private set; }

            public int RawPositionReads { get; private set; }

            public int RawMoveCalls { get; private set; }

            public int RawCoordinatedMoveCalls { get; private set; }

            public int RawValidationCalls { get; private set; }

            public void InitCardType()
            {
            }

            public bool InitCard() => true;

            public MotionStationResult InitializeStations() => MotionStationResult.Success;

            public MotionStationResult ReleaseStations() => MotionStationResult.Success;

            public MotionStationStatus GetStationStatus(short station) => new MotionStationStatus();

            public MotionStationResult SetStationSpeed(
                short station,
                double velocity,
                double acceleration,
                double deceleration,
                short axis = -1,
                StationSpeedType type = StationSpeedType.Joint)
            {
                LastSpeed = new RecordedStationSpeed
                {
                    Station = station,
                    Velocity = velocity,
                    Acceleration = acceleration,
                    Deceleration = deceleration,
                    Axis = axis,
                    Type = type
                };
                return MotionStationResult.Success;
            }

            public MotionStationResult HomeStation(
                short station,
                short axis = -1,
                bool wait = true,
                bool group = false) => MotionStationResult.Success;

            public MotionStationResult MoveStationToPoint(
                short station,
                DataPos point,
                StationMoveMode mode = StationMoveMode.Go,
                bool[] disabledAxes = null,
                short tool = 0)
            {
                MoveToPointCalls++;
                LastPoint = point == null ? null : (DataPos)point.Clone();
                LastPointMoveMode = mode;
                LastDisabledAxes = disabledAxes == null
                    ? null
                    : (bool[])disabledAxes.Clone();
                CurrentPosition = point == null ? null : (DataPos)point.Clone();
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationOffset(
                short station,
                int basePointIndex,
                IReadOnlyList<double> offsets,
                StationMoveMode mode = StationMoveMode.Go)
            {
                MoveOffsetCalls++;
                LastOffsets = offsets?.ToArray();
                LastOffsetMoveMode = mode;
                if (CurrentPosition != null && offsets != null && offsets.Count >= 6)
                {
                    CurrentPosition.X += offsets[0];
                    CurrentPosition.Y += offsets[1];
                    CurrentPosition.Z += offsets[2];
                    CurrentPosition.U += offsets[3];
                    CurrentPosition.V += offsets[4];
                    CurrentPosition.W += offsets[5];
                }
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationAxis(
                short station,
                short axis,
                double offset,
                StationAxisMoveMode mode = StationAxisMoveMode.Relative,
                short tool = 0) => MotionStationResult.Success;

            public MotionStationResult WaitStationMotion(
                short station,
                bool isHome = false,
                int axis = -1,
                int timeoutMs = 120000)
            {
                WaitCalls++;
                LastWaitTimeout = timeoutMs;
                return MotionStationResult.Success;
            }

            public MotionStationResult GetStationPosition(
                short station,
                short tool,
                out DataPos position)
            {
                GetStationPositionCalls++;
                position = CurrentPosition == null
                    ? null
                    : (DataPos)CurrentPosition.Clone();
                return MotionStationResult.Success;
            }

            public MotionStationResult SaveStationPoint(short station, DataPos point) =>
                MotionStationResult.Success;

            public MotionStationResult CreateStationTray(
                short station,
                int trayId,
                int rowCount,
                int columnCount,
                IReadOnlyList<DataPos> referencePoints) => MotionStationResult.Success;

            public MotionStationResult MoveStationTrayPoint(
                short station,
                int trayId,
                int position,
                DataPos calculatedPoint)
            {
                MoveTrayPointCalls++;
                LastTrayId = trayId;
                LastTrayPosition = position;
                LastTrayPoint = calculatedPoint == null
                    ? null
                    : (DataPos)calculatedPoint.Clone();
                CurrentPosition = calculatedPoint == null
                    ? null
                    : (DataPos)calculatedPoint.Clone();
                return MotionStationResult.Success;
            }

            public MotionStationResult ClearStationContinuousPath(short station) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousLine(short station, DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousArc(short station, DataPos start, DataPos middle, DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousArcCenterRadius(short station, DataPos target, DataPos center, double radius, int circle, bool counterClockwise) => MotionStationResult.Success;
            public MotionStationResult StartStationContinuousMove(short station) => MotionStationResult.Success;

            public MotionStationResult StopStation(short station, bool emergency = false) =>
                MotionStationResult.Success;

            public MotionStationResult StopAllStations(bool emergency = false) =>
                MotionStationResult.Success;

            public void SettHomeParam(
                ushort card,
                ushort axis,
                ushort dir,
                ushort speed,
                ushort homeMode)
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
                RawPositionReads++;
                return 1000000;
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
                RawMoveCalls++;
            }

            public void Mov(
                ushort card,
                ushort axis,
                double distance,
                ushort positionMode,
                bool wait)
            {
                RawMoveCalls++;
            }

            public void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request)
            {
                RawCoordinatedMoveCalls++;
            }

            public bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem) => true;

            public void StopCoordinatedLinear(
                ushort card,
                ushort coordinateSystem,
                ushort stopMode)
            {
            }

            public void MoveContinuousPath(ContinuousPathMoveRequest request) { }
            public bool IsContinuousPathDone(ushort card, ushort coordinateSystem) => true;
            public void StopContinuousPath(ushort card, ushort coordinateSystem, ushort stopMode) { }

            public void Jog(ushort card, ushort axis, ushort direction)
            {
                RawMoveCalls++;
            }

            public void StopOneAxis(ushort card, ushort axis, ushort stopMode)
            {
            }

            public void StopConnect()
            {
            }

            public bool HomeStatus(ushort card, ushort axis) => true;

            public bool GetInPos(ushort card, ushort axis) => true;

            public bool GetAxisSevon(ushort card, ushort axis) => true;

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

            public double GetAxisCurSpeed(ushort card, ushort axis) => 0;

            public uint GetAxisIoStatus(ushort card, ushort axis) => 0;

            public ushort GetAxisAlarmCode(ushort card, ushort axis) => 0;

            public IDisposable ValidateAxesForCommand(
                IReadOnlyCollection<AxisCommandRequest> requests)
            {
                RawValidationCalls++;
                return new EmptyLease();
            }
        }
    }
}
