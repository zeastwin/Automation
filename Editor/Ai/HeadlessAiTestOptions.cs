using Newtonsoft.Json.Linq;
// 模块：编辑器 / AI。
// 职责范围：headless 回归测试的语句约束解析；由 Bridge ai-test 端点复用，纯逻辑供单测覆盖。

using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>headless 测试语句约束；与标准测试语句约束保持一致。</summary>
    internal sealed class HeadlessAiTestOptions
    {
        internal const int MaximumPromptCount = 12;
        internal const int MaximumPromptLength = 4000;
        internal const int DefaultTurnTimeoutMinutes = 15;

        /// <summary>校验语句列表；返回 null 表示合法。</summary>
        public static string ValidatePrompts(IReadOnlyList<string> prompts)
        {
            if (prompts == null || prompts.Count < 1 || prompts.Count > MaximumPromptCount)
            {
                return $"语句数量必须在 1..{MaximumPromptCount}。";
            }
            if (prompts.Any(item => string.IsNullOrWhiteSpace(item)
                || item.Length > MaximumPromptLength))
            {
                return $"单句不能为空且长度不超过 {MaximumPromptLength} 字符。";
            }
            return null;
        }
    }
}
