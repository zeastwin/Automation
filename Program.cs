using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Automation
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (!AutomationPlatformBootstrap.TryPrepare(
                args, out IReadOnlyList<string> startupWarnings, out string startupError))
            {
                MessageBox.Show(startupError, "平台启动准备失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            foreach (string warning in startupWarnings)
            {
                MessageBox.Show(warning, "平台辅助能力不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UiBranding.Initialize();

            using (FrmMain platformEditor = new FrmMain { HideOnUserClose = false })
            {
                try
                {
                    platformEditor.InitializePlatform();
                }
                catch (Exception ex)
                {
                    platformEditor.ShutdownPlatform();
                    MessageBox.Show(ex.Message, "平台初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                platformEditor.Shown += (sender, eventArgs) => platformEditor.NotifyProcessInteractionUiReady();
                Application.Run(platformEditor);
                platformEditor.ShutdownPlatform();
            }
        }
    }
}
