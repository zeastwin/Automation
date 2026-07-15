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
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using Automation.Protocol;
using static Automation.OperationTypePartial;

namespace Automation.KernelTests
{
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            Run("未复位允许流程但禁止运动", TestResetGate);
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
            Run("PLC配置与地址契约严格校验", TestPlcConfigurationValidation);
            Run("PLC HSL授权与Modbus TCP实报文", TestPlcHslTransport);
            Run("PLC映射调度冲突与设备故障隔离", TestPlcRuntimeMappingStateMachine);
            Run("PLC管理窗体可独立显示", TestPlcManagementFormCanShow);
            Run("报警配置严格校验与原子保存", TestAlarmInfoStore);
            Run("运行中参数与后续指令安全热更新", TestRunningSafeHotUpdate);
            Run("运行中删除后续指令安全结束", TestRunningFutureOperationDeletion);
            Run("运行中当前结构修改被拒绝", TestRunningStructuralUpdateRejected);
            Run("暂停状态删除当前指令被拒绝", TestPausedCurrentOperationDeletionRejected);
            Run("编辑会话提交与取消隔离", TestEditSession);
            Run("流程结构变化按指令ID重写跳转", TestGotoRewriteByOperationId);
            Run("删除流程后重排自身索引与跳转地址", TestGotoProcIndexRebuildAfterDeletion);
            Run("逻辑判断跳转地址严格校验", TestParamGotoStrictValidation);
            Run("指令行为契约与跳转标记一致", TestOperationBehaviorContracts);
            Run("JSON原子替换与备份恢复", TestAtomicJsonRecovery);
            Run("流程目录事务中断恢复", TestProcessDirectoryRecovery);
            Run("流程目录并发事务串行化", TestConcurrentProcessDirectoryRebuild);
            Run("多文件配置批量提交", TestConfigurationBatchWriter);
            Run("数据结构索引与字段类型严格校验", TestDataStructStrictIndexes);
            Run("指令注册表无界面且定义唯一", TestOperationDefinitionRegistry);
            Run("Goose平台上下文资源完整嵌入", TestGooseEmbeddedResources);
            Run("AI语义变更集编译与符号跳转", TestAiChangeSetCompilation);
            Run("AI完整变更与精确重建", TestAiChangeSetCompleteCompilation);
            Run("全部原生指令递归契约与严格语义编译", TestStructuredNativeOperationCompilation);
            Run("AI语义完整替换现有流程", TestAiChangeSetReplaceProcess);
            Run("AI原子动作分阶段与稳定插入", TestAiAtomicChangeActions);
            Run("AI未完成流程保存与运行闸门", TestAiDraftProcessReadiness);
            Run("流程终止原因可追溯", TestTerminationReason);
            Run("AI联合配置事务与中断恢复", TestAiConfigurationTransaction);
            Run("AI回复粘连Markdown归一化", TestAiMarkdownNormalization);
            Run("AI历史会话选择器布局契约", TestAiSessionPickerTemplate);
            Run("Goose开发Shell可用性兜底", TestGooseDeveloperShellFallback);
            Run("流程详情体积闸门与批量读取边界", TestProcDetailReadBoundaries);
            Run("嵌套资源与跳转引用递归索引", TestNestedReferenceIndexing);
            Console.WriteLine(failures == 0 ? "内核回归测试全部通过。" : $"内核回归测试失败:{failures}");
            return failures == 0 ? 0 : 1;
        }

        private static void TestResetGate()
        {
            int invocationCount = 0;
            ValueConfigStore values = CreateValueStore(ResetStatus.NotReset);
            Proc proc = CreateProc(new CallCustomFunc { Name = "noop" });
            CustomFunc functions = new CustomFunc();
            functions.RegisterFunction("noop", () => Interlocked.Increment(ref invocationCount));
            using (ProcessEngine engine = CreateEngine(values, functions, proc))
            {
                Assert(engine.StartProc(proc, 0), "未复位时普通流程未能启动");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 2000, "流程未正常结束");
                Assert(Volatile.Read(ref invocationCount) == 1, "未复位时普通流程未执行");
            }

            Proc motionProc = CreateProc(new StationRunPos
            {
                Name = "未复位运动",
                StationName = "测试工站",
                AlarmType = "报警停止"
            });
            using (ProcessEngine engine = CreateEngine(values, new CustomFunc(), motionProc))
            {
                Assert(engine.StartProc(motionProc, 0), "未复位时包含运动指令的流程未能启动");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Alarming, 2000,
                    "未复位运动指令未触发报警");
                Assert(engine.GetSnapshot(0)?.AlarmMessage == "系统尚未复位完成，禁止轴和工站运动。",
                    "未复位运动门禁报警不明确");
                engine.Stop(0);
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
                HomeDirection = "负向",
                HomeSpeed = "10",
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
            Assert(!cache.GetRequired(0, 2).IsIoFresh(1), "过期轴IO快照未被识别");

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

        private static void TestPlcConfigurationValidation()
        {
            ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
            Assert(values.TrySetValue(1, "温度", "double", "0", "PLC测试"), "创建温度变量失败");
            Assert(values.TrySetValue(2, "状态文本", "string", string.Empty, "PLC测试"), "创建字符串变量失败");
            Assert(values.TrySetValue(3, "写入值", "double", "1", "PLC测试"), "创建写入变量失败");
            var device = PlcDeviceConfig.Create(PlcDeviceProfile.InovanceModbusTcp);
            device.Name = "测试PLC";
            device.IpAddress = "127.0.0.1";
            device.Mappings.Add(new PlcMapConfig
            {
                Name = "读取温度",
                Area = PlcArea.HoldingRegister,
                StartAddress = 0,
                DataType = PlcDataType.Float,
                Direction = PlcMapDirection.ReadFromPlc,
                ElementCount = 1,
                VariableNames = new List<string> { "温度" }
            });
            device.Mappings.Add(new PlcMapConfig
            {
                Name = "读取文本",
                Area = PlcArea.HoldingRegister,
                StartAddress = 10,
                DataType = PlcDataType.String,
                Direction = PlcMapDirection.ReadFromPlc,
                ElementCount = 1,
                StringByteLength = 16,
                VariableNames = new List<string> { "状态文本" }
            });
            device.Mappings.Add(new PlcMapConfig
            {
                Name = "写入参数",
                Area = PlcArea.HoldingRegister,
                StartAddress = 30,
                DataType = PlcDataType.UShort,
                Direction = PlcMapDirection.WriteToPlc,
                ElementCount = 1,
                VariableNames = new List<string> { "写入值" }
            });
            var configuration = new PlcConfiguration { Devices = new List<PlcDeviceConfig> { device } };
            Assert(PlcConfigStore.Validate(configuration, values, out string error), "有效PLC配置被拒绝:" + error);
            Assert(device.IsStringReverse, "汇川设备默认未启用字符串反转");
            Assert(device.DataFormat == "CDAB", "新建PLC默认字节序不是CDAB");
            Assert(device.AutoConnect, "新建PLC没有保持默认自动连接行为");
            Assert(PlcConfigStore.GetAddressSpan(device.Mappings[0]) == 2, "Float寄存器跨度错误");
            Assert(PlcConfigStore.GetAddressSpan(device.Mappings[1]) == 8, "String寄存器跨度错误");

            PlcConfiguration invalid = PlcModelCloneForTest(configuration);
            invalid.Devices[0].Mappings.Add(new PlcMapConfig
            {
                Name = "重复写本地",
                Area = PlcArea.HoldingRegister,
                StartAddress = 50,
                DataType = PlcDataType.Float,
                Direction = PlcMapDirection.ReadFromPlc,
                ElementCount = 1,
                VariableNames = new List<string> { "温度" }
            });
            Assert(!PlcConfigStore.Validate(invalid, values, out error) && error.Contains("多个PLC写入来源"),
                "多PLC来源写同一变量未被拒绝");

            invalid = PlcModelCloneForTest(configuration);
            invalid.Devices[0].Mappings[2].Area = PlcArea.InputRegister;
            Assert(!PlcConfigStore.Validate(invalid, values, out error) && error.Contains("只读地址区"),
                "输入寄存器写入未被拒绝");
        }

        private static PlcConfiguration PlcModelCloneForTest(PlcConfiguration source)
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<PlcConfiguration>(json);
        }

        private static void TestPlcHslTransport()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "AutomationPlcTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPath);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task serverTask = Task.Run(() =>
            {
                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] header = ReadExact(stream, 7);
                    int remaining = (header[4] << 8 | header[5]) - 1;
                    byte[] pdu = ReadExact(stream, remaining);
                    if (pdu.Length < 5 || pdu[0] != 3) throw new InvalidOperationException("收到非保持寄存器读取请求");
                    int registerCount = pdu[3] << 8 | pdu[4];
                    if (registerCount != 2) throw new InvalidOperationException("Float读取寄存器数量错误:" + registerCount);
                    int byteCount = registerCount * 2;
                    byte[] response = new byte[9 + byteCount];
                    response[0] = header[0];
                    response[1] = header[1];
                    response[4] = (byte)((3 + byteCount) >> 8);
                    response[5] = (byte)(3 + byteCount);
                    response[6] = header[6];
                    response[7] = 3;
                    response[8] = (byte)byteCount;
                    // 返回ABCD格式的12.5f。
                    byte[] value = { 0x41, 0x48, 0x00, 0x00 };
                    Buffer.BlockCopy(value, 0, response, 9, value.Length);
                    stream.Write(response, 0, response.Length);
                }
            });

            try
            {
                ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
                var store = new PlcConfigStore();
                var device = PlcDeviceConfig.Create(PlcDeviceProfile.GenericModbusTcp);
                device.Name = "HSL测试PLC";
                device.IpAddress = "127.0.0.1";
                device.Port = port;
                device.ConnectTimeoutMs = 2000;
                // 实报文服务按ABCD返回，用于验证可显式覆盖设备默认的CDAB。
                device.DataFormat = "ABCD";
                var configuration = new PlcConfiguration { Devices = new List<PlcDeviceConfig> { device } };
                Assert(store.Save(tempPath, configuration, values, out string saveError), "保存PLC测试配置失败:" + saveError);
                using (var runtime = new PlcRuntimeService(store, values))
                {
                    Assert(runtime.Initialize(out string initError), "PLC运行时授权或初始化失败:" + initError);
                    WaitUntil(() => runtime.GetSnapshots().Any(item => item.DeviceName == device.Name
                        && (item.State == PlcRuntimeState.Ready || item.State == PlcRuntimeState.Faulted)),
                        3000, "PLC测试连接未完成");
                    PlcDeviceRuntimeSnapshot state = runtime.GetSnapshots().Single(item => item.DeviceName == device.Name);
                    Assert(state.State == PlcRuntimeState.Ready, "PLC测试连接失败:" + state.LastError);
                    var request = new PlcMapConfig
                    {
                        Name = "读取Float",
                        Area = PlcArea.HoldingRegister,
                        StartAddress = 0,
                        DataType = PlcDataType.Float,
                        ElementCount = 1,
                        VariableNames = new List<string> { "测试" }
                    };
                    Assert(runtime.TryRead(device.Name, request, out object[] result, out string readError),
                        "HSL Modbus读取失败:" + readError);
                    Assert(result.Length == 1 && Math.Abs(Convert.ToDouble(result[0]) - 12.5d) < 0.0001d,
                        "HSL Float读取结果错误");
                }
                Assert(serverTask.Wait(3000), "PLC实报文测试服务未结束");
            }
            finally
            {
                listener.Stop();
                Directory.Delete(tempPath, true);
            }
        }

        private static void TestPlcRuntimeMappingStateMachine()
        {
            const string highId = "00000000000000000000000000000001";
            const string mediumId = "00000000000000000000000000000002";
            const string lowId = "00000000000000000000000000000003";
            const string bidirectionalId = "00000000000000000000000000000004";
            const string secondaryId = "00000000000000000000000000000005";
            ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
            Assert(values.TrySetValue(10, "高频变量", "double", "0", "PLC状态机测试"), "创建高频变量失败");
            Assert(values.TrySetValue(11, "中频变量", "double", "0", "PLC状态机测试"), "创建中频变量失败");
            Assert(values.TrySetValue(12, "低频变量", "double", "0", "PLC状态机测试"), "创建低频变量失败");
            Assert(values.TrySetValue(13, "双向变量", "double", "1", "PLC状态机测试"), "创建双向变量失败");
            Assert(values.TrySetValue(14, "独立设备变量", "double", "0", "PLC状态机测试"), "创建独立设备变量失败");

            PlcDeviceConfig primary = PlcDeviceConfig.Create(PlcDeviceProfile.GenericModbusTcp);
            primary.Name = "主设备";
            primary.IpAddress = "127.0.0.1";
            primary.ScanIntervalMs = 50;
            primary.Mappings.Add(CreateRuntimeTestMap(highId, "高频", 0, "高频变量",
                PlcMapDirection.ReadFromPlc, PlcMapPriority.High));
            primary.Mappings.Add(CreateRuntimeTestMap(mediumId, "中频", 2, "中频变量",
                PlcMapDirection.ReadFromPlc, PlcMapPriority.Medium));
            primary.Mappings.Add(CreateRuntimeTestMap(lowId, "低频", 4, "低频变量",
                PlcMapDirection.ReadFromPlc, PlcMapPriority.Low));
            primary.Mappings.Add(CreateRuntimeTestMap(bidirectionalId, "双向", 6, "双向变量",
                PlcMapDirection.Bidirectional, PlcMapPriority.High));

            PlcDeviceConfig secondary = PlcDeviceConfig.Create(PlcDeviceProfile.GenericModbusTcp);
            secondary.Name = "独立设备";
            secondary.IpAddress = "127.0.0.1";
            secondary.AutoConnect = false;
            secondary.ScanIntervalMs = 50;
            secondary.Mappings.Add(CreateRuntimeTestMap(secondaryId, "独立读取", 0, "独立设备变量",
                PlcMapDirection.ReadFromPlc, PlcMapPriority.High));

            var primaryAdapter = new FakePlcAdapter();
            primaryAdapter.SetValue(highId, 10d);
            primaryAdapter.SetValue(mediumId, 20d);
            primaryAdapter.SetValue(lowId, 30d);
            primaryAdapter.SetValue(bidirectionalId, 2d);
            var secondaryAdapter = new FakePlcAdapter();
            secondaryAdapter.SetValue(secondaryId, 40d);
            var adapters = new Dictionary<string, FakePlcAdapter>(StringComparer.OrdinalIgnoreCase)
            {
                [primary.Name] = primaryAdapter,
                [secondary.Name] = secondaryAdapter
            };
            var configuration = new PlcConfiguration
            {
                Devices = new List<PlcDeviceConfig> { primary, secondary }
            };
            Assert(PlcConfigStore.Validate(configuration, values, out string validationError),
                "状态机测试配置无效:" + validationError);

            using (var runtime = new PlcRuntimeService(new PlcConfigStore(), values,
                config => adapters[config.Name]))
            {
                Assert(runtime.ReloadConfiguration(configuration, true, out string reloadError),
                    "载入假PLC配置失败:" + reloadError);
                WaitUntil(() => runtime.GetSnapshots().Single(item => item.DeviceName == primary.Name)
                    .State == PlcRuntimeState.Ready, 2000, "开启自动连接的主设备未自动进入就绪状态");
                Assert(primaryAdapter.ConnectCount == 1, "自动连接设备的初始连接次数错误");
                Assert(runtime.GetSnapshots().Single(item => item.DeviceName == secondary.Name)
                    .State == PlcRuntimeState.Uninitialized && secondaryAdapter.ConnectCount == 0,
                    "关闭自动连接的设备仍在启动时连接");
                string primaryError = null;
                Assert(runtime.TryReinitialize(secondary.Name, out string secondaryError),
                    "独立设备初始化失败:" + secondaryError);
                Assert(runtime.TryStartMapping(primary.Name, out primaryError),
                    "主设备映射启动失败:" + primaryError);
                Assert(runtime.TryStartMapping(secondary.Name, out secondaryError),
                    "独立设备映射启动失败:" + secondaryError);

                WaitUntil(() => primaryAdapter.GetBatchCount(highId) >= 42, 4000,
                    "三级扫描调度未达到验证轮数");
                Dictionary<string, int> counts = primaryAdapter.GetBatchCounts();
                int highCount = counts[highId];
                Assert(counts[mediumId] == (highCount - 1) / 10 + 1,
                    $"中优先级不是每10轮执行:{highCount}/{counts[mediumId]}");
                Assert(counts[lowId] == (highCount - 1) / 40 + 1,
                    $"低优先级不是每40轮执行:{highCount}/{counts[lowId]}");

                PlcDeviceRuntimeSnapshot primaryState = runtime.GetSnapshots()
                    .Single(item => item.DeviceName == primary.Name);
                PlcMapRuntimeSnapshot conflict = primaryState.Mappings
                    .Single(item => item.MapId == bidirectionalId);
                Assert(conflict.State == PlcMapRuntimeState.Conflict,
                    "初始值不一致的双向映射未被隔离");
                Assert(runtime.TryResolveConflict(primary.Name, bidirectionalId,
                    PlcConflictResolution.UsePlcValue, out string resolveError),
                    "以PLC为准解除冲突失败:" + resolveError);
                Assert(ReadDoubleValue(values, "双向变量") == 2d, "PLC值未同步到本地变量");

                primaryAdapter.SetValue(bidirectionalId, 3d);
                Assert(values.setValueByName("双向变量", 4d, "PLC状态机测试"), "修改双向本地变量失败");
                WaitUntil(() => runtime.GetSnapshots().Single(item => item.DeviceName == primary.Name)
                    .Mappings.Single(item => item.MapId == bidirectionalId).State == PlcMapRuntimeState.Conflict,
                    2000, "两侧同时变化未产生双向冲突");
                Assert(runtime.TryResolveConflict(primary.Name, bidirectionalId,
                    PlcConflictResolution.UseLocalValue, out resolveError),
                    "以本地为准解除冲突失败:" + resolveError);
                Assert(Convert.ToDouble(primaryAdapter.GetValue(bidirectionalId)[0]) == 4d,
                    "本地值未写入PLC适配器");

                PlcMapConfig overlappingWrite = CreateRuntimeTestMap(highId, "重叠直写", 0, "高频变量",
                    PlcMapDirection.WriteToPlc, PlcMapPriority.High);
                Assert(!runtime.TryWrite(primary.Name, overlappingWrite, new object[] { 1d }, out string writeError)
                    && writeError.Contains("重叠"), "映射期间重叠直接写入未被拒绝");
                Assert(runtime.TryRead(primary.Name, overlappingWrite, out object[] directValues,
                    out string readError) && Convert.ToDouble(directValues[0]) == 10d,
                    "映射期间直接读取未与扫描串行执行:" + readError);

                primaryAdapter.SetConnectFailures(1);
                primaryAdapter.FailNextBatch();
                WaitUntil(() => runtime.GetSnapshots().Single(item => item.DeviceName == primary.Name)
                    .State == PlcRuntimeState.Ready && primaryAdapter.ConnectCount >= 3,
                    5000, "自动连接设备断线后未持续重连并恢复到就绪状态");
                PlcDeviceRuntimeSnapshot recoveredState = runtime.GetSnapshots()
                    .Single(item => item.DeviceName == primary.Name);
                Assert(recoveredState.State == PlcRuntimeState.Ready
                    && recoveredState.Mappings.All(item => item.State == PlcMapRuntimeState.Idle),
                    "自动重连后错误地恢复了变量映射");
                PlcDeviceRuntimeSnapshot secondaryState = runtime.GetSnapshots()
                    .Single(item => item.DeviceName == secondary.Name);
                Assert(secondaryState.State == PlcRuntimeState.Mapping,
                    "单设备通讯故障影响了其他设备映射");
                Assert(runtime.TryStopMapping(secondary.Name, out secondaryError),
                    "独立设备停止映射失败:" + secondaryError);
            }
        }

        private static void TestPlcManagementFormCanShow()
        {
            Exception threadError = null;
            bool shown = false;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new FrmPlc())
                    {
                        Assert(form.TopLevel, "PLC管理窗体被错误配置为嵌入式子窗体");
                        Assert(form.FormBorderStyle == FormBorderStyle.Sizable,
                            "PLC管理窗体缺少独立窗口边框");
                        form.Show();
                        Application.DoEvents();
                        shown = form.Visible && form.IsHandleCreated;
                        form.Hide();
                    }
                }
                catch (Exception ex)
                {
                    threadError = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert(thread.Join(3000), "PLC管理窗体显示测试超时");
            if (threadError != null) throw new InvalidOperationException("PLC管理窗体显示失败", threadError);
            Assert(shown, "PLC管理窗体调用Show后不可见");
        }

        private static PlcMapConfig CreateRuntimeTestMap(string id, string name, int address,
            string variableName, PlcMapDirection direction, PlcMapPriority priority)
        {
            return new PlcMapConfig
            {
                Id = id,
                Name = name,
                Area = PlcArea.HoldingRegister,
                StartAddress = address,
                DataType = PlcDataType.UShort,
                Direction = direction,
                Priority = priority,
                ElementCount = 1,
                VariableNames = new List<string> { variableName }
            };
        }

        private static double ReadDoubleValue(ValueConfigStore values, string name)
        {
            Assert(values.TryGetValueByName(name, out DicValue value) && value != null,
                "PLC测试变量不存在:" + name);
            return double.Parse(value.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed class FakePlcAdapter : IPlcAdapter
        {
            private readonly object syncRoot = new object();
            private readonly Dictionary<string, object[]> values = new Dictionary<string, object[]>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> batchCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            private bool failNextBatch;
            private int connectFailuresRemaining;
            private int connectCount;

            public int ConnectCount
            {
                get { lock (syncRoot) return connectCount; }
            }

            public bool Connect(out string error)
            {
                lock (syncRoot)
                {
                    connectCount++;
                    if (connectFailuresRemaining > 0)
                    {
                        connectFailuresRemaining--;
                        error = "模拟连接失败";
                        return false;
                    }
                    error = null;
                    return true;
                }
            }

            public void Close()
            {
            }

            public bool Read(PlcMapConfig map, out object[] result, out string error)
            {
                lock (syncRoot)
                {
                    error = null;
                    if (!values.TryGetValue(map.Id, out object[] stored))
                    {
                        result = null;
                        error = "假PLC不存在映射值:" + map.Id;
                        return false;
                    }
                    result = stored.ToArray();
                    return true;
                }
            }

            public bool ReadBatch(IReadOnlyList<PlcMapConfig> maps,
                out IReadOnlyDictionary<string, object[]> result, out string error)
            {
                lock (syncRoot)
                {
                    if (failNextBatch)
                    {
                        failNextBatch = false;
                        result = null;
                        error = "模拟通讯故障";
                        return false;
                    }
                    var output = new Dictionary<string, object[]>(StringComparer.Ordinal);
                    foreach (PlcMapConfig map in maps)
                    {
                        if (!values.TryGetValue(map.Id, out object[] stored))
                        {
                            result = null;
                            error = "假PLC不存在映射值:" + map.Id;
                            return false;
                        }
                        output[map.Id] = stored.ToArray();
                        batchCounts[map.Id] = batchCounts.TryGetValue(map.Id, out int count) ? count + 1 : 1;
                    }
                    result = output;
                    error = null;
                    return true;
                }
            }

            public bool Write(PlcMapConfig map, IReadOnlyList<object> input, out string error)
            {
                lock (syncRoot)
                {
                    values[map.Id] = input.ToArray();
                    error = null;
                    return true;
                }
            }

            public void SetValue(string mapId, params object[] input)
            {
                lock (syncRoot) values[mapId] = input.ToArray();
            }

            public object[] GetValue(string mapId)
            {
                lock (syncRoot) return values[mapId].ToArray();
            }

            public int GetBatchCount(string mapId)
            {
                lock (syncRoot) return batchCounts.TryGetValue(mapId, out int count) ? count : 0;
            }

            public Dictionary<string, int> GetBatchCounts()
            {
                lock (syncRoot) return batchCounts.ToDictionary(item => item.Key, item => item.Value,
                    StringComparer.Ordinal);
            }

            public void FailNextBatch()
            {
                lock (syncRoot) failNextBatch = true;
            }

            public void SetConnectFailures(int count)
            {
                lock (syncRoot) connectFailuresRemaining = count;
            }

            public void Dispose()
            {
            }
        }

        private static void TestRunningSafeHotUpdate()
        {
            using (var firstGate = new ManualResetEventSlim(false))
            using (var firstEntered = new ManualResetEventSlim(false))
            {
                int oldCount = 0;
                int newCount = 0;
                int appendedCount = 0;
                var functions = new CustomFunc();
                functions.RegisterFunction("block", () => { firstEntered.Set(); firstGate.Wait(3000); });
                functions.RegisterFunction("old", () => Interlocked.Increment(ref oldCount));
                functions.RegisterFunction("new", () => Interlocked.Increment(ref newCount));
                functions.RegisterFunction("appended", () => Interlocked.Increment(ref appendedCount));
                Proc proc = CreateProc(new CallCustomFunc { Name = "block" }, new CallCustomFunc { Name = "old" });
                using (ProcessEngine engine = CreateEngine(CreateValueStore(ResetStatus.ResetCompleted), functions, proc))
                {
                    Assert(engine.StartProc(proc, 0), "热更新测试流程启动失败");
                    Assert(firstEntered.Wait(1000), "热更新测试未进入首条阻塞指令");
                    Proc updated = ObjectGraphCloner.Clone(proc);
                    ((CallCustomFunc)updated.steps[0].Ops[1]).Name = "new";
                    updated.steps[0].Ops.Add(new CallCustomFunc { Id = Guid.NewGuid(), Name = "appended" });
                    Assert(engine.PublishProc(0, updated, out string error), "运行中安全热更新发布失败:" + error);
                    EngineSnapshot pending = engine.GetSnapshot(0);
                    Assert(pending.HasPendingUpdate && pending.PublishedRevision > pending.AppliedRevision,
                        "发布后未报告待应用版本");
                    firstGate.Set();
                    WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 3000, "热更新流程未结束");
                    Assert(oldCount == 0 && newCount == 1 && appendedCount == 1, "安全边界未执行新版本后续指令");
                    EngineSnapshot applied = engine.GetSnapshot(0);
                    Assert(!applied.HasPendingUpdate && applied.PublishedRevision == applied.AppliedRevision,
                        "运行版本应用后未返回版本确认");
                }
            }
        }

        private static void TestRunningStructuralUpdateRejected()
        {
            using (var gate = new ManualResetEventSlim(false))
            using (var entered = new ManualResetEventSlim(false))
            {
                var functions = new CustomFunc();
                functions.RegisterFunction("block", () => { entered.Set(); gate.Wait(3000); });
                functions.RegisterFunction("next", () => { });
                Proc proc = CreateProc(new CallCustomFunc { Name = "block" }, new CallCustomFunc { Name = "next" });
                using (ProcessEngine engine = CreateEngine(CreateValueStore(ResetStatus.ResetCompleted), functions, proc))
                {
                    Assert(engine.StartProc(proc, 0), "结构拒绝测试流程启动失败");
                    Assert(entered.Wait(1000), "结构拒绝测试未进入当前指令");
                    Proc updated = ObjectGraphCloner.Clone(proc);
                    updated.steps[0].Ops.RemoveAt(0);
                    Assert(!engine.PublishProc(0, updated, out string error), "运行中删除当前指令未被拒绝");
                    Assert(!string.IsNullOrWhiteSpace(error), "结构拒绝未返回原因");
                    gate.Set();
                    WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 3000, "结构拒绝测试流程未结束");
                }
            }
        }

        private static void TestRunningFutureOperationDeletion()
        {
            using (var gate = new ManualResetEventSlim(false))
            using (var entered = new ManualResetEventSlim(false))
            {
                int deletedCount = 0;
                var functions = new CustomFunc();
                functions.RegisterFunction("block", () => { entered.Set(); gate.Wait(3000); });
                functions.RegisterFunction("deleted", () => Interlocked.Increment(ref deletedCount));
                Proc proc = CreateProc(new CallCustomFunc { Name = "block" }, new CallCustomFunc { Name = "deleted" });
                using (ProcessEngine engine = CreateEngine(CreateValueStore(ResetStatus.ResetCompleted), functions, proc))
                {
                    Assert(engine.StartProc(proc, 0), "删除后续指令测试流程启动失败");
                    Assert(entered.Wait(1000), "删除后续指令测试未进入首条阻塞指令");
                    Proc updated = ObjectGraphCloner.Clone(proc);
                    updated.steps[0].Ops.RemoveAt(1);
                    Assert(engine.PublishProc(0, updated, out string error), "删除后续指令发布失败:" + error);
                    gate.Set();
                    WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 3000, "删除后续指令后流程未结束");
                    Assert(deletedCount == 0, "已删除的后续指令仍被执行");
                    EngineSnapshot applied = engine.GetSnapshot(0);
                    Assert(applied.State == ProcRunState.Stopped && !applied.HasPendingUpdate
                        && applied.PublishedRevision == applied.AppliedRevision,
                        "删除后续指令后未正确确认运行版本");
                }
            }
        }

        private static void TestPausedCurrentOperationDeletionRejected()
        {
            int removedCount = 0;
            int nextCount = 0;
            var functions = new CustomFunc();
            functions.RegisterFunction("removed", () => Interlocked.Increment(ref removedCount));
            functions.RegisterFunction("next", () => Interlocked.Increment(ref nextCount));
            Proc proc = CreateProc(
                new CallCustomFunc { Name = "removed", isStopPoint = true },
                new CallCustomFunc { Name = "next" });
            using (ProcessEngine engine = CreateEngine(CreateValueStore(ResetStatus.ResetCompleted), functions, proc))
            {
                Assert(engine.StartProc(proc, 0), "暂停结构测试流程启动失败");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Paused, 2000, "流程未停在断点");
                Proc updated = ObjectGraphCloner.Clone(proc);
                updated.steps[0].Ops.RemoveAt(0);
                Assert(!engine.PublishProc(0, updated, out string error), "暂停状态删除当前指令未被拒绝");
                Assert(!string.IsNullOrWhiteSpace(error), "暂停状态删除当前指令未返回拒绝原因");
                EngineSnapshot applied = engine.GetSnapshot(0);
                Assert(!applied.HasPendingUpdate && applied.PublishedRevision == applied.AppliedRevision,
                    "拒绝暂停结构更新后仍残留待发布版本");
                engine.Resume(0);
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 3000, "暂停结构测试流程未结束");
                Assert(removedCount == 1 && nextCount == 1, "拒绝更新后没有按原流程继续执行");
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

        private static void TestEditSession()
        {
            string committed = null;
            bool cancelled = false;
            var invalid = new EditSession<ProcHead>("测试编辑", new ProcHead(),
                draft => string.IsNullOrWhiteSpace(draft.Name) ? "名称为空" : null,
                draft => committed = draft.Name,
                () => cancelled = true);
            Assert(!invalid.TryCommit(out _), "无效草稿被错误提交");
            invalid.Cancel();
            Assert(cancelled && committed == null, "取消编辑修改了正式对象");

            var valid = new EditSession<ProcHead>("测试编辑", new ProcHead { Name = "P1" },
                draft => null, draft => committed = draft.Name);
            Assert(valid.TryCommit(out _) && committed == "P1", "有效草稿提交失败");
        }

        private static void TestConfigurationBatchWriter()
        {
            string path = Path.Combine(Path.GetTempPath(), "AutomationBatch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                File.WriteAllText(Path.Combine(path, "a.json"), "old-a");
                File.WriteAllText(Path.Combine(path, "b.json"), "old-b");
                using (var writer = new ConfigurationBatchWriter(path + Path.DirectorySeparatorChar))
                {
                    writer.AddJson("a.json", new { value = "new-a" });
                    writer.AddJson("b.json", new { value = "new-b" });
                    writer.Commit();
                }
                Assert(File.ReadAllText(Path.Combine(path, "a.json")).Contains("new-a"), "第一个配置文件未提交");
                Assert(File.ReadAllText(Path.Combine(path, "b.json")).Contains("new-b"), "第二个配置文件未提交");

                string transaction = Path.Combine(path, ".transaction-test-recovery");
                Directory.CreateDirectory(transaction);
                File.WriteAllText(Path.Combine(path, "a.json"), "interrupted-new-a");
                File.WriteAllText(Path.Combine(transaction, "a.json.bak"), "stable-old-a");
                File.WriteAllText(Path.Combine(path, "c.json"), "interrupted-new-c");
                File.WriteAllText(Path.Combine(transaction, "manifest.json"),
                    "{\"Entries\":[{\"FileName\":\"a.json\",\"TargetExisted\":true},{\"FileName\":\"c.json\",\"TargetExisted\":false}]}");
                Assert(ConfigurationBatchWriter.RecoverPendingTransactions(path, out string recoveryError),
                    "未完成配置事务恢复失败:" + recoveryError);
                Assert(File.ReadAllText(Path.Combine(path, "a.json")) == "stable-old-a",
                    "未完成事务没有恢复旧配置");
                Assert(!File.Exists(Path.Combine(path, "c.json")), "未完成事务没有移除新增配置");
                Assert(!Directory.Exists(transaction), "恢复后事务目录未清理");
            }
            finally
            {
                Directory.Delete(path, true);
            }
        }

        private static void TestDataStructStrictIndexes()
        {
            var store = new DataStructStore();
            Assert(store.AddStruct("S", out string addError), "创建数据结构失败:" + addError);
            Assert(!store.CreateItem(0, "越界项", 1, out _, out _), "越界插入被自动追加");
            Assert(store.CreateItem(0, "I0", 0, out int itemIndex, out string itemError),
                "创建数据项失败:" + itemError);
            Assert(store.AddField(0, itemIndex, "F0", DataStructValueType.Number, "1.5", 0,
                out _, out string fieldError), "创建字段失败:" + fieldError);
            Assert(!store.TrySetItemValueByIndex(0, itemIndex, 1, "文本"), "不存在的字段被自动创建");
            Assert(!store.TrySetItemValueByIndex(0, itemIndex, 0, "文本", DataStructValueType.Text),
                "字段类型不匹配时仍被写入");
            Assert(!store.TryInsertItem(0, 3, new DataStructItem { Name = "越界项" }),
                "运行时越界插入被自动追加");
            Assert(!store.TryCopyItemAll(0, itemIndex, 0, 3), "复制目标越界被自动追加");
        }

        private static void TestGotoRewriteByOperationId()
        {
            var jump = new Goto { Id = Guid.NewGuid(), DefaultGoto = "0-0-2" };
            var middle = new Delay { Id = Guid.NewGuid(), Name = "中间指令" };
            var target = new Delay { Id = Guid.NewGuid(), Name = "目标指令" };
            Proc before = CreateProc(jump, middle, target);

            Proc inserted = ObjectGraphCloner.Clone(before);
            inserted.steps[0].Ops.Insert(1, new Delay { Id = Guid.NewGuid(), Name = "新增指令" });
            GotoRewriteResult insertResult = ProcessEditingService.RewriteGotoTargets(before, inserted, 0);
            Assert(((Goto)inserted.steps[0].Ops[0]).DefaultGoto == "0-0-3", "插入后跳转未跟随目标指令ID");
            Assert(insertResult.RewrittenCount == 1, "插入后的跳转重写计数错误");

            Proc moved = ObjectGraphCloner.Clone(before);
            OperationType movedTarget = moved.steps[0].Ops[2];
            moved.steps[0].Ops.RemoveAt(2);
            moved.steps[0].Ops.Insert(0, movedTarget);
            ProcessEditingService.RewriteGotoTargets(before, moved, 0);
            Assert(((Goto)moved.steps[0].Ops[1]).DefaultGoto == "0-0-0", "移动后跳转未跟随目标指令ID");

            Proc deleted = ObjectGraphCloner.Clone(before);
            deleted.steps[0].Ops.RemoveAt(2);
            GotoRewriteResult deleteResult = ProcessEditingService.RewriteGotoTargets(before, deleted, 0);
            Assert(deleteResult.InvalidatedCount == 1, "删除目标后的跳转失效计数错误");
            Assert(((Goto)deleted.steps[0].Ops[0]).DefaultGoto.StartsWith(ProcessDefinitionService.DeletedGotoPrefix),
                "删除目标后未标记失效跳转");
            Assert(ProcessDefinitionService.ValidateProcGotoTargets(0, deleted).Count == 0,
                "删除跳转目标后的草稿无法分阶段保存");
            ProcessReadinessAnalysis deletedReadiness = ProcessReadinessService.Analyze(
                0, deleted, new[] { deleted });
            Assert(deletedReadiness.ReadinessStatus == "incomplete" && !deletedReadiness.Runnable,
                "删除跳转目标后的草稿未被启动闸门拦截");

            Proc forwardReferenceBefore = CreateProc(new Delay { Id = Guid.NewGuid(), Name = "已有指令" });
            Proc forwardReferenceAfter = ObjectGraphCloner.Clone(forwardReferenceBefore);
            var newJump = new Goto { Id = Guid.NewGuid(), Name = "新增跳转", DefaultGoto = "0-0-2" };
            forwardReferenceAfter.steps[0].Ops.Add(newJump);
            forwardReferenceAfter.steps[0].Ops.Add(new Delay { Id = Guid.NewGuid(), Name = "新增目标" });
            GotoRewriteResult forwardResult = ProcessEditingService.RewriteGotoTargets(
                forwardReferenceBefore, forwardReferenceAfter, 0);
            Assert(newJump.DefaultGoto == "0-0-2", "新增指令的前向跳转被当作旧索引重写");
            Assert(forwardResult.InvalidatedCount == 0, "新增指令的有效前向跳转被标记为已删除");
            Assert(ProcessDefinitionService.ValidateProcGotoTargets(0, forwardReferenceAfter).Count == 0,
                "同一草稿内新增目标的前向跳转未通过校验");

            Proc explicitUpdate = ObjectGraphCloner.Clone(before);
            explicitUpdate.steps[0].Ops.Add(new Delay { Id = Guid.NewGuid(), Name = "显式新目标" });
            ((Goto)explicitUpdate.steps[0].Ops[0]).DefaultGoto = "0-0-3";
            ProcessEditingService.RewriteGotoTargets(before, explicitUpdate, 0);
            Assert(((Goto)explicitUpdate.steps[0].Ops[0]).DefaultGoto == "0-0-3",
                "本次显式修改的跳转被旧索引重写覆盖");
        }

        private static void TestGotoProcIndexRebuildAfterDeletion()
        {
            Proc first = CreateProc(new Delay { Id = Guid.NewGuid(), Name = "流程0" });
            Proc second = CreateProc(
                new Goto { Id = Guid.NewGuid(), DefaultGoto = "1-0-1" },
                new Delay { Id = Guid.NewGuid(), Name = "流程1目标" });
            Proc third = CreateProc(
                new Goto { Id = Guid.NewGuid(), DefaultGoto = "2-0-1" },
                new Delay { Id = Guid.NewGuid(), Name = "流程2目标" });
            var processes = new List<Proc> { first, second, third };

            processes.RemoveAt(0);
            ProcessEditingService.AdaptGotoProcIndexes(processes, 0);

            Assert(((Goto)processes[0].steps[0].Ops[0]).DefaultGoto == "0-0-1",
                "原流程1删除前序流程后未更新为流程0地址");
            Assert(((Goto)processes[1].steps[0].Ops[0]).DefaultGoto == "1-0-1",
                "原流程2删除前序流程后未更新为流程1地址");
            Assert(ProcessDefinitionService.ValidateProcGotoTargets(0, processes[0]).Count == 0,
                "重排后的流程0跳转校验失败");
            Assert(ProcessDefinitionService.ValidateProcGotoTargets(1, processes[1]).Count == 0,
                "重排后的流程1跳转校验失败");
        }

        private static void TestParamGotoStrictValidation()
        {
            var operation = new ParamGoto { goto1 = "步骤：完成结束", goto2 = "0-0-1" };
            Proc proc = CreateProc(operation, new Delay { Name = "完成结束" });
            Assert(!ProcessDefinitionService.TryValidateOperationGoto(operation, 0, proc, out string displayTextError)
                && displayTextError.Contains("只能填写三段式数字地址"),
                "逻辑判断接受了界面显示文字作为跳转地址");

            operation.goto1 = string.Empty;
            Assert(ProcessDefinitionService.TryValidateOperationGoto(operation, 0, proc, out _),
                "配置保存阶段拒绝了可继续完善的空跳转地址");
            ProcessReadinessAnalysis draft = ProcessReadinessService.Analyze(0, proc, new[] { proc });
            Assert(!draft.Runnable && draft.ReadinessStatus == "incomplete"
                && draft.RunBlockers.Any(item => item.Contains("goto1")),
                "启动闸门未拦截空的成功跳转地址");

            operation.goto1 = "0-0-1";
            operation.goto2 = "0-0-1";
            Assert(ProcessDefinitionService.TryValidateOperationGoto(operation, 0, proc, out _),
                "逻辑判断拒绝了合法的三段式跳转地址");
        }

        private static void TestOperationBehaviorContracts()
        {
            Type[] operationTypes = typeof(OperationType).Assembly.GetTypes()
                .Where(type => typeof(OperationType).IsAssignableFrom(type) || type == typeof(GotoParam))
                .ToArray();
            foreach (Type type in operationTypes)
            {
                foreach (PropertyInfo property in type.GetProperties())
                {
                    TypeConverterAttribute converter = property
                        .GetCustomAttributes(typeof(TypeConverterAttribute), true)
                        .Cast<TypeConverterAttribute>()
                        .FirstOrDefault();
                    if (converter != null && converter.ConverterTypeName.Contains("GotoItem"))
                    {
                        Assert(property.GetCustomAttribute<MarkedGotoAttribute>() != null,
                            $"{type.Name}.{property.Name} 使用 GotoItem 但缺少 MarkedGoto 标记");
                    }
                }
            }

            var logic = new ParamGoto();
            JObject logicContract = OperationBehaviorCatalog.BuildContract(logic);
            Assert(logicContract?["controlFlow"]?["fallThrough"]?.Value<bool>() == false,
                "逻辑判断契约未声明显式控制流");
            Assert(OperationBehaviorCatalog.IsFieldRequired(logic, "goto1")
                && OperationBehaviorCatalog.IsFieldRequired(logic, "goto2"),
                "逻辑判断契约未声明两个跳转字段必填");

            JObject endContract = OperationBehaviorCatalog.BuildContract(new EndProcess());
            Assert(endContract?["coverage"]?.Value<string>() == "specialized"
                && endContract?["controlFlow"]?["terminal"]?.Value<bool>() == true
                && endContract?["controlFlow"]?["fallThrough"]?.Value<bool>() == false,
                "流程结束契约与运行引擎的终止语义不一致");

            JObject unknownContract = OperationBehaviorCatalog.BuildContract(new CallCustomFunc());
            Assert(unknownContract?["coverage"]?.Value<string>() == "unknown"
                && unknownContract?["controlFlow"]?["known"]?.Value<bool>() == false
                && unknownContract?["controlFlow"]?["fallThrough"] == null,
                "未覆盖指令被通用默认值错误声明了控制流语义");

            JObject modifyContract = OperationBehaviorCatalog.BuildContract(new ModifyValue());
            Assert(modifyContract?["semanticKinds"]?["variableOrNumericCompute"]?.Value<string>()
                    == "variable.compute"
                && modifyContract?["constraints"] is JArray modifyConstraints
                && modifyConstraints.Any(item => item.ToString().Contains("必须且只能选择一种")),
                "修改变量行为契约未公开强语义入口或操作数互斥规则");

            var popup = new PopupDialog();
            Assert(!OperationBehaviorCatalog.IsFieldRequired(popup, "PopupGoto1")
                && !OperationBehaviorCatalog.IsFieldRequired(popup, "PopupGoto2"),
                "弹框可选跳转被错误标记为必填");
            Proc popupAtEnd = CreateProc(popup);
            Assert(ProcessDefinitionService.TryValidateOperationGoto(popup, 0, popupAtEnd, out _),
                "流程末尾弹框的空跳转未被允许自然结束");
            popup.PopupType = "弹是与否与取消";
            Assert(!OperationBehaviorCatalog.IsFieldRequired(popup, "PopupGoto2")
                && !OperationBehaviorCatalog.IsFieldRequired(popup, "PopupGoto3"),
                "三按钮弹框的可选跳转被错误标记为必填");

            var alarmOperation = new Delay { AlarmType = "弹框确定与否" };
            Assert(OperationBehaviorCatalog.IsFieldRequired(alarmOperation, "AlarmInfoID")
                && OperationBehaviorCatalog.IsFieldRequired(alarmOperation, "Goto1")
                && OperationBehaviorCatalog.IsFieldRequired(alarmOperation, "Goto2")
                && !OperationBehaviorCatalog.IsFieldRequired(alarmOperation, "Goto3"),
                "报警策略的条件必填规则错误");
        }

        private static void TestAtomicJsonRecovery()
        {
            string path = Path.Combine(Path.GetTempPath(), "AutomationAtomicJson-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                Assert(AtomicJsonFileStore.Save(path, "config", new Dictionary<string, int> { ["version"] = 1 }),
                    "首次JSON保存失败");
                Assert(AtomicJsonFileStore.Save(path, "config", new Dictionary<string, int> { ["version"] = 2 }),
                    "第二次JSON保存失败");
                File.WriteAllText(Path.Combine(path, "config.json"), "{损坏");
                Dictionary<string, int> recovered = AtomicJsonFileStore.Read<Dictionary<string, int>>(path, "config");
                Assert(recovered != null && recovered["version"] == 1, "主文件损坏后未读取上一版备份");

                File.WriteAllText(Path.Combine(path, "unsafe.json"),
                    "{\"$type\":\"System.IO.FileInfo, mscorlib\",\"OriginalPath\":\"x\"}");
                object unsafeValue = AtomicJsonFileStore.Read<object>(path, "unsafe");
                Assert(unsafeValue == null, "配置反序列化接受了白名单外类型");
            }
            finally
            {
                Directory.Delete(path, true);
            }
        }

        private static void TestProcessDirectoryRecovery()
        {
            string configPath = Path.Combine(Path.GetTempPath(), "AutomationWorkRecovery-" + Guid.NewGuid().ToString("N"));
            string workPath = Path.Combine(configPath, "Work");
            string backupPath = Path.Combine(configPath, "Work_bak");
            Directory.CreateDirectory(backupPath);
            try
            {
                File.WriteAllText(Path.Combine(backupPath, "0.json"), "backup");
                Assert(ProcessConfigStore.RecoverIfNeeded(workPath, out string message), "流程目录恢复返回失败");
                Assert(Directory.Exists(workPath), "Work_bak未恢复为Work");
                Assert(File.Exists(Path.Combine(workPath, "0.json")), "恢复后的流程文件不存在");
                Assert(!string.IsNullOrWhiteSpace(message), "目录恢复未返回诊断消息");
            }
            finally
            {
                Directory.Delete(configPath, true);
            }
        }

        private static void TestConcurrentProcessDirectoryRebuild()
        {
            string configPath = Path.Combine(Path.GetTempPath(), "AutomationWorkConcurrent-" + Guid.NewGuid().ToString("N"));
            string workPath = Path.Combine(configPath, "Work");
            Directory.CreateDirectory(configPath);
            try
            {
                const int taskCount = 8;
                var successes = new bool[taskCount];
                var errors = new string[taskCount];
                Task[] tasks = Enumerable.Range(0, taskCount).Select(index => Task.Run(() =>
                {
                    var processes = new List<Proc>
                    {
                        CreateProc(new Delay { Id = Guid.NewGuid(), Name = $"并发流程{index}" })
                    };
                    successes[index] = ProcessConfigStore.Rebuild(
                        workPath, processes, 0, out errors[index], out bool rollbackFailed)
                        && !rollbackFailed;
                })).ToArray();

                Task.WaitAll(tasks);
                Assert(successes.All(item => item),
                    "并发流程目录事务失败：" + string.Join(";", errors.Where(item => !string.IsNullOrWhiteSpace(item))));
                Dictionary<int, string> indexMap = ProcessDefinitionService.BuildProcFileIndexMap(workPath, out int maxIndex);
                Assert(maxIndex == 0 && indexMap.Count == 1 && File.Exists(Path.Combine(workPath, "0.json")),
                    "并发事务完成后的流程目录不连续");
                Assert(!Directory.Exists(Path.Combine(configPath, "Work_tmp")),
                    "并发事务完成后残留Work_tmp目录");
            }
            finally
            {
                Directory.Delete(configPath, true);
            }
        }

        private static void TestStructuredNativeOperationCompilation()
        {
            List<OperationType> registered = OperationDefinitionRegistry.CreateAll().ToList();
            Assert(registered.Count == 45, $"原生指令注册数量异常：{registered.Count}");

            var context = new AiOperationCompileContext(
                0,
                new Dictionary<string, DicValue>(StringComparer.Ordinal),
                new AiResourceSnapshot(),
                operationKeyLocations: new Dictionary<string, OperationReferenceLocation>(StringComparer.Ordinal)
                {
                    [AiOperationCompileContext.BuildOperationKeyForStepKey("main", "target")] =
                        new OperationReferenceLocation(0, 0),
                    [AiOperationCompileContext.BuildOperationKeyForStepKey("main", "target2")] =
                        new OperationReferenceLocation(0, 1)
                });
            foreach (OperationType definition in registered)
            {
                JObject contract = StructuredOperationCompiler.BuildContract(definition.OperaType);
                Assert(contract["fields"] is JObject, $"{definition.OperaType} 未生成递归字段契约");
                IDictionary<string, object> minimumFields = definition is Goto
                    ? new Dictionary<string, object>
                    {
                        ["DefaultGoto"] = new JObject { ["stepKey"] = "main", ["operationKey"] = "target" }
                    }
                    : new Dictionary<string, object>();
                JObject behavior = OperationBehaviorCatalog.BuildContract(definition);
                foreach (JProperty rule in ((JObject)behavior["fieldRules"]).Properties()
                    .Where(item => OperationBehaviorCatalog.IsFieldRequired(definition, item.Name)))
                {
                    if (string.Equals(rule.Value?["referenceType"]?.Value<string>(),
                        "proc.goto", StringComparison.Ordinal))
                    {
                        minimumFields[rule.Name] = new JObject
                        {
                            ["stepKey"] = "main",
                            ["operationKey"] = "target"
                        };
                    }
                    else
                    {
                        minimumFields[rule.Name] = "测试";
                    }
                }
                OperationType compiled = StructuredOperationCompiler.Compile(definition.OperaType,
                    minimumFields, context);
                Assert(compiled.GetType() == definition.GetType(), $"{definition.OperaType} 编译结果类型错误");
            }

            JObject tcpContract = StructuredOperationCompiler.BuildContract("网口通讯操作");
            Assert(tcpContract["fields"]?["Params"]?["jsonType"]?.Value<string>() == "array"
                && tcpContract["fields"]?["Params"]?["items"]?["fields"]?["Name"] != null,
                "网口通讯 Params[0].Name 未进入递归契约");
            var tcpFields = new Dictionary<string, object>
            {
                ["Params"] = new JArray(new JObject
                {
                    ["Name"] = "测试TCP",
                    ["Ops"] = "启动"
                })
            };
            var tcp = (TcpOps)StructuredOperationCompiler.Compile("网口通讯操作", tcpFields, context);
            Assert(tcp.Count == "1" && tcp.Params.Count == 1 && tcp.Params[0].Name == "测试TCP",
                "通讯嵌套参数未编译或数量未自动同步");

            JObject plcReadWriteContract = StructuredOperationCompiler.BuildContract("PLC读写");
            Assert(plcReadWriteContract["fields"]?["ItemCount"] == null
                && plcReadWriteContract["fields"]?["ReadItems"]?["jsonType"]?.Value<string>() == "array"
                && plcReadWriteContract["fields"]?["ReadItems"]?["items"]?["fields"]?["VariableName"]?["referenceType"]?.Value<string>() == "value",
                "PLC按项读取没有生成可写嵌套契约，或仍要求AI手填ItemCount");
            Assert(plcReadWriteContract["behavior"]?["modeMatrix"] is JArray plcModes
                && plcModes.Count == 4
                && plcReadWriteContract["behavior"]?["dataRules"]?["variableType"] != null,
                "PLC读写缺少模式矩阵或数据类型规则");
            var plcVariables = new Dictionary<string, DicValue>(StringComparer.Ordinal)
            {
                ["温度"] = new DicValue { Index = 20, Name = "温度", Type = "double", Value = "0", ConfigValue = "0" },
                ["压力"] = new DicValue { Index = 21, Name = "压力", Type = "double", Value = "0", ConfigValue = "0" }
            };
            var plcContext = new AiOperationCompileContext(
                0, plcVariables,
                new AiResourceSnapshot(references: new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
                {
                    ["plc.device"] = new[] { "PLC_1" }
                }));
            var plcRead = (PlcReadWrite)StructuredOperationCompiler.Compile("PLC读写",
                new Dictionary<string, object>
                {
                    ["DeviceName"] = "PLC_1",
                    ["Action"] = "Read",
                    ["ReadMode"] = "DiscreteItems",
                    ["ReadItems"] = new JArray(
                        new JObject
                        {
                            ["Area"] = "HoldingRegister", ["StartAddress"] = 0,
                            ["DataType"] = "Float", ["StringByteLength"] = 0,
                            ["VariableName"] = "温度"
                        },
                        new JObject
                        {
                            ["Area"] = "HoldingRegister", ["StartAddress"] = 2,
                            ["DataType"] = "Float", ["StringByteLength"] = 0,
                            ["VariableName"] = "压力"
                        })
                }, plcContext);
            Assert(plcRead.ItemCount == 2 && plcRead.ReadItems.Count == 2
                && plcRead.ReadItems[1].VariableName == "压力",
                "PLC读取项未递归编译或ItemCount未按数组长度同步");
            var plcMapping = (PlcMappingControl)StructuredOperationCompiler.Compile("PLC映射控制",
                new Dictionary<string, object>
                {
                    ["DeviceName"] = "PLC_1",
                    ["Action"] = "Start"
                }, plcContext);
            Assert(plcMapping.DeviceName == "PLC_1" && plcMapping.Action == PlcMappingAction.Start,
                "PLC映射控制未按强类型字段编译");
            var plcValidationContext = new ProcessDefinitionValidationContext(
                plcVariables.Keys, Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), new[] { "PLC_1" }, plcVariables);
            Proc plcProc = CreateProc(plcRead, plcMapping);
            ProcessReadinessAnalysis plcReadiness = ProcessReadinessService.Analyze(
                0, plcProc, new[] { plcProc }, plcValidationContext);
            Assert(plcReadiness.Runnable,
                "有效PLC指令未通过运行就绪校验：" + string.Join("；", plcReadiness.RunBlockers));

            JObject gotoContract = StructuredOperationCompiler.BuildContract("跳转");
            Assert(gotoContract["fields"]?["Params"]?["items"]?["fields"]?["Goto"]?["referenceType"]
                    ?.Value<string>() == "proc.goto.symbolic",
                "Params[0].Goto 未声明为符号跳转");
            JArray targetShapes = (JArray)gotoContract["fields"]?["DefaultGoto"]?["shapes"];
            Assert(targetShapes != null
                && targetShapes.Any(item => item["stepId"] != null && item["operationKey"] != null)
                && targetShapes.Any(item => item["stepKey"] != null && item["operationKey"] != null)
                && targetShapes.All(item => item["step"] == null && item["operation"] == null),
                "原生跳转契约没有使用stepId/stepKey强类型目标");
            JObject modifyContract = StructuredOperationCompiler.BuildContract("修改变量");
            JToken valueSourceSchema = modifyContract["fields"]?["ValueSourceName"];
            Assert(valueSourceSchema?["referenceType"]?.Value<string>() == "value"
                && valueSourceSchema["values"] == null,
                "动态变量候选仍被重复注入原生指令Schema");
            var gotoFields = new Dictionary<string, object>
            {
                ["ValueIndex"] = "0",
                ["DefaultGoto"] = new JObject { ["stepKey"] = "main", ["operationKey"] = "target" },
                ["Params"] = new JArray(new JObject
                {
                    ["MatchValue"] = "1",
                    ["Goto"] = new JObject { ["stepKey"] = "main", ["operationKey"] = "target2" }
                })
            };
            var jump = (Goto)StructuredOperationCompiler.Compile("跳转", gotoFields, context);
            Assert(jump.Count == "1" && jump.DefaultGoto == "0-0-0" && jump.Params[0].Goto == "0-0-1",
                "嵌套符号跳转未正确编译为物理地址");
            OperationType incompleteAlarm = StructuredOperationCompiler.Compile(
                "IO逻辑跳转", new Dictionary<string, object>
                {
                    ["AlarmType"] = "自动处理",
                    ["TrueGoto"] = new JObject { ["stepKey"] = "main", ["operationKey"] = "target" },
                    ["FalseGoto"] = new JObject { ["stepKey"] = "main", ["operationKey"] = "target2" }
                }, context);
            Proc incompleteAlarmProc = CreateProc(incompleteAlarm);
            ProcessReadinessAnalysis incompleteAlarmReadiness = ProcessReadinessService.Analyze(
                0, incompleteAlarmProc, new[] { incompleteAlarmProc });
            Assert(!incompleteAlarmReadiness.Runnable
                && incompleteAlarmReadiness.RunBlockers.Any(item => item.Contains("Goto1")),
                "报警分支缺失未保存为可继续完善的草稿，或启动闸门未拦截");

            var changeSet = new AiChangeSet
            {
                Version = 2,
                Processes = new List<ProcessDefinition>
                {
                    new ProcessDefinition
                    {
                        Name = "原生递归编译测试",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "main",
                                Name = "主步骤",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation
                                    {
                                        Key = "delay",
                                        Kind = "native.operation",
                                        OperaType = "延时",
                                        Fields = new Dictionary<string, object> { ["timeMiniSecond"] = "10" }
                                    },
                                    new SemanticOperation
                                    {
                                        Kind = "native.operation",
                                        OperaType = "跳转",
                                        Fields = new Dictionary<string, object>
                                        {
                                            ["DefaultGoto"] = new JObject
                                            {
                                                ["stepKey"] = "main",
                                                ["operationKey"] = "delay"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            AiChangeSetCompileResult changeSetResult = AiChangeSetCompiler.Compile(changeSet,
                new List<Proc>(), new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Assert(changeSetResult.Processes[0].steps[0].Ops[0] is Delay
                && changeSetResult.Processes[0].steps[0].Ops[0].Name == "延时"
                && changeSetResult.Processes[0].steps[0].Ops[1] is Goto,
                "native.operation 未接入 V2 变更集编译主链");

            var ioCheck = (IoCheck)StructuredOperationCompiler.Compile("IO检测",
                new Dictionary<string, object>
                {
                    ["timeOutC"] = new JObject { ["TimeOut"] = 2500 },
                    ["IoParams"] = new JArray()
                }, context);
            Assert(ioCheck.timeOutC.TimeOut == 2500 && ioCheck.IOCount == "0",
                "嵌套对象或 IO 数量字段未正确编译");

            AssertThrows<InvalidOperationException>(() => StructuredOperationCompiler.Compile("网口通讯操作",
                new Dictionary<string, object> { ["Params_0_Name"] = "错误扁平字段" }, context),
                "原生编译器接受了 PropertyGrid 扁平字段");
            AssertThrows<InvalidOperationException>(() => StructuredOperationCompiler.Compile("跳转",
                new Dictionary<string, object> { ["DefaultGoto"] = "0-0-0" }, context),
                "原生编译器接受了物理跳转字符串");
        }

        private static void TestAiChangeSetCompilation()
        {
            var variables = new Dictionary<string, DicValue>(StringComparer.Ordinal)
            {
                ["测试计数"] = new DicValue
                {
                    Index = 10,
                    Name = "测试计数",
                    Type = "double",
                    ConfigValue = "0",
                    Value = "0"
                },
                ["累加和"] = new DicValue
                {
                    Index = 11,
                    Name = "累加和",
                    Type = "double",
                    ConfigValue = "0",
                    Value = "0"
                }
            };
            var changeSet = new AiChangeSet
            {
                Version = 2,
                Title = "重建计数测试流程",
                DeleteProcesses = new ProcessDeleteSelection { Mode = "all" },
                Variables = new List<VariableChange>
                {
                    new VariableChange
                    {
                        Name = "测试计数",
                        Type = "double",
                        InitialValue = "0",
                        Policy = "reuse"
                    }
                },
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Name = "Test_循环计数",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "loop",
                                Name = "主循环",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation
                                    {
                                        Key = "count",
                                        Kind = "variable.add",
                                        Variable = "测试计数",
                                        Amount = 1
                                    },
                                    new SemanticOperation { Key = "wait", Kind = "wait", Milliseconds = 1000 },
                                    new SemanticOperation
                                    {
                                        Kind = "flow.goto",
                                        Target = new OperationTarget { StepKey = "loop", OperationKey = "count" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            AiChangeSetCompileResult result = AiChangeSetCompiler.Compile(
                changeSet,
                new List<Proc> { CreateProc(new Delay { timeMiniSecond = "1" }) },
                variables);
            Assert(result.DeletedProcessCount == 1 && result.CreatedProcessCount == 1,
                "流程删除与创建没有合并到同一编译结果");
            Assert(result.ChangedVariableCount == 0, "reuse策略不应重复创建已有变量");
            Assert(result.OperationCount == 3, "语义指令数量不正确");
            Assert(result.Processes[0].steps[0].Ops[0] is ModifyValue modify
                && modify.ModifyType == "叠加"
                && modify.ValueSourceName == "测试计数"
                && modify.OutputValueName == "测试计数", "variable.add编译结果不正确");
            Assert(result.Processes[0].steps[0].Ops[2] is Goto gotoOperation
                && gotoOperation.Count == "0"
                && gotoOperation.DefaultGoto == "0-0-0", "符号跳转未解析为最终物理地址");
            var branchChangeSet = new AiChangeSet
            {
                Version = 2,
                Variables = new List<VariableChange>
                {
                    new VariableChange { Name = "测试计数", Type = "double", Policy = "require" }
                },
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Name = "分支测试",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "main",
                                Name = "主步骤",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation
                                    {
                                        Key = "branch",
                                        Kind = "branch.number_range",
                                        Variable = "测试计数",
                                        Min = 0,
                                        Max = 10,
                                        WhenTrue = new OperationTarget { StepKey = "main", OperationKey = "popup" },
                                        WhenFalse = new OperationTarget { StepKey = "main", OperationKey = "back" }
                                    },
                                    new SemanticOperation { Key = "popup", Kind = "popup.message", Message = "范围内" },
                                    new SemanticOperation
                                    {
                                        Key = "back",
                                        Kind = "flow.goto",
                                        Target = new OperationTarget { StepKey = "main", OperationKey = "branch" }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            AiChangeSetCompileResult branchResult = AiChangeSetCompiler.Compile(
                branchChangeSet, new List<Proc>(), variables);
            Assert(branchResult.Processes[0].steps[0].Ops[0] is ParamGoto branch
                && branch.goto1 == "0-0-1"
                && branch.goto2 == "0-0-2", "前向符号分支未按完整草稿解析");
            Assert(branchResult.Processes[0].steps[0].Ops[1] is PopupDialog popup
                && popup.PopupMessage == "范围内", "popup.message编译结果不正确");

            var computeChangeSet = new AiChangeSet
            {
                Version = 2,
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Name = "求和语义测试",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "main",
                                Name = "主步骤",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation
                                    {
                                        Key = "sum",
                                        Kind = "variable.compute",
                                        SourceVariable = "累加和",
                                        Operator = "add",
                                        OperandVariable = "测试计数",
                                        OutputVariable = "累加和"
                                    },
                                    new SemanticOperation
                                    {
                                        Key = "branch",
                                        Kind = "branch.number_compare",
                                        Variable = "测试计数",
                                        Comparison = "gt",
                                        CompareValue = 100000,
                                        WhenTrue = new OperationTarget { OperationKey = "done" },
                                        WhenFalse = new OperationTarget { OperationKey = "sum" }
                                    },
                                    new SemanticOperation { Key = "done", Kind = "popup.variable", Variable = "累加和" }
                                }
                            }
                        }
                    }
                }
            };
            AiChangeSetCompileResult computeResult = AiChangeSetCompiler.Compile(
                computeChangeSet, new List<Proc>(), variables);
            Assert(computeResult.Processes[0].steps[0].Ops[0] is ModifyValue compute
                && compute.ModifyType == "叠加"
                && compute.ValueSourceName == "累加和"
                && compute.ChangeValueName == "测试计数"
                && string.IsNullOrEmpty(compute.ChangeValue)
                && compute.OutputValueName == "累加和",
                "variable.compute 未编译为互斥且完整的修改变量配置");
            Assert(computeResult.Processes[0].steps[0].Ops[1] is ParamGoto compare
                && compare.Params[0].JudgeMode == "值在区间右"
                && !compare.Params[0].equal
                && compare.Params[0].Down == 100000
                && compare.goto1 == "0-0-2"
                && compare.goto2 == "0-0-0",
                "branch.number_compare 未正确编译数值比较和符号跳转");

            var popupVariableChangeSet = new AiChangeSet
            {
                Version = 2,
                Variables = new List<VariableChange>
                {
                    new VariableChange { Name = "测试计数", Type = "double", Policy = "require" }
                },
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Name = "变量弹框",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "main",
                                Name = "主步骤",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation
                                    {
                                        Kind = "popup.variable",
                                        Name = "当前计数",
                                        Variable = "测试计数",
                                        AutoCloseMs = 1000
                                    }
                                }
                            }
                        }
                    }
                }
            };
            AiChangeSetCompileResult popupVariableResult = AiChangeSetCompiler.Compile(
                popupVariableChangeSet, new List<Proc>(), variables);
            Assert(popupVariableResult.Processes[0].steps[0].Ops[0] is PopupDialog variablePopup
                && variablePopup.InfoType == "变量类型"
                && variablePopup.PopupMessageValue == "测试计数"
                && variablePopup.Name == "当前计数", "popup.variable未编译为平台原生变量弹框");

            popupVariableChangeSet.Processes[0].Steps[0].Operations[0] = new SemanticOperation
            {
                Kind = "popup.message",
                Message = "当前计数：{测试计数}"
            };
            bool placeholderRejected = false;
            try
            {
                AiChangeSetCompiler.Compile(popupVariableChangeSet, new List<Proc>(), variables);
            }
            catch (InvalidOperationException ex)
            {
                placeholderRejected = ex.Message.Contains("不支持 {变量名} 插值");
            }
            Assert(placeholderRejected, "popup.message错误接受了变量模板语法");

            var resourceChangeSet = new AiChangeSet
            {
                Version = 2,
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Name = "目标流程",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "main", Name = "主步骤",
                                Operations = new List<SemanticOperation> { new SemanticOperation { Kind = "wait", Milliseconds = 10 } }
                            }
                        }
                    },
                    new Automation.Protocol.ProcessDefinition
                    {
                        Name = "控制流程",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "main", Name = "主步骤",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation { Kind = "io.write", Io = "启动输出", State = true, AfterMs = 5 },
                                    new SemanticOperation { Kind = "io.wait", Io = "到位输入", State = true, TimeoutMs = 1000 },
                                    new SemanticOperation { Kind = "process.control", Process = "目标流程", Action = "start" },
                                    new SemanticOperation { Kind = "process.wait", Process = "目标流程", ExpectedState = "stopped", TimeoutMs = 2000 }
                                }
                            }
                        }
                    }
                }
            };
            AiChangeSetCompileResult resourceResult = AiChangeSetCompiler.Compile(
                resourceChangeSet,
                new List<Proc>(),
                new Dictionary<string, DicValue>(StringComparer.Ordinal),
                new AiResourceSnapshot(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["启动输出"] = "通用输出",
                    ["到位输入"] = "通用输入"
                }));
            Assert(resourceResult.Processes[1].steps[0].Ops[0] is IoOperate ioWrite
                && ioWrite.IoParams[0].IOName == "启动输出", "io.write编译结果不正确");
            Assert(resourceResult.Processes[1].steps[0].Ops[1] is IoCheck ioWait
                && ioWait.timeOutC.TimeOut == 1000, "io.wait编译结果不正确");
            Assert(resourceResult.Processes[1].steps[0].Ops[2] is ProcOps processControl
                && processControl.procParams[0].value == "运行", "process.control编译结果不正确");
            Assert(resourceResult.Processes[1].steps[0].Ops[3] is WaitProc processWait
                && processWait.Params[0].value == "停止", "process.wait编译结果不正确");

            changeSet.Variables[0].Policy = "create";
            bool rejected = false;
            try
            {
                AiChangeSetCompiler.Compile(changeSet, new List<Proc>(), variables);
            }
            catch (InvalidOperationException ex)
            {
                rejected = ex.Message.Contains("已存在");
            }
            Assert(rejected, "create策略没有严格拒绝同名变量");
        }

        private static void TestAiChangeSetCompleteCompilation()
        {
            var changeSet = new AiChangeSet
            {
                Version = 2,
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Name = "精确重建流程",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "main",
                                Name = "主步骤",
                                ExpectedOperaTypes = new List<string> { "延时", "跳转" },
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation
                                    {
                                        Key = "delay",
                                        Kind = "native.operation",
                                        OperaType = "延时",
                                        Fields = new Dictionary<string, object> { ["timeMiniSecond"] = "10" }
                                    },
                                    new SemanticOperation
                                    {
                                        Kind = "native.operation",
                                        OperaType = "跳转",
                                        Fields = new Dictionary<string, object>
                                        {
                                            ["DefaultGoto"] = new JObject
                                            {
                                                ["stepKey"] = "main",
                                                ["operationKey"] = "delay"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            AiChangeSetCompileResult complete = AiChangeSetCompiler.Compile(
                changeSet, new List<Proc>(), new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Assert(complete.OperationCount == 2
                && complete.Processes[0].steps[0].Ops[0] is Delay
                && complete.Processes[0].steps[0].Ops[1] is Goto jump
                && jump.Count == "0" && jump.DefaultGoto == "0-0-0",
                "完整变更未保留精确operaType或未归一化无条件跳转");

            var keyTargetChangeSet = new AiChangeSet
            {
                Version = 2,
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Name = "局部键跳转",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "main",
                                Name = "主步骤",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation
                                    {
                                        Kind = "native.operation",
                                        Key = "jump",
                                        OperaType = "跳转",
                                        Fields = new Dictionary<string, object>
                                        {
                                            ["DefaultGoto"] = new JObject
                                            {
                                                ["stepKey"] = "main",
                                                ["operationKey"] = "target"
                                            }
                                        }
                                    },
                                    new SemanticOperation
                                    {
                                        Kind = "native.operation",
                                        Key = "target",
                                        OperaType = "延时",
                                        Fields = new Dictionary<string, object> { ["timeMiniSecond"] = "1" }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            AiChangeSetCompileResult keyTargetResult = AiChangeSetCompiler.Compile(
                keyTargetChangeSet, new List<Proc>(), new Dictionary<string, DicValue>(),
                new AiResourceSnapshot());
            Assert(keyTargetResult.Processes[0].steps[0].Ops[0] is Goto keyJump
                && keyJump.DefaultGoto == "0-0-1", "新指令局部key没有在最终结构上解析跳转");

            var candidateResourceChangeSet = new AiChangeSet
            {
                Version = 2,
                Variables = new List<VariableChange>
                {
                    new VariableChange { Name = "接收结果", Type = "string", InitialValue = string.Empty }
                },
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Name = "候选资源校验",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "main",
                                Name = "主步骤",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation
                                    {
                                        Kind = "native.operation",
                                        OperaType = "接收TCP通讯消息",
                                        Fields = new Dictionary<string, object>
                                        {
                                            ["ID"] = "Tcp_1",
                                            ["MsgSaveValue"] = "接收结果",
                                            ["isConVert"] = false,
                                            ["TImeOut"] = 1000,
                                            ["AlarmType"] = "报警停止"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            var candidateReferences = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
            {
                ["comm.tcp"] = new[] { "Tcp_1" },
                ["comm.serial"] = Array.Empty<string>()
            };
            AiChangeSetCompileResult candidateResourceResult = AiChangeSetCompiler.Compile(
                candidateResourceChangeSet, new List<Proc>(), new Dictionary<string, DicValue>(),
                new AiResourceSnapshot(references: candidateReferences));
            Assert(candidateResourceResult.ChangedVariableCount == 1
                && candidateResourceResult.Processes[0].steps[0].Ops[0] is ReceoveTcpMsg,
                "同一ChangeSet声明的候选变量未贯穿通讯指令最终校验");

            changeSet.Processes[0].Steps[0].Operations[1].OperaType = "延时";
            changeSet.Processes[0].Steps[0].Operations[1].Fields =
                new Dictionary<string, object> { ["timeMiniSecond"] = "10" };
            AssertThrows<InvalidOperationException>(() => AiChangeSetCompiler.Compile(
                changeSet, new List<Proc>(), new Dictionary<string, DicValue>(), new AiResourceSnapshot()),
                "精确重建接受了错误顺序的原生指令类型");

            changeSet.Processes[0].Steps[0].ExpectedOperaTypes = null;
            changeSet.Processes[0].Steps[0].Operations = Enumerable.Range(0, 21)
                .Select(_ => new SemanticOperation
                {
                    Kind = "native.operation",
                    OperaType = "延时",
                    Fields = new Dictionary<string, object> { ["timeMiniSecond"] = "1" }
                }).ToList();
            AiChangeSetCompileResult largeChangeSet = AiChangeSetCompiler.Compile(
                changeSet, new List<Proc>(), new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Assert(largeChangeSet.OperationCount == 21, "完整变更仍然限制累计指令数量");

            changeSet.Processes = Enumerable.Range(0, 4)
                .Select(processIndex => new Automation.Protocol.ProcessDefinition
                {
                    Name = $"批量流程{processIndex}",
                    Steps = Enumerable.Range(0, 11).Select(stepIndex => new StepDefinition
                    {
                        Key = $"step_{stepIndex}",
                        Name = $"步骤{stepIndex}",
                        Operations = new List<SemanticOperation>
                        {
                            new SemanticOperation
                            {
                                Kind = "native.operation",
                                OperaType = "延时",
                                Fields = new Dictionary<string, object> { ["timeMiniSecond"] = "1" }
                            }
                        }
                    }).ToList()
                }).ToList();
            AiChangeSetCompileResult multiProcessChangeSet = AiChangeSetCompiler.Compile(
                changeSet, new List<Proc>(), new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Assert(multiProcessChangeSet.CreatedProcessCount == 4
                && multiProcessChangeSet.OperationCount == 44,
                "完整变更仍然限制流程数或单流程步骤数");

            Proc deletedFirst = CreateProc(new Delay { timeMiniSecond = "1" });
            deletedFirst.head.Name = "删除项";
            Proc shiftedSecond = CreateProc(new Goto
            {
                Params = new CustomList<GotoParam>(), Count = "0", DefaultGoto = "1-0-0"
            });
            shiftedSecond.head.Name = "原流程1";
            Proc shiftedThird = CreateProc(new Goto
            {
                Params = new CustomList<GotoParam>(), Count = "0", DefaultGoto = "2-0-0"
            });
            shiftedThird.head.Name = "原流程2";
            var deleteAndReindex = new AiChangeSet
            {
                Version = 2,
                DeleteProcesses = new ProcessDeleteSelection
                {
                    Mode = "selected",
                    ProcIds = new List<string> { deletedFirst.head.Id.ToString("D") }
                }
            };
            AiChangeSetCompileResult reindexed = AiChangeSetCompiler.Compile(
                deleteAndReindex, new List<Proc> { deletedFirst, shiftedSecond, shiftedThird },
                new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Assert(((Goto)reindexed.Processes[0].steps[0].Ops[0]).DefaultGoto == "0-0-0"
                && ((Goto)reindexed.Processes[1].steps[0].Ops[0]).DefaultGoto == "1-0-0",
                "删除前置流程后剩余流程的物理procIndex没有统一重写");
        }

        private static void TestOperationDefinitionRegistry()
        {
            IReadOnlyList<OperationType> first = OperationDefinitionRegistry.CreateAll();
            IReadOnlyList<OperationType> second = OperationDefinitionRegistry.CreateAll();
            Assert(first.Count == 45, $"指令注册数量错误：{first.Count}");
            Assert(first.Select(item => item.OperaType).Distinct(StringComparer.Ordinal).Count() == first.Count,
                "指令注册表包含重复OperaType");
            Assert(!ReferenceEquals(first[0], second[0]), "指令注册表返回了共享可变模板实例");
            OperationType delay = OperationDefinitionRegistry.Create(first.OfType<Delay>().Single().OperaType);
            Assert(delay is Delay, "按OperaType创建指令类型错误");
            Assert(first.OfType<PlcReadWrite>().Count() == 1 && first.OfType<PlcMappingControl>().Count() == 1,
                "PLC读写或映射控制指令未唯一注册");
            JObject capabilities = AiOperationCompilerRegistry.BuildCapabilities();
            Assert(capabilities["operationKinds"] is JArray kinds && kinds.Count == 16
                && kinds.Values<string>().Contains("native.operation", StringComparer.Ordinal)
                && kinds.Values<string>().Contains("config.placeholder", StringComparer.Ordinal)
                && kinds.Values<string>().Contains("variable.compute", StringComparer.Ordinal)
                && kinds.Values<string>().Contains("branch.number_compare", StringComparer.Ordinal)
                && kinds.Values<string>().Contains("flow.end", StringComparer.Ordinal)
                && capabilities["nativeOperation"]?["contractTool"]?.Value<string>() == "get_native_operation_schemas"
                && capabilities["changeActions"] is JArray,
                "语义能力目录未由编译适配器正确生成");
            Assert(capabilities["processDeletion"]?["modes"] is JArray deletionModes
                && deletionModes.Values<string>().SequenceEqual(new[] { "all", "selected" })
                && capabilities["processDeletion"]?["selected"]?["minimumSelectors"]?.Value<int>() == 1,
                "变更能力目录没有完整发布流程删除模式及选择规则");
            JObject contracts = AiOperationCompilerRegistry.BuildContracts(new[] { "wait", "flow.goto", "flow.end", "popup.message", "popup.variable" });
            Assert(contracts["contracts"]?["wait"]?["saveRequired"] is JArray,
                "语义指令契约未由编译适配器发布");
            Assert(contracts["contracts"]?["popup.message"]?["interpolation"]?.Value<string>() == "unsupported"
                && contracts["contracts"]?["popup.variable"]?["messageSource"]?.Value<string>() == "variable.currentValue",
                "弹框固定文本与变量来源契约未明确区分");
            Assert(contracts["contracts"]?["flow.end"]?["terminationReason"]?.Value<string>() == "Completed",
                "流程正常结束语义契约缺失");
        }

        private static void TestGooseEmbeddedResources()
        {
            string[] names = typeof(GooseRuntimeProvisioner).Assembly.GetManifestResourceNames();
            Assert(names.Contains("Automation.Assets.Goose.system.md", StringComparer.Ordinal),
                "程序集缺少内嵌System Prompt资源");
            Assert(names.Contains("Automation.Assets.Goose.automation.md", StringComparer.Ordinal),
                "程序集缺少内嵌Automation专用上下文资源");
            using (Stream stream = typeof(GooseRuntimeProvisioner).Assembly
                .GetManifestResourceStream("Automation.Assets.Goose.automation.md"))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("Automation上下文资源为空"), Encoding.UTF8))
            {
                string content = reader.ReadToEnd();
                Assert(content.Contains("preview_change_set") && !content.Contains("stage_changes")
                    && content.Contains("get_native_operation_schemas")
                    && content.Contains("run_proc_test") && content.Contains("operationKey")
                    && content.Contains("分多轮完成") && content.Contains("readinessStatus")
                    && content.Contains("资源依赖、控制流、输出与结束条件")
                    && content.Contains("discard_change_set_preview")
                    && content.Contains("结构有效、可运行、测试中已观察运行和自然完成")
                    && !content.Contains("保存为草稿"),
                    "内嵌Automation上下文不是当前V2版本");
            }
        }

        private static void TestAiChangeSetReplaceProcess()
        {
            Proc untouched = CreateProc(new Delay { timeMiniSecond = "10" });
            untouched.head.Name = "保留流程";
            Proc existing = CreateProc(new Delay { timeMiniSecond = "20" });
            existing.head.Name = "待修改流程";
            Guid existingId = existing.head.Id;
            var changeSet = new AiChangeSet
            {
                Version = 2,
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Action = "replace",
                        TargetProcId = existingId.ToString("D"),
                        Name = "修改后流程",
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                Key = "loop",
                                Name = "循环",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation { Key = "wait", Kind = "wait", Milliseconds = 100 },
                                    new SemanticOperation
                                    {
                                        Kind = "flow.goto",
                                        Target = new OperationTarget { StepKey = "loop", OperationKey = "wait" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            AiChangeSetCompileResult result = AiChangeSetCompiler.Compile(
                changeSet,
                new List<Proc> { untouched, existing },
                new Dictionary<string, DicValue>(StringComparer.Ordinal));
            Assert(result.CreatedProcessCount == 0 && result.ReplacedProcessCount == 1,
                "现有流程替换被错误统计为创建");
            Assert(result.Processes.Count == 2 && result.Processes[0].head.Name == "保留流程",
                "替换现有流程改变了流程数量或无关流程");
            Assert(result.Processes[1].head.Id == existingId && result.Processes[1].head.Name == "修改后流程",
                "替换现有流程未保留ID和索引");
            Assert(result.Processes[1].steps[0].Ops[1] is Goto jump
                && jump.DefaultGoto == "1-0-0", "替换流程的符号跳转未按原索引编译");
            Assert(result.Changes.OfType<JObject>().Any(change =>
                string.Equals(change["type"]?.Value<string>(), "process.replace", StringComparison.Ordinal)),
                "替换流程未生成确认差异");

            var sourceJump = new Goto
            {
                Id = Guid.NewGuid(),
                Name = "跳到目标",
                Params = new CustomList<GotoParam>(),
                Count = "0",
                DefaultGoto = "0-0-2"
            };
            var sourceMiddle = new Delay { Id = Guid.NewGuid(), Name = "中间", timeMiniSecond = "1" };
            var sourceTarget = new Delay { Id = Guid.NewGuid(), Name = "目标", timeMiniSecond = "1" };
            Proc stableExisting = CreateProc(sourceJump, sourceMiddle, sourceTarget);
            stableExisting.head.Name = "稳定插入流程";
            var stableInsert = new AiChangeSet
            {
                Version = 2,
                Processes = new List<Automation.Protocol.ProcessDefinition>
                {
                    new Automation.Protocol.ProcessDefinition
                    {
                        Action = "replace",
                        TargetProcId = stableExisting.head.Id.ToString("D"),
                        Name = stableExisting.head.Name,
                        Steps = new List<StepDefinition>
                        {
                            new StepDefinition
                            {
                                StepId = stableExisting.steps[0].Id.ToString("D"),
                                Key = "main",
                                Operations = new List<SemanticOperation>
                                {
                                    new SemanticOperation { OpId = sourceJump.Id.ToString("D") },
                                    new SemanticOperation
                                    {
                                        Key = "inserted",
                                        Kind = "native.operation",
                                        OperaType = "延时",
                                        Fields = new Dictionary<string, object> { ["timeMiniSecond"] = "5" }
                                    },
                                    new SemanticOperation { OpId = sourceMiddle.Id.ToString("D") },
                                    new SemanticOperation { OpId = sourceTarget.Id.ToString("D") }
                                }
                            }
                        }
                    }
                }
            };
            AiChangeSetCompileResult stableInsertResult = AiChangeSetCompiler.Compile(
                stableInsert, new List<Proc> { stableExisting },
                new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Proc insertedProc = stableInsertResult.Processes[0];
            Assert(insertedProc.steps[0].Id == stableExisting.steps[0].Id
                && insertedProc.steps[0].Ops[0].Id == sourceJump.Id
                && insertedProc.steps[0].Ops[2].Id == sourceMiddle.Id
                && insertedProc.steps[0].Ops[3].Id == sourceTarget.Id,
                "稳定插入没有保留既有步骤或指令ID");
            Assert(insertedProc.steps[0].Ops[0] is Goto rewritten
                && rewritten.DefaultGoto == "0-0-3",
                "插入指令后既有跳转没有按目标opId自动重写");
            Assert(stableInsertResult.Changes.OfType<JObject>().Any(change =>
                change["rewrittenGotoCount"]?.Value<int>() == 1),
                "预演差异没有报告自动重写的跳转数量");
        }

        private static void TestAiAtomicChangeActions()
        {
            var jump = new Goto
            {
                Id = Guid.NewGuid(),
                Name = "跳到目标",
                Params = new CustomList<GotoParam>(),
                Count = "0",
                DefaultGoto = "0-0-2"
            };
            var middle = new Delay
            {
                Id = Guid.NewGuid(), Name = "中间", timeMiniSecond = "10",
                timeMiniSecondV = "旧延时变量", AlarmType = "报警忽略"
            };
            var target = new Delay { Id = Guid.NewGuid(), Name = "目标", timeMiniSecond = "20" };
            Proc existing = CreateProc(jump, middle, target);
            existing.head.Name = "局部编辑流程";
            Guid stepId = existing.steps[0].Id;

            var insertStage = new AiChangeSet
            {
                Version = 2,
                Title = "在目标前插入延时",
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.insert",
                        TargetProcess = new ProcessSelector { ProcId = existing.head.Id.ToString("D") },
                        TargetStep = new StepSelector { StepId = stepId.ToString("D") },
                        Position = new ChangePosition { BeforeId = target.Id.ToString("D") },
                        Operation = new SemanticOperation
                        {
                            Key = "inserted_delay",
                            Kind = "wait",
                            Milliseconds = 5
                        }
                    }
                }
            };
            AiChangeSetCompileResult inserted = AiChangeSetCompiler.Compile(insertStage,
                new List<Proc> { existing }, new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Proc insertedProc = inserted.Processes[0];
            Assert(inserted.ReplacedProcessCount == 1 && insertedProc.steps[0].Ops.Count == 4,
                "局部插入动作没有形成单流程原子替换");
            Assert(inserted.AtomicActionCount == 1 && inserted.Changes.OfType<JObject>().Any(change =>
                string.Equals(change["type"]?.Value<string>(), "process.modify", StringComparison.Ordinal)),
                "原子动作预演仍暴露为完整流程替换");
            Assert(insertedProc.steps[0].Ops[0] is Goto rewritten && rewritten.DefaultGoto == "0-0-3",
                "局部插入后既有跳转没有按目标指令ID重写");
            Assert(insertedProc.steps[0].Ops[1].Id == middle.Id
                && insertedProc.steps[0].Ops[3].Id == target.Id,
                "局部插入改变了无关指令身份");

            var updateStage = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.update",
                        TargetProcess = new ProcessSelector { ProcId = insertedProc.head.Id.ToString("D") },
                        TargetOperation = new OperationSelector { OpId = middle.Id.ToString("D") },
                        Operation = new SemanticOperation
                        {
                            Kind = "native.operation",
                            OperaType = "延时",
                            Fields = new Dictionary<string, object> { ["timeMiniSecond"] = "50" },
                            ClearFields = new List<string> { "timeMiniSecondV" }
                        }
                    }
                }
            };
            AiChangeSetCompileResult updated = AiChangeSetCompiler.Compile(updateStage,
                inserted.Processes, new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Delay updatedDelay = (Delay)updated.Processes[0].steps[0].Ops[1];
            Assert(updatedDelay.Id == middle.Id && updatedDelay.Name == "中间"
                && updatedDelay.timeMiniSecond == "50" && updatedDelay.timeMiniSecondV == null
                && updatedDelay.AlarmType == "报警忽略",
                "原生指令局部更新没有清空指定旧字段、保留其他字段或稳定ID");

            var linkedAppendStage = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { ProcId = existing.head.Id.ToString("D") },
                        TargetStep = new StepSelector { StepId = stepId.ToString("D") },
                        Operation = new SemanticOperation
                        {
                            Key = "linked_a",
                            Kind = "flow.goto",
                            Target = new OperationTarget
                            {
                                StepId = stepId.ToString("D"),
                                OperationKey = "linked_b"
                            }
                        }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { ProcId = existing.head.Id.ToString("D") },
                        TargetStep = new StepSelector { StepId = stepId.ToString("D") },
                        Operation = new SemanticOperation
                        {
                            Key = "linked_b",
                            Kind = "flow.goto",
                            Target = new OperationTarget
                            {
                                StepId = stepId.ToString("D"),
                                OperationKey = "linked_a"
                            }
                        }
                    }
                }
            };
            AiChangeSetCompileResult linked = AiChangeSetCompiler.Compile(linkedAppendStage,
                updated.Processes, new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Assert(linked.Processes[0].steps[0].Ops[4] is Goto linkedA
                && linkedA.DefaultGoto == "0-0-5"
                && linked.Processes[0].steps[0].Ops[5] is Goto linkedB
                && linkedB.DefaultGoto == "0-0-4",
                "既有stepId内的本阶段新指令没有正确解析前向和循环引用");
            JArray linkedMappings = (JArray)linked.CreatedObjects["operations"];
            Assert(linkedMappings.Count == 2
                && linkedMappings.All(item => string.Equals(
                    item["stepId"]?.Value<string>(), stepId.ToString("D"), StringComparison.Ordinal)),
                "新增指令没有返回局部key到稳定opId的映射");

            var createStage = new AiChangeSet
            {
                Version = 2,
                Title = "创建独立流程",
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "process.create",
                        Process = new ProcessActionValue { Key = "new_proc", Name = "分阶段新流程" }
                    },
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { Key = "new_proc" },
                        Step = new StepActionValue { Key = "main", Name = "主步骤" }
                    },
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { Key = "new_proc" },
                        TargetStep = new StepSelector { Key = "main" },
                        Operation = new SemanticOperation
                        {
                            Key = "wait_once",
                            Kind = "wait",
                            Milliseconds = 100
                        }
                    }
                }
            };
            AiChangeSetCompileResult created = AiChangeSetCompiler.Compile(createStage,
                updated.Processes, new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Assert(created.CreatedProcessCount == 1 && created.Processes.Count == 2
                && created.Processes[1].head.Name == "分阶段新流程"
                && created.Processes[1].steps[0].Ops.Count == 1,
                "阶段内key没有正确组合流程、步骤和指令动作");
            Assert(((JArray)created.CreatedObjects["processes"]).Count == 1
                && ((JArray)created.CreatedObjects["steps"]).Count == 1
                && ((JArray)created.CreatedObjects["operations"]).Count == 1,
                "新建流程提交结果缺少流程、步骤或指令的稳定ID映射");

            AssertThrows<InvalidOperationException>(() => AiChangeSetCompiler.Compile(
                new AiChangeSet
                {
                    Version = 2,
                    Actions = new List<ChangeSetAction>
                    {
                        new ChangeSetAction
                        {
                            Type = "operation.insert",
                            TargetProcess = new ProcessSelector { ProcId = existing.head.Id.ToString("D") },
                            TargetStep = new StepSelector { StepId = stepId.ToString("D") },
                            Position = new ChangePosition { BeforeId = target.Id.ToString("D") },
                            Operation = new SemanticOperation { Key = "unknown" }
                        }
                    }
                }, new List<Proc> { existing }, new Dictionary<string, DicValue>()),
                "缺少指令kind的动作没有被严格拒绝");
        }

        private static void TestAiDraftProcessReadiness()
        {
            Proc invalidModify = CreateProc(new ModifyValue
            {
                Name = "冲突修改变量",
                ModifyType = "叠加",
                ValueSourceName = "计数",
                ChangeValue = "1",
                ChangeValueName = "增量",
                OutputValueName = "计数"
            });
            ProcessReadinessAnalysis invalidModifyReadiness =
                ProcessReadinessService.Analyze(0, invalidModify, new[] { invalidModify });
            Assert(!invalidModifyReadiness.Runnable
                && invalidModifyReadiness.RunBlockers.Any(item => item.Contains("不能同时配置")),
                "修改变量的互斥字段冲突没有在启动前被拦截");

            var emptyProcessStage = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "process.create",
                        Process = new ProcessActionValue { Name = "分阶段草稿" }
                    }
                }
            };
            AiChangeSetCompileResult emptyProcess = AiChangeSetCompiler.Compile(
                emptyProcessStage, new List<Proc>(), new Dictionary<string, DicValue>(), new AiResourceSnapshot());
            Assert(emptyProcess.Processes.Count == 1 && emptyProcess.Processes[0].steps.Count == 0,
                "空流程未能作为独立阶段保存");
            Assert(emptyProcess.ReadinessStatus == "incomplete" && !emptyProcess.Runnable
                && emptyProcess.RunBlockers.Count > 0,
                "空流程没有返回草稿状态和运行阻断原因");

            Guid procId = emptyProcess.Processes[0].head.Id;
            var emptyStepStage = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "step.append",
                        TargetProcess = new ProcessSelector { ProcId = procId.ToString("D") },
                        Step = new StepActionValue { Name = "主步骤" }
                    }
                }
            };
            AiChangeSetCompileResult emptyStep = AiChangeSetCompiler.Compile(
                emptyStepStage, emptyProcess.Processes, emptyProcess.Variables, new AiResourceSnapshot());
            Assert(emptyStep.Processes[0].steps.Count == 1 && emptyStep.Processes[0].steps[0].Ops.Count == 0,
                "空步骤未能作为后续独立阶段保存");
            Assert(emptyStep.ReadinessStatus == "incomplete" && !emptyStep.Runnable,
                "空步骤没有保持草稿状态");

            Guid stepId = emptyStep.Processes[0].steps[0].Id;
            var completeStage = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { ProcId = procId.ToString("D") },
                        TargetStep = new StepSelector { StepId = stepId.ToString("D") },
                        Operation = new SemanticOperation
                        {
                            Kind = "wait",
                            Milliseconds = 1
                        }
                    }
                }
            };
            AiChangeSetCompileResult complete = AiChangeSetCompiler.Compile(
                completeStage, emptyStep.Processes, emptyStep.Variables, new AiResourceSnapshot());
            Assert(complete.ReadinessStatus == "ready" && complete.Runnable
                && complete.RunBlockers.Count == 0,
                "补齐指令后流程未恢复可运行状态");

            var pendingJumpStage = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.insert",
                        TargetProcess = new ProcessSelector { ProcId = procId.ToString("D") },
                        TargetStep = new StepSelector { StepId = stepId.ToString("D") },
                        Position = new ChangePosition
                        {
                            BeforeId = complete.Processes[0].steps[0].Ops[0].Id.ToString("D")
                        },
                        Operation = new SemanticOperation
                        {
                            Key = "jump_to_future",
                            Kind = "flow.goto",
                            Target = new OperationTarget { OperationKey = "future_target" }
                        }
                    }
                }
            };
            AiChangeSetCompileResult pendingJump = AiChangeSetCompiler.Compile(
                pendingJumpStage, complete.Processes, complete.Variables, new AiResourceSnapshot());
            Assert(pendingJump.ReadinessStatus == "incomplete" && !pendingJump.Runnable
                && ((Goto)pendingJump.Processes[0].steps[0].Ops[0]).DefaultGoto
                    .StartsWith(ProcessDefinitionService.PendingGotoPrefix, StringComparison.Ordinal),
                "尚未创建的符号跳转目标没有保存为可继续完善的草稿");

            var resolveJumpStage = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { ProcId = procId.ToString("D") },
                        TargetStep = new StepSelector { StepId = stepId.ToString("D") },
                        Operation = new SemanticOperation
                        {
                            Key = "future_target",
                            Kind = "wait",
                            Milliseconds = 1
                        }
                    }
                }
            };
            AiChangeSetCompileResult resolvedJump = AiChangeSetCompiler.Compile(
                resolveJumpStage, pendingJump.Processes, pendingJump.Variables, new AiResourceSnapshot());
            Assert(((Goto)resolvedJump.Processes[0].steps[0].Ops[0]).DefaultGoto == "0-0-2"
                && resolvedJump.ReadinessStatus == "ready" && resolvedJump.Runnable,
                "后续阶段创建目标后未自动解析既有符号跳转");

            var unresolvedProcessStage = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { ProcId = procId.ToString("D") },
                        TargetStep = new StepSelector { StepId = stepId.ToString("D") },
                        Operation = new SemanticOperation { Kind = "process.control" }
                    }
                }
            };
            AiChangeSetCompileResult unresolvedProcess = AiChangeSetCompiler.Compile(
                unresolvedProcessStage, resolvedJump.Processes, resolvedJump.Variables,
                new AiResourceSnapshot());
            Assert(unresolvedProcess.ReadinessStatus == "incomplete" && !unresolvedProcess.Runnable
                && unresolvedProcess.RunBlockers.OfType<JObject>()
                    .Any(item => item["message"]?.ToString().Contains("目标流程") == true),
                "未确定目标流程的控制指令没有保存为未完成配置："
                + unresolvedProcess.ReadinessStatus + "/" + unresolvedProcess.Runnable + "/"
                + string.Join("|", unresolvedProcess.RunBlockers.OfType<JObject>()
                    .Select(item => item["message"]?.ToString())));

            var placeholderStage = new AiChangeSet
            {
                Version = 2,
                Actions = new List<ChangeSetAction>
                {
                    new ChangeSetAction
                    {
                        Type = "operation.append",
                        TargetProcess = new ProcessSelector { ProcId = procId.ToString("D") },
                        TargetStep = new StepSelector { StepId = stepId.ToString("D") },
                        Operation = new SemanticOperation
                        {
                            Key = "unresolved_target",
                            Kind = "config.placeholder",
                            Message = "等待用户确认目标流程"
                        }
                    }
                }
            };
            AiChangeSetCompileResult placeholder = AiChangeSetCompiler.Compile(
                placeholderStage, resolvedJump.Processes, resolvedJump.Variables, new AiResourceSnapshot());
            OperationType placeholderOperation = placeholder.Processes[0].steps[0].Ops.Last();
            Assert(ProcessReadinessService.IsPlaceholder(placeholderOperation)
                && placeholder.ReadinessStatus == "incomplete" && !placeholder.Runnable,
                "配置占位没有被持久识别并阻止流程启动");
        }

        private static void TestAiConfigurationTransaction()
        {
            string configPath = Path.Combine(Path.GetTempPath(), "AutomationAiTransaction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(configPath, "Work"));
            File.WriteAllText(Path.Combine(configPath, "Work", "old.txt"), "old");
            File.WriteAllText(Path.Combine(configPath, "value.json"), "old-value");
            try
            {
                var variables = new Dictionary<string, DicValue>(StringComparer.Ordinal)
                {
                    ["计数"] = new DicValue
                    {
                        Index = 3,
                        Name = "计数",
                        Type = "double",
                        ConfigValue = "1",
                        Value = "1"
                    }
                };
                Assert(AiConfigurationTransaction.Commit(
                    configPath,
                    new List<Proc> { CreateProc(new Delay { timeMiniSecond = "10" }) },
                    variables,
                    out string commitError,
                    out bool rollbackFailed), $"联合事务提交失败：{commitError}");
                Assert(!rollbackFailed, "成功事务不应报告回滚失败");
                Assert(File.Exists(Path.Combine(configPath, "Work", "0.json")), "流程目录未提交");
                Assert(File.ReadAllText(Path.Combine(configPath, "value.json")).Contains("计数"), "变量文件未提交");

                string transactionPath = Path.Combine(configPath, ".change-set-transaction-crash");
                Directory.CreateDirectory(Path.Combine(transactionPath, "Work.old"));
                File.WriteAllText(Path.Combine(transactionPath, "Work.old", "restored.txt"), "restored-work");
                File.WriteAllText(Path.Combine(transactionPath, "value.old.json"), "restored-value");
                File.WriteAllText(Path.Combine(transactionPath, "manifest.json"),
                    "{\"WorkExisted\":true,\"ValueExisted\":true}");

                Assert(AiConfigurationTransaction.RecoverPendingTransactions(configPath, out string recoveryError),
                    $"联合事务恢复失败：{recoveryError}");
                Assert(File.Exists(Path.Combine(configPath, "Work", "restored.txt")), "中断后旧流程目录未恢复");
                Assert(File.ReadAllText(Path.Combine(configPath, "value.json")) == "restored-value",
                    "中断后旧变量文件未恢复");
                Assert(!Directory.Exists(transactionPath), "恢复后的事务目录未清理");
            }
            finally
            {
                if (Directory.Exists(configPath)) Directory.Delete(configPath, true);
            }
        }

        private static void TestTerminationReason()
        {
            Proc finite = CreateProc(new Delay { timeMiniSecond = "1" });
            Proc loop = CreateProc(new Goto
            {
                Count = "0",
                Params = new OperationTypePartial.CustomList<GotoParam>(),
                DefaultGoto = "0-0-0"
            });

            ValueConfigStore values = CreateValueStore(ResetStatus.ResetCompleted);
            using (ProcessEngine engine = CreateEngine(values, new CustomFunc(), finite))
            {
                Assert(engine.StartProc(finite, 0), "有限流程启动失败");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 2000, "有限流程未结束");
                Assert(engine.GetSnapshot(0)?.TerminationReason == ProcTerminationReason.Completed,
                    "自然结束没有记录Completed终止原因");
            }

            int afterEndInvocationCount = 0;
            CustomFunc endFunctions = new CustomFunc();
            endFunctions.RegisterFunction("after-end", () => Interlocked.Increment(ref afterEndInvocationCount));
            Proc explicitEnd = CreateProc(
                new EndProcess(),
                new CallCustomFunc { Name = "after-end" });
            using (ProcessEngine engine = CreateEngine(values, endFunctions, explicitEnd))
            {
                Assert(engine.StartProc(explicitEnd, 0), "显式结束流程启动失败");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 2000,
                    "流程结束指令没有正常结束当前流程");
                Assert(engine.GetSnapshot(0)?.TerminationReason == ProcTerminationReason.Completed
                    && Volatile.Read(ref afterEndInvocationCount) == 0,
                    "流程结束指令没有以Completed终止，或错误执行了后续指令");
            }

            using (ProcessEngine engine = CreateEngine(values, new CustomFunc(), loop))
            {
                Assert(engine.StartProc(loop, 0), "循环流程启动失败");
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Running, 1000, "循环流程未进入运行状态");
                engine.Stop(0, ProcTerminationReason.TestWindowElapsed);
                WaitUntil(() => engine.GetSnapshot(0)?.State == ProcRunState.Stopped, 2000, "循环流程测试停止失败");
                Assert(engine.GetSnapshot(0)?.TerminationReason == ProcTerminationReason.TestWindowElapsed,
                    "测试窗口停止被错误标记为自然完成");
            }
        }

        private static void TestAiMarkdownNormalization()
        {
            MethodInfo normalize = typeof(FrmAiAssistant).GetMethod(
                "NormalizeMarkdownForRendering", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(normalize != null, "未找到AI回复Markdown归一化入口");
            const string malformed =
                "## 操作完成报告###1 删除流程- **已删除流程：** `旧流程`\n"
                + "###2 创建变量|项目 |内容 |\n"
                + "|------|------|\n"
                + "|变量名|计数|";
            string normalized = (string)normalize.Invoke(null, new object[] { malformed });
            Assert(normalized.Contains("## 操作完成报告\n### 1 删除流程\n\n- **已删除流程：** `旧流程`"),
                "粘连标题与列表项未正确拆分");
            Assert(normalized.Contains("### 2 创建变量\n\n|项目 |内容 |\n|------|------|"),
                "粘连标题与表格未正确拆分");

            MethodInfo buildFlowCards = typeof(FrmAiAssistant).GetMethod(
                "BuildAutomationFlowCardsHtml", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(buildFlowCards != null, "未找到结构化流程卡片渲染入口");
            const string visualizationJson = "[{\"action\":\"create\",\"name\":\"计数循环\",\"steps\":["
                + "{\"key\":\"loop\",\"name\":\"循环\",\"operations\":["
                + "{\"kind\":\"variable.add\",\"variable\":\"计数\",\"amount\":1},"
                + "{\"kind\":\"branch.number_range\",\"variable\":\"计数\",\"min\":3,\"max\":9999,"
                + "\"whenTrue\":{\"step\":\"done\",\"operation\":0},\"whenFalse\":{\"step\":\"loop\",\"operation\":0}}]},"
                + "{\"key\":\"done\",\"name\":\"完成\",\"operations\":["
                + "{\"kind\":\"popup.variable\",\"variable\":\"计数\",\"autoCloseMs\":2000}]}]}]";
            string flowHtml = (string)buildFlowCards.Invoke(null, new object[] { visualizationJson });
            Assert(flowHtml.Contains("automation-flow-visual")
                && flowHtml.Contains("计数循环")
                && flowHtml.Contains("满足 → done · 1")
                && flowHtml.Contains("不满足 → loop · 1")
                && flowHtml.Contains("含回环")
                && flowHtml.Contains("显示变量“计数”当前值 &#183; 2000 ms后关闭"),
                "结构化流程卡片缺少步骤、分支、回环或弹框语义");

            MethodInfo buildReadFlow = typeof(FrmAiAssistant).GetMethod(
                "BuildReadFlowVisualization", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(buildReadFlow != null, "未找到现有流程可视化转换入口");
            var overview = new JObject
            {
                ["procIndex"] = 0,
                ["name"] = "持续心跳",
                ["state"] = "Stopped",
                ["steps"] = new JArray
                {
                    new JObject
                    {
                        ["stepIndex"] = 0,
                        ["name"] = "心跳循环",
                        ["ops"] = new JArray
                        {
                            new JObject
                            {
                                ["name"] = "等待500ms",
                                ["operaType"] = "延时",
                                ["summary"] = "等待500ms，[延时]"
                            }
                        }
                    },
                    new JObject
                    {
                        ["stepIndex"] = 1,
                        ["name"] = "持续循环",
                        ["ops"] = new JArray
                        {
                            new JObject
                            {
                                ["name"] = "回心跳",
                                ["operaType"] = "跳转",
                                ["summary"] = "回心跳，[跳转]，默认跳转=0-0-0"
                            }
                        }
                    }
                }
            };
            JObject readFlow = (JObject)buildReadFlow.Invoke(null, new object[] { overview });
            string readFlowHtml = (string)buildFlowCards.Invoke(null,
                new object[] { new JArray(readFlow).ToString(Newtonsoft.Json.Formatting.None) });
            Assert(readFlowHtml.Contains("持续心跳")
                && readFlowHtml.Contains("现有")
                && readFlowHtml.Contains("等待500ms")
                && readFlowHtml.Contains("回心跳")
                && !readFlowHtml.Contains("含回环")
                && !readFlowHtml.Contains("跳转 →"),
                "现有原生流程卡片仍在根据摘要猜测跳转或回环");
        }

        private static void TestAiSessionPickerTemplate()
        {
            FieldInfo templateField = typeof(FrmAiAssistant).GetField(
                "BaseConversationHtmlTemplate", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(templateField != null, "未找到AI助手页面模板");
            string template = (string)templateField.GetRawConstantValue();
            Assert(!template.Contains("sessionSelect") && !template.Contains("<select class=\"session-select\""),
                "历史会话仍使用容易挤压标题的原生下拉框");
            Assert(template.Contains("session-trigger-text")
                && template.Contains("session-trigger-icon")
                && template.Contains("session-menu")
                && template.Contains("overflow-wrap:anywhere"),
                "历史会话选择器未隔离标题、箭头或完整标题列表");
        }

        private static void TestGooseDeveloperShellFallback()
        {
            MethodInfo resolveShell = typeof(GooseAcpClient).GetMethod(
                "ResolveGooseDeveloperShellPath", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(resolveShell != null, "未找到Goose开发Shell解析入口");
            string shellPath = (string)resolveShell.Invoke(null, null);
            Assert(!string.IsNullOrWhiteSpace(shellPath) && File.Exists(shellPath),
                "Goose UTF-8 Shell适配器未随程序发布");
            Assert(string.Equals(Path.GetFileName(shellPath), "pwsh.exe", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(shellPath)), "GooseShell",
                    StringComparison.OrdinalIgnoreCase),
                "Goose未使用受管UTF-8 Shell适配器：" + shellPath);

            string tempDirectory = Path.Combine(Path.GetTempPath(), "自动化中文路径_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            string testFile = Path.Combine(tempDirectory, "流程说明.txt");
            File.WriteAllText(testFile, "系统复位检测", new UTF8Encoding(false));
            try
            {
                var realShells = new List<string> { null };
                string windowsPowerShell = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe");
                if (File.Exists(windowsPowerShell)) realShells.Add(windowsPowerShell);

                foreach (string realShell in realShells)
                {
                    string escapedPath = testFile.Replace("'", "''");
                    string command = "$f=Get-Item -LiteralPath '" + escapedPath
                        + "';$f.FullName;Get-Content -LiteralPath $f.FullName -Raw";
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = shellPath,
                        Arguments = "-NoProfile -NonInteractive -Command \"" + command.Replace("\"", "\\\"") + "\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    if (realShell != null)
                    {
                        startInfo.EnvironmentVariables["AUTOMATION_GOOSE_POWERSHELL"] = realShell;
                    }
                    using (Process process = Process.Start(startInfo))
                    using (var stdout = new MemoryStream())
                    using (var stderr = new MemoryStream())
                    {
                        Task stdoutCopy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
                        Task stderrCopy = process.StandardError.BaseStream.CopyToAsync(stderr);
                        process.WaitForExit();
                        Task.WaitAll(stdoutCopy, stderrCopy);
                        string output = new UTF8Encoding(false, true).GetString(stdout.ToArray());
                        string error = new UTF8Encoding(false, true).GetString(stderr.ToArray());
                        Assert(process.ExitCode == 0,
                            "Goose UTF-8 Shell执行失败：" + error);
                        Assert(output.Contains("自动化中文路径")
                            && output.Contains("流程说明.txt")
                            && output.Contains("系统复位检测"),
                            "Goose UTF-8 Shell中文路径或内容发生乱码：" + output);
                    }
                }
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        private static void TestProcDetailReadBoundaries()
        {
            Type serviceType = typeof(FrmAiAssistant).Assembly.GetType(
                "Automation.Bridge.AutomationBridgeService", true);
            FieldInfo detailLimitField = serviceType.GetField(
                "MaxDetailOperationCount", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo batchLimitField = serviceType.GetField(
                "MaxBatchReadOperationCount", BindingFlags.NonPublic | BindingFlags.Static);
            Assert((int)detailLimitField.GetRawConstantValue() == 100, "完整流程详情上限不是100条指令");
            Assert((int)batchLimitField.GetRawConstantValue() == 25, "批量指令详情上限不是25条指令");

            MethodInfo buildOmitted = serviceType.GetMethod(
                "BuildProcDetailOmitted", BindingFlags.NonPublic | BindingFlags.Static);
            JObject omitted = (JObject)buildOmitted.Invoke(null,
                new object[] { 3, CreateProc(new CallCustomFunc()), 101, null });
            Assert(omitted["detailAvailable"]?.Value<bool>() == false
                && omitted["operationCount"]?.Value<int>() == 101
                && omitted["steps"] is JArray steps
                && steps.Count == 1
                && steps[0]["ops"] == null,
                "超限流程未返回轻量步骤目录");

            object service = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(serviceType);
            MethodInfo getDetails = serviceType.GetMethod(
                "HandleGetOperationDetails", BindingFlags.NonPublic | BindingFlags.Instance);
            var tooManyIds = new JArray(Enumerable.Range(0, 26).Select(_ => Guid.NewGuid().ToString("D")));
            JObject tooMany = (JObject)getDetails.Invoke(service,
                new object[] { new JObject { ["procIndex"] = 0, ["opIds"] = tooManyIds } });
            Assert(tooMany["code"]?.Value<string>() == "INVALID_ARGUMENT"
                && tooMany["message"]?.Value<string>()?.Contains("1..25") == true,
                "批量读取未拒绝26条指令");

            string duplicateId = Guid.NewGuid().ToString("D");
            JObject duplicated = (JObject)getDetails.Invoke(service,
                new object[]
                {
                    new JObject
                    {
                        ["procIndex"] = 0,
                        ["opIds"] = new JArray(duplicateId, duplicateId)
                    }
                });
            Assert(duplicated["code"]?.Value<string>() == "INVALID_ARGUMENT"
                && duplicated["message"]?.Value<string>()?.Contains("不允许重复") == true,
                "批量读取未拒绝重复opId");

            JObject emptyId = (JObject)getDetails.Invoke(service,
                new object[]
                {
                    new JObject
                    {
                        ["procIndex"] = 0,
                        ["opIds"] = new JArray(Guid.Empty.ToString("D"))
                    }
                });
            Assert(emptyId["code"]?.Value<string>() == "INVALID_ARGUMENT"
                && emptyId["message"]?.Value<string>()?.Contains("空 Guid") == true,
                "批量读取未拒绝空opId");
        }

        private static void TestNestedReferenceIndexing()
        {
            Type serviceType = typeof(FrmAiAssistant).Assembly.GetType(
                "Automation.Bridge.AutomationBridgeService", true);
            Type recordType = serviceType.GetNestedType(
                "DiagnosticFieldRecord", BindingFlags.NonPublic);
            Type listType = typeof(List<>).MakeGenericType(recordType);
            MethodInfo addFields = serviceType.GetMethod(
                "AddDiagnosticFields", BindingFlags.NonPublic | BindingFlags.Static);

            var ioOperation = new IoLogicGoto
            {
                Id = Guid.NewGuid(),
                IoParams = new CustomList<IoLogicGotoParam>
                {
                    new IoLogicGotoParam { IOName = "测试输入" }
                }
            };
            object ioFields = Activator.CreateInstance(listType);
            addFields.Invoke(null, new object[]
            {
                ioFields, 0, "流程", 0, Guid.NewGuid(), "步骤", 0,
                ioOperation, ioOperation, string.Empty, 0, new List<object>()
            });
            Assert(ContainsDiagnosticReference(ioFields, "IoParams[0].IOName", "io.all", "测试输入"),
                "未递归索引IO参数列表引用");

            var gotoOperation = new Goto
            {
                Id = Guid.NewGuid(),
                Params = new CustomList<GotoParam>
                {
                    new GotoParam { Goto = "0-3-7" }
                }
            };
            object gotoFields = Activator.CreateInstance(listType);
            addFields.Invoke(null, new object[]
            {
                gotoFields, 0, "流程", 0, Guid.NewGuid(), "步骤", 0,
                gotoOperation, gotoOperation, string.Empty, 0, new List<object>()
            });
            Assert(ContainsDiagnosticReference(gotoFields, "Params[0].Goto", "proc.goto", "0-3-7"),
                "未递归索引条件列表中的远距离跳转");

            var tcpOperation = new TcpOps
            {
                Id = Guid.NewGuid(),
                Params = new CustomList<TcpOpsParam>
                {
                    new TcpOpsParam { Name = "测试TCP" }
                }
            };
            object tcpFields = Activator.CreateInstance(listType);
            addFields.Invoke(null, new object[]
            {
                tcpFields, 0, "流程", 0, Guid.NewGuid(), "步骤", 0,
                tcpOperation, tcpOperation, string.Empty, 0, new List<object>()
            });
            Assert(ContainsDiagnosticReference(tcpFields, "Params[0].Name", "comm.tcp", "测试TCP"),
                "未递归索引TCP参数列表引用");
        }

        private static bool ContainsDiagnosticReference(object records, string field, string referenceType, string value)
        {
            foreach (object record in (System.Collections.IEnumerable)records)
            {
                Type type = record.GetType();
                if (string.Equals((string)type.GetProperty("Field").GetValue(record), field, StringComparison.Ordinal)
                    && string.Equals((string)type.GetProperty("ReferenceType").GetValue(record), referenceType, StringComparison.Ordinal)
                    && string.Equals((string)type.GetProperty("Value").GetValue(record), value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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
            foreach (OperationType operation in operations)
            {
                if (operation != null && operation.Id == Guid.Empty)
                {
                    operation.Id = Guid.NewGuid();
                }
            }
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
                string detail;
                try
                {
                    detail = ex.ToString();
                }
                catch
                {
                    detail = $"{ex.GetType().FullName}: {ex.Message}";
                }
                Console.WriteLine($"失败: {name} - {detail}");
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }
    }
}
