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
           
        }
        private void FrmState_Load(object sender, EventArgs e)
        {
            InitializeSystemStatusTimer();
            RefreshBasicInfo();
        }

        public void RefreshBasicInfo()
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)RefreshBasicInfo);
                return;
            }

            string userName = "未登录";
            UserContextSnapshot snapshot = SF.userContextStore?.GetSnapshot();
            if (snapshot != null && snapshot.IsLoggedIn)
            {
                userName = snapshot.UserName;
            }

            basicInfo = $"用户:{userName}";
            bool hasStatus = TryGetSystemStatus(out SystemStatus status);
            string systemStatus = GetSystemStatusText(hasStatus, status);
            SysInfo.Text = basicInfo;
            lblSystemStatus.Text = $"系统状态:{systemStatus}";
            lblSystemStatus.ForeColor = GetSystemStatusColor(hasStatus, status);
            int nextX = SysInfo.Right + 20;
            if (nextX < 6)
            {
                nextX = 6;
            }
            lblSystemStatus.Location = new Point(nextX, SysInfo.Top);
        }
        
        private void InitializeSystemStatusTimer()
        {
            if (systemStatusTimer != null)
            {
                return;
            }
            systemStatusTimer = new System.Windows.Forms.Timer();
            systemStatusTimer.Interval = SystemStatusPollIntervalMs;
            systemStatusTimer.Tick += (s, e) => RefreshBasicInfo();
            VisibleChanged += (s, e) => UpdateSystemStatusTimerState();
            Resize += (s, e) => UpdateSystemStatusTimerState();
            UpdateSystemStatusTimerState();
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
            if (SF.valueStore == null)
            {
                return false;
            }
            if (!SF.valueStore.TryGetValueByName(SystemStatusValueName, out DicValue value) || value == null)
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
                return Color.DimGray;
            }
            switch (status)
            {
                case SystemStatus.Uninitialized:
                    return Color.DimGray;
                case SystemStatus.ProcAlarm:
                    return Color.Red;
                case SystemStatus.Ready:
                    return Color.DodgerBlue;
                case SystemStatus.Working:
                    return Color.ForestGreen;
                case SystemStatus.Paused:
                    return Color.Orange;
                case SystemStatus.PopupAlarm:
                    return Color.Red;
                default:
                    return Color.DimGray;
            }
        }
    }
}
