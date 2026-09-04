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
        private readonly object configurationSync = new object();
        private readonly Dictionary<string, SignalObservation> lastSignals =
            new Dictionary<string, SignalObservation>(StringComparer.Ordinal);
        private readonly Dictionary<string, EvaluatedNodeState> lastNodeStates =
            new Dictionary<string, EvaluatedNodeState>(StringComparer.Ordinal);
        private List<CompiledNode> compiledNodes = new List<CompiledNode>();
        private Timer timer;
        private long compiledTopologyStoreVersion = -1;
        private long compiledIoStoreVersion = -1;
        private long topologyRevision;
        private int polling;
        private int disposed;

        public EquipmentStatePerceptionService(
            EquipmentTopologyStore topologyStore,
            IoConfigurationStore ioStore,
            IIoRuntime ioRuntime,
            EquipmentStateHistoryService history,
            TimeSpan? pollInterval = null)
        {
            this.topologyStore = topologyStore ?? throw new ArgumentNullException(nameof(topologyStore));
            this.ioStore = ioStore ?? throw new ArgumentNullException(nameof(ioStore));
            this.ioRuntime = ioRuntime ?? throw new ArgumentNullException(nameof(ioRuntime));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            this.pollInterval = pollInterval ?? DefaultPollInterval;
            if (this.pollInterval < TimeSpan.FromMilliseconds(20))
            {
                throw new ArgumentOutOfRangeException(nameof(pollInterval));
            }
        }

        public bool IsRunning => timer != null && Volatile.Read(ref disposed) == 0;
        public string LastObservationError { get; private set; }

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
                    LastObservationError = null;
                    return;
                }

                DateTime observedAtUtc = DateTime.UtcNow;
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
                        LastObservationError = ex.Message;
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
                    LastObservationError = null;
                }
            }
            catch (Exception ex)
            {
                // 感知旁路异常只降低历史质量，不改变设备和流程运行状态。
                LastObservationError = ex.Message;
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
            foreach (KeyValuePair<string, EvaluatedNodeState> previous in lastNodeStates.ToList())
            {
                if (activeNodeIds.Contains(previous.Key)) continue;
                history.Append(new EquipmentStateHistoryEvent
                {
                    ObservedAtUtc = DateTime.UtcNow,
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
                lastNodeStates.Remove(previous.Key);
            }

            lock (configurationSync)
            {
                compiledNodes = next;
                topologyRevision = topology.Revision;
                compiledTopologyStoreVersion = topologyStoreVersion;
                compiledIoStoreVersion = ioStoreVersion;
            }
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = DateTime.UtcNow,
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
                TopologyRevision = topologyRevision,
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
            lastNodeStates.TryGetValue(node.Id, out EvaluatedNodeState previous);
            if (previous != null
                && string.Equals(previous.StateName, next.StateName, StringComparison.Ordinal)
                && string.Equals(previous.Quality, next.Quality, StringComparison.Ordinal)
                && string.Equals(previous.BindingId, next.BindingId, StringComparison.Ordinal))
            {
                return;
            }
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = observedAtUtc,
                TopologyRevision = topologyRevision,
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
            lastNodeStates[node.Id] = next;
        }

        private static bool Matches(CompiledBinding binding, bool actual)
        {
            bool expected = ParseBoolean(binding.ExpectedValue);
            switch (binding.Operator)
            {
                case "not_equals": return actual != expected;
                case "inactive": return !actual;
                case "active": return actual;
                case "equals": return actual == expected;
                default: return false;
            }
        }

        private static bool ParseBoolean(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "active", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "高", StringComparison.Ordinal)
                || string.Equals(value, "开", StringComparison.Ordinal);
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
        }
    }
}
