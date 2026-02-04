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
        public List<Control> buttonsIn = new List<Control>();
        public List<Control> buttonsOut = new List<Control>();
        public List<ConnectButton> btnCon = new List<ConnectButton>();
        private ListView listView6;
        private readonly ListView[] connectListView3 = new ListView[3];
        private readonly ListView[] connectListView4 = new ListView[3];
        private readonly ListView[] connectListView5 = new ListView[3];
        private readonly ListView[] connectListView6 = new ListView[3];
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
        private const int IoColWidth = 160;
        private const int IoRowHeight = 46;
        private const int IoItemWidth = 150;
        private const int IoItemHeight = 34;
        private int inputRowsPerColumn = -1;
        private int outputRowsPerColumn = -1;
        private int connectRowsPerColumn = -1;
        private bool connectConfigResetNotified = false;
        private TabPage connectPage1;
        private TabPage connectPage2;
        private TabPage connectPage3;
        private TabPage connectConfigPage1;
        private TabControl connectConfigTabControl;
        private TabPage connectConfigTabPage1;
        private TabPage connectConfigTabPage2;
        private TabPage connectConfigTabPage3;
        private int currentConnectDisplayIndex = 0;
        private int currentConnectConfigIndex = 0;

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
            InitializeConnectConfigTabs();
            InitializeConnectPages();
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
            RelayoutIoButtonsIfNeeded();
        }

        private void FrmIODebug_Resize(object sender, EventArgs e)
        {
            UpdateRefreshEnabled();
            RelayoutIoButtonsIfNeeded();
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

        private void InitializeConnectConfigTabs()
        {
            connectConfigTabControl = new TabControl();
            connectConfigTabControl.Name = "connectConfigTabControl";
            connectConfigTabControl.Dock = DockStyle.Fill;

            connectConfigTabPage1 = new TabPage("关联配置1");
            connectConfigTabPage2 = new TabPage("关联配置2");
            connectConfigTabPage3 = new TabPage("关联配置3");
            connectConfigTabPage1.UseVisualStyleBackColor = true;
            connectConfigTabPage2.UseVisualStyleBackColor = true;
            connectConfigTabPage3.UseVisualStyleBackColor = true;

            connectConfigTabControl.Controls.Add(connectConfigTabPage1);
            connectConfigTabControl.Controls.Add(connectConfigTabPage2);
            connectConfigTabControl.Controls.Add(connectConfigTabPage3);
            connectConfigTabControl.SelectedIndex = 0;

            tabPage4.Controls.Add(connectConfigTabControl);
            tabPage4.Controls.SetChildIndex(connectConfigTabControl, 0);
            connectConfigTabControl.BringToFront();

            InitializeConnectConfigListViews();
            MoveConnectConfigViewsTo(connectConfigTabControl.SelectedTab ?? connectConfigTabPage1);
            currentConnectConfigIndex = connectConfigTabControl.SelectedIndex;
            connectConfigTabControl.SelectedIndexChanged += ConnectConfigTabControl_SelectedIndexChanged;
            connectConfigTabControl.Resize += ConnectConfigTabControl_Resize;
        }

        private void InitializeConnectConfigListViews()
        {
            connectListView3[0] = listView3;
            connectListView4[0] = listView4;
            connectListView5[0] = listView5;
            connectListView6[0] = CreateConnectOutput2ListView("listView6");
            ApplyConnectListViewLayout(connectConfigTabPage1, connectListView3[0], connectListView4[0], connectListView5[0], connectListView6[0]);

            connectListView3[1] = CreateConnectOutputListView("listView3_2");
            connectListView4[1] = CreateConnectInputListView("listView4_2");
            connectListView5[1] = CreateConnectInputListView("listView5_2");
            connectListView6[1] = CreateConnectOutput2ListView("listView6_2");
            ApplyConnectListViewLayout(connectConfigTabPage2, connectListView3[1], connectListView4[1], connectListView5[1], connectListView6[1]);

            connectListView3[2] = CreateConnectOutputListView("listView3_3");
            connectListView4[2] = CreateConnectInputListView("listView4_3");
            connectListView5[2] = CreateConnectInputListView("listView5_3");
            connectListView6[2] = CreateConnectOutput2ListView("listView6_3");
            ApplyConnectListViewLayout(connectConfigTabPage3, connectListView3[2], connectListView4[2], connectListView5[2], connectListView6[2]);
        }

        private ListView CreateConnectOutputListView(string name)
        {
            ListView listView = new ListView();
            listView.AllowDrop = true;
            listView.BackColor = Color.White;
            listView.HideSelection = false;
            listView.Name = name;
            listView.UseCompatibleStateImageBehavior = false;
            listView.View = View.Details;
            listView.ItemDrag += new ItemDragEventHandler(this.listView3_ItemDrag);
            listView.SelectedIndexChanged += new EventHandler(this.listView3_SelectedIndexChanged);
            listView.DragDrop += new DragEventHandler(this.listView3_DragDrop);
            listView.DragEnter += new DragEventHandler(this.listView3_DragEnter);
            listView.DragOver += new DragEventHandler(this.listView3_DragOver);
            return listView;
        }

        private ListView CreateConnectInputListView(string name)
        {
            ListView listView = new ListView();
            listView.AllowDrop = true;
            listView.BackColor = Color.White;
            listView.CheckBoxes = true;
            listView.HideSelection = false;
            listView.Name = name;
            listView.UseCompatibleStateImageBehavior = false;
            listView.View = View.Details;
            return listView;
        }

        private ListView CreateConnectOutput2ListView(string name)
        {
            ListView listView = new ListView();
            listView.AllowDrop = true;
            listView.BackColor = Color.White;
            listView.CheckBoxes = true;
            listView.HideSelection = false;
            listView.Name = name;
            listView.UseCompatibleStateImageBehavior = false;
            listView.View = View.Details;
            listView.ItemChecked += listView6_ItemChecked;
            return listView;
        }

        private void ApplyConnectListViewLayout(TabPage page, ListView output1, ListView input1, ListView input2, ListView output2)
        {
            if (page == null || output1 == null || input1 == null || input2 == null || output2 == null)
            {
                return;
            }
            page.SuspendLayout();
            try
            {
                if (output1.Parent != page)
                {
                    page.Controls.Add(output1);
                }
                if (input1.Parent != page)
                {
                    page.Controls.Add(input1);
                }
                if (input2.Parent != page)
                {
                    page.Controls.Add(input2);
                }
                if (output2.Parent != page)
                {
                    page.Controls.Add(output2);
                }

                output1.Dock = DockStyle.Left;
                output2.Dock = DockStyle.Left;
                input1.Dock = DockStyle.Left;
                input2.Dock = DockStyle.Left;

                page.Controls.SetChildIndex(output1, 0);
                page.Controls.SetChildIndex(output2, 1);
                page.Controls.SetChildIndex(input1, 2);
                page.Controls.SetChildIndex(input2, 3);
                UpdateConnectConfigColumnWidths(page, output1, output2, input1, input2);
            }
            finally
            {
                page.ResumeLayout();
            }
        }

        private void ConnectConfigTabControl_Resize(object sender, EventArgs e)
        {
            UpdateConnectConfigColumnWidths();
        }

        private void UpdateConnectConfigColumnWidths()
        {
            UpdateConnectConfigColumnWidths(connectConfigTabPage1, connectListView3[0], connectListView6[0], connectListView4[0], connectListView5[0]);
            UpdateConnectConfigColumnWidths(connectConfigTabPage2, connectListView3[1], connectListView6[1], connectListView4[1], connectListView5[1]);
            UpdateConnectConfigColumnWidths(connectConfigTabPage3, connectListView3[2], connectListView6[2], connectListView4[2], connectListView5[2]);
        }

        private void UpdateConnectConfigColumnWidths(TabPage page, ListView output1, ListView output2, ListView input1, ListView input2)
        {
            if (page == null || output1 == null || output2 == null || input1 == null || input2 == null)
            {
                return;
            }
            int width = page.ClientSize.Width;
            if (width <= 0)
            {
                return;
            }
            int colWidth = width / 4;
            if (colWidth <= 0)
            {
                return;
            }
            int lastWidth = width - (colWidth * 3);
            if (lastWidth <= 0)
            {
                lastWidth = colWidth;
            }
            output1.Width = colWidth;
            output2.Width = colWidth;
            input1.Width = colWidth;
            input2.Width = lastWidth;
        }

        private void InitializeConnectPages()
        {
            connectPage1 = tabPage3;
            connectPage2 = tabPage5;
            connectPage3 = tabPage6;
            connectConfigPage1 = tabPage4;

            if (connectPage1 != null && string.IsNullOrWhiteSpace(connectPage1.Text))
            {
                connectPage1.Text = "输入输出关联1";
            }
            if (connectPage2 != null && string.IsNullOrWhiteSpace(connectPage2.Text))
            {
                connectPage2.Text = "输入输出关联2";
            }
            if (connectPage3 != null && string.IsNullOrWhiteSpace(connectPage3.Text))
            {
                connectPage3.Text = "输入输出关联3";
            }
            if (connectConfigPage1 != null && string.IsNullOrWhiteSpace(connectConfigPage1.Text))
            {
                connectConfigPage1.Text = "关联配置1";
            }
            currentConnectDisplayIndex = 0;
        }

        private TabPage GetCurrentConnectDisplayPage()
        {
            if (currentConnectDisplayIndex == 1 && connectPage2 != null)
            {
                return connectPage2;
            }
            if (currentConnectDisplayIndex == 2 && connectPage3 != null)
            {
                return connectPage3;
            }
            return connectPage1 ?? tabPage3;
        }

        private void ConnectConfigTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            RunConnectConfigLayoutUpdate(() =>
            {
                currentConnectConfigIndex = connectConfigTabControl.SelectedIndex;
                MoveConnectConfigViewsTo(connectConfigTabControl.SelectedTab);
                BeginConnectListViewUpdate();
                try
                {
                    listView4.ItemChecked -= listView4_ItemChecked;
                    listView5.ItemChecked -= listView5_ItemChecked;
                    listView6.ItemChecked -= listView6_ItemChecked;
                    SetConnectItemm();
                    RefleshConnecdt();
                    listView4.ItemChecked += listView4_ItemChecked;
                    listView5.ItemChecked += listView5_ItemChecked;
                    listView6.ItemChecked += listView6_ItemChecked;
                    RefreshConnectDisplayForCurrentConfig();
                }
                finally
                {
                    EndConnectListViewUpdate();
                }
            });
        }

        private void MoveConnectConfigViewsTo(TabPage targetPage)
        {
            if (targetPage == null)
            {
                return;
            }
            int index = 0;
            if (targetPage == connectConfigTabPage2)
            {
                index = 1;
            }
            else if (targetPage == connectConfigTabPage3)
            {
                index = 2;
            }
            listView3 = connectListView3[index];
            listView4 = connectListView4[index];
            listView5 = connectListView5[index];
            listView6 = connectListView6[index];
        }

        private void RunConnectConfigLayoutUpdate(Action updateAction)
        {
            TabPage targetPage = connectConfigTabControl?.SelectedTab;
            connectConfigTabControl?.SuspendLayout();
            targetPage?.SuspendLayout();
            try
            {
                updateAction?.Invoke();
            }
            finally
            {
                targetPage?.ResumeLayout();
                connectConfigTabControl?.ResumeLayout();
            }
        }

        private void BeginConnectListViewUpdate()
        {
            listView3?.BeginUpdate();
            listView4?.BeginUpdate();
            listView5?.BeginUpdate();
            listView6?.BeginUpdate();
        }

        private void EndConnectListViewUpdate()
        {
            listView3?.EndUpdate();
            listView4?.EndUpdate();
            listView5?.EndUpdate();
            listView6?.EndUpdate();
        }

        private void EnsureConnectConfigReady()
        {
            if (IODebugMaps == null)
            {
                IODebugMaps = new IODebugMap();
                return;
            }
            if (IODebugMaps.iOConnects == null || IODebugMaps.iOConnects2 == null || IODebugMaps.iOConnects3 == null)
            {
                if (!connectConfigResetNotified)
                {
                    connectConfigResetNotified = true;
                    if (IsHandleCreated && !IsDisposed)
                    {
                        if (InvokeRequired)
                        {
                            BeginInvoke(new Action(() => MessageBox.Show("输入输出关联配置版本不匹配，已重置，请重新配置。")));
                        }
                        else
                        {
                            MessageBox.Show("输入输出关联配置版本不匹配，已重置，请重新配置。");
                        }
                    }
                }
                IODebugMaps = new IODebugMap();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            }
        }

        private List<IOConnect> GetConnectList(int pageIndex)
        {
            EnsureConnectConfigReady();
            switch (pageIndex)
            {
                case 1:
                    return IODebugMaps.iOConnects2;
                case 2:
                    return IODebugMaps.iOConnects3;
                default:
                    return IODebugMaps.iOConnects;
            }
        }

        private void SetConnectList(int pageIndex, List<IOConnect> list)
        {
            EnsureConnectConfigReady();
            switch (pageIndex)
            {
                case 1:
                    IODebugMaps.iOConnects2 = list;
                    break;
                case 2:
                    IODebugMaps.iOConnects3 = list;
                    break;
                default:
                    IODebugMaps.iOConnects = list;
                    break;
            }
        }

        private List<IOConnect> GetConnectListForDisplay()
        {
            EnsureConnectConfigReady();
            return GetConnectList(currentConnectDisplayIndex);
        }

        private List<IOConnect> GetConnectListForConfig()
        {
            EnsureConnectConfigReady();
            return GetConnectList(currentConnectConfigIndex);
        }

        private void RefreshConnectDisplayForCurrentConfig()
        {
            if (connectPage1 == null)
            {
                return;
            }
            if (currentConnectDisplayIndex != currentConnectConfigIndex)
            {
                return;
            }
            RefreshCurrentConnectDisplayPage();
        }

        private void RefreshCurrentConnectDisplayPage()
        {
            TabPage connectPage = GetCurrentConnectDisplayPage();
            connectPage.Controls.Clear();
            btnCon.Clear();
            connectRowsPerColumn = -1;
            Array.Clear(ConnectOutValid, 0, ConnectOutValid.Length);
            Array.Clear(ConnectIn1Valid, 0, ConnectIn1Valid.Length);
            Array.Clear(ConnectIn2Valid, 0, ConnectIn2Valid.Length);
            IoRefreshData data = BuildIoRefreshData(2);
            if (data != null)
            {
                ApplyIoRefresh(data);
            }
        }

        private int GetRowsPerColumn(TabPage tabPage, int rowHeight)
        {
            int height = tabPage.ClientSize.Height;
            if (height <= 0 || rowHeight <= 0)
            {
                return 1;
            }
            int rows = height / rowHeight;
            return Math.Max(1, rows);
        }

        private void RelayoutIoButtonsIfNeeded()
        {
            if (!CheckFormIsOpen(this))
            {
                return;
            }
            int newInputRows = GetRowsPerColumn(tabPage1, IoRowHeight);
            if (inputRowsPerColumn != newInputRows && IODebugMaps?.inputs != null)
            {
                tabPage1.Controls.Clear();
                buttonsIn = CreateButtonIO(IODebugMaps.inputs, tabPage1);
                EnsureInputTempSize(IODebugMaps.inputs.Count);
                Array.Clear(InTemp, 0, IODebugMaps.inputs.Count);
                Array.Clear(InValid, 0, IODebugMaps.inputs.Count);
                IoRefreshData data = BuildIoRefreshData(0);
                if (data != null)
                {
                    ApplyIoRefresh(data);
                }
            }

            int newOutputRows = GetRowsPerColumn(tabPage2, IoRowHeight);
            if (outputRowsPerColumn != newOutputRows && IODebugMaps?.outputs != null)
            {
                tabPage2.Controls.Clear();
                buttonsOut = CreateButtonIO(IODebugMaps.outputs, tabPage2);
                EnsureOutputTempSize(IODebugMaps.outputs.Count);
                Array.Clear(OutTemp, 0, IODebugMaps.outputs.Count);
                Array.Clear(OutValid, 0, IODebugMaps.outputs.Count);
                IoRefreshData data = BuildIoRefreshData(1);
                if (data != null)
                {
                    ApplyIoRefresh(data);
                }
            }

            TabPage connectPage = GetCurrentConnectDisplayPage();
            List<IOConnect> connectList = GetConnectListForDisplay();
            int newConnectRows = GetRowsPerColumn(connectPage, IoRowHeight);
            if (connectRowsPerColumn != newConnectRows && connectList != null)
            {
                connectPage.Controls.Clear();
                CreateButtonConnect(connectList, connectPage);
                EnsureConnectTempSize(connectList.Count);
                Array.Clear(ConnectOutValid, 0, connectList.Count);
                Array.Clear(ConnectIn1Valid, 0, connectList.Count);
                Array.Clear(ConnectIn2Valid, 0, connectList.Count);
                IoRefreshData data = BuildIoRefreshData(2);
                if (data != null)
                {
                    ApplyIoRefresh(data);
                }
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
                if (SF.motion == null || !SF.motion.IsCardInitialized)
                {
                    if (tabIndex == 0)
                    {
                        IO[] inputs = IODebugMaps.inputs.ToArray();
                        bool[] valid = new bool[inputs.Length];
                        for (int i = 0; i < valid.Length; i++)
                        {
                            valid[i] = true;
                        }
                        return new IoRefreshData
                        {
                            TabIndex = 0,
                            InputCount = inputs.Length,
                            InputStates = new bool[inputs.Length],
                            InputValid = valid
                        };
                    }
                    if (tabIndex == 1)
                    {
                        IO[] outputs = IODebugMaps.outputs.ToArray();
                        bool[] valid = new bool[outputs.Length];
                        for (int i = 0; i < valid.Length; i++)
                        {
                            valid[i] = true;
                        }
                        return new IoRefreshData
                        {
                            TabIndex = 1,
                            OutputCount = outputs.Length,
                            OutputStates = new bool[outputs.Length],
                            OutputValid = valid
                        };
                    }
                    if (tabIndex == 2)
                    {
                        List<IOConnect> connectList = GetConnectListForDisplay();
                        IOConnect[] connects = connectList.ToArray();
                        bool[] outValid = new bool[connects.Length];
                        bool[] in1Valid = new bool[connects.Length];
                        bool[] in2Valid = new bool[connects.Length];
                        for (int i = 0; i < connects.Length; i++)
                        {
                            outValid[i] = true;
                            in1Valid[i] = true;
                            in2Valid[i] = true;
                        }
                        return new IoRefreshData
                        {
                            TabIndex = 2,
                            ConnectCount = connects.Length,
                            ConnectOutStates = new bool[connects.Length],
                            ConnectOutValid = outValid,
                            ConnectIn1States = new bool[connects.Length],
                            ConnectIn1Valid = in1Valid,
                            ConnectIn2States = new bool[connects.Length],
                            ConnectIn2Valid = in2Valid
                        };
                    }
                }
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
                    List<IOConnect> connectList = GetConnectListForDisplay();
                    IOConnect[] connects = connectList.ToArray();
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
                List<IOConnect> connectList = GetConnectListForDisplay();
                TabPage connectPage = GetCurrentConnectDisplayPage();
                if (btnCon.Count != connectList.Count || data.ConnectCount != connectList.Count)
                {
                    connectPage.Controls.Clear();
                    CreateButtonConnect(connectList, connectPage);
                    EnsureConnectTempSize(connectList.Count);
                    return;
                }
                EnsureConnectTempSize(data.ConnectCount);
                for (int i = 0; i < data.ConnectCount; i++)
                {
                    IOConnect connect = connectList[i];
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
            List<IOConnect> connectList = GetConnectListForConfig();
            if (connectList.Any(item => item != null && item.Output != null && item.Output.Name == cacheIO.Name))
            {
                MessageBox.Show("调试列表已存在同名输出连接，已沿用现有配置。");
                return;
            }
            connectList.Add(new IOConnect() { Output = cacheIO.CloneForDebug() });
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefreshIODebugMapFrm();
            RefleshConnecdt();
            RefreshConnectDisplayForCurrentConfig();

        }
        public void RefleshConnecdt()
        {
            listView3.Clear();
            listView3.Columns.Add("通用输出1", 220);
            UpdateIoCacheIfNeeded();
            List<IOConnect> IOConnects = GetConnectListForConfig();
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
            IoRefreshData data = BuildIoRefreshData(0);
            if (data != null)
            {
                ApplyIoRefresh(data);
            }
            data = BuildIoRefreshData(1);
            if (data != null)
            {
                ApplyIoRefresh(data);
            }
            data = BuildIoRefreshData(2);
            if (data != null)
            {
                ApplyIoRefresh(data);
            }
            if (SF.frmIO?.IOMap?.FirstOrDefault() != null)
            {
                if (listView6 == null)
                {
                    MessageBox.Show("输入输出关联配置初始化失败，无法加载关联配置。");
                    return;
                }
                listView4.ItemChecked -= listView4_ItemChecked;
                listView5.ItemChecked -= listView5_ItemChecked;
                listView6.ItemChecked -= listView6_ItemChecked;
                SetConnectItemm();
                RefleshConnecdt();
                listView4.ItemChecked += listView4_ItemChecked;
                listView5.ItemChecked += listView5_ItemChecked;
                listView6.ItemChecked += listView6_ItemChecked;
            }
        }
        public List<Control> CreateButtonIO(List<IO> iOs, TabPage tabPage)
        {
            List<Control> controls = new List<Control>();
            tabPage.AutoScroll = true;
            int col = 0, row = 0;
            int colWidth = IoColWidth;
            int rowHeight = IoRowHeight;
            int itemWidth = IoItemWidth;
            int itemHeight = IoItemHeight;
            int rowsPerColumn = GetRowsPerColumn(tabPage, rowHeight);
            bool isInputPage = tabPage == tabPage1;
            if (tabPage == tabPage1)
            {
                inputRowsPerColumn = rowsPerColumn;
            }
            else if (tabPage == tabPage2)
            {
                outputRowsPerColumn = rowsPerColumn;
            }
            for (int i = 0; i < iOs.Count; i++)
            {
                IO io = iOs[i];
                if (io == null)
                {
                    controls.Add(null);
                }
                else if (io.IsRemark)
                {
                    Control remarkHeader = CreateRemarkHeader(io.Name, new Point(col * colWidth, row * rowHeight), itemWidth, itemHeight);
                    tabPage.Controls.Add(remarkHeader);
                    controls.Add(null);
                }
                else
                {
                    if (isInputPage || io.IOType == "通用输入")
                    {
                        Label dynamicLabel = new Label();
                        dynamicLabel.Text = io.Name;
                        dynamicLabel.Location = new System.Drawing.Point(col * colWidth, row * rowHeight);
                        dynamicLabel.Size = new System.Drawing.Size(itemWidth, itemHeight);
                        dynamicLabel.AutoSize = false;
                        dynamicLabel.BackColor = System.Drawing.Color.Gray;
                        dynamicLabel.TextAlign = ContentAlignment.MiddleCenter;
                        tabPage.Controls.Add(dynamicLabel);
                        controls.Add(dynamicLabel);
                    }
                    else
                    {
                        Button dynamicButton = new Button();
                        dynamicButton.Text = io.Name;
                        dynamicButton.Location = new System.Drawing.Point(col * colWidth, row * rowHeight);
                        dynamicButton.Size = new System.Drawing.Size(itemWidth, itemHeight);
                        tabPage.Controls.Add(dynamicButton);
                        controls.Add(dynamicButton);
                        if (io.IOType == "通用输出")
                        {
                            dynamicButton.Click += new EventHandler(IOButton_Click);
                        }
                    }
                }
                row++;
                if (row >= rowsPerColumn)
                {
                    row = 0;
                    col++;
                }
            }
            return controls;

        }
        public void CreateButtonConnect(List<IOConnect> connects, TabPage targetPage)
        {
            int col = 0, row = 0;
            btnCon.Clear();
            targetPage.AutoScroll = true;
            int colWidth = IoColWidth;
            int rowHeight = IoRowHeight;
            int itemWidth = IoItemWidth;
            int itemHeight = IoItemHeight;
            int groupWidth = colWidth * 3;
            int rowsPerColumn = GetRowsPerColumn(targetPage, rowHeight);
            connectRowsPerColumn = rowsPerColumn;
            for (int i = 0; i < connects.Count; i++)
            {
                IOConnect ioConnect = connects[i];
                if (ioConnect?.Output == null)
                {
                    btnCon.Add(new ConnectButton());
                    row++;
                    if (row >= rowsPerColumn)
                    {
                        row = 0;
                        col++;
                    }
                    continue;
                }
                Control outputControl;
                if (!ioConnect.Output.IsRemark)
                {
                    Button dynamicButton = new Button();
                    dynamicButton.Text = ioConnect.Output.Name;
                    int baseX = col * groupWidth;
                    dynamicButton.Location = new System.Drawing.Point(baseX, row * rowHeight);
                    dynamicButton.Size = new System.Drawing.Size(itemWidth, itemHeight);
                    dynamicButton.Tag = ioConnect;
                    dynamicButton.Click += new EventHandler(IOButton_Click);
                    targetPage.Controls.Add(dynamicButton);
                    outputControl = dynamicButton;
                }
                else
                {
                    int remarkWidth = groupWidth - 10;
                    Control remarkHeader = CreateRemarkHeader(ioConnect.Output.Name, new Point(col * groupWidth, row * rowHeight), remarkWidth, itemHeight);
                    targetPage.Controls.Add(remarkHeader);
                    outputControl = remarkHeader;
                }

                Label dynamicLabel1 = null;
                Label dynamicLabel2 = null;
                if (!ioConnect.Output.IsRemark)
                {
                    if (ioConnect.Intput1 != null && !string.IsNullOrWhiteSpace(ioConnect.Intput1.Name))
                    {
                        dynamicLabel1 = new Label();
                        dynamicLabel1.Text = ioConnect.Intput1.Name;
                        int baseX = col * groupWidth;
                        dynamicLabel1.Location = new System.Drawing.Point(baseX + colWidth, row * rowHeight);
                        dynamicLabel1.Size = new System.Drawing.Size(itemWidth, itemHeight);
                        dynamicLabel1.BackColor = System.Drawing.Color.Gray;
                        dynamicLabel1.TextAlign = ContentAlignment.MiddleCenter;

                        targetPage.Controls.Add(dynamicLabel1);
                    }

                    if (ioConnect.Intput2 != null && !string.IsNullOrWhiteSpace(ioConnect.Intput2.Name))
                    {
                        dynamicLabel2 = new Label();
                        dynamicLabel2.Text = ioConnect.Intput2.Name;
                        int baseX = col * groupWidth;
                        dynamicLabel2.Location = new System.Drawing.Point(baseX + colWidth * 2, row * rowHeight);
                        dynamicLabel2.Size = new System.Drawing.Size(itemWidth, itemHeight);
                        dynamicLabel2.BackColor = System.Drawing.Color.Gray;
                        dynamicLabel2.TextAlign = ContentAlignment.MiddleCenter;

                        targetPage.Controls.Add(dynamicLabel2);
                    }
                }

                btnCon.Add(new ConnectButton { OutPut = outputControl, InPut1 = dynamicLabel1, InPut2 = dynamicLabel2 });

                row++;
                if (row >= rowsPerColumn)
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
                EnsureConnectConfigReady();
            }
            catch (Exception ex)
            {
                //Console.WriteLine(ex.Message);
                IODebugMaps = new IODebugMap();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", IODebugMaps);
                EnsureConnectConfigReady();
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
            List<IOConnect> connectList = GetConnectListForConfig();
            foreach (IOConnect connect in connectList)
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
            List<IOConnect> connectList = GetConnectListForConfig();
            List<IOConnect> newList = new List<IOConnect>();
            foreach (IOConnect connect in connectList)
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
            SetConnectList(currentConnectConfigIndex, newList);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefleshConnecdt();
            RefreshConnectDisplayForCurrentConfig();
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
            List<IOConnect> connectList = GetConnectListForConfig();
            int insertIndex = connectList.Count;
            if (listView3.SelectedItems.Count > 0)
            {
                insertIndex = listView3.SelectedItems[0].Index + 1;
                if (insertIndex < 0 || insertIndex > connectList.Count)
                {
                    insertIndex = connectList.Count;
                }
            }
            connectList.Insert(insertIndex, remark);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefleshConnecdt();
            RefreshConnectDisplayForCurrentConfig();
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
            TabPage selectedTab = tabControl1.SelectedTab;
            if (selectedTab == tabPage1)
            {
                currentTabIndex = 0;
                IoRefreshData data = BuildIoRefreshData(0);
                if (data != null)
                {
                    ApplyIoRefresh(data);
                }
                return;
            }
            if (selectedTab == tabPage2)
            {
                currentTabIndex = 1;
                IoRefreshData data = BuildIoRefreshData(1);
                if (data != null)
                {
                    ApplyIoRefresh(data);
                }
                return;
            }
            if (selectedTab == connectPage1 || selectedTab == connectPage2 || selectedTab == connectPage3)
            {
                currentTabIndex = 2;
                if (selectedTab == connectPage2)
                {
                    currentConnectDisplayIndex = 1;
                }
                else if (selectedTab == connectPage3)
                {
                    currentConnectDisplayIndex = 2;
                }
                else
                {
                    currentConnectDisplayIndex = 0;
                }
                RefreshCurrentConnectDisplayPage();
                return;
            }
            if (selectedTab == connectConfigPage1)
            {
                currentTabIndex = 3;
                RunConnectConfigLayoutUpdate(() =>
                {
                    currentConnectConfigIndex = connectConfigTabControl.SelectedIndex;
                    MoveConnectConfigViewsTo(connectConfigTabControl.SelectedTab);
                    BeginConnectListViewUpdate();
                    try
                    {
                        listView4.ItemChecked -= listView4_ItemChecked;
                        listView5.ItemChecked -= listView5_ItemChecked;
                        listView6.ItemChecked -= listView6_ItemChecked;
                        SetConnectItemm();
                        RefreshIODebugMapFrm();
                        RefleshConnecdt();
                        listView4.ItemChecked += listView4_ItemChecked;
                        listView5.ItemChecked += listView5_ItemChecked;
                        listView6.ItemChecked += listView6_ItemChecked;
                        RefreshConnectDisplayForCurrentConfig();
                    }
                    finally
                    {
                        EndConnectListViewUpdate();
                    }
                });
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
                        RefreshConnectDisplayForCurrentConfig();
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
                        RefreshConnectDisplayForCurrentConfig();
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
                        RefreshConnectDisplayForCurrentConfig();
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
                        RefreshConnectDisplayForCurrentConfig();
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
                        RefreshConnectDisplayForCurrentConfig();
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
                        RefreshConnectDisplayForCurrentConfig();
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

            List<IOConnect> connectList = GetConnectListForConfig();
            IOConnect temp = connectList[sourceIndex];
            connectList[sourceIndex] = connectList[targetIndex];
            connectList[targetIndex] = temp;

            // 取消目标项的高亮显示
            if (targetItem != null)
            {
                targetItem.BackColor = Color.White;
            }
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
            RefleshConnecdt();
            RefreshConnectDisplayForCurrentConfig();
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
        public List<IOConnect> iOConnects2 = new List<IOConnect>();
        public List<IOConnect> iOConnects3 = new List<IOConnect>();
    }
    public class IOConnect
    {
        public IO Output = new IO();
        public IO Output2 = new IO();
        public IO Intput1 = new IO();
        public IO Intput2 = new IO();
    }
}
