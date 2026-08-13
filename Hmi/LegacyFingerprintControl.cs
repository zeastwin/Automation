using System;
using System.Drawing;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed class LegacyFingerprintControl : UserControl
{
	private IAutomationPlatform platform;
	private readonly TextBox userName;

	private readonly TextBox password;

	private readonly Label status;

	internal LegacyFingerprintControl()
	{
		BackColor = Color.White;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			ColumnCount = 3,
			RowCount = 3,
			Dock = DockStyle.Fill
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 18f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 64f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 18f));
		GroupBox groupBox = new GroupBox
		{
			Dock = DockStyle.Fill,
			Text = "用户登录 / 指纹录取"
		};
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			ColumnCount = 2,
			RowCount = 6,
			Dock = DockStyle.Fill,
			Padding = new Padding(25)
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		userName = new TextBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 12f)
		};
		password = new TextBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 12f),
			UseSystemPasswordChar = true
		};
		tableLayoutPanel2.Controls.Add(CreateLabel("用户"), 0, 0);
		tableLayoutPanel2.Controls.Add(userName, 1, 0);
		tableLayoutPanel2.Controls.Add(CreateLabel("密码"), 0, 1);
		tableLayoutPanel2.Controls.Add(password, 1, 1);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill
		};
		flowLayoutPanel.Controls.Add(new RadioButton
		{
			Text = "操作员",
			Checked = true,
			AutoSize = true
		});
		flowLayoutPanel.Controls.Add(new RadioButton
		{
			Text = "管理员",
			AutoSize = true
		});
		flowLayoutPanel.Controls.Add(new RadioButton
		{
			Text = "工程师",
			AutoSize = true
		});
		tableLayoutPanel2.SetColumnSpan(flowLayoutPanel, 2);
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 2);
		Button button = new Button
		{
			Dock = DockStyle.Fill,
			Text = "登录",
			Font = new Font("宋体", 11f)
		};
		Button button2 = new Button
		{
			Dock = DockStyle.Fill,
			Text = "指纹录取",
			Font = new Font("宋体", 11f)
		};
		button.Click += AuthenticationUnavailable_Click;
		button2.Click += AuthenticationUnavailable_Click;
		tableLayoutPanel2.SetColumnSpan(button, 2);
		tableLayoutPanel2.SetColumnSpan(button2, 2);
		tableLayoutPanel2.Controls.Add(button, 0, 3);
		tableLayoutPanel2.Controls.Add(button2, 0, 4);
		status = new Label
		{
			Dock = DockStyle.Fill,
			ForeColor = Color.Firebrick,
			Text = "新平台尚未公开用户认证/指纹设备接口，界面保留但不会伪造登录成功。",
			TextAlign = ContentAlignment.MiddleCenter
		};
		tableLayoutPanel2.SetColumnSpan(status, 2);
		tableLayoutPanel2.Controls.Add(status, 0, 5);
		groupBox.Controls.Add(tableLayoutPanel2);
		tableLayoutPanel.Controls.Add(groupBox, 1, 1);
		base.Controls.Add(tableLayoutPanel);
	}

	internal void Attach(IAutomationPlatform platform)
	{
		this.platform = platform;
		RefreshView();
	}

	internal void RefreshView()
	{
		if (TryRead("登录用户名称", out var value) && !string.IsNullOrWhiteSpace(value))
		{
			status.Text = "当前用户：" + value;
			status.ForeColor = Color.DarkGreen;
		}
	}

	private bool TryRead(string name, out string value)
	{
		value = string.Empty;
		return platform != null
			&& platform.Values.TryGet(name, out ValueSnapshot snapshot, out _)
			&& snapshot != null
			&& (value = snapshot.Value ?? string.Empty) != null;
	}

	private void AuthenticationUnavailable_Click(object sender, EventArgs e)
	{
		MessageBox.Show(FindForm(), "新平台公开契约中没有用户认证或指纹设备接口，不能绕过认证直接写入登录状态。", "认证接口未配置", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private static Label CreateLabel(string text)
	{
		return new Label
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 12f),
			Text = text,
			TextAlign = ContentAlignment.MiddleCenter
		};
	}
}

