using System;
// 模块：引擎 / 执行。
// 职责范围：只为数据通信指令提供固定间隔重试，并复用逻辑判断语义校验接收结果。

using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation
{
    public partial class ProcessEngine
    {
        private enum CommunicationResponseEvaluation
        {
            Matched,
            Mismatched,
            ConfigurationError
        }

        private sealed class RetryableCommunicationException : Exception
        {
            public RetryableCommunicationException(string message) : base(message)
            {
            }
        }

        private bool ExecuteCommunicationOperation(
            ProcHandle evt, CommunicationOperationType operation)
        {
            if (operation.RetryCount < 0 || operation.RetryCount > 10
                || operation.RetryIntervalMs < 0 || operation.RetryIntervalMs > 60000)
            {
                MarkAlarm(evt, "通信重试次数或重试间隔无效");
                return false;
            }

            int maximumAttempts = operation.RetryCount + 1;
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                bool retryable = false;
                bool result;
                try
                {
                    result = ExecuteOperationOnce(evt, operation);
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    if (!result || evt.HasAlarm)
                    {
                        return result;
                    }

                    if (operation.RetryCount <= 0
                        || !(operation is ResponseCommunicationOperationType responseOperation)
                        || !responseOperation.ShouldEvaluateResponseConditions
                        || responseOperation.ResponseConditions == null
                        || responseOperation.ResponseConditions.Count == 0)
                    {
                        return true;
                    }

                    CommunicationResponseEvaluation evaluation = EvaluateCommunicationResponse(
                        evt, responseOperation, out string evaluationMessage);
                    if (evaluation == CommunicationResponseEvaluation.Matched)
                    {
                        return true;
                    }
                    MarkAlarm(evt, evaluationMessage);
                    if (evaluation == CommunicationResponseEvaluation.ConfigurationError)
                    {
                        return false;
                    }
                    retryable = true;
                    result = false;
                }
                catch (RetryableCommunicationException ex)
                {
                    MarkAlarm(evt, ex.Message);
                    retryable = true;
                    result = false;
                }
                catch (OperationCanceledException) when (evt.CancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    MarkAlarm(evt, ex.Message);
                    return false;
                }

                if (!retryable || attempt >= maximumAttempts)
                {
                    return result;
                }

                string failure = evt.alarmMsg;
                evt.RunMetrics?.RecordRetry();
                Logger?.Log(
                    $"流程{evt.procNum}通信指令[{operation.Name ?? operation.OperaType}]第{attempt}次执行失败，"
                    + $"{operation.RetryIntervalMs}ms后固定间隔重试:{failure}",
                    LogLevel.Normal);
                evt.alarmMsg = null;
                if (operation.RetryIntervalMs > 0)
                {
                    Delay(operation.RetryIntervalMs, evt);
                }
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            }
            return false;
        }

        private CommunicationResponseEvaluation EvaluateCommunicationResponse(
            ProcHandle evt,
            ResponseCommunicationOperationType operation,
            out string message)
        {
            message = null;
            CommunicationResponseRuntimeBinding binding =
                operation.RuntimeBinding as CommunicationResponseRuntimeBinding;
            if (binding == null
                || binding.Sources.Length != operation.ResponseConditions.Count)
            {
                message = "通信结果判定运行计划未编译";
                return CommunicationResponseEvaluation.ConfigurationError;
            }

            bool combined = true;
            for (int index = 0; index < operation.ResponseConditions.Count; index++)
            {
                CommunicationResponseCondition condition = operation.ResponseConditions[index];
                string resolveError = null;
                if (condition == null
                    || !binding.Sources[index].TryResolveValue(
                        Context?.ValueStore, "通信结果判定变量", evt.procId,
                        out DicValue source, out resolveError))
                {
                    message = resolveError ?? $"通信结果判定条件{index + 1}为空";
                    return CommunicationResponseEvaluation.ConfigurationError;
                }

                CommunicationResponseEvaluation itemEvaluation = EvaluateCommunicationCondition(
                    condition, source.Value ?? string.Empty, index, out bool matched, out message);
                if (itemEvaluation != CommunicationResponseEvaluation.Matched)
                {
                    return itemEvaluation;
                }
                if (index == 0)
                {
                    combined = matched;
                }
                else if (condition.Operator == "且")
                {
                    combined = combined && matched;
                }
                else if (condition.Operator == "或")
                {
                    combined = combined || matched;
                }
                else
                {
                    message = $"通信结果判定条件{index + 1}运算符无效:{condition.Operator}";
                    return CommunicationResponseEvaluation.ConfigurationError;
                }
            }

            if (combined)
            {
                return CommunicationResponseEvaluation.Matched;
            }
            message = "通信接收结果不符合判定条件";
            return CommunicationResponseEvaluation.Mismatched;
        }

        private static CommunicationResponseEvaluation EvaluateCommunicationCondition(
            CommunicationResponseCondition condition,
            string sourceValue,
            int index,
            out bool matched,
            out string message)
        {
            matched = false;
            message = null;
            string value = sourceValue;
            bool fieldExists = true;
            if (!string.IsNullOrWhiteSpace(condition.JsonFieldPath))
            {
                if (!TryReadJsonField(sourceValue, condition.JsonFieldPath,
                    out fieldExists, out value, out string jsonError))
                {
                    message = $"通信结果判定条件{index + 1}:{jsonError}";
                    return CommunicationResponseEvaluation.Mismatched;
                }
            }

            switch (condition.JudgeMode)
            {
                case "字段存在":
                    if (string.IsNullOrWhiteSpace(condition.JsonFieldPath))
                    {
                        message = $"通信结果判定条件{index + 1}使用字段存在时必须配置JSON字段路径";
                        return CommunicationResponseEvaluation.ConfigurationError;
                    }
                    matched = fieldExists;
                    return CommunicationResponseEvaluation.Matched;
                case "非空":
                    matched = fieldExists && !string.IsNullOrEmpty(value);
                    return CommunicationResponseEvaluation.Matched;
                case "等于特征字符":
                    matched = fieldExists && string.Equals(
                        value, condition.ExpectedText ?? string.Empty, StringComparison.Ordinal);
                    return CommunicationResponseEvaluation.Matched;
                case "包含特征字符":
                    matched = fieldExists && value != null
                        && value.IndexOf(condition.ExpectedText ?? string.Empty, StringComparison.Ordinal) >= 0;
                    return CommunicationResponseEvaluation.Matched;
                case "值在区间左":
                case "值在区间右":
                case "值在区间内":
                    if (!fieldExists || !double.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number))
                    {
                        matched = false;
                        return CommunicationResponseEvaluation.Matched;
                    }
                    if (condition.JudgeMode == "值在区间左")
                    {
                        matched = condition.IncludeBoundary
                            ? number <= condition.Down
                            : number < condition.Down;
                    }
                    else if (condition.JudgeMode == "值在区间右")
                    {
                        matched = condition.IncludeBoundary
                            ? number >= condition.Down
                            : number > condition.Down;
                    }
                    else
                    {
                        matched = condition.IncludeBoundary
                            ? condition.Down <= number && number <= condition.Up
                            : condition.Down < number && number < condition.Up;
                    }
                    return CommunicationResponseEvaluation.Matched;
                default:
                    message = $"通信结果判定条件{index + 1}判断模式无效:{condition.JudgeMode}";
                    return CommunicationResponseEvaluation.ConfigurationError;
            }
        }

        private static bool TryReadJsonField(
            string source,
            string path,
            out bool fieldExists,
            out string value,
            out string error)
        {
            fieldExists = false;
            value = null;
            error = null;
            JToken current;
            try
            {
                current = JToken.Parse(source ?? string.Empty);
            }
            catch (JsonException ex)
            {
                error = $"接收内容不是有效JSON:{ex.Message}";
                return false;
            }
            foreach (string segment in path.Split('.'))
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    error = $"JSON字段路径无效:{path}";
                    return false;
                }
                if (current is JObject obj)
                {
                    if (!obj.TryGetValue(segment, StringComparison.Ordinal, out current))
                    {
                        return true;
                    }
                    continue;
                }
                if (current is JArray array
                    && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture,
                        out int arrayIndex)
                    && arrayIndex >= 0 && arrayIndex < array.Count)
                {
                    current = array[arrayIndex];
                    continue;
                }
                return true;
            }
            fieldExists = current != null && current.Type != JTokenType.Undefined;
            if (fieldExists)
            {
                value = current.Type == JTokenType.String
                    ? current.Value<string>()
                    : current.ToString(Formatting.None);
            }
            return true;
        }

        private Exception CreateRetryableCommunicationException(ProcHandle evt, string message)
        {
            MarkAlarm(evt, message);
            return new RetryableCommunicationException(message);
        }

        private T ExecuteRetryableCommunicationCall<T>(
            ProcHandle evt, string failurePrefix, Func<T> action)
        {
            try
            {
                return action();
            }
            catch (OperationCanceledException) when (evt.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateRetryableCommunicationException(
                    evt, $"{failurePrefix}:{ex.Message}");
            }
        }
    }
}
