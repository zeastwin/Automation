using System.Text.Json;

namespace Automation.McpServer
{
    /// <summary>
    /// MCP 工具调用日志落盘，便于排查请求、Bridge 返回与异常。
    /// </summary>
    internal static class ToolCallLogger
    {
        private static readonly object SyncRoot = new object();
        private static readonly Mutex ExecutionLogMutex = new Mutex(false, "AutomationAIExecutionAuditLog");
        private static string logRoot = Path.Combine(@"D:\AutomationLogs", "AIExecution");

        public static void Configure(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return;
            }

            lock (SyncRoot)
            {
                logRoot = rootPath;
            }
        }

        public static void Log(string toolName, object? args, string result, string? error = null)
        {
            try
            {
                string dir;
                lock (SyncRoot)
                {
                    dir = logRoot;
                }

                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl");
                string record = JsonSerializer.Serialize(new
                {
                    time = DateTime.Now.ToString("O"),
                    source = "mcp",
                    kind = string.IsNullOrWhiteSpace(error) ? "tool_completed" : "tool_failed",
                    callId = Guid.NewGuid().ToString("N"),
                    toolName = toolName ?? string.Empty,
                    args,
                    result = string.IsNullOrWhiteSpace(error) ? result ?? string.Empty : string.Empty,
                    error = error ?? string.Empty
                });
                bool lockTaken = false;
                try
                {
                    lockTaken = ExecutionLogMutex.WaitOne(TimeSpan.FromSeconds(2));
                    if (lockTaken)
                    {
                        File.AppendAllText(path, record + Environment.NewLine, new System.Text.UTF8Encoding(false));
                    }
                }
                catch (AbandonedMutexException)
                {
                    lockTaken = true;
                }
                finally
                {
                    if (lockTaken)
                    {
                        ExecutionLogMutex.ReleaseMutex();
                    }
                }
            }
            catch
            {
                // 日志失败不影响主流程。
            }
        }
    }
}
