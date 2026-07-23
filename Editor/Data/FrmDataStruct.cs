// 模块：编辑器 / 数据配置。
// 职责范围：数据结构与报警配置的编辑交互。

using Automation.ParamFrm;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmDataStruct : Form
    {
        private const int TextPreviewLength = 30;
        private const int TvmSetExtendedStyle = 0x112C;
        private const int TvsExDoubleBuffer = 0x0004;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(
            IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        private enum DataStructNodeType
        {
            Root,
            Struct,
            Item,
            Field,
            Placeholder
        }

        private class DataStructNodeTag
        {
            public DataStructNodeType NodeType { get; set; }
            public int StructIndex { get; set; } = -1;
            public int ItemIndex { get; set; } = -1;
            public int FieldIndex { get; set; } = -1;
            public string FieldName { get; set; } = string.Empty;
            public string DisplayValue { get; set; } = string.Empty;
            public string EditValue { get; set; } = string.Empty;
            public DataStructValueType FieldType { get; set; } = DataStructValueType.Text;
            public int FieldNameColumnWidth { get; set; } = 80;
        }

        private struct FieldNodeLayout
        {
            public Rectangle RowBounds { get; set; }
            public Rectangle IndexBounds { get; set; }
            public Rectangle NameBounds { get; set; }
            public Rectangle ValueBounds { get; set; }
            public Rectangle ValueTextBounds { get; set; }
        }

        private class DataStructClipboard
        {
            public DataStructNodeType NodeType { get; set; }
            public DataStruct StructData { get; set; }
            public DataStructItem ItemData { get; set; }
            public string FieldName { get; set; }
            public DataStructValueType FieldType { get; set; }
            public object FieldValue { get; set; }
        }

        private DataStructClipboard clipboard;
        private readonly TextBox inlineValueEditor;
        private TreeNode inlineValueNode;
        private bool inlineValueEditEnding;

        public FrmDataStruct()
        {
            InitializeComponent();
            treeView1.HideSelection = false;
            treeView1.ShowNodeToolTips = false;
            treeView1.BackColor = UiPalette.SurfaceStrong;
            treeView1.ForeColor = UiPalette.TextPrimary;
            treeView1.DrawMode = TreeViewDrawMode.OwnerDrawText;
            treeView1.DrawNode += treeView1_DrawNode;
            treeView1.MouseWheel += treeView1_MouseWheel;
            treeView1.HandleCreated += treeView1_HandleCreated;
            EnableTreeViewDoubleBuffer();
            inlineValueEditor = new TextBox
            {
                AutoSize = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = treeView1.Font,
                Visible = false
            };
            inlineValueEditor.KeyDown += inlineValueEditor_KeyDown;
            inlineValueEditor.LostFocus += inlineValueEditor_LostFocus;
            treeView1.Controls.Add(inlineValueEditor);
            AddCommonMenuItems(contextMenuRoot);
            AddCommonMenuItems(contextMenuStruct);
            AddCommonMenuItems(contextMenuItem);
            AddCommonMenuItems(contextMenuField);
        }

        private void treeView1_HandleCreated(object sender, EventArgs e)
        {
            EnableTreeViewDoubleBuffer();
        }

        private void EnableTreeViewDoubleBuffer()
        {
            if (!treeView1.IsHandleCreated)
            {
                return;
            }
            SendMessage(treeView1.Handle, TvmSetExtendedStyle,
                new IntPtr(TvsExDoubleBuffer), new IntPtr(TvsExDoubleBuffer));
        }

        private void FrmDataStruct_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (!EndInlineValueEdit(true))
                {
                    e.Cancel = true;
                    return;
                }
                e.Cancel = true;
                Hide();
            }
        }

        private void FrmDataStruct_Load(object sender, EventArgs e)
        {
            RefreshDataSturctList();
        }

        public void RefreshDataSturctList()
        {
            ReloadTree();
        }

        public void RefreshDataSturctTree()
        {
            ReloadTree();
        }

        private void ReloadTree()
        {
            if (!EndInlineValueEdit(true))
            {
                return;
            }
            treeView1.BeginUpdate();
            try
            {
                treeView1.Nodes.Clear();

                List<DataStruct> snapshot = Workspace.Runtime.Stores.DataStructures.GetSnapshot();
                if (snapshot != null)
                {
                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        DataStruct dataStruct = snapshot[i];
                        TreeNode structNode = BuildStructNode(i, dataStruct);
                        treeView1.Nodes.Add(structNode);
                    }
                }
            }
            finally
            {
                treeView1.EndUpdate();
            }
        }

        private void AddCommonMenuItems(ContextMenuStrip menu)
        {
            if (menu == null)
            {
                return;
            }
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
            }
            menu.Items.Add(new ToolStripMenuItem("全部展开(&O)", null, menuExpandAll_Click));
            menu.Items.Add(new ToolStripMenuItem("全部折叠(&B)", null, menuCollapseAll_Click));
            menu.Items.Add(new ToolStripMenuItem("复制(&C)", null, menuCopy_Click));
            menu.Items.Add(new ToolStripMenuItem("粘贴(&V)", null, menuPaste_Click));
            menu.Items.Add(new ToolStripMenuItem("刷新", null, menuRefresh_Click));
        }

        private TreeNode BuildStructNode(int structIndex, DataStruct dataStruct)
        {
            string name = dataStruct?.Name ?? string.Empty;
            TreeNode structNode = new TreeNode(BuildStructNodeText(structIndex, name))
            {
                Tag = new DataStructNodeTag
                {
                    NodeType = DataStructNodeType.Struct,
                    StructIndex = structIndex
                }
            };

            if (dataStruct?.dataStructItems == null)
            {
                return structNode;
            }

            for (int i = 0; i < dataStruct.dataStructItems.Count; i++)
            {
                DataStructItem item = dataStruct.dataStructItems[i];
                TreeNode itemNode = BuildItemNode(structIndex, i, item);
                structNode.Nodes.Add(itemNode);
            }

            return structNode;
        }

        private TreeNode BuildItemNode(int structIndex, int itemIndex, DataStructItem item)
        {
            string name = item?.Name ?? string.Empty;
            TreeNode itemNode = new TreeNode(BuildItemNodeText(itemIndex, name))
            {
                Tag = new DataStructNodeTag
                {
                    NodeType = DataStructNodeType.Item,
                    StructIndex = structIndex,
                    ItemIndex = itemIndex,
                    FieldNameColumnWidth = CalculateFieldNameColumnWidth(
                        item?.FieldNames?.Values)
                }
            };

            if (item == null)
            {
                return itemNode;
            }

            if (GetFieldIndexes(item).Count > 0)
            {
                TreeNode placeholder = new TreeNode("加载中...")
                {
                    Tag = new DataStructNodeTag { NodeType = DataStructNodeType.Placeholder }
                };
                itemNode.Nodes.Add(placeholder);
            }

            return itemNode;
        }

        private static string BuildStructNodeText(int structIndex, string name)
        {
            return $"{structIndex}:{name}";
        }

        private static string BuildItemNodeText(int itemIndex, string name)
        {
            return $"{itemIndex}:{name}";
        }

        private static string BuildFieldNodeText(int fieldIndex, string fieldName, string value)
        {
            return $"{fieldIndex}:{fieldName}    {value}";
        }

        private int CalculateFieldNameColumnWidth(IEnumerable<string> fieldNames)
        {
            int width = 0;
            if (fieldNames != null)
            {
                foreach (string fieldName in fieldNames)
                {
                    width = Math.Max(width,
                        TextRenderer.MeasureText(fieldName ?? string.Empty, treeView1.Font).Width);
                }
            }
            return Math.Min(210, Math.Max(80, width + 10));
        }

        private bool RefreshItemFieldNameColumnWidth(TreeNode itemNode)
        {
            DataStructNodeTag itemTag = itemNode?.Tag as DataStructNodeTag;
            if (itemTag == null || itemTag.NodeType != DataStructNodeType.Item)
            {
                return false;
            }
            int nextWidth = CalculateFieldNameColumnWidth(
                itemNode.Nodes.Cast<TreeNode>()
                    .Select(node => node.Tag as DataStructNodeTag)
                    .Where(tag => tag != null && tag.NodeType == DataStructNodeType.Field)
                    .Select(tag => tag.FieldName));
            if (itemTag.FieldNameColumnWidth == nextWidth)
            {
                return false;
            }
            itemTag.FieldNameColumnWidth = nextWidth;
            InvalidateItemFieldRows(itemNode);
            return true;
        }

        private static void InvalidateItemFieldRows(TreeNode itemNode)
        {
            TreeView treeView = itemNode?.TreeView;
            if (treeView == null || !itemNode.IsExpanded)
            {
                return;
            }
            Rectangle invalidBounds = Rectangle.Empty;
            foreach (TreeNode node in itemNode.Nodes)
            {
                DataStructNodeTag tag = node.Tag as DataStructNodeTag;
                if (tag == null || tag.NodeType != DataStructNodeType.Field || !node.IsVisible)
                {
                    continue;
                }
                Rectangle rowBounds = node.Bounds;
                rowBounds.Width = Math.Max(0, treeView.ClientSize.Width - rowBounds.X);
                invalidBounds = invalidBounds.IsEmpty
                    ? rowBounds
                    : Rectangle.Union(invalidBounds, rowBounds);
            }
            if (!invalidBounds.IsEmpty)
            {
                treeView.Invalidate(invalidBounds);
            }
        }

        private static void InvalidateFieldRow(TreeNode fieldNode)
        {
            TreeView treeView = fieldNode?.TreeView;
            if (treeView == null || !fieldNode.IsVisible)
            {
                return;
            }
            Rectangle rowBounds = fieldNode.Bounds;
            rowBounds.Width = Math.Max(0, treeView.ClientSize.Width - rowBounds.X);
            treeView.Invalidate(rowBounds);
        }

        private bool TryGetFieldNodeLayout(TreeNode fieldNode, Rectangle nodeBounds,
            out FieldNodeLayout layout)
        {
            layout = default;
            Rectangle rowBounds = new Rectangle(
                nodeBounds.X,
                nodeBounds.Y,
                Math.Max(0, treeView1.ClientSize.Width - nodeBounds.X),
                nodeBounds.Height);
            if (rowBounds.Width <= 6)
            {
                return false;
            }

            int right = rowBounds.Right - 4;
            int x = rowBounds.X + 2;
            int indexWidth = Math.Min(54, Math.Max(28,
                TextRenderer.MeasureText("999:", treeView1.Font).Width + 2));
            int indexActualWidth = Math.Max(0, Math.Min(indexWidth, right - x));
            var indexBounds = new Rectangle(x, rowBounds.Y, indexActualWidth, rowBounds.Height);
            x += indexWidth;

            int availableWidth = right - x;
            if (availableWidth <= 0)
            {
                return false;
            }
            DataStructNodeTag itemTag = fieldNode?.Parent?.Tag as DataStructNodeTag;
            int preferredNameWidth = itemTag?.FieldNameColumnWidth ?? 80;
            int nameWidth = Math.Min(preferredNameWidth,
                Math.Max(80, availableWidth - 104));
            nameWidth = Math.Min(nameWidth, availableWidth);
            var nameBounds = new Rectangle(x, rowBounds.Y, nameWidth, rowBounds.Height);
            x += nameWidth + 8;

            int valueWidth = Math.Max(0, right - x);
            var valueBounds = new Rectangle(x, rowBounds.Y + 1, valueWidth,
                Math.Max(1, rowBounds.Height - 2));
            var valueTextBounds = new Rectangle(valueBounds.X + 7, valueBounds.Y,
                Math.Max(0, valueBounds.Width - 12), valueBounds.Height);
            layout = new FieldNodeLayout
            {
                RowBounds = rowBounds,
                IndexBounds = indexBounds,
                NameBounds = nameBounds,
                ValueBounds = valueBounds,
                ValueTextBounds = valueTextBounds
            };
            return true;
        }

        private void treeView1_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            DataStructNodeTag tag = e.Node?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Field)
            {
                e.DrawDefault = true;
                return;
            }

            if (!TryGetFieldNodeLayout(e.Node, e.Bounds, out FieldNodeLayout layout))
            {
                return;
            }

            bool selected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
            Color rowBackColor = selected ? UiPalette.Selection : treeView1.BackColor;
            using (var rowBrush = new SolidBrush(rowBackColor))
            {
                e.Graphics.FillRectangle(rowBrush, layout.RowBounds);
            }

            const TextFormatFlags flags = TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis;
            string indexText = $"{tag.FieldIndex}:";
            TextRenderer.DrawText(e.Graphics, indexText, treeView1.Font, layout.IndexBounds,
                selected ? UiPalette.SelectionText : UiPalette.TextMuted, flags);
            TextRenderer.DrawText(e.Graphics, tag.FieldName ?? string.Empty, treeView1.Font,
                layout.NameBounds, UiPalette.TextPrimary, flags);

            if (layout.ValueBounds.Width > 20)
            {
                Color valueColor = tag.FieldType == DataStructValueType.Number
                    ? UiPalette.Success
                    : UiPalette.Brand;
                Color valueBackColor = tag.FieldType == DataStructValueType.Number
                    ? UiPalette.SuccessSoft
                    : UiPalette.BrandSoft;
                using (var valueBrush = new SolidBrush(valueBackColor))
                {
                    e.Graphics.FillRectangle(valueBrush, layout.ValueBounds);
                }
                if (layout.ValueTextBounds.Width > 0)
                {
                    TextRenderer.DrawText(e.Graphics, tag.DisplayValue ?? string.Empty,
                        treeView1.Font, layout.ValueTextBounds, valueColor, flags);
                }
            }

            if (selected && treeView1.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, layout.RowBounds,
                    UiPalette.SelectionText, rowBackColor);
            }
        }

        private void treeView1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }
            TreeNode node = treeView1.GetNodeAt(e.Location);
            if (node == null)
            {
                contextMenuRoot.Show(treeView1, e.Location);
            }
        }

        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }
            treeView1.SelectedNode = e.Node;
            DataStructNodeTag tag = e.Node.Tag as DataStructNodeTag;
            if (tag == null)
            {
                return;
            }
            switch (tag.NodeType)
            {
                case DataStructNodeType.Root:
                    contextMenuRoot.Show(treeView1, e.Location);
                    break;
                case DataStructNodeType.Struct:
                    contextMenuStruct.Show(treeView1, e.Location);
                    break;
                case DataStructNodeType.Item:
                    contextMenuItem.Show(treeView1, e.Location);
                    break;
                case DataStructNodeType.Field:
                    contextMenuField.Show(treeView1, e.Location);
                    break;
            }
        }

        private void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node == null)
            {
                return;
            }
            treeView1.SelectedNode = e.Node;
            DataStructNodeTag tag = e.Node.Tag as DataStructNodeTag;
            if (tag == null)
            {
                return;
            }
            TreeNode nodeToExpand = null;
            if (tag.NodeType == DataStructNodeType.Struct
                || tag.NodeType == DataStructNodeType.Item)
            {
                nodeToExpand = e.Node;
            }
            else if (tag.NodeType == DataStructNodeType.Field)
            {
                if (!TryGetFieldNodeLayout(e.Node, e.Node.Bounds, out FieldNodeLayout layout))
                {
                    return;
                }
                if (layout.ValueBounds.Contains(e.Location))
                {
                    BeginInlineValueEdit(e.Node, tag, layout);
                }
                else if (layout.NameBounds.Contains(e.Location))
                {
                    OpenFieldEditor(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, e.Node);
                }
                return;
            }
            if (nodeToExpand != null)
            {
                // 等系统默认双击动作结束后再展开，保证双击不会把已展开节点折叠。
                TreeNode capturedNode = nodeToExpand;
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && !Disposing && capturedNode.TreeView == treeView1)
                    {
                        capturedNode.Expand();
                    }
                }));
            }
        }

        private void BeginInlineValueEdit(TreeNode fieldNode, DataStructNodeTag tag,
            FieldNodeLayout layout)
        {
            if (fieldNode == null || tag == null || layout.ValueBounds.Width <= 20)
            {
                return;
            }
            if (!EndInlineValueEdit(true))
            {
                return;
            }
            if (!UpdateFieldNodeFromStore(fieldNode,
                    tag.StructIndex, tag.ItemIndex, tag.FieldIndex))
            {
                MessageBox.Show("字段不存在");
                return;
            }
            tag = fieldNode.Tag as DataStructNodeTag;
            if (tag == null
                || !TryGetFieldNodeLayout(fieldNode, fieldNode.Bounds, out layout))
            {
                return;
            }

            inlineValueNode = fieldNode;
            inlineValueEditor.Bounds = new Rectangle(
                layout.ValueBounds.X,
                layout.ValueBounds.Y,
                Math.Max(40, layout.ValueBounds.Width),
                Math.Max(treeView1.Font.Height + 4, layout.ValueBounds.Height));
            inlineValueEditor.ForeColor = tag.FieldType == DataStructValueType.Number
                ? UiPalette.Success
                : UiPalette.Brand;
            inlineValueEditor.BackColor = tag.FieldType == DataStructValueType.Number
                ? UiPalette.SuccessSoft
                : UiPalette.BrandSoft;
            inlineValueEditor.Text = tag.EditValue ?? string.Empty;
            inlineValueEditor.Visible = true;
            inlineValueEditor.BringToFront();
            inlineValueEditor.Focus();
            inlineValueEditor.SelectAll();
        }

        private bool EndInlineValueEdit(bool commit)
        {
            if (inlineValueEditor == null || !inlineValueEditor.Visible || inlineValueEditEnding)
            {
                return true;
            }

            inlineValueEditEnding = true;
            try
            {
                TreeNode fieldNode = inlineValueNode;
                DataStructNodeTag tag = fieldNode?.Tag as DataStructNodeTag;
                if (!commit || tag == null || tag.NodeType != DataStructNodeType.Field)
                {
                    inlineValueEditor.Visible = false;
                    inlineValueNode = null;
                    return true;
                }

                string value = inlineValueEditor.Text ?? string.Empty;
                if (!Workspace.Runtime.Stores.DataStructures.SetFieldValue(
                        tag.StructIndex, tag.ItemIndex, tag.FieldIndex,
                        tag.FieldType, value, out string error))
                {
                    MessageBox.Show(error, "字段值无效", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed && !Disposing && inlineValueEditor.Visible)
                        {
                            inlineValueEditor.Focus();
                            inlineValueEditor.SelectAll();
                        }
                    }));
                    return false;
                }

                inlineValueEditor.Visible = false;
                inlineValueNode = null;
                UpdateFieldNodeFromStore(fieldNode,
                    tag.StructIndex, tag.ItemIndex, tag.FieldIndex);
                if (!Workspace.Runtime.Stores.DataStructures.Save(
                        Workspace.Runtime.Paths.ConfigPath))
                {
                    MessageBox.Show("字段值已经写入内存，但保存到磁盘失败；平台已进入安全锁定状态。",
                        "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
                return true;
            }
            finally
            {
                inlineValueEditEnding = false;
            }
        }

        private void inlineValueEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                EndInlineValueEdit(true);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                EndInlineValueEdit(false);
                treeView1.Focus();
            }
        }

        private void inlineValueEditor_LostFocus(object sender, EventArgs e)
        {
            EndInlineValueEdit(true);
        }

        private void treeView1_MouseWheel(object sender, MouseEventArgs e)
        {
            EndInlineValueEdit(true);
        }

        private void treeView1_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            DataStructNodeTag tag = e.Node.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Item)
            {
                return;
            }
            if (IsItemNodeLoaded(e.Node))
            {
                return;
            }
            LoadFieldNodes(e.Node, tag.StructIndex, tag.ItemIndex);
        }

        private void menuAddStruct_Click(object sender, EventArgs e)
        {
            using (FrmTextInput dialog = new FrmTextInput("新建结构体", "结构体名称"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                string name = (dialog.InputText ?? string.Empty).Trim();
                if (!Workspace.Runtime.Stores.DataStructures.AddStruct(name, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
                ReloadTree();
            }
        }

        private void menuExpandAll_Click(object sender, EventArgs e)
        {
            treeView1.BeginUpdate();
            try
            {
                treeView1.ExpandAll();
            }
            finally
            {
                treeView1.EndUpdate();
            }
        }

        private void menuCollapseAll_Click(object sender, EventArgs e)
        {
            treeView1.BeginUpdate();
            try
            {
                treeView1.CollapseAll();
            }
            finally
            {
                treeView1.EndUpdate();
            }
        }

        private void menuCopy_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            DataStructNodeTag tag = node?.Tag as DataStructNodeTag;
            if (tag == null)
            {
                return;
            }
            if (tag.NodeType == DataStructNodeType.Struct)
            {
                if (!Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
                {
                    MessageBox.Show("结构体不存在");
                    return;
                }
                clipboard = new DataStructClipboard
                {
                    NodeType = DataStructNodeType.Struct,
                    StructData = (DataStruct)dataStruct.Clone()
                };
                return;
            }
            if (tag.NodeType == DataStructNodeType.Item)
            {
                if (!Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
                {
                    MessageBox.Show("结构体不存在");
                    return;
                }
                if (dataStruct.dataStructItems == null || tag.ItemIndex < 0 || tag.ItemIndex >= dataStruct.dataStructItems.Count)
                {
                    MessageBox.Show("数据项不存在");
                    return;
                }
                DataStructItem item = dataStruct.dataStructItems[tag.ItemIndex];
                if (item == null)
                {
                    MessageBox.Show("数据项为空");
                    return;
                }
                clipboard = new DataStructClipboard
                {
                    NodeType = DataStructNodeType.Item,
                    ItemData = item.Clone()
                };
                return;
            }
            if (tag.NodeType == DataStructNodeType.Field)
            {
                if (!TryGetFieldSnapshot(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, out string fieldName, out DataStructValueType fieldType, out object fieldValue))
                {
                    MessageBox.Show("字段不存在");
                    return;
                }
                clipboard = new DataStructClipboard
                {
                    NodeType = DataStructNodeType.Field,
                    FieldName = fieldName,
                    FieldType = fieldType,
                    FieldValue = fieldValue
                };
            }
        }

        private void menuPaste_Click(object sender, EventArgs e)
        {
            if (clipboard == null)
            {
                MessageBox.Show("剪贴板为空");
                return;
            }
            TreeNode node = treeView1.SelectedNode;
            DataStructNodeTag tag = node?.Tag as DataStructNodeTag;

            if (clipboard.NodeType == DataStructNodeType.Struct)
            {
                if (clipboard.StructData == null)
                {
                    MessageBox.Show("剪贴板结构体为空");
                    return;
                }
                string newName = BuildUniqueStructName(clipboard.StructData.Name);
                if (!Workspace.Runtime.Stores.DataStructures.AddStruct(newName, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                if (!Workspace.Runtime.Stores.DataStructures.TryGetStructIndexByName(newName, out int newStructIndex))
                {
                    MessageBox.Show("结构体创建失败");
                    return;
                }
                bool success = true;
                if (clipboard.StructData.dataStructItems != null)
                {
                    for (int i = 0; i < clipboard.StructData.dataStructItems.Count; i++)
                    {
                        DataStructItem sourceItem = clipboard.StructData.dataStructItems[i];
                        if (sourceItem == null)
                        {
                            continue;
                        }
                        if (!Workspace.Runtime.Stores.DataStructures.TryInsertItem(newStructIndex, i, sourceItem.Clone()))
                        {
                            success = false;
                            break;
                        }
                    }
                }
                if (!success)
                {
                    Workspace.Runtime.Stores.DataStructures.RemoveStructAt(newStructIndex, out _);
                    MessageBox.Show("粘贴结构体失败");
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
                ReloadTree();
                return;
            }

            if (clipboard.NodeType == DataStructNodeType.Item)
            {
                if (clipboard.ItemData == null)
                {
                    MessageBox.Show("剪贴板数据项为空");
                    return;
                }
                if (tag == null || (tag.NodeType != DataStructNodeType.Struct && tag.NodeType != DataStructNodeType.Item))
                {
                    MessageBox.Show("请选择结构体或数据项进行粘贴");
                    return;
                }
                int structIndex = tag.StructIndex;
                int insertIndex = tag.NodeType == DataStructNodeType.Item
                    ? tag.ItemIndex + 1
                    : Workspace.Runtime.Stores.DataStructures.GetItemCount(structIndex);
                string itemName = BuildUniqueItemName(structIndex, clipboard.ItemData.Name);
                if (!Workspace.Runtime.Stores.DataStructures.CreateItem(structIndex, itemName, insertIndex, out int itemIndex, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }

                if (!CopyFieldsToItem(structIndex, itemIndex, clipboard.ItemData, out string copyError))
                {
                    Workspace.Runtime.Stores.DataStructures.DeleteItem(structIndex, itemIndex, out _);
                    MessageBox.Show(copyError);
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);

                TreeNode structNode = GetStructNode(structIndex);
                if (structNode == null || !Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(structIndex, out DataStruct dataStruct))
                {
                    ReloadTree();
                    return;
                }
                DataStructItem newItem = dataStruct.dataStructItems[itemIndex];
                TreeNode itemNode = BuildItemNode(structIndex, itemIndex, newItem);
                if (insertIndex < 0 || insertIndex > structNode.Nodes.Count)
                {
                    structNode.Nodes.Add(itemNode);
                }
                else
                {
                    structNode.Nodes.Insert(itemIndex, itemNode);
                }
                UpdateItemNodeTexts(structNode, dataStruct, itemIndex + 1);
                structNode.Expand();
                return;
            }

            if (clipboard.NodeType == DataStructNodeType.Field)
            {
                if (tag == null || (tag.NodeType != DataStructNodeType.Item && tag.NodeType != DataStructNodeType.Field))
                {
                    MessageBox.Show("请选择数据项或字段进行粘贴");
                    return;
                }
                int structIndex = tag.StructIndex;
                int itemIndex = tag.NodeType == DataStructNodeType.Field ? tag.ItemIndex : tag.ItemIndex;
                string fieldName = string.IsNullOrWhiteSpace(clipboard.FieldName) ? "字段" : clipboard.FieldName;
                string valueText = clipboard.FieldValue == null ? string.Empty : clipboard.FieldValue.ToString();
                if (clipboard.FieldType == DataStructValueType.Number && clipboard.FieldValue is double numberValue)
                {
                    valueText = numberValue.ToString("G17", CultureInfo.InvariantCulture);
                }
                if (clipboard.FieldType == DataStructValueType.Number && string.IsNullOrWhiteSpace(valueText))
                {
                    valueText = "0";
                }
                if (!Workspace.Runtime.Stores.DataStructures.AddField(structIndex, itemIndex, fieldName, clipboard.FieldType, valueText, -1, out int fieldIndex, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);

                TreeNode itemNode = tag.NodeType == DataStructNodeType.Field ? node.Parent : node;
                if (itemNode == null)
                {
                    ReloadTree();
                    return;
                }
                AddFieldNodeToTree(itemNode, structIndex, itemIndex, fieldIndex);
            }
        }

        private void menuRefresh_Click(object sender, EventArgs e)
        {
            ReloadTree();
        }

        private void menuStructRename_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            DataStructNodeTag tag = node?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Struct)
            {
                return;
            }
            if (!Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
            {
                MessageBox.Show("结构体不存在");
                return;
            }
            using (FrmTextInput dialog = new FrmTextInput("重命名结构体", "结构体名称", dataStruct.Name))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                string name = (dialog.InputText ?? string.Empty).Trim();
                if (!Workspace.Runtime.Stores.DataStructures.RenameStruct(tag.StructIndex, name, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
                node.Text = BuildStructNodeText(tag.StructIndex, name);
            }
        }

        private void menuStructDelete_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            DataStructNodeTag tag = node?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Struct)
            {
                return;
            }
            if (MessageBox.Show("确认删除该结构体？", "删除确认", MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                return;
            }
            if (!Workspace.Runtime.Stores.DataStructures.RemoveStructAt(tag.StructIndex, out string error))
            {
                MessageBox.Show(error);
                return;
            }
            Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
            ReloadTree();
        }

        private void menuStructAddItem_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            DataStructNodeTag tag = node?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Struct)
            {
                return;
            }
            using (FrmTextInput dialog = new FrmTextInput("新建数据项", "数据项名称"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                string name = (dialog.InputText ?? string.Empty).Trim();
                int insertIndex = Workspace.Runtime.Stores.DataStructures.GetItemCount(tag.StructIndex);
                if (!Workspace.Runtime.Stores.DataStructures.CreateItem(tag.StructIndex, name, insertIndex, out int itemIndex, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
                TreeNode structNode = GetStructNode(tag.StructIndex);
                if (structNode == null)
                {
                    ReloadTree();
                    return;
                }
                if (!Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
                {
                    ReloadTree();
                    return;
                }
                DataStructItem item = dataStruct.dataStructItems[itemIndex];
                TreeNode itemNode = BuildItemNode(tag.StructIndex, itemIndex, item);
                structNode.Nodes.Insert(itemIndex, itemNode);
                UpdateItemNodeTexts(structNode, dataStruct, itemIndex + 1);
                structNode.Expand();
            }
        }

        private void menuItemRename_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            DataStructNodeTag tag = node?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Item)
            {
                return;
            }
            if (!Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
            {
                MessageBox.Show("结构体不存在");
                return;
            }
            if (tag.ItemIndex < 0 || tag.ItemIndex >= dataStruct.dataStructItems.Count)
            {
                MessageBox.Show("数据项不存在");
                return;
            }
            DataStructItem item = dataStruct.dataStructItems[tag.ItemIndex];
            using (FrmTextInput dialog = new FrmTextInput("重命名数据项", "数据项名称", item.Name))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                string name = (dialog.InputText ?? string.Empty).Trim();
                if (!Workspace.Runtime.Stores.DataStructures.RenameItem(tag.StructIndex, tag.ItemIndex, name, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
                node.Text = BuildItemNodeText(tag.ItemIndex, name);
            }
        }

        private void menuItemDelete_Click(object sender, EventArgs e)
        {
            TreeNode node = treeView1.SelectedNode;
            DataStructNodeTag tag = node?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Item)
            {
                return;
            }
            if (MessageBox.Show("确认删除该数据项？", "删除确认", MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                return;
            }
            if (!Workspace.Runtime.Stores.DataStructures.DeleteItem(tag.StructIndex, tag.ItemIndex, out string error))
            {
                MessageBox.Show(error);
                return;
            }
            Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
            TreeNode structNode = GetStructNode(tag.StructIndex);
            if (structNode == null)
            {
                ReloadTree();
                return;
            }
            if (!Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
            {
                ReloadTree();
                return;
            }
            if (tag.ItemIndex >= 0 && tag.ItemIndex < structNode.Nodes.Count)
            {
                structNode.Nodes.RemoveAt(tag.ItemIndex);
            }
            UpdateItemNodeTexts(structNode, dataStruct, tag.ItemIndex);
        }

        private void menuItemAddField_Click(object sender, EventArgs e)
        {
            TreeNode itemNode = treeView1.SelectedNode;
            DataStructNodeTag tag = itemNode?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Item)
            {
                return;
            }
            using (FrmDataStructFieldEdit dialog = new FrmDataStructFieldEdit(
                Workspace.Runtime.Stores.DataStructures, tag.StructIndex, tag.ItemIndex))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (dialog.ActualFieldIndex < 0)
                {
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
                AddFieldNodeToTree(itemNode, tag.StructIndex, tag.ItemIndex, dialog.ActualFieldIndex);
            }
        }

        private void menuFieldEdit_Click(object sender, EventArgs e)
        {
            TreeNode fieldNode = treeView1.SelectedNode;
            DataStructNodeTag tag = fieldNode?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Field)
            {
                return;
            }
            OpenFieldEditor(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, fieldNode);
        }

        private void menuFieldRename_Click(object sender, EventArgs e)
        {
            TreeNode fieldNode = treeView1.SelectedNode;
            DataStructNodeTag tag = fieldNode?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Field)
            {
                return;
            }
            if (!TryGetFieldSnapshot(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, out string fieldName, out DataStructValueType fieldType, out object fieldValue))
            {
                MessageBox.Show("字段不存在");
                return;
            }
            using (FrmTextInput dialog = new FrmTextInput("重命名字段", "字段名称", fieldName))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                string newName = (dialog.InputText ?? string.Empty).Trim();
                if (!Workspace.Runtime.Stores.DataStructures.RenameField(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, newName, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
                UpdateFieldNode(fieldNode, tag.StructIndex, tag.ItemIndex, tag.FieldIndex, newName, fieldType, fieldValue);
            }
        }

        private void menuFieldTypeText_Click(object sender, EventArgs e)
        {
            ChangeFieldType(DataStructValueType.Text);
        }

        private void menuFieldTypeNumber_Click(object sender, EventArgs e)
        {
            ChangeFieldType(DataStructValueType.Number);
        }

        private void ChangeFieldType(DataStructValueType newType)
        {
            TreeNode fieldNode = treeView1.SelectedNode;
            DataStructNodeTag tag = fieldNode?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Field)
            {
                return;
            }
            if (!Workspace.Runtime.Stores.DataStructures.SetFieldType(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, newType, out string message))
            {
                MessageBox.Show(message);
                return;
            }
            if (!string.IsNullOrEmpty(message))
            {
                MessageBox.Show(message);
            }
            Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
            UpdateFieldNodeFromStore(fieldNode, tag.StructIndex, tag.ItemIndex, tag.FieldIndex);
        }

        private void menuFieldDelete_Click(object sender, EventArgs e)
        {
            TreeNode fieldNode = treeView1.SelectedNode;
            DataStructNodeTag tag = fieldNode?.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Field)
            {
                return;
            }
            if (MessageBox.Show("确认删除该字段？", "删除确认", MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                return;
            }
            if (!Workspace.Runtime.Stores.DataStructures.RemoveField(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, out string error))
            {
                MessageBox.Show(error);
                return;
            }
            Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
            TreeNode itemNode = fieldNode.Parent;
            if (itemNode != null)
            {
                itemNode.Nodes.Remove(fieldNode);
                RefreshItemFieldNameColumnWidth(itemNode);
                if (itemNode.Nodes.Count == 0)
                {
                    itemNode.Collapse();
                }
            }
        }

        private void OpenFieldEditor(int structIndex, int itemIndex, int fieldIndex, TreeNode fieldNode)
        {
            if (!TryGetFieldSnapshot(structIndex, itemIndex, fieldIndex, out string fieldName, out DataStructValueType fieldType, out object fieldValue))
            {
                MessageBox.Show("字段不存在");
                return;
            }
            string valueText;
            if (fieldType == DataStructValueType.Number)
            {
                if (fieldValue is double numberValue)
                {
                    valueText = numberValue.ToString("G17", CultureInfo.InvariantCulture);
                }
                else
                {
                    valueText = string.Empty;
                }
            }
            else
            {
                valueText = fieldValue?.ToString() ?? string.Empty;
            }
            using (FrmDataStructFieldEdit dialog = new FrmDataStructFieldEdit(
                Workspace.Runtime.Stores.DataStructures, structIndex, itemIndex,
                fieldIndex, fieldName, fieldType, valueText))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                Workspace.Runtime.Stores.DataStructures.Save(Workspace.Runtime.Paths.ConfigPath);
                UpdateFieldNodeFromStore(fieldNode, structIndex, itemIndex, fieldIndex);
            }
        }

        private void LoadFieldNodes(TreeNode itemNode, int structIndex, int itemIndex)
        {
            itemNode.Nodes.Clear();
            if (!Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(structIndex, out DataStruct dataStruct))
            {
                return;
            }
            if (dataStruct.dataStructItems == null || itemIndex < 0 || itemIndex >= dataStruct.dataStructItems.Count)
            {
                return;
            }
            DataStructItem item = dataStruct.dataStructItems[itemIndex];
            if (item == null)
            {
                return;
            }
            List<int> fieldIndexes = GetFieldIndexes(item);
            fieldIndexes.Sort();
            foreach (int fieldIndex in fieldIndexes)
            {
                if (!TryGetFieldSnapshotFromItem(item, fieldIndex,
                        out string fieldName, out DataStructValueType fieldType, out object fieldValue))
                {
                    continue;
                }
                TreeNode fieldNode = BuildFieldNode(structIndex, itemIndex, fieldIndex, fieldName, fieldType, fieldValue);
                itemNode.Nodes.Add(fieldNode);
            }
        }

        private void AddFieldNodeToTree(TreeNode itemNode, int structIndex, int itemIndex, int fieldIndex)
        {
            if (!IsItemNodeLoaded(itemNode))
            {
                if (itemNode.Nodes.Count == 0)
                {
                    TreeNode placeholder = new TreeNode("加载中...")
                    {
                        Tag = new DataStructNodeTag { NodeType = DataStructNodeType.Placeholder }
                    };
                    itemNode.Nodes.Add(placeholder);
                }
                return;
            }
            if (!TryGetFieldSnapshot(structIndex, itemIndex, fieldIndex, out string fieldName, out DataStructValueType fieldType, out object fieldValue))
            {
                return;
            }
            TreeNode fieldNode = BuildFieldNode(structIndex, itemIndex, fieldIndex, fieldName, fieldType, fieldValue);
            int insertIndex = itemNode.Nodes.Count;
            for (int i = 0; i < itemNode.Nodes.Count; i++)
            {
                DataStructNodeTag tag = itemNode.Nodes[i].Tag as DataStructNodeTag;
                if (tag != null && tag.NodeType == DataStructNodeType.Field && tag.FieldIndex > fieldIndex)
                {
                    insertIndex = i;
                    break;
                }
            }
            itemNode.Nodes.Insert(insertIndex, fieldNode);
            RefreshItemFieldNameColumnWidth(itemNode);
            itemNode.Expand();
        }

        private bool UpdateFieldNodeFromStore(TreeNode fieldNode, int structIndex, int itemIndex, int fieldIndex)
        {
            if (!TryGetFieldSnapshot(structIndex, itemIndex, fieldIndex, out string fieldName, out DataStructValueType fieldType, out object fieldValue))
            {
                return false;
            }
            UpdateFieldNode(fieldNode, structIndex, itemIndex, fieldIndex, fieldName, fieldType, fieldValue);
            return true;
        }

        private void UpdateFieldNode(TreeNode fieldNode, int structIndex, int itemIndex, int fieldIndex, string fieldName, DataStructValueType fieldType, object fieldValue)
        {
            string displayValue = FormatFieldValue(fieldType, fieldValue);
            string editValue = FormatFieldEditValue(fieldType, fieldValue);
            string visibleValue = string.IsNullOrEmpty(displayValue) ? "（空）" : displayValue;
            fieldNode.Text = BuildFieldNodeText(fieldIndex, fieldName, visibleValue);
            fieldNode.Tag = new DataStructNodeTag
            {
                NodeType = DataStructNodeType.Field,
                StructIndex = structIndex,
                ItemIndex = itemIndex,
                FieldIndex = fieldIndex,
                FieldName = fieldName ?? string.Empty,
                DisplayValue = visibleValue,
                EditValue = editValue,
                FieldType = fieldType
            };
            fieldNode.ForeColor = UiPalette.TextPrimary;
            bool columnWidthChanged = false;
            if (fieldNode.Parent != null)
            {
                columnWidthChanged = RefreshItemFieldNameColumnWidth(fieldNode.Parent);
            }
            if (!columnWidthChanged)
            {
                InvalidateFieldRow(fieldNode);
            }
        }

        private TreeNode BuildFieldNode(int structIndex, int itemIndex, int fieldIndex, string fieldName, DataStructValueType fieldType, object fieldValue)
        {
            TreeNode node = new TreeNode();
            UpdateFieldNode(node, structIndex, itemIndex, fieldIndex, fieldName, fieldType, fieldValue);
            return node;
        }

        private static string FormatFieldValue(DataStructValueType type, object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (type == DataStructValueType.Number)
            {
                if (value is double number)
                {
                    return number.ToString("0.######", CultureInfo.CurrentCulture);
                }
                return value.ToString();
            }
            string str = value.ToString();
            if (str.Length > TextPreviewLength)
            {
                return str.Substring(0, TextPreviewLength) + "...";
            }
            return str;
        }

        private static string FormatFieldEditValue(DataStructValueType type, object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (type == DataStructValueType.Number && value is double number)
            {
                return number.ToString("G17", CultureInfo.InvariantCulture);
            }
            return value.ToString();
        }

        private static List<int> GetFieldIndexes(DataStructItem item)
        {
            HashSet<int> indexes = new HashSet<int>();
            if (item?.FieldNames != null)
            {
                indexes.UnionWith(item.FieldNames.Keys);
            }
            if (item?.FieldTypes != null)
            {
                indexes.UnionWith(item.FieldTypes.Keys);
            }
            if (item?.str != null)
            {
                indexes.UnionWith(item.str.Keys);
            }
            if (item?.num != null)
            {
                indexes.UnionWith(item.num.Keys);
            }
            return indexes.ToList();
        }

        private static bool IsItemNodeLoaded(TreeNode itemNode)
        {
            if (itemNode == null)
            {
                return false;
            }
            if (itemNode.Nodes.Count == 0)
            {
                return true;
            }
            if (itemNode.Nodes.Count == 1)
            {
                DataStructNodeTag tag = itemNode.Nodes[0].Tag as DataStructNodeTag;
                return tag == null || tag.NodeType != DataStructNodeType.Placeholder;
            }
            return true;
        }

        private TreeNode GetStructNode(int structIndex)
        {
            if (structIndex < 0 || structIndex >= treeView1.Nodes.Count)
            {
                return null;
            }
            return treeView1.Nodes[structIndex];
        }

        private void UpdateItemNodeTexts(TreeNode structNode, DataStruct dataStruct, int StartIndex)
        {
            if (structNode == null || dataStruct?.dataStructItems == null)
            {
                return;
            }
            DataStructNodeTag structTag = structNode.Tag as DataStructNodeTag;
            int structIndex = structTag?.StructIndex ?? -1;
            for (int i = StartIndex; i < dataStruct.dataStructItems.Count; i++)
            {
                if (i >= structNode.Nodes.Count)
                {
                    break;
                }
                DataStructItem item = dataStruct.dataStructItems[i];
                TreeNode itemNode = structNode.Nodes[i];
                DataStructNodeTag tag = itemNode.Tag as DataStructNodeTag;
                if (tag != null)
                {
                    tag.ItemIndex = i;
                }
                itemNode.Text = BuildItemNodeText(i, item?.Name ?? string.Empty);
                if (structIndex >= 0)
                {
                    foreach (TreeNode child in itemNode.Nodes)
                    {
                        DataStructNodeTag childTag = child.Tag as DataStructNodeTag;
                        if (childTag != null && childTag.NodeType == DataStructNodeType.Field)
                        {
                            childTag.StructIndex = structIndex;
                            childTag.ItemIndex = i;
                        }
                    }
                }
            }
        }

        private string BuildUniqueStructName(string sourceName)
        {
            string baseName = string.IsNullOrWhiteSpace(sourceName) ? "结构体" : sourceName;
            string name = $"{baseName}_复制";
            HashSet<string> exist = new HashSet<string>(Workspace.Runtime.Stores.DataStructures.GetStructNames());
            if (!exist.Contains(name))
            {
                return name;
            }
            int index = 2;
            while (true)
            {
                string candidate = $"{name}({index})";
                if (!exist.Contains(candidate))
                {
                    return candidate;
                }
                index++;
            }
        }

        private string BuildUniqueItemName(int structIndex, string sourceName)
        {
            string baseName = string.IsNullOrWhiteSpace(sourceName) ? "数据项" : sourceName;
            string name = $"{baseName}_复制";
            if (!Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(structIndex, out DataStruct dataStruct) || dataStruct.dataStructItems == null)
            {
                return name;
            }
            HashSet<string> exist = new HashSet<string>(dataStruct.dataStructItems.Where(item => item != null).Select(item => item.Name));
            if (!exist.Contains(name))
            {
                return name;
            }
            int index = 2;
            while (true)
            {
                string candidate = $"{name}({index})";
                if (!exist.Contains(candidate))
                {
                    return candidate;
                }
                index++;
            }
        }

        private bool CopyFieldsToItem(int structIndex, int itemIndex, DataStructItem sourceItem, out string error)
        {
            error = string.Empty;
            if (sourceItem == null)
            {
                error = "数据项为空";
                return false;
            }
            List<int> fieldIndexes = GetFieldIndexes(sourceItem);
            fieldIndexes.Sort();
            foreach (int fieldIndex in fieldIndexes)
            {
                string fieldName = sourceItem.FieldNames != null && sourceItem.FieldNames.TryGetValue(fieldIndex, out string nameValue)
                    ? nameValue
                    : $"字段{fieldIndex}";
                DataStructValueType type = DataStructValueType.Text;
                if (sourceItem.FieldTypes != null && sourceItem.FieldTypes.TryGetValue(fieldIndex, out DataStructValueType typeValue))
                {
                    type = typeValue;
                }
                else if (sourceItem.num != null && sourceItem.num.ContainsKey(fieldIndex))
                {
                    type = DataStructValueType.Number;
                }
                string valueText = string.Empty;
                if (type == DataStructValueType.Number)
                {
                    if (sourceItem.num != null && sourceItem.num.TryGetValue(fieldIndex, out double number))
                    {
                        valueText = number.ToString("G17", CultureInfo.InvariantCulture);
                    }
                    if (string.IsNullOrWhiteSpace(valueText))
                    {
                        valueText = "0";
                    }
                }
                else
                {
                    if (sourceItem.str != null && sourceItem.str.TryGetValue(fieldIndex, out string strValue))
                    {
                        valueText = strValue ?? string.Empty;
                    }
                }
                if (!Workspace.Runtime.Stores.DataStructures.AddField(structIndex, itemIndex, fieldName, type, valueText, fieldIndex, out _, out string fieldError))
                {
                    error = fieldError;
                    return false;
                }
            }
            return true;
        }

        private bool TryGetFieldSnapshot(int structIndex, int itemIndex, int fieldIndex, out string fieldName, out DataStructValueType fieldType, out object fieldValue)
        {
            fieldName = string.Empty;
            fieldType = DataStructValueType.Text;
            fieldValue = null;
            if (!Workspace.Runtime.Stores.DataStructures.TryGetStructSnapshotByIndex(structIndex, out DataStruct dataStruct))
            {
                return false;
            }
            if (dataStruct.dataStructItems == null || itemIndex < 0 || itemIndex >= dataStruct.dataStructItems.Count)
            {
                return false;
            }
            DataStructItem item = dataStruct.dataStructItems[itemIndex];
            if (item == null)
            {
                return false;
            }
            return TryGetFieldSnapshotFromItem(item, fieldIndex,
                out fieldName, out fieldType, out fieldValue);
        }

        private static bool TryGetFieldSnapshotFromItem(DataStructItem item, int fieldIndex,
            out string fieldName, out DataStructValueType fieldType, out object fieldValue)
        {
            fieldName = string.Empty;
            fieldType = DataStructValueType.Text;
            fieldValue = null;
            if (item?.FieldNames == null
                || !item.FieldNames.TryGetValue(fieldIndex, out fieldName))
            {
                return false;
            }
            if (item.FieldTypes == null
                || !item.FieldTypes.TryGetValue(fieldIndex, out fieldType))
            {
                return false;
            }
            if (fieldType == DataStructValueType.Number)
            {
                if (item.num != null && item.num.TryGetValue(fieldIndex, out double number))
                {
                    fieldValue = number;
                }
            }
            else
            {
                if (item.str != null && item.str.TryGetValue(fieldIndex, out string str))
                {
                    fieldValue = str;
                }
            }
            return true;
        }
    }
}
