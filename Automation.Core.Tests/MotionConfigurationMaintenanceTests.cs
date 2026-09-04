using System;
// 模块：核心测试 / 运动配置维护。
// 职责范围：固化运动配置提交与流程、手动运动资源之间的静止互斥契约。

using System.Collections.Generic;
using System.Threading;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class MotionConfigurationMaintenanceTests
    {
        [TestMethod]
        public void IdleValidation_RequiresMaintenanceLeaseAndBlocksNewManualMotion()
        {
            var runtime = new PlatformRuntime();
            using (var engine = CreateEngine(runtime, new List<Proc>()))
            {
                Assert.IsFalse(engine.TryValidateMotionConfigurationIdle(out string outsideError));
                StringAssert.Contains(outsideError, "尚未进入配置维护");

                Assert.IsTrue(runtime.Maintenance.TryBegin(
                    "测试运动配置提交",
                    out IDisposable lease,
                    out string beginError), beginError);
                using (lease)
                {
                    Assert.IsTrue(
                        engine.TryValidateMotionConfigurationIdle(out string idleError),
                        idleError);
                    Assert.IsFalse(
                        engine.TryAcquireManualMotionResource(0, 0, out string manualError));
                    StringAssert.Contains(manualError, "配置维护");
                    Assert.IsFalse(engine.TryReserveManualMotionResources(
                        new[] { new AxisCommandRequest(0, 0, AxisCommandKind.Motion) },
                        out IDisposable manualLease,
                        out string reserveError));
                    Assert.IsNull(manualLease);
                    StringAssert.Contains(reserveError, "配置维护");
                }
            }
        }

        [TestMethod]
        public void IdleValidation_RejectsExistingManualMotionResourceWithoutChangingIt()
        {
            var runtime = new PlatformRuntime();
            using (var engine = CreateEngine(runtime, new List<Proc>()))
            {
                Assert.IsTrue(
                    engine.TryAcquireManualMotionResource(0, 1, out string acquireError),
                    acquireError);
                Assert.IsTrue(runtime.Maintenance.TryBegin(
                    "测试运动配置提交",
                    out IDisposable lease,
                    out string beginError), beginError);
                using (lease)
                {
                    Assert.IsFalse(engine.TryValidateMotionConfigurationIdle(out string idleError));
                    StringAssert.Contains(idleError, "手动操作占用");

                    engine.ReleaseManualMotionResource(0, 1);
                    Assert.IsTrue(
                        engine.TryValidateMotionConfigurationIdle(out idleError),
                        idleError);
                }
            }
        }

        [TestMethod]
        public void IdleValidation_RejectsRunningProcessUntilItStops()
        {
            var runtime = new PlatformRuntime();
            Proc process = TestProcessFactory.CreateEndingProcess("运动配置静止检查", 5000);
            using (var engine = CreateEngine(runtime, new List<Proc> { process }))
            {
                Assert.IsTrue(engine.StartProc(process, 0));
                WaitForState(engine, state => !state.IsInactive(), TimeSpan.FromSeconds(3));

                Assert.IsTrue(runtime.Maintenance.TryBegin(
                    "测试运动配置提交",
                    out IDisposable lease,
                    out string beginError), beginError);
                using (lease)
                {
                    Assert.IsFalse(engine.TryValidateMotionConfigurationIdle(out string idleError));
                    StringAssert.Contains(idleError, "流程0尚未结束");

                    engine.Stop(0);
                    WaitForState(engine, state => state.IsInactive(), TimeSpan.FromSeconds(3));
                    Assert.IsTrue(
                        engine.TryValidateMotionConfigurationIdle(out idleError),
                        idleError);
                }
            }
        }

        private static ProcessEngine CreateEngine(PlatformRuntime runtime, IList<Proc> processes)
        {
            return new ProcessEngine(new EngineContext
            {
                Procs = processes,
                Maintenance = runtime.Maintenance,
                Safety = runtime.Safety,
                Readiness = runtime.Readiness,
                Paths = runtime.Paths
            });
        }

        private static void WaitForState(
            ProcessEngine engine,
            Func<ProcRunState, bool> predicate,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ProcRunState state = engine.GetSnapshot(0)?.State ?? ProcRunState.Ready;
                if (predicate(state))
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"等待流程状态超时，当前状态:{engine.GetSnapshot(0)?.State}");
        }
    }
}
