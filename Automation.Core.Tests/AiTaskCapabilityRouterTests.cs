using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AiTaskCapabilityRouterTests
    {
        [TestMethod]
        [DataRow("帮我设计一个扫码绑定和分流流程", AutomationToolProfiles.ProcessDesign)]
        [DataRow("新建一个扫码绑定和分流流程", AutomationToolProfiles.ProcessCreate)]
        [DataRow("检查下料流程为什么会走错分支", AutomationToolProfiles.ProcessReview)]
        [DataRow("修改下料流程里的扫码失败分支", AutomationToolProfiles.ProcessEdit)]
        [DataRow("新增一个生产计数变量", AutomationToolProfiles.ResourceEdit)]
        [DataRow("启动下料流程做一次试运行", AutomationToolProfiles.RuntimeControl)]
        [DataRow("修改 AI 助手的 MCP 工具路由代码", AutomationToolProfiles.SourceDevelopment)]
        public void Editor_RoutesToSmallestTaskProfile(string prompt, string expected)
        {
            AiTaskCapabilityDecision result = AiTaskCapabilityRouter.Route(
                prompt,
                new List<AiConversationMessage>(),
                null,
                AutomationToolProfiles.Editor,
                false);

            Assert.AreEqual(expected, result.EffectiveProfile);
            Assert.IsNull(result.Notice);
        }

        [TestMethod]
        public void DesignFollowUpExecute_TransitionsToCreate()
        {
            AiTaskCapabilityDecision result = AiTaskCapabilityRouter.Route(
                "好的，执行吧",
                new[]
                {
                    new AiConversationMessage { Role = "user", Text = "设计一个扫码流程" },
                    new AiConversationMessage { Role = "assistant", Text = "这是方案" }
                },
                AutomationToolProfiles.ProcessDesign,
                AutomationToolProfiles.Editor,
                false);

            Assert.AreEqual(AutomationToolProfiles.ProcessCreate, result.EffectiveProfile);
        }

        [TestMethod]
        public void ReferencedLongFollowUp_UsesRecentProcessContext()
        {
            AiTaskCapabilityDecision result = AiTaskCapabilityRouter.Route(
                "按照刚才的方案执行，先把未知设备动作保留为可信占位",
                new[]
                {
                    new AiConversationMessage { Role = "user", Text = "设计一个扫码绑定流程" },
                    new AiConversationMessage { Role = "assistant", Text = "这是方案" }
                },
                AutomationToolProfiles.ProcessDesign,
                AutomationToolProfiles.Editor,
                false);

            Assert.AreEqual(AutomationToolProfiles.ProcessCreate, result.EffectiveProfile);
        }

        [TestMethod]
        public void Diagnostic_WriteIntent_IsMechanicallyDowngradedToReview()
        {
            AiTaskCapabilityDecision result = AiTaskCapabilityRouter.Route(
                "新建一个取料流程",
                null,
                null,
                AutomationToolProfiles.Diagnostic,
                false);

            Assert.AreEqual(AutomationToolProfiles.ProcessCreate, result.RequestedProfile);
            Assert.AreEqual(AutomationToolProfiles.ProcessReview, result.EffectiveProfile);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Notice));
        }

        [TestMethod]
        public void PlatformConfiguration_RequiresFullPermission()
        {
            AiTaskCapabilityDecision blocked = AiTaskCapabilityRouter.Route(
                "修改 PLC 配置",
                null,
                null,
                AutomationToolProfiles.Editor,
                false);
            AiTaskCapabilityDecision allowed = AiTaskCapabilityRouter.Route(
                "修改 PLC 配置",
                null,
                null,
                AutomationToolProfiles.Editor,
                true);

            Assert.AreEqual(AutomationToolProfiles.ProcessReview, blocked.EffectiveProfile);
            Assert.AreEqual(AutomationToolProfiles.PlatformConfiguration, allowed.EffectiveProfile);
        }

        [TestMethod]
        public void TrajectoryBudget_ReportsReviewWithoutBlockingExecution()
        {
            AiTrajectoryEvaluation normal = AiTrajectoryBudgetPolicy.Evaluate(
                AutomationToolProfiles.ProcessCreate, 8, 0, 32 * 1024);
            AiTrajectoryEvaluation noisy = AiTrajectoryBudgetPolicy.Evaluate(
                AutomationToolProfiles.ProcessCreate, 24, 3, 512 * 1024);

            Assert.AreEqual("pass", normal.Status);
            Assert.AreEqual("review", noisy.Status);
            CollectionAssert.Contains(new List<string>(noisy.Reasons), "tool_calls>18");
        }
    }
}
