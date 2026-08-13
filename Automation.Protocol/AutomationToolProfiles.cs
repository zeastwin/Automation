using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation.Protocol
{
    /// <summary>
    /// Automation MCP 工具档位的唯一名称契约。
    /// Diagnostic/Editor 是用户权限外壳；其余名称是按单轮任务装配的最小能力包。
    /// </summary>
    public static class AutomationToolProfiles
    {
        public const string Diagnostic = "Diagnostic";
        public const string Editor = "Editor";
        public const string RuntimeDiagnostic = "RuntimeDiagnostic";

        public const string ProcessDesign = "ProcessDesign";
        public const string ProcessReview = "ProcessReview";
        public const string ProcessCreate = "ProcessCreate";
        public const string ProcessEdit = "ProcessEdit";
        public const string ResourceEdit = "ResourceEdit";
        public const string RuntimeControl = "RuntimeControl";
        public const string SourceDevelopment = "SourceDevelopment";
        public const string PlatformConfiguration = "PlatformConfiguration";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Diagnostic, Editor, RuntimeDiagnostic,
            ProcessDesign, ProcessReview, ProcessCreate, ProcessEdit, ResourceEdit,
            RuntimeControl, SourceDevelopment, PlatformConfiguration
        };

        public static readonly IReadOnlyList<string> TaskProfiles = new[]
        {
            ProcessDesign, ProcessReview, ProcessCreate, ProcessEdit, ResourceEdit,
            RuntimeControl, SourceDevelopment, PlatformConfiguration
        };

        public static string Normalize(string value)
        {
            string match = All.FirstOrDefault(item =>
                string.Equals(item, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                throw new ArgumentException(
                    $"Automation MCP 工具档位不支持：{value}。可选：{string.Join("/", All)}。",
                    nameof(value));
            }
            return match;
        }

        public static bool IsTaskProfile(string value)
        {
            return TaskProfiles.Any(item =>
                string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }

        public static bool UsesDeveloperTools(string value)
        {
            return string.Equals(value, SourceDevelopment, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, Editor, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, Diagnostic, StringComparison.OrdinalIgnoreCase);
        }
    }
}
