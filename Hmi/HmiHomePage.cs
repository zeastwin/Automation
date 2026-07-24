using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 首页。
// 职责范围：在新平台首页承载旧项目 OpDlg 及其内部五个业务页面。
// 排查入口：显示异常先检查 AttachPlatform、平台变量和 CoreWork 消息适配器快照。

namespace Automation.Hmi
{
    public partial class HmiHomePage : Form
    {
        private readonly LegacyMainPageControl mainPage;
        private readonly LegacyLogPageControl logPage;
        private readonly LegacyHivePageControl hivePage;
        private readonly LegacyCurvePageControl pressurePage;
        private readonly LegacyCurvePageControl torquePage;
        private readonly Control[] pages;
        private readonly Button[] pageButtons;
        private readonly DateTime startedAt = DateTime.Now;
        private IAutomationPlatform platform;
        private EquipmentProcessMessageService processMessages;
        private bool refreshingControls;

        public HmiHomePage()
        {
            InitializeComponent();
            mainPage = new LegacyMainPageControl();
            logPage = new LegacyLogPageControl();
            hivePage = new LegacyHivePageControl();
            pressurePage = new LegacyCurvePageControl(false);
            torquePage = new LegacyCurvePageControl(true);
            pages = new Control[] { mainPage, logPage, hivePage, pressurePage, torquePage };
            pageButtons = new[]
            {
                mainPageButton,
                logPageButton,
                hivePageButton,
                pressurePageButton,
                torquePageButton
            };
            foreach (Control page in pages)
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                pageHost.Controls.Add(page);
            }

            mainPageButton.Click += (sender, args) => ShowPage(0);
            logPageButton.Click += (sender, args) => ShowPage(1);
            hivePageButton.Click += (sender, args) => ShowPage(2);
            pressurePageButton.Click += (sender, args) => ShowPage(3);
            torquePageButton.Click += (sender, args) => ShowPage(4);
            workModeCombo.SelectedIndexChanged += WorkModeCombo_SelectedIndexChanged;
            mesDisabledCheck.Click += MesDisabledCheck_Click;
            pdcaDisabledCheck.Click += PdcaDisabledCheck_Click;
            pcManagedCheck.Click += PcManagedCheck_Click;
            ShowPage(0);
        }

        internal void AttachPlatform(IAutomationPlatform platform)
        {
            AttachPlatform(platform, null);
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.processMessages = processMessages;
            mainPage.Attach(platform, processMessages);
        }

        internal void RefreshRuntimeView()
        {
            if (platform == null)
            {
                return;
            }

            refreshingControls = true;
            try
            {
                RefreshOperationPanel();
                EquipmentProcessMessageSnapshot snapshot =
                    processMessages?.GetSnapshot() ?? CreateEmptySnapshot();
                mainPage.Render(snapshot);
                logPage.Render(snapshot);
                hivePage.Render(platform, snapshot);
                pressurePage.Render(platform);
                torquePage.Render(platform);
                RefreshAlarmTicker(snapshot);
            }
            finally
            {
                refreshingControls = false;
            }
        }

        private void RefreshOperationPanel()
        {
            int workMode = ReadInteger("工作模式", 0);
            workModeCombo.SelectedIndex = workMode == 1 ? 0 : 1;
            workModeCombo.BackColor = workMode == 1 ? Color.LimeGreen : Color.Yellow;

            bool pcManaged = ReadInteger("PC管理", 0) == 1;
            pcManagedCheck.Checked = pcManaged;
            mesDisabledCheck.Checked = ReadInteger("禁用MES", 0) == 1;
            pdcaDisabledCheck.Checked = ReadInteger("禁用PDCA", 0) == 1;
            mesDisabledCheck.Enabled = pcManaged;
            pdcaDisabledCheck.Enabled = pcManaged;

            versionLabel.Text = ReadString(
                "软件版本",
                string.IsNullOrWhiteSpace(platform.PlatformVersion)
                    ? "EW_Version_3.0.0"
                    : platform.PlatformVersion);

            TimeSpan elapsed = DateTime.Now - startedAt;
            runTimeLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                "运行：{0:00}天{1:00}小时{2:00}分{3:00}秒",
                (int)elapsed.TotalDays,
                elapsed.Hours,
                elapsed.Minutes,
                elapsed.Seconds);
            SetSystemStatus(GetSystemStatus());
        }

        private string GetSystemStatus()
        {
            IReadOnlyList<ProcessSnapshot> processes = platform.Processes.GetAll();
            if (processes.Any(item => item.IsAlarm
                || item.State == ProcessRuntimeStatus.Alarming))
            {
                return "报警";
            }
            if (processes.Any(item => item.State == ProcessRuntimeStatus.Running))
            {
                return "运行";
            }
            if (processes.Any(item => item.State == ProcessRuntimeStatus.Paused
                || item.State == ProcessRuntimeStatus.Pausing))
            {
                return "暂停";
            }
            if (platform.RuntimeStatus == PlatformRuntimeStatus.Faulted)
            {
                return "故障";
            }
            if (platform.RuntimeStatus == PlatformRuntimeStatus.Initializing)
            {
                return "初始化";
            }
            return "就绪";
        }

        private void SetSystemStatus(string status)
        {
            systemStatusLabel.Text = status;
            systemStatusLabel.ForeColor = status == "报警" || status == "故障"
                ? Color.Red
                : status == "运行"
                    ? Color.Green
                    : status == "暂停"
                        ? Color.DarkOrange
                        : Color.Black;
        }

        private void RefreshAlarmTicker(EquipmentProcessMessageSnapshot snapshot)
        {
            var messages = snapshot.Alarms
                .Select(item => string.IsNullOrWhiteSpace(item.Resolution)
                    ? item.Message
                    : item.Message + "；处理：" + item.Resolution)
                .ToList();
            if (ReadInteger("报警状态", 0) != 0)
            {
                messages.Add(ReadString("报警信息", "设备报警"));
            }
            foreach (ProcessSnapshot process in platform.Processes.GetAll())
            {
                if (process.IsAlarm && !string.IsNullOrWhiteSpace(process.AlarmMessage))
                {
                    messages.Add(process.Name + "：" + process.AlarmMessage);
                }
            }
            alarmTicker.SetMessages(messages);
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                bool selected = i == index;
                pages[i].Visible = selected;
                if (selected)
                {
                    pages[i].BringToFront();
                }
                pageButtons[i].BackColor = selected ? Color.GreenYellow : SystemColors.Control;
                pageButtons[i].ForeColor = SystemColors.ControlText;
            }
        }

        private void WorkModeCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (refreshingControls || platform == null || workModeCombo.SelectedIndex < 0)
            {
                return;
            }
            SetValue("工作模式", workModeCombo.SelectedIndex == 0 ? 1 : 0);
        }

        private void MesDisabledCheck_Click(object sender, EventArgs e)
        {
            if (!refreshingControls)
            {
                SetValue("禁用MES", mesDisabledCheck.Checked ? 1 : 0);
            }
        }

        private void PdcaDisabledCheck_Click(object sender, EventArgs e)
        {
            if (!refreshingControls)
            {
                SetValue("禁用PDCA", pdcaDisabledCheck.Checked ? 1 : 0);
            }
        }

        private void PcManagedCheck_Click(object sender, EventArgs e)
        {
            if (!refreshingControls)
            {
                SetValue("PC管理", pcManagedCheck.Checked ? 1 : 0);
            }
        }

        private int ReadInteger(string name, int fallback)
        {
            string value = ReadString(name, string.Empty);
            return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double number)
                ? Convert.ToInt32(number)
                : fallback;
        }

        private string ReadString(string name, string fallback)
        {
            return platform.Values.TryGet(name, out ValueSnapshot value, out _)
                && value != null
                ? value.Value
                : fallback;
        }

        private void SetValue(string name, object value)
        {
            if (!platform.Values.Set(name, value, out string error))
            {
                MessageBox.Show(
                    this,
                    error,
                    "变量写入失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static EquipmentProcessMessageSnapshot CreateEmptySnapshot()
        {
            return new EquipmentProcessMessageSnapshot
            {
                InputRecords = Array.Empty<EquipmentProductionRecord>(),
                OutputRecords = Array.Empty<EquipmentProductionRecord>(),
                Alarms = Array.Empty<EquipmentAlarmRecord>(),
                Logs = Array.Empty<string>()
            };
        }
    }
}
