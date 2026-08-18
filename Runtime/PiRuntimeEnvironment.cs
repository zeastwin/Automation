using System.Collections.Generic;
using System.IO;

namespace Automation
{
    /// <summary>
    /// EW-AI 外部运行组件（Pi、Git、Git Bash）的统一路径与可用性检查。
    /// </summary>
    internal static class PiRuntimeEnvironment
    {
        public const string MachinePiExecutablePath = @"D:\AutomationTools\Pi\pi.exe";
        public const string MachineGitCommandPath = @"D:\AutomationTools\Git\cmd";

        public static string MachineGitExecutablePath =>
            Path.Combine(MachineGitCommandPath, "git.exe");

        /// <summary>Pi 内置 bash 工具使用的 Git Bash，与 git 同属 AutomationTools 部署。</summary>
        public static string MachineGitBashPath =>
            Path.GetFullPath(Path.Combine(MachineGitCommandPath, @"..\bin\bash.exe"));

        public static bool TryValidate(string piExecutablePath, out string error)
        {
            var missingComponents = new List<string>();
            string normalizedPiPath = (piExecutablePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedPiPath) || !File.Exists(normalizedPiPath))
            {
                missingComponents.Add("Pi：" + (string.IsNullOrWhiteSpace(normalizedPiPath)
                    ? "未配置可执行文件路径"
                    : normalizedPiPath));
            }
            if (!File.Exists(MachineGitExecutablePath))
            {
                missingComponents.Add("Git：" + MachineGitExecutablePath);
            }
            if (!File.Exists(MachineGitBashPath))
            {
                missingComponents.Add("Git Bash：" + MachineGitBashPath);
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
