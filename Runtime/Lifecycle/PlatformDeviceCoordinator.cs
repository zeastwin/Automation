using System;
// 模块：运行时 / 生命周期。
// 职责范围：协调平台安全、设备状态、系统状态和幂等关闭。
// 排查入口：设备初始化、监视循环和 Faulted 事件集中在此；设备异常不得通过 UI 定时器自行恢复。

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
    /// <summary>
    /// 运动设备初始化和轴状态监视的实例级生命周期入口。
    /// </summary>
    internal sealed class PlatformDeviceCoordinator : IDisposable
    {
        // 3.0 BreakCtrl 在放闸前最多等待 100ms 确认轴使能，系统 IO 每 100ms 采样一次。
        private const int ServoEnableConfirmTimeoutMilliseconds = 100;
        private const int MonitorIntervalMilliseconds = 10;
        private const int SystemIoPollIntervalCycles = 10;
        // 与平台关闭流程默认的进程停止窗口一致；超时后升级为急停再确认。
        private const int MotionStopConfirmTimeoutMilliseconds = 2000;
        private readonly PlatformRuntime runtime;
        private readonly object monitorLock = new object();
        private readonly object deviceReleaseLock = new object();
        private CancellationTokenSource monitorCts;
        private Task monitorTask;
        private IReadOnlyList<IO> emergencyInputs = Array.Empty<IO>();
        private IReadOnlyList<IO> brakeOutputs = Array.Empty<IO>();
        private int monitorThreadId;
        private bool deviceReleaseStarted;
        private bool disposed;

        public PlatformDeviceCoordinator(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public event Action<string> Faulted;

        public void Initialize()
        {
            ThrowIfDisposed();
            if (runtime.Readiness.MotionConfigFaulted)
            {
                string reason = string.IsNullOrWhiteSpace(runtime.Readiness.MotionConfigFaultReason)
                    ? "未提供详细原因。"
                    : runtime.Readiness.MotionConfigFaultReason;
                runtime.ProcessEngine?.Logger?.Log(
                    "运动配置故障，已跳过物理卡、自动上使能、机器人工站和放刹车初始化：" + reason,
                    LogLevel.Error);
                return;
            }
            if (!TryCaptureSystemIoConfiguration(out string systemIoError))
            {
                string message = $"系统急停/刹车IO配置读取失败:{systemIoError}";
                runtime.Safety.Lock(message);
                runtime.ProcessEngine?.Logger?.Log(message, LogLevel.Error);
                StopAndReleaseDevices(true);
                TryRaiseFaulted(message);
                return;
            }
            int configuredCardCount = runtime.Stores.Cards?.GetControlCardCount() ?? 0;
            bool cardInitialized = false;
            try
            {
                runtime.Motion.InitCardType();
                if (configuredCardCount == 0)
                {
                    runtime.ProcessEngine?.Logger?.Log(
                        "未配置雷赛总线卡，已跳过物理轴初始化；机器人工站仍按配置初始化。",
                        LogLevel.Normal);
                }
                else if (!runtime.Motion.InitCard())
                {
                    throw new InvalidOperationException(
                        "雷赛总线卡初始化返回失败，但没有提供可安全降级的未检测到板卡原因。");
                }
                else
                {
                    runtime.Motion.DownLoadConfig();
                    // InitCard 保留 3.0“卡初始化时自动上使能”的既定语义；此时机械抱闸仍未释放。
                    // IO 可读后立即同步确认急停，带急停启动只会进入安全停止，不会放闸。
                    if (!TryReadActiveEmergencyInput(out IO startupEmergency, out string startupIoError))
                    {
                        string message = $"启动前读取急停失败，禁止放刹车:{startupIoError}";
                        runtime.Safety.Lock(message);
                        runtime.ProcessEngine?.Logger?.Log(message, LogLevel.Error);
                        StopAndReleaseDevices(true);
                        TryRaiseFaulted(message);
                        return;
                    }
                    if (startupEmergency != null)
                    {
                        string message = $"启动前检测到急停输入，禁止放刹车:{startupEmergency.Name}";
                        runtime.Safety.Lock(message);
                        runtime.ProcessEngine?.Logger?.Log(message, LogLevel.Error);
                        StopAndReleaseDevices(true);
                        TryRaiseFaulted(message);
                        return;
                    }
                    // 3.0 的既定产品行为：程序启动后自动为所有轴上使能，操作人员可直接使用。
                    runtime.Motion.SetAllAxisSevonOn();
                    runtime.Motion.SetAllAxisEquiv();
                    cardInitialized = true;
                }
            }
            catch (MotionControl.MotionCardUnavailableException ex)
            {
                // 该异常只允许由驱动在尚未接触物理卡时抛出；编辑器、流程配置和机器人继续启动。
                runtime.ProcessEngine?.Logger?.Log(
                    $"雷赛总线卡不可用，已跳过物理轴；编辑、流程配置和机器人工站继续可用:{ex.Message}",
                    LogLevel.Error);
            }
            catch (Exception ex)
            {
                // 已发现板卡后的总线、下载、轴初始化异常无法证明设备无副作用，必须安全停机。
                string message = $"雷赛总线卡初始化异常，运动设备已进入安全停机:{ex.Message}";
                runtime.Safety.Lock(message);
                runtime.ProcessEngine?.Logger?.Log(message, LogLevel.Error);
                StopAndReleaseDevices(true);
                TryRaiseFaulted(message);
                return;
            }

            try
            {
                MotionControl.MotionStationResult stationResult = runtime.Motion.InitializeStations();
                if (stationResult != MotionControl.MotionStationResult.Success)
                {
                    runtime.ProcessEngine?.Logger?.Log(
                        $"一个或多个六轴工站初始化失败:{stationResult}",
                        LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                runtime.ProcessEngine?.Logger?.Log(
                    $"六轴工站初始化异常:{ex.Message}",
                    LogLevel.Error);
            }

            if (cardInitialized)
            {
                // 工站初始化（尤其机器人网络连接）可能耗时；放闸前必须再次同步确认急停，
                // 不能沿用上使能前的旧采样结果。
                if (!TryReadActiveEmergencyInput(out IO releaseEmergency, out string releaseIoError))
                {
                    string message = $"放刹车前读取急停失败，运动设备保持安全停机:{releaseIoError}";
                    runtime.Safety.Lock(message);
                    runtime.ProcessEngine?.Logger?.Log(message, LogLevel.Error);
                    StopAndReleaseDevices(true);
                    TryRaiseFaulted(message);
                    return;
                }
                if (releaseEmergency != null)
                {
                    string message = $"放刹车前检测到急停输入，运动设备保持安全停机:{releaseEmergency.Name}";
                    runtime.Safety.Lock(message);
                    runtime.ProcessEngine?.Logger?.Log(message, LogLevel.Error);
                    StopAndReleaseDevices(true);
                    TryRaiseFaulted(message);
                    return;
                }
                if (!TryReleaseBrakesAfterAxesEnabled(out string brakeError))
                {
                    string message = $"轴使能确认或放刹车失败，运动设备保持安全停机:{brakeError}";
                    runtime.Safety.Lock(message);
                    runtime.ProcessEngine?.Logger?.Log(message, LogLevel.Error);
                    StopAndReleaseDevices(true);
                    TryRaiseFaulted(message);
                    return;
                }
            }
            // 机器人工站不依赖物理运动卡；即使未配卡，也必须持续巡检工站故障。
            StartAxisMonitor();
        }

        public void StartAxisMonitor()
        {
            ThrowIfDisposed();
            lock (monitorLock)
            {
                StopAxisMonitorCore();
                ClearAxisRuntimeState();
                monitorCts = new CancellationTokenSource();
                CancellationToken token = monitorCts.Token;
                monitorTask = Task.Run(() => MonitorAxes(token), token);
            }
        }

        public void ClearAxisRuntimeState()
        {
            runtime.ProcessEngine?.Context?.AxisStatuses?.Clear();
            runtime.ProcessEngine?.Context?.AxisMotionParameters?.Clear();
        }

        public void Stop()
        {
            lock (monitorLock)
            {
                StopAxisMonitorCore();
            }
            StopAndReleaseDevices(false);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Stop();
        }

        private void MonitorAxes(CancellationToken token)
        {
            Volatile.Write(ref monitorThreadId, Thread.CurrentThread.ManagedThreadId);
            try
            {
                int pollCycle = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (pollCycle % SystemIoPollIntervalCycles == 0)
                        {
                            EnsureStationsNotFaulted();
                            // 急停配置挂在雷赛总线 IO 上。开发机未检测到卡时跳过采样，
                            // 避免后台监控把已降级的设备离线再次升级为平台故障。
                            IO emergencyInput = null;
                            if (runtime.Motion.IsCardInitialized
                                && !TryReadActiveEmergencyInput(out emergencyInput, out string ioError))
                            {
                                throw new InvalidOperationException(ioError);
                            }
                            if (runtime.Motion.IsCardInitialized && emergencyInput != null)
                            {
                                HandleMonitorFault($"检测到急停输入:{emergencyInput.Name}");
                                break;
                            }
                        }
                        if (runtime.Motion.IsCardInitialized
                            && !runtime.Readiness.MotionConfigRestartRequired
                            && !(runtime.Editor.ActiveSession?.Draft is CardHead))
                        {
                            int cardCount = runtime.Stores.Cards?.GetControlCardCount()
                                ?? throw new InvalidOperationException("运动卡配置未初始化");
                            for (int i = 0; i < cardCount; i++)
                            {
                                int axisCount = runtime.Stores.Cards.GetAxisCount(i);
                                for (int j = 0; j < axisCount; j++)
                                {
                                    ushort card = (ushort)i;
                                    ushort axis = (ushort)j;
                                    uint ioStatus = runtime.Motion.GetAxisIoStatus(card, axis);
                                    runtime.ProcessEngine.Context.AxisStatuses.UpdateIo(card, axis, ioStatus);
                                    if (pollCycle % SystemIoPollIntervalCycles == 0)
                                    {
                                        bool isStopped = runtime.Motion.GetInPos(card, axis);
                                        bool isHomed = runtime.Motion.HomeStatus(card, axis);
                                        bool servoOn = runtime.Motion.GetAxisSevon(card, axis);
                                        double position = runtime.Motion.GetAxisPos(card, axis);
                                        double speed = runtime.Motion.GetAxisCurSpeed(card, axis);
                                        ushort alarmCode = (ioStatus & 1u) == 0
                                            ? (ushort)0
                                            : runtime.Motion.GetAxisAlarmCode(card, axis);
                                        runtime.ProcessEngine.Context.AxisStatuses.UpdateDetails(
                                            card,
                                            axis,
                                            isStopped,
                                            isHomed,
                                            servoOn,
                                            position,
                                            speed,
                                            alarmCode);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        HandleMonitorFault($"轴IO监视线程异常:{ex.Message}");
                        break;
                    }
                    pollCycle = pollCycle == int.MaxValue ? 0 : pollCycle + 1;
                    if (token.WaitHandle.WaitOne(MonitorIntervalMilliseconds))
                    {
                        break;
                    }
                }
            }
            finally
            {
                Volatile.Write(ref monitorThreadId, 0);
            }
        }

        private void EnsureStationsNotFaulted()
        {
            if (runtime.Motion == null)
            {
                return;
            }
            int stationCount = runtime.Motion.StationCount;
            if (stationCount > short.MaxValue)
            {
                throw new InvalidOperationException($"六轴工站数量超出运行索引范围:{stationCount}");
            }
            for (short station = 0; station < stationCount; station++)
            {
                MotionControl.MotionStationStatus status = runtime.Motion.GetStationStatus(station);
                if (status?.State == MotionControl.MotionStationState.Faulted)
                {
                    string detail = string.IsNullOrWhiteSpace(status.LastError)
                        ? string.Empty
                        : $":{status.LastError}";
                    throw new InvalidOperationException($"{station}号六轴工站故障{detail}");
                }
                // Disconnected 是机器人后台重连的中间态，不升级为平台安全故障。
            }
        }

        private void HandleMonitorFault(string message)
        {
            try
            {
                runtime.Safety.Lock(message);
            }
            catch (Exception ex)
            {
                runtime.ProcessEngine?.Logger?.Log($"设备故障触发安全锁失败:{ex.Message}", LogLevel.Error);
            }
            StopAndReleaseDevices(true);
            ClearAxisRuntimeState();
            TryRaiseFaulted(message);
        }

        private bool TryCaptureSystemIoConfiguration(out string error)
        {
            error = null;
            var emergency = new List<IO>();
            var brakes = new List<IO>();
            try
            {
                List<List<IO>> snapshot = runtime.Stores.IoConfiguration?.CreateSnapshot();
                if (snapshot == null)
                {
                    error = "IO配置存储未初始化";
                    return false;
                }
                foreach (List<IO> cardItems in snapshot)
                {
                    if (cardItems == null)
                    {
                        error = "IO配置包含空卡列表";
                        return false;
                    }
                    foreach (IO io in cardItems)
                    {
                        if (io == null)
                        {
                            continue;
                        }
                        if (string.Equals(io.UsedType, "急停", StringComparison.Ordinal)
                            && string.Equals(io.IOType, "通用输入", StringComparison.Ordinal))
                        {
                            emergency.Add(io);
                        }
                        else if (string.Equals(io.UsedType, "刹车", StringComparison.Ordinal)
                            && string.Equals(io.IOType, "通用输出", StringComparison.Ordinal))
                        {
                            brakes.Add(io);
                        }
                    }
                }
                emergencyInputs = emergency;
                brakeOutputs = brakes;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private bool TryReleaseBrakesAfterAxesEnabled(out string error)
        {
            error = null;
            if (brakeOutputs.Count == 0)
            {
                return true;
            }
            if (!TryWaitForAllAxesEnabled(
                ServoEnableConfirmTimeoutMilliseconds,
                out string enableError))
            {
                error = enableError;
                return false;
            }
            return TryWriteBrakeOutputs(true, out error);
        }

        private bool TryWaitForAllAxesEnabled(int timeoutMilliseconds, out string error)
        {
            error = null;
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                var disabledAxes = new List<string>();
                try
                {
                    int cardCount = runtime.Stores.Cards.GetControlCardCount();
                    for (int cardIndex = 0; cardIndex < cardCount; cardIndex++)
                    {
                        int axisCount = runtime.Stores.Cards.GetAxisCount(cardIndex);
                        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
                        {
                            if (!runtime.Motion.GetAxisSevon((ushort)cardIndex, (ushort)axisIndex))
                            {
                                disabledAxes.Add($"{cardIndex}-{axisIndex}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = $"读取轴使能状态失败:{ex.Message}";
                    return false;
                }
                if (disabledAxes.Count == 0)
                {
                    return true;
                }
                if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    error = $"以下轴未确认上使能:{string.Join(",", disabledAxes)}";
                    return false;
                }
                Thread.Sleep(MonitorIntervalMilliseconds);
            }
        }

        private bool TryReadActiveEmergencyInput(out IO activeInput, out string error)
        {
            activeInput = null;
            error = null;
            if (emergencyInputs.Count == 0)
            {
                return true;
            }
            if (runtime.Io == null)
            {
                error = "IO运行时未初始化";
                return false;
            }
            foreach (IO input in emergencyInputs)
            {
                bool active = false;
                try
                {
                    // GetInIO 已按 EffectLevel 映射为逻辑值；这里不再取反。
                    if (!runtime.Io.GetInIO(input, ref active))
                    {
                        error = $"读取急停输入失败:{input.Name}";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = $"读取急停输入异常:{input.Name} {ex.Message}";
                    return false;
                }
                if (active)
                {
                    activeInput = input;
                    return true;
                }
            }
            return true;
        }

        private bool TryWriteBrakeOutputs(bool released, out string error)
        {
            error = null;
            if (brakeOutputs.Count == 0)
            {
                return true;
            }
            if (runtime.Io == null)
            {
                error = "IO运行时未初始化";
                return false;
            }
            var failures = new List<string>();
            foreach (IO output in brakeOutputs)
            {
                try
                {
                    // 3.0 BreakCtrl：逻辑 true=开刹车/放闸，false=关刹车/抱闸。
                    if (!runtime.Io.SetIO(output, released))
                    {
                        failures.Add(output.Name);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{output.Name}({ex.Message})");
                }
            }
            if (failures.Count == 0)
            {
                return true;
            }
            if (released)
            {
                // 多路刹车只允许全成功；部分放闸时立即按逻辑 false 补偿为抱闸。
                foreach (IO output in brakeOutputs)
                {
                    try
                    {
                        runtime.Io.SetIO(output, false);
                    }
                    catch
                    {
                    }
                }
            }
            error = $"{(released ? "放" : "抱")}刹车失败:{string.Join(",", failures)}";
            return false;
        }

        private void StopAndReleaseDevices(bool emergency)
        {
            lock (deviceReleaseLock)
            {
                if (deviceReleaseStarted)
                {
                    return;
                }
                deviceReleaseStarted = true;

                bool stopCommandSucceeded = TryStopAllMotion(emergency, out string stopError);
                bool stopped = TryWaitForMotionStopped(
                    MotionStopConfirmTimeoutMilliseconds,
                    out string waitError);
                if ((!stopCommandSucceeded || !stopped) && !emergency)
                {
                    runtime.ProcessEngine?.Logger?.Log(
                        $"正常停止未完成，升级为急停:{stopError ?? waitError}",
                        LogLevel.Error);
                    stopCommandSucceeded = TryStopAllMotion(true, out stopError);
                    stopped = TryWaitForMotionStopped(
                        MotionStopConfirmTimeoutMilliseconds,
                        out waitError);
                }
                if (!stopCommandSucceeded || !stopped)
                {
                    string unsafeMessage = $"运动设备释放前无法确认全部停稳:{stopError ?? waitError}";
                    TryLockCleanupFailure(unsafeMessage);
                    runtime.ProcessEngine?.Logger?.Log(unsafeMessage, LogLevel.Error);
                }

                // 卡从未初始化时无法也无需访问总线刹车输出；驱动关闭和工站释放仍保持幂等。
                if (runtime.Motion?.IsCardInitialized == true
                    && !TryWriteBrakeOutputs(false, out string brakeError))
                {
                    string message = $"运动设备释放前抱刹车失败:{brakeError}";
                    TryLockCleanupFailure(message);
                    runtime.ProcessEngine?.Logger?.Log(message, LogLevel.Error);
                }
                try
                {
                    MotionControl.MotionStationResult releaseResult = runtime.Motion?.ReleaseStations()
                        ?? MotionControl.MotionStationResult.Success;
                    if (!IsIdempotentCleanupResult(releaseResult))
                    {
                        runtime.ProcessEngine?.Logger?.Log(
                            $"释放六轴工站失败:{releaseResult}",
                            LogLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    runtime.ProcessEngine?.Logger?.Log($"释放六轴工站失败:{ex.Message}", LogLevel.Error);
                }
                try
                {
                    runtime.Motion?.StopConnect();
                }
                catch (Exception ex)
                {
                    runtime.ProcessEngine?.Logger?.Log($"停止运动控制失败:{ex.Message}", LogLevel.Error);
                }
            }
        }

        private bool TryStopAllMotion(bool emergency, out string error)
        {
            error = null;
            if (runtime.Motion == null)
            {
                return true;
            }
            var failures = new List<string>();
            try
            {
                MotionControl.MotionStationResult result = runtime.Motion.StopAllStations(emergency);
                if (!IsIdempotentCleanupResult(result))
                {
                    failures.Add($"停止六轴工站返回:{result}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"停止六轴工站异常:{ex.Message}");
            }

            // 物理轴允许在卡调试页直接手动控制，不一定绑定到某个工站。
            // 安全停止不能只遍历工站，否则未绑定轴可能在抱闸、关卡前仍在运动。
            if (runtime.Motion.IsCardInitialized)
            {
                try
                {
                    int cardCount = runtime.Stores.Cards.GetControlCardCount();
                    for (int cardIndex = 0; cardIndex < cardCount; cardIndex++)
                    {
                        int axisCount = runtime.Stores.Cards.GetAxisCount(cardIndex);
                        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
                        {
                            try
                            {
                                runtime.Motion.StopOneAxis(
                                    (ushort)cardIndex,
                                    (ushort)axisIndex,
                                    emergency ? (ushort)1 : (ushort)0);
                            }
                            catch (Exception ex)
                            {
                                failures.Add($"停止物理轴{cardIndex}-{axisIndex}异常:{ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"读取物理轴配置失败:{ex.Message}");
                }
            }

            error = failures.Count == 0 ? null : string.Join("；", failures);
            return failures.Count == 0;
        }

        private bool TryWaitForMotionStopped(int timeoutMilliseconds, out string error)
        {
            error = null;
            if (runtime.Motion == null)
            {
                return true;
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                int stationCount = runtime.Motion.StationCount;
                if (stationCount > short.MaxValue)
                {
                    error = $"六轴工站数量超出运行索引范围:{stationCount}";
                    return false;
                }
                for (short station = 0; station < stationCount; station++)
                {
                    int remaining = Math.Max(
                        1,
                        timeoutMilliseconds - (int)Math.Min(
                            timeoutMilliseconds,
                            stopwatch.ElapsedMilliseconds));
                    MotionControl.MotionStationResult result = runtime.Motion.WaitStationMotion(
                        station,
                        false,
                        -1,
                        remaining);
                    if (!IsIdempotentCleanupResult(result))
                    {
                        error = $"等待{station}号六轴工站停稳失败:{result}";
                        return false;
                    }
                }

                if (!runtime.Motion.IsCardInitialized)
                {
                    return true;
                }

                while (true)
                {
                    var movingAxes = new List<string>();
                    int cardCount = runtime.Stores.Cards.GetControlCardCount();
                    for (int cardIndex = 0; cardIndex < cardCount; cardIndex++)
                    {
                        int axisCount = runtime.Stores.Cards.GetAxisCount(cardIndex);
                        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
                        {
                            if (!runtime.Motion.GetInPos((ushort)cardIndex, (ushort)axisIndex))
                            {
                                movingAxes.Add($"{cardIndex}-{axisIndex}");
                            }
                        }
                    }
                    if (movingAxes.Count == 0)
                    {
                        return true;
                    }
                    if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
                    {
                        error = $"以下轴未确认停稳:{string.Join(",", movingAxes)}";
                        return false;
                    }
                    Thread.Sleep(MonitorIntervalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                error = $"确认运动停止异常:{ex.Message}";
                return false;
            }
        }

        private void TryLockCleanupFailure(string message)
        {
            try
            {
                // 清理是故障后的补偿阶段。已有安全锁原因必须保留，不能被次生停止/释放错误覆盖。
                if (!runtime.Safety.IsLocked)
                {
                    runtime.Safety.Lock(message);
                }
            }
            catch
            {
            }
        }

        private static bool IsIdempotentCleanupResult(
            MotionControl.MotionStationResult result)
        {
            return result == MotionControl.MotionStationResult.Success
                || result == MotionControl.MotionStationResult.NotInitialized
                || result == MotionControl.MotionStationResult.NotConnected;
        }

        private void TryRaiseFaulted(string message)
        {
            try
            {
                Faulted?.Invoke(message);
            }
            catch (Exception ex)
            {
                runtime.ProcessEngine?.Logger?.Log(
                    $"设备故障通知异常:{ex.Message}",
                    LogLevel.Error);
            }
        }

        private void StopAxisMonitorCore()
        {
            CancellationTokenSource cancellation = monitorCts;
            Task task = monitorTask;
            monitorCts = null;
            monitorTask = null;
            cancellation?.Cancel();
            bool calledFromMonitor = task != null
                && (Task.CurrentId == task.Id
                    || Thread.CurrentThread.ManagedThreadId == Volatile.Read(ref monitorThreadId));
            if (task != null && !calledFromMonitor)
            {
                try
                {
                    task.Wait(1000);
                }
                catch (Exception ex)
                {
                    runtime.ProcessEngine?.Logger?.Log(
                        $"等待轴IO监视线程退出失败:{ex.Message}",
                        LogLevel.Error);
                }
            }
            cancellation?.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PlatformDeviceCoordinator));
            }
        }
    }
}
