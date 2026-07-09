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

        /// <summary>
        /// 流程被 AI 改动后，在 FrmProc 节点和 FrmDataGrid 上触发闪烁动效，让用户直观看到改动位置。
        /// kind 决定颜色：Modified=橙黄、Added=浅绿、Deleted=浅红。
        /// FrmDataGrid 仅在用户当前正在浏览的流程 == 被改动的流程时才闪烁，
        /// 避免用户在查看别的流程时被打扰；FrmProc 节点闪烁不受影响（始终提示哪个流程被改）。
        /// affectedOps 非空时走行级闪烁（只闪被改动的指令行），为空时走整体闪烁。
        /// </summary>
        private void NotifyProcChanged(int procIndex, ProcChangeKind kind, List<(int stepIndex, int opIndex, ProcChangeKind kind)> affectedOps = null)
        {
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
                    case "/bridge/procs/manage/preview":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("proc.manage.preview", ExecuteOnUiThread(() => HandleManagePreview(request))));
                    case "/bridge/procs/manage/apply":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("proc.manage.apply", ExecuteOnUiThread(() => HandleManageApply(request))));
                    case "/bridge/procs/control":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("proc.control", ExecuteOnUiThread(() => HandleControlProc(request))));
                    case "/bridge/procs/diagnose":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("proc.diagnose", ExecuteOnUiThread(() => HandleDiagnoseProc(request))));
                    case "/bridge/operations/detail":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("operation.detail", ExecuteOnUiThread(() => HandleGetOperationDetail(request))));
                    case "/bridge/steps/detail":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("step.detail", ExecuteOnUiThread(() => HandleGetStepDetail(request))));
                    case "/bridge/operations/search":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("operation.search", ExecuteOnUiThread(() => HandleSearchOperations(request))));
                    case "/bridge/procs/validate":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("proc.validate", ExecuteOnUiThread(() => HandleValidateProc(request))));
                    // === 资源查询与操作扩展端点 ===
                    case "/bridge/variables/list":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("variable.list", ExecuteOnUiThread(() => HandleListVariables(request))));
                    case "/bridge/variables/get":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("variable.get", ExecuteOnUiThread(() => HandleGetVariable(request))));
                    case "/bridge/variables/search":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("variable.search", ExecuteOnUiThread(() => HandleSearchVariables(request))));
                    case "/bridge/variables/set":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("variable.set", ExecuteOnUiThread(() => HandleSetVariable(request))));
                    case "/bridge/variables/delete":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("variable.delete", ExecuteOnUiThread(() => HandleDeleteVariable(request))));
                    case "/bridge/data-structs/list":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("dataStruct.list", ExecuteOnUiThread(() => HandleListDataStructs(request))));
                    case "/bridge/data-structs/get":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("dataStruct.get", ExecuteOnUiThread(() => HandleGetDataStruct(request))));
                    case "/bridge/data-structs/search":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("dataStruct.search", ExecuteOnUiThread(() => HandleSearchDataStructs(request))));
                    case "/bridge/data-structs/set-field":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("dataStruct.setField", ExecuteOnUiThread(() => HandleSetDataStructField(request))));
                    case "/bridge/io/list":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("io.list", ExecuteOnUiThread(() => HandleListIo(request))));
                    case "/bridge/io/get":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("io.get", ExecuteOnUiThread(() => HandleGetIo(request))));
                    case "/bridge/io/search":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("io.search", ExecuteOnUiThread(() => HandleSearchIo(request))));
                    case "/bridge/io/state":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("io.state", ExecuteOnUiThread(() => HandleGetIoState(request))));
                    case "/bridge/alarms/list":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("alarm.list", ExecuteOnUiThread(() => HandleListAlarms(request))));
                    case "/bridge/plc/devices":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("plc.devices", ExecuteOnUiThread(() => HandleListPlcDevices(request))));
                    case "/bridge/cards/list":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("card.list", ExecuteOnUiThread(() => HandleListCards(request))));
                    case "/bridge/tray-points/list":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("trayPoint.list", ExecuteOnUiThread(() => HandleListTrayPoints(request))));
                    case "/bridge/communications/list":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("communication.list", ExecuteOnUiThread(() => HandleListCommunications(request))));
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

        // 流程级结构操作预演：创建/删除/重排/复制流程。
        // 与单流程 Patch 不同，这些操作影响 procsList 结构，需要独立的预演确认流程。
        private JObject HandleManagePreview(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "预演流程结构操作");
            string action = ReadRequiredString(request, "action");
            JObject preview;
            switch (action)
            {
                case "create_proc":
                    preview = PreviewCreateProc(request);
                    break;
                case "delete_procs":
                    preview = PreviewDeleteProcs(request);
                    break;
                case "reorder_proc":
                    preview = PreviewReorderProc(request);
                    break;
                case "copy_proc":
                    preview = PreviewCopyProc(request);
                    break;
                default:
                    throw new BridgeRequestException(400, "UNSUPPORTED_ACTION", $"不支持的流程结构操作：{action}");
            }
            // 完全权限模式下预演已在 RegisterManagePreview 中自动确认，AI 可直接提交。
            preview["confirmed"] = SF.frmAiAssistant?.IsFullPermissionMode == true;
            return preview;
        }

        private JObject HandleManageApply(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "提交流程结构操作");
            string previewId = ReadRequiredString(request, "previewId");
            // 流程结构操作不修改单个 proc 的 patch，而是操作 procsList。
            // 复用预演确认机制：确认后即可执行。
            ValidateConfirmedManagePreview(previewId);
            string action = ReadRequiredString(request, "action");
            JObject result;
            switch (action)
            {
                case "create_proc":
                    result = ExecuteCreateProc(request);
                    break;
                case "delete_procs":
                    result = ExecuteDeleteProcs(request);
                    break;
                case "reorder_proc":
                    result = ExecuteReorderProc(request);
                    break;
                case "copy_proc":
                    result = ExecuteCopyProc(request);
                    break;
                default:
                    throw new BridgeRequestException(400, "UNSUPPORTED_ACTION", $"不支持的流程结构操作：{action}");
            }
            RemovePreview(previewId);
            return result;
        }

        // 流程运行控制：启动/停止/暂停/恢复。不需要预演确认。
        private JObject HandleControlProc(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "控制流程运行");
            EnsureRuntimeReady();
            int procIndex = ReadRequiredInt(request, "procIndex");
            string action = ReadRequiredString(request, "action");
            Proc proc = GetProcByIndex(procIndex);
            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
            ProcRunState currentState = snapshot?.State ?? ProcRunState.Stopped;

            switch (action)
            {
                case "start":
                    if (currentState == ProcRunState.Running || currentState == ProcRunState.Paused)
                    {
                        throw new BridgeRequestException(409, "PROC_ALREADY_RUNNING", $"流程 {procIndex} 已在运行或暂停状态。");
                    }
                    SF.DR.StartProc(proc, procIndex);
                    break;
                case "stop":
                    if (currentState == ProcRunState.Stopped)
                    {
                        throw new BridgeRequestException(409, "PROC_NOT_RUNNING", $"流程 {procIndex} 未在运行。");
                    }
                    SF.DR.Stop(procIndex);
                    break;
                case "pause":
                    if (currentState != ProcRunState.Running)
                    {
                        throw new BridgeRequestException(409, "PROC_NOT_RUNNING", $"流程 {procIndex} 不在运行状态，无法暂停。");
                    }
                    SF.DR.Pause(procIndex);
                    break;
                case "resume":
                    if (currentState != ProcRunState.Paused)
                    {
                        throw new BridgeRequestException(409, "PROC_NOT_PAUSED", $"流程 {procIndex} 不在暂停状态，无法恢复。");
                    }
                    SF.DR.Resume(procIndex);
                    break;
                default:
                    throw new BridgeRequestException(400, "UNSUPPORTED_ACTION", $"不支持的流程控制操作：{action}。支持：start, stop, pause, resume");
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
            return preview;
        }

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

            Proc copy = FrmPropertyGrid.DeepCopy(source);
            copy.head = new ProcHead
            {
                Name = string.IsNullOrWhiteSpace(newName) ? (source.head?.Name + "_副本") : newName,
                AutoStart = false,
                Disable = source.head?.Disable ?? false
            };

            int targetIndex = SF.frmProc.procsList.Count;
            JObject preview = new JObject
            {
                ["action"] = "copy_proc",
                ["sourceProcIndex"] = procIndex,
                ["sourceProcName"] = source.head?.Name ?? string.Empty,
                ["targetIndex"] = targetIndex,
                ["newProcName"] = copy.head.Name,
                ["stepCount"] = copy.steps?.Count ?? 0,
                ["messages"] = new JArray { $"将复制流程 {procIndex}「{source.head?.Name}」到索引 {targetIndex}，新名称「{copy.head.Name}」" }
            };

            string previewId = RegisterManagePreview(preview);
            preview["previewId"] = previewId;
            return preview;
        }

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
            SF.frmProc.NormalizeProc(procIndex, proc, errors);
            if (errors.Count > 0)
            {
                SF.frmProc.procsList.RemoveAt(procIndex);
                throw new BridgeRequestException(400, "PROC_VALIDATE_FAILED", "流程创建校验失败。", string.Join("\r\n", errors.Distinct()));
            }

            owner.SaveAsJson(SF.workPath, procIndex.ToString(CultureInfo.InvariantCulture), proc);
            SF.PublishProc(procIndex);
            SF.frmProc.Refresh();
            NotifyProcChanged(procIndex, ProcChangeKind.Added);

            return new JObject
            {
                ["action"] = "create_proc",
                ["procIndex"] = procIndex,
                ["procName"] = name,
                ["messages"] = new JArray { $"流程「{name}」已创建，索引 {procIndex}" }
            };
        }

        private JObject ExecuteDeleteProcs(JObject request)
        {
            JArray indexes = ReadRequiredArray(request, "procIndexes");
            // 从大到小删除，避免索引移位
            var sortedIndexes = indexes.Select(t => t.Value<int>()).OrderByDescending(i => i).ToList();

            var deleted = new JArray();
            foreach (int procIndex in sortedIndexes)
            {
                if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
                {
                    continue;
                }
                Proc proc = SF.frmProc.procsList[procIndex];
                string procName = proc?.head?.Name ?? procIndex.ToString();
                SF.frmProc.procsList.RemoveAt(procIndex);
                deleted.Add(new JObject { ["procIndex"] = procIndex, ["procName"] = procName });
            }

            // 重建工作配置文件
            int minDeleted = sortedIndexes.Min();
            SF.frmProc.RebuildWorkConfig(minDeleted);
            SF.frmProc.Refresh();
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
                ["remainingCount"] = SF.frmProc.procsList.Count,
                ["messages"] = new JArray { $"已删除 {deleted.Count} 个流程，剩余 {SF.frmProc.procsList.Count} 个" }
            };
        }

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

            Proc proc = SF.frmProc.procsList[procIndex];
            string procName = proc?.head?.Name ?? string.Empty;
            SF.frmProc.procsList.RemoveAt(procIndex);
            SF.frmProc.procsList.Insert(targetIndex, proc);

            // 重建工作配置
            int minIndex = Math.Min(procIndex, targetIndex);
            SF.frmProc.RebuildWorkConfig(minIndex);
            SF.frmProc.Refresh();
            NotifyProcChanged(targetIndex, ProcChangeKind.Modified);

            return new JObject
            {
                ["action"] = "reorder_proc",
                ["procName"] = procName,
                ["newProcIndex"] = targetIndex,
                ["messages"] = new JArray { $"流程「{procName}」已从索引 {procIndex} 移动到 {targetIndex}" }
            };
        }

        private JObject ExecuteCopyProc(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            string newName = ReadOptionalString(request, "newName");
            Proc source = GetProcByIndex(procIndex);

            Proc copy = FrmPropertyGrid.DeepCopy(source);
            copy.head = new ProcHead
            {
                Name = string.IsNullOrWhiteSpace(newName) ? (source.head?.Name + "_副本") : newName,
                AutoStart = false,
                Disable = source.head?.Disable ?? false
            };

            int newProcIndex = SF.frmProc.procsList.Count;
            // 重置流程内步骤和指令的 Id，避免重复
            ResetProcStepOpIds(copy);

            SF.frmProc.procsList.Add(copy);

            List<string> errors = new List<string>();
            SF.frmProc.NormalizeProc(newProcIndex, copy, errors);
            if (errors.Count > 0)
            {
                SF.frmProc.procsList.RemoveAt(newProcIndex);
                throw new BridgeRequestException(400, "PROC_VALIDATE_FAILED", "流程复制校验失败。", string.Join("\r\n", errors.Distinct()));
            }

            SF.frmProc.RebuildWorkConfig(newProcIndex);
            SF.frmProc.Refresh();
            NotifyProcChanged(newProcIndex, ProcChangeKind.Added);

            return new JObject
            {
                ["action"] = "copy_proc",
                ["sourceProcIndex"] = procIndex,
                ["newProcIndex"] = newProcIndex,
                ["newProcName"] = copy.head.Name,
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

        // 流程结构操作的预演记录，复用 previewLock 保证线程安全。
        private string RegisterManagePreview(JObject previewData)
        {
            string previewId = Guid.NewGuid().ToString("N");
            // 完全权限模式：直接标记预演为已确认，避免 FrmAiAssistant 通过 HTTP 回调确认导致 UI 线程死锁。
            bool autoConfirmed = SF.frmAiAssistant?.IsFullPermissionMode == true;
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
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
                    Confirmed = autoConfirmed
                };
                if (autoConfirmed)
                {
                    record.ConfirmedAtUtc = DateTime.UtcNow;
                }
                previewRecords[record.PreviewId] = record;
            }
            return previewId;
        }

        private void ValidateConfirmedManagePreview(string previewId)
        {
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record))
                {
                    throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                }
                if (!record.Confirmed)
                {
                    throw new BridgeRequestException(403, "PREVIEW_NOT_CONFIRMED", "预演结果尚未由 Automation 前台确认，禁止提交。");
                }
            }
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

        // 读取单条指令的完整详情：字段值、Schema、执行流向、跳转目标有效性。
        // 颗粒度介于 get_proc_detail 和 get_operation_schema 之间，适合聚焦分析某条指令。
        private JObject HandleGetOperationDetail(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取指令详情");
            int procIndex = ReadRequiredInt(request, "procIndex");
            int stepIndex = ReadRequiredInt(request, "stepIndex");
            int opIndex = ReadRequiredInt(request, "opIndex");

            Proc proc = GetProcByIndex(procIndex);
            if (proc.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                throw new BridgeRequestException(400, "STEP_NOT_FOUND", $"步骤索引越界：{stepIndex}");
            }
            Step step = proc.steps[stepIndex];
            if (step.Ops == null || opIndex < 0 || opIndex >= step.Ops.Count)
            {
                throw new BridgeRequestException(400, "OP_NOT_FOUND", $"指令索引越界：{opIndex}");
            }
            OperationType op = step.Ops[opIndex];
            bool isJump = IsJumpOperation(op?.OperaType);
            string flow = isJump
                ? "条件跳转（不自动流向下一条）"
                : (opIndex < step.Ops.Count - 1 ? $"执行后自动流向[{opIndex + 1}]" : "执行后步骤完成");

            // 检查该指令的跳转目标是否有效（仅跳转类指令）
            JArray gotoIssues = new JArray();
            if (isJump)
            {
                foreach (string error in FrmProc.ValidateProcGotoTargets(procIndex, proc))
                {
                    if (error.Contains($"{stepIndex}-{opIndex}") || error.Contains($"步骤 {stepIndex} 指令 {opIndex}"))
                    {
                        gotoIssues.Add(new JObject { ["message"] = error });
                    }
                }
            }

            return new JObject
            {
                ["procIndex"] = procIndex,
                ["stepIndex"] = stepIndex,
                ["opIndex"] = opIndex,
                ["opId"] = op?.Id.ToString("D"),
                ["name"] = op?.Name ?? string.Empty,
                ["operaType"] = op?.OperaType ?? string.Empty,
                ["disable"] = op?.Disable ?? false,
                ["isStopPoint"] = op?.isStopPoint ?? false,
                ["isJump"] = isJump,
                ["flow"] = flow,
                ["summary"] = op == null ? string.Empty : BuildOperationSummary(op),
                ["fields"] = op == null ? new JObject() : BuildOperationFields(op),
                ["gotoIssues"] = gotoIssues
            };
        }

        // 读取单个步骤的完整指令列表。介于 get_proc_overview 和 get_proc_detail 之间的颗粒度。
        private JObject HandleGetStepDetail(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "读取步骤详情");
            int procIndex = ReadRequiredInt(request, "procIndex");
            int stepIndex = ReadRequiredInt(request, "stepIndex");

            Proc proc = GetProcByIndex(procIndex);
            if (proc.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                throw new BridgeRequestException(400, "STEP_NOT_FOUND", $"步骤索引越界：{stepIndex}");
            }
            Step step = proc.steps[stepIndex];

            JArray opDetails = new JArray();
            if (step.Ops != null)
            {
                for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                {
                    OperationType op = step.Ops[opIndex];
                    bool isJump = IsJumpOperation(op?.OperaType);
                    string flow = isJump
                        ? "条件跳转（不自动流向下一条）"
                        : (opIndex < step.Ops.Count - 1 ? $"执行后自动流向[{opIndex + 1}]" : "执行后步骤完成");
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
                        ["fields"] = op == null ? new JObject() : BuildOperationFields(op)
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
        private JObject HandleSearchOperations(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "搜索指令");
            int? procIndex = ReadOptionalInt(request, "procIndex");
            string operaType = ReadOptionalString(request, "operaType");
            string keyword = ReadOptionalString(request, "keyword");

            IList<Proc> procs = SF.frmProc?.procsList;
            if (procs == null)
            {
                throw new BridgeRequestException(500, "PROCS_UNAVAILABLE", "流程列表不可用。");
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
        private JObject HandleValidateProc(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "验证流程结构");
            int procIndex = ReadRequiredInt(request, "procIndex");
            Proc proc = GetProcByIndex(procIndex);

            JArray errors = new JArray();
            JArray warnings = new JArray();

            // 1. 跳转目标有效性
            foreach (string error in FrmProc.ValidateProcGotoTargets(procIndex, proc))
            {
                errors.Add(new JObject { ["message"] = error });
            }

            // 2. 空步骤/指令检查
            if (proc.steps == null || proc.steps.Count == 0)
            {
                errors.Add(new JObject { ["message"] = "流程没有步骤。" });
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
                    }
                }
            }

            bool isValid = errors.Count == 0;
            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procName"] = proc.head?.Name ?? string.Empty,
                ["isValid"] = isValid,
                ["errorCount"] = errors.Count,
                ["warningCount"] = warnings.Count,
                ["errors"] = errors,
                ["warnings"] = warnings
            };
        }

        #region 资源查询与操作扩展 Handler

        // ===================== 变量操作 =====================

        private JObject HandleListVariables(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询变量列表");
            EnsureRuntimeReady();
            ValueConfigStore store = SF.valueStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            string typeFilter = request["type"]?.Value<string>();
            string nameLike = request["nameLike"]?.Value<string>();
            int offset = request["offset"]?.Value<int>() ?? 0;
            int limit = request["limit"]?.Value<int>() ?? 0;
            if (offset < 0) offset = 0;
            if (limit <= 0) limit = 1000;

            List<string> allNames = store.GetValueNames() ?? new List<string>();
            var items = new List<JObject>();
            int matched = 0;
            int skipped = 0;
            int taken = 0;
            foreach (string name in allNames)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(nameLike)
                    && name.IndexOf(nameLike, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (!store.TryGetValueByName(name, out DicValue val))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(typeFilter)
                    && !string.Equals(val.Type, typeFilter, StringComparison.OrdinalIgnoreCase))
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

        private JObject HandleGetVariable(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询变量");
            EnsureRuntimeReady();
            ValueConfigStore store = SF.valueStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            DicValue val = ResolveVariable(request, store);
            return BuildVariableJObject(val);
        }

        private JObject HandleSearchVariables(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "搜索变量");
            EnsureRuntimeReady();
            ValueConfigStore store = SF.valueStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            string keyword = request["keyword"]?.Value<string>() ?? string.Empty;
            string typeFilter = request["type"]?.Value<string>();
            string valueLike = request["valueLike"]?.Value<string>();
            int limit = request["limit"]?.Value<int>() ?? 100;
            if (limit <= 0) limit = 100;

            var items = new List<JObject>();
            List<string> allNames = store.GetValueNames() ?? new List<string>();
            foreach (string name in allNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                bool nameMatched = string.IsNullOrEmpty(keyword)
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
                ["type"] = typeFilter ?? string.Empty,
                ["valueLike"] = valueLike ?? string.Empty,
                ["returned"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        private JObject HandleSetVariable(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "修改变量");
            EnsureRuntimeReady();
            ValueConfigStore store = SF.valueStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            DicValue val = ResolveVariable(request, store);
            string newValue = request["value"]?.Value<string>();
            if (newValue == null)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "缺少 value 字段。");
            }
            if (string.Equals(val.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(newValue, out double dval))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"变量[{val.Name}] 是 double 类型，value 不是有效数字：{newValue}");
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
                ["message"] = $"变量[{val.Name}] 运行时值已更新为 {newValue}。"
            };
        }

        private JObject HandleDeleteVariable(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "删除变量");
            EnsureRuntimeReady();
            ValueConfigStore store = SF.valueStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            }
            int index = ReadRequiredInt(request, "index");
            if (index < 0 || index >= ValueConfigStore.ValueCapacity)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"index 超出范围 [0, {ValueConfigStore.ValueCapacity})。");
            }
            store.ClearValueByIndex(index);
            store.TryGetValueByIndex(index, out DicValue cleared);
            return new JObject
            {
                ["ok"] = true,
                ["index"] = index,
                ["variable"] = BuildVariableJObject(cleared),
                ["message"] = $"变量槽位 {index} 已清空（Name/Value/Note 重置）。"
            };
        }

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
                ["index"] = val.Index,
                ["name"] = val.Name ?? string.Empty,
                ["type"] = val.Type ?? string.Empty,
                ["runtimeValue"] = val.Value ?? string.Empty,
                ["configValue"] = val.ConfigValue ?? string.Empty,
                ["note"] = val.Note ?? string.Empty,
                ["isMark"] = val.isMark,
                ["lastChangedAt"] = val.LastChangedAt == default(DateTime) ? string.Empty : val.LastChangedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ["lastChangedBy"] = val.LastChangedBy ?? string.Empty,
                ["oldValue"] = val.LastChangedOldValue ?? string.Empty,
                ["newValue"] = val.LastChangedNewValue ?? string.Empty
            };
        }

        // ===================== 数据结构操作 =====================

        private JObject HandleListDataStructs(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询数据结构列表");
            EnsureRuntimeReady();
            DataStructStore store = SF.dataStructStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "数据结构存储未初始化。");
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

        private JObject HandleGetDataStruct(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询数据结构详情");
            EnsureRuntimeReady();
            DataStructStore store = SF.dataStructStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "数据结构存储未初始化。");
            }
            string name = ReadRequiredString(request, "name");
            if (!store.TryGetStructSnapshotByName(name, out DataStruct ds))
            {
                throw new BridgeRequestException(404, "DATA_STRUCT_NOT_FOUND", $"未找到数据结构：{name}");
            }
            return BuildDataStructJObject(name, ds);
        }

        private JObject HandleSearchDataStructs(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "搜索数据结构");
            EnsureRuntimeReady();
            DataStructStore store = SF.dataStructStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "数据结构存储未初始化。");
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
                throw new BridgeRequestException(404, "DATA_STRUCT_NOT_FOUND", $"未找到数据结构：{structName}");
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

        private JObject HandleSetDataStructField(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessEdit, "修改数据结构字段");
            EnsureRuntimeReady();
            DataStructStore store = SF.dataStructStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "数据结构存储未初始化。");
            }
            string name = ReadRequiredString(request, "name");
            int itemIndex = ReadRequiredInt(request, "itemIndex");
            int fieldIndex = ReadRequiredInt(request, "fieldIndex");
            string value = request["value"]?.Value<string>();
            if (value == null)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "缺少 value 字段。");
            }
            if (!store.TryGetStructIndexByName(name, out int structIndex))
            {
                throw new BridgeRequestException(404, "DATA_STRUCT_NOT_FOUND", $"未找到数据结构：{name}");
            }
            // 先读取现有字段类型
            if (!store.TryGetStructSnapshotByName(name, out DataStruct ds))
            {
                throw new BridgeRequestException(500, "DATA_STRUCT_ERROR", $"读取数据结构失败：{name}");
            }
            if (itemIndex < 0 || itemIndex >= ds.dataStructItems.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"itemIndex 超出范围 [0, {ds.dataStructItems.Count})。");
            }
            DataStructItem itemSnap = ds.dataStructItems[itemIndex];
            if (itemSnap == null || itemSnap.FieldTypes == null || !itemSnap.FieldTypes.TryGetValue(fieldIndex, out DataStructValueType fieldType))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"fieldIndex {fieldIndex} 不存在。");
            }
            string fieldTypeStr = fieldType == DataStructValueType.Number ? "Number" : "Text";
            if (fieldType == DataStructValueType.Number)
            {
                if (!double.TryParse(value, out _))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段是 Number 类型，value 不是有效数字：{value}");
                }
            }
            if (!store.SetFieldValue(structIndex, itemIndex, fieldIndex, fieldType, value, out string error))
            {
                throw new BridgeRequestException(400, "SET_FIELD_FAILED", $"修改字段失败：{error}");
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

        // ===================== IO 操作 =====================

        private JObject HandleListIo(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询 IO 列表");
            EnsureRuntimeReady();
            var ioMap = SF.frmIO?.DicIO;
            if (ioMap == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
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

        private JObject HandleGetIo(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询 IO");
            EnsureRuntimeReady();
            var ioMap = SF.frmIO?.DicIO;
            if (ioMap == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
            }
            string name = ReadRequiredString(request, "name");
            if (!ioMap.TryGetValue(name, out IO io) || io == null)
            {
                throw new BridgeRequestException(404, "IO_NOT_FOUND", $"未找到 IO：{name}");
            }
            return BuildIoJObject(io);
        }

        private JObject HandleSearchIo(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "搜索 IO");
            EnsureRuntimeReady();
            var ioMap = SF.frmIO?.DicIO;
            if (ioMap == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
            }
            string keyword = request["keyword"]?.Value<string>() ?? string.Empty;
            string typeFilter = request["type"]?.Value<string>();
            int? cardNum = request["cardNum"]?.Value<int>();
            int limit = request["limit"]?.Value<int>() ?? 100;
            if (limit <= 0) limit = 100;

            var items = new List<JObject>();
            foreach (var kv in ioMap)
            {
                IO io = kv.Value;
                if (io == null) continue;
                if (!string.IsNullOrEmpty(keyword)
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
                ["type"] = typeFilter ?? string.Empty,
                ["cardNum"] = cardNum.HasValue ? JToken.FromObject(cardNum.Value) : null,
                ["returned"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        private JObject HandleGetIoState(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询 IO 实时状态");
            EnsureRuntimeReady();
            var ioMap = SF.frmIO?.DicIO;
            if (ioMap == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "IO 存储未初始化。");
            }
            string name = ReadRequiredString(request, "name");
            if (!ioMap.TryGetValue(name, out IO io) || io == null)
            {
                throw new BridgeRequestException(404, "IO_NOT_FOUND", $"未找到 IO：{name}");
            }
            bool? state = null;
            string error = null;
            try
            {
                bool bval = false;
                bool ok;
                if (string.Equals(io.IOType, "通用输出", StringComparison.OrdinalIgnoreCase))
                {
                    ok = SF.motion?.GetOutIO(io, ref bval) ?? false;
                }
                else
                {
                    ok = SF.motion?.GetInIO(io, ref bval) ?? false;
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

        private JObject HandleListAlarms(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询报警清单");
            EnsureRuntimeReady();
            AlarmInfoStore store = SF.alarmInfoStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "报警存储未初始化。");
            }
            bool includeEmpty = request["includeEmpty"]?.Value<bool>() ?? false;
            string categoryLike = request["categoryLike"]?.Value<string>();
            string nameLike = request["nameLike"]?.Value<string>();

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
            return new JObject
            {
                ["total"] = items.Count,
                ["items"] = new JArray(items)
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

        // ===================== PLC 设备清单 =====================

        private JObject HandleListPlcDevices(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询 PLC 设备清单");
            EnsureRuntimeReady();
            PlcConfigStore store = SF.plcStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "PLC 存储未初始化。");
            }
            bool includeMaps = request["includeMaps"]?.Value<bool>() ?? false;
            var devices = store.Devices;
            var items = new List<JObject>();
            foreach (PlcDevice dev in devices)
            {
                if (dev == null) continue;
                JObject obj = new JObject
                {
                    ["name"] = dev.Name ?? string.Empty,
                    ["protocol"] = dev.Protocol ?? string.Empty,
                    ["cpuType"] = dev.CpuType ?? string.Empty,
                    ["ip"] = dev.Ip ?? string.Empty,
                    ["port"] = dev.Port,
                    ["rack"] = dev.Rack,
                    ["slot"] = dev.Slot,
                    ["timeoutMs"] = dev.TimeoutMs,
                    ["unitId"] = dev.UnitId
                };
                if (includeMaps)
                {
                    var maps = store.Maps;
                    var deviceMaps = new JArray();
                    foreach (PlcMapItem map in maps)
                    {
                        if (map == null) continue;
                        if (!string.Equals(map.PlcName, dev.Name, StringComparison.OrdinalIgnoreCase)) continue;
                        deviceMaps.Add(new JObject
                        {
                            ["plcName"] = map.PlcName ?? string.Empty,
                            ["dataType"] = map.DataType ?? string.Empty,
                            ["direction"] = map.Direction ?? string.Empty,
                            ["plcAddress"] = map.PlcAddress ?? string.Empty,
                            ["valueName"] = map.ValueName ?? string.Empty,
                            ["quantity"] = map.Quantity,
                            ["writeConst"] = map.WriteConst ?? string.Empty
                        });
                    }
                    obj["maps"] = deviceMaps;
                }
                items.Add(obj);
            }
            return new JObject
            {
                ["total"] = items.Count,
                ["items"] = new JArray(items)
            };
        }

        // ===================== 控制卡/轴清单 =====================

        private JObject HandleListCards(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询控制卡/轴清单");
            EnsureRuntimeReady();
            CardConfigStore store = SF.cardStore;
            if (store == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "控制卡存储未初始化。");
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
                            ["homeType"] = axis.HomeType ?? string.Empty,
                            ["homeSpeed"] = axis.HomeSpeed ?? string.Empty,
                            ["limitSpeed"] = axis.LimitSpeed ?? string.Empty,
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

        private JObject HandleListTrayPoints(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询托盘点位清单");
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

        private JObject HandleListCommunications(JObject request)
        {
            EnsureBridgePermission(PermissionKeys.ProcessAccess, "查询通讯清单");
            EnsureRuntimeReady();
            bool includeStatus = request["includeStatus"]?.Value<bool>() ?? true;
            var socketInfos = SF.frmComunication?.socketInfos ?? new List<SocketInfo>();
            var serialPortInfos = SF.frmComunication?.serialPortInfos ?? new List<SerialPortInfo>();
            var tcpItems = new JArray();
            foreach (SocketInfo sock in socketInfos)
            {
                if (sock == null) continue;
                JObject obj = new JObject
                {
                    ["name"] = sock.Name ?? string.Empty,
                    ["type"] = sock.Type ?? string.Empty,
                    ["port"] = sock.Port,
                    ["address"] = sock.Address ?? string.Empty,
                    ["isServer"] = sock.isServering,
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
                }
                tcpItems.Add(obj);
            }
            var serialItems = new JArray();
            foreach (SerialPortInfo sp in serialPortInfos)
            {
                if (sp == null) continue;
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
                }
                serialItems.Add(obj);
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

        private void CommitPatch(int procIndex, Proc draft, List<(int stepIndex, int opIndex, ProcChangeKind kind)> affectedOps = null)
        {
            if (draft == null)
            {
                throw new BridgeRequestException(500, "PATCH_EMPTY", "Patch 结果为空。");
            }

            // AI 改动流程前，若流程未停止（运行/暂停/单步），先停止再提交。
            // 否则引擎 agent 线程仍持有旧的步骤/指令索引，热更新在 WaitForStep 阻塞期间无法生效，
            // 导致后续单步/继续操作状态不一致（卡在单步、无法关闭等）。
            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
            if (snapshot != null && snapshot.State != ProcRunState.Stopped)
            {
                SF.DR.Stop(procIndex);
                SF.DR.ClearPendingProcUpdates();
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
            NotifyProcChanged(procIndex, ProcChangeKind.Modified, affectedOps);
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

            // 记录新增指令的位置，供 FrmDataGrid 行级闪烁使用。
            int stepIndex = FindStepIndexById(draft, step.Id);
            if (stepIndex >= 0)
            {
                result.AffectedOps.Add((stepIndex, insertIndex, ProcChangeKind.Added));
            }
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

            // 完全权限模式：直接标记预演为已确认，避免 FrmAiAssistant 通过 HTTP 回调确认导致 UI 线程死锁。
            bool autoConfirmed = SF.frmAiAssistant?.IsFullPermissionMode == true;
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
                            bool isJump = IsJumpOperation(op?.OperaType);
                            string flow = isJump
                                ? "条件跳转（不自动流向下一条）"
                                : (opIndex < step.Ops.Count - 1 ? $"执行后自动流向[{opIndex + 1}]" : "执行后步骤完成");
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

            // 跳转目标有效性检查：删除/插入指令后 opIndex 会变化，旧跳转目标可能越界。
            // 将无效跳转目标列为 warnings，让 AI 在读取流程详情时直接发现，不必额外调用 diagnose_proc。
            JArray gotoWarnings = new JArray();
            foreach (string error in FrmProc.ValidateProcGotoTargets(procIndex, proc))
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

        // 跳转类指令：执行后按条件跳转，不会自动流向 opIndex+1。
        // 非跳转类指令：执行后自动流向 opIndex+1（默认顺序执行规则）。
        // 此分类用于在 get_proc_detail 返回中标注每条指令的 flow 字段，帮助 AI 理解执行流。
        private static readonly HashSet<string> jumpOperaTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IO逻辑跳转", "逻辑判断", "跳转"
        };

        private static bool IsJumpOperation(string operaType)
        {
            return !string.IsNullOrWhiteSpace(operaType) && jumpOperaTypes.Contains(operaType);
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

            // 记录每个被改动指令的 (stepIndex, opIndex, kind)，供 FrmDataGrid 行级闪烁使用。
            // 为空时降级为整体闪烁。
            public List<(int stepIndex, int opIndex, ProcChangeKind kind)> AffectedOps { get; set; } = new List<(int, int, ProcChangeKind)>();
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
