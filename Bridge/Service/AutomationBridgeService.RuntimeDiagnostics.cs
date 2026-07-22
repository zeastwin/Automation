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
        private JObject HandleGetRuntimeSnapshot(JObject request)
        {
            EnsureRuntimeReady();
            int? procIndex = ReadOptionalInt(request, "procIndex");
            int offset = ReadOptionalInt(request, "offset") ?? 0;
            int limit = ReadOptionalInt(request, "limit") ?? DefaultSnapshotPageSize;
            if (offset < 0 || limit < 1 || limit > MaxSnapshotPageSize)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    $"offset 必须大于等于0，limit必须在1..{MaxSnapshotPageSize}范围内。");
            }
            if (procIndex.HasValue && (request["offset"] != null || request["limit"] != null))
            {
                return BridgeError(400, "INVALID_ARGUMENT", "指定procIndex时不接受offset或limit。");
            }
            JArray snapshots = new JArray();
            if (procIndex.HasValue)
            {
                GetProcByIndex(procIndex.Value);
                snapshots.Add(BuildEngineSnapshot(runtime.ProcessEngine?.GetSnapshot(procIndex.Value), procIndex.Value));
                offset = 0;
                limit = 1;
            }
            else
            {
                int endExclusive = (int)Math.Min(
                    runtime.Stores.Processes.Items.Count,
                    (long)offset + limit);
                for (int i = offset; i < endExclusive; i++)
                {
                    snapshots.Add(BuildEngineSnapshot(runtime.ProcessEngine?.GetSnapshot(i), i));
                }
            }

            PlatformEditorSelection selection = runtime.EditorUi?.GetSelection()
                ?? new PlatformEditorSelection
                {
                    ProcIndex = -1,
                    StepIndex = -1,
                    OperationIndex = -1
                };
            return new JObject
            {
                ["securityLocked"] = runtime.Safety.IsLocked,
                ["procConfigFaulted"] = runtime.Readiness.ProcConfigFaulted,
                ["procCount"] = runtime.Stores.Processes.Items.Count,
                ["offset"] = offset,
                ["limit"] = limit,
                ["returned"] = snapshots.Count,
                ["hasMore"] = !procIndex.HasValue && (long)offset + snapshots.Count < runtime.Stores.Processes.Items.Count,
                ["nextOffset"] = !procIndex.HasValue && (long)offset + snapshots.Count < runtime.Stores.Processes.Items.Count
                    ? (JToken)((long)offset + snapshots.Count)
                    : JValue.CreateNull(),
                ["selected"] = new JObject
                {
                    ["procIndex"] = selection.ProcIndex,
                    ["stepIndex"] = selection.StepIndex,
                    ["opIndex"] = selection.OperationIndex
                },
                ["snapshots"] = snapshots
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetInfoLogTail(JObject request)
        {
            int maxCount = ReadOptionalInt(request, "maxCount") ?? DefaultInfoLogCount;
            if (maxCount <= 0 || maxCount > MaxInfoLogCount)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    $"maxCount 必须在1..{MaxInfoLogCount}范围内。");
            }

            JArray items = new JArray();
            int omittedBySize = 0;
            foreach (PlatformInfoLogEntry item in runtime.EditorUi?.GetInfoLogTail(maxCount)
                ?? new List<PlatformInfoLogEntry>())
            {
                JObject candidate = new JObject
                {
                    ["time"] = item.TimeText,
                    ["message"] = item.Message,
                    ["level"] = item.Level
                };
                items.Add(candidate);
                if (Encoding.UTF8.GetByteCount(items.ToString(Formatting.None)) > MaxBatchReadUtf8Bytes)
                {
                    candidate.Remove();
                    omittedBySize++;
                }
            }

            return new JObject
            {
                ["maxCount"] = maxCount,
                ["returned"] = items.Count,
                ["omittedBySize"] = omittedBySize,
                ["resultUtf8ByteLimit"] = MaxBatchReadUtf8Bytes,
                ["items"] = items
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleDiagnoseProc(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int findingOffset = ReadOptionalInt(request, "findingOffset") ?? 0;
            int findingLimit = ReadOptionalInt(request, "findingLimit") ?? DefaultDiagnosticFindingPageSize;
            if (findingOffset < 0 || findingLimit < 1 || findingLimit > MaxDiagnosticFindingPageSize)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    $"findingOffset必须大于等于0，findingLimit必须在1..{MaxDiagnosticFindingPageSize}范围内。");
            }
            Proc proc = GetProcByIndex(procIndex);
            JArray findings = new JArray();

            AddFinding(findings, "info", "proc", $"流程：{proc.head?.Name ?? string.Empty}，步骤数 {proc.steps?.Count ?? 0}。");
            if (proc.head?.Disable == true)
            {
                AddFinding(findings, "warning", "proc.disabled", "流程已禁用，运行入口会跳过该流程。");
            }
            if (proc.steps == null || proc.steps.Count == 0)
            {
                AddFinding(findings, "error", "proc.empty", "流程没有步骤，无法执行有效动作。");
            }

            IEnumerable<OperationType> operationTypes = OperationDefinitionRegistry.CreateAll();
            HashSet<string> knownOperationTypes = new HashSet<string>(
                operationTypes
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.OperaType))
                    .Select(item => item.OperaType),
                StringComparer.Ordinal);

            int disabledStepCount = 0;
            int opCount = 0;
            int disabledOpCount = 0;
            if (proc.steps != null)
            {
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    if (step == null)
                    {
                        AddFinding(findings, "error", "step.null", $"步骤 {stepIndex} 为空。");
                        continue;
                    }
                    if (step.Disable)
                    {
                        disabledStepCount++;
                        AddFinding(findings, "warning", "step.disabled", $"步骤 {stepIndex} [{step.Name}] 已禁用。");
                    }
                    if (step.Ops == null || step.Ops.Count == 0)
                    {
                        AddFinding(findings, "warning", "step.empty", $"步骤 {stepIndex} [{step.Name}] 没有指令。");
                        continue;
                    }

                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        OperationType op = step.Ops[opIndex];
                        opCount++;
                        if (op == null)
                        {
                            AddFinding(findings, "error", "operation.null", $"步骤 {stepIndex} 指令 {opIndex} 为空。");
                            continue;
                        }
                        if (op.Disable)
                        {
                            disabledOpCount++;
                            AddFinding(findings, "warning", "operation.disabled", $"步骤 {stepIndex} 指令 {opIndex} [{op.Name}] 已禁用。");
                        }
                        if (string.IsNullOrWhiteSpace(op.OperaType) || !knownOperationTypes.Contains(op.OperaType))
                        {
                            AddFinding(findings, "error", "operation.unknownType", $"步骤 {stepIndex} 指令 {opIndex} 指令类型未知：{op.OperaType ?? string.Empty}。");
                        }
                    }
                }
            }

            foreach (string error in ProcessDefinitionService.ValidateProcGotoTargets(procIndex, proc))
            {
                AddFinding(findings, "error", "goto.invalid", error);
            }

            EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
            if (snapshot != null)
            {
                AddFinding(findings, snapshot.IsAlarm ? "error" : "info", "runtime.state",
                    $"运行状态 {snapshot.State}，位置 {snapshot.StepIndex}-{snapshot.OpIndex}。");
                if (snapshot.IsAlarm)
                {
                    AddFinding(findings, "error", "runtime.alarm", $"当前报警：{snapshot.AlarmMessage ?? string.Empty}");
                }
                if (snapshot.IsBreakpoint)
                {
                    AddFinding(findings, "warning", "runtime.breakpoint", "当前流程处于断点位置。");
                }
            }

            int totalFindingCount = findings.Count;
            JArray findingPage = new JArray(findings
                .Skip(findingOffset)
                .Take(findingLimit)
                .Select(item => item.DeepClone()));
            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procId"] = proc.head?.Id.ToString("D"),
                ["name"] = proc.head?.Name ?? string.Empty,
                ["summary"] = new JObject
                {
                    ["stepCount"] = proc.steps?.Count ?? 0,
                    ["disabledStepCount"] = disabledStepCount,
                    ["operationCount"] = opCount,
                    ["disabledOperationCount"] = disabledOpCount,
                    ["findingCount"] = totalFindingCount
                },
                ["runtime"] = BuildEngineSnapshot(snapshot, procIndex),
                ["findingOffset"] = findingOffset,
                ["findingLimit"] = findingLimit,
                ["returnedFindingCount"] = findingPage.Count,
                ["hasMoreFindings"] = (long)findingOffset + findingPage.Count < totalFindingCount,
                ["nextFindingOffset"] = (long)findingOffset + findingPage.Count < totalFindingCount
                    ? (JToken)((long)findingOffset + findingPage.Count)
                    : JValue.CreateNull(),
                ["findings"] = findingPage
            };
        }

        // 读取单条指令的完整详情：字段值、Schema、执行流向、跳转目标有效性。
        // 颗粒度介于 get_proc_detail 和 get_operation_schema 之间，适合聚焦分析某条指令。
        [System.Diagnostics.DebuggerNonUserCode]
        private IReadOnlyList<DiagnosticFieldRecord> GetDiagnosticFields(int procIndex, Proc proc)
        {
            string signature = BuildDiagnosticSignature(proc);
            if (diagnosticIndexes.TryGetValue(procIndex, out DiagnosticProcIndex cached)
                && string.Equals(cached.Signature, signature, StringComparison.Ordinal)
                && DateTime.UtcNow - cached.CreatedAtUtc < TimeSpan.FromSeconds(2))
            {
                return cached.Fields;
            }
            var fields = new List<DiagnosticFieldRecord>();
            if (proc?.steps != null)
            {
                for (int si = 0; si < proc.steps.Count; si++)
                {
                    Step step = proc.steps[si];
                    if (step?.Ops == null) continue;
                    for (int oi = 0; oi < step.Ops.Count; oi++)
                    {
                        OperationType op = step.Ops[oi];
                        if (op == null) continue;
                        AddDiagnosticFields(
                            fields,
                            procIndex,
                            proc.head?.Name ?? string.Empty,
                            si,
                            step.Id,
                            step.Name ?? string.Empty,
                            oi,
                            op,
                            op,
                            string.Empty,
                            0,
                            new List<object>());
                    }
                }
            }
            diagnosticIndexes[procIndex] = new DiagnosticProcIndex
            {
                Signature = signature,
                CreatedAtUtc = DateTime.UtcNow,
                Fields = fields
            };
            return fields;
        }

        // 引用字段可能位于参数列表或内嵌参数组中，必须递归索引，不能只看指令顶层属性。
        private static void AddDiagnosticFields(
            ICollection<DiagnosticFieldRecord> fields,
            int procIndex,
            string procName,
            int stepIndex,
            Guid stepId,
            string stepName,
            int opIndex,
            OperationType operation,
            object value,
            string path,
            int depth,
            IList<object> visited)
        {
            if (value == null || depth > 5 || visited.Any(item => ReferenceEquals(item, value)))
            {
                return;
            }
            visited.Add(value);
            foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(value).Cast<PropertyDescriptor>())
            {
                if (descriptor == null || !descriptor.IsBrowsable)
                {
                    continue;
                }
                object fieldValue;
                try
                {
                    fieldValue = descriptor.GetValue(value);
                }
                catch
                {
                    continue;
                }
                string fieldPath = string.IsNullOrEmpty(path) ? descriptor.Name : $"{path}.{descriptor.Name}";
                fields.Add(new DiagnosticFieldRecord
                {
                    ProcIndex = procIndex,
                    ProcName = procName,
                    StepIndex = stepIndex,
                    StepId = stepId,
                    StepName = stepName,
                    OpIndex = opIndex,
                    OpId = operation.Id,
                    OpName = operation.Name ?? string.Empty,
                    OperaType = operation.OperaType ?? string.Empty,
                    Field = fieldPath,
                    DisplayName = descriptor.DisplayName,
                    ReferenceType = IsVariableIndexDescriptor(descriptor)
                        ? "value.index"
                        : GetReferenceType(descriptor.Converter?.GetType().Name),
                    Value = ConvertFieldValueToText(fieldValue) ?? string.Empty
                });

                if (depth >= 5 || fieldValue == null || fieldValue is string)
                {
                    continue;
                }
                if (fieldValue is IEnumerable items)
                {
                    int itemIndex = 0;
                    foreach (object item in items)
                    {
                        if (item != null && !IsSimpleDiagnosticValue(item.GetType()))
                        {
                            AddDiagnosticFields(fields, procIndex, procName, stepIndex, stepId, stepName,
                                opIndex, operation, item, $"{fieldPath}[{itemIndex}]", depth + 1, visited);
                        }
                        itemIndex++;
                    }
                    continue;
                }
                Type fieldType = fieldValue.GetType();
                if (!IsSimpleDiagnosticValue(fieldType)
                    && fieldType.Assembly == typeof(OperationType).Assembly)
                {
                    AddDiagnosticFields(fields, procIndex, procName, stepIndex, stepId, stepName,
                        opIndex, operation, fieldValue, fieldPath, depth + 1, visited);
                }
            }
        }

        private static bool IsSimpleDiagnosticValue(Type type)
        {
            Type actualType = Nullable.GetUnderlyingType(type) ?? type;
            return actualType.IsPrimitive || actualType.IsEnum || actualType == typeof(string)
                || actualType == typeof(decimal) || actualType == typeof(Guid)
                || actualType == typeof(DateTime) || actualType == typeof(TimeSpan);
        }

        private static string BuildDiagnosticSignature(Proc proc)
        {
            var builder = new StringBuilder();
            builder.Append(proc?.head?.Id.ToString("N")).Append('|').Append(proc?.steps?.Count ?? 0);
            if (proc?.steps != null)
            {
                foreach (Step step in proc.steps)
                {
                    builder.Append('|').Append(step?.Id.ToString("N")).Append(':').Append(step?.Ops?.Count ?? 0);
                    if (step?.Ops != null)
                    {
                        foreach (OperationType op in step.Ops)
                        {
                            builder.Append(',').Append(op?.Id.ToString("N"));
                        }
                    }
                }
            }
            return builder.ToString();
        }

        private string GetDiagnosticIndexRevision()
        {
            var builder = new StringBuilder();
            IList<Proc> procs = runtime.Stores.Processes?.Items;
            if (procs != null)
            {
                for (int i = 0; i < procs.Count; i++)
                {
                    builder.Append(i).Append('=').Append(BuildDiagnosticSignature(procs[i])).Append(';');
                }
            }
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 16).ToLowerInvariant();
            }
        }

    }
}
