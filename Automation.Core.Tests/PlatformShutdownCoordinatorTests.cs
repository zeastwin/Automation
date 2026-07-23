using System;
// 模块：核心测试 / 平台关闭。
// 职责范围：固化编辑器与设备宿主共享的幂等关闭入口，防止资源释放链被重复执行。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class PlatformShutdownCoordinatorTests
    {
        [TestMethod]
        public void Shutdown_WhenCalledTwice_ReturnsTheCompletedReportWithoutRunningAgain()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);

                PlatformShutdownReport first = runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
                PlatformShutdownReport second = runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));

                Assert.IsTrue(runtime.ShutdownCoordinator.IsShutdownStarted);
                Assert.AreSame(first, second);
                Assert.IsTrue(first.Stages.Count > 0);
            }
        }

        [TestMethod]
        public void Shutdown_AfterVersionRestore_DoesNotOverwriteRestoredConfiguration()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                var variables = new Dictionary<string, DicValue>
                {
                    ["计数"] = new DicValue
                    {
                        Id = Guid.NewGuid(),
                        Index = 8,
                        Name = "计数",
                        Type = "double",
                        Value = "0",
                        Scope = VariableScopeContract.Public
                    }
                };
                Assert.IsTrue(
                    runtime.Stores.Values.TryCommitConfiguration(
                        runtime.Paths.ConfigPath,
                        variables,
                        out string error),
                    error);
                string valuePath = Path.Combine(
                    runtime.Paths.ConfigPath,
                    "value.json");
                string restoredFile = File.ReadAllText(valuePath);

                Assert.IsTrue(
                    runtime.Stores.Values.setValueByName("计数", "1"),
                    "测试准备失败：无法制造与磁盘快照不同的运行时值。");

                PlatformShutdownReport report =
                    runtime.ShutdownCoordinator.Shutdown(
                        TimeSpan.FromMilliseconds(200),
                        TimeSpan.FromSeconds(2),
                        saveRuntimeConfiguration: false);

                Assert.AreEqual(
                    restoredFile,
                    File.ReadAllText(valuePath),
                    "版本还原重启不得用旧内存状态覆盖磁盘快照。");
                Assert.IsTrue(
                    report.Stages.Any(stage =>
                        stage.Name == "保留已还原配置"
                        && string.IsNullOrEmpty(stage.Error)));
            }
        }
    }
}
