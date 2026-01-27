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
        private readonly List<ProcStatusRowCache> statusRowCache = new List<ProcStatusRowCache>();
        private System.Windows.Forms.Timer statusTimer;
        private bool statusPageActive;
        private bool statusPageInitialized;

        public FrmInfo()
        {
            InitializeComponent();
        }

        private void FrmInfo_Load(object sender, EventArgs e)
        {
            InitializeStatusPage();
        }

        private void btnClearInfo_Click(object sender, EventArgs e)
        {
            ReceiveTextBox.Clear();
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
                str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH时mm分ss秒")}]：{str}\r\n";
                ReceiveTextBox.AppendText(str);

                Color color = Color.Black;
                if(InfoLevel == Level.Error)
                {
                    color = Color.Red;
                }
                else if(InfoLevel == Level.Normal)
                {
                    color = Color.BurlyWood;
                }
                ReceiveTextBox.Select(length, str.Length);
                ReceiveTextBox.SelectionBackColor = color;
                ReceiveTextBox.ScrollToCaret();
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
                dgvProcStatus.SuspendLayout();
                layoutSuspended = true;
                EnsureStatusRowCount(snapshots.Count);
                for (int i = 0; i < snapshots.Count; i++)
                {
                    UpdateStatusRow(i, snapshots[i]);
                }
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

        private void EnsureStatusRowCount(int targetCount)
        {
            if (targetCount < 0)
            {
                throw new InvalidOperationException("流程状态行数异常");
            }
            while (dgvProcStatus.Rows.Count < targetCount)
            {
                int rowIndex = dgvProcStatus.Rows.Add();
                DataGridViewRow row = dgvProcStatus.Rows[rowIndex];
                ProcStatusRowCache cache = new ProcStatusRowCache();
                statusRowCache.Add(cache);
                row.Tag = cache;
            }
            while (dgvProcStatus.Rows.Count > targetCount)
            {
                int lastIndex = dgvProcStatus.Rows.Count - 1;
                dgvProcStatus.Rows.RemoveAt(lastIndex);
                statusRowCache.RemoveAt(lastIndex);
            }
        }

        private void ClearStatusRows()
        {
            if (dgvProcStatus.Rows.Count == 0)
            {
                return;
            }
            dgvProcStatus.Rows.Clear();
            statusRowCache.Clear();
        }

        private void UpdateStatusRow(int rowIndex, EngineSnapshot snapshot)
        {
            if (snapshot == null || rowIndex < 0 || rowIndex >= dgvProcStatus.Rows.Count)
            {
                return;
            }
            DataGridViewRow row = dgvProcStatus.Rows[rowIndex];
            ProcStatusRowCache cache = statusRowCache[rowIndex];

            string procName = GetProcDisplayName(snapshot.ProcIndex, snapshot.ProcName);
            string stateText = GetStateText(snapshot.State);
            string positionText = GetPositionText(snapshot);
            string opName = GetOpName(snapshot.ProcIndex, snapshot.StepIndex, snapshot.OpIndex);
            Color stateColor = GetStateColor(snapshot.State);

            if (!string.Equals(cache.ProcName, procName, StringComparison.Ordinal))
            {
                row.Cells[colProc.Index].Value = procName;
                cache.ProcName = procName;
            }
            if (!string.Equals(cache.StateText, stateText, StringComparison.Ordinal))
            {
                row.Cells[colState.Index].Value = stateText;
                cache.StateText = stateText;
            }
            if (!string.Equals(cache.PositionText, positionText, StringComparison.Ordinal))
            {
                row.Cells[colPosition.Index].Value = positionText;
                cache.PositionText = positionText;
            }
            if (!string.Equals(cache.OpName, opName, StringComparison.Ordinal))
            {
                row.Cells[colOpName.Index].Value = opName;
                cache.OpName = opName;
            }
            if (cache.StateColor != stateColor)
            {
                row.Cells[colState.Index].Style.ForeColor = stateColor;
                cache.StateColor = stateColor;
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
            if (dgvProcStatus.Columns[e.ColumnIndex] != colPosition)
            {
                return;
            }
            ProcStatusRowCache cache = dgvProcStatus.Rows[e.RowIndex].Tag as ProcStatusRowCache;
            if (cache == null)
            {
                PrintInfo("当前位置数据无效，无法跳转。", Level.Error);
                return;
            }
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

        private sealed class ProcStatusRowCache
        {
            public int ProcIndex = -1;
            public int StepIndex = -1;
            public int OpIndex = -1;
            public string ProcName;
            public string StateText;
            public string PositionText;
            public string OpName;
            public Color StateColor = Color.Empty;
        }
    }
}
