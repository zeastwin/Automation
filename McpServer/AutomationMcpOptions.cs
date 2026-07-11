using Microsoft.Extensions.Configuration;

namespace Automation.McpServer
{
    internal sealed class AutomationMcpOptions
    {
        public string ListenUrl { get; set; } = string.Empty;
        public string ListenHost { get; set; } = "127.0.0.1";
        public int ListenPort { get; set; } = 8081;
        public string BridgePipeName { get; set; } = "AutomationBridgePipe";
        public int BridgeTimeoutMs { get; set; } = 30000;
        public bool EnableTrayIcon { get; set; } = true;
        public string ToolProfile { get; set; } = "Diagnostic";
        public string LogRoot { get; set; } = Path.Combine(@"D:\AutomationLogs", "AIExecution");

        public static AutomationMcpOptions Load(IConfiguration configuration, string baseDirectory)
        {
            var options = new AutomationMcpOptions();
            configuration.GetSection("AutomationMcp").Bind(options);
            options.Normalize(baseDirectory);
            return options;
        }

        private void Normalize(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(ListenHost))
            {
                ListenHost = "127.0.0.1";
            }
            else
            {
                ListenHost = ListenHost.Trim();
            }

            if (ListenPort <= 0 || ListenPort > 65535)
            {
                ListenPort = 8081;
            }

            if (string.IsNullOrWhiteSpace(BridgePipeName))
            {
                BridgePipeName = "AutomationBridgePipe";
            }

            ListenUrl = BuildListenUrl(ListenUrl, ListenHost, ListenPort);
            SyncListenHostAndPort(ListenUrl);
            BridgePipeName = BridgePipeName.Trim();

            if (BridgeTimeoutMs <= 0)
            {
                BridgeTimeoutMs = 30000;
            }

            if (string.Equals(ToolProfile, "Diagnostic", StringComparison.OrdinalIgnoreCase))
            {
                ToolProfile = "Diagnostic";
            }
            else if (string.Equals(ToolProfile, "Editor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ToolProfile, "Focused", StringComparison.OrdinalIgnoreCase))
            {
                // Focused 是早期名称，继续兼容并统一映射为编辑模式。
                ToolProfile = "Editor";
            }
            else
            {
                throw new InvalidDataException($"AutomationMcp.ToolProfile不支持:{ToolProfile}，可选Diagnostic/Editor。");
            }

            if (string.IsNullOrWhiteSpace(LogRoot))
            {
                LogRoot = Path.Combine(@"D:\AutomationLogs", "AIExecution");
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

        private static string BuildListenUrl(string listenUrl, string listenHost, int listenPort)
        {
            if (!string.IsNullOrWhiteSpace(listenUrl))
            {
                return NormalizeUrl(listenUrl);
            }

            string host = string.IsNullOrWhiteSpace(listenHost) ? "127.0.0.1" : listenHost.Trim();
            int port = listenPort <= 0 || listenPort > 65535 ? 8081 : listenPort;
            return $"http://{host}:{port}";
        }

        private void SyncListenHostAndPort(string listenUrl)
        {
            if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(uri.Host))
            {
                ListenHost = uri.Host;
            }

            if (!uri.IsDefaultPort)
            {
                ListenPort = uri.Port;
            }
            else if (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                ListenPort = 443;
            }
            else
            {
                ListenPort = 80;
            }
        }
    }
}
