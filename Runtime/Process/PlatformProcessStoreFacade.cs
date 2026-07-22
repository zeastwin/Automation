using System;
// 模块：运行时 / 流程适配。
// 职责范围：向宿主和编辑器投影流程控制与流程、变量存储端口。

using System.Collections.Generic;
using System.Linq;
using Automation.DeviceSdk;

namespace Automation
{
    internal sealed class PlatformProcessStoreFacade : IProcessStore
    {
        private readonly AutomationPlatformHost platform;

        public PlatformProcessStoreFacade(AutomationPlatformHost platform)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            platform.ProcessSnapshotChanged += Platform_ProcessSnapshotChanged;
        }

        public event EventHandler<ProcessChangedEventArgs> Changed;

        public IReadOnlyList<ProcessSnapshot> GetAll()
        {
            return platform.GetProcesses().Select(Map).ToList();
        }

        public bool Start(int processIndex, out string error)
        {
            return platform.TryStartProcess(processIndex, out error);
        }

        public bool Pause(int processIndex, out string error)
        {
            return platform.TryPauseProcess(processIndex, out error);
        }

        public bool Resume(int processIndex, out string error)
        {
            return platform.TryResumeProcess(processIndex, out error);
        }

        public bool Stop(int processIndex, out string error)
        {
            return platform.TryStopProcess(processIndex, out error);
        }

        public bool StopAll(out string error)
        {
            return platform.TryStopAllProcesses(out error);
        }

        private void Platform_ProcessSnapshotChanged(EngineSnapshot snapshot)
        {
            Changed?.Invoke(this, new ProcessChangedEventArgs { Process = Map(snapshot) });
        }

        private static ProcessSnapshot Map(PlatformProcessInfo source)
        {
            return new ProcessSnapshot
            {
                Index = source.Index,
                Id = source.Id,
                Name = source.Name,
                State = MapState(source.State),
                StepIndex = -1,
                OperationIndex = -1,
                Disabled = source.Disabled,
                IsAlarm = source.IsAlarm,
                AlarmMessage = source.AlarmMessage,
                UpdatedAt = DateTime.MinValue
            };
        }

        private static ProcessSnapshot Map(EngineSnapshot source)
        {
            return new ProcessSnapshot
            {
                Index = source.ProcIndex,
                Id = source.ProcId,
                Name = source.ProcName,
                State = MapState(source.State),
                StepIndex = source.StepIndex,
                OperationIndex = source.OpIndex,
                Disabled = false,
                IsAlarm = source.IsAlarm,
                AlarmMessage = source.AlarmMessage,
                UpdatedAt = source.UpdateTime,
                PerformanceAnalysisEnabled = source.Performance?.Enabled == true,
                OperationCount = source.Performance?.OperationCount ?? 0,
                OperationsPerSecond = source.Performance?.OperationsPerSecond ?? 0,
                ThreadCpuPercent = source.Performance?.ThreadCpuPercent ?? 0,
                AverageOperationMicroseconds = source.Performance?.AverageOperationMicroseconds ?? 0,
                MaxOperationMicroseconds = source.Performance?.MaxOperationMicroseconds ?? 0,
                OperationDurationSampleCount = source.Performance?.OperationDurationSampleCount ?? 0,
                OperationDurationSamplingInterval = source.Performance?.OperationDurationSamplingInterval ?? 0,
                AbnormalCpuLoopDetected = source.Performance?.AbnormalCpuLoopDetected == true
            };
        }

        private static ProcessRuntimeStatus MapState(ProcRunState state)
        {
            return (ProcessRuntimeStatus)(int)state;
        }
    }
}
