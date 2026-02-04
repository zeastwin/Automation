using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
        private readonly object[] valueLocks;
        private volatile Dictionary<string, int> nameIndexSnapshot = new Dictionary<string, int>();
        private readonly object monitorLock = new object();
        private readonly bool[] monitorFlags = new bool[ValueCapacity];
        private volatile bool monitorEnabled;

        public event EventHandler<ValueChangedEventArgs> ValueChanged;

        public ValueConfigStore()
        {
            valueLocks = new object[64];
            for (int i = 0; i < valueLocks.Length; i++)
            {
                valueLocks[i] = new object();
            }
            ResetValues();
        }

        public void SetMonitorFlag(int index, bool isMonitored)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return;
            }
            lock (monitorLock)
            {
                monitorFlags[index] = isMonitored;
            }
        }

        public void SetMonitorEnabled(bool enabled)
        {
            monitorEnabled = enabled;
        }

        public bool IsMonitored(int index)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return false;
            }
            lock (monitorLock)
            {
                return monitorFlags[index];
            }
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
                values[i] = new DicValue
                {
                    Index = i,
                    Type = "double",
                    ConfigValue = "0"
                };
                values[i].ResetRuntimeFromConfig();
            }
            nameIndex.Clear();
            nameIndexSnapshot = new Dictionary<string, int>();
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
                    item.Value.ResetRuntimeFromConfig();
                    values[index] = item.Value;
                    nameIndex[item.Key] = index;
                }
                nameIndexSnapshot = new Dictionary<string, int>(nameIndex);
            }
        }

        public DicValue GetValueByIndex(int index)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"索引超出范围:{index}");
            }
            DicValue value = values[index];
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                throw new KeyNotFoundException($"未找到索引变量:{index}");
            }
            return value;
        }

        public DicValue GetValueByName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("变量名不能为空", nameof(key));
            }
            Dictionary<string, int> snapshot = nameIndexSnapshot;
            if (!snapshot.TryGetValue(key, out int index))
            {
                throw new KeyNotFoundException($"未找到变量:{key}");
            }
            return GetValueByIndex(index);
        }

        public bool TryGetValueByIndex(int index, out DicValue value)
        {
            value = null;
            if (index < 0 || index >= ValueCapacity)
            {
                return false;
            }
            value = values[index];
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                value = null;
                return false;
            }
            return true;
        }

        public bool TryGetValueByName(string key, out DicValue value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            Dictionary<string, int> snapshot = nameIndexSnapshot;
            if (!snapshot.TryGetValue(key, out int index))
            {
                return false;
            }
            return TryGetValueByIndex(index, out value);
        }

        public List<string> GetValueNames()
        {
            Dictionary<string, int> snapshot = nameIndexSnapshot;
            return snapshot.Keys.ToList();
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

        public bool TrySetValue(int index, string name, string type, string value, string note, string source = null)
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
                string oldRuntime = currentValue.Value;
                if (!string.IsNullOrEmpty(currentValue.Name) && currentValue.Name != name)
                {
                    nameIndex.Remove(currentValue.Name);
                }
                currentValue.Name = name;
                currentValue.Index = index;
                currentValue.Type = type;
                currentValue.Note = note;
                currentValue.ConfigValue = value;
                currentValue.Value = value;
                nameIndex[name] = index;
                nameIndexSnapshot = new Dictionary<string, int>(nameIndex);
                if (!string.Equals(oldRuntime, value, StringComparison.Ordinal))
                {
                    RaiseValueChanged(currentValue, oldRuntime, value, source);
                }
                return true;
            }
        }

        public bool ClearValueByIndex(int index, string source = null)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return false;
            }
            lock (valueLock)
            {
                DicValue currentValue = values[index];
                if (currentValue == null)
                {
                    return false;
                }
                string oldName = currentValue.Name;
                string oldRuntime = currentValue.Value;
                if (!string.IsNullOrEmpty(oldName))
                {
                    nameIndex.Remove(oldName);
                }
                currentValue.Name = string.Empty;
                currentValue.Type = "double";
                currentValue.Note = string.Empty;
                currentValue.ConfigValue = "0";
                currentValue.Value = "0";
                currentValue.isMark = false;
                nameIndexSnapshot = new Dictionary<string, int>(nameIndex);
                if (!string.IsNullOrEmpty(oldRuntime))
                {
                    RaiseValueChanged(currentValue, oldRuntime, currentValue.Value, source);
                }
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
            DicValue value = GetValueByIndex(index);
            return value.GetDValue();
        }

        public string get_Str_ValueByIndex(int index)
        {
            DicValue value = GetValueByIndex(index);
            return value.GetCValue();
        }

        public double get_D_ValueByName(string key)
        {
            DicValue value = GetValueByName(key);
            return value.GetDValue();
        }

        public string get_Str_ValueByName(string key)
        {
            DicValue value = GetValueByName(key);
            return value.GetCValue();

        }

        private bool TryValidateRuntimeValue(DicValue value, string runtimeValue, out string error)
        {
            error = null;
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                error = "变量不存在";
                return false;
            }
            if (runtimeValue == null)
            {
                error = "运行值为空";
                return false;
            }
            if (string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(runtimeValue) || !double.TryParse(runtimeValue, out _))
                {
                    error = $"变量{value.Name}运行值不是有效数字";
                    return false;
                }
                return true;
            }
            if (!string.Equals(value.Type, "string", StringComparison.OrdinalIgnoreCase))
            {
                error = $"变量{value.Name}类型非法:{value.Type}";
                return false;
            }
            return true;
        }

        public bool TryModifyValueByIndex(int index, Func<string, string> updater, out string error, string source = null)
        {
            error = null;
            if (index < 0 || index >= ValueCapacity)
            {
                error = $"索引超出范围:{index}";
                return false;
            }
            if (updater == null)
            {
                error = "更新函数为空";
                return false;
            }
            DicValue value = values[index];
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                error = $"未找到索引变量:{index}";
                return false;
            }
            object lockObj = valueLocks[index & (valueLocks.Length - 1)];
            lock (lockObj)
            {
                string current = value.Value;
                string updated;
                try
                {
                    updated = updater(current);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
                if (string.Equals(current, updated, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!TryValidateRuntimeValue(value, updated, out error))
                {
                    return false;
                }
                value.Value = updated;
                RaiseValueChanged(value, current, updated, source);
                return true;
            }
        }

        public bool setValueByName(string key, object newValue, string source = null)
        {
            if (string.IsNullOrEmpty(key) || newValue == null)
            {
                return false;
            }
            Dictionary<string, int> snapshot = nameIndexSnapshot;
            if (!snapshot.TryGetValue(key, out int index))
            {
                return false;
            }
            return setValueByIndex(index, newValue, source);
        }

        public bool setValueByIndex(int index, object newValue, string source = null)
        {
            if (index < 0 || index >= ValueCapacity || newValue == null)
            {
                return false;
            }
            DicValue value = values[index];
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                return false;
            }
            string runtimeValue = newValue.ToString();
            object lockObj = valueLocks[index & (valueLocks.Length - 1)];
            lock (lockObj)
            {
                string current = value.Value;
                if (string.Equals(current, runtimeValue, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!TryValidateRuntimeValue(value, runtimeValue, out _))
                {
                    return false;
                }
                value.Value = runtimeValue;
                RaiseValueChanged(value, current, runtimeValue, source);
                return true;
            }
        }

        private void RaiseValueChanged(DicValue value, string oldValue, string newValue, string source)
        {
            if (value == null)
            {
                return;
            }
            if (!monitorEnabled || !IsMonitored(value.Index))
            {
                return;
            }
            string resolvedSource = ResolveSource(value.Index, source);
            DateTime time = DateTime.Now;
            value.LastChangedAt = time;
            value.LastChangedBy = resolvedSource;
            value.LastChangedOldValue = oldValue;
            value.LastChangedNewValue = newValue;
            EventHandler<ValueChangedEventArgs> handler = ValueChanged;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler.Invoke(this, new ValueChangedEventArgs
                {
                    Index = value.Index,
                    Name = value.Name,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Source = resolvedSource,
                    ChangedAt = time
                });
            }
            catch
            {
                // 忽略事件异常，避免影响主流程
            }
        }

        private string ResolveSource(int index, string source)
        {
            if (!monitorEnabled || !IsMonitored(index))
            {
                return string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(source))
            {
                return source;
            }
            try
            {
                StackTrace trace = new StackTrace(2, false);
                StackFrame[] frames = trace.GetFrames();
                if (frames == null)
                {
                    return "代码接口";
                }
                foreach (StackFrame frame in frames)
                {
                    var method = frame.GetMethod();
                    if (method == null)
                    {
                        continue;
                    }
                    var type = method.DeclaringType;
                    if (type == null || type == typeof(ValueConfigStore))
                    {
                        continue;
                    }
                    return $"{type.Name}.{method.Name}";
                }
            }
            catch
            {
            }
            return "代码接口";
        }
    }

    public class DicValue
    {
        public int Index { get; set; }

        public string Type { get; set; }

        public string Name { get; set; }

        [JsonProperty("Value")]
        public string ConfigValue { get; set; }

        [JsonIgnore]
        public string Value
        {
            get => Volatile.Read(ref runtimeValue);
            set => Volatile.Write(ref runtimeValue, value);
        }

        [JsonIgnore]
        private string runtimeValue;

        public string Note { get; set; }
        public bool isMark { get; set; }

        [JsonIgnore]
        public DateTime LastChangedAt { get; set; }

        [JsonIgnore]
        public string LastChangedBy { get; set; }

        [JsonIgnore]
        public string LastChangedOldValue { get; set; }

        [JsonIgnore]
        public string LastChangedNewValue { get; set; }

        public void ResetRuntimeFromConfig()
        {
            Value = ConfigValue;
        }

        public double GetDValue()
        {
            if (!string.Equals(Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"变量{DisplayName()}类型不是double");
            }
            string current = Value;
            if (string.IsNullOrWhiteSpace(current))
            {
                throw new InvalidOperationException($"变量{DisplayName()}值为空");
            }
            if (!double.TryParse(current, out double result))
            {
                throw new InvalidOperationException($"变量{DisplayName()}值不是有效数字:{current}");
            }
            return result;
        }

        public string GetCValue()
        {
            if (!string.Equals(Type, "string", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"变量{DisplayName()}类型不是string");
            }
            string current = Value;
            if (current == null)
            {
                throw new InvalidOperationException($"变量{DisplayName()}值为空");
            }
            return current;
        }

        private string DisplayName()
        {
            return string.IsNullOrEmpty(Name) ? $"索引{Index}" : Name;
        }
    }

    public sealed class ValueChangedEventArgs : EventArgs
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Source { get; set; }
        public DateTime ChangedAt { get; set; }
    }
}
