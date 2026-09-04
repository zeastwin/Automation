using System;
// 模块：核心测试 / 运动配置提交。
// 职责范围：验证运动配置写盘后的重启闸门与界面降级语义。

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class FrmCardMotionConfigurationCommitTests
    {
        [TestMethod]
        public void CompleteCommit_SetsRestartGateBeforeCommittedMemoryReplacement()
        {
            var readiness = new PlatformReadinessState();
            bool bestEffortRan = false;

            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                FrmCard.CompleteCommittedMotionConfiguration(
                    readiness,
                    () => throw new InvalidOperationException("正式内存替换失败"),
                    null,
                    () => bestEffortRan = true));

            StringAssert.Contains(error.Message, "正式内存替换失败");
            Assert.IsTrue(readiness.MotionConfigRestartRequired);
            Assert.IsFalse(bestEffortRan);
        }

        [TestMethod]
        public void CompleteCommit_SwallowsUiFailureAndContinuesRemainingRefreshes()
        {
            var readiness = new PlatformReadinessState();
            var logs = new List<string>();
            bool memoryApplied = false;
            bool laterRefreshRan = false;

            FrmCard.CompleteCommittedMotionConfiguration(
                readiness,
                () => memoryApplied = true,
                logs.Add,
                () => throw new InvalidOperationException("界面刷新失败"),
                () => laterRefreshRan = true);

            Assert.IsTrue(readiness.MotionConfigRestartRequired);
            Assert.IsTrue(memoryApplied);
            Assert.IsTrue(laterRefreshRan);
            Assert.AreEqual(1, logs.Count);
            StringAssert.Contains(logs[0], "配置已经提交");
            StringAssert.Contains(logs[0], "界面刷新失败");
        }
    }
}
