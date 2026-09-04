// 模块：运动控制 / EPSON 机器人。
// 职责范围：把 3.0 EPSON 机器人直接实现为当前平台六轴工站，不持有全局通讯或旧平台对象。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Automation.MotionControl
{
    internal sealed class EpsonStation : IMotionStation
    {
        private const int AxisCount = 6;
        private const int MaximumRobotPointIndex = DataStation.RobotPointCapacity - 1;
        private const int CommandTimeoutMs = 2000;

        private readonly object operationGate = new object();
        private readonly object statusGate = new object();
        private readonly DataStation configuration;
        private readonly StationDefinitionStore stationStore;
        private readonly CommunicationHub communication;
        private readonly CommunicationConfigStore communicationStore;
        private readonly PlatformPaths paths;
        private readonly ILogger logger;
        private readonly Func<string, string, bool> testSend;
        private readonly Func<string, int, CancellationToken, CommReceiveResult> testReceive;
        private readonly Func<string, string, int, CommReceiveResult> testExchange;
        private readonly Func<string, bool> testIsChannelActive;
        private readonly MotionStationStatus status = new MotionStationStatus();

        private EpsonCommandCatalog commands;
        private CancellationTokenSource pendingMoveWait;
        private bool initialized;
        private bool ownsCommandChannel;
        private bool ownsRemoteChannel;
        private volatile bool remoteLoggedIn;
        private volatile bool pointsLoadedFromRobot;

        internal EpsonStation(
            DataStation configuration,
            StationDefinitionStore stationStore,
            CommunicationHub communication,
            CommunicationConfigStore communicationStore,
            PlatformPaths paths,
            ILogger logger)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.stationStore = stationStore ?? throw new ArgumentNullException(nameof(stationStore));
            this.communication = communication ?? throw new ArgumentNullException(nameof(communication));
            this.communicationStore = communicationStore
                ?? throw new ArgumentNullException(nameof(communicationStore));
            this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>仅供无网络特征测试使用；生产组合始终使用 CommunicationHub 构造函数。</summary>
        internal EpsonStation(
            DataStation configuration,
            EpsonCommandCatalog commands,
            Func<string, string, bool> send,
            Func<string, int, CancellationToken, CommReceiveResult> receive,
            Func<string, string, int, CommReceiveResult> exchange,
            ILogger logger,
            Func<string, bool> isChannelActive = null)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            testSend = send ?? throw new ArgumentNullException(nameof(send));
            testReceive = receive ?? throw new ArgumentNullException(nameof(receive));
            testExchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
            testIsChannelActive = isChannelActive;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public MotionStationResult Initialize()
        {
            lock (operationGate)
            {
                if (initialized)
                {
                    return MotionStationResult.Success;
                }
                configuration.NormalizeConfiguration();
                MotionStationResult validation = ValidateConfiguration(out string validationError);
                if (validation != MotionStationResult.Success)
                {
                    return Fail(validation, validationError, MotionStationState.Faulted);
                }

                if (commands == null
                    && !EpsonCommandCatalog.TryLoad(paths, out commands, out string commandError))
                {
                    return Fail(
                        MotionStationResult.InvalidConfiguration,
                        commandError,
                        MotionStationState.Faulted);
                }

                if (communication != null)
                {
                    MotionStationResult startResult = StartConfiguredChannel(
                        configuration.CommunicationName,
                        ref ownsCommandChannel,
                        out string startError);
                    if (startResult != MotionStationResult.Success)
                    {
                        return Fail(startResult, startError, MotionStationState.Faulted);
                    }
                    if (configuration.RemoteMode)
                    {
                        startResult = StartConfiguredChannel(
                            configuration.RemoteCommunicationName,
                            ref ownsRemoteChannel,
                            out startError);
                        if (startResult != MotionStationResult.Success)
                        {
                            StopOwnedChannel(configuration.CommunicationName, ref ownsCommandChannel);
                            return Fail(startResult, startError, MotionStationState.Faulted);
                        }
                    }
                }

                initialized = true;
                remoteLoggedIn = !configuration.RemoteMode;
                pointsLoadedFromRobot = !configuration.PointFromRobot;
                SetState(AreRequiredChannelsActive()
                    ? MotionStationState.Idle
                    : MotionStationState.Disconnected);

                // 与 3.0 一致：通道创建成功但现场尚未连接时初始化本身仍成功，后续命令明确返回未连接。
                if (!AreRequiredChannelsActive())
                {
                    Log($"EPSON 工站“{configuration.Name}”已初始化，等待机器人通讯连接。", LogLevel.Normal);
                    return MotionStationResult.Success;
                }

                MotionStationResult readyResult = PrepareConnectedRobot();
                if (readyResult != MotionStationResult.Success)
                {
                    return readyResult;
                }
                ClearError();
                Log($"EPSON 六轴工站“{configuration.Name}”初始化完成。", LogLevel.Normal);
                return MotionStationResult.Success;
            }
        }

        public MotionStationResult Release()
        {
            CancelPendingMoveWait();
            lock (operationGate)
            {
                MotionStationResult result = MotionStationResult.Success;
                if (communication != null)
                {
                    if (!StopOwnedChannel(configuration.RemoteCommunicationName, ref ownsRemoteChannel))
                    {
                        result = MotionStationResult.BaseFunctionError;
                    }
                    if (!StopOwnedChannel(configuration.CommunicationName, ref ownsCommandChannel))
                    {
                        result = MotionStationResult.BaseFunctionError;
                    }
                }
                initialized = false;
                remoteLoggedIn = false;
                pointsLoadedFromRobot = false;
                SetState(MotionStationState.Uninitialized);
                return result;
            }
        }

        public MotionStationResult Home(short axis = -1, bool wait = true, bool group = false)
        {
            lock (operationGate)
            {
                MotionStationResult ready = EnsureIdle();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (!TryBuild(EpsonCommandCatalog.Home, out string command))
                {
                    return MotionStationResult.InvalidConfiguration;
                }
                // 3.0 的机器人 Home 是整机命令，axis、wait、group 仅为统一工站签名保留。
                return SendMoveCommand(command);
            }
        }

        public MotionStationResult SetSpeed(
            double velocity,
            double acceleration,
            double deceleration,
            short axis = -1,
            StationSpeedType type = StationSpeedType.Joint)
        {
            lock (operationGate)
            {
                MotionStationResult ready = EnsureIdle();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (!IsFiniteNonNegative(velocity)
                    || !IsFiniteNonNegative(acceleration)
                    || !IsFiniteNonNegative(deceleration)
                    || !Enum.IsDefined(typeof(StationSpeedType), type))
                {
                    return Fail(MotionStationResult.InvalidParameter, "EPSON 速度参数无效。");
                }
                if (!TryBuild(
                    EpsonCommandCatalog.SetSpeed,
                    out string command,
                    velocity,
                    acceleration,
                    deceleration))
                {
                    return MotionStationResult.InvalidConfiguration;
                }
                return SendWithWaitOk(configuration.CommunicationName, command, CommandTimeoutMs);
            }
        }

        public MotionStationResult MoveToPoint(
            DataPos point,
            StationMoveMode mode,
            bool[] disabledAxes = null,
            short tool = 0)
        {
            lock (operationGate)
            {
                MotionStationResult ready = EnsureIdle();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (!IsValidMotionPoint(point) || tool < 0 || !Enum.IsDefined(typeof(StationMoveMode), mode))
                {
                    return Fail(MotionStationResult.InvalidParameter, "EPSON 目标点位、运动模式或工具号无效。");
                }

                string template = mode == StationMoveMode.Go
                    ? EpsonCommandCatalog.GoPoint
                    : EpsonCommandCatalog.MovePoint;
                if (!TryBuild(template, out string command, AxisCount, point.Index, tool))
                {
                    return MotionStationResult.InvalidConfiguration;
                }
                // 机器人的六通道由控制器统一规划，disabledAxes 不拆分成六条单轴指令。
                return SendMoveCommand(command);
            }
        }

        public MotionStationResult MoveOffset(
            int basePointIndex,
            IReadOnlyList<double> offsets,
            StationMoveMode mode = StationMoveMode.Go)
        {
            lock (operationGate)
            {
                MotionStationResult ready = EnsureIdle();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (basePointIndex > MaximumRobotPointIndex
                    || offsets == null
                    || offsets.Count < AxisCount
                    || !Enum.IsDefined(typeof(StationMoveMode), mode)
                    || offsets.Take(AxisCount).Any(value => double.IsNaN(value) || double.IsInfinity(value)))
                {
                    return Fail(MotionStationResult.InvalidParameter, "EPSON 偏移运动参数无效。");
                }
                int normalizedBasePoint = basePointIndex < 0 ? 0 : basePointIndex;
                if (!TryBuild(
                    EpsonCommandCatalog.MoveOffset,
                    out string command,
                    AxisCount,
                    normalizedBasePoint,
                    offsets[0],
                    offsets[1],
                    offsets[2],
                    offsets[3],
                    offsets[4],
                    offsets[5]))
                {
                    return MotionStationResult.InvalidConfiguration;
                }
                return SendMoveCommand(command);
            }
        }

        public MotionStationResult AxisMotion(
            short axis,
            double offset,
            StationAxisMoveMode mode = StationAxisMoveMode.Relative,
            short tool = 0)
        {
            if (axis < 0 || axis >= AxisCount || double.IsNaN(offset) || double.IsInfinity(offset)
                || !Enum.IsDefined(typeof(StationAxisMoveMode), mode))
            {
                return Fail(MotionStationResult.InvalidParameter, "EPSON 手动运动通道或偏移量无效。");
            }

            // 延续 3.0：XYZUVW 任一通道的单次偏移都限幅到 ±20，防止连续运动偏移过大报警。
            var offsets = new double[AxisCount];
            offsets[axis] = Math.Max(-20d, Math.Min(20d, offset));
            return MoveOffset(0, offsets, StationMoveMode.Go);
        }

        public MotionStationResult WaitMoveFinish(
            bool isHome = false,
            int axis = -1,
            int timeoutMs = 120000)
        {
            if (timeoutMs <= 0)
            {
                return Fail(MotionStationResult.InvalidParameter, "EPSON 等待运动完成超时参数无效。");
            }

            CancellationTokenSource waitSource;
            lock (statusGate)
            {
                if (!initialized)
                {
                    return MotionStationResult.NotInitialized;
                }
                if (status.State == MotionStationState.Idle)
                {
                    return MotionStationResult.Success;
                }
                if (status.State != MotionStationState.Moving)
                {
                    return status.State == MotionStationState.Disconnected
                        ? MotionStationResult.NotConnected
                        : MotionStationResult.BaseFunctionError;
                }
                pendingMoveWait?.Cancel();
                pendingMoveWait?.Dispose();
                waitSource = new CancellationTokenSource();
                pendingMoveWait = waitSource;
            }

            CommReceiveResult response = Receive(
                configuration.CommunicationName,
                timeoutMs,
                waitSource.Token);
            lock (statusGate)
            {
                if (ReferenceEquals(pendingMoveWait, waitSource))
                {
                    pendingMoveWait = null;
                }
            }
            waitSource.Dispose();

            if (!response.Success)
            {
                MotionStationResult receiveResult = IsTimeout(response.ErrorMessage)
                    ? MotionStationResult.Timeout
                    : MotionStationResult.ReceiveFailed;
                return Fail(receiveResult, $"EPSON 等待运动完成失败：{response.ErrorMessage}");
            }
            if (!TryParsePositionResponse(response.MessageText, out double[] coordinates, out string parseError))
            {
                return Fail(MotionStationResult.CommandRejected, parseError, MotionStationState.Faulted);
            }

            lock (statusGate)
            {
                status.SetPosition(coordinates);
                status.State = MotionStationState.Idle;
                status.IsHomed = status.IsHomed || isHome;
                status.HasAlarm = false;
                status.LastError = string.Empty;
            }
            return MotionStationResult.Success;
        }

        public MotionStationResult GetCurrentPosition(short tool, out DataPos position)
        {
            lock (operationGate)
            {
                position = null;
                MotionStationResult ready = EnsureIdle();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (tool < 0)
                {
                    return Fail(MotionStationResult.InvalidParameter, "EPSON 工具号无效。");
                }
                MotionStationResult result = ReadRobotPosition(-1, out double[] coordinates);
                if (result != MotionStationResult.Success)
                {
                    return result;
                }
                position = CreatePosition(-1, coordinates);
                lock (statusGate)
                {
                    status.SetPosition(coordinates);
                }
                return MotionStationResult.Success;
            }
        }

        public MotionStationResult Stop(bool emergency = false)
        {
            CancelPendingMoveWait();
            lock (operationGate)
            {
                if (!initialized)
                {
                    return MotionStationResult.NotInitialized;
                }
                if (!IsChannelActive(configuration.CommunicationName))
                {
                    SetState(MotionStationState.Disconnected);
                    return Fail(MotionStationResult.NotConnected, "EPSON 主命令通讯未连接。");
                }
                const string command = "RobotStop\r\n";
                if (!Send(configuration.CommunicationName, command))
                {
                    return Fail(
                        MotionStationResult.SendFailed,
                        "EPSON 停止命令发送失败。",
                        MotionStationState.Disconnected);
                }

                // RobotStop 本身没有完成应答。沿用 3.0 的 RobotGetPosition 协议做一次
                // 控制器往返确认；只有控制器已受理 Stop 且能返回当前位置，才允许转为空闲。
                MotionStationResult confirmation = ReadRobotPosition(
                    -1,
                    out double[] coordinates);
                if (confirmation != MotionStationResult.Success)
                {
                    string confirmationError;
                    lock (statusGate)
                    {
                        confirmationError = status.LastError;
                    }
                    return Fail(
                        confirmation,
                        $"EPSON 停止命令已发送，但未能确认机器人停稳：{confirmationError}",
                        MotionStationState.Faulted);
                }
                lock (statusGate)
                {
                    status.SetPosition(coordinates);
                    status.State = MotionStationState.Idle;
                    status.HasAlarm = false;
                    status.LastError = string.Empty;
                }
                return MotionStationResult.Success;
            }
        }

        public MotionStationStatus GetStatus()
        {
            lock (statusGate)
            {
                if (initialized && status.State != MotionStationState.Faulted
                    && !AreRequiredChannelsActive())
                {
                    MarkDisconnected();
                }
                return status;
            }
        }

        public MotionStationResult SavePoint(DataPos point)
        {
            lock (operationGate)
            {
                MotionStationResult ready = EnsureIdle();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (!IsValidMotionPoint(point))
                {
                    return Fail(MotionStationResult.InvalidParameter, "EPSON 保存点位无效或尚未示教。");
                }
                if (!TryBuild(
                    EpsonCommandCatalog.SavePoint,
                    out string command,
                    AxisCount,
                    point.Index,
                    point.X,
                    point.Y,
                    point.Z,
                    point.U,
                    point.V,
                    point.W))
                {
                    return MotionStationResult.InvalidConfiguration;
                }
                return SendWithWaitOk(configuration.CommunicationName, command, CommandTimeoutMs);
            }
        }

        public MotionStationResult CreateTray(
            int trayId,
            int rowCount,
            int columnCount,
            IReadOnlyList<DataPos> referencePoints)
        {
            lock (operationGate)
            {
                MotionStationResult ready = EnsureIdle();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (trayId < 0 || rowCount <= 0 || columnCount <= 0
                    || referencePoints == null || referencePoints.Count < 4
                    || referencePoints.Take(4).Any(point => !IsValidMotionPoint(point)))
                {
                    return Fail(MotionStationResult.InvalidParameter, "EPSON 料盘参数或参考点无效。");
                }
                if (!TryBuild(
                    EpsonCommandCatalog.CreatePallet,
                    out string command,
                    trayId,
                    referencePoints[0].Index,
                    referencePoints[1].Index,
                    referencePoints[2].Index,
                    referencePoints[3].Index,
                    columnCount,
                    rowCount))
                {
                    return MotionStationResult.InvalidConfiguration;
                }
                return SendWithWaitOk(configuration.CommunicationName, command, CommandTimeoutMs);
            }
        }

        public MotionStationResult MoveTrayPoint(int trayId, int position, DataPos calculatedPoint)
        {
            lock (operationGate)
            {
                MotionStationResult ready = EnsureIdle();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (trayId < 0 || position < 0)
                {
                    return Fail(MotionStationResult.InvalidParameter, "EPSON 料盘号或位置无效。");
                }
                if (!TryBuild(
                    EpsonCommandCatalog.GoPalletPosition,
                    out string command,
                    trayId,
                    position))
                {
                    return MotionStationResult.InvalidConfiguration;
                }
                return SendMoveCommand(command);
            }
        }

        internal MotionStationResult GetPoint(int pointIndex, out DataPos point)
        {
            lock (operationGate)
            {
                point = null;
                MotionStationResult ready = EnsureIdle();
                if (ready != MotionStationResult.Success)
                {
                    return ready;
                }
                if (!IsValidPointIndex(pointIndex))
                {
                    return Fail(MotionStationResult.InvalidParameter, "EPSON 点位索引无效。");
                }
                MotionStationResult result = ReadRobotPosition(pointIndex, out double[] coordinates);
                if (result == MotionStationResult.Success)
                {
                    point = CreatePosition(pointIndex, coordinates);
                }
                return result;
            }
        }

        private MotionStationResult ValidateConfiguration(out string error)
        {
            error = null;
            if (configuration.Type != StationType.Epson)
            {
                error = "EPSON 工站实现只能接收 Epson 类型配置。";
                return MotionStationResult.InvalidConfiguration;
            }
            if (string.IsNullOrWhiteSpace(configuration.Name))
            {
                error = "EPSON 工站名称为空。";
                return MotionStationResult.InvalidConfiguration;
            }
            if (string.IsNullOrWhiteSpace(configuration.CommunicationName))
            {
                error = $"EPSON 工站“{configuration.Name}”未配置主命令通讯对象。";
                return MotionStationResult.InvalidConfiguration;
            }
            if (configuration.RemoteMode)
            {
                if (string.IsNullOrWhiteSpace(configuration.RemoteCommunicationName))
                {
                    error = $"EPSON 工站“{configuration.Name}”已启用远程模式，但未配置独立远程通讯对象。";
                    return MotionStationResult.InvalidConfiguration;
                }
                if (string.Equals(
                    configuration.CommunicationName,
                    configuration.RemoteCommunicationName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    error = $"EPSON 工站“{configuration.Name}”的主命令通讯与远程通讯不能使用同一对象。";
                    return MotionStationResult.InvalidConfiguration;
                }
            }
            if (communicationStore != null)
            {
                if (!communicationStore.TryGetSocket(configuration.CommunicationName, out _))
                {
                    error = $"EPSON 主命令通讯对象不存在：{configuration.CommunicationName}";
                    return MotionStationResult.InvalidConfiguration;
                }
                if (configuration.RemoteMode
                    && !communicationStore.TryGetSocket(configuration.RemoteCommunicationName, out _))
                {
                    error = $"EPSON 远程通讯对象不存在：{configuration.RemoteCommunicationName}";
                    return MotionStationResult.InvalidConfiguration;
                }
            }
            return MotionStationResult.Success;
        }

        private MotionStationResult StartConfiguredChannel(
            string name,
            ref bool ownsChannel,
            out string error)
        {
            error = null;
            if (!communicationStore.TryGetSocket(name, out SocketInfo socket))
            {
                error = $"EPSON 通讯对象不存在：{name}";
                return MotionStationResult.InvalidConfiguration;
            }
            bool wasStarted = communication.GetTcpStatus(name).IsStarted;
            try
            {
                communication.StartTcpAsync(socket).GetAwaiter().GetResult();
                ownsChannel = !wasStarted;
                return MotionStationResult.Success;
            }
            catch (Exception ex)
            {
                error = $"EPSON 通讯对象启动失败：{name}；{ex.Message}";
                return MotionStationResult.NotConnected;
            }
        }

        private bool StopOwnedChannel(string name, ref bool ownsChannel)
        {
            if (!ownsChannel || string.IsNullOrWhiteSpace(name))
            {
                ownsChannel = false;
                return true;
            }
            try
            {
                communication.StopTcpAsync(name).GetAwaiter().GetResult();
                ownsChannel = false;
                return true;
            }
            catch (Exception ex)
            {
                Log($"EPSON 通讯对象释放失败：{name}；{ex.Message}", LogLevel.Error);
                ownsChannel = false;
                return false;
            }
        }

        private MotionStationResult EnsureIdle()
        {
            if (!initialized)
            {
                return Fail(MotionStationResult.NotInitialized, "EPSON 工站尚未初始化。");
            }
            if (!AreRequiredChannelsActive())
            {
                return Fail(
                    MotionStationResult.NotConnected,
                    "EPSON 机器人通讯未连接。",
                    MotionStationState.Disconnected);
            }

            MotionStationState currentState;
            lock (statusGate)
            {
                currentState = status.State;
                if (currentState == MotionStationState.Disconnected)
                {
                    status.State = MotionStationState.Idle;
                    currentState = MotionStationState.Idle;
                }
            }
            if (currentState == MotionStationState.Moving)
            {
                return Fail(MotionStationResult.Busy, "EPSON 机器人正在运动。");
            }
            if (currentState == MotionStationState.Faulted)
            {
                return MotionStationResult.BaseFunctionError;
            }
            return PrepareConnectedRobot();
        }

        private MotionStationResult PrepareConnectedRobot()
        {
            if (configuration.RemoteMode && !remoteLoggedIn)
            {
                MotionStationResult login = LoginRemoteController();
                if (login != MotionStationResult.Success)
                {
                    return login;
                }
                remoteLoggedIn = true;
            }
            if (configuration.PointFromRobot && !pointsLoadedFromRobot)
            {
                MotionStationResult load = LoadConfiguredPointsFromRobot();
                if (load != MotionStationResult.Success)
                {
                    return load;
                }
                pointsLoadedFromRobot = true;
            }
            return MotionStationResult.Success;
        }

        private MotionStationResult LoginRemoteController()
        {
            MotionStationResult result = ExchangeExpected(
                configuration.RemoteCommunicationName, "$Login\r\n", "#Login,0");
            if (result != MotionStationResult.Success)
            {
                return result;
            }

            CommReceiveResult reset = Exchange(
                configuration.RemoteCommunicationName, "$Reset\r\n", CommandTimeoutMs);
            if (!reset.Success)
            {
                return Fail(
                    ClassifyReceiveFailure(reset.ErrorMessage),
                    $"EPSON 远程复位失败：{reset.ErrorMessage}");
            }
            string resetText = reset.MessageText ?? string.Empty;
            if (resetText.IndexOf("#Reset,0", StringComparison.OrdinalIgnoreCase) < 0
                && resetText.IndexOf("#Rest,0", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return Fail(
                    MotionStationResult.CommandRejected,
                    $"EPSON 远程复位被拒绝：{resetText}",
                    MotionStationState.Faulted);
            }

            return ExchangeExpected(
                configuration.RemoteCommunicationName, "$Start,0\r\n", "#Start,0");
        }

        private MotionStationResult ExchangeExpected(string channel, string command, string expected)
        {
            CommReceiveResult response = Exchange(channel, command, CommandTimeoutMs);
            if (!response.Success)
            {
                return Fail(
                    ClassifyReceiveFailure(response.ErrorMessage),
                    $"EPSON 远程命令失败：{response.ErrorMessage}");
            }
            if ((response.MessageText ?? string.Empty)
                .IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return Fail(
                    MotionStationResult.CommandRejected,
                    $"EPSON 远程命令返回异常，期望 {expected}，实际 {response.MessageText}",
                    MotionStationState.Faulted);
            }
            return MotionStationResult.Success;
        }

        private MotionStationResult LoadConfiguredPointsFromRobot()
        {
            var updates = new List<PointCoordinatesUpdate>();
            foreach (DataPos configuredPoint in configuration.ListDataPos
                .Where(point => point != null && !string.IsNullOrWhiteSpace(point.Name)))
            {
                if (!IsValidPointIndex(configuredPoint.Index))
                {
                    return Fail(
                        MotionStationResult.InvalidConfiguration,
                        $"EPSON 点位“{configuredPoint.Name}”索引超出 0-{MaximumRobotPointIndex}：{configuredPoint.Index}",
                        MotionStationState.Faulted);
                }
                MotionStationResult read = ReadRobotPosition(configuredPoint.Index, out double[] coordinates);
                if (read != MotionStationResult.Success)
                {
                    return read;
                }
                updates.Add(new PointCoordinatesUpdate(configuredPoint, coordinates));
            }

            MotionStationResult current = ReadRobotPosition(-1, out double[] currentCoordinates);
            if (current != MotionStationResult.Success)
            {
                return current;
            }

            foreach (PointCoordinatesUpdate update in updates)
            {
                update.Apply();
            }
            if (stationStore != null && updates.Count > 0
                && !stationStore.TryPersistCurrent(paths.ConfigPath, out string persistError))
            {
                foreach (PointCoordinatesUpdate update in updates)
                {
                    update.Rollback();
                }
                return Fail(
                    MotionStationResult.BaseFunctionError,
                    $"EPSON 机器人点位已读取但持久化失败，内存修改已回滚：{persistError}",
                    MotionStationState.Faulted);
            }

            lock (statusGate)
            {
                status.SetPosition(currentCoordinates);
            }
            return MotionStationResult.Success;
        }

        private MotionStationResult ReadRobotPosition(int pointIndex, out double[] coordinates)
        {
            coordinates = null;
            if (!TryBuild(
                EpsonCommandCatalog.GetPosition,
                out string command,
                AxisCount,
                pointIndex))
            {
                return MotionStationResult.InvalidConfiguration;
            }
            CommReceiveResult response = Exchange(
                configuration.CommunicationName, command, CommandTimeoutMs);
            if (!response.Success)
            {
                return Fail(
                    ClassifyReceiveFailure(response.ErrorMessage),
                    $"EPSON 获取点位 {pointIndex} 失败：{response.ErrorMessage}");
            }
            if (!TryParsePositionResponse(response.MessageText, out coordinates, out string parseError))
            {
                return Fail(MotionStationResult.ReceiveFailed, parseError);
            }
            return MotionStationResult.Success;
        }

        private MotionStationResult SendMoveCommand(string command)
        {
            if (communication != null)
            {
                communication.ClearTcpMessages(configuration.CommunicationName);
            }
            if (!Send(configuration.CommunicationName, command))
            {
                return Fail(
                    MotionStationResult.SendFailed,
                    $"EPSON 运动命令发送失败：{command.Trim()}",
                    MotionStationState.Disconnected);
            }
            lock (statusGate)
            {
                status.State = MotionStationState.Moving;
                status.LastError = string.Empty;
            }
            return MotionStationResult.Success;
        }

        private MotionStationResult SendWithWaitOk(string channel, string command, int timeoutMs)
        {
            CommReceiveResult response = Exchange(channel, command, timeoutMs);
            if (!response.Success)
            {
                return Fail(
                    ClassifyReceiveFailure(response.ErrorMessage),
                    $"EPSON 命令执行失败：{response.ErrorMessage}");
            }
            if (!IsOkResponse(response.MessageText))
            {
                return Fail(
                    MotionStationResult.CommandRejected,
                    $"EPSON 命令返回错误：{response.MessageText}",
                    MotionStationState.Faulted);
            }
            ClearError();
            return MotionStationResult.Success;
        }

        private bool TryBuild(string section, out string command, params object[] arguments)
        {
            if (commands.TryBuild(section, out command, out string error, arguments))
            {
                return true;
            }
            Fail(MotionStationResult.InvalidConfiguration, error, MotionStationState.Faulted);
            return false;
        }

        private bool Send(string channel, string command)
        {
            if (testSend != null)
            {
                return testSend(channel, command);
            }
            return communication.SendTcpAsync(channel, command, false).GetAwaiter().GetResult();
        }

        private CommReceiveResult Receive(string channel, int timeoutMs, CancellationToken cancellationToken)
        {
            if (testReceive != null)
            {
                return testReceive(channel, timeoutMs, cancellationToken);
            }
            return communication.ReceiveTcpAsync(channel, timeoutMs, cancellationToken)
                .GetAwaiter().GetResult();
        }

        private CommReceiveResult Exchange(string channel, string command, int timeoutMs)
        {
            if (testExchange != null)
            {
                return testExchange(channel, command, timeoutMs);
            }
            return communication.SendReceiveTcpAsync(channel, command, false, timeoutMs)
                .GetAwaiter().GetResult();
        }

        private bool AreRequiredChannelsActive()
        {
            return IsChannelActive(configuration.CommunicationName)
                && (!configuration.RemoteMode
                    || IsChannelActive(configuration.RemoteCommunicationName));
        }

        private bool IsChannelActive(string channel)
        {
            if (testIsChannelActive != null)
            {
                return testIsChannelActive(channel);
            }
            return testSend != null || communication.IsTcpActive(channel);
        }

        private void MarkDisconnected()
        {
            // TCP 自动重连只恢复传输通道。EPSON 远程控制会话和机器人点表准备状态
            // 属于本次连接，掉线后必须失效，确保下一次命令沿用 3.0 路径重新登录并加载点位。
            remoteLoggedIn = !configuration.RemoteMode;
            pointsLoadedFromRobot = !configuration.PointFromRobot;
            status.State = MotionStationState.Disconnected;
        }

        private void CancelPendingMoveWait()
        {
            lock (statusGate)
            {
                pendingMoveWait?.Cancel();
            }
        }

        private MotionStationResult Fail(
            MotionStationResult result,
            string message,
            MotionStationState? state = null)
        {
            lock (statusGate)
            {
                status.LastError = message ?? string.Empty;
                if (state.HasValue)
                {
                    if (state.Value == MotionStationState.Disconnected)
                    {
                        MarkDisconnected();
                    }
                    else
                    {
                        status.State = state.Value;
                    }
                    status.HasAlarm = state.Value == MotionStationState.Faulted;
                }
            }
            Log(message, LogLevel.Error);
            return result;
        }

        private void ClearError()
        {
            lock (statusGate)
            {
                status.LastError = string.Empty;
                status.HasAlarm = false;
            }
        }

        private void SetState(MotionStationState stateValue)
        {
            lock (statusGate)
            {
                status.State = stateValue;
                if (stateValue != MotionStationState.Faulted)
                {
                    status.HasAlarm = false;
                }
            }
        }

        private void Log(string message, LogLevel level)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }
            try
            {
                logger.Log(message, level);
            }
            catch
            {
                // 日志端口异常不能改变机器人命令的确定性返回结果。
            }
        }

        private static MotionStationResult ClassifyReceiveFailure(string error)
        {
            return IsTimeout(error) ? MotionStationResult.Timeout : MotionStationResult.ReceiveFailed;
        }

        private static bool IsTimeout(string error)
        {
            return (error ?? string.Empty).IndexOf("超时", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsOkResponse(string response)
        {
            return (response ?? string.Empty).IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryParsePositionResponse(
            string response,
            out double[] coordinates,
            out string error)
        {
            coordinates = null;
            error = null;
            string[] parts = (response ?? string.Empty).Split(
                new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < AxisCount + 1 || !IsOkResponse(parts[0]))
            {
                error = $"EPSON 位置返回格式无效：{response}";
                return false;
            }

            coordinates = new double[AxisCount];
            for (int i = 0; i < AxisCount; i++)
            {
                if (!double.TryParse(
                    parts[i + 1].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out coordinates[i]))
                {
                    error = $"EPSON 位置返回第 {i + 1} 维无法解析：{response}";
                    coordinates = null;
                    return false;
                }
            }
            return true;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsValidMotionPoint(DataPos point)
        {
            return point != null && point.IsMotionReady && IsValidPointIndex(point.Index);
        }

        private static bool IsValidPointIndex(int pointIndex)
        {
            return pointIndex >= 0 && pointIndex <= MaximumRobotPointIndex;
        }

        private static DataPos CreatePosition(int index, IReadOnlyList<double> coordinates)
        {
            var point = new DataPos(index) { IsTaught = true };
            ApplyCoordinates(point, coordinates);
            return point;
        }

        private static void ApplyCoordinates(DataPos point, IReadOnlyList<double> coordinates)
        {
            point.X = coordinates[0];
            point.Y = coordinates[1];
            point.Z = coordinates[2];
            point.U = coordinates[3];
            point.V = coordinates[4];
            point.W = coordinates[5];
        }

        private sealed class PointCoordinatesUpdate
        {
            private readonly DataPos point;
            private readonly double[] coordinates;
            private readonly double[] previousCoordinates;
            private readonly bool? previousTeachingState;

            public PointCoordinatesUpdate(DataPos point, double[] coordinates)
            {
                this.point = point;
                this.coordinates = coordinates;
                previousCoordinates = point.GetAllValues().ToArray();
                previousTeachingState = point.IsTaught;
            }

            public void Apply()
            {
                ApplyCoordinates(point, coordinates);
                point.IsTaught = true;
            }

            public void Rollback()
            {
                ApplyCoordinates(point, previousCoordinates);
                point.IsTaught = previousTeachingState;
            }
        }
    }
}
