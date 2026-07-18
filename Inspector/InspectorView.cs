using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Automation
{
    internal static class InspectorFonts
    {
        public static readonly Font Regular85 = new Font("Microsoft YaHei UI", 8.5F);
        public static readonly Font Regular9 = new Font("Microsoft YaHei UI", 9F);
        public static readonly Font Regular95 = new Font("Microsoft YaHei UI", 9.5F);
        public static readonly Font Regular10 = new Font("Microsoft YaHei UI", 10F);
        public static readonly Font Bold9 = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        public static readonly Font Bold95 = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
    }

    internal sealed class InspectorView : UserControl
    {
        private readonly FlowLayoutPanel content = new FlowLayoutPanel();
        private readonly Label emptyLabel = new Label();
        private readonly List<InspectorSectionControl> sectionControls
            = new List<InspectorSectionControl>();
        private InspectorDocument document;
        private object selectedObject;
        private bool editable;
        private string filterText = string.Empty;
        private int updateDepth;
        private bool refreshPending;

        public InspectorView()
        {
            BackColor = Color.FromArgb(246, 249, 251);
            DoubleBuffered = true;

            content.AutoScroll = true;
            content.BackColor = BackColor;
            content.Dock = DockStyle.Fill;
            content.FlowDirection = FlowDirection.TopDown;
            content.Padding = new Padding(8, 8, 8, 16);
            content.WrapContents = false;
            Controls.Add(content);

            emptyLabel.AutoSize = false;
            emptyLabel.BackColor = BackColor;
            emptyLabel.Dock = DockStyle.Fill;
            emptyLabel.Font = InspectorFonts.Regular10;
            emptyLabel.ForeColor = Color.FromArgb(113, 128, 140);
            emptyLabel.Text = "选择流程、步骤、指令或配置对象后，\r\n可在这里查看和编辑参数。";
            emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(emptyLabel);

            Resize += (sender, args) => UpdateContentWidths();
        }

        public object SelectedObject => selectedObject;

        public event EventHandler FieldValueChanged;

        public void SetObject(object value, bool allowEdit)
        {
            bool objectChanged = !ReferenceEquals(selectedObject, value);
            selectedObject = value;
            editable = allowEdit;
            if (objectChanged || document == null)
            {
                Rebuild();
                return;
            }
            SetEditable(allowEdit);
            RefreshValues();
        }

        public void SetEditable(bool allowEdit)
        {
            editable = allowEdit;
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.SetEditable(allowEdit);
            }
        }

        public void SetFilter(string value)
        {
            filterText = value?.Trim() ?? string.Empty;
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.ApplyFilter(filterText);
            }
            UpdateContentWidths();
        }

        public void BeginUpdate()
        {
            updateDepth++;
            if (updateDepth == 1)
            {
                SuspendLayout();
                content.SuspendLayout();
            }
        }

        public void EndUpdate()
        {
            if (updateDepth <= 0)
            {
                return;
            }
            updateDepth--;
            if (updateDepth != 0)
            {
                return;
            }
            content.ResumeLayout(true);
            ResumeLayout(true);
        }

        public void RefreshDocument()
        {
            if (selectedObject == null)
            {
                Rebuild();
                return;
            }
            InspectorDocument next = InspectorDefinitionBuilder.Build(selectedObject);
            if (document == null || !string.Equals(
                document.Signature,
                next.Signature,
                StringComparison.Ordinal))
            {
                Rebuild(next);
                return;
            }
            document = next;
            RefreshValues();
        }

        public bool FocusFirstEditableField()
        {
            foreach (InspectorSectionControl section in sectionControls)
            {
                if (section.FocusFirstEditableField())
                {
                    return true;
                }
            }
            return false;
        }

        private void Rebuild(InspectorDocument next = null)
        {
            Point scrollPosition = content.AutoScrollPosition;
            BeginUpdate();
            try
            {
                foreach (Control control in content.Controls.Cast<Control>().ToArray())
                {
                    control.Dispose();
                }
                content.Controls.Clear();
                sectionControls.Clear();
                document = next ?? InspectorDefinitionBuilder.Build(selectedObject);
                emptyLabel.Visible = selectedObject == null || document.Sections.Count == 0;
                content.Visible = !emptyLabel.Visible;
                if (emptyLabel.Visible)
                {
                    emptyLabel.BringToFront();
                    return;
                }

                foreach (InspectorSectionDefinition section in document.Sections)
                {
                    if (section.Fields.Count == 0)
                    {
                        continue;
                    }
                    var sectionControl = new InspectorSectionControl(section, editable);
                    sectionControl.FieldValueChanged += Editor_FieldValueChanged;
                    sectionControls.Add(sectionControl);
                    content.Controls.Add(sectionControl);
                }
                content.AutoScrollPosition = new Point(
                    Math.Abs(scrollPosition.X),
                    Math.Abs(scrollPosition.Y));
                SetFilter(filterText);
            }
            finally
            {
                EndUpdate();
                UpdateContentWidths();
            }
        }

        private void Editor_FieldValueChanged(object sender, EventArgs e)
        {
            FieldValueChanged?.Invoke(this, EventArgs.Empty);
            if (!IsHandleCreated || IsDisposed || refreshPending)
            {
                return;
            }
            refreshPending = true;
            BeginInvoke((Action)(() =>
            {
                refreshPending = false;
                if (!IsDisposed)
                {
                    RefreshDocument();
                }
            }));
        }

        private void RefreshValues()
        {
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.RefreshValues();
            }
        }

        private void UpdateContentWidths()
        {
            int width = Math.Max(220, content.ClientSize.Width - content.Padding.Horizontal
                - (content.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.Width = width;
            }
        }
    }

    internal sealed class InspectorSectionControl : UserControl
    {
        private readonly Button headerButton = new Button();
        private readonly FlowLayoutPanel body = new FlowLayoutPanel();
        private readonly List<InspectorFieldControl> fields = new List<InspectorFieldControl>();
        private bool expanded = true;

        public InspectorSectionControl(InspectorSectionDefinition definition, bool editable)
        {
            AutoSize = false;
            BackColor = Color.White;
            Margin = new Padding(0, 0, 0, 8);
            Padding = Padding.Empty;

            headerButton.AutoSize = false;
            headerButton.BackColor = Color.FromArgb(238, 244, 248);
            headerButton.Cursor = Cursors.Hand;
            headerButton.FlatAppearance.BorderSize = 0;
            headerButton.FlatStyle = FlatStyle.Flat;
            headerButton.Font = InspectorFonts.Bold95;
            headerButton.ForeColor = Color.FromArgb(46, 67, 82);
            headerButton.Height = 36;
            headerButton.Padding = new Padding(8, 0, 0, 0);
            headerButton.Text = "▾  " + definition.Title;
            headerButton.TextAlign = ContentAlignment.MiddleLeft;
            headerButton.Click += (sender, args) => ToggleExpanded();
            Controls.Add(headerButton);

            body.AutoSize = false;
            body.BackColor = Color.White;
            body.FlowDirection = FlowDirection.TopDown;
            body.Padding = new Padding(10, 5, 10, 8);
            body.WrapContents = false;
            Controls.Add(body);
            body.BringToFront();

            foreach (InspectorFieldDefinition field in definition.Fields)
            {
                InspectorFieldControl editor = CreateEditor(field, editable);
                editor.FieldValueChanged += (sender, args) => FieldValueChanged?.Invoke(this, EventArgs.Empty);
                editor.SizeChanged += (sender, args) => UpdateWidths();
                fields.Add(editor);
                body.Controls.Add(editor);
            }
            Resize += (sender, args) => UpdateWidths();
            UpdateWidths();
        }

        public event EventHandler FieldValueChanged;

        public void SetEditable(bool editable)
        {
            foreach (InspectorFieldControl field in fields)
            {
                field.SetEditable(editable);
            }
        }

        public void RefreshValues()
        {
            foreach (InspectorFieldControl field in fields)
            {
                field.RefreshValue();
            }
        }

        public void ApplyFilter(string filter)
        {
            bool hasFilter = !string.IsNullOrWhiteSpace(filter);
            bool anyVisible = false;
            foreach (InspectorFieldControl field in fields)
            {
                bool visible = !hasFilter || field.Matches(filter);
                field.Visible = visible;
                anyVisible |= visible;
            }
            Visible = anyVisible;
            if (hasFilter && anyVisible && !expanded)
            {
                expanded = true;
                body.Visible = true;
                headerButton.Text = headerButton.Text.Replace("▸", "▾");
            }
            UpdateWidths();
        }

        public bool FocusFirstEditableField()
        {
            foreach (InspectorFieldControl field in fields.Where(item => item.Visible))
            {
                if (field.FocusEditor())
                {
                    return true;
                }
            }
            return false;
        }

        private static InspectorFieldControl CreateEditor(
            InspectorFieldDefinition definition,
            bool editable)
        {
            if (definition is InspectorValueReferenceFieldDefinition reference)
            {
                return new InspectorValueReferenceFieldControl(reference, editable);
            }
            if (definition is InspectorCollectionFieldDefinition collection)
            {
                return new InspectorCollectionFieldControl(collection, editable);
            }
            return new InspectorScalarFieldControl((InspectorScalarFieldDefinition)definition, editable);
        }

        private void ToggleExpanded()
        {
            expanded = !expanded;
            body.Visible = expanded;
            if (expanded)
            {
                headerButton.Text = headerButton.Text.Replace("▸", "▾");
            }
            else
            {
                headerButton.Text = headerButton.Text.Replace("▾", "▸");
            }
            UpdateWidths();
        }

        private void UpdateWidths()
        {
            int width = Math.Max(180, ClientSize.Width);
            headerButton.SetBounds(0, 0, width, 36);
            body.Width = width;
            int fieldWidth = Math.Max(180, body.ClientSize.Width - body.Padding.Horizontal);
            foreach (InspectorFieldControl field in fields)
            {
                field.Width = fieldWidth;
            }
            int bodyHeight = body.GetPreferredSize(new Size(width, 0)).Height;
            body.SetBounds(0, 36, width, bodyHeight);
            Height = 36 + (expanded ? bodyHeight : 0);
        }
    }

    internal abstract class InspectorFieldControl : UserControl
    {
        protected readonly InspectorFieldDefinition Definition;
        protected bool Editable;

        protected InspectorFieldControl(InspectorFieldDefinition definition, bool editable)
        {
            Definition = definition;
            Editable = editable;
            AutoSize = false;
            BackColor = Color.White;
            Margin = new Padding(0, 4, 0, 6);
        }

        public event EventHandler FieldValueChanged;

        public bool Matches(string filter)
        {
            return Definition.SearchText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public abstract void SetEditable(bool editable);
        public abstract void RefreshValue();
        public abstract bool FocusEditor();

        protected void OnFieldValueChanged()
        {
            FieldValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal sealed class InspectorScalarFieldControl : InspectorFieldControl
    {
        private readonly InspectorScalarFieldDefinition definition;
        private readonly Label label = new Label();
        private readonly Label message = new Label();
        private readonly Control editor;
        private bool refreshing;

        public InspectorScalarFieldControl(InspectorScalarFieldDefinition definition, bool editable)
            : base(definition, editable)
        {
            this.definition = definition;
            label.AutoEllipsis = true;
            label.Font = InspectorFonts.Regular95;
            label.ForeColor = Color.FromArgb(55, 70, 82);
            label.Height = 24;
            label.Text = definition.Label;
            label.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(label);

            editor = CreateEditor();
            editor.TabIndex = 0;
            Controls.Add(editor);

            message.AutoEllipsis = true;
            message.Font = InspectorFonts.Regular85;
            message.ForeColor = Color.FromArgb(105, 120, 132);
            message.Height = string.IsNullOrWhiteSpace(definition.Description) ? 0 : 22;
            message.Text = definition.Description;
            message.TextAlign = ContentAlignment.MiddleLeft;
            message.Visible = message.Height > 0;
            Controls.Add(message);

            Resize += (sender, args) => LayoutControls();
            SetEditable(editable);
            RefreshValue();
            LayoutControls();
        }

        public override void SetEditable(bool editable)
        {
            Editable = editable;
            bool allow = editable && !definition.IsReadOnly;
            if (editor is TextBox textBox)
            {
                textBox.ReadOnly = !allow;
                textBox.BackColor = allow ? Color.White : Color.FromArgb(246, 248, 250);
            }
            else
            {
                editor.Enabled = allow;
            }
        }

        public override void RefreshValue()
        {
            if (editor.Focused)
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
                    checkBox.Text = checkBox.Checked ? "已启用" : "未启用";
                }
                else if (editor is ComboBox comboBox)
                {
                    FillComboBox(comboBox, value);
                }
                else if (editor is TextBox textBox)
                {
                    textBox.Text = InspectorValueConversion.ToDisplayText(
                        definition.Owner,
                        definition.Property,
                        value);
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
            editor.Focus();
            return true;
        }

        private Control CreateEditor()
        {
            Type type = Nullable.GetUnderlyingType(definition.Property.PropertyType)
                ?? definition.Property.PropertyType;
            if (type == typeof(bool))
            {
                var checkBox = new CheckBox
                {
                    AutoSize = false,
                    BackColor = Color.FromArgb(247, 249, 251),
                    Font = InspectorFonts.Regular95,
                    ForeColor = Color.FromArgb(44, 76, 94),
                    Height = 34,
                    Padding = new Padding(8, 0, 0, 0),
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
                var comboBox = new ComboBox
                {
                    DropDownHeight = 320,
                    Font = InspectorFonts.Regular95,
                    IntegralHeight = false
                };
                comboBox.DropDownStyle = InspectorValueConversion.StandardValuesExclusive(
                    definition.Owner,
                    definition.Property)
                    ? ComboBoxStyle.DropDownList
                    : ComboBoxStyle.DropDown;
                comboBox.SelectionChangeCommitted += (sender, args) => CommitComboBox(comboBox);
                comboBox.Validated += (sender, args) =>
                {
                    if (comboBox.DropDownStyle != ComboBoxStyle.DropDownList)
                    {
                        CommitComboBox(comboBox);
                    }
                };
                ConfigureGotoDrop(comboBox);
                return comboBox;
            }

            var textEditor = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Font = InspectorFonts.Regular95
            };
            textEditor.Validated += (sender, args) => CommitText(textEditor);
            textEditor.KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Enter)
                {
                    CommitText(textEditor);
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                }
            };
            ConfigureGotoDrop(textEditor);
            return textEditor;
        }

        private void ConfigureGotoDrop(Control control)
        {
            if (!(definition.Property.Attributes[typeof(MarkedGotoAttribute)] is MarkedGotoAttribute))
            {
                return;
            }
            control.AllowDrop = true;
            control.DragEnter += (sender, args) =>
            {
                args.Effect = args.Data != null
                    && args.Data.GetDataPresent(FrmDataGrid.OperationAddressDragFormat)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            };
            control.DragDrop += (sender, args) =>
            {
                string address = args.Data?.GetData(FrmDataGrid.OperationAddressDragFormat) as string;
                if (string.IsNullOrWhiteSpace(address))
                {
                    return;
                }
                if (control is TextBox textBox)
                {
                    textBox.Text = address;
                    CommitText(textBox);
                }
                else if (control is ComboBox comboBox)
                {
                    comboBox.Text = address;
                    CommitComboBox(comboBox);
                }
            };
        }

        private void FillComboBox(ComboBox comboBox, object currentValue)
        {
            comboBox.BeginUpdate();
            try
            {
                comboBox.Items.Clear();
                foreach (InspectorStandardValue item in InspectorValueConversion.GetStandardValues(
                    definition.Owner,
                    definition.Property))
                {
                    comboBox.Items.Add(item);
                }
                InspectorStandardValue selected = comboBox.Items.Cast<InspectorStandardValue>()
                    .FirstOrDefault(item => Equals(item.Value, currentValue));
                if (selected != null)
                {
                    comboBox.SelectedItem = selected;
                }
                else
                {
                    comboBox.SelectedIndex = -1;
                    comboBox.Text = InspectorValueConversion.ToDisplayText(
                        definition.Owner,
                        definition.Property,
                        currentValue);
                }
            }
            finally
            {
                comboBox.EndUpdate();
            }
        }

        private void CommitComboBox(ComboBox comboBox)
        {
            if (refreshing || !comboBox.Enabled)
            {
                return;
            }
            object value = comboBox.SelectedItem is InspectorStandardValue selected
                ? selected.Value
                : InspectorValueConversion.FromText(
                    definition.Owner,
                    definition.Property,
                    comboBox.Text);
            CommitValue(value);
        }

        private void CommitText(TextBox textBox)
        {
            if (refreshing || textBox.ReadOnly)
            {
                return;
            }
            try
            {
                object value = InspectorValueConversion.FromText(
                    definition.Owner,
                    definition.Property,
                    textBox.Text);
                CommitValue(value);
            }
            catch (Exception ex)
            {
                ShowMessage(Unwrap(ex).Message, true);
                textBox.SelectAll();
                textBox.Focus();
            }
        }

        private void CommitValue(object value)
        {
            try
            {
                object current = definition.GetValue();
                if (Equals(current, value))
                {
                    ShowMessage(definition.Description, false);
                    return;
                }
                definition.SetValue(value);
                ShowMessage(definition.Description, false);
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                ShowMessage(Unwrap(ex).Message, true);
                RefreshValue();
            }
        }

        private void ShowMessage(string text, bool error)
        {
            message.ForeColor = error
                ? Color.FromArgb(182, 55, 45)
                : Color.FromArgb(105, 120, 132);
            message.Text = text ?? string.Empty;
            message.Visible = !string.IsNullOrWhiteSpace(message.Text);
            message.Height = message.Visible ? 22 : 0;
            LayoutControls();
        }

        private void LayoutControls()
        {
            int width = Math.Max(120, ClientSize.Width);
            label.SetBounds(0, 0, width, 24);
            editor.SetBounds(0, 24, width, 34);
            message.SetBounds(0, 60, width, message.Height);
            Height = 60 + message.Height;
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }
    }

    internal sealed class InspectorValueReferenceFieldControl : InspectorFieldControl
    {
        private readonly InspectorValueReferenceFieldDefinition definition;
        private readonly Label label = new Label();
        private readonly ComboBox kind = new ComboBox();
        private readonly ComboBox value = new ComboBox();
        private readonly Label message = new Label();
        private bool refreshing;

        public InspectorValueReferenceFieldControl(
            InspectorValueReferenceFieldDefinition definition,
            bool editable)
            : base(definition, editable)
        {
            this.definition = definition;
            label.Font = InspectorFonts.Regular95;
            label.ForeColor = Color.FromArgb(55, 70, 82);
            label.Text = definition.Label;
            label.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(label);

            kind.DropDownStyle = ComboBoxStyle.DropDownList;
            kind.Font = InspectorFonts.Regular9;
            kind.SelectionChangeCommitted += Kind_SelectionChangeCommitted;
            Controls.Add(kind);

            value.Font = InspectorFonts.Regular95;
            value.IntegralHeight = false;
            value.DropDownHeight = 320;
            value.SelectionChangeCommitted += (sender, args) => CommitValue();
            value.Validated += (sender, args) => CommitValue();
            Controls.Add(value);

            message.Font = InspectorFonts.Regular85;
            message.ForeColor = Color.FromArgb(105, 120, 132);
            message.Text = definition.Description;
            Controls.Add(message);

            Resize += (sender, args) => LayoutControls();
            SetEditable(editable);
            RefreshValue();
        }

        public override void SetEditable(bool editable)
        {
            Editable = editable;
            bool allow = editable && !definition.IsReadOnly;
            kind.Enabled = allow;
            value.Enabled = allow;
        }

        public override void RefreshValue()
        {
            if (value.Focused)
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
            value.Focus();
            return true;
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
            value.BeginUpdate();
            try
            {
                value.Items.Clear();
                if (property == null)
                {
                    value.Text = string.Empty;
                    value.Enabled = false;
                    return;
                }
                value.DropDownStyle = InspectorValueConversion.StandardValuesExclusive(
                    definition.Owner,
                    property)
                    ? ComboBoxStyle.DropDownList
                    : ComboBoxStyle.DropDown;
                object currentValue = definition.GetValue(selectedKind);
                foreach (InspectorStandardValue option in InspectorValueConversion.GetStandardValues(
                    definition.Owner,
                    property))
                {
                    value.Items.Add(option);
                }
                InspectorStandardValue selected = value.Items.Cast<InspectorStandardValue>()
                    .FirstOrDefault(item => Equals(item.Value, currentValue));
                value.SelectedItem = selected;
                if (selected == null)
                {
                    value.Text = InspectorValueConversion.ToDisplayText(
                        definition.Owner,
                        property,
                        currentValue);
                }
                value.Enabled = Editable && !definition.IsReadOnly;
            }
            finally
            {
                value.EndUpdate();
            }
        }

        private void CommitValue()
        {
            if (refreshing || !value.Enabled)
            {
                return;
            }
            InspectorValueReferenceKind selectedKind = CurrentKind();
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            if (property == null)
            {
                return;
            }
            try
            {
                object converted = value.SelectedItem is InspectorStandardValue option
                    ? option.Value
                    : InspectorValueConversion.FromText(definition.Owner, property, value.Text);
                definition.SetValue(selectedKind, converted);
                ShowMessage(definition.Description, false);
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, true);
                value.Focus();
            }
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
            message.ForeColor = error
                ? Color.FromArgb(182, 55, 45)
                : Color.FromArgb(105, 120, 132);
            message.Text = text ?? string.Empty;
            message.Visible = !string.IsNullOrWhiteSpace(message.Text);
            LayoutControls();
        }

        private void LayoutControls()
        {
            int width = Math.Max(180, ClientSize.Width);
            int kindWidth = Math.Min(105, Math.Max(88, width / 3));
            label.SetBounds(0, 0, width, 24);
            kind.SetBounds(0, 24, kindWidth, 34);
            value.SetBounds(kindWidth + 6, 24, Math.Max(80, width - kindWidth - 6), 34);
            int messageHeight = message.Visible ? 22 : 0;
            message.SetBounds(0, 60, width, messageHeight);
            Height = 60 + messageHeight;
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
    }

    internal sealed class InspectorCollectionFieldControl : InspectorFieldControl
    {
        private readonly InspectorCollectionFieldDefinition definition;
        private readonly Label title = new Label();
        private readonly Button addButton = new Button();
        private readonly ComboBox countEditor = new ComboBox();
        private readonly FlowLayoutPanel itemsPanel = new FlowLayoutPanel();
        private readonly PropertyDescriptor countProperty;
        private bool refreshingCount;
        private bool showAddButton;
        private bool showCountEditor;

        public InspectorCollectionFieldControl(
            InspectorCollectionFieldDefinition definition,
            bool editable)
            : base(definition, editable)
        {
            this.definition = definition;
            countProperty = InspectorDefinitionBuilder.FindCollectionCountProperty(
                definition.Owner,
                definition.Property);
            title.Font = InspectorFonts.Bold95;
            title.ForeColor = Color.FromArgb(51, 73, 87);
            title.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(title);

            addButton.BackColor = Color.White;
            addButton.Cursor = Cursors.Hand;
            addButton.FlatAppearance.BorderColor = Color.FromArgb(196, 210, 220);
            addButton.FlatStyle = FlatStyle.Flat;
            addButton.Font = InspectorFonts.Regular9;
            addButton.ForeColor = Color.FromArgb(33, 105, 145);
            addButton.Text = "+ 添加";
            addButton.Click += (sender, args) => AddItem();
            Controls.Add(addButton);

            countEditor.AccessibleName = definition.Label + "数量";
            countEditor.DropDownHeight = 260;
            countEditor.Font = InspectorFonts.Regular9;
            countEditor.IntegralHeight = false;
            countEditor.SelectionChangeCommitted += (sender, args) => CommitCount();
            countEditor.Validated += (sender, args) => CommitCount();
            Controls.Add(countEditor);

            itemsPanel.AutoSize = false;
            itemsPanel.BackColor = Color.White;
            itemsPanel.FlowDirection = FlowDirection.TopDown;
            itemsPanel.WrapContents = false;
            Controls.Add(itemsPanel);

            Resize += (sender, args) => LayoutControls();
            SetEditable(editable);
            RebuildItems();
        }

        public override void SetEditable(bool editable)
        {
            Editable = editable;
            showAddButton = editable && !definition.IsReadOnly && CanCreateItem();
            showCountEditor = editable && !definition.IsReadOnly && countProperty != null;
            addButton.Visible = showAddButton;
            countEditor.Visible = showCountEditor;
            foreach (InspectorCollectionItemControl item in itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>())
            {
                item.SetEditable(editable && !definition.IsReadOnly);
            }
            LayoutControls();
        }

        public override void RefreshValue()
        {
            IList items = definition.Items;
            RefreshCountEditor(items?.Count ?? 0);
            if (itemsPanel.Controls.Count != (items?.Count ?? 0))
            {
                RebuildItems();
                return;
            }
            foreach (InspectorCollectionItemControl item in itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>())
            {
                item.RefreshValues();
            }
            title.Text = $"{definition.Label}（{items?.Count ?? 0}）";
        }

        public override bool FocusEditor()
        {
            InspectorCollectionItemControl first = itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>().FirstOrDefault();
            return first?.FocusFirstEditor() == true;
        }

        private void RebuildItems()
        {
            itemsPanel.SuspendLayout();
            try
            {
                foreach (Control control in itemsPanel.Controls.Cast<Control>().ToArray())
                {
                    control.Dispose();
                }
                itemsPanel.Controls.Clear();
                IList items = definition.Items;
                title.Text = $"{definition.Label}（{items?.Count ?? 0}）";
                RefreshCountEditor(items?.Count ?? 0);
                if (items == null)
                {
                    return;
                }
                for (int index = 0; index < items.Count; index++)
                {
                    object item = items[index];
                    var itemControl = new InspectorCollectionItemControl(
                        definition.Label,
                        index,
                        item,
                        Editable && !definition.IsReadOnly,
                        items.Count <= 6 || index == 0);
                    itemControl.DeleteRequested += (sender, args) => DeleteItem(itemControl.ItemIndex);
                    itemControl.MoveRequested += (sender, offset) => MoveItem(itemControl.ItemIndex, offset);
                    itemControl.FieldValueChanged += (sender, args) => OnFieldValueChanged();
                    itemControl.SizeChanged += (sender, args) => LayoutControls();
                    itemsPanel.Controls.Add(itemControl);
                }
            }
            finally
            {
                itemsPanel.ResumeLayout(true);
                LayoutControls();
            }
        }

        private bool CanCreateItem()
        {
            Type itemType = definition.ItemType;
            return itemType != null && !itemType.IsAbstract
                && itemType.GetConstructor(Type.EmptyTypes) != null;
        }

        private void AddItem()
        {
            IList items = definition.Items;
            Type itemType = definition.ItemType;
            if (items == null || itemType == null)
            {
                return;
            }
            int previousCount = items.Count;
            try
            {
                object item = Activator.CreateInstance(itemType);
                ApplyItemDefaults(item);
                items.Add(item);
                SynchronizeCount(items.Count);
                RebuildItems();
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                while (items.Count > previousCount)
                {
                    items.RemoveAt(items.Count - 1);
                }
                MessageBox.Show(
                    Unwrap(ex).Message,
                    "添加配置项失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                RebuildItems();
            }
        }

        private void DeleteItem(int index)
        {
            IList items = definition.Items;
            if (items == null || index < 0 || index >= items.Count)
            {
                return;
            }
            object removed = items[index];
            items.RemoveAt(index);
            try
            {
                SynchronizeCount(items.Count);
                RebuildItems();
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                items.Insert(index, removed);
                MessageBox.Show(
                    Unwrap(ex).Message,
                    "删除配置项失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                RebuildItems();
            }
        }

        private void MoveItem(int index, int offset)
        {
            IList items = definition.Items;
            int target = index + offset;
            if (items == null || index < 0 || index >= items.Count
                || target < 0 || target >= items.Count)
            {
                return;
            }
            object item = items[index];
            items.RemoveAt(index);
            items.Insert(target, item);
            RebuildItems();
            OnFieldValueChanged();
        }

        private void SynchronizeCount(int count)
        {
            if (countProperty == null)
            {
                return;
            }
            object converted = countProperty.PropertyType == typeof(string)
                ? count.ToString(CultureInfo.InvariantCulture)
                : Convert.ChangeType(count, countProperty.PropertyType, CultureInfo.InvariantCulture);
            countProperty.SetValue(definition.Owner, converted);
        }

        private void RefreshCountEditor(int itemCount, bool force = false)
        {
            if (countProperty == null || (!force && countEditor.Focused))
            {
                return;
            }
            refreshingCount = true;
            try
            {
                countEditor.Items.Clear();
                countEditor.DropDownStyle = InspectorValueConversion.StandardValuesExclusive(
                    definition.Owner,
                    countProperty)
                    ? ComboBoxStyle.DropDownList
                    : ComboBoxStyle.DropDown;
                object currentValue = countProperty.GetValue(definition.Owner);
                foreach (InspectorStandardValue option in InspectorValueConversion.GetStandardValues(
                    definition.Owner,
                    countProperty))
                {
                    countEditor.Items.Add(option);
                }
                InspectorStandardValue selected = countEditor.Items.Cast<InspectorStandardValue>()
                    .FirstOrDefault(option => Equals(option.Value, currentValue));
                countEditor.SelectedItem = selected;
                if (selected == null)
                {
                    countEditor.Text = InspectorValueConversion.ToDisplayText(
                        definition.Owner,
                        countProperty,
                        currentValue ?? itemCount);
                }
            }
            finally
            {
                refreshingCount = false;
            }
        }

        private void CommitCount()
        {
            if (refreshingCount || !showCountEditor || countProperty == null)
            {
                return;
            }
            try
            {
                object converted = countEditor.SelectedItem is InspectorStandardValue option
                    ? option.Value
                    : InspectorValueConversion.FromText(
                        definition.Owner,
                        countProperty,
                        countEditor.Text);
                object current = countProperty.GetValue(definition.Owner);
                if (Equals(current, converted))
                {
                    return;
                }
                countProperty.SetValue(definition.Owner, converted);
                RebuildItems();
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Unwrap(ex).Message,
                    "调整配置项数量失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                RefreshCountEditor(definition.Items?.Count ?? 0, true);
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }

        private static void ApplyItemDefaults(object item)
        {
            if (item is IoOutParam io)
            {
                io.delayBefore = -1;
                io.delayAfter = -1;
            }
            else if (item is procParam process)
            {
                process.delayAfter = -1;
            }
        }

        private void LayoutControls()
        {
            int width = Math.Max(180, ClientSize.Width);
            int right = width;
            if (showAddButton)
            {
                addButton.SetBounds(Math.Max(0, right - 78), 2, 78, 28);
                right -= 84;
            }
            if (showCountEditor)
            {
                countEditor.SetBounds(Math.Max(0, right - 62), 3, 62, 27);
                right -= 68;
            }
            title.SetBounds(0, 0, Math.Max(70, right), 32);
            foreach (InspectorCollectionItemControl item in itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>())
            {
                item.Width = width;
            }
            int itemsHeight = itemsPanel.GetPreferredSize(new Size(width, 0)).Height;
            itemsPanel.SetBounds(0, 36, width, itemsHeight);
            Height = 36 + itemsHeight;
        }
    }

    internal sealed class InspectorCollectionItemControl : UserControl
    {
        private readonly Button header = new Button();
        private readonly Button delete = new Button();
        private readonly Button moveUp = new Button();
        private readonly Button moveDown = new Button();
        private readonly FlowLayoutPanel fieldsPanel = new FlowLayoutPanel();
        private readonly List<InspectorFieldControl> fieldControls = new List<InspectorFieldControl>();
        private readonly object item;
        private bool expanded;
        private bool editable;

        public InspectorCollectionItemControl(
            string label,
            int index,
            object item,
            bool editable,
            bool expanded)
        {
            ItemIndex = index;
            this.item = item;
            this.editable = editable;
            this.expanded = expanded;
            AutoSize = false;
            BackColor = Color.FromArgb(249, 251, 252);
            Margin = new Padding(0, 0, 0, 6);
            Padding = new Padding(1);

            header.BackColor = Color.FromArgb(242, 246, 249);
            header.Cursor = Cursors.Hand;
            header.FlatAppearance.BorderSize = 0;
            header.FlatStyle = FlatStyle.Flat;
            header.Font = InspectorFonts.Bold9;
            header.ForeColor = Color.FromArgb(62, 80, 92);
            header.Padding = new Padding(8, 0, 0, 0);
            header.Text = (expanded ? "▾  " : "▸  ") + label + " " + (index + 1);
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Click += (sender, args) => ToggleExpanded();
            Controls.Add(header);

            ConfigureMiniButton(moveUp, "↑");
            ConfigureMiniButton(moveDown, "↓");
            ConfigureMiniButton(delete, "×");
            moveUp.Click += (sender, args) => MoveRequested?.Invoke(this, -1);
            moveDown.Click += (sender, args) => MoveRequested?.Invoke(this, 1);
            delete.Click += (sender, args) => DeleteRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(moveUp);
            Controls.Add(moveDown);
            Controls.Add(delete);

            fieldsPanel.AutoSize = false;
            fieldsPanel.BackColor = Color.White;
            fieldsPanel.FlowDirection = FlowDirection.TopDown;
            fieldsPanel.Padding = new Padding(9, 4, 9, 6);
            fieldsPanel.WrapContents = false;
            fieldsPanel.Visible = expanded;
            Controls.Add(fieldsPanel);

            BuildFields();
            Resize += (sender, args) => LayoutControls();
            SetEditable(editable);
            LayoutControls();
        }

        public int ItemIndex { get; }
        public event EventHandler DeleteRequested;
        public event Action<object, int> MoveRequested;
        public event EventHandler FieldValueChanged;

        public void SetEditable(bool allowEdit)
        {
            editable = allowEdit;
            moveUp.Visible = allowEdit;
            moveDown.Visible = allowEdit;
            delete.Visible = allowEdit;
            foreach (InspectorFieldControl field in fieldControls)
            {
                field.SetEditable(allowEdit);
            }
            LayoutControls();
        }

        public void RefreshValues()
        {
            foreach (InspectorFieldControl field in fieldControls)
            {
                field.RefreshValue();
            }
        }

        public bool FocusFirstEditor()
        {
            if (!expanded)
            {
                ToggleExpanded();
            }
            return fieldControls.Any(field => field.FocusEditor());
        }

        private void BuildFields()
        {
            IReadOnlyList<InspectorFieldDefinition> definitions
                = InspectorDefinitionBuilder.BuildItemFields(item, "item");
            foreach (InspectorFieldDefinition definition in definitions)
            {
                InspectorFieldControl field;
                if (definition is InspectorValueReferenceFieldDefinition reference)
                {
                    field = new InspectorValueReferenceFieldControl(reference, editable);
                }
                else if (definition is InspectorCollectionFieldDefinition collection)
                {
                    field = new InspectorCollectionFieldControl(collection, editable);
                }
                else
                {
                    field = new InspectorScalarFieldControl(
                        (InspectorScalarFieldDefinition)definition,
                        editable);
                }
                field.FieldValueChanged += (sender, args) => FieldValueChanged?.Invoke(this, EventArgs.Empty);
                field.SizeChanged += (sender, args) => LayoutControls();
                fieldControls.Add(field);
                fieldsPanel.Controls.Add(field);
            }
        }

        private void ToggleExpanded()
        {
            expanded = !expanded;
            fieldsPanel.Visible = expanded;
            header.Text = header.Text.Replace(expanded ? "▸" : "▾", expanded ? "▾" : "▸");
            LayoutControls();
        }

        private static void ConfigureMiniButton(Button button, string text)
        {
            button.BackColor = Color.FromArgb(242, 246, 249);
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderSize = 0;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = InspectorFonts.Regular9;
            button.ForeColor = Color.FromArgb(82, 101, 114);
            button.Text = text;
        }

        private void LayoutControls()
        {
            int width = Math.Max(170, ClientSize.Width);
            header.SetBounds(1, 1, width - 2, 32);
            int right = width - 4;
            if (editable)
            {
                delete.SetBounds(right - 26, 4, 24, 26);
                right -= 27;
                moveDown.SetBounds(right - 26, 4, 24, 26);
                right -= 27;
                moveUp.SetBounds(right - 26, 4, 24, 26);
            }
            fieldsPanel.Width = width - 2;
            int fieldWidth = Math.Max(150, fieldsPanel.ClientSize.Width - fieldsPanel.Padding.Horizontal);
            foreach (InspectorFieldControl field in fieldControls)
            {
                field.Width = fieldWidth;
            }
            int fieldsHeight = fieldsPanel.GetPreferredSize(new Size(width - 2, 0)).Height;
            fieldsPanel.SetBounds(1, 34, width - 2, fieldsHeight);
            Height = expanded ? 34 + fieldsHeight + 1 : 34;
        }
    }

    internal sealed class InspectorStandardValue
    {
        public InspectorStandardValue(object value, string text)
        {
            Value = value;
            Text = text;
        }

        public object Value { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    internal static class InspectorValueConversion
    {
        public static bool HasStandardValues(object owner, PropertyDescriptor property)
        {
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            if (targetType.IsEnum)
            {
                return true;
            }
            try
            {
                return property.Converter?.GetStandardValuesSupported(
                    new InspectorTypeDescriptorContext(owner, property)) == true;
            }
            catch
            {
                return false;
            }
        }

        public static bool StandardValuesExclusive(object owner, PropertyDescriptor property)
        {
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            if (targetType.IsEnum)
            {
                return true;
            }
            try
            {
                return property.Converter?.GetStandardValuesExclusive(
                    new InspectorTypeDescriptorContext(owner, property)) == true;
            }
            catch
            {
                return false;
            }
        }

        public static IReadOnlyList<InspectorStandardValue> GetStandardValues(
            object owner,
            PropertyDescriptor property)
        {
            var result = new List<InspectorStandardValue>();
            var context = new InspectorTypeDescriptorContext(owner, property);
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            IEnumerable values;
            try
            {
                if (property.Converter?.GetStandardValuesSupported(context) == true)
                {
                    values = property.Converter.GetStandardValues(context);
                }
                else if (targetType.IsEnum)
                {
                    values = Enum.GetValues(targetType);
                }
                else
                {
                    values = Array.Empty<object>();
                }
            }
            catch
            {
                values = Array.Empty<object>();
            }
            foreach (object value in values)
            {
                result.Add(new InspectorStandardValue(value, ToDisplayText(owner, property, value)));
            }
            return result;
        }

        public static string ToDisplayText(object owner, PropertyDescriptor property, object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            var context = new InspectorTypeDescriptorContext(owner, property);
            try
            {
                if (property.Converter?.CanConvertTo(context, typeof(string)) == true)
                {
                    return property.Converter.ConvertToString(context, CultureInfo.CurrentCulture, value);
                }
            }
            catch
            {
            }
            return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        public static object FromText(object owner, PropertyDescriptor property, string text)
        {
            Type propertyType = property.PropertyType;
            Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (targetType == typeof(string))
            {
                return text;
            }
            if (string.IsNullOrWhiteSpace(text) && Nullable.GetUnderlyingType(propertyType) != null)
            {
                return null;
            }
            var context = new InspectorTypeDescriptorContext(owner, property);
            if (property.Converter?.CanConvertFrom(context, typeof(string)) == true)
            {
                return property.Converter.ConvertFromString(context, CultureInfo.CurrentCulture, text);
            }
            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, text, true);
            }
            return Convert.ChangeType(text, targetType, CultureInfo.CurrentCulture);
        }
    }
}
