using System;
// 模块：核心测试 / 流程引擎观测。
// 职责范围：验证位置变化批量发布、低频性能心跳和快速流程最终状态不丢失。

using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class ProcessEngineObservationTests
    {
        [TestMethod]
        public void RunningProcess_WhenPositionDoesNotChange_DoesNotRepeatFastSnapshots()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(
                    new Delay { Id = Guid.NewGuid(), Name = "进入长等待", DelayMs = 0 },
                    new Delay { Id = Guid.NewGuid(), Name = "长等待", DelayMs = 5000 },
                    new EndProcess { Id = Guid.NewGuid() });
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Assert.AreEqual(ProcRunState.Ready, engine.GetSnapshot(0).State);
                    engine.SnapshotThrottleMilliseconds = 20;
                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForPosition(engine, ProcRunState.Running, 0, 1, TimeSpan.FromSeconds(3));
                    Assert.AreEqual(1, engine.ActiveAgentCount);

                    long stableTicks = engine.GetSnapshot(0).UpdateTicks;
                    Thread.Sleep(200);

                    Assert.AreEqual(
                        stableTicks,
                        engine.GetSnapshot(0).UpdateTicks,
                        "位置未变化时不应按20ms刷新周期重复创建快照。");

                    WaitForSnapshotAfter(engine, stableTicks, TimeSpan.FromSeconds(2));
                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                    Assert.AreEqual(ProcTerminationReason.StopRequested,
                        engine.GetSnapshot(0).TerminationReason);
                    WaitForActiveAgentCount(engine, 0, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void FastProcess_WhenCompletingBeforeThrottle_PreservesLifecycleAndFinalSnapshot()
        {
            using (var directory = new TemporaryDirectory())
            using (var readyPublished = new ManualResetEventSlim(false))
            {
                Proc process = CreateProcess(new EndProcess { Id = Guid.NewGuid() });
                var runtime = new PlatformRuntime(directory.FullPath);
                int startedCount = 0;
                int completedCount = 0;
                ProcessRunAuditSnapshot completed = default(ProcessRunAuditSnapshot);
                using (var engine = CreateEngine(runtime, process))
                {
                    engine.SnapshotThrottleMilliseconds = 100;
                    engine.ProcessStarted += _ => Interlocked.Increment(ref startedCount);
                    engine.ProcessCompleted += snapshot =>
                    {
                        completed = snapshot;
                        Interlocked.Increment(ref completedCount);
                    };
                    engine.SnapshotChanged += snapshot =>
                    {
                        if (snapshot?.State == ProcRunState.Ready
                            && snapshot.TerminationReason == ProcTerminationReason.Completed)
                        {
                            readyPublished.Set();
                        }
                    };

                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForState(engine, ProcRunState.Ready, TimeSpan.FromSeconds(3));
                    WaitForCount(ref completedCount, 1, TimeSpan.FromSeconds(3));

                    Assert.AreEqual(1, Volatile.Read(ref startedCount));
                    Assert.AreEqual(1, Volatile.Read(ref completedCount));
                    Assert.AreEqual(1L, completed.OperationCount);
                    Assert.IsTrue(readyPublished.Wait(TimeSpan.FromSeconds(2)),
                        "快速流程的最终就绪快照必须在节流窗口后仍能发布。");
                    WaitForActiveAgentCount(engine, 0, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        private static ProcessEngine CreateEngine(PlatformRuntime runtime, Proc process)
        {
            return new ProcessEngine(new EngineContext
            {
                Procs = new List<Proc> { process },
                Maintenance = runtime.Maintenance,
                Safety = runtime.Safety,
                Readiness = runtime.Readiness,
                Paths = runtime.Paths
            });
        }

        private static Proc CreateProcess(params OperationType[] operations)
        {
            var process = new Proc
            {
                head = new ProcHead { Name = "流程观测回归" }
            };
            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = "观测步骤"
            };
            step.Ops.AddRange(operations);
            process.steps.Add(step);
            return process;
        }

        private static void WaitForState(ProcessEngine engine, ProcRunState state, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.GetSnapshot(0).State == state)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"等待流程状态超时：{state}。");
        }

        private static void WaitForPosition(
            ProcessEngine engine,
            ProcRunState state,
            int stepIndex,
            int operationIndex,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                EngineSnapshot snapshot = engine.GetSnapshot(0);
                if (snapshot.State == state
                    && snapshot.StepIndex == stepIndex
                    && snapshot.OpIndex == operationIndex)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"等待流程位置超时：{state} {stepIndex}-{operationIndex}。");
        }

        private static void WaitForSnapshotAfter(ProcessEngine engine, long updateTicks, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.GetSnapshot(0).UpdateTicks > updateTicks)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail("等待性能心跳快照超时。");
        }

        private static void WaitForActiveAgentCount(ProcessEngine engine, int expected, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.ActiveAgentCount == expected)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"等待活动流程数量超时：{expected}；当前：{engine.ActiveAgentCount}。");
        }

        private static void WaitForCount(ref int value, int expected, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Volatile.Read(ref value) == expected)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"等待事件数量超时：{expected}；当前：{Volatile.Read(ref value)}。");
        }
    }
}
