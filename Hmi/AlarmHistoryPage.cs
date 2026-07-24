using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// 模块：平台内置 HMI / 报警页面。
// 职责范围：复刻旧 UI_Alarm_View 的时间查询、停机 Top10、占比、时长和明细区域。
// 排查入口：无数据时检查 D:\AutomationLogs\AlarmHistory 与 Hmi\Equipment\Alarm。

namespace Automation.Hmi
{
    public partial class AlarmHistoryPage : Form
    {
        private readonly List<LegacyAlarmHistoryRecord> loadedRecords =
            new List<LegacyAlarmHistoryRecord>();
        private readonly Dictionary<string, LegacyAlarmDefinition> alarmDefinitions =
            new Dictionary<string, LegacyAlarmDefinition>(StringComparer.OrdinalIgnoreCase);
        private string projectConfigurationRoot;
        private bool updatingDeviceFilter;

        public AlarmHistoryPage()
        {
            InitializeComponent();
            BuildLegacyLayout();
        }

        internal void LoadSelectedDate()
        {
            Query();
        }

        internal void AttachProjectConfiguration(string root)
        {
            projectConfigurationRoot = root;
            LoadAlarmDefinitions();
        }

        private void BuildLegacyLayout()
        {
            var outer = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 4,
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));

            var top = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            var left = new TableLayoutPanel
            {
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            left.Controls.Add(CreateToolbar(), 0, 0);

            var summary = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            var downtimeLayout = new TableLayoutPanel
            {
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            downtimeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            downtimeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            downtimeLayout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 9F, FontStyle.Bold),
                Text = "Downtime Statistic (Top10)",
                TextAlign = ContentAlignment.MiddleCenter
            }, 0, 0);
            downtimeLayout.Controls.Add(downtimeList, 0, 1);
            summary.Controls.Add(downtimeLayout, 0, 0);
            summary.Controls.Add(pieChart, 1, 0);
            left.Controls.Add(summary, 0, 1);
            top.Controls.Add(left, 0, 0);
            top.Controls.Add(durationChart, 1, 0);
            outer.Controls.Add(top, 1, 1);
            outer.Controls.Add(detailGrid, 1, 2);
            Controls.Add(outer);
        }

        private Control CreateToolbar()
        {
            var toolbar = new TableLayoutPanel
            {
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                ColumnCount = 8,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            float[] widths = { 10F, 15F, 10F, 15F, 10F, 15F, 10F, 15F };
            foreach (float width in widths)
            {
                toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            }
            toolbar.Controls.Add(CreateToolbarLabel("Start Time:"), 0, 0);
            toolbar.Controls.Add(startPicker, 1, 0);
            toolbar.Controls.Add(CreateToolbarLabel("End Time:"), 2, 0);
            toolbar.Controls.Add(endPicker, 3, 0);
            toolbar.Controls.Add(queryButton, 4, 0);
            toolbar.Controls.Add(statisticsMode, 5, 0);
            toolbar.Controls.Add(CreateToolbarLabel("设备"), 6, 0);
            toolbar.Controls.Add(deviceFilter, 7, 0);
            return toolbar;
        }

        private void Query()
        {
            DateTime start = startPicker.Value.Date;
            DateTime end = endPicker.Value.Date;
            if (end < start)
            {
                MessageBox.Show(this, "结束时间不能早于开始时间。", "报警查询", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            loadedRecords.Clear();
            try
            {
                LoadAlarmDefinitions();
                for (DateTime date = start; date <= end; date = date.AddDays(1))
                {
                    loadedRecords.AddRange(ReadPlatformAlarmHistory(date));
                    loadedRecords.AddRange(ReadEquipmentAlarmHistory(date));
                }
                ApplyAlarmDefinitions(loadedRecords);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "报警历史读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            IReadOnlyList<LegacyAlarmHistoryRecord> filtered = FilterByDevice(loadedRecords);
            detailGrid.DataSource = new BindingList<LegacyAlarmHistoryRecord>(
                filtered.OrderByDescending(record => record.StartTime).ToList());
            RenderSummary(filtered);
        }

        private void LoadAlarmDefinitions()
        {
            alarmDefinitions.Clear();
            if (string.IsNullOrWhiteSpace(projectConfigurationRoot))
            {
                return;
            }
            string path = Path.Combine(
                projectConfigurationRoot,
                "AlarmConfig",
                "AlarmInfo.csv");
            if (!File.Exists(path))
            {
                return;
            }
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                List<string> header = LegacyCsv.Parse(reader.ReadLine() ?? string.Empty);
                int codeIndex = header.FindIndex(
                    value => string.Equals(value, "AlarmCode", StringComparison.OrdinalIgnoreCase));
                int messageIndex = header.FindIndex(
                    value => string.Equals(value, "AlarmMsg", StringComparison.OrdinalIgnoreCase));
                int noteIndex = header.FindIndex(
                    value => string.Equals(value, "AlarmNoteCN", StringComparison.OrdinalIgnoreCase));
                int typeIndex = header.FindIndex(
                    value => string.Equals(value, "AlarmType", StringComparison.OrdinalIgnoreCase));
                if (codeIndex < 0)
                {
                    throw new InvalidDataException("AlarmInfo.csv 缺少 AlarmCode 字段。");
                }
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    List<string> fields = LegacyCsv.Parse(line);
                    if (fields.Count <= codeIndex
                        || string.IsNullOrWhiteSpace(fields[codeIndex]))
                    {
                        continue;
                    }
                    string code = fields[codeIndex].Trim();
                    alarmDefinitions[code] = new LegacyAlarmDefinition
                    {
                        Code = code,
                        Message = ReadField(fields, messageIndex),
                        Measures = ReadField(fields, noteIndex),
                        Type = ReadField(fields, typeIndex)
                    };
                }
            }
        }

        private void ApplyAlarmDefinitions(IEnumerable<LegacyAlarmHistoryRecord> records)
        {
            foreach (LegacyAlarmHistoryRecord record in records)
            {
                if (!alarmDefinitions.TryGetValue(
                    record.ErrorCode ?? string.Empty,
                    out LegacyAlarmDefinition definition))
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(record.Message))
                {
                    record.Message = definition.Message;
                }
                if (string.IsNullOrWhiteSpace(record.Detail))
                {
                    record.Detail = definition.Type;
                }
                if (string.IsNullOrWhiteSpace(record.Measures))
                {
                    record.Measures = definition.Measures;
                }
            }
        }

        private static string ReadField(IReadOnlyList<string> fields, int index)
        {
            return index >= 0 && index < fields.Count
                ? fields[index]
                : string.Empty;
        }

        private IReadOnlyList<LegacyAlarmHistoryRecord> FilterByDevice(
            IEnumerable<LegacyAlarmHistoryRecord> records)
        {
            string device = deviceFilter.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(device)
                || string.Equals(device, "全部设备", StringComparison.Ordinal))
            {
                return records.ToList();
            }
            return records
                .Where(record => string.Equals(record.Device, device, StringComparison.Ordinal))
                .ToList();
        }

        private void RenderSummary(IReadOnlyList<LegacyAlarmHistoryRecord> records)
        {
            string selectedDevice = deviceFilter.SelectedItem?.ToString();
            var devices = new[] { "全部设备" }
                .Concat(records.Select(record => record.Device)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal))
                .ToArray();
            updatingDeviceFilter = true;
            try
            {
                deviceFilter.BeginUpdate();
                deviceFilter.Items.Clear();
                deviceFilter.Items.AddRange(devices.Cast<object>().ToArray());
                int selectedIndex = Array.IndexOf(devices, selectedDevice);
                deviceFilter.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                deviceFilter.EndUpdate();
            }
            finally
            {
                updatingDeviceFilter = false;
            }

            var topTen = records
                .GroupBy(record => record.Message ?? string.Empty)
                .Select(group => new LegacyAlarmAggregate
                {
                    Message = group.Key,
                    TotalSeconds = group.Sum(item => item.DurationSeconds),
                    Count = group.Count()
                })
                .OrderByDescending(item => item.TotalSeconds)
                .ThenByDescending(item => item.Count)
                .Take(10)
                .ToList();
            downtimeList.BeginUpdate();
            downtimeList.Items.Clear();
            int rank = 1;
            foreach (LegacyAlarmAggregate item in topTen)
            {
                var row = new ListViewItem(rank.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(item.Message);
                row.SubItems.Add(FormatDuration(item.TotalSeconds));
                row.SubItems.Add(item.Count.ToString(CultureInfo.InvariantCulture));
                downtimeList.Items.Add(row);
                rank++;
            }
            downtimeList.EndUpdate();
            pieChart.SetValues(
                "Alarm Top10",
                topTen.Select(item =>
                    new KeyValuePair<string, double>(item.Message, item.TotalSeconds)));
            durationChart.SetValues(
                "Alarm Durations",
                topTen.Select(item =>
                    new KeyValuePair<string, double>(item.Message, item.TotalSeconds)));
        }

        private static IReadOnlyList<LegacyAlarmHistoryRecord> ReadPlatformAlarmHistory(DateTime date)
        {
            string path = Path.Combine(
                @"D:\AutomationLogs",
                "AlarmHistory",
                date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
            var records = new List<LegacyAlarmHistoryRecord>();
            if (!File.Exists(path))
            {
                return records;
            }
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string header = reader.ReadLine();
                const string expected = "报警代码,报警内容,报警类别,开始时间,结束时间,报警时间(s),报警位置(x-x-x)";
                if (!string.Equals(header, expected, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("报警历史文件表头无效：" + path);
                }
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    List<string> fields = LegacyCsv.Parse(line);
                    if (fields.Count != 7)
                    {
                        continue;
                    }
                    DateTime.TryParse(fields[3], out DateTime start);
                    DateTime.TryParse(fields[4], out DateTime end);
                    double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds);
                    records.Add(new LegacyAlarmHistoryRecord
                    {
                        Date = start == DateTime.MinValue ? date.ToString("yyyy-MM-dd") : start.ToString("yyyy-MM-dd"),
                        StartTime = start,
                        EndTime = end,
                        DurationSeconds = seconds,
                        ErrorCode = fields[0],
                        Message = fields[1],
                        Detail = fields[2],
                        Device = string.IsNullOrWhiteSpace(fields[6]) ? "Automation" : fields[6],
                        Measures = string.Empty
                    });
                }
            }
            return records;
        }

        private static IReadOnlyList<LegacyAlarmHistoryRecord> ReadEquipmentAlarmHistory(DateTime date)
        {
            string path = Path.Combine(
                @"D:\AutomationLogs",
                "Hmi",
                "Equipment",
                "Alarm",
                date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
            var records = new List<LegacyAlarmHistoryRecord>();
            if (!File.Exists(path))
            {
                return records;
            }
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string header = reader.ReadLine();
                if (!string.Equals(header, "Time,SN,Position,Message,Resolution", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("设备报警文件表头无效：" + path);
                }
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    List<string> fields = LegacyCsv.Parse(line);
                    if (fields.Count != 5)
                    {
                        continue;
                    }
                    DateTime.TryParse(fields[0], out DateTime start);
                    records.Add(new LegacyAlarmHistoryRecord
                    {
                        Date = start == DateTime.MinValue ? date.ToString("yyyy-MM-dd") : start.ToString("yyyy-MM-dd"),
                        StartTime = start,
                        EndTime = start,
                        DurationSeconds = 0,
                        ErrorCode = fields[1],
                        Message = fields[3],
                        Detail = fields[2],
                        Device = string.IsNullOrWhiteSpace(fields[2]) ? "设备流程" : fields[2],
                        Measures = fields[4]
                    });
                }
            }
            return records;
        }

        private static string FormatDuration(double seconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return duration.TotalHours >= 1
                ? duration.ToString(@"hh\:mm\:ss")
                : duration.ToString(@"mm\:ss");
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
    }

    internal sealed class LegacyAlarmHistoryRecord
    {
        [DisplayName("Date")]
        public string Date { get; set; }

        [DisplayName("Start Time")]
        public DateTime StartTime { get; set; }

        [DisplayName("End Time")]
        public DateTime EndTime { get; set; }

        [DisplayName("Time in State")]
        public double DurationSeconds { get; set; }

        [DisplayName("Error Code")]
        public string ErrorCode { get; set; }

        [DisplayName("Error Message")]
        public string Message { get; set; }

        [DisplayName("Error Detail")]
        public string Detail { get; set; }

        [DisplayName("Error(vietnamese)")]
        public string Vietnamese { get; set; }

        [DisplayName("Error Measures")]
        public string Measures { get; set; }

        [Browsable(false)]
        public string Device { get; set; }
    }

    internal sealed class LegacyAlarmDefinition
    {
        public string Code { get; set; }

        public string Type { get; set; }

        public string Message { get; set; }

        public string Measures { get; set; }
    }

    internal sealed class LegacyAlarmAggregate
    {
        public string Message { get; set; }
        public double TotalSeconds { get; set; }
        public int Count { get; set; }
    }

    internal sealed class LegacyPieChartControl : Control
    {
        private IReadOnlyList<KeyValuePair<string, double>> values =
            Array.Empty<KeyValuePair<string, double>>();
        private string title = string.Empty;

        internal LegacyPieChartControl()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        internal void SetValues(
            string title,
            IEnumerable<KeyValuePair<string, double>> values)
        {
            this.title = title ?? string.Empty;
            this.values = (values ?? Enumerable.Empty<KeyValuePair<string, double>>())
                .Where(item => item.Value > 0)
                .ToList();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            using (var titleFont = new Font("宋体", 10F, FontStyle.Bold))
            {
                e.Graphics.DrawString(title, titleFont, Brushes.Black, 8F, 7F);
            }
            Rectangle pie = new Rectangle(
                20,
                34,
                Math.Max(20, Math.Min(Width - 40, Height - 62)),
                Math.Max(20, Math.Min(Width - 40, Height - 62)));
            if (values.Count == 0)
            {
                e.Graphics.DrawString("暂无数据", Font, Brushes.Gray, pie.Left + 20, pie.Top + 20);
                return;
            }
            Color[] colors =
            {
                Color.FromArgb(111, 121, 128), Color.FromArgb(84, 151, 193),
                Color.FromArgb(83, 172, 122), Color.FromArgb(248, 195, 92),
                Color.FromArgb(243, 150, 91), Color.FromArgb(228, 94, 105),
                Color.FromArgb(125, 114, 187), Color.FromArgb(76, 170, 233),
                Color.FromArgb(102, 217, 56), Color.FromArgb(194, 72, 134)
            };
            double total = values.Sum(item => item.Value);
            float startAngle = -90F;
            for (int index = 0; index < values.Count; index++)
            {
                float sweep = (float)(values[index].Value / total * 360D);
                using (var brush = new SolidBrush(colors[index % colors.Length]))
                {
                    e.Graphics.FillPie(brush, pie, startAngle, sweep);
                }
                startAngle += sweep;
            }
        }
    }
}
