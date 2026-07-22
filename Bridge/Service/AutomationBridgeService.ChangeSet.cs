using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。
// 状态机：preview 冻结编译结果与基础哈希，前台只确认，apply 仅凭 previewId 校验后事务提交。

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
        private static AiChangeSet ParseChangeSet(JObject request)
        {
            JObject token = ReadRequiredObject(request, "changeSet");
            ValidateChangeSetShape(token);
            try
            {
                AiChangeSet changeSet = JsonConvert.DeserializeObject<AiChangeSet>(token.ToString(Formatting.None),
                    new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Error,
                        NullValueHandling = NullValueHandling.Include
                    }) ?? throw new JsonSerializationException("changeSet 反序列化结果为空。");
                string variableValidationError = VariableChangeContract.Validate(changeSet.Variables);
                if (variableValidationError != null)
                {
                    throw new BridgeRequestException(
                        400, "CHANGE_SET_INVALID", variableValidationError);
                }
                return changeSet;
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
                "version", "title", "actions", "variables");
            ValidateObjectArray(changeSet["actions"], "changeSet.actions", ValidateAtomicActionShape);
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
            Dictionary<string, DicValue> variables = runtime.Stores.Values?.BuildSaveData()
                ?? throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            AiChangeSetCompileResult draft;
            try
            {
                draft = AiChangeSetCompiler.Compile(
                    runtime, changeSet, runtime.Stores.Processes.Items, variables, BuildAiResourceSnapshot());
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
                // 预演冻结编译结果和基线哈希；apply 只接受 previewId，不会重新解释模型原始输入。
                record.AiChangeSetPreview = draft;
                record.BaseStateHash = AiChangeSetCompiler.ComputeStateHash(runtime.Stores.Processes.Items, variables);
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
                    && item.ExpiresAtUtc > previewUtcNow());
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

        private AiResourceSnapshot BuildAiResourceSnapshot()
        {
            var ioResources = new Dictionary<string, AiIoResource>(StringComparer.Ordinal);
            if (runtime.Stores.IoConfiguration?.ByName != null)
            {
                foreach (KeyValuePair<string, IO> item in runtime.Stores.IoConfiguration.ByName)
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
            string[] tcpNames = (runtime.Stores.Communication?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
            string[] serialNames = (runtime.Stores.Communication?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
            string[] alarmInfoIds = (runtime.Stores.Alarms?.Alarms ?? new System.ComponentModel.BindingList<AlarmInfo>())
                .Where(item => item != null
                    && !string.IsNullOrWhiteSpace(item.Name)
                    && !string.IsNullOrWhiteSpace(item.Note))
                .Select(item => item.Index.ToString(CultureInfo.InvariantCulture))
                .ToArray();
            string[] plcNames = (runtime.Stores.Plc?.GetSnapshot().Devices ?? new List<PlcDeviceConfig>())
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

            Dictionary<string, DicValue> currentVariables = runtime.Stores.Values?.BuildSaveData()
                ?? throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "变量存储未初始化。");
            // 前台确认不锁住编辑器；因此提交前必须重新比较基线，避免覆盖确认后发生的人工修改。
            string currentStateHash = AiChangeSetCompiler.ComputeStateHash(runtime.Stores.Processes.Items, currentVariables);
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

            // 提交的是预演时冻结的结果。成功后立即关闭局部 key 作用域，后续编辑改用返回的稳定 ID。
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

        private void CommitChangeSet(AiChangeSetCompileResult draft)
        {
            EnsureRuntimeReady();
            EnsureAllProcsStoppedForAiStructureCommit("提交语义变更集");
            if (runtime.Maintenance.Active)
            {
                throw new BridgeRequestException(423, "CONFIG_MAINTENANCE_ACTIVE",
                    string.IsNullOrWhiteSpace(runtime.Maintenance.Reason)
                        ? "系统正在执行配置维护。"
                        : $"系统正在执行配置维护：{runtime.Maintenance.Reason}");
            }
            if (runtime.Safety.IsLocked)
            {
                throw new BridgeRequestException(423, "SECURITY_LOCKED", $"系统已安全锁定：{runtime.Safety.LockReason}");
            }

            Dictionary<string, DicValue> oldVariables = runtime.Stores.Values.BuildSaveData();
            Dictionary<string, DicValue> commitVariables = draft.Variables
                .ToDictionary(item => item.Key, item => ObjectGraphCloner.Clone(item.Value), StringComparer.Ordinal);
            var currentById = oldVariables.Values
                .Where(value => value != null && value.Id != Guid.Empty)
                .ToDictionary(value => value.Id);
            ISet<Guid> explicitValueIds = new HashSet<Guid>(
                draft.VariableValueOverrides?.Keys ?? Enumerable.Empty<Guid>());
            foreach (DicValue variable in commitVariables.Values)
            {
                // 配置变更默认保留当前运行值；只有本次 ChangeSet 明确赋值的变量才覆盖 Value。
                if (variable != null && !explicitValueIds.Contains(variable.Id)
                    && currentById.TryGetValue(variable.Id, out DicValue current))
                {
                    variable.Value = current.Value;
                }
            }
            ProcessVariableConfigurationCommitResult commitResult =
                runtime.ProcessVariableConfiguration.CommitChangeSet(
                    draft.Processes,
                    commitVariables,
                    draft.VariableValueOverrides);
            if (!commitResult.Succeeded)
            {
                if (commitResult.PostCommitFailure && !commitResult.RollbackIncomplete)
                {
                    throw new BridgeRequestException(
                        500,
                        "CHANGE_SET_COMMIT_FAILED",
                        "语义变更集提交失败，流程与变量配置已恢复。",
                        commitResult.Detail);
                }
                throw new BridgeRequestException(
                    commitResult.RollbackIncomplete ? 500 : 409,
                    commitResult.RollbackIncomplete
                        ? "CHANGE_SET_ROLLBACK_FAILED"
                        : "CHANGE_SET_COMMIT_FAILED",
                    commitResult.Message,
                    new JObject
                    {
                        ["reason"] = commitResult.RollbackIncomplete
                            ? "configuration_transaction_rollback_failed"
                            : "configuration_transaction_commit_failed",
                        ["retryableWhen"] = commitResult.RollbackIncomplete
                            ? "security_lock_cleared_after_configuration_recovery"
                            : "server_configuration_transaction_fixed",
                        ["sideEffects"] = commitResult.RollbackIncomplete ? "unknown" : "none"
                    }.ToString(Formatting.None));
            }
        }

    }
}
