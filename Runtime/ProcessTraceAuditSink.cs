using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 将高频指令轨迹聚合为一次运行的摘要。正常循环不逐指令落盘，
    /// 失败时保留即时证据，运行结束时保存统计、转移关系和首尾样本。
    /// </summary>
    internal sealed class ProcessTraceAuditSink : IDisposable
    {
        private const int SampleLimit = 5;
        private const int DetailLimit = 100;
        private const int MaxTrackedTransitions = 1000;
        private readonly ProcessEngine engine;
        private readonly object lifecycleLock = new object();
        private readonly ConcurrentDictionary<string, RunAggregate> runs =
            new ConcurrentDictionary<string, RunAggregate>(StringComparer.Ordinal);
        private bool disposed;

        public ProcessTraceAuditSink(ProcessEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            engine.ProcessStarted += HandleProcessStarted;
            engine.OperationTraced += HandleOperationTrace;
            engine.ProcessCompleted += HandleProcessCompleted;
        }

        private void HandleProcessStarted(int procIndex, Guid procId)
        {
            lock (lifecycleLock)
            {
                if (disposed)
                {
                    return;
                }
                string processKey = BuildProcessKey(procId, procIndex);
                var run = new RunAggregate(procIndex, procId);
                if (runs.TryRemove(processKey, out RunAggregate previous))
                {
                    StructuredAuditLogger.Write("ProcessExecution", previous.BuildSummary(
                        "restarted_before_completion",
                        string.Empty));
                }
                runs[processKey] = run;
                WriteProcessStarted(run, "engine_start");
            }
        }

        private void HandleOperationTrace(OperationTraceEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            lock (lifecycleLock)
            {
                if (disposed)
                {
                    return;
                }
                HandleOperationTraceCore(entry);
            }
        }

        private void HandleOperationTraceCore(OperationTraceEntry entry)
        {
            string processKey = BuildProcessKey(entry.ProcId, entry.ProcIndex);
            bool created = false;
            if (!runs.TryGetValue(processKey, out RunAggregate run))
            {
                var candidate = new RunAggregate(entry);
                if (runs.TryAdd(processKey, candidate))
                {
                    run = candidate;
                    created = true;
                }
                else
                {
                    run = runs[processKey];
                }
            }
            if (created)
            {
                WriteProcessStarted(run, "first_operation_trace");
            }

            FailureEvidence failure = run.Record(entry);
            if (failure != null)
            {
                StructuredAuditLogger.Write("ProcessExecution", failure.ToJson(run));
            }
        }

        private void HandleProcessCompleted(int procIndex, Guid procId)
        {
            lock (lifecycleLock)
            {
                if (disposed)
                {
                    return;
                }
                HandleProcessCompletedCore(procIndex, procId);
            }
        }

        private void HandleProcessCompletedCore(int procIndex, Guid procId)
        {
            string processKey = BuildProcessKey(procId, procIndex);
            if (!runs.TryRemove(processKey, out RunAggregate run))
            {
                run = new RunAggregate(procIndex, procId);
                WriteProcessStarted(run, "completion_fallback");
            }

            EngineSnapshot snapshot = null;
            try
            {
                snapshot = engine.GetSnapshot(procIndex);
            }
            catch
            {
            }

            StructuredAuditLogger.Write("ProcessExecution", run.BuildSummary(
                snapshot?.TerminationReason.ToString() ?? "completed",
                snapshot?.AlarmMessage ?? string.Empty));
        }

        private static string BuildProcessKey(Guid procId, int procIndex)
        {
            return procId != Guid.Empty ? procId.ToString("N") : "index:" + procIndex;
        }

        private static void WriteProcessStarted(RunAggregate run, string evidence)
        {
            StructuredAuditLogger.Write("ProcessExecution", new JObject
            {
                ["schemaVersion"] = 2,
                ["source"] = "process_engine",
                ["eventName"] = "process.started",
                ["correlationId"] = run.RunId,
                ["runId"] = run.RunId,
                ["procId"] = run.ProcId == Guid.Empty ? string.Empty : run.ProcId.ToString("D"),
                ["procIndex"] = run.ProcIndex,
                ["startEvidence"] = evidence ?? string.Empty,
                ["outcome"] = "started"
            });
        }

        public void Dispose()
        {
            lock (lifecycleLock)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                engine.ProcessStarted -= HandleProcessStarted;
                engine.OperationTraced -= HandleOperationTrace;
                engine.ProcessCompleted -= HandleProcessCompleted;
                foreach (RunAggregate run in runs.Values)
                {
                    StructuredAuditLogger.Write("ProcessExecution", run.BuildSummary("sink_disposed", string.Empty));
                }
                runs.Clear();
            }
        }

        private sealed class RunAggregate
        {
            private readonly object syncRoot = new object();
            private readonly Dictionary<string, OperationAggregate> operations =
                new Dictionary<string, OperationAggregate>(StringComparer.Ordinal);
            private readonly Dictionary<string, long> transitions =
                new Dictionary<string, long>(StringComparer.Ordinal);
            private readonly List<OperationSample> firstSamples = new List<OperationSample>();
            private readonly Queue<OperationSample> lastSamples = new Queue<OperationSample>();
            private readonly HashSet<string> emittedFailures = new HashSet<string>(StringComparer.Ordinal);
            private readonly DateTime startedUtc;
            private string previousLocation;
            private long startedCount;
            private long completedCount;
            private long failedCount;
            private long totalOperationDurationMs;
            private long untrackedTransitionCount;

            public RunAggregate(OperationTraceEntry entry)
                : this(entry?.ProcIndex ?? -1, entry?.ProcId ?? Guid.Empty)
            {
            }

            public RunAggregate(int procIndex, Guid procId)
            {
                ProcIndex = procIndex;
                ProcId = procId;
                RunId = Guid.NewGuid().ToString("N");
                startedUtc = DateTime.UtcNow;
            }

            public string RunId { get; }

            public int ProcIndex { get; }

            public Guid ProcId { get; }

            public FailureEvidence Record(OperationTraceEntry entry)
            {
                string phase = (entry.Phase ?? string.Empty).Trim().ToLowerInvariant();
                lock (syncRoot)
                {
                    string operationKey = BuildOperationKey(entry);
                    if (!string.Equals(phase, "started", StringComparison.Ordinal)
                        && entry.OperationId == Guid.Empty)
                    {
                        operationKey = "unresolved-completion:" + (entry.OperationType ?? string.Empty);
                    }
                    if (!operations.TryGetValue(operationKey, out OperationAggregate operation))
                    {
                        operation = new OperationAggregate(
                            entry,
                            string.Equals(phase, "started", StringComparison.Ordinal)
                                || entry.OperationId != Guid.Empty);
                        operations[operationKey] = operation;
                    }

                    if (string.Equals(phase, "started", StringComparison.Ordinal))
                    {
                        startedCount++;
                        operation.StartedCount++;
                        string location = BuildLocation(entry.StepIndex, entry.OpIndex);
                        if (!string.IsNullOrWhiteSpace(previousLocation))
                        {
                            string transition = previousLocation + " -> " + location;
                            if (transitions.TryGetValue(transition, out long count))
                            {
                                transitions[transition] = count + 1;
                            }
                            else if (transitions.Count < MaxTrackedTransitions)
                            {
                                transitions[transition] = 1;
                            }
                            else
                            {
                                untrackedTransitionCount++;
                            }
                        }
                        previousLocation = location;

                        OperationSample sample = new OperationSample(entry, startedCount);
                        if (firstSamples.Count < SampleLimit)
                        {
                            firstSamples.Add(sample);
                        }
                        lastSamples.Enqueue(sample);
                        while (lastSamples.Count > SampleLimit)
                        {
                            lastSamples.Dequeue();
                        }
                        return null;
                    }

                    totalOperationDurationMs += Math.Max(0L, entry.ElapsedMs);
                    operation.TotalDurationMs += Math.Max(0L, entry.ElapsedMs);
                    operation.MaxDurationMs = Math.Max(operation.MaxDurationMs, entry.ElapsedMs);
                    if (entry.IsAlarm || string.Equals(phase, "failed", StringComparison.Ordinal))
                    {
                        failedCount++;
                        operation.FailedCount++;
                        string fingerprint = operationKey + ":" + (entry.AlarmMessage ?? string.Empty);
                        if (emittedFailures.Add(fingerprint))
                        {
                            return new FailureEvidence(operation, entry);
                        }
                    }
                    else
                    {
                        completedCount++;
                        operation.CompletedCount++;
                    }
                    return null;
                }
            }

            public JObject BuildSummary(string outcome, string errorMessage)
            {
                lock (syncRoot)
                {
                    DateTime finishedUtc = DateTime.UtcNow;
                    var operationItems = new JArray(operations.Values
                        .OrderByDescending(item => item.StartedCount)
                        .ThenBy(item => item.StepIndex)
                        .ThenBy(item => item.OpIndex)
                        .Take(DetailLimit)
                        .Select(item => item.ToJson()));
                    var transitionItems = new JArray(transitions
                        .OrderByDescending(item => item.Value)
                        .ThenBy(item => item.Key, StringComparer.Ordinal)
                        .Take(DetailLimit)
                        .Select(item => new JObject
                        {
                            ["path"] = item.Key,
                            ["count"] = item.Value
                        }));
                    return new JObject
                    {
                        ["schemaVersion"] = 2,
                        ["source"] = "process_engine",
                        ["eventName"] = "process.summary",
                        ["correlationId"] = RunId,
                        ["runId"] = RunId,
                        ["procId"] = ProcId == Guid.Empty ? string.Empty : ProcId.ToString("D"),
                        ["procIndex"] = ProcIndex,
                        ["durationMs"] = Math.Max(0L, (long)(finishedUtc - startedUtc).TotalMilliseconds),
                        ["outcome"] = outcome ?? string.Empty,
                        ["errorMessage"] = errorMessage ?? string.Empty,
                        ["startedCount"] = startedCount,
                        ["completedCount"] = completedCount,
                        ["failedCount"] = failedCount,
                        ["totalOperationDurationMs"] = totalOperationDurationMs,
                        ["distinctOperationCount"] = operations.Count,
                        ["distinctObservedTransitionCount"] = transitions.Count,
                        ["untrackedTransitionCount"] = untrackedTransitionCount,
                        ["detailsTruncated"] = operations.Count > DetailLimit
                            || transitions.Count > DetailLimit
                            || untrackedTransitionCount > 0,
                        ["byOperation"] = operationItems,
                        ["observedStartTransitions"] = transitionItems,
                        ["firstSamples"] = new JArray(firstSamples.Select(item => item.ToJson())),
                        ["lastSamples"] = new JArray(lastSamples.Select(item => item.ToJson()))
                    };
                }
            }

            private static string BuildOperationKey(OperationTraceEntry entry)
            {
                return entry.OperationId != Guid.Empty
                    ? entry.OperationId.ToString("N")
                    : BuildLocation(entry.StepIndex, entry.OpIndex) + ":" + (entry.OperationType ?? string.Empty);
            }

            private static string BuildLocation(int stepIndex, int opIndex)
            {
                return stepIndex + ":" + opIndex;
            }

        }

        private sealed class OperationSample
        {
            private readonly long ordinal;
            private readonly int stepIndex;
            private readonly int opIndex;
            private readonly Guid operationId;
            private readonly string operationType;
            private readonly string operationName;

            public OperationSample(OperationTraceEntry entry, long ordinal)
            {
                this.ordinal = ordinal;
                stepIndex = entry.StepIndex;
                opIndex = entry.OpIndex;
                operationId = entry.OperationId;
                operationType = entry.OperationType ?? string.Empty;
                operationName = entry.OperationName ?? string.Empty;
            }

            public JObject ToJson()
            {
                return new JObject
                {
                    ["ordinal"] = ordinal,
                    ["executedStepIndex"] = stepIndex,
                    ["executedOpIndex"] = opIndex,
                    ["operationId"] = operationId == Guid.Empty ? string.Empty : operationId.ToString("D"),
                    ["operationType"] = operationType,
                    ["operationName"] = operationName
                };
            }
        }

        private sealed class OperationAggregate
        {
            public OperationAggregate(OperationTraceEntry entry, bool locationVerified)
            {
                OperationId = entry.OperationId;
                StepIndex = locationVerified ? entry.StepIndex : -1;
                OpIndex = locationVerified ? entry.OpIndex : -1;
                OperationType = entry.OperationType ?? string.Empty;
                OperationName = entry.OperationName ?? string.Empty;
            }

            public Guid OperationId { get; }
            public int StepIndex { get; }
            public int OpIndex { get; }
            public string OperationType { get; }
            public string OperationName { get; }
            public long StartedCount { get; set; }
            public long CompletedCount { get; set; }
            public long FailedCount { get; set; }
            public long TotalDurationMs { get; set; }
            public long MaxDurationMs { get; set; }

            public JObject ToJson()
            {
                return new JObject
                {
                    ["operationId"] = OperationId == Guid.Empty ? string.Empty : OperationId.ToString("D"),
                    ["executedStepIndex"] = StepIndex,
                    ["executedOpIndex"] = OpIndex,
                    ["operationType"] = OperationType,
                    ["operationName"] = OperationName,
                    ["startedCount"] = StartedCount,
                    ["completedCount"] = CompletedCount,
                    ["failedCount"] = FailedCount,
                    ["totalDurationMs"] = TotalDurationMs,
                    ["maxDurationMs"] = MaxDurationMs
                };
            }
        }

        private sealed class FailureEvidence
        {
            private readonly OperationAggregate operation;
            private readonly OperationTraceEntry completion;

            public FailureEvidence(OperationAggregate operation, OperationTraceEntry completion)
            {
                this.operation = operation;
                this.completion = completion;
            }

            public JObject ToJson(RunAggregate run)
            {
                return new JObject
                {
                    ["schemaVersion"] = 2,
                    ["timeUtc"] = (completion.Timestamp == default(DateTime)
                        ? DateTime.UtcNow
                        : completion.Timestamp.ToUniversalTime()).ToString("O"),
                    ["source"] = "process_engine",
                    ["eventName"] = "operation.failed",
                    ["correlationId"] = run.RunId,
                    ["runId"] = run.RunId,
                    ["procId"] = run.ProcId == Guid.Empty ? string.Empty : run.ProcId.ToString("D"),
                    ["procIndex"] = run.ProcIndex,
                    ["operationId"] = operation.OperationId == Guid.Empty ? string.Empty : operation.OperationId.ToString("D"),
                    ["executedStepIndex"] = operation.StepIndex,
                    ["executedOpIndex"] = operation.OpIndex,
                    ["nextStepIndex"] = completion.StepIndex,
                    ["nextOpIndex"] = completion.OpIndex,
                    ["operationType"] = operation.OperationType,
                    ["operationName"] = operation.OperationName,
                    ["durationMs"] = completion.ElapsedMs,
                    ["outcome"] = "failed",
                    ["errorCode"] = "OPERATION_ALARM",
                    ["errorMessage"] = completion.AlarmMessage ?? string.Empty
                };
            }
        }
    }
}
