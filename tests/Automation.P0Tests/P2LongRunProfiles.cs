using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Automation.P0Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class P2LongRunProfiles
    {
        private const string LongRunEnableEnv = "AUTOMATION_ENABLE_LONGRUN";
        private const string LongRunProfileEnv = "AUTOMATION_P2LR_PROFILE";
        private const string SoakMinutesEnv = "AUTOMATION_P2LR_SOAK_MINUTES";
        private const string JitterMinutesEnv = "AUTOMATION_P2LR_JITTER_MINUTES";
        private const string MemoryMinutesEnv = "AUTOMATION_P2LR_MEMORY_MINUTES";
        private const string HandleMinutesEnv = "AUTOMATION_P2LR_HANDLE_MINUTES";
        private const string SampleMsEnv = "AUTOMATION_P2LR_SAMPLE_MS";
        private const string MaxMemoryRatioEnv = "AUTOMATION_P2LR_MAX_MEMORY_DRIFT_RATIO";
        private const string MaxHandleDeltaEnv = "AUTOMATION_P2LR_MAX_HANDLE_DELTA";
        private const string MaxThreadDeltaEnv = "AUTOMATION_P2LR_MAX_THREAD_DELTA";
        private const string MaxGdiDeltaEnv = "AUTOMATION_P2LR_MAX_GDI_DELTA";
        private const string MaxAlarmLatencyEnv = "AUTOMATION_P2LR_MAX_ALARM_LATENCY_MS";
        private const string JitterCooldownEnv = "AUTOMATION_P2LR_JITTER_COOLDOWN_MS";
        private const string JitterMaxBurstEnv = "AUTOMATION_P2LR_JITTER_MAX_BURST";
        private const string JitterBurstProbabilityEnv = "AUTOMATION_P2LR_JITTER_BURST_PROBABILITY";
        private const string RandomSeedEnv = "AUTOMATION_P2LR_RANDOM_SEED";
        private const string MinTrendHoursEnv = "AUTOMATION_P2LR_MIN_TREND_HOURS";
        private const string MaxMemorySlopeEnv = "AUTOMATION_P2LR_MAX_MEMORY_SLOPE_MB_PER_HOUR";
        private const string MaxHandleSlopeEnv = "AUTOMATION_P2LR_MAX_HANDLE_SLOPE_PER_HOUR";
        private const string MaxThreadSlopeEnv = "AUTOMATION_P2LR_MAX_THREAD_SLOPE_PER_HOUR";
        private const string MaxGdiSlopeEnv = "AUTOMATION_P2LR_MAX_GDI_SLOPE_PER_HOUR";
        private const string MinCyclePerMinuteEnv = "AUTOMATION_P2LR_MIN_CYCLE_PER_MINUTE";

        [TearDown]
        public void TearDown()
        {
            SF.SetUserSession(null);
            SF.ClearSecurityLock();
        }

        private enum LongRunScenario
        {
            Soak,
            Jitter,
            Memory,
            Handle
        }

        private enum FaultMode
        {
            TcpDisconnectLike,
            SerialJitterLike,
            PlcRejectLike
        }

        [Test]
        [Category("LongRun")]
        public void P2_01_LongRun72h_Profile_ShouldKeepStableWithoutCrashOrDeadlock()
        {
            EnsureLongRunEnabledOrIgnore();

            int durationMinutes = ResolveDurationMinutes(LongRunScenario.Soak, SoakMinutesEnv, 72 * 60, 1, 60 * 24 * 14);
            int sampleIntervalMs = ResolveSampleIntervalMs(30000);
            int minCyclesPerMinute = ReadEnvInt(MinCyclePerMinuteEnv, 1, 1, 10000);
            DateTime deadline = DateTime.UtcNow.AddMinutes(durationMinutes);
            DateTime nextSampleAt = DateTime.UtcNow;
            List<RuntimeSample> samples = new List<RuntimeSample>();
            List<long> cycleCosts = new List<long>();

            Proc proc = BuildTypicalProductionProc("P2长稳典型流程");
            ProcessEngine engine = CreateEngine(new List<Proc> { proc });
            int cycles = 0;
            DateTime startedAt = DateTime.UtcNow;

            try
            {
                while (DateTime.UtcNow < deadline)
                {
                    Stopwatch cycleStopwatch = Stopwatch.StartNew();
                    RunOneCycleAndAssertNoAlarm(engine, proc, 0, 10000);
                    cycleStopwatch.Stop();
                    cycleCosts.Add(cycleStopwatch.ElapsedMilliseconds);
                    cycles++;

                    if (DateTime.UtcNow >= nextSampleAt)
                    {
                        samples.Add(CaptureRuntimeSample());
                        nextSampleAt = DateTime.UtcNow.AddMilliseconds(sampleIntervalMs);
                    }
                }
            }
            finally
            {
                try
                {
                    engine.Stop(0);
                }
                catch
                {
                }
                engine.Dispose();
            }

            samples.Add(CaptureRuntimeSample());
            string reportPath = WriteSamplesToCsv("P2_01_LongRun72h", samples);
            long p95CycleMs = Percentile(cycleCosts, 0.95);
            double elapsedMinutes = Math.Max(1d / 60d, (DateTime.UtcNow - startedAt).TotalMinutes);
            double cycleRate = cycles / elapsedMinutes;
            int expectedMinCycles = Math.Max(1, durationMinutes * minCyclesPerMinute);
            string summaryPath = WriteSummaryJson(
                "P2_01_LongRun72h",
                new Dictionary<string, object>
                {
                    { "profile", ResolveProfileName() },
                    { "durationMinutes", durationMinutes },
                    { "sampleIntervalMs", sampleIntervalMs },
                    { "totalCycles", cycles },
                    { "minCyclesPerMinute", minCyclesPerMinute },
                    { "cycleRatePerMinute", cycleRate },
                    { "cycleP95Ms", p95CycleMs },
                    { "samples", samples.Count },
                    { "reportCsv", reportPath }
                },
                samples);
            TestContext.Progress.WriteLine($"P2长稳报告: {reportPath}");
            TestContext.Progress.WriteLine($"P2长稳摘要: {summaryPath}");
            Assert.That(cycles, Is.GreaterThan(0));
            Assert.That(cycles, Is.GreaterThanOrEqualTo(expectedMinCycles),
                $"长稳循环吞吐不足，实际循环={cycles}，期望至少={expectedMinCycles}，分钟速率={cycleRate:F2}");
        }

        [Test]
        [Category("LongRun")]
        public void P2_02_JitterInjection_Profile_ShouldAlarmAndStopSafely()
        {
            EnsureLongRunEnabledOrIgnore();

            int durationMinutes = ResolveDurationMinutes(LongRunScenario.Jitter, JitterMinutesEnv, 60, 1, 60 * 24 * 7);
            int maxAlarmLatencyMs = ReadEnvInt(MaxAlarmLatencyEnv, 3000, 200, 60000);
            int cooldownMs = ReadEnvInt(JitterCooldownEnv, 20, 0, 5000);
            int maxBurst = ReadEnvInt(JitterMaxBurstEnv, 3, 1, 32);
            double burstProbability = ReadEnvDouble(JitterBurstProbabilityEnv, 0.30, 0d, 1d);
            int randomSeed = ReadEnvIntAllowNegative(RandomSeedEnv, Environment.TickCount);
            Random random = new Random(randomSeed);
            DateTime deadline = DateTime.UtcNow.AddMinutes(durationMinutes);
            Dictionary<FaultMode, FaultStats> stats = CreateFaultStats();
            List<AlarmObservation> observations = new List<AlarmObservation>();

            while (DateTime.UtcNow < deadline)
            {
                FaultMode mode = PickFaultMode(random);
                int burst = random.NextDouble() < burstProbability
                    ? random.Next(2, maxBurst + 1)
                    : 1;
                for (int i = 0; i < burst && DateTime.UtcNow < deadline; i++)
                {
                    AlarmObservation observation = RunFaultOnce(mode, maxAlarmLatencyMs);
                    observations.Add(observation);

                    FaultStats stat = stats[mode];
                    stat.Total++;
                    stat.MaxLatencyMs = Math.Max(stat.MaxLatencyMs, observation.AlarmLatencyMs);
                    stat.TotalLatencyMs += observation.AlarmLatencyMs;
                    stat.LastAlarmMessage = observation.AlarmMessage;
                    stats[mode] = stat;

                    if (cooldownMs > 0)
                    {
                        Thread.Sleep(cooldownMs);
                    }
                }
            }

            Assert.That(stats[FaultMode.TcpDisconnectLike].Total, Is.GreaterThan(0));
            Assert.That(stats[FaultMode.SerialJitterLike].Total, Is.GreaterThan(0));
            Assert.That(stats[FaultMode.PlcRejectLike].Total, Is.GreaterThan(0));
            Assert.That(stats[FaultMode.TcpDisconnectLike].MaxLatencyMs, Is.LessThanOrEqualTo(maxAlarmLatencyMs));
            Assert.That(stats[FaultMode.SerialJitterLike].MaxLatencyMs, Is.LessThanOrEqualTo(maxAlarmLatencyMs));
            Assert.That(stats[FaultMode.PlcRejectLike].MaxLatencyMs, Is.LessThanOrEqualTo(maxAlarmLatencyMs));

            string summaryPath = WriteSummaryJson(
                "P2_02_JitterInjection",
                new Dictionary<string, object>
                {
                    { "profile", ResolveProfileName() },
                    { "durationMinutes", durationMinutes },
                    { "randomSeed", randomSeed },
                    { "maxAlarmLatencyMs", maxAlarmLatencyMs },
                    { "cooldownMs", cooldownMs },
                    { "maxBurst", maxBurst },
                    { "burstProbability", burstProbability },
                    { "totalFaults", observations.Count },
                    { "tcpFaults", stats[FaultMode.TcpDisconnectLike].Total },
                    { "serialFaults", stats[FaultMode.SerialJitterLike].Total },
                    { "plcFaults", stats[FaultMode.PlcRejectLike].Total },
                    { "tcpAvgLatencyMs", GetAverageLatencyMs(stats[FaultMode.TcpDisconnectLike]) },
                    { "serialAvgLatencyMs", GetAverageLatencyMs(stats[FaultMode.SerialJitterLike]) },
                    { "plcAvgLatencyMs", GetAverageLatencyMs(stats[FaultMode.PlcRejectLike]) },
                    { "tcpMaxLatencyMs", stats[FaultMode.TcpDisconnectLike].MaxLatencyMs },
                    { "serialMaxLatencyMs", stats[FaultMode.SerialJitterLike].MaxLatencyMs },
                    { "plcMaxLatencyMs", stats[FaultMode.PlcRejectLike].MaxLatencyMs },
                    { "tcpLastAlarm", stats[FaultMode.TcpDisconnectLike].LastAlarmMessage ?? string.Empty },
                    { "serialLastAlarm", stats[FaultMode.SerialJitterLike].LastAlarmMessage ?? string.Empty },
                    { "plcLastAlarm", stats[FaultMode.PlcRejectLike].LastAlarmMessage ?? string.Empty }
                },
                null,
                observations);
            TestContext.Progress.WriteLine($"P2抖动注入摘要: {summaryPath}");
        }

        [Test]
        [Category("LongRun")]
        public void P2_03_MemoryDrift_24h72h_Profile_ShouldStayWithinConfiguredRatio()
        {
            EnsureLongRunEnabledOrIgnore();

            int durationMinutes = ResolveDurationMinutes(LongRunScenario.Memory, MemoryMinutesEnv, 24 * 60, 1, 60 * 24 * 14);
            int sampleIntervalMs = ResolveSampleIntervalMs(30000);
            double maxRatio = ReadEnvDouble(MaxMemoryRatioEnv, 0.10, 0.01, 2.0);
            double minTrendHours = ReadEnvDouble(MinTrendHoursEnv, 0.50, 0.05, 72);
            double maxMemorySlopeMbPerHour = ReadEnvDouble(MaxMemorySlopeEnv, 8.0, 0.10, 1024.0);

            Proc proc = BuildTypicalProductionProc("P2内存漂移流程");
            ProcessEngine engine = CreateEngine(new List<Proc> { proc });
            List<RuntimeSample> samples = new List<RuntimeSample>();
            DateTime deadline = DateTime.UtcNow.AddMinutes(durationMinutes);
            DateTime nextSampleAt = DateTime.UtcNow;

            try
            {
                ForceFullGc();
                while (DateTime.UtcNow < deadline)
                {
                    RunOneCycleAndAssertNoAlarm(engine, proc, 0, 10000);
                    if (DateTime.UtcNow >= nextSampleAt)
                    {
                        ForceFullGc();
                        samples.Add(CaptureRuntimeSample());
                        nextSampleAt = DateTime.UtcNow.AddMilliseconds(sampleIntervalMs);
                    }
                }
            }
            finally
            {
                try
                {
                    engine.Stop(0);
                }
                catch
                {
                }
                engine.Dispose();
            }

            ForceFullGc();
            samples.Add(CaptureRuntimeSample());
            string reportPath = WriteSamplesToCsv("P2_03_MemoryDrift", samples);
            TestContext.Progress.WriteLine($"P2内存漂移报告: {reportPath}");

            long baseline = samples.First().PrivateBytes;
            long peak = samples.Max(item => item.PrivateBytes);
            double ratio = baseline <= 0 ? 0 : (double)(peak - baseline) / baseline;
            double observedHours = Math.Max(0, (samples.Last().TimeUtc - samples.First().TimeUtc).TotalHours);
            double memorySlopeBytesPerHour = ComputeSlopePerHour(samples, item => item.PrivateBytes);
            double memorySlopeMbPerHour = memorySlopeBytesPerHour / (1024d * 1024d);
            bool trendGateEnabled = observedHours >= minTrendHours;
            string summaryPath = WriteSummaryJson(
                "P2_03_MemoryDrift",
                new Dictionary<string, object>
                {
                    { "profile", ResolveProfileName() },
                    { "durationMinutes", durationMinutes },
                    { "sampleIntervalMs", sampleIntervalMs },
                    { "samples", samples.Count },
                    { "baselinePrivateBytes", baseline },
                    { "peakPrivateBytes", peak },
                    { "driftRatio", ratio },
                    { "maxDriftRatio", maxRatio },
                    { "observedHours", observedHours },
                    { "minTrendHours", minTrendHours },
                    { "trendGateEnabled", trendGateEnabled },
                    { "memorySlopeMbPerHour", memorySlopeMbPerHour },
                    { "maxMemorySlopeMbPerHour", maxMemorySlopeMbPerHour },
                    { "reportCsv", reportPath }
                },
                samples);
            TestContext.Progress.WriteLine($"P2内存漂移摘要: {summaryPath}");
            Assert.That(ratio, Is.LessThanOrEqualTo(maxRatio),
                $"私有内存漂移超限，基线={baseline}，峰值={peak}，漂移比例={ratio:P2}，阈值={maxRatio:P2}");
            if (trendGateEnabled)
            {
                Assert.That(memorySlopeMbPerHour, Is.LessThanOrEqualTo(maxMemorySlopeMbPerHour),
                    $"内存增长斜率超限，实际={memorySlopeMbPerHour:F3}MB/h，阈值={maxMemorySlopeMbPerHour:F3}MB/h");
            }
        }

        [Test]
        [Category("LongRun")]
        public void P2_04_HandleLeak_Profile_ShouldNotShowContinuousGrowthTrend()
        {
            EnsureLongRunEnabledOrIgnore();

            int durationMinutes = ResolveDurationMinutes(LongRunScenario.Handle, HandleMinutesEnv, 24 * 60, 1, 60 * 24 * 14);
            int sampleIntervalMs = ResolveSampleIntervalMs(30000);
            int maxHandleDelta = ReadEnvInt(MaxHandleDeltaEnv, 300, 10, 50000);
            int maxThreadDelta = ReadEnvInt(MaxThreadDeltaEnv, 20, 1, 1000);
            int maxGdiDelta = ReadEnvInt(MaxGdiDeltaEnv, 50, 1, 5000);
            double minTrendHours = ReadEnvDouble(MinTrendHoursEnv, 0.50, 0.05, 72);
            double maxHandleSlope = ReadEnvDouble(MaxHandleSlopeEnv, 12.5, 0.10, 5000);
            double maxThreadSlope = ReadEnvDouble(MaxThreadSlopeEnv, 0.84, 0.01, 5000);
            double maxGdiSlope = ReadEnvDouble(MaxGdiSlopeEnv, 2.1, 0.01, 5000);

            List<RuntimeSample> samples = new List<RuntimeSample>();
            DateTime deadline = DateTime.UtcNow.AddMinutes(durationMinutes);
            DateTime nextSampleAt = DateTime.UtcNow;

            while (DateTime.UtcNow < deadline)
            {
                using (FrmInfo form = new FrmInfo())
                {
                    MethodInfo loadMethod = typeof(FrmInfo).GetMethod("FrmInfo_Load", BindingFlags.Instance | BindingFlags.NonPublic);
                    loadMethod.Invoke(form, new object[] { form, EventArgs.Empty });
                    form.PrintInfo("长稳句柄采样", FrmInfo.Level.Normal);
                    MethodInfo flushMethod = typeof(FrmInfo).GetMethod("InfoFlushTimer_Tick", BindingFlags.Instance | BindingFlags.NonPublic);
                    flushMethod.Invoke(form, new object[] { form, EventArgs.Empty });
                }

                if (DateTime.UtcNow >= nextSampleAt)
                {
                    ForceFullGc();
                    samples.Add(CaptureRuntimeSample());
                    nextSampleAt = DateTime.UtcNow.AddMilliseconds(sampleIntervalMs);
                }
            }

            ForceFullGc();
            samples.Add(CaptureRuntimeSample());
            string reportPath = WriteSamplesToCsv("P2_04_HandleLeak", samples);
            TestContext.Progress.WriteLine($"P2句柄趋势报告: {reportPath}");

            RuntimeSample first = samples.First();
            RuntimeSample last = samples.Last();
            int handleDelta = last.HandleCount - first.HandleCount;
            int threadDelta = last.ThreadCount - first.ThreadCount;
            double observedHours = Math.Max(0, (last.TimeUtc - first.TimeUtc).TotalHours);
            bool trendGateEnabled = observedHours >= minTrendHours;
            double handleSlopePerHour = ComputeSlopePerHour(samples, item => item.HandleCount);
            double threadSlopePerHour = ComputeSlopePerHour(samples, item => item.ThreadCount);
            double gdiSlopePerHour = ComputeSlopePerHour(samples.Where(item => item.GdiCount >= 0).ToList(), item => item.GdiCount);
            Assert.That(handleDelta, Is.LessThanOrEqualTo(maxHandleDelta),
                $"句柄增长超限，起始={first.HandleCount}，结束={last.HandleCount}，增量={handleDelta}，阈值={maxHandleDelta}");
            Assert.That(threadDelta, Is.LessThanOrEqualTo(maxThreadDelta),
                $"线程增长超限，起始={first.ThreadCount}，结束={last.ThreadCount}，增量={threadDelta}，阈值={maxThreadDelta}");

            int gdiDelta = -1;
            if (first.GdiCount >= 0 && last.GdiCount >= 0)
            {
                gdiDelta = last.GdiCount - first.GdiCount;
                Assert.That(gdiDelta, Is.LessThanOrEqualTo(maxGdiDelta),
                    $"GDI增长超限，起始={first.GdiCount}，结束={last.GdiCount}，增量={gdiDelta}，阈值={maxGdiDelta}");
            }

            if (trendGateEnabled)
            {
                Assert.That(handleSlopePerHour, Is.LessThanOrEqualTo(maxHandleSlope),
                    $"句柄增长斜率超限，实际={handleSlopePerHour:F3}/h，阈值={maxHandleSlope:F3}/h");
                Assert.That(threadSlopePerHour, Is.LessThanOrEqualTo(maxThreadSlope),
                    $"线程增长斜率超限，实际={threadSlopePerHour:F3}/h，阈值={maxThreadSlope:F3}/h");
                if (first.GdiCount >= 0 && last.GdiCount >= 0)
                {
                    Assert.That(gdiSlopePerHour, Is.LessThanOrEqualTo(maxGdiSlope),
                        $"GDI增长斜率超限，实际={gdiSlopePerHour:F3}/h，阈值={maxGdiSlope:F3}/h");
                }
            }

            string summaryPath = WriteSummaryJson(
                "P2_04_HandleLeak",
                new Dictionary<string, object>
                {
                    { "profile", ResolveProfileName() },
                    { "durationMinutes", durationMinutes },
                    { "sampleIntervalMs", sampleIntervalMs },
                    { "samples", samples.Count },
                    { "handleDelta", handleDelta },
                    { "maxHandleDelta", maxHandleDelta },
                    { "threadDelta", threadDelta },
                    { "maxThreadDelta", maxThreadDelta },
                    { "gdiDelta", gdiDelta },
                    { "maxGdiDelta", maxGdiDelta },
                    { "observedHours", observedHours },
                    { "minTrendHours", minTrendHours },
                    { "trendGateEnabled", trendGateEnabled },
                    { "handleSlopePerHour", handleSlopePerHour },
                    { "maxHandleSlopePerHour", maxHandleSlope },
                    { "threadSlopePerHour", threadSlopePerHour },
                    { "maxThreadSlopePerHour", maxThreadSlope },
                    { "gdiSlopePerHour", gdiSlopePerHour },
                    { "maxGdiSlopePerHour", maxGdiSlope },
                    { "reportCsv", reportPath }
                },
                samples);
            TestContext.Progress.WriteLine($"P2句柄趋势摘要: {summaryPath}");
        }

        private static AlarmObservation RunTcpDisconnectLikeFaultOnce(int maxAlarmLatencyMs)
        {
            ValueConfigStore valueStore = new ValueConfigStore();
            valueStore.TrySetValue(0, "TCP_MSG", "string", "HELLO", string.Empty, "LongRun");
            Proc proc = BuildProcWithSingleOp(
                new SendTcpMsg
                {
                    ID = "TCP_FAULT",
                    Msg = "TCP_MSG",
                    TimeOut = 1000
                },
                "TCP断连注入");
            ProcessEngine engine = CreateEngine(new List<Proc> { proc }, valueStore);
            try
            {
                return RunOneCycleAndAssertAlarm(
                    engine,
                    proc,
                    0,
                    msg => msg.Contains("通讯未初始化"),
                    maxAlarmLatencyMs,
                    5000,
                    FaultMode.TcpDisconnectLike);
            }
            finally
            {
                try
                {
                    engine.Stop(0);
                }
                catch
                {
                }
                engine.Dispose();
            }
        }

        private static AlarmObservation RunSerialJitterLikeFaultOnce(int maxAlarmLatencyMs)
        {
            ValueConfigStore valueStore = new ValueConfigStore();
            valueStore.TrySetValue(0, "SERIAL_MSG", "string", "HELLO", string.Empty, "LongRun");
            Proc proc = BuildProcWithSingleOp(
                new SendSerialPortMsg
                {
                    ID = "SERIAL_FAULT",
                    Msg = "SERIAL_MSG",
                    TimeOut = 1000
                },
                "串口抖动注入");
            ProcessEngine engine = CreateEngine(new List<Proc> { proc }, valueStore);
            try
            {
                return RunOneCycleAndAssertAlarm(
                    engine,
                    proc,
                    0,
                    msg => msg.Contains("通讯未初始化"),
                    maxAlarmLatencyMs,
                    5000,
                    FaultMode.SerialJitterLike);
            }
            finally
            {
                try
                {
                    engine.Stop(0);
                }
                catch
                {
                }
                engine.Dispose();
            }
        }

        private static AlarmObservation RunPlcRejectLikeFaultOnce(int maxAlarmLatencyMs)
        {
            ValueConfigStore valueStore = new ValueConfigStore();
            valueStore.TrySetValue(0, "PLC_VAL", "double", "1", string.Empty, "LongRun");
            Proc proc = BuildProcWithSingleOp(
                new PlcReadWrite
                {
                    PlcName = "PLC_MISSING",
                    DataType = "Float",
                    DataOps = "读PLC",
                    PlcAddress = "DB1.DBW0",
                    Quantity = 1,
                    ValueName = "PLC_VAL"
                },
                "PLC拒绝注入");
            ProcessEngine engine = CreateEngine(new List<Proc> { proc }, valueStore);
            try
            {
                return RunOneCycleAndAssertAlarm(
                    engine,
                    proc,
                    0,
                    msg => msg.Contains("PLC读取失败"),
                    maxAlarmLatencyMs,
                    5000,
                    FaultMode.PlcRejectLike);
            }
            finally
            {
                try
                {
                    engine.Stop(0);
                }
                catch
                {
                }
                engine.Dispose();
            }
        }

        private static void RunOneCycleAndAssertNoAlarm(ProcessEngine engine, Proc proc, int procIndex, int timeoutMs)
        {
            EngineSnapshot before = engine.GetSnapshot(procIndex);
            long beforeTicks = before?.UpdateTicks ?? 0;
            engine.StartProc(proc, procIndex);
            bool stopped = WaitSnapshot(
                engine,
                procIndex,
                snapshot => snapshot != null
                    && snapshot.UpdateTicks > beforeTicks
                    && snapshot.State == ProcRunState.Stopped
                    && !snapshot.IsAlarm,
                timeoutMs);
            Assert.That(stopped, Is.True, "流程循环执行未在预期时间内安全停止。");
        }

        private static AlarmObservation RunFaultOnce(FaultMode mode, int maxAlarmLatencyMs)
        {
            switch (mode)
            {
                case FaultMode.TcpDisconnectLike:
                    return RunTcpDisconnectLikeFaultOnce(maxAlarmLatencyMs);
                case FaultMode.SerialJitterLike:
                    return RunSerialJitterLikeFaultOnce(maxAlarmLatencyMs);
                default:
                    return RunPlcRejectLikeFaultOnce(maxAlarmLatencyMs);
            }
        }

        private static Dictionary<FaultMode, FaultStats> CreateFaultStats()
        {
            return new Dictionary<FaultMode, FaultStats>
            {
                { FaultMode.TcpDisconnectLike, new FaultStats() },
                { FaultMode.SerialJitterLike, new FaultStats() },
                { FaultMode.PlcRejectLike, new FaultStats() }
            };
        }

        private static double GetAverageLatencyMs(FaultStats stats)
        {
            if (stats == null || stats.Total <= 0)
            {
                return 0;
            }
            return (double)stats.TotalLatencyMs / stats.Total;
        }

        private static FaultMode PickFaultMode(Random random)
        {
            int value = random.Next(0, 3);
            switch (value)
            {
                case 0:
                    return FaultMode.TcpDisconnectLike;
                case 1:
                    return FaultMode.SerialJitterLike;
                default:
                    return FaultMode.PlcRejectLike;
            }
        }

        private static AlarmObservation RunOneCycleAndAssertAlarm(
            ProcessEngine engine,
            Proc proc,
            int procIndex,
            Func<string, bool> alarmMatch,
            int maxAlarmLatencyMs,
            int waitTimeoutMs,
            FaultMode mode)
        {
            EngineSnapshot before = engine.GetSnapshot(procIndex);
            long beforeTicks = before?.UpdateTicks ?? 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            engine.StartProc(proc, procIndex);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(waitTimeoutMs);
            EngineSnapshot matched = null;
            while (DateTime.UtcNow <= deadline)
            {
                EngineSnapshot snapshot = engine.GetSnapshot(procIndex);
                if (snapshot != null
                    && snapshot.UpdateTicks > beforeTicks
                    && snapshot.IsAlarm
                    && snapshot.State != ProcRunState.Running
                    && alarmMatch(snapshot.AlarmMessage ?? string.Empty))
                {
                    matched = snapshot;
                    break;
                }
                Thread.Sleep(20);
            }

            Assert.That(matched, Is.Not.Null, "故障注入未触发预期报警或未退出运行态。");
            stopwatch.Stop();
            long latency = stopwatch.ElapsedMilliseconds;
            Assert.That(latency, Is.LessThanOrEqualTo(maxAlarmLatencyMs),
                $"故障注入告警时延超限，模式={mode}，时延={latency}ms，阈值={maxAlarmLatencyMs}ms");
            return new AlarmObservation
            {
                Mode = mode,
                AlarmLatencyMs = latency,
                AlarmMessage = matched?.AlarmMessage ?? string.Empty,
                TimeUtc = DateTime.UtcNow
            };
        }

        private static bool WaitSnapshot(
            ProcessEngine engine,
            int procIndex,
            Func<EngineSnapshot, bool> predicate,
            int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow <= deadline)
            {
                EngineSnapshot snapshot = engine.GetSnapshot(procIndex);
                if (snapshot != null && predicate(snapshot))
                {
                    return true;
                }
                Thread.Sleep(20);
            }
            return false;
        }

        private static RuntimeSample CaptureRuntimeSample()
        {
            Process process = Process.GetCurrentProcess();
            process.Refresh();
            RuntimeSample sample = new RuntimeSample
            {
                TimeUtc = DateTime.UtcNow,
                PrivateBytes = process.PrivateMemorySize64,
                WorkingSetBytes = process.WorkingSet64,
                ManagedBytes = GC.GetTotalMemory(false),
                HandleCount = process.HandleCount,
                ThreadCount = process.Threads.Count,
                GdiCount = TryGetGuiResourceSafe(process.Handle, 0),
                UserCount = TryGetGuiResourceSafe(process.Handle, 1)
            };
            return sample;
        }

        private static int TryGetGuiResourceSafe(IntPtr processHandle, int flag)
        {
            try
            {
                return GetGuiResources(processHandle, flag);
            }
            catch
            {
                return -1;
            }
        }

        private static string WriteSamplesToCsv(string suiteName, List<RuntimeSample> samples)
        {
            string root = Path.Combine(Path.GetTempPath(), "automation-p2-longrun");
            Directory.CreateDirectory(root);
            string filePath = Path.Combine(root, $"{suiteName}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TimeUtc,PrivateBytes,WorkingSetBytes,ManagedBytes,HandleCount,ThreadCount,GdiCount,UserCount");
            foreach (RuntimeSample item in samples ?? new List<RuntimeSample>())
            {
                sb.AppendLine(string.Join(",",
                    item.TimeUtc.ToString("O"),
                    item.PrivateBytes.ToString(CultureInfo.InvariantCulture),
                    item.WorkingSetBytes.ToString(CultureInfo.InvariantCulture),
                    item.ManagedBytes.ToString(CultureInfo.InvariantCulture),
                    item.HandleCount.ToString(CultureInfo.InvariantCulture),
                    item.ThreadCount.ToString(CultureInfo.InvariantCulture),
                    item.GdiCount.ToString(CultureInfo.InvariantCulture),
                    item.UserCount.ToString(CultureInfo.InvariantCulture)));
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return filePath;
        }

        private static string WriteSummaryJson(
            string suiteName,
            IDictionary<string, object> metrics,
            IList<RuntimeSample> samples = null,
            IList<AlarmObservation> observations = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "automation-p2-longrun");
            Directory.CreateDirectory(root);
            string filePath = Path.Combine(root, $"{suiteName}-{DateTime.Now:yyyyMMdd-HHmmss}.summary.json");
            List<RuntimeSample> tailSamples = samples == null
                ? new List<RuntimeSample>()
                : samples.Skip(Math.Max(0, samples.Count - 10)).ToList();
            List<AlarmObservation> tailAlarms = observations == null
                ? new List<AlarmObservation>()
                : observations.Skip(Math.Max(0, observations.Count - 20)).ToList();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.Append("  \"suite\": \"").Append(EscapeJson(suiteName)).AppendLine("\",");
            sb.Append("  \"generatedAtUtc\": \"").Append(DateTime.UtcNow.ToString("O")).AppendLine("\",");
            sb.AppendLine("  \"metrics\": {");
            bool firstMetric = true;
            foreach (KeyValuePair<string, object> pair in metrics ?? new Dictionary<string, object>())
            {
                if (!firstMetric)
                {
                    sb.AppendLine(",");
                }
                sb.Append("    \"").Append(EscapeJson(pair.Key)).Append("\": ");
                AppendJsonValue(sb, pair.Value);
                firstMetric = false;
            }
            if (!firstMetric)
            {
                sb.AppendLine();
            }
            sb.AppendLine("  },");
            sb.AppendLine("  \"tailSamples\": [");
            for (int i = 0; i < tailSamples.Count; i++)
            {
                RuntimeSample sample = tailSamples[i];
                sb.Append("    {");
                sb.Append("\"timeUtc\":\"").Append(sample.TimeUtc.ToString("O")).Append("\",");
                sb.Append("\"privateBytes\":").Append(sample.PrivateBytes.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"workingSetBytes\":").Append(sample.WorkingSetBytes.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"managedBytes\":").Append(sample.ManagedBytes.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"handleCount\":").Append(sample.HandleCount.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"threadCount\":").Append(sample.ThreadCount.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"gdiCount\":").Append(sample.GdiCount.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"userCount\":").Append(sample.UserCount.ToString(CultureInfo.InvariantCulture));
                sb.Append("}");
                if (i < tailSamples.Count - 1)
                {
                    sb.AppendLine(",");
                }
                else
                {
                    sb.AppendLine();
                }
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"tailAlarms\": [");
            for (int i = 0; i < tailAlarms.Count; i++)
            {
                AlarmObservation item = tailAlarms[i];
                sb.Append("    {");
                sb.Append("\"timeUtc\":\"").Append(item.TimeUtc.ToString("O")).Append("\",");
                sb.Append("\"mode\":\"").Append(EscapeJson(item.Mode.ToString())).Append("\",");
                sb.Append("\"latencyMs\":").Append(item.AlarmLatencyMs.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"alarmMessage\":\"").Append(EscapeJson(item.AlarmMessage ?? string.Empty)).Append("\"");
                sb.Append("}");
                if (i < tailAlarms.Count - 1)
                {
                    sb.AppendLine(",");
                }
                else
                {
                    sb.AppendLine();
                }
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return filePath;
        }

        private static void AppendJsonValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string text:
                    sb.Append("\"").Append(EscapeJson(text)).Append("\"");
                    return;
                case bool boolValue:
                    sb.Append(boolValue ? "true" : "false");
                    return;
                case DateTime dateTime:
                    sb.Append("\"").Append(dateTime.ToString("O")).Append("\"");
                    return;
                case IFormattable formattable:
                    sb.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                    return;
                default:
                    sb.Append("\"").Append(EscapeJson(value.ToString())).Append("\"");
                    return;
            }
        }

        private static string EscapeJson(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(text.Length + 16);
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\"':
                        sb.Append("\\\"");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (ch < 32)
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        private static long Percentile(IList<long> values, double ratio)
        {
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            double clamped = Math.Max(0, Math.Min(1, ratio));
            List<long> ordered = values.OrderBy(item => item).ToList();
            if (ordered.Count == 1)
            {
                return ordered[0];
            }

            double index = (ordered.Count - 1) * clamped;
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);
            if (lower == upper)
            {
                return ordered[lower];
            }

            double fraction = index - lower;
            double value = ordered[lower] + (ordered[upper] - ordered[lower]) * fraction;
            return (long)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static double ComputeSlopePerHour(IList<RuntimeSample> samples, Func<RuntimeSample, double> valueSelector)
        {
            if (samples == null || samples.Count < 2)
            {
                return 0;
            }

            RuntimeSample first = samples[0];
            double sumX = 0;
            double sumY = 0;
            double sumXX = 0;
            double sumXY = 0;
            int count = 0;

            foreach (RuntimeSample sample in samples)
            {
                if (sample == null)
                {
                    continue;
                }
                double x = (sample.TimeUtc - first.TimeUtc).TotalHours;
                double y = valueSelector(sample);
                sumX += x;
                sumY += y;
                sumXX += x * x;
                sumXY += x * y;
                count++;
            }

            if (count < 2)
            {
                return 0;
            }

            double denominator = count * sumXX - sumX * sumX;
            if (Math.Abs(denominator) < 1e-9)
            {
                return 0;
            }
            return (count * sumXY - sumX * sumY) / denominator;
        }

        private static int ResolveDurationMinutes(LongRunScenario scenario, string envKey, int fallbackMinutes, int min, int max)
        {
            int profileDefault = GetProfileDefaultDurationMinutes(scenario, fallbackMinutes);
            return ReadEnvInt(envKey, profileDefault, min, max);
        }

        private static int ResolveSampleIntervalMs(int fallback)
        {
            int profileDefault = GetProfileDefaultSampleMs(fallback);
            return ReadEnvInt(SampleMsEnv, profileDefault, 1000, 10 * 60 * 1000);
        }

        private static int GetProfileDefaultDurationMinutes(LongRunScenario scenario, int fallbackMinutes)
        {
            switch (ResolveProfileName())
            {
                case "smoke":
                    return 1;
                case "gate":
                    switch (scenario)
                    {
                        case LongRunScenario.Soak:
                            return 60;
                        case LongRunScenario.Jitter:
                            return 30;
                        case LongRunScenario.Memory:
                            return 60;
                        case LongRunScenario.Handle:
                            return 60;
                        default:
                            return fallbackMinutes;
                    }
                case "formal24h":
                    switch (scenario)
                    {
                        case LongRunScenario.Soak:
                            return 24 * 60;
                        case LongRunScenario.Jitter:
                            return 120;
                        case LongRunScenario.Memory:
                            return 24 * 60;
                        case LongRunScenario.Handle:
                            return 24 * 60;
                        default:
                            return fallbackMinutes;
                    }
                case "formal72h":
                    switch (scenario)
                    {
                        case LongRunScenario.Soak:
                            return 72 * 60;
                        case LongRunScenario.Jitter:
                            return 240;
                        case LongRunScenario.Memory:
                            return 72 * 60;
                        case LongRunScenario.Handle:
                            return 72 * 60;
                        default:
                            return fallbackMinutes;
                    }
                default:
                    return fallbackMinutes;
            }
        }

        private static int GetProfileDefaultSampleMs(int fallback)
        {
            switch (ResolveProfileName())
            {
                case "smoke":
                    return 2000;
                case "gate":
                    return 5000;
                default:
                    return fallback;
            }
        }

        private static string ResolveProfileName()
        {
            string raw = Environment.GetEnvironmentVariable(LongRunProfileEnv);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "default";
            }
            string normalized = raw.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "smoke":
                case "gate":
                    return normalized;
                case "formal24":
                case "formal24h":
                    return "formal24h";
                case "formal72":
                case "formal72h":
                    return "formal72h";
                default:
                    return "default";
            }
        }

        private static Proc BuildTypicalProductionProc(string name)
        {
            Delay op1 = new Delay
            {
                Id = Guid.NewGuid(),
                Num = 0,
                timeMiniSecond = "50"
            };
            Delay op2 = new Delay
            {
                Id = Guid.NewGuid(),
                Num = 1,
                timeMiniSecond = "50"
            };

            return new Proc
            {
                head = new ProcHead
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Disable = false
                },
                steps = new List<Step>
                {
                    new Step
                    {
                        Id = Guid.NewGuid(),
                        Name = "步骤0",
                        Ops = new List<OperationType> { op1, op2 }
                    }
                }
            };
        }

        private static Proc BuildProcWithSingleOp(OperationType op, string name)
        {
            if (op == null)
            {
                throw new ArgumentNullException(nameof(op));
            }
            if (op.Id == Guid.Empty)
            {
                op.Id = Guid.NewGuid();
            }
            op.Num = 0;

            return new Proc
            {
                head = new ProcHead
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Disable = false
                },
                steps = new List<Step>
                {
                    new Step
                    {
                        Id = Guid.NewGuid(),
                        Name = "步骤0",
                        Ops = new List<OperationType> { op }
                    }
                }
            };
        }

        private static ProcessEngine CreateEngine(IList<Proc> procs, ValueConfigStore valueStore = null)
        {
            EngineContext context = new EngineContext
            {
                Procs = procs ?? new List<Proc>(),
                ValueStore = valueStore ?? new ValueConfigStore(),
                DataStructStore = new DataStructStore(),
                TrayPointStore = new TrayPointStore(),
                CardStore = new CardConfigStore(),
                Motion = null,
                Comm = null,
                PlcStore = new PlcConfigStore(),
                AlarmInfoStore = new AlarmInfoStore(),
                IoMap = new Dictionary<string, IO>(),
                Stations = new List<DataStation>(),
                SocketInfos = new List<SocketInfo>(),
                SerialPortInfos = new List<SerialPortInfo>(),
                CustomFunc = new CustomFunc(),
                AxisStateBitGetter = null
            };

            return new ProcessEngine(context)
            {
                Logger = new SilentLogger()
            };
        }

        private static void EnsureLongRunEnabledOrIgnore()
        {
            string value = Environment.GetEnvironmentVariable(LongRunEnableEnv);
            if (!string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Ignore($"长稳压测默认关闭。设置环境变量 {LongRunEnableEnv}=1 后再执行。");
            }
        }

        private static int ReadEnvInt(string key, int defaultValue, int min, int max)
        {
            string text = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(text))
            {
                return defaultValue;
            }
            if (!int.TryParse(text, out int value))
            {
                return defaultValue;
            }
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        private static int ReadEnvIntAllowNegative(string key, int defaultValue)
        {
            string text = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(text))
            {
                return defaultValue;
            }
            if (!int.TryParse(text, out int value))
            {
                return defaultValue;
            }
            return value;
        }

        private static double ReadEnvDouble(string key, double defaultValue, double min, double max)
        {
            string text = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(text))
            {
                return defaultValue;
            }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return defaultValue;
            }
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(100);
        }

        [DllImport("user32.dll")]
        private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

        private sealed class FaultStats
        {
            public int Total { get; set; }
            public long TotalLatencyMs { get; set; }
            public long MaxLatencyMs { get; set; }
            public string LastAlarmMessage { get; set; }
        }

        private sealed class AlarmObservation
        {
            public FaultMode Mode { get; set; }
            public long AlarmLatencyMs { get; set; }
            public string AlarmMessage { get; set; }
            public DateTime TimeUtc { get; set; }
        }

        private sealed class RuntimeSample
        {
            public DateTime TimeUtc { get; set; }
            public long PrivateBytes { get; set; }
            public long WorkingSetBytes { get; set; }
            public long ManagedBytes { get; set; }
            public int HandleCount { get; set; }
            public int ThreadCount { get; set; }
            public int GdiCount { get; set; }
            public int UserCount { get; set; }
        }

        private sealed class SilentLogger : ILogger
        {
            public void Log(string message, LogLevel level)
            {
            }
        }
    }
}
