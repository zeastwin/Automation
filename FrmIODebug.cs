using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using System.IO;
using System.Collections;

namespace Automation
{
    public partial class FrmIODebug : Form
    {
        public IODebugMap IODebugMaps = new IODebugMap();
        Font font = new Font("微软雅黑", 15, FontStyle.Regular);
        public List<Button> buttonsIn = new List<Button>();
        public List<Button> buttonsOut = new List<Button>();
        public List<ConnectButton> btnCon = new List<ConnectButton>();
        private ListView listView6;
        private readonly object ioCacheLock = new object();
        private Dictionary<string, IO> inputIoCache = new Dictionary<string, IO>(StringComparer.Ordinal);
        private Dictionary<string, IO> outputIoCache = new Dictionary<string, IO>(StringComparer.Ordinal);
        private HashSet<string> inputIoDup = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> outputIoDup = new HashSet<string>(StringComparer.Ordinal);
        private int ioCacheHash = 0;

        private readonly object ioRefreshLock = new object();
        private CancellationTokenSource ioRefreshCts;
        private volatile bool ioRefreshEnabled = false;
        private readonly int ioRefreshIntervalMs = 200;
        private volatile int currentTabIndex = 0;

        public FrmIODebug()
        {
            InitializeComponent();
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            this.UpdateStyles();
            listView1.View = View.SmallIcon;
            listView2.View = View.SmallIcon;
            listView3.View = View.SmallIcon;
            listView4.View = View.SmallIcon;
            listView5.View = View.SmallIcon;
            listView1.View = View.Details;
            listView2.View = View.Details;
            listView3.View = View.Details;
            listView4.View = View.Details;
            listView5.View = View.Details;
            listView4.CheckBoxes = true;
            listView5.CheckBoxes = true;
            listView6 = new ListView();
            listView6.AllowDrop = true;
            listView6.BackColor = Color.White;
            listView6.CheckBoxes = true;
            listView6.HideSelection = false;
            listView6.Name = "listView6";
            listView6.UseCompatibleStateImageBehavior = false;
            listView6.View = View.Details;
            listView6.Dock = DockStyle.Left;
            listView6.Width = 170;
            listView6.ItemChecked += listView6_ItemChecked;
            groupBox1.Controls.Add(listView6);
            groupBox1.Controls.SetChildIndex(listView4, 0);
            groupBox1.Controls.SetChildIndex(listView6, 1);
            groupBox1.Controls.SetChildIndex(listView3, 2);
            groupBox1.Controls.SetChildIndex(listView5, 3);
            listView3.Width = 170;
            listView5.Width = 170;
            listView4.Width = 170;
            ContextMenuStrip inputMenu = new ContextMenuStrip();
            ToolStripMenuItem inputConfigItem = new ToolStripMenuItem("配置显示");
            inputConfigItem.Click += InputConfigItem_Click;
            ToolStripMenuItem inputRemarkItem = new ToolStripMenuItem("添加备注");
            inputRemarkItem.Click += InputRemarkItem_Click;
            inputMenu.Items.Add(inputConfigItem);
            inputMenu.Items.Add(inputRemarkItem);
            listView1.ContextMenuStrip = inputMenu;
            ContextMenuStrip outputMenu = new ContextMenuStrip();
            ToolStripMenuItem outputConfigItem = new ToolStripMenuItem("配置显示");
            outputConfigItem.Click += OutputConfigItem_Click;
            ToolStripMenuItem outputRemarkItem = new ToolStripMenuItem("添加备注");
            outputRemarkItem.Click += OutputRemarkItem_Click;
            outputMenu.Items.Add(outputConfigItem);
            outputMenu.Items.Add(outputRemarkItem);
            listView2.ContextMenuStrip = outputMenu;
            ContextMenuStrip connectMenu = new ContextMenuStrip();
            ToolStripMenuItem connectConfigItem = new ToolStripMenuItem("配置显示");
            connectConfigItem.Click += ConnectConfigItem_Click;
            ToolStripMenuItem connectRemarkItem = new ToolStripMenuItem("添加备注");
            connectRemarkItem.Click += ConnectRemarkItem_Click;
            connectMenu.Items.Add(connectConfigItem);
            connectMenu.Items.Add(connectRemarkItem);
            listView3.ContextMenuStrip = connectMenu;
            this.VisibleChanged += FrmIODebug_VisibleChanged;
            this.Resize += FrmIODebug_Resize;
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
        bool[] InTemp = new bool[300];
        bool[] OutTemp = new bool[300];
        Connect[] ConnectTemp = new Connect[300];
        bool[] InValid = new bool[300];
        bool[] OutValid = new bool[300];
        bool[] ConnectOutValid = new bool[300];
        bool[] ConnectIn1Valid = new bool[300];
        bool[] ConnectIn2Valid = new bool[300];

        public class ConnectButton
        {
            public Control OutPut;
            public Label InPut1;
            public Label InPut2;
        }
        public class Connect
        {
            public bool OutPut = false;
            public bool InPut1 = false;
            public bool InPut2 = false;
        }

        public void RefleshIODebug()
        {
            UpdateRefreshEnabled();
        }
        private void FrmIODebug_VisibleChanged(object sender, EventArgs e)
        {
            UpdateRefreshEnabled();
        }

        private void FrmIODebug_Resize(object sender, EventArgs e)
        {
            UpdateRefreshEnabled();
        }

        private void UpdateRefreshEnabled()
        {
            bool enable = this.Visible && this.WindowState != FormWindowState.Minimized;
            ioRefreshEnabled = enable;
            if (enable)
            {
                StartIoRefreshLoop();
            }
            else
            {
                StopIoRefreshLoop();
            }
        }

        private void StartIoRefreshLoop()
        {
            lock (ioRefreshLock)
            {
                if (ioRefreshCts != null)
                {
                    return;
                }
                ioRefreshCts = new CancellationTokenSource();
                Task.Run(() => IoRefreshLoop(ioRefreshCts.Token));
            }
        }

        private void StopIoRefreshLoop()
        {
            lock (ioRefreshLock)
            {
                if (ioRefreshCts == null)
                {
                    return;
                }
                ioRefreshCts.Cancel();
                ioRefreshCts = null;
            }
        }

        private async Task IoRefreshLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!ioRefreshEnabled || SF.isModify == ModifyKind.IO)
                    {
                        await Task.Delay(ioRefreshIntervalMs, token);
                        continue;
                    }
                    IoRefreshData data = BuildIoRefreshData(currentTabIndex);
                    if (data != null)
                    {
                        try
                        {
                            BeginInvoke(new Action(() => ApplyIoRefresh(data)));
                        }
                        catch (InvalidOperationException)
                        {
                            return;
                        }
                    }
                    await Task.Delay(ioRefreshIntervalMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        private class IoRefreshData
        {
            public int TabIndex;
            public int InputCount;
            public int OutputCount;
            public int ConnectCount;
            public bool[] InputStates;
            public bool[] InputValid;
            public bool[] OutputStates;
            public bool[] OutputValid;
            public bool[] ConnectOutStates;
            public bool[] ConnectOutValid;
            public bool[] ConnectIn1States;
            public bool[] ConnectIn1Valid;
            public bool[] ConnectIn2States;
            public bool[] ConnectIn2Valid;
        }

        private IoRefreshData BuildIoRefreshData(int tabIndex)
        {
            try
            {
                UpdateIoCacheIfNeeded();
                if (tabIndex == 0)
                {
                    IO[] inputs = IODebugMaps.inputs.ToArray();
                    bool[] states = new bool[inputs.Length];
                    bool[] valid = new bool[inputs.Length];
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        IO ioItem = inputs[i];
                        if (ioItem == null || ioItem.IsRemark)
                        {
                            valid[i] = false;
                            continue;
                        }
                        bool open = false;
                        if (TryResolveIoByName(ioItem.Name, "通用输入", out IO io, false))
                        {
                            SF.motion.GetInIO(io, ref open);
                            states[i] = open;
                            valid[i] = true;
                        }
                        else
                        {
                            valid[i] = false;
                        }
                    }
                    return new IoRefreshData
                    {
                        TabIndex = 0,
                        InputCount = inputs.Length,
                        InputStates = states,
                        InputValid = valid
                    };
                }
                if (tabIndex == 1)
                {
                    IO[] outputs = IODebugMaps.outputs.ToArray();
                    bool[] states = new bool[outputs.Length];
                    bool[] valid = new bool[outputs.Length];
                    for (int i = 0; i < outputs.Length; i++)
                    {
                        IO ioItem = outputs[i];
                        if (ioItem == null || ioItem.IsRemark)
                        {
                            valid[i] = false;
                            continue;
                        }
                        bool open = false;
                        if (TryResolveIoByName(ioItem.Name, "通用输出", out IO io, false))
                        {
                            SF.motion.GetOutIO(io, ref open);
                            states[i] = open;
                            valid[i] = true;
                        }
                        else
                        {
                            valid[i] = false;
                        }
                    }
                    return new IoRefreshData
                    {
                        TabIndex = 1,
                        OutputCount = outputs.Length,
                        OutputStates = states,
                        OutputValid = valid
                    };
                }
                if (tabIndex == 2)
                {
                    IOConnect[] connects = IODebugMaps.iOConnects.ToArray();
                    bool[] outStates = new bool[connects.Length];
                    bool[] outValid = new bool[connects.Length];
                    bool[] in1States = new bool[connects.Length];
                    bool[] in1Valid = new bool[connects.Length];
                    bool[] in2States = new bool[connects.Length];
                    bool[] in2Valid = new bool[connects.Length];
                    for (int i = 0; i < connects.Length; i++)
                    {
                        IOConnect connect = connects[i];
                        if (connect?.Output == null || connect.Output.IsRemark)
                        {
                            outValid[i] = false;
                            in1Valid[i] = false;
                            in2Valid[i] = false;
                            continue;
                        }
                        bool open = false;
                        if (TryResolveIoByName(connect.Output.Name, "通用输出", out IO outputIo, false))
                        {
                            SF.motion.GetOutIO(outputIo, ref open);
                            outStates[i] = open;
                            outValid[i] = true;
                        }
                        else
                        {
                            outValid[i] = false;
                        }
                        if (connect.Intput1 != null && !string.IsNullOrWhiteSpace(connect.Intput1.Name)
                            && TryResolveIoByName(connect.Intput1.Name, "通用输入", out IO input1Io, false))
                        {
                            SF.motion.GetInIO(input1Io, ref open);
                            in1States[i] = open;
                            in1Valid[i] = true;
                        }
                        else
                        {
                            in1Valid[i] = false;
                        }
                        if (connect.Intput2 != null && !string.IsNullOrWhiteSpace(connect.Intput2.Name)
                            && TryResolveIoByName(connect.Intput2.Name, "通用输入", out IO input2Io, false))
                        {
                            SF.motion.GetInIO(input2Io, ref open);
                            in2States[i] = open;
                            in2Valid[i] = true;
                        }
                        else
                        {
                            in2Valid[i] = false;
                        }
                    }
                    return new IoRefreshData
                    {
                        TabIndex = 2,
                        ConnectCount = connects.Length,
                        ConnectOutStates = outStates,
                        ConnectOutValid = outValid,
                        ConnectIn1States = in1States,
                        ConnectIn1Valid = in1Valid,
                        ConnectIn2States = in2States,
                        ConnectIn2Valid = in2Valid
                    };
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        private void ApplyIoRefresh(IoRefreshData data)
        {
            if (!CheckFormIsOpen(this))
            {
                return;
            }
            if (data.TabIndex == 0)
            {
                if (buttonsIn.Count != IODebugMaps.inputs.Count || data.InputCount != IODebugMaps.inputs.Count)
                {
                    tabPage1.Controls.Clear();
                    buttonsIn = CreateButtonIO(IODebugMaps.inputs, tabPage1);
                    EnsureInputTempSize(IODebugMaps.inputs.Count);
                    return;
                }
                EnsureInputTempSize(data.InputCount);
                for (int i = 0; i < data.InputCount; i++)
                {
                    if (i >= buttonsIn.Count || buttonsIn[i] == null)
                    {
                        continue;
                    }
                    if (!data.InputValid[i])
                    {
                        if (buttonsIn[i].BackColor != Color.Red)
                        {
                            buttonsIn[i].BackColor = Color.Red;
                        }
                        InTemp[i] = false;
                        InValid[i] = false;
                        continue;
                    }
                    bool open = data.InputStates[i];
                    if (!InValid[i] || InTemp[i] != open)
                    {
                        buttonsIn[i].BackColor = open ? Color.Green : Color.Gray;
                        InTemp[i] = open;
                        InValid[i] = true;
                    }
                }
                return;
            }
            if (data.TabIndex == 1)
            {
                if (buttonsOut.Count != IODebugMaps.outputs.Count || data.OutputCount != IODebugMaps.outputs.Count)
                {
                    tabPage2.Controls.Clear();
                    buttonsOut = CreateButtonIO(IODebugMaps.outputs, tabPage2);
                    EnsureOutputTempSize(IODebugMaps.outputs.Count);
                    return;
                }
                EnsureOutputTempSize(data.OutputCount);
                for (int i = 0; i < data.OutputCount; i++)
                {
                    if (i >= buttonsOut.Count || buttonsOut[i] == null)
                    {
                        continue;
                    }
                    if (!data.OutputValid[i])
                    {
                        if (buttonsOut[i].BackColor != Color.Red)
                        {
                            buttonsOut[i].BackColor = Color.Red;
                        }
                        OutTemp[i] = false;
                        OutValid[i] = false;
                        continue;
                    }
                    bool open = data.OutputStates[i];
                    if (!OutValid[i] || OutTemp[i] != open)
                    {
                        buttonsOut[i].BackColor = open ? Color.Green : Color.Gray;
                        OutTemp[i] = open;
                        OutValid[i] = true;
                    }
                }
                return;
            }
            if (data.TabIndex == 2)
            {
                if (btnCon.Count != IODebugMaps.iOConnects.Count || data.ConnectCount != IODebugMaps.iOConnects.Count)
                {
                    tabPage3.Controls.Clear();
                    CreateButtonConnect();
                    EnsureConnectTempSize(IODebugMaps.iOConnects.Count);
                    return;
                }
                EnsureConnectTempSize(data.ConnectCount);
                for (int i = 0; i < data.ConnectCount; i++)
                {
                    IOConnect connect = IODebugMaps.iOConnects[i];
                    if (connect?.Output == null || connect.Output.IsRemark)
                    {
                        continue;
                    }
                    if (!data.ConnectOutValid[i])
                    {
                        if (btnCon[i].OutPut != null && btnCon[i].OutPut.BackColor != Color.Red)
                        {
                            btnCon[i].OutPut.BackColor = Color.Red;
                        }
                        ConnectTemp[i].OutPut = false;
                        ConnectOutValid[i] = false;
                    }
                    else
                    {
                        bool open = data.ConnectOutStates[i];
                        if (!ConnectOutValid[i] || ConnectTemp[i].OutPut != open)
                        {
                            if (btnCon[i].OutPut != null)
                            {
                                btnCon[i].OutPut.BackColor = open ? Color.Green : Color.Gray;
                            }
                            ConnectTemp[i].OutPut = open;
                            ConnectOutValid[i] = true;
                        }
                    }
                    if (!data.ConnectIn1Valid[i])
                    {
                        if (btnCon[i].InPut1 != null && btnCon[i].InPut1.BackColor != Color.Red)
                        {
                            btnCon[i].InPut1.BackColor = Color.Red;
                        }
                        ConnectTemp[i].InPut1 = false;
                        ConnectIn1Valid[i] = false;
                    }
                    else
                    {
                        bool open = data.ConnectIn1States[i];
                        if (!ConnectIn1Valid[i] || ConnectTemp[i].InPut1 != open)
                        {
                            if (btnCon[i].InPut1 != null)
                            {
                                btnCon[i].InPut1.BackColor = open ? Color.Green : Color.Gray;
                            }
                            ConnectTemp[i].InPut1 = open;
                            ConnectIn1Valid[i] = true;
                        }
                    }
                    if (!data.ConnectIn2Valid[i])
                    {
                        if (btnCon[i].InPut2 != null && btnCon[i].InPut2.BackColor != Color.Red)
                        {
                            btnCon[i].InPut2.BackColor = Color.Red;
                        }
                        ConnectTemp[i].InPut2 = false;
                        ConnectIn2Valid[i] = false;
                    }
                    else
                    {
                        bool open = data.ConnectIn2States[i];
                        if (!ConnectIn2Valid[i] || ConnectTemp[i].InPut2 != open)
                        {
                            if (btnCon[i].InPut2 != null)
                            {
                                btnCon[i].InPut2.BackColor = open ? Color.Green : Color.Gray;
                            }
                            ConnectTemp[i].InPut2 = open;
                            ConnectIn2Valid[i] = true;
                        }
                    }
                }
                return;
            }
        }

        private void UpdateIoCacheIfNeeded()
        {
            List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();
            if (cacheIOs == null)
            {
                lock (ioCacheLock)
                {
                    inputIoCache.Clear();
                    outputIoCache.Clear();
                    inputIoDup.Clear();
                    outputIoDup.Clear();
                    ioCacheHash = 0;
                }
                return;
            }
            IO[] snapshot;
            try
            {
                snapshot = cacheIOs.ToArray();
            }
            catch
            {
                return;
            }
            int hash = 17;
            StringComparer comparer = StringComparer.Ordinal;
            for (int i = 0; i < snapshot.Length; i++)
            {
                IO io = snapshot[i];
                if (io == null)
                {
                    continue;
                }
                hash = hash * 31 + comparer.GetHashCode(io.Name ?? string.Empty);
                hash = hash * 31 + comparer.GetHashCode(io.IOType ?? string.Empty);
                hash = hash * 31 + io.CardNum.GetHashCode();
                hash = hash * 31 + io.Module.GetHashCode();
                hash = hash * 31 + comparer.GetHashCode(io.IOIndex ?? string.Empty);
            }
            lock (ioCacheLock)
            {
                if (hash == ioCacheHash)
                {
                    return;
                }
                Dictionary<string, IO> newInput = new Dictionary<string, IO>(StringComparer.Ordinal);
                Dictionary<string, IO> newOutput = new Dictionary<string, IO>(StringComparer.Ordinal);
                HashSet<string> newInputDup = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> newOutputDup = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < snapshot.Length; i++)
                {
                    IO io = snapshot[i];
                    if (io == null || string.IsNullOrWhiteSpace(io.Name) || string.IsNullOrWhiteSpace(io.IOType))
                    {
                        continue;
                    }
                    if (io.IOType == "通用输入")
                    {
                        if (newInputDup.Contains(io.Name))
                        {
                            continue;
                        }
                        if (newInput.ContainsKey(io.Name))
                        {
                            newInput.Remove(io.Name);
                            newInputDup.Add(io.Name);
                            continue;
                        }
                        newInput.Add(io.Name, io);
                    }
                    else if (io.IOType == "通用输出")
                    {
                        if (newOutputDup.Contains(io.Name))
                        {
                            continue;
                        }
                        if (newOutput.ContainsKey(io.Name))
                        {
                            newOutput.Remove(io.Name);
                            newOutputDup.Add(io.Name);
                            continue;
                        }
                        newOutput.Add(io.Name, io);
                    }
                }
                inputIoCache = newInput;
                outputIoCache = newOutput;
                inputIoDup = newInputDup;
                outputIoDup = newOutputDup;
                ioCacheHash = hash;
            }
        }

        public void SetConnectItemm()
        {
            // 初始化右键菜单
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            listView3.ContextMenuStrip = contextMenu;
            contextMenu.Items.Clear();
            ToolStripMenuItem configItem = new ToolStripMenuItem("配置显示");
            configItem.Click += ConnectConfigItem_Click;
            ToolStripMenuItem remarkItem = new ToolStripMenuItem("添加备注");
            remarkItem.Click += ConnectRemarkItem_Click;
            contextMenu.Items.Add(configItem);
            contextMenu.Items.Add(remarkItem);
            listView3.Clear();
            listView3.Columns.Add("通用输出1", 220);
            listView4.Clear();
            listView4.Items.Clear();
            listView5.Clear();
            listView5.Items.Clear();
            listView6.Clear();
            listView6.Items.Clear();
            listView4.Columns.Add("通用输入", 220);
            listView5.Columns.Add("通用输入", 220);
            listView6.Columns.Add("通用输出2", 220);


            List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();

            if (cacheIOs == null) 
            {
                MessageBox.Show("轴卡未配置");
                return;
            }


            IO cacheIO;

            for (int j = 0; j < cacheIOs.Count; j++)
            {
                cacheIO = cacheIOs[j];
                if (cacheIO != null && cacheIO.Name != "" && cacheIO.IOType == "通用输出")
                {
                    string copiedString = string.Copy(cacheIO.Name);
                    ListViewItem item = new ListViewItem(copiedString);
                    item.Text = copiedString;
                    item.Font = font;
                    listView6.Items.Add(item);
                }
                if (cacheIO != null && cacheIO.Name != "" && cacheIO.IOType == "通用输入")
                {

                    string copiedString = string.Copy(cacheIO.Name);
                    ListViewItem item = new ListViewItem(copiedString);

                    item.Text = copiedString;
                    item.Font = font;

                    listView4.Items.Add(item);

                    ListViewItem item2 = new ListViewItem(copiedString);
                
                    item2.Text = copiedString;
                    item2.Font = font;
                    listView5.Items.Add(item2);
                }
            }

        }
        private void DynamicMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menuItem = (ToolStripMenuItem)sender;
            List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();
            IO cacheIO = cacheIOs.FirstOrDefault(dsh => dsh.Name == menuItem.Text);
            if (cacheIO == null)
            {
                return;
            }
            if (IODebugMaps.iOConnects.Any(item => item != null && item.Output != null && item.Output.Name == cacheIO.Name))
            {
                MessageBox.Show("调试列表已存在同名输出连接，已沿用现有配置。");
                return;
            }
            IODebugMaps.iOConnects.Add(new IOConnect() { Output = cacheIO.CloneForDebug() });
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefreshIODebugMapFrm();
            RefleshConnecdt();

        }
        public void RefleshConnecdt()
        {
            listView3.Clear();
            listView3.Columns.Add("通用输出1", 220);
            UpdateIoCacheIfNeeded();
            List<IOConnect> IOConnects = IODebugMaps.iOConnects;
            IOConnect iOConnect = null;
            for (int j = 0; j < IOConnects.Count; j++)
            {
                iOConnect = IOConnects[j];
                if (iOConnect == null || iOConnect.Output == null)
                {
                    continue;
                }
                string name = iOConnect.Output.Name;
                ListViewItem item = new ListViewItem(name);
                item.Text = name;
                item.Font = font;
                item.Tag = iOConnect;
                if (iOConnect.Output.IsRemark)
                {
                    item.ForeColor = Color.FromArgb(96, 96, 96);
                }
                else
                {
                    if (!TryResolveIoByName(name, "通用输出", out _, false))
                    {
                        item.ForeColor = Color.Red;
                    }
                    if (iOConnect.Output2 != null
                        && !string.IsNullOrWhiteSpace(iOConnect.Output2.Name)
                        && !TryResolveIoByName(iOConnect.Output2.Name, "通用输出", out _, false))
                    {
                        item.ForeColor = Color.Red;
                    }
                }
                listView3.Items.Add(item);

            }
        }
        private void FrmIODebug_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }
        private void FrmIODebug_Load(object sender, EventArgs e)
        {
            for (int i = 0; i < 300; i++)
            {
                ConnectTemp[i] = new Connect();
            }
            currentTabIndex = tabControl1.SelectedIndex;
            RefreshIODebugMapFrm();
            buttonsIn = CreateButtonIO(IODebugMaps.inputs, tabPage1);


        }
        public List<Button> CreateButtonIO(List<IO> iOs, TabPage tabPage)
        {
            List<Button> buttons = new List<Button>();
            tabPage.AutoScroll = true;
            int col = 0, row = 0;
            int colWidth = 160;
            int rowHeight = 46;
            for (int i = 0; i < iOs.Count; i++)
            {
                IO io = iOs[i];
                if (io == null)
                {
                    buttons.Add(null);
                }
                else if (io.IsRemark)
                {
                    Control remarkHeader = CreateRemarkHeader(io.Name, new Point(col * colWidth, row * rowHeight), 150, 34);
                    tabPage.Controls.Add(remarkHeader);
                    buttons.Add(null);
                }
                else
                {
                    Button dynamicButton = new Button();

                    dynamicButton.Text = io.Name;
                    dynamicButton.Location = new System.Drawing.Point(col * colWidth, row * rowHeight);
                    dynamicButton.Size = new System.Drawing.Size(150, 34);

                    tabPage.Controls.Add(dynamicButton);
                    buttons.Add(dynamicButton);
                    if (io.IOType == "通用输出")
                        dynamicButton.Click += new EventHandler(IOButton_Click);
                }
                row++;
                if (row > 10)
                {
                    row = 0;
                    col++;
                }
            }
            return buttons;

        }
        public void CreateButtonConnect()
        {
            int col = 0, row = 0;
            btnCon.Clear();
            int colWidth = 160;
            int rowHeight = 46;
            int itemWidth = 150;
            int itemHeight = 34;
            for (int i = 0; i < IODebugMaps.iOConnects.Count; i++)
            {
                IOConnect ioConnect = IODebugMaps.iOConnects[i];
                Control outputControl;
                if (!ioConnect.Output.IsRemark)
                {
                    Button dynamicButton = new Button();
                    dynamicButton.Text = ioConnect.Output.Name;
                    dynamicButton.Location = new System.Drawing.Point(col * colWidth, row * rowHeight);
                    dynamicButton.Size = new System.Drawing.Size(itemWidth, itemHeight);
                    dynamicButton.Tag = ioConnect;
                    dynamicButton.Click += new EventHandler(IOButton_Click);
                    tabPage3.Controls.Add(dynamicButton);
                    outputControl = dynamicButton;
                }
                else
                {
                    int remarkWidth = colWidth * 3 - 10;
                    Control remarkHeader = CreateRemarkHeader(ioConnect.Output.Name, new Point(col * colWidth, row * rowHeight), remarkWidth, itemHeight);
                    tabPage3.Controls.Add(remarkHeader);
                    outputControl = remarkHeader;
                }

                Label dynamicLabel1 = new Label();
                Label dynamicLabel2 = new Label();
                if (!ioConnect.Output.IsRemark)
                {
                    dynamicLabel1.Text = ioConnect.Intput1.Name;
                    dynamicLabel1.Location = new System.Drawing.Point(col * colWidth + colWidth, row * rowHeight);
                    dynamicLabel1.Size = new System.Drawing.Size(itemWidth, itemHeight);
                    dynamicLabel1.BackColor = System.Drawing.Color.Gray;
                    dynamicLabel1.TextAlign = ContentAlignment.MiddleCenter;

                    tabPage3.Controls.Add(dynamicLabel1);

                    dynamicLabel2.Text = ioConnect.Intput2.Name;
                    dynamicLabel2.Location = new System.Drawing.Point(col * colWidth + colWidth * 2, row * rowHeight);
                    dynamicLabel2.Size = new System.Drawing.Size(itemWidth, itemHeight);
                    dynamicLabel2.BackColor = System.Drawing.Color.Gray;
                    dynamicLabel2.TextAlign = ContentAlignment.MiddleCenter;

                    tabPage3.Controls.Add(dynamicLabel2);
                }

                btnCon.Add(new ConnectButton { OutPut = outputControl ,InPut1 = dynamicLabel1 ,InPut2 = dynamicLabel2});

                row++;
                if (row > 10)
                {
                    row = 0;
                    col++;
                }
            }

        }

        private void IOButton_Click(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                bool Open_1 = false;
                if (button.Tag is IOConnect ioConnect)
                {
                    if (!TryResolveIoByName(ioConnect.Output.Name, "通用输出", out IO outputIo))
                    {
                        button.BackColor = Color.Red;
                        return;
                    }
                    SF.motion.GetOutIO(outputIo, ref Open_1);
                    bool newState = !Open_1;
                    SF.motion.SetIO(outputIo, newState);
                    if (ioConnect.Output2 != null
                        && !string.IsNullOrWhiteSpace(ioConnect.Output2.Name)
                        && ioConnect.Output2.Name != ioConnect.Output.Name
                        && TryResolveIoByName(ioConnect.Output2.Name, "通用输出", out IO output2))
                    {
                        SF.motion.SetIO(output2, !newState);
                    }
                    return;
                }
                if (!TryResolveIoByName(button.Text, "通用输出", out IO outputIo2))
                {
                    button.BackColor = Color.Red;
                    return;
                }
                SF.motion.GetOutIO(outputIo2, ref Open_1);
                SF.motion.SetIO(outputIo2, !Open_1);
            }
        }
        private Control CreateRemarkHeader(string text, Point location, int width, int height)
        {
            Panel panel = new Panel();
            panel.Location = location;
            panel.Size = new Size(width, height);
            panel.BackColor = Color.FromArgb(236, 238, 242);

            Label textLabel = new Label();
            textLabel.Text = text;
            textLabel.Dock = DockStyle.Fill;
            textLabel.TextAlign = ContentAlignment.MiddleCenter;
            textLabel.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            textLabel.ForeColor = Color.FromArgb(64, 64, 64);
            textLabel.BackColor = Color.Transparent;

            int linePadding = 8;
            int textWidth = TextRenderer.MeasureText(text, textLabel.Font).Width;
            int lineWidth = Math.Max(12, (width - textWidth - linePadding * 2) / 2);
            int lineY = height / 2;

            Panel leftLine = new Panel();
            leftLine.BackColor = Color.FromArgb(170, 170, 170);
            leftLine.Size = new Size(lineWidth, 1);
            leftLine.Location = new Point(linePadding, lineY);

            Panel rightLine = new Panel();
            rightLine.BackColor = leftLine.BackColor;
            rightLine.Size = new Size(lineWidth, 1);
            rightLine.Location = new Point(width - linePadding - lineWidth, lineY);

            panel.Controls.Add(leftLine);
            panel.Controls.Add(rightLine);
            panel.Controls.Add(textLabel);
            return panel;
        }
        public void RefreshIODebugMapFrm()
        {
            listView1.Clear();
            listView2.Clear();
            listView1.Columns.Add("通用输入", 220);
            listView2.Columns.Add("通用输出", 220);
            UpdateIoCacheIfNeeded();
            //for (int i = 0; i < SF.frmCard.card.controlCards.Count; i++)
            //{
            List<IO> cacheIOs = SF.frmIODebug.IODebugMaps.inputs;

            IO cacheIO;

            for (int j = 0; j < cacheIOs.Count; j++)
            {
                cacheIO = cacheIOs[j];
                if (cacheIO != null)
                { 
                    string name = cacheIO.Name;
                    ListViewItem item = new ListViewItem(name);
                    if (cacheIO.IsRemark)
                    {
                        item.Text = $"【{name}】";
                        item.ForeColor = Color.FromArgb(96, 96, 96);
                    }
                    else
                    {
                        item.Text = name;
                    }
                    item.Font = font;
                    if (!cacheIO.IsRemark && !TryResolveIoByName(name, "通用输入", out _, false))
                    {
                        item.ForeColor = Color.Red;
                    }
                    listView1.Items.Add(item);

                }
            }

            cacheIOs = SF.frmIODebug.IODebugMaps.outputs;



            for (int j = 0; j < cacheIOs.Count; j++)
            {
                cacheIO = cacheIOs[j];
                if (cacheIO != null)
                {
                    string name = cacheIO.Name;
                    ListViewItem item = new ListViewItem(name);
                    if (cacheIO.IsRemark)
                    {
                        item.Text = $"【{name}】";
                        item.ForeColor = Color.FromArgb(96, 96, 96);
                    }
                    else
                    {
                        item.Text = name;
                    }
                    item.Font = font;
                    if (!cacheIO.IsRemark && !TryResolveIoByName(name, "通用输出", out _, false))
                    {
                        item.ForeColor = Color.Red;
                    }
                    listView2.Items.Add(item);

                }
            }
            // }



        }
        public void RefreshIODebugMap()
        {
            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }
            string filePath = Path.Combine(SF.ConfigPath, "IODebugMap.json");
            if (!File.Exists(filePath))
            {
                IODebugMaps = new IODebugMap();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", IODebugMaps);
                return;
            }
            try
            {
                IODebugMap IODebugMapTemp = SF.mainfrm.ReadJson<IODebugMap>(SF.ConfigPath, "IODebugMap");
                if (IODebugMapTemp != null)
                    IODebugMaps = IODebugMapTemp;
            }
            catch (Exception ex)
            {
                //Console.WriteLine(ex.Message);
                IODebugMaps = new IODebugMap();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", IODebugMaps);
            }
        }
        private void InputConfigItem_Click(object sender, EventArgs e)
        {
            OpenDebugConfig("通用输入");
        }
        private void OutputConfigItem_Click(object sender, EventArgs e)
        {
            OpenDebugConfig("通用输出");
        }
        private void InputRemarkItem_Click(object sender, EventArgs e)
        {
            AddRemarkItem("通用输入", IODebugMaps.inputs, listView1);
        }
        private void OutputRemarkItem_Click(object sender, EventArgs e)
        {
            AddRemarkItem("通用输出", IODebugMaps.outputs, listView2);
        }
        private void ConnectConfigItem_Click(object sender, EventArgs e)
        {
            OpenConnectConfig();
        }
        private void ConnectRemarkItem_Click(object sender, EventArgs e)
        {
            AddRemarkConnectItem();
        }
        private void OpenDebugConfig(string ioType)
        {
            List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();
            if (cacheIOs == null)
            {
                MessageBox.Show("轴卡未配置");
                return;
            }
            List<string> allNames = new List<string>();
            foreach (IO io in cacheIOs)
            {
                if (io == null || io.IOType != ioType || string.IsNullOrWhiteSpace(io.Name))
                {
                    continue;
                }
                if (!allNames.Contains(io.Name))
                {
                    allNames.Add(io.Name);
                }
            }
            HashSet<string> selectedNames = new HashSet<string>();
            List<IO> currentList = ioType == "通用输入" ? IODebugMaps.inputs : IODebugMaps.outputs;
            foreach (IO io in currentList)
            {
                if (io != null && !string.IsNullOrWhiteSpace(io.Name))
                {
                    if (io.IsRemark)
                    {
                        continue;
                    }
                    selectedNames.Add(io.Name);
                }
            }
            using (FrmIODebugConfig frm = new FrmIODebugConfig($"{ioType}显示配置", allNames, selectedNames))
            {
                if (frm.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                ApplyDebugSelection(ioType, frm.SelectedNames, cacheIOs);
            }
        }
        private void ApplyDebugSelection(string ioType, List<string> selectedNames, List<IO> cacheIOs)
        {
            Dictionary<string, IO> ioByName = new Dictionary<string, IO>();
            foreach (IO io in cacheIOs)
            {
                if (io == null || io.IOType != ioType || string.IsNullOrWhiteSpace(io.Name))
                {
                    continue;
                }
                if (!ioByName.ContainsKey(io.Name))
                {
                    ioByName.Add(io.Name, io);
                }
            }
            HashSet<string> selectedSet = new HashSet<string>(selectedNames);
            List<IO> currentList = ioType == "通用输入" ? IODebugMaps.inputs : IODebugMaps.outputs;
            List<IO> newList = new List<IO>();
            foreach (IO io in currentList)
            {
                if (io == null || string.IsNullOrWhiteSpace(io.Name))
                {
                    continue;
                }
                if (io.IsRemark)
                {
                    newList.Add(io);
                    continue;
                }
                if (!selectedSet.Contains(io.Name))
                {
                    continue;
                }
                if (ioByName.TryGetValue(io.Name, out IO sourceIo))
                {
                    IO cloned = sourceIo.CloneForDebug();
                    newList.Add(cloned);
                    selectedSet.Remove(io.Name);
                }
            }
            foreach (string name in selectedNames)
            {
                if (!selectedSet.Contains(name))
                {
                    continue;
                }
                if (ioByName.TryGetValue(name, out IO sourceIo))
                {
                    IO cloned = sourceIo.CloneForDebug();
                    newList.Add(cloned);
                    selectedSet.Remove(name);
                }
            }
            if (ioType == "通用输入")
            {
                IODebugMaps.inputs = newList;
            }
            else
            {
                IODebugMaps.outputs = newList;
            }
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefreshIODebugMapFrm();
        }
        private void OpenConnectConfig()
        {
            List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();
            if (cacheIOs == null)
            {
                MessageBox.Show("轴卡未配置");
                return;
            }
            List<string> allNames = new List<string>();
            foreach (IO io in cacheIOs)
            {
                if (io == null || io.IOType != "通用输出" || string.IsNullOrWhiteSpace(io.Name))
                {
                    continue;
                }
                if (!allNames.Contains(io.Name))
                {
                    allNames.Add(io.Name);
                }
            }
            HashSet<string> selectedNames = new HashSet<string>();
            foreach (IOConnect connect in IODebugMaps.iOConnects)
            {
                if (connect?.Output == null || connect.Output.IsRemark)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(connect.Output.Name))
                {
                    selectedNames.Add(connect.Output.Name);
                }
            }
            using (FrmIODebugConfig frm = new FrmIODebugConfig("输入输出关联显示配置", allNames, selectedNames))
            {
                if (frm.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                ApplyConnectSelection(frm.SelectedNames, cacheIOs);
            }
        }
        private void ApplyConnectSelection(List<string> selectedNames, List<IO> cacheIOs)
        {
            Dictionary<string, IO> ioByName = new Dictionary<string, IO>();
            foreach (IO io in cacheIOs)
            {
                if (io == null || io.IOType != "通用输出" || string.IsNullOrWhiteSpace(io.Name))
                {
                    continue;
                }
                if (!ioByName.ContainsKey(io.Name))
                {
                    ioByName.Add(io.Name, io);
                }
            }
            HashSet<string> selectedSet = new HashSet<string>(selectedNames);
            List<IOConnect> newList = new List<IOConnect>();
            foreach (IOConnect connect in IODebugMaps.iOConnects)
            {
                if (connect?.Output == null)
                {
                    continue;
                }
                if (connect.Output.IsRemark)
                {
                    newList.Add(connect);
                    continue;
                }
                if (!selectedSet.Contains(connect.Output.Name))
                {
                    continue;
                }
                if (ioByName.TryGetValue(connect.Output.Name, out IO outputIo))
                {
                    connect.Output = outputIo.CloneForDebug();
                }
                newList.Add(connect);
                selectedSet.Remove(connect.Output.Name);
            }
            foreach (string name in selectedNames)
            {
                if (!selectedSet.Contains(name))
                {
                    continue;
                }
                if (ioByName.TryGetValue(name, out IO outputIo))
                {
                    IOConnect connect = new IOConnect();
                    connect.Output = outputIo.CloneForDebug();
                    newList.Add(connect);
                    selectedSet.Remove(name);
                }
            }
            IODebugMaps.iOConnects = newList;
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefleshConnecdt();
        }
        private void AddRemarkItem(string ioType, List<IO> targetList, ListView targetView)
        {
            string remarkText = PromptRemark($"{ioType}备注");
            if (string.IsNullOrWhiteSpace(remarkText))
            {
                return;
            }
            IO remark = new IO
            {
                Name = remarkText.Trim(),
                IOType = ioType,
                IsRemark = true
            };
            int insertIndex = targetList.Count;
            if (targetView.SelectedItems.Count > 0)
            {
                insertIndex = targetView.SelectedItems[0].Index + 1;
                if (insertIndex < 0 || insertIndex > targetList.Count)
                {
                    insertIndex = targetList.Count;
                }
            }
            targetList.Insert(insertIndex, remark);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefreshIODebugMapFrm();
        }
        private void AddRemarkConnectItem()
        {
            string remarkText = PromptRemark("输入输出关联备注");
            if (string.IsNullOrWhiteSpace(remarkText))
            {
                return;
            }
            IOConnect remark = new IOConnect();
            remark.Output.Name = remarkText.Trim();
            remark.Output.IOType = "通用输出";
            remark.Output.IsRemark = true;
            int insertIndex = IODebugMaps.iOConnects.Count;
            if (listView3.SelectedItems.Count > 0)
            {
                insertIndex = listView3.SelectedItems[0].Index + 1;
                if (insertIndex < 0 || insertIndex > IODebugMaps.iOConnects.Count)
                {
                    insertIndex = IODebugMaps.iOConnects.Count;
                }
            }
            IODebugMaps.iOConnects.Insert(insertIndex, remark);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefleshConnecdt();
        }
        private string PromptRemark(string title)
        {
            using (Form form = new Form())
            {
                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.Width = 300;
                form.Height = 150;

                Label label = new Label();
                label.Text = "备注内容";
                label.AutoSize = true;
                label.Location = new Point(12, 12);

                TextBox textBox = new TextBox();
                textBox.Location = new Point(12, 36);
                textBox.Width = 260;

                Button ok = new Button();
                ok.Text = "确定";
                ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(116, 70);
                ok.Width = 70;

                Button cancel = new Button();
                cancel.Text = "取消";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(202, 70);
                cancel.Width = 70;

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    return textBox.Text.Trim();
                }
            }
            return string.Empty;
        }
        private void EnsureInputTempSize(int count)
        {
            if (InTemp.Length < count)
            {
                Array.Resize(ref InTemp, count);
            }
            if (InValid.Length < count)
            {
                Array.Resize(ref InValid, count);
            }
        }
        private void EnsureOutputTempSize(int count)
        {
            if (OutTemp.Length < count)
            {
                Array.Resize(ref OutTemp, count);
            }
            if (OutValid.Length < count)
            {
                Array.Resize(ref OutValid, count);
            }
        }
        private void EnsureConnectTempSize(int count)
        {
            if (ConnectTemp.Length < count)
            {
                int oldSize = ConnectTemp.Length;
                Array.Resize(ref ConnectTemp, count);
                for (int i = oldSize; i < count; i++)
                {
                    ConnectTemp[i] = new Connect();
                }
            }
            if (ConnectOutValid.Length < count)
            {
                Array.Resize(ref ConnectOutValid, count);
            }
            if (ConnectIn1Valid.Length < count)
            {
                Array.Resize(ref ConnectIn1Valid, count);
            }
            if (ConnectIn2Valid.Length < count)
            {
                Array.Resize(ref ConnectIn2Valid, count);
            }
        }
        private bool TryResolveIoByName(string name, string ioType, out IO io, bool ensureCache = true)
        {
            io = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            if (ensureCache)
            {
                UpdateIoCacheIfNeeded();
            }
            lock (ioCacheLock)
            {
                if (ioType == "通用输入")
                {
                    if (inputIoDup.Contains(name))
                    {
                        return false;
                    }
                    return inputIoCache.TryGetValue(name, out io);
                }
                if (ioType == "通用输出")
                {
                    if (outputIoDup.Contains(name))
                    {
                        return false;
                    }
                    return outputIoCache.TryGetValue(name, out io);
                }
            }
            return false;
        }
        private ListViewItem sourceItem;
        private ListViewItem targetItem;
        private void listView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            sourceItem = (ListViewItem)e.Item;
            listView1.DoDragDrop(e.Item, DragDropEffects.Move);
        }

        private void listView1_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void listView1_DragDrop(object sender, DragEventArgs e)
        {
            Point dropPoint = listView1.PointToClient(new Point(e.X, e.Y));
            ListViewItem targetItem = listView1.GetItemAt(dropPoint.X, dropPoint.Y);

            if (targetItem == null)
            {
                return;
            }

            int sourceIndex = sourceItem.Index;
            int targetIndex = targetItem.Index;

            // 交换项的位置

            IO temp = SF.frmIODebug.IODebugMaps.inputs[sourceIndex];
            SF.frmIODebug.IODebugMaps.inputs[sourceIndex] = SF.frmIODebug.IODebugMaps.inputs[targetIndex];
            SF.frmIODebug.IODebugMaps.inputs[targetIndex] = temp;
            // 取消目标项的高亮显示
            if (targetItem != null)
            {
                targetItem.BackColor = Color.White;
            }
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefreshIODebugMapFrm();
        }

        private void listView1_DragOver(object sender, DragEventArgs e)
        {
            Point dropPoint = listView1.PointToClient(new Point(e.X, e.Y));
            ListViewItem newTargetItem = listView1.GetItemAt(dropPoint.X, dropPoint.Y);

            if (newTargetItem != null && newTargetItem != targetItem)
            {
                // 取消之前目标项的高亮显示
                if (targetItem != null)
                {
                    targetItem.BackColor = Color.White;
                }

                // 设置新目标项的高亮显示
                targetItem = newTargetItem;
                targetItem.BackColor = Color.LightBlue;
            }

            e.Effect = DragDropEffects.Move;
        }
        private ListViewItem sourceItem2;
        private ListViewItem targetItem2;
        private void listView2_DragDrop(object sender, DragEventArgs e)
        {
            Point dropPoint = listView2.PointToClient(new Point(e.X, e.Y));
            ListViewItem targetItem = listView2.GetItemAt(dropPoint.X, dropPoint.Y);

            if (targetItem == null)
            {
                return;
            }

            int sourceIndex = sourceItem2.Index;
            int targetIndex = targetItem.Index;

            // 交换项的位置

            IO temp = SF.frmIODebug.IODebugMaps.outputs[sourceIndex];
            SF.frmIODebug.IODebugMaps.outputs[sourceIndex] = SF.frmIODebug.IODebugMaps.outputs[targetIndex];
            SF.frmIODebug.IODebugMaps.outputs[targetIndex] = temp;
            // 取消目标项的高亮显示
            if (targetItem != null)
            {
                targetItem.BackColor = Color.White;
            }
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefreshIODebugMapFrm();
        }

        private void listView2_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void listView2_DragOver(object sender, DragEventArgs e)
        {
            Point dropPoint = listView2.PointToClient(new Point(e.X, e.Y));
            ListViewItem newTargetItem = listView2.GetItemAt(dropPoint.X, dropPoint.Y);

            if (newTargetItem != null && newTargetItem != targetItem2)
            {
                // 取消之前目标项的高亮显示
                if (targetItem2 != null)
                {
                    targetItem2.BackColor = Color.White;
                }

                // 设置新目标项的高亮显示
                targetItem2 = newTargetItem;
                targetItem2.BackColor = Color.LightBlue;
            }

            e.Effect = DragDropEffects.Move;
        }

        private void listView2_ItemDrag(object sender, ItemDragEventArgs e)
        {
            sourceItem2 = (ListViewItem)e.Item;
            listView2.DoDragDrop(e.Item, DragDropEffects.Move);
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            int selectedIndex = tabControl1.SelectedIndex;
            currentTabIndex = selectedIndex;

            switch (selectedIndex)
            {
                case 0:
                    {
                        IoRefreshData data = BuildIoRefreshData(0);
                        if (data != null)
                        {
                            ApplyIoRefresh(data);
                        }
                    }
                    break;
                case 1:
                    {
                        IoRefreshData data = BuildIoRefreshData(1);
                        if (data != null)
                        {
                            ApplyIoRefresh(data);
                        }
                    }
                    break;
                case 2:
                    {
                        IoRefreshData data = BuildIoRefreshData(2);
                        if (data != null)
                        {
                            ApplyIoRefresh(data);
                        }
                    }
                    break;
                case 3:
                    listView4.ItemChecked -= listView4_ItemChecked;
                    listView5.ItemChecked -= listView5_ItemChecked;
                    listView6.ItemChecked -= listView6_ItemChecked;
                    SetConnectItemm();
                    RefreshIODebugMapFrm();
                    RefleshConnecdt();
                    listView4.ItemChecked += listView4_ItemChecked;
                    listView5.ItemChecked += listView5_ItemChecked;
                    listView6.ItemChecked += listView6_ItemChecked;
                  
                    break;
                default:
                    break;
            }
        }



        private void listView4_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            ListView listView = (ListView)sender;
            if (e.Item.Checked)
            {

                if (listView3.SelectedItems.Count != 0)
                {
                    listView4.ItemChecked -= listView4_ItemChecked;
                    foreach (ListViewItem item in listView.CheckedItems)
                    {
                        if (item != e.Item)
                        {
                            item.Checked = false;
                        }
                        else
                        {
                            item.Checked = true;
                        }
                    }

                    listView4.ItemChecked += listView4_ItemChecked;
                    List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();
                    IO cacheIO = cacheIOs.FirstOrDefault(dsh => dsh.Name == e.Item.Text);
                    IOConnect iOConnect = GetSelectedConnect();
                    if (cacheIO != null && iOConnect != null && iOConnect.Output != null && !iOConnect.Output.IsRemark)
                    {
                        iOConnect.Intput1 = cacheIO.CloneForDebug();

                        SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
                    }
                }
            }
            else
            {
                if (listView3.SelectedItems.Count != 0)
                {
                    IOConnect iOConnect = GetSelectedConnect();
                    if (iOConnect != null && iOConnect.Output != null && !iOConnect.Output.IsRemark)
                    {
                        iOConnect.Intput1.Name = "";

                        SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
                    }
                }
            }
        }
        private void listView5_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            ListView listView = (ListView)sender;
            if (e.Item.Checked)
            {

                if (listView3.SelectedItems.Count != 0)
                {
                    listView5.ItemChecked -= listView5_ItemChecked;
                    foreach (ListViewItem item in listView.CheckedItems)
                    {
                        if (item != e.Item)
                        {
                            item.Checked = false;
                        }
                        else
                        {
                            item.Checked = true;
                        }
                    }
                    listView5.ItemChecked += listView5_ItemChecked;
                    List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();
                    IO cacheIO = cacheIOs.FirstOrDefault(dsh => dsh.Name == e.Item.Text);
                    IOConnect iOConnect = GetSelectedConnect();
                    if (cacheIO != null && iOConnect != null && iOConnect.Output != null && !iOConnect.Output.IsRemark)
                    {
                        iOConnect.Intput2 = cacheIO.CloneForDebug();

                        SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
                    }

                }

            }
            else
            {
                if (listView3.SelectedItems.Count != 0)
                {
                    IOConnect iOConnect = GetSelectedConnect();
                    if (iOConnect != null && iOConnect.Output != null && !iOConnect.Output.IsRemark)
                    {
                        iOConnect.Intput2.Name = "";

                        SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
                    }

                }
            }
        }
        private void listView6_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            ListView listView = (ListView)sender;
            if (e.Item.Checked)
            {

                if (listView3.SelectedItems.Count != 0)
                {
                    listView6.ItemChecked -= listView6_ItemChecked;
                    foreach (ListViewItem item in listView.CheckedItems)
                    {
                        if (item != e.Item)
                        {
                            item.Checked = false;
                        }
                        else
                        {
                            item.Checked = true;
                        }
                    }

                    listView6.ItemChecked += listView6_ItemChecked;
                    List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();
                    if (cacheIOs == null)
                    {
                        return;
                    }
                    IO cacheIO = cacheIOs.FirstOrDefault(dsh => dsh.Name == e.Item.Text);
                    IOConnect iOConnect = GetSelectedConnect();
                    if (cacheIO != null && iOConnect != null && iOConnect.Output != null && !iOConnect.Output.IsRemark)
                    {
                        iOConnect.Output2 = cacheIO.CloneForDebug();

                        SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
                    }
                }
            }
            else
            {
                if (listView3.SelectedItems.Count != 0)
                {
                    IOConnect iOConnect = GetSelectedConnect();
                    if (iOConnect != null && iOConnect.Output != null && !iOConnect.Output.IsRemark)
                    {
                        if (iOConnect.Output2 == null)
                        {
                            iOConnect.Output2 = new IO();
                        }
                        iOConnect.Output2.Name = "";

                        SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
                    }
                }
            }
        }
        private void listView3_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView3.SelectedItems.Count > 0)
            {
                listView4.ItemChecked -= listView4_ItemChecked;
                listView5.ItemChecked -= listView5_ItemChecked;
                listView6.ItemChecked -= listView6_ItemChecked;
                IOConnect iOConnect = GetSelectedConnect();
                if (iOConnect == null || iOConnect.Output == null || iOConnect.Output.IsRemark)
                {
                    foreach (ListViewItem item in listView4.CheckedItems)
                    {
                        item.Checked = false;
                    }
                    foreach (ListViewItem item in listView5.CheckedItems)
                    {
                        item.Checked = false;
                    }
                    foreach (ListViewItem item in listView6.CheckedItems)
                    {
                        item.Checked = false;
                    }
                    listView4.ItemChecked += listView4_ItemChecked;
                    listView5.ItemChecked += listView5_ItemChecked;
                    listView6.ItemChecked += listView6_ItemChecked;
                    return;
                }
                if (iOConnect.Intput1.Name == "")
                {
                    foreach (ListViewItem item in listView4.CheckedItems)
                    {
                        item.Checked = false;
                    }
                }
                else
                {
                    foreach (ListViewItem item in listView4.Items)
                    {
                        if (item.Text == iOConnect.Intput1.Name)
                        {
                            item.Checked = true;
                        }
                        else
                        {
                            item.Checked = false;
                        }
                    }
                   
                }
                if (iOConnect.Intput2.Name == "")
                {
                    foreach (ListViewItem item in listView5.CheckedItems)
                    {
                        if (item.Text != iOConnect.Intput2.Name)
                            item.Checked = false;
                        else
                            item.Checked = true;
                    }
                }
                else
                {
                    foreach (ListViewItem item in listView5.Items)
                    {
                        if (item.Text == iOConnect.Intput2.Name)
                        {
                            item.Checked = true;
                        }
                        else
                        {
                            item.Checked = false;
                        }
                    }
                }
                listView4.ItemChecked += listView4_ItemChecked;
                listView5.ItemChecked += listView5_ItemChecked;
                if (iOConnect.Output2 == null || iOConnect.Output2.Name == "")
                {
                    foreach (ListViewItem item in listView6.CheckedItems)
                    {
                        item.Checked = false;
                    }
                }
                else
                {
                    foreach (ListViewItem item in listView6.Items)
                    {
                        if (item.Text == iOConnect.Output2.Name)
                        {
                            item.Checked = true;
                        }
                        else
                        {
                            item.Checked = false;
                        }
                    }
                }
                listView6.ItemChecked += listView6_ItemChecked;
            }
        }
        private IOConnect GetSelectedConnect()
        {
            if (listView3.SelectedItems.Count == 0)
            {
                return null;
            }
            return listView3.SelectedItems[0].Tag as IOConnect;
        }
        private ListViewItem sourceItem3;
        private ListViewItem targetItem3;
        private void listView3_DragDrop(object sender, DragEventArgs e)
        {
            Point dropPoint = listView3.PointToClient(new Point(e.X, e.Y));
            ListViewItem targetItem = listView3.GetItemAt(dropPoint.X, dropPoint.Y);

            if (targetItem == null)
            {
                return;
            }

            int sourceIndex = sourceItem3.Index;
            int targetIndex = targetItem.Index;

            // 交换项的位置

            IOConnect temp = SF.frmIODebug.IODebugMaps.iOConnects[sourceIndex];
            SF.frmIODebug.IODebugMaps.iOConnects[sourceIndex] = SF.frmIODebug.IODebugMaps.iOConnects[targetIndex];
            SF.frmIODebug.IODebugMaps.iOConnects[targetIndex] = temp;

            // 取消目标项的高亮显示
            if (targetItem != null)
            {
                targetItem.BackColor = Color.White;
            }
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefleshConnecdt();
        }

        private void listView3_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void listView3_DragOver(object sender, DragEventArgs e)
        {
            Point dropPoint = listView3.PointToClient(new Point(e.X, e.Y));
            ListViewItem newTargetItem = listView3.GetItemAt(dropPoint.X, dropPoint.Y);

            if (newTargetItem != null && newTargetItem != targetItem3)
            {
                // 取消之前目标项的高亮显示
                if (targetItem3 != null)
                {
                    targetItem3.BackColor = Color.White;
                }

                // 设置新目标项的高亮显示
                targetItem3 = newTargetItem;
                targetItem3.BackColor = Color.LightBlue;
            }

            e.Effect = DragDropEffects.Move;
        }

        private void listView3_ItemDrag(object sender, ItemDragEventArgs e)
        {
            sourceItem3 = (ListViewItem)e.Item;
            listView3.DoDragDrop(e.Item, DragDropEffects.Move);
        }
    }
    public class IODebugMap
    {
        public List<IO> inputs = new List<IO>();
        public List<IO> outputs = new List<IO>();
        public List<IOConnect> iOConnects = new List<IOConnect>();
    }
    public class IOConnect
    {
        public IO Output = new IO();
        public IO Output2 = new IO();
        public IO Intput1 = new IO();
        public IO Intput2 = new IO();
    }
}
