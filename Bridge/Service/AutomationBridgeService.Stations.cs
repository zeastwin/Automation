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
        private DataStation ResolveStation(int stationIndex)
        {
            if (runtime.Stores.Stations?.Items == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "工站存储未初始化。");
            }
            List<DataStation> list = runtime.Stores.Stations.Items;
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
            if (index < 0 || index >= DataStation.PointCapacity)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"点位 index 超出范围 [0, {DataStation.PointCapacity})。");
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
        private AlarmInfo ResolveAlarm(int index)
        {
            AlarmInfoStore store = runtime.Stores.Alarms;
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
                ["teachingState"] = pos.TeachingState,
                ["taught"] = pos.IsMotionReady,
                ["x"] = pos.X,
                ["y"] = pos.Y,
                ["z"] = pos.Z,
                ["u"] = pos.U,
                ["v"] = pos.V,
                ["w"] = pos.W
            };
        }

        private static IEnumerable<DataPos> EnumerateNamedPoints(DataStation station)
        {
            return (station?.ListDataPos ?? new List<DataPos>())
                .Where(point => point != null && !string.IsNullOrWhiteSpace(point.Name))
                .OrderBy(point => point.Index);
        }

        private static void RebuildStationPointDictionary(DataStation station)
        {
            if (station == null) return;
            station.dicDataPos = EnumerateNamedPoints(station)
                .GroupBy(point => point.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        private void SaveStationAndRefresh()
        {
            StationDefinitionStore store = runtime.Stores.Stations;
            List<DataStation> candidate = store.Items.Select(ObjectGraphCloner.Clone).ToList();
            foreach (DataStation station in candidate) RebuildStationPointDictionary(station);
            if (!store.TryCommit(runtime.Paths.ConfigPath, candidate, out string error))
            {
                if (!store.Load(runtime.Paths.ConfigPath, out string restoreError))
                {
                    runtime.Safety.Lock($"{error}；正式内存恢复失败：{restoreError}");
                }
                throw new BridgeRequestException(500, "STATION_COMMIT_FAILED", error);
            }
            runtime.EditorUi?.RefreshStations();
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleListStations(JObject request)
        {
            EnsureRuntimeReady();
            if (runtime.Stores.Stations?.Items == null)
            {
                return BridgeError(500, "STORE_UNAVAILABLE", "工站存储未初始化。");
            }
            JArray array = new JArray();
            List<DataStation> list = runtime.Stores.Stations.Items;
            for (int i = 0; i < list.Count; i++)
            {
                DataStation station = list[i];
                if (station == null) continue;
                int namedCount = 0;
                int taughtCount = 0;
                int plannedCount = 0;
                foreach (DataPos point in EnumerateNamedPoints(station))
                {
                    namedCount++;
                    if (point.IsMotionReady) taughtCount++;
                    else plannedCount++;
                }
                JArray axes = BuildStationAxes(station);
                array.Add(new JObject
                {
                    ["stationIndex"] = i,
                    ["name"] = station.Name ?? string.Empty,
                    ["coordinateSystem"] = station.CoordinateSystem,
                    ["manualSpeedPercent"] = station.ManualSpeedPercent,
                    ["axisCount"] = axes.Count,
                    ["axes"] = axes,
                    ["pointCount"] = namedCount,
                    ["taughtPointCount"] = taughtCount,
                    ["plannedPointCount"] = plannedCount
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
            foreach (DataPos point in EnumerateNamedPoints(station))
            {
                points.Add(BuildPointJObject(point));
            }
            JArray axes = BuildStationAxes(station);
            return new JObject
            {
                ["stationIndex"] = stationIndex,
                ["name"] = station.Name ?? string.Empty,
                ["coordinateSystem"] = station.CoordinateSystem,
                ["manualSpeedPercent"] = station.ManualSpeedPercent,
                ["axisCount"] = axes.Count,
                ["axes"] = axes,
                ["points"] = points
            };
        }

        private static JArray BuildStationAxes(DataStation station)
        {
            var axes = new JArray();
            List<AxisConfig> configurations = station?.dataAxis?.axisConfigs;
            if (configurations == null) return axes;
            for (int i = 0; i < configurations.Count; i++)
            {
                AxisConfig configuration = configurations[i];
                if (configuration == null
                    || string.IsNullOrWhiteSpace(configuration.AxisName)
                    || string.Equals(configuration.AxisName, "-1", StringComparison.Ordinal))
                {
                    continue;
                }
                axes.Add(new JObject
                {
                    ["slotIndex"] = i,
                    ["cardNum"] = configuration.CardNum ?? string.Empty,
                    ["axisName"] = configuration.AxisName
                });
            }
            return axes;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleAddStation(JObject request)
        {
            EnsureRuntimeReady();
            if (runtime.Stores.Stations?.Items == null)
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
            List<DataStation> list = runtime.Stores.Stations.Items;
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
            List<DataStation> list = runtime.Stores.Stations.Items;
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
                List<DataStation> list = runtime.Stores.Stations.Items;
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
            foreach (DataPos point in EnumerateNamedPoints(station))
            {
                array.Add(BuildPointJObject(point));
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

        private JObject HandlePlanMotionPoints(JObject request)
        {
            EnsureRuntimeReady();
            int stationIndex = ReadRequiredInt(request, "stationIndex");
            if (!(request["pointNames"] is JArray pointNamesToken)
                || pointNamesToken.Count < 1 || pointNamesToken.Count > 20)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "pointNames 必须包含1到20个点位名称。");
            }

            var pointNames = new List<string>();
            foreach (JToken token in pointNamesToken)
            {
                string name = token?.Value<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", "点位名称不能为空且长度不能超过100。");
                }
                if (pointNames.Contains(name, StringComparer.Ordinal))
                {
                    throw new BridgeRequestException(400, "DUPLICATE_NAME", $"pointNames 包含重复名称：{name}。");
                }
                pointNames.Add(name);
            }

            StationDefinitionStore store = runtime.Stores.Stations;
            if (store?.Items == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "工站存储未初始化。");
            }
            if (stationIndex < 0 || stationIndex >= store.Items.Count)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"stationIndex 超出范围 [0, {store.Items.Count})。");
            }
            List<DataStation> candidate = store.Items.Select(ObjectGraphCloner.Clone).ToList();
            DataStation station = candidate[stationIndex];
            if (station?.ListDataPos == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "工站点位列表未初始化。");
            }
            RebuildStationPointDictionary(station);

            var existingByName = station.ListDataPos
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            int requiredSlots = pointNames.Count(name => !existingByName.ContainsKey(name));
            List<DataPos> emptySlots = station.ListDataPos
                .Where(item => item != null && string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Index)
                .ToList();
            if (emptySlots.Count < requiredSlots)
            {
                throw new BridgeRequestException(409, "POINT_CAPACITY_EXCEEDED",
                    $"工站仅剩 {emptySlots.Count} 个空点位槽，无法新增 {requiredSlots} 个规划点位。");
            }

            var planned = new JArray();
            int createdCount = 0;
            foreach (string name in pointNames)
            {
                bool created = !existingByName.TryGetValue(name, out DataPos point);
                if (created)
                {
                    point = emptySlots[createdCount];
                    point.Name = name;
                    point.IsTaught = false;
                    station.dicDataPos[name] = point;
                    existingByName[name] = point;
                    createdCount++;
                }
                planned.Add(new JObject
                {
                    ["index"] = point.Index,
                    ["name"] = point.Name,
                    ["teachingState"] = point.TeachingState,
                    ["taught"] = point.IsMotionReady,
                    ["created"] = created,
                    ["resourceRef"] = $"motion_point:{stationIndex}:{point.Index}"
                });
            }
            RebuildStationPointDictionary(station);

            if (createdCount > 0)
            {
                EnsureAllProcsInactiveForAiStructureCommit("规划运动点位");
                if (!store.TryCommit(runtime.Paths.ConfigPath, candidate, out string error))
                {
                    throw new BridgeRequestException(500, "STATION_COMMIT_FAILED", error);
                }
            }
            if (createdCount > 0) runtime.EditorUi?.RefreshStations();
            return new JObject
            {
                ["ok"] = true,
                ["configurationSaved"] = true,
                ["stationIndex"] = stationIndex,
                ["stationName"] = station.Name ?? string.Empty,
                ["createdCount"] = createdCount,
                ["existingCount"] = pointNames.Count - createdCount,
                ["points"] = planned,
                ["nextStep"] = "这些点位名称现在可被流程引用；新建点位仍需人工编辑坐标或在工站界面取点，未示教前启动校验会阻止相关运动。"
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
            bool wasUnnamed = string.IsNullOrWhiteSpace(pos.Name);

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
            bool coordinatesChanged = x.HasValue || y.HasValue || z.HasValue
                || u.HasValue || v.HasValue || w.HasValue;
            if (coordinatesChanged)
            {
                pos.IsTaught = true;
            }
            else if (changed && wasUnnamed && !string.IsNullOrWhiteSpace(pos.Name))
            {
                // 原空槽位只有名称时属于规划状态；既有点位改名不降低旧数据的兼容状态。
                pos.IsTaught = false;
            }
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
            pos.IsTaught = null;
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

    }
}
