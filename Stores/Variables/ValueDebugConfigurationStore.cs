using System;
// 模块：持久化 / 变量。
// 职责范围：管理变量定义与变量调试配置。

using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;

namespace Automation
{
    public sealed class ValueDebugConfiguration
    {
        public List<int> CheckIndexes { get; set; } = new List<int>();
        public List<int> EditIndexes { get; set; } = new List<int>();
        public Dictionary<int, string> Notes { get; set; } = new Dictionary<int, string>();
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

        public ValueDebugConfiguration Current { get; private set; } = new ValueDebugConfiguration();
        private long version;

        public long Version => Interlocked.Read(ref version);

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
                return TryCommit(configPath, new ValueDebugConfiguration(), valueStore, out error);
            }

            ValueDebugConfiguration loaded = AtomicJsonFileStore.Read<ValueDebugConfiguration>(
                configPath,
                ConfigName,
                JsonSettings);
            if (!TryValidate(loaded, valueStore, out error))
            {
                return false;
            }
            Current = ObjectGraphCloner.Clone(loaded);
            Interlocked.Increment(ref version);
            return true;
        }

        public bool TryCommit(
            string configPath,
            ValueDebugConfiguration candidate,
            ValueConfigStore valueStore,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                error = "变量调试配置目录无效。";
                return false;
            }
            if (!TryValidate(candidate, valueStore, out error))
            {
                return false;
            }
            ValueDebugConfiguration snapshot = ObjectGraphCloner.Clone(candidate);
            if (!AtomicJsonFileStore.Save(configPath, ConfigName, snapshot, JsonSettings))
            {
                error = "变量调试配置保存失败。";
                return false;
            }
            Current = snapshot;
            Interlocked.Increment(ref version);
            return true;
        }

        private static bool TryValidate(
            ValueDebugConfiguration config,
            ValueConfigStore valueStore,
            out string error)
        {
            error = null;
            if (config == null)
            {
                error = "变量调试配置为空。";
                return false;
            }
            if (config.CheckIndexes == null || config.EditIndexes == null || config.Notes == null)
            {
                error = "变量调试配置字段缺失。";
                return false;
            }
            if (valueStore == null)
            {
                error = "变量库未初始化。";
                return false;
            }
            if (!TryValidateIndexes(config.CheckIndexes, valueStore, out error)
                || !TryValidateIndexes(config.EditIndexes, valueStore, out error))
            {
                return false;
            }

            var allowedIndexes = new HashSet<int>(config.CheckIndexes);
            allowedIndexes.UnionWith(config.EditIndexes);
            foreach (KeyValuePair<int, string> item in config.Notes)
            {
                if (!allowedIndexes.Contains(item.Key))
                {
                    error = $"备注索引未在调试列表中:{item.Key:D3}";
                    return false;
                }
                if (item.Value == null)
                {
                    error = $"备注为空:{item.Key:D3}";
                    return false;
                }
            }
            return true;
        }

        private static bool TryValidateIndexes(
            IEnumerable<int> indexes,
            ValueConfigStore valueStore,
            out string error)
        {
            error = null;
            var uniqueIndexes = new HashSet<int>();
            foreach (int index in indexes)
            {
                if (index < 0 || index >= ValueConfigStore.ValueCapacity)
                {
                    error = $"索引超出范围:{index}";
                    return false;
                }
                if (!valueStore.TryGetValueByIndex(index, out _))
                {
                    error = $"变量不存在:{index:D3}";
                    return false;
                }
                if (!uniqueIndexes.Add(index))
                {
                    error = $"索引重复:{index:D3}";
                    return false;
                }
            }
            return true;
        }
    }
}
