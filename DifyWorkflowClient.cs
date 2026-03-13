using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Automation
{
    public sealed class DifyWorkflowFieldDefinition
    {
        public string ControlType { get; set; }
        public string Label { get; set; }
        public string Variable { get; set; }
        public bool Required { get; set; }
        public JToken DefaultValue { get; set; }
        public JObject RawDefinition { get; set; }
    }

    public sealed class DifyWorkflowParameters
    {
        public List<DifyWorkflowFieldDefinition> Fields { get; set; } = new List<DifyWorkflowFieldDefinition>();
        public JArray RawUserInputForm { get; set; } = new JArray();
    }

    public sealed class DifyWorkflowRunReply
    {
        public string WorkflowRunId { get; set; }
        public string TaskId { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
        public JObject Outputs { get; set; } = new JObject();
    }

    public static class DifyWorkflowClient
    {
        private const int DefaultRequestTimeoutMs = 120000;

        public static DifyWorkflowParameters GetParameters(DifyConfig config)
        {
            ValidateConfig(config);
            string endpoint = BuildEndpoint(config.BaseUrl, "/parameters");
            string responseText = SendRequest(config.ApiKey, endpoint, "GET", null);
            return ParseParameters(responseText);
        }

        public static DifyWorkflowRunReply RunWorkflow(DifyConfig config, JObject inputs, string userId, Action<string> progress = null)
        {
            ValidateConfig(config);
            if (inputs == null)
            {
                throw new InvalidOperationException("Workflow inputs 不能为空。");
            }

            string endpoint = BuildEndpoint(config.BaseUrl, "/workflows/run");
            JObject payload = new JObject
            {
                ["inputs"] = inputs,
                ["response_mode"] = "blocking",
                ["user"] = string.IsNullOrWhiteSpace(userId) ? "automation-user" : userId
            };
            progress?.Invoke("本地上下文已收集，正在向 Dify 提交 workflow 请求。");
            string responseText = SendRequest(config.ApiKey, endpoint, "POST", payload.ToString(), progress, true);
            progress?.Invoke("Dify 已返回响应，正在解析 workflow 结果。");
            return ParseWorkflowRunReply(responseText);
        }

        private static void ValidateConfig(DifyConfig config)
        {
            if (config == null)
            {
                throw new InvalidOperationException("Dify 配置为空。");
            }
            if (string.IsNullOrWhiteSpace(config.BaseUrl))
            {
                throw new InvalidOperationException("Dify API 地址未配置。");
            }
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                throw new InvalidOperationException("Dify API Key 未配置。");
            }
        }

        private static string BuildEndpoint(string baseUrl, string relativePath)
        {
            string trimmed = (baseUrl ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                throw new InvalidOperationException("Dify API 地址不能为空。");
            }
            return $"{trimmed.TrimEnd('/')}{relativePath}";
        }

        private static string SendRequest(string apiKey, string endpoint, string method, string body, Action<string> progress = null, bool blockingWorkflow = false)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = method;
            request.Accept = "application/json";
            request.Timeout = DefaultRequestTimeoutMs;
            request.ReadWriteTimeout = DefaultRequestTimeoutMs;
            request.Headers[HttpRequestHeader.Authorization] = $"Bearer {apiKey}";
            progress?.Invoke($"正在请求 Dify：{method} {endpoint}");

            if (string.Equals(method, "POST", StringComparison.Ordinal))
            {
                request.ContentType = "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
                progress?.Invoke("正在发送 workflow 请求体。");
                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(bytes, 0, bytes.Length);
                }
                if (blockingWorkflow)
                {
                    progress?.Invoke($"请求已提交到 Dify，当前为 blocking 模式，正在等待 Dify 返回。超时阈值 {request.Timeout / 1000} 秒。");
                }
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    progress?.Invoke($"Dify 已返回 HTTP {(int)response.StatusCode} {response.StatusCode}，累计耗时 {stopwatch.ElapsedMilliseconds}ms。");
                    string responseText = reader.ReadToEnd();
                    progress?.Invoke($"Dify 响应体读取完成，累计耗时 {stopwatch.ElapsedMilliseconds}ms。");
                    return responseText;
                }
            }
            catch (WebException ex)
            {
                string message = BuildRequestFailureMessage(ex, endpoint, method, stopwatch.ElapsedMilliseconds, request.Timeout, blockingWorkflow);
                progress?.Invoke(message);
                throw new InvalidOperationException(message, ex);
            }
        }

        private static DifyWorkflowParameters ParseParameters(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                throw new InvalidOperationException("Dify workflow 参数返回为空。");
            }

            JObject obj = JObject.Parse(responseText);
            JToken formToken = obj["user_input_form"];
            if (formToken == null)
            {
                throw new InvalidOperationException($"Dify workflow 参数返回缺少 user_input_form:{responseText}");
            }
            if (formToken.Type != JTokenType.Array)
            {
                throw new InvalidOperationException($"Dify workflow 参数 user_input_form 类型无效:{responseText}");
            }

            DifyWorkflowParameters parameters = new DifyWorkflowParameters
            {
                RawUserInputForm = (JArray)formToken.DeepClone()
            };

            foreach (JToken itemToken in (JArray)formToken)
            {
                if (itemToken.Type != JTokenType.Object)
                {
                    continue;
                }

                JObject itemObject = (JObject)itemToken;
                foreach (JProperty property in itemObject.Properties())
                {
                    if (property.Value?.Type != JTokenType.Object)
                    {
                        continue;
                    }

                    JObject definition = (JObject)property.Value;
                    parameters.Fields.Add(new DifyWorkflowFieldDefinition
                    {
                        ControlType = property.Name,
                        Label = definition["label"]?.Value<string>() ?? string.Empty,
                        Variable = definition["variable"]?.Value<string>() ?? string.Empty,
                        Required = definition["required"]?.Value<bool>() ?? false,
                        DefaultValue = definition["default"]?.DeepClone(),
                        RawDefinition = (JObject)definition.DeepClone()
                    });
                }
            }

            return parameters;
        }

        private static DifyWorkflowRunReply ParseWorkflowRunReply(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                throw new InvalidOperationException("Dify workflow 返回为空。");
            }

            JObject obj = JObject.Parse(responseText);
            DifyWorkflowRunReply reply = new DifyWorkflowRunReply
            {
                WorkflowRunId = obj["workflow_run_id"]?.Value<string>() ?? string.Empty,
                TaskId = obj["task_id"]?.Value<string>() ?? string.Empty
            };

            JToken dataToken = obj["data"];
            if (dataToken != null)
            {
                if (dataToken.Type != JTokenType.Object)
                {
                    throw new InvalidOperationException($"Dify workflow 返回 data 类型无效:{responseText}");
                }

                JObject dataObject = (JObject)dataToken;
                reply.Status = dataObject["status"]?.Value<string>() ?? string.Empty;
                reply.Error = dataObject["error"]?.Value<string>() ?? string.Empty;

                JToken outputsToken = dataObject["outputs"];
                if (outputsToken != null)
                {
                    if (outputsToken.Type != JTokenType.Object)
                    {
                        throw new InvalidOperationException($"Dify workflow 返回 outputs 类型无效:{responseText}");
                    }
                    reply.Outputs = (JObject)outputsToken.DeepClone();
                }
            }

            return reply;
        }

        private static string ReadErrorResponse(WebException ex)
        {
            if (ex?.Response == null)
            {
                return ex?.Message ?? "未知网络错误";
            }

            try
            {
                using (Stream responseStream = ex.Response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    string text = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return ex.Message;
                    }

                    try
                    {
                        JObject obj = JObject.Parse(text);
                        JToken messageToken = obj["message"];
                        if (messageToken != null && messageToken.Type == JTokenType.String)
                        {
                            return messageToken.Value<string>();
                        }
                    }
                    catch
                    {
                    }

                    return text;
                }
            }
            catch
            {
                return ex.Message;
            }
        }

        private static string BuildRequestFailureMessage(WebException ex, string endpoint, string method, long elapsedMs, int timeoutMs, bool blockingWorkflow)
        {
            string detail = ReadErrorResponse(ex);
            HttpWebResponse response = ex?.Response as HttpWebResponse;
            string message;
            if (ex != null && (ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.RequestCanceled))
            {
                message = $"Dify 请求超时：{method} {endpoint}，已等待 {elapsedMs}ms，超时阈值 {timeoutMs}ms。";
            }
            else if (response != null)
            {
                message = $"Dify 请求失败：{method} {endpoint}，HTTP {(int)response.StatusCode} {response.StatusCode}，耗时 {elapsedMs}ms。";
            }
            else
            {
                string statusText = ex == null ? "Unknown" : ex.Status.ToString();
                message = $"Dify 请求失败：{method} {endpoint}，网络状态 {statusText}，耗时 {elapsedMs}ms。";
            }

            if (blockingWorkflow)
            {
                message += " 当前请求为 blocking workflow；若长时间无返回，通常表示 Dify 内部模型推理、Workflow 节点或 MCP 工具调用尚未完成。";
            }

            if (!string.IsNullOrWhiteSpace(detail))
            {
                message += " 详情：" + detail;
            }

            return message;
        }
    }
}
