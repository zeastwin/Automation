using System.Windows.Forms;

namespace Automation.Hmi
{
    public partial class HmiDebugPage : Form
    {
        public HmiDebugPage()
        {
            InitializeComponent();
        }

        internal void AttachHost(AutomationPlatformHost host)
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

        internal void UpdateRuntimeState(PlatformRuntimeState state, string message)
        {
            // 空架子，保留方法供 FrmHmiMain 调用
        }
    }
}
