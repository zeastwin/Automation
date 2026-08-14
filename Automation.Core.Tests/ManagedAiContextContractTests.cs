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
        public void ManagedContext_SeparatesDesignReviewAndAuthoringRoutes()
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
            StringAssert.Contains(system, "verified facts, inferences, and unresolved information");
            StringAssert.Contains(system, "For industrial runtime safety");
            Assert.IsFalse(system.Contains("proactively compare related objects"));

            string automation = ReadEmbedded("Automation.Assets.Goose.automation.md");
            StringAssert.Contains(automation, "automation-process-review");
            StringAssert.Contains(automation, "automation-process-authoring");
            StringAssert.Contains(automation, "get_process_design_guide");
            StringAssert.Contains(automation, "只要求“设计、方案、结构或怎么写”");
            StringAssert.Contains(automation, "不要加载写入 Skill");
            StringAssert.Contains(automation, "不先做全平台盘点");
            StringAssert.Contains(automation, "禁用本身不自动等于 Bug");
            StringAssert.Contains(automation, "不为了利用现有资源推导额外的同步、状态或副作用");
            StringAssert.Contains(automation, "预演一个可独立验证的功能块");
            StringAssert.Contains(automation, "复杂流程先提交安全骨架，再逐块补齐");
            StringAssert.Contains(automation, "一次修正全部已报告问题");
            StringAssert.Contains(automation, "没有新证据时不重复同类调用");

            string review = ReadEmbedded(
                "Automation.Assets.Goose.Skills.automation-process-review.SKILL.md");
            StringAssert.Contains(review, "name: automation-process-review");
            StringAssert.Contains(review, "audit_proc_batch");
            StringAssert.Contains(review, "不得隐藏、折叠掉或自动判定为 Bug");
            StringAssert.Contains(review, "reviewHandoff");
            StringAssert.Contains(review, "不能单独证明幂等缺陷");
            StringAssert.Contains(review, "不能单独证明存在或缺少恢复路径");

            string authoring = ReadEmbedded(
                "Automation.Assets.Goose.Skills.automation-process-authoring.SKILL.md");
            StringAssert.Contains(authoring, "name: automation-process-authoring");
            StringAssert.Contains(authoring, "只读流程评审使用 automation-process-review");
            StringAssert.Contains(authoring, "config.placeholder");
            StringAssert.Contains(authoring, "resolve_authoring_inputs");
            StringAssert.Contains(authoring, "resolve_operation_capability");
            StringAssert.Contains(authoring, "preview_process_blueprint");
            StringAssert.Contains(authoring, "不得用延时、弹框、常量或伪状态");
            StringAssert.Contains(authoring, "operation.replace");
            StringAssert.Contains(authoring, "复杂流程先用预期 Step");
            StringAssert.Contains(authoring, "不要求一个用户目标只预演一次");
            StringAssert.Contains(authoring, "内部计数器、复位与累加由编译器生成");
            StringAssert.Contains(authoring, "safeToRetry=true");
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
