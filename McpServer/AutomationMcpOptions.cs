using Microsoft.Extensions.Configuration;

namespace Automation.McpServer
{
    internal sealed class AutomationMcpOptions
    {
        public string ListenUrl { get; set; } = "http://127.0.0.1:8081";
        public string BridgeBaseUrl { get; set; } = "http://127.0.0.1:18081";
        public string BridgeApiKey { get; set; } = string.Empty;
        public int BridgeTimeoutMs { get; set; } = 30000;
        public bool EnableTrayIcon { get; set; } = true;
        public string LogRoot { get; set; } = Path.Combine("Logs", "McpServer");

        public static AutomationMcpOptions Load(IConfiguration configuration, string baseDirectory)
        {
            var options = new AutomationMcpOptions();
            configuration.GetSection("AutomationMcp").Bind(options);
            options.Normalize(baseDirectory);
            return options;
        }

        private void Normalize(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(ListenUrl))
            {
                ListenUrl = "http://127.0.0.1:8081";
            }

            if (string.IsNullOrWhiteSpace(BridgeBaseUrl))
            {
                BridgeBaseUrl = "http://127.0.0.1:18081";
            }

            ListenUrl = NormalizeUrl(ListenUrl);
            BridgeBaseUrl = NormalizeUrl(BridgeBaseUrl).TrimEnd('/');

            if (BridgeTimeoutMs <= 0)
            {
                BridgeTimeoutMs = 30000;
            }

            if (string.IsNullOrWhiteSpace(LogRoot))
            {
                LogRoot = Path.Combine("Logs", "McpServer");
            }

            if (!Path.IsPathRooted(LogRoot))
            {
                LogRoot = Path.GetFullPath(Path.Combine(baseDirectory, LogRoot));
            }
        }

        private static string NormalizeUrl(string url)
        {
            string trimmed = (url ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "http://127.0.0.1:8081";
            }

            if (!trimmed.Contains("://", StringComparison.Ordinal))
            {
                trimmed = "http://" + trimmed;
            }

            return trimmed;
        }
    }
}
