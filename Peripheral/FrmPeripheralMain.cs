using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Automation.Peripheral
{
    public sealed class FrmPeripheralMain : Form
    {
        private delegate bool ProcessAction(int procIndex, out string error);

        private readonly AutomationPlatformHost platformHost;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly ComboBox cmbProcess;
        private readonly Label lblRuntimeState;
        private readonly Label lblSystemState;
        private readonly Label lblResetState;
        private readonly TextBox txtConfigRoot;
        private readonly ListView lvRuntimeLog;
        private int processRefreshPending = 1;
        private bool closingConfirmed;
        private bool hostEventsDetached;

        public FrmPeripheralMain(AutomationPlatformHost platformHost)
        {
            this.platformHost = platformHost ?? throw new ArgumentNullException(nameof(platformHost));

            Text = "Automation 外围应用";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(1100, 720);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(242, 245, 249);

            Panel header = BuildHeader();
            Control processPanel = BuildProcessPanel();
            Control statusPanel = BuildStatusPanel();
            Control workspace = BuildWorkspace();
            Control logPanel = BuildLogPanel();

            Panel leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 320,
                Padding = new Padding(12, 12, 6, 12),
                BackColor = BackColor
            };
            statusPanel.Dock = DockStyle.Top;
            statusPanel.Height = 225;
            processPanel.Dock = DockStyle.Fill;
            leftPanel.Controls.Add(processPanel);
            leftPanel.Controls.Add(statusPanel);

            Panel centerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 12, 12, 12),
                BackColor = BackColor
            };
            logPanel.Dock = DockStyle.Bottom;
            logPanel.Height = 245;
            workspace.Dock = DockStyle.Fill;
            centerPanel.Controls.Add(workspace);
            centerPanel.Controls.Add(logPanel);

            Controls.Add(centerPanel);
            Controls.Add(leftPanel);
            Controls.Add(header);

            cmbProcess = (ComboBox)processPanel.Controls.Find("cmbProcess", true)[0];
            lblRuntimeState = (Label)statusPanel.Controls.Find("lblRuntimeState", true)[0];
            lblSystemState = (Label)statusPanel.Controls.Find("lblSystemState", true)[0];
            lblResetState = (Label)statusPanel.Controls.Find("lblResetState", true)[0];
            txtConfigRoot = (TextBox)statusPanel.Controls.Find("txtConfigRoot", true)[0];
            lvRuntimeLog = (ListView)logPanel.Controls.Find("lvRuntimeLog", true)[0];
            txtConfigRoot.Text = platformHost.ConfigRoot;

            platformHost.RuntimeStateChanged += PlatformHost_RuntimeStateChanged;
            platformHost.ProcessSnapshotChanged += PlatformHost_ProcessSnapshotChanged;
            platformHost.ValueChanged += PlatformHost_ValueChanged;
            RegisterBusinessCallbacks();

            refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
            refreshTimer.Tick += RefreshTimer_Tick;
            Shown += FrmPeripheralMain_Shown;
            FormClosing += FrmPeripheralMain_FormClosing;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachHostEvents();
                refreshTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        private Panel BuildHeader()
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                Padding = new Padding(20, 12, 20, 12),
                BackColor = Color.FromArgb(31, 52, 78)
            };
            Label title = new Label
            {
                AutoSize = true,
                Text = "Automation 外围应用",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                Location = new Point(20, 18)
            };
            Button openPlatform = CreateActionButton("打开平台", Color.FromArgb(33, 150, 243));
            openPlatform.Width = 120;
            openPlatform.Dock = DockStyle.Right;
            openPlatform.Click += (sender, e) => platformHost.ShowPlatformEditor(this);

            Button stopAll = CreateActionButton("停止全部流程", Color.FromArgb(198, 40, 40));
            stopAll.Width = 140;
            stopAll.Dock = DockStyle.Right;
            stopAll.Margin = new Padding(0, 0, 12, 0);
            stopAll.Click += StopAll_Click;

            panel.Controls.Add(openPlatform);
            panel.Controls.Add(stopAll);
            panel.Controls.Add(title);
            return panel;
        }

        private Control BuildProcessPanel()
        {
            GroupBox group = CreateGroup("流程控制");
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 2,
                RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            ComboBox process = new ComboBox
            {
                Name = "cmbProcess",
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            layout.Controls.Add(process, 0, 0);
            layout.SetColumnSpan(process, 2);

            Button start = CreateActionButton("启动", Color.FromArgb(46, 125, 50));
            start.Click += (sender, e) => ExecuteSelectedProcess(platformHost.TryStartProcess);
            Button pause = CreateActionButton("暂停", Color.FromArgb(239, 108, 0));
            pause.Click += (sender, e) => ExecuteSelectedProcess(platformHost.TryPauseProcess);
            Button resume = CreateActionButton("继续", Color.FromArgb(2, 119, 189));
            resume.Click += (sender, e) => ExecuteSelectedProcess(platformHost.TryResumeProcess);
            Button stop = CreateActionButton("停止", Color.FromArgb(198, 40, 40));
            stop.Click += (sender, e) => ExecuteSelectedProcess(platformHost.TryStopProcess);
            layout.Controls.Add(start, 0, 1);
            layout.Controls.Add(pause, 1, 1);
            layout.Controls.Add(resume, 0, 2);
            layout.Controls.Add(stop, 1, 2);

            Label hint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "此处仅提供平台级流程入口。具体设备手自动、工艺和产量页面由外围业务继续开发。",
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(3, 10, 3, 3)
            };
            layout.Controls.Add(hint, 0, 3);
            layout.SetColumnSpan(hint, 2);
            layout.SetRowSpan(hint, 2);
            group.Controls.Add(layout);
            return group;
        }

        private Control BuildStatusPanel()
        {
            GroupBox group = CreateGroup("平台状态");
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 2,
                RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 4; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            AddStatusRow(layout, 0, "Runtime", "lblRuntimeState");
            AddStatusRow(layout, 1, "系统状态", "lblSystemState");
            AddStatusRow(layout, 2, "复位状态", "lblResetState");
            layout.Controls.Add(new Label { Text = "Config", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            TextBox config = new TextBox
            {
                Name = "txtConfigRoot",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White
            };
            layout.Controls.Add(config, 1, 3);

            Button openConfig = CreateActionButton("打开 Config（直接编辑前请退出程序）", Color.FromArgb(84, 110, 122));
            openConfig.Click += OpenConfig_Click;
            layout.Controls.Add(openConfig, 0, 4);
            layout.SetColumnSpan(openConfig, 2);
            group.Controls.Add(layout);
            return group;
        }

        private Control BuildWorkspace()
        {
            GroupBox group = CreateGroup("外围设备 HMI 内容区");
            Label placeholder = new Label
            {
                Dock = DockStyle.Fill,
                Text = "在此区域继续开发设备首页、手自动操作、工艺参数、产量等正式业务界面",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(96, 110, 125),
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Regular)
            };
            group.Controls.Add(placeholder);
            return group;
        }

        private Control BuildLogPanel()
        {
            GroupBox group = CreateGroup("运行日志与报警");
            ListView list = new ListView
            {
                Name = "lvRuntimeLog",
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false
            };
            list.Columns.Add("时间", 105);
            list.Columns.Add("级别", 70);
            list.Columns.Add("内容", 760);
            group.Controls.Add(list);
            return group;
        }

        private void RegisterBusinessCallbacks()
        {
            // 外围业务的命名回调统一在这里注册，例如：
            // platformHost.RegisterCustomFunction("设备业务动作", ExecuteDeviceAction);
            // 首版不注册演示动作，避免把样例逻辑误带入正式设备程序。
        }

        private void FrmPeripheralMain_Shown(object sender, EventArgs e)
        {
            refreshTimer.Start();
            RefreshRuntimeView();
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshRuntimeView();
        }

        private void RefreshRuntimeView()
        {
            lblRuntimeState.Text = $"{platformHost.State} - {platformHost.StateMessage}";
            lblRuntimeState.ForeColor = platformHost.State == PlatformRuntimeState.Ready
                ? Color.FromArgb(46, 125, 50)
                : platformHost.State == PlatformRuntimeState.Faulted
                    ? Color.FromArgb(198, 40, 40)
                    : Color.FromArgb(69, 90, 100);

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
            if (!(cmbProcess.SelectedItem is PlatformProcessInfo process))
            {
                MessageBox.Show(this, "请先选择流程。", "流程控制", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!action(process.Index, out string error))
            {
                MessageBox.Show(this, error, "流程控制失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Interlocked.Exchange(ref processRefreshPending, 1);
        }

        private void StopAll_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "确认停止全部流程？", "停止确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            if (!platformHost.TryStopAllProcesses(out string error))
            {
                MessageBox.Show(this, error, "停止失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Interlocked.Exchange(ref processRefreshPending, 1);
        }

        private void OpenConfig_Click(object sender, EventArgs e)
        {
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
                MessageBox.Show(this, $"打开 Config 目录失败:{ex.Message}", "Config", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PlatformHost_RuntimeStateChanged(object sender, PlatformRuntimeStateChangedEventArgs e)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((Action)(() => PlatformHost_RuntimeStateChanged(sender, e)));
                return;
            }
            if (lblRuntimeState != null)
            {
                lblRuntimeState.Text = $"{e.State} - {e.Message}";
            }
        }

        private void PlatformHost_ProcessSnapshotChanged(EngineSnapshot snapshot)
        {
            Interlocked.Exchange(ref processRefreshPending, 1);
        }

        private void PlatformHost_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            // 状态由 UI 定时器统一刷新，避免工作线程直接操作 WinForms 控件。
        }

        private void FrmPeripheralMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!closingConfirmed && e.CloseReason == CloseReason.UserClosing)
            {
                if (MessageBox.Show(this, "确认退出外围程序并停止全部流程？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                closingConfirmed = true;
            }

            refreshTimer.Stop();
            DetachHostEvents();
            platformHost.Shutdown();
        }

        private void DetachHostEvents()
        {
            if (hostEventsDetached)
            {
                return;
            }
            hostEventsDetached = true;
            platformHost.RuntimeStateChanged -= PlatformHost_RuntimeStateChanged;
            platformHost.ProcessSnapshotChanged -= PlatformHost_ProcessSnapshotChanged;
            platformHost.ValueChanged -= PlatformHost_ValueChanged;
        }

        private static GroupBox CreateGroup(string text)
        {
            return new GroupBox
            {
                Text = text,
                BackColor = Color.White,
                Padding = new Padding(8)
            };
        }

        private static Button CreateActionButton(string text, Color color)
        {
            return new Button
            {
                Dock = DockStyle.Fill,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White,
                Margin = new Padding(4),
                UseVisualStyleBackColor = false
            };
        }

        private static void AddStatusRow(TableLayoutPanel layout, int row, string title, string valueControlName)
        {
            layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            layout.Controls.Add(new Label
            {
                Name = valueControlName,
                Text = "-",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            }, 1, row);
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
