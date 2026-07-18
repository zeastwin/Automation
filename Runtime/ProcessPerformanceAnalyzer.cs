using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Automation
{
    public sealed class ProcessPerformanceSnapshot
    {
        public bool Enabled { get; internal set; }
        public ProcessExecutionMode ExecutionMode { get; internal set; }
        public long OperationCount { get; internal set; }
        public double OperationsPerSecond { get; internal set; }
        public double ThreadCpuPercent { get; internal set; }
        public double AverageOperationMicroseconds { get; internal set; }
        public double MaxOperationMicroseconds { get; internal set; }
        public long OperationDurationSampleCount { get; internal set; }
        public int OperationDurationSamplingInterval { get; internal set; }
        public bool AbnormalCpuLoopDetected { get; internal set; }
    }

    internal sealed class ProcessPerformanceState
    {
        private const double SampleWindowMilliseconds = 500.0;
        private const double AbnormalCpuPercent = 90.0;
        private const int AbnormalWindowCount = 4;
        private readonly bool enabled;
        private readonly ProcessExecutionMode executionMode;
        private long operationCount;
        private long totalOperationTicks;
        private long maxOperationTicks;
        private long operationDurationSampleCount;
        private long sampleWallTicks;
        private long sampleCpu100Nanoseconds;
        private long sampleOperationCount;
        private double operationsPerSecond;
        private double threadCpuPercent;
        private int abnormalWindows;
        private int abnormalDetected;
        private int initialized;

        public ProcessPerformanceState(bool enabled, ProcessExecutionMode executionMode)
        {
            this.enabled = enabled;
            this.executionMode = executionMode;
        }

        public bool Enabled => enabled;
        public ProcessExecutionMode ExecutionMode => executionMode;

        public void InitializeForCurrentThread()
        {
            if (!enabled || Interlocked.Exchange(ref initialized, 1) != 0)
            {
                return;
            }
            sampleWallTicks = Stopwatch.GetTimestamp();
            TryGetCurrentThreadCpuTime(out sampleCpu100Nanoseconds);
        }

        public void RecordOperation(long elapsedTicks, bool durationMeasured)
        {
            if (!enabled)
            {
                return;
            }
            operationCount++;
            if (!durationMeasured)
            {
                return;
            }
            long normalizedTicks = Math.Max(0L, elapsedTicks);
            operationDurationSampleCount++;
            totalOperationTicks += normalizedTicks;
            if (normalizedTicks > maxOperationTicks)
            {
                maxOperationTicks = normalizedTicks;
            }
        }

        public bool TrySample(out string abnormalMessage)
        {
            abnormalMessage = null;
            if (!enabled)
            {
                return false;
            }
            if (Volatile.Read(ref initialized) == 0)
            {
                InitializeForCurrentThread();
                return false;
            }
            long nowWall = Stopwatch.GetTimestamp();
            long previousWall = Interlocked.Read(ref sampleWallTicks);
            double elapsedMilliseconds = (nowWall - previousWall) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMilliseconds < SampleWindowMilliseconds)
            {
                return false;
            }

            long currentOperations = operationCount;
            long operationDelta = currentOperations - sampleOperationCount;
            operationsPerSecond = elapsedMilliseconds <= 0
                ? 0
                : operationDelta * 1000.0 / elapsedMilliseconds;

            if (TryGetCurrentThreadCpuTime(out long currentCpu))
            {
                long previousCpu = Interlocked.Read(ref sampleCpu100Nanoseconds);
                long cpuDelta = Math.Max(0L, currentCpu - previousCpu);
                threadCpuPercent = elapsedMilliseconds <= 0
                    ? 0
                    : Math.Min(100.0, cpuDelta / (elapsedMilliseconds * 10000.0) * 100.0);
                Interlocked.Exchange(ref sampleCpu100Nanoseconds, currentCpu);
            }

            sampleWallTicks = nowWall;
            sampleOperationCount = currentOperations;

            if (threadCpuPercent < AbnormalCpuPercent)
            {
                abnormalWindows = 0;
                return true;
            }
            abnormalWindows++;
            if (abnormalWindows < AbnormalWindowCount || Interlocked.Exchange(ref abnormalDetected, 1) != 0)
            {
                return true;
            }
            abnormalMessage = $"检测到异常流程：流程线程连续占用单个逻辑处理器，CPU {threadCpuPercent:F1}%，指令速率 {operationsPerSecond:F0}/s。请排查无等待循环或耗时自定义函数。";
            return true;
        }

        public ProcessPerformanceSnapshot GetSnapshot()
        {
            long count = Interlocked.Read(ref operationCount);
            long durationSamples = Interlocked.Read(ref operationDurationSampleCount);
            long totalTicks = Interlocked.Read(ref totalOperationTicks);
            return new ProcessPerformanceSnapshot
            {
                Enabled = enabled,
                ExecutionMode = executionMode,
                OperationCount = count,
                OperationsPerSecond = Volatile.Read(ref operationsPerSecond),
                ThreadCpuPercent = Volatile.Read(ref threadCpuPercent),
                AverageOperationMicroseconds = durationSamples <= 0
                    ? 0
                    : totalTicks * 1000000.0 / Stopwatch.Frequency / durationSamples,
                MaxOperationMicroseconds = Interlocked.Read(ref maxOperationTicks) * 1000000.0 / Stopwatch.Frequency,
                OperationDurationSampleCount = durationSamples,
                OperationDurationSamplingInterval = ProcessRunMetrics.DurationSamplingInterval,
                AbnormalCpuLoopDetected = Volatile.Read(ref abnormalDetected) == 1
            };
        }

        private static bool TryGetCurrentThreadCpuTime(out long cpu100Nanoseconds)
        {
            cpu100Nanoseconds = 0;
            try
            {
                if (!GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user))
                {
                    return false;
                }
                cpu100Nanoseconds = kernel + user;
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetThreadTimes(IntPtr thread, out long creationTime, out long exitTime,
            out long kernelTime, out long userTime);
    }
}
