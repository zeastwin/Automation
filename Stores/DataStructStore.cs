using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Automation
{
    public enum DataStructValueType
    {
        Number,
        Text
    }

    public class DataStructStore
    {
        private readonly object dataLock = new object();
        private readonly List<DataStruct> dataStructs = new List<DataStruct>();
        private readonly List<int> structVersions = new List<int>();
        private int version = 0;

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
                Save(configPath);
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                List<DataStruct> temp = JsonConvert.DeserializeObject<List<DataStruct>>(json, settings);
                if (!ValidateLoadedData(temp, out string error))
                {
                    MessageBox.Show(error);
                    Reset();
                    Save(configPath);
                    return false;
                }
                LoadFromList(temp);
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                Reset();
                Save(configPath);
                return false;
            }
        }

        public bool Save(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "DataStruct.json");
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            string output = JsonConvert.SerializeObject(BuildSaveData(), settings);
            File.WriteAllText(filePath, output);
            return true;
        }

        private void Reset()
        {
            lock (dataLock)
            {
                dataStructs.Clear();
                structVersions.Clear();
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
                MarkChangedNoLock();
            }
        }

        private List<DataStruct> BuildSaveData()
        {
            lock (dataLock)
            {
                return dataStructs.Select(dataStruct => (DataStruct)dataStruct.Clone()).ToList();
            }
        }

        private static bool ValidateLoadedData(List<DataStruct> source, out string error)
        {
            error = "DataStruct.json 格式错误，请重新生成";
            if (source == null)
            {
                return false;
            }

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
                if (dataStruct.dataStructItems == null)
                {
                    error = "数据结构项列表为空，请重新生成";
                    return false;
                }

                for (int j = 0; j < dataStruct.dataStructItems.Count; j++)
                {
                    DataStructItem item = dataStruct.dataStructItems[j];
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
                return dataStructs.Select(dataStruct => (DataStruct)dataStruct.Clone()).ToList();
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
                dataStruct = (DataStruct)target.Clone();
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
                dataStruct = (DataStruct)dataStructs[index].Clone();
                return true;
            }
        }

        public bool TryGetStructNameByIndex(int index, out string name)
        {
            name = null;
            lock (dataLock)
            {
                if (index < 0 || index >= dataStructs.Count)
                {
                    return false;
                }
                name = dataStructs[index].Name;
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

        public bool TryGetStructVersionByName(string name, out int structVersion)
        {
            structVersion = -1;
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
                        if (i >= structVersions.Count)
                        {
                            return false;
                        }
                        structVersion = structVersions[i];
                        return true;
                    }
                }
            }
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
                    insertIndex = dataStruct.dataStructItems.Count;
                }
                dataStruct.dataStructItems.Insert(insertIndex, item);
                itemIndex = insertIndex;
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
                    if (!double.TryParse(value, out double number))
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
                if (!item.FieldNames.ContainsKey(fieldIndex))
                {
                    error = "字段不存在";
                    return false;
                }
                item.FieldNames[fieldIndex] = newFieldName.Trim();
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
                        item.str[fieldIndex] = number.ToString("0.######");
                        item.num.Remove(fieldIndex);
                    }
                }
                else
                {
                    if (item.str.TryGetValue(fieldIndex, out string strValue))
                    {
                        if (double.TryParse(strValue, out double number))
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
                    if (!double.TryParse(value, out double number))
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
                if (!item.FieldNames.ContainsKey(fieldIndex) && !item.FieldTypes.ContainsKey(fieldIndex))
                {
                    error = "字段不存在";
                    return false;
                }
                item.FieldNames.Remove(fieldIndex);
                item.FieldTypes.Remove(fieldIndex);
                item.str.Remove(fieldIndex);
                item.num.Remove(fieldIndex);
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TrySetStructItemByName(string structName, int itemIndex, string itemName, List<string> strings, List<double> doubles, string param)
        {
            if (string.IsNullOrEmpty(structName))
            {
                return false;
            }
            lock (dataLock)
            {
                if (!TryGetStructByNameNoLock(structName, out DataStruct dataStruct, out int structIndex))
                {
                    return false;
                }
                if (!TryBuildItem(itemName, strings, doubles, param, out DataStructItem dataStructItem))
                {
                    return false;
                }
                InsertOrReplaceItem(dataStruct, itemIndex, dataStructItem);
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TrySetStructItemByIndex(int structIndex, int itemIndex, List<string> strings, List<double> doubles, string param)
        {
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    return false;
                }
                DataStruct dataStruct = dataStructs[structIndex];
                NormalizeStruct(dataStruct);
                if (!TryBuildItem(string.Empty, strings, doubles, param, out DataStructItem dataStructItem))
                {
                    return false;
                }
                InsertOrReplaceItem(dataStruct, itemIndex, dataStructItem);
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TrySetItemValueByIndex(int structIndex, int itemIndex, int valueIndex, string value)
        {
            return TrySetItemValueByIndex(structIndex, itemIndex, valueIndex, value, null);
        }

        public bool TrySetItemValueByIndex(int structIndex, int itemIndex, int valueIndex, string value, DataStructValueType? valueType)
        {
            if (value == null)
            {
                return false;
            }
            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    return false;
                }
                NormalizeItem(item);

                if (valueType.HasValue)
                {
                    if (valueType.Value == DataStructValueType.Number)
                    {
                        if (!double.TryParse(value, out double number))
                        {
                            return false;
                        }
                        item.num[valueIndex] = number;
                        item.str.Remove(valueIndex);
                    }
                    else
                    {
                        item.str[valueIndex] = value;
                        item.num.Remove(valueIndex);
                    }

                    item.FieldTypes[valueIndex] = valueType.Value;
                    if (!item.FieldNames.ContainsKey(valueIndex) || string.IsNullOrWhiteSpace(item.FieldNames[valueIndex]))
                    {
                        item.FieldNames[valueIndex] = $"字段{valueIndex}";
                    }

                    MarkChangedNoLock();
                    MarkStructChangedNoLock(structIndex);
                    return true;
                }

                if (item.FieldTypes.TryGetValue(valueIndex, out DataStructValueType existType))
                {
                    if (existType == DataStructValueType.Number)
                    {
                        if (!double.TryParse(value, out double number))
                        {
                            return false;
                        }
                        item.num[valueIndex] = number;
                        item.str.Remove(valueIndex);
                    }
                    else
                    {
                        item.str[valueIndex] = value;
                        item.num.Remove(valueIndex);
                    }
                    MarkChangedNoLock();
                    MarkStructChangedNoLock(structIndex);
                    return true;
                }

                if (item.num.ContainsKey(valueIndex))
                {
                    if (!double.TryParse(value, out double number))
                    {
                        return false;
                    }
                    item.num[valueIndex] = number;
                    item.FieldTypes[valueIndex] = DataStructValueType.Number;
                    if (!item.FieldNames.ContainsKey(valueIndex) || string.IsNullOrWhiteSpace(item.FieldNames[valueIndex]))
                    {
                        item.FieldNames[valueIndex] = $"字段{valueIndex}";
                    }
                    MarkChangedNoLock();
                    MarkStructChangedNoLock(structIndex);
                    return true;
                }

                if (item.str.ContainsKey(valueIndex))
                {
                    item.str[valueIndex] = value;
                    item.FieldTypes[valueIndex] = DataStructValueType.Text;
                    if (!item.FieldNames.ContainsKey(valueIndex) || string.IsNullOrWhiteSpace(item.FieldNames[valueIndex]))
                    {
                        item.FieldNames[valueIndex] = $"字段{valueIndex}";
                    }
                    MarkChangedNoLock();
                    MarkStructChangedNoLock(structIndex);
                    return true;
                }

                item.str[valueIndex] = value;
                item.FieldTypes[valueIndex] = DataStructValueType.Text;
                if (!item.FieldNames.ContainsKey(valueIndex) || string.IsNullOrWhiteSpace(item.FieldNames[valueIndex]))
                {
                    item.FieldNames[valueIndex] = $"字段{valueIndex}";
                }
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TryGetItemValueByIndex(int structIndex, int itemIndex, int valueIndex, out object value)
        {
            value = null;
            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    return false;
                }
                if (item.FieldTypes.TryGetValue(valueIndex, out DataStructValueType type))
                {
                    if (type == DataStructValueType.Number)
                    {
                        if (item.num.TryGetValue(valueIndex, out double number))
                        {
                            value = number;
                            return true;
                        }
                        return false;
                    }
                    if (item.str.TryGetValue(valueIndex, out string strValue))
                    {
                        value = strValue;
                        return true;
                    }
                    return false;
                }
                if (item.num.TryGetValue(valueIndex, out double fallbackNumber))
                {
                    value = fallbackNumber;
                    return true;
                }
                if (item.str.TryGetValue(valueIndex, out string fallbackStr))
                {
                    value = fallbackStr;
                    return true;
                }
                return false;
            }
        }

        public bool TryGetItemValueByIndex(int structIndex, int itemIndex, int valueIndex, out object value, out DataStructValueType valueType)
        {
            value = null;
            valueType = DataStructValueType.Text;
            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    return false;
                }
                if (item.FieldTypes.TryGetValue(valueIndex, out DataStructValueType type))
                {
                    valueType = type;
                    if (type == DataStructValueType.Number)
                    {
                        if (item.num.TryGetValue(valueIndex, out double number))
                        {
                            value = number;
                            return true;
                        }
                        return false;
                    }
                    if (item.str.TryGetValue(valueIndex, out string strValue))
                    {
                        value = strValue;
                        return true;
                    }
                    return false;
                }
                if (item.num.TryGetValue(valueIndex, out double fallbackNumber))
                {
                    value = fallbackNumber;
                    valueType = DataStructValueType.Number;
                    return true;
                }
                if (item.str.TryGetValue(valueIndex, out string fallbackStr))
                {
                    value = fallbackStr;
                    valueType = DataStructValueType.Text;
                    return true;
                }
                return false;
            }
        }

        public int GetItemValueCount(int structIndex, int itemIndex)
        {
            lock (dataLock)
            {
                if (!TryGetItemNoLock(structIndex, itemIndex, out DataStructItem item))
                {
                    return 0;
                }
                HashSet<int> indexes = new HashSet<int>();
                indexes.UnionWith(item.FieldNames.Keys);
                indexes.UnionWith(item.FieldTypes.Keys);
                indexes.UnionWith(item.num.Keys);
                indexes.UnionWith(item.str.Keys);
                return indexes.Count;
            }
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
                    itemIndex = dataStruct.dataStructItems.Count;
                }
                dataStruct.dataStructItems.Insert(itemIndex, item);
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
                DataStructItem copy = sourceItem.Clone();
                DataStruct targetStruct = dataStructs[targetStructIndex];
                NormalizeStruct(targetStruct);
                InsertOrReplaceItem(targetStruct, targetItemIndex, copy);
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
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TryRemoveFirstItem(int structIndex)
        {
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    return false;
                }
                DataStruct dataStruct = dataStructs[structIndex];
                if (dataStruct.dataStructItems.Count == 0)
                {
                    return false;
                }
                dataStruct.dataStructItems.RemoveAt(0);
                MarkChangedNoLock();
                MarkStructChangedNoLock(structIndex);
                return true;
            }
        }

        public bool TryRemoveLastItem(int structIndex)
        {
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    return false;
                }
                DataStruct dataStruct = dataStructs[structIndex];
                if (dataStruct.dataStructItems.Count == 0)
                {
                    return false;
                }
                dataStruct.dataStructItems.RemoveAt(dataStruct.dataStructItems.Count - 1);
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
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    return false;
                }
                bool found = false;
                foreach (DataStructItem item in dataStructs[structIndex].dataStructItems)
                {
                    if (item.str == null || !item.str.ContainsValue(key))
                    {
                        continue;
                    }
                    foreach (KeyValuePair<int, string> kvp in item.str)
                    {
                        if (kvp.Value == key)
                        {
                            value = kvp.Value;
                            found = true;
                        }
                    }
                }
                return found;
            }
        }

        public bool TryFindItemByNumberValue(int structIndex, double key, out double value)
        {
            value = 0;
            lock (dataLock)
            {
                if (structIndex < 0 || structIndex >= dataStructs.Count)
                {
                    return false;
                }
                bool found = false;
                foreach (DataStructItem item in dataStructs[structIndex].dataStructItems)
                {
                    if (item.num == null || !item.num.ContainsValue(key))
                    {
                        continue;
                    }
                    foreach (KeyValuePair<int, double> kvp in item.num)
                    {
                        if (kvp.Value == key)
                        {
                            value = kvp.Value;
                            found = true;
                        }
                    }
                }
                return found;
            }
        }

        private bool TryGetStructByNameNoLock(string name, out DataStruct dataStruct)
        {
            dataStruct = dataStructs.FirstOrDefault(ds => ds.Name == name);
            if (dataStruct == null)
            {
                return false;
            }
            NormalizeStruct(dataStruct);
            return true;
        }

        private bool TryGetStructByNameNoLock(string name, out DataStruct dataStruct, out int index)
        {
            dataStruct = null;
            index = -1;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            for (int i = 0; i < dataStructs.Count; i++)
            {
                if (dataStructs[i].Name == name)
                {
                    dataStruct = dataStructs[i];
                    index = i;
                    NormalizeStruct(dataStruct);
                    return true;
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
            NormalizeStruct(dataStruct);
            if (itemIndex < 0 || itemIndex >= dataStruct.dataStructItems.Count)
            {
                return false;
            }
            item = dataStruct.dataStructItems[itemIndex];
            if (item == null)
            {
                return false;
            }
            NormalizeItem(item);
            if (!ValidateItem(item, out _))
            {
                return false;
            }
            return true;
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

        private static void InsertOrReplaceItem(DataStruct dataStruct, int itemIndex, DataStructItem dataStructItem)
        {
            if (dataStruct.dataStructItems.Count <= itemIndex)
            {
                dataStruct.dataStructItems.Add(dataStructItem);
            }
            else if (itemIndex >= 0)
            {
                dataStruct.dataStructItems[itemIndex] = dataStructItem;
            }
            else
            {
                dataStruct.dataStructItems.Add(dataStructItem);
            }
        }

        private static bool TryBuildItem(string itemName, List<string> strings, List<double> doubles, string param, out DataStructItem dataStructItem)
        {
            dataStructItem = null;
            string format = param ?? string.Empty;
            int expectedStrCount = format.Count(ch => ch == '1');
            int expectedNumCount = format.Count(ch => ch == '0');
            if ((strings?.Count ?? 0) != expectedStrCount || (doubles?.Count ?? 0) != expectedNumCount)
            {
                return false;
            }

            dataStructItem = new DataStructItem
            {
                Name = itemName ?? string.Empty,
                FieldNames = new Dictionary<int, string>(),
                FieldTypes = new Dictionary<int, DataStructValueType>(),
                str = new Dictionary<int, string>(),
                num = new Dictionary<int, double>()
            };

            List<string> strList = strings ?? new List<string>();
            List<double> numList = doubles ?? new List<double>();
            int strIndex = 0;
            int numIndex = 0;

            for (int i = 0; i < format.Length; i++)
            {
                char digitChar = format[i];
                if (digitChar == '0')
                {
                    if (numIndex >= numList.Count)
                    {
                        return false;
                    }
                    dataStructItem.num[i] = numList[numIndex];
                    dataStructItem.FieldNames[i] = $"字段{i}";
                    dataStructItem.FieldTypes[i] = DataStructValueType.Number;
                    numIndex++;
                }
                else if (digitChar == '1')
                {
                    if (strIndex >= strList.Count)
                    {
                        return false;
                    }
                    dataStructItem.str[i] = strList[strIndex];
                    dataStructItem.FieldNames[i] = $"字段{i}";
                    dataStructItem.FieldTypes[i] = DataStructValueType.Text;
                    strIndex++;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
    }
}
