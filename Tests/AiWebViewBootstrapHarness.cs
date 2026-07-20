using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation.Tests
{
    internal static class AiWebViewBootstrapHarness
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().FullName);
                try
                {
                    Console.Error.WriteLine(ex.Message);
                }
                catch
                {
                    Console.Error.WriteLine("\u5f02\u5e38\u6d88\u606f\u65e0\u6cd5\u8bfb\u53d6\u3002");
                }
                Environment.ExitCode = 1;
            }
        }

        private static void Run()
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
                if (core == null)
                {
                    throw new InvalidOperationException("AI WebView2\u672a\u572815\u79d2\u5185\u521d\u59cb\u5316\u3002");
                }

                MethodInfo execute = core.GetType().GetMethod(
                    "ExecuteScriptAsync",
                    new[] { typeof(string) });
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
                    var resultTask = resultTaskObject as Task;
                    while (DateTime.UtcNow < deadline && !resultTask.IsCompleted)
                    {
                        Application.DoEvents();
                        Thread.Sleep(20);
                    }
                    if (!resultTask.IsCompleted)
                    {
                        break;
                    }
                    result = resultTaskObject.GetType().GetProperty("Result")
                        ?.GetValue(resultTaskObject)?.ToString();
                    Application.DoEvents();
                    Thread.Sleep(20);
                }

                if (!string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                {
                    object detailTaskObject = execute.Invoke(core, new object[]
                    {
                        "JSON.stringify({generation:window.__automationConversationViewGeneration,"
                            + "canAccess:appState.canAccess,"
                            + "taskText:document.getElementById('taskList').textContent,"
                            + "hasSetter:typeof window.automationSetState==='function'})"
                    });
                    var detailTask = detailTaskObject as Task;
                    DateTime detailDeadline = DateTime.UtcNow.AddSeconds(2);
                    while (DateTime.UtcNow < detailDeadline && !detailTask.IsCompleted)
                    {
                        Application.DoEvents();
                        Thread.Sleep(20);
                    }
                    string details = detailTask.IsCompleted
                        ? detailTaskObject.GetType().GetProperty("Result")?.GetValue(detailTaskObject)?.ToString()
                        : "timeout";
                    throw new InvalidOperationException(
                        "AI\u9875\u9762\u672a\u6536\u5230\u53ef\u4ea4\u4e92\u72b6\u6001\uff0c\u811a\u672c\u7ed3\u679c\uff1a"
                            + result + "\uff0c\u9875\u9762\u7ec6\u8282\uff1a" + details);
                }
                Console.WriteLine(
                    "AI WebView\u542f\u52a8\u56de\u5f52\u901a\u8fc7\uff1a\u8f93\u5165\u533a\u3001\u6807\u51c6\u6d4b\u8bd5\u548c\u5de5\u5177\u6a21\u5f0f\u5207\u6362\u5747\u53ef\u4ea4\u4e92\u3002");
            }
        }

        private static object WaitForWebViewCore(FrmAiAssistant form, DateTime deadline)
        {
            FieldInfo webViewField = typeof(FrmAiAssistant).GetField(
                "webViewConversation",
                BindingFlags.Instance | BindingFlags.NonPublic);
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
    }
}
