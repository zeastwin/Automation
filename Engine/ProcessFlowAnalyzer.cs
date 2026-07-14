using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Automation
{
    public sealed class ProcessFlowAnalysis
    {
        public bool ContainsReachableCycle { get; internal set; }

        public IReadOnlyList<string> CycleLocations { get; internal set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 基于平台实际跳转字段分析流程控制流，不依赖 AI 提示词或语义变更集来源。
    /// </summary>
    public static class ProcessFlowAnalyzer
    {
        public static ProcessFlowAnalysis Analyze(int procIndex, Proc proc)
        {
            var operations = new Dictionary<string, OperationType>(StringComparer.Ordinal);
            var order = new List<string>();
            if (proc?.steps != null)
            {
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    if (step == null || step.Disable || step.Ops == null)
                    {
                        continue;
                    }
                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        OperationType operation = step.Ops[opIndex];
                        if (operation == null || operation.Disable)
                        {
                            continue;
                        }
                        string key = BuildKey(stepIndex, opIndex);
                        operations[key] = operation;
                        order.Add(key);
                    }
                }
            }
            if (order.Count == 0)
            {
                return new ProcessFlowAnalysis();
            }

            var edges = order.ToDictionary(key => key, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
            for (int index = 0; index < order.Count; index++)
            {
                string key = order[index];
                OperationType operation = operations[key];
                foreach (string target in ReadGotoTargets(operation))
                {
                    if (ProcessDefinitionService.TryParseGotoKey(target, out int targetProc, out int targetStep, out int targetOp)
                        && targetProc == procIndex)
                    {
                        string targetKey = ResolveActiveTarget(proc, targetStep, targetOp, operations);
                        if (targetKey != null)
                        {
                            edges[key].Add(targetKey);
                        }
                    }
                }

                // “跳转”运行时必定选择匹配分支或默认目标；其他指令保留顺序执行边。
                if (!(operation is Goto) && !(operation is EndProcess) && index + 1 < order.Count)
                {
                    edges[key].Add(order[index + 1]);
                }
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var active = new HashSet<string>(StringComparer.Ordinal);
            var stack = new List<string>();
            var cycleLocations = new HashSet<string>(StringComparer.Ordinal);
            FindCycles(order[0], edges, visited, active, stack, cycleLocations);
            return new ProcessFlowAnalysis
            {
                ContainsReachableCycle = cycleLocations.Count > 0,
                CycleLocations = cycleLocations.OrderBy(value => value, StringComparer.Ordinal).ToArray()
            };
        }

        private static void FindCycles(
            string key,
            IReadOnlyDictionary<string, HashSet<string>> edges,
            HashSet<string> visited,
            HashSet<string> active,
            List<string> stack,
            HashSet<string> cycleLocations)
        {
            if (!visited.Add(key))
            {
                return;
            }
            active.Add(key);
            stack.Add(key);
            foreach (string target in edges[key])
            {
                if (!visited.Contains(target))
                {
                    FindCycles(target, edges, visited, active, stack, cycleLocations);
                    continue;
                }
                if (!active.Contains(target))
                {
                    continue;
                }
                int start = stack.IndexOf(target);
                for (int index = Math.Max(0, start); index < stack.Count; index++)
                {
                    cycleLocations.Add(stack[index]);
                }
            }
            stack.RemoveAt(stack.Count - 1);
            active.Remove(key);
        }

        private static IEnumerable<string> ReadGotoTargets(object value)
        {
            var targets = new List<string>();
            ReadGotoTargets(value, targets, new HashSet<object>());
            return targets;
        }

        private static void ReadGotoTargets(object value, List<string> targets, HashSet<object> visited)
        {
            if (value == null || value is string || !visited.Add(value))
            {
                return;
            }
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(Guid))
            {
                return;
            }
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead)
                {
                    continue;
                }
                object propertyValue;
                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch
                {
                    continue;
                }
                if (property.PropertyType == typeof(string)
                    && property.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string target = propertyValue as string;
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        targets.Add(target.Trim());
                    }
                    continue;
                }
                if (propertyValue is IEnumerable enumerable && !(propertyValue is string))
                {
                    foreach (object item in enumerable)
                    {
                        ReadGotoTargets(item, targets, visited);
                    }
                }
            }
        }

        private static string BuildKey(int stepIndex, int opIndex)
        {
            return $"{stepIndex}-{opIndex}";
        }

        private static string ResolveActiveTarget(
            Proc proc,
            int targetStep,
            int targetOperation,
            IReadOnlyDictionary<string, OperationType> operations)
        {
            if (proc?.steps == null)
            {
                return null;
            }
            for (int stepIndex = targetStep; stepIndex < proc.steps.Count; stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                if (step == null || step.Disable || step.Ops == null)
                {
                    continue;
                }
                int start = stepIndex == targetStep ? targetOperation : 0;
                for (int opIndex = Math.Max(0, start); opIndex < step.Ops.Count; opIndex++)
                {
                    string key = BuildKey(stepIndex, opIndex);
                    if (operations.ContainsKey(key))
                    {
                        return key;
                    }
                }
            }
            return null;
        }
    }
}
