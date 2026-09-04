using System;
// 模块：持久化 / 设备配置。
// 职责范围：管理控制卡、通讯、PLC、IO、工站和点位配置，不执行设备动作。

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

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
        private long version;
        private int configurationFingerprint;

        public List<List<IO>> Map => map;
        public Dictionary<string, IO> ByName => byName;
        public List<string> OutputNames => outputNames;
        public List<string> InputNames => inputNames;
        public List<string> AllNames => allNames;
        public long Version => Interlocked.Read(ref version);

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

        /// <summary>
        /// 生成指定控制卡调整点数后的 IO 候选快照，不修改正式内存或磁盘。
        /// 重叠编号保留原配置；新增编号使用默认配置；缩减不得丢弃用户已配置的 IO。
        /// </summary>
        public bool TryCreateResizedCardMap(
            int cardIndex,
            int inputCount,
            int outputCount,
            out List<List<IO>> replacement,
            out string error)
        {
            replacement = null;
            error = null;
            if (cardIndex < 0 || inputCount < 0 || outputCount < 0)
            {
                error = "控制卡索引和IO数量不能为负数。";
                return false;
            }

            List<List<IO>> candidate = CloneMap(map);
            if (!TryBuildIndex(
                    candidate,
                    out _,
                    out _,
                    out _,
                    out _,
                    out error))
            {
                return false;
            }
            while (candidate.Count <= cardIndex)
            {
                candidate.Add(new List<IO>());
            }

            List<IO> current = candidate[cardIndex] ?? new List<IO>();
            var inputs = new Dictionary<int, IO>();
            var outputs = new Dictionary<int, IO>();
            foreach (IO item in current)
            {
                bool isInput = string.Equals(item.IOType, "通用输入", StringComparison.Ordinal);
                Dictionary<int, IO> byIndex = isInput ? inputs : outputs;
                int ioIndex = int.Parse(item.IOIndex, CultureInfo.InvariantCulture);
                if (byIndex.ContainsKey(ioIndex))
                {
                    error = $"{cardIndex}号卡{item.IOType}编号重复:{ioIndex}。";
                    return false;
                }
                byIndex.Add(ioIndex, item);

                int nextCount = isInput ? inputCount : outputCount;
                if (ioIndex >= nextCount && IsConfiguredIo(item))
                {
                    string displayName = string.IsNullOrWhiteSpace(item.Name)
                        ? $"{item.IOType}{ioIndex}"
                        : item.Name;
                    error = $"无法将{cardIndex}号卡{item.IOType}点数缩减到{nextCount}：将丢弃已配置IO[{displayName}](编号{ioIndex})。请先清除该IO配置。";
                    return false;
                }
            }

            var resized = new List<IO>(inputCount + outputCount);
            for (int ioIndex = 0; ioIndex < inputCount; ioIndex++)
            {
                resized.Add(CreateResizedIo(
                    inputs.TryGetValue(ioIndex, out IO existing) ? existing : null,
                    cardIndex,
                    ioIndex,
                    ioIndex,
                    "通用输入"));
            }
            for (int ioIndex = 0; ioIndex < outputCount; ioIndex++)
            {
                resized.Add(CreateResizedIo(
                    outputs.TryGetValue(ioIndex, out IO existing) ? existing : null,
                    cardIndex,
                    inputCount + ioIndex,
                    ioIndex,
                    "通用输出"));
            }
            candidate[cardIndex] = resized;
            if (!TryBuildIndex(
                    candidate,
                    out _,
                    out _,
                    out _,
                    out _,
                    out error))
            {
                return false;
            }
            replacement = candidate;
            return true;
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

        private static IO CreateResizedIo(
            IO source,
            int cardIndex,
            int flatIndex,
            int ioIndex,
            string ioType)
        {
            IO result = source ?? new IO
            {
                UsedType = "通用",
                EffectLevel = "正常"
            };
            result.Index = flatIndex;
            result.CardNum = cardIndex;
            result.Module = 0;
            result.IOIndex = ioIndex.ToString(CultureInfo.InvariantCulture);
            result.IOType = ioType;
            return result;
        }

        private static bool IsConfiguredIo(IO item)
        {
            return !string.IsNullOrWhiteSpace(item.Name)
                || !string.IsNullOrWhiteSpace(item.Note)
                || item.IsRemark
                || !string.Equals(item.UsedType, "通用", StringComparison.Ordinal)
                || !string.Equals(item.EffectLevel, "正常", StringComparison.Ordinal);
        }

        private void ReplaceMap(List<List<IO>> replacement,
            Dictionary<string, IO> nextByName, List<string> nextOutputs,
            List<string> nextInputs, List<string> nextAll)
        {
            map.Clear();
            map.AddRange(replacement);
            ReplaceIndex(nextByName, nextOutputs, nextInputs, nextAll);
            configurationFingerprint = CalculateFingerprint(map);
            Interlocked.Increment(ref version);
        }

        public bool TryRebuildIndex(out string error)
        {
            if (!TryBuildIndex(map, out Dictionary<string, IO> nextByName,
                    out List<string> nextOutputs, out List<string> nextInputs,
                    out List<string> nextAll, out error))
            {
                return false;
            }
            int nextFingerprint = CalculateFingerprint(map);
            ReplaceIndex(nextByName, nextOutputs, nextInputs, nextAll);
            if (nextFingerprint != configurationFingerprint)
            {
                configurationFingerprint = nextFingerprint;
                Interlocked.Increment(ref version);
            }
            return true;
        }

        private static int CalculateFingerprint(IEnumerable<List<IO>> source)
        {
            unchecked
            {
                int hash = 17;
                foreach (List<IO> cardItems in source ?? Enumerable.Empty<List<IO>>())
                {
                    hash = hash * 31 + (cardItems?.Count ?? -1);
                    if (cardItems == null)
                    {
                        continue;
                    }
                    foreach (IO item in cardItems)
                    {
                        if (item == null)
                        {
                            hash = hash * 31;
                            continue;
                        }
                        hash = hash * 31 + item.Index;
                        hash = hash * 31 + item.CardNum;
                        hash = hash * 31 + item.Module;
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.Name ?? string.Empty);
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.IOIndex ?? string.Empty);
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.IOType ?? string.Empty);
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.UsedType ?? string.Empty);
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.EffectLevel ?? string.Empty);
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(item.Note ?? string.Empty);
                    }
                }
                return hash;
            }
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
            int cardIndex = 0;
            foreach (List<IO> cardItems in source)
            {
                if (cardItems == null)
                {
                    error = "IO配置包含空卡列表。";
                    return false;
                }
                var inputIndexes = new HashSet<ushort>();
                var outputIndexes = new HashSet<ushort>();
                foreach (IO item in cardItems)
                {
                    if (item == null)
                    {
                        error = $"{cardIndex}号卡IO配置包含空项。";
                        return false;
                    }
                    if (item.CardNum != cardIndex)
                    {
                        error = $"{cardIndex}号卡IO包含错误卡号：{item.CardNum}。";
                        return false;
                    }
                    if (item.Module != 0)
                    {
                        error = $"IO[{item.Name}]模块号必须为0；当前雷赛总线卡使用扁平IO编号。";
                        return false;
                    }
                    bool isInput = string.Equals(item.IOType, "通用输入", StringComparison.Ordinal);
                    bool isOutput = string.Equals(item.IOType, "通用输出", StringComparison.Ordinal);
                    if (!isInput && !isOutput)
                    {
                        error = $"IO[{item.Name}]类型无效：{item.IOType}";
                        return false;
                    }
                    if (!ushort.TryParse(
                            item.IOIndex,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out ushort ioIndex))
                    {
                        error = $"IO[{item.Name}]编号无效：{item.IOIndex}";
                        return false;
                    }
                    HashSet<ushort> directionIndexes = isInput ? inputIndexes : outputIndexes;
                    if (!directionIndexes.Add(ioIndex))
                    {
                        error = $"{cardIndex}号卡{item.IOType}编号重复:{ioIndex}。";
                        return false;
                    }
                    if (!string.Equals(item.EffectLevel, "正常", StringComparison.Ordinal)
                        && !string.Equals(item.EffectLevel, "取反", StringComparison.Ordinal))
                    {
                        error = $"IO[{item.Name}]电平类型无效：{item.EffectLevel}";
                        return false;
                    }
                    bool isGeneral = string.Equals(item.UsedType, "通用", StringComparison.Ordinal);
                    bool isEmergency = string.Equals(item.UsedType, "急停", StringComparison.Ordinal);
                    bool isReset = string.Equals(item.UsedType, "复位", StringComparison.Ordinal);
                    bool isStart = string.Equals(item.UsedType, "启动", StringComparison.Ordinal);
                    bool isPause = string.Equals(item.UsedType, "暂停", StringComparison.Ordinal);
                    bool isStop = string.Equals(item.UsedType, "停止", StringComparison.Ordinal);
                    bool isBrake = string.Equals(item.UsedType, "刹车", StringComparison.Ordinal);
                    bool isSystemInput = isEmergency || isReset || isStart || isPause || isStop;
                    if (!isGeneral && !isSystemInput && !isBrake)
                    {
                        error = $"IO[{item.Name}]使用类型无效：{item.UsedType}";
                        return false;
                    }
                    if (isSystemInput && !isInput)
                    {
                        error = $"{item.UsedType}IO必须配置为通用输入：{item.Name}";
                        return false;
                    }
                    if (isBrake && !isOutput)
                    {
                        error = $"刹车IO必须配置为通用输出：{item.Name}";
                        return false;
                    }
                    if ((isSystemInput || isBrake) && string.IsNullOrWhiteSpace(item.Name))
                    {
                        error = "系统IO必须配置名称。";
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(item.Name))
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
                cardIndex++;
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
