using System;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 首页。
// 职责范围：显示平台与系统变量摘要；页面只消费 IAutomationPlatform 的稳定快照。
// 排查入口：系统状态异常时先核对 SDK 事件和“系统状态”变量，不要在页面内补偿或改写运行状态。

namespace Automation.Hmi
{
    public partial class HmiHomePage : Form
    {
        private IAutomationPlatform platform;
        private string lastSystemStatusText = string.Empty;

        public HmiHomePage()
        {
            InitializeComponent();
        }

        internal void AttachPlatform(IAutomationPlatform platform)
        {
            this.platform = platform;
        }

        internal void RefreshRuntimeView()
        {
            if (platform == null)
            {
                return;
            }

            // 读取系统状态变量（double），转换为中文状态文本
            if (platform.Values.TryGet("系统状态", out ValueSnapshot snapshot, out _) &&
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
