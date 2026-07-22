using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
    /// <summary>
    /// 单个平台实例唯一的运行时关闭链。编辑器和设备宿主都调用此对象，
    /// 从而保证安全停止、配置保存和资源释放只执行一次且顺序一致。
    /// </summary>
    internal sealed class PlatformShutdownCoordinator
    {
        private readonly PlatformRuntime runtime;
        private readonly object syncRoot = new object();
        private PlatformShutdownReport completedReport;
        private int shutdownState;

        public PlatformShutdownCoordinator(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool IsShutdownStarted => Volatile.Read(ref shutdownState) != 0;

        public PlatformShutdownReport Shutdown(
            TimeSpan? processStopTimeout = null,
            TimeSpan? communicationStopTimeout = null)
        {
            if (Interlocked.CompareExchange(ref shutdownState, 1, 0) != 0)
            {
                lock (syncRoot)
                {
                    return completedReport ?? PlatformShutdownReport.AlreadyInProgress();
                }
            }

            var stages = new List<PlatformShutdownStageResult>();
            TimeSpan processTimeout = processStopTimeout ?? TimeSpan.FromSeconds(2);
            TimeSpan communicationTimeout = communicationStopTimeout ?? TimeSpan.FromSeconds(3);

            RunStage(stages, "停止全部流程", () =>
                runtime.Safety.StopAllProcesses("系统关闭，停止所有流程。"));
            RunStage(stages, "停止设备动作", () => runtime.Devices?.Stop());
            RunStage(stages, "等待流程停止", () => WaitForProcessesStopped(processTimeout));
            RunStage(stages, "关闭流程交互", () => runtime.ProcessInteraction?.CloseAll());
            RunStage(stages, "保存运行配置", SaveRuntimeConfiguration);
            RunStage(stages, "释放系统状态", () =>
            {
                runtime.SystemStatus?.Dispose();
                runtime.SystemStatus = null;
            });
            RunStage(stages, "释放PLC运行时", () => runtime.PlcRuntime?.Dispose());
            RunStage(stages, "释放通讯运行时", () => DisposeCommunication(communicationTimeout));
            RunStage(stages, "释放设备运行时", () => runtime.Devices?.Dispose());
            RunStage(stages, "释放流程引擎", () => runtime.ProcessEngine?.Dispose());
            RunStage(stages, "释放流程交互器", () =>
            {
                runtime.ProcessInteraction?.Dispose();
                runtime.ProcessInteraction = null;
            });

            var report = new PlatformShutdownReport(stages);
            lock (syncRoot)
            {
                completedReport = report;
                Volatile.Write(ref shutdownState, 2);
            }
            return report;
        }

        private void WaitForProcessesStopped(TimeSpan timeout)
        {
            ProcessEngine engine = runtime.ProcessEngine;
            if (engine == null) return;
            int processCount = engine.Context?.Procs?.Count
                ?? runtime.Stores.Processes.Items.Count;
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                bool allStopped = true;
                for (int index = 0; index < processCount; index++)
                {
                    EngineSnapshot snapshot = engine.GetSnapshot(index);
                    if (snapshot != null && snapshot.State != ProcRunState.Stopped)
                    {
                        allStopped = false;
                        break;
                    }
                }
                if (allStopped) return;
                Thread.Sleep(20);
            }
            throw new TimeoutException($"等待流程停止超过{timeout.TotalMilliseconds:0}毫秒。受影响资源继续按关闭顺序释放。");
        }

        private void SaveRuntimeConfiguration()
        {
            runtime.Stores.Values?.Save(runtime.Paths.ConfigPath);
            runtime.Stores.DataStructures?.Save(runtime.Paths.ConfigPath);
            runtime.Stores.Alarms?.Save(runtime.Paths.ConfigPath);
        }

        private void DisposeCommunication(TimeSpan timeout)
        {
            if (runtime.Communication == null) return;
            Task disposeTask = Task.Run(() => runtime.Communication.Dispose());
            if (!disposeTask.Wait(timeout))
            {
                throw new TimeoutException($"关闭通讯超过{timeout.TotalMilliseconds:0}毫秒。后台释放仍在继续。 ");
            }
            if (disposeTask.IsFaulted)
            {
                throw disposeTask.Exception?.GetBaseException()
                    ?? new InvalidOperationException("通讯运行时释放失败。");
            }
        }

        private void RunStage(
            ICollection<PlatformShutdownStageResult> results,
            string name,
            Action action)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                action();
                results.Add(new PlatformShutdownStageResult(name, stopwatch.Elapsed, null));
            }
            catch (Exception ex)
            {
                results.Add(new PlatformShutdownStageResult(name, stopwatch.Elapsed, ex.Message));
                runtime.ProcessEngine?.Logger?.Log($"关闭阶段[{name}]失败:{ex.Message}", LogLevel.Error);
            }
        }
    }

    internal sealed class PlatformShutdownReport
    {
        public PlatformShutdownReport(IReadOnlyList<PlatformShutdownStageResult> stages)
        {
            Stages = stages ?? Array.Empty<PlatformShutdownStageResult>();
        }

        public IReadOnlyList<PlatformShutdownStageResult> Stages { get; }
        public bool Succeeded
        {
            get
            {
                foreach (PlatformShutdownStageResult stage in Stages)
                {
                    if (!string.IsNullOrEmpty(stage.Error)) return false;
                }
                return true;
            }
        }

        public static PlatformShutdownReport AlreadyInProgress()
        {
            return new PlatformShutdownReport(new[]
            {
                new PlatformShutdownStageResult("关闭进行中", TimeSpan.Zero, "另一调用正在执行关闭链。")
            });
        }
    }

    internal sealed class PlatformShutdownStageResult
    {
        public PlatformShutdownStageResult(string name, TimeSpan elapsed, string error)
        {
            Name = name;
            Elapsed = elapsed;
            Error = error;
        }

        public string Name { get; }
        public TimeSpan Elapsed { get; }
        public string Error { get; }
    }
}
