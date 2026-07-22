using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// IO 配置及其名称索引的内存单一事实源。
    /// Map 原地更新，确保流程引擎持有的 ByName 引用始终有效。
    /// </summary>
    public sealed class IoConfigurationStore
    {
        private readonly List<List<IO>> map = new List<List<IO>>();
        private readonly Dictionary<string, IO> byName =
            new Dictionary<string, IO>(StringComparer.Ordinal);
        private readonly List<string> outputNames = new List<string>();
        private readonly List<string> inputNames = new List<string>();
        private readonly List<string> allNames = new List<string>();

        public List<List<IO>> Map => map;
        public Dictionary<string, IO> ByName => byName;
        public List<string> OutputNames => outputNames;
        public List<string> InputNames => inputNames;
        public List<string> AllNames => allNames;

        public bool Load(string configPath, out string error)
        {
            error = null;
            Directory.CreateDirectory(configPath);
            string filePath = Path.Combine(configPath, "IOMap.json");
            if (!File.Exists(filePath))
            {
                return TryCommit(configPath, Array.Empty<List<IO>>(), out error);
            }
            List<List<IO>> loaded = AtomicJsonFileStore.Read<List<List<IO>>>(configPath, "IOMap");
            if (loaded == null)
            {
                error = "IO配置主文件及备份均无法读取。";
                return false;
            }
            return TryReplaceMap(loaded, out error);
        }

        public List<List<IO>> CreateSnapshot()
        {
            return CloneMap(map);
        }

        public bool TryCommit(string configPath, IEnumerable<List<IO>> source, out string error)
        {
            error = null;
            if (source == null)
            {
                error = "IO配置为空。";
                return false;
            }
            List<List<IO>> replacement = CloneMap(source);
            if (!TryBuildIndex(replacement, out Dictionary<string, IO> nextByName,
                    out List<string> nextOutputs, out List<string> nextInputs,
                    out List<string> nextAll, out error))
            {
                return false;
            }
            if (!AtomicJsonFileStore.Save(configPath, "IOMap", replacement))
            {
                error = "IO配置保存失败，正式内存未修改。";
                return false;
            }
            ReplaceMap(replacement, nextByName, nextOutputs, nextInputs, nextAll);
            return true;
        }

        public bool TryReplaceMap(IEnumerable<List<IO>> source, out string error)
        {
            if (source == null)
            {
                error = "IO配置为空。";
                return false;
            }
            List<List<IO>> replacement = CloneMap(source);
            if (!TryBuildIndex(replacement, out Dictionary<string, IO> nextByName,
                    out List<string> nextOutputs, out List<string> nextInputs,
                    out List<string> nextAll, out error))
            {
                return false;
            }
            ReplaceMap(replacement, nextByName, nextOutputs, nextInputs, nextAll);
            return true;
        }

        private static List<List<IO>> CloneMap(IEnumerable<List<IO>> source)
        {
            return source.Select(cardItems => cardItems == null
                    ? null
                    : cardItems.Select(ObjectGraphCloner.Clone).ToList())
                .ToList();
        }

        private void ReplaceMap(List<List<IO>> replacement,
            Dictionary<string, IO> nextByName, List<string> nextOutputs,
            List<string> nextInputs, List<string> nextAll)
        {
            map.Clear();
            map.AddRange(replacement);
            ReplaceIndex(nextByName, nextOutputs, nextInputs, nextAll);
        }

        public bool TryRebuildIndex(out string error)
        {
            if (!TryBuildIndex(map, out Dictionary<string, IO> nextByName,
                    out List<string> nextOutputs, out List<string> nextInputs,
                    out List<string> nextAll, out error))
            {
                return false;
            }
            ReplaceIndex(nextByName, nextOutputs, nextInputs, nextAll);
            return true;
        }

        private static bool TryBuildIndex(IReadOnlyCollection<List<IO>> source,
            out Dictionary<string, IO> nextByName, out List<string> nextOutputs,
            out List<string> nextInputs, out List<string> nextAll, out string error)
        {
            nextByName = new Dictionary<string, IO>(StringComparer.Ordinal);
            nextOutputs = new List<string>();
            nextInputs = new List<string>();
            nextAll = new List<string>();
            error = null;
            if (source == null)
            {
                error = "IO配置为空。";
                return false;
            }
            foreach (List<IO> cardItems in source)
            {
                if (cardItems == null)
                {
                    error = "IO配置包含空卡列表。";
                    return false;
                }
                foreach (IO item in cardItems)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Name))
                    {
                        continue;
                    }
                    if (nextByName.ContainsKey(item.Name))
                    {
                        error = $"IO名称重复：{item.Name}";
                        return false;
                    }
                    nextByName.Add(item.Name, item);
                    if (item.IOType == "通用输出")
                    {
                        nextOutputs.Add(item.Name);
                    }
                    if (item.IOType == "通用输入")
                    {
                        nextInputs.Add(item.Name);
                    }
                    nextAll.Add(item.Name);
                }
            }
            return true;
        }

        private void ReplaceIndex(Dictionary<string, IO> nextByName, List<string> nextOutputs,
            List<string> nextInputs, List<string> nextAll)
        {
            byName.Clear();
            foreach (KeyValuePair<string, IO> pair in nextByName)
            {
                byName.Add(pair.Key, pair.Value);
            }
            outputNames.Clear();
            outputNames.AddRange(nextOutputs);
            inputNames.Clear();
            inputNames.AddRange(nextInputs);
            allNames.Clear();
            allNames.AddRange(nextAll);
        }
    }
}
