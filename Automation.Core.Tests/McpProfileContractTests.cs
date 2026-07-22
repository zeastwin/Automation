using System;
// 模块：核心测试 / MCP Profile 契约。
// 职责范围：把独立 MCP 进程的工具集合与参数 Schema 自检接入统一测试入口。

using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class McpProfileContractTests
    {
        [TestMethod]
        [Timeout(30000)]
        public void EditorProfileAndSchemas_PassExecutableVerification()
        {
            string repositoryRoot = FindRepositoryRoot();
            string configuration = GetConfiguration();
            string serverAssembly = Path.Combine(repositoryRoot, "McpServer", "bin",
                configuration, "net8.0-windows", "Automation.McpServer.dll");
            Assert.IsTrue(File.Exists(serverAssembly), "找不到 MCP Server 构建输出：" + serverAssembly);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "\"" + serverAssembly + "\" --verify-profile",
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                Assert.IsTrue(process.WaitForExit(20000), "MCP Profile 校验进程超时。");
                Assert.AreEqual(0, process.ExitCode, output + Environment.NewLine + error);
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
    }
}
