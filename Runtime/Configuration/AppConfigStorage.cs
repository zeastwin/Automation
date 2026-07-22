using System;
// 模块：运行时 / 配置协调。
// 职责范围：处理应用配置、序列化边界、配置版本和 HMI 开发源码定位。

using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation
{
    public enum AutomationStartupView
    {
        Hmi = 0,
        PlatformEditor = 1
    }

    public sealed class AppConfig
    {
        public int CommMaxMessageQueueSize { get; set; }
        public AutomationRuntimeMode RuntimeMode { get; set; }
        public AutomationStartupView StartupView { get; set; }
        public bool EnablePerformanceAnalysis { get; set; }
        public bool EnableRuntimeDiagnostics { get; set; }
    }

    public static class AppConfigStorage
    {
        public const string ConfigFolderName = "Config";
        public const string ConfigFileName = "AppConfig.json";
        public const string CommMaxMessageQueueSizeKey = "CommMaxMessageQueueSize";
        public const string RuntimeModeKey = "RuntimeMode";
        public const string StartupViewKey = "StartupView";
        public const string EnablePerformanceAnalysisKey = "EnablePerformanceAnalysis";
        public const string EnableRuntimeDiagnosticsKey = "EnableRuntimeDiagnostics";
        public const int DefaultCommMaxMessageQueueSize = 1000;
        private static readonly object cacheLock = new object();
        private static AppConfig cachedConfig;

        public static string ConfigPath => AutomationRuntimeOptions.ActiveConfigFile(ConfigFileName);

        public static bool TryLoad(out AppConfig config, out string error)
        {
            config = null;
            error = null;
            string path = ConfigPath;
            if (!File.Exists(path))
            {
                AppConfig defaultConfig = new AppConfig
                {
                    CommMaxMessageQueueSize = DefaultCommMaxMessageQueueSize,
                    RuntimeMode = AutomationRuntimeMode.Hardware,
                    StartupView = AutomationStartupView.Hmi,
                    EnablePerformanceAnalysis = true,
                    EnableRuntimeDiagnostics = true
                };
                if (!TrySave(defaultConfig, out string saveError))
                {
                    error = $"默认程序配置生成失败:{saveError}";
                    return false;
                }
                config = Clone(defaultConfig);
                return true;
            }
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                JObject obj = JObject.Parse(json);
                bool saveRequired = false;
                if (!obj.TryGetValue(CommMaxMessageQueueSizeKey, StringComparison.Ordinal, out JToken token))
                {
                    token = new JValue(DefaultCommMaxMessageQueueSize);
                    saveRequired = true;
                }
                if (token.Type != JTokenType.Integer)
                {
                    error = $"队列长度类型无效:{token}";
                    return false;
                }
                int value = token.Value<int>();
                if (value <= 0)
                {
                    error = $"队列长度配置无效:{value}";
                    return false;
                }
                if (!obj.TryGetValue(RuntimeModeKey, StringComparison.Ordinal, out token))
                {
                    token = new JValue((int)AutomationRuntimeMode.Hardware);
                    saveRequired = true;
                }
                if (token.Type != JTokenType.Integer || (token.Value<int>() != (int)AutomationRuntimeMode.Hardware && token.Value<int>() != (int)AutomationRuntimeMode.Simulation))
                {
                    error = $"运行模式配置无效:{token}";
                    return false;
                }
                AutomationRuntimeMode runtimeMode = (AutomationRuntimeMode)token.Value<int>();
                if (!obj.TryGetValue(StartupViewKey, StringComparison.Ordinal, out token))
                {
                    token = new JValue((int)AutomationStartupView.Hmi);
                    saveRequired = true;
                }
                if (token.Type != JTokenType.Integer
                    || (token.Value<int>() != (int)AutomationStartupView.Hmi
                        && token.Value<int>() != (int)AutomationStartupView.PlatformEditor))
                {
                    error = $"启动界面配置无效:{token}";
                    return false;
                }
                AutomationStartupView startupView = (AutomationStartupView)token.Value<int>();
                if (!obj.TryGetValue(EnablePerformanceAnalysisKey, StringComparison.Ordinal, out token))
                {
                    token = new JValue(true);
                    saveRequired = true;
                }
                if (token.Type != JTokenType.Boolean)
                {
                    error = $"性能分析开关配置无效:{token}";
                    return false;
                }
                bool enablePerformanceAnalysis = token.Value<bool>();
                if (!obj.TryGetValue(EnableRuntimeDiagnosticsKey, StringComparison.Ordinal, out token))
                {
                    token = new JValue(true);
                    saveRequired = true;
                }
                if (token.Type != JTokenType.Boolean)
                {
                    error = $"智能诊断开关配置无效:{token}";
                    return false;
                }
                bool enableRuntimeDiagnostics = token.Value<bool>();
                config = new AppConfig
                {
                    CommMaxMessageQueueSize = value,
                    RuntimeMode = runtimeMode,
                    StartupView = startupView,
                    EnablePerformanceAnalysis = enablePerformanceAnalysis,
                    EnableRuntimeDiagnostics = enableRuntimeDiagnostics
                };
                if (saveRequired && !TrySave(config, out string saveError))
                {
                    error = $"补全程序配置失败:{saveError}";
                    config = null;
                    return false;
                }
                SetCache(config);
                return true;
            }
            catch (Exception ex)
            {
                error = $"读取配置失败:{ex.Message}";
                return false;
            }
        }

        public static bool TryGetCached(out AppConfig config, out string error)
        {
            lock (cacheLock)
            {
                if (cachedConfig == null)
                {
                    config = null;
                    error = "程序参数缓存未初始化";
                    return false;
                }
                config = Clone(cachedConfig);
                error = null;
                return true;
            }
        }

        public static bool TrySave(AppConfig config, out string error)
        {
            error = null;
            if (config == null)
            {
                error = "配置为空";
                return false;
            }
            if (config.CommMaxMessageQueueSize <= 0)
            {
                error = $"队列长度配置无效:{config.CommMaxMessageQueueSize}";
                return false;
            }
            if (config.RuntimeMode != AutomationRuntimeMode.Hardware && config.RuntimeMode != AutomationRuntimeMode.Simulation)
            {
                error = $"运行模式配置无效:{config.RuntimeMode}";
                return false;
            }
            if (config.StartupView != AutomationStartupView.Hmi
                && config.StartupView != AutomationStartupView.PlatformEditor)
            {
                error = $"启动界面配置无效:{config.StartupView}";
                return false;
            }
            JObject obj = new JObject
            {
                [CommMaxMessageQueueSizeKey] = config.CommMaxMessageQueueSize,
                [RuntimeModeKey] = (int)config.RuntimeMode,
                [StartupViewKey] = (int)config.StartupView,
                [EnablePerformanceAnalysisKey] = config.EnablePerformanceAnalysis,
                [EnableRuntimeDiagnosticsKey] = config.EnableRuntimeDiagnostics
            };
            string json = obj.ToString(Formatting.Indented);
            string path = ConfigPath;
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                error = $"配置路径无效:{path}";
                return false;
            }
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json, Encoding.UTF8);
            SetCache(config);
            return true;
        }

        private static void SetCache(AppConfig config)
        {
            lock (cacheLock)
            {
                cachedConfig = Clone(config);
            }
        }

        private static AppConfig Clone(AppConfig config)
        {
            if (config == null)
            {
                return null;
            }
            return new AppConfig
            {
                CommMaxMessageQueueSize = config.CommMaxMessageQueueSize,
                RuntimeMode = config.RuntimeMode,
                StartupView = config.StartupView,
                EnablePerformanceAnalysis = config.EnablePerformanceAnalysis,
                EnableRuntimeDiagnostics = config.EnableRuntimeDiagnostics
            };
        }
    }
}
