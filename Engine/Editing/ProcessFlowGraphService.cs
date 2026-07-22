using Automation.Protocol;
// 模块：引擎 / 结构编辑。
// 职责范围：执行流程与指令结构变换、跳转重写、发布门禁和变量生命周期处理。

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 从当前流程契约生成确定性控制流图。界面、Bridge 和 AI 共同使用本服务，
    /// 图形层不维护流程语义副本。
    /// </summary>
    public static class ProcessFlowGraphService
    {
        public const int GraphVersion = 1;

        public static ProcessFlowGraphSnapshot BuildProject(
            IReadOnlyList<Proc> processes,
            Func<int, EngineSnapshot> snapshotProvider = null,
            ProcessDefinitionValidationContext validationContext = null,
            ValueConfigStore valueStore = null)
        {
            processes = processes ?? Array.Empty<Proc>();
            var graph = new ProcessFlowGraphSnapshot
            {
                GraphVersion = GraphVersion,
                Scope = "project",
                Name = "项目流程总览"
            };
            IList<Proc> readinessProcesses = processes as IList<Proc> ?? processes.ToList();
            var nameMap = processes
                .Select((proc, index) => new ProjectTarget(proc, index))
                .Where(item => !string.IsNullOrWhiteSpace(item.Proc?.head?.Name))
                .GroupBy(item => item.Proc.head.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            for (int procIndex = 0; procIndex < processes.Count; procIndex++)
            {
                Proc proc = processes[procIndex];
                string procId = EnsureId(proc?.head?.Id, "proc-index-" + procIndex);
                EngineSnapshot runtime = snapshotProvider?.Invoke(procIndex);
                ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                    procIndex, proc, readinessProcesses, validationContext, valueStore);
                graph.Nodes.Add(new FlowGraphNode
                {
                    Id = BuildProcNodeId(procId),
                    Kind = "process",
                    Label = string.IsNullOrWhiteSpace(proc?.head?.Name) ? $"流程 {procIndex}" : proc.head.Name,
                    Summary = $"{proc?.steps?.Count ?? 0} 个步骤",
                    ProcIndex = procIndex,
                    ProcId = procId,
                    RuntimeState = runtime?.State.ToString() ?? ProcRunState.Stopped.ToString(),
                    ReadinessStatus = readiness?.ReadinessStatus ?? string.Empty,
                    AutoStart = proc?.head?.AutoStart == true,
                    Disabled = proc?.head?.Disable == true,
                    Reachable = true
                });
                if (proc?.head?.Disable == true) graph.DisabledNodeCount++;
            }

            var dynamicNodes = new Dictionary<string, string>(StringComparer.Ordinal);
            var invalidNodes = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int sourceIndex = 0; sourceIndex < processes.Count; sourceIndex++)
            {
                Proc source = processes[sourceIndex];
                string sourceProcId = EnsureId(source?.head?.Id, "proc-index-" + sourceIndex);
                string sourceNodeId = BuildProcNodeId(sourceProcId);
                foreach (ProjectOperation projectOperation in EnumerateProjectOperations(source))
                {
                    OperationType operation = projectOperation.Operation;
                    if (operation is ProcOps controls)
                    {
                        int itemIndex = 0;
                        foreach (ProcParam item in controls.Params ?? new OperationTypePartial.CustomList<ProcParam>())
                        {
                            AddProjectReference(graph, nameMap, dynamicNodes, invalidNodes,
                                sourceNodeId, sourceIndex, operation, itemIndex++,
                                item?.ProcName, item?.ProcValue,
                                string.Equals(item?.TargetState, "停止", StringComparison.Ordinal) ? "processStop" : "processStart",
                                item?.TargetState ?? "流程操作", projectOperation.Disabled);
                        }
                    }
                    else if (operation is WaitProc waits)
                    {
                        int itemIndex = 0;
                        foreach (WaitProcParam item in waits.Params ?? new OperationTypePartial.CustomList<WaitProcParam>())
                        {
                            AddProjectReference(graph, nameMap, dynamicNodes, invalidNodes,
                                sourceNodeId, sourceIndex, operation, itemIndex++,
                                item?.ProcName, item?.ProcValue,
                                "processWait", "等待" + (item?.TargetState ?? "目标状态"), projectOperation.Disabled);
                        }
                    }
                }
            }
            MarkProjectLoops(graph);
            return graph;
        }

        public static ProcessFlowGraphSnapshot BuildProcess(
            IReadOnlyList<Proc> processes,
            int procIndex,
            Func<int, EngineSnapshot> snapshotProvider = null)
        {
            if (processes == null || procIndex < 0 || procIndex >= processes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(procIndex), "流程索引超出范围。");
            }
            Proc proc = processes[procIndex] ?? throw new InvalidOperationException("流程为空。");
            string procId = EnsureId(proc.head?.Id, "proc-index-" + procIndex);
            EngineSnapshot runtime = snapshotProvider?.Invoke(procIndex);
            var graph = new ProcessFlowGraphSnapshot
            {
                GraphVersion = GraphVersion,
                Scope = "process",
                ProcIndex = procIndex,
                ProcId = procId,
                Name = string.IsNullOrWhiteSpace(proc.head?.Name) ? $"流程 {procIndex}" : proc.head.Name,
                PublishedRevision = runtime?.PublishedRevision ?? 0,
                AppliedRevision = runtime?.AppliedRevision ?? 0,
                RuntimeRevisionMatches = runtime == null
                    || runtime.ProcId.ToString("D") == procId
                    && runtime.PublishedRevision == runtime.AppliedRevision
            };
            string startId = "start:" + procId;
            string endId = "end:" + procId;
            graph.Nodes.Add(new FlowGraphNode { Id = startId, Kind = "start", Label = "开始", ProcIndex = procIndex, ProcId = procId, Reachable = true });
            graph.Nodes.Add(new FlowGraphNode { Id = endId, Kind = "end", Label = "结束", ProcIndex = procIndex, ProcId = procId });

            var locations = new List<OperationLocation>();
            var byAddress = new Dictionary<string, OperationLocation>(StringComparer.Ordinal);
            var byNodeId = new Dictionary<string, OperationLocation>(StringComparer.Ordinal);
            for (int stepIndex = 0; stepIndex < (proc.steps?.Count ?? 0); stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                string stepId = EnsureId(step?.Id, $"step-index-{procIndex}-{stepIndex}");
                string groupId = "step:" + stepId;
                graph.Groups.Add(new FlowGraphGroup
                {
                    Id = groupId,
                    Label = string.IsNullOrWhiteSpace(step?.Name) ? $"步骤 {stepIndex}" : step.Name,
                    ProcIndex = procIndex,
                    ProcId = procId,
                    StepIndex = stepIndex,
                    StepId = stepId,
                    Disabled = step?.Disable == true
                });
                for (int opIndex = 0; opIndex < (step?.Ops?.Count ?? 0); opIndex++)
                {
                    OperationType operation = step.Ops[opIndex];
                    string opId = EnsureId(operation?.Id, $"op-index-{procIndex}-{stepIndex}-{opIndex}");
                    string nodeId = "op:" + opId;
                    bool disabled = proc.head?.Disable == true || step?.Disable == true || operation?.Disable == true;
                    var location = new OperationLocation(procIndex, stepIndex, opIndex, stepId, opId, nodeId, operation, disabled);
                    locations.Add(location);
                    byAddress[$"{procIndex}-{stepIndex}-{opIndex}"] = location;
                    byNodeId[nodeId] = location;
                    graph.Nodes.Add(new FlowGraphNode
                    {
                        Id = nodeId,
                        Kind = "operation",
                        Label = string.IsNullOrWhiteSpace(operation?.Name) ? operation?.OperaType ?? "空指令" : operation.Name,
                        Summary = BuildOperationSummary(operation),
                        GroupId = groupId,
                        ProcIndex = procIndex,
                        ProcId = procId,
                        StepIndex = stepIndex,
                        StepId = stepId,
                        OpIndex = opIndex,
                        OpId = opId,
                        OperaType = operation?.OperaType ?? string.Empty,
                        Disabled = disabled,
                        Invalid = operation == null
                    });
                    if (disabled) graph.DisabledNodeCount++;
                }
            }

            List<OperationLocation> executable = locations.Where(item => !item.Disabled && item.Operation != null).ToList();
            if (executable.Count == 0)
            {
                AddEdge(graph, "sequence", startId, endId, "无可执行指令");
            }
            else
            {
                AddEdge(graph, "sequence", startId, executable[0].NodeId, string.Empty);
            }

            for (int i = 0; i < executable.Count; i++)
            {
                OperationLocation current = executable[i];
                string fallThroughTarget = i + 1 < executable.Count ? executable[i + 1].NodeId : endId;
                JObject contract = OperationBehaviorCatalog.BuildContract(current.Operation);
                JObject controlFlow = contract?["controlFlow"] as JObject;
                bool known = controlFlow?["known"]?.Value<bool?>() != false;
                bool terminal = controlFlow?["terminal"]?.Value<bool?>() == true;
                bool fallThrough = controlFlow?["fallThrough"]?.Value<bool?>() == true;
                if (!known)
                {
                    graph.Diagnostics.Add(new FlowGraphDiagnostic
                    {
                        Code = "UNKNOWN_CONTROL_FLOW",
                        Severity = "warning",
                        NodeId = current.NodeId,
                        Message = $"指令“{current.Operation.OperaType}”没有确定的控制流契约。"
                    });
                }
                if (terminal)
                {
                    AddEdge(graph, "terminal", current.NodeId, endId, "结束");
                }
                else if (known && fallThrough)
                {
                    AddEdge(graph, "sequence", current.NodeId, fallThroughTarget, "继续");
                }

                foreach (OperationGotoReference reference in OperationGotoReferenceCatalog.Enumerate(current.Operation))
                {
                    if (reference.IsAlarmField) continue;
                    if (string.IsNullOrWhiteSpace(reference.Value))
                    {
                        if (IsRequiredBusinessGoto(current.Operation, reference, fallThrough))
                        {
                            AddInvalidTarget(graph, current, reference, "跳转目标为空");
                        }
                        continue;
                    }
                    AddGotoEdge(graph, proc, current, reference, byAddress, executable, endId, false);
                }
                AddAlarmEdges(graph, proc, current, byAddress, executable, endId);
            }

            AnalyzeReachabilityAndLoops(graph, byNodeId, startId);
            foreach (OperationLocation location in executable)
            {
                FlowGraphNode node = graph.Nodes.First(item => item.Id == location.NodeId);
                if (!node.Reachable)
                {
                    graph.Diagnostics.Add(new FlowGraphDiagnostic
                    {
                        Code = "UNREACHABLE_OPERATION",
                        Severity = "warning",
                        NodeId = node.Id,
                        Message = $"步骤 {location.StepIndex} 指令 {location.OpIndex} 从流程入口不可达。"
                    });
                }
            }
            return graph;
        }

        public static JObject ToJObject(ProcessFlowGraphSnapshot graph)
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            };
            return JObject.FromObject(graph, JsonSerializer.Create(settings));
        }

        private static void AddProjectReference(
            ProcessFlowGraphSnapshot graph,
            IReadOnlyDictionary<string, List<ProjectTarget>> nameMap,
            Dictionary<string, string> dynamicNodes,
            Dictionary<string, string> invalidNodes,
            string sourceNodeId,
            int sourceIndex,
            OperationType operation,
            int itemIndex,
            string targetName,
            string targetVariable,
            string edgeKind,
            string label,
            bool disabled)
        {
            string targetNodeId;
            bool dynamic = false;
            bool invalid = false;
            if (!string.IsNullOrWhiteSpace(targetName))
            {
                if (nameMap.TryGetValue(targetName, out List<ProjectTarget> matches) && matches.Count == 1)
                {
                    ProjectTarget item = matches[0];
                    Proc targetProc = item.Proc;
                    int targetIndex = item.Index;
                    targetNodeId = BuildProcNodeId(EnsureId(targetProc?.head?.Id, "proc-index-" + targetIndex));
                }
                else
                {
                    invalid = true;
                    string key = "name:" + targetName;
                    if (!invalidNodes.TryGetValue(key, out targetNodeId))
                    {
                        targetNodeId = "invalid-proc:" + invalidNodes.Count;
                        invalidNodes[key] = targetNodeId;
                        graph.Nodes.Add(new FlowGraphNode
                        {
                            Id = targetNodeId,
                            Kind = "invalid",
                            Label = nameMap.ContainsKey(targetName) ? "流程名称不唯一" : "流程不存在",
                            Summary = targetName,
                            Invalid = true,
                            Reachable = true
                        });
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(targetVariable))
            {
                dynamic = true;
                if (!dynamicNodes.TryGetValue(targetVariable, out targetNodeId))
                {
                    targetNodeId = "dynamic-proc:" + dynamicNodes.Count;
                    dynamicNodes[targetVariable] = targetNodeId;
                    graph.Nodes.Add(new FlowGraphNode
                    {
                        Id = targetNodeId,
                        Kind = "dynamic",
                        Label = "动态流程目标",
                        Summary = targetVariable,
                        Dynamic = true,
                        Reachable = true
                    });
                }
            }
            else
            {
                invalid = true;
                string key = $"empty:{sourceIndex}:{operation?.Id}:{itemIndex}";
                targetNodeId = "invalid-proc:" + invalidNodes.Count;
                invalidNodes[key] = targetNodeId;
                graph.Nodes.Add(new FlowGraphNode
                {
                    Id = targetNodeId,
                    Kind = "invalid",
                    Label = "未配置流程目标",
                    Invalid = true,
                    Reachable = true
                });
            }
            FlowGraphEdge edge = AddEdge(graph, edgeKind, sourceNodeId, targetNodeId, label);
            edge.Dynamic = dynamic;
            edge.Invalid = invalid;
            edge.Disabled = disabled;
            if (disabled) graph.DisabledEdgeCount++;
            if (dynamic)
            {
                graph.Diagnostics.Add(new FlowGraphDiagnostic
                {
                    Code = "DYNAMIC_PROCESS_TARGET",
                    Severity = "info",
                    NodeId = sourceNodeId,
                    EdgeId = edge.Id,
                    Message = $"流程 {sourceIndex} 的目标由变量“{targetVariable}”在运行时决定。"
                });
            }
            if (invalid)
            {
                graph.Diagnostics.Add(new FlowGraphDiagnostic
                {
                    Code = "INVALID_PROCESS_TARGET",
                    Severity = "error",
                    NodeId = sourceNodeId,
                    EdgeId = edge.Id,
                    Message = $"流程 {sourceIndex} 的跨流程引用无法解析：{targetName ?? "<空>"}。"
                });
            }
        }

        private static IEnumerable<ProjectOperation> EnumerateProjectOperations(Proc proc)
        {
            foreach (Step step in proc?.steps ?? new List<Step>())
            {
                foreach (OperationType operation in step?.Ops ?? new List<OperationType>())
                {
                    if (operation != null)
                    {
                        yield return new ProjectOperation(operation,
                            proc?.head?.Disable == true || step?.Disable == true || operation.Disable);
                    }
                }
            }
        }

        private static bool IsRequiredBusinessGoto(OperationType operation, OperationGotoReference reference, bool fallThrough)
        {
            if (operation is PopupDialog) return false;
            if (operation is ParamGoto || operation is IoLogicGoto) return true;
            if (operation is Goto && reference.Container is GotoParam) return true;
            return !fallThrough && !string.Equals(reference.FieldName, "DefaultGoto", StringComparison.Ordinal);
        }

        private static void AddAlarmEdges(
            ProcessFlowGraphSnapshot graph,
            Proc proc,
            OperationLocation current,
            IReadOnlyDictionary<string, OperationLocation> byAddress,
            IReadOnlyList<OperationLocation> executable,
            string endId)
        {
            int count;
            switch (current.Operation.AlarmType)
            {
                case "自动处理":
                case "弹框确定": count = 1; break;
                case "弹框确定与否": count = 2; break;
                case "弹框确定与否与取消": count = 3; break;
                default: return;
            }
            string[] values = { current.Operation.Goto1, current.Operation.Goto2, current.Operation.Goto3 };
            string[] labels = { "报警:确定", "报警:否", "报警:取消" };
            for (int i = 0; i < count; i++)
            {
                var reference = new OperationGotoReference
                {
                    Path = "Goto" + (i + 1),
                    FieldName = "Goto" + (i + 1),
                    DisplayName = labels[i],
                    Value = values[i],
                    Container = current.Operation,
                    IsAlarmField = true
                };
                if (string.IsNullOrWhiteSpace(reference.Value))
                {
                    AddInvalidTarget(graph, current, reference, "报警跳转目标为空", true);
                }
                else
                {
                    AddGotoEdge(graph, proc, current, reference, byAddress, executable, endId, true);
                }
            }
        }

        private static void AddGotoEdge(
            ProcessFlowGraphSnapshot graph,
            Proc proc,
            OperationLocation current,
            OperationGotoReference reference,
            IReadOnlyDictionary<string, OperationLocation> byAddress,
            IReadOnlyList<OperationLocation> executable,
            string endId,
            bool alarmPath)
        {
            if (!ProcessDefinitionService.TryParseGotoKey(reference.Value, out int targetProc, out int targetStep, out int targetOp)
                || targetProc != current.ProcIndex
                || !byAddress.TryGetValue(reference.Value, out OperationLocation configuredTarget))
            {
                AddInvalidTarget(graph, current, reference, "跳转目标无效", alarmPath);
                return;
            }
            OperationLocation effectiveTarget = configuredTarget.Disabled
                ? FindExecutableAtOrAfter(executable, targetStep, targetOp)
                : configuredTarget;
            string targetId = effectiveTarget?.NodeId ?? endId;
            FlowGraphEdge edge = AddEdge(graph, alarmPath ? "alarm" : "branch",
                current.NodeId, targetId, BuildGotoLabel(current.Operation, reference));
            edge.SourceField = reference.Path;
            edge.AlarmPath = alarmPath;
            edge.ConfiguredTargetId = configuredTarget.NodeId;
            if (alarmPath) graph.AlarmEdgeCount++;
            if (configuredTarget.Disabled)
            {
                graph.Diagnostics.Add(new FlowGraphDiagnostic
                {
                    Code = "GOTO_TARGET_DISABLED",
                    Severity = "info",
                    EdgeId = edge.Id,
                    Message = $"{reference.Value} 指向禁用对象，运行时将继续到下一条有效指令。"
                });
            }
        }

        private static OperationLocation FindExecutableAtOrAfter(
            IReadOnlyList<OperationLocation> executable,
            int stepIndex,
            int opIndex)
        {
            return executable.FirstOrDefault(item => item.StepIndex > stepIndex
                || item.StepIndex == stepIndex && item.OpIndex >= opIndex);
        }

        private static string BuildGotoLabel(OperationType operation, OperationGotoReference reference)
        {
            if (reference.IsAlarmField) return reference.DisplayName;
            if (operation is ParamGoto || operation is IoLogicGoto)
            {
                if (reference.FieldName == "TrueGoto") return "成立";
                if (reference.FieldName == "FalseGoto") return "不成立";
            }
            if (operation is Goto)
            {
                if (reference.FieldName == "DefaultGoto") return "默认";
                if (reference.Container is GotoParam item)
                {
                    string match = !string.IsNullOrWhiteSpace(item.MatchValue) ? item.MatchValue : item.MatchValueV;
                    return string.IsNullOrWhiteSpace(match) ? "匹配项" : "匹配 " + match;
                }
            }
            if (operation is PopupDialog popup)
            {
                if (reference.FieldName == "PopupGoto1") return string.IsNullOrWhiteSpace(popup.Btn1Text) ? "按钮1" : popup.Btn1Text;
                if (reference.FieldName == "PopupGoto2") return string.IsNullOrWhiteSpace(popup.Btn2Text) ? "按钮2" : popup.Btn2Text;
                if (reference.FieldName == "PopupGoto3") return string.IsNullOrWhiteSpace(popup.Btn3Text) ? "按钮3" : popup.Btn3Text;
            }
            return reference.DisplayName;
        }

        private static void AddInvalidTarget(
            ProcessFlowGraphSnapshot graph,
            OperationLocation current,
            OperationGotoReference reference,
            string reason,
            bool alarmPath = false)
        {
            string invalidId = "invalid:" + current.OpId + ":" + SanitizeId(reference.Path);
            if (!graph.Nodes.Any(item => item.Id == invalidId))
            {
                graph.Nodes.Add(new FlowGraphNode
                {
                    Id = invalidId,
                    Kind = "invalid",
                    Label = reason,
                    Summary = string.IsNullOrWhiteSpace(reference.Value) ? reference.Path : reference.Value,
                    ProcIndex = current.ProcIndex,
                    Invalid = true
                });
            }
            FlowGraphEdge edge = AddEdge(graph, alarmPath ? "alarm" : "invalid",
                current.NodeId, invalidId, BuildGotoLabel(current.Operation, reference));
            edge.SourceField = reference.Path;
            edge.Invalid = true;
            edge.AlarmPath = alarmPath;
            if (alarmPath) graph.AlarmEdgeCount++;
            graph.Diagnostics.Add(new FlowGraphDiagnostic
            {
                Code = "INVALID_GOTO_TARGET",
                Severity = "error",
                NodeId = current.NodeId,
                EdgeId = edge.Id,
                Message = $"{reason}：{reference.Path}={reference.Value ?? "<空>"}。"
            });
        }

        private static FlowGraphEdge AddEdge(
            ProcessFlowGraphSnapshot graph,
            string kind,
            string sourceId,
            string targetId,
            string label)
        {
            var edge = new FlowGraphEdge
            {
                Id = "edge:" + graph.Edges.Count,
                Kind = kind,
                SourceId = sourceId,
                TargetId = targetId,
                Label = label ?? string.Empty
            };
            graph.Edges.Add(edge);
            return edge;
        }

        private static void AnalyzeReachabilityAndLoops(
            ProcessFlowGraphSnapshot graph,
            IReadOnlyDictionary<string, OperationLocation> operationNodes,
            string startId)
        {
            var outgoing = graph.Edges
                .Where(edge => !edge.Invalid)
                .GroupBy(edge => edge.SourceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetId).ToList(), StringComparer.Ordinal);
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(startId);
            while (queue.Count > 0)
            {
                string node = queue.Dequeue();
                if (!reachable.Add(node) || !outgoing.TryGetValue(node, out List<string> targets)) continue;
                foreach (string target in targets) queue.Enqueue(target);
            }
            foreach (FlowGraphNode node in graph.Nodes) node.Reachable = reachable.Contains(node.Id) || node.Disabled;

            List<HashSet<string>> components = FindStronglyConnectedComponents(
                operationNodes.Keys,
                graph.Edges.Where(edge => operationNodes.ContainsKey(edge.SourceId)
                    && operationNodes.ContainsKey(edge.TargetId) && !edge.Invalid));
            foreach (HashSet<string> component in components)
            {
                bool loop = component.Count > 1 || graph.Edges.Any(edge => edge.SourceId == edge.TargetId && component.Contains(edge.SourceId));
                if (!loop) continue;
                foreach (FlowGraphEdge edge in graph.Edges.Where(edge => component.Contains(edge.SourceId) && component.Contains(edge.TargetId)))
                {
                    edge.Loop = true;
                }
                graph.Diagnostics.Add(new FlowGraphDiagnostic
                {
                    Code = "CONTROL_FLOW_LOOP",
                    Severity = "info",
                    NodeId = component.OrderBy(value => value, StringComparer.Ordinal).First(),
                    Message = $"检测到包含 {component.Count} 个节点的控制流回环。"
                });
            }
        }

        private static List<HashSet<string>> FindStronglyConnectedComponents(
            IEnumerable<string> nodes,
            IEnumerable<FlowGraphEdge> edges)
        {
            var adjacency = nodes.ToDictionary(node => node, _ => new List<string>(), StringComparer.Ordinal);
            foreach (FlowGraphEdge edge in edges)
            {
                if (adjacency.TryGetValue(edge.SourceId, out List<string> targets)) targets.Add(edge.TargetId);
            }
            int index = 0;
            var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
            var low = new Dictionary<string, int>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            var onStack = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<HashSet<string>>();
            Action<string> visit = null;
            visit = node =>
            {
                indexes[node] = index;
                low[node] = index++;
                stack.Push(node);
                onStack.Add(node);
                foreach (string target in adjacency[node])
                {
                    if (!indexes.ContainsKey(target))
                    {
                        visit(target);
                        low[node] = Math.Min(low[node], low[target]);
                    }
                    else if (onStack.Contains(target))
                    {
                        low[node] = Math.Min(low[node], indexes[target]);
                    }
                }
                if (low[node] != indexes[node]) return;
                var component = new HashSet<string>(StringComparer.Ordinal);
                string current;
                do
                {
                    current = stack.Pop();
                    onStack.Remove(current);
                    component.Add(current);
                } while (!string.Equals(current, node, StringComparison.Ordinal));
                result.Add(component);
            };
            foreach (string node in adjacency.Keys)
            {
                if (!indexes.ContainsKey(node)) visit(node);
            }
            return result;
        }

        private static void MarkProjectLoops(ProcessFlowGraphSnapshot graph)
        {
            var procNodes = new HashSet<string>(graph.Nodes.Where(node => node.Kind == "process").Select(node => node.Id), StringComparer.Ordinal);
            foreach (HashSet<string> component in FindStronglyConnectedComponents(
                procNodes,
                graph.Edges.Where(edge => procNodes.Contains(edge.SourceId) && procNodes.Contains(edge.TargetId))))
            {
                bool loop = component.Count > 1 || graph.Edges.Any(edge => edge.SourceId == edge.TargetId && component.Contains(edge.SourceId));
                if (!loop) continue;
                foreach (FlowGraphEdge edge in graph.Edges.Where(edge => component.Contains(edge.SourceId) && component.Contains(edge.TargetId))) edge.Loop = true;
                graph.Diagnostics.Add(new FlowGraphDiagnostic
                {
                    Code = "PROCESS_RELATION_LOOP",
                    Severity = "info",
                    NodeId = component.OrderBy(value => value, StringComparer.Ordinal).First(),
                    Message = $"检测到包含 {component.Count} 个流程的跨流程关系回环。"
                });
            }
        }

        private static string BuildOperationSummary(OperationType operation)
        {
            if (operation == null) return "指令为空";
            JObject contract = OperationBehaviorCatalog.BuildContract(operation);
            string purpose = contract?["purpose"]?.Value<string>();
            return string.IsNullOrWhiteSpace(purpose) ? operation.OperaType ?? operation.GetType().Name : purpose;
        }

        private static string BuildProcNodeId(string procId) => "proc:" + procId;

        private static string EnsureId(Guid? id, string fallback)
        {
            return id.HasValue && id.Value != Guid.Empty ? id.Value.ToString("D") : fallback;
        }

        private static string SanitizeId(string value)
        {
            return new string((value ?? string.Empty).Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        }

        private sealed class OperationLocation
        {
            public OperationLocation(int procIndex, int stepIndex, int opIndex, string stepId,
                string opId, string nodeId, OperationType operation, bool disabled)
            {
                ProcIndex = procIndex;
                StepIndex = stepIndex;
                OpIndex = opIndex;
                StepId = stepId;
                OpId = opId;
                NodeId = nodeId;
                Operation = operation;
                Disabled = disabled;
            }

            public int ProcIndex { get; }
            public int StepIndex { get; }
            public int OpIndex { get; }
            public string StepId { get; }
            public string OpId { get; }
            public string NodeId { get; }
            public OperationType Operation { get; }
            public bool Disabled { get; }
        }

        private sealed class ProjectTarget
        {
            public ProjectTarget(Proc proc, int index)
            {
                Proc = proc;
                Index = index;
            }

            public Proc Proc { get; }
            public int Index { get; }
        }

        private sealed class ProjectOperation
        {
            public ProjectOperation(OperationType operation, bool disabled)
            {
                Operation = operation;
                Disabled = disabled;
            }

            public OperationType Operation { get; }
            public bool Disabled { get; }
        }

    }
}
