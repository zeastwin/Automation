using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Automation.Protocol;
using static Automation.OperationTypePartial;

namespace Automation
{
    internal enum InspectorSelectionPickerKind
    {
        Variable,
        InputOutput,
        Point,
        Address
    }

    internal static class InspectorSelectionPickerResolver
    {
        public static bool TryResolve(
            PropertyDescriptor property,
            out InspectorSelectionPickerKind kind)
        {
            kind = default(InspectorSelectionPickerKind);
            Type converterType = property?.Converter?.GetType();
            if (converterType == typeof(ValueItem))
            {
                kind = InspectorSelectionPickerKind.Variable;
                return true;
            }
            if (converterType == typeof(IoOutItem)
                || converterType == typeof(IoInItem))
            {
                kind = InspectorSelectionPickerKind.InputOutput;
                return true;
            }
            if (converterType == typeof(StationPosDic)
                || converterType == typeof(StationPosWithSpecial))
            {
                kind = InspectorSelectionPickerKind.Point;
                return true;
            }
            if (converterType == typeof(GotoItem))
            {
                kind = InspectorSelectionPickerKind.Address;
                return true;
            }
            return false;
        }
    }

    internal sealed class InspectorSelectionPickerPanel : UserControl
    {
        private const int OuterPadding = 8;
        private const int ColumnGap = 7;
        private const int ColumnWidth = 205;
        private const int HeaderHeight = 29;
        private const int ItemHeight = 29;

        private readonly List<PickerColumnControl> columnControls
            = new List<PickerColumnControl>();
        private PickerColumnControl highlightedColumn;

        public InspectorSelectionPickerPanel(
            InspectorSelectionPickerKind kind,
            object owner,
            PropertyDescriptor property,
            string currentValue)
        {
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            BackColor = UiPalette.Surface;
            DoubleBuffered = true;
            Font = InspectorFonts.Regular9;
            TabStop = true;

            IReadOnlyList<PickerColumn> columns = InspectorSelectionPickerData.Build(
                kind,
                owner,
                property,
                currentValue);
            BuildColumns(columns, currentValue);
            SizeChanged += (sender, args) => LayoutColumns();
        }

        public event Action<string> ValueSelected;
        public event Action CancelRequested;

        public int PreferredPickerWidth
        {
            get
            {
                int visibleColumns = Math.Min(3, Math.Max(1, columnControls.Count));
                return OuterPadding * 2 + visibleColumns * ColumnWidth
                    + Math.Max(0, visibleColumns - 1) * ColumnGap;
            }
        }

        public int PreferredPickerHeight => 350;

        public void FocusPicker()
        {
            PickerColumnControl target = highlightedColumn ?? columnControls.FirstOrDefault();
            target?.FocusList();
            if (highlightedColumn != null)
            {
                ScrollControlIntoView(highlightedColumn);
            }
        }

        private void BuildColumns(
            IReadOnlyList<PickerColumn> columns,
            string currentValue)
        {
            SuspendLayout();
            try
            {
                foreach (PickerColumn column in columns)
                {
                    var columnControl = new PickerColumnControl(column, currentValue)
                    {
                        Width = ColumnWidth
                    };
                    columnControl.ValueSelected += value => ValueSelected?.Invoke(value);
                    columnControl.CancelRequested += () => CancelRequested?.Invoke();
                    columnControl.MoveColumnRequested += offset => MoveColumnFocus(
                        columnControl,
                        offset);
                    columnControls.Add(columnControl);
                    Controls.Add(columnControl);
                    if (column.Highlighted)
                    {
                        highlightedColumn = columnControl;
                    }
                }
            }
            finally
            {
                ResumeLayout(false);
            }
            LayoutColumns();
        }

        private void LayoutColumns()
        {
            int height = Math.Max(180, ClientSize.Height - OuterPadding * 2);
            for (int index = 0; index < columnControls.Count; index++)
            {
                columnControls[index].SetBounds(
                    OuterPadding + index * (ColumnWidth + ColumnGap),
                    OuterPadding,
                    ColumnWidth,
                    height);
            }
            int contentWidth = OuterPadding * 2
                + columnControls.Count * ColumnWidth
                + Math.Max(0, columnControls.Count - 1) * ColumnGap;
            AutoScrollMinSize = new Size(contentWidth, 0);
        }

        private void MoveColumnFocus(PickerColumnControl source, int offset)
        {
            int index = columnControls.IndexOf(source);
            int target = index + offset;
            if (target >= 0 && target < columnControls.Count)
            {
                columnControls[target].FocusList();
                ScrollControlIntoView(columnControls[target]);
            }
        }

        private sealed class PickerColumnControl : UserControl
        {
            private readonly PickerColumn definition;
            private readonly Label header = new Label();
            private readonly ChoiceListBox list = new ChoiceListBox();

            public PickerColumnControl(PickerColumn definition, string currentValue)
            {
                this.definition = definition;
                BackColor = definition.Highlighted
                    ? UiPalette.BrandSoft
                    : UiPalette.SurfaceSubtle;
                Padding = new Padding(1);
                Margin = Padding.Empty;

                header.AutoEllipsis = true;
                header.BackColor = definition.Highlighted
                    ? UiPalette.BrandSoftHover
                    : UiPalette.SurfaceSubtle;
                header.Font = InspectorFonts.Bold9;
                header.ForeColor = definition.Highlighted
                    ? UiPalette.Brand
                    : UiPalette.TextPrimary;
                header.Padding = new Padding(28, 0, 5, 0);
                header.Text = definition.Title;
                header.TextAlign = ContentAlignment.MiddleLeft;
                header.Paint += Header_Paint;
                Controls.Add(header);

                list.BackColor = UiPalette.Surface;
                list.BorderStyle = BorderStyle.None;
                list.DrawMode = DrawMode.OwnerDrawFixed;
                list.Font = InspectorFonts.Regular9;
                list.ForeColor = UiPalette.TextPrimary;
                list.IntegralHeight = false;
                list.ItemHeight = ItemHeight;
                list.Items.AddRange(definition.Choices.Cast<object>().ToArray());
                list.DrawItem += List_DrawItem;
                list.MouseClick += List_MouseClick;
                list.KeyDown += List_KeyDown;
                list.PreviewKeyDown += List_PreviewKeyDown;
                Controls.Add(list);

                int currentIndex = definition.Choices.FindIndex(choice =>
                    choice.Selectable && string.Equals(
                        choice.Value,
                        currentValue,
                        StringComparison.Ordinal));
                if (currentIndex >= 0)
                {
                    list.SelectedIndex = currentIndex;
                }
                Resize += (sender, args) => LayoutControls();
                LayoutControls();
            }

            public event Action<string> ValueSelected;
            public event Action CancelRequested;
            public event Action<int> MoveColumnRequested;

            public void FocusList()
            {
                list.Focus();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Color border = definition.Highlighted
                    ? UiPalette.Focus
                    : UiPalette.Stroke;
                using (var pen = new Pen(border))
                {
                    e.Graphics.DrawRectangle(
                        pen,
                        0,
                        0,
                        Math.Max(0, Width - 1),
                        Math.Max(0, Height - 1));
                }
            }

            private void Header_Paint(object sender, PaintEventArgs e)
            {
                InspectorIcons.Draw(
                    e.Graphics,
                    new Rectangle(8, 7, 15, 15),
                    definition.Icon,
                    definition.Highlighted
                        ? UiPalette.Brand
                        : UiPalette.TextSecondary);
            }

            private void List_DrawItem(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= list.Items.Count)
                {
                    return;
                }
                var choice = (PickerChoice)list.Items[e.Index];
                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                Color background = selected && choice.Selectable
                    ? UiPalette.BrandSoft
                    : UiPalette.Surface;
                using (var brush = new SolidBrush(background))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }
                Color primaryColor = choice.Selectable
                    ? UiPalette.TextPrimary
                    : UiPalette.TextDisabled;
                int secondaryWidth = string.IsNullOrWhiteSpace(choice.Secondary)
                    ? 0
                    : Math.Min(88, e.Bounds.Width / 2);
                TextRenderer.DrawText(
                    e.Graphics,
                    choice.Primary,
                    list.Font,
                    new Rectangle(
                        e.Bounds.X + 8,
                        e.Bounds.Y,
                        Math.Max(1, e.Bounds.Width - secondaryWidth - 14),
                        e.Bounds.Height),
                    primaryColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPadding);
                if (secondaryWidth > 0)
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        choice.Secondary,
                        InspectorFonts.Regular85,
                        new Rectangle(
                            e.Bounds.Right - secondaryWidth - 6,
                            e.Bounds.Y,
                            secondaryWidth,
                            e.Bounds.Height),
                        UiPalette.TextSecondary,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                            | TextFormatFlags.NoPadding);
                }
                using (var pen = new Pen(UiPalette.Divider))
                {
                    e.Graphics.DrawLine(
                        pen,
                        e.Bounds.X + 7,
                        e.Bounds.Bottom - 1,
                        e.Bounds.Right - 7,
                        e.Bounds.Bottom - 1);
                }
            }

            private void List_MouseClick(object sender, MouseEventArgs e)
            {
                int index = list.IndexFromPoint(e.Location);
                SelectChoice(index);
            }

            private void List_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    SelectChoice(list.SelectedIndex);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    CancelRequested?.Invoke();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                {
                    MoveColumnRequested?.Invoke(e.KeyCode == Keys.Left ? -1 : 1);
                    e.Handled = true;
                }
            }

            private static void List_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape
                    || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                {
                    e.IsInputKey = true;
                }
            }

            private void SelectChoice(int index)
            {
                if (index < 0 || index >= list.Items.Count)
                {
                    return;
                }
                var choice = (PickerChoice)list.Items[index];
                if (choice.Selectable)
                {
                    ValueSelected?.Invoke(choice.Value);
                }
            }

            private void LayoutControls()
            {
                header.SetBounds(1, 1, Math.Max(1, ClientSize.Width - 2), HeaderHeight);
                list.SetBounds(
                    1,
                    HeaderHeight + 1,
                    Math.Max(1, ClientSize.Width - 2),
                    Math.Max(1, ClientSize.Height - HeaderHeight - 2));
            }
        }

        private sealed class ChoiceListBox : ListBox
        {
            public ChoiceListBox()
            {
                SetStyle(
                    ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.AllPaintingInWmPaint,
                    true);
            }
        }
    }

    internal static class InspectorSelectionPickerData
    {
        public static IReadOnlyList<PickerColumn> Build(
            InspectorSelectionPickerKind kind,
            object owner,
            PropertyDescriptor property,
            string currentValue)
        {
            switch (kind)
            {
                case InspectorSelectionPickerKind.Variable:
                    return BuildVariables();
                case InspectorSelectionPickerKind.InputOutput:
                    return BuildInputOutput(property);
                case InspectorSelectionPickerKind.Point:
                    return BuildPoints(owner, property);
                case InspectorSelectionPickerKind.Address:
                    return BuildAddresses();
                default:
                    return Array.Empty<PickerColumn>();
            }
        }

        private static IReadOnlyList<PickerColumn> BuildVariables()
        {
            List<DicValue> values = SF.valueStore?.GetValuesSnapshot()
                ?? new List<DicValue>();
            Guid currentProcId = GetCurrentProcessId();
            return new[]
            {
                CreateVariableColumn(
                    "公共变量",
                    values.Where(value => string.Equals(
                        value.Scope,
                        VariableScopeContract.Public,
                        StringComparison.Ordinal))),
                CreateVariableColumn(
                    "系统变量",
                    values.Where(value => string.Equals(
                        value.Scope,
                        VariableScopeContract.System,
                        StringComparison.Ordinal))),
                CreateVariableColumn(
                    "私有变量",
                    values.Where(value => string.Equals(
                            value.Scope,
                            VariableScopeContract.Process,
                            StringComparison.Ordinal)
                        && currentProcId != Guid.Empty
                        && value.OwnerProcId == currentProcId))
            };
        }

        private static PickerColumn CreateVariableColumn(
            string title,
            IEnumerable<DicValue> source)
        {
            List<PickerChoice> choices = source
                .OrderBy(value => value.Index)
                .Select(value => new PickerChoice(
                    value.Name,
                    value.Name,
                    $"{value.Index} · {GetVariableTypeName(value.Type)}"))
                .ToList();
            return new PickerColumn(
                title,
                InspectorIconKind.Data,
                false,
                EnsureChoices(choices, "暂无变量"));
        }

        private static IReadOnlyList<PickerColumn> BuildInputOutput(
            PropertyDescriptor property)
        {
            Type converterType = property?.Converter?.GetType();
            List<IO> configured = SF.frmIO?.DicIO?.Values
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.CardNum)
                .ThenBy(item => item.Module)
                .ThenBy(item => item.Index)
                .ToList() ?? new List<IO>();

            string title;
            IEnumerable<IO> choices;
            if (converterType == typeof(IoInItem))
            {
                title = "输入 IO";
                choices = configured.Where(item => string.Equals(
                    item.IOType,
                    "通用输入",
                    StringComparison.Ordinal));
            }
            else if (converterType == typeof(IoOutItem))
            {
                title = "输出 IO";
                choices = configured.Where(item => string.Equals(
                    item.IOType,
                    "通用输出",
                    StringComparison.Ordinal));
            }
            else
            {
                return Array.Empty<PickerColumn>();
            }

            return new[] { CreateIoColumn(title, choices) };
        }

        private static PickerColumn CreateIoColumn(string title, IEnumerable<IO> source)
        {
            List<PickerChoice> choices = source.Select(item => new PickerChoice(
                    item.Name,
                    item.Name,
                    $"卡{item.CardNum} · {item.IOIndex}"))
                .ToList();
            return new PickerColumn(
                title,
                InspectorIconKind.InputOutput,
                false,
                EnsureChoices(choices, "暂无 IO"));
        }

        private static IReadOnlyList<PickerColumn> BuildPoints(
            object owner,
            PropertyDescriptor property)
        {
            string stationName = GetStationName(owner);
            DataStation station = SF.frmCard?.dataStation?.FirstOrDefault(item =>
                item != null && string.Equals(item.Name, stationName, StringComparison.Ordinal));
            var columns = new List<PickerColumn>();
            if (property?.Converter?.GetType() == typeof(StationPosWithSpecial))
            {
                columns.Add(new PickerColumn(
                    "快捷项",
                    InspectorIconKind.Motion,
                    false,
                    new List<PickerChoice>
                    {
                        new PickerChoice("当前位置", "当前位置", string.Empty),
                        new PickerChoice("自定义坐标", "自定义坐标", string.Empty)
                    }));
            }
            List<PickerChoice> points = station?.ListDataPos?
                .Where(point => point != null && !string.IsNullOrWhiteSpace(point.Name))
                .OrderBy(point => point.Index)
                .Select(point => new PickerChoice(
                    point.Name,
                    point.Name,
                    $"点位 {point.Index}"))
                .ToList() ?? new List<PickerChoice>();
            string title = string.IsNullOrWhiteSpace(stationName)
                ? "点位"
                : stationName;
            string emptyText = string.IsNullOrWhiteSpace(stationName)
                ? "请先选择工站"
                : "暂无点位";
            columns.Add(new PickerColumn(
                title,
                InspectorIconKind.Motion,
                true,
                EnsureChoices(points, emptyText)));
            return columns;
        }

        private static IReadOnlyList<PickerColumn> BuildAddresses()
        {
            int procIndex = SF.frmProc?.SelectedProcNum ?? -1;
            int currentStepIndex = SF.frmProc?.SelectedStepNum ?? -1;
            Proc process = SF.frmProc?.procsList != null
                && procIndex >= 0 && procIndex < SF.frmProc.procsList.Count
                ? SF.frmProc.procsList[procIndex]
                : null;
            if (process?.steps == null)
            {
                return new[]
                {
                    new PickerColumn(
                        "地址",
                        InspectorIconKind.Process,
                        false,
                        EnsureChoices(new List<PickerChoice>(), "暂无流程地址"))
                };
            }
            var columns = new List<PickerColumn>();
            for (int stepIndex = 0; stepIndex < process.steps.Count; stepIndex++)
            {
                Step step = process.steps[stepIndex];
                var choices = new List<PickerChoice>();
                if (step?.Ops != null)
                {
                    for (int operationIndex = 0; operationIndex < step.Ops.Count; operationIndex++)
                    {
                        OperationType operation = step.Ops[operationIndex];
                        string address = $"{procIndex}-{stepIndex}-{operationIndex}";
                        string name = operation?.OperaType ?? "指令";
                        if (!string.IsNullOrWhiteSpace(operation?.Name)
                            && !string.Equals(operation.Name, name, StringComparison.Ordinal))
                        {
                            name += " · " + operation.Name;
                        }
                        choices.Add(new PickerChoice(
                            address,
                            $"{operationIndex + 1}. {name}",
                            address));
                    }
                }
                string stepName = string.IsNullOrWhiteSpace(step?.Name)
                    ? $"步骤 {stepIndex + 1}"
                    : $"{stepIndex + 1}. {step.Name}";
                columns.Add(new PickerColumn(
                    stepName,
                    InspectorIconKind.Step,
                    stepIndex == currentStepIndex,
                    EnsureChoices(choices, "暂无指令")));
            }
            return columns;
        }

        private static List<PickerChoice> EnsureChoices(
            List<PickerChoice> choices,
            string emptyText)
        {
            if (choices.Count == 0)
            {
                choices.Add(new PickerChoice(null, emptyText, string.Empty, false));
            }
            return choices;
        }

        private static Guid GetCurrentProcessId()
        {
            int procIndex = SF.frmProc?.SelectedProcNum ?? -1;
            if (SF.frmProc?.procsList == null
                || procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                return Guid.Empty;
            }
            return SF.frmProc.procsList[procIndex]?.head?.Id ?? Guid.Empty;
        }

        private static string GetStationName(object owner)
        {
            string stationName = ReadStationName(owner);
            if (!string.IsNullOrWhiteSpace(stationName))
            {
                return stationName;
            }
            return ReadStationName(SF.frmDataGrid?.OperationTemp);
        }

        private static string ReadStationName(object instance)
        {
            if (instance == null)
            {
                return null;
            }
            PropertyDescriptor property = TypeDescriptor.GetProperties(instance)["StationName"];
            return property?.GetValue(instance) as string;
        }

        private static string GetVariableTypeName(string type)
        {
            return string.Equals(type, "double", StringComparison.OrdinalIgnoreCase)
                ? "数值"
                : "文本";
        }
    }

    internal sealed class PickerColumn
    {
        public PickerColumn(
            string title,
            InspectorIconKind icon,
            bool highlighted,
            List<PickerChoice> choices)
        {
            Title = title;
            Icon = icon;
            Highlighted = highlighted;
            Choices = choices;
        }

        public string Title { get; }
        public InspectorIconKind Icon { get; }
        public bool Highlighted { get; }
        public List<PickerChoice> Choices { get; }
    }

    internal sealed class PickerChoice
    {
        public PickerChoice(
            string value,
            string primary,
            string secondary,
            bool selectable = true)
        {
            Value = value;
            Primary = primary;
            Secondary = secondary;
            Selectable = selectable;
        }

        public string Value { get; }
        public string Primary { get; }
        public string Secondary { get; }
        public bool Selectable { get; }

        public override string ToString()
        {
            return Primary;
        }
    }

    internal static class InspectorSelectionPickerDropDown
    {
        public static ToolStripDropDown Show(
            Control anchor,
            InspectorSelectionPickerKind kind,
            object owner,
            PropertyDescriptor property,
            string currentValue,
            Action<string> selected,
            Action closed)
        {
            Rectangle anchorBounds = anchor.RectangleToScreen(anchor.ClientRectangle);
            Rectangle workingArea = Screen.FromRectangle(anchorBounds).WorkingArea;
            var panel = new InspectorSelectionPickerPanel(
                kind,
                owner,
                property,
                currentValue);
            int width = Math.Min(
                panel.PreferredPickerWidth,
                Math.Max(300, workingArea.Width - 24));
            int height = Math.Min(
                panel.PreferredPickerHeight,
                Math.Max(220, workingArea.Height - 24));
            panel.Size = new Size(width, height);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = panel.Size
            };
            var dropDown = new ToolStripDropDown
            {
                AutoClose = true,
                AutoSize = false,
                BackColor = UiPalette.Surface,
                DropShadowEnabled = true,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Renderer = new BorderlessDropDownRenderer(),
                Size = panel.Size
            };
            dropDown.Items.Add(host);
            panel.ValueSelected += value =>
            {
                selected?.Invoke(value);
                dropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
            };
            panel.CancelRequested += () =>
                dropDown.Close(ToolStripDropDownCloseReason.Keyboard);
            dropDown.Closed += (sender, args) =>
            {
                closed?.Invoke();
                if (!anchor.IsDisposed && anchor.IsHandleCreated)
                {
                    anchor.BeginInvoke(new Action(dropDown.Dispose));
                }
                else
                {
                    dropDown.Dispose();
                }
            };
            dropDown.Show(
                anchor,
                new Point(Math.Min(0, anchor.Width - width), anchor.Height));
            panel.FocusPicker();
            return dropDown;
        }
    }
}
