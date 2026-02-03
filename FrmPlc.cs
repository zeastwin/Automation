using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public class FrmPlc : Form
    {
        private SplitContainer splitContainer;
        private GroupBox groupDevices;
        private GroupBox groupMaps;
        private DataGridView dgvDevices;
        private DataGridView dgvMaps;
        private ContextMenuStrip menuDevices;
        private ContextMenuStrip menuMaps;
        private ToolStripMenuItem menuDeviceAdd;
        private ToolStripMenuItem menuDeviceModify;
        private ToolStripMenuItem menuDeviceDelete;
        private ToolStripMenuItem menuDeviceSave;
        private ToolStripMenuItem menuMapAdd;
        private ToolStripMenuItem menuMapModify;
        private ToolStripMenuItem menuMapDelete;
        private ToolStripMenuItem menuMapReconnect;
        private ToolStripMenuItem menuMapStart;
        private ToolStripMenuItem menuMapStop;
        private ToolStripMenuItem menuMapSave;
        private Label lblStatus;

        private readonly BindingList<PlcDevice> deviceBinding = new BindingList<PlcDevice>();
        private readonly BindingList<PlcMapItem> mapBinding = new BindingList<PlcMapItem>();

        private CancellationTokenSource mappingCts;
        private Task mappingTask;
        private volatile bool mappingRunning;
        private readonly int mappingIntervalMs = 200;

        public FrmPlc()
        {
            InitializeComponent();
            FormClosing += FrmPlc_FormClosing;
            Load += FrmPlc_Load;
        }

        private void InitializeComponent()
        {
            splitContainer = new SplitContainer();
            groupDevices = new GroupBox();
            groupMaps = new GroupBox();
            dgvDevices = new DataGridView();
            dgvMaps = new DataGridView();
            menuDevices = new ContextMenuStrip();
            menuMaps = new ContextMenuStrip();
            menuDeviceAdd = new ToolStripMenuItem();
            menuDeviceModify = new ToolStripMenuItem();
            menuDeviceDelete = new ToolStripMenuItem();
            menuDeviceSave = new ToolStripMenuItem();
            menuMapAdd = new ToolStripMenuItem();
            menuMapModify = new ToolStripMenuItem();
            menuMapDelete = new ToolStripMenuItem();
            menuMapReconnect = new ToolStripMenuItem();
            menuMapStart = new ToolStripMenuItem();
            menuMapStop = new ToolStripMenuItem();
            menuMapSave = new ToolStripMenuItem();
            lblStatus = new Label();

            SuspendLayout();

            splitContainer.Dock = DockStyle.Fill;
            splitContainer.Orientation = Orientation.Vertical;
            splitContainer.BorderStyle = BorderStyle.FixedSingle;
            splitContainer.SplitterWidth = 6;

            groupDevices.Text = "PLC设备";
            groupDevices.Dock = DockStyle.Fill;
            groupDevices.Padding = new Padding(8, 20, 8, 8);
            groupDevices.BackColor = Color.White;

            groupMaps.Text = "PLC映射";
            groupMaps.Dock = DockStyle.Fill;
            groupMaps.Padding = new Padding(8, 20, 8, 8);
            groupMaps.BackColor = Color.White;

            dgvDevices.Dock = DockStyle.Fill;
            dgvDevices.AutoGenerateColumns = false;
            dgvDevices.RowHeadersVisible = false;
            dgvDevices.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvDevices.AllowUserToAddRows = false;
            dgvDevices.AllowUserToDeleteRows = false;
            dgvDevices.MultiSelect = false;
            dgvDevices.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvDevices.ContextMenuStrip = menuDevices;
            dgvDevices.CellEndEdit += DgvDevices_CellEndEdit;
            dgvDevices.DataError += Dgv_DataError;
            InitGridStyle(dgvDevices);

            dgvMaps.Dock = DockStyle.Fill;
            dgvMaps.AutoGenerateColumns = false;
            dgvMaps.RowHeadersVisible = false;
            dgvMaps.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvMaps.AllowUserToAddRows = false;
            dgvMaps.AllowUserToDeleteRows = false;
            dgvMaps.MultiSelect = false;
            dgvMaps.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvMaps.ContextMenuStrip = menuMaps;
            dgvMaps.CellEndEdit += DgvMaps_CellEndEdit;
            dgvMaps.DataError += Dgv_DataError;
            InitGridStyle(dgvMaps);

            menuDeviceAdd.Text = "添加";
            menuDeviceAdd.Click += MenuDeviceAdd_Click;
            menuDeviceModify.Text = "修改";
            menuDeviceModify.Click += MenuDeviceModify_Click;
            menuDeviceDelete.Text = "删除";
            menuDeviceDelete.Click += MenuDeviceDelete_Click;
            menuDeviceSave.Text = "保存";
            menuDeviceSave.Click += MenuSave_Click;
            menuDevices.Items.AddRange(new ToolStripItem[] { menuDeviceAdd, menuDeviceModify, menuDeviceDelete, new ToolStripSeparator(), menuDeviceSave });

            menuMapAdd.Text = "添加";
            menuMapAdd.Click += MenuMapAdd_Click;
            menuMapModify.Text = "修改";
            menuMapModify.Click += MenuMapModify_Click;
            menuMapDelete.Text = "删除";
            menuMapDelete.Click += MenuMapDelete_Click;
            menuMapReconnect.Text = "重新初始化";
            menuMapReconnect.Click += MenuMapReconnect_Click;
            menuMapStart.Text = "开始映射";
            menuMapStart.Click += MenuMapStart_Click;
            menuMapStop.Text = "停止映射";
            menuMapStop.Click += MenuMapStop_Click;
            menuMapSave.Text = "保存";
            menuMapSave.ShortcutKeys = Keys.Control | Keys.S;
            menuMapSave.Click += MenuSave_Click;
            menuMaps.Items.AddRange(new ToolStripItem[]
            {
                menuMapAdd,
                menuMapModify,
                menuMapDelete,
                new ToolStripSeparator(),
                menuMapReconnect,
                menuMapStart,
                menuMapStop,
                new ToolStripSeparator(),
                menuMapSave
            });

            lblStatus.Dock = DockStyle.Bottom;
            lblStatus.Height = 24;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Text = "映射状态：停止";
            lblStatus.BackColor = Color.FromArgb(237, 242, 247);
            lblStatus.BorderStyle = BorderStyle.FixedSingle;
            lblStatus.Padding = new Padding(8, 0, 0, 0);

            groupDevices.Controls.Add(dgvDevices);
            groupMaps.Controls.Add(dgvMaps);
            groupMaps.Controls.Add(lblStatus);

            splitContainer.Panel1.Controls.Add(groupDevices);
            splitContainer.Panel2.Controls.Add(groupMaps);

            Controls.Add(splitContainer);

            Text = "PLC";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 1100;
            Height = 650;
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("微软雅黑", 9F);

            ResumeLayout(false);

            InitDeviceColumns();
            InitMapColumns();
        }

        private void FrmPlc_Load(object sender, EventArgs e)
        {
            LoadConfig();
            UpdateMappingStatus();
            ApplySplitterLayout();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplySplitterLayout();
        }

        private void FrmPlc_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void InitDeviceColumns()
        {
            dgvDevices.Columns.Clear();

            dgvDevices.Columns.Add(BuildTextColumn("Name", "名称", 120));
            dgvDevices.Columns.Add(BuildComboColumn("Protocol", "协议", PlcConstants.Protocols, 90));
            dgvDevices.Columns.Add(BuildComboColumn("CpuType", "CPU类型", PlcConstants.CpuTypes, 90));
            dgvDevices.Columns.Add(BuildTextColumn("Ip", "IP", 120));
            dgvDevices.Columns.Add(BuildTextColumn("Port", "端口", 70));
            dgvDevices.Columns.Add(BuildTextColumn("Rack", "机架", 60));
            dgvDevices.Columns.Add(BuildTextColumn("Slot", "槽位", 60));
            dgvDevices.Columns.Add(BuildTextColumn("TimeoutMs", "超时ms", 80));
            dgvDevices.Columns.Add(BuildTextColumn("UnitId", "站号", 60));

            dgvDevices.DataSource = deviceBinding;
        }

        private void InitMapColumns()
        {
            dgvMaps.Columns.Clear();

            dgvMaps.Columns.Add(BuildTextColumn("PlcName", "PLC名称", 120));
            dgvMaps.Columns.Add(BuildComboColumn("DataType", "数据类型", PlcConstants.DataTypes, 90));
            dgvMaps.Columns.Add(BuildComboColumn("Direction", "读写", PlcConstants.Directions, 80));
            dgvMaps.Columns.Add(BuildTextColumn("PlcAddress", "PLC首地址", 150));
            dgvMaps.Columns.Add(BuildTextColumn("WriteConst", "写入常量", 120));
            dgvMaps.Columns.Add(BuildTextColumn("ValueName", "变量名称", 120));
            dgvMaps.Columns.Add(BuildTextColumn("Quantity", "数据数量", 80));

            dgvMaps.DataSource = mapBinding;
        }

        private static DataGridViewTextBoxColumn BuildTextColumn(string dataProperty, string headerText, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                DataPropertyName = dataProperty,
                HeaderText = headerText,
                MinimumWidth = width,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private static DataGridViewComboBoxColumn BuildComboColumn(string dataProperty, string headerText, IEnumerable<string> items, int width)
        {
            DataGridViewComboBoxColumn column = new DataGridViewComboBoxColumn
            {
                DataPropertyName = dataProperty,
                HeaderText = headerText,
                MinimumWidth = width,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = width,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FlatStyle = FlatStyle.Popup
            };
            foreach (string item in items)
            {
                column.Items.Add(item);
            }
            return column;
        }

        private void LoadConfig()
        {
            if (SF.plcStore == null)
            {
                return;
            }
            SF.plcStore.Load(SF.ConfigPath);
            deviceBinding.Clear();
            foreach (PlcDevice device in SF.plcStore.Devices)
            {
                deviceBinding.Add(device);
            }
            mapBinding.Clear();
            foreach (PlcMapItem map in SF.plcStore.Maps)
            {
                mapBinding.Add(map);
            }
        }

        private void MenuDeviceAdd_Click(object sender, EventArgs e)
        {
            PlcDevice device = new PlcDevice
            {
                Name = BuildNextDeviceName()
            };
            deviceBinding.Add(device);
        }

        private void MenuDeviceModify_Click(object sender, EventArgs e)
        {
            BeginEditSelected(dgvDevices);
        }

        private void MenuDeviceDelete_Click(object sender, EventArgs e)
        {
            int row = dgvDevices.CurrentCell?.RowIndex ?? -1;
            if (row < 0 || row >= deviceBinding.Count)
            {
                return;
            }
            if (MessageBox.Show("确认删除选中的PLC设备？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            deviceBinding.RemoveAt(row);
        }

        private void MenuMapAdd_Click(object sender, EventArgs e)
        {
            PlcMapItem map = new PlcMapItem
            {
                PlcName = deviceBinding.FirstOrDefault()?.Name ?? string.Empty
            };
            mapBinding.Add(map);
        }

        private void MenuMapModify_Click(object sender, EventArgs e)
        {
            BeginEditSelected(dgvMaps);
        }

        private void MenuMapDelete_Click(object sender, EventArgs e)
        {
            int row = dgvMaps.CurrentCell?.RowIndex ?? -1;
            if (row < 0 || row >= mapBinding.Count)
            {
                return;
            }
            if (MessageBox.Show("确认删除选中的PLC映射？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            mapBinding.RemoveAt(row);
        }

        private void MenuMapReconnect_Click(object sender, EventArgs e)
        {
            StopMappingInternal();
            if (SF.plcStore == null)
            {
                MessageBox.Show("PLC通讯未初始化");
                return;
            }
            if (!TryBuildDeviceSnapshot(out List<PlcDevice> devices, out string error))
            {
                MessageBox.Show(error);
                return;
            }
            foreach (PlcDevice device in devices)
            {
                if (!SF.plcStore.TryReconnect(device, out string reconnectError))
                {
                    ReportMappingError($"PLC重连失败:{device.Name} - {reconnectError}");
                    return;
                }
            }
            ReportNormal("PLC重连完成");
        }

        private void MenuMapStart_Click(object sender, EventArgs e)
        {
            StartMapping();
        }

        private void MenuMapStop_Click(object sender, EventArgs e)
        {
            StopMappingInternal();
        }

        private void MenuSave_Click(object sender, EventArgs e)
        {
            if (SF.plcStore == null)
            {
                MessageBox.Show("PLC配置未初始化");
                return;
            }
            if (!TryBuildDeviceSnapshot(out List<PlcDevice> devices, out string deviceError))
            {
                MessageBox.Show(deviceError);
                return;
            }
            if (!TryBuildMapSnapshot(out List<PlcMapItem> maps, out string mapError))
            {
                MessageBox.Show(mapError);
                return;
            }
            HashSet<string> deviceNames = new HashSet<string>(devices.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            foreach (PlcMapItem map in maps)
            {
                if (map == null)
                {
                    continue;
                }
                if (!deviceNames.Contains(map.PlcName ?? string.Empty))
                {
                    MessageBox.Show($"PLC名称不存在:{map.PlcName}");
                    return;
                }
            }

            SF.plcStore.ReplaceDevices(devices);
            SF.plcStore.ReplaceMaps(maps);
            SF.plcStore.SaveDevices(SF.ConfigPath);
            SF.plcStore.SaveMaps(SF.ConfigPath);
            ReportNormal("PLC配置已保存");
        }

        private void StartMapping()
        {
            if (mappingRunning)
            {
                return;
            }
            if (SF.plcStore == null)
            {
                MessageBox.Show("PLC通讯未初始化");
                return;
            }
            if (!TryBuildDeviceSnapshot(out List<PlcDevice> devices, out string deviceError))
            {
                MessageBox.Show(deviceError);
                return;
            }
            if (!TryBuildMapSnapshot(out List<PlcMapItem> maps, out string mapError))
            {
                MessageBox.Show(mapError);
                return;
            }
            if (maps.Count == 0)
            {
                MessageBox.Show("PLC映射为空");
                return;
            }
            HashSet<string> deviceNames = new HashSet<string>(devices.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            foreach (PlcMapItem map in maps)
            {
                if (map == null)
                {
                    continue;
                }
                if (!deviceNames.Contains(map.PlcName ?? string.Empty))
                {
                    MessageBox.Show($"PLC名称不存在:{map.PlcName}");
                    return;
                }
            }
            mappingRunning = true;
            UpdateMappingStatus();
            mappingCts = new CancellationTokenSource();
            CancellationToken token = mappingCts.Token;
            mappingTask = Task.Run(() => MappingLoop(devices, maps, token), token);
        }

        private void StopMappingInternal()
        {
            if (!mappingRunning)
            {
                return;
            }
            mappingCts?.Cancel();
            mappingRunning = false;
            UpdateMappingStatus();
        }

        private void MappingLoop(List<PlcDevice> devices, List<PlcMapItem> maps, CancellationToken token)
        {
            try
            {
                Dictionary<string, PlcDevice> deviceMap = devices.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
                while (!token.IsCancellationRequested)
                {
                    foreach (PlcMapItem map in maps)
                    {
                        if (token.IsCancellationRequested)
                        {
                            break;
                        }
                        if (map == null)
                        {
                            continue;
                        }
                        if (!deviceMap.TryGetValue(map.PlcName ?? string.Empty, out PlcDevice device))
                        {
                            StopMappingWithError($"PLC名称不存在:{map.PlcName}");
                            return;
                        }

                        if (!HandleMapItem(device, map))
                        {
                            return;
                        }
                    }

                    if (token.WaitHandle.WaitOne(mappingIntervalMs))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                StopMappingWithError(ex.Message);
            }
        }

        private bool HandleMapItem(PlcDevice device, PlcMapItem map)
        {
            if (!TryParseMapDirection(map.Direction, out bool doRead, out bool doWrite, out string error))
            {
                StopMappingWithError(error);
                return false;
            }

            if (doWrite)
            {
                object writeValue;
                if (!string.IsNullOrWhiteSpace(map.WriteConst))
                {
                    writeValue = map.WriteConst;
                }
                else
                {
                    if (SF.valueStore == null)
                    {
                        StopMappingWithError("变量库未初始化");
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(map.ValueName))
                    {
                        StopMappingWithError("映射变量名称为空");
                        return false;
                    }
                    if (!SF.valueStore.TryGetValueByName(map.ValueName, out DicValue value))
                    {
                        StopMappingWithError($"变量不存在:{map.ValueName}");
                        return false;
                    }
                    writeValue = value.Value;
                }

                if (!SF.plcStore.TryWriteValue(device, map, writeValue, out string writeError))
                {
                    StopMappingWithError($"PLC写入失败:{writeError}");
                    return false;
                }
            }

            if (doRead)
            {
                if (SF.valueStore == null)
                {
                    StopMappingWithError("变量库未初始化");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(map.ValueName))
                {
                    StopMappingWithError("映射变量名称为空");
                    return false;
                }
                if (!SF.valueStore.TryGetValueByName(map.ValueName, out DicValue value))
                {
                    StopMappingWithError($"变量不存在:{map.ValueName}");
                    return false;
                }
                if (!SF.plcStore.TryReadValue(device, map, out object readValue, out string readError))
                {
                    StopMappingWithError($"PLC读取失败:{readError}");
                    return false;
                }
                if (!SF.valueStore.setValueByName(map.ValueName, readValue, "PLC映射"))
                {
                    StopMappingWithError($"变量写入失败:{map.ValueName}");
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseMapDirection(string text, out bool doRead, out bool doWrite, out string error)
        {
            doRead = false;
            doWrite = false;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "读写方向不能为空";
                return false;
            }
            switch (text.Trim())
            {
                case "读PLC":
                    doRead = true;
                    return true;
                case "写PLC":
                    doWrite = true;
                    return true;
                case "读写":
                    doRead = true;
                    doWrite = true;
                    return true;
                default:
                    error = $"读写方向无效:{text}";
                    return false;
            }
        }

        private void StopMappingWithError(string message)
        {
            mappingRunning = false;
            mappingCts?.Cancel();
            UpdateMappingStatus();
            ReportMappingError(message);
        }

        private void UpdateMappingStatus()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateMappingStatus));
                return;
            }
            lblStatus.Text = mappingRunning ? "映射状态：运行中" : "映射状态：停止";
            lblStatus.BackColor = mappingRunning ? Color.LightGreen : Color.Gainsboro;
            menuMapStart.Enabled = !mappingRunning;
            menuMapStop.Enabled = mappingRunning;
        }

        private void ReportMappingError(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(ReportMappingError), message);
                return;
            }
            SF.frmInfo?.PrintInfo(message, FrmInfo.Level.Error);
            MessageBox.Show(message, "PLC映射异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ReportNormal(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(ReportNormal), message);
                return;
            }
            SF.frmInfo?.PrintInfo(message, FrmInfo.Level.Normal);
        }

        private string BuildNextDeviceName()
        {
            int index = 1;
            HashSet<string> names = new HashSet<string>(deviceBinding.Where(d => d != null).Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            while (names.Contains($"PLC{index}"))
            {
                index++;
            }
            return $"PLC{index}";
        }

        private static void BeginEditSelected(DataGridView grid)
        {
            if (grid.CurrentCell == null)
            {
                return;
            }
            grid.BeginEdit(true);
        }

        private bool TryBuildDeviceSnapshot(out List<PlcDevice> devices, out string error)
        {
            devices = deviceBinding.Select(device => device == null ? null : new PlcDevice
            {
                Name = device.Name,
                Protocol = device.Protocol,
                CpuType = device.CpuType,
                Ip = device.Ip,
                Port = device.Port,
                Rack = device.Rack,
                Slot = device.Slot,
                TimeoutMs = device.TimeoutMs,
                UnitId = device.UnitId
            }).ToList();

            if (!PlcConfigStore.ValidateDevices(devices, out error))
            {
                return false;
            }
            return true;
        }

        private bool TryBuildMapSnapshot(out List<PlcMapItem> maps, out string error)
        {
            maps = mapBinding.Select(map => map == null ? null : new PlcMapItem
            {
                PlcName = map.PlcName,
                DataType = map.DataType,
                Direction = map.Direction,
                PlcAddress = map.PlcAddress,
                ValueName = map.ValueName,
                Quantity = map.Quantity,
                WriteConst = map.WriteConst
            }).ToList();

            if (!PlcConfigStore.ValidateMaps(maps, out error))
            {
                return false;
            }
            return true;
        }

        private void DgvDevices_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            dgvDevices.EndEdit();
        }

        private void DgvMaps_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            dgvMaps.EndEdit();
        }

        private static void InitGridStyle(DataGridView grid)
        {
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Color.FromArgb(224, 224, 224);
            grid.RowTemplate.Height = 28;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 32;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(46, 105, 179);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(217, 234, 249);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 251);
        }

        private void ApplySplitterLayout()
        {
            if (splitContainer == null)
            {
                return;
            }
            if (!IsHandleCreated)
            {
                return;
            }
            int width = splitContainer.Width;
            if (width <= 0)
            {
                return;
            }
            int splitter = splitContainer.SplitterWidth;
            int panel1Min = 520;
            int panel2Min = 360;
            int minTotal = panel1Min + panel2Min + splitter;
            if (width < minTotal)
            {
                int available = Math.Max(0, width - splitter);
                int half = available / 2;
                panel1Min = Math.Min(panel1Min, half);
                panel2Min = Math.Min(panel2Min, available - panel1Min);
            }
            int max = Math.Max(0, width - panel2Min);
            int min = panel1Min;
            int desired = (int)(width * 0.65);
            if (desired < min)
            {
                desired = min;
            }
            if (desired > max)
            {
                desired = max;
            }
            splitContainer.SplitterDistance = desired;
            splitContainer.Panel1MinSize = panel1Min;
            splitContainer.Panel2MinSize = panel2Min;
            splitContainer.SplitterDistance = desired;
        }

        private void Dgv_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            MessageBox.Show("PLC配置值无效，请检查下拉选项。");
        }
    }
}
