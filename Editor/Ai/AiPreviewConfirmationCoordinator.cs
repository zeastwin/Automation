// 模块：编辑器 / AI。
// 职责范围：AI 前台、ACP 会话、预演确认与对话渲染。

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Automation
{
    /// <summary>
    /// 将 ACP 工具结果归一化为前台可处理的预演状态，并保证同一预演只展示一次。
    /// </summary>
    internal sealed class AiPreviewConfirmationCoordinator
    {
        private readonly HashSet<string> presentedPreviewIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public AiPreviewObservation Observe(JObject raw, bool autoApproveMode)
        {
            string resultText = GooseAcpEventReader.ExtractToolResultText(raw);
            if (string.IsNullOrWhiteSpace(resultText)) return AiPreviewObservation.None;

            JObject result;
            try
            {
                result = JObject.Parse(resultText);
            }
            catch
            {
                return AiPreviewObservation.None;
            }

            JObject data = result["data"] as JObject;
            string previewId = data?["previewId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(previewId)) return AiPreviewObservation.None;

            string resultType = result["type"]?.Value<string>() ?? string.Empty;
            bool confirmed = data?["confirmed"]?.Value<bool?>()
                ?? data?["preview"]?["confirmed"]?.Value<bool?>()
                ?? false;
            bool committed = data?["committed"]?.Value<bool?>() == true;
            bool rejected = data?["rejected"]?.Value<bool?>() == true
                || string.Equals(resultType, "preview.reject", StringComparison.Ordinal);
            string mode = data?["mode"]?.Value<string>()
                ?? data?["apply"]?["mode"]?.Value<string>()
                ?? string.Empty;

            AiPreviewObservationKind kind;
            if (committed || string.Equals(mode, "apply", StringComparison.Ordinal))
                kind = AiPreviewObservationKind.Applied;
            else if (rejected)
                kind = AiPreviewObservationKind.Rejected;
            else if (confirmed)
                kind = AiPreviewObservationKind.Confirmed;
            else if (autoApproveMode)
                kind = AiPreviewObservationKind.AutoApprovalMismatch;
            else if (!presentedPreviewIds.Add(previewId))
                kind = AiPreviewObservationKind.AlreadyPresented;
            else
                kind = AiPreviewObservationKind.AwaitingConfirmation;

            return new AiPreviewObservation(
                kind,
                previewId,
                resultType,
                FindFirstArray(result, "changes"),
                FindFirstArray(result, "messages"));
        }

        public void Reset()
        {
            presentedPreviewIds.Clear();
        }

        private static JArray FindFirstArray(JToken token, string fieldName)
        {
            if (token == null) return null;
            if (token is JObject obj)
            {
                if (obj.TryGetValue(fieldName, StringComparison.OrdinalIgnoreCase, out JToken value)
                    && value is JArray direct)
                {
                    return direct;
                }
                foreach (JProperty property in obj.Properties())
                {
                    JArray nested = FindFirstArray(property.Value, fieldName);
                    if (nested != null) return nested;
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    JArray nested = FindFirstArray(item, fieldName);
                    if (nested != null) return nested;
                }
            }
            return null;
        }
    }

    internal enum AiPreviewObservationKind
    {
        None,
        AwaitingConfirmation,
        AlreadyPresented,
        Confirmed,
        Rejected,
        Applied,
        AutoApprovalMismatch
    }

    internal sealed class AiPreviewObservation
    {
        public static AiPreviewObservation None { get; } = new AiPreviewObservation(
            AiPreviewObservationKind.None, null, null, null, null);

        public AiPreviewObservation(
            AiPreviewObservationKind kind,
            string previewId,
            string resultType,
            JArray changes,
            JArray messages)
        {
            Kind = kind;
            PreviewId = previewId;
            ResultType = resultType;
            Changes = changes;
            Messages = messages;
        }

        public AiPreviewObservationKind Kind { get; }
        public string PreviewId { get; }
        public string ResultType { get; }
        public JArray Changes { get; }
        public JArray Messages { get; }
    }
}
