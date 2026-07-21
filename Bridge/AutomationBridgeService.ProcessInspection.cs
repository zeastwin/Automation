using Newtonsoft.Json;
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
        private JObject HandleGetOperationDetail(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int stepIndex = ReadRequiredInt(request, "stepIndex");
            int opIndex = ReadRequiredInt(request, "opIndex");

            Proc proc = GetProcByIndex(procIndex);
            if (proc.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                return BridgeError(400, "STEP_NOT_FOUND", $"步骤索引越界：{stepIndex}");
            }
            Step step = proc.steps[stepIndex];
            if (step.Ops == null || opIndex < 0 || opIndex >= step.Ops.Count)
            {
                return BridgeError(400, "OP_NOT_FOUND", $"指令索引越界：{opIndex}");
            }
            IReadOnlyList<string> gotoErrors = ProcessDefinitionService.ValidateProcGotoTargets(procIndex, proc);
            return BuildOperationDetail(proc, procIndex, stepIndex, opIndex, step, step.Ops[opIndex], gotoErrors);
        }

        // 按稳定 opId 有限批量读取指令，避免调用方维护容易漂移的 stepIndex/opIndex 组合。
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetOperationDetails(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            JArray opIdTokens = ReadRequiredArray(request, "opIds");
            if (opIdTokens.Count < 1 || opIdTokens.Count > MaxBatchReadOperationCount)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    $"opIds 数量必须在1..{MaxBatchReadOperationCount}之间。");
            }

            var opIds = new List<Guid>(opIdTokens.Count);
            var uniqueIds = new HashSet<Guid>();
            for (int i = 0; i < opIdTokens.Count; i++)
            {
                JToken token = opIdTokens[i];
                if (token == null || token.Type != JTokenType.String)
                {
                    return BridgeError(400, "INVALID_ARGUMENT", $"opIds[{i}] 必须是 Guid 字符串。");
                }

                Guid opId;
                try
                {
                    opId = ParseGuid(token.Value<string>(), $"opIds[{i}]");
                }
                catch (BridgeRequestException ex)
                {
                    return BridgeError(ex.StatusCode, ex.Code, ex.Message);
                }
                if (opId == Guid.Empty)
                {
                    return BridgeError(400, "INVALID_ARGUMENT", $"opIds[{i}] 不能是空 Guid。");
                }
                if (!uniqueIds.Add(opId))
                {
                    return BridgeError(400, "INVALID_ARGUMENT", $"opIds 不允许重复：{opId:D}");
                }
                opIds.Add(opId);
            }

            if (!TryGetProcByIndexForRead(procIndex, out Proc proc, out JObject error))
            {
                return error;
            }

            var locations = new Dictionary<Guid, Tuple<int, int, Step, OperationType>>();
            if (proc.steps != null)
            {
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    if (step?.Ops == null)
                    {
                        continue;
                    }
                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        OperationType op = step.Ops[opIndex];
                        if (op != null && uniqueIds.Contains(op.Id))
                        {
                            locations[op.Id] = Tuple.Create(stepIndex, opIndex, step, op);
                        }
                    }
                }
            }

            Guid missingId = opIds.FirstOrDefault(opId => !locations.ContainsKey(opId));
            if (missingId != Guid.Empty)
            {
                return BridgeError(404, "OP_NOT_FOUND",
                    $"流程 {procIndex} 中未找到指令：{missingId:D}。opId 必须来自该流程的 get_proc_overview、get_proc_detail 或 get_step_detail 返回值。");
            }

            IReadOnlyList<string> gotoErrors = ProcessDefinitionService.ValidateProcGotoTargets(procIndex, proc);
            var items = new JArray();
            foreach (Guid opId in opIds)
            {
                Tuple<int, int, Step, OperationType> location = locations[opId];
                items.Add(BuildOperationDetail(
                    proc,
                    procIndex,
                    location.Item1,
                    location.Item2,
                    location.Item3,
                    location.Item4,
                    gotoErrors));
            }

            var result = new JObject
            {
                ["procIndex"] = procIndex,
                ["procId"] = proc.head?.Id.ToString("D"),
                ["procName"] = proc.head?.Name ?? string.Empty,
                ["requestedCount"] = opIds.Count,
                ["batchOperationLimit"] = MaxBatchReadOperationCount,
                ["batchUtf8ByteLimit"] = MaxBatchReadUtf8Bytes,
                ["resultUtf8Bytes"] = 0,
                ["operations"] = items
            };
            int resultBytes = Encoding.UTF8.GetByteCount(result.ToString(Formatting.None));
            result["resultUtf8Bytes"] = resultBytes;
            resultBytes = Encoding.UTF8.GetByteCount(result.ToString(Formatting.None));
            if (resultBytes > MaxBatchReadUtf8Bytes)
            {
                return BridgeError(413, "OP_DETAILS_TOO_LARGE",
                    $"{opIds.Count}条指令详情序列化后为{resultBytes}字节，超过批量读取上限{MaxBatchReadUtf8Bytes}字节；请减少 opIds 后重试。");
            }
            result["resultUtf8Bytes"] = resultBytes;
            return result;
        }

        private JObject BuildOperationDetail(
            Proc proc,
            int procIndex,
            int stepIndex,
            int opIndex,
            Step step,
            OperationType op,
            IReadOnlyList<string> gotoErrors)
        {
            bool isJump = IsJumpOperation(op);
            string flow = BuildFlowDescription(op, opIndex, step?.Ops?.Count ?? 0);
            var gotoIssues = new JArray();
            if (isJump && gotoErrors != null)
            {
                foreach (string gotoError in gotoErrors)
                {
                    if (gotoError.Contains($"{stepIndex}-{opIndex}")
                        || gotoError.Contains($"步骤 {stepIndex} 指令 {opIndex}"))
                    {
                        gotoIssues.Add(new JObject { ["message"] = gotoError });
                    }
                }
            }

            JObject fields = op == null ? new JObject() : BuildWritableOperationFields(op);
            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procId"] = proc?.head?.Id.ToString("D"),
                ["stepIndex"] = stepIndex,
                ["stepId"] = step?.Id.ToString("D"),
                ["stepName"] = step?.Name ?? string.Empty,
                ["opIndex"] = opIndex,
                ["opId"] = op?.Id.ToString("D"),
                ["name"] = op?.Name ?? string.Empty,
                ["operaType"] = op?.OperaType ?? string.Empty,
                ["disable"] = op?.Disable ?? false,
                ["IsBreakpoint"] = op?.IsBreakpoint ?? false,
                ["isJump"] = isJump,
                ["flow"] = flow,
                ["summary"] = op == null ? string.Empty : BuildOperationSummary(op),
                ["fields"] = fields,
                ["gotoIssues"] = gotoIssues
            };
        }

        private JObject BuildWritableOperationFields(OperationType operation)
        {
            return WithOperationReadContext(operation, () =>
            {
                RefreshOperationContext(operation);
                return StructuredOperationCompiler.BuildWritableFields(
                    operation,
                    address => TryResolvePhysicalGotoTarget(address, out JObject target) ? target : null);
            });
        }

        private bool TryResolvePhysicalGotoTarget(string address, out JObject target)
        {
            target = null;
            string[] parts = (address ?? string.Empty).Split('-');
            if (parts.Length != 3
                || !int.TryParse(parts[0], out int procIndex)
                || !int.TryParse(parts[1], out int stepIndex)
                || !int.TryParse(parts[2], out int opIndex)
                || procIndex < 0
                || stepIndex < 0
                || opIndex < 0
                || runtime.EditorUi?.IsReady != true
                || procIndex >= runtime.Stores.Processes.Items.Count)
            {
                return false;
            }

            Proc proc = runtime.Stores.Processes.Items[procIndex];
            if (proc?.steps == null || stepIndex >= proc.steps.Count)
            {
                return false;
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex >= step.Ops.Count)
            {
                return false;
            }
            OperationType operation = step.Ops[opIndex];
            if (operation == null || operation.Id == Guid.Empty)
            {
                return false;
            }

            target = new JObject { ["operationId"] = operation.Id.ToString("D") };
            return true;
        }

        private static bool IsMatchingReferenceType(string expected, string actual)
        {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
            {
                return false;
            }
            if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(expected, "io", StringComparison.OrdinalIgnoreCase))
            {
                return actual.StartsWith("io.", StringComparison.OrdinalIgnoreCase);
            }
            if (string.Equals(expected, "comm", StringComparison.OrdinalIgnoreCase))
            {
                return actual.StartsWith("comm.", StringComparison.OrdinalIgnoreCase);
            }
            return (string.Equals(expected, "comm.tcp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(expected, "comm.serial", StringComparison.OrdinalIgnoreCase))
                && string.Equals(actual, "comm.all", StringComparison.OrdinalIgnoreCase);
        }

        // 读取单个步骤的完整指令列表。介于 get_proc_overview 和 get_proc_detail 之间的颗粒度。
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetStepDetail(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int stepIndex = ReadRequiredInt(request, "stepIndex");
            int opOffset = ReadOptionalInt(request, "opOffset") ?? 0;
            int opLimit = ReadOptionalInt(request, "opLimit") ?? MaxStepDetailOperationCount;
            if (opOffset < 0 || opLimit < 1 || opLimit > MaxStepDetailOperationCount)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    $"opOffset必须大于等于0，opLimit必须在1..{MaxStepDetailOperationCount}范围内。");
            }

            Proc proc = GetProcByIndex(procIndex);
            if (proc.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                return BridgeError(400, "STEP_NOT_FOUND", $"步骤索引越界：{stepIndex}");
            }
            Step step = proc.steps[stepIndex];
            int operationCount = step?.Ops?.Count ?? 0;
            int endExclusive = (int)Math.Min(operationCount, (long)opOffset + opLimit);

            JArray opDetails = new JArray();
            bool returnFullDetail = operationCount <= MaxStepDetailOperationCount;
            if (step?.Ops != null)
            {
                for (int opIndex = opOffset; opIndex < endExclusive; opIndex++)
                {
                    OperationType op = step.Ops[opIndex];
                    bool isJump = IsJumpOperation(op);
                    string flow = BuildFlowDescription(op, opIndex, operationCount);
                    JObject item = new JObject
                    {
                        ["opIndex"] = opIndex,
                        ["opId"] = op?.Id.ToString("D"),
                        ["name"] = op?.Name ?? string.Empty,
                        ["operaType"] = op?.OperaType ?? string.Empty,
                        ["disable"] = op?.Disable ?? false,
                        ["isJump"] = isJump,
                        ["flow"] = flow,
                        ["summary"] = CompactDiagnosticText(
                            op == null ? string.Empty : BuildOperationSummary(op), 400)
                    };
                    if (returnFullDetail)
                    {
                        item["fields"] = op == null ? new JObject() : BuildWritableOperationFields(op);
                    }
                    opDetails.Add(item);
                }
            }

            JObject result = new JObject
            {
                ["procIndex"] = procIndex,
                ["procName"] = proc.head?.Name ?? string.Empty,
                ["stepIndex"] = stepIndex,
                ["stepId"] = step.Id.ToString("D"),
                ["stepName"] = step.Name ?? string.Empty,
                ["stepDisable"] = step.Disable,
                ["opCount"] = operationCount,
                ["opOffset"] = opOffset,
                ["opLimit"] = opLimit,
                ["returned"] = opDetails.Count,
                ["hasMore"] = (long)opOffset + opDetails.Count < operationCount,
                ["nextOpOffset"] = (long)opOffset + opDetails.Count < operationCount
                    ? (JToken)((long)opOffset + opDetails.Count)
                    : JValue.CreateNull(),
                ["detailMode"] = returnFullDetail ? "full" : "directory",
                ["operations"] = opDetails
            };
            int resultBytes = Encoding.UTF8.GetByteCount(result.ToString(Formatting.None));
            if (returnFullDetail && resultBytes > MaxBatchReadUtf8Bytes)
            {
                foreach (JObject item in opDetails.OfType<JObject>())
                {
                    item.Remove("fields");
                }
                result["detailMode"] = "directory";
                result["detailOmittedReason"] = "full_detail_exceeds_result_budget";
                result["fullDetailUtf8Bytes"] = resultBytes;
                result["resultUtf8ByteLimit"] = MaxBatchReadUtf8Bytes;
                result["exactRead"] = "使用operations中的opId调用get_op_details，单次最多25条。";
            }
            return result;
        }

        private static string CompactDiagnosticText(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
            {
                return value ?? string.Empty;
            }
            return value.Substring(0, maximumLength) + "…";
        }

        // 按条件搜索指令：支持按流程范围、指令类型、关键词（指令名/字段值）过滤。
        // 用于快速定位"哪些指令引用了变量X""哪些是跳转类指令""哪些IO操作用了Y"等问题。
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleSearchOperations(JObject request)
        {
            int? procIndex = ReadOptionalInt(request, "procIndex");
            string operaType = ReadOptionalString(request, "operaType");
            string keyword = ReadOptionalString(request, "keyword");
            int offset = ReadOptionalInt(request, "offset") ?? 0;
            int limit = ReadOptionalInt(request, "limit") ?? 50;
            if (offset < 0 || limit < 1 || limit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    "offset必须大于等于0，limit必须在1..100范围内。");
            }

            IList<Proc> procs = runtime.Stores.Processes?.Items;
            if (procs == null)
            {
                return BridgeError(500, "PROCS_UNAVAILABLE", "流程列表不可用。");
            }

            JArray results = new JArray();
            int scannedProcs = 0;
            int matchedCount = 0;
            string keywordLower = string.IsNullOrWhiteSpace(keyword) ? null : keyword.ToLowerInvariant();

            int startProc = procIndex.HasValue ? procIndex.Value : 0;
            int endProc = procIndex.HasValue ? procIndex.Value + 1 : procs.Count;
            if (procIndex.HasValue && (startProc < 0 || startProc >= procs.Count))
            {
                return BridgeError(404, "PROC_NOT_FOUND", $"流程索引不存在：{startProc}");
            }

            for (int pi = startProc; pi < endProc && pi < procs.Count; pi++)
            {
                Proc proc = procs[pi];
                if (proc?.steps == null) continue;
                scannedProcs++;
                for (int si = 0; si < proc.steps.Count; si++)
                {
                    Step step = proc.steps[si];
                    if (step?.Ops == null) continue;
                    for (int oi = 0; oi < step.Ops.Count; oi++)
                    {
                        OperationType op = step.Ops[oi];
                        if (op == null) continue;

                        // 按指令类型过滤
                        if (!string.IsNullOrWhiteSpace(operaType) &&
                            !string.Equals(op.OperaType, operaType, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // 按关键词过滤（匹配指令名或字段值的文本）
                        if (keywordLower != null)
                        {
                            string summary = BuildOperationSummary(op) ?? string.Empty;
                            if (!summary.ToLowerInvariant().Contains(keywordLower) &&
                                !(op.Name?.ToLowerInvariant().Contains(keywordLower) ?? false))
                            {
                                continue;
                            }
                        }

                        matchedCount++;
                        if (matchedCount <= offset || results.Count >= limit)
                        {
                            continue;
                        }
                        results.Add(new JObject
                        {
                            ["procIndex"] = pi,
                            ["procId"] = proc?.head?.Id.ToString("D") ?? string.Empty,
                            ["procName"] = proc.head?.Name ?? string.Empty,
                            ["stepIndex"] = si,
                            ["stepId"] = step.Id.ToString("D"),
                            ["stepName"] = step.Name ?? string.Empty,
                            ["opIndex"] = oi,
                            ["opId"] = op.Id.ToString("D"),
                            ["opName"] = op.Name ?? string.Empty,
                            ["operaType"] = op.OperaType ?? string.Empty,
                            ["disable"] = op.Disable,
                            ["summary"] = CompactDiagnosticText(BuildOperationSummary(op), 400)
                        });
                    }
                }
            }

            return new JObject
            {
                ["criteria"] = new JObject
                {
                    ["procIndex"] = procIndex,
                    ["operaType"] = operaType,
                    ["keyword"] = keyword
                },
                ["scannedProcCount"] = scannedProcs,
                ["matchedCount"] = matchedCount,
                ["offset"] = offset,
                ["limit"] = limit,
                ["returned"] = results.Count,
                ["hasMore"] = (long)offset + results.Count < matchedCount,
                ["nextOffset"] = (long)offset + results.Count < matchedCount
                    ? (JToken)((long)offset + results.Count)
                    : JValue.CreateNull(),
                ["results"] = results
            };
        }

        // 轻量级结构验证：聚焦跳转目标有效性、空步骤/指令、禁用项。
        // 比 diagnose_proc 更简洁，不包含运行时状态，适合修改前快速检查。
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleValidateProc(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            Proc proc = GetProcByIndex(procIndex);
            ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                procIndex, proc, runtime.Stores.Processes?.Items,
                runtime.CreateProcessValidationContext(), runtime.Stores.Values);

            JArray errors = new JArray();
            JArray warnings = new JArray();

            // 1. 跳转目标有效性
            foreach (string error in ProcessDefinitionService.ValidateProcGotoTargets(procIndex, proc))
            {
                errors.Add(new JObject { ["message"] = error });
            }

            // 2. 空步骤/指令检查
            if (proc.steps == null || proc.steps.Count == 0)
            {
                warnings.Add(new JObject { ["message"] = "流程尚未添加步骤。" });
            }
            else
            {
                for (int si = 0; si < proc.steps.Count; si++)
                {
                    Step step = proc.steps[si];
                    if (step == null)
                    {
                        errors.Add(new JObject { ["message"] = $"步骤 {si} 为空。" });
                        continue;
                    }
                    if (step.Disable)
                    {
                        warnings.Add(new JObject { ["message"] = $"步骤 {si} [{step.Name}] 已禁用。" });
                    }
                    if (step.Ops == null || step.Ops.Count == 0)
                    {
                        warnings.Add(new JObject { ["message"] = $"步骤 {si} [{step.Name}] 没有指令。" });
                        continue;
                    }
                    for (int oi = 0; oi < step.Ops.Count; oi++)
                    {
                        OperationType op = step.Ops[oi];
                        if (op == null)
                        {
                            errors.Add(new JObject { ["message"] = $"步骤 {si} 指令 {oi} 为空。" });
                        }
                        else if (op.Disable)
                        {
                            warnings.Add(new JObject { ["message"] = $"步骤 {si} 指令 {oi} [{op.Name}] 已禁用。" });
                        }
                        else if (ProcessReadinessService.IsPlaceholder(op))
                        {
                            warnings.Add(new JObject { ["message"] = $"步骤 {si} 指令 {oi} [{op.Name}] 是待完善占位。" });
                        }
                    }
                }
            }

            bool isValid = errors.Count == 0;
            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procName"] = proc.head?.Name ?? string.Empty,
                ["isValid"] = isValid,
                ["readinessStatus"] = readiness.ReadinessStatus,
                ["runnable"] = readiness.Runnable,
                ["runBlockers"] = new JArray(readiness.RunBlockers),
                ["errorCount"] = errors.Count,
                ["warningCount"] = warnings.Count,
                ["errors"] = errors,
                ["warnings"] = warnings
            };
        }

    }
}
