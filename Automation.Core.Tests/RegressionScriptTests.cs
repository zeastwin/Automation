using System;
// 模块：核心测试 / 隔离回归入口。
// 职责范围：把仍需独立进程、真实消息循环或旧式运行夹具的回归统一接入 MSTest。

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class RegressionScriptTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        [DataRow("CoreRuntimeRegression.ps1", "assembly")]
        [DataRow("DataBreakpointRegression.ps1", "assembly")]
        [DataRow("HeadlessPlatformHostRegression.ps1", "configuration")]
        [DataRow("HmiDevelopmentSourceLocatorRegression.ps1", "assembly")]
        [DataRow("InspectorSelectionPickerTriggerRegression.ps1", "assembly")]
        [DataRow("InspectorStandardDropDownRegression.ps1", "assembly")]
        [DataRow("InstructionSelectionFlickerRegression.ps1", "assembly")]
        [DataRow("PopupDialogAiContractRegression.ps1", "assembly")]
        [DataRow("ProcessFlowGraphRegression.ps1", "assembly")]
        [DataRow("RuntimeBlackBoxRegression.ps1", "assembly")]
        [DataRow(@"..\..\Scripts\Invoke-AiValidation.ps1", "project")]
        [Timeout(240000)]
        public void ExistingRegression_CompletesThroughUnifiedRunner(
            string scriptName,
            string argumentMode)
        {
            string repositoryRoot = FindRepositoryRoot();
            string configuration = GetConfiguration();
            string pwshPath = FindExecutableOnPath("pwsh.exe");
            string scriptPath = Path.GetFullPath(Path.Combine(
                repositoryRoot, "Automation.Core.Tests", "RegressionScripts", scriptName));
            string automationPath = Path.Combine(
                repositoryRoot, "bin", configuration, "Automation.exe");
            Assert.IsFalse(string.IsNullOrWhiteSpace(pwshPath),
                "找不到 PowerShell 7；隔离回归统一使用 UTF-8 行为确定的 pwsh.exe。");
            Assert.IsTrue(File.Exists(scriptPath), $"找不到回归脚本：{scriptPath}");
            Assert.IsTrue(File.Exists(automationPath), $"找不到主程序集：{automationPath}");

            string specificArguments;
            if (argumentMode == "configuration")
            {
                specificArguments = " -Configuration " + configuration;
            }
            else if (argumentMode == "project")
            {
                specificArguments = " -ProjectPath "
                    + Quote(Path.Combine(repositoryRoot, "Automation.csproj"));
            }
            else
            {
                specificArguments = " -AssemblyPath " + Quote(automationPath);
            }
            var startInfo = new ProcessStartInfo
            {
                FileName = pwshPath,
                Arguments = "-NoProfile -NonInteractive -File " + Quote(scriptPath)
                    + specificArguments,
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            using (Process process = Process.Start(startInfo))
            {
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(210000))
                {
                    process.Kill();
                    Assert.Fail($"回归脚本超时：{scriptName}");
                }
                Task.WaitAll(outputTask, errorTask);
                string output = outputTask.Result.Trim();
                string error = errorTask.Result.Trim();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    TestContext.WriteLine(output);
                }
                Assert.AreEqual(0, process.ExitCode,
                    $"回归脚本失败：{scriptName}{Environment.NewLine}{error}");
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Automation.sln")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("无法从测试输出目录定位 Automation.sln。");
        }

        private static string GetConfiguration()
        {
#if DEBUG
            return "Debug";
#else
            return "Release";
#endif
        }

        private static string FindExecutableOnPath(string executableName)
        {
            foreach (string path in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }
                string candidate = Path.Combine(path.Trim(), executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
