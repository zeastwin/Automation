using System;
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
            if (AIFlow.AiFlowCli.TryHandle(args))
            {
                SF.AiFlowEnabled = false;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SF.accountStore = new AccountStore();
            SF.userContextStore = new UserContextStore(() => SF.userSession, () => SF.SecurityLocked);
            SF.userLoginStore = new UserLoginStore(SF.accountStore);
            AccountLoadResult loadResult = SF.accountStore.Load(SF.ConfigPath, "system", "software_123", out string loadError);
            if (loadResult == AccountLoadResult.Invalid)
            {
                MessageBox.Show(loadError ?? "账户配置异常，已进入锁定模式。", "账户系统异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SF.SetSecurityLock(loadError ?? "账户配置异常");
            }
            if (!SF.userLoginStore.TryLogin("system", "software_123", out string loginError))
            {
                MessageBox.Show(loginError ?? "调试自动登录失败，已进入锁定模式。", "账户系统异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SF.SetSecurityLock(loginError ?? "调试自动登录失败");
            }
            Application.Run(new FrmMain());
        }
    }
}
