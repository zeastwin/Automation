using System.Collections.Generic;
using System.ComponentModel;

namespace Automation.Protocol
{
    /// <summary>
    /// 新建单个完整流程的声明式输入。MCP 会把它确定性编译为一个 ChangeSet V2 原子阶段。
    /// </summary>
    public sealed class ProcessBlueprintDefinition
    {
        public string Title { get; set; }

        [Description("必填的新流程基本信息。步骤和指令只能填写在steps中。")]
        public ProcessBlueprintProcess Process { get; set; }

        [Description("本流程需要的新建或复用变量；process作用域会自动绑定到本次新建流程。")]
        public List<ProcessBlueprintVariable> Variables { get; set; }

        [Description("按执行顺序排列的步骤；至少一项。")]
        public List<ProcessBlueprintStep> Steps { get; set; }
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

        [Description("变量作用域：public、process 或 system；process 会自动归属本次新建流程。")]
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

        [Description("按执行顺序排列的语义指令；未知动作或资源使用config.placeholder，不能用wait伪造。")]
        public List<SemanticOperation> Operations { get; set; }
    }
}
