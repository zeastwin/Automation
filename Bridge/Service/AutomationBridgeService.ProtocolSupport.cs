using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。
// 线程边界：基础类型、字段和体积错误必须在这里或 MCP 工作线程发现，进入 UI 线程后只访问平台状态。

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
        private JObject BuildEngineSnapshot(EngineSnapshot snapshot, int procIndex)
        {
            if (snapshot == null)
            {
                return new JObject
                {
                    ["procIndex"] = procIndex,
                    ["runId"] = string.Empty,
                    ["state"] = ProcRunState.Ready.ToString(),
                    ["stepIndex"] = -1,
                    ["opIndex"] = -1,
                    ["isBreakpoint"] = false,
                    ["isAlarm"] = false,
                    ["alarmMessage"] = string.Empty,
                    ["terminationReason"] = ProcTerminationReason.None.ToString(),
                    ["cycleTimeSamples"] = new JArray(),
                    ["performance"] = JValue.CreateNull(),
                    ["updateTime"] = JValue.CreateNull()
                };
            }

            return new JObject
            {
                ["procIndex"] = snapshot.ProcIndex,
                ["procId"] = snapshot.ProcId.ToString("D"),
                ["procName"] = snapshot.ProcName ?? string.Empty,
                ["runId"] = snapshot.RunId == Guid.Empty ? string.Empty : snapshot.RunId.ToString("D"),
                ["state"] = snapshot.State.ToString(),
                ["stepIndex"] = snapshot.StepIndex,
                ["opIndex"] = snapshot.OpIndex,
                ["isBreakpoint"] = snapshot.IsBreakpoint,
                ["isAlarm"] = snapshot.IsAlarm,
                ["alarmMessage"] = snapshot.AlarmMessage ?? string.Empty,
                ["terminationReason"] = snapshot.TerminationReason.ToString(),
                ["updateTime"] = snapshot.UpdateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                ["updateTicks"] = snapshot.UpdateTicks,
                ["cycleTimeSamples"] = BuildCycleTimeSamples(snapshot.ProcId),
                ["performance"] = snapshot.Performance == null
                    ? JValue.CreateNull()
                    : new JObject
                    {
                        ["enabled"] = snapshot.Performance.Enabled,
                        ["operationCount"] = snapshot.Performance.OperationCount,
                        ["operationsPerSecond"] = snapshot.Performance.OperationsPerSecond,
                        ["threadCpuPercent"] = snapshot.Performance.ThreadCpuPercent,
                        ["averageOperationMicroseconds"] = snapshot.Performance.AverageOperationMicroseconds,
                        ["maxOperationMicroseconds"] = snapshot.Performance.MaxOperationMicroseconds,
                        ["operationDurationSampleCount"] = snapshot.Performance.OperationDurationSampleCount,
                        ["operationDurationSamplingInterval"] = snapshot.Performance.OperationDurationSamplingInterval,
                        ["abnormalCpuLoopDetected"] = snapshot.Performance.AbnormalCpuLoopDetected
                    }
            };
        }

        private JArray BuildCycleTimeSamples(Guid procId)
        {
            return new JArray((runtime.ProcessEngine?.GetLatestCycleTimeSamples(procId)
                ?? Array.Empty<CycleTimeProbeSample>())
                .Select(sample => new JObject
                {
                    ["runId"] = sample.RunId == Guid.Empty ? string.Empty : sample.RunId.ToString("D"),
                    ["taskKey"] = sample.TaskKey ?? string.Empty,
                    ["segmentName"] = sample.SegmentName ?? string.Empty,
                    ["segmentIndex"] = sample.SegmentIndex,
                    ["cycleStarted"] = sample.CycleStarted,
                    ["segmentSeconds"] = sample.SegmentSeconds,
                    ["cycleSeconds"] = sample.CycleSeconds,
                    ["recordedAtUtc"] = sample.RecordedAtUtc.ToString("O", CultureInfo.InvariantCulture)
                }));
        }

        private static void AddFinding(JArray findings, string severity, string code, string message)
        {
            findings.Add(new JObject
            {
                ["severity"] = severity ?? string.Empty,
                ["code"] = code ?? string.Empty,
                ["message"] = message ?? string.Empty
            });
        }

        private CommReferenceCatalog GetCommNames()
        {
            List<string> tcp = (runtime.Stores.Communication?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            List<string> serial = (runtime.Stores.Communication?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return new CommReferenceCatalog
            {
                Tcp = tcp,
                Serial = serial,
                All = tcp.Concat(serial).Distinct(StringComparer.Ordinal).ToList()
            };
        }

        private static string NormalizePath(string path)
        {
            string value = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
            if (!value.StartsWith("/", StringComparison.Ordinal))
            {
                value = "/" + value;
            }
            value = value.TrimEnd('/');
            return string.IsNullOrEmpty(value) ? "/" : value;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject ParseRequestBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return new JObject();
            }

            try
            {
                JToken token = JToken.Parse(body);
                if (!(token is JObject obj))
                {
                    throw new BridgeRequestException(400, "INVALID_JSON", "请求体必须是 JSON 对象。");
                }
                return obj;
            }
            catch (JsonReaderException ex)
            {
                throw new BridgeRequestException(400, "INVALID_JSON", "请求体不是合法 JSON。", ex.Message);
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private T ExecuteOnUiThread<T>(Func<T> action)
        {
            if (owner.IsDisposed)
            {
                throw new BridgeRequestException(503, "BRIDGE_STOPPING", "主程序正在关闭，Bridge 不可用。");
            }

            if (owner.InvokeRequired)
            {
                return (T)owner.Invoke(action);
            }

            return action();
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void EnsureRuntimeReady()
        {
            if (runtime.EditorUi?.IsReady != true || runtime.Stores.Processes?.Items == null)
            {
                throw new BridgeRequestException(503, "BRIDGE_NOT_READY", "Automation 运行时尚未完成初始化。");
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private Proc GetProcByIndex(int procIndex)
        {
            EnsureRuntimeReady();
            if (procIndex < 0 || procIndex >= runtime.Stores.Processes.Items.Count)
            {
                throw new BridgeRequestException(404, "PROC_NOT_FOUND", $"流程索引无效：{procIndex}");
            }

            Proc proc = runtime.Stores.Processes.Items[procIndex];
            if (proc == null)
            {
                throw new BridgeRequestException(404, "PROC_NOT_FOUND", $"流程不存在：{procIndex}");
            }

            return proc;
        }

        private bool TryGetProcByIndexForRead(int procIndex, out Proc proc, out JObject error)
        {
            proc = null;
            error = null;
            if (runtime.EditorUi?.IsReady != true || runtime.Stores.Processes?.Items == null)
            {
                error = BridgeError(503, "BRIDGE_NOT_READY", "Automation 运行时尚未完成初始化。");
                return false;
            }
            if (procIndex < 0 || procIndex >= runtime.Stores.Processes.Items.Count)
            {
                error = BridgeError(
                    404,
                    "PROC_NOT_FOUND",
                    $"已提交的流程中不存在索引 {procIndex}；当前流程数为 {runtime.Stores.Processes.Items.Count}。",
                    new JObject
                    {
                        ["reason"] = "committed_process_not_found",
                        ["retryableWhen"] = "valid_committed_proc_index_provided",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
                return false;
            }
            proc = runtime.Stores.Processes.Items[procIndex];
            if (proc == null)
            {
                error = BridgeError(404, "PROC_NOT_FOUND", $"流程索引无效：{procIndex}。");
                return false;
            }
            return true;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static Step FindStepById(Proc proc, Guid stepId)
        {
            if (proc?.steps == null)
            {
                throw new BridgeRequestException(404, "STEP_NOT_FOUND", $"未找到步骤：{stepId:D}");
            }

            Step step = proc.steps.FirstOrDefault(item => item != null && item.Id == stepId);
            if (step == null)
            {
                throw new BridgeRequestException(404, "STEP_NOT_FOUND", $"未找到步骤：{stepId:D}");
            }

            return step;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static OperationType FindOperationById(Step step, Guid opId)
        {
            if (step?.Ops == null)
            {
                throw new BridgeRequestException(404, "OP_NOT_FOUND", $"未找到指令：{opId:D}");
            }

            OperationType op = step.Ops.FirstOrDefault(item => item != null && item.Id == opId);
            if (op == null)
            {
                throw new BridgeRequestException(404, "OP_NOT_FOUND", $"未找到指令：{opId:D}");
            }

            return op;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static OperationType CreateOperationTemplate(string operaType)
        {
            try
            {
                return OperationDefinitionRegistry.Create(operaType);
            }
            catch (KeyNotFoundException)
            {
                throw new BridgeRequestException(404, "OPERA_TYPE_NOT_FOUND", $"未找到指令类型：{operaType}");
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static Guid ParseGuid(string text, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 不能为空。");
            }

            if (!Guid.TryParse(text, out Guid value))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 不是合法 Guid；必须使用 get_proc_detail 或 list_procs(includeStepSummary=true) 返回的真实 Guid，不能使用占位值、名称或索引。当前值：{text}");
            }

            return value;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static int ReadRequiredInt(JObject request, string fieldName)
        {
            if (!request.TryGetValue(fieldName, out JToken token) || token == null || token.Type != JTokenType.Integer)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是整数。");
            }
            return token.Value<int>();
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static int? ReadOptionalInt(JObject request, string fieldName)
        {
            if (!request.TryGetValue(fieldName, out JToken token) || token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (token.Type != JTokenType.Integer)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是整数。");
            }
            return token.Value<int>();
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static Guid? ReadOptionalGuid(JObject request, string fieldName)
        {
            string value = ReadOptionalString(request, fieldName);
            if (value == null)
            {
                return null;
            }
            if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是非空 Guid。");
            }
            return parsed;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static bool? ReadOptionalBoolean(JObject request, string fieldName)
        {
            if (!request.TryGetValue(fieldName, out JToken token) || token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (token.Type != JTokenType.Boolean)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是布尔值。");
            }
            return token.Value<bool>();
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static string ReadRequiredString(JObject request, string fieldName)
        {
            string value = ReadOptionalString(request, fieldName);
            if (value == null)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是字符串。");
            }
            return value;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static string ReadOptionalString(JObject request, string fieldName)
        {
            if (!request.TryGetValue(fieldName, out JToken token) || token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (token.Type != JTokenType.String)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是字符串。");
            }
            return token.Value<string>();
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static JObject ReadOptionalObject(JObject request, string fieldName)
        {
            if (!request.TryGetValue(fieldName, out JToken token) || token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (!(token is JObject obj))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是对象。");
            }
            return obj;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static JObject ReadRequiredObject(JObject request, string fieldName)
        {
            JObject value = ReadOptionalObject(request, fieldName);
            if (value == null)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是对象。");
            }
            return value;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static JArray ReadRequiredArray(JObject request, string fieldName)
        {
            if (!request.TryGetValue(fieldName, out JToken token) || !(token is JArray array))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是数组。");
            }
            return array;
        }

        private static JArray ReadOptionalArray(JObject request, string fieldName)
        {
            JToken token = request?[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (!(token is JArray array))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是数组。");
            }
            return array;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void WithOperationReadContext(OperationType op, Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            WithOperationReadContext<object>(op, () =>
            {
                action();
                return null;
            });
        }

        private T WithOperationReadContext<T>(OperationType op, Func<T> action)
        {
            return WithOperationContext(op, false, action);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void WithOperationEditContext(OperationType op, Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            WithOperationEditContext<object>(op, () =>
            {
                action();
                return null;
            });
        }

        private T WithOperationEditContext<T>(OperationType op, Func<T> action)
        {
            return WithOperationContext(op, true, action);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private T WithOperationContext<T>(OperationType op, bool enableEditBehavior, Func<T> action)
        {
            if (op == null)
            {
                throw new BridgeRequestException(500, "OP_NULL", "指令为空。");
            }
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            if (runtime.EditorUi == null)
            {
                throw new BridgeRequestException(503, "BRIDGE_NOT_READY",
                    "Automation 编辑器适配器尚未完成初始化。");
            }
            return runtime.EditorUi.WithOperationContext(op, enableEditBehavior, action);
        }

        private static void RefreshOperationContext(OperationType op)
        {
            op.RefreshInspector?.Invoke();
            TypeDescriptor.Refresh(op);
        }

    }
}
