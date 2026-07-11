using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Automation
{
    public static class AiProviderSecretStorage
    {
        private static readonly byte[] entropy = Encoding.UTF8.GetBytes("Automation.EW-AI.ProviderSecrets.v1");
        public static string SecretPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Automation", "AiProviderSecrets.dat");

        public static bool HasSecret(string provider)
        {
            return TryLoad(out Dictionary<string, string> values, out _)
                && values.ContainsKey(NormalizeProvider(provider));
        }

        public static bool TryGetSecret(string provider, out string secret, out string error)
        {
            secret = null;
            if (!TryLoad(out Dictionary<string, string> values, out error)) return false;
            if (!values.TryGetValue(NormalizeProvider(provider), out secret) || string.IsNullOrWhiteSpace(secret))
            {
                error = "当前 Provider 尚未在本机保存 API Key。";
                secret = null;
                return false;
            }
            return true;
        }

        public static bool TrySaveSecret(string provider, string secret, out string error)
        {
            error = null;
            string key = NormalizeProvider(provider);
            if (string.IsNullOrWhiteSpace(key)) { error = "必须先选择 Provider。"; return false; }
            if (string.IsNullOrWhiteSpace(secret)) { error = "API Key 不能为空。"; return false; }
            if (!TryLoad(out Dictionary<string, string> values, out error)) return false;
            values[key] = secret.Trim();
            return TryWrite(values, out error);
        }

        public static bool TryDeleteSecret(string provider, out string error)
        {
            if (!TryLoad(out Dictionary<string, string> values, out error)) return false;
            values.Remove(NormalizeProvider(provider));
            return TryWrite(values, out error);
        }

        public static bool TryGetEnvironmentVariableName(string provider, out string variableName)
        {
            switch (NormalizeProvider(provider))
            {
                case "openai": variableName = "OPENAI_API_KEY"; return true;
                case "anthropic": variableName = "ANTHROPIC_API_KEY"; return true;
                case "google": variableName = "GOOGLE_API_KEY"; return true;
                case "openrouter": variableName = "OPENROUTER_API_KEY"; return true;
                case "azure_openai": variableName = "AZURE_OPENAI_API_KEY"; return true;
                case "deepseek":
                case "custom_deepseek": variableName = "DEEPSEEK_API_KEY"; return true;
                case "ollama": variableName = null; return true;
                default: variableName = null; return false;
            }
        }

        private static bool TryLoad(out Dictionary<string, string> values, out string error)
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = null;
            try
            {
                if (!File.Exists(SecretPath)) return true;
                byte[] encrypted = File.ReadAllBytes(SecretPath);
                byte[] plain = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser);
                JObject obj = JObject.Parse(Encoding.UTF8.GetString(plain));
                foreach (JProperty property in obj.Properties())
                {
                    if (property.Value.Type != JTokenType.String) throw new InvalidDataException("本机 API Key 配置格式无效。");
                    values[property.Name] = property.Value.Value<string>();
                }
                Array.Clear(plain, 0, plain.Length);
                return true;
            }
            catch (Exception ex) { error = "读取本机 API Key 失败：" + ex.Message; return false; }
        }

        private static bool TryWrite(Dictionary<string, string> values, out string error)
        {
            error = null;
            try
            {
                string directory = Path.GetDirectoryName(SecretPath);
                Directory.CreateDirectory(directory);
                byte[] plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(values));
                byte[] encrypted = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(SecretPath, encrypted);
                Array.Clear(plain, 0, plain.Length);
                return true;
            }
            catch (Exception ex) { error = "保存本机 API Key 失败：" + ex.Message; return false; }
        }

        private static string NormalizeProvider(string provider)
        {
            return (provider ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
