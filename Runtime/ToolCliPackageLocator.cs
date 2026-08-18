using System;
using System.Collections.Generic;
using System.IO;

namespace Automation
{
    /// <summary>
    /// 定位本机 Automation.ToolCli 运行包。AI 子进程经环境变量 AUTOMATION_TOOLCLI_PATH
    /// 拿到该 exe 后，以 cli list/schema/call 子命令直连 Bridge，不启动常驻服务进程。
    /// </summary>
    internal static class ToolCliPackageLocator
    {
        /// <summary>
        /// 解析本机 Automation.ToolCli.exe 完整路径；找不到完整运行包（exe、dll、deps.json、
        /// runtimeconfig.json 齐备）时返回 null。
        /// </summary>
        public static string ResolveToolCliExecutablePath()
        {
            var candidates = new List<string>();
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            AddCandidate(candidates, Path.Combine(baseDirectory, "Automation.ToolCli.exe"));
            AddCandidate(candidates, Path.Combine(baseDirectory, "ToolCli", "Automation.ToolCli.exe"));
            AddCandidate(candidates, Path.Combine(baseDirectory, "Tools", "ToolCli", "Automation.ToolCli.exe"));
            string projectRoot = ResolveProjectRoot(baseDirectory);
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                AddCandidate(candidates, Path.Combine(projectRoot, "ToolCli", "bin", "x64", "Debug", "net8.0-windows", "Automation.ToolCli.exe"));
                AddCandidate(candidates, Path.Combine(projectRoot, "ToolCli", "bin", "x64", "Release", "net8.0-windows", "Automation.ToolCli.exe"));
                AddCandidate(candidates, Path.Combine(projectRoot, "ToolCli", "bin", "Debug", "net8.0-windows", "Automation.ToolCli.exe"));
                AddCandidate(candidates, Path.Combine(projectRoot, "ToolCli", "bin", "Release", "net8.0-windows", "Automation.ToolCli.exe"));
                AddCandidate(candidates, Path.Combine(projectRoot, "bin", "Debug", "ToolCli", "Automation.ToolCli.exe"));
                AddCandidate(candidates, Path.Combine(projectRoot, "bin", "Release", "ToolCli", "Automation.ToolCli.exe"));
            }
            foreach (string candidate in candidates)
            {
                if (IsCompleteToolCliRuntime(candidate)) return candidate;
            }
            return null;
        }

        private static bool IsCompleteToolCliRuntime(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return false;
            string directory = Path.GetDirectoryName(executablePath);
            string assemblyName = Path.GetFileNameWithoutExtension(executablePath);
            return !string.IsNullOrWhiteSpace(directory)
                && File.Exists(Path.Combine(directory, assemblyName + ".dll"))
                && File.Exists(Path.Combine(directory, assemblyName + ".deps.json"))
                && File.Exists(Path.Combine(directory, assemblyName + ".runtimeconfig.json"));
        }

        private static string ResolveProjectRoot(string baseDirectory)
        {
            DirectoryInfo directory = new DirectoryInfo(baseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Automation.csproj"))) return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        private static void AddCandidate(ICollection<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || candidates.Contains(path)) return;
            candidates.Add(path);
        }
    }
}
