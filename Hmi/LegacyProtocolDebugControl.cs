using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed class LegacyProtocolDebugControl : UserControl
{
	private readonly string systemName;
	private IAutomationPlatform platform;
	private EquipmentProcessMessageService processMessages;

	private readonly Label titleLabel;

	private readonly TextBox urlText;

	private readonly ComboBox functionBox;

	private readonly ListView registerList;

	private readonly TextBox sendText;

	private readonly TextBox receiveText;

	internal LegacyProtocolDebugControl(string systemName)
	{
		this.systemName = systemName;
		BackColor = Color.White;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			ColumnCount = 3,
			RowCount = 1,
			Dock = DockStyle.Fill,
			Padding = new Padding(5)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		GroupBox groupBox = new GroupBox
		{
			Dock = DockStyle.Fill
		};
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			ColumnCount = 1,
			RowCount = 6,
			Dock = DockStyle.Fill,
			Padding = new Padding(4)
		};
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		titleLabel = new Label
		{
			Dock = DockStyle.Fill,
			Font = new Font("微软雅黑", 24f, FontStyle.Bold),
			Text = systemName,
			TextAlign = ContentAlignment.MiddleCenter
		};
		urlText = new TextBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 10.5f)
		};
		urlText.Validated += delegate
		{
			SaveUrl();
		};
		functionBox = new ComboBox
		{
			Dock = DockStyle.Fill,
			DropDownStyle = ComboBoxStyle.DropDownList,
			Font = new Font("宋体", 10.5f)
		};
		Button button = CreateButton((systemName == "HIVE") ? "设备选择" : "数据发送");
		Button button2 = CreateButton((systemName == "HIVE") ? "更新设备状态" : "数据转换");
		button.Click += delegate
		{
			ExecuteSelectedFunction();
		};
		button2.Click += delegate
		{
			ExecuteSecondaryFunction();
		};
		tableLayoutPanel2.Controls.Add(titleLabel, 0, 0);
		tableLayoutPanel2.Controls.Add(CreateLabeledControl("URL：", urlText), 0, 1);
		tableLayoutPanel2.Controls.Add(functionBox, 0, 2);
		tableLayoutPanel2.Controls.Add(button, 0, 3);
		tableLayoutPanel2.Controls.Add(button2, 0, 4);
		tableLayoutPanel2.Controls.Add(new Panel
		{
			Dock = DockStyle.Fill
		}, 0, 5);
		groupBox.Controls.Add(tableLayoutPanel2);
		registerList = new ListView
		{
			Dock = DockStyle.Fill,
			FullRowSelect = true,
			GridLines = true,
			HideSelection = false,
			View = View.Details
		};
		registerList.Columns.Add("变量", 180);
		registerList.Columns.Add("值", 140);
		registerList.ItemActivate += RegisterList_ItemActivate;
		GroupBox groupBox2 = new GroupBox
		{
			Dock = DockStyle.Fill,
			Text = "寄存器"
		};
		groupBox2.Controls.Add(registerList);
		GroupBox groupBox3 = new GroupBox
		{
			Dock = DockStyle.Fill,
			Text = "发送&接收"
		};
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			ColumnCount = 1,
			RowCount = 2,
			Dock = DockStyle.Fill
		};
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
		sendText = new TextBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Consolas", 10f),
			Multiline = true,
			ScrollBars = ScrollBars.Both
		};
		receiveText = new TextBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Consolas", 10f),
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Both
		};
		tableLayoutPanel3.Controls.Add(sendText, 0, 0);
		tableLayoutPanel3.Controls.Add(receiveText, 0, 1);
		groupBox3.Controls.Add(tableLayoutPanel3);
		tableLayoutPanel.Controls.Add(groupBox, 0, 0);
		tableLayoutPanel.Controls.Add(groupBox2, 1, 0);
		tableLayoutPanel.Controls.Add(groupBox3, 2, 0);
		base.Controls.Add(tableLayoutPanel);
	}

	internal void Attach(
		IAutomationPlatform platform,
		EquipmentProcessMessageService processMessages)
	{
		this.platform = platform;
		this.processMessages = processMessages;
		PopulateFunctions();
		RefreshView();
	}

	internal void RefreshView()
	{
		if (platform == null)
		{
			return;
		}
		string b = ((registerList.SelectedItems.Count > 0) ? registerList.SelectedItems[0].Text : string.Empty);
		registerList.BeginUpdate();
		registerList.Items.Clear();
		foreach (string item in platform.Values.GetNames().Where(IsSystemVariable).OrderBy((string item) => item, StringComparer.OrdinalIgnoreCase))
		{
			if (platform.Values.TryGet(item, out var value, out var _) && value != null)
			{
				ListViewItem listViewItem = new ListViewItem(item);
				listViewItem.SubItems.Add(value.Value ?? string.Empty);
				registerList.Items.Add(listViewItem);
				if (string.Equals(item, b, StringComparison.Ordinal))
				{
					listViewItem.Selected = true;
				}
			}
		}
		registerList.EndUpdate();
		if (!urlText.Focused)
		{
			string text = platform.Values.GetNames().FirstOrDefault((string name) => IsSystemVariable(name) && name.IndexOf("URL", StringComparison.OrdinalIgnoreCase) >= 0);
			urlText.Tag = text;
			urlText.Text = ((text != null && TryRead(text, out var value2)) ? value2 : string.Empty);
		}
		EquipmentProcessMessageSnapshot equipmentProcessMessageSnapshot = processMessages?.GetSnapshot();
		if (equipmentProcessMessageSnapshot != null)
		{
			receiveText.Text = string.Join(Environment.NewLine, equipmentProcessMessageSnapshot.Logs.Where((string line) => line.IndexOf(systemName, StringComparison.OrdinalIgnoreCase) >= 0).TakeLastCompatible(100));
		}
	}

	private void PopulateFunctions()
	{
		functionBox.Items.Clear();
		if (processMessages == null)
		{
			return;
		}
		string prefix = ((systemName == "HIVE") ? "HIVE" : systemName);
		foreach (string item in from name in processMessages.GetRegisteredFunctionNames()
			where name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			select name)
		{
			functionBox.Items.Add(item);
		}
		if (functionBox.Items.Count > 0)
		{
			functionBox.SelectedIndex = 0;
		}
	}

	private bool IsSystemVariable(string name)
	{
		return name.IndexOf(systemName, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private void SaveUrl()
	{
		if (urlText.Tag is string text && !string.IsNullOrWhiteSpace(text))
		{
			SetValue(text, urlText.Text);
		}
	}

	private void ExecuteSelectedFunction()
	{
		if (processMessages == null || !(functionBox.SelectedItem is string message))
		{
			return;
		}
		sendText.Text = message;
		try
		{
			processMessages.ExecuteMessage(message);
			RefreshView();
		}
		catch (Exception ex)
		{
			receiveText.Text = ex.Message;
		}
	}

	private void ExecuteSecondaryFunction()
	{
		if (processMessages != null)
		{
			string keyword = ((systemName == "HIVE") ? "更新设备状态" : "数据");
			string text = processMessages.GetRegisteredFunctionNames().FirstOrDefault((string name) => name.StartsWith(systemName, StringComparison.OrdinalIgnoreCase) && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
			if (text == null)
			{
				ExecuteSelectedFunction();
				return;
			}
			functionBox.SelectedItem = text;
			ExecuteSelectedFunction();
		}
	}

	private void RegisterList_ItemActivate(object sender, EventArgs e)
	{
		if (registerList.SelectedItems.Count == 0)
		{
			return;
		}
		ListViewItem listViewItem = registerList.SelectedItems[0];
		using LegacyValueInputDialog legacyValueInputDialog = new LegacyValueInputDialog(listViewItem.Text, listViewItem.SubItems[1].Text);
		if (legacyValueInputDialog.ShowDialog(FindForm()) == DialogResult.OK)
		{
			SetValue(listViewItem.Text, legacyValueInputDialog.Value);
			RefreshView();
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

	private void SetValue(string name, object value)
	{
		if (platform != null && !platform.Values.Set(name, value, out string error))
		{
			MessageBox.Show(FindForm(), error, "变量写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private static Control CreateLabeledControl(string label, Control control)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			ColumnCount = 1,
			RowCount = 2,
			Dock = DockStyle.Fill
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(new Label
		{
			Dock = DockStyle.Fill,
			Text = label,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, 0);
		tableLayoutPanel.Controls.Add(control, 0, 1);
		return tableLayoutPanel;
	}

	private static Button CreateButton(string text)
	{
		return new Button
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 10.5f),
			Text = text,
			UseVisualStyleBackColor = true
		};
	}
}
