using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmInfo : Form
    {
        private readonly List<ProcStatusCellCache> statusCellCache = new List<ProcStatusCellCache>();
        private System.Windows.Forms.Timer statusTimer;
        private bool statusPageActive;
        private bool statusPageInitialized;
        private int statusGroupCount = 1;
        private int lastStatusProcCount = -1;
        private const int StatusColumnsPerGroup = 4;
        private const int StatusMinGroupWidth = 320;
        private const int MaxInfoLogEntries = 200;
        private const int InfoAutoScrollIdleMs = 20000;
        private ContextMenuStrip infoMenu;
        private ToolStripMenuItem menuClearInfo;
        private readonly Queue<int> infoEntryLengths = new Queue<int>();
        private System.Windows.Forms.Timer infoAutoScrollTimer;
        private bool infoAutoScrollPausedByUser;
        private DateTime infoLastInteractionUtc;

        public FrmInfo()
        {
            InitializeComponent();
        }

        private void FrmInfo_Load(object sender, EventArgs e)
        {
            InitializeStatusPage();
            InitializeInfoMenu();
            InitializeInfoStreamBehavior();
        }

        private void btnClearInfo_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确认清空信息记录？", "清空确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            ReceiveTextBox.Clear();
            infoEntryLengths.Clear();
            infoAutoScrollPausedByUser = false;
        }

        private void InitializeInfoMenu()
        {
            if (infoMenu != null)
            {
                return;
            }
            infoMenu = new ContextMenuStrip();
            menuClearInfo = new ToolStripMenuItem("清空");
            menuClearInfo.Click += btnClearInfo_Click;
            infoMenu.Items.Add(menuClearInfo);
            ReceiveTextBox.ContextMenuStrip = infoMenu;
        }

        private void InitializeInfoStreamBehavior()
        {
            if (infoAutoScrollTimer != null)
            {
                return;
            }
            ReceiveTextBox.VScroll += ReceiveTextBox_VScroll;
            ReceiveTextBox.MouseWheel += ReceiveTextBox_MouseWheel;
            ReceiveTextBox.MouseDown += ReceiveTextBox_MouseDown;
            ReceiveTextBox.KeyDown += ReceiveTextBox_KeyDown;

            infoAutoScrollTimer = new System.Windows.Forms.Timer();
            infoAutoScrollTimer.Interval = 500;
            infoAutoScrollTimer.Tick += InfoAutoScrollTimer_Tick;
            infoAutoScrollTimer.Start();
        }

        private void ReceiveTextBox_VScroll(object sender, EventArgs e)
        {
            OnInfoStreamUserInteraction();
        }

        private void ReceiveTextBox_MouseWheel(object sender, MouseEventArgs e)
        {
            OnInfoStreamUserInteraction();
        }

        private void ReceiveTextBox_MouseDown(object sender, MouseEventArgs e)
        {
            OnInfoStreamUserInteraction();
        }

        private void ReceiveTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            OnInfoStreamUserInteraction();
        }

        private void OnInfoStreamUserInteraction()
        {
            infoLastInteractionUtc = DateTime.UtcNow;
            infoAutoScrollPausedByUser = !IsInfoLogEndVisible();
        }

        private void InfoAutoScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!infoAutoScrollPausedByUser)
            {
                return;
            }
            if ((DateTime.UtcNow - infoLastInteractionUtc).TotalMilliseconds < InfoAutoScrollIdleMs)
            {
                return;
            }
            infoAutoScrollPausedByUser = false;
            ScrollInfoToBottom();
        }

        private bool IsInfoLogEndVisible()
        {
            if (ReceiveTextBox.TextLength == 0)
            {
                return true;
            }
            int index = ReceiveTextBox.TextLength - 1;
            while (index > 0)
            {
                char c = ReceiveTextBox.Text[index];
                if (c != '\r' && c != '\n')
                {
                    break;
                }
                index--;
            }
            Point endPoint = ReceiveTextBox.GetPositionFromCharIndex(index);
            return endPoint.Y >= 0 && endPoint.Y <= ReceiveTextBox.ClientSize.Height - ReceiveTextBox.Font.Height;
        }

        private void ScrollInfoToBottom()
        {
            ReceiveTextBox.Select(ReceiveTextBox.TextLength, 0);
            ReceiveTextBox.ScrollToCaret();
        }

        private void TrimInfoEntries()
        {
            while (infoEntryLengths.Count > MaxInfoLogEntries)
            {
                int removeLength = infoEntryLengths.Dequeue();
                if (removeLength <= 0 || ReceiveTextBox.TextLength <= 0)
                {
                    continue;
                }
                int safeLength = Math.Min(removeLength, ReceiveTextBox.TextLength);
                ReceiveTextBox.Select(0, safeLength);
                ReceiveTextBox.SelectedText = string.Empty;
            }
        }


        [Browsable(false)]
        [JsonIgnore]
        public Level level { get; set; } = 0;

        public Level GetState()
        {
            return level;
        }
        public void SetState(Level level)
        {
            this.level = level;
        }
        public enum Level
        {
            Error = 0,
            Normal,
        }
        // InfoLevel 信息级别
        // 0 红色报警
        // 1 普通信息
        public void PrintInfo(string str, Level InfoLevel)
        {
            if (SF.frmInfo.IsDisposed)
                return;
            Invoke(new Action(() =>
            {
                int length = ReceiveTextBox.TextLength;
                string prefix = $"[{DateTime.Now.ToString("yyyy-MM-dd HH时mm分ss秒")}]";
                string line = $"{prefix}：{str}\r\n";
                ReceiveTextBox.AppendText(line);

                Color color = Color.Black;
                if(InfoLevel == Level.Error)
                {
                    color = Color.Red;
                }
                else if(InfoLevel == Level.Normal)
                {
                    color = Color.BurlyWood;
                }
                ReceiveTextBox.Select(length, prefix.Length);
                ReceiveTextBox.SelectionBackColor = color;
                infoEntryLengths.Enqueue(line.Length);
                TrimInfoEntries();
                if (!infoAutoScrollPausedByUser)
                {
                    ScrollInfoToBottom();
                }
            }));
        }

        private void InitializeStatusPage()
        {
            if (statusPageInitialized)
            {
                return;
            }
            statusPageInitialized = true;
            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = 300;
            statusTimer.Tick += StatusTimer_Tick;
            tabControl1.SelectedIndexChanged += tabControl1_SelectedIndexChanged;
            VisibleChanged += FrmInfo_VisibleChanged;
            dgvProcStatus.CellDoubleClick += dgvProcStatus_CellDoubleClick;
            dgvProcStatus.SizeChanged += dgvProcStatus_SizeChanged;
            RebuildStatusColumns(GetStatusGroupCount(0));
            UpdateStatusTimerState();
        }

        private void FrmInfo_VisibleChanged(object sender, EventArgs e)
        {
            UpdateStatusTimerState();
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateStatusTimerState();
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (!statusPageActive)
            {
                return;
            }
            RefreshProcStatus();
        }

        private void dgvProcStatus_SizeChanged(object sender, EventArgs e)
        {
            if (!statusPageActive)
            {
                return;
            }
            RefreshProcStatus();
        }

        private void UpdateStatusTimerState()
        {
            if (statusTimer == null)
            {
                return;
            }
            bool shouldRun = IsStatusPageVisible();
            if (statusPageActive == shouldRun)
            {
                return;
            }
            statusPageActive = shouldRun;
            if (statusPageActive)
            {
                RefreshProcStatus();
                statusTimer.Start();
            }
            else
            {
                statusTimer.Stop();
            }
        }

        private bool IsStatusPageVisible()
        {
            return Visible && tabControl1.SelectedTab == tabPageStatus;
        }

        private void RefreshProcStatus()
        {
            bool layoutSuspended = false;
            try
            {
                if (IsDisposed || dgvProcStatus == null || dgvProcStatus.IsDisposed)
                {
                    return;
                }
                if (SF.DR == null)
                {
                    ClearStatusRows();
                    return;
                }
                IReadOnlyList<EngineSnapshot> snapshots = SF.DR.GetSnapshots();
                if (snapshots == null)
                {
                    ClearStatusRows();
                    return;
                }
                int procCount = snapshots.Count;
                int groupCount = GetStatusGroupCount(procCount);
                bool layoutChanged = groupCount != statusGroupCount;
                bool countChanged = procCount != lastStatusProcCount;
                if (layoutChanged)
                {
                    statusGroupCount = groupCount;
                    RebuildStatusColumns(groupCount);
                }
                dgvProcStatus.SuspendLayout();
                layoutSuspended = true;
                int rowCount = GetRowCount(procCount, groupCount);
                EnsureStatusRowCount(rowCount);
                if (layoutChanged || countChanged)
                {
                    ResetStatusCellCache(procCount);
                    ClearStatusCells();
                }
                else
                {
                    EnsureStatusCellCache(procCount);
                }
                for (int i = 0; i < procCount; i++)
                {
                    UpdateStatusCell(i, snapshots[i]);
                }
                lastStatusProcCount = procCount;
            }
            catch (Exception ex)
            {
                PrintInfo($"流程状态刷新失败：{ex.Message}", Level.Error);
                if (statusTimer != null)
                {
                    statusTimer.Stop();
                }
                statusPageActive = false;
            }
            finally
            {
                if (layoutSuspended)
                {
                    dgvProcStatus.ResumeLayout();
                }
            }
        }

        private int GetRowCount(int procCount, int groupCount)
        {
            if (groupCount <= 0)
            {
                throw new InvalidOperationException("流程状态列布局异常");
            }
            if (procCount <= 0)
            {
                return 0;
            }
            return (procCount + groupCount - 1) / groupCount;
        }

        private int GetStatusGroupCount(int procCount)
        {
            int width = dgvProcStatus.ClientSize.Width;
            if (width <= 0)
            {
                return 1;
            }
            int groupCount = Math.Max(1, width / StatusMinGroupWidth);
            if (procCount > 0)
            {
                groupCount = Math.Min(groupCount, procCount);
            }
            return Math.Max(1, groupCount);
        }

        private void RebuildStatusColumns(int groupCount)
        {
            if (groupCount <= 0)
            {
                throw new InvalidOperationException("流程状态列布局异常");
            }
            dgvProcStatus.Columns.Clear();
            for (int i = 0; i < groupCount; i++)
            {
                AddStatusColumn(i, StatusColumnKind.Proc, "流程", 25F, DataGridViewContentAlignment.MiddleLeft);
                AddStatusColumn(i, StatusColumnKind.State, "状态", 15F, DataGridViewContentAlignment.MiddleCenter);
                AddStatusColumn(i, StatusColumnKind.Position, "位置", 20F, DataGridViewContentAlignment.MiddleCenter);
                AddStatusColumn(i, StatusColumnKind.OpName, "指令", 40F, DataGridViewContentAlignment.MiddleLeft);
            }
        }

        private void AddStatusColumn(int groupIndex, StatusColumnKind kind, string headerText, float fillWeight,
            DataGridViewContentAlignment alignment)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.ReadOnly = true;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.HeaderText = headerText;
            column.FillWeight = fillWeight;
            column.Tag = new StatusColumnTag(groupIndex, kind);
            column.DefaultCellStyle.Alignment = alignment;
            if (kind != StatusColumnKind.State)
            {
                column.DefaultCellStyle.BackColor = GetStatusGroupBackColor(groupIndex);
            }
            if (kind == StatusColumnKind.OpName)
            {
                column.DividerWidth = 3;
            }
            dgvProcStatus.Columns.Add(column);
        }

        private Color GetStatusGroupBackColor(int groupIndex)
        {
            if (groupIndex % 2 == 0)
            {
                return Color.FromArgb(245, 248, 255);
            }
            return Color.FromArgb(245, 255, 245);
        }

        private void EnsureStatusRowCount(int targetCount)
        {
            if (targetCount < 0)
            {
                throw new InvalidOperationException("流程状态行数异常");
            }
            while (dgvProcStatus.Rows.Count < targetCount)
            {
                dgvProcStatus.Rows.Add();
            }
            while (dgvProcStatus.Rows.Count > targetCount)
            {
                int lastIndex = dgvProcStatus.Rows.Count - 1;
                dgvProcStatus.Rows.RemoveAt(lastIndex);
            }
        }

        private void ResetStatusCellCache(int procCount)
        {
            statusCellCache.Clear();
            for (int i = 0; i < procCount; i++)
            {
                statusCellCache.Add(new ProcStatusCellCache());
            }
        }

        private void EnsureStatusCellCache(int procCount)
        {
            while (statusCellCache.Count < procCount)
            {
                statusCellCache.Add(new ProcStatusCellCache());
            }
            while (statusCellCache.Count > procCount)
            {
                statusCellCache.RemoveAt(statusCellCache.Count - 1);
            }
        }

        private void ClearStatusRows()
        {
            if (dgvProcStatus.Rows.Count == 0)
            {
                return;
            }
            dgvProcStatus.Rows.Clear();
            statusCellCache.Clear();
            lastStatusProcCount = -1;
        }

        private void ClearStatusCells()
        {
            if (dgvProcStatus.Rows.Count == 0 || dgvProcStatus.Columns.Count == 0)
            {
                return;
            }
            foreach (DataGridViewRow row in dgvProcStatus.Rows)
            {
                foreach (DataGridViewCell cell in row.Cells)
                {
                    cell.Value = null;
                    cell.Style.ForeColor = Color.Empty;
                    cell.Style.SelectionForeColor = Color.Empty;
                    cell.Style.BackColor = Color.Empty;
                    cell.Style.SelectionBackColor = Color.Empty;
                }
            }
        }

        private void UpdateStatusCell(int procIndex, EngineSnapshot snapshot)
        {
            if (snapshot == null || procIndex < 0 || procIndex >= statusCellCache.Count)
            {
                return;
            }
            int groupIndex = statusGroupCount <= 0 ? 0 : procIndex % statusGroupCount;
            int rowIndex = statusGroupCount <= 0 ? 0 : procIndex / statusGroupCount;
            int baseColumn = groupIndex * StatusColumnsPerGroup;
            if (rowIndex < 0 || rowIndex >= dgvProcStatus.Rows.Count)
            {
                return;
            }
            if (baseColumn < 0 || baseColumn + StatusColumnsPerGroup - 1 >= dgvProcStatus.Columns.Count)
            {
                return;
            }
            DataGridViewRow row = dgvProcStatus.Rows[rowIndex];
            ProcStatusCellCache cache = statusCellCache[procIndex];

            string procName = GetProcDisplayName(snapshot.ProcIndex, snapshot.ProcName);
            string stateText = GetStateText(snapshot.State);
            string positionText = GetPositionText(snapshot);
            string opName = GetOpName(snapshot.ProcIndex, snapshot.StepIndex, snapshot.OpIndex);
            Color stateColor = GetStateColor(snapshot.State);
            Color stateBackColor = GetStateBackColor(snapshot.State);

            if (!string.Equals(cache.ProcName, procName, StringComparison.Ordinal))
            {
                row.Cells[baseColumn + 0].Value = procName;
                cache.ProcName = procName;
            }
            if (!string.Equals(cache.StateText, stateText, StringComparison.Ordinal))
            {
                row.Cells[baseColumn + 1].Value = stateText;
                cache.StateText = stateText;
            }
            if (!string.Equals(cache.PositionText, positionText, StringComparison.Ordinal))
            {
                row.Cells[baseColumn + 2].Value = positionText;
                cache.PositionText = positionText;
            }
            if (!string.Equals(cache.OpName, opName, StringComparison.Ordinal))
            {
                row.Cells[baseColumn + 3].Value = opName;
                cache.OpName = opName;
            }
            if (cache.StateColor != stateColor)
            {
                row.Cells[baseColumn + 1].Style.ForeColor = stateColor;
                row.Cells[baseColumn + 1].Style.SelectionForeColor = stateColor;
                cache.StateColor = stateColor;
            }
            if (cache.StateBackColor != stateBackColor)
            {
                row.Cells[baseColumn + 1].Style.BackColor = stateBackColor;
                row.Cells[baseColumn + 1].Style.SelectionBackColor = stateBackColor;
                cache.StateBackColor = stateBackColor;
            }

            cache.ProcIndex = snapshot.ProcIndex;
            cache.StepIndex = snapshot.StepIndex;
            cache.OpIndex = snapshot.OpIndex;
        }

        private string GetProcDisplayName(int procIndex, string snapshotName)
        {
            string procName = snapshotName;
            if (string.IsNullOrWhiteSpace(procName) && SF.frmProc?.procsList != null && procIndex >= 0
                && procIndex < SF.frmProc.procsList.Count)
            {
                procName = SF.frmProc.procsList[procIndex]?.head?.Name;
            }
            if (string.IsNullOrWhiteSpace(procName))
            {
                procName = $"索引{procIndex}";
            }
            return procName;
        }

        private string GetStateText(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Stopped:
                    return "停止";
                case ProcRunState.Paused:
                    return "暂停";
                case ProcRunState.SingleStep:
                    return "单步";
                case ProcRunState.Running:
                    return "运行";
                case ProcRunState.Alarming:
                    return "报警中";
                default:
                    return "未知";
            }
        }

        private Color GetStateColor(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Running:
                    return Color.ForestGreen;
                case ProcRunState.Paused:
                case ProcRunState.SingleStep:
                    return Color.DarkOrange;
                case ProcRunState.Alarming:
                    return Color.Red;
                case ProcRunState.Stopped:
                    return Color.DimGray;
                default:
                    return Color.DimGray;
            }
        }

        private Color GetStateBackColor(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Running:
                    return Color.FromArgb(220, 245, 228);
                case ProcRunState.Paused:
                case ProcRunState.SingleStep:
                    return Color.FromArgb(255, 236, 208);
                case ProcRunState.Alarming:
                    return Color.FromArgb(255, 214, 214);
                case ProcRunState.Stopped:
                    return Color.FromArgb(238, 238, 238);
                default:
                    return Color.FromArgb(238, 238, 238);
            }
        }

        private string GetPositionText(EngineSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "-";
            }
            if (snapshot.StepIndex < 0 || snapshot.OpIndex < 0)
            {
                return "-";
            }
            return $"{snapshot.ProcIndex}-{snapshot.StepIndex}-{snapshot.OpIndex}";
        }

        private string GetOpName(int procIndex, int stepIndex, int opIndex)
        {
            if (procIndex < 0 || stepIndex < 0 || opIndex < 0)
            {
                return "-";
            }
            if (SF.frmProc?.procsList == null || procIndex >= SF.frmProc.procsList.Count)
            {
                return "-";
            }
            Proc proc = SF.frmProc.procsList[procIndex];
            if (proc?.steps == null || stepIndex >= proc.steps.Count)
            {
                return "-";
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex >= step.Ops.Count)
            {
                return "-";
            }
            OperationType op = step.Ops[opIndex];
            if (op == null)
            {
                return "-";
            }
            if (!string.IsNullOrWhiteSpace(op.Name))
            {
                return op.Name;
            }
            if (!string.IsNullOrWhiteSpace(op.OperaType))
            {
                return op.OperaType;
            }
            return "未命名";
        }

        private void dgvProcStatus_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }
            StatusColumnTag tag = dgvProcStatus.Columns[e.ColumnIndex].Tag as StatusColumnTag;
            if (tag == null || tag.Kind != StatusColumnKind.Position)
            {
                return;
            }
            if (statusGroupCount <= 0)
            {
                return;
            }
            int procIndex = e.RowIndex * statusGroupCount + tag.GroupIndex;
            if (procIndex < 0 || procIndex >= statusCellCache.Count)
            {
                PrintInfo("当前位置数据无效，无法跳转。", Level.Error);
                return;
            }
            ProcStatusCellCache cache = statusCellCache[procIndex];
            JumpToOperation(cache.ProcIndex, cache.StepIndex, cache.OpIndex);
        }

        private void JumpToOperation(int procIndex, int stepIndex, int opIndex)
        {
            if (procIndex < 0 || stepIndex < 0 || opIndex < 0)
            {
                PrintInfo("当前位置无效，无法跳转。", Level.Error);
                return;
            }
            if (SF.isAddOps || SF.isModify != ModifyKind.None)
            {
                PrintInfo("当前处于编辑状态，禁止跳转。", Level.Error);
                return;
            }
            if (SF.frmMenu == null || SF.frmProc == null || SF.frmDataGrid == null || SF.frmPropertyGrid == null)
            {
                PrintInfo("流程界面未就绪，无法跳转。", Level.Error);
                return;
            }
            if (SF.frmProc.procsList == null || procIndex >= SF.frmProc.procsList.Count)
            {
                PrintInfo("流程索引超出范围，无法跳转。", Level.Error);
                return;
            }
            Proc proc = SF.frmProc.procsList[procIndex];
            if (proc?.steps == null || stepIndex >= proc.steps.Count)
            {
                PrintInfo("步骤索引超出范围，无法跳转。", Level.Error);
                return;
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex >= step.Ops.Count)
            {
                PrintInfo("指令索引超出范围，无法跳转。", Level.Error);
                return;
            }

            TreeView tree = SF.frmProc.proc_treeView;
            if (tree == null || procIndex >= tree.Nodes.Count || stepIndex >= tree.Nodes[procIndex].Nodes.Count)
            {
                PrintInfo("流程树未就绪，无法跳转。", Level.Error);
                return;
            }
            tree.SelectedNode = tree.Nodes[procIndex].Nodes[stepIndex];
            if (SF.frmProc.SelectedProcNum != procIndex || SF.frmProc.SelectedStepNum != stepIndex)
            {
                PrintInfo("流程选择被阻止，无法跳转。", Level.Error);
                return;
            }

            if (!TrySelectOperationInGrid(opIndex))
            {
                PrintInfo("指令行未就绪，无法跳转。", Level.Error);
            }
        }

        private bool TrySelectOperationInGrid(int opIndex)
        {
            DataGridView grid = SF.frmDataGrid.dataGridView1;
            if (grid == null || opIndex < 0 || opIndex >= grid.RowCount)
            {
                return false;
            }
            grid.ClearSelection();
            grid.Rows[opIndex].Selected = true;
            SF.frmDataGrid.iSelectedRow = opIndex;
            grid.CurrentCell = grid.Rows[opIndex].Cells[0];
            SF.frmDataGrid.ScrollRowToCenter(opIndex);

            if (SF.frmProc?.procsList == null)
            {
                return true;
            }
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (procIndex < 0 || stepIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                return true;
            }
            Proc proc = SF.frmProc.procsList[procIndex];
            if (proc?.steps == null || stepIndex >= proc.steps.Count)
            {
                return true;
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex >= step.Ops.Count)
            {
                return true;
            }
            OperationType op = step.Ops[opIndex];
            if (op == null)
            {
                return true;
            }
            SF.frmDataGrid.OperationTemp = (OperationType)op.Clone();
            SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
            SF.frmDataGrid.OperationTemp.evtRP();
            SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmPropertyGrid.propertyGrid1.SelectedObject;
            string selectedValue = op.OperaType;
            if (!string.IsNullOrWhiteSpace(selectedValue))
            {
                foreach (OperationType item in SF.frmPropertyGrid.OperationType.Items)
                {
                    if (item.OperaType.ToString() == selectedValue)
                    {
                        SF.frmPropertyGrid.OperationType.SelectedItem = item;
                        break;
                    }
                }
            }
            return true;
        }

        private sealed class ProcStatusCellCache
        {
            public int ProcIndex = -1;
            public int StepIndex = -1;
            public int OpIndex = -1;
            public string ProcName;
            public string StateText;
            public string PositionText;
            public string OpName;
            public Color StateColor = Color.Empty;
            public Color StateBackColor = Color.Empty;
        }

        private enum StatusColumnKind
        {
            Proc = 0,
            State = 1,
            Position = 2,
            OpName = 3
        }

        private sealed class StatusColumnTag
        {
            public StatusColumnTag(int groupIndex, StatusColumnKind kind)
            {
                GroupIndex = groupIndex;
                Kind = kind;
            }

            public int GroupIndex { get; }
            public StatusColumnKind Kind { get; }
        }
    }
}
