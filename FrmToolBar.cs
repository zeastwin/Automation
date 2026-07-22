using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinFormsButton = System.Windows.Forms.Button;
using static Automation.OperationTypePartial;
using static System.Collections.Specialized.BitVector32;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace Automation
{
    public partial class FrmToolBar : Form
    {
        private readonly List<int> separatorPositions = new List<int>();
        private readonly System.Windows.Forms.ToolTip toolbarToolTip = new System.Windows.Forms.ToolTip();
        private readonly UiHoverAnimator hoverAnimator = new UiHoverAnimator();
        private readonly WinFormsButton btnNavigateBack = new WinFormsButton();
        private readonly WinFormsButton btnNavigateForward = new WinFormsButton();
        private readonly WinFormsButton btnUndo = new WinFormsButton { Text = "撤销" };
        private readonly WinFormsButton btnRedo = new WinFormsButton { Text = "重做" };
        private readonly WinFormsButton btnDataBreakpoints = new WinFormsButton();
        private readonly WinFormsButton btnFlowGraph = new WinFormsButton();
        private readonly WinFormsButton btnPerformanceAnalysis = new WinFormsButton();

        public FrmToolBar()
        {
            InitializeComponent();
            ToolBar_Panel.Controls.Add(btnNavigateBack);
            ToolBar_Panel.Controls.Add(btnNavigateForward);
            ToolBar_Panel.Controls.Add(btnUndo);
            ToolBar_Panel.Controls.Add(btnRedo);
            ToolBar_Panel.Controls.Add(btnDataBreakpoints);
            ToolBar_Panel.Controls.Add(btnFlowGraph);
            ToolBar_Panel.Controls.Add(btnPerformanceAnalysis);
            btnNavigateBack.Click += (sender, args) => Workspace.Main?.NavigateEditorBack();
            btnNavigateForward.Click += (sender, args) => Workspace.Main?.NavigateEditorForward();
            btnUndo.Click += (sender, args) => ExecuteHistoryAction(true);
            btnRedo.Click += (sender, args) => ExecuteHistoryAction(false);
            btnDataBreakpoints.Click += (sender, args) => Workspace.Main?.ShowDataBreakpoints();
            btnFlowGraph.Click += (sender, args) => Workspace.Main?.ShowProcessFlowGraph();
            btnPerformanceAnalysis.Click += (sender, args) => Workspace.Main?.ShowPerformanceAnalysis();
            ConfigureToolbarAppearance();
            btnSave.Enabled = false;
            btnCancel.Enabled = false;
            btnNavigateBack.Enabled = false;
            btnNavigateForward.Enabled = false;
            btnUndo.Enabled = false;
            btnRedo.Enabled = false;
            btnIOMonitor.Visible = false;
            Disposed += (sender, args) =>
            {
                if (editorWorkspace != null)
                {
                    editorWorkspace.Runtime.Editor.History.StateChanged -= EditorHistory_StateChanged;
                }
                hoverAnimator.Dispose();
                toolbarToolTip.Dispose();
            };
        }

        internal void OnEditorWorkspaceAttached()
        {
            Workspace.Runtime.Editor.History.StateChanged += EditorHistory_StateChanged;
            RefreshHistoryAvailability();
        }

        private void ConfigureToolbarAppearance()
        {
            ToolBar_Panel.BackColor = UiPalette.Surface;
            BackColor = UiPalette.Surface;

            ConfigureButton(btnNavigateBack, UiIconKind.NavigateBack, 42, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnNavigateForward, UiIconKind.NavigateForward, 42, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnUndo, UiIconKind.Undo, 42, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnRedo, UiIconKind.Redo, 42, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnPause, UiIconKind.Pause, 44, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnStop, UiIconKind.Stop, 44, UiPalette.Danger, UiPalette.Surface, UiPalette.DangerSoft);
            ConfigureButton(SingleRun, UiIconKind.Step, 44, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnLocate, UiIconKind.Locate, 44, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnDataBreakpoints, UiIconKind.Breakpoint, 44, UiPalette.Breakpoint, UiPalette.Surface, UiPalette.BreakpointSoft);
            ConfigureButton(btnAlarm, UiIconKind.Alarm, 44, UiPalette.Warning, UiPalette.Surface, UiPalette.WarningSoft);
            ConfigureButton(btnSearch, UiIconKind.Search, 44, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnFlowGraph, UiIconKind.Process, 44, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnPerformanceAnalysis, UiIconKind.Monitor, 44, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnIOMonitor, UiIconKind.Monitor, 44, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(button1, UiIconKind.Folder, 44, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnAppConfig, UiIconKind.Settings, 44, UiPalette.TextPrimary, UiPalette.Surface, UiPalette.SurfaceHover);
            ConfigureButton(btnStopAll, UiIconKind.StopAll, 110, UiPalette.DangerHover, UiPalette.DangerSoft, UiPalette.DangerSoft);
            ConfigureIconOnlyButton(btnSearch, "查找");
            ConfigureIconOnlyButton(btnNavigateBack, "后退（鼠标侧键后退）");
            ConfigureIconOnlyButton(btnNavigateForward, "前进（鼠标侧键前进）");
            ConfigureIconOnlyButton(btnUndo, "撤销（Ctrl+Z）");
            ConfigureIconOnlyButton(btnRedo, "重做（Ctrl+Y）");
            ConfigureIconOnlyButton(btnPause, "暂停");
            ConfigureIconOnlyButton(btnStop, "停止");
            ConfigureIconOnlyButton(SingleRun, "单步");
            ConfigureIconOnlyButton(btnLocate, "定位");
            ConfigureIconOnlyButton(btnDataBreakpoints, "数据断点");
            ConfigureIconOnlyButton(btnAlarm, "报警信息");
            ConfigureIconOnlyButton(btnFlowGraph, "流程图");
            ConfigureIconOnlyButton(btnPerformanceAnalysis, "性能分析");
            ConfigureIconOnlyButton(btnIOMonitor, "IO监视");
            ConfigureIconOnlyButton(button1, "打开程序文件夹");
            ConfigureIconOnlyButton(btnAppConfig, "程序设置");
            btnStopAll.FlatAppearance.BorderSize = 1;
            btnStopAll.FlatAppearance.BorderColor = UiPalette.Danger;

            ToolBar_Panel.Resize += (sender, args) => LayoutToolbarButtons();
            ToolBar_Panel.Paint += ToolBar_Panel_Paint;
            foreach (WinFormsButton button in GetToolbarButtons())
            {
                button.VisibleChanged += (sender, args) => LayoutToolbarButtons();
            }
            LayoutToolbarButtons();
        }

        private void ConfigureButton(WinFormsButton button, UiIconKind icon, int width, Color foreColor, Color backColor, Color hoverColor)
        {
            button.Tag = width;
            button.Height = 38;
            button.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            button.ForeColor = foreColor;
            button.BackColor = backColor;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = backColor;
            button.FlatAppearance.MouseDownBackColor = backColor;
            button.UseVisualStyleBackColor = false;
            button.TabStop = false;
            button.Image = UiIconFactory.Create(icon, foreColor, 28);
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(3, 0, 3, 0);
            button.Margin = Padding.Empty;
            hoverAnimator.Attach(button, () => backColor, hoverColor, true);
        }

        private void ConfigureIconOnlyButton(WinFormsButton button, string accessibleName)
        {
            button.Text = string.Empty;
            button.AccessibleName = accessibleName;
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.Overlay;
            button.Padding = Padding.Empty;
            toolbarToolTip.SetToolTip(button, accessibleName);
        }

        internal void SetPauseButtonAction(bool continueAction)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetPauseButtonAction(continueAction)));
                return;
            }

            string accessibleName = continueAction ? "继续" : "暂停";
            btnPause.Text = string.Empty;
            btnPause.AccessibleName = accessibleName;
            toolbarToolTip.SetToolTip(btnPause, accessibleName);
        }

        private WinFormsButton[] GetToolbarButtons()
        {
            return new[]
            {
                btnNavigateBack, btnNavigateForward,
                btnUndo, btnRedo,
                btnPause, btnStop, SingleRun, btnLocate,
                btnDataBreakpoints, btnAlarm, btnSearch, btnFlowGraph, btnPerformanceAnalysis, btnIOMonitor,
                button1, btnAppConfig, btnStopAll
            };
        }

        private void LayoutToolbarButtons()
        {
            separatorPositions.Clear();
            int top = Math.Max(3, (ToolBar_Panel.ClientSize.Height - 38) / 2);
            int left = 8;

            bool hasNavigationGroup = btnNavigateBack.Visible || btnNavigateForward.Visible;
            if (hasNavigationGroup)
            {
                PlaceFromLeft(btnNavigateBack, ref left, top, 0);
                PlaceFromLeft(btnNavigateForward, ref left, top, 16);
                separatorPositions.Add(left - 8);
            }

            bool hasHistoryGroup = btnUndo.Visible || btnRedo.Visible;
            if (hasHistoryGroup)
            {
                PlaceFromLeft(btnUndo, ref left, top, 0);
                PlaceFromLeft(btnRedo, ref left, top, 16);
                separatorPositions.Add(left - 8);
            }

            bool hasRunGroup = btnPause.Visible || btnStop.Visible || SingleRun.Visible || btnLocate.Visible;
            if (hasRunGroup)
            {
                PlaceFromLeft(btnPause, ref left, top, 2);
                PlaceFromLeft(btnStop, ref left, top, 2);
                PlaceFromLeft(SingleRun, ref left, top, 2);
                PlaceFromLeft(btnLocate, ref left, top, 16);
                separatorPositions.Add(left - 8);
            }

            PlaceFromLeft(btnAlarm, ref left, top, 2);
            PlaceFromLeft(btnIOMonitor, ref left, top, 2);

            int right = ToolBar_Panel.ClientSize.Width - 8;
            PlaceFromRight(btnStopAll, ref right, top, 18);
            if (btnStopAll.Visible)
            {
                separatorPositions.Add(right + 9);
            }
            PlaceFromRight(btnAppConfig, ref right, top, 2);
            PlaceFromRight(button1, ref right, top, 0);
            PlaceFromRight(btnPerformanceAnalysis, ref right, top, 2);
            PlaceFromRight(btnFlowGraph, ref right, top, 2);
            PlaceFromRight(btnSearch, ref right, top, 2);
            PlaceFromRight(btnDataBreakpoints, ref right, top, 0);

            // 极窄窗口中保持所有按钮可见，系统操作跟随左侧内容排列。
            if (button1.Visible && right < left)
            {
                PlaceFromLeft(btnDataBreakpoints, ref left, top, 2);
                PlaceFromLeft(btnSearch, ref left, top, 2);
                PlaceFromLeft(btnFlowGraph, ref left, top, 2);
                PlaceFromLeft(btnPerformanceAnalysis, ref left, top, 2);
                PlaceFromLeft(button1, ref left, top, 6);
                PlaceFromLeft(btnAppConfig, ref left, top, 6);
                PlaceFromLeft(btnStopAll, ref left, top, 0);
            }
            ToolBar_Panel.Invalidate();
        }

        internal void SetNavigationAvailability(bool canNavigateBack, bool canNavigateForward)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetNavigationAvailability(canNavigateBack, canNavigateForward)));
                return;
            }
            btnNavigateBack.Enabled = canNavigateBack;
            btnNavigateForward.Enabled = canNavigateForward;
        }

        internal void RefreshHistoryAvailability()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                BeginInvoke((Action)RefreshHistoryAvailability);
                return;
            }

            IEditSession editSession = Workspace.Runtime.Editor.ActiveSession;
            bool canUndo = editSession?.CanUndo ?? Workspace.Runtime.Editor.History.CanUndo;
            bool canRedo = editSession?.CanRedo ?? Workspace.Runtime.Editor.History.CanRedo;
            string undoDescription = editSession?.Name ?? Workspace.Runtime.Editor.History.UndoDescription;
            string redoDescription = editSession?.Name ?? Workspace.Runtime.Editor.History.RedoDescription;
            btnUndo.Enabled = canUndo;
            btnRedo.Enabled = canRedo;
            btnUndo.AccessibleName = canUndo
                ? $"撤销：{undoDescription}"
                : "撤销";
            btnRedo.AccessibleName = canRedo
                ? $"重做：{redoDescription}"
                : "重做";
            toolbarToolTip.SetToolTip(
                btnUndo,
                canUndo ? $"撤销：{undoDescription}（Ctrl+Z）" : "撤销（Ctrl+Z）");
            toolbarToolTip.SetToolTip(
                btnRedo,
                canRedo ? $"重做：{redoDescription}（Ctrl+Y）" : "重做（Ctrl+Y）");
        }

        private void EditorHistory_StateChanged(object sender, EventArgs e)
        {
            RefreshHistoryAvailability();
        }

        private void ExecuteHistoryAction(bool undo)
        {
            string description;
            string error;
            bool success = undo
                ? Workspace.Runtime.Editor.TryUndo(out description, out error)
                : Workspace.Runtime.Editor.TryRedo(out description, out error);
            if (!success)
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MessageBox.Show(
                        error,
                        undo ? "撤销失败" : "重做失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                RefreshHistoryAvailability();
                return;
            }
            if (Workspace.Info != null && !Workspace.Info.IsDisposed)
            {
                Workspace.Info.PrintInfo(
                    $"已{(undo ? "撤销" : "重做")}：{description}",
                    FrmInfo.Level.Normal);
            }
            RefreshHistoryAvailability();
        }

        private void ToolBar_Panel_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(UiPalette.Stroke))
            {
                int top = Math.Max(8, (ToolBar_Panel.ClientSize.Height - 22) / 2);
                foreach (int x in separatorPositions)
                {
                    e.Graphics.DrawLine(pen, x, top, x, top + 22);
                }
            }
            using (Pen borderPen = new Pen(UiPalette.Stroke))
            {
                e.Graphics.DrawLine(
                    borderPen,
                    0,
                    ToolBar_Panel.ClientSize.Height - 1,
                    ToolBar_Panel.ClientSize.Width,
                    ToolBar_Panel.ClientSize.Height - 1);
            }
        }

        private static void PlaceFromLeft(WinFormsButton button, ref int left, int top, int spacing)
        {
            if (!button.Visible)
            {
                return;
            }
            int width = (int)button.Tag;
            button.SetBounds(left, top, width, 38);
            left += width + spacing;
        }

        private static void PlaceFromRight(WinFormsButton button, ref int right, int top, int spacing)
        {
            if (!button.Visible)
            {
                return;
            }
            int width = (int)button.Tag;
            right -= width;
            button.SetBounds(right, top, width, 38);
            right -= spacing;
        }

        private void btnAppConfig_Click(object sender, EventArgs e)
        {
            using (FrmAppConfig frm = new FrmAppConfig())
            {
                if (frm.ShowDialog(this) == DialogResult.OK
                    && AppConfigStorage.TryGetCached(out AppConfig config, out _))
                {
                    Workspace.Main?.ApplyRuntimeDiagnosticsConfiguration(config.EnableRuntimeDiagnostics);
                }
            }
        }
      
        private void btnSave_Click(object sender, EventArgs e)
        {
            if (Workspace.Runtime.Editor.ActiveSession == null)
            {
                MessageBox.Show("当前没有可保存的编辑会话。");
                return;
            }
            try
            {
                if (!Workspace.Runtime.Editor.TryCommit(out string error))
                {
                    MessageBox.Show(error, "配置校验失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Workspace.DataGrid.dataGridView1.Enabled = true;
                Workspace.Proc.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "配置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (Workspace.Runtime.Editor.ActiveSession == null)
            {
                return;
            }
            Workspace.Runtime.Editor.Cancel();
            Workspace.DataGrid.dataGridView1.Enabled = true;
            Workspace.Proc.Enabled = true;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string path = Workspace.Runtime.Paths.ConfigPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            System.Diagnostics.Process.Start("explorer.exe", path);

        }
        
        private void btnSearch_Click(object sender, EventArgs e)
        {
            Workspace.Search.StartPosition = FormStartPosition.CenterScreen;
            Workspace.Search.Show();
            Workspace.Search.BringToFront();
            Workspace.Search.WindowState = FormWindowState.Normal;
            Workspace.Search.textBox1.Focus();
        }

        private void btnIOMonitor_Click(object sender, EventArgs e)
        {
            if (Workspace.IO == null)
            {
                return;
            }
            bool enabled = Workspace.IO.ToggleIOMonitor();
            btnIOMonitor.AccessibleName = enabled ? "停止监视" : "IO监视";
            toolbarToolTip.SetToolTip(btnIOMonitor, btnIOMonitor.AccessibleName);
        }

        private async void btnPause_Click(object sender, EventArgs e)
        {
            int procIndex = Workspace.Proc.SelectedProcNum;
            if (procIndex < 0)
            {
                return;
            }
            EngineSnapshot snapshot = Workspace.Runtime.ProcessEngine.GetSnapshot(procIndex);
            if (snapshot != null && (snapshot.State == ProcRunState.Running || snapshot.State == ProcRunState.Alarming))
            {
                Workspace.Runtime.ProcessEngine.Pause(procIndex);
                SetPauseButtonAction(true);
            }
            else if (snapshot != null && snapshot.State == ProcRunState.Paused)
            {
                if (Workspace.Info != null && !Workspace.Info.IsDisposed)
                {
                    Workspace.Info.PrintInfo("流程已暂停，禁止继续运行。", FrmInfo.Level.Error);
                }
                return;
            }
            else if (snapshot != null && snapshot.State == ProcRunState.SingleStep)
            {
                Proc proc = null;
                if (Workspace.Proc?.procsList != null && procIndex >= 0 && procIndex < Workspace.Proc.procsList.Count)
                {
                    proc = Workspace.Proc.procsList[procIndex];
                }
                string procName = snapshot.ProcName ?? proc?.head?.Name ?? $"索引{procIndex}";
                int stepIndex = snapshot.StepIndex;
                int opIndex = snapshot.OpIndex;
                string position = $"{procIndex}-{stepIndex}-{opIndex}";
                string opName = null;
                string opType = null;
                if (proc?.steps != null && stepIndex >= 0 && stepIndex < proc.steps.Count)
                {
                    Step step = proc.steps[stepIndex];
                    if (step?.Ops != null && opIndex >= 0 && opIndex < step.Ops.Count)
                    {
                        OperationType op = step.Ops[opIndex];
                        opName = op?.Name;
                        opType = op?.OperaType;
                    }
                }
                string opText = opIndex >= 0
                    ? $"{opIndex}{(string.IsNullOrWhiteSpace(opType) ? "" : $"({opType})")}{(string.IsNullOrWhiteSpace(opName) ? "" : $" {opName}")}"
                    : "未知";
                string message = $"位置: {position}\r\n操作: {opText}";
                btnPause.Enabled = false;
                try
                {
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Message confirmForm = new Message(Workspace.Runtime,
                        "继续运行确认",
                        message,
                        () => tcs.TrySetResult(true),
                        () => tcs.TrySetResult(false),
                        "继续",
                        "取消",
                        false);
                    confirmForm.txtMsg.Font = new Font("微软雅黑", 20F, FontStyle.Bold);
                    confirmForm.txtMsg.ForeColor = UiPalette.Danger;
                    bool confirmed = await tcs.Task;
                    if (!confirmed)
                    {
                        return;
                    }
                    Workspace.Runtime.ProcessEngine.Resume(procIndex);
                    SetPauseButtonAction(false);
                }
                finally
                {
                    btnPause.Enabled = true;
                }
            }
        }

        private void SingleRun_Click(object sender, EventArgs e)
        {
            int procIndex = Workspace.Proc.SelectedProcNum;
            if (procIndex != -1)
            {
                EngineSnapshot snapshot = Workspace.Runtime.ProcessEngine.GetSnapshot(procIndex);
                if (snapshot != null && snapshot.State == ProcRunState.Paused)
                {
                    if (Workspace.Info != null && !Workspace.Info.IsDisposed)
                    {
                        Workspace.Info.PrintInfo("流程已暂停，禁止单步继续。", FrmInfo.Level.Error);
                    }
                    return;
                }
                if (Workspace.Proc.SelectedStepNum != -1 && snapshot != null
                    && snapshot.State == ProcRunState.SingleStep)
                {
                    Workspace.Runtime.ProcessEngine.Step(procIndex);
                    Workspace.DataGrid.RequestSingleStepFollow(procIndex);
                }
            }
                
        }
        private void btnStop_Click(object sender, EventArgs e)
        {
            if (Workspace.Proc.SelectedProcNum >= 0)
            {
                Workspace.Runtime.ProcessEngine.Stop(Workspace.Proc.SelectedProcNum);
            }

        }

        private void btnStopAll_Click(object sender, EventArgs e)
        {
            if (Workspace.Proc?.procsList == null)
            {
                return;
            }

            int count = Workspace.Proc.procsList.Count;
            for (int i = 0; i < count; i++)
            {
                Proc proc = Workspace.Proc.procsList[i];
                string procName = proc?.head?.Name;
                if (!string.IsNullOrEmpty(procName) && procName.StartsWith("系统", StringComparison.Ordinal))
                {
                    continue;
                }

                Workspace.Runtime.ProcessEngine.Stop(i);
            }
        }

        private void btnAlarm_Click(object sender, EventArgs e)
        {
            Workspace.AlarmConfig.StartPosition = FormStartPosition.CenterScreen;
            Workspace.AlarmConfig.Show();
            Workspace.AlarmConfig.BringToFront();
            Workspace.AlarmConfig.WindowState = FormWindowState.Normal;
        }

        private void btnLocate_Click(object sender, EventArgs e)
        {
            int procIndex = Workspace.Proc.SelectedProcNum;
            if (procIndex < 0)
            {
                return;
            }
            EngineSnapshot snapshot = Workspace.Runtime.ProcessEngine.GetSnapshot(procIndex);
            if (snapshot == null || snapshot.StepIndex < 0 || snapshot.OpIndex < 0)
            {
                return;
            }
            Workspace.DataGrid.SelectChildNode(procIndex, snapshot.StepIndex);
            Workspace.DataGrid.ScrollRowToCenter(snapshot.OpIndex);
            Workspace.DataGrid.SetRowColor(snapshot.OpIndex, UiPalette.InfoSoft);
        }
    }
}
