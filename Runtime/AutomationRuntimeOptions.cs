using System;
using System.IO;

namespace Automation
{
    public enum AutomationRuntimeMode
    {
        Hardware = 0,
        Simulation = 1
    }

    public sealed class AutomationRuntimeOptions
    {
        private static readonly object sync = new object();
        private static AutomationRuntimeOptions current = new AutomationRuntimeOptions(
            AutomationRuntimeMode.Hardware, true);

        private AutomationRuntimeOptions(AutomationRuntimeMode mode, bool performanceAnalysisEnabled)
        {
            Mode = mode;
            PerformanceAnalysisEnabled = performanceAnalysisEnabled;
        }

        public AutomationRuntimeMode Mode { get; }
        public bool IsSimulation => Mode != AutomationRuntimeMode.Hardware;
        public bool PerformanceAnalysisEnabled { get; }
        public static AutomationRuntimeOptions Current { get { lock (sync) return current; } }

        public static bool TryConfigure(string[] args, AppConfig appConfig, out AutomationRuntimeOptions options, out string error)
        {
            options = null;
            error = null;
            if (args != null && args.Length != 0)
            {
                error = "启动参数无效。请在平台的“程序设置”中选择正常模式或仿真模式。";
                return false;
            }
            if (appConfig == null || (appConfig.RuntimeMode != AutomationRuntimeMode.Hardware && appConfig.RuntimeMode != AutomationRuntimeMode.Simulation))
            {
                error = "程序设置中的运行模式无效。";
                return false;
            }
            AutomationRuntimeMode mode = appConfig.RuntimeMode;
            options = new AutomationRuntimeOptions(mode, appConfig.EnablePerformanceAnalysis);
            lock (sync) current = options;
            return true;
        }

        public static string ActiveConfigFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("配置文件名为空", nameof(fileName));
            return Path.Combine(SF.ConfigPath, fileName);
        }
    }
}
