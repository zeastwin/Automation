using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using static Automation.FrmCard;

namespace Automation.KernelTests
{
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            Run("未复位禁止启动", TestResetGate);
            Run("报警忽略后继续执行", TestAlarmIgnoreContinues);
            Run("停止超时拒绝重启", TestStopTimeoutRejectsRestart);
            Run("手动轴资源禁止重复占用", TestManualMotionResourceExclusion);
            Run("轴与工站静态配置校验", TestMotionConfigurationValidation);
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
