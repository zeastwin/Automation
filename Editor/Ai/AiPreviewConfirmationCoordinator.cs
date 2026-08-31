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
            if (result["ok"]?.Value<bool>() != true || data == null)
            {
                return AiPreviewObservation.None;
            }
            string previewId = data?["previewId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(previewId)) return AiPreviewObservation.None;

            string resultType = result["type"]?.Value<string>() ?? string.Empty;
            AiPreviewObservationKind kind;
            JArray changes = null;
            JArray messages = null;
            if (string.Equals(resultType, "change_set.apply", StringComparison.Ordinal)
                || string.Equals(resultType, "migration.apply", StringComparison.Ordinal))
            {
                bool committed = string.Equals(resultType, "change_set.apply", StringComparison.Ordinal)
                    ? string.Equals(data["status"]?.Value<string>(), "committed", StringComparison.Ordinal)
                    : data["committed"]?.Value<bool>() == true;
                if (!committed || data["configurationSaved"]?.Value<bool>() != true)
                {
                    return AiPreviewObservation.None;
                }
                kind = AiPreviewObservationKind.Applied;
            }
            else if (string.Equals(resultType, "preview.reject", StringComparison.Ordinal))
            {
                if (data["rejected"]?.Value<bool>() != true)
                {
                    return AiPreviewObservation.None;
                }
                kind = AiPreviewObservationKind.Rejected;
            }
            else if (string.Equals(resultType, "change_set.preview", StringComparison.Ordinal)
                || string.Equals(resultType, "migration.preview", StringComparison.Ordinal))
            {
                bool? confirmed = data["confirmed"]?.Value<bool?>();
                string status = data["status"]?.Value<string>() ?? string.Empty;
                bool migrationPreview = string.Equals(resultType, "migration.preview", StringComparison.Ordinal);
                bool validStatus = migrationPreview
                    ? confirmed.HasValue && data["committed"]?.Value<bool>() == false
                    : confirmed == true
                        ? string.Equals(status, "confirmed", StringComparison.Ordinal)
                        : confirmed == false
                            && string.Equals(status, "awaiting_confirmation", StringComparison.Ordinal);
                if (!confirmed.HasValue || !validStatus)
                {
                    return AiPreviewObservation.None;
                }
                // 等待前台确认的结果必须携带 changes/messages 供弹窗展示；
                // 已确认结果（自动批准）不弹窗，允许投影结果省略展示字段。
                if (!confirmed.Value
                    && (data["changes"] is not JArray directChanges
                        || data["messages"] is not JArray directMessages))
                {
                    return AiPreviewObservation.None;
                }

                changes = data["changes"] as JArray;
                messages = data["messages"] as JArray;
                if (confirmed.Value)
                    kind = AiPreviewObservationKind.Confirmed;
                else if (autoApproveMode)
                    kind = AiPreviewObservationKind.AutoApprovalMismatch;
                else if (!presentedPreviewIds.Add(previewId))
                    kind = AiPreviewObservationKind.AlreadyPresented;
                else
                    kind = AiPreviewObservationKind.AwaitingConfirmation;
            }
            else
            {
                return AiPreviewObservation.None;
            }

            return new AiPreviewObservation(
                kind,
                previewId,
                resultType,
                changes,
                messages);
        }

        public void Reset()
        {
            presentedPreviewIds.Clear();
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
