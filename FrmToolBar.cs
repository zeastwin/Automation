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
using static Automation.FrmCard;
using static Automation.OperationTypePartial;
using static System.Collections.Specialized.BitVector32;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace Automation
{
    public partial class FrmToolBar : Form
    {
        private static readonly Color ToolbarBackColor = Color.FromArgb(242, 246, 249);
        private static readonly Color ButtonBackColor = Color.FromArgb(242, 246, 249);
        private static readonly Color ButtonForeColor = Color.FromArgb(48, 67, 78);
        private readonly List<int> separatorPositions = new List<int>();
        private readonly UiHoverAnimator hoverAnimator = new UiHoverAnimator();

        public FrmToolBar()
        {
            InitializeComponent();
            ConfigureToolbarAppearance();
            btnSave.Enabled = false;
            btnCancel.Enabled = false;
            btnIOMonitor.Visible = false;
            Disposed += (sender, args) => hoverAnimator.Dispose();
        }

        private void ConfigureToolbarAppearance()
        {
            ToolBar_Panel.BackColor = ToolbarBackColor;
            BackColor = ToolbarBackColor;

            ConfigureButton(btnSave, UiIconKind.Save, 78, Color.FromArgb(15, 105, 160), ButtonBackColor, Color.FromArgb(221, 237, 247));
            ConfigureButton(btnCancel, UiIconKind.Cancel, 78, ButtonForeColor, ButtonBackColor, Color.FromArgb(226, 234, 239));
            ConfigureButton(btnPause, UiIconKind.Pause, 78, ButtonForeColor, ButtonBackColor, Color.FromArgb(226, 234, 239));
            ConfigureButton(btnStop, UiIconKind.Stop, 78, Color.FromArgb(176, 55, 55), ButtonBackColor, Color.FromArgb(248, 229, 229));
            ConfigureButton(SingleRun, UiIconKind.Step, 78, ButtonForeColor, ButtonBackColor, Color.FromArgb(226, 234, 239));
            ConfigureButton(btnLocate, UiIconKind.Locate, 78, ButtonForeColor, ButtonBackColor, Color.FromArgb(226, 234, 239));
            ConfigureButton(btnAlarm, UiIconKind.Alarm, 106, Color.FromArgb(151, 91, 16), ButtonBackColor, Color.FromArgb(252, 239, 213));
            ConfigureButton(btnSearch, UiIconKind.Search, 78, ButtonForeColor, ButtonBackColor, Color.FromArgb(226, 234, 239));
            ConfigureButton(btnIOMonitor, UiIconKind.Monitor, 104, ButtonForeColor, ButtonBackColor, Color.FromArgb(226, 234, 239));
            ConfigureButton(button1, UiIconKind.Folder, 146, ButtonForeColor, ButtonBackColor, Color.FromArgb(226, 234, 239));
            ConfigureButton(btnAppConfig, UiIconKind.Settings, 108, ButtonForeColor, ButtonBackColor, Color.FromArgb(226, 234, 239));
            ConfigureButton(btnStopAll, UiIconKind.StopAll, 110, Color.FromArgb(174, 45, 45), Color.FromArgb(255, 246, 246), Color.FromArgb(249, 224, 224));
            btnStopAll.FlatAppearance.BorderSize = 1;
            btnStopAll.FlatAppearance.BorderColor = Color.FromArgb(218, 148, 148);

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
            button.Image = UiIconFactory.Create(icon, foreColor, 20);
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(3, 0, 3, 0);
            button.Margin = Padding.Empty;
            hoverAnimator.Attach(button, () => backColor, hoverColor, true);
        }

        private WinFormsButton[] GetToolbarButtons()
        {
            return new[]
            {
                btnSave, btnCancel, btnPause, btnStop, SingleRun, btnLocate,
                btnAlarm, btnSearch, btnIOMonitor, button1, btnAppConfig, btnStopAll
            };
        }

        private void LayoutToolbarButtons()
        {
            separatorPositions.Clear();
            int top = Math.Max(3, (ToolBar_Panel.ClientSize.Height - 38) / 2);
            int left = 8;
            PlaceFromLeft(btnSave, ref left, top, 2);
            PlaceFromLeft(btnCancel, ref left, top, 16);
            separatorPositions.Add(left - 8);

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
            PlaceFromLeft(btnSearch, ref left, top, 2);
            PlaceFromLeft(btnIOMonitor, ref left, top, 2);

            int right = ToolBar_Panel.ClientSize.Width - 8;
            PlaceFromRight(btnStopAll, ref right, top, 18);
            if (btnStopAll.Visible)
            {
                separatorPositions.Add(right + 9);
            }
            PlaceFromRight(btnAppConfig, ref right, top, 2);
            PlaceFromRight(button1, ref right, top, 0);

            // 极窄窗口中保持所有按钮可见，系统操作跟随左侧内容排列。
            if (button1.Visible && right < left)
            {
                PlaceFromLeft(button1, ref left, top, 6);
                PlaceFromLeft(btnAppConfig, ref left, top, 6);
                PlaceFromLeft(btnStopAll, ref left, top, 0);
            }
            ToolBar_Panel.Invalidate();
        }

        private void ToolBar_Panel_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Color.FromArgb(208, 216, 222)))
            {
                int top = Math.Max(8, (ToolBar_Panel.ClientSize.Height - 22) / 2);
                foreach (int x in separatorPositions)
                {
                    e.Graphics.DrawLine(pen, x, top, x, top + 22);
                }
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
                frm.ShowDialog(this);
            }
        }
      
        private void btnSave_Click(object sender, EventArgs e)
        {
            if (SF.ActiveEditSession == null)
            {
                MessageBox.Show("当前没有可保存的编辑会话。");
                return;
            }
            try
            {
                if (!SF.TryCommitEditSession(out string error))
                {
                    MessageBox.Show(error, "配置校验失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                SF.frmDataGrid.dataGridView1.Enabled = true;
                SF.frmProc.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "配置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (SF.ActiveEditSession == null)
            {
                return;
            }
            SF.CancelEditSession();
            SF.frmDataGrid.dataGridView1.Enabled = true;
            SF.frmProc.Enabled = true;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string path = SF.ConfigPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            System.Diagnostics.Process.Start("explorer.exe", path);

        }
        
        private void btnSearch_Click(object sender, EventArgs e)
        {
            SF.frmSearch.StartPosition = FormStartPosition.CenterScreen;
            SF.frmSearch.Show();
            SF.frmSearch.BringToFront();
            SF.frmSearch.WindowState = FormWindowState.Normal;
            SF.frmSearch.textBox1.Focus();
        }

        private void btnIOMonitor_Click(object sender, EventArgs e)
        {
            if (SF.frmIO == null)
            {
                return;
            }
            bool enabled = SF.frmIO.ToggleIOMonitor();
            btnIOMonitor.Text = enabled ? "停止监视" : "IO监视";
        }

        private async void btnPause_Click(object sender, EventArgs e)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            if (procIndex < 0)
            {
                return;
            }
            EngineSnapshot snapshot = SF.DR.GetSnapshot(procIndex);
            if (snapshot != null && (snapshot.State == ProcRunState.Running || snapshot.State == ProcRunState.Alarming))
            {
                SF.DR.Pause(procIndex);
                btnPause.Text = "继续";
            }
            else if (snapshot != null && snapshot.State == ProcRunState.Paused)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("流程已暂停，禁止继续运行。", FrmInfo.Level.Error);
                }
                return;
            }
            else if (snapshot != null && snapshot.State == ProcRunState.SingleStep)
            {
                Proc proc = null;
                if (SF.frmProc?.procsList != null && procIndex >= 0 && procIndex < SF.frmProc.procsList.Count)
                {
                    proc = SF.frmProc.procsList[procIndex];
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
                    Message confirmForm = new Message(
                        "继续运行确认",
                        message,
                        () => tcs.TrySetResult(true),
                        () => tcs.TrySetResult(false),
                        "继续",
                        "取消",
                        false);
                    confirmForm.txtMsg.Font = new Font("微软雅黑", 20F, FontStyle.Bold);
                    confirmForm.txtMsg.ForeColor = Color.Red;
                    bool confirmed = await tcs.Task;
                    if (!confirmed)
                    {
                        return;
                    }
                    SF.DR.Resume(procIndex);
                    btnPause.Text = "暂停";
                }
                finally
                {
                    btnPause.Enabled = true;
                }
            }
        }

        private void SingleRun_Click(object sender, EventArgs e)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            if (procIndex != -1)
            {
                EngineSnapshot snapshot = SF.DR.GetSnapshot(procIndex);
                if (snapshot != null && snapshot.State == ProcRunState.Paused)
                {
                    if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                    {
                        SF.frmInfo.PrintInfo("流程已暂停，禁止单步继续。", FrmInfo.Level.Error);
                    }
                    return;
                }
                if (SF.frmProc.SelectedStepNum != -1 && snapshot != null
                    && snapshot.State == ProcRunState.SingleStep)
                {
                    SF.DR.Step(procIndex);
                    SF.frmDataGrid.RequestSingleStepFollow(procIndex);
                }
            }
                
        }
        private void btnStop_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum >= 0)
            {
                SF.DR.Stop(SF.frmProc.SelectedProcNum);
            }

        }

        private void btnStopAll_Click(object sender, EventArgs e)
        {
            if (SF.frmProc?.procsList == null)
            {
                return;
            }

            int count = SF.frmProc.procsList.Count;
            for (int i = 0; i < count; i++)
            {
                Proc proc = SF.frmProc.procsList[i];
                string procName = proc?.head?.Name;
                if (!string.IsNullOrEmpty(procName) && procName.StartsWith("系统", StringComparison.Ordinal))
                {
                    continue;
                }

                SF.DR.Stop(i);
            }
        }

        private void btnAlarm_Click(object sender, EventArgs e)
        {
            SF.frmAlarmConfig.StartPosition = FormStartPosition.CenterScreen;
            SF.frmAlarmConfig.Show();
            SF.frmAlarmConfig.BringToFront();
            SF.frmAlarmConfig.WindowState = FormWindowState.Normal;
        }

        private void btnLocate_Click(object sender, EventArgs e)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            if (procIndex < 0)
            {
                return;
            }
            EngineSnapshot snapshot = SF.DR.GetSnapshot(procIndex);
            if (snapshot == null || snapshot.StepIndex < 0 || snapshot.OpIndex < 0)
            {
                return;
            }
            SF.frmDataGrid.SelectChildNode(procIndex, snapshot.StepIndex);
            SF.frmDataGrid.ScrollRowToCenter(snapshot.OpIndex);
            SF.frmDataGrid.SetRowColor(snapshot.OpIndex, Color.LightBlue);
        }
    }
}
