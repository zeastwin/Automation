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
    internal sealed class InspectorSectionControl : UserControl
    {
        private const int HeaderHeight = 26;
        private readonly InspectorSectionButton headerButton = new InspectorSectionButton();
        private readonly InspectorFlowPanel body = new InspectorFlowPanel();
        private readonly List<InspectorFieldControl> fields = new List<InspectorFieldControl>();
        private bool expanded = true;
        private bool updatingLayout;

        public InspectorSectionControl(
            InspectorSectionDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
        {
            AutoSize = false;
            BackColor = UiPalette.SurfaceStrong;
            Margin = new Padding(0, 0, 0, 2);
            Padding = Padding.Empty;

            headerButton.AutoSize = false;
            headerButton.BackColor = UiPalette.Background;
            headerButton.Cursor = Cursors.Hand;
            headerButton.FlatAppearance.BorderSize = 0;
            headerButton.FlatAppearance.MouseOverBackColor = UiPalette.SurfaceHover;
            headerButton.FlatAppearance.MouseDownBackColor = UiPalette.SurfacePressed;
            headerButton.FlatStyle = FlatStyle.Flat;
            headerButton.Font = InspectorFonts.Bold9;
            headerButton.ForeColor = UiPalette.TextPrimary;
            headerButton.Height = HeaderHeight;
            headerButton.IconKind = InspectorIcons.FromSectionTitle(definition.Title);
            headerButton.Expanded = expanded;
            headerButton.Text = definition.Title;
            headerButton.Visible = definition.ShowHeader;
            headerButton.Click += (sender, args) => ToggleExpanded();
            Controls.Add(headerButton);

            body.AutoSize = false;
            body.BackColor = UiPalette.SurfaceStrong;
            body.FlowDirection = FlowDirection.TopDown;
            body.Padding = new Padding(4, 0, 4, 1);
            body.WrapContents = false;
            Controls.Add(body);
            body.BringToFront();

            var editors = new List<Control>();
            foreach (InspectorFieldDefinition field in definition.Fields)
            {
                InspectorFieldControl editor = CreateEditor(
                    field,
                    editable,
                    descriptionToolTip);
                editor.FieldValueChanged += (sender, args) => FieldValueChanged?.Invoke(this, EventArgs.Empty);
                editor.SizeChanged += (sender, args) => UpdateWidths();
                fields.Add(editor);
                editors.Add(editor);
            }
            body.Controls.AddRange(editors.ToArray());
            Resize += (sender, args) =>
            {
                UpdateWidths();
            };
        }

        public event EventHandler FieldValueChanged;

        internal bool Expanded => expanded;

        internal void SetExpanded(bool value)
        {
            if (expanded == value)
            {
                return;
            }
            expanded = value;
            if (!expanded)
            {
                foreach (InspectorFieldControl field in fields)
                {
                    field.EndEdit();
                }
            }
            body.Visible = expanded;
            headerButton.Expanded = expanded;
            UpdateWidths();
        }

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

        public void PrewarmSelectionPickers(
            InspectorSelectionPickerPrewarmSession session)
        {
            foreach (InspectorFieldControl field in fields)
            {
                field.PrewarmSelectionPickers(session);
            }
        }

        public bool CanRebind(InspectorSectionDefinition definition)
        {
            if (definition == null || fields.Count != definition.Fields.Count)
            {
                return false;
            }
            for (int index = 0; index < fields.Count; index++)
            {
                if (!fields[index].CanRebind(definition.Fields[index]))
                {
                    return false;
                }
            }
            return true;
        }

        public void Rebind(InspectorSectionDefinition definition, bool editable)
        {
            headerButton.IconKind = InspectorIcons.FromSectionTitle(definition.Title);
            headerButton.Text = definition.Title;
            headerButton.Visible = definition.ShowHeader;
            for (int index = 0; index < fields.Count; index++)
            {
                fields[index].Rebind(definition.Fields[index], editable);
            }
            UpdateWidths();
        }

        private static InspectorFieldControl CreateEditor(
            InspectorFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
        {
            if (definition is InspectorValueReferenceFieldDefinition reference)
            {
                return new InspectorValueReferenceFieldControl(
                    reference,
                    editable,
                    descriptionToolTip);
            }
            if (definition is InspectorCollectionFieldDefinition collection)
            {
                return new InspectorCollectionFieldControl(
                    collection,
                    editable,
                    descriptionToolTip);
            }
            return new InspectorScalarFieldControl(
                (InspectorScalarFieldDefinition)definition,
                editable,
                descriptionToolTip);
        }

        private void ToggleExpanded()
        {
            SetExpanded(!expanded);
        }

        private void UpdateWidths()
        {
            if (updatingLayout)
            {
                return;
            }
            updatingLayout = true;
            try
            {
                int width = Math.Max(180, ClientSize.Width);
                int headerHeight = headerButton.Visible ? HeaderHeight : 0;
                headerButton.SetBounds(0, 0, width, headerHeight);
                body.Width = width;
                int fieldWidth = Math.Max(180, body.ClientSize.Width - body.Padding.Horizontal);
                foreach (InspectorFieldControl field in fields)
                {
                    field.Width = fieldWidth;
                }
                int bodyHeight = body.GetPreferredSize(new Size(width, 0)).Height;
                body.SetBounds(0, headerHeight, width, bodyHeight);
                Height = headerHeight + (expanded ? bodyHeight : 0);
            }
            finally
            {
                updatingLayout = false;
            }
        }
    }

    internal abstract class InspectorFieldControl : UserControl
    {
        protected const int PropertyRowHeight = 26;
        protected const int PropertyEditorHeight = 24;
        protected InspectorFieldDefinition Definition;
        protected readonly ToolTip DescriptionToolTip;
        protected bool Editable;

        protected InspectorFieldControl(
            InspectorFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
        {
            Definition = definition;
            DescriptionToolTip = descriptionToolTip;
            Editable = editable;
            AutoSize = false;
            BackColor = UiPalette.SurfaceStrong;
            Margin = Padding.Empty;
        }

        public event EventHandler FieldValueChanged;

        public abstract void SetEditable(bool editable);
        public abstract void RefreshValue();
        public abstract bool FocusEditor();
        public abstract void Rebind(InspectorFieldDefinition definition, bool editable);

        public virtual void EndEdit()
        {
        }

        public virtual void PrewarmSelectionPickers(
            InspectorSelectionPickerPrewarmSession session)
        {
        }

        public virtual bool CanRebind(InspectorFieldDefinition definition)
        {
            return definition != null
                && Definition.GetType() == definition.GetType();
        }

        protected void OnFieldValueChanged()
        {
            FieldValueChanged?.Invoke(this, EventArgs.Empty);
        }

        protected void AttachDescription(params Control[] controls)
        {
            if (DescriptionToolTip == null)
            {
                return;
            }
            string description = Definition.Description ?? string.Empty;
            foreach (Control control in controls.Where(control => control != null))
            {
                control.AccessibleDescription = description;
                DescriptionToolTip.SetToolTip(control, description);
            }
        }

        protected void DrawPropertyRowBackground(PaintEventArgs e, int labelWidth)
        {
            using (var labelBrush = new SolidBrush(UiPalette.Surface))
            using (var valueBrush = new SolidBrush(UiPalette.SurfaceStrong))
            {
                e.Graphics.FillRectangle(
                    labelBrush,
                    new Rectangle(0, 0, labelWidth, PropertyRowHeight));
                e.Graphics.FillRectangle(
                    valueBrush,
                    new Rectangle(
                        labelWidth,
                        0,
                        Math.Max(1, Width - labelWidth),
                        PropertyRowHeight));
            }
            using (var divider = new Pen(UiPalette.Divider))
            {
                e.Graphics.DrawLine(
                    divider,
                    Math.Max(0, labelWidth - 1),
                    0,
                    Math.Max(0, labelWidth - 1),
                    Math.Max(0, Height - 1));
                e.Graphics.DrawLine(
                    divider,
                    0,
                    Math.Max(0, Height - 1),
                    Math.Max(0, Width - 1),
                    Math.Max(0, Height - 1));
            }
        }

        protected static int GetLabelWidth(int availableWidth)
        {
            return Math.Min(104, Math.Max(84, availableWidth * 28 / 100));
        }

        protected static void PopulateStandardValues(
            InspectorComboBox comboBox,
            object owner,
            PropertyDescriptor property,
            object currentValue,
            bool includeOptions)
        {
            comboBox.BeginUpdate();
            try
            {
                comboBox.Items.Clear();
                if (includeOptions)
                {
                    foreach (InspectorStandardValue option
                        in InspectorValueConversion.GetStandardValues(owner, property))
                    {
                        comboBox.Items.Add(option);
                    }
                }
                InspectorStandardValue selected = comboBox.Items
                    .Cast<InspectorStandardValue>()
                    .FirstOrDefault(option => Equals(option.Value, currentValue));
                if (selected != null)
                {
                    comboBox.SelectedItem = selected;
                    return;
                }

                string displayText = InspectorValueConversion.ToDisplayText(
                    owner,
                    property,
                    currentValue);
                comboBox.SelectedIndex = -1;
                if (comboBox.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        var current = new InspectorStandardValue(currentValue, displayText);
                        comboBox.Items.Add(current);
                        comboBox.SelectedItem = current;
                    }
                }
                else
                {
                    comboBox.Text = displayText;
                }
            }
            finally
            {
                comboBox.EndUpdate();
                comboBox.ClearTextSelection();
            }
        }
    }

}
