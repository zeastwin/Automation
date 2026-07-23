using System;
// 模块：持久化 / 变量。
// 职责范围：管理变量定义与变量调试配置。
// 状态所有权：变量定义与当前运行值共处一张固定槽位表；配置提交必须保留同一变量的当前值。

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Automation.Protocol;
using Newtonsoft.Json;

namespace Automation
{
    public class ValueConfigStore
    {
        private readonly PlatformRuntime runtime;
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
        private ValueTableState valueTable;
        private readonly object monitorLock = new object();
        private readonly HashSet<Guid> monitoredVariableIds = new HashSet<Guid>();
        private volatile bool monitorEnabled;
        private IDataBreakpointRuntimeSink dataBreakpointSink;
        private long configurationVersion;

        /// <summary>
        /// 变量定义、作用域、常用标记等配置发生变化时递增。
        /// 当前运行值变化不影响该版本，避免 UI 结构缓存被高频运行值反复击穿。
        /// </summary>
        public long ConfigurationVersion => Interlocked.Read(ref configurationVersion);

        internal IDataBreakpointRuntimeSink DataBreakpointSink
        {
            get => Volatile.Read(ref dataBreakpointSink);
            set => Volatile.Write(ref dataBreakpointSink, value);
        }

        private sealed class CurrentValueState
        {
            public Guid Id { get; set; }
            public string Value { get; set; }
            public DateTime LastChangedAt { get; set; }
            public string LastChangedBy { get; set; }
            public string LastChangedOldValue { get; set; }
            public string LastChangedNewValue { get; set; }
        }

        private sealed class ValueTableState
        {
            public ValueTableState(DicValue[] values, Dictionary<string, int> nameIndex)
            {
                Values = values;
                NameIndex = nameIndex;
            }

            public DicValue[] Values { get; }
            public Dictionary<string, int> NameIndex { get; }
        }

        private ValueTableState CurrentTable => Volatile.Read(ref valueTable);

        public event EventHandler<ValueChangedEventArgs> ValueChanged;

        public bool ConfigurationFaulted { get; private set; }

        internal ValueConfigStore(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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

        private static bool HasSameRuntimeContract(DicValue current, DicValue candidate)
        {
            return current != null
                && candidate != null
                && current.Id == candidate.Id
                && current.Index == candidate.Index
                && string.Equals(current.Name, candidate.Name, StringComparison.Ordinal)
                && string.Equals(current.Type, candidate.Type, StringComparison.Ordinal)
                && string.Equals(current.Scope, candidate.Scope, StringComparison.Ordinal)
                && current.OwnerProcId == candidate.OwnerProcId;
        }

        public void SetMonitorFlag(int index, bool isMonitored)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return;
            }
            Guid id;
            lock (valueLock) id = CurrentTable.Values[index]?.Id ?? Guid.Empty;
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
            lock (valueLock) id = CurrentTable.Values[index]?.Id ?? Guid.Empty;
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
                MarkConfigurationChanged();
                if (!Save(configPath))
                {
                    ConfigurationFaulted = true;
                    runtime.Safety.Lock("变量配置初始化保存失败");
                }
                return false;
            }

            try
            {
                Dictionary<string, DicValue> temp = AtomicJsonFileStore.Read<Dictionary<string, DicValue>>(configPath, "value");
                if (temp == null)
                {
                    ConfigurationFaulted = true;
                    runtime.Safety.Lock("变量配置及其备份均无法读取，已保留原文件并禁止继续运行");
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
                runtime.ProcessEngine?.Logger?.Log($"变量配置加载失败:{e}", LogLevel.Error);
                runtime.Safety.Lock($"变量配置加载失败:{e.Message}");
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
                runtime.Safety.Lock("变量配置保存失败，内存与磁盘状态可能不一致");
            }
            return saved;
        }

        private void ResetValues()
        {
            Volatile.Write(ref valueTable, CreateEmptyValueTable());
        }

        private static ValueTableState CreateEmptyValueTable()
        {
            var values = new DicValue[ValueCapacity];
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
            return new ValueTableState(
                values,
                new Dictionary<string, int>(StringComparer.Ordinal));
        }

        private void LoadFromDictionary(
            Dictionary<string, DicValue> source,
            IReadOnlyDictionary<string, CurrentValueState> preservedCurrentValues = null)
        {
            lock (valueLock)
            {
                ValueTableState currentTable = CurrentTable;
                var currentById = currentTable.NameIndex.Values
                    .Select(index => currentTable.Values[index])
                    .Where(value => value != null && value.Id != Guid.Empty)
                    .GroupBy(value => value.Id)
                    .ToDictionary(group => group.Key, group => group.First());
                ValueTableState next = CreateEmptyValueTable();
                foreach (var item in source ?? new Dictionary<string, DicValue>())
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
                    if (!string.IsNullOrEmpty(next.Values[index].Name))
                    {
                        continue;
                    }
                    item.Value.Name = item.Key;
                    if (item.Value.Type != "double" && item.Value.Type != "string")
                    {
                        item.Value.Type = "string";
                    }
                    if (item.Value.Id != Guid.Empty
                        && currentById.TryGetValue(item.Value.Id, out DicValue current)
                        && HasSameRuntimeContract(current, item.Value))
                    {
                        lock (current.SyncRoot)
                        {
                            current.Note = item.Value.Note;
                            current.isMark = item.Value.isMark;
                        }
                        next.Values[index] = current;
                        next.NameIndex[item.Key] = index;
                        continue;
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
                    next.Values[index] = item.Value;
                    next.NameIndex[item.Key] = index;
                }
                // 数组和名称索引作为同一不可分割快照发布，运行线程不会观察到
                // “变量数组已切换、名称索引仍是旧版”或整表暂时为空的中间状态。
                Volatile.Write(ref valueTable, next);
            }
            MarkConfigurationChanged();
        }

        public DicValue GetValueByIndex(int index)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"索引超出范围:{index}");
            }
            DicValue value = CurrentTable.Values[index];
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
            ValueTableState table = CurrentTable;
            if (!table.NameIndex.TryGetValue(key, out int index))
            {
                throw new KeyNotFoundException($"未找到变量:{key}");
            }
            DicValue value = table.Values[index];
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                throw new KeyNotFoundException($"未找到变量:{key}");
            }
            return value;
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

        public bool TryGetValueByIndex(int index, out DicValue value)
        {
            value = null;
            if (index < 0 || index >= ValueCapacity)
            {
                return false;
            }
            value = CurrentTable.Values[index];
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                value = null;
                return false;
            }
            return true;
        }

        public bool TryGetValueById(Guid variableId, out DicValue value)
        {
            value = null;
            if (variableId == Guid.Empty)
            {
                return false;
            }
            ValueTableState table = CurrentTable;
            foreach (int index in table.NameIndex.Values)
            {
                DicValue candidate = table.Values[index];
                if (candidate != null
                    && candidate.Id == variableId
                    && !string.IsNullOrEmpty(candidate.Name))
                {
                    value = candidate;
                    return true;
                }
            }
            return false;
        }

        public bool TryGetValueByName(string key, out DicValue value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            ValueTableState table = CurrentTable;
            if (!table.NameIndex.TryGetValue(key, out int index))
            {
                return false;
            }
            value = table.Values[index];
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                value = null;
                return false;
            }
            return true;
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
            return CurrentTable.NameIndex.Keys.ToList();
        }

        public Dictionary<string, DicValue> BuildSaveData()
        {
            Dictionary<string, DicValue> data = new Dictionary<string, DicValue>();
            lock (valueLock)
            {
                ValueTableState table = CurrentTable;
                foreach (var item in table.NameIndex)
                {
                    DicValue value = table.Values[item.Value];
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
                ValueTableState table = CurrentTable;
                return table.NameIndex.Values
                    .Select(index => table.Values[index])
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
                ValueTableState table = CurrentTable;
                var incomingById = snapshot.Values
                    .Where(value => value != null && value.Id != Guid.Empty)
                    .GroupBy(value => value.Id)
                    .ToDictionary(group => group.Key, group => group.First());
                List<DicValue> changedVariables = table.NameIndex.Values
                    .Select(index => table.Values[index])
                    .Where(current => current != null
                        && current.Id != Guid.Empty
                        && incomingById.TryGetValue(current.Id, out DicValue incoming)
                        && !HasSameRuntimeContract(current, incoming))
                    .OrderBy(current => current.Index)
                    .ToList();
                foreach (DicValue current in changedVariables)
                {
                    Monitor.Enter(current.SyncRoot);
                }
                try
                {
                    foreach (KeyValuePair<string, int> currentEntry in table.NameIndex)
                    {
                        DicValue current = table.Values[currentEntry.Value];
                        if (current == null
                            || current.Id == Guid.Empty
                            || !incomingById.ContainsKey(current.Id))
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
                finally
                {
                    for (int index = changedVariables.Count - 1; index >= 0; index--)
                    {
                        Monitor.Exit(changedVariables[index].SyncRoot);
                    }
                }
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
            string currentValueSource = null,
            string historyDescription = null)
        {
            error = null;
            Dictionary<string, DicValue> snapshot;
            try
            {
                snapshot = CreateValidatedConfigurationSnapshot(source);
                ValidateProcessOwners(snapshot.Values, runtime.Stores.Processes.Items);
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
            if (!runtime.ProcessEditing.CanApplyVariableConfiguration(
                oldConfiguration.Values,
                snapshot.Values,
                out error))
            {
                return false;
            }
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
                UpdateEditorHistory(
                    configPath,
                    historyDescription,
                    oldConfiguration,
                    snapshot);
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
                    runtime.Safety.Lock(error);
                }
                return false;
            }
        }

        private void UpdateEditorHistory(
            string configPath,
            string historyDescription,
            IDictionary<string, DicValue> before,
            IDictionary<string, DicValue> after)
        {
            if (runtime.Editor.History.IsReplaying)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(historyDescription))
            {
                runtime.Editor.History.Clear();
                return;
            }

            Dictionary<string, DicValue> beforeSnapshot = before.ToDictionary(
                item => item.Key,
                item => ObjectGraphCloner.Clone(item.Value),
                StringComparer.Ordinal);
            Dictionary<string, DicValue> afterSnapshot = after.ToDictionary(
                item => item.Key,
                item => ObjectGraphCloner.Clone(item.Value),
                StringComparer.Ordinal);
            runtime.Editor.History.Record(
                historyDescription,
                delegate(out string historyError)
                {
                    return TryRestoreConfiguration(
                        configPath, beforeSnapshot, out historyError);
                },
                delegate(out string historyError)
                {
                    return TryRestoreConfiguration(
                        configPath, afterSnapshot, out historyError);
                });
        }

        private bool TryRestoreConfiguration(
            string configPath,
            IDictionary<string, DicValue> snapshot,
            out string error)
        {
            if (!runtime.ProcessEditing.CanEditVariableConfiguration())
            {
                error = "变量配置当前不可编辑。";
                return false;
            }
            bool success = TryCommitConfiguration(
                configPath,
                snapshot,
                out error);
            if (success)
            {
                runtime.EditorUi?.RefreshVariables();
            }
            return success;
        }

        private Dictionary<string, CurrentValueState> CaptureCurrentValueStates()
        {
            var snapshot = new Dictionary<string, CurrentValueState>(StringComparer.Ordinal);
            lock (valueLock)
            {
                ValueTableState table = CurrentTable;
                foreach (KeyValuePair<string, int> item in table.NameIndex)
                {
                    DicValue value = table.Values[item.Value];
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
                DicValue[] values = CurrentTable.Values;
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
            long changeSequence = 0;
            bool configurationChanged = false;
            lock (valueLock)
            {
                ValueTableState table = CurrentTable;
                if (table.NameIndex.TryGetValue(name, out int existIndex) && existIndex != index)
                {
                    return false;
                }

                DicValue currentValue = table.Values[index];
                if (ProtectedValueNames.Contains(currentValue.Name)
                    && (!string.Equals(currentValue.Name, name, StringComparison.Ordinal)
                        || !string.Equals(type, "double", StringComparison.Ordinal)))
                {
                    return false;
                }
                lock (currentValue.SyncRoot)
                {
                    oldRuntime = currentValue.Value;
                    configurationChanged = !string.Equals(currentValue.Name, name, StringComparison.Ordinal)
                        || !string.Equals(currentValue.Type, type, StringComparison.Ordinal)
                        || !string.Equals(currentValue.Scope, scope, StringComparison.Ordinal)
                        || currentValue.OwnerProcId != ownerProcId
                        || !string.Equals(currentValue.Note, note, StringComparison.Ordinal);
                    DicValue replacement = ObjectGraphCloner.Clone(currentValue);
                    var nextNames = new Dictionary<string, int>(
                        table.NameIndex,
                        StringComparer.Ordinal);
                    if (!string.IsNullOrEmpty(replacement.Name) && replacement.Name != name)
                    {
                        nextNames.Remove(replacement.Name);
                    }
                    replacement.Name = name;
                    if (replacement.Id == Guid.Empty)
                    {
                        replacement.Id = Guid.NewGuid();
                    }
                    replacement.Index = index;
                    replacement.Type = type;
                    replacement.Scope = scope;
                    replacement.OwnerProcId = ownerProcId;
                    replacement.Note = note;
                    replacement.Value = value;
                    nextNames[name] = index;
                    if (!string.Equals(oldRuntime, value, StringComparison.Ordinal))
                    {
                        changedValue = replacement;
                        changeSequence = replacement.AdvanceChangeSequenceNoLock();
                    }
                    var nextValues = (DicValue[])table.Values.Clone();
                    nextValues[index] = replacement;
                    Volatile.Write(
                        ref valueTable,
                        new ValueTableState(nextValues, nextNames));
                }
            }
            if (configurationChanged)
            {
                MarkConfigurationChanged();
            }
            if (changedValue != null)
            {
                RaiseValueChanged(
                    changedValue, oldRuntime, value, source, changeSequence);
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
                return CurrentTable.Values[index].isMark;
            }
        }

        public void ClearMarks()
        {
            bool changed = false;
            lock (valueLock)
            {
                DicValue[] values = CurrentTable.Values;
                for (int i = 0; i < ValueCapacity; i++)
                {
                    changed |= values[i].isMark;
                    values[i].isMark = false;
                }
            }
            if (changed)
            {
                MarkConfigurationChanged();
            }
        }

        public double get_D_ValueByIndex(int index)
        {
            DicValue value = GetValueByIndex(index);
            return value.GetDValue();
        }

        private bool TryValidateCurrentValue(DicValue value, string currentValue, out string error)
        {
            return TryValidateCurrentValue(
                value, currentValue, out error, out _, out _);
        }

        private bool TryValidateCurrentValue(
            DicValue value,
            string currentValue,
            out string error,
            out bool hasParsedDouble,
            out double parsedDouble)
        {
            error = null;
            hasParsedDouble = false;
            parsedDouble = 0d;
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
            hasParsedDouble = double.TryParse(currentValue, out parsedDouble);
            if (string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(currentValue) || !hasParsedDouble)
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
            return TryModifyValueByIndexCore(
                index, null, updater, null, null, null,
                false, false, out error, source);
        }

        internal bool TryModifyValueByIndex(
            int index,
            DicValue expected,
            Func<string, string> updater,
            out string error,
            string source = null)
        {
            return TryModifyValueByIndexCore(
                index, expected, updater, null, null, null,
                false, false, out error, source);
        }

        internal bool TryModifyValueByIndex(
            int index,
            DicValue expected,
            string sourceValue,
            string changeValue,
            bool sourceFromCurrent,
            bool changeFromCurrent,
            Func<string, string, string> updater,
            out string error,
            string source = null)
        {
            return TryModifyValueByIndexCore(
                index, expected, null, updater, sourceValue, changeValue,
                sourceFromCurrent, changeFromCurrent, out error, source);
        }

        internal bool TryModifyNumberByIndex(
            int index,
            DicValue expected,
            double sourceValue,
            double changeValue,
            bool sourceFromCurrent,
            bool changeFromCurrent,
            Func<double, double, double> updater,
            out string error,
            string source = null)
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
            DicValue value = CurrentTable.Values[index];
            if (value == null
                || expected != null && !ReferenceEquals(value, expected)
                || string.IsNullOrEmpty(value.Name))
            {
                error = expected == null
                    ? $"未找到索引变量:{index}"
                    : $"预绑定变量已失效:索引{index}";
                return false;
            }
            string current;
            string updated;
            long changeSequence;
            lock (value.SyncRoot)
            {
                if (!ReferenceEquals(CurrentTable.Values[index], value)
                    || string.IsNullOrEmpty(value.Name))
                {
                    error = $"预绑定变量已失效:索引{index}";
                    return false;
                }
                current = value.Value;
                try
                {
                    double currentNumber = value.GetDValue();
                    double result = updater(
                        sourceFromCurrent ? currentNumber : sourceValue,
                        changeFromCurrent ? currentNumber : changeValue);
                    updated = result.ToString();
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
                if (!TryValidateCurrentValue(
                        value,
                        updated,
                        out error,
                        out bool hasParsedDouble,
                        out double parsedDouble))
                {
                    return false;
                }
                value.SetValidatedValue(
                    updated, hasParsedDouble, parsedDouble);
                changeSequence = value.AdvanceChangeSequenceNoLock();
            }
            RaiseValueChanged(
                value, current, updated, source, changeSequence);
            return true;
        }

        private bool TryModifyValueByIndexCore(
            int index,
            DicValue expected,
            Func<string, string> unaryUpdater,
            Func<string, string, string> binaryUpdater,
            string sourceValue,
            string changeValue,
            bool sourceFromCurrent,
            bool changeFromCurrent,
            out string error,
            string source)
        {
            error = null;
            if (index < 0 || index >= ValueCapacity)
            {
                error = $"索引超出范围:{index}";
                return false;
            }
            if (unaryUpdater == null && binaryUpdater == null)
            {
                error = "更新函数为空";
                return false;
            }
            DicValue value = CurrentTable.Values[index];
            if (value == null
                || expected != null && !ReferenceEquals(value, expected)
                || string.IsNullOrEmpty(value.Name))
            {
                error = expected == null
                    ? $"未找到索引变量:{index}"
                    : $"预绑定变量已失效:索引{index}";
                return false;
            }
            string current;
            string updated;
            long changeSequence;
            lock (value.SyncRoot)
            {
                if (!ReferenceEquals(CurrentTable.Values[index], value)
                    || string.IsNullOrEmpty(value.Name))
                {
                    error = expected == null
                        ? $"未找到索引变量:{index}"
                        : $"预绑定变量已失效:索引{index}";
                    return false;
                }
                current = value.Value;
                try
                {
                    updated = binaryUpdater != null
                        ? binaryUpdater(
                            sourceFromCurrent ? current : sourceValue,
                            changeFromCurrent ? current : changeValue)
                        : unaryUpdater(current);
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
                if (!TryValidateCurrentValue(
                        value, updated, out error, out bool hasParsedDouble, out double parsedDouble))
                {
                    return false;
                }
                value.SetValidatedValue(updated, hasParsedDouble, parsedDouble);
                changeSequence = value.AdvanceChangeSequenceNoLock();
            }
            RaiseValueChanged(value, current, updated, source, changeSequence);
            return true;
        }

        public bool setValueByName(string key, object newValue, string source = null)
        {
            if (string.IsNullOrEmpty(key) || newValue == null)
            {
                return false;
            }
            ValueTableState table = CurrentTable;
            if (!table.NameIndex.TryGetValue(key, out int index))
            {
                return false;
            }
            return SetValue(table.Values[index], newValue, source);
        }

        public bool SetValueByNameForProcess(
            string key, object newValue, Guid procId, string source = null)
        {
            if (!TryGetValueByNameForProcess(key, procId, out DicValue value))
            {
                return false;
            }
            return SetResolvedValueForProcess(
                value, newValue, procId, source);
        }

        public bool SetValueByIndexForProcess(
            int index, object newValue, Guid procId, string source = null)
        {
            if (!TryGetValueByIndexForProcess(index, procId, out DicValue value))
            {
                return false;
            }
            return SetResolvedValueForProcess(
                value, newValue, procId, source);
        }

        internal bool SetResolvedValueForProcess(
            DicValue value, object newValue, Guid procId, string source = null)
        {
            if (value == null
                || value.Index < 0
                || value.Index >= ValueCapacity
                || !ReferenceEquals(CurrentTable.Values[value.Index], value)
                || !CanProcessAccess(value, procId))
            {
                return false;
            }
            return SetValue(value, newValue, source);
        }

        public bool setValueByIndex(int index, object newValue, string source = null)
        {
            if (index < 0 || index >= ValueCapacity || newValue == null)
            {
                return false;
            }
            DicValue value = CurrentTable.Values[index];
            return SetValue(value, newValue, source);
        }

        private bool SetValue(DicValue value, object newValue, string source)
        {
            if (value == null || string.IsNullOrEmpty(value.Name) || newValue == null)
            {
                return false;
            }
            string currentValue = newValue.ToString();
            string current;
            long changeSequence;
            lock (value.SyncRoot)
            {
                if (value.Index < 0
                    || value.Index >= ValueCapacity
                    || !ReferenceEquals(CurrentTable.Values[value.Index], value)
                    || string.IsNullOrEmpty(value.Name))
                {
                    return false;
                }
                current = value.Value;
                if (string.Equals(current, currentValue, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!TryValidateCurrentValue(
                        value, currentValue, out _, out bool hasParsedDouble, out double parsedDouble))
                {
                    return false;
                }
                value.SetValidatedValue(currentValue, hasParsedDouble, parsedDouble);
                changeSequence = value.AdvanceChangeSequenceNoLock();
            }
            RaiseValueChanged(
                value, current, currentValue, source, changeSequence);
            return true;
        }

        private void RaiseValueChanged(
            DicValue value,
            string oldValue,
            string newValue,
            string source,
            long changeSequence)
        {
            if (value == null)
            {
                return;
            }
            try
            {
                DataBreakpointSink?.OnVariableChanged(
                    value, oldValue, newValue, source, changeSequence);
            }
            catch
            {
                // 调试辅助能力异常不能改变变量写入结果。
            }
            if (!monitorEnabled || !IsMonitored(value.Index))
            {
                return;
            }
            string resolvedSource = ResolveSource(value.Index, source);
            DateTime time = DateTime.Now;
            lock (value.SyncRoot)
            {
                if (changeSequence >= value.LastChangedSequence)
                {
                    value.LastChangedSequence = changeSequence;
                    value.LastChangedAt = time;
                    value.LastChangedBy = resolvedSource;
                    value.LastChangedOldValue = oldValue;
                    value.LastChangedNewValue = newValue;
                }
            }
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
                    ChangedAt = time,
                    Sequence = changeSequence
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

        private void MarkConfigurationChanged()
        {
            Interlocked.Increment(ref configurationVersion);
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
                bool hasParsedDouble = double.TryParse(value, out double parsedDouble);
                SetValidatedValue(value, hasParsedDouble, parsedDouble);
            }
        }

        internal void SetValidatedValue(
            string value, bool hasParsedDouble, double parsedDouble)
        {
            if (hasParsedDouble)
            {
                Volatile.Write(
                    ref currentDoubleBits, BitConverter.DoubleToInt64Bits(parsedDouble));
                Volatile.Write(ref hasCurrentDouble, 1);
            }
            else
            {
                Volatile.Write(ref hasCurrentDouble, 0);
            }
            Volatile.Write(ref currentValue, value);
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

        [JsonIgnore]
        internal long ChangeSequence { get; set; }

        [JsonIgnore]
        internal long LastChangedSequence { get; set; }

        internal long AdvanceChangeSequenceNoLock()
        {
            ChangeSequence = ChangeSequence == long.MaxValue
                ? 1
                : ChangeSequence + 1;
            return ChangeSequence;
        }

        public double GetDValue()
        {
            if (!string.Equals(Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"变量{DisplayName()}类型不是double");
            }
            if (Volatile.Read(ref hasCurrentDouble) == 1)
            {
                return BitConverter.Int64BitsToDouble(Volatile.Read(ref currentDoubleBits));
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
        public long Sequence { get; set; }
    }
}
