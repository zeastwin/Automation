using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public sealed class FrmPlc : Form
    {
        private readonly ListBox deviceList = new ListBox();
        private readonly PropertyGrid deviceProperties = new PropertyGrid();
        private readonly DataGridView mapGrid = new DataGridView();
        private readonly DataGridView historyGrid = new DataGridView();
        private readonly BindingList<DebugHistoryItem> history = new BindingList<DebugHistoryItem>();
        private readonly Label stateLabel = new Label();
        private readonly Label summaryLabel = new Label();
        private readonly Label monitorLabel = new Label();
        private readonly ComboBox variableSelector = new ComboBox();
        private readonly ComboBox debugArea = new ComboBox();
        private readonly ComboBox debugType = new ComboBox();
        private readonly NumericUpDown debugAddress = new NumericUpDown();
        private readonly NumericUpDown debugCount = new NumericUpDown();
        private readonly NumericUpDown debugStringLength = new NumericUpDown();
        private readonly TextBox debugValue = new TextBox();
        private readonly Timer refreshTimer = new Timer();
        private readonly Timer monitorTimer = new Timer();
        private PlcConfiguration draft = new PlcConfiguration();
        private PlcDeviceConfig currentDevice;
        private bool loading;
        private bool monitorBusy;
        private string lastMonitorSignature;

        public FrmPlc()
        {
            Text = "PLC";
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 650);
            Size = new Size(1280, 800);
            BuildUi();
            Load += FrmPlc_Load;
            VisibleChanged += FrmPlc_VisibleChanged;
            FormClosing += (sender, args) =>
            {
                if (args.CloseReason == CloseReason.UserClosing)
                {
                    args.Cancel = true;
                    Hide();
                }
            };
            refreshTimer.Interval = 500;
            refreshTimer.Tick += (sender, args) => RefreshRuntimeState();
            monitorTimer.Interval = 500;
            monitorTimer.Tick += async (sender, args) => await MonitorOnceAsync();
        }

        private void BuildUi()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.White,
                Padding = new Padding(20, 8, 20, 6)
            };
            header.Controls.Add(new Label
            {
                Text = "PLC",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                Location = new Point(20, 7)
            });
            summaryLabel.AutoSize = true;
            summaryLabel.ForeColor = Color.FromArgb(100, 116, 139);
            summaryLabel.Location = new Point(22, 38);
            header.Controls.Add(summaryLabel);

            var commandBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(20, 9, 12, 8),
                WrapContents = false
            };
            commandBar.Controls.AddRange(new Control[]
            {
                SectionLabel("设备配置"),
                CommandButton("新增", AddDevice),
                CommandButton("删除", DeleteDevice),
                CommandButton("保存配置", SaveConfiguration, true),
                CommandSeparator(),
                SectionLabel("设备控制"),
                CommandButton("重新初始化", ReinitializeDevice),
                CommandButton("启动映射", StartMapping, true),
                CommandButton("停止映射", StopMapping, false, true)
            });

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                IsSplitterFixed = true,
                SplitterWidth = 1,
                BackColor = Color.FromArgb(226, 232, 240)
            };
            split.Panel1.Padding = new Padding(12);
            split.Panel1.BackColor = Color.FromArgb(248, 250, 252);
            split.Panel2.Padding = new Padding(12);
            split.Panel2.BackColor = BackColor;
            // Dock布局按添加顺序反向计算，标题必须最后加入才能显示在最上方。
            Controls.Add(split);
            Controls.Add(commandBar);
            Controls.Add(header);
            bool splitInitialized = false;
            Action applySplitLayout = () =>
            {
                // SplitContainer 创建时仍是设计期默认宽度，此时设置最小宽度会抛异常；
                // 必须等窗体完成首轮布局后再固定设备栏，避免首次打开时只剩几十像素。
                if (splitInitialized || split.IsDisposed || split.Width < 900) return;
                split.Panel1MinSize = 220;
                split.Panel2MinSize = 650;
                split.SplitterDistance = 240;
                splitInitialized = true;
            };
            split.Layout += (sender, args) => applySplitLayout();
            Shown += (sender, args) => applySplitLayout();

            var deviceTitle = new Label
            {
                Text = "设备列表",
                Dock = DockStyle.Top,
                Height = 30,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85)
            };
            deviceList.Dock = DockStyle.Fill;
            deviceList.BorderStyle = BorderStyle.None;
            deviceList.DisplayMember = "Name";
            deviceList.Font = new Font(Font.FontFamily, 10F);
            deviceList.IntegralHeight = false;
            deviceList.SelectedIndexChanged += DeviceList_SelectedIndexChanged;
            stateLabel.Dock = DockStyle.Bottom;
            stateLabel.Height = 94;
            stateLabel.Padding = new Padding(8);
            stateLabel.BackColor = Color.White;
            stateLabel.ForeColor = Color.FromArgb(71, 85, 105);
            stateLabel.Font = new Font(Font.FontFamily, 9F);
            split.Panel1.Controls.Add(deviceList);
            split.Panel1.Controls.Add(stateLabel);
            split.Panel1.Controls.Add(deviceTitle);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildOverviewTab());
            tabs.TabPages.Add(BuildMappingTab());
            tabs.TabPages.Add(BuildDebugTab());
            split.Panel2.Controls.Add(tabs);
        }

        private TabPage BuildOverviewTab()
        {
            var tab = new TabPage("设备概览") { BackColor = Color.White, Padding = new Padding(8) };
            deviceProperties.Dock = DockStyle.Fill;
            deviceProperties.HelpVisible = true;
            deviceProperties.ToolbarVisible = false;
            deviceProperties.PropertySort = PropertySort.Categorized;
            deviceProperties.PropertyValueChanged += (sender, args) =>
            {
                deviceList.DisplayMember = string.Empty;
                deviceList.DisplayMember = "Name";
                RefreshSummary();
            };
            tab.Controls.Add(deviceProperties);
            return tab;
        }

        private TabPage BuildMappingTab()
        {
            var tab = new TabPage("变量映射") { BackColor = Color.White, Padding = new Padding(8) };
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 78,
                WrapContents = true,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(4, 5, 4, 4)
            };
            variableSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            variableSelector.Width = 190;
            variableSelector.Margin = new Padding(12, 5, 2, 4);
            actions.Controls.AddRange(new Control[]
            {
                CommandButton("新增映射", AddMap),
                CommandButton("删除映射", DeleteMap),
                new Label { Text = "首变量：", AutoSize = true, Padding = new Padding(5, 9, 0, 0) },
                variableSelector,
                CommandButton("填入变量", ApplySelectedVariable),
                CommandButton("采用PLC值", (s,e) => ResolveConflict(PlcConflictResolution.UsePlcValue)),
                CommandButton("采用本地值", (s,e) => ResolveConflict(PlcConflictResolution.UseLocalValue))
            });
            ConfigureMapGrid();
            tab.Controls.Add(mapGrid);
            tab.Controls.Add(actions);
            return tab;
        }

        private TabPage BuildDebugTab()
        {
            var tab = new TabPage("在线调试") { BackColor = Color.White, Padding = new Padding(8) };
            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 126,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(4, 7, 4, 3),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            for (int index = 0; index < 3; index++)
            {
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            }
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            debugArea.DropDownStyle = ComboBoxStyle.DropDownList;
            debugArea.DataSource = Enum.GetValues(typeof(PlcArea));
            debugArea.FormattingEnabled = true;
            debugArea.Format += (sender, args) =>
            {
                if (args.ListItem is PlcArea area) args.Value = EnumDisplay(area);
            };
            debugType.DropDownStyle = ComboBoxStyle.DropDownList;
            debugType.FormattingEnabled = true;
            debugType.Format += (sender, args) =>
            {
                if (args.ListItem is PlcDataType dataType) args.Value = EnumDisplay(dataType);
            };
            debugArea.SelectedIndexChanged += (sender, args) => RefreshDebugDataTypes();
            RefreshDebugDataTypes();
            debugAddress.Maximum = 65535;
            debugCount.Minimum = 1;
            debugCount.Maximum = 1000;
            debugCount.Value = 1;
            debugStringLength.Maximum = 2000;
            fields.Controls.Add(DebugField("地址区", debugArea), 0, 0);
            fields.Controls.Add(DebugField("起始地址", debugAddress), 1, 0);
            fields.Controls.Add(DebugField("数据类型", debugType), 2, 0);
            fields.Controls.Add(DebugField("元素数", debugCount), 0, 1);
            fields.Controls.Add(DebugField("字符串字节数", debugStringLength), 1, 1);
            fields.Controls.Add(DebugField("写入值（逗号分隔）", debugValue), 2, 1);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 46,
                WrapContents = false,
                BackColor = Color.White,
                Padding = new Padding(4, 5, 4, 4)
            };
            actions.Controls.AddRange(new Control[]
            {
                CommandButton("读取一次", async (s,e) => await DebugReadAsync("单次读取"), true),
                CommandButton("写入一次", async (s,e) => await DebugWriteAsync(), false),
                CommandButton("连续监视", ToggleMonitor),
                CommandButton("性能测试", async (s,e) => await PerformanceTestAsync())
            });
            monitorLabel.AutoSize = true;
            monitorLabel.Padding = new Padding(8, 9, 0, 0);
            monitorLabel.ForeColor = Color.FromArgb(71, 85, 105);
            actions.Controls.Add(monitorLabel);

            historyGrid.Dock = DockStyle.Fill;
            historyGrid.AutoGenerateColumns = true;
            historyGrid.DataSource = history;
            var historyHeaders = new Dictionary<string, string>
            {
                ["Time"] = "时间",
                ["Action"] = "操作",
                ["Device"] = "设备",
                ["Address"] = "PLC地址",
                ["Success"] = "结果",
                ["Result"] = "数据 / 错误信息",
                ["ElapsedMs"] = "耗时(ms)"
            };
            foreach (DataGridViewColumn column in historyGrid.Columns)
            {
                if (historyHeaders.TryGetValue(column.Name, out string header)) column.HeaderText = header;
            }
            historyGrid.ReadOnly = true;
            historyGrid.AllowUserToAddRows = false;
            historyGrid.AllowUserToDeleteRows = false;
            historyGrid.BackgroundColor = Color.White;
            historyGrid.BorderStyle = BorderStyle.None;
            historyGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            historyGrid.ColumnHeadersHeight = 32;
            historyGrid.RowTemplate.Height = 28;
            historyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            historyGrid.MultiSelect = false;
            historyGrid.EnableHeadersVisualStyles = false;
            historyGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font(Font, FontStyle.Bold)
            };
            historyGrid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(248, 250, 252)
            };
            historyGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            tab.Controls.Add(historyGrid);
            tab.Controls.Add(actions);
            tab.Controls.Add(fields);
            return tab;
        }

        private void ConfigureMapGrid()
        {
            mapGrid.Dock = DockStyle.Fill;
            mapGrid.AllowUserToAddRows = false;
            mapGrid.AllowUserToDeleteRows = false;
            mapGrid.AllowUserToResizeRows = false;
            mapGrid.BackgroundColor = Color.White;
            mapGrid.RowHeadersVisible = false;
            mapGrid.BorderStyle = BorderStyle.None;
            mapGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            mapGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            mapGrid.ColumnHeadersHeight = 34;
            mapGrid.RowTemplate.Height = 30;
            mapGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            mapGrid.MultiSelect = false;
            mapGrid.EnableHeadersVisualStyles = false;
            mapGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font(Font, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            mapGrid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(248, 250, 252)
            };
            mapGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Visible = false });
            mapGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", Width = 58 });
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MapName", HeaderText = "名称", Width = 120 });
            mapGrid.Columns.Add(EnumColumn<PlcArea>("Area", "地址区"));
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StartAddress", HeaderText = "起始地址", Width = 82 });
            mapGrid.Columns.Add(EnumColumn<PlcDataType>("DataType", "数据类型"));
            mapGrid.Columns.Add(EnumColumn<PlcMapDirection>("Direction", "方向"));
            mapGrid.Columns.Add(EnumColumn<PlcMapPriority>("Priority", "优先级"));
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ElementCount", HeaderText = "元素数", Width = 72 });
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StringByteLength", HeaderText = "字符串字节数", Width = 105 });
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "VariableNames",
                HeaderText = "变量列表（下拉选择或逗号分隔）",
                Width = 240
            });
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ChangeTolerance", HeaderText = "变化容差", Width = 82 });
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RuntimeState", HeaderText = "运行状态", ReadOnly = true, Width = 96 });
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RuntimeMessage", HeaderText = "状态信息", ReadOnly = true, Width = 240 });
            mapGrid.Columns["Area"].Width = 190;
            mapGrid.Columns["DataType"].Width = 155;
            mapGrid.Columns["Direction"].Width = 110;
            mapGrid.Columns["Priority"].Width = 112;
        }

        private void FrmPlc_Load(object sender, EventArgs e)
        {
            ReloadDraft();
            refreshTimer.Start();
        }

        private void FrmPlc_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                ReloadDraft();
                refreshTimer.Start();
            }
            else
            {
                refreshTimer.Stop();
                monitorTimer.Stop();
            }
        }

        private void ReloadDraft()
        {
            if (SF.plcStore == null) return;
            loading = true;
            try
            {
                draft = SF.plcStore.GetSnapshot();
                currentDevice = null;
                deviceList.DataSource = null;
                deviceList.DataSource = draft.Devices;
                deviceList.DisplayMember = "Name";
                if (draft.Devices.Count > 0)
                {
                    deviceList.SelectedIndex = 0;
                    ShowDevice(draft.Devices[0]);
                }
                else ShowDevice(null);
                RefreshSummary();
            }
            finally { loading = false; }
        }

        private void DeviceList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading) return;
            if (!CommitMaps(currentDevice, out string error))
            {
                MessageBox.Show(error, "PLC映射", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ShowDevice(deviceList.SelectedItem as PlcDeviceConfig);
        }

        private void ShowDevice(PlcDeviceConfig device)
        {
            currentDevice = device;
            deviceProperties.SelectedObject = device == null ? null : new DevicePropertyView(device);
            RefreshVariableSelector();
            LoadMaps(device);
            RefreshRuntimeState();
        }

        private void AddDevice(object sender, EventArgs e)
        {
            if (!CommitMaps(currentDevice, out string error)) { ShowError(error); return; }
            string name = NextDeviceName();
            PlcDeviceConfig device = PlcDeviceConfig.Create(PlcDeviceProfile.GenericModbusTcp);
            device.Name = name;
            device.IpAddress = "127.0.0.1";
            draft.Devices.Add(device);
            deviceList.DataSource = null;
            deviceList.DataSource = draft.Devices;
            deviceList.DisplayMember = "Name";
            deviceList.SelectedItem = device;
            ShowDevice(device);
            RefreshSummary();
        }

        private void DeleteDevice(object sender, EventArgs e)
        {
            if (currentDevice == null) return;
            PlcDeviceRuntimeSnapshot runtime = GetCurrentRuntime();
            if (runtime?.State == PlcRuntimeState.Mapping) { ShowError("请先停止该设备映射。" ); return; }
            if (MessageBox.Show($"确认删除PLC设备[{currentDevice.Name}]及其全部映射？", "删除确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            draft.Devices.Remove(currentDevice);
            currentDevice = null;
            deviceList.DataSource = null;
            deviceList.DataSource = draft.Devices;
            deviceList.DisplayMember = "Name";
            if (draft.Devices.Count > 0) deviceList.SelectedIndex = 0;
            else ShowDevice(null);
            RefreshSummary();
        }

        private void SaveConfiguration(object sender, EventArgs e)
        {
            if (!CommitMaps(currentDevice, out string error)) { ShowError(error); return; }
            if ((SF.plcRuntime?.GetSnapshots() ?? new List<PlcDeviceRuntimeSnapshot>())
                .Any(item => item.State == PlcRuntimeState.Mapping))
            { ShowError("保存配置前必须停止全部PLC设备映射。" ); return; }
            if (!SF.plcStore.Save(SF.ConfigPath, draft, SF.valueStore, out error)) { ShowError(error); return; }
            if (SF.plcRuntime == null || !SF.plcRuntime.ReloadConfiguration(false, out error))
            {
                ShowError(error ?? "PLC运行时未初始化，配置已保存但未加载。");
                return;
            }
            const string message = "PLC配置已保存并加载；未自动连接设备，可按需重新初始化或启动映射。";
            SF.frmInfo?.PrintInfo(message, FrmInfo.Level.Normal);
            MessageBox.Show(message, "PLC配置",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadDraft();
        }

        private void ReinitializeDevice(object sender, EventArgs e)
        {
            ExecuteDeviceAction("重新初始化", (string name, out string error) =>
            {
                if (SF.plcRuntime == null) { error = "PLC运行时未初始化。"; return false; }
                return SF.plcRuntime.TryReinitialize(name, out error);
            });
        }

        private void StartMapping(object sender, EventArgs e)
        {
            ExecuteDeviceAction("启动映射", (string name, out string error) =>
            {
                if (SF.plcRuntime == null) { error = "PLC运行时未初始化。"; return false; }
                return SF.plcRuntime.TryStartMapping(name, out error);
            });
        }

        private void StopMapping(object sender, EventArgs e)
        {
            ExecuteDeviceAction("停止映射", (string name, out string error) =>
            {
                if (SF.plcRuntime == null) { error = "PLC运行时未初始化。"; return false; }
                return SF.plcRuntime.TryStopMapping(name, out error);
            });
        }

        private void ExecuteDeviceAction(string title, DeviceAction action)
        {
            if (currentDevice == null) { ShowError("请先选择PLC设备。" ); return; }
            if (action == null) { ShowError(title + "失败：PLC运行时未初始化。" ); return; }
            if (!action(currentDevice.Name, out string error)) { ShowError(error ?? title + "失败。" ); return; }
            RefreshRuntimeState();
        }

        private void AddMap(object sender, EventArgs e)
        {
            if (currentDevice == null) { ShowError("请先新增或选择PLC设备。" ); return; }
            int index = currentDevice.Mappings.Count + 1;
            var map = new PlcMapConfig { Name = "映射" + index };
            currentDevice.Mappings.Add(map);
            AddMapRow(map);
        }

        private void DeleteMap(object sender, EventArgs e)
        {
            if (mapGrid.CurrentRow == null) return;
            mapGrid.Rows.Remove(mapGrid.CurrentRow);
        }

        private void ApplySelectedVariable(object sender, EventArgs e)
        {
            if (mapGrid.CurrentRow == null) { ShowError("请先选择需要绑定变量的映射项。" ); return; }
            if (!(variableSelector.SelectedItem is VariableChoice first))
            { ShowError("变量表中没有可选择的变量。" ); return; }
            try
            {
                DataGridViewRow row = mapGrid.CurrentRow;
                PlcDataType dataType = RequireEnum<PlcDataType>(row, "DataType");
                int count = RequireInt(row, "ElementCount");
                if (dataType == PlcDataType.String && count != 1)
                { ShowError("String映射只能绑定一个变量，请先将元素数设为1。" ); return; }
                string requiredType = dataType == PlcDataType.String ? "string" : "double";
                var names = new List<string>();
                for (int offset = 0; offset < count; offset++)
                {
                    if (!SF.valueStore.TryGetValueByIndex(first.Index + offset, out DicValue value) || value == null)
                    { ShowError($"变量表中不存在连续第{offset + 1}个变量，无法按数量展开。" ); return; }
                    if (!string.Equals(value.Type, requiredType, StringComparison.OrdinalIgnoreCase))
                    { ShowError($"变量[{value.Name}]类型为{value.Type}，当前{dataType}映射要求{requiredType}变量。" ); return; }
                    names.Add(value.Name);
                }
                row.Cells["VariableNames"].Value = string.Join(",", names);
            }
            catch (Exception ex)
            {
                ShowError("填入变量失败：" + ex.Message);
            }
        }

        private void ResolveConflict(PlcConflictResolution resolution)
        {
            if (currentDevice == null || mapGrid.CurrentRow == null) { ShowError("请选择冲突映射项。" ); return; }
            string mapId = Convert.ToString(mapGrid.CurrentRow.Cells["Id"].Value);
            PlcMapRuntimeSnapshot mapRuntime = GetCurrentRuntime()?.Mappings?.FirstOrDefault(item => item.MapId == mapId);
            if (mapRuntime?.State != PlcMapRuntimeState.Conflict) { ShowError("所选映射项当前不是冲突状态。" ); return; }
            string side = resolution == PlcConflictResolution.UsePlcValue ? "PLC" : "本地";
            string message = $"确认以{side}值为准解除冲突？\r\nPLC：{FormatValues(mapRuntime.PlcValues)}\r\n本地：{FormatValues(mapRuntime.LocalValues)}\r\n此操作会覆盖另一侧。";
            if (MessageBox.Show(message, "PLC冲突处理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!SF.plcRuntime.TryResolveConflict(currentDevice.Name, mapId, resolution, out string error)) ShowError(error);
            RefreshRuntimeState();
        }

        private void LoadMaps(PlcDeviceConfig device)
        {
            mapGrid.Rows.Clear();
            if (device == null) return;
            foreach (PlcMapConfig map in device.Mappings) AddMapRow(map);
        }

        private void AddMapRow(PlcMapConfig map)
        {
            mapGrid.Rows.Add(map.Id, map.Enabled, map.Name, map.Area, map.StartAddress, map.DataType,
                map.Direction, map.Priority, map.ElementCount, map.StringByteLength,
                string.Join(",", map.VariableNames ?? new List<string>()),
                map.ChangeTolerance.ToString("G17", CultureInfo.InvariantCulture), string.Empty, string.Empty);
        }

        private bool CommitMaps(PlcDeviceConfig device, out string error)
        {
            error = null;
            if (device == null) return true;
            try
            {
                mapGrid.EndEdit();
                var maps = new List<PlcMapConfig>();
                foreach (DataGridViewRow row in mapGrid.Rows)
                {
                    var map = new PlcMapConfig
                    {
                        Id = RequireText(row, "Id"),
                        Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false),
                        Name = RequireText(row, "MapName"),
                        Area = RequireEnum<PlcArea>(row, "Area"),
                        StartAddress = RequireInt(row, "StartAddress"),
                        DataType = RequireEnum<PlcDataType>(row, "DataType"),
                        Direction = RequireEnum<PlcMapDirection>(row, "Direction"),
                        Priority = RequireEnum<PlcMapPriority>(row, "Priority"),
                        ElementCount = RequireInt(row, "ElementCount"),
                        StringByteLength = RequireInt(row, "StringByteLength"),
                        VariableNames = SplitVariables(Convert.ToString(row.Cells["VariableNames"].Value)),
                        ChangeTolerance = RequireDouble(row, "ChangeTolerance")
                    };
                    maps.Add(map);
                }
                device.Mappings = maps;
                return true;
            }
            catch (Exception ex)
            {
                error = "映射表存在无效值:" + ex.Message;
                return false;
            }
        }

        private async Task DebugReadAsync(string action)
        {
            if (!TryBuildDebugRequest(out string deviceName, out PlcMapConfig request, out string error)) { ShowError(error); return; }
            Stopwatch watch = Stopwatch.StartNew();
            var result = await Task.Run(() =>
            {
                bool ok = SF.plcRuntime.TryRead(deviceName, request, out object[] values, out string readError);
                return new DebugResult { Success = ok, Values = values, Error = readError };
            });
            watch.Stop();
            AddHistory(action, result.Success, result.Success ? FormatValues(result.Values) : result.Error, watch.ElapsedMilliseconds);
        }

        private async Task DebugWriteAsync()
        {
            if (!TryBuildDebugRequest(out string deviceName, out PlcMapConfig request, out string error)) { ShowError(error); return; }
            object[] values = request.DataType == PlcDataType.String
                ? new object[] { debugValue.Text }
                : debugValue.Text.Split(new[] { ',' }, StringSplitOptions.None).Select(item => (object)item.Trim()).ToArray();
            if (values.Length != request.ElementCount) { ShowError("写入值数量必须与元素数一致。" ); return; }
            if (!HslModbusAdapter.TryNormalizeValues(request.DataType, values, out _, out error)) { ShowError(error); return; }
            if (MessageBox.Show($"确认写入PLC？\r\n设备：{deviceName}\r\n地址：{request.Area}/{request.StartAddress}\r\n值：{FormatValues(values)}",
                "PLC写入确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Stopwatch watch = Stopwatch.StartNew();
            bool success = await Task.Run(() => SF.plcRuntime.TryWrite(deviceName, request, values, out error));
            watch.Stop();
            AddHistory("单次写入", success, success ? FormatValues(values) : error, watch.ElapsedMilliseconds);
        }

        private void ToggleMonitor(object sender, EventArgs e)
        {
            if (monitorTimer.Enabled)
            {
                monitorTimer.Stop();
                monitorLabel.Text = "监视已停止";
                lastMonitorSignature = null;
                return;
            }
            if (!TryBuildDebugRequest(out _, out _, out string error))
            {
                monitorLabel.Text = "监视未启动：" + error;
                ShowError(error);
                return;
            }
            lastMonitorSignature = null;
            monitorTimer.Start();
            monitorLabel.Text = "监视中，等待首次数据";
        }

        private async Task MonitorOnceAsync()
        {
            if (monitorBusy) return;
            monitorBusy = true;
            try
            {
                if (!TryBuildDebugRequest(out string deviceName, out PlcMapConfig request, out string error))
                {
                    monitorTimer.Stop();
                    monitorLabel.Text = "监视已停止：" + error;
                    lastMonitorSignature = null;
                    return;
                }
                Stopwatch watch = Stopwatch.StartNew();
                DebugResult result = await Task.Run(() =>
                {
                    bool ok = SF.plcRuntime.TryRead(deviceName, request, out object[] values, out string readError);
                    return new DebugResult { Success = ok, Values = values, Error = readError };
                });
                watch.Stop();
                string resultText = result.Success ? FormatValues(result.Values) : result.Error ?? string.Empty;
                string signature = $"{deviceName}|{request.Area}|{request.StartAddress}|{request.DataType}|"
                    + $"{request.ElementCount}|{request.StringByteLength}|{result.Success}|{resultText}";
                if (!string.Equals(signature, lastMonitorSignature, StringComparison.Ordinal))
                {
                    lastMonitorSignature = signature;
                    monitorLabel.Text = result.Success ? "监视中 · 当前值：" + resultText : "监视异常：" + resultText;
                    AddHistory("监视变化", result.Success, resultText, watch.ElapsedMilliseconds);
                }
            }
            finally { monitorBusy = false; }
        }

        private void RefreshDebugDataTypes()
        {
            PlcDataType selected = debugType.SelectedItem is PlcDataType value ? value : PlcDataType.Float;
            bool bitArea = debugArea.SelectedItem is PlcArea area
                && (area == PlcArea.Coil || area == PlcArea.DiscreteInput);
            List<PlcDataType> options = bitArea
                ? new List<PlcDataType> { PlcDataType.Boolean }
                : Enum.GetValues(typeof(PlcDataType)).Cast<PlcDataType>()
                    .Where(item => item != PlcDataType.Boolean).ToList();
            debugType.DataSource = options;
            debugType.SelectedItem = options.Contains(selected) ? selected : options[0];
        }

        private async Task PerformanceTestAsync()
        {
            if (!TryBuildDebugRequest(out string deviceName, out PlcMapConfig request, out string error)) { ShowError(error); return; }
            monitorTimer.Stop();
            monitorLabel.Text = "性能测试中";
            var result = await Task.Run(() =>
            {
                var samples = new List<long>();
                int success = 0;
                string lastError = string.Empty;
                for (int i = 0; i < 100; i++)
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    bool ok = SF.plcRuntime.TryRead(deviceName, request, out _, out string readError);
                    watch.Stop();
                    if (ok) { success++; samples.Add(watch.ElapsedMilliseconds); }
                    else lastError = readError;
                }
                samples.Sort();
                return new PerformanceResult
                {
                    SuccessCount = success,
                    Minimum = samples.Count == 0 ? 0 : samples[0],
                    Average = samples.Count == 0 ? 0 : samples.Average(),
                    P95 = samples.Count == 0 ? 0 : samples[(int)Math.Ceiling(samples.Count * 0.95) - 1],
                    Maximum = samples.Count == 0 ? 0 : samples[samples.Count - 1],
                    Error = lastError
                };
            });
            monitorLabel.Text = "性能测试完成";
            string text = $"成功 {result.SuccessCount}/100，min={result.Minimum}ms，avg={result.Average:F1}ms，P95={result.P95}ms，max={result.Maximum}ms";
            AddHistory("100次性能测试", result.SuccessCount == 100, result.SuccessCount == 100 ? text : text + "；" + result.Error, 0);
        }

        private bool TryBuildDebugRequest(out string deviceName, out PlcMapConfig request, out string error)
        {
            deviceName = currentDevice?.Name;
            request = null;
            error = null;
            if (string.IsNullOrWhiteSpace(deviceName)) { error = "请先选择PLC设备。"; return false; }
            PlcArea area = (PlcArea)debugArea.SelectedItem;
            PlcDataType type = (PlcDataType)debugType.SelectedItem;
            int count = (int)debugCount.Value;
            int stringLength = (int)debugStringLength.Value;
            if ((area == PlcArea.Coil || area == PlcArea.DiscreteInput) != (type == PlcDataType.Boolean))
            { error = "Boolean只允许线圈区，其他类型只允许寄存器区。"; return false; }
            if (type == PlcDataType.String && (count != 1 || stringLength < 1))
            { error = "String要求元素数为1且字符串字节数大于0。"; return false; }
            if (type != PlcDataType.String && stringLength != 0)
            { error = "非String类型的字符串字节数必须为0。"; return false; }
            request = new PlcMapConfig
            {
                Name = "在线调试",
                Area = area,
                StartAddress = (int)debugAddress.Value,
                DataType = type,
                Direction = PlcMapDirection.ReadFromPlc,
                ElementCount = count,
                StringByteLength = stringLength,
                VariableNames = Enumerable.Repeat("调试", count).ToList()
            };
            if ((long)request.StartAddress + PlcConfigStore.GetAddressSpan(request) - 1 > 65535)
            { error = "调试访问范围超过65535。"; request = null; return false; }
            return true;
        }

        private void RefreshRuntimeState()
        {
            PlcDeviceRuntimeSnapshot runtime = GetCurrentRuntime();
            if (currentDevice == null)
            {
                stateLabel.Text = "未选择设备";
            }
            else
            {
                string state = runtime == null ? "未初始化" : FormatRuntimeState(runtime.State);
                string mode = currentDevice.AutoConnect ? "自动连接" : "手动连接";
                stateLabel.Text = $"状态：{state}    方式：{mode}\r\n"
                    + $"最后通讯：{FormatUtc(runtime?.LastCommunicationUtc)}    耗时：{runtime?.LastScanElapsedMs ?? 0}ms";
                if (!string.IsNullOrWhiteSpace(runtime?.LastError))
                {
                    stateLabel.Text += "\r\n" + runtime.LastError;
                }
            }
            if (currentDevice == null || runtime?.Mappings == null) return;
            foreach (DataGridViewRow row in mapGrid.Rows)
            {
                string id = Convert.ToString(row.Cells["Id"].Value);
                PlcMapRuntimeSnapshot map = runtime.Mappings.FirstOrDefault(item => item.MapId == id);
                row.Cells["RuntimeState"].Value = map == null ? string.Empty : FormatMapState(map.State);
                row.Cells["RuntimeMessage"].Value = map?.Message ?? string.Empty;
                row.DefaultCellStyle.BackColor = map?.State == PlcMapRuntimeState.Conflict
                    ? Color.FromArgb(254, 226, 226)
                    : map?.State == PlcMapRuntimeState.Faulted
                        ? Color.FromArgb(255, 237, 213)
                        : Color.White;
            }
            RefreshSummary();
        }

        private PlcDeviceRuntimeSnapshot GetCurrentRuntime()
        {
            if (currentDevice == null || SF.plcRuntime == null) return null;
            return SF.plcRuntime.GetSnapshots().FirstOrDefault(item =>
                string.Equals(item.DeviceName, currentDevice.Name, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshSummary()
        {
            IReadOnlyList<PlcDeviceRuntimeSnapshot> states = SF.plcRuntime?.GetSnapshots() ?? new List<PlcDeviceRuntimeSnapshot>();
            summaryLabel.Text = $"设备 {draft.Devices.Count} 台 · 映射 {draft.Devices.Sum(item => item.Mappings?.Count ?? 0)} 项 · 在线 {states.Count(item => item.State == PlcRuntimeState.Ready || item.State == PlcRuntimeState.Mapping || item.State == PlcRuntimeState.Stopped)} 台 · 故障 {states.Count(item => item.State == PlcRuntimeState.Faulted)} 台";
        }

        private void RefreshVariableSelector()
        {
            List<VariableChoice> choices = new List<VariableChoice>();
            if (SF.valueStore != null)
            {
                foreach (string name in SF.valueStore.GetValueNames())
                {
                    if (SF.valueStore.TryGetValueByName(name, out DicValue value) && value != null)
                    {
                        choices.Add(new VariableChoice { Index = value.Index, Name = value.Name, Type = value.Type });
                    }
                }
            }
            variableSelector.DataSource = choices.OrderBy(item => item.Index).ToList();
        }

        private static string FormatRuntimeState(PlcRuntimeState state)
        {
            switch (state)
            {
                case PlcRuntimeState.Ready:
                case PlcRuntimeState.Stopped: return "已就绪";
                case PlcRuntimeState.Mapping: return "映射中";
                case PlcRuntimeState.Faulted: return "通讯故障";
                default: return "未初始化";
            }
        }

        private static string FormatMapState(PlcMapRuntimeState state)
        {
            switch (state)
            {
                case PlcMapRuntimeState.Idle: return "未启动";
                case PlcMapRuntimeState.Normal: return "正常";
                case PlcMapRuntimeState.Conflict: return "冲突隔离";
                case PlcMapRuntimeState.Faulted: return "故障隔离";
                default: return state.ToString();
            }
        }

        private void AddHistory(string action, bool success, string result, long elapsedMs)
        {
            history.Insert(0, new DebugHistoryItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                Action = action,
                Device = currentDevice?.Name ?? string.Empty,
                Address = debugArea.SelectedItem is PlcArea area
                    ? $"{EnumDisplay(area)} / {debugAddress.Value}"
                    : $"{debugArea.SelectedItem} / {debugAddress.Value}",
                Success = success ? "成功" : "失败",
                Result = result ?? string.Empty,
                ElapsedMs = elapsedMs
            });
            while (history.Count > 200) history.RemoveAt(history.Count - 1);
        }

        private string NextDeviceName()
        {
            for (int index = 1; ; index++)
            {
                string name = "PLC" + index;
                if (!draft.Devices.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
            }
        }

        private static Button CommandButton(string text, EventHandler click, bool primary = false, bool danger = false)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 31,
                FlatStyle = FlatStyle.Flat,
                BackColor = danger ? Color.FromArgb(254, 242, 242) : primary ? Color.FromArgb(37, 99, 235) : Color.White,
                ForeColor = danger ? Color.FromArgb(185, 28, 28) : primary ? Color.White : Color.FromArgb(51, 65, 85),
                Margin = new Padding(4, 1, 4, 1),
                Padding = new Padding(8, 0, 8, 0)
            };
            button.FlatAppearance.BorderColor = danger ? Color.FromArgb(252, 165, 165) : primary ? Color.FromArgb(37, 99, 235) : Color.FromArgb(203, 213, 225);
            button.Click += click;
            return button;
        }

        private static Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                Padding = new Padding(4, 8, 2, 0),
                Margin = new Padding(4, 1, 0, 1)
            };
        }

        private static Control CommandSeparator()
        {
            return new Panel
            {
                Width = 1,
                Height = 28,
                BackColor = Color.FromArgb(203, 213, 225),
                Margin = new Padding(10, 2, 6, 1)
            };
        }

        private static Control DebugField(string label, Control control)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(5, 2, 5, 2) };
            panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(71, 85, 105) });
            control.Dock = DockStyle.Bottom;
            control.Height = 28;
            panel.Controls.Add(control);
            return panel;
        }

        private static DataGridViewComboBoxColumn EnumColumn<T>(string name, string header) where T : struct
        {
            List<EnumOption<T>> options = Enum.GetValues(typeof(T)).Cast<T>()
                .Select(value => new EnumOption<T> { Value = value, Display = EnumDisplay(value) })
                .ToList();
            return new DataGridViewComboBoxColumn
            {
                Name = name,
                HeaderText = header,
                DataSource = options,
                DisplayMember = "Display",
                ValueMember = "Value",
                ValueType = typeof(T),
                FlatStyle = FlatStyle.Flat
            };
        }

        private static string EnumDisplay<T>(T value) where T : struct
        {
            if (typeof(T) == typeof(PlcArea))
            {
                switch ((PlcArea)(object)value)
                {
                    case PlcArea.Coil: return "线圈 Coil";
                    case PlcArea.DiscreteInput: return "离散输入 DiscreteInput";
                    case PlcArea.HoldingRegister: return "保持寄存器 HoldingRegister";
                    case PlcArea.InputRegister: return "输入寄存器 InputRegister";
                }
            }
            if (typeof(T) == typeof(PlcDataType))
            {
                switch ((PlcDataType)(object)value)
                {
                    case PlcDataType.String: return "字符串 String";
                    case PlcDataType.Boolean: return "布尔 Boolean";
                    case PlcDataType.Byte: return "字节 Byte";
                    case PlcDataType.UShort: return "无符号16位 UShort";
                    case PlcDataType.Short: return "有符号16位 Short";
                    case PlcDataType.UInt: return "无符号32位 UInt";
                    case PlcDataType.Int: return "有符号32位 Int";
                    case PlcDataType.Float: return "单精度浮点 Float";
                    case PlcDataType.Double: return "双精度浮点 Double";
                }
            }
            if (typeof(T) == typeof(PlcMapDirection))
            {
                switch ((PlcMapDirection)(object)value)
                {
                    case PlcMapDirection.ReadFromPlc: return "PLC → 平台";
                    case PlcMapDirection.WriteToPlc: return "平台 → PLC";
                    case PlcMapDirection.Bidirectional: return "双向同步";
                }
            }
            if (typeof(T) == typeof(PlcMapPriority))
            {
                switch ((PlcMapPriority)(object)value)
                {
                    case PlcMapPriority.High: return "高（每轮）";
                    case PlcMapPriority.Medium: return "中（每10轮）";
                    case PlcMapPriority.Low: return "低（每40轮）";
                }
            }
            return value.ToString();
        }

        private static string RequireText(DataGridViewRow row, string column)
        {
            string value = Convert.ToString(row.Cells[column].Value)?.Trim();
            if (string.IsNullOrEmpty(value)) throw new FormatException($"{column}为空");
            return value;
        }

        private static int RequireInt(DataGridViewRow row, string column)
        {
            if (!int.TryParse(Convert.ToString(row.Cells[column].Value), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value)) throw new FormatException($"{column}不是整数");
            return value;
        }

        private static double RequireDouble(DataGridViewRow row, string column)
        {
            if (!double.TryParse(Convert.ToString(row.Cells[column].Value), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException($"{column}不是有限数");
            return value;
        }

        private static T RequireEnum<T>(DataGridViewRow row, string column) where T : struct
        {
            object cellValue = row.Cells[column].Value;
            if (cellValue is EnumOption<T> option)
            {
                return option.Value;
            }
            if (cellValue is T typed && Enum.IsDefined(typeof(T), typed))
            {
                return typed;
            }
            if (!Enum.TryParse(Convert.ToString(cellValue), false, out T parsed)
                || !Enum.IsDefined(typeof(T), parsed)) throw new FormatException($"{column}无效");
            return parsed;
        }

        private static List<string> SplitVariables(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split(new[] { ',' }, StringSplitOptions.None).Select(item => item.Trim()).ToList();
        }

        private static string FormatValues(IEnumerable<object> values)
        {
            return values == null ? string.Empty : string.Join(", ", values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)));
        }

        private static string FormatUtc(DateTime? utc)
        {
            return utc.HasValue ? utc.Value.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) : "无";
        }

        private static void ShowError(string error)
        {
            MessageBox.Show(error ?? "操作失败。", "PLC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private delegate bool DeviceAction(string deviceName, out string error);

        private sealed class DebugResult { public bool Success; public object[] Values; public string Error; }
        private sealed class PerformanceResult { public int SuccessCount; public long Minimum; public double Average; public long P95; public long Maximum; public string Error; }
        private sealed class DebugHistoryItem
        {
            public string Time { get; set; }
            public string Action { get; set; }
            public string Device { get; set; }
            public string Address { get; set; }
            public string Success { get; set; }
            public string Result { get; set; }
            public long ElapsedMs { get; set; }
        }

        private sealed class VariableChoice
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }

            public override string ToString()
            {
                return $"{Index}: {Name} ({Type})";
            }
        }

        private sealed class EnumOption<T>
        {
            public T Value { get; set; }
            public string Display { get; set; }

            public override string ToString()
            {
                return Display;
            }
        }

        private sealed class DevicePropertyView
        {
            private readonly PlcDeviceConfig device;
            public DevicePropertyView(PlcDeviceConfig device) { this.device = device; }

            [Category("身份"), DisplayName("名称")]
            public string Name { get => device.Name; set => device.Name = value; }
            [Category("身份"), DisplayName("设备类型")]
            public PlcDeviceProfile Profile
            {
                get => device.Profile;
                set
                {
                    if (device.Profile != value) device.IsStringReverse = value == PlcDeviceProfile.InovanceModbusTcp;
                    device.Profile = value;
                }
            }
            [Category("网络"), DisplayName("IPv4地址")]
            public string IpAddress { get => device.IpAddress; set => device.IpAddress = value; }
            [Category("网络"), DisplayName("端口")]
            public int Port { get => device.Port; set => device.Port = value; }
            [Category("网络"), DisplayName("站号")]
            public int UnitId { get => device.UnitId; set => device.UnitId = value; }
            [Category("网络"), DisplayName("连接超时(ms)")]
            public int ConnectTimeoutMs { get => device.ConnectTimeoutMs; set => device.ConnectTimeoutMs = value; }
            [Category("网络"), DisplayName("自动连接"), Description("开启后平台启动自动连接，断线后自动重连；关闭后只能通过重新初始化手动连接。")]
            public bool AutoConnect { get => device.AutoConnect; set => device.AutoConnect = value; }
            [Category("映射"), DisplayName("扫描周期(ms)")]
            public int ScanIntervalMs { get => device.ScanIntervalMs; set => device.ScanIntervalMs = value; }
            [Category("数据格式"), DisplayName("字节序")]
            public PlcDataFormat DataFormat
            {
                get
                {
                    return Enum.TryParse(device.DataFormat, false, out PlcDataFormat value)
                        && Enum.IsDefined(typeof(PlcDataFormat), value)
                        ? value
                        : PlcDataFormat.CDAB;
                }
                set => device.DataFormat = value.ToString();
            }
            [Category("数据格式"), DisplayName("字符串反转")]
            public bool IsStringReverse { get => device.IsStringReverse; set => device.IsStringReverse = value; }
            [Category("数据格式"), DisplayName("地址从0开始")]
            public bool AddressStartWithZero { get => device.AddressStartWithZero; set => device.AddressStartWithZero = value; }
            [Category("映射"), DisplayName("状态变量")]
            public string StatusVariableName { get => device.StatusVariableName; set => device.StatusVariableName = value; }
        }
    }
}
