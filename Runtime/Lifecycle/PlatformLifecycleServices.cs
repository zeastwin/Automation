using System;
// 模块：运行时 / 生命周期。
// 职责范围：协调平台安全、设备状态、系统状态和幂等关闭。

using System.Threading;

namespace Automation
{
    public sealed class PlatformReadinessState
    {
        public bool ProcConfigFaulted { get; set; }

        public bool VersionRestartRequired { get; set; }

        public bool MotionConfigRestartRequired { get; set; }
    }

    /// <summary>
    /// 配置维护互斥状态。Lease 释放前，其他维护请求保持失败。
    /// </summary>
    public sealed class ConfigurationMaintenanceService
    {
        private readonly object syncRoot = new object();
        private bool active;
        private string reason = string.Empty;

        public bool Active
        {
            get
            {
                lock (syncRoot)
                {
                    return active;
                }
            }
        }

        public string Reason
        {
            get
            {
                lock (syncRoot)
                {
                    return reason;
                }
            }
        }

        public bool TryBegin(string requestedReason, out IDisposable lease, out string error)
        {
            lock (syncRoot)
            {
                if (active)
                {
                    lease = null;
                    error = string.IsNullOrWhiteSpace(reason)
                        ? "系统正在执行配置维护。"
                        : $"系统正在执行配置维护:{reason}";
                    return false;
                }
                reason = string.IsNullOrWhiteSpace(requestedReason)
                    ? "配置维护"
                    : requestedReason.Trim();
                active = true;
                lease = new MaintenanceLease(this);
                error = null;
                return true;
            }
        }

        private void End()
        {
            lock (syncRoot)
            {
                active = false;
                reason = string.Empty;
            }
        }

        private sealed class MaintenanceLease : IDisposable
        {
            private ConfigurationMaintenanceService owner;

            public MaintenanceLease(ConfigurationMaintenanceService owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                ConfigurationMaintenanceService current = Interlocked.Exchange(ref owner, null);
                current?.End();
            }
        }
    }

    /// <summary>
    /// 平台安全锁及安全停机的唯一协调入口。
    /// </summary>
    public sealed class PlatformSafetyCoordinator
    {
        private readonly PlatformRuntime runtime;
        private volatile bool locked;
        private string lockReason = string.Empty;

        internal PlatformSafetyCoordinator(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool IsLocked => locked;

        public string LockReason => lockReason;

        public void Lock(string reason)
        {
            locked = true;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                lockReason = reason;
            }
            StopAllProcesses(reason);
        }

        public void StopAllProcesses(string reason)
        {
            ProcessEngine engine = runtime.ProcessEngine;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                if (engine?.Logger != null)
                {
                    engine.Logger.Log(reason, LogLevel.Error);
                }
                else
                {
                    runtime.EditorUi?.WriteInfo(reason, LogLevel.Error);
                }
            }
            engine?.StopAllManualMotion();
            if (engine == null)
            {
                return;
            }
            int count = engine.Context?.Procs?.Count
                ?? runtime.Stores.Processes.Items.Count;
            for (int i = 0; i < count; i++)
            {
                engine.Stop(i);
            }
        }
    }
}
