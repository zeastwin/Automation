using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Automation.McpServer
{
    /// <summary>
    /// ProcessCreate 在首次提交后继续编辑同一新流程所需的最小作用域凭据。
    /// 凭据只收窄可写目标，不代替 ChangeSet 预演确认，也不授予其他流程权限。
    /// </summary>
    internal sealed class ProcessAuthoringLease
    {
        public ProcessAuthoringLease(string leaseId, string procId, string? processName)
        {
            LeaseId = leaseId;
            ProcId = procId;
            ProcessName = processName;
            CreatedUtc = DateTime.UtcNow;
        }

        public string LeaseId { get; }

        public string ProcId { get; }

        public string? ProcessName { get; }

        public DateTime CreatedUtc { get; }

        public JsonObject ToJson() => new JsonObject
        {
            ["leaseId"] = LeaseId,
            ["procId"] = ProcId,
            ["processName"] = ProcessName,
            ["purpose"] = "仅供当前ProcessCreate工作连续修改这个新建流程；后续preview_change_set原样传authoringLeaseId。"
        };
    }

    internal static class ProcessAuthoringLeaseRegistry
    {
        private static readonly TimeSpan LeaseLifetime = TimeSpan.FromHours(24);
        private static readonly ConcurrentDictionary<string, ProcessAuthoringLease> Leases =
            new ConcurrentDictionary<string, ProcessAuthoringLease>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string> PreviewLeaseIds =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> InitialPreviewIds =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public static ProcessAuthoringLease ResolveRequired(string leaseId)
        {
            CleanupExpired();
            string normalized = leaseId?.Trim() ?? string.Empty;
            if (normalized.Length == 0 || !Leases.TryGetValue(normalized, out ProcessAuthoringLease? lease))
            {
                throw new ArgumentException(
                    "authoringLeaseId 无效或已过期。不要猜测该值；请使用首次apply_change_set返回的authoringLease.leaseId，或切换ProcessEdit后按稳定procId继续。",
                    nameof(leaseId));
            }
            return lease;
        }

        public static ProcessAuthoringLease? RegisterCreatedProcess(string? rawApplyResult)
        {
            JsonObject? response = JsonNode.Parse(rawApplyResult ?? string.Empty) as JsonObject;
            if (response?["ok"]?.GetValue<bool>() != true
                || response["data"]?["createdObjects"]?["processes"] is not JsonArray processes
                || processes.Count != 1
                || processes[0] is not JsonObject process)
            {
                return null;
            }

            string procId = process["procId"]?.GetValue<string>()?.Trim() ?? string.Empty;
            if (!Guid.TryParse(procId, out _)) return null;

            string leaseId = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
            var lease = new ProcessAuthoringLease(
                leaseId,
                procId,
                process["name"]?.GetValue<string>());
            Leases[leaseId] = lease;
            return lease;
        }

        public static void BindPreview(string previewId, ProcessAuthoringLease lease)
        {
            string normalized = previewId?.Trim() ?? string.Empty;
            if (normalized.Length > 0 && lease != null)
                PreviewLeaseIds[normalized] = lease.LeaseId;
        }

        public static void BindInitialPreview(string previewId)
        {
            string normalized = previewId?.Trim() ?? string.Empty;
            if (normalized.Length > 0) InitialPreviewIds[normalized] = DateTime.UtcNow;
        }

        public static bool IsInitialPreview(string previewId)
        {
            CleanupExpired();
            string normalized = previewId?.Trim() ?? string.Empty;
            return normalized.Length > 0 && InitialPreviewIds.ContainsKey(normalized);
        }

        public static ProcessAuthoringLease? GetPreviewLease(string previewId)
        {
            CleanupExpired();
            string normalized = previewId?.Trim() ?? string.Empty;
            return normalized.Length > 0
                && PreviewLeaseIds.TryGetValue(normalized, out string? leaseId)
                && Leases.TryGetValue(leaseId, out ProcessAuthoringLease? lease)
                    ? lease
                    : null;
        }

        public static void CompletePreview(string previewId)
        {
            string normalized = previewId?.Trim() ?? string.Empty;
            if (normalized.Length > 0)
            {
                PreviewLeaseIds.TryRemove(normalized, out _);
                InitialPreviewIds.TryRemove(normalized, out _);
            }
        }

        public static string? ReadPreviewId(string? rawResult)
        {
            try
            {
                JsonObject? response = JsonNode.Parse(rawResult ?? string.Empty) as JsonObject;
                return response?["ok"]?.GetValue<bool>() == true
                    ? response["data"]?["previewId"]?.GetValue<string>()
                    : null;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                return null;
            }
        }

        public static string AttachToApplyResult(string result, ProcessAuthoringLease? lease)
        {
            if (lease == null) return result;
            try
            {
                JsonObject? response = JsonNode.Parse(result ?? string.Empty) as JsonObject;
                if (response?["ok"]?.GetValue<bool>() != true
                    || response["data"] is not JsonObject data)
                {
                    return result ?? string.Empty;
                }
                data["authoringLease"] = lease.ToJson();
                return response.ToJsonString();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                return result ?? string.Empty;
            }
        }

        private static void CleanupExpired()
        {
            DateTime cutoff = DateTime.UtcNow - LeaseLifetime;
            foreach (KeyValuePair<string, ProcessAuthoringLease> pair in Leases)
            {
                if (pair.Value.CreatedUtc < cutoff)
                    Leases.TryRemove(pair.Key, out _);
            }
            foreach (KeyValuePair<string, string> pair in PreviewLeaseIds)
            {
                if (!Leases.ContainsKey(pair.Value))
                    PreviewLeaseIds.TryRemove(pair.Key, out _);
            }
            foreach (KeyValuePair<string, DateTime> pair in InitialPreviewIds)
            {
                if (pair.Value < cutoff)
                    InitialPreviewIds.TryRemove(pair.Key, out _);
            }
        }
    }
}
