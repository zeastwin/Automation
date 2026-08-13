using System.ComponentModel;

namespace Automation.Hmi;

internal sealed class LegacyDatabaseValueRow
{
	[DisplayName("Name")]
	public string Name { get; set; }

	[DisplayName("Value")]
	public string Value { get; set; }

	[DisplayName("Type")]
	public string Type { get; set; }

	[DisplayName("Scope")]
	public string Scope { get; set; }

	[DisplayName("Note")]
	public string Note { get; set; }

	[Browsable(false)]
	public bool Dirty { get; set; }
}


