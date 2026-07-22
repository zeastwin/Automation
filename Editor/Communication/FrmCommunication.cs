// 模块：编辑器 / 通讯。
// 职责范围：串口、Socket 与 PLC 的配置和调试页面。

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmCommunication : Form
    {
        private const int MaxLogLength = 200000;
        private const int MaxPendingUiLogEntries = 2000;
        private const int UiLogFlushBatchSize = 256;
        private const int DebugSendTimeoutMs = 5000;
        private const string TcpStateColumnName = "state";
        private const string TcpDelimiterColumnName = "tcpFrameDelimiter";
        private const string SerialStateColumnName = "dataGridViewTextBoxColumn5";
        private const string SerialDelimiterColumnName = "serialFrameDelimiter";

        private List<SocketInfo> socketInfos = new List<SocketInfo>();
        private List<SerialPortInfo> serialPortInfos = new List<SerialPortInfo>();

        public int iSelectedSocketRow;
        public int iSelectedSerialPortRow;

        private readonly System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer logFlushTimer = new System.Windows.Forms.Timer();
        private readonly ConcurrentQueue<CommLogEventArgs> pendingUiLogs = new ConcurrentQueue<CommLogEventArgs>();
        private int pendingUiLogCount;
        private volatile bool captureUiLogs;
        private CancellationTokenSource sendLoopCts;
        private int runtimeReleased;

        public FrmCommunication()
        {
            InitializeComponent();
            ConfigureResponsiveLayout();
            Disposed += FrmCommunication_Disposed;

            dataGridView1.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.AutoGenerateColumns = false;

            Type dgvType = dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(dataGridView1, true, null);
            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dataGridView2.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dataGridView2.RowHeadersVisible = false;
            dataGridView2.AutoGenerateColumns = false;

            Type dgvType2 = dataGridView2.GetType();
            PropertyInfo pi2 = dgvType2.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi2.SetValue(dataGridView2, true, null);
            dataGridView2.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            iSelectedSocketRow = -1;
            iSelectedSerialPortRow = -1;
            VisibleChanged += FrmCommunication_VisibleChanged;
            checkBox2.CheckedChanged += (s, e) =>
            {
                if (!checkBox2.Checked)
                {
                    sendLoopCts?.Cancel();
                }
            };
            ApplyCommunicationStyle();
        }

        private void ConfigureResponsiveLayout()
        {
            var receiveOptions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 145,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(12, 12, 8, 8),
                BackColor = UiPalette.Background
            };
            checkBox3.Margin = new Padding(0, 0, 0, 12);
            ClearBoard.Margin = Padding.Empty;
            ClearBoard.Width = 108;
            receiveOptions.Controls.Add(checkBox3);
            receiveOptions.Controls.Add(ClearBoard);
            groupBox1.Controls.Clear();
            ReceiveTextBox.Dock = DockStyle.Fill;
            groupBox1.Controls.Add(ReceiveTextBox);
            groupBox1.Controls.Add(receiveOptions);

            var sendOptions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 155,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(12, 8, 8, 6),
                BackColor = UiPalette.Background
            };
            checkBox1.Margin = new Padding(0, 0, 0, 7);
            checkBox2.Margin = new Padding(0, 0, 0, 5);
            label1.Margin = new Padding(0, 0, 0, 3);
            DelayText.Margin = new Padding(0, 0, 0, 6);
            DelayText.Width = 120;
            send.Margin = Padding.Empty;
            send.Width = 120;
            send.Height = 34;
            sendOptions.Controls.Add(checkBox1);
            sendOptions.Controls.Add(checkBox2);
            sendOptions.Controls.Add(label1);
            sendOptions.Controls.Add(DelayText);
            sendOptions.Controls.Add(send);
            groupBox2.Controls.Clear();
            groupBox2.Height = 180;
            SendTextBox.Dock = DockStyle.Fill;
            groupBox2.Controls.Add(SendTextBox);
            groupBox2.Controls.Add(sendOptions);
        }

        private void ApplyCommunicationStyle()
        {
            BackColor = UiPalette.SurfaceStrong;
            tabControl1.Font = new Font("微软雅黑", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);
            foreach (DataGridView grid in new[] { dataGridView1, dataGridView2 })
            {
                grid.BackgroundColor = UiPalette.SurfaceStrong;
                grid.BorderStyle = BorderStyle.None;
                grid.GridColor = UiPalette.Stroke;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersHeight = 28;
                grid.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.SurfaceSubtle;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
                grid.DefaultCellStyle.SelectionBackColor = UiPalette.Selection;
                grid.DefaultCellStyle.SelectionForeColor = UiPalette.Navigation;
                grid.AlternatingRowsDefaultCellStyle.BackColor = UiPalette.Input;
                grid.RowTemplate.Height = 26;
            }
            foreach (RichTextBox box in new[] { SendTextBox, ReceiveTextBox })
            {
                box.BackColor = UiPalette.SurfaceStrong;
                box.BorderStyle = BorderStyle.FixedSingle;
                box.Font = new Font("Consolas", 10F, FontStyle.Regular);
            }
            StyleCommunicationButton(send, UiPalette.Brand, UiPalette.TextInverse);
            StyleCommunicationButton(ClearBoard, UiPalette.SurfaceStrong, UiPalette.TextPrimary);
            foreach (ContextMenuStrip menu in new[] { contextMenuStrip1, contextMenuStrip2, contextMenuStrip3 })
            {
                menu.ShowImageMargin = false;
                menu.Font = new Font("微软雅黑", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            }
        }

        private static void StyleCommunicationButton(Button button, Color backColor, Color foreColor)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = UiPalette.StrokeStrong;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
        }

        private void FrmCommunication_Load(object sender, EventArgs e)
        {
            RefleshSocketDgv();
            RefleshSerialPortDgv();
            UpdateLogSubscription();
            StartStateTimer();
            logFlushTimer.Interval = 100;
            logFlushTimer.Tick -= LogFlushTimer_Tick;
            logFlushTimer.Tick += LogFlushTimer_Tick;
            logFlushTimer.Start();
            UpdateOnlineState();
        }

        private void FrmCommunication_VisibleChanged(object sender, EventArgs e)
        {
            UpdateLogSubscription();
            if (!Visible)
            {
                while (pendingUiLogs.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref pendingUiLogCount);
                }
            }
        }

        private void UpdateLogSubscription()
        {
            captureUiLogs = Visible && !IsDisposed && !Disposing;
            if (Workspace.Runtime.Communication == null)
            {
                return;
            }
            Workspace.Runtime.Communication.Log -= Comm_Log;
            if (captureUiLogs)
            {
                Workspace.Runtime.Communication.Log += Comm_Log;
            }
        }

        private void StartStateTimer()
        {
            stateTimer.Interval = 1000;
            stateTimer.Tick -= StateTimer_Tick;
            stateTimer.Tick += StateTimer_Tick;
            stateTimer.Start();
        }

        private void StateTimer_Tick(object sender, EventArgs e)
        {
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            UpdateOnlineState();
        }

        private void UpdateOnlineState()
        {
            if (Workspace.Runtime.Communication == null)
            {
                return;
            }

            for (int i = 0; i < socketInfos.Count && i < dataGridView1.Rows.Count; i++)
            {
                SocketInfo info = socketInfos[i];
                if (info == null)
                {
                    continue;
                }

                TcpStatus status = Workspace.Runtime.Communication.GetTcpStatus(info.Name);
                string droppedSuffix = status.DroppedFrames > 0 ? $" 丢帧:{status.DroppedFrames}" : string.Empty;
                if (ReferenceEquals(status, TcpStatus.Empty))
                {
                    SetRowCellValue(dataGridView1.Rows[i], TcpStateColumnName, string.Empty);
                    continue;
                }

                if (string.Equals(info.Type, "Client", StringComparison.Ordinal))
                {
                    string clientState = status.ConnectionState == TcpConnectionState.Connected ? "已连接"
                        : status.ConnectionState == TcpConnectionState.Connecting ? "连接中"
                        : status.ConnectionState == TcpConnectionState.Reconnecting ? "重连中"
                        : status.ConnectionState == TcpConnectionState.Faulted ? "故障"
                        : "未启动";
                    SetRowCellValue(dataGridView1.Rows[i], TcpStateColumnName,
                        clientState + droppedSuffix);
                    continue;
                }

                if (status.IsStarted)
                {
                    SetRowCellValue(dataGridView1.Rows[i], TcpStateColumnName,
                        (status.ClientCount > 0 ? $"已连接({status.ClientCount})" : "已启动") + droppedSuffix);
                }
                else
                {
                    SetRowCellValue(dataGridView1.Rows[i], TcpStateColumnName, "已关闭");
                }
            }

            for (int i = 0; i < serialPortInfos.Count && i < dataGridView2.Rows.Count; i++)
            {
                SerialPortInfo info = serialPortInfos[i];
                if (info == null)
                {
                    continue;
                }

                SerialStatus status = Workspace.Runtime.Communication.GetSerialStatus(info.Name);
                string droppedSuffix = status.DroppedFrames > 0 ? $" 丢帧:{status.DroppedFrames}" : string.Empty;
                SetRowCellValue(dataGridView2.Rows[i], SerialStateColumnName,
                    (status.IsOpen ? "已打开" : "已关闭") + droppedSuffix);
            }
        }

        private void Comm_Log(object sender, CommLogEventArgs e)
        {
            if (e == null || !captureUiLogs)
            {
                return;
            }
            pendingUiLogs.Enqueue(e);
            Interlocked.Increment(ref pendingUiLogCount);
            while (Volatile.Read(ref pendingUiLogCount) > MaxPendingUiLogEntries
                && pendingUiLogs.TryDequeue(out _))
            {
                Interlocked.Decrement(ref pendingUiLogCount);
            }
        }

        private void LogFlushTimer_Tick(object sender, EventArgs e)
        {
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                return;
            }
            int count = 0;
            while (count < UiLogFlushBatchSize && pendingUiLogs.TryDequeue(out CommLogEventArgs entry))
            {
                Interlocked.Decrement(ref pendingUiLogCount);
                AppendCommLog(entry);
                count++;
            }
        }

        private void AppendCommLog(CommLogEventArgs e)
        {
            if (e == null || ReceiveTextBox == null || ReceiveTextBox.IsDisposed)
            {
                return;
            }

            string message = e.Message ?? string.Empty;
            bool showHex = e.Direction == CommDirection.Receive && checkBox3.Checked && !string.IsNullOrWhiteSpace(e.MessageHex);
            if (showHex)
            {
                message = e.MessageHex;
            }

            string target = e.Kind == CommChannelKind.SerialPort
                ? (string.IsNullOrWhiteSpace(e.RemoteEndPoint) ? e.Name : e.RemoteEndPoint)
                : e.Name;
            string remote = e.Kind == CommChannelKind.TcpServer && !string.IsNullOrWhiteSpace(e.RemoteEndPoint)
                ? $"[{e.RemoteEndPoint}]"
                : string.Empty;
            string action = e.Direction == CommDirection.Send ? "Send" : e.Direction == CommDirection.Receive ? "Receive" : "Error";
            List<string> logLines = showHex
                ? new List<string> { message }
                : SplitCommMessageForDisplay(message);

            foreach (string line in logLines)
            {
                string str = $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}]{remote}[{target}][{action}]：{line}\r\n";

                TrimLogIfNeeded();

                int length = ReceiveTextBox.TextLength;
                ReceiveTextBox.AppendText(str);

                if (e.Direction == CommDirection.Error)
                {
                    SetTextColor(length, str, UiPalette.Danger);
                }
                else if (e.Direction == CommDirection.Send)
                {
                    SetTextColor(length, str, UiPalette.WarningSoft);
                }
                else
                {
                    SetTextColor(length, str, UiPalette.InfoSoft);
                }
            }
        }

        private static List<string> SplitCommMessageForDisplay(string message)
        {
            string normalized = (message ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] parts = normalized.Split('\n');

            int end = parts.Length - 1;
            while (end >= 0 && parts[end].Length == 0)
            {
                end--;
            }

            if (end < 0)
            {
                return new List<string> { string.Empty };
            }

            List<string> result = new List<string>(end + 1);
            for (int i = 0; i <= end; i++)
            {
                result.Add(EscapeControlChars(parts[i]));
            }

            return result;
        }

        private static string EscapeControlChars(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\t')
                {
                    sb.Append("\\t");
                    continue;
                }

                if (char.IsControl(c))
                {
                    sb.Append("\\x");
                    sb.Append(((int)c).ToString("X2"));
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private void TrimLogIfNeeded()
        {
            if (ReceiveTextBox == null || ReceiveTextBox.IsDisposed)
            {
                return;
            }

            int over = ReceiveTextBox.TextLength - MaxLogLength;
            if (over <= 0)
            {
                return;
            }

            int removeLength = Math.Min(ReceiveTextBox.TextLength, Math.Max(over, MaxLogLength / 10));
            ReceiveTextBox.Select(0, removeLength);
            ReceiveTextBox.SelectedText = string.Empty;
        }

        public void SetTextColor(int startindex, string str, Color color)
        {
            if (ReceiveTextBox == null || ReceiveTextBox.IsDisposed)
            {
                return;
            }

            int length = str.Length;
            ReceiveTextBox.Select(startindex, length);
            ReceiveTextBox.SelectionBackColor = color;
            ReceiveTextBox.ScrollToCaret();
        }

        private void FrmCommunication_FormClosing(object sender, FormClosingEventArgs e)
        {
            sendLoopCts?.Cancel();
            sendLoopCts?.Dispose();
            sendLoopCts = null;
            while (pendingUiLogs.TryDequeue(out _))
            {
                Interlocked.Decrement(ref pendingUiLogCount);
            }
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            ReleaseRuntimeResources();
        }

        private void FrmCommunication_Disposed(object sender, EventArgs e)
        {
            VisibleChanged -= FrmCommunication_VisibleChanged;
            ReleaseRuntimeResources();
            stateTimer.Dispose();
            logFlushTimer.Stop();
            logFlushTimer.Tick -= LogFlushTimer_Tick;
            logFlushTimer.Dispose();
        }

        public void RefreshSocketMap()
        {
            socketInfos = Workspace.Runtime.Stores.Communication?.GetSocketSnapshot().ToList() ?? new List<SocketInfo>();
        }

        public void RefreshSerialPortInfo()
        {
            serialPortInfos = Workspace.Runtime.Stores.Communication?.GetSerialSnapshot().ToList() ?? new List<SerialPortInfo>();
        }

        private bool TryPersistSocketConfigs()
        {
            string error = null;
            if (Workspace.Runtime.Stores.Communication == null
                || !Workspace.Runtime.Stores.Communication.TryReplaceSocketsAndSave(socketInfos, Workspace.Runtime.Paths.ConfigPath, out error))
            {
                MessageBox.Show(error ?? "通讯配置存储未初始化。", "TCP配置错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (Workspace.Runtime.ProcessEngine?.Context != null)
            {
                Workspace.Runtime.ProcessEngine.Context.SocketInfos = Workspace.Runtime.Stores.Communication.GetSocketSnapshot().ToList();
            }
            return true;
        }

        private bool TryPersistSerialConfigs()
        {
            string error = null;
            if (Workspace.Runtime.Stores.Communication == null
                || !Workspace.Runtime.Stores.Communication.TryReplaceSerialPortsAndSave(serialPortInfos, Workspace.Runtime.Paths.ConfigPath, out error))
            {
                MessageBox.Show(error ?? "通讯配置存储未初始化。", "串口配置错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (Workspace.Runtime.ProcessEngine?.Context != null)
            {
                Workspace.Runtime.ProcessEngine.Context.SerialPortInfos = Workspace.Runtime.Stores.Communication.GetSerialSnapshot().ToList();
            }
            return true;
        }

        public void RefleshSocketDgv()
        {
            dataGridView1.Rows.Clear();
            for (int i = 0; i < socketInfos.Count; i++)
            {
                SocketInfo cache = socketInfos[i];
                if (cache == null)
                {
                    continue;
                }

                dataGridView1.Rows.Add();
                ApplySocketInfoToRow(i, cache);
            }
        }

        public void RefleshSerialPortDgv()
        {
            dataGridView2.Rows.Clear();
            for (int i = 0; i < serialPortInfos.Count; i++)
            {
                SerialPortInfo cache = serialPortInfos[i];
                if (cache == null)
                {
                    continue;
                }

                dataGridView2.Rows.Add();
                ApplySerialInfoToRow(i, cache);
            }
        }

        private void ApplySocketInfoToRow(int rowIndex, SocketInfo info)
        {
            if (info == null || rowIndex < 0 || rowIndex >= dataGridView1.Rows.Count)
            {
                return;
            }

            DataGridViewRow row = dataGridView1.Rows[rowIndex];
            row.Cells[0].Value = info.ID;
            row.Cells[1].Value = info.Name;
            row.Cells[2].Value = info.Type;
            row.Cells[3].Value = info.LocalAddress;
            row.Cells[4].Value = info.LocalPort;
            row.Cells[5].Value = info.RemoteAddress;
            row.Cells[6].Value = info.RemotePort;
            row.Cells[7].Value = info.AutoReconnect;
            SetRowCellValue(row, TcpDelimiterColumnName, ToUiDelimiterSelection(info.FrameMode, info.FrameDelimiter, "Raw"));
        }

        private void ApplySerialInfoToRow(int rowIndex, SerialPortInfo info)
        {
            if (info == null || rowIndex < 0 || rowIndex >= dataGridView2.Rows.Count)
            {
                return;
            }

            DataGridViewRow row = dataGridView2.Rows[rowIndex];
            row.Cells[0].Value = info.ID;
            row.Cells[1].Value = info.Name;
            row.Cells[2].Value = info.Port;
            row.Cells[3].Value = info.BitRate;
            row.Cells[4].Value = info.CheckBit;
            row.Cells[5].Value = info.DataBit;
            row.Cells[6].Value = info.StopBit;
            SetRowCellValue(row, SerialDelimiterColumnName, ToUiDelimiterSelection(info.FrameMode, info.FrameDelimiter, "Delimiter"));
        }

        private void AddItem_Click(object sender, EventArgs e)
        {
            int id = socketInfos.LastOrDefault() == null ? 1 : socketInfos.LastOrDefault().ID + 1;
            string name = BuildUniqueName(socketInfos.Select(item => item?.Name), "Tcp");
            SocketInfo socketInfo = new SocketInfo
            {
                ID = id,
                Name = name,
                Type = "Client",
                LocalAddress = "0.0.0.0",
                LocalPort = 0,
                RemoteAddress = "127.0.0.1",
                RemotePort = 5000,
                AutoReconnect = true,
                FrameMode = "Raw",
                FrameDelimiter = "\\n",
                EncodingName = "UTF-8",
                ConnectTimeoutMs = 5000
            };
            socketInfos.Add(socketInfo);
            if (!TryPersistSocketConfigs())
            {
                RefreshSocketMap();
                RefleshSocketDgv();
                return;
            }
            RefreshSocketMap();
            RefleshSocketDgv();
        }

        private async void RemoveItem_Click(object sender, EventArgs e)
        {
            if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
            {
                return;
            }
            int rowIndex = iSelectedSocketRow;
            if (MessageBox.Show("确认删除选中的TCP配置？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            SocketInfo selected = socketInfos[rowIndex];
            if (selected != null && TryFindCommunicationReference(selected.Name, true, out string reference))
            {
                MessageBox.Show($"TCP配置正在被流程引用，禁止删除：{reference}", "TCP配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                if (selected != null && Workspace.Runtime.Communication != null)
                {
                    await Workspace.Runtime.Communication.StopTcpAsync(selected.Name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "TCP关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (rowIndex >= socketInfos.Count || !ReferenceEquals(socketInfos[rowIndex], selected))
            {
                MessageBox.Show("TCP配置列表已变化，请重新选择后再删除。", "TCP配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            socketInfos.RemoveAt(rowIndex);
            if (!TryPersistSocketConfigs())
            {
                RefreshSocketMap();
                RefleshSocketDgv();
                return;
            }
            RefreshSocketMap();
            RefleshSocketDgv();
        }

        private void dataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
            }
        }

        private async void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= socketInfos.Count)
            {
                return;
            }

            SocketInfo previous = socketInfos[e.RowIndex];
            ApplySocketTypeDefaultsToRow(e.RowIndex, previous);
            if (!TryBuildSocketInfoFromRow(e.RowIndex, out SocketInfo parsed, out string error))
            {
                MessageBox.Show(error, "TCP配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ApplySocketInfoToRow(e.RowIndex, socketInfos[e.RowIndex]);
                return;
            }

            if (HasDuplicateSocketName(parsed.Name, e.RowIndex))
            {
                MessageBox.Show($"TCP名称重复:{parsed.Name}", "TCP配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ApplySocketInfoToRow(e.RowIndex, socketInfos[e.RowIndex]);
                return;
            }

            if (previous != null && !string.Equals(previous.Name, parsed.Name, StringComparison.OrdinalIgnoreCase)
                && TryFindCommunicationReference(previous.Name, true, out string reference))
            {
                MessageBox.Show($"TCP配置正在被流程引用，禁止改名：{reference}", "TCP配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ApplySocketInfoToRow(e.RowIndex, previous);
                return;
            }
            if (previous != null && Workspace.Runtime.Communication?.GetTcpStatus(previous.Name).IsStarted == true)
            {
                try
                {
                    await Workspace.Runtime.Communication.StopTcpAsync(previous.Name);
                    Workspace.Info?.PrintInfo($"TCP配置已修改，原连接已停止:{previous.Name}", FrmInfo.Level.Normal);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "TCP配置修改失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ApplySocketInfoToRow(e.RowIndex, previous);
                    return;
                }
            }
            if (e.RowIndex >= socketInfos.Count || !ReferenceEquals(socketInfos[e.RowIndex], previous))
            {
                return;
            }
            socketInfos[e.RowIndex] = parsed;
            if (!TryPersistSocketConfigs())
            {
                RefreshSocketMap();
                RefleshSocketDgv();
                return;
            }
            RefreshSocketMap();
            ApplySocketInfoToRow(e.RowIndex, parsed);
        }

        private bool TryBuildSocketInfoFromRow(int rowIndex, out SocketInfo parsed, out string error)
        {
            parsed = null;
            error = null;

            DataGridViewRow row = dataGridView1.Rows[rowIndex];
            if (!int.TryParse(Convert.ToString(row.Cells[0].Value), out int id) || id <= 0)
            {
                error = "TCP ID无效，必须为正整数。";
                return false;
            }

            string name = Convert.ToString(row.Cells[1].Value);
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "TCP名称不能为空。";
                return false;
            }

            string type = Convert.ToString(row.Cells[2].Value);
            if (!string.Equals(type, "Client", StringComparison.Ordinal) && !string.Equals(type, "Server", StringComparison.Ordinal))
            {
                error = "TCP类型必须为Client或Server。";
                return false;
            }

            string localAddress = Convert.ToString(row.Cells[3].Value);
            if (string.IsNullOrWhiteSpace(localAddress)
                || !IPAddress.TryParse(localAddress, out IPAddress parsedLocalAddress)
                || parsedLocalAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                error = "TCP本地IP无效，仅支持IPv4。";
                return false;
            }

            if (!int.TryParse(Convert.ToString(row.Cells[4].Value), out int localPort)
                || localPort < 0 || localPort > 65535
                || (string.Equals(type, "Server", StringComparison.Ordinal) && localPort == 0))
            {
                error = string.Equals(type, "Server", StringComparison.Ordinal)
                    ? "TCP服务端本地端口必须在1-65535之间。"
                    : "TCP客户端本地端口必须在0-65535之间，0表示由系统分配。";
                return false;
            }

            string remoteAddress = Convert.ToString(row.Cells[5].Value);
            bool allowAnyRemote = string.Equals(type, "Server", StringComparison.Ordinal)
                && string.Equals(remoteAddress, "*", StringComparison.Ordinal);
            if (!allowAnyRemote
                && (string.IsNullOrWhiteSpace(remoteAddress)
                    || !IPAddress.TryParse(remoteAddress, out IPAddress parsedRemoteAddress)
                    || parsedRemoteAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
            {
                error = string.Equals(type, "Server", StringComparison.Ordinal)
                    ? "TCP服务端远端IP必须为明确IPv4地址或*。"
                    : "TCP客户端远端IP无效，仅支持IPv4。";
                return false;
            }

            if (!int.TryParse(Convert.ToString(row.Cells[6].Value), out int remotePort)
                || remotePort < 0 || remotePort > 65535
                || (string.Equals(type, "Client", StringComparison.Ordinal) && remotePort == 0))
            {
                error = string.Equals(type, "Server", StringComparison.Ordinal)
                    ? "TCP服务端远端端口必须在0-65535之间，0表示任意源端口。"
                    : "TCP客户端远端端口必须在1-65535之间。";
                return false;
            }

            bool autoReconnect = string.Equals(type, "Client", StringComparison.Ordinal)
                && row.Cells[7].Value is bool reconnectEnabled
                && reconnectEnabled;

            string delimiterSelection = GetCellValue(row, TcpDelimiterColumnName);
            if (!TryParseUiDelimiterSelection(delimiterSelection, out string frameMode, out string frameDelimiter))
            {
                error = "TCP分隔符必须为无、\\n或\\r\\n。";
                return false;
            }

            SocketInfo current = socketInfos[rowIndex] ?? new SocketInfo();
            parsed = new SocketInfo
            {
                ID = id,
                Name = name,
                Type = type,
                LocalAddress = localAddress,
                LocalPort = localPort,
                RemoteAddress = remoteAddress,
                RemotePort = remotePort,
                AutoReconnect = autoReconnect,
                FrameMode = frameMode,
                FrameDelimiter = frameDelimiter,
                EncodingName = string.IsNullOrWhiteSpace(current.EncodingName) ? "UTF-8" : current.EncodingName,
                ConnectTimeoutMs = current.ConnectTimeoutMs > 0 ? current.ConnectTimeoutMs : 5000
            };
            return true;
        }

        private void ApplySocketTypeDefaultsToRow(int rowIndex, SocketInfo previous)
        {
            if (previous == null || rowIndex < 0 || rowIndex >= dataGridView1.Rows.Count) return;
            DataGridViewRow row = dataGridView1.Rows[rowIndex];
            string type = Convert.ToString(row.Cells[2].Value);
            if (string.Equals(type, previous.Type, StringComparison.Ordinal)) return;

            if (string.Equals(type, "Server", StringComparison.Ordinal))
            {
                row.Cells[3].Value = "0.0.0.0";
                row.Cells[4].Value = previous.RemotePort > 0 ? previous.RemotePort : 5000;
                row.Cells[5].Value = "*";
                row.Cells[6].Value = 0;
                row.Cells[7].Value = false;
                return;
            }

            if (string.Equals(type, "Client", StringComparison.Ordinal))
            {
                row.Cells[3].Value = "0.0.0.0";
                row.Cells[4].Value = 0;
                row.Cells[5].Value = previous.LocalAddress == "0.0.0.0" ? "127.0.0.1" : previous.LocalAddress;
                row.Cells[6].Value = previous.LocalPort > 0 ? previous.LocalPort : 5000;
                row.Cells[7].Value = true;
            }
        }

        private bool HasDuplicateSocketName(string name, int skipIndex)
        {
            for (int i = 0; i < socketInfos.Count; i++)
            {
                if (i == skipIndex)
                {
                    continue;
                }
                SocketInfo info = socketInfos[i];
                if (info != null && string.Equals(info.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public async Task SendMessageAsync()
        {
            string text = SendTextBox.Text;
            bool convert = checkBox1.Checked;
            bool loopSend = checkBox2.Checked;
            bool useTcp = tabControl1.SelectedTab == tabPage1;
            string targetName;
            if (useTcp)
            {
                if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
                {
                    MessageBox.Show("未选择TCP配置。", "发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                targetName = socketInfos[iSelectedSocketRow]?.Name;
            }
            else if (tabControl1.SelectedTab == tabPage2)
            {
                if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
                {
                    MessageBox.Show("未选择串口配置。", "发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                targetName = serialPortInfos[iSelectedSerialPortRow]?.Name;
            }
            else
            {
                MessageBox.Show("未选择通讯页签。", "发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(targetName))
            {
                MessageBox.Show("通讯配置名称为空。", "发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int delayMs = 0;
            if (loopSend)
            {
                if (!int.TryParse(DelayText.Text, out delayMs) || delayMs <= 0)
                {
                    MessageBox.Show("循环发送间隔必须为大于0的整数。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            sendLoopCts?.Cancel();
            sendLoopCts?.Dispose();
            sendLoopCts = null;

            CancellationToken cancellationToken = CancellationToken.None;
            CancellationTokenSource currentLoopCts = null;
            if (loopSend)
            {
                currentLoopCts = new CancellationTokenSource();
                sendLoopCts = currentLoopCts;
                cancellationToken = currentLoopCts.Token;
            }

            try
            {
                do
                {
                    using (CancellationTokenSource sendTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                    sendTimeoutCts.CancelAfter(DebugSendTimeoutMs);
                    if (useTcp)
                    {
                        if (Workspace.Runtime.Communication == null
                            || !await Workspace.Runtime.Communication.SendTcpAsync(targetName, text, convert, sendTimeoutCts.Token))
                        {
                            throw new InvalidOperationException($"TCP发送失败:{targetName}");
                        }
                    }
                    else
                    {
                        if (Workspace.Runtime.Communication == null
                            || !await Workspace.Runtime.Communication.SendSerialAsync(targetName, text, convert, sendTimeoutCts.Token))
                        {
                            throw new InvalidOperationException($"串口发送失败:{targetName}");
                        }
                    }
                    }

                    if (!loopSend)
                    {
                        break;
                    }
                    if (!checkBox2.Checked)
                    {
                        break;
                    }

                    await Task.Delay(delayMs, cancellationToken);
                }
                while (!cancellationToken.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "发送失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (loopSend)
                {
                    if (ReferenceEquals(sendLoopCts, currentLoopCts))
                    {
                        sendLoopCts = null;
                    }
                    currentLoopCts?.Dispose();
                }
            }
        }

        private void dataGridView1_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
        }

        private async void connect_Click(object sender, EventArgs e)
        {
            if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
            {
                return;
            }
            if (Workspace.Runtime.Communication == null)
            {
                MessageBox.Show("通讯未初始化。", "TCP启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                await Workspace.Runtime.Communication.StartTcpAsync(socketInfos[iSelectedSocketRow]);
                UpdateOnlineState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "TCP启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dataGridView1_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            iSelectedSocketRow = e.RowIndex;
        }

        private async void SendTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await SendMessageAsync();
            }
        }

        private void ClearBoard_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确认清除通讯记录？", "清除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            ReceiveTextBox.Clear();
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            if (iSelectedSocketRow == -1 || dataGridView1.Rows.Count <= iSelectedSocketRow)
            {
                return;
            }

            DataGridViewRow row = dataGridView1.Rows[iSelectedSocketRow];
            string channelType = Convert.ToString(row.Cells[2].Value);
            string channelName = Convert.ToString(row.Cells[1].Value);
            TcpStatus status = Workspace.Runtime.Communication?.GetTcpStatus(channelName) ?? TcpStatus.Empty;
            if (string.Equals(channelType, "Client", StringComparison.Ordinal))
            {
                contextMenuStrip1.Items[2].Text = "连接";
                contextMenuStrip1.Items[2].Enabled = !status.IsStarted;
                contextMenuStrip1.Items[3].Enabled = status.IsStarted;
                return;
            }

            if (string.Equals(channelType, "Server", StringComparison.Ordinal))
            {
                contextMenuStrip1.Items[2].Text = "启动服务";
                contextMenuStrip1.Items[2].Enabled = !status.IsStarted;
                contextMenuStrip1.Items[3].Enabled = status.IsStarted;
            }
        }

        private async void CloseSocket_Click(object sender, EventArgs e)
        {
            if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
            {
                return;
            }
            if (Workspace.Runtime.Communication == null)
            {
                MessageBox.Show("通讯未初始化。", "TCP断开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                string name = socketInfos[iSelectedSocketRow].Name;
                await Workspace.Runtime.Communication.StopTcpAsync(name);
                UpdateOnlineState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "TCP断开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void copy_Click(object sender, EventArgs e)
        {
            ReceiveTextBox.Copy();
        }

        private void AddSerial_Click(object sender, EventArgs e)
        {
            int id = serialPortInfos.LastOrDefault() == null ? 1 : serialPortInfos.LastOrDefault().ID + 1;
            string name = BuildUniqueName(serialPortInfos.Select(item => item?.Name), "COM");
            SerialPortInfo serialPortInfo = new SerialPortInfo
            {
                ID = id,
                Name = name,
                Port = "COM1",
                BitRate = "9600",
                CheckBit = "None",
                DataBit = "8",
                StopBit = "One",
                FrameMode = "Delimiter",
                FrameDelimiter = "\\n",
                EncodingName = "UTF-8"
            };
            serialPortInfos.Add(serialPortInfo);
            if (!TryPersistSerialConfigs())
            {
                RefreshSerialPortInfo();
                RefleshSerialPortDgv();
                return;
            }
            RefreshSerialPortInfo();
            RefleshSerialPortDgv();
        }

        private async void RemoveSerial_Click(object sender, EventArgs e)
        {
            if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
            {
                return;
            }
            int rowIndex = iSelectedSerialPortRow;
            if (MessageBox.Show("确认删除选中的串口配置？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            SerialPortInfo selected = serialPortInfos[rowIndex];
            if (selected != null && TryFindCommunicationReference(selected.Name, false, out string reference))
            {
                MessageBox.Show($"串口配置正在被流程引用，禁止删除：{reference}", "串口配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                if (selected != null && Workspace.Runtime.Communication != null)
                {
                    await Workspace.Runtime.Communication.StopSerialAsync(selected.Name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "串口关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (rowIndex >= serialPortInfos.Count || !ReferenceEquals(serialPortInfos[rowIndex], selected))
            {
                MessageBox.Show("串口配置列表已变化，请重新选择后再删除。", "串口配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            serialPortInfos.RemoveAt(rowIndex);
            if (!TryPersistSerialConfigs())
            {
                RefreshSerialPortInfo();
                RefleshSerialPortDgv();
                return;
            }
            RefreshSerialPortInfo();
            RefleshSerialPortDgv();
        }

        private async void OpenSerial_Click(object sender, EventArgs e)
        {
            if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
            {
                return;
            }
            if (Workspace.Runtime.Communication == null)
            {
                MessageBox.Show("通讯未初始化。", "串口打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                await Workspace.Runtime.Communication.StartSerialAsync(serialPortInfos[iSelectedSerialPortRow]);
                UpdateOnlineState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "串口打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void Close_Click(object sender, EventArgs e)
        {
            if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
            {
                return;
            }
            if (Workspace.Runtime.Communication == null)
            {
                MessageBox.Show("通讯未初始化。", "串口关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                await Workspace.Runtime.Communication.StopSerialAsync(serialPortInfos[iSelectedSerialPortRow].Name);
                UpdateOnlineState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "串口关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void dataGridView2_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= serialPortInfos.Count)
            {
                return;
            }

            if (!TryBuildSerialInfoFromRow(e.RowIndex, out SerialPortInfo parsed, out string error))
            {
                MessageBox.Show(error, "串口配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ApplySerialInfoToRow(e.RowIndex, serialPortInfos[e.RowIndex]);
                return;
            }

            if (HasDuplicateSerialName(parsed.Name, e.RowIndex))
            {
                MessageBox.Show($"串口名称重复:{parsed.Name}", "串口配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ApplySerialInfoToRow(e.RowIndex, serialPortInfos[e.RowIndex]);
                return;
            }

            SerialPortInfo previous = serialPortInfos[e.RowIndex];
            if (previous != null && !string.Equals(previous.Name, parsed.Name, StringComparison.OrdinalIgnoreCase)
                && TryFindCommunicationReference(previous.Name, false, out string reference))
            {
                MessageBox.Show($"串口配置正在被流程引用，禁止改名：{reference}", "串口配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ApplySerialInfoToRow(e.RowIndex, previous);
                return;
            }
            if (previous != null && Workspace.Runtime.Communication?.GetSerialStatus(previous.Name).IsOpen == true)
            {
                try
                {
                    await Workspace.Runtime.Communication.StopSerialAsync(previous.Name);
                    Workspace.Info?.PrintInfo($"串口配置已修改，原连接已停止:{previous.Name}", FrmInfo.Level.Normal);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "串口配置修改失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ApplySerialInfoToRow(e.RowIndex, previous);
                    return;
                }
            }
            if (e.RowIndex >= serialPortInfos.Count || !ReferenceEquals(serialPortInfos[e.RowIndex], previous))
            {
                return;
            }
            serialPortInfos[e.RowIndex] = parsed;
            if (!TryPersistSerialConfigs())
            {
                RefreshSerialPortInfo();
                RefleshSerialPortDgv();
                return;
            }
            RefreshSerialPortInfo();
            ApplySerialInfoToRow(e.RowIndex, parsed);
        }

        private bool TryBuildSerialInfoFromRow(int rowIndex, out SerialPortInfo parsed, out string error)
        {
            parsed = null;
            error = null;

            DataGridViewRow row = dataGridView2.Rows[rowIndex];
            if (!int.TryParse(Convert.ToString(row.Cells[0].Value), out int id) || id <= 0)
            {
                error = "串口ID无效，必须为正整数。";
                return false;
            }

            string name = Convert.ToString(row.Cells[1].Value);
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "串口名称不能为空。";
                return false;
            }

            string port = Convert.ToString(row.Cells[2].Value);
            if (string.IsNullOrWhiteSpace(port))
            {
                error = "串口号不能为空。";
                return false;
            }

            string bitRate = Convert.ToString(row.Cells[3].Value);
            if (!int.TryParse(bitRate, out int br) || br <= 0)
            {
                error = "波特率必须为正整数。";
                return false;
            }

            string check = Convert.ToString(row.Cells[4].Value);
            if (string.IsNullOrWhiteSpace(check))
            {
                error = "校验位不能为空。";
                return false;
            }

            string dataBit = Convert.ToString(row.Cells[5].Value);
            if (!int.TryParse(dataBit, out int db) || db < 5 || db > 8)
            {
                error = "数据位必须在5-8之间。";
                return false;
            }

            string stopBit = Convert.ToString(row.Cells[6].Value);
            if (string.IsNullOrWhiteSpace(stopBit))
            {
                error = "停止位不能为空。";
                return false;
            }

            string delimiterSelection = GetCellValue(row, SerialDelimiterColumnName);
            if (!TryParseUiDelimiterSelection(delimiterSelection, out string frameMode, out string frameDelimiter))
            {
                error = "串口分隔符必须为无、\\n或\\r\\n。";
                return false;
            }

            SerialPortInfo current = serialPortInfos[rowIndex] ?? new SerialPortInfo();
            parsed = new SerialPortInfo
            {
                ID = id,
                Name = name,
                Port = port,
                BitRate = bitRate,
                CheckBit = check,
                DataBit = dataBit,
                StopBit = stopBit,
                FrameMode = frameMode,
                FrameDelimiter = frameDelimiter,
                EncodingName = string.IsNullOrWhiteSpace(current.EncodingName) ? "UTF-8" : current.EncodingName
            };
            return true;
        }

        private bool HasDuplicateSerialName(string name, int skipIndex)
        {
            for (int i = 0; i < serialPortInfos.Count; i++)
            {
                if (i == skipIndex)
                {
                    continue;
                }
                SerialPortInfo info = serialPortInfos[i];
                if (info != null && string.Equals(info.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryFindCommunicationReference(string name, bool tcp, out string reference)
        {
            reference = null;
            if (string.IsNullOrWhiteSpace(name) || Workspace.Proc?.procsList == null)
            {
                return false;
            }
            bool IsSameName(string candidate) => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase);
            for (int procIndex = 0; procIndex < Workspace.Proc.procsList.Count; procIndex++)
            {
                Proc proc = Workspace.Proc.procsList[procIndex];
                if (proc?.steps == null) continue;
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    if (step?.Ops == null) continue;
                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        OperationType operation = step.Ops[opIndex];
                        bool matched = tcp
                            ? (operation is TcpOps tcpOps && tcpOps.Params?.Any(item => IsSameName(item?.Name)) == true)
                                || (operation is WaitTcp waitTcp && waitTcp.Params?.Any(item => IsSameName(item?.Name)) == true)
                                || (operation is SendTcpMsg sendTcp && IsSameName(sendTcp.ConnectionName))
                                || (operation is ReceiveTcpMsg receiveTcp && IsSameName(receiveTcp.ConnectionName))
                                || (operation is SendReceiveCommMsg tcpRequest && tcpRequest.CommType == "TCP" && IsSameName(tcpRequest.ConnectionName))
                            : (operation is SerialPortOps serialOps && serialOps.Params?.Any(item => IsSameName(item?.Name)) == true)
                                || (operation is WaitSerialPort waitSerial && waitSerial.Params?.Any(item => IsSameName(item?.Name)) == true)
                                || (operation is SendSerialPortMsg sendSerial && IsSameName(sendSerial.ConnectionName))
                                || (operation is ReceiveSerialPortMsg receiveSerial && IsSameName(receiveSerial.ConnectionName))
                                || (operation is SendReceiveCommMsg serialRequest && serialRequest.CommType == "串口" && IsSameName(serialRequest.ConnectionName));
                        if (matched)
                        {
                            reference = $"流程{procIndex}-步骤{stepIndex}-指令{opIndex}";
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void dataGridView2_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
        }

        private void dataGridView2_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            iSelectedSerialPortRow = e.RowIndex;
        }

        private void dataGridView2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
            }
        }

        private void contextMenuStrip3_Opening(object sender, CancelEventArgs e)
        {
            if (iSelectedSerialPortRow == -1 || dataGridView2.Rows.Count <= iSelectedSerialPortRow)
            {
                return;
            }

            string state = GetCellValue(dataGridView2.Rows[iSelectedSerialPortRow], SerialStateColumnName);
            bool opened = !string.IsNullOrEmpty(state) && state.StartsWith("已打开", StringComparison.Ordinal);
            contextMenuStrip3.Items[2].Enabled = !opened;
            contextMenuStrip3.Items[3].Enabled = opened;
        }

        private async void send_Click(object sender, EventArgs e)
        {
            await SendMessageAsync();
        }

        private static void SetRowCellValue(DataGridViewRow row, string columnName, object value)
        {
            if (row == null || row.DataGridView == null || string.IsNullOrWhiteSpace(columnName))
            {
                return;
            }
            if (!row.DataGridView.Columns.Contains(columnName))
            {
                return;
            }

            row.Cells[columnName].Value = value;
        }

        private static string GetCellValue(DataGridViewRow row, string columnName)
        {
            if (row == null || row.DataGridView == null || string.IsNullOrWhiteSpace(columnName))
            {
                return string.Empty;
            }
            if (!row.DataGridView.Columns.Contains(columnName))
            {
                return string.Empty;
            }

            return Convert.ToString(row.Cells[columnName].Value)?.Trim() ?? string.Empty;
        }

        private static bool TryParseUiDelimiterSelection(string selection, out string frameMode, out string frameDelimiter)
        {
            if (string.Equals(selection, "无", StringComparison.Ordinal))
            {
                frameMode = "Raw";
                frameDelimiter = "\\n";
                return true;
            }
            if (string.Equals(selection, "\\n", StringComparison.Ordinal))
            {
                frameMode = "Delimiter";
                frameDelimiter = "\\n";
                return true;
            }
            if (string.Equals(selection, "\\r\\n", StringComparison.Ordinal))
            {
                frameMode = "Delimiter";
                frameDelimiter = "\\r\\n";
                return true;
            }

            frameMode = null;
            frameDelimiter = null;
            return false;
        }

        private static string ToUiDelimiterSelection(string frameMode, string frameDelimiter, string defaultMode)
        {
            string mode = string.IsNullOrWhiteSpace(frameMode) ? defaultMode : frameMode;
            if (string.Equals(mode, "Raw", StringComparison.Ordinal))
            {
                return "无";
            }
            if (string.Equals(frameDelimiter, "\\r\\n", StringComparison.Ordinal))
            {
                return "\\r\\n";
            }

            return "\\n";
        }

        private static string BuildUniqueName(IEnumerable<string> existingNames, string prefix)
        {
            HashSet<string> names = new HashSet<string>(existingNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.Ordinal);
            int index = 1;
            while (true)
            {
                string candidate = $"{prefix}_{index}";
                if (!names.Contains(candidate))
                {
                    return candidate;
                }
                index++;
            }
        }

        private void ReleaseRuntimeResources()
        {
            if (Interlocked.Exchange(ref runtimeReleased, 1) != 0)
            {
                return;
            }

            captureUiLogs = false;

            try
            {
                stateTimer.Stop();
                stateTimer.Tick -= StateTimer_Tick;
            }
            catch
            {
            }

            try
            {
                sendLoopCts?.Cancel();
                sendLoopCts?.Dispose();
            }
            catch
            {
            }
            finally
            {
                sendLoopCts = null;
            }

            try
            {
                if (Workspace.Runtime.Communication != null)
                {
                    Workspace.Runtime.Communication.Log -= Comm_Log;
                }
            }
            catch
            {
            }
        }
    }
}
