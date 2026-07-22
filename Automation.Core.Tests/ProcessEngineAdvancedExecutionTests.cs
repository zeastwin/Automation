using System;
// 模块：核心测试 / 流程高级执行。
// 职责范围：验证CT探针与受约束通信重试。

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Automation.Protocol;
using Newtonsoft.Json.Linq;
using static Automation.OperationTypePartial;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class ProcessEngineAdvancedExecutionTests
    {
        [ClassInitialize]
        public static void InitializeRuntimeConfiguration(TestContext context)
        {
            Assert.IsTrue(AppConfigStorage.TryLoad(out _, out string error), error);
        }

        [TestMethod]
        public void CycleTimeProbe_FastProcess_PublishesEveryExplicitProbeAndRetainsLatestSample()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateProcess("CT流程",
                    new CycleTimeProbe
                    {
                        Id = Guid.NewGuid(),
                        TaskKey = "上料周期",
                        SegmentName = "开始",
                        StartNewCycle = true
                    },
                    new Delay { Id = Guid.NewGuid(), DelayMs = 10 },
                    new CycleTimeProbe
                    {
                        Id = Guid.NewGuid(),
                        TaskKey = "上料周期",
                        SegmentName = "到位"
                    },
                    new EndProcess { Id = Guid.NewGuid() });
                var runtime = new PlatformRuntime(directory.FullPath);
                var samples = new ConcurrentQueue<CycleTimeProbeSample>();
                using (var engine = CreateEngine(runtime, process))
                {
                    engine.CycleTimeMeasured += sample => samples.Enqueue(sample);

                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForState(engine, 0, ProcRunState.Ready, TimeSpan.FromSeconds(3));
                    WaitForCount(samples, 2, TimeSpan.FromSeconds(2));

                    CycleTimeProbeSample[] actual = samples.ToArray();
                    Assert.AreEqual(2, actual.Length);
                    Assert.IsTrue(actual[0].CycleStarted);
                    Assert.AreEqual(0, actual[0].SegmentIndex);
                    Assert.AreEqual(1, actual[1].SegmentIndex);
                    Assert.AreEqual(actual[0].RunId, actual[1].RunId);
                    Assert.AreNotEqual(Guid.Empty, actual[1].RunId);
                    Assert.IsTrue(actual[1].SegmentMilliseconds > 0d);
                    Assert.AreEqual("到位", engine.GetLatestCycleTimeSamples(process.head.Id).Single().SegmentName);
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public async Task CommunicationRetry_ResponseMismatch_RetriesWithFixedIntervalAndCompletes()
        {
            using (var directory = new TemporaryDirectory())
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                try
                {
                    listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var socket = new SocketInfo
                {
                    ID = 1,
                    Name = "测试客户端",
                    Type = "Client",
                    LocalAddress = IPAddress.Loopback.ToString(),
                    LocalPort = 0,
                    RemoteAddress = IPAddress.Loopback.ToString(),
                    RemotePort = port,
                    AutoReconnect = false,
                    FrameMode = "Raw",
                    ConnectTimeoutMs = 1000
                };
                var request = new SendReceiveCommMsg
                {
                    Id = Guid.NewGuid(),
                    CommType = "TCP",
                    ConnectionName = socket.Name,
                    SendMsg = "请求",
                    ReceiveSaveValue = "响应",
                    TimeoutMs = 1000,
                    RetryCount = 1,
                    RetryIntervalMs = 20,
                    ResponseConditions = new CustomList<CommunicationResponseCondition>
                    {
                        new CommunicationResponseCondition
                        {
                            SourceVariableName = "响应",
                            JsonFieldPath = "data.code",
                            JudgeMode = "等于特征字符",
                            ExpectedText = "OK"
                        }
                    }
                };
                Proc process = CreateProcess("通信重试流程", request,
                    new EndProcess { Id = Guid.NewGuid() });
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(runtime.Stores.Communication.ReplaceSockets(
                    new[] { socket }, out string configError), configError);
                AddProcessVariable(runtime.Stores.Values, 10, "请求", "PING", process.head.Id, "string");
                AddProcessVariable(runtime.Stores.Values, 11, "响应", string.Empty, process.head.Id, "string");

                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
                await runtime.Communication.StartTcpAsync(socket);
                using (TcpClient serverClient = await acceptTask)
                {
                    Task responder = Task.Run(async () =>
                    {
                        byte[] buffer = new byte[64];
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            int count = await serverClient.GetStream().ReadAsync(buffer, 0, buffer.Length);
                            Assert.IsTrue(count > 0);
                            string response = attempt == 0
                                ? "{\"data\":{}}"
                                : "{\"data\":{\"code\":\"OK\"}}";
                            byte[] payload = Encoding.UTF8.GetBytes(response);
                            await serverClient.GetStream().WriteAsync(payload, 0, payload.Length);
                        }
                    });

                    ProcessRunAuditSnapshot completed = default(ProcessRunAuditSnapshot);
                    using (var engine = CreateEngine(runtime, process))
                    {
                    engine.ProcessCompleted += snapshot =>
                    {
                            if (snapshot.ProcId == process.head.Id) completed = snapshot;
                    };

                        Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForState(engine, 0, ProcRunState.Ready, TimeSpan.FromSeconds(3));
                        await responder;

                        Assert.AreEqual(ProcTerminationReason.Completed, completed.TerminationReason);
                        Assert.AreEqual(1L, completed.RetryCount);
                        Assert.AreEqual(0L, completed.FailedCount,
                        "中间尝试失败不应被记录为最终指令失败。");
                        Assert.AreEqual("{\"data\":{\"code\":\"OK\"}}",
                            runtime.Stores.Values.GetValueByNameForProcess(
                                "响应", process.head.Id).GetCValue());
                    }
                }
                    runtime.ShutdownCoordinator.Shutdown(
                        TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
                }
                finally
                {
                    listener.Stop();
                }
            }
        }

        [TestMethod]
        public async Task CommunicationRetry_NoResponse_RetriesThenReceivesNextMessage()
        {
            using (var directory = new TemporaryDirectory())
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                try
                {
                    listener.Start();
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    var socket = new SocketInfo
                    {
                        ID = 1,
                        Name = "接收重试客户端",
                        Type = "Client",
                        LocalAddress = IPAddress.Loopback.ToString(),
                        LocalPort = 0,
                        RemoteAddress = IPAddress.Loopback.ToString(),
                        RemotePort = port,
                        AutoReconnect = false,
                        FrameMode = "Raw",
                        ConnectTimeoutMs = 1000
                    };
                    var receive = new ReceiveTcpMsg
                    {
                        Id = Guid.NewGuid(),
                        ConnectionName = socket.Name,
                        MsgSaveValue = "响应",
                        TimeoutMs = 80,
                        RetryCount = 1,
                        RetryIntervalMs = 20
                    };
                    Proc process = CreateProcess("无回应重试流程", receive,
                        new EndProcess { Id = Guid.NewGuid() });
                    var runtime = new PlatformRuntime(directory.FullPath);
                    Assert.IsTrue(runtime.Stores.Communication.ReplaceSockets(
                        new[] { socket }, out string configError), configError);
                    AddProcessVariable(runtime.Stores.Values, 10, "响应", string.Empty,
                        process.head.Id, "string");

                    Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
                    await runtime.Communication.StartTcpAsync(socket);
                    using (TcpClient serverClient = await acceptTask)
                    {
                        Task responder = Task.Run(async () =>
                        {
                            await Task.Delay(130);
                            byte[] payload = Encoding.UTF8.GetBytes("READY");
                            await serverClient.GetStream().WriteAsync(payload, 0, payload.Length);
                        });
                        ProcessRunAuditSnapshot completed = default(ProcessRunAuditSnapshot);
                        using (var engine = CreateEngine(runtime, process))
                        {
                            engine.ProcessCompleted += snapshot =>
                            {
                                if (snapshot.ProcId == process.head.Id) completed = snapshot;
                            };
                            Assert.IsTrue(engine.StartProc(process, 0));
                            WaitForState(engine, 0, ProcRunState.Ready, TimeSpan.FromSeconds(3));
                            await responder;

                            Assert.AreEqual(ProcTerminationReason.Completed, completed.TerminationReason);
                            Assert.AreEqual(1L, completed.RetryCount);
                            Assert.AreEqual("READY", runtime.Stores.Values
                                .GetValueByNameForProcess("响应", process.head.Id).GetCValue());
                        }
                    }
                    runtime.ShutdownCoordinator.Shutdown(
                        TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
                }
                finally
                {
                    listener.Stop();
                }
            }
        }

        [TestMethod]
        public void CommunicationRetry_Zero_SkipsResponseConditionValidation()
        {
            using (var directory = new TemporaryDirectory())
            {
                var receive = new ReceiveTcpMsg
                {
                    Id = Guid.NewGuid(),
                    ConnectionName = "测试TCP",
                    MsgSaveValue = "响应",
                    TimeoutMs = 100,
                    RetryCount = 0,
                    ResponseConditions = new CustomList<CommunicationResponseCondition>
                    {
                        new CommunicationResponseCondition
                        {
                            SourceVariableName = "不存在变量",
                            JsonFieldPath = "data.code",
                            JudgeMode = "字段存在"
                        }
                    }
                };
                Proc process = CreateProcess("跳过判定流程", receive,
                    new EndProcess { Id = Guid.NewGuid() });
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(runtime.Stores.Communication.ReplaceSockets(new[]
                {
                    new SocketInfo
                    {
                        ID = 1,
                        Name = "测试TCP",
                        Type = "Client",
                        LocalAddress = IPAddress.Loopback.ToString(),
                        RemoteAddress = IPAddress.Loopback.ToString(),
                        RemotePort = 9000,
                        ConnectTimeoutMs = 100
                    }
                }, out string configError), configError);
                AddProcessVariable(runtime.Stores.Values, 10, "响应", string.Empty,
                    process.head.Id, "string");

                ProcessReadinessAnalysis withoutJudgment = ProcessReadinessService.Analyze(
                    0, process, new[] { process }, runtime.CreateProcessValidationContext(),
                    runtime.Stores.Values);
                Assert.IsTrue(withoutJudgment.Runnable,
                    string.Join("；", withoutJudgment.RunBlockers));

                receive.RetryCount = 1;
                ProcessReadinessAnalysis withJudgment = ProcessReadinessService.Analyze(
                    0, process, new[] { process }, runtime.CreateProcessValidationContext(),
                    runtime.Stores.Values);
                Assert.IsFalse(withJudgment.Runnable);
                Assert.IsTrue(withJudgment.RunBlockers.Any(item => item.Contains("不存在变量")));
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void ProcessCoordination_FastTarget_WaitingForReadyObservesNaturalCompletion()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc target = CreateProcess("快速目标流程",
                    new EndProcess { Id = Guid.NewGuid() });
                Proc waiter = CreateProcess("等待流程",
                    new WaitProc
                    {
                        Id = Guid.NewGuid(),
                        Timeout = new TimeoutSetting { TimeoutMs = 1000 },
                        Params = new CustomList<WaitProcParam>
                        {
                            new WaitProcParam
                            {
                                ProcName = target.head.Name,
                                TargetState = "就绪"
                            }
                        }
                    },
                    new EndProcess { Id = Guid.NewGuid() });
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, target, waiter))
                {
                    Assert.IsTrue(engine.StartProc(target, 0));
                    WaitForState(engine, 0, ProcRunState.Ready, TimeSpan.FromSeconds(3));
                    Assert.AreEqual(ProcTerminationReason.Completed,
                        engine.GetSnapshot(0).TerminationReason);

                    Assert.IsTrue(engine.StartProc(waiter, 1));
                    WaitForState(engine, 1, ProcRunState.Ready, TimeSpan.FromSeconds(3));
                    Assert.AreEqual(ProcTerminationReason.Completed,
                        engine.GetSnapshot(1).TerminationReason);
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void ProcessCoordination_StoppedTarget_DoesNotSatisfyReadyWait()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc target = CreateProcess("人工停止目标流程",
                    new Delay { Id = Guid.NewGuid(), DelayMs = 5000 },
                    new EndProcess { Id = Guid.NewGuid() });
                Proc waiter = CreateProcess("等待就绪流程",
                    new WaitProc
                    {
                        Id = Guid.NewGuid(),
                        Timeout = new TimeoutSetting { TimeoutMs = 100 },
                        Params = new CustomList<WaitProcParam>
                        {
                            new WaitProcParam
                            {
                                ProcName = target.head.Name,
                                TargetState = "就绪"
                            }
                        }
                    },
                    new EndProcess { Id = Guid.NewGuid() });
                var runtime = new PlatformRuntime(directory.FullPath);
                using (var engine = CreateEngine(runtime, target, waiter))
                {
                    Assert.IsTrue(engine.StartProc(target, 0));
                    WaitForState(engine, 0, ProcRunState.Running, TimeSpan.FromSeconds(3));
                    engine.Stop(0);
                    WaitForState(engine, 0, ProcRunState.Stopped, TimeSpan.FromSeconds(3));

                    Assert.IsTrue(engine.StartProc(waiter, 1));
                    WaitForState(engine, 1, ProcRunState.Alarming, TimeSpan.FromSeconds(3));
                    StringAssert.Contains(engine.GetSnapshot(1).AlarmMessage, "等待超时");
                    engine.Stop(1);
                    WaitForState(engine, 1, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
                runtime.ShutdownCoordinator.Shutdown(
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
            }
        }

        [TestMethod]
        public void NativeContracts_ExposeCommunicationOnlyRetryAndNoBackoffFields()
        {
            JObject probe = StructuredOperationCompiler.BuildContract("CT探针");
            JObject request = StructuredOperationCompiler.BuildContract("发送与接收");
            JObject send = StructuredOperationCompiler.BuildContract("发送TCP通讯消息");
            JObject wait = StructuredOperationCompiler.BuildContract("等待网口连接");
            JObject plc = StructuredOperationCompiler.BuildContract("PLC读写");

            Assert.IsFalse(OperationDefinitionRegistry.CreateAll()
                .Any(operation => operation.OperaType == "调用子流程"));
            Assert.IsNotNull(probe["fields"]?["TaskKey"]);
            Assert.IsNotNull(probe["fields"]?["StartNewCycle"]);
            Assert.IsNull(probe["fields"]?["RetryCount"]);
            Assert.IsNotNull(request["fields"]?["RetryCount"]);
            Assert.IsNotNull(request["fields"]?["RetryIntervalMs"]);
            Assert.IsNotNull(request["fields"]?["ResponseConditions"]);
            Assert.IsNull(request["fields"]?["BackoffFactor"]);
            Assert.AreEqual(true,
                request["behavior"]?["communicationRetry"]?["supported"]?.Value<bool>());
            Assert.IsNotNull(send["fields"]?["RetryCount"]);
            Assert.IsNull(send["fields"]?["ResponseConditions"]);
            Assert.IsNull(wait["fields"]?["RetryCount"]);
            Assert.IsNotNull(plc["fields"]?["RetryCount"]);
            Assert.IsNotNull(plc["fields"]?["ResponseConditions"]);
        }

        private static ProcessEngine CreateEngine(PlatformRuntime runtime, params Proc[] processes)
        {
            return new ProcessEngine(new EngineContext
            {
                Procs = processes.ToList(),
                ValueStore = runtime.Stores.Values,
                Comm = runtime.Communication,
                CommunicationStore = runtime.Stores.Communication,
                Maintenance = runtime.Maintenance,
                Safety = runtime.Safety,
                Readiness = runtime.Readiness,
                Paths = runtime.Paths,
                ValidationContextFactory = runtime.CreateProcessValidationContext
            });
        }

        private static Proc CreateProcess(string name, params OperationType[] operations)
        {
            var process = new Proc { head = new ProcHead { Name = name } };
            var step = new Step { Id = Guid.NewGuid(), Name = "执行" };
            step.Ops.AddRange(operations);
            process.steps.Add(step);
            return process;
        }

        private static void AddProcessVariable(
            ValueConfigStore store, int index, string name, string value, Guid ownerProcId,
            string type = "double")
        {
            Assert.IsTrue(store.TrySetValue(index, name, type, value, string.Empty,
                "高级执行测试", VariableScopeContract.Process, ownerProcId));
        }

        private static void WaitForState(
            ProcessEngine engine, int procIndex, ProcRunState state, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.GetSnapshot(procIndex)?.State == state) return;
                Thread.Sleep(10);
            }
            Assert.Fail($"等待流程{procIndex}状态超时：{state}。");
        }

        private static void WaitForCount<T>(
            ConcurrentQueue<T> queue, int count, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (queue.Count >= count) return;
                Thread.Sleep(10);
            }
            Assert.Fail($"等待事件数量超时：{count}；当前：{queue.Count}。");
        }
    }
}
