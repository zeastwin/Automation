// 模块：核心测试 / 流程树。
// 职责范围：验证流程选中行使用高对比度的自定义视觉。

using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class ProcessTreeSelectionRenderingTests
    {
        private const int GwlStyle = -16;
        private const int TvsNoHorizontalScroll = 0x8000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr windowHandle, int index);

        [TestMethod]
        [TestCategory("Desktop")]
        public void ProcessTree_DisablesHorizontalScrollingSoSelectionCannotMoveViewport()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmProc
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(180, 120)
                })
                {
                    TreeView tree = form.proc_treeView;
                    tree.CreateControl();

                    int nativeStyle = GetWindowLong(tree.Handle, GwlStyle);
                    Assert.AreNotEqual(
                        0,
                        nativeStyle & TvsNoHorizontalScroll,
                        "流程树应禁止原生水平滚动，避免选择长名称或深层节点时整个视口左右跳动。");
                }
            }, TimeSpan.FromSeconds(10));
        }

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

        [TestMethod]
        [TestCategory("Desktop")]
        public void SelectedChildRow_PreservesNativeIndentation()
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
                using (var layoutHost = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(280, 120)
                })
                using (var marker = new Bitmap(20, 20))
                using (var images = new ImageList
                {
                    ColorDepth = ColorDepth.Depth32Bit,
                    ImageSize = new Size(20, 20),
                    TransparentColor = Color.Transparent
                })
                {
                    using (Graphics markerGraphics = Graphics.FromImage(marker))
                    {
                        markerGraphics.Clear(Color.Magenta);
                    }
                    images.Images.Add("marker", marker);

                    TreeView tree = form.proc_treeView;
                    tree.ImageList = images;
                    var layoutTree = new TreeView
                    {
                        Dock = DockStyle.Fill,
                        Font = tree.Font,
                        FullRowSelect = true,
                        ImageList = images,
                        Indent = tree.Indent,
                        ItemHeight = tree.ItemHeight,
                        ShowLines = false,
                        ShowRootLines = false
                    };
                    layoutHost.Controls.Add(layoutTree);
                    var processNode = new TreeNode("0：流程")
                    {
                        ImageKey = "marker",
                        SelectedImageKey = "marker"
                    };
                    var stepNode = new TreeNode("0：步骤")
                    {
                        ImageKey = "marker",
                        SelectedImageKey = "marker"
                    };
                    processNode.Nodes.Add(stepNode);
                    layoutTree.Nodes.Add(processNode);

                    layoutHost.Show();
                    processNode.Expand();
                    Application.DoEvents();
                    Assert.IsTrue(
                        stepNode.Bounds.Left > processNode.Bounds.Left,
                        "测试前提：子节点原生标签位置应包含一级缩进。");

                    using (var bitmap = new Bitmap(
                        tree.ClientSize.Width,
                        tree.ItemHeight))
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
                                stepNode,
                                new Rectangle(
                                    0,
                                    0,
                                    tree.ClientSize.Width,
                                    tree.ItemHeight),
                                TreeNodeStates.Selected)
                        });

                        int expectedImageLeft =
                            stepNode.Bounds.Left - images.ImageSize.Width - 3;
                        Assert.AreEqual(
                            expectedImageLeft,
                            FindFirstMarkerColumn(bitmap),
                            "选中子节点的图标必须保持原生层级缩进，不能跳到根节点左侧。");
                    }
                    tree.ImageList = null;
                    layoutTree.ImageList = null;
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ReadyAndRunningIcons_UseClearlyDifferentStatusColors()
        {
            using (Bitmap ready = ProcTreeIconFactory.Create(
                ProcTreeIconKind.Ready,
                20))
            using (Bitmap running = ProcTreeIconFactory.Create(
                ProcTreeIconKind.Running,
                20))
            {
                Assert.IsTrue(
                    CountColor(ready, UiPalette.BrandAccent) > 0,
                    "就绪或执行完成状态应使用蓝色勾，不应继续使用运行绿。");
                Assert.IsTrue(
                    CountColor(running, UiPalette.Success) > 0,
                    "运行状态应继续使用绿色播放徽标。");
                Assert.AreNotEqual(
                    UiPalette.BrandAccent.ToArgb(),
                    UiPalette.Success.ToArgb(),
                    "执行完成与运行中的主色必须具有明确区分度。");
            }
        }

        private static int FindFirstMarkerColumn(Bitmap bitmap)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                for (int y = 0; y < bitmap.Height; y++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    if (color.R > 240 && color.G < 20 && color.B > 240)
                    {
                        return x;
                    }
                }
            }
            return -1;
        }

        private static int CountColor(Bitmap bitmap, Color expected)
        {
            int count = 0;
            for (int x = 0; x < bitmap.Width; x++)
            {
                for (int y = 0; y < bitmap.Height; y++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() == expected.ToArgb())
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
