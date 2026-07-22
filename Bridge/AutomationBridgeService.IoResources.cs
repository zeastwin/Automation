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

    }
}
