using System.ComponentModel;

namespace Automation.Hmi;

internal sealed class LegacyToolProductRow
{
	[DisplayName("时间")]
	public string Time { get; set; }

	[DisplayName("SN")]
	public string SN { get; set; }

	[DisplayName("流程信息")]
	public string ProcessInfo { get; set; }

	[DisplayName("结果")]
	public string Result { get; set; }

	[DisplayName("模式")]
	public string Mode { get; set; }
}


