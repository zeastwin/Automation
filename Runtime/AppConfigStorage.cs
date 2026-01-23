using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation
{
    public sealed class AppConfig
    {
        public int CommMaxMessageQueueSize { get; set; }
    }

    public static class AppConfigStorage
    {
        public const string ConfigFileName = "AppConfig.json";
        public const string CommMaxMessageQueueSizeKey = "CommMaxMessageQueueSize";

        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

        public static bool TryLoad(out AppConfig config, out string error)
        {
            config = null;
            error = null;
            string path = ConfigPath;
            if (!File.Exists(path))
            {
                error = $"配置文件不存在:{path}";
                return false;
            }
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                JObject obj = JObject.Parse(json);
                if (!obj.TryGetValue(CommMaxMessageQueueSizeKey, StringComparison.Ordinal, out JToken token))
                {
                    error = $"配置缺少字段:{CommMaxMessageQueueSizeKey}";
                    return false;
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
                config = new AppConfig
                {
                    CommMaxMessageQueueSize = value
                };
                return true;
            }
            catch (Exception ex)
            {
                error = $"读取配置失败:{ex.Message}";
                return false;
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
            JObject obj = new JObject
            {
                [CommMaxMessageQueueSizeKey] = config.CommMaxMessageQueueSize
            };
            string json = obj.ToString(Formatting.Indented);
            File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            return true;
        }
    }
}
