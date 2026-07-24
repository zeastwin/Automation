using System;
// 模块：核心测试 / 编辑器外壳。
// 职责范围：验证 FrmMain 构造阶段不隐式初始化平台或依赖既有静态窗体。

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class EditorShellConstructionTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void Constructor_DoesNotRequireAnInitializedPlatform()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmMain())
                {
                    Assert.IsNotNull(form);
                    Assert.IsFalse(form.frmAiAssistant.IsViewLoaded,
                        "平台编辑器构造阶段不应提前加载隐藏的 AI WebView。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ProcessOutlineWidth_CanBeAdjustedWithSplitter()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Assert.AreEqual(
                        System.Windows.Forms.DockStyle.Left,
                        form.processOutlineSplitter.Dock);
                    Assert.AreEqual(150, form.processOutlineSplitter.MinSize);
                    Assert.AreSame(
                        form.processOutlinePanel.Parent,
                        form.processOutlineSplitter.Parent,
                        "流程导航与拖动分隔条必须位于同一个编辑器布局容器中。");
                    Assert.AreEqual(
                        form.processOutlinePanel.Right,
                        form.processOutlineSplitter.Left,
                        "分隔条应紧贴流程导航右侧，供用户直接拖动调整宽度。");
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ReturningToProcessWorkspace_RestoresActiveOperationDraftAndSelection()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = TestProcessFactory.CreateEndingProcess("编辑态切页");
                    form.Runtime.Stores.Processes.Items.Add(process);
                    form.frmProc.SelectedProcNum = 0;
                    form.frmProc.SelectedStepNum = 0;
                    form.frmDataGrid.iSelectedRow = 0;

                    var draft = new EndProcess
                    {
                        Id = process.steps[0].Ops[0].Id,
                        Name = "尚未保存的指令名"
                    };
                    form.Runtime.Editor.ModifyKind = ModifyKind.Operation;
                    form.Runtime.Editor.Begin(new EditSession<OperationType>(
                        "修改指令",
                        draft,
                        null,
                        value => { }));

                    form.frmMenu.ShowIoConfigurationWorkspace();
                    form.frmMenu.ShowProcessWorkspace();

                    Assert.AreSame(draft, form.Runtime.Editor.ActiveSession?.Draft);
                    Assert.AreSame(draft, form.frmDataGrid.OperationTemp);
                    Assert.AreSame(draft, form.frmInspector.SelectedObject);
                    Assert.AreEqual(0, form.frmProc.SelectedProcNum);
                    Assert.AreEqual(0, form.frmProc.SelectedStepNum);
                    Assert.AreEqual(0, form.frmDataGrid.iSelectedRow);
                    Assert.IsTrue(form.frmToolBar.btnSave.Enabled);
                    Assert.IsTrue(form.frmToolBar.btnCancel.Enabled);

                    form.Runtime.Editor.Cancel();

                    Assert.AreSame(
                        process.steps[0].Ops[0],
                        form.frmInspector.SelectedObject,
                        "取消指令编辑后应继续查看当前选中的已保存指令。");
                    Assert.AreNotSame(
                        draft,
                        form.frmInspector.SelectedObject,
                        "取消后不应继续呈现已丢弃的编辑草稿。");
                    Assert.AreEqual(0, form.frmDataGrid.iSelectedRow);
                    Assert.IsFalse(form.frmToolBar.btnSave.Enabled);
                    Assert.IsFalse(form.frmToolBar.btnCancel.Enabled);
                }
            }, TimeSpan.FromSeconds(20));
        }
    }
}
