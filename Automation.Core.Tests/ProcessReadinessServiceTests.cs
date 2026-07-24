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
    }
}
