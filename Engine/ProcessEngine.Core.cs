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
    public partial class ProcessEngine : IDisposable
    {
        private ProcAgent[] agents = Array.Empty<ProcAgent>();
        private EngineSnapshot[] snapshots = Array.Empty<EngineSnapshot>();
        private readonly object agentLock = new object();
        private readonly TimeSpan stopJoinTimeout = TimeSpan.FromSeconds(2);
        private static readonly double stopwatchTickToMilliseconds = 1000.0 / Stopwatch.Frequency;
        private int snapshotThrottleMilliseconds = 50;
        private readonly ConcurrentDictionary<int, EngineSnapshot> pendingSnapshots = new ConcurrentDictionary<int, EngineSnapshot>();
        private readonly ConcurrentDictionary<int, Proc> pendingProcUpdates = new ConcurrentDictionary<int, Proc>();
        private readonly object snapshotDispatchLock = new object();
        private readonly object procPublishLock = new object();
        private System.Threading.Timer snapshotTimer;
        private int snapshotFlushRunning;
        private int disposed;
        public EngineContext Context { get; }
        public IAlarmHandler AlarmHandler { get; set; }
        public Control UiInvoker { get; set; }
        public ILogger Logger { get; set; }
        public Func<string, bool> PermissionChecker { get; set; }
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
        public bool PublishProc(int procIndex, Proc proc, out string error)
        {
            error = null;
            if (procIndex < 0)
            {
                error = "流程索引无效";
                return false;
            }
            if (proc == null)
            {
                error = "流程为空";
                return false;
            }
            if (Context == null)
            {
                error = "引擎上下文为空";
                return false;
            }
            EnsureCapacity(procIndex);
            lock (procPublishLock)
            {
                IList<Proc> current = Context.Procs;
                int newCount = Math.Max(procIndex + 1, current?.Count ?? 0);
                List<Proc> next = current != null ? new List<Proc>(current) : new List<Proc>(newCount);
                while (next.Count < newCount)
                {
                    next.Add(null);
                }
                next[procIndex] = proc;
                Context.Procs = next;
            }
            pendingProcUpdates[procIndex] = proc;
            EngineSnapshot snapshot = GetSnapshot(procIndex);
            if (snapshot == null || snapshot.State == ProcRunState.Stopped)
            {
                UpdateSnapshot(procIndex, proc.head?.Name, ProcRunState.Stopped, -1, -1, false, false, null, true);
            }
            return true;
        }

        public void ClearPendingProcUpdates()
        {
            pendingProcUpdates.Clear();
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
            if (Volatile.Read(ref disposed) == 1)
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
        private void MarkAlarm(ProcHandle evt, string message)
        {
            if (evt == null)
            {
                return;
            }
            string normalized = string.IsNullOrWhiteSpace(message) ? "执行失败" : message;
            evt.isAlarm = true;
            evt.alarmMsg = normalized;
        }

        private bool CheckPermission(string permissionKey, string actionName, ProcHandle evt = null)
        {
            if (PermissionChecker == null)
            {
                return true;
            }
            bool allowed;
            try
            {
                allowed = PermissionChecker(permissionKey);
            }
            catch (Exception ex)
            {
                string error = $"权限校验异常:{ex.Message}";
                Logger?.Log(error, LogLevel.Error);
                if (evt != null)
                {
                    MarkAlarm(evt, error);
                }
                return false;
            }
            if (allowed)
            {
                return true;
            }
            string message = $"权限不足:{actionName}";
            Logger?.Log(message, LogLevel.Error);
            if (evt != null)
            {
                MarkAlarm(evt, message);
            }
            return false;
        }
        private InvalidOperationException CreateAlarmException(ProcHandle evt, string message, Exception innerException = null)
        {
            string normalized = message;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = evt?.alarmMsg;
            }
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = "执行失败";
            }
            if (evt != null)
            {
                MarkAlarm(evt, normalized);
            }
            return innerException == null ? new InvalidOperationException(normalized) : new InvalidOperationException(normalized, innerException);
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
            int length = gotoText.Length;
            int index = 0;
            bool TryReadNumber(out int value)
            {
                value = 0;
                if (index >= length)
                {
                    return false;
                }
                long acc = 0;
                if (gotoText[index] < '0' || gotoText[index] > '9')
                {
                    return false;
                }
                while (index < length)
                {
                    char c = gotoText[index];
                    if (c < '0' || c > '9')
                    {
                        break;
                    }
                    acc = acc * 10 + (c - '0');
                    if (acc > int.MaxValue)
                    {
                        return false;
                    }
                    index++;
                }
                value = (int)acc;
                return true;
            }

            if (!TryReadNumber(out int procIndex) || index >= length || gotoText[index] != '-')
            {
                errorMessage = $"跳转失败：格式错误 {gotoText}";
                return false;
            }
            index++;
            if (!TryReadNumber(out int stepIndex) || index >= length || gotoText[index] != '-')
            {
                errorMessage = $"跳转失败：格式错误 {gotoText}";
                return false;
            }
            index++;
            if (!TryReadNumber(out int opIndex))
            {
                errorMessage = $"跳转失败：索引解析失败 {gotoText}";
                return false;
            }
            if (index != length)
            {
                errorMessage = $"跳转失败：格式错误 {gotoText}";
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
            Proc proc = evt.Proc;
            if (proc == null && Context?.Procs != null && evt.procNum >= 0 && evt.procNum < Context.Procs.Count)
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

        private bool HandlePauseSignal(ProcHandle evt, ProcessControl control)
        {
            if (evt == null || control == null)
            {
                return false;
            }
            if (evt.State == ProcRunState.Alarming)
            {
                return true;
            }
            if (evt.State == ProcRunState.SingleStep)
            {
                return true;
            }
            ProcHead head = evt.Proc?.head;
            if (head == null)
            {
                return true;
            }
            bool hasPauseIo = !string.IsNullOrWhiteSpace(head.PauseIoCount);
            bool hasPauseValue = !string.IsNullOrWhiteSpace(head.PauseValueCount)
                || (head.PauseValueParams != null && head.PauseValueParams.Count > 0);
            if (!hasPauseIo && !hasPauseValue)
            {
                return true;
            }

            while (true)
            {
                if (control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                if (!TryEvaluatePauseSignal(head, out bool pauseActive, out string error))
                {
                    MarkAlarm(evt, error);
                    HandleAlarm(null, evt);
                    return false;
                }
                if (!pauseActive)
                {
                    if (evt.PauseBySignal && evt.State == ProcRunState.Paused)
                    {
                        evt.State = ProcRunState.Running;
                        evt.isBreakpoint = false;
                        evt.PauseBySignal = false;
                        control.SetRunning();
                        UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
                    }
                    return true;
                }
                if (!evt.PauseBySignal)
                {
                    evt.State = ProcRunState.Paused;
                    evt.isBreakpoint = false;
                    evt.PauseBySignal = true;
                    control.SetPaused();
                    UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
                }
                Delay(50, evt);
            }
        }
        private bool FailHotUpdate(ProcHandle evt, ProcessControl control, string error)
        {
            string message = string.IsNullOrWhiteSpace(error) ? "热更新失败" : $"热更新失败:{error}";
            Logger?.Log(message, LogLevel.Error);
            MarkAlarm(evt, message);
            RaiseAlarmState(evt);
            control?.RequestStop();
            return false;
        }

        private bool TryApplyPendingProcUpdate(ProcHandle evt, ProcessControl control)
        {
            if (evt == null)
            {
                return false;
            }
            if (!pendingProcUpdates.TryRemove(evt.procNum, out Proc newProc))
            {
                return true;
            }
            if (newProc == null)
            {
                return true;
            }
            Proc oldProc = evt.Proc;
            if (oldProc == null)
            {
                evt.Proc = newProc;
                evt.procName = newProc.head?.Name;
                UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
                return true;
            }
            if (!TryMapProcPosition(oldProc, newProc, evt.stepNum, evt.opsNum, out int newStepIndex, out int newOpIndex, out string error))
            {
                return FailHotUpdate(evt, control, error);
            }
            int newSingleStep = evt.singleOpStep;
            int newSingleOp = evt.singleOpOp;
            if (evt.singleOpOnce)
            {
                if (!TryMapProcPosition(oldProc, newProc, evt.singleOpStep, evt.singleOpOp, out newSingleStep, out newSingleOp, out string singleError))
                {
                    return FailHotUpdate(evt, control, singleError);
                }
            }
            evt.Proc = newProc;
            evt.procName = newProc.head?.Name;
            evt.stepNum = newStepIndex;
            evt.opsNum = newOpIndex;
            if (evt.singleOpOnce)
            {
                evt.singleOpStep = newSingleStep;
                evt.singleOpOp = newSingleOp;
            }
            UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
            Logger?.Log($"流程{evt.procNum}热更新完成。", LogLevel.Normal);
            return true;
        }

        private bool TryMapProcPosition(Proc oldProc, Proc newProc, int oldStepIndex, int oldOpIndex,
            out int newStepIndex, out int newOpIndex, out string error)
        {
            newStepIndex = -1;
            newOpIndex = -1;
            error = null;
            if (oldProc?.steps == null || newProc?.steps == null)
            {
                error = "流程步骤为空";
                return false;
            }
            if (oldStepIndex < 0 || oldStepIndex >= oldProc.steps.Count)
            {
                error = $"当前步骤索引超界:{oldStepIndex}";
                return false;
            }
            Step oldStep = oldProc.steps[oldStepIndex];
            if (oldStep == null)
            {
                error = "当前步骤为空";
                return false;
            }
            if (oldStep.Id == Guid.Empty)
            {
                error = "当前步骤缺少ID";
                return false;
            }
            newStepIndex = newProc.steps.FindIndex(step => step != null && step.Id == oldStep.Id);
            if (newStepIndex < 0)
            {
                error = "当前步骤已被删除或重建";
                return false;
            }
            if (oldOpIndex < 0)
            {
                newOpIndex = oldOpIndex;
                return true;
            }
            if (oldStep.Ops == null || oldOpIndex >= oldStep.Ops.Count)
            {
                error = $"当前指令索引超界:{oldOpIndex}";
                return false;
            }
            OperationType oldOp = oldStep.Ops[oldOpIndex];
            if (oldOp == null)
            {
                error = "当前指令为空";
                return false;
            }
            if (oldOp.Id == Guid.Empty)
            {
                error = "当前指令缺少ID";
                return false;
            }
            Step newStep = newProc.steps[newStepIndex];
            if (newStep?.Ops == null)
            {
                error = "更新后指令列表为空";
                return false;
            }
            newOpIndex = newStep.Ops.FindIndex(op => op != null && op.Id == oldOp.Id);
            if (newOpIndex < 0)
            {
                error = "当前指令已被删除或重建";
                return false;
            }
            return true;
        }

        private bool TryEvaluatePauseSignal(ProcHead head, out bool pauseActive, out string error)
        {
            pauseActive = false;
            error = null;
            if (head == null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(head.PauseIoCount))
            {
                if (!int.TryParse(head.PauseIoCount, out int ioCount) || ioCount < 0)
                {
                    error = $"暂停IO数无效:{head.PauseIoCount}";
                    return false;
                }
                if (ioCount > 0)
                {
                    if (head.PauseIoParams == null || head.PauseIoParams.Count < ioCount)
                    {
                        error = "暂停IO配置不足";
                        return false;
                    }
                    for (int i = 0; i < ioCount; i++)
                    {
                        PauseIoParam param = head.PauseIoParams[i];
                        if (param == null || string.IsNullOrWhiteSpace(param.IOName))
                        {
                            error = $"暂停IO{i + 1}名称为空";
                            return false;
                        }
                        if (Context?.IoMap == null || !Context.IoMap.TryGetValue(param.IOName, out IO io) || io == null)
                        {
                            error = $"IO映射不存在:{param.IOName}";
                            return false;
                        }
                        bool value = false;
                        bool ok;
                        if (io.IOType == "通用输入")
                        {
                            ok = Context.Motion != null && Context.Motion.GetInIO(io, ref value);
                        }
                        else if (io.IOType == "通用输出")
                        {
                            ok = Context.Motion != null && Context.Motion.GetOutIO(io, ref value);
                        }
                        else
                        {
                            error = $"IO类型无效:{param.IOName}";
                            return false;
                        }
                        if (!ok)
                        {
                            error = $"IO读取失败:{param.IOName}";
                            return false;
                        }
                        if (value)
                        {
                            pauseActive = true;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(head.PauseValueCount) || (head.PauseValueParams != null && head.PauseValueParams.Count > 0))
            {
                if (Context?.ValueStore == null)
                {
                    error = "变量库未初始化";
                    return false;
                }
                int valueCount = 0;
                if (!string.IsNullOrWhiteSpace(head.PauseValueCount))
                {
                    if (!int.TryParse(head.PauseValueCount, out valueCount) || valueCount < 0)
                    {
                        error = $"暂停变量数无效:{head.PauseValueCount}";
                        return false;
                    }
                }
                else if (head.PauseValueParams != null)
                {
                    valueCount = head.PauseValueParams.Count;
                }
                if (valueCount > 0)
                {
                    if (head.PauseValueParams == null || head.PauseValueParams.Count < valueCount)
                    {
                        error = "暂停变量配置不足";
                        return false;
                    }
                    for (int i = 0; i < valueCount; i++)
                    {
                        PauseValueParam param = head.PauseValueParams[i];
                        if (param == null || string.IsNullOrWhiteSpace(param.ValueName))
                        {
                            error = $"暂停变量{i + 1}名称为空";
                            return false;
                        }
                        if (!Context.ValueStore.TryGetValueByName(param.ValueName, out DicValue valueItem) || valueItem == null)
                        {
                            error = $"暂停变量不存在:{param.ValueName}";
                            return false;
                        }
                        if (!string.Equals(valueItem.Type, "double", StringComparison.OrdinalIgnoreCase))
                        {
                            error = $"暂停变量类型不是double:{param.ValueName}";
                            return false;
                        }
                        double value;
                        try
                        {
                            value = valueItem.GetDValue();
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                            return false;
                        }
                        if (value != 0)
                        {
                            pauseActive = true;
                        }
                    }
                }
            }

            return true;
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
            if (proc == null && Context?.Procs != null && procIndex >= 0 && procIndex < Context.Procs.Count)
            {
                proc = Context.Procs[procIndex];
            }
            if (proc == null)
            {
                Logger?.Log("启动流程失败：流程为空。", LogLevel.Error);
                return;
            }
            if (proc.head?.Disable == true)
            {
                string name = string.IsNullOrWhiteSpace(proc.head?.Name) ? $"索引{procIndex}" : proc.head.Name;
                Logger?.Log($"流程已禁用，禁止启动：{name}", LogLevel.Normal);
                return;
            }
            if (!CheckPermission(PermissionKeys.ProcessRun, "启动流程"))
            {
                return;
            }
            EngineCommand command = EngineCommand.Start(procIndex, proc, stepIndex, opIndex, startState);
            EnqueueCommand(procIndex, command);
        }
        public void Pause(int procIndex)
        {
            if (!CheckPermission(PermissionKeys.ProcessRun, "暂停流程"))
            {
                return;
            }
            EnqueueCommand(procIndex, EngineCommand.Pause(procIndex));
        }
        public void Resume(int procIndex)
        {
            if (!CheckPermission(PermissionKeys.ProcessRun, "继续流程"))
            {
                return;
            }
            EnqueueCommand(procIndex, EngineCommand.Resume(procIndex));
        }
        public void Step(int procIndex)
        {
            if (!CheckPermission(PermissionKeys.ProcessRun, "单步流程"))
            {
                return;
            }
            EnqueueCommand(procIndex, EngineCommand.Step(procIndex));
        }
        public void RunSingleOpOnce(Proc proc, int procIndex, int stepIndex, int opIndex)
        {
            if (proc == null && Context?.Procs != null && procIndex >= 0 && procIndex < Context.Procs.Count)
            {
                proc = Context.Procs[procIndex];
            }
            if (proc == null)
            {
                Logger?.Log("单步执行指令失败：流程为空。", LogLevel.Error);
                return;
            }
            if (!CheckPermission(PermissionKeys.ProcessRun, "单步执行指令"))
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
            if (evt == null)
            {
                return;
            }
            if (proc == null)
            {
                MarkAlarm(evt, "运行流程失败：流程为空");
                HandleAlarm(null, evt);
                return;
            }
            evt.Proc = proc;
            evt.procName = proc.head?.Name;
            if (proc.head?.Disable == true)
            {
                string name = string.IsNullOrWhiteSpace(evt.procName) ? $"索引{evt.procNum}" : evt.procName;
                Logger?.Log($"流程已禁用，禁止运行：{name}", LogLevel.Normal);
                return;
            }
            if (evt.State == ProcRunState.Stopped)
            {
                evt.State = ProcRunState.Running;
                control.SetRunning();
            }
            evt.PauseBySignal = false;
            evt.isBreakpoint = false;
            UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
            while (true)
            {
                if (control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                if (!TryApplyPendingProcUpdate(evt, control))
                {
                    break;
                }
                Proc currentProc = evt.Proc;
                if (currentProc == null)
                {
                    MarkAlarm(evt, "运行流程失败：流程为空");
                    HandleAlarm(null, evt);
                    break;
                }
                if (currentProc.steps == null || currentProc.steps.Count == 0)
                {
                    MarkAlarm(evt, "运行流程失败：步骤为空");
                    HandleAlarm(null, evt);
                    break;
                }
                if (evt.stepNum < 0 || evt.stepNum >= currentProc.steps.Count)
                {
                    MarkAlarm(evt, $"运行流程失败：步骤索引超界 {evt.stepNum}");
                    HandleAlarm(null, evt);
                    break;
                }
                Step currentStep = currentProc.steps[evt.stepNum];
                if (currentStep != null && currentStep.Disable)
                {
                    evt.opsNum = -1;
                    UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, false);
                    if (evt.stepNum >= currentProc.steps.Count - 1)
                    {
                        break;
                    }
                    evt.stepNum++;
                    continue;
                }
                evt.procName = currentProc.head?.Name;
                UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, false);
                RunStep(evt, control);
                if (control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                if (evt.isGoto)
                {
                    continue;
                }
                if (evt.State == ProcRunState.Alarming)
                {
                    continue;
                }
                currentProc = evt.Proc;
                if (currentProc == null || currentProc.steps == null)
                {
                    MarkAlarm(evt, "运行流程失败：流程为空");
                    HandleAlarm(null, evt);
                    break;
                }
                if (evt.stepNum >= currentProc.steps.Count - 1)
                {
                    break;
                }
                evt.opsNum = 0;
                evt.stepNum++;
            }
            evt.State = ProcRunState.Stopped;
            evt.isBreakpoint = false;
            UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
        }
        //运行步骤
        private bool RunStep(ProcHandle evt, ProcessControl control)
        {

            bool bOK = true;
            if (evt == null)
            {
                return false;
            }
            if (control == null)
            {
                return false;
            }
            while (true)
            {
                if (control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                if (!TryApplyPendingProcUpdate(evt, control))
                {
                    return false;
                }
                Proc currentProc = evt.Proc;
                if (currentProc == null)
                {
                    MarkAlarm(evt, "运行步骤失败：流程为空");
                    HandleAlarm(null, evt);
                    return false;
                }
                if (currentProc.steps == null)
                {
                    MarkAlarm(evt, "运行步骤失败：步骤为空");
                    HandleAlarm(null, evt);
                    return false;
                }
                if (evt.stepNum < 0 || evt.stepNum >= currentProc.steps.Count)
                {
                    MarkAlarm(evt, $"运行步骤失败：步骤索引超界 {evt.stepNum}");
                    HandleAlarm(null, evt);
                    return false;
                }
                Step step = currentProc.steps[evt.stepNum];
                if (step == null)
                {
                    MarkAlarm(evt, "运行步骤失败：步骤为空");
                    HandleAlarm(null, evt);
                    return false;
                }
                if (step.Ops == null)
                {
                    MarkAlarm(evt, "运行步骤失败：操作列表为空");
                    HandleAlarm(null, evt);
                    return false;
                }
                if (step.Ops.Count == 0)
                {
                    evt.opsNum = -1;
                    UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, false);
                    return true;
                }
                if (evt.opsNum < 0 || evt.opsNum >= step.Ops.Count)
                {
                    MarkAlarm(evt, $"运行步骤失败：操作索引超界 {evt.opsNum}");
                    HandleAlarm(null, evt);
                    return false;
                }
                while (evt.opsNum < step.Ops.Count)
                {
                    if (control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    if (!TryApplyPendingProcUpdate(evt, control))
                    {
                        return false;
                    }
                    currentProc = evt.Proc;
                    if (currentProc == null)
                    {
                        MarkAlarm(evt, "运行步骤失败：流程为空");
                        HandleAlarm(null, evt);
                        return false;
                    }
                    if (currentProc.steps == null || evt.stepNum < 0 || evt.stepNum >= currentProc.steps.Count)
                    {
                        MarkAlarm(evt, $"运行步骤失败：步骤索引超界 {evt.stepNum}");
                        HandleAlarm(null, evt);
                        return false;
                    }
                    step = currentProc.steps[evt.stepNum];
                    if (step == null || step.Ops == null)
                    {
                        MarkAlarm(evt, "运行步骤失败：操作列表为空");
                        HandleAlarm(null, evt);
                        return false;
                    }
                    if (evt.opsNum < 0 || evt.opsNum >= step.Ops.Count)
                    {
                        MarkAlarm(evt, $"运行步骤失败：操作索引超界 {evt.opsNum}");
                        HandleAlarm(null, evt);
                        return false;
                    }
                    evt.isGoto = false;
                    if (evt.State != ProcRunState.Alarming)
                    {
                        evt.isAlarm = false;
                        evt.alarmMsg = null;
                    }
                    UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, false);
                    OperationType operation = step.Ops[evt.opsNum];
                    if (operation == null)
                    {
                        MarkAlarm(evt, "运行步骤失败：指令为空");
                        HandleAlarm(null, evt);
                        return false;
                    }
                    if (operation.Disable)
                    {
                        evt.opsNum++;
                        continue;
                    }
                    if (operation.isStopPoint)
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
                    if (control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    if (evt.State == ProcRunState.SingleStep)
                    {
                        control.WaitForStep();
                    }
                    if (control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    if (evt.isBreakpoint)
                    {
                        evt.isBreakpoint = false;
                        UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
                    }
                    if (control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    if (!HandlePauseSignal(evt, control))
                    {
                        return false;
                    }
                    if (control.IsStopRequested || evt.CancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    try
                    {
                        ExecuteOperation(evt, operation);
                        if (evt.isAlarm)
                        {
                            HandleAlarm(operation, evt);
                        }
                        if (evt.isGoto)
                        {
                            return true;
                        }
                        if (evt.State == ProcRunState.Alarming)
                        {
                            return false;
                        }
                        if (evt.singleOpOnce && evt.stepNum == evt.singleOpStep && evt.opsNum == evt.singleOpOp)
                        {
                            control.RequestStop();
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        MarkAlarm(evt, ex.Message);
                        HandleAlarm(operation, evt);
                        return false;
                    }
                    evt.opsNum++;
                }

                evt.opsNum = -1;
                UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, false);
                return bOK;
            }


        }

        private void HandleAlarm(OperationType operation, ProcHandle evt)
        {
            evt.State = ProcRunState.Alarming;
            evt.isBreakpoint = false;
            evt.Control?.SetPaused();
            UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);

            Logger?.Log($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum} {evt.alarmMsg}", LogLevel.Error);

            AlarmDecision decision = ResolveAlarmDecision(operation, evt);
            ApplyAlarmDecision(operation, evt, decision);
        }

        private void RaiseAlarmState(ProcHandle evt)
        {
            if (evt == null)
            {
                return;
            }
            evt.State = ProcRunState.Alarming;
            evt.isBreakpoint = false;
            evt.Control?.SetPaused();
            UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
            Logger?.Log($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum} {evt.alarmMsg}", LogLevel.Error);
        }

        private AlarmDecision ResolveAlarmDecision(OperationType operation, ProcHandle evt)
        {
            if (operation == null)
            {
                return AlarmDecision.Stop;
            }
            AlarmTypeKind alarmTypeKind;
            switch (operation.AlarmType)
            {
                case "报警停止":
                    alarmTypeKind = AlarmTypeKind.Stop;
                    break;
                case "报警忽略":
                    alarmTypeKind = AlarmTypeKind.Ignore;
                    break;
                case "自动处理":
                    alarmTypeKind = AlarmTypeKind.AutoHandle;
                    break;
                case "弹框确定":
                    alarmTypeKind = AlarmTypeKind.Confirm;
                    break;
                case "弹框确定与否":
                    alarmTypeKind = AlarmTypeKind.ConfirmYesNo;
                    break;
                case "弹框确定与否与取消":
                    alarmTypeKind = AlarmTypeKind.ConfirmYesNoCancel;
                    break;
                default:
                    string invalidType = string.IsNullOrWhiteSpace(operation.AlarmType) ? "<空>" : operation.AlarmType;
                    string invalidMessage = $"报警类型无效:{invalidType}";
                    MarkAlarm(evt, string.IsNullOrWhiteSpace(evt.alarmMsg) ? invalidMessage : $"{evt.alarmMsg}; {invalidMessage}");
                    Logger?.Log(invalidMessage, LogLevel.Error);
                    return AlarmDecision.Stop;
            }

            switch (alarmTypeKind)
            {
                case AlarmTypeKind.Stop:
                    return AlarmDecision.Stop;
                case AlarmTypeKind.Ignore:
                    return AlarmDecision.Ignore;
                case AlarmTypeKind.AutoHandle:
                    return AlarmDecision.Goto1;
                case AlarmTypeKind.Confirm:
                case AlarmTypeKind.ConfirmYesNo:
                case AlarmTypeKind.ConfirmYesNoCancel:
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
            AlarmInfo alarmInfo = TryGetAlarmInfo(operation);
            DateTime alarmStartTime = DateTime.Now;
            bool needLog = true;
            try
            {
                Task<AlarmDecision> decisionTask = AlarmHandler.HandleAsync(context);
                if (decisionTask == null)
                {
                    needLog = false;
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
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return AlarmDecision.Stop;
                        }
                        return AlarmDecision.Ignore;
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
            finally
            {
                if (needLog)
                {
                    try
                    {
                        WriteWarmDisplayLog(
                            evt,
                            alarmInfo?.Name ?? string.Empty,
                            context?.Note ?? string.Empty,
                            alarmInfo?.Category ?? string.Empty,
                            alarmStartTime,
                            DateTime.Now);
                    }
                    catch (Exception ex)
                    {
                        MarkAlarm(evt, $"报警提示日志写入失败:{ex.Message}");
                        throw CreateAlarmException(evt, evt?.alarmMsg, ex);
                    }
                }
            }
        }

        private AlarmInfo TryGetAlarmInfo(OperationType operation)
        {
            if (operation == null)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(operation.AlarmInfoID))
            {
                return null;
            }
            if (!int.TryParse(operation.AlarmInfoID, out int alarmIndex))
            {
                return null;
            }
            if (Context.AlarmInfoStore == null)
            {
                return null;
            }
            Context.AlarmInfoStore.TryGetByIndex(alarmIndex, out AlarmInfo alarmInfo);
            return alarmInfo;
        }

        private AlarmContext BuildAlarmContext(OperationType operation, ProcHandle evt)
        {
            AlarmInfo alarmInfo = TryGetAlarmInfo(operation);
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
                    break;
                case AlarmDecision.Ignore:
                    break;
                case AlarmDecision.Goto1:
                    evt.isGoto = true;
                    if (!TryExecuteGoto(operation.Goto1, evt, out string goto1Error))
                    {
                        MarkAlarm(evt, string.IsNullOrWhiteSpace(evt.alarmMsg) ? goto1Error : $"{evt.alarmMsg}; {goto1Error}");
                        RaiseAlarmState(evt);
                    }
                    break;
                case AlarmDecision.Goto2:
                    evt.isGoto = true;
                    if (!TryExecuteGoto(operation.Goto2, evt, out string goto2Error))
                    {
                        MarkAlarm(evt, string.IsNullOrWhiteSpace(evt.alarmMsg) ? goto2Error : $"{evt.alarmMsg}; {goto2Error}");
                        RaiseAlarmState(evt);
                    }
                    break;
                case AlarmDecision.Goto3:
                    evt.isGoto = true;
                    if (!TryExecuteGoto(operation.Goto3, evt, out string goto3Error))
                    {
                        MarkAlarm(evt, string.IsNullOrWhiteSpace(evt.alarmMsg) ? goto3Error : $"{evt.alarmMsg}; {goto3Error}");
                        RaiseAlarmState(evt);
                    }
                    break;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1)
            {
                return;
            }
            lock (snapshotDispatchLock)
            {
                snapshotTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                snapshotTimer?.Dispose();
                snapshotTimer = null;
            }
            ProcAgent[] agentsToDispose;
            lock (agentLock)
            {
                agentsToDispose = agents;
                agents = Array.Empty<ProcAgent>();
                snapshots = Array.Empty<EngineSnapshot>();
            }
            if (agentsToDispose != null)
            {
                foreach (ProcAgent agent in agentsToDispose)
                {
                    agent?.Dispose();
                }
            }
            pendingSnapshots.Clear();
            pendingProcUpdates.Clear();
            GC.SuppressFinalize(this);
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
                if (command.Type == EngineCommandType.StartAt
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
            try
            {
                foreach (EngineCommand command in queue.GetConsumingEnumerable())
                {
                    if (disposed)
                    {
                        return;
                    }
                    try
                    {
                        HandleCommand(command);
                    }
                    catch (Exception ex)
                    {
                        engine.Logger?.Log($"调度处理异常:{command?.Type} {ex.Message}，触发安全停机", LogLevel.Error);
                        if (!disposed)
                        {
                            RequestStop();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                engine.Logger?.Log($"调度线程异常:{ex.Message}，触发安全停机", LogLevel.Error);
                if (!disposed)
                {
                    RequestStop();
                }
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
                procName = proc.head?.Name,
                Proc = proc,
                singleOpOnce = command.SingleOpOnce,
                singleOpStep = command.StepIndex,
                singleOpOp = command.OpIndex,
                CancellationToken = control.CancellationToken,
                Control = control
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
            handle.PauseBySignal = false;
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
            bool resumeFromSingleStep = handle.State == ProcRunState.SingleStep;
            handle.State = ProcRunState.Running;
            handle.isBreakpoint = false;
            handle.PauseBySignal = false;
            control.SetRunning();
            if (resumeFromSingleStep)
            {
                control.RequestStep();
            }
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
            handle.PauseBySignal = false;
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
                runControl?.RequestStop();
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
