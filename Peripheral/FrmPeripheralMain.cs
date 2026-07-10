using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation.Peripheral
{
    public sealed partial class FrmPeripheralMain : Form
    {
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly PeripheralHomePage homePage;
        private readonly PeripheralDebugPage debugPage;
        private readonly AlarmHistoryPage alarmHistoryPage;
        private readonly PeripheralCapacityPage capacityPage;
        private readonly Form[] pageForms;
        private AutomationPlatformHost platformHost;
        private bool closingConfirmed;
        private bool hostEventsDetached;

        public FrmPeripheralMain()
        {
            InitializeComponent();
            homePage = new PeripheralHomePage();
            debugPage = new PeripheralDebugPage();
            alarmHistoryPage = new AlarmHistoryPage();
            capacityPage = new PeripheralCapacityPage();
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
            Shown += FrmPeripheralMain_Shown;
            FormClosing += FrmPeripheralMain_FormClosing;
            Disposed += FrmPeripheralMain_Disposed;
            ShowPage(homePage, btnHome);
        }

        public FrmPeripheralMain(AutomationPlatformHost platformHost)
            : this()
        {
            this.platformHost = platformHost ?? throw new ArgumentNullException(nameof(platformHost));
            debugPage.AttachHost(platformHost);
            platformHost.RuntimeStateChanged += PlatformHost_RuntimeStateChanged;
            platformHost.ProcessSnapshotChanged += PlatformHost_ProcessSnapshotChanged;
            platformHost.ValueChanged += PlatformHost_ValueChanged;
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
            platformHost.ShowPlatformEditor(this);
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
                button.BackColor = active ? Color.FromArgb(32, 104, 163) : Color.FromArgb(24, 38, 54);
                button.ForeColor = active ? Color.White : Color.FromArgb(202, 216, 228);
            }
        }

        private void PlatformHost_RuntimeStateChanged(object sender, PlatformRuntimeStateChangedEventArgs e)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((Action)(() => PlatformHost_RuntimeStateChanged(sender, e)));
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
            platformHost?.Shutdown();
        }

        private void FrmPeripheralMain_Disposed(object sender, EventArgs e)
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
