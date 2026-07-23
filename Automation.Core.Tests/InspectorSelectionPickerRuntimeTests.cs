using System;
// 模块：核心测试 / Inspector。
// 职责范围：验证动态集合项继承编辑器运行时，并能读取变量与 IO 选择数据。

using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
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
    }
}
