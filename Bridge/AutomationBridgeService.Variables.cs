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

    }
}
