using System;
// 模块：核心测试 / 运动配置故障门禁。
// 职责范围：验证损坏文件与跨配置不一致不会被降级成合法空卡项目。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Automation.MotionControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class MotionConfigurationFaultTests
    {
        [TestMethod]
        public void LoadMotionConfiguration_控制卡文件损坏时保留明确故障原因()
        {
            using (var directory = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(directory.FullPath, "card.json"), "{损坏");
                var runtime = new PlatformRuntime(directory.FullPath);

                PlatformRuntimeInitializer.LoadMotionConfiguration(runtime);

                Assert.IsTrue(runtime.Readiness.MotionConfigFaulted);
                Assert.IsFalse(string.IsNullOrWhiteSpace(runtime.Readiness.MotionConfigFaultReason));
                StringAssert.Contains(runtime.Readiness.MotionConfigFaultReason, "控制卡");
            }
        }

        [TestMethod]
        public void LoadMotionConfiguration_控制卡语义非法时不得退化为空卡项目()
        {
            using (var directory = new TemporaryDirectory())
            {
                Assert.IsTrue(AtomicJsonFileStore.Save(
                    directory.FullPath,
                    "card",
                    new Card
                    {
                        controlCards = new List<ControlCard>
                        {
                            new ControlCard
                            {
                                cardHead = new CardHead { AxisCount = 1 },
                                axis = new List<Axis>()
                            }
                        }
                    }));
                var runtime = new PlatformRuntime(directory.FullPath);

                PlatformRuntimeInitializer.LoadMotionConfiguration(runtime);

                Assert.IsTrue(runtime.Readiness.MotionConfigFaulted);
                StringAssert.Contains(runtime.Readiness.MotionConfigFaultReason, "轴数量与轴列表不一致");
                Assert.AreEqual(1, runtime.Stores.Cards.GetControlCardCount(),
                    "语义非法的现有卡配置不得被替换成合法空卡默认值。");
            }
        }

        [TestMethod]
        public void LoadMotionConfiguration_卡与Io不一致时进入故障态()
        {
            using (var directory = new TemporaryDirectory())
            {
                Assert.IsTrue(AtomicJsonFileStore.Save(
                    directory.FullPath,
                    "card",
                    new Card
                    {
                        controlCards = new List<ControlCard>
                        {
                            new ControlCard
                            {
                                cardHead = new CardHead
                                {
                                    AxisCount = 0,
                                    InputCount = 1,
                                    OutputCount = 0
                                },
                                axis = new List<Axis>()
                            }
                        }
                    }));
                var runtime = new PlatformRuntime(directory.FullPath);

                PlatformRuntimeInitializer.LoadMotionConfiguration(runtime);

                Assert.IsTrue(runtime.Readiness.MotionConfigFaulted);
                StringAssert.Contains(runtime.Readiness.MotionConfigFaultReason, "控制卡与IO配置一致性校验失败");
            }
        }

        [TestMethod]
        public void LoadMotionConfiguration_Io文件损坏时进入故障态()
        {
            using (var directory = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(directory.FullPath, "IOMap.json"), "[损坏");
                var runtime = new PlatformRuntime(directory.FullPath);

                PlatformRuntimeInitializer.LoadMotionConfiguration(runtime);

                Assert.IsTrue(runtime.Readiness.MotionConfigFaulted);
                StringAssert.Contains(runtime.Readiness.MotionConfigFaultReason, "IO配置");
            }
        }

        [TestMethod]
        public void LoadMotionConfiguration_轴工站引用不存在卡轴时进入故障态()
        {
            using (var directory = new TemporaryDirectory())
            {
                var station = new DataStation(true)
                {
                    Name = "非法轴工站",
                    Type = StationType.Axis
                };
                station.dataAxis.axisConfig1.CardNum = "0";
                station.dataAxis.axisConfig1.AxisName = "不存在轴";
                Assert.IsTrue(AtomicJsonFileStore.Save(
                    directory.FullPath,
                    "DataStation",
                    new List<DataStation> { station }));
                var runtime = new PlatformRuntime(directory.FullPath);

                PlatformRuntimeInitializer.LoadMotionConfiguration(runtime);

                Assert.IsTrue(runtime.Readiness.MotionConfigFaulted);
                StringAssert.Contains(runtime.Readiness.MotionConfigFaultReason, "工站配置加载校验失败");
                StringAssert.Contains(runtime.Readiness.MotionConfigFaultReason, "轴配置不存在");
            }
        }

        [TestMethod]
        public void LoadMotionConfiguration_配置文件均缺失时生成合法纯机器人空配置()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);

                PlatformRuntimeInitializer.LoadMotionConfiguration(runtime);

                Assert.IsFalse(runtime.Readiness.MotionConfigFaulted,
                    runtime.Readiness.MotionConfigFaultReason);
                Assert.AreEqual(string.Empty, runtime.Readiness.MotionConfigFaultReason);
                Assert.AreEqual(0, runtime.Stores.Cards.GetControlCardCount());
                Assert.AreEqual(0, runtime.Stores.IoConfiguration.Map.Count);
                Assert.AreEqual(0, runtime.Stores.Stations.Items.Count);
                Assert.IsTrue(new[] { "card.json", "IOMap.json", "DataStation.json" }
                    .All(file => File.Exists(Path.Combine(directory.FullPath, file))));
            }
        }

        [TestMethod]
        public void ProcessReadiness_运动配置故障只阻止含运动指令流程()
        {
            var runtime = new PlatformRuntime();
            runtime.Readiness.MotionConfigFaulted = true;
            runtime.Readiness.MotionConfigFaultReason = "测试卡配置损坏";
            Proc nonMotion = TestProcessFactory.CreateEndingProcess("MES流程", 1);
            Proc motion = TestProcessFactory.CreateEndingProcess("运动流程");
            motion.steps[0].Ops.Insert(0, new HomeRun
            {
                Id = Guid.NewGuid(),
                Name = "工站回原",
                StationName = "轴工站"
            });

            ProcessReadinessAnalysis nonMotionReadiness = ProcessReadinessService.Analyze(
                0, nonMotion, new[] { nonMotion }, runtime.CreateProcessValidationContext(),
                runtime.Stores.Values);
            ProcessReadinessAnalysis motionReadiness = ProcessReadinessService.Analyze(
                0, motion, new[] { motion }, runtime.CreateProcessValidationContext(),
                runtime.Stores.Values);

            Assert.IsTrue(nonMotionReadiness.Runnable,
                string.Join("；", nonMotionReadiness.RunBlockers));
            Assert.IsFalse(motionReadiness.Runnable);
            Assert.IsTrue(motionReadiness.RunBlockers.Any(item =>
                item.Contains("运动配置故障") && item.Contains("测试卡配置损坏")));
        }

        [TestMethod]
        public void MotionCtrl_轴与机器人工站入口返回同一配置故障原因()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                runtime.Readiness.MotionConfigFaulted = true;
                runtime.Readiness.MotionConfigFaultReason = "card.json轴数量非法";
                var motion = new MotionCtrl(
                    runtime.Stores.Values,
                    runtime.Stores.Cards,
                    runtime.Stores.Stations,
                    runtime.Communication,
                    runtime.Stores.Communication,
                    runtime.Paths,
                    runtime.Safety,
                    runtime.Readiness,
                    new NoopLogger());

                InvalidOperationException axisError = Assert.ThrowsExactly<InvalidOperationException>(
                    () => motion.ValidateAxesForCommand(new[]
                    {
                        new AxisCommandRequest(0, 0, AxisCommandKind.Motion)
                    }));
                MotionStationResult stationResult = motion.HomeStation(0);
                MotionStationStatus stationStatus = motion.GetStationStatus(0);

                StringAssert.Contains(axisError.Message, "card.json轴数量非法");
                Assert.AreEqual(MotionStationResult.CommandRejected, stationResult);
                Assert.AreEqual(MotionStationState.Faulted, stationStatus.State);
                StringAssert.Contains(stationStatus.LastError, "card.json轴数量非法");
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
