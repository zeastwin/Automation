using System;
using System.Windows.Forms;
// 模块：测试 / 无编辑器宿主冒烟。
// 职责范围：验证平台宿主不显示编辑器也能初始化和关闭。
// 排查入口：失败按 AppConfig、Host.Initialize 和 ShutdownCoordinator 顺序定位。

namespace Automation.Tests
{
    internal static class HeadlessPlatformHostSmoke
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                AppConfig config;
                string configError;
                if (!AppConfigStorage.TryLoad(out config, out configError))
                {
                    throw new InvalidOperationException(configError);
                }
                AutomationRuntimeOptions options;
                string runtimeError;
                if (!AutomationRuntimeOptions.TryConfigure(
                    new string[0], config, out options, out runtimeError))
                {
                    throw new InvalidOperationException(runtimeError);
                }
                using (var host = new AutomationPlatformHost())
                {
                    string initializeError;
                    if (!host.Initialize(out initializeError))
                    {
                        throw new InvalidOperationException(initializeError);
                    }
                    if (Application.OpenForms.Count != 0 || host.IsPlatformVisible)
                    {
                        throw new InvalidOperationException("HMI 运行时初始化期间创建了平台编辑器窗体。");
                    }
                    PlatformValueSnapshot status;
                    string statusError;
                    if (!host.TryGetValue("系统状态", out status, out statusError))
                    {
                        throw new InvalidOperationException(statusError);
                    }
                    double statusValue;
                    if (!double.TryParse(status.Value, out statusValue))
                    {
                        throw new InvalidOperationException("系统状态变量未由 Runtime 服务维护。");
                    }
                    string setError;
                    if (!host.TrySetValue("复位状态", 2d, out setError))
                    {
                        throw new InvalidOperationException(setError);
                    }
                    if (!host.TryGetValue("系统状态", out status, out statusError)
                        || !double.TryParse(status.Value, out statusValue)
                        || statusValue != (double)SystemStatus.Ready)
                    {
                        throw new InvalidOperationException(
                            statusError ?? "复位状态变化后，系统状态服务未同步更新为 Ready。");
                    }
                    host.NotifyInteractionUiReady();
                    if (Application.OpenForms.Count != 0 || host.IsPlatformVisible)
                    {
                        throw new InvalidOperationException("交互就绪通知创建了平台编辑器窗体。");
                    }
                }
                Console.WriteLine("Headless platform host: PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }
}
