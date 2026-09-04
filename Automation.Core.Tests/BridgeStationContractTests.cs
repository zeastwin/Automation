using System;
// 模块：核心测试 / Bridge 工站契约。
// 职责范围：验证退役工站写入口、机器人投影和不同工站类型的点位容量。

using Automation.Bridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class BridgeStationContractTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void LegacyStationAndPointWriteRoutes_AreRetired()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    var service = new AutomationBridgeService(form);
                    foreach (string route in new[]
                    {
                        "/bridge/station/add",
                        "/bridge/station/update",
                        "/bridge/station/delete",
                        "/bridge/point/set",
                        "/bridge/point/delete"
                    })
                    {
                        AutomationBridgeResponse response = service.Handle("POST", route, "{}");
                        Assert.AreEqual(404, response.StatusCode, route);
                        Assert.AreEqual(
                            "NOT_FOUND",
                            JObject.Parse(response.Body)["errorCode"]?.Value<string>(),
                            route);
                    }
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ListAndGetStation_ExposeRobotConfigurationAndPointCapacity()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    DataStation robot = CreateStation("EPSON六轴工站", StationType.Epson);
                    robot.CommunicationName = "EPSON命令";
                    robot.PointFromRobot = false;
                    robot.RemoteMode = true;
                    robot.RemoteCommunicationName = "EPSON远程";
                    Assert.IsTrue(
                        form.Runtime.Stores.Stations.TryCommit(
                            form.Runtime.Paths.ConfigPath,
                            new[] { robot },
                            out string error),
                        error);
                    var service = new AutomationBridgeService(form);

                    JObject listItem = ReadData(service.Handle(
                        "POST", "/bridge/station/list", "{}"))["items"]?[0] as JObject;
                    AssertStationProjection(listItem);

                    JObject detail = ReadData(service.Handle(
                        "POST",
                        "/bridge/station/get",
                        new JObject { ["stationIndex"] = 0 }.ToString(Formatting.None)));
                    AssertStationProjection(detail);
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void PlanMotionPoints_UsesRobotTwoHundredAndAxisFourHundredSlotLimits()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    DataStation robot = CreateStation("EPSON六轴工站", StationType.Epson);
                    robot.CommunicationName = "EPSON命令";
                    FillNamedPoints(robot, DataStation.RobotPointCapacity);
                    DataStation axis = CreateStation("平台轴工站", StationType.Axis);
                    FillNamedPoints(axis, DataStation.RobotPointCapacity);
                    Assert.IsTrue(
                        form.Runtime.Stores.Stations.TryCommit(
                            form.Runtime.Paths.ConfigPath,
                            new[] { robot, axis },
                            out string error),
                        error);
                    DataStation axisReference = form.Runtime.Stores.Stations.Items[1];
                    DataPos axisPointReference = axisReference.ListDataPos[DataStation.RobotPointCapacity];
                    var axisDictionaryReference = axisReference.dicDataPos;
                    var service = new AutomationBridgeService(form);

                    AutomationBridgeResponse robotGetResponse = service.Handle(
                        "POST",
                        "/bridge/point/get",
                        new JObject
                        {
                            ["stationIndex"] = 0,
                            ["index"] = DataStation.RobotPointCapacity
                        }.ToString(Formatting.None));
                    Assert.AreEqual(400, robotGetResponse.StatusCode, robotGetResponse.Body);
                    Assert.AreEqual(
                        "INVALID_ARGUMENT",
                        JObject.Parse(robotGetResponse.Body)["errorCode"]?.Value<string>());

                    AutomationBridgeResponse robotResponse = PlanPoint(service, 0, "机器人越界点");
                    Assert.AreEqual(409, robotResponse.StatusCode, robotResponse.Body);
                    Assert.AreEqual(
                        "POINT_CAPACITY_EXCEEDED",
                        JObject.Parse(robotResponse.Body)["errorCode"]?.Value<string>());

                    AutomationBridgeResponse axisResponse = PlanPoint(service, 1, "轴工站第201点");
                    Assert.AreEqual(200, axisResponse.StatusCode, axisResponse.Body);
                    JObject axisData = ReadData(axisResponse);
                    Assert.AreEqual(DataStation.PointCapacity,
                        axisData["pointCapacity"]?.Value<int>());
                    Assert.AreEqual(DataStation.RobotPointCapacity,
                        axisData["points"]?[0]?["index"]?.Value<int>());
                    Assert.AreSame(axisReference, form.Runtime.Stores.Stations.Items[1],
                        "名称规划不得替换正式工站配置对象。");
                    Assert.AreSame(axisPointReference,
                        form.Runtime.Stores.Stations.Items[1].ListDataPos[DataStation.RobotPointCapacity],
                        "名称规划不得替换点位对象。");
                    Assert.AreSame(axisDictionaryReference,
                        form.Runtime.Stores.Stations.Items[1].dicDataPos,
                        "名称规划不得替换点位字典对象。");
                }
            }, TimeSpan.FromSeconds(30));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void PlanMotionPoints_WhenPersistenceFails_RollsBackInPlaceChanges()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    DataStation target = CreateStation("目标EPSON工站", StationType.Epson);
                    target.CommunicationName = "EPSON命令";
                    DataStation invalidatedAfterCommit = CreateStation(
                        "并发失效的EPSON工站",
                        StationType.Epson);
                    invalidatedAfterCommit.CommunicationName = "EPSON命令2";
                    Assert.IsTrue(
                        form.Runtime.Stores.Stations.TryCommit(
                            form.Runtime.Paths.ConfigPath,
                            new[] { target, invalidatedAfterCommit },
                            out string error),
                        error);

                    DataStation targetReference = form.Runtime.Stores.Stations.Items[0];
                    DataPos targetPointReference = targetReference.ListDataPos[0];
                    var dictionaryReference = targetReference.dicDataPos;
                    DataStation invalidStation = form.Runtime.Stores.Stations.Items[1];
                    DataPos invalidPoint = invalidStation.ListDataPos[DataStation.RobotPointCapacity];
                    invalidPoint.Name = "触发持久化失败";
                    invalidPoint.IsTaught = true;
                    invalidStation.dicDataPos[invalidPoint.Name] = invalidPoint;

                    var service = new AutomationBridgeService(form);
                    AutomationBridgeResponse response = PlanPoint(service, 0, "待回滚规划点");

                    Assert.AreEqual(500, response.StatusCode, response.Body);
                    Assert.AreEqual(
                        "STATION_COMMIT_FAILED",
                        JObject.Parse(response.Body)["errorCode"]?.Value<string>());
                    Assert.AreSame(targetReference, form.Runtime.Stores.Stations.Items[0]);
                    Assert.AreSame(targetPointReference,
                        form.Runtime.Stores.Stations.Items[0].ListDataPos[0]);
                    Assert.AreSame(dictionaryReference,
                        form.Runtime.Stores.Stations.Items[0].dicDataPos);
                    Assert.IsTrue(string.IsNullOrWhiteSpace(targetPointReference.Name));
                    Assert.IsNull(targetPointReference.IsTaught);
                    Assert.IsFalse(dictionaryReference.ContainsKey("待回滚规划点"));

                    var persisted = new StationDefinitionStore();
                    Assert.IsTrue(persisted.Load(
                        form.Runtime.Paths.ConfigPath,
                        out string loadError), loadError);
                    Assert.IsTrue(string.IsNullOrWhiteSpace(
                        persisted.Items[0].ListDataPos[0].Name));

                    invalidStation.dicDataPos.Remove(invalidPoint.Name);
                    invalidPoint.Name = string.Empty;
                    invalidPoint.IsTaught = null;
                }
            }, TimeSpan.FromSeconds(30));
        }

        private static DataStation CreateStation(string name, StationType type)
        {
            return new DataStation(false)
            {
                Name = name,
                Type = type
            };
        }

        private static void FillNamedPoints(DataStation station, int count)
        {
            for (int index = 0; index < count; index++)
            {
                DataPos point = station.ListDataPos[index];
                point.Name = "点位" + index;
                point.IsTaught = true;
                station.dicDataPos[point.Name] = point;
            }
        }

        private static AutomationBridgeResponse PlanPoint(
            AutomationBridgeService service,
            int stationIndex,
            string name)
        {
            return service.Handle(
                "POST",
                "/bridge/point/plan",
                new JObject
                {
                    ["stationIndex"] = stationIndex,
                    ["pointNames"] = new JArray(name)
                }.ToString(Formatting.None));
        }

        private static void AssertStationProjection(JObject station)
        {
            Assert.IsNotNull(station);
            Assert.AreEqual("Epson", station["type"]?.Value<string>());
            Assert.AreEqual("EPSON命令", station["communicationName"]?.Value<string>());
            Assert.IsFalse(station["pointFromRobot"]?.Value<bool>() == true);
            Assert.IsTrue(station["remoteMode"]?.Value<bool>() == true);
            Assert.AreEqual("EPSON远程", station["remoteCommunicationName"]?.Value<string>());
            Assert.AreEqual(DataStation.RobotPointCapacity,
                station["pointCapacity"]?.Value<int>());
        }

        private static JObject ReadData(AutomationBridgeResponse response)
        {
            Assert.AreEqual(200, response.StatusCode, response.Body);
            JObject body = JObject.Parse(response.Body);
            Assert.IsTrue(body["ok"]?.Value<bool>() == true, response.Body);
            return body["data"] as JObject
                ?? throw new AssertFailedException("Bridge 成功响应缺少 data 对象：" + response.Body);
        }
    }
}
