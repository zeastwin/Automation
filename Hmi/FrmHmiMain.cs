using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 主窗口。
// 职责范围：按旧 FormMain 的页面和设备控制顺序组织外壳，只通过 IAutomationPlatform 使用平台能力。
// 排查入口：页面不刷新时检查公开 SDK 事件和 refreshTimer；不要从窗体反查 PlatformRuntime。

namespace Automation.Hmi
{
    public sealed partial class FrmHmiMain : Form
    {
        internal const Keys ShowPlatformEditorShortcut = Keys.Alt | Keys.Z;
        internal const string ShowPlatformEditorShortcutText = "Alt+Z";

        private readonly Timer refreshTimer;
        private readonly HmiHomePage homePage;
        private readonly HmiDebugPage debugPage;
        private readonly LegacyVideoPage videoPage;
        private readonly AlarmHistoryPage alarmPage;
        private readonly LegacyDataPage dataPage;
        private readonly LegacyExcelPage excelPage;
        private readonly LegacyLogPage logPage;
        private readonly Form[] pageForms;
        private readonly Button[] navigationButtons;
        private IAutomationPlatform platform;
        private EquipmentProcessMessageService processMessages;
        private LegacyEquipmentServices equipmentServices;
        private bool closingConfirmed;
        private bool hostEventsDetached;

        public FrmHmiMain()
        {
            InitializeComponent();
            homePage = new HmiHomePage();
            debugPage = new HmiDebugPage();
            videoPage = new LegacyVideoPage();
            alarmPage = new AlarmHistoryPage();
            dataPage = new LegacyDataPage();
            excelPage = new LegacyExcelPage();
            logPage = new LegacyLogPage();
            pageForms = new Form[]
            {
                homePage,
                debugPage,
                videoPage,
                alarmPage,
                dataPage,
                excelPage,
                logPage
            };
            navigationButtons = new[]
            {
                btnHome,
                btnDebug,
                btnVideo,
                btnAlarm,
                btnData,
                btnExcel,
                btnLog
            };
            foreach (Form page in pageForms)
            {
                page.TopLevel = false;
                page.FormBorderStyle = FormBorderStyle.None;
                page.ShowInTaskbar = false;
                page.Dock = DockStyle.Fill;
                pageHost.Controls.Add(page);
            }
            refreshTimer = new Timer { Interval = 500 };
            refreshTimer.Tick += (sender, args) => RefreshRuntimeView();
            Shown += FrmHmiMain_Shown;
            FormClosing += FrmHmiMain_FormClosing;
            Disposed += FrmHmiMain_Disposed;
            ShowPage(homePage, btnHome);
        }

        public FrmHmiMain(IAutomationPlatform platform)
            : this(platform, null)
        {
        }

        internal FrmHmiMain(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages)
            : this(platform, processMessages, null)
        {
        }

        internal FrmHmiMain(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages,
            LegacyEquipmentServices equipmentServices)
            : this()
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.processMessages = processMessages;
            this.equipmentServices = equipmentServices;
            homePage.AttachPlatform(platform, processMessages);
            debugPage.AttachPlatform(platform, processMessages, equipmentServices);
            videoPage.AttachPlatform(platform, equipmentServices?.Video);
            alarmPage.AttachProjectConfiguration(
                equipmentServices?.ProjectConfigurationRoot);
            dataPage.AttachPlatform(platform, processMessages);
            excelPage.AttachPlatform(platform);
            logPage.AttachPlatform(platform);
            platform.RuntimeStatusChanged += Platform_RuntimeStatusChanged;
            platform.Processes.Changed += Processes_Changed;
            platform.Values.Changed += Values_Changed;
        }

        private void FrmHmiMain_Shown(object sender, EventArgs e)
        {
            refreshTimer.Start();
            RefreshRuntimeView();
            BeginInvoke((Action)(() => platform?.NotifyInteractionUiReady()));
        }

        private void RefreshRuntimeView()
        {
            lblDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
            lblTime.Text = DateTime.Now.ToString("HH:mm:ss");
            if (platform == null)
            {
                return;
            }
            homePage.RefreshRuntimeView();
            debugPage.RefreshRuntimeView();
            videoPage.RefreshRuntimeView();
            dataPage.RefreshRuntimeView();
            RefreshHeader();
        }

        private void RefreshHeader()
        {
            lblDeviceName.Text = TryReadValue("设备名称", out string deviceName)
                && !string.IsNullOrWhiteSpace(deviceName)
                ? deviceName
                : "JS_ICT_NPI_LAX_XXXX";
            lblVersion.Text = "版本号: V" + (platform.PlatformVersion ?? "3.0.0");
            lblFooterUser.Text = TryReadValue("登录用户名称", out string userName)
                && !string.IsNullOrWhiteSpace(userName)
                ? userName
                : "User";
            btnLogin.Text = string.IsNullOrWhiteSpace(userName)
                ? "请登录"
                : userName;

            IReadOnlyList<ProcessSnapshot> processes = platform.Processes.GetAll();
            ProcessSnapshot main = processes.FirstOrDefault(process => process.Index == 0);
            string fixtureState = ReadFirstValue("治具状态", "Hive设备状态", "设备状态");
            if (string.IsNullOrWhiteSpace(fixtureState))
            {
                fixtureState = main?.State.ToString() ?? platform.RuntimeStatus.ToString();
            }
            lblFixtureStatus.Text = fixtureState;
            ApplyFixtureColor(fixtureState);

            ProcessRuntimeStatus? mainState = main?.State;
            btnStart.BackColor = mainState == ProcessRuntimeStatus.Running
                ? Color.GreenYellow
                : Color.Transparent;
            bool paused = mainState == ProcessRuntimeStatus.Paused
                || mainState == ProcessRuntimeStatus.Pausing;
            btnPause.BackColor = paused ? Color.Khaki : Color.Transparent;
            btnStop.BackColor = mainState == ProcessRuntimeStatus.Stopped
                || mainState == ProcessRuntimeStatus.Ready
                ? Color.LightCoral
                : Color.Transparent;
        }

        private void btnHome_Click(object sender, EventArgs e)
        {
            ShowPage(homePage, btnHome);
        }

        private void btnDebug_Click(object sender, EventArgs e)
        {
            ShowPage(debugPage, btnDebug);
        }

        private void btnVideo_Click(object sender, EventArgs e)
        {
            ShowPage(videoPage, btnVideo);
            videoPage.RefreshRuntimeView();
        }

        private void btnAlarm_Click(object sender, EventArgs e)
        {
            ShowPage(alarmPage, btnAlarm);
            alarmPage.LoadSelectedDate();
        }

        private void btnData_Click(object sender, EventArgs e)
        {
            ShowPage(dataPage, btnData);
            dataPage.RefreshRuntimeView();
        }

        private void btnExcel_Click(object sender, EventArgs e)
        {
            ShowPage(excelPage, btnExcel);
            excelPage.RefreshRuntimeView();
        }

        private void btnLog_Click(object sender, EventArgs e)
        {
            ShowPage(logPage, btnLog);
            logPage.RefreshRuntimeView();
        }

        private void lblDeviceName_Click(object sender, EventArgs e)
        {
            ShowPage(homePage, btnHome);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (platform == null)
            {
                return;
            }
            if (!platform.Processes.Start(0, out string error))
            {
                MessageBox.Show(this, error, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            debugPage.MarkProcessListDirty();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            if (platform == null)
            {
                return;
            }
            if (!platform.Processes.StopAll(out string error))
            {
                MessageBox.Show(this, error, "停止失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            debugPage.MarkProcessListDirty();
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (platform == null)
            {
                return;
            }
            IReadOnlyList<ProcessSnapshot> processes = platform.Processes.GetAll();
            bool resume = processes.Any(process =>
                process.State == ProcessRuntimeStatus.Paused
                || process.State == ProcessRuntimeStatus.Pausing);
            var failures = new List<string>();
            foreach (ProcessSnapshot process in processes)
            {
                bool relevant = resume
                    ? process.State == ProcessRuntimeStatus.Paused
                    : process.State == ProcessRuntimeStatus.Running
                        || process.State == ProcessRuntimeStatus.SingleStep;
                if (!relevant)
                {
                    continue;
                }
                bool succeeded = resume
                    ? platform.Processes.Resume(process.Index, out string error)
                    : platform.Processes.Pause(process.Index, out error);
                if (!succeeded)
                {
                    failures.Add($"{process.Index} {process.Name}: {error}");
                }
            }
            if (failures.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, failures),
                    resume ? "恢复失败" : "暂停失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            debugPage.MarkProcessListDirty();
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            string user = TryReadValue("登录用户名称", out string value)
                ? value
                : string.Empty;
            MessageBox.Show(
                this,
                string.IsNullOrWhiteSpace(user)
                    ? "新平台尚未公开用户认证接口；请在调试页 FingerPrint 查看适配状态。"
                    : "当前用户：" + user,
                "登录",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void lblFixtureStatus_Click(object sender, EventArgs e)
        {
            if (platform == null)
            {
                return;
            }
            using (var stateShow = new HmiStateShowForm(platform))
            {
                stateShow.ShowDialog(this);
            }
        }

        protected override bool ProcessCmdKey(
            ref System.Windows.Forms.Message msg,
            Keys keyData)
        {
            if (keyData == ShowPlatformEditorShortcut)
            {
                ShowPlatformEditor();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowPlatformEditor()
        {
            if (platform == null)
            {
                return;
            }
            try
            {
                platform.ShowPlatformEditor();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "平台不可用",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
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
            foreach (Button button in navigationButtons)
            {
                button.BackColor = ReferenceEquals(button, navigationButton)
                    ? Color.LightGreen
                    : Color.Transparent;
            }
        }

        private void Platform_RuntimeStatusChanged(
            object sender,
            PlatformRuntimeStatusChangedEventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => Platform_RuntimeStatusChanged(sender, e)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            debugPage.UpdateRuntimeState(e.Status, e.Message);
            RefreshHeader();
        }

        private void Processes_Changed(object sender, ProcessChangedEventArgs e)
        {
            debugPage.MarkProcessListDirty();
            RefreshHeader();
        }

        private void Values_Changed(
            object sender,
            Automation.DeviceSdk.ValueChangedEventArgs e)
        {
            // 统一由 UI 定时器读取快照，流程线程不直接操作 WinForms 控件。
        }

        private void FrmHmiMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!closingConfirmed && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                bool wasTopMost = TopMost;
                try
                {
                    TopMost = true;
                    BringToFront();
                    Activate();
                    if (MessageBox.Show(
                        this,
                        "确定要退出吗？",
                        "提示",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question) == DialogResult.OK)
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
            platform?.Shutdown();
        }

        private void FrmHmiMain_Disposed(object sender, EventArgs e)
        {
            refreshTimer.Dispose();
            foreach (Button button in commandBar.Controls.OfType<Button>())
            {
                button.Image?.Dispose();
                button.Image = null;
            }
            DetachHostEvents();
        }

        private void DetachHostEvents()
        {
            if (hostEventsDetached || platform == null)
            {
                return;
            }
            hostEventsDetached = true;
            platform.RuntimeStatusChanged -= Platform_RuntimeStatusChanged;
            platform.Processes.Changed -= Processes_Changed;
            platform.Values.Changed -= Values_Changed;
        }

        private bool TryReadValue(string name, out string value)
        {
            value = string.Empty;
            if (platform == null
                || !platform.Values.TryGet(name, out ValueSnapshot snapshot, out _)
                || snapshot == null)
            {
                return false;
            }
            value = snapshot.Value ?? string.Empty;
            return true;
        }

        private string ReadFirstValue(params string[] names)
        {
            foreach (string name in names)
            {
                if (TryReadValue(name, out string value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return string.Empty;
        }

        private void ApplyFixtureColor(string state)
        {
            string normalized = (state ?? string.Empty).Trim();
            if (normalized.IndexOf("Running", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.Contains("运行"))
            {
                lblFixtureStatus.BackColor = Color.Lime;
            }
            else if (normalized.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.Contains("空闲")
                || normalized.Contains("Ready"))
            {
                lblFixtureStatus.BackColor = Color.Yellow;
            }
            else if (normalized.IndexOf("Engineering", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.Contains("工程"))
            {
                lblFixtureStatus.BackColor = Color.Plum;
            }
            else if (normalized.IndexOf("Down", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.Contains("报警")
                || normalized.Contains("Fault"))
            {
                lblFixtureStatus.BackColor = Color.Red;
            }
            else
            {
                lblFixtureStatus.BackColor = SystemColors.ControlLight;
            }
        }
    }
}
