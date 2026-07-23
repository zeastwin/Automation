// 模块：核心测试 / 指令表。
// 职责范围：验证步骤切换时复用整流程跳转关系，避免交互路径重复扫描。

using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class InstructionListInteractionTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void SwitchingSteps_ReusesWholeProcessJumpLinksUntilOperationsChange()
        {
            StaTestRunner.Run(() =>
            {
                var proc = new Proc();
                var firstStep = new Step { Name = "步骤一" };
                firstStep.Ops.Add(new Delay());
                var secondStep = new Step { Name = "步骤二" };
                secondStep.Ops.Add(new EndProcess());
                proc.steps.Add(firstStep);
                proc.steps.Add(secondStep);

                using (var bindingSource = new BindingSource())
                using (var list = new InstructionListView { Dock = DockStyle.Fill })
                using (var form = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(640, 320)
                })
                {
                    form.Controls.Add(list);
                    form.Show();
                    Application.DoEvents();

                    list.SetFlowContext(0, 0, proc);
                    bindingSource.DataSource = firstStep.Ops;
                    list.DataSource = bindingSource;

                    Assert.AreEqual(
                        1,
                        list.JumpLinkBuildCount,
                        "首次显示步骤时只应构建一次整流程跳转关系。");

                    list.SetFlowContext(0, 1, proc);
                    bindingSource.DataSource = secondStep.Ops;

                    Assert.AreEqual(
                        1,
                        list.JumpLinkBuildCount,
                        "同一流程内切换步骤应直接复用跳转关系。");

                    bindingSource.ResetBindings(false);

                    Assert.AreEqual(
                        2,
                        list.JumpLinkBuildCount,
                        "指令数据明确刷新后应重建一次跳转关系。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void SelectingProcessRoot_PrewarmsJumpLinksBeforeFirstStepClick()
        {
            StaTestRunner.Run(() =>
            {
                var proc = new Proc();
                var step = new Step { Name = "步骤" };
                step.Ops.Add(new Delay());
                proc.steps.Add(step);

                using (var bindingSource = new BindingSource())
                using (var list = new InstructionListView { Dock = DockStyle.Fill })
                using (var form = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(640, 320)
                })
                {
                    form.Controls.Add(list);
                    form.Show();

                    list.SetFlowContext(0, -1, proc);

                    Assert.AreEqual(
                        0,
                        list.JumpLinkBuildCount,
                        "流程根节点的选中处理内不应同步扫描跳转关系。");

                    Application.DoEvents();

                    Assert.AreEqual(
                        1,
                        list.JumpLinkBuildCount,
                        "流程根节点显示后应在消息队列空档预热跳转关系。");

                    list.SetFlowContext(0, 0, proc);
                    bindingSource.DataSource = step.Ops;
                    list.DataSource = bindingSource;

                    Assert.AreEqual(
                        1,
                        list.JumpLinkBuildCount,
                        "首次点击步骤时应直接使用已经预热的跳转关系。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void StartupCache_AllowsRapidSwitchingAcrossProcessesWithoutRebuild()
        {
            StaTestRunner.Run(() =>
            {
                Proc firstProc = CreateProcess("流程一");
                Proc secondProc = CreateProcess("流程二");

                using (var bindingSource = new BindingSource())
                using (var list = new InstructionListView { Dock = DockStyle.Fill })
                using (var form = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(640, 320)
                })
                {
                    form.Controls.Add(list);
                    form.Show();
                    Application.DoEvents();

                    list.RebuildJumpLinkCaches(new[] { firstProc, secondProc });
                    Assert.AreEqual(
                        2,
                        list.JumpLinkBuildCount,
                        "启动预热应为每个流程各建立一次缓存。");

                    list.SetFlowContext(0, 0, firstProc);
                    bindingSource.DataSource = firstProc.steps[0].Ops;
                    list.DataSource = bindingSource;
                    list.SetFlowContext(1, 0, secondProc);
                    bindingSource.DataSource = secondProc.steps[0].Ops;
                    list.SetFlowContext(0, 0, firstProc);
                    bindingSource.DataSource = firstProc.steps[0].Ops;

                    Assert.AreEqual(
                        2,
                        list.JumpLinkBuildCount,
                        "流程一、流程二之间快速往返应始终直接切换缓存。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ReplacingOneProcess_RebuildsOnlyItsJumpLinkCache()
        {
            StaTestRunner.Run(() =>
            {
                Proc firstProc = CreateProcess("流程一");
                Proc secondProc = CreateProcess("流程二");
                Proc replacement = CreateProcess("流程一修改后");

                using (var list = new InstructionListView())
                {
                    list.RebuildJumpLinkCaches(new[] { firstProc, secondProc });
                    list.RebuildJumpLinkCache(0, replacement);

                    Assert.AreEqual(
                        3,
                        list.JumpLinkBuildCount,
                        "流程编辑提交后只应重建被替换流程的缓存。");

                    list.SetFlowContext(1, 0, secondProc);
                    list.DataSource = secondProc.steps[0].Ops;
                    list.SetFlowContext(0, 0, replacement);
                    list.DataSource = replacement.steps[0].Ops;

                    Assert.AreEqual(
                        3,
                        list.JumpLinkBuildCount,
                        "未修改流程和替换后的流程都应直接命中各自缓存。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        private static Proc CreateProcess(string name)
        {
            var proc = new Proc
            {
                head = new ProcHead { Name = name }
            };
            var step = new Step { Name = "步骤" };
            step.Ops.Add(new Delay());
            proc.steps.Add(step);
            return proc;
        }
    }
}
