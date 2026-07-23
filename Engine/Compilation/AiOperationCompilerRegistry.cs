using Automation.Protocol;
// 模块：引擎 / 编译。
// 职责范围：把 ChangeSet、语义指令和结构化原生指令编译为确定性流程模型。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using static Automation.OperationTypePartial;

namespace Automation
{
    public sealed class AiIoResource
    {
        public string IoType { get; set; }
        public int CardNum { get; set; }
        public string IoIndex { get; set; }
    }

    public sealed class AiResourceSnapshot
    {
        public AiResourceSnapshot(
            IReadOnlyDictionary<string, AiIoResource> ios = null,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> references = null)
        {
            IoResources = ios ?? new Dictionary<string, AiIoResource>(StringComparer.Ordinal);
            References = references ?? new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, AiIoResource> IoResources { get; }

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
        private readonly IReadOnlyDictionary<Guid, OperationReferenceLocation> _operationIdLocations;
        private readonly IReadOnlyDictionary<string, OperationReferenceLocation> _operationKeyLocations;
        private readonly IReadOnlyDictionary<string, DicValue> _variables;
        private readonly AiResourceSnapshot _resources;
        private readonly Guid _currentStepId;
        private readonly string _currentStepKey;

        public AiOperationCompileContext(
            int procIndex,
            IReadOnlyDictionary<string, DicValue> variables,
            AiResourceSnapshot resources,
            IReadOnlyDictionary<Guid, OperationReferenceLocation> operationIdLocations = null,
            IReadOnlyDictionary<string, OperationReferenceLocation> operationKeyLocations = null,
            Guid currentStepId = default(Guid),
            string currentStepKey = null)
        {
            _procIndex = procIndex;
            _operationIdLocations = operationIdLocations
                ?? new Dictionary<Guid, OperationReferenceLocation>();
            _operationKeyLocations = operationKeyLocations
                ?? new Dictionary<string, OperationReferenceLocation>(StringComparer.Ordinal);
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _resources = resources ?? new AiResourceSnapshot();
            _currentStepId = currentStepId;
            _currentStepKey = currentStepKey;
        }

        public string RequireIo(string name, string path, string requiredIoType = null)
        {
            string exactName = RequireText(name, path);
            if (!_resources.IoResources.TryGetValue(exactName, out AiIoResource io))
            {
                throw new InvalidOperationException($"{path} 引用的IO不存在：{exactName}");
            }
            if (!string.IsNullOrEmpty(requiredIoType)
                && !string.Equals(io.IoType, requiredIoType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{path} 只能引用{requiredIoType}：{exactName} 当前类型为 {io.IoType}");
            }
            return exactName;
        }

        public AiIoResource RequireIoResource(
            string name,
            string path,
            string requiredIoType = null)
        {
            string exactName = RequireIo(name, path, requiredIoType);
            return _resources.IoResources[exactName];
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
            if (string.Equals(referenceType, "alarm.infoId", StringComparison.Ordinal))
            {
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int alarmIndex)
                    || alarmIndex < 0
                    || alarmIndex >= AlarmInfoStore.AlarmCapacity)
                {
                    throw new InvalidOperationException(
                        $"{path} 必须是 [0, {AlarmInfoStore.AlarmCapacity}) 范围内的报警信息编号。");
                }

                // 报警资源允许晚于流程定义补齐。资源是否存在属于运行就绪条件，
                // 由 ProcessReadinessService 在启动闸门统一拦截，不阻止配置保存。
                return;
            }
            if (string.Equals(referenceType, "plc.device", StringComparison.Ordinal))
            {
                // PLC设备允许晚于流程定义补齐；运行就绪分析会基于当前设备清单拦截启动。
                return;
            }
            if (string.Equals(referenceType, "value", StringComparison.Ordinal))
            {
                RequireVariable(text, path);
                return;
            }
            if (referenceType.StartsWith("io.", StringComparison.Ordinal))
            {
                string requiredIoType = string.Equals(
                    referenceType,
                    "io.input",
                    StringComparison.Ordinal)
                    ? "通用输入"
                    : (string.Equals(referenceType, "io.output", StringComparison.Ordinal)
                        ? "通用输出"
                        : null);
                RequireIo(text, path, requiredIoType);
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
            if (target == null) return string.Empty;
            bool hasOperationId = !string.IsNullOrWhiteSpace(target.OperationId);
            bool hasOperationKey = !string.IsNullOrWhiteSpace(target.OperationKey);
            int selectorCount = (hasOperationId ? 1 : 0) + (hasOperationKey ? 1 : 0);
            if (selectorCount != 1)
            {
                throw new InvalidOperationException(
                    $"{path} 必须且只能使用 operationId 或 operationKey 定位目标。");
            }
            if (hasOperationId)
            {
                if (!string.IsNullOrWhiteSpace(target.StepId)
                    || !string.IsNullOrWhiteSpace(target.StepKey))
                {
                    throw new InvalidOperationException(
                        $"{path} 使用 operationId 时不得提供 stepId 或 stepKey。");
                }
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

            int stepSelectorCount = (!string.IsNullOrWhiteSpace(target.StepId) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(target.StepKey) ? 1 : 0);
            if (stepSelectorCount > 1)
            {
                throw new InvalidOperationException(
                    $"{path} 使用 operationKey 时 stepId 和 stepKey 不能同时提供。");
            }
            string operationKey = RequireText(target.OperationKey, path + ".operationKey");
            string mapKey;
            string stepDisplay;
            if (!string.IsNullOrWhiteSpace(target.StepId))
            {
                if (!Guid.TryParse(target.StepId, out Guid stepId) || stepId == Guid.Empty)
                {
                    throw new InvalidOperationException($"{path}.stepId 不是有效 Guid。");
                }
                mapKey = BuildOperationKeyForStepId(stepId, operationKey);
                stepDisplay = stepId.ToString("D");
            }
            else if (!string.IsNullOrWhiteSpace(target.StepKey))
            {
                string stepKey = RequireText(target.StepKey, path + ".stepKey");
                mapKey = BuildOperationKeyForStepKey(stepKey, operationKey);
                stepDisplay = stepKey;
            }
            else if (_currentStepId != Guid.Empty)
            {
                mapKey = BuildOperationKeyForStepId(_currentStepId, operationKey);
                stepDisplay = _currentStepId.ToString("D");
            }
            else if (!string.IsNullOrWhiteSpace(_currentStepKey))
            {
                mapKey = BuildOperationKeyForStepKey(_currentStepKey, operationKey);
                stepDisplay = _currentStepKey;
            }
            else
            {
                throw new InvalidOperationException($"{path} 无法确定 operationKey 所在步骤。");
            }
            if (!_operationKeyLocations.TryGetValue(mapKey, out OperationReferenceLocation keyLocation))
            {
                return ProcessDefinitionService.BuildPendingGoto(mapKey);
            }
            return $"{_procIndex}-{keyLocation.StepIndex}-{keyLocation.OperationIndex}";
        }

        public static string BuildOperationKeyForStepKey(string stepKey, string operationKey)
        {
            return "key:" + stepKey + "\0" + operationKey;
        }

        public static string BuildOperationKeyForStepId(Guid stepId, string operationKey)
        {
            return "id:" + stepId.ToString("D") + "\0" + operationKey;
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
        public const string SaveRequiredContractField = "saveRequired";
        public const string OptionalContractField = "optional";

        private static readonly IReadOnlyDictionary<string, IAiOperationCompiler> Compilers
            = new IAiOperationCompiler[]
            {
                new VariableSetCompiler(),
                new VariableAddCompiler(),
                new VariableComputeCompiler(),
                new WaitCompiler(),
                new FlowGotoCompiler(),
                new FlowEndCompiler(),
                new NumberCompareBranchCompiler(),
                new NumberRangeBranchCompiler(),
                new IoBranchCompiler(),
                new PopupMessageCompiler(),
                new PopupVariableCompiler(),
                new ConfigurationPlaceholderCompiler(),
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

        public static IReadOnlyCollection<string> GetDefinitionFields(string kind)
        {
            JObject contract = Get(kind).BuildContract();
            if (!(contract[SaveRequiredContractField] is JArray saveRequired)
                || !(contract[OptionalContractField] is JArray optional))
            {
                throw new InvalidOperationException($"语义指令契约缺少字段集合：{kind}");
            }
            string[] fields = saveRequired.Values<string>()
                .Concat(optional.Values<string>())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (!fields.Contains("kind", StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"语义指令契约未声明 kind：{kind}");
            }
            return fields;
        }

        public static JObject BuildCapabilities()
        {
            return new JObject
            {
                ["protocol"] = "change-set-v2",
                ["changeActions"] = new JArray(ChangeSetActionTypes.SupportedTypes.Split('、')),
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
                    ["contractTool"] = "get_native_operation_schemas",
                    ["rule"] = "高层 kind 不覆盖目标指令时，按一个精确原生 operaType 读取递归契约"
                },
                ["workflow"] = new JArray(
                    "按依赖和验证边界划分一个可独立审查的原子阶段",
                    "用稳定ID和阶段内key构造actions",
                    "preview_change_set(actions)",
                    "内容符合目标时等待前台确认并apply_change_set(previewId)",
                    "内容需要改写时discard_change_set_preview(previewId)",
                    "需要时根据真实提交结果继续下一原子阶段"),
                ["rule"] = "局部key只在当前阶段有效；后续阶段使用提交返回的稳定ID。普通业务目标优先使用语义kind，精确原生类型使用native.operation。"
            };
        }

        public static JObject BuildContracts(IEnumerable<string> kinds)
        {
            string[] requested = (kinds ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (requested.Length < 1)
            {
                throw new InvalidOperationException("kinds 至少包含一个语义指令类型。");
            }
            var contracts = new JObject();
            foreach (string kind in requested)
            {
                IAiOperationCompiler compiler = Get(kind ?? string.Empty);
                GetDefinitionFields(kind);
                contracts[kind] = compiler.BuildContract();
            }
            return new JObject
            {
                ["schemaRoute"] = new JObject
                {
                    ["representation"] = "semantic",
                    ["writeField"] = "operation.kind",
                    ["nextTool"] = "preview_change_set",
                    ["fieldMeaning"] = "saveRequired决定配置能否保存；runRequired决定流程能否启动；optional可省略",
                    ["rule"] = "按返回契约填写语义kind；native.operation使用原生Schema"
                },
                ["contracts"] = contracts
            };
        }

        private static JObject Contract(string purpose, string[] saveRequired, string[] optional, params JProperty[] extras)
        {
            var result = new JObject
            {
                ["purpose"] = purpose,
                [SaveRequiredContractField] = new JArray(saveRequired),
                [OptionalContractField] = new JArray(optional)
            };
            foreach (JProperty extra in extras ?? Array.Empty<JProperty>()) result.Add(extra);
            return result;
        }

        private static JObject SymbolicTargetContract()
        {
            return new JObject
            {
                ["selectorRule"] = "operationId与operationKey二选一",
                ["currentStep"] = new JObject { ["operationKey"] = "当前ChangeSet内的指令key" },
                ["crossStep"] = new JArray(
                    new JObject { ["stepId"] = "现有步骤Guid", ["operationKey"] = "目标指令key" },
                    new JObject { ["stepKey"] = "当前ChangeSet内的步骤key", ["operationKey"] = "目标指令key" }),
                ["existingOperation"] = new JObject { ["operationId"] = "现有指令Guid" },
                ["unresolvedReference"] = "未定义的operationKey可先保存为未就绪引用；后续创建同标签指令后由Bridge解析",
                ["physicalIndexAllowed"] = false
            };
        }

        private sealed class ResolvedIoCondition
        {
            public string Io { get; set; }

            public bool State { get; set; }
        }

        private static List<ResolvedIoCondition> ResolveInputIoConditions(
            SemanticOperation definition,
            AiOperationCompileContext context,
            string path)
        {
            if (definition?.Conditions == null || definition.Conditions.Count == 0)
            {
                throw new InvalidOperationException($"{path} 至少包含一个IO条件。");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<ResolvedIoCondition>();
            for (int index = 0; index < definition.Conditions.Count; index++)
            {
                IoStateCondition condition = definition.Conditions[index]
                    ?? throw new InvalidOperationException($"{path}[{index}] 不能为空。");
                string io = context.RequireIo(
                    condition.Io,
                    $"{path}[{index}].io",
                    "通用输入");
                if (!condition.State.HasValue)
                {
                    throw new InvalidOperationException($"{path}[{index}].state 必填。");
                }
                if (!names.Add(io))
                {
                    throw new InvalidOperationException($"{path} 包含重复IO：{io}");
                }
                result.Add(new ResolvedIoCondition { Io = io, State = condition.State.Value });
            }
            return result;
        }

        private sealed class NativeOperationCompiler : IAiOperationCompiler
        {
            public string Kind => "native.operation";

            public string DefaultName => "原生指令";

            public JObject BuildContract()
            {
                return Contract("按精确 operaType 和递归字段契约编译任意平台注册指令",
                    new[] { "kind", "operaType", "fields" }, new[] { "name", "clearFields" },
                    new JProperty("contractTool", "get_native_operation_schemas"),
                    new JProperty("rule", "fields使用原生递归契约；clearFields仅用于update清空旧字符串字段"));
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
                    ? Contract("对 double 变量累加固定数值", new[] { "kind", "variable", "amount" }, new[] { "name" },
                        new JProperty("valueRule", "amount是JSON数值；变量参与运算改用variable.compute"))
                    : Contract("把变量设置为固定字面量", new[] { "kind", "variable", "value" }, new[] { "name" },
                        new JProperty("valueRule", "value不解析算式、模板或变量引用；double变量填写数字文本，变量运算改用variable.compute"));
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
                        throw new InvalidOperationException(
                            $"变量[{variableName}]是 double，variable.set.value 必须是有效数字文本；这里不解析算式或模板，变量运算请改用 variable.add/variable.compute。");
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

        private sealed class VariableComputeCompiler : IAiOperationCompiler
        {
            private static readonly HashSet<string> Operators = new HashSet<string>(StringComparer.Ordinal)
            {
                "add", "subtract", "multiply", "divide", "modulo", "absolute"
            };

            public string Kind => "variable.compute";

            public string DefaultName => "变量计算";

            public JObject BuildContract() => Contract(
                "用 double 变量和固定数值或另一个 double 变量计算，并把结果写入指定变量",
                new[] { "kind", "sourceVariable", "operator", "outputVariable" },
                new[] { "name", "operandValue", "operandVariable" },
                new JProperty("operators", new JArray(Operators.OrderBy(value => value, StringComparer.Ordinal))),
                new JProperty("operandRule", "除 absolute 外，operandValue 与 operandVariable 必须且只能提供一个；absolute 两者都不提供"));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string source = AiOperationCompileContext.RequireText(
                    definition.SourceVariable, "variable.compute.sourceVariable");
                string output = AiOperationCompileContext.RequireText(
                    definition.OutputVariable, "variable.compute.outputVariable");
                context.RequireVariable(source, "variable.compute.sourceVariable", "double");
                context.RequireVariable(output, "variable.compute.outputVariable", "double");

                string semanticOperator = AiOperationCompileContext.RequireText(
                    definition.Operator, "variable.compute.operator");
                if (!Operators.Contains(semanticOperator))
                {
                    throw new InvalidOperationException(
                        "variable.compute.operator 只能是 add/subtract/multiply/divide/modulo/absolute。");
                }

                bool hasLiteral = definition.OperandValue.HasValue;
                bool hasVariable = !string.IsNullOrWhiteSpace(definition.OperandVariable);
                if (semanticOperator == "absolute")
                {
                    if (hasLiteral || hasVariable)
                        throw new InvalidOperationException("variable.compute.operator=absolute 时不得提供操作数。");
                }
                else if (hasLiteral == hasVariable)
                {
                    throw new InvalidOperationException(
                        "variable.compute 必须且只能提供 operandValue 或 operandVariable。");
                }

                if (hasVariable)
                {
                    context.RequireVariable(definition.OperandVariable,
                        "variable.compute.operandVariable", "double");
                }
                if ((semanticOperator == "divide" || semanticOperator == "modulo")
                    && hasLiteral && definition.OperandValue.Value == 0d)
                {
                    throw new InvalidOperationException("variable.compute 的除数或求余操作数不能为0。");
                }

                string modifyType;
                bool reverseOperand = false;
                switch (semanticOperator)
                {
                    case "add": modifyType = "叠加"; break;
                    case "subtract": modifyType = "叠加"; reverseOperand = true; break;
                    case "multiply": modifyType = "乘法"; break;
                    case "divide": modifyType = "除法"; break;
                    case "modulo": modifyType = "求余"; break;
                    default: modifyType = "绝对值"; break;
                }

                return new ModifyValue
                {
                    ModifyType = modifyType,
                    ValueSourceName = source.Trim(),
                    NegateOperand = reverseOperand,
                    ChangeValue = semanticOperator == "absolute" || hasLiteral
                        ? (semanticOperator == "absolute" ? "0" : definition.OperandValue.Value.ToString(CultureInfo.InvariantCulture))
                        : null,
                    ChangeValueName = hasVariable ? definition.OperandVariable.Trim() : null,
                    OutputValueName = output.Trim()
                };
            }
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
                    DelayMs = definition.Milliseconds.Value
                };
            }
        }

        private sealed class FlowGotoCompiler : IAiOperationCompiler
        {
            public string Kind => "flow.goto";
            public string DefaultName => "跳转";

            public JObject BuildContract() => Contract("无条件跳转到当前定义流程内的符号位置",
                new[] { "kind" }, new[] { "name", "target" },
                new JProperty("targetShape", SymbolicTargetContract()),
                new JProperty("runRequired", new JArray("target")));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                return new Goto
                {
                    Params = new CustomList<GotoParam>(),
                    DefaultGoto = context.ResolveTarget(definition.Target, "flow.goto.target")
                };
            }
        }

        private sealed class FlowEndCompiler : IAiOperationCompiler
        {
            public string Kind => "flow.end";
            public string DefaultName => "结束流程";

            public JObject BuildContract() => Contract(
                "在执行到当前位置时正常结束当前流程",
                new[] { "kind" }, new[] { "name" },
                new JProperty("terminationReason", "Completed"));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                return new EndProcess();
            }
        }

        private sealed class NumberRangeBranchCompiler : IAiOperationCompiler
        {
            public string Kind => "branch.number_range";
            public string DefaultName => "数值区间判断";

            public JObject BuildContract() => Contract("按 double 变量是否位于数值区间进行双分支跳转",
                new[] { "kind", "variable", "min", "max" },
                new[] { "name", "includeBounds", "whenTrue", "whenFalse" },
                new JProperty("targetShape", SymbolicTargetContract()),
                new JProperty("runRequired", new JArray("whenTrue", "whenFalse")));

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
                    InvalidDelayMs = 10,
                    TrueGoto = context.ResolveTarget(definition.WhenTrue, "branch.number_range.whenTrue"),
                    FalseGoto = context.ResolveTarget(definition.WhenFalse, "branch.number_range.whenFalse"),
                    Params = new CustomList<ParamGotoParam>
                    {
                        new ParamGotoParam
                        {
                            ValueName = variableName,
                            JudgeMode = "值在区间内",
                            Down = definition.Min.Value,
                            Up = definition.Max.Value,
                            IncludeBoundary = definition.IncludeBounds ?? true,
                            Operator = "且"
                        }
                    }
                };
            }
        }

        private sealed class NumberCompareBranchCompiler : IAiOperationCompiler
        {
            private static readonly HashSet<string> Comparisons = new HashSet<string>(StringComparer.Ordinal)
            {
                "gt", "gte", "lt", "lte", "eq", "ne"
            };

            public string Kind => "branch.number_compare";

            public string DefaultName => "数值比较";

            public JObject BuildContract() => Contract(
                "把 double 变量与固定数值比较，并按结果跳转",
                new[] { "kind", "variable", "comparison", "compareValue" },
                new[] { "name", "whenTrue", "whenFalse" },
                new JProperty("comparisons", new JArray("gt", "gte", "lt", "lte", "eq", "ne")),
                new JProperty("targetShape", SymbolicTargetContract()),
                new JProperty("runRequired", new JArray("whenTrue", "whenFalse")));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                context.RequireVariable(definition.Variable, "branch.number_compare.variable", "double");
                string comparison = AiOperationCompileContext.RequireText(
                    definition.Comparison, "branch.number_compare.comparison");
                if (!Comparisons.Contains(comparison))
                    throw new InvalidOperationException(
                        "branch.number_compare.comparison 只能是 gt/gte/lt/lte/eq/ne。");
                if (!definition.CompareValue.HasValue)
                    throw new InvalidOperationException("branch.number_compare.compareValue 必填。");

                string judgeMode;
                bool includeBoundary;
                double up = 0;
                switch (comparison)
                {
                    case "gt": judgeMode = "值在区间右"; includeBoundary = false; break;
                    case "gte": judgeMode = "值在区间右"; includeBoundary = true; break;
                    case "lt": judgeMode = "值在区间左"; includeBoundary = false; break;
                    case "lte": judgeMode = "值在区间左"; includeBoundary = true; break;
                    default:
                        judgeMode = "值在区间内";
                        includeBoundary = true;
                        up = definition.CompareValue.Value;
                        break;
                }

                string trueGoto = context.ResolveTarget(
                    comparison == "ne" ? definition.WhenFalse : definition.WhenTrue,
                    comparison == "ne" ? "branch.number_compare.whenFalse" : "branch.number_compare.whenTrue");
                string falseGoto = context.ResolveTarget(
                    comparison == "ne" ? definition.WhenTrue : definition.WhenFalse,
                    comparison == "ne" ? "branch.number_compare.whenTrue" : "branch.number_compare.whenFalse");
                return new ParamGoto
                {
                    InvalidDelayMs = 10,
                    TrueGoto = trueGoto,
                    FalseGoto = falseGoto,
                    Params = new CustomList<ParamGotoParam>
                    {
                        new ParamGotoParam
                        {
                            ValueName = definition.Variable.Trim(),
                            JudgeMode = judgeMode,
                            Down = definition.CompareValue.Value,
                            Up = up,
                            IncludeBoundary = includeBoundary,
                            Operator = "且"
                        }
                    }
                };
            }
        }

        private sealed class IoBranchCompiler : IAiOperationCompiler
        {
            public string Kind => "branch.io";

            public string DefaultName => "IO条件分支";

            public JObject BuildContract() => Contract(
                "立即读取一组输入IO的运行时逻辑状态，并按组合结果双分支跳转",
                new[] { "kind", "conditions" },
                new[] { "name", "conditionLogic", "whenTrue", "whenFalse" },
                new JProperty("conditionLogicValues", new JArray("all", "any")),
                new JProperty("conditionShape", new JObject
                {
                    ["required"] = new JArray("io", "state"),
                    ["ioType"] = "通用输入",
                    ["stateMeaning"] = "输入IO运行时逻辑状态，不统一表示安全位或工作位"
                }),
                new JProperty("targetShape", SymbolicTargetContract()),
                new JProperty("runRequired", new JArray("whenTrue", "whenFalse")),
                new JProperty("runtimeBehavior", "读取或映射失败进入当前指令的报警策略；条件为真跳转whenTrue，为假跳转whenFalse"));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                List<ResolvedIoCondition> conditions = ResolveInputIoConditions(
                    definition, context, "branch.io.conditions");
                string logic = string.IsNullOrWhiteSpace(definition.ConditionLogic)
                    ? "all"
                    : definition.ConditionLogic.Trim();
                if (!string.Equals(logic, "all", StringComparison.Ordinal)
                    && !string.Equals(logic, "any", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("branch.io.conditionLogic 只能是 all 或 any。");
                }
                string nativeLogic = string.Equals(logic, "all", StringComparison.Ordinal) ? "与" : "或";
                var ioParams = new CustomList<IoLogicGotoParam>();
                foreach (ResolvedIoCondition condition in conditions)
                {
                    ioParams.Add(new IoLogicGotoParam
                    {
                        IoName = condition.Io,
                        Target = condition.State,
                        Logic = nativeLogic
                    });
                }
                return new IoLogicGoto
                {
                    InvalidDelayMs = 0,
                    TrueGoto = context.ResolveTarget(definition.WhenTrue, "branch.io.whenTrue"),
                    FalseGoto = context.ResolveTarget(definition.WhenFalse, "branch.io.whenFalse"),
                    IoParams = ioParams
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
                new JProperty("autoCloseMs", "省略表示不自动关闭；提供时范围1..3600000，不能用0表示关闭"),
                new JProperty("runtimeBehavior", "message 会原样显示；{变量名}不是模板语法"),
                new JProperty("targetShape", SymbolicTargetContract()),
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
                    throw new InvalidOperationException(
                        "popup.message.autoCloseMs 必须在 1..3600000 之间；不需要自动关闭时请省略该字段，不能填0。");
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
                new JProperty("autoCloseMs", "省略表示不自动关闭；提供时范围1..3600000，不能用0表示关闭"),
                new JProperty("runtimeBehavior", "弹框正文只显示变量当前值；name 是弹框标题"),
                new JProperty("targetShape", SymbolicTargetContract()),
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
                    throw new InvalidOperationException(
                        "popup.variable.autoCloseMs 必须在 1..3600000 之间；不需要自动关闭时请省略该字段，不能填0。");
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

        private sealed class ConfigurationPlaceholderCompiler : IAiOperationCompiler
        {
            public string Kind => "config.placeholder";

            public string DefaultName => "待完善配置";

            public JObject BuildContract() => Contract(
                "在目标、资源或业务参数暂时无法确定时保留一个显式占位，后续可继续更新；占位存在时平台允许保存但禁止启动流程",
                new[] { "kind", "message" }, new[] { "name" },
                new JProperty("readiness", "incomplete-until-replaced"),
                new JProperty("runBehavior", "blocked-before-start"));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string reason = AiOperationCompileContext.RequireText(
                    definition.Message, "config.placeholder.message");
                return new PopupDialog
                {
                    PopupType = "弹是",
                    InfoType = "自定义提示信息",
                    PopupMessage = "此处配置尚未完成：" + reason,
                    Btn1Text = "知道了",
                    AlarmLightEnable = "禁用",
                    Note = ProcessReadinessService.PlaceholderNotePrefix + reason
                };
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
            public string DefaultName => "同步设置IO输出";
            public JObject BuildContract() => Contract("通过一次端口写入同步设置同一卡的一个或多个输出IO",
                new[] { "kind", "outputs" }, new[] { "name" },
                new JProperty("stateSemantics", new JObject
                {
                    ["valueDomain"] = "runtime-logical-boolean",
                    ["true"] = "该精确输出IO逻辑激活",
                    ["false"] = "该精确输出IO逻辑未激活",
                    ["componentMeaning"] = "true/false不统一表示安全位或工作位；先确定机构目标，再确定输出逻辑值"
                }),
                new JProperty("outputShape", new JObject
                {
                    ["required"] = new JArray("io", "state"),
                    ["minimumItems"] = 1,
                    ["constraints"] = new JArray("IO名称不重复", "全部IO为通用输出", "全部IO位于同一张控制卡", "IOIndex在0..31内且不重复")
                }),
                new JProperty("runtimeBehavior", "同一outputs数组编译为一条IO操作，并通过一次端口写入同步下达"));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                if (definition.Outputs == null || definition.Outputs.Count == 0)
                    throw new InvalidOperationException("io.write.outputs 至少包含一个输出IO。");
                var ioParams = new CustomList<IoOutParam>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                var indexes = new HashSet<int>();
                int? cardNum = null;
                for (int index = 0; index < definition.Outputs.Count; index++)
                {
                    IoOutputState output = definition.Outputs[index]
                        ?? throw new InvalidOperationException($"io.write.outputs[{index}] 不能为空。");
                    AiIoResource ioResource = context.RequireIoResource(
                        output.Io,
                        $"io.write.outputs[{index}].io",
                        "通用输出");
                    string io = output.Io.Trim();
                    if (!output.State.HasValue)
                        throw new InvalidOperationException($"io.write.outputs[{index}].state 必填。");
                    if (!names.Add(io))
                        throw new InvalidOperationException($"io.write.outputs 包含重复IO：{io}");
                    if (cardNum.HasValue && cardNum.Value != ioResource.CardNum)
                        throw new InvalidOperationException("io.write.outputs 的全部输出IO必须位于同一张控制卡。");
                    cardNum = ioResource.CardNum;
                    if (!int.TryParse(ioResource.IoIndex, out int ioIndex) || ioIndex < 0 || ioIndex > 31)
                        throw new InvalidOperationException($"io.write.outputs[{index}] 的IOIndex必须在0..31内：{ioResource.IoIndex}");
                    if (!indexes.Add(ioIndex))
                        throw new InvalidOperationException($"io.write.outputs 包含重复输出索引：{ioIndex}");
                    ioParams.Add(new IoOutParam { IoName = io, TargetState = output.State.Value });
                }
                return new IoOperate
                {
                    IoParams = ioParams
                };
            }
        }

        private sealed class IoWaitCompiler : IAiOperationCompiler
        {
            public string Kind => "io.wait";
            public string DefaultName => "等待IO状态";
            public JObject BuildContract() => Contract("等待一组现有输入IO同时达到目标状态；失败时报警停止或进入明确恢复目标",
                new[] { "kind", "conditions", "timeoutMs" }, new[] { "name", "onFailure" },
                new JProperty("stateSemantics", new JObject
                {
                    ["valueDomain"] = "runtime-logical-boolean",
                    ["true"] = "该精确IO或传感器条件成立",
                    ["false"] = "该精确IO或传感器条件不成立",
                    ["componentMeaning"] = "机构到位由目标反馈和必要的对向反馈共同证明；例如缩回时原位为true、动位为false"
                }),
                new JProperty("conditionShape", new JObject
                {
                    ["required"] = new JArray("io", "state"),
                    ["ioType"] = "通用输入",
                    ["match"] = "全部条件同时成立时正常完成"
                }),
                new JProperty("targetShape", SymbolicTargetContract()),
                new JProperty("failureBehavior", new JObject
                {
                    ["withoutOnFailure"] = "报警停止",
                    ["withOnFailure"] = "自动处理并跳转onFailure",
                    ["failureScope"] = "超时、IO映射缺失、IO类型无效和IO读取失败"
                }));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                List<ResolvedIoCondition> conditions = ResolveInputIoConditions(
                    definition, context, "io.wait.conditions");
                if (!definition.TimeoutMs.HasValue || definition.TimeoutMs.Value < 1
                    || definition.TimeoutMs.Value > 86400000)
                {
                    throw new InvalidOperationException("io.wait.timeoutMs 必须在 1..86400000 之间。");
                }
                var ioParams = new CustomList<IoCheckParam>();
                foreach (ResolvedIoCondition condition in conditions)
                {
                    ioParams.Add(new IoCheckParam { IoName = condition.Io, ExpectedState = condition.State });
                }
                var operation = new IoCheck
                {
                    Timeout = new TimeoutSetting { TimeoutMs = definition.TimeoutMs.Value },
                    IoParams = ioParams
                };
                if (definition.OnFailure != null)
                {
                    operation.AlarmType = "自动处理";
                    operation.Goto1 = context.ResolveTarget(definition.OnFailure, "io.wait.onFailure");
                }
                return operation;
            }
        }

        private sealed class ProcessControlCompiler : IAiOperationCompiler
        {
            public string Kind => "process.control";
            public string DefaultName => "控制流程";
            public JObject BuildContract() => Contract("启动或停止一个现有或同一变更集内定义的流程",
                new[] { "kind" }, new[] { "name", "process", "action", "afterMs" },
                new JProperty("actions", new JArray("start", "stop")),
                new JProperty("runRequired", new JArray("process", "action")));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string process = definition.Process?.Trim() ?? string.Empty;
                string action = definition.Action?.Trim() ?? string.Empty;
                string platformAction = string.Empty;
                if (action == "start") platformAction = "运行";
                else if (action == "stop") platformAction = "停止";
                else if (action.Length > 0)
                    throw new InvalidOperationException("process.control.action 只能是 start 或 stop。");
                int after = definition.AfterMs ?? 0;
                if (after < 0 || after > 3600000)
                {
                    throw new InvalidOperationException("process.control.afterMs 必须在 0..3600000 之间。");
                }
                return new ProcOps
                {
                    Params = new CustomList<ProcParam>
                    {
                        new ProcParam { ProcName = process, TargetState = platformAction, DelayAfterMs = after }
                    }
                };
            }
        }

        private sealed class ProcessWaitCompiler : IAiOperationCompiler
        {
            public string Kind => "process.wait";
            public string DefaultName => "等待流程状态";
            public JObject BuildContract() => Contract("等待一个流程进入运行或就绪状态，超时报警",
                new[] { "kind" }, new[] { "name", "process", "expectedState", "timeoutMs", "afterMs" },
                new JProperty("states", new JArray("running", "ready")),
                new JProperty("runRequired", new JArray("process", "expectedState", "timeoutMs")));

            public OperationType Compile(SemanticOperation definition, AiOperationCompileContext context)
            {
                string process = definition.Process?.Trim() ?? string.Empty;
                string state = definition.ExpectedState?.Trim() ?? string.Empty;
                string platformState = string.Empty;
                if (state == "running") platformState = "运行";
                else if (state == "ready") platformState = "就绪";
                else if (state.Length > 0)
                    throw new InvalidOperationException("process.wait.expectedState 只能是 running 或 ready。");
                if (definition.TimeoutMs.HasValue && (definition.TimeoutMs.Value < 1
                    || definition.TimeoutMs.Value > 86400000))
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
                    WorkMode = WaitProc.WaitReadyMode,
                    DelayAfterMs = after,
                    Timeout = new TimeoutSetting { TimeoutMs = definition.TimeoutMs ?? 0 },
                    Params = new CustomList<WaitProcParam>
                    {
                        new WaitProcParam { ProcName = process, TargetState = platformState }
                    }
                };
            }
        }
    }
}
