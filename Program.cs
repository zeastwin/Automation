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
            if (!GooseConfigStorage.TryApplyStartupSafetyDefaults(out string aiSafetyError))
            {
                // AI 配置异常不阻断主控/HMI启动；本次运行的 AI 配置读取保持失败关闭状态，
                // MCP 和 AI 页面只能回退到诊断模式、自动批准关闭。
                MessageBox.Show(aiSafetyError, "AI安全默认值应用失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (!GooseRuntimeProvisioner.TryEnsureManagedContext(out string promptMessage))
            {
                // Goose 属于辅助能力，部署异常只能禁用 EW-AI，不能阻断 HMI/平台初始化。
                MessageBox.Show(promptMessage, "EW-AI 受管上下文不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UiBranding.Initialize();

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
