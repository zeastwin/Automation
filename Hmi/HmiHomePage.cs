using System;
using System.Windows.Forms;

namespace Automation.Hmi
{
    public partial class HmiHomePage : Form
    {
        private AutomationPlatformHost platformHost;
        private string lastSystemStatusText = string.Empty;

        public HmiHomePage()
        {
            InitializeComponent();
        }

        internal void AttachHost(AutomationPlatformHost host)
        {
            platformHost = host;
        }

        internal void RefreshRuntimeView()
        {
            if (platformHost == null)
            {
                return;
            }

            // 读取系统状态变量（double），转换为中文状态文本
            if (platformHost.TryGetValue("系统状态", out PlatformValueSnapshot snapshot, out _) &&
                snapshot != null)
            {
                string chineseStatus = ConvertToChineseStatus(snapshot.Value);
                if (!string.Equals(lastSystemStatusText, chineseStatus))
                {
                    lastSystemStatusText = chineseStatus;
                    lblSystemStatus.Text = chineseStatus;
                }
            }
        }

        /// <summary>
        /// 将系统状态数值转换为中文描述。
        /// 0=未初始化, 1=暂停工作, 2=就绪, 3=工作中, 4=流程报警, 5=弹框报警
        /// </summary>
        private static string ConvertToChineseStatus(string rawValue)
        {
            if (string.IsNullOrEmpty(rawValue))
            {
                return "未知";
            }

            if (!double.TryParse(rawValue, out double num))
            {
                return "未知";
            }

            int intValue = (int)num;
            if (num != intValue)
            {
                return "未知";
            }

            return intValue switch
            {
                0 => "未初始化",
                1 => "暂停工作",
                2 => "就绪",
                3 => "工作中",
                4 => "流程报警",
                5 => "弹框报警",
                _ => "未知"
            };
        }
    }
}
