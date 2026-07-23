// 模块：核心测试 / UI 缓存。
// 职责范围：验证 UI 投影缓存依赖的配置版本只随结构配置变化。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
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

        [TestMethod]
        [TestCategory("Desktop")]
        public void ValueDebugRefresh_ReusesRowsAndAppliesNewConfiguration()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    var variables = new Dictionary<string, DicValue>
                    {
                        ["变量一"] = new DicValue
                        {
                            Id = Guid.NewGuid(),
                            Index = 1,
                            Name = "变量一",
                            Type = "double",
                            Value = "0",
                            Scope = VariableScopeContract.Public
                        },
                        ["变量二"] = new DicValue
                        {
                            Id = Guid.NewGuid(),
                            Index = 2,
                            Name = "变量二",
                            Type = "double",
                            Value = "0",
                            Scope = VariableScopeContract.Public
                        }
                    };
                    Assert.IsTrue(
                        main.Runtime.Stores.Values.TryCommitConfiguration(
                            main.Runtime.Paths.ConfigPath,
                            variables,
                            out string variableError),
                        variableError);
                    Assert.IsTrue(
                        main.Runtime.Stores.ValueDebug.TryCommit(
                            main.Runtime.Paths.ConfigPath,
                            new ValueDebugConfiguration
                            {
                                CheckIndexes = new List<int> { 1 }
                            },
                            main.Runtime.Stores.Values,
                            out string debugError),
                        debugError);

                    FrmValueDebug form = main.frmValueDebug;
                    form.RefreshAllLists();
                    DataGridView grid = GetPrivateField<DataGridView>(
                        form,
                        "dgvCheck");
                    DataGridViewRow retainedRow = grid.Rows[0];

                    Assert.IsTrue(
                        main.Runtime.Stores.ValueDebug.TryCommit(
                            main.Runtime.Paths.ConfigPath,
                            new ValueDebugConfiguration
                            {
                                CheckIndexes = new List<int> { 1, 2 }
                            },
                            main.Runtime.Stores.Values,
                            out debugError),
                        debugError);
                    form.RefreshAllLists();

                    Assert.AreSame(
                        retainedRow,
                        grid.Rows[0],
                        "调试配置增加变量时应保留原有变量行。");
                    Assert.AreEqual(2, grid.Rows.Count);
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void WarmupCoordinator_ReplacesStaleKeyAndRunsOneItemPerTick()
        {
            StaTestRunner.Run(() =>
            {
                using (var dispatcher = new Control())
                using (var coordinator =
                    new UiWarmupCoordinator(dispatcher))
                {
                    int value = 0;
                    coordinator.Schedule("same", 20, () => value = 1);
                    coordinator.Schedule("same", 20, () => value = 2);
                    coordinator.Schedule("later", 30, () => value = 3);

                    InvokeWarmupTick(coordinator);
                    Assert.AreEqual(
                        2,
                        value,
                        "同一键只应执行最后一次预热请求。");

                    InvokeWarmupTick(coordinator);
                    Assert.AreEqual(
                        3,
                        value,
                        "每个 UI tick 只启动一个后台预热入口。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void IoAssociationCandidates_ReuseMatchingItemsAndCheckedState()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmIODebug())
                {
                    ListView list = GetPrivateField<ListView>(
                        form,
                        "listView4");
                    var first = new ListViewItem("输入一");
                    var retained = new ListViewItem("输入二")
                    {
                        Checked = true
                    };
                    list.Items.Add(first);
                    list.Items.Add(retained);

                    MethodInfo reconcile = typeof(FrmIODebug).GetMethod(
                        "ReconcileIoNameItems",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(
                            "未找到 IO 关联候选列表差量刷新入口。");
                    reconcile.Invoke(
                        form,
                        new object[]
                        {
                            list,
                            new[] { "输入二", "输入三" }
                        });

                    Assert.AreSame(retained, list.Items[0]);
                    Assert.IsTrue(
                        retained.Checked,
                        "候选 IO 列表刷新后应保留既有勾选状态。");
                    Assert.AreEqual("输入三", list.Items[1].Text);
                }
            }, TimeSpan.FromSeconds(10));
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

        private static T GetPrivateField<T>(
            object owner,
            string fieldName)
            where T : class
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"未找到字段：{fieldName}");
            return field.GetValue(owner) as T
                ?? throw new InvalidOperationException(
                    $"字段类型不正确：{fieldName}");
        }

        private static void InvokeWarmupTick(
            UiWarmupCoordinator coordinator)
        {
            MethodInfo tick = typeof(UiWarmupCoordinator).GetMethod(
                "Timer_Tick",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "未找到空闲预热调度器 tick。");
            tick.Invoke(
                coordinator,
                new object[] { coordinator, EventArgs.Empty });
        }
    }
}
