using System.Collections.Generic;
using System.ComponentModel;

namespace Automation.Protocol
{
    public static class SemanticOperationKinds
    {
        public const string SupportedKinds =
            "variable.set、variable.add、wait、flow.goto、branch.number_range、popup.message、popup.variable、io.write、io.wait、process.control、process.wait、native.operation";
    }

    /// <summary>
    /// AI 写入协议。字段表达业务意图，不暴露 OperationType 的 PropertyGrid 属性。
    /// </summary>
    public sealed class AiChangeSet
    {
        public int Version { get; set; }

        public string Title { get; set; }

        [Description("删除流程选择；mode=all 删除全部，mode=selected 时通过 names/procIds 精确选择。")]
        public ProcessDeleteSelection DeleteProcesses { get; set; }

        public List<VariableChange> Variables { get; set; }

        public List<ProcessDefinition> Processes { get; set; }
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
    /// MCP 对外的单次完整变更定义。内部语义协议不暴露给模型。
    /// </summary>
    public sealed class CompleteChangeSetDefinition
    {
        public string Title { get; set; }

        public ProcessDeleteSelection DeleteProcesses { get; set; }

        public List<VariableChange> Variables { get; set; }

        public List<CompleteProcessDefinition> Processes { get; set; }
    }

    public sealed class CompleteProcessDefinition
    {
        public string Action { get; set; }

        public string TargetProcId { get; set; }

        public string TargetName { get; set; }

        public string Name { get; set; }

        public bool? AutoStart { get; set; }

        public bool? Disable { get; set; }

        public List<CompleteStepDefinition> Steps { get; set; }
    }

    public sealed class CompleteStepDefinition
    {
        [Description("替换现有流程时，使用 get_proc_detail 返回的 stepId 保留步骤身份；新步骤省略。")]
        public string StepId { get; set; }

        public string Key { get; set; }

        public string Name { get; set; }

        public bool? Disable { get; set; }

        [Description("仅在精确复刻既有配置或规范时提供按顺序排列的原生 operaType；普通新建流程省略。")]
        public List<string> ExpectedOperaTypes { get; set; }

        [Description("该步骤的完整原生指令列表；operaType必须来自get_operation_schemas。")]
        public List<NativeOperationDefinition> Operations { get; set; }
    }

    public sealed class NativeOperationDefinition
    {
        [Description("替换现有流程时，使用读取结果中的 opId 保留指令身份；仅调整顺序时只需提供 opId。")]
        public string OpId { get; set; }

        [Description("同一 ChangeSet 新建指令的局部稳定 key；被其他新指令跳转引用时必填。")]
        public string Key { get; set; }

        public string OperaType { get; set; }

        public string Name { get; set; }

        [Description("严格结构化字段；嵌套对象和数组必须遵循get_operation_schemas返回的契约。")]
        public Dictionary<string, object> Fields { get; set; }
    }

    /// <summary>
    /// 常用语义指令的参数并集。编译器会按 kind 严格检查允许字段。
    /// </summary>
    public sealed class SemanticOperation
    {
        [Description("replace 时保留既有指令身份；只提供 opId 表示完整复用原指令。")]
        public string OpId { get; set; }

        [Description("新指令在当前步骤内的局部稳定 key。")]
        public string Key { get; set; }

        [Description("严格枚举：" + SemanticOperationKinds.SupportedKinds + "。固定文本弹框用 popup.message；显示变量当前值用 popup.variable；不得自行创造 kind。")]
        public string Kind { get; set; }

        public string Name { get; set; }

        public string Variable { get; set; }

        public string Value { get; set; }

        public double? Amount { get; set; }

        public int? Milliseconds { get; set; }

        public OperationTarget Target { get; set; }

        public double? Min { get; set; }

        public double? Max { get; set; }

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

        [Description("native.operation 的严格结构化字段；嵌套对象和数组必须遵循 get_native_operation_contract 返回的契约。")]
        public Dictionary<string, object> Fields { get; set; }
    }

    public sealed class OperationTarget
    {
        [Description("目标现有指令的稳定 Guid；与 step/operationKey/operation 三种方式互斥。")]
        public string OperationId { get; set; }

        public string Step { get; set; }

        [Description("目标新指令的局部 key；必须同时提供 step。")]
        public string OperationKey { get; set; }

        [Description("兼容完整新建流程的最终索引；编辑既有流程优先使用 operationId 或 operationKey。")]
        public int? Operation { get; set; }
    }
}
