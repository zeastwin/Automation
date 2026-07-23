// 模块：核心测试 / 变量调试。
// 职责范围：验证稳定身份、安全闸门、配置提交和运行值刷新。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class VariableDebugServiceTests
    {
        private sealed class LegacyValueDebugConfiguration
        {
            public List<int> CheckIndexes { get; set; } = new List<int>();
            public List<int> EditIndexes { get; set; } = new List<int>();
            public Dictionary<int, string> Notes { get; set; } =
                new Dictionary<int, string>();
        }

        [TestMethod]
        public void Load_LegacyIndexes_MigratesToStableVariableIds()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                    12,
                    "迁移变量",
                    "double",
                    "0",
                    string.Empty));
                Guid variableId =
                    runtime.Stores.Values.GetValueByIndex(12).Id;
                Assert.IsTrue(AtomicJsonFileStore.Save(
                    directory.FullPath,
                    "value_debug",
                    new LegacyValueDebugConfiguration
                    {
                        CheckIndexes = new List<int> { 12 },
                        Notes = new Dictionary<int, string>
                        {
                            [12] = "迁移备注"
                        }
                    }));

                Assert.IsTrue(runtime.Stores.ValueDebug.Load(
                    directory.FullPath,
                    runtime.Stores.Values,
                    out string error),
                    error);
                ValueDebugConfiguration configuration =
                    runtime.Stores.ValueDebug.GetSnapshot(out _);

                CollectionAssert.AreEqual(
                    new[] { variableId },
                    configuration.CheckVariableIds);
                Assert.AreEqual("迁移备注", configuration.Notes[variableId]);
                JObject persisted = JObject.Parse(File.ReadAllText(
                    Path.Combine(directory.FullPath, "value_debug.json")));
                Assert.IsNotNull(persisted["CheckVariableIds"]);
                Assert.IsNull(persisted["CheckIndexes"]);
            }
        }

        [TestMethod]
        public void TryCommit_WhenSaveFails_KeepsPublishedConfigurationAndVersion()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                    1, "变量一", "double", "0", string.Empty));
                Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                    2, "变量二", "double", "0", string.Empty));
                Guid firstId = runtime.Stores.Values.GetValueByIndex(1).Id;
                Guid secondId = runtime.Stores.Values.GetValueByIndex(2).Id;
                var initial = new ValueDebugConfiguration
                {
                    CheckVariableIds = new List<Guid> { firstId }
                };
                Assert.IsTrue(runtime.Stores.ValueDebug.TryCommit(
                    directory.FullPath,
                    initial,
                    runtime.Stores.ValueDebug.Version,
                    out string initialError),
                    initialError);
                ValueDebugConfiguration before =
                    runtime.Stores.ValueDebug.GetSnapshot(out long versionBefore);

                string invalidDirectory = Path.Combine(
                    directory.FullPath,
                    "not-a-directory");
                File.WriteAllText(invalidDirectory, "占位文件");
                var candidate = new ValueDebugConfiguration
                {
                    CheckVariableIds = new List<Guid> { firstId, secondId }
                };

                Assert.IsFalse(runtime.Stores.ValueDebug.TryCommit(
                    invalidDirectory,
                    candidate,
                    versionBefore,
                    out string error));
                StringAssert.Contains(error, "保存失败");
                ValueDebugConfiguration after =
                    runtime.Stores.ValueDebug.GetSnapshot(out long versionAfter);
                Assert.AreEqual(versionBefore, versionAfter);
                CollectionAssert.AreEqual(
                    before.CheckVariableIds,
                    after.CheckVariableIds);
            }
        }

        [TestMethod]
        public void TryApplyValue_WhenVariableMoves_WritesStableVariableOnly()
        {
            var runtime = new PlatformRuntime();
            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();
            runtime.Stores.Values.ReplaceConfiguration(
                CreateSwappedConfiguration(firstId, secondId));

            Assert.IsTrue(runtime.VariableDebug.TryApplyValue(
                firstId,
                "7",
                "稳定身份测试",
                out string error),
                error);
            Assert.IsTrue(runtime.Stores.Values.TryGetValueById(
                firstId,
                out DicValue first));
            Assert.IsTrue(runtime.Stores.Values.TryGetValueById(
                secondId,
                out DicValue second));
            Assert.AreEqual(11, first.Index);
            Assert.AreEqual("7", first.Value);
            Assert.AreEqual(10, second.Index);
            Assert.AreEqual("2", second.Value);
        }

        [TestMethod]
        public void TryApplyValue_WhenSafetyLocked_RejectsWithoutSideEffects()
        {
            var runtime = new PlatformRuntime();
            Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                8, "安全变量", "double", "1", string.Empty));
            Guid variableId = runtime.Stores.Values.GetValueByIndex(8).Id;
            runtime.Safety.Lock("测试安全锁");

            Assert.IsFalse(runtime.VariableDebug.TryApplyValue(
                variableId,
                "9",
                "安全测试",
                out string error));
            StringAssert.Contains(error, "安全锁定");
            Assert.AreEqual(
                "1",
                runtime.Stores.Values.GetValueByIndex(8).Value);
        }

        [TestMethod]
        public void TryApplyValue_WhenConfigurationMaintenanceActive_RejectsWithoutSideEffects()
        {
            var runtime = new PlatformRuntime();
            Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                18, "维护变量", "double", "1", string.Empty));
            Guid variableId = runtime.Stores.Values.GetValueByIndex(18).Id;
            Assert.IsTrue(runtime.Maintenance.TryBegin(
                "测试配置维护",
                out IDisposable maintenanceLease,
                out string beginError),
                beginError);
            using (maintenanceLease)
            {
                Assert.IsFalse(runtime.VariableDebug.TryApplyValue(
                    variableId,
                    "9",
                    "维护测试",
                    out string error));
                StringAssert.Contains(error, "配置维护");
            }
            Assert.AreEqual(
                "1",
                runtime.Stores.Values.GetValueByIndex(18).Value);
        }

        [TestMethod]
        public void TryCommitConfiguration_WhenRestartRequired_DoesNotOverwriteConfiguration()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                    19, "版本变量", "double", "1", string.Empty));
                Guid variableId =
                    runtime.Stores.Values.GetValueByIndex(19).Id;
                runtime.Readiness.VersionRestartRequired = true;

                Assert.IsFalse(runtime.VariableDebug.TryCommitConfiguration(
                    new ValueDebugConfiguration
                    {
                        CheckVariableIds = new List<Guid> { variableId }
                    },
                    runtime.Stores.ValueDebug.Version,
                    out _,
                    out _,
                    out string error));
                StringAssert.Contains(error, "必须重启");
                ValueDebugConfiguration current =
                    runtime.Stores.ValueDebug.GetSnapshot(out long version);
                Assert.AreEqual(0, version);
                Assert.AreEqual(0, current.CheckVariableIds.Count);
                Assert.IsFalse(File.Exists(Path.Combine(
                    directory.FullPath,
                    "value_debug.json")));
            }
        }

        [TestMethod]
        public void TryApplyValue_UsesStoreNumberValidationContract()
        {
            var runtime = new PlatformRuntime();
            Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                9, "数值变量", "double", "1", string.Empty));
            Guid variableId = runtime.Stores.Values.GetValueByIndex(9).Id;

            Assert.IsTrue(runtime.VariableDebug.TryApplyValue(
                variableId,
                "1E3",
                "数值格式测试",
                out string validError),
                validError);
            Assert.IsFalse(runtime.VariableDebug.TryApplyValue(
                variableId,
                ".",
                "数值格式测试",
                out string invalidError));
            StringAssert.Contains(invalidError, "不是有效数字");
            Assert.AreEqual(
                "1E3",
                runtime.Stores.Values.GetValueByIndex(9).Value);
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshRuntimeValues_UpdatesVisibleCellsAndKeepsDeletedIdentity()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Assert.IsTrue(main.Runtime.Stores.Values.TrySetValue(
                        15,
                        "原变量",
                        "double",
                        "1",
                        string.Empty));
                    Guid variableId =
                        main.Runtime.Stores.Values.GetValueByIndex(15).Id;
                    Assert.IsTrue(main.Runtime.Stores.ValueDebug.TryCommit(
                        main.Runtime.Paths.ConfigPath,
                        new ValueDebugConfiguration
                        {
                            EditVariableIds = new List<Guid> { variableId }
                        },
                        main.Runtime.Stores.ValueDebug.Version,
                        out string debugError),
                        debugError);

                    FrmValueDebug form = main.frmValueDebug;
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-10000, -10000);
                    form.Show();
                    form.RefreshAllLists();
                    DataGridView grid = ReadPrivateField<DataGridView>(
                        form,
                        "dgvEdit");
                    Assert.AreEqual("1", grid.Rows[0].Cells[4].Value);

                    Assert.IsTrue(main.Runtime.Stores.Values.setValueByIndex(
                        15,
                        "5",
                        "运行值刷新测试"));
                    InvokePrivateMethod(form, "RefreshDisplayedRuntimeValues");
                    Assert.AreEqual("5", grid.Rows[0].Cells[4].Value);

                    main.Runtime.Stores.Values.ReplaceConfiguration(
                        new Dictionary<string, DicValue>(StringComparer.Ordinal)
                        {
                            ["新变量"] = new DicValue
                            {
                                Id = Guid.NewGuid(),
                                Index = 15,
                                Name = "新变量",
                                Type = "double",
                                Value = "9",
                                Scope = VariableScopeContract.Public
                            }
                        });
                    form.RefreshAllLists();
                    StringAssert.Contains(
                        grid.Rows[0].Cells[2].Value?.ToString(),
                        "已删除变量");
                    Assert.IsTrue(grid.Rows[0].Cells[4].ReadOnly);
                    Assert.AreEqual(
                        "9",
                        main.Runtime.Stores.Values.GetValueByIndex(15).Value);
                }
            }, TimeSpan.FromSeconds(20));
        }

        private static Dictionary<string, DicValue> CreateSwappedConfiguration(
            Guid firstId,
            Guid secondId)
        {
            return new Dictionary<string, DicValue>(StringComparer.Ordinal)
            {
                ["变量甲"] = new DicValue
                {
                    Id = firstId,
                    Index = 11,
                    Name = "变量甲",
                    Type = "double",
                    Value = "1",
                    Scope = VariableScopeContract.Public
                },
                ["变量乙"] = new DicValue
                {
                    Id = secondId,
                    Index = 10,
                    Name = "变量乙",
                    Type = "double",
                    Value = "2",
                    Scope = VariableScopeContract.Public
                }
            };
        }

        private static T ReadPrivateField<T>(object target, string fieldName)
            where T : class
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"未找到字段：{fieldName}");
            return field.GetValue(target) as T
                ?? throw new InvalidOperationException(
                    $"字段类型不正确：{fieldName}");
        }

        private static void InvokePrivateMethod(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"未找到方法：{methodName}");
            method.Invoke(target, null);
        }
    }
}
