using System;

namespace Automation.Hmi;

internal sealed class LegacyProductionHistoryRow
{
	public DateTime Time { get; set; }

	public string SN { get; set; }

	public string ProcessInfo { get; set; }

	public string InfoData { get; set; }

	public string Mode { get; set; }

	public bool IsFailure { get; set; }
}


