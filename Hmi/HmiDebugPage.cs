using System;
using System.Drawing;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 调试页面。
// 职责范围：保留旧 DebugApp 的八个入口；固定布局在 Designer，运行时只装载业务子页。

namespace Automation.Hmi
{
    public sealed partial class HmiDebugPage : Form
    {
        private readonly LegacyProtocolDebugControl mesPage;
        private readonly LegacyProtocolDebugControl pdcaPage;
        private readonly LegacyProtocolDebugControl hivePage;
        private readonly LegacyPlcDebugControl plcPage;
        private readonly LegacyFingerprintControl fingerprintPage;
        private readonly LegacyToolsControl toolsPage;
        private readonly LegacySetControl setPage;
        private readonly LegacyDatabaseControl databasePage;

        public HmiDebugPage()
        {
            InitializeComponent();

            mesPage = new LegacyProtocolDebugControl("MES");
            pdcaPage = new LegacyProtocolDebugControl("PDCA");
            hivePage = new LegacyProtocolDebugControl("HIVE");
            plcPage = new LegacyPlcDebugControl();
            fingerprintPage = new LegacyFingerprintControl();
            toolsPage = new LegacyToolsControl();
            setPage = new LegacySetControl();
            databasePage = new LegacyDatabaseControl();

            AddDebugPage(buttonMes, mesPage);
            AddDebugPage(buttonPdca, pdcaPage);
            AddDebugPage(buttonHive, hivePage);
            AddDebugPage(buttonPlc, plcPage);
            AddDebugPage(buttonFingerprint, fingerprintPage);
            AddDebugPage(buttonTools, toolsPage);
            AddDebugPage(buttonSet, setPage);
            AddDebugPage(buttonDatabase, databasePage);
            ShowPage(buttonMes, mesPage);
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages)
        {
            AttachPlatform(platform, processMessages, null);
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages,
            LegacyEquipmentServices equipmentServices)
        {
            mesPage.Attach(platform, processMessages);
            pdcaPage.Attach(platform, processMessages);
            hivePage.Attach(platform, processMessages);
            plcPage.Attach(platform);
            fingerprintPage.Attach(platform);
            toolsPage.Attach(platform, processMessages);
            setPage.Attach(platform);
            databasePage.AttachDatabaseService(equipmentServices?.Database);
            RefreshRuntimeView();
        }

        internal void RefreshRuntimeView()
        {
            if (mesPage.Visible) mesPage.RefreshView();
            else if (pdcaPage.Visible) pdcaPage.RefreshView();
            else if (hivePage.Visible) hivePage.RefreshView();
            else if (plcPage.Visible) plcPage.RefreshView();
            else if (fingerprintPage.Visible) fingerprintPage.RefreshView();
            else if (toolsPage.Visible) toolsPage.RefreshView();
            else if (setPage.Visible) setPage.RefreshView();
            else if (databasePage.Visible) databasePage.RefreshView();
        }

        internal void MarkProcessListDirty()
        {
            RefreshRuntimeView();
        }

        internal void UpdateRuntimeState(PlatformRuntimeStatus state, string message)
        {
            RefreshRuntimeView();
        }

        private void AddDebugPage(Button button, Control page)
        {
            button.Click += (sender, args) => ShowPage(button, page);
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            pageHost.Controls.Add(page);
        }

        private void ShowPage(Button selectedButton, Control selectedPage)
        {
            foreach (Control control in buttonBar.Controls)
            {
                if (control is Button button)
                {
                    button.BackColor = ReferenceEquals(button, selectedButton)
                        ? Color.GreenYellow
                        : Color.Gray;
                }
            }

            foreach (Control page in pageHost.Controls)
            {
                page.Visible = ReferenceEquals(page, selectedPage);
            }
            selectedPage.BringToFront();
            RefreshRuntimeView();
        }
    }
}
