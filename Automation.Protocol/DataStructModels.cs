using System.Collections.Generic;
using System.ComponentModel;
// 模块：Automation 协议 / 数据结构配置。
// 职责范围：定义数据结构整对象提交契约；字段 Description 会进入 MCP 参数 Schema。
// 排查入口：同名更新或类型失败时核对名称、完整 Items 列表和 Bridge 的数据结构校验结果。

namespace Automation.Protocol
{
    public sealed class DataStructDefinition
    {
        [Description("数据结构精确名称；同名对象存在时完整更新。")]
        public string Name { get; set; } = string.Empty;
        [Description("该数据结构的完整数据项列表。")]
        public List<DataStructItemDefinition> Items { get; set; } = new List<DataStructItemDefinition>();
    }

    public sealed class DataStructItemDefinition
    {
        [Description("数据项名称；同一数据结构内唯一。")]
        public string Name { get; set; } = string.Empty;
        [Description("该数据项的完整字段列表。")]
        public List<DataStructFieldDefinition> Fields { get; set; } = new List<DataStructFieldDefinition>();
    }

    public sealed class DataStructFieldDefinition
    {
        [Description("字段非负索引；同一数据项内唯一。")]
        public int Index { get; set; }
        [Description("字段名称。")]
        public string Name { get; set; } = string.Empty;
        [Description("严格枚举：Text 或 Number。")]
        public string Type { get; set; } = "Text";
        [Description("字段值文本；Number按InvariantCulture有限数解析。")]
        public string Value { get; set; } = string.Empty;
    }
}
