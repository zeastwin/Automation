using System.Collections.Generic;
using System.ComponentModel;

namespace Automation.Protocol
{
    public static class SemanticOperationKinds
    {
        public const string SupportedKinds =
            "variable.set、variable.add、variable.compute、wait、flow.goto、flow.end、branch.number_compare、branch.number_range、popup.message、popup.variable、config.placeholder、io.write、io.wait、process.control、process.wait、native.operation";
    }

    /// <summary>
    /// AI 写入协议。字段表达业务意图，不暴露 OperationType 的 PropertyGrid 属性。
    /// </summary>
    public sealed class AiChangeSet
    {
        public int Version { get; set; }

        public string Title { get; set; }

        [Description("当前公开写入协议使用的原子动作列表；不得与旧的 deleteProcesses/variables/processes 混用。")]
        public List<ChangeSetAction> Actions { get; set; }

        [Description("删除流程选择；mode=all 删除全部，mode=selected 时通过 names/procIds 精确选择。")]
        public ProcessDeleteSelection DeleteProcesses { get; set; }

        public List<VariableChange> Variables { get; set; }

        public List<ProcessDefinition> Processes { get; set; }
    }

    /// <summary>
    /// MCP 对外的原子阶段定义。一个用户任务可以包含多个自然阶段，每个阶段独立预演和提交。
    /// </summary>
    public sealed class AtomicChangeSetDefinition
    {
        public string Title { get; set; }

        [Description("当前已知且可独立保存的动作；同一阶段内全部成功或全部不生效，不要求一次完成整个流程。")]
        public List<ChangeSetAction> Actions { get; set; }
    }

    public static class ChangeSetActionTypes
    {
        public const string SupportedTypes =
            "variable.change、process.create、process.update、process.delete、process.delete_all、"
            + "step.append、step.insert、step.update、step.delete、step.move、"
            + "operation.append、operation.insert、operation.update、operation.delete、operation.move";
    }

    /// <summary>
    /// 强类型动作并集。Type 决定本动作使用哪些目标和载荷，Bridge 会严格拒绝多余字段。
    /// </summary>
    public sealed class ChangeSetAction
    {
        [Description("严格枚举：" + ChangeSetActionTypes.SupportedTypes)]
        public string Type { get; set; }

        [Description("除 variable.change、process.create、process.delete_all 外的流程目标。")]
        public ProcessSelector TargetProcess { get; set; }

        [Description("步骤和指令动作的步骤目标；现有步骤用 stepId，本阶段新步骤用 key。operation.update/delete/move 使用 targetOperation.opId 时可省略，由Bridge反查所属步骤。")]
        public StepSelector TargetStep { get; set; }

        [Description("operation.update/delete/move 的指令目标。")]
        public OperationSelector TargetOperation { get; set; }

        [Description("insert/move 必填的位置锚点；append 不提供。")]
        public ChangePosition Position { get; set; }

        [Description("variable.change 的变量定义及 reuse/create/update/replace/require 策略。")]
        public VariableChange Variable { get; set; }

        [Description("process.create/update 的流程字段；update 只提供要修改的字段。")]
        public ProcessActionValue Process { get; set; }

        [Description("step.append/insert/update 的步骤字段；update 只提供要修改的字段。")]
        public StepActionValue Step { get; set; }

        [Description("operation.append/insert/update 的指令定义；update 使用 native.operation 时 fields 是局部字段补丁，clearFields 显式清空旧字符串字段。")]
        public SemanticOperation Operation { get; set; }
    }

    public sealed class ProcessSelector
    {
        [Description("现有流程稳定 Guid；与 name/key 互斥。")]
        public string ProcId { get; set; }

        [Description("现有流程精确名称；与 procId/key 互斥。")]
        public string Name { get; set; }

        [Description("同一阶段新建流程的局部 key；与 procId/name 互斥。")]
        public string Key { get; set; }
    }

    public sealed class StepSelector
    {
        [Description("现有步骤稳定 Guid；与 key 互斥。")]
        public string StepId { get; set; }

        [Description("步骤局部 key；与 stepId 互斥。AI 创建的步骤会保留该 key，后续阶段可继续使用。")]
        public string Key { get; set; }
    }

    public sealed class OperationSelector
    {
        [Description("现有指令稳定 Guid；与 key 互斥。")]
        public string OpId { get; set; }

        [Description("指令局部 key；与 opId 互斥。AI 创建的指令会保留该 key，后续阶段可继续使用。")]
        public string Key { get; set; }
    }

    public sealed class ChangePosition
    {
        [Description("插入或移动到该现有对象之前；与其他位置字段互斥。")]
        public string BeforeId { get; set; }

        [Description("插入或移动到该局部 key 对象之前；与其他位置字段互斥。")]
        public string BeforeKey { get; set; }

        [Description("插入或移动到该现有对象之后；与其他位置字段互斥。")]
        public string AfterId { get; set; }

        [Description("插入或移动到该局部 key 对象之后；与其他位置字段互斥。")]
        public string AfterKey { get; set; }
    }

    public sealed class ProcessActionValue
    {
        [Description("流程局部 key；同一阶段后续动作需要引用该流程时提供，否则可省略由Bridge生成。")]
        public string Key { get; set; }

        public string Name { get; set; }

        public bool? AutoStart { get; set; }

        public bool? Disable { get; set; }
    }

    public sealed class StepActionValue
    {
        [Description("步骤局部 key；同一阶段需要定位该步骤时提供，否则可省略由Bridge生成。")]
        public string Key { get; set; }

        public string Name { get; set; }

        public bool? Disable { get; set; }
    }

    public sealed class ProcessDeleteSelection
    {
        [Description("严格枚举：all 或 selected。all 不得提供 names/procIds；selected 必须至少提供一种选择器。")]
        public string Mode { get; set; }

        [Description("mode=selected 时按流程名称精确选择；不支持模糊匹配。")]
        public List<string> Names { get; set; }

        [Description("mode=selected 时按流程 GUID 精确选择。")]
        public List<string> ProcIds { get; set; }
    }

    public sealed class VariableChange
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public string InitialValue { get; set; }

        public string Note { get; set; }

        public string Policy { get; set; }
    }

    public sealed class ProcessDefinition
    {
        [Description("原子阶段内新建流程的局部 key；内部编译结果使用，替换现有流程时省略。")]
        public string Key { get; set; }

        /// <summary>create（默认）或 replace。</summary>
        public string Action { get; set; }

        /// <summary>replace 时与 TargetName 二选一，定位现有流程。</summary>
        public string TargetProcId { get; set; }

        /// <summary>replace 时与 TargetProcId 二选一，按精确名称定位现有流程。</summary>
        public string TargetName { get; set; }

        public string Name { get; set; }

        [Description("create 时省略表示 false；replace 时省略表示保留现值。")]
        public bool? AutoStart { get; set; }

        [Description("create 时省略表示 false；replace 时省略表示保留现值。")]
        public bool? Disable { get; set; }

        public List<StepDefinition> Steps { get; set; }
    }

    public sealed class StepDefinition
    {
        [Description("replace 时可提供现有 stepId 以保留步骤身份；新步骤不得提供。")]
        public string StepId { get; set; }

        public string Key { get; set; }

        public string Name { get; set; }

        [Description("新步骤省略表示 false；保留步骤省略表示保留现值。")]
        public bool? Disable { get; set; }

        [Description("按既有配置或精确规范重建时提供原生指令类型序列；完整预演会逐项核对，禁止静默替换相近指令。")]
        public List<string> ExpectedOperaTypes { get; set; }

        public List<SemanticOperation> Operations { get; set; }
    }

    /// <summary>
    /// 常用语义指令的参数并集。编译器会按 kind 严格检查允许字段。
    /// </summary>
    public sealed class SemanticOperation
    {
        [Description("内部完整结构保留既有指令身份；原子动作的 operation 载荷省略，改用 targetOperation.opId 定位。")]
        public string OpId { get; set; }

        [Description("步骤内局部稳定 key；需要作为位置或跳转目标时提供，否则可省略由Bridge生成；operation.update 时继承原key。")]
        public string Key { get; set; }

        [Description("严格枚举：" + SemanticOperationKinds.SupportedKinds + "。固定文本弹框用 popup.message；显示变量当前值用 popup.variable；不得自行创造 kind。")]
        public string Kind { get; set; }

        public string Name { get; set; }

        public string Variable { get; set; }

        public string Value { get; set; }

        public double? Amount { get; set; }

        [Description("variable.compute 的源 double 变量。")]
        public string SourceVariable { get; set; }

        [Description("variable.compute 的运算符：add/subtract/multiply/divide/modulo/absolute。")]
        public string Operator { get; set; }

        [Description("variable.compute 的固定数值操作数；与 operandVariable 互斥，absolute 时省略。")]
        public double? OperandValue { get; set; }

        [Description("variable.compute 的 double 变量操作数；与 operandValue 互斥，absolute 时省略。")]
        public string OperandVariable { get; set; }

        [Description("variable.compute 的结果 double 变量；可与源变量或操作数变量相同。")]
        public string OutputVariable { get; set; }

        public int? Milliseconds { get; set; }

        public OperationTarget Target { get; set; }

        public double? Min { get; set; }

        public double? Max { get; set; }

        [Description("branch.number_compare 的比较符：gt/gte/lt/lte/eq/ne。")]
        public string Comparison { get; set; }

        [Description("branch.number_compare 与变量比较的固定数值。")]
        public double? CompareValue { get; set; }

        public bool? IncludeBounds { get; set; }

        public OperationTarget WhenTrue { get; set; }

        public OperationTarget WhenFalse { get; set; }

        public string Message { get; set; }

        public string ButtonText { get; set; }

        public int? AutoCloseMs { get; set; }

        public string Io { get; set; }

        public bool? State { get; set; }

        public int? BeforeMs { get; set; }

        public int? AfterMs { get; set; }

        public int? TimeoutMs { get; set; }

        public string Process { get; set; }

        public string Action { get; set; }

        public string ExpectedState { get; set; }

        [Description("native.operation 使用的原生指令类型，必须是平台注册的精确 operaType。")]
        public string OperaType { get; set; }

        [Description("native.operation 的严格结构化字段；嵌套对象和数组必须遵循 get_native_operation_schemas 返回的契约。")]
        public Dictionary<string, object> Fields { get; set; }

        [Description("仅 operation.update + native.operation 使用：显式清空现有指令的顶层字符串字段；字段不得同时出现在 fields。")]
        public List<string> ClearFields { get; set; }
    }

    public sealed class OperationTarget
    {
        [Description("目标现有指令的稳定 Guid；与 operationKey 方式互斥。")]
        public string OperationId { get; set; }

        [Description("跨步骤定位时可提供目标步骤稳定 Guid；当前步骤内定位无需提供。")]
        public string StepId { get; set; }

        [Description("跨步骤定位时可提供目标步骤局部 key；当前步骤内定位无需提供。")]
        public string StepKey { get; set; }

        [Description("按指令局部 key 定位；当前步骤只需 operationKey，跨步骤时附加 stepId 或 stepKey。目标尚未创建时保留为待解析引用，后续补齐后自动解析。")]
        public string OperationKey { get; set; }
    }
}
