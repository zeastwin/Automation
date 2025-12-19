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
            Task.Run(() =>
            {
                while (true)
                {
                    if (CheckFormIsOpen(SF.frmIODebug)&&SF.isModify != 5)
                    {
                        Invoke(new Action(() =>
                        {
                            try
                            {
                                if (tabControl1.SelectedIndex == 0)
                                {
                                    for (int i = 0; i < IODebugMaps.inputs.Count; i++)
                                    {
                                        bool Open_1 = false;
                                        SF.motion.GetInIO(IODebugMaps.inputs[i], ref Open_1);
                                        if (Open_1 != InTemp[i])
                                        {
                                            buttonsIn[i].BackColor = Open_1 ? Color.Green : Color.Gray;
                                            InTemp[i] = Open_1;
                                        }

                                    }
                                }
                                else if (tabControl1.SelectedIndex == 1)
                                {
                                    for (int i = 0; i < IODebugMaps.outputs.Count; i++)
                                    {
                                        bool Open_1 = false;
                                        SF.motion.GetOutIO(IODebugMaps.outputs[i], ref Open_1);
                                        if (Open_1 != OutTemp[i])
                                        {
                                            buttonsOut[i].BackColor = Open_1 ? Color.Green : Color.Gray;
                                            OutTemp[i] = Open_1;
                                        }

                                    }
                                }
                                else if (tabControl1.SelectedIndex == 2)
                                {
                                    for (int i = 0; i < IODebugMaps.iOConnects.Count; i++)
                                    {
                                        bool Open_1 = false;
                                        SF.motion.GetOutIO(IODebugMaps.iOConnects[i].Output, ref Open_1);
                                        if (Open_1 != ConnectTemp[i].OutPut)
                                        {
                                            btnCon[i].OutPut.BackColor = Open_1 ? Color.Green : Color.Gray;
                                            ConnectTemp[i].OutPut = Open_1;
                                        }
                                        SF.motion.GetInIO(IODebugMaps.iOConnects[i].Intput1, ref Open_1);
                                        if (Open_1 != ConnectTemp[i].InPut1)
                                        {
                                            btnCon[i].InPut1.BackColor = Open_1 ? Color.Green : Color.Gray;
                                            ConnectTemp[i].InPut1 = Open_1;
                                        }
                                        SF.motion.GetInIO(IODebugMaps.iOConnects[i].Intput2, ref Open_1);
                                        if (Open_1 != ConnectTemp[i].InPut2)
                                        {
                                            btnCon[i].InPut2.BackColor = Open_1 ? Color.Green : Color.Gray;
                                            ConnectTemp[i].InPut2 = Open_1;
                                        }

                                    }
                                }
                            }
                            catch(Exception ex)
                            {
                                MessageBox.Show(ex.Message);
                            }
                          

                        }));
                    }

                    SF.Delay(200);
                }

            });
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
            listView4.Columns.Add("通用输入", 220);
            listView5.Columns.Add("通用输入", 220);


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
            IODebugMaps.iOConnects.Add(new IOConnect() { Output = cacheIO });
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
        public List<Button> CreateButtonIO(List<IO> iOs,TabPage tabPage)
        {
            List<Button> buttons = new List<Button>();
            int col = 0, row = 0;
            for (int i = 0; i < iOs.Count; i++)
            {
                Button dynamicButton = new Button();

                dynamicButton.Text = iOs[i].Name;
                dynamicButton.Location = new System.Drawing.Point(col * 110, row * 40);
                dynamicButton.Size = new System.Drawing.Size(100, 30);

                tabPage.Controls.Add(dynamicButton);
                buttons.Add(dynamicButton);
                if (iOs[i].IOType == "通用输出")
                    dynamicButton.Click += new EventHandler(IOButton_Click);
                row++;
                if(row>10)
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
                IO value = SF.frmIO.DicIO[button.Text];
                SF.motion.GetOutIO(value, ref Open_1);
                SF.motion.SetIO(value, !Open_1);
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
                    item.Text = name;
                    item.Font = font;
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
                    item.Text = name;
                    item.Font = font;
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
                    for (int i = 0; i < IODebugMaps.inputs.Count; i++)
                    {
                        bool Open_1 = false;
                        SF.motion.GetInIO(IODebugMaps.inputs[i], ref Open_1);
                        buttonsIn[i].BackColor = Open_1 ? Color.Green : Color.Gray;
                        InTemp[i] = Open_1;
                    }
                    break;
                case 1:
                    tabPage2.Controls.Clear();
                    buttonsOut = CreateButtonIO(IODebugMaps.outputs, tabPage2);
                    for (int i = 0; i < IODebugMaps.outputs.Count; i++)
                    {
                        bool Open_1 = false;
                        SF.motion.GetOutIO(IODebugMaps.outputs[i], ref Open_1);
                        buttonsOut[i].BackColor = Open_1 ? Color.Green : Color.Gray;
                        OutTemp[i] = Open_1;
                    }
                    break;
                case 2:
                    tabPage3.Controls.Clear();
                    CreateButtonConnect();
                    for (int i = 0; i < IODebugMaps.iOConnects.Count; i++)
                    {
                        bool Open_1 = false;
                        SF.motion.GetOutIO(IODebugMaps.iOConnects[i].Output, ref Open_1);

                        btnCon[i].OutPut.BackColor = Open_1 ? Color.Green : Color.Gray;
                        ConnectTemp[i].OutPut = Open_1;

                        SF.motion.GetInIO(IODebugMaps.iOConnects[i].Intput1, ref Open_1);

                        btnCon[i].InPut1.BackColor = Open_1 ? Color.Green : Color.Gray;
                        ConnectTemp[i].InPut1 = Open_1;

                        SF.motion.GetInIO(IODebugMaps.iOConnects[i].Intput2, ref Open_1);

                        btnCon[i].InPut2.BackColor = Open_1 ? Color.Green : Color.Gray;
                        ConnectTemp[i].InPut2 = Open_1;
                    }
                    break;
                case 3:
                    listView4.ItemChecked -= listView4_ItemChecked;
                    listView5.ItemChecked -= listView5_ItemChecked;
                    SetConnectItemm();
                    RefreshIODebugMapFrm();
                    RefleshConnecdt();
                    listView4.ItemChecked += listView4_ItemChecked;
                    listView5.ItemChecked += listView5_ItemChecked;
                  
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
                        iOConnect.Intput1 = cacheIO;

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
                        iOConnect.Intput2 = cacheIO;

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
        private void listView3_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView3.SelectedItems.Count > 0)
            {
                listView4.ItemChecked -= listView4_ItemChecked;
                listView5.ItemChecked -= listView5_ItemChecked;
                string selectedText = listView3.SelectedItems[0].Text;
                IOConnect iOConnect = IODebugMaps.iOConnects.FirstOrDefault(con => con.Output.Name == selectedText);
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
        public IO Intput1 = new IO();
        public IO Intput2 = new IO();
    }
}
