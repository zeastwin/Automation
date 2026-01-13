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
using System.Windows.Forms;
using Automation.MotionControl;
using static Automation.FrmProc;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
using static Automation.FrmCard;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Numerics;

namespace Automation
{
    public partial class ProcessEngine
    {
        private ProcAgent[] agents = Array.Empty<ProcAgent>();
        private EngineSnapshot[] snapshots = Array.Empty<EngineSnapshot>();
        private readonly object agentLock = new object();
        private readonly TimeSpan stopJoinTimeout = TimeSpan.FromSeconds(2);
        private static readonly double stopwatchTickToMilliseconds = 1000.0 / Stopwatch.Frequency;
        private int snapshotThrottleMilliseconds = 50;
        private readonly ConcurrentDictionary<int, EngineSnapshot> pendingSnapshots = new ConcurrentDictionary<int, EngineSnapshot>();
        private readonly object snapshotDispatchLock = new object();
        private System.Threading.Timer snapshotTimer;
        private int snapshotFlushRunning;
        public EngineContext Context { get; }
        public IAlarmHandler AlarmHandler { get; set; }
        public ILogger Logger { get; set; }
        public event Action<EngineSnapshot> SnapshotChanged;
        public int SnapshotThrottleMilliseconds
        {
            get => snapshotThrottleMilliseconds;
            set
            {
                int normalized = value < 0 ? 0 : value;
                if (snapshotThrottleMilliseconds == normalized)
                {
                    return;
                }
                snapshotThrottleMilliseconds = normalized;
                UpdateSnapshotTimer();
            }
        }

        public ProcessEngine(EngineContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            if (Context.Procs != null && Context.Procs.Count > 0)
            {
                EnsureCapacity(Context.Procs.Count - 1);
            }
            UpdateSnapshotTimer();
        }
        public EngineSnapshot GetSnapshot(int procIndex)
        {
            if (procIndex < 0)
            {
                return null;
            }
            EnsureCapacity(procIndex);
            EngineSnapshot snapshot = null;
            if (procIndex < snapshots.Length)
            {
                snapshot = Volatile.Read(ref snapshots[procIndex]);
            }
            if (snapshot != null)
            {
                return snapshot;
            }

            string procName = null;
            if (Context?.Procs != null && procIndex < Context.Procs.Count)
            {
                procName = Context.Procs[procIndex]?.head?.Name;
            }
            long nowTicks = Stopwatch.GetTimestamp();
            snapshot = new EngineSnapshot(procIndex, procName, ProcRunState.Stopped, -1, -1, false, false, null, DateTime.Now, nowTicks);
            if (procIndex < snapshots.Length)
            {
                Volatile.Write(ref snapshots[procIndex], snapshot);
            }
            return snapshot;
        }
        public IReadOnlyList<EngineSnapshot> GetSnapshots()
        {
            int count = Context?.Procs?.Count ?? 0;
            List<EngineSnapshot> result = new List<EngineSnapshot>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(GetSnapshot(i));
            }
            return result;
        }
        public void RefreshProcName(int procIndex)
        {
            EngineSnapshot snapshot = GetSnapshot(procIndex);
            if (snapshot == null)
            {
                return;
            }
            string procName = null;
            if (Context?.Procs != null && procIndex >= 0 && procIndex < Context.Procs.Count)
            {
                procName = Context.Procs[procIndex]?.head?.Name;
            }
            UpdateSnapshot(procIndex, procName, snapshot.State, snapshot.StepIndex, snapshot.OpIndex, snapshot.IsBreakpoint,
                snapshot.IsAlarm, snapshot.AlarmMessage, true);
        }
        private void EnsureCapacity(int procIndex)
        {
            if (procIndex < 0)
            {
                return;
            }
            if (procIndex < agents.Length)
            {
                return;
            }
            lock (agentLock)
            {
                if (procIndex < agents.Length)
                {
                    return;
                }
                int newSize = agents.Length == 0 ? 1 : agents.Length;
                while (newSize <= procIndex)
                {
                    newSize *= 2;
                }
                Array.Resize(ref agents, newSize);
                Array.Resize(ref snapshots, newSize);
            }
        }
        private ProcAgent GetOrCreateAgent(int procIndex)
        {
            if (procIndex < 0)
            {
                return null;
            }
            EnsureCapacity(procIndex);
            lock (agentLock)
            {
                ProcAgent agent = agents[procIndex];
                if (agent == null)
                {
                    agent = new ProcAgent(this, procIndex);
                    agents[procIndex] = agent;
                }
                return agent;
            }
        }
        private ProcRunState GetProcState(int procIndex)
        {
            EngineSnapshot snapshot = GetSnapshot(procIndex);
            return snapshot?.State ?? ProcRunState.Stopped;
        }
        internal TimeSpan StopJoinTimeout => stopJoinTimeout;
        internal void UpdateSnapshot(int procIndex, string procName, ProcRunState state, int stepIndex, int opIndex,
            bool isBreakpoint, bool isAlarm, string alarmMessage, bool raiseEvent)
        {
            EnsureCapacity(procIndex);
            long nowTicks = Stopwatch.GetTimestamp();
            if (!raiseEvent && snapshotThrottleMilliseconds > 0)
            {
                EngineSnapshot current = Volatile.Read(ref snapshots[procIndex]);
                if (current != null)
                {
                    double elapsed = (nowTicks - current.UpdateTicks) * stopwatchTickToMilliseconds;
                    if (elapsed < snapshotThrottleMilliseconds
                        && current.State == state
                        && current.IsAlarm == isAlarm
                        && current.IsBreakpoint == isBreakpoint
                        && current.StepIndex == stepIndex
                        && current.OpIndex == opIndex
                        && string.Equals(current.ProcName, procName, StringComparison.Ordinal)
                        && string.Equals(current.AlarmMessage, alarmMessage, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }
            EngineSnapshot snapshot = new EngineSnapshot(procIndex, procName, state, stepIndex, opIndex, isBreakpoint,
                isAlarm, alarmMessage, DateTime.Now, nowTicks);
            Volatile.Write(ref snapshots[procIndex], snapshot);
            EnqueueSnapshot(snapshot);
        }
        private void UpdateSnapshotTimer()
        {
            lock (snapshotDispatchLock)
            {
                if (snapshotThrottleMilliseconds <= 0)
                {
                    snapshotTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }
                if (snapshotTimer == null)
                {
                    snapshotTimer = new System.Threading.Timer(_ => FlushPendingSnapshots(), null, snapshotThrottleMilliseconds, snapshotThrottleMilliseconds);
                    return;
                }
                snapshotTimer.Change(snapshotThrottleMilliseconds, snapshotThrottleMilliseconds);
            }
        }
        private void EnqueueSnapshot(EngineSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }
            pendingSnapshots[snapshot.ProcIndex] = snapshot;
            if (snapshotThrottleMilliseconds <= 0)
            {
                RaiseSnapshotChanged(snapshot);
            }
        }
        private void FlushPendingSnapshots()
        {
            if (pendingSnapshots.IsEmpty)
            {
                return;
            }
            if (Interlocked.Exchange(ref snapshotFlushRunning, 1) == 1)
            {
                return;
            }
            try
            {
                foreach (var item in pendingSnapshots)
                {
                    if (pendingSnapshots.TryRemove(item.Key, out EngineSnapshot snapshot))
                    {
                        RaiseSnapshotChanged(snapshot);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref snapshotFlushRunning, 0);
            }
        }
        private void RaiseSnapshotChanged(EngineSnapshot snapshot)
        {
            try
            {
                SnapshotChanged?.Invoke(snapshot);
            }
            catch (Exception ex)
            {
                Logger?.Log(ex.Message, LogLevel.Error);
            }
        }
        private bool TryExecuteGoto(string gotoText, ProcHandle evt, out string errorMessage)
        {
            errorMessage = null;
            if (evt == null)
            {
                errorMessage = "跳转失败：流程句柄为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(gotoText))
            {
                errorMessage = "跳转失败：跳转位置为空";
                return false;
            }
            string[] key = gotoText.Split('-');
            if (key.Length < 3)
            {
                errorMessage = $"跳转失败：格式错误 {gotoText}";
                return false;
            }
            if (!int.TryParse(key[0], out int procIndex)
                || !int.TryParse(key[1], out int stepIndex)
                || !int.TryParse(key[2], out int opIndex))
            {
                errorMessage = $"跳转失败：索引解析失败 {gotoText}";
                return false;
            }
            if (procIndex != evt.procNum)
            {
                errorMessage = $"跳转失败：流程索引不一致 {gotoText}";
                return false;
            }
            if (stepIndex < 0 || opIndex < 0)
            {
                errorMessage = $"跳转失败：索引无效 {gotoText}";
                return false;
            }
            Proc proc = null;
            if (Context?.Procs != null && evt.procNum >= 0 && evt.procNum < Context.Procs.Count)
            {
                proc = Context.Procs[evt.procNum];
            }
            if (proc != null)
            {
                if (proc.steps == null || stepIndex >= proc.steps.Count)
                {
                    errorMessage = $"跳转失败：步骤索引超界 {gotoText}";
                    return false;
                }
                Step step = proc.steps[stepIndex];
                if (step?.Ops == null || opIndex >= step.Ops.Count)
                {
                    errorMessage = $"跳转失败：操作索引超界 {gotoText}";
                    return false;
                }
            }
            evt.stepNum = stepIndex;
            evt.opsNum = opIndex;
            return true;
        }
        public void Delay(int milliSecond, ProcHandle evt)
        {
            if (milliSecond <= 0)
                return;
            if (evt == null)
            {
                Thread.Sleep(milliSecond);
                return;
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            int remaining = milliSecond;
            WaitHandle waitHandle = evt.CancellationToken.WaitHandle;
            while (remaining > 0
                && evt.State != ProcRunState.Stopped
                && !evt.isThStop
                && !evt.CancellationToken.IsCancellationRequested)
            {
                int slice = remaining > 20 ? 20 : remaining;
                if (waitHandle.WaitOne(slice))
                {
                    break;
                }
                remaining = milliSecond - (int)stopwatch.ElapsedMilliseconds;
            }
        }
        public void StartProc(Proc proc, int procIndex)
        {
            StartProcAt(proc, procIndex, 0, 0, ProcRunState.Running);
        }
        public void StartProcAuto(Proc proc, int index)
        {
            StartProcAt(proc, index, 0, 0, ProcRunState.Running);
        }
        public void StartProcAt(Proc proc, int procIndex, int stepIndex, int opIndex, ProcRunState startState)
        {
            if (proc == null)
            {
                return;
            }
            EngineCommand command = EngineCommand.Start(procIndex, proc, stepIndex, opIndex, startState);
            EnqueueCommand(procIndex, command);
        }
        public void Pause(int procIndex)
        {
            EnqueueCommand(procIndex, EngineCommand.Pause(procIndex));
        }
        public void Resume(int procIndex)
        {
            EnqueueCommand(procIndex, EngineCommand.Resume(procIndex));
        }
        public void Step(int procIndex)
        {
            EnqueueCommand(procIndex, EngineCommand.Step(procIndex));
        }
        public void RunSingleOpOnce(Proc proc, int procIndex, int stepIndex, int opIndex)
        {
            if (proc == null)
            {
                return;
            }
            EngineCommand command = EngineCommand.RunSingleOpOnce(procIndex, proc, stepIndex, opIndex);
            EnqueueCommand(procIndex, command);
        }
        public void Stop(int procIndex)
        {
            ProcAgent agent = GetOrCreateAgent(procIndex);
            agent?.RequestStop();
        }
        private void EnqueueCommand(int procIndex, EngineCommand command)
        {
            ProcAgent agent = GetOrCreateAgent(procIndex);
            if (agent == null || command == null)
            {
                return;
            }
            agent.Enqueue(command);
        }
        internal void RunProc(Proc proc, ProcHandle evt, ProcessControl control)
        {
            if (control == null)
            {
                return;
            }
            if (evt.State == ProcRunState.Stopped)
            {
                evt.State = ProcRunState.Running;
                control.SetRunning();
            }
            evt.isBreakpoint = false;
            UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
            for (int i = evt.stepNum; i < proc.steps.Count; i++)
            {
                if (evt.isThStop || control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                evt.stepNum = i;
                UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, false);
                RunStep(proc.steps[i], evt, control);
                if (evt.isThStop || control.IsStopRequested)
                    break;
                if (evt.isGoto)
                {
                    i = evt.stepNum - 1;
                    continue;
                }
                if (i != proc.steps.Count - 1)
                    evt.opsNum = 0;
            }
            evt.State = ProcRunState.Stopped;
            evt.isBreakpoint = false;
            UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
        }
        //运行步骤
        private bool RunStep(Step steps, ProcHandle evt, ProcessControl control)
        {

            bool bOK = true;
            for (int i = evt.opsNum; i < steps.Ops.Count; i++)
            {
                evt.isAlarm = false;
                evt.isGoto = false;
                evt.alarmMsg = null;
                if (evt.isThStop || control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                    return false;
                evt.opsNum = i;
                UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, false);
                if (steps.Ops[i].Enable)
                {
                    continue;
                }
                if (steps.Ops[i].isStopPoint)
                {
                    control.SetPaused();
                    evt.isBreakpoint = true;
                    if (evt.State != ProcRunState.SingleStep)
                    {
                        evt.State = ProcRunState.Paused;
                    }
                    UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
                }
                control.WaitForRun();
                if (evt.isThStop || control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                if (evt.State == ProcRunState.SingleStep)
                {
                    control.WaitForStep();
                }
                if (evt.isThStop || control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                if (evt.isBreakpoint)
                {
                    evt.isBreakpoint = false;
                    UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
                }
                if (evt.isThStop || control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                    return false;
                try
                {
                    ExecuteOperation(evt, steps.Ops[i]);
                    if (evt.isAlarm)
                    {
                        HandleAlarm(steps.Ops[i], evt);
                    }
                    if (evt.isGoto)
                    {
                        return true;
                    }
                    if (evt.singleOpOnce && evt.stepNum == evt.singleOpStep && evt.opsNum == evt.singleOpOp)
                    {
                        evt.isThStop = true;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = ex.Message;
                    Logger?.Log(ex.Message, LogLevel.Error);
                    HandleAlarm(steps.Ops[i], evt);
                    evt.isThStop = true;
                    return false;
                }
            }

            evt.opsNum = -1;
            UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, false);
            return bOK;


        }

        private void HandleAlarm(OperationType operation, ProcHandle evt)
        {
            ProcRunState lastState = evt.State;
            bool lastBreakpoint = evt.isBreakpoint;
            evt.State = ProcRunState.Alarming;
            evt.isBreakpoint = false;
            UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);

            Logger?.Log($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum} {evt.alarmMsg}", LogLevel.Error);

            AlarmDecision decision = ResolveAlarmDecision(operation, evt);
            ApplyAlarmDecision(operation, evt, decision);

            if (evt.State == ProcRunState.Alarming)
            {
                evt.State = evt.isThStop ? ProcRunState.Stopped : lastState;
                evt.isBreakpoint = evt.State == ProcRunState.Paused ? lastBreakpoint : false;
                UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
            }
        }

        private AlarmDecision ResolveAlarmDecision(OperationType operation, ProcHandle evt)
        {
            if (operation == null)
            {
                return AlarmDecision.Stop;
            }
            switch (operation.AlarmType)
            {
                case "报警停止":
                    return AlarmDecision.Stop;
                case "报警忽略":
                    return AlarmDecision.Ignore;
                case "自动处理":
                    return AlarmDecision.Goto1;
                case "弹框确定":
                case "弹框确定与否":
                case "弹框确定与否与取消":
                    return RequestAlarmDecision(operation, evt);
                default:
                    return AlarmDecision.Stop;
            }
        }

        private AlarmDecision RequestAlarmDecision(OperationType operation, ProcHandle evt)
        {
            if (AlarmHandler == null)
            {
                return AlarmDecision.Stop;
            }
            AlarmContext context = BuildAlarmContext(operation, evt);
            try
            {
                Task<AlarmDecision> decisionTask = AlarmHandler.HandleAsync(context);
                if (decisionTask == null)
                {
                    return AlarmDecision.Stop;
                }
                if (evt != null && evt.CancellationToken.CanBeCanceled)
                {
                    try
                    {
                        decisionTask.Wait(evt.CancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return AlarmDecision.Stop;
                    }
                }
                else
                {
                    decisionTask.Wait();
                }
                return decisionTask.Result;
            }
            catch (Exception ex)
            {
                Logger?.Log(ex.Message, LogLevel.Error);
                return AlarmDecision.Stop;
            }
        }

        private AlarmContext BuildAlarmContext(OperationType operation, ProcHandle evt)
        {
            AlarmInfo alarmInfo = null;
            if (!string.IsNullOrWhiteSpace(operation.AlarmInfoID)
                && int.TryParse(operation.AlarmInfoID, out int alarmIndex)
                && Context.AlarmInfoStore != null)
            {
                Context.AlarmInfoStore.TryGetByIndex(alarmIndex, out alarmInfo);
            }
            string note = !string.IsNullOrEmpty(alarmInfo?.Note) ? alarmInfo.Note : (evt.alarmMsg ?? "发生报警");
            string btn1 = !string.IsNullOrEmpty(alarmInfo?.Btn1) ? alarmInfo.Btn1 : "确定";
            string btn2 = !string.IsNullOrEmpty(alarmInfo?.Btn2) ? alarmInfo.Btn2 : "否";
            string btn3 = !string.IsNullOrEmpty(alarmInfo?.Btn3) ? alarmInfo.Btn3 : "取消";

            return new AlarmContext(
                evt.procNum,
                evt.stepNum,
                evt.opsNum,
                operation.AlarmType,
                evt.alarmMsg,
                note,
                btn1,
                btn2,
                btn3);
        }

        private void ApplyAlarmDecision(OperationType operation, ProcHandle evt, AlarmDecision decision)
        {
            switch (decision)
            {
                case AlarmDecision.Stop:
                    evt.isThStop = true;
                    break;
                case AlarmDecision.Ignore:
                    break;
                case AlarmDecision.Goto1:
                    evt.isGoto = true;
                    if (!TryExecuteGoto(operation.Goto1, evt, out string goto1Error))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = string.IsNullOrWhiteSpace(evt.alarmMsg) ? goto1Error : $"{evt.alarmMsg}; {goto1Error}";
                        evt.isThStop = true;
                    }
                    break;
                case AlarmDecision.Goto2:
                    evt.isGoto = true;
                    if (!TryExecuteGoto(operation.Goto2, evt, out string goto2Error))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = string.IsNullOrWhiteSpace(evt.alarmMsg) ? goto2Error : $"{evt.alarmMsg}; {goto2Error}";
                        evt.isThStop = true;
                    }
                    break;
                case AlarmDecision.Goto3:
                    evt.isGoto = true;
                    if (!TryExecuteGoto(operation.Goto3, evt, out string goto3Error))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = string.IsNullOrWhiteSpace(evt.alarmMsg) ? goto3Error : $"{evt.alarmMsg}; {goto3Error}";
                        evt.isThStop = true;
                    }
                    break;
            }
        }

    }

    internal sealed class ProcAgent : IDisposable
    {
        private sealed class ExecutionContext
        {
            public Proc Proc;
            public ProcHandle Handle;
            public ProcessControl Control;
            public Thread Thread;
        }

        private const int MaxQueueSize = 128;
        private readonly BlockingCollection<EngineCommand> queue = new BlockingCollection<EngineCommand>(new ConcurrentQueue<EngineCommand>(), MaxQueueSize);
        private readonly object queueLock = new object();
        private readonly Thread dispatcher;
        private readonly object sync = new object();
        private readonly ProcessEngine engine;
        private readonly int procIndex;
        private ExecutionContext current;
        private long generation;
        private bool disposed;

        public ProcAgent(ProcessEngine engine, int procIndex)
        {
            this.engine = engine;
            this.procIndex = procIndex;
            dispatcher = new Thread(DispatchLoop)
            {
                IsBackground = true
            };
            dispatcher.Start();
        }

        public void Enqueue(EngineCommand command)
        {
            if (disposed || command == null)
            {
                return;
            }
            lock (queueLock)
            {
                command.Generation = Volatile.Read(ref generation);
                if (command.Type == EngineCommandType.Start
                    || command.Type == EngineCommandType.StartAt
                    || command.Type == EngineCommandType.RunSingleOpOnce)
                {
                    ClearQueue();
                }
                if (!queue.TryAdd(command))
                {
                    if (queue.IsAddingCompleted)
                    {
                        return;
                    }
                    while (!queue.TryAdd(command))
                    {
                        if (!queue.TryTake(out _))
                        {
                            break;
                        }
                    }
                }
            }
        }

        public void RequestStop()
        {
            Interlocked.Increment(ref generation);
            lock (queueLock)
            {
                ClearQueue();
            }
            StopCurrent(true);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            queue.CompleteAdding();
            StopCurrent(false);
        }

        private void ClearQueue()
        {
            while (queue.TryTake(out _))
            {
            }
        }

        private void DispatchLoop()
        {
            foreach (EngineCommand command in queue.GetConsumingEnumerable())
            {
                if (disposed)
                {
                    return;
                }
                HandleCommand(command);
            }
        }

        private void HandleCommand(EngineCommand command)
        {
            long currentGen = Volatile.Read(ref generation);
            if (command.Generation != currentGen)
            {
                return;
            }
            switch (command.Type)
            {
                case EngineCommandType.Start:
                case EngineCommandType.StartAt:
                case EngineCommandType.RunSingleOpOnce:
                    StartInternal(command);
                    break;
                case EngineCommandType.Pause:
                    PauseInternal();
                    break;
                case EngineCommandType.Resume:
                    ResumeInternal();
                    break;
                case EngineCommandType.Step:
                    StepInternal();
                    break;
                case EngineCommandType.Stop:
                    RequestStop();
                    break;
            }
        }

        private void StartInternal(EngineCommand command)
        {
            Proc proc = command.Proc;
            if (proc == null && engine.Context?.Procs != null && command.ProcIndex >= 0 && command.ProcIndex < engine.Context.Procs.Count)
            {
                proc = engine.Context.Procs[command.ProcIndex];
            }
            if (proc == null)
            {
                engine.Logger?.Log("启动流程失败：流程为空。", LogLevel.Error);
                return;
            }

            ExecutionContext runningContext;
            lock (sync)
            {
                runningContext = current;
            }
            if (runningContext?.Thread != null && runningContext.Thread.IsAlive)
            {
                StopCurrent(true);
                if (!runningContext.Thread.Join(engine.StopJoinTimeout))
                {
                    const string timeoutMessage = "停止超时";
                    engine.Logger?.Log($"流程{procIndex}停止超时，拒绝再次启动。", LogLevel.Error);
                    ProcHandle timeoutHandle = runningContext.Handle;
                    if (timeoutHandle != null)
                    {
                        timeoutHandle.isAlarm = true;
                        timeoutHandle.alarmMsg = timeoutMessage;
                        engine.UpdateSnapshot(procIndex, timeoutHandle.procName, timeoutHandle.State, timeoutHandle.stepNum,
                            timeoutHandle.opsNum, timeoutHandle.isBreakpoint, true, timeoutMessage, true);
                    }
                    else
                    {
                        engine.UpdateSnapshot(procIndex, proc.head?.Name, ProcRunState.Stopped, -1, -1, false, true, timeoutMessage, true);
                    }
                    return;
                }
            }

            ProcessControl control = new ProcessControl();
            ProcHandle handle = new ProcHandle
            {
                procNum = procIndex,
                stepNum = command.StepIndex,
                opsNum = command.OpIndex,
                isThStop = false,
                procName = proc.head?.Name,
                singleOpOnce = command.SingleOpOnce,
                singleOpStep = command.StepIndex,
                singleOpOp = command.OpIndex,
                CancellationToken = control.CancellationToken
            };
            handle.State = command.StartState;
            handle.isBreakpoint = false;
            if (handle.State == ProcRunState.Running || handle.State == ProcRunState.SingleStep)
            {
                control.SetRunning();
            }
            else
            {
                control.SetPaused();
            }

            Thread execThread = new Thread(() => RunWorker(proc, handle, control))
            {
                IsBackground = true
            };
            lock (sync)
            {
                current = new ExecutionContext
                {
                    Proc = proc,
                    Handle = handle,
                    Control = control,
                    Thread = execThread
                };
            }
            execThread.Start();
            engine.UpdateSnapshot(procIndex, handle.procName, handle.State, handle.stepNum, handle.opsNum,
                handle.isBreakpoint, handle.isAlarm, handle.alarmMsg, true);
            if (command.AutoStep)
            {
                control.RequestStep();
            }
        }

        private void PauseInternal()
        {
            ProcHandle handle;
            ProcessControl control;
            lock (sync)
            {
                handle = current?.Handle;
                control = current?.Control;
            }
            if (handle == null || control == null)
            {
                return;
            }
            if (handle.State != ProcRunState.Running && handle.State != ProcRunState.Alarming)
            {
                return;
            }
            handle.State = ProcRunState.Paused;
            handle.isBreakpoint = false;
            control.SetPaused();
            engine.UpdateSnapshot(handle.procNum, handle.procName, handle.State, handle.stepNum,
                handle.opsNum, handle.isBreakpoint, handle.isAlarm, handle.alarmMsg, true);
        }

        private void ResumeInternal()
        {
            ProcHandle handle;
            ProcessControl control;
            lock (sync)
            {
                handle = current?.Handle;
                control = current?.Control;
            }
            if (handle == null || control == null)
            {
                return;
            }
            if (handle.State != ProcRunState.Paused && handle.State != ProcRunState.SingleStep)
            {
                return;
            }
            handle.State = ProcRunState.Running;
            handle.isBreakpoint = false;
            control.SetRunning();
            engine.UpdateSnapshot(handle.procNum, handle.procName, handle.State, handle.stepNum,
                handle.opsNum, handle.isBreakpoint, handle.isAlarm, handle.alarmMsg, true);
        }

        private void StepInternal()
        {
            ProcHandle handle;
            ProcessControl control;
            lock (sync)
            {
                handle = current?.Handle;
                control = current?.Control;
            }
            if (handle == null || control == null)
            {
                return;
            }
            if (handle.State != ProcRunState.Paused && handle.State != ProcRunState.SingleStep)
            {
                return;
            }
            handle.State = ProcRunState.SingleStep;
            handle.isBreakpoint = false;
            control.RequestStep();
            engine.UpdateSnapshot(handle.procNum, handle.procName, handle.State, handle.stepNum,
                handle.opsNum, handle.isBreakpoint, handle.isAlarm, handle.alarmMsg, true);
        }

        private void StopCurrent(bool raiseSnapshot)
        {
            ProcHandle handle;
            ProcessControl control;
            lock (sync)
            {
                handle = current?.Handle;
                control = current?.Control;
            }
            if (handle == null || control == null)
            {
                return;
            }
            handle.isThStop = true;
            handle.State = ProcRunState.Stopped;
            handle.isBreakpoint = false;
            control.RequestStop();
            if (raiseSnapshot)
            {
                engine.UpdateSnapshot(handle.procNum, handle.procName, handle.State, handle.stepNum,
                    handle.opsNum, handle.isBreakpoint, handle.isAlarm, handle.alarmMsg, true);
            }
        }

        private void RunWorker(Proc proc, ProcHandle runHandle, ProcessControl runControl)
        {
            try
            {
                engine.RunProc(proc, runHandle, runControl);
            }
            catch (Exception ex)
            {
                runHandle.isAlarm = true;
                runHandle.alarmMsg = ex.Message;
                runHandle.State = ProcRunState.Stopped;
                engine.UpdateSnapshot(runHandle.procNum, runHandle.procName, runHandle.State, runHandle.stepNum, runHandle.opsNum,
                    runHandle.isBreakpoint, runHandle.isAlarm, runHandle.alarmMsg, true);
                engine.Logger?.Log(ex.Message, LogLevel.Error);
            }
            finally
            {
                if (runHandle != null && runHandle.RunningTasks != null)
                {
                    Task[] tasks = runHandle.RunningTasks.ToArray();
                    if (tasks.Length > 0)
                    {
                        try
                        {
                            if (!Task.WaitAll(tasks, engine.StopJoinTimeout))
                            {
                                engine.Logger?.Log($"流程{runHandle.procNum}后台任务未在超时内结束。", LogLevel.Error);
                            }
                        }
                        catch (AggregateException ex)
                        {
                            engine.Logger?.Log($"流程{runHandle.procNum}后台任务异常:{ex.Message}", LogLevel.Error);
                        }
                        catch (Exception ex)
                        {
                            engine.Logger?.Log($"流程{runHandle.procNum}等待后台任务失败:{ex.Message}", LogLevel.Error);
                        }
                    }
                }
                runControl?.Dispose();
                lock (sync)
                {
                    if (current != null && ReferenceEquals(current.Thread, Thread.CurrentThread))
                    {
                        current = null;
                    }
                }
            }
        }
    }

    internal sealed class ProcessControl : IDisposable
    {
        private readonly ManualResetEventSlim runGate = new ManualResetEventSlim(false);
        private readonly SemaphoreSlim stepGate = new SemaphoreSlim(0, 1);
        private readonly CancellationTokenSource stopCts = new CancellationTokenSource();

        public bool IsStopRequested => stopCts.IsCancellationRequested;
        public CancellationToken CancellationToken => stopCts.Token;

        public void SetRunning()
        {
            runGate.Set();
        }

        public void SetPaused()
        {
            runGate.Reset();
        }

        public void RequestStep()
        {
            runGate.Set();
            try
            {
                stepGate.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }

        public void RequestStop()
        {
            stopCts.Cancel();
            runGate.Set();
            try
            {
                stepGate.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }

        public void WaitForRun()
        {
            try
            {
                runGate.Wait(stopCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void WaitForStep()
        {
            try
            {
                stepGate.Wait(stopCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose()
        {
            runGate.Dispose();
            stepGate.Dispose();
            stopCts.Dispose();
        }
    }
}
