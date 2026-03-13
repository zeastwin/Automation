using System.Text;
using System.Text.Json;

namespace Automation.McpServer
{
    /// <summary>
    /// MCP 工具调用日志落盘，便于排查请求、Bridge 返回与异常。
    /// </summary>
    internal static class ToolCallLogger
    {
        private static readonly object SyncRoot = new object();
        private static string logRoot = Path.Combine(AppContext.BaseDirectory, "Logs", "McpServer");

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
                string path = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var builder = new StringBuilder();
                builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {toolName}");
                if (args != null)
                {
                    builder.AppendLine("args: " + SafeSerialize(args));
                }
                if (!string.IsNullOrWhiteSpace(error))
                {
                    builder.AppendLine("error: " + error);
                }
                else
                {
                    builder.AppendLine("result: " + result);
                }
                builder.AppendLine(new string('-', 72));

                lock (SyncRoot)
                {
                    File.AppendAllText(path, builder.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志失败不影响主流程。
            }
        }

        private static string SafeSerialize(object value)
        {
            try
            {
                return JsonSerializer.Serialize(value);
            }
            catch
            {
                return value?.ToString() ?? string.Empty;
            }
        }
    }
}
