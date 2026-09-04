using System;
// 模块：运动控制 / 核心。
// 职责范围：定义统一运动运行契约、运动协调和轴状态缓存。
// 安全边界：命令先经过 Readiness、Safety 和 ValidateAxesForCommand；驱动事件只在 InitCardType 中绑定。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static Automation.MotionControl.MotionCtrl;

namespace Automation.MotionControl
{
    public class MotionCtrl : IMotionRuntime, IIoRuntime
    {
        private readonly ValueConfigStore valueStore;
        private readonly CardConfigStore cardStore;
        private readonly StationDefinitionStore stationStore;
        private readonly CommunicationHub communication;
        private readonly CommunicationConfigStore communicationStore;
        private readonly PlatformPaths paths;
        private readonly ILogger logger;
        private readonly string configPath;
        private readonly PlatformSafetyCoordinator safety;
        private readonly PlatformReadinessState readiness;
        private readonly object driverLock = new object();
        private readonly object stationLock = new object();
        private readonly Dictionary<short, IMotionStation> stations =
            new Dictionary<short, IMotionStation>();
        public LS ls;

        public MotionCtrl(
            ValueConfigStore valueStore,
            CardConfigStore cardStore,
            StationDefinitionStore stationStore,
            CommunicationHub communication,
            CommunicationConfigStore communicationStore,
            PlatformPaths paths,
            PlatformSafetyCoordinator safety,
            PlatformReadinessState readiness,
            ILogger logger)
        {
            this.valueStore = valueStore ?? throw new ArgumentNullException(nameof(valueStore));
            this.cardStore = cardStore ?? throw new ArgumentNullException(nameof(cardStore));
            this.stationStore = stationStore ?? throw new ArgumentNullException(nameof(stationStore));
            this.communication = communication ?? throw new ArgumentNullException(nameof(communication));
            this.communicationStore = communicationStore ?? throw new ArgumentNullException(nameof(communicationStore));
            this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(paths.ConfigPath) || !Path.IsPathRooted(paths.ConfigPath))
            {
                throw new ArgumentException("运动控制配置目录必须是绝对路径。", nameof(paths));
            }
            configPath = Path.GetFullPath(paths.ConfigPath);
            this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
            this.readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        }

        public int StationCount => stationStore.Items.Count;

        public delegate ushort InitCardHandler();
        public delegate bool SetIOHandler(IO io, bool isOpen);
        public delegate bool SetOutputsHandler(IReadOnlyList<IoOutputCommand> commands);
        public delegate bool GetOutIOHandler(IO io, ref bool value);
        public delegate bool GetInIOHandler(IO io, ref bool value);
        public delegate void SettHomeParamHandler(ushort card,ushort axis, ushort dir, ushort speed, ushort homeMode);
        public delegate void StartHomeHandler(ushort card, ushort axis);
        public delegate void CleanPosHandler(ushort card, ushort axis);
        public delegate double GetAxisPosHandler(ushort card, ushort axis);
        public delegate void SetMovParamHandler(ushort card,ushort axis, double minVel, double dMaxVel, double acc, double dec, double dStopVel, double dS_para,int equiv);
        public delegate void MovHandler(ushort card, ushort axis, double dDist, ushort sPosi_mode, bool wait);
        public delegate void MoveCoordinatedLinearHandler(CoordinatedLinearMoveRequest request);
        public delegate bool IsCoordinatedLinearDoneHandler(ushort card, ushort coordinateSystem);
        public delegate void StopCoordinatedLinearHandler(ushort card, ushort coordinateSystem, ushort stopMode);
        public delegate void JogHandler(ushort card, ushort axis, ushort sDir);
        public delegate void StopOneAxisHandler(ushort card, ushort axis, ushort stop_mode);
        public delegate void StopConnectHandler();
        public delegate bool HomeStatusHandler(ushort card, ushort axis);
        public delegate bool GetInPosHandler(ushort card, ushort axis);
        public delegate bool GetAxisSevonHandler(ushort card, ushort axis);
        public delegate void SetAxisSevonHandler(ushort card, ushort axis, bool isSevon);
        public delegate void DownLoadConfigHandler();
        public delegate void SetAllAxisSevonOnHandler();
        public delegate void SetAllAxisEquivHandler();
        public delegate void ResetAxisAlarmHandler(ushort card, ushort axis);
        public delegate double GetAxisCurSpeedHandler(ushort card, ushort axis);
        public delegate ushort GetAxisAlarmCodeHandler(ushort card, ushort axis);
        public delegate uint GetAxisIoStatusHandler(ushort card, ushort axis);

        public event InitCardHandler initCard;
        public event SetIOHandler setIO;
        public event SetOutputsHandler setOutputs;
        public event GetOutIOHandler getOutIO;
        public event GetInIOHandler getInIO;
        public event SettHomeParamHandler settHomeParam;
        public event StartHomeHandler startHome;
        public event CleanPosHandler cleanPos;
        public event GetAxisPosHandler getAxisPos;
        public event SetMovParamHandler setMovParam;
        public event MovHandler mov;
        public event MoveCoordinatedLinearHandler moveCoordinatedLinear;
        public event IsCoordinatedLinearDoneHandler isCoordinatedLinearDone;
        public event StopCoordinatedLinearHandler stopCoordinatedLinear;
        public event JogHandler jog;
        public event StopOneAxisHandler stopOneAxis;
        public event StopConnectHandler stopConnect;
        public event HomeStatusHandler homeStatus;
        public event GetInPosHandler getInPos;
        public event GetAxisSevonHandler getAxisSevon;
        public event SetAxisSevonHandler setAxisSevon;
        public event DownLoadConfigHandler downLoadConfig;
        public event SetAllAxisSevonOnHandler setAllAxisSevonOn;
        public event SetAllAxisEquivHandler setAllAxisEquiv;
        public event ResetAxisAlarmHandler resetAxisAlarm;
        public event GetAxisCurSpeedHandler getAxisCurSpeed;
        public event GetAxisAlarmCodeHandler getAxisAlarmCode;
        public event GetAxisIoStatusHandler getAxisIoStatus;
        public bool IsCardInitialized { get; private set; }

        [ThreadStatic]
        private static HashSet<long> validatedCommands;

        private sealed class CommandValidationLease : IDisposable
        {
            private HashSet<long> commands;

            public CommandValidationLease(HashSet<long> commands)
            {
                this.commands = commands;
            }

            public void Dispose()
            {
                HashSet<long> current = commands;
                commands = null;
                if (current != null && ReferenceEquals(validatedCommands, current))
                {
                    validatedCommands = null;
                }
            }
        }

        private void EnsureCardInitialized()
        {
            if (!IsCardInitialized || ls == null)
            {
                throw new InvalidOperationException("运动控制卡未初始化");
            }
        }

        private void EnsureMotionConfigurationReady()
        {
            if (!readiness.MotionConfigFaulted)
            {
                return;
            }
            string reason = string.IsNullOrWhiteSpace(readiness.MotionConfigFaultReason)
                ? "未提供详细原因。"
                : readiness.MotionConfigFaultReason;
            throw new InvalidOperationException("运动配置故障，禁止执行任何轴或机器人工站动作：" + reason);
        }

        private void EnsureResetCompleted()
        {
            EnsureMotionConfigurationReady();
            if (safety.IsLocked
                || !valueStore.TryGetValueByName("复位状态", out DicValue resetValue)
                || resetValue == null
                || !string.Equals(resetValue.Type, "double", StringComparison.OrdinalIgnoreCase)
                || !double.TryParse(resetValue.Value, out double resetRaw)
                || resetRaw != (double)ResetStatus.ResetCompleted)
            {
                throw new InvalidOperationException("系统尚未复位完成，禁止轴运动。");
            }
        }

        public MotionStationResult InitializeStations()
        {
            EnsureMotionConfigurationReady();
            ReleaseStations();
            MotionStationResult firstFailure = MotionStationResult.Success;
            for (short index = 0; index < stationStore.Items.Count; index++)
            {
                DataStation configuration = stationStore.Items[index];
                if (configuration == null)
                {
                    firstFailure = firstFailure == MotionStationResult.Success
                        ? MotionStationResult.InvalidConfiguration
                        : firstFailure;
                    logger.Log($"{index}号六轴工站配置为空。", LogLevel.Error);
                    continue;
                }

                IMotionStation station;
                try
                {
                    station = CreateMotionStation(configuration);
                }
                catch (Exception ex)
                {
                    firstFailure = firstFailure == MotionStationResult.Success
                        ? MotionStationResult.InvalidConfiguration
                        : firstFailure;
                    logger.Log($"六轴工站{configuration.Name}创建失败:{ex.Message}", LogLevel.Error);
                    continue;
                }
                lock (stationLock)
                {
                    stations[index] = station;
                }
                MotionStationResult result;
                try
                {
                    result = station.Initialize();
                }
                catch (Exception ex)
                {
                    result = MotionStationResult.BaseFunctionError;
                    logger.Log($"六轴工站{configuration.Name}初始化异常:{ex.Message}", LogLevel.Error);
                }
                if (result != MotionStationResult.Success)
                {
                    firstFailure = firstFailure == MotionStationResult.Success ? result : firstFailure;
                    logger.Log($"六轴工站{configuration.Name}初始化失败:{result}", LogLevel.Error);
                }
            }
            return firstFailure;
        }

        public MotionStationResult ReleaseStations()
        {
            KeyValuePair<short, IMotionStation>[] snapshot;
            lock (stationLock)
            {
                snapshot = stations.OrderByDescending(item => item.Key).ToArray();
                stations.Clear();
            }
            MotionStationResult firstFailure = MotionStationResult.Success;
            foreach (KeyValuePair<short, IMotionStation> item in snapshot)
            {
                try
                {
                    MotionStationResult result = item.Value.Release();
                    if (result != MotionStationResult.Success
                        && firstFailure == MotionStationResult.Success)
                    {
                        firstFailure = result;
                    }
                }
                catch (Exception ex)
                {
                    if (firstFailure == MotionStationResult.Success)
                    {
                        firstFailure = MotionStationResult.BaseFunctionError;
                    }
                    logger.Log($"释放{item.Key}号六轴工站异常:{ex.Message}", LogLevel.Error);
                }
            }
            return firstFailure;
        }

        public MotionStationStatus GetStationStatus(short station)
        {
            if (readiness.MotionConfigFaulted)
            {
                return new MotionStationStatus
                {
                    State = MotionStationState.Faulted,
                    LastError = string.IsNullOrWhiteSpace(readiness.MotionConfigFaultReason)
                        ? "运动配置故障，禁止执行工站动作。"
                        : "运动配置故障，禁止执行工站动作：" + readiness.MotionConfigFaultReason
                };
            }
            if (!TryGetStation(station, out IMotionStation runtimeStation))
            {
                return new MotionStationStatus
                {
                    State = MotionStationState.Uninitialized,
                    LastError = $"六轴工站索引无效或尚未初始化:{station}"
                };
            }
            return runtimeStation.GetStatus();
        }

        public MotionStationResult SetStationSpeed(short station, double velocity,
            double acceleration, double deceleration, short axis = -1,
            StationSpeedType type = StationSpeedType.Joint)
        {
            if (TryRejectMotionConfigFault(station, out MotionStationResult fault))
            {
                return fault;
            }
            if (!TryGetStation(station, out IMotionStation runtimeStation))
            {
                return MotionStationResult.NotInitialized;
            }
            return runtimeStation.SetSpeed(velocity, acceleration, deceleration, axis, type);
        }

        public MotionStationResult HomeStation(short station, short axis = -1,
            bool wait = true, bool group = false)
        {
            if (!TryValidateStationCommand(station, out IMotionStation runtimeStation,
                out MotionStationResult error))
            {
                return error;
            }
            return runtimeStation.Home(axis, wait, group);
        }

        public MotionStationResult MoveStationToPoint(short station, DataPos point,
            StationMoveMode mode = StationMoveMode.Go, bool[] disabledAxes = null, short tool = 0)
        {
            if (TryRejectMotionConfigFault(station, out MotionStationResult fault))
            {
                return fault;
            }
            if (point == null || !point.IsMotionReady)
            {
                return MotionStationResult.InvalidParameter;
            }
            if (!TryValidateStationCommand(station, out IMotionStation runtimeStation,
                out MotionStationResult error))
            {
                return error;
            }
            return runtimeStation.MoveToPoint(point, mode, disabledAxes, tool);
        }

        public MotionStationResult MoveStationOffset(short station, int basePointIndex,
            IReadOnlyList<double> offsets, StationMoveMode mode = StationMoveMode.Go)
        {
            if (TryRejectMotionConfigFault(station, out MotionStationResult fault))
            {
                return fault;
            }
            if (offsets == null || offsets.Count < 6)
            {
                return MotionStationResult.InvalidParameter;
            }
            if (!TryValidateStationCommand(station, out IMotionStation runtimeStation,
                out MotionStationResult error))
            {
                return error;
            }
            return runtimeStation.MoveOffset(basePointIndex, offsets, mode);
        }

        public MotionStationResult MoveStationAxis(short station, short axis, double offset,
            StationAxisMoveMode mode = StationAxisMoveMode.Relative, short tool = 0)
        {
            if (!TryValidateStationCommand(station, out IMotionStation runtimeStation,
                out MotionStationResult error))
            {
                return error;
            }
            return runtimeStation.AxisMotion(axis, offset, mode, tool);
        }

        public MotionStationResult WaitStationMotion(short station, bool isHome = false,
            int axis = -1, int timeoutMs = 120000)
        {
            if (!TryGetStation(station, out IMotionStation runtimeStation))
            {
                return MotionStationResult.NotInitialized;
            }
            return runtimeStation.WaitMoveFinish(isHome, axis, timeoutMs);
        }

        public MotionStationResult GetStationPosition(short station, short tool, out DataPos position)
        {
            position = null;
            if (TryRejectMotionConfigFault(station, out MotionStationResult fault))
            {
                return fault;
            }
            if (!TryGetStation(station, out IMotionStation runtimeStation))
            {
                return MotionStationResult.NotInitialized;
            }
            return runtimeStation.GetCurrentPosition(tool, out position);
        }

        public MotionStationResult SaveStationPoint(short station, DataPos point)
        {
            if (TryRejectMotionConfigFault(station, out MotionStationResult fault))
            {
                return fault;
            }
            if (point == null || point.Index < 0 || !point.IsMotionReady
                || point.GetAllValues().Any(value => double.IsNaN(value) || double.IsInfinity(value)))
            {
                return MotionStationResult.InvalidParameter;
            }
            if (station < 0 || station >= stationStore.Items.Count)
            {
                return MotionStationResult.NotInitialized;
            }

            DataStation configuration = stationStore.Items[station];
            if (configuration?.ListDataPos == null)
            {
                return MotionStationResult.InvalidConfiguration;
            }
            if (point.Index >= DataStation.GetPointCapacity(configuration.Type))
            {
                return MotionStationResult.InvalidParameter;
            }
            if (!TryGetStation(station, out IMotionStation runtimeStation))
            {
                return MotionStationResult.NotInitialized;
            }

            DataPos previous;
            DataPos candidate = (DataPos)point.Clone();
            string stationName;
            lock (configuration)
            {
                DataPos configuredPoint = configuration.ListDataPos.FirstOrDefault(
                    item => item != null && item.Index == point.Index);
                if (configuredPoint == null
                    || !string.Equals(configuredPoint.Name, point.Name, StringComparison.Ordinal))
                {
                    return MotionStationResult.InvalidParameter;
                }

                previous = (DataPos)configuredPoint.Clone();
                stationName = configuration.Name;
            }

            // 设备调用可能进入厂商 SDK 的工站锁，不能持有配置锁跨层调用。
            MotionStationResult deviceResult = runtimeStation.SavePoint(candidate);
            if (deviceResult != MotionStationResult.Success)
            {
                if (MayHaveChangedController(deviceResult))
                {
                    MotionStationResult compensation = runtimeStation.SavePoint(previous);
                    if (compensation != MotionStationResult.Success)
                    {
                        logger.Log(
                            $"六轴工站{stationName}点位{point.Name}写入失败，且控制器补偿回滚失败：写入={deviceResult}，回滚={compensation}。",
                            LogLevel.Error);
                        return MotionStationResult.InconsistentState;
                    }
                }
                return deviceResult;
            }

            string commitError = null;
            DataPos controllerRollbackPoint = previous;
            lock (configuration)
            {
                DataPos configuredPoint = configuration.ListDataPos.FirstOrDefault(
                    item => item != null && item.Index == point.Index);
                if (configuredPoint == null
                    || !string.Equals(configuredPoint.Name, point.Name, StringComparison.Ordinal)
                    || !PointsEqual(configuredPoint, previous))
                {
                    if (configuredPoint?.IsMotionReady == true)
                    {
                        controllerRollbackPoint = (DataPos)configuredPoint.Clone();
                    }
                    commitError = "保存期间点位配置已被并发修改，未覆盖较新的配置。";
                }
                else
                {
                    ApplyPoint(configuredPoint, candidate);
                    if (configuration.dicDataPos != null)
                    {
                        configuration.dicDataPos[configuredPoint.Name] = configuredPoint;
                    }
                    if (stationStore.TryPersistCurrent(configPath, out string persistError))
                    {
                        return MotionStationResult.Success;
                    }

                    ApplyPoint(configuredPoint, previous);
                    if (configuration.dicDataPos != null)
                    {
                        configuration.dicDataPos[configuredPoint.Name] = configuredPoint;
                    }
                    commitError = persistError;
                }
            }

            // 配置提交失败后的控制器补偿同样在配置锁之外完成。
            MotionStationResult controllerRollback = runtimeStation.SavePoint(controllerRollbackPoint);
            if (controllerRollback != MotionStationResult.Success)
            {
                logger.Log(
                    $"六轴工站{stationName}点位{point.Name}配置提交失败，且控制器补偿回滚失败：{commitError}；回滚={controllerRollback}。",
                    LogLevel.Error);
                return MotionStationResult.InconsistentState;
            }

            logger.Log(
                $"六轴工站{stationName}点位{point.Name}配置提交失败，控制器与内存已回滚：{commitError}",
                LogLevel.Error);
            return MotionStationResult.BaseFunctionError;
        }

        public MotionStationResult CreateStationTray(
            short station,
            int trayId,
            int rowCount,
            int columnCount,
            IReadOnlyList<DataPos> referencePoints)
        {
            if (TryRejectMotionConfigFault(station, out MotionStationResult fault))
            {
                return fault;
            }
            if (!TryGetStation(station, out IMotionStation runtimeStation))
            {
                return MotionStationResult.NotInitialized;
            }
            return runtimeStation.CreateTray(
                trayId, rowCount, columnCount, referencePoints);
        }

        public MotionStationResult MoveStationTrayPoint(
            short station,
            int trayId,
            int position,
            DataPos calculatedPoint)
        {
            if (!TryValidateStationCommand(station, out IMotionStation runtimeStation,
                out MotionStationResult error))
            {
                return error;
            }
            return runtimeStation.MoveTrayPoint(
                trayId, position, calculatedPoint);
        }

        private static bool MayHaveChangedController(MotionStationResult result)
        {
            return result == MotionStationResult.SendFailed
                || result == MotionStationResult.ReceiveFailed
                || result == MotionStationResult.Timeout
                || result == MotionStationResult.CommandRejected
                || result == MotionStationResult.BaseFunctionError
                || result == MotionStationResult.InconsistentState;
        }

        private static void ApplyPoint(DataPos target, DataPos source)
        {
            target.Index = source.Index;
            target.Name = source.Name;
            target.IsTaught = source.IsTaught;
            target.GroupName = source.GroupName;
            target.GroupVisible = source.GroupVisible;
            target.Enabled = source.Enabled;
            target.X = source.X;
            target.Y = source.Y;
            target.Z = source.Z;
            target.U = source.U;
            target.V = source.V;
            target.W = source.W;
            target.Pose = source.Pose == null ? null : (short[])source.Pose.Clone();
            target.Velocity = source.Velocity == null ? null : (double[])source.Velocity.Clone();
            target.PositionLimits = source.PositionLimits == null
                ? null
                : source.PositionLimits.Select(limit => limit == null ? null : (double[])limit.Clone()).ToArray();
            target.Description = source.Description;
        }

        private static bool PointsEqual(DataPos left, DataPos right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null
                || left.Index != right.Index
                || !string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                || left.IsTaught != right.IsTaught
                || !string.Equals(left.GroupName, right.GroupName, StringComparison.Ordinal)
                || left.GroupVisible != right.GroupVisible
                || left.Enabled != right.Enabled
                || !string.Equals(left.Description, right.Description, StringComparison.Ordinal)
                || !left.GetAllValues().SequenceEqual(right.GetAllValues())
                || !ArraysEqual(left.Pose, right.Pose)
                || !ArraysEqual(left.Velocity, right.Velocity))
            {
                return false;
            }
            if (ReferenceEquals(left.PositionLimits, right.PositionLimits))
            {
                return true;
            }
            if (left.PositionLimits == null || right.PositionLimits == null
                || left.PositionLimits.Length != right.PositionLimits.Length)
            {
                return false;
            }
            for (int index = 0; index < left.PositionLimits.Length; index++)
            {
                if (!ArraysEqual(left.PositionLimits[index], right.PositionLimits[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ArraysEqual<T>(T[] left, T[] right)
        {
            return ReferenceEquals(left, right)
                || (left != null && right != null && left.SequenceEqual(right));
        }

        public MotionStationResult StopStation(short station, bool emergency = false)
        {
            if (!TryGetStation(station, out IMotionStation runtimeStation))
            {
                return MotionStationResult.NotInitialized;
            }
            return runtimeStation.Stop(emergency);
        }

        public MotionStationResult StopAllStations(bool emergency = false)
        {
            IMotionStation[] snapshot;
            lock (stationLock)
            {
                snapshot = stations.OrderBy(item => item.Key).Select(item => item.Value).ToArray();
            }
            MotionStationResult firstFailure = MotionStationResult.Success;
            foreach (IMotionStation station in snapshot)
            {
                MotionStationResult result;
                try
                {
                    result = station.Stop(emergency);
                }
                catch (Exception ex)
                {
                    result = MotionStationResult.BaseFunctionError;
                    logger.Log($"停止六轴工站异常:{ex.Message}", LogLevel.Error);
                }
                if (result != MotionStationResult.Success
                    && firstFailure == MotionStationResult.Success)
                {
                    firstFailure = result;
                }
            }
            return firstFailure;
        }

        private bool TryValidateStationCommand(short station, out IMotionStation runtimeStation,
            out MotionStationResult error)
        {
            runtimeStation = null;
            error = MotionStationResult.Success;
            try
            {
                EnsureMotionConfigurationReady();
                if (readiness.MotionConfigRestartRequired)
                {
                    throw new InvalidOperationException("运动设备配置已变更，必须重启程序后才能执行工站运动。");
                }
                EnsureResetCompleted();
            }
            catch (Exception ex)
            {
                logger.Log($"六轴工站{station}运动被安全门禁拒绝:{ex.Message}", LogLevel.Error);
                error = MotionStationResult.CommandRejected;
                return false;
            }
            if (!TryGetStation(station, out runtimeStation))
            {
                error = MotionStationResult.NotInitialized;
                return false;
            }
            return true;
        }

        private bool TryRejectMotionConfigFault(short station, out MotionStationResult error)
        {
            error = MotionStationResult.Success;
            if (!readiness.MotionConfigFaulted)
            {
                return false;
            }
            string reason = string.IsNullOrWhiteSpace(readiness.MotionConfigFaultReason)
                ? "未提供详细原因。"
                : readiness.MotionConfigFaultReason;
            logger.Log(
                $"六轴工站{station}命令被运动配置门禁拒绝:{reason}",
                LogLevel.Error);
            error = MotionStationResult.CommandRejected;
            return true;
        }

        private bool TryGetStation(short station, out IMotionStation runtimeStation)
        {
            lock (stationLock)
            {
                return stations.TryGetValue(station, out runtimeStation);
            }
        }

        private IMotionStation CreateMotionStation(DataStation configuration)
        {
            switch (configuration.Type)
            {
                case StationType.Axis:
                    return new AxisMotionStation(this, cardStore, configuration);
                case StationType.Epson:
                    return new EpsonStation(
                        configuration,
                        stationStore,
                        communication,
                        communicationStore,
                        paths,
                        logger);
                case StationType.Inovance:
                    return new InovanceStation(
                        configuration,
                        stationStore,
                        communicationStore,
                        paths);
                case StationType.InovanceV4:
                    return new InovanceV4Station(
                        configuration,
                        stationStore,
                        communicationStore,
                        paths);
                default:
                    throw new InvalidOperationException($"未知六轴工站类型:{configuration.Type}");
            }
        }

        public IDisposable ValidateAxesForCommand(IReadOnlyCollection<AxisCommandRequest> requests)
        {
            EnsureMotionConfigurationReady();
            if (readiness.MotionConfigRestartRequired)
            {
                throw new InvalidOperationException("运动设备配置已变更，必须重启程序后才能执行轴运动。");
            }
            EnsureCardInitialized();
            if (requests == null || requests.Count == 0)
            {
                throw new ArgumentException("轴状态校验列表为空。", nameof(requests));
            }
            HashSet<long> commands = new HashSet<long>();
            foreach (AxisCommandRequest request in requests)
            {
                if (request == null)
                {
                    throw new InvalidOperationException("轴状态校验项为空。");
                }
                long key = BuildCommandKey(request.Card, request.Axis, request.Kind);
                if (!commands.Add(key))
                {
                    continue;
                }
                uint ioStatus = GetAxisIoStatus(request.Card, request.Axis);
                if ((ioStatus & 1u) != 0)
                {
                    ushort alarmCode = GetAxisAlarmCode(request.Card, request.Axis);
                    string codeText = alarmCode == ushort.MaxValue ? "驱动器未提供详细码(ALM=ON)" : alarmCode.ToString();
                    throw new InvalidOperationException($"轴存在伺服报警:{request.Card}-{request.Axis},错误码:{codeText}");
                }
                if ((ioStatus & (1u << 3)) != 0)
                {
                    throw new InvalidOperationException($"轴急停信号有效:{request.Card}-{request.Axis}");
                }
                if (!GetInPos(request.Card, request.Axis))
                {
                    throw new InvalidOperationException($"轴正在运动:{request.Card}-{request.Axis}");
                }
                if (!GetAxisSevon(request.Card, request.Axis))
                {
                    throw new InvalidOperationException($"轴未使能:{request.Card}-{request.Axis}");
                }
                if (request.Kind == AxisCommandKind.Motion && !HomeStatus(request.Card, request.Axis))
                {
                    throw new InvalidOperationException($"轴尚未回原完成:{request.Card}-{request.Axis}");
                }
            }
            // 校验结果只在当前线程、当前 using 作用域有效，避免“先校验、状态变化后很久再执行”的窗口。
            validatedCommands = commands;
            return new CommandValidationLease(commands);
        }

        private static long BuildCommandKey(ushort card, ushort axis, AxisCommandKind kind)
        {
            return ((long)kind << 48) | ((long)card << 32) | axis;
        }

        private void EnsureCommandValidated(ushort card, ushort axis, AxisCommandKind kind, bool allowHomeJog)
        {
            long key = BuildCommandKey(card, axis, kind);
            if (validatedCommands != null && validatedCommands.Remove(key))
            {
                return;
            }
            if (allowHomeJog && validatedCommands != null
                && validatedCommands.Remove(BuildCommandKey(card, axis, AxisCommandKind.Home)))
            {
                return;
            }
            using (ValidateAxesForCommand(new[] { new AxisCommandRequest(card, axis, kind) }))
            {
            }
        }

        public bool InitCard()
        {
            EnsureMotionConfigurationReady();
            initCard?.Invoke();
            IsCardInitialized = ls != null && ls.IsCardInitialized;
            return IsCardInitialized;
        }
        public bool SetIO(IO io, bool isOpen)
        {
            return (bool)setIO?.Invoke(io, isOpen);
        }
        public bool SetOutputs(IReadOnlyList<IoOutputCommand> commands)
        {
            return setOutputs?.Invoke(commands) == true;
        }
        public bool GetOutIO(IO io, ref bool value)
        {
            return (bool)getOutIO?.Invoke(io, ref value);
        }
        public bool GetInIO(IO io, ref bool value)
        {
            return (bool)getInIO?.Invoke(io, ref value);
        }
        public void SettHomeParam(ushort card,ushort axis, ushort dir, ushort speed, ushort homeMode)
        {
            EnsureMotionConfigurationReady();
            EnsureCardInitialized();
            settHomeParam?.Invoke(card, axis, dir, speed, homeMode);
        }
        public void StartHome(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            EnsureResetCompleted();
            EnsureCommandValidated(card, axis, AxisCommandKind.Home, false);
            startHome?.Invoke(card, axis);
        }
        public void CleanPos(ushort card, ushort axis)
        {
            EnsureMotionConfigurationReady();
            EnsureCardInitialized();
            cleanPos?.Invoke(card, axis);
        }
        public double GetAxisPos(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return (double)(getAxisPos?.Invoke(card, axis));
        }
        public void SetMovParam(ushort card ,ushort axis, double minVel, double dMaxVel, double acc, double dec, double dStopVel, double dS_para,int equiv)
        {
            EnsureMotionConfigurationReady();
            EnsureCardInitialized();
            setMovParam?.Invoke(card, axis, minVel, dMaxVel, acc, dec, dStopVel, dS_para, equiv);
        }
        public void Mov(ushort card, ushort axis, double dDist, ushort sPosi_mode, bool wait)
        {
            EnsureCardInitialized();
            EnsureResetCompleted();
            EnsureCommandValidated(card, axis, AxisCommandKind.Motion, false);
            mov?.Invoke(card, axis, dDist, sPosi_mode, false);
            if (wait)
            {
                // 到位轮询用高精度等待器：等待期间不占 CPU，且不受系统定时器 15.6ms 默认粒度影响，
                // 避免 Thread.Sleep 在到位后叠加最长约一个间隔的节拍延迟。
                using (var waiter = new HighResolutionWaiter(CancellationToken.None))
                {
                    while (!GetInPos(card, axis))
                    {
                        waiter.Wait(1);
                    }
                }
            }
        }

        public void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request)
        {
            EnsureCardInitialized();
            EnsureResetCompleted();
            if (request?.Axes == null || request.Positions == null || request.Axes.Count == 0
                || request.Axes.Count != request.Positions.Count
                || request.CoordinateSystem > CoordinatedLinearMoveRequest.MaximumCoordinateSystem)
            {
                throw new ArgumentException("协调直线运动轴或位置列表无效。", nameof(request));
            }
            for (int i = 0; i < request.Axes.Count; i++)
            {
                EnsureCommandValidated(request.Card, request.Axes[i], AxisCommandKind.Motion, false);
            }
            (moveCoordinatedLinear
                ?? throw new InvalidOperationException("协调直线运动接口未初始化"))
                .Invoke(request);
        }

        public bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem)
        {
            EnsureCardInitialized();
            EnsureCoordinateSystemInRange(coordinateSystem);
            return isCoordinatedLinearDone?.Invoke(card, coordinateSystem)
                ?? throw new InvalidOperationException("协调直线运动状态接口未初始化");
        }

        public void StopCoordinatedLinear(ushort card, ushort coordinateSystem, ushort stopMode)
        {
            EnsureCardInitialized();
            EnsureCoordinateSystemInRange(coordinateSystem);
            (stopCoordinatedLinear
                ?? throw new InvalidOperationException("协调直线运动停止接口未初始化"))
                .Invoke(card, coordinateSystem, stopMode);
        }

        private static void EnsureCoordinateSystemInRange(ushort coordinateSystem)
        {
            if (coordinateSystem > CoordinatedLinearMoveRequest.MaximumCoordinateSystem)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(coordinateSystem),
                    $"雷赛总线卡坐标系必须在0到{CoordinatedLinearMoveRequest.MaximumCoordinateSystem}之间。");
            }
        }

        public void Jog(ushort card, ushort axis, ushort sDir)
        {
            EnsureCardInitialized();
            EnsureResetCompleted();
            EnsureCommandValidated(card, axis, AxisCommandKind.Motion, true);
            jog?.Invoke(card, axis, sDir);
        }
        public void StopOneAxis(ushort card, ushort axis, ushort stop_mode)
        {
            EnsureCardInitialized();
            stopOneAxis?.Invoke(card, axis, stop_mode);
        }
        public void StopConnect()
        {
            try
            {
                stopConnect?.Invoke();
            }
            finally
            {
                IsCardInitialized = false;
            }
        }
        public bool HomeStatus(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return (bool)homeStatus?.Invoke(card, axis);
        }
        public bool GetInPos(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return (bool)getInPos?.Invoke(card, axis);
        }
        public bool GetAxisSevon(ushort card, ushort axis)
        {
            return (bool)getAxisSevon?.Invoke(card, axis);
        }
        public void SetAxisSevon(ushort card, ushort axis, bool isSevon)
        {
            if (isSevon)
            {
                EnsureMotionConfigurationReady();
            }
            setAxisSevon?.Invoke(card, axis, isSevon);
        }
        public void DownLoadConfig()
        {
            EnsureMotionConfigurationReady();
            downLoadConfig?.Invoke();
        }
        public void SetAllAxisSevonOn()
        {
            EnsureMotionConfigurationReady();
            setAllAxisSevonOn?.Invoke();
        }
        public void SetAllAxisEquiv()
        {
            EnsureMotionConfigurationReady();
            setAllAxisEquiv?.Invoke();
        }
        public void ResetAxisAlarm(ushort card, ushort axis)
        {
            EnsureMotionConfigurationReady();
            EnsureCardInitialized();
            resetAxisAlarm?.Invoke(card, axis);
        }
        public double GetAxisCurSpeed(ushort card, ushort axis)
        {
            return (double)getAxisCurSpeed?.Invoke(card, axis);
        }
        public uint GetAxisIoStatus(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return getAxisIoStatus?.Invoke(card, axis)
                ?? throw new InvalidOperationException("轴IO状态读取接口未初始化");
        }
        public ushort GetAxisAlarmCode(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return getAxisAlarmCode?.Invoke(card, axis)
                ?? throw new InvalidOperationException("轴报警码读取接口未初始化");
        }
        public void InitCardType()
        {
            EnsureMotionConfigurationReady();
            lock (driverLock)
            {
                if (ls != null)
                {
                    return;
                }
                // 纯机器人项目不创建雷赛驱动对象，也不触发任何 LTDMC 原生入口。
                if (cardStore.GetControlCardCount() == 0)
                {
                    IsCardInitialized = false;
                    return;
                }

                var driver = new LS(cardStore, configPath);
                initCard = driver.InitCard;
                setIO = driver.SetIO;
                setOutputs = driver.SetOutputs;
                getInIO = driver.GetInIO;
                getOutIO = driver.GetOutIO;
                settHomeParam = driver.SettHomeParam;
                startHome = driver.StartHome;
                cleanPos = driver.CleanPos;
                getAxisPos = driver.GetAxisPosEncoder;
                setMovParam = driver.SetMovParam;
                mov = driver.Mov;
                moveCoordinatedLinear = driver.MoveCoordinatedLinear;
                isCoordinatedLinearDone = driver.IsCoordinatedLinearDone;
                stopCoordinatedLinear = driver.StopCoordinatedLinear;
                jog = driver.Jog;
                stopOneAxis = driver.StopOneAxis;
                stopConnect = driver.StopConnect;
                homeStatus = driver.HomeStatus;
                getInPos = driver.GetInPos;
                getAxisSevon = driver.GetAxisSevon;
                setAxisSevon = driver.SetAxisSevon;
                downLoadConfig = driver.DownLoadConfig;
                setAllAxisSevonOn = driver.SetAllAxisSevonOn;
                setAllAxisEquiv = driver.SetAllAxisEquiv;
                resetAxisAlarm = driver.ResetAxisAlarm;
                getAxisCurSpeed = driver.GetAxisCurSpeed;
                getAxisAlarmCode = driver.GetAxisAlarmCode;
                getAxisIoStatus = driver.GetAxisIoStatus;
                ls = driver;
            }
        }

    }
}
