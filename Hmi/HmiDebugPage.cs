using System.Windows.Forms;
using Automation.DeviceSdk;

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
