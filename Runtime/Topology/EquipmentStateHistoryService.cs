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
        private const int MaximumReportedSequenceGaps = 100;
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
        private readonly List<EquipmentStateSequenceGap> knownSequenceGaps =
            new List<EquipmentStateSequenceGap>();
        private readonly int maximumRetainedEvents;
        private readonly TimeSpan retention;
        private readonly string persistenceRoot;
        private readonly object persistenceSync = new object();
        private Task persistenceTail = Task.CompletedTask;
        private string lastWriteError;
        private string lastRecoveryError;
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
            RecoverPersistedHistory();
        }

        public event EventHandler<EquipmentStateHistoryEvent> EventAppended;

        public long Revision => Interlocked.Read(ref nextSequence);
        public string LastRecoveryError => Volatile.Read(ref lastRecoveryError);
        public string LastPersistenceError
        {
            get
            {
                string writeError = Volatile.Read(ref lastWriteError);
                string recoveryError = LastRecoveryError;
                if (string.IsNullOrWhiteSpace(writeError)) return recoveryError;
                if (string.IsNullOrWhiteSpace(recoveryError)) return writeError;
                return writeError + "；" + recoveryError;
            }
        }

        private void RecoverPersistedHistory()
        {
            if (string.IsNullOrWhiteSpace(persistenceRoot)
                || !Directory.Exists(persistenceRoot))
            {
                return;
            }

            var recoveredBySequence =
                new Dictionary<long, EquipmentStateHistoryEvent>();
            var errorSamples = new List<string>();
            int skippedLines = 0;
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(
                        persistenceRoot,
                        "equipment-state.jsonl",
                        SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Volatile.Write(
                    ref lastRecoveryError,
                    "设备状态历史恢复失败，已按空历史启动：" + ex.Message);
                return;
            }

            foreach (string path in paths)
            {
                int lineNumber = 0;
                try
                {
                    foreach (string line in File.ReadLines(path, Utf8NoBom))
                    {
                        lineNumber++;
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            EquipmentStateHistoryEvent item =
                                JsonConvert.DeserializeObject<EquipmentStateHistoryEvent>(
                                    line,
                                    PersistenceJsonSettings);
                            if (item == null || item.Sequence <= 0)
                            {
                                throw new JsonSerializationException(
                                    "缺少有效的 sequence。");
                            }
                            if (item.ObservedAtUtc == default(DateTime))
                            {
                                if (item.ReceivedAtUtc == default(DateTime))
                                {
                                    throw new JsonSerializationException(
                                        "缺少有效的 observedAtUtc。");
                                }
                                item.ObservedAtUtc = item.ReceivedAtUtc;
                            }
                            if (item.ReceivedAtUtc == default(DateTime))
                            {
                                item.ReceivedAtUtc = item.ObservedAtUtc;
                            }
                            Normalize(item);
                            if (recoveredBySequence.ContainsKey(item.Sequence))
                            {
                                skippedLines++;
                                AddRecoveryErrorSample(
                                    errorSamples,
                                    path,
                                    lineNumber,
                                    "sequence 重复，采用后出现的记录。");
                            }
                            recoveredBySequence[item.Sequence] = item;
                        }
                        catch (Exception ex)
                        {
                            skippedLines++;
                            AddRecoveryErrorSample(
                                errorSamples, path, lineNumber, ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    skippedLines++;
                    AddRecoveryErrorSample(errorSamples, path, lineNumber, ex.Message);
                }
            }

            DateTime nowUtc = DateTime.UtcNow;
            lock (syncRoot)
            {
                List<EquipmentStateHistoryEvent> recovered = recoveredBySequence.Values
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
                long previousSequence = 0;
                foreach (EquipmentStateHistoryEvent item in recovered)
                {
                    if (item.Sequence > previousSequence + 1)
                    {
                        knownSequenceGaps.Add(new EquipmentStateSequenceGap
                        {
                            FirstMissingSequence = previousSequence + 1,
                            LastMissingSequence = item.Sequence - 1
                        });
                    }
                    events.Enqueue(item);
                    nextSequence = Math.Max(nextSequence, item.Sequence);
                    currentTopologyRevision = item.TopologyRevision;
                    ApplyProjection(current, item);
                    PruneLocked(nowUtc);
                    previousSequence = item.Sequence;
                }
            }
            if (skippedLines > 0 || knownSequenceGaps.Count > 0)
            {
                string detail = string.Join("；", errorSamples);
                string gapDetail = knownSequenceGaps.Count == 0
                    ? string.Empty
                    : $"；检测到 {knownSequenceGaps.Count} 个 sequence 缺口";
                Volatile.Write(
                    ref lastRecoveryError,
                    $"设备状态历史恢复跳过 {skippedLines} 条损坏或重复记录"
                    + (string.IsNullOrWhiteSpace(detail) ? string.Empty : "：" + detail)
                    + gapDetail + "。");
            }
        }

        private static void AddRecoveryErrorSample(
            ICollection<string> samples,
            string path,
            int lineNumber,
            string error)
        {
            if (samples.Count >= 3) return;
            string location = Path.GetFileName(Path.GetDirectoryName(path))
                + "/" + Path.GetFileName(path)
                + ":" + Math.Max(1, lineNumber).ToString(CultureInfo.InvariantCulture);
            samples.Add(location + " " + (error ?? "未知错误"));
        }

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

        /// <summary>
        /// 按全局序列读取状态历史。有游标时从最早的未读事实开始向前分页；
        /// 未提供游标时返回最近窗口，便于首次读取直接取得当前上下文。
        /// </summary>
        public EquipmentStateHistoryWindow GetWindowAfterSequence(
            long? afterSequence,
            int maximumEvents)
        {
            ValidateMaximumEvents(maximumEvents);
            lock (syncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                PruneLocked(nowUtc);
                List<EquipmentStateHistoryEvent> eligible = afterSequence.HasValue
                    ? events.Where(item => item.Sequence > afterSequence.Value).ToList()
                    : events.ToList();
                bool truncated = eligible.Count > maximumEvents;
                IEnumerable<EquipmentStateHistoryEvent> page = afterSequence.HasValue
                    ? eligible.Take(maximumEvents)
                    : eligible.Skip(Math.Max(0, eligible.Count - maximumEvents));
                return BuildWindowLocked(
                    page.Select(CloneEvent).ToList(),
                    truncated,
                    nowUtc);
            }
        }

        public EquipmentStateHistoryWindow GetWindow(
            DateTime? startUtc,
            DateTime? endUtc,
            int maximumEvents,
            string nodeId)
        {
            ValidateMaximumEvents(maximumEvents);
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
                return BuildWindowLocked(selected, truncated, nowUtc);
            }
        }

        private static void ValidateMaximumEvents(int maximumEvents)
        {
            if (maximumEvents < 1 || maximumEvents > 5000)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEvents));
            }
        }

        private EquipmentStateHistoryWindow BuildWindowLocked(
            List<EquipmentStateHistoryEvent> selected,
            bool truncated,
            DateTime nowUtc)
        {
            long firstSequence = selected.Count > 0
                ? selected[0].Sequence
                : nextSequence + 1;
            Dictionary<string, EquipmentNodeStateProjection> baseline =
                CloneProjection(retainedBaseline);
            DateTime baselineTime = selected.Count > 0
                ? selected[0].ObservedAtUtc
                : nowUtc;
            long baselineTopologyRevision = retainedBaselineTopologyRevision;
            long baselineSequence = retainedBaselineSequence;
            foreach (EquipmentStateHistoryEvent item in events)
            {
                if (item.Sequence >= firstSequence) break;
                ApplyProjection(baseline, item);
                baselineTime = item.ObservedAtUtc;
                baselineTopologyRevision = item.TopologyRevision;
                baselineSequence = item.Sequence;
            }
            long pageEndSequence = selected.Count > 0
                ? selected[selected.Count - 1].Sequence
                : baselineSequence;
            List<EquipmentStateSequenceGap> pageGaps = knownSequenceGaps
                .Where(gap => gap.LastMissingSequence > baselineSequence
                    && gap.FirstMissingSequence <= pageEndSequence)
                .Take(MaximumReportedSequenceGaps + 1)
                .Select(CloneGap)
                .ToList();
            bool gapsTruncated = pageGaps.Count > MaximumReportedSequenceGaps;
            if (gapsTruncated)
            {
                pageGaps.RemoveAt(pageGaps.Count - 1);
            }
            return new EquipmentStateHistoryWindow
            {
                EarliestAvailableSequence = events.Count > 0
                    ? events.Peek().Sequence
                    : retainedBaselineSequence,
                LatestSequence = nextSequence,
                Truncated = truncated,
                BaselineComplete = !knownSequenceGaps.Any(gap =>
                    gap.FirstMissingSequence <= baselineSequence),
                SequenceGaps = pageGaps,
                SequenceGapsTruncated = gapsTruncated,
                Baseline = BuildSnapshotLocked(
                    baselineSequence,
                    baselineTime,
                    baselineTopologyRevision,
                    baseline),
                Events = selected
            };
        }

        private static EquipmentStateSequenceGap CloneGap(EquipmentStateSequenceGap source)
        {
            return new EquipmentStateSequenceGap
            {
                FirstMissingSequence = source.FirstMissingSequence,
                LastMissingSequence = source.LastMissingSequence
            };
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
                ProcessName = source.ProcessName,
                StepId = source.StepId,
                StepIndex = source.StepIndex,
                OperationId = source.OperationId,
                OperationIndex = source.OperationIndex,
                OperationType = source.OperationType,
                OperationName = source.OperationName,
                ProcessState = source.ProcessState,
                Outcome = source.Outcome,
                TerminationReason = source.TerminationReason,
                PreviewId = source.PreviewId,
                SkillId = source.SkillId,
                ActionId = source.ActionId,
                ExpectedOutcome = source.ExpectedOutcome,
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
            item.ProcessName = item.ProcessName ?? string.Empty;
            item.StepId = item.StepId ?? string.Empty;
            item.OperationId = item.OperationId ?? string.Empty;
            item.OperationType = item.OperationType ?? string.Empty;
            item.OperationName = item.OperationName ?? string.Empty;
            item.ProcessState = item.ProcessState ?? string.Empty;
            item.Outcome = item.Outcome ?? string.Empty;
            item.TerminationReason = item.TerminationReason ?? string.Empty;
            item.PreviewId = item.PreviewId ?? string.Empty;
            item.SkillId = item.SkillId ?? string.Empty;
            item.ActionId = item.ActionId ?? string.Empty;
            item.ExpectedOutcome = item.ExpectedOutcome ?? string.Empty;
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
                Volatile.Write(ref lastWriteError, null);
            }
            catch (Exception ex)
            {
                // 历史持久化属于诊断旁路，失败不得改变设备控制状态。
                Volatile.Write(ref lastWriteError, ex.Message);
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
