using Automation.MotionControl;
// 模块：运行时 / 手动运动。
// 职责范围：在平台安全门禁下协调手动运动请求。
// 安全边界：拒绝原因通过 CommandRejected 返回；流程资源占用或状态不确定时不允许绕过门禁直接调用驱动。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Automation.DeviceSdk;

namespace Automation
{
    public sealed class ManualMotionRejectedEventArgs : EventArgs
    {
        public string Title { get; }
        public string Message { get; }

        public ManualMotionRejectedEventArgs(string title, string message)
        {
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public sealed class ManualMotionParameters
    {
        public double MinVelocity { get; }
        public double MaxVelocity { get; }
        public double Acceleration { get; }
        public double Deceleration { get; }
        public double StopVelocity { get; }
        public double SmoothingTime { get; }
        public int Equivalent { get; }

        public ManualMotionParameters(double minVelocity, double maxVelocity, double acceleration,
            double deceleration, double stopVelocity, double smoothingTime, int equivalent)
        {
            if (maxVelocity <= 0 || acceleration <= 0 || deceleration <= 0 || equivalent <= 0
                || minVelocity < 0 || stopVelocity < 0 || smoothingTime < 0
                || !IsFinite(minVelocity) || !IsFinite(maxVelocity) || !IsFinite(acceleration)
                || !IsFinite(deceleration) || !IsFinite(stopVelocity) || !IsFinite(smoothingTime))
            {
                throw new ArgumentOutOfRangeException(nameof(maxVelocity), "手动调试运动参数无效。");
            }
            MinVelocity = minVelocity;
            MaxVelocity = maxVelocity;
            Acceleration = acceleration;
            Deceleration = deceleration;
            StopVelocity = stopVelocity;
            SmoothingTime = smoothingTime;
            Equivalent = equivalent;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class ManualAxisMoveRequest
    {
        public ushort Card { get; }
        public ushort Axis { get; }
        public double Distance { get; }
        public ushort PositionMode { get; }
        public ManualMotionParameters Parameters { get; }

        public ManualAxisMoveRequest(ushort card, ushort axis, double distance, ushort positionMode,
            ManualMotionParameters parameters)
        {
            Card = card;
            Axis = axis;
            Distance = distance;
            PositionMode = positionMode;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }
    }

    /// <summary>
    /// 手动运动的应用服务。负责运行门禁、流程资源互斥、参数下发和异步完成监控；
    /// 自动流程仍直接使用 IMotionRuntime，不经过本服务。
    /// </summary>
    public sealed class ManualMotionService
    {
        private sealed class StationOperationRegistration
        {
            public long Version { get; set; }
            public IDisposable Resources { get; set; }
        }

        private const int MotionTimeoutMilliseconds = 120000;
        private readonly object parametersLock = new object();
        private readonly Dictionary<long, ManualMotionParameters> parametersByAxis =
            new Dictionary<long, ManualMotionParameters>();
        private readonly object axisOperationLock = new object();
        private readonly Dictionary<long, long> axisOperations = new Dictionary<long, long>();
        private long axisOperationVersion;
        private readonly object stationOperationLock = new object();
        private readonly Dictionary<short, StationOperationRegistration> stationOperations =
            new Dictionary<short, StationOperationRegistration>();
        private long stationOperationVersion;
        private readonly IMotionRuntime motion;
        private readonly ProcessEngine engine;
        private readonly ValueConfigStore valueStore;
        private readonly Func<bool> isConfigurationRestartRequired;
        private readonly Action<string> setSecurityLock;
        private readonly AccountSecurityService accounts;

        public event EventHandler<ManualMotionRejectedEventArgs> CommandRejected;

        public ManualMotionService(IMotionRuntime motion, ProcessEngine engine, ValueConfigStore valueStore,
            Func<bool> isConfigurationRestartRequired, Action<string> setSecurityLock)
            : this(motion, engine, valueStore, isConfigurationRestartRequired, setSecurityLock, null)
        {
        }

        internal ManualMotionService(IMotionRuntime motion, ProcessEngine engine, ValueConfigStore valueStore,
            Func<bool> isConfigurationRestartRequired, Action<string> setSecurityLock,
            AccountSecurityService accounts)
        {
            this.motion = motion ?? throw new ArgumentNullException(nameof(motion));
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.valueStore = valueStore ?? throw new ArgumentNullException(nameof(valueStore));
            this.isConfigurationRestartRequired = isConfigurationRestartRequired
                ?? throw new ArgumentNullException(nameof(isConfigurationRestartRequired));
            this.setSecurityLock = setSecurityLock ?? throw new ArgumentNullException(nameof(setSecurityLock));
            this.accounts = accounts;
        }

        public void ConfigureAxis(ushort card, ushort axis, ManualMotionParameters parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            lock (parametersLock)
            {
                parametersByAxis[BuildAxisKey(card, axis)] = parameters;
            }
        }

        public bool TryMove(ushort card, ushort axis, double distance, ushort positionMode, bool wait)
        {
            if (!CanOperateMotion(out string permissionError))
            {
                Reject("权限不足", permissionError);
                return false;
            }
            if (!TryValidateGate(out string gateError))
            {
                Reject("运动门禁", gateError);
                return false;
            }
            if (!TryGetParameters(card, axis, out ManualMotionParameters parameters))
            {
                Reject("手动运动", $"手动调试运动参数尚未设置:{card}-{axis}");
                return false;
            }
            if (!TryAcquireAxisOperation(card, axis, out long operation,
                    out string resourceError))
            {
                Reject("运动资源占用", resourceError);
                return false;
            }

            try
            {
                using (motion.ValidateAxesForCommand(new[]
                {
                    new AxisCommandRequest(card, axis, AxisCommandKind.Motion)
                }))
                {
                    ApplyParameters(card, axis, parameters);
                    motion.Mov(card, axis, distance, positionMode, false);
                }
                if (wait)
                {
                    WaitForStop(card, axis, MotionTimeoutMilliseconds);
                    CompleteAxisOperation(card, axis, operation);
                }
                else
                {
                    _ = MonitorMoveCompletionAsync(card, axis, operation);
                }
                return true;
            }
            catch (Exception ex)
            {
                TryStopAxisOperationAfterCommandFailure(card, axis, operation, ex.Message);
                Reject("手动运动失败", ex.Message);
                return false;
            }
        }

        public bool TryMoveAxes(IReadOnlyCollection<ManualAxisMoveRequest> commands)
        {
            if (!CanOperateMotion(out string permissionError))
            {
                Reject("权限不足", permissionError);
                return false;
            }
            if (commands == null || commands.Count == 0)
            {
                Reject("手动运动", "手动运动轴列表为空。");
                return false;
            }
            if (!TryValidateGate(out string gateError))
            {
                Reject("运动门禁", gateError);
                return false;
            }
            if (commands.Any(item => item == null))
            {
                Reject("手动运动", "手动运动轴列表包含空项。");
                return false;
            }

            List<ManualAxisMoveRequest> distinctCommands = commands
                .GroupBy(item => BuildAxisKey(item.Card, item.Axis))
                .Select(group => group.First())
                .ToList();
            if (distinctCommands.Count != commands.Count)
            {
                Reject("手动运动", "手动运动轴列表包含重复轴。");
                return false;
            }
            List<AxisCommandRequest> validationRequests = distinctCommands
                .Select(item => new AxisCommandRequest(item.Card, item.Axis, AxisCommandKind.Motion))
                .ToList();
            if (!engine.TryReserveManualMotionResources(validationRequests,
                    out IDisposable reservation, out string resourceError))
            {
                Reject("运动资源占用", resourceError);
                return false;
            }

            var acquiredAxes = new List<ManualAxisMoveRequest>();
            var startedAxes = new List<ManualAxisMoveRequest>();
            var operations = new Dictionary<long, long>();
            try
            {
                using (reservation)
                {
                    foreach (ManualAxisMoveRequest command in distinctCommands)
                    {
                        if (!TryAcquireAxisOperation(command.Card, command.Axis,
                                out long operation, out resourceError))
                        {
                            throw new InvalidOperationException(resourceError);
                        }
                        acquiredAxes.Add(command);
                        operations[BuildAxisKey(command.Card, command.Axis)] = operation;
                    }
                    using (motion.ValidateAxesForCommand(validationRequests))
                    {
                        foreach (ManualAxisMoveRequest command in distinctCommands)
                        {
                            ApplyParameters(command.Card, command.Axis, command.Parameters);
                        }
                        foreach (ManualAxisMoveRequest command in distinctCommands)
                        {
                            startedAxes.Add(command);
                            motion.Mov(command.Card, command.Axis, command.Distance,
                                command.PositionMode, false);
                        }
                    }
                }
                foreach (ManualAxisMoveRequest command in startedAxes)
                {
                    long operation = operations[BuildAxisKey(command.Card, command.Axis)];
                    _ = MonitorMoveCompletionAsync(command.Card, command.Axis, operation);
                }
                return true;
            }
            catch (Exception ex)
            {
                foreach (ManualAxisMoveRequest command in acquiredAxes)
                {
                    long operation = operations[BuildAxisKey(command.Card, command.Axis)];
                    bool commandAttempted = startedAxes.Any(item =>
                        item.Card == command.Card && item.Axis == command.Axis);
                    if (!commandAttempted)
                    {
                        CompleteAxisOperation(command.Card, command.Axis, operation);
                    }
                    else
                    {
                        TryStopAxisOperationAfterCommandFailure(
                            command.Card, command.Axis, operation, ex.Message);
                    }
                }
                Reject("工站移动失败", ex.Message);
                return false;
            }
        }

        public bool TryJog(ushort card, ushort axis, ushort direction)
        {
            if (!CanOperateMotion(out string permissionError))
            {
                Reject("权限不足", permissionError);
                return false;
            }
            if (!TryValidateGate(out string gateError))
            {
                Reject("运动门禁", gateError);
                return false;
            }
            if (!TryGetParameters(card, axis, out ManualMotionParameters parameters))
            {
                Reject("手动运动", $"手动调试运动参数尚未设置:{card}-{axis}");
                return false;
            }
            if (!TryAcquireAxisOperation(card, axis, out long operation,
                    out string resourceError))
            {
                Reject("运动资源占用", resourceError);
                return false;
            }
            // Jog 没有完成监控，但仍登记新代次，使此前动作遗留的观察者立即失效。
            try
            {
                using (motion.ValidateAxesForCommand(new[]
                {
                    new AxisCommandRequest(card, axis, AxisCommandKind.Motion)
                }))
                {
                    ApplyParameters(card, axis, parameters);
                    motion.Jog(card, axis, direction);
                }
                return true;
            }
            catch (Exception ex)
            {
                TryStopAxisOperationAfterCommandFailure(card, axis, operation, ex.Message);
                Reject("手动运动失败", ex.Message);
                return false;
            }
        }

        public bool TryStop(ushort card, ushort axis, ushort stopMode)
        {
            string failure = null;
            lock (axisOperationLock)
            {
                // 先让旧观察者失效，但停止确认前保持资源占用。
                axisOperations.Remove(BuildAxisKey(card, axis));
                try
                {
                    motion.StopOneAxis(card, axis, stopMode);
                    engine.ReleaseManualMotionResource(card, axis);
                }
                catch (Exception ex)
                {
                    failure = $"手动停止轴失败:{card}-{axis} {ex.Message}";
                }
            }
            if (failure == null)
            {
                return true;
            }
            setSecurityLock(failure);
            Reject("手动停止失败", failure);
            return false;
        }

        /// <summary>
        /// 按 3.0 的六通道工站语义执行单通道人工运动。
        /// 机器人的小步距由控制器执行 Inch/偏移，大步距由实现解释为连续 Jog；
        /// 轴工站则由 AxisMotionStation 映射到对应物理轴。
        /// </summary>
        public bool TryMoveStationAxis(short station, short axis, double offset,
            double speedPercent, bool wait = false, short tool = 0,
            bool retainUntilStop = false)
        {
            if (station < 0 || axis < 0 || axis >= 6
                || !IsFinite(offset) || !IsValidPercent(speedPercent) || tool < 0)
            {
                Reject("手动运动", "工站索引、六通道、步距、速度或工具号无效。");
                return false;
            }
            return TryRunStationMotion(
                station,
                axis,
                false,
                wait,
                false,
                () =>
                {
                    EnsureStationResult(station, "设置人工运动速度",
                        motion.SetStationSpeed(station, speedPercent, speedPercent,
                            speedPercent, axis, StationSpeedType.Joint));
                    EnsureStationResult(station, "设置直线运动速度",
                        motion.SetStationSpeed(station, speedPercent, speedPercent,
                            speedPercent, 0, StationSpeedType.Move));
                    return motion.MoveStationAxis(station, axis, offset,
                        StationAxisMoveMode.Relative, tool);
                },
                !retainUntilStop);
        }

        public bool TryMoveStationToPoint(short station, DataPos point, double speedPercent,
            StationMoveMode mode = StationMoveMode.Go, bool wait = false, short tool = 0)
        {
            if (!TryGetStationPointCapacity(station, out int pointCapacity, out string stationError)
                || point == null || point.Index < 0
                || point.Index >= pointCapacity
                || !point.IsMotionReady
                || !IsValidPercent(speedPercent) || tool < 0
                || !Enum.IsDefined(typeof(StationMoveMode), mode))
            {
                Reject("工站移动", stationError
                    ?? "工站、目标点位、速度、模式或工具号无效。");
                return false;
            }
            return TryRunStationMotion(
                station,
                -1,
                false,
                wait,
                mode == StationMoveMode.Move,
                () =>
                {
                    EnsureStationResult(station, "设置工站运动速度",
                        motion.SetStationSpeed(station, speedPercent, speedPercent,
                            speedPercent, -1, StationSpeedType.Joint));
                    EnsureStationResult(station, "设置直线运动速度",
                        motion.SetStationSpeed(station, speedPercent, speedPercent,
                            speedPercent, 0, StationSpeedType.Move));
                    return motion.MoveStationToPoint(station, point, mode, null, tool);
                });
        }

        /// <summary>
        /// 按 3.0 “取点即提交”语义读取机器人当前位置，并同步写入控制器与 StationStore。
        /// </summary>
        public bool TryTeachStationPoint(short station, DataPos configuredPoint,
            out DataPos taughtPoint, short tool = 0)
        {
            taughtPoint = null;
            if (!TryGetStationPointCapacity(station, out int pointCapacity, out string stationError)
                || configuredPoint == null || configuredPoint.Index < 0
                || configuredPoint.Index >= pointCapacity
                || string.IsNullOrWhiteSpace(configuredPoint.Name)
                || tool < 0)
            {
                Reject("工站取点", stationError
                    ?? "工站、点位索引、名称或工具号无效。");
                return false;
            }
            if (!CanOperateMotion(out string permissionError))
            {
                Reject("权限不足", permissionError);
                return false;
            }
            if (!TryValidateGate(out string gateError))
            {
                Reject("运动门禁", gateError);
                return false;
            }
            if (!engine.TryAcquireManualStationMotionResource(station, out string resourceError))
            {
                Reject("运动资源占用", resourceError);
                return false;
            }

            try
            {
                MotionStationResult positionResult = motion.GetStationPosition(
                    station, tool, out DataPos currentPosition);
                EnsureStationResult(station, "读取机器人当前位置", positionResult);
                if (currentPosition == null)
                {
                    throw new InvalidOperationException("机器人返回的当前位置为空。");
                }

                DataPos candidate = (DataPos)configuredPoint.Clone();
                candidate.X = currentPosition.X;
                candidate.Y = currentPosition.Y;
                candidate.Z = currentPosition.Z;
                candidate.U = currentPosition.U;
                candidate.V = currentPosition.V;
                candidate.W = currentPosition.W;
                candidate.Pose = currentPosition.Pose?.ToArray() ?? candidate.Pose;
                candidate.IsTaught = true;

                SaveStationPointCore(station, candidate);
                taughtPoint = candidate;
                return true;
            }
            catch (Exception ex)
            {
                Reject("工站取点失败", ex.Message);
                return false;
            }
            finally
            {
                engine.ReleaseManualStationMotionResource(station);
            }
        }

        /// <summary>
        /// 把人工编辑后的已示教点位同步写入机器人控制器与 StationStore。
        /// </summary>
        public bool TrySaveStationPoint(short station, DataPos point)
        {
            if (!TryGetStationPointCapacity(station, out int pointCapacity, out string stationError)
                || point == null || point.Index < 0
                || point.Index >= pointCapacity
                || !point.IsMotionReady
                || point.GetAllValues().Any(value => !IsFinite(value)))
            {
                Reject("保存工站点位", stationError
                    ?? "工站或已示教点位参数无效。");
                return false;
            }
            if (!CanOperateMotion(out string permissionError))
            {
                Reject("权限不足", permissionError);
                return false;
            }
            if (!TryValidateGate(out string gateError))
            {
                Reject("运动门禁", gateError);
                return false;
            }
            if (!engine.TryAcquireManualStationMotionResource(station, out string resourceError))
            {
                Reject("运动资源占用", resourceError);
                return false;
            }

            try
            {
                SaveStationPointCore(station, point);
                return true;
            }
            catch (Exception ex)
            {
                Reject("保存机器人点位失败", ex.Message);
                return false;
            }
            finally
            {
                engine.ReleaseManualStationMotionResource(station);
            }
        }

        private void SaveStationPointCore(short station, DataPos point)
        {
            MotionStationResult saveResult = motion.SaveStationPoint(station, point);
            if (saveResult == MotionStationResult.InconsistentState)
            {
                setSecurityLock($"机器人工站点位写入后状态不一致:{station}-{point.Name}");
            }
            EnsureStationResult(station, "保存机器人点位", saveResult);
        }

        public bool TryHomeStation(short station, short axis = -1, bool group = false,
            bool wait = false)
        {
            if (station < 0 || axis < -1 || axis >= 6)
            {
                Reject("工站回零", "工站索引或六通道无效。");
                return false;
            }
            return TryRunStationMotion(
                station,
                axis,
                true,
                wait,
                false,
                () => motion.HomeStation(station, axis, false, group));
        }

        /// <summary>
        /// 停止始终可用，不经过启动门禁；只有确认工站接受停止后才释放整组资源。
        /// </summary>
        public bool TryStopStation(short station, bool emergency = false)
        {
            if (!TryGetStationConfiguration(
                    station, out DataStation configuration, out string stationError))
            {
                Reject("工站停止", stationError);
                return false;
            }

            StationOperationRegistration operation = TakeStationOperation(station);
            bool stopped = false;
            try
            {
                MotionStationResult result = motion.StopStation(station, emergency);
                EnsureStationResult(station, emergency ? "急停工站" : "停止工站", result);
                if (configuration.Type == StationType.Axis)
                {
                    WaitForAxisStationStop(configuration, 30000);
                }
                stopped = true;
                return true;
            }
            catch (Exception ex)
            {
                string message = $"手动停止工站失败:{station} {ex.Message}";
                setSecurityLock(message);
                Reject("手动停止失败", message);
                return false;
            }
            finally
            {
                if (stopped)
                {
                    ReleaseAxisOperationsAfterStationStop(configuration);
                    if (operation != null)
                    {
                        operation.Resources.Dispose();
                    }
                    else
                    {
                        engine.ReleaseManualStationMotionResource(station);
                    }
                }
                else
                {
                    RestoreStationOperation(station, operation);
                }
            }
        }

        private bool TryRunStationMotion(short station, int waitAxis, bool isHome, bool wait,
            bool requiresCoordinateSystem, Func<MotionStationResult> command,
            bool monitorCompletion = true)
        {
            if (!CanOperateMotion(out string permissionError))
            {
                Reject("权限不足", permissionError);
                return false;
            }
            if (!TryValidateGate(out string gateError))
            {
                Reject("运动门禁", gateError);
                return false;
            }
            if (!engine.TryAcquireManualStationMotionResources(
                    station,
                    requiresCoordinateSystem,
                    out IDisposable resources,
                    out string resourceError))
            {
                Reject("运动资源占用", resourceError);
                return false;
            }

            long operation = RegisterStationOperation(station, resources);
            try
            {
                MotionStationResult commandResult = command();
                EnsureStationResult(station, isHome ? "工站回零" : "工站运动", commandResult);
                if (wait)
                {
                    MotionStationResult waitResult = motion.WaitStationMotion(
                        station, isHome, waitAxis, MotionTimeoutMilliseconds);
                    EnsureStationResult(station, "等待工站运动完成", waitResult);
                    CompleteStationOperation(station, operation);
                }
                else if (monitorCompletion)
                {
                    _ = MonitorStationCompletionAsync(station, waitAxis, isHome, operation);
                }
                // 连续工站 Jog 与物理轴 Jog 一致，整组资源由明确的 MouseUp/停止命令释放。
                return true;
            }
            catch (Exception ex)
            {
                if (TryStopStationAfterCommandFailure(station, ex.Message))
                {
                    CompleteStationOperation(station, operation);
                }
                Reject(isHome ? "工站回零失败" : "工站运动失败", ex.Message);
                return false;
            }
        }

        private async Task MonitorStationCompletionAsync(short station, int axis, bool isHome,
            long operation)
        {
            MotionStationResult result;
            try
            {
                result = await Task.Run(() => motion.WaitStationMotion(
                        station, isHome, axis, MotionTimeoutMilliseconds))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!IsCurrentStationOperation(station, operation))
                {
                    return;
                }
                bool stopped = TryStopStationAfterCommandFailure(station, ex.Message);
                if (stopped)
                {
                    CompleteStationOperation(station, operation);
                    setSecurityLock($"工站运动监控异常，工站已停止:{station} {ex.Message}");
                }
                return;
            }

            if (!IsCurrentStationOperation(station, operation))
            {
                return;
            }
            if (result == MotionStationResult.Success)
            {
                CompleteStationOperation(station, operation);
                return;
            }

            string error = BuildStationResultError(station, "监控工站运动", result);
            bool stationStopped = TryStopStationAfterCommandFailure(station, error);
            if (stationStopped)
            {
                CompleteStationOperation(station, operation);
                setSecurityLock($"工站运动监控失败，工站已停止:{station} {error}");
            }
        }

        private bool TryStopStationAfterCommandFailure(short station, string commandError)
        {
            try
            {
                MotionStationResult result = motion.StopStation(station, false);
                if (result != MotionStationResult.Success)
                {
                    throw new InvalidOperationException(BuildStationResultError(
                        station, "停止工站", result));
                }
                return true;
            }
            catch (Exception stopException)
            {
                setSecurityLock(
                    $"工站运动失败后停止失败，资源保持占用:{station} {commandError}; {stopException.Message}");
                return false;
            }
        }

        private void EnsureStationResult(short station, string operation, MotionStationResult result)
        {
            if (result != MotionStationResult.Success)
            {
                throw new InvalidOperationException(BuildStationResultError(station, operation, result));
            }
        }

        private string BuildStationResultError(short station, string operation, MotionStationResult result)
        {
            string detail = motion.GetStationStatus(station)?.LastError;
            return string.IsNullOrWhiteSpace(detail)
                ? $"{operation}失败:{station} 返回结果:{result}"
                : $"{operation}失败:{station} 返回结果:{result}，{detail}";
        }

        private long RegisterStationOperation(short station, IDisposable resources)
        {
            StationOperationRegistration previous = null;
            long operation;
            lock (stationOperationLock)
            {
                stationOperations.TryGetValue(station, out previous);
                operation = ++stationOperationVersion;
                stationOperations[station] = new StationOperationRegistration
                {
                    Version = operation,
                    Resources = resources
                };
            }
            // 安全停机可能已直接释放旧资源，但旧监控尚未返回。独立 owner token
            // 保证这里清理旧 lease 时不会误释放刚登记的新动作。
            previous?.Resources?.Dispose();
            return operation;
        }

        private bool IsCurrentStationOperation(short station, long operation)
        {
            lock (stationOperationLock)
            {
                return stationOperations.TryGetValue(
                        station, out StationOperationRegistration current)
                    && current.Version == operation;
            }
        }

        private void CompleteStationOperation(short station, long operation)
        {
            StationOperationRegistration completed;
            lock (stationOperationLock)
            {
                if (!stationOperations.TryGetValue(
                        station, out StationOperationRegistration current)
                    || current.Version != operation)
                {
                    return;
                }
                stationOperations.Remove(station);
                completed = current;
            }
            completed.Resources.Dispose();
        }

        private StationOperationRegistration TakeStationOperation(short station)
        {
            lock (stationOperationLock)
            {
                stationOperations.TryGetValue(station, out StationOperationRegistration operation);
                stationOperations.Remove(station);
                return operation;
            }
        }

        private void RestoreStationOperation(
            short station,
            StationOperationRegistration operation)
        {
            if (operation == null)
            {
                return;
            }
            lock (stationOperationLock)
            {
                if (!stationOperations.ContainsKey(station))
                {
                    stationOperations[station] = operation;
                }
            }
        }

        private bool TryValidateGate(out string error)
        {
            if (!engine.TryValidateStartGate(out error))
            {
                return false;
            }
            PlatformReadinessState readiness = engine.Context?.Readiness;
            if (readiness?.MotionConfigFaulted == true)
            {
                string reason = string.IsNullOrWhiteSpace(readiness.MotionConfigFaultReason)
                    ? "未提供详细原因。"
                    : readiness.MotionConfigFaultReason;
                error = "运动配置故障，禁止执行手动轴或工站动作：" + reason;
                return false;
            }
            if (isConfigurationRestartRequired())
            {
                error = "运动设备配置已变更，必须重启程序后才能执行轴运动。";
                return false;
            }
            if (!valueStore.TryGetValueByName("复位状态", out DicValue resetValue)
                || resetValue == null
                || !string.Equals(resetValue.Type, "double", StringComparison.OrdinalIgnoreCase)
                || !double.TryParse(resetValue.Value, out double resetRaw)
                || resetRaw != (double)ResetStatus.ResetCompleted)
            {
                error = "系统尚未复位完成，禁止手动运动。";
                return false;
            }
            error = null;
            return true;
        }

        private bool TryGetStationPointCapacity(
            short station,
            out int capacity,
            out string error)
        {
            capacity = 0;
            error = null;
            PlatformReadinessState readiness = engine.Context?.Readiness;
            if (readiness?.MotionConfigFaulted == true)
            {
                string reason = string.IsNullOrWhiteSpace(readiness.MotionConfigFaultReason)
                    ? "未提供详细原因。"
                    : readiness.MotionConfigFaultReason;
                error = "运动配置故障，禁止执行手动工站动作：" + reason;
                return false;
            }
            if (!TryGetStationConfiguration(station, out DataStation configuration, out error))
            {
                return false;
            }
            capacity = DataStation.GetPointCapacity(configuration.Type);
            return true;
        }

        private bool TryGetStationConfiguration(
            short station,
            out DataStation configuration,
            out string error)
        {
            IList<DataStation> stations = engine.Context?.Stations;
            if (station < 0 || stations == null || station >= stations.Count
                || stations[station] == null)
            {
                configuration = null;
                error = $"工站索引无效或配置尚未发布:{station}";
                return false;
            }
            configuration = stations[station];
            error = null;
            return true;
        }

        private void WaitForAxisStationStop(DataStation station, int timeoutMilliseconds)
        {
            AxisCommandRequest[] axes = GetAxisStationResources(station).ToArray();
            if (axes.Length == 0)
            {
                throw new InvalidOperationException("轴工站没有配置任何有效物理轴。");
            }
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (!axes.All(item => motion.GetInPos(item.Card, item.Axis)))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("等待轴工站停止超时。");
                }
                Thread.Sleep(5);
            }
        }

        private void ReleaseAxisOperationsAfterStationStop(DataStation station)
        {
            if (station?.Type != StationType.Axis)
            {
                return;
            }
            lock (axisOperationLock)
            {
                foreach (AxisCommandRequest axis in GetAxisStationResources(station))
                {
                    axisOperations.Remove(BuildAxisKey(axis.Card, axis.Axis));
                    engine.ReleaseManualMotionResource(axis.Card, axis.Axis);
                }
            }
        }

        private static IEnumerable<AxisCommandRequest> GetAxisStationResources(DataStation station)
        {
            if (station?.dataAxis?.axisConfigs == null)
            {
                yield break;
            }
            var keys = new HashSet<long>();
            foreach (AxisConfig configuredAxis in station.dataAxis.axisConfigs)
            {
                if (configuredAxis == null || configuredAxis.AxisName == "-1"
                    || configuredAxis.axis == null
                    || !ushort.TryParse(configuredAxis.CardNum, out ushort card)
                    || configuredAxis.axis.AxisNum < 0
                    || configuredAxis.axis.AxisNum > ushort.MaxValue)
                {
                    continue;
                }
                ushort axis = (ushort)configuredAxis.axis.AxisNum;
                if (keys.Add(BuildAxisKey(card, axis)))
                {
                    yield return new AxisCommandRequest(card, axis, AxisCommandKind.Motion);
                }
            }
        }

        private bool CanOperateMotion(out string error)
        {
            if (accounts == null)
            {
                error = null;
                return true;
            }
            return accounts.AuthorizeApplicationOperation(
                PlatformPermissionCodes.MotionOperate,
                "执行手动运动",
                out error);
        }

        private bool TryGetParameters(ushort card, ushort axis, out ManualMotionParameters parameters)
        {
            lock (parametersLock)
            {
                return parametersByAxis.TryGetValue(BuildAxisKey(card, axis), out parameters);
            }
        }

        private void ApplyParameters(ushort card, ushort axis, ManualMotionParameters parameters)
        {
            motion.SetMovParam(card, axis, parameters.MinVelocity, parameters.MaxVelocity,
                parameters.Acceleration, parameters.Deceleration, parameters.StopVelocity,
                parameters.SmoothingTime, parameters.Equivalent);
        }

        private async Task MonitorMoveCompletionAsync(ushort card, ushort axis, long operation)
        {
            // 不在 UI 发起线程同步执行第一次硬件读取；后续结果仍由动作代次确认归属。
            await Task.Yield();
            try
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(MotionTimeoutMilliseconds);
                while (DateTime.UtcNow < deadline)
                {
                    if (!IsCurrentAxisOperation(card, axis, operation))
                    {
                        return;
                    }
                    if (motion.GetInPos(card, axis))
                    {
                        CompleteAxisOperation(card, axis, operation);
                        return;
                    }
                    await Task.Delay(10).ConfigureAwait(false);
                }
                if (!IsCurrentAxisOperation(card, axis, operation))
                {
                    return;
                }
                throw new TimeoutException($"手动运动超时:{card}-{axis}");
            }
            catch (Exception ex)
            {
                HandleAxisMonitorFailure(card, axis, operation, ex);
            }
        }

        private void WaitForStop(ushort card, ushort axis, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (!motion.GetInPos(card, axis))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException($"手动运动超时:{card}-{axis}");
                }
                Thread.Sleep(5);
            }
        }

        private void HandleAxisMonitorFailure(ushort card, ushort axis, long operation,
            Exception monitorException)
        {
            string securityError = null;
            lock (axisOperationLock)
            {
                long key = BuildAxisKey(card, axis);
                if (!axisOperations.TryGetValue(key, out long current)
                    || current != operation)
                {
                    return;
                }
                try
                {
                    motion.StopOneAxis(card, axis, 0);
                    axisOperations.Remove(key);
                    engine.ReleaseManualMotionResource(card, axis);
                    securityError =
                        $"手动运动监控异常，轴已停止:{card}-{axis} {monitorException.Message}";
                }
                catch (Exception stopException)
                {
                    // 停止未确认时保留当前代次与资源，后续只能由明确停止重试释放。
                    securityError =
                        $"手动运动监控失败且停止轴失败，资源保持占用:{card}-{axis} "
                        + $"{monitorException.Message}; {stopException.Message}";
                }
            }
            setSecurityLock(securityError);
        }

        private void TryStopAxisOperationAfterCommandFailure(ushort card, ushort axis,
            long operation, string commandError)
        {
            string securityError = null;
            lock (axisOperationLock)
            {
                long key = BuildAxisKey(card, axis);
                if (!axisOperations.TryGetValue(key, out long current)
                    || current != operation)
                {
                    return;
                }
                try
                {
                    motion.StopOneAxis(card, axis, 0);
                    axisOperations.Remove(key);
                    engine.ReleaseManualMotionResource(card, axis);
                }
                catch (Exception stopException)
                {
                    securityError =
                        $"手动运动失败后停止轴失败，资源保持占用:{card}-{axis} "
                        + $"{commandError}; {stopException.Message}";
                }
            }
            if (securityError != null)
            {
                setSecurityLock(securityError);
            }
        }

        private bool TryAcquireAxisOperation(ushort card, ushort axis, out long operation,
            out string error)
        {
            lock (axisOperationLock)
            {
                if (!engine.TryAcquireManualMotionResource(card, axis, out error))
                {
                    operation = 0;
                    return false;
                }
                operation = ++axisOperationVersion;
                axisOperations[BuildAxisKey(card, axis)] = operation;
                return true;
            }
        }

        private bool IsCurrentAxisOperation(ushort card, ushort axis, long operation)
        {
            lock (axisOperationLock)
            {
                return axisOperations.TryGetValue(BuildAxisKey(card, axis), out long current)
                    && current == operation;
            }
        }

        private void CompleteAxisOperation(ushort card, ushort axis, long operation)
        {
            lock (axisOperationLock)
            {
                long key = BuildAxisKey(card, axis);
                if (!axisOperations.TryGetValue(key, out long current)
                    || current != operation)
                {
                    return;
                }
                axisOperations.Remove(key);
                // 代次确认与资源释放在同一临界区，旧观察者不能释放后续动作的资源。
                engine.ReleaseManualMotionResource(card, axis);
            }
        }

        private void Reject(string title, string message)
        {
            CommandRejected?.Invoke(this, new ManualMotionRejectedEventArgs(title, message));
        }

        private static long BuildAxisKey(ushort card, ushort axis)
        {
            return ((long)card << 32) | axis;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsValidPercent(double value)
        {
            return IsFinite(value) && value > 0 && value <= 100;
        }
    }
}
