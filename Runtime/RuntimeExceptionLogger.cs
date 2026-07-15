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
        private static int uiExceptionHandling;

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
            Exception exception = e?.Exception;
            bool fatal = IsFatalUiException(exception);
            if (Interlocked.Exchange(ref uiExceptionHandling, 1) != 0)
            {
                Write("UI异常处理期间再次异常", exception, "已忽略重复处理，安全锁定保持有效。");
                return;
            }
            try
            {
                Write("未处理（UI 线程）", exception, fatal
                    ? "异常不可安全恢复，将执行停机退出。"
                    : "已停止全部流程并进入安全锁定，HMI继续运行。");
                SF.SetSecurityLock($"UI线程发生未处理异常，已触发安全停止:{exception?.Message}");
                MessageBox.Show(
                    fatal
                        ? "发生不可恢复的运行时异常，已写入本地日志，程序将安全退出。"
                        : "界面发生异常，已停止全部流程并进入安全锁定。HMI仍可用于查看状态和处理故障。",
                    "运行时异常",
                    MessageBoxButtons.OK,
                    fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
            }
            catch
            {
            }
            finally
            {
                if (!fatal)
                {
                    Interlocked.Exchange(ref uiExceptionHandling, 0);
                }
            }
            if (!fatal)
            {
                return;
            }
            try
            {
                Application.Exit();
            }
            catch (Exception exitException)
            {
                Write("UI线程退出异常", exitException, "Application.Exit失败，改为结束当前消息循环。");
                try { Application.ExitThread(); }
                catch { }
            }
        }

        private static bool IsFatalUiException(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is OutOfMemoryException
                    || current is AccessViolationException
                    || current is System.Runtime.InteropServices.SEHException)
                {
                    return true;
                }
                current = current.InnerException;
            }
            return false;
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
