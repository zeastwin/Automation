using System;
// 模块：核心测试 / 平台关闭。
// 职责范围：固化编辑器与设备宿主共享的幂等关闭入口，防止资源释放链被重复执行。

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
    }
}
