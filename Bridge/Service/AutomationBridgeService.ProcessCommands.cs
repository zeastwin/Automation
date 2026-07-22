using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。

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
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleConfirmPreview(JObject request)
        {
            string previewId = ReadRequiredString(request, "previewId");
            PreviewApprovalRecord record;
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out record))
                {
                    return BridgeError(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                }
                if (record.Rejected)
                {
                    return BridgeError(409, "PREVIEW_REJECTED", $"预演已结束，不能再次确认：{previewId}");
                }

                EnsurePreviewProcVersion(record);
                record.Confirmed = true;
                record.ConfirmedAtUtc = previewUtcNow();
                Monitor.PulseAll(previewLock);
            }

            return new JObject
            {
                ["previewId"] = record.PreviewId,
                ["patchHash"] = record.PatchHash,
                ["procIndex"] = record.ProcIndex,
                ["baseProcId"] = record.BaseProcId,
                ["confirmed"] = true,
                ["expiresAt"] = record.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
        }

        private JObject HandleRejectPreview(JObject request)
        {
            string previewId = ReadRequiredString(request, "previewId");
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record))
                {
                    return BridgeError(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                }
                record.Rejected = true;
                Monitor.PulseAll(previewLock);
            }
            return new JObject { ["previewId"] = previewId, ["rejected"] = true };
        }

        // 提交请求可能在用户操作审核窗口前抵达。在 Bridge 工作线程等待审核结果，
        // 不占用 UI 线程；确认后原提交直接继续，拒绝则明确终止。
        private void WaitForPreviewConfirmation(JObject request, bool previewIdRequired = true)
        {
            string previewId = ReadOptionalString(request, "previewId");
            if (string.IsNullOrWhiteSpace(previewId))
            {
                if (previewIdRequired)
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT", "提交阶段必须携带 previewId。");
                }
                return;
            }
            ValidatePreviewIdFormat(previewId);
            DateTime deadline = previewUtcNow().AddSeconds(110);
            lock (previewLock)
            {
                while (true)
                {
                    CleanupExpiredPreviewsLocked();
                    if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record))
                    {
                        throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                    }
                    if (record.Rejected)
                    {
                        throw new BridgeRequestException(409, "PREVIEW_REJECTED", "预演已结束或被替换，本次提交未执行。");
                    }
                    if (record.Confirmed)
                    {
                        return;
                    }
                    TimeSpan remaining = deadline - previewUtcNow();
                    if (remaining <= TimeSpan.Zero)
                    {
                        throw new BridgeRequestException(408, "PREVIEW_CONFIRM_TIMEOUT", "等待前台确认预演超时，未执行提交。");
                    }
                    Monitor.Wait(previewLock, remaining);
                }
            }
        }

        // 流程运行控制：启动/停止/暂停/恢复。不需要预演确认。
        [System.Diagnostics.DebuggerNonUserCode]
        private JObject HandleControlProc(JObject request)
        {
            EnsureRuntimeReady();
            int procIndex = ReadRequiredInt(request, "procIndex");
            string action = ReadRequiredString(request, "action");
            Proc proc = GetProcByIndex(procIndex);
            EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
            ProcRunState currentState = snapshot?.State ?? ProcRunState.Stopped;

            switch (action)
            {
                case "start":
                    if (currentState != ProcRunState.Stopped)
                    {
                        return BridgeError(409, "PROC_NOT_STOPPED",
                            $"流程 {procIndex} 尚未结束，当前状态为 {currentState}。请排查流程未结束原因后再启动。");
                    }
                    if (!runtime.ProcessEngine.StartProc(proc, procIndex))
                    {
                        string startError;
                        if (!runtime.ProcessEngine.TryValidateProcessStopped(procIndex, out string stoppedError))
                        {
                            startError = stoppedError;
                        }
                        else
                        {
                            startError = runtime.ProcessEngine.TryValidateProcessStart(proc, procIndex, out string gateError)
                                ? "流程启动请求未被内核接受，详见流程日志。"
                                : gateError;
                        }
                        return BridgeError(409, "START_GATE_REJECTED", startError);
                    }
                    break;
                case "stop":
                    if (currentState == ProcRunState.Stopped)
                    {
                        return BridgeError(409, "PROC_NOT_RUNNING", $"流程 {procIndex} 未在运行。");
                    }
                    runtime.ProcessEngine.Stop(procIndex);
                    break;
                case "pause":
                    if (currentState != ProcRunState.Running)
                    {
                        return BridgeError(409, "PROC_NOT_RUNNING", $"流程 {procIndex} 不在运行状态，无法暂停。");
                    }
                    runtime.ProcessEngine.Pause(procIndex);
                    break;
                case "resume":
                    if (currentState != ProcRunState.Paused)
                    {
                        return BridgeError(409, "PROC_NOT_PAUSED", $"流程 {procIndex} 不在暂停状态，无法恢复。");
                    }
                    runtime.ProcessEngine.Resume(procIndex);
                    break;
                default:
                    return BridgeError(400, "UNSUPPORTED_ACTION", $"不支持的流程控制操作：{action}。支持：start, stop, pause, resume");
            }

            return new JObject
            {
                ["procIndex"] = procIndex,
                ["action"] = action,
                ["procName"] = proc?.head?.Name ?? string.Empty,
                ["previousState"] = currentState.ToString(),
                ["message"] = $"已发送{action}命令到流程 {procIndex}"
            };
        }

        private JObject HandleWaitForProcState(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int timeoutMs = ReadOptionalInt(request, "timeoutMs") ?? 30000;
            if (timeoutMs < 100 || timeoutMs > 60000)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "timeoutMs 必须在 100..60000 之间。");
            }
            JArray statesToken = ReadOptionalArray(request, "states") ?? new JArray("Stopped", "Alarming");
            if (statesToken.Count < 1 || statesToken.Count > 4)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "states 必须包含 1..4 个目标状态。");
            }
            var targetStates = new HashSet<ProcRunState>();
            foreach (JToken stateToken in statesToken)
            {
                if (stateToken.Type != JTokenType.String
                    || !Enum.TryParse(stateToken.Value<string>(), false, out ProcRunState state))
                {
                    throw new BridgeRequestException(400, "INVALID_ARGUMENT",
                        $"不支持的流程状态：{stateToken}. 可选 Stopped/Running/Paused/Alarming/Stopping。");
                }
                targetStates.Add(state);
            }

            DateTime startedAt = DateTime.UtcNow;
            EngineSnapshot snapshot;
            Guid expectedProcId = Guid.Empty;
            while (true)
            {
                if (runtime.ProcessEngine == null || runtime.ProcessEngine.Context?.Procs == null)
                {
                    throw new BridgeRequestException(503, "RUNTIME_NOT_READY", "流程运行时尚未初始化。");
                }
                if (procIndex < 0 || procIndex >= runtime.ProcessEngine.Context.Procs.Count)
                {
                    throw new BridgeRequestException(404, "PROC_NOT_FOUND", $"流程索引不存在：{procIndex}");
                }
                Guid currentProcId = runtime.ProcessEngine.Context.Procs[procIndex]?.head?.Id ?? Guid.Empty;
                if (expectedProcId == Guid.Empty) expectedProcId = currentProcId;
                else if (currentProcId != expectedProcId)
                {
                    throw new BridgeRequestException(409, "PROC_ID_CHANGED",
                        $"等待期间流程索引 {procIndex} 已指向其他流程，已停止等待以避免误判。");
                }
                snapshot = runtime.ProcessEngine.GetSnapshot(procIndex);
                if (snapshot != null && targetStates.Contains(snapshot.State))
                {
                    break;
                }
                if ((DateTime.UtcNow - startedAt).TotalMilliseconds >= timeoutMs)
                {
                    return new JObject
                    {
                        ["reached"] = false,
                        ["timedOut"] = true,
                        ["elapsedMs"] = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                        ["snapshot"] = snapshot == null ? null : BuildEngineSnapshot(snapshot, procIndex)
                    };
                }
                Thread.Sleep(50);
            }
            return new JObject
            {
                ["reached"] = true,
                ["timedOut"] = false,
                ["elapsedMs"] = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                ["snapshot"] = BuildEngineSnapshot(snapshot, procIndex)
            };
        }

        private JObject HandleRunProcTest(JObject request)
        {
            int procIndex = ReadRequiredInt(request, "procIndex");
            int durationMs = ReadOptionalInt(request, "durationMs") ?? 5000;
            if (durationMs < 500 || durationMs > 15000)
            {
                throw new BridgeRequestException(400, "INVALID_ARGUMENT", "durationMs 必须在 500..15000 之间。");
            }

            Guid procId = Guid.Empty;
            string procName = string.Empty;
            ExecuteOnUiThread(() =>
            {
                EnsureRuntimeReady();
                Proc proc = GetProcByIndex(procIndex);
                EngineSnapshot before = runtime.ProcessEngine.GetSnapshot(procIndex);
                if (before != null && before.State != ProcRunState.Stopped)
                {
                    throw new BridgeRequestException(409, "PROC_ALREADY_RUNNING",
                        $"流程 {procIndex} 已处于 {before.State}，测试运行不会接管已有运行实例。");
                }
                procId = proc.head?.Id ?? Guid.Empty;
                procName = proc.head?.Name ?? string.Empty;
                if (!runtime.ProcessEngine.StartProc(proc, procIndex))
                {
                    string startError = runtime.ProcessEngine.TryValidateProcessStart(proc, procIndex, out string gateError)
                        ? "流程测试启动请求未被内核接受，详见流程日志。"
                        : gateError;
                    throw new BridgeRequestException(409, "START_GATE_REJECTED", startError);
                }
                return true;
            });

            DateTime startedAt = DateTime.UtcNow;
            bool observedRunning = false;
            int positionChanges = 0;
            string lastPosition = null;
            EngineSnapshot snapshot = null;
            bool stoppedByTestRunner = false;
            ProcTerminationReason requestedReason = ProcTerminationReason.TestWindowElapsed;
            try
            {
                while ((DateTime.UtcNow - startedAt).TotalMilliseconds < durationMs)
                {
                    snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
                    if (snapshot != null)
                    {
                        if (snapshot.ProcId != procId)
                        {
                            throw new BridgeRequestException(409, "PROC_ID_CHANGED",
                                $"测试期间流程索引 {procIndex} 已指向其他流程，已中止结果判定。");
                        }
                        observedRunning |= snapshot.State == ProcRunState.Running
                            || snapshot.State == ProcRunState.Paused
                            || snapshot.State == ProcRunState.SingleStep;
                        string position = $"{snapshot.StepIndex}-{snapshot.OpIndex}";
                        if (lastPosition != null && !string.Equals(lastPosition, position, StringComparison.Ordinal))
                        {
                            positionChanges++;
                        }
                        lastPosition = position;
                        if (snapshot.State == ProcRunState.Alarming)
                        {
                            requestedReason = ProcTerminationReason.Alarm;
                            break;
                        }
                        if (snapshot.State == ProcRunState.Stopped)
                        {
                            break;
                        }
                    }
                    Thread.Sleep(50);
                }
            }
            finally
            {
                ExecuteOnUiThread(() =>
                {
                    EngineSnapshot current = runtime.ProcessEngine?.GetSnapshot(procIndex);
                    if (current != null && current.ProcId == procId && current.State != ProcRunState.Stopped)
                    {
                        runtime.ProcessEngine.Stop(procIndex, requestedReason);
                        stoppedByTestRunner = true;
                    }
                    return true;
                });
            }

            DateTime stopDeadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
                if (snapshot != null && snapshot.State == ProcRunState.Stopped)
                {
                    break;
                }
                Thread.Sleep(25);
            }
            while (DateTime.UtcNow < stopDeadline);

            if (snapshot == null || snapshot.State != ProcRunState.Stopped)
            {
                throw new BridgeRequestException(500, "TEST_RUN_STOP_TIMEOUT",
                    $"流程 {procIndex} 测试窗口结束后未能在 3 秒内停止，已保持安全停止请求，请人工检查设备与流程状态。");
            }

            string outcome;
            switch (snapshot.TerminationReason)
            {
                case ProcTerminationReason.Completed:
                    outcome = "NaturallyCompleted";
                    break;
                case ProcTerminationReason.Disabled:
                    outcome = "NotExecutedDisabled";
                    break;
                case ProcTerminationReason.TestWindowElapsed:
                    outcome = "ObservationWindowCompleted";
                    break;
                case ProcTerminationReason.Alarm:
                    outcome = "Alarmed";
                    break;
                default:
                    outcome = "ExternallyStopped";
                    break;
            }
            return new JObject
            {
                ["procIndex"] = procIndex,
                ["procId"] = procId.ToString("D"),
                ["procName"] = procName,
                ["outcome"] = outcome,
                ["observedRunning"] = observedRunning,
                ["positionChanges"] = positionChanges,
                ["stoppedByTestRunner"] = stoppedByTestRunner,
                ["continuationAuthorized"] = false,
                ["startRequiresExplicitUserRequest"] = true,
                ["elapsedMs"] = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                ["snapshot"] = BuildEngineSnapshot(snapshot, procIndex),
                ["runtimeEvidence"] = runtime.RuntimeBlackBoxRecorder?.BuildEvidencePackage(procIndex)
                    ?? RuntimeBlackBoxRecorder.BuildUnavailableEvidencePackage(procIndex)
            };
        }

        private void EnsureAllProcsStoppedForAiStructureCommit(string actionName)
        {
            int procCount = runtime.Stores.Processes?.Items?.Count ?? 0;
            for (int procIndex = 0; procIndex < procCount; procIndex++)
            {
                EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(procIndex);
                if (snapshot != null && snapshot.State != ProcRunState.Stopped)
                {
                    throw new BridgeRequestException(
                        409,
                        "PROC_STRUCTURE_NOT_STOPPED",
                        $"流程 {procIndex} 当前为 {snapshot.State}，{actionName}尚未执行。",
                        new JObject
                        {
                            ["blockingProcIndex"] = procIndex,
                            ["currentState"] = snapshot.State.ToString(),
                            ["retryableNow"] = false,
                            ["retryableWhen"] = "all_processes_stopped",
                            ["sideEffects"] = "none"
                        }.ToString(Formatting.None));
                }
            }
        }

        // 流程结构操作的预演记录，复用 previewLock 保证线程安全。
        private string RegisterManagePreview(
            JObject previewData,
            string replacePreviewId = null,
            bool supportsExplicitReplacement = false)
        {
            string previewId = Guid.NewGuid().ToString("N");
            // 自动批准模式：直接标记预演为已确认，避免 FrmAiAssistant 通过 HTTP 回调确认导致 UI 线程死锁。
            bool autoConfirmed = runtime.EditorUi?.IsAutoApproveMode == true;
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                string replacedPreviewId = null;
                if (!string.IsNullOrWhiteSpace(replacePreviewId))
                {
                    ValidatePreviewIdFormat(replacePreviewId);
                    if (!previewRecords.TryGetValue(replacePreviewId, out PreviewApprovalRecord replaced)
                        || replaced.Rejected
                        || !replaced.IsChangeSetPreview)
                    {
                        throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND",
                            $"要替换的 ChangeSet 预演不存在、已结束或已过期：{replacePreviewId}");
                    }
                    replaced.Rejected = true;
                    replacedPreviewId = replaced.PreviewId;
                    Monitor.PulseAll(previewLock);
                }
                else if (supportsExplicitReplacement)
                {
                    PreviewApprovalRecord activeChangeSet = previewRecords.Values.FirstOrDefault(item =>
                        item != null
                        && item.IsChangeSetPreview
                        && !item.Rejected
                        && item.ExpiresAtUtc > previewUtcNow());
                    if (activeChangeSet != null)
                    {
                        activeChangeSet.Rejected = true;
                        replacedPreviewId = activeChangeSet.PreviewId;
                        Monitor.PulseAll(previewLock);
                    }
                }
                EnsureNoActivePreviewLocked(supportsExplicitReplacement);
                // 复用 PreviewApprovalRecord，patch 字段存 previewData
                DateTime createdAtUtc = previewUtcNow();
                var record = new PreviewApprovalRecord
                {
                    PreviewId = previewId,
                    Patch = previewData,
                    PatchHash = ComputePatchHash(previewData),
                    ProcIndex = -1,  // 流程结构操作不绑定单个 procIndex
                    BaseProcId = string.Empty,
                    CreatedAtUtc = createdAtUtc,
                    ExpiresAtUtc = createdAtUtc.Add(previewLifetime),
                    Confirmed = autoConfirmed,
                    IsChangeSetPreview = supportsExplicitReplacement,
                    ReplacedPreviewId = replacedPreviewId
                };
                if (autoConfirmed)
                {
                    record.ConfirmedAtUtc = createdAtUtc;
                }
                previewRecords[record.PreviewId] = record;
            }
            return previewId;
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void ValidateConfirmedManagePreview(string previewId)
        {
            ValidatePreviewIdFormat(previewId);
            lock (previewLock)
            {
                CleanupExpiredPreviewsLocked();
                if (!previewRecords.TryGetValue(previewId, out PreviewApprovalRecord record))
                {
                    throw new BridgeRequestException(404, "PREVIEW_NOT_FOUND", $"预演记录不存在或已过期：{previewId}");
                }
                if (record.Rejected)
                {
                    throw new BridgeRequestException(409, "PREVIEW_REJECTED", $"预演已结束，不能提交：{previewId}");
                }
                if (!record.Confirmed)
                {
                    throw new BridgeRequestException(
                        403,
                        "PREVIEW_NOT_CONFIRMED",
                        "预演仍在等待前台确认，本次提交未执行。",
                        new JObject
                        {
                            ["previewId"] = previewId,
                            ["state"] = "awaiting_foreground_confirmation",
                            ["retryableWhen"] = "preview_confirmed",
                            ["sideEffects"] = "none"
                        }.ToString(Formatting.None));
                }
            }
        }

    }
}
