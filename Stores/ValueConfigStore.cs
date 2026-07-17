using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Automation.Protocol;
using Newtonsoft.Json;

namespace Automation
{
    public class ValueConfigStore
    {
        public const int NormalValueCapacity = VariableIndexContract.NormalValueCapacity;
        public const int SystemValueCapacity = VariableIndexContract.SystemValueCapacity;
        public const int SystemValueStartIndex = VariableIndexContract.SystemValueStartIndex;
        public const int ValueCapacity = VariableIndexContract.ValueCapacity;
        private static readonly HashSet<string> ProtectedValueNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "复位状态",
            "系统状态"
        };

        private readonly object valueLock = new object();
        private readonly DicValue[] values = new DicValue[ValueCapacity];
        private readonly Dictionary<string, int> nameIndex = new Dictionary<string, int>();
        private volatile Dictionary<string, int> nameIndexSnapshot = new Dictionary<string, int>();
        private readonly object monitorLock = new object();
        private readonly bool[] monitorFlags = new bool[ValueCapacity];
        private volatile bool monitorEnabled;

        private sealed class RuntimeValueState
        {
            public string Value { get; set; }
            public DateTime LastChangedAt { get; set; }
            public string LastChangedBy { get; set; }
            public string LastChangedOldValue { get; set; }
            public string LastChangedNewValue { get; set; }
        }

        public event EventHandler<ValueChangedEventArgs> ValueChanged;

        public ValueConfigStore()
        {
            ResetValues();
        }

        public static bool IsProtectedValueName(string name)
        {
            return !string.IsNullOrEmpty(name) && ProtectedValueNames.Contains(name);
        }

        public static bool IsSystemValueIndex(int index)
        {
            return VariableIndexContract.IsSystemIndex(index);
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
                if (!Save(configPath))
                {
                    SF.SetSecurityLock("变量配置初始化保存失败");
                }
                return false;
            }

            try
            {
                Dictionary<string, DicValue> temp = AtomicJsonFileStore.Read<Dictionary<string, DicValue>>(configPath, "value");
                if (temp == null)
                {
                    SF.SetSecurityLock("变量配置及其备份均无法读取，已保留原文件并禁止继续运行");
                    return false;
                }
                LoadFromDictionary(temp);
                return true;
            }
            catch (Exception e)
            {
                SF.DR?.Logger?.Log($"变量配置加载失败:{e}", LogLevel.Error);
                SF.SetSecurityLock($"变量配置加载失败:{e.Message}");
                return false;
            }
        }

        public bool Save(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            bool saved = AtomicJsonFileStore.Save(configPath, "value", BuildSaveData());
            if (!saved)
            {
                SF.SetSecurityLock("变量配置保存失败，内存与磁盘状态可能不一致");
            }
            return saved;
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

        private void LoadFromDictionary(
            Dictionary<string, DicValue> source,
            IReadOnlyDictionary<string, RuntimeValueState> preservedRuntimeValues = null)
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
                    if (ProtectedValueNames.Contains(item.Key) && !IsSystemValueIndex(index))
                    {
                        throw new InvalidDataException(
                            $"系统保留变量[{item.Key}]必须位于索引范围 [{SystemValueStartIndex}, {ValueCapacity})。");
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
                    if (preservedRuntimeValues != null
                        && preservedRuntimeValues.TryGetValue(item.Key, out RuntimeValueState runtimeState))
                    {
                        item.Value.Value = runtimeState.Value;
                        item.Value.LastChangedAt = runtimeState.LastChangedAt;
                        item.Value.LastChangedBy = runtimeState.LastChangedBy;
                        item.Value.LastChangedOldValue = runtimeState.LastChangedOldValue;
                        item.Value.LastChangedNewValue = runtimeState.LastChangedNewValue;
                    }
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
                    data[item.Key] = ObjectGraphCloner.Clone(value);
                }
            }
            return data;
        }

        /// <summary>
        /// 在配置事务已经落盘后，用同一份已校验快照替换内存变量表。
        /// 同一变量保留当前运行值，防止配置编辑把整张变量表重置到配置值。
        /// initialValue 只属于配置；新建、改名、改类型或改索引的变量才从新配置初始化运行值。
        /// 不在此方法内保存文件，避免把事务拆成第二次独立写入。
        /// </summary>
        public void ReplaceConfiguration(IDictionary<string, DicValue> source)
        {
            Dictionary<string, DicValue> snapshot = CreateValidatedConfigurationSnapshot(source);
            lock (valueLock)
            {
                var preservedRuntimeValues = new Dictionary<string, RuntimeValueState>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, DicValue> item in snapshot)
                {
                    if (!nameIndex.TryGetValue(item.Key, out int currentIndex))
                    {
                        continue;
                    }
                    DicValue current = values[currentIndex];
                    DicValue incoming = item.Value;
                    if (current == null
                        || current.Index != incoming.Index
                        || !string.Equals(current.Name, item.Key, StringComparison.Ordinal)
                        || !string.Equals(current.Type, incoming.Type, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    preservedRuntimeValues[item.Key] = new RuntimeValueState
                    {
                        Value = current.Value,
                        LastChangedAt = current.LastChangedAt,
                        LastChangedBy = current.LastChangedBy,
                        LastChangedOldValue = current.LastChangedOldValue,
                        LastChangedNewValue = current.LastChangedNewValue
                    };
                }
                LoadFromDictionary(snapshot, preservedRuntimeValues);
            }
        }

        /// <summary>
        /// 原子保存一份已修改的变量配置并刷新内存。公开调用方仍只修改单个变量；
        /// 整份字典只用于 value.json 的文件级原子替换和失败回滚。
        /// </summary>
        public bool TryCommitConfiguration(
            string configPath,
            IDictionary<string, DicValue> source,
            out string error,
            IReadOnlyDictionary<string, string> runtimeValueOverrides = null,
            string runtimeValueSource = null)
        {
            error = null;
            Dictionary<string, DicValue> snapshot;
            try
            {
                snapshot = CreateValidatedConfigurationSnapshot(source);
                if (runtimeValueOverrides != null)
                {
                    foreach (KeyValuePair<string, string> item in runtimeValueOverrides)
                    {
                        if (!snapshot.TryGetValue(item.Key, out DicValue variable))
                        {
                            throw new InvalidDataException($"运行值目标变量不存在：{item.Key}");
                        }
                        if (!TryValidateRuntimeValue(variable, item.Value, out string validationError))
                        {
                            throw new InvalidDataException(validationError);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            Dictionary<string, DicValue> oldConfiguration = BuildSaveData();
            Dictionary<string, RuntimeValueState> oldRuntimeValues = CaptureRuntimeValueStates();
            if (!AtomicJsonFileStore.Save(configPath, "value", snapshot))
            {
                error = "变量配置写入磁盘失败。";
                return false;
            }
            try
            {
                ReplaceConfiguration(snapshot);
                if (runtimeValueOverrides != null)
                {
                    foreach (KeyValuePair<string, string> item in runtimeValueOverrides)
                    {
                        if (!setValueByName(item.Key, item.Value, runtimeValueSource))
                        {
                            throw new InvalidOperationException($"变量[{item.Key}]运行值更新失败。");
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                bool diskRestored = AtomicJsonFileStore.Save(configPath, "value", oldConfiguration);
                bool memoryRestored = true;
                try
                {
                    ReplaceConfiguration(oldConfiguration);
                    RestoreRuntimeValueStates(oldRuntimeValues);
                }
                catch
                {
                    memoryRestored = false;
                }
                error = $"变量配置刷新失败：{ex.Message}；diskRestored={diskRestored}，memoryRestored={memoryRestored}";
                if (!diskRestored || !memoryRestored)
                {
                    SF.SetSecurityLock(error);
                }
                return false;
            }
        }

        private Dictionary<string, RuntimeValueState> CaptureRuntimeValueStates()
        {
            var snapshot = new Dictionary<string, RuntimeValueState>(StringComparer.Ordinal);
            lock (valueLock)
            {
                foreach (KeyValuePair<string, int> item in nameIndex)
                {
                    DicValue value = values[item.Value];
                    if (value == null || string.IsNullOrEmpty(value.Name))
                    {
                        continue;
                    }
                    snapshot[item.Key] = new RuntimeValueState
                    {
                        Value = value.Value,
                        LastChangedAt = value.LastChangedAt,
                        LastChangedBy = value.LastChangedBy,
                        LastChangedOldValue = value.LastChangedOldValue,
                        LastChangedNewValue = value.LastChangedNewValue
                    };
                }
            }
            return snapshot;
        }

        private void RestoreRuntimeValueStates(IReadOnlyDictionary<string, RuntimeValueState> source)
        {
            if (source == null)
            {
                return;
            }
            lock (valueLock)
            {
                foreach (KeyValuePair<string, RuntimeValueState> item in source)
                {
                    if (!nameIndex.TryGetValue(item.Key, out int index))
                    {
                        continue;
                    }
                    DicValue value = values[index];
                    RuntimeValueState state = item.Value;
                    value.Value = state.Value;
                    value.LastChangedAt = state.LastChangedAt;
                    value.LastChangedBy = state.LastChangedBy;
                    value.LastChangedOldValue = state.LastChangedOldValue;
                    value.LastChangedNewValue = state.LastChangedNewValue;
                }
            }
        }

        private static Dictionary<string, DicValue> CreateValidatedConfigurationSnapshot(
            IDictionary<string, DicValue> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var snapshot = new Dictionary<string, DicValue>(StringComparer.Ordinal);
            var indexes = new HashSet<int>();
            foreach (KeyValuePair<string, DicValue> item in source)
            {
                if (string.IsNullOrWhiteSpace(item.Key) || item.Value == null)
                {
                    throw new InvalidDataException("变量配置快照包含空名称或空对象。");
                }
                if (item.Value.Index < 0 || item.Value.Index >= ValueCapacity)
                {
                    throw new InvalidDataException($"变量[{item.Key}]索引超出范围：{item.Value.Index}");
                }
                if (ProtectedValueNames.Contains(item.Key) && !IsSystemValueIndex(item.Value.Index))
                {
                    throw new InvalidDataException(
                        $"系统保留变量[{item.Key}]必须位于索引范围 [{SystemValueStartIndex}, {ValueCapacity})。");
                }
                if (!indexes.Add(item.Value.Index))
                {
                    throw new InvalidDataException($"变量配置包含重复索引：{item.Value.Index}");
                }
                if (!string.Equals(item.Value.Type, "double", StringComparison.Ordinal)
                    && !string.Equals(item.Value.Type, "string", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"变量[{item.Key}]类型无效：{item.Value.Type}");
                }
                if (string.Equals(item.Value.Type, "double", StringComparison.Ordinal)
                    && !double.TryParse(item.Value.ConfigValue, out _))
                {
                    throw new InvalidDataException($"变量[{item.Key}]配置值不是有效数字：{item.Value.ConfigValue}");
                }
                DicValue clone = ObjectGraphCloner.Clone(item.Value);
                clone.Name = item.Key;
                snapshot[item.Key] = clone;
            }
            return snapshot;
        }

        public bool TrySetValue(int index, string name, string type, string value, string note, string source = null)
        {
            name = name?.Trim();
            if (index < 0 || index >= ValueCapacity || string.IsNullOrEmpty(name)
                || (!string.Equals(type, "double", StringComparison.Ordinal)
                    && !string.Equals(type, "string", StringComparison.Ordinal)))
            {
                return false;
            }
            if (ProtectedValueNames.Contains(name) && !IsSystemValueIndex(index))
            {
                return false;
            }
            if (string.Equals(type, "double", StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(value) || !double.TryParse(value, out _)))
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
                if (ProtectedValueNames.Contains(currentValue.Name)
                    && (!string.Equals(currentValue.Name, name, StringComparison.Ordinal)
                        || !string.Equals(type, "double", StringComparison.Ordinal)))
                {
                    return false;
                }
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
                if (ProtectedValueNames.Contains(currentValue.Name))
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
            lock (valueLock)
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
            lock (valueLock)
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
