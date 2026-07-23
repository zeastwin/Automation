// 模块：核心测试 / Inspector。
// 职责范围：验证变量引用方式切换与下拉字体不会被刷新逻辑破坏。

using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class InspectorValueReferenceInteractionTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void InspectorHeader_UsesBorderlessLabelAndWhiteContentCanvas()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmInspector())
                {
                    Label operationTypeLabel = GetPrivateField<Label>(
                        form,
                        "operationTypeLabel");
                    InspectorIconButton operationTypeButton =
                        GetPrivateField<InspectorIconButton>(
                            form,
                            "operationTypeButton");
                    InspectorView inspectorView = GetPrivateField<InspectorView>(
                        form,
                        "inspectorView");

                    Assert.AreEqual(BorderStyle.None, operationTypeLabel.BorderStyle);
                    Assert.AreEqual(UiPalette.SurfaceStrong, operationTypeLabel.BackColor);
                    Assert.IsTrue(operationTypeButton.Font.Bold);
                    Assert.AreEqual(UiPalette.TextPrimary, operationTypeButton.ForeColor);
                    Assert.AreEqual(UiPalette.SurfaceStrong, inspectorView.BackColor);
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void EditActions_AppearAboveOperationHeaderAndInspectorContent()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmInspector
                {
                    ClientSize = new Size(360, 520)
                })
                using (var saveButton = new Button { Text = "保存" })
                using (var cancelButton = new Button { Text = "取消" })
                {
                    form.AttachEditActions(saveButton, cancelButton);
                    InspectorView inspectorView = GetPrivateField<InspectorView>(
                        form,
                        "inspectorView");
                    Panel actionBar = GetPrivateField<Panel>(form, "actionBar");
                    Panel header = GetPrivateField<Panel>(form, "header");
                    Label operationTypeLabel = GetPrivateField<Label>(
                        form,
                        "operationTypeLabel");
                    InspectorIconButton operationTypeButton =
                        GetPrivateField<InspectorIconButton>(
                            form,
                            "operationTypeButton");
                    var operation = new ModifyValue();
                    inspectorView.SetObject(operation, true);
                    typeof(FrmInspector).GetMethod(
                        "UpdatePresentation",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(form, new object[] { operation });

                    Assert.AreEqual(0, actionBar.Top);
                    Assert.AreEqual(actionBar.Bottom, header.Top,
                        "指令类型应排列在顶部操作栏之后。");
                    Assert.AreEqual(header.Bottom, inspectorView.Top,
                        "Inspector 内容应排列在指令类型之后。");
                    Assert.AreEqual(form.ClientSize.Height, inspectorView.Bottom);
                    Assert.AreEqual(
                        operationTypeLabel.Right + 8,
                        operationTypeButton.Left,
                        "指令类型选择框应与属性值列的起始线对齐。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void SelectingSecondLevelIndex_KeepsFullSelectionAndUsesRegularOptionFont()
        {
            StaTestRunner.Run(() =>
            {
                var owner = new ModifyValue
                {
                    ValueSourceName = "已有变量"
                };
                PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(owner);
                var definition = new InspectorValueReferenceFieldDefinition
                {
                    Label = "源变量",
                    Owner = owner
                };
                definition.Add(
                    InspectorValueReferenceKind.Name,
                    properties[nameof(ModifyValue.ValueSourceName)]);
                definition.Add(
                    InspectorValueReferenceKind.Name2,
                    properties[nameof(ModifyValue.ValueSourceName2Index)]);
                definition.Add(
                    InspectorValueReferenceKind.Index,
                    properties[nameof(ModifyValue.ValueSourceIndex)]);
                definition.Add(
                    InspectorValueReferenceKind.Index2,
                    properties[nameof(ModifyValue.ValueSourceIndex2Index)]);

                using (var toolTip = new ToolTip())
                using (var control = new InspectorValueReferenceFieldControl(
                    definition,
                    true,
                    toolTip))
                using (var form = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(-10000, -10000),
                    ClientSize = new System.Drawing.Size(360, 120)
                })
                {
                    control.Dock = DockStyle.Top;
                    form.Controls.Add(control);
                    form.Show();
                    Application.DoEvents();

                    InspectorComboBox kind = GetPrivateField<InspectorComboBox>(control, "kind");
                    InspectorValueCell kindDisplay = GetPrivateField<InspectorValueCell>(
                        control,
                        "kindDisplay");
                    object indexItem = kind.Items.Cast<object>()
                        .Single(item => item.ToString() == "索引二级");
                    kind.SelectedItem = indexItem;
                    InvokeProtected(kind, "OnSelectionChangeCommitted");

                    Assert.AreEqual("索引二级", kindDisplay.DisplayText);
                    InvokeProtected(kind, "OnDropDownClosed");
                    Application.DoEvents();

                    Assert.AreEqual("索引二级", kindDisplay.DisplayText,
                        "空二级索引尚未填写时，下拉关闭不应把引用方式刷新回变量。");
                    Assert.IsNull(owner.ValueSourceName);
                    AssertFontEquals(
                        InspectorFonts.Bold95,
                        kind.Font,
                        "编辑状态的当前值应使用更醒目的粗体。");
                    Assert.IsTrue(kind.Font.Bold, "当前值必须使用系统字体的真实粗体字重。");
                    Assert.IsFalse(
                        kind.Font.FontFamily.Name.StartsWith(
                            "MiSans",
                            StringComparison.OrdinalIgnoreCase),
                        "Inspector 不应继续依赖外部 MiSans 字体文件。");
                    AssertFontEquals(
                        InspectorFonts.Regular9,
                        kind.ItemFont,
                        "下拉候选项应使用常规字体。");
                    Assert.IsFalse(kind.ItemFont.Bold, "下拉候选项不应加粗。");
                    AssertFontEquals(
                        InspectorFonts.Bold95,
                        kindDisplay.Font,
                        "非编辑状态的当前值应使用更醒目的粗体。");
                    int textWidth = TextRenderer.MeasureText(
                        kindDisplay.DisplayText,
                        kindDisplay.Font,
                        Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                    Assert.IsTrue(
                        kindDisplay.Width >= textWidth + 37,
                        "引用方式列应完整容纳二级索引文字和下拉箭头。");
                    Assert.AreEqual(
                        UiPalette.SurfaceStrong,
                        kindDisplay.BackColor,
                        "属性值区域应使用清晰的白色内容底。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void GotoAddressDrop_WorksOnVisibleDisplayCell()
        {
            StaTestRunner.Run(() =>
            {
                var operation = new IoLogicGoto();
                var definition = new InspectorScalarFieldDefinition
                {
                    Label = "true跳转",
                    Owner = operation,
                    Property = TypeDescriptor.GetProperties(operation)[nameof(IoLogicGoto.TrueGoto)]
                };
                using (var toolTip = new ToolTip())
                using (var control = new InspectorScalarFieldControl(definition, true, toolTip))
                {
                    InspectorValueCell displayCell = GetPrivateField<InspectorValueCell>(
                        control,
                        "displayCell");
                    Assert.IsTrue(displayCell.Visible);
                    Assert.IsTrue(displayCell.AllowDrop,
                        "可见的字段展示层必须能够接收指令地址。");

                    var data = new DataObject();
                    data.SetData(FrmDataGrid.OperationAddressDragFormat, "1-2-3");
                    InvokeProtected(
                        displayCell,
                        "OnDragDrop",
                        new DragEventArgs(
                            data,
                            0,
                            0,
                            0,
                            DragDropEffects.Copy,
                            DragDropEffects.Copy));

                    Assert.AreEqual("1-2-3", operation.TrueGoto);
                    Assert.AreEqual("1-2-3", displayCell.DisplayText);
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void SelectionPickerFields_AllowManualTextEntry()
        {
            StaTestRunner.Run(() =>
            {
                var gotoOperation = new IoLogicGoto();
                var gotoDefinition = new InspectorScalarFieldDefinition
                {
                    Label = "true跳转",
                    Owner = gotoOperation,
                    Property = TypeDescriptor.GetProperties(gotoOperation)[
                        nameof(IoLogicGoto.TrueGoto)]
                };
                var valueOperation = new ModifyValue();
                PropertyDescriptorCollection valueProperties =
                    TypeDescriptor.GetProperties(valueOperation);
                var valueDefinition = new InspectorValueReferenceFieldDefinition
                {
                    Label = "源变量",
                    Owner = valueOperation
                };
                valueDefinition.Add(
                    InspectorValueReferenceKind.Name,
                    valueProperties[nameof(ModifyValue.ValueSourceName)]);

                using (var toolTip = new ToolTip())
                using (var gotoControl = new InspectorScalarFieldControl(
                    gotoDefinition,
                    true,
                    toolTip))
                using (var valueControl = new InspectorValueReferenceFieldControl(
                    valueDefinition,
                    true,
                    toolTip))
                {
                    InspectorComboBox gotoEditor =
                        GetPrivateField<InspectorComboBox>(gotoControl, "editor");
                    Assert.AreEqual(ComboBoxStyle.DropDown, gotoEditor.DropDownStyle);
                    Assert.IsTrue(gotoControl.FocusEditor());
                    gotoEditor.Text = "2-3-4";
                    InvokeProtected(gotoEditor, "OnValidated");
                    Assert.AreEqual("2-3-4", gotoOperation.TrueGoto);

                    InspectorComboBox valueEditor =
                        GetPrivateField<InspectorComboBox>(valueControl, "value");
                    Assert.AreEqual(ComboBoxStyle.DropDown, valueEditor.DropDownStyle);
                    Assert.IsTrue(valueControl.FocusEditor());
                    valueEditor.Text = "手工变量";
                    InvokeProtected(valueEditor, "OnValidated");
                    Assert.AreEqual("手工变量", valueOperation.ValueSourceName);
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ReadOnlyToggle_StillDistinguishesOnFromOff()
        {
            StaTestRunner.Run(() =>
            {
                using (var enabledBitmap = new Bitmap(38, 24))
                using (var disabledBitmap = new Bitmap(38, 24))
                using (var toggle = new InspectorToggle
                {
                    Size = new Size(38, 24),
                    Enabled = false
                })
                {
                    toggle.Checked = true;
                    toggle.DrawToBitmap(enabledBitmap, new Rectangle(Point.Empty, toggle.Size));
                    toggle.Checked = false;
                    toggle.DrawToBitmap(disabledBitmap, new Rectangle(Point.Empty, toggle.Size));

                    Assert.IsTrue(
                        CountColor(enabledBitmap, UiPalette.Brand) > 0,
                        "只读的开启状态仍应保留品牌色。");
                    Assert.AreEqual(
                        0,
                        CountColor(disabledBitmap, UiPalette.Brand),
                        "只读的关闭状态不应与开启状态同色。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void Toolbar_ExposesDedicatedContinueButtonForSingleStep()
        {
            StaTestRunner.Run(() =>
            {
                using (var toolbar = new FrmToolBar())
                {
                    Button continueButton = GetPrivateField<Button>(
                        toolbar,
                        "btnContinue");

                    toolbar.ApplyProcessRunState(ProcRunState.SingleStep);
                    Assert.IsTrue(continueButton.Enabled);
                    Assert.IsFalse(toolbar.btnPause.Enabled);
                    Assert.IsTrue(toolbar.SingleRun.Enabled);

                    toolbar.ApplyProcessRunState(ProcRunState.Running);
                    Assert.IsFalse(continueButton.Enabled);
                    Assert.IsTrue(toolbar.btnPause.Enabled);
                    Assert.IsFalse(toolbar.SingleRun.Enabled);
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ContinueConfirmationMessage_LeavesAllLargeTextAboveButtonArea()
        {
            StaTestRunner.Run(() =>
            {
                using (var dialog = new Message
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000)
                })
                {
                    dialog.txtMsg.Font = new Font("微软雅黑", 16F, FontStyle.Bold);
                    dialog.txtMsg.Text =
                        "是否从地址 0-0-0 开始全速运行？\r\n\r\n流程：1\r\n指令：0(CT探针)";
                    dialog.PresentDeferred(false);
                    Application.DoEvents();

                    Point lastLinePosition = dialog.txtMsg.GetPositionFromCharIndex(
                        dialog.txtMsg.Text.Length - 1);
                    int lineHeight = TextRenderer.MeasureText(
                        "中Ag",
                        dialog.txtMsg.Font,
                        Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;

                    Assert.IsTrue(
                        lastLinePosition.Y + lineHeight <= dialog.txtMsg.ClientSize.Height,
                        $"继续运行确认内容的最后一行不应被底部按钮区遮挡。"
                        + $" lastY={lastLinePosition.Y}, lineHeight={lineHeight},"
                        + $" textHeight={dialog.txtMsg.ClientSize.Height}, dialogHeight={dialog.ClientSize.Height}");
                    Assert.AreEqual(RichTextBoxScrollBars.None, dialog.txtMsg.ScrollBars);
                }
            }, TimeSpan.FromSeconds(10));
        }

        private static T GetPrivateField<T>(object owner, string name)
            where T : class
        {
            return owner.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(owner) as T
                ?? throw new InvalidOperationException($"未找到字段：{name}");
        }

        private static void InvokeProtected(Control control, string methodName)
        {
            MethodInfo method = control.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? typeof(ComboBox).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"未找到方法：{methodName}");
            method.Invoke(control, new object[] { EventArgs.Empty });
        }

        private static void InvokeProtected(Control control, string methodName, EventArgs eventArgs)
        {
            MethodInfo method = control.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? typeof(Control).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"未找到方法：{methodName}");
            method.Invoke(control, new object[] { eventArgs });
        }

        private static void AssertFontEquals(Font expected, Font actual, string message)
        {
            Assert.AreEqual(expected.FontFamily.Name, actual.FontFamily.Name, message);
            Assert.AreEqual(expected.Style, actual.Style, message);
            Assert.AreEqual(expected.Size, actual.Size, message);
        }

        private static int CountColor(Bitmap bitmap, Color color)
        {
            int count = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() == color.ToArgb())
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
