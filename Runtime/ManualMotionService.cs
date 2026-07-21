using Automation.MotionControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        private const int MotionTimeoutMilliseconds = 120000;
        private readonly object parametersLock = new object();
        private readonly Dictionary<long, ManualMotionParameters> parametersByAxis =
            new Dictionary<long, ManualMotionParameters>();
        private readonly IMotionRuntime motion;
        private readonly ProcessEngine engine;
        private readonly ValueConfigStore valueStore;
        private readonly Func<bool> isConfigurationRestartRequired;
        private readonly Action<string> setSecurityLock;

        public event EventHandler<ManualMotionRejectedEventArgs> CommandRejected;

        public ManualMotionService(IMotionRuntime motion, ProcessEngine engine, ValueConfigStore valueStore,
            Func<bool> isConfigurationRestartRequired, Action<string> setSecurityLock)
        {
            this.motion = motion ?? throw new ArgumentNullException(nameof(motion));
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.valueStore = valueStore ?? throw new ArgumentNullException(nameof(valueStore));
            this.isConfigurationRestartRequired = isConfigurationRestartRequired
                ?? throw new ArgumentNullException(nameof(isConfigurationRestartRequired));
            this.setSecurityLock = setSecurityLock ?? throw new ArgumentNullException(nameof(setSecurityLock));
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
            if (!engine.TryAcquireManualMotionResource(card, axis, out string resourceError))
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
                    engine.ReleaseManualMotionResource(card, axis);
                }
                else
                {
                    _ = MonitorMoveCompletionAsync(card, axis);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (TryStopAfterCommandFailure(card, axis, ex.Message))
                {
                    engine.ReleaseManualMotionResource(card, axis);
                }
                Reject("手动运动失败", ex.Message);
                return false;
            }
        }

        public bool TryMoveAxes(IReadOnlyCollection<ManualAxisMoveRequest> commands)
        {
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
            try
            {
                using (reservation)
                {
                    foreach (ManualAxisMoveRequest command in distinctCommands)
                    {
                        if (!engine.TryAcquireManualMotionResource(command.Card, command.Axis,
                                out resourceError))
                        {
                            throw new InvalidOperationException(resourceError);
                        }
                        acquiredAxes.Add(command);
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
                    _ = MonitorMoveCompletionAsync(command.Card, command.Axis);
                }
                return true;
            }
            catch (Exception ex)
            {
                foreach (ManualAxisMoveRequest command in acquiredAxes)
                {
                    bool commandAttempted = startedAxes.Any(item =>
                        item.Card == command.Card && item.Axis == command.Axis);
                    if (!commandAttempted
                        || TryStopAfterCommandFailure(command.Card, command.Axis, ex.Message))
                    {
                        engine.ReleaseManualMotionResource(command.Card, command.Axis);
                    }
                }
                Reject("工站移动失败", ex.Message);
                return false;
            }
        }

        public bool TryJog(ushort card, ushort axis, ushort direction)
        {
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
            if (!engine.TryAcquireManualMotionResource(card, axis, out string resourceError))
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
                    motion.Jog(card, axis, direction);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (TryStopAfterCommandFailure(card, axis, ex.Message))
                {
                    engine.ReleaseManualMotionResource(card, axis);
                }
                Reject("手动运动失败", ex.Message);
                return false;
            }
        }

        public bool TryStop(ushort card, ushort axis, ushort stopMode)
        {
            bool stopped = false;
            try
            {
                motion.StopOneAxis(card, axis, stopMode);
                stopped = true;
                return true;
            }
            catch (Exception ex)
            {
                string message = $"手动停止轴失败:{card}-{axis} {ex.Message}";
                setSecurityLock(message);
                Reject("手动停止失败", message);
                return false;
            }
            finally
            {
                if (stopped)
                {
                    engine.ReleaseManualMotionResource(card, axis);
                }
            }
        }

        private bool TryValidateGate(out string error)
        {
            if (!engine.TryValidateStartGate(out error))
            {
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

        private async Task MonitorMoveCompletionAsync(ushort card, ushort axis)
        {
            try
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(MotionTimeoutMilliseconds);
                while (DateTime.UtcNow < deadline)
                {
                    if (motion.GetInPos(card, axis))
                    {
                        engine.ReleaseManualMotionResource(card, axis);
                        return;
                    }
                    await Task.Delay(10).ConfigureAwait(false);
                }
                throw new TimeoutException($"手动运动超时:{card}-{axis}");
            }
            catch (Exception ex)
            {
                try
                {
                    motion.StopOneAxis(card, axis, 0);
                    engine.ReleaseManualMotionResource(card, axis);
                    setSecurityLock($"手动运动监控异常，轴已停止:{card}-{axis} {ex.Message}");
                }
                catch (Exception stopException)
                {
                    setSecurityLock($"手动运动监控失败且停止轴失败:{card}-{axis} {ex.Message}; {stopException.Message}");
                }
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

        private bool TryStopAfterCommandFailure(ushort card, ushort axis, string commandError)
        {
            try
            {
                motion.StopOneAxis(card, axis, 0);
                return true;
            }
            catch (Exception stopException)
            {
                setSecurityLock($"手动运动失败后停止轴失败:{card}-{axis} {commandError}; {stopException.Message}");
                return false;
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
    }
}
