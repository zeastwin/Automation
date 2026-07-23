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
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class InspectorValueReferenceFieldControl : InspectorFieldControl
    {
        private InspectorValueReferenceFieldDefinition definition;
        private readonly InspectorComboBox kind = new InspectorComboBox();
        private readonly InspectorComboBox value = new InspectorComboBox();
        private readonly InspectorValueCell kindDisplay = new InspectorValueCell();
        private readonly InspectorValueCell valueDisplay = new InspectorValueCell();
        private string validationMessage = string.Empty;
        private bool refreshing;
        private bool endingEdit;
        private bool valueOptionsLoaded;
        private ToolStripDropDown activeSelectionPicker;

        public InspectorValueReferenceFieldControl(
            InspectorValueReferenceFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
            : base(definition, editable, descriptionToolTip)
        {
            this.definition = definition;
            AccessibleName = definition.Label;
            DoubleBuffered = true;

            kind.DropDownStyle = ComboBoxStyle.DropDownList;
            kind.Font = InspectorFonts.Bold95;
            kind.ItemFont = InspectorFonts.Regular9;
            kind.SelectionChangeCommitted += Kind_SelectionChangeCommitted;
            kind.DropDownClosed += (sender, args) =>
                BeginInvoke((Action)(() => DeactivateEditors(false)));
            kind.KeyDown += Editor_KeyDown;
            kind.Visible = false;
            kind.TabStop = false;
            Controls.Add(kind);

            value.Font = InspectorFonts.Bold95;
            value.ItemFont = InspectorFonts.Regular9;
            value.IntegralHeight = false;
            value.DropDownHeight = 320;
            value.DropDown += (sender, args) => EnsureValueOptionsLoaded();
            value.SelectionPickerRequested += (sender, args) => ShowSelectionPicker();
            value.SelectionChangeCommitted += (sender, args) => CommitValue();
            value.DropDownClosed += (sender, args) =>
            {
                if (value.Visible && !value.UseSelectionPicker)
                {
                    BeginInvoke((Action)(() => DeactivateEditors(true)));
                }
            };
            value.Validated += (sender, args) =>
            {
                if (value.Visible && CommitValue())
                {
                    DeactivateEditors(true);
                }
            };
            value.KeyDown += Editor_KeyDown;
            value.Visible = false;
            value.TabStop = false;
            Controls.Add(value);

            kindDisplay.AccessibleName = definition.Label + "引用方式";
            kindDisplay.Font = InspectorFonts.Bold95;
            kindDisplay.ShowDropDownArrow = true;
            kindDisplay.ActivationRequested += (sender, args) => ActivateKindEditor();
            kindDisplay.DropDownRequested += (sender, args) => ActivateKindEditor();
            Controls.Add(kindDisplay);

            valueDisplay.AccessibleName = definition.Label;
            valueDisplay.Font = InspectorFonts.Bold95;
            valueDisplay.ActivationRequested += (sender, args) => ActivateValueEditor(false);
            valueDisplay.DropDownRequested += (sender, args) => ActivateValueEditor(true);
            Controls.Add(valueDisplay);
            kindDisplay.BringToFront();
            valueDisplay.BringToFront();

            AttachDescription(this, kind, value, kindDisplay, valueDisplay);

            Resize += (sender, args) => LayoutControls();
            SetEditable(editable);
            RefreshValue();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int width = Math.Max(180, ClientSize.Width);
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
                        Math.Max(80, width - labelWidth - 6), 20),
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
            if (!allow)
            {
                DeactivateEditors(true);
            }
            kind.Enabled = allow;
            value.Enabled = allow;
            kindDisplay.Editable = allow;
            valueDisplay.Editable = allow;
        }

        public override void RefreshValue()
        {
            RefreshValue(false);
        }

        private void RefreshValue(bool force)
        {
            if (!force && value.Focused)
            {
                return;
            }
            refreshing = true;
            try
            {
                InspectorValueReferenceKind current = definition.GetCurrentKind();
                kind.Items.Clear();
                if (current == InspectorValueReferenceKind.Conflict)
                {
                    kind.Items.Add(new InspectorReferenceKindItem(
                        InspectorValueReferenceKind.Conflict,
                        definition.GetKindDisplayName(InspectorValueReferenceKind.Conflict)));
                }
                foreach (InspectorValueReferenceKind option in definition.AvailableKinds)
                {
                    kind.Items.Add(new InspectorReferenceKindItem(
                        option,
                        definition.GetKindDisplayName(option)));
                }
                InspectorReferenceKindItem selectedKind = kind.Items.Cast<InspectorReferenceKindItem>()
                    .FirstOrDefault(item => item.Kind == current);
                kind.SelectedItem = selectedKind ?? kind.Items.Cast<InspectorReferenceKindItem>().FirstOrDefault();
                kindDisplay.DisplayText = kind.SelectedItem?.ToString() ?? string.Empty;
                ConfigureValueEditor(CurrentKind());
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
            if (!kind.Enabled)
            {
                return false;
            }
            return ActivateValueEditor();
        }

        public override void EndEdit()
        {
            DeactivateEditors(true);
        }

        public override void Rebind(InspectorFieldDefinition next, bool editable)
        {
            DeactivateEditors(false);
            definition = (InspectorValueReferenceFieldDefinition)next;
            Definition = next;
            AccessibleName = definition.Label;
            kindDisplay.AccessibleName = definition.Label + "引用方式";
            valueDisplay.AccessibleName = definition.Label;
            valueOptionsLoaded = false;
            AttachDescription(this, kind, value, kindDisplay, valueDisplay);
            SetEditable(editable);
            RefreshValue(true);
            Invalidate();
        }

        private void Kind_SelectionChangeCommitted(object sender, EventArgs e)
        {
            if (refreshing || !(kind.SelectedItem is InspectorReferenceKindItem selected)
                || selected.Kind == InspectorValueReferenceKind.Conflict)
            {
                return;
            }
            try
            {
                definition.SetKind(selected.Kind);
                kindDisplay.DisplayText = selected.Text;
                ConfigureValueEditor(selected.Kind);
                // 引用方式只有在写入新值后才可由模型事实反推；此处不触发整页刷新，
                // 避免空值状态立即回落到默认方式，导致用户无法继续输入索引。
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, true);
            }
        }

        private void ConfigureValueEditor(InspectorValueReferenceKind selectedKind)
        {
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            valueOptionsLoaded = false;
            if (property == null)
            {
                value.UseSelectionPicker = false;
                value.Items.Clear();
                value.Text = string.Empty;
                value.Enabled = false;
                valueDisplay.DisplayText = string.Empty;
                valueDisplay.ShowDropDownArrow = false;
                valueDisplay.Editable = false;
                return;
            }
            value.UseSelectionPicker = InspectorSelectionPickerResolver.TryResolve(
                property,
                out InspectorSelectionPickerKind _);
            value.DropDownStyle = value.UseSelectionPicker
                ? ComboBoxStyle.DropDown
                : InspectorValueConversion.StandardValuesExclusive(
                    definition.Owner,
                    property)
                    ? ComboBoxStyle.DropDownList
                    : ComboBoxStyle.DropDown;
            valueDisplay.ShowDropDownArrow = value.UseSelectionPicker
                || InspectorValueConversion.HasStandardValues(definition.Owner, property)
                || (Nullable.GetUnderlyingType(property.PropertyType)
                    ?? property.PropertyType).IsEnum;
            PopulateStandardValues(
                value,
                definition.Owner,
                property,
                definition.GetValue(selectedKind),
                false);
            value.Enabled = Editable && !definition.IsReadOnly;
            valueDisplay.Editable = value.Enabled;
            valueDisplay.DisplayText = InspectorValueConversion.ToDisplayText(
                definition.Owner,
                property,
                definition.GetValue(selectedKind));
        }

        private bool ActivateKindEditor()
        {
            if (!kindDisplay.Editable || !kind.Enabled || kind.IsDisposed)
            {
                return false;
            }
            DeactivateEditors(false);
            kindDisplay.Visible = false;
            kind.Visible = true;
            kind.TabStop = true;
            kind.BringToFront();
            kind.Focus();
            BeginInvoke((Action)(() =>
            {
                if (!IsDisposed && kind.Visible && kind.Enabled)
                {
                    kind.DroppedDown = true;
                }
            }));
            return true;
        }

        private bool ActivateValueEditor(bool openDropDown = false)
        {
            InspectorValueReferenceKind selectedKind = CurrentKind();
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            if (!valueDisplay.Editable || !value.Enabled
                || value.IsDisposed || property == null)
            {
                return false;
            }
            DeactivateEditors(false);
            valueDisplay.Visible = false;
            value.Visible = true;
            value.TabStop = true;
            value.BringToFront();
            value.Focus();

            if (value.UseSelectionPicker && openDropDown)
            {
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && value.Visible && value.Enabled)
                    {
                        ShowSelectionPicker();
                    }
                }));
                return true;
            }

            bool shouldOpenDropDown = openDropDown
                || value.DropDownStyle == ComboBoxStyle.DropDownList;
            if (shouldOpenDropDown
                && (InspectorValueConversion.HasStandardValues(definition.Owner, property)
                    || (Nullable.GetUnderlyingType(property.PropertyType)
                        ?? property.PropertyType).IsEnum))
            {
                EnsureValueOptionsLoaded();
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && value.Visible && value.Enabled)
                    {
                        value.DroppedDown = true;
                    }
                }));
            }
            else
            {
                value.SelectAll();
            }
            return true;
        }

        private void DeactivateEditors(bool refreshDisplay)
        {
            if (endingEdit)
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
                kind.Visible = false;
                kind.TabStop = false;
                value.Visible = false;
                value.TabStop = false;
                kindDisplay.Visible = true;
                valueDisplay.Visible = true;
                kindDisplay.BringToFront();
                valueDisplay.BringToFront();
            }
            finally
            {
                endingEdit = false;
            }
        }

        private void Editor_KeyDown(object sender, KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Enter
                && ReferenceEquals(sender, value)
                && value.DropDownStyle != ComboBoxStyle.DropDownList)
            {
                if (CommitValue())
                {
                    DeactivateEditors(true);
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
            DeactivateEditors(false);
            args.Handled = true;
            args.SuppressKeyPress = true;
        }

        private void ShowSelectionPicker()
        {
            InspectorValueReferenceKind selectedKind = CurrentKind();
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            if (!Editable || definition.IsReadOnly || property == null
                || !InspectorSelectionPickerResolver.TryResolve(
                    property,
                    out InspectorSelectionPickerKind kind))
            {
                return;
            }
            activeSelectionPicker?.Close();
            activeSelectionPicker = InspectorSelectionPickerDropDown.Show(
                value,
                kind,
                definition.Owner,
                property,
                Convert.ToString(
                    definition.GetValue(selectedKind),
                    CultureInfo.CurrentCulture),
                selectedValue => CommitPickerValue(selectedKind, selectedValue),
                () =>
                {
                    activeSelectionPicker = null;
                    DeactivateEditors(true);
                });
        }

        private void CommitPickerValue(
            InspectorValueReferenceKind selectedKind,
            string selectedValue)
        {
            if (InspectorFieldValueService.TrySetReference(
                definition, selectedKind, selectedValue, out bool changed, out string error))
            {
                ShowMessage(definition.Description, false);
                if (changed) OnFieldValueChanged();
                return;
            }
            ShowMessage(error, true);
        }

        private void EnsureValueOptionsLoaded()
        {
            if (valueOptionsLoaded)
            {
                return;
            }
            InspectorValueReferenceKind selectedKind = CurrentKind();
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            if (property == null)
            {
                return;
            }
            bool wasRefreshing = refreshing;
            refreshing = true;
            try
            {
                PopulateStandardValues(
                    value,
                    definition.Owner,
                    property,
                    definition.GetValue(selectedKind),
                    true);
                valueOptionsLoaded = true;
            }
            finally
            {
                refreshing = wasRefreshing;
            }
        }

        private bool CommitValue()
        {
            if (refreshing || !value.Enabled)
            {
                return false;
            }
            InspectorValueReferenceKind selectedKind = CurrentKind();
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            if (property == null)
            {
                return false;
            }
            bool success;
            bool changed;
            string error;
            if (value.SelectedItem is InspectorStandardValue option)
            {
                success = InspectorFieldValueService.TrySetReference(
                    definition, selectedKind, option.Value, out changed, out error);
            }
            else
            {
                success = InspectorFieldValueService.TryConvertAndSetReference(
                    definition, selectedKind, value.Text, out changed, out error);
            }
            if (success)
            {
                ShowMessage(definition.Description, false);
                if (changed) OnFieldValueChanged();
                return true;
            }
            ShowMessage(error, true);
            value.Focus();
            return false;
        }

        private InspectorValueReferenceKind CurrentKind()
        {
            if (kind.SelectedItem is InspectorReferenceKindItem selected
                && selected.Kind != InspectorValueReferenceKind.Conflict)
            {
                return selected.Kind;
            }
            return definition.GetDefaultKind();
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
            int width = Math.Max(180, ClientSize.Width);
            int labelWidth = GetLabelWidth(width);
            int editorLeft = labelWidth;
            int editorWidth = Math.Max(80, width - editorLeft);
            int kindTextWidth = definition.AvailableKinds
                .Select(option => TextRenderer.MeasureText(
                    definition.GetKindDisplayName(option),
                    kindDisplay.Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width)
                .Concat(new[]
                {
                    TextRenderer.MeasureText(
                        definition.GetKindDisplayName(InspectorValueReferenceKind.Conflict),
                        kindDisplay.Font,
                        Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width
                })
                .DefaultIfEmpty(0)
                .Max();
            int preferredKindWidth = Math.Max(96, kindTextWidth + 40);
            int kindWidth = Math.Min(
                preferredKindWidth,
                Math.Max(72, editorWidth - 48));
            kind.SetBounds(editorLeft, 1, kindWidth, PropertyEditorHeight);
            kindDisplay.SetBounds(editorLeft, 1, kindWidth, PropertyEditorHeight);
            value.SetBounds(
                editorLeft + kindWidth + 2,
                1,
                Math.Max(48, editorWidth - kindWidth - 2),
                PropertyEditorHeight);
            valueDisplay.SetBounds(
                editorLeft + kindWidth + 2,
                1,
                Math.Max(48, editorWidth - kindWidth - 2),
                PropertyEditorHeight);
            int messageHeight = string.IsNullOrEmpty(validationMessage) ? 0 : 20;
            Height = PropertyRowHeight + messageHeight;
        }

        private sealed class InspectorReferenceKindItem
        {
            public InspectorReferenceKindItem(InspectorValueReferenceKind kind, string text)
            {
                Kind = kind;
                Text = text;
            }

            public InspectorValueReferenceKind Kind { get; }
            public string Text { get; }
            public override string ToString() => Text;
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
