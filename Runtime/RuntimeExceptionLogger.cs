using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace Automation
{
    /// <summary>
    /// 记录运行时抛出的全部托管异常，便于开发阶段追查已被捕获的异常。
    /// </summary>
    public static class RuntimeExceptionLogger
    {
        private static readonly LocalFileLogger logger = new LocalFileLogger(@"D:\AutomationLogs\RuntimeExceptions");

        [ThreadStatic]
        private static bool isWriting;

        private static int initialized;
        private static int uiExitStarted;

        public static void Initialize()
        {
            if (Interlocked.Exchange(ref initialized, 1) != 0)
            {
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e?.ExceptionObject as Exception;
            Write("未处理（AppDomain）", exception, $"正在终止:{e?.IsTerminating}");
            try
            {
                SF.SetSecurityLock($"发生未处理异常，已触发安全停止:{exception?.Message}");
            }
            catch
            {
            }
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Write("未观察任务异常", e?.Exception, null);
            try
            {
                SF.SetSecurityLock($"发生未观察任务异常，已触发安全停止:{e?.Exception?.GetBaseException().Message}");
            }
            catch
            {
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            if (Interlocked.Exchange(ref uiExitStarted, 1) != 0)
            {
                Write("退出期间的UI线程异常", e?.Exception, "已在执行停机退出，不重复枚举窗体。");
                return;
            }
            Write("未处理（UI 线程）", e?.Exception, "将退出程序并执行停机流程。");
            try
            {
                SF.SetSecurityLock($"UI线程发生未处理异常，已触发安全停止:{e?.Exception?.Message}");
                MessageBox.Show("发生未处理异常，已写入本地日志，程序将安全退出。", "运行时异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
            try
            {
                Application.Exit();
            }
            catch (Exception exitException)
            {
                Write("UI线程退出异常", exitException, "Application.Exit失败，改为结束当前消息循环。");
                try
                {
                    Application.ExitThread();
                }
                catch
                {
                }
            }
        }

        private static void Write(string category, Exception exception, string detail)
        {
            if (exception == null || isWriting)
            {
                return;
            }

            try
            {
                isWriting = true;
                StringBuilder message = new StringBuilder();
                message.Append('[').Append(category ?? "异常").Append("] 线程=")
                    .Append(Thread.CurrentThread.ManagedThreadId);
                if (!string.IsNullOrWhiteSpace(Thread.CurrentThread.Name))
                {
                    message.Append(" 名称=").Append(Thread.CurrentThread.Name);
                }
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    message.AppendLine().Append(detail);
                }
                message.AppendLine().Append(exception);
                logger.Log(message.ToString(), LogLevel.Error);
                StructuredAuditLogger.Write("RuntimeExceptions", new JObject
                {
                    ["source"] = "runtime",
                    ["eventName"] = "runtime.exception",
                    ["level"] = "error",
                    ["category"] = category ?? "异常",
                    ["threadId"] = Thread.CurrentThread.ManagedThreadId,
                    ["threadName"] = Thread.CurrentThread.Name ?? string.Empty,
                    ["outcome"] = "failed",
                    ["errorCode"] = exception.GetType().FullName ?? exception.GetType().Name,
                    ["errorMessage"] = exception.Message ?? string.Empty,
                    ["detail"] = detail ?? string.Empty,
                    ["stackTrace"] = exception.ToString()
                });
            }
            catch
            {
            }
            finally
            {
                isWriting = false;
            }
        }
    }
}
