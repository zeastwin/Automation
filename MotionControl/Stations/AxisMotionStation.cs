using System;
// 模块：运动控制 / 轴工站。
// 职责范围：按 3.0 Station 的六通道语义，将当前工站直接驱动到统一 IMotionRuntime。
// 安全边界：构造阶段不访问硬件；每次回原或运动都在 ValidateAxesForCommand 作用域内下发。

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Automation.MotionControl
{
    internal sealed class AxisMotionStation : IMotionStation
    {
        private const int ChannelCount = 6;
        private const int PollIntervalMilliseconds = 20;
        private const double LimitSearchOffset = 10000;
        private const uint AxisAlarmMask = 1u;
        private const uint PositiveHardLimitMask = 1u << 1;
        private const uint NegativeHardLimitMask = 1u << 2;
        private const uint EmergencyStopMask = 1u << 3;
        private const uint PositiveSoftLimitMask = 1u << 6;
        private const uint NegativeSoftLimitMask = 1u << 7;
        private const uint UnconditionalStationMoveFaultMask = AxisAlarmMask | EmergencyStopMask;
        private const uint PositiveLimitMask = PositiveHardLimitMask | PositiveSoftLimitMask;
        private const uint NegativeLimitMask = NegativeHardLimitMask | NegativeSoftLimitMask;

        private readonly IMotionRuntime motion;
        private readonly CardConfigStore cardStore;
        private readonly DataStation configuration;
        private readonly object stateLock = new object();
        private readonly SpeedProfile globalSpeed = new SpeedProfile();
        private readonly SpeedProfile moveSpeed = new SpeedProfile();
        private readonly SpeedProfile[] jointSpeeds =
        {
            new SpeedProfile(), new SpeedProfile(), new SpeedProfile(),
            new SpeedProfile(), new SpeedProfile(), new SpeedProfile()
        };
        private readonly MotionStationStatus status = new MotionStationStatus();
        private readonly HashSet<short> pendingHomeCleanChannels = new HashSet<short>();
        private readonly List<ContinuousPathSegment> queuedContinuousSegments =
            new List<ContinuousPathSegment>();

        private AxisBinding[] bindings = Array.Empty<AxisBinding>();
        private double[] lastTarget;
        private bool[] lastTargetChannels;
        private int[] activeMoveDirections;
        private bool coordinatedMoveActive;
        private bool continuousMoveActive;
        private bool stationMoveActive;
        private bool initialized;

        public AxisMotionStation(
            IMotionRuntime motion,
            CardConfigStore cardStore,
            DataStation configuration)
        {
            this.motion = motion ?? throw new ArgumentNullException(nameof(motion));
            this.cardStore = cardStore ?? throw new ArgumentNullException(nameof(cardStore));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public MotionStationResult Initialize()
        {
            if (configuration.Type != StationType.Axis)
            {
                return SetFailure(
                    MotionStationResult.InvalidConfiguration,
                    $"工站{configuration.Name ?? "<未命名>"}不是轴工站。");
            }
            if (!cardStore.TryValidateAllAxes(out List<string> cardErrors))
            {
                return SetFailure(
                    MotionStationResult.InvalidConfiguration,
                    string.Join("；", cardErrors));
            }
            if (!cardStore.TryValidateStations(new[] { configuration }, out List<string> stationErrors))
            {
                return SetFailure(
                    MotionStationResult.InvalidConfiguration,
                    string.Join("；", stationErrors));
            }
            if (!TryBuildBindings(out AxisBinding[] resolved, out string error))
            {
                return SetFailure(MotionStationResult.InvalidConfiguration, error);
            }
            if (!motion.IsCardInitialized)
            {
                return SetFailure(MotionStationResult.NotConnected, "雷赛总线卡尚未初始化。");
            }

            lock (stateLock)
            {
                bindings = resolved;
                initialized = true;
                coordinatedMoveActive = false;
                continuousMoveActive = false;
                stationMoveActive = false;
                queuedContinuousSegments.Clear();
                pendingHomeCleanChannels.Clear();
                lastTarget = null;
                lastTargetChannels = null;
                activeMoveDirections = null;
                InitializeSpeedProfiles();
                status.State = MotionStationState.Idle;
                status.LastError = string.Empty;
            }
            return MotionStationResult.Success;
        }

        public MotionStationResult Release()
        {
            MotionStationResult result = MotionStationResult.Success;
            if (initialized && motion.IsCardInitialized)
            {
                result = Stop(false);
            }
            lock (stateLock)
            {
                initialized = false;
                bindings = Array.Empty<AxisBinding>();
                coordinatedMoveActive = false;
                continuousMoveActive = false;
                stationMoveActive = false;
                queuedContinuousSegments.Clear();
                pendingHomeCleanChannels.Clear();
                lastTarget = null;
                lastTargetChannels = null;
                activeMoveDirections = null;
                status.State = MotionStationState.Uninitialized;
                if (result == MotionStationResult.Success)
                {
                    status.LastError = string.Empty;
                }
            }
            return result;
        }

        public MotionStationResult Home(short axis = -1, bool wait = true, bool group = false)
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (axis >= ChannelCount || axis < -1)
            {
                return SetFailure(MotionStationResult.InvalidParameter, $"工站通道超出范围:{axis}");
            }

            if (axis >= 0)
            {
                AxisBinding binding = FindBinding(axis);
                if (binding == null)
                {
                    return SetFailure(MotionStationResult.InvalidParameter, $"工站通道{axis}未配置物理轴。");
                }
                return StartHome(new[] { binding }, wait, axis);
            }

            List<AxisBinding> remaining = bindings.OrderBy(item => item.Channel).ToList();
            if (!group)
            {
                foreach (AxisBinding priority in ResolveHomeSequence())
                {
                    MotionStationResult priorityResult = StartHome(
                        new[] { priority },
                        true,
                        priority.Channel);
                    if (priorityResult != MotionStationResult.Success)
                    {
                        return priorityResult;
                    }
                    remaining.RemoveAll(item => item.Channel == priority.Channel);
                }
            }
            return remaining.Count == 0
                ? MotionStationResult.Success
                : StartHome(remaining, wait, -1);
        }

        public MotionStationResult SetSpeed(
            double velocity,
            double acceleration,
            double deceleration,
            short axis = -1,
            StationSpeedType type = StationSpeedType.Joint)
        {
            MotionStationResult ready = EnsureInitialized();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (!TryNormalizePercent(velocity, out double normalizedVelocity)
                || !TryNormalizePercent(acceleration, out double normalizedAcceleration)
                || !TryNormalizePercent(deceleration, out double normalizedDeceleration)
                || !Enum.IsDefined(typeof(StationSpeedType), type))
            {
                return SetFailure(MotionStationResult.InvalidParameter, "速度、加速度或减速度百分比无效。");
            }

            lock (stateLock)
            {
                switch (type)
                {
                    case StationSpeedType.Global:
                        UpdateSpeed(globalSpeed, normalizedVelocity, normalizedAcceleration, normalizedDeceleration);
                        if (normalizedVelocity > 0)
                        {
                            configuration.ManualSpeedPercent = normalizedVelocity;
                        }
                        break;
                    case StationSpeedType.Joint:
                        if (axis >= ChannelCount || axis < -1)
                        {
                            return SetFailure(
                                MotionStationResult.InvalidParameter,
                                $"工站通道超出范围:{axis}");
                        }
                        if (axis >= 0)
                        {
                            UpdateSpeed(
                                jointSpeeds[axis],
                                normalizedVelocity,
                                normalizedAcceleration,
                                normalizedDeceleration);
                        }
                        else
                        {
                            foreach (SpeedProfile speed in jointSpeeds)
                            {
                                UpdateSpeed(
                                    speed,
                                    normalizedVelocity,
                                    normalizedAcceleration,
                                    normalizedDeceleration);
                            }
                        }
                        break;
                    case StationSpeedType.Move:
                        UpdateSpeed(moveSpeed, normalizedVelocity, normalizedAcceleration, normalizedDeceleration);
                        break;
                }
                status.LastError = string.Empty;
            }
            return MotionStationResult.Success;
        }

        public MotionStationResult MoveToPoint(
            DataPos point,
            StationMoveMode mode,
            bool[] disabledAxes = null,
            short tool = 0)
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (point == null || !Enum.IsDefined(typeof(StationMoveMode), mode)
                || (disabledAxes != null && disabledAxes.Length < ChannelCount))
            {
                return SetFailure(MotionStationResult.InvalidParameter, "点位、运动模式或禁用轴列表无效。");
            }
            if (!point.IsMotionReady)
            {
                return SetFailure(
                    MotionStationResult.InvalidParameter,
                    $"点位P{point.Index}尚未示教或名称为空，不能执行运动。");
            }
            // 3.0 中点位组禁用是有意的软屏蔽：调用成功，但不向控制器发送任何命令。
            if (!point.Enabled)
            {
                ClearError();
                return MotionStationResult.Success;
            }

            double[] positions = point.GetAllValues().ToArray();
            List<AxisBinding> activeBindings = bindings
                .Where(item => disabledAxes == null || !disabledAxes[item.Channel])
                .OrderBy(item => item.Channel)
                .ToList();
            if (activeBindings.Count == 0)
            {
                ClearError();
                return MotionStationResult.Success;
            }
            foreach (AxisBinding binding in activeBindings)
            {
                double target = positions[binding.Channel];
                if (!IsFinite(target) || !IsWithinPointLimit(point, binding.Channel, target))
                {
                    return SetFailure(
                        MotionStationResult.InvalidParameter,
                        $"点位P{point.Index}通道{binding.Channel}坐标无效或超出点位限值:{target}");
                }
            }

            try
            {
                AxisCommandRequest[] requests = activeBindings
                    .Select(CreateMotionRequest)
                    .ToArray();
                int[] moveDirections;
                using (motion.ValidateAxesForCommand(requests))
                {
                    moveDirections = ResolveMoveDirections(activeBindings, positions, mode);
                    EnsureStationMoveAllowed(moveDirections);
                    switch (mode)
                    {
                        case StationMoveMode.Go:
                            foreach (AxisBinding binding in activeBindings)
                            {
                                ApplyJointProfile(binding);
                                motion.Mov(
                                    binding.Card,
                                    binding.Axis,
                                    positions[binding.Channel],
                                    1,
                                    false);
                            }
                            break;
                        case StationMoveMode.Move:
                            MotionStationResult moveResult = StartCoordinatedMove(activeBindings, positions);
                            if (moveResult != MotionStationResult.Success)
                            {
                                return moveResult;
                            }
                            break;
                        case StationMoveMode.Jog:
                            foreach (AxisBinding binding in activeBindings)
                            {
                                ApplyJointProfile(binding);
                                motion.Jog(
                                    binding.Card,
                                    binding.Axis,
                                    positions[binding.Channel] > 0 ? (ushort)1 : (ushort)0);
                            }
                            break;
                    }
                }

                bool tracksTarget = mode != StationMoveMode.Jog;
                lock (stateLock)
                {
                    coordinatedMoveActive = mode == StationMoveMode.Move;
                    continuousMoveActive = false;
                    stationMoveActive = true;
                    lastTarget = tracksTarget ? (double[])positions.Clone() : null;
                    lastTargetChannels = tracksTarget
                        ? BuildActiveChannelFlags(activeBindings)
                        : null;
                    activeMoveDirections = moveDirections;
                    status.State = MotionStationState.Moving;
                    status.LastError = string.Empty;
                }
                return MotionStationResult.Success;
            }
            catch (Exception ex)
            {
                return MapCommandFailure(ex);
            }
        }

        public MotionStationResult MoveOffset(
            int basePointIndex,
            IReadOnlyList<double> offsets,
            StationMoveMode mode = StationMoveMode.Go)
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (offsets == null || offsets.Count == 0 || offsets.Count > ChannelCount
                || offsets.Any(value => !IsFinite(value))
                || !Enum.IsDefined(typeof(StationMoveMode), mode))
            {
                return SetFailure(MotionStationResult.InvalidParameter, "偏移量或运动模式无效。");
            }

            if (offsets.Any(value => Math.Abs(value) > LimitSearchOffset))
            {
                return MoveUntilLimit(offsets);
            }

            DataPos basePoint = FindPoint(basePointIndex);
            if (basePoint == null)
            {
                MotionStationResult currentResult = GetCurrentPosition(0, out basePoint);
                if (currentResult != MotionStationResult.Success)
                {
                    return currentResult;
                }
            }
            double[] targetValues = basePoint.GetAllValues().ToArray();
            for (int i = 0; i < offsets.Count; i++)
            {
                targetValues[i] += offsets[i];
            }
            DataPos target = CreateRuntimePoint(targetValues, "系统偏移点");
            return MoveToPoint(target, mode);
        }

        public MotionStationResult AxisMotion(
            short axis,
            double offset,
            StationAxisMoveMode mode = StationAxisMoveMode.Relative,
            short tool = 0)
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (axis < 0 || axis >= ChannelCount || !IsFinite(offset)
                || !Enum.IsDefined(typeof(StationAxisMoveMode), mode))
            {
                return SetFailure(MotionStationResult.InvalidParameter, "单轴运动参数无效。");
            }
            AxisBinding binding = FindBinding(axis);
            if (binding == null)
            {
                return SetFailure(MotionStationResult.InvalidParameter, $"工站通道{axis}未配置物理轴。");
            }

            try
            {
                using (motion.ValidateAxesForCommand(new[] { CreateMotionRequest(binding) }))
                {
                    ApplyJointProfile(binding);
                    double target = offset;
                    ushort positionMode = 0;
                    double currentPosition = 0;
                    if (mode == StationAxisMoveMode.Absolute)
                    {
                        positionMode = 1;
                    }
                    else if (mode == StationAxisMoveMode.RelativeByEncoder)
                    {
                        currentPosition = motion.GetAxisPos(binding.Card, binding.Axis);
                        target = currentPosition + offset;
                        positionMode = 1;
                    }
                    else
                    {
                        currentPosition = motion.GetAxisPos(binding.Card, binding.Axis);
                    }
                    motion.Mov(binding.Card, binding.Axis, target, positionMode, false);

                    double absoluteTarget = positionMode == 1
                        ? target
                        : currentPosition + offset;
                    lock (stateLock)
                    {
                        coordinatedMoveActive = false;
                        continuousMoveActive = false;
                        stationMoveActive = false;
                        lastTarget = new double[ChannelCount];
                        lastTarget[axis] = absoluteTarget;
                        lastTargetChannels = new bool[ChannelCount];
                        lastTargetChannels[axis] = true;
                        activeMoveDirections = null;
                        status.State = MotionStationState.Moving;
                        status.LastError = string.Empty;
                    }
                }
                return MotionStationResult.Success;
            }
            catch (Exception ex)
            {
                return MapCommandFailure(ex);
            }
        }

        public MotionStationResult WaitMoveFinish(
            bool isHome = false,
            int axis = -1,
            int timeoutMs = 120000)
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (axis < -1 || axis >= ChannelCount || timeoutMs < 0)
            {
                return SetFailure(MotionStationResult.InvalidParameter, "等待运动完成参数无效。");
            }
            AxisBinding selected = axis >= 0 ? FindBinding(axis) : null;
            if (axis >= 0 && selected == null)
            {
                return SetFailure(MotionStationResult.InvalidParameter, $"工站通道{axis}未配置物理轴。");
            }
            AxisBinding[] waitingBindings = selected == null
                ? bindings.ToArray()
                : new[] { selected };

            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (true)
                {
                    if (TryDetectAndStopStationMoveFault(out MotionStationResult faultResult))
                    {
                        return faultResult;
                    }

                    bool done;
                    bool coordinated;
                    bool continuous;
                    lock (stateLock)
                    {
                        coordinated = coordinatedMoveActive;
                        continuous = continuousMoveActive;
                    }
                    if (axis < 0 && continuous)
                    {
                        AxisBinding first = bindings[0];
                        done = motion.IsContinuousPathDone(first.Card, configuration.CoordinateSystem);
                    }
                    else if (axis < 0 && coordinated)
                    {
                        AxisBinding first = bindings[0];
                        done = motion.IsCoordinatedLinearDone(first.Card, configuration.CoordinateSystem);
                    }
                    else
                    {
                        done = waitingBindings.All(item =>
                            motion.GetInPos(item.Card, item.Axis)
                            && (!isHome || motion.HomeStatus(item.Card, item.Axis)));
                    }
                    if (done && IsLastTargetInPosition(axis))
                    {
                        if (isHome)
                        {
                            CleanCompletedHomePositions(waitingBindings);
                        }
                        lock (stateLock)
                        {
                            if (axis < 0)
                            {
                                coordinatedMoveActive = false;
                                continuousMoveActive = false;
                                stationMoveActive = false;
                                activeMoveDirections = null;
                            }
                            status.State = MotionStationState.Idle;
                            status.LastError = string.Empty;
                        }
                        return MotionStationResult.Success;
                    }
                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        return SetFailure(
                            MotionStationResult.Timeout,
                            $"等待工站运动完成超时:{timeoutMs}ms");
                    }
                    Thread.Sleep(PollIntervalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                return MapCommandFailure(ex);
            }
        }

        public MotionStationResult GetCurrentPosition(short tool, out DataPos position)
        {
            position = CreateRuntimePoint(new double[ChannelCount], "当前位置");
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            try
            {
                double[] values = new double[ChannelCount];
                foreach (AxisBinding binding in bindings)
                {
                    values[binding.Channel] = motion.GetAxisPos(binding.Card, binding.Axis);
                }
                position = CreateRuntimePoint(values, "当前位置");
                status.SetPosition(values);
                ClearError();
                return MotionStationResult.Success;
            }
            catch (Exception ex)
            {
                return MapCommandFailure(ex);
            }
        }

        public MotionStationResult SavePoint(DataPos point)
        {
            if (point == null || point.Index < 0 || !point.IsMotionReady
                || point.GetAllValues().Any(value => !IsFinite(value)))
            {
                return SetFailure(MotionStationResult.InvalidParameter, "轴工站保存点位参数无效。");
            }
            double[] positions = point.GetAllValues().ToArray();
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                if (!IsWithinPointLimit(point, channel, positions[channel]))
                {
                    return SetFailure(
                        MotionStationResult.InvalidParameter,
                        $"点位P{point.Index}通道{channel}坐标超出点位限值:{positions[channel]}");
                }
            }
            ClearError();
            return MotionStationResult.Success;
        }

        public MotionStationResult CreateTray(
            int trayId,
            int rowCount,
            int columnCount,
            IReadOnlyList<DataPos> referencePoints)
        {
            if (trayId < 0 || rowCount <= 0 || columnCount <= 0
                || referencePoints == null || referencePoints.Count < 4)
            {
                return SetFailure(MotionStationResult.InvalidParameter, "轴工站料盘参数无效。");
            }
            return MotionStationResult.Success;
        }

        public MotionStationResult MoveTrayPoint(int trayId, int position, DataPos calculatedPoint)
        {
            if (trayId < 0 || position < 0 || calculatedPoint == null)
            {
                return SetFailure(MotionStationResult.InvalidParameter, "轴工站料盘点参数无效。");
            }
            return MoveToPoint(calculatedPoint, StationMoveMode.Move);
        }

        public MotionStationResult ClearContinuousPath()
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            lock (stateLock)
            {
                if (continuousMoveActive)
                {
                    return SetFailure(MotionStationResult.Busy, "连续轨迹正在运行，不能清除待执行轨迹。");
                }
                queuedContinuousSegments.Clear();
                status.LastError = string.Empty;
            }
            return MotionStationResult.Success;
        }

        public MotionStationResult AddContinuousLine(DataPos target)
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (!TryValidateContinuousPoint(target, "直线目标点", out string error))
            {
                return SetFailure(MotionStationResult.InvalidParameter, error);
            }
            if (!target.Enabled)
            {
                ClearError();
                return MotionStationResult.Success;
            }
            lock (stateLock)
            {
                if (continuousMoveActive)
                {
                    return SetFailure(MotionStationResult.Busy, "连续轨迹正在运行，不能继续添加轨迹段。");
                }
                queuedContinuousSegments.Add(CreateContinuousSegment(
                    ContinuousPathSegmentType.Line, null, null, target, 0, 0, -1));
                status.LastError = string.Empty;
            }
            return MotionStationResult.Success;
        }

        public MotionStationResult AddContinuousArc(DataPos start, DataPos middle, DataPos target)
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (!TryValidateContinuousPoint(start, "三点圆弧起点", out string error)
                || !TryValidateContinuousPoint(middle, "三点圆弧中间点", out error)
                || !TryValidateContinuousPoint(target, "三点圆弧目标点", out error))
            {
                return SetFailure(MotionStationResult.InvalidParameter, error);
            }
            if (!start.Enabled || !middle.Enabled || !target.Enabled)
            {
                ClearError();
                return MotionStationResult.Success;
            }
            lock (stateLock)
            {
                if (continuousMoveActive)
                {
                    return SetFailure(MotionStationResult.Busy, "连续轨迹正在运行，不能继续添加轨迹段。");
                }
                queuedContinuousSegments.Add(CreateContinuousSegment(
                    ContinuousPathSegmentType.ArcThreePoint,
                    start,
                    middle,
                    target,
                    0,
                    0,
                    -1));
                status.LastError = string.Empty;
            }
            return MotionStationResult.Success;
        }

        public MotionStationResult AddContinuousArcCenterRadius(
            DataPos target,
            DataPos center,
            double radius,
            int circle,
            bool counterClockwise)
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (!TryValidateContinuousPoint(target, "圆弧目标点", out string error)
                || (radius <= 0 && !TryValidateContinuousPoint(center, "圆心点", out error))
                || !IsFinite(radius))
            {
                return SetFailure(MotionStationResult.InvalidParameter,
                    error ?? "圆弧半径必须是有限数。");
            }
            if (!target.Enabled || (radius <= 0 && !center.Enabled))
            {
                ClearError();
                return MotionStationResult.Success;
            }
            lock (stateLock)
            {
                if (continuousMoveActive)
                {
                    return SetFailure(MotionStationResult.Busy, "连续轨迹正在运行，不能继续添加轨迹段。");
                }
                queuedContinuousSegments.Add(CreateContinuousSegment(
                    radius > 0
                        ? ContinuousPathSegmentType.ArcRadius
                        : ContinuousPathSegmentType.ArcCenter,
                    null,
                    center,
                    target,
                    radius,
                    counterClockwise ? (ushort)1 : (ushort)0,
                    circle));
                status.LastError = string.Empty;
            }
            return MotionStationResult.Success;
        }

        public MotionStationResult StartContinuousMove()
        {
            MotionStationResult ready = EnsureOperational();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }

            ContinuousPathSegment[] queued;
            lock (stateLock)
            {
                if (continuousMoveActive)
                {
                    return MotionStationResult.Busy;
                }
                if (queuedContinuousSegments.Count == 0)
                {
                    return SetFailure(MotionStationResult.InvalidParameter, "尚未添加连续轨迹段。");
                }
                queued = queuedContinuousSegments.ToArray();
            }

            List<AxisBinding> activeBindings = bindings.OrderBy(item => item.Channel).ToList();
            ushort card = activeBindings[0].Card;
            if (activeBindings.Any(item => item.Card != card))
            {
                return SetFailure(
                    MotionStationResult.InvalidConfiguration,
                    "连续轨迹要求六通道位于同一张雷赛总线卡。");
            }

            double[] finalTarget = queued[queued.Length - 1].TargetPositions.ToArray();
            try
            {
                int[] moveDirections;
                using (motion.ValidateAxesForCommand(
                    activeBindings.Select(CreateMotionRequest).ToArray()))
                {
                    moveDirections = ResolveMoveDirections(
                        activeBindings, finalTarget, StationMoveMode.Move);
                    EnsureStationMoveAllowed(moveDirections);
                    motion.MoveContinuousPath(new ContinuousPathMoveRequest
                    {
                        Card = card,
                        CoordinateSystem = configuration.CoordinateSystem,
                        Axes = activeBindings.Select(item => item.Axis).ToArray(),
                        PositionMode = 1,
                        Segments = queued.Select(item => ProjectContinuousSegment(
                            item, activeBindings)).ToArray(),
                        LookAheadEnabled = configuration.LookAheadEnabled,
                        PathError = configuration.PathError,
                        LookAheadAcceleration = ResolveLookAheadAcceleration()
                    });
                }

                lock (stateLock)
                {
                    queuedContinuousSegments.Clear();
                    coordinatedMoveActive = false;
                    continuousMoveActive = true;
                    stationMoveActive = true;
                    lastTarget = finalTarget;
                    lastTargetChannels = BuildActiveChannelFlags(activeBindings);
                    activeMoveDirections = moveDirections;
                    status.State = MotionStationState.Moving;
                    status.LastError = string.Empty;
                }
                return MotionStationResult.Success;
            }
            catch (Exception ex)
            {
                // 3.0 在 StartCPMove 下发前即结束本轮缓存；失败后不能再次启动同一批轨迹。
                lock (stateLock)
                {
                    queuedContinuousSegments.Clear();
                    continuousMoveActive = false;
                }
                return MapCommandFailure(ex);
            }
        }

        public MotionStationResult Stop(bool emergency = false)
        {
            MotionStationResult ready = EnsureInitialized();
            if (ready != MotionStationResult.Success)
            {
                return ready;
            }
            if (!motion.IsCardInitialized)
            {
                return SetFailure(MotionStationResult.NotConnected, "雷赛总线卡连接已断开。");
            }

            Exception failure = null;
            try
            {
                if (continuousMoveActive && bindings.Length > 0)
                {
                    motion.StopContinuousPath(
                        bindings[0].Card,
                        configuration.CoordinateSystem,
                        emergency ? (ushort)1 : (ushort)0);
                }
                else if (coordinatedMoveActive && bindings.Length > 0)
                {
                    motion.StopCoordinatedLinear(
                        bindings[0].Card,
                        configuration.CoordinateSystem,
                        emergency ? (ushort)1 : (ushort)0);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            foreach (AxisBinding binding in bindings)
            {
                try
                {
                    motion.StopOneAxis(
                        binding.Card,
                        binding.Axis,
                        emergency ? (ushort)1 : (ushort)0);
                }
                catch (Exception ex)
                {
                    failure = failure ?? ex;
                }
            }

            lock (stateLock)
            {
                coordinatedMoveActive = false;
                continuousMoveActive = false;
                stationMoveActive = false;
                queuedContinuousSegments.Clear();
                pendingHomeCleanChannels.Clear();
                lastTarget = null;
                lastTargetChannels = null;
                activeMoveDirections = null;
                status.State = failure == null
                    ? MotionStationState.Idle
                    : MotionStationState.Faulted;
                status.LastError = failure?.Message ?? string.Empty;
            }
            return failure == null
                ? MotionStationResult.Success
                : MotionStationResult.BaseFunctionError;
        }

        public MotionStationStatus GetStatus()
        {
            if (!initialized)
            {
                lock (stateLock)
                {
                    status.State = MotionStationState.Uninitialized;
                    return status;
                }
            }
            if (!motion.IsCardInitialized)
            {
                lock (stateLock)
                {
                    status.State = MotionStationState.Disconnected;
                    return status;
                }
            }

            try
            {
                double[] positions = new double[ChannelCount];
                bool moving = false;
                bool faulted = false;
                AxisBinding stationFaultBinding = null;
                AxisBinding alarmBinding = null;
                uint stationFaultBits = 0;
                foreach (AxisBinding binding in bindings)
                {
                    positions[binding.Channel] = motion.GetAxisPos(binding.Card, binding.Axis);
                    bool inPosition = motion.GetInPos(binding.Card, binding.Axis);
                    moving |= !inPosition;
                    uint ioStatus = motion.GetAxisIoStatus(binding.Card, binding.Axis);
                    bool alarmOrEmergency = (ioStatus & (AxisAlarmMask | EmergencyStopMask)) != 0;
                    faulted |= alarmOrEmergency;
                    if (alarmOrEmergency && alarmBinding == null)
                    {
                        alarmBinding = binding;
                    }
                    int direction = ResolveActiveMoveDirection(
                        binding,
                        positions[binding.Channel],
                        inPosition);
                    uint faultBits = GetStationMoveFaultBits(ioStatus, direction);
                    if (faultBits != 0 && stationFaultBinding == null)
                    {
                        stationFaultBinding = binding;
                        stationFaultBits = faultBits;
                    }
                }
                if (TryStopStationMoveOnFault(
                    stationFaultBinding,
                    stationFaultBits,
                    out _))
                {
                    lock (stateLock)
                    {
                        status.SetPosition(positions);
                        return status;
                    }
                }
                lock (stateLock)
                {
                    status.SetPosition(positions);
                    if (!moving)
                    {
                        coordinatedMoveActive = false;
                        continuousMoveActive = false;
                        stationMoveActive = false;
                        activeMoveDirections = null;
                    }
                    status.State = faulted
                        ? MotionStationState.Faulted
                        : moving ? MotionStationState.Moving : MotionStationState.Idle;
                    status.HasAlarm = faulted;
                    status.WarningAxis = alarmBinding?.Channel ?? -1;
                    if (!faulted)
                    {
                        status.LastError = string.Empty;
                    }
                    return status;
                }
            }
            catch (Exception ex)
            {
                lock (stateLock)
                {
                    status.State = MotionStationState.Faulted;
                    status.LastError = ex.Message;
                    return status;
                }
            }
        }

        private MotionStationResult StartHome(
            IReadOnlyList<AxisBinding> homeBindings,
            bool wait,
            int waitAxis)
        {
            try
            {
                AxisCommandRequest[] requests = homeBindings
                    .Select(item => new AxisCommandRequest(item.Card, item.Axis, AxisCommandKind.Home))
                    .ToArray();
                using (motion.ValidateAxesForCommand(requests))
                {
                    foreach (AxisBinding binding in homeBindings)
                    {
                        ApplyHomeProfile(binding);
                        motion.StartHome(binding.Card, binding.Axis);
                    }
                }
                lock (stateLock)
                {
                    coordinatedMoveActive = false;
                    continuousMoveActive = false;
                    stationMoveActive = false;
                    foreach (AxisBinding binding in homeBindings)
                    {
                        pendingHomeCleanChannels.Add(binding.Channel);
                    }
                    lastTarget = null;
                    lastTargetChannels = null;
                    activeMoveDirections = null;
                    status.State = MotionStationState.Moving;
                    status.LastError = string.Empty;
                }
                return wait
                    ? WaitMoveFinish(true, waitAxis)
                    : MotionStationResult.Success;
            }
            catch (Exception ex)
            {
                return MapCommandFailure(ex);
            }
        }

        private MotionStationResult StartCoordinatedMove(
            IReadOnlyList<AxisBinding> activeBindings,
            IReadOnlyList<double> positions)
        {
            ushort card = activeBindings[0].Card;
            if (activeBindings.Any(item => item.Card != card))
            {
                return SetFailure(
                    MotionStationResult.InvalidConfiguration,
                    "协调直线运动要求六通道位于同一张雷赛总线卡。");
            }
            SpeedProfileSnapshot profile = CreateMoveProfile(activeBindings);
            motion.MoveCoordinatedLinear(new CoordinatedLinearMoveRequest
            {
                Card = card,
                CoordinateSystem = configuration.CoordinateSystem,
                Axes = activeBindings.Select(item => item.Axis).ToArray(),
                Positions = activeBindings.Select(item => positions[item.Channel]).ToArray(),
                PositionMode = 1,
                MaxVelocity = profile.Velocity,
                AccelerationTime = profile.AccelerationTime,
                DecelerationTime = profile.DecelerationTime
            });
            return MotionStationResult.Success;
        }

        private MotionStationResult MoveUntilLimit(IReadOnlyList<double> offsets)
        {
            List<AxisBinding> searchBindings = bindings
                .Where(item => item.Channel < offsets.Count
                    && Math.Abs(offsets[item.Channel]) > LimitSearchOffset)
                .OrderBy(item => item.Channel)
                .ToList();
            if (searchBindings.Count == 0)
            {
                return MotionStationResult.Success;
            }
            try
            {
                using (motion.ValidateAxesForCommand(searchBindings.Select(CreateMotionRequest).ToArray()))
                {
                    foreach (AxisBinding binding in searchBindings)
                    {
                        ApplyJointProfile(binding);
                        motion.Mov(
                            binding.Card,
                            binding.Axis,
                            offsets[binding.Channel],
                            0,
                            false);
                    }
                }
                lock (stateLock)
                {
                    coordinatedMoveActive = false;
                    stationMoveActive = false;
                    lastTarget = null;
                    lastTargetChannels = null;
                    activeMoveDirections = null;
                    status.State = MotionStationState.Moving;
                    status.LastError = string.Empty;
                }
                MotionStationResult waitResult = WaitForControllerDone(searchBindings, 120000);
                if (waitResult != MotionStationResult.Success)
                {
                    return waitResult;
                }
                foreach (AxisBinding binding in searchBindings)
                {
                    uint ioStatus = motion.GetAxisIoStatus(binding.Card, binding.Axis);
                    bool positive = offsets[binding.Channel] > 0;
                    bool limitReached = positive
                        ? (ioStatus & ((1u << 1) | (1u << 6))) != 0
                        : (ioStatus & ((1u << 2) | (1u << 7))) != 0;
                    if (!limitReached)
                    {
                        return SetFailure(
                            MotionStationResult.CommandRejected,
                            $"工站通道{binding.Channel}未到达预期{(positive ? "正" : "负")}限位。");
                    }
                }
                ClearError();
                return MotionStationResult.Success;
            }
            catch (Exception ex)
            {
                return MapCommandFailure(ex);
            }
        }

        private MotionStationResult WaitForControllerDone(
            IReadOnlyList<AxisBinding> waitingBindings,
            int timeoutMs)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                if (waitingBindings.All(item => motion.GetInPos(item.Card, item.Axis)))
                {
                    lock (stateLock)
                    {
                        status.State = MotionStationState.Idle;
                    }
                    return MotionStationResult.Success;
                }
                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    return SetFailure(
                        MotionStationResult.Timeout,
                        $"等待工站运动完成超时:{timeoutMs}ms");
                }
                Thread.Sleep(PollIntervalMilliseconds);
            }
        }

        private int[] ResolveMoveDirections(
            IEnumerable<AxisBinding> activeBindings,
            IReadOnlyList<double> positions,
            StationMoveMode mode)
        {
            int[] directions = new int[ChannelCount];
            foreach (AxisBinding binding in activeBindings)
            {
                if (mode == StationMoveMode.Jog)
                {
                    directions[binding.Channel] = positions[binding.Channel] > 0 ? 1 : -1;
                    continue;
                }

                double current = motion.GetAxisPos(binding.Card, binding.Axis);
                directions[binding.Channel] = ResolveDirection(
                    current,
                    positions[binding.Channel]);
            }
            return directions;
        }

        private void EnsureStationMoveAllowed(IReadOnlyList<int> moveDirections)
        {
            foreach (AxisBinding binding in bindings)
            {
                uint ioStatus = motion.GetAxisIoStatus(binding.Card, binding.Axis);
                uint faultBits = GetStationMoveFaultBits(
                    ioStatus,
                    moveDirections[binding.Channel]);
                if (faultBits != 0)
                {
                    throw new InvalidOperationException(
                        BuildStationMoveRejectedMessage(binding.Channel, faultBits));
                }
            }
        }

        private int ResolveActiveMoveDirection(
            AxisBinding binding,
            double? observedPosition = null,
            bool? controllerInPosition = null)
        {
            int recordedDirection;
            bool tracksTarget;
            double target = 0;
            lock (stateLock)
            {
                if (!stationMoveActive)
                {
                    return 0;
                }
                recordedDirection = activeMoveDirections != null
                    && activeMoveDirections.Length > binding.Channel
                        ? activeMoveDirections[binding.Channel]
                        : 0;
                tracksTarget = lastTarget != null
                    && lastTarget.Length > binding.Channel
                    && lastTargetChannels != null
                    && lastTargetChannels.Length > binding.Channel
                    && lastTargetChannels[binding.Channel];
                if (tracksTarget)
                {
                    target = lastTarget[binding.Channel];
                }
            }

            if (!tracksTarget)
            {
                return recordedDirection;
            }
            double current = observedPosition
                ?? motion.GetAxisPos(binding.Card, binding.Axis);
            bool inPosition = controllerInPosition
                ?? motion.GetInPos(binding.Card, binding.Axis);
            if (inPosition
                && Math.Abs(target - current) <= configuration.PositionTolerances[binding.Channel])
            {
                return 0;
            }
            int remainingDirection = ResolveDirection(current, target);
            return remainingDirection == 0 ? recordedDirection : remainingDirection;
        }

        private static int ResolveDirection(double current, double target)
        {
            if (target == current)
            {
                return 0;
            }
            return target > current ? 1 : -1;
        }

        // 沿用 3.0 语义：报警和急停无条件阻断；限位只阻断继续压向该侧的运动，反向退出放行。
        private static uint GetStationMoveFaultBits(uint ioStatus, int direction)
        {
            uint faultBits = ioStatus & UnconditionalStationMoveFaultMask;
            if (direction > 0)
            {
                faultBits |= ioStatus & PositiveLimitMask;
            }
            else if (direction < 0)
            {
                faultBits |= ioStatus & NegativeLimitMask;
            }
            return faultBits;
        }

        private bool TryDetectAndStopStationMoveFault(out MotionStationResult result)
        {
            result = MotionStationResult.Success;
            AxisBinding[] observedBindings;
            lock (stateLock)
            {
                if (!stationMoveActive)
                {
                    return false;
                }
                observedBindings = bindings.ToArray();
            }

            AxisBinding faultBinding = null;
            uint faultBits = 0;
            foreach (AxisBinding binding in observedBindings)
            {
                uint ioStatus = motion.GetAxisIoStatus(binding.Card, binding.Axis);
                int direction = (ioStatus & (PositiveLimitMask | NegativeLimitMask)) == 0
                    ? 0
                    : ResolveActiveMoveDirection(binding);
                uint currentFaultBits = GetStationMoveFaultBits(ioStatus, direction);
                if (currentFaultBits != 0 && faultBinding == null)
                {
                    faultBinding = binding;
                    faultBits = currentFaultBits;
                }
            }
            return TryStopStationMoveOnFault(
                faultBinding,
                faultBits,
                out result);
        }

        private bool TryStopStationMoveOnFault(
            AxisBinding faultBinding,
            uint faultBits,
            out MotionStationResult result)
        {
            result = MotionStationResult.Success;
            if (faultBinding == null)
            {
                return false;
            }

            AxisBinding[] stopBindings;
            bool stopCoordinated;
            bool stopContinuous;
            lock (stateLock)
            {
                if (!stationMoveActive)
                {
                    return false;
                }
                stopBindings = bindings.ToArray();
                stopCoordinated = coordinatedMoveActive;
                stopContinuous = continuousMoveActive;
                stationMoveActive = false;
                coordinatedMoveActive = false;
                continuousMoveActive = false;
                lastTarget = null;
                lastTargetChannels = null;
                activeMoveDirections = null;
            }

            Exception failure = null;
            if (stopContinuous && stopBindings.Length > 0)
            {
                try
                {
                    motion.StopContinuousPath(
                        stopBindings[0].Card,
                        configuration.CoordinateSystem,
                        0);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }
            else if (stopCoordinated && stopBindings.Length > 0)
            {
                try
                {
                    motion.StopCoordinatedLinear(
                        stopBindings[0].Card,
                        configuration.CoordinateSystem,
                        0);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }
            foreach (AxisBinding binding in stopBindings)
            {
                try
                {
                    motion.StopOneAxis(binding.Card, binding.Axis, 0);
                }
                catch (Exception ex)
                {
                    failure = failure ?? ex;
                }
            }

            string message = BuildStationMoveFaultMessage(faultBinding.Channel, faultBits);
            if (failure != null)
            {
                message += $"；停止轴失败:{failure.Message}";
            }
            lock (stateLock)
            {
                status.State = MotionStationState.Faulted;
                status.HasAlarm = true;
                status.WarningAxis = faultBinding.Channel;
                status.LastError = message;
            }
            result = failure == null
                ? MotionStationResult.CommandRejected
                : MotionStationResult.BaseFunctionError;
            return true;
        }

        private void CleanCompletedHomePositions(IEnumerable<AxisBinding> completedBindings)
        {
            foreach (AxisBinding binding in completedBindings)
            {
                bool shouldClean;
                lock (stateLock)
                {
                    shouldClean = pendingHomeCleanChannels.Remove(binding.Channel);
                }
                if (!shouldClean)
                {
                    continue;
                }

                try
                {
                    motion.CleanPos(binding.Card, binding.Axis);
                }
                catch
                {
                    lock (stateLock)
                    {
                        pendingHomeCleanChannels.Add(binding.Channel);
                    }
                    throw;
                }
            }
        }

        private static string BuildStationMoveFaultMessage(short channel, uint faultBits)
        {
            return $"工站通道{channel}运动中检测到{DescribeStationMoveFault(faultBits)}，已停止全部绑定轴。";
        }

        private static string BuildStationMoveRejectedMessage(short channel, uint faultBits)
        {
            return $"工站通道{channel}检测到{DescribeStationMoveFault(faultBits)}，当前运动命令已拒绝。";
        }

        private static string DescribeStationMoveFault(uint faultBits)
        {
            var reasons = new List<string>();
            if ((faultBits & AxisAlarmMask) != 0)
            {
                reasons.Add("轴报警");
            }
            if ((faultBits & EmergencyStopMask) != 0)
            {
                reasons.Add("急停");
            }
            if ((faultBits & PositiveHardLimitMask) != 0)
            {
                reasons.Add("正硬限位");
            }
            if ((faultBits & NegativeHardLimitMask) != 0)
            {
                reasons.Add("负硬限位");
            }
            if ((faultBits & PositiveSoftLimitMask) != 0)
            {
                reasons.Add("正软限位");
            }
            if ((faultBits & NegativeSoftLimitMask) != 0)
            {
                reasons.Add("负软限位");
            }
            return string.Join("、", reasons);
        }

        private bool IsLastTargetInPosition(int selectedChannel)
        {
            double[] target;
            bool[] activeChannels;
            lock (stateLock)
            {
                target = lastTarget == null ? null : (double[])lastTarget.Clone();
                activeChannels = lastTargetChannels == null
                    ? null
                    : (bool[])lastTargetChannels.Clone();
            }
            if (target == null || activeChannels == null)
            {
                return true;
            }

            foreach (AxisBinding binding in bindings)
            {
                if (!activeChannels[binding.Channel]
                    || (selectedChannel >= 0 && binding.Channel != selectedChannel))
                {
                    continue;
                }
                double tolerance = configuration.PositionTolerances[binding.Channel];
                double current = motion.GetAxisPos(binding.Card, binding.Axis);
                if (Math.Abs(current - target[binding.Channel]) > tolerance)
                {
                    return false;
                }
            }
            return true;
        }

        private void ApplyJointProfile(AxisBinding binding)
        {
            SpeedProfileSnapshot profile = CreateJointProfile(binding);
            motion.SetMovParam(
                binding.Card,
                binding.Axis,
                0,
                profile.Velocity,
                profile.AccelerationTime,
                profile.DecelerationTime,
                0,
                0,
                binding.Configuration.PulseToMM);
        }

        private void ApplyHomeProfile(AxisBinding binding)
        {
            if (!double.TryParse(
                binding.Configuration.HomeSpeed,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double configuredHomeSpeed))
            {
                throw new InvalidOperationException(
                    $"工站通道{binding.Channel}回原速度无效:{binding.Configuration.HomeSpeed}");
            }
            SpeedProfileSnapshot joint = CreateJointProfile(binding);
            double speedRatio = joint.Velocity / binding.Configuration.SpeedMax;
            double homeVelocity = Math.Max(0.000001, configuredHomeSpeed * speedRatio);
            motion.SetMovParam(
                binding.Card,
                binding.Axis,
                0,
                homeVelocity,
                joint.AccelerationTime,
                joint.DecelerationTime,
                0,
                0,
                binding.Configuration.PulseToMM);
            motion.SettHomeParam(
                binding.Card,
                binding.Axis,
                0,
                1,
                binding.Configuration.HomeMethod > 0
                    ? checked((ushort)binding.Configuration.HomeMethod)
                    : (ushort)0);
        }

        private SpeedProfileSnapshot CreateJointProfile(AxisBinding binding)
        {
            lock (stateLock)
            {
                SpeedProfile joint = jointSpeeds[binding.Channel];
                double velocityRatio = PercentRatio(joint.Velocity, globalSpeed.Velocity);
                double accelerationRatio = PercentRatio(joint.Acceleration, globalSpeed.Acceleration);
                double decelerationRatio = PercentRatio(joint.Deceleration, globalSpeed.Deceleration);
                return new SpeedProfileSnapshot
                {
                    Velocity = Math.Max(0.000001, binding.Configuration.SpeedMax * velocityRatio),
                    AccelerationTime = ConvertProfileTime(
                        binding.Configuration.AccMax,
                        velocityRatio,
                        accelerationRatio),
                    DecelerationTime = ConvertProfileTime(
                        binding.Configuration.DecMax,
                        velocityRatio,
                        decelerationRatio)
                };
            }
        }

        private SpeedProfileSnapshot CreateMoveProfile(IReadOnlyList<AxisBinding> activeBindings)
        {
            lock (stateLock)
            {
                double velocityRatio = PercentRatio(moveSpeed.Velocity, globalSpeed.Velocity);
                double accelerationRatio = PercentRatio(moveSpeed.Acceleration, globalSpeed.Acceleration);
                double decelerationRatio = PercentRatio(moveSpeed.Deceleration, globalSpeed.Deceleration);
                return new SpeedProfileSnapshot
                {
                    Velocity = activeBindings.Min(item => item.Configuration.SpeedMax) * velocityRatio,
                    AccelerationTime = activeBindings.Max(item => ConvertProfileTime(
                        item.Configuration.AccMax,
                        velocityRatio,
                        accelerationRatio)),
                    DecelerationTime = activeBindings.Max(item => ConvertProfileTime(
                        item.Configuration.DecMax,
                        velocityRatio,
                        decelerationRatio))
                };
            }
        }

        private IEnumerable<AxisBinding> ResolveHomeSequence()
        {
            HashSet<short> usedChannels = new HashSet<short>();
            IEnumerable<AxisName> sequence = configuration.homeSeq?.axisSeq
                ?? Enumerable.Empty<AxisName>();
            foreach (AxisName item in sequence)
            {
                if (item == null || item.Name == "-1")
                {
                    continue;
                }
                AxisBinding binding = bindings.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Configuration.AxisName,
                        item.Name,
                        StringComparison.Ordinal));
                if (binding != null && usedChannels.Add(binding.Channel))
                {
                    yield return binding;
                }
            }
        }

        private bool TryBuildBindings(out AxisBinding[] resolved, out string error)
        {
            var result = new List<AxisBinding>();
            IReadOnlyList<AxisConfig> configuredAxes = configuration.dataAxis.axisConfigs;
            for (short channel = 0; channel < ChannelCount; channel++)
            {
                AxisConfig configured = configuredAxes[channel];
                if (configured == null || configured.AxisName == "-1")
                {
                    continue;
                }
                if (!ushort.TryParse(
                        configured.CardNum,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out ushort card)
                    || !cardStore.TryGetAxisByName(card, configured.AxisName, out Axis physicalAxis)
                    || physicalAxis.AxisNum < 0 || physicalAxis.AxisNum > ushort.MaxValue)
                {
                    resolved = Array.Empty<AxisBinding>();
                    error = $"工站通道{channel}绑定不存在:{configured.CardNum}-{configured.AxisName}";
                    return false;
                }
                result.Add(new AxisBinding
                {
                    Channel = channel,
                    Card = card,
                    Axis = (ushort)physicalAxis.AxisNum,
                    Configuration = physicalAxis
                });
            }
            resolved = result.ToArray();
            error = resolved.Length == 0 ? "轴工站没有配置任何物理轴。" : null;
            return resolved.Length > 0;
        }

        private AxisBinding FindBinding(int channel)
        {
            return bindings.FirstOrDefault(item => item.Channel == channel);
        }

        private DataPos FindPoint(int pointIndex)
        {
            if (pointIndex < 0 || configuration.ListDataPos == null)
            {
                return null;
            }
            if (pointIndex < configuration.ListDataPos.Count)
            {
                DataPos indexed = configuration.ListDataPos[pointIndex];
                if (indexed != null && indexed.Index == pointIndex && indexed.IsMotionReady)
                {
                    return indexed;
                }
            }
            return configuration.ListDataPos.FirstOrDefault(item =>
                item != null && item.Index == pointIndex && item.IsMotionReady);
        }

        private static DataPos CreateRuntimePoint(IReadOnlyList<double> values, string name)
        {
            return new DataPos(-1)
            {
                Name = name,
                IsTaught = true,
                Enabled = true,
                X = values[0],
                Y = values[1],
                Z = values[2],
                U = values[3],
                V = values[4],
                W = values[5]
            };
        }

        private bool TryValidateContinuousPoint(DataPos point, string role, out string error)
        {
            error = null;
            if (point == null || !point.IsMotionReady)
            {
                error = $"{role}不存在、名称为空或尚未示教。";
                return false;
            }
            IReadOnlyList<double> values = point.GetAllValues();
            foreach (AxisBinding binding in bindings)
            {
                double value = values[binding.Channel];
                if (!IsFinite(value) || !IsWithinPointLimit(point, binding.Channel, value))
                {
                    error = $"{role}P{point.Index}通道{binding.Channel}坐标无效或超出点位限值:{value}";
                    return false;
                }
            }
            return true;
        }

        private ContinuousPathSegment CreateContinuousSegment(
            ContinuousPathSegmentType type,
            DataPos start,
            DataPos middle,
            DataPos target,
            double radius,
            ushort direction,
            int circle)
        {
            SpeedProfileSnapshot profile = CreateContinuousMoveProfile();
            return new ContinuousPathSegment
            {
                Type = type,
                StartPositions = start?.GetAllValues().ToArray(),
                MiddlePositions = middle?.GetAllValues().ToArray(),
                TargetPositions = target.GetAllValues().ToArray(),
                Radius = radius,
                ArcDirection = direction,
                Circle = circle,
                MaxVelocity = profile.Velocity,
                AccelerationTime = profile.AccelerationTime,
                DecelerationTime = profile.DecelerationTime,
                EndVelocity = 0
            };
        }

        private static ContinuousPathSegment ProjectContinuousSegment(
            ContinuousPathSegment source,
            IReadOnlyList<AxisBinding> activeBindings)
        {
            double[] Project(IReadOnlyList<double> values) => values == null
                ? null
                : activeBindings.Select(item => values[item.Channel]).ToArray();
            return new ContinuousPathSegment
            {
                Type = source.Type,
                StartPositions = Project(source.StartPositions),
                MiddlePositions = Project(source.MiddlePositions),
                TargetPositions = Project(source.TargetPositions),
                Radius = source.Radius,
                ArcDirection = source.ArcDirection,
                Circle = source.Circle,
                MaxVelocity = source.MaxVelocity,
                AccelerationTime = source.AccelerationTime,
                DecelerationTime = source.DecelerationTime,
                EndVelocity = source.EndVelocity
            };
        }

        private double ResolveLookAheadAcceleration()
        {
            // 3.0 EtherCatDmc 使用工站插补最大加速度乘 lookMultipleAcc；
            // 参数在 open_list 之前下发，且不随本段速度百分比缩小。
            return Math.Max(1,
                configuration.ContinuousPathMaximumAcceleration
                * configuration.LookAheadAccelerationMultiplier);
        }

        private SpeedProfileSnapshot CreateContinuousMoveProfile()
        {
            lock (stateLock)
            {
                double velocityRatio = PercentRatio(moveSpeed.Velocity, globalSpeed.Velocity);
                double accelerationRatio = PercentRatio(moveSpeed.Acceleration, globalSpeed.Acceleration);
                double decelerationRatio = PercentRatio(moveSpeed.Deceleration, globalSpeed.Deceleration);
                double velocity = configuration.ContinuousPathMaximumVelocity * velocityRatio;
                double acceleration = configuration.ContinuousPathMaximumAcceleration * accelerationRatio;
                double deceleration = configuration.ContinuousPathMaximumDeceleration * decelerationRatio;
                return new SpeedProfileSnapshot
                {
                    Velocity = Math.Max(0.000001, velocity),
                    AccelerationTime = ConvertAccelerationToTime(velocity, acceleration),
                    DecelerationTime = ConvertAccelerationToTime(velocity, deceleration)
                };
            }
        }

        private static double ConvertAccelerationToTime(double velocity, double acceleration)
        {
            if (acceleration <= 0)
            {
                return 0.5;
            }
            return Math.Max(0.0005, Math.Min(0.5, velocity / acceleration));
        }

        private static AxisCommandRequest CreateMotionRequest(AxisBinding binding)
        {
            return new AxisCommandRequest(binding.Card, binding.Axis, AxisCommandKind.Motion);
        }

        private static bool[] BuildActiveChannelFlags(IEnumerable<AxisBinding> activeBindings)
        {
            bool[] active = new bool[ChannelCount];
            foreach (AxisBinding binding in activeBindings)
            {
                active[binding.Channel] = true;
            }
            return active;
        }

        private static bool IsWithinPointLimit(DataPos point, int channel, double value)
        {
            if (point.PositionLimits == null || point.PositionLimits.Length <= channel
                || point.PositionLimits[channel] == null
                || point.PositionLimits[channel].Length < 2)
            {
                return false;
            }
            return value >= point.PositionLimits[channel][0]
                && value <= point.PositionLimits[channel][1];
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool TryNormalizePercent(double value, out double normalized)
        {
            normalized = 0;
            if (!IsFinite(value))
            {
                return false;
            }
            normalized = Math.Min(100, Math.Abs(value));
            return true;
        }

        private static double PercentRatio(double local, double global)
        {
            return local * global / 10000d;
        }

        private static double ConvertProfileTime(
            double configuredTime,
            double velocityRatio,
            double accelerationRatio)
        {
            if (accelerationRatio <= 0)
            {
                return 0.5;
            }
            double value = configuredTime * velocityRatio / accelerationRatio;
            return Math.Max(0.0005, Math.Min(0.5, value));
        }

        private static void UpdateSpeed(
            SpeedProfile target,
            double velocity,
            double acceleration,
            double deceleration)
        {
            if (velocity > 0)
            {
                target.Velocity = velocity;
            }
            if (acceleration > 0)
            {
                target.Acceleration = acceleration;
            }
            if (deceleration > 0)
            {
                target.Deceleration = deceleration;
            }
        }

        private void InitializeSpeedProfiles()
        {
            double global = configuration.ManualSpeedPercent;
            globalSpeed.Velocity = global;
            globalSpeed.Acceleration = global;
            globalSpeed.Deceleration = global;
            foreach (SpeedProfile joint in jointSpeeds)
            {
                joint.Velocity = 30;
                joint.Acceleration = 30;
                joint.Deceleration = 30;
            }
            moveSpeed.Velocity = 10;
            moveSpeed.Acceleration = 10;
            moveSpeed.Deceleration = 10;
        }

        private MotionStationResult EnsureInitialized()
        {
            return initialized
                ? MotionStationResult.Success
                : SetFailure(MotionStationResult.NotInitialized, "工站尚未初始化。");
        }

        private MotionStationResult EnsureOperational()
        {
            MotionStationResult initializedResult = EnsureInitialized();
            if (initializedResult != MotionStationResult.Success)
            {
                return initializedResult;
            }
            return motion.IsCardInitialized
                ? MotionStationResult.Success
                : SetFailure(MotionStationResult.NotConnected, "雷赛总线卡连接已断开。");
        }

        private MotionStationResult MapCommandFailure(Exception exception)
        {
            MotionStationResult result = exception is ArgumentException
                ? MotionStationResult.InvalidParameter
                : exception is InvalidOperationException
                    ? MotionStationResult.CommandRejected
                    : MotionStationResult.BaseFunctionError;
            return SetFailure(result, exception.Message);
        }

        private MotionStationResult SetFailure(MotionStationResult result, string error)
        {
            lock (stateLock)
            {
                status.LastError = error ?? string.Empty;
                if (result == MotionStationResult.NotConnected)
                {
                    status.State = MotionStationState.Disconnected;
                }
                else if (result == MotionStationResult.BaseFunctionError
                    || result == MotionStationResult.ReceiveFailed
                    || result == MotionStationResult.SendFailed
                    || result == MotionStationResult.Timeout)
                {
                    status.State = MotionStationState.Faulted;
                }
            }
            return result;
        }

        private void ClearError()
        {
            lock (stateLock)
            {
                status.LastError = string.Empty;
            }
        }

        private sealed class AxisBinding
        {
            public short Channel { get; set; }
            public ushort Card { get; set; }
            public ushort Axis { get; set; }
            public Axis Configuration { get; set; }
        }

        private sealed class SpeedProfile
        {
            public double Velocity { get; set; } = 10;
            public double Acceleration { get; set; } = 10;
            public double Deceleration { get; set; } = 10;
        }

        private sealed class SpeedProfileSnapshot
        {
            public double Velocity { get; set; }
            public double AccelerationTime { get; set; }
            public double DecelerationTime { get; set; }
        }
    }
}
