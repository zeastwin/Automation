using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Security.Policy;
using System.Net.Http;
using System.Reflection;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices.ComTypes;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.AxHost;

namespace Automation
{
    public partial class FrmComunication : Form
    {

        public List<SocketInfo> socketInfos = new List<SocketInfo>();
        public List<SerialPortInfo> serialPortInfos = new List<SerialPortInfo>();

        public int iSelectedSocketRow;
        public int iSelectedSerialPortRow;

        bool isEndEdit = false;
        bool isEndEdit2 = false;
        private readonly System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer();


        public FrmComunication()
        {
            InitializeComponent();

            dataGridView1.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.AutoGenerateColumns = false;

            Type dgvType = this.dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dataGridView1, true, null);

            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dataGridView2.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dataGridView2.RowHeadersVisible = false;
            dataGridView2.AutoGenerateColumns = false;

            Type dgvType2 = this.dataGridView2.GetType();
            PropertyInfo pi2 = dgvType2.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi2.SetValue(this.dataGridView2, true, null);

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
                SF.comm.Log += Comm_Log;
            }
            StartStateTimer();
            UpdateOnlineState();

        }
        private void StartStateTimer()
        {
            stateTimer.Interval = 1000;
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
                if (string.Equals(info.Type, "Client", StringComparison.OrdinalIgnoreCase))
                {
                    dataGridView1.Rows[i].Cells[5].Value = status.IsRunning ? "已连接" : "未连接";
                }
                else
                {
                    if (status.IsRunning)
                    {
                        if (status.ClientCount > 0)
                        {
                            dataGridView1.Rows[i].Cells[5].Value = $"已连接({status.ClientCount})";
                        }
                        else
                        {
                            dataGridView1.Rows[i].Cells[5].Value = "已启动";
                        }
                    }
                    else
                    {
                        dataGridView1.Rows[i].Cells[5].Value = "已关闭";
                    }
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
            if (e.Direction == CommDirection.Receive && checkBox3.Checked)
            {
                if (int.TryParse(message, out int number))
                {
                    message = Convert.ToString(number, 16).ToUpper();
                }
            }

            string target = e.Kind == CommChannelKind.SerialPort
                ? (string.IsNullOrWhiteSpace(e.RemoteEndPoint) ? e.Name : e.RemoteEndPoint)
                : e.Name;
            string remote = e.Kind == CommChannelKind.TcpServer && !string.IsNullOrWhiteSpace(e.RemoteEndPoint)
                ? $"[{e.RemoteEndPoint}]"
                : string.Empty;
            string action = e.Direction == CommDirection.Send ? "Send" : e.Direction == CommDirection.Receive ? "Receive" : "Error";
            string str = $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}]{remote}[{target}][{action}]：{message}\r\n";
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
        public void SetTextColor(int startindex, string str, Color color)
        {
            int length = str.Length;
            ReceiveTextBox.Select(startindex, length);
            ReceiveTextBox.SelectionBackColor = color;
            ReceiveTextBox.ScrollToCaret();
        }

        public async Task SendSocketMessageFormAsync(string name, string sendMessage)
        {
            if (SF.comm == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            do
            {
                bool convert = checkBox1.Checked;
                await SF.comm.SendTcpAsync(name, sendMessage, convert);

                if (checkBox2.Checked)
                {
                    if (int.TryParse(DelayText.Text, out int time))
                    {
                        await Task.Delay(time);
                    }
                }
            }
            while (checkBox2.Checked);
        }

        public async Task SendSerialPortMessageFormAsync(string name, string sendMessage)
        {
            if (SF.comm == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            do
            {
                bool convert = checkBox1.Checked;
                await SF.comm.SendSerialAsync(name, sendMessage, convert);

                if (checkBox2.Checked)
                {
                    if (int.TryParse(DelayText.Text, out int time))
                    {
                        await Task.Delay(time);
                    }
                }
            }
            while (checkBox2.Checked);
        }

        private void FrmComunication_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
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
            socketInfos = SF.mainfrm.ReadJson<List<SocketInfo>>(SF.ConfigPath, "SocketInfo");
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
            serialPortInfos = SF.mainfrm.ReadJson<List<SerialPortInfo>>(SF.ConfigPath, "SerialPortInfo");
        }
        public void RefleshSocketDgv()
        {
            if (isEndEdit == false)
                dataGridView1.Rows.Clear();
            for (int i = 0; i < socketInfos.Count; i++)
            {
                SocketInfo cache = socketInfos[i];
                if (cache != null)
                {
                    if (isEndEdit == false)
                        dataGridView1.Rows.Add();
                    dataGridView1.Rows[i].Cells[0].Value = cache.ID;
                    dataGridView1.Rows[i].Cells[1].Value = cache.Name;
                    dataGridView1.Rows[i].Cells[2].Value = cache.Type;

                    //DataGridViewComboBoxCell cell = (dataGridView1.Rows[i].Cells[3]) as DataGridViewComboBoxCell;
                    dataGridView1.Rows[i].Cells[3].Value = cache.Address;
                    dataGridView1.Rows[i].Cells[4].Value = cache.Port;

                }
            }
        }
        public void RefleshSerialPortDgv()
        {
            if (isEndEdit2 == false)
                dataGridView2.Rows.Clear();
            for (int i = 0; i < serialPortInfos.Count; i++)
            {
                SerialPortInfo cache = serialPortInfos[i];
                if (cache != null)
                {
                    if (isEndEdit2 == false)
                        dataGridView2.Rows.Add();
                    dataGridView2.Rows[i].Cells[0].Value = cache.ID;
                    dataGridView2.Rows[i].Cells[1].Value = cache.Name;
                    dataGridView2.Rows[i].Cells[2].Value = cache.Port;
                    dataGridView2.Rows[i].Cells[3].Value = cache.BitRate;
                    dataGridView2.Rows[i].Cells[4].Value = cache.CheckBit;
                    dataGridView2.Rows[i].Cells[5].Value = cache.DataBit;
                    dataGridView2.Rows[i].Cells[6].Value = cache.StopBit;

                }
            }
        }
        private void AddItem_Click(object sender, EventArgs e)
        {
            int id = socketInfos.LastOrDefault() == null ? 1 : socketInfos.LastOrDefault().ID + 1;
            SocketInfo socketInfo = new SocketInfo() { Address = "127.0.0.1", ID = id, Name = "TcpServer", Port = 5000, Type = "Client" };
            socketInfos.Add(socketInfo);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "SocketInfo", socketInfos);
            RefreshSocketMap();
            RefleshSocketDgv();

        }

        private void RemoveItem_Click(object sender, EventArgs e)
        {
            if (iSelectedSocketRow != -1)
            {
                socketInfos.RemoveAt(iSelectedSocketRow);
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "SocketInfo", socketInfos);
                RefreshSocketMap();
                RefleshSocketDgv();
            }

        }

        private void dataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true; // 阻止默认行为 防止选择条向下切换

            }
        }

        private void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                isEndEdit = true;
                DataGridView dataGridView = (DataGridView)sender;

                int id = (int)dataGridView.Rows[e.RowIndex].Cells[0].Value;
                string name = (string)dataGridView.Rows[e.RowIndex].Cells[1].Value;
                string type = (string)dataGridView.Rows[e.RowIndex].Cells[2].Value;
                string ip = (string)dataGridView.Rows[e.RowIndex].Cells[3].Value;
                int port = int.Parse(dataGridView.Rows[e.RowIndex].Cells[4].Value.ToString());

                SocketInfo socketInfo = new SocketInfo() { Address = ip, ID = id, Name = name, Port = port, Type = type };
                socketInfos[socketInfos.IndexOf(socketInfos.FirstOrDefault(sc => sc.ID == id))] = socketInfo;

                SF.mainfrm.SaveAsJson(SF.ConfigPath, "SocketInfo", socketInfos);
                RefreshSocketMap();
                RefleshSocketDgv();
            }
        }
        public bool CheckFormIsOpen(Form form)
        {
            bool bResult = false;

            if (form.Visible == true && form.WindowState != FormWindowState.Minimized)
            {
                bResult = true;
            }
            return bResult;
        }
        public void SendMessage()
        {
            string text = SendTextBox.Text;
            TabPage selectedTabPage = tabControl1.SelectedTab;
            if (selectedTabPage == tabPage1)
            {
                if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
                {
                    return;
                }

                string name = socketInfos[iSelectedSocketRow].Name;
                _ = SendSocketMessageFormAsync(name, text);
            }
            else if (selectedTabPage == tabPage2)
            {
                if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
                {
                    return;
                }

                SerialPortInfo serialPortInfo = serialPortInfos[iSelectedSerialPortRow];
                _ = SendSerialPortMessageFormAsync(serialPortInfo.Name, text);

            }
        }
        private void dataGridView1_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && isEndEdit == true)
            {
                isEndEdit = false;
            }
        }

        private void connect_Click(object sender, EventArgs e)
        {
            if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
            {
                return;
            }

            _ = SF.comm.StartTcpAsync(socketInfos[iSelectedSocketRow]);
        }

        private void dataGridView1_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            iSelectedSocketRow = e.RowIndex;
        }


        private void SendTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                SendMessage();
            }
        }

        private void ClearBoard_Click(object sender, EventArgs e)
        {
            ReceiveTextBox.Clear();
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            if (iSelectedSocketRow != -1 && dataGridView1.Rows.Count > iSelectedSocketRow)
            {
                if (dataGridView1.Rows[iSelectedSocketRow].Cells[2].Value?.ToString() == "Client")
                {
                    contextMenuStrip1.Items[2].Text = "连接";
                    if (dataGridView1.Rows[iSelectedSocketRow].Cells[5].Value?.ToString() == "已连接")
                    {
                        contextMenuStrip1.Items[2].Enabled = false;
                        contextMenuStrip1.Items[3].Enabled = true;

                    }
                    else
                    {
                        contextMenuStrip1.Items[2].Enabled = true;
                        contextMenuStrip1.Items[3].Enabled = false;
                    }
                }
                else if (dataGridView1.Rows[iSelectedSocketRow].Cells[2].Value?.ToString() == "Server")
                {
                    contextMenuStrip1.Items[2].Text = "启动服务";
                    if (dataGridView1.Rows[iSelectedSocketRow].Cells[5].Value?.ToString() == "已启动" || dataGridView1.Rows[iSelectedSocketRow].Cells[5].Value?.ToString() == "已连接")
                    {
                        contextMenuStrip1.Items[2].Enabled = false;
                        contextMenuStrip1.Items[3].Enabled = true;
                    }
                    else
                    {
                        contextMenuStrip1.Items[2].Enabled = true;
                        contextMenuStrip1.Items[3].Enabled = false;
                    }
                }

            }

        }

        private void CloseSocket_Click(object sender, EventArgs e)
        {
            if (iSelectedSocketRow < 0 || iSelectedSocketRow >= socketInfos.Count)
            {
                return;
            }

            string name = socketInfos[iSelectedSocketRow].Name;
            _ = SF.comm.StopTcpAsync(name);
            if (dataGridView1.Rows[iSelectedSocketRow].Cells[2].Value?.ToString() == "Client")
            {
                dataGridView1.Rows[iSelectedSocketRow].Cells[5].Value = "未连接";
            }
            else
            {
                dataGridView1.Rows[iSelectedSocketRow].Cells[5].Value = "已关闭";
            }

        }

        private void copy_Click(object sender, EventArgs e)
        {
            ReceiveTextBox.Copy();
        }

        private void AddSerial_Click(object sender, EventArgs e)
        {
            int id = serialPortInfos.LastOrDefault() == null ? 1 : serialPortInfos.LastOrDefault().ID + 1;
            SerialPortInfo serialPortInfo = new SerialPortInfo() { ID = id, Name = "COM", Port = "COM1", BitRate = "600", CheckBit = "None", DataBit = "5", StopBit = "None" };
            serialPortInfos.Add(serialPortInfo);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "SerialPortInfo", serialPortInfos);
            RefreshSerialPortInfo();
            RefleshSerialPortDgv();
        }

        private void RemoveSerial_Click(object sender, EventArgs e)
        {
            if (iSelectedSerialPortRow != -1)
            {
                serialPortInfos.RemoveAt(iSelectedSerialPortRow);
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "SerialPortInfo", serialPortInfos);
                RefreshSerialPortInfo();
                RefleshSerialPortDgv();
            }
        }
        private void OpenSerial_Click(object sender, EventArgs e)
        {
            if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
            {
                return;
            }

            _ = SF.comm.StartSerialAsync(serialPortInfos[iSelectedSerialPortRow]);
        }
        private void Close_Click(object sender, EventArgs e)
        {
            if (iSelectedSerialPortRow < 0 || iSelectedSerialPortRow >= serialPortInfos.Count)
            {
                return;
            }

            _ = SF.comm.StopSerialAsync(serialPortInfos[iSelectedSerialPortRow].Name);

        }
        private void dataGridView2_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                isEndEdit2 = true;
                DataGridView dataGridView = (DataGridView)sender;

                int id = (int)dataGridView.Rows[e.RowIndex].Cells[0].Value;
                string name = (string)dataGridView.Rows[e.RowIndex].Cells[1].Value;
                string port = (string)dataGridView.Rows[e.RowIndex].Cells[2].Value;
                string bitrate = (string)dataGridView.Rows[e.RowIndex].Cells[3].Value;
                string check = (string)dataGridView.Rows[e.RowIndex].Cells[4].Value;
                string data = (string)dataGridView.Rows[e.RowIndex].Cells[5].Value;
                string stop = (string)dataGridView.Rows[e.RowIndex].Cells[6].Value;

                SerialPortInfo serialPortInfo = new SerialPortInfo() { ID = id, Name = name, Port = port, BitRate = bitrate, CheckBit = check, DataBit = data, StopBit = stop };
                serialPortInfos[serialPortInfos.IndexOf(serialPortInfos.FirstOrDefault(sc => sc.ID == id))] = serialPortInfo;

                SF.mainfrm.SaveAsJson(SF.ConfigPath, "SerialPortInfo", serialPortInfos);
                RefreshSerialPortInfo();
                RefleshSerialPortDgv();
            }
        }

        private void dataGridView2_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && isEndEdit2 == true)
            {
                isEndEdit2 = false;
            }
        }

        private void dataGridView2_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            iSelectedSerialPortRow = e.RowIndex;
        }

        private void dataGridView2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true; // 阻止默认行为 防止选择条向下切换

            }
        }

        private void contextMenuStrip3_Opening(object sender, CancelEventArgs e)
        {
            if (iSelectedSerialPortRow != -1 && dataGridView2.Rows.Count > iSelectedSerialPortRow)
            {
                if (dataGridView2.Rows[iSelectedSerialPortRow].Cells[7].Value?.ToString() == "已打开")
                {
                    contextMenuStrip3.Items[2].Enabled = false;
                    contextMenuStrip3.Items[3].Enabled = true;

                }
                else
                {
                    contextMenuStrip3.Items[2].Enabled = true;
                    contextMenuStrip3.Items[3].Enabled = false;
                }
            }
        }

        private void send_Click(object sender, EventArgs e)
        {
            SendMessage();
        }
    }
}

