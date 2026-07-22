using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。

using Newtonsoft.Json.Linq;
using Automation.Protocol;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using static System.ComponentModel.TypeConverter;

namespace Automation.Bridge
{
    internal sealed partial class AutomationBridgeService
    {
        private JObject BuildProcOverview(int procIndex, Proc proc)
        {
            EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
            ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                procIndex, proc, runtime.Stores.Processes?.Items,
                runtime.CreateProcessValidationContext(), runtime.Stores.Values);
            JArray steps = new JArray();
            if (proc?.steps != null)
            {
                for (int i = 0; i < proc.steps.Count; i++)
                {
                    Step step = proc.steps[i];
                    JArray ops = new JArray();
                    if (step?.Ops != null)
                    {
                        for (int j = 0; j < step.Ops.Count; j++)
                        {
                            OperationType op = step.Ops[j];
                            ops.Add(new JObject
                            {
                                ["opIndex"] = j,
                                ["opId"] = op?.Id.ToString("D"),
                                ["name"] = op?.Name ?? string.Empty,
                                ["operaType"] = op?.OperaType ?? string.Empty,
                                ["disable"] = op?.Disable ?? false,
                                ["summary"] = op == null ? string.Empty : BuildOperationSummary(op)
                            });
                        }
                    }

                    steps.Add(new JObject
                    {
                        ["stepIndex"] = i,
                        ["stepId"] = step?.Id.ToString("D"),
                        ["name"] = step?.Name ?? string.Empty,
                        ["disable"] = step?.Disable ?? false,
                        ["opCount"] = step?.Ops?.Count ?? 0,
                        ["ops"] = ops
                    });
                }
            }

            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procId"] = proc?.head?.Id.ToString("D"),
                ["name"] = proc?.head?.Name ?? string.Empty,
                ["autoStart"] = proc?.head?.AutoStart ?? false,
                ["disable"] = proc?.head?.Disable ?? false,
                ["state"] = snapshot?.State.ToString() ?? ProcRunState.Stopped.ToString(),
                ["readinessStatus"] = readiness.ReadinessStatus,
                ["runnable"] = readiness.Runnable,
                ["warnings"] = new JArray(readiness.Warnings),
                ["runBlockers"] = new JArray(readiness.RunBlockers),
                ["stepCount"] = proc?.steps?.Count ?? 0,
                ["operationCount"] = CountOperations(proc),
                ["steps"] = steps
            };
        }

        private static JObject BuildProcDetailOmitted(
            int procIndex,
            Proc proc,
            int operationCount,
            int? detailUtf8Bytes = null)
        {
            var steps = new JArray();
            if (proc?.steps != null)
            {
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    steps.Add(new JObject
                    {
                        ["stepIndex"] = stepIndex,
                        ["stepId"] = step?.Id.ToString("D"),
                        ["name"] = step?.Name ?? string.Empty,
                        ["disable"] = step?.Disable ?? false,
                        ["opCount"] = step?.Ops?.Count ?? 0
                    });
                }
            }

            string reasonCode = operationCount > MaxDetailOperationCount
                ? "OPERATION_COUNT_EXCEEDED"
                : "SERIALIZED_SIZE_EXCEEDED";
            string reason = operationCount > MaxDetailOperationCount
                ? $"流程包含{operationCount}条指令，超过完整详情上限{MaxDetailOperationCount}条。"
                : $"完整详情序列化后为{detailUtf8Bytes.GetValueOrDefault()}字节，超过{MaxProcDetailUtf8Bytes}字节上限。";
            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procId"] = proc?.head?.Id.ToString("D"),
                ["name"] = proc?.head?.Name ?? string.Empty,
                ["detailAvailable"] = false,
                ["reasonCode"] = reasonCode,
                ["reason"] = reason,
                ["operationCount"] = operationCount,
                ["detailOperationLimit"] = MaxDetailOperationCount,
                ["detailUtf8Bytes"] = detailUtf8Bytes,
                ["detailUtf8ByteLimit"] = MaxProcDetailUtf8Bytes,
                ["stepCount"] = proc?.steps?.Count ?? 0,
                ["steps"] = steps,
                ["nextReadOptions"] = new JArray(
                    "调用 get_proc_overview 获取含 opId 的轻量指令目录",
                    "调用 get_step_detail 读取一个步骤",
                    $"调用 get_op_details 按明确 opId 批量读取，单次最多{MaxBatchReadOperationCount}条")
            };
        }

        private JObject BuildProcDetail(int procIndex, Proc proc)
        {
            JObject overview = BuildProcOverview(procIndex, proc);
            overview["head"] = new JObject
            {
                ["fields"] = new JObject
                {
                    ["Name"] = proc?.head?.Name ?? string.Empty,
                    ["AutoStart"] = proc?.head?.AutoStart ?? false,
                    ["Disable"] = proc?.head?.Disable ?? false
                }
            };

            JArray stepDetails = new JArray();
            if (proc?.steps != null)
            {
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    JArray opDetails = new JArray();
                    if (step?.Ops != null)
                    {
                        for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                        {
                            OperationType op = step.Ops[opIndex];
                            bool isJump = IsJumpOperation(op);
                            string flow = BuildFlowDescription(op, opIndex, step.Ops.Count);
                            opDetails.Add(new JObject
                            {
                                ["opIndex"] = opIndex,
                                ["opId"] = op?.Id.ToString("D"),
                                ["name"] = op?.Name ?? string.Empty,
                                ["operaType"] = op?.OperaType ?? string.Empty,
                                ["disable"] = op?.Disable ?? false,
                                ["isJump"] = isJump,
                                ["flow"] = flow,
                                ["summary"] = op == null ? string.Empty : BuildOperationSummary(op),
                                ["fields"] = op == null ? new JObject() : BuildWritableOperationFields(op)
                            });
                        }
                    }

                    stepDetails.Add(new JObject
                    {
                        ["stepIndex"] = stepIndex,
                        ["stepId"] = step?.Id.ToString("D"),
                        ["name"] = step?.Name ?? string.Empty,
                        ["disable"] = step?.Disable ?? false,
                        ["fields"] = new JObject
                        {
                            ["Name"] = step?.Name ?? string.Empty,
                            ["Disable"] = step?.Disable ?? false
                        },
                        ["ops"] = opDetails
                    });
                }
            }

            overview["steps"] = stepDetails;

            // 跳转目标有效性检查：删除/插入指令后 opIndex 会变化，旧跳转目标可能越界。
            // 将无效跳转目标列为 warnings，让 AI 在读取流程详情时直接发现，不必额外调用 diagnose_proc。
            JArray gotoWarnings = new JArray();
            foreach (string error in ProcessDefinitionService.ValidateProcGotoTargets(procIndex, proc))
            {
                gotoWarnings.Add(new JObject { ["message"] = error });
            }
            if (gotoWarnings.Count > 0)
            {
                overview["gotoWarnings"] = gotoWarnings;
            }

            return overview;
        }

    }
}
