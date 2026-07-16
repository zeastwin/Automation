using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Automation;
using Automation.DeviceSdk;
using MachineApp.Hmi;

namespace MachineApp
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
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
            using (var runtime = new AutomationPlatformHost())
            {
                IAutomationPlatform platform = runtime;
                DeviceCustomFunctions.Register(platform);
                using (var mainForm = new FrmHmiMain(platform))
                {
                    using (Icon applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    {
                        if (applicationIcon != null)
                        {
                            mainForm.Icon = (Icon)applicationIcon.Clone();
                        }
                    }
                    if (!platform.Initialize(out string platformError))
                    {
                        MessageBox.Show(platformError, "平台初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    Application.Run(mainForm);
                }
            }
        }

    }
}
