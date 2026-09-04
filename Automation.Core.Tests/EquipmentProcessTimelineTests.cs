using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

// 模块：核心测试 / 设备状态时间线。
// 职责范围：验证流程生命周期与 Machine Agent 动作结果进入同一条可关联时间线。

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class EquipmentProcessTimelineTests
    {
        [TestMethod]
        public void MachineAgent动作关联流程运行并形成开始完成及结果观测事件()
        {
            using (var directory = new TemporaryDirectory())
            using (var history = new EquipmentStateHistoryService())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Proc process = CreateProcess(out Delay operation);
                runtime.Stores.Processes.ReplaceAll(new[] { process });
                using (var engine = new ProcessEngine(new EngineContext
                {
                    Procs = new List<Proc> { process },
                    ValueStore = runtime.Stores.Values,
                    DataStructStore = runtime.Stores.DataStructures,
                    Maintenance = runtime.Maintenance,
                    Safety = runtime.Safety,
                    Readiness = runtime.Readiness,
                    Paths = runtime.Paths,
                    ValidationContextFactory = runtime.CreateProcessValidationContext
                }))
                using (var timeline = new EquipmentProcessTimelineService(
                    engine, runtime.Stores.Topology, history))
                {
                    string actionId = timeline.BeginMachineAction(
                        "preview-1",
                        "skill-1",
                        process.head.Id,
                        operation.Id,
                        0,
                        0,
                        "node-1",
                        "测试节点",
                        Automation.Protocol.MachineExecutionModes.SingleOperation,
                        "节点到达目标状态");

                    Assert.IsTrue(engine.RunSingleOpOnce(process, 0, 0, 0));
                    Assert.IsTrue(SpinWait.SpinUntil(
                        () => history.GetRecentWindow(100).Events.Any(item =>
                            item.EventType == EquipmentStateEventTypes.MachineActionCompleted
                            && item.ActionId == actionId),
                        TimeSpan.FromSeconds(2)),
                        "流程结束后没有形成 Machine Agent 完成事件。");

                    List<EquipmentStateHistoryEvent> events = history.GetRecentWindow(100).Events;
                    EquipmentStateHistoryEvent started = events.Single(item =>
                        item.EventType == EquipmentStateEventTypes.MachineActionStarted
                        && item.ActionId == actionId);
                    EquipmentStateHistoryEvent processStarted = events.Single(item =>
                        item.EventType == EquipmentStateEventTypes.ProcessStarted
                        && item.ActionId == actionId);
                    EquipmentStateHistoryEvent completed = events.Single(item =>
                        item.EventType == EquipmentStateEventTypes.MachineActionCompleted
                        && item.ActionId == actionId);
                    EquipmentStateHistoryEvent outcome = events.Single(item =>
                        item.EventType == EquipmentStateEventTypes.MachineActionOutcomeObserved
                        && item.ActionId == actionId);

                    Assert.AreEqual(operation.Id.ToString("D"), started.OperationId);
                    Assert.AreEqual("skill-1", processStarted.SkillId);
                    Assert.AreEqual(started.Sequence, completed.CausedBySequence);
                    Assert.AreEqual("observed_unverified", outcome.Outcome);
                    Assert.AreEqual(EquipmentStateQualities.Unknown, outcome.Quality);
                    Assert.AreEqual("节点到达目标状态", outcome.ExpectedOutcome);
                    Assert.IsTrue(events.Any(item =>
                        item.EventType == EquipmentStateEventTypes.ProcessPositionChanged
                        && item.OperationId == operation.Id.ToString("D")));
                    Assert.IsTrue(events.Any(item =>
                        item.EventType == EquipmentStateEventTypes.ProcessCompleted
                        && item.RunId == processStarted.RunId));
                }
            }
        }

        [TestMethod]
        public void 外部停止单指令时不会把取消返回误报为动作完成()
        {
            using (var directory = new TemporaryDirectory())
            using (var history = new EquipmentStateHistoryService())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Proc process = CreateProcess(out Delay operation);
                operation.DelayMs = 30000;
                runtime.Stores.Processes.ReplaceAll(new[] { process });
                using (var engine = new ProcessEngine(new EngineContext
                {
                    Procs = new List<Proc> { process },
                    ValueStore = runtime.Stores.Values,
                    DataStructStore = runtime.Stores.DataStructures,
                    Maintenance = runtime.Maintenance,
                    Safety = runtime.Safety,
                    Readiness = runtime.Readiness,
                    Paths = runtime.Paths,
                    ValidationContextFactory = runtime.CreateProcessValidationContext
                }))
                using (var timeline = new EquipmentProcessTimelineService(
                    engine, runtime.Stores.Topology, history))
                {
                    string actionId = timeline.BeginMachineAction(
                        "preview-interrupted",
                        "skill-interrupted",
                        process.head.Id,
                        operation.Id,
                        0,
                        0,
                        "node-interrupted",
                        "中断测试节点",
                        Automation.Protocol.MachineExecutionModes.SingleOperation,
                        "动作应完整结束");

                    Assert.IsTrue(engine.RunSingleOpOnce(process, 0, 0, 0));
                    Assert.IsTrue(SpinWait.SpinUntil(() =>
                    {
                        EngineSnapshot snapshot = engine.GetSnapshot(0);
                        return snapshot != null
                            && snapshot.RunId != Guid.Empty
                            && snapshot.State == ProcRunState.Running;
                    }, TimeSpan.FromSeconds(2)));
                    EngineSnapshot active = engine.GetSnapshot(0);
                    Assert.IsTrue(engine.Stop(0, active.RunId, Guid.NewGuid()).Accepted);

                    Assert.IsTrue(SpinWait.SpinUntil(() => history.GetRecentWindow(100).Events.Any(item =>
                        item.ActionId == actionId
                        && item.EventType == EquipmentStateEventTypes.MachineActionFailed),
                        TimeSpan.FromSeconds(2)));
                    List<EquipmentStateHistoryEvent> events = history.GetRecentWindow(100).Events;
                    Assert.IsFalse(events.Any(item => item.ActionId == actionId
                        && item.EventType == EquipmentStateEventTypes.MachineActionCompleted));
                }
            }
        }

        private static Proc CreateProcess(out Delay operation)
        {
            operation = new Delay
            {
                Id = Guid.NewGuid(),
                Name = "执行目标",
                OperaType = "延时",
                DelayMs = 1
            };
            var process = new Proc
            {
                head = new ProcHead { Id = Guid.NewGuid(), Name = "时间线测试流程" }
            };
            var step = new Step { Id = Guid.NewGuid(), Name = "步骤" };
            step.Ops.Add(operation);
            process.steps.Add(step);
            return process;
        }
    }
}
