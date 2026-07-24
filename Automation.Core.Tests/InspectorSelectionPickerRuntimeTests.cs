using System;
// 模块：核心测试 / Inspector。
// 职责范围：验证动态集合项继承编辑器运行时，并能读取变量与 IO 选择数据。

using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class InspectorSelectionPickerRuntimeTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void AddressPicker_StepHeadersUseZeroBasedIndexes()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var form = new FrmMain(
                    new PlatformRuntime(directory.FullPath)))
                {
                    var process = new Proc
                    {
                        head = new ProcHead
                        {
                            Id = Guid.NewGuid(),
                            Name = "零基步骤标题"
                        }
                    };
                    process.steps.Add(new Step
                    {
                        Id = Guid.NewGuid(),
                        Name = "等待信号"
                    });
                    process.steps.Add(new Step
                    {
                        Id = Guid.NewGuid(),
                        Name = string.Empty
                    });
                    form.Runtime.Stores.Processes.ReplaceAll(
                        new[] { process });
                    form.frmProc.SelectedProcNum = 0;
                    form.frmProc.SelectedStepNum = 0;

                    var operation = new IoLogicGoto();
                    EditorServiceRegistry.AttachGraph(operation, form.Runtime);
                    IReadOnlyList<PickerGroupDefinition> groups =
                        InspectorSelectionPickerData.Build(
                            InspectorSelectionPickerKind.Address,
                            operation,
                            TypeDescriptor.GetProperties(operation)[
                                nameof(IoLogicGoto.TrueGoto)],
                            null);

                    CollectionAssert.AreEqual(
                        new[] { "0. 等待信号", "步骤 1" },
                        groups.Select(group => group.Title).ToArray(),
                        "Inspector 地址选择页的步骤标题应与零基步骤地址保持一致。");
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void AddedPauseItems_InheritRuntimeAndExposeVariableAndIoChoices()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                {
                    var runtime = new PlatformRuntime(directory.FullPath);
                    var variables = new Dictionary<string, DicValue>
                    {
                        ["公共测试变量"] = new DicValue
                        {
                            Id = Guid.NewGuid(),
                            Index = 10,
                            Name = "公共测试变量",
                            Type = "double",
                            Value = "0",
                            Scope = VariableScopeContract.Public
                        }
                    };
                    Assert.IsTrue(runtime.Stores.Values.TryCommitConfiguration(
                        runtime.Paths.ConfigPath,
                        variables,
                        out string variableError), variableError);
                    Assert.IsTrue(runtime.Stores.IoConfiguration.TryReplaceMap(
                        new[]
                        {
                            new List<IO>
                            {
                                new IO
                                {
                                    Name = "输入测试IO",
                                    IOType = "通用输入"
                                }
                            }
                        },
                        out string ioError), ioError);

                    var processHead = new ProcHead();
                    EditorServiceRegistry.AttachGraph(processHead, runtime);

                    using (var toolTip = new ToolTip())
                    using (InspectorCollectionFieldControl variableControl = CreateCollectionControl(
                        processHead,
                        nameof(ProcHead.PauseValueParams),
                        "暂停变量",
                        toolTip))
                    using (InspectorCollectionFieldControl ioControl = CreateCollectionControl(
                        processHead,
                        nameof(ProcHead.PauseIoParams),
                        "暂停IO",
                        toolTip))
                    {
                        ClickAdd(variableControl);
                        ClickAdd(ioControl);

                        PauseValueParam pauseVariable = processHead.PauseValueParams[0];
                        PauseIoParam pauseIo = processHead.PauseIoParams[0];
                        Assert.AreSame(runtime, EditorServiceRegistry.GetRuntime(pauseVariable));
                        Assert.AreSame(runtime, EditorServiceRegistry.GetRuntime(pauseIo));

                        IReadOnlyList<PickerGroupDefinition> variableGroups =
                            InspectorSelectionPickerData.Build(
                                InspectorSelectionPickerKind.Variable,
                                pauseVariable,
                                TypeDescriptor.GetProperties(pauseVariable)[nameof(PauseValueParam.ValueName)],
                                null);
                        Assert.IsTrue(variableGroups
                            .Single(group => group.Title == "公共变量")
                            .Choices.Any(choice => choice.Value == "公共测试变量"));
                        CollectionAssert.AreEqual(
                            new[] { "当前流程私有变量", "公共变量", "系统变量" },
                            variableGroups.Select(group => group.Title).ToArray());

                        IReadOnlyList<PickerGroupDefinition> ioGroups =
                            InspectorSelectionPickerData.Build(
                                InspectorSelectionPickerKind.InputOutput,
                                pauseIo,
                                TypeDescriptor.GetProperties(pauseIo)[nameof(PauseIoParam.IoName)],
                                null);
                        Assert.IsTrue(ioGroups
                            .Single(group => group.Title == "输入 IO")
                            .Choices.Any(choice => choice.Value == "输入测试IO"));
                    }
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        public void PickerNearBottom_IsPlacedAboveAndInsideWorkingArea()
        {
            Rectangle anchor = new Rectangle(900, 980, 240, 28);
            Rectangle workingArea = new Rectangle(0, 0, 1200, 1024);
            Size popup = new Size(360, 420);

            Point location = InspectorSelectionPickerDropDown.CalculatePopupLocation(
                anchor,
                workingArea,
                popup,
                true);
            Rectangle popupBounds = new Rectangle(
                anchor.Left + location.X,
                anchor.Top + location.Y,
                popup.Width,
                popup.Height);

            Assert.IsTrue(popupBounds.Bottom <= anchor.Top);
            Assert.IsTrue(popupBounds.Left >= workingArea.Left);
            Assert.IsTrue(popupBounds.Right <= workingArea.Right);
            Assert.IsTrue(popupBounds.Top >= workingArea.Top);
        }

        [TestMethod]
        public void PickerCatalog_ReusesVersionedProjectionAndInvalidatesAfterConfigurationChange()
        {
            using (var directory = new TemporaryDirectory())
            {
                var runtime = new PlatformRuntime(directory.FullPath);
                var variables = new Dictionary<string, DicValue>
                {
                    ["变量一"] = new DicValue
                    {
                        Id = Guid.NewGuid(),
                        Index = 1,
                        Name = "变量一",
                        Type = "double",
                        Value = "0",
                        Scope = VariableScopeContract.Public
                    }
                };
                Assert.IsTrue(runtime.Stores.Values.TryCommitConfiguration(
                    runtime.Paths.ConfigPath,
                    variables,
                    out string firstError), firstError);
                var operation = new ModifyValue();
                EditorServiceRegistry.AttachGraph(operation, runtime);
                PropertyDescriptor property = TypeDescriptor.GetProperties(operation)[
                    nameof(ModifyValue.ValueSourceName)];

                IReadOnlyList<PickerGroupDefinition> first =
                    InspectorSelectionPickerData.Build(
                        InspectorSelectionPickerKind.Variable,
                        operation,
                        property,
                        null);
                IReadOnlyList<PickerGroupDefinition> second =
                    InspectorSelectionPickerData.Build(
                        InspectorSelectionPickerKind.Variable,
                        operation,
                        property,
                        null);
                Assert.AreSame(
                    first,
                    second,
                    "配置版本未变化时应直接复用选择目录。");

                variables["变量二"] = new DicValue
                {
                    Id = Guid.NewGuid(),
                    Index = 2,
                    Name = "变量二",
                    Type = "double",
                    Value = "0",
                    Scope = VariableScopeContract.Public
                };
                Assert.IsTrue(runtime.Stores.Values.TryCommitConfiguration(
                    runtime.Paths.ConfigPath,
                    variables,
                    out string secondError), secondError);
                IReadOnlyList<PickerGroupDefinition> changed =
                    InspectorSelectionPickerData.Build(
                        InspectorSelectionPickerKind.Variable,
                        operation,
                        property,
                        null);

                Assert.AreNotSame(first, changed);
                Assert.IsTrue(changed
                    .Single(group => group.Title == "公共变量")
                    .Choices.Any(choice => choice.Value == "变量二"));
            }
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void EnteringInspectorEditMode_PrewarmsPickerAndExitReleasesCache()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var view = new InspectorView { Dock = DockStyle.Fill })
                using (var host = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(420, 560)
                })
                {
                    var runtime = new PlatformRuntime(directory.FullPath);
                    var variables = new Dictionary<string, DicValue>
                    {
                        ["预热变量"] = new DicValue
                        {
                            Id = Guid.NewGuid(),
                            Index = 12,
                            Name = "预热变量",
                            Type = "double",
                            Value = "0",
                            Scope = VariableScopeContract.Public
                        }
                    };
                    Assert.IsTrue(runtime.Stores.Values.TryCommitConfiguration(
                        runtime.Paths.ConfigPath,
                        variables,
                        out string variableError), variableError);
                    Assert.IsTrue(runtime.Stores.IoConfiguration.TryReplaceMap(
                        new[]
                        {
                            new List<IO>
                            {
                                new IO
                                {
                                    Name = "预热输入IO",
                                    IOType = "通用输入"
                                }
                            }
                        },
                        out string ioError), ioError);

                    var operation = new ModifyValue();
                    EditorServiceRegistry.AttachGraph(operation, runtime);
                    host.Controls.Add(view);
                    host.Show();
                    view.SetObject(operation, true);
                    Application.DoEvents();

                    InspectorSelectionPickerPrewarmSession session =
                        GetPrivateField<InspectorSelectionPickerPrewarmSession>(
                            view,
                            "selectionPickerPrewarmSession");
                    Assert.IsTrue(session.PreparedCount > 0,
                        "进入 Inspector 编辑态后应在首次点击前完成选择器预热。");

                    PropertyDescriptor variableProperty =
                        TypeDescriptor.GetProperties(operation)[
                            nameof(ModifyValue.ValueSourceName)];
                    InspectorSelectionPickerPanel prepared = session.Take(
                        InspectorSelectionPickerKind.Variable,
                        operation,
                        variableProperty,
                        null);
                    Assert.IsNotNull(prepared);
                    Assert.IsTrue(prepared.IsHandleCreated,
                        "预热面板应提前创建控件句柄，点击时只需定位和显示。");
                    prepared.Dispose();

                    var ioGoto = new IoLogicGoto();
                    EditorServiceRegistry.AttachGraph(ioGoto, runtime);
                    view.SetObject(ioGoto, true);
                    Application.DoEvents();

                    IoLogicGotoParam ioParam = ioGoto.IoParams[0];
                    PropertyDescriptor ioProperty =
                        TypeDescriptor.GetProperties(ioParam)[
                            nameof(IoLogicGotoParam.IoName)];
                    using (InspectorSelectionPickerPanel ioPanel = session.Take(
                        InspectorSelectionPickerKind.InputOutput,
                        ioParam,
                        ioProperty,
                        null))
                    {
                        Assert.IsNotNull(ioPanel,
                            "进入编辑态时应提前生成 IO 选择页。");
                        Assert.IsTrue(ioPanel.IsHandleCreated);
                    }

                    PropertyDescriptor addressProperty =
                        TypeDescriptor.GetProperties(ioGoto)[
                            nameof(IoLogicGoto.TrueGoto)];
                    using (InspectorSelectionPickerPanel addressPanel = session.Take(
                        InspectorSelectionPickerKind.Address,
                        ioGoto,
                        addressProperty,
                        null))
                    {
                        Assert.IsNotNull(addressPanel,
                            "进入编辑态时应提前生成跳转地址选择页。");
                        Assert.IsTrue(addressPanel.IsHandleCreated);
                    }

                    view.SetEditable(false);
                    Assert.AreEqual(0, session.PreparedCount,
                        "退出编辑态后应释放预热面板。");

                    view.SetObject(ioGoto, true);
                    Application.DoEvents();
                    Assert.IsTrue(session.PreparedCount > 0,
                        "同一对象从只读态重新进入编辑态时应重新建立预热缓存。");
                    view.SetEditable(false);
                }
            }, TimeSpan.FromSeconds(15));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void CollectionRefresh_ReusesItemControlsAcrossAddAndReorder()
        {
            StaTestRunner.Run(() =>
            {
                var processHead = new ProcHead();
                var first = new PauseIoParam { IoName = "输入一" };
                var second = new PauseIoParam { IoName = "输入二" };
                processHead.PauseIoParams.Add(first);
                processHead.PauseIoParams.Add(second);

                using (var toolTip = new ToolTip())
                using (InspectorCollectionFieldControl control =
                    CreateCollectionControl(
                        processHead,
                        nameof(ProcHead.PauseIoParams),
                        "暂停IO",
                        toolTip))
                {
                    InspectorFlowPanel itemsPanel =
                        GetPrivateField<InspectorFlowPanel>(
                            control,
                            "itemsPanel");
                    InspectorCollectionItemControl firstControl =
                        itemsPanel.Controls
                            .OfType<InspectorCollectionItemControl>()
                            .ElementAt(0);
                    InspectorCollectionItemControl secondControl =
                        itemsPanel.Controls
                            .OfType<InspectorCollectionItemControl>()
                            .ElementAt(1);

                    processHead.PauseIoParams.RemoveAt(0);
                    processHead.PauseIoParams.Add(first);
                    control.RefreshValue();

                    Assert.AreSame(
                        secondControl,
                        itemsPanel.Controls[0],
                        "集合项移动时应移动既有控件。");
                    Assert.AreSame(firstControl, itemsPanel.Controls[1]);

                    processHead.PauseIoParams.Insert(
                        1,
                        new PauseIoParam { IoName = "输入三" });
                    control.RefreshValue();

                    Assert.AreSame(secondControl, itemsPanel.Controls[0]);
                    Assert.AreSame(firstControl, itemsPanel.Controls[2]);
                    Assert.AreEqual(3, itemsPanel.Controls.Count);
                }
            }, TimeSpan.FromSeconds(10));
        }

        private static InspectorCollectionFieldControl CreateCollectionControl(
            ProcHead owner,
            string propertyName,
            string label,
            ToolTip toolTip)
        {
            return new InspectorCollectionFieldControl(
                new InspectorCollectionFieldDefinition
                {
                    Label = label,
                    Owner = owner,
                    Property = TypeDescriptor.GetProperties(owner)[propertyName]
                },
                true,
                toolTip);
        }

        private static void ClickAdd(InspectorCollectionFieldControl control)
        {
            InspectorIconButton addButton = control.Controls
                .OfType<InspectorIconButton>()
                .Single(button => button.Text == "添加");
            addButton.PerformClick();
        }

        private static T GetPrivateField<T>(object owner, string fieldName)
            where T : class
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"未找到字段：{fieldName}");
            return field.GetValue(owner) as T
                ?? throw new InvalidOperationException($"字段类型不正确：{fieldName}");
        }
    }
}
