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

        private JObject HandleTraceResource(JObject request)
        {
            EnsureRuntimeReady();
            string name = ReadRequiredString(request, "name").Trim();
            string resourceKind = (ReadOptionalString(request, "resourceKind") ?? "auto").Trim();
            var resolvedTypes = new List<string>();
            if (!string.Equals(resourceKind, "auto", StringComparison.OrdinalIgnoreCase))
            {
                var kindMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["variable"] = "value", ["value"] = "value", ["io"] = "io",
                    ["communication"] = "comm", ["comm"] = "comm",
                    ["tcp"] = "comm.tcp", ["serial"] = "comm.serial",
                    ["station"] = "station", ["plc"] = "plc.device",
                    ["dataStruct"] = "dataStruct", ["alarm"] = "alarm.infoId"
                };
                if (!kindMap.TryGetValue(resourceKind, out string mapped))
                {
                    return BridgeError(400, "INVALID_ARGUMENT",
                        "resourceKind 可选:auto/variable/io/communication/tcp/serial/station/plc/dataStruct/alarm。");
                }
                resolvedTypes.Add(mapped);
            }
            else
            {
                if ((runtime.Stores.Values?.GetValueNames() ?? new List<string>()).Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("value");
                if ((runtime.Stores.IoConfiguration?.AllNames ?? new List<string>()).Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("io");
                CommReferenceCatalog communications = GetCommNames();
                if (communications.Tcp.Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("comm.tcp");
                if (communications.Serial.Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("comm.serial");
                if ((runtime.Stores.Stations?.Items ?? new List<DataStation>()).Any(item => item?.Name == name))
                    resolvedTypes.Add("station");
                if ((runtime.Stores.Plc?.GetSnapshot().Devices ?? new List<PlcDeviceConfig>()).Any(item => item?.Name == name))
                    resolvedTypes.Add("plc.device");
                if ((runtime.Stores.DataStructures?.GetStructNames() ?? new List<string>()).Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("dataStruct");
                if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int alarmIndex)
                    && runtime.Stores.Alarms?.GetValidIndices().Contains(alarmIndex) == true)
                    resolvedTypes.Add("alarm.infoId");
            }
            if (resolvedTypes.Count == 0)
            {
                return BridgeError(404, "RESOURCE_NOT_FOUND",
                    $"未在变量、IO、TCP/串口通讯、工站、PLC、数据结构或报警配置中找到资源:{name}");
            }
            var delegated = new JObject
            {
                ["referenceType"] = string.Join("|", resolvedTypes),
                ["value"] = name,
                ["procOffset"] = ReadOptionalInt(request, "procOffset") ?? 0,
                ["procLimit"] = ReadOptionalInt(request, "procLimit") ?? 20,
                ["resultLimit"] = ReadOptionalInt(request, "resultLimit") ?? 50
            };
            JObject result = HandleFindReferences(delegated);
            result["resource"] = new JObject
            {
                ["name"] = name,
                ["requestedKind"] = resourceKind,
                ["resolvedReferenceTypes"] = new JArray(resolvedTypes),
                ["ambiguous"] = resolvedTypes.Count > 1,
                ["variable"] = result["variable"]?.DeepClone()
            };
            return result;
        }

        private JObject HandleSearchOperationFields(JObject request)
        {
            EnsureRuntimeReady();
            string query = ReadRequiredString(request, "query");
            string matchMode = (ReadOptionalString(request, "matchMode") ?? "contains").Trim();
            string fieldName = ReadOptionalString(request, "fieldName")?.Trim();
            string operaType = ReadOptionalString(request, "operaType")?.Trim();
            int procOffset = ReadOptionalInt(request, "procOffset") ?? 0;
            int procLimit = ReadOptionalInt(request, "procLimit") ?? 20;
            int resultLimit = ReadOptionalInt(request, "resultLimit") ?? 50;
            if (query.Length > 200 || (matchMode != "exact" && matchMode != "contains")
                || procOffset < 0 || procLimit < 1 || procLimit > 50 || resultLimit < 1 || resultLimit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    "query最长200字符；matchMode为exact/contains；procLimit为1..50；resultLimit为1..100。");
            }
            int procCount = runtime.Stores.Processes.Items.Count;
            int procEnd = Math.Min(procCount, procOffset + procLimit);
            string indexRevision = GetDiagnosticIndexRevision();
            int total = 0;
            var matches = new JArray();
            for (int pi = procOffset; pi < procEnd; pi++)
            {
                Proc proc = runtime.Stores.Processes.Items[pi];
                foreach (DiagnosticFieldRecord field in GetDiagnosticFields(pi, proc))
                {
                    if ((!string.IsNullOrEmpty(operaType) && !string.Equals(field.OperaType, operaType, StringComparison.Ordinal))
                        || (!string.IsNullOrEmpty(fieldName) && !string.Equals(field.Field, fieldName, StringComparison.Ordinal))) continue;
                    bool matched = matchMode == "exact"
                        ? string.Equals(field.Value, query, StringComparison.Ordinal)
                        : field.Value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matched) continue;
                    total++;
                    if (matches.Count < resultLimit) matches.Add(BuildDiagnosticMatch(field, true));
                }
            }
            return new JObject
            {
                ["criteria"] = new JObject { ["query"] = query, ["matchMode"] = matchMode,
                    ["fieldName"] = fieldName, ["operaType"] = operaType },
                ["procRange"] = new JObject { ["from"] = procOffset, ["toExclusive"] = procEnd },
                ["indexRevision"] = indexRevision,
                ["matchCountInBatch"] = total, ["truncatedMatches"] = total > matches.Count,
                ["hasMoreProcs"] = procEnd < procCount,
                ["nextProcOffset"] = procEnd < procCount ? procEnd : (JToken)JValue.CreateNull(),
                ["matches"] = matches
            };
        }

        private JObject HandleFindReferences(JObject request)
        {
            EnsureRuntimeReady();
            string referenceType = ReadRequiredString(request, "referenceType").Trim();
            HashSet<string> referenceTypes = new HashSet<string>(
                referenceType.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim()), StringComparer.OrdinalIgnoreCase);
            string value = ReadRequiredString(request, "value").Trim();
            DicValue tracedVariable = null;
            if (referenceTypes.Contains("value"))
            {
                runtime.Stores.Values?.TryGetValueByName(value, out tracedVariable);
            }
            string fieldName = ReadOptionalString(request, "fieldName")?.Trim();
            int procOffset = ReadOptionalInt(request, "procOffset") ?? 0;
            int procLimit = ReadOptionalInt(request, "procLimit") ?? 20;
            int resultLimit = ReadOptionalInt(request, "resultLimit") ?? 50;
            if (procOffset < 0 || procLimit < 1 || procLimit > 50 || resultLimit < 1 || resultLimit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    "procOffset 必须大于等于0，procLimit 必须在1..50，resultLimit 必须在1..100。");
            }

            int procCount = runtime.Stores.Processes.Items.Count;
            int procEnd = Math.Min(procCount, procOffset + procLimit);
            string indexRevision = GetDiagnosticIndexRevision();
            int totalMatchesInBatch = 0;
            var matches = new JArray();
            for (int pi = procOffset; pi < procEnd; pi++)
            {
                Proc proc = runtime.Stores.Processes.Items[pi];
                foreach (DiagnosticFieldRecord field in GetDiagnosticFields(pi, proc))
                {
                    if (!string.IsNullOrEmpty(fieldName) && !string.Equals(field.Field, fieldName, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    bool nameReference = referenceTypes.Any(expected =>
                        IsMatchingReferenceType(expected, field.ReferenceType))
                        && string.Equals(field.Value.Trim(), value, StringComparison.Ordinal);
                    bool indexReference = tracedVariable != null
                        && string.Equals(field.ReferenceType, "value.index", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(field.Value.Trim(), tracedVariable.Index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                    if (!nameReference && !indexReference) continue;
                    totalMatchesInBatch++;
                    if (matches.Count < resultLimit)
                    {
                        JObject match = BuildDiagnosticMatch(field, false);
                        if (tracedVariable != null)
                        {
                            Guid procId = proc?.head?.Id ?? Guid.Empty;
                            match["referenceKind"] = indexReference ? "index" : "name";
                            match["variableId"] = tracedVariable.Id.ToString("D");
                            match["variableName"] = tracedVariable.Name;
                            match["variableIndex"] = tracedVariable.Index;
                            match["scope"] = tracedVariable.Scope;
                            match["ownerProcId"] = tracedVariable.OwnerProcId?.ToString("D");
                            match["ownerProcName"] = ResolveProcessName(tracedVariable.OwnerProcId);
                            match["accessStatus"] = ValueConfigStore.CanProcessAccess(tracedVariable, procId)
                                ? "accessible"
                                : "inaccessible_other_process_private";
                        }
                        matches.Add(match);
                    }
                }
            }
            return new JObject
            {
                ["criteria"] = new JObject
                {
                    ["referenceType"] = referenceType,
                    ["value"] = value,
                    ["fieldName"] = fieldName
                },
                ["procRange"] = new JObject { ["from"] = procOffset, ["toExclusive"] = procEnd },
                ["totalProcCount"] = procCount,
                ["indexRevision"] = indexRevision,
                ["matchCountInBatch"] = totalMatchesInBatch,
                ["truncatedMatches"] = totalMatchesInBatch > matches.Count,
                ["hasMoreProcs"] = procEnd < procCount,
                ["nextProcOffset"] = procEnd < procCount ? procEnd : (JToken)JValue.CreateNull(),
                ["matches"] = matches,
                ["variable"] = tracedVariable == null ? null : new JObject
                {
                    ["variableId"] = tracedVariable.Id,
                    ["name"] = tracedVariable.Name,
                    ["index"] = tracedVariable.Index,
                    ["scope"] = tracedVariable.Scope,
                    ["ownerProcId"] = tracedVariable.OwnerProcId?.ToString("D"),
                    ["ownerProcName"] = ResolveProcessName(tracedVariable.OwnerProcId)
                }
            };
        }

        private JObject HandleGetOperationReferences(JObject request)
        {
            int targetProcIndex = ReadRequiredInt(request, "procIndex");
            Guid targetOpId = ParseGuid(ReadRequiredString(request, "opId"), "opId");
            int procOffset = ReadOptionalInt(request, "procOffset") ?? 0;
            int procLimit = ReadOptionalInt(request, "procLimit") ?? 20;
            int resultLimit = ReadOptionalInt(request, "resultLimit") ?? 50;
            if (procOffset < 0 || procLimit < 1 || procLimit > 50 || resultLimit < 1 || resultLimit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    "procOffset 必须大于等于0，procLimit 必须在1..50，resultLimit 必须在1..100。");
            }
            if (!TryGetProcByIndexForRead(targetProcIndex, out Proc targetProc, out JObject error))
            {
                return error;
            }

            int targetStepIndex = -1;
            int targetOpIndex = -1;
            Step targetStep = null;
            OperationType targetOperation = null;
            if (targetProc.steps != null)
            {
                for (int stepIndex = 0; stepIndex < targetProc.steps.Count && targetOperation == null; stepIndex++)
                {
                    Step step = targetProc.steps[stepIndex];
                    if (step?.Ops == null) continue;
                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        if (step.Ops[opIndex]?.Id != targetOpId) continue;
                        targetStepIndex = stepIndex;
                        targetOpIndex = opIndex;
                        targetStep = step;
                        targetOperation = step.Ops[opIndex];
                        break;
                    }
                }
            }
            if (targetOperation == null)
            {
                return BridgeError(404, "OP_NOT_FOUND", $"流程 {targetProcIndex} 中未找到指令：{targetOpId:D}");
            }

            var outgoing = new JArray();
            foreach (DiagnosticFieldRecord field in GetDiagnosticFields(targetProcIndex, targetProc)
                .Where(item => item.OpId == targetOpId
                    && string.Equals(item.ReferenceType, "proc.goto", StringComparison.OrdinalIgnoreCase)))
            {
                outgoing.Add(BuildGotoReference(field));
            }

            int procCount = runtime.Stores.Processes.Items.Count;
            int procEnd = Math.Min(procCount, procOffset + procLimit);
            int incomingCount = 0;
            var incoming = new JArray();
            for (int procIndex = procOffset; procIndex < procEnd; procIndex++)
            {
                Proc proc = runtime.Stores.Processes.Items[procIndex];
                foreach (DiagnosticFieldRecord field in GetDiagnosticFields(procIndex, proc))
                {
                    if (!string.Equals(field.ReferenceType, "proc.goto", StringComparison.OrdinalIgnoreCase)
                        || !ProcessDefinitionService.TryParseGotoKey(
                            field.Value, out int gotoProc, out int gotoStep, out int gotoOp)
                        || gotoProc != targetProcIndex || gotoStep != targetStepIndex || gotoOp != targetOpIndex)
                    {
                        continue;
                    }
                    incomingCount++;
                    if (incoming.Count < resultLimit)
                    {
                        JObject match = BuildDiagnosticMatch(field, false);
                        match["referenceKind"] = "explicitGoto";
                        match["isRemote"] = field.ProcIndex != targetProcIndex
                            || field.StepIndex != targetStepIndex
                            || Math.Abs(field.OpIndex - targetOpIndex) > 10;
                        incoming.Add(match);
                    }
                }
            }

            return new JObject
            {
                ["target"] = new JObject
                {
                    ["procIndex"] = targetProcIndex,
                    ["procId"] = targetProc.head?.Id.ToString("D"),
                    ["procName"] = targetProc.head?.Name ?? string.Empty,
                    ["stepIndex"] = targetStepIndex,
                    ["stepId"] = targetStep?.Id.ToString("D"),
                    ["stepName"] = targetStep?.Name ?? string.Empty,
                    ["opIndex"] = targetOpIndex,
                    ["opId"] = targetOpId.ToString("D"),
                    ["opName"] = targetOperation.Name ?? string.Empty,
                    ["operaType"] = targetOperation.OperaType ?? string.Empty
                },
                ["outgoingGotoTargets"] = outgoing,
                ["incomingGotoCountInBatch"] = incomingCount,
                ["truncatedIncoming"] = incomingCount > incoming.Count,
                ["incomingGotoReferences"] = incoming,
                ["procRange"] = new JObject { ["from"] = procOffset, ["toExclusive"] = procEnd },
                ["indexRevision"] = GetDiagnosticIndexRevision(),
                ["hasMoreProcs"] = procEnd < procCount,
                ["nextProcOffset"] = procEnd < procCount ? procEnd : (JToken)JValue.CreateNull()
            };
        }

        private JObject HandleGetProcReferences(JObject request)
        {
            int targetProcIndex = ReadRequiredInt(request, "procIndex");
            int procOffset = ReadOptionalInt(request, "procOffset") ?? 0;
            int procLimit = ReadOptionalInt(request, "procLimit") ?? 20;
            int resultLimit = ReadOptionalInt(request, "resultLimit") ?? 50;
            if (procOffset < 0 || procLimit < 1 || procLimit > 50 || resultLimit < 1 || resultLimit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    "procOffset 必须大于等于0，procLimit 必须在1..50，resultLimit 必须在1..100。");
            }
            if (!TryGetProcByIndexForRead(targetProcIndex, out Proc targetProc, out JObject error))
            {
                return error;
            }

            int procCount = runtime.Stores.Processes.Items.Count;
            int procEnd = Math.Min(procCount, procOffset + procLimit);
            int matchCount = 0;
            var matches = new JArray();
            for (int procIndex = procOffset; procIndex < procEnd; procIndex++)
            {
                Proc proc = runtime.Stores.Processes.Items[procIndex];
                foreach (DiagnosticFieldRecord field in GetDiagnosticFields(procIndex, proc))
                {
                    string referenceKind = null;
                    if (string.Equals(field.ReferenceType, "proc", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(field.Value.Trim(), targetProc.head?.Name ?? string.Empty, StringComparison.Ordinal))
                    {
                        referenceKind = "processControl";
                    }
                    else if (string.Equals(field.ReferenceType, "proc.goto", StringComparison.OrdinalIgnoreCase)
                        && ProcessDefinitionService.TryParseGotoKey(
                            field.Value, out int gotoProc, out _, out _)
                        && gotoProc == targetProcIndex)
                    {
                        referenceKind = "gotoIntoProcess";
                    }
                    if (referenceKind == null) continue;
                    matchCount++;
                    if (matches.Count < resultLimit)
                    {
                        JObject match = BuildDiagnosticMatch(field, false);
                        match["referenceKind"] = referenceKind;
                        matches.Add(match);
                    }
                }
            }

            return new JObject
            {
                ["target"] = new JObject
                {
                    ["procIndex"] = targetProcIndex,
                    ["procId"] = targetProc.head?.Id.ToString("D"),
                    ["procName"] = targetProc.head?.Name ?? string.Empty
                },
                ["referenceCountInBatch"] = matchCount,
                ["truncatedReferences"] = matchCount > matches.Count,
                ["references"] = matches,
                ["procRange"] = new JObject { ["from"] = procOffset, ["toExclusive"] = procEnd },
                ["indexRevision"] = GetDiagnosticIndexRevision(),
                ["hasMoreProcs"] = procEnd < procCount,
                ["nextProcOffset"] = procEnd < procCount ? procEnd : (JToken)JValue.CreateNull()
            };
        }

        private static JObject BuildGotoReference(DiagnosticFieldRecord field)
        {
            bool parsed = ProcessDefinitionService.TryParseGotoKey(
                field.Value, out int procIndex, out int stepIndex, out int opIndex);
            return new JObject
            {
                ["field"] = field.Field,
                ["displayName"] = field.DisplayName,
                ["rawValue"] = field.Value,
                ["parsed"] = parsed,
                ["procIndex"] = parsed ? procIndex : (JToken)JValue.CreateNull(),
                ["stepIndex"] = parsed ? stepIndex : (JToken)JValue.CreateNull(),
                ["opIndex"] = parsed ? opIndex : (JToken)JValue.CreateNull()
            };
        }

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

