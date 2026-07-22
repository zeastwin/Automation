// 模块：运行时 / 诊断。
// 职责范围：统一 Newtonsoft.Json 日志中的敏感字段识别与递归脱敏。

using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace Automation
{
    internal static class SensitiveDataRedactor
    {
        public static void Redact(JToken token)
        {
            if (!(token is JContainer container))
            {
                return;
            }

            foreach (JToken child in container.Children().ToList())
            {
                if (child is JProperty property && IsSensitiveName(property.Name))
                {
                    property.Value = "***";
                    continue;
                }
                Redact(child);
            }
        }

        public static bool IsSensitiveName(string name)
        {
            string value = name ?? string.Empty;
            return value.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("apiKey", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(value, "headers", StringComparison.OrdinalIgnoreCase);
        }
    }
}
