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
            StringAssert.Contains(automation, "list_authoring_resources");
            StringAssert.Contains(automation, "不预猜名称");
            StringAssert.Contains(automation, "禁用本身不自动等于 Bug");
            StringAssert.Contains(automation, "不为利用现有资源而追加用户目标之外的绑定");
            StringAssert.Contains(automation, "标准工艺时序中的复位、反馈验证和安全过渡");
            StringAssert.Contains(automation, "两种解读机械后果相反");
            StringAssert.Contains(automation, "具体创建、评审和修改步骤由对应 Skill 提供");
            StringAssert.Contains(automation, "safeToRetry=true");
            StringAssert.Contains(automation, "不是已实现结构");
            StringAssert.Contains(automation, "不是该功能无需实现的证据");
            StringAssert.Contains(automation, "单个输入未激活不证明相反机械终态");
            StringAssert.Contains(automation, "plan_motion_points");
            StringAssert.Contains(automation, "人工示教坐标前不能运行");

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
            StringAssert.Contains(authoring, "list_authoring_resources");
            StringAssert.Contains(authoring, "resolve_operation_capability");
            StringAssert.Contains(authoring, "基础 Schema 已足以表达");
            StringAssert.Contains(authoring, "只证明动作类型和契约已确定");
            StringAssert.Contains(authoring, "bindingRepair.candidates");
            StringAssert.Contains(authoring, "resourceRef");
            StringAssert.Contains(authoring, "新建流程直接使用 `ProcessCreate.preview_change_set`");
            StringAssert.Contains(authoring, "不得用延时、弹框、常量或伪状态");
            StringAssert.Contains(authoring, "operation.replace");
            StringAssert.Contains(authoring, "保持占位时可用 `operation.update`");
            StringAssert.Contains(authoring, "变量复位/累加、分支和跳转");
            StringAssert.Contains(authoring, "不自动补报警、停机、复位、重试或提示");
            StringAssert.Contains(authoring, "可独立审查和保存的安全功能块");
            StringAssert.Contains(authoring, "不要求一个用户目标一次写完");
            StringAssert.Contains(authoring, "authoringLease.leaseId");
            StringAssert.Contains(authoring, "ownerProcess.key");
            StringAssert.Contains(authoring, "string.clear");
            StringAssert.Contains(authoring, "number.zero");
            StringAssert.Contains(authoring, "inspect_process");
            StringAssert.Contains(authoring, "跨步骤全局唯一时可直接引用");
            StringAssert.Contains(authoring, "自然语言声明不是实现证据");
            StringAssert.Contains(authoring, "字段或行为错误才使用对应小契约");
            StringAssert.Contains(authoring, "authoringGaps");
            StringAssert.Contains(authoring, "它不证明该功能不需要");
            StringAssert.Contains(authoring, "单个输入的反向状态也不能代替相反机械终态反馈");
            StringAssert.Contains(authoring, "用 `plan_motion_points`");
            StringAssert.Contains(authoring, "planned 点位可以保存和继续搭建流程");
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
