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
            AutomationRuntimeMode.Hardware, ProcessExecutionMode.Normal, true);

        private AutomationRuntimeOptions(AutomationRuntimeMode mode, ProcessExecutionMode processExecutionMode,
            bool performanceAnalysisEnabled)
        {
            Mode = mode;
            ProcessExecutionMode = processExecutionMode;
            PerformanceAnalysisEnabled = performanceAnalysisEnabled;
        }

        public AutomationRuntimeMode Mode { get; }
        public bool IsSimulation => Mode != AutomationRuntimeMode.Hardware;
        public ProcessExecutionMode ProcessExecutionMode { get; }
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
            if (appConfig.ProcessExecutionMode != ProcessExecutionMode.Normal
                && appConfig.ProcessExecutionMode != ProcessExecutionMode.HighPerformance)
            {
                error = "程序设置中的流程执行模式无效。";
                return false;
            }

            options = new AutomationRuntimeOptions(
                mode, appConfig.ProcessExecutionMode, appConfig.EnablePerformanceAnalysis);
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
