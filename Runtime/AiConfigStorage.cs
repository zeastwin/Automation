using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Automation
{
    public sealed class AiConfig
    {
        public string AgentExecutablePath { get; set; }

        public string WorkingDirectory { get; set; }

        public string SessionName { get; set; }

        public string Provider { get; set; }

        public string Model { get; set; }

        public string ModelServiceId { get; set; }

        public List<AiModelServiceConfig> ModelServices { get; set; }

        public int MaxOutputTokens { get; set; }

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

    public static class AiConfigStorage
    {
        public const string ConfigFolderName = "Config";
        public const string ConfigFileName = "AiConfig.json";
        public const string AgentExecutablePathKey = "AgentExecutablePath";
        public const string WorkingDirectoryKey = "WorkingDirectory";
        public const string SessionNameKey = "SessionName";
        public const string ProviderKey = "Provider";
        public const string ModelKey = "Model";
        public const string ModelServiceIdKey = "ModelServiceId";
        public const string ModelServicesKey = "ModelServices";
        public const string MaxOutputTokensKey = "MaxOutputTokens";
        public const string ToolProfileKey = "ToolProfile";
        public const string DefaultToolProfile = "Diagnostic";
        public const int DefaultMaxOutputTokens = 8192;
        public const string DefaultProvider = "deepseek";
        public const string DefaultModel = "deepseek-v4-pro";

        private static readonly object cacheLock = new object();
        private static AiConfig cachedConfig;
        private static string startupSafetyError;

        public static string ConfigPath => AutomationRuntimeOptions.ActiveConfigFile(ConfigFileName);

        public static bool TryLoad(out AiConfig config, out string error)
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
                AiConfig defaultConfig = CreateDefaultConfig();
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
                config = new AiConfig
                {
                    AgentExecutablePath = ReadRequiredString(obj, AgentExecutablePathKey),
                    WorkingDirectory = ReadRequiredString(obj, WorkingDirectoryKey),
                    SessionName = ReadRequiredString(obj, SessionNameKey),
                    Provider = ReadRequiredString(obj, ProviderKey),
                    Model = ReadRequiredString(obj, ModelKey),
                    ModelServiceId = ReadOptionalString(obj, ModelServiceIdKey, string.Empty),
                    ModelServices = ReadModelServices(obj),
                    MaxOutputTokens = ReadOptionalInt(obj, MaxOutputTokensKey, DefaultMaxOutputTokens),
                    ToolProfile = ReadToolProfile(obj)
                };

                if (!Validate(config, out error))
                {
                    config = null;
                    return false;
                }

                bool configMigrated = !obj.TryGetValue(MaxOutputTokensKey, StringComparison.Ordinal, out _)
                    || !obj.TryGetValue(ModelServiceIdKey, StringComparison.Ordinal, out _)
                    || !obj.TryGetValue(ModelServicesKey, StringComparison.Ordinal, out _);
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

        public static bool TryGetCached(out AiConfig config, out string error)
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

        public static bool TrySave(AiConfig config, out string error)
        {
            error = null;
            if (!Validate(config, out error))
            {
                return false;
            }

            JObject obj = new JObject
            {
                [AgentExecutablePathKey] = config.AgentExecutablePath,
                [WorkingDirectoryKey] = config.WorkingDirectory,
                [SessionNameKey] = config.SessionName,
                [ProviderKey] = config.Provider,
                [ModelKey] = config.Model,
                [ModelServiceIdKey] = config.ModelServiceId ?? string.Empty,
                [ModelServicesKey] = JArray.FromObject(config.ModelServices ?? new List<AiModelServiceConfig>()),
                [MaxOutputTokensKey] = config.MaxOutputTokens,
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
            if (!TryLoad(out AiConfig config, out error))
            {
                startupSafetyError = error ?? "EW-AI 启动安全配置读取失败";
                return false;
            }

            config.ToolProfile = DefaultToolProfile;
            if (!TrySave(config, out error))
            {
                startupSafetyError = "EW-AI 启动安全默认值保存失败:" + error;
                return false;
            }
            startupSafetyError = null;
            return true;
        }

        public static AiConfig CreateDefaultConfig()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return new AiConfig
            {
                AgentExecutablePath = PiRuntimeEnvironment.MachinePiExecutablePath,
                WorkingDirectory = baseDirectory,
                SessionName = "automation",
                Provider = DefaultProvider,
                Model = DefaultModel,
                ModelServiceId = string.Empty,
                ModelServices = new List<AiModelServiceConfig>(),
                MaxOutputTokens = DefaultMaxOutputTokens,
                ToolProfile = DefaultToolProfile
            };
        }

        public static bool TryValidate(AiConfig config, out string error)
        {
            return Validate(config, out error);
        }

        private static bool Validate(AiConfig config, out string error)
        {
            error = null;
            if (config == null)
            {
                error = "EW-AI 配置为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(config.AgentExecutablePath))
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

        private static void SetCache(AiConfig config)
        {
            lock (cacheLock)
            {
                cachedConfig = Clone(config);
            }
        }

        private static AiConfig Clone(AiConfig config)
        {
            if (config == null)
            {
                return null;
            }
            return new AiConfig
            {
                AgentExecutablePath = config.AgentExecutablePath,
                WorkingDirectory = config.WorkingDirectory,
                SessionName = config.SessionName,
                Provider = config.Provider,
                Model = config.Model,
                ModelServiceId = config.ModelServiceId,
                ModelServices = CloneModelServices(config.ModelServices),
                MaxOutputTokens = config.MaxOutputTokens,
                ToolProfile = config.ToolProfile
            };
        }

        public static AiModelServiceConfig FindModelService(AiConfig config)
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
    }
}
