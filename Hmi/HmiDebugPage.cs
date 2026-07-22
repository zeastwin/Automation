using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 调试页面。
// 职责范围：通过 IAutomationPlatform 展示变量和流程调试能力，不直接控制设备实现。
// 排查入口：按钮无效时先查看 SDK 返回的 error，再沿 PlatformValueStoreFacade 或 PlatformProcessStoreFacade 定位。

namespace Automation.Hmi
{
    public partial class HmiDebugPage : Form
    {
        public HmiDebugPage()
        {
            InitializeComponent();
        }

        internal void AttachPlatform(IAutomationPlatform platform)
        {
            // 空架子，保留方法供 FrmHmiMain 调用
        }

        internal void RefreshRuntimeView()
        {
            // 空架子，保留方法供 FrmHmiMain 调用
        }

        internal void MarkProcessListDirty()
        {
            // 空架子，保留方法供 FrmHmiMain 调用
        }

        internal void UpdateRuntimeState(PlatformRuntimeStatus state, string message)
        {
            // 空架子，保留方法供 FrmHmiMain 调用
        }
    }
}
