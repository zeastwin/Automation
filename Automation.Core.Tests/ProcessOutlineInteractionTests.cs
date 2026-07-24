// 模块：核心测试 / 流程导航列表。
// 职责范围：验证流程与步骤的稳定选择、紧凑绘制和数据重新绑定。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class ProcessOutlineInteractionTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void ProcessOutline_UsesSingleBufferedCanvasWithoutNativeListState()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmProc())
                {
                    ProcessOutlineList outline = form.processOutline;
                    MethodInfo getStyle = typeof(Control).GetMethod(
                        "GetStyle",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("未找到控件绘制样式读取入口。");

                    Assert.IsInstanceOfType(
                        outline,
                        typeof(ScrollableControl));
                    Assert.IsFalse(
                        typeof(ListBox).IsAssignableFrom(
                            outline.GetType()));
                    Assert.IsTrue((bool)getStyle.Invoke(outline, new object[]
                    {
                        ControlStyles.UserPaint
                    }));
                    Assert.IsTrue((bool)getStyle.Invoke(outline, new object[]
                    {
                        ControlStyles.OptimizedDoubleBuffer
                    }));
                    Assert.IsTrue((bool)getStyle.Invoke(outline, new object[]
                    {
                        ControlStyles.AllPaintingInWmPaint
                    }));
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void MouseHover_DoesNotChangeSelectionOrRaiseNavigationEvent()
        {
            StaTestRunner.Run(() =>
            {
                using (var outline = CreateDrawingOutline(
                    out ImageList images,
                    out Guid procId,
                    out _))
                using (images)
                {
                    outline.SelectIdentity(procId, Guid.Empty, false);
                    int userSelectionCount = 0;
                    outline.UserSelectionChanged += (sender, args) =>
                        userSelectionCount++;
                    MethodInfo mouseMove = typeof(Control).GetMethod(
                        "OnMouseMove",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(
                            "未找到鼠标移动事件入口。");

                    mouseMove.Invoke(outline, new object[]
                    {
                        new MouseEventArgs(
                            MouseButtons.None,
                            0,
                            80,
                            outline.ItemHeight + outline.ItemHeight / 2,
                            0)
                    });

                    Assert.AreEqual(
                        procId,
                        outline.SelectedOutlineItem?.ProcId);
                    Assert.AreEqual(
                        Guid.Empty,
                        outline.SelectedOutlineItem?.StepId);
                    Assert.AreEqual(
                        0,
                        userSelectionCount,
                        "鼠标划过步骤时不应触发选择、导航或浮动提示链路。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ExpansionArrow_OnlyChangesVisibilityAndKeepsSelection()
        {
            StaTestRunner.Run(() =>
            {
                using (var outline = CreateDrawingOutline(
                    out ImageList images,
                    out Guid procId,
                    out Guid stepId))
                using (images)
                {
                    outline.SelectIdentity(procId, stepId, false);
                    int selectionChangedCount = 0;
                    int userSelectionCount = 0;
                    outline.SelectedIndexChanged += (sender, args) =>
                        selectionChangedCount++;
                    outline.UserSelectionChanged += (sender, args) =>
                        userSelectionCount++;

                    InvokeMouseDown(
                        outline,
                        new MouseEventArgs(
                            MouseButtons.Left,
                            1,
                            4,
                            outline.ItemHeight / 2,
                            0));

                    Assert.AreEqual(1, outline.VisibleItemCount);
                    Assert.AreEqual(
                        stepId,
                        outline.SelectedOutlineItem?.StepId,
                        "折叠流程不得把步骤选择改成流程选择。");
                    Assert.AreEqual(0, selectionChangedCount);
                    Assert.AreEqual(0, userSelectionCount);

                    InvokeMouseDown(
                        outline,
                        new MouseEventArgs(
                            MouseButtons.Left,
                            1,
                            4,
                            outline.ItemHeight / 2,
                            0));

                    Assert.AreEqual(2, outline.VisibleItemCount);
                    Assert.AreEqual(
                        stepId,
                        outline.SelectedOutlineItem?.StepId);
                    Assert.AreEqual(0, selectionChangedCount);
                    Assert.AreEqual(0, userSelectionCount);
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ExpandingProcessAboveViewport_PreservesExactScrollAnchor()
        {
            StaTestRunner.Run(() =>
            {
                using (var outline = new ProcessOutlineList
                {
                    Size = new Size(280, 80),
                    BackColor = UiPalette.Background
                })
                {
                    IReadOnlyList<ProcessOutlineItem> items =
                        CreateOutlineItems(
                            10,
                            3,
                            out Guid[] procIds);
                    _ = outline.Handle;
                    outline.ReplaceItems(
                        items,
                        Enumerable.Empty<Guid>(),
                        Guid.Empty,
                        Guid.Empty,
                        new ProcessOutlineScrollAnchor(
                            procIds[5],
                            Guid.Empty,
                            7,
                            0));
                    ProcessOutlineScrollAnchor before =
                        outline.ScrollAnchor;

                    MethodInfo toggle = typeof(ProcessOutlineList).GetMethod(
                        "ToggleProcess",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(
                            "未找到流程展开入口。");
                    toggle.Invoke(outline, new object[] { procIds[0] });

                    ProcessOutlineScrollAnchor after =
                        outline.ScrollAnchor;
                    Assert.AreEqual(before.ProcId, after.ProcId);
                    Assert.AreEqual(before.StepId, after.StepId);
                    Assert.AreEqual(
                        before.OffsetWithinRow,
                        after.OffsetWithinRow);
                    Assert.AreEqual(
                        before.AbsoluteOffset + 3 * outline.ItemHeight,
                        after.AbsoluteOffset,
                        "在视口上方展开流程时，应调整内部偏移以保持画面原位。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void MouseWheel_OnlyChangesPixelOffsetAndKeepsSelection()
        {
            StaTestRunner.Run(() =>
            {
                using (var outline = new ProcessOutlineList
                {
                    Size = new Size(280, 80),
                    BackColor = UiPalette.Background
                })
                {
                    IReadOnlyList<ProcessOutlineItem> items =
                        CreateOutlineItems(
                            12,
                            0,
                            out Guid[] procIds);
                    _ = outline.Handle;
                    outline.ReplaceItems(
                        items,
                        Enumerable.Empty<Guid>(),
                        procIds[2],
                        Guid.Empty,
                        new ProcessOutlineScrollAnchor(
                            procIds[3],
                            Guid.Empty,
                            5,
                            0));
                    int selectionChangedCount = 0;
                    outline.SelectedIndexChanged += (sender, args) =>
                        selectionChangedCount++;
                    int beforeOffset = outline.ScrollOffset;

                    MethodInfo mouseWheel = typeof(ProcessOutlineList)
                        .GetMethod(
                            "OnMouseWheel",
                            BindingFlags.Instance
                                | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(
                            "未找到鼠标滚轮事件入口。");
                    mouseWheel.Invoke(outline, new object[]
                    {
                        new MouseEventArgs(
                            MouseButtons.None,
                            0,
                            80,
                            40,
                            -120)
                    });

                    Assert.IsTrue(
                        outline.ScrollOffset > beforeOffset,
                        "向下滚动应只增加像素滚动偏移。");
                    Assert.AreEqual(
                        procIds[2],
                        outline.SelectedOutlineItem?.ProcId);
                    Assert.AreEqual(0, selectionChangedCount);
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void FocusTransfer_DoesNotChangeSelectionOrPaintedFrame()
        {
            StaTestRunner.Run(() =>
            {
                using (var outline = CreateDrawingOutline(
                    out ImageList images,
                    out Guid procId,
                    out _))
                using (images)
                using (var before = new Bitmap(
                    outline.Width,
                    outline.Height))
                using (var after = new Bitmap(
                    outline.Width,
                    outline.Height))
                {
                    outline.SelectIdentity(procId, Guid.Empty, false);
                    using (Graphics graphics = Graphics.FromImage(before))
                    {
                        DrawOutline(outline, graphics);
                    }

                    InvokeFocusEvent(outline, "OnGotFocus");
                    InvokeFocusEvent(outline, "OnLostFocus");
                    using (Graphics graphics = Graphics.FromImage(after))
                    {
                        DrawOutline(outline, graphics);
                    }

                    Assert.AreEqual(
                        procId,
                        outline.SelectedOutlineItem?.ProcId);
                    Assert.AreEqual(
                        0,
                        CountDifferentPixels(before, after),
                        "焦点转移不应改变流程导航画面。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void SelectedProcessRow_UsesLightFullRowBackgroundAndAccent()
        {
            StaTestRunner.Run(() =>
            {
                using (var outline = CreateDrawingOutline(
                    out ImageList images,
                    out Guid procId,
                    out _))
                using (images)
                using (var bitmap = new Bitmap(outline.Width, outline.Height))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    outline.SelectIdentity(procId, Guid.Empty, false);
                    DrawOutline(outline, graphics);

                    Color rowBackground = bitmap.GetPixel(
                        bitmap.Width - 8,
                        outline.ItemHeight / 2);
                    Assert.AreEqual(
                        UiPalette.Selection.ToArgb(),
                        rowBackground.ToArgb());
                    Color accent = bitmap.GetPixel(1, outline.ItemHeight / 2);
                    Assert.AreEqual(UiPalette.Brand.ToArgb(), accent.ToArgb());
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void StepRow_UsesSixPixelCompactIndentInEveryState()
        {
            StaTestRunner.Run(() =>
            {
                using (var outline = CreateDrawingOutline(
                    out ImageList images,
                    out _,
                    out _))
                using (images)
                {
                    int rootImageLeft;
                    int stepImageLeft;
                    using (var bitmap = new Bitmap(outline.Width, outline.Height))
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        DrawOutline(outline, graphics);
                        rootImageLeft = FindFirstMarkerColumn(
                            bitmap,
                            0,
                            outline.ItemHeight);
                        stepImageLeft = FindFirstMarkerColumn(
                            bitmap,
                            outline.ItemHeight,
                            outline.ItemHeight * 2);
                    }

                    Assert.AreEqual(8, rootImageLeft);
                    Assert.AreEqual(
                        rootImageLeft + 6,
                        stepImageLeft,
                        "步骤只应保留 6px 层级错位。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ProcessOutline_UsesCompactLayoutAndReadableDisabledText()
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

                    ProcessOutlineList outline = main.frmProc.processOutline;
                    Assert.AreEqual(25, outline.ItemHeight);
                    Assert.AreEqual(new Size(18, 18), outline.StateImages.ImageSize);
                    Assert.IsTrue(outline.ProcessFont.Size <= 11F);
                    Assert.IsTrue(outline.StepFont.Size <= 10.5F);
                    Assert.IsTrue(outline.TryGetProcess(
                        process.head.Id,
                        out ProcessOutlineItem processItem));
                    Assert.IsTrue(outline.TryGetStep(
                        process.head.Id,
                        process.steps[0].Id,
                        out ProcessOutlineItem stepItem));
                    Assert.AreEqual(
                        UiPalette.TextMuted.ToArgb(),
                        processItem.ForeColor.ToArgb());
                    Assert.AreEqual(
                        UiPalette.TextMuted.ToArgb(),
                        stepItem.ForeColor.ToArgb());
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ReadyAndRunningIcons_UseClearlyDifferentStatusColors()
        {
            using (Bitmap ready = ProcessOutlineIconFactory.Create(
                ProcessOutlineIconKind.Ready,
                20))
            using (Bitmap running = ProcessOutlineIconFactory.Create(
                ProcessOutlineIconKind.Running,
                20))
            {
                Assert.IsTrue(
                    CountColor(ready, UiPalette.BrandAccent) > 0);
                Assert.IsTrue(
                    CountColor(running, UiPalette.Success) > 0);
                Assert.AreNotEqual(
                    UiPalette.BrandAccent.ToArgb(),
                    UiPalette.Success.ToArgb());
            }
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ReadyProcessText_StaysNormalUntilProcessIsRunning()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = CreateProcessWithSteps(
                        "未运行流程",
                        "待机步骤");
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { process });
                    main.frmProc.RefreshProcListFromStore();

                    Assert.IsTrue(main.frmProc.processOutline.TryGetProcess(
                        process.head.Id,
                        out ProcessOutlineItem item));
                    Assert.AreEqual(
                        UiPalette.TextPrimary.ToArgb(),
                        item.ForeColor.ToArgb(),
                        "流程未运行时文字必须使用正常黑色。");

                    main.frmProc.UpdateProcessSnapshot(new EngineSnapshot(
                        0,
                        process.head.Id,
                        process.head.Name,
                        ProcRunState.Running,
                        0,
                        0,
                        false,
                        string.Empty,
                        DateTime.UtcNow,
                        1));

                    Assert.AreEqual(
                        UiPalette.Success.ToArgb(),
                        item.ForeColor.ToArgb(),
                        "只有运行中的流程文字才显示绿色。");
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void RepeatedEquivalentSnapshot_DoesNotRequestAnotherOutlineRepaint()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    Proc process = CreateProcessWithSteps(
                        "静止刷新",
                        "运行步骤");
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { process });
                    main.frmProc.RefreshProcListFromStore();

                    var running = new EngineSnapshot(
                        0,
                        process.head.Id,
                        process.head.Name,
                        ProcRunState.Running,
                        0,
                        0,
                        false,
                        string.Empty,
                        DateTime.UtcNow,
                        1);
                    var nextPoll = new EngineSnapshot(
                        0,
                        process.head.Id,
                        process.head.Name,
                        ProcRunState.Running,
                        0,
                        3,
                        false,
                        string.Empty,
                        DateTime.UtcNow.AddMilliseconds(100),
                        2);

                    Assert.IsTrue(main.frmProc.UpdateProcessSnapshot(running));
                    Assert.IsFalse(
                        main.frmProc.UpdateProcessSnapshot(nextPoll),
                        "仅轮询时间或当前指令变化、显示内容未变化时不得重绘流程导航。");
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshProcessView_PreservesStableStepSelectionAcrossInsertAndReorder()
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
                    Guid selectedStepId = process.steps[0].Id;
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { process });
                    main.frmProc.RefreshProcListFromStore();
                    Assert.IsTrue(main.frmProc.TrySelectProcessStep(0, 0));

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

                    Assert.AreEqual(
                        selectedStepId,
                        main.frmProc.processOutline.SelectedOutlineItem?.StepId,
                        "步骤插入或重排后应按稳定 ID 保留选择。");
                    Assert.AreEqual(2, main.frmProc.SelectedStepNum);
                    Assert.IsTrue(
                        main.frmProc.processOutline.ExpandedProcIds.Contains(
                            process.head.Id));
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshProcessList_RebindsOperationsForStableStepSelection()
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
                    Guid stepId = process.steps[0].Id;
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { process });
                    main.frmProc.RefreshProcListFromStore();
                    Assert.IsTrue(main.frmProc.TrySelectProcessStep(0, 0));
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

                    Assert.AreEqual(
                        stepId,
                        main.frmProc.processOutline.SelectedOutlineItem?.StepId);
                    Assert.AreEqual(0, main.frmProc.SelectedProcNum);
                    Assert.AreEqual(0, main.frmProc.SelectedStepNum);
                    Assert.AreEqual(
                        1,
                        main.frmDataGrid.dataGridView1.OperationCount);
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
                    Assert.IsTrue(main.frmProc.TrySelectProcessStep(0, 0));

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

                    Assert.AreEqual(1, grid.OperationCount);
                    Assert.AreEqual(
                        operationId,
                        grid.GetOperation(0)?.Id);
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshProcessList_PreservesSelectionWhenProcessOrderChanges()
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
                    Guid retainedStepId = retained.steps[0].Id;
                    Guid retainedOperationId = Guid.NewGuid();
                    retained.steps[0].Ops.Add(new EndProcess
                    {
                        Id = retainedOperationId,
                        Name = "稳定指令"
                    });
                    main.Runtime.Stores.Processes.ReplaceAll(
                        new[] { retained });
                    main.frmProc.RefreshProcListFromStore();
                    Assert.IsTrue(main.frmProc.TrySelectProcessStep(0, 0));

                    Proc inserted = CreateProcessWithSteps(
                        "插入流程",
                        "插入步骤");
                    main.Runtime.Stores.Processes.ReplaceAll(new[]
                    {
                        inserted,
                        ObjectGraphCloner.Clone(retained)
                    });
                    main.frmProc.RefreshProcListFromStore();

                    Assert.AreEqual(
                        retained.head.Id,
                        main.frmProc.processOutline.SelectedOutlineItem?.ProcId);
                    Assert.AreEqual(
                        retainedStepId,
                        main.frmProc.processOutline.SelectedOutlineItem?.StepId);
                    Assert.AreEqual(1, main.frmProc.SelectedProcNum);
                    Assert.AreEqual(0, main.frmProc.SelectedStepNum);
                    Assert.AreEqual(
                        retainedOperationId,
                        main.frmDataGrid.dataGridView1.GetOperation(0)?.Id);
                    Assert.IsTrue(
                        main.frmProc.processOutline.ExpandedProcIds.Contains(
                            retained.head.Id));
                }
            }, TimeSpan.FromSeconds(20));
        }

        private static ProcessOutlineList CreateDrawingOutline(
            out ImageList images,
            out Guid procId,
            out Guid stepId)
        {
            procId = Guid.NewGuid();
            stepId = Guid.NewGuid();
            var marker = new Bitmap(18, 18);
            using (Graphics markerGraphics = Graphics.FromImage(marker))
            {
                markerGraphics.Clear(Color.Magenta);
            }
            images = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(18, 18),
                TransparentColor = Color.Transparent
            };
            images.Images.Add("marker", marker);
            _ = images.Handle;
            marker.Dispose();

            var outline = new ProcessOutlineList
            {
                Size = new Size(280, 100),
                StateImages = images,
                ProcessFont = ProcessPageFont.Create(11F, FontStyle.Bold),
                StepFont = ProcessPageFont.Create(10.5F, FontStyle.Regular),
                BackColor = UiPalette.Background,
                ForeColor = UiPalette.TextPrimary
            };
            outline.ReplaceItems(
                new[]
                {
                    new ProcessOutlineItem
                    {
                        ProcId = procId,
                        ProcIndex = 0,
                        StepIndex = -1,
                        Text = "0：流程",
                        ImageKey = "marker",
                        ForeColor = UiPalette.TextPrimary,
                        HasChildren = true
                    },
                    new ProcessOutlineItem
                    {
                        ProcId = procId,
                        StepId = stepId,
                        ProcIndex = 0,
                        StepIndex = 0,
                        Text = "0：步骤",
                        ImageKey = "marker",
                        ForeColor = UiPalette.TextPrimary
                    }
                },
                new[] { procId },
                Guid.Empty,
                Guid.Empty,
                ProcessOutlineScrollAnchor.Empty);
            return outline;
        }

        private static IReadOnlyList<ProcessOutlineItem> CreateOutlineItems(
            int processCount,
            int stepsPerProcess,
            out Guid[] procIds)
        {
            procIds = new Guid[processCount];
            var items = new System.Collections.Generic.List<ProcessOutlineItem>();
            for (int procIndex = 0;
                procIndex < processCount;
                procIndex++)
            {
                Guid procId = Guid.NewGuid();
                procIds[procIndex] = procId;
                items.Add(new ProcessOutlineItem
                {
                    ProcId = procId,
                    ProcIndex = procIndex,
                    StepIndex = -1,
                    Text = procIndex + "：流程",
                    ForeColor = UiPalette.TextPrimary,
                    HasChildren = stepsPerProcess > 0
                });
                for (int stepIndex = 0;
                    stepIndex < stepsPerProcess;
                    stepIndex++)
                {
                    items.Add(new ProcessOutlineItem
                    {
                        ProcId = procId,
                        StepId = Guid.NewGuid(),
                        ProcIndex = procIndex,
                        StepIndex = stepIndex,
                        Text = stepIndex + "：步骤",
                        ForeColor = UiPalette.TextPrimary
                    });
                }
            }
            return items;
        }

        private static void DrawOutline(
            ProcessOutlineList outline,
            Graphics graphics)
        {
            MethodInfo paintMethod = typeof(ProcessOutlineList).GetMethod(
                "OnPaint",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "未找到流程导航绘制入口。");
            paintMethod.Invoke(outline, new object[]
            {
                new PaintEventArgs(
                    graphics,
                    new Rectangle(Point.Empty, outline.Size))
            });
        }

        private static void InvokeMouseDown(
            ProcessOutlineList outline,
            MouseEventArgs args)
        {
            MethodInfo mouseDown = typeof(ProcessOutlineList).GetMethod(
                "OnMouseDown",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "未找到鼠标按下事件入口。");
            mouseDown.Invoke(outline, new object[] { args });
        }

        private static void InvokeFocusEvent(
            ProcessOutlineList outline,
            string methodName)
        {
            MethodInfo focusEvent = typeof(Control).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "未找到焦点事件入口：" + methodName);
            focusEvent.Invoke(outline, new object[] { EventArgs.Empty });
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

        private static int FindFirstMarkerColumn(
            Bitmap bitmap,
            int top,
            int bottom)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                for (int y = Math.Max(0, top);
                    y < Math.Min(bitmap.Height, bottom);
                    y++)
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

        private static int CountDifferentPixels(
            Bitmap first,
            Bitmap second)
        {
            Assert.AreEqual(first.Size, second.Size);
            int count = 0;
            for (int x = 0; x < first.Width; x++)
            {
                for (int y = 0; y < first.Height; y++)
                {
                    if (first.GetPixel(x, y).ToArgb()
                        != second.GetPixel(x, y).ToArgb())
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
