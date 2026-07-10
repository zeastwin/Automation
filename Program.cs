using System;
using System.Windows.Forms;
using Automation.Hmi;

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
            RuntimeExceptionLogger.Initialize();
            if (!AppConfigStorage.TryLoad(out AppConfig appConfig, out string appConfigError))
            {
                MessageBox.Show(appConfigError ?? "程序参数配置异常，程序已停止启动。", "程序配置异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!AutomationRuntimeOptions.TryConfigure(args, appConfig, out AutomationRuntimeOptions runtimeOptions, out string argumentError))
            {
                MessageBox.Show(argumentError, "启动参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (AutomationPlatformHost platformHost = new AutomationPlatformHost())
            {
                using (FrmHmiMain hmiMain = new FrmHmiMain(platformHost))
                {
                    if (!platformHost.Initialize(out string platformError))
                    {
                        MessageBox.Show(platformError ?? "平台初始化失败。", "HMI 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    Application.Run(hmiMain);
                }
            }
        }
    }
}
