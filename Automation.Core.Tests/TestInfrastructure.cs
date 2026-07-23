using System;
// 模块：核心测试 / 测试基础设施。
// 职责范围：构造跨测试复用的最小流程与受控临时目录，不模拟生产服务。

using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    internal static class TestProcessFactory
    {
        public static Proc CreateEndingProcess(string name, int? delayMilliseconds = null)
        {
            var process = new Proc
            {
                head = new ProcHead { Name = name }
            };
            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = "执行并结束"
            };
            if (delayMilliseconds.HasValue)
            {
                step.Ops.Add(new Delay
                {
                    Id = Guid.NewGuid(),
                    DelayMs = delayMilliseconds.Value
                });
            }
            step.Ops.Add(new EndProcess { Id = Guid.NewGuid() });
            process.steps.Add(step);
            return process;
        }
    }

    internal sealed class TemporaryDirectory : IDisposable
    {
        private static readonly string TestRoot = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "Automation.Core.Tests"));

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(TestRoot);
            FullPath = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(FullPath);
        }

        public string FullPath { get; }

        public void Dispose()
        {
            string resolvedPath = Path.GetFullPath(FullPath);
            string allowedPrefix = TestRoot.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!resolvedPath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("测试临时目录超出允许的清理范围。");
            }
            if (Directory.Exists(resolvedPath))
            {
                foreach (string file in Directory.GetFiles(
                    resolvedPath,
                    "*",
                    SearchOption.AllDirectories))
                {
                    FileAttributes attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(
                            file,
                            attributes & ~FileAttributes.ReadOnly);
                    }
                }
                Directory.Delete(resolvedPath, true);
            }
        }
    }

    internal static class StaTestRunner
    {
        public static void Run(Action action, TimeSpan timeout)
        {
            ExceptionDispatchInfo failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            })
            {
                IsBackground = true,
                Name = "Automation.Core.Tests.STA"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(timeout))
            {
                Assert.Fail($"STA 测试超过 {timeout.TotalSeconds:0} 秒仍未结束。");
            }
            failure?.Throw();
        }
    }
}
