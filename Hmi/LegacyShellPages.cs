using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 旧设备主窗体子页面。
// 职责范围：复刻旧 FormMain 下的 CCD、Data、Excel、Log 页面，并通过公开 SDK 接入运行数据。
// 排查入口：页面无数据时先检查对应路径/变量是否存在，再检查 IAutomationPlatform 快照。

namespace Automation.Hmi
{
    internal sealed class LegacyVideoPage : Form
    {
        private readonly TableLayoutPanel videoLayout;
        private readonly GroupBox[] videoGroups = new GroupBox[4];
        private readonly ComboBox[] deviceSelectors = new ComboBox[4];
        private readonly PictureBox[] previews = new PictureBox[4];
        private readonly Label[] videoStatuses = new Label[4];
        private readonly string[] openedMonikers = new string[4];
        private IAutomationPlatform platform;
        private ILegacyVideoService videoService;
        private Control maximizedGroup;

        internal LegacyVideoPage()
        {
            Text = "CCD";
            BackColor = Color.White;
            Font = new Font("宋体", 9F);
            videoLayout = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(3)
            };
            videoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            videoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            videoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            videoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            for (int index = 0; index < videoGroups.Length; index++)
            {
                int videoId = index + 1;
                GroupBox group = CreateVideoGroup(videoId);
                videoGroups[index] = group;
                videoLayout.Controls.Add(group, index % 2, index / 2);
            }
            Controls.Add(videoLayout);
        }

        internal void AttachPlatform(IAutomationPlatform platform)
        {
            AttachPlatform(platform, null);
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            ILegacyVideoService videoService)
        {
            this.platform = platform;
            if (this.videoService != null)
            {
                this.videoService.FrameReady -= VideoService_FrameReady;
            }
            this.videoService = videoService;
            if (this.videoService != null)
            {
                this.videoService.FrameReady += VideoService_FrameReady;
                RefreshDeviceList();
            }
            RefreshRuntimeView();
        }

        internal void RefreshRuntimeView()
        {
            if (platform == null)
            {
                return;
            }

            bool disabled = TryReadInteger("禁用Video", out int disabledValue)
                && disabledValue != 0;
            for (int index = 0; index < deviceSelectors.Length; index++)
            {
                string variableName = "摄像头设备" + (index + 1);
                string configured = TryReadString(variableName, out string value)
                    ? value
                    : string.Empty;
                ComboBox selector = deviceSelectors[index];
                if (!string.IsNullOrWhiteSpace(configured)
                    && selector.Items
                        .OfType<LegacyVideoDeviceInfo>()
                        .All(item => !string.Equals(
                            item.Moniker,
                            configured,
                            StringComparison.Ordinal)))
                {
                    selector.Items.Add(new LegacyVideoDeviceInfo(
                        "已配置设备（当前未枚举）",
                        configured));
                }
                if (!selector.DroppedDown)
                {
                    SelectConfiguredDevice(selector, configured);
                }
                videoStatuses[index].Text = disabled
                    ? $"Video{index + 1}\r\nVideo 已禁用"
                    : string.IsNullOrWhiteSpace(configured)
                        ? $"Video{index + 1}\r\n未选择摄像头"
                        : videoService == null
                            ? $"Video{index + 1}\r\n视频服务未装载"
                            : videoService.IsOpen(index + 1)
                                ? $"Video{index + 1}  预览中"
                                : $"Video{index + 1}\r\n{configured}\r\n等待打开";
                videoStatuses[index].ForeColor = disabled ? Color.Gray : Color.White;
                if ((disabled || string.IsNullOrWhiteSpace(configured))
                    && videoService != null
                    && videoService.IsOpen(index + 1))
                {
                    videoService.Stop(index + 1);
                    openedMonikers[index] = null;
                    Image previous = previews[index].Image;
                    previews[index].Image = null;
                    previous?.Dispose();
                }
                if (!disabled
                    && videoService != null
                    && !string.IsNullOrWhiteSpace(configured)
                    && !videoService.IsOpen(index + 1)
                    && !string.Equals(
                        openedMonikers[index],
                        configured,
                        StringComparison.Ordinal))
                {
                    TryOpen(index + 1, configured, showError: false);
                }
            }
        }

        private GroupBox CreateVideoGroup(int videoId)
        {
            var group = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "Video" + videoId,
                Font = new Font("宋体", 9F),
                Margin = new Padding(3)
            };
            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = Padding.Empty
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var selector = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("宋体", 10.5F),
                Margin = new Padding(3, 2, 3, 3)
            };
            selector.Tag = videoId;
            selector.DropDown += (sender, args) => RefreshDeviceList();
            selector.SelectionChangeCommitted += DeviceSelector_Commit;
            deviceSelectors[videoId - 1] = selector;

            var previewPanel = new Panel
            {
                BackColor = Color.Black,
                Dock = DockStyle.Fill
            };
            var preview = new PictureBox
            {
                BackColor = Color.Black,
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Tag = group
            };
            var status = new Label
            {
                BackColor = Color.FromArgb(160, 0, 0, 0),
                Dock = DockStyle.Bottom,
                Font = new Font("微软雅黑", 10.5F),
                ForeColor = Color.White,
                Height = 44,
                Text = "Video" + videoId,
                TextAlign = ContentAlignment.MiddleCenter
            };
            preview.DoubleClick += Preview_DoubleClick;
            previews[videoId - 1] = preview;
            videoStatuses[videoId - 1] = status;
            previewPanel.Controls.Add(preview);
            previewPanel.Controls.Add(status);
            status.BringToFront();
            layout.Controls.Add(selector, 0, 0);
            layout.Controls.Add(previewPanel, 0, 1);
            group.Controls.Add(layout);
            return group;
        }

        private void DeviceSelector_Commit(object sender, EventArgs e)
        {
            if (platform == null
                || !(sender is ComboBox selector)
                || !(selector.SelectedItem is LegacyVideoDeviceInfo selected))
            {
                return;
            }
            int videoId = Convert.ToInt32(selector.Tag, CultureInfo.InvariantCulture);
            string value = selected.Moniker;
            if (!platform.Values.Set("摄像头设备" + videoId, value, out string error))
            {
                MessageBox.Show(FindForm(), error, "摄像头配置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                videoService?.Stop(videoId);
                openedMonikers[videoId - 1] = null;
                return;
            }
            TryOpen(videoId, value, showError: true);
        }

        private void Preview_DoubleClick(object sender, EventArgs e)
        {
            if (!(sender is PictureBox preview) || !(preview.Tag is GroupBox selected))
            {
                return;
            }
            if (maximizedGroup == null)
            {
                foreach (GroupBox group in videoGroups)
                {
                    group.Visible = ReferenceEquals(group, selected);
                }
                videoLayout.SetColumn(selected, 0);
                videoLayout.SetRow(selected, 0);
                videoLayout.SetColumnSpan(selected, 2);
                videoLayout.SetRowSpan(selected, 2);
                maximizedGroup = selected;
                return;
            }

            videoLayout.SetColumnSpan(selected, 1);
            videoLayout.SetRowSpan(selected, 1);
            for (int index = 0; index < videoGroups.Length; index++)
            {
                GroupBox group = videoGroups[index];
                videoLayout.SetColumn(group, index % 2);
                videoLayout.SetRow(group, index / 2);
                group.Visible = true;
            }
            maximizedGroup = null;
        }

        private void RefreshDeviceList()
        {
            if (videoService == null)
            {
                return;
            }
            IReadOnlyList<LegacyVideoDeviceInfo> devices;
            try
            {
                devices = videoService.GetDevices();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    FindForm(),
                    ex.Message,
                    "摄像头枚举失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            foreach (ComboBox selector in deviceSelectors)
            {
                string selectedMoniker =
                    (selector.SelectedItem as LegacyVideoDeviceInfo)?.Moniker;
                selector.BeginUpdate();
                selector.Items.Clear();
                selector.Items.Add(new LegacyVideoDeviceInfo("未配置", string.Empty));
                selector.Items.AddRange(devices.Cast<object>().ToArray());
                SelectConfiguredDevice(selector, selectedMoniker);
                selector.EndUpdate();
            }
        }

        private static void SelectConfiguredDevice(ComboBox selector, string moniker)
        {
            if (string.IsNullOrWhiteSpace(moniker))
            {
                selector.SelectedIndex = selector.Items.Count > 0 ? 0 : -1;
                return;
            }
            for (int index = 0; index < selector.Items.Count; index++)
            {
                if (selector.Items[index] is LegacyVideoDeviceInfo device
                    && string.Equals(device.Moniker, moniker, StringComparison.Ordinal))
                {
                    selector.SelectedIndex = index;
                    return;
                }
            }
            selector.SelectedIndex = -1;
        }

        private void TryOpen(int channel, string moniker, bool showError)
        {
            if (videoService == null)
            {
                return;
            }
            if (videoService.Open(channel, moniker, out string error))
            {
                openedMonikers[channel - 1] = moniker;
                videoStatuses[channel - 1].Text = $"Video{channel}  正在连接";
                return;
            }
            openedMonikers[channel - 1] = moniker;
            videoStatuses[channel - 1].Text = $"Video{channel}\r\n打开失败：{error}";
            if (showError)
            {
                MessageBox.Show(
                    FindForm(),
                    error,
                    "摄像头打开失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void VideoService_FrameReady(
            object sender,
            LegacyVideoFrameEventArgs e)
        {
            if (IsDisposed || Disposing)
            {
                e.Frame.Dispose();
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => ApplyVideoFrame(e)));
                }
                catch (InvalidOperationException)
                {
                    e.Frame.Dispose();
                }
                return;
            }
            ApplyVideoFrame(e);
        }

        private void ApplyVideoFrame(LegacyVideoFrameEventArgs e)
        {
            PictureBox preview = previews[e.Channel - 1];
            Image previous = preview.Image;
            preview.Image = e.Frame;
            previous?.Dispose();
            videoStatuses[e.Channel - 1].Text = $"Video{e.Channel}  预览中";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (videoService != null)
                {
                    videoService.FrameReady -= VideoService_FrameReady;
                }
                foreach (PictureBox preview in previews)
                {
                    preview?.Image?.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private bool TryReadString(string name, out string value)
        {
            value = string.Empty;
            if (!platform.Values.TryGet(name, out ValueSnapshot snapshot, out _)
                || snapshot == null)
            {
                return false;
            }
            value = snapshot.Value ?? string.Empty;
            return true;
        }

        private bool TryReadInteger(string name, out int value)
        {
            value = 0;
            return TryReadString(name, out string text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }

    internal sealed class LegacyDataPage : Form
    {
        private readonly LegacyDataViewControl dataView;
        private readonly LegacyProductDataControl productData;

        internal LegacyDataPage()
        {
            Text = "DataPage";
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 12F)
            };
            var dataViewTab = new TabPage("DataView");
            var productDataTab = new TabPage("ProductData");
            dataView = new LegacyDataViewControl { Dock = DockStyle.Fill };
            productData = new LegacyProductDataControl { Dock = DockStyle.Fill };
            dataViewTab.Controls.Add(dataView);
            productDataTab.Controls.Add(productData);
            tabs.TabPages.Add(dataViewTab);
            tabs.TabPages.Add(productDataTab);
            tabs.SelectedIndexChanged += (sender, args) => RefreshRuntimeView();
            Controls.Add(tabs);
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages)
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

    internal sealed class LegacyDataViewControl : UserControl
    {
        private readonly Button uphButton;
        private readonly Button ngButton;
        private readonly DataGridView grid;
        private readonly LegacyBarChartControl summaryChart;
        private IAutomationPlatform platform;
        private EquipmentProcessMessageService processMessages;
        private bool showNg;

        internal LegacyDataViewControl()
        {
            BackColor = Color.White;
            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 8, 0, 8),
                WrapContents = false
            };
            uphButton = CreateModeButton("UPH/CT");
            ngButton = CreateModeButton("Tossing/NG");
            uphButton.Click += (sender, args) =>
            {
                showNg = false;
                RefreshRuntimeView();
            };
            ngButton.Click += (sender, args) =>
            {
                showNg = true;
                RefreshRuntimeView();
            };
            toolbar.Controls.Add(uphButton);
            toolbar.Controls.Add(ngButton);

            var content = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 0, 8, 8)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            grid = CreateReadOnlyGrid();
            summaryChart = new LegacyBarChartControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(8, 0, 0, 0)
            };
            content.Controls.Add(grid, 0, 0);
            content.Controls.Add(summaryChart, 1, 0);
            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(content, 0, 1);
            Controls.Add(root);
            UpdateModeButtons();
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages)
        {
            this.platform = platform;
            this.processMessages = processMessages;
            RefreshRuntimeView();
        }

        internal void RefreshRuntimeView()
        {
            UpdateModeButtons();
            if (platform == null)
            {
                return;
            }

            string[] keywords = showNg
                ? new[] { "NG", "Tossing", "抛料", "不良", "缺陷" }
                : new[] { "UPH", "CT", "产能", "良率", "产品信息" };
            var rows = new List<LegacyValueRow>();
            foreach (string name in platform.Values.GetNames())
            {
                if (!keywords.Any(keyword =>
                    name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }
                if (platform.Values.TryGet(name, out ValueSnapshot snapshot, out _)
                    && snapshot != null)
                {
                    rows.Add(new LegacyValueRow
                    {
                        Name = name,
                        Value = snapshot.Value,
                        Type = snapshot.Type,
                        Note = snapshot.Note
                    });
                }
            }
            grid.DataSource = new BindingList<LegacyValueRow>(rows);

            EquipmentProcessMessageSnapshot state = processMessages?.GetSnapshot();
            if (state == null)
            {
                summaryChart.SetValues(
                    showNg ? "Tossing/NG" : "UPH/CT",
                    Array.Empty<KeyValuePair<string, double>>());
                return;
            }
            if (showNg)
            {
                summaryChart.SetValues(
                    "Tossing/NG",
                    new[]
                    {
                        new KeyValuePair<string, double>("OK", state.GoodTotal),
                        new KeyValuePair<string, double>("NG", state.DefectTotal)
                    });
            }
            else
            {
                double uph = state.LastCycleSeconds.GetValueOrDefault() > 0
                    ? 3600D / state.LastCycleSeconds.Value
                    : 0D;
                summaryChart.SetValues(
                    "UPH/CT",
                    new[]
                    {
                        new KeyValuePair<string, double>("UPH", uph),
                        new KeyValuePair<string, double>("CT", state.LastCycleSeconds ?? 0D),
                        new KeyValuePair<string, double>("产出", state.OutputTotal)
                    });
            }
        }

        private void UpdateModeButtons()
        {
            uphButton.BackColor = showNg ? Color.Gainsboro : Color.GreenYellow;
            ngButton.BackColor = showNg ? Color.GreenYellow : Color.Gainsboro;
        }

        private static Button CreateModeButton(string text)
        {
            return new Button
            {
                BackColor = Color.Gainsboro,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 12F),
                Margin = new Padding(0, 0, 8, 0),
                Size = new Size(150, 50),
                Text = text,
                UseVisualStyleBackColor = false
            };
        }

        private static DataGridView CreateReadOnlyGrid()
        {
            return new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
        }
    }

    internal sealed class LegacyProductDataControl : UserControl
    {
        private readonly LegacyBarChartControl todayChart;
        private readonly LegacyBarChartControl weekChart;
        private readonly LegacyBarChartControl yieldChart;
        private readonly Label tossingTotal;
        private readonly TextBox stationATotal;
        private readonly TextBox stationBTotal;
        private readonly DateTimePicker startPicker;
        private readonly DateTimePicker endPicker;
        private readonly List<LegacyProductionHistoryRow> currentRows =
            new List<LegacyProductionHistoryRow>();
        private IAutomationPlatform platform;
        private EquipmentProcessMessageService processMessages;

        internal LegacyProductDataControl()
        {
            BackColor = Color.White;
            var root = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(3)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 71.3F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28.7F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            todayChart = CreateChart("Today Product");
            weekChart = CreateChart("Week Product");
            yieldChart = CreateChart("Yield");
            root.Controls.Add(todayChart, 0, 0);
            root.Controls.Add(weekChart, 0, 1);
            root.Controls.Add(yieldChart, 1, 0);

            var functionLayout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            functionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            functionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            functionLayout.Controls.Add(CreateTossingPanel(out tossingTotal, out stationATotal, out stationBTotal), 0, 0);
            functionLayout.Controls.Add(CreateQueryPanel(out startPicker, out endPicker), 0, 1);
            root.Controls.Add(functionLayout, 1, 1);
            Controls.Add(root);
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages)
        {
            this.platform = platform;
            this.processMessages = processMessages;
            RefreshRuntimeView();
        }

        internal void RefreshRuntimeView()
        {
            EquipmentProcessMessageSnapshot state = processMessages?.GetSnapshot();
            if (state == null)
            {
                return;
            }
            int total = state.GoodTotal + state.DefectTotal;
            todayChart.SetValues(
                "Today Product",
                new[]
                {
                    new KeyValuePair<string, double>("投入", state.InputTotal),
                    new KeyValuePair<string, double>("产出", state.OutputTotal)
                });
            yieldChart.SetValues(
                "Yield",
                new[]
                {
                    new KeyValuePair<string, double>("OK", state.GoodTotal),
                    new KeyValuePair<string, double>("NG", state.DefectTotal)
                });
            tossingTotal.Text = total == 0
                ? "TossingSum: 0"
                : $"TossingSum: {state.DefectTotal}  Yield: {(state.GoodTotal * 100D / total):0.00}%";
            stationATotal.Text = state.GoodTotal.ToString(CultureInfo.InvariantCulture);
            stationBTotal.Text = state.DefectTotal.ToString(CultureInfo.InvariantCulture);
        }

        private void QueryHistory()
        {
            currentRows.Clear();
            DateTime start = startPicker.Value.Date;
            DateTime end = endPicker.Value.Date;
            if (end < start)
            {
                MessageBox.Show(FindForm(), "结束时间不能早于开始时间。", "查询", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            for (DateTime date = start; date <= end; date = date.AddDays(1))
            {
                string path = Path.Combine(
                    @"D:\AutomationLogs",
                    "Hmi",
                    "Equipment",
                    "Production",
                    date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_Output.csv");
                currentRows.AddRange(ReadProductionFile(path));
                currentRows.AddRange(ReadLegacyHiveHistory(date));
            }
            var perDay = currentRows
                .GroupBy(row => row.Time.Date)
                .OrderBy(group => group.Key)
                .Select(group => new KeyValuePair<string, double>(
                    group.Key.ToString("MM-dd"),
                    group.Count()))
                .ToList();
            weekChart.SetValues("Week Product", perDay);
            todayChart.SetValues(
                "Today Product",
                currentRows
                    .Where(row => row.Time.Date == DateTime.Today)
                    .GroupBy(row => row.IsFailure ? "NG" : "OK")
                    .Select(group => new KeyValuePair<string, double>(
                        group.Key,
                        group.Count()))
                    .ToList());
            int good = currentRows.Count(row => !row.IsFailure);
            int ng = currentRows.Count - good;
            yieldChart.SetValues(
                "Yield",
                new[]
                {
                    new KeyValuePair<string, double>("OK", good),
                    new KeyValuePair<string, double>("NG", ng)
                });
            tossingTotal.Text = $"TossingSum: {ng}  Total: {currentRows.Count}";
            stationATotal.Text = good.ToString(CultureInfo.InvariantCulture);
            stationBTotal.Text = ng.ToString(CultureInfo.InvariantCulture);
        }

        private void ExportHistory()
        {
            if (currentRows.Count == 0)
            {
                QueryHistory();
            }
            using (var dialog = new SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = "ProductData_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv"
            })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                {
                    return;
                }
                using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("Time,SN,ProcessInfo,InfoData,Mode");
                    foreach (LegacyProductionHistoryRow row in currentRows)
                    {
                        writer.WriteLine(string.Join(",", new[]
                        {
                            EscapeCsv(row.Time.ToString("yyyy-MM-dd HH:mm:ss")),
                            EscapeCsv(row.SN),
                            EscapeCsv(row.ProcessInfo),
                            EscapeCsv(row.InfoData),
                            EscapeCsv(row.Mode)
                        }));
                    }
                }
            }
        }

        private static IReadOnlyList<LegacyProductionHistoryRow> ReadProductionFile(string path)
        {
            var rows = new List<LegacyProductionHistoryRow>();
            if (!File.Exists(path))
            {
                return rows;
            }
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string header = reader.ReadLine();
                if (!string.Equals(header, "Time,SN,ProcessInfo,InfoData,Mode", StringComparison.Ordinal))
                {
                    return rows;
                }
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    List<string> fields = LegacyCsv.Parse(line);
                    if (fields.Count != 5
                        || !DateTime.TryParse(fields[0], out DateTime time))
                    {
                        continue;
                    }
                    rows.Add(new LegacyProductionHistoryRow
                    {
                        Time = time,
                        SN = fields[1],
                        ProcessInfo = fields[2],
                        InfoData = fields[3],
                        Mode = fields[4],
                        IsFailure = fields[3].IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0
                            || fields[3].Contains("12")
                    });
                }
            }
            return rows;
        }

        private IReadOnlyList<LegacyProductionHistoryRow> ReadLegacyHiveHistory(DateTime date)
        {
            string hiveRoot = ReadPlatformValue("<Hive文件加载地址>");
            if (string.IsNullOrWhiteSpace(hiveRoot))
            {
                string equipmentName = ReadPlatformValue("设备名称");
                if (!string.IsNullOrWhiteSpace(equipmentName))
                {
                    hiveRoot = ReadPlatformValue(equipmentName + "<Hive文件加载地址>");
                }
            }
            if (string.IsNullOrWhiteSpace(hiveRoot))
            {
                return Array.Empty<LegacyProductionHistoryRow>();
            }

            string dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string directory = Path.Combine(hiveRoot, dateText, "Hive");
            var rows = new List<LegacyProductionHistoryRow>();
            rows.AddRange(ReadLegacyHiveFile(
                Path.Combine(directory, dateText + "-产品记录表.csv"),
                isFailure: false));
            rows.AddRange(ReadLegacyHiveFile(
                Path.Combine(directory, dateText + "-抛料记录表.csv"),
                isFailure: true));
            return rows;
        }

        private static IReadOnlyList<LegacyProductionHistoryRow> ReadLegacyHiveFile(
            string path,
            bool isFailure)
        {
            var rows = new List<LegacyProductionHistoryRow>();
            if (!File.Exists(path))
            {
                return rows;
            }
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.Default, true))
            {
                reader.ReadLine();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    List<string> fields = LegacyCsv.Parse(line);
                    if (fields.Count < 4
                        || !DateTime.TryParse(fields[1].Trim(), out DateTime time))
                    {
                        continue;
                    }
                    rows.Add(new LegacyProductionHistoryRow
                    {
                        Time = time,
                        SN = fields[0],
                        ProcessInfo = isFailure ? "抛料记录" : "产品记录",
                        InfoData = string.Join(";", fields.Skip(2)),
                        Mode = "LegacyHive",
                        IsFailure = isFailure
                    });
                }
            }
            return rows;
        }

        private string ReadPlatformValue(string name)
        {
            return platform != null
                && platform.Values.TryGet(name, out ValueSnapshot snapshot, out _)
                && snapshot != null
                    ? snapshot.Value ?? string.Empty
                    : string.Empty;
        }

        private static Control CreateTossingPanel(
            out Label total,
            out TextBox stationA,
            out TextBox stationB)
        {
            var layout = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            total = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 12F, FontStyle.Bold),
                Text = "TossingSum",
                TextAlign = ContentAlignment.MiddleCenter
            };
            layout.SetColumnSpan(total, 2);
            layout.Controls.Add(total, 0, 0);
            layout.Controls.Add(CreateStationPanel("工站A", out stationA), 0, 1);
            layout.Controls.Add(CreateStationPanel("工站B", out stationB), 1, 1);
            return layout;
        }

        private Control CreateQueryPanel(
            out DateTimePicker start,
            out DateTimePicker end)
        {
            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
            var dateLayout = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            dateLayout.Controls.Add(CreateCenteredLabel("开始时间"), 0, 0);
            dateLayout.Controls.Add(CreateCenteredLabel("结束时间"), 0, 1);
            start = new DateTimePicker
            {
                Dock = DockStyle.Fill,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd"
            };
            end = new DateTimePicker
            {
                Dock = DockStyle.Fill,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd"
            };
            start.Value = DateTime.Today.AddDays(-6);
            end.Value = DateTime.Today;
            dateLayout.Controls.Add(start, 1, 0);
            dateLayout.Controls.Add(end, 1, 1);
            var buttons = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            Button query = CreateActionButton("查询");
            Button export = CreateActionButton("导出");
            query.Click += (sender, args) => QueryHistory();
            export.Click += (sender, args) => ExportHistory();
            buttons.Controls.Add(query, 0, 0);
            buttons.Controls.Add(export, 1, 0);
            layout.Controls.Add(dateLayout, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            return layout;
        }

        private static Control CreateStationPanel(string title, out TextBox value)
        {
            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(CreateCenteredLabel(title), 0, 0);
            value = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 18F, FontStyle.Bold),
                ReadOnly = true,
                Text = "0",
                TextAlign = HorizontalAlignment.Center
            };
            layout.Controls.Add(value, 0, 1);
            return layout;
        }

        private static LegacyBarChartControl CreateChart(string title)
        {
            var chart = new LegacyBarChartControl
            {
                BackColor = Color.White,
                Dock = DockStyle.Fill,
                Margin = new Padding(4)
            };
            chart.SetValues(title, Array.Empty<KeyValuePair<string, double>>());
            return chart;
        }

        private static Label CreateCenteredLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 10.5F),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private static Button CreateActionButton(string text)
        {
            return new Button
            {
                BackColor = Color.Gainsboro,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 11F),
                Margin = new Padding(4),
                Text = text,
                UseVisualStyleBackColor = false
            };
        }

        private static string EscapeCsv(string value)
        {
            string text = value ?? string.Empty;
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
                ? text
                : "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }

    internal sealed class LegacyExcelPage : Form
    {
        private readonly LegacyFileBrowserControl browser;

        internal LegacyExcelPage()
        {
            Text = "UI_Excel";
            browser = new LegacyFileBrowserControl(
                "Excel加载文件路径",
                "Excel显示文件类型",
                @"D:\AutomationLogs",
                true)
            {
                Dock = DockStyle.Fill
            };
            Controls.Add(browser);
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

    internal sealed class LegacyLogPage : Form
    {
        private readonly LegacyFileBrowserControl productLog;
        private readonly LegacyFileBrowserControl platformLog;

        internal LegacyLogPage()
        {
            Text = "LogForm";
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 12F)
            };
            var productTab = new TabPage("ProductLog");
            var platformTab = new TabPage("TerraceLog");
            productLog = new LegacyFileBrowserControl(
                "Log加载文件路径",
                "Log显示文件类型",
                Path.Combine(@"D:\AutomationLogs", "Hmi", "Equipment"),
                false)
            {
                Dock = DockStyle.Fill
            };
            platformLog = new LegacyFileBrowserControl(
                null,
                null,
                @"D:\AutomationLogs",
                false)
            {
                Dock = DockStyle.Fill
            };
            productTab.Controls.Add(productLog);
            platformTab.Controls.Add(platformLog);
            tabs.TabPages.Add(productTab);
            tabs.TabPages.Add(platformTab);
            tabs.SelectedIndexChanged += (sender, args) => RefreshRuntimeView();
            Controls.Add(tabs);
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

    internal sealed class LegacyFileBrowserControl : UserControl
    {
        private const string LazyNodeMarker = "__lazy__";
        private readonly string rootVariableName;
        private readonly string filterVariableName;
        private readonly string defaultRoot;
        private readonly bool openBinaryFiles;
        private readonly TreeView directoryTree;
        private readonly TreeView fileTree;
        private readonly RichTextBox preview;
        private readonly ContextMenuStrip directoryMenu;
        private IAutomationPlatform platform;
        private bool loaded;
        private string extensionFilter = string.Empty;

        internal LegacyFileBrowserControl(
            string rootVariableName,
            string filterVariableName,
            string defaultRoot,
            bool openBinaryFiles)
        {
            this.rootVariableName = rootVariableName;
            this.filterVariableName = filterVariableName;
            this.defaultRoot = defaultRoot;
            this.openBinaryFiles = openBinaryFiles;
            BackColor = Color.White;
            var root = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            var treeLayout = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            treeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            treeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            directoryTree = CreateTree();
            fileTree = CreateTree();
            preview = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 12F),
                ReadOnly = true,
                WordWrap = false
            };
            directoryMenu = new ContextMenuStrip();
            directoryMenu.Items.Add("更新", null, (sender, args) => Reload());
            directoryTree.ContextMenuStrip = directoryMenu;
            directoryTree.BeforeExpand += DirectoryTree_BeforeExpand;
            directoryTree.AfterSelect += DirectoryTree_AfterSelect;
            directoryTree.NodeMouseDoubleClick += DirectoryTree_NodeMouseDoubleClick;
            fileTree.AfterSelect += FileTree_AfterSelect;
            fileTree.NodeMouseDoubleClick += FileTree_NodeMouseDoubleClick;
            treeLayout.Controls.Add(directoryTree, 0, 0);
            treeLayout.Controls.Add(fileTree, 1, 0);
            root.Controls.Add(treeLayout, 0, 0);
            root.Controls.Add(preview, 1, 0);
            Controls.Add(root);
        }

        internal void AttachPlatform(IAutomationPlatform platform)
        {
            this.platform = platform;
            Reload();
        }

        internal void EnsureLoaded()
        {
            if (!loaded)
            {
                Reload();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                directoryMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        private void Reload()
        {
            directoryTree.Nodes.Clear();
            fileTree.Nodes.Clear();
            preview.Clear();
            extensionFilter = ReadConfiguredValue(filterVariableName);
            string configuredRoots = ReadConfiguredValue(rootVariableName);
            if (string.IsNullOrWhiteSpace(configuredRoots))
            {
                configuredRoots = defaultRoot;
            }
            foreach (string rootPath in configuredRoots
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AddRoot(rootPath);
            }
            loaded = true;
            if (directoryTree.Nodes.Count == 0)
            {
                preview.Text = "未找到可用目录：" + configuredRoots;
            }
        }

        private void AddRoot(string rootPath)
        {
            try
            {
                string fullPath = Path.GetFullPath(rootPath);
                var node = new TreeNode(fullPath) { Tag = fullPath };
                if (Directory.Exists(fullPath) && HasSubdirectories(fullPath))
                {
                    node.Nodes.Add(new TreeNode(LazyNodeMarker));
                }
                directoryTree.Nodes.Add(node);
            }
            catch (Exception ex)
            {
                directoryTree.Nodes.Add(new TreeNode(rootPath + "（" + ex.Message + "）"));
            }
        }

        private void DirectoryTree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Nodes.Count != 1
                || !string.Equals(e.Node.Nodes[0].Text, LazyNodeMarker, StringComparison.Ordinal)
                || !(e.Node.Tag is string path))
            {
                return;
            }
            e.Node.Nodes.Clear();
            try
            {
                foreach (string directory in Directory.GetDirectories(path)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                {
                    var child = new TreeNode(Path.GetFileName(directory)) { Tag = directory };
                    if (HasSubdirectories(directory))
                    {
                        child.Nodes.Add(new TreeNode(LazyNodeMarker));
                    }
                    e.Node.Nodes.Add(child);
                }
            }
            catch (Exception ex)
            {
                e.Node.Nodes.Add(new TreeNode("无法读取：" + ex.Message));
            }
        }

        private void DirectoryTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            fileTree.Nodes.Clear();
            if (!(e.Node.Tag is string path) || !Directory.Exists(path))
            {
                return;
            }
            try
            {
                foreach (string file in Directory.GetFiles(path)
                    .Where(IsVisibleFile)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .Take(5000))
                {
                    fileTree.Nodes.Add(new TreeNode(Path.GetFileName(file)) { Tag = file });
                }
                preview.Text = $"目录：{path}\r\n文件数：{fileTree.Nodes.Count}";
            }
            catch (Exception ex)
            {
                preview.Text = ex.Message;
            }
        }

        private void FileTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is string path)
            {
                PreviewFile(path);
            }
        }

        private void DirectoryTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Tag is string path && Directory.Exists(path))
            {
                OpenPath(path);
            }
        }

        private void FileTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (!(e.Node.Tag is string path) || !File.Exists(path))
            {
                return;
            }
            if (openBinaryFiles || !IsTextFile(path))
            {
                OpenPath(path);
            }
        }

        private void PreviewFile(string path)
        {
            try
            {
                var file = new FileInfo(path);
                if (!IsTextFile(path))
                {
                    preview.Text = $"文件：{file.FullName}\r\n大小：{file.Length:N0} Bytes\r\n修改时间：{file.LastWriteTime:yyyy-MM-dd HH:mm:ss}\r\n\r\n双击文件可使用系统程序打开。";
                    return;
                }
                const int maximumPreviewChars = 300000;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    char[] buffer = new char[maximumPreviewChars];
                    int read = reader.ReadBlock(buffer, 0, buffer.Length);
                    preview.Text = new string(buffer, 0, read);
                    if (!reader.EndOfStream)
                    {
                        preview.AppendText("\r\n\r\n……文件较大，仅显示前 300000 个字符……");
                    }
                }
            }
            catch (Exception ex)
            {
                preview.Text = ex.Message;
            }
        }

        private string ReadConfiguredValue(string variableName)
        {
            if (platform == null
                || string.IsNullOrWhiteSpace(variableName)
                || !platform.Values.TryGet(variableName, out ValueSnapshot snapshot, out _)
                || snapshot == null)
            {
                return string.Empty;
            }
            return snapshot.Value ?? string.Empty;
        }

        private bool IsVisibleFile(string path)
        {
            if (string.IsNullOrWhiteSpace(extensionFilter))
            {
                return true;
            }
            string extension = Path.GetExtension(path).TrimStart('.');
            return extensionFilter
                .Split(new[] { ';', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim().TrimStart('*', '.'))
                .Any(item => string.Equals(item, extension, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasSubdirectories(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path).Take(1).Any();
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTextFile(string path)
        {
            string extension = Path.GetExtension(path);
            return new[]
            {
                ".txt", ".log", ".csv", ".json", ".xml", ".md", ".ini", ".config"
            }.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static TreeView CreateTree()
        {
            return new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 12F),
                HideSelection = false
            };
        }
    }

    internal sealed class LegacyBarChartControl : Control
    {
        private IReadOnlyList<KeyValuePair<string, double>> values =
            Array.Empty<KeyValuePair<string, double>>();
        private string title = string.Empty;

        internal LegacyBarChartControl()
        {
            DoubleBuffered = true;
            Font = new Font("微软雅黑", 9F);
        }

        internal void SetValues(
            string title,
            IEnumerable<KeyValuePair<string, double>> values)
        {
            this.title = title ?? string.Empty;
            this.values = (values ?? Enumerable.Empty<KeyValuePair<string, double>>()).ToList();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            using (var border = new Pen(Color.Silver))
            using (var titleFont = new Font("微软雅黑", 11F, FontStyle.Bold))
            using (var valueBrush = new SolidBrush(Color.FromArgb(40, 105, 155)))
            {
                graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
                graphics.DrawString(title, titleFont, Brushes.Black, 10F, 8F);
                Rectangle chart = new Rectangle(48, 42, Math.Max(10, Width - 66), Math.Max(10, Height - 84));
                graphics.DrawLine(Pens.Gray, chart.Left, chart.Top, chart.Left, chart.Bottom);
                graphics.DrawLine(Pens.Gray, chart.Left, chart.Bottom, chart.Right, chart.Bottom);
                if (values.Count == 0)
                {
                    graphics.DrawString("暂无数据", Font, Brushes.Gray, chart.Left + 12, chart.Top + 12);
                    return;
                }
                double maximum = Math.Max(1D, values.Max(item => Math.Abs(item.Value)));
                float slot = chart.Width / (float)values.Count;
                for (int index = 0; index < values.Count; index++)
                {
                    KeyValuePair<string, double> value = values[index];
                    float barWidth = Math.Max(8F, slot * 0.55F);
                    float height = (float)(Math.Abs(value.Value) / maximum * Math.Max(4, chart.Height - 8));
                    float x = chart.Left + index * slot + (slot - barWidth) / 2F;
                    float y = chart.Bottom - height;
                    graphics.FillRectangle(valueBrush, x, y, barWidth, height);
                    string number = value.Value.ToString("0.##", CultureInfo.InvariantCulture);
                    SizeF numberSize = graphics.MeasureString(number, Font);
                    graphics.DrawString(number, Font, Brushes.Black, x + (barWidth - numberSize.Width) / 2F, y - numberSize.Height);
                    string label = value.Key ?? string.Empty;
                    if (label.Length > 8)
                    {
                        label = label.Substring(0, 8);
                    }
                    SizeF labelSize = graphics.MeasureString(label, Font);
                    graphics.DrawString(label, Font, Brushes.Black, x + (barWidth - labelSize.Width) / 2F, chart.Bottom + 4F);
                }
            }
        }
    }

    internal sealed class LegacyValueRow
    {
        [DisplayName("变量")]
        public string Name { get; set; }

        [DisplayName("当前值")]
        public string Value { get; set; }

        [DisplayName("类型")]
        public string Type { get; set; }

        [DisplayName("说明")]
        public string Note { get; set; }
    }

    internal sealed class LegacyProductionHistoryRow
    {
        public DateTime Time { get; set; }
        public string SN { get; set; }
        public string ProcessInfo { get; set; }
        public string InfoData { get; set; }
        public string Mode { get; set; }
        public bool IsFailure { get; set; }
    }

    internal static class LegacyCsv
    {
        internal static List<string> Parse(string line)
        {
            var fields = new List<string>();
            var value = new StringBuilder();
            bool quoted = false;
            for (int index = 0; index < (line ?? string.Empty).Length; index++)
            {
                char current = line[index];
                if (quoted)
                {
                    if (current == '"')
                    {
                        if (index + 1 < line.Length && line[index + 1] == '"')
                        {
                            value.Append('"');
                            index++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        value.Append(current);
                    }
                }
                else if (current == ',')
                {
                    fields.Add(value.ToString());
                    value.Clear();
                }
                else if (current == '"')
                {
                    quoted = true;
                }
                else
                {
                    value.Append(current);
                }
            }
            fields.Add(value.ToString());
            return fields;
        }
    }
}
