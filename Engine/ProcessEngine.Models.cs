using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Automation.MotionControl;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Numerics;

namespace Automation
{
    public class ProcHandle
    {
        private int state = (int)ProcRunState.Stopped;
        private int breakpointFlag;
        private int gotoFlag;
        private int pauseBySignalFlag;
        private int completionRequestedFlag;
        private string alarmMessage;
        private long appliedRevision;
        private int cooperativeOperationCount;
        private long cooperativeSliceStartTimestamp;
        private int stepNumber;
        private int operationNumber;
        private int cachedSourceStep = int.MinValue;
        private int cachedSourceOperation = int.MinValue;
        private string cachedOperationSource;
        private long lastProgressSnapshotTimestamp;

        public int procNum;
        public int stepNum
        {
            get => Volatile.Read(ref stepNumber);
            set => Volatile.Write(ref stepNumber, value);
        }
        public int opsNum
        {
            get => Volatile.Read(ref operationNumber);
            set => Volatile.Write(ref operationNumber, value);
        }

        public string procName;
        public Guid procId;

        // 工作线程和命令线程共享的唯一逻辑状态；对外读取统一使用 EngineSnapshot。
        public ProcRunState State
        {
            get => (ProcRunState)Volatile.Read(ref state);
            internal set
            {
                if (value < ProcRunState.Stopped || value > ProcRunState.Stopping)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                Volatile.Write(ref state, (int)value);
            }
        }
        public bool isBreakpoint
        {
            get => Volatile.Read(ref breakpointFlag) == 1;
            internal set => Volatile.Write(ref breakpointFlag, value ? 1 : 0);
        }
        public bool isGoto
        {
            get => Volatile.Read(ref gotoFlag) == 1;
            internal set => Volatile.Write(ref gotoFlag, value ? 1 : 0);
        }
        // 报警文本是报警结果的唯一数据源，避免布尔标志与文本互相矛盾。
        public bool HasAlarm => !string.IsNullOrWhiteSpace(alarmMsg);
        public string alarmMsg
        {
            get => Volatile.Read(ref alarmMessage);
            internal set => Volatile.Write(ref alarmMessage, value);
        }
        public bool IsSingleOperation { get; internal set; }
        // 取消状态直接来自 ProcessControl，不在句柄中重复保存令牌。
        public CancellationToken CancellationToken => Control?.CancellationToken ?? System.Threading.CancellationToken.None;
        internal ProcessControl Control { get; set; }
        public ConcurrentBag<Task> RunningTasks { get; } = new ConcurrentBag<Task>();
        internal ConcurrentDictionary<long, byte> OwnedAxes { get; } = new ConcurrentDictionary<long, byte>();
        internal ConcurrentDictionary<long, byte> OwnedCoordinateSystems { get; } = new ConcurrentDictionary<long, byte>();
        public bool PauseBySignal
        {
            get => Volatile.Read(ref pauseBySignalFlag) == 1;
            internal set => Volatile.Write(ref pauseBySignalFlag, value ? 1 : 0);
        }
        public Proc Proc { get; set; }
        public long AppliedRevision
        {
            get => Interlocked.Read(ref appliedRevision);
            internal set => Interlocked.Exchange(ref appliedRevision, value);
        }
        public bool CompletionRequested
        {
            get => Volatile.Read(ref completionRequestedFlag) == 1;
            internal set => Volatile.Write(ref completionRequestedFlag, value ? 1 : 0);
        }

        public ProcTerminationReason TerminationReason { get; internal set; }

        internal int CooperativeOperationCount
        {
            get => cooperativeOperationCount;
            set => cooperativeOperationCount = value;
        }

        internal long CooperativeSliceStartTimestamp
        {
            get => cooperativeSliceStartTimestamp;
            set => cooperativeSliceStartTimestamp = value;
        }

        internal ProcessPerformanceState Performance { get; set; }

        internal ProcessRunMetrics RunMetrics { get; set; }

        internal long LastProgressSnapshotTimestamp
        {
            get => Interlocked.Read(ref lastProgressSnapshotTimestamp);
            set => Interlocked.Exchange(ref lastProgressSnapshotTimestamp, value);
        }

        internal HighResolutionWaiter Waiter { get; set; }

        internal string GetOperationSource()
        {
            int currentStep = stepNum;
            int currentOperation = opsNum;
            if (cachedOperationSource == null || cachedSourceStep != currentStep
                || cachedSourceOperation != currentOperation)
            {
                cachedSourceStep = currentStep;
                cachedSourceOperation = currentOperation;
                cachedOperationSource = $"{procNum}-{currentStep}-{currentOperation}";
            }
            return cachedOperationSource;
        }
    }

    internal sealed class ProcessRunMetrics
    {
        public const int DurationSamplingInterval = 128;
        private long operationCount;
        private long failedCount;
        private long durationSampleCount;
        private long totalSampledOperationTicks;
        private long maxSampledOperationTicks;
        private int durationSequence;

        public bool ShouldMeasureDuration()
        {
            return (durationSequence++ & (DurationSamplingInterval - 1)) == 0;
        }

        public void RecordOperation(long elapsedTicks, bool durationMeasured, bool failed)
        {
            operationCount++;
            if (failed)
            {
                failedCount++;
            }
            if (!durationMeasured)
            {
                return;
            }
            long normalizedTicks = Math.Max(0L, elapsedTicks);
            durationSampleCount++;
            totalSampledOperationTicks += normalizedTicks;
            if (normalizedTicks > maxSampledOperationTicks)
            {
                maxSampledOperationTicks = normalizedTicks;
            }
        }

        public ProcessRunAuditSnapshot CreateSnapshot(int procIndex, Guid procId)
        {
            return new ProcessRunAuditSnapshot(
                procIndex,
                procId,
                operationCount,
                failedCount,
                durationSampleCount,
                totalSampledOperationTicks,
                maxSampledOperationTicks,
                DurationSamplingInterval);
        }
    }

    internal readonly struct ProcessRunAuditSnapshot
    {
        public ProcessRunAuditSnapshot(int procIndex, Guid procId, long operationCount, long failedCount,
            long durationSampleCount, long totalSampledOperationTicks, long maxSampledOperationTicks,
            int durationSamplingInterval)
        {
            ProcIndex = procIndex;
            ProcId = procId;
            OperationCount = operationCount;
            FailedCount = failedCount;
            DurationSampleCount = durationSampleCount;
            TotalSampledOperationTicks = totalSampledOperationTicks;
            MaxSampledOperationTicks = maxSampledOperationTicks;
            DurationSamplingInterval = durationSamplingInterval;
        }

        public int ProcIndex { get; }
        public Guid ProcId { get; }
        public long OperationCount { get; }
        public long FailedCount { get; }
        public long DurationSampleCount { get; }
        public long TotalSampledOperationTicks { get; }
        public long MaxSampledOperationTicks { get; }
        public int DurationSamplingInterval { get; }
    }

    internal readonly struct OperationFailureEntry
    {
        public OperationFailureEntry(int procIndex, Guid procId, int stepIndex, int opIndex,
            Guid operationId, string operationType, string operationName, string alarmMessage,
            long elapsedTicks, bool durationMeasured)
        {
            ProcIndex = procIndex;
            ProcId = procId;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            OperationId = operationId;
            OperationType = operationType;
            OperationName = operationName;
            AlarmMessage = alarmMessage;
            ElapsedTicks = elapsedTicks;
            DurationMeasured = durationMeasured;
        }

        public int ProcIndex { get; }
        public Guid ProcId { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public Guid OperationId { get; }
        public string OperationType { get; }
        public string OperationName { get; }
        public string AlarmMessage { get; }
        public long ElapsedTicks { get; }
        public bool DurationMeasured { get; }
    }
    public enum EngineCommandType
    {
        StartAt = 1,
        RunSingleOpOnce = 2,
        Pause = 3,
        Resume = 4,
        Step = 5,
        Stop = 6
    }

    public sealed class EngineCommand
    {
        private EngineCommand(EngineCommandType type, int procIndex, Proc proc, int stepIndex, int opIndex,
            ProcRunState startState)
        {
            Type = type;
            ProcIndex = procIndex;
            Proc = proc;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            StartState = startState;
        }

        public EngineCommandType Type { get; }
        public int ProcIndex { get; }
        public Proc Proc { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public ProcRunState StartState { get; }
        public Guid DataBreakpointHitId { get; private set; }
        internal long Generation { get; set; }
        internal TaskCompletionSource<bool> Completion { get; } =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public static EngineCommand Start(int procIndex, Proc proc, int stepIndex, int opIndex, ProcRunState startState)
        {
            return new EngineCommand(EngineCommandType.StartAt, procIndex, proc, stepIndex, opIndex, startState);
        }

        public static EngineCommand RunSingleOpOnce(int procIndex, Proc proc, int stepIndex, int opIndex)
        {
            return new EngineCommand(EngineCommandType.RunSingleOpOnce, procIndex, proc, stepIndex, opIndex,
                ProcRunState.SingleStep);
        }

        public static EngineCommand Pause(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Pause, procIndex, null, 0, 0, ProcRunState.Paused);
        }

        internal static EngineCommand PauseForDataBreakpoint(int procIndex, Guid hitId)
        {
            EngineCommand command = Pause(procIndex);
            command.DataBreakpointHitId = hitId;
            return command;
        }

        public static EngineCommand Resume(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Resume, procIndex, null, 0, 0, ProcRunState.Running);
        }

        public static EngineCommand Step(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Step, procIndex, null, 0, 0, ProcRunState.SingleStep);
        }

        public static EngineCommand Stop(int procIndex)
        {
            return new EngineCommand(EngineCommandType.Stop, procIndex, null, 0, 0, ProcRunState.Stopped);
        }
    }
    public sealed class EngineSnapshot
    {
        // 不可变只读模型，供 UI、HMI、Bridge 和其他线程安全观察流程状态。
        public EngineSnapshot(int procIndex, Guid procId, string procName, ProcRunState state, int stepIndex, int opIndex,
            bool isBreakpoint, string alarmMessage, DateTime updateTime, long updateTicks,
            long publishedRevision = 0, long appliedRevision = 0)
            : this(procIndex, procId, procName, state, stepIndex, opIndex, isBreakpoint, alarmMessage,
                updateTime, updateTicks, publishedRevision, appliedRevision, ProcTerminationReason.None)
        {
        }

        public EngineSnapshot(int procIndex, Guid procId, string procName, ProcRunState state, int stepIndex, int opIndex,
            bool isBreakpoint, string alarmMessage, DateTime updateTime, long updateTicks,
            long publishedRevision, long appliedRevision, ProcTerminationReason terminationReason)
        {
            ProcIndex = procIndex;
            ProcId = procId;
            ProcName = procName;
            State = state;
            StepIndex = stepIndex;
            OpIndex = opIndex;
            IsBreakpoint = isBreakpoint;
            AlarmMessage = alarmMessage;
            UpdateTime = updateTime;
            UpdateTicks = updateTicks;
            PublishedRevision = publishedRevision;
            AppliedRevision = appliedRevision;
            TerminationReason = terminationReason;
        }

        public int ProcIndex { get; }
        public Guid ProcId { get; }
        public string ProcName { get; }
        public ProcRunState State { get; }
        public int StepIndex { get; }
        public int OpIndex { get; }
        public bool IsBreakpoint { get; }
        public bool IsAlarm => !string.IsNullOrWhiteSpace(AlarmMessage);
        public string AlarmMessage { get; }
        public DateTime UpdateTime { get; }
        public long UpdateTicks { get; }
        public long PublishedRevision { get; }
        public long AppliedRevision { get; }
        public ProcTerminationReason TerminationReason { get; }
        public ProcessPerformanceSnapshot Performance { get; internal set; }
    }

    public sealed class EngineContext
    {
        public IList<Proc> Procs { get; set; }
        public ValueConfigStore ValueStore { get; set; }
        public DataStructStore DataStructStore { get; set; }
        public TrayPointStore TrayPointStore { get; set; }
        public CardConfigStore CardStore { get; set; }
        public IMotionRuntime Motion { get; set; }
        public IIoRuntime Io { get; set; }
        public CommunicationHub Comm { get; set; }
        public CommunicationConfigStore CommunicationStore { get; set; }
        public PlcRuntimeService PlcRuntime { get; set; }
        public PlcConfigStore PlcStore { get; set; }
        public PlatformPaths Paths { get; set; }
        public AlarmInfoStore AlarmInfoStore { get; set; }
        public IDictionary<string, IO> IoMap { get; set; }
        public IList<DataStation> Stations { get; set; }
        public IList<SocketInfo> SocketInfos { get; set; }
        public IList<SerialPortInfo> SerialPortInfos { get; set; }
        public CustomFunc CustomFunc { get; set; }
        public AxisStatusCache AxisStatuses { get; set; }
        public AxisMotionParameterStore AxisMotionParameters { get; set; }
        public PlatformSafetyCoordinator Safety { get; set; }
        public ConfigurationMaintenanceService Maintenance { get; set; }
        public PlatformReadinessState Readiness { get; set; }
        public Func<ProcessDefinitionValidationContext> ValidationContextFactory { get; set; }
        public IProcessPopupService PopupService { get; set; }
    }
}
