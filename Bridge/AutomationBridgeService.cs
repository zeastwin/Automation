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
        private const int MaxOverviewOperationCount = 300;
        private const int MaxDetailOperationCount = 100;
        private const int MaxBatchReadOperationCount = 25;
        // 19:45批次实测单轮输入已达5.6万到7.5万token；单个256KB结果足以独占同量级上下文。
        // 详情能力保留，通过摘要、分页和稳定ID精确读取把单次模型结果控制在64KB内。
        private const int MaxProcDetailUtf8Bytes = 64 * 1024;
        private const int MaxBatchReadUtf8Bytes = 64 * 1024;
        private const int MaxStepDetailOperationCount = 100;
        private const int MaxSnapshotPageSize = 100;
        private const int DefaultSnapshotPageSize = 50;
        private const int MaxDiagnosticFindingPageSize = 100;
        private const int DefaultDiagnosticFindingPageSize = 50;
        private const int MaxDiagnosticEvidencePageSize = 100;
        private const int DefaultDiagnosticEvidencePageSize = 40;
        private const int MaxInfoLogCount = 100;
        private const int DefaultInfoLogCount = 30;
        private static readonly LocalFileLogger bridgeErrorLogger = new LocalFileLogger(
            Path.Combine(@"D:\AutomationLogs", "Bridge"));
        private static readonly JsonSerializerSettings migrationContractJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly FrmMain owner;
        private readonly PlatformRuntime runtime;
        private readonly object previewLock = new object();
        private readonly Dictionary<string, PreviewApprovalRecord> previewRecords =
            new Dictionary<string, PreviewApprovalRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, DiagnosticProcIndex> diagnosticIndexes =
            new Dictionary<int, DiagnosticProcIndex>();

        public AutomationBridgeService(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            runtime = owner.Runtime;
        }

        /// <summary>
        /// 流程被 AI 改动后，在 FrmProc 节点和 FrmDataGrid 上触发闪烁动效，让用户直观看到改动位置。
        /// kind 决定颜色：Modified=橙黄、Added=浅绿、Deleted=浅红。
        /// FrmDataGrid 仅在用户当前正在浏览的流程 == 被改动的流程时才闪烁，
        /// 避免用户在查看别的流程时被打扰；FrmProc 节点闪烁不受影响（始终提示哪个流程被改）。
        /// affectedOps 非空时走行级闪烁（只闪被改动的指令行），为空时走整体闪烁。
        /// </summary>
        private void NotifyProcChanged(int procIndex, ProcChangeKind kind, List<(int stepIndex, int opIndex, ProcChangeKind kind)> affectedOps = null)
        {
            diagnosticIndexes.Remove(procIndex);
            runtime.EditorUi?.NotifyProcessChanged(procIndex, kind, affectedOps);
        }

        public AutomationBridgeResponse Handle(string method, string path, string body)
        {
            string normalizedPath = NormalizePath(path);
            try
            {
                if (string.Equals(normalizedPath, "/bridge/health", StringComparison.Ordinal))
                {
                    if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new BridgeRequestException(405, "METHOD_NOT_ALLOWED", "当前端点只支持 GET。");
                    }

                    JObject payload = ExecuteOnUiThread(() => new JObject
                    {
                        ["pipeName"] = AutomationBridgeHost.DefaultPipeName,
                        ["pipePath"] = AutomationBridgeHost.DefaultPipePath,
                        ["procCount"] = runtime.Stores.Processes?.Items?.Count ?? 0,
                        ["securityLocked"] = runtime.Safety.IsLocked
                    });
                    return AutomationBridgeResponse.Ok(BuildSuccessBody("bridge.health", payload));
                }

                if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    throw new BridgeRequestException(405, "METHOD_NOT_ALLOWED", "当前端点只支持 POST。");
                }

                JObject request = ParseRequestBody(body);
                switch (normalizedPath)
                {
                    // ---------- proc_query 拆分端点 ----------
                    case "/bridge/proc/list":
                        return WrapResponse("proc.list", ExecuteOnUiThread(() => HandleListProcs(request)));
                    case "/bridge/proc/overview":
                        return WrapResponse("proc.overview", ExecuteOnUiThread(() => HandleGetProcOverview(request)));
                    case "/bridge/proc/detail":
                        return WrapResponse("proc.detail", ExecuteOnUiThread(() => HandleGetProcDetail(request)));
                    case "/bridge/proc/flow_graph":
                        return WrapResponse("proc.flow_graph", ExecuteOnUiThread(() => HandleGetFlowGraph(request)));
                    case "/bridge/proc/op_detail":
                        return WrapResponse("proc.op_detail", ExecuteOnUiThread(() => HandleGetOperationDetail(request)));
                    case "/bridge/proc/op_details":
                        return WrapResponse("proc.op_details", ExecuteOnUiThread(() => HandleGetOperationDetails(request)));
                    case "/bridge/proc/step_detail":
                        return WrapResponse("proc.step_detail", ExecuteOnUiThread(() => HandleGetStepDetail(request)));
                    case "/bridge/proc/search":
                        return WrapResponse("proc.search", ExecuteOnUiThread(() => HandleSearchOperations(request)));
                    case "/bridge/proc/snapshot":
                        return WrapResponse("proc.snapshot", ExecuteOnUiThread(() => HandleGetRuntimeSnapshot(request)));
                    case "/bridge/proc/wait_state":
                        return WrapResponse("proc.wait_state", HandleWaitForProcState(request));
                    case "/bridge/proc/test_run":
                        return WrapResponse("proc.test_run", HandleRunProcTest(request));
                    // ---------- proc_diagnose 拆分端点 ----------
                    case "/bridge/proc/log_tail":
                        return WrapResponse("proc.log_tail", ExecuteOnUiThread(() => HandleGetInfoLogTail(request)));
                    case "/bridge/proc/diagnose":
                        return WrapResponse("proc.diagnose", ExecuteOnUiThread(() => HandleDiagnoseProc(request)));
                    case "/bridge/proc/validate":
                        return WrapResponse("proc.validate", ExecuteOnUiThread(() => HandleValidateProc(request)));
                    case "/bridge/diagnostics/references":
                        return WrapResponse("diagnostics.references", ExecuteOnUiThread(() => HandleFindReferences(request)));
                    case "/bridge/diagnostics/operation_references":
                        return WrapResponse("diagnostics.operation_references", ExecuteOnUiThread(() => HandleGetOperationReferences(request)));
                    case "/bridge/diagnostics/proc_references":
                        return WrapResponse("diagnostics.proc_references", ExecuteOnUiThread(() => HandleGetProcReferences(request)));
                    case "/bridge/diagnostics/trace_resource":
                        return WrapResponse("diagnostics.trace_resource", ExecuteOnUiThread(() => HandleTraceResource(request)));
                    case "/bridge/diagnostics/search_fields":
                        return WrapResponse("diagnostics.search_fields", ExecuteOnUiThread(() => HandleSearchOperationFields(request)));
                    case "/bridge/diagnostics/context":
                        return WrapResponse("diagnostics.context", ExecuteOnUiThread(() => HandleGetOperationContext(request)));
                    case "/bridge/diagnostics/audit":
                        return WrapResponse("diagnostics.audit", ExecuteOnUiThread(() => HandleAuditProcBatch(request)));
                    case "/bridge/diagnostics/issue":
                        return WrapResponse("diagnostics.issue", ExecuteOnUiThread(() => HandleDiagnoseIssue(request)));
                    // ---------- AI 语义变更集 V2 ----------
                    case "/bridge/change-set/preview":
                        AiChangeSet changeSet = ParseChangeSet(request);
                        string replacePreviewId = ReadOptionalString(request, "replacePreviewId");
                        return WrapResponse("change_set.preview",
                            ExecuteOnUiThread(() => HandlePreviewChangeSet(changeSet, replacePreviewId)));
                    case "/bridge/change-set/apply":
                        WaitForPreviewConfirmation(request);
                        return WrapResponse("change_set.apply", ExecuteOnUiThread(() => HandleApplyChangeSet(request)));
                    case "/bridge/change-set/capabilities":
                        return WrapResponse("change_set.capabilities", AiOperationCompilerRegistry.BuildCapabilities());
                    case "/bridge/change-set/contracts":
                        return WrapResponse("change_set.contracts", HandleGetChangeSetContracts(request));
                    case "/bridge/change-set/native-contract":
                        return WrapResponse("change_set.native_contract", HandleGetNativeOperationContract(request));
                    case "/bridge/change-set/native-contracts":
                        return WrapResponse("change_set.native_contracts", HandleGetNativeOperationContracts(request));
                    // ---------- proc_manage 拆分端点（previewId 为空预演，非空提交） ----------
                    case "/bridge/proc/create":
                        WaitForPreviewConfirmation(request, false);
                        return WrapResponse("proc.create", ExecuteOnUiThread(() => HandleCreateOrApply(request)));
                    case "/bridge/proc/delete":
                        WaitForPreviewConfirmation(request, false);
                        return WrapResponse("proc.delete", ExecuteOnUiThread(() => HandleDeleteOrApply(request)));
                    case "/bridge/proc/reorder":
                        WaitForPreviewConfirmation(request, false);
                        return WrapResponse("proc.reorder", ExecuteOnUiThread(() => HandleReorderOrApply(request)));
                    case "/bridge/proc/copy":
                        WaitForPreviewConfirmation(request, false);
                        return WrapResponse("proc.copy", ExecuteOnUiThread(() => HandleCopyOrApply(request)));
                    // ---------- control_proc 拆分端点（直接构造 action 调用 HandleControlProc） ----------
                    case "/bridge/proc/start":
                        return WrapResponse("proc.start", ExecuteOnUiThread(() => HandleControlProc(BuildControlRequest(request, "start"))));
                    case "/bridge/proc/stop":
                        return WrapResponse("proc.stop", ExecuteOnUiThread(() => HandleControlProc(BuildControlRequest(request, "stop"))));
                    case "/bridge/proc/pause":
                        return WrapResponse("proc.pause", ExecuteOnUiThread(() => HandleControlProc(BuildControlRequest(request, "pause"))));
                    case "/bridge/proc/resume":
                        return WrapResponse("proc.resume", ExecuteOnUiThread(() => HandleControlProc(BuildControlRequest(request, "resume"))));
                    // ---------- variable 拆分端点 ----------
                    case "/bridge/variable/list":
                        return WrapResponse("variable.list", ExecuteOnUiThread(() => HandleListVariables(request)));
                    case "/bridge/variable/get":
                        return WrapResponse("variable.get", ExecuteOnUiThread(() => HandleGetVariable(request)));
                    case "/bridge/variable/search":
                        return WrapResponse("variable.search", ExecuteOnUiThread(() => HandleSearchVariables(request)));
                    case "/bridge/variable/set":
                        return WrapResponse("variable.set", ExecuteOnUiThread(() => HandleSetVariable(request)));
                    case "/bridge/variable/delete":
                        return WrapResponse("variable.delete", ExecuteOnUiThread(() => HandleDeleteVariable(request)));
                    case "/bridge/variable/add":
                        return WrapResponse("variable.add", ExecuteOnUiThread(() => HandleAddVariable(request)));
                    case "/bridge/variable/update":
                        return WrapResponse("variable.update", ExecuteOnUiThread(() => HandleUpdateVariable(request)));
                    // ---------- station 拆分端点 ----------
                    case "/bridge/station/list":
                        return WrapResponse("station.list", ExecuteOnUiThread(() => HandleListStations(request)));
                    case "/bridge/station/get":
                        return WrapResponse("station.get", ExecuteOnUiThread(() => HandleGetStation(request)));
                    case "/bridge/station/add":
                        return WrapResponse("station.add", ExecuteOnUiThread(() => HandleAddStation(request)));
                    case "/bridge/station/delete":
                        return WrapResponse("station.delete", ExecuteOnUiThread(() => HandleDeleteStation(request)));
                    case "/bridge/station/update":
                        return WrapResponse("station.update", ExecuteOnUiThread(() => HandleUpdateStation(request)));
                    // ---------- point 拆分端点 ----------
                    case "/bridge/point/list":
                        return WrapResponse("point.list", ExecuteOnUiThread(() => HandleListPoints(request)));
                    case "/bridge/point/get":
                        return WrapResponse("point.get", ExecuteOnUiThread(() => HandleGetPoint(request)));
                    case "/bridge/point/set":
                        return WrapResponse("point.set", ExecuteOnUiThread(() => HandleSetPoint(request)));
                    case "/bridge/point/delete":
                        return WrapResponse("point.delete", ExecuteOnUiThread(() => HandleDeletePoint(request)));
                    // ---------- data_struct 拆分端点 ----------
                    case "/bridge/data_struct/list":
                        return WrapResponse("data_struct.list", ExecuteOnUiThread(() => HandleListDataStructs(request)));
                    case "/bridge/data_struct/get":
                        return WrapResponse("data_struct.get", ExecuteOnUiThread(() => HandleGetDataStruct(request)));
                    case "/bridge/data_struct/search":
                        return WrapResponse("data_struct.search", ExecuteOnUiThread(() => HandleSearchDataStructs(request)));
                    case "/bridge/data_struct/set_field":
                        return WrapResponse("data_struct.set_field", ExecuteOnUiThread(() => HandleSetDataStructField(request)));
                    case "/bridge/data_struct/upsert":
                        return WrapResponse("data_struct.upsert", ExecuteOnUiThread(() => HandleUpsertDataStruct(request)));
                    case "/bridge/data_struct/delete":
                        return WrapResponse("data_struct.delete", ExecuteOnUiThread(() => HandleDeleteDataStruct(request)));
                    // ---------- migration capability pack ----------
                    case "/bridge/migration/get":
                        return WrapResponse("migration.configuration", ExecuteOnUiThread(() => HandleGetMigrationConfiguration(request)));
                    case "/bridge/migration/motion-io/preview":
                        return WrapResponse("migration.preview", ExecuteOnUiThread(() => HandlePreviewMotionIoConfiguration(request)));
                    case "/bridge/migration/io-debug/preview":
                        return WrapResponse("migration.preview", ExecuteOnUiThread(() => HandlePreviewIoDebugConfiguration(request)));
                    case "/bridge/migration/plc/preview":
                        return WrapResponse("migration.preview", ExecuteOnUiThread(() => HandlePreviewPlcConfiguration(request)));
                    case "/bridge/migration/communication/preview":
                        return WrapResponse("migration.preview", ExecuteOnUiThread(() => HandlePreviewCommunicationConfiguration(request)));
                    case "/bridge/migration/apply":
                        WaitForPreviewConfirmation(request);
                        return WrapResponse("migration.apply", ExecuteOnUiThread(() => HandleApplyMigrationConfiguration(request)));
                    case "/bridge/migration/validate":
                        return WrapResponse("migration.validation", ExecuteOnUiThread(HandleValidatePlatformConfiguration));
                    // ---------- io 拆分端点 ----------
                    case "/bridge/io/list":
                        return WrapResponse("io.list", ExecuteOnUiThread(() => HandleListIo(request)));
                    case "/bridge/io/get":
                        return WrapResponse("io.get", ExecuteOnUiThread(() => HandleGetIo(request)));
                    case "/bridge/io/search":
                        return WrapResponse("io.search", ExecuteOnUiThread(() => HandleSearchIo(request)));
                    case "/bridge/io/state":
                        return WrapResponse("io.state", ExecuteOnUiThread(() => HandleGetIoState(request)));
                    // ---------- alarm 拆分端点 ----------
                    case "/bridge/alarm/list":
                        return WrapResponse("alarm.list", ExecuteOnUiThread(() => HandleListAlarms(request)));
                    case "/bridge/alarm/get":
                        return WrapResponse("alarm.get", ExecuteOnUiThread(() => HandleGetAlarm(request)));
                    case "/bridge/alarm/set":
                        return WrapResponse("alarm.set", ExecuteOnUiThread(() => HandleSetAlarm(request)));
                    case "/bridge/alarm/delete":
                        return WrapResponse("alarm.delete", ExecuteOnUiThread(() => HandleDeleteAlarm(request)));
                    // ---------- 保留的合并端点 ----------
                    case "/bridge/op/meta":
                        return WrapResponse("op.meta", ExecuteOnUiThread(() => HandleOpMeta(request)));
                    case "/bridge/resources":
                        return WrapResponse("resources", ExecuteOnUiThread(() => HandleListResources(request)));
                    case "/bridge/previews/confirm":
                        return WrapResponse("preview.confirm", ExecuteOnUiThread(() => HandleConfirmPreview(request)));
                    case "/bridge/previews/reject":
                        return WrapResponse("preview.reject", HandleRejectPreview(request));
                    default:
                        throw new BridgeRequestException(404, "NOT_FOUND", $"未知的 Bridge 端点：{normalizedPath}");
                }
            }
            catch (BridgeRequestException ex)
            {
                LogBridgeException(method, normalizedPath, body, ex);
                return AutomationBridgeResponse.Error(ex.StatusCode, ex.Code, ex.Message, ex.Details);
            }
            catch (Exception ex)
            {
                LogBridgeException(method, normalizedPath, body, ex);
                return AutomationBridgeResponse.Error(500, "UNHANDLED_EXCEPTION", "Automation Bridge 处理失败。", ex.Message);
            }
        }

        private static void LogBridgeException(string method, string path, string body, Exception exception)
        {
            try
            {
                const int maxBodyLength = 16384;
                string requestBody = body ?? string.Empty;
                if (requestBody.Length > maxBodyLength)
                {
                    requestBody = requestBody.Substring(0, maxBodyLength) + "...<已截断>";
                }
                var builder = new StringBuilder();
                builder.AppendLine("Bridge 请求异常");
                builder.Append("method=").AppendLine(method ?? string.Empty);
                builder.Append("path=").AppendLine(path ?? string.Empty);
                builder.Append("body=").AppendLine(requestBody);
                if (exception is BridgeRequestException bridgeException)
                {
                    builder.Append("statusCode=").AppendLine(bridgeException.StatusCode.ToString(CultureInfo.InvariantCulture));
                    builder.Append("errorCode=").AppendLine(bridgeException.Code ?? string.Empty);
                    builder.Append("details=").AppendLine(bridgeException.Details ?? string.Empty);
                }
                else
                {
                    builder.AppendLine("statusCode=500");
                    builder.AppendLine("errorCode=UNHANDLED_EXCEPTION");
                    builder.Append("details=").AppendLine(exception?.Message ?? string.Empty);
                }
                builder.Append("exception=").Append(exception?.ToString() ?? string.Empty);
                LogLevel level = exception is BridgeRequestException bridgeRequest
                    && bridgeRequest.StatusCode < 500
                    ? LogLevel.Normal
                    : LogLevel.Error;
                bridgeErrorLogger.Log(builder.ToString(), level);
            }
            catch
            {
                // 异常日志失败不影响 Bridge 的标准错误响应。
            }
        }

        public static string BuildErrorBody(string code, string message, string details = null)
        {
            var error = new JObject
            {
                ["ok"] = false,
                ["type"] = "bridge.error",
                ["errorCode"] = code ?? string.Empty,
                ["message"] = message ?? string.Empty
            };
            if (!string.IsNullOrWhiteSpace(details))
            {
                try
                {
                    JToken recovery = JToken.Parse(details);
                    if (recovery is JObject)
                    {
                        error["recovery"] = recovery;
                    }
                    else
                    {
                        error["details"] = details;
                    }
                }
                catch (JsonReaderException)
                {
                    error["details"] = details;
                }
            }
            return error.ToString(Formatting.None);
        }

        private static string BuildSuccessBody(string type, JToken data)
        {
            return new JObject
            {
                ["ok"] = true,
                ["type"] = type,
                ["data"] = data ?? JValue.CreateNull()
            }.ToString(Formatting.None);
        }

        // Bridge 错误返回对象：handler 方法用 return 代替 throw 时使用。
        // 通过 __bridgeError 标记让 WrapResponse 识别并转换为 AutomationBridgeResponse.Error。
        private static JObject BridgeError(int statusCode, string code, string message, string details = null)
        {
            return new JObject
            {
                ["__bridgeError"] = true,
                ["statusCode"] = statusCode,
                ["code"] = code ?? string.Empty,
                ["message"] = message ?? string.Empty,
                ["details"] = string.IsNullOrWhiteSpace(details) ? null : details
            };
        }

        // 包装 handler 返回值：如果是 BridgeError 错误对象则返回 AutomationBridgeResponse.Error，否则按成功包装。
        private static AutomationBridgeResponse WrapResponse(string type, JObject result)
        {
            if (result != null && result["__bridgeError"] is JToken flag && flag.Value<bool>())
            {
                int statusCode = result["statusCode"]?.Value<int>() ?? 400;
                string code = result["code"]?.Value<string>() ?? "BRIDGE_ERROR";
                string message = result["message"]?.Value<string>() ?? string.Empty;
                string details = result["details"]?.Value<string>();
                return AutomationBridgeResponse.Error(statusCode, code, message, details);
            }
            return AutomationBridgeResponse.Ok(BuildSuccessBody(type, result));
        }

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

        private static bool TryReadString(JObject obj, string fieldName, out string value, out JObject error)
        {
            value = null;
            error = null;
            JToken token = obj?[fieldName];
            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
            {
                error = BridgeError(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是非空字符串。",
                    "当前类型：" + (token?.Type.ToString() ?? "缺失"));
                return false;
            }
            value = token.Value<string>();
            return true;
        }

        private static bool TryReadInteger(JObject obj, string fieldName, out int value, out JObject error)
        {
            value = 0;
            error = null;
            JToken token = obj?[fieldName];
            if (token == null || token.Type != JTokenType.Integer)
            {
                error = BridgeError(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是整数。",
                    "当前类型：" + (token?.Type.ToString() ?? "缺失"));
                return false;
            }
            value = token.Value<int>();
            return true;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleConfirmPreview(JObject request)
        {
            string previewId = ReadRequiredString(request, "previewId");
            PreviewApprovalRecord record;
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out record))
                {
                    return BridgeError(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                }
                if (record.Rejected)
                {
                    return BridgeError(409, "PREVIEW_REJECTED", $"预演已结束，不能再次确认：{previewId}");
                }

                EnsurePreviewProcVersion(record);
                record.Confirmed = true;
                record.ConfirmedAtUtc = DateTime.UtcNow;
                Monitor.PulseAll(previewLock);
            }

            return new JObject
            {
                ["previewId"] = record.PreviewId,
                ["patchHash"] = record.PatchHash,
                ["procIndex"] = record.ProcIndex,
                ["baseProcId"] = record.BaseProcId,
                ["confirmed"] = true,
                ["expiresAt"] = record.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
        }

        private JObject HandleRejectPreview(JObject request)
        {
            string previewId = ReadRequiredString(request, "previewId");
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record))
                {
                    return BridgeError(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                }
                record.Rejected = true;
                Monitor.PulseAll(previewLock);
            }
            return new JObject { ["previewId"] = previewId, ["rejected"] = true };
        }

        // 提交请求可能在用户操作审核窗口前抵达。在 Bridge 工作线程等待审核结果，
        // 不占用 UI 线程；确认后原提交直接继续，拒绝则明确终止。
        private void WaitForPreviewConfirmation(JObject request, bool previewIdRequired = true)
        {
            string previewId = ReadOptionalString(request, "previewId");
            if (string.IsNullOrWhiteSpace(previewId))
            {
                if (previewIdRequired)
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", "提交阶段必须携带 previewId。");
                }
                return;
            }
            ValidatePreviewIdFormat(previewId);
            DateTime deadline = DateTime.UtcNow.AddSeconds(110);
            lock (previewLock)
            {
                while (true)
                {
                    CleanupExpiredPreviewsLocked();
                    if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record))
                    {
                        throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                    }
                    if (record.Rejected)
                    {
                        throw new BridgeRequestException(409, "PREVIEW_REJECTED", "预演已结束或被替换，本次提交未执行。");
                    }
                    if (record.Confirmed)
                    {
                        return;
                    }
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        throw new BridgeRequestException(408, "PREVIEW_CONFIRM_TIMEOUT", "等待前台确认预演超时，未执行提交。");
                    }
                    Monitor.Wait(previewLock, remaining);
                }
            }
        }

        // 流程运行控制：启动/停止/暂停/恢复。不需要预演确认。
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleControlProc(JObject request)
        {
            EnsureRuntimeReady();
            int procIndex = ReadRequiredInt(request, "procIndex");
            string action = ReadRequiredString(request, "action");
            Proc proc = GetProcByIndex(procIndex);
            EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
            ProcRunState currentState = snapshot?.State ?? ProcRunState.Stopped;

            switch (action)
            {
                case "start":
                    if (currentState != ProcRunState.Stopped)
                    {
                        return BridgeError(409, "PROC_NOT_STOPPED",
                            $"流程 {procIndex} 尚未结束，当前状态为 {currentState}。请排查流程未结束原因后再启动。");
                    }
                    if (!runtime.ProcessEngine.StartProc(proc, procIndex))
                    {
                        string startError;
                        if (!runtime.ProcessEngine.TryValidateProcessStopped(procIndex, out string stoppedError))
                        {
                            startError = stoppedError;
                        }
                        else
                        {
                            startError = runtime.ProcessEngine.TryValidateProcessStart(proc, procIndex, out string gateError)
                                ? "流程启动请求未被内核接受，详见流程日志。"
                                : gateError;
                        }
                        return BridgeError(409, "START_GATE_REJECTED", startError);
                    }
                    break;
                case "stop":
                    if (currentState == ProcRunState.Stopped)
                    {
                        return BridgeError(409, "PROC_NOT_RUNNING", $"流程 {procIndex} 未在运行。");
                    }
                    runtime.ProcessEngine.Stop(procIndex);
                    break;
                case "pause":
                    if (currentState != ProcRunState.Running)
                    {
                        return BridgeError(409, "PROC_NOT_RUNNING", $"流程 {procIndex} 不在运行状态，无法暂停。");
                    }
                    runtime.ProcessEngine.Pause(procIndex);
                    break;
                case "resume":
                    if (currentState != ProcRunState.Paused)
                    {
                        return BridgeError(409, "PROC_NOT_PAUSED", $"流程 {procIndex} 不在暂停状态，无法恢复。");
                    }
                    runtime.ProcessEngine.Resume(procIndex);
                    break;
                default:
                    return BridgeError(400, "UNSUPPORTED_ACTION", $"不支持的流程控制操作：{action}。支持：start, stop, pause, resume");
            }

            return new JObject
            {
                ["procIndex"] = procIndex,
                ["action"] = action,
                ["procName"] = proc?.head?.Name ?? string.Empty,
                ["previousState"] = currentState.ToString(),
                ["message"] = $"已发送{action}命令到流程 {procIndex}"
            };
        }

        private JObject PreviewCreateProc(JObject request)
        {
            EnsureRuntimeReady();
            string name = ReadRequiredString(request, "name");
            bool autoStart = ReadOptionalBoolean(request, "autoStart") ?? false;
            bool disable = ReadOptionalBoolean(request, "disable") ?? false;
            EnsureAllProcsStoppedForAiStructureCommit("创建流程");

            // 预演：创建 proc 对象但不加入 procsList
            Proc proc = new Proc();
            proc.head = new ProcHead { Name = name, AutoStart = autoStart, Disable = disable };

            int targetIndex = runtime.Stores.Processes.Items.Count;
            JObject preview = new JObject
            {
                ["action"] = "create_proc",
                ["targetIndex"] = targetIndex,
                ["procName"] = name,
                ["autoStart"] = autoStart,
                ["disable"] = disable,
                ["messages"] = new JArray { $"将在索引 {targetIndex} 创建流程「{name}」" }
            };

            string previewId = RegisterManagePreview(preview);
            preview["previewId"] = previewId;
            preview["committed"] = false;
            return preview;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject PreviewDeleteProcs(JObject request)
        {
            EnsureRuntimeReady();
            JArray indexes = ReadRequiredArray(request, "procIndexes");
            if (indexes == null || indexes.Count == 0)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "procIndexes 不能为空。");
            }

            var procInfos = new JArray();
            var messages = new JArray();
            for (int i = 0; i < indexes.Count; i++)
            {
                int procIndex = indexes[i].Value<int>();
                if (procIndex < 0 || procIndex >= runtime.Stores.Processes.Items.Count)
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"流程索引 {procIndex} 越界。");
                }
                Proc proc = runtime.Stores.Processes.Items[procIndex];
                EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
                if (snapshot != null && snapshot.State != ProcRunState.Stopped)
                {
                    throw new BridgeRequestException(409, "PROC_RUNNING", $"流程 {procIndex}「{proc?.head?.Name}」正在运行，请先停止。");
                }
                procInfos.Add(new JObject
                {
                    ["procIndex"] = procIndex,
                    ["procName"] = proc?.head?.Name ?? string.Empty,
                    ["procId"] = proc?.head?.Id.ToString("D") ?? string.Empty
                });
                messages.Add($"将删除流程 {procIndex}「{proc?.head?.Name ?? procIndex.ToString()}」");
            }

            JObject preview = new JObject
            {
                ["action"] = "delete_procs",
                ["procIndexes"] = indexes,
                ["procs"] = procInfos,
                ["messages"] = messages
            };

            string previewId = RegisterManagePreview(preview);
            preview["previewId"] = previewId;
            return preview;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject PreviewReorderProc(JObject request)
        {
            EnsureRuntimeReady();
            int procIndex = ReadRequiredInt(request, "procIndex");
            int targetIndex = ReadRequiredInt(request, "targetIndex");
            Proc proc = GetProcByIndex(procIndex);
            if (targetIndex < 0 || targetIndex >= runtime.Stores.Processes.Items.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"目标索引 {targetIndex} 越界。");
            }
            EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
            if (snapshot != null && snapshot.State != ProcRunState.Stopped)
            {
                throw new BridgeRequestException(409, "PROC_RUNNING", $"流程 {procIndex} 正在运行，请先停止。");
            }

            JObject preview = new JObject
            {
                ["action"] = "reorder_proc",
                ["procIndex"] = procIndex,
                ["targetIndex"] = targetIndex,
                ["procName"] = proc?.head?.Name ?? string.Empty,
                ["messages"] = new JArray { $"将流程 {procIndex}「{proc?.head?.Name}」移动到索引 {targetIndex}" }
            };

            string previewId = RegisterManagePreview(preview);
            preview["previewId"] = previewId;
            return preview;
        }

        private JObject PreviewCopyProc(JObject request)
        {
            EnsureRuntimeReady();
            int procIndex = ReadRequiredInt(request, "procIndex");
            string newName = ReadOptionalString(request, "newName");
            Proc source = GetProcByIndex(procIndex);

            Proc copy = ObjectGraphCloner.Clone(source);
            copy.head.Id = Guid.NewGuid();
            copy.head.Name = ResolveCopiedProcessName(source.head?.Name, newName);
            copy.head.AutoStart = false;
            Dictionary<string, DicValue> draftVariables = runtime.Stores.Values.BuildSaveData();
            ProcessVariableCopyResult variableCopy = ProcessVariableLifecycleService.CopyPrivateVariables(
                source.head.Id, copy.head.Id, copy, draftVariables);
            JArray variableMappings = new JArray(variableCopy.Mappings.Select(mapping => new JObject
            {
                ["sourceVariableId"] = mapping.SourceVariableId,
                ["variableId"] = mapping.VariableId,
                ["sourceName"] = mapping.SourceName,
                ["name"] = mapping.Name,
                ["sourceIndex"] = mapping.SourceIndex,
                ["index"] = mapping.Index,
                ["scope"] = VariableScopeContract.Process,
                ["ownerProcId"] = copy.head.Id,
                ["ownerProcName"] = copy.head.Name
            }));

            int targetIndex = runtime.Stores.Processes.Items.Count;
            JObject preview = new JObject
            {
                ["action"] = "copy_proc",
                ["sourceProcIndex"] = procIndex,
                ["sourceProcName"] = source.head?.Name ?? string.Empty,
                ["targetIndex"] = targetIndex,
                ["newProcName"] = copy.head.Name,
                ["stepCount"] = copy.steps?.Count ?? 0,
                ["variableMappings"] = variableMappings,
                ["warnings"] = new JArray(variableCopy.Warnings),
                ["messages"] = new JArray { $"将复制流程 {procIndex}「{source.head?.Name}」到索引 {targetIndex}，新名称「{copy.head.Name}」" }
            };

            string previewId = RegisterManagePreview(preview);
            preview["previewId"] = previewId;
            return preview;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject ExecuteCreateProc(JObject request)
        {
            string name = ReadRequiredString(request, "name");
            bool autoStart = ReadOptionalBoolean(request, "autoStart") ?? false;
            bool disable = ReadOptionalBoolean(request, "disable") ?? false;

            Proc proc = new Proc();
            proc.head = new ProcHead { Name = name, AutoStart = autoStart, Disable = disable };

            int procIndex = runtime.Stores.Processes.Items.Count;
            runtime.Stores.Processes.Items.Add(proc);

            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(
                procIndex, proc, errors, runtime.CreateProcessValidationContext());
            if (errors.Count > 0)
            {
                runtime.Stores.Processes.Items.RemoveAt(procIndex);
                throw new BridgeRequestException(400, "PROC_VALIDATE_FAILED", "流程创建校验失败。", string.Join("\r\n", errors.Distinct()));
            }

            if (runtime.EditorUi?.RebuildWorkConfig(procIndex) != true)
            {
                throw new BridgeRequestException(500, "SAVE_FAILED", "流程创建失败，原流程配置已恢复。");
            }
            NotifyProcChanged(procIndex, ProcChangeKind.Added);

            return new JObject
            {
                ["action"] = "create_proc",
                ["procIndex"] = procIndex,
                ["procName"] = name,
                ["messages"] = new JArray { $"流程「{name}」已创建，索引 {procIndex}" }
            };
        }

        private JObject HandleWaitForProcState(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int timeoutMs = ReadOptionalInt(request, "timeoutMs") ?? 30000;
            if (timeoutMs < 100 || timeoutMs > 60000)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "timeoutMs 必须在 100..60000 之间。");
            }
            JArray statesToken = ReadOptionalArray(request, "states") ?? new JArray("Stopped", "Alarming");
            if (statesToken.Count < 1 || statesToken.Count > 4)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "states 必须包含 1..4 个目标状态。");
            }
            var targetStates = new HashSet<ProcRunState>();
            foreach (JToken stateToken in statesToken)
            {
                if (stateToken.Type != JTokenType.String
                    || !Enum.TryParse(stateToken.Value<string>(), false, out ProcRunState state))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT",
                        $"不支持的流程状态：{stateToken}. 可选 Stopped/Running/Paused/Alarming/Stopping。");
                }
                targetStates.Add(state);
            }

            DateTime startedAt = DateTime.UtcNow;
            EngineSnapshot snapshot;
            Guid expectedProcId = Guid.Empty;
            while (true)
            {
                if (runtime.ProcessEngine == null || runtime.ProcessEngine.Context?.Procs == null)
                {
                    throw new BridgeRequestException(503, "RUNTIME_NOT_READY", "流程运行时尚未初始化。");
                }
                if (procIndex < 0 || procIndex >= runtime.ProcessEngine.Context.Procs.Count)
                {
                    throw new BridgeRequestException(404, "PROC_NOT_FOUND", $"流程索引不存在：{procIndex}");
                }
                Guid currentProcId = runtime.ProcessEngine.Context.Procs[procIndex]?.head?.Id ?? Guid.Empty;
                if (expectedProcId == Guid.Empty) expectedProcId = currentProcId;
                else if (currentProcId != expectedProcId)
                {
                    throw new BridgeRequestException(409, "PROC_ID_CHANGED",
                        $"等待期间流程索引 {procIndex} 已指向其他流程，已停止等待以避免误判。");
                }
                snapshot = runtime.ProcessEngine.GetSnapshot(procIndex);
                if (snapshot != null && targetStates.Contains(snapshot.State))
                {
                    break;
                }
                if ((DateTime.UtcNow - startedAt).TotalMilliseconds >= timeoutMs)
                {
                    return new JObject
                    {
                        ["reached"] = false,
                        ["timedOut"] = true,
                        ["elapsedMs"] = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                        ["snapshot"] = snapshot == null ? null : BuildEngineSnapshot(snapshot, procIndex)
                    };
                }
                Thread.Sleep(50);
            }
            return new JObject
            {
                ["reached"] = true,
                ["timedOut"] = false,
                ["elapsedMs"] = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                ["snapshot"] = BuildEngineSnapshot(snapshot, procIndex)
            };
        }

        private JObject HandleRunProcTest(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int durationMs = ReadOptionalInt(request, "durationMs") ?? 5000;
            if (durationMs < 500 || durationMs > 15000)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "durationMs 必须在 500..15000 之间。");
            }

            Guid procId = Guid.Empty;
            string procName = string.Empty;
            ExecuteOnUiThread(() =>
            {
                EnsureRuntimeReady();
                Proc proc = GetProcByIndex(procIndex);
                EngineSnapshot before = runtime.ProcessEngine.GetSnapshot(procIndex);
                if (before != null && before.State != ProcRunState.Stopped)
                {
                    throw new BridgeRequestException(409, "PROC_ALREADY_RUNNING",
                        $"流程 {procIndex} 已处于 {before.State}，测试运行不会接管已有运行实例。");
                }
                procId = proc.head?.Id ?? Guid.Empty;
                procName = proc.head?.Name ?? string.Empty;
                if (!runtime.ProcessEngine.StartProc(proc, procIndex))
                {
                    string startError = runtime.ProcessEngine.TryValidateProcessStart(proc, procIndex, out string gateError)
                        ? "流程测试启动请求未被内核接受，详见流程日志。"
                        : gateError;
                    throw new BridgeRequestException(409, "START_GATE_REJECTED", startError);
                }
                return true;
            });

            DateTime startedAt = DateTime.UtcNow;
            bool observedRunning = false;
            int positionChanges = 0;
            string lastPosition = null;
            EngineSnapshot snapshot = null;
            bool stoppedByTestRunner = false;
            ProcTerminationReason requestedReason = ProcTerminationReason.TestWindowElapsed;
            try
            {
                while ((DateTime.UtcNow - startedAt).TotalMilliseconds < durationMs)
                {
                    snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
                    if (snapshot != null)
                    {
                        if (snapshot.ProcId != procId)
                        {
                            throw new BridgeRequestException(409, "PROC_ID_CHANGED",
                                $"测试期间流程索引 {procIndex} 已指向其他流程，已中止结果判定。");
                        }
                        observedRunning |= snapshot.State == ProcRunState.Running
                            || snapshot.State == ProcRunState.Paused
                            || snapshot.State == ProcRunState.SingleStep;
                        string position = $"{snapshot.StepIndex}-{snapshot.OpIndex}";
                        if (lastPosition != null && !string.Equals(lastPosition, position, StringComparison.Ordinal))
                        {
                            positionChanges++;
                        }
                        lastPosition = position;
                        if (snapshot.State == ProcRunState.Alarming)
                        {
                            requestedReason = ProcTerminationReason.Alarm;
                            break;
                        }
                        if (snapshot.State == ProcRunState.Stopped)
                        {
                            break;
                        }
                    }
                    Thread.Sleep(50);
                }
            }
            finally
            {
                ExecuteOnUiThread(() =>
                {
                    EngineSnapshot current = runtime.ProcessEngine?.GetSnapshot(procIndex);
                    if (current != null && current.ProcId == procId && current.State != ProcRunState.Stopped)
                    {
                        runtime.ProcessEngine.Stop(procIndex, requestedReason);
                        stoppedByTestRunner = true;
                    }
                    return true;
                });
            }

            DateTime stopDeadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
                if (snapshot != null && snapshot.State == ProcRunState.Stopped)
                {
                    break;
                }
                Thread.Sleep(25);
            }
            while (DateTime.UtcNow < stopDeadline);

            if (snapshot == null || snapshot.State != ProcRunState.Stopped)
            {
                throw new BridgeRequestException(500, "TEST_RUN_STOP_TIMEOUT",
                    $"流程 {procIndex} 测试窗口结束后未能在 3 秒内停止，已保持安全停止请求，请人工检查设备与流程状态。");
            }

            string outcome;
            switch (snapshot.TerminationReason)
            {
                case ProcTerminationReason.Completed:
                    outcome = "NaturallyCompleted";
                    break;
                case ProcTerminationReason.Disabled:
                    outcome = "NotExecutedDisabled";
                    break;
                case ProcTerminationReason.TestWindowElapsed:
                    outcome = "ObservationWindowCompleted";
                    break;
                case ProcTerminationReason.Alarm:
                    outcome = "Alarmed";
                    break;
                default:
                    outcome = "ExternallyStopped";
                    break;
            }
            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procId"] = procId.ToString("D"),
                ["procName"] = procName,
                ["outcome"] = outcome,
                ["observedRunning"] = observedRunning,
                ["positionChanges"] = positionChanges,
                ["stoppedByTestRunner"] = stoppedByTestRunner,
                ["continuationAuthorized"] = false,
                ["startRequiresExplicitUserRequest"] = true,
                ["elapsedMs"] = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                ["snapshot"] = BuildEngineSnapshot(snapshot, procIndex),
                ["runtimeEvidence"] = runtime.RuntimeBlackBoxRecorder?.BuildEvidencePackage(procIndex)
                    ?? RuntimeBlackBoxRecorder.BuildUnavailableEvidencePackage(procIndex)
            };
        }

        private JObject ExecuteDeleteProcs(JObject request)
        {
            JArray indexes = ReadRequiredArray(request, "procIndexes");
            // 从大到小删除，避免索引移位
            var sortedIndexes = indexes.Select(t => t.Value<int>()).OrderByDescending(i => i).ToList();
            EnsureAllProcsStoppedForAiStructureCommit("删除流程");

            List<Proc> draftProcesses = runtime.Stores.Processes.Items
                .Select(ObjectGraphCloner.Clone).ToList();
            Dictionary<string, DicValue> draftVariables = runtime.Stores.Values.BuildSaveData();
            var deleted = new JArray();
            var deletedProcIds = new List<Guid>();
            foreach (int procIndex in sortedIndexes)
            {
                if (procIndex < 0 || procIndex >= draftProcesses.Count)
                {
                    continue;
                }
                Proc proc = draftProcesses[procIndex];
                string procName = proc?.head?.Name ?? procIndex.ToString();
                Guid procId = proc?.head?.Id ?? Guid.Empty;
                if (procId != Guid.Empty) deletedProcIds.Add(procId);
                draftProcesses.RemoveAt(procIndex);
                deleted.Add(new JObject
                {
                    ["procIndex"] = procIndex,
                    ["procId"] = procId,
                    ["procName"] = procName
                });
            }

            int minDeleted = sortedIndexes.Min();
            int deletedPrivateVariableCount = ProcessVariableLifecycleService.RemoveOwnedVariables(
                draftVariables, deletedProcIds);
            CommitChangeSet(new AiChangeSetCompileResult
            {
                Processes = draftProcesses,
                Variables = draftVariables
            });
            // 删除后原 procIndex 节点已不存在，闪烁剩余列表中同索引位置（若有效）作为视觉提示。
            if (minDeleted < runtime.Stores.Processes.Items.Count)
            {
                NotifyProcChanged(minDeleted, ProcChangeKind.Deleted);
            }
            else if (runtime.Stores.Processes.Items.Count > 0)
            {
                NotifyProcChanged(runtime.Stores.Processes.Items.Count - 1, ProcChangeKind.Deleted);
            }

            return new JObject
            {
                ["action"] = "delete_procs",
                ["deleted"] = deleted,
                ["deletedPrivateVariableCount"] = deletedPrivateVariableCount,
                ["remainingCount"] = runtime.Stores.Processes.Items.Count,
                ["messages"] = new JArray { $"已删除 {deleted.Count} 个流程，剩余 {runtime.Stores.Processes.Items.Count} 个" }
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject ExecuteReorderProc(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int targetIndex = ReadRequiredInt(request, "targetIndex");
            if (procIndex < 0 || procIndex >= runtime.Stores.Processes.Items.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"流程索引 {procIndex} 越界。");
            }
            if (targetIndex < 0 || targetIndex >= runtime.Stores.Processes.Items.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"目标索引 {targetIndex} 越界。");
            }
            if (procIndex == targetIndex)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "源索引与目标索引相同。");
            }
            EnsureAllProcsStoppedForAiStructureCommit("调整流程顺序");

            Proc proc = runtime.Stores.Processes.Items[procIndex];
            string procName = proc?.head?.Name ?? string.Empty;
            runtime.Stores.Processes.Items.RemoveAt(procIndex);
            runtime.Stores.Processes.Items.Insert(targetIndex, proc);

            // 重建工作配置
            int minIndex = Math.Min(procIndex, targetIndex);
            if (runtime.EditorUi?.RebuildWorkConfig(minIndex) != true)
            {
                throw new BridgeRequestException(500, "SAVE_FAILED", "流程重排序失败，原流程配置已恢复。");
            }
            NotifyProcChanged(targetIndex, ProcChangeKind.Modified);

            return new JObject
            {
                ["action"] = "reorder_proc",
                ["procName"] = procName,
                ["newProcIndex"] = targetIndex,
                ["messages"] = new JArray { $"流程「{procName}」已从索引 {procIndex} 移动到 {targetIndex}" }
            };
        }

        private void EnsureAllProcsStoppedForAiStructureCommit(string actionName)
        {
            int procCount = runtime.Stores.Processes?.Items?.Count ?? 0;
            for (int procIndex = 0; procIndex < procCount; procIndex++)
            {
                EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
                if (snapshot != null && snapshot.State != ProcRunState.Stopped)
                {
                    throw new BridgeRequestException(
                        409,
                        "PROC_STRUCTURE_NOT_STOPPED",
                        $"流程 {procIndex} 当前为 {snapshot.State}，{actionName}尚未执行。",
                        new JObject
                        {
                            ["blockingProcIndex"] = procIndex,
                            ["currentState"] = snapshot.State.ToString(),
                            ["retryableNow"] = false,
                            ["retryableWhen"] = "all_processes_stopped",
                            ["sideEffects"] = "none"
                        }.ToString(Formatting.None));
                }
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject ExecuteCopyProc(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            string newName = ReadOptionalString(request, "newName");
            Proc source = GetProcByIndex(procIndex);
            EnsureAllProcsStoppedForAiStructureCommit("复制流程");

            Proc copy = ObjectGraphCloner.Clone(source);
            copy.head.Id = Guid.NewGuid();
            copy.head.Name = ResolveCopiedProcessName(source.head?.Name, newName);
            copy.head.AutoStart = false;
            if (runtime.Stores.Processes.Items.Any(proc => string.Equals(
                proc?.head?.Name, copy.head.Name, StringComparison.Ordinal)))
            {
                throw new BridgeRequestException(409, "PROC_NAME_EXISTS", $"流程名称已存在：{copy.head.Name}");
            }

            int newProcIndex = runtime.Stores.Processes.Items.Count;
            // 重置流程内步骤和指令的 Id，避免重复
            ResetProcStepOpIds(copy);

            List<Proc> draftProcesses = runtime.Stores.Processes.Items
                .Select(ObjectGraphCloner.Clone).ToList();
            Dictionary<string, DicValue> draftVariables = runtime.Stores.Values.BuildSaveData();
            ProcessVariableCopyResult variableCopy = ProcessVariableLifecycleService.CopyPrivateVariables(
                source.head.Id, copy.head.Id, copy, draftVariables);
            draftProcesses.Add(copy);

            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(
                newProcIndex, copy, errors, runtime.CreateProcessValidationContext());
            if (errors.Count > 0)
            {
                throw new BridgeRequestException(400, "PROC_VALIDATE_FAILED", "流程复制校验失败。", string.Join("\r\n", errors.Distinct()));
            }

            CommitChangeSet(new AiChangeSetCompileResult
            {
                Processes = draftProcesses,
                Variables = draftVariables
            });
            NotifyProcChanged(newProcIndex, ProcChangeKind.Added);

            JArray variableMappings = new JArray(variableCopy.Mappings.Select(mapping => new JObject
            {
                ["sourceVariableId"] = mapping.SourceVariableId,
                ["variableId"] = mapping.VariableId,
                ["sourceName"] = mapping.SourceName,
                ["name"] = mapping.Name,
                ["sourceIndex"] = mapping.SourceIndex,
                ["index"] = mapping.Index,
                ["scope"] = VariableScopeContract.Process,
                ["ownerProcId"] = copy.head.Id,
                ["ownerProcName"] = copy.head.Name
            }));

            return new JObject
            {
                ["action"] = "copy_proc",
                ["sourceProcIndex"] = procIndex,
                ["newProcIndex"] = newProcIndex,
                ["newProcId"] = copy.head.Id,
                ["newProcName"] = copy.head.Name,
                ["variableMappings"] = variableMappings,
                ["warnings"] = new JArray(variableCopy.Warnings),
                ["readiness"] = BuildProcessReadinessJObject(newProcIndex),
                ["messages"] = new JArray { $"已复制流程 {procIndex}「{source.head?.Name}」到索引 {newProcIndex}，新名称「{copy.head.Name}」" }
            };
        }

        private static void ResetProcStepOpIds(Proc proc)
        {
            if (proc?.steps == null) return;
            foreach (var step in proc.steps)
            {
                if (step == null) continue;
                step.Id = Guid.NewGuid();
                if (step.Ops == null) continue;
                foreach (var op in step.Ops)
                {
                    if (op != null)
                    {
                        op.Id = Guid.NewGuid();
                    }
                }
            }
        }

        private string ResolveCopiedProcessName(string sourceName, string requestedName)
        {
            if (!string.IsNullOrWhiteSpace(requestedName)) return requestedName.Trim();
            string basis = string.IsNullOrWhiteSpace(sourceName) ? "流程" : sourceName;
            for (int number = 1; ; number++)
            {
                string candidate = basis + (number == 1 ? "_副本" : "_副本" + number);
                if (!(runtime.Stores.Processes?.Items ?? new List<Proc>()).Any(proc =>
                    string.Equals(proc?.head?.Name, candidate, StringComparison.Ordinal)))
                {
                    return candidate;
                }
            }
        }

        private JObject BuildProcessReadinessJObject(int procIndex)
        {
            Proc proc = procIndex >= 0 && procIndex < (runtime.Stores.Processes?.Items?.Count ?? 0)
                ? runtime.Stores.Processes.Items[procIndex]
                : null;
            ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                procIndex, proc, runtime.Stores.Processes?.Items,
                runtime.CreateProcessValidationContext(), runtime.Stores.Values);
            return new JObject
            {
                ["readinessStatus"] = readiness.ReadinessStatus,
                ["runnable"] = readiness.Runnable,
                ["warnings"] = new JArray(readiness.Warnings),
                ["runBlockers"] = new JArray(readiness.RunBlockers)
            };
        }

        // 流程结构操作的预演记录，复用 previewLock 保证线程安全。
        private string RegisterManagePreview(
            JObject previewData,
            string replacePreviewId = null,
            bool supportsExplicitReplacement = false)
        {
            string previewId = Guid.NewGuid().ToString("N");
            // 自动批准模式：直接标记预演为已确认，避免 FrmAiAssistant 通过 HTTP 回调确认导致 UI 线程死锁。
            bool autoConfirmed = runtime.EditorUi?.IsAutoApproveMode == true;
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                string replacedPreviewId = null;
                if (!string.IsNullOrWhiteSpace(replacePreviewId))
                {
                    ValidatePreviewIdFormat(replacePreviewId);
                    if (!previewRecords.TryGetValue(replacePreviewId, out PreviewApprovalRecord replaced)
                        || replaced.Rejected
                        || !replaced.IsChangeSetPreview)
                    {
                        throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND",
                            $"要替换的 ChangeSet 预演不存在、已结束或已过期：{replacePreviewId}");
                    }
                    replaced.Rejected = true;
                    replacedPreviewId = replaced.PreviewId;
                    Monitor.PulseAll(previewLock);
                }
                else if (supportsExplicitReplacement)
                {
                    PreviewApprovalRecord activeChangeSet = previewRecords.Values.FirstOrDefault(item =>
                        item != null
                        && item.IsChangeSetPreview
                        && !item.Rejected
                        && item.ExpiresAtUtc > DateTime.UtcNow);
                    if (activeChangeSet != null)
                    {
                        activeChangeSet.Rejected = true;
                        replacedPreviewId = activeChangeSet.PreviewId;
                        Monitor.PulseAll(previewLock);
                    }
                }
                EnsureNoActivePreviewLocked(supportsExplicitReplacement);
                // 复用 PreviewApprovalRecord，patch 字段存 previewData
                var record = new PreviewApprovalRecord
                {
                    PreviewId = previewId,
                    Patch = previewData,
                    PatchHash = ComputePatchHash(previewData),
                    ProcIndex = -1,  // 流程结构操作不绑定单个 procIndex
                    BaseProcId = string.Empty,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
                    Confirmed = autoConfirmed,
                    IsChangeSetPreview = supportsExplicitReplacement,
                    ReplacedPreviewId = replacedPreviewId
                };
                if (autoConfirmed)
                {
                    record.ConfirmedAtUtc = DateTime.UtcNow;
                }
                previewRecords[record.PreviewId] = record;
            }
            return previewId;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void ValidateConfirmedManagePreview(string previewId)
        {
            ValidatePreviewIdFormat(previewId);
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record))
                {
                    throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                }
                if (record.Rejected)
                {
                    throw new BridgeRequestException(409, "PREVIEW_REJECTED", $"预演已结束，不能提交：{previewId}");
                }
                if (!record.Confirmed)
                {
                    throw new BridgeRequestException(
                        403,
                        "PREVIEW_NOT_CONFIRMED",
                        "预演仍在等待前台确认，本次提交未执行。",
                        new JObject
                        {
                            ["previewId"] = previewId,
                            ["state"] = "awaiting_foreground_confirmation",
                            ["retryableWhen"] = "preview_confirmed",
                            ["sideEffects"] = "none"
                        }.ToString(Formatting.None));
                }
            }
        }

        #region 路由 Handler

        // 保留的合并工具路由（op_meta / list_resources 仍通过 action 分发）。
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleOpMeta(JObject request)
        {
            string action = ReadRequiredString(request, "action");
            JObject p = request["params"] as JObject ?? new JObject();
            switch (action)
            {
                case "list_types": return HandleListOperationTypes();
                case "schema": return HandleGetOperationSchema(p);
                case "guide": return HandleGetOperationGuide(p);
                case "reference_catalog": return HandleGetReferenceCatalog(p);
                default: return BridgeError(400, "INVALID_ACTION", $"不支持的 action: {action}，可选：list_types/schema/guide/reference_catalog");
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListResources(JObject request)
        {
            string action = ReadRequiredString(request, "action");
            JObject p = request["params"] as JObject ?? new JObject();
            switch (action)
            {
                case "alarms": return HandleListAlarms(p);
                case "plc": return HandleListPlcDevices(p);
                case "cards": return HandleListCards(p);
                case "tray_points": return HandleListTrayPoints(p);
                case "communications": return HandleListCommunications(p);
                default: return BridgeError(400, "INVALID_ACTION", $"不支持的 action: {action}，可选：alarms/plc/cards/tray_points/communications");
            }
        }

        // 流程级结构操作的两阶段分发：previewId 为空走预演，非空走提交。
        // 拆开后的每个 handler 只处理一种操作，不再需要 action switch。
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleCreateOrApply(JObject request)
        {
            string previewId = ReadOptionalString(request, "previewId");
            if (string.IsNullOrEmpty(previewId))
            {
                JObject preview = PreviewCreateProc(request);
                preview["confirmed"] = runtime.EditorUi?.IsAutoApproveMode == true;
                return preview;
            }
            ValidateConfirmedManagePreview(previewId);
            JObject result = ExecuteCreateProc(request);
            RemovePreview(previewId);
            return result;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleDeleteOrApply(JObject request)
        {
            string previewId = ReadOptionalString(request, "previewId");
            if (string.IsNullOrEmpty(previewId))
            {
                JObject preview = PreviewDeleteProcs(request);
                preview["confirmed"] = runtime.EditorUi?.IsAutoApproveMode == true;
                return preview;
            }
            ValidateConfirmedManagePreview(previewId);
            JObject result = ExecuteDeleteProcs(request);
            RemovePreview(previewId);
            return result;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleReorderOrApply(JObject request)
        {
            string previewId = ReadOptionalString(request, "previewId");
            if (string.IsNullOrEmpty(previewId))
            {
                JObject preview = PreviewReorderProc(request);
                preview["confirmed"] = runtime.EditorUi?.IsAutoApproveMode == true;
                return preview;
            }
            ValidateConfirmedManagePreview(previewId);
            JObject result = ExecuteReorderProc(request);
            RemovePreview(previewId);
            return result;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleCopyOrApply(JObject request)
        {
            string previewId = ReadOptionalString(request, "previewId");
            if (string.IsNullOrEmpty(previewId))
            {
                JObject preview = PreviewCopyProc(request);
                preview["confirmed"] = runtime.EditorUi?.IsAutoApproveMode == true;
                return preview;
            }
            ValidateConfirmedManagePreview(previewId);
            JObject result = ExecuteCopyProc(request);
            RemovePreview(previewId);
            return result;
        }

        // 构造控制流程运行的请求对象：合并 procIndex 与 action 到同一层级，供 HandleControlProc 读取。
        private static JObject BuildControlRequest(JObject request, string action)
        {
            JObject p = new JObject { ["procIndex"] = request["procIndex"], ["action"] = action };
            return p;
        }

        #endregion

        #region 资源查询与操作扩展 Handler

        // ===================== 变量操作 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListVariables(JObject request)
        {
            EnsureRuntimeReady();
            ValueConfigStore store = runtime.Stores.Values;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            string typeFilter = request["type"]?.Value<string>();
            string nameLike = request["nameLike"]?.Value<string>();
            string scopeFilter = request["scope"]?.Value<string>();
            Guid? ownerProcIdFilter = ReadOptionalGuid(request, "ownerProcId");
            if (!string.IsNullOrEmpty(scopeFilter) && !VariableScopeContract.IsValid(scopeFilter))
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"scope 必须是 public、process 或 system，当前：{scopeFilter}");
            }
            int offset = request["offset"]?.Value<int>() ?? 0;
            int limit = request["limit"]?.Value<int>() ?? 100;
            if (offset < 0) offset = 0;
            if (limit < 1 || limit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "limit必须在1..100范围内。");
            }

            var items = new List<JObject>();
            int matched = 0;
            int skipped = 0;
            int taken = 0;
            foreach (DicValue val in store.GetValuesSnapshot())
            {
                string name = val?.Name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(nameLike)
                    && name.IndexOf(nameLike, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(typeFilter)
                    && !string.Equals(val.Type, typeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(scopeFilter)
                    && !string.Equals(val.Scope, scopeFilter, StringComparison.Ordinal))
                {
                    continue;
                }
                if (ownerProcIdFilter.HasValue && val.OwnerProcId != ownerProcIdFilter)
                {
                    continue;
                }
                matched++;
                if (skipped < offset)
                {
                    skipped++;
                    continue;
                }
                if (taken >= limit)
                {
                    continue;
                }
                items.Add(BuildVariableJObject(val));
                taken++;
            }
            return new JObject
            {
                ["totalMatched"] = matched,
                ["offset"] = offset,
                ["limit"] = limit,
                ["returned"] = items.Count,
                ["hasMore"] = offset + items.Count < matched,
                ["nextOffset"] = offset + items.Count < matched
                    ? (JToken)(offset + items.Count)
                    : JValue.CreateNull(),
                ["items"] = new JArray(items)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetVariable(JObject request)
        {
            EnsureRuntimeReady();
            ValueConfigStore store = runtime.Stores.Values;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            DicValue val = ResolveVariable(request, store);
            return BuildVariableJObject(val);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleSearchVariables(JObject request)
        {
            EnsureRuntimeReady();
            ValueConfigStore store = runtime.Stores.Values;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            string keyword = request["keyword"]?.Value<string>()?.Trim() ?? string.Empty;
            bool returnAll = string.IsNullOrEmpty(keyword)
                || string.Equals(keyword, "*", StringComparison.Ordinal);
            string typeFilter = request["type"]?.Value<string>();
            string valueLike = request["valueLike"]?.Value<string>();
            int limit = request["limit"]?.Value<int>() ?? 100;
            if (limit <= 0) limit = 100;

            var items = new List<JObject>();
            List<string> allNames = store.GetValueNames() ?? new List<string>();
            foreach (string name in allNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                bool nameMatched = returnAll
                    || name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!nameMatched) continue;
                if (!store.TryGetValueByName(name, out DicValue val)) continue;
                if (!string.IsNullOrEmpty(typeFilter)
                    && !string.Equals(val.Type, typeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(valueLike))
                {
                    string runtimeStr = val.Value?.ToString() ?? string.Empty;
                    if (runtimeStr.IndexOf(valueLike, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }
                items.Add(BuildVariableJObject(val));
                if (items.Count >= limit) break;
            }
            return new JObject
            {
                ["keyword"] = keyword,
                ["queryMode"] = returnAll ? "all" : "contains",
                ["type"] = typeFilter ?? string.Empty,
                ["valueLike"] = valueLike ?? string.Empty,
                ["returned"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleSetVariable(JObject request)
        {
            EnsureRuntimeReady();
            ValueConfigStore store = runtime.Stores.Values;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            DicValue val = ResolveVariable(request, store);
            string newValue = request["value"]?.Value<string>();
            if (newValue == null)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "缺少 value 字段。");
            }
            if (string.Equals(val.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(newValue, out double dval))
                {
                    return BridgeError(400, "INVALID_ARGUMENT", $"变量[{val.Name}] 是 double 类型，value 不是有效数字：{newValue}");
                }
                store.setValueByIndex(val.Index, dval, "EW-AI运行值设置");
            }
            else
            {
                store.setValueByIndex(val.Index, newValue, "EW-AI运行值设置");
            }
            // 重新读取以返回最新值
            store.TryGetValueByIndex(val.Index, out DicValue updated);
            return new JObject
            {
                ["ok"] = true,
                ["variable"] = BuildVariableJObject(updated ?? val),
                ["message"] = $"变量[{val.Name}] 当前值已更新为 {newValue}。"
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleDeleteVariable(JObject request)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("删除变量");
            ValueConfigStore store = runtime.Stores.Values;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            DicValue target = ResolveVariable(request, store);
            if (ValueConfigStore.IsSystemValueIndex(target.Index))
            {
                return BridgeError(
                    409,
                    "SYSTEM_VARIABLE_CONFIG_READ_ONLY",
                    $"系统变量区配置对 AI 只读：{target.Name}，index={target.Index}。");
            }
            Dictionary<string, DicValue> draft = store.BuildSaveData();
            if (!draft.Remove(target.Name))
            {
                return BridgeError(404, "VARIABLE_NOT_FOUND", $"未找到变量：{target.Name}");
            }
            if (!store.TryCommitConfiguration(runtime.Paths.ConfigPath, draft, out string commitError))
            {
                return BridgeError(500, "VARIABLE_COMMIT_FAILED", commitError);
            }
            runtime.EditorUi?.RefreshVariables();
            return new JObject
            {
                ["ok"] = true,
                ["deleted"] = BuildVariableJObject(target),
                ["message"] = $"变量[{target.Name}]已删除，原槽位 index={target.Index} 保持为空。"
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleAddVariable(JObject request)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("新增变量");
            ValueConfigStore store = runtime.Stores.Values;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            string name = request["name"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return BridgeError(400, "INVALID_ARGUMENT", "缺少 name 字段或 name 为空。");
            }
            if (ValueConfigStore.IsProtectedValueName(name))
            {
                return BridgeError(
                    409,
                    "SYSTEM_VARIABLE_CONFIG_READ_ONLY",
                    $"系统变量区配置对 AI 只读：{name}。");
            }
            string type = (request["type"]?.Value<string>() ?? "double").ToLowerInvariant();
            if (!string.Equals(type, "double", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "string", StringComparison.OrdinalIgnoreCase))
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"type 只能是 double 或 string，当前：{type}");
            }
            string value = request["value"]?.Value<string>();
            if (value == null)
            {
                value = string.Equals(type, "double", StringComparison.OrdinalIgnoreCase) ? "0" : "";
            }
            if (string.Equals(type, "double", StringComparison.OrdinalIgnoreCase) && !double.TryParse(value, out _))
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"type=double 时 value 必须是有效数字，当前：{value}");
            }
            string note = request["note"]?.Value<string>() ?? string.Empty;
            int? requestedIndex = request["index"]?.Value<int>();
            string scope = request["scope"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(scope) || !VariableScopeContract.IsValid(scope))
            {
                return BridgeError(400, "INVALID_ARGUMENT", "scope 为必填项，且必须是 public 或 process。");
            }
            if (string.Equals(scope, VariableScopeContract.System, StringComparison.Ordinal))
            {
                return BridgeError(409, "SYSTEM_VARIABLE_CONFIG_READ_ONLY", "系统变量区配置对 AI 只读。");
            }
            Guid? ownerProcId = ReadOptionalGuid(request, "ownerProcId");
            if (string.Equals(scope, VariableScopeContract.Process, StringComparison.Ordinal))
            {
                if (!ownerProcId.HasValue || !ProcessExists(ownerProcId.Value))
                {
                    return BridgeError(400, "INVALID_ARGUMENT", "scope=process 时 ownerProcId 必填且必须指向现有流程。");
                }
            }
            else if (ownerProcId.HasValue)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "scope=public 时不能携带 ownerProcId。");
            }

            // 名称查重
            if (store.TryGetValueByName(name, out DicValue existing))
            {
                return BridgeError(400, "DUPLICATE_NAME", $"变量名 [{name}] 已存在（index={existing.Index}）。");
            }

            int targetIndex;
            if (requestedIndex.HasValue)
            {
                targetIndex = requestedIndex.Value;
                if (targetIndex < 0 || targetIndex >= ValueConfigStore.NormalValueCapacity)
                {
                    return BridgeError(
                        400,
                        "INVALID_ARGUMENT",
                        $"add_variable 的 index 必须位于普通变量区 [0, {ValueConfigStore.NormalValueCapacity})。");
                }
                if (store.TryGetValueByIndex(targetIndex, out DicValue occupied))
                {
                    return BridgeError(400, "SLOT_OCCUPIED", $"index={targetIndex} 已被变量 [{occupied.Name}] 占用。");
                }
            }
            else
            {
                targetIndex = -1;
                for (int i = 0; i < ValueConfigStore.NormalValueCapacity; i++)
                {
                    if (!store.TryGetValueByIndex(i, out _))
                    {
                        targetIndex = i;
                        break;
                    }
                }
                if (targetIndex < 0)
                {
                    return BridgeError(
                        500,
                        "STORE_FULL",
                        $"普通变量区已满（{ValueConfigStore.NormalValueCapacity} 个槽位均被占用）。");
                }
            }

            Dictionary<string, DicValue> draft = store.BuildSaveData();
            draft[name] = new DicValue
            {
                Id = Guid.NewGuid(),
                Index = targetIndex,
                Name = name,
                Type = type,
                Scope = scope,
                OwnerProcId = ownerProcId,
                Value = value,
                Note = note
            };
            if (!store.TryCommitConfiguration(runtime.Paths.ConfigPath, draft, out string commitError))
            {
                return BridgeError(500, "VARIABLE_COMMIT_FAILED", commitError);
            }
            runtime.EditorUi?.RefreshVariables();
            store.TryGetValueByIndex(targetIndex, out DicValue created);
            return new JObject
            {
                ["ok"] = true,
                ["variable"] = BuildVariableJObject(created),
                ["message"] = $"变量 [{name}] 已创建于 index={targetIndex}。"
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleUpdateVariable(JObject request)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("修改变量配置");
            ValueConfigStore store = runtime.Stores.Values;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            DicValue target = ResolveVariable(request, store);
            if (ValueConfigStore.IsSystemValueIndex(target.Index))
            {
                return BridgeError(
                    409,
                    "SYSTEM_VARIABLE_CONFIG_READ_ONLY",
                    $"系统变量区配置对 AI 只读：{target.Name}，index={target.Index}。");
            }

            bool hasNewName = request.Property("newName", StringComparison.Ordinal) != null;
            bool hasType = request.Property("type", StringComparison.Ordinal) != null;
            bool hasValue = request.Property("value", StringComparison.Ordinal) != null;
            bool hasNote = request.Property("note", StringComparison.Ordinal) != null;
            bool hasScope = request.Property("scope", StringComparison.Ordinal) != null;
            bool hasOwnerProcId = request.Property("ownerProcId", StringComparison.Ordinal) != null;
            bool hasIndex = request.Property("index", StringComparison.Ordinal) != null;
            if (!hasNewName && !hasType && !hasValue && !hasNote
                && !hasScope && !hasOwnerProcId && !hasIndex)
            {
                return BridgeError(
                    400,
                    "INVALID_ARGUMENT",
                    "至少提供 newName、type、value、note、scope、ownerProcId 或 index 之一。");
            }

            string newName = hasNewName ? request["newName"]?.Value<string>()?.Trim() : target.Name;
            if (string.IsNullOrWhiteSpace(newName))
            {
                return BridgeError(400, "INVALID_ARGUMENT", "newName 不能为空。");
            }
            string type = hasType ? request["type"]?.Value<string>() : target.Type;
            if (!string.Equals(type, "double", StringComparison.Ordinal)
                && !string.Equals(type, "string", StringComparison.Ordinal))
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"type 只能是 double 或 string，当前：{type}");
            }
            string value = hasValue
                ? request["value"]?.Value<string>()
                : target.Value;
            if (value == null)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "value 不能为 null。");
            }
            if (string.Equals(type, "double", StringComparison.Ordinal) && !double.TryParse(value, out _))
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"type=double 时 value 必须是有效数字，当前：{value}");
            }
            string note = hasNote ? request["note"]?.Value<string>() ?? string.Empty : target.Note ?? string.Empty;
            string scope = hasScope ? request["scope"]?.Value<string>() : target.Scope;
            if (!VariableScopeContract.IsValid(scope)
                || string.Equals(scope, VariableScopeContract.System, StringComparison.Ordinal))
            {
                return BridgeError(400, "INVALID_ARGUMENT", "scope 只能修改为 public 或 process。");
            }
            Guid? ownerProcId = hasOwnerProcId
                ? ReadOptionalGuid(request, "ownerProcId")
                : target.OwnerProcId;
            if (string.Equals(scope, VariableScopeContract.Process, StringComparison.Ordinal))
            {
                if (!ownerProcId.HasValue || !ProcessExists(ownerProcId.Value))
                {
                    return BridgeError(400, "INVALID_ARGUMENT", "scope=process 时 ownerProcId 必填且必须指向现有流程。");
                }
            }
            else
            {
                if (hasOwnerProcId && ownerProcId.HasValue)
                {
                    return BridgeError(400, "INVALID_ARGUMENT", "scope=public 时不能携带 ownerProcId。");
                }
                ownerProcId = null;
            }
            int index = hasIndex ? request["index"].Value<int>() : target.Index;
            if (index < 0 || index >= ValueConfigStore.NormalValueCapacity)
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"index 必须位于普通变量区 [0, {ValueConfigStore.NormalValueCapacity})。");
            }
            if (index != target.Index && store.TryGetValueByIndex(index, out DicValue indexOwner))
            {
                return BridgeError(409, "SLOT_OCCUPIED", $"index={index} 已被变量[{indexOwner.Name}]占用。");
            }

            Dictionary<string, DicValue> draft = store.BuildSaveData();
            if (!string.Equals(newName, target.Name, StringComparison.Ordinal)
                && draft.ContainsKey(newName))
            {
                return BridgeError(409, "DUPLICATE_NAME", $"变量名已存在：{newName}");
            }
            draft.Remove(target.Name);
            DicValue updated = ObjectGraphCloner.Clone(target);
            updated.Name = newName;
            updated.Type = type;
            updated.Scope = scope;
            updated.OwnerProcId = ownerProcId;
            updated.Index = index;
            updated.Value = value;
            updated.Note = note;
            draft[newName] = updated;
            IReadOnlyDictionary<string, string> valueOverrides = hasValue
                ? new Dictionary<string, string>(StringComparer.Ordinal) { [newName] = value }
                : null;
            if (!store.TryCommitConfiguration(
                runtime.Paths.ConfigPath,
                draft,
                out string commitError,
                valueOverrides,
                "AI变量配置更新"))
            {
                return BridgeError(500, "VARIABLE_COMMIT_FAILED", commitError);
            }
            runtime.EditorUi?.RefreshVariables();
            store.TryGetValueByName(newName, out DicValue committed);
            return new JObject
            {
                ["ok"] = true,
                ["variable"] = BuildVariableJObject(committed),
                ["message"] = hasValue
                    ? $"变量[{target.Name}]当前值和属性已更新。"
                    : $"变量[{target.Name}]属性已更新，当前值保持不变。"
            };
        }

        // ===================== 工站/点位操作 =====================

        private const int DataStationPointCapacity = 400;

        [System.Diagnostics.DebuggerNonUserCode]
        private DataStation ResolveStation(int stationIndex)
        {
            if (runtime.Stores.Stations?.Items == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "工站存储未初始化。");
            }
            List<DataStation> list = runtime.Stores.Stations.Items;
            if (stationIndex < 0 || stationIndex >= list.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"stationIndex 超出范围 [0, {list.Count})。");
            }
            DataStation station = list[stationIndex];
            if (station == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", $"工站 stationIndex={stationIndex} 为空。");
            }
            return station;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static DataPos ResolvePoint(DataStation station, int index)
        {
            if (station.ListDataPos == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "工站点位列表未初始化。");
            }
            if (index < 0 || index >= DataStationPointCapacity)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"点位 index 超出范围 [0, {DataStationPointCapacity})。");
            }
            // 旧数据可能未填满 400 个槽位，按实际容量防御
            if (index >= station.ListDataPos.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"点位 index 超出实际槽位范围 [0, {station.ListDataPos.Count})。");
            }
            DataPos pos = station.ListDataPos[index];
            if (pos == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", $"点位 index={index} 为空。");
            }
            return pos;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private AlarmInfo ResolveAlarm(int index)
        {
            AlarmInfoStore store = runtime.Stores.Alarms;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "报警存储未初始化。");
            }
            if (index < 0 || index >= AlarmInfoStore.AlarmCapacity)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"index 超出范围 [0, {AlarmInfoStore.AlarmCapacity})。");
            }
            if (!store.TryGetByIndex(index, out AlarmInfo alarm) || alarm == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", $"报警 index={index} 为空。");
            }
            return alarm;
        }

        private static JObject BuildPointJObject(DataPos pos)
        {
            if (pos == null) return new JObject();
            return new JObject
            {
                ["index"] = pos.Index,
                ["name"] = pos.Name ?? string.Empty,
                ["x"] = pos.X,
                ["y"] = pos.Y,
                ["z"] = pos.Z,
                ["u"] = pos.U,
                ["v"] = pos.V,
                ["w"] = pos.W
            };
        }

        private void SaveStationAndRefresh()
        {
            StationDefinitionStore store = runtime.Stores.Stations;
            if (!store.TryCommit(runtime.Paths.ConfigPath, store.Items, out string error))
            {
                if (!store.Load(runtime.Paths.ConfigPath, out string restoreError))
                {
                    runtime.Safety.Lock($"{error}；正式内存恢复失败：{restoreError}");
                }
                throw new BridgeRequestException(500, "STATION_COMMIT_FAILED", error);
            }
            runtime.EditorUi?.RefreshStations();
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListStations(JObject request)
        {
            EnsureRuntimeReady();
            if (runtime.Stores.Stations?.Items == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "工站存储未初始化。");
            }
            JArray array = new JArray();
            List<DataStation> list = runtime.Stores.Stations.Items;
            for (int i = 0; i < list.Count; i++)
            {
                DataStation station = list[i];
                if (station == null) continue;
                int namedCount = 0;
                if (station.dicDataPos != null)
                {
                    foreach (KeyValuePair<string, DataPos> kv in station.dicDataPos)
                    {
                        if (kv.Value != null && !string.IsNullOrEmpty(kv.Value.Name)) namedCount++;
                    }
                }
                array.Add(new JObject
                {
                    ["stationIndex"] = i,
                    ["name"] = station.Name ?? string.Empty,
                    ["coordinateSystem"] = station.CoordinateSystem,
                    ["manualSpeedPercent"] = station.ManualSpeedPercent,
                    ["pointCount"] = namedCount
                });
            }
            return new JObject
            {
                ["total"] = array.Count,
                ["items"] = array
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetStation(JObject request)
        {
            EnsureRuntimeReady();
            int stationIndex = ReadRequiredInt(request, "stationIndex");
            DataStation station = ResolveStation(stationIndex);
            JArray points = new JArray();
            if (station.dicDataPos != null)
            {
                foreach (KeyValuePair<string, DataPos> kv in station.dicDataPos)
                {
                    if (kv.Value == null) continue;
                    points.Add(BuildPointJObject(kv.Value));
                }
            }
            return new JObject
            {
                ["stationIndex"] = stationIndex,
                ["name"] = station.Name ?? string.Empty,
                ["coordinateSystem"] = station.CoordinateSystem,
                ["manualSpeedPercent"] = station.ManualSpeedPercent,
                ["points"] = points
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleAddStation(JObject request)
        {
            EnsureRuntimeReady();
            if (runtime.Stores.Stations?.Items == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "工站存储未初始化。");
            }
            string name = request["name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                return BridgeError(400, "INVALID_ARGUMENT", "缺少 name 字段或 name 为空。");
            }
            double? manualSpeedPercent = request["manualSpeedPercent"]?.Value<double>();
            int coordinateSystem = request["coordinateSystem"]?.Value<int>() ?? 0;
            if (coordinateSystem < 0 || coordinateSystem > 1)
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"坐标系无效:{coordinateSystem}。");
            }
            List<DataStation> list = runtime.Stores.Stations.Items;
            foreach (DataStation s in list)
            {
                if (s != null && string.Equals(s.Name, name, StringComparison.Ordinal))
                {
                    return BridgeError(400, "DUPLICATE_NAME", $"工站名 [{name}] 已存在。");
                }
            }
            DataStation station = new DataStation(false)
            {
                Name = name,
                CoordinateSystem = (ushort)coordinateSystem
            };
            if (manualSpeedPercent.HasValue) station.ManualSpeedPercent = manualSpeedPercent.Value;
            list.Add(station);
            SaveStationAndRefresh();
            int newIndex = list.Count - 1;
            return new JObject
            {
                ["ok"] = true,
                ["stationIndex"] = newIndex,
                ["name"] = station.Name,
                ["message"] = $"工站 [{name}] 已创建于 stationIndex={newIndex}。"
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleDeleteStation(JObject request)
        {
            EnsureRuntimeReady();
            int stationIndex = ReadRequiredInt(request, "stationIndex");
            // 先校验范围与存在性
            ResolveStation(stationIndex);
            List<DataStation> list = runtime.Stores.Stations.Items;
            string name = list[stationIndex]?.Name ?? string.Empty;
            list.RemoveAt(stationIndex);
            SaveStationAndRefresh();
            return new JObject
            {
                ["ok"] = true,
                ["stationIndex"] = stationIndex,
                ["name"] = name,
                ["message"] = $"工站 [{name}] (stationIndex={stationIndex}) 已删除，后续工站索引前移。"
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleUpdateStation(JObject request)
        {
            EnsureRuntimeReady();
            int stationIndex = ReadRequiredInt(request, "stationIndex");
            DataStation station = ResolveStation(stationIndex);
            string name = request["name"]?.Value<string>();
            double? manualSpeedPercent = request["manualSpeedPercent"]?.Value<double>();
            int? coordinateSystem = request["coordinateSystem"]?.Value<int>();
            if (coordinateSystem.HasValue && (coordinateSystem.Value < 0 || coordinateSystem.Value > 1))
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"坐标系无效:{coordinateSystem.Value}。");
            }
            bool changed = false;
            if (!string.IsNullOrWhiteSpace(name))
            {
                List<DataStation> list = runtime.Stores.Stations.Items;
                for (int i = 0; i < list.Count; i++)
                {
                    if (i == stationIndex) continue;
                    DataStation s = list[i];
                    if (s != null && string.Equals(s.Name, name, StringComparison.Ordinal))
                    {
                        return BridgeError(400, "DUPLICATE_NAME", $"工站名 [{name}] 已存在。");
                    }
                }
                station.Name = name;
                changed = true;
            }
            if (manualSpeedPercent.HasValue)
            {
                station.ManualSpeedPercent = manualSpeedPercent.Value;
                changed = true;
            }
            if (coordinateSystem.HasValue)
            {
                station.CoordinateSystem = (ushort)coordinateSystem.Value;
                changed = true;
            }
            if (!changed)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "至少提供 name、manualSpeedPercent 或 coordinateSystem 之一。");
            }
            SaveStationAndRefresh();
            return new JObject
            {
                ["ok"] = true,
                ["station"] = new JObject
                {
                    ["stationIndex"] = stationIndex,
                    ["name"] = station.Name ?? string.Empty,
                    ["manualSpeedPercent"] = station.ManualSpeedPercent
                },
                ["message"] = $"工站 stationIndex={stationIndex} 已更新。"
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListPoints(JObject request)
        {
            EnsureRuntimeReady();
            int stationIndex = ReadRequiredInt(request, "stationIndex");
            DataStation station = ResolveStation(stationIndex);
            JArray array = new JArray();
            if (station.dicDataPos != null)
            {
                foreach (KeyValuePair<string, DataPos> kv in station.dicDataPos)
                {
                    if (kv.Value == null) continue;
                    array.Add(BuildPointJObject(kv.Value));
                }
            }
            return new JObject
            {
                ["stationIndex"] = stationIndex,
                ["stationName"] = station.Name ?? string.Empty,
                ["total"] = array.Count,
                ["items"] = array
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetPoint(JObject request)
        {
            EnsureRuntimeReady();
            int stationIndex = ReadRequiredInt(request, "stationIndex");
            int index = ReadRequiredInt(request, "index");
            DataStation station = ResolveStation(stationIndex);
            DataPos pos = ResolvePoint(station, index);
            return new JObject
            {
                ["stationIndex"] = stationIndex,
                ["point"] = BuildPointJObject(pos)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleSetPoint(JObject request)
        {
            EnsureRuntimeReady();
            int stationIndex = ReadRequiredInt(request, "stationIndex");
            int index = ReadRequiredInt(request, "index");
            DataStation station = ResolveStation(stationIndex);
            DataPos pos = ResolvePoint(station, index);

            string name = request["name"]?.Value<string>();
            double? x = request["x"]?.Value<double>();
            double? y = request["y"]?.Value<double>();
            double? z = request["z"]?.Value<double>();
            double? u = request["u"]?.Value<double>();
            double? v = request["v"]?.Value<double>();
            double? w = request["w"]?.Value<double>();

            bool changed = false;
            if (!string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, pos.Name ?? string.Empty, StringComparison.Ordinal))
            {
                // 工站内点位名唯一校验（排除自身）
                if (station.dicDataPos != null)
                {
                    foreach (KeyValuePair<string, DataPos> kv in station.dicDataPos)
                    {
                        if (kv.Value != null && kv.Value != pos
                            && string.Equals(kv.Value.Name, name, StringComparison.Ordinal))
                        {
                            return BridgeError(400, "DUPLICATE_NAME", $"点位名 [{name}] 在工站内已存在。");
                        }
                    }
                }
                // 同步字典：删除旧 key（若旧名非空），再添加新 key
                string oldName = pos.Name ?? string.Empty;
                if (station.dicDataPos != null && !string.IsNullOrEmpty(oldName))
                {
                    station.dicDataPos.Remove(oldName);
                }
                pos.Name = name;
                if (station.dicDataPos != null)
                {
                    station.dicDataPos[name] = pos;
                }
                changed = true;
            }
            if (x.HasValue) { pos.X = x.Value; changed = true; }
            if (y.HasValue) { pos.Y = y.Value; changed = true; }
            if (z.HasValue) { pos.Z = z.Value; changed = true; }
            if (u.HasValue) { pos.U = u.Value; changed = true; }
            if (v.HasValue) { pos.V = v.Value; changed = true; }
            if (w.HasValue) { pos.W = w.Value; changed = true; }
            if (!changed)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "至少提供一个可修改字段（name/x/y/z/u/v/w）。");
            }
            SaveStationAndRefresh();
            return new JObject
            {
                ["ok"] = true,
                ["point"] = BuildPointJObject(pos),
                ["message"] = $"工站 stationIndex={stationIndex} 的点位 index={index} 已更新。"
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleDeletePoint(JObject request)
        {
            EnsureRuntimeReady();
            int stationIndex = ReadRequiredInt(request, "stationIndex");
            int index = ReadRequiredInt(request, "index");
            DataStation station = ResolveStation(stationIndex);
            DataPos pos = ResolvePoint(station, index);

            // 判断点位是否已经为空（名称为空且坐标全零）
            bool alreadyEmpty = string.IsNullOrEmpty(pos.Name)
                && pos.X == 0 && pos.Y == 0 && pos.Z == 0
                && pos.U == 0 && pos.V == 0 && pos.W == 0;
            if (alreadyEmpty)
            {
                return BridgeError(404, "POINT_NOT_FOUND", $"工站 stationIndex={stationIndex} 的点位 index={index} 本身为空，无需删除。");
            }

            string oldName = pos.Name ?? string.Empty;
            // 同步字典：移除旧名称
            if (station.dicDataPos != null && !string.IsNullOrEmpty(oldName))
            {
                station.dicDataPos.Remove(oldName);
            }
            // 清空点位数据（Index 保持不变，固定槽位）
            pos.Name = null;
            pos.X = 0;
            pos.Y = 0;
            pos.Z = 0;
            pos.U = 0;
            pos.V = 0;
            pos.W = 0;

            SaveStationAndRefresh();
            return new JObject
            {
                ["ok"] = true,
                ["stationIndex"] = stationIndex,
                ["index"] = index,
                ["message"] = $"工站 stationIndex={stationIndex} 的点位 index={index}「{oldName}」已清空。"
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static DicValue ResolveVariable(JObject request, ValueConfigStore store)
        {
            string name = request["name"]?.Value<string>();
            int? index = request["index"]?.Value<int>();
            if (string.IsNullOrWhiteSpace(name) && !index.HasValue)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "必须提供 name 或 index 之一。");
            }
            DicValue val = null;
            if (index.HasValue)
            {
                if (index.Value < 0 || index.Value >= ValueConfigStore.ValueCapacity)
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"index 超出范围 [0, {ValueConfigStore.ValueCapacity})。");
                }
                if (!store.TryGetValueByIndex(index.Value, out val) || val == null || string.IsNullOrEmpty(val.Name))
                {
                    throw new BridgeRequestException(404, "VARIABLE_NOT_FOUND", $"未找到 index={index.Value} 的有效变量。");
                }
            }
            else
            {
                if (!store.TryGetValueByName(name, out val) || val == null)
                {
                    throw new BridgeRequestException(404, "VARIABLE_NOT_FOUND", $"未找到 name={name} 的变量。");
                }
            }
            return val;
        }

        private JObject BuildVariableJObject(DicValue val)
        {
            if (val == null) return new JObject();
            return new JObject
            {
                ["variableId"] = val.Id,
                ["index"] = val.Index,
                ["name"] = val.Name ?? string.Empty,
                ["type"] = val.Type ?? string.Empty,
                ["scope"] = val.Scope ?? string.Empty,
                ["ownerProcId"] = val.OwnerProcId.HasValue ? JToken.FromObject(val.OwnerProcId.Value) : JValue.CreateNull(),
                ["ownerProcName"] = ResolveProcessName(val.OwnerProcId),
                ["value"] = val.Value ?? string.Empty,
                ["note"] = val.Note ?? string.Empty,
                ["isMark"] = val.isMark,
                ["lastChangedAt"] = val.LastChangedAt == default(DateTime) ? string.Empty : val.LastChangedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ["lastChangedBy"] = val.LastChangedBy ?? string.Empty,
                ["oldValue"] = val.LastChangedOldValue ?? string.Empty,
                ["newValue"] = val.LastChangedNewValue ?? string.Empty,
                ["referenceImpact"] = BuildVariableReferenceImpact(val)
            };
        }

        private bool ProcessExists(Guid procId)
        {
            return procId != Guid.Empty && (runtime.Stores.Processes?.Items ?? new List<Proc>())
                .Any(proc => proc?.head?.Id == procId);
        }

        private string ResolveProcessName(Guid? procId)
        {
            if (!procId.HasValue) return string.Empty;
            return (runtime.Stores.Processes?.Items ?? new List<Proc>())
                .FirstOrDefault(proc => proc?.head?.Id == procId.Value)?.head?.Name ?? string.Empty;
        }

        private JObject BuildVariableReferenceImpact(DicValue variable)
        {
            int nameReferences = 0;
            int indexReferences = 0;
            int inaccessibleReferences = 0;
            foreach (Proc proc in runtime.Stores.Processes?.Items ?? new List<Proc>())
            {
                Guid procId = proc?.head?.Id ?? Guid.Empty;
                foreach (OperationType operation in (proc?.steps ?? new List<Step>())
                    .Where(step => step?.Ops != null)
                    .SelectMany(step => step.Ops)
                    .Where(operation => operation != null))
                {
                    foreach (VariableReferenceRecord reference in VariableReferenceCatalog.Enumerate(operation))
                    {
                        bool matched = reference.Kind == VariableReferenceKind.Name
                            ? string.Equals(reference.Value, variable.Name, StringComparison.Ordinal)
                            : int.TryParse(reference.Value, out int index) && index == variable.Index;
                        if (!matched) continue;
                        if (reference.Kind == VariableReferenceKind.Name) nameReferences++;
                        else indexReferences++;
                        if (!ValueConfigStore.CanProcessAccess(variable, procId)) inaccessibleReferences++;
                    }
                }
            }
            return new JObject
            {
                ["total"] = nameReferences + indexReferences,
                ["nameReferences"] = nameReferences,
                ["indexReferences"] = indexReferences,
                ["inaccessibleReferences"] = inaccessibleReferences
            };
        }

        // ===================== 数据结构操作 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListDataStructs(JObject request)
        {
            EnsureRuntimeReady();
            DataStructStore store = runtime.Stores.DataStructures;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "数据结构存储未初始化。");
            }
            List<string> names = store.GetStructNames() ?? new List<string>();
            var items = new List<JObject>();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                int count = 0;
                if (store.TryGetStructIndexByName(name, out int sidx))
                {
                    count = store.GetItemCount(sidx);
                }
                items.Add(new JObject
                {
                    ["name"] = name,
                    ["itemCount"] = count
                });
            }
            return new JObject
            {
                ["total"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetDataStruct(JObject request)
        {
            EnsureRuntimeReady();
            DataStructStore store = runtime.Stores.DataStructures;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "数据结构存储未初始化。");
            }
            string name = ReadRequiredString(request, "name");
            if (!store.TryGetStructSnapshotByName(name, out DataStruct ds))
            {
                return BridgeError(404, "DATA_STRUCT_NOT_FOUND", $"未找到数据结构：{name}");
            }
            return BuildDataStructJObject(name, ds);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleSearchDataStructs(JObject request)
        {
            EnsureRuntimeReady();
            DataStructStore store = runtime.Stores.DataStructures;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "数据结构存储未初始化。");
            }
            string structName = ReadRequiredString(request, "name");
            string itemNameLike = request["itemNameLike"]?.Value<string>();
            string strValueLike = request["strValueLike"]?.Value<string>();
            double? numValueMin = request["numValueMin"]?.Value<double>();
            double? numValueMax = request["numValueMax"]?.Value<double>();
            int limit = request["limit"]?.Value<int>() ?? 100;
            if (limit <= 0) limit = 100;

            if (!store.TryGetStructSnapshotByName(structName, out DataStruct ds))
            {
                return BridgeError(404, "DATA_STRUCT_NOT_FOUND", $"未找到数据结构：{structName}");
            }
            var items = new List<JObject>();
            for (int i = 0; i < ds.dataStructItems.Count; i++)
            {
                DataStructItem item = ds.dataStructItems[i];
                if (item == null) continue;
                if (!string.IsNullOrEmpty(itemNameLike)
                    && (item.Name ?? string.Empty).IndexOf(itemNameLike, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                // 字段值过滤
                bool hasFilter = !string.IsNullOrEmpty(strValueLike) || numValueMin.HasValue || numValueMax.HasValue;
                if (hasFilter)
                {
                    bool fieldMatched = false;
                    if (item.str != null)
                    {
                        foreach (var kv in item.str)
                        {
                            if (!string.IsNullOrEmpty(strValueLike)
                                && (kv.Value ?? string.Empty).IndexOf(strValueLike, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                fieldMatched = true;
                                break;
                            }
                        }
                    }
                    if (!fieldMatched && item.num != null)
                    {
                        foreach (var kv in item.num)
                        {
                            if (numValueMin.HasValue && kv.Value >= numValueMin.Value)
                            {
                                fieldMatched = true;
                                break;
                            }
                            if (numValueMax.HasValue && kv.Value <= numValueMax.Value)
                            {
                                fieldMatched = true;
                                break;
                            }
                        }
                    }
                    if (!fieldMatched) continue;
                }
                items.Add(BuildDataStructItemJObject(i, item));
                if (items.Count >= limit) break;
            }
            return new JObject
            {
                ["name"] = structName,
                ["returned"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleSetDataStructField(JObject request)
        {
            EnsureRuntimeReady();
            DataStructStore store = runtime.Stores.DataStructures;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "数据结构存储未初始化。");
            }
            string name = ReadRequiredString(request, "name");
            int itemIndex = ReadRequiredInt(request, "itemIndex");
            int fieldIndex = ReadRequiredInt(request, "fieldIndex");
            string value = request["value"]?.Value<string>();
            if (value == null)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "缺少 value 字段。");
            }
            if (!store.TryGetStructIndexByName(name, out int structIndex))
            {
                return BridgeError(404, "DATA_STRUCT_NOT_FOUND", $"未找到数据结构：{name}");
            }
            // 先读取现有字段类型
            if (!store.TryGetStructSnapshotByName(name, out DataStruct ds))
            {
                return BridgeError(500, "DATA_STRUCT_ERROR", $"读取数据结构失败：{name}");
            }
            if (itemIndex < 0 || itemIndex >= ds.dataStructItems.Count)
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"itemIndex 超出范围 [0, {ds.dataStructItems.Count})。");
            }
            DataStructItem itemSnap = ds.dataStructItems[itemIndex];
            if (itemSnap == null || itemSnap.FieldTypes == null || !itemSnap.FieldTypes.TryGetValue(fieldIndex, out DataStructValueType fieldType))
            {
                return BridgeError(400, "INVALID_ARGUMENT", $"fieldIndex {fieldIndex} 不存在。");
            }
            string fieldTypeStr = fieldType == DataStructValueType.Number ? "Number" : "Text";
            if (fieldType == DataStructValueType.Number)
            {
                if (!double.TryParse(value, out _))
                {
                    return BridgeError(400, "INVALID_ARGUMENT", $"字段是 Number 类型，value 不是有效数字：{value}");
                }
            }
            if (!store.SetFieldValue(structIndex, itemIndex, fieldIndex, fieldType, value, out string error))
            {
                return BridgeError(400, "SET_FIELD_FAILED", $"修改字段失败：{error}");
            }
            // 重新读取以返回最新值
            store.TryGetStructSnapshotByName(name, out DataStruct updated);
            DataStructItem updatedItem = updated.dataStructItems[itemIndex];
            return new JObject
            {
                ["ok"] = true,
                ["name"] = name,
                ["itemIndex"] = itemIndex,
                ["fieldIndex"] = fieldIndex,
                ["fieldName"] = updatedItem.FieldNames.TryGetValue(fieldIndex, out string fn) ? fn : string.Empty,
                ["fieldType"] = fieldTypeStr,
                ["item"] = BuildDataStructItemJObject(itemIndex, updatedItem),
                ["message"] = $"数据结构[{name}] item[{itemIndex}] 字段[{fieldIndex}] 已更新为 {value}。"
            };
        }

        private JObject HandleUpsertDataStruct(JObject request)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("保存数据结构");
            DataStructDefinition definition = ReadRequiredObject(request, "definition")
                .ToObject<DataStructDefinition>();
            DataStruct candidate = BuildDataStructCandidate(definition);
            if (!runtime.Stores.DataStructures.TryUpsertAndSave(
                candidate, runtime.Paths.ConfigPath, out bool created, out string error))
            {
                throw new BridgeRequestException(400, "DATA_STRUCT_SAVE_FAILED", error);
            }
            runtime.EditorUi?.RefreshDataStructures();
            return new JObject
            {
                ["name"] = candidate.Name,
                ["created"] = created,
                ["itemCount"] = candidate.dataStructItems?.Count ?? 0,
                ["configurationSaved"] = true
            };
        }

        private JObject HandleDeleteDataStruct(JObject request)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("删除数据结构");
            string name = ReadRequiredString(request, "name");
            if (!runtime.Stores.DataStructures.TryDeleteAndSave(name, runtime.Paths.ConfigPath, out string error))
            {
                throw new BridgeRequestException(400, "DATA_STRUCT_DELETE_FAILED", error);
            }
            runtime.EditorUi?.RefreshDataStructures();
            return new JObject
            {
                ["name"] = name,
                ["deleted"] = true,
                ["configurationSaved"] = true
            };
        }

        private static DataStruct BuildDataStructCandidate(DataStructDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "数据结构名称不能为空。");
            }
            var result = new DataStruct { Name = definition.Name.Trim() };
            var itemNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataStructItemDefinition itemDefinition
                in definition.Items ?? new List<DataStructItemDefinition>())
            {
                if (itemDefinition == null || string.IsNullOrWhiteSpace(itemDefinition.Name)
                    || !itemNames.Add(itemDefinition.Name.Trim()))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", "数据项名称为空或重复。");
                }
                var item = new DataStructItem { Name = itemDefinition.Name.Trim() };
                var indexes = new HashSet<int>();
                foreach (DataStructFieldDefinition field
                    in itemDefinition.Fields ?? new List<DataStructFieldDefinition>())
                {
                    if (field == null || field.Index < 0 || string.IsNullOrWhiteSpace(field.Name)
                        || !indexes.Add(field.Index))
                    {
                        throw new BridgeRequestException(400, "INVALID_ARGUMENT",
                            $"数据项 {item.Name} 的字段索引或名称无效。");
                    }
                    DataStructValueType type;
                    if (string.Equals(field.Type, "Text", StringComparison.OrdinalIgnoreCase))
                    {
                        type = DataStructValueType.Text;
                        item.str[field.Index] = field.Value ?? string.Empty;
                    }
                    else if (string.Equals(field.Type, "Number", StringComparison.OrdinalIgnoreCase)
                        && double.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                        && !double.IsNaN(number) && !double.IsInfinity(number))
                    {
                        type = DataStructValueType.Number;
                        item.num[field.Index] = number;
                    }
                    else
                    {
                        throw new BridgeRequestException(400, "INVALID_ARGUMENT",
                            $"数据项 {item.Name} 的字段 {field.Name} 类型或数值无效。");
                    }
                    item.FieldNames[field.Index] = field.Name.Trim();
                    item.FieldTypes[field.Index] = type;
                }
                result.dataStructItems.Add(item);
            }
            return result;
        }

        private static JObject BuildDataStructJObject(string name, DataStruct ds)
        {
            var items = new JArray();
            for (int i = 0; i < ds.dataStructItems.Count; i++)
            {
                items.Add(BuildDataStructItemJObject(i, ds.dataStructItems[i]));
            }
            return new JObject
            {
                ["name"] = name,
                ["itemCount"] = ds.dataStructItems.Count,
                ["items"] = items
            };
        }

        private static JObject BuildDataStructItemJObject(int index, DataStructItem item)
        {
            var fields = new JArray();
            if (item != null && item.FieldNames != null)
            {
                // 按 fieldIndex 排序输出字段
                foreach (int fidx in item.FieldNames.Keys.OrderBy(k => k))
                {
                    string fName = item.FieldNames[fidx];
                    string fType = (item.FieldTypes != null && item.FieldTypes.TryGetValue(fidx, out DataStructValueType ft))
                        ? (ft == DataStructValueType.Number ? "Number" : "Text") : string.Empty;
                    string fStrVal = (item.str != null && item.str.TryGetValue(fidx, out string sv)) ? (sv ?? string.Empty) : string.Empty;
                    double fNumVal = (item.num != null && item.num.TryGetValue(fidx, out double nv)) ? nv : 0;
                    fields.Add(new JObject
                    {
                        ["index"] = fidx,
                        ["name"] = fName ?? string.Empty,
                        ["type"] = fType,
                        ["strValue"] = fStrVal,
                        ["numValue"] = fNumVal
                    });
                }
            }
            return new JObject
            {
                ["index"] = index,
                ["name"] = item?.Name ?? string.Empty,
                ["fields"] = fields
            };
        }

        // ===================== 迁移配置能力包 =====================

        private JObject HandleListIo(JObject request)
        {
            EnsureRuntimeReady();
            var ioMap = runtime.Stores.IoConfiguration?.ByName;
            if (ioMap == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
            }
            string typeFilter = request["type"]?.Value<string>();
            string nameLike = request["nameLike"]?.Value<string>();
            int offset = request["offset"]?.Value<int>() ?? 0;
            int limit = request["limit"]?.Value<int>() ?? 50;
            if (offset < 0 || limit < 1 || limit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    "offset必须大于等于0，limit必须在1..100范围内。");
            }

            List<IO> matches = ioMap.Values
                .Where(io => io != null)
                .Where(io => string.IsNullOrEmpty(typeFilter)
                    || string.Equals(io.IOType, typeFilter, StringComparison.OrdinalIgnoreCase))
                .Where(io => string.IsNullOrEmpty(nameLike)
                    || (io.Name ?? string.Empty).IndexOf(nameLike, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(io => io.Name ?? string.Empty, StringComparer.Ordinal)
                .ToList();
            JArray items = new JArray(matches
                .Skip(offset)
                .Take(limit)
                .Select(BuildIoCatalogJObject));
            return new JObject
            {
                ["total"] = matches.Count,
                ["offset"] = offset,
                ["limit"] = limit,
                ["returned"] = items.Count,
                ["hasMore"] = (long)offset + items.Count < matches.Count,
                ["nextOffset"] = (long)offset + items.Count < matches.Count
                    ? (JToken)((long)offset + items.Count)
                    : JValue.CreateNull(),
                ["items"] = items
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetIo(JObject request)
        {
            EnsureRuntimeReady();
            var ioMap = runtime.Stores.IoConfiguration?.ByName;
            if (ioMap == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
            }
            string name = ReadRequiredString(request, "name");
            if (!ioMap.TryGetValue(name, out IO io) || io == null)
            {
                return BridgeError(404, "IO_NOT_FOUND", $"未找到 IO：{name}");
            }
            return BuildIoJObject(io);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleSearchIo(JObject request)
        {
            EnsureRuntimeReady();
            var ioMap = runtime.Stores.IoConfiguration?.ByName;
            if (ioMap == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
            }
            string keyword = request["keyword"]?.Value<string>()?.Trim() ?? string.Empty;
            bool returnAll = string.IsNullOrEmpty(keyword)
                || string.Equals(keyword, "*", StringComparison.Ordinal);
            string typeFilter = request["type"]?.Value<string>();
            int? cardNum = request["cardNum"]?.Value<int>();
            int offset = request["offset"]?.Value<int>() ?? 0;
            int limit = request["limit"]?.Value<int>() ?? 50;
            if (offset < 0 || limit < 1 || limit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    "offset必须大于等于0，limit必须在1..100范围内。");
            }

            List<IO> matches = ioMap.Values
                .Where(io => io != null)
                .Where(io => returnAll
                    || (io.Name ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(io => string.IsNullOrEmpty(typeFilter)
                    || string.Equals(io.IOType, typeFilter, StringComparison.OrdinalIgnoreCase))
                .Where(io => !cardNum.HasValue || io.CardNum == cardNum.Value)
                .OrderBy(io => io.Name ?? string.Empty, StringComparer.Ordinal)
                .ToList();
            JArray items = new JArray(matches
                .Skip(offset)
                .Take(limit)
                .Select(BuildIoCatalogJObject));
            return new JObject
            {
                ["keyword"] = keyword,
                ["queryMode"] = returnAll ? "all" : "contains",
                ["type"] = typeFilter ?? string.Empty,
                ["cardNum"] = cardNum.HasValue ? JToken.FromObject(cardNum.Value) : null,
                ["total"] = matches.Count,
                ["offset"] = offset,
                ["limit"] = limit,
                ["returned"] = items.Count,
                ["hasMore"] = (long)offset + items.Count < matches.Count,
                ["nextOffset"] = (long)offset + items.Count < matches.Count
                    ? (JToken)((long)offset + items.Count)
                    : JValue.CreateNull(),
                ["items"] = items
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetIoState(JObject request)
        {
            EnsureRuntimeReady();
            var ioMap = runtime.Stores.IoConfiguration?.ByName;
            if (ioMap == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
            }
            string name = ReadRequiredString(request, "name");
            if (!ioMap.TryGetValue(name, out IO io) || io == null)
            {
                return BridgeError(404, "IO_NOT_FOUND", $"未找到 IO：{name}");
            }
            bool? state = null;
            string error = null;
            try
            {
                bool bval = false;
                bool ok;
                if (string.Equals(io.IOType, "通用输出", StringComparison.OrdinalIgnoreCase))
                {
                    ok = runtime.Io?.GetOutIO(io, ref bval) ?? false;
                }
                else
                {
                    ok = runtime.Io?.GetInIO(io, ref bval) ?? false;
                }
                if (ok)
                {
                    state = bval;
                }
                else
                {
                    error = "读取失败或硬件未就绪";
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return new JObject
            {
                ["name"] = name,
                ["ioType"] = io.IOType ?? string.Empty,
                ["cardNum"] = io.CardNum,
                ["module"] = io.Module,
                ["ioIndex"] = io.IOIndex ?? string.Empty,
                ["state"] = state.HasValue ? JToken.FromObject(state.Value) : null,
                ["error"] = error ?? string.Empty
            };
        }

        private static JObject BuildIoJObject(IO io)
        {
            return new JObject
            {
                ["index"] = io.Index,
                ["name"] = io.Name ?? string.Empty,
                ["cardNum"] = io.CardNum,
                ["module"] = io.Module,
                ["ioIndex"] = io.IOIndex ?? string.Empty,
                ["ioType"] = io.IOType ?? string.Empty,
                ["usedType"] = io.UsedType ?? string.Empty,
                ["effectLevel"] = io.EffectLevel ?? string.Empty,
                ["note"] = io.Note ?? string.Empty,
                ["isRemark"] = io.IsRemark
            };
        }

        private static JObject BuildIoCatalogJObject(IO io)
        {
            JObject item = BuildIoJObject(io);
            string note = io?.Note ?? string.Empty;
            item["note"] = CompactDiagnosticText(note, 300);
            item["noteTruncated"] = note.Length > 300;
            return item;
        }

        // ===================== 报警清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListAlarms(JObject request)
        {
            EnsureRuntimeReady();
            AlarmInfoStore store = runtime.Stores.Alarms;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "报警存储未初始化。");
            }
            bool includeEmpty = request["includeEmpty"]?.Value<bool>() ?? false;
            string categoryLike = request["categoryLike"]?.Value<string>();
            string nameLike = request["nameLike"]?.Value<string>();
            int offset = ReadOptionalInt(request, "offset") ?? 0;
            int limit = ReadOptionalInt(request, "limit") ?? 50;
            if (offset < 0 || limit < 1 || limit > 100)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "offset 必须大于等于0，limit 必须在1..100之间。");
            }

            List<int> indices = store.GetValidIndices();
            var items = new List<JObject>();
            if (includeEmpty)
            {
                for (int i = 0; i < AlarmInfoStore.AlarmCapacity; i++)
                {
                    if (store.TryGetByIndex(i, out AlarmInfo alarm))
                    {
                        items.Add(BuildAlarmJObject(alarm));
                    }
                }
            }
            else
            {
                foreach (int idx in indices)
                {
                    if (store.TryGetByIndex(idx, out AlarmInfo alarm))
                    {
                        items.Add(BuildAlarmJObject(alarm));
                    }
                }
            }
            // 过滤
            if (!string.IsNullOrEmpty(categoryLike) || !string.IsNullOrEmpty(nameLike))
            {
                items = items.Where(a =>
                {
                    string cat = a["category"]?.Value<string>() ?? string.Empty;
                    string nm = a["name"]?.Value<string>() ?? string.Empty;
                    bool catOk = string.IsNullOrEmpty(categoryLike)
                        || cat.IndexOf(categoryLike, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool nameOk = string.IsNullOrEmpty(nameLike)
                        || nm.IndexOf(nameLike, StringComparison.OrdinalIgnoreCase) >= 0;
                    return catOk && nameOk;
                }).ToList();
            }
            int filteredTotal = items.Count;
            List<JObject> page = items.Skip(offset).Take(limit).ToList();
            return new JObject
            {
                ["total"] = filteredTotal,
                ["offset"] = offset,
                ["limit"] = limit,
                ["hasMore"] = offset + page.Count < filteredTotal,
                ["items"] = new JArray(page)
            };
        }

        private static JObject BuildAlarmJObject(AlarmInfo alarm)
        {
            return new JObject
            {
                ["index"] = alarm.Index,
                ["name"] = alarm.Name ?? string.Empty,
                ["category"] = alarm.Category ?? string.Empty,
                ["btn1"] = alarm.Btn1 ?? string.Empty,
                ["btn2"] = alarm.Btn2 ?? string.Empty,
                ["btn3"] = alarm.Btn3 ?? string.Empty,
                ["note"] = alarm.Note ?? string.Empty
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetAlarm(JObject request)
        {
            EnsureRuntimeReady();
            int index = ReadRequiredInt(request, "index");
            AlarmInfo alarm = ResolveAlarm(index);
            return new JObject
            {
                ["ok"] = true,
                ["alarm"] = BuildAlarmJObject(alarm)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleSetAlarm(JObject request)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("修改报警信息");
            int index = ReadRequiredInt(request, "index");
            string name = ReadRequiredString(request, "name");
            string note = ReadRequiredString(request, "note");
            string category = ReadOptionalString(request, "category");
            string btn1 = ReadOptionalString(request, "btn1");
            string btn2 = ReadOptionalString(request, "btn2");
            string btn3 = ReadOptionalString(request, "btn3");

            // 业务约束：name 与 note 必须同时非空白（与 FrmAlarmConfig 校验一致）
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(note))
            {
                return BridgeError(400, "INVALID_ARGUMENT", "name 与 note 必须同时填写且不能为空白。");
            }

            AlarmInfo alarm = ResolveAlarm(index);
            bool allowOverwrite = ReadOptionalBoolean(request, "allowOverwrite") ?? false;
            if (!allowOverwrite && !string.IsNullOrWhiteSpace(alarm.Name)
                && !string.Equals(alarm.Name.Trim(), name.Trim(), StringComparison.Ordinal))
            {
                return BridgeError(409, "ALARM_SLOT_OCCUPIED",
                    $"报警槽位 index={index} 已被“{alarm.Name}”占用；确认替换后请设置 allowOverwrite=true。");
            }
            runtime.Stores.Alarms.UpdateAlarm(index, name, category, btn1, btn2, btn3, note);
            runtime.Stores.Alarms.Save(runtime.Paths.ConfigPath);
            runtime.Stores.Alarms.TryGetByIndex(index, out alarm);
            RefreshAlarmConfigView();
            return new JObject
            {
                ["ok"] = true,
                ["alarm"] = BuildAlarmJObject(alarm),
                ["message"] = $"报警 [{name}] 已保存于 index={index}。"
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleDeleteAlarm(JObject request)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("删除报警信息");
            int index = ReadRequiredInt(request, "index");
            AlarmInfo alarm = ResolveAlarm(index);
            if (string.IsNullOrEmpty(alarm.Name) && string.IsNullOrEmpty(alarm.Note))
            {
                return BridgeError(404, "ALARM_NOT_FOUND", $"报警 index={index} 本身为空，无需删除。");
            }

            string oldName = alarm.Name ?? string.Empty;
            runtime.Stores.Alarms.ClearAlarm(index);
            // Index 保持不变（固定槽位）

            runtime.Stores.Alarms.Save(runtime.Paths.ConfigPath);
            RefreshAlarmConfigView();
            return new JObject
            {
                ["ok"] = true,
                ["index"] = index,
                ["message"] = $"报警 index={index}「{oldName}」已清空。"
            };
        }

        // 报警配置窗口可能已打开，触发界面刷新以显示最新数据。
        // RefreshAlarmInfo 会从已保存的文件重新加载，数据一致；失败时不影响数据保存结果。
        private void RefreshAlarmConfigView()
        {
            try { runtime.EditorUi?.RefreshAlarmConfiguration(); }
            catch { /* 界面刷新失败不影响数据保存 */ }
        }

        // ===================== PLC 设备清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListPlcDevices(JObject request)
        {
            EnsureRuntimeReady();
            PlcConfigStore store = runtime.Stores.Plc;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "PLC 存储未初始化。");
            }
            bool includeMaps = request["includeMaps"]?.Value<bool>() ?? false;
            string exactName = ReadOptionalString(request, "name");
            PlcConfiguration configuration = store.GetSnapshot();
            var devices = configuration.Devices;
            IReadOnlyDictionary<string, PlcDeviceRuntimeSnapshot> runtimeByName =
                (runtime.PlcRuntime?.GetSnapshots() ?? new List<PlcDeviceRuntimeSnapshot>())
                .ToDictionary(item => item.DeviceName, StringComparer.OrdinalIgnoreCase);
            var items = new List<JObject>();
            foreach (PlcDeviceConfig dev in devices)
            {
                if (dev == null) continue;
                if (!string.IsNullOrWhiteSpace(exactName)
                    && !string.Equals(dev.Name, exactName, StringComparison.Ordinal)) continue;
                runtimeByName.TryGetValue(dev.Name ?? string.Empty, out PlcDeviceRuntimeSnapshot runtime);
                JObject obj = new JObject
                {
                    ["name"] = dev.Name ?? string.Empty,
                    ["protocol"] = "ModbusTcp",
                    ["profile"] = dev.Profile.ToString(),
                    ["ip"] = dev.IpAddress ?? string.Empty,
                    ["port"] = dev.Port,
                    ["unitId"] = dev.UnitId,
                    ["connectTimeoutMs"] = dev.ConnectTimeoutMs,
                    ["autoConnect"] = dev.AutoConnect,
                    ["scanIntervalMs"] = dev.ScanIntervalMs,
                    ["dataFormat"] = dev.DataFormat,
                    ["isStringReverse"] = dev.IsStringReverse,
                    ["addressStartWithZero"] = dev.AddressStartWithZero,
                    ["statusVariableName"] = dev.StatusVariableName ?? string.Empty,
                    ["runtimeState"] = runtime?.State.ToString() ?? PlcRuntimeState.Uninitialized.ToString(),
                    ["lastError"] = runtime?.LastError ?? string.Empty
                };
                if (includeMaps)
                {
                    var deviceMaps = new JArray();
                    foreach (PlcMapConfig map in dev.Mappings)
                    {
                        if (map == null) continue;
                        deviceMaps.Add(new JObject
                        {
                            ["id"] = map.Id ?? string.Empty,
                            ["name"] = map.Name ?? string.Empty,
                            ["enabled"] = map.Enabled,
                            ["area"] = map.Area.ToString(),
                            ["startAddress"] = map.StartAddress,
                            ["dataType"] = map.DataType.ToString(),
                            ["direction"] = map.Direction.ToString(),
                            ["priority"] = map.Priority.ToString(),
                            ["elementCount"] = map.ElementCount,
                            ["stringByteLength"] = map.StringByteLength,
                            ["variableNames"] = new JArray(map.VariableNames ?? new List<string>()),
                            ["changeTolerance"] = map.ChangeTolerance
                        });
                    }
                    obj["maps"] = deviceMaps;
                }
                items.Add(obj);
            }
            if (!string.IsNullOrWhiteSpace(exactName) && items.Count == 0)
            {
                throw new BridgeRequestException(404, "PLC_DEVICE_NOT_FOUND", $"未找到PLC设备：{exactName}");
            }
            return new JObject
            {
                ["total"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        // ===================== 控制卡/轴清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListCards(JObject request)
        {
            EnsureRuntimeReady();
            CardConfigStore store = runtime.Stores.Cards;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "控制卡存储未初始化。");
            }
            bool includeAxes = request["includeAxes"]?.Value<bool>() ?? true;
            var items = new List<JObject>();
            int cardCount = store.GetControlCardCount();
            for (int ci = 0; ci < cardCount; ci++)
            {
                if (!store.TryGetControlCard(ci, out ControlCard card) || card == null) continue;
                JObject obj = new JObject
                {
                    ["cardIndex"] = ci,
                    ["cardType"] = card.cardHead?.CardType ?? string.Empty,
                    ["axisCount"] = card.cardHead?.AxisCount ?? 0,
                    ["inputCount"] = card.cardHead?.InputCount ?? 0,
                    ["outputCount"] = card.cardHead?.OutputCount ?? 0
                };
                if (includeAxes && card.axis != null)
                {
                    var axes = new JArray();
                    for (int ai = 0; ai < card.axis.Count; ai++)
                    {
                        Axis axis = card.axis[ai];
                        if (axis == null) continue;
                        axes.Add(new JObject
                        {
                            ["axisIndex"] = ai,
                            ["axisName"] = axis.AxisName ?? string.Empty,
                            ["axisNum"] = axis.AxisNum,
                            ["pulseToMM"] = axis.PulseToMM,
                            ["homeDirection"] = axis.HomeDirection ?? string.Empty,
                            ["homeMode"] = "一次回零加回找",
                            ["homeSpeed"] = axis.HomeSpeed ?? string.Empty,
                            ["speedInfo"] = axis.SpeedInfo,
                            ["speedMax"] = axis.SpeedMax,
                            ["accMax"] = axis.AccMax,
                            ["decMax"] = axis.DecMax
                        });
                    }
                    obj["axes"] = axes;
                }
                items.Add(obj);
            }
            return new JObject
            {
                ["total"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        // ===================== 托盘点位清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListTrayPoints(JObject request)
        {
            // TrayPointStore 是运行时缓存，无持久化枚举 API，这里返回空列表提示 AI 当前无缓存。
            // 流程通过指令写入 TrayPointStore，AI 若需查询需先知道 stationName + trayId。
            string stationName = request["stationName"]?.Value<string>();
            int trayId = request["trayId"]?.Value<int>() ?? -1;
            var store = runtime.Stores.TrayPoints;
            if (store == null)
            {
                return new JObject
                {
                    ["available"] = false,
                    ["message"] = "TrayPointStore 未初始化。",
                    ["items"] = new JArray()
                };
            }
            if (string.IsNullOrWhiteSpace(stationName) || trayId < 0)
            {
                return new JObject
                {
                    ["available"] = true,
                    ["message"] = "需提供 stationName 和 trayId 才能读取已缓存的料盘点位。",
                    ["items"] = new JArray()
                };
            }
            if (!store.TryGet(stationName, trayId, out TrayPointGrid grid) || grid == null)
            {
                return new JObject
                {
                    ["available"] = true,
                    ["stationName"] = stationName,
                    ["trayId"] = trayId,
                    ["message"] = "该料盘尚未缓存点位。",
                    ["items"] = new JArray()
                };
            }
            var points = new JArray();
            foreach (TrayPoint pt in grid.Points)
            {
                points.Add(new JObject
                {
                    ["order"] = pt.Order,
                    ["row"] = pt.Row,
                    ["col"] = pt.Col,
                    ["x"] = pt.X,
                    ["y"] = pt.Y,
                    ["z"] = pt.Z,
                    ["u"] = pt.U,
                    ["v"] = pt.V,
                    ["w"] = pt.W
                });
            }
            return new JObject
            {
                ["available"] = true,
                ["stationName"] = stationName,
                ["trayId"] = trayId,
                ["rowCount"] = grid.RowCount,
                ["colCount"] = grid.ColCount,
                ["total"] = points.Count,
                ["items"] = points
            };
        }

        // ===================== 通讯清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListCommunications(JObject request)
        {
            EnsureRuntimeReady();
            bool includeStatus = request["includeStatus"]?.Value<bool>() ?? true;
            string exactName = ReadOptionalString(request, "name");
            string kind = ReadOptionalString(request, "kind");
            if (!string.IsNullOrWhiteSpace(kind)
                && !string.Equals(kind, "tcp", StringComparison.Ordinal)
                && !string.Equals(kind, "serial", StringComparison.Ordinal))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "kind 只能是 tcp 或 serial。");
            }
            IReadOnlyList<SocketInfo> socketInfos = runtime.Stores.Communication?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>();
            IReadOnlyList<SerialPortInfo> serialPortInfos = runtime.Stores.Communication?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>();
            var tcpItems = new JArray();
            foreach (SocketInfo sock in socketInfos)
            {
                if (sock == null) continue;
                if (!string.IsNullOrWhiteSpace(exactName)
                    && !string.Equals(sock.Name, exactName, StringComparison.Ordinal)) continue;
                if (string.Equals(kind, "serial", StringComparison.Ordinal)) continue;
                JObject obj = new JObject
                {
                    ["name"] = sock.Name ?? string.Empty,
                    ["type"] = sock.Type ?? string.Empty,
                    ["port"] = sock.Port,
                    ["address"] = sock.Address ?? string.Empty,
                    ["isServer"] = string.Equals(sock.Type, "Server", StringComparison.Ordinal),
                    ["frameMode"] = sock.FrameMode ?? string.Empty,
                    ["frameDelimiter"] = sock.FrameDelimiter ?? string.Empty,
                    ["encodingName"] = sock.EncodingName ?? string.Empty,
                    ["connectTimeoutMs"] = sock.ConnectTimeoutMs
                };
                if (includeStatus && runtime.Communication != null)
                {
                    TcpStatus status = runtime.Communication.GetTcpStatus(sock.Name);
                    obj["isRunning"] = status.IsRunning;
                    obj["clientCount"] = status.ClientCount;
                    obj["droppedFrames"] = status.DroppedFrames;
                }
                tcpItems.Add(obj);
            }
            var serialItems = new JArray();
            foreach (SerialPortInfo sp in serialPortInfos)
            {
                if (sp == null) continue;
                if (!string.IsNullOrWhiteSpace(exactName)
                    && !string.Equals(sp.Name, exactName, StringComparison.Ordinal)) continue;
                if (string.Equals(kind, "tcp", StringComparison.Ordinal)) continue;
                JObject obj = new JObject
                {
                    ["name"] = sp.Name ?? string.Empty,
                    ["port"] = sp.Port ?? string.Empty,
                    ["bitRate"] = sp.BitRate ?? string.Empty,
                    ["checkBit"] = sp.CheckBit ?? string.Empty,
                    ["dataBit"] = sp.DataBit ?? string.Empty,
                    ["stopBit"] = sp.StopBit ?? string.Empty,
                    ["frameMode"] = sp.FrameMode ?? string.Empty,
                    ["frameDelimiter"] = sp.FrameDelimiter ?? string.Empty,
                    ["encodingName"] = sp.EncodingName ?? string.Empty
                };
                if (includeStatus && runtime.Communication != null)
                {
                    SerialStatus status = runtime.Communication.GetSerialStatus(sp.Name);
                    obj["isOpen"] = status.IsOpen;
                    obj["droppedFrames"] = status.DroppedFrames;
                }
                serialItems.Add(obj);
            }
            if (!string.IsNullOrWhiteSpace(exactName) && tcpItems.Count + serialItems.Count == 0)
            {
                throw new BridgeRequestException(404, "COMMUNICATION_NOT_FOUND",
                    $"未找到通讯对象：{exactName}" + (string.IsNullOrWhiteSpace(kind) ? string.Empty : $" ({kind})"));
            }
            if (!string.IsNullOrWhiteSpace(exactName) && string.IsNullOrWhiteSpace(kind)
                && tcpItems.Count + serialItems.Count > 1)
            {
                throw new BridgeRequestException(409, "COMMUNICATION_AMBIGUOUS",
                    $"通讯名称同时存在于 TCP 和串口配置：{exactName}，请指定 kind。");
            }
            return new JObject
            {
                ["tcpCount"] = tcpItems.Count,
                ["serialCount"] = serialItems.Count,
                ["tcp"] = tcpItems,
                ["serial"] = serialItems
            };
        }

        #endregion

        private static int CountOperations(Proc proc)
        {
            return proc?.steps?.Sum(step => step?.Ops?.Count ?? 0) ?? 0;
        }

        private void EnsureNoActivePreviewLocked(bool supportsExplicitReplacement = false)
        {
            PreviewApprovalRecord active = previewRecords.Values.FirstOrDefault(item =>
                item != null && !item.Rejected && item.ExpiresAtUtc > DateTime.UtcNow);
            if (active != null)
            {
                bool canReplace = supportsExplicitReplacement && active.IsChangeSetPreview;
                var allowedTransitions = new JArray();
                if (canReplace)
                {
                    allowedTransitions.Add(new JObject
                    {
                        ["tool"] = "preview_change_set",
                        ["requiredArguments"] = new JArray("changeSet", "replacePreviewId"),
                        ["fixedArguments"] = new JObject { ["replacePreviewId"] = active.PreviewId },
                        ["changeSetMode"] = "complete_replacement",
                        ["previousPreviewActionsInherited"] = false,
                        ["previousPreviewLocalKeysInherited"] = false
                    });
                }
                if (active.IsChangeSetPreview && active.Confirmed)
                {
                    allowedTransitions.Add(new JObject
                    {
                        ["tool"] = "apply_change_set",
                        ["arguments"] = new JObject { ["previewId"] = active.PreviewId }
                    });
                }
                else if (active.MigrationConfigurationPreview != null && active.Confirmed)
                {
                    allowedTransitions.Add(new JObject
                    {
                        ["tool"] = "apply_migration_configuration",
                        ["arguments"] = new JObject { ["previewId"] = active.PreviewId }
                    });
                }
                else if (!active.Confirmed)
                {
                    allowedTransitions.Add(new JObject
                    {
                        ["state"] = "awaiting_foreground_confirmation"
                    });
                }
                throw new BridgeRequestException(
                    409,
                    "PREVIEW_IN_FLIGHT",
                    "已有一个尚未结束的预演，本次新预演未创建。",
                    new JObject
                    {
                        ["activePreviewId"] = active.PreviewId,
                        ["confirmed"] = active.Confirmed,
                        ["allowedTransitions"] = allowedTransitions,
                        ["retryableWhen"] = canReplace
                            ? "complete_replacement_change_set_retried_with_replace_preview_id"
                            : "active_preview_committed_discarded_or_expired",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
            }
        }

        private void RemovePreview(string previewId)
        {
            lock (previewLock)
            {
                previewRecords.Remove(previewId);
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static void ValidatePreviewIdFormat(string previewId)
        {
            if (string.IsNullOrWhiteSpace(previewId))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "previewId 需要使用预演工具返回的32位编号。");
            }

            if (!Guid.TryParseExact(previewId, "N", out _))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"previewId 不是合法的32位预演编号：{previewId}");
            }
        }

        private void CleanupExpiredPreviewsLocked()
        {
            DateTime now = DateTime.UtcNow;
            List<string> expiredIds = previewRecords
                .Where(item => item.Value == null || item.Value.ExpiresAtUtc <= now)
                .Select(item => item.Key)
                .ToList();
            foreach (string expiredId in expiredIds)
            {
                previewRecords.Remove(expiredId);
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void EnsurePreviewProcVersion(PreviewApprovalRecord record)
        {
            // 流程结构操作（创建/删除/重排/复制）不绑定单个 procIndex，跳过版本校验
            if (record.ProcIndex < 0)
            {
                return;
            }
            Proc current = GetProcByIndex(record.ProcIndex);
            string currentProcId = current.head?.Id.ToString("D") ?? string.Empty;
            if (!string.Equals(currentProcId, record.BaseProcId, StringComparison.OrdinalIgnoreCase))
            {
                throw new BridgeRequestException(409, "PROC_VERSION_MISMATCH", "流程版本已变化，请重新读取流程详情并重新预演。");
            }
        }

        private static string ComputePatchHash(JObject patch)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(patch.ToString(Formatting.None));
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

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

        private JObject BuildOperationFields(OperationType op)
        {
            return WithOperationReadContext(op, () =>
            {
                RefreshOperationContext(op);
                JObject fields = new JObject();
                foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(op).Cast<PropertyDescriptor>())
                {
                    if (descriptor == null || !descriptor.IsBrowsable)
                    {
                        continue;
                    }
                    fields[descriptor.Name] = ConvertValueToToken(descriptor.GetValue(op));
                }
                return fields;
            });
        }

        private JObject BuildOperationSchema(OperationType op)
        {
            return WithOperationReadContext(op, () =>
            {
                RefreshOperationContext(op);
                JArray fields = new JArray();
                foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(op).Cast<PropertyDescriptor>())
                {
                    if (descriptor == null)
                    {
                        continue;
                    }

                    JObject behaviorRule = OperationBehaviorCatalog.BuildFieldRule(op, descriptor.Name);
                    if (!descriptor.IsBrowsable && behaviorRule == null)
                    {
                        continue;
                    }

                    JObject field = new JObject
                    {
                        ["key"] = descriptor.Name,
                        ["displayName"] = descriptor.DisplayName,
                        ["category"] = descriptor.Category,
                        ["description"] = descriptor.Description ?? string.Empty,
                        ["dataType"] = GetTypeLabel(descriptor.PropertyType),
                        ["jsonType"] = GetJsonTypeLabel(descriptor.PropertyType),
                        ["valueShape"] = GetFieldValueShape(descriptor),
                        ["visible"] = descriptor.IsBrowsable,
                        ["readOnly"] = descriptor.IsReadOnly,
                        ["referenceType"] = GetReferenceType(descriptor.Converter?.GetType().Name),
                        ["enumValues"] = BuildStandardValues(descriptor),
                        ["currentValue"] = ConvertValueToToken(descriptor.GetValue(op))
                    };

                    if (string.Equals(GetReferenceType(descriptor.Converter?.GetType().Name), "proc.goto", StringComparison.Ordinal))
                    {
                        field["format"] = "procIndex-stepIndex-opIndex";
                        field["example"] = "0-2-3";
                        field["allowDisplayText"] = false;
                        field["writeRule"] = "只能写三段式非负整数地址；下拉框显示的步骤名、指令名或“步骤：完成结束”等文字仅供界面展示，禁止作为字段值写入。";
                        field["required"] = op is ParamGoto
                            && (string.Equals(descriptor.Name, "TrueGoto", StringComparison.Ordinal)
                                || string.Equals(descriptor.Name, "FalseGoto", StringComparison.Ordinal));
                    }

                    if (behaviorRule != null)
                    {
                        foreach (JProperty ruleProperty in behaviorRule.Properties())
                        {
                            field[ruleProperty.Name] = ruleProperty.Value.DeepClone();
                        }
                    }

                    JObject itemSchema = BuildOperationListItemSchema(descriptor);
                    if (itemSchema != null)
                    {
                        field["itemSchema"] = itemSchema;
                    }

                    fields.Add(field);
                }

                return new JObject
                {
                    ["operaType"] = op.OperaType ?? string.Empty,
                    ["name"] = op.Name ?? string.Empty,
                    ["behavior"] = OperationBehaviorCatalog.BuildContract(op),
                    ["fields"] = fields
                };
            });
        }

        private JObject BuildOperationListItemSchema(PropertyDescriptor listDescriptor)
        {
            Type listType = listDescriptor?.PropertyType;
            if (listType == null || !listType.IsGenericType)
            {
                return null;
            }

            Type[] arguments = listType.GetGenericArguments();
            if (arguments.Length != 1 || arguments[0] == typeof(string) || arguments[0].IsPrimitive)
            {
                return null;
            }

            Type itemType = arguments[0];
            object item;
            try
            {
                item = Activator.CreateInstance(itemType);
            }
            catch
            {
                return null;
            }

            JArray itemFields = new JArray();
            foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(item).Cast<PropertyDescriptor>())
            {
                if (descriptor == null || !descriptor.IsBrowsable)
                {
                    continue;
                }

                string referenceType = GetReferenceType(descriptor.Converter?.GetType().Name);
                JObject field = new JObject
                {
                    ["key"] = descriptor.Name,
                    ["displayName"] = descriptor.DisplayName,
                    ["description"] = descriptor.Description ?? string.Empty,
                    ["jsonType"] = GetJsonTypeLabel(descriptor.PropertyType),
                    ["valueShape"] = GetFieldValueShape(descriptor),
                    ["referenceType"] = referenceType,
                    ["enumValues"] = BuildStandardValues(descriptor)
                };
                if (string.Equals(referenceType, "proc.goto", StringComparison.Ordinal))
                {
                    field["requiredWhen"] = itemType == typeof(GotoParam)
                        && string.Equals(descriptor.Name, "Goto", StringComparison.Ordinal)
                        ? (JToken)new JObject
                        {
                            ["anySiblingConfigured"] = new JArray("MatchValue", "MatchValueIndex", "MatchValueV")
                        }
                        : null;
                    field["format"] = "procIndex-stepIndex-opIndex";
                    field["allowDisplayText"] = false;
                    field["writeRule"] = "只能写三段式非负整数地址，禁止写步骤名、指令名或界面显示文字。";
                }
                itemFields.Add(field);
            }

            return new JObject
            {
                ["itemType"] = itemType.Name,
                ["fields"] = itemFields
            };
        }

        private static bool IsJumpOperation(OperationType operation)
        {
            return OperationGotoReferenceCatalog.HasBusinessGoto(operation);
        }

        private static string BuildFlowDescription(OperationType operation, int opIndex, int operationCount)
        {
            JObject controlFlow = OperationBehaviorCatalog.BuildContract(operation)?["controlFlow"] as JObject;
            if (controlFlow?["known"]?.Value<bool?>() == false)
            {
                return "控制流尚无确定契约";
            }
            if (controlFlow?["terminal"]?.Value<bool?>() == true)
            {
                return "执行后结束当前流程";
            }
            bool fallThrough = controlFlow?["fallThrough"]?.Value<bool?>() == true;
            bool hasGoto = IsJumpOperation(operation);
            if (hasGoto && fallThrough)
            {
                return opIndex < operationCount - 1
                    ? $"满足分支时跳转；未跳转时自动流向[{opIndex + 1}]"
                    : "满足分支时跳转；未跳转时步骤完成";
            }
            if (hasGoto)
            {
                return "由配置分支决定后续流向（不自动流向下一条）";
            }
            return opIndex < operationCount - 1 ? $"执行后自动流向[{opIndex + 1}]" : "执行后步骤完成";
        }

        private string BuildOperationSummary(OperationType op)
        {
            return WithOperationReadContext(op, () =>
            {
                RefreshOperationContext(op);
                List<string> parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(op.Name))
                {
                    parts.Add(op.Name);
                }
                if (!string.IsNullOrWhiteSpace(op.OperaType))
                {
                    parts.Add($"[{op.OperaType}]");
                }

                int count = 0;
                foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(op).Cast<PropertyDescriptor>())
                {
                    if (descriptor == null || !descriptor.IsBrowsable)
                    {
                        continue;
                    }
                    if (string.Equals(descriptor.Name, "Name", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "OperaType", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "Num", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "Note", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "Disable", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "IsBreakpoint", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    object value = descriptor.GetValue(op);
                    if (value == null)
                    {
                        continue;
                    }

                    string text = ConvertFieldValueToText(value);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    parts.Add($"{descriptor.DisplayName}={text}");
                    count++;
                    if (count >= 3)
                    {
                        break;
                    }
                }

                return string.Join("，", parts);
            });
        }

        private JArray BuildGotoTargets(int? procIndexFilter)
        {
            JArray targets = new JArray();
            if (runtime.Stores.Processes?.Items == null)
            {
                return targets;
            }

            for (int procIndex = 0; procIndex < runtime.Stores.Processes.Items.Count; procIndex++)
            {
                if (procIndexFilter.HasValue && procIndexFilter.Value != procIndex)
                {
                    continue;
                }

                Proc proc = runtime.Stores.Processes.Items[procIndex];
                if (proc?.steps == null)
                {
                    continue;
                }

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
                        targets.Add(new JObject
                        {
                            ["key"] = $"{procIndex}-{stepIndex}-{opIndex}",
                            ["procIndex"] = procIndex,
                            ["stepIndex"] = stepIndex,
                            ["opIndex"] = opIndex,
                            ["procId"] = proc?.head?.Id.ToString("D"),
                            ["stepId"] = step?.Id.ToString("D"),
                            ["opId"] = op?.Id.ToString("D"),
                            ["procName"] = proc?.head?.Name ?? string.Empty,
                            ["stepName"] = step?.Name ?? string.Empty,
                            ["opName"] = op?.Name ?? string.Empty,
                            ["operaType"] = op?.OperaType ?? string.Empty
                        });
                    }
                }
            }

            return targets;
        }

        private static JObject BuildEngineSnapshot(EngineSnapshot snapshot, int procIndex)
        {
            if (snapshot == null)
            {
                return new JObject
                {
                    ["procIndex"] = procIndex,
                    ["state"] = ProcRunState.Stopped.ToString(),
                    ["stepIndex"] = -1,
                    ["opIndex"] = -1,
                    ["isBreakpoint"] = false,
                    ["isAlarm"] = false,
                    ["alarmMessage"] = string.Empty,
                    ["terminationReason"] = ProcTerminationReason.None.ToString(),
                    ["performance"] = JValue.CreateNull(),
                    ["updateTime"] = JValue.CreateNull()
                };
            }

            return new JObject
            {
                ["procIndex"] = snapshot.ProcIndex,
                ["procId"] = snapshot.ProcId.ToString("D"),
                ["procName"] = snapshot.ProcName ?? string.Empty,
                ["state"] = snapshot.State.ToString(),
                ["stepIndex"] = snapshot.StepIndex,
                ["opIndex"] = snapshot.OpIndex,
                ["isBreakpoint"] = snapshot.IsBreakpoint,
                ["isAlarm"] = snapshot.IsAlarm,
                ["alarmMessage"] = snapshot.AlarmMessage ?? string.Empty,
                ["terminationReason"] = snapshot.TerminationReason.ToString(),
                ["updateTime"] = snapshot.UpdateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                ["updateTicks"] = snapshot.UpdateTicks,
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
            op.RefleshPropertyAlarm();
            op.RefreshInspector?.Invoke();
            TypeDescriptor.Refresh(op);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static object ConvertTokenToValue(JToken token, PropertyDescriptor descriptor, string targetLabel)
        {
            Type targetType = descriptor.PropertyType;
            Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (token == null || token.Type == JTokenType.Null)
            {
                if (!underlyingType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                {
                    return null;
                }

                throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 不允许为 null。");
            }

            if (underlyingType == typeof(string))
            {
                if (token.Type != JTokenType.String)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 必须是字符串。");
                }
                return token.Value<string>();
            }

            if (underlyingType == typeof(bool))
            {
                if (token.Type != JTokenType.Boolean)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 必须是布尔值。");
                }
                return token.Value<bool>();
            }

            if (underlyingType == typeof(int))
            {
                if (token.Type != JTokenType.Integer)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 必须是整数。");
                }
                return token.Value<int>();
            }

            if (underlyingType == typeof(long))
            {
                if (token.Type != JTokenType.Integer)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 必须是整数。");
                }
                return token.Value<long>();
            }

            if (underlyingType == typeof(float))
            {
                if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 必须是数值。");
                }
                return token.Value<float>();
            }

            if (underlyingType == typeof(double))
            {
                if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 必须是数值。");
                }
                return token.Value<double>();
            }

            if (underlyingType == typeof(decimal))
            {
                if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 必须是数值。");
                }
                return token.Value<decimal>();
            }

            if (underlyingType == typeof(Guid))
            {
                if (token.Type != JTokenType.String || !Guid.TryParse(token.Value<string>(), out Guid guid))
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 必须是 Guid 字符串。");
                }
                return guid;
            }

            if (underlyingType.IsEnum)
            {
                if (token.Type != JTokenType.String)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 必须是枚举字符串。");
                }
                try
                {
                    return Enum.Parse(underlyingType, token.Value<string>(), false);
                }
                catch (Exception ex)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 枚举值非法。", ex.Message);
                }
            }

            if (token.Type == JTokenType.String && descriptor.Converter != null && descriptor.Converter.CanConvertFrom(typeof(string)))
            {
                try
                {
                    return descriptor.Converter.ConvertFromInvariantString(token.Value<string>());
                }
                catch (Exception ex)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 转换失败。", ex.Message);
                }
            }

            try
            {
                return token.ToObject(targetType);
            }
            catch (Exception ex)
            {
                throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{descriptor.Name} 转换失败。", ex.Message);
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static void ValidateStandardValue(PropertyDescriptor descriptor, object value, string targetLabel, string fieldName)
        {
            if (descriptor?.Converter == null || value == null)
            {
                return;
            }

            // 自定义函数源码与流程配置允许在平台运行期间一起准备。新函数只有在用户手动编译并
            // 启动新版本后才会进入运行时列表；ProcessEngine 启动闸门会阻止旧版本执行该流程。
            if (descriptor.ComponentType == typeof(CallCustomFunc)
                && string.Equals(fieldName, nameof(CallCustomFunc.FunctionName), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(value.ToString()))
            {
                return;
            }

            try
            {
                if (!descriptor.Converter.GetStandardValuesSupported(null))
                {
                    return;
                }
                if (!descriptor.Converter.GetStandardValuesExclusive(null))
                {
                    return;
                }

                StandardValuesCollection values = descriptor.Converter.GetStandardValues(null);
                if (values == null || values.Count == 0)
                {
                    return;
                }

                foreach (object item in values)
                {
                    if (Equals(item, value))
                    {
                        return;
                    }

                    if (item != null && value != null
                        && string.Equals(item.ToString(), value.ToString(), StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                throw new BridgeRequestException(400, "PATCH_FIELD_VALUE_INVALID", $"{targetLabel}.{fieldName} 取值不在允许列表中。");
            }
            catch (BridgeRequestException)
            {
                throw;
            }
            catch
            {
            }
        }

        private static JToken ConvertValueToToken(object value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            try
            {
                return JToken.FromObject(value);
            }
            catch
            {
                return value.ToString();
            }
        }

        private static string ConvertFieldValueToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (value is bool boolValue)
            {
                return boolValue ? "true" : "false";
            }
            return value.ToString();
        }

        private static string GetTypeLabel(Type type)
        {
            Type underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying == typeof(string))
            {
                return "string";
            }
            if (underlying == typeof(bool))
            {
                return "bool";
            }
            if (underlying == typeof(int))
            {
                return "int";
            }
            if (underlying == typeof(long))
            {
                return "long";
            }
            if (underlying == typeof(float))
            {
                return "float";
            }
            if (underlying == typeof(double))
            {
                return "double";
            }
            if (underlying == typeof(decimal))
            {
                return "decimal";
            }
            if (underlying == typeof(Guid))
            {
                return "guid";
            }
            if (underlying.IsEnum)
            {
                return "enum";
            }
            return underlying.Name;
        }

        private static string GetJsonTypeLabel(Type type)
        {
            Type underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying == typeof(string) || underlying == typeof(Guid) || underlying.IsEnum)
            {
                return "string";
            }
            if (underlying == typeof(bool))
            {
                return "boolean";
            }
            if (underlying == typeof(int) || underlying == typeof(long))
            {
                return "integer";
            }
            if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal))
            {
                return "number";
            }
            return "object";
        }

        private static string GetFieldValueShape(PropertyDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return string.Empty;
            }

            string jsonType = GetJsonTypeLabel(descriptor.PropertyType);
            string referenceType = GetReferenceType(descriptor.Converter?.GetType().Name);
            if (jsonType == "string" && !string.IsNullOrEmpty(referenceType))
            {
                return "必须传 JSON 字符串；即使候选值看起来是数字编号，也要写成带引号的字符串，例如 \"0\"。";
            }
            if (jsonType == "string")
            {
                return "必须传 JSON 字符串。";
            }
            if (jsonType == "boolean")
            {
                return "必须传 JSON 布尔值 true/false。";
            }
            if (jsonType == "integer")
            {
                return "必须传 JSON 整数。";
            }
            if (jsonType == "number")
            {
                return "必须传 JSON 数值。";
            }
            return "必须传与字段类型匹配的 JSON 值。";
        }

        private static string GetReferenceType(string converterTypeName)
        {
            switch (converterTypeName)
            {
                case "IoOutItem":
                    return "io.output";
                case "IoInItem":
                    return "io.input";
                case "AlarmInfoItem":
                    return "alarm.infoId";
                case "DataStItem":
                    return "dataStruct";
                case "ProcItem":
                    return "proc";
                case "CommItem":
                    return "comm.all";
                case "ValueItem":
                    return "value";
                case "TcpItem":
                    return "comm.tcp";
                case "SerialPortItem":
                    return "comm.serial";
                case "PlcItem":
                    return "plc.device";
                case "GotoItem":
                    return "proc.goto";
                case "StationtItem":
                case "SetStationVelItem":
                    return "station";
                case "StationPosDic":
                case "StationPosWithSpecial":
                    return "station.position";
                case "StationAixsItem":
                    return "station.axis";
                case "funcNameItem":
                    return "customFunc";
                default:
                    return string.Empty;
            }
        }

        private static bool IsVariableIndexDescriptor(PropertyDescriptor descriptor)
        {
            if (descriptor == null) return false;
            string displayName = descriptor.DisplayName ?? string.Empty;
            string description = descriptor.Description ?? string.Empty;
            return displayName.IndexOf("变量", StringComparison.Ordinal) >= 0
                && displayName.IndexOf("索引", StringComparison.Ordinal) >= 0
                || description.IndexOf("变量索引", StringComparison.Ordinal) >= 0;
        }

        private static JArray BuildStandardValues(PropertyDescriptor descriptor)
        {
            JArray values = new JArray();
            if (descriptor?.Converter == null)
            {
                return values;
            }

            try
            {
                if (!descriptor.Converter.GetStandardValuesSupported(null))
                {
                    return values;
                }

                StandardValuesCollection collection = descriptor.Converter.GetStandardValues(null);
                if (collection == null)
                {
                    return values;
                }

                foreach (object item in collection)
                {
                    values.Add(ConvertValueToToken(item));
                }
            }
            catch
            {
            }

            return values;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private sealed class BridgeRequestException : Exception
        {
            public BridgeRequestException(int statusCode, string code, string message, string details = null)
                : base(message)
            {
                StatusCode = statusCode;
                Code = code;
                Details = details;
            }

            public int StatusCode { get; }

            public string Code { get; }

            public string Details { get; }
        }

        private sealed class PreviewApprovalRecord
        {
            public string PreviewId { get; set; }

            public JObject Patch { get; set; }

            public string PatchHash { get; set; }

            public int ProcIndex { get; set; }

            public string BaseProcId { get; set; }

            public DateTime CreatedAtUtc { get; set; }

            public DateTime ExpiresAtUtc { get; set; }

            public bool Confirmed { get; set; }

            public bool Rejected { get; set; }

            public bool IsChangeSetPreview { get; set; }

            public string ReplacedPreviewId { get; set; }

            public Proc DraftProc { get; set; }

            public AiChangeSetCompileResult AiChangeSetPreview { get; set; }

            public MigrationConfigurationPreview MigrationConfigurationPreview { get; set; }

            public string BaseStateHash { get; set; }

            public DateTime? ConfirmedAtUtc { get; set; }
        }

        private sealed class CommReferenceCatalog
        {
            public List<string> All { get; set; } = new List<string>();

            public List<string> Tcp { get; set; } = new List<string>();

            public List<string> Serial { get; set; } = new List<string>();
        }
    }
}
