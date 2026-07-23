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
                    InspectorView inspectorView = GetPrivateField<InspectorView>(
                        form,
                        "inspectorView");

                    Assert.AreEqual(BorderStyle.None, operationTypeLabel.BorderStyle);
                    Assert.AreEqual(UiPalette.SurfaceStrong, operationTypeLabel.BackColor);
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

        private static void AssertFontEquals(Font expected, Font actual, string message)
        {
            Assert.AreEqual(expected.FontFamily.Name, actual.FontFamily.Name, message);
            Assert.AreEqual(expected.Style, actual.Style, message);
            Assert.AreEqual(expected.Size, actual.Size, message);
        }
    }
}
