using Automation.ParamFrm;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Forms;

namespace Automation
{
    [Serializable]
    public class DataStruct : ICloneable
    {
        [Browsable(false)]
        public string Name { get; set; }

        public List<DataStructItem> dataStructItems = new List<DataStructItem>();

        public object Clone()
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(memoryStream, this);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return formatter.Deserialize(memoryStream);
            }
        }
    }

    [Serializable]
    public class DataStructItem
    {
        public string Name { get; set; }

        public Dictionary<int, string> FieldNames { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, DataStructValueType> FieldTypes { get; set; } = new Dictionary<int, DataStructValueType>();

        public Dictionary<int, string> str { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, double> num { get; set; } = new Dictionary<int, double>();

        public DataStructItem Clone()
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(memoryStream, this);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return (DataStructItem)formatter.Deserialize(memoryStream);
            }
        }

        public int GetMaxIndex()
        {
            int maxIndex = -1;
            if (FieldNames != null && FieldNames.Count > 0)
            {
                maxIndex = Math.Max(maxIndex, FieldNames.Keys.Max());
            }
            if (FieldTypes != null && FieldTypes.Count > 0)
            {
                maxIndex = Math.Max(maxIndex, FieldTypes.Keys.Max());
            }
            if (str != null && str.Count > 0)
            {
                maxIndex = Math.Max(maxIndex, str.Keys.Max());
            }

            if (num != null && num.Count > 0)
            {
                maxIndex = Math.Max(maxIndex, num.Keys.Max());
            }

            return maxIndex;
        }
    }

    public partial class FrmDataStruct : Form
    {
        private const int TextPreviewLength = 30;

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

        public FrmDataStruct()
        {
            InitializeComponent();
            treeView1.HideSelection = false;
            treeView1.ShowNodeToolTips = true;
            AddCommonMenuItems(contextMenuRoot);
            AddCommonMenuItems(contextMenuStruct);
            AddCommonMenuItems(contextMenuItem);
            AddCommonMenuItems(contextMenuField);
        }

        private void FrmDataStruct_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
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
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();

            TreeNode rootNode = new TreeNode("结构体")
            {
                Tag = new DataStructNodeTag { NodeType = DataStructNodeType.Root }
            };
            treeView1.Nodes.Add(rootNode);

            List<DataStruct> snapshot = SF.dataStructStore.GetSnapshot();
            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    DataStruct dataStruct = snapshot[i];
                    TreeNode structNode = BuildStructNode(i, dataStruct);
                    rootNode.Nodes.Add(structNode);
                }
            }

            rootNode.Expand();
            treeView1.EndUpdate();
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
                    ItemIndex = itemIndex
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
            DataStructNodeTag tag = e.Node.Tag as DataStructNodeTag;
            if (tag == null || tag.NodeType != DataStructNodeType.Field)
            {
                return;
            }
            OpenFieldEditor(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, e.Node);
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
                if (!SF.dataStructStore.AddStruct(name, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);
                ReloadTree();
            }
        }

        private void menuExpandAll_Click(object sender, EventArgs e)
        {
            treeView1.ExpandAll();
        }

        private void menuCollapseAll_Click(object sender, EventArgs e)
        {
            treeView1.CollapseAll();
            if (treeView1.Nodes.Count > 0)
            {
                treeView1.Nodes[0].Expand();
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
                if (!SF.dataStructStore.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
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
                if (!SF.dataStructStore.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
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
                if (!SF.dataStructStore.AddStruct(newName, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                if (!SF.dataStructStore.TryGetStructIndexByName(newName, out int newStructIndex))
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
                        if (!SF.dataStructStore.TryInsertItem(newStructIndex, i, sourceItem.Clone()))
                        {
                            success = false;
                            break;
                        }
                    }
                }
                if (!success)
                {
                    SF.dataStructStore.RemoveStructAt(newStructIndex, out _);
                    MessageBox.Show("粘贴结构体失败");
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);
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
                int insertIndex = tag.NodeType == DataStructNodeType.Item ? tag.ItemIndex + 1 : -1;
                string itemName = BuildUniqueItemName(structIndex, clipboard.ItemData.Name);
                if (!SF.dataStructStore.CreateItem(structIndex, itemName, insertIndex, out int itemIndex, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }

                if (!CopyFieldsToItem(structIndex, itemIndex, clipboard.ItemData, out string copyError))
                {
                    SF.dataStructStore.DeleteItem(structIndex, itemIndex, out _);
                    MessageBox.Show(copyError);
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);

                TreeNode structNode = GetStructNode(structIndex);
                if (structNode == null || !SF.dataStructStore.TryGetStructSnapshotByIndex(structIndex, out DataStruct dataStruct))
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
                    valueText = numberValue.ToString("0.######", CultureInfo.CurrentCulture);
                }
                if (clipboard.FieldType == DataStructValueType.Number && string.IsNullOrWhiteSpace(valueText))
                {
                    valueText = "0";
                }
                if (!SF.dataStructStore.AddField(structIndex, itemIndex, fieldName, clipboard.FieldType, valueText, -1, out int fieldIndex, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);

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
            if (!SF.dataStructStore.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
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
                if (!SF.dataStructStore.RenameStruct(tag.StructIndex, name, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);
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
            if (!SF.dataStructStore.RemoveStructAt(tag.StructIndex, out string error))
            {
                MessageBox.Show(error);
                return;
            }
            SF.dataStructStore.Save(SF.ConfigPath);
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
                if (!SF.dataStructStore.CreateItem(tag.StructIndex, name, -1, out int itemIndex, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);
                TreeNode structNode = GetStructNode(tag.StructIndex);
                if (structNode == null)
                {
                    ReloadTree();
                    return;
                }
                if (!SF.dataStructStore.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
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
            if (!SF.dataStructStore.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
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
                if (!SF.dataStructStore.RenameItem(tag.StructIndex, tag.ItemIndex, name, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);
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
            if (!SF.dataStructStore.DeleteItem(tag.StructIndex, tag.ItemIndex, out string error))
            {
                MessageBox.Show(error);
                return;
            }
            SF.dataStructStore.Save(SF.ConfigPath);
            TreeNode structNode = GetStructNode(tag.StructIndex);
            if (structNode == null)
            {
                ReloadTree();
                return;
            }
            if (!SF.dataStructStore.TryGetStructSnapshotByIndex(tag.StructIndex, out DataStruct dataStruct))
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
            using (FrmDataStructFieldEdit dialog = new FrmDataStructFieldEdit(tag.StructIndex, tag.ItemIndex))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (dialog.ActualFieldIndex < 0)
                {
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);
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
                if (!SF.dataStructStore.RenameField(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, newName, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);
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
            if (!SF.dataStructStore.SetFieldType(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, newType, out string message))
            {
                MessageBox.Show(message);
                return;
            }
            if (!string.IsNullOrEmpty(message))
            {
                MessageBox.Show(message);
            }
            SF.dataStructStore.Save(SF.ConfigPath);
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
            if (!SF.dataStructStore.RemoveField(tag.StructIndex, tag.ItemIndex, tag.FieldIndex, out string error))
            {
                MessageBox.Show(error);
                return;
            }
            SF.dataStructStore.Save(SF.ConfigPath);
            TreeNode itemNode = fieldNode.Parent;
            if (itemNode != null)
            {
                itemNode.Nodes.Remove(fieldNode);
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
                    valueText = numberValue.ToString("0.######", CultureInfo.CurrentCulture);
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
            using (FrmDataStructFieldEdit dialog = new FrmDataStructFieldEdit(structIndex, itemIndex, fieldIndex, fieldName, fieldType, valueText))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                SF.dataStructStore.Save(SF.ConfigPath);
                UpdateFieldNodeFromStore(fieldNode, structIndex, itemIndex, fieldIndex);
            }
        }

        private void LoadFieldNodes(TreeNode itemNode, int structIndex, int itemIndex)
        {
            itemNode.Nodes.Clear();
            if (!SF.dataStructStore.TryGetStructSnapshotByIndex(structIndex, out DataStruct dataStruct))
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
                if (!TryGetFieldSnapshot(structIndex, itemIndex, fieldIndex, out string fieldName, out DataStructValueType fieldType, out object fieldValue))
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
            string displayValue = FormatFieldValue(fieldType, fieldValue, out string toolTip);
            fieldNode.Text = BuildFieldNodeText(fieldIndex, fieldName, displayValue);
            fieldNode.ToolTipText = toolTip;
            fieldNode.Tag = new DataStructNodeTag
            {
                NodeType = DataStructNodeType.Field,
                StructIndex = structIndex,
                ItemIndex = itemIndex,
                FieldIndex = fieldIndex
            };
            fieldNode.ForeColor = fieldType == DataStructValueType.Text ? Color.RoyalBlue : Color.DarkGreen;
        }

        private TreeNode BuildFieldNode(int structIndex, int itemIndex, int fieldIndex, string fieldName, DataStructValueType fieldType, object fieldValue)
        {
            TreeNode node = new TreeNode();
            UpdateFieldNode(node, structIndex, itemIndex, fieldIndex, fieldName, fieldType, fieldValue);
            return node;
        }

        private static string FormatFieldValue(DataStructValueType type, object value, out string toolTip)
        {
            toolTip = string.Empty;
            if (value == null)
            {
                return string.Empty;
            }
            if (type == DataStructValueType.Number)
            {
                if (value is double number)
                {
                    string display = number.ToString("0.######", CultureInfo.CurrentCulture);
                    toolTip = number.ToString("G17", CultureInfo.CurrentCulture);
                    return display;
                }
                string text = value.ToString();
                toolTip = text;
                return text;
            }
            string str = value.ToString();
            if (str.Length > TextPreviewLength)
            {
                toolTip = str;
                return str.Substring(0, TextPreviewLength) + "...";
            }
            return str;
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
            if (treeView1.Nodes.Count == 0)
            {
                return null;
            }
            TreeNode root = treeView1.Nodes[0];
            if (structIndex < 0 || structIndex >= root.Nodes.Count)
            {
                return null;
            }
            return root.Nodes[structIndex];
        }

        private void UpdateItemNodeTexts(TreeNode structNode, DataStruct dataStruct, int startIndex)
        {
            if (structNode == null || dataStruct?.dataStructItems == null)
            {
                return;
            }
            DataStructNodeTag structTag = structNode.Tag as DataStructNodeTag;
            int structIndex = structTag?.StructIndex ?? -1;
            for (int i = startIndex; i < dataStruct.dataStructItems.Count; i++)
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
            HashSet<string> exist = new HashSet<string>(SF.dataStructStore.GetStructNames());
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
            if (!SF.dataStructStore.TryGetStructSnapshotByIndex(structIndex, out DataStruct dataStruct) || dataStruct.dataStructItems == null)
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
                        valueText = number.ToString("0.######", CultureInfo.CurrentCulture);
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
                if (!SF.dataStructStore.AddField(structIndex, itemIndex, fieldName, type, valueText, fieldIndex, out _, out string fieldError))
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
            if (!SF.dataStructStore.TryGetStructSnapshotByIndex(structIndex, out DataStruct dataStruct))
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
            if (!item.FieldNames.TryGetValue(fieldIndex, out fieldName))
            {
                return false;
            }
            if (!item.FieldTypes.TryGetValue(fieldIndex, out fieldType))
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
