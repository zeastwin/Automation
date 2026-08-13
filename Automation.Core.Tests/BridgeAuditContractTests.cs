using System;
// 模块：核心测试 / Bridge 批量审计契约。
// 职责范围：验证批量审计的无损 finding 分页、300 条页上限和快照一致性。

using Automation.Bridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class BridgeAuditContractTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void AuditProcBatch_ReturnsAllDisabledFindingsAcrossStablePages()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    form.Runtime.Stores.Processes.Items.Clear();
                    form.Runtime.Stores.Processes.Items.Add(CreateDisabledOperationProcess(350));
                    var service = new AutomationBridgeService(form);

                    JObject first = ReadData(service.Handle(
                        "POST",
                        "/bridge/diagnostics/audit",
                        new JObject
                        {
                            ["procOffset"] = 0,
                            ["procLimit"] = 1,
                            ["findingOffset"] = 0,
                            ["findingLimit"] = 300
                        }.ToString(Formatting.None)));

                    Assert.AreEqual(350, first["findingCountInBatch"]?.Value<int>());
                    Assert.AreEqual(0, first["findingOffset"]?.Value<int>());
                    Assert.AreEqual(300, first["findingLimit"]?.Value<int>());
                    Assert.AreEqual(300, first["returnedFindingCount"]?.Value<int>());
                    Assert.IsTrue(first["hasMoreFindings"]?.Value<bool>() == true);
                    Assert.AreEqual(300, first["nextFindingOffset"]?.Value<int>());
                    Assert.IsNull(first["nextProcOffset"]?.Value<int?>());
                    Assert.AreEqual(350,
                        first["findingSummary"]?["byCode"]?["operation.disabled"]?.Value<int>());
                    Assert.AreEqual(0, first["findings"]?[0]?["opIndex"]?.Value<int>());
                    Assert.AreEqual(299, first["findings"]?[299]?["opIndex"]?.Value<int>());

                    string revision = first["indexRevision"]?.Value<string>();
                    JObject second = ReadData(service.Handle(
                        "POST",
                        "/bridge/diagnostics/audit",
                        new JObject
                        {
                            ["procOffset"] = 0,
                            ["procLimit"] = 1,
                            ["findingOffset"] = first["nextFindingOffset"],
                            ["findingLimit"] = 300,
                            ["expectedIndexRevision"] = revision
                        }.ToString(Formatting.None)));

                    Assert.AreEqual(revision, second["indexRevision"]?.Value<string>());
                    Assert.AreEqual(50, second["returnedFindingCount"]?.Value<int>());
                    Assert.IsFalse(second["hasMoreFindings"]?.Value<bool>() == true);
                    Assert.IsNull(second["nextFindingOffset"]?.Value<int?>());
                    Assert.AreEqual(300, second["findings"]?[0]?["opIndex"]?.Value<int>());
                    Assert.AreEqual(349, second["findings"]?[49]?["opIndex"]?.Value<int>());
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void AuditProcBatch_RejectsChangedRevisionAndLimitAbove300()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    form.Runtime.Stores.Processes.Items.Clear();
                    form.Runtime.Stores.Processes.Items.Add(CreateDisabledOperationProcess(1));
                    var service = new AutomationBridgeService(form);

                    AutomationBridgeResponse changed = service.Handle(
                        "POST",
                        "/bridge/diagnostics/audit",
                        new JObject
                        {
                            ["procOffset"] = 0,
                            ["procLimit"] = 1,
                            ["findingOffset"] = 1,
                            ["findingLimit"] = 300,
                            ["expectedIndexRevision"] = "outdated"
                        }.ToString(Formatting.None));
                    Assert.AreEqual(409, changed.StatusCode);
                    Assert.AreEqual("AUDIT_REVISION_CHANGED",
                        JObject.Parse(changed.Body)["errorCode"]?.Value<string>());

                    AutomationBridgeResponse tooLarge = service.Handle(
                        "POST",
                        "/bridge/diagnostics/audit",
                        new JObject { ["findingLimit"] = 301 }.ToString(Formatting.None));
                    Assert.AreEqual(400, tooLarge.StatusCode);
                    Assert.AreEqual("INVALID_ARGUMENT",
                        JObject.Parse(tooLarge.Body)["errorCode"]?.Value<string>());

                    AutomationBridgeResponse missingRevision = service.Handle(
                        "POST",
                        "/bridge/diagnostics/audit",
                        new JObject { ["findingOffset"] = 1 }.ToString(Formatting.None));
                    Assert.AreEqual(400, missingRevision.StatusCode);
                    Assert.AreEqual("AUDIT_REVISION_REQUIRED",
                        JObject.Parse(missingRevision.Body)["errorCode"]?.Value<string>());
                }
            }, TimeSpan.FromSeconds(20));
        }

        private static Proc CreateDisabledOperationProcess(int operationCount)
        {
            var process = new Proc
            {
                head = new ProcHead
                {
                    Id = Guid.NewGuid(),
                    Name = "批量审计分页"
                }
            };
            var step = new Step
            {
                Id = Guid.NewGuid(),
                Name = "禁用指令集合"
            };
            for (int i = 0; i < operationCount; i++)
            {
                step.Ops.Add(new Delay
                {
                    Id = Guid.NewGuid(),
                    Name = "禁用指令" + i,
                    Disable = true,
                    DelayMs = 0
                });
            }
            process.steps.Add(step);
            return process;
        }

        private static JObject ReadData(AutomationBridgeResponse response)
        {
            Assert.AreEqual(200, response.StatusCode, response.Body);
            JObject body = JObject.Parse(response.Body);
            Assert.IsTrue(body["ok"]?.Value<bool>() == true, response.Body);
            return (JObject)body["data"];
        }
    }
}
