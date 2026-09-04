// 模块：核心测试 / 配置版本。
// 职责范围：固化统一历史、保护点、业务差异和源码文本规范化边界。

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
                bool applyInvoked = false;

                ConfigurationRestoreResult restoreResult = runtime.VersionService.Restore(
                    target.CommitId,
                    () => true,
                    () =>
                    {
                        restartRequired = true;
                        runtime.Readiness.VersionRestartRequired = true;
                    },
                    () =>
                    {
                        applyInvoked = true;
                        return false;
                    });
                Assert.IsTrue(restoreResult.Success, restoreResult.Error);

                Assert.IsTrue(restartRequired);
                Assert.IsFalse(
                    applyInvoked,
                    "HMI 源码变更属于重启档，不得尝试免重启生效。");
                Assert.IsTrue(restoreResult.RestartRequired);
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
        public void DeviceProjectFile_IsTrackedAndRestoredWithRestartTier()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string projectPath = Path.Combine(directory.FullPath, "MachineApp.csproj");
                const string originalProject =
                    "<Project>\r\n  <ItemGroup />\r\n</Project>\r\n";
                File.WriteAllText(projectPath, originalProject, new UTF8Encoding(false));
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

                // 模拟源码开发在工程文件中注册新 HMI 源文件。
                const string modifiedProject =
                    "<Project>\r\n  <ItemGroup><Compile Include=\"Hmi\\NewPage.cs\" /></ItemGroup>\r\n</Project>\r\n";
                File.WriteAllText(projectPath, modifiedProject, new UTF8Encoding(false));

                bool applyInvoked = false;
                ConfigurationRestoreResult result = runtime.VersionService.Restore(
                    target.CommitId,
                    () => true,
                    () => runtime.Readiness.VersionRestartRequired = true,
                    () =>
                    {
                        applyInvoked = true;
                        return false;
                    });
                Assert.IsTrue(result.Success, result.Error);
                Assert.IsTrue(result.RestartRequired);
                Assert.IsFalse(
                    applyInvoked,
                    "工程文件变更属于重启档，不得尝试免重启生效。");

                // 快照以规范化文本落盘，还原后行尾统一为 \n。
                Assert.AreEqual(
                    originalProject.Replace("\r\n", "\n"),
                    File.ReadAllText(projectPath, Encoding.UTF8));

                ConfigurationVersionRecord[] history = runtime.VersionService
                    .GetHistory(out bool dirty, out historyError)
                    .ToArray();
                Assert.IsTrue(string.IsNullOrEmpty(historyError), historyError);
                Assert.IsFalse(dirty);
                ConfigurationVersionDiffEntry[] protectionDiff = runtime.VersionService
                    .GetStructuredDiff(
                        history.Single(item => item.SnapshotType == "还原前保护点").CommitId,
                        true,
                        out string diffError)
                    .ToArray();
                Assert.IsTrue(string.IsNullOrEmpty(diffError), diffError);
                Assert.IsTrue(
                    protectionDiff.Any(item =>
                        item.Category == "HMI 代码"
                        && string.Equals(item.Target, "Project" + Path.DirectorySeparatorChar + "MachineApp.csproj", StringComparison.OrdinalIgnoreCase)),
                    "还原前保护点差异应包含设备工程文件变更。");
            }
        }

        [TestMethod]
        public void Restore_WhenOnlyEditStateFilesChanged_AppliesInPlaceWithoutRestart()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                var runtime = new PlatformRuntime(directory.FullPath);
                ComposeOfflineEngine(runtime);

                Assert.IsTrue(
                    runtime.Stores.Values.TrySetValue(
                        0,
                        "测试变量",
                        "double",
                        "0",
                        "测试用途",
                        "测试初始化"),
                    "测试准备失败：无法创建测试变量。");
                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot("原始版本", "测试", out string snapshotError),
                    snapshotError);
                string commitId = runtime.VersionService
                    .GetHistory(out _, out _)
                    .Single()
                    .CommitId;
                Assert.IsTrue(
                    runtime.Stores.Values.setValueByName("测试变量", "5"),
                    "测试准备失败：无法修改变量运行值。");
                Assert.IsTrue(
                    runtime.Stores.Values.Save(runtime.Paths.ConfigPath),
                    "测试准备失败：无法保存变量运行值。");

                bool applyInvoked = false;
                ConfigurationRestoreResult result = runtime.VersionService.Restore(
                    commitId,
                    () => true,
                    () => runtime.Readiness.VersionRestartRequired = true,
                    () =>
                    {
                        applyInvoked = true;
                        return PlatformRuntimeInitializer.TryApplyRestoredEditStateConfiguration(
                            runtime,
                            out _);
                    });

                Assert.IsTrue(result.Success, result.Error);
                Assert.IsTrue(applyInvoked, "仅编辑态配置变化时必须尝试免重启生效。");
                Assert.IsFalse(result.RestartRequired);
                Assert.IsFalse(runtime.Readiness.VersionRestartRequired);
            }
        }

        [TestMethod]
        public void Restore_WhenRestartTierFileChanged_DoesNotAttemptInPlaceApply()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string cardPath = Path.Combine(directory.FullPath, "card.json");
                File.WriteAllText(
                    cardPath,
                    "{\"Revision\":1}",
                    new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot("原始版本", "测试", out string snapshotError),
                    snapshotError);
                string commitId = runtime.VersionService
                    .GetHistory(out _, out _)
                    .Single()
                    .CommitId;

                File.WriteAllText(
                    cardPath,
                    "{\"Revision\":2}",
                    new UTF8Encoding(false));
                bool applyInvoked = false;
                bool restartMarked = false;
                ConfigurationRestoreResult result = runtime.VersionService.Restore(
                    commitId,
                    () => true,
                    () =>
                    {
                        restartMarked = true;
                        runtime.Readiness.VersionRestartRequired = true;
                    },
                    () =>
                    {
                        applyInvoked = true;
                        return true;
                    });

                Assert.IsTrue(result.Success, result.Error);
                Assert.IsFalse(applyInvoked, "控制卡变更属于重启档，不得尝试免重启生效。");
                Assert.IsTrue(result.RestartRequired);
                Assert.IsTrue(restartMarked);
                Assert.IsTrue(runtime.Readiness.VersionRestartRequired);
            }
        }

        [TestMethod]
        [DataRow("DataStation.json", "[{\"Name\":\"已修改工站\"}]")]
        [DataRow("IOMap.json", "[[]]")]
        public void Restore_WhenMotionRuntimeConfigurationChanged_RequiresRestart(
            string fileName,
            string modifiedJson)
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string configurationPath = Path.Combine(directory.FullPath, fileName);
                File.WriteAllText(configurationPath, "[]", new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "运动运行时原始版本",
                        "测试",
                        out string snapshotError),
                    snapshotError);
                string commitId = runtime.VersionService
                    .GetHistory(out _, out _)
                    .Single()
                    .CommitId;

                File.WriteAllText(configurationPath, modifiedJson, new UTF8Encoding(false));
                bool applyInvoked = false;
                bool restartMarked = false;
                ConfigurationRestoreResult result = runtime.VersionService.Restore(
                    commitId,
                    () => true,
                    () =>
                    {
                        restartMarked = true;
                        runtime.Readiness.VersionRestartRequired = true;
                    },
                    () =>
                    {
                        applyInvoked = true;
                        return true;
                    });

                Assert.IsTrue(result.Success, result.Error);
                Assert.IsFalse(
                    applyInvoked,
                    fileName + " 承载运动设备运行时配置，不得尝试免重启生效。");
                Assert.IsTrue(result.RestartRequired);
                Assert.IsTrue(restartMarked);
                Assert.IsTrue(runtime.Readiness.VersionRestartRequired);
            }
        }

        [TestMethod]
        public void Restore_WhenOnlyIoDebugLayoutChanged_AppliesInPlaceWithoutRestart()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string ioDebugPath = Path.Combine(directory.FullPath, "IODebugMap.json");
                File.WriteAllText(ioDebugPath, "{}", new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot(
                        "IO调试布局原始版本",
                        "测试",
                        out string snapshotError),
                    snapshotError);
                string commitId = runtime.VersionService
                    .GetHistory(out _, out _)
                    .Single()
                    .CommitId;

                File.WriteAllText(
                    ioDebugPath,
                    "{\"inputs\":[]}",
                    new UTF8Encoding(false));
                bool applyInvoked = false;
                bool restartMarked = false;
                ConfigurationRestoreResult result = runtime.VersionService.Restore(
                    commitId,
                    () => true,
                    () =>
                    {
                        restartMarked = true;
                        runtime.Readiness.VersionRestartRequired = true;
                    },
                    () =>
                    {
                        applyInvoked = true;
                        return true;
                    });

                Assert.IsTrue(result.Success, result.Error);
                Assert.IsTrue(applyInvoked, "IODebugMap.json 仅承载调试布局，应允许免重启生效。");
                Assert.IsFalse(result.RestartRequired);
                Assert.IsFalse(restartMarked);
                Assert.IsFalse(runtime.Readiness.VersionRestartRequired);
            }
        }

        [TestMethod]
        public void Restore_WhenApplyFails_FallsBackToRestartGate()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                // 快照内携带无法解析的 IO 配置：还原后磁盘合法替换，但内存重载必须失败。
                string ioMapPath = Path.Combine(directory.FullPath, "IOMap.json");
                File.WriteAllText(
                    ioMapPath,
                    "{\"Name\":\"损坏的IO配置\"}",
                    new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);
                ComposeOfflineEngine(runtime);

                Assert.IsTrue(
                    runtime.Stores.Values.TrySetValue(
                        0,
                        "测试变量",
                        "double",
                        "0",
                        "测试用途",
                        "测试初始化"),
                    "测试准备失败：无法创建测试变量。");
                Assert.IsTrue(
                    runtime.VersionService.CreateManualSnapshot("原始版本", "测试", out string snapshotError),
                    snapshotError);
                string commitId = runtime.VersionService
                    .GetHistory(out _, out _)
                    .Single()
                    .CommitId;

                // 修改变量运行值制造仅“变量”分类的还原差异，触发免重启生效路径。
                Assert.IsTrue(
                    runtime.Stores.Values.setValueByName("测试变量", "5"),
                    "测试准备失败：无法修改变量运行值。");
                Assert.IsTrue(
                    runtime.Stores.Values.Save(runtime.Paths.ConfigPath),
                    "测试准备失败：无法保存变量运行值。");

                bool applyInvoked = false;
                ConfigurationRestoreResult result = runtime.VersionService.Restore(
                    commitId,
                    () => true,
                    () => runtime.Readiness.VersionRestartRequired = true,
                    () =>
                    {
                        applyInvoked = true;
                        return PlatformRuntimeInitializer.TryApplyRestoredEditStateConfiguration(
                            runtime,
                            out _);
                    });

                Assert.IsTrue(result.Success, result.Error);
                Assert.IsTrue(applyInvoked);
                Assert.IsTrue(result.RestartRequired);
                Assert.IsTrue(runtime.Readiness.VersionRestartRequired);
                StringAssert.Contains(result.Error, "免重启生效失败");
            }
        }

        private static void ComposeOfflineEngine(PlatformRuntime runtime)
        {
            PlatformRuntimeComposer.Compose(
                runtime,
                new StubInteractionPort(),
                new StubInteractionPort(),
                new StubLogger());
        }

        private sealed class StubInteractionPort : IAlarmHandler, IProcessPopupService
        {
            public Task<AlarmDecision> HandleAsync(AlarmContext context)
                => Task.FromResult(AlarmDecision.Ignore);

            public Task<AlarmDecision> ShowAsync(
                ProcessPopupRequest request,
                CancellationToken cancellationToken)
                => Task.FromResult(AlarmDecision.Ignore);
        }

        private sealed class StubLogger : ILogger
        {
            public void Log(string message, LogLevel level)
            {
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
                ConfigurationRestoreResult firstRestore = runtime.VersionService.Restore(
                    originalCommitId,
                    () => true,
                    () => runtime.Readiness.VersionRestartRequired = true,
                    () => PlatformRuntimeInitializer.TryApplyRestoredEditStateConfiguration(
                        runtime,
                        out _));
                Assert.IsTrue(firstRestore.Success, firstRestore.Error);
                // 裸 PlatformRuntime 未组合内核，免重启生效失败必须回退重启闸门。
                Assert.IsTrue(firstRestore.RestartRequired);
                ConfigurationVersionRecord firstResult =
                    runtime.VersionService.GetHistory(out _, out _)
                        .First();

                File.WriteAllText(
                    ioMapPath,
                    "{\"Revision\":3}",
                    new UTF8Encoding(false));
                ConfigurationRestoreResult secondRestore = runtime.VersionService.Restore(
                    firstResult.CommitId,
                    () => true,
                    () => runtime.Readiness.VersionRestartRequired = true,
                    () => PlatformRuntimeInitializer.TryApplyRestoredEditStateConfiguration(
                        runtime,
                        out _));
                Assert.IsTrue(secondRestore.Success, secondRestore.Error);
                Assert.IsTrue(secondRestore.RestartRequired);
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

        [TestMethod]
        public void AccountSecurityFile_IsExcludedFromProjectVersionSnapshots()
        {
            using (var directory = new TemporaryDirectory())
            {
                CreateHmiSource(directory.FullPath, "public class TestHmi { }\r\n");
                string accountPath = Path.Combine(directory.FullPath, "AccountSecurity.json");
                File.WriteAllText(accountPath, "{\"security\":\"before\"}", new UTF8Encoding(false));
                var runtime = new PlatformRuntime(directory.FullPath);

                Assert.IsTrue(runtime.VersionService.CreateManualSnapshot(
                    "账户文件排除验证", "测试", out string error), error);
                ConfigurationVersionRecord snapshot = runtime.VersionService
                    .GetHistory(out bool dirty, out error)
                    .Single();
                Assert.IsFalse(dirty);
                Assert.IsTrue(string.IsNullOrEmpty(error), error);

                File.WriteAllText(accountPath, "{\"security\":\"after\"}", new UTF8Encoding(false));
                runtime.VersionService.GetHistory(out dirty, out error);
                Assert.IsFalse(dirty, "账户安全文件变化不得使项目版本显示为脏状态。");
                Assert.IsTrue(string.IsNullOrEmpty(error), error);
                Assert.AreEqual(0, runtime.VersionService
                    .GetStructuredDiff(snapshot.CommitId, false, out error)
                    .Count);
                Assert.IsTrue(string.IsNullOrEmpty(error), error);
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
