using System;
using System.Windows.Forms;
using Automation.Peripheral;

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
            bool platformOnly;
            if (args == null || args.Length == 0)
            {
                platformOnly = false;
            }
            else if (args.Length == 1 && string.Equals(args[0], "--platform", StringComparison.Ordinal))
            {
                platformOnly = true;
            }
            else
            {
                MessageBox.Show("启动参数无效。仅支持无参数启动外围，或使用 --platform 直接启动平台。", "启动参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!AppConfigStorage.TryLoad(out _, out string appConfigError))
            {
                MessageBox.Show(appConfigError ?? "程序参数配置异常，程序已停止启动。", "程序配置异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (AutomationPlatformHost platformHost = new AutomationPlatformHost())
            {
                if (platformOnly)
                {
                    if (!platformHost.Initialize(out string platformError))
                    {
                        MessageBox.Show(platformError ?? "平台初始化失败。", "平台启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    platformHost.PreparePlatformOnlyMode();
                    Application.Run(platformHost.PlatformEditor);
                    return;
                }

                using (FrmPeripheralMain peripheralMain = new FrmPeripheralMain(platformHost))
                {
                    if (!platformHost.Initialize(out string platformError))
                    {
                        MessageBox.Show(platformError ?? "平台初始化失败。", "外围启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    Application.Run(peripheralMain);
                }
            }
        }
    }
}
