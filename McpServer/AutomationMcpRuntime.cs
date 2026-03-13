namespace Automation.McpServer
{
    internal static class AutomationMcpRuntime
    {
        private static readonly object SyncRoot = new object();
        private static AutomationBridgeClient? bridgeClient;
        private static AutomationMcpOptions? options;

        public static void Initialize(AutomationMcpOptions appOptions)
        {
            lock (SyncRoot)
            {
                options = appOptions ?? throw new ArgumentNullException(nameof(appOptions));
                bridgeClient?.Dispose();
                bridgeClient = new AutomationBridgeClient(appOptions);
            }
        }

        public static AutomationBridgeClient GetBridgeClient()
        {
            lock (SyncRoot)
            {
                if (bridgeClient == null || options == null)
                {
                    throw new InvalidOperationException("Automation MCP Runtime 未初始化。");
                }

                return bridgeClient;
            }
        }
    }
}
