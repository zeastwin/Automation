using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Automation.GooseShell
{
    internal static class Program
    {
        private const string ShellOverrideEnvironmentVariable = "AUTOMATION_GOOSE_POWERSHELL";

        public static async Task<int> Main(string[] args)
        {
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

            string command = args[commandIndex + 1];
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
