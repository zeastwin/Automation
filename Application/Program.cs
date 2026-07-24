// 模块：应用入口。
// 职责范围：启动 WinForms 进程并进入平台 Bootstrap；不承载运行时组合。
// 排查入口：按 TryPrepare -> AppConfigStorage -> AutomationPlatformHost.Initialize 的顺序定位启动失败。

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
            if (!AppConfigStorage.TryGetCached(out AppConfig appConfig, out string configError))
            {
                MessageBox.Show(configError, "程序配置不可用", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UiBranding.Initialize();

            using (var runtime = new AutomationPlatformHost())
            {
                Hmi.CustomFunctions.Register(runtime);
                if (!runtime.Initialize(out string platformError))
                {
                    MessageBox.Show(platformError, "平台初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (appConfig.StartupView == AutomationStartupView.Hmi)
                {
                    using (var hmi = new Hmi.FrmHmiMain(runtime))
                    {
                        ScheduleHiddenPlatformEditorPreload(hmi, runtime);
                        Application.Run(hmi);
                    }
                }
                else
                {
                    Form platformEditor = runtime.PreparePlatformEditorMainWindow();
                    platformEditor.Shown += (sender, eventArgs) => runtime.NotifyInteractionUiReady();
                    Application.Run(platformEditor);
                }
            }
        }

        private static void ScheduleHiddenPlatformEditorPreload(
            Form hmi,
            AutomationPlatformHost runtime)
        {
            EventHandler preloadOnIdle = null;
            preloadOnIdle = (sender, eventArgs) =>
            {
                Application.Idle -= preloadOnIdle;
                if (hmi.IsDisposed || hmi.Disposing)
                {
                    return;
                }
                runtime.TryPreloadPlatformEditor(out _);
            };
            hmi.Shown += (sender, eventArgs) => Application.Idle += preloadOnIdle;
            hmi.FormClosed += (sender, eventArgs) => Application.Idle -= preloadOnIdle;
        }
    }
}
