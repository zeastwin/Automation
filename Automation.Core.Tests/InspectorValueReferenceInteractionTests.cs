// 模块：核心测试 / Inspector。
// 职责范围：验证变量引用方式切换与下拉字体不会被刷新逻辑破坏。

using System;
using System.Collections.Generic;
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
        public void EditableComboBox_AllowsFullMouseSelection()
        {
            StaTestRunner.Run(() =>
            {
                using (var comboBox = new InspectorComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDown,
                    Text = "从右向左完整选择",
                    Size = new Size(240, 28)
                })
                using (var form = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000)
                })
                {
                    form.Controls.Add(comboBox);
                    form.Show();
                    comboBox.Focus();
                    comboBox.SelectionStart = 0;
                    comboBox.SelectionLength = comboBox.Text.Length;

                    InvokeProtected(
                        comboBox,
                        "OnMouseUp",
                        new MouseEventArgs(MouseButtons.Left, 1, 4, 12, 0));

                    Assert.AreEqual(comboBox.Text.Length, comboBox.SelectionLength,
                        "鼠标完成整段选择后不得擅自清除选区。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void ModifyValue_CombinesAlternativesAndFragmentedSections()
        {
            var operation = new ModifyValue
            {
                ChangeValue = "1"
            };

            InspectorDocument document = InspectorDefinitionBuilder.Build(operation);
            InspectorSectionDefinition parameterSection = document.Sections.Single(section =>
                section.Key == "参数");
            Assert.IsFalse(document.Sections.Any(section =>
                section.Key == "源变量"
                || section.Key == "修改参数"
                || section.Key == "保存参数"));

            InspectorValueReferenceFieldDefinition changeValue =
                parameterSection.Fields
                    .OfType<InspectorValueReferenceFieldDefinition>()
                    .Single(field => field.Label == "修改值");
            CollectionAssert.Contains(
                changeValue.AvailableKinds.ToList(),
                InspectorValueReferenceKind.Fixed);
            CollectionAssert.Contains(
                changeValue.AvailableKinds.ToList(),
                InspectorValueReferenceKind.Name);
            Assert.AreEqual(
                InspectorValueReferenceKind.Fixed,
                changeValue.GetCurrentKind());

            changeValue.SetValue(InspectorValueReferenceKind.Name, "计数");
            Assert.IsNull(operation.ChangeValue);
            Assert.AreEqual("计数", operation.ChangeValueName);
            Assert.AreEqual(
                InspectorValueReferenceKind.Name,
                changeValue.GetCurrentKind());

            changeValue.SetValue(InspectorValueReferenceKind.Fixed, "2");
            Assert.AreEqual("2", operation.ChangeValue);
            Assert.IsNull(operation.ChangeValueName);
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void OtherAlternativeSources_UseSameCompactPresentation()
        {
            InspectorDocument replaceDocument =
                InspectorDefinitionBuilder.Build(new Replace());
            InspectorSectionDefinition replaceParameters =
                replaceDocument.Sections.Single(section => section.Key == "参数");
            Assert.AreEqual(
                1,
                replaceParameters.Fields
                    .OfType<InspectorValueReferenceFieldDefinition>()
                    .Count(field => field.Label == "被替换字符串"));
            Assert.AreEqual(
                1,
                replaceParameters.Fields
                    .OfType<InspectorValueReferenceFieldDefinition>()
                    .Count(field => field.Label == "新字符串"));

            InspectorDocument trayDocument =
                InspectorDefinitionBuilder.Build(new TrayRunPos());
            InspectorSectionDefinition trayParameters =
                trayDocument.Sections.Single(section => section.Key == "参数");
            Assert.IsFalse(trayDocument.Sections.Any(section =>
                section.Key.Contains("固定值")
                || section.Key.Contains("变量读取")));
            Assert.AreEqual(
                2,
                trayParameters.Fields
                    .OfType<InspectorValueReferenceFieldDefinition>()
                    .Count(field => field.AvailableKinds.Contains(
                        InspectorValueReferenceKind.Fixed)));

            InspectorDocument speedDocument =
                InspectorDefinitionBuilder.Build(new SetStationVel());
            InspectorSectionDefinition speedSection =
                speedDocument.Sections.Single(section => section.Key == "速度设置");
            Assert.AreEqual(
                3,
                speedSection.Fields
                    .OfType<InspectorValueReferenceFieldDefinition>()
                    .Count(field => field.AvailableKinds.Contains(
                        InspectorValueReferenceKind.Fixed)));

            var gotoFields =
                InspectorDefinitionBuilder.BuildItemFields(
                    new GotoParam(),
                    "Params[0]");
            Assert.AreEqual(
                1,
                gotoFields.OfType<InspectorValueReferenceFieldDefinition>()
                    .Count(field => field.Label == "匹配值"));

            var insertFields =
                InspectorDefinitionBuilder.BuildItemFields(
                    new InsertDataStructItemParam(),
                    "Params[0]");
            Assert.AreEqual(
                1,
                insertFields.OfType<InspectorValueReferenceFieldDefinition>()
                    .Count(field => field.Label == "数据来源"));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void SetDataStructItem_UsesCascadingNameFirstLayoutAndFullValueSources()
        {
            var runtime = new PlatformRuntime();
            Assert.IsTrue(
                runtime.Stores.DataStructures.AddStruct("产品结构", out string error),
                error);
            Assert.IsTrue(
                runtime.Stores.DataStructures.CreateItem(
                    0, "当前产品", 0, out int itemIndex, out error),
                error);
            Assert.IsTrue(
                runtime.Stores.DataStructures.AddField(
                    0,
                    itemIndex,
                    "计数",
                    DataStructValueType.Number,
                    "0",
                    0,
                    out _,
                    out error),
                error);

            var operation = new SetDataStructItem();
            EditorServiceRegistry.AttachGraph(operation, runtime);
            InspectorDocument initial = InspectorDefinitionBuilder.Build(operation);
            InspectorSectionDefinition initialParameters =
                initial.Sections.Single(section => section.Key == "参数");
            Assert.AreEqual(2, initialParameters.Fields.Count);
            Assert.AreEqual("结构体", initialParameters.Fields[0].Label);
            Assert.AreEqual("数据项", initialParameters.Fields[1].Label);
            Assert.IsFalse(initialParameters.Fields
                .OfType<InspectorCollectionFieldDefinition>().Any(),
                "目标结构体和数据项尚未确定时，不应先展示字段写入配置。");

            foreach (InspectorValueReferenceFieldDefinition address
                in initialParameters.Fields
                    .OfType<InspectorValueReferenceFieldDefinition>())
            {
                Assert.AreEqual(
                    InspectorValueReferenceKind.Name,
                    address.GetDefaultKind(),
                    "结构体和数据项地址应默认使用名称。");
            }

            operation.StructName = "产品结构";
            operation.ItemName = "当前产品";
            InspectorDocument configured =
                InspectorDefinitionBuilder.Build(operation);
            InspectorSectionDefinition configuredParameters =
                configured.Sections.Single(section => section.Key == "参数");
            Assert.IsInstanceOfType(
                configuredParameters.Fields[0],
                typeof(InspectorValueReferenceFieldDefinition));
            Assert.IsInstanceOfType(
                configuredParameters.Fields[1],
                typeof(InspectorValueReferenceFieldDefinition));
            Assert.IsInstanceOfType(
                configuredParameters.Fields[2],
                typeof(InspectorCollectionFieldDefinition));

            SetDataStructItemParam parameter = operation.Params[0];
            IReadOnlyList<InspectorFieldDefinition> itemFields =
                InspectorDefinitionBuilder.BuildItemFields(
                    parameter,
                    "Params[0]");
            InspectorValueReferenceFieldDefinition fieldAddress =
                itemFields.OfType<InspectorValueReferenceFieldDefinition>()
                    .Single(field => field.Label == "字段");
            Assert.AreEqual(
                InspectorValueReferenceKind.Name,
                fieldAddress.GetDefaultKind());
            InspectorValueReferenceFieldDefinition writeValue =
                itemFields.OfType<InspectorValueReferenceFieldDefinition>()
                    .Single(field => field.Label == "写入值");
            CollectionAssert.AreEquivalent(
                new[]
                {
                    InspectorValueReferenceKind.Fixed,
                    InspectorValueReferenceKind.Name,
                    InspectorValueReferenceKind.Name2,
                    InspectorValueReferenceKind.Index,
                    InspectorValueReferenceKind.Index2
                },
                writeValue.AvailableKinds.ToArray());

            PropertyDescriptor itemNameProperty =
                TypeDescriptor.GetProperties(operation)[
                    nameof(SetDataStructItem.ItemName)];
            CollectionAssert.Contains(
                InspectorValueConversion.GetStandardValues(
                    operation,
                    itemNameProperty)
                    .Select(option => option.Text)
                    .ToList(),
                "当前产品");
            PropertyDescriptor fieldNameProperty =
                TypeDescriptor.GetProperties(parameter)[
                    nameof(SetDataStructItemParam.FieldName)];
            CollectionAssert.Contains(
                InspectorValueConversion.GetStandardValues(
                    parameter,
                    fieldNameProperty)
                    .Select(option => option.Text)
                    .ToList(),
                "计数");
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void EditableTextField_UsesRealTextBoxForEntireEditSession()
        {
            StaTestRunner.Run(() =>
            {
                var operation = new ModifyValue { ChangeValue = "12345" };
                var definition = new InspectorScalarFieldDefinition
                {
                    Label = "修改值",
                    Owner = operation,
                    Property = TypeDescriptor.GetProperties(operation)[
                        nameof(ModifyValue.ChangeValue)]
                };

                using (var toolTip = new ToolTip())
                using (var control = new InspectorScalarFieldControl(
                    definition,
                    true,
                    toolTip))
                using (var form = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(360, 80)
                })
                {
                    control.Dock = DockStyle.Top;
                    form.Controls.Add(control);
                    form.Show();
                    Application.DoEvents();

                    InspectorTextBox editor =
                        GetPrivateField<InspectorTextBox>(control, "editor");
                    InspectorValueCell displayCell =
                        GetPrivateField<InspectorValueCell>(control, "displayCell");

                    Assert.IsTrue(editor.Visible,
                        "进入编辑态后应全程显示真实文本框。");
                    Assert.IsFalse(displayCell.Visible,
                        "编辑态不应再用展示层覆盖真实文本框。");

                    editor.Text = "123456";
                    InvokeProtected(editor, "OnValidated");
                    Assert.IsTrue(editor.Visible,
                        "字段提交后仍处于编辑会话时应保持真实文本框。");
                    Assert.IsFalse(displayCell.Visible);
                }
            }, TimeSpan.FromSeconds(10));
        }

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
        public void SelectingSecondLevelReference_KeepsUserChoiceAcrossCancelAndRefresh()
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
                    Key = "ValueSource.reference",
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

                    int changeNotifications = 0;
                    control.FieldValueChanged += (sender, args) => changeNotifications++;
                    InspectorComboBox kind = GetPrivateField<InspectorComboBox>(control, "kind");
                    InspectorValueCell kindDisplay = GetPrivateField<InspectorValueCell>(
                        control,
                        "kindDisplay");
                    object secondLevelItem = kind.Items.Cast<object>()
                        .Single(item => item.ToString() == "变量二级");
                    kind.SelectedItem = secondLevelItem;
                    InvokeProtected(kind, "OnSelectionChangeCommitted");

                    Assert.AreEqual("变量二级", kindDisplay.DisplayText);
                    InvokeProtected(kind, "OnDropDownClosed");
                    Application.DoEvents();

                    Assert.AreEqual("变量二级", kindDisplay.DisplayText,
                        "二级变量尚未填写时，下拉关闭不应把引用方式刷新回变量。");
                    Assert.IsNull(owner.ValueSourceName);
                    Assert.AreEqual(1, changeNotifications,
                        "切换引用方式清空旧值时必须记录为明确的编辑变更。");

                    typeof(InspectorValueReferenceFieldControl).GetMethod(
                        "DeactivateEditors",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(control, new object[] { true });
                    Assert.AreEqual("变量二级", kindDisplay.DisplayText,
                        "关闭变量选择器后应保留用户选择的引用方式。");

                    var reboundDefinition = new InspectorValueReferenceFieldDefinition
                    {
                        Key = "ValueSource.reference",
                        Label = "源变量",
                        Owner = owner
                    };
                    reboundDefinition.Add(
                        InspectorValueReferenceKind.Name,
                        properties[nameof(ModifyValue.ValueSourceName)]);
                    reboundDefinition.Add(
                        InspectorValueReferenceKind.Name2,
                        properties[nameof(ModifyValue.ValueSourceName2Index)]);
                    reboundDefinition.Add(
                        InspectorValueReferenceKind.Index,
                        properties[nameof(ModifyValue.ValueSourceIndex)]);
                    reboundDefinition.Add(
                        InspectorValueReferenceKind.Index2,
                        properties[nameof(ModifyValue.ValueSourceIndex2Index)]);
                    control.Rebind(reboundDefinition, true);
                    Assert.AreEqual("变量二级", kindDisplay.DisplayText,
                        "同一草稿刷新后应继续保留尚未填写的引用方式。");

                    owner.ValueSourceName = "撤销恢复变量";
                    control.RefreshValue();
                    Assert.AreEqual("变量", kindDisplay.DisplayText,
                        "撤销或外部刷新恢复真实值后，应以恢复后的模型事实为准。");

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
