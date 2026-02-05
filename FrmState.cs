using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmState : Form , INotifyPropertyChanged
    {
        private const string SystemStatusValueName = "系统状态";
        private string basicInfo;
        private string displayInfo;
        private System.Windows.Forms.Timer systemStatusTimer;
        private const int SystemStatusPollIntervalMs = 500;

        public string DisplayInfo
        {
            get
            {
                return displayInfo;
            }
            private set
            {
                displayInfo = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayInfo)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FrmState()
        {
            InitializeComponent();
           
        }
        private void FrmState_Load(object sender, EventArgs e)
        {
            SysInfo.DataBindings.Add(new Binding("Text", this, "DisplayInfo"));
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
            string systemStatus = GetSystemStatusText();
            DisplayInfo = $"{basicInfo}  系统状态:{systemStatus}";
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

        private string GetSystemStatusText()
        {
            if (SF.valueStore == null)
            {
                return "未知";
            }
            if (!SF.valueStore.TryGetValueByName(SystemStatusValueName, out DicValue value) || value == null)
            {
                return "未知";
            }
            if (!string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                return "未知";
            }
            if (!double.TryParse(value.Value, out double rawValue))
            {
                return "未知";
            }
            int intValue = (int)rawValue;
            if (rawValue != intValue)
            {
                return "未知";
            }
            switch (intValue)
            {
                case (int)SystemStatus.Uninitialized:
                    return "未初始化";
                case (int)SystemStatus.ProcAlarm:
                    return "流程报警";
                case (int)SystemStatus.Ready:
                    return "就绪";
                case (int)SystemStatus.Working:
                    return "工作中";
                case (int)SystemStatus.Paused:
                    return "暂停工作";
                case (int)SystemStatus.PopupAlarm:
                    return "弹框报警";
                default:
                    return "未知";
            }
        }
    }
}
