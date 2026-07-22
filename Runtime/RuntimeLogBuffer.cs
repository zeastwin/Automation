using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    internal sealed class RuntimeLogRecord
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public LogLevel Level { get; set; }
    }

    internal sealed class RuntimeLogBuffer : ILogger
    {
        private const int Capacity = 200;
        private readonly object sync = new object();
        private readonly Queue<RuntimeLogRecord> records = new Queue<RuntimeLogRecord>();

        public void Log(string message, LogLevel level)
        {
            lock (sync)
            {
                records.Enqueue(new RuntimeLogRecord
                {
                    Timestamp = DateTime.Now,
                    Message = message ?? string.Empty,
                    Level = level
                });
                while (records.Count > Capacity)
                {
                    records.Dequeue();
                }
            }
        }

        public IReadOnlyList<RuntimeLogRecord> GetTail(int maxCount)
        {
            if (maxCount <= 0)
            {
                return Array.Empty<RuntimeLogRecord>();
            }
            lock (sync)
            {
                return records.Skip(Math.Max(0, records.Count - maxCount)).ToList();
            }
        }
    }
}
