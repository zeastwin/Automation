using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。
// 状态所有权：预演的过期、确认、替换和删除都由本文件管理；聊天文本不是预演状态源。

using Newtonsoft.Json.Linq;
using Automation.Protocol;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using static System.ComponentModel.TypeConverter;

namespace Automation.Bridge
{
    internal sealed partial class AutomationBridgeService
    {
        private static int CountOperations(Proc proc)
        {
            return proc?.steps?.Sum(step => step?.Ops?.Count ?? 0) ?? 0;
        }

        private void EnsureNoActivePreviewLocked(bool supportsExplicitReplacement = false)
        {
            PreviewApprovalRecord active = previewRecords.Values.FirstOrDefault(item =>
                item != null && !item.Rejected && item.ExpiresAtUtc > previewUtcNow());
            if (active != null)
            {
                bool canReplace = supportsExplicitReplacement && active.IsChangeSetPreview;
                JArray allowedTransitions;
                if (active.IsChangeSetPreview)
                {
                    allowedTransitions = BuildChangeSetAllowedTransitions(
                        active,
                        includeReplacement: canReplace,
                        includeDiscard: false);
                }
                else
                {
                    allowedTransitions = new JArray();
                    if (active.MigrationConfigurationPreview != null && active.Confirmed)
                    {
                        allowedTransitions.Add(new JObject
                        {
                            ["tool"] = "apply_migration_configuration",
                            ["arguments"] = new JObject { ["previewId"] = active.PreviewId }
                        });
                    }
                    else if (!active.Confirmed)
                    {
                        allowedTransitions.Add(new JObject
                        {
                            ["state"] = "awaiting_foreground_confirmation"
                        });
                    }
                }
                throw new BridgeRequestException(
                    409,
                    "PREVIEW_IN_FLIGHT",
                    "已有一个尚未结束的预演，本次新预演未创建。",
                    new JObject
                    {
                        ["activePreviewId"] = active.PreviewId,
                        ["confirmed"] = active.Confirmed,
                        ["allowedTransitions"] = allowedTransitions,
                        ["retryableWhen"] = canReplace
                            ? "complete_replacement_change_set_retried_with_replace_preview_id"
                            : "active_preview_committed_discarded_or_expired",
                        ["sideEffects"] = "none"
                    }.ToString(Formatting.None));
            }
        }

        private void RemovePreview(string previewId)
        {
            lock (previewLock)
            {
                previewRecords.Remove(previewId);
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private static void ValidatePreviewIdFormat(string previewId)
        {
            if (string.IsNullOrWhiteSpace(previewId))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "previewId 需要使用预演工具返回的32位编号。");
            }

            if (!Guid.TryParseExact(previewId, "N", out _))
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", $"previewId 不是合法的32位预演编号：{previewId}");
            }
        }

        private void CleanupExpiredPreviewsLocked()
        {
            DateTime now = previewUtcNow();
            List<string> expiredIds = previewRecords
                .Where(item => item.Value == null || item.Value.ExpiresAtUtc <= now)
                .Select(item => item.Key)
                .ToList();
            foreach (string expiredId in expiredIds)
            {
                previewRecords.Remove(expiredId);
            }
        }

        private static string ComputePatchHash(JObject patch)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(patch.ToString(Formatting.None));
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

    }
}
