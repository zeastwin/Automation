using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static Automation.OperationTypePartial;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace Automation
{
    public partial class FrmDataGrid : Form
    {
        //临时保存操作对象
        public OperationType OperationTemp;
        //鼠标选定的行数
        public int iSelectedRow = -1;

        private int lastHighlightedRow = -1;
        private int lastHighlightedProc = -1;
        private int lastHighlightedStep = -1;
        private bool lastHighlightActive = false;

        //记录要复制行的index
        public List<int> selectedRowIndexes4Copy = new List<int>();
        //
        public List<int> selectedRowIndexes4Del = new List<int>();

        public FrmDataGrid()
        {
            InitializeComponent();
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.ReadOnly = true;
            dataGridView1.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.AutoGenerateColumns = false;

            Type dgvType = this.dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dataGridView1, true, null);

            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        }

        public void UpdateHighlight(EngineSnapshot snapshot)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                if (SF.frmProc == null || SF.frmComunication == null || SF.frmInfo == null || SF.mainfrm == null)
                {
                    return;
                }

                if (SF.curPage != 0)
                {
                    ClearLastHighlight();
                    return;
                }

                if (!SF.frmComunication.CheckFormIsOpen(SF.frmInfo) || !SF.frmComunication.CheckFormIsOpen(SF.mainfrm))
                {
                    return;
                }

                int selectedProc = SF.frmProc.SelectedProcNum;
                if (selectedProc < 0)
                {
                    ClearLastHighlight();
                    return;
                }

                if (snapshot == null || snapshot.ProcIndex != selectedProc || snapshot.State == ProcRunState.Stopped)
                {
                    ClearLastHighlight();
                    return;
                }

                if ((snapshot.State == ProcRunState.Paused || snapshot.State == ProcRunState.SingleStep) && SF.frmProc.SelectedStepNum != snapshot.StepIndex)
                {
                    if (snapshot.StepIndex >= 0
                        && selectedProc >= 0
                        && selectedProc < SF.frmProc.proc_treeView.Nodes.Count
                        && snapshot.StepIndex < SF.frmProc.proc_treeView.Nodes[selectedProc].Nodes.Count)
                    {
                        SelectChildNode(selectedProc, snapshot.StepIndex);
                    }
                }

                if (SF.frmProc.SelectedStepNum != snapshot.StepIndex)
                {
                    ClearLastHighlight();
                    return;
                }

                int rowIndex = snapshot.OpIndex;
                if (rowIndex < 0 || rowIndex >= dataGridView1.RowCount)
                {
                    ClearLastHighlight();
                    return;
                }

                if (!lastHighlightActive
                    || rowIndex != lastHighlightedRow
                    || selectedProc != lastHighlightedProc
                    || snapshot.StepIndex != lastHighlightedStep)
                {
                    if (lastHighlightActive && lastHighlightedRow >= 0 && lastHighlightedRow < dataGridView1.RowCount)
                    {
                        ClearRowColor(lastHighlightedRow);
                        dataGridView1.InvalidateRow(lastHighlightedRow);
                    }

                    SetRowColor(rowIndex, Color.LightBlue);
                    dataGridView1.InvalidateRow(rowIndex);
                    lastHighlightActive = true;
                    lastHighlightedRow = rowIndex;
                    lastHighlightedProc = selectedProc;
                    lastHighlightedStep = snapshot.StepIndex;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                SF.frmInfo.PrintInfo(ex.Message, FrmInfo.Level.Error);
            }
        }

        private void ClearLastHighlight()
        {
            if (lastHighlightActive && lastHighlightedRow >= 0 && lastHighlightedRow < dataGridView1.RowCount)
            {
                ClearRowColor(lastHighlightedRow);
                dataGridView1.InvalidateRow(lastHighlightedRow);
            }
            lastHighlightActive = false;
            lastHighlightedRow = -1;
            lastHighlightedProc = -1;
            lastHighlightedStep = -1;
        }

        public void SelectChildNode(int parentIndex, int childIndex)
        {

            TreeNode parentNode = SF.frmProc.proc_treeView.Nodes[parentIndex];
            if (childIndex >= 0 && childIndex < parentNode.Nodes.Count)
            {
                Invoke(new Action(() =>
                {
                    SF.frmProc.proc_treeView.SelectedNode = parentNode.Nodes[childIndex];

                }));
                dataGridView1.ClearSelection();
            }

        }
        public void ScrollRowToCenter(int rowIndex)
        {
            Invoke(new Action(() =>
            {
                if (rowIndex >= 0 && rowIndex < dataGridView1.RowCount)
                {
                    dataGridView1.FirstDisplayedScrollingRowIndex = rowIndex;
                }
            }));
        }
        public void ClearAllRowColors()
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                row.DefaultCellStyle.BackColor = Color.Empty;
            }
        }
        public void SetRowColor(int rowIndex, Color color)
        {
            if (rowIndex >= 0 && rowIndex < dataGridView1.RowCount)
            {
                dataGridView1.Rows[rowIndex].DefaultCellStyle.BackColor = color;
            }

        }

        public void ClearRowColor(int rowIndex)
        {
            dataGridView1.Rows[rowIndex].DefaultCellStyle.BackColor = Color.Empty;
        }
        private void Add_Click(object sender, EventArgs e)
        {
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            if (this.dataGridView1.SelectedRows.Count == 0)
                iSelectedRow = -1;
            OperationTemp = new HomeRun() { Num = iSelectedRow == -1 ? this.dataGridView1.Rows.Count : iSelectedRow + 1 };
            SF.frmPropertyGrid.OperationType.SelectedIndex = 0;
            OperationTemp.RefleshPropertyAlarm();
            SF.frmPropertyGrid.propertyGrid1.SelectedObject = OperationTemp;
            SF.isAddOps = true;
            SF.BeginEdit(ModifyKind.None);
            SF.frmDataGrid.dataGridView1.Enabled = false;
        }

        private void dataGridView1_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            //输出Ops信息到属性窗体上并输出当前选择行数
            if (e.RowIndex >= 0 && SF.frmProc.SelectedProcNum >= 0 && e.Button == MouseButtons.Left)
            {
                

                SF.frmDataGrid.OperationTemp = (OperationType)(SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[e.RowIndex]).Clone();
                dataGridView1.Rows[e.RowIndex].Selected = true;
                SF.frmPropertyGrid.propertyGrid1.SelectedObject = OperationTemp;
                OperationTemp.evtRP();
                SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmPropertyGrid.propertyGrid1.SelectedObject;
                string selectedValue = SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[e.RowIndex].OperaType;

                foreach (OperationType item in SF.frmPropertyGrid.OperationType.Items)
                {
                    if (item.OperaType.ToString() == selectedValue)
                    {
                        SF.frmPropertyGrid.OperationType.SelectedItem = item; // 设置选定项为匹配的项
                        break;
                    }
                }
                SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();

            }
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                dataGridView1.Rows[e.RowIndex].Selected = !dataGridView1.Rows[e.RowIndex].Selected;
            }
            iSelectedRow = e.RowIndex;

        }

        public void SaveSingleProc(int ProcIndex)
        {
            try
            {
                SF.mainfrm.SaveAsJson(SF.workPath, ProcIndex.ToString(), SF.frmProc.procsList[ProcIndex]);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Delete_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                return;
            }
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            // int count = 0;
            selectedRowIndexes4Del.Clear();
            foreach (DataGridViewRow selectedRow in dataGridView1.SelectedRows)
            {
                selectedRowIndexes4Del.Add(selectedRow.Index);
            }
            selectedRowIndexes4Del.Sort();

            for (int i = selectedRowIndexes4Del.Count - 1; i >= 0; i--)
            {
                int index = selectedRowIndexes4Del[i];
                if (index >= 0 && index < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Count)
                {
                    SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.RemoveAt(index);
                }
            }
            SF.frmProc.RefleshGoto();
            for (int i = 0; i < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Count; i++)
            {
                SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[i].Num = i;
            }

            SaveSingleProc(SF.frmProc.SelectedProcNum);
            SF.frmProc.bindingSource.ResetBindings(true);

        }

        private void Modify_Click(object sender, EventArgs e)
        {
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            SF.BeginEdit(ModifyKind.Operation);
            SF.frmDataGrid.dataGridView1.Enabled = false;
            SF.frmProc.Enabled = false;
        }

        private void dataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (dataGridView1.SelectedRows.Count > 0)
                {
                    Copy();
                }

                e.Handled = true;
            }
            if (e.Control && e.KeyCode == Keys.V)
            {

                Paste();

                e.Handled = true;
            }
            if (e.KeyCode == Keys.Enter)
            {

                e.Handled = true; // 阻止默认行为 防止选择条向下切换

            }
        }

        private void SetStartOps_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum >= 0)
            {
                SF.DR.Stop(SF.frmProc.SelectedProcNum);
            }

            SF.DR.StartProcAt(
                SF.frmProc.procsList[SF.frmProc.SelectedProcNum],
                SF.frmProc.SelectedProcNum,
                SF.frmProc.SelectedStepNum,
                iSelectedRow,
                ProcRunState.Paused);

            Invoke(new Action(() =>
            {
                SF.frmToolBar.btnPause.Text = "继续";
            }));
        }

        private void dataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // 只关心第一列（索引为0）的单元格
            if (e.ColumnIndex == 0 && e.RowIndex >= 0)
            {
                // 获取当前行对应的数据项
                OperationType dataItem = dataGridView1.Rows[e.RowIndex].DataBoundItem as OperationType;


                if (dataItem != null)
                {
                    if (dataItem.Enable)
                    {
                        SetRowColor(e.RowIndex, Color.Gray);
                    }
                    else if (dataItem.isStopPoint)
                    {
                        //  SetRowColor(e.RowIndex, dataGridView1.DefaultCellStyle.BackColor);
                        e.CellStyle.BackColor = Color.Red;
                    }
                    else
                    {
                        // 如果不是断点，保持默认颜色
                        // SetRowColor(e.RowIndex, dataGridView1.DefaultCellStyle.BackColor);
                        e.CellStyle.BackColor = dataGridView1.DefaultCellStyle.BackColor;
                    }
                }
            }
        }

        private void SetStopPoint_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                return;
            }
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            if (iSelectedRow >= 0 && iSelectedRow < dataGridView1.Rows.Count)
            {
                // 获取当前行对应的数据项
                OperationType dataItem = dataGridView1.Rows[iSelectedRow].DataBoundItem as OperationType;

                if (dataItem != null)
                {
                    dataItem.isStopPoint = !dataItem.isStopPoint;
                    SF.frmProc.bindingSource.ResetBindings(true);
                    SF.frmProc.isStopPointDirty = true;
                    dataGridView1.InvalidateRow(iSelectedRow);
                }
            }
        }
        List<OperationType> ListOperationType4Copy = new List<OperationType>();
        public void Copy()
        {
            selectedRowIndexes4Copy.Clear();
            ListOperationType4Copy.Clear();
            foreach (DataGridViewRow selectedRow in dataGridView1.SelectedRows)
            {
                selectedRowIndexes4Copy.Add(selectedRow.Index);
            }
            selectedRowIndexes4Copy.Sort();
            for (int i = 0; i < selectedRowIndexes4Copy.Count; i++)
            {
                OperationType dataItem = (OperationType)(dataGridView1.Rows[selectedRowIndexes4Copy[i]].DataBoundItem as OperationType).Clone();
                dataItem.Num = -1;
                ListOperationType4Copy.Add(dataItem);
            }
        }
        public void Paste()
        {
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                return;
            }
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            bool isEmptyRow = false;
            List<OperationType> deepCopy;
            using (MemoryStream stream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, ListOperationType4Copy);
                stream.Seek(0, SeekOrigin.Begin);
                deepCopy = (List<OperationType>)formatter.Deserialize(stream);
            }
            if (SF.frmDataGrid.dataGridView1.Rows.Count != 0)
            {
                SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.InsertRange(iSelectedRow + 1, deepCopy);

            }
            else
            {
                SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.AddRange(deepCopy);
                isEmptyRow = true;
            }
            SF.frmProc.RefleshGoto();
            for (int i = 0; i < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Count; i++)
            {
                SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[i].Num = i;
            }

            SaveSingleProc(SF.frmProc.SelectedProcNum);
            SF.frmProc.bindingSource.ResetBindings(true);



            if (!isEmptyRow)
            {
                int rowCountAfterPaste = iSelectedRow + 1 + deepCopy.Count;
                int rowCountBeforePaste = iSelectedRow + 1;
                for (int i = rowCountBeforePaste; i < rowCountAfterPaste; i++)
                {
                    dataGridView1.Rows[i].DefaultCellStyle.BackColor = Color.Red;
                }
            }

        }
        private void copy_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count > 0)
            {
                Copy();
            }
        }
        private void paste_Click(object sender, EventArgs e)
        {
            Paste();
        }
        private int dragIndex = -1;
        private void dataGridView1_MouseDown(object sender, MouseEventArgs e)
        {
            if (ModifierKeys == Keys.Alt && e.Button == MouseButtons.Left)
            {
                dragIndex = dataGridView1.HitTest(e.X, e.Y).RowIndex;
                if (dragIndex >= 0)
                {
                    dataGridView1.DoDragDrop(dataGridView1.Rows[dragIndex], DragDropEffects.Move);
                }
            }
        }

        private void dataGridView1_DragDrop(object sender, DragEventArgs e)
        {
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                dragIndex = -1;
                return;
            }
            if (dragIndex >= 0)
            {
                Point p = dataGridView1.PointToClient(new Point(e.X, e.Y));
                int targetIndex = dataGridView1.HitTest(p.X, p.Y).RowIndex;

                if (targetIndex >= 0 && targetIndex < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Count)
                {
                    OperationType draggedItem = SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[dragIndex];
                    SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.RemoveAt(dragIndex);
                    SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Insert(targetIndex, draggedItem);

                    SF.frmProc.RefleshGoto();
                    for (int i = 0; i < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Count; i++)
                    {
                        SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[i].Num = i;
                    }

                    SaveSingleProc(SF.frmProc.SelectedProcNum);
                    SF.frmProc.bindingSource.ResetBindings(true);

                }
            }

            dragIndex = -1;
        }

        private void dataGridView1_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void dataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
                {
                    return;
                }
                SF.BeginEdit(ModifyKind.Operation);
                SF.frmDataGrid.dataGridView1.Enabled = false;
                SF.frmProc.Enabled = false;
            }
        }

        private void OneSetp_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0 || iSelectedRow < 0)
            {
                return;
            }
            SF.DR.RunSingleOpOnce(
                SF.frmProc.procsList[SF.frmProc.SelectedProcNum],
                SF.frmProc.SelectedProcNum,
                SF.frmProc.SelectedStepNum,
                iSelectedRow);
        }

        private void Enable_Click(object sender, EventArgs e)
        {
            if (iSelectedRow >= 0)
            {
                if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
                {
                    return;
                }
                OperationType dataItem = dataGridView1.Rows[iSelectedRow].DataBoundItem as OperationType;

                if (dataItem != null)
                {
                    dataItem.Enable = !dataItem.Enable;
                }
            }
        }

        private void CProgramCopy_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count > 0)
            {
                selectedRowIndexes4Copy.Clear();
                ListOperationType4Copy.Clear();
                foreach (DataGridViewRow selectedRow in dataGridView1.SelectedRows)
                {
                    selectedRowIndexes4Copy.Add(selectedRow.Index);
                }
                selectedRowIndexes4Copy.Sort();
                for (int i = 0; i < selectedRowIndexes4Copy.Count; i++)
                {
                    OperationType dataItem = (OperationType)(dataGridView1.Rows[selectedRowIndexes4Copy[i]].DataBoundItem as OperationType).Clone();
                    dataItem.Num = -1;
                    ListOperationType4Copy.Add(dataItem);
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    IFormatter formatter = new BinaryFormatter();
                    formatter.Serialize(stream, ListOperationType4Copy);
                    byte[] serializedData = stream.ToArray();
                    Clipboard.SetData("MyCustomDataFormat", serializedData);
                }
            }
        }

        private void CProgramPaste_Click(object sender, EventArgs e)
        {
            try
            {
                if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
                {
                    return;
                }
                bool isEmptyRow = false;
                List<OperationType> deepCopy = null;
                if (Clipboard.ContainsData("MyCustomDataFormat"))
                {
                    byte[] receivedData = (byte[])Clipboard.GetData("MyCustomDataFormat");
                    using (MemoryStream stream = new MemoryStream(receivedData))
                    {
                        IFormatter formatter = new BinaryFormatter();
                        deepCopy = (List<OperationType>)formatter.Deserialize(stream);
                    }
                }
                if (deepCopy == null)
                {
                    return;
                }
                if (SF.frmDataGrid.dataGridView1.Rows.Count != 0)
                {
                    SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.InsertRange(iSelectedRow + 1, deepCopy);

                }
                else
                {
                    SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.AddRange(deepCopy);
                    isEmptyRow = true;
                }
                SF.frmProc.RefleshGoto();
                for (int i = 0; i < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Count; i++)
                {
                    SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[i].Num = i;
                }
                SaveSingleProc(SF.frmProc.SelectedProcNum);
                SF.frmProc.bindingSource.ResetBindings(true);


                if (!isEmptyRow)
                {
                    int rowCountAfterPaste = iSelectedRow + 1 + deepCopy.Count;
                    int rowCountBeforePaste = iSelectedRow + 1;
                    for (int i = rowCountBeforePaste; i < rowCountAfterPaste; i++)
                    {
                        dataGridView1.Rows[i].DefaultCellStyle.BackColor = Color.Red;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}
