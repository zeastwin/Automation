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
using static Automation.FrmCard;
using static Automation.OperationTypePartial;
using static System.Collections.Specialized.BitVector32;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace Automation
{
    public partial class FrmToolBar : Form
    {
        public FrmToolBar()
        {
            InitializeComponent();
            btnSave.Enabled = false;
            btnCancel.Enabled = false;
            btnIOMonitor.Visible = false;
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
