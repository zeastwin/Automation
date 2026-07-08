using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using static System.ComponentModel.TypeConverter;

namespace Automation.Bridge
{
    internal sealed class AutomationBridgeService
    {
        private const string IntentTemplateCatalogRelativePath = @"IntentTemplates\intent_templates.json";

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

        public AutomationBridgeService(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
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
                    case "/bridge/procs/list":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("proc.list", ExecuteOnUiThread(() => HandleListProcs(request))));
                    case "/bridge/procs/overview":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("proc.overview", ExecuteOnUiThread(() => HandleGetProcOverview(request))));
                    case "/bridge/procs/detail":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("proc.detail", ExecuteOnUiThread(() => HandleGetProcDetail(request))));
                    case "/bridge/operations/types":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("operation.types", ExecuteOnUiThread(HandleListOperationTypes)));
                    case "/bridge/operations/schema":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("operation.schema", ExecuteOnUiThread(() => HandleGetOperationSchema(request))));
                    case "/bridge/references/catalog":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("reference.catalog", ExecuteOnUiThread(() => HandleGetReferenceCatalog(request))));
                    case "/bridge/intents/catalog":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("intent.catalog", ExecuteOnUiThread(() => HandleListIntentTemplates(request))));
                    case "/bridge/intents/template":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("intent.template", ExecuteOnUiThread(() => HandleGetIntentTemplate(request))));
                    case "/bridge/intents/build-patch":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("intent.patch", ExecuteOnUiThread(() => HandleBuildPatchFromIntent(request))));
                    case "/bridge/intents/preview":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("intent.preview", ExecuteOnUiThread(() => HandlePreviewIntent(request))));
                    case "/bridge/intents/apply":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("intent.apply", ExecuteOnUiThread(() => HandleApplyIntent(request))));
                    case "/bridge/previews/confirm":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("preview.confirm", ExecuteOnUiThread(() => HandleConfirmPreview(request))));
                    case "/bridge/patch/preview":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("patch.preview", ExecuteOnUiThread(() => HandlePreviewPatch(request))));
                    case "/bridge/patch/apply":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("patch.apply", ExecuteOnUiThread(() => HandleApplyPatch(request))));
                    case "/bridge/runtime/snapshot":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("runtime.snapshot", ExecuteOnUiThread(() => HandleGetRuntimeSnapshot(request))));
                    case "/bridge/info-log/tail":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("infoLog.tail", ExecuteOnUiThread(() => HandleGetInfoLogTail(request))));
                    case "/bridge/procs/diagnose":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("proc.diagnose", ExecuteOnUiThread(() => HandleDiagnoseProc(request))));
                    default:
                        throw new BridgeRequestException(404, "ENDPOINT_NOT_FOUND", $"未找到端点：{normalizedPath}");
                }
            }
            catch (BridgeRequestException ex)
            {
                return AutomationBridgeResponse.Error(ex.StatusCode, ex.Code, ex.Message, ex.Details);
            }
            catch (Exception ex)
            {
                return AutomationBridgeResponse.Error(500, "UNHANDLED_EXCEPTION", "Automation Bridge 处理失败。", ex.Message);
            }
        }

        public static string BuildErrorBody(string code, string message, string details = null)
        {
            return new JObject
            {
                ["ok"] = false,
                ["type"] = "bridge.error",
                ["errorCode"] = code ?? string.Empty,
                ["message"] = message ?? string.Empty,
                ["details"] = string.IsNullOrWhiteSpace(details) ? null : details
            }.ToString(Formatting.None);
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

        private JObject HandleListProcs(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取流程列表");
            bool includeStepSummary = ReadOptionalBoolean(request, "includeStepSummary") ?? false;
            EnsureRuntimeReady();

            var array = new JArray();
            for (int i = 0; i < SF.frmProc.procsList.Count; i++)
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
                ["items"] = array
            };
        }

        private JObject HandleGetProcOverview(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取流程摘要");
            int procIndex = ReadRequiredInt(request, "procIndex");
            Proc proc = GetProcByIndex(procIndex);
            return BuildProcOverview(procIndex, proc);
        }

        private JObject HandleGetProcDetail(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取流程详情");
            int procIndex = ReadRequiredInt(request, "procIndex");
            Proc proc = GetProcByIndex(procIndex);
            return BuildProcDetail(procIndex, proc);
        }

        private JObject HandleListOperationTypes()
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取指令类型");
            EnsureRuntimeReady();
            JArray items = new JArray();
            foreach (OperationType template in SF.frmPropertyGrid.OperationTypeList.OfType<OperationType>())
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

        private JObject HandleGetOperationSchema(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取指令 Schema");
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
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", "读取现有指令实例时必须提供 procIndex。");
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
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", "读取指令类型 Schema 时必须提供 operaType。");
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

        private JObject HandleGetReferenceCatalog(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取引用目录");
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
                ["plcDevices"] = new JArray((SF.plcStore?.Devices ?? Array.Empty<PlcDevice>())
                    .Where(device => device != null && !string.IsNullOrWhiteSpace(device.Name))
                    .Select(device => device.Name)),
                ["stations"] = stations,
                ["gotoTargets"] = BuildGotoTargets(procIndex)
            };
        }

        private JObject HandleListIntentTemplates(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取中间意图模板");
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

        private JObject HandleGetIntentTemplate(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取中间意图模板");
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
                throw new BridgeRequestException(404, "INTENT_TEMPLATE_NOT_FOUND", "未找到匹配的中间意图模板。");
            }

            return new JObject
            {
                ["template"] = matched.DeepClone()
            };
        }

        private JObject HandleBuildPatchFromIntent(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "构建 Patch");
            JObject intent = ReadIntentObject(request);
            JObject patch = ConvertIntentToPatch(intent);
            return new JObject
            {
                ["intentType"] = intent["intentType"]?.Value<string>() ?? string.Empty,
                ["patch"] = patch
            };
        }

        private JObject HandlePreviewIntent(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "预演中间意图");
            JObject intent = ReadIntentObject(request);
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

        private JObject HandleApplyIntent(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "提交中间意图");
            string previewId = ReadRequiredString(request, "previewId");
            JObject intent = ReadIntentObject(request);
            JObject patch = ConvertIntentToPatch(intent);
            ValidateConfirmedPreview(previewId, patch);
            PatchExecutionResult result = ExecutePatch(patch);
            CommitPatch(result.ProcIndex, result.Proc);
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

        private JObject HandleConfirmPreview(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "确认预演结果");
            string previewId = ReadRequiredString(request, "previewId");
            PreviewApprovalRecord record;
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out record))
                {
                    throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                }

                EnsurePreviewProcVersion(record);
                record.Confirmed = true;
                record.ConfirmedAtUtc = DateTime.UtcNow;
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

        private JObject HandlePreviewPatch(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "预演 Patch");
            PatchExecutionResult result = ExecutePatch(request);
            return BuildRegisteredPatchPreview(request, result);
        }

        private JObject HandleApplyPatch(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "提交 Patch");
            string previewId = ReadRequiredString(request, "previewId");
            ValidateConfirmedPreview(previewId, request);
            PatchExecutionResult result = ExecutePatch(request);
            CommitPatch(result.ProcIndex, result.Proc);
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

        private JObject HandleGetRuntimeSnapshot(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取运行快照");
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

        private JObject HandleGetInfoLogTail(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取运行日志");
            int maxCount = ReadOptionalInt(request, "maxCount") ?? 50;
            if (maxCount <= 0 || maxCount > 200)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "maxCount 必须在 1..200 范围内。");
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

        private JObject HandleDiagnoseProc(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "诊断流程");
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

            IEnumerable<OperationType> operationTypes = SF.frmPropertyGrid?.OperationTypeList?.OfType<OperationType>()
                ?? Enumerable.Empty<OperationType>();
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

            foreach (string error in FrmProc.ValidateProcGotoTargets(procIndex, proc))
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

            Proc draft = FrmPropertyGrid.DeepCopy(current);
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

            List<string> errors = new List<string>();
            SF.frmProc.NormalizeProc(procIndex, draft, errors);
            if (errors.Count > 0)
            {
                throw new BridgeRequestException(400, "PATCH_VALIDATE_FAILED", "Patch 预演失败。", string.Join("\r\n", errors.Distinct()));
            }

            return result;
        }

        private void CommitPatch(int procIndex, Proc draft)
        {
            if (draft == null)
            {
                throw new BridgeRequestException(500, "PATCH_EMPTY", "Patch 结果为空。");
            }

            if (!owner.SaveAsJson(SF.workPath, procIndex.ToString(CultureInfo.InvariantCulture), draft))
            {
                throw new BridgeRequestException(500, "SAVE_FAILED", "流程保存失败。");
            }

            SF.frmProc.procsList[procIndex] = draft;
            if (!SF.PublishProc(procIndex))
            {
                throw new BridgeRequestException(500, "PUBLISH_FAILED", "流程发布失败。");
            }

            SF.frmProc.Refresh();
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

        private void ApplyOperationUpdate(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Step step = FindStepById(draft, ParseGuid(ReadRequiredString(action, "stepId"), "stepId"));
            OperationType op = FindOperationById(step, ParseGuid(ReadRequiredString(action, "opId"), "opId"));
            ValidateExpectedOperaType(action, op);

            JObject fieldChanges = ReadRequiredFieldChanges(action, actionIndex, "update_operation_fields");
            ApplyOperationPropertyChanges(op, fieldChanges, $"指令[{op.Name}]", actionIndex, result);
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
            Proc before = FrmPropertyGrid.DeepCopy(draft);

            draft.steps.RemoveAt(stepIndex);
            RewriteGotoTargetsAfterStructureChange(before, draft, result.ProcIndex, actionIndex, result);

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

        private static void ValidateExpectedOperaType(JObject action, OperationType op)
        {
            string expectedOperaType = ReadOptionalString(action, "expectedOperaType");
            if (!string.IsNullOrWhiteSpace(expectedOperaType)
                && !string.Equals(expectedOperaType, op?.OperaType, StringComparison.Ordinal))
            {
                throw new BridgeRequestException(409, "PATCH_TARGET_MISMATCH", $"指令类型不匹配，期望 {expectedOperaType}，实际 {op?.OperaType ?? string.Empty}。");
            }
        }

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

        private static int ReadRequiredInsertIndex(JObject request, string fieldName, int maxInclusive, string label)
        {
            int value = ReadRequiredInt(request, fieldName);
            if (value < 0 || value > maxInclusive)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"{label}超出范围：{value}");
            }
            return value;
        }

        private void RewriteGotoTargetsAfterStructureChange(Proc before, Proc after, int procIndex, int actionIndex, PatchExecutionResult result)
        {
            Dictionary<Guid, GotoLocation> newLocations = BuildOperationLocationMap(after);
            GotoRewriteSummary summary = new GotoRewriteSummary();
            if (after?.steps != null)
            {
                foreach (Step step in after.steps)
                {
                    if (step?.Ops == null)
                    {
                        continue;
                    }

                    foreach (OperationType op in step.Ops)
                    {
                        if (op == null)
                        {
                            continue;
                        }
                        RewriteGotoTargetsRecursive(op, before, after, procIndex, newLocations, summary);
                    }
                }
            }

            if (summary.RewrittenCount == 0 && summary.FallbackCount == 0 && summary.ClearedCount == 0)
            {
                return;
            }

            result.Messages.Add($"动作{actionIndex}：已重写跳转 {summary.RewrittenCount} 个，回退 {summary.FallbackCount} 个，清空 {summary.ClearedCount} 个。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = actionIndex,
                ["type"] = "goto_rewrite",
                ["rewrittenCount"] = summary.RewrittenCount,
                ["fallbackCount"] = summary.FallbackCount,
                ["clearedCount"] = summary.ClearedCount
            });
        }

        private void RewriteGotoTargetsRecursive(
            object currentObject,
            Proc beforeProc,
            Proc afterProc,
            int procIndex,
            Dictionary<Guid, GotoLocation> newLocations,
            GotoRewriteSummary summary)
        {
            if (currentObject == null)
            {
                return;
            }

            foreach (var propertyInfo in currentObject.GetType().GetProperties())
            {
                if (propertyInfo.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (propertyInfo.PropertyType == typeof(string)
                    && propertyInfo.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string currentValue = propertyInfo.GetValue(currentObject) as string;
                    RewriteSingleGotoTarget(currentObject, propertyInfo, currentValue, beforeProc, afterProc, procIndex, newLocations, summary);
                }

                var propertyValue = propertyInfo.GetValue(currentObject);
                if (propertyValue is System.Collections.IEnumerable enumerable && !(propertyValue is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null)
                        {
                            continue;
                        }
                        RewriteGotoTargetsRecursive(item, beforeProc, afterProc, procIndex, newLocations, summary);
                    }
                }
            }
        }

        private void RewriteSingleGotoTarget(
            object currentObject,
            System.Reflection.PropertyInfo propertyInfo,
            string currentValue,
            Proc beforeProc,
            Proc afterProc,
            int procIndex,
            Dictionary<Guid, GotoLocation> newLocations,
            GotoRewriteSummary summary)
        {
            if (string.IsNullOrWhiteSpace(currentValue))
            {
                return;
            }
            if (!FrmProc.TryParseGotoKey(currentValue, out int gotoProc, out int gotoStep, out int gotoOp) || gotoProc != procIndex)
            {
                return;
            }

            if (TryResolveTargetOperationId(beforeProc, gotoStep, gotoOp, out Guid targetOpId)
                && newLocations.TryGetValue(targetOpId, out GotoLocation currentTarget))
            {
                string newValue = BuildGotoKey(procIndex, currentTarget.StepIndex, currentTarget.OpIndex);
                if (!string.Equals(newValue, currentValue, StringComparison.Ordinal))
                {
                    propertyInfo.SetValue(currentObject, newValue);
                    summary.RewrittenCount++;
                }
                return;
            }

            GotoLocation fallback = FindClosestOperationLocation(afterProc, gotoStep, gotoOp);
            if (fallback != null)
            {
                string fallbackValue = BuildGotoKey(procIndex, fallback.StepIndex, fallback.OpIndex);
                propertyInfo.SetValue(currentObject, fallbackValue);
                summary.FallbackCount++;
                return;
            }

            propertyInfo.SetValue(currentObject, string.Empty);
            summary.ClearedCount++;
        }

        private static bool TryResolveTargetOperationId(Proc proc, int stepIndex, int opIndex, out Guid opId)
        {
            opId = Guid.Empty;
            if (proc?.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                return false;
            }

            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex < 0 || opIndex >= step.Ops.Count)
            {
                return false;
            }

            OperationType op = step.Ops[opIndex];
            if (op == null || op.Id == Guid.Empty)
            {
                return false;
            }

            opId = op.Id;
            return true;
        }

        private static Dictionary<Guid, GotoLocation> BuildOperationLocationMap(Proc proc)
        {
            Dictionary<Guid, GotoLocation> map = new Dictionary<Guid, GotoLocation>();
            if (proc?.steps == null)
            {
                return map;
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
                    if (op == null || op.Id == Guid.Empty)
                    {
                        continue;
                    }

                    map[op.Id] = new GotoLocation
                    {
                        StepIndex = stepIndex,
                        OpIndex = opIndex
                    };
                }
            }

            return map;
        }

        private static GotoLocation FindClosestOperationLocation(Proc proc, int preferredStepIndex, int preferredOpIndex)
        {
            if (proc?.steps == null)
            {
                return null;
            }

            GotoLocation best = null;
            int bestStepDistance = int.MaxValue;
            int bestOpDistance = int.MaxValue;

            for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                if (step?.Ops == null)
                {
                    continue;
                }

                for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                {
                    int stepDistance = Math.Abs(stepIndex - preferredStepIndex);
                    int opDistance = Math.Abs(opIndex - preferredOpIndex);

                    if (best == null
                        || stepDistance < bestStepDistance
                        || (stepDistance == bestStepDistance && opDistance < bestOpDistance))
                    {
                        best = new GotoLocation
                        {
                            StepIndex = stepIndex,
                            OpIndex = opIndex
                        };
                        bestStepDistance = stepDistance;
                        bestOpDistance = opDistance;
                    }
                }
            }

            return best;
        }

        private static string BuildGotoKey(int procIndex, int stepIndex, int opIndex)
        {
            return $"{procIndex}-{stepIndex}-{opIndex}";
        }

        private void ApplyMoveStep(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Guid stepId = ParseGuid(ReadRequiredString(action, "stepId"), "stepId");
            int sourceIndex = FindStepIndexById(draft, stepId);
            Step step = draft.steps[sourceIndex];
            Proc before = FrmPropertyGrid.DeepCopy(draft);

            draft.steps.RemoveAt(sourceIndex);
            int targetIndex = ReadRequiredInsertIndex(action, "targetIndex", draft.steps.Count, "步骤目标位置");
            draft.steps.Insert(targetIndex, step);
            RewriteGotoTargetsAfterStructureChange(before, draft, result.ProcIndex, actionIndex, result);

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

            Proc before = FrmPropertyGrid.DeepCopy(draft);
            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = ReadOptionalString(action, "name") ?? $"步骤{insertIndex}",
                Disable = ReadOptionalBoolean(action, "disable") ?? false,
                Ops = new List<OperationType>()
            };

            draft.steps.Insert(insertIndex, step);
            RewriteGotoTargetsAfterStructureChange(before, draft, result.ProcIndex, actionIndex, result);

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
            Proc before = FrmPropertyGrid.DeepCopy(draft);

            step.Ops.RemoveAt(opIndex);
            RewriteGotoTargetsAfterStructureChange(before, draft, result.ProcIndex, actionIndex, result);

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
            Proc before = FrmPropertyGrid.DeepCopy(draft);

            sourceStep.Ops.RemoveAt(sourceOpIndex);
            int targetStepIndex = FindStepIndexById(draft, targetStepId);
            Step targetStep = draft.steps[targetStepIndex];
            EnsureStepOps(targetStep);
            int targetIndex = ReadRequiredInsertIndex(action, "targetIndex", targetStep.Ops.Count, "指令目标位置");
            targetStep.Ops.Insert(targetIndex, op);
            RewriteGotoTargetsAfterStructureChange(before, draft, result.ProcIndex, actionIndex, result);

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
            Proc before = FrmPropertyGrid.DeepCopy(draft);

            step.Ops.Insert(insertIndex, op);
            RewriteGotoTargetsAfterStructureChange(before, draft, result.ProcIndex, actionIndex, result);

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
        }

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
            return new JObject
            {
                ["mode"] = mode,
                ["procIndex"] = result.ProcIndex,
                ["procId"] = result.Proc?.head?.Id.ToString("D"),
                ["messages"] = new JArray(result.Messages.Select(item => (JToken)item)),
                ["changes"] = result.Changes,
                ["procDetail"] = BuildProcDetail(result.ProcIndex, result.Proc)
            };
        }

        private JObject BuildRegisteredPatchPreview(JObject patch, PatchExecutionResult result)
        {
            PreviewApprovalRecord record = RegisterPreview(patch, result);
            JObject preview = BuildPatchResult("preview", result);
            preview["previewId"] = record.PreviewId;
            preview["patchHash"] = record.PatchHash;
            preview["confirmed"] = false;
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
                Confirmed = false
            };

            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                previewRecords[record.PreviewId] = record;
            }

            return record;
        }

        private void ValidateConfirmedPreview(string previewId, JObject patch)
        {
            JObject normalizedPatch = NormalizePatchForApproval(patch);
            PreviewApprovalRecord record;
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out record))
                {
                    throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                }

                if (!record.Confirmed)
                {
                    throw new BridgeRequestException(403, "PREVIEW_NOT_CONFIRMED", "预演结果尚未由 Automation 前台确认，禁止提交。");
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

        private void EnsurePreviewProcVersion(PreviewApprovalRecord record)
        {
            Proc current = GetProcByIndex(record.ProcIndex);
            string currentProcId = current.head?.Id.ToString("D") ?? string.Empty;
            if (!string.Equals(currentProcId, record.BaseProcId, StringComparison.OrdinalIgnoreCase))
            {
                throw new BridgeRequestException(409, "PROC_VERSION_MISMATCH", "流程版本已变化，请重新读取流程详情并重新预演。");
            }
        }

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
                ["stepCount"] = proc?.steps?.Count ?? 0,
                ["steps"] = steps
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
                            opDetails.Add(new JObject
                            {
                                ["opIndex"] = opIndex,
                                ["opId"] = op?.Id.ToString("D"),
                                ["name"] = op?.Name ?? string.Empty,
                                ["operaType"] = op?.OperaType ?? string.Empty,
                                ["disable"] = op?.Disable ?? false,
                                ["summary"] = op == null ? string.Empty : BuildOperationSummary(op),
                                ["fields"] = op == null ? new JObject() : BuildOperationFields(op)
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
                    if (descriptor == null || !descriptor.IsBrowsable)
                    {
                        continue;
                    }

                    fields.Add(new JObject
                    {
                        ["key"] = descriptor.Name,
                        ["displayName"] = descriptor.DisplayName,
                        ["category"] = descriptor.Category,
                        ["description"] = descriptor.Description ?? string.Empty,
                        ["dataType"] = GetTypeLabel(descriptor.PropertyType),
                        ["readOnly"] = descriptor.IsReadOnly,
                        ["referenceType"] = GetReferenceType(descriptor.Converter?.GetType().Name),
                        ["enumValues"] = BuildStandardValues(descriptor),
                        ["currentValue"] = ConvertValueToToken(descriptor.GetValue(op))
                    });
                }

                return new JObject
                {
                    ["operaType"] = op.OperaType ?? string.Empty,
                    ["name"] = op.Name ?? string.Empty,
                    ["fields"] = fields
                };
            });
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
                        || string.Equals(descriptor.Name, "isStopPoint", StringComparison.Ordinal))
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
                ["updateTime"] = snapshot.UpdateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                ["updateTicks"] = snapshot.UpdateTicks
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

        private static void EnsureBridgePermission(string permissionKey, string action)
        {
            if (!SF.HasPermission(permissionKey))
            {
                throw new BridgeRequestException(403, "PERMISSION_DENIED", $"当前账号无权限：{action}");
            }
        }

        private CommReferenceCatalog GetCommNames()
        {
            List<string> tcp = (SF.frmComunication?.socketInfos ?? new List<SocketInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            List<string> serial = (SF.frmComunication?.serialPortInfos ?? new List<SerialPortInfo>())
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

        private static JObject ReadIntentObject(JObject request)
        {
            JObject intent = ReadOptionalObject(request, "intent");
            if (intent != null)
            {
                return (JObject)intent.DeepClone();
            }

            string intentJson = ReadOptionalString(request, "intentJson");
            if (string.IsNullOrWhiteSpace(intentJson))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "字段 intentJson 必须是字符串，或直接提供 intent 对象。");
            }

            try
            {
                JToken token = JToken.Parse(intentJson);
                if (!(token is JObject obj))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", "intentJson 必须是 JSON 对象。");
                }
                return obj;
            }
            catch (BridgeRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "intentJson 不是合法 JSON。", ex.Message);
            }
        }

        private static JObject ConvertIntentToPatch(JObject intent)
        {
            string intentType = ReadRequiredString(intent, "intentType");
            int procIndex = ReadRequiredInt(intent, "procIndex");
            string baseProcId = ReadRequiredString(intent, "baseProcId");

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

        private static void EnsureRuntimeReady()
        {
            if (SF.mainfrm == null || SF.frmProc?.procsList == null || SF.frmPropertyGrid == null)
            {
                throw new BridgeRequestException(503, "BRIDGE_NOT_READY", "Automation 运行时尚未完成初始化。");
            }
        }

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

        private static OperationType CreateOperationTemplate(string operaType)
        {
            EnsureRuntimeReady();
            OperationType template = SF.frmPropertyGrid.OperationTypeList
                .OfType<OperationType>()
                .FirstOrDefault(item => item != null && string.Equals(item.OperaType, operaType, StringComparison.Ordinal));

            if (template == null)
            {
                throw new BridgeRequestException(404, "OPERA_TYPE_NOT_FOUND", $"未找到指令类型：{operaType}");
            }

            return (OperationType)template.Clone();
        }

        private static Guid ParseGuid(string text, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 不能为空。");
            }

            if (!Guid.TryParse(text, out Guid value))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 不是合法 Guid。");
            }

            return value;
        }

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

        private static int ReadRequiredInt(JObject request, string fieldName)
        {
            if (!request.TryGetValue(fieldName, out JToken token) || token == null || token.Type != JTokenType.Integer)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是整数。");
            }
            return token.Value<int>();
        }

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

        private static string ReadRequiredString(JObject request, string fieldName)
        {
            string value = ReadOptionalString(request, fieldName);
            if (value == null)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是字符串。");
            }
            return value;
        }

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

        private static JObject ReadRequiredObject(JObject request, string fieldName)
        {
            JObject value = ReadOptionalObject(request, fieldName);
            if (value == null)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是对象。");
            }
            return value;
        }

        private static JArray ReadRequiredArray(JObject request, string fieldName)
        {
            if (!request.TryGetValue(fieldName, out JToken token) || !(token is JArray array))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是数组。");
            }
            return array;
        }

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
            op.evtRP?.Invoke();
            TypeDescriptor.Refresh(op);
        }

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

        private static void ValidateStandardValue(PropertyDescriptor descriptor, object value, string targetLabel, string fieldName)
        {
            if (descriptor?.Converter == null || value == null)
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

        private static string GetReferenceType(string converterTypeName)
        {
            switch (converterTypeName)
            {
                case "IoOutItem":
                    return "io.output";
                case "IoInItem":
                    return "io.input";
                case "IoItem":
                    return "io.all";
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
        }

        private sealed class GotoLocation
        {
            public int StepIndex { get; set; }

            public int OpIndex { get; set; }
        }

        private sealed class GotoRewriteSummary
        {
            public int RewrittenCount { get; set; }

            public int FallbackCount { get; set; }

            public int ClearedCount { get; set; }
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
