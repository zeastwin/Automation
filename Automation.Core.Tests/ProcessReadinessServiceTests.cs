using System;
// 模块：核心测试 / 流程就绪性。
// 职责范围：固化“可保存”和“可运行”的关键边界，启动闸门变化应先在此处说明。

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class ProcessReadinessServiceTests
    {
        [TestMethod]
        public void Analyze_WhenProcessIsNull_ReturnsInvalidBlocker()
        {
            ProcessReadinessAnalysis analysis = ProcessReadinessService.Analyze(0, null);

            Assert.AreEqual("invalid", analysis.ReadinessStatus);
            Assert.IsFalse(analysis.Runnable);
            Assert.IsTrue(analysis.RunBlockers.Any(item => item.Contains("流程对象为空")));
        }

        [TestMethod]
        public void Analyze_WhenProcessHasNoSteps_ReturnsIncompleteWithRunBlocker()
        {
            var process = new Proc
            {
                head = new ProcHead { Name = "待完善流程" }
            };

            ProcessReadinessAnalysis analysis = ProcessReadinessService.Analyze(0, process);

            Assert.AreEqual("incomplete", analysis.ReadinessStatus);
            Assert.IsFalse(analysis.Runnable);
            Assert.IsTrue(analysis.Warnings.Any(item => item.Contains("尚未添加步骤")));
            Assert.IsTrue(analysis.RunBlockers.Any(item => item.Contains("没有可执行步骤")));
        }

        [TestMethod]
        public void Analyze_WhenProcessCanEnd_ReturnsReady()
        {
            Proc process = TestProcessFactory.CreateEndingProcess("可运行流程");

            ProcessReadinessAnalysis analysis = ProcessReadinessService.Analyze(
                0, process, new[] { process });

            Assert.AreEqual("ready", analysis.ReadinessStatus);
            Assert.IsTrue(analysis.Runnable);
            Assert.AreEqual(0, analysis.RunBlockers.Count);
        }

        [TestMethod]
        public void Analyze_WhenLegacyPendingGotoExists_RemainsRunBlocked()
        {
            Proc process = TestProcessFactory.CreateEndingProcess("历史待解析跳转");
            process.steps[0].Ops.Insert(0, new Goto
            {
                Id = Guid.NewGuid(),
                DefaultGoto = ProcessDefinitionService.PendingGotoPrefix + "bGVnYWN5"
            });

            ProcessReadinessAnalysis analysis = ProcessReadinessService.Analyze(
                0, process, new[] { process });

            Assert.AreEqual("incomplete", analysis.ReadinessStatus);
            Assert.IsFalse(analysis.Runnable);
            Assert.IsTrue(analysis.RunBlockers.Any(item =>
                item.Contains("跳转目标尚未解析")));
        }

        [TestMethod]
        public void Analyze_WhenMotionPointIsOnlyPlanned_BlocksUntilTaught()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                var station = new DataStation(false) { Name = "搬运工站" };
                DataPos point = station.ListDataPos[0];
                point.Name = "取料位";
                point.IsTaught = false;
                station.dicDataPos[point.Name] = point;
                runtime.Stores.Stations.ReplaceAll(new[] { station });

                Proc process = TestProcessFactory.CreateEndingProcess("规划点位流程");
                process.steps[0].Ops.Insert(0, new StationRunPos
                {
                    Id = Guid.NewGuid(),
                    Name = "移动到取料位",
                    StationName = station.Name,
                    PosName = point.Name,
                    PosIndex = -1
                });

                ProcessReadinessAnalysis planned = ProcessReadinessService.Analyze(
                    0, process, new[] { process }, runtime.CreateProcessValidationContext());
                Assert.AreEqual("incomplete", planned.ReadinessStatus);
                Assert.IsTrue(planned.RunBlockers.Any(item => item.Contains("尚未人工示教坐标")));

                point.IsTaught = true;
                ProcessReadinessAnalysis taught = ProcessReadinessService.Analyze(
                    0, process, new[] { process }, runtime.CreateProcessValidationContext());
                Assert.AreEqual("ready", taught.ReadinessStatus);
                Assert.IsFalse(taught.RunBlockers.Any(item => item.Contains("尚未人工示教坐标")));
            }
        }

        [TestMethod]
        public void StationStore_PreservesPlannedStateAndRebindsPointDictionary()
        {
            using (var directory = new TemporaryDirectory())
            {
                var store = new StationDefinitionStore();
                var station = new DataStation(false) { Name = "搬运工站" };
                DataPos planned = station.ListDataPos[0];
                planned.Name = "放料位";
                planned.IsTaught = false;
                station.dicDataPos[planned.Name] = new DataPos(0)
                {
                    Name = planned.Name,
                    IsTaught = true
                };
                station.dicDataPos["历史字典点位"] = new DataPos(1)
                {
                    Name = "历史字典点位",
                    IsTaught = null
                };

                Assert.IsTrue(store.TryCommit(
                    directory.FullPath, new[] { station }, out string commitError), commitError);
                DataStation committed = store.Items.Single();
                Assert.AreSame(committed.ListDataPos[0], committed.dicDataPos[planned.Name]);
                Assert.AreEqual(false, committed.ListDataPos[0].IsTaught);
                Assert.AreEqual("历史字典点位", committed.ListDataPos[1].Name);
                Assert.IsTrue(committed.ListDataPos[1].IsMotionReady);

                var reloaded = new StationDefinitionStore();
                Assert.IsTrue(reloaded.Load(directory.FullPath, out string loadError), loadError);
                DataStation loaded = reloaded.Items.Single();
                Assert.AreSame(loaded.ListDataPos[0], loaded.dicDataPos[planned.Name]);
                Assert.AreEqual("planned", loaded.ListDataPos[0].TeachingState);
            }
        }

        [TestMethod]
        public void DataPos_WhenLegacyTeachingStateIsMissing_RemainsMotionReady()
        {
            var legacy = new DataPos(0) { Name = "历史点位", IsTaught = null };

            Assert.IsTrue(legacy.IsMotionReady);
            Assert.AreEqual("taught", legacy.TeachingState);
        }
    }
}
