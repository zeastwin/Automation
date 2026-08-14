using Automation.Protocol;
// 模块：运行时 / AI 集成。
// 职责范围：管理 AI 会话、配置、ACP/MCP 进程、受管运行环境和分析记录。

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Automation
{
    public enum AiStandardTestSetupKind
    {
        EmptyOwnedObjects,
        ProductBaseline,
        ProductDuplicateRelease,
        ProductLargeInspection
    }

    public sealed class AiStandardTestScenario
    {
        public AiStandardTestScenario(
            string id,
            string name,
            string description,
            AiStandardTestSetupKind setupKind,
            params string[] prompts)
        {
            Id = id;
            Name = name;
            Description = description;
            SetupKind = setupKind;
            Prompts = prompts;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public AiStandardTestSetupKind SetupKind { get; }
        public IReadOnlyList<string> Prompts { get; }

        internal AiStandardTestScenario WithPrompts(IEnumerable<string> prompts)
        {
            return new AiStandardTestScenario(
                Id,
                Name,
                Description,
                SetupKind,
                (prompts ?? Enumerable.Empty<string>()).ToArray());
        }
    }

    public sealed class AiStandardTestPromptSet
    {
        public string Id { get; set; }

        public List<string> Prompts { get; set; }
    }

    public sealed class AiStandardTestFixtureState
    {
        public string ScenarioId { get; internal set; }
        public string SelectedProcessName { get; internal set; }
        public int InitialOperationCount { get; internal set; }
        public IReadOnlyList<Guid> InitialOperationIds { get; internal set; }
    }

    public sealed class AiStandardTestEvaluation
    {
        private readonly List<string> details = new List<string>();

        public bool Passed { get; private set; } = true;

        public IReadOnlyList<string> Details => details;

        public void Check(bool passed, string success, string failure)
        {
            Passed &= passed;
            details.Add((passed ? "✓ " + success : "✗ " + failure));
        }

        public string ToMarkdown(string scenarioName)
        {
            return $"### {(Passed ? "通过" : "未通过")} · {scenarioName}\n\n"
                + string.Join("\n", details.Select(item => "- " + item));
        }
    }

    public static class AiStandardTestSuite
    {
        private const int PromptConfigVersion = 1;
        private const string PromptConfigFileName = "AiStandardTests.json";
        internal const int MaximumPromptCount = 12;
        internal const int MaximumPromptLength = 4000;
        internal const int MaximumScenarioPromptLength = 20000;
        private static readonly object promptConfigLock = new object();
        private static IReadOnlyList<AiStandardTestScenario> configuredScenarioCache;
        private static string configuredScenarioCacheError;
        private const string OwnedPrefix = "标准测试_";
        private const string ProductProcessName = OwnedPrefix + "产品检测流程";
        private const string ResultVariableName = OwnedPrefix + "检测结果";
        private const string RetryVariableName = OwnedPrefix + "重试次数";

        private static readonly IReadOnlyList<AiStandardTestScenario> scenarios =
            new List<AiStandardTestScenario>
            {
                new AiStandardTestScenario(
                    "build",
                    "从零构建中型流程",
                    "验证规划、资源发现、分阶段提交和不确定信息处理。",
                    AiStandardTestSetupKind.EmptyOwnedObjects,
                    "创建“标准测试_产品检测流程”：扫码获取产品编号，读取检测结果，合格则计数并放行，不合格则记录报警并等待人工处理；检测等待时间为3秒。先完成可确定的部分，缺失资源保持未决或使用明确的配置占位，不要虚构资源。"),
                new AiStandardTestScenario(
                    "multi_process",
                    "多流程协作",
                    "验证跨流程引用、共享变量和任务拆分。",
                    AiStandardTestSetupKind.EmptyOwnedObjects,
                    "创建“标准测试_上料流程”“标准测试_检测流程”“标准测试_分拣流程”三个相互配合的流程，共用“标准测试_当前产品编号”和“标准测试_检测结果”变量。先设计协作关系，再逐步完成配置并检查引用。"),
                new AiStandardTestScenario(
                    "focused_edit",
                    "精确局部修改",
                    "验证最小改动能力，避免误改指令顺序和跳转。",
                    AiStandardTestSetupKind.ProductBaseline,
                    "把当前选中流程中的“等待检测结果”延时从3秒改为5秒，并紧接其后增加一个固定文本报警弹框；不要改变其他指令、顺序和跳转关系。"),
                new AiStandardTestScenario(
                    "diagnose_fix",
                    "控制流诊断与修复",
                    "先诊断、后修复，验证远距离跳转和稳定对象身份。",
                    AiStandardTestSetupKind.ProductDuplicateRelease,
                    "当前流程偶尔会重复执行放行指令，请检查跳转和条件分支，定位能够由现有配置证明的原因。先报告问题，暂时不要修改。",
                    "按照刚才已经证实的结论修复，并回读验证所有跳转目标。"),
                new AiStandardTestScenario(
                    "iterative_refinement",
                    "多轮持续精修",
                    "验证同一会话中的事实复用和连续修改能力。",
                    AiStandardTestSetupKind.ProductBaseline,
                    "给当前流程增加失败重试，使用变量“标准测试_重试次数”；首次失败后最多再重试3次，也就是最多检测4次。",
                    "重试期间不要重复记录报警，只在重试耗尽后记录一次。",
                    "再增加人工跳过路径；人工跳过时先清理本流程已经置位的报警状态，再进入放行后的结束路径。",
                    "检查前面所有改动是否互相冲突，并用结构回读验证。"),
                new AiStandardTestScenario(
                    "large_process",
                    "大体量流程检查",
                    "验证按需读取、长流程分析和分阶段修复。",
                    AiStandardTestSetupKind.ProductLargeInspection,
                    "检查当前流程中的变量写入、报警和全部跳转关系，列出能够由配置直接证明的问题；只修复确定存在的问题，允许分阶段提交，完成后回读验证。")
            };

        public static IReadOnlyList<AiStandardTestScenario> Scenarios => scenarios;

        public static IReadOnlyList<AiStandardTestScenario> GetConfiguredScenarios(out string error)
        {
            lock (promptConfigLock)
            {
                if (configuredScenarioCache != null)
                {
                    error = configuredScenarioCacheError;
                    return configuredScenarioCache;
                }
                error = null;
                string path = AutomationRuntimeOptions.ActiveConfigFile(PromptConfigFileName);
                if (!File.Exists(path))
                {
                    configuredScenarioCache = scenarios;
                    return configuredScenarioCache;
                }
                try
                {
                    JObject root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                    if (root["version"]?.Value<int?>() != PromptConfigVersion
                        || root["scenarios"] is not JArray configured)
                    {
                        throw new InvalidOperationException("自定义标准测试配置结构无效。");
                    }
                    if (configured.Count == 0)
                    {
                        configuredScenarioCache = scenarios;
                        return configuredScenarioCache;
                    }
                    List<AiStandardTestPromptSet> promptSets = configured
                        .OfType<JObject>()
                        .Select(item => new AiStandardTestPromptSet
                        {
                            Id = item["id"]?.Value<string>(),
                            Prompts = (item["prompts"] as JArray)?.Values<string>().ToList()
                        }).ToList();
                    if (promptSets.Count != configured.Count)
                        throw new InvalidOperationException("自定义标准测试场景必须是对象。");
                    if (!TryNormalizePromptSets(
                        promptSets, false, out Dictionary<string, string[]> normalized, out error))
                    {
                        configuredScenarioCache = scenarios;
                        configuredScenarioCacheError = error;
                        return configuredScenarioCache;
                    }
                    configuredScenarioCache = scenarios.Select(item =>
                        normalized.TryGetValue(item.Id, out string[] prompts)
                            ? item.WithPrompts(prompts)
                            : item).ToList();
                    return configuredScenarioCache;
                }
                catch (Exception ex)
                {
                    error = "读取自定义标准测试语句失败，已使用内置默认值：" + ex.Message;
                    configuredScenarioCache = scenarios;
                    configuredScenarioCacheError = error;
                    return configuredScenarioCache;
                }
            }
        }

        public static List<AiStandardTestScenario> Select(
            IEnumerable<AiStandardTestPromptSet> requested,
            out string error)
        {
            if (!TryNormalizePromptSets(requested, false, out Dictionary<string, string[]> normalized, out error))
                return new List<AiStandardTestScenario>();
            return scenarios.Where(item => normalized.ContainsKey(item.Id))
                .Select(item => item.WithPrompts(normalized[item.Id])).ToList();
        }

        public static bool TrySavePromptOverrides(
            IEnumerable<AiStandardTestPromptSet> requested,
            out string error)
        {
            if (!TryNormalizePromptSets(requested, true, out Dictionary<string, string[]> normalized, out error))
                return false;
            var overrides = new JArray();
            foreach (AiStandardTestScenario scenario in scenarios)
            {
                string[] prompts = normalized[scenario.Id];
                if (prompts.SequenceEqual(scenario.Prompts, StringComparer.Ordinal)) continue;
                overrides.Add(new JObject
                {
                    ["id"] = scenario.Id,
                    ["prompts"] = new JArray(prompts)
                });
            }
            return TryWritePromptConfig(overrides, out error);
        }

        public static bool TryResetPromptOverrides(out string error)
        {
            return TryWritePromptConfig(new JArray(), out error);
        }

        private static bool TryNormalizePromptSets(
            IEnumerable<AiStandardTestPromptSet> requested,
            bool requireAllScenarios,
            out Dictionary<string, string[]> normalized,
            out string error)
        {
            normalized = new Dictionary<string, string[]>(StringComparer.Ordinal);
            error = null;
            var knownIds = new HashSet<string>(scenarios.Select(item => item.Id), StringComparer.Ordinal);
            foreach (AiStandardTestPromptSet promptSet in requested ?? Enumerable.Empty<AiStandardTestPromptSet>())
            {
                string id = promptSet?.Id?.Trim() ?? string.Empty;
                if (!knownIds.Contains(id))
                {
                    error = "未知标准测试场景：" + (id.Length == 0 ? "<空>" : id);
                    return false;
                }
                if (normalized.ContainsKey(id))
                {
                    error = "标准测试场景重复：" + id;
                    return false;
                }
                List<string> prompts = promptSet.Prompts ?? new List<string>();
                if (prompts.Count < 1 || prompts.Count > MaximumPromptCount)
                {
                    error = $"标准测试[{id}]语句轮数必须在1..{MaximumPromptCount}。";
                    return false;
                }
                string[] values = prompts.Select(value => (value ?? string.Empty).Trim()).ToArray();
                int invalidIndex = Array.FindIndex(values,
                    value => value.Length == 0 || value.Length > MaximumPromptLength);
                if (invalidIndex >= 0)
                {
                    error = $"标准测试[{id}]第{invalidIndex + 1}轮语句必须在1..{MaximumPromptLength}个字符。";
                    return false;
                }
                if (values.Sum(value => value.Length) > MaximumScenarioPromptLength)
                {
                    error = $"标准测试[{id}]全部语句合计不能超过{MaximumScenarioPromptLength}个字符。";
                    return false;
                }
                normalized.Add(id, values);
            }
            if (normalized.Count == 0)
            {
                error = "请至少选择一个标准测试场景。";
                return false;
            }
            if (requireAllScenarios && normalized.Count != scenarios.Count)
            {
                error = "保存自定义语句时必须包含全部标准测试场景。";
                return false;
            }
            return true;
        }

        private static bool TryWritePromptConfig(JArray configured, out string error)
        {
            error = null;
            try
            {
                string path = AutomationRuntimeOptions.ActiveConfigFile(PromptConfigFileName);
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    error = "标准测试配置路径无效：" + path;
                    return false;
                }
                Directory.CreateDirectory(directory);
                var root = new JObject
                {
                    ["version"] = PromptConfigVersion,
                    ["scenarios"] = configured ?? new JArray()
                };
                File.WriteAllText(path, root.ToString(Formatting.Indented), new UTF8Encoding(false));
                lock (promptConfigLock)
                {
                    configuredScenarioCache = null;
                    configuredScenarioCacheError = null;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "保存自定义标准测试语句失败：" + ex.Message;
                return false;
            }
        }

        public static bool Prepare(
            PlatformRuntime runtime,
            AiStandardTestScenario scenario,
            out AiStandardTestFixtureState state,
            out string error)
        {
            state = null;
            error = null;
            if (runtime == null)
            {
                error = "平台运行时为空。";
                return false;
            }
            if (scenario == null)
            {
                error = "测试场景为空。";
                return false;
            }
            if (runtime.EditorUi?.IsReady != true)
            {
                error = "流程编辑器或变量服务尚未初始化。";
                return false;
            }
            for (int index = 0; index < runtime.Stores.Processes.Items.Count; index++)
            {
                EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(index);
                if (snapshot != null && !snapshot.State.IsInactive())
                {
                    error = $"流程[{runtime.Stores.Processes.Items[index]?.head?.Name ?? index.ToString()}]仍在运行，标准测试准备未修改配置。";
                    return false;
                }
            }

            try
            {
                List<Proc> processes = runtime.Stores.Processes.Items
                    .Where(proc => !IsOwnedName(proc?.head?.Name))
                    .Select(ObjectGraphCloner.Clone).ToList();
                Dictionary<string, DicValue> variables = runtime.Stores.Values.BuildSaveData()
                    .Where(item => !IsOwnedName(item.Key))
                    .ToDictionary(item => item.Key, item => ObjectGraphCloner.Clone(item.Value), StringComparer.Ordinal);

                if (scenario.SetupKind != AiStandardTestSetupKind.EmptyOwnedObjects)
                {
                    AiChangeSetCompileResult fixture = AiChangeSetCompiler.Compile(
                        runtime, BuildProductFixture(scenario.SetupKind), processes, variables);
                    processes = fixture.Processes;
                    variables = fixture.Variables;
                }

                if (!ProcessVariableConfigurationTransaction.Commit(
                    runtime.Paths.ConfigPath, processes, variables, out string commitError, out bool rollbackFailed))
                {
                    if (rollbackFailed) runtime.Safety.Lock(commitError);
                    error = commitError;
                    return false;
                }

                runtime.Stores.Values.ReplaceConfiguration(variables);
                runtime.EditorUi.RefreshProcesses();
                runtime.EditorUi.RefreshVariables();

                string selectedName = scenario.SetupKind == AiStandardTestSetupKind.EmptyOwnedObjects
                    ? null
                    : ProductProcessName;
                if (!string.IsNullOrWhiteSpace(selectedName))
                {
                    int procIndex = runtime.Stores.Processes.Items.FindIndex(proc =>
                        string.Equals(proc?.head?.Name, selectedName, StringComparison.Ordinal));
                    if (procIndex < 0)
                    {
                        error = $"测试夹具流程未创建：{selectedName}";
                        return false;
                    }
                    runtime.EditorUi.SelectProcessContext(procIndex, 0);
                }
                else
                {
                    runtime.EditorUi.SelectProcessContext(-1, -1);
                }

                Proc selectedProcess = FindProcess(runtime, selectedName);
                state = new AiStandardTestFixtureState
                {
                    ScenarioId = scenario.Id,
                    SelectedProcessName = selectedName,
                    InitialOperationCount = CountOperations(selectedProcess),
                    InitialOperationIds = selectedProcess?.steps?
                        .SelectMany(step => step?.Ops ?? new List<OperationType>())
                        .Where(operation => operation != null)
                        .Select(operation => operation.Id).ToList() ?? new List<Guid>()
                };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static AiStandardTestEvaluation Evaluate(
            PlatformRuntime runtime,
            AiStandardTestScenario scenario,
            AiStandardTestFixtureState fixture)
        {
            var result = new AiStandardTestEvaluation();
            if (scenario == null || fixture == null)
            {
                result.Check(false, string.Empty, "测试上下文缺失，无法判定结果。");
                return result;
            }

            switch (scenario.Id)
            {
                case "build":
                {
                    Proc proc = FindProcess(runtime, ProductProcessName);
                    result.Check(proc != null, "目标流程已创建", $"未找到流程“{ProductProcessName}”。");
                    result.Check(CountOperations(proc) >= 4, "流程包含可审查的业务结构", "流程指令不足4条，未形成可审查结构。");
                    List<OperationType> operations = proc?.steps?
                        .SelectMany(step => step?.Ops ?? new List<OperationType>())
                        .Where(operation => operation != null).ToList()
                        ?? new List<OperationType>();
                    bool hasExplicitPlaceholder = operations.Any(operation =>
                        operation.Note?.StartsWith(
                            ProcessReadinessService.PlaceholderNotePrefix,
                            StringComparison.Ordinal) == true);
                    result.Check(
                        hasExplicitPlaceholder,
                        "缺失资源使用显式配置占位",
                        "缺失的扫码、放行或报警资源没有使用config.placeholder，可能被伪动作包装。");
                    int procIndex = proc == null ? -1 : runtime.Stores.Processes.Items.IndexOf(proc);
                    ProcessReadinessAnalysis readiness = procIndex < 0
                        ? null
                        : ProcessReadinessService.Analyze(
                            procIndex,
                            proc,
                            runtime.Stores.Processes.Items,
                            runtime.CreateProcessValidationContext(),
                            runtime.Stores.Values);
                    result.Check(
                        readiness?.ReadinessStatus == "incomplete" && readiness.Runnable == false,
                        "占位流程可保存但不可启动",
                        "缺失资源的占位流程被错误提升为ready/runnable。");
                    CheckGotoStructure(runtime, result, proc);
                    break;
                }
                case "multi_process":
                {
                    foreach (string suffix in new[] { "上料流程", "检测流程", "分拣流程" })
                    {
                        string name = OwnedPrefix + suffix;
                        result.Check(FindProcess(runtime, name) != null, $"已创建{name}", $"未找到流程“{name}”。");
                    }
                    Dictionary<string, DicValue> variables = runtime.Stores.Values.BuildSaveData();
                    foreach (string name in new[] { OwnedPrefix + "当前产品编号", ResultVariableName })
                    {
                        result.Check(variables.ContainsKey(name), $"已声明共享变量{name}", $"未找到共享变量“{name}”。");
                    }
                    break;
                }
                case "focused_edit":
                {
                    Proc proc = FindProcess(runtime, ProductProcessName);
                    List<OperationType> operations = proc?.steps?
                        .SelectMany(step => step.Ops ?? new List<OperationType>()).ToList()
                        ?? new List<OperationType>();
                    Delay delay = operations
                        .OfType<Delay>().FirstOrDefault(op => string.Equals(op.Name, "等待检测结果", StringComparison.Ordinal));
                    result.Check(delay?.DelayMs == 5000, "目标延时已精确改为5000ms", "目标延时没有变为5000ms。");
                    int delayIndex = delay == null ? -1 : operations.IndexOf(delay);
                    result.Check(delayIndex >= 0 && delayIndex + 1 < operations.Count
                        && operations[delayIndex + 1] is PopupDialog,
                        "固定文本报警弹框紧接目标延时",
                        "目标延时后没有紧接固定文本弹框。");
                    result.Check(operations.Count == fixture.InitialOperationCount + 1,
                        "只增加了一条指令", "本次局部修改改变了非预期的指令数量。");
                    List<Guid> retainedIds = operations
                        .Where(operation => fixture.InitialOperationIds.Contains(operation.Id))
                        .Select(operation => operation.Id).ToList();
                    result.Check(retainedIds.SequenceEqual(fixture.InitialOperationIds),
                        "既有指令身份与相对顺序保持不变",
                        "既有指令被替换、删除或改变了相对顺序。");
                    CheckGotoStructure(runtime, result, proc);
                    break;
                }
                case "diagnose_fix":
                {
                    Proc proc = FindProcess(runtime, ProductProcessName);
                    result.Check(proc != null, "诊断夹具仍存在", "诊断流程不存在。");
                    result.Check(!HasReleaseSelfLoop(runtime, proc), "放行自循环已消除", "放行后的跳转仍返回放行指令。");
                    CheckGotoStructure(runtime, result, proc);
                    break;
                }
                case "iterative_refinement":
                {
                    Proc proc = FindProcess(runtime, ProductProcessName);
                    Dictionary<string, DicValue> variables = runtime.Stores.Values.BuildSaveData();
                    result.Check(variables.ContainsKey(RetryVariableName), "重试变量已声明", $"未找到变量“{RetryVariableName}”。");
                    result.Check(CountOperations(proc) > fixture.InitialOperationCount, "流程包含新增的重试与人工路径", "流程结构没有新增指令。");
                    CheckGotoStructure(runtime, result, proc);
                    break;
                }
                case "large_process":
                {
                    Proc proc = FindProcess(runtime, ProductProcessName);
                    result.Check(proc != null, "检查目标仍存在", "检查目标流程不存在。");
                    result.Check(!HasReleaseSelfLoop(runtime, proc), "已处理能够证明的放行自循环", "放行后的自循环仍然存在。");
                    CheckGotoStructure(runtime, result, proc);
                    break;
                }
                default:
                    result.Check(false, string.Empty, $"未知测试场景：{scenario.Id}");
                    break;
            }
            return result;
        }

        private static AiChangeSet BuildProductFixture(AiStandardTestSetupKind setupKind)
        {
            var actions = new List<ChangeSetAction>
            {
                new ChangeSetAction
                {
                    Type = "process.create",
                    Process = new ProcessActionValue { Key = "product", Name = ProductProcessName }
                },
                new ChangeSetAction
                {
                    Type = "step.append",
                    TargetProcess = new ProcessSelector { Key = "product" },
                    Step = new StepActionValue { Key = "main", Name = "检测与放行" }
                },
                Append("main", new SemanticOperation
                {
                    Key = "wait_result", Kind = "wait", Name = "等待检测结果", Milliseconds = 3000
                }),
                Append("main", new SemanticOperation
                {
                    Key = "judge_result",
                    Kind = "branch.number_compare",
                    Name = "判断检测结果",
                    Variable = ResultVariableName,
                    Comparison = "gte",
                    CompareValue = 1,
                    WhenTrue = new OperationTarget { OperationKey = "release" },
                    WhenFalse = new OperationTarget { OperationKey = "alarm" }
                }),
                Append("main", new SemanticOperation
                {
                    Key = "release", Kind = "wait", Name = "放行", Milliseconds = 10
                })
            };

            if (setupKind == AiStandardTestSetupKind.ProductDuplicateRelease
                || setupKind == AiStandardTestSetupKind.ProductLargeInspection)
            {
                actions.Add(Append("main", new SemanticOperation
                {
                    Key = "after_release", Kind = "flow.goto", Name = "放行后跳转",
                    Target = new OperationTarget { OperationKey = "release" }
                }));
            }
            else
            {
                actions.Add(Append("main", new SemanticOperation
                {
                    Key = "after_release", Kind = "flow.end", Name = "放行结束"
                }));
            }

            actions.Add(Append("main", new SemanticOperation
            {
                Key = "alarm", Kind = "popup.message", Name = "检测失败报警",
                Message = "检测失败，请人工处理"
            }));
            actions.Add(Append("main", new SemanticOperation
            {
                Key = "failure_end", Kind = "flow.end", Name = "失败结束"
            }));

            if (setupKind == AiStandardTestSetupKind.ProductLargeInspection)
            {
                for (int index = 0; index < 54; index++)
                {
                    actions.Insert(3 + index, Append("main", new SemanticOperation
                    {
                        Key = "inspect_" + index,
                        Kind = "wait",
                        Name = "检查段" + (index + 1),
                        Milliseconds = 5
                    }));
                }
            }

            return new AiChangeSet
            {
                Version = 2,
                Title = "准备标准测试夹具",
                Variables = new List<VariableChange>
                {
                    new VariableChange
                    {
                        Name = ResultVariableName,
                        Scope = VariableScopeContract.Process,
                        OwnerProcess = new ProcessSelector { Key = "product" },
                        Type = "double",
                        Value = "0",
                        Policy = "reuse",
                        Note = "AI标准测试夹具"
                    }
                },
                Actions = actions
            };
        }

        private static ChangeSetAction Append(string stepKey, SemanticOperation operation)
        {
            return new ChangeSetAction
            {
                Type = "operation.append",
                TargetProcess = new ProcessSelector { Key = "product" },
                TargetStep = new StepSelector { Key = stepKey },
                Operation = operation
            };
        }

        private static Proc FindProcess(PlatformRuntime runtime, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return runtime.Stores.Processes.Items.FirstOrDefault(proc =>
                string.Equals(proc?.head?.Name, name, StringComparison.Ordinal));
        }

        private static int CountOperations(Proc proc)
        {
            return proc?.steps?.Sum(step => step?.Ops?.Count ?? 0) ?? 0;
        }

        private static bool IsOwnedName(string name)
        {
            return name?.StartsWith(OwnedPrefix, StringComparison.Ordinal) == true;
        }

        private static bool HasReleaseSelfLoop(PlatformRuntime runtime, Proc proc)
        {
            if (proc?.steps == null) return false;
            int procIndex = runtime.Stores.Processes.Items.IndexOf(proc);
            for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
            {
                List<OperationType> operations = proc.steps[stepIndex]?.Ops;
                if (operations == null) continue;
                int releaseIndex = operations.FindIndex(operation =>
                    string.Equals(operation?.Name, "放行", StringComparison.Ordinal));
                if (releaseIndex < 0) continue;
                string target = $"{procIndex}-{stepIndex}-{releaseIndex}";
                if (operations.OfType<Goto>().Any(operation =>
                    string.Equals(operation.DefaultGoto, target, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        private static void CheckGotoStructure(
            PlatformRuntime runtime,
            AiStandardTestEvaluation result,
            Proc proc)
        {
            if (proc == null)
            {
                result.Check(false, string.Empty, "流程不存在，无法校验跳转结构。");
                return;
            }
            int procIndex = runtime.Stores.Processes.Items.IndexOf(proc);
            List<string> errors = ProcessDefinitionService.ValidateProcGotoTargets(procIndex, proc);
            result.Check(errors.Count == 0, "跳转目标结构有效", "跳转校验失败：" + string.Join("；", errors));
        }
    }
}
