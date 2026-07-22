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
        private JObject HandleListProcs(JObject request)
        {
            bool includeStepSummary = ReadOptionalBoolean(request, "includeStepSummary") ?? false;
            EnsureRuntimeReady();
            string keyword = ReadOptionalString(request, "keyword")?.Trim();
            int offset = ReadOptionalInt(request, "offset") ?? 0;
            int limit = ReadOptionalInt(request, "limit") ?? 50;
            if (offset < 0 || limit < 1 || limit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "offset 必须大于等于0，limit 必须在1..100之间。");
            }

            List<int> candidates = Enumerable.Range(0, runtime.Stores.Processes.Items.Count)
                .Where(i => string.IsNullOrEmpty(keyword)
                    || (runtime.Stores.Processes.Items[i]?.head?.Name?.IndexOf(keyword,
                        StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                    || i.ToString(CultureInfo.InvariantCulture).Contains(keyword))
                .ToList();

            var array = new JArray();
            foreach (int i in candidates.Skip(offset).Take(limit))
            {
                Proc proc = runtime.Stores.Processes.Items[i];
                EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(i);
                JObject item = new JObject
                {
                    ["procIndex"] = i,
                    ["procId"] = proc?.head?.Id.ToString("D"),
                    ["name"] = proc?.head?.Name ?? string.Empty,
                    ["autoStart"] = proc?.head?.AutoStart ?? false,
                    ["disable"] = proc?.head?.Disable ?? false,
                    ["state"] = snapshot?.State.ToString() ?? ProcRunState.Stopped.ToString(),
                    ["stepCount"] = proc?.steps?.Count ?? 0
                };

                if (includeStepSummary)
                {
                    JArray steps = new JArray();
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
                    item["steps"] = steps;
                }

                array.Add(item);
            }

            return new JObject
            {
                ["total"] = candidates.Count,
                ["offset"] = offset,
                ["limit"] = limit,
                ["hasMore"] = offset + array.Count < candidates.Count,
                ["items"] = array
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetProcOverview(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            if (!TryGetProcByIndexForRead(procIndex, out Proc proc, out JObject error))
            {
                return error;
            }
            int operationCount = CountOperations(proc);
            if (operationCount > MaxOverviewOperationCount)
            {
                return BridgeError(413, "PROC_OVERVIEW_TOO_LARGE",
                    $"流程包含{operationCount}条指令，超过摘要上限{MaxOverviewOperationCount}；请使用search_proc_catalog、trace_resource、search_operation_fields或get_operation_context局部读取。");
            }
            return BuildProcOverview(procIndex, proc);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetProcDetail(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            if (!TryGetProcByIndexForRead(procIndex, out Proc proc, out JObject error))
            {
                return error;
            }
            int operationCount = CountOperations(proc);
            if (operationCount > MaxDetailOperationCount)
            {
                return BuildProcDetailOmitted(procIndex, proc, operationCount);
            }
            JObject detail = BuildProcDetail(procIndex, proc);
            detail["detailOperationLimit"] = MaxDetailOperationCount;
            detail["detailUtf8ByteLimit"] = MaxProcDetailUtf8Bytes;
            detail["detailUtf8Bytes"] = 0;
            int detailBytes = Encoding.UTF8.GetByteCount(detail.ToString(Formatting.None));
            detail["detailUtf8Bytes"] = detailBytes;
            detailBytes = Encoding.UTF8.GetByteCount(detail.ToString(Formatting.None));
            if (detailBytes > MaxProcDetailUtf8Bytes)
            {
                return BuildProcDetailOmitted(procIndex, proc, operationCount, detailBytes);
            }
            detail["detailUtf8Bytes"] = detailBytes;
            return detail;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetFlowGraph(JObject request)
        {
            string scope = ReadRequiredString(request, "scope").Trim();
            ProcessFlowGraphSnapshot graph;
            if (string.Equals(scope, "project", StringComparison.OrdinalIgnoreCase))
            {
                graph = ProcessFlowGraphService.BuildProject(
                    runtime.Stores.Processes.Items,
                    index => runtime.ProcessEngine?.GetSnapshot(index),
                    runtime.CreateProcessValidationContext(),
                    runtime.Stores.Values);
            }
            else if (string.Equals(scope, "process", StringComparison.OrdinalIgnoreCase))
            {
                int procIndex = ReadRequiredInt(request, "procIndex");
                if (!TryGetProcByIndexForRead(procIndex, out _, out JObject error))
                {
                    return error;
                }
                graph = ProcessFlowGraphService.BuildProcess(
                    runtime.Stores.Processes.Items,
                    procIndex,
                    index => runtime.ProcessEngine?.GetSnapshot(index));
            }
            else
            {
                return BridgeError(400, "INVALID_ARGUMENT", "scope 只支持 project 或 process。");
            }

            JObject payload = ProcessFlowGraphService.ToJObject(graph);
            payload["graphAvailable"] = true;
            payload["graphUtf8ByteLimit"] = MaxProcDetailUtf8Bytes;
            payload["graphUtf8Bytes"] = 0;
            int graphBytes = 0;
            for (int measurement = 0; measurement < 3; measurement++)
            {
                graphBytes = Encoding.UTF8.GetByteCount(payload.ToString(Formatting.None));
                payload["graphUtf8Bytes"] = graphBytes;
            }
            if (graphBytes <= MaxProcDetailUtf8Bytes)
            {
                return payload;
            }

            return new JObject
            {
                ["graphAvailable"] = false,
                ["scope"] = graph.Scope,
                ["procIndex"] = graph.ProcIndex,
                ["procId"] = graph.ProcId,
                ["name"] = graph.Name,
                ["nodeCount"] = graph.Nodes.Count,
                ["edgeCount"] = graph.Edges.Count,
                ["diagnosticCount"] = graph.Diagnostics.Count,
                ["graphUtf8Bytes"] = graphBytes,
                ["graphUtf8ByteLimit"] = MaxProcDetailUtf8Bytes,
                ["groups"] = payload["groups"]?.DeepClone() ?? new JArray(),
                ["reason"] = $"流程图序列化后为{graphBytes}字节，超过当前模型上下文单对象边界{MaxProcDetailUtf8Bytes}字节。",
                ["nextReadOptions"] = new JArray(
                    "调用 get_proc_overview 获取轻量流程目录",
                    "按明确步骤调用 get_step_detail",
                    "按明确 opId 调用 get_op_details")
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListOperationTypes()
        {
            JArray items = new JArray();
            foreach (OperationType template in OperationDefinitionRegistry.CreateAll())
            {
                if (template == null)
                {
                    continue;
                }

                items.Add(new JObject
                {
                    ["operaType"] = template.OperaType ?? string.Empty,
                    ["name"] = template.Name ?? string.Empty
                });
            }

            return new JObject
            {
                ["items"] = items
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetOperationSchema(JObject request)
        {
            EnsureRuntimeReady();

            int? procIndex = ReadOptionalInt(request, "procIndex");
            string stepIdText = ReadOptionalString(request, "stepId");
            string opIdText = ReadOptionalString(request, "opId");
            string operaType = ReadOptionalString(request, "operaType");

            OperationType operation;
            JObject source;

            if (procIndex.HasValue || !string.IsNullOrWhiteSpace(stepIdText) || !string.IsNullOrWhiteSpace(opIdText))
            {
                if (!procIndex.HasValue)
                {
                    return BridgeError(400, "INVALID_ARGUMENT", "读取现有指令实例时必须提供 procIndex。");
                }
                Guid stepId = ParseGuid(stepIdText, "stepId");
                Guid opId = ParseGuid(opIdText, "opId");
                Proc proc = GetProcByIndex(procIndex.Value);
                Step step = FindStepById(proc, stepId);
                operation = FindOperationById(step, opId);
                source = new JObject
                {
                    ["mode"] = "instance",
                    ["procIndex"] = procIndex.Value,
                    ["stepId"] = stepId.ToString("D"),
                    ["opId"] = opId.ToString("D"),
                    ["operaType"] = operation.OperaType ?? string.Empty
                };
            }
            else
            {
                if (string.IsNullOrWhiteSpace(operaType))
                {
                    return BridgeError(400, "INVALID_ARGUMENT", "读取指令类型 Schema 时必须提供 operaType。");
                }

                operation = CreateOperationTemplate(operaType);
                source = new JObject
                {
                    ["mode"] = "template",
                    ["operaType"] = operation.OperaType ?? string.Empty
                };
            }

            return new JObject
            {
                ["source"] = source,
                ["schema"] = BuildOperationSchema(operation)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetOperationGuide(JObject request)
        {
            EnsureRuntimeReady();
            string operaType = ReadRequiredString(request, "operaType");
            OperationType operation = CreateOperationTemplate(operaType);
            JObject behavior = OperationBehaviorCatalog.BuildContract(operation);
            return new JObject
            {
                ["representation"] = "native",
                ["schemaTool"] = "get_native_operation_schemas",
                ["operaType"] = operation.OperaType ?? string.Empty,
                ["guide"] = behavior
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetReferenceCatalog(JObject request)
        {
            EnsureRuntimeReady();
            int? procIndex = ReadOptionalInt(request, "procIndex");
            CommReferenceCatalog commNames = GetCommNames();

            JArray stations = new JArray();
            if (runtime.Stores.Stations?.Items != null)
            {
                foreach (DataStation station in runtime.Stores.Stations.Items)
                {
                    if (station == null || string.IsNullOrWhiteSpace(station.Name))
                    {
                        continue;
                    }

                    List<string> axes = station.dataAxis?.axisConfigs == null
                        ? new List<string>()
                        : station.dataAxis.axisConfigs
                            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.AxisName) && item.AxisName != "-1")
                            .Select(item => item.AxisName)
                            .Distinct(StringComparer.Ordinal)
                            .ToList();

                    List<string> positions = station.ListDataPos == null
                        ? new List<string>()
                        : station.ListDataPos
                            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                            .Select(item => item.Name)
                            .Distinct(StringComparer.Ordinal)
                            .ToList();

                    stations.Add(new JObject
                    {
                        ["name"] = station.Name,
                        ["axes"] = new JArray(axes),
                        ["positions"] = new JArray(positions)
                    });
                }
            }

            return new JObject
            {
                ["procIndex"] = procIndex.HasValue ? (JToken)procIndex.Value : JValue.CreateNull(),
                ["values"] = new JArray(runtime.Stores.Values?.GetValueNames() ?? new List<string>()),
                ["dataStructs"] = new JArray(runtime.Stores.DataStructures?.GetStructNames() ?? new List<string>()),
                ["alarmInfoIds"] = new JArray((runtime.Stores.Alarms?.GetValidIndices() ?? new List<int>()).Select(item => (JToken)item)),
                ["io"] = new JObject
                {
                    ["all"] = new JArray(runtime.Stores.IoConfiguration?.AllNames ?? new List<string>()),
                    ["inputs"] = new JArray(runtime.Stores.IoConfiguration?.InputNames ?? new List<string>()),
                    ["outputs"] = new JArray(runtime.Stores.IoConfiguration?.OutputNames ?? new List<string>())
                },
                ["communication"] = new JObject
                {
                    ["all"] = new JArray(commNames.All),
                    ["tcp"] = new JArray(commNames.Tcp),
                    ["serial"] = new JArray(commNames.Serial)
                },
                ["plcDevices"] = new JArray((runtime.Stores.Plc?.GetSnapshot().Devices ?? new List<PlcDeviceConfig>())
                    .Where(device => device != null && !string.IsNullOrWhiteSpace(device.Name))
                    .Select(device => device.Name)),
                ["stations"] = stations,
                ["gotoTargets"] = BuildGotoTargets(procIndex)
            };
        }

    }
}
