using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// 模块：运行时 / 设备状态历史。
// 职责范围：保存权威语义事件时间线、生成节点投影和回放快照；不依赖运行黑匣子。

namespace Automation
{
    /// <summary>
    /// 设备状态历史的一等运行时服务。时间线是事实源，节点当前状态与任意时刻状态均由时间线投影。
    /// </summary>
    public sealed class EquipmentStateHistoryService : IDisposable
    {
        private const int DefaultMaximumRetainedEvents = 100000;
        private static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(24);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly JsonSerializerSettings PersistenceJsonSettings =
            new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None
            };

        private readonly object syncRoot = new object();
        private readonly Queue<EquipmentStateHistoryEvent> events =
            new Queue<EquipmentStateHistoryEvent>();
        private readonly Dictionary<string, EquipmentNodeStateProjection> retainedBaseline =
            new Dictionary<string, EquipmentNodeStateProjection>(StringComparer.Ordinal);
        private readonly Dictionary<string, EquipmentNodeStateProjection> current =
            new Dictionary<string, EquipmentNodeStateProjection>(StringComparer.Ordinal);
        private readonly int maximumRetainedEvents;
        private readonly TimeSpan retention;
        private readonly string persistenceRoot;
        private readonly object persistenceSync = new object();
        private Task persistenceTail = Task.CompletedTask;
        private long nextSequence;
        private long retainedBaselineSequence;
        private long retainedBaselineTopologyRevision;
        private long currentTopologyRevision;
        private int disposed;

        public EquipmentStateHistoryService(
            string persistenceRoot = null,
            int maximumRetainedEvents = DefaultMaximumRetainedEvents,
            TimeSpan? retention = null)
        {
            if (maximumRetainedEvents < 100)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRetainedEvents));
            }
            this.persistenceRoot = persistenceRoot;
            this.maximumRetainedEvents = maximumRetainedEvents;
            this.retention = retention ?? DefaultRetention;
        }

        public event EventHandler<EquipmentStateHistoryEvent> EventAppended;

        public long Revision => Interlocked.Read(ref nextSequence);
        public string LastPersistenceError { get; private set; }

        public EquipmentStateHistoryEvent Append(EquipmentStateHistoryEvent source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (Volatile.Read(ref disposed) != 0) return null;

            EquipmentStateHistoryEvent appended;
            lock (syncRoot)
            {
                DateTime receivedAtUtc = DateTime.UtcNow;
                appended = CloneEvent(source);
                appended.Sequence = ++nextSequence;
                appended.ReceivedAtUtc = receivedAtUtc;
                if (appended.ObservedAtUtc == default(DateTime))
                {
                    appended.ObservedAtUtc = receivedAtUtc;
                }
                Normalize(appended);
                events.Enqueue(appended);
                currentTopologyRevision = appended.TopologyRevision;
                ApplyProjection(current, appended);
                PruneLocked(receivedAtUtc);
            }

            QueuePersistence(appended);
            try
            {
                EventAppended?.Invoke(this, CloneEvent(appended));
            }
            catch
            {
                // 观察者故障不能反向影响状态历史事实的写入。
            }
            return CloneEvent(appended);
        }

        public EquipmentStateSnapshot GetCurrentSnapshot()
        {
            lock (syncRoot)
            {
                return BuildSnapshotLocked(
                    nextSequence, DateTime.UtcNow, currentTopologyRevision, current);
            }
        }

        public EquipmentStateSnapshot GetSnapshotAt(long sequence)
        {
            lock (syncRoot)
            {
                long target = Math.Max(retainedBaselineSequence, Math.Min(sequence, nextSequence));
                Dictionary<string, EquipmentNodeStateProjection> projection = CloneProjection(retainedBaseline);
                DateTime timeUtc = DateTime.UtcNow;
                long snapshotTopologyRevision = retainedBaselineTopologyRevision;
                foreach (EquipmentStateHistoryEvent item in events)
                {
                    if (item.Sequence > target) break;
                    ApplyProjection(projection, item);
                    timeUtc = item.ObservedAtUtc;
                    snapshotTopologyRevision = item.TopologyRevision;
                }
                return BuildSnapshotLocked(target, timeUtc, snapshotTopologyRevision, projection);
            }
        }

        public EquipmentStateHistoryWindow GetRecentWindow(int maximumEvents = 500)
        {
            return GetWindow(null, null, maximumEvents, null);
        }

        public EquipmentStateHistoryWindow GetWindow(
            DateTime? startUtc,
            DateTime? endUtc,
            int maximumEvents,
            string nodeId)
        {
            if (maximumEvents < 1 || maximumEvents > 5000)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEvents));
            }
            lock (syncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                PruneLocked(nowUtc);
                List<EquipmentStateHistoryEvent> eligible = events
                    .Where(item => (!startUtc.HasValue || item.ObservedAtUtc >= startUtc.Value)
                        && (!endUtc.HasValue || item.ObservedAtUtc <= endUtc.Value)
                        && (string.IsNullOrWhiteSpace(nodeId)
                            || string.Equals(item.NodeId, nodeId, StringComparison.Ordinal)))
                    .ToList();
                bool truncated = eligible.Count > maximumEvents;
                List<EquipmentStateHistoryEvent> selected = eligible
                    .Skip(Math.Max(0, eligible.Count - maximumEvents))
                    .Select(CloneEvent)
                    .ToList();
                long firstSequence = selected.Count > 0
                    ? selected[0].Sequence
                    : nextSequence + 1;
                Dictionary<string, EquipmentNodeStateProjection> baseline = CloneProjection(retainedBaseline);
                DateTime baselineTime = selected.Count > 0
                    ? selected[0].ObservedAtUtc
                    : nowUtc;
                long baselineTopologyRevision = retainedBaselineTopologyRevision;
                foreach (EquipmentStateHistoryEvent item in events)
                {
                    if (item.Sequence >= firstSequence) break;
                    ApplyProjection(baseline, item);
                    baselineTime = item.ObservedAtUtc;
                    baselineTopologyRevision = item.TopologyRevision;
                }
                return new EquipmentStateHistoryWindow
                {
                    EarliestAvailableSequence = events.Count > 0
                        ? events.Peek().Sequence
                        : retainedBaselineSequence,
                    LatestSequence = nextSequence,
                    Truncated = truncated,
                    Baseline = BuildSnapshotLocked(
                        Math.Max(retainedBaselineSequence, firstSequence - 1),
                        baselineTime,
                        baselineTopologyRevision,
                        baseline),
                    Events = selected
                };
            }
        }

        private void PruneLocked(DateTime nowUtc)
        {
            DateTime cutoffUtc = nowUtc - retention;
            while (events.Count > 0
                && (events.Count > maximumRetainedEvents
                    || events.Peek().ObservedAtUtc < cutoffUtc))
            {
                EquipmentStateHistoryEvent removed = events.Dequeue();
                ApplyProjection(retainedBaseline, removed);
                retainedBaselineSequence = removed.Sequence;
                retainedBaselineTopologyRevision = removed.TopologyRevision;
            }
        }

        private static void ApplyProjection(
            IDictionary<string, EquipmentNodeStateProjection> target,
            EquipmentStateHistoryEvent item)
        {
            if (!string.Equals(item.EventType, EquipmentStateEventTypes.NodeStateChanged,
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(item.NodeId))
            {
                return;
            }
            if (string.Equals(item.Quality, EquipmentStateQualities.Retired, StringComparison.Ordinal))
            {
                target.Remove(item.NodeId);
                return;
            }
            target[item.NodeId] = new EquipmentNodeStateProjection
            {
                NodeId = item.NodeId,
                NodeLabel = item.NodeLabel,
                StateName = item.NewValue,
                Meaning = item.Meaning,
                Quality = item.Quality,
                Confidence = item.Confidence,
                UpdatedAtUtc = item.ObservedAtUtc,
                Sequence = item.Sequence,
                TopologyRevision = item.TopologyRevision,
                SourceKind = item.SourceKind,
                ResourceRef = item.ResourceRef,
                BindingId = item.BindingId
            };
        }

        private EquipmentStateSnapshot BuildSnapshotLocked(
            long sequence,
            DateTime timeUtc,
            long topologyRevision,
            IDictionary<string, EquipmentNodeStateProjection> projection)
        {
            return new EquipmentStateSnapshot
            {
                Sequence = sequence,
                TimeUtc = timeUtc,
                TopologyRevision = topologyRevision,
                NodeStates = projection.Values
                    .OrderBy(item => item.NodeLabel, StringComparer.Ordinal)
                    .ThenBy(item => item.NodeId, StringComparer.Ordinal)
                    .Select(CloneProjection)
                    .ToList()
            };
        }

        private static Dictionary<string, EquipmentNodeStateProjection> CloneProjection(
            IDictionary<string, EquipmentNodeStateProjection> source)
        {
            return source.ToDictionary(
                pair => pair.Key,
                pair => CloneProjection(pair.Value),
                StringComparer.Ordinal);
        }

        private static EquipmentNodeStateProjection CloneProjection(EquipmentNodeStateProjection source)
        {
            return new EquipmentNodeStateProjection
            {
                NodeId = source.NodeId,
                NodeLabel = source.NodeLabel,
                StateName = source.StateName,
                Meaning = source.Meaning,
                Quality = source.Quality,
                Confidence = source.Confidence,
                UpdatedAtUtc = source.UpdatedAtUtc,
                Sequence = source.Sequence,
                TopologyRevision = source.TopologyRevision,
                SourceKind = source.SourceKind,
                ResourceRef = source.ResourceRef,
                BindingId = source.BindingId
            };
        }

        private static EquipmentStateHistoryEvent CloneEvent(EquipmentStateHistoryEvent source)
        {
            return new EquipmentStateHistoryEvent
            {
                Sequence = source.Sequence,
                ObservedAtUtc = source.ObservedAtUtc,
                ReceivedAtUtc = source.ReceivedAtUtc,
                TopologyRevision = source.TopologyRevision,
                EventType = source.EventType,
                NodeId = source.NodeId,
                NodeLabel = source.NodeLabel,
                Aspect = source.Aspect,
                OldValue = source.OldValue,
                NewValue = source.NewValue,
                Meaning = source.Meaning,
                Quality = source.Quality,
                Confidence = source.Confidence,
                SourceKind = source.SourceKind,
                ResourceRef = source.ResourceRef,
                BindingId = source.BindingId,
                RunId = source.RunId,
                ProcessId = source.ProcessId,
                OperationId = source.OperationId,
                CausedBySequence = source.CausedBySequence
            };
        }

        private static void Normalize(EquipmentStateHistoryEvent item)
        {
            item.EventType = item.EventType ?? string.Empty;
            item.NodeId = item.NodeId ?? string.Empty;
            item.NodeLabel = item.NodeLabel ?? string.Empty;
            item.Aspect = item.Aspect ?? string.Empty;
            item.OldValue = item.OldValue ?? string.Empty;
            item.NewValue = item.NewValue ?? string.Empty;
            item.Meaning = item.Meaning ?? string.Empty;
            item.Quality = string.IsNullOrWhiteSpace(item.Quality)
                ? EquipmentStateQualities.Unknown
                : item.Quality;
            item.SourceKind = item.SourceKind ?? string.Empty;
            item.ResourceRef = item.ResourceRef ?? string.Empty;
            item.BindingId = item.BindingId ?? string.Empty;
            item.RunId = item.RunId ?? string.Empty;
            item.ProcessId = item.ProcessId ?? string.Empty;
            item.OperationId = item.OperationId ?? string.Empty;
            if (double.IsNaN(item.Confidence) || double.IsInfinity(item.Confidence))
            {
                item.Confidence = 0;
            }
            item.Confidence = Math.Max(0, Math.Min(1, item.Confidence));
        }

        private void QueuePersistence(EquipmentStateHistoryEvent item)
        {
            if (string.IsNullOrWhiteSpace(persistenceRoot)) return;
            EquipmentStateHistoryEvent copy = CloneEvent(item);
            lock (persistenceSync)
            {
                persistenceTail = persistenceTail.ContinueWith(
                    ignored => Persist(copy),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        private void Persist(EquipmentStateHistoryEvent item)
        {
            try
            {
                string directory = Path.Combine(
                    persistenceRoot,
                    item.ObservedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "equipment-state.jsonl");
                string line = JsonConvert.SerializeObject(item, PersistenceJsonSettings)
                    + Environment.NewLine;
                File.AppendAllText(path, line, Utf8NoBom);
                LastPersistenceError = null;
            }
            catch (Exception ex)
            {
                // 历史持久化属于诊断旁路，失败不得改变设备控制状态。
                LastPersistenceError = ex.Message;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Task pending;
            lock (persistenceSync) pending = persistenceTail;
            try
            {
                pending.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 关闭链继续执行，并由 LastPersistenceError 暴露旁路故障。
            }
        }
    }
}
