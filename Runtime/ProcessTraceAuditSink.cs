using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;

namespace Automation
{
    /// <summary>
    /// 将流程引擎已经产生的指令轨迹写入结构化审计日志，并为一次运行分配稳定 runId。
    /// </summary>
    internal sealed class ProcessTraceAuditSink : IDisposable
    {
        private readonly ProcessEngine engine;
        private readonly ConcurrentDictionary<string, string> runIds =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private bool disposed;

        public ProcessTraceAuditSink(ProcessEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            engine.OperationTraced += HandleOperationTrace;
            engine.ProcessCompleted += HandleProcessCompleted;
        }

        private void HandleOperationTrace(OperationTraceEntry entry)
        {
            if (entry == null || disposed)
            {
                return;
            }

            string processKey = BuildProcessKey(entry.ProcId, entry.ProcIndex);
            bool newRun = false;
            string runId = runIds.GetOrAdd(processKey, _ =>
            {
                newRun = true;
                return Guid.NewGuid().ToString("N");
            });

            if (newRun)
            {
                StructuredAuditLogger.Write("ProcessExecution", new JObject
                {
                    ["source"] = "process_engine",
                    ["eventName"] = "process.started",
                    ["correlationId"] = runId,
                    ["runId"] = runId,
                    ["procId"] = entry.ProcId == Guid.Empty ? string.Empty : entry.ProcId.ToString("D"),
                    ["procIndex"] = entry.ProcIndex,
                    ["outcome"] = "started"
                });
            }

            string phase = string.IsNullOrWhiteSpace(entry.Phase)
                ? "unknown"
                : entry.Phase.Trim().ToLowerInvariant();
            StructuredAuditLogger.Write("ProcessExecution", new JObject
            {
                ["timeUtc"] = (entry.Timestamp == default(DateTime)
                    ? DateTime.UtcNow
                    : entry.Timestamp.ToUniversalTime()).ToString("O"),
                ["source"] = "process_engine",
                ["eventName"] = "operation." + phase,
                ["correlationId"] = runId,
                ["runId"] = runId,
                ["procId"] = entry.ProcId == Guid.Empty ? string.Empty : entry.ProcId.ToString("D"),
                ["procIndex"] = entry.ProcIndex,
                ["stepIndex"] = entry.StepIndex,
                ["operationId"] = entry.OperationId == Guid.Empty ? string.Empty : entry.OperationId.ToString("D"),
                ["opIndex"] = entry.OpIndex,
                ["operationType"] = entry.OperationType ?? string.Empty,
                ["operationName"] = entry.OperationName ?? string.Empty,
                ["durationMs"] = entry.ElapsedMs,
                ["outcome"] = entry.IsAlarm || string.Equals(phase, "failed", StringComparison.Ordinal)
                    ? "failed"
                    : phase,
                ["errorCode"] = entry.IsAlarm ? "OPERATION_ALARM" : string.Empty,
                ["errorMessage"] = entry.AlarmMessage ?? string.Empty
            });
        }

        private void HandleProcessCompleted(int procIndex, Guid procId)
        {
            if (disposed)
            {
                return;
            }

            string processKey = BuildProcessKey(procId, procIndex);
            if (!runIds.TryRemove(processKey, out string runId))
            {
                runId = Guid.NewGuid().ToString("N");
            }

            EngineSnapshot snapshot = null;
            try
            {
                snapshot = engine.GetSnapshot(procIndex);
            }
            catch
            {
            }

            StructuredAuditLogger.Write("ProcessExecution", new JObject
            {
                ["source"] = "process_engine",
                ["eventName"] = "process.completed",
                ["correlationId"] = runId,
                ["runId"] = runId,
                ["procId"] = procId == Guid.Empty ? string.Empty : procId.ToString("D"),
                ["procIndex"] = procIndex,
                ["outcome"] = snapshot?.TerminationReason.ToString() ?? "completed",
                ["errorMessage"] = snapshot?.AlarmMessage ?? string.Empty
            });
        }

        private static string BuildProcessKey(Guid procId, int procIndex)
        {
            return procId != Guid.Empty ? procId.ToString("N") : "index:" + procIndex;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            engine.OperationTraced -= HandleOperationTrace;
            engine.ProcessCompleted -= HandleProcessCompleted;
            runIds.Clear();
        }
    }
}
