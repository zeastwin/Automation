using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Automation.Simulation
{
    internal sealed class SimulationTestDefinition
    {
        [JsonProperty("scenarioVersion", Required = Required.Always)] public int ScenarioVersion { get; set; }
        [JsonProperty("simulatorScenario", Required = Required.Always)] public string SimulatorScenario { get; set; }
        [JsonProperty("startProcIds", Required = Required.Always)] public List<Guid> StartProcIds { get; set; }
        [JsonProperty("maxDurationMs", Required = Required.Always)] public int MaxDurationMs { get; set; }
        [JsonProperty("noAlarms", Required = Required.Always)] public bool NoAlarms { get; set; }
    }

    internal sealed class SimulationTestReport
    {
        public string ScenarioName { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public long DurationMs { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public List<EngineSnapshotReport> Snapshots { get; set; }
        public List<OperationTraceEntry> Trace { get; set; }
    }

    internal sealed class EngineSnapshotReport
    {
        public int ProcIndex { get; set; }
        public Guid ProcId { get; set; }
        public string ProcName { get; set; }
        public string State { get; set; }
        public int StepIndex { get; set; }
        public int OpIndex { get; set; }
        public bool IsAlarm { get; set; }
        public string AlarmMessage { get; set; }
    }

    internal sealed class FailFastSimulationAlarmHandler : IAlarmHandler
    {
        public Task<AlarmDecision> HandleAsync(AlarmContext context) => Task.FromResult(AlarmDecision.Stop);
    }

    internal static class SimulationTestRunner
    {
        public static int Run(string scenarioName, SimulationGatewayClient gateway, ProcessEngine engine)
        {
            DateTime startedAt = DateTime.Now;
            var stopwatch = Stopwatch.StartNew();
            var report = new SimulationTestReport { ScenarioName = scenarioName, StartedAt = startedAt, Snapshots = new List<EngineSnapshotReport>(), Trace = new List<OperationTraceEntry>() };
            var trace = new ConcurrentQueue<OperationTraceEntry>();
            try
            {
                if (gateway == null || !gateway.IsCardInitialized) throw new InvalidOperationException("模拟器网关未就绪");
                if (engine?.Context?.Procs == null) throw new InvalidOperationException("流程引擎未初始化");
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SimulationScenarios", scenarioName + ".json");
                if (!File.Exists(path)) throw new InvalidOperationException($"平台仿真场景不存在:{path}");
                var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error, NullValueHandling = NullValueHandling.Include };
                SimulationTestDefinition definition = JsonConvert.DeserializeObject<SimulationTestDefinition>(File.ReadAllText(path), settings);
                ValidateDefinition(definition);
                List<Proc> procList = engine.Context.Procs.ToList();
                var indices = new List<int>();
                foreach (Guid id in definition.StartProcIds)
                {
                    int index = procList.FindIndex(proc => proc?.head?.Id == id);
                    if (index < 0) throw new InvalidOperationException($"场景流程不存在:{id}");
                    if (procList[index]?.head?.Disable == true) throw new InvalidOperationException($"场景流程已禁用:{id}");
                    indices.Add(index);
                }
                engine.AlarmHandler = new FailFastSimulationAlarmHandler();
                gateway.ArmScenario(definition.SimulatorScenario);
                var latest = new ConcurrentDictionary<int, EngineSnapshot>();
                var completed = new ConcurrentDictionary<int, bool>();
                int operationsInFlight = 0;
                Action<OperationTraceEntry> traceHandler = entry =>
                {
                    trace.Enqueue(entry);
                    if (!indices.Contains(entry.ProcIndex)) return;
                    if (entry.Phase == "Started") Interlocked.Increment(ref operationsInFlight);
                    else Interlocked.Decrement(ref operationsInFlight);
                };
                engine.OperationTraced += traceHandler;
                Action<int, Guid> completedHandler = (index, id) =>
                {
                    if (indices.Contains(index)) completed[index] = true;
                };
                engine.ProcessCompleted += completedHandler;
                Action<EngineSnapshot> handler = snapshot =>
                {
                    if (snapshot == null || !indices.Contains(snapshot.ProcIndex)) return;
                    latest[snapshot.ProcIndex] = snapshot;
                };
                engine.SnapshotChanged += handler;
                try
                {
                    foreach (int index in indices) engine.StartProcAuto(null, index);
                    while (stopwatch.ElapsedMilliseconds <= definition.MaxDurationMs)
                    {
                        Application.DoEvents();
                        List<EngineSnapshot> snapshots = indices.Select(engine.GetSnapshot).ToList();
                        if (definition.NoAlarms && snapshots.Any(snapshot => snapshot?.IsAlarm == true)) throw new InvalidOperationException("场景出现未预期报警");
                        if (indices.All(index => completed.ContainsKey(index)) && Volatile.Read(ref operationsInFlight) == 0)
                        {
                            report.Success = true;
                            break;
                        }
                        Thread.Sleep(20);
                    }
                    if (!report.Success)
                    {
                        string locations = string.Join("；", latest.Values.OrderBy(item => item.ProcIndex).Select(item => $"{item.ProcName}:{item.State} {item.StepIndex}-{item.OpIndex} {item.AlarmMessage}"));
                        throw new TimeoutException($"场景运行超时:{definition.MaxDurationMs}ms，当前位置:{locations}");
                    }
                }
                finally
                {
                    engine.SnapshotChanged -= handler;
                    engine.OperationTraced -= traceHandler;
                    engine.ProcessCompleted -= completedHandler;
                    report.Snapshots = report.Success
                        ? indices.Select(engine.GetSnapshot).Where(item => item != null).Select(ToReport).ToList()
                        : latest.Values.OrderBy(item => item.ProcIndex).Select(ToReport).ToList();
                    foreach (int index in indices) engine.Stop(index);
                }
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.Error = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                report.DurationMs = stopwatch.ElapsedMilliseconds;
                report.FinishedAt = DateTime.Now;
                report.Trace = trace.ToList();
                if (engine != null && (report.Snapshots == null || report.Snapshots.Count == 0)) report.Snapshots = engine.GetSnapshots().Where(snapshot => snapshot != null).Select(ToReport).ToList();
                WriteReport(report);
            }
            return report.Success ? 0 : 1;
        }

        private static void ValidateDefinition(SimulationTestDefinition definition)
        {
            if (definition == null || definition.ScenarioVersion != 1) throw new InvalidOperationException("平台仿真场景版本无效");
            if (string.IsNullOrWhiteSpace(definition.SimulatorScenario) || definition.SimulatorScenario.Contains("..") || definition.SimulatorScenario.Contains("/") || definition.SimulatorScenario.Contains("\\")) throw new InvalidOperationException("模拟器场景名称无效");
            if (definition.StartProcIds == null || definition.StartProcIds.Count == 0 || definition.StartProcIds.Any(id => id == Guid.Empty) || definition.StartProcIds.Distinct().Count() != definition.StartProcIds.Count) throw new InvalidOperationException("场景流程ID为空、无效或重复");
            if (definition.MaxDurationMs <= 0) throw new InvalidOperationException("场景最大运行时间无效");
        }

        private static EngineSnapshotReport ToReport(EngineSnapshot snapshot)
        {
            return new EngineSnapshotReport { ProcIndex = snapshot.ProcIndex, ProcId = snapshot.ProcId, ProcName = snapshot.ProcName, State = snapshot.State.ToString(), StepIndex = snapshot.StepIndex, OpIndex = snapshot.OpIndex, IsAlarm = snapshot.IsAlarm, AlarmMessage = snapshot.AlarmMessage };
        }

        private static void WriteReport(SimulationTestReport report)
        {
            string root = AutomationRuntimeOptions.Current.SessionRoot;
            if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SimulationRuns");
            string directory = Path.Combine(root, "Reports");
            Directory.CreateDirectory(directory);
            string fileName = report.ScenarioName + "_" + report.StartedAt.ToString("yyyyMMdd_HHmmss_fff") + ".json";
            File.WriteAllText(Path.Combine(directory, fileName), JsonConvert.SerializeObject(report, Formatting.Indented));
            string traceName = Path.GetFileNameWithoutExtension(fileName) + ".trace.jsonl";
            File.WriteAllLines(Path.Combine(directory, traceName), (report.Trace ?? new List<OperationTraceEntry>()).Select(item => JsonConvert.SerializeObject(item, Formatting.None)));
        }
    }
}
