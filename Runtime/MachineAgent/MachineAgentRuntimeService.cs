using Automation.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

// 模块：运行时 / Machine Agent。
// 职责范围：聚合设备语义上下文，冻结“从指定指令执行”预演，并在确认时校验现场事实后调用现有流程引擎。
// 安全边界：AI 只能创建预演；本服务不接收自然语言执行命令，也不绕过流程引擎、Readiness 或账户权限。

namespace Automation
{
    internal sealed class MachineAgentControlException : Exception
    {
        public MachineAgentControlException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    /// <summary>
    /// Machine Agent 的运行时应用服务。预演记录只存在于当前平台实例内，重启后全部失效。
    /// </summary>
    internal sealed class MachineAgentRuntimeService
    {
        private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(5);
        // 这里只限制“现场读取连续中断”的时长；稳定状态每次成功轮询都会刷新观测时间，
        // 绝不能按状态值保持不变的时长判过期。
        private static readonly TimeSpan ObservationFailureTolerance = TimeSpan.FromSeconds(5);
        private static readonly JsonSerializerSettings CamelCaseJsonSettings =
            new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
        private const int MaximumPreviewCount = 32;
        private const int DefaultContextNodeLimit = 80;
        private const int MaximumContextNodeLimit = 200;
        private const int DefaultContextRelationLimit = 160;
        private const int MaximumContextRelationLimit = 500;
        private const int ContextEventLimit = 80;
        private static readonly HashSet<string> OperationMetadataFields = new HashSet<string>(
            new[] { "Id", "Name", "OperaType", "Disable", "AlarmType", "AlarmInfoId",
                "Goto1", "Goto2", "Goto3" }, StringComparer.Ordinal);

        private readonly PlatformRuntime runtime;
        private readonly object previewLock = new object();
        private readonly Dictionary<string, FrozenEntryPreview> previews =
            new Dictionary<string, FrozenEntryPreview>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrozenStopPreview> stopPreviews =
            new Dictionary<string, FrozenStopPreview>(StringComparer.OrdinalIgnoreCase);

        public MachineAgentRuntimeService(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <summary>一次返回 Machine Agent 判断当前设备所需的拓扑、状态、流程和时间线事实。</summary>
        public JObject BuildContext(
            int eventLimit = 40,
            int nodeOffset = 0,
            int nodeLimit = DefaultContextNodeLimit,
            int relationOffset = 0,
            int relationLimit = DefaultContextRelationLimit)
        {
            int normalizedEventLimit = Math.Max(1, Math.Min(ContextEventLimit, eventLimit));
            int normalizedNodeOffset = Math.Max(0, nodeOffset);
            int normalizedNodeLimit = Math.Max(1, Math.Min(MaximumContextNodeLimit, nodeLimit));
            int normalizedRelationOffset = Math.Max(0, relationOffset);
            int normalizedRelationLimit = Math.Max(
                1,
                Math.Min(MaximumContextRelationLimit, relationLimit));
            EquipmentTopologyDefinition topology = runtime.Stores.Topology.CreateSnapshot();
            EquipmentStateSnapshot state = runtime.StateHistory?.GetCurrentSnapshot()
                ?? new EquipmentStateSnapshot();
            EquipmentPerceptionSnapshot perception = runtime.StatePerception?.GetCurrentSnapshot()
                ?? new EquipmentPerceptionSnapshot();
            EquipmentStateHistoryWindow history = runtime.StateHistory?.GetRecentWindow(normalizedEventLimit)
                ?? new EquipmentStateHistoryWindow();
            Dictionary<string, EquipmentNodeStateProjection> states =
                (state.NodeStates ?? new List<EquipmentNodeStateProjection>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                .GroupBy(item => item.NodeId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Sequence).First(),
                    StringComparer.Ordinal);
            Dictionary<string, EquipmentNodePerceptionState> perceptionStates =
                (perception.NodeStates ?? new List<EquipmentNodePerceptionState>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                .GroupBy(item => item.NodeId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.OrderByDescending(item => item.LastSuccessfulObservationAtUtc).First(),
                    StringComparer.Ordinal);

            var processItems = new JArray();
            foreach (EngineSnapshot snapshot in (runtime.ProcessEngine?.GetSnapshots()
                ?? new List<EngineSnapshot>()).Where(item => item != null).OrderBy(item => item.ProcIndex))
            {
                processItems.Add(new JObject
                {
                    ["procId"] = snapshot.ProcId == Guid.Empty ? string.Empty : snapshot.ProcId.ToString("D"),
                    ["procIndex"] = snapshot.ProcIndex,
                    ["name"] = snapshot.ProcName ?? string.Empty,
                    ["state"] = snapshot.State.ToString(),
                    ["stepIndex"] = snapshot.StepIndex,
                    ["opIndex"] = snapshot.OpIndex,
                    ["isAlarm"] = snapshot.IsAlarm,
                    ["alarmMessage"] = snapshot.AlarmMessage ?? string.Empty,
                    ["runId"] = snapshot.RunId == Guid.Empty ? string.Empty : snapshot.RunId.ToString("D"),
                    ["publishedRevision"] = snapshot.PublishedRevision,
                    ["appliedRevision"] = snapshot.AppliedRevision
                });
            }

            List<EquipmentTopologyNode> orderedNodes =
                (topology.Nodes ?? new List<EquipmentTopologyNode>())
                .Where(item => item != null)
                .OrderBy(item => string.Equals(item.ReviewState, "confirmed", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();
            List<EquipmentTopologyRelation> orderedRelations =
                (topology.Relations ?? new List<EquipmentTopologyRelation>())
                .Where(item => item != null)
                .OrderBy(item => string.Equals(item.ReviewState, "confirmed", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();

            var nodeItems = new JArray();
            foreach (EquipmentTopologyNode node in orderedNodes
                .Skip(normalizedNodeOffset)
                .Take(normalizedNodeLimit))
            {
                states.TryGetValue(node.Id ?? string.Empty, out EquipmentNodeStateProjection nodeState);
                perceptionStates.TryGetValue(
                    node.Id ?? string.Empty, out EquipmentNodePerceptionState perceptionState);
                nodeItems.Add(BuildNodeContext(node, nodeState, perceptionState));
            }

            return new JObject
            {
                ["contract"] = "machine.context.v1",
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["safety"] = new JObject
                {
                    ["locked"] = runtime.Safety.IsLocked,
                    ["lockReason"] = runtime.Safety.LockReason ?? string.Empty
                },
                ["perception"] = new JObject
                {
                    ["running"] = perception.IsRunning,
                    ["lastError"] = perception.LastObservationError
                        ?? runtime.StateHistory?.LastPersistenceError
                        ?? string.Empty,
                    ["latestSequence"] = runtime.StateHistory?.Revision ?? 0
                },
                ["topology"] = new JObject
                {
                    ["name"] = topology.Name ?? string.Empty,
                    ["revision"] = topology.Revision,
                    ["nodeCount"] = topology.Nodes?.Count ?? 0,
                    ["relationCount"] = topology.Relations?.Count ?? 0,
                    ["nodeWindow"] = BuildWindow(
                        normalizedNodeOffset,
                        normalizedNodeLimit,
                        nodeItems.Count,
                        orderedNodes.Count),
                    ["relationWindow"] = BuildWindow(
                        normalizedRelationOffset,
                        normalizedRelationLimit,
                        Math.Min(normalizedRelationLimit,
                            Math.Max(0, orderedRelations.Count - normalizedRelationOffset)),
                        orderedRelations.Count),
                    ["nodes"] = nodeItems,
                    ["relations"] = BuildRelationContext(
                        orderedRelations,
                        normalizedRelationOffset,
                        normalizedRelationLimit)
                },
                ["processes"] = processItems,
                ["recentEvents"] = BuildEventArray(history.Events, normalizedEventLimit),
                ["evidenceBoundary"] = new JObject
                {
                    ["topologyPrimary"] = true,
                    ["processNamesAreEvidence"] = false,
                    ["nodeStateIsProjection"] = true,
                    ["historyIsOrderedFactSource"] = true,
                    ["liveStateSourceKinds"] = new JArray("io")
                }
            };
        }

        public JObject BuildStateHistory(long? afterSequence, int limit)
        {
            int normalizedLimit = Math.Max(1, Math.Min(500, limit <= 0 ? 120 : limit));
            EquipmentStateHistoryWindow source = runtime.StateHistory?.GetWindowAfterSequence(
                    afterSequence,
                    normalizedLimit)
                ?? new EquipmentStateHistoryWindow();
            return new JObject
            {
                ["contract"] = "machine.state_history.v1",
                ["earliestAvailableSequence"] = source.EarliestAvailableSequence,
                ["latestSequence"] = source.LatestSequence,
                ["truncated"] = source.Truncated,
                ["baselineComplete"] = source.BaselineComplete,
                ["sequenceGapsTruncated"] = source.SequenceGapsTruncated,
                ["sequenceGaps"] = new JArray((source.SequenceGaps
                    ?? new List<EquipmentStateSequenceGap>()).Select(ToCamelCaseObject)),
                ["baseline"] = ToCamelCaseObject(
                    source.Baseline ?? new EquipmentStateSnapshot()),
                ["events"] = BuildChronologicalEventArray(source.Events)
            };
        }

        /// <summary>
        /// 冻结一个现有流程运行实例的停止预演。停止沿用现有 ProcessControl，
        /// 不依赖拓扑感知或安全锁，避免辅助能力妨碍必要停机。
        /// </summary>
        public JObject PreviewProcessStop(MachineProcessStopPreviewRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!Guid.TryParse((request.ProcId ?? string.Empty).Trim(), out Guid procId)
                || procId == Guid.Empty)
                throw new MachineAgentControlException(
                    "MACHINE_PROC_ID_INVALID", "停止预演必须提供有效的流程稳定 ID。");
            string reason = (request.Reason ?? string.Empty).Trim();
            if (reason.Length == 0 || reason.Length > 1000)
                throw new MachineAgentControlException(
                    "MACHINE_STOP_REASON_INVALID", "停止原因不能为空且不能超过 1000 个字符。");

            List<EngineSnapshot> matches = (runtime.ProcessEngine?.GetSnapshots()
                ?? new List<EngineSnapshot>())
                .Where(item => item != null && item.ProcId == procId)
                .ToList();
            if (matches.Count != 1)
                throw new MachineAgentControlException("MACHINE_PROC_NOT_FOUND",
                    matches.Count == 0 ? "没有找到目标流程运行快照。" : "流程稳定 ID 不唯一，拒绝停止。");
            EngineSnapshot snapshot = matches[0];
            DateTime now = DateTime.UtcNow;
            string previewId = Guid.NewGuid().ToString("N");
            var blockers = new List<string>();
            var warnings = new List<string>();
            if (snapshot.State.IsInactive())
                blockers.Add("目标流程当前为 " + snapshot.State + "，没有活动运行实例需要停止。");
            if (snapshot.State == ProcRunState.Stopping)
                warnings.Add("目标流程已经处于停止中；确认后会对冻结的同一 runId 幂等重申停止。");
            if (snapshot.RunId == Guid.Empty && !snapshot.State.IsInactive())
                blockers.Add("目标流程处于活动状态但缺少 runId，不能冻结停止目标。");
            if (snapshot.IsAlarm)
                warnings.Add("停止会终止当前运行实例，但不会证明报警根因已经消除。");

            var record = new FrozenStopPreview
            {
                PreviewId = previewId,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(PreviewLifetime),
                ProcId = procId,
                ProcIndex = snapshot.ProcIndex,
                RunId = snapshot.RunId,
                Reason = reason,
                Executable = blockers.Count == 0
            };
            lock (previewLock)
            {
                RemoveExpiredPreviewsLocked(now);
                while (stopPreviews.Count >= MaximumPreviewCount)
                {
                    string oldest = stopPreviews.Values.OrderBy(item => item.CreatedAtUtc)
                        .Select(item => item.PreviewId).First();
                    stopPreviews.Remove(oldest);
                }
                stopPreviews[previewId] = record;
            }

            JObject result = new JObject
            {
                ["contract"] = "machine.process_stop.preview.v1",
                ["actionKind"] = "stop_process",
                ["previewId"] = previewId,
                ["createdAtUtc"] = now.ToString("O"),
                ["expiresAtUtc"] = record.ExpiresAtUtc.ToString("O"),
                ["requiresForegroundConfirmation"] = true,
                ["executable"] = record.Executable,
                ["reason"] = reason,
                ["target"] = new JObject
                {
                    ["procId"] = procId.ToString("D"),
                    ["procIndex"] = snapshot.ProcIndex,
                    ["procName"] = snapshot.ProcName ?? string.Empty,
                    ["runId"] = snapshot.RunId == Guid.Empty
                        ? string.Empty
                        : snapshot.RunId.ToString("D"),
                    ["state"] = snapshot.State.ToString(),
                    ["stepIndex"] = snapshot.StepIndex,
                    ["opIndex"] = snapshot.OpIndex,
                    ["alarmMessage"] = snapshot.AlarmMessage ?? string.Empty
                },
                ["blockingReasons"] = new JArray(blockers),
                ["warnings"] = new JArray(warnings),
                ["frozenFacts"] = new JObject
                {
                    ["procId"] = procId.ToString("D"),
                    ["runId"] = snapshot.RunId == Guid.Empty
                        ? string.Empty
                        : snapshot.RunId.ToString("D")
                },
                ["evidenceBoundary"] = new JObject
                {
                    ["stopsOnlyFrozenRunId"] = true,
                    ["requiresTopologyState"] = false,
                    ["blockedBySafetyLock"] = false,
                    ["aiCanExecuteDirectly"] = false
                }
            };
            WriteAudit("machine.process_stop.previewed", result);
            return result;
        }

        /// <summary>
        /// 冻结目标指令、流程修订、拓扑修订和状态序列。该调用无设备副作用。
        /// </summary>
        public JObject PreviewProcessEntry(MachineProcessEntryPreviewRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            EquipmentTopologyDefinition topology = runtime.Stores.Topology.CreateSnapshot();
            ResolvedEntryRequest resolved = ResolveEntryRequest(request, topology);
            Guid procId = resolved.ProcId;
            Guid operationId = resolved.OperationId;
            string mode = resolved.Mode;
            LocatedOperation located = LocateOperation(procId, operationId);
            EquipmentStateSnapshot state = runtime.StateHistory?.GetCurrentSnapshot()
                ?? new EquipmentStateSnapshot();
            EquipmentPerceptionSnapshot perception = runtime.StatePerception?.GetCurrentSnapshot()
                ?? new EquipmentPerceptionSnapshot();
            EngineSnapshot engineSnapshot = runtime.ProcessEngine?.GetSnapshot(located.ProcIndex);
            long processRevision = runtime.Stores.Processes.GetRevision(procId);
            long stateSequence = runtime.StateHistory?.Revision ?? 0;
            DateTime now = DateTime.UtcNow;

            var blockers = new List<string>();
            var warnings = new List<string>();
            if (runtime.Safety.IsLocked)
                blockers.Add("平台安全锁已锁定：" + (runtime.Safety.LockReason ?? "原因未记录"));
            if (engineSnapshot != null && !engineSnapshot.State.IsInactive())
                blockers.Add("目标流程当前为 " + engineSnapshot.State + "，必须先由工程师停止并确认现场。");
            if (located.Process.head?.Disable == true) blockers.Add("目标流程已禁用。");
            if (located.Step.Disable) blockers.Add("目标步骤已禁用。");
            if (located.Operation.Disable) blockers.Add("目标指令已禁用。");
            if (ProcessReadinessService.IsPlaceholder(located.Operation))
                blockers.Add("目标指令仍是配置占位，不能执行。");

            if (string.IsNullOrWhiteSpace(resolved.Objective))
                warnings.Add("没有提供本次动作目标，确认人需要直接根据指令参数判断意图。");
            if (string.IsNullOrWhiteSpace(resolved.ExpectedOutcome))
                warnings.Add("没有提供预期结果，执行后无法自动比较任务目标是否达成。");

            MachineOperationEffect operationEffect = ClassifyOperationEffect(located.Operation);
            bool requiresMachineEvidence = operationEffect == MachineOperationEffect.External
                || (resolved.Skill != null
                    && string.Equals(mode, MachineExecutionModes.ContinueFlow, StringComparison.Ordinal));
            if (operationEffect == MachineOperationEffect.Forbidden)
                blockers.Add(located.Operation is CallCustomFunc
                    ? "自定义函数的内部设备副作用无法由拓扑和指令参数完整证明，当前不允许 Machine Agent 从该指令执行。"
                    : "该指令的外部副作用类别没有权威运行契约，当前不允许 Machine Agent 执行。");
            if (operationEffect == MachineOperationEffect.External && resolved.Skill == null)
                blockers.Add("该指令会与设备、通讯、PLC、人员界面或其他流程交互，必须通过已确认节点技能 skillId 预演。");
            if (resolved.Skill == null
                && string.Equals(mode, MachineExecutionModes.ContinueFlow, StringComparison.Ordinal))
                blockers.Add("兼容流程入口不能使用 continue_flow，因为后续控制流可能产生未确认的外部副作用；必须改用已确认节点技能。");

            JToken operationData = JObject.FromObject(located.Operation);
            IReadOnlyList<MatchedTopologyNode> matches = MatchTopologyNodes(
                topology, operationData, state, perception, procId, operationId,
                resolved.Skill?.Id);
            IReadOnlyList<MatchedTopologyNode> confirmedMatches = matches
                .Where(item => item.HasConfirmedEvidence)
                .ToList();
            string[] relevantNodeIds = confirmedMatches.Select(item => item.Node.Id)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            if (requiresMachineEvidence && confirmedMatches.Count == 0)
                blockers.Add("该外部交互动作没有命中已确认拓扑节点和技能，不能证明它作用于哪个设备对象。");
            if (requiresMachineEvidence && !perception.IsRunning)
                blockers.Add("状态感知未运行，不能在动作前确认设备当前状态。");
            if (requiresMachineEvidence)
            {
                foreach (MatchedTopologyNode match in confirmedMatches)
                {
                    AddPerceptionBlockers(match, now, blockers);
                }
            }

            ConditionEvaluationContext conditionContext = CreateConditionContext(
                topology, state, perception, resolved.SkillNode?.Id, procId);
            IReadOnlyList<ConditionEvaluation> preconditionChecks = EvaluateSkillPreconditions(
                resolved.Skill, conditionContext);
            AddConditionBlockers(preconditionChecks, "技能前置条件", blockers, false);
            IReadOnlyList<RelationEvaluation> relationChecks = requiresMachineEvidence
                ? EvaluateRelationConstraints(topology, relevantNodeIds, conditionContext)
                : Array.Empty<RelationEvaluation>();
            AddRelationBlockers(relationChecks, blockers);

            ProcessReadinessAnalysis readiness = ProcessReadinessService.Analyze(
                located.ProcIndex, located.Process, runtime.ProcessEngine?.Context?.Procs,
                runtime.CreateProcessValidationContext(), runtime.Stores.Values);
            if (string.Equals(mode, MachineExecutionModes.ContinueFlow, StringComparison.Ordinal))
            {
                if (!readiness.Runnable) blockers.AddRange(readiness.RunBlockers.Select(item => "流程不可运行：" + item));
                int skipped = CountLexicalPrefixOperations(located);
                if (skipped > 0)
                    warnings.Add("continue_flow 将按编辑顺序跳过入口前 " + skipped
                        + " 条指令；这只是词法统计，跳转、异常出口和跨流程依赖仍须由 AI 阅读控制流后判断。");
            }
            else if (!readiness.Runnable)
            {
                warnings.Add("完整流程当前不可运行，但 single_operation 只绑定并执行目标指令；目标指令仍会经过引擎启动闸门。");
            }

            if (HasConfiguredBranches(located.Operation))
                warnings.Add("目标指令配置了跳转或异常出口；single_operation 不继续执行出口，continue_flow 会沿原流程语义运行。");
            string previewId = Guid.NewGuid().ToString("N");
            DateTime expiresAtUtc = now.Add(PreviewLifetime);
            var record = new FrozenEntryPreview
            {
                PreviewId = previewId,
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc,
                ProcId = procId,
                OperationId = operationId,
                ProcIndex = located.ProcIndex,
                StepIndex = located.StepIndex,
                OpIndex = located.OpIndex,
                Mode = mode,
                SkillId = resolved.Skill?.Id ?? string.Empty,
                SkillNodeId = resolved.SkillNode?.Id ?? string.Empty,
                Objective = resolved.Objective ?? string.Empty,
                ExpectedOutcome = resolved.ExpectedOutcome ?? string.Empty,
                RequiresMachineEvidence = requiresMachineEvidence,
                ProcessRevision = processRevision,
                PublishedRevision = engineSnapshot?.PublishedRevision ?? 0,
                AppliedRevision = engineSnapshot?.AppliedRevision ?? 0,
                TopologyRevision = topology.Revision,
                StateSequence = stateSequence,
                RequireGlobalStateSequence = string.Equals(
                    mode, MachineExecutionModes.ContinueFlow, StringComparison.Ordinal),
                RelevantNodeIds = relevantNodeIds,
                RelevantStateFingerprint = BuildRelevantStateFingerprint(state, relevantNodeIds),
                Executable = blockers.Count == 0
            };
            lock (previewLock)
            {
                RemoveExpiredPreviewsLocked(now);
                while (previews.Count >= MaximumPreviewCount)
                {
                    string oldest = previews.Values.OrderBy(item => item.CreatedAtUtc)
                        .Select(item => item.PreviewId).First();
                    previews.Remove(oldest);
                }
                previews[previewId] = record;
            }

            JObject result = new JObject
            {
                ["contract"] = "machine.process_entry.preview.v1",
                ["previewId"] = previewId,
                ["createdAtUtc"] = now.ToString("O"),
                ["expiresAtUtc"] = expiresAtUtc.ToString("O"),
                ["requiresForegroundConfirmation"] = true,
                ["executable"] = record.Executable,
                ["mode"] = mode,
                ["skill"] = resolved.Skill == null ? null : new JObject
                {
                    ["skillId"] = resolved.Skill.Id ?? string.Empty,
                    ["nodeId"] = resolved.SkillNode?.Id ?? string.Empty,
                    ["nodeLabel"] = resolved.SkillNode?.Label ?? string.Empty,
                    ["name"] = resolved.Skill.Name ?? string.Empty,
                    ["executionMode"] = resolved.Skill.ExecutionMode ?? string.Empty,
                    ["reviewState"] = resolved.Skill.ReviewState ?? string.Empty
                },
                ["objective"] = resolved.Objective ?? string.Empty,
                ["expectedOutcome"] = resolved.ExpectedOutcome ?? string.Empty,
                ["operationEffect"] = ToContractValue(operationEffect),
                ["target"] = BuildTarget(located, operationData),
                ["entryWindow"] = BuildEntryWindow(located),
                ["topologyMatches"] = new JArray(matches.Select(BuildTopologyMatch)),
                ["preconditionChecks"] = new JArray(preconditionChecks.Select(BuildConditionCheck)),
                ["relationChecks"] = new JArray(relationChecks.Select(BuildRelationCheck)),
                ["readiness"] = new JObject
                {
                    ["fullProcessRunnable"] = readiness.Runnable,
                    ["status"] = readiness.ReadinessStatus ?? string.Empty,
                    ["runBlockers"] = new JArray(readiness.RunBlockers ?? Array.Empty<string>()),
                    ["warnings"] = new JArray(readiness.Warnings ?? Array.Empty<string>())
                },
                ["blockingReasons"] = new JArray(blockers.Distinct(StringComparer.Ordinal)),
                ["warnings"] = new JArray(warnings.Distinct(StringComparer.Ordinal)),
                ["frozenFacts"] = new JObject
                {
                    ["processRevision"] = processRevision,
                    ["publishedRevision"] = record.PublishedRevision,
                    ["appliedRevision"] = record.AppliedRevision,
                    ["topologyRevision"] = topology.Revision,
                    ["stateSequence"] = stateSequence,
                    ["skillId"] = record.SkillId,
                    ["stateScope"] = record.RequireGlobalStateSequence
                        ? "global_for_continue_flow"
                        : relevantNodeIds.Length == 0 ? "no_physical_node" : "matched_nodes",
                    ["relevantNodeIds"] = new JArray(relevantNodeIds)
                },
                ["evidenceBoundary"] = new JObject
                {
                    ["nameBasedInferenceUsed"] = false,
                    ["operationTypeAndParametersIncluded"] = true,
                    ["topologyIsPrimaryPhysicalModel"] = true,
                    ["aiCanExecuteDirectly"] = false
                }
            };
            WriteAudit("machine.entry.previewed", result);
            return result;
        }

        /// <summary>执行前重新核对被冻结的全部现场事实；任一变化都要求重新预演。</summary>
        public JObject ExecuteProcessEntry(string previewId)
        {
            string normalized = (previewId ?? string.Empty).Trim();
            if (!Guid.TryParseExact(normalized, "N", out _))
                throw new MachineAgentControlException("MACHINE_PREVIEW_ID_INVALID", "previewId 格式无效。");

            FrozenEntryPreview record;
            lock (previewLock)
            {
                RemoveExpiredPreviewsLocked(DateTime.UtcNow);
                if (!previews.TryGetValue(normalized, out record))
                    throw new MachineAgentControlException("MACHINE_PREVIEW_NOT_FOUND", "预演不存在或已过期，请重新分析。");
                if (!record.Executable)
                    throw new MachineAgentControlException("MACHINE_PREVIEW_BLOCKED", "该预演存在阻塞项，不能确认执行。");
                // 预演只能被确认一次；后续校验失败也必须基于新现场重新预演。
                previews.Remove(normalized);
            }

            LocatedOperation located = LocateOperation(record.ProcId, record.OperationId);
            EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(located.ProcIndex);
            if (located.ProcIndex != record.ProcIndex || located.StepIndex != record.StepIndex
                || located.OpIndex != record.OpIndex
                || runtime.Stores.Processes.GetRevision(record.ProcId) != record.ProcessRevision
                || (snapshot?.PublishedRevision ?? 0) != record.PublishedRevision
                || (snapshot?.AppliedRevision ?? 0) != record.AppliedRevision)
                throw new MachineAgentControlException("MACHINE_PROCESS_CHANGED", "流程或目标指令在预演后发生变化，请重新分析。");
            EquipmentTopologyDefinition currentTopology = runtime.Stores.Topology.CreateSnapshot();
            if (currentTopology.Revision != record.TopologyRevision)
                throw new MachineAgentControlException("MACHINE_TOPOLOGY_CHANGED", "设备拓扑在预演后发生变化，请重新分析。");
            if (record.RequireGlobalStateSequence
                && (runtime.StateHistory?.Revision ?? 0) != record.StateSequence)
                throw new MachineAgentControlException("MACHINE_STATE_CHANGED",
                    "continue_flow 预演后的设备状态时间线已变化，请重新分析整段流程。");
            EquipmentStateSnapshot currentState = runtime.StateHistory?.GetCurrentSnapshot()
                ?? new EquipmentStateSnapshot();
            if (!record.RequireGlobalStateSequence
                && !string.Equals(record.RelevantStateFingerprint,
                    BuildRelevantStateFingerprint(currentState, record.RelevantNodeIds),
                    StringComparison.Ordinal))
                throw new MachineAgentControlException("MACHINE_STATE_CHANGED",
                    "目标拓扑节点状态在预演后发生变化，请重新分析。");
            if (snapshot != null && !snapshot.State.IsInactive())
                throw new MachineAgentControlException("MACHINE_PROCESS_ACTIVE", "目标流程已不再处于停止状态，请重新分析。");
            if (runtime.Safety.IsLocked)
                throw new MachineAgentControlException("MACHINE_SAFETY_LOCKED", "平台安全锁已锁定，拒绝执行。");

            ResolvedSkillBinding currentSkill = ResolveFrozenSkill(record, currentTopology);
            EquipmentPerceptionSnapshot currentPerception = runtime.StatePerception?.GetCurrentSnapshot()
                ?? new EquipmentPerceptionSnapshot();
            var executionBlockers = new List<string>();
            IReadOnlyList<MatchedTopologyNode> currentMatches = MatchTopologyNodes(
                currentTopology, JObject.FromObject(located.Operation), currentState, currentPerception,
                record.ProcId, record.OperationId, record.SkillId)
                .Where(item => item.HasConfirmedEvidence
                    && (record.RelevantNodeIds ?? Array.Empty<string>()).Contains(
                        item.Node.Id, StringComparer.Ordinal))
                .ToList();
            if (record.RequiresMachineEvidence)
            {
                if (!currentPerception.IsRunning)
                    executionBlockers.Add("状态感知已停止。");
                foreach (string nodeId in record.RelevantNodeIds ?? Array.Empty<string>())
                {
                    MatchedTopologyNode match = currentMatches.FirstOrDefault(item =>
                        string.Equals(item.Node.Id, nodeId, StringComparison.Ordinal));
                    if (match == null)
                        executionBlockers.Add("已冻结的拓扑节点“" + nodeId + "”不再具有已确认动作证据。");
                    else
                        AddPerceptionBlockers(match, DateTime.UtcNow, executionBlockers);
                }
            }
            ConditionEvaluationContext executionConditionContext = CreateConditionContext(
                currentTopology, currentState, currentPerception, currentSkill?.Node?.Id, record.ProcId);
            AddConditionBlockers(EvaluateSkillPreconditions(currentSkill?.Skill, executionConditionContext),
                "技能前置条件", executionBlockers, false);
            if (record.RequiresMachineEvidence)
            {
                AddRelationBlockers(EvaluateRelationConstraints(
                    currentTopology, record.RelevantNodeIds, executionConditionContext), executionBlockers);
            }
            if (executionBlockers.Count > 0)
                throw new MachineAgentControlException("MACHINE_LIVE_GUARD_FAILED",
                    "执行瞬间现场条件不再满足：" + string.Join("；", executionBlockers.Distinct(StringComparer.Ordinal)));

            if (runtime.ProcessTimeline == null)
                throw new MachineAgentControlException("MACHINE_TIMELINE_UNAVAILABLE",
                    "设备动作结果时间线未运行，无法建立执行闭环，拒绝下发。");
            string actionId = runtime.ProcessTimeline.BeginMachineAction(
                normalized,
                record.SkillId,
                record.ProcId,
                record.OperationId,
                located.StepIndex,
                located.OpIndex,
                currentSkill?.Node?.Id ?? string.Empty,
                currentSkill?.Node?.Label ?? string.Empty,
                record.Mode,
                record.ExpectedOutcome);
            bool accepted;
            try
            {
                accepted = string.Equals(record.Mode, MachineExecutionModes.SingleOperation, StringComparison.Ordinal)
                    ? runtime.ProcessEngine.RunSingleOpOnce(
                        located.Process, located.ProcIndex, located.StepIndex, located.OpIndex)
                    : runtime.ProcessEngine.StartProcAt(
                        located.Process, located.ProcIndex, located.StepIndex, located.OpIndex, ProcRunState.Running);
            }
            catch (Exception ex)
            {
                runtime.ProcessTimeline.FailMachineActionBeforeStart(actionId,
                    "流程引擎下发异常：" + ex.Message);
                throw new MachineAgentControlException("MACHINE_ENGINE_DISPATCH_FAILED",
                    "流程引擎下发异常；失败事实已写入设备时间线：" + ex.Message);
            }
            if (!accepted)
            {
                runtime.ProcessTimeline.FailMachineActionBeforeStart(actionId, "流程引擎拒绝动作。");
                throw new MachineAgentControlException("MACHINE_ENGINE_REJECTED",
                    "流程引擎拒绝执行；没有旁路重试，请查看运行日志并重新预演。");
            }

            EngineSnapshot started = runtime.ProcessEngine.GetSnapshot(located.ProcIndex);
            var result = new JObject
            {
                ["contract"] = "machine.process_entry.execution.v1",
                ["accepted"] = true,
                ["previewId"] = normalized,
                ["actionId"] = actionId,
                ["skillId"] = record.SkillId,
                ["mode"] = record.Mode,
                ["procId"] = record.ProcId.ToString("D"),
                ["operationId"] = record.OperationId.ToString("D"),
                ["runId"] = started?.RunId == Guid.Empty ? string.Empty : started?.RunId.ToString("D"),
                ["state"] = started?.State.ToString() ?? string.Empty,
                ["historySequence"] = runtime.StateHistory?.Revision ?? 0
            };
            WriteAudit("machine.entry.started", result);
            return result;
        }

        /// <summary>只停止预演时冻结的同一 runId；流程已结束或换代时拒绝碰触新实例。</summary>
        public JObject ExecuteProcessStop(string previewId)
        {
            string normalized = (previewId ?? string.Empty).Trim();
            if (!Guid.TryParseExact(normalized, "N", out _))
                throw new MachineAgentControlException(
                    "MACHINE_PREVIEW_ID_INVALID", "previewId 格式无效。");

            FrozenStopPreview record;
            lock (previewLock)
            {
                RemoveExpiredPreviewsLocked(DateTime.UtcNow);
                if (!stopPreviews.TryGetValue(normalized, out record))
                    throw new MachineAgentControlException(
                        "MACHINE_PREVIEW_NOT_FOUND", "停止预演不存在或已过期，请重新读取流程状态。");
                if (!record.Executable)
                    throw new MachineAgentControlException(
                        "MACHINE_PREVIEW_BLOCKED", "该停止预演存在阻塞项，不能确认执行。");
                // 原子认领，防止两个前台请求重复消费同一停止预演。
                stopPreviews.Remove(normalized);
            }

            EngineSnapshot current = runtime.ProcessEngine?.GetSnapshot(record.ProcIndex);
            if (current == null
                || current.ProcId != record.ProcId
                || current.RunId != record.RunId)
                throw new MachineAgentControlException(
                    "MACHINE_PROCESS_INSTANCE_CHANGED",
                    "目标流程运行实例已变化，拒绝停止未被预演冻结的新实例。");
            if (current.State.IsInactive())
                throw new MachineAgentControlException(
                    "MACHINE_PROCESS_INACTIVE", "目标流程已经结束，无需再次停止。");
            string actionId = string.Empty;
            try
            {
                actionId = runtime.ProcessTimeline?.BeginMachineStop(
                    normalized,
                    record.ProcId,
                    record.RunId,
                    record.ProcIndex,
                    current.StepIndex,
                    current.OpIndex,
                    current.ProcName,
                    record.Reason) ?? string.Empty;
            }
            catch (Exception ex)
            {
                // 停止是安全动作；辅助时间线故障只降级审计，不能阻止向流程控制器下发。
                WriteAudit("machine.process_stop.timeline_degraded", new JObject
                {
                    ["previewId"] = normalized,
                    ["procId"] = record.ProcId.ToString("D"),
                    ["runId"] = record.RunId.ToString("D"),
                    ["error"] = ex.Message
                });
            }
            EngineSnapshot dispatchSnapshot = runtime.ProcessEngine?.GetSnapshot(record.ProcIndex);
            if (dispatchSnapshot == null
                || dispatchSnapshot.ProcId != record.ProcId
                || dispatchSnapshot.RunId != record.RunId
                || dispatchSnapshot.State.IsInactive())
            {
                runtime.ProcessTimeline?.FailMachineActionBeforeStart(
                    actionId, "停止下发前流程运行实例已经结束或换代。");
                throw new MachineAgentControlException(
                    "MACHINE_PROCESS_INSTANCE_CHANGED",
                    "停止下发前流程运行实例已经结束或换代，没有停止新实例。");
            }
            ProcessStopRequestResult dispatch;
            try
            {
                Guid.TryParse(actionId, out Guid attemptId);
                dispatch = runtime.ProcessControl?.Stop(
                        record.ProcIndex,
                        record.RunId,
                        attemptId)
                    ?? ProcessStopRequestResult.Reject(record.RunId, attemptId);
            }
            catch (Exception ex)
            {
                runtime.ProcessTimeline?.FailMachineActionBeforeStart(
                    actionId, "停止流程下发异常：" + ex.Message);
                throw new MachineAgentControlException(
                    "MACHINE_STOP_DISPATCH_FAILED", "停止流程下发异常：" + ex.Message);
            }
            if (!dispatch.Accepted)
            {
                runtime.ProcessTimeline?.FailMachineActionBeforeStart(
                    actionId, "流程控制器拒绝停止请求。");
                throw new MachineAgentControlException(
                    "MACHINE_STOP_REJECTED", "流程控制器拒绝停止请求。");
            }

            var result = new JObject
            {
                ["contract"] = "machine.process_stop.execution.v1",
                ["accepted"] = dispatch.Accepted,
                ["dispatchStatus"] = dispatch.IsReassertion ? "reasserted" : "accepted",
                ["reasserted"] = dispatch.IsReassertion,
                ["previewId"] = normalized,
                ["actionId"] = actionId,
                ["timelineTracked"] = !string.IsNullOrWhiteSpace(actionId),
                ["procId"] = record.ProcId.ToString("D"),
                ["runId"] = record.RunId.ToString("D"),
                ["reason"] = record.Reason,
                ["state"] = runtime.ProcessEngine?.GetSnapshot(record.ProcIndex)?.State.ToString()
                    ?? string.Empty
            };
            WriteAudit("machine.process_stop.requested", result);
            return result;
        }

        public bool DiscardPreview(string previewId)
        {
            lock (previewLock)
            {
                string normalized = (previewId ?? string.Empty).Trim();
                return previews.Remove(normalized) || stopPreviews.Remove(normalized);
            }
        }

        private ResolvedEntryRequest ResolveEntryRequest(
            MachineProcessEntryPreviewRequest request,
            EquipmentTopologyDefinition topology)
        {
            string skillId = (request.SkillId ?? string.Empty).Trim();
            if (skillId.Length > 0)
            {
                List<ResolvedSkillBinding> matches = FindSkills(topology, skillId);
                if (matches.Count != 1)
                    throw new MachineAgentControlException("MACHINE_SKILL_NOT_FOUND",
                        matches.Count == 0 ? "没有找到节点技能。" : "节点技能 ID 不唯一，拒绝预演。");
                ResolvedSkillBinding binding = matches[0];
                EquipmentTopologySkillBinding skill = binding.Skill;
                if (!string.Equals(binding.Node.ReviewState, "confirmed", StringComparison.Ordinal)
                    || !string.Equals(skill.ReviewState, "confirmed", StringComparison.Ordinal))
                    throw new MachineAgentControlException("MACHINE_SKILL_NOT_CONFIRMED",
                        "节点及技能都必须经人工确认为 confirmed 后才能用于设备控制。");
                if (!string.Equals(skill.ActionKind, "process_operation", StringComparison.Ordinal))
                    throw new MachineAgentControlException("MACHINE_SKILL_ACTION_INVALID",
                        "当前只支持绑定既有流程指令的 process_operation 节点技能。");
                if (!Guid.TryParse(skill.ProcessId, out Guid skillProcId) || skillProcId == Guid.Empty
                    || !Guid.TryParse(skill.OperationId, out Guid skillOperationId) || skillOperationId == Guid.Empty
                    || !MachineExecutionModes.IsSupported(skill.ExecutionMode))
                    throw new MachineAgentControlException("MACHINE_SKILL_BINDING_INVALID",
                        "节点技能的流程、指令或执行模式绑定无效。");

                EnsureSkillFieldNotOverridden("procId", request.ProcId, skill.ProcessId, true);
                EnsureSkillFieldNotOverridden("operationId", request.OperationId, skill.OperationId, true);
                EnsureSkillFieldNotOverridden("mode", request.Mode, skill.ExecutionMode, false);
                EnsureSkillFieldNotOverridden("objective", request.Objective, skill.Objective, false);
                EnsureSkillFieldNotOverridden(
                    "expectedOutcome", request.ExpectedOutcome, skill.ExpectedOutcome, false);
                return new ResolvedEntryRequest
                {
                    ProcId = skillProcId,
                    OperationId = skillOperationId,
                    Mode = skill.ExecutionMode,
                    Objective = skill.Objective,
                    ExpectedOutcome = skill.ExpectedOutcome,
                    SkillNode = binding.Node,
                    Skill = skill
                };
            }

            if (!Guid.TryParse(request.ProcId, out Guid procId) || procId == Guid.Empty)
                throw new MachineAgentControlException("MACHINE_PROC_ID_INVALID",
                    "必须提供已确认节点技能 skillId；无外部副作用的兼容诊断可提供现有流程 procId。");
            if (!Guid.TryParse(request.OperationId, out Guid operationId) || operationId == Guid.Empty)
                throw new MachineAgentControlException("MACHINE_OPERATION_ID_INVALID",
                    "operationId 必须是现有指令的稳定 ID。");
            string mode = (request.Mode ?? string.Empty).Trim();
            if (!MachineExecutionModes.IsSupported(mode))
                throw new MachineAgentControlException("MACHINE_EXECUTION_MODE_INVALID",
                    "mode 只允许 single_operation 或 continue_flow。");
            return new ResolvedEntryRequest
            {
                ProcId = procId,
                OperationId = operationId,
                Mode = mode,
                Objective = request.Objective,
                ExpectedOutcome = request.ExpectedOutcome
            };
        }

        private static void EnsureSkillFieldNotOverridden(
            string fieldName, string requested, string approved, bool ignoreCase)
        {
            string supplied = (requested ?? string.Empty).Trim();
            if (supplied.Length == 0) return;
            StringComparison comparison = ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(supplied, (approved ?? string.Empty).Trim(), comparison))
                throw new MachineAgentControlException("MACHINE_SKILL_OVERRIDE_FORBIDDEN",
                    "提供 skillId 后，" + fieldName + " 不能覆盖已确认技能绑定。批准值为：“"
                    + (approved ?? string.Empty) + "”。");
        }

        private static List<ResolvedSkillBinding> FindSkills(
            EquipmentTopologyDefinition topology, string skillId)
        {
            return (topology?.Nodes ?? new List<EquipmentTopologyNode>())
                .Where(node => node != null)
                .SelectMany(node => (node.Skills ?? new List<EquipmentTopologySkillBinding>())
                    .Where(skill => skill != null
                        && string.Equals(skill.Id, skillId, StringComparison.Ordinal))
                    .Select(skill => new ResolvedSkillBinding { Node = node, Skill = skill }))
                .ToList();
        }

        private static ResolvedSkillBinding ResolveFrozenSkill(
            FrozenEntryPreview record, EquipmentTopologyDefinition topology)
        {
            if (string.IsNullOrWhiteSpace(record.SkillId))
            {
                if (record.RequiresMachineEvidence)
                    throw new MachineAgentControlException("MACHINE_SKILL_REQUIRED",
                        "设备交互预演没有冻结节点技能，拒绝执行。");
                return null;
            }
            List<ResolvedSkillBinding> matches = FindSkills(topology, record.SkillId);
            if (matches.Count != 1)
                throw new MachineAgentControlException("MACHINE_SKILL_CHANGED",
                    "已冻结节点技能不存在或不唯一，请重新预演。");
            ResolvedSkillBinding current = matches[0];
            if (!string.Equals(current.Node.Id, record.SkillNodeId, StringComparison.Ordinal)
                || !string.Equals(current.Node.ReviewState, "confirmed", StringComparison.Ordinal)
                || !string.Equals(current.Skill.ReviewState, "confirmed", StringComparison.Ordinal)
                || !string.Equals(current.Skill.ProcessId, record.ProcId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(current.Skill.OperationId, record.OperationId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(current.Skill.ExecutionMode, record.Mode, StringComparison.Ordinal)
                || !string.Equals(current.Skill.Objective ?? string.Empty, record.Objective ?? string.Empty,
                    StringComparison.Ordinal)
                || !string.Equals(current.Skill.ExpectedOutcome ?? string.Empty,
                    record.ExpectedOutcome ?? string.Empty, StringComparison.Ordinal))
                throw new MachineAgentControlException("MACHINE_SKILL_CHANGED",
                    "节点技能的审核状态或已批准绑定发生变化，请重新预演。");
            return current;
        }

        private static void AddPerceptionBlockers(
            MatchedTopologyNode match, DateTime now, ICollection<string> blockers)
        {
            string label = match.Node.Label ?? match.Node.Id;
            EquipmentNodePerceptionState observed = match.PerceptionState;
            if (observed == null)
            {
                blockers.Add("节点“" + label + "”尚无实时感知事实。");
                return;
            }
            if (!string.Equals(observed.Quality, EquipmentStateQualities.Good, StringComparison.Ordinal))
                blockers.Add("节点“" + label + "”状态质量为 "
                    + (observed.Quality ?? EquipmentStateQualities.Unknown) + "。");
            if (observed.LastSuccessfulObservationAtUtc == default(DateTime)
                || now - observed.LastSuccessfulObservationAtUtc > ObservationFailureTolerance)
                blockers.Add("节点“" + label + "”现场读取已连续超过 "
                    + ObservationFailureTolerance.TotalSeconds.ToString("0") + " 秒没有成功。");
        }

        private ConditionEvaluationContext CreateConditionContext(
            EquipmentTopologyDefinition topology,
            EquipmentStateSnapshot state,
            EquipmentPerceptionSnapshot perception,
            string currentNodeId,
            Guid targetProcessId)
        {
            return new ConditionEvaluationContext
            {
                CurrentNodeId = currentNodeId ?? string.Empty,
                TargetProcessId = targetProcessId,
                EvaluationTimeUtc = DateTime.UtcNow,
                NodeStates = (state?.NodeStates ?? new List<EquipmentNodeStateProjection>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                    .GroupBy(item => item.NodeId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Sequence).First(),
                        StringComparer.Ordinal),
                PerceptionStates = (perception?.NodeStates ?? new List<EquipmentNodePerceptionState>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                    .GroupBy(item => item.NodeId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key,
                        group => group.OrderByDescending(item => item.LastSuccessfulObservationAtUtc).First(),
                        StringComparer.Ordinal),
                ProcessSnapshots = (runtime.ProcessEngine?.GetSnapshots() ?? new List<EngineSnapshot>())
                    .Where(item => item != null && item.ProcId != Guid.Empty)
                    .GroupBy(item => item.ProcId)
                    .ToDictionary(group => group.Key, group => group.First()),
                SafetyLocked = runtime.Safety.IsLocked,
                TopologyNodeIds = new HashSet<string>((topology?.Nodes ?? new List<EquipmentTopologyNode>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
                    .Select(item => item.Id), StringComparer.Ordinal)
            };
        }

        private static IReadOnlyList<ConditionEvaluation> EvaluateSkillPreconditions(
            EquipmentTopologySkillBinding skill, ConditionEvaluationContext context)
        {
            if (skill == null) return Array.Empty<ConditionEvaluation>();
            return (skill.Preconditions ?? new List<string>())
                .Select(expression => EvaluateCondition(expression, context, null, null))
                .ToList();
        }

        private static IReadOnlyList<RelationEvaluation> EvaluateRelationConstraints(
            EquipmentTopologyDefinition topology,
            IEnumerable<string> relevantNodeIds,
            ConditionEvaluationContext context)
        {
            var relevant = new HashSet<string>(relevantNodeIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            return (topology?.Relations ?? new List<EquipmentTopologyRelation>())
                .Where(relation => relation != null
                    && string.Equals(relation.ReviewState, "confirmed", StringComparison.Ordinal)
                    && (relevant.Contains(relation.SourceNodeId ?? string.Empty)
                        || relevant.Contains(relation.TargetNodeId ?? string.Empty))
                    && (string.Equals(relation.Kind, "requires", StringComparison.Ordinal)
                        || string.Equals(relation.Kind, "blocks", StringComparison.Ordinal)
                        || string.Equals(relation.Kind, "interlock", StringComparison.Ordinal)
                        || string.Equals(relation.Layer, "interlock", StringComparison.Ordinal)))
                .Select(relation =>
                {
                    ConditionEvaluation condition = EvaluateCondition(
                        relation.Condition, context, relation.SourceNodeId, relation.TargetNodeId);
                    bool blocks = !condition.Evaluable
                        || (string.Equals(relation.Kind, "blocks", StringComparison.Ordinal)
                            ? condition.Satisfied
                            : !condition.Satisfied);
                    return new RelationEvaluation
                    {
                        Relation = relation,
                        Condition = condition,
                        BlocksExecution = blocks
                    };
                })
                .ToList();
        }

        private static ConditionEvaluation EvaluateCondition(
            string expression,
            ConditionEvaluationContext context,
            string sourceNodeId,
            string targetNodeId)
        {
            string text = (expression ?? string.Empty).Trim();
            if (string.Equals(text, "节点实时状态质量为 good", StringComparison.Ordinal))
            {
                string nodeId = ResolveNodeId("$current", context, sourceNodeId, targetNodeId);
                if (!TryGetFreshPerceptionState(context, nodeId, out EquipmentNodePerceptionState state,
                    out string perceptionDetail))
                    return ConditionEvaluation.Unknown(text, perceptionDetail);
                return ConditionEvaluation.Known(text, true,
                    "节点质量=" + state.Quality + "；" + perceptionDetail);
            }
            if (string.Equals(text, "流程处于非活动状态", StringComparison.Ordinal))
            {
                bool exists = context.ProcessSnapshots.TryGetValue(
                    context.TargetProcessId, out EngineSnapshot snapshot);
                if (!exists)
                    return ConditionEvaluation.Unknown(text, "缺少目标流程运行快照。");
                return ConditionEvaluation.Known(text, exists && snapshot.State.IsInactive(),
                    "流程状态=" + snapshot.State);
            }

            JObject definition;
            try
            {
                definition = JObject.Parse(text);
            }
            catch (JsonException)
            {
                return ConditionEvaluation.Unknown(text,
                    "不支持的自由文本；请使用当前支持的规范条件或严格 JSON 条件。");
            }
            string[] allowedFields = { "kind", "nodeId", "processId", "operator", "value" };
            if (definition.Properties().Any(property => !allowedFields.Contains(property.Name, StringComparer.Ordinal))
                || definition.Properties().Any(property => property.Value.Type != JTokenType.String))
                return ConditionEvaluation.Unknown(text, "条件包含未知字段或非字符串字段。");
            string kind = definition.Value<string>("kind") ?? string.Empty;
            string op = definition.Value<string>("operator") ?? string.Empty;
            string expected = definition.Value<string>("value") ?? string.Empty;
            if ((op != "equals" && op != "not_equals") || expected.Length == 0)
                return ConditionEvaluation.Unknown(text, "operator 只允许 equals/not_equals，且 value 不能为空。");

            string actual;
            if (kind == "node_state" || kind == "node_quality")
            {
                string nodeId = ResolveNodeId(
                    definition.Value<string>("nodeId"), context, sourceNodeId, targetNodeId);
                if (nodeId.Length == 0 || !context.TopologyNodeIds.Contains(nodeId))
                    return ConditionEvaluation.Unknown(text, "条件引用的节点不存在。");
                if (!TryGetFreshPerceptionState(context, nodeId, out EquipmentNodePerceptionState perception,
                    out string perceptionDetail))
                    return ConditionEvaluation.Unknown(text, perceptionDetail);
                if (kind == "node_quality")
                {
                    actual = perception.Quality ?? EquipmentStateQualities.Unknown;
                }
                else
                {
                    if (!context.NodeStates.TryGetValue(nodeId, out EquipmentNodeStateProjection current))
                        return ConditionEvaluation.Unknown(text, "节点缺少语义状态事实。");
                    actual = current.StateName ?? string.Empty;
                }
            }
            else if (kind == "process_state")
            {
                string processText = definition.Value<string>("processId") ?? string.Empty;
                Guid processId = string.Equals(processText, "$target", StringComparison.Ordinal)
                    ? context.TargetProcessId
                    : Guid.TryParse(processText, out Guid parsed) ? parsed : Guid.Empty;
                if (processId == Guid.Empty
                    || !context.ProcessSnapshots.TryGetValue(processId, out EngineSnapshot processState))
                    return ConditionEvaluation.Unknown(text, "条件引用的流程不存在或没有运行快照。");
                actual = processState.State.IsInactive() ? "inactive" : processState.State.ToString();
            }
            else if (kind == "safety_state")
            {
                actual = context.SafetyLocked ? "locked" : "unlocked";
            }
            else
            {
                return ConditionEvaluation.Unknown(text, "kind 只支持 node_state、node_quality、process_state、safety_state。");
            }

            bool equal = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            bool satisfied = op == "equals" ? equal : !equal;
            return ConditionEvaluation.Known(text, satisfied, "实际值=" + actual + "，期望=" + op + " " + expected);
        }

        private static bool TryGetFreshPerceptionState(
            ConditionEvaluationContext context,
            string nodeId,
            out EquipmentNodePerceptionState state,
            out string detail)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(nodeId)
                || context?.PerceptionStates == null
                || !context.PerceptionStates.TryGetValue(nodeId, out state))
            {
                detail = "节点缺少实时感知事实。";
                return false;
            }
            if (!string.Equals(state.Quality, EquipmentStateQualities.Good, StringComparison.Ordinal))
            {
                detail = "节点实时感知质量为 "
                    + (state.Quality ?? EquipmentStateQualities.Unknown) + "。";
                return false;
            }
            if (state.LastSuccessfulObservationAtUtc == default(DateTime))
            {
                detail = "节点没有成功现场观测时间。";
                return false;
            }
            DateTime evaluationTime = context.EvaluationTimeUtc == default(DateTime)
                ? DateTime.UtcNow
                : context.EvaluationTimeUtc;
            if (evaluationTime - state.LastSuccessfulObservationAtUtc > ObservationFailureTolerance)
            {
                detail = "节点现场读取已连续超过 "
                    + ObservationFailureTolerance.TotalSeconds.ToString("0") + " 秒没有成功。";
                return false;
            }
            detail = "最近成功观测=" + state.LastSuccessfulObservationAtUtc.ToString("O");
            return true;
        }

        private static string ResolveNodeId(
            string value,
            ConditionEvaluationContext context,
            string sourceNodeId,
            string targetNodeId)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized == "$current") return context.CurrentNodeId ?? string.Empty;
            if (normalized == "$source") return sourceNodeId ?? string.Empty;
            if (normalized == "$target") return targetNodeId ?? string.Empty;
            return normalized;
        }

        private static void AddConditionBlockers(
            IEnumerable<ConditionEvaluation> checks,
            string prefix,
            ICollection<string> blockers,
            bool blockWhenSatisfied)
        {
            foreach (ConditionEvaluation check in checks ?? Enumerable.Empty<ConditionEvaluation>())
            {
                if (!check.Evaluable)
                    blockers.Add(prefix + "无法机械求值：“" + check.Expression + "”（" + check.Detail + "）");
                else if (blockWhenSatisfied ? check.Satisfied : !check.Satisfied)
                    blockers.Add(prefix + "不满足：“" + check.Expression + "”（" + check.Detail + "）");
            }
        }

        private static void AddRelationBlockers(
            IEnumerable<RelationEvaluation> checks,
            ICollection<string> blockers)
        {
            foreach (RelationEvaluation check in checks ?? Enumerable.Empty<RelationEvaluation>())
            {
                if (!check.BlocksExecution) continue;
                EquipmentTopologyRelation relation = check.Relation;
                string prefix = "已确认防呆关系“" + (relation.Label ?? relation.Id ?? relation.Kind) + "”";
                blockers.Add(!check.Condition.Evaluable
                    ? prefix + "的成立条件无法机械求值，存在证据缺口：“" + (relation.Condition ?? string.Empty) + "”。"
                    : prefix + (string.Equals(relation.Kind, "blocks", StringComparison.Ordinal)
                        ? "当前阻塞动作。"
                        : "当前未满足。") + "（" + check.Condition.Detail + "）");
            }
        }

        private static JObject BuildConditionCheck(ConditionEvaluation check)
        {
            return new JObject
            {
                ["expression"] = check.Expression ?? string.Empty,
                ["evaluable"] = check.Evaluable,
                ["satisfied"] = check.Satisfied,
                ["detail"] = check.Detail ?? string.Empty
            };
        }

        private static JObject BuildRelationCheck(RelationEvaluation check)
        {
            EquipmentTopologyRelation relation = check.Relation;
            return new JObject
            {
                ["relationId"] = relation.Id ?? string.Empty,
                ["sourceNodeId"] = relation.SourceNodeId ?? string.Empty,
                ["targetNodeId"] = relation.TargetNodeId ?? string.Empty,
                ["layer"] = relation.Layer ?? string.Empty,
                ["kind"] = relation.Kind ?? string.Empty,
                ["condition"] = BuildConditionCheck(check.Condition),
                ["blocksExecution"] = check.BlocksExecution
            };
        }

        private static MachineOperationEffect ClassifyOperationEffect(OperationType operation)
        {
            if (operation == null || operation is CallCustomFunc
                || operation is ConfigurationPlaceholder)
                return MachineOperationEffect.Forbidden;

            if (operation is IoOperate || operation is IoCheck || operation is IoGroup
                || operation is IoLogicGoto || operation is ProcOps || operation is WaitProc
                || operation is PopupDialog || operation is CommunicationOperationType
                || operation is TcpOps || operation is WaitTcp || operation is SerialPortOps
                || operation is WaitSerialPort || operation is PlcMappingControl
                || operation is CreateTray || operation is TrayRunPos || operation is HomeRun
                || operation is StationRunPos || operation is ModifyStationPos
                || operation is GetStationPos || operation is StationRunRel
                || operation is SetStationVel || operation is StationStop
                || operation is WaitStationStop)
                return MachineOperationEffect.External;

            if (operation is CycleTimeProbe || operation is Goto || operation is ParamGoto
                || operation is Delay || operation is EndProcess || operation is GetValue
                || operation is ModifyValue || operation is StringFormat || operation is Split
                || operation is Replace || operation is SetDataStructItem
                || operation is GetDataStructItem || operation is CopyDataStructItem
                || operation is InsertDataStructItem || operation is DelDataStructItem
                || operation is FindDataStructItem || operation is GetDataStructCount)
                return MachineOperationEffect.None;

            return MachineOperationEffect.Forbidden;
        }

        private static string ToContractValue(MachineOperationEffect effect)
        {
            switch (effect)
            {
                case MachineOperationEffect.None: return "none";
                case MachineOperationEffect.External: return "external_interaction";
                default: return "forbidden_or_unknown";
            }
        }

        private LocatedOperation LocateOperation(Guid procId, Guid operationId)
        {
            IList<Proc> processes = runtime.ProcessEngine?.Context?.Procs
                ?? runtime.Stores.Processes.Items;
            List<int> procMatches = Enumerable.Range(0, processes.Count)
                .Where(index => processes[index]?.head?.Id == procId).ToList();
            if (procMatches.Count != 1)
                throw new MachineAgentControlException("MACHINE_PROC_NOT_FOUND",
                    procMatches.Count == 0 ? "没有找到目标流程。" : "流程稳定 ID 不唯一，拒绝执行。");
            int procIndex = procMatches[0];
            Proc proc = processes[procIndex];
            var matches = new List<LocatedOperation>();
            for (int stepIndex = 0; stepIndex < (proc.steps?.Count ?? 0); stepIndex++)
            {
                Step step = proc.steps[stepIndex];
                for (int opIndex = 0; opIndex < (step?.Ops?.Count ?? 0); opIndex++)
                {
                    if (step.Ops[opIndex]?.Id == operationId)
                    {
                        matches.Add(new LocatedOperation
                        {
                            Process = proc,
                            Step = step,
                            Operation = step.Ops[opIndex],
                            ProcIndex = procIndex,
                            StepIndex = stepIndex,
                            OpIndex = opIndex
                        });
                    }
                }
            }
            if (matches.Count != 1)
                throw new MachineAgentControlException("MACHINE_OPERATION_NOT_FOUND",
                    matches.Count == 0 ? "目标流程中没有找到该指令。" : "指令稳定 ID 不唯一，拒绝执行。");
            return matches[0];
        }

        private static JObject BuildTarget(LocatedOperation located, JToken operationData)
        {
            return new JObject
            {
                ["procId"] = located.Process.head.Id.ToString("D"),
                ["procIndex"] = located.ProcIndex,
                ["procName"] = located.Process.head.Name ?? string.Empty,
                ["stepId"] = located.Step.Id == Guid.Empty ? string.Empty : located.Step.Id.ToString("D"),
                ["stepIndex"] = located.StepIndex,
                ["stepName"] = located.Step.Name ?? string.Empty,
                ["operationId"] = located.Operation.Id.ToString("D"),
                ["opIndex"] = located.OpIndex,
                ["operationName"] = located.Operation.Name ?? string.Empty,
                ["operationType"] = located.Operation.OperaType ?? located.Operation.GetType().Name,
                ["runtimeType"] = located.Operation.GetType().Name,
                ["disabled"] = located.Operation.Disable,
                ["alarmType"] = located.Operation.AlarmType ?? string.Empty,
                ["parameters"] = operationData,
                ["behaviorContract"] = OperationBehaviorCatalog.BuildContract(located.Operation)
            };
        }

        private static JArray BuildEntryWindow(LocatedOperation located)
        {
            var result = new JArray();
            int start = Math.Max(0, located.OpIndex - 6);
            int end = Math.Min(located.Step.Ops.Count - 1, located.OpIndex + 10);
            for (int index = start; index <= end; index++)
            {
                OperationType operation = located.Step.Ops[index];
                if (operation == null) continue;
                result.Add(new JObject
                {
                    ["relative"] = index - located.OpIndex,
                    ["isTarget"] = index == located.OpIndex,
                    ["operationId"] = operation.Id == Guid.Empty ? string.Empty : operation.Id.ToString("D"),
                    ["opIndex"] = index,
                    ["name"] = operation.Name ?? string.Empty,
                    ["operationType"] = operation.OperaType ?? operation.GetType().Name,
                    ["runtimeType"] = operation.GetType().Name,
                    ["disabled"] = operation.Disable,
                    ["alarmType"] = operation.AlarmType ?? string.Empty,
                    ["goto1"] = operation.Goto1 ?? string.Empty,
                    ["goto2"] = operation.Goto2 ?? string.Empty,
                    ["goto3"] = operation.Goto3 ?? string.Empty
                });
            }
            return result;
        }

        private static int CountLexicalPrefixOperations(LocatedOperation located)
        {
            int count = 0;
            for (int stepIndex = 0; stepIndex <= located.StepIndex; stepIndex++)
            {
                Step step = located.Process.steps[stepIndex];
                if (step == null || step.Disable) continue;
                int end = stepIndex == located.StepIndex ? located.OpIndex : step.Ops.Count;
                count += step.Ops.Take(end).Count(operation => operation != null && !operation.Disable);
            }
            return count;
        }

        private static bool HasConfiguredBranches(OperationType operation)
        {
            return !string.IsNullOrWhiteSpace(operation?.Goto1)
                || !string.IsNullOrWhiteSpace(operation?.Goto2)
                || !string.IsNullOrWhiteSpace(operation?.Goto3)
                || string.Equals(operation?.AlarmType, "自动处理", StringComparison.Ordinal);
        }

        private static IReadOnlyList<MatchedTopologyNode> MatchTopologyNodes(
            EquipmentTopologyDefinition topology,
            JToken operationData,
            EquipmentStateSnapshot state,
            EquipmentPerceptionSnapshot perception,
            Guid processId,
            Guid operationId,
            string selectedSkillId)
        {
            var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            CollectStringValues(operationData, "$", values);
            Dictionary<string, EquipmentNodeStateProjection> states =
                (state.NodeStates ?? new List<EquipmentNodeStateProjection>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                .GroupBy(item => item.NodeId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Sequence).First(),
                    StringComparer.Ordinal);
            Dictionary<string, EquipmentNodePerceptionState> perceptionStates =
                (perception?.NodeStates ?? new List<EquipmentNodePerceptionState>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                .GroupBy(item => item.NodeId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.OrderByDescending(item => item.LastSuccessfulObservationAtUtc).First(),
                    StringComparer.Ordinal);
            var result = new List<MatchedTopologyNode>();
            foreach (EquipmentTopologyNode node in topology.Nodes ?? new List<EquipmentTopologyNode>())
            {
                if (node == null) continue;
                var paths = new List<string>();
                var confirmedPaths = new List<string>();
                AddMatchPaths(values, node.ResourceRef, paths);
                if (string.Equals(node.ReviewState, "confirmed", StringComparison.Ordinal))
                    AddMatchPaths(values, node.ResourceRef, confirmedPaths);
                foreach (EquipmentTopologyStateBinding binding in node.StateBindings
                    ?? new List<EquipmentTopologyStateBinding>())
                {
                    AddMatchPaths(values, binding?.ResourceRef, paths);
                    if (string.Equals(binding?.ReviewState, "confirmed", StringComparison.Ordinal))
                        AddMatchPaths(values, binding?.ResourceRef, confirmedPaths);
                }
                bool hasConfirmedResourceMatch = confirmedPaths.Count > 0;
                List<EquipmentTopologySkillBinding> skillMatches = (node.Skills
                    ?? new List<EquipmentTopologySkillBinding>())
                    .Where(skill => skill != null
                        && !string.IsNullOrWhiteSpace(selectedSkillId)
                        && string.Equals(skill.Id, selectedSkillId, StringComparison.Ordinal)
                        && string.Equals(skill.ProcessId, processId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                        && string.Equals(skill.OperationId, operationId.ToString("D"), StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (EquipmentTopologySkillBinding skill in skillMatches)
                    paths.Add("$.skills[" + (skill.Id ?? "matched") + "]");
                if (paths.Count == 0) continue;
                states.TryGetValue(node.Id ?? string.Empty, out EquipmentNodeStateProjection projection);
                perceptionStates.TryGetValue(
                    node.Id ?? string.Empty, out EquipmentNodePerceptionState perceptionState);
                result.Add(new MatchedTopologyNode
                {
                    Node = node,
                    State = projection,
                    PerceptionState = perceptionState,
                    ParameterPaths = paths.Distinct(StringComparer.Ordinal).OrderBy(item => item).ToList(),
                    Skills = skillMatches,
                    HasConfirmedEvidence = string.Equals(
                        node.ReviewState, "confirmed", StringComparison.Ordinal)
                        && (hasConfirmedResourceMatch || skillMatches.Any(skill => string.Equals(
                            skill.ReviewState, "confirmed", StringComparison.Ordinal)))
                });
            }
            return result;
        }

        private static void AddMatchPaths(
            IReadOnlyDictionary<string, List<string>> values,
            string resourceRef,
            ICollection<string> paths)
        {
            string key = (resourceRef ?? string.Empty).Trim();
            if (key.Length == 0 || !values.TryGetValue(key, out List<string> matched)) return;
            foreach (string path in matched) paths.Add(path);
        }

        private static void CollectStringValues(
            JToken token,
            string path,
            IDictionary<string, List<string>> result)
        {
            if (token == null) return;
            if (token.Type == JTokenType.String)
            {
                string value = (token.Value<string>() ?? string.Empty).Trim();
                if (value.Length == 0) return;
                if (!result.TryGetValue(value, out List<string> paths))
                {
                    paths = new List<string>();
                    result[value] = paths;
                }
                paths.Add(path);
                return;
            }
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    if (string.Equals(path, "$", StringComparison.Ordinal)
                        && OperationMetadataFields.Contains(property.Name))
                        continue;
                    CollectStringValues(property.Value, path + "." + property.Name, result);
                }
            }
            else if (token is JArray array)
            {
                for (int index = 0; index < array.Count; index++)
                    CollectStringValues(array[index], path + "[" + index + "]", result);
            }
        }

        private static JObject BuildTopologyMatch(MatchedTopologyNode match)
        {
            return new JObject
            {
                ["nodeId"] = match.Node.Id ?? string.Empty,
                ["label"] = match.Node.Label ?? string.Empty,
                ["kind"] = match.Node.Kind ?? string.Empty,
                ["reviewState"] = match.Node.ReviewState ?? string.Empty,
                ["resourceKind"] = match.Node.ResourceKind ?? string.Empty,
                ["resourceRef"] = match.Node.ResourceRef ?? string.Empty,
                ["matchedParameterPaths"] = new JArray(match.ParameterPaths),
                ["matchedSkills"] = new JArray((match.Skills
                    ?? Array.Empty<EquipmentTopologySkillBinding>())
                    .Where(item => item != null)
                    .Select(BuildSkillContext)),
                ["confirmedEvidence"] = match.HasConfirmedEvidence,
                ["currentState"] = match.State == null ? null : new JObject
                {
                    ["name"] = match.State.StateName ?? string.Empty,
                    ["meaning"] = match.State.Meaning ?? string.Empty,
                    ["quality"] = match.State.Quality ?? EquipmentStateQualities.Unknown,
                    ["confidence"] = match.State.Confidence,
                    ["sequence"] = match.State.Sequence,
                    ["updatedAtUtc"] = match.State.UpdatedAtUtc.ToString("O")
                },
                ["perception"] = match.PerceptionState == null ? null : new JObject
                {
                    ["quality"] = match.PerceptionState.Quality ?? EquipmentStateQualities.Unknown,
                    ["stateChangedAtUtc"] = match.PerceptionState.StateChangedAtUtc.ToString("O"),
                    ["lastSuccessfulObservationAtUtc"] =
                        match.PerceptionState.LastSuccessfulObservationAtUtc.ToString("O")
                }
            };
        }

        private static JObject BuildNodeContext(
            EquipmentTopologyNode node,
            EquipmentNodeStateProjection state,
            EquipmentNodePerceptionState perception)
        {
            return new JObject
            {
                ["nodeId"] = node.Id ?? string.Empty,
                ["label"] = node.Label ?? string.Empty,
                ["kind"] = node.Kind ?? string.Empty,
                ["zone"] = node.Zone ?? string.Empty,
                ["description"] = node.Description ?? string.Empty,
                ["resourceKind"] = node.ResourceKind ?? string.Empty,
                ["resourceRef"] = node.ResourceRef ?? string.Empty,
                ["reviewState"] = node.ReviewState ?? string.Empty,
                ["confidence"] = node.Confidence,
                ["stateBindings"] = new JArray((node.StateBindings
                    ?? new List<EquipmentTopologyStateBinding>())
                    .Where(item => item != null)
                    .Select(BuildStateBindingContext)),
                ["skills"] = new JArray((node.Skills
                    ?? new List<EquipmentTopologySkillBinding>())
                    .Where(item => item != null)
                    .Select(BuildSkillContext)),
                ["currentState"] = state == null ? null : new JObject
                {
                    ["name"] = state.StateName ?? string.Empty,
                    ["meaning"] = state.Meaning ?? string.Empty,
                    ["quality"] = state.Quality ?? EquipmentStateQualities.Unknown,
                    ["confidence"] = state.Confidence,
                    ["sequence"] = state.Sequence,
                    ["updatedAtUtc"] = state.UpdatedAtUtc.ToString("O")
                },
                ["perception"] = perception == null ? null : new JObject
                {
                    ["quality"] = perception.Quality ?? EquipmentStateQualities.Unknown,
                    ["stateChangedAtUtc"] = perception.StateChangedAtUtc.ToString("O"),
                    ["lastSuccessfulObservationAtUtc"] =
                        perception.LastSuccessfulObservationAtUtc.ToString("O")
                }
            };
        }

        private static JObject BuildStateBindingContext(EquipmentTopologyStateBinding binding)
        {
            return new JObject
            {
                ["bindingId"] = binding.Id ?? string.Empty,
                ["stateName"] = binding.StateName ?? string.Empty,
                ["sourceKind"] = binding.SourceKind ?? string.Empty,
                ["resourceRef"] = binding.ResourceRef ?? string.Empty,
                ["operator"] = binding.Operator ?? string.Empty,
                ["expectedValue"] = binding.ExpectedValue ?? string.Empty,
                ["meaning"] = binding.Meaning ?? string.Empty,
                ["priority"] = binding.Priority,
                ["reviewState"] = binding.ReviewState ?? string.Empty,
                ["confidence"] = binding.Confidence,
                ["evidence"] = BuildEvidenceContext(binding.Evidence)
            };
        }

        private static JObject BuildSkillContext(EquipmentTopologySkillBinding skill)
        {
            return new JObject
            {
                ["skillId"] = skill.Id ?? string.Empty,
                ["name"] = skill.Name ?? string.Empty,
                ["description"] = skill.Description ?? string.Empty,
                ["actionKind"] = skill.ActionKind ?? string.Empty,
                ["processId"] = skill.ProcessId ?? string.Empty,
                ["operationId"] = skill.OperationId ?? string.Empty,
                ["executionMode"] = skill.ExecutionMode ?? string.Empty,
                ["objective"] = skill.Objective ?? string.Empty,
                ["expectedOutcome"] = skill.ExpectedOutcome ?? string.Empty,
                ["preconditions"] = new JArray(skill.Preconditions ?? new List<string>()),
                ["reviewState"] = skill.ReviewState ?? string.Empty,
                ["confidence"] = skill.Confidence,
                ["evidence"] = BuildEvidenceContext(skill.Evidence)
            };
        }

        private static JArray BuildEvidenceContext(IEnumerable<EquipmentTopologyEvidence> evidence)
        {
            return new JArray((evidence ?? Enumerable.Empty<EquipmentTopologyEvidence>())
                .Where(item => item != null)
                .Select(ToCamelCaseObject));
        }

        private static JObject BuildWindow(int offset, int limit, int returned, int total)
        {
            return new JObject
            {
                ["offset"] = offset,
                ["limit"] = limit,
                ["returned"] = returned,
                ["total"] = total,
                ["hasMore"] = offset + returned < total
            };
        }

        private static JArray BuildRelationContext(
            IEnumerable<EquipmentTopologyRelation> relations,
            int offset,
            int limit)
        {
            return new JArray((relations ?? Enumerable.Empty<EquipmentTopologyRelation>())
                .Skip(offset)
                .Take(limit)
                .Select(item => new JObject
                {
                    ["relationId"] = item.Id ?? string.Empty,
                    ["sourceNodeId"] = item.SourceNodeId ?? string.Empty,
                    ["targetNodeId"] = item.TargetNodeId ?? string.Empty,
                    ["layer"] = item.Layer ?? string.Empty,
                    ["kind"] = item.Kind ?? string.Empty,
                    ["label"] = item.Label ?? string.Empty,
                    ["condition"] = item.Condition ?? string.Empty,
                    ["reviewState"] = item.ReviewState ?? string.Empty,
                    ["confidence"] = item.Confidence
                }));
        }

        private static JArray BuildEventArray(IEnumerable<EquipmentStateHistoryEvent> events, int limit)
        {
            return new JArray((events ?? Enumerable.Empty<EquipmentStateHistoryEvent>())
                .Where(item => item != null)
                .OrderByDescending(item => item.Sequence)
                .Take(limit)
                .Select(ToCamelCaseObject));
        }

        private static JArray BuildChronologicalEventArray(
            IEnumerable<EquipmentStateHistoryEvent> events)
        {
            return new JArray((events ?? Enumerable.Empty<EquipmentStateHistoryEvent>())
                .Where(item => item != null)
                .OrderBy(item => item.Sequence)
                .Select(ToCamelCaseObject));
        }

        private static JObject ToCamelCaseObject(object value)
        {
            return JObject.FromObject(
                value,
                JsonSerializer.Create(CamelCaseJsonSettings));
        }

        private static string BuildRelevantStateFingerprint(
            EquipmentStateSnapshot snapshot,
            IEnumerable<string> nodeIds)
        {
            Dictionary<string, EquipmentNodeStateProjection> states =
                (snapshot?.NodeStates ?? new List<EquipmentNodeStateProjection>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                .GroupBy(item => item.NodeId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Sequence).First(),
                    StringComparer.Ordinal);
            return string.Join("|", (nodeIds ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .Select(nodeId => states.TryGetValue(nodeId, out EquipmentNodeStateProjection state)
                    ? nodeId + ":" + state.Sequence + ":" + (state.StateName ?? string.Empty)
                        + ":" + (state.Quality ?? string.Empty) + ":" + state.UpdatedAtUtc.Ticks
                    : nodeId + ":<missing>"));
        }

        private void RemoveExpiredPreviewsLocked(DateTime now)
        {
            foreach (string previewId in previews.Values
                .Where(item => item.ExpiresAtUtc <= now)
                .Select(item => item.PreviewId).ToList())
            {
                previews.Remove(previewId);
            }
            foreach (string previewId in stopPreviews.Values
                .Where(item => item.ExpiresAtUtc <= now)
                .Select(item => item.PreviewId).ToList())
            {
                stopPreviews.Remove(previewId);
            }
        }

        private static void WriteAudit(string eventName, JObject payload)
        {
            try
            {
                StructuredAuditLogger.Write("MachineAgent", new JObject
                {
                    ["source"] = "machine_agent",
                    ["eventName"] = eventName,
                    ["payload"] = payload?.DeepClone() ?? new JObject()
                });
            }
            catch
            {
                // 审计写入故障不伪造执行失败；主动作仍由流程日志与状态历史记录。
            }
        }

        private sealed class LocatedOperation
        {
            public Proc Process { get; set; }
            public Step Step { get; set; }
            public OperationType Operation { get; set; }
            public int ProcIndex { get; set; }
            public int StepIndex { get; set; }
            public int OpIndex { get; set; }
        }

        private sealed class MatchedTopologyNode
        {
            public EquipmentTopologyNode Node { get; set; }
            public EquipmentNodeStateProjection State { get; set; }
            public EquipmentNodePerceptionState PerceptionState { get; set; }
            public IReadOnlyList<string> ParameterPaths { get; set; }
            public IReadOnlyList<EquipmentTopologySkillBinding> Skills { get; set; }
            public bool HasConfirmedEvidence { get; set; }
        }

        private sealed class ResolvedEntryRequest
        {
            public Guid ProcId { get; set; }
            public Guid OperationId { get; set; }
            public string Mode { get; set; }
            public string Objective { get; set; }
            public string ExpectedOutcome { get; set; }
            public EquipmentTopologyNode SkillNode { get; set; }
            public EquipmentTopologySkillBinding Skill { get; set; }
        }

        private sealed class ResolvedSkillBinding
        {
            public EquipmentTopologyNode Node { get; set; }
            public EquipmentTopologySkillBinding Skill { get; set; }
        }

        private sealed class ConditionEvaluationContext
        {
            public string CurrentNodeId { get; set; }
            public Guid TargetProcessId { get; set; }
            public DateTime EvaluationTimeUtc { get; set; }
            public IReadOnlyDictionary<string, EquipmentNodeStateProjection> NodeStates { get; set; }
            public IReadOnlyDictionary<string, EquipmentNodePerceptionState> PerceptionStates { get; set; }
            public IReadOnlyDictionary<Guid, EngineSnapshot> ProcessSnapshots { get; set; }
            public bool SafetyLocked { get; set; }
            public HashSet<string> TopologyNodeIds { get; set; }
        }

        private sealed class ConditionEvaluation
        {
            public string Expression { get; private set; }
            public bool Evaluable { get; private set; }
            public bool Satisfied { get; private set; }
            public string Detail { get; private set; }

            public static ConditionEvaluation Known(string expression, bool satisfied, string detail)
            {
                return new ConditionEvaluation
                {
                    Expression = expression ?? string.Empty,
                    Evaluable = true,
                    Satisfied = satisfied,
                    Detail = detail ?? string.Empty
                };
            }

            public static ConditionEvaluation Unknown(string expression, string detail)
            {
                return new ConditionEvaluation
                {
                    Expression = expression ?? string.Empty,
                    Evaluable = false,
                    Satisfied = false,
                    Detail = detail ?? string.Empty
                };
            }
        }

        private sealed class RelationEvaluation
        {
            public EquipmentTopologyRelation Relation { get; set; }
            public ConditionEvaluation Condition { get; set; }
            public bool BlocksExecution { get; set; }
        }

        private enum MachineOperationEffect
        {
            None,
            External,
            Forbidden
        }

        private sealed class FrozenEntryPreview
        {
            public string PreviewId { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
            public Guid ProcId { get; set; }
            public Guid OperationId { get; set; }
            public int ProcIndex { get; set; }
            public int StepIndex { get; set; }
            public int OpIndex { get; set; }
            public string Mode { get; set; }
            public string SkillId { get; set; }
            public string SkillNodeId { get; set; }
            public string Objective { get; set; }
            public string ExpectedOutcome { get; set; }
            public bool RequiresMachineEvidence { get; set; }
            public long ProcessRevision { get; set; }
            public long PublishedRevision { get; set; }
            public long AppliedRevision { get; set; }
            public long TopologyRevision { get; set; }
            public long StateSequence { get; set; }
            public bool RequireGlobalStateSequence { get; set; }
            public IReadOnlyList<string> RelevantNodeIds { get; set; }
            public string RelevantStateFingerprint { get; set; }
            public bool Executable { get; set; }
        }

        private sealed class FrozenStopPreview
        {
            public string PreviewId { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
            public Guid ProcId { get; set; }
            public int ProcIndex { get; set; }
            public Guid RunId { get; set; }
            public string Reason { get; set; }
            public bool Executable { get; set; }
        }
    }
}
