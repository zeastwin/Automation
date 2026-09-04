// 模块：核心测试 / 汇川机器人工站。
// 职责范围：验证六轴厂商映射、Jog/Inch阈值和初始化自动使能，不加载原生DLL。

using System;
using System.Threading;
using System.Threading.Tasks;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class InovanceStationTests
    {
        [TestMethod]
        public void AxisMapping_Preserves3Point0UvwOrder()
        {
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3, 6, 4, 5 },
                new[]
                {
                    InovanceStationBase.MapVendorAxis(0),
                    InovanceStationBase.MapVendorAxis(1),
                    InovanceStationBase.MapVendorAxis(2),
                    InovanceStationBase.MapVendorAxis(3),
                    InovanceStationBase.MapVendorAxis(4),
                    InovanceStationBase.MapVendorAxis(5)
                });
        }

        [TestMethod]
        public void AxisMotion_UsesJogOnlyWhenAbsoluteOffsetExceeds20()
        {
            var jogApi = new InovanceRobotApiProbe();
            InovanceStation jogStation = CreateStation(jogApi);
            var inchApi = new InovanceRobotApiProbe();
            InovanceV4Station inchStation = CreateV4Station(inchApi);
            try
            {
                InitializeAndWait(jogStation);
                Assert.AreEqual(MotionStationResult.Success, jogStation.AxisMotion(3, 20.01));
                Assert.AreEqual(6, jogApi.LastJogAxis);
                Assert.AreEqual(1, jogApi.LastJogCommand);
                Assert.AreEqual(0, jogApi.InchCalls);

                InitializeAndWait(inchStation);
                Assert.AreEqual(MotionStationResult.Success, inchStation.AxisMotion(3, -20));
                Assert.AreEqual(0, inchApi.JogCalls);
                Assert.AreEqual(6, inchApi.LastInchAxis);
                Assert.AreEqual(-1, inchApi.LastInchCommand);
                Assert.AreEqual(20f, inchApi.LinearStep);
                Assert.AreEqual(20f, inchApi.RotaryStep);
            }
            finally
            {
                jogStation.Release();
                inchStation.Release();
            }
        }

        [TestMethod]
        public void Initialize_UsesConfiguredEndpointAndAutomaticallyLogsInResetsAndEnables()
        {
            var api = new InovanceRobotApiProbe { SystemError = 12 };
            InovanceStation station = CreateStation(api, "192.168.8.21", 2233, 2);
            try
            {
                InitializeAndWait(station);

                Assert.AreEqual(0xC0A80815u, api.Address);
                Assert.AreEqual((ushort)2233, api.Port);
                Assert.AreEqual(0, api.ConnectionId);
                Assert.AreEqual(1, api.LoginCalls);
                Assert.AreEqual(1, api.ResetCalls);
                Assert.AreEqual(1, api.EnableCalls);
                Assert.IsTrue(station.GetStatus().IsServoEnabled);
            }
            finally
            {
                station.Release();
            }
        }

        [TestMethod]
        public void Initialize_WhenRobotIsOffline_RetriesUntilConnectedAndReleaseStopsRetrying()
        {
            var api = new InovanceRobotApiProbe();
            api.FailNextInitializeAttempts(2);
            InovanceStation station = CreateStation(api);

            Assert.AreEqual(MotionStationResult.Success, station.Initialize(),
                "机器人离线不能阻塞平台初始化。");
            Assert.IsTrue(SpinWait.SpinUntil(
                () => station.GetStatus().State == MotionStationState.Idle,
                2000), station.GetStatus().LastError);
            Assert.AreEqual(3, api.InitializeCalls);
            Assert.AreEqual(1, api.EnableCalls);

            Assert.AreEqual(MotionStationResult.Success, station.Release());
            int attemptsAfterRelease = api.InitializeCalls;
            Thread.Sleep(150);
            Assert.AreEqual(attemptsAfterRelease, api.InitializeCalls,
                "释放工站后不得残留重连线程。");
        }

        [TestMethod]
        public void Initialize_LoadsNamedPointFromControllerUsingSixAxisPoseMapping()
        {
            var robotPoint = new InovanceRobotPose();
            Array.Copy(new[] { 11d, 22d, 33d, 44d, 55d, 66d }, robotPoint.Coordinates, 6);
            Array.Copy(new[] { -1, 1, 0, -1 }, robotPoint.ArmParameters, 4);
            var api = new InovanceRobotApiProbe { PointToReturn = robotPoint };
            CreateConfiguration("192.168.1.20", 2222, 1, out DataStation configuration,
                out CommunicationConfigStore communicationStore);
            configuration.ListDataPos[7].Name = "取料点";
            configuration.ListDataPos[7].IsTaught = false;
            var station = new InovanceStation(configuration, communicationStore, api);
            try
            {
                InitializeAndWait(station);

                Assert.AreEqual(7, api.LastGetPointIndex);
                CollectionAssert.AreEqual(
                    new[] { 11d, 22d, 33d, 44d, 55d, 66d },
                    configuration.ListDataPos[7].GetAllValues());
                CollectionAssert.AreEqual(
                    new short[] { 1, 0, 2, 1 },
                    configuration.ListDataPos[7].Pose);
                Assert.AreEqual(true, configuration.ListDataPos[7].IsTaught);
            }
            finally
            {
                station.Release();
            }
        }

        [TestMethod]
        public void ReconnectPointLoad_ReleasesStationLockBeforeWaitingForConfigurationLock()
        {
            var robotPoint = new InovanceRobotPose();
            Array.Copy(new[] { 11d, 22d, 33d, 44d, 55d, 66d }, robotPoint.Coordinates, 6);
            var api = new InovanceRobotApiProbe
            {
                PointToReturn = robotPoint,
                BlockGetPoint = true
            };
            CreateConfiguration("192.168.1.20", 2222, 1, out DataStation configuration,
                out CommunicationConfigStore communicationStore);
            configuration.PointFromRobot = true;
            configuration.ListDataPos[7].Name = "取料点";
            configuration.ListDataPos[7].IsTaught = true;
            var station = new InovanceStation(configuration, communicationStore, api);
            using (var configurationLockEntered = new ManualResetEventSlim(false))
            using (var allowConfigurationLockExit = new ManualResetEventSlim(false))
            {
                Task configurationTask = null;
                Task<MotionStationResult> saveTask = null;
                try
                {
                    Assert.AreEqual(MotionStationResult.Success, station.Initialize());
                    Assert.IsTrue(
                        api.GetPointEntered.Wait(1000),
                        "重连线程未进入持有 syncRoot 的控制器点位读取阶段。");

                    configurationTask = Task.Run(() =>
                    {
                        lock (configuration)
                        {
                            configurationLockEntered.Set();
                            allowConfigurationLockExit.Wait(5000);
                        }
                    });
                    Assert.IsTrue(
                        configurationLockEntered.Wait(1000),
                        "测试未能建立 configuration 锁占用条件。");

                    // 此时重连线程确定持有 syncRoot，配置线程确定持有 configuration。
                    // 放行原生读取后，重连必须先释放 syncRoot 再等待配置提交。
                    api.AllowGetPointReturn.Set();
                    saveTask = Task.Run(() => station.SavePoint(new DataPos(7)
                    {
                        Name = "取料点",
                        IsTaught = true
                    }));

                    Assert.IsTrue(
                        saveTask.Wait(1000),
                        "重连线程在等待 configuration 时仍持有 syncRoot，形成锁反转。");
                    Assert.AreEqual(
                        MotionStationResult.Busy,
                        saveTask.Result,
                        "点位加载未提交前工站应保持忙碌，不得并发写控制器。");
                }
                finally
                {
                    api.AllowGetPointReturn.Set();
                    allowConfigurationLockExit.Set();
                    configurationTask?.Wait(2000);
                    station.Release();
                }

                Assert.IsNotNull(saveTask);
                Assert.IsTrue(SpinWait.SpinUntil(
                    () => configuration.ListDataPos[7].X == 11d,
                    1000), "配置锁释放后重连点位提交未完成。");
            }
        }

        private static void InitializeAndWait(InovanceStationBase station)
        {
            Assert.AreEqual(MotionStationResult.Success, station.Initialize());
            Assert.IsTrue(SpinWait.SpinUntil(
                () => station.GetStatus().State == MotionStationState.Idle,
                2000), station.GetStatus().LastError);
        }

        private static InovanceStation CreateStation(
            InovanceRobotApiProbe api,
            string address = "192.168.1.20",
            int port = 2222,
            int id = 1)
        {
            CreateConfiguration(address, port, id, out DataStation configuration,
                out CommunicationConfigStore communicationStore);
            return new InovanceStation(configuration, communicationStore, api);
        }

        private static InovanceV4Station CreateV4Station(InovanceRobotApiProbe api)
        {
            CreateConfiguration("192.168.1.20", 2222, 1, out DataStation configuration,
                out CommunicationConfigStore communicationStore);
            return new InovanceV4Station(configuration, communicationStore, api);
        }

        private static void CreateConfiguration(
            string address,
            int port,
            int id,
            out DataStation configuration,
            out CommunicationConfigStore communicationStore)
        {
            communicationStore = new CommunicationConfigStore();
            Assert.IsTrue(communicationStore.ReplaceSockets(new[]
            {
                new SocketInfo
                {
                    ID = id,
                    Name = "汇川机器人",
                    Type = "Client",
                    LocalAddress = "0.0.0.0",
                    LocalPort = 0,
                    RemoteAddress = address,
                    RemotePort = port,
                    ConnectTimeoutMs = 5000
                }
            }, out string error), error);
            configuration = new DataStation(false)
            {
                Name = "汇川六轴工站",
                Type = StationType.Inovance,
                CommunicationName = "汇川机器人"
            };
        }

        private sealed class InovanceRobotApiProbe : IInovanceRobotApi
        {
            private int dataStreamMode;
            private int initializeFailuresRemaining;
            private int initializeCalls;

            public uint Address { get; private set; }
            public ushort Port { get; private set; }
            public int ConnectionId { get; private set; }
            public int LoginCalls { get; private set; }
            public int ResetCalls { get; private set; }
            public int EnableCalls { get; private set; }
            public int JogCalls { get; private set; }
            public int InchCalls { get; private set; }
            public int LastJogAxis { get; private set; }
            public int LastJogCommand { get; private set; }
            public int LastInchAxis { get; private set; }
            public int LastInchCommand { get; private set; }
            public float LinearStep { get; private set; }
            public float RotaryStep { get; private set; }
            public int SetPointCalls { get; private set; }
            public int LastSetPointIndex { get; private set; } = -1;
            public InovanceRobotPose LastSetPoint { get; private set; }
            public int ClearPalletCalls { get; private set; }
            public int SetPalletCalls { get; private set; }
            public int GetPalletPointCalls { get; private set; }
            public int MovePositionCalls { get; private set; }
            public int SystemError { get; set; }
            public InovanceRobotPose PointToReturn { get; set; }
            public int LastGetPointIndex { get; private set; } = -1;
            public int InitializeCalls => Volatile.Read(ref initializeCalls);
            public bool BlockGetPoint { get; set; }
            public ManualResetEventSlim GetPointEntered { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim AllowGetPointReturn { get; } = new ManualResetEventSlim(false);

            public void FailNextInitializeAttempts(int count)
            {
                Volatile.Write(ref initializeFailuresRemaining, count);
            }

            public int Initialize(uint address, ushort port, int timeoutMs, int connectionId)
            {
                Interlocked.Increment(ref initializeCalls);
                Address = address;
                Port = port;
                ConnectionId = connectionId;
                if (Interlocked.Decrement(ref initializeFailuresRemaining) >= 0)
                {
                    return -255;
                }
                return 0;
            }

            public int Exit(int connectionId) => 0;

            public int AcquirePermit(int command, int connectionId) => 0;

            public int UserLogin(int type, byte[] password, int connectionId)
            {
                LoginCalls++;
                return 0;
            }

            public int GetEmergencyStopStatus(out int status, int connectionId)
            {
                status = 0;
                return 0;
            }

            public int EmergencyStop(int command, int connectionId) => 0;

            public int GetSystemError(out int error, int connectionId)
            {
                error = SystemError;
                SystemError = 0;
                return 0;
            }

            public int ResetError(int connectionId)
            {
                ResetCalls++;
                return 0;
            }

            public int SetCoordinate(int type, int connectionId) => 0;

            public int SetMode(int mode, int connectionId) => 0;

            public int GetMotorStatus(out int status, int connectionId)
            {
                status = 0;
                return 0;
            }

            public int MotorEnable(int command, int connectionId)
            {
                if (command == 1)
                {
                    EnableCalls++;
                }
                return 0;
            }

            public int GetDataStreamMode(out int mode, int connectionId)
            {
                mode = dataStreamMode;
                return 0;
            }

            public int SetDataStreamMode(int command, int connectionId)
            {
                dataStreamMode = command == 3 ? 1 : command;
                return 0;
            }

            public int SetSlewMode(int command, int connectionId) => 0;

            public int GetMotionStatus(out int status, int connectionId)
            {
                status = 0;
                return 0;
            }

            public int GetPosition(out InovanceRobotPose position, int connectionId)
            {
                position = new InovanceRobotPose();
                return 0;
            }

            public int GetPoint(int pointIndex, out InovanceRobotPose position, int connectionId)
            {
                LastGetPointIndex = pointIndex;
                if (BlockGetPoint)
                {
                    GetPointEntered.Set();
                    AllowGetPointReturn.Wait(5000);
                }
                position = PointToReturn?.Clone() ?? new InovanceRobotPose();
                return 0;
            }

            public int SetPoint(int pointIndex, InovanceRobotPose position, int connectionId)
            {
                SetPointCalls++;
                LastSetPointIndex = pointIndex;
                LastSetPoint = position?.Clone();
                return 0;
            }

            public int ClearPallet(int connectionId)
            {
                ClearPalletCalls++;
                return 0;
            }

            public int SetPalletParameters(int rowCount, int columnCount, int connectionId)
            {
                SetPalletCalls++;
                return 0;
            }

            public int GetPalletPoint(InovanceRobotPose point1, InovanceRobotPose point2,
                InovanceRobotPose point3, int rowIndex, int columnIndex,
                out InovanceRobotPose position, int connectionId)
            {
                GetPalletPointCalls++;
                position = point1.Clone();
                return 0;
            }

            public int MovePoint(int pointIndex, bool linear, int speed, int zone, int connectionId) => 0;

            public int MovePosition(InovanceRobotPose position, bool linear, int speed, int zone, int connectionId)
            {
                MovePositionCalls++;
                return 0;
            }

            public int SetRapidMove(int moveType, int enabled, int connectionId) => 0;

            public int SetVelocity(int velocity, int connectionId) => 0;

            public int SetAcceleration(double acceleration, double deceleration, int connectionId) => 0;

            public int SetInchMode(int command, int connectionId) => 0;

            public int SetStepMotion(float linearStep, float rotaryStep, int connectionId)
            {
                LinearStep = linearStep;
                RotaryStep = rotaryStep;
                return 0;
            }

            public int SetInchStep(int stepType, int connectionId) => 0;

            public int Jog(int vendorAxis, int command, int connectionId)
            {
                JogCalls++;
                LastJogAxis = vendorAxis;
                LastJogCommand = command;
                return 0;
            }

            public int Inch(int vendorAxis, int command, int connectionId)
            {
                InchCalls++;
                LastInchAxis = vendorAxis;
                LastInchCommand = command;
                return 0;
            }

            public void Delay(int milliseconds)
            {
            }
        }
    }
}
