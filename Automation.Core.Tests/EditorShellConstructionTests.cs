using System;
// 模块：核心测试 / 编辑器外壳。
// 职责范围：验证 FrmMain 构造阶段不隐式初始化平台或依赖既有静态窗体。

using System.Reflection;
using System.Windows.Forms;
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
        public void UnauthenticatedEditor_KeepsWorkspaceVisibleAndToolbarLoginAvailable()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                {
                    var runtime = new PlatformRuntime(directory.FullPath);
                    Assert.IsTrue(runtime.Accounts.Initialize(out string error), error);
                    using (var form = new FrmMain(runtime))
                    {
                        form.frmToolBar.ApplyAccountPermissions();
                        var loginButton = (Button)typeof(FrmToolBar)
                            .GetField("btnAccount", BindingFlags.Instance | BindingFlags.NonPublic)
                            ?.GetValue(form.frmToolBar);
                        var flowGraphButton = (Button)typeof(FrmToolBar)
                            .GetField("btnFlowGraph", BindingFlags.Instance | BindingFlags.NonPublic)
                            ?.GetValue(form.frmToolBar);
                        var settingsButton = (Button)typeof(FrmToolBar)
                            .GetField("btnAppConfig", BindingFlags.Instance | BindingFlags.NonPublic)
                            ?.GetValue(form.frmToolBar);
                        var accountStatusLabel = (Label)typeof(FrmState)
                            .GetField("lblAccountLevel", BindingFlags.Instance | BindingFlags.NonPublic)
                            ?.GetValue(form.frmState);

                        Assert.IsFalse(runtime.Accounts.IsAuthenticated);
                        Assert.IsNotNull(loginButton);
                        Assert.IsTrue(loginButton.Enabled,
                            "未登录时工具栏登录入口必须保持可用，不能形成进入编辑器的权限死循环。");
                        Assert.AreEqual(string.Empty, loginButton.Text,
                            "账户入口只显示状态图标，不应再占用工具栏宽度显示账户文字。");
                        Assert.AreEqual(44, (int)loginButton.Tag,
                            "账户纯图标入口应只占用一个标准工具栏按钮宽度。");
                        StringAssert.Contains(loginButton.AccessibleName, "未登录");
                        Assert.IsNull(typeof(FrmMain).GetField(
                            "accountLockPanel",
                            BindingFlags.Instance | BindingFlags.NonPublic),
                            "未登录时不应再创建遮挡平台内容的工作区锁定层。");
                        Assert.IsNotNull(form.processOutlinePanel.Parent,
                            "流程导航必须继续挂载在可查看的编辑器工作区中。");
                        Assert.IsNotNull(form.DataGrid_panel.Parent,
                            "流程内容必须继续挂载在可查看的编辑器工作区中。");
                        Assert.IsNotNull(form.inspector_panel.Parent,
                            "属性面板必须继续挂载在可查看的编辑器工作区中。");
                        Assert.IsNotNull(flowGraphButton);
                        Assert.IsFalse(flowGraphButton.Enabled,
                            "平台内容可以查看，但受控操作必须保持禁用。");
                        Assert.IsNotNull(settingsButton);
                        Assert.IsNotNull(accountStatusLabel);
                        Assert.AreEqual("账户：未登录", accountStatusLabel.Text);
                        Assert.IsFalse(ImagesEqual(loginButton.Image, settingsButton.Image),
                            "账户入口必须使用独立的人物图标，不能复用程序设置的齿轮图标。");

                        using (var signedOutIcon = new System.Drawing.Bitmap(loginButton.Image))
                        {
                            Assert.IsTrue(runtime.Accounts.Login(
                                AccountSecurityStore.BuiltInSystemUserName,
                                AccountSecurityStore.BuiltInSystemDefaultPassword,
                                out error), error);
                            form.frmToolBar.ApplyAccountPermissions();
                            Assert.AreEqual(string.Empty, loginButton.Text);
                            StringAssert.Contains(loginButton.AccessibleName, "系统管理员");
                            Assert.AreEqual("账户：系统管理员", accountStatusLabel.Text,
                                "编辑器状态栏最右侧应即时显示当前账户级别。");
                            Assert.IsFalse(ImagesEqual(signedOutIcon, loginButton.Image),
                                "登录后账户图标必须切换为对应账户级别的状态图形。");
                        }
                    }
                }
            }, TimeSpan.FromSeconds(20));
        }

        private static bool ImagesEqual(System.Drawing.Image left, System.Drawing.Image right)
        {
            if (left == null || right == null || left.Size != right.Size)
            {
                return false;
            }
            using (var leftBitmap = new System.Drawing.Bitmap(left))
            using (var rightBitmap = new System.Drawing.Bitmap(right))
            {
                for (int y = 0; y < leftBitmap.Height; y++)
                {
                    for (int x = 0; x < leftBitmap.Width; x++)
                    {
                        if (leftBitmap.GetPixel(x, y) != rightBitmap.GetPixel(x, y))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
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

        [TestMethod]
        [TestCategory("Desktop")]
        public void InspectorCannotEnableOrCommitLiveObjectDuringDraftSession()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = TestProcessFactory.CreateEndingProcess(
                        "检查器草稿身份");
                    form.Runtime.Stores.Processes.Items.Add(process);
                    form.frmProc.SelectedProcNum = 0;
                    form.frmProc.SelectedStepNum = 0;
                    form.frmDataGrid.iSelectedRow = 0;

                    var draft = new EndProcess
                    {
                        Id = process.steps[0].Ops[0].Id,
                        Name = "草稿"
                    };
                    form.Runtime.Editor.ModifyKind = ModifyKind.Operation;
                    form.Runtime.Editor.Begin(new EditSession<OperationType>(
                        "修改指令",
                        draft,
                        null,
                        value => { }));

                    form.frmInspector.ShowObject(
                        process.steps[0].Ops[0]);
                    form.frmInspector.SetEditingState(true);

                    Assert.IsFalse(form.frmToolBar.btnSave.Enabled);
                    Assert.IsTrue(form.frmToolBar.btnCancel.Enabled,
                        "草稿显示失配时仍必须允许用户取消会话。");
                    Assert.IsFalse(
                        form.frmInspector.TryCommitPendingEdit(
                            out string error));
                    StringAssert.Contains(error, "不是当前编辑草稿");

                    form.Runtime.Editor.Cancel();
                }
            }, TimeSpan.FromSeconds(20));
        }
    }
}
