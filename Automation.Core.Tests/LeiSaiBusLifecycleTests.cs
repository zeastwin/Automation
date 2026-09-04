using System;
// 模块：核心测试 / 雷赛总线卡。
// 职责范围：验证无硬件参与的单卡生命周期和 IO 逻辑电平契约。

using System.Collections.Generic;
using System.Reflection;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class LeiSaiBusLifecycleTests
    {
        [TestMethod]
        public void InitCardType_NoConfiguredCard_DoesNotCreateLeiSaiDriver()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                MotionCtrl motion = CreateMotion(runtime);

                motion.InitCardType();

                Assert.IsNull(motion.ls, "纯机器人配置不应创建雷赛原生驱动。");
                Assert.IsFalse(motion.InitCard(), "无卡配置不应进入原生初始化。");
                motion.StopConnect();
            }
        }

        [TestMethod]
        public void InitCardType_RepeatedCall_ReusesDriverWithoutDuplicatingCleanPositionHandlers()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                runtime.Stores.Cards.AddControlCard(new ControlCard
                {
                    cardHead = new CardHead(),
                    axis = new List<Axis>()
                });
                MotionCtrl motion = CreateMotion(runtime);

                motion.InitCardType();
                LS first = motion.ls;
                motion.InitCardType();

                Assert.IsNotNull(first);
                Assert.AreSame(first, motion.ls, "重复初始化类型不得丢失已经持有板卡会话的驱动。");
                FieldInfo cleanPositionEvent = typeof(MotionCtrl).GetField(
                    "cleanPos",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var handlers = cleanPositionEvent?.GetValue(motion) as Delegate;
                Assert.IsNotNull(handlers);
                Assert.AreEqual(1, handlers.GetInvocationList().Length,
                    "清零必须由驱动内单个原子入口完成，不得拆成可重排的多播调用。");
                Assert.AreEqual("CleanPos", handlers.Method.Name);
            }
        }

        [TestMethod]
        public void IoEffectLevel_MapsLogicalAndHardwareStatesSymmetrically()
        {
            AssertLogicalMapping("正常", false, 0);
            AssertLogicalMapping("正常", true, 1);
            AssertLogicalMapping("取反", false, 1);
            AssertLogicalMapping("取反", true, 0);

            Assert.IsFalse(LS.TryMapLogicalIoState("未知", true, out _));
            Assert.IsFalse(LS.TryMapHardwareIoState("正常", -1, out _),
                "SDK 读取错误码不得冒充 IO 有效状态。");
            Assert.IsFalse(LS.TryMapHardwareIoState("未知", 1, out _));
        }

        [TestMethod]
        public void IoAddress_RejectsNonZeroModuleAndNonLogicalCardZero()
        {
            IO io = CreateIo();
            Assert.IsTrue(LS.TryGetIoIndex(io, "通用输出", out ushort index));
            Assert.AreEqual((ushort)3, index);

            io.Module = 1;
            Assert.IsFalse(LS.TryGetIoIndex(io, "通用输出", out _),
                "单卡扁平 IO 契约不得静默忽略非零模块号。");
            io.Module = 0;
            io.CardNum = 1;
            Assert.IsFalse(LS.TryGetIoIndex(io, "通用输出", out _));
        }

        [TestMethod]
        public void CoordinateSystemContract_UsesZeroThroughSevenAcrossModelAndDriver()
        {
            NumericRangeAttribute range = typeof(DataStation)
                .GetProperty(nameof(DataStation.CoordinateSystem))
                ?.GetCustomAttribute<NumericRangeAttribute>();
            Assert.IsNotNull(range);
            Assert.AreEqual(0d, range.Minimum);
            Assert.AreEqual(7d, range.Maximum);

            using (var directory = new TemporaryDirectory())
            {
                var driver = new LS(new CardConfigStore(), directory.FullPath);
                var request = new CoordinatedLinearMoveRequest
                {
                    Card = 0,
                    CoordinateSystem = 7,
                    Axes = new ushort[] { 0 },
                    Positions = new[] { 1d },
                    PositionMode = 1,
                    MaxVelocity = 10,
                    AccelerationTime = 1,
                    DecelerationTime = 1
                };

                InvalidOperationException notInitialized =
                    Assert.ThrowsExactly<InvalidOperationException>(
                        () => driver.MoveCoordinatedLinear(request));
                StringAssert.Contains(notInitialized.Message, "尚未初始化");

                request.CoordinateSystem = 8;
                Assert.ThrowsExactly<ArgumentException>(
                    () => driver.MoveCoordinatedLinear(request));
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                    () => driver.IsCoordinatedLinearDone(0, 8));
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                    () => driver.StopCoordinatedLinear(0, 8, 0));
            }
        }

        [TestMethod]
        public void DirectionalLimitCheck_BlocksOnlyMotionFurtherIntoActiveLimit()
        {
            const uint positiveHardLimit = 1u << 1;
            const uint negativeHardLimit = 1u << 2;
            const uint positiveSoftLimit = 1u << 6;
            const uint negativeSoftLimit = 1u << 7;

            Assert.IsTrue(LS.IsDirectionBlockedByLimit(positiveHardLimit, 1));
            Assert.IsTrue(LS.IsDirectionBlockedByLimit(positiveSoftLimit, 1));
            Assert.IsFalse(LS.IsDirectionBlockedByLimit(positiveHardLimit | positiveSoftLimit, -1),
                "正限位有效时必须允许负向退出。");

            Assert.IsTrue(LS.IsDirectionBlockedByLimit(negativeHardLimit, -1));
            Assert.IsTrue(LS.IsDirectionBlockedByLimit(negativeSoftLimit, -1));
            Assert.IsFalse(LS.IsDirectionBlockedByLimit(negativeHardLimit | negativeSoftLimit, 1),
                "负限位有效时必须允许正向退出。");

            Assert.IsFalse(LS.IsDirectionBlockedByLimit(
                positiveHardLimit | negativeHardLimit | positiveSoftLimit | negativeSoftLimit,
                0), "零位移不得被限位误判为继续压限。");
            Assert.IsFalse(LS.IsDirectionBlockedByLimit(0, 1));
            Assert.IsFalse(LS.IsDirectionBlockedByLimit(0, -1));
        }

        [TestMethod]
        public void PointMotionDirection_UsesDistanceForRelativeAndTargetDeltaForAbsolute()
        {
            Assert.AreEqual(1, LS.ResolvePointMotionDirection(5, 0, 100));
            Assert.AreEqual(-1, LS.ResolvePointMotionDirection(-5, 0, -100));
            Assert.AreEqual(0, LS.ResolvePointMotionDirection(0, 0, 100));

            Assert.AreEqual(1, LS.ResolvePointMotionDirection(105, 1, 100));
            Assert.AreEqual(-1, LS.ResolvePointMotionDirection(95, 1, 100));
            Assert.AreEqual(0, LS.ResolvePointMotionDirection(100, 1, 100));
        }

        private static MotionCtrl CreateMotion(PlatformRuntime runtime)
        {
            return new MotionCtrl(
                runtime.Stores.Values,
                runtime.Stores.Cards,
                runtime.Stores.Stations,
                runtime.Communication,
                runtime.Stores.Communication,
                runtime.Paths,
                runtime.Safety,
                runtime.Readiness,
                new NoopLogger());
        }

        private static IO CreateIo()
        {
            return new IO
            {
                CardNum = 0,
                Module = 0,
                IOIndex = "3",
                IOType = "通用输出",
                EffectLevel = "正常"
            };
        }

        private static void AssertLogicalMapping(string effectLevel, bool logicalValue, ushort expectedHardware)
        {
            Assert.IsTrue(LS.TryMapLogicalIoState(effectLevel, logicalValue, out ushort hardwareValue));
            Assert.AreEqual(expectedHardware, hardwareValue);
            Assert.IsTrue(LS.TryMapHardwareIoState(effectLevel, (short)hardwareValue, out bool mappedLogical));
            Assert.AreEqual(logicalValue, mappedLogical);
        }

        private sealed class NoopLogger : ILogger
        {
            public void Log(string message, LogLevel level)
            {
            }
        }
    }
}
