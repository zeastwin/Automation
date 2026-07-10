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
        static void Main()
        {
            if (!AppConfigStorage.TryLoad(out _, out string appConfigError))
            {
                MessageBox.Show(appConfigError ?? "程序参数配置异常，程序已停止启动。", "程序配置异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FrmMain());
        }
    }
}
