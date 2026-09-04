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
            int pointCapacity = DataStation.GetPointCapacity(station.Type);
            if (index < 0 || index >= pointCapacity)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"点位 index 超出范围 [0, {pointCapacity})。");
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
                    ["type"] = station.Type.ToString(),
                    ["communicationName"] = station.CommunicationName ?? string.Empty,
                    ["pointFromRobot"] = station.PointFromRobot,
                    ["remoteMode"] = station.RemoteMode,
                    ["remoteCommunicationName"] = station.RemoteCommunicationName ?? string.Empty,
                    ["coordinateSystem"] = station.CoordinateSystem,
                    ["manualSpeedPercent"] = station.ManualSpeedPercent,
                    ["axisCount"] = axes.Count,
                    ["axes"] = axes,
                    ["pointCapacity"] = DataStation.GetPointCapacity(station.Type),
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
                ["type"] = station.Type.ToString(),
                ["communicationName"] = station.CommunicationName ?? string.Empty,
                ["pointFromRobot"] = station.PointFromRobot,
                ["remoteMode"] = station.RemoteMode,
                ["remoteCommunicationName"] = station.RemoteCommunicationName ?? string.Empty,
                ["coordinateSystem"] = station.CoordinateSystem,
                ["manualSpeedPercent"] = station.ManualSpeedPercent,
                ["axisCount"] = axes.Count,
                ["axes"] = axes,
                ["pointCapacity"] = DataStation.GetPointCapacity(station.Type),
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
            DataStation station = store.Items[stationIndex];
            if (station?.ListDataPos == null)
            {
                throw new BridgeRequestException(500, "STORE_UNAVAILABLE", "工站点位列表未初始化。");
            }

            // 先在工站配置锁内确认是否真的需要写入，保持纯幂等查询不受流程运行闸门影响。
            lock (station)
            {
                AnalyzeMotionPointPlan(
                    station,
                    pointNames,
                    out int currentCapacity,
                    out Dictionary<string, DataPos> currentPoints,
                    out _);
                if (pointNames.All(currentPoints.ContainsKey))
                {
                    return BuildMotionPointPlanResult(
                        stationIndex,
                        station.Name,
                        currentCapacity,
                        pointNames.Count,
                        0,
                        BuildMotionPointPlanProjection(stationIndex, pointNames, currentPoints, null));
                }
            }

            EnsureAllProcsInactiveForAiStructureCommit("规划运动点位");

            JObject result;
            int createdCount = 0;
            lock (station)
            {
                if (stationIndex >= store.Items.Count
                    || !ReferenceEquals(store.Items[stationIndex], station))
                {
                    throw new BridgeRequestException(
                        409,
                        "STATION_CONFIGURATION_CHANGED",
                        "规划运动点位期间工站配置已被替换，请刷新资源后重试。");
                }

                AnalyzeMotionPointPlan(
                    station,
                    pointNames,
                    out int pointCapacity,
                    out Dictionary<string, DataPos> existingByName,
                    out List<DataPos> emptySlots);

                if (pointNames.All(existingByName.ContainsKey))
                {
                    return BuildMotionPointPlanResult(
                        stationIndex,
                        station.Name,
                        pointCapacity,
                        pointNames.Count,
                        0,
                        BuildMotionPointPlanProjection(
                            stationIndex,
                            pointNames,
                            existingByName,
                            null));
                }

                bool dictionaryWasNull = station.dicDataPos == null;
                Dictionary<string, DataPos> pointDictionary = station.dicDataPos
                    ?? new Dictionary<string, DataPos>(StringComparer.Ordinal);
                KeyValuePair<string, DataPos>[] dictionarySnapshot = pointDictionary.ToArray();
                var changedPoints = new List<DataPos>();
                var previousNames = new List<string>();
                var previousTeachingStates = new List<bool?>();
                var createdNames = new HashSet<string>(StringComparer.Ordinal);
                station.dicDataPos = pointDictionary;
                pointDictionary.Clear();
                foreach (DataPos namedPoint in EnumerateNamedPoints(station))
                {
                    if (!pointDictionary.ContainsKey(namedPoint.Name))
                    {
                        pointDictionary[namedPoint.Name] = namedPoint;
                    }
                }

                foreach (string name in pointNames)
                {
                    if (existingByName.ContainsKey(name))
                    {
                        continue;
                    }
                    DataPos point = emptySlots[createdCount];
                    changedPoints.Add(point);
                    previousNames.Add(point.Name);
                    previousTeachingStates.Add(point.IsTaught);
                    point.Name = name;
                    point.IsTaught = false;
                    pointDictionary[name] = point;
                    existingByName[name] = point;
                    createdNames.Add(name);
                    createdCount++;
                }

                if (createdCount > 0)
                {
                    bool persisted = false;
                    try
                    {
                        if (!store.TryPersistCurrent(runtime.Paths.ConfigPath, out string error))
                        {
                            throw new BridgeRequestException(500, "STATION_COMMIT_FAILED", error);
                        }
                        persisted = true;
                    }
                    finally
                    {
                        if (!persisted)
                        {
                            for (int i = 0; i < changedPoints.Count; i++)
                            {
                                changedPoints[i].Name = previousNames[i];
                                changedPoints[i].IsTaught = previousTeachingStates[i];
                            }
                            pointDictionary.Clear();
                            foreach (KeyValuePair<string, DataPos> pair in dictionarySnapshot)
                            {
                                pointDictionary[pair.Key] = pair.Value;
                            }
                            if (dictionaryWasNull)
                            {
                                station.dicDataPos = null;
                            }
                        }
                    }
                }

                result = BuildMotionPointPlanResult(
                    stationIndex,
                    station.Name,
                    pointCapacity,
                    pointNames.Count,
                    createdCount,
                    BuildMotionPointPlanProjection(
                        stationIndex,
                        pointNames,
                        existingByName,
                        createdNames));
            }

            return result;
        }

        private static void AnalyzeMotionPointPlan(
            DataStation station,
            IReadOnlyCollection<string> pointNames,
            out int pointCapacity,
            out Dictionary<string, DataPos> existingByName,
            out List<DataPos> emptySlots)
        {
            if (station?.ListDataPos == null)
            {
                throw new BridgeRequestException(
                    500,
                    "STORE_UNAVAILABLE",
                    "工站点位列表未初始化。");
            }
            int capacity = DataStation.GetPointCapacity(station.Type);
            DataPos invalidPoint = EnumerateNamedPoints(station).FirstOrDefault(point =>
                point.Index < 0 || point.Index >= capacity);
            if (invalidPoint != null)
            {
                throw new BridgeRequestException(
                    409,
                    "POINT_INDEX_OUT_OF_RANGE",
                    $"{station.Type} 工站点位“{invalidPoint.Name}”索引为 {invalidPoint.Index}，允许范围为 [0, {capacity})。");
            }

            Dictionary<string, DataPos> existing = station.ListDataPos
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            int requiredSlots = pointNames.Count(name => !existing.ContainsKey(name));
            List<DataPos> availableSlots = station.ListDataPos
                .Where(item => item != null
                    && item.Index >= 0
                    && item.Index < capacity
                    && string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Index)
                .ToList();
            if (availableSlots.Count < requiredSlots)
            {
                throw new BridgeRequestException(
                    409,
                    "POINT_CAPACITY_EXCEEDED",
                    $"{station.Type} 工站容量为 {capacity}，仅剩 {availableSlots.Count} 个空点位槽，无法新增 {requiredSlots} 个规划点位。");
            }
            pointCapacity = capacity;
            existingByName = existing;
            emptySlots = availableSlots;
        }

        private static JArray BuildMotionPointPlanProjection(
            int stationIndex,
            IEnumerable<string> pointNames,
            IReadOnlyDictionary<string, DataPos> existingByName,
            ISet<string> createdNames)
        {
            var planned = new JArray();
            foreach (string name in pointNames)
            {
                DataPos point = existingByName[name];
                planned.Add(new JObject
                {
                    ["index"] = point.Index,
                    ["name"] = point.Name,
                    ["teachingState"] = point.TeachingState,
                    ["taught"] = point.IsMotionReady,
                    ["created"] = createdNames?.Contains(name) == true,
                    ["resourceRef"] = $"motion_point:{stationIndex}:{point.Index}"
                });
            }
            return planned;
        }

        private static JObject BuildMotionPointPlanResult(
            int stationIndex,
            string stationName,
            int pointCapacity,
            int requestedCount,
            int createdCount,
            JArray planned)
        {
            return new JObject
            {
                ["ok"] = true,
                ["configurationSaved"] = true,
                ["stationIndex"] = stationIndex,
                ["stationName"] = stationName ?? string.Empty,
                ["pointCapacity"] = pointCapacity,
                ["createdCount"] = createdCount,
                ["existingCount"] = requestedCount - createdCount,
                ["points"] = planned,
                ["nextStep"] = "这些点位名称现在可被流程引用；新建点位仍需人工编辑坐标或在工站界面取点，未示教前启动校验会阻止相关运动。"
            };
        }

    }
}
