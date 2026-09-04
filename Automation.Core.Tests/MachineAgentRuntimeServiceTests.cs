using Automation.Protocol;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

// 模块：核心测试 / Machine Agent。
// 职责范围：验证稳定 ID 预演、现场事实冻结和前台确认执行边界。

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class MachineAgentRuntimeServiceTests
    {
        [TestMethod]
        public void 预演根据稳定ID和指令参数生成且不依赖显示名称()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateDelayProcess(out Delay target);
                var runtime = CreateRuntime(directory, process, out ProcessEngine engine);
                using (engine)
                {
                    JObject preview = runtime.MachineAgent.PreviewProcessEntry(Request(
                        process, target, MachineExecutionModes.SingleOperation));

                    Assert.IsTrue(preview.Value<bool>("executable"));
                    Assert.AreEqual(target.Id.ToString("D"),
                        preview["target"]?["operationId"]?.Value<string>());
                    Assert.AreEqual(target.DelayMs,
                        preview["target"]?["parameters"]?[nameof(Delay.DelayMs)]?.Value<int>());
                    Assert.AreEqual(false,
                        preview["evidenceBoundary"]?["nameBasedInferenceUsed"]?.Value<bool>());
                    Assert.AreEqual(true,
                        preview["evidenceBoundary"]?["operationTypeAndParametersIncluded"]?.Value<bool>());
                }
            }
        }

        [TestMethod]
        public void 状态历史接口按游标正序分页且JSON字段统一为camelCase()
        {
            using (var directory = new TemporaryDirectory())
            using (var history = new EquipmentStateHistoryService())
            {
                var runtime = new PlatformRuntime(directory.FullPath)
                {
                    StateHistory = history
                };
                DateTime start = DateTime.UtcNow.AddMinutes(-1);
                for (int index = 0; index < 650; index++)
                {
                    history.Append(new EquipmentStateHistoryEvent
                    {
                        ObservedAtUtc = start.AddMilliseconds(index),
                        TopologyRevision = 7,
                        EventType = EquipmentStateEventTypes.NodeStateChanged,
                        NodeId = "buffer-1",
                        NodeLabel = "缓存工位",
                        Aspect = EquipmentStateAspects.Estimated,
                        NewValue = "状态" + index,
                        Meaning = "状态" + index,
                        Quality = EquipmentStateQualities.Good,
                        Confidence = 1,
                        SourceKind = "io",
                        ResourceRef = "DO_BUFFER",
                        BindingId = "binding-" + index
                    });
                }

                JObject page = runtime.MachineAgent.BuildStateHistory(0, 120);
                JArray events = (JArray)page["events"];
                JObject firstEvent = (JObject)events.First;
                JObject baseline = (JObject)page["baseline"];

                Assert.AreEqual(120, events.Count);
                Assert.AreEqual(1, firstEvent.Value<long>("sequence"));
                Assert.AreEqual(120, events.Last.Value<long>("sequence"));
                Assert.AreEqual(650, page.Value<long>("latestSequence"));
                Assert.IsTrue(page.Value<bool>("truncated"));
                Assert.IsTrue(page.Value<bool>("baselineComplete"));
                Assert.AreEqual(0, page["sequenceGaps"]?.Count());
                Assert.IsFalse(page.Value<bool>("sequenceGapsTruncated"));
                Assert.AreEqual(0, baseline.Value<long>("sequence"));
                Assert.IsNotNull(baseline["nodeStates"]);
                Assert.IsNull(baseline["Sequence"]);
                Assert.IsNotNull(firstEvent["eventType"]);
                Assert.IsNotNull(firstEvent["observedAtUtc"]);
                Assert.IsNull(firstEvent["EventType"]);
                Assert.IsNull(firstEvent["ObservedAtUtc"]);

                JObject nextPage = runtime.MachineAgent.BuildStateHistory(120, 120);
                Assert.AreEqual(121, nextPage["events"].First.Value<long>("sequence"));
                Assert.AreEqual(240, nextPage["events"].Last.Value<long>("sequence"));
                Assert.AreEqual(120, nextPage["baseline"].Value<long>("sequence"));
            }
        }

        [TestMethod]
        public void 设备上下文分页不静默截断且节点技能使用明确camelCase契约()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                var definition = new EquipmentTopologyDefinition();
                Guid processId = Guid.NewGuid();
                Guid operationId = Guid.NewGuid();
                for (int index = 0; index < 85; index++)
                {
                    var node = new EquipmentTopologyNode
                    {
                        Id = "node-" + index.ToString("D3"),
                        Label = "节点 " + index.ToString("D3"),
                        Kind = "actuator",
                        ReviewState = index == 0 ? "confirmed" : "candidate",
                        Confidence = 1
                    };
                    if (index == 0)
                    {
                        node.StateBindings.Add(new EquipmentTopologyStateBinding
                        {
                            Id = "binding-001",
                            StateName = "已夹紧",
                            SourceKind = "io",
                            ResourceRef = "DO_CONTEXT_TEST",
                            Operator = "equals",
                            ExpectedValue = "true",
                            Meaning = "测试上下文状态绑定",
                            Priority = 10,
                            ReviewState = "confirmed",
                            Confidence = 1
                        });
                        node.Skills.Add(new EquipmentTopologySkillBinding
                        {
                            Id = "skill-context-test",
                            Name = "测试节点动作",
                            ProcessId = processId.ToString("D"),
                            OperationId = operationId.ToString("D"),
                            ExecutionMode = MachineExecutionModes.SingleOperation,
                            Objective = "验证设备上下文契约",
                            ExpectedOutcome = "上下文字段稳定",
                            Preconditions = new List<string>(),
                            ReviewState = "confirmed",
                            Confidence = 1
                        });
                    }
                    definition.Nodes.Add(node);
                }
                for (int index = 0; index < 165; index++)
                {
                    definition.Relations.Add(new EquipmentTopologyRelation
                    {
                        Id = "relation-" + index.ToString("D3"),
                        SourceNodeId = "node-000",
                        TargetNodeId = "node-001",
                        Layer = "physical",
                        Kind = "contains",
                        ReviewState = "candidate",
                        Confidence = 1
                    });
                }
                Assert.IsTrue(runtime.Stores.Topology.TryCommit(
                    directory.FullPath, definition, out string error), error);

                JObject first = runtime.MachineAgent.BuildContext(1, 0, 80, 0, 160);
                JObject topology = (JObject)first["topology"];
                Assert.AreEqual(80, topology["nodes"].Count());
                Assert.AreEqual(160, topology["relations"].Count());
                Assert.IsTrue(topology["nodeWindow"].Value<bool>("hasMore"));
                Assert.IsTrue(topology["relationWindow"].Value<bool>("hasMore"));
                JObject firstNode = (JObject)topology["nodes"].First;
                JObject skill = (JObject)firstNode["skills"].First;
                JObject binding = (JObject)firstNode["stateBindings"].First;
                Assert.AreEqual("skill-context-test", skill.Value<string>("skillId"));
                Assert.IsNotNull(skill["processId"]);
                Assert.IsNull(skill["Id"]);
                Assert.IsNull(skill["id"]);
                Assert.AreEqual("binding-001", binding.Value<string>("bindingId"));
                Assert.IsNull(binding["Id"]);

                JObject second = runtime.MachineAgent.BuildContext(1, 80, 80, 160, 160);
                Assert.AreEqual(5, second["topology"]?["nodes"]?.Count());
                Assert.AreEqual(5, second["topology"]?["relations"]?.Count());
                Assert.IsFalse(second["topology"]?["nodeWindow"]?.Value<bool>("hasMore") ?? true);
                Assert.IsFalse(second["topology"]?["relationWindow"]?.Value<bool>("hasMore") ?? true);
            }
        }

        [TestMethod]
        public void 前台确认后只执行目标指令并写入设备时间线()
        {
            using (var directory = new TemporaryDirectory())
            using (var history = new EquipmentStateHistoryService())
            {
                Proc process = CreateDelayProcess(out Delay target);
                var runtime = CreateRuntime(directory, process, out ProcessEngine engine);
                runtime.StateHistory = history;
                using (engine)
                using (var timeline = new EquipmentProcessTimelineService(
                    engine, runtime.Stores.Topology, history))
                {
                    runtime.ProcessTimeline = timeline;
                    JObject preview = runtime.MachineAgent.PreviewProcessEntry(Request(
                        process, target, MachineExecutionModes.SingleOperation));
                    JObject result = runtime.MachineAgent.ExecuteProcessEntry(
                        preview.Value<string>("previewId"));

                    Assert.IsTrue(result.Value<bool>("accepted"));
                    WaitForInactive(engine, TimeSpan.FromSeconds(2));
                    EquipmentStateHistoryEvent command = history.GetRecentWindow(20).Events
                        .Single(item => item.EventType == EquipmentStateEventTypes.MachineActionStarted);
                    Assert.AreEqual(target.Id.ToString("D"), command.OperationId);
                    Assert.AreEqual(MachineExecutionModes.SingleOperation, command.NewValue);
                    Assert.AreEqual(result.Value<string>("actionId"), command.ActionId);
                }
            }
        }

        [TestMethod]
        public void 预演后状态事实变化会拒绝执行且无引擎副作用()
        {
            using (var fixture = new PhysicalRuntimeFixture(DateTime.UtcNow))
            {
                fixture.SetExecutionMode(MachineExecutionModes.ContinueFlow);
                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });
                fixture.History.Append(new EquipmentStateHistoryEvent
                {
                    ObservedAtUtc = DateTime.UtcNow,
                    EventType = EquipmentStateEventTypes.SignalChanged,
                    Aspect = EquipmentStateAspects.Observed,
                    NewValue = "changed",
                    Quality = EquipmentStateQualities.Good,
                    Confidence = 1d,
                    SourceKind = "test"
                });

                MachineAgentControlException error = Assert.ThrowsExactly<MachineAgentControlException>(() =>
                    fixture.Runtime.MachineAgent.ExecuteProcessEntry(preview.Value<string>("previewId")));
                Assert.AreEqual("MACHINE_STATE_CHANGED", error.Code);
                Assert.IsTrue(fixture.Engine.GetSnapshot(0).State.IsInactive());
            }
        }

        [TestMethod]
        public void 外部交互动作必须通过已确认节点技能预演()
        {
            using (var fixture = new PhysicalRuntimeFixture(DateTime.UtcNow))
            {
                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessEntry(Request(
                    fixture.Process, fixture.Operation, MachineExecutionModes.SingleOperation));

                Assert.IsFalse(preview.Value<bool>("executable"));
                Assert.AreEqual("external_interaction", preview.Value<string>("operationEffect"));
                Assert.IsTrue(BlockingReasons(preview).Any(item => item.Contains("必须通过已确认节点技能 skillId")));
            }
        }

        [TestMethod]
        public void 无技能兼容入口禁止continueFlow越过后续副作用边界()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = CreateDelayProcess(out Delay target);
                var runtime = CreateRuntime(directory, process, out ProcessEngine engine);
                using (engine)
                {
                    JObject preview = runtime.MachineAgent.PreviewProcessEntry(Request(
                        process, target, MachineExecutionModes.ContinueFlow));
                    Assert.IsFalse(preview.Value<bool>("executable"));
                    Assert.IsTrue(BlockingReasons(preview).Any(item => item.Contains(
                        "兼容流程入口不能使用 continue_flow")));
                }
            }
        }

        [TestMethod]
        public void 技能预演从确认绑定解析全部动作语义且禁止覆盖模式()
        {
            using (var fixture = new PhysicalRuntimeFixture(DateTime.UtcNow))
            {
                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });

                Assert.IsTrue(preview.Value<bool>("executable"),
                    string.Join("；", BlockingReasons(preview)));
                Assert.AreEqual(fixture.Process.head.Id.ToString("D"),
                    preview["target"]?["procId"]?.Value<string>());
                Assert.AreEqual(fixture.Operation.Id.ToString("D"),
                    preview["target"]?["operationId"]?.Value<string>());
                Assert.AreEqual(MachineExecutionModes.SingleOperation, preview.Value<string>("mode"));
                Assert.AreEqual("切换测试输出", preview.Value<string>("objective"));
                Assert.AreEqual("测试输出切换为开启", preview.Value<string>("expectedOutcome"));

                MachineAgentControlException error = Assert.ThrowsExactly<MachineAgentControlException>(() =>
                    fixture.Runtime.MachineAgent.PreviewProcessEntry(
                        new MachineProcessEntryPreviewRequest
                        {
                            SkillId = fixture.SkillId,
                            Mode = MachineExecutionModes.ContinueFlow
                        }));
                Assert.AreEqual("MACHINE_SKILL_OVERRIDE_FORBIDDEN", error.Code);
            }
        }

        [TestMethod]
        public void 节点状态长期不变但持续成功观测时仍可预演()
        {
            DateTime oldObservation = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(12));
            using (var fixture = new PhysicalRuntimeFixture(oldObservation))
            {
                EquipmentNodeStateProjection initial = fixture.History.GetCurrentSnapshot().NodeStates.Single();
                fixture.Clock = DateTime.UtcNow;
                fixture.Perception.PollOnce();
                EquipmentNodePerceptionState current = fixture.Perception.GetCurrentSnapshot().NodeStates.Single();

                Assert.IsTrue(current.LastSuccessfulObservationAtUtc > initial.UpdatedAtUtc);
                Assert.IsTrue(DateTime.UtcNow - initial.UpdatedAtUtc > TimeSpan.FromSeconds(5));
                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });
                Assert.IsTrue(preview.Value<bool>("executable"),
                    string.Join("；", BlockingReasons(preview)));
            }
        }

        [TestMethod]
        public void 节点条件不会把过期历史状态当作实时事实()
        {
            using (var fixture = new PhysicalRuntimeFixture(
                DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(12))))
            {
                fixture.ReplacePreconditions(
                    "{\"kind\":\"node_quality\",\"nodeId\":\"$current\",\"operator\":\"equals\",\"value\":\"good\"}");

                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });

                Assert.IsFalse(preview.Value<bool>("executable"));
                Assert.IsFalse(preview["preconditionChecks"]?[0]?["evaluable"]?.Value<bool>() ?? true);
                StringAssert.Contains(
                    preview["preconditionChecks"]?[0]?["detail"]?.Value<string>() ?? string.Empty,
                    "现场读取已连续超过");
            }
        }

        [TestMethod]
        public void 无法机械求值的技能条件和已确认防呆关系都会阻塞()
        {
            using (var fixture = new PhysicalRuntimeFixture(DateTime.UtcNow))
            {
                fixture.ReplacePreconditions("吸附稳定后才允许动作");
                JObject skillBlocked = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });
                Assert.IsFalse(skillBlocked.Value<bool>("executable"));
                Assert.IsFalse(skillBlocked["preconditionChecks"]?[0]?["evaluable"]?.Value<bool>() ?? true);

                fixture.ReplacePreconditions("节点实时状态质量为 good", "流程处于非活动状态");
                fixture.AddUnresolvedInterlock();
                JObject relationBlocked = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });
                Assert.IsFalse(relationBlocked.Value<bool>("executable"));
                Assert.IsTrue(relationBlocked["relationChecks"]?[0]?["blocksExecution"]?.Value<bool>() == true);
                Assert.IsTrue(BlockingReasons(relationBlocked).Any(item => item.Contains("证据缺口")));
            }
        }

        [TestMethod]
        public void 可机械判定的requires与blocks关系按各自语义闸门()
        {
            using (var fixture = new PhysicalRuntimeFixture(DateTime.UtcNow))
            {
                const string currentStateIsOff =
                    "{\"kind\":\"node_state\",\"nodeId\":\"$source\",\"operator\":\"equals\",\"value\":\"关闭\"}";
                fixture.SetInterlock("requires", currentStateIsOff);
                JObject allowed = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });
                Assert.IsTrue(allowed.Value<bool>("executable"),
                    string.Join("；", BlockingReasons(allowed)));

                fixture.SetInterlock("blocks", currentStateIsOff);
                JObject blocked = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });
                Assert.IsFalse(blocked.Value<bool>("executable"));
                Assert.IsTrue(blocked["relationChecks"]?[0]?["blocksExecution"]?.Value<bool>() == true);
            }
        }

        [TestMethod]
        public void 执行瞬间感知停止会拒绝且不会向引擎下发动作()
        {
            using (var fixture = new PhysicalRuntimeFixture(DateTime.UtcNow))
            {
                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });
                Assert.IsTrue(preview.Value<bool>("executable"),
                    string.Join("；", BlockingReasons(preview)));
                fixture.Perception.Dispose();

                MachineAgentControlException error = Assert.ThrowsExactly<MachineAgentControlException>(() =>
                    fixture.Runtime.MachineAgent.ExecuteProcessEntry(preview.Value<string>("previewId")));
                Assert.AreEqual("MACHINE_LIVE_GUARD_FAILED", error.Code);
                Assert.AreEqual(0, fixture.Io.BatchCalls);
                Assert.IsFalse(fixture.History.GetRecentWindow(100).Events.Any(item =>
                    item.EventType == EquipmentStateEventTypes.MachineActionStarted));
            }
        }

        [TestMethod]
        public void 已确认技能按批准模式执行并进入动作结果时间线()
        {
            using (var fixture = new PhysicalRuntimeFixture(DateTime.UtcNow))
            {
                JObject preview = fixture.Runtime.MachineAgent.PreviewProcessEntry(
                    new MachineProcessEntryPreviewRequest { SkillId = fixture.SkillId });
                JObject result = fixture.Runtime.MachineAgent.ExecuteProcessEntry(
                    preview.Value<string>("previewId"));

                Assert.IsTrue(result.Value<bool>("accepted"));
                Assert.AreEqual(fixture.SkillId, result.Value<string>("skillId"));
                Assert.AreEqual(MachineExecutionModes.SingleOperation, result.Value<string>("mode"));
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.Value<string>("actionId")));
                MachineAgentControlException consumed = Assert.ThrowsExactly<MachineAgentControlException>(() =>
                    fixture.Runtime.MachineAgent.ExecuteProcessEntry(preview.Value<string>("previewId")));
                Assert.AreEqual("MACHINE_PREVIEW_NOT_FOUND", consumed.Code);
                WaitForInactive(fixture.Engine, TimeSpan.FromSeconds(2));
                Assert.AreEqual(1, fixture.Io.BatchCalls);
                bool completed = SpinWait.SpinUntil(() => fixture.History.GetRecentWindow(100).Events.Any(item =>
                    item.ActionId == result.Value<string>("actionId")
                    && item.EventType == EquipmentStateEventTypes.MachineActionCompleted),
                    TimeSpan.FromSeconds(2));
                Assert.IsTrue(completed, "技能动作完成事实未进入设备时间线。\n"
                    + string.Join("\n", fixture.History.GetRecentWindow(100).Events.Select(item =>
                        item.Sequence + " " + item.EventType + " action=" + item.ActionId
                        + " run=" + item.RunId + " pos=" + item.StepIndex + ":" + item.OperationIndex)));
            }
        }

        [TestMethod]
        public void 通讯指令也属于必须绑定节点技能的外部交互()
        {
            using (var directory = new TemporaryDirectory())
            {
                var operation = new SendTcpMsg
                {
                    Id = Guid.NewGuid(),
                    Name = "名字不决定副作用",
                    ConnectionName = "未配置通道",
                    Msg = "未配置变量"
                };
                Proc process = CreateProcess(operation);
                var runtime = CreateRuntime(directory, process, out ProcessEngine engine);
                using (engine)
                {
                    JObject preview = runtime.MachineAgent.PreviewProcessEntry(Request(
                        process, operation, MachineExecutionModes.SingleOperation));
                    Assert.AreEqual("external_interaction", preview.Value<string>("operationEffect"));
                    Assert.IsTrue(BlockingReasons(preview).Any(item => item.Contains("skillId")));
                }
            }
        }

        private static IEnumerable<string> BlockingReasons(JObject preview)
        {
            return preview["blockingReasons"]?.Values<string>() ?? Enumerable.Empty<string>();
        }

        private static PlatformRuntime CreateRuntime(
            TemporaryDirectory directory,
            Proc process,
            out ProcessEngine engine,
            IIoRuntime io = null)
        {
            var runtime = new PlatformRuntime(directory.FullPath);
            runtime.Io = io;
            runtime.Stores.Processes.ReplaceAll(new[] { process });
            engine = new ProcessEngine(new EngineContext
            {
                Procs = new List<Proc> { process },
                Io = io,
                IoMap = runtime.Stores.IoConfiguration.ByName,
                ValueStore = runtime.Stores.Values,
                DataStructStore = runtime.Stores.DataStructures,
                Maintenance = runtime.Maintenance,
                Safety = runtime.Safety,
                Readiness = runtime.Readiness,
                Paths = runtime.Paths,
                ValidationContextFactory = runtime.CreateProcessValidationContext
            });
            runtime.ProcessEngine = engine;
            return runtime;
        }

        private static Proc CreateDelayProcess(out Delay target)
        {
            target = new Delay
            {
                Id = Guid.NewGuid(),
                Name = "名字不能决定语义",
                OperaType = "延时",
                DelayMs = 1
            };
            return CreateProcess(target);
        }

        private static Proc CreateProcess(OperationType target)
        {
            var process = new Proc { head = new ProcHead { Id = Guid.NewGuid(), Name = "任意流程名" } };
            var step = new Step { Id = Guid.NewGuid(), Name = "任意步骤名" };
            step.Ops.Add(target);
            step.Ops.Add(new EndProcess
            {
                Id = Guid.NewGuid(),
                Name = "结束",
                OperaType = "结束流程"
            });
            process.steps.Add(step);
            return process;
        }

        private static MachineProcessEntryPreviewRequest Request(
            Proc process, OperationType operation, string mode)
        {
            return new MachineProcessEntryPreviewRequest
            {
                ProcId = process.head.Id.ToString("D"),
                OperationId = operation.Id.ToString("D"),
                Mode = mode,
                Objective = "验证目标指令",
                ExpectedOutcome = "只执行目标指令并回到非活动态"
            };
        }

        private sealed class PhysicalRuntimeFixture : IDisposable
        {
            private const string IoName = "DO_MACHINE_AGENT_TEST";
            private readonly TemporaryDirectory directory;
            private readonly EquipmentProcessTimelineService timeline;

            public PhysicalRuntimeFixture(DateTime initialClock)
            {
                directory = new TemporaryDirectory();
                Clock = initialClock;
                Io = new FakeIoRuntime();
                Io.Values[IoName] = false;
                Operation = new IoOperate
                {
                    Id = Guid.NewGuid(),
                    Name = "名称不是控制契约",
                    IoParams = new OperationTypePartial.CustomList<IoOutParam>
                    {
                        new IoOutParam { IoName = IoName, TargetState = true }
                    }
                };
                Process = CreateProcess(Operation);
                Runtime = CreateRuntime(directory, Process, out ProcessEngine engine, Io);
                Engine = engine;
                Assert.IsTrue(Runtime.Stores.IoConfiguration.TryReplaceMap(new[]
                {
                    new List<IO>
                    {
                        new IO
                        {
                            Name = IoName,
                            CardNum = 0,
                            IOIndex = "0",
                            IOType = "通用输出",
                            UsedType = "通用",
                            EffectLevel = "正常"
                        }
                    }
                }, out string ioError), ioError);

                SkillId = "skill-machine-agent-test";
                var definition = new EquipmentTopologyDefinition();
                definition.Nodes.Add(new EquipmentTopologyNode
                {
                    Id = "actuator-machine-agent-test",
                    Label = "测试执行器",
                    Kind = "actuator",
                    ResourceKind = "ioOutput",
                    ResourceRef = IoName,
                    ReviewState = "confirmed",
                    Confidence = 1,
                    StateBindings = new List<EquipmentTopologyStateBinding>
                    {
                        StateBinding("output-on", "开启", "true", 20),
                        StateBinding("output-off", "关闭", "false", 10)
                    },
                    Skills = new List<EquipmentTopologySkillBinding>
                    {
                        new EquipmentTopologySkillBinding
                        {
                            Id = SkillId,
                            Name = "开启测试输出",
                            ProcessId = Process.head.Id.ToString("D"),
                            OperationId = Operation.Id.ToString("D"),
                            ExecutionMode = MachineExecutionModes.SingleOperation,
                            Objective = "切换测试输出",
                            ExpectedOutcome = "测试输出切换为开启",
                            Preconditions = new List<string>
                            {
                                "节点实时状态质量为 good",
                                "流程处于非活动状态"
                            },
                            ReviewState = "confirmed",
                            Confidence = 1
                        }
                    }
                });
                Assert.IsTrue(Runtime.Stores.Topology.TryCommit(
                    directory.FullPath, definition, out string topologyError), topologyError);

                History = new EquipmentStateHistoryService();
                Runtime.StateHistory = History;
                Perception = new EquipmentStatePerceptionService(
                    Runtime.Stores.Topology,
                    Runtime.Stores.IoConfiguration,
                    Io,
                    History,
                    TimeSpan.FromMilliseconds(100),
                    () => Clock);
                Runtime.StatePerception = Perception;
                Perception.Start();
                WaitForPerceptionRevision();
                timeline = new EquipmentProcessTimelineService(
                    Engine, Runtime.Stores.Topology, History);
                Runtime.ProcessTimeline = timeline;
            }

            public DateTime Clock { get; set; }
            public FakeIoRuntime Io { get; }
            public Proc Process { get; }
            public IoOperate Operation { get; }
            public string SkillId { get; }
            public PlatformRuntime Runtime { get; }
            public ProcessEngine Engine { get; }
            public EquipmentStateHistoryService History { get; }
            public EquipmentStatePerceptionService Perception { get; }

            public void ReplacePreconditions(params string[] expressions)
            {
                EquipmentTopologyDefinition definition = Runtime.Stores.Topology.CreateSnapshot();
                definition.Nodes.Single(node => node.Id == "actuator-machine-agent-test")
                    .Skills.Single(skill => skill.Id == SkillId).Preconditions = expressions.ToList();
                Assert.IsTrue(Runtime.Stores.Topology.TryCommit(
                    directory.FullPath, definition, out string error), error);
                Perception.PollOnce();
                WaitForPerceptionRevision();
            }

            public void SetExecutionMode(string mode)
            {
                EquipmentTopologyDefinition definition = Runtime.Stores.Topology.CreateSnapshot();
                definition.Nodes.Single(node => node.Id == "actuator-machine-agent-test")
                    .Skills.Single(skill => skill.Id == SkillId).ExecutionMode = mode;
                Assert.IsTrue(Runtime.Stores.Topology.TryCommit(
                    directory.FullPath, definition, out string error), error);
                Perception.PollOnce();
                WaitForPerceptionRevision();
            }

            public void AddUnresolvedInterlock()
            {
                SetInterlock("requires", "吸附确认后才允许搬运");
            }

            public void SetInterlock(string kind, string condition)
            {
                EquipmentTopologyDefinition definition = Runtime.Stores.Topology.CreateSnapshot();
                if (!definition.Nodes.Any(item => item.Id == "safety-machine-agent-test"))
                {
                    definition.Nodes.Add(new EquipmentTopologyNode
                    {
                        Id = "safety-machine-agent-test",
                        Label = "测试安全条件",
                        Kind = "safety",
                        ReviewState = "confirmed",
                        Confidence = 1
                    });
                }
                definition.Relations.RemoveAll(item => item.Id == "relation-machine-agent-interlock");
                definition.Relations.Add(new EquipmentTopologyRelation
                {
                    Id = "relation-machine-agent-interlock",
                    SourceNodeId = "actuator-machine-agent-test",
                    TargetNodeId = "safety-machine-agent-test",
                    Layer = "interlock",
                    Kind = kind,
                    Label = "测试前置防呆",
                    Condition = condition,
                    ReviewState = "confirmed",
                    Confidence = 1
                });
                Assert.IsTrue(Runtime.Stores.Topology.TryCommit(
                    directory.FullPath, definition, out string error), error);
                Perception.PollOnce();
                WaitForPerceptionRevision();
            }

            public void Dispose()
            {
                timeline.Dispose();
                Perception.Dispose();
                History.Dispose();
                Engine.Dispose();
                directory.Dispose();
            }

            private void WaitForPerceptionRevision()
            {
                long expected = Runtime.Stores.Topology.CreateSnapshot().Revision;
                bool ready = SpinWait.SpinUntil(() =>
                {
                    Perception.PollOnce();
                    EquipmentPerceptionSnapshot snapshot = Perception.GetCurrentSnapshot();
                    return snapshot.TopologyRevision == expected
                        && snapshot.NodeStates.Any(item => item.NodeId == "actuator-machine-agent-test");
                }, TimeSpan.FromSeconds(2));
                Assert.IsTrue(ready, "感知器未在期限内生成节点状态。");
            }

            private static EquipmentTopologyStateBinding StateBinding(
                string id, string stateName, string expectedValue, int priority)
            {
                return new EquipmentTopologyStateBinding
                {
                    Id = id,
                    StateName = stateName,
                    SourceKind = "io",
                    ResourceRef = IoName,
                    Operator = "equals",
                    ExpectedValue = expectedValue,
                    Meaning = stateName,
                    Priority = priority,
                    ReviewState = "confirmed",
                    Confidence = 1
                };
            }
        }

        private sealed class FakeIoRuntime : IIoRuntime
        {
            public Dictionary<string, bool> Values { get; } =
                new Dictionary<string, bool>(StringComparer.Ordinal);
            public int BatchCalls { get; private set; }

            public bool SetIO(IO io, bool isOpen)
            {
                if (io == null) return false;
                Values[io.Name] = isOpen;
                return true;
            }

            public bool SetOutputs(IReadOnlyList<IoOutputCommand> commands)
            {
                BatchCalls++;
                foreach (IoOutputCommand command in commands ?? Array.Empty<IoOutputCommand>())
                    Values[command.Io.Name] = command.TargetState;
                return true;
            }

            public bool GetOutIO(IO io, ref bool value)
            {
                return Read(io, ref value);
            }

            public bool GetInIO(IO io, ref bool value)
            {
                return Read(io, ref value);
            }

            private bool Read(IO io, ref bool value)
            {
                if (io == null || !Values.TryGetValue(io.Name, out bool current)) return false;
                value = current;
                return true;
            }
        }

        private static void WaitForInactive(ProcessEngine engine, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.GetSnapshot(0).State.IsInactive()) return;
                Thread.Sleep(10);
            }
            Assert.Fail("单指令执行未在期限内结束。");
        }
    }
}
