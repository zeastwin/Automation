using Automation.MotionControl;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

// 模块：运行时 / 设备状态感知。
// 职责范围：采集已确认拓扑绑定的现场 IO，生成信号事实和节点语义状态；不发出任何设备动作。

namespace Automation
{
    /// <summary>
    /// 轻量状态感知器。只轮询已确认拓扑所引用的 IO，不依赖拓扑页面是否打开。
    /// </summary>
    public sealed class EquipmentStatePerceptionService : IDisposable
    {
        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

        private readonly EquipmentTopologyStore topologyStore;
        private readonly IoConfigurationStore ioStore;
        private readonly IIoRuntime ioRuntime;
        private readonly EquipmentStateHistoryService history;
        private readonly TimeSpan pollInterval;
        private readonly Func<DateTime> utcNow;
        private readonly object configurationSync = new object();
        private readonly object stateSync = new object();
        private readonly Dictionary<string, SignalObservation> lastSignals =
            new Dictionary<string, SignalObservation>(StringComparer.Ordinal);
        private readonly Dictionary<string, EvaluatedNodeState> lastNodeStates =
            new Dictionary<string, EvaluatedNodeState>(StringComparer.Ordinal);
        private List<CompiledNode> compiledNodes = new List<CompiledNode>();
        private Timer timer;
        private long compiledTopologyStoreVersion = -1;
        private long compiledIoStoreVersion = -1;
        private long topologyRevision;
        private string lastObservationError;
        private int polling;
        private int disposed;

        public EquipmentStatePerceptionService(
            EquipmentTopologyStore topologyStore,
            IoConfigurationStore ioStore,
            IIoRuntime ioRuntime,
            EquipmentStateHistoryService history,
            TimeSpan? pollInterval = null,
            Func<DateTime> utcNow = null)
        {
            this.topologyStore = topologyStore ?? throw new ArgumentNullException(nameof(topologyStore));
            this.ioStore = ioStore ?? throw new ArgumentNullException(nameof(ioStore));
            this.ioRuntime = ioRuntime ?? throw new ArgumentNullException(nameof(ioRuntime));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            this.pollInterval = pollInterval ?? DefaultPollInterval;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            if (this.pollInterval < TimeSpan.FromMilliseconds(20))
            {
                throw new ArgumentOutOfRangeException(nameof(pollInterval));
            }
        }

        public bool IsRunning => timer != null && Volatile.Read(ref disposed) == 0;
        public string LastObservationError => Volatile.Read(ref lastObservationError);

        /// <summary>
        /// 返回感知器当前运行态。最近成功观测时间会在每次现场读取成功时刷新，
        /// 即使语义状态没有变化也不会写入新的历史事件。
        /// </summary>
        public EquipmentPerceptionSnapshot GetCurrentSnapshot()
        {
            lock (stateSync)
            {
                return new EquipmentPerceptionSnapshot
                {
                    CapturedAtUtc = utcNow(),
                    TopologyRevision = Interlocked.Read(ref topologyRevision),
                    IsRunning = IsRunning,
                    LastObservationError = LastObservationError ?? string.Empty,
                    NodeStates = lastNodeStates
                        .OrderBy(pair => pair.Value.NodeLabel, StringComparer.Ordinal)
                        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => ToPerceptionState(pair.Key, pair.Value))
                        .ToList()
                };
            }
        }

        public void Start()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(EquipmentStatePerceptionService));
            }
            if (timer != null) return;
            timer = new Timer(Poll, null, TimeSpan.Zero, pollInterval);
        }

        internal void PollOnce()
        {
            Poll(null);
        }

        private void Poll(object state)
        {
            if (Volatile.Read(ref disposed) != 0
                || Interlocked.Exchange(ref polling, 1) != 0)
            {
                return;
            }
            try
            {
                RefreshBindingsIfNeeded();
                List<CompiledNode> nodes;
                lock (configurationSync) nodes = compiledNodes.ToList();
                if (nodes.Count == 0)
                {
                    Volatile.Write(ref lastObservationError, null);
                    return;
                }

                DateTime observedAtUtc = utcNow();
                Dictionary<string, CompiledBinding> uniqueBindings = nodes
                    .SelectMany(item => item.Bindings)
                    .GroupBy(item => item.ResourceRef, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                var observations = new Dictionary<string, SignalObservation>(StringComparer.Ordinal);
                foreach (CompiledBinding binding in uniqueBindings.Values)
                {
                    lastSignals.TryGetValue(binding.ResourceRef, out SignalObservation previousObservation);
                    bool value = false;
                    bool succeeded;
                    try
                    {
                        succeeded = binding.IsOutput
                            ? ioRuntime.GetOutIO(binding.Io, ref value)
                            : ioRuntime.GetInIO(binding.Io, ref value);
                    }
                    catch (Exception ex)
                    {
                        succeeded = false;
                        Volatile.Write(ref lastObservationError, ex.Message);
                    }
                    var observation = new SignalObservation
                    {
                        HasValue = succeeded,
                        HasKnownValue = succeeded || previousObservation?.HasKnownValue == true,
                        Value = succeeded ? value : (previousObservation?.Value ?? false),
                        Quality = succeeded
                            ? EquipmentStateQualities.Good
                            : previousObservation?.HasKnownValue == true
                                ? EquipmentStateQualities.Stale
                                : EquipmentStateQualities.Unknown,
                        ObservedAtUtc = observedAtUtc,
                        Aspect = binding.IsOutput
                            ? EquipmentStateAspects.Commanded
                            : EquipmentStateAspects.Observed
                    };
                    observations[binding.ResourceRef] = observation;
                    RecordSignalTransition(binding, observation);
                }

                foreach (CompiledNode node in nodes)
                {
                    EvaluatedNodeState evaluated = EvaluateNode(node, observations);
                    RecordNodeTransition(node, evaluated, observedAtUtc);
                }
                if (observations.Values.Any(item => item.HasValue))
                {
                    Volatile.Write(ref lastObservationError, null);
                }
            }
            catch (Exception ex)
            {
                // 感知旁路异常只降低历史质量，不改变设备和流程运行状态。
                Volatile.Write(ref lastObservationError, ex.Message);
            }
            finally
            {
                Volatile.Write(ref polling, 0);
            }
        }

        private void RefreshBindingsIfNeeded()
        {
            long topologyStoreVersion = topologyStore.Version;
            long ioStoreVersion = ioStore.Version;
            if (topologyStoreVersion == compiledTopologyStoreVersion
                && ioStoreVersion == compiledIoStoreVersion)
            {
                return;
            }

            EquipmentTopologyDefinition topology = topologyStore.CreateSnapshot();
            var ioByName = ioStore.CreateSnapshot()
                .Where(group => group != null)
                .SelectMany(group => group)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            List<CompiledNode> next = (topology.Nodes ?? new List<EquipmentTopologyNode>())
                .Where(node => node != null
                    && string.Equals(node.ReviewState, "confirmed", StringComparison.Ordinal))
                .Select(node => CompileNode(node, ioByName))
                .Where(node => node.Bindings.Count > 0)
                .ToList();

            HashSet<string> activeNodeIds = new HashSet<string>(
                next.Select(item => item.Id), StringComparer.Ordinal);
            List<KeyValuePair<string, EvaluatedNodeState>> retiredStates;
            lock (stateSync)
            {
                retiredStates = lastNodeStates
                    .Where(pair => !activeNodeIds.Contains(pair.Key))
                    .Select(pair => new KeyValuePair<string, EvaluatedNodeState>(
                        pair.Key, CloneNodeState(pair.Value)))
                    .ToList();
                foreach (KeyValuePair<string, EvaluatedNodeState> retired in retiredStates)
                {
                    lastNodeStates.Remove(retired.Key);
                }
            }
            foreach (KeyValuePair<string, EvaluatedNodeState> previous in retiredStates)
            {
                history.Append(new EquipmentStateHistoryEvent
                {
                    ObservedAtUtc = utcNow(),
                    TopologyRevision = topology.Revision,
                    EventType = EquipmentStateEventTypes.NodeStateChanged,
                    NodeId = previous.Key,
                    NodeLabel = previous.Value.NodeLabel,
                    Aspect = EquipmentStateAspects.Estimated,
                    OldValue = previous.Value.StateName,
                    NewValue = "已移出当前感知拓扑",
                    Meaning = "节点或其已确认状态绑定已从当前拓扑移除。",
                    Quality = EquipmentStateQualities.Retired,
                    Confidence = 1,
                    SourceKind = "topology"
                });
            }

            lock (configurationSync)
            {
                compiledNodes = next;
                Interlocked.Exchange(ref topologyRevision, topology.Revision);
                compiledTopologyStoreVersion = topologyStoreVersion;
                compiledIoStoreVersion = ioStoreVersion;
            }
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = utcNow(),
                TopologyRevision = topology.Revision,
                EventType = EquipmentStateEventTypes.TopologyChanged,
                Aspect = EquipmentStateAspects.Topology,
                NewValue = topology.Revision.ToString(CultureInfo.InvariantCulture),
                Meaning = $"状态感知绑定已刷新：{next.Count} 个节点。",
                Quality = EquipmentStateQualities.Good,
                Confidence = 1,
                SourceKind = "topology"
            });
        }

        private static CompiledNode CompileNode(
            EquipmentTopologyNode node,
            IReadOnlyDictionary<string, IO> ioByName)
        {
            var compiled = new CompiledNode
            {
                Id = node.Id,
                Label = node.Label,
                Confidence = node.Confidence
            };
            foreach (EquipmentTopologyStateBinding binding in
                (node.StateBindings ?? new List<EquipmentTopologyStateBinding>())
                    .Where(item => item != null
                        && string.Equals(item.ReviewState, "confirmed", StringComparison.Ordinal)
                        && string.Equals(item.SourceKind, "io", StringComparison.Ordinal)
                        && ioByName.ContainsKey(item.ResourceRef ?? string.Empty))
                    .OrderByDescending(item => item.Priority)
                    .ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                IO io = ioByName[binding.ResourceRef];
                compiled.Bindings.Add(new CompiledBinding
                {
                    Id = binding.Id,
                    StateName = binding.StateName,
                    ResourceRef = binding.ResourceRef,
                    Operator = binding.Operator,
                    ExpectedValue = binding.ExpectedValue,
                    Meaning = binding.Meaning,
                    Confidence = binding.Confidence,
                    IsOutput = string.Equals(io.IOType, "通用输出", StringComparison.Ordinal),
                    Io = io
                });
            }
            return compiled;
        }

        private void RecordSignalTransition(CompiledBinding binding, SignalObservation next)
        {
            lastSignals.TryGetValue(binding.ResourceRef, out SignalObservation previous);
            bool valueChanged = previous == null
                || previous.HasValue != next.HasValue
                || (next.HasValue && previous.Value != next.Value);
            bool qualityChanged = previous == null
                || !string.Equals(previous.Quality, next.Quality, StringComparison.Ordinal);
            if (!valueChanged && !qualityChanged)
            {
                next.Sequence = previous.Sequence;
                lastSignals[binding.ResourceRef] = next;
                return;
            }

            EquipmentStateHistoryEvent appended = history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = next.ObservedAtUtc,
                TopologyRevision = Interlocked.Read(ref topologyRevision),
                EventType = valueChanged
                    ? EquipmentStateEventTypes.SignalChanged
                    : EquipmentStateEventTypes.SignalQualityChanged,
                Aspect = next.Aspect,
                OldValue = previous?.HasValue == true
                    ? previous.Value.ToString().ToLowerInvariant()
                    : string.Empty,
                NewValue = next.HasValue
                    ? next.Value.ToString().ToLowerInvariant()
                    : string.Empty,
                Meaning = binding.IsOutput ? "输出命令反馈" : "现场输入反馈",
                Quality = next.Quality,
                Confidence = 1,
                SourceKind = "io",
                ResourceRef = binding.ResourceRef
            });
            next.Sequence = appended?.Sequence ?? 0;
            lastSignals[binding.ResourceRef] = next;
        }

        private static EvaluatedNodeState EvaluateNode(
            CompiledNode node,
            IReadOnlyDictionary<string, SignalObservation> observations)
        {
            bool hasUnavailableSignal = false;
            EvaluatedNodeState staleCandidate = null;
            foreach (CompiledBinding binding in node.Bindings)
            {
                if (!observations.TryGetValue(binding.ResourceRef, out SignalObservation observation)
                    || !observation.HasKnownValue)
                {
                    hasUnavailableSignal = true;
                    continue;
                }
                if (!observation.HasValue)
                {
                    hasUnavailableSignal = true;
                    if (staleCandidate == null && Matches(binding, observation.Value))
                    {
                        staleCandidate = new EvaluatedNodeState
                        {
                            NodeLabel = node.Label,
                            StateName = binding.StateName,
                            Meaning = "现场信号暂不可读；显示最近一次已知状态。" + binding.Meaning,
                            Quality = EquipmentStateQualities.Stale,
                            Confidence = Math.Min(node.Confidence, binding.Confidence) * .5,
                            SourceKind = "io",
                            ResourceRef = binding.ResourceRef,
                            BindingId = binding.Id,
                            CausedBySequence = observation.Sequence
                        };
                    }
                    continue;
                }
                if (Matches(binding, observation.Value))
                {
                    return new EvaluatedNodeState
                    {
                        NodeLabel = node.Label,
                        StateName = binding.StateName,
                        Meaning = binding.Meaning,
                        Quality = EquipmentStateQualities.Good,
                        Confidence = Math.Min(node.Confidence, binding.Confidence),
                        SourceKind = "io",
                        ResourceRef = binding.ResourceRef,
                        BindingId = binding.Id,
                        CausedBySequence = observation.Sequence
                    };
                }
            }
            if (staleCandidate != null) return staleCandidate;
            return new EvaluatedNodeState
            {
                NodeLabel = node.Label,
                StateName = hasUnavailableSignal ? "状态未知" : "未匹配已知状态",
                Meaning = hasUnavailableSignal
                    ? "至少一个已确认状态信号当前不可读取。"
                    : "现场信号未命中该节点的任何已确认状态绑定。",
                Quality = hasUnavailableSignal
                    ? EquipmentStateQualities.Unknown
                    : EquipmentStateQualities.Good,
                Confidence = hasUnavailableSignal ? 0 : node.Confidence,
                SourceKind = "topology"
            };
        }

        private void RecordNodeTransition(
            CompiledNode node,
            EvaluatedNodeState next,
            DateTime observedAtUtc)
        {
            EvaluatedNodeState previous;
            lock (stateSync)
            {
                lastNodeStates.TryGetValue(node.Id, out previous);
                previous = CloneNodeState(previous);
            }
            bool stateChanged = previous == null
                || !string.Equals(previous.StateName, next.StateName, StringComparison.Ordinal)
                || !string.Equals(previous.Quality, next.Quality, StringComparison.Ordinal)
                || !string.Equals(previous.BindingId, next.BindingId, StringComparison.Ordinal);
            next.LastSuccessfulObservationAtUtc = string.Equals(
                    next.Quality, EquipmentStateQualities.Good, StringComparison.Ordinal)
                ? observedAtUtc
                : previous?.LastSuccessfulObservationAtUtc ?? default(DateTime);
            next.StateChangedAtUtc = stateChanged
                ? observedAtUtc
                : previous.StateChangedAtUtc;
            next.Sequence = previous?.Sequence ?? 0;
            if (!stateChanged)
            {
                lock (stateSync)
                {
                    lastNodeStates[node.Id] = CloneNodeState(next);
                }
                return;
            }
            EquipmentStateHistoryEvent appended = history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = observedAtUtc,
                TopologyRevision = Interlocked.Read(ref topologyRevision),
                EventType = EquipmentStateEventTypes.NodeStateChanged,
                NodeId = node.Id,
                NodeLabel = node.Label,
                Aspect = EquipmentStateAspects.Estimated,
                OldValue = previous?.StateName ?? string.Empty,
                NewValue = next.StateName,
                Meaning = next.Meaning,
                Quality = next.Quality,
                Confidence = next.Confidence,
                SourceKind = next.SourceKind,
                ResourceRef = next.ResourceRef,
                BindingId = next.BindingId,
                CausedBySequence = next.CausedBySequence > 0
                    ? (long?)next.CausedBySequence
                    : null
            });
            next.Sequence = appended?.Sequence ?? next.Sequence;
            lock (stateSync)
            {
                lastNodeStates[node.Id] = CloneNodeState(next);
            }
        }

        private static bool Matches(CompiledBinding binding, bool actual)
        {
            if (!EquipmentTopologyStore.TryParseIoBoolean(binding.ExpectedValue, out bool expected))
            {
                return false;
            }
            switch (binding.Operator)
            {
                case "not_equals": return actual != expected;
                case "inactive": return !actual;
                case "active": return actual;
                case "equals": return actual == expected;
                default: return false;
            }
        }

        private static EvaluatedNodeState CloneNodeState(EvaluatedNodeState source)
        {
            if (source == null) return null;
            return new EvaluatedNodeState
            {
                NodeLabel = source.NodeLabel,
                StateName = source.StateName,
                Meaning = source.Meaning,
                Quality = source.Quality,
                Confidence = source.Confidence,
                SourceKind = source.SourceKind,
                ResourceRef = source.ResourceRef,
                BindingId = source.BindingId,
                CausedBySequence = source.CausedBySequence,
                StateChangedAtUtc = source.StateChangedAtUtc,
                LastSuccessfulObservationAtUtc = source.LastSuccessfulObservationAtUtc,
                Sequence = source.Sequence
            };
        }

        private EquipmentNodePerceptionState ToPerceptionState(
            string nodeId,
            EvaluatedNodeState source)
        {
            return new EquipmentNodePerceptionState
            {
                NodeId = nodeId,
                NodeLabel = source.NodeLabel,
                StateName = source.StateName,
                Meaning = source.Meaning,
                Quality = source.Quality,
                Confidence = source.Confidence,
                StateChangedAtUtc = source.StateChangedAtUtc,
                LastSuccessfulObservationAtUtc = source.LastSuccessfulObservationAtUtc,
                Sequence = source.Sequence,
                TopologyRevision = Interlocked.Read(ref topologyRevision),
                SourceKind = source.SourceKind,
                ResourceRef = source.ResourceRef,
                BindingId = source.BindingId
            };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Timer currentTimer = Interlocked.Exchange(ref timer, null);
            currentTimer?.Dispose();
        }

        private sealed class CompiledNode
        {
            public CompiledNode()
            {
                Bindings = new List<CompiledBinding>();
            }

            public string Id { get; set; }
            public string Label { get; set; }
            public double Confidence { get; set; }
            public List<CompiledBinding> Bindings { get; }
        }

        private sealed class CompiledBinding
        {
            public string Id { get; set; }
            public string StateName { get; set; }
            public string ResourceRef { get; set; }
            public string Operator { get; set; }
            public string ExpectedValue { get; set; }
            public string Meaning { get; set; }
            public double Confidence { get; set; }
            public bool IsOutput { get; set; }
            public IO Io { get; set; }
        }

        private sealed class SignalObservation
        {
            public bool HasValue { get; set; }
            public bool HasKnownValue { get; set; }
            public bool Value { get; set; }
            public string Quality { get; set; }
            public DateTime ObservedAtUtc { get; set; }
            public string Aspect { get; set; }
            public long Sequence { get; set; }
        }

        private sealed class EvaluatedNodeState
        {
            public string NodeLabel { get; set; }
            public string StateName { get; set; }
            public string Meaning { get; set; }
            public string Quality { get; set; }
            public double Confidence { get; set; }
            public string SourceKind { get; set; }
            public string ResourceRef { get; set; }
            public string BindingId { get; set; }
            public long CausedBySequence { get; set; }
            public DateTime StateChangedAtUtc { get; set; }
            public DateTime LastSuccessfulObservationAtUtc { get; set; }
            public long Sequence { get; set; }
        }
    }
}
