using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace Automation.Bridge
{
    internal sealed partial class AutomationBridgeService
    {
        public static string BuildErrorBody(string code, string message, string details = null)
        {
            var error = new JObject
            {
                ["ok"] = false,
                ["type"] = "bridge.error",
                ["errorCode"] = code ?? string.Empty,
                ["message"] = message ?? string.Empty
            };
            if (!string.IsNullOrWhiteSpace(details))
            {
                try
                {
                    JToken recovery = JToken.Parse(details);
                    if (recovery is JObject)
                    {
                        error["recovery"] = recovery;
                    }
                    else
                    {
                        error["details"] = details;
                    }
                }
                catch (JsonReaderException)
                {
                    error["details"] = details;
                }
            }
            return error.ToString(Formatting.None);
        }

        private static string BuildSuccessBody(string type, JToken data)
        {
            return new JObject
            {
                ["ok"] = true,
                ["type"] = type,
                ["data"] = data ?? JValue.CreateNull()
            }.ToString(Formatting.None);
        }

        // Bridge 错误返回对象：handler 方法用 return 代替 throw 时使用。
        // 通过 __bridgeError 标记让 WrapResponse 识别并转换为 AutomationBridgeResponse.Error。
        private static JObject BridgeError(int statusCode, string code, string message, string details = null)
        {
            return new JObject
            {
                ["__bridgeError"] = true,
                ["statusCode"] = statusCode,
                ["code"] = code ?? string.Empty,
                ["message"] = message ?? string.Empty,
                ["details"] = string.IsNullOrWhiteSpace(details) ? null : details
            };
        }

        // 包装 handler 返回值：如果是 BridgeError 错误对象则返回 AutomationBridgeResponse.Error，否则按成功包装。
        private static AutomationBridgeResponse WrapResponse(string type, JObject result)
        {
            if (result != null && result["__bridgeError"] is JToken flag && flag.Value<bool>())
            {
                int statusCode = result["statusCode"]?.Value<int>() ?? 400;
                string code = result["code"]?.Value<string>() ?? "BRIDGE_ERROR";
                string message = result["message"]?.Value<string>() ?? string.Empty;
                string details = result["details"]?.Value<string>();
                return AutomationBridgeResponse.Error(statusCode, code, message, details);
            }
            return AutomationBridgeResponse.Ok(BuildSuccessBody(type, result));
        }

    }
}
