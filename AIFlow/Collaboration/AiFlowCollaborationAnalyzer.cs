using System;
using System.Collections.Generic;
using System.Linq;
using Automation;

namespace Automation.AIFlow
{
    public static class AiFlowCollaborationAnalyzer
    {
        public const string ContractVersion = "contract-1";

        public static List<AiFlowIssue> Analyze(AiCoreFlow core, AiFlowContractSet contractSet)
        {
            var issues = new List<AiFlowIssue>();
            if (contractSet == null)
            {
                issues.Add(new AiFlowIssue("CONTRACT_NULL", "contract 为空", "collaboration"));
                return issues;
            }
            if (!string.Equals(contractSet.Version, ContractVersion, StringComparison.Ordinal))
            {
                issues.Add(new AiFlowIssue("CONTRACT_VERSION", $"contract 版本不匹配:{contractSet.Version}", "collaboration"));
                return issues;
            }
            if (contractSet.Contracts == null)
            {
                issues.Add(new AiFlowIssue("CONTRACT_LIST_NULL", "contract 列表为空", "collaboration"));
                return issues;
            }

            AiFlowCompileResult compile = AiFlowCompiler.CompileCore(core);
            if (!compile.Success)
            {
                issues.AddRange(compile.Issues);
                return issues;
            }

            var procMap = BuildProcMap(core);
            foreach (AiFlowContract contract in contractSet.Contracts)
            {
                if (contract == null)
                {
                    issues.Add(new AiFlowIssue("CONTRACT_ITEM_NULL", "contract 为空", "collaboration"));
                    continue;
                }
                if (contract.Type != "commandAck")
                {
                    issues.Add(new AiFlowIssue("CONTRACT_TYPE", $"不支持的 contract 类型:{contract.Type}", "collaboration"));
                    continue;
                }
                if (!procMap.TryGetValue(contract.CommandProcId, out int cmdIndex))
                {
                    issues.Add(new AiFlowIssue("CONTRACT_CMD_PROC", $"commandProc 不存在:{contract.CommandProcId}", "collaboration"));
                    continue;
                }
                if (!procMap.TryGetValue(contract.AckProcId, out int ackIndex))
                {
                    issues.Add(new AiFlowIssue("CONTRACT_ACK_PROC", $"ackProc 不存在:{contract.AckProcId}", "collaboration"));
                    continue;
                }

                Proc cmdProc = compile.Procs[cmdIndex];
                Proc ackProc = compile.Procs[ackIndex];
                string ackProcName = ackProc?.head?.Name;

                if (!HasProcStart(cmdProc, ackProcName))
                {
                    issues.Add(new AiFlowIssue("CONTRACT_CMD_START", $"commandProc 未包含 ProcOps 启动:{ackProcName}", contract.Id));
                }
                if (!HasValueWrite(cmdProc, contract.CmdValueName))
                {
                    issues.Add(new AiFlowIssue("CONTRACT_CMD_WRITE", $"commandProc 未写入 cmd 变量:{contract.CmdValueName}", contract.Id));
                }
                if (!HasValueCheck(cmdProc, contract.AckValueName))
                {
                    issues.Add(new AiFlowIssue("CONTRACT_CMD_WAIT", $"commandProc 未等待 ack 变量:{contract.AckValueName}", contract.Id));
                }

                if (!HasValueCheck(ackProc, contract.CmdValueName))
                {
                    issues.Add(new AiFlowIssue("CONTRACT_ACK_CHECK", $"ackProc 未读取 cmd 变量:{contract.CmdValueName}", contract.Id));
                }
                if (!HasValueWrite(ackProc, contract.AckValueName))
                {
                    issues.Add(new AiFlowIssue("CONTRACT_ACK_WRITE", $"ackProc 未写入 ack 变量:{contract.AckValueName}", contract.Id));
                }
            }

            DetectWaitDeadlock(compile.Procs, issues);
            return issues;
        }

        private static Dictionary<string, int> BuildProcMap(AiCoreFlow core)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            if (core?.Procs == null)
            {
                return map;
            }
            for (int i = 0; i < core.Procs.Count; i++)
            {
                string id = core.Procs[i]?.Id;
                if (!string.IsNullOrWhiteSpace(id) && !map.ContainsKey(id))
                {
                    map[id] = i;
                }
            }
            return map;
        }

        private static bool HasProcStart(Proc proc, string targetProcId)
        {
            if (proc?.steps == null)
            {
                return false;
            }
            foreach (Step step in proc.steps)
            {
                if (step?.Ops == null)
                {
                    continue;
                }
                foreach (OperationType op in step.Ops)
                {
                    if (op is ProcOps procOps && procOps.procParams != null)
                    {
                        foreach (var param in procOps.procParams)
                        {
                            if (param == null)
                            {
                                continue;
                            }
                            if (param.value == "运行" && string.Equals(param.ProcName, targetProcId))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static bool HasValueWrite(Proc proc, string valueName)
        {
            if (proc?.steps == null)
            {
                return false;
            }
            foreach (Step step in proc.steps)
            {
                if (step?.Ops == null)
                {
                    continue;
                }
                foreach (OperationType op in step.Ops)
                {
                    if (op is ModifyValue modifyValue)
                    {
                        if (string.Equals(modifyValue.OutputValueName, valueName))
                        {
                            return true;
                        }
                    }
                    if (op is GetValue getValue && getValue.Params != null)
                    {
                        foreach (var param in getValue.Params)
                        {
                            if (param != null && string.Equals(param.ValueSaveName, valueName))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static bool HasValueCheck(Proc proc, string valueName)
        {
            if (proc?.steps == null)
            {
                return false;
            }
            foreach (Step step in proc.steps)
            {
                if (step?.Ops == null)
                {
                    continue;
                }
                foreach (OperationType op in step.Ops)
                {
                    if (op is ParamGoto paramGoto && paramGoto.Params != null)
                    {
                        foreach (var param in paramGoto.Params)
                        {
                            if (param != null && string.Equals(param.ValueName, valueName))
                            {
                                return true;
                            }
                        }
                    }
                    if (op is Goto gotoOp)
                    {
                        if (string.Equals(gotoOp.ValueName, valueName))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static void DetectWaitDeadlock(List<Proc> procs, List<AiFlowIssue> issues)
        {
            if (procs == null)
            {
                return;
            }
            var edges = new Dictionary<int, List<int>>();
            for (int i = 0; i < procs.Count; i++)
            {
                edges[i] = new List<int>();
                Proc proc = procs[i];
                if (proc?.steps == null)
                {
                    continue;
                }
                foreach (Step step in proc.steps)
                {
                    if (step?.Ops == null)
                    {
                        continue;
                    }
                    foreach (OperationType op in step.Ops)
                    {
                        if (op is WaitProc waitProc && waitProc.Params != null)
                        {
                            foreach (var param in waitProc.Params)
                            {
                                if (param == null)
                                {
                                    continue;
                                }
                                if (param.value == "停止" && !string.IsNullOrWhiteSpace(param.ProcName))
                                {
                                    int targetIndex = FindProcIndexByName(procs, param.ProcName);
                                    if (targetIndex >= 0 && !edges[i].Contains(targetIndex))
                                    {
                                        edges[i].Add(targetIndex);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var visited = new bool[procs.Count];
            var stack = new bool[procs.Count];
            for (int i = 0; i < procs.Count; i++)
            {
                if (DetectCycle(i, edges, visited, stack))
                {
                    issues.Add(new AiFlowIssue("COLLAB_DEADLOCK", "检测到等待流程死锁环", "collaboration"));
                    break;
                }
            }
        }

        private static bool DetectCycle(int node, Dictionary<int, List<int>> edges, bool[] visited, bool[] stack)
        {
            if (stack[node])
            {
                return true;
            }
            if (visited[node])
            {
                return false;
            }
            visited[node] = true;
            stack[node] = true;
            foreach (int next in edges[node])
            {
                if (DetectCycle(next, edges, visited, stack))
                {
                    return true;
                }
            }
            stack[node] = false;
            return false;
        }

        private static int FindProcIndexByName(List<Proc> procs, string name)
        {
            if (procs == null)
            {
                return -1;
            }
            for (int i = 0; i < procs.Count; i++)
            {
                if (procs[i]?.head?.Name == name)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
