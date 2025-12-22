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
        private List<DataStruct> dataStructs = new List<DataStruct>();
        public List<DataStructHandle> dataStructHandles = new List<DataStructHandle>();
        public FrmDataStructSet frmDataStructSet;
        public int selectDataStructIndex = -1;
        private readonly System.Windows.Forms.Timer trackTimer = new System.Windows.Forms.Timer();
        private bool isRefreshing = false;
        private int lastRefreshTick = 0;
        private int lastStoreVersion = -1;
        private const int TrackIntervalMs = 100;
        private const int TrackMinRefreshMs = 200;
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
        private void Form_Closing(object sender, FormClosedEventArgs e)
        {
            Form form = (Form)sender;
            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.form == form);
            if (result != null)
            {
                dataStructHandles.Remove(result);
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
            if (treeView1.SelectedNode == null)
            {
                return string.Empty;
            }
            int indexOfTargetChar = treeView1.SelectedNode.Text.IndexOf(targetChar);
            if (indexOfTargetChar < 0)
            {
                return treeView1.SelectedNode.Text;
            }
            return treeView1.SelectedNode.Text.Substring(indexOfTargetChar + 1);
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
            string structName = GetStructName();
            if (string.IsNullOrEmpty(structName))
            {
                return;
            }
            frmDataStructSet = new FrmDataStructSet(structName);
            frmDataStructSet.StartPosition = FormStartPosition.CenterScreen;
            frmDataStructSet.TopMost = true;
            frmDataStructSet.Show();
        }
        private void RemoveDataStruct_Click(object sender, EventArgs e)
        {
            DeleteDataSturct();
        }
        public void DeleteDataSturct()
        {
            if (selectDataStructIndex < 0)
            {
                return;
            }

            if (!SF.dataStructStore.TryGetStructNameByIndex(selectDataStructIndex, out string structName))
            {
                return;
            }

            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.Name == structName);
            if (result != null)
            {
                dataStructHandles.Remove(result);
                result.form.Close();
            }
            if (!SF.dataStructStore.RemoveStructAt(selectDataStructIndex))
            {
                return;
            }
            SF.dataStructStore.Save(SF.ConfigPath);
            RefreshDataSturctList();
            RefreshDataSturctTree();
        }
        public bool ModifyStruct(string name)
        {
            if (selectDataStructIndex < 0)
            {
                return false;
            }

            if (!SF.dataStructStore.TryGetStructNameByIndex(selectDataStructIndex, out string structName))
            {
                return false;
            }

            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.Name == structName);
            if (result != null && result.form != null)
            {
                result.form.Close();
            }

            if (!SF.dataStructStore.RenameStruct(selectDataStructIndex, name))
            {
                return false;
            }
            SF.dataStructStore.Save(SF.ConfigPath);
            RefreshDataSturctList();
            RefreshDataSturctTree();
            return true;
        }
        public void RefreshDataSturctList()
        {
            dataStructs = SF.dataStructStore.GetSnapshot();
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
            if (dataStruct == null || dataStruct.dataStructItems == null)
            {
                return 0;
            }

            int totalCount = 0;
            for (int i = 0; i < dataStruct.dataStructItems.Count; i++)
            {
                DataStructItem item = dataStruct.dataStructItems[i];
                if (item == null)
                {
                    continue;
                }
                int itemCount = item.GetMaxIndex() + 1;
                if (itemCount > totalCount)
                {
                    totalCount = itemCount;
                }
            }
            return totalCount;
        }
  
        public void RefreshDataStructFrm(DataStructHandle dataStructHandle)
        {
            if (dataStructHandle == null || dataStructHandle.form == null || dataStructHandle.form.IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => RefreshDataStructFrm(dataStructHandle)));
                return;
            }

            if (!SF.dataStructStore.TryGetStructSnapshotByName(dataStructHandle.Name, out DataStruct dataStruct))
            {
                return;
            }

            if (dataStructHandle.form.Controls.Count == 0)
            {
                return;
            }
            DataGridView dataGridView1 = dataStructHandle.form.Controls[0] as DataGridView;
            if (dataGridView1 == null)
            {
                return;
            }

            if (dataGridView1.Columns.Count == 0)
            {
                DataGridViewTextBoxColumn textColumn = new DataGridViewTextBoxColumn();
                textColumn.HeaderText = "名称";
                dataGridView1.Columns.Add(textColumn);
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
                    dataGridView1.Columns.Add(textColumn1);
                }
                else if (ColDiff < 0)
                {
                    if (dataGridView1.Columns.Count > 0)
                    {
                        dataGridView1.Columns.RemoveAt(dataGridView1.Columns.Count - 1);
                    }
                }
              
            }
            int dataRowCount = dataStruct.dataStructItems.Count;
            int dataRowShowCount = dataGridView1.Rows.Count;

            int RowDiff = dataRowCount - dataRowShowCount;

            for (int i = 0; i < Math.Abs(RowDiff); i++)
            {
                if (RowDiff > 0)
                {
                    dataGridView1.Rows.Add();
                }
                else if (RowDiff < 0)
                {
                    if (dataGridView1.Rows.Count > 0)
                    {
                        dataGridView1.Rows.RemoveAt(dataGridView1.Rows.Count - 1);
                    }
                }  
            }
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                foreach (DataGridViewCell cell in row.Cells)
                {
                    cell.Value = null;
                    cell.Style.BackColor = Color.White;
                }
            }
            for (int i = 0; i < dataRowCount; i++)
            {
                dataGridView1.Rows[i].Cells[0].Value = dataStruct.dataStructItems[i].Name;
               // List<KeyValuePair<int, string>> temp1 = dataStruct.dataStructItems[i].str.ToList();
                if (dataStruct.dataStructItems[i].str != null)
                {
                    foreach (KeyValuePair<int, string> kvp in dataStruct.dataStructItems[i].str)
                    {
                        int key = kvp.Key + 1;
                        string strs = kvp.Value;
                        dataGridView1.Rows[i].Cells[key].Value = strs;
                        dataGridView1.Rows[i].Cells[key].Style.BackColor = Color.Gray;
                    }
                }
               // List<KeyValuePair<int, double>> temp2 = dataStruct.dataStructItems[i].num.ToList();
                if (dataStruct.dataStructItems[i].num != null)
                {
                    foreach (KeyValuePair<int, double> kvp in dataStruct.dataStructItems[i].num)
                    {
                        int key = kvp.Key + 1;
                        double num = kvp.Value;
                        dataGridView1.Rows[i].Cells[key].Value = num;
                        dataGridView1.Rows[i].Cells[key].Style.BackColor = Color.White;
                    }
                }

            }
         

        }
        
        private void FrmDataStruct_Load(object sender, EventArgs e)
        {
            RefreshDataSturctList();
            RefreshDataSturctTree();
            suppressSelection = true;
            trackTimer.Interval = TrackIntervalMs;
            trackTimer.Tick += TrackTimer_Tick;
            trackTimer.Stop();
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
        private void TrackTimer_Tick(object sender, EventArgs e)
        {
            if (isRefreshing)
            {
                return;
            }
            if (dataStructHandles.Count == 0)
            {
                return;
            }
            int nowTick = Environment.TickCount;
            if (Math.Abs(nowTick - lastRefreshTick) < TrackMinRefreshMs)
            {
                return;
            }
            int currentVersion = SF.dataStructStore.Version;
            if (currentVersion == lastStoreVersion)
            {
                return;
            }

            isRefreshing = true;
            try
            {
                bool refreshed = false;
                for (int i = 0; i < dataStructHandles.Count; i++)
                {
                    DataStructHandle handle = dataStructHandles[i];
                    if (!IsHandleVisibleInPanel(handle))
                    {
                        continue;
                    }
                    RefreshDataStructFrm(handle);
                    refreshed = true;
                }
                if (refreshed)
                {
                    lastStoreVersion = currentVersion;
                }
                lastRefreshTick = nowTick;
            }
            finally
            {
                isRefreshing = false;
            }
        }

        private bool IsHandleVisibleInPanel(DataStructHandle handle)
        {
            if (handle == null || handle.form == null || handle.form.IsDisposed)
            {
                return false;
            }
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                return false;
            }
            if (panel1 == null || !panel1.Visible)
            {
                return false;
            }
            if (!handle.form.Visible || handle.form.WindowState == FormWindowState.Minimized)
            {
                return false;
            }
            if (handle.form.Parent != panel1)
            {
                return false;
            }

            Rectangle viewRect = new Rectangle(-panel1.AutoScrollPosition.X, -panel1.AutoScrollPosition.Y, panel1.ClientSize.Width, panel1.ClientSize.Height);
            return handle.form.Bounds.IntersectsWith(viewRect);
        }
        bool isMonitor = true;
        private void btnTrack_Click(object sender, EventArgs e)
        {
            if (isMonitor)
            {
                lastStoreVersion = -1;
                lastRefreshTick = 0;
                trackTimer.Start();
                btnTrack.BackColor = Color.Green;
            }
            else
            {
                trackTimer.Stop();
                btnTrack.BackColor = Color.White;
            }
            isMonitor = !isMonitor;
        }

      
        private void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {

            if (selectDataStructIndex != -1)
            {

                if (!SF.dataStructStore.TryGetStructNameByIndex(selectDataStructIndex, out string structName))
                {
                    return;
                }
                bool exactMatchExists = dataStructHandles.Any(dsh => dsh.Name == structName);
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
                    form.Text = structName;
                    form.FormClosed += Form_Closing;
                    loadFillForm(panel1, form);
                    dataStructHandles.Add(new DataStructHandle() { form = form, Name = structName });

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
            if (result == null || string.IsNullOrEmpty(result.Name))
            {
                return;
            }
            if (!SF.dataStructStore.TryGetStructSnapshotByName(result.Name, out DataStruct dt))
            {
                return;
            }
            frmDataStructSet = new FrmDataStructSet(dt, result);
            frmDataStructSet.StartPosition = FormStartPosition.CenterScreen;
            frmDataStructSet.TopMost = true;
            frmDataStructSet.Show();
        }

        private void ModifyItem_Click(object sender, EventArgs e)
        {
            Form ownerForm = GetForm(sender);
            DataStructHandle result = dataStructHandles.FirstOrDefault(dsh => dsh.form == ownerForm);
            if (result == null || string.IsNullOrEmpty(result.Name))
            {
                return;
            }
            if (!SF.dataStructStore.TryGetStructSnapshotByName(result.Name, out DataStruct dt))
            {
                return;
            }

            if (result.SelectRow < 0 || result.SelectRow >= dt.dataStructItems.Count)
            {
                return;
            }
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
            if (result == null || string.IsNullOrEmpty(result.Name))
            {
                return;
            }
            if (!SF.dataStructStore.TryGetStructIndexByName(result.Name, out int structIndex))
            {
                return;
            }
            if (!SF.dataStructStore.TryRemoveItemAt(structIndex, result.SelectRow))
            {
                return;
            }

            RefreshDataStructFrm(result);
            SF.dataStructStore.Save(SF.ConfigPath);
            SF.frmdataStruct.RefreshDataSturctList();
            SF.frmdataStruct.RefreshDataSturctTree();

        }
    }
    public class DataStructHandle
    {

        public Form form { get; set; }

        public string Name { get; set; }

        public int SelectRow { get; set; }

    }
}



