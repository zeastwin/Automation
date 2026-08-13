using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed class LegacyLogPage : Form
{
	private readonly LegacyFileBrowserControl productLog;

	private readonly LegacyFileBrowserControl platformLog;

	internal LegacyLogPage()
	{
		Text = "LogForm";
		TabControl tabControl = new TabControl
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 12f)
		};
		TabPage tabPage = new TabPage("ProductLog");
		TabPage tabPage2 = new TabPage("TerraceLog");
		productLog = new LegacyFileBrowserControl("Log加载文件路径", "Log显示文件类型", Path.Combine("D:\\AutomationLogs", "Hmi", "Equipment"), openBinaryFiles: false)
		{
			Dock = DockStyle.Fill
		};
		platformLog = new LegacyFileBrowserControl(null, null, "D:\\AutomationLogs", openBinaryFiles: false)
		{
			Dock = DockStyle.Fill
		};
		tabPage.Controls.Add(productLog);
		tabPage2.Controls.Add(platformLog);
		tabControl.TabPages.Add(tabPage);
		tabControl.TabPages.Add(tabPage2);
		tabControl.SelectedIndexChanged += delegate
		{
			RefreshRuntimeView();
		};
		base.Controls.Add(tabControl);
	}

	internal void AttachPlatform(IAutomationPlatform platform)
	{
		productLog.AttachPlatform(platform);
		platformLog.AttachPlatform(platform);
	}

	internal void RefreshRuntimeView()
	{
		productLog.EnsureLoaded();
		platformLog.EnsureLoaded();
	}
}


