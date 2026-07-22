using Newtonsoft.Json.Linq;
// 模块：运行时 / 诊断。
// 职责范围：记录并投影断点、性能、审计、异常、日志缓冲和运行黑匣子事实。

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Automation
{
    /// <summary>
    /// 只记录流程开始、流程摘要和指令失败。正常高速循环仅更新整数计数，
    /// 不创建逐指令对象、转移字符串或明细字典。
    /// </summary>
    internal sealed class ProcessTraceAuditSink : IDisposable
    {
        private readonly ProcessEngine engine;
        private readonly ConcurrentDictionary<RunKey, RunAggregate> runs =
            new ConcurrentDictionary<RunKey, RunAggregate>();
        private int disposed;

        public ProcessTraceAuditSink(ProcessEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            engine.ProcessStarted += HandleProcessStarted;
            engine.OperationFailed += HandleOperationFailure;
            engine.ProcessCompleted += HandleProcessCompleted;
        }

        private void HandleProcessStarted(int procIndex, Guid procId)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }
            RunKey key = new RunKey(procId, procIndex);
            var run = new RunAggregate(procIndex, procId);
            if (runs.TryRemove(key, out RunAggregate previous))
            {
                StructuredAuditLogger.Write("ProcessExecution",
                    previous.BuildSummary("restarted_before_completion", string.Empty, null));
            }
            runs[key] = run;
            StructuredAuditLogger.Write("ProcessExecution", run.BuildStarted());
        }

        private void HandleOperationFailure(OperationFailureEntry entry)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }
            RunKey key = new RunKey(entry.ProcId, entry.ProcIndex);
            if (!runs.TryGetValue(key, out RunAggregate run))
            {
                run = runs.GetOrAdd(key, _ => new RunAggregate(entry.ProcIndex, entry.ProcId));
            }
            if (run.TryMarkFailureEmitted())
            {
                StructuredAuditLogger.Write("ProcessExecution", run.BuildFailure(entry));
            }
        }

        private void HandleProcessCompleted(ProcessRunAuditSnapshot metrics)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }
            RunKey key = new RunKey(metrics.ProcId, metrics.ProcIndex);
            if (!runs.TryRemove(key, out RunAggregate run))
            {
                run = new RunAggregate(metrics.ProcIndex, metrics.ProcId);
            }
            EngineSnapshot snapshot = engine.GetSnapshot(metrics.ProcIndex);
            StructuredAuditLogger.Write("ProcessExecution", run.BuildSummary(
                snapshot?.TerminationReason.ToString() ?? "completed",
                snapshot?.AlarmMessage ?? string.Empty,
                metrics));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            engine.ProcessStarted -= HandleProcessStarted;
            engine.OperationFailed -= HandleOperationFailure;
            engine.ProcessCompleted -= HandleProcessCompleted;
            foreach (RunAggregate run in runs.Values)
            {
                StructuredAuditLogger.Write("ProcessExecution", run.BuildSummary("sink_disposed", string.Empty, null));
            }
            runs.Clear();
        }

        private readonly struct RunKey : IEquatable<RunKey>
        {
            public RunKey(Guid procId, int procIndex)
            {
                ProcId = procId;
                ProcIndex = procIndex;
            }

            private Guid ProcId { get; }
            private int ProcIndex { get; }

            public bool Equals(RunKey other)
            {
                return ProcId.Equals(other.ProcId) && ProcIndex == other.ProcIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is RunKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ProcId.GetHashCode() * 397) ^ ProcIndex;
                }
            }
        }

        private sealed class RunAggregate
        {
            private readonly DateTime startedUtc = DateTime.UtcNow;
            private int failureEmitted;

            public RunAggregate(int procIndex, Guid procId)
            {
                ProcIndex = procIndex;
                ProcId = procId;
                RunId = Guid.NewGuid().ToString("N");
            }

            public int ProcIndex { get; }
            public Guid ProcId { get; }
            public string RunId { get; }

            public bool TryMarkFailureEmitted()
            {
                return Interlocked.Exchange(ref failureEmitted, 1) == 0;
            }

            public JObject BuildStarted()
            {
                return new JObject
                {
                    ["schemaVersion"] = 3,
                    ["source"] = "process_engine",
                    ["eventName"] = "process.started",
                    ["correlationId"] = RunId,
                    ["runId"] = RunId,
                    ["procId"] = ProcId == Guid.Empty ? string.Empty : ProcId.ToString("D"),
                    ["procIndex"] = ProcIndex,
                    ["outcome"] = "started"
                };
            }

            public JObject BuildFailure(OperationFailureEntry entry)
            {
                return new JObject
                {
                    ["schemaVersion"] = 3,
                    ["timeUtc"] = DateTime.UtcNow.ToString("O"),
                    ["source"] = "process_engine",
                    ["eventName"] = "operation.failed",
                    ["correlationId"] = RunId,
                    ["runId"] = RunId,
                    ["procId"] = ProcId == Guid.Empty ? string.Empty : ProcId.ToString("D"),
                    ["procIndex"] = ProcIndex,
                    ["operationId"] = entry.OperationId == Guid.Empty ? string.Empty : entry.OperationId.ToString("D"),
                    ["executedStepIndex"] = entry.StepIndex,
                    ["executedOpIndex"] = entry.OpIndex,
                    ["operationType"] = entry.OperationType ?? string.Empty,
                    ["operationName"] = entry.OperationName ?? string.Empty,
                    ["durationMeasured"] = entry.DurationMeasured,
                    ["durationMicroseconds"] = entry.DurationMeasured
                        ? (JToken)(entry.ElapsedTicks * 1000000.0 / Stopwatch.Frequency)
                        : JValue.CreateNull(),
                    ["outcome"] = "failed",
                    ["errorCode"] = "OPERATION_ALARM",
                    ["errorMessage"] = entry.AlarmMessage ?? string.Empty
                };
            }

            public JObject BuildSummary(string outcome, string errorMessage, ProcessRunAuditSnapshot? metrics)
            {
                long operationCount = metrics?.OperationCount ?? 0;
                long durationSampleCount = metrics?.DurationSampleCount ?? 0;
                long totalSampledTicks = metrics?.TotalSampledOperationTicks ?? 0;
                return new JObject
                {
                    ["schemaVersion"] = 3,
                    ["source"] = "process_engine",
                    ["eventName"] = "process.summary",
                    ["correlationId"] = RunId,
                    ["runId"] = RunId,
                    ["procId"] = ProcId == Guid.Empty ? string.Empty : ProcId.ToString("D"),
                    ["procIndex"] = ProcIndex,
                    ["durationMs"] = Math.Max(0L, (long)(DateTime.UtcNow - startedUtc).TotalMilliseconds),
                    ["outcome"] = outcome ?? string.Empty,
                    ["errorMessage"] = errorMessage ?? string.Empty,
                    ["operationCount"] = metrics.HasValue ? (JToken)operationCount : JValue.CreateNull(),
                    ["failedCount"] = metrics.HasValue ? (JToken)metrics.Value.FailedCount : JValue.CreateNull(),
                    ["operationDurationSampleCount"] = durationSampleCount,
                    ["operationDurationSamplingInterval"] = metrics?.DurationSamplingInterval ?? ProcessRunMetrics.DurationSamplingInterval,
                    ["averageOperationMicroseconds"] = durationSampleCount <= 0
                        ? 0
                        : totalSampledTicks * 1000000.0 / Stopwatch.Frequency / durationSampleCount,
                    ["maxOperationMicroseconds"] = (metrics?.MaxSampledOperationTicks ?? 0) * 1000000.0 / Stopwatch.Frequency
                };
            }
        }
    }
}
