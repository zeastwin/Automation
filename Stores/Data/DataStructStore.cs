using System;
// 模块：持久化 / 业务数据。
// 职责范围：管理报警与数据结构配置的模型和持久化。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;

namespace Automation
{
    public enum DataStructValueType
    {
        Number,
        Text
    }

    public sealed class DataStructFieldValueUpdate
    {
        public int FieldIndex { get; set; }
        public string Value { get; set; }
        public DataStructValueType? ExpectedType { get; set; }
    }

    public sealed class DataStructFieldValueSnapshot
    {
        public int FieldIndex { get; set; }
        public object Value { get; set; }
        public DataStructValueType ValueType { get; set; }
    }

    public class DataStructStore
    {
        private sealed class PreparedFieldWrite
        {
            internal DataStructFieldSlot Field { get; set; }
            internal double Number { get; set; }
            internal string Text { get; set; }
        }

        private sealed class FieldLockScope : IDisposable
        {
            private readonly IReadOnlyList<DataStructFieldSlot> fields;

            internal FieldLockScope(IEnumerable<DataStructFieldSlot> fields)
            {
                this.fields = GetOrderedUniqueFields(fields);
                EnterFieldLocks(this.fields);
            }

            public void Dispose()
            {
                ExitFieldLocks(fields);
            }
        }

        private readonly PlatformRuntime runtime;
        private readonly object dataLock = new object();
        private readonly List<DataStruct> dataStructs = new List<DataStruct>();
        private readonly List<int> structVersions = new List<int>();
        private volatile DataStructItem[][] runtimeItems = Array.Empty<DataStructItem[]>();
        private int version = 0;

        internal DataStructStore(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public int Version
        {
            get
            {
                lock (dataLock)
                {
                    return version;
                }
            }
        }

        public bool Load(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "DataStruct.json");
            if (!File.Exists(filePath))
            {
                Reset();
                if (!Save(configPath))
                {
                    runtime.Safety.Lock("数据结构配置初始化保存失败");
                }
                return false;
            }

            try
            {
                List<DataStruct> data = AtomicJsonFileStore.Read<List<DataStruct>>(configPath, "DataStruct");
                if (data == null)
                {
                    runtime.Safety.Lock("数据结构配置及其备份均无法读取，已保留原文件并禁止继续运行");
                    return false;
                }
                if (!ValidateLoadedData(data, out string error))
                {
                    runtime.ProcessEngine?.Logger?.Log($"数据结构配置校验失败:{error}", LogLevel.Error);
                    runtime.Safety.Lock(error);
                    return false;
                }
                LoadFromList(data);
                return true;
            }
            catch (Exception e)
            {
                runtime.ProcessEngine?.Logger?.Log($"数据结构配置加载失败:{e}", LogLevel.Error);
                runtime.Safety.Lock($"数据结构配置加载失败:{e.Message}");
                return false;
            }
        }

        public bool Save(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            bool saved = AtomicJsonFileStore.Save(configPath, "DataStruct", BuildSaveData());
            if (!saved)
            {
                runtime.Safety.Lock("数据结构配置保存失败，内存与磁盘状态可能不一致");
            }
            return saved;
        }

        public bool TryUpsertAndSave(
            DataStruct candidate,
            string configPath,
            out bool created,
            out string error)
        {
            created = false;
            error = string.Empty;
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Name))
            {
                error = "数据结构名称不能为空。";
                return false;
            }

            List<DataStruct> snapshot = BuildSaveData();
            int index = snapshot.FindIndex(item => item != null
                && string.Equals(item.Name, candidate.Name.Trim(), StringComparison.Ordinal));
            DataStruct replacement = (DataStruct)candidate.Clone();
            replacement.Name = candidate.Name.Trim();
            NormalizeStruct(replacement);
            if (index < 0)
            {
                snapshot.Add(replacement);
                created = true;
            }
            else
            {
                snapshot[index] = replacement;
            }
            if (!ValidateLoadedData(snapshot, out error))
            {
                return false;
            }
            if (!AtomicJsonFileStore.Save(configPath, "DataStruct", snapshot))
            {
                error = "数据结构配置保存失败。";
                return false;
            }
            LoadFromList(snapshot);
            return true;
        }

        public bool TryDeleteAndSave(string name, string configPath, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "数据结构名称不能为空。";
                return false;
            }

            List<DataStruct> snapshot = BuildSaveData();
            int removed = snapshot.RemoveAll(item => item != null
                && string.Equals(item.Name, name.Trim(), StringComparison.Ordinal));
            if (removed == 0)
            {
                error = $"未找到数据结构：{name}";
                return false;
            }
            if (!ValidateLoadedData(snapshot, out error))
            {
                return false;
            }
            if (!AtomicJsonFileStore.Save(configPath, "DataStruct", snapshot))
            {
                error = "数据结构配置保存失败。";
                return false;
            }
            LoadFromList(snapshot);
            return true;
        }

        private void Reset()
        {
            lock (dataLock)
            {
                dataStructs.Clear();
                structVersions.Clear();
                RebuildRuntimeItemsNoLock();
                MarkChangedNoLock();
            }
        }

        private void LoadFromList(List<DataStruct> source)
        {
            lock (dataLock)
            {
                dataStructs.Clear();
                structVersions.Clear();
                if (source == null)
                {
                    return;
                }

                foreach (DataStruct dataStruct in source)
                {
                    if (dataStruct == null)
                    {
                        continue;
                    }
                    NormalizeStruct(dataStruct);
                    dataStructs.Add(dataStruct);
                    structVersions.Add(0);
                }
                RebuildRuntimeItemsNoLock();
                MarkChangedNoLock();
            }
        }

        private List<DataStruct> BuildSaveData()
        {
            lock (dataLock)
            {
                return dataStructs.Select(CloneDataStruct).ToList();
            }
        }

        private static DataStruct CloneDataStruct(DataStruct source)
        {
            var clone = new DataStruct { Name = source?.Name };
            if (source?.dataStructItems == null)
            {
                return clone;
            }
            foreach (DataStructItem item in source.dataStructItems)
            {
                if (item == null)
                {
                    clone.dataStructItems.Add(null);
                    continue;
                }
                lock (item.SyncRoot)
                {
                    using (LockRuntimeFields(item))
                    {
                        SyncRuntimeValuesToModelNoLock(item);
                        clone.dataStructItems.Add(item.Clone());
                    }
                }
            }
            return clone;
        }

        private static bool ValidateLoadedData(List<DataStruct> source, out string error)
        {
            error = "DataStruct.json 格式错误，请重新生成";
            if (source == null)
            {
                return false;
            }

            var structNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < source.Count; i++)
            {
                DataStruct dataStruct = source[i];
                if (dataStruct == null)
                {
                    error = "数据结构配置包含空对象，请重新生成";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(dataStruct.Name))
                {
                    error = "数据结构名称不能为空，请重新生成";
                    return false;
                }
                if (!structNames.Add(dataStruct.Name))
                {
                    error = $"数据结构名称重复:{dataStruct.Name}";
                    return false;
                }
                if (dataStruct.dataStructItems == null)
                {
                    error = "数据结构项列表为空，请重新生成";
                    return false;
                }

                var itemNames = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < dataStruct.dataStructItems.Count; j++)
                {
                    DataStructItem item = dataStruct.dataStructItems[j];
                    if (item == null)
                    {
                        error = $"数据结构 {dataStruct.Name} 包含空项";
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(item.Name) || !itemNames.Add(item.Name))
                    {
                        error = $"数据结构 {dataStruct.Name} 的数据项名称为空或重复";
                        return false;
                    }
                    if (!ValidateItem(item, out string itemError))
                    {
                        error = $"数据结构项无效: {itemError}";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ValidateItem(DataStructItem item, out string error)
        {
            error = "数据项为空";
            if (item == null)
            {
                return false;
            }
            if (item.FieldNames == null || item.FieldTypes == null || item.str == null || item.num == null)
            {
                error = "字段元数据缺失";
                return false;
            }

            HashSet<int> nameKeys = new HashSet<int>(item.FieldNames.Keys);
            HashSet<int> typeKeys = new HashSet<int>(item.FieldTypes.Keys);
            if (!nameKeys.SetEquals(typeKeys))
            {
                error = "字段名称与类型不一致";
                return false;
            }

            foreach (KeyValuePair<int, string> kvp in item.FieldNames)
            {
                if (kvp.Key < 0)
                {
                    error = "字段索引无效";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(kvp.Value))
                {
                    error = "字段名称不能为空";
                    return false;
                }
            }

            foreach (KeyValuePair<int, string> kvp in item.str)
            {
                if (!item.FieldTypes.TryGetValue(kvp.Key, out DataStructValueType type) || type != DataStructValueType.Text)
                {
                    error = "字段类型与文本值不匹配";
                    return false;
                }
                if (item.num.ContainsKey(kvp.Key))
                {
                    error = "字段索引重复";
                    return false;
                }
            }

            foreach (KeyValuePair<int, double> kvp in item.num)
            {
                if (!item.FieldTypes.TryGetValue(kvp.Key, out DataStructValueType type) || type != DataStructValueType.Number)
                {
                    error = "字段类型与数值不匹配";
                    return false;
                }
                if (item.str.ContainsKey(kvp.Key))
                {
                    error = "字段索引重复";
                    return false;
                }
                if (double.IsNaN(kvp.Value) || double.IsInfinity(kvp.Value))
                {
                    error = "数值字段必须是有限数";
                    return false;
                }
            }

            return true;
        }

        private static void NormalizeStruct(DataStruct dataStruct)
        {
            if (dataStruct.dataStructItems == null)
            {
                dataStruct.dataStructItems = new List<DataStructItem>();
            }

            foreach (DataStructItem item in dataStruct.dataStructItems)
            {
                NormalizeItem(item);
            }
        }

        private static void NormalizeItem(DataStructItem item)
        {
            if (item == null)
            {
                return;
            }
            if (item.FieldNames == null)
            {
                item.FieldNames = new Dictionary<int, string>();
            }
            if (item.FieldTypes == null)
            {
                item.FieldTypes = new Dictionary<int, DataStructValueType>();
            }
            if (item.str == null)
            {
                item.str = new Dictionary<int, string>();
            }
            if (item.num == null)
            {
                item.num = new Dictionary<int, double>();
            }
        }

        private static void PublishRuntimeStateNoLock(DataStructItem item)
        {
            var fields = new Dictionary<int, DataStructFieldSlot>();
            foreach (KeyValuePair<int, DataStructValueType> field in item.FieldTypes)
            {
                if (!item.FieldNames.TryGetValue(field.Key, out string name))
                {
                    continue;
                }
                bool hasValue;
                double number = 0;
                string text = null;
                if (field.Value == DataStructValueType.Number)
                {
                    hasValue = item.num.TryGetValue(field.Key, out number);
                }
                else
                {
                    hasValue = item.str.TryGetValue(field.Key, out text);
                }
                fields[field.Key] = new DataStructFieldSlot(
                    field.Key, name, field.Value, hasValue, number, text);
            }
            item.PublishRuntimeState(new DataStructRuntimeState(fields));
        }

        private static void SyncRuntimeValuesToModelNoLock(DataStructItem item)
        {
            DataStructRuntimeState state = item.RuntimeState;
            if (state == null)
            {
                return;
            }
            foreach (DataStructFieldSlot field in state.Fields)
            {
                if (!field.HasValue)
                {
                    item.num.Remove(field.Index);
                    item.str.Remove(field.Index);
                    continue;
                }
                if (field.ValueType == DataStructValueType.Number)
                {
                    item.num[field.Index] = field.ReadNumber();
                    item.str.Remove(field.Index);
                }
                else
                {
                    item.str[field.Index] = field.ReadText();
                    item.num.Remove(field.Index);
                }
            }
        }

        private static List<DataStructFieldSlot> GetOrderedUniqueFields(
            IEnumerable<DataStructFieldSlot> fields)
        {
            return fields
                .GroupBy(field => field.Index)
                .Select(group => group.First())
                .OrderBy(field => field.Index)
                .ToList();
        }

        private static void EnterFieldLocks(
            IReadOnlyList<DataStructFieldSlot> fields)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                Monitor.Enter(fields[i].SyncRoot);
            }
        }

        private static void ExitFieldLocks(
            IReadOnlyList<DataStructFieldSlot> fields)
        {
            for (int i = fields.Count - 1; i >= 0; i--)
            {
                Monitor.Exit(fields[i].SyncRoot);
            }
        }

        private static FieldLockScope LockRuntimeFields(DataStructItem item)
        {
            return new FieldLockScope(
                item.RuntimeState?.Fields
                ?? Enumerable.Empty<DataStructFieldSlot>());
        }

        public int Count
        {
            get
            {
                lock (dataLock)
                {
                    return dataStructs.Count;
                }
            }
        }

        public List<string> GetStructNames()
        {
            lock (dataLock)
            {
                return dataStructs.Where(ds => !string.IsNullOrEmpty(ds.Name)).Select(ds => ds.Name).ToList();
            }
        }

        public List<DataStruct> GetSnapshot()
        {
            lock (dataLock)
            {
                return dataStructs.Select(CloneDataStruct).ToList();
            }
        }

        public bool TryGetStructSnapshotByName(string name, out DataStruct dataStruct)
        {
            dataStruct = null;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            lock (dataLock)
            {
                DataStruct target = dataStructs.FirstOrDefault(ds => ds.Name == name);
                if (target == null)
                {
                    return false;
                }
                dataStruct = CloneDataStruct(target);
                return true;
            }
        }

        public bool TryGetStructSnapshotByIndex(int index, out DataStruct dataStruct)
        {
            dataStruct = null;
            lock (dataLock)
            {
                if (index < 0 || index >= dataStructs.Count)
                {
                    return false;
                }
                dataStruct = CloneDataStruct(dataStructs[index]);
                return true;
            }
        }

        public bool TryGetStructIndexByName(string name, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            lock (dataLock)
            {
                for (int i = 0; i < dataStructs.Count; i++)
                {
                    if (dataStructs[i].Name == name)
                    {
                        index = i;
                        return true;
                    }
                }
            }
            return false;
        }

        public bool TryResolveStructIndex(bool useName, int configuredIndex, string configuredName,
            out int resolvedIndex, out string error)
        {
            resolvedIndex = -1;
            error = string.Empty;
            if (!useName)
            {
                DataStructItem[][] snapshot = runtimeItems;
                if (configuredIndex < 0 || configuredIndex >= snapshot.Length)
                {
                    error = $"结构体索引无效:{configuredIndex}";
                    return false;
                }
                resolvedIndex = configuredIndex;
                return true;
            }

            lock (dataLock)
            {
                if (string.IsNullOrWhiteSpace(configuredName))
                {
                    error = "结构体名称不能为空";
                    return false;
                }
                for (int i = 0; i < dataStructs.Count; i++)
                {
                    if (string.Equals(dataStructs[i]?.Name, configuredName, StringComparison.Ordinal))
                    {
                        resolvedIndex = i;
                        return true;
                    }
                }
                error = $"结构体不存在:{configuredName}";
                return false;
            }
        }

        public bool TryResolveItemIndex(int structIndex, bool useName, int configuredIndex,
            string configuredName, out int resolvedIndex, out string error)
        {
            resolvedIndex = -1;
            error = string.Empty;
            if (!useName)
            {
                DataStructItem[][] snapshot = runtimeItems;
                if (structIndex < 0 || structIndex >= snapshot.Length)
                {
                    error = $"结构体索引无效:{structIndex}";
                    return false;
                }
                DataStructItem[] items = snapshot[structIndex];
                if (items == null || configuredIndex < 0 || configuredIndex >= items.Length)
                {
                    error = $"数据项索引无效:{configuredIndex}";
                    return false;
                }
                resolvedIndex = configuredIndex;
                return true;
            }

            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    error = $"结构体索引无效:{structIndex}";
                    return false;
                }
                DataStruct dataStruct = dataStructs[structIndex];
                NormalizeStruct(dataStruct);
                if (string.IsNullOrWhiteSpace(configuredName))
                {
                    error = "数据项名称不能为空";
                    return false;
                }
                for (int i = 0; i < dataStruct.dataStructItems.Count; i++)
                {
                    if (string.Equals(dataStruct.dataStructItems[i]?.Name, configuredName, StringComparison.Ordinal))
                    {
                        resolvedIndex = i;
                        return true;
                    }
                }
                error = $"数据项不存在:{configuredName}";
                return false;
            }
        }

        public bool TryResolveFieldIndex(int structIndex, int itemIndex, bool useName,
            int configuredIndex, string configuredName, out int resolvedIndex, out string error)
        {
            resolvedIndex = -1;
            error = string.Empty;
            if (!TryGetRuntimeItem(structIndex, itemIndex, out DataStructItem runtimeItem))
            {
                error = $"数据项不存在:结构{structIndex},项{itemIndex}";
                return false;
            }
            DataStructRuntimeState state = runtimeItem.RuntimeState;
            if (state == null)
            {
                error = $"数据项运行描述不存在:结构{structIndex},项{itemIndex}";
                return false;
            }
            if (!useName)
            {
                if (!state.TryGetField(configuredIndex, out _))
                {
                    error = $"字段索引无效:{configuredIndex}";
                    return false;
                }
                resolvedIndex = configuredIndex;
                return true;
            }

            if (string.IsNullOrWhiteSpace(configuredName))
            {
                error = "字段名称不能为空";
                return false;
            }
            List<int> matches = state.Fields
                .Where(field => string.Equals(
                    field.Name, configuredName, StringComparison.Ordinal))
                .Select(field => field.Index)
                .ToList();
            if (matches.Count == 1)
            {
                resolvedIndex = matches[0];
                return true;
            }
            error = matches.Count == 0
                ? $"字段不存在:{configuredName}"
                : $"字段名称重复，无法按名称寻址:{configuredName}";
            return false;
        }

        public bool AddStruct(string name, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "名称不能为空";
                return false;
            }
            lock (dataLock)
            {
                if (dataStructs.Any(ds => ds.Name == name))
                {
                    error = "名称重复";
                    return false;
                }
                dataStructs.Add(new DataStruct { Name = name });
                structVersions.Add(0);
                RebuildRuntimeItemsNoLock();
                MarkChangedNoLock();
                MarkStructChangedNoLock(structVersions.Count - 1);
                return true;
            }
        }

        public bool RenameStruct(int index, string newName, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = "名称不能为空";
                return false;
            }
            lock (dataLock)
            {
                if (index < 0 || index >= dataStructs.Count)
                {
                    error = "结构体索引无效";
                    return false;
                }
                int existIndex = dataStructs.FindIndex(ds => ds.Name == newName);
                if (existIndex >= 0 && existIndex != index)
                {
                    error = "名称重复";
                    return false;
                }
                dataStructs[index].Name = newName;
                MarkChangedNoLock();
                MarkStructChangedNoLock(index);
                return true;
            }
        }

        public bool RemoveStructAt(int index, out string error)
        {
            error = string.Empty;
            lock (dataLock)
            {
                if (index < 0 || index >= dataStructs.Count)
                {
                    error = "结构体索引无效";
                    return false;
                }
                dataStructs.RemoveAt(index);
                if (index >= 0 && index < structVersions.Count)
                {
                    structVersions.RemoveAt(index);
                }
                RebuildRuntimeItemsNoLock();
                MarkChangedNoLock();
                return true;
            }
        }

        public bool CreateItem(int structIndex, string itemName, int insertIndex, out int itemIndex, out string error)
        {
            itemIndex = -1;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(itemName))
            {
                error = "名称不能为空";
                return false;
            }
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    error = "结构体索引无效";
                    return false;
                }
                DataStruct dataStruct = dataStructs[structIndex];
                NormalizeStruct(dataStruct);
                if (dataStruct.dataStructItems.Any(item => item != null && item.Name == itemName))
                {
                    error = "名称重复";
                    return false;
                }
                DataStructItem item = new DataStructItem
                {
                    Name = itemName,
                    FieldNames = new Dictionary<int, string>(),
                    FieldTypes = new Dictionary<int, DataStructValueType>(),
                    str = new Dictionary<int, string>(),
                    num = new Dictionary<int, double>()
                };
                if (insertIndex < 0 || insertIndex > dataStruct.dataStructItems.Count)
                {
                    error = $"插入索引超出范围：{insertIndex}，允许范围0..{dataStruct.dataStructItems.Count}";
                    return false;
                }
                dataStruct.dataStructItems.Insert(insertIndex, item);
                itemIndex = insertIndex;
                RebuildRuntimeItemsNoLock();
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool RenameItem(int structIndex, int itemIndex, string newName, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = "名称不能为空";
                return false;
            }
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    error = "结构体索引无效";
                    return false;
                }
                DataStruct dataStruct = dataStructs[structIndex];
                NormalizeStruct(dataStruct);
                if (itemIndex < 0 || itemIndex >= dataStruct.dataStructItems.Count)
                {
                    error = "数据项索引无效";
                    return false;
                }
                for (int i = 0; i < dataStruct.dataStructItems.Count; i++)
                {
                    if (i == itemIndex)
                    {
                        continue;
                    }
                    DataStructItem exist = dataStruct.dataStructItems[i];
                    if (exist != null && exist.Name == newName)
                    {
                        error = "名称重复";
                        return false;
                    }
                }
                DataStructItem item = dataStruct.dataStructItems[itemIndex];
                if (item == null)
                {
                    error = "数据项为空";
                    return false;
                }
                item.Name = newName;
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool DeleteItem(int structIndex, int itemIndex, out string error)
        {
            error = string.Empty;
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    error = "结构体索引无效";
                    return false;
                }
                DataStruct dataStruct = dataStructs[structIndex];
                NormalizeStruct(dataStruct);
                if (itemIndex < 0 || itemIndex >= dataStruct.dataStructItems.Count)
                {
                    error = "数据项索引无效";
                    return false;
                }
                dataStruct.dataStructItems.RemoveAt(itemIndex);
                RebuildRuntimeItemsNoLock();
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool AddField(int structIndex, int itemIndex, string fieldName, DataStructValueType fieldType, string value, int index, out int actualIndex, out string error)
        {
            actualIndex = -1;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                error = "字段名称不能为空";
                return false;
            }
            if (value == null)
            {
                error = "字段值不能为空";
                return false;
            }
            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    error = "数据项不存在";
                    return false;
                }
                NormalizeItem(item);
                lock (item.SyncRoot)
                {
                    using (LockRuntimeFields(item))
                    {
                        SyncRuntimeValuesToModelNoLock(item);
                        HashSet<int> used = new HashSet<int>();
                        used.UnionWith(item.FieldNames.Keys);
                        used.UnionWith(item.FieldTypes.Keys);
                        used.UnionWith(item.str.Keys);
                        used.UnionWith(item.num.Keys);

                        if (index < 0)
                        {
                            int candidate = 0;
                            while (used.Contains(candidate))
                            {
                                candidate++;
                            }
                            actualIndex = candidate;
                        }
                        else
                        {
                            if (used.Contains(index))
                            {
                                error = "字段索引已存在";
                                return false;
                            }
                            actualIndex = index;
                        }

                        if (fieldType == DataStructValueType.Number)
                        {
                            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                                || double.IsNaN(number) || double.IsInfinity(number))
                            {
                                error = "数值格式错误";
                                return false;
                            }
                            item.num[actualIndex] = number;
                            item.str.Remove(actualIndex);
                        }
                        else
                        {
                            item.str[actualIndex] = value;
                            item.num.Remove(actualIndex);
                        }
                        item.FieldNames[actualIndex] = fieldName.Trim();
                        item.FieldTypes[actualIndex] = fieldType;
                        PublishRuntimeStateNoLock(item);
                    }
                }

                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool RenameField(int structIndex, int itemIndex, int fieldIndex, string newFieldName, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(newFieldName))
            {
                error = "字段名称不能为空";
                return false;
            }
            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    error = "数据项不存在";
                    return false;
                }
                NormalizeItem(item);
                lock (item.SyncRoot)
                {
                    using (LockRuntimeFields(item))
                    {
                        SyncRuntimeValuesToModelNoLock(item);
                        if (!item.FieldNames.ContainsKey(fieldIndex))
                        {
                            error = "字段不存在";
                            return false;
                        }
                        item.FieldNames[fieldIndex] = newFieldName.Trim();
                        PublishRuntimeStateNoLock(item);
                    }
                }
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool SetFieldType(int structIndex, int itemIndex, int fieldIndex, DataStructValueType newType, out string message)
        {
            message = string.Empty;
            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    message = "数据项不存在";
                    return false;
                }
                NormalizeItem(item);
                lock (item.SyncRoot)
                {
                    using (LockRuntimeFields(item))
                    {
                        SyncRuntimeValuesToModelNoLock(item);
                        if (!item.FieldTypes.TryGetValue(fieldIndex, out DataStructValueType oldType))
                        {
                            message = "字段不存在";
                            return false;
                        }
                        if (oldType == newType)
                        {
                            return true;
                        }

                        if (newType == DataStructValueType.Text)
                        {
                            if (item.num.TryGetValue(fieldIndex, out double number))
                            {
                                item.str[fieldIndex] = number.ToString("G17", CultureInfo.InvariantCulture);
                                item.num.Remove(fieldIndex);
                            }
                        }
                        else
                        {
                            if (item.str.TryGetValue(fieldIndex, out string strValue))
                            {
                                if (double.TryParse(strValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                                {
                                    item.num[fieldIndex] = number;
                                }
                                else
                                {
                                    item.num.Remove(fieldIndex);
                                    message = "文本无法转换为数值，已清空字段值";
                                }
                                item.str.Remove(fieldIndex);
                            }
                        }

                        item.FieldTypes[fieldIndex] = newType;
                        PublishRuntimeStateNoLock(item);
                    }
                }
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool SetFieldValue(int structIndex, int itemIndex, int fieldIndex, DataStructValueType type, string value, out string error)
        {
            error = string.Empty;
            if (value == null)
            {
                error = "字段值不能为空";
                return false;
            }
            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    error = "数据项不存在";
                    return false;
                }
                NormalizeItem(item);
                lock (item.SyncRoot)
                {
                    using (LockRuntimeFields(item))
                    {
                        SyncRuntimeValuesToModelNoLock(item);
                        if (!item.FieldTypes.TryGetValue(fieldIndex, out DataStructValueType existType))
                        {
                            error = "字段不存在";
                            return false;
                        }
                        if (existType != type)
                        {
                            error = "字段类型不一致";
                            return false;
                        }

                        if (type == DataStructValueType.Number)
                        {
                            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                                || double.IsNaN(number) || double.IsInfinity(number))
                            {
                                error = "数值格式错误";
                                return false;
                            }
                            item.num[fieldIndex] = number;
                            item.str.Remove(fieldIndex);
                        }
                        else
                        {
                            item.str[fieldIndex] = value;
                            item.num.Remove(fieldIndex);
                        }
                        PublishRuntimeStateNoLock(item);
                    }
                }
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TryUpdateField(int structIndex, int itemIndex, int fieldIndex,
            string fieldName, DataStructValueType type, string value, out string error)
        {
            error = string.Empty;
            fieldName = fieldName?.Trim();
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                error = "字段名称不能为空";
                return false;
            }
            if (value == null)
            {
                error = "字段值不能为空";
                return false;
            }

            double number = 0;
            if (type == DataStructValueType.Number
                && (!double.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out number)
                    || double.IsNaN(number) || double.IsInfinity(number)))
            {
                error = "数值格式错误";
                return false;
            }

            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    error = "数据项不存在";
                    return false;
                }
                NormalizeItem(item);
                lock (item.SyncRoot)
                {
                    using (LockRuntimeFields(item))
                    {
                        SyncRuntimeValuesToModelNoLock(item);
                        if (!item.FieldNames.ContainsKey(fieldIndex)
                            || !item.FieldTypes.ContainsKey(fieldIndex))
                        {
                            error = "字段不存在";
                            return false;
                        }
                        item.FieldNames[fieldIndex] = fieldName;
                        item.FieldTypes[fieldIndex] = type;
                        if (type == DataStructValueType.Number)
                        {
                            item.num[fieldIndex] = number;
                            item.str.Remove(fieldIndex);
                        }
                        else
                        {
                            item.str[fieldIndex] = value;
                            item.num.Remove(fieldIndex);
                        }
                        PublishRuntimeStateNoLock(item);
                    }
                }
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool RemoveField(int structIndex, int itemIndex, int fieldIndex, out string error)
        {
            error = string.Empty;
            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    error = "数据项不存在";
                    return false;
                }
                NormalizeItem(item);
                lock (item.SyncRoot)
                {
                    using (LockRuntimeFields(item))
                    {
                        SyncRuntimeValuesToModelNoLock(item);
                        if (!item.FieldNames.ContainsKey(fieldIndex) && !item.FieldTypes.ContainsKey(fieldIndex))
                        {
                            error = "字段不存在";
                            return false;
                        }
                        item.FieldNames.Remove(fieldIndex);
                        item.FieldTypes.Remove(fieldIndex);
                        item.str.Remove(fieldIndex);
                        item.num.Remove(fieldIndex);
                        PublishRuntimeStateNoLock(item);
                    }
                }
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TrySetItemValueByIndex(int structIndex, int itemIndex, int ValueIndex, string value)
        {
            return TrySetItemValueByIndex(structIndex, itemIndex, ValueIndex, value, null);
        }

        public bool TrySetItemValueByIndex(int structIndex, int itemIndex, int ValueIndex, string value, DataStructValueType? valueType)
        {
            if (value == null)
            {
                return false;
            }
            if (!TryGetRuntimeItem(structIndex, itemIndex, out DataStructItem item))
            {
                return false;
            }
            while (true)
            {
                DataStructRuntimeState state = item.RuntimeState;
                if (state == null
                    || !state.TryGetField(ValueIndex, out DataStructFieldSlot field)
                    || valueType.HasValue && valueType.Value != field.ValueType)
                {
                    return false;
                }
                if (field.ValueType == DataStructValueType.Number)
                {
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    {
                        return false;
                    }
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        return false;
                    }
                    lock (field.SyncRoot)
                    {
                        if (!ReferenceEquals(item.RuntimeState, state))
                        {
                            continue;
                        }
                        if (!IsCurrentRuntimeItem(
                                structIndex, itemIndex, item))
                        {
                            return false;
                        }
                        field.WriteNumber(number);
                    }
                }
                else
                {
                    lock (field.SyncRoot)
                    {
                        if (!ReferenceEquals(item.RuntimeState, state))
                        {
                            continue;
                        }
                        if (!IsCurrentRuntimeItem(
                                structIndex, itemIndex, item))
                        {
                            return false;
                        }
                        field.WriteText(value);
                    }
                }
                return true;
            }
        }

        public bool TrySetItemValuesByIndex(int structIndex, int itemIndex,
            IReadOnlyList<DataStructFieldValueUpdate> updates, out string error)
        {
            error = string.Empty;
            if (updates == null)
            {
                error = "字段更新集合为空";
                return false;
            }
            if (!TryGetRuntimeItem(structIndex, itemIndex, out DataStructItem item))
            {
                error = $"数据项不存在:结构{structIndex},项{itemIndex}";
                return false;
            }

            while (true)
            {
                if (updates.Count == 1)
                {
                    DataStructFieldValueUpdate update = updates[0];
                    if (update == null)
                    {
                        error = "字段更新项为空";
                        return false;
                    }
                    DataStructRuntimeState singleState = item.RuntimeState;
                    if (update.Value == null
                        || singleState == null
                        || !singleState.TryGetField(
                            update.FieldIndex, out DataStructFieldSlot singleField))
                    {
                        error = $"字段不存在或值为空:{update.FieldIndex}";
                        return false;
                    }
                    if (update.ExpectedType.HasValue
                        && update.ExpectedType.Value != singleField.ValueType)
                    {
                        error = $"字段类型不一致:{update.FieldIndex}";
                        return false;
                    }
                    if (singleField.ValueType == DataStructValueType.Number)
                    {
                        if (!double.TryParse(update.Value, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double number)
                            || double.IsNaN(number) || double.IsInfinity(number))
                        {
                            error = $"字段数值格式错误:{update.FieldIndex}";
                            return false;
                        }
                        lock (singleField.SyncRoot)
                        {
                            if (!ReferenceEquals(item.RuntimeState, singleState))
                            {
                                continue;
                            }
                            if (!IsCurrentRuntimeItem(
                                    structIndex, itemIndex, item))
                            {
                                error = $"数据项运行代际已变更:结构{structIndex},项{itemIndex}";
                                return false;
                            }
                            singleField.WriteNumber(number);
                        }
                    }
                    else
                    {
                        lock (singleField.SyncRoot)
                        {
                            if (!ReferenceEquals(item.RuntimeState, singleState))
                            {
                                continue;
                            }
                            if (!IsCurrentRuntimeItem(
                                    structIndex, itemIndex, item))
                            {
                                error = $"数据项运行代际已变更:结构{structIndex},项{itemIndex}";
                                return false;
                            }
                            singleField.WriteText(update.Value);
                        }
                    }
                    return true;
                }

                var unique = new HashSet<int>();
                var prepared = new List<PreparedFieldWrite>(updates.Count);
                DataStructRuntimeState state = item.RuntimeState;
                if (state == null)
                {
                    error = $"数据项运行描述不存在:结构{structIndex},项{itemIndex}";
                    return false;
                }
                foreach (DataStructFieldValueUpdate update in updates)
                {
                    if (update == null)
                    {
                        error = "字段更新项为空";
                        return false;
                    }
                    if (!unique.Add(update.FieldIndex))
                    {
                        error = $"字段索引重复:{update.FieldIndex}";
                        return false;
                    }
                    if (update.Value == null
                        || !state.TryGetField(
                            update.FieldIndex, out DataStructFieldSlot field))
                    {
                        error = $"字段不存在或值为空:{update.FieldIndex}";
                        return false;
                    }
                    if (update.ExpectedType.HasValue
                        && update.ExpectedType.Value != field.ValueType)
                    {
                        error = $"字段类型不一致:{update.FieldIndex}";
                        return false;
                    }
                    var preparedWrite = new PreparedFieldWrite
                    {
                        Field = field,
                        Text = update.Value
                    };
                    if (field.ValueType == DataStructValueType.Number)
                    {
                        if (!double.TryParse(update.Value, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double number)
                            || double.IsNaN(number) || double.IsInfinity(number))
                        {
                            error = $"字段数值格式错误:{update.FieldIndex}";
                            return false;
                        }
                        preparedWrite.Number = number;
                    }
                    prepared.Add(preparedWrite);
                }

                List<DataStructFieldSlot> lockedFields =
                    GetOrderedUniqueFields(prepared.Select(write => write.Field));
                EnterFieldLocks(lockedFields);
                try
                {
                    if (!ReferenceEquals(item.RuntimeState, state))
                    {
                        continue;
                    }
                    if (!IsCurrentRuntimeItem(
                            structIndex, itemIndex, item))
                    {
                        error = $"数据项运行代际已变更:结构{structIndex},项{itemIndex}";
                        return false;
                    }
                    foreach (PreparedFieldWrite write in prepared)
                    {
                        if (write.Field.ValueType == DataStructValueType.Number)
                        {
                            write.Field.WriteNumber(write.Number);
                        }
                        else
                        {
                            write.Field.WriteText(write.Text);
                        }
                    }
                }
                finally
                {
                    ExitFieldLocks(lockedFields);
                }
                return true;
            }
        }

        public bool TryGetItemValuesByIndex(int structIndex, int itemIndex,
            IReadOnlyList<int> fieldIndexes,
            out List<DataStructFieldValueSnapshot> snapshots, out string error)
        {
            snapshots = null;
            error = string.Empty;
            if (fieldIndexes == null)
            {
                error = "字段索引集合为空";
                return false;
            }
            if (!TryGetRuntimeItem(structIndex, itemIndex, out DataStructItem item))
            {
                error = $"数据项不存在:结构{structIndex},项{itemIndex}";
                return false;
            }

            while (true)
            {
                DataStructRuntimeState state = item.RuntimeState;
                if (state == null)
                {
                    error = $"数据项运行描述不存在:结构{structIndex},项{itemIndex}";
                    return false;
                }
                var requestedFields =
                    new List<DataStructFieldSlot>(fieldIndexes.Count);
                foreach (int fieldIndex in fieldIndexes)
                {
                    if (!state.TryGetField(fieldIndex, out DataStructFieldSlot field))
                    {
                        error = $"字段不存在:{fieldIndex}";
                        return false;
                    }
                    requestedFields.Add(field);
                }
                List<DataStructFieldSlot> lockedFields =
                    GetOrderedUniqueFields(requestedFields);
                EnterFieldLocks(lockedFields);
                var result = new List<DataStructFieldValueSnapshot>(fieldIndexes.Count);
                try
                {
                    if (!ReferenceEquals(item.RuntimeState, state))
                    {
                        continue;
                    }
                    if (!IsCurrentRuntimeItem(
                            structIndex, itemIndex, item))
                    {
                        error = $"数据项运行代际已变更:结构{structIndex},项{itemIndex}";
                        return false;
                    }
                    foreach (DataStructFieldSlot field in requestedFields)
                    {
                        if (!field.HasValue)
                        {
                            error = field.ValueType == DataStructValueType.Number
                                ? $"数值字段没有值:{field.Index}"
                                : $"文本字段没有值:{field.Index}";
                            return false;
                        }
                        object value = field.ValueType == DataStructValueType.Number
                            ? (object)field.ReadNumber()
                            : field.ReadText();
                        result.Add(new DataStructFieldValueSnapshot
                        {
                            FieldIndex = field.Index,
                            Value = value,
                            ValueType = field.ValueType
                        });
                    }
                    snapshots = result;
                    return true;
                }
                finally
                {
                    ExitFieldLocks(lockedFields);
                }
            }
        }

        public bool TryGetItemValueByIndex(int structIndex, int itemIndex, int ValueIndex, out object value)
        {
            return TryGetItemValueByIndex(
                structIndex, itemIndex, ValueIndex, out value, out _);
        }

        public bool TryGetItemValueByIndex(int structIndex, int itemIndex, int ValueIndex, out object value, out DataStructValueType valueType)
        {
            value = null;
            valueType = DataStructValueType.Text;
            if (!TryGetRuntimeItem(structIndex, itemIndex, out DataStructItem item))
            {
                return false;
            }
            DataStructRuntimeState state = item.RuntimeState;
            if (state == null
                || !state.TryGetField(ValueIndex, out DataStructFieldSlot field))
            {
                return false;
            }
            valueType = field.ValueType;
            if (!field.HasValue)
            {
                return false;
            }
            value = field.ValueType == DataStructValueType.Number
                ? (object)field.ReadNumber()
                : field.ReadText();
            if (IsCurrentRuntimeItem(structIndex, itemIndex, item))
            {
                return true;
            }
            value = null;
            valueType = DataStructValueType.Text;
            return false;
        }

        public int GetItemValueCount(int structIndex, int itemIndex)
        {
            if (!TryGetRuntimeItem(structIndex, itemIndex, out DataStructItem item))
            {
                return 0;
            }
            return item.RuntimeState?.SortedFieldIndexes.Length ?? 0;
        }

        public int GetItemCount(int structIndex)
        {
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    return 0;
                }
                return dataStructs[structIndex].dataStructItems.Count;
            }
        }

        public bool TryInsertItem(int structIndex, int itemIndex, DataStructItem item)
        {
            if (item == null)
            {
                return false;
            }
            if (!ValidateItem(item, out _))
            {
                return false;
            }
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    return false;
                }
                NormalizeItem(item);
                DataStruct dataStruct = dataStructs[structIndex];
                NormalizeStruct(dataStruct);
                if (itemIndex < 0 || itemIndex > dataStruct.dataStructItems.Count)
                {
                    return false;
                }
                dataStruct.dataStructItems.Insert(itemIndex, item);
                RebuildRuntimeItemsNoLock();
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TryCopyItemAll(int sourceStructIndex, int sourceItemIndex, int targetStructIndex, int targetItemIndex)
        {
            lock (dataLock)
            {
                if (!TryGetItemNoLock(sourceStructIndex, sourceItemIndex, out DataStructItem sourceItem))
                {
                    return false;
                }
                if (targetStructIndex < 0 || targetStructIndex >= dataStructs.Count)
                {
                    return false;
                }
                DataStructItem copy;
                lock (sourceItem.SyncRoot)
                {
                    using (LockRuntimeFields(sourceItem))
                    {
                        SyncRuntimeValuesToModelNoLock(sourceItem);
                        copy = sourceItem.Clone();
                    }
                }
                DataStruct targetStruct = dataStructs[targetStructIndex];
                NormalizeStruct(targetStruct);
                if (!TryInsertOrReplaceItem(targetStruct, targetItemIndex, copy))
                {
                    return false;
                }
                RebuildRuntimeItemsNoLock();
                MarkChangedNoLock();
                MarkStructChangedNoLock(targetStructIndex);
                return true;
            }
        }

        public bool TryRemoveItemAt(int structIndex, int itemIndex)
        {
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    return false;
                }
                DataStruct dataStruct = dataStructs[structIndex];
                if (itemIndex < 0 || itemIndex >= dataStruct.dataStructItems.Count)
                {
                    return false;
                }
                dataStruct.dataStructItems.RemoveAt(itemIndex);
                RebuildRuntimeItemsNoLock();
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TryFindItemByName(int structIndex, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    return false;
                }
                DataStructItem dst = dataStructs[structIndex].dataStructItems.FirstOrDefault(item => item.Name == key);
                if (dst == null)
                {
                    return false;
                }
                value = dst.Name;
                return true;
            }
        }

        public bool TryFindItemByStringValue(int structIndex, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            DataStructItem[][] snapshot = runtimeItems;
            if (structIndex < 0 || structIndex >= snapshot.Length)
            {
                return false;
            }
            foreach (DataStructItem item in snapshot[structIndex])
            {
                DataStructRuntimeState state = item?.RuntimeState;
                if (state == null)
                {
                    continue;
                }
                foreach (DataStructFieldSlot field in state.Fields)
                {
                    if (field.ValueType == DataStructValueType.Text
                        && field.HasValue)
                    {
                        string candidate = field.ReadText();
                        if (candidate == key)
                        {
                            value = candidate;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public bool TryFindItemByNumberValue(int structIndex, double key, out double value)
        {
            value = 0;
            DataStructItem[][] snapshot = runtimeItems;
            if (structIndex < 0 || structIndex >= snapshot.Length)
            {
                return false;
            }
            foreach (DataStructItem item in snapshot[structIndex])
            {
                DataStructRuntimeState state = item?.RuntimeState;
                if (state == null)
                {
                    continue;
                }
                foreach (DataStructFieldSlot field in state.Fields)
                {
                    if (field.ValueType == DataStructValueType.Number
                        && field.HasValue)
                    {
                        double candidate = field.ReadNumber();
                        if (candidate == key)
                        {
                            value = candidate;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool TryGetItemNoLock(int structIndex, int itemIndex, out DataStructItem item)
        {
            item = null;
            if (structIndex < 0 || structIndex >= dataStructs.Count)
            {
                return false;
            }
            DataStruct dataStruct = dataStructs[structIndex];
            if (dataStruct?.dataStructItems == null || itemIndex < 0 || itemIndex >= dataStruct.dataStructItems.Count)
            {
                return false;
            }
            item = dataStruct.dataStructItems[itemIndex];
            if (item == null)
            {
                return false;
            }
            return true;
        }

        public List<int> GetItemValueIndexes(int structIndex, int itemIndex)
        {
            if (!TryGetRuntimeItem(structIndex, itemIndex, out DataStructItem item))
            {
                return new List<int>();
            }
            int[] indexes = item.RuntimeState?.SortedFieldIndexes;
            return indexes == null
                ? new List<int>()
                : new List<int>(indexes);
        }

        private bool TryGetRuntimeItem(int structIndex, int itemIndex, out DataStructItem item)
        {
            item = null;
            DataStructItem[][] snapshot = runtimeItems;
            if (structIndex < 0 || structIndex >= snapshot.Length)
            {
                return false;
            }
            DataStructItem[] items = snapshot[structIndex];
            if (items == null || itemIndex < 0 || itemIndex >= items.Length)
            {
                return false;
            }
            item = items[itemIndex];
            return item != null;
        }

        private bool IsCurrentRuntimeItem(
            int structIndex,
            int itemIndex,
            DataStructItem expected)
        {
            DataStructItem[][] snapshot = runtimeItems;
            return structIndex >= 0
                && structIndex < snapshot.Length
                && snapshot[structIndex] != null
                && itemIndex >= 0
                && itemIndex < snapshot[structIndex].Length
                && ReferenceEquals(snapshot[structIndex][itemIndex], expected);
        }

        private void RebuildRuntimeItemsNoLock()
        {
            DataStructItem[][] previous = runtimeItems;
            var next = new DataStructItem[dataStructs.Count][];
            for (int i = 0; i < dataStructs.Count; i++)
            {
                List<DataStructItem> items = dataStructs[i]?.dataStructItems;
                next[i] = items?.ToArray() ?? Array.Empty<DataStructItem>();
                foreach (DataStructItem item in next[i])
                {
                    if (item == null)
                    {
                        continue;
                    }
                    NormalizeItem(item);
                    lock (item.SyncRoot)
                    {
                        using (LockRuntimeFields(item))
                        {
                            SyncRuntimeValuesToModelNoLock(item);
                            PublishRuntimeStateNoLock(item);
                        }
                    }
                }
            }
            var retainedItems = new HashSet<DataStructItem>(
                next.SelectMany(items => items)
                    .Where(item => item != null));
            foreach (DataStructItem retiredItem in previous
                .SelectMany(items => items ?? Array.Empty<DataStructItem>())
                .Where(item => item != null && !retainedItems.Contains(item)))
            {
                lock (retiredItem.SyncRoot)
                {
                    using (LockRuntimeFields(retiredItem))
                    {
                        retiredItem.PublishRuntimeState(
                            new DataStructRuntimeState(
                                new Dictionary<int, DataStructFieldSlot>()));
                    }
                }
            }
            runtimeItems = next;
        }

        private void MarkChangedNoLock()
        {
            if (version == int.MaxValue)
            {
                version = 0;
            }
            else
            {
                version++;
            }
        }

        private void MarkStructChangedNoLock(int structIndex)
        {
            if (structIndex < 0 || structIndex >= structVersions.Count)
            {
                return;
            }
            if (structVersions[structIndex] == int.MaxValue)
            {
                structVersions[structIndex] = 0;
            }
            else
            {
                structVersions[structIndex]++;
            }
        }

        private static bool TryInsertOrReplaceItem(DataStruct dataStruct, int itemIndex, DataStructItem dataStructItem)
        {
            if (dataStruct?.dataStructItems == null || dataStructItem == null
                || itemIndex < 0 || itemIndex > dataStruct.dataStructItems.Count)
            {
                return false;
            }
            if (itemIndex == dataStruct.dataStructItems.Count)
            {
                dataStruct.dataStructItems.Add(dataStructItem);
            }
            else
            {
                dataStruct.dataStructItems[itemIndex] = dataStructItem;
            }
            return true;
        }

    }
}
