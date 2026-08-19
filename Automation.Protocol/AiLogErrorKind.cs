// 模块：协议 / AI 日志观测契约。
// 职责范围：把 Bridge/MCP/ACP 的机器 errorCode 机械归类为稳定 errorKind，
// 供 Editor Analysis 与 McpServer structured 日志共用，避免两侧映射漂移。

using System;

namespace Automation.Protocol
{
    // MCP 侧产生、ACP 客户端消费的跨进程稳定错误码。
    // McpServer 抛出时必须在异常消息开头携带该标记，客户端按标记识别，不匹配自然语言文案。
    public static class AutomationMcpErrorCodes
    {
        public const string ToolNotAvailable = "TOOL_NOT_AVAILABLE";
    }

    public static class AiLogErrorKind
    {
        // 按前缀归类的稳定类别；未匹配时回退为小写 errorCode。
        public static string Classify(string errorCode)
        {
            string code = (errorCode ?? string.Empty).Trim();
            if (code.Length == 0) return "unknown";
            if (code.StartsWith("OPERA_TYPE_NOT_FOUND", StringComparison.Ordinal)) return "opera_type_not_found";
            if (code.StartsWith("CHANGE_SET", StringComparison.Ordinal)) return "change_set";
            if (code.StartsWith("RESOURCE_BINDING", StringComparison.Ordinal)) return "resource_binding";
            if (code.StartsWith("INVALID_ARGUMENT", StringComparison.Ordinal)) return "invalid_argument";
            if (code.StartsWith("TOOL_NOT_AVAILABLE", StringComparison.Ordinal)) return "tool_not_available";
            if (code.StartsWith("TOOL_INVOCATION_FAILED", StringComparison.Ordinal)) return "tool_invocation";
            if (code.StartsWith("PROVIDER_TOOL_ARGUMENTS", StringComparison.Ordinal)) return "provider_arguments";
            if (code.StartsWith("PREVIEW", StringComparison.Ordinal)) return "preview";
            // 平台迁移配置各域错误码（PLC_CONFIG*、IO_DEBUG_CONFIG*、MOTION_CONFIG* 等）统一归并，
            // 便于按 migration_config 检索迁移链路失败。
            if (code.StartsWith("MIGRATION", StringComparison.Ordinal)
                || code.StartsWith("PLC_CONFIG", StringComparison.Ordinal)
                || code.StartsWith("IO_DEBUG_CONFIG", StringComparison.Ordinal)
                || code.StartsWith("IO_CONFIG", StringComparison.Ordinal)
                || code.StartsWith("MOTION_CONFIG", StringComparison.Ordinal)
                || code.StartsWith("COMMUNICATION_CONFIG", StringComparison.Ordinal)) return "migration_config";
            if (code.StartsWith("PROC_", StringComparison.Ordinal)) return "process_state";
            if (code.StartsWith("STORE_", StringComparison.Ordinal)) return "store";
            if (code.StartsWith("EDITOR_SESSION", StringComparison.Ordinal)) return "editor_session";
            if (code.StartsWith("UNHANDLED_EXCEPTION", StringComparison.Ordinal)) return "unhandled";
            return code.ToLowerInvariant();
        }
    }
}
