using System.Collections.Generic;
using System.IO;

namespace Automation
{
    /// <summary>
    /// EW-AI 外部运行组件的统一路径与可用性检查。
    /// </summary>
    internal static class GooseRuntimeEnvironment
    {
        public const string MachineGooseExecutablePath = @"D:\AutomationTools\Goose\goose.exe";
        public const string MachineGitCommandPath = @"D:\AutomationTools\Git\cmd";

        public static string MachineGitExecutablePath =>
            Path.Combine(MachineGitCommandPath, "git.exe");

        public static bool TryValidate(string gooseExecutablePath, out string error)
        {
            var missingComponents = new List<string>();
            string normalizedGoosePath = (gooseExecutablePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedGoosePath) || !File.Exists(normalizedGoosePath))
            {
                missingComponents.Add("Goose：" + (string.IsNullOrWhiteSpace(normalizedGoosePath)
                    ? "未配置可执行文件路径"
                    : normalizedGoosePath));
            }
            if (!File.Exists(MachineGitExecutablePath))
            {
                missingComponents.Add("Git：" + MachineGitExecutablePath);
            }

            if (missingComponents.Count == 0)
            {
                error = null;
                return true;
            }

            error = "EW-AI 运行组件不可用（" + string.Join("；", missingComponents)
                + "）。仅 EW-AI 功能已禁用，平台、HMI 与流程生产运行不受影响。";
            return false;
        }
    }
}
