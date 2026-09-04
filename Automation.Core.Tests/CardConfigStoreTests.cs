using System;
// 模块：核心测试 / 控制卡配置。
// 职责范围：验证单张雷赛总线卡配置契约，不访问运动控制 SDK。

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class CardConfigStoreTests
    {
        [TestMethod]
        public void NewCardHead_DefaultsToLeiSaiBusCard()
        {
            var cardHead = new CardHead();

            Assert.AreEqual(CardHead.LeiSaiBusCardType, cardHead.CardType);
        }

        [TestMethod]
        public void AddControlCard_RejectsSecondCardWithoutChangingConfiguration()
        {
            var store = new CardConfigStore();

            Assert.AreEqual(0, store.AddControlCard(CreateCard()));

            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(
                () => store.AddControlCard(CreateCard()));
            StringAssert.Contains(error.Message, "只允许配置一张雷赛总线卡");
            Assert.AreEqual(1, store.GetControlCardCount());
        }

        [TestMethod]
        public void Validation_RejectsNonLeiSaiBusCard()
        {
            var store = new CardConfigStore();
            Card card = new Card();
            ControlCard controlCard = CreateCard();
            controlCard.cardHead.CardType = "其他控制卡";
            card.controlCards.Add(controlCard);
            store.SetCard(card);

            Assert.IsFalse(store.TryValidateAllAxes(out List<string> errors));
            StringAssert.Contains(string.Join("；", errors), "类型必须为雷赛总线卡");
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Load_NormalizesBlankLegacyCardTypeAndPersistsIt(string legacyCardType)
        {
            using (var directory = new TemporaryDirectory())
            {
                Card card = CreateCardContainer(CreateCard());
                card.controlCards[0].cardHead.CardType = legacyCardType;
                Assert.IsTrue(AtomicJsonFileStore.Save(directory.FullPath, "card", card));
                var store = new CardConfigStore();

                Assert.IsTrue(store.Load(directory.FullPath, out string error), error);

                Assert.AreEqual(
                    CardHead.LeiSaiBusCardType,
                    store.CardData.controlCards[0].cardHead.CardType);
                Card persisted = AtomicJsonFileStore.Read<Card>(directory.FullPath, "card");
                Assert.AreEqual(
                    CardHead.LeiSaiBusCardType,
                    persisted.controlCards[0].cardHead.CardType);
            }
        }

        [TestMethod]
        public void Load_RejectsNonBlankUnsupportedCardTypeWithoutRewritingIt()
        {
            using (var directory = new TemporaryDirectory())
            {
                Card card = CreateCardContainer(CreateCard());
                card.controlCards[0].cardHead.CardType = "旧脉冲卡";
                Assert.IsTrue(AtomicJsonFileStore.Save(directory.FullPath, "card", card));
                var store = new CardConfigStore();

                Assert.IsFalse(store.Load(directory.FullPath, out string error));

                StringAssert.Contains(error, "类型必须为雷赛总线卡");
                Card persisted = AtomicJsonFileStore.Read<Card>(directory.FullPath, "card");
                Assert.AreEqual("旧脉冲卡", persisted.controlCards[0].cardHead.CardType);
            }
        }

        [TestMethod]
        public void Load_RejectsExistingMultipleCardsWithoutSilentlyRemovingThem()
        {
            using (var directory = new TemporaryDirectory())
            {
                var card = new Card();
                card.controlCards.Add(CreateCard());
                card.controlCards.Add(CreateCard());
                Assert.IsTrue(AtomicJsonFileStore.Save(directory.FullPath, "card", card));
                var store = new CardConfigStore();

                Assert.IsFalse(store.Load(directory.FullPath, out string error));

                StringAssert.Contains(error, "只允许配置一张雷赛总线卡");
                Assert.AreEqual(2, store.GetControlCardCount(), "失败加载不得裁剪已有控制卡配置。");
                Card persisted = AtomicJsonFileStore.Read<Card>(directory.FullPath, "card");
                Assert.AreEqual(2, persisted.controlCards.Count, "失败加载不得改写磁盘配置。");
            }
        }

        [TestMethod]
        public void Validation_AcceptsCurrentLeiSaiBusAxisContract()
        {
            var store = new CardConfigStore();
            ControlCard controlCard = CreateCard();
            controlCard.axis.Add(CreateAxis());
            controlCard.cardHead.AxisCount = 1;
            var card = new Card();
            card.controlCards.Add(controlCard);
            store.SetCard(card);

            Assert.IsTrue(store.TryValidateAllAxes(out List<string> errors), string.Join("；", errors));
        }

        [TestMethod]
        public void IoMapValidation_RequiresCardGroupsAndDeclaredDirectionCountsToMatch()
        {
            var store = new CardConfigStore();
            ControlCard controlCard = CreateCard();
            controlCard.cardHead.InputCount = 1;
            controlCard.cardHead.OutputCount = 1;
            store.SetCard(CreateCardContainer(controlCard));
            var validMap = new List<List<IO>>
            {
                new List<IO>
                {
                    CreateIo(0, "0", "通用输入"),
                    CreateIo(0, "0", "通用输出")
                }
            };

            Assert.IsTrue(store.TryValidateIoMap(validMap, out string validError), validError);

            validMap[0].RemoveAt(1);
            Assert.IsFalse(store.TryValidateIoMap(validMap, out string countError));
            StringAssert.Contains(countError, "输出0/1");

            validMap.Add(new List<IO>());
            Assert.IsFalse(store.TryValidateIoMap(validMap, out string groupError));
            StringAssert.Contains(groupError, "控制卡数量与IO卡分组数量不一致");
        }

        [TestMethod]
        public void IoMapValidation_RejectsDirectionIndexOutsideDeclaredRange()
        {
            var store = new CardConfigStore();
            ControlCard controlCard = CreateCard();
            controlCard.cardHead.InputCount = 1;
            store.SetCard(CreateCardContainer(controlCard));
            var map = new List<List<IO>>
            {
                new List<IO> { CreateIo(0, "5", "通用输入") }
            };

            Assert.IsFalse(store.TryValidateIoMap(map, out string error));
            StringAssert.Contains(error, "编号5超出声明范围0到0");
        }

        [TestMethod]
        public void StationValidation_AcceptsCoordinateSystemSevenAndRejectsEight()
        {
            var store = new CardConfigStore();
            ControlCard controlCard = CreateCard();
            controlCard.axis.Add(CreateAxis());
            controlCard.cardHead.AxisCount = 1;
            store.SetCard(CreateCardContainer(controlCard));
            DataStation station = CreateAxisStation(7);

            Assert.IsTrue(
                store.TryValidateStations(new[] { station }, out List<string> validErrors),
                string.Join("；", validErrors));

            station.CoordinateSystem = 8;
            Assert.IsFalse(store.TryValidateStations(new[] { station }, out List<string> invalidErrors));
            StringAssert.Contains(string.Join("；", invalidErrors), "0到7");
        }

        [TestMethod]
        public void StationValidation_RejectsCandidateCardThatRemovesReferencedAxis()
        {
            var candidateStore = new CardConfigStore();
            ControlCard candidateCard = CreateCard();
            candidateCard.axis.Add(CreateAxis());
            candidateCard.cardHead.AxisCount = 1;
            candidateStore.SetCard(CreateCardContainer(candidateCard));
            DataStation existingStation = CreateAxisStation(0);
            existingStation.dataAxis.axisConfig1.AxisName = "Y";

            Assert.IsFalse(candidateStore.TryValidateStations(
                new[] { existingStation }, out List<string> errors));

            StringAssert.Contains(string.Join("；", errors), "轴配置不存在:0-Y");
        }

        [TestMethod]
        public void Validation_RejectsIncompleteOrReversedSoftLimits()
        {
            var store = new CardConfigStore();
            ControlCard controlCard = CreateCard();
            Axis axis = CreateAxis();
            axis.NegativeSoftLimit = -100;
            axis.PositiveSoftLimit = 0;
            controlCard.axis.Add(axis);
            controlCard.cardHead.AxisCount = 1;
            var card = new Card();
            card.controlCards.Add(controlCard);
            store.SetCard(card);

            Assert.IsFalse(store.TryValidateAllAxes(out List<string> incompleteErrors));
            StringAssert.Contains(string.Join("；", incompleteErrors), "必须同时配置");

            axis.PositiveSoftLimit = -200;
            Assert.IsFalse(store.TryValidateAllAxes(out List<string> reversedErrors));
            StringAssert.Contains(string.Join("；", reversedErrors), "负软限位必须小于正软限位");
        }

        [TestMethod]
        public void Validation_RejectsUnknownEncoderTypeAndHomeMethod()
        {
            var store = new CardConfigStore();
            ControlCard controlCard = CreateCard();
            Axis axis = CreateAxis();
            axis.EncoderType = (AxisEncoderType)99;
            controlCard.axis.Add(axis);
            controlCard.cardHead.AxisCount = 1;
            var card = new Card();
            card.controlCards.Add(controlCard);
            store.SetCard(card);

            Assert.IsFalse(store.TryValidateAllAxes(out List<string> encoderErrors));
            StringAssert.Contains(string.Join("；", encoderErrors), "编码器类型无效");

            axis.EncoderType = AxisEncoderType.Incremental;
            axis.HomeMethod = -2;
            Assert.IsFalse(store.TryValidateAllAxes(out List<string> homeErrors));
            StringAssert.Contains(string.Join("；", homeErrors), "总线回原方法");
        }

        [TestMethod]
        public void Validation_RejectsDuplicateAxisNameInEditedCardCandidate()
        {
            var store = new CardConfigStore();
            ControlCard controlCard = CreateCard();
            Axis first = CreateAxis();
            Axis second = CreateAxis();
            second.AxisNum = 1;
            controlCard.axis.Add(first);
            controlCard.axis.Add(second);
            controlCard.cardHead.AxisCount = 2;
            store.SetCard(CreateCardContainer(controlCard));

            Assert.IsFalse(store.TryValidateAllAxes(out List<string> errors));

            StringAssert.Contains(string.Join("；", errors), "轴名称重复:X");
        }

        private static ControlCard CreateCard()
        {
            return new ControlCard
            {
                cardHead = new CardHead
                {
                    AxisCount = 0,
                    InputCount = 0,
                    OutputCount = 0
                }
            };
        }

        private static Card CreateCardContainer(ControlCard controlCard)
        {
            var card = new Card();
            card.controlCards.Add(controlCard);
            return card;
        }

        private static DataStation CreateAxisStation(ushort coordinateSystem)
        {
            var station = new DataStation(false)
            {
                Name = "轴工站",
                Type = StationType.Axis,
                CoordinateSystem = coordinateSystem
            };
            station.dataAxis.axisConfig1.CardNum = "0";
            station.dataAxis.axisConfig1.AxisName = "X";
            return station;
        }

        private static IO CreateIo(int cardIndex, string ioIndex, string ioType)
        {
            return new IO
            {
                CardNum = cardIndex,
                Module = 0,
                IOIndex = ioIndex,
                IOType = ioType,
                UsedType = "通用",
                EffectLevel = "正常"
            };
        }

        private static Axis CreateAxis()
        {
            return new Axis
            {
                AxisName = "X",
                AxisNum = 0,
                PulseToMM = 1000,
                HomeMethod = -1,
                HomeSpeed = "10",
                SpeedMax = 20,
                AccMax = 40,
                DecMax = 40
            };
        }
    }
}
