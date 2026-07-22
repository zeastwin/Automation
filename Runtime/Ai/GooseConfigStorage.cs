using Newtonsoft.Json;
// 模块：运行时 / AI 集成。
// 职责范围：管理 AI 会话、配置、ACP/MCP 进程、受管运行环境和分析记录。

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Automation
{
    public sealed class GooseConfig
    {
        public string GooseExecutablePath { get; set; }

        public string WorkingDirectory { get; set; }

        public string McpUri { get; set; }

        public string SessionName { get; set; }

        public string Provider { get; set; }

        public string Model { get; set; }

        public string ModelServiceId { get; set; }

        public List<AiModelServiceConfig> ModelServices { get; set; }

        public double Temperature { get; set; }

        public int MaxTurns { get; set; }

        public int MaxOutputTokens { get; set; }

        public bool AutoApproveMode { get; set; }

        public string ToolProfile { get; set; }
    }

    /// <summary>
    /// 用户在 EW-AI 中维护的 OpenAI 兼容模型服务。密钥不保存在此对象中，
    /// 仅由 <see cref="AiProviderSecretStorage"/> 按服务 ID 加密保存。
    /// </summary>
    public sealed class AiModelServiceConfig
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string BaseUrl { get; set; }

        public string Model { get; set; }

        public int? ContextLimit { get; set; }

        public bool SupportsVision { get; set; }

        public bool RequiresApiKey { get; set; }
    }

    /// <summary>
    /// EW-AI 自身配置文件（config.yaml）里已注册的 provider 信息。
    /// </summary>
    public sealed class GooseProviderInfo
    {
        public string Name { get; set; }

        public string Model { get; set; }

        public bool IsActive { get; set; }
    }

    public static class GooseConfigStorage
    {
        public const string ConfigFolderName = "Config";
        public const string ConfigFileName = "GooseConfig.json";
        public const string GooseExecutablePathKey = "GooseExecutablePath";
        public const string WorkingDirectoryKey = "WorkingDirectory";
        public const string McpUriKey = "McpUri";
        public const string SessionNameKey = "SessionName";
        public const string ProviderKey = "Provider";
        public const string ModelKey = "Model";
        public const string ModelServiceIdKey = "ModelServiceId";
        public const string ModelServicesKey = "ModelServices";
        public const string TemperatureKey = "Temperature";
        public const string MaxTurnsKey = "MaxTurns";
        public const string MaxOutputTokensKey = "MaxOutputTokens";
        public const string AutoApproveModeKey = "AutoApproveMode";
        private const string LegacyFullPermissionModeKey = "FullPermissionMode";
        public const string ToolProfileKey = "ToolProfile";
        public const string DefaultToolProfile = "Diagnostic";
        public const int DefaultMaxTurns = 100;
        public const int DefaultMaxOutputTokens = 8192;
        public const double DefaultTemperature = 0.7d;
        public const string DefaultProvider = "deepseek";
        public const string DefaultModel = "deepseek-v4-pro";

        private static readonly object cacheLock = new object();
        private static GooseConfig cachedConfig;
        private static string startupSafetyError;

        public static string ConfigPath => AutomationRuntimeOptions.ActiveConfigFile(ConfigFileName);

        public static bool TryLoad(out GooseConfig config, out string error)
        {
            config = null;
            error = null;
            if (!string.IsNullOrWhiteSpace(startupSafetyError))
            {
                error = startupSafetyError;
                return false;
            }
            string path = ConfigPath;
            if (!File.Exists(path))
            {
                GooseConfig defaultConfig = CreateDefaultConfig();
                if (!TrySave(defaultConfig, out string saveError))
                {
                    error = $"默认 EW-AI 配置生成失败:{saveError}";
                    return false;
                }
                config = Clone(defaultConfig);
                SetCache(config);
                return true;
            }

            try
            {
                JObject obj = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                config = new GooseConfig
                {
                    GooseExecutablePath = ReadRequiredString(obj, GooseExecutablePathKey),
                    WorkingDirectory = ReadRequiredString(obj, WorkingDirectoryKey),
                    McpUri = ReadRequiredString(obj, McpUriKey),
                    SessionName = ReadRequiredString(obj, SessionNameKey),
                    Provider = ReadRequiredString(obj, ProviderKey),
                    Model = ReadRequiredString(obj, ModelKey),
                    ModelServiceId = ReadOptionalString(obj, ModelServiceIdKey, string.Empty),
                    ModelServices = ReadModelServices(obj),
                    Temperature = ReadOptionalDouble(obj, TemperatureKey, DefaultTemperature),
                    MaxTurns = ReadRequiredInt(obj, MaxTurnsKey),
                    MaxOutputTokens = ReadOptionalInt(obj, MaxOutputTokensKey, DefaultMaxOutputTokens),
                    AutoApproveMode = ReadOptionalBool(
                        obj,
                        AutoApproveModeKey,
                        ReadOptionalBool(obj, LegacyFullPermissionModeKey, false)),
                    ToolProfile = ReadToolProfile(obj)
                };

                if (!Validate(config, out error))
                {
                    config = null;
                    return false;
                }

                bool configMigrated = !obj.TryGetValue(
                    MaxOutputTokensKey, StringComparison.Ordinal, out _)
                    || !obj.TryGetValue(ModelServiceIdKey, StringComparison.Ordinal, out _)
                    || !obj.TryGetValue(ModelServicesKey, StringComparison.Ordinal, out _)
                    || !obj.TryGetValue(TemperatureKey, StringComparison.Ordinal, out _)
                    || !obj.TryGetValue(AutoApproveModeKey, StringComparison.Ordinal, out _)
                    || obj.TryGetValue(LegacyFullPermissionModeKey, StringComparison.Ordinal, out _);
                // DeepSeek 已发布 V4-Pro/V4-Flash，并将在 2026-07-24 停用旧模型标识。
                // 项目原先默认使用旧标识，统一迁移到面向复杂代理任务的 V4-Pro。
                if (string.Equals(config.Provider, "deepseek", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(config.Model, "deepseek-chat", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(config.Model, "deepseek-reasoner", StringComparison.OrdinalIgnoreCase)))
                {
                    config.Model = DefaultModel;
                    configMigrated = true;
                }

                // 旧版本默认值为 30；该值不足以完成包含多次 MCP 预演、确认和审计的流程编辑任务。
                // 仅迁移旧默认值，保留用户主动设置的其他上限。
                if (config.MaxTurns == 30)
                {
                    config.MaxTurns = DefaultMaxTurns;
                    configMigrated = true;
                }
                if (configMigrated)
                {
                    if (!TrySave(config, out string saveError))
                    {
                        config = null;
                        error = $"迁移 EW-AI 配置失败:{saveError}";
                        return false;
                    }
                }

                SetCache(config);
                return true;
            }
            catch (Exception ex)
            {
                error = $"读取 EW-AI 配置失败:{ex.Message}";
                return false;
            }
        }

        public static bool TryGetCached(out GooseConfig config, out string error)
        {
            lock (cacheLock)
            {
                if (cachedConfig == null)
                {
                    config = null;
                    error = "EW-AI 配置缓存未初始化";
                    return false;
                }
                config = Clone(cachedConfig);
                error = null;
                return true;
            }
        }

        public static bool TrySave(GooseConfig config, out string error)
        {
            error = null;
            if (!Validate(config, out error))
            {
                return false;
            }

            JObject obj = new JObject
            {
                [GooseExecutablePathKey] = config.GooseExecutablePath,
                [WorkingDirectoryKey] = config.WorkingDirectory,
                [McpUriKey] = config.McpUri,
                [SessionNameKey] = config.SessionName,
                [ProviderKey] = config.Provider,
                [ModelKey] = config.Model,
                [ModelServiceIdKey] = config.ModelServiceId ?? string.Empty,
                [ModelServicesKey] = JArray.FromObject(config.ModelServices ?? new List<AiModelServiceConfig>()),
                [TemperatureKey] = config.Temperature,
                [MaxTurnsKey] = config.MaxTurns,
                [MaxOutputTokensKey] = config.MaxOutputTokens,
                [AutoApproveModeKey] = config.AutoApproveMode,
                [ToolProfileKey] = config.ToolProfile
            };

            string path = ConfigPath;
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                error = $"EW-AI 配置路径无效:{path}";
                return false;
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, obj.ToString(Formatting.Indented), Encoding.UTF8);
            SetCache(config);
            startupSafetyError = null;
            return true;
        }

        public static bool TryApplyStartupSafetyDefaults(out string error)
        {
            if (!TryLoad(out GooseConfig config, out error))
            {
                startupSafetyError = error ?? "EW-AI 启动安全配置读取失败";
                return false;
            }

            config.ToolProfile = DefaultToolProfile;
            config.AutoApproveMode = false;
            if (!TrySave(config, out error))
            {
                startupSafetyError = "EW-AI 启动安全默认值保存失败:" + error;
                return false;
            }
            startupSafetyError = null;
            return true;
        }

        public static GooseConfig CreateDefaultConfig()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return new GooseConfig
            {
                GooseExecutablePath = GooseRuntimeEnvironment.MachineGooseExecutablePath,
                WorkingDirectory = baseDirectory,
                McpUri = "http://127.0.0.1:8081",
                SessionName = "automation",
                Provider = DefaultProvider,
                Model = DefaultModel,
                ModelServiceId = string.Empty,
                ModelServices = new List<AiModelServiceConfig>(),
                Temperature = DefaultTemperature,
                MaxTurns = DefaultMaxTurns,
                MaxOutputTokens = DefaultMaxOutputTokens,
                AutoApproveMode = false,
                ToolProfile = DefaultToolProfile
            };
        }

        public static bool TryValidate(GooseConfig config, out string error)
        {
            return Validate(config, out error);
        }

        public static void RemoveManagedDeepSeekGooseConfiguration()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Block", "goose", "config", "custom_providers", "custom_deepseek.json");
            if (!File.Exists(path))
            {
                return;
            }

            JObject provider = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (string.Equals(provider["name"]?.Value<string>(), "custom_deepseek", StringComparison.Ordinal)
                && string.Equals(
                    provider["description"]?.Value<string>(),
                    "Automation 自动配置的 DeepSeek 官方 API Provider",
                    StringComparison.Ordinal))
            {
                File.Delete(path);
            }
        }

        private static bool Validate(GooseConfig config, out string error)
        {
            error = null;
            if (config == null)
            {
                error = "EW-AI 配置为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(config.GooseExecutablePath))
            {
                error = "EW-AI 可执行文件路径不能为空";
                return false;
            }
            if (config.WorkingDirectory == null)
            {
                error = "EW-AI 工作目录不能为 null";
                return false;
            }
            if (string.IsNullOrWhiteSpace(config.WorkingDirectory))
            {
                error = "EW-AI 工作目录不能为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(config.McpUri))
            {
                error = "MCP 地址不能为空";
                return false;
            }
            if (!Uri.TryCreate(config.McpUri, UriKind.Absolute, out Uri uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                error = $"MCP 地址必须是 http 绝对地址:{config.McpUri}";
                return false;
            }
            if (config.SessionName == null)
            {
                error = "EW-AI 会话名不能为 null";
                return false;
            }
            if (config.Provider == null)
            {
                error = "EW-AI Provider 不能为 null";
                return false;
            }
            if (config.Model == null)
            {
                error = "EW-AI Model 不能为 null";
                return false;
            }
            if (config.ModelServiceId == null)
            {
                error = "EW-AI ModelServiceId 不能为 null";
                return false;
            }
            if (config.ModelServices == null)
            {
                error = "EW-AI ModelServices 不能为 null";
                return false;
            }
            var serviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var serviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AiModelServiceConfig service in config.ModelServices)
            {
                if (!ValidateModelService(service, out error)
                    || !serviceIds.Add(service.Id))
                {
                    if (error == null) error = $"自定义模型服务 ID 重复:{service?.Id}";
                    return false;
                }
                if (!serviceNames.Add(service.Name))
                {
                    error = $"自定义模型服务名称重复:{service.Name}";
                    return false;
                }
            }
            if (!string.IsNullOrWhiteSpace(config.ModelServiceId)
                && !serviceIds.Contains(config.ModelServiceId))
            {
                error = $"当前自定义模型服务不存在:{config.ModelServiceId}";
                return false;
            }
            if (double.IsNaN(config.Temperature) || double.IsInfinity(config.Temperature)
                || config.Temperature < 0d || config.Temperature > 1d)
            {
                error = $"EW-AI Temperature 必须在 0..1 之间:{config.Temperature}";
                return false;
            }
            if (config.MaxTurns <= 0)
            {
                error = $"EW-AI MaxTurns 必须大于 0:{config.MaxTurns}";
                return false;
            }
            if (config.MaxOutputTokens < 1024 || config.MaxOutputTokens > 65536)
            {
                error = $"EW-AI MaxOutputTokens 必须在 1024..65536 之间:{config.MaxOutputTokens}";
                return false;
            }
            if (!string.Equals(config.ToolProfile, "Diagnostic", StringComparison.Ordinal)
                && !string.Equals(config.ToolProfile, "Editor", StringComparison.Ordinal))
            {
                error = $"AI工具模式不支持:{config.ToolProfile}，可选Diagnostic/Editor";
                return false;
            }
            return true;
        }

        private static string ReadToolProfile(JObject obj)
        {
            if (!obj.TryGetValue(ToolProfileKey, StringComparison.Ordinal, out JToken token))
            {
                return DefaultToolProfile;
            }
            if (token.Type != JTokenType.String)
            {
                throw new InvalidOperationException($"EW-AI配置字段类型无效:{ToolProfileKey}");
            }
            string value = token.Value<string>();
            if (!string.Equals(value, "Diagnostic", StringComparison.Ordinal)
                && !string.Equals(value, "Editor", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"AI工具模式不支持:{value}，可选Diagnostic/Editor");
            }
            return value;
        }

        private static string ReadRequiredString(JObject obj, string key)
        {
            if (!obj.TryGetValue(key, StringComparison.Ordinal, out JToken token))
            {
                throw new InvalidOperationException($"EW-AI 配置缺少字段:{key}");
            }
            if (token.Type != JTokenType.String)
            {
                throw new InvalidOperationException($"EW-AI 配置字段类型无效:{key}");
            }
            return token.Value<string>();
        }

        private static int ReadRequiredInt(JObject obj, string key)
        {
            if (!obj.TryGetValue(key, StringComparison.Ordinal, out JToken token))
            {
                throw new InvalidOperationException($"EW-AI 配置缺少字段:{key}");
            }
            if (token.Type != JTokenType.Integer)
            {
                throw new InvalidOperationException($"EW-AI 配置字段类型无效:{key}");
            }
            return token.Value<int>();
        }

        private static int ReadOptionalInt(JObject obj, string key, int defaultValue)
        {
            if (!obj.TryGetValue(key, StringComparison.Ordinal, out JToken token))
            {
                return defaultValue;
            }
            if (token.Type != JTokenType.Integer)
            {
                throw new InvalidOperationException($"EW-AI 配置字段类型无效:{key}");
            }
            return token.Value<int>();
        }

        // 可选布尔字段读取：旧配置文件可能缺少该字段，缺失时返回 defaultValue，兼容向前版本。
        private static bool ReadOptionalBool(JObject obj, string key, bool defaultValue)
        {
            if (!obj.TryGetValue(key, StringComparison.Ordinal, out JToken token))
            {
                return defaultValue;
            }
            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }
            if (token.Type == JTokenType.String)
            {
                string s = token.Value<string>();
                if (bool.TryParse(s, out bool v)) return v;
            }
            return defaultValue;
        }

        private static void SetCache(GooseConfig config)
        {
            lock (cacheLock)
            {
                cachedConfig = Clone(config);
            }
        }

        private static GooseConfig Clone(GooseConfig config)
        {
            if (config == null)
            {
                return null;
            }
            return new GooseConfig
            {
                GooseExecutablePath = config.GooseExecutablePath,
                WorkingDirectory = config.WorkingDirectory,
                McpUri = config.McpUri,
                SessionName = config.SessionName,
                Provider = config.Provider,
                Model = config.Model,
                ModelServiceId = config.ModelServiceId,
                ModelServices = CloneModelServices(config.ModelServices),
                Temperature = config.Temperature,
                MaxTurns = config.MaxTurns,
                MaxOutputTokens = config.MaxOutputTokens,
                AutoApproveMode = config.AutoApproveMode,
                ToolProfile = config.ToolProfile
            };
        }

        public static AiModelServiceConfig FindModelService(GooseConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.ModelServiceId))
            {
                return null;
            }
            return (config.ModelServices ?? new List<AiModelServiceConfig>()).Find(item =>
                string.Equals(item.Id, config.ModelServiceId, StringComparison.OrdinalIgnoreCase));
        }

        public static List<AiModelServiceConfig> CloneModelServices(IEnumerable<AiModelServiceConfig> services)
        {
            var result = new List<AiModelServiceConfig>();
            if (services == null) return result;
            foreach (AiModelServiceConfig service in services)
            {
                if (service == null) continue;
                result.Add(new AiModelServiceConfig
                {
                    Id = service.Id,
                    Name = service.Name,
                    BaseUrl = service.BaseUrl,
                    Model = service.Model,
                    ContextLimit = service.ContextLimit,
                    SupportsVision = service.SupportsVision,
                    RequiresApiKey = service.RequiresApiKey
                });
            }
            return result;
        }

        public static bool ValidateModelService(AiModelServiceConfig service, out string error)
        {
            error = null;
            if (service == null) { error = "自定义模型服务为空"; return false; }
            if (!Guid.TryParse(service.Id, out _)) { error = $"自定义模型服务 ID 无效:{service.Id}"; return false; }
            if (string.IsNullOrWhiteSpace(service.Name)) { error = "自定义模型服务名称不能为空"; return false; }
            if (string.IsNullOrWhiteSpace(service.Model)) { error = $"自定义模型服务 {service.Name} 的模型 ID 不能为空"; return false; }
            if (!Uri.TryCreate(service.BaseUrl, UriKind.Absolute, out Uri uri)
                || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"自定义模型服务 {service.Name} 的 Base URL 必须是 HTTP/HTTPS 绝对地址:{service.BaseUrl}";
                return false;
            }
            string path = uri.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                error = $"自定义模型服务 {service.Name} 必须填写 Base URL（例如 http://主机:端口/v1），不能填写具体请求端点:{service.BaseUrl}";
                return false;
            }
            if (service.ContextLimit.HasValue && service.ContextLimit.Value <= 0)
            {
                error = $"自定义模型服务 {service.Name} 的上下文长度必须大于 0:{service.ContextLimit}";
                return false;
            }
            return true;
        }

        private static List<AiModelServiceConfig> ReadModelServices(JObject obj)
        {
            if (!obj.TryGetValue(ModelServicesKey, StringComparison.Ordinal, out JToken token))
            {
                return new List<AiModelServiceConfig>();
            }
            if (token.Type != JTokenType.Array)
            {
                throw new InvalidOperationException($"EW-AI 配置字段类型无效:{ModelServicesKey}");
            }
            return token.ToObject<List<AiModelServiceConfig>>() ?? new List<AiModelServiceConfig>();
        }

        private static string ReadOptionalString(JObject obj, string key, string defaultValue)
        {
            if (!obj.TryGetValue(key, StringComparison.Ordinal, out JToken token)) return defaultValue;
            if (token.Type != JTokenType.String) throw new InvalidOperationException($"EW-AI 配置字段类型无效:{key}");
            return token.Value<string>();
        }

        private static double ReadOptionalDouble(JObject obj, string key, double defaultValue)
        {
            if (!obj.TryGetValue(key, StringComparison.Ordinal, out JToken token)) return defaultValue;
            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                throw new InvalidOperationException($"EW-AI 配置字段类型无效:{key}");
            }
            return token.Value<double>();
        }

        /// <summary>
        /// 从 EW-AI 自身配置文件（%APPDATA%\Block\goose\config\config.yaml）读取已注册的 provider 列表。
        /// 用于 AI 助手 Provider 下拉框，避免界面硬编码的 provider 名与 EW-AI 实际注册名不一致
        /// （例如 goose configure 创建的 custom_deepseek）。解析失败返回空列表，不影响主流程。
        /// </summary>
        public static List<GooseProviderInfo> TryLoadGooseProviders()
        {
            var result = new List<GooseProviderInfo>();
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Block", "goose", "config", "config.yaml");
                if (!File.Exists(path))
                {
                    return result;
                }

                string[] lines = File.ReadAllLines(path);
                string activeProvider = null;
                bool inProviders = false;
                int providerIndent = -1;
                string currentProvider = null;
                int currentProviderIndent = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int hashIdx = line.IndexOf('#');
                    if (hashIdx >= 0)
                    {
                        line = line.Substring(0, hashIdx);
                    }
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        continue;
                    }

                    int indent = line.Length - line.TrimStart().Length;

                    if (indent == 0)
                    {
                        inProviders = false;
                        currentProvider = null;

                        if (trimmed.StartsWith("active_provider:", StringComparison.Ordinal))
                        {
                            activeProvider = trimmed.Substring("active_provider:".Length).Trim().Trim('"', '\'');
                        }
                        else if (trimmed == "providers:")
                        {
                            inProviders = true;
                            providerIndent = -1;
                        }
                        continue;
                    }

                    if (!inProviders)
                    {
                        continue;
                    }

                    if (providerIndent < 0)
                    {
                        providerIndent = indent;
                    }

                    if (indent == providerIndent && trimmed.EndsWith(":", StringComparison.Ordinal))
                    {
                        currentProvider = trimmed.TrimEnd(':').Trim();
                        currentProviderIndent = indent;
                        result.Add(new GooseProviderInfo
                        {
                            Name = currentProvider,
                            Model = null,
                            IsActive = string.Equals(currentProvider, activeProvider, StringComparison.Ordinal)
                        });
                    }
                    else if (currentProvider != null && indent > currentProviderIndent)
                    {
                        if (trimmed.StartsWith("model:", StringComparison.Ordinal))
                        {
                            string model = trimmed.Substring("model:".Length).Trim().Trim('"', '\'');
                            if (result.Count > 0 && string.Equals(result[result.Count - 1].Name, currentProvider, StringComparison.Ordinal))
                            {
                                result[result.Count - 1].Model = model;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return result;
        }
    }
}
