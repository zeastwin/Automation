using System.ComponentModel;

namespace Automation.Hmi;

internal sealed class LegacyValueRow
{
	[DisplayName("变量")]
	public string Name { get; set; }

	[DisplayName("当前值")]
	public string Value { get; set; }

	[DisplayName("类型")]
	public string Type { get; set; }

	[DisplayName("说明")]
	public string Note { get; set; }
}


