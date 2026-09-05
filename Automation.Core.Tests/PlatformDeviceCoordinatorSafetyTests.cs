using System;
// 模块：核心测试 / 设备生命周期安全。
// 职责范围：验证 3.0 急停、刹车语义在实例级设备协调器中的直接接入。

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class PlatformDeviceCoordinatorSafetyTests
    {
        [TestMethod]
        public void IoUsedType_ExposesTheCompleteThreePointZeroContract()
        {
            TypeConverter.StandardValuesCollection values =
                new OperationTypePartial.IOUsedItem().GetStandardValues(null);

            CollectionAssert.AreEqual(
                new[] { "通用", "急停", "复位", "启动", "暂停", "停止", "刹车" },
                values.Cast<string>().ToArray());
        }

        [TestMethod]
        public void Initialize_EnablesAndConfirmsEveryAxisBeforeReleasingBrake()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 2,
                stationCount: 1,
                includeEmergencyInput: true,
                includeBrakeOutput: true))
            {
                fixture.Coordinator.Initialize();

                Assert.IsTrue(fixture.Device.BrakeReleased.Wait(1000));
                string[] calls = fixture.Device.SnapshotCalls();
                int servoOn = RequireIndex(calls, "SetAllAxisSevonOn");
                int stationsInitialized = RequireIndex(calls, "InitializeStations");
                int axis0Confirmed = RequireIndex(calls, "GetAxisServo:0-0");
                int axis1Confirmed = RequireIndex(calls, "GetAxisServo:0-1");
                int brakeReleased = RequireIndex(
                    calls,
                    "SetIO:刹车输出:True:取反");

                Assert.IsTrue(servoOn < stationsInitialized);
                Assert.IsTrue(stationsInitialized < axis0Confirmed);
                Assert.IsTrue(stationsInitialized < axis1Confirmed);
                Assert.IsTrue(axis0Confirmed < brakeReleased);
                Assert.IsTrue(axis1Confirmed < brakeReleased);
                Assert.IsFalse(fixture.Runtime.Safety.IsLocked);
            }
        }

        [TestMethod]
        public void Initialize_运动配置故障时不接触任何卡机器人或刹车接口()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: true,
                includeBrakeOutput: true))
            {
                fixture.Runtime.Readiness.MotionConfigFaulted = true;
                fixture.Runtime.Readiness.MotionConfigFaultReason = "测试非法运动配置";

                fixture.Coordinator.Initialize();

                Assert.AreEqual(0, fixture.Device.SnapshotCalls().Length,
                    "配置故障应在读取IO或调用任何设备初始化接口前返回。");
                Assert.IsFalse(fixture.Runtime.Safety.IsLocked,
                    "运动配置故障不应关闭MES、通讯等非设备服务。");
            }
        }

        [TestMethod]
        public void Initialize_AxisNotEnabled_KeepsBrakeEngagedAndReleasesDevices()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 2,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: true))
            {
                fixture.Device.ServoEnabled = false;
                string fault = null;
                fixture.Coordinator.Faulted += message => fault = message;

                fixture.Coordinator.Initialize();

                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsTrue(fixture.Runtime.Safety.IsLocked);
                Assert.IsFalse(calls.Contains("SetIO:刹车输出:True:取反"));
                AssertBefore(calls, "StopAll:True", "SetIO:刹车输出:False:取反");
                AssertBefore(calls, "SetIO:刹车输出:False:取反", "ReleaseStations");
                AssertBefore(calls, "ReleaseStations", "StopConnect");
                StringAssert.Contains(fault, "未确认上使能");
            }
        }

        [TestMethod]
        public void Initialize_ConfiguredCardReturnsFalseWithoutReason_LocksBecauseHardwareStateIsUnknown()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: true))
            {
                fixture.Device.InitCardSucceeds = false;
                string fault = null;
                fixture.Coordinator.Faulted += message => fault = message;

                fixture.Coordinator.Initialize();

                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsTrue(fixture.Runtime.Safety.IsLocked);
                StringAssert.Contains(fault, "没有提供可安全降级");
                Assert.IsFalse(calls.Contains("InitializeStations"));
                Assert.IsFalse(calls.Contains("SetAllAxisSevonOn"));
                Assert.IsFalse(calls.Contains("SetIO:刹车输出:True:取反"));
                Assert.IsTrue(calls.Contains("StopAll:True"));
                AssertBefore(calls, "ReleaseStations", "StopConnect");
            }
        }

        [TestMethod]
        public void Initialize_UnclassifiedInitializationException_LocksBecauseHardwareStateIsUnknown()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: true))
            {
                fixture.Device.InitCardException = new InvalidOperationException("测试初始化异常");
                string fault = null;
                fixture.Coordinator.Faulted += message => fault = message;

                fixture.Coordinator.Initialize();

                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsTrue(fixture.Runtime.Safety.IsLocked);
                StringAssert.Contains(fault, "测试初始化异常");
                Assert.IsFalse(calls.Contains("InitializeStations"));
                Assert.IsFalse(calls.Contains("SetAllAxisSevonOn"));
                Assert.IsFalse(calls.Contains("SetIO:刹车输出:True:取反"));
                Assert.IsTrue(calls.Contains("StopAll:True"));
                AssertBefore(calls, "ReleaseStations", "StopConnect");
            }
        }

        [TestMethod]
        public void Initialize_ConfirmedCardUnavailable_DegradesAndContinuesInitializingStations()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: true))
            {
                fixture.Device.InitCardException = new MotionCardUnavailableException(
                    "SDK实际发现0张卡");
                string fault = null;
                fixture.Coordinator.Faulted += message => fault = message;

                fixture.Coordinator.Initialize();

                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsFalse(fixture.Runtime.Safety.IsLocked);
                Assert.IsNull(fault);
                Assert.IsTrue(calls.Contains("InitializeStations"));
                Assert.IsFalse(calls.Contains("SetAllAxisSevonOn"));
                Assert.IsFalse(calls.Contains("SetIO:刹车输出:True:取反"));
                Assert.IsFalse(calls.Contains("StopAll:True"));

                Proc process = TestProcessFactory.CreateEndingProcess("缺卡开发环境流程", 1);
                fixture.Runtime.ProcessEngine.Context.Procs = new List<Proc> { process };
                Assert.IsTrue(
                    fixture.Runtime.ProcessEngine.StartProc(process, 0),
                    "明确未检测到控制卡不能形成全局流程启动门禁。");
            }
        }

        [TestMethod]
        public void Initialize_CardBecomesActiveThenFails_StillLocksAndReleasesSafely()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: true))
            {
                fixture.Device.DownLoadConfigException =
                    new InvalidOperationException("测试下载配置异常");
                string fault = null;
                fixture.Coordinator.Faulted += message => fault = message;

                fixture.Coordinator.Initialize();

                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsTrue(fixture.Runtime.Safety.IsLocked);
                StringAssert.Contains(fixture.Runtime.Safety.LockReason, "测试下载配置异常");
                StringAssert.Contains(fault, "测试下载配置异常");
                Assert.IsFalse(calls.Contains("InitializeStations"));
                Assert.IsTrue(calls.Contains("StopAll:True"));
                Assert.IsTrue(calls.Contains("StopAxis:0-0:1"));
                AssertBefore(calls, "ReleaseStations", "StopConnect");
            }
        }

        [TestMethod]
        public void Stop_UninitializedCardAndStations_AreIdempotentAndDoNotCreateSafetyLock()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: true))
            {
                fixture.Device.InitCardException = new MotionCardUnavailableException(
                    "SDK实际发现0张卡");
                fixture.Device.InitializeStationsResult = MotionStationResult.NotConnected;
                fixture.Device.StopAllStationsResult = MotionStationResult.NotInitialized;
                fixture.Device.WaitStationMotionResult = MotionStationResult.NotInitialized;
                fixture.Device.ReleaseStationsResult = MotionStationResult.NotInitialized;

                fixture.Coordinator.Initialize();
                fixture.Device.ClearCalls();
                fixture.Coordinator.Stop();

                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsFalse(fixture.Runtime.Safety.IsLocked);
                Assert.IsTrue(calls.Contains("StopAll:False"));
                Assert.IsTrue(calls.Contains("WaitStationMotion:0"));
                Assert.IsTrue(calls.Contains("ReleaseStations"));
                Assert.IsTrue(calls.Contains("StopConnect"));
                Assert.IsFalse(calls.Any(call => call.StartsWith("GetInPos:", StringComparison.Ordinal)));
                Assert.IsFalse(calls.Any(call => call.StartsWith("SetIO:刹车输出:", StringComparison.Ordinal)));
            }
        }

        [TestMethod]
        public void Stop_CleanupFailure_DoesNotOverwriteExistingSafetyLockReason()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: false))
            {
                fixture.Coordinator.Initialize();
                fixture.Device.StopAllStationsResult = MotionStationResult.BaseFunctionError;
                fixture.Device.WaitStationMotionResult = MotionStationResult.Timeout;
                fixture.Runtime.Safety.Lock("首个设备故障");

                fixture.Coordinator.Stop();

                Assert.IsTrue(fixture.Runtime.Safety.IsLocked);
                Assert.AreEqual("首个设备故障", fixture.Runtime.Safety.LockReason);
            }
        }

        [TestMethod]
        public void Initialize_ZeroCardRobotMode_DoesNotCallCardInitialization()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: false,
                axisCount: 0,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: false))
            {
                fixture.Device.InitCardException = new InvalidOperationException(
                    "零卡模式不应调用此入口");

                fixture.Coordinator.Initialize();

                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsTrue(calls.Contains("InitCardType"));
                Assert.IsFalse(calls.Contains("InitCard"));
                Assert.IsTrue(calls.Contains("InitializeStations"));
                Assert.IsFalse(fixture.Runtime.Safety.IsLocked);
            }
        }

        [TestMethod]
        public void Initialize_CardUnavailableWithEmergencyIo_DoesNotPollMissingBusOrLockLater()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: true,
                includeBrakeOutput: false))
            {
                fixture.Device.InitCardException = new MotionCardUnavailableException(
                    "SDK实际发现0张卡");

                fixture.Coordinator.Initialize();
                Assert.IsTrue(fixture.Device.StationStatusRead.Wait(1000));

                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsFalse(fixture.Runtime.Safety.IsLocked);
                Assert.IsTrue(calls.Contains("InitializeStations"));
                Assert.IsTrue(calls.Contains("GetStationStatus:0"));
                Assert.IsFalse(calls.Any(call => call.StartsWith(
                    "GetInIO:急停输入", StringComparison.Ordinal)));
            }
        }

        [TestMethod]
        public void Initialize_EmergencyAlreadyActive_DoesNotReleaseBrake()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: true,
                includeBrakeOutput: true))
            {
                fixture.Device.EmergencyActive = true;
                string fault = null;
                fixture.Coordinator.Faulted += message => fault = message;

                fixture.Coordinator.Initialize();

                string[] calls = fixture.Device.SnapshotCalls();
                int download = RequireIndex(calls, "DownLoadConfig");
                int emergencyRead = RequireIndex(calls, "GetInIO:急停输入:True:取反");
                Assert.IsTrue(download < emergencyRead);
                Assert.IsFalse(calls.Contains("SetIO:刹车输出:True:取反"));
                Assert.IsTrue(fixture.Runtime.Safety.IsLocked);
                StringAssert.Contains(fault, "启动前检测到急停输入");
                AssertBefore(calls, "StopAll:True", "SetIO:刹车输出:False:取反");
                AssertBefore(calls, "SetIO:刹车输出:False:取反", "StopConnect");
            }
        }

        [TestMethod]
        public void Initialize_EmergencyActivatesDuringStationInitialization_DoesNotReleaseBrake()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: true,
                includeBrakeOutput: true))
            {
                fixture.Device.ActivateEmergencyOnStationInitialize = true;
                string fault = null;
                fixture.Coordinator.Faulted += message => fault = message;

                fixture.Coordinator.Initialize();

                string[] calls = fixture.Device.SnapshotCalls();
                int servoOn = RequireIndex(calls, "SetAllAxisSevonOn");
                int stationInitialize = RequireIndex(calls, "InitializeStations");
                int emergencyRead = RequireIndex(calls, "GetInIO:急停输入:True:取反");
                Assert.IsTrue(servoOn < stationInitialize);
                Assert.IsTrue(stationInitialize < emergencyRead);
                Assert.IsFalse(calls.Contains("SetIO:刹车输出:True:取反"));
                Assert.IsTrue(fixture.Runtime.Safety.IsLocked);
                StringAssert.Contains(fault, "放刹车前检测到急停输入");
                AssertBefore(calls, "StopAll:True", "SetIO:刹车输出:False:取反");
            }
        }

        [TestMethod]
        public void Stop_StopsAndWaitsBeforeEngagingBrakeAndClosingCard()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 2,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: true))
            {
                fixture.Coordinator.Initialize();
                Assert.IsTrue(fixture.Device.BrakeReleased.Wait(1000));
                fixture.Device.ClearCalls();

                fixture.Coordinator.Stop();
                fixture.Coordinator.Stop();

                string[] calls = fixture.Device.SnapshotCalls();
                int stop = RequireIndex(calls, "StopAll:False");
                int wait = RequireIndex(calls, "WaitStationMotion:0");
                int inPosition = RequireIndexAfter(calls, "GetInPos:0-0", stop);
                int brakeEngaged = RequireIndex(calls, "SetIO:刹车输出:False:取反");
                int release = RequireIndex(calls, "ReleaseStations");
                int closeCard = RequireIndex(calls, "StopConnect");

                Assert.IsTrue(stop < wait);
                Assert.IsTrue(wait < inPosition);
                Assert.IsTrue(inPosition < brakeEngaged);
                Assert.IsTrue(brakeEngaged < release);
                Assert.IsTrue(release < closeCard);
                Assert.AreEqual(1, calls.Count(call => call == "ReleaseStations"));
                Assert.AreEqual(1, calls.Count(call => call == "StopConnect"));
            }
        }

        [TestMethod]
        public void Stop_AlsoStopsPhysicalAxisNotBoundToAnyStation()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 0,
                includeEmergencyInput: false,
                includeBrakeOutput: true))
            {
                fixture.Coordinator.Initialize();
                Assert.IsTrue(fixture.Device.BrakeReleased.Wait(1000));
                fixture.Device.ClearCalls();

                fixture.Coordinator.Stop();

                string[] calls = fixture.Device.SnapshotCalls();
                AssertBefore(calls, "StopAxis:0-0:0", "SetIO:刹车输出:False:取反");
                AssertBefore(calls, "SetIO:刹车输出:False:取反", "StopConnect");
            }
        }

        [TestMethod]
        public void EmergencyLogicalInput_LocksStopsEngagesBrakeAndDoesNotSelfWait()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: true,
                axisCount: 1,
                stationCount: 1,
                includeEmergencyInput: true,
                includeBrakeOutput: true))
            {
                string fault = null;
                long callbackElapsedMilliseconds = long.MaxValue;
                using (var callbackReturned = new ManualResetEventSlim(false))
                {
                    fixture.Coordinator.Faulted += message =>
                    {
                        fault = message;
                        long started = Environment.TickCount;
                        fixture.Coordinator.Stop();
                        callbackElapsedMilliseconds = Math.Max(
                            0,
                            (long)Environment.TickCount - started);
                        callbackReturned.Set();
                    };
                    fixture.Coordinator.Initialize();
                    Assert.IsTrue(fixture.Device.BrakeReleased.Wait(1000));
                    fixture.Device.ClearCalls();

                    // GetInIO 返回的已经是逻辑值；EffectLevel=取反也不能在协调器内再次翻转。
                    fixture.Device.EmergencyActive = true;

                    Assert.IsTrue(fixture.Device.EmergencyActiveRead.Wait(3000));
                    Assert.IsTrue(callbackReturned.Wait(3000));
                    Assert.IsTrue(
                        callbackElapsedMilliseconds < 500,
                        $"故障回调内 Stop 不应等待当前监控任务，实际 {callbackElapsedMilliseconds}ms。");
                }

                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsTrue(fixture.Runtime.Safety.IsLocked);
                StringAssert.Contains(fault, "检测到急停输入:急停输入");
                Assert.IsTrue(calls.Contains("GetInIO:急停输入:True:取反"));
                AssertBefore(calls, "StopAll:True", "SetIO:刹车输出:False:取反");
                AssertBefore(calls, "SetIO:刹车输出:False:取反", "ReleaseStations");
                AssertBefore(calls, "ReleaseStations", "StopConnect");
            }
        }

        [TestMethod]
        public void RobotStationFault_WithoutMotionCard_StillTriggersEmergencyRelease()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: false,
                axisCount: 0,
                stationCount: 2,
                includeEmergencyInput: false,
                includeBrakeOutput: false))
            {
                fixture.Device.SetStationState(1, MotionStationState.Faulted, "机器人报警");
                string fault = null;
                using (var faultRaised = new ManualResetEventSlim(false))
                {
                    fixture.Coordinator.Faulted += message =>
                    {
                        fault = message;
                        faultRaised.Set();
                    };

                    fixture.Coordinator.Initialize();

                    Assert.IsTrue(faultRaised.Wait(3000));
                }
                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsTrue(fixture.Runtime.Safety.IsLocked);
                Assert.IsTrue(calls.Contains("GetStationStatus:0"));
                Assert.IsTrue(calls.Contains("GetStationStatus:1"));
                Assert.IsTrue(calls.Contains("StopAll:True"));
                StringAssert.Contains(fault, "1号六轴工站故障:机器人报警");
            }
        }

        [TestMethod]
        public void RobotStationDisconnected_IsPolledButDoesNotTriggerSafetyFault()
        {
            using (var fixture = new CoordinatorFixture(
                configureCard: false,
                axisCount: 0,
                stationCount: 1,
                includeEmergencyInput: false,
                includeBrakeOutput: false))
            {
                fixture.Device.SetStationState(0, MotionStationState.Disconnected);

                fixture.Coordinator.Initialize();

                Assert.IsTrue(fixture.Device.StationStatusRead.Wait(1000));
                fixture.Device.StationStatusRead.Reset();
                Assert.IsTrue(
                    fixture.Device.StationStatusRead.Wait(1000),
                    "Disconnected 工站应继续由后台巡检，为机器人重连保留机会。");
                string[] calls = fixture.Device.SnapshotCalls();
                Assert.IsFalse(fixture.Runtime.Safety.IsLocked);
                Assert.IsFalse(calls.Contains("StopAll:True"));
                Assert.IsFalse(calls.Contains("StopConnect"));
            }
        }

        private static void AssertBefore(string[] calls, string first, string second)
        {
            int firstIndex = RequireIndex(calls, first);
            int secondIndex = RequireIndex(calls, second);
            Assert.IsTrue(
                firstIndex < secondIndex,
                $"调用顺序错误：{first} 应早于 {second}。实际：{string.Join(" | ", calls)}");
        }

        private static int RequireIndex(string[] calls, string expected)
        {
            int index = Array.IndexOf(calls, expected);
            Assert.IsTrue(
                index >= 0,
                $"未找到调用 {expected}。实际：{string.Join(" | ", calls)}");
            return index;
        }

        private static int RequireIndexAfter(string[] calls, string expected, int startIndex)
        {
            for (int index = Math.Max(0, startIndex + 1); index < calls.Length; index++)
            {
                if (string.Equals(calls[index], expected, StringComparison.Ordinal))
                {
                    return index;
                }
            }
            Assert.Fail(
                $"在索引 {startIndex} 后未找到调用 {expected}。实际：{string.Join(" | ", calls)}");
            return -1;
        }

        private sealed class CoordinatorFixture : IDisposable
        {
            private readonly TemporaryDirectory directory;
            private readonly ProcessEngine engine;

            public CoordinatorFixture(
                bool configureCard,
                int axisCount,
                int stationCount,
                bool includeEmergencyInput,
                bool includeBrakeOutput)
            {
                directory = new TemporaryDirectory();
                Runtime = new PlatformRuntime(directory.FullPath);
                if (configureCard)
                {
                    var axes = new List<Axis>();
                    for (int axis = 0; axis < axisCount; axis++)
                    {
                        axes.Add(new Axis
                        {
                            AxisName = $"轴{axis}",
                            AxisNum = axis
                        });
                    }
                    Runtime.Stores.Cards.AddControlCard(new ControlCard
                    {
                        cardHead = new CardHead { AxisCount = axisCount },
                        axis = axes
                    });
                }

                var ioItems = new List<IO>();
                if (includeEmergencyInput)
                {
                    ioItems.Add(new IO
                    {
                        Index = 0,
                        CardNum = 0,
                        Module = 0,
                        IOIndex = "0",
                        Name = "急停输入",
                        IOType = "通用输入",
                        UsedType = "急停",
                        EffectLevel = "取反"
                    });
                }
                if (includeBrakeOutput)
                {
                    ioItems.Add(new IO
                    {
                        Index = 1,
                        CardNum = 0,
                        Module = 0,
                        IOIndex = "1",
                        Name = "刹车输出",
                        IOType = "通用输出",
                        UsedType = "刹车",
                        EffectLevel = "取反"
                    });
                }
                Assert.IsTrue(
                    Runtime.Stores.IoConfiguration.TryReplaceMap(
                        new[] { ioItems },
                        out string ioError),
                    ioError);

                Device = new RecordingDeviceRuntime(stationCount);
                Runtime.Motion = Device;
                Runtime.Io = Device;
                engine = new ProcessEngine(new EngineContext
                {
                    Procs = new List<Proc>(),
                    Motion = Device,
                    Io = Device,
                    CardStore = Runtime.Stores.Cards,
                    Stations = Runtime.Stores.Stations.Items,
                    ValueStore = Runtime.Stores.Values,
                    Maintenance = Runtime.Maintenance,
                    Paths = Runtime.Paths,
                    Safety = Runtime.Safety,
                    Readiness = Runtime.Readiness
                })
                {
                    Logger = new NoopLogger()
                };
                Runtime.ProcessEngine = engine;
                Coordinator = new PlatformDeviceCoordinator(Runtime);
            }

            public PlatformRuntime Runtime { get; }
            public RecordingDeviceRuntime Device { get; }
            public PlatformDeviceCoordinator Coordinator { get; }

            public void Dispose()
            {
                Coordinator.Dispose();
                engine.Dispose();
                Device.Dispose();
                directory.Dispose();
            }
        }

        private sealed class RecordingDeviceRuntime : IMotionRuntime, IIoRuntime, IDisposable
        {
            private sealed class EmptyLease : IDisposable
            {
                public void Dispose()
                {
                }
            }

            private readonly object callsLock = new object();
            private readonly object stationLock = new object();
            private readonly List<string> calls = new List<string>();
            private readonly Dictionary<short, MotionStationState> stationStates =
                new Dictionary<short, MotionStationState>();
            private readonly Dictionary<short, string> stationErrors =
                new Dictionary<short, string>();
            private readonly int stationCount;
            private int cardInitialized;
            private int servoEnabled = 1;
            private int emergencyActive;

            public RecordingDeviceRuntime(int stationCount)
            {
                this.stationCount = stationCount;
            }

            public bool IsCardInitialized => Volatile.Read(ref cardInitialized) == 1;
            public int StationCount => stationCount;
            public ManualResetEventSlim BrakeReleased { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim EmergencyActiveRead { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim StationStatusRead { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim CardClosed { get; } = new ManualResetEventSlim(false);

            public bool ServoEnabled
            {
                get => Volatile.Read(ref servoEnabled) == 1;
                set => Volatile.Write(ref servoEnabled, value ? 1 : 0);
            }

            public bool EmergencyActive
            {
                get => Volatile.Read(ref emergencyActive) == 1;
                set => Volatile.Write(ref emergencyActive, value ? 1 : 0);
            }

            public bool ActivateEmergencyOnStationInitialize { get; set; }
            public bool InitCardSucceeds { get; set; } = true;
            public Exception InitCardException { get; set; }
            public Exception DownLoadConfigException { get; set; }
            public MotionStationResult InitializeStationsResult { get; set; } =
                MotionStationResult.Success;
            public MotionStationResult StopAllStationsResult { get; set; } =
                MotionStationResult.Success;
            public MotionStationResult WaitStationMotionResult { get; set; } =
                MotionStationResult.Success;
            public MotionStationResult ReleaseStationsResult { get; set; } =
                MotionStationResult.Success;

            public void SetStationState(
                short station,
                MotionStationState state,
                string error = null)
            {
                lock (stationLock)
                {
                    stationStates[station] = state;
                    stationErrors[station] = error ?? string.Empty;
                }
            }

            public string[] SnapshotCalls()
            {
                lock (callsLock)
                {
                    return calls.ToArray();
                }
            }

            public void ClearCalls()
            {
                lock (callsLock)
                {
                    calls.Clear();
                }
            }

            public void InitCardType()
            {
                Record("InitCardType");
            }

            public bool InitCard()
            {
                Record("InitCard");
                if (InitCardException != null)
                {
                    throw InitCardException;
                }
                Volatile.Write(ref cardInitialized, InitCardSucceeds ? 1 : 0);
                return InitCardSucceeds;
            }

            public MotionStationResult InitializeStations()
            {
                Record("InitializeStations");
                if (ActivateEmergencyOnStationInitialize)
                {
                    EmergencyActive = true;
                }
                return InitializeStationsResult;
            }

            public MotionStationResult ReleaseStations()
            {
                Record("ReleaseStations");
                return ReleaseStationsResult;
            }

            public MotionStationStatus GetStationStatus(short station)
            {
                Record($"GetStationStatus:{station}");
                MotionStationState state;
                string error;
                lock (stationLock)
                {
                    state = stationStates.TryGetValue(station, out MotionStationState configured)
                        ? configured
                        : MotionStationState.Idle;
                    error = stationErrors.TryGetValue(station, out string configuredError)
                        ? configuredError
                        : string.Empty;
                }
                StationStatusRead.Set();
                return new MotionStationStatus
                {
                    State = state,
                    LastError = error
                };
            }

            public MotionStationResult SetStationSpeed(
                short station,
                double velocity,
                double acceleration,
                double deceleration,
                short axis = -1,
                StationSpeedType type = StationSpeedType.Joint)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult HomeStation(
                short station,
                short axis = -1,
                bool wait = true,
                bool group = false)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationToPoint(
                short station,
                DataPos point,
                StationMoveMode mode = StationMoveMode.Go,
                bool[] disabledAxes = null,
                short tool = 0)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationOffset(
                short station,
                int basePointIndex,
                IReadOnlyList<double> offsets,
                StationMoveMode mode = StationMoveMode.Go)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationAxis(
                short station,
                short axis,
                double offset,
                StationAxisMoveMode mode = StationAxisMoveMode.Relative,
                short tool = 0)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult WaitStationMotion(
                short station,
                bool isHome = false,
                int axis = -1,
                int timeoutMs = 120000)
            {
                Record($"WaitStationMotion:{station}");
                return WaitStationMotionResult;
            }

            public MotionStationResult GetStationPosition(
                short station,
                short tool,
                out DataPos position)
            {
                position = null;
                return MotionStationResult.Success;
            }

            public MotionStationResult SaveStationPoint(short station, DataPos point)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult CreateStationTray(
                short station,
                int trayId,
                int rowCount,
                int columnCount,
                IReadOnlyList<DataPos> referencePoints)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult MoveStationTrayPoint(
                short station,
                int trayId,
                int position,
                DataPos calculatedPoint)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult ClearStationContinuousPath(short station) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousLine(short station, DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousArc(short station, DataPos start, DataPos middle, DataPos target) => MotionStationResult.Success;
            public MotionStationResult AddStationContinuousArcCenterRadius(short station, DataPos target, DataPos center, double radius, int circle, bool counterClockwise) => MotionStationResult.Success;
            public MotionStationResult StartStationContinuousMove(short station) => MotionStationResult.Success;

            public MotionStationResult StopStation(short station, bool emergency = false)
            {
                return MotionStationResult.Success;
            }

            public MotionStationResult StopAllStations(bool emergency = false)
            {
                Record($"StopAll:{emergency}");
                return StopAllStationsResult;
            }

            public void SettHomeParam(
                ushort card,
                ushort axis,
                ushort dir,
                ushort speed,
                ushort homeMode)
            {
            }

            public void StartHome(ushort card, ushort axis)
            {
            }

            public void CleanPos(ushort card, ushort axis)
            {
            }

            public double GetAxisPos(ushort card, ushort axis)
            {
                Record($"GetAxisPos:{card}-{axis}");
                return 0;
            }

            public void SetMovParam(
                ushort card,
                ushort axis,
                double minVel,
                double maxVel,
                double acc,
                double dec,
                double stopVel,
                double sPara,
                int equiv)
            {
            }

            public void Mov(
                ushort card,
                ushort axis,
                double distance,
                ushort positionMode,
                bool wait)
            {
            }

            public void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request)
            {
            }

            public bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem)
            {
                return true;
            }

            public void StopCoordinatedLinear(
                ushort card,
                ushort coordinateSystem,
                ushort stopMode)
            {
            }

            public void MoveContinuousPath(ContinuousPathMoveRequest request) { }
            public bool IsContinuousPathDone(ushort card, ushort coordinateSystem) => true;
            public void StopContinuousPath(ushort card, ushort coordinateSystem, ushort stopMode) { }

            public void Jog(ushort card, ushort axis, ushort direction)
            {
            }

            public void StopOneAxis(ushort card, ushort axis, ushort stopMode)
            {
                Record($"StopAxis:{card}-{axis}:{stopMode}");
            }

            public void StopConnect()
            {
                Record("StopConnect");
                Volatile.Write(ref cardInitialized, 0);
                CardClosed.Set();
            }

            public bool HomeStatus(ushort card, ushort axis)
            {
                Record($"HomeStatus:{card}-{axis}");
                return true;
            }

            public bool GetInPos(ushort card, ushort axis)
            {
                Record($"GetInPos:{card}-{axis}");
                return true;
            }

            public bool GetAxisSevon(ushort card, ushort axis)
            {
                Record($"GetAxisServo:{card}-{axis}");
                return ServoEnabled;
            }

            public void SetAxisSevon(ushort card, ushort axis, bool isSevon)
            {
            }

            public void DownLoadConfig()
            {
                Record("DownLoadConfig");
                if (DownLoadConfigException != null)
                {
                    throw DownLoadConfigException;
                }
            }

            public void SetAllAxisSevonOn()
            {
                Record("SetAllAxisSevonOn");
            }

            public void SetAllAxisEquiv()
            {
                Record("SetAllAxisEquiv");
            }

            public void ResetAxisAlarm(ushort card, ushort axis)
            {
            }

            public double GetAxisCurSpeed(ushort card, ushort axis)
            {
                Record($"GetAxisSpeed:{card}-{axis}");
                return 0;
            }

            public uint GetAxisIoStatus(ushort card, ushort axis)
            {
                Record($"GetAxisIoStatus:{card}-{axis}");
                return 0;
            }

            public ushort GetAxisAlarmCode(ushort card, ushort axis)
            {
                return 0;
            }

            public IDisposable ValidateAxesForCommand(
                IReadOnlyCollection<AxisCommandRequest> requests)
            {
                return new EmptyLease();
            }

            public bool SetIO(IO io, bool isOpen)
            {
                Record($"SetIO:{io.Name}:{isOpen}:{io.EffectLevel}");
                if (string.Equals(io.Name, "刹车输出", StringComparison.Ordinal)
                    && isOpen)
                {
                    BrakeReleased.Set();
                }
                return true;
            }

            public bool SetOutputs(IReadOnlyList<IoOutputCommand> commands)
            {
                foreach (IoOutputCommand command in commands)
                {
                    if (!SetIO(command.Io, command.TargetState))
                    {
                        return false;
                    }
                }
                return true;
            }

            public bool GetOutIO(IO io, ref bool value)
            {
                value = false;
                return true;
            }

            public bool GetInIO(IO io, ref bool value)
            {
                value = EmergencyActive;
                Record($"GetInIO:{io.Name}:{value}:{io.EffectLevel}");
                if (value)
                {
                    EmergencyActiveRead.Set();
                }
                return true;
            }

            public void Dispose()
            {
                BrakeReleased.Dispose();
                EmergencyActiveRead.Dispose();
                StationStatusRead.Dispose();
                CardClosed.Dispose();
            }

            private void Record(string call)
            {
                lock (callsLock)
                {
                    calls.Add(call);
                }
            }
        }

        private sealed class NoopLogger : ILogger
        {
            public void Log(string message, LogLevel level)
            {
            }
        }
    }
}
