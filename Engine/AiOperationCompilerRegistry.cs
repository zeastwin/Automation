using Automation.Protocol;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using static Automation.OperationTypePartial;

namespace Automation
{
    public sealed class AiResourceSnapshot
    {
        public AiResourceSnapshot(
            IReadOnlyDictionary<string, string> ioTypes = null,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> references = null)
        {
            IoTypes = ioTypes ?? new Dictionary<string, string>(StringComparer.Ordinal);
            References = references ?? new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, string> IoTypes { get; }

        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> References { get; }
    }

    public interface IAiOperationCompiler
    {
        string Kind { get; }

        string DefaultName { get; }

        JObject BuildContract();

        OperationType Compile(SemanticOperation definition, AiOperationCompileContext context);
    }

    public sealed class AiOperationCompileContext
    {
        private readonly int _procIndex;
        private readonly IReadOnlyDictionary<string, int> _stepIndexes;
        private readonly IReadOnlyDictionary<string, int> _stepOperationCounts;
        private readonly IReadOnlyDictionary<Guid, OperationReferenceLocation> _operationIdLocations;
        private readonly IReadOnlyDictionary<string, OperationReferenceLocation> _operationKeyLocations;
        private readonly IReadOnlyDictionary<string, DicValue> _variables;
        private readonly AiResourceSnapshot _resources;

        public AiOperationCompileContext(
            int procIndex,
            IReadOnlyDictionary<string, int> stepIndexes,
            IReadOnlyDictionary<string, int> stepOperationCounts,
            IReadOnlyDictionary<string, DicValue> variables,
            AiResourceSnapshot resources,
            IReadOnlyDictionary<Guid, OperationReferenceLocation> operationIdLocations = null,
            IReadOnlyDictionary<string, OperationReferenceLocation> operationKeyLocations = null)
        {
            _procIndex = procIndex;
            _stepIndexes = stepIndexes ?? throw new ArgumentNullException(nameof(stepIndexes));
            _stepOperationCounts = stepOperationCounts ?? throw new ArgumentNullException(nameof(stepOperationCounts));
            _operationIdLocations = operationIdLocations
                ?? new Dictionary<Guid, OperationReferenceLocation>();
            _operationKeyLocations = operationKeyLocations
                ?? new Dictionary<string, OperationReferenceLocation>(StringComparer.Ordinal);
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _resources = resources ?? new AiResourceSnapshot();
        }

        public string RequireIo(string name, string path, bool outputOnly)
        {
            string exactName = RequireText(name, path);
            if (!_resources.IoTypes.TryGetValue(exactName, out string ioType))
            {
                throw new InvalidOperationException($"{path} 引用的IO不存在：{exactName}");
            }
            if (outputOnly && !string.Equals(ioType, "通用输出", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} 只能引用通用输出：{exactName} 当前类型为 {ioType}");
            }
            return exactName;
        }

        public DicValue RequireVariable(string name, string path, string requiredType = null)
        {
            string exactName = RequireText(name, path);
            if (!_variables.TryGetValue(exactName, out DicValue variable))
            {
                throw new InvalidOperationException($"{path} 引用的变量不存在：{exactName}。请在同一 changeSet.variables 中声明资源策略。");
            }
            if (!string.IsNullOrEmpty(requiredType)
                && !string.Equals(variable?.Type, requiredType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"变量[{exactName}]类型不匹配：现有 {variable?.Type ?? "空"}，要求 {requiredType}。");
            }
            return variable;
        }

        public void ValidateReference(string referenceType, object value, string path)
        {
            string text = value?.ToString();
            if (string.IsNullOrWhiteSpace(referenceType) || string.IsNullOrWhiteSpace(text)) return;
            if (string.Equals(referenceType, "value", StringComparison.Ordinal))
            {
                RequireVariable(text, path);
                return;
            }
            if (referenceType.StartsWith("io.", StringComparison.Ordinal))
            {
                string ioName = RequireIo(text, path,
                    string.Equals(referenceType, "io.output", StringComparison.Ordinal));
                if (string.Equals(referenceType, "io.input", StringComparison.Ordinal)
                    && _resources.IoTypes.TryGetValue(ioName, out string ioType)
                    && !string.Equals(ioType, "通用输入", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path} 只能引用通用输入：{ioName} 当前类型为 {ioType}");
                }
                return;
            }
            if (!_resources.References.TryGetValue(referenceType, out IReadOnlyCollection<string> candidates)) return;
            if (!candidates.Contains(text, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{path} 引用的{referenceType}资源不存在：{text}");
            }
        }

        public string ResolveTarget(OperationTarget target, string path)
        {
            if (target == null) throw new InvalidOperationException($"{path} 必填。");
            bool hasOperationId = !string.IsNullOrWhiteSpace(target.OperationId);
            bool hasOperationKey = !string.IsNullOrWhiteSpace(target.OperationKey);
            bool hasOperationIndex = target.Operation.HasValue;
            int selectorCount = (hasOperationId ? 1 : 0) + (hasOperationKey ? 1 : 0)
                + (hasOperationIndex ? 1 : 0);
            if (selectorCount != 1)
            {
                throw new InvalidOperationException(
                    $"{path} 必须且只能使用 operationId、operationKey 或 operation 定位目标。");
            }
            if (hasOperationId)
            {
                if (!Guid.TryParse(target.OperationId, out Guid operationId) || operationId == Guid.Empty)
                {
                    throw new InvalidOperationException($"{path}.operationId 不是有效 Guid。");
                }
                if (!_operationIdLocations.TryGetValue(operationId, out OperationReferenceLocation idLocation))
                {
                    throw new InvalidOperationException($"{path}.operationId 未在最终流程结构中找到：{operationId:D}");
                }
                return $"{_procIndex}-{idLocation.StepIndex}-{idLocation.OperationIndex}";
            }

            string stepKey = RequireText(target.Step, path + ".step");
            if (!_stepIndexes.TryGetValue(stepKey, out int stepIndex))
            {
                throw new InvalidOperationException($"{path}.step 未找到：{stepKey}");
            }
            if (hasOperationKey)
            {
                string operationKey = RequireText(target.OperationKey, path + ".operationKey");
                if (!_operationKeyLocations.TryGetValue(BuildOperationKey(stepKey, operationKey),
                    out OperationReferenceLocation keyLocation))
                {
                    throw new InvalidOperationException(
                        $"{path}.operationKey 未在步骤[{stepKey}]中找到：{operationKey}");
                }
                return $"{_procIndex}-{keyLocation.StepIndex}-{keyLocation.OperationIndex}";
            }
            int operationCount = _stepOperationCounts[stepKey];
            int operationIndex = target.Operation.Value;
            if (operationIndex < 0 || operationIndex >= operationCount)
            {
                throw new InvalidOperationException($"{path}.operation 超出步骤[{stepKey}]指令范围 [0,{operationCount})。");
            }
            return $"{_procIndex}-{stepIndex}-{operationIndex}";
        }

        public static string BuildOperationKey(string stepKey, string operationKey)
        {
            return stepKey + "\0" + operationKey;
        }

        public static string RequireText(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{path} 不能为空。");
            return value.Trim();
        }
    }

    public sealed class OperationReferenceLocation
    {
        public OperationReferenceLocation(int stepIndex, int operationIndex)
        {
            StepIndex = stepIndex;
            OperationIndex = operationIndex;
        }

        public int StepIndex { get; }

        public int OperationIndex { get; }
    }

    /// <summary>
    /// AI 语义指令编译器注册表。新增语义能力时注册独立适配器，禁止回到集中 switch。
    /// </summary>
    public static class AiOperationCompilerRegistry
    {
        private static readonly IReadOnlyDictionary<string, IAiOperationCompiler> Compilers
            = new IAiOperationCompiler[]
            {
                new VariableSetCompiler(),
                new VariableAddCompiler(),
                new WaitCompiler(),
                new FlowGotoCompiler(),
                new NumberRangeBranchCompiler(),
                new PopupMessageCompiler(),
                new PopupVariableCompiler(),
                new IoWriteCompiler(),
                new IoWaitCompiler(),
                new ProcessControlCompiler(),
                new ProcessWaitCompiler(),
                new NativeOperationCompiler()
            }.ToDictionary(item => item.Kind, StringComparer.Ordinal);

        public static IReadOnlyCollection<string> Kinds => Compilers.Keys.ToArray();

        public static IAiOperationCompiler Get(string kind)
        {
            if (!Compilers.TryGetValue(kind, out IAiOperationCompiler compiler))
            {
                throw new InvalidOperationException($"不支持的语义指令：{kind}");
            }
            return compiler;
        }

        public static JObject BuildCapabilities()
        {
            return new JObject
            {
                ["protocol"] = "change-set-v2",
                ["processActions"] = new JArray("create", "replace"),
                ["processDeletion"] = new JObject
                {
                    ["modes"] = new JArray("all", "selected"),
                    ["all"] = new JObject
                    {
                        ["selectorsAllowed"] = false,
                        ["meaning"] = "删除当前全部流程"
                    },
                    ["selected"] = new JObject
                    {
                        ["selectors"] = new JArray("names", "procIds"),
                        ["selectorMatch"] = "exact",
                        ["minimumSelectors"] = 1
                    }
                },
                ["variablePolicies"] = new JArray("reuse", "create", "update", "replace", "require"),
                ["operationKinds"] = new JArray(Compilers.Keys.OrderBy(value => value, StringComparer.Ordinal)),
                ["nativeOperation"] = new JObject
                {
                    ["kind"] = "native.operation",
                    ["contractTool"] = "get_native_operation_contract",
                    ["rule"] = "高层 kind 不覆盖目标指令时，按一个精确原生 operaType 读取递归契约"
                },
                ["workflow"] = new JArray(
                    "一次性构造包含全部步骤和指令的完整 changeSet",
                    "preview_change_set(changeSet)",
                    "等待 Automation 前台确认",
                    "apply_change_set(previewId)"),
                ["rule"] = "根据既有配置或精确规范重建时使用preserveOperationTypes=true并按原operaType追加native.operation；仅业务目标创建才使用高层语义kind。"
            };
        }

        public static JObject BuildContracts(IEnumerable<string> kinds)
        {
            string[] requested = (kinds ?? Enumerable.Empty<string>()).ToArray();
            if (requested.Length < 1 || requested.Length > 6)
            {
                throw new InvalidOperationException("kinds 数量必须在 1..6 之间。");
            }
            var contracts = new JObject();
            foreach (string kind in requested.Distinct(StringComparer.Ordinal))
            {
                IAiOperationCompiler compiler = Get(kind ?? string.Empty);
                contracts[kind] = compiler.BuildContract();
            }
            return new JObject { ["contracts"] = contracts };
        }

        private static JObject Contract(string purpose, string[] required, string[] optional, params JProperty[] extras)
        {
            var result = new JObject
            {
                ["purpose"] = purpose,
                ["required"] = new JArray(required),
                ["optional"] = new JArray(optional)
            };
            foreach (JProperty extra in extras ?? Array.Empty<JProperty>()) result.Add(extra);
            return result;
        }

        private sealed class NativeOperationCompiler : IAiOperationCompiler
        {
            public string Kind => "native.operation";

            public string DefaultName => "原生指令";

            public JObject BuildContract()
            {
                return Contract("按精确 operaType 和递归字段契约编译任意平台注册指令",
                    new[] { "kind", "operaType", "fields" }, new[] { "name" },
                    new JProperty("contractTool", "get_native_operation_contract"),
                    new JProperty("rule", "先按精确 operaType 读取契约；fields 禁止使用扁平化 PropertyGrid 键"));
            }

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                return StructuredOperationCompiler.Compile(definition.OperaType, definition.Fields, context);
            }
        }

        private abstract class VariableCompilerBase : IAiOperationCompiler
        {
            private readonly bool _add;

            protected VariableCompilerBase(bool add)
            {
                _add = add;
            }

            public abstract string Kind { get; }

            public abstract string DefaultName { get; }

            public JObject BuildContract()
            {
                return _add
                    ? Contract("对 double 变量累加固定数值", new[] { "kind", "variable", "amount" }, new[] { "name" })
                    : Contract("把变量设置为固定值", new[] { "kind", "variable", "value" }, new[] { "name" });
            }

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string path = _add ? "variable.add.variable" : "variable.set.variable";
                DicValue variable = context.RequireVariable(definition.Variable, path);
                string variableName = definition.Variable.Trim();
                string changeValue;
                if (_add)
                {
                    if (!string.Equals(variable.Type, "double", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"variable.add 只能用于 double 变量：{variableName}");
                    }
                    if (!definition.Amount.HasValue)
                    {
                        throw new InvalidOperationException("variable.add.amount 必填。");
                    }
                    changeValue = definition.Amount.Value.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    if (definition.Value == null)
                    {
                        throw new InvalidOperationException("variable.set.value 必填。");
                    }
                    changeValue = definition.Value;
                    if (string.Equals(variable.Type, "double", StringComparison.Ordinal)
                        && !double.TryParse(changeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                        && !double.TryParse(changeValue, out _))
                    {
                        throw new InvalidOperationException($"变量[{variableName}]是 double，variable.set.value 必须是有效数字文本。");
                    }
                }
                return new ModifyValue
                {
                    ModifyType = _add ? "叠加" : "替换",
                    ValueSourceName = variableName,
                    ChangeValue = changeValue,
                    OutputValueName = variableName
                };
            }
        }

        private sealed class VariableSetCompiler : VariableCompilerBase
        {
            public VariableSetCompiler() : base(false) { }
            public override string Kind => "variable.set";
            public override string DefaultName => "设置变量";
        }

        private sealed class VariableAddCompiler : VariableCompilerBase
        {
            public VariableAddCompiler() : base(true) { }
            public override string Kind => "variable.add";
            public override string DefaultName => "变量累加";
        }

        private sealed class WaitCompiler : IAiOperationCompiler
        {
            public string Kind => "wait";
            public string DefaultName => "等待";

            public JObject BuildContract() => Contract("等待固定毫秒数",
                new[] { "kind", "milliseconds" }, new[] { "name" }, new JProperty("range", "0..86400000"));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                if (!definition.Milliseconds.HasValue || definition.Milliseconds.Value < 0
                    || definition.Milliseconds.Value > 86400000)
                {
                    throw new InvalidOperationException("wait.milliseconds 必须在 0..86400000 之间。");
                }
                return new Delay
                {
                    timeMiniSecond = definition.Milliseconds.Value.ToString(CultureInfo.InvariantCulture)
                };
            }
        }

        private sealed class FlowGotoCompiler : IAiOperationCompiler
        {
            public string Kind => "flow.goto";
            public string DefaultName => "跳转";

            public JObject BuildContract() => Contract("无条件跳转到当前定义流程内的符号位置",
                new[] { "kind", "target" }, new[] { "name" },
                new JProperty("target", new JObject { ["step"] = "步骤key", ["operation"] = "步骤内从0开始的指令索引" }));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                return new Goto
                {
                    Count = "0",
                    Params = new CustomList<GotoParam>(),
                    DefaultGoto = context.ResolveTarget(definition.Target, "flow.goto.target")
                };
            }
        }

        private sealed class NumberRangeBranchCompiler : IAiOperationCompiler
        {
            public string Kind => "branch.number_range";
            public string DefaultName => "数值区间判断";

            public JObject BuildContract() => Contract("按 double 变量是否位于数值区间进行双分支跳转",
                new[] { "kind", "variable", "min", "max", "whenTrue", "whenFalse" },
                new[] { "name", "includeBounds" });

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                context.RequireVariable(definition.Variable, "branch.number_range.variable", "double");
                string variableName = definition.Variable.Trim();
                if (!definition.Min.HasValue || !definition.Max.HasValue || definition.Min.Value > definition.Max.Value)
                {
                    throw new InvalidOperationException("branch.number_range 必须提供 min/max，且 min 不得大于 max。");
                }
                return new ParamGoto
                {
                    Count = "1",
                    failDelay = "10",
                    goto1 = context.ResolveTarget(definition.WhenTrue, "branch.number_range.whenTrue"),
                    goto2 = context.ResolveTarget(definition.WhenFalse, "branch.number_range.whenFalse"),
                    Params = new CustomList<ParamGotoParam>
                    {
                        new ParamGotoParam
                        {
                            ValueName = variableName,
                            JudgeMode = "值在区间内",
                            Down = definition.Min.Value,
                            Up = definition.Max.Value,
                            equal = definition.IncludeBounds ?? true,
                            Operator = "且"
                        }
                    }
                };
            }
        }

        private sealed class PopupMessageCompiler : IAiOperationCompiler
        {
            public string Kind => "popup.message";
            public string DefaultName => "显示提示";

            public JObject BuildContract() => Contract("显示固定文本提示框",
                new[] { "kind", "message" }, new[] { "name", "buttonText", "autoCloseMs", "target" },
                new JProperty("messageSource", "literal"),
                new JProperty("interpolation", "unsupported"),
                new JProperty("runtimeBehavior", "message 会原样显示；{变量名}不是模板语法"),
                new JProperty("targetBehavior", "只有用户点击确定才跳转 target；自动关闭时顺序执行下一条"));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string message = AiOperationCompileContext.RequireText(definition.Message, "popup.message.message");
                if (ContainsPlaceholderSyntax(message))
                {
                    throw new InvalidOperationException(
                        "popup.message.message 是固定文本，不支持 {变量名} 插值。显示变量当前值请改用 popup.variable；该模式只显示变量值，指令 name 可作为弹框标题。");
                }
                if (definition.AutoCloseMs.HasValue
                    && (definition.AutoCloseMs.Value < 1 || definition.AutoCloseMs.Value > 3600000))
                {
                    throw new InvalidOperationException("popup.message.autoCloseMs 必须在 1..3600000 之间。");
                }
                var operation = new PopupDialog
                {
                    PopupType = "弹是",
                    InfoType = "自定义提示信息",
                    PopupMessage = message,
                    Btn1Text = string.IsNullOrWhiteSpace(definition.ButtonText) ? "确定" : definition.ButtonText.Trim(),
                    DelayClose = definition.AutoCloseMs.HasValue,
                    DelayCloseTimeMs = definition.AutoCloseMs ?? 0,
                    AlarmLightEnable = "禁用"
                };
                if (definition.Target != null)
                {
                    operation.PopupGoto1 = context.ResolveTarget(definition.Target, "popup.message.target");
                }
                return operation;
            }
        }

        private sealed class PopupVariableCompiler : IAiOperationCompiler
        {
            public string Kind => "popup.variable";
            public string DefaultName => "显示变量值";

            public JObject BuildContract() => Contract("显示一个变量的当前值",
                new[] { "kind", "variable" }, new[] { "name", "buttonText", "autoCloseMs", "target" },
                new JProperty("messageSource", "variable.currentValue"),
                new JProperty("supportedVariableTypes", new JArray("double", "string")),
                new JProperty("prefix", "unsupported"),
                new JProperty("runtimeBehavior", "弹框正文只显示变量当前值；name 是弹框标题"),
                new JProperty("targetBehavior", "只有用户点击确定才跳转 target；自动关闭时顺序执行下一条"));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                DicValue variable = context.RequireVariable(definition.Variable, "popup.variable.variable");
                if (!string.Equals(variable.Type, "double", StringComparison.Ordinal)
                    && !string.Equals(variable.Type, "string", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"popup.variable 只支持 double 或 string 变量：{definition.Variable}");
                }
                if (definition.AutoCloseMs.HasValue
                    && (definition.AutoCloseMs.Value < 1 || definition.AutoCloseMs.Value > 3600000))
                {
                    throw new InvalidOperationException("popup.variable.autoCloseMs 必须在 1..3600000 之间。");
                }
                var operation = new PopupDialog
                {
                    PopupType = "弹是",
                    InfoType = "变量类型",
                    PopupMessageValue = definition.Variable.Trim(),
                    Btn1Text = string.IsNullOrWhiteSpace(definition.ButtonText) ? "确定" : definition.ButtonText.Trim(),
                    DelayClose = definition.AutoCloseMs.HasValue,
                    DelayCloseTimeMs = definition.AutoCloseMs ?? 0,
                    AlarmLightEnable = "禁用"
                };
                if (definition.Target != null)
                {
                    operation.PopupGoto1 = context.ResolveTarget(definition.Target, "popup.variable.target");
                }
                return operation;
            }
        }

        private static bool ContainsPlaceholderSyntax(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            int opening = value.IndexOf('{');
            while (opening >= 0 && opening + 1 < value.Length)
            {
                int closing = value.IndexOf('}', opening + 1);
                if (closing > opening + 1)
                {
                    return true;
                }
                opening = value.IndexOf('{', opening + 1);
            }
            return false;
        }

        private sealed class IoWriteCompiler : IAiOperationCompiler
        {
            public string Kind => "io.write";
            public string DefaultName => "设置IO输出";
            public JObject BuildContract() => Contract("设置一个现有输出IO的状态",
                new[] { "kind", "io", "state" }, new[] { "name", "beforeMs", "afterMs" });

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string io = context.RequireIo(definition.Io, "io.write.io", true);
                if (!definition.State.HasValue) throw new InvalidOperationException("io.write.state 必填。");
                int before = definition.BeforeMs ?? 0;
                int after = definition.AfterMs ?? 0;
                if (before < 0 || before > 3600000 || after < 0 || after > 3600000)
                {
                    throw new InvalidOperationException("io.write.beforeMs/afterMs 必须在 0..3600000 之间。");
                }
                return new IoOperate
                {
                    IOCount = "1",
                    IoParams = new CustomList<IoOutParam>
                    {
                        new IoOutParam { IOName = io, value = definition.State.Value, delayBefore = before, delayAfter = after }
                    }
                };
            }
        }

        private sealed class IoWaitCompiler : IAiOperationCompiler
        {
            public string Kind => "io.wait";
            public string DefaultName => "等待IO状态";
            public JObject BuildContract() => Contract("等待一个现有IO达到目标状态，超时报警",
                new[] { "kind", "io", "state", "timeoutMs" }, new[] { "name" });

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string io = context.RequireIo(definition.Io, "io.wait.io", false);
                if (!definition.State.HasValue) throw new InvalidOperationException("io.wait.state 必填。");
                if (!definition.TimeoutMs.HasValue || definition.TimeoutMs.Value < 1
                    || definition.TimeoutMs.Value > 86400000)
                {
                    throw new InvalidOperationException("io.wait.timeoutMs 必须在 1..86400000 之间。");
                }
                return new IoCheck
                {
                    IOCount = "1",
                    timeOutC = new TimeOutC { TimeOut = definition.TimeoutMs.Value },
                    IoParams = new CustomList<IoCheckParam>
                    {
                        new IoCheckParam { IOName = io, value = definition.State.Value }
                    }
                };
            }
        }

        private sealed class ProcessControlCompiler : IAiOperationCompiler
        {
            public string Kind => "process.control";
            public string DefaultName => "控制流程";
            public JObject BuildContract() => Contract("启动或停止一个现有或同一变更集内定义的流程",
                new[] { "kind", "process", "action" }, new[] { "name", "afterMs" },
                new JProperty("actions", new JArray("start", "stop")));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string process = AiOperationCompileContext.RequireText(definition.Process, "process.control.process");
                string action = AiOperationCompileContext.RequireText(definition.Action, "process.control.action");
                string platformAction;
                if (action == "start") platformAction = "运行";
                else if (action == "stop") platformAction = "停止";
                else throw new InvalidOperationException("process.control.action 只能是 start 或 stop。");
                int after = definition.AfterMs ?? 0;
                if (after < 0 || after > 3600000)
                {
                    throw new InvalidOperationException("process.control.afterMs 必须在 0..3600000 之间。");
                }
                return new ProcOps
                {
                    ProcCount = "1",
                    procParams = new CustomList<procParam>
                    {
                        new procParam { ProcName = process, value = platformAction, delayAfter = after }
                    }
                };
            }
        }

        private sealed class ProcessWaitCompiler : IAiOperationCompiler
        {
            public string Kind => "process.wait";
            public string DefaultName => "等待流程状态";
            public JObject BuildContract() => Contract("等待一个流程进入运行或停止状态，超时报警",
                new[] { "kind", "process", "expectedState", "timeoutMs" }, new[] { "name", "afterMs" },
                new JProperty("states", new JArray("running", "stopped")));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string process = AiOperationCompileContext.RequireText(definition.Process, "process.wait.process");
                string state = AiOperationCompileContext.RequireText(definition.ExpectedState, "process.wait.expectedState");
                string platformState;
                if (state == "running") platformState = "运行";
                else if (state == "stopped") platformState = "停止";
                else throw new InvalidOperationException("process.wait.expectedState 只能是 running 或 stopped。");
                if (!definition.TimeoutMs.HasValue || definition.TimeoutMs.Value < 1
                    || definition.TimeoutMs.Value > 86400000)
                {
                    throw new InvalidOperationException("process.wait.timeoutMs 必须在 1..86400000 之间。");
                }
                int after = definition.AfterMs ?? 0;
                if (after < 0 || after > 3600000)
                {
                    throw new InvalidOperationException("process.wait.afterMs 必须在 0..3600000 之间。");
                }
                return new WaitProc
                {
                    ProcCount = "1",
                    delayAfter = after,
                    timeOutC = new TimeOutC { TimeOut = definition.TimeoutMs.Value },
                    Params = new CustomList<WaitProcParam>
                    {
                        new WaitProcParam { ProcName = process, value = platformState }
                    }
                };
            }
        }
    }
}
