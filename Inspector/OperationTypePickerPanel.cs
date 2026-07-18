using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class OperationTypePickerPanel : UserControl
    {
        private static readonly string[] CategoryOrder =
        {
            "流程控制", "变量与文本", "IO", "运动控制",
            "料盘", "通讯", "数据结构", "其他"
        };

        private readonly List<OperationType> templates;
        private readonly List<PickerGroup> groups = new List<PickerGroup>();
        private readonly List<Button> operationButtons = new List<Button>();
        private readonly ToolTip toolTip = new ToolTip();
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
                        args.Graphics.FillRectangle(
                            accent,
                            0,
                            3,
                            3,
                            Math.Max(1, categoryLabel.Height - 6));
                    }
                };
                Controls.Add(categoryLabel);

                var group = new PickerGroup(categoryLabel);
                foreach (OperationType operation in categoryOperations)
                {
                    bool alternate = group.Buttons.Count % 2 == 1;
                    var button = new Button
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

            var button = sender as Button;
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
                if (!string.Equals(
                    contract?["coverage"]?.ToString(),
                    "unknown",
                    StringComparison.Ordinal))
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
            public List<Button> Buttons { get; } = new List<Button>();
        }
    }

    internal sealed class BorderlessDropDownRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }
    }
}
