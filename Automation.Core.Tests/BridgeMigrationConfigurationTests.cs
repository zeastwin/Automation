using System.Collections.Generic;
// 模块：核心测试 / Bridge 配置迁移。
// 职责范围：固化运动与IO成组迁移后的正式 Store 重载及重启闸门契约。

using System.IO;
using Automation.Bridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class BridgeMigrationConfigurationTests
    {
        [TestMethod]
        public void CommitMotionIo_ReloadsBothStoresWithoutEditorWindowAndRequiresRestart()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Card cards = BuildCards();
                List<List<IO>> ioMap = BuildIoMap();

                bool restartRequired = AutomationBridgeService.CommitAndReloadMotionIoConfiguration(
                    runtime,
                    cards,
                    ioMap);

                Assert.IsTrue(restartRequired);
                Assert.IsTrue(runtime.Readiness.MotionConfigRestartRequired);
                Assert.AreEqual(1, runtime.Stores.Cards.GetControlCardCount());
                Assert.AreEqual(
                    CardHead.LeiSaiBusCardType,
                    runtime.Stores.Cards.CardData.controlCards[0].cardHead.CardType);
                Assert.AreEqual(1, runtime.Stores.IoConfiguration.Map.Count);
                Assert.AreEqual(2, runtime.Stores.IoConfiguration.Map[0].Count);
                Assert.AreEqual("急停输入", runtime.Stores.IoConfiguration.Map[0][0].Name);
                Assert.AreEqual("刹车输出", runtime.Stores.IoConfiguration.Map[0][1].Name);
                Assert.IsTrue(File.Exists(Path.Combine(directory.FullPath, "card.json")));
                Assert.IsTrue(File.Exists(Path.Combine(directory.FullPath, "IOMap.json")));
            }
        }

        [TestMethod]
        public void CommitMotionIo_WhenReloadFails_RestoresBothStoresAndLocksPlatform()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Card invalidCards = BuildCards();
                invalidCards.controlCards[0].cardHead.CardType = "不支持的控制卡";
                List<List<IO>> ioMap = BuildIoMap();

                System.Exception exception = null;
                try
                {
                    AutomationBridgeService.CommitAndReloadMotionIoConfiguration(
                        runtime,
                        invalidCards,
                        ioMap);
                }
                catch (System.Exception ex)
                {
                    exception = ex;
                }

                Assert.IsNotNull(exception, "重载失败必须返回显式失败，不能伪装为提交成功。");
                StringAssert.Contains(exception.Message, "已写入磁盘但重载失败");
                Assert.IsTrue(runtime.Readiness.MotionConfigRestartRequired);
                Assert.IsTrue(runtime.Safety.IsLocked);
                Assert.AreEqual(0, runtime.Stores.Cards.GetControlCardCount());
                Assert.AreEqual(0, runtime.Stores.IoConfiguration.Map.Count);
                Assert.IsTrue(File.Exists(Path.Combine(directory.FullPath, "card.json")));
                Assert.IsTrue(File.Exists(Path.Combine(directory.FullPath, "IOMap.json")));
            }
        }

        private static Card BuildCards()
        {
            return new Card
            {
                controlCards = new List<ControlCard>
                {
                    new ControlCard
                    {
                        cardHead = new CardHead
                        {
                            CardType = CardHead.LeiSaiBusCardType,
                            AxisCount = 0,
                            InputCount = 1,
                            OutputCount = 1
                        },
                        axis = new List<Axis>()
                    }
                }
            };
        }

        private static List<List<IO>> BuildIoMap()
        {
            return new List<List<IO>>
            {
                new List<IO>
                {
                    new IO
                    {
                        Index = 0,
                        Name = "急停输入",
                        CardNum = 0,
                        Module = 0,
                        IOIndex = "0",
                        IOType = "通用输入",
                        UsedType = "急停",
                        EffectLevel = "正常"
                    },
                    new IO
                    {
                        Index = 1,
                        Name = "刹车输出",
                        CardNum = 0,
                        Module = 0,
                        IOIndex = "0",
                        IOType = "通用输出",
                        UsedType = "刹车",
                        EffectLevel = "正常"
                    }
                }
            };
        }
    }
}
