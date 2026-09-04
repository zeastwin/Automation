// 模块：运动控制 / 汇川机器人工站。
// 职责范围：沿用 3.0 的六轴工站语义，直接对接两代 IMC100 控制器。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Automation.MotionControl
{
    internal sealed class InovanceStation : InovanceStationBase
    {
        public InovanceStation(
            DataStation configuration,
            StationDefinitionStore stationStore,
            CommunicationConfigStore communicationStore,
            PlatformPaths paths)
            : base(configuration, stationStore, communicationStore, paths, new NativeInovanceRobotApi())
        {
        }

        internal InovanceStation(
            DataStation configuration,
            CommunicationConfigStore communicationStore,
            IInovanceRobotApi api)
            : base(configuration, null, communicationStore, null, api)
        {
        }
    }

    internal sealed class InovanceV4Station : InovanceStationBase
    {
        public InovanceV4Station(
            DataStation configuration,
            StationDefinitionStore stationStore,
            CommunicationConfigStore communicationStore,
            PlatformPaths paths)
            : base(configuration, stationStore, communicationStore, paths, new NativeInovanceV4RobotApi())
        {
        }

        internal InovanceV4Station(
            DataStation configuration,
            CommunicationConfigStore communicationStore,
            IInovanceRobotApi api)
            : base(configuration, null, communicationStore, null, api)
        {
        }
    }

    internal abstract class InovanceStationBase : IMotionStation
    {
        private const int AxisCount = 6;
        private const int MaximumRobotPointIndex = DataStation.RobotPointCapacity - 1;
        private const int DataStreamOff = 0;
        private const int DataStreamOn = 1;
        private const int DataStreamPause = 2;
        private const int DataStreamContinue = 3;
        private const int TeachMode = 1;
        private const double JogThreshold = 20d;
        private const int Zone = 0;

        private readonly object syncRoot = new object();
        private readonly DataStation configuration;
        private readonly StationDefinitionStore stationStore;
        private readonly CommunicationConfigStore communicationStore;
        private readonly PlatformPaths paths;
        private readonly IInovanceRobotApi api;
        private readonly MotionStationStatus status = new MotionStationStatus();
        private readonly Dictionary<int, PalletDefinition> pallets =
            new Dictionary<int, PalletDefinition>();

        private int connectionId;
        private uint robotAddress;
        private ushort robotPort;
        private int connectTimeoutSeconds;
        private bool nativeConnectionOpen;
        private bool initialized;
        private bool lifecycleInitialized;
        private bool pointsLoading;
        private CancellationTokenSource connectionMonitorCancellation;
        private Task connectionMonitorTask;
        private int speedPercent = 30;
        private double globalSpeedPercent = 100d;

        protected InovanceStationBase(
            DataStation configuration,
            StationDefinitionStore stationStore,
            CommunicationConfigStore communicationStore,
            PlatformPaths paths,
            IInovanceRobotApi api)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.stationStore = stationStore;
            this.communicationStore = communicationStore ?? throw new ArgumentNullException(nameof(communicationStore));
            this.paths = paths;
            this.api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public MotionStationResult Initialize()
        {
            lock (syncRoot)
            {
                if (lifecycleInitialized)
                {
                    return MotionStationResult.Success;
                }

                if (!TryResolveEndpoint(out uint address, out ushort port, out int timeoutSeconds,
                    out int resolvedConnectionId,
                    out string configurationError))
                {
                    return Fail(MotionStationResult.InvalidConfiguration, configurationError,
                        MotionStationState.Uninitialized);
                }

                robotAddress = address;
                robotPort = port;
                connectTimeoutSeconds = timeoutSeconds;
                connectionId = resolvedConnectionId;
                lifecycleInitialized = true;
                initialized = false;
                nativeConnectionOpen = false;
                pointsLoading = false;
                status.State = MotionStationState.Disconnected;
                status.LastError = string.Empty;
                status.HasAlarm = false;
                status.IsHomed = false;
                status.IsServoEnabled = false;
                connectionMonitorCancellation = new CancellationTokenSource();
                CancellationToken token = connectionMonitorCancellation.Token;
                connectionMonitorTask = Task.Run(() => MonitorConnection(token), token);

                // 3.0 在机器人暂时离线时仍完成平台初始化，由工站后台持续尝试连接。
                return MotionStationResult.Success;
            }
        }

        public MotionStationResult Release()
        {
            CancellationTokenSource cancellation;
            Task monitor;
            lock (syncRoot)
            {
                cancellation = connectionMonitorCancellation;
                monitor = connectionMonitorTask;
                connectionMonitorCancellation = null;
                connectionMonitorTask = null;
                lifecycleInitialized = false;
            }
            cancellation?.Cancel();
            if (monitor != null)
            {
                try
                {
                    monitor.Wait();
                }
                catch (AggregateException ex) when (ex.InnerExceptions.All(
                    inner => inner is TaskCanceledException || inner is OperationCanceledException))
                {
                }
            }
            cancellation?.Dispose();

            lock (syncRoot)
            {
                if (!nativeConnectionOpen)
                {
                    pallets.Clear();
                    initialized = false;
                    pointsLoading = false;
                    status.State = MotionStationState.Uninitialized;
                    status.IsServoEnabled = false;
                    status.IsHomed = false;
                    return MotionStationResult.Success;
                }

                int firstError = 0;
                try
                {
                    CaptureFirstError(ref firstError, SetDataStreamMode(DataStreamOff));
                    CaptureFirstError(ref firstError, api.MotorEnable(DataStreamOff, connectionId));
                    CaptureFirstError(ref firstError, api.Exit(connectionId));
                }
                catch (Exception ex) when (IsNativeBoundaryException(ex))
                {
                    status.LastError = $"释放汇川机器人连接失败：{ex.Message}";
                    firstError = int.MinValue;
                }
                finally
                {
                    pallets.Clear();
                    initialized = false;
                    lifecycleInitialized = false;
                    nativeConnectionOpen = false;
                    pointsLoading = false;
                    status.State = MotionStationState.Uninitialized;
                    status.IsServoEnabled = false;
                    status.IsHomed = false;
                }

                return firstError == 0
                    ? MotionStationResult.Success
                    : Fail(MotionStationResult.BaseFunctionError,
                        $"释放汇川机器人连接失败，返回值：{firstError}",
                        MotionStationState.Uninitialized);
            }
        }

        private void MonitorConnection(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool connected;
                bool shouldConnect;
                lock (syncRoot)
                {
                    if (!lifecycleInitialized)
                    {
                        return;
                    }
                    shouldConnect = !initialized || !nativeConnectionOpen
                        || status.State == MotionStationState.Disconnected;
                    connected = initialized && nativeConnectionOpen
                        && status.State != MotionStationState.Disconnected;
                }

                if (shouldConnect)
                {
                    PointLoadRequest pointRequest = CapturePointLoadRequest();
                    PointLoadBatch pointBatch = null;
                    lock (syncRoot)
                    {
                        if (!lifecycleInitialized)
                        {
                            return;
                        }
                        shouldConnect = !initialized || !nativeConnectionOpen
                            || status.State == MotionStationState.Disconnected;
                        if (shouldConnect && TryConnect())
                        {
                            try
                            {
                                MotionStationResult readResult = ReadConfiguredPointsFromRobot(
                                    pointRequest,
                                    out pointBatch);
                                if (readResult != MotionStationResult.Success)
                                {
                                    pointsLoading = false;
                                }
                            }
                            catch (Exception ex) when (IsNativeBoundaryException(ex))
                            {
                                pointBatch = null;
                                FailConnectionAttempt(
                                    $"加载或调用汇川机器人原生库失败：{ex.Message}",
                                    int.MinValue);
                            }
                        }
                        connected = initialized && nativeConnectionOpen
                            && status.State != MotionStationState.Disconnected;
                    }

                    if (pointBatch != null)
                    {
                        MotionStationResult commitResult = CommitConfiguredPoints(
                            pointBatch,
                            out string commitError);
                        lock (syncRoot)
                        {
                            pointsLoading = false;
                            if (lifecycleInitialized && nativeConnectionOpen)
                            {
                                if (commitResult == MotionStationResult.Success)
                                {
                                    status.SetPosition(pointBatch.CurrentPosition.Coordinates);
                                    status.State = MotionStationState.Idle;
                                    status.HasAlarm = false;
                                    status.LastError = string.Empty;
                                }
                                else
                                {
                                    Fail(
                                        commitResult,
                                        commitError,
                                        MotionStationState.Faulted);
                                }
                            }
                            connected = initialized && nativeConnectionOpen
                                && status.State != MotionStationState.Disconnected;
                        }
                    }
                }

                // 3.0 的状态监听周期为30ms、连接失败重试周期为50ms；连接后降低空轮询频率。
                if (token.WaitHandle.WaitOne(connected ? 100 : 50))
                {
                    return;
                }
            }
        }

        private bool TryConnect()
        {
            CloseConnectionForRetry();
            try
            {
                int result = api.Initialize(robotAddress, robotPort, connectTimeoutSeconds, connectionId);
                if (result != 0)
                {
                    return FailConnectionAttempt("连接汇川机器人失败", result);
                }
                nativeConnectionOpen = true;

                result = api.AcquirePermit(1, connectionId);
                if (result != 0)
                {
                    return FailConnectionAttempt("获取汇川机器人控制权失败", result);
                }

                byte[] password = new byte[8];
                byte[] passwordText = Encoding.ASCII.GetBytes("000000");
                Array.Copy(passwordText, password, passwordText.Length);
                result = api.UserLogin(2, password, connectionId);
                if (result != 0)
                {
                    return FailConnectionAttempt("登录汇川机器人管理模式失败", result);
                }

                result = api.GetEmergencyStopStatus(out int emergencyStop, connectionId);
                if (result != 0)
                {
                    return FailConnectionAttempt("读取汇川机器人急停状态失败", result);
                }
                if (emergencyStop != 0)
                {
                    result = api.EmergencyStop(DataStreamOff, connectionId);
                    if (result != 0)
                    {
                        return FailConnectionAttempt("释放汇川机器人急停失败", result);
                    }
                }

                result = api.GetSystemError(out int systemError, connectionId);
                if (result != 0)
                {
                    return FailConnectionAttempt("读取汇川机器人故障状态失败", result);
                }
                if (systemError != 0)
                {
                    result = api.ResetError(connectionId);
                    if (result != 0)
                    {
                        return FailConnectionAttempt("复位汇川机器人故障失败", result);
                    }
                    api.Delay(50);
                }

                result = api.SetCoordinate(2, connectionId);
                if (result != 0)
                {
                    return FailConnectionAttempt("设置汇川机器人直角坐标系失败", result);
                }
                result = api.SetMode(TeachMode, connectionId);
                if (result != 0)
                {
                    return FailConnectionAttempt("切换汇川机器人示教模式失败", result);
                }

                result = api.GetMotorStatus(out int motorStatus, connectionId);
                if (result != 0)
                {
                    return FailConnectionAttempt("读取汇川机器人电机使能状态失败", result);
                }
                if (motorStatus == 0)
                {
                    result = api.MotorEnable(DataStreamOn, connectionId);
                    if (result != 0)
                    {
                        return FailConnectionAttempt("汇川机器人自动上使能失败", result);
                    }
                    api.Delay(550);
                }

                result = SetDataStreamMode(DataStreamOn);
                if (result != 0)
                {
                    return FailConnectionAttempt("开启汇川机器人数据流失败", result);
                }

                initialized = true;
                pointsLoading = true;
                status.State = MotionStationState.Moving;
                status.IsServoEnabled = true;
                status.HasAlarm = false;
                status.LastError = string.Empty;
                return true;
            }
            catch (Exception ex) when (IsNativeBoundaryException(ex))
            {
                return FailConnectionAttempt($"加载或调用汇川机器人原生库失败：{ex.Message}", int.MinValue);
            }
        }

        private bool FailConnectionAttempt(string message, int nativeResult)
        {
            CloseConnectionForRetry();
            initialized = false;
            pointsLoading = false;
            status.State = MotionStationState.Disconnected;
            status.IsServoEnabled = false;
            status.LastError = nativeResult == int.MinValue
                ? message
                : $"{message}，返回值：{nativeResult}";
            return false;
        }

        private void CloseConnectionForRetry()
        {
            if (!nativeConnectionOpen)
            {
                return;
            }
            try
            {
                api.Exit(connectionId);
            }
            catch (Exception ex) when (IsNativeBoundaryException(ex))
            {
            }
            nativeConnectionOpen = false;
            pointsLoading = false;
        }

        public MotionStationResult Home(short axis = -1, bool wait = true, bool group = false)
        {
            MotionStationResult commandResult;
            lock (syncRoot)
            {
                commandResult = EnsureReadyForMotion();
                if (commandResult != MotionStationResult.Success)
                {
                    return commandResult;
                }
                if (axis >= AxisCount)
                {
                    return Fail(MotionStationResult.InvalidParameter, "汇川机器人回原轴号必须为-1或0到5。",
                        status.State);
                }
                if (IsMoving())
                {
                    return MotionStationResult.Busy;
                }

                int result = SetDataStreamMode(DataStreamOn);
                if (result == 0)
                {
                    result = api.GetPosition(out InovanceRobotPose current, connectionId);
                    if (result == 0)
                    {
                        result = api.GetPoint(1, out InovanceRobotPose home, connectionId);
                        if (result == 0)
                        {
                            current.Coordinates[2] = home.Coordinates[2];
                            result = api.MovePosition(current, false, speedPercent, Zone, connectionId);
                            if (result == 0)
                            {
                                result = api.MovePosition(home, false, speedPercent, Zone, connectionId);
                            }
                        }
                    }
                }

                commandResult = FinishMoveCommand(result, "汇川机器人回原失败");
                if (commandResult == MotionStationResult.Success)
                {
                    status.IsHomed = false;
                }
            }

            return commandResult == MotionStationResult.Success && wait
                ? WaitMoveFinish(true)
                : commandResult;
        }

        public MotionStationResult SetSpeed(
            double velocity,
            double acceleration,
            double deceleration,
            short axis = -1,
            StationSpeedType type = StationSpeedType.Joint)
        {
            lock (syncRoot)
            {
                if (!IsFinitePositive(velocity) || !IsFinitePositive(acceleration) || !IsFinitePositive(deceleration))
                {
                    return Fail(MotionStationResult.InvalidParameter, "汇川机器人速度、加速度和减速度必须是大于0的有限数。",
                        status.State);
                }
                if (axis >= AxisCount)
                {
                    return Fail(MotionStationResult.InvalidParameter, "汇川机器人速度轴号必须为-1或0到5。",
                        status.State);
                }
                if (type == StationSpeedType.Global)
                {
                    globalSpeedPercent = ClampPercent(velocity);
                    return MotionStationResult.Success;
                }

                MotionStationResult ready = EnsureInitialized();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }

                speedPercent = (int)ClampPercent(velocity * globalSpeedPercent / 100d);
                double effectiveAcceleration = ClampPercent(acceleration * globalSpeedPercent / 100d);
                double effectiveDeceleration = ClampPercent(deceleration * globalSpeedPercent / 100d);
                int result = api.SetAcceleration(effectiveAcceleration, effectiveDeceleration, connectionId);
                if (result == 0)
                {
                    result = api.SetVelocity(speedPercent, connectionId);
                }
                return result == 0
                    ? MotionStationResult.Success
                    : FailNative(MotionStationResult.CommandRejected, "设置汇川机器人速度失败", result);
            }
        }

        public MotionStationResult MoveToPoint(
            DataPos point,
            StationMoveMode mode,
            bool[] disabledAxes = null,
            short tool = 0)
        {
            lock (syncRoot)
            {
                MotionStationResult ready = EnsureReadyForMotion();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (point == null || !point.IsMotionReady || point.Index < 0
                    || point.Index > MaximumRobotPointIndex)
                {
                    return Fail(MotionStationResult.InvalidParameter, "汇川机器人目标点无效或尚未示教。", status.State);
                }
                if (mode == StationMoveMode.Jog)
                {
                    return Fail(MotionStationResult.InvalidParameter, "汇川机器人点位运动不支持Jog模式。", status.State);
                }
                if (disabledAxes != null && disabledAxes.Length < AxisCount)
                {
                    return Fail(MotionStationResult.InvalidParameter, "汇川机器人禁用轴数组必须包含六个通道。", status.State);
                }
                if (IsMoving())
                {
                    return MotionStationResult.Busy;
                }

                bool linear = mode == StationMoveMode.Move;
                bool hasDisabledAxis = HasDisabledAxis(disabledAxes);
                int result = SetDataStreamMode(DataStreamOn);
                if (result == 0)
                {
                    result = api.SetRapidMove(1, 1, connectionId);
                }

                if (result == 0 && configuration.PointFromRobot && !hasDisabledAxis)
                {
                    result = api.MovePoint(point.Index, linear, speedPercent, Zone, connectionId);
                }
                else if (result == 0)
                {
                    InovanceRobotPose target;
                    if (configuration.PointFromRobot)
                    {
                        result = api.GetPoint(point.Index, out target, connectionId);
                    }
                    else
                    {
                        target = ToRobotPose(point);
                    }

                    if (result == 0 && hasDisabledAxis)
                    {
                        result = api.GetPosition(out InovanceRobotPose current, connectionId);
                        if (result == 0)
                        {
                            for (int i = 0; i < AxisCount; i++)
                            {
                                if (disabledAxes[i])
                                {
                                    target.Coordinates[i] = current.Coordinates[i];
                                }
                            }
                        }
                    }
                    if (result == 0)
                    {
                        result = api.MovePosition(target, linear, speedPercent, Zone, connectionId);
                    }
                }

                return FinishMoveCommand(result, "汇川机器人点位运动失败");
            }
        }

        public MotionStationResult MoveOffset(
            int basePointIndex,
            IReadOnlyList<double> offsets,
            StationMoveMode mode = StationMoveMode.Go)
        {
            lock (syncRoot)
            {
                MotionStationResult ready = EnsureReadyForMotion();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (offsets == null || offsets.Count < AxisCount || mode == StationMoveMode.Jog)
                {
                    return Fail(MotionStationResult.InvalidParameter, "汇川机器人偏移必须包含XYZUVW六个分量。", status.State);
                }
                for (int i = 0; i < AxisCount; i++)
                {
                    if (double.IsNaN(offsets[i]) || double.IsInfinity(offsets[i]))
                    {
                        return Fail(MotionStationResult.InvalidParameter, "汇川机器人偏移量必须是有限数。", status.State);
                    }
                }
                if (IsMoving())
                {
                    return MotionStationResult.Busy;
                }

                int result;
                InovanceRobotPose target;
                if (basePointIndex < 0)
                {
                    result = api.GetPosition(out target, connectionId);
                }
                else if (basePointIndex > MaximumRobotPointIndex)
                {
                    return Fail(MotionStationResult.InvalidParameter,
                        $"汇川机器人基准点索引超出0-{MaximumRobotPointIndex}。", status.State);
                }
                else if (configuration.PointFromRobot)
                {
                    result = api.GetPoint(basePointIndex, out target, connectionId);
                }
                else if (TryGetConfiguredPoint(basePointIndex, out DataPos configuredPoint))
                {
                    target = ToRobotPose(configuredPoint);
                    result = 0;
                }
                else
                {
                    return Fail(MotionStationResult.InvalidParameter,
                        $"汇川机器人基准点不存在：{basePointIndex}", status.State);
                }

                if (result != 0)
                {
                    return FailNative(MotionStationResult.ReceiveFailed, "读取汇川机器人偏移基准点失败", result);
                }
                for (int i = 0; i < AxisCount; i++)
                {
                    target.Coordinates[i] += offsets[i];
                }

                result = SetDataStreamMode(DataStreamOn);
                if (result == 0)
                {
                    result = api.SetRapidMove(1, 1, connectionId);
                }
                if (result == 0)
                {
                    result = api.MovePosition(target, mode == StationMoveMode.Move, speedPercent, Zone, connectionId);
                }
                return FinishMoveCommand(result, "汇川机器人偏移运动失败");
            }
        }

        public MotionStationResult AxisMotion(
            short axis,
            double offset,
            StationAxisMoveMode mode = StationAxisMoveMode.Relative,
            short tool = 0)
        {
            lock (syncRoot)
            {
                MotionStationResult ready = EnsureReadyForMotion();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (axis < 0 || axis >= AxisCount || double.IsNaN(offset) || double.IsInfinity(offset))
                {
                    return Fail(MotionStationResult.InvalidParameter, "汇川机器人人工轴运动参数无效。", status.State);
                }

                int vendorAxis = MapVendorAxis(axis);
                int result = SetDataStreamMode(DataStreamOff);
                if (result != 0)
                {
                    return FailNative(MotionStationResult.CommandRejected, "关闭汇川机器人数据流失败", result);
                }

                if (Math.Abs(offset) > JogThreshold)
                {
                    result = api.Jog(vendorAxis, offset < 0 ? -1 : 1, connectionId);
                }
                else
                {
                    result = api.SetInchMode(1, connectionId);
                    if (result == 0)
                    {
                        float step = (float)Math.Abs(offset);
                        result = api.SetStepMotion(step, step, connectionId);
                    }
                    if (result == 0)
                    {
                        result = api.SetInchStep(4, connectionId);
                    }
                    if (result == 0)
                    {
                        result = api.Inch(vendorAxis, offset < 0 ? -1 : 1, connectionId);
                    }
                }

                return FinishMoveCommand(result, "汇川机器人人工轴运动失败");
            }
        }

        public MotionStationResult WaitMoveFinish(bool isHome = false, int axis = -1, int timeoutMs = 120000)
        {
            if (timeoutMs < 0)
            {
                return Fail(MotionStationResult.InvalidParameter, "汇川机器人等待超时不能为负数。", status.State);
            }

            int remaining = timeoutMs;
            while (remaining >= 0)
            {
                lock (syncRoot)
                {
                    MotionStationResult initializedResult = EnsureInitialized();
                    if (initializedResult != MotionStationResult.Success)
                    {
                        return initializedResult;
                    }
                    int result = api.GetMotionStatus(out int motionStatus, connectionId);
                    if (result != 0)
                    {
                        return FailNative(MotionStationResult.ReceiveFailed, "读取汇川机器人运动状态失败", result);
                    }
                    if (motionStatus == 0)
                    {
                        status.State = MotionStationState.Idle;
                        status.IsHomed = isHome || status.IsHomed;
                        TryRefreshPosition();
                        return MotionStationResult.Success;
                    }
                    if (motionStatus != 1)
                    {
                        api.GetSystemError(out int systemError, connectionId);
                        status.HasAlarm = systemError != 0;
                        return Fail(MotionStationResult.CommandRejected,
                            $"汇川机器人运动已中断，控制器状态：{motionStatus}，错误：{systemError}",
                            MotionStationState.Faulted);
                    }
                    status.State = MotionStationState.Moving;
                }

                if (remaining == 0)
                {
                    break;
                }
                int delay = Math.Min(20, remaining);
                api.Delay(delay);
                remaining -= delay;
            }

            return Fail(MotionStationResult.Timeout, "等待汇川机器人运动完成超时。", status.State);
        }

        public MotionStationResult GetCurrentPosition(short tool, out DataPos position)
        {
            lock (syncRoot)
            {
                position = null;
                MotionStationResult ready = EnsureInitialized();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                int result = api.GetPosition(out InovanceRobotPose nativePosition, connectionId);
                if (result != 0)
                {
                    return FailNative(MotionStationResult.ReceiveFailed, "读取汇川机器人当前位置失败", result);
                }
                position = FromRobotPose(nativePosition);
                status.SetPosition(nativePosition.Coordinates);
                return MotionStationResult.Success;
            }
        }

        public MotionStationResult SavePoint(DataPos point)
        {
            lock (syncRoot)
            {
                MotionStationResult ready = EnsureReadyForMotion();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (point == null || point.Index < 0 || point.Index > MaximumRobotPointIndex
                    || !point.IsMotionReady)
                {
                    return Fail(MotionStationResult.InvalidParameter,
                        "汇川机器人保存点位参数无效。", status.State);
                }
                IReadOnlyList<double> values = point.GetAllValues();
                for (int axis = 0; axis < AxisCount; axis++)
                {
                    if (double.IsNaN(values[axis]) || double.IsInfinity(values[axis]))
                    {
                        return Fail(MotionStationResult.InvalidParameter,
                            "汇川机器人点位坐标必须是有限数。", status.State);
                    }
                }

                int result = api.SetPoint(point.Index, ToRobotPose(point), connectionId);
                return result == 0
                    ? MotionStationResult.Success
                    : FailNative(MotionStationResult.CommandRejected,
                        $"保存汇川机器人点位失败：{point.Index}", result);
            }
        }

        public MotionStationResult CreateTray(
            int trayId,
            int rowCount,
            int columnCount,
            IReadOnlyList<DataPos> referencePoints)
        {
            lock (syncRoot)
            {
                MotionStationResult ready = EnsureReadyForMotion();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (trayId < 0 || rowCount <= 0 || columnCount <= 0
                    || referencePoints == null || referencePoints.Count < 4)
                {
                    return Fail(MotionStationResult.InvalidParameter,
                        "汇川机器人料盘参数无效。", status.State);
                }
                for (int index = 0; index < 4; index++)
                {
                    DataPos point = referencePoints[index];
                    if (point == null || point.Index < 0
                        || point.Index > MaximumRobotPointIndex || !point.IsMotionReady)
                    {
                        return Fail(MotionStationResult.InvalidParameter,
                            "汇川机器人料盘参考点无效。", status.State);
                    }
                }

                pallets[trayId] = new PalletDefinition(
                    rowCount,
                    columnCount,
                    referencePoints[0].Index,
                    referencePoints[1].Index,
                    referencePoints[2].Index,
                    referencePoints[3].Index);
                return MotionStationResult.Success;
            }
        }

        public MotionStationResult MoveTrayPoint(int trayId, int position, DataPos calculatedPoint)
        {
            InovanceRobotPose target = null;
            lock (syncRoot)
            {
                MotionStationResult ready = EnsureReadyForMotion();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (!pallets.TryGetValue(trayId, out PalletDefinition pallet)
                    || position < 0 || position >= pallet.RowCount * pallet.ColumnCount)
                {
                    return Fail(MotionStationResult.InvalidParameter,
                        $"汇川机器人料盘{trayId}或位置{position}无效。", status.State);
                }
                if (IsMoving())
                {
                    return MotionStationResult.Busy;
                }

                int result = api.GetPoint(pallet.Point1, out InovanceRobotPose point1, connectionId);
                if (result == 0)
                {
                    result = api.GetPoint(pallet.Point2, out InovanceRobotPose point2, connectionId);
                    if (result == 0)
                    {
                        result = api.GetPoint(pallet.Point3, out InovanceRobotPose point3, connectionId);
                        if (result == 0)
                        {
                            result = api.ClearPallet(connectionId);
                        }
                        if (result == 0)
                        {
                            result = api.SetPalletParameters(
                                pallet.RowCount, pallet.ColumnCount, connectionId);
                        }
                        if (result == 0)
                        {
                            result = api.GetPalletPoint(
                                point1,
                                point2,
                                point3,
                                position / pallet.ColumnCount,
                                position % pallet.ColumnCount,
                                out target,
                                connectionId);
                            if (result == 0)
                            {
                                result = SetDataStreamMode(DataStreamOn);
                            }
                            if (result == 0)
                            {
                                result = api.GetPosition(out InovanceRobotPose approach, connectionId);
                                if (result == 0)
                                {
                                    for (int axis = 0; axis < AxisCount; axis++)
                                    {
                                        if (axis != 2)
                                        {
                                            approach.Coordinates[axis] = target.Coordinates[axis];
                                        }
                                    }
                                    Array.Copy(target.ArmParameters, approach.ArmParameters,
                                        target.ArmParameters.Length);
                                    result = api.MovePosition(
                                        approach, false, speedPercent, Zone, connectionId);
                                }
                            }
                        }
                    }
                }
                if (result != 0)
                {
                    return FailNative(MotionStationResult.CommandRejected,
                        "汇川机器人料盘点上方运动失败", result);
                }
                if (target == null)
                {
                    return Fail(MotionStationResult.ReceiveFailed,
                        "汇川机器人未返回料盘目标点。", status.State);
                }
                status.State = MotionStationState.Moving;
            }

            MotionStationResult approachResult = WaitMoveFinish(false, -1, 120000);
            if (approachResult != MotionStationResult.Success)
            {
                return approachResult;
            }

            lock (syncRoot)
            {
                MotionStationResult ready = EnsureReadyForMotion();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                int result = api.MovePosition(target, false, speedPercent, Zone, connectionId);
                return FinishMoveCommand(result, "汇川机器人料盘点运动失败");
            }
        }

        public MotionStationResult Stop(bool emergency = false)
        {
            lock (syncRoot)
            {
                MotionStationResult ready = EnsureInitialized();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }

                int firstError = 0;
                CaptureFirstError(ref firstError, SetDataStreamMode(DataStreamOff));
                if (emergency)
                {
                    CaptureFirstError(ref firstError, api.EmergencyStop(DataStreamOn, connectionId));
                    status.State = MotionStationState.Faulted;
                    status.HasAlarm = true;
                }
                else
                {
                    for (short axis = 0; axis < AxisCount; axis++)
                    {
                        CaptureFirstError(ref firstError, api.Jog(MapVendorAxis(axis), 0, connectionId));
                    }
                    api.Delay(5);
                    CaptureFirstError(ref firstError, SetDataStreamMode(DataStreamOn));
                    TryRefreshPosition();
                    status.State = MotionStationState.Idle;
                }

                return firstError == 0
                    ? MotionStationResult.Success
                    : FailNative(MotionStationResult.CommandRejected,
                        emergency ? "汇川机器人急停失败" : "汇川机器人停止失败", firstError);
            }
        }

        public MotionStationStatus GetStatus()
        {
            lock (syncRoot)
            {
                if (initialized)
                {
                    int result = api.GetMotionStatus(out int motionStatus, connectionId);
                    if (result != 0)
                    {
                        FailNative(MotionStationResult.ReceiveFailed,
                            "读取汇川机器人运动状态失败", result);
                        return status;
                    }
                    if (!pointsLoading && status.State != MotionStationState.Faulted)
                    {
                        status.State = motionStatus == 0 ? MotionStationState.Idle : MotionStationState.Moving;
                    }
                    TryRefreshPosition();
                    result = api.GetSystemError(out int systemError, connectionId);
                    if (result != 0)
                    {
                        FailNative(MotionStationResult.ReceiveFailed,
                            "读取汇川机器人故障状态失败", result);
                    }
                    else
                    {
                        status.HasAlarm = systemError != 0;
                        if (status.HasAlarm)
                        {
                            status.State = MotionStationState.Faulted;
                        }
                    }
                }
                return status;
            }
        }

        internal static int MapVendorAxis(short logicalAxis)
        {
            switch (logicalAxis)
            {
                case 3:
                    return 6;
                case 4:
                    return 4;
                case 5:
                    return 5;
                default:
                    return logicalAxis + 1;
            }
        }

        private MotionStationResult EnsureInitialized()
        {
            if (!lifecycleInitialized)
            {
                return Fail(MotionStationResult.NotInitialized,
                    "汇川机器人工站尚未初始化。", MotionStationState.Uninitialized);
            }
            if (!initialized || !nativeConnectionOpen)
            {
                return Fail(MotionStationResult.NotConnected,
                    "汇川机器人尚未连接，后台正在重试。", MotionStationState.Disconnected);
            }
            return MotionStationResult.Success;
        }

        private MotionStationResult EnsureReadyForMotion()
        {
            MotionStationResult initializedResult = EnsureInitialized();
            if (initializedResult != MotionStationResult.Success)
            {
                return initializedResult;
            }
            if (status.State == MotionStationState.Moving)
            {
                return MotionStationResult.Busy;
            }
            if (status.State == MotionStationState.Faulted)
            {
                return Fail(MotionStationResult.CommandRejected, "汇川机器人处于故障状态。", status.State);
            }
            return MotionStationResult.Success;
        }

        private bool IsMoving()
        {
            int result = api.GetMotionStatus(out int motionStatus, connectionId);
            if (result != 0)
            {
                FailNative(MotionStationResult.ReceiveFailed, "读取汇川机器人运动状态失败", result);
                return true;
            }
            if (motionStatus != 0)
            {
                status.State = MotionStationState.Moving;
                return true;
            }
            return false;
        }

        private int SetDataStreamMode(int desiredMode)
        {
            int result = api.GetDataStreamMode(out int currentMode, connectionId);
            if (result != 0)
            {
                return result;
            }
            if (desiredMode == DataStreamOn)
            {
                if (currentMode == DataStreamOff)
                {
                    result = api.SetDataStreamMode(DataStreamOn, connectionId);
                    if (result == 0)
                    {
                        result = api.SetSlewMode(0, connectionId);
                    }
                }
                else if (currentMode == DataStreamPause)
                {
                    result = api.SetDataStreamMode(DataStreamContinue, connectionId);
                }
            }
            else if (currentMode != DataStreamOff)
            {
                result = api.SetDataStreamMode(DataStreamOff, connectionId);
            }
            return result;
        }

        private MotionStationResult FinishMoveCommand(int result, string message)
        {
            if (result != 0)
            {
                return FailNative(MotionStationResult.CommandRejected, message, result);
            }
            api.Delay(50);
            status.State = MotionStationState.Moving;
            status.LastError = string.Empty;
            return MotionStationResult.Success;
        }

        private bool TryResolveEndpoint(
            out uint address,
            out ushort port,
            out int timeoutSeconds,
            out int resolvedConnectionId,
            out string error)
        {
            address = 0;
            port = 0;
            timeoutSeconds = 1;
            resolvedConnectionId = 0;
            error = null;

            if (string.IsNullOrWhiteSpace(configuration.CommunicationName))
            {
                error = $"机器人工站“{configuration.Name}”未配置通讯对象。";
                return false;
            }
            if (!communicationStore.TryGetSocket(configuration.CommunicationName, out SocketInfo socket))
            {
                error = $"机器人工站“{configuration.Name}”引用的通讯对象不存在：{configuration.CommunicationName}";
                return false;
            }
            if (!string.Equals(socket.Type, "Client", StringComparison.Ordinal))
            {
                error = $"汇川机器人通讯对象必须是TCP客户端：{socket.Name}";
                return false;
            }
            if (!IPAddress.TryParse(socket.RemoteAddress, out IPAddress parsedAddress)
                || parsedAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                error = $"汇川机器人远端IPv4地址无效：{socket.RemoteAddress}";
                return false;
            }
            if (socket.RemotePort <= 0 || socket.RemotePort > ushort.MaxValue)
            {
                error = $"汇川机器人远端端口无效：{socket.RemotePort}";
                return false;
            }
            byte[] bytes = parsedAddress.GetAddressBytes();
            address = ((uint)bytes[0] << 24)
                | ((uint)bytes[1] << 16)
                | ((uint)bytes[2] << 8)
                | bytes[3];
            port = (ushort)socket.RemotePort;
            timeoutSeconds = Math.Max(1, (socket.ConnectTimeoutMs + 999) / 1000);
            // 3.0 以机器人端口尝试绑定 SDK 通讯槽；常规网络端口超出0~4时固定使用0号槽。
            resolvedConnectionId = socket.RemotePort <= 4 ? socket.RemotePort : 0;
            return true;
        }

        private bool TryGetConfiguredPoint(int pointIndex, out DataPos point)
        {
            if (configuration.dicDataPos != null)
            {
                foreach (DataPos candidate in configuration.dicDataPos.Values)
                {
                    if (candidate != null && candidate.Index == pointIndex && candidate.IsMotionReady)
                    {
                        point = candidate;
                        return true;
                    }
                }
            }
            if (configuration.ListDataPos != null)
            {
                foreach (DataPos candidate in configuration.ListDataPos)
                {
                    if (candidate != null && candidate.Index == pointIndex && candidate.IsMotionReady)
                    {
                        point = candidate;
                        return true;
                    }
                }
            }
            point = null;
            return false;
        }

        private PointLoadRequest CapturePointLoadRequest()
        {
            lock (configuration)
            {
                DataPos[] points = configuration.PointFromRobot
                    ? (configuration.ListDataPos ?? new List<DataPos>())
                        .Where(point => point != null && !string.IsNullOrWhiteSpace(point.Name))
                        .Select(point => (DataPos)point.Clone())
                        .ToArray()
                    : Array.Empty<DataPos>();
                return new PointLoadRequest(configuration.PointFromRobot, points);
            }
        }

        /// <summary>
        /// 此阶段只持有厂商 SDK 工站锁并读取控制器，不触碰配置锁。
        /// </summary>
        private MotionStationResult ReadConfiguredPointsFromRobot(
            PointLoadRequest request,
            out PointLoadBatch batch)
        {
            batch = null;
            var points = new List<RobotPointRead>();
            if (request.PointFromRobot)
            {
                foreach (DataPos configuredPoint in request.Points)
                {
                    if (configuredPoint.Index < 0
                        || configuredPoint.Index > MaximumRobotPointIndex)
                    {
                        return Fail(
                            MotionStationResult.InvalidConfiguration,
                            $"汇川机器人点位“{configuredPoint.Name}”索引超出0-{MaximumRobotPointIndex}：{configuredPoint.Index}",
                            MotionStationState.Faulted);
                    }
                    int readResult = api.GetPoint(
                        configuredPoint.Index,
                        out InovanceRobotPose robotPoint,
                        connectionId);
                    if (readResult != 0)
                    {
                        return FailNative(
                            MotionStationResult.ReceiveFailed,
                            $"加载汇川机器人点位失败：{configuredPoint.Index}",
                            readResult);
                    }
                    points.Add(new RobotPointRead(configuredPoint, robotPoint));
                }
            }

            int currentResult = api.GetPosition(out InovanceRobotPose current, connectionId);
            if (currentResult != 0)
            {
                return FailNative(
                    MotionStationResult.ReceiveFailed,
                    "读取汇川机器人当前位置失败",
                    currentResult);
            }

            batch = new PointLoadBatch(request.PointFromRobot, points, current);
            return MotionStationResult.Success;
        }

        /// <summary>
        /// 此阶段只持有配置锁并提交读取结果，不进入厂商 SDK 工站锁。
        /// </summary>
        private MotionStationResult CommitConfiguredPoints(
            PointLoadBatch batch,
            out string error)
        {
            error = null;
            if (!batch.PointFromRobot || batch.Points.Count == 0)
            {
                return MotionStationResult.Success;
            }

            lock (configuration)
            {
                if (!configuration.PointFromRobot)
                {
                    error = "汇川机器人点位读取期间配置来源已改变，未覆盖较新的配置。";
                    return MotionStationResult.BaseFunctionError;
                }

                var updates = new List<PointUpdate>();
                foreach (RobotPointRead pointRead in batch.Points)
                {
                    DataPos configuredPoint = (configuration.ListDataPos
                        ?? new List<DataPos>()).FirstOrDefault(point => point != null
                            && point.Index == pointRead.ConfigurationSnapshot.Index);
                    if (!PointSnapshotStillCurrent(
                        configuredPoint,
                        pointRead.ConfigurationSnapshot))
                    {
                        error = $"汇川机器人点位“{pointRead.ConfigurationSnapshot.Name}”读取期间已被并发修改，未覆盖较新的配置。";
                        return MotionStationResult.BaseFunctionError;
                    }
                    updates.Add(new PointUpdate(configuredPoint, pointRead.ControllerPoint));
                }

                foreach (PointUpdate update in updates)
                {
                    update.Apply();
                }
                if (stationStore != null
                    && !stationStore.TryPersistCurrent(paths.ConfigPath, out string persistError))
                {
                    foreach (PointUpdate update in updates)
                    {
                        update.Rollback();
                    }
                    error = $"汇川机器人点位已读取但持久化失败，内存修改已回滚：{persistError}";
                    return MotionStationResult.BaseFunctionError;
                }
            }
            return MotionStationResult.Success;
        }

        private static InovanceRobotPose ToRobotPose(DataPos point)
        {
            var result = new InovanceRobotPose();
            IReadOnlyList<double> values = point.GetAllValues();
            for (int i = 0; i < AxisCount; i++)
            {
                result.Coordinates[i] = values[i];
            }
            if (point.Pose != null)
            {
                int poseCount = Math.Min(point.Pose.Length, result.ArmParameters.Length);
                for (int i = 0; i < poseCount; i++)
                {
                    result.ArmParameters[i] = point.Pose[i] == 1 ? -1 : point.Pose[i] == 0 ? 1 : 0;
                }
            }
            return result;
        }

        private static DataPos FromRobotPose(InovanceRobotPose position)
        {
            var result = new DataPos(-1)
            {
                X = position.Coordinates[0],
                Y = position.Coordinates[1],
                Z = position.Coordinates[2],
                U = position.Coordinates[3],
                V = position.Coordinates[4],
                W = position.Coordinates[5],
                IsTaught = true
            };
            int poseCount = Math.Min(result.Pose.Length, position.ArmParameters.Length);
            for (int i = 0; i < poseCount; i++)
            {
                result.Pose[i] = position.ArmParameters[i] == -1
                    ? (short)1
                    : position.ArmParameters[i] == 1 ? (short)0 : (short)2;
            }
            return result;
        }

        private static void ApplyRobotPose(DataPos target, InovanceRobotPose source)
        {
            DataPos converted = FromRobotPose(source);
            target.X = converted.X;
            target.Y = converted.Y;
            target.Z = converted.Z;
            target.U = converted.U;
            target.V = converted.V;
            target.W = converted.W;
            target.Pose = converted.Pose.ToArray();
            target.IsTaught = true;
        }

        private static bool PointSnapshotStillCurrent(DataPos current, DataPos snapshot)
        {
            return current != null
                && snapshot != null
                && current.Index == snapshot.Index
                && string.Equals(current.Name, snapshot.Name, StringComparison.Ordinal)
                && current.IsTaught == snapshot.IsTaught
                && current.GetAllValues().SequenceEqual(snapshot.GetAllValues())
                && (ReferenceEquals(current.Pose, snapshot.Pose)
                    || (current.Pose != null && snapshot.Pose != null
                        && current.Pose.SequenceEqual(snapshot.Pose)));
        }

        private void TryRefreshPosition()
        {
            if (api.GetPosition(out InovanceRobotPose position, connectionId) == 0)
            {
                status.SetPosition(position.Coordinates);
            }
        }

        private MotionStationResult FailNative(
            MotionStationResult result,
            string message,
            int nativeResult,
            MotionStationState? forcedState = null)
        {
            MotionStationState targetState = forcedState
                ?? (nativeResult == -255 || nativeResult == -510
                    ? MotionStationState.Disconnected
                    : MotionStationState.Faulted);
            if (targetState == MotionStationState.Disconnected)
            {
                initialized = false;
                status.IsServoEnabled = false;
            }
            return Fail(result, $"{message}，返回值：{nativeResult}", targetState);
        }

        private MotionStationResult Fail(MotionStationResult result, string message, MotionStationState state)
        {
            status.LastError = message ?? string.Empty;
            status.State = state;
            return result;
        }

        private static bool HasDisabledAxis(bool[] disabledAxes)
        {
            if (disabledAxes == null)
            {
                return false;
            }
            for (int i = 0; i < AxisCount; i++)
            {
                if (disabledAxes[i])
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsFinitePositive(double value) =>
            value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

        private static double ClampPercent(double value) => Math.Max(1d, Math.Min(100d, value));

        private static void CaptureFirstError(ref int firstError, int result)
        {
            if (firstError == 0 && result != 0)
            {
                firstError = result;
            }
        }

        private static bool IsNativeBoundaryException(Exception exception) =>
            exception is DllNotFoundException
            || exception is EntryPointNotFoundException
            || exception is BadImageFormatException
            || exception is SEHException;

        private sealed class PalletDefinition
        {
            public PalletDefinition(
                int rowCount,
                int columnCount,
                int point1,
                int point2,
                int point3,
                int point4)
            {
                RowCount = rowCount;
                ColumnCount = columnCount;
                Point1 = point1;
                Point2 = point2;
                Point3 = point3;
                Point4 = point4;
            }

            public int RowCount { get; }
            public int ColumnCount { get; }
            public int Point1 { get; }
            public int Point2 { get; }
            public int Point3 { get; }
            public int Point4 { get; }
        }

        private sealed class PointLoadRequest
        {
            public PointLoadRequest(bool pointFromRobot, IReadOnlyList<DataPos> points)
            {
                PointFromRobot = pointFromRobot;
                Points = points ?? Array.Empty<DataPos>();
            }

            public bool PointFromRobot { get; }
            public IReadOnlyList<DataPos> Points { get; }
        }

        private sealed class RobotPointRead
        {
            public RobotPointRead(DataPos configurationSnapshot, InovanceRobotPose controllerPoint)
            {
                ConfigurationSnapshot = configurationSnapshot;
                ControllerPoint = controllerPoint;
            }

            public DataPos ConfigurationSnapshot { get; }
            public InovanceRobotPose ControllerPoint { get; }
        }

        private sealed class PointLoadBatch
        {
            public PointLoadBatch(
                bool pointFromRobot,
                IReadOnlyList<RobotPointRead> points,
                InovanceRobotPose currentPosition)
            {
                PointFromRobot = pointFromRobot;
                Points = points ?? Array.Empty<RobotPointRead>();
                CurrentPosition = currentPosition;
            }

            public bool PointFromRobot { get; }
            public IReadOnlyList<RobotPointRead> Points { get; }
            public InovanceRobotPose CurrentPosition { get; }
        }

        private sealed class PointUpdate
        {
            private readonly DataPos target;
            private readonly DataPos previous;
            private readonly InovanceRobotPose source;

            public PointUpdate(DataPos target, InovanceRobotPose source)
            {
                this.target = target;
                this.source = source;
                previous = (DataPos)target.Clone();
            }

            public void Apply()
            {
                ApplyRobotPose(target, source);
            }

            public void Rollback()
            {
                target.X = previous.X;
                target.Y = previous.Y;
                target.Z = previous.Z;
                target.U = previous.U;
                target.V = previous.V;
                target.W = previous.W;
                target.Pose = previous.Pose?.ToArray();
                target.IsTaught = previous.IsTaught;
            }
        }
    }
}
