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
        private const int TvsNoToolTips = 0x0080;

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
                    Assert.AreNotEqual(
                        0,
                        nativeStyle & TvsNoToolTips,
                        "流程树应禁用原生截断标签提示，避免展开后提示窗体反复显隐造成闪烁。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ProcessTree_EnablesBufferedPaintingForStableInteraction()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmProc())
                {
                    MethodInfo getStyle = typeof(Control).GetMethod(
                        "GetStyle",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("未找到控件绘制样式读取入口。");
                    var tree = form.proc_treeView;

                    Assert.IsTrue((bool)getStyle.Invoke(tree, new object[]
                    {
                        ControlStyles.OptimizedDoubleBuffer
                    }));
                    Assert.IsTrue((bool)getStyle.Invoke(tree, new object[]
                    {
                        ControlStyles.AllPaintingInWmPaint
                    }));
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
        public void ProcessTree_UsesCompactLayoutAndReadableDisabledText()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = CreateProcessWithSteps(
                        "已禁用流程",
                        "已禁用步骤");
                    process.head.Disable = true;
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { process });
                    main.frmProc.RefreshProcListFromStore();

                    TreeView tree = main.frmProc.proc_treeView;
                    TreeNode processNode = tree.Nodes[0];
                    TreeNode stepNode = processNode.Nodes[0];

                    Assert.AreEqual(25, tree.ItemHeight);
                    Assert.AreEqual(new Size(18, 18), tree.ImageList.ImageSize);
                    Assert.IsFalse(
                        tree.ShowNodeToolTips,
                        "流程树节点悬停时不应显示浮动提示。");
                    Assert.IsTrue(processNode.NodeFont.Size <= 11F);
                    Assert.IsTrue(stepNode.NodeFont.Size <= 10.5F);
                    Assert.AreEqual(
                        UiPalette.TextMuted.ToArgb(),
                        processNode.ForeColor.ToArgb(),
                        "禁用流程文字应保持可读，不能使用接近背景色的浅色填充色。");
                    Assert.AreEqual(
                        UiPalette.TextMuted.ToArgb(),
                        stepNode.ForeColor.ToArgb(),
                        "禁用步骤文字应保持可读，不能使用接近背景色的浅色填充色。");
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ChildRow_UsesCompactIndentationInEveryState()
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
                        "步骤应保留轻微错位，以便区分流程与步骤层级。");
                    Assert.AreEqual(
                        6,
                        tree.Indent,
                        "流程树应声明紧凑步骤缩进。");

                    int expectedImageLeft = 5 + tree.Indent;
                    foreach (TreeNodeStates state in new[]
                    {
                        TreeNodeStates.Default,
                        TreeNodeStates.Selected
                    })
                    {
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
                                    state)
                            });

                            Assert.AreEqual(
                                expectedImageLeft,
                                FindFirstMarkerColumn(bitmap),
                                "步骤图标在选中和普通状态下都应跳过原生展开槽，只保留紧凑层级缩进。");
                        }
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

        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshProcessView_ReusesStableStepNodesAcrossInsertAndReorder()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = CreateProcessWithSteps(
                        "原位刷新",
                        "步骤一",
                        "步骤二");
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { process });
                    main.frmProc.RefreshProcListFromStore();

                    TreeNode processNode = main.frmProc.proc_treeView.Nodes[0];
                    TreeNode firstStepNode = processNode.Nodes[0];
                    TreeNode secondStepNode = processNode.Nodes[1];
                    processNode.Expand();
                    main.frmProc.proc_treeView.SelectedNode = firstStepNode;
                    main.frmProc.SelectedProcNum = 0;
                    main.frmProc.SelectedStepNum = 0;

                    Proc changed = ObjectGraphCloner.Clone(process);
                    Step inserted = new Step
                    {
                        Id = Guid.NewGuid(),
                        Name = "新增步骤"
                    };
                    Step first = changed.steps[0];
                    Step second = changed.steps[1];
                    changed.steps.Clear();
                    changed.steps.Add(second);
                    changed.steps.Add(inserted);
                    changed.steps.Add(first);
                    main.Runtime.Stores.Processes.ReplaceAt(0, changed);

                    main.frmProc.RefreshProcView(0);

                    Assert.AreSame(processNode, main.frmProc.proc_treeView.Nodes[0]);
                    Assert.AreSame(secondStepNode, processNode.Nodes[0]);
                    Assert.AreSame(firstStepNode, processNode.Nodes[2]);
                    Assert.AreSame(
                        firstStepNode,
                        main.frmProc.proc_treeView.SelectedNode,
                        "步骤插入或重排后应按稳定 ID 保留原选中节点。");
                    Assert.AreEqual(2, main.frmProc.SelectedStepNum);
                    Assert.IsTrue(processNode.IsExpanded);
                    Assert.AreEqual(string.Empty, processNode.ToolTipText);
                    Assert.AreEqual(
                        string.Empty,
                        firstStepNode.ToolTipText,
                        "复用的步骤节点不应残留悬停提示文本。");
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshProcessList_RebindsOperationsWhenSelectedStepNodeIsReused()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = CreateProcessWithSteps(
                        "原位换源",
                        "稳定步骤");
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { process });
                    main.frmProc.RefreshProcListFromStore();

                    TreeNode stepNode =
                        main.frmProc.proc_treeView.Nodes[0].Nodes[0];
                    main.frmProc.proc_treeView.SelectedNode = stepNode;
                    Application.DoEvents();
                    Assert.AreEqual(
                        0,
                        main.frmDataGrid.dataGridView1.OperationCount);

                    Guid operationId = Guid.NewGuid();
                    Proc replacement = ObjectGraphCloner.Clone(process);
                    replacement.steps[0].Ops.Add(new EndProcess
                    {
                        Id = operationId,
                        Name = "AI新增指令"
                    });
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { replacement });

                    main.frmProc.RefreshProcListFromStore();
                    Application.DoEvents();

                    Assert.AreSame(
                        stepNode,
                        main.frmProc.proc_treeView.SelectedNode,
                        "全量刷新应继续复用稳定步骤节点。");
                    Assert.AreEqual(0, main.frmProc.SelectedProcNum);
                    Assert.AreEqual(0, main.frmProc.SelectedStepNum);
                    Assert.AreEqual(
                        1,
                        main.frmDataGrid.dataGridView1.OperationCount,
                        "复用同一树节点时，指令表仍必须切换到新的 Ops 集合。");
                    Assert.AreEqual(
                        operationId,
                        main.frmDataGrid.dataGridView1.GetOperation(0)?.Id);
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshProcessList_RepairsMissedBindingSourceChangeNotification()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = CreateProcessWithSteps(
                        "换源通知恢复",
                        "稳定步骤");
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { process });
                    main.frmProc.RefreshProcListFromStore();
                    main.frmProc.proc_treeView.SelectedNode =
                        main.frmProc.proc_treeView.Nodes[0].Nodes[0];
                    Application.DoEvents();

                    InstructionListView grid =
                        main.frmDataGrid.dataGridView1;
                    MethodInfo listChangedMethod = typeof(InstructionListView).GetMethod(
                        "BindingSource_ListChanged",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(
                            "未找到指令表 BindingSource 换源处理器。");
                    var listChangedHandler =
                        (System.ComponentModel.ListChangedEventHandler)Delegate.CreateDelegate(
                            typeof(System.ComponentModel.ListChangedEventHandler),
                            grid,
                            listChangedMethod);
                    main.frmProc.bindingSource.ListChanged -= listChangedHandler;

                    Guid operationId = Guid.NewGuid();
                    Proc replacement = ObjectGraphCloner.Clone(process);
                    replacement.steps[0].Ops.Add(new EndProcess
                    {
                        Id = operationId,
                        Name = "通知遗漏后新增指令"
                    });
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { replacement });

                    main.frmProc.RefreshProcListFromStore();
                    Application.DoEvents();

                    Assert.AreEqual(
                        1,
                        grid.OperationCount,
                        "即使一次 BindingSource 换源通知遗漏，刷新结束也必须按真实 List 自愈。");
                    Assert.AreEqual(
                        operationId,
                        grid.GetOperation(0)?.Id);
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshProcessList_ReusesProcessAndStepNodesWhenOrderChanges()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Proc retained = CreateProcessWithSteps(
                        "保留流程",
                        "稳定步骤");
                    Guid retainedOperationId = Guid.NewGuid();
                    retained.steps[0].Ops.Add(new EndProcess
                    {
                        Id = retainedOperationId,
                        Name = "稳定指令"
                    });
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { retained });
                    main.frmProc.RefreshProcListFromStore();

                    TreeNode retainedProcessNode =
                        main.frmProc.proc_treeView.Nodes[0];
                    TreeNode retainedStepNode =
                        retainedProcessNode.Nodes[0];
                    retainedProcessNode.Expand();
                    main.frmProc.proc_treeView.SelectedNode =
                        retainedStepNode;

                    Proc inserted = CreateProcessWithSteps(
                        "插入流程",
                        "插入步骤");
                    main.Runtime.Stores.Processes.ReplaceAll(new[]
                    {
                        inserted,
                        ObjectGraphCloner.Clone(retained)
                    });
                    main.frmProc.RefreshProcListFromStore();

                    Assert.AreSame(
                        retainedProcessNode,
                        main.frmProc.proc_treeView.Nodes[1],
                        "流程顺序变化时应移动既有节点，不应重新创建。");
                    Assert.AreSame(
                        retainedStepNode,
                        retainedProcessNode.Nodes[0]);
                    Assert.AreSame(
                        retainedStepNode,
                        main.frmProc.proc_treeView.SelectedNode);
                    Assert.AreEqual(
                        1,
                        main.frmProc.SelectedProcNum,
                        "流程节点移动后，显示索引必须按稳定 ID 更新。");
                    Assert.AreEqual(0, main.frmProc.SelectedStepNum);
                    Assert.AreEqual(
                        retainedOperationId,
                        main.frmDataGrid.dataGridView1.GetOperation(0)?.Id,
                        "索引移动后指令表必须继续绑定原稳定流程。");
                    Assert.IsTrue(retainedProcessNode.IsExpanded);
                }
            }, TimeSpan.FromSeconds(20));
        }

        private static Proc CreateProcessWithSteps(
            string processName,
            params string[] stepNames)
        {
            var process = new Proc
            {
                head = new ProcHead
                {
                    Id = Guid.NewGuid(),
                    Name = processName
                }
            };
            foreach (string stepName in stepNames)
            {
                process.steps.Add(new Step
                {
                    Id = Guid.NewGuid(),
                    Name = stepName
                });
            }
            return process;
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
