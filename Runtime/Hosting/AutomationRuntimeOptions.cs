using System;
// 模块：运行时 / 宿主组合。
// 职责范围：负责平台入口、实例组合、初始化、路径和宿主对外生命周期。

using System.IO;

namespace Automation
{
    public sealed class AutomationRuntimeOptions
    {
        private static readonly object sync = new object();
        private static AutomationRuntimeOptions current = new AutomationRuntimeOptions(true);

        private AutomationRuntimeOptions(bool performanceAnalysisEnabled)
        {
            PerformanceAnalysisEnabled = performanceAnalysisEnabled;
        }

        public bool PerformanceAnalysisEnabled { get; }
        public static AutomationRuntimeOptions Current { get { lock (sync) return current; } }

        public static bool TryConfigure(string[] args, AppConfig appConfig, out AutomationRuntimeOptions options, out string error)
        {
            options = null;
            error = null;
            if (args != null && args.Length != 0)
            {
                error = "启动参数无效。程序启动界面请在“程序设置”中选择。";
                return false;
            }
            if (appConfig == null)
            {
                error = "程序设置为空。";
                return false;
            }
            options = new AutomationRuntimeOptions(appConfig.EnablePerformanceAnalysis);
            lock (sync) current = options;
            return true;
        }

        public static string ActiveConfigFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("配置文件名为空", nameof(fileName));
            return Path.Combine(new PlatformPaths().ConfigPath, fileName);
        }
    }
}
