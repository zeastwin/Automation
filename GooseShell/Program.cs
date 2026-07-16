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
        private const string ShellOverrideEnvironmentVariable = "AUTOMATION_GOOSE_POWERSHELL";
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
                await Console.Error.WriteLineAsync("Goose Shell 缺少 -Command 参数。");
                return 2;
            }

            string shellPath = ResolvePowerShellPath();
            if (shellPath == null)
            {
                await Console.Error.WriteLineAsync("未找到 PowerShell 7 或系统 PowerShell。");
                return 3;
            }

            string command = UnwrapNestedPowerShellCommand(args[commandIndex + 1]);
            if (TargetsProtectedHostProcess(command))
            {
                await Console.Error.WriteLineAsync(
                    "{\"ok\":false,\"type\":\"developer.shell.error\",\"errorCode\":\"HOST_PROCESS_PROTECTED\",\"message\":\"目标进程是当前 EW-AI 宿主进程。\",\"recovery\":{\"reason\":\"current_host_process_is_protected\",\"retryableWhen\":\"command_does_not_terminate_current_host\",\"sideEffects\":\"none\"}}");
                return 5;
            }
            const string utf8Bootstrap =
                "$utf8NoBom=[System.Text.UTF8Encoding]::new($false);"
                + "[Console]::InputEncoding=$utf8NoBom;"
                + "[Console]::OutputEncoding=$utf8NoBom;"
                + "$global:OutputEncoding=$utf8NoBom;"
                + "$global:ProgressPreference='SilentlyContinue';"
                + "$PSDefaultParameterValues['*:Encoding']='utf8';";
            string encodedCommand = Convert.ToBase64String(
                Encoding.Unicode.GetBytes(utf8Bootstrap + command));

            var startInfo = new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = "-NoLogo -NoProfile -NonInteractive -InputFormat Text -OutputFormat Text -EncodedCommand " + encodedCommand,
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    await Console.Error.WriteLineAsync("PowerShell 启动失败。");
                    return 4;
                }

                Task stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
                Task stderrTask = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
                Task exitTask = Task.Run(() => process.WaitForExit());
                await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
                return process.ExitCode;
            }
        }

        private static string UnwrapNestedPowerShellCommand(string command)
        {
            Match match = Regex.Match(command ?? string.Empty,
                @"^\s*(?:powershell|pwsh)(?:\.exe)?\s+(?:(?:-NoLogo|-NoProfile|-NonInteractive)\s+)*(?:-Command|-c)\s+(?<script>[\s\S]+?)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return command;
            }

            string script = match.Groups["script"].Value.Trim();
            if (script.Length >= 2
                && ((script[0] == '"' && script[script.Length - 1] == '"')
                    || (script[0] == '\'' && script[script.Length - 1] == '\'')))
            {
                script = script.Substring(1, script.Length - 2);
            }
            return script;
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

        private static string ResolvePowerShellPath()
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
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            })
            {
                if (!string.IsNullOrWhiteSpace(programFiles))
                {
                    candidates.Add(Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"));
                }
            }

            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"));
            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
        }
    }
}
