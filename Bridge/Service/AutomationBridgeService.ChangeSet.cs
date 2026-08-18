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
                || ex is ArgumentException)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                throw new BridgeRequestException(
                    400, "OPERA_TYPE_NOT_FOUND", ex.Message, BuildOperaTypeNotFoundDetails(operaType));
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
                return BuildNativeOperationContractsWithRoute(distinct);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                || ex is ArgumentException)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                // 找出未注册的类型并附相近候选，模型一轮即可纠正而不是逐个猜名。
                string missing = distinct.FirstOrDefault(type =>
                {
                    try { OperationDefinitionRegistry.Create(type); return false; }
                    catch (KeyNotFoundException) { return true; }
                }) ?? operaTypes.Values<string>().First();
                throw new BridgeRequestException(
                    400, "OPERA_TYPE_NOT_FOUND", ex.Message, BuildOperaTypeNotFoundDetails(missing));
            }
        }

        private static JObject BuildNativeOperationContractsWithRoute(IEnumerable<string> operaTypes)
        {
            JObject compactContracts = StructuredOperationCompiler.BuildCompactContracts(operaTypes);
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
            {
                EnsureOnlyProperties(value, "changeSet.actions[].process",
                    "key", "name", "autoStart", "disable", "parameters");
                ValidateObjectArray(value["parameters"], "changeSet.actions[].process.parameters", parameter =>
                    EnsureOnlyProperties(parameter, "changeSet.actions[].process.parameters[]",
                        "name", "direction", "type", "variableName", "required", "defaultValue"));
            });
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
                AiResourceBindingException bindingError = ex as AiResourceBindingException;
                bool candidatesCarryRefs = bindingError?.Candidates.Any(candidate =>
                    !string.IsNullOrEmpty(candidate.ResourceRef)) == true;
                JArray issues = bindingError != null
                    ? new JArray(new JObject
                    {
                        ["path"] = bindingError.Path,
                        ["rule"] = "resource_binding",
                        ["message"] = bindingError.Message,
                        ["suggestedRepair"] = bindingError.Candidates.Count > 0
                            ? (candidatesCarryRefs
                                ? "直接采用 recovery.bindingRepair.candidates 中同类型资源的 resourceRef，不改写展示名称。"
                                : "直接采用 recovery.bindingRepair.candidates 中的精确名称重试；该引用按名称消费。")
                            : string.Equals(bindingError.RequiredResourceType, "variable", StringComparison.Ordinal)
                                ? "确认是新建变量时在同一 changeSet.variables 中声明资源策略；否则重新读取变量目录。"
                                : "重新读取对应资源类别；没有合法对象时保留占位或询问用户。"
                    })
                    : ex is AiChangeSetValidationException validation
                    ? new JArray(validation.Issues.Select(issue => new JObject
                    {
                        ["path"] = issue.Path ?? "$.changeSet",
                        ["rule"] = issue.Rule ?? "change_set_compile",
                        ["message"] = issue.Message ?? ex.Message,
                        ["suggestedRepair"] = issue.SuggestedRepair
                            ?? "保持业务目标不变，按当前路径修正后重试同一功能块。"
                    }))
                    : new JArray(new JObject
                    {
                        ["path"] = "$.changeSet",
                        ["rule"] = "change_set_compile",
                        ["message"] = ex.Message,
                        ["suggestedRepair"] = "保持业务目标不变，按编译错误修正全部相关字段后重试同一功能块。"
                    });
                JObject repairContracts = bindingError == null
                    ? BuildChangeSetRepairContracts(changeSet, issues)
                    : new JObject();
                var recovery = new JObject
                {
                    ["validationError"] = ex.Message,
                    ["issues"] = issues,
                    ["reason"] = "fix_validation_error",
                    ["retryableWhen"] = "change_set_passes_validation",
                    ["sideEffects"] = "none",
                    ["safeToRetry"] = true,
                    ["retryScope"] = "same_function_block"
                };
                if (bindingError != null)
                    recovery["bindingRepair"] = BuildResourceBindingRepair(bindingError);
                if (repairContracts.HasValues)
                    recovery["repairContracts"] = repairContracts;
                throw new BridgeRequestException(400, "CHANGE_SET_COMPILE_FAILED",
                    "语义变更集编译失败。", recovery.ToString(Formatting.None));
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
                ["status"] = record.Confirmed ? "confirmed" : "awaiting_confirmation",
                ["nextStep"] = record.Confirmed
                    ? "该预演事务已确认（含前台自动批准）；必须在同一请求内用 apply_change_set(previewId) 提交，不得以存在业务假设为由跳过已确认的提交。机构角色、极性等假设在提交后的答复中声明，并可用提交返回的 authoringLease 或稳定 ID 修正。"
                    : "等待前台确认结果；确认后仅用同一 previewId 提交，不修改已预演内容。",
                ["allowedTransitions"] = allowedTransitions,
                ["expiresAt"] = record.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
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

        private static JObject BuildResourceBindingRepair(AiResourceBindingException error)
        {
            return new JObject
            {
                ["path"] = error?.Path ?? string.Empty,
                ["requested"] = error?.RequestedValue ?? string.Empty,
                ["requiredResourceType"] = error?.RequiredResourceType ?? string.Empty,
                ["reason"] = error?.Reason ?? string.Empty,
                ["actualResourceType"] = error?.ActualResourceType ?? string.Empty,
                ["bindingValidation"] = "failed",
                ["candidates"] = new JArray((error?.Candidates
                    ?? Array.Empty<AiResourceBindingCandidate>()).Select(candidate => new JObject
                    {
                        ["resourceRef"] = candidate.ResourceRef ?? string.Empty,
                        ["name"] = candidate.Name ?? string.Empty,
                        ["resourceType"] = candidate.ResourceType ?? string.Empty,
                        ["replacement"] = new JObject
                        {
                            ["field"] = error?.Path ?? string.Empty,
                            ["value"] = candidate.ResourceRef ?? candidate.Name ?? string.Empty
                        }
                    }))
            };
        }

        private static JObject BuildChangeSetRepairContracts(AiChangeSet changeSet, JArray issues)
        {
            IReadOnlyList<ChangeSetAction> actions = changeSet?.Actions
                ?? new List<ChangeSetAction>();
            string[] allSemanticKinds = actions
                .Select(action => action?.Operation?.Kind?.Trim())
                .Where(kind => !string.IsNullOrWhiteSpace(kind)
                    && AiOperationCompilerRegistry.Kinds.Contains(kind, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            string[] allNativeTypes = actions
                .Where(action => string.Equals(
                    action?.Operation?.Kind, "native.operation", StringComparison.Ordinal))
                .Select(action => action?.Operation?.OperaType?.Trim())
                .Where(operaType => !string.IsNullOrWhiteSpace(operaType))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var referencedSemanticKinds = new HashSet<string>(StringComparer.Ordinal);
            var referencedNativeTypes = new HashSet<string>(StringComparer.Ordinal);

            foreach (JObject issue in (issues ?? new JArray()).OfType<JObject>())
            {
                SemanticOperation operation = null;
                if (TryReadActionIndex(issue["path"]?.Value<string>(), out int actionIndex)
                    && actionIndex >= 0 && actionIndex < actions.Count)
                {
                    operation = actions[actionIndex]?.Operation;
                }
                if (operation == null)
                {
                    string message = issue["message"]?.Value<string>() ?? string.Empty;
                    string inferredKind = allSemanticKinds.FirstOrDefault(kind =>
                        message.IndexOf(kind + ".", StringComparison.Ordinal) >= 0);
                    if (!string.IsNullOrWhiteSpace(inferredKind))
                        operation = actions.Select(action => action?.Operation).FirstOrDefault(candidate =>
                            string.Equals(candidate?.Kind, inferredKind, StringComparison.Ordinal));
                    if (operation == null)
                    {
                        string inferredType = allNativeTypes.FirstOrDefault(operaType =>
                            message.IndexOf(operaType, StringComparison.Ordinal) >= 0);
                        if (!string.IsNullOrWhiteSpace(inferredType))
                            operation = actions.Select(action => action?.Operation).FirstOrDefault(candidate =>
                                string.Equals(candidate?.OperaType, inferredType, StringComparison.Ordinal));
                    }
                }
                if (string.IsNullOrWhiteSpace(operation?.Kind)) continue;
                if (string.Equals(operation.Kind, "native.operation", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(operation.OperaType))
                {
                    referencedNativeTypes.Add(operation.OperaType.Trim());
                    issue["contractRef"] = "native." + operation.OperaType.Trim();
                }
                else
                {
                    referencedSemanticKinds.Add(operation.Kind.Trim());
                    issue["contractRef"] = "semantic." + operation.Kind.Trim();
                }
            }

            var contracts = new JObject();
            if (referencedSemanticKinds.Count > 0)
                contracts["semantic"] = AiOperationCompilerRegistry.BuildContracts(
                    referencedSemanticKinds.ToArray());
            if (referencedNativeTypes.Count > 0)
            {
                try
                {
                    contracts["native"] = BuildNativeOperationContractsWithRoute(
                        referencedNativeTypes.ToArray());
                }
                catch (Exception contractError) when (contractError is InvalidOperationException
                    || contractError is ArgumentException || contractError is KeyNotFoundException)
                {
                    contracts["nativeContractError"] = contractError.Message;
                }
            }
            return contracts;
        }

        private static bool TryReadActionIndex(string path, out int actionIndex)
        {
            actionIndex = -1;
            if (string.IsNullOrWhiteSpace(path)) return false;
            const string marker = "actions[";
            int start = path.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return false;
            start += marker.Length;
            int end = path.IndexOf(']', start);
            return end > start
                && int.TryParse(path.Substring(start, end - start),
                    NumberStyles.None, CultureInfo.InvariantCulture, out actionIndex);
        }

        private static JArray BuildChangeSetAllowedTransitions(
            PreviewApprovalRecord record,
            bool includeReplacement = true,
            bool includeDiscard = true)
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
            if (includeReplacement)
            {
                allowedTransitions.Add(new JObject
                {
                    ["tool"] = "preview_change_set",
                    ["requiredArguments"] = new JArray("changeSet"),
                    ["fixedArguments"] = new JObject { ["replacePreviewId"] = record.PreviewId },
                    ["changeSetMode"] = "complete_replacement"
                });
            }
            if (includeDiscard)
            {
                allowedTransitions.Add(new JObject
                {
                    ["tool"] = "discard_change_set_preview",
                    ["arguments"] = new JObject { ["previewId"] = record.PreviewId }
                });
            }
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
                            Name = item.Key,
                            ResourceRef = AuthoringResourceRefs.ForIo(
                                item.Value.IOType,
                                item.Value.CardNum,
                                item.Value.Module,
                                item.Value.IOIndex),
                            IoType = item.Value.IOType ?? string.Empty,
                            CardNum = item.Value.CardNum,
                            Module = item.Value.Module,
                            IoIndex = item.Value.IOIndex ?? string.Empty
                        };
                        string resourceRef = AuthoringResourceRefs.ForIo(
                            item.Value.IOType,
                            item.Value.CardNum,
                            item.Value.Module,
                            item.Value.IOIndex);
                        if (!string.IsNullOrWhiteSpace(resourceRef))
                            ioResources[resourceRef] = ioResources[item.Key];
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
            // 工站/点位/数据结构/流程/自定义函数引用保持晚绑定：允许缺少运行资源保存为 incomplete，
            // 由 ProcessReadinessService 在预演 runBlockers 与启动闸门拦截并附相近候选。
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
                        ["runnable"] = analysis["runnable"]?.Value<bool>() ?? true
                    };
                    affectedProcesses.Add(item);
                }
            }
            return new JObject
            {
                ["previewId"] = previewId,
                ["configurationSaved"] = true,
                ["status"] = "committed",
                ["summary"] = new JObject
                {
                    ["affectedProcesses"] = affectedProcesses.Count,
                    ["changedVariables"] = draft.ChangedVariableCount
                },
                ["variableResolutions"] = draft.VariableResolutions?.DeepClone() ?? new JArray(),
                ["affectedProcesses"] = affectedProcesses,
                ["createdObjects"] = draft.CreatedObjects?.DeepClone() ?? new JObject
                {
                    ["processes"] = new JArray(),
                    ["steps"] = new JArray(),
                    ["operations"] = new JArray()
                },
                ["readinessStatus"] = draft.ReadinessStatus,
                ["runnable"] = draft.Runnable,
                ["warnings"] = draft.ConfigurationWarnings?.DeepClone() ?? new JArray(),
                ["runBlockers"] = draft.RunBlockers?.DeepClone() ?? new JArray(),
                ["message"] = "语义变更集已按冻结预演原子提交。"
            };
        }

        private void CommitChangeSet(AiChangeSetCompileResult draft)
        {
            EnsureRuntimeReady();
            EnsureAllProcsInactiveForAiStructureCommit("提交语义变更集");
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
            if (runtime.Editor.ActiveSession != null)
            {
                throw new BridgeRequestException(
                    409,
                    "EDITOR_SESSION_ACTIVE",
                    $"当前存在未完成的编辑会话：{runtime.Editor.ActiveSession.Name}。"
                    + "请先保存或取消，再提交 AI 变更集。",
                    new JObject
                    {
                        ["reason"] = "editor_session_active",
                        ["retryableWhen"] = "editor_session_saved_or_canceled",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
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
