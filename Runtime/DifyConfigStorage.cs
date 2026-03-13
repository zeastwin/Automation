using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation
{
    public sealed class DifyConfig
    {
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
    }

    public static class DifyConfigStorage
    {
        public const string ConfigFolderName = "Config";
        public const string ConfigFileName = "DifyConfig.json";
        public const string BaseUrlKey = "BaseUrl";
        public const string ApiKeyKey = "ApiKey";
        private static readonly object cacheLock = new object();
        private static DifyConfig cachedConfig;

        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFolderName, ConfigFileName);

        public static bool TryLoad(out DifyConfig config, out string error)
        {
            config = null;
            error = null;
            string path = ConfigPath;
            if (!File.Exists(path))
            {
                DifyConfig defaultConfig = new DifyConfig
                {
                    BaseUrl = string.Empty,
                    ApiKey = string.Empty
                };
                if (!TrySave(defaultConfig, out string saveError))
                {
                    error = $"默认 Dify 配置生成失败:{saveError}";
                    return false;
                }
                config = Clone(defaultConfig);
                SetCache(config);
                return true;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                JObject obj = JObject.Parse(json);
                if (!obj.TryGetValue(BaseUrlKey, StringComparison.Ordinal, out JToken baseUrlToken))
                {
                    error = $"Dify 配置缺少字段:{BaseUrlKey}";
                    return false;
                }
                if (baseUrlToken.Type != JTokenType.String)
                {
                    error = $"Dify 地址字段类型无效:{baseUrlToken}";
                    return false;
                }
                if (!obj.TryGetValue(ApiKeyKey, StringComparison.Ordinal, out JToken apiKeyToken))
                {
                    error = $"Dify 配置缺少字段:{ApiKeyKey}";
                    return false;
                }
                if (apiKeyToken.Type != JTokenType.String)
                {
                    error = $"Dify Key 字段类型无效:{apiKeyToken}";
                    return false;
                }

                config = new DifyConfig
                {
                    BaseUrl = baseUrlToken.Value<string>() ?? string.Empty,
                    ApiKey = apiKeyToken.Value<string>() ?? string.Empty
                };
                SetCache(config);
                return true;
            }
            catch (Exception ex)
            {
                error = $"读取 Dify 配置失败:{ex.Message}";
                return false;
            }
        }

        public static bool TryGetCached(out DifyConfig config, out string error)
        {
            lock (cacheLock)
            {
                if (cachedConfig == null)
                {
                    config = null;
                    error = "Dify 配置缓存未初始化";
                    return false;
                }
                config = Clone(cachedConfig);
                error = null;
                return true;
            }
        }

        public static bool TrySave(DifyConfig config, out string error)
        {
            error = null;
            if (config == null)
            {
                error = "Dify 配置为空";
                return false;
            }
            if (config.BaseUrl == null)
            {
                error = "Dify 地址不能为 null";
                return false;
            }
            if (config.ApiKey == null)
            {
                error = "Dify Key 不能为 null";
                return false;
            }

            JObject obj = new JObject
            {
                [BaseUrlKey] = config.BaseUrl,
                [ApiKeyKey] = config.ApiKey
            };
            string json = obj.ToString(Formatting.Indented);
            string path = ConfigPath;
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                error = $"Dify 配置路径无效:{path}";
                return false;
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json, Encoding.UTF8);
            SetCache(config);
            return true;
        }

        private static void SetCache(DifyConfig config)
        {
            lock (cacheLock)
            {
                cachedConfig = Clone(config);
            }
        }

        private static DifyConfig Clone(DifyConfig config)
        {
            if (config == null)
            {
                return null;
            }
            return new DifyConfig
            {
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey
            };
        }
    }
}
