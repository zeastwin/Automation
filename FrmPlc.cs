using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public class FrmPlc : Form
    {
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
        private TableLayoutPanel mainLayout;
        private Panel headerPanel;
        private Label lblTitle;
        private Label lblSubtitle;
        private FlowLayoutPanel headerActions;
        private Button btnSaveAll;
        private Button btnReconnectAll;
        private Button btnStartMapping;
        private Button btnStopMapping;
        private NumericUpDown numMappingInterval;
        private TabControl mainTabs;
        private TabPage tabDevices;
        private TabPage tabMaps;
        private TabPage tabDebug;
        private ComboBox cmbDebugDevice;
        private ComboBox cmbDebugDataType;
        private TextBox txtDebugAddress;
        private NumericUpDown numDebugQuantity;
        private TextBox txtDebugWriteValue;
        private NumericUpDown numDebugInterval;
        private Button btnDebugRead;
        private Button btnDebugWrite;
        private Button btnDebugWatch;
        private Button btnDebugStop;
        private DataGridView dgvDebugHistory;
        private Label lblDebugStatus;
        private System.Windows.Forms.Timer uiStatusTimer;
        private CancellationTokenSource debugWatchCts;
        private Task debugWatchTask;
        private int debugOperationBusy;
        private const int MaxDebugHistoryRows = 500;

        private readonly BindingList<PlcDevice> deviceBinding = new BindingList<PlcDevice>();
        private readonly BindingList<PlcMapItem> mapBinding = new BindingList<PlcMapItem>();

        private CancellationTokenSource mappingCts;
        private Task mappingTask;
        private readonly object mappingLifecycleLock = new object();
        private volatile bool mappingRunning;
        private int mappingIntervalMs = 200;
        private long mappingCycleCount;
        private long lastMappingCycleElapsedMs;
        private long lastMappingReadCount;

        public FrmPlc()
        {
            InitializeComponent();
            FormClosing += FrmPlc_FormClosing;
            Load += FrmPlc_Load;
            Disposed += FrmPlc_Disposed;
            VisibleChanged += FrmPlc_VisibleChanged;
        }

        private void InitializeComponent()
        {
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
            mainLayout = new TableLayoutPanel();
            headerPanel = new Panel();
            lblTitle = new Label();
            lblSubtitle = new Label();
            headerActions = new FlowLayoutPanel();
            btnSaveAll = CreateToolbarButton("保存配置", true);
            btnReconnectAll = CreateToolbarButton("连接测试", false);
            btnStartMapping = CreateToolbarButton("启动映射", true);
            btnStopMapping = CreateToolbarButton("停止映射", false);
            numMappingInterval = new NumericUpDown();
            mainTabs = new TabControl();
            tabDevices = new TabPage("PLC设备");
            tabMaps = new TabPage("变量映射");
            tabDebug = new TabPage("在线调试");

            SuspendLayout();

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

            lblStatus.Dock = DockStyle.Fill;
            lblStatus.Height = 30;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Text = "映射状态：停止";
            lblStatus.BackColor = Color.FromArgb(237, 242, 247);
            lblStatus.BorderStyle = BorderStyle.FixedSingle;
            lblStatus.Padding = new Padding(8, 0, 0, 0);

            groupDevices.Controls.Add(dgvDevices);
            groupMaps.Controls.Add(dgvMaps);
            tabDevices.BackColor = Color.White;
            tabDevices.Padding = new Padding(8);
            tabDevices.Controls.Add(groupDevices);
            tabMaps.BackColor = Color.White;
            tabMaps.Padding = new Padding(8);
            tabMaps.Controls.Add(groupMaps);
            InitializeDebugPage();
            mainTabs.Dock = DockStyle.Fill;
            mainTabs.Font = new Font("Microsoft YaHei UI", 10F);
            mainTabs.Controls.Add(tabDevices);
            mainTabs.Controls.Add(tabMaps);
            mainTabs.Controls.Add(tabDebug);

            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(27, 43, 59);
            lblTitle.Location = new Point(20, 12);
            lblTitle.Text = "PLC 通讯中心";
            lblSubtitle.AutoSize = true;
            lblSubtitle.Font = new Font("Microsoft YaHei UI", 9F);
            lblSubtitle.ForeColor = Color.FromArgb(102, 116, 132);
            lblSubtitle.Location = new Point(22, 46);
            lblSubtitle.Text = "设备连接、变量映射、批量采集与在线读写调试";

            headerActions.Dock = DockStyle.Right;
            headerActions.FlowDirection = FlowDirection.LeftToRight;
            headerActions.WrapContents = false;
            headerActions.AutoSize = true;
            headerActions.Padding = new Padding(0, 14, 14, 0);
            numMappingInterval.Minimum = 50;
            numMappingInterval.Maximum = 5000;
            numMappingInterval.Value = mappingIntervalMs;
            numMappingInterval.Width = 72;
            numMappingInterval.Margin = new Padding(6, 7, 10, 0);
            numMappingInterval.ValueChanged += (sender, args) =>
                Interlocked.Exchange(ref mappingIntervalMs, (int)numMappingInterval.Value);
            Label intervalLabel = new Label
            {
                AutoSize = true,
                Text = "周期(ms)",
                ForeColor = Color.FromArgb(80, 94, 108),
                Margin = new Padding(8, 11, 0, 0)
            };
            btnSaveAll.Click += MenuSave_Click;
            btnReconnectAll.Click += MenuMapReconnect_Click;
            btnStartMapping.Click += MenuMapStart_Click;
            btnStopMapping.Click += MenuMapStop_Click;
            headerActions.Controls.Add(intervalLabel);
            headerActions.Controls.Add(numMappingInterval);
            headerActions.Controls.Add(btnSaveAll);
            headerActions.Controls.Add(btnReconnectAll);
            headerActions.Controls.Add(btnStartMapping);
            headerActions.Controls.Add(btnStopMapping);
            headerPanel.Dock = DockStyle.Fill;
            headerPanel.BackColor = Color.White;
            headerPanel.Controls.Add(headerActions);
            headerPanel.Controls.Add(lblSubtitle);
            headerPanel.Controls.Add(lblTitle);

            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 3;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            mainLayout.Controls.Add(headerPanel, 0, 0);
            mainLayout.Controls.Add(mainTabs, 0, 1);
            mainLayout.Controls.Add(lblStatus, 0, 2);
            Controls.Add(mainLayout);

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

        private static Button CreateToolbarButton(string text, bool primary)
        {
            var button = new Button
            {
                AutoSize = true,
                MinimumSize = new Size(96, 36),
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular),
                BackColor = primary ? Color.FromArgb(36, 112, 184) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(49, 63, 77),
                Margin = new Padding(6, 4, 0, 0),
                Padding = new Padding(10, 0, 10, 0)
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(190, 201, 213);
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(48, 128, 201)
                : Color.FromArgb(239, 243, 247);
            return button;
        }

        private void InitializeDebugPage()
        {
            tabDebug.BackColor = Color.FromArgb(247, 249, 252);
            tabDebug.Padding = new Padding(12);
            var debugLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            debugLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
            debugLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            debugLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            var commandPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(14, 14, 14, 8),
                WrapContents = true
            };
            cmbDebugDevice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            cmbDebugDataType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92 };
            cmbDebugDataType.Items.AddRange(PlcConstants.DataTypes.Cast<object>().ToArray());
            cmbDebugDataType.SelectedItem = "Float";
            cmbDebugDataType.SelectedIndexChanged += DebugDataType_SelectedIndexChanged;
            txtDebugAddress = new TextBox { Width = 150 };
            numDebugQuantity = new NumericUpDown { Minimum = 1, Maximum = 2000, Value = 1, Width = 64 };
            txtDebugWriteValue = new TextBox { Width = 150 };
            numDebugInterval = new NumericUpDown { Minimum = 100, Maximum = 10000, Value = 500, Increment = 100, Width = 72 };
            btnDebugRead = CreateToolbarButton("读取一次", true);
            btnDebugWrite = CreateToolbarButton("写入一次", false);
            btnDebugWatch = CreateToolbarButton("连续监视", true);
            btnDebugStop = CreateToolbarButton("停止监视", false);
            btnDebugStop.Enabled = false;
            btnDebugRead.Click += DebugRead_Click;
            btnDebugWrite.Click += DebugWrite_Click;
            btnDebugWatch.Click += DebugWatch_Click;
            btnDebugStop.Click += DebugStop_Click;
            AddDebugField(commandPanel, "PLC", cmbDebugDevice);
            AddDebugField(commandPanel, "类型", cmbDebugDataType);
            AddDebugField(commandPanel, "地址", txtDebugAddress);
            AddDebugField(commandPanel, "数量/长度", numDebugQuantity);
            AddDebugField(commandPanel, "写入值", txtDebugWriteValue);
            AddDebugField(commandPanel, "监视周期(ms)", numDebugInterval);
            commandPanel.Controls.Add(btnDebugRead);
            commandPanel.Controls.Add(btnDebugWrite);
            commandPanel.Controls.Add(btnDebugWatch);
            commandPanel.Controls.Add(btnDebugStop);

            dgvDebugHistory = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            InitGridStyle(dgvDebugHistory);
            dgvDebugHistory.Columns.Add(BuildTextColumn("Time", "时间", 125));
            dgvDebugHistory.Columns.Add(BuildTextColumn("Operation", "操作", 60));
            dgvDebugHistory.Columns.Add(BuildTextColumn("Device", "PLC", 90));
            dgvDebugHistory.Columns.Add(BuildTextColumn("Address", "地址", 110));
            dgvDebugHistory.Columns.Add(BuildTextColumn("DataType", "类型", 70));
            dgvDebugHistory.Columns.Add(BuildTextColumn("Value", "结果/写入值", 220));
            dgvDebugHistory.Columns.Add(BuildTextColumn("Elapsed", "耗时", 70));
            dgvDebugHistory.Columns.Add(BuildTextColumn("Status", "状态", 80));
            lblDebugStatus = new Label
            {
                Dock = DockStyle.Fill,
                Text = "调试状态：就绪",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(80, 94, 108),
                Padding = new Padding(8, 0, 0, 0)
            };
            debugLayout.Controls.Add(commandPanel, 0, 0);
            debugLayout.Controls.Add(dgvDebugHistory, 0, 1);
            debugLayout.Controls.Add(lblDebugStatus, 0, 2);
            UpdateDebugQuantityState();
            tabDebug.Controls.Add(debugLayout);
        }

        private static void AddDebugField(FlowLayoutPanel panel, string title, Control control)
        {
            var container = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 0, 12, 0)
            };
            container.Controls.Add(new Label
            {
                AutoSize = true,
                Text = title,
                ForeColor = Color.FromArgb(92, 105, 119),
                Margin = new Padding(0, 0, 0, 4)
            });
            container.Controls.Add(control);
            panel.Controls.Add(container);
        }

        private sealed class DebugRequest
        {
            public PlcDevice Device { get; set; }
            public string DataType { get; set; }
            public string Address { get; set; }
            public int Quantity { get; set; }
            public string WriteValue { get; set; }
        }

        private sealed class DebugResult
        {
            public DebugRequest Request { get; set; }
            public string Operation { get; set; }
            public object Value { get; set; }
            public long ElapsedMilliseconds { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
        }

        private void FrmPlc_Load(object sender, EventArgs e)
        {
            LoadConfig();
            RefreshDebugDeviceList();
            UpdateMappingStatus();
            uiStatusTimer = new System.Windows.Forms.Timer { Interval = 500 };
            uiStatusTimer.Tick += UiStatusTimer_Tick;
            uiStatusTimer.Start();
        }

        private void RefreshDebugDeviceList()
        {
            if (cmbDebugDevice == null)
            {
                return;
            }
            string selected = cmbDebugDevice.SelectedItem?.ToString();
            cmbDebugDevice.BeginUpdate();
            try
            {
                cmbDebugDevice.Items.Clear();
                foreach (PlcDevice device in deviceBinding)
                {
                    if (device != null && !string.IsNullOrWhiteSpace(device.Name))
                    {
                        cmbDebugDevice.Items.Add(device.Name);
                    }
                }
                if (!string.IsNullOrWhiteSpace(selected) && cmbDebugDevice.Items.Contains(selected))
                {
                    cmbDebugDevice.SelectedItem = selected;
                }
                else if (cmbDebugDevice.Items.Count > 0)
                {
                    cmbDebugDevice.SelectedIndex = 0;
                }
            }
            finally
            {
                cmbDebugDevice.EndUpdate();
            }
        }

        private bool TryBuildDebugRequest(out DebugRequest request, out string error)
        {
            request = null;
            error = null;
            string deviceName = cmbDebugDevice.SelectedItem?.ToString();
            PlcDevice source = deviceBinding.FirstOrDefault(device => device != null
                && string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase));
            if (source == null)
            {
                error = "请选择有效PLC设备。";
                return false;
            }
            PlcDevice device = new PlcDevice
            {
                Name = source.Name,
                Protocol = source.Protocol,
                CpuType = source.CpuType,
                Ip = source.Ip,
                Port = source.Port,
                Rack = source.Rack,
                Slot = source.Slot,
                TimeoutMs = source.TimeoutMs,
                UnitId = source.UnitId
            };
            if (!PlcConfigStore.ValidateDevices(new List<PlcDevice> { device }, out error))
            {
                return false;
            }
            string dataType = cmbDebugDataType.SelectedItem?.ToString();
            string address = txtDebugAddress.Text?.Trim();
            if (string.IsNullOrWhiteSpace(dataType) || string.IsNullOrWhiteSpace(address))
            {
                error = "数据类型和PLC地址不能为空。";
                return false;
            }
            request = new DebugRequest
            {
                Device = device,
                DataType = dataType,
                Address = address,
                Quantity = (int)numDebugQuantity.Value,
                WriteValue = txtDebugWriteValue.Text
            };
            return true;
        }

        private async void DebugRead_Click(object sender, EventArgs e)
        {
            if (!TryBuildDebugRequest(out DebugRequest request, out string error))
            {
                MessageBox.Show(error, "PLC调试", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            await ExecuteDebugOperationAsync(request, false);
        }

        private async void DebugWrite_Click(object sender, EventArgs e)
        {
            if (!TryBuildDebugRequest(out DebugRequest request, out string error))
            {
                MessageBox.Show(error, "PLC调试", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(request.WriteValue))
            {
                MessageBox.Show("写入值不能为空。", "PLC调试", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show($"确认写入PLC？\r\n设备：{request.Device.Name}\r\n地址：{request.Address}\r\n值：{request.WriteValue}",
                "PLC写入确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            await ExecuteDebugOperationAsync(request, true);
        }

        private async Task ExecuteDebugOperationAsync(DebugRequest request, bool write)
        {
            if (Interlocked.Exchange(ref debugOperationBusy, 1) != 0)
            {
                return;
            }
            SetDebugControlsEnabled(false);
            try
            {
                DebugResult result = await Task.Run(() => ExecuteDebugOperation(request, write));
                AppendDebugResult(result);
            }
            catch (Exception ex)
            {
                AppendDebugResult(new DebugResult
                {
                    Request = request,
                    Operation = write ? "写入" : "读取",
                    Success = false,
                    Error = ex.Message
                });
            }
            finally
            {
                Interlocked.Exchange(ref debugOperationBusy, 0);
                SetDebugControlsEnabled(debugWatchCts == null);
            }
        }

        private DebugResult ExecuteDebugOperation(DebugRequest request, bool write)
        {
            Stopwatch watch = Stopwatch.StartNew();
            var map = new PlcMapItem
            {
                PlcName = request.Device.Name,
                DataType = request.DataType,
                PlcAddress = request.Address,
                Quantity = request.Quantity,
                Direction = write ? "写PLC" : "读PLC",
                WriteConst = write ? request.WriteValue : string.Empty,
                ValueName = write ? string.Empty : "调试读取"
            };
            bool success = false;
            object value = null;
            string error = null;
            if (SF.plcStore == null)
            {
                error = "PLC通讯未初始化";
            }
            else if (write)
            {
                success = SF.plcStore.TryWriteValue(request.Device, map, request.WriteValue, out error);
                value = request.WriteValue;
            }
            else
            {
                success = SF.plcStore.TryReadValue(request.Device, map, out value, out error);
            }
            watch.Stop();
            return new DebugResult
            {
                Request = request,
                Operation = write ? "写入" : "读取",
                Value = value,
                ElapsedMilliseconds = watch.ElapsedMilliseconds,
                Success = success,
                Error = error
            };
        }

        private void DebugWatch_Click(object sender, EventArgs e)
        {
            if (!TryBuildDebugRequest(out DebugRequest request, out string error))
            {
                MessageBox.Show(error, "PLC调试", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (debugWatchTask != null && !debugWatchTask.IsCompleted)
            {
                return;
            }
            debugWatchCts?.Dispose();
            debugWatchCts = new CancellationTokenSource();
            CancellationTokenSource cts = debugWatchCts;
            int interval = (int)numDebugInterval.Value;
            SetDebugControlsEnabled(false);
            btnDebugStop.Enabled = true;
            lblDebugStatus.Text = "调试状态：连续监视中";
            debugWatchTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    DebugResult result = ExecuteDebugOperation(request, false);
                    TryBeginInvoke(() => AppendDebugResult(result));
                    await Task.Delay(interval, cts.Token).ConfigureAwait(false);
                }
            }, cts.Token);
            debugWatchTask.ContinueWith(task =>
            {
                _ = task.Exception;
                cts.Dispose();
                TryBeginInvoke(() =>
                {
                    if (ReferenceEquals(debugWatchCts, cts))
                    {
                        debugWatchCts = null;
                        debugWatchTask = null;
                    }
                    SetDebugControlsEnabled(true);
                    btnDebugStop.Enabled = false;
                    lblDebugStatus.Text = "调试状态：已停止";
                });
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void DebugStop_Click(object sender, EventArgs e)
        {
            StopDebugWatch();
        }

        private void StopDebugWatch()
        {
            debugWatchCts?.Cancel();
        }

        private void SetDebugControlsEnabled(bool enabled)
        {
            cmbDebugDevice.Enabled = enabled;
            cmbDebugDataType.Enabled = enabled;
            txtDebugAddress.Enabled = enabled;
            numDebugQuantity.Enabled = enabled
                && string.Equals(cmbDebugDataType.SelectedItem?.ToString(), "String", StringComparison.Ordinal);
            txtDebugWriteValue.Enabled = enabled;
            numDebugInterval.Enabled = enabled;
            btnDebugRead.Enabled = enabled;
            btnDebugWrite.Enabled = enabled;
            btnDebugWatch.Enabled = enabled;
        }

        private void DebugDataType_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDebugQuantityState();
        }

        private void UpdateDebugQuantityState()
        {
            bool isString = string.Equals(cmbDebugDataType.SelectedItem?.ToString(), "String",
                StringComparison.Ordinal);
            if (!isString)
            {
                numDebugQuantity.Value = 1;
            }
            numDebugQuantity.Enabled = isString && (debugWatchCts == null || debugWatchCts.IsCancellationRequested);
        }

        private void AppendDebugResult(DebugResult result)
        {
            if (result?.Request == null || dgvDebugHistory.IsDisposed)
            {
                return;
            }
            string valueText = FormatDebugValue(result.Value);
            int rowIndex = dgvDebugHistory.Rows.Add(DateTime.Now.ToString("HH:mm:ss.fff"), result.Operation,
                result.Request.Device.Name, result.Request.Address, result.Request.DataType,
                result.Success ? valueText : result.Error, result.ElapsedMilliseconds + " ms",
                result.Success ? "成功" : "失败");
            DataGridViewRow row = dgvDebugHistory.Rows[rowIndex];
            row.DefaultCellStyle.ForeColor = result.Success ? Color.FromArgb(34, 92, 54) : Color.Firebrick;
            if (dgvDebugHistory.Rows.Count > MaxDebugHistoryRows)
            {
                dgvDebugHistory.Rows.RemoveAt(0);
            }
            if (dgvDebugHistory.Rows.Count > 0)
            {
                dgvDebugHistory.FirstDisplayedScrollingRowIndex = dgvDebugHistory.Rows.Count - 1;
            }
            lblDebugStatus.Text = result.Success
                ? $"调试状态：{result.Operation}成功，耗时 {result.ElapsedMilliseconds} ms"
                : $"调试状态：{result.Operation}失败 - {result.Error}";
        }

        private static string FormatDebugValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (value is Array array)
            {
                return string.Join(", ", array.Cast<object>().Select(item => item?.ToString() ?? string.Empty));
            }
            return value.ToString();
        }

        private void FrmPlc_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                StopDebugWatch();
                uiStatusTimer?.Stop();
                e.Cancel = true;
                Hide();
                return;
            }
            StopMappingInternal();
            StopDebugWatch();
        }

        private void FrmPlc_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible && uiStatusTimer != null && !uiStatusTimer.Enabled)
            {
                uiStatusTimer.Start();
            }
            else if (!Visible)
            {
                uiStatusTimer?.Stop();
            }
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
            dgvDevices.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ConnectionState",
                HeaderText = "连接状态",
                ReadOnly = true,
                MinimumWidth = 80,
                FillWeight = 80,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

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

        private async void MenuMapReconnect_Click(object sender, EventArgs e)
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
            btnReconnectAll.Enabled = false;
            try
            {
                string reconnectError = await Task.Run(() =>
                {
                    foreach (PlcDevice device in devices)
                    {
                        if (!SF.plcStore.TryReconnect(device, out string deviceError))
                        {
                            return $"PLC重连失败:{device.Name} - {deviceError}";
                        }
                    }
                    return null;
                });
                if (!string.IsNullOrWhiteSpace(reconnectError))
                {
                    ReportMappingError(reconnectError);
                    return;
                }
                ReportNormal("PLC重连完成");
            }
            catch (Exception ex)
            {
                ReportMappingError($"PLC连接测试异常:{ex.Message}");
            }
            finally
            {
                btnReconnectAll.Enabled = true;
            }
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
            if (mappingRunning || (debugWatchTask != null && !debugWatchTask.IsCompleted))
            {
                MessageBox.Show("请先停止PLC映射和连续监视，再保存配置。", "PLC配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
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
            Dictionary<string, PlcDevice> deviceMap = devices.ToDictionary(device => device.Name,
                StringComparer.OrdinalIgnoreCase);
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
                if (!PlcHub.TryValidateAccess(deviceMap[map.PlcName], map, out string accessError))
                {
                    MessageBox.Show($"PLC映射参数无效:{map.PlcName}/{map.PlcAddress} - {accessError}");
                    return;
                }
            }

            SF.plcStore.ReplaceDevices(devices);
            SF.plcStore.ReplaceMaps(maps);
            SF.plcStore.SaveDevices(SF.ConfigPath);
            SF.plcStore.SaveMaps(SF.ConfigPath);
            RefreshDebugDeviceList();
            ReportNormal("PLC配置已保存");
        }

        private void StartMapping()
        {
            lock (mappingLifecycleLock)
            {
                if (mappingRunning || (mappingTask != null && !mappingTask.IsCompleted))
                {
                    return;
                }
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
            if (!TryValidateRuntimeMaps(maps, out string runtimeMapError))
            {
                MessageBox.Show(runtimeMapError, "PLC映射", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            HashSet<string> deviceNames = new HashSet<string>(devices.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PlcDevice> startDeviceMap = devices.ToDictionary(device => device.Name,
                StringComparer.OrdinalIgnoreCase);
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
                if (!PlcHub.TryValidateAccess(startDeviceMap[map.PlcName], map, out string accessError))
                {
                    MessageBox.Show($"PLC映射参数无效:{map.PlcName}/{map.PlcAddress} - {accessError}");
                    return;
                }
            }
            CancellationTokenSource cts = new CancellationTokenSource();
            Task task;
            lock (mappingLifecycleLock)
            {
                mappingRunning = true;
                mappingCts = cts;
                task = Task.Run(() => MappingLoop(devices, maps, cts.Token), cts.Token);
                mappingTask = task;
            }
            task.ContinueWith(completedTask => CompleteMappingTask(completedTask, cts),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            UpdateMappingStatus();
        }

        private void StopMappingInternal()
        {
            CancellationTokenSource cts;
            lock (mappingLifecycleLock)
            {
                cts = mappingCts;
                mappingRunning = false;
            }
            cts?.Cancel();
            UpdateMappingStatus();
        }

        private void CompleteMappingTask(Task completedTask, CancellationTokenSource cts)
        {
            _ = completedTask.Exception;
            lock (mappingLifecycleLock)
            {
                if (ReferenceEquals(mappingTask, completedTask))
                {
                    mappingTask = null;
                    mappingCts = null;
                    mappingRunning = false;
                }
            }
            cts.Dispose();
            UpdateMappingStatus();
        }

        private void MappingLoop(List<PlcDevice> devices, List<PlcMapItem> maps, CancellationToken token)
        {
            try
            {
                Dictionary<string, PlcDevice> deviceMap = devices.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
                while (!token.IsCancellationRequested)
                {
                    Stopwatch cycleWatch = Stopwatch.StartNew();
                    if (!HandleMappingCycle(deviceMap, maps, token, out int readCount))
                    {
                        return;
                    }
                    cycleWatch.Stop();
                    Interlocked.Increment(ref mappingCycleCount);
                    Interlocked.Exchange(ref lastMappingCycleElapsedMs, cycleWatch.ElapsedMilliseconds);
                    Interlocked.Exchange(ref lastMappingReadCount, readCount);

                    int remainingDelay = Math.Max(0,
                        Volatile.Read(ref mappingIntervalMs) - (int)Math.Min(int.MaxValue, cycleWatch.ElapsedMilliseconds));
                    if (token.WaitHandle.WaitOne(remainingDelay))
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

        private bool HandleMappingCycle(Dictionary<string, PlcDevice> deviceMap, List<PlcMapItem> maps,
            CancellationToken token, out int readCount)
        {
            readCount = 0;
            var readMapsByDevice = new Dictionary<string, List<PlcMapItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (PlcMapItem map in maps)
            {
                if (token.IsCancellationRequested)
                {
                    return false;
                }
                if (map == null)
                {
                    continue;
                }
                if (!deviceMap.TryGetValue(map.PlcName ?? string.Empty, out PlcDevice device))
                {
                    StopMappingWithError($"PLC名称不存在:{map.PlcName}");
                    return false;
                }
                if (!TryParseMapDirection(map.Direction, out bool doRead, out bool doWrite, out string error))
                {
                    StopMappingWithError(error);
                    return false;
                }
                if (doWrite && !TryWriteMapValue(device, map))
                {
                    return false;
                }
                if (doRead)
                {
                    if (!readMapsByDevice.TryGetValue(device.Name, out List<PlcMapItem> deviceMaps))
                    {
                        deviceMaps = new List<PlcMapItem>();
                        readMapsByDevice[device.Name] = deviceMaps;
                    }
                    deviceMaps.Add(map);
                }
            }

            foreach (KeyValuePair<string, List<PlcMapItem>> pair in readMapsByDevice)
            {
                if (token.IsCancellationRequested)
                {
                    return false;
                }
                PlcDevice device = deviceMap[pair.Key];
                if (!SF.plcStore.TryReadBatch(device, pair.Value,
                    out IReadOnlyList<PlcBatchReadResult> results, out string readError))
                {
                    StopMappingWithError($"PLC批量读取失败:{device.Name} {readError}");
                    return false;
                }
                foreach (PlcBatchReadResult result in results)
                {
                    if (result?.Map == null || string.IsNullOrWhiteSpace(result.Map.ValueName)
                        || SF.valueStore == null
                        || !SF.valueStore.setValueByName(result.Map.ValueName, result.Value, "PLC映射"))
                    {
                        StopMappingWithError($"变量写入失败:{result?.Map?.ValueName}");
                        return false;
                    }
                    readCount++;
                }
            }
            return true;
        }

        private bool TryWriteMapValue(PlcDevice device, PlcMapItem map)
        {
            object writeValue;
            if (!string.IsNullOrWhiteSpace(map.WriteConst))
            {
                writeValue = map.WriteConst;
            }
            else
            {
                if (SF.valueStore == null || string.IsNullOrWhiteSpace(map.ValueName)
                    || !SF.valueStore.TryGetValueByName(map.ValueName, out DicValue value))
                {
                    StopMappingWithError($"PLC写映射变量不存在:{map.ValueName}");
                    return false;
                }
                writeValue = value.Value;
            }
            if (!SF.plcStore.TryWriteValue(device, map, writeValue, out string writeError))
            {
                StopMappingWithError($"PLC写入失败:{device.Name} {writeError}");
                return false;
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
            CancellationTokenSource cts;
            lock (mappingLifecycleLock)
            {
                mappingRunning = false;
                cts = mappingCts;
            }
            cts?.Cancel();
            UpdateMappingStatus();
            ReportMappingError(message);
        }

        private void UpdateMappingStatus()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                TryBeginInvoke(UpdateMappingStatus);
                return;
            }
            long cycles = Interlocked.Read(ref mappingCycleCount);
            long elapsed = Interlocked.Read(ref lastMappingCycleElapsedMs);
            long reads = Interlocked.Read(ref lastMappingReadCount);
            lblStatus.Text = mappingRunning
                ? $"映射运行中  |  周期 {Volatile.Read(ref mappingIntervalMs)} ms  |  最近采集 {reads} 点/{elapsed} ms  |  总周期 {cycles}"
                : $"映射已停止  |  已完成周期 {cycles}";
            lblStatus.BackColor = mappingRunning ? Color.LightGreen : Color.Gainsboro;
            menuMapStart.Enabled = !mappingRunning;
            menuMapStop.Enabled = mappingRunning;
            btnStartMapping.Enabled = !mappingRunning;
            btnStopMapping.Enabled = mappingRunning;
        }

        private void UiStatusTimer_Tick(object sender, EventArgs e)
        {
            UpdateMappingStatus();
            if (SF.plcStore == null || dgvDevices.IsDisposed)
            {
                return;
            }
            int count = Math.Min(dgvDevices.Rows.Count, deviceBinding.Count);
            for (int i = 0; i < count; i++)
            {
                PlcDevice device = deviceBinding[i];
                DataGridViewCell cell = dgvDevices.Rows[i].Cells["ConnectionState"];
                bool connected = device != null && SF.plcStore.IsConnected(device.Name);
                string text = connected ? "已连接" : "未连接";
                if (!string.Equals(Convert.ToString(cell.Value), text, StringComparison.Ordinal))
                {
                    cell.Value = text;
                    cell.Style.ForeColor = connected ? Color.ForestGreen : Color.Gray;
                }
            }
        }

        private void ReportMappingError(string message)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                TryBeginInvoke(() => ReportMappingError(message));
                return;
            }
            SF.frmInfo?.PrintInfo(message, FrmInfo.Level.Error);
            MessageBox.Show(message, "PLC映射异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ReportNormal(string message)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                TryBeginInvoke(() => ReportNormal(message));
                return;
            }
            SF.frmInfo?.PrintInfo(message, FrmInfo.Level.Normal);
        }

        private void TryBeginInvoke(Action action)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void FrmPlc_Disposed(object sender, EventArgs e)
        {
            StopMappingInternal();
            StopDebugWatch();
            if (uiStatusTimer != null)
            {
                uiStatusTimer.Stop();
                uiStatusTimer.Tick -= UiStatusTimer_Tick;
                uiStatusTimer.Dispose();
                uiStatusTimer = null;
            }
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

        private static bool TryValidateRuntimeMaps(IEnumerable<PlcMapItem> maps, out string error)
        {
            error = null;
            if (SF.valueStore == null)
            {
                error = "变量库未初始化。";
                return false;
            }
            foreach (PlcMapItem map in maps)
            {
                if (map == null || !TryParseMapDirection(map.Direction, out bool doRead, out bool doWrite, out error))
                {
                    return false;
                }
                bool needsVariable = doRead || (doWrite && string.IsNullOrWhiteSpace(map.WriteConst));
                if (needsVariable && (string.IsNullOrWhiteSpace(map.ValueName)
                    || !SF.valueStore.TryGetValueByName(map.ValueName, out _)))
                {
                    error = $"PLC映射变量不存在:{map.ValueName}";
                    return false;
                }
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

        private void Dgv_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            MessageBox.Show("PLC配置值无效，请检查下拉选项。");
        }
    }
}
