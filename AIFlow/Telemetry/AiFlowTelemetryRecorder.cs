using System;
using System.Collections.Generic;
using System.Threading;
using Automation;

namespace Automation.AIFlow
{
    public sealed class AiFlowTelemetryRecorder : IDisposable
    {
        private readonly ProcessEngine engine;
        private readonly object sync = new object();
        private int disposed;
        private int sequence;

        public AiFlowTelemetryRecorder(ProcessEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Trace = new AiFlowTrace();
        }

        public AiFlowTrace Trace { get; }

        public void Start()
        {
            engine.SnapshotChanged += OnSnapshotChanged;
        }

        public void Stop()
        {
            engine.SnapshotChanged -= OnSnapshotChanged;
        }

        private void OnSnapshotChanged(EngineSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }
            lock (sync)
            {
                Trace.Events.Add(new AiFlowTraceEvent
                {
                    Sequence = sequence++,
                    Type = "snapshot",
                    ProcId = snapshot.ProcIndex.ToString(),
                    StepId = snapshot.StepIndex.ToString(),
                    OpId = snapshot.OpIndex.ToString(),
                    OpCode = snapshot.State.ToString(),
                    Message = snapshot.IsAlarm ? snapshot.AlarmMessage : null
                });
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1)
            {
                return;
            }
            Stop();
        }
    }
}
