using System.Collections.Generic;
using System.ComponentModel;

namespace Automation.Protocol
{
    /// <summary>
    /// 新建单个流程当前创建阶段的声明式输入。MCP 会把它确定性编译为一个 ChangeSet V2 原子阶段。
    /// </summary>
    public sealed class ProcessBlueprintDefinition
    {
        public string Title { get; set; }

        [Description("必填的新流程基本信息。步骤和指令只能填写在steps中。")]
        public ProcessBlueprintProcess Process { get; set; }

        [Description("本流程需要的新建或复用变量；process作用域会自动绑定到本次新建流程。")]
        public List<ProcessBlueprintVariable> Variables { get; set; }

        [Description("按执行顺序排列的步骤；至少一项。简单流程可一次给出完整实现；复杂流程可先用config.placeholder表达待补齐功能块，提交安全骨架后再按稳定ID分阶段编辑。")]
        public List<ProcessBlueprintStep> Steps { get; set; }

        [Description("蓝图中的标准重试事务；调用方声明业务入口、判定、总尝试次数和业务变量清理，内部计数器及其复位、累加由编译器生成。")]
        public List<ProcessBlueprintRetryPolicy> Retries { get; set; }
    }

    public sealed class ProcessBlueprintProcess
    {
        public string Name { get; set; }
        public bool? AutoStart { get; set; }
        public bool? Disable { get; set; }
    }

    public sealed class ProcessBlueprintVariable
    {
        public string Name { get; set; }

        [Description("变量作用域：public、process 或 system；process 会自动归属本次新建流程。计数器、阶段标记和临时结果等流程内部状态使用process；只有外部流程/HMI确实需要访问的接口变量才使用public。")]
        public string Scope { get; set; }

        public int? Index { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string Note { get; set; }
        public string Policy { get; set; }
    }

    public sealed class ProcessBlueprintStep
    {
        [Description("可选符号key；跨指令跳转目标需要引用本步骤时提供。省略则由编译器生成。")]
        public string Key { get; set; }

        public string Name { get; set; }
        public bool? Disable { get; set; }

        [Description("按执行顺序排列的语义指令；未知动作、资源或复杂流程中待后续补齐的功能块使用config.placeholder，不能用wait伪造。")]
        public List<SemanticOperation> Operations { get; set; }
    }

    public sealed class ProcessBlueprintRetryPolicy
    {
        [Description("本次尝试的第一条指令key；首次尝试和重试都必须从这里进入，不能跳过复位或清理。")]
        public string EntryOperationKey { get; set; }

        [Description("编译器生成的流程内部double计数变量；调用方省略，不能绑定或改造现有公共变量。")]
        public string CounterVariable { get; set; }

        [Description("总尝试次数，包含首次尝试；例如失败最多重试3次填写4。范围1..100。")]
        public int MaxAttempts { get; set; }

        [Description("决定是否重试的branch.number_compare指令key；调用方保留失败出口，编译器填充内部计数比较和回跳目标。")]
        public string RetryDecisionOperationKey { get; set; }

        [Description("每次尝试前需要复位为0的业务状态变量；编译器生成对应variable.set。")]
        public List<string> ResetVariables { get; set; }

        [Description("每次尝试前需要清空的string结果缓存；编译器生成对应variable.clear，没有时省略。")]
        public List<string> ClearVariables { get; set; }
    }
}
