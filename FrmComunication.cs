using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmComunication : Form
    {
        private const int MaxLogLength = 200000;

        public List<SocketInfo> socketInfos = new List<SocketInfo>();
        public List<SerialPortInfo> serialPortInfos = new List<SerialPortInfo>();

        public int iSelectedSocketRow;
        public int iSelectedSerialPortRow;

        private readonly System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer();
        private CancellationTokenSource sendLoopCts;

        public FrmComunication()
        {
            InitializeComponent();

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
        }

        private void FrmComunication_Load(object sender, EventArgs e)
        {
            RefleshSocketDgv();
            RefleshSerialPortDgv();
            if (SF.comm != null)
            {
                SF.comm.Log -= Comm_Log;
                SF.comm.Log += Comm_Log;
            }
            StartStateTimer();
            UpdateOnlineState();
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
            if (!CheckFormIsOpen(this))
            {
                return;
            }

            UpdateOnlineState();
        }

        private void UpdateOnlineState()
        {
            if (SF.comm == null)
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

                TcpStatus status = SF.comm.GetTcpStatus(info.Name);
                if (ReferenceEquals(status, TcpStatus.Empty))
                {
                    dataGridView1.Rows[i].Cells[5].Value = "";
                    continue;
                }

                if (string.Equals(info.Type, "Client", StringComparison.Ordinal))
                {
                    dataGridView1.Rows[i].Cells[5].Value = status.IsRunning ? "已连接" : "未连接";
                    continue;
                }

                if (status.IsRunning)
                {
                    dataGridView1.Rows[i].Cells[5].Value = status.ClientCount > 0 ? $"已连接({status.ClientCount})" : "已启动";
                }
                else
                {
                    dataGridView1.Rows[i].Cells[5].Value = "已关闭";
                }
            }

            for (int i = 0; i < serialPortInfos.Count && i < dataGridView2.Rows.Count; i++)
            {
                SerialPortInfo info = serialPortInfos[i];
                if (info == null)
                {
                    continue;
                }

                SerialStatus status = SF.comm.GetSerialStatus(info.Name);
                dataGridView2.Rows[i].Cells[7].Value = status.IsOpen ? "已打开" : "已关闭";
            }
        }

        private void Comm_Log(object sender, CommLogEventArgs e)
        {
            if (!CheckFormIsOpen(this))
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => AppendCommLog(e)));
                return;
            }

            AppendCommLog(e);
        }

        private void AppendCommLog(CommLogEventArgs e)
        {
            string message = e.Message ?? string.Empty;
            if (e.Direction == CommDirection.Receive && checkBox3.Checked && !string.IsNullOrWhiteSpace(e.MessageHex))
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
            string str = $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}]{remote}[{target}][{action}]：{message}\r\n";

            TrimLogIfNeeded();

            int length = ReceiveTextBox.TextLength;
            ReceiveTextBox.AppendText(str);

            if (e.Direction == CommDirection.Error)
            {
                SetTextColor(length, str, Color.Red);
            }
            else if (e.Direction == CommDirection.Send)
            {
                SetTextColor(length, str, Color.LightYellow);
            }
            else
            {
                SetTextColor(length, str, Color.LightBlue);
            }
        }

        private void TrimLogIfNeeded()
        {
            int over = ReceiveTextBox.TextLength - MaxLogLength;
            if (over <= 0)
            {
                return;
            }

            ReceiveTextBox.Select(0, over);
            ReceiveTextBox.SelectedText = string.Empty;
        }

        public void SetTextColor(int startindex, string str, Color color)
        {
            int length = str.Length;
            ReceiveTextBox.Select(startindex, length);
            ReceiveTextBox.SelectionBackColor = color;
            ReceiveTextBox.ScrollToCaret();
        }

        public async Task SendSocketMessageFormAsync(string name, string sendMessage, bool convert, CancellationToken cancellationToken)
        {
            if (SF.comm == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            bool success = await SF.comm.SendTcpAsync(name, sendMessage, convert, cancellationToken).ConfigureAwait(false);
            if (!success)
            {
                throw new InvalidOperationException($"TCP发送失败:{name}");
            }
        }

        public async Task SendSerialPortMessageFormAsync(string name, string sendMessage, bool convert, CancellationToken cancellationToken)
        {
            if (SF.comm == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            bool success = await SF.comm.SendSerialAsync(name, sendMessage, convert, cancellationToken).ConfigureAwait(false);
            if (!success)
            {
                throw new InvalidOperationException($"串口发送失败:{name}");
            }
        }

        private void FrmComunication_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            sendLoopCts?.Cancel();
            sendLoopCts?.Dispose();
            sendLoopCts = null;
            Hide();
        }

        public void RefreshSocketMap()
        {
            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }
            if (!File.Exists(SF.ConfigPath + "SocketInfo.json"))
            {
                socketInfos = new List<SocketInfo>();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "SocketInfo", socketInfos);
            }

            socketInfos = SF.mainfrm.ReadJson<List<SocketInfo>>(SF.ConfigPath, "SocketInfo") ?? new List<SocketInfo>();
            bool changed = false;
            foreach (SocketInfo info in socketInfos)
            {
                if (info == null)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(info.ChannelId))
                {
                    info.ChannelId = Guid.NewGuid().ToString("N");
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(info.FrameMode))
                {
                    info.FrameMode = "Delimiter";
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(info.FrameDelimiter))
                {
                    info.FrameDelimiter = "\\n";
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(info.EncodingName))
                {
                    info.EncodingName = "UTF-8";
                    changed = true;
                }
                if (info.ConnectTimeoutMs <= 0)
                {
                    info.ConnectTimeoutMs = 5000;
                    changed = true;
                }
            }

            if (changed)
            {
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "SocketInfo", socketInfos);
            }

            if (SF.DR?.Context != null)
            {
                SF.DR.Context.SocketInfos = socketInfos;
            }
        }

        public void RefreshSerialPortInfo()
        {
            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }
            if (!File.Exists(SF.ConfigPath + "SerialPortInfo.json"))
            {
                serialPortInfos = new List<SerialPortInfo>();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "SerialPortInfo", serialPortInfos);
            }

            serialPortInfos = SF.mainfrm.ReadJson<List<SerialPortInfo>>(SF.ConfigPath, "SerialPortInfo") ?? new List<SerialPortInfo>();
            bool changed = false;
            foreach (SerialPortInfo info in serialPortInfos)
            {
                if (info == null)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(info.ChannelId))
                {
                    info.ChannelId = Guid.NewGuid().ToString("N");
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(info.FrameMode))
                {
                    info.FrameMode = "Delimiter";
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(info.FrameDelimiter))
                {
                    info.FrameDelimiter = "\\n";
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(info.EncodingName))
                {
                    info.EncodingName = "UTF-8";
                    changed = true;
                }
            }

            if (changed)
            {
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "SerialPortInfo", serialPortInfos);
            }

            if (SF.DR?.Context != null)
            {
                SF.DR.Context.SerialPortInfos = serialPortInfos;
            }
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
            row.Cells[3].Value = info.Address;
            row.Cells[4].Value = info.Port;
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
        }

        private void AddItem_Click(object sender, EventArgs e)
        {
            int id = socketInfos.LastOrDefault() == null ? 1 : socketInfos.LastOrDefault().ID + 1;
            string name = BuildUniqueName(socketInfos.Select(item => item?.Name), "Tcp");
            SocketInfo socketInfo = new SocketInfo
            {
                Address = "127.0.0.1",
                ID = id,
                Name = name,
                Port = 5000,
                Type = "Client",
                FrameMode = "Delimiter",
                FrameDelimiter = "\\n",
                EncodingName = "UTF-8",
                ConnectTimeoutMs = 5000
            };
            socketInfos.Add(socketInfo);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "SocketInfo", socketInfos);
            RefreshSocketMap();
            RefleshSocketDgv();
        }

        private async void RemoveItem_Click(object sender, EventArgs e)
        {
            if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
            {
                return;
            }
            if (MessageBox.Show("确认删除选中的TCP配置？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            SocketInfo selected = socketInfos[iSelectedSocketRow];
            try
            {
                if (selected != null && SF.comm != null)
                {
                    await SF.comm.StopTcpAsync(selected.Name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "TCP关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            socketInfos.RemoveAt(iSelectedSocketRow);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "SocketInfo", socketInfos);
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

        private void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= socketInfos.Count)
            {
                return;
            }

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

            socketInfos[e.RowIndex] = parsed;
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "SocketInfo", socketInfos);
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

            string address = Convert.ToString(row.Cells[3].Value);
            if (string.IsNullOrWhiteSpace(address) || !IPAddress.TryParse(address, out _))
            {
                error = "TCP地址无效。";
                return false;
            }

            if (!int.TryParse(Convert.ToString(row.Cells[4].Value), out int port) || port <= 0 || port > 65535)
            {
                error = "TCP端口无效，必须在1-65535之间。";
                return false;
            }

            SocketInfo current = socketInfos[rowIndex] ?? new SocketInfo();
            parsed = new SocketInfo
            {
                ID = id,
                ChannelId = string.IsNullOrWhiteSpace(current.ChannelId) ? Guid.NewGuid().ToString("N") : current.ChannelId,
                Name = name,
                Type = type,
                Address = address,
                Port = port,
                FrameMode = string.IsNullOrWhiteSpace(current.FrameMode) ? "Delimiter" : current.FrameMode,
                FrameDelimiter = string.IsNullOrWhiteSpace(current.FrameDelimiter) ? "\\n" : current.FrameDelimiter,
                EncodingName = string.IsNullOrWhiteSpace(current.EncodingName) ? "UTF-8" : current.EncodingName,
                ConnectTimeoutMs = current.ConnectTimeoutMs <= 0 ? 5000 : current.ConnectTimeoutMs,
                isServering = current.isServering
            };
            return true;
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

        public bool CheckFormIsOpen(Form form)
        {
            return form.Visible && form.WindowState != FormWindowState.Minimized;
        }

        public async Task SendMessageAsync()
        {
            string text = SendTextBox.Text;
            bool convert = checkBox1.Checked;
            bool loopSend = checkBox2.Checked;

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
                    TabPage selectedTabPage = tabControl1.SelectedTab;
                    if (selectedTabPage == tabPage1)
                    {
                        if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
                        {
                            throw new InvalidOperationException("未选择TCP配置。");
                        }

                        SocketInfo socketInfo = socketInfos[iSelectedSocketRow];
                        await SendSocketMessageFormAsync(socketInfo.Name, text, convert, cancellationToken);
                    }
                    else if (selectedTabPage == tabPage2)
                    {
                        if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
                        {
                            throw new InvalidOperationException("未选择串口配置。");
                        }

                        SerialPortInfo serialPortInfo = serialPortInfos[iSelectedSerialPortRow];
                        await SendSerialPortMessageFormAsync(serialPortInfo.Name, text, convert, cancellationToken);
                    }
                    else
                    {
                        throw new InvalidOperationException("未选择通讯页签。");
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
            if (SF.comm == null)
            {
                MessageBox.Show("通讯未初始化。", "TCP启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                await SF.comm.StartTcpAsync(socketInfos[iSelectedSocketRow]);
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

            string channelType = dataGridView1.Rows[iSelectedSocketRow].Cells[2].Value?.ToString();
            string state = dataGridView1.Rows[iSelectedSocketRow].Cells[5].Value?.ToString();
            if (string.Equals(channelType, "Client", StringComparison.Ordinal))
            {
                contextMenuStrip1.Items[2].Text = "连接";
                bool connected = string.Equals(state, "已连接", StringComparison.Ordinal);
                contextMenuStrip1.Items[2].Enabled = !connected;
                contextMenuStrip1.Items[3].Enabled = connected;
                return;
            }

            if (string.Equals(channelType, "Server", StringComparison.Ordinal))
            {
                contextMenuStrip1.Items[2].Text = "启动服务";
                bool running = string.Equals(state, "已启动", StringComparison.Ordinal) || (!string.IsNullOrEmpty(state) && state.StartsWith("已连接", StringComparison.Ordinal));
                contextMenuStrip1.Items[2].Enabled = !running;
                contextMenuStrip1.Items[3].Enabled = running;
            }
        }

        private async void CloseSocket_Click(object sender, EventArgs e)
        {
            if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
            {
                return;
            }
            if (SF.comm == null)
            {
                MessageBox.Show("通讯未初始化。", "TCP断开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                string name = socketInfos[iSelectedSocketRow].Name;
                await SF.comm.StopTcpAsync(name);
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
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "SerialPortInfo", serialPortInfos);
            RefreshSerialPortInfo();
            RefleshSerialPortDgv();
        }

        private async void RemoveSerial_Click(object sender, EventArgs e)
        {
            if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
            {
                return;
            }
            if (MessageBox.Show("确认删除选中的串口配置？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            SerialPortInfo selected = serialPortInfos[iSelectedSerialPortRow];
            try
            {
                if (selected != null && SF.comm != null)
                {
                    await SF.comm.StopSerialAsync(selected.Name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "串口关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            serialPortInfos.RemoveAt(iSelectedSerialPortRow);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "SerialPortInfo", serialPortInfos);
            RefreshSerialPortInfo();
            RefleshSerialPortDgv();
        }

        private async void OpenSerial_Click(object sender, EventArgs e)
        {
            if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
            {
                return;
            }
            if (SF.comm == null)
            {
                MessageBox.Show("通讯未初始化。", "串口打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                await SF.comm.StartSerialAsync(serialPortInfos[iSelectedSerialPortRow]);
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
            if (SF.comm == null)
            {
                MessageBox.Show("通讯未初始化。", "串口关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                await SF.comm.StopSerialAsync(serialPortInfos[iSelectedSerialPortRow].Name);
                UpdateOnlineState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "串口关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dataGridView2_CellEndEdit(object sender, DataGridViewCellEventArgs e)
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

            serialPortInfos[e.RowIndex] = parsed;
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "SerialPortInfo", serialPortInfos);
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

            SerialPortInfo current = serialPortInfos[rowIndex] ?? new SerialPortInfo();
            parsed = new SerialPortInfo
            {
                ID = id,
                ChannelId = string.IsNullOrWhiteSpace(current.ChannelId) ? Guid.NewGuid().ToString("N") : current.ChannelId,
                Name = name,
                Port = port,
                BitRate = bitRate,
                CheckBit = check,
                DataBit = dataBit,
                StopBit = stopBit,
                FrameMode = string.IsNullOrWhiteSpace(current.FrameMode) ? "Delimiter" : current.FrameMode,
                FrameDelimiter = string.IsNullOrWhiteSpace(current.FrameDelimiter) ? "\\n" : current.FrameDelimiter,
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

            bool opened = string.Equals(dataGridView2.Rows[iSelectedSerialPortRow].Cells[7].Value?.ToString(), "已打开", StringComparison.Ordinal);
            contextMenuStrip3.Items[2].Enabled = !opened;
            contextMenuStrip3.Items[3].Enabled = opened;
        }

        private async void send_Click(object sender, EventArgs e)
        {
            await SendMessageAsync();
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
    }
}
