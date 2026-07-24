using System;
using System.Reflection;
using System.Windows.Forms;
// 模块：核心测试 / 隔离进程夹具。
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
                    MethodInfo preloadMethod = typeof(AutomationPlatformHost).GetMethod(
                        "TryPreloadPlatformEditor",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (preloadMethod == null)
                    {
                        throw new InvalidOperationException("缺少平台编辑器隐藏预加载入口。");
                    }
                    object[] preloadArguments = { null };
                    if (!(bool)preloadMethod.Invoke(host, preloadArguments))
                    {
                        throw new InvalidOperationException(
                            preloadArguments[0] as string ?? "平台编辑器隐藏预加载失败。");
                    }
                    FieldInfo editorField = typeof(AutomationPlatformHost).GetField(
                        "platformEditor",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Form editor = editorField == null
                        ? null
                        : editorField.GetValue(host) as Form;
                    if (editor == null || editor.IsDisposed || editor.Visible || host.IsPlatformVisible)
                    {
                        throw new InvalidOperationException("平台编辑器未完成隐藏预加载。");
                    }
                    FieldInfo aiStartStateField = editor.GetType().GetField(
                        "aiInfrastructureStartState",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (aiStartStateField == null
                        || (int)aiStartStateField.GetValue(editor) != 0)
                    {
                        throw new InvalidOperationException("隐藏预加载期间提前启动了 AI 基础设施。");
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
