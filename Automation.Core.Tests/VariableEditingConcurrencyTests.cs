using System;
// 模块：核心测试 / 变量与指令编辑并发。
// 职责范围：验证变量配置热发布的一致视图、调试写入身份保护和 Inspector 过期提交拒绝。

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class VariableEditingConcurrencyTests
    {
        [TestMethod]
        public void VariableConfigurationPublish_ReadersNeverObserveMixedTableAndNameIndex()
        {
            var runtime = new PlatformRuntime();
            ValueConfigStore store = runtime.Stores.Values;
            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();
            Dictionary<string, DicValue> first = CreateSwappedConfiguration(
                firstId, secondId, false);
            Dictionary<string, DicValue> second = CreateSwappedConfiguration(
                firstId, secondId, true);
            store.ReplaceConfiguration(first);

            const int iterations = 3000;
            var errors = new ConcurrentQueue<string>();
            int writerFinished = 0;
            Task writer = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    store.ReplaceConfiguration((i & 1) == 0 ? second : first);
                }
                Volatile.Write(ref writerFinished, 1);
            });
            Task reader = Task.Run(() =>
            {
                int readsAfterWriter = 0;
                while (Volatile.Read(ref writerFinished) == 0
                    || readsAfterWriter++ < 100)
                {
                    if (!store.TryGetValueByName("变量甲", out DicValue value)
                        || value.Id != firstId
                        || !string.Equals(value.Name, "变量甲", StringComparison.Ordinal))
                    {
                        errors.Enqueue("变量数组与名称索引不是同一版快照。");
                        return;
                    }
                }
            });

            Assert.IsTrue(
                Task.WaitAll(new[] { writer, reader }, TimeSpan.FromSeconds(10)),
                "变量配置并发发布未在限定时间内完成。");
            Assert.AreEqual(
                0,
                errors.Count,
                string.Join(Environment.NewLine, errors.ToArray()));
        }

        [TestMethod]
        public void VariableDebugWrite_WhenSlotWasReplaced_RejectsExpectedInstance()
        {
            var runtime = new PlatformRuntime();
            ValueConfigStore store = runtime.Stores.Values;
            Assert.IsTrue(store.TrySetValue(
                15, "原变量", "double", "1", string.Empty));
            DicValue expected = store.GetValueByIndex(15);

            var replacement = new Dictionary<string, DicValue>(StringComparer.Ordinal)
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
            };
            store.ReplaceConfiguration(replacement);

            Assert.IsFalse(store.TryModifyValueByIndex(
                15,
                expected,
                _ => "2",
                out string error,
                "变量调试测试"));
            StringAssert.Contains(error, "预绑定变量已失效");
            Assert.AreEqual("9", store.GetValueByIndex(15).Value);
        }

        [TestMethod]
        public void VariableConfigurationPublish_WhenRuntimeContractIsUnchanged_KeepsLiveInstance()
        {
            var runtime = new PlatformRuntime();
            ValueConfigStore store = runtime.Stores.Values;
            Assert.IsTrue(store.TrySetValue(
                16, "在线保留变量", "double", "1", "旧备注"));
            DicValue expected = store.GetValueByIndex(16);
            Dictionary<string, DicValue> candidate = store.BuildSaveData();
            candidate["在线保留变量"].Note = "新备注";

            store.ReplaceConfiguration(candidate);

            DicValue current = store.GetValueByIndex(16);
            Assert.AreSame(expected, current);
            Assert.AreEqual("新备注", current.Note);
            Assert.IsTrue(store.TryModifyValueByIndex(
                16,
                expected,
                _ => "2",
                out string error,
                "在线保留测试"),
                error);
            Assert.AreEqual("2", current.Value);
        }

        [TestMethod]
        public void VariableConfigurationHistory_RuntimeValueChangesDoNotMutateUndoSnapshots()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                var initial = new Dictionary<string, DicValue>(StringComparer.Ordinal)
                {
                    ["历史变量"] = new DicValue
                    {
                        Id = Guid.NewGuid(),
                        Index = 17,
                        Name = "历史变量",
                        Type = "double",
                        Value = "1",
                        Note = "旧备注",
                        Scope = VariableScopeContract.Public
                    }
                };
                Assert.IsTrue(runtime.Stores.Values.TryCommitConfiguration(
                    runtime.Paths.ConfigPath,
                    initial,
                    out string initialError),
                    initialError);

                Dictionary<string, DicValue> edited =
                    runtime.Stores.Values.BuildSaveData();
                edited["历史变量"].Note = "新备注";
                Assert.IsTrue(runtime.Stores.Values.TryCommitConfiguration(
                    runtime.Paths.ConfigPath,
                    edited,
                    out string editError,
                    historyDescription: "修改变量备注"),
                    editError);
                Assert.IsTrue(runtime.Stores.Values.setValueByName(
                    "历史变量",
                    "9",
                    "历史快照测试"));

                Assert.IsTrue(runtime.Editor.History.TryUndo(
                    out string undoDescription,
                    out string undoError),
                    undoError);
                Assert.AreEqual("修改变量备注", undoDescription);
                Assert.AreEqual(
                    "旧备注",
                    runtime.Stores.Values.GetValueByName("历史变量").Note);
                Assert.AreEqual(
                    "9",
                    runtime.Stores.Values.GetValueByName("历史变量").Value,
                    "撤销配置不能把提交后的运行值回退。");

                Assert.IsTrue(runtime.Editor.History.TryRedo(
                    out string redoDescription,
                    out string redoError),
                    redoError);
                Assert.AreEqual("修改变量备注", redoDescription);
                Assert.AreEqual(
                    "新备注",
                    runtime.Stores.Values.GetValueByName("历史变量").Note);
                Assert.AreEqual(
                    "9",
                    runtime.Stores.Values.GetValueByName("历史变量").Value);
            }
        }

        [TestMethod]
        public void OperationEditCommit_WhenProcessRevisionChanged_RejectsStaleTarget()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = TestProcessFactory.CreateEndingProcess("并发编辑流程");
                var runtime = new PlatformRuntime(directory.FullPath);
                runtime.Stores.Processes.ReplaceAll(new[] { process });
                using (var engine = CreateEngine(runtime, process))
                {
                    runtime.ProcessEngine = engine;
                    Assert.IsTrue(runtime.OperationEditing.TryCreateCommitTarget(
                        0,
                        0,
                        0,
                        false,
                        out OperationEditCommitTarget target,
                        out string targetError),
                        targetError);

                    Proc replacement = ObjectGraphCloner.Clone(process);
                    replacement.head.Name = "外部入口已修改";
                    runtime.Stores.Processes.ReplaceAt(0, replacement);

                    Assert.IsFalse(runtime.OperationEditing.TrySave(
                        target,
                        ObjectGraphCloner.Clone(process.steps[0].Ops[0]),
                        out _,
                        out string saveError));
                    StringAssert.Contains(saveError, "已被其他操作修改");
                }
            }
        }

        [TestMethod]
        public void AiChangeSetCommit_WhenEditorSessionActive_RejectsWithoutSideEffects()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                Proc process = TestProcessFactory.CreateEndingProcess("正式流程");
                runtime.Stores.Processes.ReplaceAll(new[] { process });
                long processVersion = runtime.Stores.Processes.Version;
                Proc candidate = ObjectGraphCloner.Clone(process);
                candidate.head.Name = "AI候选流程";
                var draft = ObjectGraphCloner.Clone(process.steps[0].Ops[0]);
                runtime.Editor.Begin(new EditSession<OperationType>(
                    "修改指令",
                    draft,
                    null,
                    value => { }));
                try
                {
                    ProcessVariableConfigurationCommitResult result =
                        runtime.ProcessVariableConfiguration.CommitChangeSet(
                            new[] { candidate },
                            runtime.Stores.Values.BuildSaveData(),
                            null);

                    Assert.IsFalse(result.Succeeded);
                    StringAssert.Contains(result.Message, "未完成的编辑会话");
                    Assert.AreEqual(
                        processVersion,
                        runtime.Stores.Processes.Version,
                        "拒绝提交不能修改流程仓库版本。");
                    Assert.AreEqual(
                        "正式流程",
                        runtime.Stores.Processes.Items[0].head.Name);
                    Assert.IsFalse(
                        Directory.Exists(runtime.Paths.WorkPath),
                        "活动草稿门禁必须在配置事务写盘前生效。");
                    Assert.AreSame(
                        draft,
                        runtime.Editor.ActiveSession?.Draft,
                        "拒绝 AI 提交后必须保留原编辑草稿。");
                }
                finally
                {
                    runtime.Editor.Cancel();
                }
            }
        }

        [TestMethod]
        public void VariableConfigurationGate_WhileProcessRuns_RemainsAvailable()
        {
            using (var directory = new TemporaryDirectory())
            {
                Proc process = TestProcessFactory.CreateEndingProcess(
                    "变量在线调试流程",
                    2000);
                var runtime = new PlatformRuntime(directory.FullPath);
                Assert.IsTrue(runtime.Stores.Values.TrySetValue(
                    20,
                    "在线变量",
                    "double",
                    "1",
                    string.Empty));
                Proc editorProcess = ObjectGraphCloner.Clone(process);
                editorProcess.steps[0].Ops.Insert(0, new Goto
                {
                    Id = Guid.NewGuid(),
                    ValueName = "在线变量"
                });
                runtime.Stores.Processes.ReplaceAll(new[] { editorProcess });
                using (var engine = CreateEngine(runtime, process))
                {
                    runtime.ProcessEngine = engine;
                    Assert.IsTrue(engine.StartProc(process, 0));
                    WaitForState(engine, ProcRunState.Running, TimeSpan.FromSeconds(3));

                    Assert.IsFalse(runtime.ProcessEditing.CanEditStructure());
                    Assert.IsTrue(runtime.ProcessEditing.CanEditVariableConfiguration());
                    Dictionary<string, DicValue> current =
                        runtime.Stores.Values.BuildSaveData();
                    Dictionary<string, DicValue> added =
                        current.ToDictionary(
                            item => item.Key,
                            item => ObjectGraphCloner.Clone(item.Value),
                            StringComparer.Ordinal);
                    added["新增变量"] = new DicValue
                    {
                        Id = Guid.NewGuid(),
                        Index = 21,
                        Name = "新增变量",
                        Type = "double",
                        Value = "0",
                        Scope = VariableScopeContract.Public
                    };
                    Assert.IsTrue(
                        runtime.ProcessEditing.CanApplyVariableConfiguration(
                            current.Values,
                            added.Values,
                            out string addError),
                        addError);

                    Dictionary<string, DicValue> renamed =
                        current.ToDictionary(
                            item => item.Key,
                            item => ObjectGraphCloner.Clone(item.Value),
                            StringComparer.Ordinal);
                    DicValue renamedVariable = renamed["在线变量"];
                    renamed.Remove("在线变量");
                    renamedVariable.Name = "在线变量新名";
                    renamed[renamedVariable.Name] = renamedVariable;
                    Assert.IsFalse(
                        runtime.ProcessEditing.CanApplyVariableConfiguration(
                            current.Values,
                            renamed.Values,
                            out string renameError));
                    StringAssert.Contains(renameError, "只停止受影响流程");

                    renamed["新增变量"] = added["新增变量"];
                    Assert.IsFalse(
                        runtime.ProcessEditing.CanApplyVariableConfiguration(
                            current.Values,
                            renamed.Values,
                            out _),
                        "新增变量不能掩盖同一候选中的危险重命名。");

                    engine.Stop(0);
                    WaitForState(engine, ProcRunState.Stopped, TimeSpan.FromSeconds(3));
                }
            }
        }

        private static Dictionary<string, DicValue> CreateSwappedConfiguration(
            Guid firstId,
            Guid secondId,
            bool swapped)
        {
            return new Dictionary<string, DicValue>(StringComparer.Ordinal)
            {
                ["变量甲"] = new DicValue
                {
                    Id = firstId,
                    Index = swapped ? 11 : 10,
                    Name = "变量甲",
                    Type = "double",
                    Value = "1",
                    Scope = VariableScopeContract.Public
                },
                ["变量乙"] = new DicValue
                {
                    Id = secondId,
                    Index = swapped ? 10 : 11,
                    Name = "变量乙",
                    Type = "double",
                    Value = "2",
                    Scope = VariableScopeContract.Public
                }
            };
        }

        private static ProcessEngine CreateEngine(
            PlatformRuntime runtime,
            Proc process)
        {
            return new ProcessEngine(new EngineContext
            {
                Procs = new List<Proc> { process },
                ValueStore = runtime.Stores.Values,
                Maintenance = runtime.Maintenance,
                Safety = runtime.Safety,
                Readiness = runtime.Readiness,
                Paths = runtime.Paths
            });
        }

        private static void WaitForState(
            ProcessEngine engine,
            ProcRunState expected,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (engine.GetSnapshot(0)?.State == expected)
                {
                    return;
                }
                Thread.Sleep(20);
            }
            Assert.Fail(
                $"等待流程状态超时：{expected}；当前状态：{engine.GetSnapshot(0)?.State}。");
        }
    }
}
