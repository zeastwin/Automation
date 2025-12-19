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
using System.Threading;
using System.Reflection;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices.ComTypes;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.AxHost;
using System.IO.Ports;
using static Automation.FrmComunication;

namespace Automation
{
    public partial class FrmComunication : Form
    {

        public List<SocketInfo> socketInfos = new List<SocketInfo>();
        public List<Socketer> socketers = new List<Socketer>();

        public List<SerialPortInfo> serialPortInfos = new List<SerialPortInfo>();
        public List<SerialPorter> serialPorters = new List<SerialPorter>();


        public int iSelectedSocketRow;
        public int iSelectedSerialPortRow;

        bool isEndEdit = false;
        bool isEndEdit2 = false;


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
            IsOnline();

        }
        public class ConnectHandle
        {
            public ManualResetEvent connectDone = new ManualResetEvent(false);
            public byte[] buffer = new byte[1024];
            public Socketer socketer;
        }

        public void TryConnect(SocketInfo socketInfo)
        {

            try
            {
                Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                Socketer socketClient = new Socketer() { SocketInfo = socketInfo, socket = clientSocket };
                socketClient.isRun.Set();
                ConnectHandle connectHandle = new ConnectHandle() { socketer = socketClient };

                // 设置连接超时时间为 5 秒
                TimeSpan timeout = TimeSpan.FromSeconds(5);
                clientSocket.BeginConnect(new IPEndPoint(IPAddress.Parse(socketInfo.Address), socketInfo.Port), ConnectCallback, connectHandle);

                // 等待连接完成或超时
                if (connectHandle.connectDone.WaitOne(timeout))
                {
                    socketers.Add(socketClient);
                    // 开始异步接收数据
                    clientSocket.BeginReceive(connectHandle.buffer, 0, connectHandle.buffer.Length, SocketFlags.None, ReceiveCallback, connectHandle);
                }
                else
                {
                    // 连接超时
                    clientSocket.Close();
                    Invoke(new Action(() =>
                    {
                        int length = ReceiveTextBox.TextLength;
                        string str = socketInfo.Name + "连接超时\r\n";
                        ReceiveTextBox.AppendText(str);
                        SetTextColor(length, str, Color.Red);

                    }));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);

            }
        }
        public void ConnectCallback(IAsyncResult ar)
        {
            ConnectHandle state = (ConnectHandle)ar.AsyncState;
            try
            {
                // 完成连接

                state.socketer.socket.EndConnect(ar);

                // 通知连接已完成
                state.connectDone.Set();
            }
            catch (Exception ex)
            {
                // 通知连接已完成
                state.connectDone.Reset();
                MessageBox.Show("Error: " + ex.Message);

            }
        }
        public void ReceiveCallback(IAsyncResult ar)
        {
            bool isCheck = false;
            try
            {
                ConnectHandle state = (ConnectHandle)ar.AsyncState;
                int bytesRead = state.socketer.socket.EndReceive(ar);

                if (bytesRead > 0)
                {
                    string receivedData = Encoding.UTF8.GetString(state.buffer, 0, bytesRead);

                    if (state.socketer.isRun.WaitOne())
                    {
                        state.socketer.Msg = receivedData;
                        if (SF.frmComunication.Visible == true)
                        {
                            Invoke(new Action(() =>
                            {
                                isCheck = checkBox3.Checked;
                                if (isCheck)
                                {
                                    if (int.TryParse(receivedData, out int number))
                                    {
                                        receivedData = Convert.ToString(number, 16).ToUpper();
                                    }
                                }
                                int length = ReceiveTextBox.TextLength;
                                string str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}][{state.socketer.SocketInfo.Name}][Receive]：{receivedData}\r\n";
                                ReceiveTextBox.AppendText(str);
                                SetTextColor(length, str, Color.LightBlue);

                            }));
                        }
                    }
                }

                // 继续异步接收数据
                state.socketer.socket.BeginReceive(state.buffer, 0, state.buffer.Length, SocketFlags.None, ReceiveCallback, state);
            }
            catch (SocketException se)
            {
                if (se.SocketErrorCode == SocketError.ConnectionReset)
                {
                    Console.WriteLine("Connection closed by remote host.");
                }
                else
                {
                    Console.WriteLine("SocketError: " + se.Message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in ReceiveCallback: " + ex.Message);


            }
        }
        public void SetTextColor(int startindex, string str, Color color)
        {
            int length = str.Length;
            ReceiveTextBox.Select(startindex, length);
            ReceiveTextBox.SelectionBackColor = color;
            ReceiveTextBox.ScrollToCaret();
        }

        public void SendSocketMessageForm(Socketer Socket, string sendMessage)
        {
            bool isCheck = false;

            try
            {
                do
                {
                    Invoke(new Action(() =>
                    {
                        isCheck = checkBox1.Checked;
                    }));
                    if (isCheck)
                    {
                        if (int.TryParse(sendMessage, out int number))
                        {
                            sendMessage = Convert.ToString(number, 16).ToUpper();
                        }
                    }
                    byte[] buffer = Encoding.UTF8.GetBytes(sendMessage);
                    if (Socket.SocketInfo.Type == "Client")
                    {
                        Socket.socket.Send(buffer);
                    }
                    else
                    {
                        for (int i = 0; i < Socket.socketClient.Count; i++)
                        {
                            Socket.socketClient[i].Send(buffer);
                        }
                    }
                 
                    Invoke(new Action(() =>
                    {
                        int length = ReceiveTextBox.TextLength;
                        string str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}][{Socket.SocketInfo.Name}][Send] ：{sendMessage}\r\n";
                        ReceiveTextBox.AppendText(str);
                        SetTextColor(length, str, Color.LightYellow);
                    }));

                    Invoke(new Action(() =>
                    {
                        isCheck = checkBox2.Checked;
                    }));
                    if (isCheck)
                    {
                        if (int.TryParse(DelayText.Text, out int time))
                        {
                            SF.Delay(time);
                        }
                    }

                }
                while (isCheck);

            }
            catch (Exception ex)
            {
                if (SF.frmComunication.Visible == true)
                {
                    Invoke(new Action(() =>
                    {
                        int length = ReceiveTextBox.TextLength;
                        string str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}][{Socket.SocketInfo.Name}][SocketException] {ex.Message}\r\n";
                        ReceiveTextBox.AppendText(str);
                        SetTextColor(length, str, Color.Red);
                    }));
                }
            }
        }

        public void SendSocketMessage(Socketer Socket, string sendMessage, SendTcpMsg sendTcpMsg)
        {
            try
            {
                if (sendTcpMsg.isConVert)
                {
                    if (int.TryParse(sendMessage, out int number))
                    {
                        sendMessage = Convert.ToString(number, 16).ToUpper();
                    }
                }
                byte[] buffer = Encoding.UTF8.GetBytes(sendMessage);
                if (Socket.SocketInfo.Type == "Client")
                {
                    Socket.socket.Send(buffer);
                }
                else
                {
                    for (int i = 0; i < Socket.socketClient.Count; i++)
                    {
                        Socket.socketClient[i].Send(buffer);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
        public void SendSerialPortMessageForm(SerialPorter serialPorter, string sendMessage)
        {
            bool isCheck = false;

            try
            {
                do
                {
                    Invoke(new Action(() =>
                    {
                        isCheck = checkBox1.Checked;
                    }));
                    if (isCheck)
                    {
                        if (int.TryParse(sendMessage, out int number))
                        {
                            sendMessage = Convert.ToString(number, 16).ToUpper();
                        }
                    }
                    serialPorter.serialPort.WriteLine(sendMessage); // 发送字符串数据

                    Invoke(new Action(() =>
                    {
                        int length = ReceiveTextBox.TextLength;
                        string str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}][{serialPorter.serialPortInfo.Port}][Send] ：{sendMessage}\r\n";
                        ReceiveTextBox.AppendText(str);
                        SetTextColor(length, str, Color.LightYellow);
                    }));

                    Invoke(new Action(() =>
                    {
                        isCheck = checkBox2.Checked;
                    }));
                    if (isCheck)
                    {
                        if (int.TryParse(DelayText.Text, out int time))
                        {
                            SF.Delay(time);
                        }
                    }

                }
                while (isCheck);

            }
            catch (Exception ex)
            {
                if (SF.frmComunication.Visible == true)
                {
                    Invoke(new Action(() =>
                    {
                        int length = ReceiveTextBox.TextLength;
                        string str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}][{serialPorter.serialPortInfo.Port}][SocketException] {ex.Message}\r\n";
                        ReceiveTextBox.AppendText(str);
                        SetTextColor(length, str, Color.Red);
                    }));
                }
            }
        }
        public void SendSerialPortMessage(SerialPorter serialPorter, string sendMessage, SendSerialPortMsg sendSerialPortMsg)
        {
            try
            {
                if (sendSerialPortMsg.isConVert)
                {
                    if (int.TryParse(sendMessage, out int number))
                    {
                        sendMessage = Convert.ToString(number, 16).ToUpper();
                    }
                }
                serialPorter.serialPort.WriteLine(sendMessage); // 发送字符串数据
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
        public void StartServer(SocketInfo socketInfo)
        {
            try
            {
                Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                serverSocket.Bind(new IPEndPoint(IPAddress.Parse(socketInfo.Address), socketInfo.Port));
                serverSocket.Listen(100);
                socketInfo.isServering = true;
                // dataGridView1.Rows[socketInfos.IndexOf(socketInfo)].Cells[5].Value = "已启动";
                Socketer socketer = new Socketer() { SocketInfo = socketInfo, socket = serverSocket };
                socketer.isRun.Set();
                socketers.Add(socketer);
                while (socketInfo.isServering)
                {
                    Socket clientSocket = serverSocket.Accept(); // 接受客户端连接
                    socketer.socketClient.Add(clientSocket);
                    Socketer socketClient = new Socketer() { SocketInfo = socketInfo, socket = clientSocket };
                    Task.Run(() =>
                    {
                        //   dataGridView1.Rows[socketInfos.IndexOf(socketInfo)].Cells[5].Value = "已连接";
                        HandleClient(socketer,socketClient, clientSocket);
                    });
                }

            }
            catch (SocketException se)
            {

            }
            catch (Exception ex)
            {
                if (SF.frmComunication.Visible == true)
                {
                    Invoke(new Action(() =>
                    {
                        int length = ReceiveTextBox.TextLength;
                        string str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}][{socketInfo.Name}][SocketException] {ex.Message}\r\n";
                        ReceiveTextBox.AppendText(str);
                        SetTextColor(length, str, Color.Red);
                    }));
                }
            }


            //}
        }
        public void HandleClient(Socketer socketerAll,Socketer socketer, Socket socketClient)
        {
            Console.WriteLine("客户端连接成功：" + socketer.socket.RemoteEndPoint);

            byte[] buffer = new byte[1024];
            int bytesRead;
            bool isCheck;
            while (true)
            {
                try
                {
                    bytesRead = socketer.socket.Receive(buffer); // 接收客户端数据

                    if (bytesRead == 0)
                        break;

                    string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                   // socketer.Msg = receivedData;
                    if (SF.frmComunication.Visible == true)
                    {
                        Invoke(new Action(() =>
                        {
                            isCheck = checkBox3.Checked;
                            if (isCheck)
                            {
                                if (int.TryParse(receivedData, out int number))
                                {
                                    receivedData = Convert.ToString(number, 16).ToUpper();
                                }
                            }
                            int length = ReceiveTextBox.TextLength;
                            string str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}][{socketer.socket.RemoteEndPoint}][{socketer.SocketInfo.Name}][Receive]：{receivedData}\r\n";
                            ReceiveTextBox.AppendText(str);
                            SetTextColor(length, str, Color.LightBlue);
                        }));
                    }
                    // 在此处可以根据需要处理消息，并向客户端发送响应

                    Array.Clear(buffer, 0, buffer.Length);
                }
                catch (SocketException ex)
                {
                    Console.WriteLine("客户端连接断开：" + ex.Message);
                    socketer.socketClient.Remove(socketClient);
                    break;
                }
            }

            socketerAll.socketClient.Remove(socketClient);
            Console.WriteLine("客户端连接已关闭");
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
        public void IsOnline()
        {
            Task.Run(() =>
            {

                while (true)
                {
                    try
                    {
                        if (CheckFormIsOpen(SF.frmComunication))
                        {

                            for (int i = 0; i < dataGridView1.Rows.Count; i++)
                            {
                                DataGridViewRow rowtemp = dataGridView1.Rows[i];
                                Socketer item = socketers.FirstOrDefault(sc => sc.SocketInfo.Name == rowtemp.Cells[1].Value);
                                if (item != null)
                                {
                                    if (item.SocketInfo.Type.ToString() == "Client")
                                    {
                                        bool isOnline = !((item.socket.Poll(1000, SelectMode.SelectRead) && (item.socket.Available == 0)) || !item.socket.Connected);
                                        int state = isOnline ? 1 : 0;
                                        if (state != item.state)
                                        {
                                            item.state = state;
                                            string str = isOnline ? "已连接" : "未连接";
                                            //foreach (DataGridViewRow row in dataGridView1.Rows)
                                            //{
                                            //    if (row.Cells[0].Value.ToString() == item.SocketInfo.ID.ToString())  // 检查第一列的单元格是否有值
                                            //    {
                                                    dataGridView1.Rows[i].Cells[5].Value = str;
                                                    break;
                                            //    }
                                            //}
                                            if (isOnline == false)
                                            {
                                                socketers.Remove(item);
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        //foreach (DataGridViewRow row in dataGridView1.Rows)
                                        //{
                                        //    if (row.Cells[0].Value.ToString() == item.SocketInfo.ID.ToString())  // 检查第一列的单元格是否有值
                                        //    {
                                                if (item.socketClient.Count == 0 && item.state != 1)
                                                {
                                                    dataGridView1.Rows[i].Cells[5].Value = "已启动";
                                                    item.state = 1;
                                                    break;
                                                }
                                                else if (item.socketClient.Count != item.ClientCount)
                                                {
                                                    dataGridView1.Rows[i].Cells[5].Value = $"已连接({item.socketClient.Count})";
                                                    item.state = 2;
                                                    item.ClientCount = item.socketClient.Count;
                                                    break;
                                                }
                                           // }
                                       // }

                                    }
                                }
                                else
                                {
                                    if(dataGridView1.Rows[i].Cells[5].Value != "")
                                    dataGridView1.Rows[i].Cells[5].Value = "";
                                }
                            }

                            //List<int> list = new List<int>();

                            for (int i = 0; i < dataGridView2.Rows.Count; i++)
                            {
                                SerialPorter serialPorter =serialPorters.FirstOrDefault(sc => sc.serialPortInfo.ID == serialPortInfos[i].ID);
                                if(serialPorter != null)
                                {
                                    bool isOpen = serialPorter.serialPort.IsOpen;
                                    if (isOpen != serialPorter.state)
                                    {
                                        serialPorter.state = isOpen;
                                        string str = isOpen ? "已打开" : "已关闭";

                                        int serialPorterIndex = serialPortInfos.IndexOf(serialPortInfos.FirstOrDefault(sc => sc.ID.ToString() == serialPorter.serialPortInfo.ID.ToString()));

                                        dataGridView2.Rows[serialPorterIndex].Cells[7].Value = str;

                                        if (isOpen == false)
                                        {
                                            serialPorters.Remove(serialPorter);
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    dataGridView2.Rows[i].Cells[7].Value = "已关闭";
                                }
                            }

                            //foreach (var item in serialPorters)
                            //{
                            //    bool isOpen = item.serialPort.IsOpen;
                            //    if (isOpen != item.state)
                            //    {
                            //        item.state = isOpen;
                            //        string str = isOpen ? "已打开" : "已关闭";

                            //        int serialPorterIndex = serialPortInfos.IndexOf(serialPortInfos.FirstOrDefault(sc => sc.ID.ToString() == item.serialPortInfo.ID.ToString()));

                            //        dataGridView2.Rows[serialPorterIndex].Cells[7].Value = str;
     

                            //        if (isOpen == false)
                            //        {
                            //            serialPorters.Remove(item);
                            //            break;
                            //        }
                            //    }
                            //}
                            //for (int i = 0; i < serialPortInfos.Count; i++)
                            //{
                            //    bool isExist = false ;
                            //    for(int j = 0;j < list.Count; j++)
                            //    {
                            //        if (i == list[j])
                            //        {
                            //            isExist = true;
                            //            break;
                            //        }                          
                            //    }
                            //    if(!isExist)
                            //    {
                            //        dataGridView2.Rows[i].Cells[7].Value = "已关闭";
                            //    }
                            //}
                           
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                    SF.Delay(1000);
                }
            });
        }
        public void SendMessage()
        {
            string text = SendTextBox.Text;
            TabPage selectedTabPage = tabControl1.SelectedTab;
            if (selectedTabPage == tabPage1)
            {
                string Id = dataGridView1.Rows[iSelectedSocketRow].Cells[0].Value?.ToString();
                Socketer socketClient = socketers.FirstOrDefault(sc => sc.SocketInfo.ID.ToString() == Id);
                Task receTask = Task.Run(() => SendSocketMessageForm(socketClient, text));
            }
            else if (selectedTabPage == tabPage2)
            {
                SerialPortInfo serialPortInfo = serialPortInfos[iSelectedSerialPortRow];
                SerialPorter serialPorter = serialPorters.FirstOrDefault(sc => sc.serialPortInfo.ID == serialPortInfo.ID);
                Task receTask = Task.Run(() => SendSerialPortMessageForm(serialPorter, text));

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
            if (socketInfos[iSelectedSocketRow].Type.ToString() == "Client")
            {
                Task receTask = Task.Run(() => TryConnect(socketInfos[iSelectedSocketRow]));
            }
            else
            {
                Task receTask = Task.Run(() => StartServer(socketInfos[iSelectedSocketRow]));
            }
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
            if (iSelectedSocketRow != -1)
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
            string Id = dataGridView1.Rows[iSelectedSocketRow].Cells[0].Value?.ToString();
            Socketer socketClient = socketers.FirstOrDefault(sc => sc.SocketInfo.ID.ToString() == Id);
            socketClient.socket.Dispose();
            socketClient.socket.Close();
            socketers.Remove(socketClient);
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
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            bool isCheck;
            SerialPort serialPort = (SerialPort)sender;
            SerialPorter serialPorter = serialPorters.FirstOrDefault(client => client.serialPort == serialPort);
            string data = serialPort.ReadExisting();

            if (serialPorter.isRun.WaitOne())
            {
                serialPorter.Msg = data;

                if (SF.frmComunication.Visible == true)
                {
                    Invoke(new Action(() =>
                    {
                        isCheck = checkBox3.Checked;
                        if (isCheck)
                        {
                            if (int.TryParse(data, out int number))
                            {
                                data = Convert.ToString(number, 16).ToUpper();
                            }
                        }
                        int length = ReceiveTextBox.TextLength;
                        string str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}][{serialPort.PortName}][Receive]：{data}\r\n";
                        ReceiveTextBox.AppendText(str);
                        SetTextColor(length, str, Color.LightBlue);

                    }));
                }
            }

           
        }
        public void ConnectSerialPort(SerialPortInfo serialPortInfo)
        {
            try
            {
                SerialPort serialPort = new SerialPort(serialPortInfo.Port, int.Parse(serialPortInfo.BitRate), (Parity)Parity.Parse(typeof(Parity), serialPortInfo.CheckBit), int.Parse(serialPortInfo.DataBit), (StopBits)StopBits.Parse(typeof(StopBits), serialPortInfo.StopBit)); // 替换为你的串口号和波特率

                SerialPorter serialPorter = new SerialPorter() { serialPortInfo = serialPortInfo, serialPort = serialPort };

                serialPorter.isRun.Set();

                serialPorters.Add(serialPorter);

                serialPort.Open();

                serialPort.DataReceived += SerialPort_DataReceived;

            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);

            }
        }
        private void OpenSerial_Click(object sender, EventArgs e)
        {
            Task receTask = Task.Run(() => ConnectSerialPort(serialPortInfos[iSelectedSerialPortRow]));
        }
        private void Close_Click(object sender, EventArgs e)
        {
            string Id = dataGridView2.Rows[iSelectedSerialPortRow].Cells[0].Value?.ToString();
            SerialPorter serialPorter = serialPorters.FirstOrDefault(sc => sc.serialPortInfo.ID.ToString() == Id);
            serialPorter.serialPort.Close();
            serialPorters.Remove(serialPorter);

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

    public class Socketer
    {
        public Socket socket { get; set; }
        public SocketInfo SocketInfo { get; set; }

        public int state;

        public int ClientCount;

        public ManualResetEvent isRun = new ManualResetEvent(false);

        public List<Socket> socketClient = new List<Socket>();

        public string Msg;
    }

    public class SocketInfo
    {
        public int ID { get; set; }
        public string Name { get; set; }

        public string Type { get; set; }
        public int Port { get; set; }
        public string Address { get; set; }
        public bool isServering { get; set; }

    }
    public class SerialPorter
    {
        public SerialPort serialPort { get; set; }
        public SerialPortInfo serialPortInfo { get; set; }

        public bool state;

        public ManualResetEvent isRun = new ManualResetEvent(false);

        public string Msg;

    }
    public class SerialPortInfo
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string Port { get; set; }
        public string BitRate { get; set; }
        public string CheckBit { get; set; }
        public string DataBit { get; set; }
        public string StopBit { get; set; }
    }
}

