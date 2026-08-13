using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed partial class LegacyDataViewControl : UserControl
{
	private IAutomationPlatform platform;

	private EquipmentProcessMessageService processMessages;

	private bool showNg;

	internal LegacyDataViewControl()
	{
		InitializeComponent();
		UpdateModeButtons();
	}

	private void UphButton_Click(object sender, EventArgs e)
	{
		showNg = false;
		RefreshRuntimeView();
	}

	private void NgButton_Click(object sender, EventArgs e)
	{
		showNg = true;
		RefreshRuntimeView();
	}

	internal void AttachPlatform(IAutomationPlatform platform, EquipmentProcessMessageService processMessages)
	{
		this.platform = platform;
		this.processMessages = processMessages;
		RefreshRuntimeView();
	}

	internal void RefreshRuntimeView()
	{
		UpdateModeButtons();
		if (platform == null)
		{
			return;
		}
		string[] source = ((!showNg) ? new string[5] { "UPH", "CT", "产能", "良率", "产品信息" } : new string[5] { "NG", "Tossing", "抛料", "不良", "缺陷" });
		List<LegacyValueRow> list = new List<LegacyValueRow>();
		foreach (string name in platform.Values.GetNames())
		{
			if (source.Any((string keyword) => name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) && platform.Values.TryGet(name, out var value, out var _) && value != null)
			{
				list.Add(new LegacyValueRow
				{
					Name = name,
					Value = value.Value,
					Type = value.Type,
					Note = value.Note
				});
			}
		}
		grid.DataSource = new BindingList<LegacyValueRow>(list);
		EquipmentProcessMessageSnapshot equipmentProcessMessageSnapshot = processMessages?.GetSnapshot();
		if (equipmentProcessMessageSnapshot == null)
		{
			summaryChart.SetValues(showNg ? "Tossing/NG" : "UPH/CT", Array.Empty<KeyValuePair<string, double>>());
		}
		else if (showNg)
		{
			summaryChart.SetValues("Tossing/NG", new KeyValuePair<string, double>[2]
			{
				new KeyValuePair<string, double>("OK", equipmentProcessMessageSnapshot.GoodTotal),
				new KeyValuePair<string, double>("NG", equipmentProcessMessageSnapshot.DefectTotal)
			});
		}
		else
		{
			double value2 = ((equipmentProcessMessageSnapshot.LastCycleSeconds.GetValueOrDefault() > 0.0) ? (3600.0 / equipmentProcessMessageSnapshot.LastCycleSeconds.Value) : 0.0);
			summaryChart.SetValues("UPH/CT", new KeyValuePair<string, double>[3]
			{
				new KeyValuePair<string, double>("UPH", value2),
				new KeyValuePair<string, double>("CT", equipmentProcessMessageSnapshot.LastCycleSeconds.GetValueOrDefault()),
				new KeyValuePair<string, double>("产出", equipmentProcessMessageSnapshot.OutputTotal)
			});
		}
	}

	private void UpdateModeButtons()
	{
		uphButton.BackColor = (showNg ? Color.Gainsboro : Color.GreenYellow);
		ngButton.BackColor = (showNg ? Color.GreenYellow : Color.Gainsboro);
	}

}

