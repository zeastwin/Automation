using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using static Automation.FrmPropertyGrid;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Runtime.InteropServices.ComTypes;
using static Automation.OperationTypePartial;

namespace Automation
{
    public partial class FrmPropertyGrid : Form
    {
        private ToolStripDropDown activeOperationTypePicker;

        public List<object> OperationTypeList = new List<object>();

        public FrmPropertyGrid()
        {
            InitializeComponent();
            ConfigureAppearance();
            propertyGrid1.PropertySort = PropertySort.Categorized;
            InlineListTypeDescriptionProvider.Register();
            
            OperationTypeList.AddRange(OperationDefinitionRegistry.CreateAll().Cast<object>());
     
         



            OperationType.DisplayMember = "OperaType";
            OperationType.DataSource = OperationTypeList;

            KeyPreview = true;

            Enabled = false;

        }

        private void ConfigureAppearance()
        {
            Color textColor = Color.FromArgb(49, 63, 73);
            Color mutedTextColor = Color.FromArgb(83, 99, 110);
            Color borderColor = Color.FromArgb(218, 226, 231);

            BackColor = Color.White;
            panel1.BackColor = Color.FromArgb(249, 251, 252);
            panel1.Paint += (sender, args) =>
            {
                using (Pen pen = new Pen(borderColor))
                {
                    args.Graphics.DrawLine(
                        pen,
                        0,
                        panel1.ClientSize.Height - 1,
                        panel1.ClientSize.Width,
                        panel1.ClientSize.Height - 1);
                }
            };

            label1.AutoSize = false;
            label1.BackColor = Color.Transparent;
            label1.ForeColor = mutedTextColor;
            label1.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            label1.TextAlign = ContentAlignment.MiddleLeft;

            OperationType.BackColor = Color.White;
            OperationType.ForeColor = textColor;
            OperationType.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            OperationType.DropDownStyle = ComboBoxStyle.DropDownList;
            OperationType.Visible = false;

            System.Windows.Forms.Button operationTypePickerButton = new System.Windows.Forms.Button
            {
                AutoEllipsis = true,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
                ForeColor = textColor,
                Padding = new Padding(11, 0, 30, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = false
            };
            operationTypePickerButton.FlatAppearance.BorderColor = borderColor;
            operationTypePickerButton.FlatAppearance.BorderSize = 1;
            operationTypePickerButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 248, 253);
            operationTypePickerButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 241, 252);
            operationTypePickerButton.Paint += (sender, args) =>
            {
                int centerX = operationTypePickerButton.ClientSize.Width - 18;
                int centerY = operationTypePickerButton.ClientSize.Height / 2;
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(74, 95, 108)))
                {
                    args.Graphics.FillRectangle(brush, centerX - 5, centerY - 5, 4, 4);
                    args.Graphics.FillRectangle(brush, centerX + 1, centerY - 5, 4, 4);
                    args.Graphics.FillRectangle(brush, centerX - 5, centerY + 1, 4, 4);
                    args.Graphics.FillRectangle(brush, centerX + 1, centerY + 1, 4, 4);
                }
            };
            operationTypePickerButton.Click += (sender, args) => ShowOperationTypePicker(operationTypePickerButton);
            panel1.Controls.Add(operationTypePickerButton);

            Label operationTypeReadOnly = new Label
            {
                AutoEllipsis = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
                ForeColor = textColor,
                Padding = new Padding(11, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            panel1.Controls.Add(operationTypeReadOnly);

            Action refreshOperationTypePresentation = () =>
            {
                operationTypeReadOnly.Text = OperationType.Text;
                operationTypePickerButton.Text = OperationType.Text;
                operationTypeReadOnly.Visible = !OperationType.Enabled;
                operationTypePickerButton.Visible = OperationType.Enabled;
                if (operationTypeReadOnly.Visible)
                {
                    operationTypeReadOnly.BringToFront();
                }
                else
                {
                    operationTypePickerButton.BringToFront();
                }
            };
            OperationType.EnabledChanged += (sender, args) => refreshOperationTypePresentation();
            OperationType.SelectedIndexChanged += (sender, args) => refreshOperationTypePresentation();
            EnabledChanged += (sender, args) => refreshOperationTypePresentation();

            Action layoutHeader = () =>
            {
                int comboHeight = OperationType.PreferredHeight;
                int headerHeight = Math.Max(44, comboHeight + 16);
                if (panel1.Height != headerHeight)
                {
                    panel1.Height = headerHeight;
                }
                label1.SetBounds(12, 0, 76, panel1.ClientSize.Height - 1);
                Rectangle selectorBounds = new Rectangle(
                    92,
                    Math.Max(0, (panel1.ClientSize.Height - comboHeight) / 2),
                    Math.Max(80, panel1.ClientSize.Width - 104),
                    comboHeight);
                OperationType.Bounds = selectorBounds;
                operationTypePickerButton.Bounds = selectorBounds;
                operationTypeReadOnly.Bounds = selectorBounds;
            };
            panel1.Resize += (sender, args) => layoutHeader();
            layoutHeader();
            refreshOperationTypePresentation();

            propertyGrid1.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            propertyGrid1.BackColor = Color.White;
            propertyGrid1.ViewBackColor = Color.White;
            propertyGrid1.ViewForeColor = textColor;
            propertyGrid1.ViewBorderColor = borderColor;
            propertyGrid1.CategoryForeColor = Color.FromArgb(45, 72, 88);
            propertyGrid1.CategorySplitterColor = Color.FromArgb(235, 240, 243);
            propertyGrid1.LineColor = Color.FromArgb(231, 236, 239);
            propertyGrid1.SelectedItemWithFocusBackColor = Color.FromArgb(220, 239, 248);
            propertyGrid1.SelectedItemWithFocusForeColor = Color.FromArgb(25, 82, 112);
            propertyGrid1.HelpBackColor = Color.FromArgb(249, 251, 252);
            propertyGrid1.HelpBorderColor = borderColor;
            propertyGrid1.HelpForeColor = mutedTextColor;
            propertyGrid1.CommandsBackColor = Color.FromArgb(249, 251, 252);
            propertyGrid1.CommandsBorderColor = borderColor;
            propertyGrid1.CommandsForeColor = textColor;
            propertyGrid1.ToolbarVisible = false;

            PropertyInfo doubleBufferedProperty = typeof(Control).GetProperty(
                "DoubleBuffered",
                BindingFlags.Instance | BindingFlags.NonPublic);
            doubleBufferedProperty?.SetValue(propertyGrid1, true, null);
        }

        private void ShowOperationTypePicker(Control anchorControl)
        {
            activeOperationTypePicker?.Close();
            List<OperationType> templates = OperationTypeList.OfType<OperationType>().ToList();
            if (templates.Count == 0)
            {
                return;
            }

            Rectangle anchorBounds = anchorControl.RectangleToScreen(anchorControl.ClientRectangle);
            Rectangle workingArea = Screen.FromRectangle(anchorBounds).WorkingArea;
            int pickerWidth = Math.Min(550, Math.Max(420, workingArea.Width - 24));
            var pickerPanel = new OperationTypePickerPanel(templates)
            {
                Size = new Size(pickerWidth, 300)
            };
            pickerPanel.PerformLayout();
            pickerPanel.RefreshPickerLayout();
            int pickerHeight = Math.Min(
                pickerPanel.ContentHeight,
                Math.Min(480, Math.Max(320, workingArea.Height - 24)));
            pickerPanel.Height = pickerHeight;

            var host = new ToolStripControlHost(pickerPanel)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = pickerPanel.Size
            };
            var dropDown = new ToolStripDropDown
            {
                AutoClose = true,
                AutoSize = false,
                BackColor = Color.White,
                DropShadowEnabled = true,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Renderer = new BorderlessDropDownRenderer(),
                Size = pickerPanel.Size
            };
            dropDown.Items.Add(host);
            pickerPanel.OperationSelected += operation =>
            {
                OperationType.SelectedItem = operation;
                dropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
            };
            pickerPanel.CancelRequested += () => dropDown.Close(ToolStripDropDownCloseReason.Keyboard);
            dropDown.Closed += (sender, args) =>
            {
                if (ReferenceEquals(activeOperationTypePicker, dropDown))
                {
                    activeOperationTypePicker = null;
                }
                if (!anchorControl.IsDisposed && anchorControl.IsHandleCreated)
                {
                    anchorControl.BeginInvoke(new Action(dropDown.Dispose));
                }
                else
                {
                    dropDown.Dispose();
                }
            };
            activeOperationTypePicker = dropDown;
            dropDown.Show(
                anchorControl,
                new Point(Math.Min(0, anchorControl.Width - pickerWidth), anchorControl.Height));
            pickerPanel.FocusPicker();
        }

        private static string GetOperationPickerCategory(OperationType operation)
        {
            if (operation is HomeRun
                || operation is StationRunPos
                || operation is ModifyStationPos
                || operation is GetStationPos
                || operation is StationRunRel
                || operation is SetStationVel
                || operation is StationStop
                || operation is WaitStationStop)
            {
                return "运动控制";
            }
            if (operation is CreateTray || operation is TrayRunPos)
            {
                return "料盘";
            }
            if (operation is IoOperate
                || operation is IoCheck
                || operation is IoGroup
                || operation is IoLogicGoto)
            {
                return "IO";
            }
            if (operation is ProcOps
                || operation is WaitProc
                || operation is Goto
                || operation is ParamGoto
                || operation is Delay
                || operation is EndProcess
                || operation is PopupDialog)
            {
                return "流程控制";
            }
            if (operation is GetValue
                || operation is ModifyValue
                || operation is StringFormat
                || operation is Split
                || operation is Replace)
            {
                return "变量与文本";
            }
            if (operation is TcpOps
                || operation is WaitTcp
                || operation is SendTcpMsg
                || operation is ReceoveTcpMsg
                || operation is SerialPortOps
                || operation is WaitSerialPort
                || operation is SendSerialPortMsg
                || operation is ReceoveSerialPortMsg
                || operation is SendReceoveCommMsg
                || operation is PlcReadWrite
                || operation is PlcMappingControl)
            {
                return "通讯";
            }
            if (operation is GetDataStructCount
                || operation is SetDataStructItem
                || operation is GetDataStructItem
                || operation is CopyDataStructItem
                || operation is InsertDataStructItem
                || operation is DelDataStructItem
                || operation is FindDataStructItem)
            {
                return "数据结构";
            }
            return "其他";
        }

        private static string GetOperationPickerDescription(OperationType operation)
        {
            try
            {
                var contract = OperationBehaviorCatalog.BuildContract(operation);
                if (!string.Equals(contract?["coverage"]?.ToString(), "unknown", StringComparison.Ordinal))
                {
                    string purpose = contract?["purpose"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(purpose))
                    {
                        return purpose;
                    }
                }
            }
            catch
            {
            }
            return "类别：" + GetOperationPickerCategory(operation);
        }

        private sealed class OperationTypePickerPanel : UserControl
        {
            private static readonly string[] CategoryOrder =
            {
                "流程控制", "变量与文本", "IO", "运动控制",
                "料盘", "通讯", "数据结构", "其他"
            };

            private readonly List<OperationType> templates;
            private readonly List<PickerGroup> groups = new List<PickerGroup>();
            private readonly List<System.Windows.Forms.Button> operationButtons
                = new List<System.Windows.Forms.Button>();
            private readonly System.Windows.Forms.ToolTip toolTip = new System.Windows.Forms.ToolTip();
            private readonly Font categoryFont;
            private int layoutColumns = 1;

            public OperationTypePickerPanel(List<OperationType> templates)
            {
                this.templates = templates;
                AutoScaleMode = AutoScaleMode.None;
                AutoScroll = false;
                BackColor = Color.White;
                DoubleBuffered = true;
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
                categoryFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                TabStop = true;
                BuildPicker();
                SizeChanged += (sender, args) => RefreshPickerLayout();
            }

            public event Action<OperationType> OperationSelected;
            public event Action CancelRequested;
            public int ContentHeight { get; private set; }

            private void BuildPicker()
            {
                SuspendLayout();
                foreach (string categoryName in CategoryOrder)
                {
                    List<OperationType> categoryOperations = templates
                        .Where(item => string.Equals(
                            GetOperationPickerCategory(item),
                            categoryName,
                            StringComparison.Ordinal))
                        .ToList();
                    if (categoryOperations.Count == 0)
                    {
                        continue;
                    }

                    var categoryLabel = new Label
                    {
                        AutoEllipsis = true,
                        BackColor = Color.FromArgb(238, 243, 248),
                        Font = categoryFont,
                        ForeColor = Color.FromArgb(48, 63, 78),
                        Padding = new Padding(8, 0, 4, 0),
                        Text = categoryName,
                        TextAlign = ContentAlignment.MiddleLeft
                    };
                    categoryLabel.Paint += (sender, args) =>
                    {
                        using (SolidBrush accent = new SolidBrush(Color.FromArgb(38, 126, 186)))
                        {
                            args.Graphics.FillRectangle(accent, 0, 3, 3, Math.Max(1, categoryLabel.Height - 6));
                        }
                    };
                    Controls.Add(categoryLabel);

                    var group = new PickerGroup(categoryLabel);
                    foreach (OperationType operation in categoryOperations)
                    {
                        bool alternate = group.Buttons.Count % 2 == 1;
                        var button = new System.Windows.Forms.Button
                        {
                            AccessibleName = operation.OperaType,
                            AutoEllipsis = true,
                            BackColor = alternate ? Color.FromArgb(248, 250, 252) : Color.White,
                            Cursor = Cursors.Hand,
                            FlatStyle = FlatStyle.Flat,
                            Font = Font,
                            ForeColor = Color.FromArgb(48, 63, 78),
                            Padding = new Padding(8, 0, 6, 0),
                            TabStop = true,
                            Tag = operation,
                            Text = string.Empty,
                            TextAlign = ContentAlignment.MiddleLeft,
                            UseVisualStyleBackColor = false
                        };
                        button.FlatAppearance.BorderSize = 0;
                        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(217, 234, 250);
                        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(205, 225, 243);
                        button.Paint += (sender, args) =>
                        {
                            TextRenderer.DrawText(
                                args.Graphics,
                                operation.OperaType,
                                button.Font,
                                new Rectangle(
                                    8,
                                    0,
                                    Math.Max(1, button.ClientSize.Width - 14),
                                    button.ClientSize.Height),
                                button.ForeColor,
                                TextFormatFlags.Left
                                    | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.SingleLine
                                    | TextFormatFlags.EndEllipsis
                                    | TextFormatFlags.NoPadding);
                            using (Pen separator = new Pen(Color.FromArgb(222, 228, 234)))
                            {
                                args.Graphics.DrawLine(
                                    separator,
                                    0,
                                    button.ClientSize.Height - 1,
                                    button.ClientSize.Width,
                                    button.ClientSize.Height - 1);
                            }
                        };
                        button.Click += (sender, args) =>
                            OperationSelected?.Invoke((OperationType)button.Tag);
                        button.PreviewKeyDown += (sender, args) =>
                        {
                            if (args.KeyCode == Keys.Left
                                || args.KeyCode == Keys.Right
                                || args.KeyCode == Keys.Up
                                || args.KeyCode == Keys.Down
                                || args.KeyCode == Keys.Escape)
                            {
                                args.IsInputKey = true;
                            }
                        };
                        button.KeyDown += OperationButton_KeyDown;
                        toolTip.SetToolTip(button, GetOperationPickerDescription(operation));
                        group.Buttons.Add(button);
                        operationButtons.Add(button);
                        Controls.Add(button);
                    }
                    groups.Add(group);
                }
                ResumeLayout(false);
                RefreshPickerLayout();
            }

            public void RefreshPickerLayout()
            {
                if (groups.Count == 0)
                {
                    ContentHeight = 80;
                    AutoScrollMinSize = new Size(0, ContentHeight);
                    return;
                }

                const int outerPadding = 8;
                const int columnGap = 8;
                const int rowGap = 1;
                const int itemHeight = 22;
                const int categoryHeight = 19;
                const int groupGap = 4;
                layoutColumns = 3;
                int availableWidth = Math.Max(360, ClientSize.Width - outerPadding * 2);
                int itemWidth = Math.Max(
                    110,
                    (availableWidth - columnGap * (layoutColumns - 1)) / layoutColumns);
                int[] columnY = { 6, 6, 6 };

                foreach (PickerGroup group in groups)
                {
                    int column = 0;
                    for (int index = 1; index < columnY.Length; index++)
                    {
                        if (columnY[index] < columnY[column])
                        {
                            column = index;
                        }
                    }
                    int x = outerPadding + column * (itemWidth + columnGap);
                    int y = columnY[column];
                    group.Label.SetBounds(x, y, itemWidth, categoryHeight);
                    y += categoryHeight + 1;
                    for (int index = 0; index < group.Buttons.Count; index++)
                    {
                        group.Buttons[index].SetBounds(
                            x,
                            y + index * (itemHeight + rowGap),
                            itemWidth,
                            itemHeight);
                    }
                    columnY[column] = y + group.Buttons.Count * (itemHeight + rowGap) + groupGap;
                }

                ContentHeight = columnY.Max() + 2;
                AutoScrollMinSize = Size.Empty;
            }

            public void FocusPicker()
            {
                Focus();
            }

            private void OperationButton_KeyDown(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Escape)
                {
                    CancelRequested?.Invoke();
                    args.Handled = true;
                    return;
                }

                var button = sender as System.Windows.Forms.Button;
                int index = operationButtons.IndexOf(button);
                if (index < 0)
                {
                    return;
                }

                int targetIndex = index;
                switch (args.KeyCode)
                {
                    case Keys.Left:
                        targetIndex--;
                        break;
                    case Keys.Right:
                        targetIndex++;
                        break;
                    case Keys.Up:
                        targetIndex -= layoutColumns;
                        break;
                    case Keys.Down:
                        targetIndex += layoutColumns;
                        break;
                    default:
                        return;
                }

                if (targetIndex >= 0 && targetIndex < operationButtons.Count)
                {
                    operationButtons[targetIndex].Focus();
                }
                args.Handled = true;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    toolTip.Dispose();
                    categoryFont.Dispose();
                }
                base.Dispose(disposing);
            }

            private sealed class PickerGroup
            {
                public PickerGroup(Label label)
                {
                    Label = label;
                }

                public Label Label { get; }
                public List<System.Windows.Forms.Button> Buttons { get; }
                    = new List<System.Windows.Forms.Button>();
            }
        }

        private sealed class BorderlessDropDownRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
            }
        }
        public OperationType temp;
        private void OperationType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (OperationType.SelectedIndex == -1
                || (SF.isModify != ModifyKind.Operation && !SF.isAddOps)
                || !(OperationType.SelectedItem is OperationType template))
            {
                return;
            }

            int num = SF.isAddOps && propertyGrid1.SelectedObject is OperationType current
                ? current.Num
                : SF.frmDataGrid.iSelectedRow;

            // 克隆会经过属性 setter。克隆期间关闭编辑联动，避免 setter 操作仍指向旧类型的全局草稿。
            ModifyKind originalModifyKind = SF.isModify;
            bool originalIsAddOps = SF.isAddOps;
            OperationType draft;
            try
            {
                SF.isModify = ModifyKind.None;
                SF.isAddOps = false;
                draft = (OperationType)template.Clone();
            }
            finally
            {
                SF.isModify = originalModifyKind;
                SF.isAddOps = originalIsAddOps;
            }

            draft.Num = num;
            SF.frmDataGrid.OperationTemp = draft;
            propertyGrid1.SelectedObject = draft;
            draft.evtRP?.Invoke();
            if (SF.ActiveEditSession != null)
            {
                SF.ReplaceActiveEditDraft(draft);
            }
            propertyGrid1.ExpandAllGridItems();
        }
        //展开特定的组
        public void ExpandGroup(PropertyGrid propertyGrid, string groupName)
        {
            GridItem root = propertyGrid.SelectedGridItem;
            while (root.Parent != null)
                root = root.Parent;

            if (root != null)
            {
                foreach (GridItem g in root.GridItems)
                {
                    if (g.GridItemType == GridItemType.Category && g.Label == groupName)
                    {
                        g.Expanded = true;
                        break;
                    }
                }
            }
        }
        private void FrmPropertyGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SF.frmToolBar.btnSave.PerformClick();
            }
        }

        private void propertyGrid1_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            object selected = propertyGrid1.SelectedObject;
            if (selected == null)
            {
                return;
            }
            TypeDescriptor.Refresh(selected);
            propertyGrid1.SelectedObject = selected;
            propertyGrid1.Refresh();
        }

        private void Address_Click(object sender, EventArgs e)
        {
            var obj = SF.frmDataGrid.OperationTemp;

            foreach (var propertyInfo in obj.GetType().GetProperties())
            {
                var markedAttribute = propertyInfo.GetCustomAttribute<ItemTypeAttribute>();

                if (markedAttribute != null)
                {
                    string propertyName = propertyInfo.Name;
                    if (PropertyName == propertyName)
                    {

                    }
                }
                else
                {
                    // 获取标记了 ItemTypeAttribute 的属性的名称
                    string propertyName = propertyInfo.Name;

                    // 获取属性的值
                    var propertyValue = propertyInfo.GetValue(obj);
                    // 如果属性的值是 List<T> 类型，进一步迭代获取列表元素的属性信息
                    if (propertyValue is System.Collections.IEnumerable enumerable && !(propertyValue is string))
                    {
                        int Num = 1;
                        foreach (var listItem in enumerable)
                        {
                            foreach (var listItemPropertyInfo in listItem.GetType().GetProperties())
                            {
                                var listItemMarkedAttribute = listItemPropertyInfo.GetCustomAttribute<ItemTypeAttribute>();

                                if (listItemMarkedAttribute != null)
                                {
                                    // 获取列表元素中标记了 ItemTypeAttribute 的属性的名称
                                    string listItemPropertyName = listItemPropertyInfo.Name;
                                    string label = listItemMarkedAttribute.Label;
                                    string[] keyTemp = label.Split('-');
                                    if (Index == Num.ToString())
                                    {
                                        GridItem parentGridItem = propertyGrid1.SelectedGridItem;

                                        parentGridItem = parentGridItem.Parent;
                                        // 获取父级的 PropertyDescriptor
                                        PropertyDescriptor parentPropertyDescriptor = parentGridItem.PropertyDescriptor;

                                        // 获取父级属性的实例
                                        object parentInstance = null;

                                        if (parentPropertyDescriptor != null)
                                        {
                                            parentInstance = parentPropertyDescriptor.GetValue(parentGridItem.Parent);
                                        }

                                        if (Key[0] == keyTemp[0]&& Key[1] == keyTemp[1])
                                        {
                                            SetPropertyAttribute(parentInstance, listItemPropertyName, typeof(BrowsableAttribute), "browsable", true);
                                        }
                                        else if(Key[0] == keyTemp[0] && Key[1] != keyTemp[1])
                                        {
                                            SetPropertyAttribute(parentInstance, listItemPropertyName, typeof(BrowsableAttribute), "browsable", false);
                                        }
                                        
                                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmPropertyGrid.propertyGrid1.SelectedObject;
                                    }
                                }
                            }

                            Num++;
                        }
                        
                    }
                }

            }
        }
        public void SetPropertyAttribute(object obj, string propertyName, Type attrType, string attrField, object value)
        {
            if (obj == null || string.IsNullOrEmpty(propertyName) || attrType == null)
            {
                return;
            }
            PropertyDescriptorCollection props = InlineListTypeDescriptionProvider.GetOriginalProperties(obj)
                ?? TypeDescriptor.GetProperties(obj);
            PropertyDescriptor prop = props[propertyName];
            if (prop == null)
            {
                return;
            }
            Attribute attr = prop.Attributes[attrType];
            if (attr == null)
            {
                return;
            }
            FieldInfo field = attrType.GetField(attrField, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return;
            }
            field.SetValue(attr, value);
        }
        public string PropertyName;
        public string AttributeName;
        public string Index;
        public string[] Key;
        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            propertyGrid1.ContextMenuStrip.Items.Clear();
            AttributeName = "";
            PropertyName = "";
            Index = "";
            Key = null;

            ItemTypeAttribute itemTypeAttribute = propertyGrid1.SelectedGridItem.PropertyDescriptor.Attributes[typeof(ItemTypeAttribute)] as ItemTypeAttribute;

            if (itemTypeAttribute != null)
            {
                AttributeName = itemTypeAttribute.Label;
                PropertyName = propertyGrid1.SelectedGridItem.PropertyDescriptor.Name;

                GridItem parentGridItem = propertyGrid1.SelectedGridItem;

                parentGridItem = parentGridItem.Parent;

                Index = parentGridItem.Label.Substring(parentGridItem.Label.IndexOf("：")+1, parentGridItem.Label.Length - parentGridItem.Label.IndexOf("：")-1);

                Key = AttributeName.Split('-');

            }
            propertyGrid1.ContextMenuStrip.Items.Add("切换地址", null, Address_Click);  
        }
    }
  

    public sealed class InlineListTypeDescriptionProvider : TypeDescriptionProvider
    {
        private static bool registered;
        private static InlineListTypeDescriptionProvider instance;
        private readonly TypeDescriptionProvider baseProvider;

        public InlineListTypeDescriptionProvider()
            : this(TypeDescriptor.GetProvider(typeof(OperationType)))
        {
        }

        private InlineListTypeDescriptionProvider(TypeDescriptionProvider baseProvider)
        {
            this.baseProvider = baseProvider;
        }

        public static void Register()
        {
            if (registered)
            {
                return;
            }
            instance = new InlineListTypeDescriptionProvider();
            TypeDescriptor.AddProvider(instance, typeof(OperationType));
            registered = true;
        }

        public static PropertyDescriptorCollection GetOriginalProperties(object instance)
        {
            if (instance == null || !registered || InlineListTypeDescriptionProvider.instance == null)
            {
                return null;
            }
            return InlineListTypeDescriptionProvider.instance.baseProvider
                .GetTypeDescriptor(instance)
                .GetProperties();
        }

        public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
        {
            ICustomTypeDescriptor descriptor = baseProvider.GetTypeDescriptor(objectType, instance);
            if (instance == null)
            {
                return descriptor;
            }
            return new InlineListCustomTypeDescriptor(descriptor);
        }
    }

    public sealed class InlineListCustomTypeDescriptor : CustomTypeDescriptor
    {
        public InlineListCustomTypeDescriptor(ICustomTypeDescriptor parent)
            : base(parent)
        {
        }

        public override PropertyDescriptorCollection GetProperties()
        {
            return GetProperties(null);
        }

        public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            PropertyDescriptorCollection props = base.GetProperties(attributes);
            List<PropertyDescriptor> list = new List<PropertyDescriptor>(props.Count);
            foreach (PropertyDescriptor prop in props)
            {
                object propertyOwner = GetPropertyOwner(prop);
                bool visible = prop.IsBrowsable;
                if (propertyOwner is IPropertyVisibilityProvider visibilityProvider)
                {
                    visible = visibilityProvider.IsPropertyVisible(prop.Name, visible);
                }
                if (!visible)
                {
                    continue;
                }

                InlineGroupAttribute inlineGroup = prop.Attributes[typeof(InlineGroupAttribute)] as InlineGroupAttribute;
                if (inlineGroup != null)
                {
                    object groupValue = prop.GetValue(propertyOwner);
                    if (groupValue != null)
                    {
                        string groupLabel = string.IsNullOrWhiteSpace(inlineGroup.DisplayName) ? prop.DisplayName : inlineGroup.DisplayName;
                        PropertyDescriptorCollection groupProps = GetExpandableProperties(groupValue);
                        foreach (PropertyDescriptor child in groupProps)
                        {
                            list.Add(new InlineGroupMemberDescriptor(prop, child, groupLabel));
                        }
                    }
                    continue;
                }

                InlineListAttribute inline = prop.Attributes[typeof(InlineListAttribute)] as InlineListAttribute;
                if (inline == null)
                {
                    list.Add(prop);
                    continue;
                }

                IList items = prop.GetValue(propertyOwner) as IList;
                if (items == null)
                {
                    continue;
                }

                string displayName = string.IsNullOrWhiteSpace(inline.DisplayName) ? prop.DisplayName : inline.DisplayName;
                for (int i = 0; i < items.Count; i++)
                {
                    object item = items[i];
                    if (item == null)
                    {
                        continue;
                    }
                    string groupLabel = $"{displayName}：{i + 1}";
                    PropertyDescriptorCollection itemProps = GetExpandableProperties(item);
                    foreach (PropertyDescriptor child in itemProps)
                    {
                        list.Add(new InlineListItemMemberDescriptor(prop, i, child, groupLabel));
                    }
                }
            }
            list = ValueRefPropertyMerger.Merge(list);
            return new PropertyDescriptorCollection(list.ToArray(), true);
        }

        private static PropertyDescriptorCollection GetExpandableProperties(object value)
        {
            TypeConverter converter = TypeDescriptor.GetConverter(value);
            if (converter != null && converter.GetPropertiesSupported(null))
            {
                PropertyDescriptorCollection properties = converter.GetProperties(null, value, null);
                if (properties != null)
                {
                    return properties;
                }
            }
            return TypeDescriptor.GetProperties(value);
        }
    }

    public sealed class InlineGroupMemberDescriptor : PropertyDescriptor
    {
        private readonly PropertyDescriptor groupProperty;
        private readonly PropertyDescriptor memberProperty;
        private readonly string category;
        private readonly string displayName;

        public InlineGroupMemberDescriptor(PropertyDescriptor groupProperty, PropertyDescriptor memberProperty, string category)
            : base($"{groupProperty.Name}_{memberProperty.Name}", memberProperty.Attributes.Cast<Attribute>().ToArray())
        {
            this.groupProperty = groupProperty;
            this.memberProperty = memberProperty;
            this.category = category;
            displayName = memberProperty.DisplayName;
        }

        public override string DisplayName => displayName;

        public override string Category => category;

        public override Type ComponentType => groupProperty.ComponentType;

        public override bool IsReadOnly => memberProperty.IsReadOnly;

        public override Type PropertyType => memberProperty.PropertyType;

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override object GetValue(object component)
        {
            object groupValue = groupProperty.GetValue(component);
            if (groupValue == null)
            {
                return null;
            }
            return memberProperty.GetValue(groupValue);
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object component, object value)
        {
            object groupValue = groupProperty.GetValue(component);
            if (groupValue == null)
            {
                return;
            }
            memberProperty.SetValue(groupValue, value);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return false;
        }

    }

    public sealed class InlineListItemMemberDescriptor : PropertyDescriptor
    {
        private readonly PropertyDescriptor listProperty;
        private readonly int index;
        private readonly PropertyDescriptor memberProperty;
        private readonly string category;
        private readonly string displayName;

        public InlineListItemMemberDescriptor(PropertyDescriptor listProperty, int index, PropertyDescriptor memberProperty, string category)
            : base($"{listProperty.Name}_{index}_{memberProperty.Name}", memberProperty.Attributes.Cast<Attribute>().ToArray())
        {
            this.listProperty = listProperty;
            this.index = index;
            this.memberProperty = memberProperty;
            this.category = category;
            displayName = memberProperty.DisplayName;
        }

        public override string DisplayName => displayName;

        public override string Category => category;

        public override Type ComponentType => listProperty.ComponentType;

        public override bool IsReadOnly => memberProperty.IsReadOnly;

        public override Type PropertyType => memberProperty.PropertyType;

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override object GetValue(object component)
        {
            IList items = listProperty.GetValue(component) as IList;
            if (items == null || index < 0 || index >= items.Count)
            {
                return null;
            }
            object item = items[index];
            if (item == null)
            {
                return null;
            }
            return memberProperty.GetValue(item);
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object component, object value)
        {
            IList items = listProperty.GetValue(component) as IList;
            if (items == null || index < 0 || index >= items.Count)
            {
                return;
            }
            object item = items[index];
            if (item == null)
            {
                return;
            }
            memberProperty.SetValue(item, value);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return false;
        }
    }

    public enum ValueRefInputKind
    {
        Name,
        Name2,
        Index,
        Index2,
        Conflict
    }

    public sealed class ValueRefGroup
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, ValueRefInputKind>> PreferredKinds
            = new System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, ValueRefInputKind>>();

        public ValueRefGroup(string baseLabel, string category)
        {
            BaseLabel = baseLabel;
            Category = category;
        }

        public string BaseLabel { get; }
        public string Category { get; }
        public int FirstIndex { get; set; } = int.MaxValue;
        public PropertyDescriptor Index { get; set; }
        public PropertyDescriptor Index2 { get; set; }
        public PropertyDescriptor Name { get; set; }
        public PropertyDescriptor Name2 { get; set; }

        public bool IsVariableReference
        {
            get
            {
                return Name?.Converter?.GetType() == typeof(ValueItem)
                    || Name2?.Converter?.GetType() == typeof(ValueItem);
            }
        }

        public Type ComponentType
        {
            get
            {
                if (Index != null) return Index.ComponentType;
                if (Index2 != null) return Index2.ComponentType;
                if (Name != null) return Name.ComponentType;
                if (Name2 != null) return Name2.ComponentType;
                return typeof(object);
            }
        }

        public bool CanMerge
        {
            get
            {
                int count = 0;
                if (Index != null) count++;
                if (Index2 != null) count++;
                if (Name != null) count++;
                if (Name2 != null) count++;
                return count >= 2;
            }
        }

        public ValueRefInputKind GetKind(object component)
        {
            int count = 0;
            ValueRefInputKind kind = GetDefaultKind();
            if (HasValue(Index, component))
            {
                count++;
                kind = ValueRefInputKind.Index;
            }
            if (HasValue(Index2, component))
            {
                count++;
                kind = ValueRefInputKind.Index2;
            }
            if (HasValue(Name, component))
            {
                count++;
                kind = ValueRefInputKind.Name;
            }
            if (HasValue(Name2, component))
            {
                count++;
                kind = ValueRefInputKind.Name2;
            }
            if (count == 0)
            {
                ValueRefInputKind? preferred = GetPreferredKind(component);
                if (preferred.HasValue)
                {
                    return preferred.Value;
                }
                return GetDefaultKind();
            }
            if (count > 1)
            {
                return ValueRefInputKind.Conflict;
            }
            SetPreferredKind(component, kind);
            return kind;
        }

        public string GetValueText(object component, ValueRefInputKind kind)
        {
            switch (kind)
            {
                case ValueRefInputKind.Index:
                    return GetText(Index, component);
                case ValueRefInputKind.Index2:
                    return GetText(Index2, component);
                case ValueRefInputKind.Name2:
                    return GetText(Name2, component);
                case ValueRefInputKind.Name:
                    return GetText(Name, component);
                default:
                    return string.Empty;
            }
        }

        public void SetKind(object component, ValueRefInputKind kind)
        {
            ClearAll(component);
            SetPreferredKind(component, kind);
        }

        public void SetValueFromText(object component, ValueRefInputKind kind, string value)
        {
            ClearAll(component);
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            switch (kind)
            {
                case ValueRefInputKind.Index:
                    SetValue(Index, component, value);
                    break;
                case ValueRefInputKind.Index2:
                    SetValue(Index2, component, value);
                    break;
                case ValueRefInputKind.Name2:
                    SetValue(Name2, component, value);
                    break;
                case ValueRefInputKind.Name:
                    SetValue(Name, component, value);
                    break;
            }
            SetPreferredKind(component, kind);
        }

        public bool UseNameSelector(object component)
        {
            ValueRefInputKind kind = GetKind(component);
            return kind == ValueRefInputKind.Name || kind == ValueRefInputKind.Name2;
        }

        private ValueRefInputKind GetDefaultKind()
        {
            if (Name != null) return ValueRefInputKind.Name;
            if (Name2 != null) return ValueRefInputKind.Name2;
            if (Index != null) return ValueRefInputKind.Index;
            if (Index2 != null) return ValueRefInputKind.Index2;
            return ValueRefInputKind.Name;
        }

        private static bool HasValue(PropertyDescriptor prop, object component)
        {
            if (prop == null)
            {
                return false;
            }
            object value = prop.GetValue(component);
            if (value == null)
            {
                return false;
            }
            if (value is string text)
            {
                return text.Length > 0;
            }
            if (IsNumericType(prop.PropertyType))
            {
                if (!TryGetNumericValue(value, out long number))
                {
                    return false;
                }
                return number >= 0;
            }
            return true;
        }

        private static string GetText(PropertyDescriptor prop, object component)
        {
            if (prop == null)
            {
                return string.Empty;
            }
            object value = prop.GetValue(component);
            if (value == null)
            {
                return string.Empty;
            }
            if (value is string text)
            {
                return text;
            }
            if (IsNumericType(prop.PropertyType))
            {
                if (!TryGetNumericValue(value, out long number))
                {
                    return string.Empty;
                }
                if (number < 0)
                {
                    return string.Empty;
                }
                return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return value.ToString() ?? string.Empty;
        }

        private static void SetValue(PropertyDescriptor prop, object component, string value)
        {
            if (prop == null)
            {
                return;
            }
            Type propType = prop.PropertyType;
            Type targetType = Nullable.GetUnderlyingType(propType) ?? propType;
            if (targetType == typeof(string))
            {
                prop.SetValue(component, value);
                return;
            }
            if (IsNumericType(targetType))
            {
                if (!TryParseNumeric(value, targetType, out object parsed, out string error))
                {
                    throw new FormatException(error);
                }
                prop.SetValue(component, parsed);
                return;
            }
            prop.SetValue(component, Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture));
        }

        private void ClearAll(object component)
        {
            ClearValue(Index, component);
            ClearValue(Index2, component);
            ClearValue(Name, component);
            ClearValue(Name2, component);
        }

        private static void ClearValue(PropertyDescriptor prop, object component)
        {
            if (prop == null)
            {
                return;
            }
            Type propType = prop.PropertyType;
            Type targetType = Nullable.GetUnderlyingType(propType) ?? propType;
            if (targetType == typeof(string))
            {
                prop.SetValue(component, null);
                return;
            }
            if (Nullable.GetUnderlyingType(propType) != null)
            {
                prop.SetValue(component, null);
                return;
            }
            if (IsNumericType(targetType))
            {
                object emptyValue = Convert.ChangeType(-1, targetType, System.Globalization.CultureInfo.InvariantCulture);
                prop.SetValue(component, emptyValue);
                return;
            }
            object defaultValue = Activator.CreateInstance(targetType);
            prop.SetValue(component, defaultValue);
        }

        private static bool IsNumericType(Type type)
        {
            Type targetType = Nullable.GetUnderlyingType(type) ?? type;
            return targetType == typeof(int)
                || targetType == typeof(long)
                || targetType == typeof(short)
                || targetType == typeof(sbyte)
                || targetType == typeof(uint)
                || targetType == typeof(ulong)
                || targetType == typeof(ushort)
                || targetType == typeof(byte);
        }

        private static bool TryGetNumericValue(object value, out long number)
        {
            number = 0;
            if (value == null)
            {
                return false;
            }
            switch (value)
            {
                case int intValue:
                    number = intValue;
                    return true;
                case long longValue:
                    number = longValue;
                    return true;
                case short shortValue:
                    number = shortValue;
                    return true;
                case sbyte sbyteValue:
                    number = sbyteValue;
                    return true;
                case uint uintValue:
                    number = uintValue;
                    return true;
                case ulong ulongValue:
                    number = unchecked((long)ulongValue);
                    return true;
                case ushort ushortValue:
                    number = ushortValue;
                    return true;
                case byte byteValue:
                    number = byteValue;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseNumeric(string value, Type targetType, out object result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "索引不能为空";
                return false;
            }
            if (targetType == typeof(int))
            {
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(long))
            {
                if (!long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long parsed) || parsed < 0)
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(short))
            {
                if (!short.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out short parsed) || parsed < 0)
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(sbyte))
            {
                if (!sbyte.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out sbyte parsed) || parsed < 0)
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(uint))
            {
                if (!uint.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint parsed))
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(ulong))
            {
                if (!ulong.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out ulong parsed))
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(ushort))
            {
                if (!ushort.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out ushort parsed))
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(byte))
            {
                if (!byte.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out byte parsed))
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            error = "索引格式不支持";
            return false;
        }

        private string GetKey()
        {
            return $"{Category ?? string.Empty}||{BaseLabel ?? string.Empty}";
        }

        private ValueRefInputKind? GetPreferredKind(object component)
        {
            if (component == null)
            {
                return null;
            }
            if (!PreferredKinds.TryGetValue(component, out Dictionary<string, ValueRefInputKind> map))
            {
                return null;
            }
            if (map.TryGetValue(GetKey(), out ValueRefInputKind kind))
            {
                return kind;
            }
            return null;
        }

        private void SetPreferredKind(object component, ValueRefInputKind kind)
        {
            if (component == null)
            {
                return;
            }
            if (kind == ValueRefInputKind.Conflict)
            {
                return;
            }
            Dictionary<string, ValueRefInputKind> map = PreferredKinds.GetOrCreateValue(component);
            map[GetKey()] = kind;
        }
    }

    public static class ValueRefPropertyMerger
    {
        private const string SuffixIndex = "索引";
        private const string SuffixIndex2 = "索引二级";
        private const string SuffixName = "名称";
        private const string SuffixName2 = "名称二级";

        public static List<PropertyDescriptor> Merge(List<PropertyDescriptor> props)
        {
            Dictionary<string, ValueRefGroup> groups = new Dictionary<string, ValueRefGroup>();
            Dictionary<string, (PropertyDescriptor Prop, int Index)> nameCandidates = new Dictionary<string, (PropertyDescriptor, int)>();

            for (int i = 0; i < props.Count; i++)
            {
                PropertyDescriptor prop = props[i];
                if (TryGetValueRefPart(prop, out string baseLabel, out ValueRefInputKind part))
                {
                    string key = GetKey(prop.Category, baseLabel);
                    if (!groups.TryGetValue(key, out ValueRefGroup group))
                    {
                        group = new ValueRefGroup(baseLabel, prop.Category);
                        groups[key] = group;
                    }
                    group.FirstIndex = Math.Min(group.FirstIndex, i);
                    switch (part)
                    {
                        case ValueRefInputKind.Index:
                            group.Index = prop;
                            break;
                        case ValueRefInputKind.Index2:
                            group.Index2 = prop;
                            break;
                        case ValueRefInputKind.Name:
                            group.Name = prop;
                            break;
                        case ValueRefInputKind.Name2:
                            group.Name2 = prop;
                            break;
                    }
                    continue;
                }
                if (IsNameCandidate(prop, out string candidateBase))
                {
                    string key = GetKey(prop.Category, candidateBase);
                    if (!nameCandidates.ContainsKey(key))
                    {
                        nameCandidates[key] = (prop, i);
                    }
                }
            }

            foreach (KeyValuePair<string, (PropertyDescriptor Prop, int Index)> candidate in nameCandidates)
            {
                if (!groups.TryGetValue(candidate.Key, out ValueRefGroup group))
                {
                    continue;
                }
                if (group.Name == null)
                {
                    group.Name = candidate.Value.Prop;
                    group.FirstIndex = Math.Min(group.FirstIndex, candidate.Value.Index);
                }
            }

            List<ValueRefGroup> validGroups = groups.Values.Where(group => group.CanMerge).ToList();
            if (validGroups.Count == 0)
            {
                return props;
            }

            HashSet<PropertyDescriptor> hidden = new HashSet<PropertyDescriptor>();
            Dictionary<int, List<ValueRefGroup>> insertAt = new Dictionary<int, List<ValueRefGroup>>();
            foreach (ValueRefGroup group in validGroups)
            {
                if (group.Index != null) hidden.Add(group.Index);
                if (group.Index2 != null) hidden.Add(group.Index2);
                if (group.Name != null) hidden.Add(group.Name);
                if (group.Name2 != null) hidden.Add(group.Name2);

                if (!insertAt.TryGetValue(group.FirstIndex, out List<ValueRefGroup> groupList))
                {
                    groupList = new List<ValueRefGroup>();
                    insertAt[group.FirstIndex] = groupList;
                }
                groupList.Add(group);
            }

            List<PropertyDescriptor> output = new List<PropertyDescriptor>(props.Count);
            for (int i = 0; i < props.Count; i++)
            {
                if (insertAt.TryGetValue(i, out List<ValueRefGroup> groupList))
                {
                    foreach (ValueRefGroup group in groupList)
                    {
                        output.Add(new ValueRefTypePropertyDescriptor(group));
                        output.Add(new ValueRefValuePropertyDescriptor(group));
                    }
                }
                PropertyDescriptor prop = props[i];
                if (hidden.Contains(prop))
                {
                    continue;
                }
                output.Add(prop);
            }

            return output;
        }

        private static bool TryGetValueRefPart(PropertyDescriptor prop, out string baseLabel, out ValueRefInputKind part)
        {
            baseLabel = null;
            part = ValueRefInputKind.Name;
            string name = prop.DisplayName ?? string.Empty;
            if (name.EndsWith(SuffixIndex2, StringComparison.Ordinal))
            {
                baseLabel = name.Substring(0, name.Length - SuffixIndex2.Length);
                part = ValueRefInputKind.Index2;
                return true;
            }
            if (name.EndsWith(SuffixIndex, StringComparison.Ordinal))
            {
                baseLabel = name.Substring(0, name.Length - SuffixIndex.Length);
                part = ValueRefInputKind.Index;
                return true;
            }
            if (name.EndsWith(SuffixName2, StringComparison.Ordinal))
            {
                baseLabel = name.Substring(0, name.Length - SuffixName2.Length);
                part = ValueRefInputKind.Name2;
                return true;
            }
            if (name.EndsWith(SuffixName, StringComparison.Ordinal))
            {
                baseLabel = name.Substring(0, name.Length - SuffixName.Length);
                part = ValueRefInputKind.Name;
                return true;
            }
            return false;
        }

        private static bool IsNameCandidate(PropertyDescriptor prop, out string baseLabel)
        {
            baseLabel = null;
            string name = prop.DisplayName ?? string.Empty;
            if (name.Length == 0)
            {
                return false;
            }
            if (name.EndsWith(SuffixIndex2, StringComparison.Ordinal)
                || name.EndsWith(SuffixIndex, StringComparison.Ordinal)
                || name.EndsWith(SuffixName2, StringComparison.Ordinal)
                || name.EndsWith(SuffixName, StringComparison.Ordinal))
            {
                return false;
            }
            if (prop.Converter == null || prop.Converter.GetType() != typeof(ValueItem))
            {
                return false;
            }
            baseLabel = name;
            return true;
        }

        private static string GetKey(string category, string baseLabel)
        {
            return $"{category ?? string.Empty}||{baseLabel ?? string.Empty}";
        }
    }

    public sealed class ValueRefTypePropertyDescriptor : PropertyDescriptor
    {
        private readonly ValueRefGroup group;

        public ValueRefTypePropertyDescriptor(ValueRefGroup group)
            : base($"{group.Category}.{group.BaseLabel}.Type", null)
        {
            this.group = group;
        }

        public override string DisplayName
        {
            get
            {
                return "引用方式";
            }
        }

        public override string Category => group.Category;

        public override Type ComponentType => group.ComponentType;

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override object GetValue(object component)
        {
            return ToText(group.GetKind(component), group);
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object component, object value)
        {
            ValueRefInputKind kind = ParseKind(value as string, group);
            if (kind == ValueRefInputKind.Conflict)
            {
                return;
            }
            group.SetKind(component, kind);
            TypeDescriptor.Refresh(component);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return false;
        }

        public override TypeConverter Converter => new ValueRefTypeConverter(group);

        private static string ToText(ValueRefInputKind kind, ValueRefGroup group)
        {
            switch (kind)
            {
                case ValueRefInputKind.Index:
                    return "索引";
                case ValueRefInputKind.Index2:
                    return "索引二级";
                case ValueRefInputKind.Name2:
                    return group.IsVariableReference ? "变量二级" : "名称二级";
                case ValueRefInputKind.Name:
                    return group.IsVariableReference ? "变量" : "名称";
                case ValueRefInputKind.Conflict:
                    return "冲突";
                default:
                    return group.IsVariableReference ? "变量" : "名称";
            }
        }

        private static ValueRefInputKind ParseKind(string text, ValueRefGroup group)
        {
            if (string.Equals(text, "索引", StringComparison.Ordinal))
            {
                return group.Index != null ? ValueRefInputKind.Index : ValueRefInputKind.Conflict;
            }
            if (string.Equals(text, "索引二级", StringComparison.Ordinal))
            {
                return group.Index2 != null ? ValueRefInputKind.Index2 : ValueRefInputKind.Conflict;
            }
            if (string.Equals(text, "名称二级", StringComparison.Ordinal)
                || (group.IsVariableReference && string.Equals(text, "变量二级", StringComparison.Ordinal)))
            {
                return group.Name2 != null ? ValueRefInputKind.Name2 : ValueRefInputKind.Conflict;
            }
            if (string.Equals(text, "名称", StringComparison.Ordinal)
                || (group.IsVariableReference && string.Equals(text, "变量", StringComparison.Ordinal)))
            {
                return group.Name != null ? ValueRefInputKind.Name : ValueRefInputKind.Conflict;
            }
            if (string.Equals(text, "冲突", StringComparison.Ordinal))
            {
                return ValueRefInputKind.Conflict;
            }
            return group.Name != null ? ValueRefInputKind.Name : ValueRefInputKind.Conflict;
        }

        private sealed class ValueRefTypeConverter : StringConverter
        {
            private readonly ValueRefGroup group;

            public ValueRefTypeConverter(ValueRefGroup group)
            {
                this.group = group;
            }

            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                List<string> values = new List<string>();
                if (IsConflict(context))
                {
                    values.Add("冲突");
                }
                if (group.Name != null) values.Add(group.IsVariableReference ? "变量" : "名称");
                if (group.Name2 != null) values.Add(group.IsVariableReference ? "变量二级" : "名称二级");
                if (group.Index != null) values.Add("索引");
                if (group.Index2 != null) values.Add("索引二级");
                if (values.Count == 0)
                {
                    values.Add("名称");
                }
                return new StandardValuesCollection(values);
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            private bool IsConflict(ITypeDescriptorContext context)
            {
                object instance = GetContextInstance(context);
                if (instance == null)
                {
                    return false;
                }
                return group.GetKind(instance) == ValueRefInputKind.Conflict;
            }

            private static object GetContextInstance(ITypeDescriptorContext context)
            {
                if (context == null)
                {
                    return null;
                }
                if (context.Instance is object[] instances)
                {
                    if (instances.Length > 0)
                    {
                        return instances[0];
                    }
                    return null;
                }
                return context.Instance;
            }
        }
    }

    public sealed class ValueRefValuePropertyDescriptor : PropertyDescriptor
    {
        private readonly ValueRefGroup group;

        public ValueRefValuePropertyDescriptor(ValueRefGroup group)
            : base($"{group.Category}.{group.BaseLabel}.Value", null)
        {
            this.group = group;
        }

        public override string DisplayName => group.BaseLabel;

        public override string Category => group.Category;

        public override Type ComponentType => group.ComponentType;

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override object GetValue(object component)
        {
            ValueRefInputKind kind = group.GetKind(component);
            if (kind == ValueRefInputKind.Conflict)
            {
                return string.Empty;
            }
            return group.GetValueText(component, kind);
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object component, object value)
        {
            string text = value as string;
            ValueRefInputKind kind = group.GetKind(component);
            if (kind == ValueRefInputKind.Conflict)
            {
                kind = ValueRefInputKind.Name;
            }
            group.SetValueFromText(component, kind, text);
            TypeDescriptor.Refresh(component);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return false;
        }

        public override TypeConverter Converter => new ValueRefValueConverter(group);

        private sealed class ValueRefValueConverter : StringConverter
        {
            private readonly ValueRefGroup group;

            public ValueRefValueConverter(ValueRefGroup group)
            {
                this.group = group;
            }

            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                object instance = GetContextInstance(context);
                if (instance == null)
                {
                    return false;
                }
                TypeConverter converter = GetActiveConverter(instance);
                if (converter == null)
                {
                    return false;
                }
                return converter.GetStandardValuesSupported(context);
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                object instance = GetContextInstance(context);
                if (instance == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                TypeConverter converter = GetActiveConverter(instance);
                if (converter == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                return converter.GetStandardValues(context);
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                object instance = GetContextInstance(context);
                if (instance == null)
                {
                    return false;
                }
                TypeConverter converter = GetActiveConverter(instance);
                if (converter == null)
                {
                    return false;
                }
                return converter.GetStandardValuesExclusive(context);
            }

            private static object GetContextInstance(ITypeDescriptorContext context)
            {
                if (context == null)
                {
                    return null;
                }
                if (context.Instance is object[] instances)
                {
                    if (instances.Length > 0)
                    {
                        return instances[0];
                    }
                    return null;
                }
                return context.Instance;
            }

            private TypeConverter GetActiveConverter(object instance)
            {
                ValueRefInputKind kind = group.GetKind(instance);
                switch (kind)
                {
                    case ValueRefInputKind.Name:
                        return group.Name?.Converter;
                    case ValueRefInputKind.Name2:
                        return group.Name2?.Converter;
                    case ValueRefInputKind.Index:
                        return group.Index?.Converter;
                    case ValueRefInputKind.Index2:
                        return group.Index2?.Converter;
                    default:
                        return null;
                }
            }
        }
    }
}
