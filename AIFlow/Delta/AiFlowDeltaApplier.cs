using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Automation.AIFlow
{
    public static class AiFlowDeltaApplier
    {
        public const string DeltaVersion = "delta-1";

        private static readonly JsonSerializerSettings CloneSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MissingMemberHandling = MissingMemberHandling.Error
        };

        public static AiCoreFlow Apply(AiCoreFlow baseCore, AiFlowDelta delta, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (baseCore == null)
            {
                issues.Add(new AiFlowIssue("DELTA_BASE_NULL", "base core 为空", "delta"));
                return null;
            }
            if (delta == null)
            {
                issues.Add(new AiFlowIssue("DELTA_NULL", "delta 为空", "delta"));
                return null;
            }
            if (!string.Equals(delta.Version, DeltaVersion, StringComparison.Ordinal))
            {
                issues.Add(new AiFlowIssue("DELTA_VERSION", $"delta 版本不匹配:{delta.Version}", "delta.version"));
            }
            if (delta.Ops == null)
            {
                issues.Add(new AiFlowIssue("DELTA_OPS_NULL", "delta.ops 为空", "delta.ops"));
            }
            if (issues.Count > 0)
            {
                return null;
            }

            AiCoreFlow core = CloneCore(baseCore, issues);
            if (core == null)
            {
                return null;
            }

            foreach (AiFlowDeltaOp op in delta.Ops)
            {
                if (op == null)
                {
                    issues.Add(new AiFlowIssue("DELTA_OP_NULL", "delta op 为空", "delta.ops"));
                    continue;
                }
                string opType = op.Type;
                if (string.IsNullOrWhiteSpace(opType))
                {
                    issues.Add(new AiFlowIssue("DELTA_OP_TYPE_EMPTY", "delta op.type 为空", "delta.ops"));
                    continue;
                }

                switch (opType)
                {
                    case "add_proc":
                        ApplyAddProc(core, op, issues);
                        break;
                    case "remove_proc":
                        ApplyRemoveProc(core, op, issues);
                        break;
                    case "add_step":
                        ApplyAddStep(core, op, issues);
                        break;
                    case "remove_step":
                        ApplyRemoveStep(core, op, issues);
                        break;
                    case "add_op":
                        ApplyAddOp(core, op, issues);
                        break;
                    case "remove_op":
                        ApplyRemoveOp(core, op, issues);
                        break;
                    case "move_op":
                        ApplyMoveOp(core, op, issues);
                        break;
                    case "replace_args":
                        ApplyReplaceArgs(core, op, issues);
                        break;
                    case "replace_op":
                        ApplyReplaceOp(core, op, issues);
                        break;
                    default:
                        issues.Add(new AiFlowIssue("DELTA_OP_UNKNOWN", $"未知 delta op.type:{opType}", "delta.ops"));
                        break;
                }
            }

            if (issues.Count > 0)
            {
                return null;
            }

            NormalizeCore(core, issues);
            if (issues.Count > 0)
            {
                return null;
            }

            return core;
        }

        private static AiCoreFlow CloneCore(AiCoreFlow baseCore, List<AiFlowIssue> issues)
        {
            try
            {
                string json = JsonConvert.SerializeObject(baseCore, CloneSettings);
                return JsonConvert.DeserializeObject<AiCoreFlow>(json, CloneSettings);
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("DELTA_CLONE_FAIL", ex.Message, "delta"));
                return null;
            }
        }

        private static void ApplyAddProc(AiCoreFlow core, AiFlowDeltaOp op, List<AiFlowIssue> issues)
        {
            if (op.Proc == null)
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_PROC_NULL", "add_proc 缺少 proc", "delta.ops"));
                return;
            }
            if (string.IsNullOrWhiteSpace(op.Proc.Id))
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_PROC_ID", "add_proc.proc.id 为空", "delta.ops"));
                return;
            }
            if (FindProc(core, op.Proc.Id) != null)
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_PROC_DUP", $"流程已存在:{op.Proc.Id}", "delta.ops"));
                return;
            }
            EnsureProcs(core);
            int index = NormalizeInsertIndex(op.Index, core.Procs.Count, issues, "proc", op.Proc.Id);
            if (index < 0)
            {
                return;
            }
            core.Procs.Insert(index, op.Proc);
        }

        private static void ApplyRemoveProc(AiCoreFlow core, AiFlowDeltaOp op, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(op.ProcId))
            {
                issues.Add(new AiFlowIssue("DELTA_REMOVE_PROC_ID", "remove_proc 缺少 procId", "delta.ops"));
                return;
            }
            AiCoreProc proc = FindProc(core, op.ProcId);
            if (proc == null)
            {
                issues.Add(new AiFlowIssue("DELTA_REMOVE_PROC_MISSING", $"流程不存在:{op.ProcId}", "delta.ops"));
                return;
            }
            core.Procs.Remove(proc);
        }

        private static void ApplyAddStep(AiCoreFlow core, AiFlowDeltaOp op, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(op.ProcId))
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_STEP_PROC", "add_step 缺少 procId", "delta.ops"));
                return;
            }
            if (op.Step == null)
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_STEP_NULL", "add_step 缺少 step", "delta.ops"));
                return;
            }
            if (string.IsNullOrWhiteSpace(op.Step.Id))
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_STEP_ID", "add_step.step.id 为空", "delta.ops"));
                return;
            }
            AiCoreProc proc = FindProc(core, op.ProcId);
            if (proc == null)
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_STEP_PROC_MISSING", $"流程不存在:{op.ProcId}", "delta.ops"));
                return;
            }
            EnsureSteps(proc);
            if (FindStep(proc, op.Step.Id) != null)
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_STEP_DUP", $"步骤已存在:{op.Step.Id}", "delta.ops"));
                return;
            }
            int index = NormalizeInsertIndex(op.Index, proc.Steps.Count, issues, "step", op.Step.Id);
            if (index < 0)
            {
                return;
            }
            proc.Steps.Insert(index, op.Step);
        }

        private static void ApplyRemoveStep(AiCoreFlow core, AiFlowDeltaOp op, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(op.ProcId) || string.IsNullOrWhiteSpace(op.StepId))
            {
                issues.Add(new AiFlowIssue("DELTA_REMOVE_STEP_ID", "remove_step 缺少 procId/stepId", "delta.ops"));
                return;
            }
            AiCoreProc proc = FindProc(core, op.ProcId);
            if (proc == null)
            {
                issues.Add(new AiFlowIssue("DELTA_REMOVE_STEP_PROC_MISSING", $"流程不存在:{op.ProcId}", "delta.ops"));
                return;
            }
            AiCoreStep step = FindStep(proc, op.StepId);
            if (step == null)
            {
                issues.Add(new AiFlowIssue("DELTA_REMOVE_STEP_MISSING", $"步骤不存在:{op.StepId}", "delta.ops"));
                return;
            }
            proc.Steps.Remove(step);
        }

        private static void ApplyAddOp(AiCoreFlow core, AiFlowDeltaOp op, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(op.ProcId) || string.IsNullOrWhiteSpace(op.StepId))
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_OP_LOC", "add_op 缺少 procId/stepId", "delta.ops"));
                return;
            }
            if (op.Op == null)
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_OP_NULL", "add_op 缺少 op", "delta.ops"));
                return;
            }
            if (string.IsNullOrWhiteSpace(op.Op.Id))
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_OP_ID", "add_op.op.id 为空", "delta.ops"));
                return;
            }
            AiCoreStep step = FindStep(core, op.ProcId, op.StepId, issues);
            if (step == null)
            {
                return;
            }
            EnsureOps(step);
            if (FindOp(step, op.Op.Id) != null)
            {
                issues.Add(new AiFlowIssue("DELTA_ADD_OP_DUP", $"操作已存在:{op.Op.Id}", "delta.ops"));
                return;
            }
            int index = NormalizeInsertIndex(op.Index, step.Ops.Count, issues, "op", op.Op.Id);
            if (index < 0)
            {
                return;
            }
            step.Ops.Insert(index, op.Op);
        }

        private static void ApplyRemoveOp(AiCoreFlow core, AiFlowDeltaOp op, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(op.ProcId) || string.IsNullOrWhiteSpace(op.StepId) || string.IsNullOrWhiteSpace(op.OpId))
            {
                issues.Add(new AiFlowIssue("DELTA_REMOVE_OP_ID", "remove_op 缺少 procId/stepId/opId", "delta.ops"));
                return;
            }
            AiCoreStep step = FindStep(core, op.ProcId, op.StepId, issues);
            if (step == null)
            {
                return;
            }
            AiCoreOp target = FindOp(step, op.OpId);
            if (target == null)
            {
                issues.Add(new AiFlowIssue("DELTA_REMOVE_OP_MISSING", $"操作不存在:{op.OpId}", "delta.ops"));
                return;
            }
            step.Ops.Remove(target);
        }

        private static void ApplyMoveOp(AiCoreFlow core, AiFlowDeltaOp op, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(op.ProcId) || string.IsNullOrWhiteSpace(op.StepId) || string.IsNullOrWhiteSpace(op.OpId))
            {
                issues.Add(new AiFlowIssue("DELTA_MOVE_OP_ID", "move_op 缺少 procId/stepId/opId", "delta.ops"));
                return;
            }
            if (!op.Index.HasValue)
            {
                issues.Add(new AiFlowIssue("DELTA_MOVE_OP_INDEX", "move_op 缺少 index", "delta.ops"));
                return;
            }
            AiCoreStep step = FindStep(core, op.ProcId, op.StepId, issues);
            if (step == null)
            {
                return;
            }
            AiCoreOp target = FindOp(step, op.OpId);
            if (target == null)
            {
                issues.Add(new AiFlowIssue("DELTA_MOVE_OP_MISSING", $"操作不存在:{op.OpId}", "delta.ops"));
                return;
            }
            int index = NormalizeMoveIndex(op.Index, step.Ops.Count, issues, "op", op.OpId);
            if (index < 0)
            {
                return;
            }
            step.Ops.Remove(target);
            if (index > step.Ops.Count)
            {
                index = step.Ops.Count;
            }
            step.Ops.Insert(index, target);
        }

        private static void ApplyReplaceArgs(AiCoreFlow core, AiFlowDeltaOp op, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(op.ProcId) || string.IsNullOrWhiteSpace(op.StepId) || string.IsNullOrWhiteSpace(op.OpId))
            {
                issues.Add(new AiFlowIssue("DELTA_REPLACE_ARGS_ID", "replace_args 缺少 procId/stepId/opId", "delta.ops"));
                return;
            }
            if (op.Args == null)
            {
                issues.Add(new AiFlowIssue("DELTA_REPLACE_ARGS_NULL", "replace_args 缺少 args", "delta.ops"));
                return;
            }
            AiCoreStep step = FindStep(core, op.ProcId, op.StepId, issues);
            if (step == null)
            {
                return;
            }
            AiCoreOp target = FindOp(step, op.OpId);
            if (target == null)
            {
                issues.Add(new AiFlowIssue("DELTA_REPLACE_ARGS_MISSING", $"操作不存在:{op.OpId}", "delta.ops"));
                return;
            }
            target.Args = op.Args;
        }

        private static void ApplyReplaceOp(AiCoreFlow core, AiFlowDeltaOp op, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(op.ProcId) || string.IsNullOrWhiteSpace(op.StepId))
            {
                issues.Add(new AiFlowIssue("DELTA_REPLACE_OP_LOC", "replace_op 缺少 procId/stepId", "delta.ops"));
                return;
            }
            if (op.Op == null)
            {
                issues.Add(new AiFlowIssue("DELTA_REPLACE_OP_NULL", "replace_op 缺少 op", "delta.ops"));
                return;
            }
            if (string.IsNullOrWhiteSpace(op.Op.Id))
            {
                issues.Add(new AiFlowIssue("DELTA_REPLACE_OP_ID", "replace_op.op.id 为空", "delta.ops"));
                return;
            }
            AiCoreStep step = FindStep(core, op.ProcId, op.StepId, issues);
            if (step == null)
            {
                return;
            }
            AiCoreOp target = FindOp(step, op.Op.Id);
            if (target == null)
            {
                issues.Add(new AiFlowIssue("DELTA_REPLACE_OP_MISSING", $"操作不存在:{op.Op.Id}", "delta.ops"));
                return;
            }
            int index = step.Ops.IndexOf(target);
            step.Ops[index] = op.Op;
        }

        private static AiCoreProc FindProc(AiCoreFlow core, string procId)
        {
            return core?.Procs?.FirstOrDefault(p => string.Equals(p?.Id, procId, StringComparison.Ordinal));
        }

        private static AiCoreStep FindStep(AiCoreProc proc, string stepId)
        {
            return proc?.Steps?.FirstOrDefault(s => string.Equals(s?.Id, stepId, StringComparison.Ordinal));
        }

        private static AiCoreOp FindOp(AiCoreStep step, string opId)
        {
            return step?.Ops?.FirstOrDefault(o => string.Equals(o?.Id, opId, StringComparison.Ordinal));
        }

        private static AiCoreStep FindStep(AiCoreFlow core, string procId, string stepId, List<AiFlowIssue> issues)
        {
            AiCoreProc proc = FindProc(core, procId);
            if (proc == null)
            {
                issues.Add(new AiFlowIssue("DELTA_STEP_PROC_MISSING", $"流程不存在:{procId}", "delta.ops"));
                return null;
            }
            AiCoreStep step = FindStep(proc, stepId);
            if (step == null)
            {
                issues.Add(new AiFlowIssue("DELTA_STEP_MISSING", $"步骤不存在:{stepId}", "delta.ops"));
            }
            return step;
        }

        private static void EnsureProcs(AiCoreFlow core)
        {
            if (core.Procs == null)
            {
                core.Procs = new List<AiCoreProc>();
            }
        }

        private static void EnsureSteps(AiCoreProc proc)
        {
            if (proc.Steps == null)
            {
                proc.Steps = new List<AiCoreStep>();
            }
        }

        private static void EnsureOps(AiCoreStep step)
        {
            if (step.Ops == null)
            {
                step.Ops = new List<AiCoreOp>();
            }
        }

        private static int NormalizeInsertIndex(int? index, int count, List<AiFlowIssue> issues, string kind, string id)
        {
            if (!index.HasValue)
            {
                return count < 0 ? 0 : count;
            }
            int value = index.Value;
            if (value < 0 || value > count)
            {
                issues.Add(new AiFlowIssue("DELTA_INDEX_RANGE", $"{kind} 插入索引超界:{id} index={value}", "delta.ops"));
                return -1;
            }
            return value;
        }

        private static int NormalizeMoveIndex(int? index, int count, List<AiFlowIssue> issues, string kind, string id)
        {
            if (!index.HasValue)
            {
                issues.Add(new AiFlowIssue("DELTA_INDEX_REQUIRED", $"{kind} 移动索引缺失:{id}", "delta.ops"));
                return -1;
            }
            int value = index.Value;
            if (value < 0 || value >= count)
            {
                issues.Add(new AiFlowIssue("DELTA_INDEX_RANGE", $"{kind} 移动索引超界:{id} index={value}", "delta.ops"));
                return -1;
            }
            return value;
        }

        private static void NormalizeCore(AiCoreFlow core, List<AiFlowIssue> issues)
        {
            if (core == null)
            {
                issues.Add(new AiFlowIssue("DELTA_CORE_NULL", "core 为空", "delta"));
                return;
            }
            if (core.Procs == null)
            {
                issues.Add(new AiFlowIssue("DELTA_CORE_PROCS_NULL", "core.procs 为空", "delta"));
                return;
            }
            var procIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AiCoreProc proc in core.Procs)
            {
                if (proc == null)
                {
                    issues.Add(new AiFlowIssue("DELTA_PROC_NULL", "流程为空", "delta"));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(proc.Id))
                {
                    issues.Add(new AiFlowIssue("DELTA_PROC_ID", "流程 id 为空", "delta"));
                }
                else if (!procIds.Add(proc.Id))
                {
                    issues.Add(new AiFlowIssue("DELTA_PROC_ID_DUP", $"流程 id 重复:{proc.Id}", "delta"));
                }
                if (proc.Steps == null)
                {
                    issues.Add(new AiFlowIssue("DELTA_PROC_STEPS_NULL", $"流程步骤为空:{proc.Id}", "delta"));
                    continue;
                }
                var stepIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (AiCoreStep step in proc.Steps)
                {
                    if (step == null)
                    {
                        issues.Add(new AiFlowIssue("DELTA_STEP_NULL", $"步骤为空:{proc.Id}", "delta"));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(step.Id))
                    {
                        issues.Add(new AiFlowIssue("DELTA_STEP_ID", $"步骤 id 为空:{proc.Id}", "delta"));
                    }
                    else if (!stepIds.Add(step.Id))
                    {
                        issues.Add(new AiFlowIssue("DELTA_STEP_ID_DUP", $"步骤 id 重复:{step.Id}", "delta"));
                    }
                    if (step.Ops == null)
                    {
                        issues.Add(new AiFlowIssue("DELTA_STEP_OPS_NULL", $"步骤操作为空:{step.Id}", "delta"));
                        continue;
                    }
                    var opIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (AiCoreOp op in step.Ops)
                    {
                        if (op == null)
                        {
                            issues.Add(new AiFlowIssue("DELTA_OP_NULL", $"操作为空:{step.Id}", "delta"));
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(op.Id))
                        {
                            issues.Add(new AiFlowIssue("DELTA_OP_ID", $"操作 id 为空:{step.Id}", "delta"));
                        }
                        else if (!opIds.Add(op.Id))
                        {
                            issues.Add(new AiFlowIssue("DELTA_OP_ID_DUP", $"操作 id 重复:{op.Id}", "delta"));
                        }
                    }
                }
            }
        }
    }
}
