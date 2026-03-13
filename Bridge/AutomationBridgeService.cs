using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using static System.ComponentModel.TypeConverter;

namespace Automation.Bridge
{
    internal sealed class AutomationBridgeService
    {
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
                    case "/bridge/patch/preview":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("patch.preview", ExecuteOnUiThread(() => HandlePreviewPatch(request))));
                    case "/bridge/patch/apply":
                        return AutomationBridgeResponse.Ok(BuildSuccessBody("patch.apply", ExecuteOnUiThread(() => HandleApplyPatch(request))));
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
            int procIndex = ReadRequiredInt(request, "procIndex");
            Proc proc = GetProcByIndex(procIndex);
            return BuildProcOverview(procIndex, proc);
        }

        private JObject HandleGetProcDetail(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            Proc proc = GetProcByIndex(procIndex);
            return BuildProcDetail(procIndex, proc);
        }

        private JObject HandleListOperationTypes()
        {
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

        private JObject HandlePreviewPatch(JObject request)
        {
            PatchExecutionResult result = ExecutePatch(request);
            return BuildPatchResult("preview", result);
        }

        private JObject HandleApplyPatch(JObject request)
        {
            PatchExecutionResult result = ExecutePatch(request);
            CommitPatch(result.ProcIndex, result.Proc);

            Proc current = GetProcByIndex(result.ProcIndex);
            return BuildPatchResult("apply", new PatchExecutionResult
            {
                ProcIndex = result.ProcIndex,
                Proc = current,
                Messages = result.Messages,
                Changes = result.Changes
            });
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

                string actionType = ReadRequiredString(action, "type");
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
                    case "append_operation":
                        ApplyAppendOperation(action, draft, result, i);
                        break;
                    default:
                        throw new BridgeRequestException(400, "PATCH_UNSUPPORTED_ACTION", $"不支持的 Patch 动作：{actionType}");
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
            JObject fieldChanges = ReadRequiredObject(action, "fieldChanges");
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
            JObject fieldChanges = ReadRequiredObject(action, "fieldChanges");
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
            string expectedOperaType = ReadOptionalString(action, "expectedOperaType");
            if (!string.IsNullOrWhiteSpace(expectedOperaType)
                && !string.Equals(expectedOperaType, op.OperaType, StringComparison.Ordinal))
            {
                throw new BridgeRequestException(409, "PATCH_TARGET_MISMATCH", $"指令类型不匹配，期望 {expectedOperaType}，实际 {op.OperaType}。");
            }

            JObject fieldChanges = ReadRequiredObject(action, "fieldChanges");
            ApplyOperationPropertyChanges(op, fieldChanges, $"指令[{op.Name}]", actionIndex, result);
        }

        private void ApplyAppendStep(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            if (draft.steps == null)
            {
                draft.steps = new List<Step>();
            }

            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = ReadOptionalString(action, "name") ?? $"步骤{draft.steps.Count}",
                Disable = ReadOptionalBoolean(action, "disable") ?? false,
                Ops = new List<OperationType>()
            };

            draft.steps.Add(step);
            result.Messages.Add($"动作{actionIndex}：已追加步骤 {step.Name}。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = actionIndex,
                ["type"] = "append_step",
                ["stepId"] = step.Id.ToString("D"),
                ["name"] = step.Name
            });
        }

        private void ApplyAppendOperation(JObject action, Proc draft, PatchExecutionResult result, int actionIndex)
        {
            Step step = FindStepById(draft, ParseGuid(ReadRequiredString(action, "stepId"), "stepId"));
            string operaType = ReadRequiredString(action, "operaType");
            OperationType op = CreateOperationTemplate(operaType);
            op.Id = Guid.NewGuid();

            JObject fieldValues = ReadOptionalObject(action, "fieldValues");
            if (fieldValues != null && fieldValues.Properties().Any())
            {
                ApplyOperationPropertyChanges(op, fieldValues, $"新增指令[{operaType}]", actionIndex, result);
            }

            if (step.Ops == null)
            {
                step.Ops = new List<OperationType>();
            }

            step.Ops.Add(op);
            result.Messages.Add($"动作{actionIndex}：已在步骤[{step.Name}] 末尾追加指令 {op.Name}({op.OperaType})。");
            result.Changes.Add(new JObject
            {
                ["actionIndex"] = actionIndex,
                ["type"] = "append_operation",
                ["stepId"] = step.Id.ToString("D"),
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
            WithOperationContext(op, () =>
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
            return WithOperationContext(op, () =>
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
            return WithOperationContext(op, () =>
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
            return WithOperationContext(op, () =>
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

        private static JObject ReadRequiredObject(JObject request, string fieldName)
        {
            JObject obj = ReadOptionalObject(request, fieldName);
            if (obj == null)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是对象。");
            }
            return obj;
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

        private static JArray ReadRequiredArray(JObject request, string fieldName)
        {
            if (!request.TryGetValue(fieldName, out JToken token) || !(token is JArray array))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"字段 {fieldName} 必须是数组。");
            }
            return array;
        }

        private static void WithOperationContext(OperationType op, Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            WithOperationContext<object>(op, () =>
            {
                action();
                return null;
            });
        }

        private static T WithOperationContext<T>(OperationType op, Func<T> action)
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
                SF.isModify = ModifyKind.Operation;
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

        private sealed class CommReferenceCatalog
        {
            public List<string> All { get; set; } = new List<string>();

            public List<string> Tcp { get; set; } = new List<string>();

            public List<string> Serial { get; set; } = new List<string>();
        }
    }
}
