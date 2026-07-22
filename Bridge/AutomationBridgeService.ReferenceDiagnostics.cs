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

    }
}
