using Automation.Protocol;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。
// 排查入口：工具存在但请求未到业务 handler 时，核对 route、method、线程切换和本文件的分发表。

using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Text;

namespace Automation.Bridge
{
    internal sealed partial class AutomationBridgeService
    {
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

    }
}
