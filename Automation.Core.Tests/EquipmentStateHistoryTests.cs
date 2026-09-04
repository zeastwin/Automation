using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    public class EquipmentStateHistoryTests
    {
        [TestMethod]
        public void 时间线是事实源并可重建任意序列的节点状态()
        {
            using (var history = new EquipmentStateHistoryService())
            {
                DateTime start = new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);
                EquipmentStateHistoryEvent first = history.Append(NodeState(
                    start, "nozzle-1", "吸嘴1", "释放", "release-binding"));
                EquipmentStateHistoryEvent second = history.Append(NodeState(
                    start.AddMilliseconds(120), "nozzle-1", "吸嘴1", "吸取中", "vacuum-binding"));

                EquipmentStateSnapshot atFirst = history.GetSnapshotAt(first.Sequence);
                EquipmentStateSnapshot current = history.GetCurrentSnapshot();
                EquipmentStateHistoryWindow window = history.GetRecentWindow(20);

                Assert.AreEqual("释放", atFirst.NodeStates.Single().StateName);
                Assert.AreEqual("吸取中", current.NodeStates.Single().StateName);
                Assert.AreEqual(second.Sequence, current.Sequence);
                Assert.AreEqual(2, window.Events.Count);
                Assert.AreEqual(first.Sequence, window.Events[0].Sequence);
                Assert.IsTrue(window.Events[0].Sequence < window.Events[1].Sequence);
            }
        }

        [TestMethod]
        public void 感知器只消费已确认拓扑绑定并记录IO到语义状态的因果链()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "automation-state-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var ioStore = new IoConfigurationStore();
                Assert.IsTrue(ioStore.TryReplaceMap(new[]
                {
                    new List<IO>
                    {
                        new IO
                        {
                            Name = "真空阀",
                            CardNum = 0,
                            IOIndex = "1",
                            IOType = "通用输出",
                            UsedType = "通用",
                            EffectLevel = "正常"
                        }
                    }
                }, out string ioError), ioError);

                var topologyStore = new EquipmentTopologyStore();
                EquipmentTopologyDefinition definition = BuildTopology();
                Assert.IsTrue(topologyStore.TryCommit(directory, definition, out string topologyError), topologyError);

                var fakeIo = new FakeIoRuntime();
                fakeIo.Values["真空阀"] = false;
                using (var history = new EquipmentStateHistoryService())
                using (var perception = new EquipmentStatePerceptionService(
                    topologyStore, ioStore, fakeIo, history, TimeSpan.FromMilliseconds(100)))
                {
                    perception.PollOnce();
                    Assert.AreEqual("释放", history.GetCurrentSnapshot().NodeStates.Single().StateName);
                    Assert.IsFalse(perception.GetCurrentSnapshot().NodeStates.Any(item =>
                        item.NodeId == "variable-only-node"));

                    fakeIo.Values["真空阀"] = true;
                    perception.PollOnce();
                    EquipmentNodeStateProjection current = history.GetCurrentSnapshot().NodeStates.Single();
                    EquipmentStateHistoryWindow window = history.GetRecentWindow(100);
                    EquipmentStateHistoryEvent semantic = window.Events.Last(item =>
                        item.EventType == EquipmentStateEventTypes.NodeStateChanged);

                    Assert.AreEqual("吸取中", current.StateName);
                    Assert.AreEqual("vacuum-on", current.BindingId);
                    Assert.AreEqual(1, history.GetCurrentSnapshot().NodeStates.Count);
                    Assert.IsTrue(semantic.CausedBySequence.HasValue);
                    Assert.IsTrue(window.Events.Any(item =>
                        item.Sequence == semantic.CausedBySequence.Value
                        && item.EventType == EquipmentStateEventTypes.SignalChanged
                        && item.Aspect == EquipmentStateAspects.Commanded));
                    DateTime lastSuccessfulObservationAtUtc = perception.GetCurrentSnapshot()
                        .NodeStates.Single().LastSuccessfulObservationAtUtc;

                    fakeIo.FailReads = true;
                    perception.PollOnce();
                    EquipmentNodeStateProjection stale = history.GetCurrentSnapshot().NodeStates.Single();
                    Assert.AreEqual("吸取中", stale.StateName);
                    Assert.AreEqual(EquipmentStateQualities.Stale, stale.Quality);
                    Assert.AreEqual(
                        lastSuccessfulObservationAtUtc,
                        perception.GetCurrentSnapshot().NodeStates.Single()
                            .LastSuccessfulObservationAtUtc);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void 稳定状态持续成功观测时刷新新鲜度但不污染时间线()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "automation-state-freshness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var ioStore = new IoConfigurationStore();
                Assert.IsTrue(ioStore.TryReplaceMap(new[]
                {
                    new List<IO>
                    {
                        new IO
                        {
                            Name = "真空阀",
                            CardNum = 0,
                            IOIndex = "1",
                            IOType = "通用输出",
                            UsedType = "通用",
                            EffectLevel = "正常"
                        }
                    }
                }, out string ioError), ioError);
                var topologyStore = new EquipmentTopologyStore();
                Assert.IsTrue(
                    topologyStore.TryCommit(
                        directory, BuildTopology(), out string topologyError),
                    topologyError);
                var fakeIo = new FakeIoRuntime();
                fakeIo.Values["真空阀"] = true;
                DateTime nowUtc = DateTime.UtcNow;

                using (var history = new EquipmentStateHistoryService())
                using (var perception = new EquipmentStatePerceptionService(
                    topologyStore,
                    ioStore,
                    fakeIo,
                    history,
                    TimeSpan.FromMilliseconds(100),
                    () => nowUtc))
                {
                    perception.PollOnce();
                    EquipmentNodePerceptionState first = perception
                        .GetCurrentSnapshot().NodeStates.Single();
                    int eventCount = history.GetRecentWindow(100).Events.Count;

                    nowUtc = nowUtc.AddSeconds(6);
                    perception.PollOnce();
                    EquipmentNodePerceptionState stable = perception
                        .GetCurrentSnapshot().NodeStates.Single();

                    Assert.AreEqual(first.StateChangedAtUtc, stable.StateChangedAtUtc);
                    Assert.AreEqual(nowUtc, stable.LastSuccessfulObservationAtUtc);
                    Assert.AreEqual(first.Sequence, stable.Sequence);
                    Assert.AreEqual(eventCount, history.GetRecentWindow(100).Events.Count);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void 启动恢复JSONL重建序列窗口和投影且损坏行只降级报告()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "automation-state-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                EquipmentStateHistoryEvent latest;
                using (var first = new EquipmentStateHistoryService(directory))
                {
                    first.Append(NodeState(
                        nowUtc.AddHours(-25),
                        "old-stable-node",
                        "长期稳定节点",
                        "就绪",
                        "old-ready"));
                    first.Append(NodeState(
                        nowUtc.AddMinutes(-2),
                        "nozzle-1",
                        "吸嘴1",
                        "释放",
                        "vacuum-off"));
                    latest = first.Append(NodeState(
                        nowUtc.AddMinutes(-1),
                        "nozzle-1",
                        "吸嘴1",
                        "吸取中",
                        "vacuum-on"));
                }

                string recentPath = Path.Combine(
                    directory,
                    nowUtc.ToString("yyyy-MM-dd"),
                    "equipment-state.jsonl");
                File.AppendAllText(recentPath, "{这不是合法JSON}" + Environment.NewLine);

                using (var recovered = new EquipmentStateHistoryService(directory))
                {
                    EquipmentStateSnapshot snapshot = recovered.GetCurrentSnapshot();
                    EquipmentStateHistoryWindow window = recovered.GetRecentWindow(100);

                    Assert.AreEqual(latest.Sequence, recovered.Revision);
                    Assert.AreEqual("吸取中", snapshot.NodeStates.Single(
                        item => item.NodeId == "nozzle-1").StateName);
                    Assert.AreEqual("就绪", snapshot.NodeStates.Single(
                        item => item.NodeId == "old-stable-node").StateName);
                    Assert.AreEqual(2, window.Events.Count);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(recovered.LastRecoveryError));
                    Assert.IsTrue(recovered.LastPersistenceError.Contains("跳过 1 条"));

                    EquipmentStateHistoryEvent appended = recovered.Append(NodeState(
                        nowUtc,
                        "nozzle-1",
                        "吸嘴1",
                        "释放",
                        "vacuum-off"));
                    Assert.AreEqual(latest.Sequence + 1, appended.Sequence);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(recovered.LastPersistenceError));
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void 启动恢复遵守事件上限并保留基线和最新投影()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "automation-state-limit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                DateTime nowUtc = DateTime.UtcNow.AddMinutes(-1);
                using (var first = new EquipmentStateHistoryService(
                    directory, maximumRetainedEvents: 100))
                {
                    for (int index = 0; index < 105; index++)
                    {
                        first.Append(NodeState(
                            nowUtc.AddMilliseconds(index),
                            "buffer-1",
                            "缓存工位",
                            "状态" + index,
                            "binding-" + index));
                    }
                }

                using (var recovered = new EquipmentStateHistoryService(
                    directory, maximumRetainedEvents: 100))
                {
                    EquipmentStateHistoryWindow window = recovered.GetRecentWindow(500);
                    EquipmentNodeStateProjection current = recovered.GetCurrentSnapshot()
                        .NodeStates.Single(item => item.NodeId == "buffer-1");

                    Assert.AreEqual(105, recovered.Revision);
                    Assert.AreEqual(100, window.Events.Count);
                    Assert.AreEqual(6, window.EarliestAvailableSequence);
                    Assert.AreEqual(5, window.Baseline.Sequence);
                    Assert.AreEqual("状态4", window.Baseline.NodeStates.Single().StateName);
                    Assert.AreEqual("状态104", current.StateName);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void 序列游标可连续分页超过五百条事实且基线紧邻首条事件()
        {
            using (var history = new EquipmentStateHistoryService())
            {
                DateTime start = DateTime.UtcNow.AddMinutes(-1);
                for (int index = 0; index < 650; index++)
                {
                    history.Append(NodeState(
                        start.AddMilliseconds(index),
                        "buffer-1",
                        "缓存工位",
                        "状态" + index,
                        "binding-" + index));
                }

                EquipmentStateHistoryWindow first =
                    history.GetWindowAfterSequence(0, 120);
                EquipmentStateHistoryWindow second =
                    history.GetWindowAfterSequence(first.Events.Last().Sequence, 120);
                EquipmentStateHistoryWindow tail =
                    history.GetWindowAfterSequence(600, 120);
                EquipmentStateHistoryWindow recent =
                    history.GetWindowAfterSequence(null, 3);

                Assert.AreEqual(1, first.Events.First().Sequence);
                Assert.AreEqual(120, first.Events.Last().Sequence);
                Assert.AreEqual(0, first.Baseline.Sequence);
                Assert.IsTrue(first.Truncated);
                Assert.AreEqual(650, first.LatestSequence);

                Assert.AreEqual(121, second.Events.First().Sequence);
                Assert.AreEqual(240, second.Events.Last().Sequence);
                Assert.AreEqual(120, second.Baseline.Sequence);
                Assert.AreEqual("状态119", second.Baseline.NodeStates.Single().StateName);
                Assert.IsTrue(second.Truncated);

                Assert.AreEqual(50, tail.Events.Count);
                Assert.AreEqual(601, tail.Events.First().Sequence);
                Assert.AreEqual(650, tail.Events.Last().Sequence);
                Assert.AreEqual(600, tail.Baseline.Sequence);
                Assert.AreEqual("状态599", tail.Baseline.NodeStates.Single().StateName);
                Assert.IsFalse(tail.Truncated);

                CollectionAssert.AreEqual(
                    new long[] { 648, 649, 650 },
                    recent.Events.Select(item => item.Sequence).ToArray());
                Assert.AreEqual(647, recent.Baseline.Sequence);
                Assert.IsTrue(recent.Truncated);
            }
        }

        [TestMethod]
        public void 恢复存在序列缺口时基线使用实际事实并显式报告缺口()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "automation-state-gap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                DateTime observedAtUtc = DateTime.UtcNow.AddMinutes(-1);
                string dayDirectory = Path.Combine(
                    directory, observedAtUtc.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(dayDirectory);
                EquipmentStateHistoryEvent first = NodeState(
                    observedAtUtc, "buffer-1", "缓存工位", "空", "empty");
                first.Sequence = 1;
                first.ReceivedAtUtc = observedAtUtc;
                EquipmentStateHistoryEvent third = NodeState(
                    observedAtUtc.AddSeconds(1), "buffer-1", "缓存工位", "有料", "occupied");
                third.Sequence = 3;
                third.ReceivedAtUtc = third.ObservedAtUtc;
                File.WriteAllLines(
                    Path.Combine(dayDirectory, "equipment-state.jsonl"),
                    new[]
                    {
                        JsonConvert.SerializeObject(first),
                        JsonConvert.SerializeObject(third)
                    });

                using (var history = new EquipmentStateHistoryService(directory))
                {
                    EquipmentStateHistoryWindow page =
                        history.GetWindowAfterSequence(1, 10);

                    Assert.AreEqual(1, page.Baseline.Sequence,
                        "基线只能声明实际已应用到的最后一条事实。");
                    Assert.IsTrue(page.BaselineComplete);
                    Assert.AreEqual(3, page.Events.Single().Sequence);
                    Assert.AreEqual(1, page.SequenceGaps.Count);
                    Assert.AreEqual(2, page.SequenceGaps[0].FirstMissingSequence);
                    Assert.AreEqual(2, page.SequenceGaps[0].LastMissingSequence);
                    Assert.IsFalse(page.SequenceGapsTruncated);

                    EquipmentStateHistoryWindow current =
                        history.GetWindowAfterSequence(3, 10);
                    Assert.AreEqual(3, current.Baseline.Sequence);
                    Assert.IsFalse(current.BaselineComplete,
                        "跨过已知缺口后的基线必须明确标记为不完整。");
                    StringAssert.Contains(history.LastRecoveryError, "sequence 缺口");

                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void 黑匣子关闭时诊断证据仍引用独立状态历史()
        {
            using (var history = new EquipmentStateHistoryService())
            {
                history.Append(NodeState(
                    DateTime.UtcNow, "gripper-1", "夹爪1", "夹紧", "grip-on"));

                Newtonsoft.Json.Linq.JObject evidence =
                    RuntimeBlackBoxRecorder.BuildUnavailableEvidencePackage(0, history);
                Newtonsoft.Json.Linq.JObject stateEvidence =
                    evidence["equipmentStateHistory"] as Newtonsoft.Json.Linq.JObject;

                Assert.IsNotNull(stateEvidence);
                Assert.IsTrue(stateEvidence.Value<bool>("available"));
                Assert.AreEqual("equipment_state_history", stateEvidence.Value<string>("owner"));
                Assert.AreEqual(1, stateEvidence["events"].Count());
            }
        }

        private static EquipmentStateHistoryEvent NodeState(
            DateTime timeUtc,
            string nodeId,
            string label,
            string stateName,
            string bindingId)
        {
            return new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = timeUtc,
                TopologyRevision = 3,
                EventType = EquipmentStateEventTypes.NodeStateChanged,
                NodeId = nodeId,
                NodeLabel = label,
                Aspect = EquipmentStateAspects.Estimated,
                NewValue = stateName,
                Meaning = stateName,
                Quality = EquipmentStateQualities.Good,
                Confidence = 1,
                SourceKind = "io",
                ResourceRef = "真空阀",
                BindingId = bindingId
            };
        }

        private static EquipmentTopologyDefinition BuildTopology()
        {
            var definition = new EquipmentTopologyDefinition
            {
                Name = "测试拓扑"
            };
            definition.Nodes.Add(new EquipmentTopologyNode
            {
                Id = "nozzle-1",
                Label = "吸嘴1",
                Kind = "actuator",
                ReviewState = "confirmed",
                Confidence = 1,
                StateBindings = new List<EquipmentTopologyStateBinding>
                {
                    Binding("vacuum-on", "吸取中", "true", 20, "confirmed"),
                    Binding("vacuum-off", "释放", "false", 10, "confirmed")
                }
            });
            definition.Nodes.Add(new EquipmentTopologyNode
            {
                Id = "candidate-nozzle",
                Label = "候选吸嘴",
                Kind = "actuator",
                ReviewState = "candidate",
                Confidence = .6,
                StateBindings = new List<EquipmentTopologyStateBinding>
                {
                    Binding("candidate-binding", "候选状态", "true", 20, "candidate")
                }
            });
            definition.Nodes.Add(new EquipmentTopologyNode
            {
                Id = "variable-only-node",
                Label = "仅变量节点",
                Kind = "sensor",
                ReviewState = "confirmed",
                Confidence = 1,
                StateBindings = new List<EquipmentTopologyStateBinding>
                {
                    new EquipmentTopologyStateBinding
                    {
                        Id = "variable-binding",
                        StateName = "变量确认",
                        SourceKind = "variable",
                        ResourceRef = "runtime-variable-1",
                        Operator = "equals",
                        ExpectedValue = "true",
                        Meaning = "当前版本不采集变量状态。",
                        Priority = 30,
                        ReviewState = "confirmed",
                        Confidence = 1
                    }
                }
            });
            return definition;
        }

        private static EquipmentTopologyStateBinding Binding(
            string id,
            string stateName,
            string expected,
            int priority,
            string reviewState)
        {
            return new EquipmentTopologyStateBinding
            {
                Id = id,
                StateName = stateName,
                SourceKind = "io",
                ResourceRef = "真空阀",
                Operator = "equals",
                ExpectedValue = expected,
                Meaning = stateName,
                Priority = priority,
                ReviewState = reviewState,
                Confidence = reviewState == "confirmed" ? 1 : .6
            };
        }

        private sealed class FakeIoRuntime : IIoRuntime
        {
            public Dictionary<string, bool> Values { get; } =
                new Dictionary<string, bool>(StringComparer.Ordinal);
            public bool FailReads { get; set; }

            public bool SetIO(IO io, bool isOpen)
            {
                throw new AssertFailedException("状态感知不得写IO。");
            }

            public bool SetOutputs(IReadOnlyList<IoOutputCommand> commands)
            {
                throw new AssertFailedException("状态感知不得批量写IO。");
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
                if (FailReads) return false;
                if (io == null || !Values.TryGetValue(io.Name, out bool current)) return false;
                value = current;
                return true;
            }
        }
    }
}
