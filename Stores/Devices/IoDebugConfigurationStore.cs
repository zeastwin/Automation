using System.IO;

// 模块：持久化 / 设备配置。
// 职责范围：管理控制卡、通讯、PLC、IO、工站和点位配置，不执行设备动作。

namespace Automation
{
    /// <summary>
    /// IO 调试布局配置的唯一持久化入口。
    /// </summary>
    public sealed class IoDebugConfigurationStore
    {
        public IODebugMap Current { get; private set; } = new IODebugMap();

        public bool Load(string configPath, out string error)
        {
            error = null;
            Directory.CreateDirectory(configPath);
            string filePath = Path.Combine(configPath, "IODebugMap.json");
            if (!File.Exists(filePath))
            {
                return TryCommit(configPath, new IODebugMap(), out error);
            }
            IODebugMap loaded = AtomicJsonFileStore.Read<IODebugMap>(configPath, "IODebugMap");
            if (loaded == null)
            {
                error = "IO调试配置主文件及备份均无法读取。";
                return false;
            }
            Current = loaded;
            return true;
        }

        public bool TryCommit(string configPath, IODebugMap candidate, out string error)
        {
            error = null;
            if (candidate == null)
            {
                error = "IO调试配置为空。";
                return false;
            }
            IODebugMap snapshot = ObjectGraphCloner.Clone(candidate);
            if (!AtomicJsonFileStore.Save(configPath, "IODebugMap", snapshot))
            {
                error = "IO调试配置保存失败。";
                return false;
            }
            Current = snapshot;
            return true;
        }
    }
}
