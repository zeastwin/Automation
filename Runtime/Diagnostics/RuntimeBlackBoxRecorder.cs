using Newtonsoft.Json;
// 模块：运行时 / 诊断。
// 职责范围：记录并投影断点、性能、审计、异常、日志缓冲和运行黑匣子事实。

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Automation
{
    /// <summary>
    /// 运行黑匣子按流程保存有界状态变化，发生指令失败时异步冻结完整事故窗口。
    /// 正常高速运行路径不写盘，也不保存逐次指令执行报文。
    /// </summary>
    internal sealed class RuntimeBlackBoxRecorder : IDisposable
    {
        private const int MaximumProcessEventCount = 8192;
        private const int MaximumPlatformEventCount = 2048;
        private const int MaximumAiEvidenceEventCount = 300;
        private const int MaximumDefaultTimelineEventCount = 200;
        private const int MaximumTextLength = 512;
        private static readonly TimeSpan ProcessRetention = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan PlatformRetention = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DefaultTimelineWindow = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan RecentPlatformWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan IncidentPreWindow = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan IncidentPostWindow = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan IncidentPlatformWindow = TimeSpan.FromSeconds(30);
        private static readonly string IncidentRoot = Path.Combine(@"D:\AutomationLogs", "Incident");
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly ProcessEngine engine;
        private readonly ValueConfigStore valueStore;
        private readonly object syncRoot = new object();
        private readonly Dictionary<Guid, Queue<RuntimeBlackBoxEvent>> processEvents =
            new Dictionary<Guid, Queue<RuntimeBlackBoxEvent>>();
        private readonly Queue<RuntimeBlackBoxEvent> platformEvents = new Queue<RuntimeBlackBoxEvent>();
        private readonly Dictionary<Guid, ProcessPosition> lastPositions = new Dictionary<Guid, ProcessPosition>();
        private readonly Dictionary<Guid, IncidentMarker> currentIncidents = new Dictionary<Guid, IncidentMarker>();
        private readonly Dictionary<string, IncidentMarker> pendingIncidents =
            new Dictionary<string, IncidentMarker>(StringComparer.Ordinal);
        private readonly object persistenceSync = new object();
        private readonly CancellationTokenSource disposeCts = new CancellationTokenSource();
        private Task persistenceTail = Task.CompletedTask;
        private long nextSequence;
        private int disposed;

        public RuntimeBlackBoxRecorder(ProcessEngine engine, ValueConfigStore valueStore)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.valueStore = valueStore ?? throw new ArgumentNullException(nameof(valueStore));
            engine.SnapshotChanged += HandleSnapshotChanged;
            engine.ProcessStarted += HandleProcessStarted;
            engine.OperationFailed += HandleOperationFailed;
            engine.ProcessCompleted += HandleProcessCompleted;
            engine.CycleTimeMeasured += HandleCycleTimeMeasured;
            valueStore.ValueChanged += HandleValueChanged;
        }

        public long Revision => Interlocked.Read(ref nextSequence);

        /// <summary>生成初始化前的V2空证据包，避免Bridge维护另一份近似契约。</summary>
        internal static JObject BuildUnavailableEvidencePackage(int procIndex)
        {
            return new JObject
            {
                ["schemaVersion"] = 2,
                ["packageType"] = "ai_evidence",
                ["procIndex"] = procIndex,
                ["procId"] = string.Empty,
                ["incidentId"] = string.Empty,
                ["rawRelevantEventCount"] = 0,
                ["capturedEventCount"] = 0,
                ["processBufferEventCount"] = 0,
                ["platformBufferEventCount"] = 0,
                ["selectionMode"] = "unavailable",
                ["retentionPolicy"] = BuildRetentionPolicy(),
                ["events"] = new JArray(),
                ["evidenceLimits"] = new JArray("运行黑匣子尚未初始化。")
            };
        }

        internal static JObject BuildUnavailableEvidencePage(int procIndex, int offset, int limit)
        {
            JObject package = BuildUnavailableEvidencePackage(procIndex);
            package["eligibleEventCount"] = 0;
            package["evidenceOffset"] = offset;
            package["evidenceLimit"] = limit;
            package["hasMoreEvents"] = false;
            package["nextEvidenceOffset"] = JValue.CreateNull();
            return package;
        }

        /// <summary>生成供 MCP/AI 使用的关键证据包，最多返回300条事件。</summary>
        public JObject BuildEvidencePackage(int procIndex)
        {
            lock (syncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                PruneAllLocked(nowUtc);
                Guid procId = ResolveProcessId(procIndex);
                currentIncidents.TryGetValue(procId, out IncidentMarker marker);
                List<RuntimeBlackBoxEvent> rawEvents = SelectRelevantEventsLocked(
                    procId, marker, nowUtc, includeFullRecentBuffer: false);
                List<RuntimeBlackBoxEvent> selected = SelectAiEvidence(
                    rawEvents, marker, MaximumAiEvidenceEventCount);
                return BuildPackageLocked(
                    "ai_evidence",
                    procIndex,
                    procId,
                    marker,
                    nowUtc,
                    rawEvents,
                    selected,
                    selected.Count < rawEvents.Count ? "critical_and_even_sample" : "all_relevant_events");
            }
        }

        /// <summary>生成供 MCP/AI 分页读取的关键证据，候选集仍使用同一套300条关键证据选择规则。</summary>
        public JObject BuildEvidencePage(int procIndex, int offset, int limit)
        {
            lock (syncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                PruneAllLocked(nowUtc);
                Guid procId = ResolveProcessId(procIndex);
                currentIncidents.TryGetValue(procId, out IncidentMarker marker);
                List<RuntimeBlackBoxEvent> rawEvents = SelectRelevantEventsLocked(
                    procId, marker, nowUtc, includeFullRecentBuffer: false);
                List<RuntimeBlackBoxEvent> eligible = SelectAiEvidence(
                    rawEvents, marker, MaximumAiEvidenceEventCount);
                List<RuntimeBlackBoxEvent> page = eligible
                    .Skip(offset)
                    .Take(limit)
                    .ToList();
                JObject package = BuildPackageLocked(
                    "ai_evidence",
                    procIndex,
                    procId,
                    marker,
                    nowUtc,
                    rawEvents,
                    page,
                    eligible.Count < rawEvents.Count
                        ? "critical_and_even_sample_paged"
                        : "all_relevant_events_paged");
                package["eligibleEventCount"] = eligible.Count;
                package["evidenceOffset"] = offset;
                package["evidenceLimit"] = limit;
                package["hasMoreEvents"] = (long)offset + page.Count < eligible.Count;
                package["nextEvidenceOffset"] = (long)offset + page.Count < eligible.Count
                    ? (JToken)((long)offset + page.Count)
                    : JValue.CreateNull();
                return package;
            }
        }

        /// <summary>生成诊断窗口时间线；默认最多200条，展开时返回完整事故窗口。</summary>
        public JObject BuildTimelinePackage(int procIndex, bool includeFullIncident)
        {
            lock (syncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                PruneAllLocked(nowUtc);
                Guid procId = ResolveProcessId(procIndex);
                currentIncidents.TryGetValue(procId, out IncidentMarker marker);
                List<RuntimeBlackBoxEvent> rawEvents = SelectRelevantEventsLocked(
                    procId, marker, nowUtc, includeFullIncident);
                List<RuntimeBlackBoxEvent> selected;
                string selectionMode;
                if (includeFullIncident)
                {
                    selected = rawEvents;
                    selectionMode = marker == null ? "full_retained_buffer" : "full_incident_window";
                }
                else if (marker != null)
                {
                    selected = SelectAiEvidence(rawEvents, marker, MaximumDefaultTimelineEventCount);
                    selectionMode = selected.Count < rawEvents.Count
                        ? "critical_and_even_sample"
                        : "all_relevant_events";
                }
                else
                {
                    selected = rawEvents.OrderByDescending(item => item.Sequence)
                        .Take(MaximumDefaultTimelineEventCount)
                        .OrderBy(item => item.Sequence)
                        .ToList();
                    selectionMode = selected.Count < rawEvents.Count ? "latest_events" : "all_recent_events";
                }
                return BuildPackageLocked(
                    "diagnostic_timeline",
                    procIndex,
                    procId,
                    marker,
                    nowUtc,
                    rawEvents,
                    selected,
                    selectionMode);
            }
        }

        public void RecordExternalEvent(string eventName, string source, string message, bool isAlarm)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            AddEvent(new RuntimeBlackBoxEvent
            {
                EventName = NormalizeText(eventName),
                Source = NormalizeText(source),
                Message = NormalizeText(message),
                Outcome = isAlarm ? "alarm" : "observed",
                ProcIndex = -1
            });
        }

        private void HandleProcessStarted(ProcessRunStartedSnapshot started)
        {
            lock (syncRoot)
            {
                currentIncidents.Remove(started.ProcId);
                lastPositions.Remove(started.ProcId);
            }
            AddEvent(new RuntimeBlackBoxEvent
            {
                EventName = "process.started",
                ProcIndex = started.ProcIndex,
                ProcId = started.ProcId,
                Outcome = "started",
                RunId = started.RunId,
                Message = $"runId={started.RunId:D}"
            });
        }

        private void HandleCycleTimeMeasured(CycleTimeProbeSample sample)
        {
            if (sample == null) return;
            AddEvent(new RuntimeBlackBoxEvent
            {
                EventName = "process.ct.measured",
                ProcIndex = sample.ProcIndex,
                ProcId = sample.ProcId,
                RunId = sample.RunId,
                Source = NormalizeText(sample.TaskKey),
                ResourceName = NormalizeText(sample.SegmentName),
                Outcome = "measured",
                SegmentIndex = sample.SegmentIndex,
                CycleStarted = sample.CycleStarted,
                SegmentMilliseconds = sample.SegmentMilliseconds,
                CycleMilliseconds = sample.CycleMilliseconds,
                TimeUtc = sample.RecordedAtUtc
            });
        }

        private void HandleSnapshotChanged(EngineSnapshot snapshot)
        {
            if (snapshot == null || Volatile.Read(ref disposed) != 0) return;
            lock (syncRoot)
            {
                var current = new ProcessPosition(snapshot);
                if (lastPositions.TryGetValue(snapshot.ProcId, out ProcessPosition previous)
                    && previous.Equals(current))
                {
                    return;
                }
                lastPositions[snapshot.ProcId] = current;
                AddEventLocked(new RuntimeBlackBoxEvent
                {
                    EventName = "process.position.changed",
                    ProcIndex = snapshot.ProcIndex,
                    ProcId = snapshot.ProcId,
                    ProcName = NormalizeText(snapshot.ProcName),
                    State = snapshot.State.ToString(),
                    StepIndex = snapshot.StepIndex,
                    OpIndex = snapshot.OpIndex,
                    Message = NormalizeText(snapshot.AlarmMessage),
                    Outcome = snapshot.IsAlarm ? "alarm" : "observed",
                    PublishedRevision = snapshot.PublishedRevision,
                    AppliedRevision = snapshot.AppliedRevision,
                    TerminationReason = snapshot.TerminationReason.ToString()
                });
            }
        }

        private void HandleOperationFailed(OperationFailureEntry entry)
        {
            RuntimeBlackBoxEvent failure = new RuntimeBlackBoxEvent
            {
                EventName = "operation.failed",
                ProcIndex = entry.ProcIndex,
                ProcId = entry.ProcId,
                StepIndex = entry.StepIndex,
                OpIndex = entry.OpIndex,
                OperationId = entry.OperationId,
                OperationType = NormalizeText(entry.OperationType),
                OperationName = NormalizeText(entry.OperationName),
                Message = NormalizeText(entry.AlarmMessage),
                Outcome = "failed",
                DurationMicroseconds = entry.DurationMeasured
                    ? (double?)(entry.ElapsedTicks * 1000000.0 / Stopwatch.Frequency)
                    : null
            };
            long failureSequence = AddEvent(failure);
            if (failureSequence <= 0) return;

            IncidentMarker marker;
            lock (syncRoot)
            {
                Guid procId = entry.ProcId == Guid.Empty ? ResolveProcessId(entry.ProcIndex) : entry.ProcId;
                if (currentIncidents.ContainsKey(procId)) return;
                marker = new IncidentMarker
                {
                    IncidentId = Guid.NewGuid().ToString("N"),
                    ProcIndex = entry.ProcIndex,
                    ProcId = procId,
                    FailureSequence = failureSequence,
                    FailureTimeUtc = failure.TimeUtc
                };
                currentIncidents[procId] = marker;
                pendingIncidents[marker.IncidentId] = marker;
            }
            QueueIncidentPersistence(marker, "failure_captured");
            ScheduleIncidentFinalization(marker);
        }

        private void HandleProcessCompleted(ProcessRunAuditSnapshot snapshot)
        {
            AddEvent(new RuntimeBlackBoxEvent
            {
                EventName = "process.completed",
                ProcIndex = snapshot.ProcIndex,
                ProcId = snapshot.ProcId,
                RunId = snapshot.RunId,
                Outcome = snapshot.TerminationReason.ToString(),
                Message = $"runId={snapshot.RunId:D}, operationCount={snapshot.OperationCount}, failedCount={snapshot.FailedCount}, retryCount={snapshot.RetryCount}"
            });
        }

        private void HandleValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (e == null) return;
            int procIndex = ResolveProcessIndex(e.OwnerProcId, e.Source);
            AddEvent(new RuntimeBlackBoxEvent
            {
                EventName = "variable.changed",
                ProcIndex = procIndex,
                ProcId = e.OwnerProcId ?? ResolveProcessId(procIndex),
                Source = NormalizeText(e.Source),
                ResourceId = e.Id == Guid.Empty ? string.Empty : e.Id.ToString("D"),
                ResourceName = NormalizeText(e.Name),
                OldValue = NormalizeValue(e.Name, e.OldValue),
                NewValue = NormalizeValue(e.Name, e.NewValue),
                Outcome = "changed",
                TimeUtc = e.ChangedAt == default(DateTime)
                    ? DateTime.UtcNow
                    : e.ChangedAt.ToUniversalTime()
            });
        }

        private int ResolveProcessIndex(Guid? ownerProcId, string source)
        {
            if (ownerProcId.HasValue && ownerProcId.Value != Guid.Empty)
            {
                IList<Proc> processes = engine.Context?.Procs;
                if (processes != null)
                {
                    for (int index = 0; index < processes.Count; index++)
                    {
                        if (processes[index]?.head?.Id == ownerProcId.Value) return index;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(source))
            {
                int separator = source.IndexOf('-');
                string first = separator < 0 ? source : source.Substring(0, separator);
                if (int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    && parsed >= 0)
                {
                    return parsed;
                }
            }
            return -1;
        }

        private long AddEvent(RuntimeBlackBoxEvent item)
        {
            if (item == null || Volatile.Read(ref disposed) != 0) return 0;
            lock (syncRoot) return AddEventLocked(item);
        }

        private long AddEventLocked(RuntimeBlackBoxEvent item)
        {
            item.Sequence = ++nextSequence;
            if (item.TimeUtc == default(DateTime)) item.TimeUtc = DateTime.UtcNow;
            if (item.ProcIndex >= 0)
            {
                if (item.ProcId == Guid.Empty) item.ProcId = ResolveProcessId(item.ProcIndex);
                if (!processEvents.TryGetValue(item.ProcId, out Queue<RuntimeBlackBoxEvent> queue))
                {
                    queue = new Queue<RuntimeBlackBoxEvent>();
                    processEvents[item.ProcId] = queue;
                }
                queue.Enqueue(item);
                PruneQueue(queue, item.TimeUtc - ProcessRetention, MaximumProcessEventCount);
            }
            else
            {
                platformEvents.Enqueue(item);
                PruneQueue(platformEvents, item.TimeUtc - PlatformRetention, MaximumPlatformEventCount);
            }
            if ((item.Sequence & 127) == 0) PruneAllLocked(item.TimeUtc);
            return item.Sequence;
        }

        private void PruneAllLocked(DateTime nowUtc)
        {
            DateTime processCutoff = nowUtc - ProcessRetention;
            foreach (Guid procId in processEvents.Keys.ToArray())
            {
                Queue<RuntimeBlackBoxEvent> queue = processEvents[procId];
                PruneQueue(queue, processCutoff, MaximumProcessEventCount);
                if (queue.Count == 0) processEvents.Remove(procId);
            }
            PruneQueue(platformEvents, nowUtc - PlatformRetention, MaximumPlatformEventCount);
        }

        private static void PruneQueue(
            Queue<RuntimeBlackBoxEvent> queue,
            DateTime cutoffUtc,
            int maximumCount)
        {
            while (queue.Count > 0
                && (queue.Count > maximumCount || queue.Peek().TimeUtc < cutoffUtc))
            {
                queue.Dequeue();
            }
        }

        private List<RuntimeBlackBoxEvent> SelectRelevantEventsLocked(
            Guid procId,
            IncidentMarker marker,
            DateTime nowUtc,
            bool includeFullRecentBuffer)
        {
            DateTime processStartUtc;
            DateTime processEndUtc;
            DateTime platformStartUtc;
            DateTime platformEndUtc;
            if (marker != null)
            {
                processStartUtc = marker.FailureTimeUtc - IncidentPreWindow;
                processEndUtc = marker.FailureTimeUtc + IncidentPostWindow;
                platformStartUtc = marker.FailureTimeUtc - IncidentPlatformWindow;
                platformEndUtc = marker.FailureTimeUtc + IncidentPlatformWindow;
            }
            else
            {
                processStartUtc = nowUtc - (includeFullRecentBuffer ? ProcessRetention : DefaultTimelineWindow);
                processEndUtc = nowUtc;
                platformStartUtc = nowUtc - (includeFullRecentBuffer ? PlatformRetention : RecentPlatformWindow);
                platformEndUtc = nowUtc;
            }

            IEnumerable<RuntimeBlackBoxEvent> processSource =
                processEvents.TryGetValue(procId, out Queue<RuntimeBlackBoxEvent> queue)
                    ? queue
                    : Enumerable.Empty<RuntimeBlackBoxEvent>();
            return processSource
                .Where(item => item.TimeUtc >= processStartUtc && item.TimeUtc <= processEndUtc)
                .Concat(platformEvents.Where(item =>
                    item.TimeUtc >= platformStartUtc && item.TimeUtc <= platformEndUtc))
                .OrderBy(item => item.Sequence)
                .ToList();
        }

        private static List<RuntimeBlackBoxEvent> SelectAiEvidence(
            List<RuntimeBlackBoxEvent> source,
            IncidentMarker marker,
            int maximumCount)
        {
            if (source.Count <= maximumCount) return source.ToList();
            var selected = new Dictionary<long, RuntimeBlackBoxEvent>();
            List<RuntimeBlackBoxEvent> failures = source
                .Where(item => string.Equals(item.EventName, "operation.failed", StringComparison.Ordinal))
                .ToList();
            foreach (RuntimeBlackBoxEvent item in SampleEvenly(failures, Math.Min(failures.Count, maximumCount)))
            {
                selected[item.Sequence] = item;
            }
            if (marker != null)
            {
                RuntimeBlackBoxEvent centered = source.FirstOrDefault(item => item.Sequence == marker.FailureSequence);
                if (centered != null) selected[centered.Sequence] = centered;
            }

            List<RuntimeBlackBoxEvent> critical = source.Where(item =>
                    !string.Equals(item.EventName, "process.position.changed", StringComparison.Ordinal)
                    || string.Equals(item.Outcome, "alarm", StringComparison.Ordinal)
                    || string.Equals(item.Outcome, "failed", StringComparison.Ordinal))
                .Where(item => !selected.ContainsKey(item.Sequence))
                .ToList();
            int criticalSlots = Math.Max(0, maximumCount - selected.Count);
            foreach (RuntimeBlackBoxEvent item in SampleEvenly(critical, Math.Min(critical.Count, criticalSlots)))
            {
                selected[item.Sequence] = item;
            }

            List<RuntimeBlackBoxEvent> ordinary = source
                .Where(item => !selected.ContainsKey(item.Sequence))
                .ToList();
            int ordinarySlots = Math.Max(0, maximumCount - selected.Count);
            foreach (RuntimeBlackBoxEvent item in SampleEvenly(ordinary, ordinarySlots))
            {
                selected[item.Sequence] = item;
            }
            return selected.Values.OrderBy(item => item.Sequence).Take(maximumCount).ToList();
        }

        private static IEnumerable<RuntimeBlackBoxEvent> SampleEvenly(
            IReadOnlyList<RuntimeBlackBoxEvent> source,
            int count)
        {
            if (source == null || source.Count == 0 || count <= 0) return Enumerable.Empty<RuntimeBlackBoxEvent>();
            if (count >= source.Count) return source;
            if (count == 1) return new[] { source[source.Count - 1] };
            var result = new List<RuntimeBlackBoxEvent>(count);
            for (int index = 0; index < count; index++)
            {
                int sourceIndex = (int)Math.Round(index * (source.Count - 1d) / (count - 1d));
                result.Add(source[sourceIndex]);
            }
            return result;
        }

        private JObject BuildPackageLocked(
            string packageType,
            int procIndex,
            Guid procId,
            IncidentMarker marker,
            DateTime nowUtc,
            IReadOnlyCollection<RuntimeBlackBoxEvent> rawEvents,
            IReadOnlyCollection<RuntimeBlackBoxEvent> selectedEvents,
            string selectionMode)
        {
            int processBufferCount = processEvents.TryGetValue(procId, out Queue<RuntimeBlackBoxEvent> queue)
                ? queue.Count
                : 0;
            DateTime? windowStartUtc = rawEvents.Count > 0 ? rawEvents.Min(item => item.TimeUtc) : (DateTime?)null;
            DateTime? windowEndUtc = rawEvents.Count > 0 ? rawEvents.Max(item => item.TimeUtc) : (DateTime?)null;
            return new JObject
            {
                ["schemaVersion"] = 2,
                ["packageType"] = packageType,
                ["procIndex"] = procIndex,
                ["procId"] = procId == Guid.Empty ? string.Empty : procId.ToString("D"),
                ["incidentId"] = marker?.IncidentId ?? string.Empty,
                ["failureSequence"] = marker == null ? JValue.CreateNull() : (JToken)marker.FailureSequence,
                ["failureTimeUtc"] = marker == null
                    ? JValue.CreateNull()
                    : (JToken)marker.FailureTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                ["completePostFailureWindow"] = marker == null
                    ? JValue.CreateNull()
                    : (JToken)(nowUtc >= marker.FailureTimeUtc + IncidentPostWindow),
                ["rawRelevantEventCount"] = rawEvents.Count,
                ["capturedEventCount"] = selectedEvents.Count,
                ["processBufferEventCount"] = processBufferCount,
                ["platformBufferEventCount"] = platformEvents.Count,
                ["selectionMode"] = selectionMode,
                ["bufferRevision"] = nextSequence,
                ["windowStartUtc"] = windowStartUtc.HasValue
                    ? (JToken)windowStartUtc.Value.ToString("O", CultureInfo.InvariantCulture)
                    : JValue.CreateNull(),
                ["windowEndUtc"] = windowEndUtc.HasValue
                    ? (JToken)windowEndUtc.Value.ToString("O", CultureInfo.InvariantCulture)
                    : JValue.CreateNull(),
                ["retentionPolicy"] = BuildRetentionPolicy(),
                ["events"] = new JArray(selectedEvents.Select(BuildEventObject)),
                ["evidenceLimits"] = new JArray(
                    "流程位置来自50ms节流后的变化快照，不等同于逐指令追踪。",
                    "变量事件只包含平台已启用监控的变量。",
                    "故障事故包保留目标流程故障前3分钟、后60秒；平台级PLC和通讯事件按故障前后30秒关联。",
                    "IO、轴、PLC和通讯的历史值仅在对应运行事件已产生时存在；诊断时仍需读取当前状态。")
            };
        }

        private static JObject BuildRetentionPolicy()
        {
            return new JObject
            {
                ["processRetentionSeconds"] = (int)ProcessRetention.TotalSeconds,
                ["maximumEventsPerProcess"] = MaximumProcessEventCount,
                ["platformRetentionSeconds"] = (int)PlatformRetention.TotalSeconds,
                ["maximumPlatformEvents"] = MaximumPlatformEventCount,
                ["incidentPreSeconds"] = (int)IncidentPreWindow.TotalSeconds,
                ["incidentPostSeconds"] = (int)IncidentPostWindow.TotalSeconds,
                ["incidentPlatformCorrelationSeconds"] = (int)IncidentPlatformWindow.TotalSeconds,
                ["maximumAiEvidenceEvents"] = MaximumAiEvidenceEventCount,
                ["maximumDefaultTimelineEvents"] = MaximumDefaultTimelineEventCount
            };
        }

        private JObject BuildIncidentArchive(IncidentMarker marker, string captureReason)
        {
            lock (syncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                PruneAllLocked(nowUtc);
                List<RuntimeBlackBoxEvent> rawEvents = SelectRelevantEventsLocked(
                    marker.ProcId, marker, nowUtc, includeFullRecentBuffer: true);
                JObject package = BuildPackageLocked(
                    "incident_archive",
                    marker.ProcIndex,
                    marker.ProcId,
                    marker,
                    nowUtc,
                    rawEvents,
                    rawEvents,
                    "full_incident_window");
                package["captureReason"] = captureReason ?? string.Empty;
                package["capturedAtUtc"] = nowUtc.ToString("O", CultureInfo.InvariantCulture);
                package["expectedPostWindowEndUtc"] = (marker.FailureTimeUtc + IncidentPostWindow)
                    .ToString("O", CultureInfo.InvariantCulture);
                return package;
            }
        }

        private void QueueIncidentPersistence(IncidentMarker marker, string captureReason)
        {
            JObject evidence = BuildIncidentArchive(marker, captureReason);
            QueueIncidentPersistence(marker, evidence);
        }

        private void QueueIncidentPersistence(IncidentMarker marker, JObject evidence)
        {
            lock (persistenceSync)
            {
                persistenceTail = persistenceTail.ContinueWith(
                    ignored => PersistIncident(marker, evidence),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        private void ScheduleIncidentFinalization(IncidentMarker marker)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(IncidentPostWindow, disposeCts.Token).ConfigureAwait(false);
                    JObject evidence = BuildIncidentArchive(marker, "post_window_completed");
                    QueueIncidentPersistence(marker, evidence);
                    lock (syncRoot) pendingIncidents.Remove(marker.IncidentId);
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    // 事故旁路失败不能改变流程运行状态。
                }
            });
        }

        private static void PersistIncident(IncidentMarker marker, JObject evidence)
        {
            try
            {
                string directory = Path.Combine(
                    IncidentRoot,
                    marker.FailureTimeUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, marker.IncidentId + ".json");
                string temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, evidence.ToString(Formatting.Indented), Utf8NoBom);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            catch
            {
                // 黑匣子持久化属于诊断旁路，失败不能影响流程运行。
            }
        }

        private static JObject BuildEventObject(RuntimeBlackBoxEvent item)
        {
            var result = new JObject
            {
                ["seq"] = item.Sequence,
                ["timeUtc"] = item.TimeUtc.ToString("O", CultureInfo.InvariantCulture),
                ["eventName"] = item.EventName ?? string.Empty,
                ["procIndex"] = item.ProcIndex >= 0 ? (JToken)item.ProcIndex : JValue.CreateNull(),
                ["outcome"] = item.Outcome ?? string.Empty
            };
            AddText(result, "procId", item.ProcId == Guid.Empty ? null : item.ProcId.ToString("D"));
            AddText(result, "runId", item.RunId == Guid.Empty ? null : item.RunId.ToString("D"));
            AddText(result, "procName", item.ProcName);
            AddText(result, "state", item.State);
            AddNumber(result, "stepIndex", item.StepIndex);
            AddNumber(result, "opIndex", item.OpIndex);
            AddText(result, "operationId", item.OperationId == Guid.Empty ? null : item.OperationId.ToString("D"));
            AddText(result, "operationType", item.OperationType);
            AddText(result, "operationName", item.OperationName);
            AddText(result, "source", item.Source);
            AddText(result, "resourceId", item.ResourceId);
            AddText(result, "resourceName", item.ResourceName);
            AddText(result, "oldValue", item.OldValue);
            AddText(result, "newValue", item.NewValue);
            AddText(result, "message", item.Message);
            AddText(result, "terminationReason", item.TerminationReason);
            if (item.PublishedRevision.HasValue) result["publishedRevision"] = item.PublishedRevision.Value;
            if (item.AppliedRevision.HasValue) result["appliedRevision"] = item.AppliedRevision.Value;
            if (item.DurationMicroseconds.HasValue) result["durationMicroseconds"] = item.DurationMicroseconds.Value;
            if (item.SegmentIndex.HasValue) result["segmentIndex"] = item.SegmentIndex.Value;
            if (item.CycleStarted.HasValue) result["cycleStarted"] = item.CycleStarted.Value;
            if (item.SegmentMilliseconds.HasValue) result["segmentMilliseconds"] = item.SegmentMilliseconds.Value;
            if (item.CycleMilliseconds.HasValue) result["cycleMilliseconds"] = item.CycleMilliseconds.Value;
            return result;
        }

        private static void AddText(JObject target, string name, string value)
        {
            if (!string.IsNullOrEmpty(value)) target[name] = value;
        }

        private static void AddNumber(JObject target, string name, int? value)
        {
            if (value.HasValue && value.Value >= 0) target[name] = value.Value;
        }

        private static string NormalizeValue(string name, string value)
        {
            string normalizedName = name ?? string.Empty;
            if (normalizedName.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedName.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedName.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedName.IndexOf("密码", StringComparison.Ordinal) >= 0
                || normalizedName.IndexOf("密钥", StringComparison.Ordinal) >= 0)
            {
                return "***";
            }
            return NormalizeText(value);
        }

        private Guid ResolveProcessId(int procIndex)
        {
            if (procIndex < 0) return Guid.Empty;
            IList<Proc> processes = engine.Context?.Procs;
            if (processes == null || procIndex >= processes.Count) return Guid.Empty;
            EngineSnapshot snapshot = engine.GetSnapshot(procIndex);
            if (snapshot?.ProcId != Guid.Empty) return snapshot.ProcId;
            return processes[procIndex]?.head?.Id ?? Guid.Empty;
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= MaximumTextLength ? value : value.Substring(0, MaximumTextLength);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            engine.SnapshotChanged -= HandleSnapshotChanged;
            engine.ProcessStarted -= HandleProcessStarted;
            engine.OperationFailed -= HandleOperationFailed;
            engine.ProcessCompleted -= HandleProcessCompleted;
            engine.CycleTimeMeasured -= HandleCycleTimeMeasured;
            valueStore.ValueChanged -= HandleValueChanged;
            disposeCts.Cancel();
            IncidentMarker[] pending;
            lock (syncRoot) pending = pendingIncidents.Values.ToArray();
            foreach (IncidentMarker marker in pending)
            {
                QueueIncidentPersistence(marker, BuildIncidentArchive(marker, "recorder_disposed"));
            }
            Task pendingPersistence;
            lock (persistenceSync) pendingPersistence = persistenceTail;
            try
            {
                pendingPersistence.GetAwaiter().GetResult();
            }
            catch
            {
                // 事故旁路失败不能改变平台关闭流程。
            }
            disposeCts.Dispose();
        }

        private sealed class RuntimeBlackBoxEvent
        {
            public long Sequence { get; set; }
            public DateTime TimeUtc { get; set; }
            public string EventName { get; set; }
            public int ProcIndex { get; set; } = -1;
            public Guid ProcId { get; set; }
            public Guid RunId { get; set; }
            public string ProcName { get; set; }
            public string State { get; set; }
            public int? StepIndex { get; set; }
            public int? OpIndex { get; set; }
            public Guid OperationId { get; set; }
            public string OperationType { get; set; }
            public string OperationName { get; set; }
            public string Source { get; set; }
            public string ResourceId { get; set; }
            public string ResourceName { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Message { get; set; }
            public string Outcome { get; set; }
            public string TerminationReason { get; set; }
            public long? PublishedRevision { get; set; }
            public long? AppliedRevision { get; set; }
            public double? DurationMicroseconds { get; set; }
            public int? SegmentIndex { get; set; }
            public bool? CycleStarted { get; set; }
            public double? SegmentMilliseconds { get; set; }
            public double? CycleMilliseconds { get; set; }
        }

        private sealed class IncidentMarker
        {
            public string IncidentId { get; set; }
            public int ProcIndex { get; set; }
            public Guid ProcId { get; set; }
            public long FailureSequence { get; set; }
            public DateTime FailureTimeUtc { get; set; }
        }

        private readonly struct ProcessPosition : IEquatable<ProcessPosition>
        {
            public ProcessPosition(EngineSnapshot snapshot)
            {
                State = snapshot.State;
                StepIndex = snapshot.StepIndex;
                OpIndex = snapshot.OpIndex;
                AlarmMessage = snapshot.AlarmMessage ?? string.Empty;
                PublishedRevision = snapshot.PublishedRevision;
                AppliedRevision = snapshot.AppliedRevision;
                TerminationReason = snapshot.TerminationReason;
            }

            private ProcRunState State { get; }
            private int StepIndex { get; }
            private int OpIndex { get; }
            private string AlarmMessage { get; }
            private long PublishedRevision { get; }
            private long AppliedRevision { get; }
            private ProcTerminationReason TerminationReason { get; }

            public bool Equals(ProcessPosition other)
            {
                return State == other.State && StepIndex == other.StepIndex && OpIndex == other.OpIndex
                    && string.Equals(AlarmMessage, other.AlarmMessage, StringComparison.Ordinal)
                    && PublishedRevision == other.PublishedRevision && AppliedRevision == other.AppliedRevision
                    && TerminationReason == other.TerminationReason;
            }

            public override bool Equals(object obj) => obj is ProcessPosition other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)State;
                    hash = (hash * 397) ^ StepIndex;
                    hash = (hash * 397) ^ OpIndex;
                    hash = (hash * 397) ^ AlarmMessage.GetHashCode();
                    hash = (hash * 397) ^ PublishedRevision.GetHashCode();
                    hash = (hash * 397) ^ AppliedRevision.GetHashCode();
                    return (hash * 397) ^ (int)TerminationReason;
                }
            }
        }
    }
}
