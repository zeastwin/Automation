using Newtonsoft.Json;
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

        public int MaxTurns { get; set; }
    }

    /// <summary>
    /// Goose 自身配置文件（config.yaml）里已注册的 provider 信息。
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
        public const string MaxTurnsKey = "MaxTurns";
        public const int DefaultMaxTurns = 30;

        private static readonly object cacheLock = new object();
        private static GooseConfig cachedConfig;

        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFolderName, ConfigFileName);

        public static bool TryLoad(out GooseConfig config, out string error)
        {
            config = null;
            error = null;
            string path = ConfigPath;
            if (!File.Exists(path))
            {
                GooseConfig defaultConfig = CreateDefaultConfig();
                if (!TrySave(defaultConfig, out string saveError))
                {
                    error = $"默认 Goose 配置生成失败:{saveError}";
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
                    MaxTurns = ReadRequiredInt(obj, MaxTurnsKey)
                };

                if (!Validate(config, out error))
                {
                    config = null;
                    return false;
                }

                SetCache(config);
                return true;
            }
            catch (Exception ex)
            {
                error = $"读取 Goose 配置失败:{ex.Message}";
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
                    error = "Goose 配置缓存未初始化";
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
                [MaxTurnsKey] = config.MaxTurns
            };

            string path = ConfigPath;
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                error = $"Goose 配置路径无效:{path}";
                return false;
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, obj.ToString(Formatting.Indented), Encoding.UTF8);
            SetCache(config);
            return true;
        }

        public static GooseConfig CreateDefaultConfig()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return new GooseConfig
            {
                GooseExecutablePath = "goose",
                WorkingDirectory = baseDirectory,
                McpUri = "http://127.0.0.1:8081",
                SessionName = "automation",
                Provider = string.Empty,
                Model = string.Empty,
                MaxTurns = DefaultMaxTurns
            };
        }

        public static bool TryValidate(GooseConfig config, out string error)
        {
            return Validate(config, out error);
        }

        private static bool Validate(GooseConfig config, out string error)
        {
            error = null;
            if (config == null)
            {
                error = "Goose 配置为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(config.GooseExecutablePath))
            {
                error = "Goose 可执行文件路径不能为空";
                return false;
            }
            if (config.WorkingDirectory == null)
            {
                error = "Goose 工作目录不能为 null";
                return false;
            }
            if (string.IsNullOrWhiteSpace(config.WorkingDirectory))
            {
                error = "Goose 工作目录不能为空";
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
                error = "Goose 会话名不能为 null";
                return false;
            }
            if (config.Provider == null)
            {
                error = "Goose Provider 不能为 null";
                return false;
            }
            if (config.Model == null)
            {
                error = "Goose Model 不能为 null";
                return false;
            }
            if (config.MaxTurns <= 0)
            {
                error = $"Goose MaxTurns 必须大于 0:{config.MaxTurns}";
                return false;
            }
            return true;
        }

        private static string ReadRequiredString(JObject obj, string key)
        {
            if (!obj.TryGetValue(key, StringComparison.Ordinal, out JToken token))
            {
                throw new InvalidOperationException($"Goose 配置缺少字段:{key}");
            }
            if (token.Type != JTokenType.String)
            {
                throw new InvalidOperationException($"Goose 配置字段类型无效:{key}");
            }
            return token.Value<string>();
        }

        private static int ReadRequiredInt(JObject obj, string key)
        {
            if (!obj.TryGetValue(key, StringComparison.Ordinal, out JToken token))
            {
                throw new InvalidOperationException($"Goose 配置缺少字段:{key}");
            }
            if (token.Type != JTokenType.Integer)
            {
                throw new InvalidOperationException($"Goose 配置字段类型无效:{key}");
            }
            return token.Value<int>();
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
                MaxTurns = config.MaxTurns
            };
        }

        /// <summary>
        /// 从 Goose 自身配置文件（%APPDATA%\Block\goose\config\config.yaml）读取已注册的 provider 列表。
        /// 用于 AI 助手 Provider 下拉框，避免界面硬编码的 provider 名与 Goose 实际注册名不一致
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
