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
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetOperationContext(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int stepIndex = ReadRequiredInt(request, "stepIndex");
            int opIndex = ReadRequiredInt(request, "opIndex");
            int radius = ReadOptionalInt(request, "radius") ?? 2;
            if (radius < 0 || radius > 10)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "radius 必须在0..10范围内。");
            }
            Proc proc = GetProcByIndex(procIndex);
            if (proc.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                return BridgeError(400, "STEP_NOT_FOUND", $"步骤索引越界:{stepIndex}");
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex < 0 || opIndex >= step.Ops.Count)
            {
                return BridgeError(400, "OP_NOT_FOUND", $"指令索引越界:{opIndex}");
            }
            int from = Math.Max(0, opIndex - radius);
            int to = Math.Min(step.Ops.Count - 1, opIndex + radius);
            var operations = new JArray();
            for (int oi = from; oi <= to; oi++)
            {
                OperationType op = step.Ops[oi];
                operations.Add(new JObject
                {
                    ["opIndex"] = oi,
                    ["isTarget"] = oi == opIndex,
                    ["opId"] = op?.Id.ToString("D"),
                    ["name"] = op?.Name ?? string.Empty,
                    ["operaType"] = op?.OperaType ?? string.Empty,
                    ["disable"] = op?.Disable ?? false,
                    ["isJump"] = IsJumpOperation(op),
                    ["summary"] = op == null ? string.Empty : BuildOperationSummary(op),
                    ["fields"] = oi == opIndex && op != null ? BuildWritableOperationFields(op) : null
                });
            }
            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procName"] = proc.head?.Name ?? string.Empty,
                ["stepIndex"] = stepIndex,
                ["stepName"] = step.Name ?? string.Empty,
                ["targetOpIndex"] = opIndex,
                ["window"] = new JObject { ["from"] = from, ["toInclusive"] = to },
                ["operations"] = operations
            };
        }

        private JObject BuildDiagnosticMatch(DiagnosticFieldRecord field, bool truncateValue)
        {
            string value = field.Value ?? string.Empty;
            if (truncateValue && value.Length > 300) value = value.Substring(0, 300);
            return new JObject
            {
                ["procIndex"] = field.ProcIndex,
                ["procId"] = (runtime.Stores.Processes?.Items != null
                    && field.ProcIndex >= 0
                    && field.ProcIndex < runtime.Stores.Processes.Items.Count)
                    ? runtime.Stores.Processes.Items[field.ProcIndex]?.head?.Id.ToString("D") ?? string.Empty
                    : string.Empty,
                ["procName"] = field.ProcName,
                ["stepIndex"] = field.StepIndex,
                ["stepId"] = field.StepId.ToString("D"),
                ["stepName"] = field.StepName,
                ["opIndex"] = field.OpIndex,
                ["opId"] = field.OpId.ToString("D"),
                ["opName"] = field.OpName,
                ["operaType"] = field.OperaType,
                ["field"] = field.Field,
                ["displayName"] = field.DisplayName,
                ["value"] = value
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleAuditProcBatch(JObject request)
        {
            EnsureRuntimeReady();
            int procOffset = ReadOptionalInt(request, "procOffset") ?? 0;
            int procLimit = ReadOptionalInt(request, "procLimit") ?? 20;
            int findingLimit = ReadOptionalInt(request, "findingLimit") ?? 50;
            if (procOffset < 0 || procLimit < 1 || procLimit > 50 || findingLimit < 1 || findingLimit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    "procOffset 必须大于等于0，procLimit 必须在1..50，findingLimit 必须在1..100。");
            }
            int procCount = runtime.Stores.Processes.Items.Count;
            int procEnd = Math.Min(procCount, procOffset + procLimit);
            string indexRevision = GetDiagnosticIndexRevision();
            int totalFindingCount = 0;
            var findings = new JArray();
            var knownOperationTypes = new HashSet<string>(
                OperationDefinitionRegistry.CreateAll()
                    .Where(item => !string.IsNullOrWhiteSpace(item?.OperaType))
                    .Select(item => item.OperaType),
                StringComparer.Ordinal);
            for (int pi = procOffset; pi < procEnd; pi++)
            {
                Proc proc = runtime.Stores.Processes.Items[pi];
                if (proc?.steps == null || proc.steps.Count == 0)
                {
                    AddAuditFinding(findings, findingLimit, ref totalFindingCount, pi, proc, -1, -1,
                        "error", "proc.empty", "流程没有步骤");
                    continue;
                }
                var ids = new HashSet<Guid>();
                foreach (string error in ProcessDefinitionService.ValidateProcGotoTargets(pi, proc))
                {
                    AddAuditFinding(findings, findingLimit, ref totalFindingCount, pi, proc, -1, -1,
                        "error", "goto.invalid", error);
                }
                for (int si = 0; si < proc.steps.Count; si++)
                {
                    Step step = proc.steps[si];
                    if (step != null && step.Id != Guid.Empty && !ids.Add(step.Id))
                    {
                        AddAuditFinding(findings, findingLimit, ref totalFindingCount, pi, proc, si, -1,
                            "error", "id.duplicate", "步骤或指令存在重复ID");
                    }
                    if (step == null || step.Ops == null || step.Ops.Count == 0)
                    {
                        AddAuditFinding(findings, findingLimit, ref totalFindingCount, pi, proc, si, -1,
                            step == null ? "error" : "warning", step == null ? "step.null" : "step.empty",
                            step == null ? "步骤为空" : "步骤没有指令");
                        continue;
                    }
                    if (step.Disable)
                    {
                        AddAuditFinding(findings, findingLimit, ref totalFindingCount, pi, proc, si, -1,
                            "warning", "step.disabled", "步骤已禁用");
                    }
                    for (int oi = 0; oi < step.Ops.Count; oi++)
                    {
                        OperationType op = step.Ops[oi];
                        if (op != null && op.Id != Guid.Empty && !ids.Add(op.Id))
                        {
                            AddAuditFinding(findings, findingLimit, ref totalFindingCount, pi, proc, si, oi,
                                "error", "id.duplicate", "步骤或指令存在重复ID");
                        }
                        if (op == null || string.IsNullOrWhiteSpace(op.OperaType))
                        {
                            AddAuditFinding(findings, findingLimit, ref totalFindingCount, pi, proc, si, oi,
                                "error", op == null ? "operation.null" : "operation.missingType",
                                op == null ? "指令为空" : "指令类型为空");
                        }
                        else if (!knownOperationTypes.Contains(op.OperaType))
                        {
                            AddAuditFinding(findings, findingLimit, ref totalFindingCount, pi, proc, si, oi,
                                "error", "operation.unknownType", $"未知指令类型:{op.OperaType}");
                        }
                        else if (op.Disable)
                        {
                            AddAuditFinding(findings, findingLimit, ref totalFindingCount, pi, proc, si, oi,
                                "warning", "operation.disabled", "指令已禁用");
                        }
                    }
                }
            }
            return new JObject
            {
                ["procRange"] = new JObject { ["from"] = procOffset, ["toExclusive"] = procEnd },
                ["totalProcCount"] = procCount,
                ["indexRevision"] = indexRevision,
                ["findingCountInBatch"] = totalFindingCount,
                ["truncatedFindings"] = totalFindingCount > findings.Count,
                ["hasMoreProcs"] = procEnd < procCount,
                ["nextProcOffset"] = procEnd < procCount ? procEnd : (JToken)JValue.CreateNull(),
                ["findings"] = findings
            };
        }

        private static void AddAuditFinding(JArray findings, int limit, ref int total, int procIndex,
            Proc proc, int stepIndex, int opIndex, string severity, string code, string message)
        {
            total++;
            if (findings.Count >= limit) return;
            findings.Add(new JObject
            {
                ["severity"] = severity,
                ["code"] = code,
                ["message"] = message,
                ["procIndex"] = procIndex,
                ["procName"] = proc?.head?.Name ?? string.Empty,
                ["stepIndex"] = stepIndex,
                ["opIndex"] = opIndex
            });
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleDiagnoseIssue(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            string symptom = ReadOptionalString(request, "symptom") ?? string.Empty;
            int? stepIndex = ReadOptionalInt(request, "stepIndex");
            int? opIndex = ReadOptionalInt(request, "opIndex");
            int evidenceOffset = ReadOptionalInt(request, "evidenceOffset") ?? 0;
            int evidenceLimit = ReadOptionalInt(request, "evidenceLimit") ?? DefaultDiagnosticEvidencePageSize;
            if (evidenceOffset < 0 || evidenceLimit < 1 || evidenceLimit > MaxDiagnosticEvidencePageSize)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    $"evidenceOffset必须大于等于0，evidenceLimit必须在1..{MaxDiagnosticEvidencePageSize}范围内。");
            }
            JObject validation = HandleValidateProc(new JObject { ["procIndex"] = procIndex });
            EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
            var result = new JObject
            {
                ["symptom"] = symptom.Length <= 300 ? symptom : symptom.Substring(0, 300),
                ["procIndex"] = procIndex,
                ["runtime"] = BuildEngineSnapshot(snapshot, procIndex),
                ["structureValidation"] = new JObject
                {
                    ["isValid"] = validation["isValid"],
                    ["readinessStatus"] = validation["readinessStatus"],
                    ["runnable"] = validation["runnable"],
                    ["runBlockers"] = validation["runBlockers"],
                    ["errors"] = validation["errors"],
                    ["warnings"] = validation["warnings"]
                },
                ["runtimeEvidence"] = runtime.RuntimeBlackBoxRecorder?.BuildEvidencePage(
                    procIndex, evidenceOffset, evidenceLimit)
                    ?? RuntimeBlackBoxRecorder.BuildUnavailableEvidencePage(
                        procIndex, evidenceOffset, evidenceLimit)
            };
            int targetStep = stepIndex ?? snapshot?.StepIndex ?? -1;
            int targetOp = opIndex ?? snapshot?.OpIndex ?? -1;
            if (targetStep >= 0 && targetOp >= 0)
            {
                JObject context = HandleGetOperationContext(new JObject
                {
                    ["procIndex"] = procIndex,
                    ["stepIndex"] = targetStep,
                    ["opIndex"] = targetOp,
                    ["radius"] = 2
                });
                result["context"] = context;
            }
            return result;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private sealed class DiagnosticProcIndex
        {
            public string Signature { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public IReadOnlyList<DiagnosticFieldRecord> Fields { get; set; }
        }

        private sealed class DiagnosticFieldRecord
        {
            public int ProcIndex { get; set; }
            public string ProcName { get; set; }
            public int StepIndex { get; set; }
            public Guid StepId { get; set; }
            public string StepName { get; set; }
            public int OpIndex { get; set; }
            public Guid OpId { get; set; }
            public string OpName { get; set; }
            public string OperaType { get; set; }
            public string Field { get; set; }
            public string DisplayName { get; set; }
            public string ReferenceType { get; set; }
            public string Value { get; set; }
        }

    }
}
