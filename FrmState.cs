using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmState : Form
    {
        private const string SystemStatusValueName = "系统状态";
        private string basicInfo;
        private System.Windows.Forms.Timer systemStatusTimer;
        private const int SystemStatusPollIntervalMs = 500;

        public FrmState()
        {
            InitializeComponent();
            ConfigureAppearance();
            Disposed += FrmState_Disposed;
        }

        private void ConfigureAppearance()
        {
            BackColor = UiPalette.Background;
            Paint += (sender, args) =>
            {
                using (Pen pen = new Pen(UiPalette.Stroke))
                {
                    args.Graphics.DrawLine(pen, 0, 0, ClientSize.Width, 0);
                }
            };
            lblSystemStatus.AutoSize = false;
            lblSystemStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblSystemStatus.TextAlign = ContentAlignment.MiddleLeft;
            SysInfo.AutoSize = false;
            SysInfo.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            SysInfo.ForeColor = UiPalette.TextSecondary;
            SysInfo.TextAlign = ContentAlignment.MiddleLeft;
            LayoutStatusBar();
        }

        private void LayoutStatusBar()
        {
            int height = Math.Max(1, ClientSize.Height - 1);
            Size statusTextSize = TextRenderer.MeasureText(
                string.IsNullOrEmpty(lblSystemStatus.Text) ? "●  系统状态：未初始化" : lblSystemStatus.Text,
                lblSystemStatus.Font);
            int statusWidth = Math.Max(170, statusTextSize.Width + 24);
            lblSystemStatus.SetBounds(12, 1, statusWidth, height);
            SysInfo.SetBounds(lblSystemStatus.Right + 12, 1,
                Math.Max(0, ClientSize.Width - lblSystemStatus.Right - 24), height);
        }
        private void FrmState_Load(object sender, EventArgs e)
        {
            InitializeSystemStatusTimer();
            RefreshBasicInfo();
        }

        public void RefreshBasicInfo()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)RefreshBasicInfo);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            basicInfo = string.Empty;
            bool hasStatus = TryGetSystemStatus(out SystemStatus status);
            string systemStatus = GetSystemStatusText(hasStatus, status);
            SysInfo.Text = basicInfo;
            lblSystemStatus.Text = $"●  系统状态：{systemStatus}";
            lblSystemStatus.ForeColor = GetSystemStatusColor(hasStatus, status);
            LayoutStatusBar();
        }
        
        private void InitializeSystemStatusTimer()
        {
            if (systemStatusTimer != null)
            {
                return;
            }
            systemStatusTimer = new System.Windows.Forms.Timer();
            systemStatusTimer.Interval = SystemStatusPollIntervalMs;
            systemStatusTimer.Tick += SystemStatusTimer_Tick;
            VisibleChanged += FrmState_VisibleChanged;
            Resize += FrmState_Resize;
            UpdateSystemStatusTimerState();
        }

        private void SystemStatusTimer_Tick(object sender, EventArgs e) => RefreshBasicInfo();

        private void FrmState_VisibleChanged(object sender, EventArgs e) => UpdateSystemStatusTimerState();

        private void FrmState_Resize(object sender, EventArgs e)
        {
            LayoutStatusBar();
            UpdateSystemStatusTimerState();
        }

        private void FrmState_Disposed(object sender, EventArgs e)
        {
            VisibleChanged -= FrmState_VisibleChanged;
            Resize -= FrmState_Resize;
            if (systemStatusTimer == null)
            {
                return;
            }
            systemStatusTimer.Stop();
            systemStatusTimer.Tick -= SystemStatusTimer_Tick;
            systemStatusTimer.Dispose();
            systemStatusTimer = null;
        }

        private void UpdateSystemStatusTimerState()
        {
            if (systemStatusTimer == null)
            {
                return;
            }
            if (IsDisposed || !Visible || WindowState == FormWindowState.Minimized)
            {
                systemStatusTimer.Stop();
                return;
            }
            systemStatusTimer.Start();
        }

        private bool TryGetSystemStatus(out SystemStatus status)
        {
            status = SystemStatus.Uninitialized;
            if (Workspace.Runtime.Stores.Values == null)
            {
                return false;
            }
            if (!Workspace.Runtime.Stores.Values.TryGetValueByName(SystemStatusValueName, out DicValue value) || value == null)
            {
                return false;
            }
            if (!string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!double.TryParse(value.Value, out double rawValue))
            {
                return false;
            }
            int intValue = (int)rawValue;
            if (rawValue != intValue)
            {
                return false;
            }
            if (!Enum.IsDefined(typeof(SystemStatus), intValue))
            {
                return false;
            }
            status = (SystemStatus)intValue;
            return true;
        }

        private string GetSystemStatusText(bool hasStatus, SystemStatus status)
        {
            if (!hasStatus)
            {
                return "未知";
            }
            switch (status)
            {
                case SystemStatus.Uninitialized:
                    return "未初始化";
                case SystemStatus.ProcAlarm:
                    return "流程报警";
                case SystemStatus.Ready:
                    return "就绪";
                case SystemStatus.Working:
                    return "工作中";
                case SystemStatus.Paused:
                    return "暂停工作";
                case SystemStatus.PopupAlarm:
                    return "弹框报警";
                default:
                    return "未知";
            }
        }

        private Color GetSystemStatusColor(bool hasStatus, SystemStatus status)
        {
            if (!hasStatus)
            {
                return UiPalette.TextMuted;
            }
            switch (status)
            {
                case SystemStatus.Uninitialized:
                    return UiPalette.TextMuted;
                case SystemStatus.ProcAlarm:
                    return UiPalette.Danger;
                case SystemStatus.Ready:
                    return UiPalette.Brand;
                case SystemStatus.Working:
                    return UiPalette.Success;
                case SystemStatus.Paused:
                    return UiPalette.Warning;
                case SystemStatus.PopupAlarm:
                    return UiPalette.Danger;
                default:
                    return UiPalette.TextMuted;
            }
        }
    }
}
