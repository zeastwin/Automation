namespace Automation.McpServer
{
    internal sealed class ToolContractIssue
    {
        public string Path { get; set; } = "$";
        public string Rule { get; set; } = "contract";
        public string Message { get; set; } = string.Empty;
        public string SuggestedRepair { get; set; } = string.Empty;
    }

    /// <summary>一次返回同一输入中可机械发现的全部契约问题，减少逐条试错。</summary>
    internal sealed class ToolContractValidationException : ArgumentException
    {
        public ToolContractValidationException(
            string errorCode,
            IReadOnlyList<ToolContractIssue> issues)
            : base(issues == null || issues.Count == 0
                ? "工具输入不符合契约。"
                : string.Join("；", issues.Select(issue => issue.Message)),
                "input")
        {
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "INVALID_ARGUMENT" : errorCode;
            Issues = issues ?? Array.Empty<ToolContractIssue>();
        }

        public string ErrorCode { get; }
        public IReadOnlyList<ToolContractIssue> Issues { get; }
    }
}
