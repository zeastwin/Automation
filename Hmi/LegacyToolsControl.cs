using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed class LegacyToolsControl : UserControl
{
	private IAutomationPlatform platform;
	private EquipmentProcessMessageService processMessages;
	private readonly DateTimePicker startPicker;

	private readonly DateTimePicker endPicker;

	private readonly ComboBox functionBox;

	private readonly DataGridView resultGrid;

	internal LegacyToolsControl()
	{
		BackColor = Color.White;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			ColumnCount = 1,
			RowCount = 2,
			Dock = DockStyle.Fill,
			Padding = new Padding(5)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			ColumnCount = 6,
			RowCount = 2,
			Dock = DockStyle.Fill
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
		tableLayoutPanel2.Controls.Add(CreateToolbarLabel("开始时间"), 0, 0);
		tableLayoutPanel2.Controls.Add(CreateToolbarLabel("结束时间"), 2, 0);
		startPicker = CreateDatePicker();
		endPicker = CreateDatePicker();
		startPicker.Value = DateTime.Today.AddDays(-7.0);
		tableLayoutPanel2.Controls.Add(startPicker, 1, 0);
		tableLayoutPanel2.Controls.Add(endPicker, 3, 0);
		functionBox = new ComboBox
		{
			Dock = DockStyle.Fill,
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		functionBox.Items.AddRange(new object[4] { "查询NG产品", "MES_查询", "MES_过站", "PDCA补传" });
		functionBox.SelectedIndex = 0;
		tableLayoutPanel2.Controls.Add(functionBox, 4, 0);
		Button button = new Button
		{
			Dock = DockStyle.Fill,
			Text = "查询"
		};
		button.Click += delegate
		{
			Search();
		};
		tableLayoutPanel2.Controls.Add(button, 5, 0);
		tableLayoutPanel2.SetColumnSpan(button, 1);
		Label control = new Label
		{
			Dock = DockStyle.Fill,
			ForeColor = Color.DimGray,
			Text = "右键产品行可执行：清空数据 / MES_查询 / MES_过站 / PDCA补传",
			TextAlign = ContentAlignment.MiddleLeft
		};
		tableLayoutPanel2.SetColumnSpan(control, 6);
		tableLayoutPanel2.Controls.Add(control, 0, 1);
		resultGrid = new DataGridView
		{
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			BackgroundColor = Color.White,
			Dock = DockStyle.Fill,
			ReadOnly = true,
			RowHeadersVisible = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect
		};
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			Items = 
			{
				{
					"清空数据",
					(Image)null,
					(EventHandler)delegate
					{
						resultGrid.DataSource = null;
					}
				},
				{
					"MES_查询",
					(Image)null,
					(EventHandler)delegate
					{
						ExecuteForSelected("MES流程信息||消息 进站MES查询", "SN_Code-进站位");
					}
				},
				{
					"MES_过站",
					(Image)null,
					(EventHandler)delegate
					{
						ExecuteForSelected("MES流程信息||消息 MES过站", "SN_Code-出站位");
					}
				},
				{
					"PDCA补传",
					(Image)null,
					(EventHandler)delegate
					{
						ExecuteForSelected("PDCA流程信息||消息 PDCA上传", "PDCA上传SN");
					}
				}
			}
		};
		resultGrid.ContextMenuStrip = contextMenuStrip;
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.Controls.Add(CreateGroup("NG产品", resultGrid), 0, 1);
		base.Controls.Add(tableLayoutPanel);
	}

	internal void Attach(
		IAutomationPlatform platform,
		EquipmentProcessMessageService processMessages)
	{
		this.platform = platform;
		this.processMessages = processMessages;
		RefreshView();
	}

	internal void RefreshView()
	{
	}

	private void Search()
	{
		List<LegacyToolProductRow> list = new List<LegacyToolProductRow>();
		DateTime date = startPicker.Value.Date;
		DateTime date2 = endPicker.Value.Date;
		if (date2 < date)
		{
			MessageBox.Show(FindForm(), "结束时间不能早于开始时间。", "查询", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		DateTime dateTime = date;
		while (dateTime <= date2)
		{
			string path = Path.Combine("D:\\AutomationLogs", "Hmi", "Equipment", "Production", dateTime.ToString("yyyyMMdd") + "_Output.csv");
			if (File.Exists(path))
			{
				using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
				streamReader.ReadLine();
				string line;
				while ((line = streamReader.ReadLine()) != null)
				{
					List<string> list2 = LegacyCsv.Parse(line);
					if (list2.Count == 5 && (list2[3].IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0 || list2[3].Contains("12")))
					{
						list.Add(new LegacyToolProductRow
						{
							Time = list2[0],
							SN = list2[1],
							ProcessInfo = list2[2],
							Result = list2[3],
							Mode = list2[4]
						});
					}
				}
			}
			dateTime = dateTime.AddDays(1.0);
		}
		resultGrid.DataSource = new BindingList<LegacyToolProductRow>(list);
	}

	private void ExecuteForSelected(string functionName, string snVariable)
	{
		if (resultGrid.SelectedRows.Count == 0 || !(resultGrid.SelectedRows[0].DataBoundItem is LegacyToolProductRow legacyToolProductRow) || processMessages == null)
		{
			return;
		}
		SetValue(snVariable, legacyToolProductRow.SN);
		try
		{
			processMessages.ExecuteMessage(functionName);
			MessageBox.Show(FindForm(), "执行完成。", "Tools", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		catch (Exception ex)
		{
			MessageBox.Show(FindForm(), ex.Message, "Tools 执行失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void SetValue(string name, object value)
	{
		if (platform != null && !platform.Values.Set(name, value, out string error))
		{
			MessageBox.Show(FindForm(), error, "变量写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private static Control CreateGroup(string text, Control content)
	{
		GroupBox groupBox = new GroupBox
		{
			Dock = DockStyle.Fill,
			Text = text
		};
		groupBox.Controls.Add(content);
		return groupBox;
	}

	private static Label CreateToolbarLabel(string text)
	{
		return new Label
		{
			Dock = DockStyle.Fill,
			Text = text,
			TextAlign = ContentAlignment.MiddleCenter
		};
	}

	private static DateTimePicker CreateDatePicker()
	{
		return new DateTimePicker
		{
			CustomFormat = "yyyy-MM-dd",
			Dock = DockStyle.Fill,
			Format = DateTimePickerFormat.Custom
		};
	}
}

