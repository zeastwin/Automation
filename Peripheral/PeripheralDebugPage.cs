using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Automation.Peripheral
{
    public partial class PeripheralDebugPage : Form
    {
        private delegate bool ProcessAction(int procIndex, out string error);

        private AutomationPlatformHost platformHost;
        private int processRefreshPending = 1;

        public PeripheralDebugPage()
        {
            InitializeComponent();
        }

        internal void AttachHost(AutomationPlatformHost host)
        {
            platformHost = host ?? throw new ArgumentNullException(nameof(host));
            txtConfigRoot.Text = platformHost.ConfigRoot;
        }

        internal void MarkProcessListDirty()
        {
            Interlocked.Exchange(ref processRefreshPending, 1);
        }

        internal void UpdateRuntimeState(PlatformRuntimeState state, string message)
        {
            lblRuntimeState.Text = $"{state} - {message}";
            lblRuntimeState.ForeColor = state == PlatformRuntimeState.Ready
                ? Color.FromArgb(46, 125, 50)
                : state == PlatformRuntimeState.Faulted
                    ? Color.FromArgb(198, 40, 40)
                    : Color.FromArgb(69, 90, 100);
        }

        internal void RefreshRuntimeView()
        {
            if (platformHost == null)
            {
                return;
            }

            UpdateRuntimeState(platformHost.State, platformHost.StateMessage);
            if (platformHost.State != PlatformRuntimeState.Ready && platformHost.State != PlatformRuntimeState.Faulted)
            {
                lblSystemState.Text = "-";
                lblResetState.Text = "-";
                return;
            }

            lblSystemState.Text = ReadSystemStatus();
            lblResetState.Text = ReadResetStatus();
            if (Interlocked.Exchange(ref processRefreshPending, 0) != 0 || cmbProcess.Items.Count == 0)
            {
                RefreshProcessList();
            }
            RefreshLogs();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            ExecuteSelectedProcess(platformHost == null ? null : platformHost.TryStartProcess);
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            ExecuteSelectedProcess(platformHost == null ? null : platformHost.TryPauseProcess);
        }

        private void btnResume_Click(object sender, EventArgs e)
        {
            ExecuteSelectedProcess(platformHost == null ? null : platformHost.TryResumeProcess);
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            ExecuteSelectedProcess(platformHost == null ? null : platformHost.TryStopProcess);
        }

        private void btnOpenConfig_Click(object sender, EventArgs e)
        {
            if (platformHost == null)
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{platformHost.ConfigRoot}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"打开 Config 目录失败:{ex.Message}", "Config", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshProcessList()
        {
            int selectedIndex = cmbProcess.SelectedItem is PlatformProcessInfo selected ? selected.Index : -1;
            IReadOnlyList<PlatformProcessInfo> processes = platformHost.GetProcesses();
            cmbProcess.BeginUpdate();
            try
            {
                cmbProcess.Items.Clear();
                foreach (PlatformProcessInfo process in processes)
                {
                    cmbProcess.Items.Add(process);
                }
                int target = -1;
                for (int i = 0; i < cmbProcess.Items.Count; i++)
                {
                    if (((PlatformProcessInfo)cmbProcess.Items[i]).Index == selectedIndex)
                    {
                        target = i;
                        break;
                    }
                }
                cmbProcess.SelectedIndex = target >= 0 ? target : (cmbProcess.Items.Count > 0 ? 0 : -1);
            }
            finally
            {
                cmbProcess.EndUpdate();
            }
        }

        private void RefreshLogs()
        {
            IReadOnlyList<PlatformLogEntry> logs = platformHost.GetRecentLogs(200);
            lvRuntimeLog.BeginUpdate();
            try
            {
                lvRuntimeLog.Items.Clear();
                foreach (PlatformLogEntry log in logs)
                {
                    string levelText = log.Level == FrmInfo.Level.Error ? "报警" : "信息";
                    ListViewItem item = new ListViewItem(log.TimeText ?? string.Empty);
                    item.SubItems.Add(levelText);
                    item.SubItems.Add(log.Message ?? string.Empty);
                    if (log.Level == FrmInfo.Level.Error)
                    {
                        item.ForeColor = Color.FromArgb(183, 28, 28);
                    }
                    lvRuntimeLog.Items.Add(item);
                }
                if (lvRuntimeLog.Items.Count > 0)
                {
                    lvRuntimeLog.EnsureVisible(lvRuntimeLog.Items.Count - 1);
                }
            }
            finally
            {
                lvRuntimeLog.EndUpdate();
            }
        }

        private string ReadSystemStatus()
        {
            if (!platformHost.TryGetValue("系统状态", out PlatformValueSnapshot value, out string error))
            {
                return error;
            }
            if (!TryReadEnumValue(value.Value, out int raw) || !Enum.IsDefined(typeof(SystemStatus), raw))
            {
                return $"无效:{value.Value}";
            }
            switch ((SystemStatus)raw)
            {
                case SystemStatus.Paused: return "暂停";
                case SystemStatus.Ready: return "就绪";
                case SystemStatus.Working: return "工作中";
                case SystemStatus.ProcAlarm: return "流程报警";
                case SystemStatus.PopupAlarm: return "弹窗报警";
                default: return "未初始化";
            }
        }

        private string ReadResetStatus()
        {
            if (!platformHost.TryGetValue("复位状态", out PlatformValueSnapshot value, out string error))
            {
                return error;
            }
            if (!TryReadEnumValue(value.Value, out int raw) || !Enum.IsDefined(typeof(ResetStatus), raw))
            {
                return $"无效:{value.Value}";
            }
            switch ((ResetStatus)raw)
            {
                case ResetStatus.Resetting: return "复位中";
                case ResetStatus.ResetCompleted: return "复位完成";
                default: return "未复位";
            }
        }

        private void ExecuteSelectedProcess(ProcessAction action)
        {
            if (platformHost == null || action == null)
            {
                MessageBox.Show(FindForm(), "平台尚未初始化。", "流程控制", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!(cmbProcess.SelectedItem is PlatformProcessInfo process))
            {
                MessageBox.Show(FindForm(), "请先选择流程。", "流程控制", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!action(process.Index, out string error))
            {
                MessageBox.Show(FindForm(), error, "流程控制失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            MarkProcessListDirty();
        }

        private static bool TryReadEnumValue(string value, out int result)
        {
            result = 0;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw)
                || raw < int.MinValue
                || raw > int.MaxValue
                || Math.Abs(raw - Math.Truncate(raw)) > double.Epsilon)
            {
                return false;
            }
            result = (int)raw;
            return true;
        }
    }
}
