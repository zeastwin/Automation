using System;
// 模块：持久化 / 设备配置。
// 职责范围：管理控制卡、通讯、PLC、IO、工站和点位配置，不执行设备动作。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Automation
{
    /// <summary>
    /// 当前工站定义的内存单一事实源。
    /// </summary>
    public sealed class StationDefinitionStore
    {
        private readonly List<DataStation> items = new List<DataStation>();
        private long version;

        public List<DataStation> Items => items;
        public long Version => Interlocked.Read(ref version);

        public bool Load(string configPath, out string error)
        {
            error = null;
            Directory.CreateDirectory(configPath);
            string filePath = Path.Combine(configPath, "DataStation.json");
            if (!File.Exists(filePath))
            {
                return TryCommit(configPath, Array.Empty<DataStation>(), out error);
            }

            List<DataStation> loaded = AtomicJsonFileStore.Read<List<DataStation>>(
                configPath, "DataStation");
            if (loaded == null)
            {
                error = "工站配置主文件及备份均无法读取。";
                return false;
            }
            if (!TryValidatePointCapacities(loaded, out error))
            {
                error = "工站配置加载失败：" + error;
                return false;
            }
            NormalizePointCollections(loaded);
            if (!TryValidatePointCapacities(loaded, out error))
            {
                error = "工站配置加载失败：" + error;
                return false;
            }
            ReplaceAll(loaded);
            return true;
        }

        public bool TryCommit(string configPath, IEnumerable<DataStation> stations, out string error)
        {
            error = null;
            if (stations == null)
            {
                error = "工站配置为空。";
                return false;
            }
            List<DataStation> candidate = stations
                .Select(ObjectGraphCloner.Clone)
                .ToList();
            if (!TryValidatePointCapacities(candidate, out error))
            {
                return false;
            }
            NormalizePointCollections(candidate);
            if (!TryValidatePointCapacities(candidate, out error))
            {
                return false;
            }
            if (!AtomicJsonFileStore.Save(configPath, "DataStation", candidate))
            {
                error = "工站配置保存失败，正式内存未修改。";
                return false;
            }
            ReplaceAll(candidate);
            return true;
        }

        public bool TryPersistCurrent(string configPath, out string error)
        {
            error = null;
            if (!TryValidatePointCapacities(items, out error))
            {
                return false;
            }
            if (AtomicJsonFileStore.Save(configPath, "DataStation", items))
            {
                Interlocked.Increment(ref version);
                return true;
            }
            error = "工站配置保存失败。";
            return false;
        }

        public void ReplaceAll(IEnumerable<DataStation> stations)
        {
            if (stations == null)
            {
                throw new ArgumentNullException(nameof(stations));
            }
            List<DataStation> replacement = stations.ToList();
            items.Clear();
            items.AddRange(replacement);
            Interlocked.Increment(ref version);
        }

        private static void NormalizePointCollections(IEnumerable<DataStation> stations)
        {
            foreach (DataStation station in stations ?? Array.Empty<DataStation>())
            {
                if (station == null) continue;
                station.NormalizeConfiguration();
                foreach (DataPos legacyPoint in ((IEnumerable<DataPos>)station.dicDataPos?.Values
                    ?? Array.Empty<DataPos>())
                    .Where(point => point != null
                        && point.Index >= 0 && point.Index < DataStation.PointCapacity
                        && !string.IsNullOrWhiteSpace(point.Name)))
                {
                    while (station.ListDataPos.Count <= legacyPoint.Index)
                    {
                        station.ListDataPos.Add(new DataPos(station.ListDataPos.Count));
                    }
                    DataPos listPoint = station.ListDataPos[legacyPoint.Index];
                    if (listPoint == null || string.IsNullOrWhiteSpace(listPoint.Name))
                    {
                        station.ListDataPos[legacyPoint.Index] = legacyPoint;
                    }
                }
                while (station.ListDataPos.Count < DataStation.PointCapacity)
                {
                    station.ListDataPos.Add(new DataPos(station.ListDataPos.Count));
                }
                foreach (DataPos point in station.ListDataPos)
                {
                    point?.NormalizeRobotMetadata();
                }
                station.dicDataPos = station.ListDataPos
                    .Where(point => point != null && !string.IsNullOrWhiteSpace(point.Name))
                    .GroupBy(point => point.Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            }
        }

        private static bool TryValidatePointCapacities(
            IEnumerable<DataStation> stations,
            out string error)
        {
            error = null;
            foreach (DataStation station in stations ?? Array.Empty<DataStation>())
            {
                if (station == null)
                {
                    continue;
                }
                int capacity = DataStation.GetPointCapacity(station.Type);
                IEnumerable<DataPos> points = station.ListDataPos ?? new List<DataPos>();
                if (station.dicDataPos != null)
                {
                    points = points.Concat(station.dicDataPos.Values);
                }
                DataPos invalidPoint = points.FirstOrDefault(point => point != null
                    && !string.IsNullOrWhiteSpace(point.Name)
                    && (point.Index < 0 || point.Index >= capacity));
                if (invalidPoint == null)
                {
                    continue;
                }
                error = $"工站“{station.Name}”的命名点位“{invalidPoint.Name}”索引无效：{invalidPoint.Index}；{station.Type} 工站仅允许 [0, {capacity})。";
                return false;
            }
            return true;
        }
    }
}
