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
                    TypeNameHandling = TypeNameHandling.All
                };
                List<DataStruct> temp = JsonConvert.DeserializeObject<List<DataStruct>>(json, settings);
                LoadFromList(temp);
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                Reset();
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
                MarkChangedNoLock();
            }
        }

        private void LoadFromList(List<DataStruct> source)
        {
            lock (dataLock)
            {
                dataStructs.Clear();
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

        public bool AddStruct(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            lock (dataLock)
            {
                if (dataStructs.Any(ds => ds.Name == name))
                {
                    return false;
                }
                dataStructs.Add(new DataStruct { Name = name });
                MarkChangedNoLock();
                return true;
            }
        }

        public bool RenameStruct(int index, string newName)
        {
            if (string.IsNullOrEmpty(newName))
            {
                return false;
            }
            lock (dataLock)
            {
                if (index < 0 || index >= dataStructs.Count)
                {
                    return false;
                }
                int existIndex = dataStructs.FindIndex(ds => ds.Name == newName);
                if (existIndex >= 0 && existIndex != index)
                {
                    return false;
                }
                dataStructs[index].Name = newName;
                MarkChangedNoLock();
                return true;
            }
        }

        public bool RemoveStructAt(int index)
        {
            lock (dataLock)
            {
                if (index < 0 || index >= dataStructs.Count)
                {
                    return false;
                }
                dataStructs.RemoveAt(index);
                MarkChangedNoLock();
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
                if (!TryGetStructByNameNoLock(structName, out DataStruct dataStruct))
                {
                    return false;
                }
                DataStructItem dataStructItem = BuildItem(itemName, strings, doubles, param);
                InsertOrReplaceItem(dataStruct, itemIndex, dataStructItem);
                MarkChangedNoLock();
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
                DataStructItem dataStructItem = BuildItem(string.Empty, strings, doubles, param);
                InsertOrReplaceItem(dataStruct, itemIndex, dataStructItem);
                MarkChangedNoLock();
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
                    MarkChangedNoLock();
                    return true;
                }

                item.str[valueIndex] = value;
                item.num.Remove(valueIndex);
                MarkChangedNoLock();
                return true;
            }

            if (item.num.ContainsKey(valueIndex))
            {
                if (!double.TryParse(value, out double number))
                {
                    return false;
                }
                item.num[valueIndex] = number;
                MarkChangedNoLock();
                return true;
            }

            if (item.str.ContainsKey(valueIndex))
            {
                item.str[valueIndex] = value;
                MarkChangedNoLock();
                return true;
            }

            item.str[valueIndex] = value;
            MarkChangedNoLock();
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
                if (item.num.TryGetValue(valueIndex, out double number))
                {
                    value = number;
                    return true;
                }
                if (item.str.TryGetValue(valueIndex, out string strValue))
                {
                    value = strValue;
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
                if (item.num.TryGetValue(valueIndex, out double number))
                {
                    value = number;
                    valueType = DataStructValueType.Number;
                    return true;
                }
                if (item.str.TryGetValue(valueIndex, out string strValue))
                {
                    value = strValue;
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
                return item.num.Count + item.str.Count;
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
                item = new DataStructItem();
                dataStruct.dataStructItems[itemIndex] = item;
            }
            NormalizeItem(item);
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

        private static DataStructItem BuildItem(string itemName, List<string> strings, List<double> doubles, string param)
        {
            DataStructItem dataStructItem = new DataStructItem
            {
                Name = itemName ?? string.Empty
            };

            List<string> strList = strings ?? new List<string>();
            List<double> numList = doubles ?? new List<double>();
            string format = param ?? string.Empty;
            int strIndex = 0;
            int numIndex = 0;

            for (int i = 0; i < format.Length; i++)
            {
                char digitChar = format[i];
                if (digitChar == '0')
                {
                    if (numIndex < numList.Count)
                    {
                        dataStructItem.num[i] = numList[numIndex];
                    }
                    numIndex++;
                }
                else if (digitChar == '1')
                {
                    if (strIndex < strList.Count)
                    {
                        dataStructItem.str[i] = strList[strIndex];
                    }
                    strIndex++;
                }
            }

            return dataStructItem;
        }
    }
}
