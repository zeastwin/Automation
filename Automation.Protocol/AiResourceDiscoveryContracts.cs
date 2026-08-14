using System.Collections.Generic;
using System.ComponentModel;

namespace Automation.Protocol
{
    public static class ProjectResourceDiscoveryKinds
    {
        public const string SupportedKinds = "process、io、variable、station、point、data_struct、alarm、communication、plc";
    }

    public static class ProjectResourceResolutionStatuses
    {
        public const string Exact = "exact";
        public const string Candidate = "candidate";
        public const string Ambiguous = "ambiguous";
        public const string Missing = "missing";
    }

    public sealed class ProjectResourceDiscoveryQuery
    {
        [Description("资源类别。严格枚举：" + ProjectResourceDiscoveryKinds.SupportedKinds + "。")]
        public string Kind { get; set; }

        [Description("1..6个普通文本名称线索；按任一关键词命中并去重。空字符串和*不表示全量，必须提供真实线索。")]
        public List<string> Keywords { get; set; }

        [Description("kind=io时可选：通用输入或通用输出；其他类别省略。")]
        public string IoType { get; set; }

        [Description("kind=point时必填：点位所属工站索引；其他类别省略。")]
        public int? StationIndex { get; set; }
    }

    /// <summary>
    /// 流程编写阶段的单个绑定意图。Names 是同一资源的名称或别名，不能把多个独立资源塞进一项。
    /// </summary>
    public sealed class AuthoringInputRequirement
    {
        [Description("本阶段内稳定且唯一的用途键，例如scanResult或partPresent；用于把解析结果对应回蓝图字段。")]
        public string Key { get; set; }

        [Description("资源类别。严格枚举：" + ProjectResourceDiscoveryKinds.SupportedKinds + "。")]
        public string Kind { get; set; }

        [Description("1..4个指向同一个资源的精确名称或别名；每个独立资源必须使用单独requirement。")]
        public List<string> Names { get; set; }

        [Description("该资源在当前功能块中的用途，例如读取扫码结果、写入完成标志或等待到位反馈。")]
        public string Purpose { get; set; }

        [Description("kind=io时可选：通用输入或通用输出；其他类别省略。")]
        public string IoType { get; set; }

        [Description("kind=variable时可选：要求double或string；其他类别省略。")]
        public string RequiredType { get; set; }

        [Description("kind=variable时可选：要求public、process或system；其他类别省略。")]
        public string RequiredScope { get; set; }

        [Description("kind=variable且requiredScope=process时可选：要求归属的流程稳定ID。")]
        public string OwnerProcId { get; set; }

        [Description("kind=point时必填：点位所属工站索引；其他类别省略。")]
        public int? StationIndex { get; set; }
    }

    public sealed class OperationCapabilityIntent
    {
        [Description("本阶段内稳定且唯一的动作键，例如scanCode或waitCylinderReady。")]
        public string Key { get; set; }

        [Description("业务动作的简短自然语言描述；平台据此返回真实可用能力候选，不把描述直接当作operaType。")]
        public string Intent { get; set; }
    }
}
