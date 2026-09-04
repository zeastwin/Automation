using System;
using System.Collections.Generic;
using System.Linq;

// 模块：运行时 / 设备状态时间线。
// 职责范围：把流程引擎生命周期与 Machine Agent 动作结果投影到统一设备时间线；不参与流程调度和安全联锁。

namespace Automation
{
    /// <summary>
    /// 订阅现有流程引擎事件，并将流程位置、失败和结束保存为可跨重启回放的设备事实。
    /// Machine Agent 只向本服务登记已确认动作，真正执行仍由 ProcessEngine 完成。
    /// </summary>
    internal sealed class EquipmentProcessTimelineService : IDisposable
    {
        private readonly ProcessEngine engine;
        private readonly EquipmentTopologyStore topologyStore;
        private readonly EquipmentStateHistoryService history;
        private readonly object syncRoot = new object();
        private readonly Dictionary<Guid, string> lastPositionFingerprints =
            new Dictionary<Guid, string>();
        private readonly Dictionary<string, MachineActionRecord> pendingActions =
            new Dictionary<string, MachineActionRecord>(StringComparer.Ordinal);
        private readonly Dictionary<Guid, MachineActionRecord> runningActions =
            new Dictionary<Guid, MachineActionRecord>();
        // 停止允许对同一 runId 幂等重申；以 actionId/attemptId 为键，避免后一次尝试
        // 误认领前一次动作，也允许每次前台确认分别留下结果事实。
        private readonly Dictionary<string, MachineActionRecord> stoppingActions =
            new Dictionary<string, MachineActionRecord>(StringComparer.Ordinal);
        private int disposed;

        public EquipmentProcessTimelineService(
            ProcessEngine engine,
            EquipmentTopologyStore topologyStore,
            EquipmentStateHistoryService history)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.topologyStore = topologyStore ?? throw new ArgumentNullException(nameof(topologyStore));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            engine.SnapshotChanged += HandleSnapshotChanged;
            engine.ProcessStarted += HandleProcessStarted;
            engine.ProcessStopRequested += HandleProcessStopRequested;
            engine.OperationFailed += HandleOperationFailed;
            engine.ProcessCompleted += HandleProcessCompleted;
        }

        /// <summary>
        /// 在命令入队前登记一次已由前台确认的动作。登记只写审计事实，不启动流程。
        /// </summary>
        public string BeginMachineAction(
            string previewId,
            string skillId,
            Guid processId,
            Guid operationId,
            int stepIndex,
            int operationIndex,
            string nodeId,
            string nodeLabel,
            string executionMode,
            string expectedOutcome)
        {
            if (processId == Guid.Empty) throw new ArgumentException("流程 ID 不能为空。", nameof(processId));
            if (operationId == Guid.Empty) throw new ArgumentException("指令 ID 不能为空。", nameof(operationId));
            if (stepIndex < 0) throw new ArgumentOutOfRangeException(nameof(stepIndex));
            if (operationIndex < 0) throw new ArgumentOutOfRangeException(nameof(operationIndex));
            ThrowIfDisposed();

            ProcessLocation location = ResolveLocation(processId, stepIndex, operationIndex);
            var record = new MachineActionRecord
            {
                ActionId = Guid.NewGuid().ToString("N"),
                PreviewId = Normalize(previewId),
                SkillId = Normalize(skillId),
                ProcessId = processId,
                OperationId = operationId,
                StepIndex = stepIndex,
                OperationIndex = operationIndex,
                NodeId = Normalize(nodeId),
                NodeLabel = Normalize(nodeLabel),
                ExecutionMode = Normalize(executionMode),
                ExpectedOutcome = Normalize(expectedOutcome),
                CreatedAtUtc = DateTime.UtcNow,
                Location = location
            };
            lock (syncRoot)
            {
                PruneAbandonedActionsLocked(record.CreatedAtUtc);
                pendingActions.Add(record.ActionId, record);
            }

            EquipmentStateHistoryEvent started = history.Append(BuildActionEvent(
                record,
                EquipmentStateEventTypes.MachineActionStarted,
                "accepted_for_dispatch",
                "前台已确认 Machine Agent 动作，等待流程引擎接受并启动。"));
            record.StartSequence = started?.Sequence ?? 0;
            return record.ActionId;
        }

        /// <summary>命令未被流程引擎接受时，结束登记并留下失败事实。</summary>
        public void FailMachineActionBeforeStart(string actionId, string reason)
        {
            MachineActionRecord record = RemoveAction(actionId);
            if (record == null) return;
            history.Append(BuildActionEvent(
                record,
                EquipmentStateEventTypes.MachineActionFailed,
                "dispatch_rejected",
                string.IsNullOrWhiteSpace(reason) ? "流程引擎未接受动作。" : reason));
        }

        /// <summary>关联一个已存在 runId 的人工确认停止动作；不直接停止流程。</summary>
        public string BeginMachineStop(
            string previewId,
            Guid processId,
            Guid runId,
            int processIndex,
            int stepIndex,
            int operationIndex,
            string processName,
            string reason)
        {
            if (processId == Guid.Empty) throw new ArgumentException("流程 ID 不能为空。", nameof(processId));
            if (runId == Guid.Empty) throw new ArgumentException("运行实例 ID 不能为空。", nameof(runId));
            ThrowIfDisposed();
            ProcessLocation location = ResolveLocation(processId, stepIndex, operationIndex);
            if (string.IsNullOrWhiteSpace(location.ProcessName))
            {
                location.ProcessName = Normalize(processName);
            }
            var record = new MachineActionRecord
            {
                ActionId = Guid.NewGuid().ToString("N"),
                PreviewId = Normalize(previewId),
                ProcessId = processId,
                OperationId = location.OperationId,
                ProcessIndex = processIndex,
                StepIndex = stepIndex,
                OperationIndex = operationIndex,
                ExecutionMode = "stop_process",
                ExpectedOutcome = "目标流程运行实例进入非活动状态",
                CreatedAtUtc = DateTime.UtcNow,
                RunId = runId,
                Location = location
            };
            lock (syncRoot)
            {
                stoppingActions.Add(record.ActionId, record);
                try
                {
                    // 与完成回调共用同一把锁，保证开始事实一定排在结果事实之前。
                    EquipmentStateHistoryEvent started = history.Append(BuildActionEvent(
                        record,
                        EquipmentStateEventTypes.MachineActionStarted,
                        "stop_requested",
                        "前台已确认停止当前流程运行实例。原因：" + Normalize(reason)));
                    record.StartSequence = started?.Sequence ?? 0;
                }
                catch
                {
                    stoppingActions.Remove(record.ActionId);
                    throw;
                }
            }
            return record.ActionId;
        }

        private void HandleProcessStopRequested(ProcessRunStopRequestedSnapshot requested)
        {
            if (System.Threading.Volatile.Read(ref disposed) != 0
                || requested.AttemptId == Guid.Empty) return;
            string actionId = requested.AttemptId.ToString("N");
            lock (syncRoot)
            {
                if (stoppingActions.TryGetValue(actionId, out MachineActionRecord action)
                    && action.ProcessId == requested.ProcId
                    && action.ProcessIndex == requested.ProcIndex
                    && action.RunId == requested.RunId)
                {
                    action.StopDispatchAccepted = true;
                    action.StopWasReassertion = requested.IsReassertion;
                }
            }
        }

        private void HandleProcessStarted(ProcessRunStartedSnapshot started)
        {
            if (System.Threading.Volatile.Read(ref disposed) != 0) return;
            ProcessLocation location = ResolveLocation(
                started.ProcId, started.StepIndex, started.OperationIndex);
            MachineActionRecord action = BindPendingActionToRun(
                started.ProcId,
                started.RunId,
                started.StepIndex,
                started.OperationIndex,
                started.OperationId == Guid.Empty ? location.OperationId : started.OperationId);
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = DateTime.UtcNow,
                TopologyRevision = CurrentTopologyRevision(),
                EventType = EquipmentStateEventTypes.ProcessStarted,
                NodeId = action?.NodeId ?? string.Empty,
                NodeLabel = action?.NodeLabel ?? string.Empty,
                Aspect = EquipmentStateAspects.Observed,
                NewValue = started.IsSingleOperation ? "single_operation" : "running",
                Meaning = "流程引擎已创建运行实例。",
                Quality = EquipmentStateQualities.Good,
                Confidence = 1,
                SourceKind = "process_engine",
                RunId = Format(started.RunId),
                ProcessId = Format(started.ProcId),
                ProcessName = location.ProcessName,
                StepId = Format(location.StepId),
                StepIndex = started.StepIndex >= 0 ? (int?)started.StepIndex : null,
                OperationId = Format(started.OperationId == Guid.Empty
                    ? location.OperationId
                    : started.OperationId),
                OperationIndex = started.OperationIndex >= 0
                    ? (int?)started.OperationIndex
                    : null,
                OperationType = location.OperationType,
                OperationName = location.OperationName,
                ProcessState = started.IsSingleOperation
                    ? ProcRunState.SingleStep.ToString()
                    : ProcRunState.Running.ToString(),
                Outcome = "started",
                PreviewId = action?.PreviewId ?? string.Empty,
                SkillId = action?.SkillId ?? string.Empty,
                ActionId = action?.ActionId ?? string.Empty,
                ExpectedOutcome = action?.ExpectedOutcome ?? string.Empty,
                CausedBySequence = PositiveSequence(action?.StartSequence ?? 0)
            });
            AppendInitialPositionIfMissing(started, location, action);
        }

        /// <summary>
        /// 快速单指令可能在节流快照发布前结束；启动事件携带的精确位置必须补入时间线。
        /// </summary>
        private void AppendInitialPositionIfMissing(
            ProcessRunStartedSnapshot started,
            ProcessLocation location,
            MachineActionRecord action)
        {
            ProcRunState state = started.IsSingleOperation
                ? ProcRunState.SingleStep
                : ProcRunState.Running;
            string fingerprint = string.Join("|",
                Format(started.RunId),
                state.ToString(),
                started.StepIndex.ToString(),
                started.OperationIndex.ToString(),
                string.Empty,
                ProcTerminationReason.None.ToString());
            lock (syncRoot)
            {
                if (lastPositionFingerprints.TryGetValue(started.ProcId, out string previous)
                    && string.Equals(previous, fingerprint, StringComparison.Ordinal))
                {
                    return;
                }
                lastPositionFingerprints[started.ProcId] = fingerprint;
            }

            MatchedNode node = action == null
                ? ResolveNode(started.ProcId, location.OperationId)
                : new MatchedNode(action.NodeId, action.NodeLabel);
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = DateTime.UtcNow,
                TopologyRevision = CurrentTopologyRevision(),
                EventType = EquipmentStateEventTypes.ProcessPositionChanged,
                NodeId = node.NodeId,
                NodeLabel = node.NodeLabel,
                Aspect = EquipmentStateAspects.Observed,
                NewValue = $"{state}@{started.StepIndex}:{started.OperationIndex}",
                Meaning = "流程运行实例的初始位置。",
                Quality = EquipmentStateQualities.Good,
                Confidence = 1,
                SourceKind = "process_engine",
                RunId = Format(started.RunId),
                ProcessId = Format(started.ProcId),
                ProcessName = location.ProcessName,
                StepId = Format(location.StepId),
                StepIndex = started.StepIndex >= 0 ? (int?)started.StepIndex : null,
                OperationId = Format(started.OperationId == Guid.Empty
                    ? location.OperationId
                    : started.OperationId),
                OperationIndex = started.OperationIndex >= 0
                    ? (int?)started.OperationIndex
                    : null,
                OperationType = location.OperationType,
                OperationName = location.OperationName,
                ProcessState = state.ToString(),
                Outcome = "observed",
                PreviewId = action?.PreviewId ?? string.Empty,
                SkillId = action?.SkillId ?? string.Empty,
                ActionId = action?.ActionId ?? string.Empty,
                ExpectedOutcome = action?.ExpectedOutcome ?? string.Empty,
                CausedBySequence = PositiveSequence(action?.StartSequence ?? 0)
            });
        }

        private void HandleSnapshotChanged(EngineSnapshot snapshot)
        {
            if (snapshot == null || System.Threading.Volatile.Read(ref disposed) != 0) return;
            ProcessLocation location = ResolveLocation(
                snapshot.ProcId, snapshot.StepIndex, snapshot.OpIndex);
            MachineActionRecord action = BindActionToRun(snapshot, location);
            MachineActionRecord timelineAction = FindAcceptedStoppingAction(snapshot.RunId) ?? action;
            string fingerprint = string.Join("|",
                Format(snapshot.RunId),
                snapshot.State.ToString(),
                snapshot.StepIndex.ToString(),
                snapshot.OpIndex.ToString(),
                snapshot.AlarmMessage ?? string.Empty,
                snapshot.TerminationReason.ToString());
            lock (syncRoot)
            {
                if (lastPositionFingerprints.TryGetValue(snapshot.ProcId, out string previous)
                    && string.Equals(previous, fingerprint, StringComparison.Ordinal))
                {
                    return;
                }
                lastPositionFingerprints[snapshot.ProcId] = fingerprint;
            }

            MatchedNode node = timelineAction == null
                ? ResolveNode(snapshot.ProcId, location.OperationId)
                : new MatchedNode(timelineAction.NodeId, timelineAction.NodeLabel);
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = NormalizeUtc(snapshot.UpdateTime),
                TopologyRevision = CurrentTopologyRevision(),
                EventType = EquipmentStateEventTypes.ProcessPositionChanged,
                NodeId = node.NodeId,
                NodeLabel = node.NodeLabel,
                Aspect = EquipmentStateAspects.Observed,
                NewValue = $"{snapshot.State}@{snapshot.StepIndex}:{snapshot.OpIndex}",
                Meaning = string.IsNullOrWhiteSpace(snapshot.AlarmMessage)
                    ? "流程运行位置或状态发生变化。"
                    : snapshot.AlarmMessage,
                Quality = EquipmentStateQualities.Good,
                Confidence = 1,
                SourceKind = "process_engine",
                RunId = Format(snapshot.RunId),
                ProcessId = Format(snapshot.ProcId),
                ProcessName = string.IsNullOrWhiteSpace(snapshot.ProcName)
                    ? location.ProcessName
                    : snapshot.ProcName,
                StepId = Format(location.StepId),
                StepIndex = snapshot.StepIndex >= 0 ? (int?)snapshot.StepIndex : null,
                OperationId = Format(location.OperationId),
                OperationIndex = snapshot.OpIndex >= 0 ? (int?)snapshot.OpIndex : null,
                OperationType = location.OperationType,
                OperationName = location.OperationName,
                ProcessState = snapshot.State.ToString(),
                Outcome = snapshot.IsAlarm ? "alarm" : "observed",
                TerminationReason = snapshot.TerminationReason.ToString(),
                PreviewId = timelineAction?.PreviewId ?? string.Empty,
                SkillId = timelineAction?.SkillId ?? string.Empty,
                ActionId = timelineAction?.ActionId ?? string.Empty,
                ExpectedOutcome = timelineAction?.ExpectedOutcome ?? string.Empty,
                CausedBySequence = PositiveSequence(timelineAction?.StartSequence ?? 0)
            });
        }

        private void HandleOperationFailed(OperationFailureEntry entry)
        {
            if (System.Threading.Volatile.Read(ref disposed) != 0) return;
            EngineSnapshot snapshot = engine.GetSnapshot(entry.ProcIndex);
            MachineActionRecord action = FindRunningAction(snapshot?.RunId ?? Guid.Empty);
            ProcessLocation location = ResolveLocation(entry.ProcId, entry.StepIndex, entry.OpIndex);
            MatchedNode node = action == null
                ? ResolveNode(entry.ProcId, entry.OperationId)
                : new MatchedNode(action.NodeId, action.NodeLabel);
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = DateTime.UtcNow,
                TopologyRevision = CurrentTopologyRevision(),
                EventType = EquipmentStateEventTypes.ProcessOperationFailed,
                NodeId = node.NodeId,
                NodeLabel = node.NodeLabel,
                Aspect = EquipmentStateAspects.Observed,
                NewValue = "failed",
                Meaning = entry.AlarmMessage ?? "流程指令执行失败。",
                Quality = EquipmentStateQualities.Good,
                Confidence = 1,
                SourceKind = "process_engine",
                RunId = Format(snapshot?.RunId ?? Guid.Empty),
                ProcessId = Format(entry.ProcId),
                ProcessName = location.ProcessName,
                StepId = Format(location.StepId),
                StepIndex = entry.StepIndex,
                OperationId = Format(entry.OperationId),
                OperationIndex = entry.OpIndex,
                OperationType = string.IsNullOrWhiteSpace(entry.OperationType)
                    ? location.OperationType
                    : entry.OperationType,
                OperationName = string.IsNullOrWhiteSpace(entry.OperationName)
                    ? location.OperationName
                    : entry.OperationName,
                ProcessState = snapshot?.State.ToString() ?? string.Empty,
                Outcome = "failed",
                PreviewId = action?.PreviewId ?? string.Empty,
                SkillId = action?.SkillId ?? string.Empty,
                ActionId = action?.ActionId ?? string.Empty,
                ExpectedOutcome = action?.ExpectedOutcome ?? string.Empty,
                CausedBySequence = PositiveSequence(action?.StartSequence ?? 0)
            });
        }

        private void HandleProcessCompleted(ProcessRunAuditSnapshot completed)
        {
            if (System.Threading.Volatile.Read(ref disposed) != 0) return;
            MachineActionRecord action = RemoveRunningAction(completed.RunId);
            List<MachineActionRecord> stopActions = RemoveStoppingActions(completed.RunId);
            MachineActionRecord correlatedAction = stopActions
                .Where(item => item.StopDispatchAccepted)
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefault()
                ?? action;
            ProcessLocation location = ResolveLocation(completed.ProcId, -1, -1);
            // 单指令成功只能由引擎工作线程的“目标正常完成后自停”事实证明，
            // 不能从 StopRequested 反推，否则外部中断也会被误报成功。
            bool singleOperationCompleted = action != null
                && string.Equals(action.ExecutionMode,
                    Automation.Protocol.MachineExecutionModes.SingleOperation,
                    StringComparison.Ordinal)
                && completed.IsSingleOperation
                && completed.SingleOperationTargetCompleted;
            bool succeeded = completed.FailedCount == 0
                && string.IsNullOrWhiteSpace(completed.AlarmMessage)
                && (completed.TerminationReason == ProcTerminationReason.Completed
                    || singleOperationCompleted);
            string processOutcome = completed.TerminationReason == ProcTerminationReason.Completed
                ? "completed"
                : completed.TerminationReason == ProcTerminationReason.StopRequested
                    ? "stopped"
                    : "failed";
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = DateTime.UtcNow,
                TopologyRevision = CurrentTopologyRevision(),
                EventType = EquipmentStateEventTypes.ProcessCompleted,
                NodeId = correlatedAction?.NodeId ?? string.Empty,
                NodeLabel = correlatedAction?.NodeLabel ?? string.Empty,
                Aspect = EquipmentStateAspects.Observed,
                NewValue = processOutcome,
                Meaning = string.IsNullOrWhiteSpace(completed.AlarmMessage)
                    ? $"流程运行结束；指令数={completed.OperationCount}，失败数={completed.FailedCount}，重试数={completed.RetryCount}。"
                    : completed.AlarmMessage,
                Quality = EquipmentStateQualities.Good,
                Confidence = 1,
                SourceKind = "process_engine",
                RunId = Format(completed.RunId),
                ProcessId = Format(completed.ProcId),
                ProcessName = location.ProcessName,
                Outcome = processOutcome,
                TerminationReason = completed.TerminationReason.ToString(),
                PreviewId = correlatedAction?.PreviewId ?? string.Empty,
                SkillId = correlatedAction?.SkillId ?? string.Empty,
                ActionId = correlatedAction?.ActionId ?? string.Empty,
                ExpectedOutcome = correlatedAction?.ExpectedOutcome ?? string.Empty,
                CausedBySequence = PositiveSequence(correlatedAction?.StartSequence ?? 0)
            });
            if (action != null)
            {
                history.Append(BuildActionEvent(
                    action,
                    succeeded
                        ? EquipmentStateEventTypes.MachineActionCompleted
                        : EquipmentStateEventTypes.MachineActionFailed,
                    succeeded ? "completed" : "failed",
                    succeeded
                        ? "Machine Agent 动作对应的流程运行已正常结束。"
                        : "Machine Agent 动作对应的流程运行未正常完成："
                            + completed.TerminationReason));
                AppendOutcomeObservation(action, succeeded);
            }
            foreach (MachineActionRecord stopAction in stopActions)
            {
                // 已报警实例保留 Alarm 作为流程根因；这不否定人工停止已使同一 runId 进入非活动态。
                bool stopSucceeded = stopAction.StopDispatchAccepted
                    && (completed.TerminationReason == ProcTerminationReason.StopRequested
                        || completed.TerminationReason == ProcTerminationReason.Alarm);
                bool stoppedWithAlarm = stopSucceeded
                    && completed.TerminationReason == ProcTerminationReason.Alarm;
                string stopOutcome = !stopAction.StopDispatchAccepted
                    ? "dispatch_not_accepted"
                    : stoppedWithAlarm
                        ? "stopped_with_alarm"
                        : stopSucceeded
                            ? stopAction.StopWasReassertion ? "stop_reasserted" : "stopped"
                            : "stop_failed";
                string stopMeaning = !stopAction.StopDispatchAccepted
                    ? "流程在停止指令被原子接受前已结束或进入停止，未把结果归因于本次动作。"
                    : stoppedWithAlarm
                        ? "已停止预演冻结的同一流程运行实例；原报警根因仍需处理。"
                    : stopSucceeded
                        ? stopAction.StopWasReassertion
                            ? "已对停止中的同一流程运行实例重申停止，并确认实例进入非活动态。"
                            : "已停止预演冻结的同一流程运行实例。"
                        : "流程以非停止原因结束：" + completed.TerminationReason;
                history.Append(BuildActionEvent(
                    stopAction,
                    stopSucceeded
                        ? EquipmentStateEventTypes.MachineActionCompleted
                        : EquipmentStateEventTypes.MachineActionFailed,
                    stopOutcome,
                    stopMeaning));
                AppendStopOutcomeObservation(stopAction, stopSucceeded);
            }
        }

        private MachineActionRecord BindActionToRun(
            EngineSnapshot snapshot,
            ProcessLocation location)
        {
            if (snapshot.RunId == Guid.Empty || snapshot.State.IsInactive())
            {
                return FindRunningAction(snapshot.RunId);
            }
            lock (syncRoot)
            {
                if (runningActions.TryGetValue(snapshot.RunId, out MachineActionRecord existing))
                {
                    return existing;
                }
                MachineActionRecord match = pendingActions.Values
                    .Where(item => item.ProcessId == snapshot.ProcId
                        && item.StepIndex == snapshot.StepIndex
                        && item.OperationIndex == snapshot.OpIndex
                        && item.OperationId == location.OperationId)
                    .OrderBy(item => item.CreatedAtUtc)
                    .FirstOrDefault();
                if (match == null) return null;
                pendingActions.Remove(match.ActionId);
                match.RunId = snapshot.RunId;
                runningActions[snapshot.RunId] = match;
                return match;
            }
        }

        private MachineActionRecord BindPendingActionToRun(
            Guid processId,
            Guid runId,
            int stepIndex,
            int operationIndex,
            Guid operationId)
        {
            if (runId == Guid.Empty) return null;
            lock (syncRoot)
            {
                if (runningActions.TryGetValue(runId, out MachineActionRecord existing))
                {
                    return existing;
                }
                MachineActionRecord match = pendingActions.Values
                    .Where(item => item.ProcessId == processId
                        && item.StepIndex == stepIndex
                        && item.OperationIndex == operationIndex
                        && item.OperationId == operationId)
                    .OrderBy(item => item.CreatedAtUtc)
                    .FirstOrDefault();
                if (match == null) return null;
                pendingActions.Remove(match.ActionId);
                match.RunId = runId;
                runningActions[runId] = match;
                return match;
            }
        }

        private void AppendOutcomeObservation(MachineActionRecord action, bool processSucceeded)
        {
            EquipmentNodeStateProjection observed = history.GetCurrentSnapshot().NodeStates
                .FirstOrDefault(item => string.Equals(
                    item.NodeId, action.NodeId, StringComparison.Ordinal));
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = DateTime.UtcNow,
                TopologyRevision = CurrentTopologyRevision(),
                EventType = EquipmentStateEventTypes.MachineActionOutcomeObserved,
                NodeId = action.NodeId,
                NodeLabel = action.NodeLabel,
                Aspect = EquipmentStateAspects.Observed,
                OldValue = action.ExpectedOutcome,
                NewValue = observed == null
                    ? string.Empty
                    : observed.StateName + " (" + observed.Quality + ")",
                Meaning = observed == null
                    ? "动作已结束，但没有可关联的节点观测；预期结果尚未机械验证。"
                    : "已记录动作结束时的节点观测；预期结果是说明文字，未据此宣称验证通过。",
                Quality = EquipmentStateQualities.Unknown,
                Confidence = 0,
                SourceKind = "machine_agent",
                ResourceRef = string.IsNullOrWhiteSpace(action.SkillId)
                    ? string.Empty
                    : "skill:" + action.SkillId,
                BindingId = action.SkillId,
                RunId = Format(action.RunId),
                ProcessId = Format(action.ProcessId),
                ProcessName = action.Location.ProcessName,
                StepId = Format(action.Location.StepId),
                StepIndex = action.StepIndex,
                OperationId = Format(action.OperationId),
                OperationIndex = action.OperationIndex,
                OperationType = action.Location.OperationType,
                OperationName = action.Location.OperationName,
                Outcome = processSucceeded ? "observed_unverified" : "not_verified_after_failure",
                PreviewId = action.PreviewId,
                SkillId = action.SkillId,
                ActionId = action.ActionId,
                ExpectedOutcome = action.ExpectedOutcome,
                CausedBySequence = PositiveSequence(action.StartSequence)
            });
        }

        private void AppendStopOutcomeObservation(
            MachineActionRecord action,
            bool processReportedStopped)
        {
            EngineSnapshot snapshot = engine.GetSnapshot(action.ProcessIndex);
            bool verified = processReportedStopped
                && snapshot != null
                && snapshot.ProcId == action.ProcessId
                && snapshot.RunId == action.RunId
                && snapshot.State.IsInactive();
            history.Append(new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = DateTime.UtcNow,
                TopologyRevision = CurrentTopologyRevision(),
                EventType = EquipmentStateEventTypes.MachineActionOutcomeObserved,
                Aspect = EquipmentStateAspects.Observed,
                OldValue = action.ExpectedOutcome,
                NewValue = snapshot?.State.ToString() ?? string.Empty,
                Meaning = verified
                    ? "流程引擎快照已确认冻结的运行实例进入非活动状态。"
                    : "没有取得与冻结 runId 一致的非活动快照，停止结果未验证。",
                Quality = verified
                    ? EquipmentStateQualities.Good
                    : EquipmentStateQualities.Unknown,
                Confidence = verified ? 1 : 0,
                SourceKind = "machine_agent",
                RunId = Format(action.RunId),
                ProcessId = Format(action.ProcessId),
                ProcessName = action.Location.ProcessName,
                StepId = Format(action.Location.StepId),
                StepIndex = action.StepIndex >= 0 ? (int?)action.StepIndex : null,
                OperationId = Format(action.OperationId),
                OperationIndex = action.OperationIndex >= 0
                    ? (int?)action.OperationIndex
                    : null,
                OperationType = action.Location.OperationType,
                OperationName = action.Location.OperationName,
                ProcessState = snapshot?.State.ToString() ?? string.Empty,
                Outcome = verified ? "verified" : "unverified",
                PreviewId = action.PreviewId,
                ActionId = action.ActionId,
                ExpectedOutcome = action.ExpectedOutcome,
                CausedBySequence = PositiveSequence(action.StartSequence)
            });
        }

        private EquipmentStateHistoryEvent BuildActionEvent(
            MachineActionRecord action,
            string eventType,
            string outcome,
            string meaning)
        {
            return new EquipmentStateHistoryEvent
            {
                ObservedAtUtc = DateTime.UtcNow,
                TopologyRevision = CurrentTopologyRevision(),
                EventType = eventType,
                NodeId = action.NodeId,
                NodeLabel = action.NodeLabel,
                Aspect = EquipmentStateAspects.Commanded,
                NewValue = action.ExecutionMode,
                Meaning = meaning,
                Quality = EquipmentStateQualities.Good,
                Confidence = 1,
                SourceKind = "machine_agent",
                ResourceRef = string.IsNullOrWhiteSpace(action.SkillId)
                    ? string.Empty
                    : "skill:" + action.SkillId,
                BindingId = action.SkillId,
                RunId = Format(action.RunId),
                ProcessId = Format(action.ProcessId),
                ProcessName = action.Location.ProcessName,
                StepId = Format(action.Location.StepId),
                StepIndex = action.StepIndex,
                OperationId = Format(action.OperationId),
                OperationIndex = action.OperationIndex,
                OperationType = action.Location.OperationType,
                OperationName = action.Location.OperationName,
                Outcome = outcome,
                PreviewId = action.PreviewId,
                SkillId = action.SkillId,
                ActionId = action.ActionId,
                ExpectedOutcome = action.ExpectedOutcome,
                CausedBySequence = eventType == EquipmentStateEventTypes.MachineActionStarted
                    ? null
                    : PositiveSequence(action.StartSequence)
            };
        }

        private ProcessLocation ResolveLocation(Guid processId, int stepIndex, int operationIndex)
        {
            try
            {
                Proc process = (engine.Context?.Procs ?? Array.Empty<Proc>())
                    .FirstOrDefault(item => item?.head?.Id == processId);
                if (process == null)
                {
                    return new ProcessLocation { ProcessId = processId };
                }
                var location = new ProcessLocation
                {
                    ProcessId = processId,
                    ProcessName = process.head?.Name ?? string.Empty
                };
                if (stepIndex < 0 || process.steps == null || stepIndex >= process.steps.Count)
                {
                    return location;
                }
                Step step = process.steps[stepIndex];
                location.StepId = step?.Id ?? Guid.Empty;
                if (operationIndex < 0 || step?.Ops == null || operationIndex >= step.Ops.Count)
                {
                    return location;
                }
                OperationType operation = step.Ops[operationIndex];
                location.OperationId = operation?.Id ?? Guid.Empty;
                location.OperationType = operation?.GetType().Name ?? string.Empty;
                location.OperationName = operation?.Name ?? string.Empty;
                return location;
            }
            catch (ArgumentOutOfRangeException)
            {
                return new ProcessLocation { ProcessId = processId };
            }
            catch (InvalidOperationException)
            {
                return new ProcessLocation { ProcessId = processId };
            }
        }

        private MatchedNode ResolveNode(Guid processId, Guid operationId)
        {
            if (processId == Guid.Empty || operationId == Guid.Empty) return MatchedNode.Empty;
            EquipmentTopologyDefinition topology = topologyStore.CreateSnapshot();
            List<EquipmentTopologyNode> matches = (topology.Nodes ?? new List<EquipmentTopologyNode>())
                .Where(node => node != null
                    && string.Equals(node.ReviewState, "confirmed", StringComparison.Ordinal)
                    && (node.Skills ?? new List<EquipmentTopologySkillBinding>()).Any(skill =>
                        skill != null
                        && string.Equals(skill.ReviewState, "confirmed", StringComparison.Ordinal)
                        && Guid.TryParse(skill.ProcessId, out Guid skillProcessId)
                        && skillProcessId == processId
                        && Guid.TryParse(skill.OperationId, out Guid skillOperationId)
                        && skillOperationId == operationId))
                .Take(2)
                .ToList();
            return matches.Count == 1
                ? new MatchedNode(matches[0].Id, matches[0].Label)
                : MatchedNode.Empty;
        }

        private MachineActionRecord FindRunningAction(Guid runId)
        {
            if (runId == Guid.Empty) return null;
            lock (syncRoot)
            {
                runningActions.TryGetValue(runId, out MachineActionRecord action);
                return action;
            }
        }

        private MachineActionRecord FindAcceptedStoppingAction(Guid runId)
        {
            if (runId == Guid.Empty) return null;
            lock (syncRoot)
            {
                return stoppingActions.Values
                    .Where(item => item.RunId == runId && item.StopDispatchAccepted)
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .FirstOrDefault();
            }
        }

        private MachineActionRecord RemoveRunningAction(Guid runId)
        {
            if (runId == Guid.Empty) return null;
            lock (syncRoot)
            {
                if (!runningActions.TryGetValue(runId, out MachineActionRecord action)) return null;
                runningActions.Remove(runId);
                return action;
            }
        }

        private List<MachineActionRecord> RemoveStoppingActions(Guid runId)
        {
            if (runId == Guid.Empty) return new List<MachineActionRecord>();
            lock (syncRoot)
            {
                List<MachineActionRecord> actions = stoppingActions.Values
                    .Where(item => item.RunId == runId)
                    .OrderBy(item => item.CreatedAtUtc)
                    .ToList();
                foreach (MachineActionRecord action in actions)
                {
                    stoppingActions.Remove(action.ActionId);
                }
                return actions;
            }
        }

        private MachineActionRecord RemoveAction(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId)) return null;
            lock (syncRoot)
            {
                if (pendingActions.TryGetValue(actionId, out MachineActionRecord pending))
                {
                    pendingActions.Remove(actionId);
                    return pending;
                }
                Guid runId = runningActions
                    .Where(pair => string.Equals(pair.Value.ActionId, actionId, StringComparison.Ordinal))
                    .Select(pair => pair.Key)
                    .FirstOrDefault();
                if (runId != Guid.Empty)
                {
                    MachineActionRecord running = runningActions[runId];
                    runningActions.Remove(runId);
                    return running;
                }
                if (!stoppingActions.TryGetValue(actionId, out MachineActionRecord stopping))
                {
                    return null;
                }
                stoppingActions.Remove(actionId);
                return stopping;
            }
        }

        private void PruneAbandonedActionsLocked(DateTime nowUtc)
        {
            DateTime cutoff = nowUtc - TimeSpan.FromMinutes(5);
            foreach (string actionId in pendingActions.Values
                .Where(item => item.CreatedAtUtc < cutoff)
                .Select(item => item.ActionId)
                .ToList())
            {
                pendingActions.Remove(actionId);
            }
        }

        private long CurrentTopologyRevision()
        {
            try
            {
                return topologyStore.CreateSnapshot().Revision;
            }
            catch
            {
                return 0;
            }
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default(DateTime)) return DateTime.UtcNow;
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }

        private static string Normalize(string value) => (value ?? string.Empty).Trim();
        private static string Format(Guid value) => value == Guid.Empty ? string.Empty : value.ToString("D");
        private static long? PositiveSequence(long value) => value > 0 ? (long?)value : null;

        private void ThrowIfDisposed()
        {
            if (System.Threading.Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(EquipmentProcessTimelineService));
            }
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref disposed, 1) != 0) return;
            engine.SnapshotChanged -= HandleSnapshotChanged;
            engine.ProcessStarted -= HandleProcessStarted;
            engine.ProcessStopRequested -= HandleProcessStopRequested;
            engine.OperationFailed -= HandleOperationFailed;
            engine.ProcessCompleted -= HandleProcessCompleted;
            lock (syncRoot)
            {
                pendingActions.Clear();
                runningActions.Clear();
                stoppingActions.Clear();
                lastPositionFingerprints.Clear();
            }
        }

        private sealed class MachineActionRecord
        {
            public string ActionId { get; set; }
            public string PreviewId { get; set; }
            public string SkillId { get; set; }
            public Guid ProcessId { get; set; }
            public Guid OperationId { get; set; }
            public int ProcessIndex { get; set; }
            public int StepIndex { get; set; }
            public int OperationIndex { get; set; }
            public string NodeId { get; set; }
            public string NodeLabel { get; set; }
            public string ExecutionMode { get; set; }
            public string ExpectedOutcome { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public Guid RunId { get; set; }
            public long StartSequence { get; set; }
            public bool StopDispatchAccepted { get; set; }
            public bool StopWasReassertion { get; set; }
            public ProcessLocation Location { get; set; }
        }

        private sealed class ProcessLocation
        {
            public Guid ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public Guid StepId { get; set; }
            public Guid OperationId { get; set; }
            public string OperationType { get; set; } = string.Empty;
            public string OperationName { get; set; } = string.Empty;
        }

        private readonly struct MatchedNode
        {
            public static readonly MatchedNode Empty = new MatchedNode(string.Empty, string.Empty);

            public MatchedNode(string nodeId, string nodeLabel)
            {
                NodeId = nodeId ?? string.Empty;
                NodeLabel = nodeLabel ?? string.Empty;
            }

            public string NodeId { get; }
            public string NodeLabel { get; }
        }
    }
}
