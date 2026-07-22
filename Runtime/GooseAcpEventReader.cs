using Newtonsoft.Json.Linq;

namespace Automation
{
    internal static class GooseAcpEventReader
    {
        /// <summary>
        /// 从 ACP tool_call_update 完成通知读取 MCP 工具返回的文本。
        /// </summary>
        public static string ExtractToolResultText(JObject raw)
        {
            JToken parameters = raw?["params"];
            JToken update = parameters?["update"] ?? parameters;
            JToken content = update?["content"];
            if (content is JArray items && items.Count > 0)
            {
                JToken text = items[0]["text"] ?? items[0]?["content"]?["text"];
                if (text?.Type == JTokenType.String) return text.Value<string>();
            }
            return null;
        }
    }
}
