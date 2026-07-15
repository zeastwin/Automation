using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    public sealed class AiStandardTestScenario
    {
        public AiStandardTestScenario(string id, string name, string description, params string[] prompts)
        {
            Id = id;
            Name = name;
            Description = description;
            Prompts = prompts;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<string> Prompts { get; }
    }

    public static class AiStandardTestSuite
    {
        private static readonly IReadOnlyList<AiStandardTestScenario> scenarios =
            new List<AiStandardTestScenario>
            {
                new AiStandardTestScenario(
                    "build",
                    "从零构建中型流程",
                    "验证规划、资源发现、分阶段提交和不确定信息处理。",
                    "创建一个产品检测流程：扫码获取产品编号，读取检测结果，合格则计数并放行，不合格则记录报警并等待人工处理。先完成可确定的部分，缺失资源允许暂时留空或占位。"),
                new AiStandardTestScenario(
                    "multi_process",
                    "多流程协作",
                    "验证跨流程引用、共享变量和任务拆分。",
                    "创建上料、检测、分拣三个相互配合的流程，共用“当前产品编号”和“检测结果”变量。先设计流程间的协作关系，再逐步完成配置并检查引用。"),
                new AiStandardTestScenario(
                    "focused_edit",
                    "精确局部修改",
                    "验证最小改动能力，避免误改指令顺序和跳转。",
                    "把当前选中流程中的检测超时从3秒改为5秒；超时后增加报警，但不要改变其他指令、顺序和跳转关系。"),
                new AiStandardTestScenario(
                    "diagnose_fix",
                    "控制流诊断与修复",
                    "先诊断、后修复，验证远距离跳转和稳定对象身份。",
                    "当前流程偶尔会重复执行放行指令，请检查跳转、条件分支和指令插入后的目标变化，定位原因。先报告问题，暂时不要修改。",
                    "按照刚才的结论修复，并验证所有跳转目标。"),
                new AiStandardTestScenario(
                    "iterative_refinement",
                    "多轮持续精修",
                    "验证同一会话中的事实复用和连续修改能力。",
                    "给当前流程增加失败重试，最多3次。",
                    "重试期间不要重复记录报警。",
                    "再增加人工跳过功能，但只能在流程停止时配置。",
                    "检查前面所有改动是否互相冲突。"),
                new AiStandardTestScenario(
                    "large_process",
                    "大体量流程检查",
                    "验证按需读取、长流程分析和分阶段修复。",
                    "检查当前流程中所有报警、变量写入、通讯调用和跳转关系，列出问题；只修复确定存在的问题，分阶段提交。")
            };

        public static IReadOnlyList<AiStandardTestScenario> Scenarios => scenarios;

        public static List<AiStandardTestScenario> Select(IEnumerable<string> ids)
        {
            var selectedIds = new HashSet<string>(ids ?? Enumerable.Empty<string>());
            return scenarios.Where(item => selectedIds.Contains(item.Id)).ToList();
        }
    }
}
