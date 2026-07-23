// 模块：核心测试 / 配置版本。
// 职责范围：固化统一历史、保护点、业务差异和源码文本规范化边界。

using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class ConfigurationVersionServiceTests
    {
        [TestMethod]
        public void Restore_CreatesProtectionOnMainHistoryAndRequiresRestart()
        {
            using (var directory = new TemporaryDirectory())
            {
                string ioMapPath = Path.Combine(directory.FullPath, "IOMap.json");
                string hmiPath = CreateHmiSource(
                    directory.FullPath,
                    "public class TestHmi { public const int Value = 1; }\r\n");
                const string originalConfiguration = "{\"Name\":\"原始配置\"}";
                const string currentConfiguration = "{\"Name\":\"当前配置\"}";
                File.WriteAllText(
                    ioMapPath,
                    originalConfiguration,
                    new UTF8Encoding(false));

                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "原始版本",
                        "测试",
                        out string snapshotError),
                    snapshotError);
                ConfigurationVersionRecord target = runtime.VersionService
                    .GetHistory(out _, out string historyError)
                    .Single();
                Assert.IsTrue(string.IsNullOrEmpty(historyError), historyError);

                File.WriteAllText(
                    ioMapPath,
                    currentConfiguration,
                    new UTF8Encoding(false));
                File.WriteAllText(
                    hmiPath,
                    "public class TestHmi { public const int Value = 2; }\r\n",
                    new UTF8Encoding(false));
                bool restartRequired = false;

                Assert.IsTrue(
                    runtime.VersionService.Restore(
                        target.CommitId,
                        () => true,
                        () =>
                        {
                            restartRequired = true;
                            runtime.Readiness.VersionRestartRequired = true;
                        },
                        out string restoreError),
                    restoreError);

                Assert.IsTrue(restartRequired);
                Assert.IsTrue(runtime.Readiness.VersionRestartRequired);
                Assert.AreEqual(
                    originalConfiguration,
                    File.ReadAllText(ioMapPath, Encoding.UTF8));
                StringAssert.Contains(
                    File.ReadAllText(hmiPath, Encoding.UTF8),
                    "Value = 1");

                ConfigurationVersionRecord[] history = runtime.VersionService
                    .GetHistory(out bool dirty, out historyError)
                    .ToArray();
                Assert.IsTrue(string.IsNullOrEmpty(historyError), historyError);
                Assert.IsFalse(dirty);
                Assert.AreEqual(3, history.Length);
                Assert.AreEqual("还原结果", history[0].SnapshotType);
                StringAssert.StartsWith(history[0].Message, "还原结果：");
                Assert.AreEqual("还原前保护点", history[1].SnapshotType);
                StringAssert.StartsWith(history[1].Message, "还原前保护点：");
                Assert.AreEqual(target.CommitId, history[2].CommitId);

                var protectionDiff = runtime.VersionService.GetStructuredDiff(
                    history[1].CommitId,
                    true,
                    out string diffError);
                Assert.IsTrue(string.IsNullOrEmpty(diffError), diffError);
                Assert.IsTrue(protectionDiff.Any(item => item.Category == "IO"));
                Assert.IsTrue(protectionDiff.Any(item => item.Category == "HMI 代码"));
            }
        }

        [TestMethod]
        public void SourceLineEndings_DoNotCreateFalseDifference()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(
                    directory.FullPath,
                    "public class TestHmi\r\n{\r\n    public int Value => 1;\r\n}\r\n");
                var runtime = new PlatformRuntime(directory.FullPath);

                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "换行测试",
                        "测试",
                        out string snapshotError),
                    snapshotError);
                ConfigurationVersionRecord snapshot = runtime.VersionService
                    .GetHistory(out bool dirty, out string historyError)
                    .Single();

                Assert.IsTrue(string.IsNullOrEmpty(historyError), historyError);
                Assert.IsFalse(dirty);
                var diff = runtime.VersionService.GetStructuredDiff(
                    snapshot.CommitId,
                    false,
                    out string diffError);
                Assert.IsTrue(string.IsNullOrEmpty(diffError), diffError);
                Assert.IsFalse(diff.Any(item => item.Category == "HMI 代码"));
            }
        }

        [TestMethod]
        public void SameNameOperations_AreKeptAsSeparateBusinessDifferences()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string workRoot = Path.Combine(directory.FullPath, "Work");
                Directory.CreateDirectory(workRoot);
                string processPath = Path.Combine(workRoot, "process.json");
                Guid stepId = Guid.NewGuid();
                Guid firstOperationId = Guid.NewGuid();
                Guid secondOperationId = Guid.NewGuid();
                File.WriteAllText(
                    processPath,
                    CreateProcessJson(
                        stepId,
                        firstOperationId,
                        1,
                        secondOperationId,
                        2),
                    new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);

                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "原始流程",
                        "测试",
                        out string snapshotError),
                    snapshotError);
                ConfigurationVersionRecord snapshot = runtime.VersionService
                    .GetHistory(out _, out _)
                    .Single();
                File.WriteAllText(
                    processPath,
                    CreateProcessJson(
                        stepId,
                        firstOperationId,
                        10,
                        secondOperationId,
                        20),
                    new UTF8Encoding(false));

                var diff = runtime.VersionService.GetStructuredDiff(
                    snapshot.CommitId,
                    false,
                    out string diffError);
                Assert.IsTrue(string.IsNullOrEmpty(diffError), diffError);
                ConfigurationVersionDiffEntry[] operations = diff
                    .Where(item => item.Category == "指令")
                    .ToArray();
                Assert.AreEqual(2, operations.Length);
                Assert.AreNotEqual(operations[0].Target, operations[1].Target);
            }
        }

        [TestMethod]
        public void StableIdArrayReorder_DoesNotCreateBusinessDifference()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string ioMapPath = Path.Combine(directory.FullPath, "IOMap.json");
                string firstId = Guid.NewGuid().ToString("D");
                string secondId = Guid.NewGuid().ToString("D");
                File.WriteAllText(
                    ioMapPath,
                    "[{\"Id\":\"" + firstId + "\",\"Name\":\"A\",\"Value\":1},"
                        + "{\"Id\":\"" + secondId + "\",\"Name\":\"B\",\"Value\":2}]",
                    new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);

                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "原始顺序",
                        "测试",
                        out string snapshotError),
                    snapshotError);
                ConfigurationVersionRecord snapshot = runtime.VersionService
                    .GetHistory(out _, out _)
                    .Single();
                File.WriteAllText(
                    ioMapPath,
                    "[{\"Id\":\"" + secondId + "\",\"Name\":\"B\",\"Value\":2},"
                        + "{\"Id\":\"" + firstId + "\",\"Name\":\"A\",\"Value\":1}]",
                    new UTF8Encoding(false));

                var diff = runtime.VersionService.GetStructuredDiff(
                    snapshot.CommitId,
                    false,
                    out string diffError);
                Assert.IsTrue(string.IsNullOrEmpty(diffError), diffError);
                Assert.AreEqual(0, diff.Count);
                runtime.VersionService.GetHistory(
                    out bool dirty,
                    out string historyError);
                Assert.IsTrue(string.IsNullOrEmpty(historyError), historyError);
                Assert.IsFalse(dirty);
            }
        }

        [TestMethod]
        public void VariableReindex_UsesStableIdInsteadOfSplittingDeleteAndAdd()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string valuePath = Path.Combine(directory.FullPath, "value.json");
                string variableId = Guid.NewGuid().ToString("D");
                File.WriteAllText(
                    valuePath,
                    CreateVariableJson(variableId, 1, "10"),
                    new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(
                    runtime.Stores.Values.Load(directory.FullPath),
                    "测试准备失败：变量配置未加载到运行时。");

                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "原始变量",
                        "测试",
                        out string snapshotError),
                    snapshotError);
                ConfigurationVersionRecord snapshot = runtime.VersionService
                    .GetHistory(out _, out _)
                    .Single();
                File.WriteAllText(
                    valuePath,
                    CreateVariableJson(variableId, 2, "20"),
                    new UTF8Encoding(false));

                ConfigurationVersionDiffEntry[] variables = runtime.VersionService
                    .GetStructuredDiff(
                        snapshot.CommitId,
                        false,
                        out string diffError)
                    .Where(item => item.Category == "变量")
                    .ToArray();
                Assert.IsTrue(string.IsNullOrEmpty(diffError), diffError);
                Assert.AreEqual(1, variables.Length);
                Assert.AreEqual("修改", variables[0].ChangeType);
                Assert.IsTrue(
                    variables[0].Details.Any(detail =>
                        detail.FieldName == "索引"));
                Assert.IsTrue(
                    variables[0].Details.Any(detail =>
                        detail.FieldName == "初始值"));
            }
        }

        [TestMethod]
        public void ManualSnapshot_PersistsCurrentRuntimeValueBeforeCapture()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string valuePath = Path.Combine(directory.FullPath, "value.json");
                string variableId = Guid.NewGuid().ToString("D");
                File.WriteAllText(
                    valuePath,
                    CreateVariableJson(variableId, 1, "10"),
                    new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(runtime.Stores.Values.Load(directory.FullPath));
                Assert.IsTrue(
                    runtime.Stores.Values.setValueByName(
                        "测试变量",
                        "25",
                        "版本测试"));

                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "运行值",
                        "测试",
                        out string snapshotError),
                    snapshotError);

                JObject saved = JObject.Parse(
                    File.ReadAllText(valuePath, Encoding.UTF8));
                Assert.AreEqual(
                    "25",
                    saved["测试变量"]?["Value"]?.Value<string>());
            }
        }

        [TestMethod]
        public void ManualSnapshot_WhenConfigurationUnchanged_IsRejected()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "第一个版本",
                        "测试",
                        out string firstError),
                    firstError);

                Assert.IsFalse(
                    runtime.VersionService.CreateManualSnapshot(
                        "重复版本",
                        "测试",
                        out string duplicateError));
                StringAssert.Contains(
                    duplicateError,
                    "无需重复创建");
                Assert.AreEqual(
                    1,
                    runtime.VersionService.GetHistory(
                        out _,
                        out _).Count);
            }
        }

        [TestMethod]
        public void HistoryRetention_AtThresholdPhysicallyCompactsToLatestOneHundred()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string ioMapPath = Path.Combine(
                    directory.FullPath,
                    "IOMap.json");
                var runtime = new PlatformRuntime(directory.FullPath);
                string oldestCommitId = null;

                for (int index = 0; index < 120; index++)
                {
                    File.WriteAllText(
                        ioMapPath,
                        "{\"Revision\":" + index + "}",
                        new UTF8Encoding(false));
                    Assert.IsTrue(
                        runtime.VersionService.CreateManualSnapshot(
                            "版本" + index,
                            "测试",
                            out string snapshotError),
                        "第 " + index + " 个版本失败：" + snapshotError);
                    if (index == 0)
                    {
                        oldestCommitId = runtime.VersionService
                            .GetHistory(out _, out _)
                            .Single()
                            .CommitId;
                    }
                }

                ConfigurationVersionRecord[] history = runtime.VersionService
                    .GetHistory(out _, out string historyError)
                    .ToArray();
                Assert.IsTrue(string.IsNullOrEmpty(historyError), historyError);
                Assert.AreEqual(100, history.Length);
                StringAssert.EndsWith(history[0].Message, "版本119");
                StringAssert.EndsWith(history[99].Message, "版本20");

                runtime.VersionService.GetStructuredDiff(
                    oldestCommitId,
                    false,
                    out string expiredError);
                StringAssert.Contains(expiredError, "找不到选中的版本");

                string versionParent = Path.Combine(
                    directory.FullPath,
                    ".AutomationVersions");
                Assert.IsFalse(
                    Directory.GetDirectories(
                        versionParent,
                        ".Configuration-*",
                        SearchOption.TopDirectoryOnly).Any(),
                    "裁剪完成后不应残留临时仓库或备份仓库。");
            }
        }

        [TestMethod]
        public void InterruptedRestore_OnNextStartupRestoresCompleteBackup()
        {
            using (var directory = new TemporaryDirectory())
            {
                string hmiPath = CreateHmiSource(
                    directory.FullPath,
                    "public class TestHmi { public const int Value = 2; }\r\n");
                string ioMapPath = Path.Combine(
                    directory.FullPath,
                    "IOMap.json");
                File.WriteAllText(
                    ioMapPath,
                    "{\"Name\":\"部分新配置\"}",
                    new UTF8Encoding(false));

                string operationRoot = Path.Combine(
                    directory.FullPath,
                    ".AutomationVersions",
                    "Configuration",
                    "Restore",
                    Guid.NewGuid().ToString("N"));
                string backupRoot = Path.Combine(
                    operationRoot,
                    "backup");
                Directory.CreateDirectory(
                    Path.Combine(backupRoot, "Hmi"));
                File.WriteAllText(
                    Path.Combine(backupRoot, "IOMap.json"),
                    "{\"Name\":\"完整旧配置\"}",
                    new UTF8Encoding(false));
                File.WriteAllText(
                    Path.Combine(backupRoot, "Hmi", "TestHmi.cs"),
                    "public class TestHmi { public const int Value = 1; }\r\n",
                    new UTF8Encoding(false));
                File.WriteAllText(
                    Path.Combine(
                        operationRoot,
                        "restore-transaction.json"),
                    new JObject
                    {
                        ["Status"] = "Replacing",
                        ["TargetCommitId"] = "test",
                        ["CreatedAt"] = DateTimeOffset.Now
                    }.ToString(),
                    new UTF8Encoding(false));

                _ = new PlatformRuntime(directory.FullPath);

                StringAssert.Contains(
                    File.ReadAllText(ioMapPath, Encoding.UTF8),
                    "完整旧配置");
                StringAssert.Contains(
                    File.ReadAllText(hmiPath, Encoding.UTF8),
                    "Value = 1");
                Assert.IsFalse(Directory.Exists(operationRoot));
            }
        }

        [TestMethod]
        public void RestoreResult_WhenRestoredAgain_DoesNotRepeatMessagePrefix()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string ioMapPath = Path.Combine(
                    directory.FullPath,
                    "IOMap.json");
                File.WriteAllText(
                    ioMapPath,
                    "{\"Revision\":1}",
                    new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "原始版本",
                        "测试",
                        out string snapshotError),
                    snapshotError);
                string originalCommitId = runtime.VersionService
                    .GetHistory(out _, out _)
                    .Single()
                    .CommitId;

                File.WriteAllText(
                    ioMapPath,
                    "{\"Revision\":2}",
                    new UTF8Encoding(false));
                Assert.IsTrue(
                    runtime.VersionService.Restore(
                        originalCommitId,
                        () => true,
                        () => runtime.Readiness.VersionRestartRequired = true,
                        out string firstRestoreError),
                    firstRestoreError);
                ConfigurationVersionRecord firstResult =
                    runtime.VersionService.GetHistory(out _, out _)
                        .First();

                File.WriteAllText(
                    ioMapPath,
                    "{\"Revision\":3}",
                    new UTF8Encoding(false));
                Assert.IsTrue(
                    runtime.VersionService.Restore(
                        firstResult.CommitId,
                        () => true,
                        () => runtime.Readiness.VersionRestartRequired = true,
                        out string secondRestoreError),
                    secondRestoreError);
                string latestMessage = runtime.VersionService
                    .GetHistory(out _, out _)
                    .First()
                    .Message;

                Assert.AreEqual(
                    firstResult.Message,
                    latestMessage);
                Assert.AreEqual(
                    1,
                    CountOccurrences(
                        latestMessage,
                        "还原结果："));
            }
        }

        [TestMethod]
        public void InterruptedRestore_WhenMarkedCompleted_PreservesRestoredFiles()
        {
            using (var directory = new TemporaryDirectory())
            {
                string hmiPath = CreateHmiSource(
                    directory.FullPath,
                    "public class TestHmi { public const int Value = 2; }\r\n");
                string ioMapPath = Path.Combine(
                    directory.FullPath,
                    "IOMap.json");
                File.WriteAllText(
                    ioMapPath,
                    "{\"Name\":\"已还原配置\"}",
                    new UTF8Encoding(false));

                string operationRoot = Path.Combine(
                    directory.FullPath,
                    ".AutomationVersions",
                    "Configuration",
                    "Restore",
                    Guid.NewGuid().ToString("N"));
                string backupRoot = Path.Combine(
                    operationRoot,
                    "backup");
                Directory.CreateDirectory(
                    Path.Combine(backupRoot, "Hmi"));
                File.WriteAllText(
                    Path.Combine(backupRoot, "IOMap.json"),
                    "{\"Name\":\"还原前配置\"}",
                    new UTF8Encoding(false));
                File.WriteAllText(
                    Path.Combine(backupRoot, "Hmi", "TestHmi.cs"),
                    "public class TestHmi { public const int Value = 1; }\r\n",
                    new UTF8Encoding(false));
                File.WriteAllText(
                    Path.Combine(
                        operationRoot,
                        "restore-transaction.json"),
                    new JObject
                    {
                        ["Status"] = "Completed",
                        ["TargetCommitId"] = "test",
                        ["CreatedAt"] = DateTimeOffset.Now
                    }.ToString(),
                    new UTF8Encoding(false));

                _ = new PlatformRuntime(directory.FullPath);

                StringAssert.Contains(
                    File.ReadAllText(ioMapPath, Encoding.UTF8),
                    "已还原配置");
                StringAssert.Contains(
                    File.ReadAllText(hmiPath, Encoding.UTF8),
                    "Value = 2");
                Assert.IsFalse(Directory.Exists(operationRoot));
            }
        }

        private static string CreateHmiSource(string root, string content)
        {
            string hmiRoot = Path.Combine(root, "Hmi");
            Directory.CreateDirectory(hmiRoot);
            string path = Path.Combine(hmiRoot, "TestHmi.cs");
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        private static string CreateProcessJson(
            Guid stepId,
            Guid firstOperationId,
            int firstValue,
            Guid secondOperationId,
            int secondValue)
        {
            return "{\"head\":{\"Name\":\"流程A\",\"Id\":\""
                + Guid.NewGuid().ToString("D")
                + "\"},\"steps\":[{\"Id\":\""
                + stepId.ToString("D")
                + "\",\"Name\":\"步骤A\",\"Ops\":["
                + "{\"Id\":\""
                + firstOperationId.ToString("D")
                + "\",\"Name\":\"同名指令\",\"OperaType\":\"测试\",\"Value\":"
                + firstValue
                + "},{\"Id\":\""
                + secondOperationId.ToString("D")
                + "\",\"Name\":\"同名指令\",\"OperaType\":\"测试\",\"Value\":"
                + secondValue
                + "}]}]}";
        }

        private static string CreateVariableJson(
            string id,
            int index,
            string value)
        {
            return "{\"测试变量\":{\"Id\":\""
                + id
                + "\",\"Index\":"
                + index
                + ",\"Type\":\"double\",\"Name\":\"测试变量\",\"Value\":\""
                + value
                + "\",\"Scope\":\"public\",\"OwnerProcId\":null,\"Note\":\"\",\"isMark\":false}}";
        }

        private static int CountOccurrences(
            string value,
            string fragment)
        {
            int count = 0;
            int startIndex = 0;
            while ((startIndex = value.IndexOf(
                fragment,
                startIndex,
                StringComparison.Ordinal)) >= 0)
            {
                count++;
                startIndex += fragment.Length;
            }
            return count;
        }
    }
}
