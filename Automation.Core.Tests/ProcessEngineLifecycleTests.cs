using System;
// 模块：核心测试 / 流程引擎生命周期。
// 职责范围：验证无 UI 运行与重复启动保护；失败的重复请求不得替换当前实例。

using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class ProcessEngineLifecycleTests
    {
        [TestMethod]
        public void StartProc_WhenInstanceIsRunning_RejectsDuplicateAndKeepsCurrentInstance()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = TestProcessFactory.CreateEndingProcess("重复启动保护流程", 2000);
                var runtime = new PlatformRuntime(directory.FullPath);
                var context = new EngineContext
                {
                    Procs = new List<Proc> { process },
                    Maintenance = runtime.Maintenance,
                    Safety = runtime.Safety,
                    Readiness = runtime.Readiness,
                    Paths = runtime.Paths
                };
                using (var engine = new ProcessEngine(context))
                {
                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForState(engine, ProcRunState.Running, TimeSpan.FromSeconds(3));
                    EngineSnapshot running = engine.GetSnapshot(0);

                    Assert.IsFalse(engine.StartProc(process, 0));

                    EngineSnapshot afterDuplicateStart = engine.GetSnapshot(0);
                    Assert.AreEqual(ProcRunState.Running, afterDuplicateStart.State);
                    Assert.AreEqual(running.ProcId, afterDuplicateStart.ProcId);
                    Assert.AreEqual(running.AppliedRevision, afterDuplicateStart.AppliedRevision);

                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        private static void WaitForState(
            ProcessEngine engine,
            ProcRunState expectedState,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.GetSnapshot(0).State == expectedState)
                {
                    return;
                }
                Thread.Sleep(20);
            }
            Assert.Fail($"等待流程状态超时：{expectedState}；当前状态：{engine.GetSnapshot(0).State}。");
        }
    }
}
