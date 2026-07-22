using System;
// 模块：核心测试 / 配置事务。
// 职责范围：验证 Work 与 value.json 的联合提交，以及未完成事务的启动恢复边界。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class ProcessVariableConfigurationTransactionTests
    {
        [TestMethod]
        public void Commit_WritesProcessesAndVariablesAsOneCompletedConfiguration()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = TestProcessFactory.CreateEndingProcess("事务流程");
                var variables = new Dictionary<string, DicValue>
                {
                    ["测试变量"] = new DicValue
                    {
                        Id = Guid.NewGuid(),
                        Index = 10,
                        Type = "double",
                        Name = "测试变量",
                        Scope = "public",
                        Value = "5"
                    }
                };

                bool committed = ProcessVariableConfigurationTransaction.Commit(
                    directory.FullPath,
                    new[] { process },
                    variables,
                    out string error,
                    out bool rollbackFailed);

                Assert.IsTrue(committed, error);
                Assert.IsFalse(rollbackFailed);
                string processPath = Path.Combine(directory.FullPath, "Work", "0.json");
                string valuePath = Path.Combine(directory.FullPath, "value.json");
                Assert.IsTrue(File.Exists(processPath));
                Assert.IsTrue(File.Exists(valuePath));
                StringAssert.Contains(File.ReadAllText(processPath), "事务流程");
                StringAssert.Contains(File.ReadAllText(valuePath), "测试变量");
                Assert.AreEqual(0, Directory.EnumerateDirectories(
                    directory.FullPath, ".change-set-transaction-*").Count());
            }
        }

        [TestMethod]
        public void RecoverPendingTransactions_RestoresPreviousConfiguration()
        {
            using (var directory = new TemporaryDirectory())
            {
                string activeWork = Path.Combine(directory.FullPath, "Work");
                string transaction = Path.Combine(
                    directory.FullPath, ".change-set-transaction-test");
                string oldWork = Path.Combine(transaction, "Work.old");
                Directory.CreateDirectory(activeWork);
                Directory.CreateDirectory(oldWork);
                File.WriteAllText(Path.Combine(activeWork, "0.json"), "new-process");
                File.WriteAllText(Path.Combine(directory.FullPath, "value.json"), "new-values");
                File.WriteAllText(Path.Combine(oldWork, "0.json"), "old-process");
                File.WriteAllText(Path.Combine(transaction, "value.old.json"), "old-values");
                File.WriteAllText(
                    Path.Combine(transaction, "manifest.json"),
                    "{\"WorkExisted\":true,\"ValueExisted\":true}");

                bool recovered = ProcessVariableConfigurationTransaction.RecoverPendingTransactions(
                    directory.FullPath, out string error);

                Assert.IsTrue(recovered, error);
                Assert.AreEqual(
                    "old-process", File.ReadAllText(Path.Combine(activeWork, "0.json")));
                Assert.AreEqual(
                    "old-values", File.ReadAllText(Path.Combine(directory.FullPath, "value.json")));
                Assert.IsFalse(Directory.Exists(transaction));
            }
        }
    }
}
