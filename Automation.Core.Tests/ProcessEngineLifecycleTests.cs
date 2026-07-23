using System;
// 模块：核心测试 / 流程引擎生命周期。
// 职责范围：验证无 UI 运行与重复启动保护；失败的重复请求不得替换当前实例。

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class ProcessEngineLifecycleTests
    {
        [TestMethod]
        public void StartProc_WhenInstanceIsRunning_RejectsDuplicateAndKeepsCurrentInstance()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = TestProcessFactory.CreateEndingProcess("重复启动保护流程", 2000);
                var runtime = new PlatformRuntime(directory.FullPath);
                var context = new EngineContext
                {
                    Procs = new List<Proc> { process },
                    Maintenance = runtime.Maintenance,
                    Safety = runtime.Safety,
                    Readiness = runtime.Readiness,
                    Paths = runtime.Paths
                };
                using (var engine = new ProcessEngine(context))
                {
                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForState(engine, ProcRunState.Running, TimeSpan.FromSeconds(3));
                    EngineSnapshot running = engine.GetSnapshot(0);

                    Assert.IsFalse(engine.StartProc(process, 0));

                    EngineSnapshot afterDuplicateStart = engine.GetSnapshot(0);
                    Assert.AreEqual(ProcRunState.Running, afterDuplicateStart.State);
                    Assert.AreEqual(running.ProcId, afterDuplicateStart.ProcId);
                    Assert.AreEqual(running.AppliedRevision, afterDuplicateStart.AppliedRevision);

                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void Step_WhenOperationCompletes_ParksAtNextUnexecutedOperation()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(
                    CreateStep("顺序执行",
                        new Delay { Id = Guid.NewGuid(), Name = "第一条", DelayMs = 0 },
                        new Delay { Id = Guid.NewGuid(), Name = "第二条", DelayMs = 0 },
                        new EndProcess { Id = Guid.NewGuid() }));
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Assert.IsTrue(engine.StartProcAt(process, 0, 0, 0, ProcRunState.SingleStep));
                    WaitForPosition(engine, ProcRunState.SingleStep, 0, 0, TimeSpan.FromSeconds(3));

                    Assert.IsTrue(engine.Step(0));

                    WaitForPosition(engine, ProcRunState.SingleStep, 0, 1, TimeSpan.FromSeconds(3));
                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void Step_WhenFollowingPositionsAreDisabled_ParksAtNextEnabledOperationAcrossSteps()
        {
            using (var directory = new TemporaryDirectory())
            {
                Step disabledStep = CreateStep("禁用步骤",
                    new Delay { Id = Guid.NewGuid(), Name = "不会执行", DelayMs = 0 });
                disabledStep.Disable = true;
                Proc process = CreateProcess(
                    CreateStep("第一步",
                        new Delay { Id = Guid.NewGuid(), Name = "当前指令", DelayMs = 0 }),
                    disabledStep,
                    CreateStep("第三步",
                        new Delay { Id = Guid.NewGuid(), Name = "禁用指令", DelayMs = 0, Disable = true },
                        new Delay { Id = Guid.NewGuid(), Name = "下一条有效指令", DelayMs = 0 },
                        new EndProcess { Id = Guid.NewGuid() }));
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Assert.IsTrue(engine.StartProcAt(process, 0, 0, 0, ProcRunState.SingleStep));
                    WaitForPosition(engine, ProcRunState.SingleStep, 0, 0, TimeSpan.FromSeconds(3));

                    Assert.IsTrue(engine.Step(0));

                    WaitForPosition(engine, ProcRunState.SingleStep, 2, 1, TimeSpan.FromSeconds(3));
                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void Step_WhenGotoTargetsDisabledOperation_ParksAtNextEnabledTarget()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(
                    CreateStep("跳转",
                        new Goto
                        {
                            Id = Guid.NewGuid(),
                            Name = "跳转到第二步",
                            DefaultGoto = "0-1-0"
                        }),
                    CreateStep("目标步骤",
                        new Delay { Id = Guid.NewGuid(), Name = "禁用目标", DelayMs = 0, Disable = true },
                        new Delay { Id = Guid.NewGuid(), Name = "下一条有效指令", DelayMs = 0 },
                        new EndProcess { Id = Guid.NewGuid() }));
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Assert.IsTrue(engine.StartProcAt(process, 0, 0, 0, ProcRunState.SingleStep));
                    WaitForPosition(engine, ProcRunState.SingleStep, 0, 0, TimeSpan.FromSeconds(3));

                    Assert.IsTrue(engine.Step(0));

                    WaitForPosition(engine, ProcRunState.SingleStep, 1, 1, TimeSpan.FromSeconds(3));
                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void SetDebugStartPoint_WhenSingleStepIsWaiting_RepositionsCurrentInstance()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(
                    CreateStep("调试跳转",
                        new EndProcess { Id = Guid.NewGuid(), Name = "不得执行的旧位置" },
                        new Delay { Id = Guid.NewGuid(), Name = "新启动点", DelayMs = 0 },
                        new Delay { Id = Guid.NewGuid(), Name = "下一条", DelayMs = 0 },
                        new EndProcess { Id = Guid.NewGuid() }));
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Assert.IsTrue(engine.StartProcAt(
                        process, 0, 0, 0, ProcRunState.SingleStep));
                    WaitForPosition(
                        engine, ProcRunState.SingleStep, 0, 0, TimeSpan.FromSeconds(3));
                    Guid runId = engine.GetSnapshot(0).RunId;

                    Assert.IsTrue(
                        engine.TrySetDebugStartPoint(process, 0, 0, 1, out string error),
                        error);

                    WaitForPosition(
                        engine, ProcRunState.SingleStep, 0, 1, TimeSpan.FromSeconds(3));
                    Assert.AreEqual(runId, engine.GetSnapshot(0).RunId);

                    Assert.IsTrue(engine.Step(0));
                    WaitForPosition(
                        engine, ProcRunState.SingleStep, 0, 2, TimeSpan.FromSeconds(3));
                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void SetDebugStartPoint_WhenSingleStepOperationIsExecuting_RejectsReposition()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(
                    CreateStep("执行中保护",
                        new Delay { Id = Guid.NewGuid(), Name = "执行中的指令", DelayMs = 1000 },
                        new Delay { Id = Guid.NewGuid(), Name = "不可跳转", DelayMs = 0 },
                        new EndProcess { Id = Guid.NewGuid() }));
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Assert.IsTrue(engine.StartProcAt(
                        process, 0, 0, 0, ProcRunState.SingleStep));
                    WaitForPosition(
                        engine, ProcRunState.SingleStep, 0, 0, TimeSpan.FromSeconds(3));
                    Assert.IsTrue(engine.Step(0));
                    WaitForState(engine, ProcRunState.Running, TimeSpan.FromSeconds(3));

                    Assert.IsFalse(
                        engine.TrySetDebugStartPoint(process, 0, 0, 1, out string error));
                    StringAssert.Contains(error, "指令尚未执行");
                    Assert.AreEqual(0, engine.GetSnapshot(0).OpIndex);

                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void SetDebugStartPoint_WhenProcessIsInactive_StartsSingleStepAtSelectedPosition()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(
                    CreateStep("初始启动点",
                        new EndProcess { Id = Guid.NewGuid(), Name = "跳过" },
                        new Delay { Id = Guid.NewGuid(), Name = "选中位置", DelayMs = 0 },
                        new EndProcess { Id = Guid.NewGuid() }));
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Assert.IsTrue(
                        engine.TrySetDebugStartPoint(process, 0, 0, 1, out string error),
                        error);

                    WaitForPosition(
                        engine, ProcRunState.SingleStep, 0, 1, TimeSpan.FromSeconds(3));
                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void RequestStep_WhenSignalIsPending_ReturnsFalseInsteadOfSilentlyDroppingCommand()
        {
            using (var control = new ProcessControl())
            {
                Assert.IsTrue(control.RequestStep());
                Assert.IsFalse(control.RequestStep());
                Assert.IsFalse(control.TryRequestSingleStepReposition(1, 2));

                control.WaitForStep();
                control.CompleteStepWake();

                Assert.IsTrue(control.RequestStep());
            }
        }

        [TestMethod]
        public void WaitForRun_WhenGateChanges_PreservesRunningAndPausedSemantics()
        {
            using (var control = new ProcessControl())
            using (var waitStarted = new ManualResetEventSlim(false))
            using (var waitCompleted = new ManualResetEventSlim(false))
            {
                control.SetRunning();
                Assert.IsTrue(control.IsRunGateOpen);
                control.WaitForRun();

                control.SetPaused();
                Assert.IsFalse(control.IsRunGateOpen);
                Task waitTask = Task.Run(() =>
                {
                    waitStarted.Set();
                    control.WaitForRun();
                    waitCompleted.Set();
                });

                Assert.IsTrue(waitStarted.Wait(TimeSpan.FromSeconds(1)));
                Assert.IsFalse(waitCompleted.Wait(TimeSpan.FromMilliseconds(100)),
                    "暂停闸门关闭后，工作线程不得穿透等待。");

                control.SetRunning();
                Assert.IsTrue(waitCompleted.Wait(TimeSpan.FromSeconds(1)),
                    "继续运行后，等待中的工作线程应立即恢复。");
                Assert.IsTrue(waitTask.Wait(TimeSpan.FromSeconds(1)));
            }
        }

        [TestMethod]
        public void RunningSelfGoto_WhenPausedAndResumed_RemainsControllable()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess(
                    CreateStep("持续运行",
                        new Goto
                        {
                            Id = Guid.NewGuid(),
                            Name = "自跳转",
                            DefaultGoto = "0-0-0"
                        }));
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, process))
                {
                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForState(engine, ProcRunState.Running, TimeSpan.FromSeconds(3));

                    engine.Pause(0);
                    WaitForState(engine, ProcRunState.Paused, TimeSpan.FromSeconds(3));
                    Thread.Sleep(100);
                    Assert.AreEqual(ProcRunState.Paused, engine.GetSnapshot(0).State);

                    engine.Resume(0);
                    WaitForState(engine, ProcRunState.Running, TimeSpan.FromSeconds(3));
                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void PositionTracking_WhenPositionIsUnchanged_DoesNotPublishNewRevision()
        {
            var handle = new ProcHandle
            {
                stepNum = 1,
                opsNum = 2
            };
            handle.InitializePositionTracking();

            handle.MarkPositionChanged();
            Assert.IsFalse(handle.HasUnpublishedPosition);

            handle.opsNum = 3;
            handle.MarkPositionChanged();
            Assert.IsTrue(handle.HasUnpublishedPosition);
            long revision = handle.CapturePositionRevision();
            handle.MarkPositionSnapshotPublished(revision);
            Assert.IsFalse(handle.HasUnpublishedPosition);

            handle.MarkPositionChanged();
            Assert.IsFalse(handle.HasUnpublishedPosition);
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

        private static Proc CreateProcess(params Step[] steps)
        {
            var process = new Proc
            {
                head = new ProcHead
                {
                    Id = Guid.NewGuid(),
                    Name = "单步位置回归流程"
                }
            };
            process.steps.AddRange(steps);
            return process;
        }

        private static Step CreateStep(string name, params OperationType[] operations)
        {
            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = name
            };
            step.Ops.AddRange(operations);
            return step;
        }

        private static void WaitForState(
            ProcessEngine engine,
            ProcRunState expectedState,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.GetSnapshot(0).State == expectedState)
                {
                    return;
                }
                Thread.Sleep(20);
            }
            Assert.Fail($"等待流程状态超时：{expectedState}；当前状态：{engine.GetSnapshot(0).State}。");
        }

        private static void WaitForPosition(
            ProcessEngine engine,
            ProcRunState expectedState,
            int expectedStepIndex,
            int expectedOperationIndex,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                EngineSnapshot snapshot = engine.GetSnapshot(0);
                if (snapshot.State == expectedState
                    && snapshot.StepIndex == expectedStepIndex
                    && snapshot.OpIndex == expectedOperationIndex)
                {
                    return;
                }
                Thread.Sleep(10);
            }
            EngineSnapshot current = engine.GetSnapshot(0);
            Assert.Fail(
                $"等待单步位置超时：{expectedState} {expectedStepIndex}-{expectedOperationIndex}；" +
                $"当前位置：{current.State} {current.StepIndex}-{current.OpIndex}。");
        }
    }
}
