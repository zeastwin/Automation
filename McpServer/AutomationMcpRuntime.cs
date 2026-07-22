// 模块：MCP / 运行时组合。
// 职责范围：持有单个 Bridge 客户端和当前 MCP 配置，为工具调用提供进程级连接。
// 排查入口：未初始化或重连异常时检查 Program 初始化顺序和 BridgeClient 是否已被替换/释放。

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
