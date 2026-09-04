using System;
// 模块：核心测试 / 工站配置存储。
// 职责范围：验证轴工站与机器人工站点位容量在提交和持久化边界严格生效。

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class StationDefinitionStoreTests
    {
        [TestMethod]
        public void TryCommit_RejectsRobotIndexTwoHundredAndAcceptsAxisIndexThreeHundredNinetyNine()
        {
            foreach (StationType type in new[]
            {
                StationType.Epson,
                StationType.Inovance,
                StationType.InovanceV4
            })
            {
                using (var directory = new TemporaryDirectory())
                {
                    var store = new StationDefinitionStore();
                    DataStation robot = CreateStation(type, "机器人工站");
                    NamePoint(robot, DataStation.RobotPointCapacity, "机器人越界点");

                    Assert.IsFalse(store.TryCommit(
                        directory.FullPath,
                        new[] { robot },
                        out string error));
                    StringAssert.Contains(error, "[0, 200)");
                    Assert.AreEqual(0, store.Items.Count, "失败提交不得修改正式内存。");
                }
            }

            using (var directory = new TemporaryDirectory())
            {
                var store = new StationDefinitionStore();
                DataStation axis = CreateStation(StationType.Axis, "轴工站");
                NamePoint(axis, DataStation.PointCapacity - 1, "轴工站末点");

                Assert.IsTrue(store.TryCommit(
                    directory.FullPath,
                    new[] { axis },
                    out string error), error);
                Assert.AreEqual("轴工站末点",
                    store.Items[0].ListDataPos[DataStation.PointCapacity - 1].Name);
            }
        }

        [TestMethod]
        public void TryPersistCurrent_RejectsRobotIndexTwoHundredWithoutOverwritingSavedConfiguration()
        {
            using (var directory = new TemporaryDirectory())
            {
                var store = new StationDefinitionStore();
                DataStation robot = CreateStation(StationType.Epson, "EPSON六轴工站");
                Assert.IsTrue(store.TryCommit(
                    directory.FullPath,
                    new[] { robot },
                    out string commitError), commitError);

                NamePoint(
                    store.Items[0],
                    DataStation.RobotPointCapacity,
                    "不允许持久化的点");
                Assert.IsFalse(store.TryPersistCurrent(directory.FullPath, out string persistError));
                StringAssert.Contains(persistError, "[0, 200)");

                var reloaded = new StationDefinitionStore();
                Assert.IsTrue(reloaded.Load(directory.FullPath, out string loadError), loadError);
                Assert.IsTrue(string.IsNullOrWhiteSpace(
                    reloaded.Items[0].ListDataPos[DataStation.RobotPointCapacity].Name),
                    "持久化失败不能覆盖上一次有效配置。");
            }
        }

        [TestMethod]
        public void Load_RejectsRobotIndexTwoHundredBeforeNormalizingLegacyDictionary()
        {
            using (var directory = new TemporaryDirectory())
            {
                DataStation robot = new DataStation(true)
                {
                    Name = "旧版EPSON六轴工站",
                    Type = StationType.Epson,
                    CommunicationName = "机器人通讯"
                };
                var invalidPoint = new DataPos(DataStation.RobotPointCapacity)
                {
                    Name = "旧版越界点",
                    IsTaught = true
                };
                robot.dicDataPos[invalidPoint.Name] = invalidPoint;
                Assert.IsTrue(AtomicJsonFileStore.Save(
                    directory.FullPath,
                    "DataStation",
                    new[] { robot }));

                var store = new StationDefinitionStore();
                Assert.IsFalse(store.Load(directory.FullPath, out string error));
                StringAssert.Contains(error, "工站配置加载失败");
                StringAssert.Contains(error, "[0, 200)");
                Assert.AreEqual(0, store.Items.Count, "非法旧配置不得进入正式内存。");
            }
        }

        private static DataStation CreateStation(StationType type, string name)
        {
            return new DataStation(false)
            {
                Name = name,
                Type = type,
                CommunicationName = type == StationType.Axis ? string.Empty : "机器人通讯"
            };
        }

        private static void NamePoint(DataStation station, int index, string name)
        {
            DataPos point = station.ListDataPos[index];
            point.Name = name;
            point.IsTaught = true;
            station.dicDataPos[name] = point;
        }
    }
}
