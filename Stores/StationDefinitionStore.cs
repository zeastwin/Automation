using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 当前工站定义的内存单一事实源。
    /// </summary>
    public sealed class StationDefinitionStore
    {
        private readonly List<DataStation> items = new List<DataStation>();

        public List<DataStation> Items => items;

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
            if (AtomicJsonFileStore.Save(configPath, "DataStation", items))
            {
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
        }
    }
}
