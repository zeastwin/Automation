using System;
using System.Collections.Generic;
using System.IO;
using Automation;
using Newtonsoft.Json;

namespace Automation.AIFlow
{
    public sealed class AiFlowAiConfig
    {
        public const string ConfigVersion = "ai-1";

        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; } = ConfigVersion;

        [JsonProperty("endpoint", Required = Required.Always)]
        public string Endpoint { get; set; }

        [JsonProperty("apiKey", Required = Required.Always)]
        public string ApiKey { get; set; }

        [JsonProperty("model", Required = Required.Always)]
        public string Model { get; set; }

        [JsonProperty("timeoutSeconds", Required = Required.Always)]
        public int TimeoutSeconds { get; set; }

        [JsonProperty("authHeader", Required = Required.Always)]
        public string AuthHeader { get; set; }

        [JsonProperty("authPrefix", Required = Required.Always)]
        public string AuthPrefix { get; set; }

        [JsonProperty("temperature", Required = Required.Always)]
        public double Temperature { get; set; }
    }

    public static class AiFlowAiConfigStore
    {
        public const int MaxTimeoutSeconds = 600;
        private static readonly JsonSerializerSettings StrictSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            TypeNameHandling = TypeNameHandling.None,
            DateParseHandling = DateParseHandling.None
        };

        public static string GetConfigPath()
        {
            string root = SF.ConfigPath ?? string.Empty;
            return Path.Combine(root, "AIFlowAi.json");
        }

        public static bool TryLoad(out AiFlowAiConfig config, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            config = null;
            string path = GetConfigPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_PATH_EMPTY", "配置路径为空", "ai-config"));
                return false;
            }
            if (!File.Exists(path))
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_NOT_FOUND", $"配置文件不存在:{path}", "ai-config"));
                return false;
            }
            try
            {
                string json = File.ReadAllText(path);
                config = JsonConvert.DeserializeObject<AiFlowAiConfig>(json, StrictSettings);
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_PARSE", ex.Message, "ai-config"));
                return false;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_READ", ex.Message, "ai-config"));
                return false;
            }
            issues.AddRange(Validate(config));
            return issues.Count == 0;
        }

        public static bool TrySave(AiFlowAiConfig config, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            if (config == null)
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_NULL", "配置为空", "ai-config"));
                return false;
            }
            issues.AddRange(Validate(config));
            if (issues.Count > 0)
            {
                return false;
            }
            string path = GetConfigPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_PATH_EMPTY", "配置路径为空", "ai-config"));
                return false;
            }
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_WRITE", ex.Message, "ai-config"));
                return false;
            }
        }

        public static AiFlowAiConfig CreateDefault()
        {
            return new AiFlowAiConfig
            {
                Version = AiFlowAiConfig.ConfigVersion,
                Endpoint = string.Empty,
                ApiKey = string.Empty,
                Model = "",
                TimeoutSeconds = 60,
                AuthHeader = "Authorization",
                AuthPrefix = "Bearer",
                Temperature = 0.2
            };
        }

        public static List<AiFlowIssue> Validate(AiFlowAiConfig config)
        {
            var issues = new List<AiFlowIssue>();
            if (config == null)
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_NULL", "配置为空", "ai-config"));
                return issues;
            }
            if (!string.Equals(config.Version, AiFlowAiConfig.ConfigVersion, StringComparison.Ordinal))
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_VERSION", $"配置版本不匹配:{config.Version}", "ai-config.version"));
            }
            if (string.IsNullOrWhiteSpace(config.Endpoint))
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_ENDPOINT", "接口地址为空", "ai-config.endpoint"));
            }
            else if (!Uri.TryCreate(config.Endpoint, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_ENDPOINT", $"接口地址无效:{config.Endpoint}", "ai-config.endpoint"));
            }
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_KEY", "ApiKey 为空", "ai-config.apiKey"));
            }
            if (string.IsNullOrWhiteSpace(config.Model))
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_MODEL", "模型为空", "ai-config.model"));
            }
            if (config.TimeoutSeconds <= 0)
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_TIMEOUT", "超时必须大于0", "ai-config.timeoutSeconds"));
            }
            else if (config.TimeoutSeconds > MaxTimeoutSeconds)
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_TIMEOUT", $"超时超过上限:{MaxTimeoutSeconds}", "ai-config.timeoutSeconds"));
            }
            if (string.IsNullOrWhiteSpace(config.AuthHeader))
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_AUTH_HEADER", "鉴权头为空", "ai-config.authHeader"));
            }
            if (config.Temperature < 0 || config.Temperature > 2)
            {
                issues.Add(new AiFlowIssue("AI_CONFIG_TEMPERATURE", "temperature 超出范围(0-2)", "ai-config.temperature"));
            }
            return issues;
        }
    }
}
