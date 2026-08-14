// 模块：MCP / 运行时组合。
// 职责范围：持有单个 Bridge 客户端和当前 MCP 配置，为工具调用提供进程级连接。
// 排查入口：未初始化或重连异常时检查 Program 初始化顺序和 BridgeClient 是否已被替换/释放。

using Automation.Protocol;

namespace Automation.McpServer
{
    internal static class AutomationMcpRuntime
    {
        private static readonly object SyncRoot = new object();
        private static readonly AsyncLocal<string?> InvocationToolProfile = new AsyncLocal<string?>();
        private static AutomationBridgeClient? bridgeClient;
        private static AutomationMcpOptions? options;
        private static string currentToolProfile = AutomationToolProfiles.Diagnostic;

        public static void Initialize(AutomationMcpOptions appOptions)
        {
            lock (SyncRoot)
            {
                options = appOptions ?? throw new ArgumentNullException(nameof(appOptions));
                currentToolProfile = AutomationToolProfiles.Normalize(appOptions.ToolProfile);
                bridgeClient?.Dispose();
                bridgeClient = new AutomationBridgeClient(appOptions);
            }
        }

        internal static string CurrentToolProfile
        {
            get
            {
                string? invocationProfile = InvocationToolProfile.Value;
                if (!string.IsNullOrWhiteSpace(invocationProfile)) return invocationProfile;
                lock (SyncRoot) return currentToolProfile;
            }
        }

        internal static void SetToolProfile(string value)
        {
            string normalized = AutomationToolProfiles.Normalize(value);
            lock (SyncRoot) currentToolProfile = normalized;
        }

        internal static IDisposable BeginToolInvocation(string toolProfile)
        {
            string? previous = InvocationToolProfile.Value;
            InvocationToolProfile.Value = AutomationToolProfiles.Normalize(toolProfile);
            return new InvocationProfileScope(previous);
        }

        private sealed class InvocationProfileScope : IDisposable
        {
            private readonly string? previous;
            private bool disposed;

            public InvocationProfileScope(string? previousValue)
            {
                previous = previousValue;
            }

            public void Dispose()
            {
                if (disposed) return;
                InvocationToolProfile.Value = previous;
                disposed = true;
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
