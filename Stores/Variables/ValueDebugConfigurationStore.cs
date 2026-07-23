using System;
// 模块：持久化 / 变量。
// 职责范围：管理变量定义与变量调试配置。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation
{
    public sealed class ValueDebugConfiguration
    {
        public List<Guid> CheckVariableIds { get; set; } = new List<Guid>();
        public List<Guid> EditVariableIds { get; set; } = new List<Guid>();
        public Dictionary<Guid, string> Notes { get; set; } = new Dictionary<Guid, string>();
    }

    /// <summary>
    /// 变量调试布局配置的唯一持久化与校验入口。
    /// </summary>
    public sealed class ValueDebugConfigurationStore
    {
        private const string ConfigName = "value_debug";
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };
        private readonly object syncRoot = new object();
        private ValueDebugConfiguration current = new ValueDebugConfiguration();
        private long version;

        public long Version => Interlocked.Read(ref version);

        public ValueDebugConfiguration GetSnapshot(out long currentVersion)
        {
            lock (syncRoot)
            {
                currentVersion = version;
                return ObjectGraphCloner.Clone(current);
            }
        }

        public bool Load(string configPath, ValueConfigStore valueStore, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(configPath))
            {
                error = "变量调试配置目录无效。";
                return false;
            }
            Directory.CreateDirectory(configPath);
            string filePath = Path.Combine(configPath, ConfigName + ".json");
            if (!File.Exists(filePath) && !File.Exists(filePath + ".bak"))
            {
                return TryCommit(
                    configPath,
                    new ValueDebugConfiguration(),
                    Version,
                    out error);
            }

            JObject document = AtomicJsonFileStore.Read<JObject>(
                configPath,
                ConfigName,
                JsonSettings);
            if (document == null)
            {
                error = "变量调试配置及其备份均无法读取。";
                return false;
            }
            if (!TryDeserialize(
                document,
                valueStore,
                out ValueDebugConfiguration loaded,
                out bool migrated,
                out error)
                || !TryValidate(loaded, out error))
            {
                return false;
            }
            if (migrated)
            {
                return TryCommit(configPath, loaded, Version, out error);
            }
            Publish(loaded);
            return true;
        }

        public bool TryCommit(
            string configPath,
            ValueDebugConfiguration candidate,
            long expectedVersion,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                error = "变量调试配置目录无效。";
                return false;
            }
            if (!TryValidate(candidate, out error))
            {
                return false;
            }
            ValueDebugConfiguration snapshot = ObjectGraphCloner.Clone(candidate);
            lock (syncRoot)
            {
                if (version != expectedVersion)
                {
                    error = "变量调试配置已被其他操作更新，请刷新后重试。";
                    return false;
                }
                try
                {
                    if (!AtomicJsonFileStore.Save(
                        configPath,
                        ConfigName,
                        snapshot,
                        JsonSettings))
                    {
                        error = "变量调试配置保存失败。";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = $"变量调试配置保存失败：{ex.Message}";
                    return false;
                }
                current = snapshot;
                Interlocked.Increment(ref version);
            }
            return true;
        }

        private static bool TryValidate(
            ValueDebugConfiguration config,
            out string error)
        {
            error = null;
            if (config == null)
            {
                error = "变量调试配置为空。";
                return false;
            }
            if (config.CheckVariableIds == null
                || config.EditVariableIds == null
                || config.Notes == null)
            {
                error = "变量调试配置字段缺失。";
                return false;
            }
            if (!TryValidateVariableIds(config.CheckVariableIds, out error)
                || !TryValidateVariableIds(config.EditVariableIds, out error))
            {
                return false;
            }

            var allowedVariableIds = new HashSet<Guid>(config.CheckVariableIds);
            allowedVariableIds.UnionWith(config.EditVariableIds);
            foreach (KeyValuePair<Guid, string> item in config.Notes)
            {
                if (!allowedVariableIds.Contains(item.Key))
                {
                    error = $"备注变量未在调试列表中:{item.Key:D}";
                    return false;
                }
                if (item.Value == null)
                {
                    error = $"备注为空:{item.Key:D}";
                    return false;
                }
            }
            return true;
        }

        private static bool TryValidateVariableIds(
            IEnumerable<Guid> variableIds,
            out string error)
        {
            error = null;
            var uniqueVariableIds = new HashSet<Guid>();
            foreach (Guid variableId in variableIds)
            {
                if (variableId == Guid.Empty)
                {
                    error = "变量调试项稳定ID为空。";
                    return false;
                }
                if (!uniqueVariableIds.Add(variableId))
                {
                    error = $"变量调试项重复:{variableId:D}";
                    return false;
                }
            }
            return true;
        }

        private static bool TryDeserialize(
            JObject document,
            ValueConfigStore valueStore,
            out ValueDebugConfiguration configuration,
            out bool migrated,
            out string error)
        {
            configuration = null;
            migrated = false;
            error = null;
            if (document["CheckVariableIds"] != null
                || document["EditVariableIds"] != null)
            {
                configuration = document.ToObject<ValueDebugConfiguration>(
                    JsonSerializer.Create(JsonSettings));
                return true;
            }
            if (document["CheckIndexes"] == null
                && document["EditIndexes"] == null)
            {
                error = "变量调试配置字段缺失。";
                return false;
            }
            if (valueStore == null)
            {
                error = "变量库未初始化，无法迁移变量调试配置。";
                return false;
            }

            List<int> checkIndexes = document["CheckIndexes"]?.ToObject<List<int>>()
                ?? new List<int>();
            List<int> editIndexes = document["EditIndexes"]?.ToObject<List<int>>()
                ?? new List<int>();
            Dictionary<int, string> notes =
                document["Notes"]?.ToObject<Dictionary<int, string>>()
                ?? new Dictionary<int, string>();
            var migratedConfiguration = new ValueDebugConfiguration();
            var variableIdsByIndex = new Dictionary<int, Guid>();
            foreach (int index in checkIndexes.Concat(editIndexes).Distinct())
            {
                if (!valueStore.TryGetValueByIndex(index, out DicValue value)
                    || value.Id == Guid.Empty)
                {
                    error = $"旧变量调试配置引用的变量不存在:{index:D3}";
                    return false;
                }
                variableIdsByIndex[index] = value.Id;
            }
            migratedConfiguration.CheckVariableIds = checkIndexes
                .Select(index => variableIdsByIndex[index])
                .ToList();
            migratedConfiguration.EditVariableIds = editIndexes
                .Select(index => variableIdsByIndex[index])
                .ToList();
            foreach (KeyValuePair<int, string> note in notes)
            {
                if (!variableIdsByIndex.TryGetValue(note.Key, out Guid variableId))
                {
                    error = $"旧变量调试备注索引未在调试列表中:{note.Key:D3}";
                    return false;
                }
                migratedConfiguration.Notes[variableId] = note.Value;
            }
            configuration = migratedConfiguration;
            migrated = true;
            return true;
        }

        private void Publish(ValueDebugConfiguration configuration)
        {
            ValueDebugConfiguration snapshot = ObjectGraphCloner.Clone(configuration);
            lock (syncRoot)
            {
                current = snapshot;
                Interlocked.Increment(ref version);
            }
        }
    }
}
