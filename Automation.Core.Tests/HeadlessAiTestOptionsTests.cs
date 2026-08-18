using Microsoft.VisualStudio.TestTools.UnitTesting;
// 模块：核心测试 / AI headless 回归入口。
// 职责范围：验证 Bridge ai-test 触发的语句机械约束。

using System.Collections.Generic;
using System.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    public class HeadlessAiTestOptionsTests
    {
        [TestMethod]
        public void ValidatePrompts_AcceptsNormalPrompts()
        {
            Assert.IsNull(HeadlessAiTestOptions.ValidatePrompts(
                new List<string> { "第一句", "第二句" }));
            Assert.IsNull(HeadlessAiTestOptions.ValidatePrompts(
                Enumerable.Range(0, 12).Select(i => "句" + i).ToList()));
        }

        [TestMethod]
        public void ValidatePrompts_RejectsEmptyOrTooMany()
        {
            StringAssert.Contains(
                HeadlessAiTestOptions.ValidatePrompts(null), "1..12");
            StringAssert.Contains(
                HeadlessAiTestOptions.ValidatePrompts(new List<string>()), "1..12");
            StringAssert.Contains(
                HeadlessAiTestOptions.ValidatePrompts(
                    Enumerable.Range(0, 13).Select(i => "句" + i).ToList()), "1..12");
        }

        [TestMethod]
        public void ValidatePrompts_RejectsBlankOrOverlongPrompt()
        {
            StringAssert.Contains(
                HeadlessAiTestOptions.ValidatePrompts(new List<string> { " " }), "4000");
            StringAssert.Contains(
                HeadlessAiTestOptions.ValidatePrompts(
                    new List<string> { new string('长', 4001) }), "4000");
        }
    }
}
