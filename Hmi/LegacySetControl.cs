using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed class LegacySetControl : UserControl
{
	private IAutomationPlatform platform;
	private readonly ComboBox timeZone;

	private readonly Label platformState;

	internal LegacySetControl()
	{
		BackColor = Color.White;
		timeZone = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Font = new Font("宋体", 11f)
		};
		foreach (TimeZoneInfo systemTimeZone in TimeZoneInfo.GetSystemTimeZones())
		{
			timeZone.Items.Add(systemTimeZone.Id);
		}
		timeZone.SelectedItem = TimeZoneInfo.Local.Id;
		timeZone.SelectionChangeCommitted += TimeZone_SelectionChangeCommitted;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			ColumnCount = 3,
			RowCount = 4,
			Dock = DockStyle.Fill
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 75f));
		tableLayoutPanel.Controls.Add(new Label
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 11f),
			Text = "TimeZone",
			TextAlign = ContentAlignment.MiddleCenter
		}, 0, 1);
		tableLayoutPanel.Controls.Add(timeZone, 1, 1);
		Button button = new Button
		{
			Dock = DockStyle.Fill,
			Font = new Font("微软雅黑", 11f),
			Text = "打开 Automation 平台（Alt+Z）"
		};
		button.Click += delegate
		{
			try
			{
				platform?.ShowPlatformEditor();
			}
			catch (Exception ex)
			{
				MessageBox.Show(FindForm(), ex.Message, "平台不可用", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		};
		tableLayoutPanel.Controls.Add(button, 1, 2);
		platformState = new Label
		{
			Dock = DockStyle.Fill,
			ForeColor = Color.DimGray,
			TextAlign = ContentAlignment.TopCenter
		};
		tableLayoutPanel.Controls.Add(platformState, 1, 3);
		base.Controls.Add(tableLayoutPanel);
	}

	internal void Attach(IAutomationPlatform platform)
	{
		this.platform = platform;
		RefreshView();
	}

	internal void RefreshView()
	{
		platformState.Text = ((platform == null) ? "平台未连接" : $"平台状态：{platform.RuntimeStatus}\r\n{platform.RuntimeMessage}");
	}

	private void TimeZone_SelectionChangeCommitted(object sender, EventArgs e)
	{
		string text = timeZone.SelectedItem?.ToString();
		if (string.IsNullOrWhiteSpace(text) || string.Equals(text, TimeZoneInfo.Local.Id, StringComparison.Ordinal))
		{
			return;
		}
		if (MessageBox.Show(FindForm(), "确定将 Windows 时区切换为“" + text + "”吗？", "设置 TimeZone", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			timeZone.SelectedItem = TimeZoneInfo.Local.Id;
			return;
		}
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "tzutil.exe",
				Arguments = "/s \"" + text + "\"",
				CreateNoWindow = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				WindowStyle = ProcessWindowStyle.Hidden
			};
			using (Process process = Process.Start(startInfo))
			{
				process.WaitForExit(5000);
				if (!process.HasExited || process.ExitCode != 0)
				{
					string text2 = (process.HasExited ? process.StandardError.ReadToEnd() : "设置时区超时。");
					throw new InvalidOperationException(string.IsNullOrWhiteSpace(text2) ? "tzutil 返回失败。" : text2.Trim());
				}
			}
			TimeZoneInfo.ClearCachedData();
			timeZone.SelectedItem = TimeZoneInfo.Local.Id;
		}
		catch (Exception ex)
		{
			timeZone.SelectedItem = TimeZoneInfo.Local.Id;
			MessageBox.Show(FindForm(), ex.Message, "设置时区失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}
}

