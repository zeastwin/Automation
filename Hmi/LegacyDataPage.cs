using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed partial class LegacyDataPage : Form
{
	internal LegacyDataPage()
	{
		InitializeComponent();
	}

	private void DataTabs_SelectedIndexChanged(object sender, System.EventArgs e)
	{
		RefreshRuntimeView();
	}

	internal void AttachPlatform(IAutomationPlatform platform, EquipmentProcessMessageService processMessages)
	{
		dataView.AttachPlatform(platform, processMessages);
		productData.AttachPlatform(platform, processMessages);
	}

	internal void RefreshRuntimeView()
	{
		dataView.RefreshRuntimeView();
		productData.RefreshRuntimeView();
	}
}

