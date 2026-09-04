using System;
// 模块：核心测试 / EPSON 六轴工站。
// 职责范围：固化 3.0 指令模板、工具号和 XYZUVW 手动偏移语义；测试不连接机器人。

using System.Collections.Generic;
using System.IO;
using System.Threading;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class EpsonStationTests
    {
        [TestMethod]
        public void CommandCatalog_FormatsLegacyPointAndSpeedCommands()
        {
            EpsonCommandCatalog catalog = LoadCatalog();

            Assert.IsTrue(catalog.TryBuild(
                EpsonCommandCatalog.GoPoint,
                out string goPoint,
                out string goPointError,
                6,
                12,
                3), goPointError);
            Assert.AreEqual("RobotGoPoint,6,12,3,\r\n", goPoint);

            Assert.IsTrue(catalog.TryBuild(
                EpsonCommandCatalog.SetSpeed,
                out string setSpeed,
                out string setSpeedError,
                25.5,
                30,
                35), setSpeedError);
            Assert.AreEqual("RobotSetSpeed,25.5,30,35,\r\n", setSpeed);

            Assert.IsTrue(catalog.TryBuild(
                EpsonCommandCatalog.Home,
                out string home,
                out string homeError), homeError);
            Assert.AreEqual("RobotHome,\r\n", home);

            Assert.IsTrue(catalog.TryBuild(
                EpsonCommandCatalog.MovePoint,
                out string movePoint,
                out string movePointError,
                6,
                12,
                3), movePointError);
            Assert.AreEqual("RobotMovePoint,6,12,3,\r\n", movePoint);

            Assert.IsTrue(catalog.TryBuild(
                EpsonCommandCatalog.GetPosition,
                out string getPosition,
                out string getPositionError,
                6,
                -1), getPositionError);
            Assert.AreEqual("RobotGetPosition,6,-1,\r\n", getPosition);

            Assert.IsTrue(catalog.TryBuild(
                EpsonCommandCatalog.SavePoint,
                out string savePoint,
                out string savePointError,
                6,
                12,
                1,
                2,
                3,
                4,
                5,
                6), savePointError);
            Assert.AreEqual("RobotSavePoint,6,12,1,2,3,4,5,6,\r\n", savePoint);

            Assert.IsTrue(catalog.TryBuild(
                EpsonCommandCatalog.CreatePallet,
                out string createPallet,
                out string createPalletError,
                7,
                1,
                2,
                3,
                4,
                3,
                2), createPalletError);
            Assert.AreEqual("CreatePallet,7,1,2,3,4,3,2,\r\n", createPallet);

            Assert.IsTrue(catalog.TryBuild(
                EpsonCommandCatalog.GoPalletPosition,
                out string goPalletPosition,
                out string goPalletPositionError,
                7,
                0), goPalletPositionError);
            Assert.AreEqual("GoPalletPos,7,0,\r\n", goPalletPosition);
        }

        [TestMethod]
        public void AxisMotion_UsesSixChannelsAndClampsOffsetToTwenty()
        {
            EpsonCommandCatalog catalog = LoadCatalog();
            var sent = new List<string>();
            EpsonStation station = CreateStation(
                catalog,
                (channel, command) =>
                {
                    sent.Add(channel + "|" + command);
                    return true;
                },
                (channel, timeout, cancellationToken) =>
                    CommReceiveResult.CreateSuccess("ok,1,2,3,4,5,6;", null, null));

            Assert.AreEqual(MotionStationResult.Success, station.Initialize());
            Assert.AreEqual(
                MotionStationResult.Success,
                station.AxisMotion(2, 35, StationAxisMoveMode.Relative));
            Assert.AreEqual(
                "EPSON命令|RobotMoveOffset,6,0,0,0,20,0,0,0,\r\n",
                sent[0]);

            Assert.AreEqual(MotionStationResult.Success, station.WaitMoveFinish());
            CollectionAssert.AreEqual(
                new[] { 1d, 2d, 3d, 4d, 5d, 6d },
                new List<double>(station.GetStatus().Position).ToArray());

            Assert.AreEqual(
                MotionStationResult.Success,
                station.AxisMotion(5, -35, StationAxisMoveMode.Relative));
            Assert.AreEqual(
                "EPSON命令|RobotMoveOffset,6,0,0,0,0,0,0,-20,\r\n",
                sent[1]);
        }

        [TestMethod]
        public void MoveToPoint_PreservesRobotPointIndexAndToolNumber()
        {
            EpsonCommandCatalog catalog = LoadCatalog();
            string sentCommand = null;
            EpsonStation station = CreateStation(
                catalog,
                (channel, command) =>
                {
                    sentCommand = command;
                    return true;
                },
                (channel, timeout, cancellationToken) =>
                    CommReceiveResult.CreateSuccess("ok,0,0,0,0,0,0;", null, null));
            var point = new DataPos(18)
            {
                Name = "取料位",
                IsTaught = true
            };

            Assert.AreEqual(MotionStationResult.Success, station.Initialize());
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(point, StationMoveMode.Go, tool: 4));

            Assert.AreEqual("RobotGoPoint,6,18,4,\r\n", sentCommand);
        }

        [TestMethod]
        public void PalletOperations_PreserveLegacyControllerCommands()
        {
            EpsonCommandCatalog catalog = LoadCatalog();
            string exchangedCommand = null;
            string sentCommand = null;
            var station = new EpsonStation(
                CreateConfiguration(),
                catalog,
                (channel, command) =>
                {
                    sentCommand = command;
                    return true;
                },
                (channel, timeout, cancellationToken) =>
                    CommReceiveResult.CreateSuccess("ok,0,0,0,0,0,0;", null, null),
                (channel, command, timeout) =>
                {
                    exchangedCommand = command;
                    return CommReceiveResult.CreateSuccess("ok", null, null);
                },
                new CapturingLogger());
            var referencePoints = new List<DataPos>
            {
                CreatePoint(1, "左上"),
                CreatePoint(2, "右上"),
                CreatePoint(3, "左下"),
                CreatePoint(4, "右下")
            };

            Assert.AreEqual(MotionStationResult.Success, station.Initialize());
            Assert.AreEqual(
                MotionStationResult.Success,
                station.CreateTray(7, 2, 3, referencePoints));
            Assert.AreEqual("CreatePallet,7,1,2,3,4,3,2,\r\n", exchangedCommand);

            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveTrayPoint(7, 0, null));
            Assert.AreEqual("GoPalletPos,7,0,\r\n", sentCommand);
        }

        [TestMethod]
        public void RobotPointIndex_RejectsIndexOutsideLegacyZeroToOneHundredNinetyNine()
        {
            EpsonCommandCatalog catalog = LoadCatalog();
            int commandCount = 0;
            EpsonStation station = CreateStation(
                catalog,
                (channel, command) =>
                {
                    commandCount++;
                    return true;
                },
                (channel, timeout, cancellationToken) =>
                    CommReceiveResult.CreateSuccess("ok,0,0,0,0,0,0;", null, null));
            DataPos invalidPoint = CreatePoint(200, "越界点");

            Assert.AreEqual(MotionStationResult.Success, station.Initialize());
            Assert.AreEqual(
                MotionStationResult.InvalidParameter,
                station.MoveToPoint(invalidPoint, StationMoveMode.Go));
            Assert.AreEqual(
                MotionStationResult.InvalidParameter,
                station.SavePoint(invalidPoint));
            Assert.AreEqual(0, commandCount);
        }

        [TestMethod]
        public void Initialize_RemoteModeRequiresExplicitIndependentChannel()
        {
            EpsonCommandCatalog catalog = LoadCatalog();
            DataStation configuration = CreateConfiguration();
            configuration.RemoteMode = true;
            configuration.RemoteCommunicationName = string.Empty;
            var logger = new CapturingLogger();
            var station = new EpsonStation(
                configuration,
                catalog,
                (channel, command) => true,
                (channel, timeout, cancellationToken) =>
                    CommReceiveResult.CreateSuccess("ok,0,0,0,0,0,0;", null, null),
                (channel, command, timeout) =>
                    CommReceiveResult.CreateSuccess("ok", null, null),
                logger);

            Assert.AreEqual(MotionStationResult.InvalidConfiguration, station.Initialize());
            StringAssert.Contains(logger.LastMessage, "未配置独立远程通讯对象");
        }

        [TestMethod]
        public void Reconnect_RemoteModeLogsInAgainAndReloadsRobotPoints()
        {
            EpsonCommandCatalog catalog = LoadCatalog();
            DataStation configuration = CreateConfiguration();
            configuration.RemoteMode = true;
            configuration.RemoteCommunicationName = "EPSON远程";
            configuration.PointFromRobot = true;
            configuration.ListDataPos[1].Name = "取料位";
            configuration.ListDataPos[1].IsTaught = true;
            bool channelsActive = true;
            int loginCount = 0;
            int pointReadCount = 0;
            var station = new EpsonStation(
                configuration,
                catalog,
                (channel, command) => true,
                (channel, timeout, cancellationToken) =>
                    CommReceiveResult.CreateSuccess("ok,0,0,0,0,0,0;", null, null),
                (channel, command, timeout) =>
                {
                    if (command == "$Login\r\n")
                    {
                        loginCount++;
                        return CommReceiveResult.CreateSuccess("#Login,0", null, null);
                    }
                    if (command == "$Reset\r\n")
                    {
                        return CommReceiveResult.CreateSuccess("#Reset,0", null, null);
                    }
                    if (command == "$Start,0\r\n")
                    {
                        return CommReceiveResult.CreateSuccess("#Start,0", null, null);
                    }
                    if (command.StartsWith("RobotGetPosition,", StringComparison.Ordinal))
                    {
                        pointReadCount++;
                        return CommReceiveResult.CreateSuccess("ok,1,2,3,4,5,6;", null, null);
                    }
                    return CommReceiveResult.CreateFailure("未预期命令：" + command);
                },
                new CapturingLogger(),
                channel => channelsActive);

            Assert.AreEqual(MotionStationResult.Success, station.Initialize());
            Assert.AreEqual(1, loginCount);
            Assert.AreEqual(2, pointReadCount, "初始化应读取命名点和当前位置。");

            channelsActive = false;
            Assert.AreEqual(MotionStationState.Disconnected, station.GetStatus().State);
            channelsActive = true;

            Assert.AreEqual(
                MotionStationResult.Success,
                station.GetCurrentPosition(0, out _));
            Assert.AreEqual(2, loginCount, "重连后必须重新建立EPSON远程控制会话。");
            Assert.AreEqual(5, pointReadCount, "重连后应重载命名点、当前位置，再完成本次位置读取。");
        }

        [TestMethod]
        public void Stop_ConfirmsControllerRoundTripBeforeChangingStateToIdle()
        {
            EpsonCommandCatalog catalog = LoadCatalog();
            var sentCommands = new List<string>();
            string confirmationCommand = null;
            var station = new EpsonStation(
                CreateConfiguration(),
                catalog,
                (channel, command) =>
                {
                    sentCommands.Add(command);
                    return true;
                },
                (channel, timeout, cancellationToken) =>
                    CommReceiveResult.CreateFailure("不应读取旧运动完成消息"),
                (channel, command, timeout) =>
                {
                    confirmationCommand = command;
                    return CommReceiveResult.CreateSuccess(
                        "ok,11,12,13,14,15,16;",
                        null,
                        null);
                },
                new CapturingLogger());

            Assert.AreEqual(MotionStationResult.Success, station.Initialize());
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(CreatePoint(18, "取料位"), StationMoveMode.Go));
            Assert.AreEqual(MotionStationState.Moving, station.GetStatus().State);

            Assert.AreEqual(MotionStationResult.Success, station.Stop());

            CollectionAssert.AreEqual(
                new[] { "RobotGoPoint,6,18,0,\r\n", "RobotStop\r\n" },
                sentCommands.ToArray());
            Assert.AreEqual("RobotGetPosition,6,-1,\r\n", confirmationCommand);
            Assert.AreEqual(MotionStationState.Idle, station.GetStatus().State);
            CollectionAssert.AreEqual(
                new[] { 11d, 12d, 13d, 14d, 15d, 16d },
                new List<double>(station.GetStatus().Position).ToArray());
            Assert.AreEqual(MotionStationResult.Success, station.WaitMoveFinish());
        }

        [TestMethod]
        public void Stop_WhenControllerConfirmationFails_DoesNotExposeIdleState()
        {
            EpsonCommandCatalog catalog = LoadCatalog();
            var logger = new CapturingLogger();
            var station = new EpsonStation(
                CreateConfiguration(),
                catalog,
                (channel, command) => true,
                (channel, timeout, cancellationToken) =>
                    CommReceiveResult.CreateSuccess("ok,1,2,3,4,5,6;", null, null),
                (channel, command, timeout) =>
                    CommReceiveResult.CreateFailure("TCP请求超时"),
                logger);

            Assert.AreEqual(MotionStationResult.Success, station.Initialize());
            Assert.AreEqual(
                MotionStationResult.Success,
                station.MoveToPoint(CreatePoint(18, "取料位"), StationMoveMode.Go));

            Assert.AreEqual(MotionStationResult.Timeout, station.Stop());
            Assert.AreEqual(MotionStationState.Faulted, station.GetStatus().State);
            Assert.AreNotEqual(MotionStationResult.Success, station.WaitMoveFinish());
            StringAssert.Contains(logger.LastMessage, "未能确认机器人停稳");
        }

        private static EpsonStation CreateStation(
            EpsonCommandCatalog catalog,
            Func<string, string, bool> send,
            Func<string, int, CancellationToken, CommReceiveResult> receive)
        {
            return new EpsonStation(
                CreateConfiguration(),
                catalog,
                send,
                receive,
                (channel, command, timeout) =>
                    CommReceiveResult.CreateSuccess("ok", null, null),
                new CapturingLogger());
        }

        private static DataStation CreateConfiguration()
        {
            return new DataStation(false)
            {
                Name = "EPSON六轴工站",
                Type = StationType.Epson,
                CommunicationName = "EPSON命令",
                PointFromRobot = false
            };
        }

        private static DataPos CreatePoint(int index, string name)
        {
            return new DataPos(index)
            {
                Name = name,
                IsTaught = true
            };
        }

        private static EpsonCommandCatalog LoadCatalog()
        {
            using (var directory = new TemporaryDirectory())
            {
                Assert.IsTrue(
                    EpsonCommandCatalog.TryLoad(
                        new PlatformPaths(directory.FullPath),
                        out EpsonCommandCatalog catalog,
                        out string error),
                    error);
                Assert.IsTrue(File.Exists(Path.Combine(
                    directory.FullPath,
                    "RbtCmd",
                    "Epson.ini")));
                return catalog;
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public string LastMessage { get; private set; }

            public void Log(string message, LogLevel level)
            {
                LastMessage = message;
            }
        }
    }
}
