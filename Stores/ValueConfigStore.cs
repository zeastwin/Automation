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
        private readonly HashSet<Guid> monitoredVariableIds = new HashSet<Guid>();
        private volatile bool monitorEnabled;

        private sealed class CurrentValueState
        {
            public Guid Id { get; set; }
            public string Value { get; set; }
            public DateTime LastChangedAt { get; set; }
            public string LastChangedBy { get; set; }
            public string LastChangedOldValue { get; set; }
            public string LastChangedNewValue { get; set; }
        }

        public event EventHandler<ValueChangedEventArgs> ValueChanged;

        public bool ConfigurationFaulted { get; private set; }

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

        public static bool IsProcessValue(DicValue value)
        {
            return value != null
                && string.Equals(value.Scope, VariableScopeContract.Process, StringComparison.Ordinal);
        }

        public static bool CanProcessAccess(DicValue value, Guid procId)
        {
            if (value == null)
            {
                return false;
            }
            return !IsProcessValue(value) || procId != Guid.Empty && value.OwnerProcId == procId;
        }

        public void SetMonitorFlag(int index, bool isMonitored)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return;
            }
            Guid id;
            lock (valueLock) id = values[index]?.Id ?? Guid.Empty;
            SetMonitorFlag(id, isMonitored);
        }

        public void SetMonitorFlag(Guid variableId, bool isMonitored)
        {
            if (variableId == Guid.Empty) return;
            lock (monitorLock)
            {
                if (isMonitored) monitoredVariableIds.Add(variableId);
                else monitoredVariableIds.Remove(variableId);
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
            Guid id;
            lock (valueLock) id = values[index]?.Id ?? Guid.Empty;
            lock (monitorLock) return id != Guid.Empty && monitoredVariableIds.Contains(id);
        }

        public bool Load(string configPath, IEnumerable<Proc> processes = null)
        {
            ConfigurationFaulted = false;
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
                    ConfigurationFaulted = true;
                    SF.SetSecurityLock("变量配置初始化保存失败");
                }
                return false;
            }

            try
            {
                Dictionary<string, DicValue> temp = AtomicJsonFileStore.Read<Dictionary<string, DicValue>>(configPath, "value");
                if (temp == null)
                {
                    ConfigurationFaulted = true;
                    SF.SetSecurityLock("变量配置及其备份均无法读取，已保留原文件并禁止继续运行");
                    return false;
                }
                Dictionary<string, DicValue> snapshot = CreateValidatedConfigurationSnapshot(temp);
                ValidateProcessOwners(snapshot.Values, processes);
                LoadFromDictionary(snapshot);
                return true;
            }
            catch (Exception e)
            {
                ConfigurationFaulted = true;
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
                    Value = "0",
                    Scope = IsSystemValueIndex(i)
                        ? VariableScopeContract.System
                        : VariableScopeContract.Public
                };
            }
            nameIndex.Clear();
            nameIndexSnapshot = new Dictionary<string, int>();
        }

        private void LoadFromDictionary(
            Dictionary<string, DicValue> source,
            IReadOnlyDictionary<string, CurrentValueState> preservedCurrentValues = null)
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
                    ValidateScope(item.Key, item.Value);
                    if (!string.IsNullOrEmpty(values[index].Name))
                    {
                        continue;
                    }
                    item.Value.Name = item.Key;
                    if (item.Value.Type != "double" && item.Value.Type != "string")
                    {
                        item.Value.Type = "string";
                    }
                    if (preservedCurrentValues != null
                        && item.Value.Id != Guid.Empty
                        && preservedCurrentValues.TryGetValue(item.Value.Id.ToString("D"), out CurrentValueState currentState))
                    {
                        item.Value.Value = currentState.Value;
                        item.Value.LastChangedAt = currentState.LastChangedAt;
                        item.Value.LastChangedBy = currentState.LastChangedBy;
                        item.Value.LastChangedOldValue = currentState.LastChangedOldValue;
                        item.Value.LastChangedNewValue = currentState.LastChangedNewValue;
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

        public DicValue GetValueByNameForProcess(string key, Guid procId)
        {
            DicValue value = GetValueByName(key);
            if (!CanProcessAccess(value, procId))
            {
                throw new InvalidOperationException(
                    $"流程无权访问其他流程的私有变量:{key}");
            }
            return value;
        }

        public DicValue GetValueByIndexForProcess(int index, Guid procId)
        {
            DicValue value = GetValueByIndex(index);
            if (!CanProcessAccess(value, procId))
            {
                throw new InvalidOperationException(
                    $"流程无权访问其他流程的私有变量索引:{index}");
            }
            return value;
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

        public bool TryGetValueByNameForProcess(string key, Guid procId, out DicValue value)
        {
            if (!TryGetValueByName(key, out value) || !CanProcessAccess(value, procId))
            {
                value = null;
                return false;
            }
            return true;
        }

        public bool TryGetValueByIndexForProcess(int index, Guid procId, out DicValue value)
        {
            if (!TryGetValueByIndex(index, out value) || !CanProcessAccess(value, procId))
            {
                value = null;
                return false;
            }
            return true;
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

        public List<DicValue> GetValuesSnapshot()
        {
            lock (valueLock)
            {
                return nameIndex.Values
                    .Select(index => values[index])
                    .Where(value => value != null && !string.IsNullOrEmpty(value.Name))
                    .Select(ObjectGraphCloner.Clone)
                    .OrderBy(value => value.Index)
                    .ToList();
            }
        }

        /// <summary>
        /// 在配置事务已经落盘后，用同一份已校验快照替换内存变量表。
        /// 同一变量按稳定ID保留当前值，名称、类型、作用域、归属和索引变化不改变该值。
        /// 不在此方法内保存文件，避免把事务拆成第二次独立写入。
        /// </summary>
        public void ReplaceConfiguration(IDictionary<string, DicValue> source)
        {
            Dictionary<string, DicValue> snapshot = CreateValidatedConfigurationSnapshot(source);
            lock (valueLock)
            {
                var preservedCurrentValues = new Dictionary<string, CurrentValueState>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, int> currentEntry in nameIndex)
                {
                    DicValue current = values[currentEntry.Value];
                    if (current == null
                        || current.Id == Guid.Empty)
                    {
                        continue;
                    }
                    DicValue incoming = snapshot.Values.FirstOrDefault(value => value != null && value.Id == current.Id);
                    if (incoming == null)
                    {
                        continue;
                    }
                    preservedCurrentValues[current.Id.ToString("D")] = new CurrentValueState
                    {
                        Id = current.Id,
                        Value = current.Value,
                        LastChangedAt = current.LastChangedAt,
                        LastChangedBy = current.LastChangedBy,
                        LastChangedOldValue = current.LastChangedOldValue,
                        LastChangedNewValue = current.LastChangedNewValue
                    };
                }
                LoadFromDictionary(snapshot, preservedCurrentValues);
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
            IReadOnlyDictionary<string, string> currentValueOverrides = null,
            string currentValueSource = null)
        {
            error = null;
            Dictionary<string, DicValue> snapshot;
            try
            {
                snapshot = CreateValidatedConfigurationSnapshot(source);
                ValidateProcessOwners(snapshot.Values, SF.frmProc?.procsList);
                if (currentValueOverrides != null)
                {
                    foreach (KeyValuePair<string, string> item in currentValueOverrides)
                    {
                        if (!snapshot.TryGetValue(item.Key, out DicValue variable))
                        {
                            throw new InvalidDataException($"当前值目标变量不存在：{item.Key}");
                        }
                        if (!TryValidateCurrentValue(variable, item.Value, out string validationError))
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
            Dictionary<string, CurrentValueState> oldCurrentValues = CaptureCurrentValueStates();
            if (!AtomicJsonFileStore.Save(configPath, "value", snapshot))
            {
                error = "变量配置写入磁盘失败。";
                return false;
            }
            try
            {
                ReplaceConfiguration(snapshot);
                if (currentValueOverrides != null)
                {
                    foreach (KeyValuePair<string, string> item in currentValueOverrides)
                    {
                        if (!setValueByName(item.Key, item.Value, currentValueSource))
                        {
                            throw new InvalidOperationException($"变量[{item.Key}]当前值更新失败。");
                        }
                    }
                }
                ConfigurationFaulted = false;
                return true;
            }
            catch (Exception ex)
            {
                bool diskRestored = AtomicJsonFileStore.Save(configPath, "value", oldConfiguration);
                bool memoryRestored = true;
                try
                {
                    ReplaceConfiguration(oldConfiguration);
                    RestoreCurrentValueStates(oldCurrentValues);
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

        private Dictionary<string, CurrentValueState> CaptureCurrentValueStates()
        {
            var snapshot = new Dictionary<string, CurrentValueState>(StringComparer.Ordinal);
            lock (valueLock)
            {
                foreach (KeyValuePair<string, int> item in nameIndex)
                {
                    DicValue value = values[item.Value];
                    if (value == null || string.IsNullOrEmpty(value.Name))
                    {
                        continue;
                    }
                    if (value.Id == Guid.Empty)
                    {
                        continue;
                    }
                    snapshot[value.Id.ToString("D")] = new CurrentValueState
                    {
                        Id = value.Id,
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

        private void RestoreCurrentValueStates(IReadOnlyDictionary<string, CurrentValueState> source)
        {
            if (source == null)
            {
                return;
            }
            lock (valueLock)
            {
                foreach (KeyValuePair<string, CurrentValueState> item in source)
                {
                    DicValue value = values.FirstOrDefault(candidate =>
                        candidate != null && !string.IsNullOrEmpty(candidate.Name)
                        && candidate.Id == item.Value.Id);
                    if (value == null)
                    {
                        continue;
                    }
                    CurrentValueState state = item.Value;
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
            var ids = new HashSet<Guid>();
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
                if (item.Value.Id == Guid.Empty)
                {
                    throw new InvalidDataException($"变量[{item.Key}]缺少稳定ID。");
                }
                if (!string.Equals(item.Value.Name, item.Key, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"变量字典键[{item.Key}]与对象名称[{item.Value.Name ?? "空"}]不一致。");
                }
                if (!ids.Add(item.Value.Id))
                {
                    throw new InvalidDataException($"变量配置包含重复稳定ID：{item.Value.Id:D}");
                }
                ValidateScope(item.Key, item.Value);
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
                if (item.Value.Value == null)
                {
                    throw new InvalidDataException($"变量[{item.Key}]当前值不能为null。");
                }
                if (string.Equals(item.Value.Type, "double", StringComparison.Ordinal)
                    && !double.TryParse(item.Value.Value, out _))
                {
                    throw new InvalidDataException($"变量[{item.Key}]当前值不是有效数字：{item.Value.Value}");
                }
                DicValue clone = ObjectGraphCloner.Clone(item.Value);
                clone.Name = item.Key;
                snapshot[item.Key] = clone;
            }
            return snapshot;
        }

        public static void ValidateProcessOwners(
            IEnumerable<DicValue> variables, IEnumerable<Proc> processes)
        {
            var processIds = new HashSet<Guid>((processes ?? Enumerable.Empty<Proc>())
                .Select(proc => proc?.head?.Id ?? Guid.Empty)
                .Where(id => id != Guid.Empty));
            foreach (DicValue variable in variables ?? Enumerable.Empty<DicValue>())
            {
                if (!IsProcessValue(variable))
                {
                    continue;
                }
                if (!variable.OwnerProcId.HasValue
                    || !processIds.Contains(variable.OwnerProcId.Value))
                {
                    throw new InvalidDataException(
                        $"流程私有变量[{variable.Name}]所属流程不存在："
                        + $"{variable.OwnerProcId?.ToString("D") ?? "空"}");
                }
            }
        }

        private static void ValidateScope(string name, DicValue value)
        {
            if (value == null || !VariableScopeContract.IsValid(value.Scope))
            {
                throw new InvalidDataException(
                    $"变量[{name}]作用域无效：{value?.Scope ?? "空"}。");
            }
            bool systemIndex = IsSystemValueIndex(value.Index);
            if (string.Equals(value.Scope, VariableScopeContract.System, StringComparison.Ordinal))
            {
                if (!systemIndex || value.OwnerProcId.HasValue)
                {
                    throw new InvalidDataException(
                        $"系统变量[{name}]必须位于系统变量区且不能设置OwnerProcId。");
                }
                return;
            }
            if (systemIndex)
            {
                throw new InvalidDataException(
                    $"变量[{name}]位于系统变量区，Scope必须为system。");
            }
            if (string.Equals(value.Scope, VariableScopeContract.Process, StringComparison.Ordinal))
            {
                if (!value.OwnerProcId.HasValue || value.OwnerProcId.Value == Guid.Empty)
                {
                    throw new InvalidDataException($"流程私有变量[{name}]必须设置OwnerProcId。");
                }
                return;
            }
            if (value.OwnerProcId.HasValue)
            {
                throw new InvalidDataException($"公共变量[{name}]不能设置OwnerProcId。");
            }
        }

        public bool TrySetValue(int index, string name, string type, string value, string note, string source = null,
            string scope = VariableScopeContract.Public, Guid? ownerProcId = null)
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
            if (IsSystemValueIndex(index))
            {
                scope = VariableScopeContract.System;
                ownerProcId = null;
            }
            try
            {
                ValidateScope(name, new DicValue
                {
                    Index = index,
                    Scope = scope,
                    OwnerProcId = ownerProcId
                });
            }
            catch (InvalidDataException)
            {
                return false;
            }
            if (string.Equals(type, "double", StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(value) || !double.TryParse(value, out _)))
            {
                return false;
            }
            DicValue changedValue = null;
            string oldRuntime = null;
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
                lock (currentValue.SyncRoot)
                {
                    oldRuntime = currentValue.Value;
                    if (!string.IsNullOrEmpty(currentValue.Name) && currentValue.Name != name)
                    {
                        nameIndex.Remove(currentValue.Name);
                    }
                    currentValue.Name = name;
                    if (currentValue.Id == Guid.Empty)
                    {
                        currentValue.Id = Guid.NewGuid();
                    }
                    currentValue.Index = index;
                    currentValue.Type = type;
                    currentValue.Scope = scope;
                    currentValue.OwnerProcId = ownerProcId;
                    currentValue.Note = note;
                    currentValue.Value = value;
                    nameIndex[name] = index;
                    nameIndexSnapshot = new Dictionary<string, int>(nameIndex);
                    if (!string.Equals(oldRuntime, value, StringComparison.Ordinal))
                    {
                        changedValue = currentValue;
                    }
                }
            }
            if (changedValue != null)
            {
                RaiseValueChanged(changedValue, oldRuntime, value, source);
            }
            return true;
        }

        public bool ClearValueByIndex(int index, string source = null)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return false;
            }
            DicValue changedValue = null;
            string oldRuntime = null;
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
                lock (currentValue.SyncRoot)
                {
                    string oldName = currentValue.Name;
                    oldRuntime = currentValue.Value;
                    if (!string.IsNullOrEmpty(oldName))
                    {
                        nameIndex.Remove(oldName);
                    }
                    currentValue.Name = string.Empty;
                    currentValue.Id = Guid.Empty;
                    currentValue.Type = "double";
                    currentValue.Scope = IsSystemValueIndex(index)
                        ? VariableScopeContract.System
                        : VariableScopeContract.Public;
                    currentValue.OwnerProcId = null;
                    currentValue.Note = string.Empty;
                    currentValue.Value = "0";
                    currentValue.isMark = false;
                    nameIndexSnapshot = new Dictionary<string, int>(nameIndex);
                    if (!string.IsNullOrEmpty(oldRuntime))
                    {
                        changedValue = currentValue;
                    }
                }
            }
            if (changedValue != null)
            {
                RaiseValueChanged(changedValue, oldRuntime, "0", source);
            }
            return true;
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

        private bool TryValidateCurrentValue(DicValue value, string currentValue, out string error)
        {
            error = null;
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                error = "变量不存在";
                return false;
            }
            if (currentValue == null)
            {
                error = "当前值为空";
                return false;
            }
            if (string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(currentValue) || !double.TryParse(currentValue, out _))
                {
                    error = $"变量{value.Name}当前值不是有效数字";
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
            string current;
            string updated;
            lock (value.SyncRoot)
            {
                if (string.IsNullOrEmpty(value.Name))
                {
                    error = $"未找到索引变量:{index}";
                    return false;
                }
                current = value.Value;
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
                if (!TryValidateCurrentValue(value, updated, out error))
                {
                    return false;
                }
                value.Value = updated;
            }
            RaiseValueChanged(value, current, updated, source);
            return true;
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

        public bool SetValueByNameForProcess(
            string key, object newValue, Guid procId, string source = null)
        {
            if (!TryGetValueByNameForProcess(key, procId, out DicValue value))
            {
                return false;
            }
            return setValueByIndex(value.Index, newValue, source);
        }

        public bool SetValueByIndexForProcess(
            int index, object newValue, Guid procId, string source = null)
        {
            if (!TryGetValueByIndexForProcess(index, procId, out _))
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
            string currentValue = newValue.ToString();
            string current;
            lock (value.SyncRoot)
            {
                if (string.IsNullOrEmpty(value.Name))
                {
                    return false;
                }
                current = value.Value;
                if (string.Equals(current, currentValue, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!TryValidateCurrentValue(value, currentValue, out _))
                {
                    return false;
                }
                value.Value = currentValue;
            }
            RaiseValueChanged(value, current, currentValue, source);
            return true;
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
                    Id = value.Id,
                    Index = value.Index,
                    Name = value.Name,
                    Scope = value.Scope,
                    OwnerProcId = value.OwnerProcId,
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
        [JsonIgnore]
        internal object SyncRoot { get; } = new object();

        public Guid Id { get; set; }

        public int Index { get; set; }

        public string Type { get; set; }

        public string Name { get; set; }

        public string Scope { get; set; }

        public Guid? OwnerProcId { get; set; }

        [JsonProperty("Value")]
        public string Value
        {
            get => Volatile.Read(ref currentValue);
            set
            {
                if (double.TryParse(value, out double parsed))
                {
                    Interlocked.Exchange(ref currentDoubleBits, BitConverter.DoubleToInt64Bits(parsed));
                    Volatile.Write(ref hasCurrentDouble, 1);
                }
                else
                {
                    Volatile.Write(ref hasCurrentDouble, 0);
                }
                Volatile.Write(ref currentValue, value);
            }
        }

        [JsonIgnore]
        private string currentValue;

        [JsonIgnore]
        private long currentDoubleBits;

        [JsonIgnore]
        private int hasCurrentDouble;

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

        public double GetDValue()
        {
            if (!string.Equals(Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"变量{DisplayName()}类型不是double");
            }
            if (Volatile.Read(ref hasCurrentDouble) == 1)
            {
                return BitConverter.Int64BitsToDouble(Interlocked.Read(ref currentDoubleBits));
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
        public Guid Id { get; set; }
        public int Index { get; set; }
        public string Name { get; set; }
        public string Scope { get; set; }
        public Guid? OwnerProcId { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Source { get; set; }
        public DateTime ChangedAt { get; set; }
    }
}
