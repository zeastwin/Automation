using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation.Hmi
{
    public sealed partial class FrmHmiMain : Form
    {
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly HmiHomePage homePage;
        private readonly HmiDebugPage debugPage;
        private readonly AlarmHistoryPage alarmHistoryPage;
        private readonly HmiCapacityPage capacityPage;
        private readonly Form[] pageForms;
        private AutomationPlatformHost platformHost;
        private bool closingConfirmed;
        private bool hostEventsDetached;

        public FrmHmiMain()
        {
            InitializeComponent();
            UiBranding.Apply(this);
            headerPanel.Resize += HeaderPanel_Resize;
            homePage = new HmiHomePage();
            debugPage = new HmiDebugPage();
            alarmHistoryPage = new AlarmHistoryPage();
            capacityPage = new HmiCapacityPage();
            pageForms = new Form[] { homePage, debugPage, alarmHistoryPage, capacityPage };
            foreach (Form pageForm in pageForms)
            {
                pageForm.TopLevel = false;
                pageForm.FormBorderStyle = FormBorderStyle.None;
                pageForm.ShowInTaskbar = false;
                pageForm.Dock = DockStyle.Fill;
                pageHost.Controls.Add(pageForm);
            }
            refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
            refreshTimer.Tick += RefreshTimer_Tick;
            Shown += FrmHmiMain_Shown;
            FormClosing += FrmHmiMain_FormClosing;
            Disposed += FrmHmiMain_Disposed;
            ShowPage(homePage, btnHome);
            CenterNavigationButtons();
        }

        private void HeaderPanel_Resize(object sender, EventArgs e)
        {
            CenterNavigationButtons();
        }

        private void CenterNavigationButtons()
        {
            int centeredLeft = (headerPanel.ClientSize.Width - navigationPanel.Width) / 2;
            int maximumLeft = btnStartProcess.Left - navigationPanel.Width - 12;
            navigationPanel.Left = Math.Max(12, Math.Min(centeredLeft, maximumLeft));
            navigationPanel.Top = Math.Max(0, (headerPanel.ClientSize.Height - navigationPanel.Height) / 2);
        }

        public FrmHmiMain(AutomationPlatformHost platformHost)
            : this()
        {
            this.platformHost = platformHost ?? throw new ArgumentNullException(nameof(platformHost));
            debugPage.AttachHost(platformHost);
            platformHost.RuntimeStateChanged += PlatformHost_RuntimeStateChanged;
            platformHost.ProcessSnapshotChanged += PlatformHost_ProcessSnapshotChanged;
            platformHost.ValueChanged += PlatformHost_ValueChanged;
        }

        private void FrmHmiMain_Shown(object sender, EventArgs e)
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
            if (platformHost == null)
            {
                return;
            }
            debugPage.RefreshRuntimeView();
        }

        private void btnHome_Click(object sender, EventArgs e)
        {
            ShowPage(homePage, btnHome);
        }

        private void btnDebug_Click(object sender, EventArgs e)
        {
            ShowPage(debugPage, btnDebug);
        }

        private void btnAlarmHistory_Click(object sender, EventArgs e)
        {
            ShowPage(alarmHistoryPage, btnAlarmHistory);
            alarmHistoryPage.LoadSelectedDate();
        }

        private void btnCapacity_Click(object sender, EventArgs e)
        {
            ShowPage(capacityPage, btnCapacity);
        }

        private void btnOpenPlatform_Click(object sender, EventArgs e)
        {
            if (platformHost == null)
            {
                return;
            }
            try
            {
                platformHost.ShowPlatformEditor(this);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "平台不可用", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnStopAll_Click(object sender, EventArgs e)
        {
            if (platformHost == null)
            {
                return;
            }
            if (MessageBox.Show(this, "确认停止全部流程？", "停止确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            if (!platformHost.TryStopAllProcesses(out string error))
            {
                MessageBox.Show(this, error, "停止失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            debugPage.MarkProcessListDirty();
        }

        private void ShowPage(Form page, Button navigationButton)
        {
            foreach (Form pageForm in pageForms)
            {
                if (ReferenceEquals(pageForm, page))
                {
                    if (!pageForm.Visible)
                    {
                        pageForm.Show();
                    }
                }
                else
                {
                    pageForm.Hide();
                }
            }
            page.BringToFront();
            SetActiveNavigation(navigationButton);
        }

        private void SetActiveNavigation(Button selected)
        {
            Button[] buttons = { btnHome, btnDebug, btnAlarmHistory, btnCapacity };
            foreach (Button button in buttons)
            {
                bool active = ReferenceEquals(button, selected);
                button.BackColor = active ? Color.FromArgb(20, 126, 197) : Color.FromArgb(24, 38, 54);
                button.ForeColor = Color.White;
            }
        }

        private void PlatformHost_RuntimeStateChanged(object sender, PlatformRuntimeStateChangedEventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => PlatformHost_RuntimeStateChanged(sender, e)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            debugPage.UpdateRuntimeState(e.State, e.Message);
        }

        private void PlatformHost_ProcessSnapshotChanged(EngineSnapshot snapshot)
        {
            debugPage.MarkProcessListDirty();
        }

        private void PlatformHost_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            // 由界面定时器统一读取状态，避免流程线程操作 WinForms 控件。
        }

        private void FrmHmiMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!closingConfirmed && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                bool wasTopMost = TopMost;
                try
                {
                    // 从任务栏关闭时 HMI 不一定处于激活状态；临时置顶确保确认框不会被独立工具窗体遮挡。
                    TopMost = true;
                    BringToFront();
                    Activate();
                    if (MessageBox.Show(this, "确认退出程序并停止全部流程？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        closingConfirmed = true;
                        e.Cancel = false;
                    }
                }
                finally
                {
                    TopMost = wasTopMost;
                }
                if (e.Cancel)
                {
                    return;
                }
            }

            refreshTimer.Stop();
            DetachHostEvents();
            platformHost?.Shutdown();
        }

        private void FrmHmiMain_Disposed(object sender, EventArgs e)
        {
            refreshTimer.Dispose();
            DetachHostEvents();
        }

        private void DetachHostEvents()
        {
            if (hostEventsDetached || platformHost == null)
            {
                return;
            }
            hostEventsDetached = true;
            platformHost.RuntimeStateChanged -= PlatformHost_RuntimeStateChanged;
            platformHost.ProcessSnapshotChanged -= PlatformHost_ProcessSnapshotChanged;
            platformHost.ValueChanged -= PlatformHost_ValueChanged;
        }
    }
}
