using Automation.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 在模型启动前把自然语言任务路由到一个最小能力包。它只决定工具面，不替代模型判断业务方案。
    /// </summary>
    internal static class AiTaskCapabilityRouter
    {
        public static AiTaskCapabilityDecision Route(
            string prompt,
            IEnumerable<AiConversationMessage> history,
            string previousCapability,
            string permissionProfile,
            bool fullPermissionEnabled)
        {
            string current = (prompt ?? string.Empty).Trim();
            string recent = string.Join("\n", (history ?? Enumerable.Empty<AiConversationMessage>())
                .Where(item => string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase))
                .Reverse()
                .Take(2)
                .Reverse()
                .Select(item => item.Text ?? string.Empty));
            string context = recent + "\n" + current;

            string requested = InferRequestedProfile(current, context, previousCapability);
            string effective = requested;
            string notice = null;

            bool writeRequested = string.Equals(requested, AutomationToolProfiles.ProcessCreate, StringComparison.Ordinal)
                || string.Equals(requested, AutomationToolProfiles.ProcessEdit, StringComparison.Ordinal)
                || string.Equals(requested, AutomationToolProfiles.ResourceEdit, StringComparison.Ordinal)
                || string.Equals(requested, AutomationToolProfiles.RuntimeControl, StringComparison.Ordinal)
                || string.Equals(requested, AutomationToolProfiles.PlatformConfiguration, StringComparison.Ordinal);
            if (writeRequested
                && string.Equals(permissionProfile, AutomationToolProfiles.Diagnostic, StringComparison.OrdinalIgnoreCase))
            {
                effective = AutomationToolProfiles.ProcessReview;
                notice = "用户请求了写入或运行操作，但当前界面处于只读诊断权限。只调查并给出可执行方案，不要声称已经修改或运行；提示用户切换到编辑模式后再执行。";
            }
            else if (string.Equals(requested, AutomationToolProfiles.PlatformConfiguration, StringComparison.Ordinal)
                && !fullPermissionEnabled)
            {
                effective = AutomationToolProfiles.ProcessReview;
                notice = "用户请求了平台级配置变更，但完全权限尚未开启。只读取和解释相关事实，不要声称已经修改；提示用户明确开启完全权限。";
            }

            return new AiTaskCapabilityDecision
            {
                RequestedProfile = requested,
                EffectiveProfile = effective,
                Notice = notice,
                Reason = BuildReason(requested, effective)
            };
        }

        private static string InferRequestedProfile(string current, string context, string previousCapability)
        {
            if (LooksLikeSourceDevelopment(current)
                || IsContinuation(current) && LooksLikeSourceDevelopment(context))
                return AutomationToolProfiles.SourceDevelopment;
            if (LooksLikePlatformConfiguration(current)) return AutomationToolProfiles.PlatformConfiguration;
            if (LooksLikeRuntimeControl(current)) return AutomationToolProfiles.RuntimeControl;
            if (LooksLikeProcessCreation(current, context)) return AutomationToolProfiles.ProcessCreate;
            if (LooksLikeProcessEdit(current, context)) return AutomationToolProfiles.ProcessEdit;
            if (LooksLikeResourceEdit(current)) return AutomationToolProfiles.ResourceEdit;
            if (ContainsAny(current, "审查", "审核", "检查", "比较", "对比", "解释", "分析", "诊断", "为什么", "问题在哪", "看看log", "看看日志"))
                return AutomationToolProfiles.ProcessReview;
            if (ContainsAny(current, "设计", "方案", "怎么写", "如何写", "规划", "建议", "流程结构"))
                return AutomationToolProfiles.ProcessDesign;

            if (IsContinuation(current) && AutomationToolProfiles.IsTaskProfile(previousCapability))
            {
                bool execute = ContainsAny(current, "执行", "写入", "创建", "落地", "改吧", "写吧");
                if (execute && string.Equals(
                        previousCapability, AutomationToolProfiles.ProcessDesign, StringComparison.Ordinal))
                    return AutomationToolProfiles.ProcessCreate;
                if (execute && string.Equals(
                        previousCapability, AutomationToolProfiles.ProcessReview, StringComparison.Ordinal))
                    return AutomationToolProfiles.ProcessEdit;
                return previousCapability;
            }
            return ContainsAny(context, "流程", "步骤", "指令", "工艺")
                ? AutomationToolProfiles.ProcessReview
                : AutomationToolProfiles.SourceDevelopment;
        }

        private static bool LooksLikeSourceDevelopment(string value)
        {
            return ContainsAny(value,
                "源码", "代码改", "改代码", "代码实现", "仓库", "csproj", "C#", "WinForms",
                "编译", "单元测试", "接口开发", "API开发", "HMI开发", "Prompt.md", "system.md",
                "automation.md", "SKILL.md", "MCP工具", "Bridge代码", "AI助手设计", "harness");
        }

        private static bool LooksLikePlatformConfiguration(string value)
        {
            bool resource = ContainsAny(value, "控制卡", "运动卡", "PLC", "通讯", "通信", "IO", "平台", "迁移");
            bool configuration = ContainsAny(value, "配置", "迁移");
            bool action = ContainsAny(value, "修改", "新增", "创建", "配置", "写入", "删除", "迁移", "应用");
            return resource && configuration && action;
        }

        private static bool LooksLikeRuntimeControl(string value)
        {
            return ContainsAny(value,
                "启动流程", "停止流程", "暂停流程", "恢复流程", "继续流程", "运行流程",
                "执行流程", "跑一下流程", "测试运行", "试运行");
        }

        private static bool LooksLikeProcessCreation(string current, string context)
        {
            bool action = ContainsAny(current, "创建", "新建", "生成", "搭建", "写一个", "帮我写", "写入", "落进去",
                "按方案执行", "执行方案", "执行上面的方案", "按照刚才的方案");
            bool target = ContainsAny(context, "流程", "工艺", "步骤", "指令");
            return action && target;
        }

        private static bool LooksLikeProcessEdit(string current, string context)
        {
            bool action = ContainsAny(current, "修改", "调整", "重构", "复制", "删除", "修复", "替换", "补上", "改一下", "优化流程");
            bool target = ContainsAny(context, "流程", "步骤", "指令", "工艺");
            return action && target;
        }

        private static bool LooksLikeResourceEdit(string value)
        {
            bool resource = ContainsAny(value, "变量", "数据结构", "数据项", "报警");
            bool action = ContainsAny(value, "新增", "添加", "创建", "修改", "更新", "删除", "设置", "配置");
            return resource && action;
        }

        private static bool IsContinuation(string value)
        {
            return value.Length <= 24 && ContainsAny(value,
                "好的", "可以", "继续", "执行", "就这样", "按这个来", "改吧", "写吧", "确认");
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return terms.Any(term => value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildReason(string requested, string effective)
        {
            return string.Equals(requested, effective, StringComparison.Ordinal)
                ? $"任务已路由到最小能力包 {effective}。"
                : $"任务意图为 {requested}，受当前权限约束降级为 {effective}。";
        }
    }

    internal sealed class AiTaskCapabilityDecision
    {
        public string RequestedProfile { get; set; }
        public string EffectiveProfile { get; set; }
        public string Notice { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// 轨迹预算只生成评估信号，不中断模型。真正的越权由能力包工具集合机械阻断。
    /// </summary>
    internal static class AiTrajectoryBudgetPolicy
    {
        public static AiTrajectoryEvaluation Evaluate(
            string profile,
            int toolCalls,
            int toolFailures,
            long toolResultBytes)
        {
            int callLimit;
            long byteLimit;
            switch (profile)
            {
                case AutomationToolProfiles.ProcessDesign:
                    callLimit = 3;
                    byteLimit = 64 * 1024;
                    break;
                case AutomationToolProfiles.ProcessReview:
                    callLimit = 20;
                    byteLimit = 512 * 1024;
                    break;
                case AutomationToolProfiles.ProcessCreate:
                case AutomationToolProfiles.ProcessEdit:
                case AutomationToolProfiles.ResourceEdit:
                case AutomationToolProfiles.PlatformConfiguration:
                    callLimit = 18;
                    byteLimit = 384 * 1024;
                    break;
                case AutomationToolProfiles.RuntimeControl:
                    callLimit = 15;
                    byteLimit = 256 * 1024;
                    break;
                case AutomationToolProfiles.SourceDevelopment:
                    callLimit = 30;
                    byteLimit = 1024 * 1024;
                    break;
                default:
                    callLimit = 30;
                    byteLimit = 1024 * 1024;
                    break;
            }
            var reasons = new List<string>();
            if (toolCalls > callLimit) reasons.Add($"tool_calls>{callLimit}");
            if (toolResultBytes > byteLimit) reasons.Add($"tool_result_bytes>{byteLimit}");
            if (toolFailures > 2) reasons.Add("tool_failures>2");
            return new AiTrajectoryEvaluation
            {
                Status = reasons.Count == 0 ? "pass" : "review",
                ToolCallLimit = callLimit,
                ToolResultByteLimit = byteLimit,
                Reasons = reasons
            };
        }
    }

    internal sealed class AiTrajectoryEvaluation
    {
        public string Status { get; set; }
        public int ToolCallLimit { get; set; }
        public long ToolResultByteLimit { get; set; }
        public IReadOnlyList<string> Reasons { get; set; }
    }
}
