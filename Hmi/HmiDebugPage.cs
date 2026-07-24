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

        private void BtnTest_Click(object sender, System.EventArgs e)
        {
            MessageBox.Show("这是一条测试消息", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
