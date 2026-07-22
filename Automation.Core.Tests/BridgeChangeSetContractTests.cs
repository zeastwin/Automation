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
