using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

// 模块：核心测试 / Machine Agent 停止控制。
// 职责范围：验证停止预演冻结运行实例、按 runId 比较后停止以及时间线结果。

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class MachineProcessStopTests
    {
        [TestMethod]
        public void 停止预演只停止冻结实例并形成可验证时间线()
        {
            using (var fixture = new StopRuntimeFixture())
            {
                EngineSnapshot running = fixture.StartAndWaitForActiveRun();
                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessStop(
                    new MachineProcessStopPreviewRequest
                    {
                        ProcId = fixture.Process.head.Id.ToString("D"),
                        Reason = "人工确认后停止当前实例以检查异常"
                    });

                Assert.AreEqual("machine.process_stop.preview.v1", preview.Value<string>("contract"));
                Assert.IsTrue(preview.Value<bool>("executable"));
                Assert.AreEqual(running.RunId.ToString("D"), preview["target"]?["runId"]?.Value<string>());

                JObject result = fixture.Runtime.MachineAgent.ExecuteProcessStop(
                    preview.Value<string>("previewId"));
                Assert.IsTrue(result.Value<bool>("accepted"));
                Assert.AreEqual(running.RunId.ToString("D"), result.Value<string>("runId"));
                Assert.IsTrue(result.Value<bool>("timelineTracked"));
                string actionId = result.Value<string>("actionId");
                Assert.IsFalse(string.IsNullOrWhiteSpace(actionId));
                MachineAgentControlException consumed = Assert.ThrowsExactly<MachineAgentControlException>(() =>
                    fixture.Runtime.MachineAgent.ExecuteProcessStop(preview.Value<string>("previewId")));
                Assert.AreEqual("MACHINE_PREVIEW_NOT_FOUND", consumed.Code);
                fixture.WaitForInactive();

                Assert.IsTrue(SpinWait.SpinUntil(() => fixture.History.GetRecentWindow(100).Events.Any(item =>
                    item.ActionId == actionId
                    && item.EventType == EquipmentStateEventTypes.MachineActionOutcomeObserved
                    && item.Outcome == "verified"), TimeSpan.FromSeconds(2)),
                    "停止完成后没有形成机械验证的结果观测。");
                List<EquipmentStateHistoryEvent> events = fixture.History.GetRecentWindow(100).Events;
                Assert.IsTrue(events.Any(item => item.ActionId == actionId
                    && item.EventType == EquipmentStateEventTypes.MachineActionStarted));
                Assert.IsTrue(events.Any(item => item.ActionId == actionId
                    && item.EventType == EquipmentStateEventTypes.MachineActionCompleted
                    && item.Outcome == "stopped"));
            }
        }

        [TestMethod]
        public void 同一运行实例的多次停止尝试按AttemptId独立归因()
        {
            using (var fixture = new StopRuntimeFixture())
            {
                EngineSnapshot running = fixture.StartAndWaitForActiveRun();
                string unacceptedActionId = fixture.Timeline.BeginMachineStop(
                    "先登记但未下发的动作",
                    fixture.Process.head.Id,
                    running.RunId,
                    0,
                    running.StepIndex,
                    running.OpIndex,
                    running.ProcName,
                    "验证停止尝试不能互相认领");
                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessStop(
                    new MachineProcessStopPreviewRequest
                    {
                        ProcId = fixture.Process.head.Id.ToString("D"),
                        Reason = "执行第二次且真正下发的停止"
                    });

                JObject result = fixture.Runtime.MachineAgent.ExecuteProcessStop(
                    preview.Value<string>("previewId"));
                Assert.IsTrue(result.Value<bool>("accepted"));
                Assert.IsTrue(result.Value<bool>("timelineTracked"));
                string acceptedActionId = result.Value<string>("actionId");
                Assert.IsFalse(string.IsNullOrWhiteSpace(acceptedActionId));
                fixture.WaitForInactive();

                List<EquipmentStateHistoryEvent> events = fixture.History.GetRecentWindow(100).Events;
                Assert.IsTrue(events.Any(item => item.ActionId == unacceptedActionId
                    && item.EventType == EquipmentStateEventTypes.MachineActionFailed
                    && item.Outcome == "dispatch_not_accepted"));
                Assert.IsFalse(events.Any(item => item.ActionId == unacceptedActionId
                    && item.EventType == EquipmentStateEventTypes.MachineActionCompleted));
                Assert.IsTrue(events.Any(item => item.ActionId == acceptedActionId
                    && item.EventType == EquipmentStateEventTypes.MachineActionCompleted
                    && item.Outcome == "stopped"));
            }
        }

        [TestMethod]
        public void 停止中的同一运行实例可由新预演幂等重申()
        {
            using (var fixture = new StopRuntimeFixture())
            {
                EngineSnapshot running = fixture.StartAndWaitForActiveRun();
                Guid firstAttemptId = Guid.NewGuid();
                JObject reasserted = null;
                fixture.Engine.ProcessStopRequested += requested =>
                {
                    if (requested.AttemptId != firstAttemptId) return;
                    JObject preview = fixture.Runtime.MachineAgent.PreviewProcessStop(
                        new MachineProcessStopPreviewRequest
                        {
                            ProcId = fixture.Process.head.Id.ToString("D"),
                            Reason = "停止中的实例仍需重申硬件停止"
                    });
                    Assert.IsTrue(preview.Value<bool>("executable"));
                    reasserted = fixture.Runtime.MachineAgent.ExecuteProcessStop(
                        preview.Value<string>("previewId"));
                };

                ProcessStopRequestResult first = fixture.Runtime.ProcessControl.Stop(
                    0, running.RunId, firstAttemptId);

                Assert.IsTrue(first.Accepted);
                Assert.IsNotNull(reasserted);
                Assert.IsTrue(reasserted.Value<bool>("accepted"));
                Assert.IsTrue(reasserted.Value<bool>("reasserted"));
                Assert.AreEqual("reasserted", reasserted.Value<string>("dispatchStatus"));
                fixture.WaitForInactive();

                string actionId = reasserted.Value<string>("actionId");
                Assert.IsTrue(SpinWait.SpinUntil(() => fixture.History.GetRecentWindow(100).Events.Any(item =>
                    item.ActionId == actionId
                    && item.EventType == EquipmentStateEventTypes.MachineActionCompleted
                    && item.Outcome == "stop_reasserted"), TimeSpan.FromSeconds(2)));
            }
        }

        [TestMethod]
        public void 未被原子接受的停止预登记不会冒充成功动作()
        {
            using (var fixture = new StopRuntimeFixture())
            {
                EngineSnapshot running = fixture.StartAndWaitForActiveRun();
                fixture.Engine.SnapshotThrottleMilliseconds = 0;
                string actionId = fixture.Timeline.BeginMachineStop(
                    "尚未下发的预登记",
                    fixture.Process.head.Id,
                    running.RunId,
                    0,
                    running.StepIndex,
                    running.OpIndex,
                    running.ProcName,
                    "由其他控制源抢先停止");

                Assert.IsTrue(fixture.Runtime.ProcessControl.Stop(0));
                fixture.WaitForInactive();
                Assert.IsTrue(SpinWait.SpinUntil(() => fixture.History.GetRecentWindow(100).Events.Any(item =>
                    item.ActionId == actionId
                    && item.EventType == EquipmentStateEventTypes.MachineActionFailed
                    && item.Outcome == "dispatch_not_accepted"), TimeSpan.FromSeconds(2)));
                Assert.IsFalse(fixture.History.GetRecentWindow(100).Events.Any(item =>
                    item.ActionId == actionId
                    && item.EventType == EquipmentStateEventTypes.MachineActionCompleted));
                Assert.IsFalse(fixture.History.GetRecentWindow(100).Events.Any(item =>
                    item.ActionId == actionId
                    && (item.EventType == EquipmentStateEventTypes.ProcessCompleted
                        || item.EventType == EquipmentStateEventTypes.ProcessPositionChanged)),
                    "未被流程引擎原子接受的停止 attempt 不得认领流程事实。");
            }
        }

        [TestMethod]
        public void 旧停止预演不能碰触后来启动的新实例()
        {
            using (var fixture = new StopRuntimeFixture())
            {
                EngineSnapshot first = fixture.StartAndWaitForActiveRun();
                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessStop(
                    new MachineProcessStopPreviewRequest
                    {
                        ProcId = fixture.Process.head.Id.ToString("D"),
                        Reason = "冻结第一个运行实例"
                    });

                Assert.IsFalse(fixture.Runtime.ProcessControl.Stop(0, Guid.NewGuid()),
                    "错误 runId 不得停止当前实例。");
                EngineSnapshot stillFirst = fixture.Engine.GetSnapshot(0);
                Assert.AreEqual(first.RunId, stillFirst.RunId);
                Assert.IsFalse(stillFirst.State.IsInactive());

                Assert.IsTrue(fixture.Runtime.ProcessControl.Stop(0));
                fixture.WaitForInactive();
                EngineSnapshot second = fixture.StartAndWaitForActiveRun();
                Assert.AreNotEqual(first.RunId, second.RunId);

                MachineAgentControlException error = Assert.ThrowsExactly<MachineAgentControlException>(() =>
                    fixture.Runtime.MachineAgent.ExecuteProcessStop(preview.Value<string>("previewId")));
                Assert.AreEqual("MACHINE_PROCESS_INSTANCE_CHANGED", error.Code);
                EngineSnapshot afterRejectedStop = fixture.Engine.GetSnapshot(0);
                Assert.AreEqual(second.RunId, afterRejectedStop.RunId);
                Assert.IsFalse(afterRejectedStop.State.IsInactive());
            }
        }

        private sealed class StopRuntimeFixture : IDisposable
        {
            private readonly TemporaryDirectory directory = new TemporaryDirectory();
            private readonly EquipmentProcessTimelineService timeline;

            public StopRuntimeFixture()
            {
                Runtime = new PlatformRuntime(directory.FullPath);
                Process = CreateLongRunningProcess();
                Runtime.Stores.Processes.ReplaceAll(new[] { Process });
                Engine = new ProcessEngine(new EngineContext
                {
                    Procs = new List<Proc> { Process },
                    ValueStore = Runtime.Stores.Values,
                    DataStructStore = Runtime.Stores.DataStructures,
                    Maintenance = Runtime.Maintenance,
                    Safety = Runtime.Safety,
                    Readiness = Runtime.Readiness,
                    Paths = Runtime.Paths,
                    ValidationContextFactory = Runtime.CreateProcessValidationContext
                });
                Runtime.ProcessEngine = Engine;
                Runtime.ProcessControl = new ProcessRuntimeControl(Engine);
                History = new EquipmentStateHistoryService();
                Runtime.StateHistory = History;
                timeline = new EquipmentProcessTimelineService(
                    Engine, Runtime.Stores.Topology, History);
                Runtime.ProcessTimeline = timeline;
            }

            public PlatformRuntime Runtime { get; }
            public Proc Process { get; }
            public ProcessEngine Engine { get; }
            public EquipmentStateHistoryService History { get; }
            public EquipmentProcessTimelineService Timeline => timeline;

            public EngineSnapshot StartAndWaitForActiveRun()
            {
                Assert.IsTrue(Engine.StartProc(Process, 0));
                EngineSnapshot snapshot = null;
                Assert.IsTrue(SpinWait.SpinUntil(() =>
                {
                    snapshot = Engine.GetSnapshot(0);
                    return snapshot != null
                        && snapshot.RunId != Guid.Empty
                        && !snapshot.State.IsInactive();
                }, TimeSpan.FromSeconds(2)), "流程未进入活动运行状态。");
                return snapshot;
            }

            public void WaitForInactive()
            {
                Assert.IsTrue(SpinWait.SpinUntil(() =>
                    Engine.GetSnapshot(0)?.State.IsInactive() == true,
                    TimeSpan.FromSeconds(2)), "流程未在期限内停止。");
            }

            public void Dispose()
            {
                try
                {
                    Runtime.ProcessControl?.Stop(0);
                    SpinWait.SpinUntil(() =>
                        Engine.GetSnapshot(0)?.State.IsInactive() == true,
                        TimeSpan.FromSeconds(2));
                }
                finally
                {
                    timeline.Dispose();
                    History.Dispose();
                    Engine.Dispose();
                    directory.Dispose();
                }
            }

            private static Proc CreateLongRunningProcess()
            {
                var process = new Proc
                {
                    head = new ProcHead { Id = Guid.NewGuid(), Name = "停止测试流程" }
                };
                var step = new Step { Id = Guid.NewGuid(), Name = "运行步骤" };
                step.Ops.Add(new Delay
                {
                    Id = Guid.NewGuid(),
                    Name = "长时间运行",
                    OperaType = "延时",
                    DelayMs = 30000
                });
                process.steps.Add(step);
                return process;
            }
        }
    }
}
