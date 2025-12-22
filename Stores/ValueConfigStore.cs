using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Automation
{
    public class ValueConfigStore
    {
        public const int ValueCapacity = 1000;

        private readonly object valueLock = new object();
        private readonly DicValue[] values = new DicValue[ValueCapacity];
        private readonly Dictionary<string, int> nameIndex = new Dictionary<string, int>();

        public ValueConfigStore()
        {
            ResetValues();
        }

        public bool Load(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "value.json");
            if (!File.Exists(filePath))
            {
                ResetValues();
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
                Dictionary<string, DicValue> temp = JsonConvert.DeserializeObject<Dictionary<string, DicValue>>(json, settings);
                LoadFromDictionary(temp);
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                ResetValues();
                return false;
            }
        }

        public bool Save(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "value.json");
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            string output = JsonConvert.SerializeObject(BuildSaveData(), settings);
            File.WriteAllText(filePath, output);
            return true;
        }

        private void ResetValues()
        {
            for (int i = 0; i < ValueCapacity; i++)
            {
                values[i] = new DicValue { Index = i };
            }
            nameIndex.Clear();
        }

        private void LoadFromDictionary(Dictionary<string, DicValue> source)
        {
            lock (valueLock)
            {
                ResetValues();
                if (source == null)
                {
                    return;
                }
                foreach (var item in source)
                {
                    if (item.Value == null)
                    {
                        continue;
                    }
                    if (string.IsNullOrEmpty(item.Key))
                    {
                        continue;
                    }
                    int index = item.Value.Index;
                    if (index < 0 || index >= ValueCapacity)
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(values[index].Name))
                    {
                        continue;
                    }
                    item.Value.Name = item.Key;
                    if (item.Value.Type != "double" && item.Value.Type != "string")
                    {
                        item.Value.Type = "string";
                    }
                    values[index] = item.Value;
                    nameIndex[item.Key] = index;
                }
            }
        }

        public DicValue GetValueByIndex(int index)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"索引超出范围:{index}");
            }
            lock (valueLock)
            {
                if (string.IsNullOrEmpty(values[index].Name))
                {
                    throw new KeyNotFoundException($"未找到索引变量:{index}");
                }
                return values[index];
            }
        }

        public DicValue GetValueByName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("变量名不能为空", nameof(key));
            }
            lock (valueLock)
            {
                if (!nameIndex.TryGetValue(key, out int index))
                {
                    throw new KeyNotFoundException($"未找到变量:{key}");
                }
                return values[index];
            }
        }

        public bool TryGetValueByIndex(int index, out DicValue value)
        {
            value = null;
            if (index < 0 || index >= ValueCapacity)
            {
                return false;
            }
            lock (valueLock)
            {
                if (string.IsNullOrEmpty(values[index].Name))
                {
                    return false;
                }
                value = values[index];
                return true;
            }
        }

        public bool TryGetValueByName(string key, out DicValue value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            lock (valueLock)
            {
                if (!nameIndex.TryGetValue(key, out int index))
                {
                    return false;
                }
                value = values[index];
                return true;
            }
        }

        public List<string> GetValueNames()
        {
            lock (valueLock)
            {
                return nameIndex.Keys.ToList();
            }
        }

        public Dictionary<string, DicValue> BuildSaveData()
        {
            Dictionary<string, DicValue> data = new Dictionary<string, DicValue>();
            lock (valueLock)
            {
                foreach (var item in nameIndex)
                {
                    DicValue value = values[item.Value];
                    if (value == null || string.IsNullOrEmpty(value.Name))
                    {
                        continue;
                    }
                    data[item.Key] = value;
                }
            }
            return data;
        }

        public bool TrySetValue(int index, string name, string type, string value, string note)
        {
            if (index < 0 || index >= ValueCapacity || string.IsNullOrEmpty(name))
            {
                return false;
            }
            lock (valueLock)
            {
                if (nameIndex.TryGetValue(name, out int existIndex) && existIndex != index)
                {
                    return false;
                }

                DicValue currentValue = values[index];
                if (!string.IsNullOrEmpty(currentValue.Name) && currentValue.Name != name)
                {
                    nameIndex.Remove(currentValue.Name);
                }
                currentValue.Name = name;
                currentValue.Index = index;
                currentValue.Type = type;
                currentValue.Note = note;
                currentValue.Value = value;
                nameIndex[name] = index;
                return true;
            }
        }

        public bool IsMarked(int index)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return false;
            }
            lock (valueLock)
            {
                return values[index].isMark;
            }
        }

        public bool ToggleMark(int index)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return false;
            }
            lock (valueLock)
            {
                values[index].isMark = !values[index].isMark;
                return true;
            }
        }

        public void ClearMarks()
        {
            lock (valueLock)
            {
                for (int i = 0; i < ValueCapacity; i++)
                {
                    values[i].isMark = false;
                }
            }
        }

        public double get_D_ValueByIndex(int index)
        {
            double result;
            if (index < 0 || index >= ValueCapacity)
            {
                return -97654321;
            }
            lock (valueLock)
            {
                if (double.TryParse(values[index].Value, out result))
                {
                    return result;
                }
                else
                {
                    return -97654321;
                }
            }
        }

        public string get_Str_ValueByIndex(int index)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return null;
            }
            lock (valueLock)
            {
                return values[index].Value;
            }
        }

        public double get_D_ValueByName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return -97654321;
            }
            lock (valueLock)
            {
                if (nameIndex.TryGetValue(key, out int index))
                {
                    double result;
                    if (double.TryParse(values[index].Value, out result))
                    {
                        return result;
                    }
                }
                return -97654321;
            }
        }

        public string get_Str_ValueByName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            lock (valueLock)
            {
                if (nameIndex.TryGetValue(key, out int index))
                {
                    return values[index].Value;
                }
            }
            return null;

        }

        public bool setValueByName(string key, object newValue)
        {
            if (string.IsNullOrEmpty(key) || newValue == null)
            {
                return false;
            }
            lock (valueLock)
            {
                if (nameIndex.TryGetValue(key, out int index))
                {
                    DicValue value = values[index];
                    value.Value = newValue.ToString();
                    return true;
                }
                return false;
            }
        }

        public bool setValueByIndex(int index, object newValue)
        {
            if (index < 0 || index >= ValueCapacity || newValue == null)
            {
                return false;
            }
            lock (valueLock)
            {
                if (string.IsNullOrEmpty(values[index].Name))
                {
                    return false;
                }
                values[index].Value = newValue.ToString();
                return true;
            }
        }
    }

    public class DicValue
    {
        public int Index { get; set; }

        public string Type { get; set; }

        public string Name { get; set; }

        public string Value { get; set; }

        public string Note { get; set; }
        public bool isMark { get; set; }

        public double GetDValue()
        {
            return double.Parse(Value);
        }

        public string GetCValue()
        {
            return Value;
        }
    }
}
