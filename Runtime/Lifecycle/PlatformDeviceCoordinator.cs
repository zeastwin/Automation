using System;
// 模块：运行时 / 生命周期。
// 职责范围：协调平台安全、设备状态、系统状态和幂等关闭。
// 排查入口：设备初始化、监视循环和 Faulted 事件集中在此；设备异常不得通过 UI 定时器自行恢复。

using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
    /// <summary>
    /// 运动设备初始化和轴状态监视的实例级生命周期入口。
    /// </summary>
    internal sealed class PlatformDeviceCoordinator : IDisposable
    {
        private readonly PlatformRuntime runtime;
        private readonly object monitorLock = new object();
        private CancellationTokenSource monitorCts;
        private Task monitorTask;
        private bool disposed;

        public PlatformDeviceCoordinator(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public event Action<string> Faulted;

        public void Initialize()
        {
            ThrowIfDisposed();
            int configuredCardCount = runtime.Stores.Cards?.GetControlCardCount() ?? 0;
            if (configuredCardCount == 0)
            {
                runtime.ProcessEngine?.Logger?.Log(
                    "未配置运动控制卡，已跳过运动控制卡初始化。",
                    LogLevel.Normal);
                return;
            }
            try
            {
                runtime.Motion.InitCardType();
                if (!runtime.Motion.InitCard())
                {
                    runtime.ProcessEngine?.Logger?.Log(
                        "运动控制卡初始化失败，运动操作已禁用。",
                        LogLevel.Error);
                    return;
                }
                runtime.Motion.DownLoadConfig();
                runtime.Motion.SetAllAxisSevonOn();
                runtime.Motion.SetAllAxisEquiv();
                StartAxisMonitor();
            }
            catch (Exception ex)
            {
                runtime.ProcessEngine?.Logger?.Log(
                    $"运动控制卡初始化异常，运动操作已禁用:{ex.Message}",
                    LogLevel.Error);
            }
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
            try
            {
                runtime.Motion?.StopConnect();
            }
            catch (Exception ex)
            {
                runtime.ProcessEngine?.Logger?.Log($"停止运动控制失败:{ex.Message}", LogLevel.Error);
            }
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
            int pollCycle = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!runtime.Readiness.MotionConfigRestartRequired
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
                                if (pollCycle % 10 == 0)
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
                    string message = $"轴IO监视线程异常:{ex.Message}";
                    runtime.Safety.Lock(message);
                    TryStopMotion();
                    ClearAxisRuntimeState();
                    Faulted?.Invoke(message);
                    break;
                }
                pollCycle = pollCycle == int.MaxValue ? 0 : pollCycle + 1;
                if (token.WaitHandle.WaitOne(10))
                {
                    break;
                }
            }
        }

        private void TryStopMotion()
        {
            try
            {
                runtime.Motion?.StopConnect();
            }
            catch (Exception ex)
            {
                runtime.ProcessEngine?.Logger?.Log($"停止运动控制失败:{ex.Message}", LogLevel.Error);
            }
        }

        private void StopAxisMonitorCore()
        {
            CancellationTokenSource cancellation = monitorCts;
            Task task = monitorTask;
            monitorCts = null;
            monitorTask = null;
            cancellation?.Cancel();
            if (task != null)
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
