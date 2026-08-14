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
            StringAssert.Contains(automation, "不为了利用现有资源推导额外副作用");
            StringAssert.Contains(automation, "具体创建、评审和修改步骤由对应 Skill 提供");
            StringAssert.Contains(automation, "safeToRetry=true");
            StringAssert.Contains(automation, "不是已实现结构");

            string review = ReadEmbedded(
                "Automation.Assets.Goose.Skills.automation-process-review.SKILL.md");
            AssertSkillFrontmatter(review, "automation-process-review");
            StringAssert.Contains(review, "name: automation-process-review");
            StringAssert.Contains(review, "audit_proc_batch");
            StringAssert.Contains(review, "inspect_process");
            StringAssert.Contains(review, "优先复用已返回事实");
            StringAssert.Contains(review, "新缺口会影响结论，则精确回读");
            StringAssert.Contains(review, "submit_review_handoff");
            StringAssert.Contains(review, "不要求为了包装普通评审再调用工具");
            StringAssert.Contains(review, "同一业务事件会重复不可逆副作用");
            StringAssert.Contains(review, "必须由可执行路径、状态迁移、调用关系或运行证据证明");
            StringAssert.Contains(review, "includeOperationDetails=true");
            StringAssert.Contains(review, "不等于这些结构已经实现");

            string authoring = ReadEmbedded(
                "Automation.Assets.Goose.Skills.automation-process-authoring.SKILL.md");
            AssertSkillFrontmatter(authoring, "automation-process-authoring");
            StringAssert.Contains(authoring, "name: automation-process-authoring");
            StringAssert.Contains(authoring, "只读流程评审使用 automation-process-review");
            StringAssert.Contains(authoring, "config.placeholder");
            StringAssert.Contains(authoring, "resolve_authoring_inputs");
            StringAssert.Contains(authoring, "resolve_operation_capability");
            StringAssert.Contains(authoring, "新建流程直接使用 `preview_change_set`");
            StringAssert.Contains(authoring, "不得用延时、弹框、常量或伪状态");
            StringAssert.Contains(authoring, "operation.replace");
            StringAssert.Contains(authoring, "保持占位时可用 `operation.update`");
            StringAssert.Contains(authoring, "变量复位/累加、分支和跳转");
            StringAssert.Contains(authoring, "不自动补报警、停机、复位、重试或提示");
            StringAssert.Contains(authoring, "可独立审查和保存的安全功能块");
            StringAssert.Contains(authoring, "不要求一个用户目标一次写完");
            StringAssert.Contains(authoring, "`process.create`、`step.append` 和 `operation.append`");
            StringAssert.Contains(authoring, "跨步骤全局唯一时可直接使用");
            StringAssert.Contains(authoring, "自然语言声明不是实现证据");
            Assert.IsFalse(authoring.Contains("preview_process_blueprint", StringComparison.Ordinal));
            Assert.IsFalse(authoring.Contains("retries[]", StringComparison.Ordinal));
            Assert.IsFalse(authoring.Contains("blueprintEvidence", StringComparison.Ordinal));
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

        private static void AssertSkillFrontmatter(string skill, string expectedName)
        {
            string normalized = (skill ?? string.Empty).Replace("\r\n", "\n");
            Assert.IsTrue(normalized.StartsWith("---\n"), expectedName);
            int closing = normalized.IndexOf("\n---\n", 4, System.StringComparison.Ordinal);
            Assert.IsTrue(closing > 4, expectedName);
            string[] lines = normalized.Substring(4, closing - 4)
                .Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(2, lines.Length, expectedName);
            Assert.AreEqual("name: " + expectedName, lines[0]);
            StringAssert.StartsWith(lines[1], "description:");
        }
    }
}
