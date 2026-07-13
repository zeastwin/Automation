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

        public bool AutoStart { get; set; }

        public bool Disable { get; set; }

        public List<StepDefinition> Steps { get; set; }
    }

    public sealed class StepDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public bool Disable { get; set; }

        public List<SemanticOperation> Operations { get; set; }
    }

    /// <summary>
    /// 常用语义指令的参数并集。编译器会按 kind 严格检查允许字段。
    /// </summary>
    public sealed class SemanticOperation
    {
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
        public string Step { get; set; }

        public int Operation { get; set; }
    }
}
