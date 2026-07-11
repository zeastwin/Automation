using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using static Automation.FrmCard;
using Automation.MotionControl;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace Automation.KernelTests
{
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            Run("未复位禁止启动", TestResetGate);
            Run("报警忽略后继续执行", TestAlarmIgnoreContinues);
            Run("报警状态与文本保持一致", TestAlarmStateConsistency);
            Run("单条指令只执行一次", TestSingleOperationStopsExactlyOnce);
            Run("暂停过渡状态保持一致", TestPauseTransitionConsistency);
            Run("停止超时拒绝重启", TestStopTimeoutRejectsRestart);
            Run("手动轴资源禁止重复占用", TestManualMotionResourceExclusion);
            Run("轴与工站静态配置校验", TestMotionConfigurationValidation);
            Run("轴状态快照并发与过期保护", TestAxisStatusCache);
            Run("通讯帧拆包与有界缓存", TestCommunicationReceiveDispatcher);
            Run("通讯配置独立存储与严格校验", TestCommunicationConfigStore);
            Run("TCP请求响应事务与生命周期", TestTcpRequestResponse);
            Run("PLC连续地址批量读取合并", TestPlcBatchRead);
            Run("报警配置严格校验与原子保存", TestAlarmInfoStore);
            Console.WriteLine(failures == 0 ? "内核回归测试全部通过。" : $"内核回归测试失败:{failures}");
            return failures == 0 ? 0 : 1;
        }

        private static void TestResetGate()
        {
            ValueConfigStore values = CreateValueStore(ResetStatus.NotReset);
            Proc proc = CreateProc(new CallCustomFunc { Name = "noop" });
            CustomFunc functions = new CustomFunc();
            functions.RegisterFunction("noop", () => { });
            using (ProcessEngine engine = CreateEngine(values, functions, proc))
            {
                Assert(!engine.StartProc(proc, 0), "未复位时流程启动未被拒绝");
                Assert(values.setValueByName("复位状态", (double)ResetStatus.ResetCompleted), "设置复位状态失败");
                Assert(engine.StartProc(proc, 0), "复位完成后流程启动失败");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 2000, "流程未正常结束");
            }
        }

        private static void TestAlarmIgnoreContinues()
        {
            int continued = 0;
            ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
            Proc proc = CreateProc(
                new CallCustomFunc { Name = "throws", AlarmType = "报警忽略" },
                new CallCustomFunc { Name = "after" });
            CustomFunc functions = new CustomFunc();
            functions.RegisterFunction("throws", () => throw new InvalidOperationException("模拟指令异常"));
            functions.RegisterFunction("after", () => Interlocked.Increment(ref continued));
            using (ProcessEngine engine = CreateEngine(values, functions, proc))
            {
                Assert(engine.StartProc(proc, 0), "报警恢复测试流程启动失败");
                WaitUntil(() => Volatile.Read(ref continued) == 1, 2000, "报警忽略后未继续执行下一条指令");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 2000, "报警恢复测试流程未结束");
                Assert(engine.GetSnapshot(0)?.IsAlarm == false, "忽略后报警标志未清除");
            }
        }

        private static void TestAlarmStateConsistency()
        {
            ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
            Proc proc = CreateProc(new CallCustomFunc { Name = "throws", AlarmType = "报警停止" });
            CustomFunc functions = new CustomFunc();
            functions.RegisterFunction("throws", () => throw new InvalidOperationException("状态一致性报警"));
            using (ProcessEngine engine = CreateEngine(values, functions, proc))
            {
                Assert(engine.StartProc(proc, 0), "报警一致性测试流程启动失败");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Alarming, 2000, "流程未进入报警状态");
                EngineSnapshot alarming = engine.GetSnapshot(0);
                Assert(alarming.IsAlarm, "存在报警文本但报警状态为否");
                Assert(alarming.AlarmMessage == "状态一致性报警", "报警文本未正确发布");
                engine.Stop(0);
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 2000, "报警流程停止失败");
                EngineSnapshot stopped = engine.GetSnapshot(0);
                Assert(stopped.IsAlarm && stopped.AlarmMessage == "状态一致性报警", "停止后报警结果丢失");
            }
        }

        private static void TestSingleOperationStopsExactlyOnce()
        {
            int firstCount = 0;
            int secondCount = 0;
            ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
            Proc proc = CreateProc(
                new CallCustomFunc { Name = "first" },
                new CallCustomFunc { Name = "second" });
            CustomFunc functions = new CustomFunc();
            functions.RegisterFunction("first", () => Interlocked.Increment(ref firstCount));
            functions.RegisterFunction("second", () => Interlocked.Increment(ref secondCount));
            using (ProcessEngine engine = CreateEngine(values, functions, proc))
            {
                Assert(engine.RunSingleOpOnce(proc, 0, 0, 0), "单条指令启动失败");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 2000, "单条指令未停止");
                Assert(Volatile.Read(ref firstCount) == 1, "目标指令执行次数错误");
                Assert(Volatile.Read(ref secondCount) == 0, "单条模式错误执行了后续指令");
            }
        }

        private static void TestPauseTransitionConsistency()
        {
            int secondCount = 0;
            using (ManualResetEventSlim firstEntered = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseFirst = new ManualResetEventSlim(false))
            {
                ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
                Proc proc = CreateProc(
                    new CallCustomFunc { Name = "first" },
                    new CallCustomFunc { Name = "second" });
                CustomFunc functions = new CustomFunc();
                functions.RegisterFunction("first", () =>
                {
                    firstEntered.Set();
                    releaseFirst.Wait(2000);
                });
                functions.RegisterFunction("second", () => Interlocked.Increment(ref secondCount));
                using (ProcessEngine engine = CreateEngine(values, functions, proc))
                {
                    Assert(engine.StartProc(proc, 0), "暂停测试流程启动失败");
                    Assert(firstEntered.Wait(1000), "首条指令未开始执行");
                    engine.Pause(0);
                    WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Pausing, 1000, "流程未进入暂停过渡态");
                    releaseFirst.Set();
                    WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Paused, 1000, "流程未确认暂停");
                    Assert(Volatile.Read(ref secondCount) == 0, "暂停期间错误执行了下一条指令");
                    engine.Resume(0);
                    WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 2000, "恢复后流程未结束");
                    Assert(Volatile.Read(ref secondCount) == 1, "恢复后下一条指令未执行");
                }
            }
        }

        private static void TestStopTimeoutRejectsRestart()
        {
            int invocationCount = 0;
            using (ManualResetEventSlim entered = new ManualResetEventSlim(false))
            {
                ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
                Proc proc = CreateProc(new CallCustomFunc { Name = "blocking" });
                CustomFunc functions = new CustomFunc();
                functions.RegisterFunction("blocking", () =>
                {
                    Interlocked.Increment(ref invocationCount);
                    entered.Set();
                    Thread.Sleep(3500);
                });
                using (ProcessEngine engine = CreateEngine(values, functions, proc))
                {
                    Assert(engine.StartProc(proc, 0), "阻塞流程首次启动失败");
                    Assert(entered.Wait(1000), "阻塞指令未进入执行");
                    engine.Stop(0);
                    Assert(engine.GetSnapshot(0)?.State == ProcRunState.Stopping, "停止请求后未进入Stopping状态");
                    Assert(!engine.StartProc(proc, 0), "旧线程未退出时错误地允许了重启");
                    Assert(Volatile.Read(ref invocationCount) == 1, "检测到同流程工作线程重叠");
                    WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 3000, "阻塞流程最终未停止");
                }
            }
        }

        private static void TestCommunicationReceiveDispatcher()
        {
            var dispatcher = new ReceiveDispatcher(2);
            dispatcher.Publish(new ReceivedFrame(new byte[] { 1 }, "A"));
            dispatcher.Publish(new ReceivedFrame(new byte[] { 2 }, "B"));
            dispatcher.Publish(new ReceivedFrame(new byte[] { 3 }, "C"));
            Assert(dispatcher.DroppedFrames == 1, "接收队列溢出未记录丢帧计数");
            ReceivedFrame first = dispatcher.WaitNextAsync(1000, CancellationToken.None).GetAwaiter().GetResult();
            ReceivedFrame second = dispatcher.WaitNextAsync(1000, CancellationToken.None).GetAwaiter().GetResult();
            Assert(first.Payload[0] == 2 && second.Payload[0] == 3, "有界缓存未按预期丢弃最旧帧");

            Task<ReceivedFrame> waiter = dispatcher.WaitNextAsync(1000, CancellationToken.None);
            dispatcher.Publish(new ReceivedFrame(new byte[] { 4 }, "D"));
            Assert(waiter.GetAwaiter().GetResult().Payload[0] == 4, "等待者未收到后续帧");

            var settings = new CommFrameSettings(CommFrameMode.Delimiter, new byte[] { (byte)'\r', (byte)'\n' }, Encoding.UTF8);
            var decoder = new FrameDecoder(settings, 1024);
            Assert(decoder.Push(Encoding.UTF8.GetBytes("AB\r"), 3).Count == 0, "半包被错误地解析为完整帧");
            byte[] remaining = Encoding.UTF8.GetBytes("\nCD\r\n");
            IList<byte[]> frames = decoder.Push(remaining, remaining.Length);
            Assert(frames.Count == 2
                && Encoding.UTF8.GetString(frames[0]) == "AB"
                && Encoding.UTF8.GetString(frames[1]) == "CD", "粘包或拆包解析错误");
        }

        private static void TestCommunicationConfigStore()
        {
            var store = new CommunicationConfigStore();
            var sockets = new[]
            {
                new SocketInfo
                {
                    ID = 1,
                    Name = "MES",
                    Type = "Client",
                    Address = "127.0.0.1",
                    Port = 9000,
                    FrameMode = "Delimiter",
                    FrameDelimiter = "\\r\\n",
                    EncodingName = "UTF-8",
                    ConnectTimeoutMs = 1500
                }
            };
            Assert(store.ReplaceSockets(sockets, out _), "有效TCP配置未通过校验");
            Assert(store.TryGetSocket("mes", out SocketInfo snapshot) && snapshot.ConnectTimeoutMs == 1500,
                "配置Store未按名称返回独立快照");
            snapshot.Port = 1;
            Assert(store.TryGetSocket("MES", out SocketInfo unchanged) && unchanged.Port == 9000,
                "外部修改污染了配置Store");

            var duplicate = new[] { sockets[0], new SocketInfo
            {
                ID = 2,
                Name = "mes",
                Type = "Server",
                Address = "0.0.0.0",
                Port = 9001,
                FrameMode = "Raw",
                EncodingName = "UTF-8",
                ConnectTimeoutMs = 1000
            }};
            Assert(!store.ReplaceSockets(duplicate, out _), "重复名称错误地通过通讯配置校验");

            string tempPath = Path.Combine(Path.GetTempPath(), "AutomationCommStore_" + Guid.NewGuid().ToString("N"));
            try
            {
                store.Save(tempPath);
                var reloaded = new CommunicationConfigStore();
                Assert(reloaded.Load(tempPath, out string loadError), "通讯配置重新加载失败:" + loadError);
                Assert(reloaded.TryGetSocket("MES", out SocketInfo loaded) && loaded.Port == 9000,
                    "通讯配置持久化结果不一致");
            }
            finally
            {
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
            }
        }

        private static void TestTcpRequestResponse()
        {
            Assert(AppConfigStorage.TrySave(new AppConfig
            {
                CommMaxMessageQueueSize = 32,
                RuntimeMode = AutomationRuntimeMode.Hardware
            }, out string configError), "初始化通讯测试参数失败:" + configError);

            int port;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            using (var hub = new CommunicationHub())
            using (var client = new TcpClient())
            {
                hub.StartTcpAsync(new SocketInfo
                {
                    ID = 1,
                    Name = "Loopback",
                    Type = "Server",
                    Address = "127.0.0.1",
                    Port = port,
                    FrameMode = "Delimiter",
                    FrameDelimiter = "\\n",
                    EncodingName = "UTF-8",
                    ConnectTimeoutMs = 1000
                }).GetAwaiter().GetResult();

                client.Connect(IPAddress.Loopback, port);
                WaitUntil(() => hub.GetTcpStatus("Loopback").ClientCount == 1, 2000, "TCP测试客户端未接入");
                Task responder = Task.Run(() =>
                {
                    NetworkStream stream = client.GetStream();
                    var received = new List<byte>();
                    while (true)
                    {
                        int value = stream.ReadByte();
                        if (value < 0 || value == '\n') break;
                        received.Add((byte)value);
                    }
                    Assert(Encoding.UTF8.GetString(received.ToArray()) == "PING", "TCP请求内容不正确");
                    byte[] response = Encoding.UTF8.GetBytes("PONG\n");
                    stream.Write(response, 0, response.Length);
                });

                CommReceiveResult result = hub.SendReceiveTcpAsync("Loopback", "PING", false, 2000)
                    .GetAwaiter().GetResult();
                Assert(result.Success && result.MessageText == "PONG", "TCP请求响应事务失败:" + result.ErrorMessage);
                Assert(responder.Wait(2000), "TCP测试响应线程未结束");
                using (IDisposable transaction = hub.EnterTcpTransactionAsync("Loopback", CancellationToken.None)
                    .GetAwaiter().GetResult())
                {
                    Stopwatch timeoutWatch = Stopwatch.StartNew();
                    CommReceiveResult queuedResult = hub.ReceiveTcpAsync("Loopback", 100).GetAwaiter().GetResult();
                    Assert(!queuedResult.Success && queuedResult.ErrorMessage.Contains("超时")
                        && timeoutWatch.ElapsedMilliseconds < 500, "通讯事务排队时间未计入超时");
                }
                hub.StopTcpAsync("Loopback").GetAwaiter().GetResult();
                Assert(!hub.GetTcpStatus("Loopback").IsRunning, "TCP停止后状态仍为运行");
            }
        }

        private static void TestManualMotionResourceExclusion()
        {
            ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
            Proc proc = CreateProc(new CallCustomFunc { Name = "noop" });
            using (ProcessEngine engine = CreateEngine(values, new CustomFunc(), proc))
            {
                Assert(engine.TryAcquireManualMotionResource(0, 1, out _), "首次占用手动轴资源失败");
                Assert(!engine.TryAcquireManualMotionResource(0, 1, out _), "同一手动轴资源被重复占用");
                engine.ReleaseManualMotionResource(0, 1);
                Assert(engine.TryAcquireManualMotionResource(0, 1, out _), "释放后无法重新占用手动轴资源");
                engine.ReleaseManualMotionResource(0, 1);

                var requests = new[]
                {
                    new AxisCommandRequest(0, 1, AxisCommandKind.Motion),
                    new AxisCommandRequest(0, 2, AxisCommandKind.Motion)
                };
                Assert(engine.TryReserveManualMotionResources(requests, out IDisposable lease, out _),
                    "多轴手动资源原子预留失败");
                using (lease)
                {
                    Assert(engine.TryAcquireManualMotionResource(0, 1, out _), "预留的第一个轴无法消费");
                    Assert(engine.TryAcquireManualMotionResource(0, 2, out _), "预留的第二个轴无法消费");
                    Assert(!engine.TryAcquireManualMotionResource(0, 1, out _), "已消费的预留轴被重复占用");
                }
                engine.ReleaseManualMotionResource(0, 1);
                engine.ReleaseManualMotionResource(0, 2);

                Assert(engine.TryAcquireManualMotionResource(0, 1, out _), "冲突测试轴占用失败");
                Assert(!engine.TryReserveManualMotionResources(requests, out IDisposable conflictLease, out _),
                    "存在冲突时错误地完成了多轴预留");
                conflictLease?.Dispose();
                Assert(engine.TryAcquireManualMotionResource(0, 2, out _), "多轴预留失败后遗留了部分占用");
                engine.ReleaseManualMotionResource(0, 1);
                engine.ReleaseManualMotionResource(0, 2);
            }
        }

        private static void TestMotionConfigurationValidation()
        {
            Axis axis = new Axis
            {
                AxisName = "Axis0",
                AxisNum = 0,
                PulseToMM = 1000,
                HomeType = "从负限位回零",
                HomeSpeed = "10",
                LimitSpeed = "20",
                SpeedMax = 100,
                AccMax = 0.2,
                DecMax = 0.2
            };
            CardConfigStore store = new CardConfigStore();
            store.SetCard(new Card
            {
                controlCards = new List<ControlCard>
                {
                    new ControlCard
                    {
                        cardHead = new CardHead { AxisCount = 1 },
                        axis = new List<Axis> { axis }
                    }
                }
            });
            Assert(store.TryValidateAllAxes(out _), "有效轴配置被错误拒绝");
            axis.PulseToMM = 0;
            Assert(!store.TryValidateAllAxes(out _), "无效脉冲当量未被拒绝");
            axis.PulseToMM = 1000;

            DataStation station = new DataStation(false) { Name = "Station0" };
            SF.cardStore = store;
            SF.isModify = ModifyKind.Station;
            station.dataAxis.axisConfig1.CardNum = "0";
            station.dataAxis.axisConfig1.AxisName = "Axis0";
            SF.isModify = ModifyKind.None;
            Assert(store.TryValidateStations(new[] { station }, out _), "有效工站配置被错误拒绝");
            SF.isModify = ModifyKind.Station;
            station.dataAxis.axisConfig2.CardNum = "0";
            station.dataAxis.axisConfig2.AxisName = "Axis0";
            SF.isModify = ModifyKind.None;
            Assert(!store.TryValidateStations(new[] { station }, out _), "工站重复物理轴未被拒绝");
        }

        private static void TestAxisStatusCache()
        {
            AxisStatusCache cache = new AxisStatusCache();
            cache.UpdateIo(0, 1, 1u << 2);
            cache.UpdateDetails(0, 1, true, true, true, 12.5, 3.2, 0);
            AxisStatusSnapshot snapshot = cache.GetRequired(0, 1);
            Assert(snapshot.NegativeLimit, "负限位快照解析错误");
            Assert(snapshot.IsStopped && snapshot.IsHomed && snapshot.ServoOn, "轴详细状态快照错误");

            Parallel.For(0, 1000, index =>
            {
                cache.UpdateIo(0, 1, (uint)(index & 1));
                cache.TryGet(0, 1, out _);
            });
            Assert(cache.TryGet(0, 1, out snapshot), "并发更新后轴快照丢失");

            cache.UpdateIo(0, 2, 0);
            Thread.Sleep(20);
            bool expiredRejected = false;
            try
            {
                cache.GetRequiredSignal(0, 2, 1, 1);
            }
            catch (InvalidOperationException)
            {
                expiredRejected = true;
            }
            Assert(expiredRejected, "过期轴IO快照未被拒绝");

            AxisMotionParameterStore parameters = new AxisMotionParameterStore();
            Assert(parameters.Get(0, 1).SpeedPercent == 100, "轴运行参数默认值错误");
            parameters.Set(0, 1, 50, 60, 70);
            Assert(parameters.Get(0, 1).AccelerationPercent == 60, "轴运行参数存储错误");
        }

        private static void TestAlarmInfoStore()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "AutomationAlarmStore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPath);
            try
            {
                var store = new AlarmInfoStore();
                store.UpdateAlarm(8, "气压不足", "设备", "确认", null, null, "请检查主气路压力");
                store.Save(tempPath);
                string json = File.ReadAllText(Path.Combine(tempPath, "AlarmInfo.json"));
                Assert(json.IndexOf("$type", StringComparison.OrdinalIgnoreCase) < 0, "报警配置不应包含类型元数据");

                var loaded = new AlarmInfoStore();
                Assert(loaded.Load(tempPath), "报警配置重新加载失败");
                Assert(loaded.TryGetByIndex(8, out AlarmInfo alarm) && alarm.Name == "气压不足",
                    "报警配置保存后内容不一致");

                bool rejected = false;
                try
                {
                    loaded.UpdateAlarm(9, "名称存在", null, null, null, null, null);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                Assert(rejected, "名称与报警信息不完整时未拒绝保存");
            }
            finally
            {
                Directory.Delete(tempPath, true);
            }
        }

        private static void TestPlcBatchRead()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            int requestCount = 0;
            int requestedRegisters = 0;
            Task serverTask = Task.Run(() =>
            {
                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] header = ReadExact(stream, 7);
                    int remaining = (header[4] << 8 | header[5]) - 1;
                    byte[] pdu = ReadExact(stream, remaining);
                    Interlocked.Increment(ref requestCount);
                    if (pdu.Length < 5 || pdu[0] != 3)
                    {
                        throw new InvalidOperationException("PLC批量测试收到非保持寄存器读取请求");
                    }
                    requestedRegisters = pdu[3] << 8 | pdu[4];
                    int byteCount = requestedRegisters * 2;
                    byte[] response = new byte[9 + byteCount];
                    response[0] = header[0];
                    response[1] = header[1];
                    response[4] = (byte)((3 + byteCount) >> 8);
                    response[5] = (byte)(3 + byteCount);
                    response[6] = header[6];
                    response[7] = 3;
                    response[8] = (byte)byteCount;
                    stream.Write(response, 0, response.Length);
                }
            });

            try
            {
                var device = new PlcDevice
                {
                    Name = "BatchPlc",
                    Protocol = "ModbusTcp",
                    Ip = "127.0.0.1",
                    Port = port,
                    UnitId = 1,
                    TimeoutMs = 2000
                };
                var maps = new List<PlcMapItem>
                {
                    new PlcMapItem { PlcName = device.Name, Direction = "读PLC", DataType = "Float", PlcAddress = "HR:0", Quantity = 1, ValueName = "V1" },
                    new PlcMapItem { PlcName = device.Name, Direction = "读PLC", DataType = "Float", PlcAddress = "HR:2", Quantity = 1, ValueName = "V2" },
                    new PlcMapItem { PlcName = device.Name, Direction = "读PLC", DataType = "UShort", PlcAddress = "HR:10", Quantity = 1, ValueName = "V3" }
                };
                using (var hub = new PlcHub())
                {
                    Assert(hub.TryReadBatch(device, maps, out IReadOnlyList<PlcBatchReadResult> results, out string error),
                        "PLC批量读取失败:" + error);
                    Assert(results.Count == 3, "PLC批量读取结果数量错误");
                }
                Assert(serverTask.Wait(3000), "PLC批量读取测试服务未结束");
                Assert(requestCount == 1, "连续地址未合并为单个Modbus请求");
                Assert(requestedRegisters == 11, "Modbus合并读取区间错误");
            }
            finally
            {
                listener.Stop();
            }
        }

        private static byte[] ReadExact(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException("测试连接提前关闭");
                }
                offset += read;
            }
            return buffer;
        }

        private static ProcessEngine CreateEngine(ValueConfigStore values, CustomFunc functions, Proc proc)
        {
            return new ProcessEngine(new EngineContext
            {
                Procs = new List<Proc> { proc },
                ValueStore = values,
                CustomFunc = functions
            })
            {
                SnapshotThrottleMilliseconds = 0
            };
        }

        private static ValueConfigStore CreateValueStore(ResetStatus status)
        {
            ValueConfigStore values = new ValueConfigStore();
            Assert(values.TrySetValue(0, "复位状态", "double", ((double)status).ToString(), "测试变量"), "创建复位状态失败");
            return values;
        }

        private static Proc CreateProc(params OperationType[] operations)
        {
            return new Proc
            {
                head = new ProcHead { Name = "内核测试流程" },
                steps = new List<Step>
                {
                    new Step { Id = Guid.NewGuid(), Name = "测试步骤", Ops = new List<OperationType>(operations) }
                }
            };
        }

        private static void WaitUntil(Func<bool> predicate, int timeoutMs, string message)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate())
                {
                    return;
                }
                Thread.Sleep(10);
            }
            throw new InvalidOperationException(message);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"通过: {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"失败: {name} - {ex}");
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
