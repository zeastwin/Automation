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
using System.Drawing.Drawing2D;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static Automation.OperationTypePartial;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace Automation
{
    public partial class FrmDataGrid : Form
    {
        private const int MenuIconSize = 20;

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
            InitContextMenuIcons();
            contextMenuStrip2.KeyDown += contextMenuStrip2_KeyDown;
            contextMenuStrip2.Opening += contextMenuStrip2_Opening;

            Type dgvType = this.dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dataGridView1, true, null);

            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        }

        private void InitContextMenuIcons()
        {
            SetStartOps.Image = CreateMenuIcon(MenuIconType.StartPoint);
            Add.Image = CreateMenuIcon(MenuIconType.Add);
            Modify.Image = CreateMenuIcon(MenuIconType.Edit);
            SetStopPoint.Image = CreateMenuIcon(MenuIconType.Breakpoint);
            Enable.Image = CreateMenuIcon(MenuIconType.Toggle);
            SetStopPoint.ShortcutKeyDisplayString = "X";
            Enable.ShortcutKeyDisplayString = "U";
        }

        private enum MenuIconType
        {
            StartPoint,
            Add,
            Edit,
            Breakpoint,
            Toggle
        }

        private static Bitmap CreateMenuIcon(MenuIconType iconType)
        {
            Bitmap bitmap = new Bitmap(MenuIconSize, MenuIconSize);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen pen = new Pen(Color.DimGray, 2))
                using (Brush brush = new SolidBrush(Color.DimGray))
                {
                    switch (iconType)
                    {
                        case MenuIconType.StartPoint:
                            g.DrawLine(pen, 6, 3, 6, 17);
                            g.FillPolygon(brush, new[] { new Point(6, 4), new Point(15, 7), new Point(6, 10) });
                            break;
                        case MenuIconType.Add:
                            g.DrawLine(pen, 10, 4, 10, 16);
                            g.DrawLine(pen, 4, 10, 16, 10);
                            break;
                        case MenuIconType.Edit:
                            g.DrawLine(pen, 5, 15, 15, 5);
                            g.FillPolygon(brush, new[] { new Point(14, 4), new Point(17, 3), new Point(16, 6) });
                            break;
                        case MenuIconType.Breakpoint:
                            g.DrawEllipse(pen, 4, 4, 12, 12);
                            g.FillEllipse(brush, 8, 8, 4, 4);
                            break;
                        case MenuIconType.Toggle:
                            g.DrawArc(pen, 4, 4, 12, 12, 40, 280);
                            g.DrawLine(pen, 10, 2, 10, 8);
                            break;
                    }
                }
            }
            return bitmap;
        }

        private void contextMenuStrip2_Opening(object sender, CancelEventArgs e)
        {
            if (dataGridView1 == null)
            {
                return;
            }

            Point clientPoint = dataGridView1.PointToClient(Cursor.Position);
            DataGridView.HitTestInfo hitTest = dataGridView1.HitTest(clientPoint.X, clientPoint.Y);
            int rowIndex = hitTest.RowIndex;
            if (rowIndex < 0 && dataGridView1.CurrentCell != null)
            {
                rowIndex = dataGridView1.CurrentCell.RowIndex;
            }

            if (rowIndex >= 0 && rowIndex < dataGridView1.Rows.Count)
            {
                iSelectedRow = rowIndex;
                if (!dataGridView1.Rows[rowIndex].Selected)
                {
                    dataGridView1.ClearSelection();
                    dataGridView1.Rows[rowIndex].Selected = true;
                }
            }
        }

        private void contextMenuStrip2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.X && e.KeyCode != Keys.U)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;

            if (SF.frmProc == null)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：流程界面未初始化，无法操作。", FrmInfo.Level.Error);
                }
                return;
            }

            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：未选择流程或步骤。", FrmInfo.Level.Error);
                }
                return;
            }

            if (iSelectedRow < 0 || iSelectedRow >= dataGridView1.Rows.Count)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：未选择指令。", FrmInfo.Level.Error);
                }
                return;
            }

            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：当前流程运行中禁止编辑。", FrmInfo.Level.Error);
                }
                return;
            }

            OperationType dataItem = dataGridView1.Rows[iSelectedRow].DataBoundItem as OperationType;
            if (dataItem == null)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：指令数据为空，无法操作。", FrmInfo.Level.Error);
                }
                return;
            }

            if (e.KeyCode == Keys.X)
            {
                SetStopPoint_Click(sender, EventArgs.Empty);
                string action = dataItem.isStopPoint ? "已设置断点" : "已取消断点";
                string opName = string.IsNullOrWhiteSpace(dataItem.Name) ? "未命名" : dataItem.Name;
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo($"快捷键：{SF.frmProc.SelectedProcNum}-{SF.frmProc.SelectedStepNum}-{iSelectedRow} {opName} {action}", FrmInfo.Level.Normal);
                }
                return;
            }

            Enable_Click(sender, EventArgs.Empty);
            string enableAction = dataItem.Disable ? "已禁用" : "已启用";
            string enableOpName = string.IsNullOrWhiteSpace(dataItem.Name) ? "未命名" : dataItem.Name;
            if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo($"快捷键：{SF.frmProc.SelectedProcNum}-{SF.frmProc.SelectedStepNum}-{iSelectedRow} {enableOpName} {enableAction}", FrmInfo.Level.Normal);
            }
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

                if (snapshot.State == ProcRunState.Paused || snapshot.State == ProcRunState.SingleStep)
                {
                    if (SF.isSingleStepFollowPending)
                    {
                        if (selectedProc != SF.singleStepFollowProcIndex)
                        {
                            SF.isSingleStepFollowPending = false;
                            SF.singleStepFollowProcIndex = -1;
                        }
                        else
                        {
                            if (SF.frmProc.SelectedStepNum != snapshot.StepIndex)
                            {
                                if (snapshot.StepIndex >= 0
                                    && selectedProc >= 0
                                    && selectedProc < SF.frmProc.proc_treeView.Nodes.Count
                                    && snapshot.StepIndex < SF.frmProc.proc_treeView.Nodes[selectedProc].Nodes.Count)
                                {
                                    SelectChildNode(selectedProc, snapshot.StepIndex);
                                }
                            }
                            SF.isSingleStepFollowPending = false;
                            SF.singleStepFollowProcIndex = -1;
                        }
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
            ProcRunState startState = ProcRunState.SingleStep;
            EngineSnapshot startSnapshot = SF.DR.GetSnapshot(SF.frmProc.SelectedProcNum);
            if (startSnapshot != null && startSnapshot.State == ProcRunState.Paused)
            {
                startState = ProcRunState.Paused;
            }

            if (SF.frmProc.SelectedProcNum >= 0)
            {
                SF.DR.Stop(SF.frmProc.SelectedProcNum);
            }

            SF.DR.StartProcAt(
                SF.frmProc.procsList[SF.frmProc.SelectedProcNum],
                SF.frmProc.SelectedProcNum,
                SF.frmProc.SelectedStepNum,
                iSelectedRow,
                startState);

            Invoke(new Action(() =>
            {
                SF.frmToolBar.btnPause.Text = "继续";
                SF.frmToolBar.btnPause.Enabled = startState != ProcRunState.Paused;
                SF.frmToolBar.SingleRun.Enabled = startState == ProcRunState.SingleStep;
            }));
        }

        private void dataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            OperationType dataItem = dataGridView1.Rows[e.RowIndex].DataBoundItem as OperationType;
            if (dataItem == null)
            {
                return;
            }

            if (e.ColumnIndex == statusMark.Index)
            {
                string mark = string.Empty;
                if (dataItem.Disable)
                {
                    mark += "X";
                }
                if (dataItem.isStopPoint)
                {
                    mark += "●";
                }
                e.Value = mark;
                if (dataItem.isStopPoint)
                {
                    e.CellStyle.BackColor = Color.Red;
                    // 断点标记列保持红底，不再走后续行色逻辑
                    return;
                }
            }

            Color rowColor = dataGridView1.Rows[e.RowIndex].DefaultCellStyle.BackColor;
            bool hasRowColor = !rowColor.IsEmpty;
            if (hasRowColor)
            {
                e.CellStyle.BackColor = rowColor;
            }
            else if (dataItem.Disable)
            {
                e.CellStyle.BackColor = Color.Gray;
            }
            else
            {
                e.CellStyle.BackColor = dataGridView1.DefaultCellStyle.BackColor;
            }

            // 保持系统默认选中颜色，避免选中状态不可见
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
                    dataItem.Disable = !dataItem.Disable;
                    dataGridView1.InvalidateRow(iSelectedRow);
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
