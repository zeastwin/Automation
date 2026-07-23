// 模块：核心测试 / 流程树。
// 职责范围：验证流程选中行使用高对比度的自定义视觉。

using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class ProcessTreeSelectionRenderingTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void SelectedProcessRow_UsesLightFullRowBackgroundInsteadOfSystemHighlight()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmProc
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(280, 120)
                })
                {
                    TreeView tree = form.proc_treeView;
                    var processNode = new TreeNode("1：循环示例")
                    {
                        ImageKey = "proc-ready",
                        SelectedImageKey = "proc-ready"
                    };
                    processNode.Nodes.Add(new TreeNode("0：步骤"));
                    tree.Nodes.Add(processNode);
                    tree.CreateControl();

                    Assert.AreEqual(TreeViewDrawMode.OwnerDrawAll, tree.DrawMode);
                    using (var bitmap = new Bitmap(tree.ClientSize.Width, tree.ItemHeight))
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(tree.BackColor);
                        MethodInfo drawHandler = typeof(FrmProc).GetMethod(
                            "ProcTreeView_DrawNode",
                            BindingFlags.Instance | BindingFlags.NonPublic)
                            ?? throw new InvalidOperationException("未找到流程树绘制处理器。");
                        drawHandler.Invoke(form, new object[]
                        {
                            tree,
                            new DrawTreeNodeEventArgs(
                                graphics,
                                processNode,
                                new Rectangle(28, 0, 150, tree.ItemHeight),
                                TreeNodeStates.Selected)
                        });
                        Color rowBackground = bitmap.GetPixel(bitmap.Width - 8, tree.ItemHeight / 2);
                        Assert.AreEqual(UiPalette.Selection.ToArgb(), rowBackground.ToArgb(),
                            "选中流程应使用浅色整行背景，避免系统高饱和蓝底降低可读性。");
                        Color accent = bitmap.GetPixel(1, tree.ItemHeight / 2);
                        Assert.AreEqual(UiPalette.Brand.ToArgb(), accent.ToArgb(),
                            "选中行应通过左侧强调条表达焦点。");
                    }
                }
            }, TimeSpan.FromSeconds(10));
        }
    }
}
