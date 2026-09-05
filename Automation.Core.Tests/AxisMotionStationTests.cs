using System;
// 模块：核心测试 / 轴工站。
// 职责范围：以无硬件记录运行时锁定 3.0 六通道工站的关键行为。

using System.Collections.Generic;
using System.Linq;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AxisMotionStationTests
    {
        [TestMethod]
        public void MoveToPoint_MapsSixChannelsToConfiguredPhysicalAxes()
        {
            AxisMotionStation station = CreateStation(
                new[] { 4, 2, 5, 0, 3, 1 },
                out RecordingMotionRuntime runtime,
                out _);
            DataPos point = CreatePoint(10, 20, 30, 40, 50, 60);

            MotionStationResult result = station.MoveToPoint(point, StationMoveMode.Go);

            Assert.AreEqual(MotionStationResult.Success, result);
            CollectionAssert.AreEqual(
                new ushort[] { 4, 2, 5, 0, 3, 1 },
                runtime.Moves.Select(item => item.Axis).ToArray());
            CollectionAssert.AreEqual(
                new double[] { 10, 20, 30, 40, 50, 60 },
                runtime.Moves.Select(item => item.Target).ToArray());
            Assert.AreEqual(1, runtime.ValidationBatches.Count);
            CollectionAssert.AreEqual(
                new ushort[] { 4, 2, 5, 0, 3, 1 },
                runtime.ValidationBatches[0].Select(item => item.Axis).ToArray());
        }

        [TestMethod]
        public void Home_UsesConfiguredSequenceBeforeStartingRemainingAxes()
        {
            AxisMotionStation station = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime runtime,
                out DataStation definition,
                initialize: false);
            definition.homeSeq.AxisName1.Name = "A2";
            definition.homeSeq.AxisName2.Name = "A0";
            Assert.AreEqual(MotionStationResult.Success, station.Initialize());

            MotionStationResult result = station.Home();

            Assert.AreEqual(MotionStationResult.Success, result);
            CollectionAssert.AreEqual(
                new ushort[] { 2, 0, 1, 3, 4, 5 },
                runtime.HomeStarts.ToArray());
            Assert.AreEqual(3, runtime.ValidationBatches.Count);
            CollectionAssert.AreEqual(
                new ushort[] { 2 },
                runtime.ValidationBatches[0].Select(item => item.Axis).ToArray());
            CollectionAssert.AreEqual(
                new ushort[] { 0 },
                runtime.ValidationBatches[1].Select(item => item.Axis).ToArray());
            CollectionAssert.AreEqual(
                new ushort[] { 1, 3, 4, 5 },
                runtime.ValidationBatches[2].Select(item => item.Axis).ToArray());
            CollectionAssert.AreEqual(
                new ushort[] { 2, 0, 1, 3, 4, 5 },
                runtime.CleanedAxes.ToArray());
            CollectionAssert.AreEqual(
                new ushort[] { 22, 0, 21, 23, 24, 25 },
                runtime.HomeProfiles.Select(item => item.HomeMethod).ToArray());
            Assert.IsTrue(runtime.HomeProfiles.All(item => item.Direction == 0));

            Assert.AreEqual(
                MotionStationResult.Success,
                station.WaitMoveFinish(isHome: true, timeoutMs: 0));
            Assert.AreEqual(6, runtime.CleanedAxes.Count);
        }

        [TestMethod]
        public void MoveToPoint_DisabledPointAndAxesDoNotSendCommands()
        {
            AxisMotionStation station = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime runtime,
                out _);
            DataPos point = CreatePoint(10, 20, 30, 40, 50, 60);
            point.Enabled = false;

            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(point, StationMoveMode.Go));
            Assert.AreEqual(0, runtime.Moves.Count);
            Assert.AreEqual(0, runtime.ValidationBatches.Count);

            point.Enabled = true;
            bool[] disabledAxes = { false, true, false, true, true, false };
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(point, StationMoveMode.Go, disabledAxes));
            CollectionAssert.AreEqual(
                new ushort[] { 0, 2, 5 },
                runtime.Moves.Select(item => item.Axis).ToArray());
            CollectionAssert.AreEqual(
                new ushort[] { 0, 2, 5 },
                runtime.ValidationBatches.Single().Select(item => item.Axis).ToArray());
        }

        [TestMethod]
        public void AxisMotion_RelativeAndEncoderRelativeUseOneMappedAxis()
        {
            AxisMotionStation station = CreateStation(
                new[] { 4, 1, 2, 3, 0, 5 },
                out RecordingMotionRuntime runtime,
                out _);
            runtime.Positions[4] = 12;

            Assert.AreEqual(
                MotionStationResult.Success,
                station.AxisMotion(0, 5, StationAxisMoveMode.Relative));
            RecordedMove relative = runtime.Moves.Single();
            Assert.AreEqual((ushort)4, relative.Axis);
            Assert.AreEqual((ushort)0, relative.PositionMode);
            Assert.AreEqual(5d, relative.Target);
            Assert.AreEqual(17d, runtime.Positions[4]);

            runtime.Moves.Clear();
            runtime.Positions[4] = 12;
            Assert.AreEqual(
                MotionStationResult.Success,
                station.AxisMotion(0, 5, StationAxisMoveMode.RelativeByEncoder));
            RecordedMove encoderRelative = runtime.Moves.Single();
            Assert.AreEqual((ushort)4, encoderRelative.Axis);
            Assert.AreEqual((ushort)1, encoderRelative.PositionMode);
            Assert.AreEqual(17d, encoderRelative.Target);
            Assert.AreEqual(17d, runtime.Positions[4]);
            Assert.AreEqual(2, runtime.ValidationBatches.Count);
            Assert.IsTrue(runtime.ValidationBatches.All(batch => batch.Count == 1));
        }

        [TestMethod]
        public void SpeedProfiles_PreserveGlobalJointAndInterpolationPercentSemantics()
        {
            AxisMotionStation station = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime runtime,
                out _);
            Assert.AreEqual(
                MotionStationResult.Success,
                station.SetSpeed(50, 50, 50, type: StationSpeedType.Global));
            Assert.AreEqual(
                MotionStationResult.Success,
                station.SetSpeed(40, 40, 40, axis: 0, type: StationSpeedType.Joint));

            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(CreatePoint(1, 2, 3, 4, 5, 6), StationMoveMode.Go));
            Assert.AreEqual(200d, runtime.Profiles.Single(item => item.Axis == 0).MaxVelocity, 0.000001);
            Assert.AreEqual(150d, runtime.Profiles.Single(item => item.Axis == 1).MaxVelocity, 0.000001);

            runtime.ClearCommands();
            Assert.AreEqual(
                MotionStationResult.Success,
                station.SetSpeed(20, 20, 20, type: StationSpeedType.Move));
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(CreatePoint(6, 5, 4, 3, 2, 1), StationMoveMode.Move));
            Assert.IsNotNull(runtime.CoordinatedMove);
            Assert.AreEqual(100d, runtime.CoordinatedMove.MaxVelocity, 0.000001);
            Assert.AreEqual((ushort)1, runtime.CoordinatedMove.PositionMode);
        }

        [TestMethod]
        public void ContinuousPath_PreservesSegmentsAndMapsConfiguredAxesOnceAtStart()
        {
            AxisMotionStation station = CreateStation(
                new[] { 4, 2, 5, 0, 3, 1 },
                out RecordingMotionRuntime runtime,
                out DataStation definition);
            definition.LookAheadEnabled = true;
            definition.PathError = 0.25;
            definition.LookAheadAccelerationMultiplier = 1800;

            Assert.AreEqual(MotionStationResult.Success,
                station.AddContinuousLine(CreatePoint(1, 2, 3, 4, 5, 6)));
            Assert.AreEqual(MotionStationResult.Success,
                station.AddContinuousArc(
                    CreatePoint(1, 2, 3, 4, 5, 6),
                    CreatePoint(7, 8, 9, 10, 11, 12),
                    CreatePoint(13, 14, 15, 16, 17, 18)));
            Assert.AreEqual(MotionStationResult.Success, station.StartContinuousMove());

            ContinuousPathMoveRequest request = runtime.ContinuousPathMove;
            Assert.IsNotNull(request);
            Assert.AreEqual((ushort)1, request.CoordinateSystem);
            CollectionAssert.AreEqual(
                new ushort[] { 4, 2, 5, 0, 3, 1 },
                request.Axes.ToArray());
            Assert.AreEqual(2, request.Segments.Count);
            Assert.AreEqual(ContinuousPathSegmentType.Line, request.Segments[0].Type);
            Assert.AreEqual(ContinuousPathSegmentType.ArcThreePoint, request.Segments[1].Type);
            Assert.AreEqual(0.2, request.Segments[0].MaxVelocity, 0.000001);
            Assert.AreEqual(0.1, request.Segments[0].AccelerationTime, 0.000001);
            CollectionAssert.AreEqual(
                new double[] { 13, 14, 15, 16, 17, 18 },
                request.Segments[1].TargetPositions.ToArray());
            Assert.IsTrue(request.LookAheadEnabled);
            Assert.AreEqual(0.25, request.PathError, 0.000001);
            Assert.AreEqual(360000, request.LookAheadAcceleration, 0.000001);
            Assert.AreEqual(1, runtime.ValidationBatches.Count);
        }

        [TestMethod]
        public void Release_UsesDeceleratedStopForAllConfiguredAxes()
        {
            AxisMotionStation station = CreateStation(
                new[] { 4, 2, 5, 0, 3, 1 },
                out RecordingMotionRuntime runtime,
                out _);

            Assert.AreEqual(MotionStationResult.Success, station.Release());

            CollectionAssert.AreEqual(
                new ushort[] { 4, 2, 5, 0, 3, 1 },
                runtime.Stops.Select(item => item.Axis).ToArray());
            Assert.IsTrue(runtime.Stops.All(item => item.StopMode == 0));
        }

        [TestMethod]
        public void WaitMoveFinish_UsesConfiguredChannelToleranceAfterControllerReportsDone()
        {
            AxisMotionStation station = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime runtime,
                out DataStation definition);
            definition.PositionTolerances[0] = 0.01;
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(CreatePoint(10, 20, 30, 40, 50, 60), StationMoveMode.Go));

            runtime.Positions[0] = 10.02;
            Assert.AreEqual(
                MotionStationResult.Timeout,
                station.WaitMoveFinish(timeoutMs: 0));

            runtime.Positions[0] = 10.005;
            Assert.AreEqual(
                MotionStationResult.Success,
                station.WaitMoveFinish(timeoutMs: 0));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(4)]
        [DataRow(5)]
        public void SavePoint_RejectsEachCoordinateOutsideItsConfiguredLimit(int channel)
        {
            AxisMotionStation station = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out _,
                out _);
            DataPos point = CreatePoint(0, 0, 0, 0, 0, 0);
            point.PositionLimits[channel] = new[] { -1d, 1d };
            SetCoordinate(point, channel, 1.01);

            Assert.AreEqual(MotionStationResult.InvalidParameter, station.SavePoint(point));

            SetCoordinate(point, channel, 1);
            Assert.AreEqual(MotionStationResult.Success, station.SavePoint(point));
        }

        [TestMethod]
        [DataRow(0, -1)]
        [DataRow(0, 1)]
        [DataRow(1, -1)]
        [DataRow(1, 1)]
        [DataRow(2, -1)]
        [DataRow(2, 1)]
        public void MoveToPoint_AllowsLeavingActiveLimitForEveryMode(
            int modeValue,
            int activeLimitSide)
        {
            AxisMotionStation station = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime runtime,
                out _);
            runtime.Positions[0] = 10 * activeLimitSide;
            runtime.IoStatuses[0] = activeLimitSide > 0
                ? (1u << 1) | (1u << 6)
                : (1u << 2) | (1u << 7);

            MotionStationResult result = station.MoveToPoint(
                CreatePoint(-10 * activeLimitSide, 1, 1, 1, 1, 1),
                (StationMoveMode)modeValue);

            Assert.AreEqual(MotionStationResult.Success, result);
            runtime.ClearCommands();
            runtime.Positions[0] = 5 * activeLimitSide;
            runtime.InPositionStates[0] = false;

            MotionStationStatus status = station.GetStatus();

            Assert.AreEqual(MotionStationState.Moving, status.State);
            Assert.IsFalse(status.HasAlarm);
            Assert.AreEqual(0, runtime.Stops.Count);
        }

        [TestMethod]
        [DataRow(0, -1)]
        [DataRow(0, 1)]
        [DataRow(1, -1)]
        [DataRow(1, 1)]
        [DataRow(2, -1)]
        [DataRow(2, 1)]
        public void MoveToPoint_RejectsContinuingIntoActiveLimitForEveryMode(
            int modeValue,
            int activeLimitSide)
        {
            AxisMotionStation station = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime runtime,
                out _);
            runtime.IoStatuses[0] = activeLimitSide > 0
                ? (1u << 1) | (1u << 6)
                : (1u << 2) | (1u << 7);

            MotionStationResult result = station.MoveToPoint(
                CreatePoint(10 * activeLimitSide, 1, 1, 1, 1, 1),
                (StationMoveMode)modeValue);

            Assert.AreEqual(MotionStationResult.CommandRejected, result);
            Assert.AreEqual(0, runtime.Moves.Count);
            Assert.IsNull(runtime.CoordinatedMove);
            Assert.AreEqual(0, runtime.JogAxes.Count);
        }

        [TestMethod]
        [DataRow(0, 2d)]
        [DataRow(1, 2d)]
        [DataRow(2, -2d)]
        [DataRow(3, 2d)]
        [DataRow(6, 2d)]
        [DataRow(7, -2d)]
        public void WaitMoveFinish_StopsEntireStationForRelevantStationSafetySignal(
            int bit,
            double limitAxisTarget)
        {
            int[] mapping = { 4, 2, 5, 0, 3, 1 };
            AxisMotionStation station = CreateStation(
                mapping,
                out RecordingMotionRuntime runtime,
                out _);
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(
                    CreatePoint(1, limitAxisTarget, 3, 4, 5, 6),
                    StationMoveMode.Go));
            runtime.ClearCommands();
            runtime.Positions[2] = 0;
            runtime.InPositionStates[2] = false;
            runtime.IoStatuses[2] = 1u << bit;

            Assert.AreEqual(
                MotionStationResult.CommandRejected,
                station.WaitMoveFinish(timeoutMs: 0));
            CollectionAssert.AreEqual(
                mapping.Select(item => (ushort)item).ToArray(),
                runtime.Stops.Select(item => item.Axis).ToArray());
            Assert.IsTrue(runtime.Stops.All(item => item.StopMode == 0));
        }

        [TestMethod]
        public void GetStatus_StopsEntireStationWhenNormalOffsetMoveHitsPositiveLimit()
        {
            int[] mapping = { 4, 2, 5, 0, 3, 1 };
            AxisMotionStation station = CreateStation(
                mapping,
                out RecordingMotionRuntime runtime,
                out _);
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveOffset(-1, new[] { 1d, 2d, 3d }));
            runtime.ClearCommands();
            runtime.Positions[2] = 0;
            runtime.InPositionStates[2] = false;
            runtime.IoStatuses[2] = 1u << 6;

            MotionStationStatus stationStatus = station.GetStatus();

            Assert.AreEqual(MotionStationState.Faulted, stationStatus.State);
            Assert.IsTrue(stationStatus.HasAlarm);
            Assert.AreEqual(1, stationStatus.WarningAxis);
            StringAssert.Contains(stationStatus.LastError, "正软限位");
            CollectionAssert.AreEqual(
                mapping.Select(item => (ushort)item).ToArray(),
                runtime.Stops.Select(item => item.Axis).ToArray());
            Assert.IsTrue(runtime.Stops.All(item => item.StopMode == 0));
        }

        [TestMethod]
        public void WaitMoveFinish_StopsEntireStationWhenLimitAxisAlreadyReportsStopped()
        {
            int[] mapping = { 4, 2, 5, 0, 3, 1 };
            AxisMotionStation station = CreateStation(
                mapping,
                out RecordingMotionRuntime runtime,
                out _);
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(CreatePoint(1, 2, 3, 4, 5, 6), StationMoveMode.Go));
            runtime.ClearCommands();
            runtime.Positions[2] = 0;
            runtime.IoStatuses[2] = 1u << 6;

            Assert.AreEqual(
                MotionStationResult.CommandRejected,
                station.WaitMoveFinish(timeoutMs: 0));
            CollectionAssert.AreEqual(
                mapping.Select(item => (ushort)item).ToArray(),
                runtime.Stops.Select(item => item.Axis).ToArray());
            Assert.IsTrue(runtime.Stops.All(item => item.StopMode == 0));
        }

        [TestMethod]
        public void WaitMoveFinish_AllowsCompletedTargetAtActiveLimit()
        {
            AxisMotionStation station = CreateStation(
                new[] { 4, 2, 5, 0, 3, 1 },
                out RecordingMotionRuntime runtime,
                out _);
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(CreatePoint(1, 2, 3, 4, 5, 6), StationMoveMode.Go));
            runtime.ClearCommands();
            runtime.IoStatuses[2] = (1u << 1) | (1u << 6);

            Assert.AreEqual(
                MotionStationResult.Success,
                station.WaitMoveFinish(timeoutMs: 0));
            Assert.AreEqual(0, runtime.Stops.Count);
        }

        [TestMethod]
        public void MoveTrayPoint_UsesStationCoordinatedMove()
        {
            AxisMotionStation station = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime runtime,
                out _);
            DataPos point = CreatePoint(11, 12, 13, 14, 15, 16);

            MotionStationResult result = station.MoveTrayPoint(2, 3, point);

            Assert.AreEqual(MotionStationResult.Success, result);
            Assert.IsNotNull(runtime.CoordinatedMove);
            Assert.AreEqual((ushort)1, runtime.CoordinatedMove.PositionMode);
            CollectionAssert.AreEqual(
                point.GetAllValues().ToArray(),
                runtime.CoordinatedMove.Positions.ToArray());
            Assert.AreEqual(0, runtime.Moves.Count);
        }

        [TestMethod]
        public void HomeAxisMotionAndLimitSearch_DoNotArmEntireStationFaultStop()
        {
            AxisMotionStation homeStation = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime homeRuntime,
                out _);
            Assert.AreEqual(MotionStationResult.Success, homeStation.Home(0, wait: false));
            AssertNoEntireStationFaultStop(homeStation, homeRuntime);

            AxisMotionStation axisStation = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime axisRuntime,
                out _);
            Assert.AreEqual(
                MotionStationResult.Success,
                axisStation.AxisMotion(0, 10, StationAxisMoveMode.Relative));
            AssertNoEntireStationFaultStop(axisStation, axisRuntime);

            AxisMotionStation limitStation = CreateStation(
                new[] { 0, 1, 2, 3, 4, 5 },
                out RecordingMotionRuntime limitRuntime,
                out _);
            limitRuntime.IoStatuses[0] = 1u << 1;
            Assert.AreEqual(
                MotionStationResult.Success,
                limitStation.MoveOffset(-1, new[] { 10001d }));
            AssertNoEntireStationFaultStop(limitStation, limitRuntime);
        }

        private static AxisMotionStation CreateStation(
            IReadOnlyList<int> channelToPhysicalAxis,
            out RecordingMotionRuntime runtime,
            out DataStation definition,
            bool initialize = true)
        {
            var store = new CardConfigStore();
            var card = new Card();
            var controlCard = new ControlCard
            {
                cardHead = new CardHead
                {
                    AxisCount = 6,
                    InputCount = 16,
                    OutputCount = 16
                }
            };
            for (int i = 0; i < 6; i++)
            {
                controlCard.axis.Add(new Axis
                {
                    AxisName = "A" + i,
                    AxisNum = i,
                    PulseToMM = 1000,
                    HomeMethod = i == 0 ? -1 : 20 + i,
                    HomeSpeed = "100",
                    SpeedMax = 1000,
                    AccMax = 0.2,
                    DecMax = 0.25
                });
            }
            card.controlCards.Add(controlCard);
            store.SetCard(card);

            definition = new DataStation(false)
            {
                Name = "测试轴工站",
                Type = StationType.Axis,
                CoordinateSystem = 1
            };
            for (int channel = 0; channel < 6; channel++)
            {
                definition.dataAxis.axisConfigs[channel].CardNum = "0";
                definition.dataAxis.axisConfigs[channel].AxisName =
                    "A" + channelToPhysicalAxis[channel];
            }
            runtime = new RecordingMotionRuntime();
            var station = new AxisMotionStation(runtime, store, definition);
            if (initialize)
            {
                Assert.AreEqual(MotionStationResult.Success, station.Initialize());
            }
            return station;
        }

        private static DataPos CreatePoint(
            double x,
            double y,
            double z,
            double u,
            double v,
            double w)
        {
            return new DataPos(1)
            {
                Name = "P1",
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

        private static void SetCoordinate(DataPos point, int channel, double value)
        {
            switch (channel)
            {
                case 0:
                    point.X = value;
                    break;
                case 1:
                    point.Y = value;
                    break;
                case 2:
                    point.Z = value;
                    break;
                case 3:
                    point.U = value;
                    break;
                case 4:
                    point.V = value;
                    break;
                case 5:
                    point.W = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }

        private static void AssertNoEntireStationFaultStop(
            AxisMotionStation station,
            RecordingMotionRuntime runtime)
        {
            runtime.ClearCommands();
            runtime.InPositionStates[0] = false;
            runtime.IoStatuses[1] = 1u;

            station.GetStatus();

            Assert.AreEqual(0, runtime.Stops.Count);
        }

        private sealed class RecordingMotionRuntime : IMotionRuntime
        {
            private HashSet<string> activeValidation;

            public bool IsCardInitialized { get; set; } = true;
            public int StationCount => 0;
            public Dictionary<ushort, double> Positions { get; } = new Dictionary<ushort, double>();
            public List<RecordedMove> Moves { get; } = new List<RecordedMove>();
            public List<RecordedProfile> Profiles { get; } = new List<RecordedProfile>();
            public List<ushort> HomeStarts { get; } = new List<ushort>();
            public List<ushort> CleanedAxes { get; } = new List<ushort>();
            public List<RecordedHomeProfile> HomeProfiles { get; } = new List<RecordedHomeProfile>();
            public List<RecordedStop> Stops { get; } = new List<RecordedStop>();
            public List<ushort> JogAxes { get; } = new List<ushort>();
            public Dictionary<ushort, bool> InPositionStates { get; }
                = new Dictionary<ushort, bool>();
            public Dictionary<ushort, uint> IoStatuses { get; }
                = new Dictionary<ushort, uint>();
            public List<List<AxisCommandRequest>> ValidationBatches { get; }
                = new List<List<AxisCommandRequest>>();
            public CoordinatedLinearMoveRequest CoordinatedMove { get; private set; }
            public ContinuousPathMoveRequest ContinuousPathMove { get; private set; }

            public void InitCardType()
            {
            }

            public bool InitCard()
            {
                IsCardInitialized = true;
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
                throw new NotSupportedException();
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
                return MotionStationResult.Success;
            }

            public MotionStationResult GetStationPosition(
                short station,
                short tool,
                out DataPos position)
            {
                position = null;
                return MotionStationResult.Success;
            }

            public MotionStationResult SaveStationPoint(short station, DataPos point)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult CreateStationTray(short station, int trayId,
                int rowCount, int columnCount, IReadOnlyList<DataPos> referencePoints)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationTrayPoint(short station, int trayId,
                int position, DataPos calculatedPoint)
            {
                return MotionStationResult.Success;
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
                HomeProfiles.Add(new RecordedHomeProfile(axis, dir, homeMode));
            }

            public void StartHome(ushort card, ushort axis)
            {
                Consume(card, axis, AxisCommandKind.Home);
                HomeStarts.Add(axis);
                Positions[axis] = 0;
            }

            public void CleanPos(ushort card, ushort axis)
            {
                CleanedAxes.Add(axis);
                Positions[axis] = 0;
            }

            public double GetAxisPos(ushort card, ushort axis)
            {
                return Positions.TryGetValue(axis, out double value) ? value : 0;
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
                Profiles.Add(new RecordedProfile(axis, maxVel, acc, dec));
            }

            public void Mov(ushort card, ushort axis, double distance, ushort positionMode, bool wait)
            {
                Consume(card, axis, AxisCommandKind.Motion);
                Moves.Add(new RecordedMove(axis, distance, positionMode));
                Positions[axis] = positionMode == 1
                    ? distance
                    : GetAxisPos(card, axis) + distance;
            }

            public void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request)
            {
                for (int i = 0; i < request.Axes.Count; i++)
                {
                    Consume(request.Card, request.Axes[i], AxisCommandKind.Motion);
                    Positions[request.Axes[i]] = request.PositionMode == 1
                        ? request.Positions[i]
                        : GetAxisPos(request.Card, request.Axes[i]) + request.Positions[i];
                }
                CoordinatedMove = request;
            }

            public bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem)
            {
                return true;
            }

            public void StopCoordinatedLinear(ushort card, ushort coordinateSystem, ushort stopMode)
            {
            }

            public void MoveContinuousPath(ContinuousPathMoveRequest request)
            {
                ContinuousPathMove = request;
            }
            public bool IsContinuousPathDone(ushort card, ushort coordinateSystem) => true;
            public void StopContinuousPath(ushort card, ushort coordinateSystem, ushort stopMode) { }

            public void Jog(ushort card, ushort axis, ushort direction)
            {
                Consume(card, axis, AxisCommandKind.Motion);
                JogAxes.Add(axis);
            }

            public void StopOneAxis(ushort card, ushort axis, ushort stopMode)
            {
                Stops.Add(new RecordedStop(axis, stopMode));
            }

            public void StopConnect()
            {
                IsCardInitialized = false;
            }

            public bool HomeStatus(ushort card, ushort axis)
            {
                return true;
            }

            public bool GetInPos(ushort card, ushort axis)
            {
                return !InPositionStates.TryGetValue(axis, out bool inPosition) || inPosition;
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
                return IoStatuses.TryGetValue(axis, out uint ioStatus) ? ioStatus : 0;
            }

            public ushort GetAxisAlarmCode(ushort card, ushort axis)
            {
                return 0;
            }

            public IDisposable ValidateAxesForCommand(IReadOnlyCollection<AxisCommandRequest> requests)
            {
                if (activeValidation != null)
                {
                    throw new InvalidOperationException("测试运行时不允许嵌套校验作用域。");
                }
                List<AxisCommandRequest> batch = requests.ToList();
                ValidationBatches.Add(batch);
                activeValidation = new HashSet<string>(batch.Select(BuildValidationKey));
                return new ActionScope(() => activeValidation = null);
            }

            public void ClearCommands()
            {
                Moves.Clear();
                Profiles.Clear();
                Stops.Clear();
                JogAxes.Clear();
                ValidationBatches.Clear();
                CoordinatedMove = null;
            }

            private void Consume(ushort card, ushort axis, AxisCommandKind kind)
            {
                string key = BuildValidationKey(new AxisCommandRequest(card, axis, kind));
                if (activeValidation == null || !activeValidation.Remove(key))
                {
                    throw new InvalidOperationException($"运动命令未通过当前安全校验:{key}");
                }
            }

            private static string BuildValidationKey(AxisCommandRequest request)
            {
                return request.Card + ":" + request.Axis + ":" + request.Kind;
            }
        }

        private sealed class RecordedMove
        {
            public RecordedMove(ushort axis, double target, ushort positionMode)
            {
                Axis = axis;
                Target = target;
                PositionMode = positionMode;
            }

            public ushort Axis { get; }
            public double Target { get; }
            public ushort PositionMode { get; }
        }

        private sealed class RecordedProfile
        {
            public RecordedProfile(
                ushort axis,
                double maxVelocity,
                double accelerationTime,
                double decelerationTime)
            {
                Axis = axis;
                MaxVelocity = maxVelocity;
                AccelerationTime = accelerationTime;
                DecelerationTime = decelerationTime;
            }

            public ushort Axis { get; }
            public double MaxVelocity { get; }
            public double AccelerationTime { get; }
            public double DecelerationTime { get; }
        }

        private sealed class RecordedStop
        {
            public RecordedStop(ushort axis, ushort stopMode)
            {
                Axis = axis;
                StopMode = stopMode;
            }

            public ushort Axis { get; }
            public ushort StopMode { get; }
        }

        private sealed class RecordedHomeProfile
        {
            public RecordedHomeProfile(ushort axis, ushort direction, ushort homeMethod)
            {
                Axis = axis;
                Direction = direction;
                HomeMethod = homeMethod;
            }

            public ushort Axis { get; }
            public ushort Direction { get; }
            public ushort HomeMethod { get; }
        }

        private sealed class ActionScope : IDisposable
        {
            private Action release;

            public ActionScope(Action release)
            {
                this.release = release;
            }

            public void Dispose()
            {
                Action action = release;
                release = null;
                action?.Invoke();
            }
        }
    }
}
