using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class IoConfigurationStoreMotionSafetyTests
    {
        [TestMethod]
        public void Validation_AcceptsEmergencyInputAndBrakeOutput()
        {
            var store = new IoConfigurationStore();
            var map = new List<List<IO>>
            {
                new List<IO>
                {
                    CreateIo("急停按钮", "0", "通用输入", "急停"),
                    CreateIo("伺服刹车", "1", "通用输出", "刹车")
                }
            };

            Assert.IsTrue(store.TryReplaceMap(map, out string error), error);
        }

        [TestMethod]
        public void Validation_AcceptsAllThreePointZeroSystemInputPurposes()
        {
            var store = new IoConfigurationStore();
            var map = new List<List<IO>>
            {
                new List<IO>
                {
                    CreateIo("急停按钮", "0", "通用输入", "急停"),
                    CreateIo("复位按钮", "1", "通用输入", "复位"),
                    CreateIo("启动按钮", "2", "通用输入", "启动"),
                    CreateIo("暂停按钮", "3", "通用输入", "暂停"),
                    CreateIo("停止按钮", "4", "通用输入", "停止")
                }
            };

            Assert.IsTrue(store.TryReplaceMap(map, out string error), error);
        }

        [TestMethod]
        [DataRow("通用输出", "急停", "急停IO必须配置为通用输入")]
        [DataRow("通用输出", "复位", "复位IO必须配置为通用输入")]
        [DataRow("通用输出", "启动", "启动IO必须配置为通用输入")]
        [DataRow("通用输出", "暂停", "暂停IO必须配置为通用输入")]
        [DataRow("通用输出", "停止", "停止IO必须配置为通用输入")]
        [DataRow("通用输入", "刹车", "刹车IO必须配置为通用输出")]
        public void Validation_RejectsSystemIoAssignedToWrongDirection(
            string ioType,
            string usedType,
            string expectedError)
        {
            var store = new IoConfigurationStore();
            var map = new List<List<IO>>
            {
                new List<IO> { CreateIo("系统IO", "0", ioType, usedType) }
            };

            Assert.IsFalse(store.TryReplaceMap(map, out string error));
            StringAssert.Contains(error, expectedError);
        }

        [TestMethod]
        public void Validation_RejectsNonZeroModuleForFlatLeiSaiIo()
        {
            var store = new IoConfigurationStore();
            IO io = CreateIo("输出", "0", "通用输出", "通用");
            io.Module = 1;

            Assert.IsFalse(store.TryReplaceMap(
                new[] { new List<IO> { io } }, out string error));
            StringAssert.Contains(error, "模块号必须为0");
        }

        [TestMethod]
        public void Validation_RejectsDuplicateIoIndexWithinSameDirection()
        {
            var store = new IoConfigurationStore();
            var map = new[]
            {
                new List<IO>
                {
                    CreateIo("输入一", "0", "通用输入", "通用"),
                    CreateIo("输入二", "0", "通用输入", "通用")
                }
            };

            Assert.IsFalse(store.TryReplaceMap(map, out string error));
            StringAssert.Contains(error, "通用输入编号重复:0");
        }

        [TestMethod]
        public void ResizeCardIo_ExpandsWithDefaultsAndPreservesOverlappingConfiguration()
        {
            var store = new IoConfigurationStore();
            IO input = CreateIo("启动按钮", "0", "通用输入", "启动");
            input.Note = "保留输入配置";
            IO output = CreateIo("伺服刹车", "0", "通用输出", "刹车");
            output.EffectLevel = "取反";
            Assert.IsTrue(store.TryReplaceMap(
                new[] { new List<IO> { input, output } }, out string replaceError), replaceError);

            Assert.IsTrue(
                store.TryCreateResizedCardMap(0, 2, 2, out List<List<IO>> candidate, out string error),
                error);

            Assert.AreEqual(2, store.Map[0].Count, "生成候选快照不得提前修改正式内存。");
            Assert.AreEqual(4, candidate[0].Count);
            Assert.AreEqual("启动按钮", candidate[0][0].Name);
            Assert.AreEqual("启动", candidate[0][0].UsedType);
            Assert.AreEqual("保留输入配置", candidate[0][0].Note);
            Assert.AreEqual("", candidate[0][1].Name);
            Assert.AreEqual("通用", candidate[0][1].UsedType);
            Assert.AreEqual("正常", candidate[0][1].EffectLevel);
            Assert.AreEqual("伺服刹车", candidate[0][2].Name);
            Assert.AreEqual("刹车", candidate[0][2].UsedType);
            Assert.AreEqual("取反", candidate[0][2].EffectLevel);
            Assert.AreEqual("", candidate[0][3].Name);
            Assert.AreEqual(0, candidate[0][0].Index);
            Assert.AreEqual(1, candidate[0][1].Index);
            Assert.AreEqual(2, candidate[0][2].Index);
            Assert.AreEqual(3, candidate[0][3].Index);
        }

        [TestMethod]
        public void ResizeCardIo_RejectsShrinkThatWouldDiscardConfiguredIo()
        {
            var store = new IoConfigurationStore();
            Assert.IsTrue(store.TryReplaceMap(
                new[]
                {
                    new List<IO>
                    {
                        CreateIo("", "0", "通用输入", "通用"),
                        CreateIo("急停按钮", "1", "通用输入", "急停")
                    }
                },
                out string replaceError),
                replaceError);

            Assert.IsFalse(
                store.TryCreateResizedCardMap(0, 1, 0, out _, out string error));

            StringAssert.Contains(error, "将丢弃已配置IO[急停按钮]");
            Assert.AreEqual(2, store.Map[0].Count);
        }

        [TestMethod]
        public void ResizeCardIo_AllowsShrinkOfUnusedDefaults()
        {
            var store = new IoConfigurationStore();
            Assert.IsTrue(store.TryReplaceMap(
                new[]
                {
                    new List<IO>
                    {
                        CreateIo("", "0", "通用输入", "通用"),
                        CreateIo("", "1", "通用输入", "通用"),
                        CreateIo("", "0", "通用输出", "通用"),
                        CreateIo("", "1", "通用输出", "通用")
                    }
                },
                out string replaceError),
                replaceError);

            Assert.IsTrue(
                store.TryCreateResizedCardMap(0, 1, 1, out List<List<IO>> candidate, out string error),
                error);

            Assert.AreEqual(2, candidate[0].Count);
            Assert.AreEqual("通用输入", candidate[0][0].IOType);
            Assert.AreEqual("通用输出", candidate[0][1].IOType);
            Assert.AreEqual(4, store.Map[0].Count);
        }

        private static IO CreateIo(string name, string index, string ioType, string usedType)
        {
            return new IO
            {
                Name = name,
                CardNum = 0,
                Module = 0,
                IOIndex = index,
                IOType = ioType,
                UsedType = usedType,
                EffectLevel = "正常"
            };
        }
    }
}
