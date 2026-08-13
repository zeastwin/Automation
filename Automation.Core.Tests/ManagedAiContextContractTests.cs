using System;
// 模块：核心测试 / EW-AI 受管上下文契约。
// 职责范围：验证 Prompt、路由和两类流程 Skill 的版本、入口及同版本内容同步。

using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class ManagedAiContextContractTests
    {
        [TestMethod]
        public void ManagedContext_ContainsExplorationCoverageAndSeparatedSkillRoutes()
        {
            Assert.AreEqual(
                GooseRuntimeProvisioner.ProcessAuthoringSkillVersion,
                int.Parse(ReadEmbedded(
                    "Automation.Assets.Goose.Skills.automation-process-authoring.skill-version").Trim()));
            Assert.AreEqual(
                GooseRuntimeProvisioner.ProcessReviewSkillVersion,
                int.Parse(ReadEmbedded(
                    "Automation.Assets.Goose.Skills.automation-process-review.skill-version").Trim()));

            string system = ReadEmbedded("Automation.Assets.Goose.system.md");
            StringAssert.Contains(system, "facts, inferences, and unresolved evidence gaps");
            StringAssert.Contains(system, "Stop gathering evidence");

            string automation = ReadEmbedded("Automation.Assets.Goose.automation.md");
            StringAssert.Contains(automation, "automation-process-review");
            StringAssert.Contains(automation, "automation-process-authoring");
            StringAssert.Contains(automation, "get_process_design_guide");
            StringAssert.Contains(automation, "功能块");
            StringAssert.Contains(automation, "nextFindingOffset");
            StringAssert.Contains(automation, "禁用本身不自动等于 Bug");

            string review = ReadEmbedded(
                "Automation.Assets.Goose.Skills.automation-process-review.SKILL.md");
            StringAssert.Contains(review, "name: automation-process-review");
            StringAssert.Contains(review, "audit_proc_batch");
            StringAssert.Contains(review, "不得隐藏、折叠掉或自动判定为 Bug");

            string authoring = ReadEmbedded(
                "Automation.Assets.Goose.Skills.automation-process-authoring.SKILL.md");
            StringAssert.Contains(authoring, "name: automation-process-authoring");
            StringAssert.Contains(authoring, "只读流程评审使用 automation-process-review");
            StringAssert.Contains(authoring, "采用的功能块");
            StringAssert.Contains(authoring, "不复制旧名称");
        }

        [TestMethod]
        public void ProcessSkills_SameVersionContentDriftIsResynchronized()
        {
            using (var directory = new TemporaryDirectory())
            {
                Assert.IsTrue(GooseRuntimeProvisioner.TryEnsureProcessSkills(
                    directory.FullPath,
                    out string firstMessage),
                    firstMessage);
                Assert.IsTrue(File.Exists(GooseRuntimeProvisioner.ProcessAuthoringSkillPath));
                Assert.IsTrue(File.Exists(GooseRuntimeProvisioner.ProcessReviewSkillPath));

                File.WriteAllText(
                    GooseRuntimeProvisioner.ProcessReviewSkillPath,
                    "同版本漂移内容");

                Assert.IsTrue(GooseRuntimeProvisioner.TryEnsureProcessSkills(
                    directory.FullPath,
                    out string secondMessage),
                    secondMessage);
                string restored = File.ReadAllText(
                    GooseRuntimeProvisioner.ProcessReviewSkillPath);
                StringAssert.Contains(restored, "name: automation-process-review");
                Assert.AreEqual(
                    GooseRuntimeProvisioner.ProcessReviewSkillVersion.ToString(),
                    File.ReadAllText(
                        GooseRuntimeProvisioner.GetProcessReviewSkillVersionPath()).Trim());
            }
        }

        private static string ReadEmbedded(string resourceName)
        {
            using (Stream stream = typeof(GooseRuntimeProvisioner).Assembly
                .GetManifestResourceStream(resourceName))
            {
                Assert.IsNotNull(stream, resourceName);
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
