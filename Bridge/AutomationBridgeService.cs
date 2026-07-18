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
using System.Text.RegularExpressions;
using System.Threading;
using static System.ComponentModel.TypeConverter;

namespace Automation.Bridge
{
    internal sealed class AutomationBridgeService
    {
        private const string IntentTemplateCatalogRelativePath = @"IntentTemplates\intent_templates.json";
        private const int MaxOverviewOperationCount = 300;
        private const int MaxDetailOperationCount = 100;
        private const int MaxBatchReadOperationCount = 25;
        private const int MaxProcDetailUtf8Bytes = 256 * 1024;
        private const int MaxBatchReadUtf8Bytes = 256 * 1024;
        private const int MaxStepDetailOperationCount = 100;
        private const int MaxBatchFieldValuesUtf8Bytes = 8 * 1024;
        private const int MaxBatchNameLength = 64;
        private static readonly LocalFileLogger bridgeErrorLogger = new LocalFileLogger(
            Path.Combine(@"D:\AutomationLogs", "Bridge"));
        private static readonly JsonSerializerSettings migrationContractJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private static readonly HashSet<string> SupportedPatchActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "update_proc_head_fields",
            "update_step_fields",
            "update_operation_fields",
            "append_step",
            "insert_step",
            "delete_step",
            "move_step",
            "append_operation",
            "insert_operation",
            "delete_operation",
            "move_operation"
        };

        private static readonly HashSet<string> PreviewOnlyChangeTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "field_change",
            "goto_rewrite"
        };

        private static readonly HashSet<string> ProcHeadEditableFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "Name",
            "AutoStart",
            "Disable"
        };

        private static readonly HashSet<string> StepEditableFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "Name",
            "Disable"
        };

        private readonly FrmMain owner;
        private readonly object previewLock = new object();
        private readonly Dictionary<string, PreviewApprovalRecord> previewRecords =
            new Dictionary<string, PreviewApprovalRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, DiagnosticProcIndex> diagnosticIndexes =
            new Dictionary<int, DiagnosticProcIndex>();

        public AutomationBridgeService(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
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
            SF.mainfrm?.RefreshProcessFlowGraph();
            try
            {
                SF.frmProc?.FlashProcNode(procIndex, kind);
                if (SF.frmProc == null || SF.frmProc.IsDisposed
                    || SF.frmDataGrid == null || SF.frmDataGrid.IsDisposed)
                {
                    return;
                }
                // 仅在用户当前正在浏览的流程 == 被改动的流程时刷新+闪烁 FrmDataGrid。
                // 用户在看别的流程时不打断其浏览。
                if (SF.frmProc.SelectedProcNum != procIndex)
                {
                    return;
                }
                SF.frmProc.RefreshCurrentBinding();
                if (affectedOps != null && affectedOps.Count > 0)
                {
                    SF.frmDataGrid.FlashRows(affectedOps);
                }
                else
                {
                    SF.frmDataGrid.FlashGrid(kind);
                }
            }
            catch
            {
                // 动效失败不影响主流程
            }
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
                        ["procCount"] = SF.frmProc?.procsList?.Count ?? 0,
                        ["securityLocked"] = SF.SecurityLocked
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
                    // ---------- intent 拆分端点 ----------
                    case "/bridge/intent/list_templates":
                        return WrapResponse("intent.list_templates", ExecuteOnUiThread(() => HandleListIntentTemplates(request)));
                    case "/bridge/intent/get_template":
                        return WrapResponse("intent.get_template", ExecuteOnUiThread(() => HandleGetIntentTemplate(request)));
                    case "/bridge/intent/build_patch":
                        return WrapResponse("intent.build_patch", ExecuteOnUiThread(() => HandleBuildPatchFromIntent(request)));
                    // ---------- patch 拆分端点 ----------
                    case "/bridge/patch/preview_intent":
                        return WrapResponse("patch.preview_intent", ExecuteOnUiThread(() => HandlePreviewIntent(request)));
                    case "/bridge/patch/apply_intent":
                        WaitForPreviewConfirmation(request);
                        return WrapResponse("patch.apply_intent", ExecuteOnUiThread(() => HandleApplyIntent(request)));
                    case "/bridge/patch/preview_patch":
                        return WrapResponse("patch.preview_patch", ExecuteOnUiThread(() => HandlePreviewPatch(request)));
                    case "/bridge/patch/apply_patch":
                        WaitForPreviewConfirmation(request);
                        return WrapResponse("patch.apply_patch", ExecuteOnUiThread(() => HandleApplyPatch(request)));
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
                    case "/bridge/proc/create_batch":
                        ValidateCreateBatchRequestShape(request);
                        WaitForPreviewConfirmation(request, false);
                        return WrapResponse("proc.create_batch", ExecuteOnUiThread(() => HandleCreateBatchOrApply(request)));
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

            List<int> candidates = Enumerable.Range(0, SF.frmProc.procsList.Count)
                .Where(i => string.IsNullOrEmpty(keyword)
                    || (SF.frmProc.procsList[i]?.head?.Name?.IndexOf(keyword,
                        StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                    || i.ToString(CultureInfo.InvariantCulture).Contains(keyword))
                .ToList();

            var array = new JArray();
            foreach (int i in candidates.Skip(offset).Take(limit))
            {
                Proc proc = SF.frmProc.procsList[i];
                EngineSnapshot snapshot = SF.DR?.GetSnapshot(i);
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
                    SF.frmProc.procsList,
                    index => SF.DR?.GetSnapshot(index));
            }
            else if (string.Equals(scope, "process", StringComparison.OrdinalIgnoreCase))
            {
                int procIndex = ReadRequiredInt(request, "procIndex");
                if (!TryGetProcByIndexForRead(procIndex, out _, out JObject error))
                {
                    return error;
                }
                graph = ProcessFlowGraphService.BuildProcess(
                    SF.frmProc.procsList,
                    procIndex,
                    index => SF.DR?.GetSnapshot(index));
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
            if (SF.frmCard?.dataStation != null)
            {
                foreach (DataStation station in SF.frmCard.dataStation)
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
                ["values"] = new JArray(SF.valueStore?.GetValueNames() ?? new List<string>()),
                ["dataStructs"] = new JArray(SF.dataStructStore?.GetStructNames() ?? new List<string>()),
                ["alarmInfoIds"] = new JArray((SF.alarmInfoStore?.GetValidIndices() ?? new List<int>()).Select(item => (JToken)item)),
                ["io"] = new JObject
                {
                    ["all"] = new JArray(SF.frmIO?.IoItems ?? new List<string>()),
                    ["inputs"] = new JArray(SF.frmIO?.IoInItems ?? new List<string>()),
                    ["outputs"] = new JArray(SF.frmIO?.IoOutItems ?? new List<string>())
                },
                ["communication"] = new JObject
                {
                    ["all"] = new JArray(commNames.All),
                    ["tcp"] = new JArray(commNames.Tcp),
                    ["serial"] = new JArray(commNames.Serial)
                },
                ["plcDevices"] = new JArray((SF.plcStore?.GetSnapshot().Devices ?? new List<PlcDeviceConfig>())
                    .Where(device => device != null && !string.IsNullOrWhiteSpace(device.Name))
                    .Select(device => device.Name)),
                ["stations"] = stations,
                ["gotoTargets"] = BuildGotoTargets(procIndex)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListIntentTemplates(JObject request)
        {
            string patchAction = ReadOptionalString(request, "patchAction");
            JArray templates = LoadIntentTemplateCatalog();
            JArray items = new JArray();
            foreach (JObject template in templates.OfType<JObject>())
            {
                if (!string.IsNullOrWhiteSpace(patchAction)
                    && !string.Equals(template["patchAction"]?.Value<string>(), patchAction, StringComparison.Ordinal))
                {
                    continue;
                }

                items.Add(new JObject
                {
                    ["templateId"] = template["templateId"]?.Value<string>() ?? string.Empty,
                    ["intentType"] = template["intentType"]?.Value<string>() ?? string.Empty,
                    ["patchAction"] = template["patchAction"]?.Value<string>() ?? string.Empty,
                    ["title"] = template["title"]?.Value<string>() ?? string.Empty,
                    ["whenToUse"] = template["whenToUse"]?.Value<string>() ?? string.Empty
                });
            }

            return new JObject
            {
                ["items"] = items
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetIntentTemplate(JObject request)
        {
            string templateId = ReadOptionalString(request, "templateId");
            string patchAction = ReadOptionalString(request, "patchAction");
            JArray templates = LoadIntentTemplateCatalog();
            JObject matched = templates
                .OfType<JObject>()
                .FirstOrDefault(item =>
                    (!string.IsNullOrWhiteSpace(templateId)
                        && string.Equals(item["templateId"]?.Value<string>(), templateId, StringComparison.Ordinal))
                    || (!string.IsNullOrWhiteSpace(patchAction)
                        && string.Equals(item["patchAction"]?.Value<string>(), patchAction, StringComparison.Ordinal)));

            if (matched == null)
            {
                // 未找到模板不是错误，是正常的查询结果。诚实返回给 AI，引导其改用其他工具，
                // 避免 throw BridgeRequestException 被 Bridge 客户端包装成模糊的 BRIDGE_ERROR。
                return new JObject
                {
                    ["found"] = false,
                    ["message"] = "未找到匹配的中间意图模板。可改用 preview_patch/apply_patch 直接构建 Patch，或调用 create_proc/delete_procs/reorder_proc/copy_proc 进行流程级操作。"
                };
            }

            return new JObject
            {
                ["found"] = true,
                ["template"] = matched.DeepClone()
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleBuildPatchFromIntent(JObject request)
        {
            if (!TryReadIntentEnvelope(request, out JObject intent, out JObject error))
            {
                return error;
            }
            JObject patch = ConvertIntentToPatch(intent);
            return new JObject
            {
                ["intentType"] = intent["intentType"]?.Value<string>() ?? string.Empty,
                ["patch"] = patch
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandlePreviewIntent(JObject request)
        {
            if (!TryReadIntentEnvelope(request, out JObject intent, out JObject error))
            {
                return error;
            }
            JObject patch = ConvertIntentToPatch(intent);
            PatchExecutionResult result = ExecutePatch(patch);
            JObject preview = BuildRegisteredPatchPreview(patch, result);
            return new JObject
            {
                ["intentType"] = intent["intentType"]?.Value<string>() ?? string.Empty,
                ["patch"] = patch,
                ["previewId"] = preview["previewId"],
                ["patchHash"] = preview["patchHash"],
                ["preview"] = preview
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleApplyIntent(JObject request)
        {
            if (!TryReadString(request, "previewId", out string previewId, out JObject previewError))
            {
                return previewError;
            }
            if (!TryReadIntentEnvelope(request, out JObject intent, out JObject error))
            {
                return error;
            }
            JObject patch = ConvertIntentToPatch(intent);
            ValidateConfirmedPreview(previewId, patch);
            PatchExecutionResult result = GetStoredPatchResult(previewId);
            CommitPatch(result.ProcIndex, result.Proc, result.AffectedOps);
            RemovePreview(previewId);

            Proc current = GetProcByIndex(result.ProcIndex);
            return new JObject
            {
                ["intentType"] = intent["intentType"]?.Value<string>() ?? string.Empty,
                ["patch"] = patch,
                ["previewId"] = previewId,
                ["apply"] = BuildPatchResult("apply", new PatchExecutionResult
                {
                    ProcIndex = result.ProcIndex,
                    Proc = current,
                    Messages = result.Messages,
                    Changes = result.Changes
                })
            };
        }

        private static bool TryReadIntentEnvelope(JObject request, out JObject intent, out JObject error)
        {
            intent = null;
            error = null;
            JToken intentToken = request?["intent"];
            if (intentToken != null && intentToken.Type != JTokenType.Null)
            {
                if (!(intentToken is JObject intentObject))
                {
                    error = BridgeError(400, "INVALID_ARGUMENT", "字段 intent 必须是 JSON 对象。",
                        "请传入 intent 对象，或把完整对象序列化到 intentJson 字符串中。");
                    return false;
                }
                intent = (JObject)intentObject.DeepClone();
            }
            else
            {
                JToken jsonToken = request?["intentJson"];
                if (jsonToken == null || jsonToken.Type != JTokenType.String)
                {
                    error = BridgeError(400, "INVALID_ARGUMENT", "字段 intentJson 必须是字符串，或直接提供 intent 对象。",
                        "当前类型：" + (jsonToken?.Type.ToString() ?? "缺失"));
                    return false;
                }
                try
                {
                    intent = JToken.Parse(jsonToken.Value<string>()) as JObject;
                }
                catch (JsonReaderException ex)
                {
                    error = BridgeError(400, "INVALID_ARGUMENT", "intentJson 不是合法 JSON。", ex.Message);
                    return false;
                }
                if (intent == null)
                {
                    error = BridgeError(400, "INVALID_ARGUMENT", "intentJson 必须是 JSON 对象。",
                        "请不要传数组、字符串、数字或 null。修正后可直接重试预演。");
                    return false;
                }
            }

            if (!TryReadString(intent, "intentType", out _, out error)
                || !TryReadInteger(intent, "procIndex", out _, out error))
            {
                return false;
            }
            JToken baseProcIdToken = intent["baseProcId"];
            if (baseProcIdToken != null && baseProcIdToken.Type != JTokenType.Null
                && (baseProcIdToken.Type != JTokenType.String
                    || !Guid.TryParse(baseProcIdToken.Value<string>(), out _)))
            {
                error = BridgeError(400, "INVALID_ARGUMENT", "字段 baseProcId 必须是合法 Guid 字符串。",
                    "baseProcId 可以省略，由 Bridge 根据 procIndex 自动补齐；如果提供，必须使用 get_proc_detail 返回的 procId 原值。");
                return false;
            }
            return true;
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
            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
            ProcRunState currentState = snapshot?.State ?? ProcRunState.Stopped;

            switch (action)
            {
                case "start":
                    if (currentState != ProcRunState.Stopped)
                    {
                        return BridgeError(409, "PROC_NOT_STOPPED",
                            $"流程 {procIndex} 尚未结束，当前状态为 {currentState}。请排查流程未结束原因后再启动。");
                    }
                    if (!SF.DR.StartProc(proc, procIndex))
                    {
                        string startError;
                        if (!SF.DR.TryValidateProcessStopped(procIndex, out string stoppedError))
                        {
                            startError = stoppedError;
                        }
                        else
                        {
                            startError = SF.DR.TryValidateProcessStart(proc, procIndex, out string gateError)
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
                    SF.DR.Stop(procIndex);
                    break;
                case "pause":
                    if (currentState != ProcRunState.Running)
                    {
                        return BridgeError(409, "PROC_NOT_RUNNING", $"流程 {procIndex} 不在运行状态，无法暂停。");
                    }
                    SF.DR.Pause(procIndex);
                    break;
                case "resume":
                    if (currentState != ProcRunState.Paused)
                    {
                        return BridgeError(409, "PROC_NOT_PAUSED", $"流程 {procIndex} 不在暂停状态，无法恢复。");
                    }
                    SF.DR.Resume(procIndex);
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

            int targetIndex = SF.frmProc.procsList.Count;
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
                if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"流程索引 {procIndex} 越界。");
                }
                Proc proc = SF.frmProc.procsList[procIndex];
                EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
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
            if (targetIndex < 0 || targetIndex >= SF.frmProc.procsList.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"目标索引 {targetIndex} 越界。");
            }
            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
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
            Dictionary<string, DicValue> draftVariables = SF.valueStore.BuildSaveData();
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

            int targetIndex = SF.frmProc.procsList.Count;
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

            int procIndex = SF.frmProc.procsList.Count;
            SF.frmProc.procsList.Add(proc);

            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(procIndex, proc, errors);
            if (errors.Count > 0)
            {
                SF.frmProc.procsList.RemoveAt(procIndex);
                throw new BridgeRequestException(400, "PROC_VALIDATE_FAILED", "流程创建校验失败。", string.Join("\r\n", errors.Distinct()));
            }

            if (!SF.frmProc.RebuildWorkConfig(procIndex))
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

        private JObject HandleCreateBatchOrApply(JObject request)
        {
            string previewId = ReadOptionalString(request, "previewId");
            JObject definition = ReadRequiredObject(request, "definition");
            if (string.IsNullOrWhiteSpace(previewId))
            {
                return PreviewCreateProcBatch(definition);
            }

            ValidateConfirmedManagePreview(previewId);
            PreviewApprovalRecord record;
            lock (previewLock)
            {
                if (!previewRecords.TryGetValue(previewId, out record)
                    || record.DraftProc == null
                    || !JToken.DeepEquals(record.Patch, definition))
                {
                    throw new BridgeRequestException(409, "PREVIEW_PATCH_MISMATCH", "提交的流程变更集与已确认预演不一致。");
                }
            }

            Proc draft = ObjectGraphCloner.Clone(record.DraftProc);
            int procIndex = SF.frmProc.procsList.Count;
            EnsureAllProcsStoppedForAiStructureCommit("批量创建流程");
            SF.frmProc.procsList.Add(draft);
            if (!SF.frmProc.RebuildWorkConfig(procIndex))
            {
                if (SF.frmProc.procsList.Count > procIndex)
                {
                    SF.frmProc.procsList.RemoveAt(procIndex);
                }
                throw new BridgeRequestException(500, "SAVE_FAILED", "批量创建流程失败，原流程配置已恢复。");
            }
            RemovePreview(previewId);
            NotifyProcChanged(procIndex, ProcChangeKind.Added);
            return new JObject
            {
                ["action"] = "create_proc_batch",
                ["procIndex"] = procIndex,
                ["procName"] = draft.head?.Name ?? string.Empty,
                ["stepCount"] = draft.steps?.Count ?? 0,
                ["operationCount"] = CountOperations(draft),
                ["committed"] = true
            };
        }

        // 纯JSON结构校验不依赖WinForms状态，必须在切换UI线程前完成，
        // 避免无效请求因UI线程繁忙而长时间等待。
        private void ValidateCreateBatchRequestShape(JObject request)
        {
            JObject definition = ReadRequiredObject(request, "definition");
            JToken stepsToken = definition["steps"];
            if (!(stepsToken is JArray steps))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "definition.steps 必须是数组。");
            }
            if (steps.Count < 1)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "steps 至少包含一个步骤。");
            }

            for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                if (!(steps[stepIndex] is JObject step))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"definition.steps[{stepIndex}] 必须是对象。");
                }
                JToken operationsToken = step["operations"];
                if (!(operationsToken is JArray operations))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT",
                        $"definition.steps[{stepIndex}].operations 必须是数组。");
                }
            }
        }

        private static AiChangeSet ParseChangeSet(JObject request)
        {
            JObject token = ReadRequiredObject(request, "changeSet");
            ValidateChangeSetShape(token);
            try
            {
                return JsonConvert.DeserializeObject<AiChangeSet>(token.ToString(Formatting.None),
                    new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Error,
                        NullValueHandling = NullValueHandling.Include
                    }) ?? throw new JsonSerializationException("changeSet 反序列化结果为空。");
            }
            catch (JsonException ex)
            {
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID",
                    "changeSet 不符合 V2 协议。", ex.Message);
            }
        }

        private static JObject HandleGetChangeSetContracts(JObject request)
        {
            JArray kinds = ReadRequiredArray(request, "kinds");
            if (kinds.Any(token => token.Type != JTokenType.String))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "kinds 只能包含字符串。");
            }
            try
            {
                return AiOperationCompilerRegistry.BuildContracts(kinds.Values<string>());
            }
            catch (InvalidOperationException ex)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", ex.Message);
            }
        }

        private static JObject HandleGetNativeOperationContract(JObject request)
        {
            string operaType = ReadRequiredString(request, "operaType").Trim();
            try
            {
                return StructuredOperationCompiler.BuildContract(operaType);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                || ex is ArgumentException || ex is KeyNotFoundException)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", ex.Message);
            }
        }

        private static JObject HandleGetNativeOperationContracts(JObject request)
        {
            EnsureOnlyProperties(request, "nativeOperationContracts", "operaTypes");
            JArray operaTypes = ReadRequiredArray(request, "operaTypes");
            if (operaTypes.Count < 1
                || operaTypes.Any(token => token.Type != JTokenType.String
                    || string.IsNullOrWhiteSpace(token.Value<string>())))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT",
                    "operaTypes 至少包含一个非空字符串。");
            }
            string[] distinct = operaTypes.Values<string>()
                .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
            try
            {
                JObject compactContracts = StructuredOperationCompiler.BuildCompactContracts(distinct);
                compactContracts.AddFirst(new JProperty("schemaRoute", new JObject
                {
                    ["representation"] = "native",
                    ["writeKind"] = "native.operation",
                    ["writeFields"] = "operation.operaType + operation.fields",
                    ["nextTool"] = "preview_change_set",
                    ["fieldMeaning"] = "saveRequired决定配置能否保存；critical与behavior.fieldRules决定流程能否启动",
                    ["rule"] = "先合并common与精确operaType差量再填写递归字段；语义kind使用语义Schema"
                }));
                return compactContracts;
            }
            catch (Exception ex) when (ex is InvalidOperationException
                || ex is ArgumentException || ex is KeyNotFoundException)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", ex.Message);
            }
        }

        private static void ValidateChangeSetShape(JObject changeSet)
        {
            EnsureOnlyProperties(changeSet, "changeSet",
                "version", "title", "actions", "deleteProcesses", "variables", "processes");
            ValidateObjectArray(changeSet["actions"], "changeSet.actions", ValidateAtomicActionShape);
            if (changeSet["deleteProcesses"] is JObject deletion)
            {
                EnsureOnlyProperties(deletion, "changeSet.deleteProcesses", "mode", "names", "procIds");
            }
            else if (changeSet["deleteProcesses"] != null && changeSet["deleteProcesses"].Type != JTokenType.Null)
            {
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID", "changeSet.deleteProcesses 必须是对象。");
            }

            ValidateObjectArray(changeSet["variables"], "changeSet.variables", variable =>
            {
                EnsureOnlyProperties(variable, "changeSet.variables[]",
                    "name", "scope", "ownerProcess", "index", "type", "value", "note", "policy");
                if (variable["ownerProcess"] is JObject owner)
                {
                    EnsureOnlyProperties(owner, "changeSet.variables[].ownerProcess", "procId", "name", "key");
                }
                else if (variable["ownerProcess"] != null
                    && variable["ownerProcess"].Type != JTokenType.Null)
                {
                    throw new BridgeRequestException(
                        400, "CHANGE_SET_INVALID", "changeSet.variables[].ownerProcess 必须是对象。");
                }
            });
            ValidateObjectArray(changeSet["processes"], "changeSet.processes", process =>
            {
                EnsureOnlyProperties(process, "changeSet.processes[]", "key", "action", "targetProcId", "targetName",
                    "name", "autoStart", "disable", "steps");
                ValidateObjectArray(process["steps"], "changeSet.processes[].steps", step =>
                {
                    EnsureOnlyProperties(step, "changeSet.processes[].steps[]", "stepId", "key", "name", "disable",
                        "expectedOperaTypes", "operations");
                    ValidateObjectArray(step["operations"], "changeSet.processes[].steps[].operations", operation =>
                    {
                        ValidateSemanticOperationShape(operation);
                    });
                });
            });
        }

        private static void ValidateAtomicActionShape(JObject action)
        {
            EnsureOnlyProperties(action, "changeSet.actions[]", "type", "targetProcess", "targetStep",
                "targetOperation", "position", "process", "step", "operation");
            if (action["type"]?.Type != JTokenType.String
                || string.IsNullOrWhiteSpace(action["type"]?.Value<string>()))
            {
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID",
                    "changeSet.actions[].type 必须是非空字符串。");
            }
            ValidateOptionalObject(action["targetProcess"], "changeSet.actions[].targetProcess", value =>
                EnsureOnlyProperties(value, "changeSet.actions[].targetProcess", "procId", "name", "key"));
            ValidateOptionalObject(action["targetStep"], "changeSet.actions[].targetStep", value =>
                EnsureOnlyProperties(value, "changeSet.actions[].targetStep", "stepId", "key"));
            ValidateOptionalObject(action["targetOperation"], "changeSet.actions[].targetOperation", value =>
                EnsureOnlyProperties(value, "changeSet.actions[].targetOperation", "opId", "key"));
            ValidateOptionalObject(action["position"], "changeSet.actions[].position", value =>
                EnsureOnlyProperties(value, "changeSet.actions[].position",
                    "beforeId", "beforeKey", "afterId", "afterKey"));
            ValidateOptionalObject(action["process"], "changeSet.actions[].process", value =>
                EnsureOnlyProperties(value, "changeSet.actions[].process",
                    "key", "name", "autoStart", "disable"));
            ValidateOptionalObject(action["step"], "changeSet.actions[].step", value =>
                EnsureOnlyProperties(value, "changeSet.actions[].step", "key", "name", "disable"));
            ValidateOptionalObject(action["operation"], "changeSet.actions[].operation",
                ValidateSemanticOperationShape);
        }

        private static void ValidateOptionalObject(JToken token, string path, Action<JObject> validate)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            if (!(token is JObject value))
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID", $"{path} 必须是对象。");
            validate(value);
        }

        private static void ValidateSemanticOperationShape(JObject operation)
        {
            if (operation["kind"] == null && operation["opId"]?.Type == JTokenType.String)
            {
                EnsureOnlyProperties(operation, "既有指令引用", "opId", "key");
                return;
            }
            string kind = operation["kind"]?.Type == JTokenType.String
                ? operation["kind"].Value<string>()
                : null;
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID",
                    "语义指令 kind 必须是字符串且不能为空。");
            }
            try
            {
                AiOperationCompilerRegistry.Get(kind);
            }
            catch (InvalidOperationException ex)
            {
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID", ex.Message);
            }
            IReadOnlyCollection<string> contractFields;
            try
            {
                contractFields = AiOperationCompilerRegistry.GetDefinitionFields(kind);
            }
            catch (InvalidOperationException ex)
            {
                throw new BridgeRequestException(
                    500, "SEMANTIC_CONTRACT_INVALID", "平台内部语义指令契约无效。", ex.Message);
            }
            string[] allowed = contractFields
                .Concat(new[] { "opId", "key" })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            EnsureOnlyProperties(operation, $"语义指令 {kind}", allowed);
            if (allowed.Contains("conditions", StringComparer.Ordinal))
            {
                ValidateIoConditionArray(operation["conditions"], $"语义指令 {kind}.conditions");
            }
            if (allowed.Contains("outputs", StringComparer.Ordinal))
            {
                ValidateIoConditionArray(operation["outputs"], $"语义指令 {kind}.outputs");
            }
            if (string.Equals(kind, "native.operation", StringComparison.Ordinal))
            {
                if (operation["operaType"]?.Type != JTokenType.String
                    || string.IsNullOrWhiteSpace(operation["operaType"]?.Value<string>()))
                {
                    throw new BridgeRequestException(400, "CHANGE_SET_INVALID",
                        "native.operation.operaType 必须是非空字符串。");
                }
                if (!(operation["fields"] is JObject))
                {
                    throw new BridgeRequestException(400, "CHANGE_SET_INVALID",
                        "native.operation.fields 必须是 JSON 对象。");
                }
            }
            foreach (string targetField in new[] { "target", "whenTrue", "whenFalse", "onFailure" })
            {
                if (allowed.Contains(targetField, StringComparer.Ordinal))
                {
                    ValidateOperationTarget(operation[targetField], targetField);
                }
            }
        }

        private static void ValidateObjectArray(JToken token, string path, Action<JObject> validate)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return;
            }
            if (!(token is JArray array))
            {
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID", $"{path} 必须是数组。");
            }
            for (int index = 0; index < array.Count; index++)
            {
                if (!(array[index] is JObject item))
                {
                    throw new BridgeRequestException(400, "CHANGE_SET_INVALID", $"{path}[{index}] 必须是对象。");
                }
                validate(item);
            }
        }

        private static void ValidateIoConditionArray(JToken token, string path)
        {
            if (!(token is JArray array) || array.Count == 0)
            {
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID", $"{path} 必须是非空数组。");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < array.Count; index++)
            {
                if (!(array[index] is JObject condition))
                    throw new BridgeRequestException(400, "CHANGE_SET_INVALID", $"{path}[{index}] 必须是对象。");
                EnsureOnlyProperties(condition, $"{path}[{index}]", "io", "state");
                string io = condition["io"]?.Type == JTokenType.String
                    ? condition["io"].Value<string>()?.Trim()
                    : null;
                if (string.IsNullOrWhiteSpace(io) || condition["state"]?.Type != JTokenType.Boolean)
                    throw new BridgeRequestException(400, "CHANGE_SET_INVALID", $"{path}[{index}] 必须提供非空字符串 io 和布尔值 state。");
                if (!names.Add(io))
                    throw new BridgeRequestException(400, "CHANGE_SET_INVALID", $"{path} 包含重复IO：{io}。");
            }
        }

        private static void ValidateOperationTarget(JToken token, string field)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return;
            }
            if (!(token is JObject target))
            {
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID", $"语义指令 {field} 必须是对象。");
            }
            EnsureOnlyProperties(target, $"语义指令.{field}",
                "stepId", "stepKey", "operationId", "operationKey");
        }

        private static void EnsureOnlyProperties(JObject value, string path, params string[] allowedNames)
        {
            var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
            string invalidName = value.Properties().Select(property => property.Name)
                .FirstOrDefault(name => !allowed.Contains(name));
            if (invalidName != null)
            {
                throw new BridgeRequestException(400, "CHANGE_SET_INVALID",
                    $"{path} 包含未定义字段：{invalidName}");
            }
        }

        private JObject HandlePreviewChangeSet(AiChangeSet changeSet, string replacePreviewId)
        {
            EnsureRuntimeReady();
            Dictionary<string, DicValue> variables = SF.valueStore?.BuildSaveData()
                ?? throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            AiChangeSetCompileResult draft;
            try
            {
                draft = AiChangeSetCompiler.Compile(
                    changeSet, SF.frmProc.procsList, variables, BuildAiResourceSnapshot());
            }
            catch (InvalidOperationException ex)
            {
                if (TryBuildLocalKeyScopeRecovery(changeSet, ex.Message, out JObject scopeRecovery))
                {
                    throw new BridgeRequestException(409, "CHANGE_SET_LOCAL_KEY_OUT_OF_SCOPE",
                        "当前 ChangeSet 引用了另一未提交预演中的局部 key。",
                        scopeRecovery.ToString(Formatting.None));
                }
                throw new BridgeRequestException(400, "CHANGE_SET_COMPILE_FAILED",
                    "语义变更集编译失败。", new JObject
                    {
                        ["validationError"] = ex.Message,
                        ["reason"] = "fix_validation_error",
                        ["retryableWhen"] = "change_set_passes_validation",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
            }

            JObject normalized = JObject.FromObject(changeSet);
            string previewId = RegisterManagePreview(normalized, replacePreviewId, true);
            PreviewApprovalRecord record;
            lock (previewLock)
            {
                record = previewRecords[previewId];
                record.AiChangeSetPreview = draft;
                record.BaseStateHash = AiChangeSetCompiler.ComputeStateHash(SF.frmProc.procsList, variables);
            }
            var createdPreviewProcIds = new HashSet<string>(draft.Changes.OfType<JObject>()
                .Where(change => string.Equals(
                    change["type"]?.Value<string>(), "process.create", StringComparison.Ordinal))
                .Select(change => change["procId"]?.Value<string>())
                .Where(procId => !string.IsNullOrWhiteSpace(procId)), StringComparer.OrdinalIgnoreCase);
            JArray allowedTransitions = BuildChangeSetAllowedTransitions(record);
            return new JObject
            {
                ["previewId"] = previewId,
                ["confirmed"] = record.Confirmed,
                ["committed"] = false,
                ["configurationSaved"] = false,
                ["objectState"] = "preview_only",
                ["localKeyScope"] = "current_change_set",
                ["readAfterApplyFrom"] = "apply_change_set.createdObjects/affectedProcesses",
                ["status"] = record.Confirmed ? "confirmed" : "awaiting_confirmation",
                ["allowedTransitions"] = allowedTransitions,
                ["draftSaveAllowed"] = true,
                ["revisionMode"] = "full_stage_replacement",
                ["replacedPreviewId"] = record.ReplacedPreviewId,
                ["expiresAt"] = record.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["title"] = changeSet.Title ?? string.Empty,
                ["summary"] = new JObject
                {
                    ["deletedProcesses"] = draft.DeletedProcessCount,
                    ["createdProcesses"] = draft.CreatedProcessCount,
                    ["replacedProcesses"] = draft.ReplacedProcessCount,
                    ["changedVariables"] = draft.ChangedVariableCount,
                    ["atomicActions"] = draft.AtomicActionCount,
                    ["operationsInAffectedProcesses"] = draft.OperationCount
                },
                ["variableResolutions"] = draft.VariableResolutions?.DeepClone() ?? new JArray(),
                ["changes"] = BuildPreviewOnlyView(draft.Changes, createdPreviewProcIds),
                ["processAnalyses"] = BuildPreviewOnlyView(draft.ProcessAnalyses, createdPreviewProcIds),
                ["readinessStatus"] = draft.ReadinessStatus,
                ["runnable"] = draft.Runnable,
                ["warnings"] = BuildPreviewOnlyView(draft.ConfigurationWarnings, createdPreviewProcIds),
                ["runBlockers"] = BuildPreviewOnlyView(draft.RunBlockers, createdPreviewProcIds),
                ["stageIssues"] = BuildPreviewOnlyView(draft.StageIssues, createdPreviewProcIds),
                ["messages"] = new JArray(draft.AtomicActionCount > 0
                    ? $"本阶段包含 {draft.AtomicActionCount} 个原子动作；将删除 {draft.DeletedProcessCount} 个流程、创建 {draft.CreatedProcessCount} 个流程、修改 {draft.ReplacedProcessCount} 个流程、变更 {draft.ChangedVariableCount} 个变量。受影响流程修改后共 {draft.OperationCount} 条指令。"
                    : $"本次将删除 {draft.DeletedProcessCount} 个流程、创建 {draft.CreatedProcessCount} 个流程、替换 {draft.ReplacedProcessCount} 个流程、变更 {draft.ChangedVariableCount} 个变量，共 {draft.OperationCount} 条指令。")
            };
        }

        private static JArray BuildChangeSetAllowedTransitions(PreviewApprovalRecord record)
        {
            var allowedTransitions = new JArray();
            if (record.Confirmed)
            {
                allowedTransitions.Add(new JObject
                {
                    ["tool"] = "apply_change_set",
                    ["arguments"] = new JObject { ["previewId"] = record.PreviewId }
                });
            }
            else
            {
                allowedTransitions.Add(new JObject
                {
                    ["state"] = "awaiting_foreground_confirmation"
                });
            }
            allowedTransitions.Add(new JObject
            {
                ["tool"] = "preview_change_set",
                ["requiredArguments"] = new JArray("changeSet", "replacePreviewId"),
                ["fixedArguments"] = new JObject { ["replacePreviewId"] = record.PreviewId },
                ["changeSetMode"] = "complete_replacement",
                ["previousPreviewActionsInherited"] = false,
                ["previousPreviewLocalKeysInherited"] = false
            });
            allowedTransitions.Add(new JObject
            {
                ["tool"] = "discard_change_set_preview",
                ["arguments"] = new JObject { ["previewId"] = record.PreviewId }
            });
            return allowedTransitions;
        }

        private bool TryBuildLocalKeyScopeRecovery(
            AiChangeSet incoming,
            string validationError,
            out JObject recovery)
        {
            recovery = null;
            PreviewApprovalRecord active;
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                active = previewRecords.Values.FirstOrDefault(item => item != null
                    && item.IsChangeSetPreview
                    && !item.Rejected
                    && item.ExpiresAtUtc > DateTime.UtcNow);
            }
            if (active?.Patch == null)
            {
                return false;
            }

            AiChangeSet activeChangeSet;
            try
            {
                activeChangeSet = active.Patch.ToObject<AiChangeSet>();
            }
            catch (JsonException)
            {
                return false;
            }
            var declaredKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ChangeSetAction action in activeChangeSet?.Actions ?? new List<ChangeSetAction>())
            {
                AddLocalKey(declaredKeys, "process", action?.Process?.Key);
                AddLocalKey(declaredKeys, "step", action?.Step?.Key);
                AddLocalKey(declaredKeys, "operation", action?.Operation?.Key);
            }

            var references = new List<JObject>();
            foreach (ChangeSetAction action in incoming?.Actions ?? new List<ChangeSetAction>())
            {
                if (action == null) continue;
                AddLocalKeyReference(references, "process", action.TargetProcess?.Key, "targetProcess.key");
                AddLocalKeyReference(references, "step", action.TargetStep?.Key, "targetStep.key");
                AddLocalKeyReference(references, "operation", action.TargetOperation?.Key, "targetOperation.key");
                string positionKind = action.Type != null && action.Type.StartsWith("step.", StringComparison.Ordinal)
                    ? "step"
                    : "operation";
                AddLocalKeyReference(references, positionKind, action.Position?.BeforeKey, "position.beforeKey");
                AddLocalKeyReference(references, positionKind, action.Position?.AfterKey, "position.afterKey");
                AddOperationTargetReferences(references, action.Operation?.Target, "operation.target");
                AddOperationTargetReferences(references, action.Operation?.WhenTrue, "operation.whenTrue");
                AddOperationTargetReferences(references, action.Operation?.WhenFalse, "operation.whenFalse");
            }
            JObject matched = references.FirstOrDefault(item =>
            {
                string key = item["key"]?.Value<string>();
                string kind = item["kind"]?.Value<string>();
                return declaredKeys.Contains(kind + ":" + key)
                    && !string.IsNullOrWhiteSpace(validationError)
                    && validationError.IndexOf(key, StringComparison.Ordinal) >= 0;
            });
            if (matched == null)
            {
                return false;
            }

            recovery = new JObject
            {
                ["validationError"] = validationError ?? string.Empty,
                ["reason"] = "local_key_belongs_to_uncommitted_preview",
                ["retryableWhen"] = "configuration_saved_or_complete_replacement_previewed",
                ["sideEffects"] = "none",
                ["configurationSaved"] = false,
                ["localKey"] = matched.DeepClone(),
                ["activePreview"] = new JObject
                {
                    ["previewId"] = active.PreviewId,
                    ["confirmed"] = active.Confirmed,
                    ["status"] = active.Confirmed ? "confirmed" : "awaiting_confirmation",
                    ["objectState"] = "preview_only",
                    ["configurationSaved"] = false,
                    ["localKeyScope"] = "current_change_set"
                },
                ["allowedTransitions"] = BuildChangeSetAllowedTransitions(active)
            };
            return true;
        }

        private static void AddLocalKey(HashSet<string> keys, string kind, string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                keys.Add(kind + ":" + key.Trim());
            }
        }

        private static void AddLocalKeyReference(List<JObject> references, string kind, string key, string path)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                references.Add(new JObject
                {
                    ["kind"] = kind,
                    ["key"] = key.Trim(),
                    ["path"] = path
                });
            }
        }

        private static void AddOperationTargetReferences(
            List<JObject> references,
            OperationTarget target,
            string path)
        {
            if (target == null) return;
            AddLocalKeyReference(references, "step", target.StepKey, path + ".stepKey");
            AddLocalKeyReference(references, "operation", target.OperationKey, path + ".operationKey");
        }

        private static JArray BuildPreviewOnlyView(JArray source, HashSet<string> createdPreviewProcIds)
        {
            var result = source?.DeepClone() as JArray ?? new JArray();
            foreach (JObject item in result.OfType<JObject>())
            {
                string procId = item["procId"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(procId)
                    && createdPreviewProcIds.Contains(procId)
                    && item["procIndex"] != null)
                {
                    item["plannedProcIndex"] = item["procIndex"];
                    item.Remove("procIndex");
                    item["objectState"] = "preview_only";
                }
            }
            return result;
        }

        private static AiResourceSnapshot BuildAiResourceSnapshot()
        {
            var ioResources = new Dictionary<string, AiIoResource>(StringComparer.Ordinal);
            if (SF.frmIO?.DicIO != null)
            {
                foreach (KeyValuePair<string, IO> item in SF.frmIO.DicIO)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key) && item.Value != null)
                    {
                        ioResources[item.Key] = new AiIoResource
                        {
                            IoType = item.Value.IOType ?? string.Empty,
                            CardNum = item.Value.CardNum,
                            IoIndex = item.Value.IOIndex ?? string.Empty
                        };
                    }
                }
            }
            string[] tcpNames = (SF.communicationStore?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
            string[] serialNames = (SF.communicationStore?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
            string[] alarmInfoIds = (SF.alarmInfoStore?.Alarms ?? new System.ComponentModel.BindingList<AlarmInfo>())
                .Where(item => item != null
                    && !string.IsNullOrWhiteSpace(item.Name)
                    && !string.IsNullOrWhiteSpace(item.Note))
                .Select(item => item.Index.ToString(CultureInfo.InvariantCulture))
                .ToArray();
            string[] plcNames = (SF.plcStore?.GetSnapshot().Devices ?? new List<PlcDeviceConfig>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
            var references = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
            {
                ["comm.tcp"] = tcpNames,
                ["comm.serial"] = serialNames,
                ["comm.all"] = tcpNames.Concat(serialNames).Distinct(StringComparer.Ordinal).ToArray(),
                ["alarm.infoId"] = alarmInfoIds,
                ["plc.device"] = plcNames
            };
            return new AiResourceSnapshot(ioResources, references);
        }

        private JObject HandleApplyChangeSet(JObject request)
        {
            string previewId = ReadRequiredString(request, "previewId");
            ValidateConfirmedManagePreview(previewId);
            AiChangeSetCompileResult draft;
            string expectedStateHash;
            JArray changes;
            lock (previewLock)
            {
                if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record)
                    || record.AiChangeSetPreview == null)
                {
                    throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND",
                        $"语义变更集预演不存在或已过期：{previewId}");
                }
                draft = record.AiChangeSetPreview;
                expectedStateHash = record.BaseStateHash;
                changes = draft.Changes == null ? new JArray() : (JArray)draft.Changes.DeepClone();
            }

            Dictionary<string, DicValue> currentVariables = SF.valueStore?.BuildSaveData()
                ?? throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            string currentStateHash = AiChangeSetCompiler.ComputeStateHash(SF.frmProc.procsList, currentVariables);
            if (!string.Equals(expectedStateHash, currentStateHash, StringComparison.Ordinal))
            {
                throw new BridgeRequestException(409, "CHANGE_SET_VERSION_MISMATCH",
                    "预演后的流程或变量配置已经变化，本次提交未执行。",
                    new JObject
                    {
                        ["reason"] = "base_state_changed",
                        ["retryableWhen"] = "new_preview_created_from_current_state",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
            }

            CommitChangeSet(draft);
            RemovePreview(previewId);
            var createdProcesses = new JArray();
            var affectedProcesses = new JArray();
            var createdProcIds = new HashSet<string>(
                (draft.CreatedObjects?["processes"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .Select(item => item["procId"]?.Value<string>())
                    .Where(value => !string.IsNullOrEmpty(value)),
                StringComparer.OrdinalIgnoreCase);
            foreach (JObject analysis in (draft.ProcessAnalyses ?? new JArray()).OfType<JObject>())
            {
                int procIndex = analysis["procIndex"]?.Value<int>() ?? -1;
                if (procIndex >= 0)
                {
                    string procId = analysis["procId"]?.Value<string>() ?? string.Empty;
                    string changeType = createdProcIds.Contains(procId)
                        ? "process.create"
                        : "configuration.affected";
                    var item = new JObject
                    {
                        ["procIndex"] = procIndex,
                        ["procId"] = procId,
                        ["name"] = analysis["name"]?.Value<string>() ?? string.Empty,
                        ["changeType"] = changeType,
                        ["readinessStatus"] = analysis["readinessStatus"]?.Value<string>() ?? "ready",
                        ["runnable"] = analysis["runnable"]?.Value<bool>() ?? true,
                        ["warnings"] = analysis["warnings"]?.DeepClone() ?? new JArray(),
                        ["runBlockers"] = analysis["runBlockers"]?.DeepClone() ?? new JArray()
                    };
                    affectedProcesses.Add(item);
                    if (string.Equals(changeType, "process.create", StringComparison.Ordinal))
                    {
                        createdProcesses.Add(item.DeepClone());
                    }
                }
            }
            return new JObject
            {
                ["previewId"] = previewId,
                ["committed"] = true,
                ["configurationSaved"] = true,
                ["status"] = "committed",
                ["localKeyScope"] = "closed_after_apply",
                ["procCount"] = draft.Processes.Count,
                ["variableCount"] = draft.Variables.Count,
                ["totalVariableCount"] = draft.Variables.Count,
                ["changedVariableCount"] = draft.ChangedVariableCount,
                ["variableResolutions"] = draft.VariableResolutions?.DeepClone() ?? new JArray(),
                ["createdProcesses"] = createdProcesses,
                ["affectedProcesses"] = affectedProcesses,
                ["createdObjects"] = draft.CreatedObjects?.DeepClone() ?? new JObject
                {
                    ["processes"] = new JArray(),
                    ["steps"] = new JArray(),
                    ["operations"] = new JArray()
                },
                ["processAnalyses"] = draft.ProcessAnalyses?.DeepClone() ?? new JArray(),
                ["readinessStatus"] = draft.ReadinessStatus,
                ["runnable"] = draft.Runnable,
                ["warnings"] = draft.ConfigurationWarnings?.DeepClone() ?? new JArray(),
                ["runBlockers"] = draft.RunBlockers?.DeepClone() ?? new JArray(),
                ["changes"] = changes,
                ["message"] = "语义变更集已按冻结预演原子提交。"
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
                if (SF.DR == null || SF.DR.Context?.Procs == null)
                {
                    throw new BridgeRequestException(503, "RUNTIME_NOT_READY", "流程运行时尚未初始化。");
                }
                if (procIndex < 0 || procIndex >= SF.DR.Context.Procs.Count)
                {
                    throw new BridgeRequestException(404, "PROC_NOT_FOUND", $"流程索引不存在：{procIndex}");
                }
                Guid currentProcId = SF.DR.Context.Procs[procIndex]?.head?.Id ?? Guid.Empty;
                if (expectedProcId == Guid.Empty) expectedProcId = currentProcId;
                else if (currentProcId != expectedProcId)
                {
                    throw new BridgeRequestException(409, "PROC_ID_CHANGED",
                        $"等待期间流程索引 {procIndex} 已指向其他流程，已停止等待以避免误判。");
                }
                snapshot = SF.DR.GetSnapshot(procIndex);
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
                EngineSnapshot before = SF.DR.GetSnapshot(procIndex);
                if (before != null && before.State != ProcRunState.Stopped)
                {
                    throw new BridgeRequestException(409, "PROC_ALREADY_RUNNING",
                        $"流程 {procIndex} 已处于 {before.State}，测试运行不会接管已有运行实例。");
                }
                procId = proc.head?.Id ?? Guid.Empty;
                procName = proc.head?.Name ?? string.Empty;
                if (!SF.DR.StartProc(proc, procIndex))
                {
                    string startError = SF.DR.TryValidateProcessStart(proc, procIndex, out string gateError)
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
                    snapshot = SF.DR?.GetSnapshot(procIndex);
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
                    EngineSnapshot current = SF.DR?.GetSnapshot(procIndex);
                    if (current != null && current.ProcId == procId && current.State != ProcRunState.Stopped)
                    {
                        SF.DR.Stop(procIndex, requestedReason);
                        stoppedByTestRunner = true;
                    }
                    return true;
                });
            }

            DateTime stopDeadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                snapshot = SF.DR?.GetSnapshot(procIndex);
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
                ["runtimeEvidence"] = SF.mainfrm?.RuntimeBlackBoxRecorder?.BuildEvidencePackage(procIndex)
                    ?? RuntimeBlackBoxRecorder.BuildUnavailableEvidencePackage(procIndex)
            };
        }

        private void CommitChangeSet(AiChangeSetCompileResult draft)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("提交语义变更集");
            if (SF.MaintenanceActive)
            {
                throw new BridgeRequestException(423, "CONFIG_MAINTENANCE_ACTIVE",
                    string.IsNullOrWhiteSpace(SF.MaintenanceReason)
                        ? "系统正在执行配置维护。"
                        : $"系统正在执行配置维护：{SF.MaintenanceReason}");
            }
            if (SF.SecurityLocked)
            {
                throw new BridgeRequestException(423, "SECURITY_LOCKED", $"系统已安全锁定：{SF.SecurityLockReason}");
            }

            List<Proc> oldProcesses = SF.frmProc.procsList.Select(ObjectGraphCloner.Clone).ToList();
            Dictionary<string, DicValue> oldVariables = SF.valueStore.BuildSaveData();
            Dictionary<string, DicValue> commitVariables = draft.Variables
                .ToDictionary(item => item.Key, item => ObjectGraphCloner.Clone(item.Value), StringComparer.Ordinal);
            var currentById = oldVariables.Values
                .Where(value => value != null && value.Id != Guid.Empty)
                .ToDictionary(value => value.Id);
            ISet<Guid> explicitValueIds = new HashSet<Guid>(
                draft.VariableValueOverrides?.Keys ?? Enumerable.Empty<Guid>());
            foreach (DicValue variable in commitVariables.Values)
            {
                if (variable != null && !explicitValueIds.Contains(variable.Id)
                    && currentById.TryGetValue(variable.Id, out DicValue current))
                {
                    variable.Value = current.Value;
                }
            }
            if (!AiConfigurationTransaction.Commit(
                SF.ConfigPath, draft.Processes, commitVariables,
                out string commitError, out bool rollbackFailed))
            {
                if (rollbackFailed) SF.SetSecurityLock(commitError);
                throw new BridgeRequestException(
                    rollbackFailed ? 500 : 409,
                    rollbackFailed ? "CHANGE_SET_ROLLBACK_FAILED" : "CHANGE_SET_COMMIT_FAILED",
                    commitError,
                    new JObject
                    {
                        ["reason"] = rollbackFailed
                            ? "configuration_transaction_rollback_failed"
                            : "configuration_transaction_commit_failed",
                        ["retryableWhen"] = rollbackFailed
                            ? "security_lock_cleared_after_configuration_recovery"
                            : "server_configuration_transaction_fixed",
                        ["sideEffects"] = rollbackFailed ? "unknown" : "none"
                    }.ToString(Formatting.None));
            }
            try
            {
                SF.frmProc.RefreshProcList();
                ValueConfigStore.ValidateProcessOwners(
                    commitVariables.Values, SF.frmProc.procsList);
                SF.valueStore.ReplaceConfiguration(commitVariables);
                foreach (KeyValuePair<Guid, string> valueOverride in
                    draft.VariableValueOverrides ?? new Dictionary<Guid, string>())
                {
                    DicValue target = commitVariables.Values.FirstOrDefault(value =>
                        value != null && value.Id == valueOverride.Key);
                    if (target == null
                        || !SF.valueStore.setValueByName(target.Name, valueOverride.Value, "ChangeSet变量值提交"))
                    {
                        throw new InvalidOperationException(
                            $"变量当前值提交失败：{target?.Name ?? valueOverride.Key.ToString("D")}");
                    }
                }
                SF.frmValue?.FreshFrmValue();
            }
            catch (Exception ex)
            {
                bool diskRestored = AiConfigurationTransaction.Commit(
                    SF.ConfigPath, oldProcesses, oldVariables,
                    out string restoreError, out bool restoreRollbackFailed);
                bool memoryRestored = true;
                try
                {
                    SF.frmProc.RefreshProcList();
                    SF.valueStore.ReplaceConfiguration(oldVariables);
                    SF.frmValue?.FreshFrmValue();
                }
                catch
                {
                    memoryRestored = false;
                }
                if (!diskRestored || !memoryRestored || restoreRollbackFailed)
                {
                    string reason = $"语义变更集提交后刷新失败且回滚不完整：diskRestored={diskRestored}, memoryRestored={memoryRestored}, error={ex.Message}, restoreError={restoreError}";
                    SF.SetSecurityLock(reason);
                    throw new BridgeRequestException(500, "CHANGE_SET_ROLLBACK_FAILED", reason);
                }
                throw new BridgeRequestException(500, "CHANGE_SET_COMMIT_FAILED",
                    "语义变更集提交失败，流程与变量配置已恢复。", ex.Message);
            }
        }

        private JObject PreviewCreateProcBatch(JObject definition)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("批量创建流程");
            string name = ReadRequiredString(definition, "name");
            ValidateBatchName(name, "definition.name");
            if (SF.frmProc.procsList.Any(item => string.Equals(item?.head?.Name, name, StringComparison.Ordinal)))
            {
                throw new BridgeRequestException(409, "PROC_NAME_EXISTS", $"流程名称已存在：{name}");
            }
            JArray steps = ReadRequiredArray(definition, "steps");
            if (steps.Count == 0)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "steps 至少包含一个步骤。");
            }

            int procIndex = SF.frmProc.procsList.Count;
            var draft = new Proc
            {
                head = new ProcHead
                {
                    Name = name,
                    AutoStart = ReadOptionalBoolean(definition, "autoStart") ?? false,
                    Disable = ReadOptionalBoolean(definition, "disable") ?? false
                },
                steps = new List<Step>()
            };
            var result = new PatchExecutionResult { ProcIndex = procIndex, Proc = draft };
            int operationCount = 0;
            for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                if (!(steps[stepIndex] is JObject stepDefinition))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"steps[{stepIndex}] 必须是 JSON 对象。");
                }
                var step = new Step
                {
                    Id = Guid.NewGuid(),
                    Name = ReadRequiredString(stepDefinition, "name"),
                    Disable = ReadOptionalBoolean(stepDefinition, "disable") ?? false,
                    Ops = new List<OperationType>()
                };
                ValidateBatchName(step.Name, $"steps[{stepIndex}].name");
                draft.steps.Add(step);
                result.Changes.Add(new JObject
                {
                    ["type"] = "append_step",
                    ["stepIndex"] = stepIndex,
                    ["stepId"] = step.Id.ToString("D"),
                    ["name"] = step.Name
                });
                JArray operations = ReadOptionalArray(stepDefinition, "operations") ?? new JArray();
                operationCount += operations.Count;
                for (int opIndex = 0; opIndex < operations.Count; opIndex++)
                {
                    if (!(operations[opIndex] is JObject operation))
                    {
                        throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"steps[{stepIndex}].operations[{opIndex}] 必须是 JSON 对象。");
                    }
                    JObject action = new JObject
                    {
                        ["operaType"] = ReadRequiredString(operation, "operaType")
                    };
                    JObject fieldValues = ReadOptionalObject(operation, "fieldValues");
                    if (fieldValues != null)
                    {
                        int fieldValuesBytes = Encoding.UTF8.GetByteCount(fieldValues.ToString(Formatting.None));
                        if (fieldValuesBytes > MaxBatchFieldValuesUtf8Bytes)
                        {
                            throw new BridgeRequestException(413, "FIELD_VALUES_TOO_LARGE", $"steps[{stepIndex}].operations[{opIndex}].fieldValues 超过 {MaxBatchFieldValuesUtf8Bytes / 1024} KB 上限。");
                        }
                        action["fieldValues"] = fieldValues.DeepClone();
                    }
                    InsertOperationCore(action, draft, step, result, operationCount, step.Ops.Count, "append_operation");
                }
            }
            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(procIndex, draft, errors);
            if (errors.Count > 0)
            {
                throw new BridgeRequestException(400, "PROC_VALIDATE_FAILED", "流程变更集校验失败。", string.Join("\r\n", errors.Distinct()));
            }

            string previewId = RegisterManagePreview(definition);
            lock (previewLock)
            {
                previewRecords[previewId].DraftProc = ObjectGraphCloner.Clone(draft);
            }
            return new JObject
            {
                ["action"] = "create_proc_batch",
                ["procName"] = name,
                ["targetIndex"] = procIndex,
                ["stepCount"] = draft.steps.Count,
                ["operationCount"] = operationCount,
                ["changes"] = result.Changes,
                ["messages"] = new JArray($"将创建流程「{name}」，包含 {draft.steps.Count} 个步骤、{operationCount} 条指令。"),
                ["previewId"] = previewId,
                ["confirmed"] = SF.frmAiAssistant?.IsAutoApproveMode == true,
                ["committed"] = false
            };
        }

        private static void ValidateBatchName(string value, string fieldPath)
        {
            if (value.Length > MaxBatchNameLength)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldPath} 最长 {MaxBatchNameLength} 个字符。");
            }
        }

        private JObject ExecuteDeleteProcs(JObject request)
        {
            JArray indexes = ReadRequiredArray(request, "procIndexes");
            // 从大到小删除，避免索引移位
            var sortedIndexes = indexes.Select(t => t.Value<int>()).OrderByDescending(i => i).ToList();
            EnsureAllProcsStoppedForAiStructureCommit("删除流程");

            List<Proc> draftProcesses = SF.frmProc.procsList
                .Select(ObjectGraphCloner.Clone).ToList();
            Dictionary<string, DicValue> draftVariables = SF.valueStore.BuildSaveData();
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
            if (minDeleted < SF.frmProc.procsList.Count)
            {
                NotifyProcChanged(minDeleted, ProcChangeKind.Deleted);
            }
            else if (SF.frmProc.procsList.Count > 0)
            {
                NotifyProcChanged(SF.frmProc.procsList.Count - 1, ProcChangeKind.Deleted);
            }

            return new JObject
            {
                ["action"] = "delete_procs",
                ["deleted"] = deleted,
                ["deletedPrivateVariableCount"] = deletedPrivateVariableCount,
                ["remainingCount"] = SF.frmProc.procsList.Count,
                ["messages"] = new JArray { $"已删除 {deleted.Count} 个流程，剩余 {SF.frmProc.procsList.Count} 个" }
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject ExecuteReorderProc(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int targetIndex = ReadRequiredInt(request, "targetIndex");
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"流程索引 {procIndex} 越界。");
            }
            if (targetIndex < 0 || targetIndex >= SF.frmProc.procsList.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"目标索引 {targetIndex} 越界。");
            }
            if (procIndex == targetIndex)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "源索引与目标索引相同。");
            }
            EnsureAllProcsStoppedForAiStructureCommit("调整流程顺序");

            Proc proc = SF.frmProc.procsList[procIndex];
            string procName = proc?.head?.Name ?? string.Empty;
            SF.frmProc.procsList.RemoveAt(procIndex);
            SF.frmProc.procsList.Insert(targetIndex, proc);

            // 重建工作配置
            int minIndex = Math.Min(procIndex, targetIndex);
            if (!SF.frmProc.RebuildWorkConfig(minIndex))
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

        private static void EnsureAllProcsStoppedForAiStructureCommit(string actionName)
        {
            int procCount = SF.frmProc?.procsList?.Count ?? 0;
            for (int procIndex = 0; procIndex < procCount; procIndex++)
            {
                EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
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
            if (SF.frmProc.procsList.Any(proc => string.Equals(
                proc?.head?.Name, copy.head.Name, StringComparison.Ordinal)))
            {
                throw new BridgeRequestException(409, "PROC_NAME_EXISTS", $"流程名称已存在：{copy.head.Name}");
            }

            int newProcIndex = SF.frmProc.procsList.Count;
            // 重置流程内步骤和指令的 Id，避免重复
            ResetProcStepOpIds(copy);

            List<Proc> draftProcesses = SF.frmProc.procsList
                .Select(ObjectGraphCloner.Clone).ToList();
            Dictionary<string, DicValue> draftVariables = SF.valueStore.BuildSaveData();
            ProcessVariableCopyResult variableCopy = ProcessVariableLifecycleService.CopyPrivateVariables(
                source.head.Id, copy.head.Id, copy, draftVariables);
            draftProcesses.Add(copy);

            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(newProcIndex, copy, errors);
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

        private static string ResolveCopiedProcessName(string sourceName, string requestedName)
        {
            if (!string.IsNullOrWhiteSpace(requestedName)) return requestedName.Trim();
            string basis = string.IsNullOrWhiteSpace(sourceName) ? "流程" : sourceName;
            for (int number = 1; ; number++)
            {
                string candidate = basis + (number == 1 ? "_副本" : "_副本" + number);
                if (!(SF.frmProc?.procsList ?? new List<Proc>()).Any(proc =>
                    string.Equals(proc?.head?.Name, candidate, StringComparison.Ordinal)))
                {
                    return candidate;
                }
            }
        }

        private static JObject BuildProcessReadinessJObject(int procIndex)
        {
            Proc proc = procIndex >= 0 && procIndex < (SF.frmProc?.procsList?.Count ?? 0)
                ? SF.frmProc.procsList[procIndex]
                : null;
            ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                procIndex, proc, SF.frmProc?.procsList);
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
            bool autoConfirmed = SF.frmAiAssistant?.IsAutoApproveMode == true;
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

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandlePreviewPatch(JObject request)
        {
            PatchExecutionResult result = ExecutePatch(request);
            return BuildRegisteredPatchPreview(request, result);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleApplyPatch(JObject request)
        {
            string previewId = ReadRequiredString(request, "previewId");
            ValidateConfirmedPreview(previewId, request);
            PatchExecutionResult result = GetStoredPatchResult(previewId);
            CommitPatch(result.ProcIndex, result.Proc, result.AffectedOps);
            RemovePreview(previewId);

            Proc current = GetProcByIndex(result.ProcIndex);
            JObject apply = BuildPatchResult("apply", new PatchExecutionResult
            {
                ProcIndex = result.ProcIndex,
                Proc = current,
                Messages = result.Messages,
                Changes = result.Changes
            });
            apply["previewId"] = previewId;
            return apply;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetRuntimeSnapshot(JObject request)
        {
            EnsureRuntimeReady();
            int? procIndex = ReadOptionalInt(request, "procIndex");
            JArray snapshots = new JArray();
            if (procIndex.HasValue)
            {
                GetProcByIndex(procIndex.Value);
                snapshots.Add(BuildEngineSnapshot(SF.DR?.GetSnapshot(procIndex.Value), procIndex.Value));
            }
            else
            {
                for (int i = 0; i < SF.frmProc.procsList.Count; i++)
                {
                    snapshots.Add(BuildEngineSnapshot(SF.DR?.GetSnapshot(i), i));
                }
            }

            return new JObject
            {
                ["securityLocked"] = SF.SecurityLocked,
                ["procConfigFaulted"] = SF.ProcConfigFaulted,
                ["procCount"] = SF.frmProc.procsList.Count,
                ["selected"] = new JObject
                {
                    ["procIndex"] = SF.frmProc.SelectedProcNum,
                    ["stepIndex"] = SF.frmProc.SelectedStepNum,
                    ["opIndex"] = SF.frmDataGrid?.iSelectedRow ?? -1
                },
                ["snapshots"] = snapshots
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetInfoLogTail(JObject request)
        {
            int maxCount = ReadOptionalInt(request, "maxCount") ?? 50;
            if (maxCount <= 0 || maxCount > 200)
            {
                return BridgeError(400, "INVALID_ARGUMENT", "maxCount 必须在 1..200 范围内。");
            }

            JArray items = new JArray();
            foreach (FrmInfo.InfoLogSnapshot item in SF.frmInfo?.GetInfoLogTail(maxCount) ?? new List<FrmInfo.InfoLogSnapshot>())
            {
                items.Add(new JObject
                {
                    ["time"] = item.TimeText,
                    ["message"] = item.Message,
                    ["level"] = item.Level.ToString()
                });
            }

            return new JObject
            {
                ["maxCount"] = maxCount,
                ["items"] = items
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleDiagnoseProc(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
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

            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
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
                    ["findingCount"] = findings.Count
                },
                ["runtime"] = BuildEngineSnapshot(snapshot, procIndex),
                ["findings"] = findings
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

        private static string GetDiagnosticIndexRevision()
        {
            var builder = new StringBuilder();
            IList<Proc> procs = SF.frmProc?.procsList;
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
                if ((SF.valueStore?.GetValueNames() ?? new List<string>()).Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("value");
                if ((SF.frmIO?.IoItems ?? new List<string>()).Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("io");
                CommReferenceCatalog communications = GetCommNames();
                if (communications.Tcp.Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("comm.tcp");
                if (communications.Serial.Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("comm.serial");
                if ((SF.frmCard?.dataStation ?? new List<DataStation>()).Any(item => item?.Name == name))
                    resolvedTypes.Add("station");
                if ((SF.plcStore?.GetSnapshot().Devices ?? new List<PlcDeviceConfig>()).Any(item => item?.Name == name))
                    resolvedTypes.Add("plc.device");
                if ((SF.dataStructStore?.GetStructNames() ?? new List<string>()).Contains(name, StringComparer.Ordinal))
                    resolvedTypes.Add("dataStruct");
                if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int alarmIndex)
                    && SF.alarmInfoStore?.GetValidIndices().Contains(alarmIndex) == true)
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
            int procCount = SF.frmProc.procsList.Count;
            int procEnd = Math.Min(procCount, procOffset + procLimit);
            string indexRevision = GetDiagnosticIndexRevision();
            int total = 0;
            var matches = new JArray();
            for (int pi = procOffset; pi < procEnd; pi++)
            {
                Proc proc = SF.frmProc.procsList[pi];
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
                SF.valueStore?.TryGetValueByName(value, out tracedVariable);
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

            int procCount = SF.frmProc.procsList.Count;
            int procEnd = Math.Min(procCount, procOffset + procLimit);
            string indexRevision = GetDiagnosticIndexRevision();
            int totalMatchesInBatch = 0;
            var matches = new JArray();
            for (int pi = procOffset; pi < procEnd; pi++)
            {
                Proc proc = SF.frmProc.procsList[pi];
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

            int procCount = SF.frmProc.procsList.Count;
            int procEnd = Math.Min(procCount, procOffset + procLimit);
            int incomingCount = 0;
            var incoming = new JArray();
            for (int procIndex = procOffset; procIndex < procEnd; procIndex++)
            {
                Proc proc = SF.frmProc.procsList[procIndex];
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

            int procCount = SF.frmProc.procsList.Count;
            int procEnd = Math.Min(procCount, procOffset + procLimit);
            int matchCount = 0;
            var matches = new JArray();
            for (int procIndex = procOffset; procIndex < procEnd; procIndex++)
            {
                Proc proc = SF.frmProc.procsList[procIndex];
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

        private static JObject BuildDiagnosticMatch(DiagnosticFieldRecord field, bool truncateValue)
        {
            string value = field.Value ?? string.Empty;
            if (truncateValue && value.Length > 300) value = value.Substring(0, 300);
            return new JObject
            {
                ["procIndex"] = field.ProcIndex,
                ["procId"] = (SF.frmProc?.procsList != null
                    && field.ProcIndex >= 0
                    && field.ProcIndex < SF.frmProc.procsList.Count)
                    ? SF.frmProc.procsList[field.ProcIndex]?.head?.Id.ToString("D") ?? string.Empty
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
            int findingLimit = ReadOptionalInt(request, "findingLimit") ?? 100;
            if (procOffset < 0 || procLimit < 1 || procLimit > 50 || findingLimit < 1 || findingLimit > 200)
            {
                return BridgeError(400, "INVALID_ARGUMENT",
                    "procOffset 必须大于等于0，procLimit 必须在1..50，findingLimit 必须在1..200。");
            }
            int procCount = SF.frmProc.procsList.Count;
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
                Proc proc = SF.frmProc.procsList[pi];
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
            JObject validation = HandleValidateProc(new JObject { ["procIndex"] = procIndex });
            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
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
                ["runtimeEvidence"] = SF.mainfrm?.RuntimeBlackBoxRecorder?.BuildEvidencePackage(procIndex)
                    ?? RuntimeBlackBoxRecorder.BuildUnavailableEvidencePackage(procIndex)
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
                || SF.frmProc == null
                || procIndex >= SF.frmProc.procsList.Count)
            {
                return false;
            }

            Proc proc = SF.frmProc.procsList[procIndex];
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

            Proc proc = GetProcByIndex(procIndex);
            if (proc.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                return BridgeError(400, "STEP_NOT_FOUND", $"步骤索引越界：{stepIndex}");
            }
            Step step = proc.steps[stepIndex];
            if ((step?.Ops?.Count ?? 0) > MaxStepDetailOperationCount)
            {
                return BridgeError(413, "STEP_DETAIL_TOO_LARGE",
                    $"步骤包含{step.Ops.Count}条指令，超过详情上限{MaxStepDetailOperationCount}；请使用get_operation_context按位置读取。");
            }

            JArray opDetails = new JArray();
            if (step.Ops != null)
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

            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procName"] = proc.head?.Name ?? string.Empty,
                ["stepIndex"] = stepIndex,
                ["stepId"] = step.Id.ToString("D"),
                ["stepName"] = step.Name ?? string.Empty,
                ["stepDisable"] = step.Disable,
                ["opCount"] = step.Ops?.Count ?? 0,
                ["operations"] = opDetails
            };
        }

        // 按条件搜索指令：支持按流程范围、指令类型、关键词（指令名/字段值）过滤。
        // 用于快速定位"哪些指令引用了变量X""哪些是跳转类指令""哪些IO操作用了Y"等问题。
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleSearchOperations(JObject request)
        {
            int? procIndex = ReadOptionalInt(request, "procIndex");
            string operaType = ReadOptionalString(request, "operaType");
            string keyword = ReadOptionalString(request, "keyword");

            IList<Proc> procs = SF.frmProc?.procsList;
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
                        results.Add(new JObject
                        {
                            ["procIndex"] = pi,
                            ["procName"] = proc.head?.Name ?? string.Empty,
                            ["stepIndex"] = si,
                            ["stepName"] = step.Name ?? string.Empty,
                            ["opIndex"] = oi,
                            ["opName"] = op.Name ?? string.Empty,
                            ["operaType"] = op.OperaType ?? string.Empty,
                            ["disable"] = op.Disable,
                            ["summary"] = BuildOperationSummary(op)
                        });

                        // 限制返回数量避免响应过大
                        if (results.Count >= 200)
                        {
                            goto done;
                        }
                    }
                }
            }
            done:

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
                ["truncated"] = results.Count >= 200,
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
                procIndex, proc, SF.frmProc?.procsList);

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
                preview["confirmed"] = SF.frmAiAssistant?.IsAutoApproveMode == true;
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
                preview["confirmed"] = SF.frmAiAssistant?.IsAutoApproveMode == true;
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
                preview["confirmed"] = SF.frmAiAssistant?.IsAutoApproveMode == true;
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
                preview["confirmed"] = SF.frmAiAssistant?.IsAutoApproveMode == true;
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
            ValueConfigStore store = SF.valueStore;
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
            int limit = request["limit"]?.Value<int>() ?? 0;
            if (offset < 0) offset = 0;
            if (limit <= 0) limit = 1000;

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
                ["items"] = new JArray(items)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetVariable(JObject request)
        {
            EnsureRuntimeReady();
            ValueConfigStore store = SF.valueStore;
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
            ValueConfigStore store = SF.valueStore;
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
            ValueConfigStore store = SF.valueStore;
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
                store.setValueByIndex(val.Index, dval);
            }
            else
            {
                store.setValueByIndex(val.Index, newValue);
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
            ValueConfigStore store = SF.valueStore;
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
            if (!store.TryCommitConfiguration(SF.ConfigPath, draft, out string commitError))
            {
                return BridgeError(500, "VARIABLE_COMMIT_FAILED", commitError);
            }
            SF.frmValue?.FreshFrmValue();
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
            ValueConfigStore store = SF.valueStore;
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
            if (!store.TryCommitConfiguration(SF.ConfigPath, draft, out string commitError))
            {
                return BridgeError(500, "VARIABLE_COMMIT_FAILED", commitError);
            }
            SF.frmValue?.FreshFrmValue();
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
            ValueConfigStore store = SF.valueStore;
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
                SF.ConfigPath,
                draft,
                out string commitError,
                valueOverrides,
                "AI变量配置更新"))
            {
                return BridgeError(500, "VARIABLE_COMMIT_FAILED", commitError);
            }
            SF.frmValue?.FreshFrmValue();
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
        private static DataStation ResolveStation(int stationIndex)
        {
            if (SF.frmCard == null || SF.frmCard.dataStation == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "工站存储未初始化。");
            }
            List<DataStation> list = SF.frmCard.dataStation;
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
        private static AlarmInfo ResolveAlarm(int index)
        {
            AlarmInfoStore store = SF.alarmInfoStore;
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

        private static void SaveStationAndRefresh()
        {
            AtomicJsonFileStore.Save(SF.ConfigPath, "DataStation", SF.frmCard?.dataStation);
            SF.frmCard?.RefreshStationList();
            SF.frmCard?.RefreshStationTree();
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListStations(JObject request)
        {
            EnsureRuntimeReady();
            if (SF.frmCard?.dataStation == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "工站存储未初始化。");
            }
            JArray array = new JArray();
            List<DataStation> list = SF.frmCard.dataStation;
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
            if (SF.frmCard?.dataStation == null)
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
            List<DataStation> list = SF.frmCard.dataStation;
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
            List<DataStation> list = SF.frmCard.dataStation;
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
                List<DataStation> list = SF.frmCard.dataStation;
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

        private static JObject BuildVariableJObject(DicValue val)
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

        private static bool ProcessExists(Guid procId)
        {
            return procId != Guid.Empty && (SF.frmProc?.procsList ?? new List<Proc>())
                .Any(proc => proc?.head?.Id == procId);
        }

        private static string ResolveProcessName(Guid? procId)
        {
            if (!procId.HasValue) return string.Empty;
            return (SF.frmProc?.procsList ?? new List<Proc>())
                .FirstOrDefault(proc => proc?.head?.Id == procId.Value)?.head?.Name ?? string.Empty;
        }

        private static JObject BuildVariableReferenceImpact(DicValue variable)
        {
            int nameReferences = 0;
            int indexReferences = 0;
            int inaccessibleReferences = 0;
            foreach (Proc proc in SF.frmProc?.procsList ?? new List<Proc>())
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
            DataStructStore store = SF.dataStructStore;
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
            DataStructStore store = SF.dataStructStore;
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
            DataStructStore store = SF.dataStructStore;
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
            DataStructStore store = SF.dataStructStore;
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
            if (!SF.dataStructStore.TryUpsertAndSave(
                candidate, SF.ConfigPath, out bool created, out string error))
            {
                throw new BridgeRequestException(400, "DATA_STRUCT_SAVE_FAILED", error);
            }
            SF.frmdataStruct?.RefreshDataSturctList();
            SF.frmdataStruct?.RefreshDataSturctTree();
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
            if (!SF.dataStructStore.TryDeleteAndSave(name, SF.ConfigPath, out string error))
            {
                throw new BridgeRequestException(400, "DATA_STRUCT_DELETE_FAILED", error);
            }
            SF.frmdataStruct?.RefreshDataSturctList();
            SF.frmdataStruct?.RefreshDataSturctTree();
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

        private JObject HandleGetMigrationConfiguration(JObject request)
        {
            EnsureRuntimeReady();
            string domain = ReadRequiredString(request, "domain").Trim().ToLowerInvariant();
            switch (domain)
            {
                case "motion_io":
                    return new JObject
                    {
                        ["domain"] = domain,
                        ["definition"] = JObject.FromObject(
                            BuildMotionIoMigrationDefinition(),
                            JsonSerializer.Create(migrationContractJsonSettings))
                    };
                case "io_debug":
                    return new JObject
                    {
                        ["domain"] = domain,
                        ["definition"] = JObject.FromObject(
                            BuildIoDebugMigrationDefinition(),
                            JsonSerializer.Create(migrationContractJsonSettings))
                    };
                case "plc":
                    return new JObject
                    {
                        ["domain"] = domain,
                        ["definition"] = JObject.FromObject(
                            BuildPlcMigrationDefinition(),
                            JsonSerializer.Create(migrationContractJsonSettings))
                    };
                case "communication":
                    return new JObject
                    {
                        ["domain"] = domain,
                        ["definition"] = JObject.FromObject(
                            BuildCommunicationMigrationDefinition(),
                            JsonSerializer.Create(migrationContractJsonSettings))
                    };
                default:
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT",
                        "domain 仅支持 motion_io、io_debug、plc、communication。");
            }
        }

        private static MotionIoMigrationDefinition BuildMotionIoMigrationDefinition()
        {
            var definition = new MotionIoMigrationDefinition();
            foreach (FrmCard.ControlCard card in SF.cardStore?.CardData?.controlCards
                ?? new List<FrmCard.ControlCard>())
            {
                definition.ControlCards.Add(new ControlCardMigrationDefinition
                {
                    CardType = card?.cardHead?.CardType ?? string.Empty,
                    InputCount = card?.cardHead?.InputCount ?? 0,
                    OutputCount = card?.cardHead?.OutputCount ?? 0,
                    Axes = (card?.axis ?? new List<FrmCard.Axis>()).Select(axis =>
                        new AxisMigrationDefinition
                        {
                            Name = axis?.AxisName ?? string.Empty,
                            PulseToMm = axis?.PulseToMM ?? 0,
                            HomeDirection = axis?.HomeDirection ?? string.Empty,
                            HomeSpeed = axis?.HomeSpeed ?? string.Empty,
                            SpeedInfo = axis?.SpeedInfo ?? 0,
                            MaxSpeed = axis?.SpeedMax ?? 0,
                            AccelerationTime = axis?.AccMax ?? 0,
                            DecelerationTime = axis?.DecMax ?? 0
                        }).ToList()
                });
            }
            List<List<IO>> ioMap = SF.frmIO?.IOMap ?? new List<List<IO>>();
            for (int cardIndex = 0; cardIndex < ioMap.Count; cardIndex++)
            {
                foreach (IO io in ioMap[cardIndex] ?? new List<IO>())
                {
                    definition.IoMappings.Add(new IoMigrationDefinition
                    {
                        Name = io?.Name ?? string.Empty,
                        CardIndex = cardIndex,
                        Module = io?.Module ?? 0,
                        IoIndex = io?.IOIndex ?? string.Empty,
                        IoType = io?.IOType ?? string.Empty,
                        UsedType = io?.UsedType ?? string.Empty,
                        EffectLevel = io?.EffectLevel ?? string.Empty,
                        Note = io?.Note ?? string.Empty,
                        IsRemark = io?.IsRemark ?? false
                    });
                }
            }
            return definition;
        }

        private static IoDebugMigrationDefinition BuildIoDebugMigrationDefinition()
        {
            IODebugMap map = SF.frmIODebug?.IODebugMaps ?? new IODebugMap();
            return new IoDebugMigrationDefinition
            {
                InputNames = (map.inputs ?? new List<IO>()).Select(io => io?.Name ?? string.Empty).ToList(),
                OutputNames = (map.outputs ?? new List<IO>()).Select(io => io?.Name ?? string.Empty).ToList(),
                Group1 = BuildIoDebugConnectionDefinitions(map.iOConnects),
                Group2 = BuildIoDebugConnectionDefinitions(map.iOConnects2),
                Group3 = BuildIoDebugConnectionDefinitions(map.iOConnects3)
            };
        }

        private static List<IoDebugConnectionMigrationDefinition> BuildIoDebugConnectionDefinitions(
            IEnumerable<IOConnect> connections)
        {
            return (connections ?? Enumerable.Empty<IOConnect>()).Select(connection =>
                new IoDebugConnectionMigrationDefinition
                {
                    Output1 = connection?.Output?.Name ?? string.Empty,
                    Output2 = connection?.Output2?.Name ?? string.Empty,
                    Input1 = connection?.Intput1?.Name ?? string.Empty,
                    Input2 = connection?.Intput2?.Name ?? string.Empty
                }).ToList();
        }

        private static PlcMigrationDefinition BuildPlcMigrationDefinition()
        {
            PlcConfiguration snapshot = SF.plcStore?.GetSnapshot() ?? new PlcConfiguration();
            return new PlcMigrationDefinition
            {
                Devices = (snapshot.Devices ?? new List<PlcDeviceConfig>()).Select(device =>
                    new PlcDeviceMigrationDefinition
                    {
                        Name = device?.Name ?? string.Empty,
                        Profile = device?.Profile.ToString() ?? PlcDeviceProfile.GenericModbusTcp.ToString(),
                        IpAddress = device?.IpAddress ?? string.Empty,
                        Port = device?.Port ?? 502,
                        UnitId = device?.UnitId ?? 1,
                        ConnectTimeoutMs = device?.ConnectTimeoutMs ?? 1000,
                        AutoConnect = device?.AutoConnect ?? true,
                        ScanIntervalMs = device?.ScanIntervalMs ?? 50,
                        DataFormat = device?.DataFormat ?? "CDAB",
                        IsStringReverse = device?.IsStringReverse ?? false,
                        AddressStartWithZero = device?.AddressStartWithZero ?? true,
                        StatusVariableName = device?.StatusVariableName ?? string.Empty,
                        Mappings = (device?.Mappings ?? new List<PlcMapConfig>()).Select(mapping =>
                            new PlcMapMigrationDefinition
                            {
                                Id = mapping?.Id ?? string.Empty,
                                Name = mapping?.Name ?? string.Empty,
                                Enabled = mapping?.Enabled ?? true,
                                Area = mapping?.Area.ToString() ?? PlcArea.HoldingRegister.ToString(),
                                StartAddress = mapping?.StartAddress ?? 0,
                                DataType = mapping?.DataType.ToString() ?? PlcDataType.Float.ToString(),
                                Direction = mapping?.Direction.ToString() ?? PlcMapDirection.ReadFromPlc.ToString(),
                                Priority = mapping?.Priority.ToString() ?? PlcMapPriority.High.ToString(),
                                ElementCount = mapping?.ElementCount ?? 1,
                                StringByteLength = mapping?.StringByteLength ?? 0,
                                VariableNames = mapping?.VariableNames?.ToList() ?? new List<string>(),
                                ChangeTolerance = mapping?.ChangeTolerance ?? 0
                            }).ToList()
                    }).ToList()
            };
        }

        private static CommunicationMigrationDefinition BuildCommunicationMigrationDefinition()
        {
            return new CommunicationMigrationDefinition
            {
                Tcp = (SF.communicationStore?.GetSocketSnapshot() ?? new List<SocketInfo>()).Select(item =>
                    new TcpMigrationDefinition
                    {
                        Id = item?.ID ?? 0,
                        Name = item?.Name ?? string.Empty,
                        Type = item?.Type ?? string.Empty,
                        Port = item?.Port ?? 0,
                        Address = item?.Address ?? string.Empty,
                        FrameMode = item?.FrameMode ?? string.Empty,
                        FrameDelimiter = item?.FrameDelimiter ?? string.Empty,
                        EncodingName = item?.EncodingName ?? string.Empty,
                        ConnectTimeoutMs = item?.ConnectTimeoutMs ?? 0
                    }).ToList(),
                Serial = (SF.communicationStore?.GetSerialSnapshot() ?? new List<SerialPortInfo>()).Select(item =>
                    new SerialMigrationDefinition
                    {
                        Id = item?.ID ?? 0,
                        Name = item?.Name ?? string.Empty,
                        Port = item?.Port ?? string.Empty,
                        BitRate = item?.BitRate ?? string.Empty,
                        CheckBit = item?.CheckBit ?? string.Empty,
                        DataBit = item?.DataBit ?? string.Empty,
                        StopBit = item?.StopBit ?? string.Empty,
                        FrameMode = item?.FrameMode ?? string.Empty,
                        FrameDelimiter = item?.FrameDelimiter ?? string.Empty,
                        EncodingName = item?.EncodingName ?? string.Empty
                    }).ToList()
            };
        }

        private JObject HandlePreviewMotionIoConfiguration(JObject request)
        {
            MotionIoMigrationDefinition definition = ReadRequiredObject(request, "definition")
                .ToObject<MotionIoMigrationDefinition>();
            BuildMotionIoCandidate(definition, out FrmCard.Card cards, out List<List<IO>> ioMap);
            var preview = new MigrationConfigurationPreview
            {
                Kind = "motion_io",
                Cards = cards,
                IoMap = ioMap,
                BaseStateHash = ComputeMigrationStateHash("motion_io")
            };
            return RegisterMigrationPreview(preview, new JArray
            {
                $"将控制卡配置替换为 {cards.controlCards.Count} 张卡",
                $"将IO映射替换为 {ioMap.Sum(list => list?.Count ?? 0)} 项"
            });
        }

        private JObject HandlePreviewIoDebugConfiguration(JObject request)
        {
            IoDebugMigrationDefinition definition = ReadRequiredObject(request, "definition")
                .ToObject<IoDebugMigrationDefinition>();
            IODebugMap candidate = BuildIoDebugCandidate(definition);
            var preview = new MigrationConfigurationPreview
            {
                Kind = "io_debug",
                IoDebug = candidate,
                BaseStateHash = ComputeMigrationStateHash("io_debug")
            };
            return RegisterMigrationPreview(preview, new JArray
            {
                $"将IO调试输入显示配置替换为 {candidate.inputs.Count} 项",
                $"将IO调试输出显示配置替换为 {candidate.outputs.Count} 项",
                $"将IO关联配置替换为 {candidate.iOConnects.Count + candidate.iOConnects2.Count + candidate.iOConnects3.Count} 项"
            });
        }

        private JObject HandlePreviewPlcConfiguration(JObject request)
        {
            PlcMigrationDefinition definition = ReadRequiredObject(request, "definition")
                .ToObject<PlcMigrationDefinition>();
            PlcConfiguration candidate = BuildPlcCandidate(definition);
            if (!PlcConfigStore.Validate(candidate, SF.valueStore, out string error))
            {
                throw new BridgeRequestException(400, "PLC_CONFIG_INVALID", error);
            }
            var preview = new MigrationConfigurationPreview
            {
                Kind = "plc",
                Plc = candidate,
                BaseStateHash = ComputeMigrationStateHash("plc")
            };
            return RegisterMigrationPreview(preview, new JArray
            {
                $"将PLC配置替换为 {candidate.Devices.Count} 个设备、{candidate.Devices.Sum(item => item.Mappings?.Count ?? 0)} 项映射"
            });
        }

        private JObject HandlePreviewCommunicationConfiguration(JObject request)
        {
            CommunicationMigrationDefinition definition = ReadRequiredObject(request, "definition")
                .ToObject<CommunicationMigrationDefinition>();
            BuildCommunicationCandidate(definition, out List<SocketInfo> sockets, out List<SerialPortInfo> serialPorts);
            var validator = new CommunicationConfigStore();
            if (!validator.ReplaceSockets(sockets, out string error)
                || !validator.ReplaceSerialPorts(serialPorts, out error))
            {
                throw new BridgeRequestException(400, "COMMUNICATION_CONFIG_INVALID", error);
            }
            var preview = new MigrationConfigurationPreview
            {
                Kind = "communication",
                Sockets = sockets,
                SerialPorts = serialPorts,
                BaseStateHash = ComputeMigrationStateHash("communication")
            };
            return RegisterMigrationPreview(preview, new JArray
            {
                $"将TCP配置替换为 {sockets.Count} 项",
                $"将串口配置替换为 {serialPorts.Count} 项"
            });
        }

        private JObject RegisterMigrationPreview(MigrationConfigurationPreview draft, JArray messages)
        {
            EnsureRuntimeReady();
            JObject patch = new JObject
            {
                ["action"] = "migration_configuration",
                ["domain"] = draft.Kind,
                ["messages"] = messages.DeepClone()
            };
            string previewId = RegisterManagePreview(patch);
            PreviewApprovalRecord record;
            lock (previewLock)
            {
                record = previewRecords[previewId];
                record.MigrationConfigurationPreview = draft;
                record.BaseStateHash = draft.BaseStateHash;
            }
            return new JObject
            {
                ["previewId"] = previewId,
                ["confirmed"] = record.Confirmed,
                ["committed"] = false,
                ["configurationSaved"] = false,
                ["domain"] = draft.Kind,
                ["messages"] = messages,
                ["changes"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "configuration.replace",
                        ["object"] = draft.Kind,
                        ["newValue"] = string.Join("；", messages.Values<string>())
                    }
                }
            };
        }

        private JObject HandleApplyMigrationConfiguration(JObject request)
        {
            string previewId = ReadRequiredString(request, "previewId");
            ValidateConfirmedManagePreview(previewId);
            MigrationConfigurationPreview draft;
            lock (previewLock)
            {
                if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record)
                    || record.MigrationConfigurationPreview == null)
                {
                    throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", "迁移配置预演不存在或已过期。");
                }
                draft = record.MigrationConfigurationPreview;
            }
            EnsureAllProcsStoppedForAiStructureCommit("提交迁移平台配置");
            if (!string.Equals(draft.BaseStateHash, ComputeMigrationStateHash(draft.Kind), StringComparison.Ordinal))
            {
                throw new BridgeRequestException(409, "PREVIEW_BASE_CHANGED",
                    "预演后的基础配置已经变化，本次提交未执行。",
                    new JObject
                    {
                        ["reason"] = "configuration_changed_after_preview",
                        ["retryableWhen"] = "configuration_previewed_again",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
            }

            ApplyMigrationConfiguration(draft);
            RemovePreview(previewId);
            return new JObject
            {
                ["previewId"] = previewId,
                ["domain"] = draft.Kind,
                ["committed"] = true,
                ["configurationSaved"] = true
            };
        }

        private static void ApplyMigrationConfiguration(MigrationConfigurationPreview draft)
        {
            switch (draft.Kind)
            {
                case "motion_io":
                    using (var batch = new ConfigurationBatchWriter(SF.ConfigPath))
                    {
                        batch.AddJson("card.json", draft.Cards);
                        batch.AddJson("IOMap.json", draft.IoMap);
                        batch.Commit();
                    }
                    if (!SF.cardStore.Load(SF.ConfigPath))
                    {
                        SF.SetSecurityLock("迁移后的控制卡配置加载失败。");
                    }
                    SF.frmIO.RefreshIOMap();
                    SF.frmCard.RefreshCardTree();
                    if (SF.DR?.Context != null) SF.DR.Context.IoMap = SF.frmIO.DicIO;
                    break;
                case "io_debug":
                    using (var batch = new ConfigurationBatchWriter(SF.ConfigPath))
                    {
                        batch.AddJson("IODebugMap.json", draft.IoDebug);
                        batch.Commit();
                    }
                    SF.frmIODebug.RefreshIODebugMap();
                    SF.frmIODebug.RefreshIODebugMapFrm();
                    break;
                case "plc":
                    if (!SF.plcStore.Save(SF.ConfigPath, draft.Plc, SF.valueStore, out string plcError))
                    {
                        throw new BridgeRequestException(500, "PLC_CONFIG_SAVE_FAILED", plcError);
                    }
                    if (SF.plcRuntime != null && !SF.plcRuntime.ReloadConfiguration(true, out string reloadError))
                    {
                        SF.DR?.Logger?.Log(reloadError, LogLevel.Error);
                    }
                    break;
                case "communication":
                    using (var batch = new ConfigurationBatchWriter(SF.ConfigPath))
                    {
                        batch.AddJson("SocketInfo.json", draft.Sockets);
                        batch.AddJson("SerialPortInfo.json", draft.SerialPorts);
                        batch.Commit();
                    }
                    bool socketsLoaded = SF.communicationStore.ReplaceSockets(
                        draft.Sockets, out string socketError);
                    bool serialPortsLoaded = SF.communicationStore.ReplaceSerialPorts(
                        draft.SerialPorts, out string serialError);
                    if (!socketsLoaded || !serialPortsLoaded)
                    {
                        SF.SetSecurityLock(socketError ?? serialError ?? "迁移后的通讯配置加载失败。");
                    }
                    SF.frmComunication.RefreshSocketMap();
                    SF.frmComunication.RefreshSerialPortInfo();
                    if (SF.DR?.Context != null)
                    {
                        SF.DR.Context.SocketInfos = SF.communicationStore.GetSocketSnapshot().ToList();
                        SF.DR.Context.SerialPortInfos = SF.communicationStore.GetSerialSnapshot().ToList();
                    }
                    break;
                default:
                    throw new BridgeRequestException(400, "MIGRATION_DOMAIN_INVALID", $"不支持的迁移领域：{draft.Kind}");
            }
        }

        private JObject HandleValidatePlatformConfiguration()
        {
            EnsureRuntimeReady();
            var domains = new JArray();
            AddMigrationValidation(domains, "motion_io", () =>
            {
                if (!SF.cardStore.TryValidateAllAxes(out List<string> errors))
                {
                    throw new InvalidOperationException(string.Join("；", errors));
                }
                ValidateIoMapAgainstCards(SF.cardStore.CardData, SF.frmIO.IOMap);
            });
            AddMigrationValidation(domains, "io_debug", () =>
                ValidateIoDebugMap(SF.frmIODebug.IODebugMaps, SF.frmIO.DicIO));
            AddMigrationValidation(domains, "plc", () =>
            {
                if (!PlcConfigStore.Validate(SF.plcStore.GetSnapshot(), SF.valueStore, out string error))
                {
                    throw new InvalidOperationException(error);
                }
            });
            AddMigrationValidation(domains, "communication", () =>
            {
                var validator = new CommunicationConfigStore();
                if (!validator.ReplaceSockets(SF.communicationStore.GetSocketSnapshot(), out string error)
                    || !validator.ReplaceSerialPorts(SF.communicationStore.GetSerialSnapshot(), out error))
                {
                    throw new InvalidOperationException(error);
                }
            });
            return new JObject
            {
                ["valid"] = domains.OfType<JObject>().All(item => item["valid"]?.Value<bool>() == true),
                ["domains"] = domains
            };
        }

        private static void AddMigrationValidation(JArray target, string domain, Action validate)
        {
            try
            {
                validate();
                target.Add(new JObject { ["domain"] = domain, ["valid"] = true });
            }
            catch (Exception ex)
            {
                target.Add(new JObject
                {
                    ["domain"] = domain,
                    ["valid"] = false,
                    ["error"] = ex.Message
                });
            }
        }

        private static void BuildMotionIoCandidate(
            MotionIoMigrationDefinition definition,
            out FrmCard.Card cards,
            out List<List<IO>> ioMap)
        {
            if (definition == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "控制卡与IO配置不能为空。");
            cards = new FrmCard.Card();
            foreach (ControlCardMigrationDefinition source
                in definition.ControlCards ?? new List<ControlCardMigrationDefinition>())
            {
                if (source == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "控制卡配置包含空项。");
                var card = new FrmCard.ControlCard
                {
                    cardHead = new FrmCard.CardHead
                    {
                        CardType = source.CardType ?? string.Empty,
                        AxisCount = source.Axes?.Count ?? 0,
                        InputCount = source.InputCount,
                        OutputCount = source.OutputCount
                    }
                };
                int axisIndex = 0;
                foreach (AxisMigrationDefinition axis in source.Axes ?? new List<AxisMigrationDefinition>())
                {
                    card.axis.Add(new FrmCard.Axis
                    {
                        AxisNum = axisIndex++,
                        AxisName = axis?.Name ?? string.Empty,
                        PulseToMM = axis?.PulseToMm ?? 0,
                        HomeDirection = axis?.HomeDirection ?? string.Empty,
                        HomeSpeed = axis?.HomeSpeed ?? string.Empty,
                        SpeedInfo = axis?.SpeedInfo ?? 0,
                        SpeedMax = axis?.MaxSpeed ?? 0,
                        AccMax = axis?.AccelerationTime ?? 0,
                        DecMax = axis?.DecelerationTime ?? 0
                    });
                }
                cards.controlCards.Add(card);
            }
            var validator = new CardConfigStore();
            validator.SetCard(cards);
            if (!validator.TryValidateAllAxes(out List<string> axisErrors))
            {
                throw new BridgeRequestException(400, "MOTION_CONFIG_INVALID", string.Join("；", axisErrors));
            }

            ioMap = Enumerable.Range(0, cards.controlCards.Count).Select(_ => new List<IO>()).ToList();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (IoMigrationDefinition source in definition.IoMappings ?? new List<IoMigrationDefinition>())
            {
                if (source == null || source.CardIndex < 0 || source.CardIndex >= ioMap.Count)
                {
                    throw new BridgeRequestException(400, "IO_CONFIG_INVALID", "IO配置引用了无效控制卡索引。");
                }
                string ioName = source.Name?.Trim() ?? string.Empty;
                if (ioName.Length > 0 && !names.Add(ioName))
                {
                    throw new BridgeRequestException(400, "IO_CONFIG_INVALID", $"IO名称重复：{ioName}");
                }
                ioMap[source.CardIndex].Add(new IO
                {
                    Name = ioName,
                    CardNum = source.CardIndex,
                    Module = source.Module,
                    IOIndex = source.IoIndex ?? string.Empty,
                    IOType = source.IoType ?? string.Empty,
                    UsedType = source.UsedType ?? string.Empty,
                    EffectLevel = source.EffectLevel ?? string.Empty,
                    Note = source.Note ?? string.Empty,
                    IsRemark = source.IsRemark
                });
            }
            foreach (List<IO> cardIo in ioMap)
            {
                cardIo.Sort((left, right) => string.Equals(left.IOType, "通用输入", StringComparison.Ordinal)
                    == string.Equals(right.IOType, "通用输入", StringComparison.Ordinal) ? 0
                    : string.Equals(left.IOType, "通用输入", StringComparison.Ordinal) ? -1 : 1);
                for (int i = 0; i < cardIo.Count; i++) cardIo[i].Index = i;
            }
            ValidateIoMapAgainstCards(cards, ioMap);
        }

        private static void ValidateIoMapAgainstCards(FrmCard.Card cards, List<List<IO>> ioMap)
        {
            if (cards?.controlCards == null || ioMap == null || cards.controlCards.Count != ioMap.Count)
            {
                throw new InvalidOperationException("控制卡数量与IO卡分组数量不一致。");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int cardIndex = 0; cardIndex < cards.controlCards.Count; cardIndex++)
            {
                FrmCard.CardHead head = cards.controlCards[cardIndex]?.cardHead
                    ?? throw new InvalidOperationException($"{cardIndex}号控制卡配置为空。");
                List<IO> items = ioMap[cardIndex] ?? new List<IO>();
                int inputs = items.Count(item => item != null && item.IOType == "通用输入");
                int outputs = items.Count(item => item != null && item.IOType == "通用输出");
                if (inputs != head.InputCount || outputs != head.OutputCount)
                {
                    throw new InvalidOperationException($"{cardIndex}号卡IO数量不一致：输入{inputs}/{head.InputCount}，输出{outputs}/{head.OutputCount}。");
                }
                foreach (IO item in items)
                {
                    if (item == null || item.CardNum != cardIndex)
                    {
                        throw new InvalidOperationException($"{cardIndex}号卡包含空IO或错误卡号。");
                    }
                    if (!string.IsNullOrWhiteSpace(item.Name) && !names.Add(item.Name))
                    {
                        throw new InvalidOperationException($"IO名称重复：{item.Name}");
                    }
                    if (item.IOType != "通用输入" && item.IOType != "通用输出")
                    {
                        throw new InvalidOperationException($"IO[{item.Name}]类型无效：{item.IOType}");
                    }
                }
            }
        }

        private static IODebugMap BuildIoDebugCandidate(IoDebugMigrationDefinition definition)
        {
            if (definition == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "IO调试配置不能为空。");
            Dictionary<string, IO> ioByName = SF.frmIO?.DicIO
                ?? throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "IO配置未初始化。");
            var result = new IODebugMap
            {
                inputs = ResolveIoNames(definition.InputNames, "通用输入", ioByName),
                outputs = ResolveIoNames(definition.OutputNames, "通用输出", ioByName),
                iOConnects = BuildIoConnections(definition.Group1, ioByName),
                iOConnects2 = BuildIoConnections(definition.Group2, ioByName),
                iOConnects3 = BuildIoConnections(definition.Group3, ioByName)
            };
            ValidateIoDebugMap(result, ioByName);
            return result;
        }

        private static List<IO> ResolveIoNames(IEnumerable<string> names, string expectedType, Dictionary<string, IO> ioByName)
        {
            var result = new List<IO>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name) || !unique.Add(name.Trim())
                    || !ioByName.TryGetValue(name.Trim(), out IO io) || io == null
                    || !string.Equals(io.IOType, expectedType, StringComparison.Ordinal))
                {
                    throw new BridgeRequestException(400, "IO_DEBUG_CONFIG_INVALID",
                        $"IO调试配置引用无效或重复的{expectedType}：{name}");
                }
                result.Add(io.CloneForDebug());
            }
            return result;
        }

        private static List<IOConnect> BuildIoConnections(
            IEnumerable<IoDebugConnectionMigrationDefinition> definitions,
            Dictionary<string, IO> ioByName)
        {
            return (definitions ?? Enumerable.Empty<IoDebugConnectionMigrationDefinition>())
                .Select(item => new IOConnect
                {
                    Output = ResolveOptionalIo(item?.Output1, "通用输出", ioByName),
                    Output2 = ResolveOptionalIo(item?.Output2, "通用输出", ioByName),
                    Intput1 = ResolveOptionalIo(item?.Input1, "通用输入", ioByName),
                    Intput2 = ResolveOptionalIo(item?.Input2, "通用输入", ioByName)
                }).ToList();
        }

        private static IO ResolveOptionalIo(string name, string expectedType, Dictionary<string, IO> ioByName)
        {
            if (string.IsNullOrWhiteSpace(name)) return new IO();
            if (!ioByName.TryGetValue(name.Trim(), out IO io) || io == null
                || !string.Equals(io.IOType, expectedType, StringComparison.Ordinal))
            {
                throw new BridgeRequestException(400, "IO_DEBUG_CONFIG_INVALID", $"IO关联引用无效：{name}");
            }
            return io.CloneForDebug();
        }

        private static void ValidateIoDebugMap(IODebugMap candidate, Dictionary<string, IO> ioByName)
        {
            if (candidate == null) throw new InvalidOperationException("IO调试配置为空。");
            foreach (IO io in (candidate.inputs ?? new List<IO>()).Concat(candidate.outputs ?? new List<IO>()))
            {
                if (io == null || string.IsNullOrWhiteSpace(io.Name) || !ioByName.ContainsKey(io.Name))
                {
                    throw new InvalidOperationException($"IO调试显示项引用不存在：{io?.Name}");
                }
            }
            foreach (IOConnect connection in (candidate.iOConnects ?? new List<IOConnect>())
                .Concat(candidate.iOConnects2 ?? new List<IOConnect>())
                .Concat(candidate.iOConnects3 ?? new List<IOConnect>()))
            {
                foreach (IO io in new[] { connection?.Output, connection?.Output2, connection?.Intput1, connection?.Intput2 })
                {
                    if (io != null && !string.IsNullOrWhiteSpace(io.Name) && !ioByName.ContainsKey(io.Name))
                    {
                        throw new InvalidOperationException($"IO关联引用不存在：{io.Name}");
                    }
                }
            }
        }

        private static PlcConfiguration BuildPlcCandidate(PlcMigrationDefinition definition)
        {
            if (definition == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "PLC配置不能为空。");
            var result = new PlcConfiguration();
            foreach (PlcDeviceMigrationDefinition source in definition.Devices ?? new List<PlcDeviceMigrationDefinition>())
            {
                if (!Enum.TryParse(source?.Profile, true, out PlcDeviceProfile profile))
                    throw new BridgeRequestException(400, "PLC_CONFIG_INVALID", $"PLC Profile无效：{source?.Profile}");
                var device = new PlcDeviceConfig
                {
                    Name = source.Name ?? string.Empty,
                    Profile = profile,
                    IpAddress = source.IpAddress ?? string.Empty,
                    Port = source.Port,
                    UnitId = source.UnitId,
                    ConnectTimeoutMs = source.ConnectTimeoutMs,
                    AutoConnect = source.AutoConnect,
                    ScanIntervalMs = source.ScanIntervalMs,
                    DataFormat = source.DataFormat ?? string.Empty,
                    IsStringReverse = source.IsStringReverse,
                    AddressStartWithZero = source.AddressStartWithZero,
                    StatusVariableName = source.StatusVariableName ?? string.Empty
                };
                foreach (PlcMapMigrationDefinition mapSource in source.Mappings ?? new List<PlcMapMigrationDefinition>())
                {
                    if (!Enum.TryParse(mapSource?.Area, true, out PlcArea area)
                        || !Enum.TryParse(mapSource?.DataType, true, out PlcDataType dataType)
                        || !Enum.TryParse(mapSource?.Direction, true, out PlcMapDirection direction)
                        || !Enum.TryParse(mapSource?.Priority, true, out PlcMapPriority priority))
                    {
                        throw new BridgeRequestException(400, "PLC_CONFIG_INVALID", $"PLC映射枚举无效：{mapSource?.Name}");
                    }
                    device.Mappings.Add(new PlcMapConfig
                    {
                        Id = string.IsNullOrWhiteSpace(mapSource.Id) ? Guid.NewGuid().ToString("N") : mapSource.Id,
                        Name = mapSource.Name ?? string.Empty,
                        Enabled = mapSource.Enabled,
                        Area = area,
                        StartAddress = mapSource.StartAddress,
                        DataType = dataType,
                        Direction = direction,
                        Priority = priority,
                        ElementCount = mapSource.ElementCount,
                        StringByteLength = mapSource.StringByteLength,
                        VariableNames = mapSource.VariableNames?.ToList() ?? new List<string>(),
                        ChangeTolerance = mapSource.ChangeTolerance
                    });
                }
                result.Devices.Add(device);
            }
            return result;
        }

        private static void BuildCommunicationCandidate(
            CommunicationMigrationDefinition definition,
            out List<SocketInfo> sockets,
            out List<SerialPortInfo> serialPorts)
        {
            if (definition == null) throw new BridgeRequestException(400, "INVALID_ARGUMENT", "通讯配置不能为空。");
            sockets = (definition.Tcp ?? new List<TcpMigrationDefinition>()).Select(item => new SocketInfo
            {
                ID = item.Id,
                Name = item.Name ?? string.Empty,
                Type = item.Type ?? string.Empty,
                Port = item.Port,
                Address = item.Address ?? string.Empty,
                FrameMode = item.FrameMode ?? string.Empty,
                FrameDelimiter = item.FrameDelimiter ?? string.Empty,
                EncodingName = item.EncodingName ?? string.Empty,
                ConnectTimeoutMs = item.ConnectTimeoutMs
            }).ToList();
            serialPorts = (definition.Serial ?? new List<SerialMigrationDefinition>()).Select(item => new SerialPortInfo
            {
                ID = item.Id,
                Name = item.Name ?? string.Empty,
                Port = item.Port ?? string.Empty,
                BitRate = item.BitRate ?? string.Empty,
                CheckBit = item.CheckBit ?? string.Empty,
                DataBit = item.DataBit ?? string.Empty,
                StopBit = item.StopBit ?? string.Empty,
                FrameMode = item.FrameMode ?? string.Empty,
                FrameDelimiter = item.FrameDelimiter ?? string.Empty,
                EncodingName = item.EncodingName ?? string.Empty
            }).ToList();
        }

        private static string ComputeMigrationStateHash(string kind)
        {
            var state = new JObject { ["domain"] = kind };
            switch (kind)
            {
                case "motion_io":
                    state["cards"] = JToken.FromObject(SF.cardStore?.CardData ?? new FrmCard.Card());
                    state["io"] = JToken.FromObject(SF.frmIO?.IOMap ?? new List<List<IO>>());
                    break;
                case "io_debug":
                    state["ioDebug"] = JToken.FromObject(SF.frmIODebug?.IODebugMaps ?? new IODebugMap());
                    break;
                case "plc":
                    state["plc"] = JToken.FromObject(SF.plcStore?.GetSnapshot() ?? new PlcConfiguration());
                    break;
                case "communication":
                    state["tcp"] = JToken.FromObject(SF.communicationStore?.GetSocketSnapshot() ?? new List<SocketInfo>());
                    state["serial"] = JToken.FromObject(SF.communicationStore?.GetSerialSnapshot() ?? new List<SerialPortInfo>());
                    break;
            }
            return ComputePatchHash(state);
        }

        // ===================== IO 操作 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListIo(JObject request)
        {
            EnsureRuntimeReady();
            var ioMap = SF.frmIO?.DicIO;
            if (ioMap == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
            }
            string typeFilter = request["type"]?.Value<string>();
            string nameLike = request["nameLike"]?.Value<string>();
            int limit = request["limit"]?.Value<int>() ?? 0;
            if (limit <= 0) limit = 10000;

            var items = new List<JObject>();
            foreach (var kv in ioMap)
            {
                IO io = kv.Value;
                if (io == null) continue;
                if (!string.IsNullOrEmpty(typeFilter)
                    && !string.Equals(io.IOType, typeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(nameLike)
                    && (io.Name ?? string.Empty).IndexOf(nameLike, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                items.Add(BuildIoJObject(io));
                if (items.Count >= limit) break;
            }
            return new JObject
            {
                ["total"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetIo(JObject request)
        {
            EnsureRuntimeReady();
            var ioMap = SF.frmIO?.DicIO;
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
            var ioMap = SF.frmIO?.DicIO;
            if (ioMap == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
            }
            string keyword = request["keyword"]?.Value<string>()?.Trim() ?? string.Empty;
            bool returnAll = string.IsNullOrEmpty(keyword)
                || string.Equals(keyword, "*", StringComparison.Ordinal);
            string typeFilter = request["type"]?.Value<string>();
            int? cardNum = request["cardNum"]?.Value<int>();
            int limit = request["limit"]?.Value<int>() ?? 100;
            if (limit <= 0) limit = 100;

            var items = new List<JObject>();
            foreach (var kv in ioMap)
            {
                IO io = kv.Value;
                if (io == null) continue;
                if (!returnAll
                    && (io.Name ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(typeFilter)
                    && !string.Equals(io.IOType, typeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (cardNum.HasValue && io.CardNum != cardNum.Value)
                {
                    continue;
                }
                items.Add(BuildIoJObject(io));
                if (items.Count >= limit) break;
            }
            return new JObject
            {
                ["keyword"] = keyword,
                ["queryMode"] = returnAll ? "all" : "contains",
                ["type"] = typeFilter ?? string.Empty,
                ["cardNum"] = cardNum.HasValue ? JToken.FromObject(cardNum.Value) : null,
                ["returned"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleGetIoState(JObject request)
        {
            EnsureRuntimeReady();
            var ioMap = SF.frmIO?.DicIO;
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
                    ok = SF.io?.GetOutIO(io, ref bval) ?? false;
                }
                else
                {
                    ok = SF.io?.GetInIO(io, ref bval) ?? false;
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

        // ===================== 报警清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListAlarms(JObject request)
        {
            EnsureRuntimeReady();
            AlarmInfoStore store = SF.alarmInfoStore;
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
            SF.alarmInfoStore.UpdateAlarm(index, name, category, btn1, btn2, btn3, note);
            SF.alarmInfoStore.Save(SF.ConfigPath);
            SF.alarmInfoStore.TryGetByIndex(index, out alarm);
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
            SF.alarmInfoStore.ClearAlarm(index);
            // Index 保持不变（固定槽位）

            SF.alarmInfoStore.Save(SF.ConfigPath);
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
        private static void RefreshAlarmConfigView()
        {
            try { SF.frmAlarmConfig?.RefreshAlarmInfo(); }
            catch { /* 界面刷新失败不影响数据保存 */ }
        }

        // ===================== PLC 设备清单 =====================

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListPlcDevices(JObject request)
        {
            EnsureRuntimeReady();
            PlcConfigStore store = SF.plcStore;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "PLC 存储未初始化。");
            }
            bool includeMaps = request["includeMaps"]?.Value<bool>() ?? false;
            string exactName = ReadOptionalString(request, "name");
            PlcConfiguration configuration = store.GetSnapshot();
            var devices = configuration.Devices;
            IReadOnlyDictionary<string, PlcDeviceRuntimeSnapshot> runtimeByName =
                (SF.plcRuntime?.GetSnapshots() ?? new List<PlcDeviceRuntimeSnapshot>())
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
            CardConfigStore store = SF.cardStore;
            if (store == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "控制卡存储未初始化。");
            }
            bool includeAxes = request["includeAxes"]?.Value<bool>() ?? true;
            var items = new List<JObject>();
            int cardCount = store.GetControlCardCount();
            for (int ci = 0; ci < cardCount; ci++)
            {
                if (!store.TryGetControlCard(ci, out FrmCard.ControlCard card) || card == null) continue;
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
                        FrmCard.Axis axis = card.axis[ai];
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
            var store = SF.trayPointStore;
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
            IReadOnlyList<SocketInfo> socketInfos = SF.communicationStore?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>();
            IReadOnlyList<SerialPortInfo> serialPortInfos = SF.communicationStore?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>();
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
                if (includeStatus && SF.comm != null)
                {
                    TcpStatus status = SF.comm.GetTcpStatus(sock.Name);
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
                if (includeStatus && SF.comm != null)
                {
                    SerialStatus status = SF.comm.GetSerialStatus(sp.Name);
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

        [System.Diagnostics.DebuggerNonUserCode]
        private PatchExecutionResult ExecutePatch(JObject request)
        {
            EnsureRuntimeReady();

            int procIndex = ReadRequiredInt(request, "procIndex");
            string baseProcId = ReadRequiredString(request, "baseProcId");
            JArray actions = ReadRequiredArray(request, "actions");

            Proc current = GetProcByIndex(procIndex);
            string currentProcId = current.head?.Id.ToString("D") ?? string.Empty;
            if (!string.Equals(currentProcId, baseProcId, StringComparison.OrdinalIgnoreCase))
            {
                throw new BridgeRequestException(409, "PROC_VERSION_MISMATCH", "流程版本不一致，请重新读取流程详情后再提交。");
            }

            Proc draft = ObjectGraphCloner.Clone(current);
            var result = new PatchExecutionResult
            {
                ProcIndex = procIndex,
                Proc = draft
            };

            for (int i = 0; i < actions.Count; i++)
            {
                if (!(actions[i] is JObject action))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"actions[{i}] 必须是 JSON 对象。");
                }

                string actionType = ReadRequiredActionType(action, i);
                switch (actionType)
                {
                    case "update_proc_head_fields":
                        ApplyProcHeadUpdate(action, draft, result, i);
                        break;
                    case "update_step_fields":
                        ApplyStepUpdate(action, draft, result, i);
                        break;
                    case "update_operation_fields":
                        ApplyOperationUpdate(action, draft, result, i);
                        break;
                    case "append_step":
                        ApplyAppendStep(action, draft, result, i);
                        break;
                    case "insert_step":
                        ApplyInsertStep(action, draft, result, i);
                        break;
                    case "delete_step":
                        ApplyDeleteStep(action, draft, result, i);
                        break;
                    case "move_step":
                        ApplyMoveStep(action, draft, result, i);
                        break;
                    case "append_operation":
                        ApplyAppendOperation(action, draft, result, i);
                        break;
                    case "insert_operation":
                        ApplyInsertOperation(action, draft, result, i);
                        break;
                    case "delete_operation":
                        ApplyDeleteOperation(action, draft, result, i);
                        break;
                    case "move_operation":
                        ApplyMoveOperation(action, draft, result, i);
                        break;
                    default:
                        ThrowUnsupportedPatchAction(actionType, i);
                        break;
                }
            }

            RewriteGotoTargetsAfterStructureChange(current, draft, result.ProcIndex, result);
            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(procIndex, draft, errors);
            if (errors.Count > 0)
            {
                throw new BridgeRequestException(400, "PATCH_VALIDATE_FAILED", "Patch 预演失败。", string.Join("\r\n", errors.Distinct()));
            }

            return result;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void CommitPatch(int procIndex, Proc draft, List<(int stepIndex, int opIndex, ProcChangeKind kind)> affectedOps = null)
        {
            if (draft == null)
            {
                throw new BridgeRequestException(500, "PATCH_EMPTY", "Patch 结果为空。");
            }

            // AI 可以读取和预演运行中的流程，但正式提交不得改变设备运行状态，
            // 也不得替换正在执行的流程对象。停止流程必须由操作员显式执行。
            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
            if (snapshot != null && snapshot.State != ProcRunState.Stopped)
            {
                throw new BridgeRequestException(
                    409,
                    "PROC_NOT_STOPPED",
                    $"流程 {procIndex} 当前为 {snapshot.State}，本次提交尚未执行。",
                    new JObject
                    {
                        ["procIndex"] = procIndex,
                        ["currentState"] = snapshot.State.ToString(),
                        ["retryableNow"] = false,
                        ["retryableWhen"] = "process_stopped",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
            }

            ValidateHmiCustomFunctionSource();

            if (!ProcessEditingService.TryCommitProcDraft(procIndex, draft, out string commitError))
            {
                throw new BridgeRequestException(500, "COMMIT_FAILED", "流程提交失败。", commitError);
            }
            NotifyProcChanged(procIndex, ProcChangeKind.Modified, affectedOps);
        }

        private static void ValidateHmiCustomFunctionSource()
        {
            string sourcePath = FindHmiCustomFunctionSource();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            string source;
            try
            {
                source = File.ReadAllText(sourcePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new BridgeRequestException(
                    409,
                    "SOURCE_VALIDATION_FAILED",
                    "无法读取 HMI 自定义函数源码，已阻止流程提交。",
                    ex.Message);
            }

            var errors = new List<string>();
            if (Regex.IsMatch(source, @"\bSF\s*\."))
            {
                errors.Add("HMI 自定义函数只能通过 IAutomationPlatform 公开接口访问平台能力，不能直接引用 SF。");
            }

            string[] registeredFunctions = Regex.Matches(
                    source,
                    "\\bRegisterCustomFunction\\s*\\(\\s*\"([^\"]+)\"")
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .ToArray();
            string[] duplicateNames = registeredFunctions
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (duplicateNames.Length > 0)
            {
                errors.Add("HMI 自定义函数存在重复注册名称：" + string.Join("、", duplicateNames) + "。");
            }

            if (errors.Count > 0)
            {
                throw new BridgeRequestException(
                    409,
                    "SOURCE_VALIDATION_FAILED",
                    "HMI 自定义函数源码静态检查未通过；本次流程没有保存，预演记录仍保留。请根据 details 修正源码后，使用同一 previewId 重试提交。",
                    string.Join("\r\n", errors.Distinct(StringComparer.Ordinal)));
            }
        }

        private static string FindHmiCustomFunctionSource()
        {
            if (HmiDevelopmentSourceLocator.TryResolve(
                AppDomain.CurrentDomain.BaseDirectory,
                out HmiDevelopmentSource source,
                out _))
            {
                return source.CustomFunctionSourcePath;
            }
            return null;
        }

        private void ApplyProcHeadUpdate(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            JObject fieldChanges = ReadRequiredFieldChanges(action, actionIndex, "update_proc_head_fields");
            ApplyDirectPropertyChanges(
                draft.head,
                ProcHeadEditableFields,
                fieldChanges,
                $"流程{result.ProcIndex}头信息",
                actionIndex,
                result);
        }

        private void ApplyStepUpdate(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Step step = FindStepById(draft, ParseGuid(ReadRequiredString(action, "stepId"), "stepId"));
            JObject fieldChanges = ReadRequiredFieldChanges(action, actionIndex, "update_step_fields");
            ApplyDirectPropertyChanges(
                step,
                StepEditableFields,
                fieldChanges,
                $"步骤[{step.Name}]",
                actionIndex,
                result);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void ApplyOperationUpdate(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Step step = FindStepById(draft, ParseGuid(ReadRequiredString(action, "stepId"), "stepId"));
            OperationType op = FindOperationById(step, ParseGuid(ReadRequiredString(action, "opId"), "opId"));
            ValidateExpectedOperaType(action, op);

            JObject fieldChanges = ReadRequiredFieldChanges(action, actionIndex, "update_operation_fields");
            ApplyOperationPropertyChanges(op, fieldChanges, $"指令[{op.Name}]", actionIndex, result);

            // 记录被修改指令的位置，供 FrmDataGrid 行级闪烁使用。
            int stepIndex = FindStepIndexById(draft, step.Id);
            int opIndex = FindOperationIndexById(step, op.Id);
            if (stepIndex >= 0 && opIndex >= 0)
            {
                result.AffectedOps.Add((stepIndex, opIndex, ProcChangeKind.Modified));
            }
        }

        private void ApplyAppendStep(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            InsertStepCore(
                action,
                draft,
                result,
                actionIndex,
                draft.steps?.Count ?? 0,
                "append_step");
        }

        private void ApplyInsertStep(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            int insertIndex = ReadRequiredInsertIndex(action, "insertIndex", draft.steps?.Count ?? 0, "步骤插入位置");
            InsertStepCore(
                action,
                draft,
                result,
                actionIndex,
                insertIndex,
                "insert_step");
        }

        private void ApplyDeleteStep(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Guid stepId = ParseGuid(ReadRequiredString(action, "stepId"), "stepId");
            int stepIndex = FindStepIndexById(draft, stepId);
            Step step = draft.steps[stepIndex];

            draft.steps.RemoveAt(stepIndex);

            result.Messages.Add($"动作{actionIndex}：已删除步骤[{step?.Name ?? string.Empty}]。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = actionIndex,
                ["type"] = "delete_step",
                ["stepId"] = stepId.ToString("D"),
                ["oldStepIndex"] = stepIndex,
                ["name"] = step?.Name ?? string.Empty
            });
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static void ValidateExpectedOperaType(JObject action, OperationType op)
        {
            string expectedOperaType = ReadOptionalString(action, "expectedOperaType");
            if (!string.IsNullOrWhiteSpace(expectedOperaType)
                && !string.Equals(expectedOperaType, op?.OperaType, StringComparison.Ordinal))
            {
                throw new BridgeRequestException(409, "PATCH_TARGET_MISMATCH", $"指令类型不匹配，期望 {expectedOperaType}，实际 {op?.OperaType ?? string.Empty}。");
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static void EnsureStepOps(Step step)
        {
            if (step == null)
            {
                throw new BridgeRequestException(500, "STEP_NULL", "步骤为空。");
            }
            if (step.Ops == null)
            {
                step.Ops = new List<OperationType>();
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static int FindStepIndexById(Proc proc, Guid stepId)
        {
            if (proc?.steps == null)
            {
                throw new BridgeRequestException(404, "STEP_NOT_FOUND", $"未找到步骤：{stepId:D}");
            }

            for (int i = 0; i < proc.steps.Count; i++)
            {
                if (proc.steps[i] != null && proc.steps[i].Id == stepId)
                {
                    return i;
                }
            }

            throw new BridgeRequestException(404, "STEP_NOT_FOUND", $"未找到步骤：{stepId:D}");
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static int FindOperationIndexById(Step step, Guid opId)
        {
            if (step?.Ops == null)
            {
                throw new BridgeRequestException(404, "OP_NOT_FOUND", $"未找到指令：{opId:D}");
            }

            for (int i = 0; i < step.Ops.Count; i++)
            {
                if (step.Ops[i] != null && step.Ops[i].Id == opId)
                {
                    return i;
                }
            }

            throw new BridgeRequestException(404, "OP_NOT_FOUND", $"未找到指令：{opId:D}");
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static int ReadRequiredInsertIndex(JObject request, string fieldName, int maxInclusive, string label)
        {
            int value = ReadRequiredInt(request, fieldName);
            if (value < 0 || value > maxInclusive)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"{label}超出范围：{value}");
            }
            return value;
        }

        private void RewriteGotoTargetsAfterStructureChange(Proc before, Proc after, int procIndex, PatchExecutionResult result)
        {
            GotoRewriteResult summary = ProcessEditingService.RewriteGotoTargets(before, after, procIndex);

            if (summary.RewrittenCount == 0 && summary.InvalidatedCount == 0)
            {
                return;
            }

            result.Messages.Add($"Patch：已重写跳转 {summary.RewrittenCount} 个，发现已删除目标 {summary.InvalidatedCount} 个；已删除目标必须明确修复后才能提交。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = -1,
                ["type"] = "goto_rewrite",
                ["rewrittenCount"] = summary.RewrittenCount,
                ["invalidatedCount"] = summary.InvalidatedCount
            });
        }

        private void ApplyMoveStep(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Guid stepId = ParseGuid(ReadRequiredString(action, "stepId"), "stepId");
            int sourceIndex = FindStepIndexById(draft, stepId);
            Step step = draft.steps[sourceIndex];

            draft.steps.RemoveAt(sourceIndex);
            int targetIndex = ReadRequiredInsertIndex(action, "targetIndex", draft.steps.Count, "步骤目标位置");
            draft.steps.Insert(targetIndex, step);

            result.Messages.Add($"动作{actionIndex}：已移动步骤[{step?.Name ?? string.Empty}] 到索引 {targetIndex}。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = actionIndex,
                ["type"] = "move_step",
                ["stepId"] = stepId.ToString("D"),
                ["oldStepIndex"] = sourceIndex,
                ["newStepIndex"] = targetIndex,
                ["name"] = step?.Name ?? string.Empty
            });
        }

        private void InsertStepCore(JObject action, Proc draft, PatchExecutionResult result, int actionIndex, int insertIndex, string changeType)
        {
            if (draft.steps == null)
            {
                draft.steps = new List<Step>();
            }

            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = ReadOptionalString(action, "name") ?? $"步骤{insertIndex}",
                Disable = ReadOptionalBoolean(action, "disable") ?? false,
                Ops = new List<OperationType>()
            };

            draft.steps.Insert(insertIndex, step);

            result.Messages.Add($"动作{actionIndex}：已在索引 {insertIndex} 插入步骤 {step.Name}。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = actionIndex,
                ["type"] = changeType,
                ["stepIndex"] = insertIndex,
                ["stepId"] = step.Id.ToString("D"),
                ["name"] = step.Name
            });
        }

        private void ApplyAppendOperation(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Step step = FindStepById(draft, ParseGuid(ReadRequiredString(action, "stepId"), "stepId"));
            InsertOperationCore(
                action,
                draft,
                step,
                result,
                actionIndex,
                step?.Ops?.Count ?? 0,
                "append_operation");
        }

        private void ApplyInsertOperation(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Step step = FindStepById(draft, ParseGuid(ReadRequiredString(action, "stepId"), "stepId"));
            int insertIndex = ReadRequiredInsertIndex(action, "insertIndex", step?.Ops?.Count ?? 0, "指令插入位置");
            InsertOperationCore(
                action,
                draft,
                step,
                result,
                actionIndex,
                insertIndex,
                "insert_operation");
        }

        private void ApplyDeleteOperation(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Guid stepId = ParseGuid(ReadRequiredString(action, "stepId"), "stepId");
            Guid opId = ParseGuid(ReadRequiredString(action, "opId"), "opId");
            int stepIndex = FindStepIndexById(draft, stepId);
            Step step = draft.steps[stepIndex];
            int opIndex = FindOperationIndexById(step, opId);
            OperationType op = step.Ops[opIndex];
            ValidateExpectedOperaType(action, op);

            step.Ops.RemoveAt(opIndex);

            result.Messages.Add($"动作{actionIndex}：已删除步骤[{step?.Name ?? string.Empty}] 中的指令 {op?.Name ?? string.Empty}({op?.OperaType ?? string.Empty})。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = actionIndex,
                ["type"] = "delete_operation",
                ["stepId"] = stepId.ToString("D"),
                ["opId"] = opId.ToString("D"),
                ["oldStepIndex"] = stepIndex,
                ["oldOpIndex"] = opIndex,
                ["operaType"] = op?.OperaType ?? string.Empty,
                ["name"] = op?.Name ?? string.Empty
            });
        }

        private void ApplyMoveOperation(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Guid stepId = ParseGuid(ReadRequiredString(action, "stepId"), "stepId");
            Guid opId = ParseGuid(ReadRequiredString(action, "opId"), "opId");
            Guid targetStepId = string.IsNullOrWhiteSpace(ReadOptionalString(action, "targetStepId"))
                ? stepId
                : ParseGuid(ReadRequiredString(action, "targetStepId"), "targetStepId");

            int sourceStepIndex = FindStepIndexById(draft, stepId);
            Step sourceStep = draft.steps[sourceStepIndex];
            int sourceOpIndex = FindOperationIndexById(sourceStep, opId);
            OperationType op = sourceStep.Ops[sourceOpIndex];
            ValidateExpectedOperaType(action, op);

            sourceStep.Ops.RemoveAt(sourceOpIndex);
            int targetStepIndex = FindStepIndexById(draft, targetStepId);
            Step targetStep = draft.steps[targetStepIndex];
            EnsureStepOps(targetStep);
            int targetIndex = ReadRequiredInsertIndex(action, "targetIndex", targetStep.Ops.Count, "指令目标位置");
            targetStep.Ops.Insert(targetIndex, op);

            result.Messages.Add($"动作{actionIndex}：已移动指令 {op?.Name ?? string.Empty}({op?.OperaType ?? string.Empty}) 到步骤[{targetStep?.Name ?? string.Empty}] 索引 {targetIndex}。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = actionIndex,
                ["type"] = "move_operation",
                ["opId"] = opId.ToString("D"),
                ["stepId"] = stepId.ToString("D"),
                ["targetStepId"] = targetStepId.ToString("D"),
                ["oldStepIndex"] = sourceStepIndex,
                ["oldOpIndex"] = sourceOpIndex,
                ["newStepIndex"] = targetStepIndex,
                ["newOpIndex"] = targetIndex,
                ["operaType"] = op?.OperaType ?? string.Empty,
                ["name"] = op?.Name ?? string.Empty
            });

            // 记录移动后新位置的指令，供 FrmDataGrid 行级闪烁使用。
            result.AffectedOps.Add((targetStepIndex, targetIndex, ProcChangeKind.Modified));
        }

        private void InsertOperationCore(JObject action, Proc draft, Step step, PatchExecutionResult result, int actionIndex, int insertIndex, string changeType)
        {
            string operaType = ReadRequiredString(action, "operaType");
            OperationType op = CreateOperationTemplate(operaType);
            op.Id = Guid.NewGuid();

            JObject fieldValues = ReadOptionalInsertFieldValues(action, actionIndex, changeType);
            if (fieldValues != null && fieldValues.Properties().Any())
            {
                ApplyOperationPropertyChanges(op, fieldValues, $"新增指令[{operaType}]", actionIndex, result);
            }

            EnsureStepOps(step);

            step.Ops.Insert(insertIndex, op);

            result.Messages.Add($"动作{actionIndex}：已在步骤[{step.Name}] 索引 {insertIndex} 插入指令 {op.Name}({op.OperaType})。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = actionIndex,
                ["type"] = changeType,
                ["stepId"] = step.Id.ToString("D"),
                ["opIndex"] = insertIndex,
                ["opId"] = op.Id.ToString("D"),
                ["operaType"] = op.OperaType ?? string.Empty,
                ["name"] = op.Name ?? string.Empty
            });

            // 记录新增指令的位置，供 FrmDataGrid 行级闪烁使用。
            int stepIndex = FindStepIndexById(draft, step.Id);
            if (stepIndex >= 0)
            {
                result.AffectedOps.Add((stepIndex, insertIndex, ProcChangeKind.Added));
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void ApplyDirectPropertyChanges(object target, ISet<string> editableFields, JObject fieldChanges, string targetLabel, int actionIndex, PatchExecutionResult result)
        {
            if (target == null)
            {
                throw new BridgeRequestException(500, "PATCH_TARGET_NULL", $"{targetLabel} 为空。");
            }

            var propertyMap = TypeDescriptor.GetProperties(target)
                .Cast<PropertyDescriptor>()
                .Where(item => item != null)
                .ToDictionary(item => item.Name, item => item, StringComparer.Ordinal);

            foreach (JProperty field in fieldChanges.Properties())
            {
                if (!editableFields.Contains(field.Name))
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_NOT_ALLOWED", $"{targetLabel} 不允许修改字段：{field.Name}");
                }

                if (!propertyMap.TryGetValue(field.Name, out PropertyDescriptor descriptor))
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_NOT_FOUND", $"{targetLabel} 不存在字段：{field.Name}");
                }

                if (descriptor.IsReadOnly)
                {
                    throw new BridgeRequestException(400, "PATCH_FIELD_READONLY", $"{targetLabel} 字段只读：{field.Name}");
                }

                object oldValue = descriptor.GetValue(target);
                object newValue = ConvertTokenToValue(field.Value, descriptor, targetLabel);
                ValidateStandardValue(descriptor, newValue, targetLabel, field.Name);
                descriptor.SetValue(target, newValue);
                object finalValue = descriptor.GetValue(target);

                result.Messages.Add($"动作{actionIndex}：已更新 {targetLabel}.{field.Name}。");
                result.Changes.Add(new JObject
                {
                    ["actionIndex"] = actionIndex,
                    ["type"] = "field_change",
                    ["target"] = targetLabel,
                    ["field"] = field.Name,
                    ["oldValue"] = ConvertValueToToken(oldValue),
                    ["newValue"] = ConvertValueToToken(finalValue)
                });
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void ApplyOperationPropertyChanges(OperationType op, JObject fieldChanges, string targetLabel, int actionIndex, PatchExecutionResult result)
        {
            WithOperationEditContext(op, () =>
            {
                RefreshOperationContext(op);
                foreach (JProperty field in fieldChanges.Properties())
                {
                    Dictionary<string, PropertyDescriptor> map = TypeDescriptor.GetProperties(op)
                        .Cast<PropertyDescriptor>()
                        .Where(item => item != null && item.IsBrowsable)
                        .ToDictionary(item => item.Name, item => item, StringComparer.Ordinal);

                    if (!map.TryGetValue(field.Name, out PropertyDescriptor descriptor))
                    {
                        throw new BridgeRequestException(400, "PATCH_FIELD_NOT_FOUND", $"{targetLabel} 不存在或当前不可见字段：{field.Name}");
                    }

                    if (descriptor.IsReadOnly)
                    {
                        throw new BridgeRequestException(400, "PATCH_FIELD_READONLY", $"{targetLabel} 字段只读：{field.Name}");
                    }

                    object oldValue = descriptor.GetValue(op);
                    object newValue = ConvertTokenToValue(field.Value, descriptor, targetLabel);
                    ValidateStandardValue(descriptor, newValue, targetLabel, field.Name);
                    descriptor.SetValue(op, newValue);
                    RefreshOperationContext(op);
                    object finalValue = descriptor.GetValue(op);

                    result.Messages.Add($"动作{actionIndex}：已更新 {targetLabel}.{field.Name}。");
                    result.Changes.Add(new JObject
                    {
                        ["actionIndex"] = actionIndex,
                        ["type"] = "field_change",
                        ["target"] = targetLabel,
                        ["field"] = field.Name,
                        ["oldValue"] = ConvertValueToToken(oldValue),
                        ["newValue"] = ConvertValueToToken(finalValue)
                    });
                }
            });
        }

        private JObject BuildPatchResult(string mode, PatchExecutionResult result)
        {
            int operationCount = CountOperations(result.Proc);
            var output = new JObject
            {
                ["mode"] = mode,
                ["procIndex"] = result.ProcIndex,
                ["procId"] = result.Proc?.head?.Id.ToString("D"),
                ["messages"] = new JArray(result.Messages.Select(item => (JToken)item)),
                ["changes"] = result.Changes,
                ["operationCount"] = operationCount
            };
            if (operationCount <= MaxDetailOperationCount)
            {
                output["procDetail"] = BuildProcDetail(result.ProcIndex, result.Proc);
            }
            else
            {
                output["procDetailOmitted"] = true;
                output["procDetailReason"] = $"指令数超过{MaxDetailOperationCount}，请按changes中的位置局部复查";
            }
            return output;
        }

        private static int CountOperations(Proc proc)
        {
            return proc?.steps?.Sum(step => step?.Ops?.Count ?? 0) ?? 0;
        }

        private JObject BuildRegisteredPatchPreview(JObject patch, PatchExecutionResult result)
        {
            PreviewApprovalRecord record = RegisterPreview(patch, result);

            // 自动批准模式：直接标记预演为已确认，避免 FrmAiAssistant 通过 HTTP 回调确认导致 UI 线程死锁。
            bool autoConfirmed = SF.frmAiAssistant?.IsAutoApproveMode == true;
            if (autoConfirmed)
            {
                lock (previewLock)
                {
                    if (previewRecords.TryGetValue(record.PreviewId, out PreviewApprovalRecord stored))
                    {
                        stored.Confirmed = true;
                        stored.ConfirmedAtUtc = DateTime.UtcNow;
                    }
                }
            }

            JObject preview = BuildPatchResult("preview", result);
            preview["previewId"] = record.PreviewId;
            preview["patchHash"] = record.PatchHash;
            preview["confirmed"] = autoConfirmed;
            preview["expiresAt"] = record.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            preview["summary"] = new JObject
            {
                ["messageCount"] = result.Messages?.Count ?? 0,
                ["changeCount"] = result.Changes?.Count ?? 0
            };
            return preview;
        }

        private PreviewApprovalRecord RegisterPreview(JObject patch, PatchExecutionResult result)
        {
            JObject normalizedPatch = NormalizePatchForApproval(patch);
            string patchHash = ComputePatchHash(normalizedPatch);
            var record = new PreviewApprovalRecord
            {
                PreviewId = Guid.NewGuid().ToString("N"),
                Patch = normalizedPatch,
                PatchHash = patchHash,
                ProcIndex = result.ProcIndex,
                BaseProcId = ReadRequiredString(normalizedPatch, "baseProcId"),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
                Confirmed = false,
                DraftProc = ObjectGraphCloner.Clone(result.Proc),
                PreviewMessages = new List<string>(result.Messages ?? new List<string>()),
                PreviewChanges = result.Changes == null ? new JArray() : (JArray)result.Changes.DeepClone(),
                AffectedOps = result.AffectedOps == null
                    ? new List<(int stepIndex, int opIndex, ProcChangeKind kind)>()
                    : new List<(int stepIndex, int opIndex, ProcChangeKind kind)>(result.AffectedOps)
            };

            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                EnsureNoActivePreviewLocked();
                previewRecords[record.PreviewId] = record;
            }

            return record;
        }

        private PatchExecutionResult GetStoredPatchResult(string previewId)
        {
            lock (previewLock)
            {
                if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record)
                    || record.DraftProc == null)
                {
                    throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", $"冻结预演不存在或已过期：{previewId}");
                }
                return new PatchExecutionResult
                {
                    ProcIndex = record.ProcIndex,
                    Proc = ObjectGraphCloner.Clone(record.DraftProc),
                    Messages = new List<string>(record.PreviewMessages ?? new List<string>()),
                    Changes = record.PreviewChanges == null ? new JArray() : (JArray)record.PreviewChanges.DeepClone(),
                    AffectedOps = record.AffectedOps == null
                        ? new List<(int stepIndex, int opIndex, ProcChangeKind kind)>()
                        : new List<(int stepIndex, int opIndex, ProcChangeKind kind)>(record.AffectedOps)
                };
            }
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

        [System.Diagnostics.DebuggerNonUserCode]
        private void ValidateConfirmedPreview(string previewId, JObject patch)
        {
            ValidatePreviewIdFormat(previewId);
            JObject normalizedPatch = NormalizePatchForApproval(patch);
            PreviewApprovalRecord record;
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out record))
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

                EnsurePreviewProcVersion(record);
                if (!JToken.DeepEquals(record.Patch, normalizedPatch))
                {
                    throw new BridgeRequestException(409, "PREVIEW_PATCH_MISMATCH", "提交 Patch 与已确认预演不一致，请重新预演并确认。");
                }
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

        [System.Diagnostics.DebuggerNonUserCode]
        private static JObject NormalizePatchForApproval(JObject patch)
        {
            if (patch == null)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "Patch 请求不能为空。");
            }
            JObject normalized = (JObject)patch.DeepClone();
            normalized.Remove("previewId");
            return normalized;
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
            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
            ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                procIndex, proc, SF.frmProc?.procsList);
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
            if (SF.frmProc?.procsList == null)
            {
                return targets;
            }

            for (int procIndex = 0; procIndex < SF.frmProc.procsList.Count; procIndex++)
            {
                if (procIndexFilter.HasValue && procIndexFilter.Value != procIndex)
                {
                    continue;
                }

                Proc proc = SF.frmProc.procsList[procIndex];
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
            List<string> tcp = (SF.communicationStore?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            List<string> serial = (SF.communicationStore?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>())
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
        private static JArray LoadIntentTemplateCatalog()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, IntentTemplateCatalogRelativePath);
            if (!File.Exists(path))
            {
                throw new BridgeRequestException(500, "INTENT_TEMPLATE_MISSING", $"中间意图模板文件不存在：{path}");
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                if (!(root["templates"] is JArray templates))
                {
                    throw new BridgeRequestException(500, "INTENT_TEMPLATE_INVALID", "中间意图模板文件缺少 templates 数组。");
                }
                return templates;
            }
            catch (BridgeRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BridgeRequestException(500, "INTENT_TEMPLATE_INVALID", "中间意图模板文件读取失败。", ex.Message);
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject ConvertIntentToPatch(JObject intent)
        {
            string intentType = ReadRequiredString(intent, "intentType");
            int procIndex = ReadRequiredInt(intent, "procIndex");
            string baseProcId = ReadOptionalString(intent, "baseProcId");
            if (string.IsNullOrWhiteSpace(baseProcId))
            {
                baseProcId = GetProcByIndex(procIndex).head.Id.ToString("D");
            }

            JObject action = new JObject();
            switch (intentType)
            {
                case "update_proc_head_field":
                    action["type"] = "update_proc_head_fields";
                    action["fieldChanges"] = ReadRequiredObject(intent, "fieldChanges");
                    break;
                case "update_step_field":
                    action["type"] = "update_step_fields";
                    action["stepId"] = ReadRequiredString(intent, "stepId");
                    action["fieldChanges"] = ReadRequiredObject(intent, "fieldChanges");
                    break;
                case "update_operation_field":
                    action["type"] = "update_operation_fields";
                    action["stepId"] = ReadRequiredString(intent, "stepId");
                    action["opId"] = ReadRequiredString(intent, "opId");
                    CopyOptionalString(intent, action, "expectedOperaType");
                    action["fieldChanges"] = ReadRequiredObject(intent, "fieldChanges");
                    break;
                case "append_step":
                    action["type"] = "append_step";
                    CopyOptionalString(intent, action, "name");
                    CopyOptionalBoolean(intent, action, "disable");
                    break;
                case "insert_step":
                    action["type"] = "insert_step";
                    action["insertIndex"] = ReadRequiredInt(intent, "insertIndex");
                    CopyOptionalString(intent, action, "name");
                    CopyOptionalBoolean(intent, action, "disable");
                    break;
                case "delete_step":
                    action["type"] = "delete_step";
                    action["stepId"] = ReadRequiredString(intent, "stepId");
                    break;
                case "move_step":
                    action["type"] = "move_step";
                    action["stepId"] = ReadRequiredString(intent, "stepId");
                    action["targetIndex"] = ReadRequiredInt(intent, "targetIndex");
                    break;
                case "append_operation":
                    action["type"] = "append_operation";
                    action["stepId"] = ReadRequiredString(intent, "stepId");
                    action["operaType"] = ReadRequiredString(intent, "operaType");
                    CopyOptionalObject(intent, action, "fieldValues");
                    break;
                case "insert_operation":
                    action["type"] = "insert_operation";
                    action["stepId"] = ReadRequiredString(intent, "stepId");
                    action["insertIndex"] = ReadRequiredInt(intent, "insertIndex");
                    action["operaType"] = ReadRequiredString(intent, "operaType");
                    CopyOptionalObject(intent, action, "fieldValues");
                    break;
                case "delete_operation":
                    action["type"] = "delete_operation";
                    action["stepId"] = ReadRequiredString(intent, "stepId");
                    action["opId"] = ReadRequiredString(intent, "opId");
                    CopyOptionalString(intent, action, "expectedOperaType");
                    break;
                case "move_operation":
                    action["type"] = "move_operation";
                    action["stepId"] = ReadRequiredString(intent, "stepId");
                    action["opId"] = ReadRequiredString(intent, "opId");
                    action["targetIndex"] = ReadRequiredInt(intent, "targetIndex");
                    CopyOptionalString(intent, action, "targetStepId");
                    CopyOptionalString(intent, action, "expectedOperaType");
                    break;
                default:
                    throw new BridgeRequestException(400, "INTENT_UNSUPPORTED", $"不支持的中间意图类型：{intentType}");
            }

            return new JObject
            {
                ["procIndex"] = procIndex,
                ["baseProcId"] = baseProcId,
                ["actions"] = new JArray(action)
            };
        }

        private static void CopyOptionalString(JObject source, JObject target, string fieldName)
        {
            string value = ReadOptionalString(source, fieldName);
            if (value != null)
            {
                target[fieldName] = value;
            }
        }

        private static void CopyOptionalBoolean(JObject source, JObject target, string fieldName)
        {
            bool? value = ReadOptionalBoolean(source, fieldName);
            if (value.HasValue)
            {
                target[fieldName] = value.Value;
            }
        }

        private static void CopyOptionalObject(JObject source, JObject target, string fieldName)
        {
            JObject value = ReadOptionalObject(source, fieldName);
            if (value != null)
            {
                target[fieldName] = value.DeepClone();
            }
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
        private static void EnsureRuntimeReady()
        {
            if (SF.mainfrm == null || SF.frmProc?.procsList == null || SF.frmInspector == null)
            {
                throw new BridgeRequestException(503, "BRIDGE_NOT_READY", "Automation 运行时尚未完成初始化。");
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static Proc GetProcByIndex(int procIndex)
        {
            EnsureRuntimeReady();
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                throw new BridgeRequestException(404, "PROC_NOT_FOUND", $"流程索引无效：{procIndex}");
            }

            Proc proc = SF.frmProc.procsList[procIndex];
            if (proc == null)
            {
                throw new BridgeRequestException(404, "PROC_NOT_FOUND", $"流程不存在：{procIndex}");
            }

            return proc;
        }

        private static bool TryGetProcByIndexForRead(int procIndex, out Proc proc, out JObject error)
        {
            proc = null;
            error = null;
            if (SF.mainfrm == null || SF.frmProc?.procsList == null || SF.frmInspector == null)
            {
                error = BridgeError(503, "BRIDGE_NOT_READY", "Automation 运行时尚未完成初始化。");
                return false;
            }
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                error = BridgeError(
                    404,
                    "PROC_NOT_FOUND",
                    $"已提交的流程中不存在索引 {procIndex}；当前流程数为 {SF.frmProc.procsList.Count}。",
                    new JObject
                    {
                        ["reason"] = "committed_process_not_found",
                        ["retryableWhen"] = "valid_committed_proc_index_provided",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
                return false;
            }
            proc = SF.frmProc.procsList[procIndex];
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
        private static string ReadRequiredActionType(JObject action, int actionIndex)
        {
            if (!action.TryGetValue("type", out JToken token) || token == null || token.Type == JTokenType.Null)
            {
                if (action.TryGetValue("action", out JToken legacyToken)
                    && legacyToken != null
                    && legacyToken.Type == JTokenType.String)
                {
                    throw new BridgeRequestException(
                        400,
                        "INVALID_ARGUMENT",
                        $"actions[{actionIndex}] 必须使用字段 type；当前请求使用了旧字段 action={legacyToken.Value<string>()}。");
                }

                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"actions[{actionIndex}].type 必须是字符串。");
            }

            if (token.Type != JTokenType.String)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"actions[{actionIndex}].type 必须是字符串。");
            }

            return token.Value<string>();
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static void ThrowUnsupportedPatchAction(string actionType, int actionIndex)
        {
            if (PreviewOnlyChangeTypes.Contains(actionType))
            {
                throw new BridgeRequestException(
                    400,
                    "PATCH_UNSUPPORTED_ACTION",
                    $"actions[{actionIndex}].type={actionType} 不是可提交 Patch 动作；它属于 preview_patch 返回结果中的 changes 类型。apply_patch 必须复用原始 patch 的 actions，不能把 preview 返回的 changes 直接当作提交参数。");
            }

            throw new BridgeRequestException(
                400,
                "PATCH_UNSUPPORTED_ACTION",
                $"actions[{actionIndex}].type={actionType} 不受支持。允许的动作只有：{string.Join(", ", SupportedPatchActions)}");
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static JObject ReadRequiredFieldChanges(JObject action, int actionIndex, string actionType)
        {
            JObject fieldChanges = ReadOptionalObject(action, "fieldChanges");
            if (fieldChanges != null)
            {
                return fieldChanges;
            }

            JObject legacyFields = ReadOptionalObject(action, "fields");
            if (legacyFields != null)
            {
                throw new BridgeRequestException(
                    400,
                    "INVALID_ARGUMENT",
                    $"actions[{actionIndex}] 的 {actionType} 必须使用 fieldChanges；当前请求使用了旧字段 fields。");
            }

            throw new BridgeRequestException(
                400,
                "INVALID_ARGUMENT",
                $"actions[{actionIndex}] 的 {actionType}.fieldChanges 必须是对象。");
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static JObject ReadOptionalInsertFieldValues(JObject action, int actionIndex, string actionType)
        {
            JObject fieldValues = ReadOptionalObject(action, "fieldValues");
            if (fieldValues != null)
            {
                return fieldValues;
            }

            JObject legacyFields = ReadOptionalObject(action, "fields");
            if (legacyFields != null)
            {
                throw new BridgeRequestException(
                    400,
                    "INVALID_ARGUMENT",
                    $"actions[{actionIndex}] 的 {actionType} 必须使用 fieldValues；当前请求使用了旧字段 fields。");
            }

            return null;
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
        private static void WithOperationReadContext(OperationType op, Action action)
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

        private static T WithOperationReadContext<T>(OperationType op, Func<T> action)
        {
            return WithOperationContext(op, false, action);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static void WithOperationEditContext(OperationType op, Action action)
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

        private static T WithOperationEditContext<T>(OperationType op, Func<T> action)
        {
            return WithOperationContext(op, true, action);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static T WithOperationContext<T>(OperationType op, bool enableEditBehavior, Func<T> action)
        {
            if (op == null)
            {
                throw new BridgeRequestException(500, "OP_NULL", "指令为空。");
            }
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            OperationType originalTemp = SF.frmDataGrid?.OperationTemp;
            ModifyKind originalModify = SF.isModify;
            bool originalAddOps = SF.isAddOps;

            try
            {
                if (SF.frmDataGrid != null)
                {
                    SF.frmDataGrid.OperationTemp = op;
                }
                SF.isModify = enableEditBehavior ? ModifyKind.Operation : ModifyKind.None;
                SF.isAddOps = false;
                return action();
            }
            finally
            {
                if (SF.frmDataGrid != null)
                {
                    SF.frmDataGrid.OperationTemp = originalTemp;
                }
                SF.isModify = originalModify;
                SF.isAddOps = originalAddOps;
            }
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

        private sealed class PatchExecutionResult
        {
            public int ProcIndex { get; set; }

            public Proc Proc { get; set; }

            public List<string> Messages { get; set; } = new List<string>();

            public JArray Changes { get; set; } = new JArray();

            // 记录每个被改动指令的 (stepIndex, opIndex, kind)，供 FrmDataGrid 行级闪烁使用。
            // 为空时降级为整体闪烁。
            public List<(int stepIndex, int opIndex, ProcChangeKind kind)> AffectedOps { get; set; } = new List<(int, int, ProcChangeKind)>();
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

            public List<string> PreviewMessages { get; set; }

            public JArray PreviewChanges { get; set; }

            public List<(int stepIndex, int opIndex, ProcChangeKind kind)> AffectedOps { get; set; }

            public AiChangeSetCompileResult AiChangeSetPreview { get; set; }

            public MigrationConfigurationPreview MigrationConfigurationPreview { get; set; }

            public string BaseStateHash { get; set; }

            public DateTime? ConfirmedAtUtc { get; set; }
        }

        private sealed class MigrationConfigurationPreview
        {
            public string Kind { get; set; }

            public string BaseStateHash { get; set; }

            public FrmCard.Card Cards { get; set; }

            public List<List<IO>> IoMap { get; set; }

            public IODebugMap IoDebug { get; set; }

            public PlcConfiguration Plc { get; set; }

            public List<SocketInfo> Sockets { get; set; }

            public List<SerialPortInfo> SerialPorts { get; set; }
        }

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

        private sealed class CommReferenceCatalog
        {
            public List<string> All { get; set; } = new List<string>();

            public List<string> Tcp { get; set; } = new List<string>();

            public List<string> Serial { get; set; } = new List<string>();
        }
    }
}
