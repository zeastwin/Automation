using System;
// 模块：运行时 / 宿主组合。
// 职责范围：负责平台入口、实例组合、初始化、路径和宿主对外生命周期。

using System.IO;

namespace Automation
{
    /// <summary>
    /// 当前平台实例使用的配置路径。路径在组合根创建后保持不变。
    /// </summary>
    public sealed class PlatformPaths
    {
        public PlatformPaths(string configPath = null)
        {
            string executableDirectory = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            string root = string.IsNullOrWhiteSpace(configPath)
                ? Path.Combine(executableDirectory, "Config")
                : configPath;
            ConfigPath = EnsureTrailingSeparator(Path.GetFullPath(root));
            WorkPath = EnsureTrailingSeparator(Path.Combine(ConfigPath, "Work"));
        }

        public string ConfigPath { get; }

        public string WorkPath { get; }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
