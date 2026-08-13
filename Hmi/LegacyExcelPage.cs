using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed class LegacyExcelPage : Form
{
	private readonly LegacyFileBrowserControl browser;

	internal LegacyExcelPage()
	{
		Text = "UI_Excel";
		browser = new LegacyFileBrowserControl("Excel加载文件路径", "Excel显示文件类型", "D:\\AutomationLogs", openBinaryFiles: true)
		{
			Dock = DockStyle.Fill
		};
		base.Controls.Add(browser);
	}

	internal void AttachPlatform(IAutomationPlatform platform)
	{
		browser.AttachPlatform(platform);
	}

	internal void RefreshRuntimeView()
	{
		browser.EnsureLoaded();
	}
}


