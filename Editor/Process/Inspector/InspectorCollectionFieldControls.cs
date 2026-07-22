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
    internal sealed class InspectorCollectionFieldControl : InspectorFieldControl
    {
        private InspectorCollectionFieldDefinition definition;
        private readonly Label title = new Label();
        private readonly InspectorIconButton addButton = new InspectorIconButton();
        private readonly InspectorFlowPanel itemsPanel = new InspectorFlowPanel();
        private bool showAddButton;
        private bool updatingLayout;

        public InspectorCollectionFieldControl(
            InspectorCollectionFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
            : base(definition, editable, descriptionToolTip)
        {
            this.definition = definition;
            title.AutoEllipsis = true;
            title.Font = InspectorFonts.Bold9;
            title.ForeColor = UiPalette.TextPrimary;
            title.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(title);

            addButton.BackColor = UiPalette.BrandSoft;
            addButton.Cursor = Cursors.Hand;
            addButton.FlatAppearance.BorderSize = 0;
            addButton.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoftHover;
            addButton.FlatAppearance.MouseDownBackColor = UiPalette.Selection;
            addButton.FlatStyle = FlatStyle.Flat;
            addButton.Font = InspectorFonts.Regular85;
            addButton.ForeColor = UiPalette.Brand;
            addButton.IconKind = InspectorIconKind.Add;
            addButton.AccessibleName = "添加" + definition.Label;
            addButton.Text = "添加";
            addButton.Click += (sender, args) => AddItem();
            Controls.Add(addButton);

            AttachDescription(title, addButton);

            itemsPanel.AutoSize = false;
            itemsPanel.BackColor = UiPalette.Surface;
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
            showAddButton = editable && !definition.IsReadOnly
                && CanCreateItem(definition);
            addButton.Visible = showAddButton;
            addButton.Enabled = definition.Items == null
                || definition.Items.Count < GetMaxItems(definition);
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

        public override void EndEdit()
        {
            foreach (InspectorCollectionItemControl item in itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>())
            {
                item.EndEdit();
            }
        }

        public override bool CanRebind(InspectorFieldDefinition next)
        {
            if (!base.CanRebind(next))
            {
                return false;
            }
            IList items = ((InspectorCollectionFieldDefinition)next).Items;
            List<InspectorCollectionItemControl> itemControls = itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>().ToList();
            if (items == null || itemControls.Count != items.Count)
            {
                return false;
            }
            for (int index = 0; index < items.Count; index++)
            {
                if (!itemControls[index].CanRebind(items[index]))
                {
                    return false;
                }
            }
            return true;
        }

        public override void Rebind(InspectorFieldDefinition next, bool editable)
        {
            definition = (InspectorCollectionFieldDefinition)next;
            Definition = next;
            addButton.AccessibleName = "添加" + definition.Label;
            AttachDescription(title, addButton);
            Editable = editable;
            bool nextShowAddButton = editable && !definition.IsReadOnly
                && CanCreateItem(definition);
            bool layoutChanged = showAddButton != nextShowAddButton;
            showAddButton = nextShowAddButton;
            addButton.Visible = showAddButton;
            addButton.Enabled = definition.Items == null
                || definition.Items.Count < GetMaxItems(definition);
            if (!TryRebindItems())
            {
                RebuildItems();
                return;
            }
            if (layoutChanged)
            {
                LayoutControls();
            }
        }

        private bool TryRebindItems()
        {
            IList items = definition.Items;
            List<InspectorCollectionItemControl> itemControls = itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>().ToList();
            if (items == null || itemControls.Count != items.Count)
            {
                return false;
            }
            itemsPanel.SuspendLayout();
            try
            {
                for (int index = 0; index < items.Count; index++)
                {
                    if (!itemControls[index].TryRebind(
                        definition.Label,
                        index,
                        items.Count,
                        items[index],
                        Editable && !definition.IsReadOnly))
                    {
                        return false;
                    }
                }
                title.Text = $"{definition.Label}（{items.Count}）";
                return true;
            }
            finally
            {
                itemsPanel.ResumeLayout(false);
            }
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
                addButton.Enabled = items == null
                    || items.Count < GetMaxItems(definition);
                if (items == null)
                {
                    return;
                }
                var itemControls = new List<Control>(items.Count);
                for (int index = 0; index < items.Count; index++)
                {
                    object item = items[index];
                    var itemControl = new InspectorCollectionItemControl(
                        definition.Label,
                        index,
                        items.Count,
                        item,
                        Editable && !definition.IsReadOnly,
                        items.Count <= 6 || index == 0,
                        DescriptionToolTip);
                    itemControl.DeleteRequested += (sender, args) => DeleteItem(itemControl.ItemIndex);
                    itemControl.MoveRequested += (sender, offset) => MoveItem(itemControl.ItemIndex, offset);
                    itemControl.FieldValueChanged += (sender, args) => OnFieldValueChanged();
                    itemControl.SizeChanged += (sender, args) => LayoutControls();
                    itemControls.Add(itemControl);
                }
                itemsPanel.Controls.AddRange(itemControls.ToArray());
            }
            finally
            {
                itemsPanel.ResumeLayout(true);
                LayoutControls();
            }
        }

        private void AddItem()
        {
            if (!TryAddItem(
                definition,
                out Action rollback,
                out string error))
            {
                MessageBox.Show(error, "添加配置项失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try
            {
                RebuildItems();
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                rollback?.Invoke();
                MessageBox.Show(
                    ex.Message,
                    "添加配置项失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                RebuildItems();
            }
        }

        private void DeleteItem(int index)
        {
            if (!TryDeleteItem(
                definition,
                index,
                out Action rollback,
                out string error))
            {
                MessageBox.Show(error, "删除配置项失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                RebuildItems();
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                rollback?.Invoke();
                MessageBox.Show(
                    ex.Message,
                    "删除配置项失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                RebuildItems();
            }
        }

        private void MoveItem(int index, int offset)
        {
            if (!TryMoveItem(
                definition,
                index,
                offset,
                out Action rollback,
                out string error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MessageBox.Show(error, "移动配置项失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }
            try
            {
                RebuildItems();
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                rollback?.Invoke();
                MessageBox.Show(ex.Message, "移动配置项失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RebuildItems();
            }
        }

        private static bool CanCreateItem(InspectorCollectionFieldDefinition field)
        {
            Type itemType = field?.ItemType;
            return itemType != null
                && !itemType.IsAbstract
                && itemType.GetConstructor(Type.EmptyTypes) != null;
        }

        private static int GetMinItems(InspectorCollectionFieldDefinition field)
        {
            InlineListAttribute attribute =
                field?.Property?.Attributes[typeof(InlineListAttribute)] as InlineListAttribute;
            return Math.Max(0, attribute?.MinItems ?? 0);
        }

        private static int GetMaxItems(InspectorCollectionFieldDefinition field)
        {
            InlineListAttribute attribute =
                field?.Property?.Attributes[typeof(InlineListAttribute)] as InlineListAttribute;
            return Math.Max(GetMinItems(field), attribute?.MaxItems ?? int.MaxValue);
        }

        private static bool TryAddItem(
            InspectorCollectionFieldDefinition field,
            out Action rollback,
            out string error)
        {
            rollback = null;
            error = null;
            IList items = field?.Items;
            Type itemType = field?.ItemType;
            if (items == null || itemType == null)
            {
                error = "集合或元素类型不存在。";
                return false;
            }
            if (!CanCreateItem(field))
            {
                error = $"{field.Label}的元素类型无法直接创建。";
                return false;
            }
            if (items.Count >= GetMaxItems(field))
            {
                error = $"{field.Label}最多允许 {GetMaxItems(field)} 项。";
                return false;
            }

            try
            {
                object item = Activator.CreateInstance(itemType);
                if (item is ProcParam process) process.DelayAfterMs = -1;
                int insertedIndex = items.Count;
                try
                {
                    items.Add(item);
                }
                catch
                {
                    if (items.Count > insertedIndex) items.RemoveAt(insertedIndex);
                    throw;
                }
                rollback = () =>
                {
                    if (insertedIndex >= 0 && insertedIndex < items.Count)
                        items.RemoveAt(insertedIndex);
                };
                return true;
            }
            catch (Exception ex)
            {
                error = Unwrap(ex).Message;
                return false;
            }
        }

        private static bool TryDeleteItem(
            InspectorCollectionFieldDefinition field,
            int index,
            out Action rollback,
            out string error)
        {
            rollback = null;
            error = null;
            IList items = field?.Items;
            if (items == null || index < 0 || index >= items.Count)
            {
                error = "待删除的集合项已失效。";
                return false;
            }
            if (items.Count <= GetMinItems(field))
            {
                error = $"{field.Label}至少需要 {GetMinItems(field)} 项。";
                return false;
            }

            object removed = items[index];
            try
            {
                items.RemoveAt(index);
                rollback = () => items.Insert(index, removed);
                return true;
            }
            catch (Exception ex)
            {
                error = Unwrap(ex).Message;
                return false;
            }
        }

        private static bool TryMoveItem(
            InspectorCollectionFieldDefinition field,
            int index,
            int offset,
            out Action rollback,
            out string error)
        {
            rollback = null;
            error = null;
            IList items = field?.Items;
            int target = index + offset;
            if (items == null || index < 0 || index >= items.Count
                || target < 0 || target >= items.Count)
            {
                error = "移动集合项的位置已失效。";
                return false;
            }
            object item = items[index];
            bool removed = false;
            try
            {
                items.RemoveAt(index);
                removed = true;
                items.Insert(target, item);
                rollback = () =>
                {
                    items.RemoveAt(target);
                    items.Insert(index, item);
                };
                return true;
            }
            catch (Exception ex)
            {
                error = Unwrap(ex).Message;
                try
                {
                    if (removed && !items.Contains(item)) items.Insert(index, item);
                }
                catch (Exception rollbackException)
                {
                    error += $"；回滚失败：{Unwrap(rollbackException).Message}";
                }
                return false;
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }

        private void LayoutControls()
        {
            if (updatingLayout)
            {
                return;
            }
            updatingLayout = true;
            try
            {
                int width = Math.Max(180, ClientSize.Width);
                int right = width;
                if (showAddButton)
                {
                    addButton.SetBounds(Math.Max(0, right - 60), 1, 60, 23);
                    right -= 64;
                }
                title.SetBounds(0, 0, Math.Max(70, right), 25);
                foreach (InspectorCollectionItemControl item in itemsPanel.Controls
                    .OfType<InspectorCollectionItemControl>())
                {
                    item.Width = width;
                }
                int itemsHeight = itemsPanel.GetPreferredSize(new Size(width, 0)).Height;
                itemsPanel.SetBounds(0, 26, width, itemsHeight);
                Height = 26 + itemsHeight;
            }
            finally
            {
                updatingLayout = false;
            }
        }
    }

    internal sealed class InspectorCollectionItemControl : UserControl
    {
        private const int HeaderHeight = 25;
        private readonly InspectorSectionButton header = new InspectorSectionButton();
        private readonly InspectorIconButton delete = new InspectorIconButton();
        private readonly InspectorIconButton moveUp = new InspectorIconButton();
        private readonly InspectorIconButton moveDown = new InspectorIconButton();
        private readonly InspectorFlowPanel fieldsPanel = new InspectorFlowPanel();
        private readonly List<InspectorFieldControl> fieldControls = new List<InspectorFieldControl>();
        private object item;
        private string itemLabel;
        private int itemCount;
        private readonly ToolTip descriptionToolTip;
        private bool expanded;
        private bool editable;
        private bool updatingLayout;

        public InspectorCollectionItemControl(
            string label,
            int index,
            int itemCount,
            object item,
            bool editable,
            bool expanded,
            ToolTip descriptionToolTip)
        {
            ItemIndex = index;
            this.itemCount = itemCount;
            this.item = item;
            itemLabel = label;
            this.descriptionToolTip = descriptionToolTip;
            this.editable = editable;
            this.expanded = expanded;
            AutoSize = false;
            BackColor = UiPalette.Stroke;
            Margin = new Padding(0, 0, 0, 1);
            Padding = new Padding(1);

            header.BackColor = UiPalette.SurfaceSubtle;
            header.AutoEllipsis = true;
            header.Cursor = Cursors.Hand;
            header.FlatAppearance.BorderSize = 0;
            header.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoft;
            header.FlatAppearance.MouseDownBackColor = UiPalette.BrandSoftHover;
            header.FlatStyle = FlatStyle.Flat;
            header.Font = InspectorFonts.Bold9;
            header.ForeColor = UiPalette.TextPrimary;
            header.Expanded = expanded;
            header.ShowDivider = false;
            header.Click += (sender, args) => ToggleExpanded();
            Controls.Add(header);

            ConfigureMiniButton(moveUp, InspectorIconKind.MoveUp);
            ConfigureMiniButton(moveDown, InspectorIconKind.MoveDown);
            ConfigureMiniButton(delete, InspectorIconKind.Delete);
            moveUp.AccessibleName = "上移配置项";
            moveDown.AccessibleName = "下移配置项";
            delete.AccessibleName = "删除配置项";
            delete.ForeColor = UiPalette.Danger;
            delete.FlatAppearance.MouseOverBackColor = UiPalette.DangerSoft;
            moveUp.Click += (sender, args) => MoveRequested?.Invoke(this, -1);
            moveDown.Click += (sender, args) => MoveRequested?.Invoke(this, 1);
            delete.Click += (sender, args) => DeleteRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(moveUp);
            Controls.Add(moveDown);
            Controls.Add(delete);

            fieldsPanel.AutoSize = false;
            fieldsPanel.BackColor = UiPalette.Surface;
            fieldsPanel.FlowDirection = FlowDirection.TopDown;
            fieldsPanel.Padding = new Padding(3, 0, 3, 1);
            fieldsPanel.WrapContents = false;
            fieldsPanel.Visible = expanded;
            Controls.Add(fieldsPanel);

            BuildFields();
            UpdateHeaderText();
            Resize += (sender, args) =>
            {
                LayoutControls();
            };
            SetEditable(editable);
            LayoutControls();
        }

        public int ItemIndex { get; private set; }
        public event EventHandler DeleteRequested;
        public event Action<object, int> MoveRequested;
        public event EventHandler FieldValueChanged;

        public void SetEditable(bool allowEdit)
        {
            editable = allowEdit;
            moveUp.Visible = allowEdit;
            moveDown.Visible = allowEdit;
            delete.Visible = allowEdit;
            moveUp.Enabled = allowEdit && ItemIndex > 0;
            moveDown.Enabled = allowEdit && ItemIndex < itemCount - 1;
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
            UpdateHeaderText();
        }

        public bool FocusFirstEditor()
        {
            if (!expanded)
            {
                ToggleExpanded();
            }
            return fieldControls.Any(field => field.FocusEditor());
        }

        public void EndEdit()
        {
            foreach (InspectorFieldControl field in fieldControls)
            {
                field.EndEdit();
            }
        }

        public bool TryRebind(
            string label,
            int index,
            int nextItemCount,
            object nextItem,
            bool allowEdit)
        {
            IReadOnlyList<InspectorFieldDefinition> definitions
                = InspectorDefinitionBuilder.BuildItemFields(nextItem, "item", index);
            if (definitions.Count != fieldControls.Count)
            {
                return false;
            }
            for (int fieldIndex = 0; fieldIndex < definitions.Count; fieldIndex++)
            {
                if (!fieldControls[fieldIndex].CanRebind(definitions[fieldIndex]))
                {
                    return false;
                }
            }

            itemLabel = label;
            ItemIndex = index;
            itemCount = nextItemCount;
            item = nextItem;
            bool layoutChanged = editable != allowEdit;
            for (int fieldIndex = 0; fieldIndex < definitions.Count; fieldIndex++)
            {
                fieldControls[fieldIndex].Rebind(definitions[fieldIndex], allowEdit);
            }
            editable = allowEdit;
            moveUp.Visible = allowEdit;
            moveDown.Visible = allowEdit;
            delete.Visible = allowEdit;
            moveUp.Enabled = allowEdit && ItemIndex > 0;
            moveDown.Enabled = allowEdit && ItemIndex < itemCount - 1;
            UpdateHeaderText();
            if (layoutChanged)
            {
                LayoutControls();
            }
            return true;
        }

        public bool CanRebind(object nextItem)
        {
            IReadOnlyList<InspectorFieldDefinition> definitions
                = InspectorDefinitionBuilder.BuildItemFields(nextItem, "item", ItemIndex);
            if (definitions.Count != fieldControls.Count)
            {
                return false;
            }
            for (int index = 0; index < definitions.Count; index++)
            {
                if (!fieldControls[index].CanRebind(definitions[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private void BuildFields()
        {
            IReadOnlyList<InspectorFieldDefinition> definitions
                = InspectorDefinitionBuilder.BuildItemFields(item, "item", ItemIndex);
            var builtFields = new List<Control>(definitions.Count);
            foreach (InspectorFieldDefinition definition in definitions)
            {
                InspectorFieldControl field;
                if (definition is InspectorValueReferenceFieldDefinition reference)
                {
                    field = new InspectorValueReferenceFieldControl(
                        reference,
                        editable,
                        descriptionToolTip);
                }
                else if (definition is InspectorCollectionFieldDefinition collection)
                {
                    field = new InspectorCollectionFieldControl(
                        collection,
                        editable,
                        descriptionToolTip);
                }
                else
                {
                    field = new InspectorScalarFieldControl(
                        (InspectorScalarFieldDefinition)definition,
                        editable,
                        descriptionToolTip);
                }
                field.FieldValueChanged += (sender, args) =>
                {
                    UpdateHeaderText();
                    FieldValueChanged?.Invoke(this, EventArgs.Empty);
                };
                field.SizeChanged += (sender, args) => LayoutControls();
                fieldControls.Add(field);
                builtFields.Add(field);
            }
            fieldsPanel.Controls.AddRange(builtFields.ToArray());
        }

        private void ToggleExpanded()
        {
            expanded = !expanded;
            if (!expanded)
            {
                EndEdit();
            }
            fieldsPanel.Visible = expanded;
            header.Expanded = expanded;
            UpdateHeaderText();
            LayoutControls();
        }

        private void UpdateHeaderText()
        {
            header.Text = string.Equals(itemLabel, "条件", StringComparison.Ordinal)
                ? "第 " + (ItemIndex + 1) + " 条"
                : itemLabel + " " + (ItemIndex + 1);
        }

        private static void ConfigureMiniButton(
            InspectorIconButton button,
            InspectorIconKind iconKind)
        {
            button.BackColor = UiPalette.SurfaceSubtle;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoft;
            button.FlatAppearance.MouseDownBackColor = UiPalette.BrandSoftHover;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = InspectorFonts.Regular9;
            button.ForeColor = UiPalette.TextSecondary;
            button.IconKind = iconKind;
            button.Text = string.Empty;
        }

        private void LayoutControls()
        {
            if (updatingLayout)
            {
                return;
            }
            updatingLayout = true;
            try
            {
                int width = Math.Max(170, ClientSize.Width);
                int right = width - 4;
                if (editable)
                {
                    delete.SetBounds(right - 22, 2, 21, 21);
                    right -= 25;
                    moveDown.SetBounds(right - 22, 2, 21, 21);
                    right -= 25;
                    moveUp.SetBounds(right - 22, 2, 21, 21);
                    right -= 25;
                }
                header.SetBounds(1, 1, Math.Max(80, right - 1), HeaderHeight);
                fieldsPanel.Width = width - 2;
                int fieldWidth = Math.Max(
                    150,
                    fieldsPanel.ClientSize.Width - fieldsPanel.Padding.Horizontal);
                foreach (InspectorFieldControl field in fieldControls)
                {
                    field.Width = fieldWidth;
                }
                int fieldsHeight = fieldsPanel.GetPreferredSize(new Size(width - 2, 0)).Height;
                fieldsPanel.SetBounds(1, HeaderHeight + 2, width - 2, fieldsHeight);
                Height = expanded
                    ? HeaderHeight + 2 + fieldsHeight + 1
                    : HeaderHeight + 2;
            }
            finally
            {
                updatingLayout = false;
            }
        }
    }

}
