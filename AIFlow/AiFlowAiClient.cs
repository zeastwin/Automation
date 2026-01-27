using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation.AIFlow
{
    public sealed class AiFlowAiResult
    {
        public string Content { get; set; }
        public string RawResponse { get; set; }
        public List<AiFlowIssue> Issues { get; } = new List<AiFlowIssue>();
        public bool Success => Issues.Count == 0 && !string.IsNullOrWhiteSpace(Content);
    }

    public static class AiFlowAiClient
    {
        private static readonly JsonSerializerSettings StrictSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            TypeNameHandling = TypeNameHandling.None,
            DateParseHandling = DateParseHandling.None
        };

        public static async Task<AiFlowAiResult> RequestAsync(AiFlowAiConfig config, string request, string context, string outputKind)
        {
            var result = new AiFlowAiResult();
            if (config == null)
            {
                result.Issues.Add(new AiFlowIssue("AI_REQUEST_CONFIG", "配置为空", "ai-request"));
                return result;
            }
            List<AiFlowIssue> configIssues = AiFlowAiConfigStore.Validate(config);
            if (configIssues.Count > 0)
            {
                result.Issues.AddRange(configIssues);
                return result;
            }
            if (string.IsNullOrWhiteSpace(request))
            {
                result.Issues.Add(new AiFlowIssue("AI_REQUEST_EMPTY", "需求描述为空", "ai-request"));
                return result;
            }
            if (string.IsNullOrWhiteSpace(outputKind))
            {
                result.Issues.Add(new AiFlowIssue("AI_REQUEST_OUTPUT_KIND", "输出类型为空", "ai-request"));
                return result;
            }
            if (!string.Equals(outputKind, "FlowDelta", StringComparison.Ordinal) && !string.Equals(outputKind, "Core", StringComparison.Ordinal))
            {
                result.Issues.Add(new AiFlowIssue("AI_REQUEST_OUTPUT_KIND", $"输出类型无效:{outputKind}", "ai-request"));
                return result;
            }

            string systemPrompt = BuildSystemPrompt(outputKind);
            string userPrompt = BuildUserPrompt(request, context, outputKind);

            var payload = new
            {
                model = config.Model,
                temperature = config.Temperature,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            string json = JsonConvert.SerializeObject(payload, Formatting.None);
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
                using (var message = new HttpRequestMessage(HttpMethod.Post, config.Endpoint))
                {
                    message.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    string headerValue = BuildAuthHeaderValue(config);
                    if (!string.IsNullOrWhiteSpace(config.AuthHeader) && !string.IsNullOrWhiteSpace(headerValue))
                    {
                        message.Headers.TryAddWithoutValidation(config.AuthHeader, headerValue);
                    }
                    try
                    {
                        using (HttpResponseMessage response = await client.SendAsync(message).ConfigureAwait(false))
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            result.RawResponse = body;
                            if (!response.IsSuccessStatusCode)
                            {
                                result.Issues.Add(new AiFlowIssue("AI_HTTP_ERROR", $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", "ai-request"));
                                return result;
                            }
                            string content = ParseOpenAiContent(body, result.Issues);
                            if (result.Issues.Count == 0)
                            {
                                result.Content = content;
                            }
                            return result;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        result.Issues.Add(new AiFlowIssue("AI_HTTP_TIMEOUT", "请求超时", "ai-request"));
                        return result;
                    }
                    catch (Exception ex)
                    {
                        result.Issues.Add(new AiFlowIssue("AI_HTTP_FAIL", ex.Message, "ai-request"));
                        return result;
                    }
                }
            }
        }

        public static bool TryParseDelta(string json, out AiFlowDelta delta, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            delta = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                issues.Add(new AiFlowIssue("AI_PARSE_EMPTY", "输出为空", "ai-parse"));
                return false;
            }
            try
            {
                delta = JsonConvert.DeserializeObject<AiFlowDelta>(json, StrictSettings);
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("AI_PARSE_DELTA", ex.Message, "ai-parse"));
                return false;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("AI_PARSE_DELTA", ex.Message, "ai-parse"));
                return false;
            }
            if (delta == null)
            {
                issues.Add(new AiFlowIssue("AI_PARSE_DELTA", "delta 解析失败", "ai-parse"));
                return false;
            }
            if (!string.Equals(delta.Version, AiFlowDeltaApplier.DeltaVersion, StringComparison.Ordinal))
            {
                issues.Add(new AiFlowIssue("AI_DELTA_VERSION", $"delta 版本不匹配:{delta.Version}", "ai-parse"));
                return false;
            }
            return true;
        }

        public static bool TryParseCore(string json, out AiCoreFlow core, out List<AiFlowIssue> issues)
        {
            issues = new List<AiFlowIssue>();
            core = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                issues.Add(new AiFlowIssue("AI_PARSE_EMPTY", "输出为空", "ai-parse"));
                return false;
            }
            try
            {
                core = JsonConvert.DeserializeObject<AiCoreFlow>(json, StrictSettings);
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("AI_PARSE_CORE", ex.Message, "ai-parse"));
                return false;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("AI_PARSE_CORE", ex.Message, "ai-parse"));
                return false;
            }
            if (core == null)
            {
                issues.Add(new AiFlowIssue("AI_PARSE_CORE", "core 解析失败", "ai-parse"));
                return false;
            }
            if (!string.Equals(core.Version, AiFlowCompiler.CoreVersion, StringComparison.Ordinal))
            {
                issues.Add(new AiFlowIssue("AI_CORE_VERSION", $"core 版本不匹配:{core.Version}", "ai-parse"));
                return false;
            }
            return true;
        }

        private static string BuildSystemPrompt(string outputKind)
        {
            return "你是自动化流程生成助手。必须严格输出JSON，不要包含任何解释文字或Markdown。" +
                   "输出必须可被本地验证器通过，禁止跨流程goto，禁止直接填写Count/IOCount/ProcCount，" +
                   "Goto必须为\"proc-step-op\"格式且仅限当前流程。" +
                   $"输出类型:{outputKind}。";
        }

        private static string BuildUserPrompt(string request, string context, string outputKind)
        {
            string safeContext = string.IsNullOrWhiteSpace(context) ? "(无)" : context;
            return $"需求:\n{request}\n\n上下文:\n{safeContext}\n\n仅输出{outputKind}的JSON。";
        }

        private static string BuildAuthHeaderValue(AiFlowAiConfig config)
        {
            if (config == null)
            {
                return string.Empty;
            }
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                return string.Empty;
            }
            if (string.IsNullOrWhiteSpace(config.AuthPrefix))
            {
                return config.ApiKey;
            }
            return $"{config.AuthPrefix} {config.ApiKey}";
        }

        private static string ParseOpenAiContent(string body, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                issues.Add(new AiFlowIssue("AI_RESPONSE_EMPTY", "响应为空", "ai-response"));
                return null;
            }
            try
            {
                JObject root = JObject.Parse(body);
                JToken content = root["choices"]?[0]?["message"]?["content"];
                if (content == null)
                {
                    issues.Add(new AiFlowIssue("AI_RESPONSE_FORMAT", "响应不包含 choices[0].message.content", "ai-response"));
                    return null;
                }
                return content.ToString();
            }
            catch (JsonException ex)
            {
                issues.Add(new AiFlowIssue("AI_RESPONSE_PARSE", ex.Message, "ai-response"));
                return null;
            }
            catch (Exception ex)
            {
                issues.Add(new AiFlowIssue("AI_RESPONSE_PARSE", ex.Message, "ai-response"));
                return null;
            }
        }
    }
}
