using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed class LegacyPlcDebugControl : UserControl
{
	private IAutomationPlatform platform;
	private readonly ComboBox filterBox;

	private readonly DataGridView grid;

	internal LegacyPlcDebugControl()
	{
		BackColor = Color.White;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			ColumnCount = 1,
			RowCount = 2,
			Dock = DockStyle.Fill,
			Padding = new Padding(6)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			ColumnCount = 3,
			RowCount = 1,
			Dock = DockStyle.Fill
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220f));
		filterBox = new ComboBox
		{
			Dock = DockStyle.Fill,
			DropDownStyle = ComboBoxStyle.DropDownList,
			Font = new Font("宋体", 10.5f)
		};
		filterBox.Items.AddRange(new object[4] { "全部PLC变量", "触发位", "结果位", "SN_Code" });
		filterBox.SelectedIndex = 0;
		filterBox.SelectedIndexChanged += delegate
		{
			RefreshView();
		};
		Button button = new Button
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 10.5f),
			Text = "生成机器点位参数"
		};
		button.Click += Generate_Click;
		tableLayoutPanel2.Controls.Add(filterBox, 0, 0);
		tableLayoutPanel2.Controls.Add(new Panel
		{
			Dock = DockStyle.Fill
		}, 1, 0);
		tableLayoutPanel2.Controls.Add(button, 2, 0);
		grid = new DataGridView
		{
			AllowUserToAddRows = false,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			BackgroundColor = Color.White,
			Dock = DockStyle.Fill,
			RowHeadersVisible = false
		};
		grid.CellEndEdit += Grid_CellEndEdit;
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.Controls.Add(grid, 0, 1);
		base.Controls.Add(tableLayoutPanel);
	}

	internal void Attach(IAutomationPlatform platform)
	{
		this.platform = platform;
		RefreshView();
	}

	internal void RefreshView()
	{
		if (platform == null)
		{
			return;
		}
		string filter = ((filterBox.SelectedIndex <= 0) ? string.Empty : filterBox.SelectedItem.ToString());
		BindingList<LegacyEditableValueRow> bindingList = new BindingList<LegacyEditableValueRow>();
		foreach (string item in (from name in platform.Values.GetNames()
			where name.IndexOf("PLC", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("触发位", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("结果位", StringComparison.OrdinalIgnoreCase) >= 0
			where filter.Length == 0 || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
			select name).OrderBy((string name) => name, StringComparer.OrdinalIgnoreCase))
		{
			if (platform.Values.TryGet(item, out var value, out var _) && value != null)
			{
				bindingList.Add(new LegacyEditableValueRow
				{
					Name = item,
					Value = value.Value,
					Type = value.Type,
					Note = value.Note
				});
			}
		}
		grid.DataSource = bindingList;
		if (grid.Columns["Name"] != null)
		{
			grid.Columns["Name"].ReadOnly = true;
			grid.Columns["Type"].ReadOnly = true;
			grid.Columns["Note"].ReadOnly = true;
		}
	}

	private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
	{
		if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is LegacyEditableValueRow legacyEditableValueRow && e.ColumnIndex == grid.Columns["Value"].Index)
		{
			SetValue(legacyEditableValueRow.Name, legacyEditableValueRow.Value);
		}
	}

	private void SetValue(string name, object value)
	{
		if (platform != null && !platform.Values.Set(name, value, out string error))
		{
			MessageBox.Show(FindForm(), error, "变量写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void Generate_Click(object sender, EventArgs e)
	{
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "CSV 文件 (*.csv)|*.csv",
			FileName = "PLC点位设备参数_" + DateTime.Now.ToString("yyyyMMdd") + ".csv"
		};
		if (saveFileDialog.ShowDialog(FindForm()) != DialogResult.OK)
		{
			return;
		}
		using StreamWriter streamWriter = new StreamWriter(saveFileDialog.FileName, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		streamWriter.WriteLine("Name,Value,Type,Note");
		foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
		{
			if (item.DataBoundItem is LegacyEditableValueRow legacyEditableValueRow)
			{
				streamWriter.WriteLine(string.Join(",", Csv(legacyEditableValueRow.Name), Csv(legacyEditableValueRow.Value), Csv(legacyEditableValueRow.Type), Csv(legacyEditableValueRow.Note)));
			}
		}
	}

	private static string Csv(string value)
	{
		string text = value ?? string.Empty;
		return "\"" + text.Replace("\"", "\"\"") + "\"";
	}
}

