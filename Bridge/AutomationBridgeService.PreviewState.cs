using Newtonsoft.Json;
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
                item != null && !item.Rejected && item.ExpiresAtUtc > DateTime.UtcNow);
            if (active != null)
            {
                bool canReplace = supportsExplicitReplacement && active.IsChangeSetPreview;
                var allowedTransitions = new JArray();
                if (canReplace)
                {
                    allowedTransitions.Add(new JObject
                    {
                        ["tool"] = "preview_change_set",
                        ["requiredArguments"] = new JArray("changeSet", "replacePreviewId"),
                        ["fixedArguments"] = new JObject { ["replacePreviewId"] = active.PreviewId },
                        ["changeSetMode"] = "complete_replacement",
                        ["previousPreviewActionsInherited"] = false,
                        ["previousPreviewLocalKeysInherited"] = false
                    });
                }
                if (active.IsChangeSetPreview && active.Confirmed)
                {
                    allowedTransitions.Add(new JObject
                    {
                        ["tool"] = "apply_change_set",
                        ["arguments"] = new JObject { ["previewId"] = active.PreviewId }
                    });
                }
                else if (active.MigrationConfigurationPreview != null && active.Confirmed)
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
            DateTime now = DateTime.UtcNow;
            List<string> expiredIds = previewRecords
                .Where(item => item.Value == null || item.Value.ExpiresAtUtc <= now)
                .Select(item => item.Key)
                .ToList();
            foreach (string expiredId in expiredIds)
            {
                previewRecords.Remove(expiredId);
            }
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void EnsurePreviewProcVersion(PreviewApprovalRecord record)
        {
            // 流程结构操作（创建/删除/重排/复制）不绑定单个 procIndex，跳过版本校验
            if (record.ProcIndex < 0)
            {
                return;
            }
            Proc current = GetProcByIndex(record.ProcIndex);
            string currentProcId = current.head?.Id.ToString("D") ?? string.Empty;
            if (!string.Equals(currentProcId, record.BaseProcId, StringComparison.OrdinalIgnoreCase))
            {
                throw new BridgeRequestException(409, "PROC_VERSION_MISMATCH", "流程版本已变化，请重新读取流程详情并重新预演。");
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
