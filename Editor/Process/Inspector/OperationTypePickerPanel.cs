// 模块：编辑器 / 流程 / Inspector。
// 职责范围：指令属性定义、编辑控件、选择器和值转换。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
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
            BackColor = UiPalette.Surface;
            DoubleBuffered = true;
            Font = InspectorFonts.Regular85;
            // 分类标题保持较小字号，通过原生粗体建立层级。
            categoryFont = new Font(
                InspectorFonts.Bold9.FontFamily,
                10F,
                FontStyle.Bold,
                GraphicsUnit.Point);
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
                    BackColor = UiPalette.SurfaceSubtle,
                    Font = categoryFont,
                    ForeColor = UiPalette.TextPrimary,
                    Padding = new Padding(30, 0, 4, 0),
                    Text = categoryName,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                categoryLabel.Paint += (sender, args) => InspectorIcons.Draw(
                    args.Graphics,
                    new Rectangle(9, 3, 15, 15),
                    GetCategoryIcon(categoryName),
                    UiPalette.Brand);
                Controls.Add(categoryLabel);

                var group = new PickerGroup(categoryLabel);
                foreach (OperationType operation in categoryOperations)
                {
                    var button = new Button
                    {
                        AccessibleName = operation.OperaType,
                        AutoEllipsis = true,
                        BackColor = UiPalette.Surface,
                        Cursor = Cursors.Hand,
                        FlatStyle = FlatStyle.Flat,
                        Font = Font,
                        ForeColor = UiPalette.TextSecondary,
                        Padding = new Padding(8, 0, 6, 0),
                        TabStop = true,
                        Tag = operation,
                        Text = string.Empty,
                        TextAlign = ContentAlignment.MiddleLeft,
                        UseVisualStyleBackColor = false
                    };
                    button.FlatAppearance.BorderSize = 0;
                    button.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoft;
                    button.FlatAppearance.MouseDownBackColor = UiPalette.BrandSoftHover;
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
                        using (Pen separator = new Pen(UiPalette.Stroke))
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
            const int itemHeight = 23;
            const int categoryHeight = 21;
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
                || operation is ReceiveTcpMsg
                || operation is SerialPortOps
                || operation is WaitSerialPort
                || operation is SendSerialPortMsg
                || operation is ReceiveSerialPortMsg
                || operation is SendReceiveCommMsg
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

        private static InspectorIconKind GetCategoryIcon(string category)
        {
            switch (category)
            {
                case "流程控制":
                    return InspectorIconKind.Process;
                case "变量与文本":
                case "数据结构":
                    return InspectorIconKind.Data;
                case "IO":
                    return InspectorIconKind.InputOutput;
                case "运动控制":
                case "料盘":
                    return InspectorIconKind.Motion;
                case "通讯":
                    return InspectorIconKind.Communication;
                default:
                    return InspectorIconKind.Settings;
            }
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

    internal sealed class InstantToolStripDropDown : ToolStripDropDown
    {
        private const int WmSetRedraw = 0x000B;
        private const uint RedrawInvalidate = 0x0001;
        private const uint RedrawErase = 0x0004;
        private const uint RedrawAllChildren = 0x0080;
        private const uint RedrawUpdateNow = 0x0100;
        private const uint RedrawFrame = 0x0400;
        private const int DwmTransitionsForcedDisabled = 3;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(
            IntPtr windowHandle,
            IntPtr updateRectangle,
            IntPtr updateRegion,
            uint flags);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        public void ShowInstant(Control anchor, Point location, Control content)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            if (content == null) throw new ArgumentNullException(nameof(content));

            CreateControl();
            CreateChildHandles(content);
            IntPtr handle = Handle;
            int disableTransitions = 1;
            DwmSetWindowAttribute(
                handle,
                DwmTransitionsForcedDisabled,
                ref disableTransitions,
                sizeof(int));
            SendMessage(handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            try
            {
                Show(anchor, location);
            }
            finally
            {
                SendMessage(handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                RedrawWindow(
                    handle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    RedrawInvalidate | RedrawErase | RedrawAllChildren
                        | RedrawUpdateNow | RedrawFrame);
            }
        }

        private static void CreateChildHandles(Control control)
        {
            control.CreateControl();
            foreach (Control child in control.Controls)
            {
                CreateChildHandles(child);
            }
        }
    }

    internal sealed class BorderlessDropDownRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }
    }
}
