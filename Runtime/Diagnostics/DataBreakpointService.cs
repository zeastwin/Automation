using System;
// 模块：运行时 / 诊断。
// 职责范围：记录并投影断点、性能、审计、异常、日志缓冲和运行黑匣子事实。

using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Automation
{
    internal enum DataBreakpointKind
    {
        VariableChanged = 0,
        ProcessStateChanged = 1
    }

    internal interface IDataBreakpointRuntimeSink
    {
        void OnVariableChanged(DicValue variable, string oldValue, string newValue, string source);
        void OnProcessStateChanged(EngineSnapshot previous, EngineSnapshot current);
    }

    internal sealed class DataBreakpointRuleSnapshot
    {
        public Guid Id { get; set; }
        public bool Enabled { get; set; }
        public DataBreakpointKind Kind { get; set; }
        public Guid VariableId { get; set; }
        public Guid ObservedProcId { get; set; }
        public ProcRunState TargetState { get; set; }
        public Guid PauseProcId { get; set; }
        public long HitCount { get; set; }
        public DataBreakpointHit LastHit { get; set; }
    }

    internal sealed class DataBreakpointHit : EventArgs
    {
        public Guid HitId { get; set; }
        public Guid RuleId { get; set; }
        public DataBreakpointKind Kind { get; set; }
        public DateTime HitTime { get; set; }
        public Guid VariableId { get; set; }
        public int VariableIndex { get; set; }
        public string VariableName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public ProcRunState? PreviousState { get; set; }
        public ProcRunState? CurrentState { get; set; }
        public string RawSource { get; set; }
        public string TriggerDescription { get; set; }
        public Guid TriggerProcId { get; set; }
        public Guid TriggerStepId { get; set; }
        public Guid TriggerOperationId { get; set; }
        public int TriggerProcIndex { get; set; } = -1;
        public int TriggerStepIndex { get; set; } = -1;
        public int TriggerOperationIndex { get; set; } = -1;
        public Guid PauseProcId { get; set; }
        public int PauseProcIndex { get; set; } = -1;
        public Guid PauseStepId { get; set; }
        public Guid PauseOperationId { get; set; }
        public int PauseStepIndex { get; set; } = -1;
        public int PauseOperationIndex { get; set; } = -1;
        public bool PauseRequestAccepted { get; set; }

        public string BuildSummary()
        {
            string trigger = string.IsNullOrWhiteSpace(TriggerDescription)
                ? "未标注的代码接口"
                : TriggerDescription;
            string change = Kind == DataBreakpointKind.VariableChanged
                ? $"变量[{VariableIndex:D3}] {VariableName}：{OldValue} -> {NewValue}"
                : $"流程状态：{PreviousState} -> {CurrentState}";
            string pauseResult = PauseRequestAccepted
                ? "暂停请求已接受"
                : "暂停目标当前不在可暂停状态";
            return $"数据断点命中：{change}；触发源：{trigger}；{pauseResult}。";
        }
    }

    /// <summary>
    /// 会话级数据断点。配置变更时构建不可变索引，变量写入和流程状态迁移热路径只读取索引。
    /// </summary>
    internal sealed class DataBreakpointService : IDataBreakpointRuntimeSink, IDisposable
    {
        private sealed class RuntimeRule
        {
            private int enabled;
            private long hitCount;
            private DataBreakpointHit lastHit;

            public Guid Id { get; set; }
            public bool Enabled
            {
                get => Volatile.Read(ref enabled) == 1;
                set => Volatile.Write(ref enabled, value ? 1 : 0);
            }
            public DataBreakpointKind Kind { get; set; }
            public Guid VariableId { get; set; }
            public Guid ObservedProcId { get; set; }
            public ProcRunState TargetState { get; set; }
            public Guid PauseProcId { get; set; }

            public long HitCount => Interlocked.Read(ref hitCount);
            public DataBreakpointHit LastHit => Volatile.Read(ref lastHit);

            public void RecordHit(DataBreakpointHit hit)
            {
                Interlocked.Increment(ref hitCount);
                Volatile.Write(ref lastHit, hit);
            }

            public DataBreakpointRuleSnapshot CreateSnapshot()
            {
                return new DataBreakpointRuleSnapshot
                {
                    Id = Id,
                    Enabled = Enabled,
                    Kind = Kind,
                    VariableId = VariableId,
                    ObservedProcId = ObservedProcId,
                    TargetState = TargetState,
                    PauseProcId = PauseProcId,
                    HitCount = HitCount,
                    LastHit = LastHit
                };
            }
        }

        private struct ProcessStateKey : IEquatable<ProcessStateKey>
        {
            public Guid ProcId;
            public ProcRunState State;

            public bool Equals(ProcessStateKey other)
            {
                return ProcId == other.ProcId && State == other.State;
            }

            public override bool Equals(object obj)
            {
                return obj is ProcessStateKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ProcId.GetHashCode() * 397) ^ (int)State;
                }
            }
        }

        private sealed class BreakpointIndex
        {
            public static readonly BreakpointIndex Empty = new BreakpointIndex(
                new Dictionary<Guid, RuntimeRule[]>(),
                new Dictionary<ProcessStateKey, RuntimeRule[]>());

            public BreakpointIndex(
                Dictionary<Guid, RuntimeRule[]> variableRules,
                Dictionary<ProcessStateKey, RuntimeRule[]> processStateRules)
            {
                VariableRules = variableRules;
                ProcessStateRules = processStateRules;
            }

            public Dictionary<Guid, RuntimeRule[]> VariableRules { get; }
            public Dictionary<ProcessStateKey, RuntimeRule[]> ProcessStateRules { get; }
        }

        private readonly object configurationLock = new object();
        private readonly Dictionary<Guid, RuntimeRule> rules = new Dictionary<Guid, RuntimeRule>();
        private readonly ValueConfigStore valueStore;
        private readonly ProcessEngine engine;
        private BreakpointIndex activeIndex = BreakpointIndex.Empty;
        private int disposed;

        public DataBreakpointService(ValueConfigStore valueStore, ProcessEngine engine)
        {
            this.valueStore = valueStore ?? throw new ArgumentNullException(nameof(valueStore));
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public event EventHandler RulesChanged;
        public event EventHandler<DataBreakpointHit> BreakpointHit;

        public IReadOnlyList<DataBreakpointRuleSnapshot> GetRulesSnapshot()
        {
            lock (configurationLock)
            {
                return rules.Values
                    .Select(rule => rule.CreateSnapshot())
                    .OrderBy(rule => rule.Kind)
                    .ThenBy(rule => rule.Id)
                    .ToList();
            }
        }

        public bool TryGetRule(Guid ruleId, out DataBreakpointRuleSnapshot snapshot)
        {
            lock (configurationLock)
            {
                if (rules.TryGetValue(ruleId, out RuntimeRule rule))
                {
                    snapshot = rule.CreateSnapshot();
                    return true;
                }
            }
            snapshot = null;
            return false;
        }

        public bool TryAddVariableBreakpoint(Guid variableId, Guid pauseProcId, out string error)
        {
            error = null;
            if (variableId == Guid.Empty || !valueStore.GetValuesSnapshot().Any(value => value.Id == variableId))
            {
                error = "变量不存在或稳定ID无效。";
                return false;
            }
            if (!TryFindProcess(pauseProcId, out _, out _))
            {
                error = "暂停目标流程不存在。";
                return false;
            }
            lock (configurationLock)
            {
                if (rules.Values.Any(rule => rule.Kind == DataBreakpointKind.VariableChanged
                    && rule.VariableId == variableId
                    && rule.PauseProcId == pauseProcId))
                {
                    error = "相同变量和暂停目标的数据断点已经存在。";
                    return false;
                }
                RuntimeRule rule = new RuntimeRule
                {
                    Id = Guid.NewGuid(),
                    Enabled = true,
                    Kind = DataBreakpointKind.VariableChanged,
                    VariableId = variableId,
                    PauseProcId = pauseProcId
                };
                rules.Add(rule.Id, rule);
                RebuildIndexLocked();
            }
            RaiseRulesChanged();
            return true;
        }

        public bool TryAddProcessStateBreakpoint(
            Guid observedProcId,
            ProcRunState targetState,
            Guid pauseProcId,
            out string error)
        {
            error = null;
            if (!TryFindProcess(observedProcId, out _, out _))
            {
                error = "被观察流程不存在。";
                return false;
            }
            if (!TryFindProcess(pauseProcId, out _, out _))
            {
                error = "暂停目标流程不存在。";
                return false;
            }
            if (targetState == ProcRunState.Pausing || targetState == ProcRunState.Paused)
            {
                error = "Pausing/Paused 是断点自身产生的暂停状态，不能作为状态断点条件。";
                return false;
            }
            if (observedProcId == pauseProcId
                && targetState != ProcRunState.Running
                && targetState != ProcRunState.SingleStep)
            {
                error = "观察流程进入该状态时自身已经不可继续运行，请选择另一个暂停目标流程。";
                return false;
            }
            lock (configurationLock)
            {
                if (rules.Values.Any(rule => rule.Kind == DataBreakpointKind.ProcessStateChanged
                    && rule.ObservedProcId == observedProcId
                    && rule.TargetState == targetState
                    && rule.PauseProcId == pauseProcId))
                {
                    error = "相同状态条件和暂停目标的数据断点已经存在。";
                    return false;
                }
                RuntimeRule rule = new RuntimeRule
                {
                    Id = Guid.NewGuid(),
                    Enabled = true,
                    Kind = DataBreakpointKind.ProcessStateChanged,
                    ObservedProcId = observedProcId,
                    TargetState = targetState,
                    PauseProcId = pauseProcId
                };
                rules.Add(rule.Id, rule);
                RebuildIndexLocked();
            }
            RaiseRulesChanged();
            return true;
        }

        public bool SetEnabled(Guid ruleId, bool enabled)
        {
            lock (configurationLock)
            {
                if (!rules.TryGetValue(ruleId, out RuntimeRule rule))
                {
                    return false;
                }
                if (rule.Enabled == enabled)
                {
                    return true;
                }
                rule.Enabled = enabled;
                RebuildIndexLocked();
            }
            RaiseRulesChanged();
            return true;
        }

        public bool Remove(Guid ruleId)
        {
            lock (configurationLock)
            {
                if (!rules.Remove(ruleId))
                {
                    return false;
                }
                RebuildIndexLocked();
            }
            RaiseRulesChanged();
            return true;
        }

        public void Clear()
        {
            lock (configurationLock)
            {
                if (rules.Count == 0)
                {
                    return;
                }
                rules.Clear();
                Volatile.Write(ref activeIndex, BreakpointIndex.Empty);
                valueStore.DataBreakpointSink = null;
                engine.DataBreakpointSink = null;
            }
            RaiseRulesChanged();
        }

        public void OnVariableChanged(DicValue variable, string oldValue, string newValue, string source)
        {
            if (Volatile.Read(ref disposed) != 0 || variable == null || variable.Id == Guid.Empty)
            {
                return;
            }
            BreakpointIndex index = Volatile.Read(ref activeIndex);
            if (!index.VariableRules.TryGetValue(variable.Id, out RuntimeRule[] matchedRules))
            {
                return;
            }
            for (int i = 0; i < matchedRules.Length; i++)
            {
                RuntimeRule rule = matchedRules[i];
                if (!rule.Enabled)
                {
                    continue;
                }
                DataBreakpointHit hit = new DataBreakpointHit
                {
                    HitId = Guid.NewGuid(),
                    RuleId = rule.Id,
                    Kind = DataBreakpointKind.VariableChanged,
                    HitTime = DateTime.Now,
                    VariableId = variable.Id,
                    VariableIndex = variable.Index,
                    VariableName = variable.Name,
                    OldValue = oldValue,
                    NewValue = newValue,
                    RawSource = source,
                    PauseProcId = rule.PauseProcId
                };
                ResolveVariableTrigger(hit, source);
                CapturePausePosition(hit);
                hit.PauseRequestAccepted = engine.RequestDataBreakpointPause(rule.PauseProcId, hit.HitId);
                rule.RecordHit(hit);
                RaiseBreakpointHit(hit);
            }
        }

        public void OnProcessStateChanged(EngineSnapshot previous, EngineSnapshot current)
        {
            if (Volatile.Read(ref disposed) != 0
                || previous == null
                || current == null
                || previous.ProcId != current.ProcId
                || previous.State == current.State
                || current.ProcId == Guid.Empty)
            {
                return;
            }
            BreakpointIndex index = Volatile.Read(ref activeIndex);
            ProcessStateKey key = new ProcessStateKey
            {
                ProcId = current.ProcId,
                State = current.State
            };
            if (!index.ProcessStateRules.TryGetValue(key, out RuntimeRule[] matchedRules))
            {
                return;
            }
            for (int i = 0; i < matchedRules.Length; i++)
            {
                RuntimeRule rule = matchedRules[i];
                if (!rule.Enabled)
                {
                    continue;
                }
                DataBreakpointHit hit = new DataBreakpointHit
                {
                    HitId = Guid.NewGuid(),
                    RuleId = rule.Id,
                    Kind = DataBreakpointKind.ProcessStateChanged,
                    HitTime = DateTime.Now,
                    PreviousState = previous.State,
                    CurrentState = current.State,
                    RawSource = "流程状态迁移",
                    PauseProcId = rule.PauseProcId
                };
                ResolveProcessTrigger(hit, current);
                CapturePausePosition(hit);
                hit.PauseRequestAccepted = engine.RequestDataBreakpointPause(rule.PauseProcId, hit.HitId);
                rule.RecordHit(hit);
                RaiseBreakpointHit(hit);
            }
        }

        private void RebuildIndexLocked()
        {
            var variables = new Dictionary<Guid, List<RuntimeRule>>();
            var states = new Dictionary<ProcessStateKey, List<RuntimeRule>>();
            foreach (RuntimeRule rule in rules.Values)
            {
                if (!rule.Enabled)
                {
                    continue;
                }
                if (rule.Kind == DataBreakpointKind.VariableChanged)
                {
                    if (!variables.TryGetValue(rule.VariableId, out List<RuntimeRule> variableRules))
                    {
                        variableRules = new List<RuntimeRule>();
                        variables.Add(rule.VariableId, variableRules);
                    }
                    variableRules.Add(rule);
                }
                else
                {
                    ProcessStateKey key = new ProcessStateKey
                    {
                        ProcId = rule.ObservedProcId,
                        State = rule.TargetState
                    };
                    if (!states.TryGetValue(key, out List<RuntimeRule> stateRules))
                    {
                        stateRules = new List<RuntimeRule>();
                        states.Add(key, stateRules);
                    }
                    stateRules.Add(rule);
                }
            }
            Dictionary<Guid, RuntimeRule[]> variableIndex = variables
                .ToDictionary(item => item.Key, item => item.Value.ToArray());
            Dictionary<ProcessStateKey, RuntimeRule[]> stateIndex = states
                .ToDictionary(item => item.Key, item => item.Value.ToArray());
            Volatile.Write(ref activeIndex, new BreakpointIndex(variableIndex, stateIndex));
            valueStore.DataBreakpointSink = variableIndex.Count == 0 ? null : this;
            engine.DataBreakpointSink = stateIndex.Count == 0 ? null : this;
        }

        private void ResolveVariableTrigger(DataBreakpointHit hit, string source)
        {
            if (TryParseOperationSource(source, out int procIndex, out int stepIndex, out int operationIndex)
                && TryResolvePosition(procIndex, stepIndex, operationIndex, hit))
            {
                return;
            }
            hit.TriggerDescription = string.IsNullOrWhiteSpace(source)
                ? "未标注的代码接口"
                : source.Trim();
        }

        private void ResolveProcessTrigger(DataBreakpointHit hit, EngineSnapshot snapshot)
        {
            if (!TryResolvePosition(snapshot.ProcIndex, snapshot.StepIndex, snapshot.OpIndex, hit))
            {
                hit.TriggerProcId = snapshot.ProcId;
                hit.TriggerProcIndex = snapshot.ProcIndex;
                hit.TriggerStepIndex = snapshot.StepIndex;
                hit.TriggerOperationIndex = snapshot.OpIndex;
                hit.TriggerDescription = $"流程[{snapshot.ProcIndex}] {snapshot.ProcName} 状态迁移";
            }
        }

        private bool TryResolvePosition(
            int procIndex,
            int stepIndex,
            int operationIndex,
            DataBreakpointHit hit)
        {
            IList<Proc> processes = engine.Context?.Procs;
            if (processes == null || procIndex < 0 || procIndex >= processes.Count)
            {
                return false;
            }
            Proc proc = processes[procIndex];
            if (proc?.head == null)
            {
                return false;
            }
            hit.TriggerProcId = proc.head.Id;
            hit.TriggerProcIndex = procIndex;
            string description = $"流程[{procIndex}] {proc.head.Name}";
            if (stepIndex >= 0 && stepIndex < (proc.steps?.Count ?? 0))
            {
                Step step = proc.steps[stepIndex];
                hit.TriggerStepId = step?.Id ?? Guid.Empty;
                hit.TriggerStepIndex = stepIndex;
                description += $" / 步骤[{stepIndex}] {step?.Name}";
                if (operationIndex >= 0 && operationIndex < (step?.Ops?.Count ?? 0))
                {
                    OperationType operation = step.Ops[operationIndex];
                    hit.TriggerOperationId = operation?.Id ?? Guid.Empty;
                    hit.TriggerOperationIndex = operationIndex;
                    string operationName = string.IsNullOrWhiteSpace(operation?.Name)
                        ? operation?.OperaType
                        : operation.Name;
                    description += $" / 指令[{operationIndex}] {operationName}";
                }
            }
            hit.TriggerDescription = description;
            return true;
        }

        private void CapturePausePosition(DataBreakpointHit hit)
        {
            if (!TryFindProcess(hit.PauseProcId, out Proc proc, out int procIndex))
            {
                return;
            }
            hit.PauseProcIndex = procIndex;
            EngineSnapshot snapshot = engine.GetSnapshot(procIndex);
            if (snapshot == null)
            {
                return;
            }
            hit.PauseStepIndex = snapshot.StepIndex;
            hit.PauseOperationIndex = snapshot.OpIndex;
            if (snapshot.StepIndex < 0 || snapshot.StepIndex >= (proc.steps?.Count ?? 0))
            {
                return;
            }
            Step step = proc.steps[snapshot.StepIndex];
            hit.PauseStepId = step?.Id ?? Guid.Empty;
            if (snapshot.OpIndex >= 0 && snapshot.OpIndex < (step?.Ops?.Count ?? 0))
            {
                hit.PauseOperationId = step.Ops[snapshot.OpIndex]?.Id ?? Guid.Empty;
            }
        }

        private bool TryFindProcess(Guid procId, out Proc proc, out int procIndex)
        {
            proc = null;
            procIndex = -1;
            if (procId == Guid.Empty)
            {
                return false;
            }
            IList<Proc> processes = engine.Context?.Procs;
            if (processes == null)
            {
                return false;
            }
            for (int i = 0; i < processes.Count; i++)
            {
                if (processes[i]?.head?.Id == procId)
                {
                    proc = processes[i];
                    procIndex = i;
                    return true;
                }
            }
            return false;
        }

        private static bool TryParseOperationSource(
            string source,
            out int procIndex,
            out int stepIndex,
            out int operationIndex)
        {
            procIndex = -1;
            stepIndex = -1;
            operationIndex = -1;
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }
            string[] parts = source.Split('-');
            return parts.Length == 3
                && int.TryParse(parts[0], out procIndex)
                && int.TryParse(parts[1], out stepIndex)
                && int.TryParse(parts[2], out operationIndex)
                && procIndex >= 0
                && stepIndex >= 0
                && operationIndex >= 0;
        }

        private void RaiseRulesChanged()
        {
            try
            {
                RulesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
            }
        }

        private void RaiseBreakpointHit(DataBreakpointHit hit)
        {
            try
            {
                BreakpointHit?.Invoke(this, hit);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            if (ReferenceEquals(valueStore.DataBreakpointSink, this))
            {
                valueStore.DataBreakpointSink = null;
            }
            if (ReferenceEquals(engine.DataBreakpointSink, this))
            {
                engine.DataBreakpointSink = null;
            }
            lock (configurationLock)
            {
                rules.Clear();
                Volatile.Write(ref activeIndex, BreakpointIndex.Empty);
            }
            RulesChanged = null;
            BreakpointHit = null;
        }
    }
}
