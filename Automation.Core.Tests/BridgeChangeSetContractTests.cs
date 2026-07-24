using System;
// 模块：核心测试 / Bridge ChangeSet 契约。
// 职责范围：验证流程结构只有一条公开写入链，并覆盖预演、替换、确认、提交和版本冲突。

using System.IO;
using Automation.Bridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class BridgeChangeSetContractTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void LegacyProcessWriteRoutes_AreRetired()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    var service = new AutomationBridgeService(form);
                    foreach (string route in new[]
                    {
                        "/bridge/proc/create",
                        "/bridge/proc/delete",
                        "/bridge/proc/reorder",
                        "/bridge/proc/copy"
                    })
                    {
                        AutomationBridgeResponse response = service.Handle("POST", route, "{}");
                        Assert.AreEqual(404, response.StatusCode, route);
                        Assert.AreEqual("NOT_FOUND", JObject.Parse(response.Body)["errorCode"]?.Value<string>());
                    }
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void Preview_RejectsRetiredProcessPayload()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    var service = new AutomationBridgeService(form);
                    var request = new JObject
                    {
                        ["changeSet"] = new JObject
                        {
                            ["version"] = 2,
                            ["processes"] = new JArray()
                        }
                    };

                    AutomationBridgeResponse response = service.Handle(
                        "POST", "/bridge/change-set/preview", request.ToString(Formatting.None));

                    Assert.AreEqual(400, response.StatusCode);
                    Assert.AreEqual("CHANGE_SET_INVALID",
                        JObject.Parse(response.Body)["errorCode"]?.Value<string>());
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void PreviewReplacement_ConfirmAndApply_CommitsFrozenStage()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    var service = new AutomationBridgeService(form);
                    JObject first = PreviewProcess(service, "待替换流程");
                    string firstPreviewId = first["previewId"]?.Value<string>();
                    JObject replacement = PreviewProcess(service, "最终流程", firstPreviewId);
                    string replacementPreviewId = replacement["previewId"]?.Value<string>();

                    AutomationBridgeResponse replacedConfirmation = Confirm(service, firstPreviewId);
                    Assert.AreEqual(409, replacedConfirmation.StatusCode);
                    Assert.AreEqual("PREVIEW_REJECTED",
                        JObject.Parse(replacedConfirmation.Body)["errorCode"]?.Value<string>());

                    AutomationBridgeResponse confirmation = Confirm(service, replacementPreviewId);
                    Assert.AreEqual(200, confirmation.StatusCode);
                    AutomationBridgeResponse apply = Apply(service, replacementPreviewId);
                    JObject applied = ReadData(apply);

                    Assert.AreEqual(200, apply.StatusCode);
                    Assert.IsTrue(applied["configurationSaved"]?.Value<bool>() == true);
                    Assert.AreEqual("committed", applied["status"]?.Value<string>());
                    Assert.AreEqual("最终流程", form.Runtime.Stores.Processes.Items[0].head.Name);
                    Assert.IsTrue(File.Exists(Path.Combine(directory.FullPath, "Work", "0.json")));

                    AutomationBridgeResponse repeatedApply = Apply(service, replacementPreviewId);
                    Assert.AreEqual(404, repeatedApply.StatusCode,
                        "已提交的 previewId 必须失效，重复提交不得再次产生副作用。");
                }
            }, TimeSpan.FromSeconds(30));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void Apply_WhenBaseStateChanged_DoesNotWriteConfiguration()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    var service = new AutomationBridgeService(form);
                    JObject preview = PreviewProcess(service, "冲突流程");
                    string previewId = preview["previewId"]?.Value<string>();
                    Assert.AreEqual(200, Confirm(service, previewId).StatusCode);

                    form.Runtime.Stores.Processes.Items.Add(
                        TestProcessFactory.CreateEndingProcess("人工并发修改"));
                    AutomationBridgeResponse apply = Apply(service, previewId);
                    JObject error = JObject.Parse(apply.Body);

                    Assert.AreEqual(409, apply.StatusCode);
                    Assert.AreEqual("CHANGE_SET_VERSION_MISMATCH", error["errorCode"]?.Value<string>());
                    Assert.AreEqual("none", error["recovery"]?["sideEffects"]?.Value<string>());
                    Assert.IsFalse(Directory.Exists(Path.Combine(directory.FullPath, "Work")),
                        "版本冲突不得写入正式流程配置。");
                }
            }, TimeSpan.FromSeconds(30));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void Apply_WhenEditorSessionActive_RejectsAndKeepsConfirmedPreview()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    var service = new AutomationBridgeService(form);
                    string previewId =
                        PreviewProcess(service, "编辑会话门禁流程")["previewId"]?.Value<string>();
                    Assert.AreEqual(200, Confirm(service, previewId).StatusCode);

                    var draft = new EndProcess
                    {
                        Id = Guid.NewGuid(),
                        Name = "未保存草稿"
                    };
                    form.Runtime.Editor.ModifyKind = ModifyKind.Operation;
                    form.Runtime.Editor.Begin(new EditSession<OperationType>(
                        "修改指令",
                        draft,
                        null,
                        value => { }));
                    AutomationBridgeResponse rejected;
                    try
                    {
                        rejected = Apply(service, previewId);
                    }
                    finally
                    {
                        form.Runtime.Editor.Cancel();
                    }

                    JObject error = JObject.Parse(rejected.Body);
                    Assert.AreEqual(409, rejected.StatusCode);
                    Assert.AreEqual(
                        "EDITOR_SESSION_ACTIVE",
                        error["errorCode"]?.Value<string>());
                    Assert.AreEqual(
                        "none",
                        error["recovery"]?["sideEffects"]?.Value<string>());
                    Assert.IsFalse(
                        Directory.Exists(Path.Combine(directory.FullPath, "Work")),
                        "编辑会话冲突不得写入正式配置。");

                    AutomationBridgeResponse retried = Apply(service, previewId);
                    Assert.AreEqual(
                        200,
                        retried.StatusCode,
                        "仅因活动草稿被拒绝时，已确认预演应保留并允许取消草稿后重试。");
                    Assert.AreEqual(
                        "编辑会话门禁流程",
                        form.Runtime.Stores.Processes.Items[0].head.Name);
                }
            }, TimeSpan.FromSeconds(30));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void Confirm_AfterPreviewExpired_ReturnsNotFoundWithoutSideEffects()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    DateTime now = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);
                    var service = new AutomationBridgeService(
                        form, () => now, TimeSpan.FromMinutes(30));
                    JObject preview = PreviewProcess(service, "过期流程");
                    string previewId = preview["previewId"]?.Value<string>();

                    now = now.AddMinutes(31);
                    AutomationBridgeResponse confirmation = Confirm(service, previewId);

                    Assert.AreEqual(404, confirmation.StatusCode);
                    Assert.AreEqual("PREVIEW_NOT_FOUND",
                        JObject.Parse(confirmation.Body)["errorCode"]?.Value<string>());
                    Assert.AreEqual(0, form.Runtime.Stores.Processes.Items.Count);
                    Assert.IsFalse(Directory.Exists(Path.Combine(directory.FullPath, "Work")));
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void DiscardPreview_EndsStageWithoutWritingConfiguration()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    var service = new AutomationBridgeService(form);
                    string previewId = PreviewProcess(service, "丢弃流程")["previewId"]?.Value<string>();
                    AutomationBridgeResponse discard = service.Handle(
                        "POST", "/bridge/previews/reject",
                        new JObject { ["previewId"] = previewId }.ToString(Formatting.None));

                    Assert.AreEqual(200, discard.StatusCode);
                    Assert.IsTrue(ReadData(discard)["rejected"]?.Value<bool>() == true);
                    AutomationBridgeResponse apply = Apply(service, previewId);
                    Assert.AreEqual(409, apply.StatusCode);
                    Assert.AreEqual("PREVIEW_REJECTED",
                        JObject.Parse(apply.Body)["errorCode"]?.Value<string>());
                    Assert.IsFalse(Directory.Exists(Path.Combine(directory.FullPath, "Work")));
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void Preview_SameStageForwardOperationKey_ResolvesWithoutPlaceholder()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    var service = new AutomationBridgeService(form);
                    AutomationBridgeResponse preview = Preview(service, new JArray
                    {
                        CreateProcessAction("same_stage", "同阶段跳转"),
                        AppendStepAction("same_stage", "source_step", "判断"),
                        AppendStepAction("same_stage", "target_step", "结束"),
                        AppendOperationAction(
                            "same_stage",
                            "source_step",
                            new JObject
                            {
                                ["key"] = "jump",
                                ["kind"] = "flow.goto",
                                ["target"] = new JObject
                                {
                                    ["stepKey"] = "target_step",
                                    ["operationKey"] = "end"
                                }
                            }),
                        AppendOperationAction(
                            "same_stage",
                            "target_step",
                            new JObject
                            {
                                ["key"] = "end",
                                ["kind"] = "flow.end"
                            })
                    });
                    Assert.AreEqual(200, preview.StatusCode, preview.Body);
                    string previewId = ReadData(preview)["previewId"]?.Value<string>();

                    Assert.AreEqual(200, Confirm(service, previewId).StatusCode);
                    AutomationBridgeResponse apply = Apply(service, previewId);

                    Assert.AreEqual(200, apply.StatusCode, apply.Body);
                    var jump = form.Runtime.Stores.Processes.Items[0].steps[0].Ops[0] as Goto;
                    Assert.IsNotNull(jump);
                    Assert.AreEqual("0-1-0", jump.DefaultGoto);
                    Assert.IsFalse(jump.DefaultGoto.StartsWith(
                        ProcessDefinitionService.PendingGotoPrefix,
                        StringComparison.Ordinal));
                }
            }, TimeSpan.FromSeconds(30));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void Preview_ExistingOperationKey_IsRejectedWithoutSideEffects()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = CreateExistingKeyedTargetProcess();
                    form.Runtime.Stores.Processes.Items.Add(process);
                    var service = new AutomationBridgeService(form);

                    AutomationBridgeResponse preview = Preview(service, new JArray
                    {
                        AppendOperationAction(
                            process.head.Id.ToString("D"),
                            process.steps[0].Id.ToString("D"),
                            new JObject
                            {
                                ["key"] = "late_jump",
                                ["kind"] = "flow.goto",
                                ["target"] = new JObject
                                {
                                    ["stepId"] = process.steps[0].Id.ToString("D"),
                                    ["operationKey"] = "committed_end"
                                }
                            },
                            useStableIds: true)
                    });
                    JObject error = JObject.Parse(preview.Body);

                    Assert.AreEqual(400, preview.StatusCode, preview.Body);
                    Assert.AreEqual("CHANGE_SET_COMPILE_FAILED",
                        error["errorCode"]?.Value<string>());
                    StringAssert.Contains(
                        error["recovery"]?["validationError"]?.Value<string>() ?? string.Empty,
                        "已提交目标请改用 operationId");
                    Assert.AreEqual(1, form.Runtime.Stores.Processes.Items.Count);
                    Assert.AreEqual(1, form.Runtime.Stores.Processes.Items[0].steps[0].Ops.Count,
                        "失败的预演不得修改正式流程。");
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void Preview_ExistingOperationId_ResolvesAndApplies()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = CreateExistingKeyedTargetProcess();
                    form.Runtime.Stores.Processes.Items.Add(process);
                    var service = new AutomationBridgeService(form);
                    AutomationBridgeResponse preview = Preview(service, new JArray
                    {
                        AppendOperationAction(
                            process.head.Id.ToString("D"),
                            process.steps[0].Id.ToString("D"),
                            new JObject
                            {
                                ["key"] = "stable_jump",
                                ["kind"] = "flow.goto",
                                ["target"] = new JObject
                                {
                                    ["operationId"] =
                                        process.steps[0].Ops[0].Id.ToString("D")
                                }
                            },
                            useStableIds: true)
                    });
                    Assert.AreEqual(200, preview.StatusCode, preview.Body);
                    string previewId = ReadData(preview)["previewId"]?.Value<string>();

                    Assert.AreEqual(200, Confirm(service, previewId).StatusCode);
                    AutomationBridgeResponse apply = Apply(service, previewId);

                    Assert.AreEqual(200, apply.StatusCode, apply.Body);
                    var jump = form.Runtime.Stores.Processes.Items[0].steps[0].Ops[1] as Goto;
                    Assert.IsNotNull(jump);
                    Assert.AreEqual("0-0-0", jump.DefaultGoto);
                }
            }, TimeSpan.FromSeconds(30));
        }

        private static JObject PreviewProcess(
            AutomationBridgeService service,
            string processName,
            string replacePreviewId = null)
        {
            var request = new JObject
            {
                ["changeSet"] = new JObject
                {
                    ["version"] = 2,
                    ["title"] = processName,
                    ["actions"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "process.create",
                            ["process"] = new JObject
                            {
                                ["key"] = "process",
                                ["name"] = processName
                            }
                        }
                    }
                }
            };
            if (!string.IsNullOrWhiteSpace(replacePreviewId))
            {
                request["replacePreviewId"] = replacePreviewId;
            }
            AutomationBridgeResponse response = service.Handle(
                "POST", "/bridge/change-set/preview", request.ToString(Formatting.None));
            Assert.AreEqual(200, response.StatusCode, response.Body);
            return ReadData(response);
        }

        private static AutomationBridgeResponse Preview(
            AutomationBridgeService service,
            JArray actions)
        {
            return service.Handle(
                "POST",
                "/bridge/change-set/preview",
                new JObject
                {
                    ["changeSet"] = new JObject
                    {
                        ["version"] = 2,
                        ["title"] = "跳转目标契约测试",
                        ["actions"] = actions
                    }
                }.ToString(Formatting.None));
        }

        private static JObject CreateProcessAction(string processKey, string name)
        {
            return new JObject
            {
                ["type"] = "process.create",
                ["process"] = new JObject
                {
                    ["key"] = processKey,
                    ["name"] = name
                }
            };
        }

        private static JObject AppendStepAction(
            string processKey,
            string stepKey,
            string name)
        {
            return new JObject
            {
                ["type"] = "step.append",
                ["targetProcess"] = new JObject { ["key"] = processKey },
                ["step"] = new JObject
                {
                    ["key"] = stepKey,
                    ["name"] = name
                }
            };
        }

        private static JObject AppendOperationAction(
            string processSelector,
            string stepSelector,
            JObject operation,
            bool useStableIds = false)
        {
            return new JObject
            {
                ["type"] = "operation.append",
                ["targetProcess"] = new JObject
                {
                    [useStableIds ? "procId" : "key"] = processSelector
                },
                ["targetStep"] = new JObject
                {
                    [useStableIds ? "stepId" : "key"] = stepSelector
                },
                ["operation"] = operation
            };
        }

        private static Proc CreateExistingKeyedTargetProcess()
        {
            Proc process = TestProcessFactory.CreateEndingProcess("已提交流程");
            process.steps[0].AiKey = "committed_step";
            process.steps[0].Ops[0].AiKey = "committed_end";
            return process;
        }

        private static AutomationBridgeResponse Confirm(
            AutomationBridgeService service,
            string previewId)
        {
            return service.Handle("POST", "/bridge/previews/confirm",
                new JObject { ["previewId"] = previewId }.ToString(Formatting.None));
        }

        private static AutomationBridgeResponse Apply(
            AutomationBridgeService service,
            string previewId)
        {
            return service.Handle("POST", "/bridge/change-set/apply",
                new JObject { ["previewId"] = previewId }.ToString(Formatting.None));
        }

        private static JObject ReadData(AutomationBridgeResponse response)
        {
            return JObject.Parse(response.Body)["data"] as JObject
                ?? throw new AssertFailedException("Bridge 成功响应缺少 data 对象：" + response.Body);
        }
    }
}
