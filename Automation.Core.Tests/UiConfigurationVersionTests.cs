// 模块：核心测试 / UI 缓存。
// 职责范围：验证 UI 投影缓存依赖的配置版本只随结构配置变化。

using System;
using System.Collections.Generic;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class UiConfigurationVersionTests
    {
        [TestMethod]
        public void ValueStore_RuntimeValueChangeDoesNotInvalidateConfigurationVersion()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                var variables = new Dictionary<string, DicValue>
                {
                    ["计数"] = new DicValue
                    {
                        Id = Guid.NewGuid(),
                        Index = 8,
                        Name = "计数",
                        Type = "double",
                        Value = "0",
                        Scope = VariableScopeContract.Public
                    }
                };
                Assert.IsTrue(runtime.Stores.Values.TryCommitConfiguration(
                    runtime.Paths.ConfigPath,
                    variables,
                    out string error), error);
                long version = runtime.Stores.Values.ConfigurationVersion;

                Assert.IsTrue(runtime.Stores.Values.setValueByName("计数", "1"));

                Assert.AreEqual(
                    version,
                    runtime.Stores.Values.ConfigurationVersion,
                    "运行值变化不应击穿变量结构和选择目录缓存。");
            }
        }

        [TestMethod]
        public void ProcessRepository_ReplaceAtOnlyAdvancesTargetRevision()
        {
            var repository = new ProcessDefinitionRepository();
            Proc first = CreateProcess("流程一");
            Proc second = CreateProcess("流程二");
            repository.ReplaceAll(new[] { first, second });
            long firstRevision = repository.GetRevision(first.head.Id);
            long secondRevision = repository.GetRevision(second.head.Id);

            Proc changed = ObjectGraphCloner.Clone(first);
            changed.head.Name = "流程一修改";
            repository.ReplaceAt(0, changed);

            Assert.IsTrue(repository.GetRevision(first.head.Id) > firstRevision);
            Assert.AreEqual(
                secondRevision,
                repository.GetRevision(second.head.Id),
                "修改一个流程时不应让其他流程的搜索、跳转和流程图缓存失效。");
        }

        private static Proc CreateProcess(string name)
        {
            return new Proc
            {
                head = new ProcHead
                {
                    Id = Guid.NewGuid(),
                    Name = name
                }
            };
        }
    }
}
