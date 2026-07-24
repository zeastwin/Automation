using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Automation.GooseShell
{
    internal static class Program
    {
        private const string ShellOverrideEnvironmentVariable = "AUTOMATION_GOOSE_SHELL";
        private const string HostProcessIdEnvironmentVariable = "AUTOMATION_HOST_PROCESS_ID";
        private const string HostExecutableEnvironmentVariable = "AUTOMATION_HOST_EXECUTABLE";

        public static async Task<int> Main(string[] args)
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
            int commandIndex = Array.FindIndex(args,
                value => string.Equals(value, "-Command", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "-c", StringComparison.OrdinalIgnoreCase));
            if (commandIndex < 0 || commandIndex + 1 >= args.Length)
            {
                await Console.Error.WriteLineAsync("Goose Shell 缺少 -c/-Command 参数。");
                return 2;
            }

            string shellPath = ResolveGitBashPath();
            if (shellPath == null)
            {
                await Console.Error.WriteLineAsync("未找到 Git Bash（bash.exe）。");
                return 3;
            }

            string command = args[commandIndex + 1];
            if (TargetsProtectedHostProcess(command))
            {
                await Console.Error.WriteLineAsync(
                    "{\"ok\":false,\"type\":\"developer.shell.error\",\"errorCode\":\"HOST_PROCESS_PROTECTED\",\"message\":\"目标进程是当前 EW-AI 宿主进程。\",\"recovery\":{\"reason\":\"current_host_process_is_protected\",\"retryableWhen\":\"command_does_not_terminate_current_host\",\"sideEffects\":\"none\"}}");
                return 5;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = "--noprofile --norc -c " + QuoteArgument(command),
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            };
            // MSYS2 默认按当前代码页输出；固定 UTF-8，保证 Goose 侧严格 UTF-8 解码不错乱。
            startInfo.EnvironmentVariables["LANG"] = "C.UTF-8";
            startInfo.EnvironmentVariables["LC_ALL"] = "C.UTF-8";

            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    await Console.Error.WriteLineAsync("Git Bash 启动失败。");
                    return 4;
                }

                Task stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
                Task stderrTask = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
                Task exitTask = Task.Run(() => process.WaitForExit());
                await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
                return process.ExitCode;
            }
        }

        /// <summary>命令整体作为单个 -c 参数传递，只需处理双引号与反斜杠的 Windows 命令行转义。</summary>
        private static string QuoteArgument(string value)
        {
            var builder = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }
                builder.Append('\\', backslashes);
                backslashes = 0;
                builder.Append(c);
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private static bool TargetsProtectedHostProcess(string command)
        {
            if (!int.TryParse(
                    Environment.GetEnvironmentVariable(HostProcessIdEnvironmentVariable),
                    out int hostProcessId)
                || hostProcessId <= 0
                || string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            string hostId = Regex.Escape(hostProcessId.ToString());
            const RegexOptions options = RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant
                | RegexOptions.Singleline;
            if (Regex.IsMatch(
                    command,
                    @"\b(?:Stop-Process|spps|kill)\b[^;\r\n]*(?:-Id\s+)?" + hostId + @"\b",
                    options)
                || Regex.IsMatch(
                    command,
                    @"\btaskkill(?:\.exe)?\b[^;\r\n]*/PID\s+" + hostId + @"\b",
                    options)
                || Regex.IsMatch(
                    command,
                    @"GetProcessById\s*\(\s*" + hostId + @"\s*\)\s*\.\s*Kill\s*\(",
                    options))
            {
                return true;
            }

            string hostExecutable = Environment.GetEnvironmentVariable(HostExecutableEnvironmentVariable);
            string hostProcessName = Path.GetFileNameWithoutExtension(hostExecutable);
            if (string.IsNullOrWhiteSpace(hostProcessName))
            {
                return false;
            }

            string hostName = Regex.Escape(hostProcessName);
            return Regex.IsMatch(
                       command,
                       @"\b(?:Stop-Process|spps)\b[^;\r\n]*-Name\s+['""]?" + hostName + @"(?:\.exe)?['""]?\b",
                       options)
                || Regex.IsMatch(
                       command,
                       @"\b(?:Get-Process|gps)\b[^;|\r\n]*(?:-Name\s+)?['""]?" + hostName
                           + @"(?:\.exe)?['""]?[^;\r\n]*\|[^;\r\n]*\b(?:Stop-Process|spps|kill)\b",
                       options)
                || Regex.IsMatch(
                       command,
                       @"\btaskkill(?:\.exe)?\b[^;\r\n]*/IM\s+['""]?" + hostName + @"(?:\.exe)?['""]?\b",
                       options)
                || Regex.IsMatch(
                       command,
                       @"GetProcessesByName\s*\(\s*['""]" + hostName + @"['""]\s*\)[^;\r\n]*\.\s*Kill\s*\(",
                       options);
        }

        private static string ResolveGitBashPath()
        {
            string overridePath = Environment.GetEnvironmentVariable(ShellOverrideEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            {
                return Path.GetFullPath(overridePath);
            }

            var candidates = new List<string>();
            foreach (string programFiles in new[]
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)")
            })
            {
                if (!string.IsNullOrWhiteSpace(programFiles))
                {
                    candidates.Add(Path.Combine(programFiles, "Git", "bin", "bash.exe"));
                }
            }

            // git.exe 位于 <Git>\cmd，bash.exe 位于 <Git>\bin，按 PATH 中的 git 位置推导。
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string pathEntry in pathValue.Split(Path.PathSeparator))
            {
                string directory = pathEntry.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }
                string gitExe = Path.Combine(directory, "git.exe");
                if (File.Exists(gitExe))
                {
                    candidates.Add(Path.GetFullPath(Path.Combine(directory, @"..\bin\bash.exe")));
                }
            }

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
        }
    }
}
