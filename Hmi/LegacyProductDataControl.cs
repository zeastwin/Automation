using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed partial class LegacyProductDataControl : UserControl
{
	private readonly List<LegacyProductionHistoryRow> currentRows = new List<LegacyProductionHistoryRow>();

	private IAutomationPlatform platform;

	private EquipmentProcessMessageService processMessages;

	internal LegacyProductDataControl()
	{
		InitializeComponent();
		todayChart.SetValues("Today Product", Array.Empty<KeyValuePair<string, double>>());
		weekChart.SetValues("Week Product", Array.Empty<KeyValuePair<string, double>>());
		yieldChart.SetValues("Yield", Array.Empty<KeyValuePair<string, double>>());
		startPicker.Value = DateTime.Today.AddDays(-6.0);
		endPicker.Value = DateTime.Today;
	}

	private void QueryButton_Click(object sender, EventArgs e)
	{
		QueryHistory();
	}

	private void ExportButton_Click(object sender, EventArgs e)
	{
		ExportHistory();
	}

	internal void AttachPlatform(IAutomationPlatform platform, EquipmentProcessMessageService processMessages)
	{
		this.platform = platform;
		this.processMessages = processMessages;
		RefreshRuntimeView();
	}

	internal void RefreshRuntimeView()
	{
		EquipmentProcessMessageSnapshot equipmentProcessMessageSnapshot = processMessages?.GetSnapshot();
		if (equipmentProcessMessageSnapshot != null)
		{
			int num = equipmentProcessMessageSnapshot.GoodTotal + equipmentProcessMessageSnapshot.DefectTotal;
			todayChart.SetValues("Today Product", new KeyValuePair<string, double>[2]
			{
				new KeyValuePair<string, double>("投入", equipmentProcessMessageSnapshot.InputTotal),
				new KeyValuePair<string, double>("产出", equipmentProcessMessageSnapshot.OutputTotal)
			});
			yieldChart.SetValues("Yield", new KeyValuePair<string, double>[2]
			{
				new KeyValuePair<string, double>("OK", equipmentProcessMessageSnapshot.GoodTotal),
				new KeyValuePair<string, double>("NG", equipmentProcessMessageSnapshot.DefectTotal)
			});
			tossingTotal.Text = ((num == 0) ? "TossingSum: 0" : $"TossingSum: {equipmentProcessMessageSnapshot.DefectTotal}  Yield: {(double)equipmentProcessMessageSnapshot.GoodTotal * 100.0 / (double)num:0.00}%");
			stationATotal.Text = equipmentProcessMessageSnapshot.GoodTotal.ToString(CultureInfo.InvariantCulture);
			stationBTotal.Text = equipmentProcessMessageSnapshot.DefectTotal.ToString(CultureInfo.InvariantCulture);
		}
	}

	private void QueryHistory()
	{
		currentRows.Clear();
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
			string path = Path.Combine("D:\\AutomationLogs", "Hmi", "Equipment", "Production", dateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_Output.csv");
			currentRows.AddRange(ReadProductionFile(path));
			currentRows.AddRange(ReadLegacyHiveHistory(dateTime));
			dateTime = dateTime.AddDays(1.0);
		}
		List<KeyValuePair<string, double>> values = (from row in currentRows
			group row by row.Time.Date into @group
			orderby @group.Key
			select new KeyValuePair<string, double>(@group.Key.ToString("MM-dd"), @group.Count())).ToList();
		weekChart.SetValues("Week Product", values);
		todayChart.SetValues("Today Product", (from row in currentRows
			where row.Time.Date == DateTime.Today
			group row by row.IsFailure ? "NG" : "OK" into @group
			select new KeyValuePair<string, double>(@group.Key, @group.Count())).ToList());
		int num = currentRows.Count((LegacyProductionHistoryRow row) => !row.IsFailure);
		int num2 = currentRows.Count - num;
		yieldChart.SetValues("Yield", new KeyValuePair<string, double>[2]
		{
			new KeyValuePair<string, double>("OK", num),
			new KeyValuePair<string, double>("NG", num2)
		});
		tossingTotal.Text = $"TossingSum: {num2}  Total: {currentRows.Count}";
		stationATotal.Text = num.ToString(CultureInfo.InvariantCulture);
		stationBTotal.Text = num2.ToString(CultureInfo.InvariantCulture);
	}

	private void ExportHistory()
	{
		if (currentRows.Count == 0)
		{
			QueryHistory();
		}
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "CSV 文件 (*.csv)|*.csv",
			FileName = "ProductData_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv"
		};
		if (saveFileDialog.ShowDialog(FindForm()) != DialogResult.OK)
		{
			return;
		}
		using StreamWriter streamWriter = new StreamWriter(saveFileDialog.FileName, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		streamWriter.WriteLine("Time,SN,ProcessInfo,InfoData,Mode");
		foreach (LegacyProductionHistoryRow currentRow in currentRows)
		{
			streamWriter.WriteLine(string.Join(",", EscapeCsv(currentRow.Time.ToString("yyyy-MM-dd HH:mm:ss")), EscapeCsv(currentRow.SN), EscapeCsv(currentRow.ProcessInfo), EscapeCsv(currentRow.InfoData), EscapeCsv(currentRow.Mode)));
		}
	}

	private static IReadOnlyList<LegacyProductionHistoryRow> ReadProductionFile(string path)
	{
		List<LegacyProductionHistoryRow> list = new List<LegacyProductionHistoryRow>();
		if (!File.Exists(path))
		{
			return list;
		}
		using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
		{
			using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
			string a = streamReader.ReadLine();
			if (!string.Equals(a, "Time,SN,ProcessInfo,InfoData,Mode", StringComparison.Ordinal))
			{
				return list;
			}
			string line;
			while ((line = streamReader.ReadLine()) != null)
			{
				List<string> list2 = LegacyCsv.Parse(line);
				if (list2.Count == 5 && DateTime.TryParse(list2[0], out var result))
				{
					list.Add(new LegacyProductionHistoryRow
					{
						Time = result,
						SN = list2[1],
						ProcessInfo = list2[2],
						InfoData = list2[3],
						Mode = list2[4],
						IsFailure = (list2[3].IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0 || list2[3].Contains("12"))
					});
				}
			}
		}
		return list;
	}

	private IReadOnlyList<LegacyProductionHistoryRow> ReadLegacyHiveHistory(DateTime date)
	{
		string text = ReadPlatformValue("<Hive文件加载地址>");
		if (string.IsNullOrWhiteSpace(text))
		{
			string text2 = ReadPlatformValue("设备名称");
			if (!string.IsNullOrWhiteSpace(text2))
			{
				text = ReadPlatformValue(text2 + "<Hive文件加载地址>");
			}
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return Array.Empty<LegacyProductionHistoryRow>();
		}
		string text3 = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		string path = Path.Combine(text, text3, "Hive");
		List<LegacyProductionHistoryRow> list = new List<LegacyProductionHistoryRow>();
		list.AddRange(ReadLegacyHiveFile(Path.Combine(path, text3 + "-产品记录表.csv"), isFailure: false));
		list.AddRange(ReadLegacyHiveFile(Path.Combine(path, text3 + "-抛料记录表.csv"), isFailure: true));
		return list;
	}

	private static IReadOnlyList<LegacyProductionHistoryRow> ReadLegacyHiveFile(string path, bool isFailure)
	{
		List<LegacyProductionHistoryRow> list = new List<LegacyProductionHistoryRow>();
		if (!File.Exists(path))
		{
			return list;
		}
		using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
		{
			using StreamReader streamReader = new StreamReader(stream, Encoding.Default, detectEncodingFromByteOrderMarks: true);
			streamReader.ReadLine();
			string line;
			while ((line = streamReader.ReadLine()) != null)
			{
				List<string> list2 = LegacyCsv.Parse(line);
				if (list2.Count >= 4 && DateTime.TryParse(list2[1].Trim(), out var result))
				{
					list.Add(new LegacyProductionHistoryRow
					{
						Time = result,
						SN = list2[0],
						ProcessInfo = (isFailure ? "抛料记录" : "产品记录"),
						InfoData = string.Join(";", list2.Skip(2)),
						Mode = "LegacyHive",
						IsFailure = isFailure
					});
				}
			}
		}
		return list;
	}

	private string ReadPlatformValue(string name)
	{
		ValueSnapshot value;
		string error;
		return (platform != null && platform.Values.TryGet(name, out value, out error) && value != null) ? (value.Value ?? string.Empty) : string.Empty;
	}

	private static string EscapeCsv(string value)
	{
		string text = value ?? string.Empty;
		return (text.IndexOfAny(new char[4] { ',', '"', '\r', '\n' }) < 0) ? text : ("\"" + text.Replace("\"", "\"\"") + "\"");
	}
}




