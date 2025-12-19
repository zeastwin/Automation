using Automation.ParamFrm;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrackBar;

namespace Automation
{
    public partial class FrmDataStruct : Form
    {
        public List<DataStruct> dataStructs = new List<DataStruct>();
        public List<DataStructHandle> dataStructHandles = new List<DataStructHandle>();
        public FrmDataStructSet frmDataStructSet;
        public int selectDataStructIndex = -1;
        public ManualResetEvent m_evtTrack1 = new ManualResetEvent(false);
        public FrmDataStruct()
        {
            InitializeComponent();
            treeView1.HideSelection = true;
        }

        private void FrmDataStruct_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }
        public void loadFillForm(Panel panel, System.Windows.Forms.Form frm)
        {
            if (frm != null && panel != null)
            {
                frm.ShowIcon = false;
                frm.ShowInTaskbar = false;
                frm.TopLevel = false;
                frm.Dock = DockStyle.Top;
                panel.Controls.Add(frm);
                frm.BringToFront();
                frm.Show();
                frm.Focus();
            }
        }
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
         
            selectDataStructIndex = treeView1.SelectedNode.Index;
        
        }
        bool isTrack=false;
        private void Form_Closing(object sender, FormClosedEventArgs e)
        {
            if (m_evtTrack1.WaitOne(0))
            {
                isTrack = true;
            }
            m_evtTrack1.Reset();
            SF.Delay(50);
            while (isRefleshedStruct != true)
            {
                SF.Delay(10);
            }
            Form form = (Form)sender;
            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.form == form);
            dataStructHandles.Remove(result);
            if (isTrack)
            {
                m_evtTrack1.Set();
                isTrack = !isTrack;
            }
           
        }
        private void dataGridView1_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            DataGridView dataGridView = sender as DataGridView;
            if (dataGridView != null)
            {
                Form ownerForm = dataGridView.FindForm();
                if (ownerForm != null)
                {
                    DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.form == ownerForm);
                    result.SelectRow = e.RowIndex;
                }
            }
            
        }
        private void dataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            DataGridView dataGridView = (DataGridView)sender;
            dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        private void treeView1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                var treeView = (System.Windows.Forms.TreeView)sender;
                var clickedNode = treeView.GetNodeAt(e.Location);

                if (clickedNode == null) // 点击的是空白区域
                {
                    treeView.SelectedNode = null; // 取消当前节点选择
                    selectDataStructIndex = -1;
                }
                if (clickedNode != null)
                {
                    // 选择右键点击的节点
                    treeView.SelectedNode = clickedNode;
                }
            }
        }
        public string GetStructName()
        {
            char targetChar = ':';
            int indexOfTargetChar = treeView1.SelectedNode.Text.IndexOf(targetChar);
            string resultString = treeView1.SelectedNode.Text.Substring(indexOfTargetChar + 1);
            return resultString;
        }
        private void AddDataStruct_Click(object sender, EventArgs e)
        {
            frmDataStructSet = new FrmDataStructSet();
            frmDataStructSet.StartPosition = FormStartPosition.CenterScreen;
            frmDataStructSet.TopMost = true;
            frmDataStructSet.Show();
        }

        private void ModifyDataStruct_Click(object sender, EventArgs e)
        {
           
            frmDataStructSet = new FrmDataStructSet(GetStructName());
            frmDataStructSet.StartPosition = FormStartPosition.CenterScreen;
            frmDataStructSet.TopMost = true;
            frmDataStructSet.Show();
        }
        bool isTrack2 = false;
        private void RemoveDataStruct_Click(object sender, EventArgs e)
        {
            DeleteDataSturct();
        }
        public void DeleteDataSturct()
        {
            if (m_evtTrack1.WaitOne(0))
            {
                isTrack2 = true;
                m_evtTrack1.Reset();

                SF.Delay(100);
                while (isRefleshedStruct != true)
                {
                    SF.Delay(10);
                }
            }

            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.Name == GetStructName());
            if (result != null)
            {
                dataStructHandles.Remove(result);
                result.form.Close();
            }
            dataStructs.RemoveAt(selectDataStructIndex);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStruct", dataStructs);
            RefreshDataSturctList();
            RefreshDataSturctTree();
            if (isTrack2)
            {
                m_evtTrack1.Set();

                isTrack = !isTrack;
            }
        }
        public void ModifyStruct(string name)
        {
            if (m_evtTrack1.WaitOne(0))
            {
                isTrack2 = true;
                m_evtTrack1.Reset();

                SF.Delay(100);
                while (isRefleshedStruct != true)
                {
                    SF.Delay(10);
                }
            }
            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.Name == GetStructName());
            if (result !=null&& result.form != null)
            result.form.Close();
            SF.frmdataStruct.dataStructs[SF.frmdataStruct.selectDataStructIndex].Name = name;
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStruct", dataStructs);
            RefreshDataSturctList();
            RefreshDataSturctTree();
            if (isTrack2)
            {
                m_evtTrack1.Set();

                isTrack = !isTrack;
            }
        }
        public void RefreshDataSturctList()
        {
            treeView1.Nodes.Clear();

            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }
            try
            {
                List<DataStruct> dataStructsTemp = SF.mainfrm.ReadJson<List<DataStruct>>(SF.ConfigPath, "DataStruct");
                if (dataStructsTemp != null)
                    dataStructs = dataStructsTemp;
            }
            catch (Exception ex) 
            {
                //Console.WriteLine(ex.Message);
              
            }
        }
        public void RefreshDataSturctTree()
        {
            treeView1.Nodes.Clear();
            if (dataStructs == null)
            {
                return;
            }
            for (int i = 0; i < dataStructs.Count; i++)
            {
                TreeNode chnode = new TreeNode(i+":"+dataStructs[i].Name);
                treeView1.Nodes.Add(chnode);

            }
           treeView1.SelectedNode = null;
        }
        public int CalculateItemLength(DataStruct dataStruct)
        {
            int dataRowCount = dataStruct.dataStructItems.Count;

            int totalCount = 0;

            for (int i = 0; i < dataRowCount; i++)
            {

                int strCount = dataStruct.dataStructItems[i].str.Count;
                int numCount = dataStruct.dataStructItems[i].num.Count;

                int totalCountTemp = strCount + numCount;

                if(totalCountTemp> totalCount)
                    totalCount = totalCountTemp;
            }
            return totalCount;
        }
  
        public void RefreshDataStructFrm(DataStructHandle dataStructHandle)
        {
            //DataStruct dataStruct = FrmPropertyGrid.DeepCopy(dataStructs.FirstOrDefault(dsh => dsh.Name == dataStructHandle.Name));
            DataStruct dataStruct = (DataStruct)(dataStructs.FirstOrDefault(dsh => dsh.Name == dataStructHandle.Name).Clone());

            DataGridView  dataGridView1 = (DataGridView)dataStructHandle.form.Controls[0];

            if(dataGridView1.Columns.Count == 0)
            {
                DataGridViewTextBoxColumn textColumn = new DataGridViewTextBoxColumn();
                textColumn.HeaderText = "名称";
                Invoke(new Action(() =>
                {
                    dataGridView1.Columns.Add(textColumn);

                }));
            }

            int length = CalculateItemLength(dataStruct);

            int ColDiff = length + 1 - dataGridView1.Columns.Count;

            int columnCount = dataGridView1.Columns.Count;

            for (int i = 0; i < Math.Abs(ColDiff); i++)
            {
                DataGridViewTextBoxColumn textColumn1 = new DataGridViewTextBoxColumn();
                textColumn1.HeaderText = (columnCount + i-1).ToString();
                textColumn1.Width = 90;
                if (ColDiff > 0)
                {
                    Invoke(new Action(() =>
                    {
                        dataGridView1.Columns.Add(textColumn1);
                    }));
                }
                else if (ColDiff < 0)
                {
                    Invoke(new Action(() =>
                    {
                        if (dataGridView1.Columns.Count > 0)
                        {
                            dataGridView1.Columns.RemoveAt(dataGridView1.Columns.Count - 1);
                        }
                    }));
                }
              
            }
            int dataRowCount = dataStruct.dataStructItems.Count;
            int dataRowShowCount = dataGridView1.Rows.Count;

            int RowDiff = dataRowCount - dataRowShowCount;

            for (int i = 0; i < Math.Abs(RowDiff); i++)
            {
                if (RowDiff > 0)
                {
                    Invoke(new Action(() =>
                    {
                        dataGridView1.Rows.Add();
                    }));
                }
                else if (RowDiff < 0)
                {
                    Invoke(new Action(() =>
                    {
                        if (dataGridView1.Rows.Count > 0)
                        {
                            dataGridView1.Rows.RemoveAt(dataGridView1.Rows.Count - 1);
                        }
                    }));
                }  
            }
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                foreach (DataGridViewCell cell in row.Cells)
                {
                    cell.Value = null;
                }
            }
            for (int i = 0; i < dataRowCount; i++)
            {
                dataGridView1.Rows[i].Cells[0].Value = dataStruct.dataStructItems[i].Name;
               // List<KeyValuePair<int, string>> temp1 = dataStruct.dataStructItems[i].str.ToList();
                foreach (KeyValuePair<int, string> kvp in dataStruct.dataStructItems[i].str)
                {
                    int key = kvp.Key + 1;
                    string strs = kvp.Value;
                    dataGridView1.Rows[i].Cells[key].Value = strs;
                    dataGridView1.Rows[i].Cells[key].Style.BackColor = Color.Gray;
                }
               // List<KeyValuePair<int, double>> temp2 = dataStruct.dataStructItems[i].num.ToList();
                foreach (KeyValuePair<int, double> kvp in dataStruct.dataStructItems[i].num)
                {
                    int key = kvp.Key + 1;
                    double num = kvp.Value;
                    dataGridView1.Rows[i].Cells[key].Value = num;
                    dataGridView1.Rows[i].Cells[key].Style.BackColor = Color.White;
                }

            }
         

        }
        
        private void FrmDataStruct_Load(object sender, EventArgs e)
        {
            RefreshDataSturctList();
            RefreshDataSturctTree();
            suppressSelection = true;
            Track();

        }
        private bool suppressSelection;
        private void treeView1_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (suppressSelection)
            {
                e.Cancel = true;
                suppressSelection = false;
            }
        }
        public bool isRefleshedStruct = true;
        public void Track()
        {
            Task.Run(() =>
            {                                                                                                                                                             
                while (m_evtTrack1.WaitOne())
                {
                    isRefleshedStruct = false;
                    for (int i = 0; i < dataStructHandles.Count; i++)
                    {
                        RefreshDataStructFrm(dataStructHandles[i]);
                    }
                    Thread.Sleep(200);
                    isRefleshedStruct = true;
                }
            });
          
        }
        bool isMonitor = true;
        private void btnTrack_Click(object sender, EventArgs e)
        {
            if (isMonitor)
            {
                m_evtTrack1.Set();
                btnTrack.BackColor = Color.Green;
            }
            else
            {
                m_evtTrack1.Reset();
                btnTrack.BackColor = Color.White;
            }
            isMonitor = !isMonitor;
        }

      
        /*=================================================================================================================================*/
        public void Set_StructItem_byName(string name,int ItemIndex, string ItemName ,List<string> strings, List<double> doubles ,string param)
        {
            DataStruct dataStruct = dataStructs.FirstOrDefault(ds => ds.Name == name);
            if (dataStruct == null)
                return;
            DataStructItem dataStructItem = new DataStructItem();
            dataStructItem.str = new Dictionary<int, string>();
            dataStructItem.num = new Dictionary<int, double>();
            int x = 0, y = 0;
            for (int i = 0; i < param.Length; i++)
            {
                char digitChar = param[i];
                if(digitChar == '0')
                {
                    dataStructItem.num.Add(i, doubles[x]);
                    x++;
                }
                else if (digitChar == '1')
                {
                    dataStructItem.str.Add(i, strings[y]);
                    y++;
                }
            }
            dataStructItem.Name = ItemName;
            if (dataStruct.dataStructItems.Count<= ItemIndex)
                dataStruct.dataStructItems.Add(dataStructItem);
            else 
                dataStruct.dataStructItems[ItemIndex]= dataStructItem;
        }
        public void Set_StructItem_byIndex_Param(int index, int ItemIndex, List<string> strings, List<double> doubles , string param)
        {
            DataStruct dataStruct = null;
            if (index >= 0 && index < dataStructs.Count)
            {
                dataStruct = dataStructs[index];
            }
            else
            {
                return;
            }
            DataStructItem dataStructItem = new DataStructItem();
            dataStructItem.str = new Dictionary<int, string>();
            dataStructItem.num = new Dictionary<int, double>();
            int x = 0, y = 0;
            for (int i = 0; i < param.Length; i++)
            {
                char digitChar = param[i];
                if (digitChar == '0')
                {
                    dataStructItem.num.Add(i, doubles[x]);
                    x++;
                }
                else if (digitChar == '1')
                {
                    dataStructItem.str.Add(i, strings[y]);
                    y++;
                }
            }
            if (dataStruct.dataStructItems.Count <= ItemIndex)
                dataStruct.dataStructItems.Add(dataStructItem);
            else
                dataStruct.dataStructItems[ItemIndex] = dataStructItem;
        }

        public void Set_StructItem_byIndex(int DSTindex, int ItemIndex,int index, string value)
        {
            if (dataStructs[DSTindex].dataStructItems[ItemIndex].num.ContainsKey(index))
            {
                if (int.TryParse(value, out int number))
                {
                    dataStructs[DSTindex].dataStructItems[ItemIndex].num[index] = number;
                }
            }
            else
            {
                dataStructs[DSTindex].dataStructItems[ItemIndex].str[index] = value;
            }

        }
        public object Get_StructItem_byIndex(int DSTindex, int ItemIndex, int index)
        {
            if (dataStructs[DSTindex].dataStructItems[ItemIndex].num.ContainsKey(index))
            {
                return dataStructs[DSTindex].dataStructItems[ItemIndex].num[index];
            }
            else if(dataStructs[DSTindex].dataStructItems[ItemIndex].str.ContainsKey(index))
            {
                return dataStructs[DSTindex].dataStructItems[ItemIndex].str[index];
            }
            else { return null; }
        }
        public int Get_StructItemCount_byIndex(int DSTindex, int ItemIndex)
        {
            return dataStructs[DSTindex].dataStructItems[ItemIndex].num.Count + dataStructs[DSTindex].dataStructItems[ItemIndex].str.Count;

        }
        /*=================================================================================================================================*/
        private void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {

            if (selectDataStructIndex != -1)
            {

                bool exactMatchExists = dataStructHandles.Any(dsh => dsh.Name == GetStructName());
                if (!exactMatchExists)
                {
                    DataGridView dataGridView1 = new DataGridView();
                    dataGridView1.Dock = DockStyle.Fill;
                    dataGridView1.AllowUserToAddRows = false;
                    dataGridView1.SelectionMode = DataGridViewSelectionMode.CellSelect;
                    dataGridView1.RowHeadersVisible = false;
                    dataGridView1.AutoGenerateColumns = false;
                    dataGridView1.CellFormatting += dataGridView1_CellFormatting;
                    dataGridView1.CellMouseDown += dataGridView1_CellMouseDown;
                    dataGridView1.ScrollBars = ScrollBars.Both;
                    dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    dataGridView1.ContextMenuStrip = contextMenuStrip2;
                    dataGridView1.Leave += DataGridView1_Leave;

                    Type dgvType = dataGridView1.GetType();
                    PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                    pi.SetValue(dataGridView1, true, null);

                  
                    Form form = new Form();
                    form.Controls.Add(dataGridView1);
                    form.Text = dataStructs[selectDataStructIndex].Name;
                    form.FormClosed += Form_Closing;
                    loadFillForm(panel1, form);
                    dataStructHandles.Add(new DataStructHandle() { form = form, Name = GetStructName() });

                    RefreshDataStructFrm(dataStructHandles.Last());
                }

            }
        }
        private void DataGridView1_Leave(object sender, EventArgs e)
        {
            DataGridView dataGridView = sender as DataGridView;
            if (dataGridView != null)
            {
                dataGridView.CurrentCell = null;
            }
        }
        public Form GetForm(object sender)
        {
            ToolStripMenuItem menuItem = sender as ToolStripMenuItem;
            if (menuItem != null)
            {
                ContextMenuStrip contextMenuStrip = menuItem.Owner as ContextMenuStrip;
                if (contextMenuStrip != null)
                {
                    DataGridView dataGridView = contextMenuStrip.SourceControl as DataGridView;
                    if (dataGridView != null)
                    {
                        Form ownerForm = dataGridView.FindForm();
                        if (ownerForm != null)
                        {
                            return ownerForm;
                        }
                    }
                }
            }
            return null;
        }
      
        private void NewItem_Click(object sender, EventArgs e)
        {
            Form ownerForm = GetForm(sender);
            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.form == ownerForm);
            char targetChar = ':';
            int indexOfTargetChar = result.Name.IndexOf(targetChar);
            string resultString = result.Name.Substring(indexOfTargetChar + 1);
            DataStruct dt = dataStructs.FirstOrDefault(dsh => dsh.Name == resultString);
            frmDataStructSet = new FrmDataStructSet(dt, result);
            frmDataStructSet.StartPosition = FormStartPosition.CenterScreen;
            frmDataStructSet.TopMost = true;
            frmDataStructSet.Show();
        }

        private void ModifyItem_Click(object sender, EventArgs e)
        {
            Form ownerForm = GetForm(sender);
            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.form == ownerForm);
            char targetChar = ':';
            int indexOfTargetChar = result.Name.IndexOf(targetChar);
            string resultString = result.Name.Substring(indexOfTargetChar + 1);
            DataStruct dt = dataStructs.FirstOrDefault(dsh => dsh.Name == resultString);

            frmDataStructSet = new FrmDataStructSet(dt.dataStructItems[result.SelectRow], result);
            frmDataStructSet.dt = dt;
            frmDataStructSet.StartPosition = FormStartPosition.CenterScreen;
            frmDataStructSet.TopMost = true;
            frmDataStructSet.Show();


        }

        private void RemoveItem_Click(object sender, EventArgs e)
        {
            Form ownerForm = GetForm(sender);
            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.form == ownerForm);
            char targetChar = ':';
            int indexOfTargetChar = result.Name.IndexOf(targetChar);
            string resultString = result.Name.Substring(indexOfTargetChar + 1);
            DataStruct dt = dataStructs.FirstOrDefault(dsh => dsh.Name == resultString);

            dt.dataStructItems.RemoveAt(result.SelectRow);

            RefreshDataStructFrm(result);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStruct", SF.frmdataStruct.dataStructs);
            SF.frmdataStruct.RefreshDataSturctList();
            SF.frmdataStruct.RefreshDataSturctTree();

        }
    }
    [Serializable]
    public class DataStruct : ICloneable
    {
        [Browsable(false)]
        public string Name { get; set; }

        public List<DataStructItem> dataStructItems = new List<DataStructItem>();
        public object Clone()
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(memoryStream, this);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return formatter.Deserialize(memoryStream);
            }
        }
    }
    [Serializable]
    public class DataStructItem
    {
        public string Name { get; set; }

        public Dictionary<int,string> str { get; set; }
        public Dictionary<int,double> num { get; set; }

    }
    public class DataStructHandle
    {

        public Form form { get; set; }

        public string Name { get; set; }

        public int SelectRow { get; set; }

    }
}



