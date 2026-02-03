using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmState : Form , INotifyPropertyChanged
    {

        private string basicInfo;
        private string displayInfo;

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
            DisplayInfo = basicInfo;
        }
    }
}
