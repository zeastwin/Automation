// 模块：编辑器 / 流程 / Inspector。
// 职责范围：指令属性定义、编辑控件、选择器和值转换。

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class InspectorScalarFieldControl : InspectorFieldControl
    {
        private InspectorScalarFieldDefinition definition;
        private readonly Control editor;
        private readonly InspectorValueCell displayCell;
        private string validationMessage = string.Empty;
        private bool refreshing;
        private bool endingEdit;
        private bool standardValuesLoaded;
        private readonly HashSet<Control> gotoDropControls = new HashSet<Control>();
        private bool selectionPickerConfigured;
        private ToolStripDropDown activeSelectionPicker;

        public InspectorScalarFieldControl(
            InspectorScalarFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
            : base(definition, editable, descriptionToolTip)
        {
            this.definition = definition;
            AccessibleName = definition.Label;
            DoubleBuffered = true;

            editor = CreateEditor();
            editor.TabIndex = 0;
            Controls.Add(editor);

            if (!(editor is InspectorToggle))
            {
                editor.Visible = false;
                editor.TabStop = false;
                displayCell = new InspectorValueCell
                {
                    AccessibleName = definition.Label,
                    Font = InspectorFonts.Bold95,
                    ShowDropDownArrow = editor is InspectorComboBox,
                    TabIndex = 0
                };
                displayCell.ActivationRequested += (sender, args) => ActivateEditor(false);
                displayCell.DropDownRequested += (sender, args) => ActivateEditor(true);
                Controls.Add(displayCell);
                displayCell.BringToFront();
                ConfigureGotoDrop(displayCell);
            }

            AttachDescription(this, editor, displayCell);

            Resize += (sender, args) => LayoutControls();
            SetEditable(editable);
            RefreshValue();
            LayoutControls();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int width = Math.Max(120, ClientSize.Width);
            int labelWidth = GetLabelWidth(width);
            DrawPropertyRowBackground(e, labelWidth);
            TextRenderer.DrawText(
                e.Graphics,
                definition.Label,
                InspectorFonts.Regular9,
                new Rectangle(6, 0, Math.Max(1, labelWidth - 10), PropertyRowHeight),
                UiPalette.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(validationMessage))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    validationMessage,
                    InspectorFonts.Regular85,
                    new Rectangle(labelWidth + 6, PropertyRowHeight,
                        Math.Max(48, width - labelWidth - 6), 20),
                    UiPalette.Danger,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPadding);
            }
        }

        public override void SetEditable(bool editable)
        {
            Editable = editable;
            bool allow = editable && !definition.IsReadOnly;
            if (displayCell != null)
            {
                displayCell.Editable = allow;
            }
            if (!allow)
            {
                DeactivateEditor(true);
            }
            else if (editor is InspectorTextBox)
            {
                ShowPersistentTextEditor();
            }
            if (editor is TextBox textBox)
            {
                textBox.ReadOnly = !allow;
                textBox.BackColor = UiPalette.SurfaceStrong;
            }
            else
            {
                editor.Enabled = allow;
            }
            foreach (Control gotoDropControl in gotoDropControls)
            {
                gotoDropControl.AllowDrop = allow && IsGotoProperty();
            }
        }

        public override void RefreshValue()
        {
            RefreshValue(false);
        }

        private void RefreshValue(bool force)
        {
            if (!force && editor.Focused)
            {
                return;
            }
            refreshing = true;
            try
            {
                object value = definition.GetValue();
                if (editor is CheckBox checkBox)
                {
                    checkBox.Checked = value is bool flag && flag;
                    checkBox.Text = string.Empty;
                    checkBox.AccessibleName = definition.Label;
                    checkBox.AccessibleDescription = checkBox.Checked ? "已开启" : "已关闭";
                }
                else if (editor is InspectorComboBox comboBox)
                {
                    standardValuesLoaded = false;
                    PopulateStandardValues(
                        comboBox,
                        definition.Owner,
                        definition.Property,
                        value,
                        false);
                    displayCell.DisplayText = InspectorValueConversion.ToDisplayText(
                        definition.Owner,
                        definition.Property,
                        value);
                }
                else if (editor is TextBox textBox)
                {
                    string displayText = InspectorValueConversion.ToDisplayText(
                        definition.Owner,
                        definition.Property,
                        value);
                    textBox.Text = displayText;
                    displayCell.DisplayText = displayText;
                }
                ShowMessage(definition.Description, false);
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, true);
            }
            finally
            {
                refreshing = false;
            }
        }

        public override bool FocusEditor()
        {
            if (!editor.Enabled || (editor is TextBox textBox && textBox.ReadOnly))
            {
                return false;
            }
            if (editor is InspectorToggle)
            {
                editor.Focus();
                return true;
            }
            return ActivateEditor();
        }

        public override void EndEdit()
        {
            DeactivateEditor(true);
        }

        public override bool CanRebind(InspectorFieldDefinition next)
        {
            if (!base.CanRebind(next))
            {
                return false;
            }
            var scalar = (InspectorScalarFieldDefinition)next;
            Type type = Nullable.GetUnderlyingType(scalar.Property.PropertyType)
                ?? scalar.Property.PropertyType;
            if (editor is InspectorToggle)
            {
                return type == typeof(bool);
            }
            bool usesComboBox = InspectorValueConversion.HasStandardValues(
                scalar.Owner,
                scalar.Property) || type.IsEnum;
            return editor is InspectorComboBox ? usesComboBox : !usesComboBox;
        }

        public override void Rebind(InspectorFieldDefinition next, bool editable)
        {
            DeactivateEditor(false);
            definition = (InspectorScalarFieldDefinition)next;
            Definition = next;
            AccessibleName = definition.Label;
            if (displayCell != null)
            {
                displayCell.AccessibleName = definition.Label;
            }
            standardValuesLoaded = false;
            if (editor is InspectorComboBox comboBox)
            {
                comboBox.DropDownStyle = GetComboBoxStyle();
                ConfigureSelectionPicker(comboBox);
            }
            ConfigureGotoDrop(editor);
            ConfigureGotoDrop(displayCell);
            AttachDescription(this, editor, displayCell);
            SetEditable(editable);
            RefreshValue(true);
            Invalidate();
        }

        private Control CreateEditor()
        {
            Type type = Nullable.GetUnderlyingType(definition.Property.PropertyType)
                ?? definition.Property.PropertyType;
            if (type == typeof(bool))
            {
                var checkBox = new InspectorToggle
                {
                    AutoSize = false,
                    BackColor = UiPalette.SurfaceStrong,
                    Font = InspectorFonts.Bold95,
                    ForeColor = UiPalette.Navigation,
                    Height = 24,
                    TextAlign = ContentAlignment.MiddleLeft,
                    UseVisualStyleBackColor = false
                };
                checkBox.CheckedChanged += (sender, args) =>
                {
                    if (!refreshing && checkBox.Enabled)
                    {
                        CommitValue(checkBox.Checked);
                    }
                };
                return checkBox;
            }

            if (InspectorValueConversion.HasStandardValues(definition.Owner, definition.Property)
                || type.IsEnum)
            {
                var comboBox = new InspectorComboBox
                {
                    DropDownHeight = 320,
                    Font = InspectorFonts.Bold95,
                    IntegralHeight = false
                };
                comboBox.DropDownStyle = GetComboBoxStyle();
                comboBox.DropDown += (sender, args) =>
                    EnsureStandardValuesLoaded(comboBox);
                comboBox.SelectionChangeCommitted += (sender, args) => CommitComboBox(comboBox);
                comboBox.DropDownClosed += (sender, args) =>
                {
                    if (comboBox.Visible && !comboBox.UseSelectionPicker)
                    {
                        BeginInvoke((Action)(() => DeactivateEditor(true)));
                    }
                };
                comboBox.Validated += (sender, args) =>
                {
                    if (comboBox.Visible
                        && comboBox.DropDownStyle != ComboBoxStyle.DropDownList
                        && CommitComboBox(comboBox))
                    {
                        DeactivateEditor(true);
                    }
                };
                comboBox.KeyDown += Editor_KeyDown;
                ConfigureGotoDrop(comboBox);
                ConfigureSelectionPicker(comboBox);
                return comboBox;
            }

            var textEditor = new InspectorTextBox
            {
                Font = InspectorFonts.Bold95
            };
            textEditor.Validated += (sender, args) =>
            {
                if (textEditor.Visible && CommitText(textEditor))
                {
                    DeactivateEditor(true);
                }
            };
            textEditor.KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Enter)
                {
                    if (CommitText(textEditor))
                    {
                        DeactivateEditor(true);
                    }
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                }
                else if (args.KeyCode == Keys.Escape)
                {
                    RefreshValue(true);
                    DeactivateEditor(false);
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                }
            };
            ConfigureGotoDrop(textEditor);
            return textEditor;
        }

        private ComboBoxStyle GetComboBoxStyle()
        {
            if (InspectorSelectionPickerResolver.TryResolve(
                definition.Property,
                out InspectorSelectionPickerKind _))
            {
                // 变量、IO、点位和地址选择器只提供候选值，不剥夺手工输入能力。
                return ComboBoxStyle.DropDown;
            }
            return InspectorValueConversion.StandardValuesExclusive(
                definition.Owner,
                definition.Property)
                ? ComboBoxStyle.DropDownList
                : ComboBoxStyle.DropDown;
        }

        private bool ActivateEditor(bool openDropDown = false)
        {
            if (displayCell == null || !displayCell.Editable
                || editor.IsDisposed || !editor.Enabled)
            {
                return false;
            }

            displayCell.Visible = false;
            editor.Visible = true;
            editor.TabStop = true;
            editor.BringToFront();
            editor.Focus();

            if (editor is InspectorTextBox textBox)
            {
                textBox.SelectAll();
                return true;
            }

            if (editor is InspectorComboBox comboBox)
            {
                if (!comboBox.UseSelectionPicker
                    && (openDropDown || comboBox.DropDownStyle == ComboBoxStyle.DropDownList))
                {
                    EnsureStandardValuesLoaded(comboBox);
                }
                bool shouldOpenDropDown = openDropDown
                    || comboBox.DropDownStyle == ComboBoxStyle.DropDownList;
                if (shouldOpenDropDown)
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed || !comboBox.Visible || !comboBox.Enabled)
                        {
                            return;
                        }
                        if (comboBox.UseSelectionPicker)
                        {
                            ShowSelectionPicker(comboBox);
                        }
                        else
                        {
                            comboBox.DroppedDown = true;
                        }
                    }));
                }
                else
                {
                    comboBox.SelectAll();
                }
            }
            return true;
        }

        private void DeactivateEditor(bool refreshDisplay)
        {
            if (displayCell == null || endingEdit)
            {
                return;
            }
            endingEdit = true;
            try
            {
                if (refreshDisplay)
                {
                    RefreshValue(true);
                }
                if (editor is InspectorTextBox && displayCell.Editable)
                {
                    ShowPersistentTextEditor();
                    return;
                }
                editor.Visible = false;
                editor.TabStop = false;
                displayCell.Visible = true;
                displayCell.BringToFront();
            }
            finally
            {
                endingEdit = false;
            }
        }

        private void ShowPersistentTextEditor()
        {
            displayCell.Visible = false;
            editor.Visible = true;
            editor.TabStop = true;
            editor.BringToFront();
        }

        private void Editor_KeyDown(object sender, KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Enter
                && sender is ComboBox comboBox
                && comboBox.DropDownStyle != ComboBoxStyle.DropDownList)
            {
                if (CommitComboBox(comboBox))
                {
                    DeactivateEditor(true);
                }
                args.Handled = true;
                args.SuppressKeyPress = true;
                return;
            }
            if (args.KeyCode != Keys.Escape)
            {
                return;
            }
            RefreshValue(true);
            DeactivateEditor(false);
            args.Handled = true;
            args.SuppressKeyPress = true;
        }

        private void ConfigureGotoDrop(Control control)
        {
            if (control == null)
            {
                return;
            }
            control.AllowDrop = Editable && !definition.IsReadOnly && IsGotoProperty();
            if (!gotoDropControls.Add(control))
            {
                return;
            }
            control.DragEnter += (sender, args) =>
            {
                args.Effect = control.AllowDrop && args.Data != null
                    && args.Data.GetDataPresent(FrmDataGrid.OperationAddressDragFormat)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            };
            control.DragDrop += (sender, args) =>
            {
                if (!control.AllowDrop)
                {
                    return;
                }
                string address = args.Data?.GetData(FrmDataGrid.OperationAddressDragFormat) as string;
                if (string.IsNullOrWhiteSpace(address))
                {
                    return;
                }
                CommitDroppedGotoAddress(address);
            };
        }

        private bool IsGotoProperty()
        {
            return definition.Property.Attributes[typeof(MarkedGotoAttribute)]
                is MarkedGotoAttribute;
        }

        private void CommitDroppedGotoAddress(string address)
        {
            bool committed = false;
            if (editor is TextBox textBox)
            {
                textBox.Text = address;
                committed = CommitText(textBox);
            }
            else if (editor is ComboBox comboBox)
            {
                comboBox.Text = address;
                committed = CommitComboBox(comboBox);
            }
            if (committed)
            {
                RefreshValue(true);
            }
        }

        private void ConfigureSelectionPicker(InspectorComboBox comboBox)
        {
            comboBox.UseSelectionPicker = InspectorSelectionPickerResolver.TryResolve(
                definition.Property,
                out InspectorSelectionPickerKind _);
            if (selectionPickerConfigured)
            {
                return;
            }
            selectionPickerConfigured = true;
            comboBox.SelectionPickerRequested += (sender, args) =>
                ShowSelectionPicker(comboBox);
        }

        private void ShowSelectionPicker(InspectorComboBox comboBox)
        {
            if (!Editable || definition.IsReadOnly
                || !InspectorSelectionPickerResolver.TryResolve(
                    definition.Property,
                    out InspectorSelectionPickerKind kind))
            {
                return;
            }
            activeSelectionPicker?.Close();
            activeSelectionPicker = InspectorSelectionPickerDropDown.Show(
                comboBox,
                kind,
                definition.Owner,
                definition.Property,
                Convert.ToString(definition.GetValue(), CultureInfo.CurrentCulture),
                selectedValue => CommitValue(selectedValue),
                () =>
                {
                    activeSelectionPicker = null;
                    DeactivateEditor(true);
                });
        }

        private void EnsureStandardValuesLoaded(InspectorComboBox comboBox)
        {
            if (standardValuesLoaded)
            {
                return;
            }
            bool wasRefreshing = refreshing;
            refreshing = true;
            try
            {
                PopulateStandardValues(
                    comboBox,
                    definition.Owner,
                    definition.Property,
                    definition.GetValue(),
                    true);
                standardValuesLoaded = true;
            }
            finally
            {
                refreshing = wasRefreshing;
            }
        }

        private bool CommitComboBox(ComboBox comboBox)
        {
            if (refreshing || !comboBox.Enabled)
            {
                return false;
            }
            if (comboBox.SelectedItem is InspectorStandardValue selected)
            {
                return CommitValue(selected.Value);
            }
            if (InspectorFieldValueService.TryConvertAndSetScalar(
                definition, comboBox.Text, out bool changed, out string error))
            {
                ShowMessage(definition.Description, false);
                if (changed) OnFieldValueChanged();
                return true;
            }
            ShowMessage(error, true);
            comboBox.Focus();
            return false;
        }

        private bool CommitText(TextBox textBox)
        {
            if (refreshing || textBox.ReadOnly)
            {
                return false;
            }
            if (InspectorFieldValueService.TryConvertAndSetScalar(
                definition, textBox.Text, out bool changed, out string error))
            {
                ShowMessage(definition.Description, false);
                if (changed) OnFieldValueChanged();
                return true;
            }
            ShowMessage(error, true);
            textBox.SelectAll();
            textBox.Focus();
            return false;
        }

        private bool CommitValue(object value)
        {
            if (InspectorFieldValueService.TrySetScalar(
                definition, value, out bool changed, out string error))
            {
                ShowMessage(definition.Description, false);
                if (changed) OnFieldValueChanged();
                return true;
            }
            ShowMessage(error, true);
            RefreshValue();
            return false;
        }

        private void ShowMessage(string text, bool error)
        {
            string nextMessage = error ? text ?? string.Empty : string.Empty;
            if (string.Equals(validationMessage, nextMessage, StringComparison.Ordinal))
            {
                return;
            }
            validationMessage = nextMessage;
            LayoutControls();
            Invalidate();
        }

        private void LayoutControls()
        {
            int width = Math.Max(120, ClientSize.Width);
            int labelWidth = GetLabelWidth(width);
            int editorLeft = labelWidth;
            int editorTop = 1;
            int editorWidth = Math.Max(48, width - editorLeft);
            editor.SetBounds(
                editorLeft,
                editorTop,
                editor is InspectorToggle ? 38 : Math.Max(48, width - editorLeft),
                PropertyEditorHeight);
            if (displayCell != null)
            {
                displayCell.SetBounds(
                    editorLeft,
                    editorTop,
                    editorWidth,
                    PropertyEditorHeight);
            }
            Height = PropertyRowHeight
                + (string.IsNullOrEmpty(validationMessage) ? 0 : 20);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                activeSelectionPicker?.Dispose();
                activeSelectionPicker = null;
            }
            base.Dispose(disposing);
        }
    }

}
