using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

                    fakeIo.FailReads = true;
                    perception.PollOnce();
                    EquipmentNodeStateProjection stale = history.GetCurrentSnapshot().NodeStates.Single();
                    Assert.AreEqual("吸取中", stale.StateName);
                    Assert.AreEqual(EquipmentStateQualities.Stale, stale.Quality);
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
