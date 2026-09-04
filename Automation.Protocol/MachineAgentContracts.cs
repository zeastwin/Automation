using System;

namespace Automation.Protocol
{
    /// <summary>Machine Agent 从流程内部入口执行时支持的受控模式。</summary>
    public static class MachineExecutionModes
    {
        /// <summary>只执行目标指令一次，适合补发一次握手输出、单个 IO 动作或工程验证。</summary>
        public const string SingleOperation = "single_operation";

        /// <summary>从目标指令进入，并沿原流程继续运行到流程自身结束。</summary>
        public const string ContinueFlow = "continue_flow";

        public static bool IsSupported(string value)
        {
            return string.Equals(value, SingleOperation, StringComparison.Ordinal)
                || string.Equals(value, ContinueFlow, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Machine Agent 指令入口预演请求。设备交互优先使用已确认节点技能的稳定 ID；
    /// 直接流程入口仅保留给无外部副作用的兼容诊断。
    /// </summary>
    public sealed class MachineProcessEntryPreviewRequest
    {
        /// <summary>已确认节点技能的稳定 ID。提供后，目标、模式和动作语义全部由技能绑定解析。</summary>
        public string SkillId { get; set; }

        /// <summary>兼容诊断入口的流程稳定 ID；使用 skillId 时只能省略或与技能绑定完全一致。</summary>
        public string ProcId { get; set; }

        /// <summary>兼容诊断入口的指令稳定 ID；使用 skillId 时只能省略或与技能绑定完全一致。</summary>
        public string OperationId { get; set; }

        /// <summary>兼容诊断入口模式；使用 skillId 时不能覆盖技能批准的模式。</summary>
        public string Mode { get; set; }

        /// <summary>兼容诊断入口目标；使用 skillId 时不能覆盖技能已确认语义。</summary>
        public string Objective { get; set; }

        /// <summary>兼容诊断入口预期结果；使用 skillId 时不能覆盖技能已确认语义。</summary>
        public string ExpectedOutcome { get; set; }
    }

    /// <summary>Machine Agent 请求停止一个当前运行实例的无副作用预演。</summary>
    public sealed class MachineProcessStopPreviewRequest
    {
        /// <summary>目标流程稳定 ID。</summary>
        public string ProcId { get; set; }

        /// <summary>本次停止的具体原因，供确认窗口与时间线审计。</summary>
        public string Reason { get; set; }
    }
}
