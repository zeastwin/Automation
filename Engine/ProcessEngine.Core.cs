using System;
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
        private ProcRunner[] runners = Array.Empty<ProcRunner>();
        private EngineSnapshot[] snapshots = Array.Empty<EngineSnapshot>();
        private readonly object runnerLock = new object();
        private readonly TimeSpan stopJoinTimeout = TimeSpan.FromSeconds(2);
        public EngineContext Context { get; }
        public IAlarmHandler AlarmHandler { get; set; }
        public ILogger Logger { get; set; }
        public event Action<EngineSnapshot> SnapshotChanged;

        public ProcessEngine(EngineContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            if (Context.Procs != null && Context.Procs.Count > 0)
            {
                EnsureCapacity(Context.Procs.Count - 1);
            }
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
            snapshot = new EngineSnapshot(procIndex, procName, ProcRunState.Stopped, -1, -1, false, false, null, DateTime.Now);
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
            if (procIndex < runners.Length)
            {
                return;
            }
            lock (runnerLock)
            {
                if (procIndex < runners.Length)
                {
                    return;
                }
                int newSize = runners.Length == 0 ? 1 : runners.Length;
                while (newSize <= procIndex)
                {
                    newSize *= 2;
                }
                Array.Resize(ref runners, newSize);
                Array.Resize(ref snapshots, newSize);
            }
        }
        private ProcRunner GetOrCreateRunner(int procIndex)
        {
            if (procIndex < 0)
            {
                return null;
            }
            EnsureCapacity(procIndex);
            lock (runnerLock)
            {
                ProcRunner runner = runners[procIndex];
                if (runner == null)
                {
                    runner = new ProcRunner(this, procIndex);
                    runners[procIndex] = runner;
                }
                return runner;
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
            EngineSnapshot snapshot = new EngineSnapshot(procIndex, procName, state, stepIndex, opIndex, isBreakpoint,
                isAlarm, alarmMessage, DateTime.Now);
            Volatile.Write(ref snapshots[procIndex], snapshot);
            if (raiseEvent)
            {
                RaiseSnapshotChanged(snapshot);
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
        public void ExecuteGoto(string GOTO, ProcHandle evt)
        {
            string[] key = GOTO.Split('-');
            evt.stepNum = int.Parse(key[1]);
            evt.opsNum = int.Parse(key[2]);
        }
        public void Delay(int milliSecond, ProcHandle evt)
        {
            if (milliSecond <= 0)
                return;
            int start = Environment.TickCount;
            while (Math.Abs(Environment.TickCount - start) < milliSecond && evt.State != ProcRunState.Stopped && !evt.isThStop)//毫秒
            {
                Thread.Sleep(2);
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
            StartProcAtInternal(proc, procIndex, stepIndex, opIndex, startState, false);
        }
        private bool StartProcAtInternal(Proc proc, int procIndex, int stepIndex, int opIndex, ProcRunState startState, bool singleOpOnce)
        {
            if (proc == null)
            {
                return false;
            }
            ProcRunner runner = GetOrCreateRunner(procIndex);
            if (runner == null)
            {
                return false;
            }
            return runner.Start(proc, stepIndex, opIndex, startState, singleOpOnce);
        }
        public void Pause(int procIndex)
        {
            ProcRunner runner = GetOrCreateRunner(procIndex);
            runner?.Pause();
        }
        public void Resume(int procIndex)
        {
            ProcRunner runner = GetOrCreateRunner(procIndex);
            runner?.Resume();
        }
        public void Step(int procIndex)
        {
            ProcRunner runner = GetOrCreateRunner(procIndex);
            runner?.Step();
        }
        public void RunSingleOpOnce(Proc proc, int procIndex, int stepIndex, int opIndex)
        {
            if (StartProcAtInternal(proc, procIndex, stepIndex, opIndex, ProcRunState.SingleStep, true))
            {
                Step(procIndex);
            }
        }
        public void Stop(int procIndex)
        {
            ProcRunner runner = GetOrCreateRunner(procIndex);
            runner?.Stop();
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
                if (evt.isThStop || control.IsStopRequested)
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
                if (evt.State == ProcRunState.SingleStep)
                {
                    control.WaitForStep();
                }
                if (evt.isBreakpoint)
                {
                    evt.isBreakpoint = false;
                    UpdateSnapshot(evt.procNum, evt.procName, evt.State, evt.stepNum, evt.opsNum, evt.isBreakpoint, evt.isAlarm, evt.alarmMsg, true);
                }
                if (evt.isThStop || control.IsStopRequested)
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
                return AlarmHandler.HandleAsync(context).GetAwaiter().GetResult();
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
                    ExecuteGoto(operation.Goto1, evt);
                    break;
                case AlarmDecision.Goto2:
                    evt.isGoto = true;
                    ExecuteGoto(operation.Goto2, evt);
                    break;
                case AlarmDecision.Goto3:
                    evt.isGoto = true;
                    ExecuteGoto(operation.Goto3, evt);
                    break;
            }
        }

    }

    internal sealed class ProcRunner
    {
        private readonly object sync = new object();
        private readonly ProcessEngine engine;
        private readonly int procIndex;
        private Thread worker;
        private ProcessControl control;
        private ProcHandle handle;

        public ProcRunner(ProcessEngine engine, int procIndex)
        {
            this.engine = engine;
            this.procIndex = procIndex;
        }

        public bool Start(Proc proc, int stepIndex, int opIndex, ProcRunState startState, bool singleOpOnce)
        {
            if (proc == null)
            {
                return false;
            }
            Thread runningWorker = null;
            lock (sync)
            {
                if (worker != null && worker.IsAlive)
                {
                    RequestStopLocked();
                    runningWorker = worker;
                }
            }
            if (runningWorker != null)
            {
                if (!runningWorker.Join(engine.StopJoinTimeout))
                {
                    engine.Logger?.Log($"流程{procIndex}停止超时，拒绝再次启动。", LogLevel.Error);
                    return false;
                }
            }
            ProcHandle currentHandle;
            lock (sync)
            {
                control?.Dispose();
                control = new ProcessControl();
                handle = new ProcHandle
                {
                    procNum = procIndex,
                    stepNum = stepIndex,
                    opsNum = opIndex,
                    isThStop = false,
                    procName = proc.head?.Name,
                    singleOpOnce = singleOpOnce,
                    singleOpStep = stepIndex,
                    singleOpOp = opIndex
                };
                handle.State = startState;
                handle.isBreakpoint = false;
                if (startState == ProcRunState.Running || startState == ProcRunState.SingleStep)
                {
                    control.SetRunning();
                }
                else
                {
                    control.SetPaused();
                }
                worker = new Thread(() => RunWorker(proc, handle, control))
                {
                    IsBackground = true
                };
                worker.Start();
                currentHandle = handle;
            }
            engine.UpdateSnapshot(procIndex, currentHandle.procName, currentHandle.State, currentHandle.stepNum, currentHandle.opsNum,
                currentHandle.isBreakpoint, currentHandle.isAlarm, currentHandle.alarmMsg, true);
            return true;
        }

        public void Pause()
        {
            ProcHandle currentHandle;
            ProcessControl currentControl;
            lock (sync)
            {
                currentHandle = handle;
                currentControl = control;
                if (currentHandle == null || currentControl == null)
                {
                    return;
                }
                if (currentHandle.State != ProcRunState.Running && currentHandle.State != ProcRunState.Alarming)
                {
                    return;
                }
                currentHandle.State = ProcRunState.Paused;
                currentHandle.isBreakpoint = false;
                currentControl.SetPaused();
            }
            engine.UpdateSnapshot(currentHandle.procNum, currentHandle.procName, currentHandle.State, currentHandle.stepNum,
                currentHandle.opsNum, currentHandle.isBreakpoint, currentHandle.isAlarm, currentHandle.alarmMsg, true);
        }

        public void Resume()
        {
            ProcHandle currentHandle;
            ProcessControl currentControl;
            lock (sync)
            {
                currentHandle = handle;
                currentControl = control;
                if (currentHandle == null || currentControl == null)
                {
                    return;
                }
                if (currentHandle.State != ProcRunState.Paused && currentHandle.State != ProcRunState.SingleStep)
                {
                    return;
                }
                currentHandle.State = ProcRunState.Running;
                currentHandle.isBreakpoint = false;
                currentControl.SetRunning();
            }
            engine.UpdateSnapshot(currentHandle.procNum, currentHandle.procName, currentHandle.State, currentHandle.stepNum,
                currentHandle.opsNum, currentHandle.isBreakpoint, currentHandle.isAlarm, currentHandle.alarmMsg, true);
        }

        public void Step()
        {
            ProcHandle currentHandle;
            ProcessControl currentControl;
            lock (sync)
            {
                currentHandle = handle;
                currentControl = control;
                if (currentHandle == null || currentControl == null)
                {
                    return;
                }
                if (currentHandle.State != ProcRunState.Paused && currentHandle.State != ProcRunState.SingleStep)
                {
                    return;
                }
                currentHandle.State = ProcRunState.SingleStep;
                currentHandle.isBreakpoint = false;
                currentControl.RequestStep();
            }
            engine.UpdateSnapshot(currentHandle.procNum, currentHandle.procName, currentHandle.State, currentHandle.stepNum,
                currentHandle.opsNum, currentHandle.isBreakpoint, currentHandle.isAlarm, currentHandle.alarmMsg, true);
        }

        public void Stop()
        {
            ProcHandle currentHandle;
            ProcessControl currentControl;
            lock (sync)
            {
                currentHandle = handle;
                currentControl = control;
                if (currentHandle == null || currentControl == null)
                {
                    return;
                }
                currentHandle.isThStop = true;
                currentHandle.State = ProcRunState.Stopped;
                currentHandle.isBreakpoint = false;
                currentControl.RequestStop();
            }
            engine.UpdateSnapshot(currentHandle.procNum, currentHandle.procName, currentHandle.State, currentHandle.stepNum,
                currentHandle.opsNum, currentHandle.isBreakpoint, currentHandle.isAlarm, currentHandle.alarmMsg, true);
        }

        private void RequestStopLocked()
        {
            if (handle == null || control == null)
            {
                return;
            }
            handle.isThStop = true;
            control.RequestStop();
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
                lock (sync)
                {
                    runControl?.Dispose();
                    if (ReferenceEquals(control, runControl))
                    {
                        control = null;
                    }
                    if (ReferenceEquals(handle, runHandle))
                    {
                        handle = null;
                    }
                    if (ReferenceEquals(worker, Thread.CurrentThread))
                    {
                        worker = null;
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
