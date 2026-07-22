using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。

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

    }
}
