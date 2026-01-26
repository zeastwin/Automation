using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Automation.AIFlow
{
    public sealed class AiFlowDiffResult
    {
        public List<string> AddedProcs { get; } = new List<string>();
        public List<string> RemovedProcs { get; } = new List<string>();
        public List<string> ModifiedProcs { get; } = new List<string>();
        public List<string> AddedSteps { get; } = new List<string>();
        public List<string> RemovedSteps { get; } = new List<string>();
        public List<string> ModifiedSteps { get; } = new List<string>();
        public List<string> AddedOps { get; } = new List<string>();
        public List<string> RemovedOps { get; } = new List<string>();
        public List<string> ModifiedOps { get; } = new List<string>();
    }

    public static class AiFlowDiff
    {
        public static AiFlowDiffResult Build(AiCoreFlow baseCore, AiCoreFlow targetCore)
        {
            var result = new AiFlowDiffResult();
            if (baseCore == null || targetCore == null)
            {
                return result;
            }
            var baseProcs = baseCore.Procs?.ToDictionary(p => p.Id) ?? new Dictionary<string, AiCoreProc>();
            var targetProcs = targetCore.Procs?.ToDictionary(p => p.Id) ?? new Dictionary<string, AiCoreProc>();

            foreach (var procId in baseProcs.Keys)
            {
                if (!targetProcs.ContainsKey(procId))
                {
                    result.RemovedProcs.Add(procId);
                }
            }
            foreach (var procId in targetProcs.Keys)
            {
                if (!baseProcs.ContainsKey(procId))
                {
                    result.AddedProcs.Add(procId);
                }
            }

            foreach (var kv in targetProcs)
            {
                if (!baseProcs.TryGetValue(kv.Key, out var baseProc))
                {
                    continue;
                }
                var targetProc = kv.Value;
                if (baseProc == null || targetProc == null)
                {
                    continue;
                }
                if (!string.Equals(baseProc.Name, targetProc.Name)
                    || baseProc.AutoStart != targetProc.AutoStart
                    || !SameList(baseProc.PauseIo, targetProc.PauseIo)
                    || !SameList(baseProc.PauseValue, targetProc.PauseValue))
                {
                    result.ModifiedProcs.Add(kv.Key);
                }

                var baseSteps = baseProc.Steps?.ToDictionary(s => s.Id) ?? new Dictionary<string, AiCoreStep>();
                var targetSteps = targetProc.Steps?.ToDictionary(s => s.Id) ?? new Dictionary<string, AiCoreStep>();

                foreach (var stepId in baseSteps.Keys)
                {
                    if (!targetSteps.ContainsKey(stepId))
                    {
                        result.RemovedSteps.Add($"proc:{kv.Key}/step:{stepId}");
                    }
                }
                foreach (var stepId in targetSteps.Keys)
                {
                    if (!baseSteps.ContainsKey(stepId))
                    {
                        result.AddedSteps.Add($"proc:{kv.Key}/step:{stepId}");
                    }
                }

                foreach (var stepPair in targetSteps)
                {
                    if (!baseSteps.TryGetValue(stepPair.Key, out var baseStep))
                    {
                        continue;
                    }
                    var targetStep = stepPair.Value;
                    if (baseStep == null || targetStep == null)
                    {
                        continue;
                    }
                    if (!string.Equals(baseStep.Name, targetStep.Name))
                    {
                        result.ModifiedSteps.Add($"proc:{kv.Key}/step:{stepPair.Key}");
                    }

                    var baseOps = baseStep.Ops?.ToDictionary(o => o.Id) ?? new Dictionary<string, AiCoreOp>();
                    var targetOps = targetStep.Ops?.ToDictionary(o => o.Id) ?? new Dictionary<string, AiCoreOp>();

                    foreach (var opId in baseOps.Keys)
                    {
                        if (!targetOps.ContainsKey(opId))
                        {
                            result.RemovedOps.Add($"proc:{kv.Key}/step:{stepPair.Key}/op:{opId}");
                        }
                    }
                    foreach (var opId in targetOps.Keys)
                    {
                        if (!baseOps.ContainsKey(opId))
                        {
                            result.AddedOps.Add($"proc:{kv.Key}/step:{stepPair.Key}/op:{opId}");
                        }
                    }

                    foreach (var opPair in targetOps)
                    {
                        if (!baseOps.TryGetValue(opPair.Key, out var baseOp))
                        {
                            continue;
                        }
                        var targetOp = opPair.Value;
                        if (baseOp == null || targetOp == null)
                        {
                            continue;
                        }
                        if (!string.Equals(baseOp.OpCode, targetOp.OpCode)
                            || !string.Equals(baseOp.Name, targetOp.Name)
                            || baseOp.Disabled != targetOp.Disabled
                            || baseOp.Breakpoint != targetOp.Breakpoint
                            || !SameAlarm(baseOp.Alarm, targetOp.Alarm)
                            || !SameArgs(baseOp.Args, targetOp.Args))
                        {
                            result.ModifiedOps.Add($"proc:{kv.Key}/step:{stepPair.Key}/op:{opPair.Key}");
                        }
                    }
                }
            }

            return result;
        }

        private static bool SameList(List<string> left, List<string> right)
        {
            if (left == null && right == null)
            {
                return true;
            }
            if (left == null || right == null)
            {
                return false;
            }
            if (left.Count != right.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameAlarm(AiCoreAlarm left, AiCoreAlarm right)
        {
            if (left == null && right == null)
            {
                return true;
            }
            if (left == null || right == null)
            {
                return false;
            }
            return string.Equals(left.Type, right.Type)
                && string.Equals(left.AlarmInfoId, right.AlarmInfoId)
                && string.Equals(left.Goto1, right.Goto1)
                && string.Equals(left.Goto2, right.Goto2)
                && string.Equals(left.Goto3, right.Goto3);
        }

        private static bool SameArgs(JObject left, JObject right)
        {
            if (left == null && right == null)
            {
                return true;
            }
            if (left == null || right == null)
            {
                return false;
            }
            return JToken.DeepEquals(left, right);
        }
    }
}
