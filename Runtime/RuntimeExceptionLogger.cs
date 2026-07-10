using System;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        public static void Initialize()
        {
            if (Interlocked.Exchange(ref initialized, 1) != 0)
            {
                return;
            }

            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
        }

        private static void CurrentDomain_FirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
            Write("已抛出（可能已捕获）", e?.Exception, null);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e?.ExceptionObject as Exception;
            Write("未处理（AppDomain）", exception, $"正在终止:{e?.IsTerminating}");
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Write("未观察任务异常", e?.Exception, null);
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Write("未处理（UI 线程）", e?.Exception, "将退出程序并执行停机流程。");
            try
            {
                MessageBox.Show("发生未处理异常，已写入本地日志，程序将安全退出。", "运行时异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
            Application.Exit();
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
