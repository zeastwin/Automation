using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private ProcRunState lastHighlightedState = ProcRunState.Stopped;
        private bool lastHighlightActive = false;
        private bool singleStepFollowPending;
        private int singleStepFollowProcIndex = -1;
        private bool contextMenuByMouse = false;
        private int contextMenuRowIndex = -1;

        // 数据网格变动动效：AI 改动当前显示的流程后，闪烁整个网格提示用户。
        private System.Windows.Forms.Timer gridFlashTimer;
        private Color gridFlashColor;
        private int gridFlashCount;
        private const int GridFlashMaxCount = 6;
        // 行级闪烁目标列表：(行索引, 颜色)。为空时闪烁整个 grid。
        private List<(int rowIndex, Color color)> flashTargetRows;

        //记录要复制行的index
        public List<int> selectedRowIndexes4Copy = new List<int>();
        //
        public List<int> selectedRowIndexes4Del = new List<int>();

        public FrmDataGrid()
        {
            InitializeComponent();
            Disposed += FrmDataGrid_Disposed;
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

            int rowIndex = -1;
            if (contextMenuByMouse)
            {
                rowIndex = contextMenuRowIndex;
            }
            else
            {
                Point clientPoint = dataGridView1.PointToClient(Cursor.Position);
                DataGridView.HitTestInfo hitTest = dataGridView1.HitTest(clientPoint.X, clientPoint.Y);
                rowIndex = hitTest.RowIndex;
                if (rowIndex < 0 && dataGridView1.CurrentCell != null)
                {
                    rowIndex = dataGridView1.CurrentCell.RowIndex;
                }
            }
            contextMenuByMouse = false;
            contextMenuRowIndex = -1;

            if (rowIndex >= 0 && rowIndex < dataGridView1.Rows.Count)
            {
                iSelectedRow = rowIndex;
                dataGridView1.ClearSelection();
                dataGridView1.Rows[rowIndex].Selected = true;
                dataGridView1.CurrentCell = dataGridView1.Rows[rowIndex].Cells[0];
            }
            else
            {
                iSelectedRow = -1;
                dataGridView1.ClearSelection();
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

                if (SF.frmProc == null || SF.mainfrm == null)
                {
                    return;
                }

                if (SF.mainfrm.WindowState == FormWindowState.Minimized || !SF.mainfrm.ContainsFocus)
                {
                    ClearLastHighlight();
                    return;
                }

                if (SF.curPage != 0)
                {
                    ClearLastHighlight();
                    return;
                }

                if (SF.mainfrm.IsDisposed || !SF.mainfrm.IsHandleCreated || !SF.mainfrm.Visible)
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
                    if (singleStepFollowPending)
                    {
                        if (selectedProc != singleStepFollowProcIndex)
                        {
                            singleStepFollowPending = false;
                            singleStepFollowProcIndex = -1;
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
                            singleStepFollowPending = false;
                            singleStepFollowProcIndex = -1;
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
                    || snapshot.StepIndex != lastHighlightedStep
                    || snapshot.State != lastHighlightedState)
                {
                    if (lastHighlightActive && lastHighlightedRow >= 0 && lastHighlightedRow < dataGridView1.RowCount)
                    {
                        ClearRowColor(lastHighlightedRow);
                        dataGridView1.InvalidateRow(lastHighlightedRow);
                    }

                    Color highlightColor = snapshot.State == ProcRunState.Alarming ? Color.Red : Color.LightBlue;
                    SetRowColor(rowIndex, highlightColor);
                    dataGridView1.InvalidateRow(rowIndex);
                    lastHighlightActive = true;
                    lastHighlightedRow = rowIndex;
                    lastHighlightedProc = selectedProc;
                    lastHighlightedStep = snapshot.StepIndex;
                    lastHighlightedState = snapshot.State;
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

        public void RequestSingleStepFollow(int procIndex)
        {
            singleStepFollowProcIndex = procIndex;
            singleStepFollowPending = procIndex >= 0;
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
            lastHighlightedState = ProcRunState.Stopped;
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

        /// <summary>
        /// 闪烁整个数据网格的所有行，提示用户当前显示的流程/步骤被 AI 改动。
        /// kind 决定闪烁颜色：Modified=橙黄、Added=浅绿、Deleted=浅红。
        /// 仅在当前网格显示的流程/步骤是被改动的流程时调用。
        /// </summary>
        public void FlashGrid(ProcChangeKind kind)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => FlashGrid(kind)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            // 停止之前未完成的闪烁
            if (gridFlashTimer != null)
            {
                gridFlashTimer.Stop();
                gridFlashTimer.Dispose();
                gridFlashTimer = null;
            }
            ClearAllRowColors();

            gridFlashColor = kind == ProcChangeKind.Added ? Color.LightGreen
                           : kind == ProcChangeKind.Deleted ? Color.LightPink
                           : Color.Khaki;
            gridFlashCount = 0;

            gridFlashTimer = new System.Windows.Forms.Timer();
            gridFlashTimer.Interval = 300;
            gridFlashTimer.Tick += GridFlashTimer_Tick;
            gridFlashTimer.Start();
        }

        private void GridFlashTimer_Tick(object sender, EventArgs e)
        {
            if (dataGridView1 == null || dataGridView1.IsDisposed)
            {
                gridFlashTimer?.Stop();
                return;
            }
            if (gridFlashCount >= GridFlashMaxCount)
            {
                ClearAllRowColors();
                gridFlashTimer.Stop();
                gridFlashTimer.Dispose();
                gridFlashTimer = null;
                flashTargetRows = null;
                return;
            }
            bool setColor = (gridFlashCount % 2 == 0);
            if (flashTargetRows != null && flashTargetRows.Count > 0)
            {
                // 行级闪烁：只闪烁目标行
                foreach (var (rowIndex, color) in flashTargetRows)
                {
                    if (rowIndex >= 0 && rowIndex < dataGridView1.Rows.Count)
                    {
                        dataGridView1.Rows[rowIndex].DefaultCellStyle.BackColor = setColor ? color : Color.Empty;
                    }
                }
            }
            else
            {
                // 整体闪烁：闪烁所有行
                Color c = setColor ? gridFlashColor : Color.Empty;
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    row.DefaultCellStyle.BackColor = c;
                }
            }
            gridFlashCount++;
        }

        /// <summary>
        /// 只闪烁被修改的行。从 affectedOps 中筛选当前步骤匹配的 opIndex，只闪烁这些行。
        /// kind 决定颜色：Modified=橙黄、Added=浅绿、Deleted=浅红。
        /// </summary>
        public void FlashRows(List<(int stepIndex, int opIndex, ProcChangeKind kind)> affectedOps)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => FlashRows(affectedOps)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            int currentStepIndex = SF.frmProc.SelectedStepNum;
            var targetRows = new List<(int rowIndex, Color color)>();
            foreach (var (stepIndex, opIndex, kind) in affectedOps)
            {
                if (stepIndex != currentStepIndex || opIndex < 0)
                {
                    continue;
                }
                Color color = kind == ProcChangeKind.Added ? Color.LightGreen
                            : kind == ProcChangeKind.Deleted ? Color.LightPink
                            : Color.Khaki;
                targetRows.Add((opIndex, color));
            }

            if (targetRows.Count == 0)
            {
                return; // 当前步骤没有被修改的指令，不闪烁
            }

            // 新增指令位于当前可视区域之外时自动跟随一次，便于观察 AI 的插入过程；
            // 修改和删除不改变用户滚动位置。
            int addedRowIndex = targetRows
                .Where(item => item.color == Color.LightGreen)
                .Select(item => item.rowIndex)
                .DefaultIfEmpty(-1)
                .First();
            if (addedRowIndex >= 0 && addedRowIndex < dataGridView1.Rows.Count
                && !dataGridView1.Rows[addedRowIndex].Displayed)
            {
                try
                {
                    dataGridView1.FirstDisplayedScrollingRowIndex = addedRowIndex;
                }
                catch (InvalidOperationException)
                {
                }
            }

            // 停止之前未完成的闪烁
            if (gridFlashTimer != null)
            {
                gridFlashTimer.Stop();
                gridFlashTimer.Dispose();
                gridFlashTimer = null;
            }
            ClearAllRowColors();

            flashTargetRows = targetRows;
            gridFlashCount = 0;

            gridFlashTimer = new System.Windows.Forms.Timer();
            gridFlashTimer.Interval = 300;
            gridFlashTimer.Tick += GridFlashTimer_Tick;
            gridFlashTimer.Start();
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
            if (rowIndex >= 0 && rowIndex < dataGridView1.RowCount)
            {
                dataGridView1.Rows[rowIndex].DefaultCellStyle.BackColor = Color.Empty;
            }
        }

        private void FrmDataGrid_Disposed(object sender, EventArgs e)
        {
            if (gridFlashTimer != null)
            {
                gridFlashTimer.Stop();
                gridFlashTimer.Tick -= GridFlashTimer_Tick;
                gridFlashTimer.Dispose();
                gridFlashTimer = null;
            }
            flashTargetRows = null;
        }
        private void Add_Click(object sender, EventArgs e)
        {
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                MessageBox.Show("请先选择流程步骤。");
                return;
            }
            if (SF.frmProc.SelectedProcNum >= SF.frmProc.procsList.Count
                || SF.frmProc.SelectedStepNum >= SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps.Count)
            {
                MessageBox.Show("流程或步骤索引无效，无法新增指令。");
                return;
            }
            if (this.dataGridView1.SelectedRows.Count == 0)
                iSelectedRow = -1;
            OperationTemp = new HomeRun() { Num = iSelectedRow == -1 ? this.dataGridView1.Rows.Count : iSelectedRow + 1 };
            SF.frmPropertyGrid.OperationType.SelectedIndex = 0;
            OperationTemp.RefleshPropertyAlarm();
            SF.frmPropertyGrid.propertyGrid1.SelectedObject = OperationTemp;
            SF.isAddOps = true;
            BeginOperationEditSession(true);
            SF.frmDataGrid.dataGridView1.Enabled = false;
        }

        private void dataGridView1_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dataGridView1.Rows.Count)
            {
                if (e.Button == MouseButtons.Right)
                {
                    iSelectedRow = -1;
                    dataGridView1.ClearSelection();
                }
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                iSelectedRow = e.RowIndex;
                dataGridView1.ClearSelection();
                dataGridView1.Rows[e.RowIndex].Selected = true;
                dataGridView1.CurrentCell = dataGridView1.Rows[e.RowIndex].Cells[0];
                return;
            }

            //输出Ops信息到属性窗体上并输出当前选择行数
            if (e.RowIndex >= 0
                && SF.frmProc.SelectedProcNum >= 0
                && SF.frmProc.SelectedStepNum >= 0
                && SF.frmProc.SelectedProcNum < SF.frmProc.procsList.Count
                && SF.frmProc.SelectedStepNum < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps.Count
                && e.Button == MouseButtons.Left)
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
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control && e.RowIndex >= 0 && e.RowIndex < dataGridView1.Rows.Count)
            {
                dataGridView1.Rows[e.RowIndex].Selected = !dataGridView1.Rows[e.RowIndex].Selected;
            }
            iSelectedRow = e.RowIndex;

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
            if (selectedRowIndexes4Del.Count == 0)
            {
                return;
            }

            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                MessageBox.Show("流程索引无效，无法删除指令。");
                return;
            }
            if (stepIndex < 0 || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
            {
                MessageBox.Show("步骤索引无效，无法删除指令。");
                return;
            }
            Proc proc = SF.frmProc.procsList[procIndex];
            Step step = proc.steps[stepIndex];
            string procName = string.IsNullOrWhiteSpace(proc?.head?.Name) ? $"索引{procIndex}" : proc.head.Name;
            string stepName = string.IsNullOrWhiteSpace(step?.Name) ? $"索引{stepIndex}" : step.Name;
            string warnMsg;
            if (selectedRowIndexes4Del.Count == 1)
            {
                int opIndex = selectedRowIndexes4Del[0];
                OperationType op = step?.Ops != null && opIndex >= 0 && opIndex < step.Ops.Count ? step.Ops[opIndex] : null;
                string opType = op?.OperaType ?? "未知类型";
                string opName = string.IsNullOrWhiteSpace(op?.Name) ? "未命名" : op.Name;
                string opText = $"{opIndex}({opType}) {opName}";
                warnMsg = $"警告：即将删除指令【{opText}】\r\n所属流程：【{procName}】\r\n所属步骤：【{stepName}】\r\n此操作不可恢复，确认删除？";
            }
            else
            {
                warnMsg = $"警告：即将删除{selectedRowIndexes4Del.Count}条指令\r\n所属流程：【{procName}】\r\n所属步骤：【{stepName}】\r\n此操作不可恢复，确认删除？";
            }
            DialogResult confirmResult = MessageBox.Show(
                this,
                warnMsg,
                "删除指令确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmResult != DialogResult.Yes)
            {
                return;
            }

            Proc before = ObjectGraphCloner.Clone(proc);
            Proc draft = ObjectGraphCloner.Clone(proc);
            for (int i = selectedRowIndexes4Del.Count - 1; i >= 0; i--)
            {
                int index = selectedRowIndexes4Del[i];
                if (index >= 0 && index < draft.steps[stepIndex].Ops.Count)
                {
                    draft.steps[stepIndex].Ops.RemoveAt(index);
                }
            }
            ProcessEditingService.RewriteGotoTargets(before, draft, procIndex);
            if (!ProcessEditingService.TryCommitProcDraft(procIndex, draft, out string commitError))
            {
                MessageBox.Show(commitError, "删除指令失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private void Modify_Click(object sender, EventArgs e)
        {
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                MessageBox.Show("请先选择流程步骤。");
                return;
            }
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                MessageBox.Show("流程索引无效，无法编辑指令。");
                return;
            }
            if (stepIndex < 0 || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
            {
                MessageBox.Show("步骤索引无效，无法编辑指令。");
                return;
            }
            int opCount = SF.frmProc.procsList[procIndex].steps[stepIndex].Ops.Count;
            if (iSelectedRow < 0 || iSelectedRow >= opCount)
            {
                MessageBox.Show("请选择需要编辑的指令。");
                return;
            }
            BeginOperationEditSession(false);
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
            if (e.KeyCode == Keys.Delete)
            {
                Delete_Click(sender, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void SetStartOps_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                MessageBox.Show("请先选择流程步骤。");
                return;
            }
            if (SF.frmProc.SelectedProcNum >= SF.frmProc.procsList.Count
                || SF.frmProc.SelectedStepNum >= SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps.Count)
            {
                MessageBox.Show("流程或步骤索引无效，无法设置启动点。");
                return;
            }
            int opCount = SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Count;
            if (iSelectedRow < 0 || iSelectedRow >= opCount)
            {
                MessageBox.Show("请先选择需要设为启动点的指令。");
                return;
            }
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
                null,
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

            if (dataGridView1.DataSource == null)
            {
                return;
            }

            CurrencyManager currencyManager = dataGridView1.BindingContext[dataGridView1.DataSource] as CurrencyManager;
            if (currencyManager == null || e.RowIndex >= currencyManager.Count)
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
                OperationType boundItem = dataGridView1.Rows[selectedRowIndexes4Copy[i]].DataBoundItem as OperationType;
                if (boundItem == null)
                {
                    continue;
                }
                OperationType dataItem = (OperationType)boundItem.Clone();
                dataItem.Num = -1;
                ListOperationType4Copy.Add(dataItem);
            }
        }
        public void Paste()
        {
            if (!TryPasteOperations(ListOperationType4Copy, out int insertIndex, out int insertedCount, out string error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MessageBox.Show(error, "粘贴指令失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }
            HighlightInsertedRows(insertIndex, insertedCount);
        }

        private bool TryPasteOperations(IEnumerable<OperationType> source, out int insertIndex, out int insertedCount, out string error)
        {
            insertIndex = -1;
            insertedCount = 0;
            error = null;
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (procIndex < 0 || stepIndex < 0)
            {
                error = "请先选择流程步骤。";
                return false;
            }
            if (!SF.CanEditProc(procIndex))
            {
                return false;
            }
            if (procIndex >= SF.frmProc.procsList.Count
                || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
            {
                error = "流程或步骤索引无效，无法粘贴指令。";
                return false;
            }
            List<OperationType> copiedOperations = OperationClipboardService.PrepareForPaste(source, procIndex);
            if (copiedOperations == null || copiedOperations.Count == 0)
            {
                return false;
            }
            insertIndex = iSelectedRow + 1;
            Proc current = SF.frmProc.procsList[procIndex];
            int opCount = current.steps[stepIndex].Ops.Count;
            if (insertIndex < 0 || insertIndex > opCount)
            {
                error = "当前指令索引无效，无法粘贴。";
                return false;
            }
            Proc before = ObjectGraphCloner.Clone(current);
            Proc draft = ObjectGraphCloner.Clone(current);
            draft.steps[stepIndex].Ops.InsertRange(insertIndex, copiedOperations);
            ProcessEditingService.RewriteGotoTargets(before, draft, procIndex);
            if (!ProcessEditingService.TryCommitProcDraft(procIndex, draft, out error))
            {
                return false;
            }
            insertedCount = copiedOperations.Count;
            return true;
        }

        private void HighlightInsertedRows(int insertIndex, int insertedCount)
        {
            for (int i = insertIndex; i < insertIndex + insertedCount && i < dataGridView1.Rows.Count; i++)
            {
                dataGridView1.Rows[i].DefaultCellStyle.BackColor = Color.LightGreen;
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
            if (e.Button == MouseButtons.Right)
            {
                contextMenuByMouse = true;
                contextMenuRowIndex = dataGridView1.HitTest(e.X, e.Y).RowIndex;
            }
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
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (!SF.CanEditProc(procIndex))
            {
                dragIndex = -1;
                return;
            }
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count
                || stepIndex < 0 || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
            {
                dragIndex = -1;
                return;
            }
            if (dragIndex >= 0)
            {
                Point p = dataGridView1.PointToClient(new Point(e.X, e.Y));
                int targetIndex = dataGridView1.HitTest(p.X, p.Y).RowIndex;
                Proc current = SF.frmProc.procsList[procIndex];

                if (targetIndex >= 0 && targetIndex < current.steps[stepIndex].Ops.Count
                    && dragIndex < current.steps[stepIndex].Ops.Count && targetIndex != dragIndex)
                {
                    Proc before = ObjectGraphCloner.Clone(current);
                    Proc draft = ObjectGraphCloner.Clone(current);
                    OperationType draggedItem = draft.steps[stepIndex].Ops[dragIndex];
                    draft.steps[stepIndex].Ops.RemoveAt(dragIndex);
                    draft.steps[stepIndex].Ops.Insert(targetIndex, draggedItem);
                    ProcessEditingService.RewriteGotoTargets(before, draft, procIndex);
                    if (!ProcessEditingService.TryCommitProcDraft(procIndex, draft, out string commitError))
                    {
                        MessageBox.Show(commitError, "移动指令失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
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
                if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
                {
                    MessageBox.Show("请先选择流程步骤。");
                    return;
                }
                BeginOperationEditSession(false);
                SF.frmDataGrid.dataGridView1.Enabled = false;
                SF.frmProc.Enabled = false;
            }
        }

        private void BeginOperationEditSession(bool isAdd)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            int selectedRow = iSelectedRow;
            SF.isAddOps = isAdd;
            SF.isModify = isAdd ? ModifyKind.None : ModifyKind.Operation;
            SF.BeginEditSession(new EditSession<OperationType>(isAdd ? "新增指令" : "修改指令", OperationTemp,
                draft =>
                {
                    if (draft == null)
                    {
                        return "指令草稿为空。";
                    }
                    Proc proc = procIndex >= 0 && procIndex < SF.frmProc.procsList.Count
                        ? SF.frmProc.procsList[procIndex]
                        : null;
                    return ProcessDefinitionService.TryValidateOperationGoto(draft, procIndex, proc, out string error)
                        ? null
                        : error;
                },
                draft =>
                {
                    if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count
                        || stepIndex < 0 || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
                    {
                        throw new InvalidOperationException("流程或步骤索引已失效。");
                    }
                    Proc before = ObjectGraphCloner.Clone(SF.frmProc.procsList[procIndex]);
                    Proc procDraft = ObjectGraphCloner.Clone(SF.frmProc.procsList[procIndex]);
                    Step step = procDraft.steps[stepIndex];
                    int targetIndex;
                    if (isAdd)
                    {
                        draft.Id = Guid.NewGuid();
                        targetIndex = selectedRow < 0 ? step.Ops.Count : selectedRow + 1;
                        step.Ops.Insert(targetIndex, draft);
                    }
                    else
                    {
                        if (selectedRow < 0 || selectedRow >= step.Ops.Count)
                        {
                            throw new InvalidOperationException("指令索引已失效。");
                        }
                        OperationType original = step.Ops[selectedRow];
                        draft.Id = original?.Id != Guid.Empty ? original.Id : Guid.NewGuid();
                        step.Ops[selectedRow] = draft;
                        targetIndex = selectedRow;
                    }
                    ProcessEditingService.RewriteGotoTargets(before, procDraft, procIndex);
                    if (!ProcessEditingService.TryCommitProcDraft(procIndex, procDraft, out string commitError))
                    {
                        throw new InvalidOperationException(commitError);
                    }
                    OperationTemp = (OperationType)SF.frmProc.procsList[procIndex].steps[stepIndex].Ops[targetIndex].Clone();
                    iSelectedRow = targetIndex;
                    dataGridView1.Enabled = true;
                    SF.frmProc.Enabled = true;
                    SF.isAddOps = false;
                    SF.isModify = ModifyKind.None;
                },
                () =>
                {
                    OperationTemp = null;
                    dataGridView1.Enabled = true;
                    SF.frmProc.Enabled = true;
                    SF.isAddOps = false;
                    SF.isModify = ModifyKind.None;
                }));
        }

        private void Enable_Click(object sender, EventArgs e)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (iSelectedRow >= 0 && procIndex >= 0 && stepIndex >= 0)
            {
                if (!SF.CanEditProc(procIndex)
                    || procIndex >= SF.frmProc.procsList.Count
                    || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count
                    || iSelectedRow >= SF.frmProc.procsList[procIndex].steps[stepIndex].Ops.Count)
                {
                    return;
                }
                Proc draft = ObjectGraphCloner.Clone(SF.frmProc.procsList[procIndex]);
                OperationType operation = draft.steps[stepIndex].Ops[iSelectedRow];
                if (operation != null)
                {
                    operation.Disable = !operation.Disable;
                    if (!ProcessEditingService.TryCommitProcDraft(procIndex, draft, out string commitError))
                    {
                        MessageBox.Show(commitError, "更新指令状态失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
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

                string json = OperationClipboardService.Serialize(ListOperationType4Copy);
                Clipboard.SetData(OperationClipboardService.Format, json);
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
                if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
                {
                    MessageBox.Show("请先选择流程步骤。");
                    return;
                }
                if (SF.frmProc.SelectedProcNum >= SF.frmProc.procsList.Count
                    || SF.frmProc.SelectedStepNum >= SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps.Count)
                {
                    MessageBox.Show("流程或步骤索引无效，无法粘贴指令。");
                    return;
                }
                List<OperationType> deepCopy = null;
                if (Clipboard.ContainsData(OperationClipboardService.Format))
                {
                    string json = Clipboard.GetData(OperationClipboardService.Format) as string;
                    deepCopy = OperationClipboardService.Deserialize(json);
                }
                if (deepCopy == null)
                {
                    return;
                }
                if (!TryPasteOperations(deepCopy, out int insertIndex, out int insertedCount, out string error))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        MessageBox.Show(error, "粘贴指令失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return;
                }
                HighlightInsertedRows(insertIndex, insertedCount);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

    }
}
