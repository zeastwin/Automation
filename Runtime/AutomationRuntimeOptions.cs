using System;
using System.IO;

namespace Automation
{
    public enum AutomationRuntimeMode
    {
        Hardware = 0,
        Simulation = 1,
        SimulationTest = 2
    }

    public sealed class AutomationRuntimeOptions
    {
        private static readonly object sync = new object();
        private static AutomationRuntimeOptions current = new AutomationRuntimeOptions(AutomationRuntimeMode.Hardware, false, null, null);

        private AutomationRuntimeOptions(AutomationRuntimeMode mode, bool platformOnly, string scenarioName, string sessionRoot)
        {
            Mode = mode;
            PlatformOnly = platformOnly;
            ScenarioName = scenarioName;
            SessionRoot = sessionRoot;
        }

        public AutomationRuntimeMode Mode { get; }
        public bool PlatformOnly { get; }
        public string ScenarioName { get; }
        public string SessionRoot { get; }
        public bool IsSimulation => Mode != AutomationRuntimeMode.Hardware;
        public static AutomationRuntimeOptions Current { get { lock (sync) return current; } }

        public static bool TryConfigure(string[] args, out AutomationRuntimeOptions options, out string error)
        {
            options = null;
            error = null;
            AutomationRuntimeMode mode;
            bool platformOnly;
            string scenario = null;
            if (args == null || args.Length == 0)
            {
                mode = AutomationRuntimeMode.Hardware;
                platformOnly = false;
            }
            else if (args.Length == 1 && string.Equals(args[0], "--platform", StringComparison.Ordinal))
            {
                mode = AutomationRuntimeMode.Hardware;
                platformOnly = true;
            }
            else if (args.Length == 2 && string.Equals(args[0], "--platform", StringComparison.Ordinal) && string.Equals(args[1], "--simulation", StringComparison.Ordinal))
            {
                mode = AutomationRuntimeMode.Simulation;
                platformOnly = true;
            }
            else if (args.Length == 2 && string.Equals(args[0], "--simulation-test", StringComparison.Ordinal))
            {
                scenario = args[1];
                if (!IsValidScenarioName(scenario))
                {
                    error = $"仿真场景名称无效:{scenario}";
                    return false;
                }
                mode = AutomationRuntimeMode.SimulationTest;
                platformOnly = true;
            }
            else
            {
                error = "启动参数无效。仅支持无参数、--platform、--platform --simulation 或 --simulation-test <场景名>。";
                return false;
            }

            string sessionRoot = null;
            if (mode != AutomationRuntimeMode.Hardware)
            {
                try
                {
                    string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    string sourceConfig = Path.Combine(baseDirectory, "Config");
                    if (!Directory.Exists(sourceConfig))
                    {
                        error = $"正式配置目录不存在:{sourceConfig}";
                        return false;
                    }
                    sessionRoot = Path.Combine(baseDirectory, "SimulationRuns", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N"));
                    string sessionConfig = Path.Combine(sessionRoot, "Config");
                    CopyDirectory(sourceConfig, sessionConfig);
                    SF.ConfigPath = EnsureTrailingSeparator(sessionConfig);
                    SF.workPath = EnsureTrailingSeparator(Path.Combine(sessionConfig, "Work"));
                }
                catch (Exception ex)
                {
                    error = $"创建仿真配置副本失败:{ex.Message}";
                    return false;
                }
            }
            options = new AutomationRuntimeOptions(mode, platformOnly, scenario, sessionRoot);
            lock (sync) current = options;
            return true;
        }

        public static string ActiveConfigFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("配置文件名为空", nameof(fileName));
            return Path.Combine(Current.IsSimulation ? SF.ConfigPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config"), fileName);
        }

        private static bool IsValidScenarioName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains("..") || value.Contains("/") || value.Contains("\\")) return false;
            return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
            foreach (string directory in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(directory);
                if (string.Equals(name, ".AutomationVersions", StringComparison.OrdinalIgnoreCase)) continue;
                CopyDirectory(directory, Path.Combine(destination, name));
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            string full = Path.GetFullPath(path);
            return full.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? full : full + Path.DirectorySeparatorChar;
        }
    }
}
