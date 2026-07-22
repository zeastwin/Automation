using System;
// 模块：核心测试 / AI 前台。
// 职责范围：验证聊天 WebView 初始化、页面导航和可交互状态桥接的最小链路。

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class AiWebViewBootstrapTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        [Timeout(30000)]
        public void Show_WhenWebViewRuntimeIsAvailable_EnablesEditorControls()
        {
            StaTestRunner.Run(RunBootstrap, TimeSpan.FromSeconds(25));
        }

        private static void RunBootstrap()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new FrmAiAssistant
            {
                Opacity = 0,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None
            })
            {
                form.Show();
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                object core = WaitForWebViewCore(form, deadline);
                Assert.IsNotNull(core, "AI WebView2 未在15秒内初始化。");

                MethodInfo execute = core.GetType().GetMethod(
                    "ExecuteScriptAsync", new[] { typeof(string) });
                Assert.IsNotNull(execute);
                string result = null;
                while (DateTime.UtcNow < deadline
                    && !string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                {
                    object resultTaskObject = execute.Invoke(core, new object[]
                    {
                        "appState.canAccess===true&&appState.canEditConfig===true"
                            + "&&!document.getElementById('promptInput').disabled"
                            + "&&!document.getElementById('standardTestButton').disabled"
                            + "&&!document.getElementById('toolDiagnostic').disabled"
                            + "&&!document.getElementById('toolEditor').disabled"
                    });
                    var task = (Task)resultTaskObject;
                    PumpUntilCompleted(task, deadline);
                    if (!task.IsCompleted)
                    {
                        break;
                    }
                    result = resultTaskObject.GetType().GetProperty("Result")
                        ?.GetValue(resultTaskObject)?.ToString();
                    Application.DoEvents();
                    Thread.Sleep(20);
                }
                Assert.AreEqual("true", result?.ToLowerInvariant(),
                    "AI 页面未收到可交互状态。");
            }
        }

        private static object WaitForWebViewCore(FrmAiAssistant form, DateTime deadline)
        {
            FieldInfo webViewField = typeof(FrmAiAssistant).GetField(
                "webViewConversation", BindingFlags.Instance | BindingFlags.NonPublic);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(20);
                object webView = webViewField?.GetValue(form);
                object core = webView?.GetType().GetProperty("CoreWebView2")?.GetValue(webView);
                if (core != null)
                {
                    return core;
                }
            }
            return null;
        }

        private static void PumpUntilCompleted(Task task, DateTime deadline)
        {
            while (DateTime.UtcNow < deadline && !task.IsCompleted)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
        }
    }
}
