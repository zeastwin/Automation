using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    public class AiStandardTestSuiteTests
    {
        [TestMethod]
        public void Select_CustomPromptsPreserveScenarioIdentityAndDoNotChangeDefaults()
        {
            AiStandardTestScenario original = AiStandardTestSuite.Scenarios
                .Single(item => item.Id == "build");
            string defaultPrompt = original.Prompts.Single();

            List<AiStandardTestScenario> selected = AiStandardTestSuite.Select(
                new[]
                {
                    new AiStandardTestPromptSet
                    {
                        Id = "build",
                        Prompts = new List<string> { "  第一轮自定义语句  ", "第二轮自定义语句" }
                    }
                },
                out string error);

            Assert.IsNull(error);
            Assert.AreEqual(1, selected.Count);
            Assert.AreEqual(AiStandardTestSetupKind.EmptyOwnedObjects, selected[0].SetupKind);
            CollectionAssert.AreEqual(
                new[] { "第一轮自定义语句", "第二轮自定义语句" },
                selected[0].Prompts.ToArray());
            Assert.AreEqual(defaultPrompt, original.Prompts.Single());
        }

        [TestMethod]
        public void Select_RejectsInvalidManualPromptsBeforeRunning()
        {
            AssertRejected(new AiStandardTestPromptSet
            {
                Id = "build",
                Prompts = new List<string> { " " }
            }, "第1轮");
            AssertRejected(new AiStandardTestPromptSet
            {
                Id = "build",
                Prompts = new List<string>
                {
                    new string('x', AiStandardTestSuite.MaximumPromptLength + 1)
                }
            }, "第1轮");
            AssertRejected(new AiStandardTestPromptSet
            {
                Id = "build",
                Prompts = Enumerable.Repeat("测试", AiStandardTestSuite.MaximumPromptCount + 1).ToList()
            }, "轮数");
            AssertRejected(new AiStandardTestPromptSet
            {
                Id = "unknown",
                Prompts = new List<string> { "测试" }
            }, "未知标准测试场景");
        }

        [TestMethod]
        public void SavePromptOverrides_RequiresCompleteScenarioSetBeforeWriting()
        {
            bool saved = AiStandardTestSuite.TrySavePromptOverrides(
                new[]
                {
                    new AiStandardTestPromptSet
                    {
                        Id = "build",
                        Prompts = new List<string> { "只提交一个场景" }
                    }
                },
                out string error);

            Assert.IsFalse(saved);
            StringAssert.Contains(error, "全部标准测试场景");
        }

        private static void AssertRejected(AiStandardTestPromptSet promptSet, string message)
        {
            List<AiStandardTestScenario> selected = AiStandardTestSuite.Select(
                new[] { promptSet }, out string error);

            Assert.AreEqual(0, selected.Count);
            StringAssert.Contains(error, message);
        }
    }
}
