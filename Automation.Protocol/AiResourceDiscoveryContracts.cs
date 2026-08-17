using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Automation.Protocol
{
    public static class AuthoringResourceTypes
    {
        public const string SupportedTypes =
            "motion、io_input、io_output、variable、communication、plc、alarm、process、data_struct";
    }

    /// <summary>
    /// 作者资源目录返回的机器引用。引用只表达当前配置对象身份，不附加业务角色、
    /// 电气极性或安全语义；展示名称变化时，物理 IO 引用仍保持稳定。
    /// </summary>
    public static class AuthoringResourceRefs
    {
        public static string ForIo(
            string ioType,
            int cardNum,
            int module,
            string ioIndex)
        {
            string category = string.Equals(ioType, "通用输入", StringComparison.Ordinal)
                ? "io_input"
                : string.Equals(ioType, "通用输出", StringComparison.Ordinal)
                    ? "io_output"
                    : "io";
            return category + ":" + cardNum + ":" + module + ":"
                + Uri.EscapeDataString((ioIndex ?? string.Empty).Trim());
        }

        public static string ForStableId(string resourceType, string stableId)
        {
            string type = (resourceType ?? string.Empty).Trim();
            string id = (stableId ?? string.Empty).Trim();
            if (type.Length == 0 || id.Length == 0) return string.Empty;
            return type + ":" + Uri.EscapeDataString(id);
        }
    }

    /// <summary>
    /// 流程编写阶段的资源目录请求。名称过滤是可选的：模型可以先按类别查看现场，
    /// 再根据返回的真实名称和结构决定是否需要缩小范围。
    /// </summary>
    public sealed class AuthoringResourceListRequest
    {
        [Description("资源类别。严格枚举：" + AuthoringResourceTypes.SupportedTypes + "。")]
        public string Type { get; set; }

        [Description("可选名称过滤；省略时在有界结果内罗列该类别的当前资源，不要求模型预先猜名称。")]
        public string NameLike { get; set; }

        [Description("该类别的分页起点，默认0；仅在上一结果hasMore=true时继续读取。motion按全部匹配点位的顺序分页，工站和轴摘要每页都会保留。")]
        public int? Offset { get; set; }
    }

    public sealed class OperationCapabilityIntent
    {
        [Description("本阶段内稳定且唯一的动作键，例如scanCode或waitCylinderReady。")]
        public string Key { get; set; }

        [Description("业务动作的简短自然语言描述；平台据此返回真实可用能力候选，不把描述直接当作operaType。")]
        public string Intent { get; set; }
    }

    /// <summary>
    /// 语义指令的自然语言入口目录。这里只描述“业务表达通常对应哪个现有语义 kind”，
    /// 字段、运行行为和资源合法性仍由 ChangeSet Schema、编译器与行为契约决定。
    /// </summary>
    public static class SemanticOperationIntentCatalog
    {
        private static readonly IReadOnlyDictionary<string, string[]> Aliases =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["string.clear"] = new[] { "清空", "置空", "清除文本", "清除字符串" },
                ["number.zero"] = new[] { "清零", "归零", "置零" },
                ["variable.set"] = new[] { "赋值", "设置变量", "写变量" },
                ["variable.copy"] = new[] { "复制变量", "拷贝变量" },
                ["variable.add"] = new[] { "累加", "计数" },
                ["variable.compute"] = new[] { "计算", "运算" },
                ["wait"] = new[] { "延时", "等待时间", "节拍" },
                ["flow.goto"] = new[] { "跳转", "转到" },
                ["flow.end"] = new[] { "结束流程", "正常结束" },
                ["branch.number_compare"] = new[] { "数值比较", "次数判断", "大于", "小于", "等于" },
                ["branch.number_range"] = new[] { "数值范围", "区间" },
                ["branch.io"] = new[] { "IO判断", "输入判断", "信号判断" },
                ["alarm.raise"] = new[] { "报警", "故障提示" },
                ["popup.message"] = new[] { "弹框", "普通提示" },
                ["io.write"] = new[]
                {
                    "写IO", "输出信号", "置位输出", "复位输出", "气缸伸出", "气缸缩回",
                    "气缸前进", "气缸后退", "夹爪夹紧", "夹爪松开", "打开阀", "关闭阀",
                    "开启真空", "关闭真空", "破真空"
                },
                ["io.wait"] = new[]
                {
                    "等待IO", "等待输入", "等待反馈", "等待气缸", "等待夹爪",
                    "气缸到位反馈", "夹爪到位反馈", "真空反馈"
                },
                ["process.control"] = new[] { "启动子流程", "停止子流程", "控制流程" },
                ["process.wait"] = new[] { "等待子流程", "等待流程" }
            };

        public static string[] ResolveCandidates(string intent)
        {
            string normalized = Normalize(intent);
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> entry in Aliases)
            {
                if (entry.Value.Any(alias => normalized.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0))
                    candidates.Add(entry.Key);
            }
            if ((normalized.IndexOf("复制", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.IndexOf("拷贝", StringComparison.OrdinalIgnoreCase) >= 0)
                && normalized.IndexOf("变量", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                candidates.Add("variable.copy");
            }
            if (normalized.IndexOf("结构体", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("数据结构", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                candidates.Remove("variable.copy");
            }
            if (normalized.IndexOf("等待", StringComparison.OrdinalIgnoreCase) >= 0
                && (normalized.IndexOf("信号", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.IndexOf("输入", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.IndexOf("反馈", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                candidates.Add("io.wait");
            }
            return candidates.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private static string Normalize(string intent)
        {
            string value = (intent ?? string.Empty).Trim();
            foreach (string filler in new[] { " ", "\t", "\r", "\n", "工件", "物料", "载具", "设备" })
                value = value.Replace(filler, string.Empty);
            return value;
        }
    }
}
