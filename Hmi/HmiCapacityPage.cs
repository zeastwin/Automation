using System.Windows.Forms;

// 模块：平台内置 HMI / 产能页面。
// 职责范围：承载产能界面布局；业务数据接入应通过公开 SDK 或页面显式依赖完成。
// 排查入口：本页目前没有运行时数据源，新增数据时不要直接引用 PlatformRuntime 或平台窗体。

namespace Automation.Hmi
{
    public partial class HmiCapacityPage : Form
    {
        public HmiCapacityPage()
        {
            InitializeComponent();
        }
    }
}
