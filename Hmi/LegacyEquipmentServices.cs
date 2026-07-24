using System;
using System.IO;
using System.Text;
using Automation.DeviceSdk;

// 模块：设备项目适配 / 旧项目服务组合。
// 职责范围：集中持有视频、数据库和设备项目默认配置，供 HMI 与自定义函数共享。
// 排查入口：设备项目能力不可用时检查 Config\EquipmentProject 和 InitializationWarning。

namespace Automation.Hmi
{
    internal sealed class LegacyEquipmentServices : IDisposable
    {
        internal LegacyEquipmentServices(IValueStore values, string configRoot)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            if (string.IsNullOrWhiteSpace(configRoot))
            {
                throw new ArgumentException("平台配置目录不能为空。", nameof(configRoot));
            }

            ProjectConfigurationRoot = Path.Combine(
                Path.GetFullPath(configRoot),
                "EquipmentProject");
            Video = new LegacyVideoService();
            Database = new LegacyDatabaseService(values);
            try
            {
                EnsureDefaultConfiguration(ProjectConfigurationRoot);
            }
            catch (Exception ex)
            {
                // 缺少设备项目辅助配置不能阻止 Automation 与 HMI 初始化。
                InitializationWarning = ex.Message;
            }
        }

        internal ILegacyVideoService Video { get; }

        internal LegacyDatabaseService Database { get; }

        internal string ProjectConfigurationRoot { get; }

        internal string InitializationWarning { get; }

        public void Dispose()
        {
            Video.Dispose();
        }

        private static void EnsureDefaultConfiguration(string root)
        {
            Directory.CreateDirectory(root);
            string alarmDirectory = Path.Combine(root, "AlarmConfig");
            Directory.CreateDirectory(alarmDirectory);
            WriteIfMissing(
                Path.Combine(alarmDirectory, "AlarmInfo.csv"),
                "AlarmCode,AlarmType,AlarmMsg,AlarmNoteCN,AlarmNoteEN,"
                + "AlarmNoteOT,PLCAddress,PLCBit,PLCIgnore,Enabled"
                + Environment.NewLine);
            WriteIfMissing(
                Path.Combine(root, "EquipmentProject.json"),
                "{"
                + Environment.NewLine
                + "  \"schemaVersion\": 1,"
                + Environment.NewLine
                + "  \"name\": \"JS_ICT_NPI_LAX_XXXX\","
                + Environment.NewLine
                + "  \"alarmConfig\": \"AlarmConfig\\\\AlarmInfo.csv\","
                + Environment.NewLine
                + "  \"description\": \"旧设备项目迁移配置；缺失项可在 Automation 中补充。\""
                + Environment.NewLine
                + "}"
                + Environment.NewLine);
        }

        private static void WriteIfMissing(string path, string content)
        {
            if (File.Exists(path))
            {
                return;
            }
            File.WriteAllText(path, content, new UTF8Encoding(true));
        }
    }
}
