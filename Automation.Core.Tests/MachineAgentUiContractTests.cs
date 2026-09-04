using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

// 模块：核心测试 / Machine Agent 前台。
// 职责范围：验证一级工作台、预演控制边界、独立智能交互和拓扑子模块入口没有退化。

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class MachineAgentUiContractTests
    {
        [TestMethod]
        public void Dashboard_UsesAutomationPaletteAndKeepsPreviewControlBoundary()
        {
            Assembly assembly = typeof(FrmMachineAgent).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(
                "Automation.Assets.MachineAgent.index.html"))
            {
                Assert.IsNotNull(stream, "Machine Agent WebView 页面必须作为程序集资源部署。");
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string html = reader.ReadToEnd();
                    StringAssert.Contains(html, "--bg: #f6f8fb");
                    StringAssert.Contains(html, "--brand: #2563eb");
                    StringAssert.Contains(html, "openTopology");
                    StringAssert.Contains(html, "设备状态时间线");
                    StringAssert.Contains(html, "executePreview");
                    StringAssert.Contains(html, "machine.process_stop.preview.v1");
                    StringAssert.Contains(html, "只停止当前冻结的运行实例");
                    StringAssert.Contains(html, "AI 无直接执行权");
                    Assert.IsFalse(html.Contains("控制契约"), "不得重复展示已由运行边界保证的说明卡。 ");
                    Assert.IsFalse(html.Contains("设备上下文完整度"), "总览不得重复派生一套上下文评分。 ");
                    Assert.IsFalse(html.Contains("class=\"topbar\""), "WebView 内不得再复制外层工作台导航。 ");
                    Assert.IsFalse(html.Contains("execute_process_entry_execution"),
                        "页面不得获得可由模型直接调用的设备执行工具。");
                }
            }
        }

        [TestMethod]
        public void TopologyChildModule_UsesAutomationPaletteAndReadableTypeScale()
        {
            Assembly assembly = typeof(FrmMachineAgent).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(
                "Automation.Assets.EquipmentTopology.index.html"))
            {
                Assert.IsNotNull(stream, "拓扑 WebView 页面必须作为程序集资源部署。");
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string html = reader.ReadToEnd();
                    StringAssert.Contains(html, "--bg: #f6f8fb");
                    StringAssert.Contains(html, ".field label { font-size: 13px; }");
                    StringAssert.Contains(html, ".empty-list { font-size: 13px; }");
                    Assert.IsFalse(html.Contains("Semantic Equipment Twin"),
                        "拓扑子模块不应重复展示英文解释性品牌标题。");
                    Assert.IsFalse(html.Contains("value: \"vision\"", StringComparison.Ordinal));
                    Assert.IsFalse(html.Contains("vision: \"视觉\"", StringComparison.Ordinal));
                    StringAssert.Contains(html, "node.reviewState = node.reviewState || \"candidate\"");
                    StringAssert.Contains(html, "binding.reviewState = binding.reviewState || \"candidate\"");
                    StringAssert.Contains(html, "skill.reviewState = skill.reviewState || \"candidate\"");
                    StringAssert.Contains(html, "relation.reviewState = relation.reviewState || \"candidate\"");
                    StringAssert.Contains(html, "function hasRuleProvenance(item)");
                    StringAssert.Contains(html, "sourceType: \"manual_edit\"");
                    StringAssert.Contains(html, "detachNodeFromRule(node)");
                    StringAssert.Contains(html, "operatorOptions(binding.operator, binding.sourceKind)");
                    StringAssert.Contains(html, "message.lastSuccessfulObservationAtUtc");
                    StringAssert.Contains(html, "const live = state.stateHistoryWindow ? perceptionLive : signalLive");
                    StringAssert.Contains(html, "&& !state.observationError");
                    Assert.IsFalse(html.Contains("Date.now() - lastSuccessfulAt"),
                        "稳定状态不能仅因持续时间超过固定阈值而被判定过期。");
                    Assert.IsFalse(html.Contains("状态感知连接超时"),
                        "界面不得把未变化的稳定状态描述为连接超时。");
                    StringAssert.Contains(html, "setInterval(renderRuntimeIndicator, 1000)");
                }
            }
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void MachineAgent_NavigationIsHorizontalAndUsesAutomationPalette()
        {
            StaTestRunner.Run(() =>
            {
                // 这里只验证尚未挂接平台工作区的构造布局；运行期实例由 EditorWorkspace 统一释放。
                var form = new FrmMachineAgent();
                var layout = ReadField<TableLayoutPanel>(form, "rootLayout");
                var agent = ReadField<Button>(form, "agentButton");
                var overview = ReadField<Button>(form, "overviewButton");
                var timeline = ReadField<Button>(form, "timelineButton");
                var topology = ReadField<Button>(form, "topologyButton");

                Assert.AreEqual(1, layout.ColumnCount);
                Assert.AreEqual(2, layout.RowCount);
                Assert.AreEqual(agent.Top, overview.Top);
                Assert.AreEqual(agent.Top, timeline.Top);
                Assert.AreEqual(agent.Top, topology.Top);
                Assert.IsTrue(agent.Left < overview.Left && overview.Left < timeline.Left && timeline.Left < topology.Left);
                Assert.IsTrue(agent.Font.Size >= 10F, "顶部功能页签文字不得再使用小字号。");
                Assert.AreEqual(Color.FromArgb(246, 248, 251), form.BackColor);
                Assert.IsFalse(agent.Text.Contains(Environment.NewLine), "功能页签只保留一级名称。");
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void MainMenu_ExposesMachineAgentInsteadOfStandaloneTopology()
        {
            StaTestRunner.Run(() =>
            {
                using (var menu = new FrmMenu())
                {
                    var machineButton = typeof(FrmMenu).GetField(
                        "machineAgent_Page",
                        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(menu) as Button;
                    Assert.IsNotNull(machineButton);
                    Assert.AreEqual("Machine Agent", machineButton.Tag,
                        "主导航自绘后应在 Tag 中保留产品名称。");
                    Assert.AreEqual("Machine Agent", machineButton.AccessibleName);
                    Assert.IsNull(typeof(FrmMenu).GetField(
                        "equipmentTopology_Page",
                        BindingFlags.Instance | BindingFlags.NonPublic),
                        "拓扑不应继续作为一级主导航模块。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        private static T ReadField<T>(object instance, string name) where T : class
        {
            var value = instance.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T;
            Assert.IsNotNull(value, $"缺少字段 {name}。");
            return value;
        }
    }
}
