using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
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
        private readonly Timer ioRefreshTimer = new Timer();
        private bool ioRefreshTimerInit = false;

        public FrmIODebug()
        {
            InitializeComponent();
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
            listView6.Dock = DockStyle.Right;
            listView6.Width = 170;
            listView6.ItemChecked += listView6_ItemChecked;
            groupBox1.Controls.Add(listView6);
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
            this.VisibleChanged += FrmIODebug_VisibleChanged;
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

        public class ConnectButton
        {
            public Button OutPut;
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
            if (!ioRefreshTimerInit)
            {
                ioRefreshTimer.Interval = 200;
                ioRefreshTimer.Tick += IoRefreshTimer_Tick;
                ioRefreshTimerInit = true;
            }
            if (!ioRefreshTimer.Enabled)
            {
                ioRefreshTimer.Start();
            }
        }
        private void FrmIODebug_VisibleChanged(object sender, EventArgs e)
        {
            if (!ioRefreshTimerInit)
            {
                return;
            }
            ioRefreshTimer.Enabled = this.Visible;
        }

        private void IoRefreshTimer_Tick(object sender, EventArgs e)
        {
            if (!CheckFormIsOpen(SF.frmIODebug))
            {
                return;
            }
            if (SF.isModify == ModifyKind.IO)
            {
                return;
            }
            try
            {
                if (tabControl1.SelectedIndex == 0)
                {
                    if (buttonsIn.Count != IODebugMaps.inputs.Count)
                    {
                        tabPage1.Controls.Clear();
                        buttonsIn = CreateButtonIO(IODebugMaps.inputs, tabPage1);
                    }
                    EnsureInputTempSize(IODebugMaps.inputs.Count);
                    for (int i = 0; i < IODebugMaps.inputs.Count; i++)
                    {
                        if (i >= buttonsIn.Count)
                        {
                            continue;
                        }
                        IO ioItem = IODebugMaps.inputs[i];
                        if (ioItem == null || ioItem.IsRemark || buttonsIn[i] == null)
                        {
                            continue;
                        }
                        bool Open_1 = false;
                        if (TryResolveIoByName(ioItem.Name, "通用输入", out IO io))
                        {
                            SF.motion.GetInIO(io, ref Open_1);
                            if (Open_1 != InTemp[i])
                            {
                                buttonsIn[i].BackColor = Open_1 ? Color.Green : Color.Gray;
                                InTemp[i] = Open_1;
                            }
                        }
                        else
                        {
                            buttonsIn[i].BackColor = Color.Red;
                            InTemp[i] = false;
                        }
                    }
                }
                else if (tabControl1.SelectedIndex == 1)
                {
                    if (buttonsOut.Count != IODebugMaps.outputs.Count)
                    {
                        tabPage2.Controls.Clear();
                        buttonsOut = CreateButtonIO(IODebugMaps.outputs, tabPage2);
                    }
                    EnsureOutputTempSize(IODebugMaps.outputs.Count);
                    for (int i = 0; i < IODebugMaps.outputs.Count; i++)
                    {
                        if (i >= buttonsOut.Count)
                        {
                            continue;
                        }
                        IO ioItem = IODebugMaps.outputs[i];
                        if (ioItem == null || ioItem.IsRemark || buttonsOut[i] == null)
                        {
                            continue;
                        }
                        bool Open_1 = false;
                        if (TryResolveIoByName(ioItem.Name, "通用输出", out IO io))
                        {
                            SF.motion.GetOutIO(io, ref Open_1);
                            if (Open_1 != OutTemp[i])
                            {
                                buttonsOut[i].BackColor = Open_1 ? Color.Green : Color.Gray;
                                OutTemp[i] = Open_1;
                            }
                        }
                        else
                        {
                            buttonsOut[i].BackColor = Color.Red;
                            OutTemp[i] = false;
                        }
                    }
                }
                else if (tabControl1.SelectedIndex == 2)
                {
                    if (btnCon.Count != IODebugMaps.iOConnects.Count)
                    {
                        tabPage3.Controls.Clear();
                        CreateButtonConnect();
                    }
                    EnsureConnectTempSize(IODebugMaps.iOConnects.Count);
                    for (int i = 0; i < IODebugMaps.iOConnects.Count; i++)
                    {
                        bool Open_1 = false;
                        if (TryResolveIoByName(IODebugMaps.iOConnects[i].Output.Name, "通用输出", out IO outputIo))
                        {
                            SF.motion.GetOutIO(outputIo, ref Open_1);
                            if (Open_1 != ConnectTemp[i].OutPut)
                            {
                                btnCon[i].OutPut.BackColor = Open_1 ? Color.Green : Color.Gray;
                                ConnectTemp[i].OutPut = Open_1;
                            }
                        }
                        else
                        {
                            btnCon[i].OutPut.BackColor = Color.Red;
                            ConnectTemp[i].OutPut = false;
                        }
                        if (TryResolveIoByName(IODebugMaps.iOConnects[i].Intput1.Name, "通用输入", out IO input1Io))
                        {
                            SF.motion.GetInIO(input1Io, ref Open_1);
                            if (Open_1 != ConnectTemp[i].InPut1)
                            {
                                btnCon[i].InPut1.BackColor = Open_1 ? Color.Green : Color.Gray;
                                ConnectTemp[i].InPut1 = Open_1;
                            }
                        }
                        else
                        {
                            btnCon[i].InPut1.BackColor = Color.Red;
                            ConnectTemp[i].InPut1 = false;
                        }
                        if (TryResolveIoByName(IODebugMaps.iOConnects[i].Intput2.Name, "通用输入", out IO input2Io))
                        {
                            SF.motion.GetInIO(input2Io, ref Open_1);
                            if (Open_1 != ConnectTemp[i].InPut2)
                            {
                                btnCon[i].InPut2.BackColor = Open_1 ? Color.Green : Color.Gray;
                                ConnectTemp[i].InPut2 = Open_1;
                            }
                        }
                        else
                        {
                            btnCon[i].InPut2.BackColor = Color.Red;
                            ConnectTemp[i].InPut2 = false;
                        }
                    }
                }
            }
            catch
            {
                return;
            }
        }

        public void SetConnectItemm()
        {
            // 初始化右键菜单
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            listView3.ContextMenuStrip = contextMenu;
            contextMenu.Items.Clear();
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
                    ToolStripMenuItem dynamicMenuItem = new ToolStripMenuItem(cacheIO.Name);
                    dynamicMenuItem.Click += DynamicMenuItem_Click;
                    contextMenu.Items.Add(dynamicMenuItem);

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
            listView3.Columns.Add("通用输出", 220);
            List<IOConnect> IOConnects = IODebugMaps.iOConnects;
            IOConnect iOConnect = null;
            for (int j = 0; j < IOConnects.Count; j++)
            {
                iOConnect = IOConnects[j];
                string name = iOConnect.Output.Name;
                ListViewItem item = new ListViewItem(name);
                item.Text = name;
                item.Font = font;
                if (!TryResolveIoByName(name, "通用输出", out _))
                {
                    item.ForeColor = Color.Red;
                }
                if (iOConnect.Output2 != null
                    && !string.IsNullOrWhiteSpace(iOConnect.Output2.Name)
                    && !TryResolveIoByName(iOConnect.Output2.Name, "通用输出", out _))
                {
                    item.ForeColor = Color.Red;
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
            RefreshIODebugMapFrm();
            buttonsIn = CreateButtonIO(IODebugMaps.inputs, tabPage1);


        }
        public List<Button> CreateButtonIO(List<IO> iOs, TabPage tabPage)
        {
            List<Button> buttons = new List<Button>();
            tabPage.AutoScroll = true;
            int col = 0, row = 0;
            int colWidth = 110;
            int rowHeight = 40;
            for (int i = 0; i < iOs.Count; i++)
            {
                IO io = iOs[i];
                if (io == null)
                {
                    buttons.Add(null);
                }
                else if (io.IsRemark)
                {
                    Label remarkLabel = new Label();
                    remarkLabel.Text = io.Name;
                    remarkLabel.Location = new System.Drawing.Point(col * colWidth, row * rowHeight);
                    remarkLabel.Size = new System.Drawing.Size(100, 30);
                    remarkLabel.TextAlign = ContentAlignment.MiddleCenter;
                    remarkLabel.BackColor = Color.FromArgb(230, 232, 236);
                    remarkLabel.ForeColor = Color.FromArgb(64, 64, 64);
                    remarkLabel.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
                    tabPage.Controls.Add(remarkLabel);
                    buttons.Add(null);
                }
                else
                {
                    Button dynamicButton = new Button();

                    dynamicButton.Text = io.Name;
                    dynamicButton.Location = new System.Drawing.Point(col * colWidth, row * rowHeight);
                    dynamicButton.Size = new System.Drawing.Size(100, 30);

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
            for (int i = 0; i < IODebugMaps.iOConnects.Count; i++)
            {
                Button dynamicButton = new Button();

                dynamicButton.Text = IODebugMaps.iOConnects[i].Output.Name;
                dynamicButton.Location = new System.Drawing.Point(col * 110, row * 40);
                dynamicButton.Size = new System.Drawing.Size(100, 30);
                dynamicButton.Tag = IODebugMaps.iOConnects[i];

                tabPage3.Controls.Add(dynamicButton);

                dynamicButton.Click += new EventHandler(IOButton_Click);

                Label dynamicLabel1 = new Label();

                dynamicLabel1.Text = IODebugMaps.iOConnects[i].Intput1.Name;
                dynamicLabel1.Location = new System.Drawing.Point(col * 110+110, row * 40);
                dynamicLabel1.Size = new System.Drawing.Size(100, 30);
                dynamicLabel1.BackColor = System.Drawing.Color.Gray;
                dynamicLabel1.TextAlign = ContentAlignment.MiddleCenter;


                tabPage3.Controls.Add(dynamicLabel1);

                Label dynamicLabel2 = new Label();

                dynamicLabel2.Text = IODebugMaps.iOConnects[i].Intput2.Name;
                dynamicLabel2.Location = new System.Drawing.Point(col * 110 + 220, row * 40);
                dynamicLabel2.Size = new System.Drawing.Size(100, 30);
                dynamicLabel2.BackColor = System.Drawing.Color.Gray;
                dynamicLabel2.TextAlign = ContentAlignment.MiddleCenter;

                tabPage3.Controls.Add(dynamicLabel2);

                btnCon.Add(new ConnectButton { OutPut = dynamicButton ,InPut1 = dynamicLabel1 ,InPut2 = dynamicLabel2});

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
        public void RefreshIODebugMapFrm()
        {
            listView1.Clear();
            listView2.Clear();
            listView1.Columns.Add("通用输入", 220);
            listView2.Columns.Add("通用输出", 220);
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
                    if (!cacheIO.IsRemark && !TryResolveIoByName(name, "通用输入", out _))
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
                    if (!cacheIO.IsRemark && !TryResolveIoByName(name, "通用输出", out _))
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
                BuildIODebugMapFromIOMap();
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

            }
        }
        private void BuildIODebugMapFromIOMap()
        {
            IODebugMap result = new IODebugMap();
            List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();
            if (cacheIOs == null)
            {
                IODebugMaps = result;
                return;
            }
            foreach (IO io in cacheIOs)
            {
                if (io == null)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(io.Name))
                {
                    continue;
                }
                if (io.IOType == "通用输入")
                {
                    if (!result.inputs.Any(item => item != null && item.Name == io.Name))
                    {
                        result.inputs.Add(io.CloneForDebug());
                    }
                }
                else if (io.IOType == "通用输出")
                {
                    if (!result.outputs.Any(item => item != null && item.Name == io.Name))
                    {
                        result.outputs.Add(io.CloneForDebug());
                    }
                }
            }
            IODebugMaps = result;
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
        }
        private void EnsureOutputTempSize(int count)
        {
            if (OutTemp.Length < count)
            {
                Array.Resize(ref OutTemp, count);
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
        }
        private bool TryResolveIoByName(string name, string ioType, out IO io)
        {
            io = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            List<IO> cacheIOs = SF.frmIO.IOMap.FirstOrDefault();
            if (cacheIOs == null)
            {
                return false;
            }
            List<IO> matches = cacheIOs.Where(item => item != null && item.IOType == ioType && item.Name == name).ToList();
            if (matches.Count == 1)
            {
                io = matches[0];
                return true;
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

            switch (selectedIndex)
            {
                case 0:
                    tabPage1.Controls.Clear();
                    buttonsIn = CreateButtonIO(IODebugMaps.inputs, tabPage1);
                    EnsureInputTempSize(IODebugMaps.inputs.Count);
                    for (int i = 0; i < IODebugMaps.inputs.Count; i++)
                    {
                        if (i >= buttonsIn.Count)
                        {
                            continue;
                        }
                        IO ioItem = IODebugMaps.inputs[i];
                        if (ioItem == null || ioItem.IsRemark || buttonsIn[i] == null)
                        {
                            continue;
                        }
                        bool Open_1 = false;
                        if (TryResolveIoByName(ioItem.Name, "通用输入", out IO io))
                        {
                            SF.motion.GetInIO(io, ref Open_1);
                            buttonsIn[i].BackColor = Open_1 ? Color.Green : Color.Gray;
                            InTemp[i] = Open_1;
                        }
                        else
                        {
                            buttonsIn[i].BackColor = Color.Red;
                            InTemp[i] = false;
                        }
                    }
                    break;
                case 1:
                    tabPage2.Controls.Clear();
                    buttonsOut = CreateButtonIO(IODebugMaps.outputs, tabPage2);
                    EnsureOutputTempSize(IODebugMaps.outputs.Count);
                    for (int i = 0; i < IODebugMaps.outputs.Count; i++)
                    {
                        if (i >= buttonsOut.Count)
                        {
                            continue;
                        }
                        IO ioItem = IODebugMaps.outputs[i];
                        if (ioItem == null || ioItem.IsRemark || buttonsOut[i] == null)
                        {
                            continue;
                        }
                        bool Open_1 = false;
                        if (TryResolveIoByName(ioItem.Name, "通用输出", out IO io))
                        {
                            SF.motion.GetOutIO(io, ref Open_1);
                            buttonsOut[i].BackColor = Open_1 ? Color.Green : Color.Gray;
                            OutTemp[i] = Open_1;
                        }
                        else
                        {
                            buttonsOut[i].BackColor = Color.Red;
                            OutTemp[i] = false;
                        }
                    }
                    break;
                case 2:
                    tabPage3.Controls.Clear();
                    CreateButtonConnect();
                    EnsureConnectTempSize(IODebugMaps.iOConnects.Count);
                    for (int i = 0; i < IODebugMaps.iOConnects.Count; i++)
                    {
                        bool Open_1 = false;
                        if (TryResolveIoByName(IODebugMaps.iOConnects[i].Output.Name, "通用输出", out IO outputIo))
                        {
                            SF.motion.GetOutIO(outputIo, ref Open_1);
                            btnCon[i].OutPut.BackColor = Open_1 ? Color.Green : Color.Gray;
                            ConnectTemp[i].OutPut = Open_1;
                        }
                        else
                        {
                            btnCon[i].OutPut.BackColor = Color.Red;
                            ConnectTemp[i].OutPut = false;
                        }

                        if (TryResolveIoByName(IODebugMaps.iOConnects[i].Intput1.Name, "通用输入", out IO input1Io))
                        {
                            SF.motion.GetInIO(input1Io, ref Open_1);
                            btnCon[i].InPut1.BackColor = Open_1 ? Color.Green : Color.Gray;
                            ConnectTemp[i].InPut1 = Open_1;
                        }
                        else
                        {
                            btnCon[i].InPut1.BackColor = Color.Red;
                            ConnectTemp[i].InPut1 = false;
                        }

                        if (TryResolveIoByName(IODebugMaps.iOConnects[i].Intput2.Name, "通用输入", out IO input2Io))
                        {
                            SF.motion.GetInIO(input2Io, ref Open_1);
                            btnCon[i].InPut2.BackColor = Open_1 ? Color.Green : Color.Gray;
                            ConnectTemp[i].InPut2 = Open_1;
                        }
                        else
                        {
                            btnCon[i].InPut2.BackColor = Color.Red;
                            ConnectTemp[i].InPut2 = false;
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
                    IOConnect iOConnect = IODebugMaps.iOConnects.FirstOrDefault(con => con.Output.Name == listView3.SelectedItems[0].Text);
                    if (cacheIO != null)
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
                    IOConnect iOConnect = IODebugMaps.iOConnects.FirstOrDefault(con => con.Output.Name == listView3.SelectedItems[0].Text);
                    iOConnect.Intput1.Name = "";

                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
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
                    IOConnect iOConnect = IODebugMaps.iOConnects.FirstOrDefault(con => con.Output.Name == listView3.SelectedItems[0].Text);
                    if (cacheIO != null)
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
                    IOConnect iOConnect = IODebugMaps.iOConnects.FirstOrDefault(con => con.Output.Name == listView3.SelectedItems[0].Text);

                    iOConnect.Intput2.Name = "";

                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);

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
                    IOConnect iOConnect = IODebugMaps.iOConnects.FirstOrDefault(con => con.Output.Name == listView3.SelectedItems[0].Text);
                    if (cacheIO != null && iOConnect != null)
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
                    IOConnect iOConnect = IODebugMaps.iOConnects.FirstOrDefault(con => con.Output.Name == listView3.SelectedItems[0].Text);
                    if (iOConnect != null)
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
                string selectedText = listView3.SelectedItems[0].Text;
                IOConnect iOConnect = IODebugMaps.iOConnects.FirstOrDefault(con => con.Output.Name == selectedText);
                if (iOConnect == null)
                {
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
