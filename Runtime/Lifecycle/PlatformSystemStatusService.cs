using System;
// 模块：运行时 / 生命周期。
// 职责范围：协调平台安全、设备状态、系统状态和幂等关闭。
// 排查入口：“系统状态/复位状态”异常时从此处核对输入事实和变量写入，不要由 HMI 直接改系统变量。

using System.Collections.Generic;

namespace Automation
{
    /// <summary>
    /// 根据复位状态、流程快照和报警弹框维护系统状态变量。
    /// 服务属于运行时，不依赖平台编辑器是否已创建。
    /// </summary>
    internal sealed class PlatformSystemStatusService : IDisposable
    {
        private const string ResetStatusValueName = "复位状态";
        private const string SystemStatusValueName = "系统状态";
        private readonly PlatformRuntime runtime;
        private readonly object updateLock = new object();
        private bool started;
        private bool faulted;

        public PlatformSystemStatusService(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Start()
        {
            if (started)
            {
                return;
            }
            if (runtime.ProcessEngine == null || runtime.ProcessInteraction == null)
            {
                throw new InvalidOperationException("平台运行时尚未完成组合，无法启动系统状态服务。");
            }
            started = true;
            runtime.ProcessEngine.SnapshotChanged += HandleSnapshotChanged;
            runtime.Stores.Values.ValueChanged += HandleValueChanged;
            runtime.ProcessInteraction.PopupAlarmCountChanged += HandlePopupAlarmCountChanged;
            Update();
        }

        public void Dispose()
        {
            if (!started)
            {
                return;
            }
            started = false;
            runtime.ProcessEngine.SnapshotChanged -= HandleSnapshotChanged;
            runtime.Stores.Values.ValueChanged -= HandleValueChanged;
            runtime.ProcessInteraction.PopupAlarmCountChanged -= HandlePopupAlarmCountChanged;
        }

        private void HandleSnapshotChanged(EngineSnapshot snapshot)
        {
            Update();
        }

        private void HandleValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (string.Equals(e?.Name, ResetStatusValueName, StringComparison.Ordinal))
            {
                Update();
            }
        }

        private void HandlePopupAlarmCountChanged()
        {
            Update();
        }

        private void Update()
        {
            lock (updateLock)
            {
                if (!started || faulted)
                {
                    return;
                }
                if (!TryReadSystemValue(ResetStatusValueName, out _, out double resetRaw))
                {
                    return;
                }
                if (resetRaw != 0d && resetRaw != 1d && resetRaw != 2d)
                {
                    Fail($"变量“{ResetStatusValueName}”取值超出定义:{resetRaw}");
                    return;
                }
                if (!TryReadSystemValue(SystemStatusValueName, out _, out double currentRaw))
                {
                    return;
                }

                double target = (double)Calculate((ResetStatus)(int)resetRaw);
                if (currentRaw == target)
                {
                    return;
                }
                if (!runtime.Stores.Values.setValueByName(
                    SystemStatusValueName,
                    target,
                    "系统状态自动更新"))
                {
                    Fail($"写入变量“{SystemStatusValueName}”失败。");
                }
            }
        }

        private bool TryReadSystemValue(string name, out DicValue value, out double numericValue)
        {
            numericValue = 0d;
            if (!runtime.Stores.Values.TryGetValueByName(name, out value) || value == null)
            {
                Fail($"缺少变量：{name}");
                return false;
            }
            if (!string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                Fail($"变量“{name}”类型不是double。");
                return false;
            }
            if (!double.TryParse(value.Value, out numericValue))
            {
                Fail($"变量“{name}”数值无效:{value.Value}");
                return false;
            }
            return true;
        }

        private SystemStatus Calculate(ResetStatus resetStatus)
        {
            bool hasRunning = false;
            bool hasPaused = false;
            bool hasAlarm = false;
            IReadOnlyList<EngineSnapshot> snapshots = runtime.ProcessEngine.GetSnapshots();
            if (snapshots != null)
            {
                foreach (EngineSnapshot snapshot in snapshots)
                {
                    if (snapshot == null)
                    {
                        continue;
                    }
                    switch (snapshot.State)
                    {
                        case ProcRunState.Alarming:
                            hasAlarm = true;
                            break;
                        case ProcRunState.Paused:
                            hasPaused = true;
                            break;
                        case ProcRunState.Pausing:
                        case ProcRunState.Stopping:
                        case ProcRunState.Running:
                        case ProcRunState.SingleStep:
                            if (!IsSystemProcess(snapshot))
                            {
                                hasRunning = true;
                            }
                            break;
                    }
                }
            }
            if (runtime.ProcessInteraction.PopupAlarmCount > 0) return SystemStatus.PopupAlarm;
            if (hasAlarm) return SystemStatus.ProcAlarm;
            if (hasPaused) return SystemStatus.Paused;
            if (hasRunning) return SystemStatus.Working;
            return resetStatus == ResetStatus.ResetCompleted
                ? SystemStatus.Ready
                : SystemStatus.Uninitialized;
        }

        private bool IsSystemProcess(EngineSnapshot snapshot)
        {
            string processName = snapshot.ProcName;
            if (string.IsNullOrWhiteSpace(processName)
                && snapshot.ProcIndex >= 0
                && runtime.ProcessEngine.Context?.Procs != null
                && snapshot.ProcIndex < runtime.ProcessEngine.Context.Procs.Count)
            {
                processName = runtime.ProcessEngine.Context.Procs[snapshot.ProcIndex]?.head?.Name;
            }
            return !string.IsNullOrEmpty(processName)
                && processName.StartsWith("系统", StringComparison.Ordinal);
        }

        private void Fail(string message)
        {
            if (faulted)
            {
                return;
            }
            faulted = true;
            runtime.Safety.Lock(message);
        }
    }
}
