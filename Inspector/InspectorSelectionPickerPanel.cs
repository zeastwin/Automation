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
        private const int MaximumColumnCount = 3;
        private const int ColumnGap = 8;
        private const int GroupGap = 4;
        private const int BandGap = 8;
        private const int MaximumColumnHeight = 464;
        private const int MinimumGroupWidth = 168;
        private const int MaximumGroupWidth = 260;

        private readonly List<PickerGroupControl> groupControls
            = new List<PickerGroupControl>();
        private readonly List<Button> choiceButtons = new List<Button>();
        private PickerGroupControl highlightedGroup;
        private Button selectedButton;

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

            IReadOnlyList<PickerGroupDefinition> groups = InspectorSelectionPickerData.Build(
                kind,
                owner,
                property,
                currentValue);
            BuildGroups(groups, currentValue);
            SizeChanged += (sender, args) => RefreshPickerLayout();
        }

        public event Action<string> ValueSelected;
        public event Action CancelRequested;

        public int PreferredPickerWidth
        {
            get
            {
                int columnCount = GetPreferredColumnCount();
                int groupWidth = groupControls.Count == 0
                    ? MinimumGroupWidth
                    : groupControls.Max(group => group.PreferredContentWidth);
                groupWidth = Math.Min(
                    MaximumGroupWidth,
                    Math.Max(MinimumGroupWidth, groupWidth));
                return OuterPadding * 2
                    + groupWidth * columnCount
                    + ColumnGap * (columnCount - 1);
            }
        }

        public int ContentHeight { get; private set; }

        public void FocusPicker()
        {
            Button target = selectedButton
                ?? highlightedGroup?.FirstSelectableButton
                ?? choiceButtons.FirstOrDefault();
            target?.Focus();
            if (target != null)
            {
                ScrollControlIntoView(target);
            }
        }

        public void RefreshPickerLayout()
        {
            int requestedColumns = GetPreferredColumnCount();
            int scrollBarWidth = ContentHeight > ClientSize.Height
                ? SystemInformation.VerticalScrollBarWidth
                : 0;
            int availableWidth = Math.Max(
                MinimumGroupWidth,
                ClientSize.Width - OuterPadding * 2
                    - scrollBarWidth);
            int columnCount = Math.Min(
                requestedColumns,
                Math.Max(1, (availableWidth + ColumnGap)
                    / (MinimumGroupWidth + ColumnGap)));
            int groupWidth = Math.Max(
                80,
                (availableWidth - ColumnGap * (columnCount - 1)) / columnCount);
            int[] columnY = Enumerable.Repeat(OuterPadding, columnCount).ToArray();
            int bandTop = OuterPadding;
            int column = 0;
            foreach (PickerGroupControl groupControl in groupControls)
            {
                groupControl.RefreshPickerLayout(groupWidth);
                if (column < columnCount - 1
                    && columnY[column] > bandTop
                    && columnY[column] + groupControl.ContentHeight
                        > bandTop + MaximumColumnHeight)
                {
                    column++;
                }
                else if (column == columnCount - 1
                    && columnY[column] > bandTop
                    && columnY[column] + groupControl.ContentHeight
                        > bandTop + MaximumColumnHeight)
                {
                    bandTop += MaximumColumnHeight + BandGap;
                    column = 0;
                    for (int index = 0; index < columnY.Length; index++)
                    {
                        columnY[index] = bandTop;
                    }
                }
                groupControl.SetBounds(
                    OuterPadding + column * (groupWidth + ColumnGap),
                    columnY[column],
                    groupWidth,
                    groupControl.ContentHeight);
                columnY[column] += groupControl.ContentHeight + GroupGap;
            }
            ContentHeight = Math.Max(
                70,
                columnY.Max() - GroupGap + OuterPadding);
            AutoScrollMinSize = new Size(0, ContentHeight);
        }

        private int GetPreferredColumnCount()
        {
            if (groupControls.Count == 0)
            {
                return 1;
            }
            int columns = 1;
            int columnHeight = OuterPadding;
            foreach (PickerGroupControl group in groupControls)
            {
                int groupHeight = group.ContentHeight > 0
                    ? group.ContentHeight
                    : group.PreferredContentHeight;
                if (columnHeight > OuterPadding
                    && columnHeight + groupHeight > MaximumColumnHeight + OuterPadding)
                {
                    columns++;
                    columnHeight = OuterPadding;
                    if (columns >= MaximumColumnCount)
                    {
                        return MaximumColumnCount;
                    }
                }
                columnHeight += groupHeight + GroupGap;
            }
            return Math.Min(MaximumColumnCount, columns);
        }

        private void BuildGroups(
            IReadOnlyList<PickerGroupDefinition> groups,
            string currentValue)
        {
            SuspendLayout();
            try
            {
                foreach (PickerGroupDefinition sourceGroup in groups)
                {
                    int maximumChoices = PickerGroupControl.GetMaximumChoiceCount(
                        MaximumColumnHeight);
                    for (int offset = 0; offset < sourceGroup.Choices.Count; offset += maximumChoices)
                    {
                        PickerGroupDefinition group = sourceGroup.CreateChunk(
                            offset,
                            maximumChoices,
                            offset > 0);
                        var groupControl = new PickerGroupControl(group, currentValue);
                        groupControl.ValueSelected += value => ValueSelected?.Invoke(value);
                        groupControl.CancelRequested += () => CancelRequested?.Invoke();
                        groupControl.MoveFocusRequested += MoveFocus;
                        groupControls.Add(groupControl);
                        choiceButtons.AddRange(groupControl.SelectableButtons);
                        Controls.Add(groupControl);
                        if (group.Highlighted && highlightedGroup == null)
                        {
                            highlightedGroup = groupControl;
                        }
                        if (groupControl.SelectedButton != null)
                        {
                            selectedButton = groupControl.SelectedButton;
                        }
                    }
                }
            }
            finally
            {
                ResumeLayout(false);
            }
            RefreshPickerLayout();
        }

        private void MoveFocus(Button source, Keys key)
        {
            int index = choiceButtons.IndexOf(source);
            if (index < 0)
            {
                return;
            }
            int offset;
            switch (key)
            {
                case Keys.Left:
                case Keys.Up:
                    offset = -1;
                    break;
                case Keys.Right:
                case Keys.Down:
                    offset = 1;
                    break;
                default:
                    return;
            }
            int targetIndex = index + offset;
            if (targetIndex >= 0 && targetIndex < choiceButtons.Count)
            {
                Button target = choiceButtons[targetIndex];
                target.Focus();
                ScrollControlIntoView(target);
            }
        }

        private sealed class PickerGroupControl : UserControl
        {
            private const int RowGap = 1;
            private const int HeaderHeight = 21;
            private const int ItemHeight = 23;

            private readonly PickerGroupDefinition definition;
            private readonly Label header = new Label();
            private readonly List<Button> buttons = new List<Button>();

            public PickerGroupControl(PickerGroupDefinition definition, string currentValue)
            {
                this.definition = definition;
                BackColor = UiPalette.Surface;
                Padding = Padding.Empty;
                Margin = Padding.Empty;
                DoubleBuffered = true;

                header.AutoEllipsis = true;
                header.BackColor = definition.Highlighted
                    ? UiPalette.BrandSoftHover
                    : UiPalette.SurfaceSubtle;
                header.Font = InspectorFonts.Bold9;
                header.ForeColor = definition.Highlighted
                    ? UiPalette.Brand
                    : UiPalette.TextPrimary;
                header.Padding = new Padding(30, 0, 4, 0);
                header.Text = definition.Title;
                header.TextAlign = ContentAlignment.MiddleLeft;
                header.Paint += Header_Paint;
                Controls.Add(header);

                foreach (PickerChoice choice in definition.Choices)
                {
                    bool selected = choice.Selectable && string.Equals(
                        choice.Value,
                        currentValue,
                        StringComparison.Ordinal);
                    var button = new Button
                    {
                        AccessibleName = choice.Primary,
                        AccessibleDescription = choice.Secondary,
                        AutoEllipsis = true,
                        BackColor = selected
                            ? UiPalette.BrandSoftHover
                            : UiPalette.Surface,
                        Cursor = choice.Selectable ? Cursors.Hand : Cursors.Default,
                        Enabled = choice.Selectable,
                        FlatStyle = FlatStyle.Flat,
                        Font = InspectorFonts.Regular85,
                        ForeColor = selected
                            ? UiPalette.Brand
                            : choice.Selectable
                                ? UiPalette.TextPrimary
                                : UiPalette.TextDisabled,
                        TabStop = choice.Selectable,
                        Tag = choice,
                        Text = string.Empty,
                        UseVisualStyleBackColor = false
                    };
                    button.FlatAppearance.BorderSize = 0;
                    button.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoft;
                    button.FlatAppearance.MouseDownBackColor = UiPalette.BrandSoftHover;
                    button.Paint += ChoiceButton_Paint;
                    button.Click += ChoiceButton_Click;
                    button.KeyDown += ChoiceButton_KeyDown;
                    button.PreviewKeyDown += ChoiceButton_PreviewKeyDown;
                    buttons.Add(button);
                    Controls.Add(button);
                    if (selected)
                    {
                        SelectedButton = button;
                    }
                }
            }

            public event Action<string> ValueSelected;
            public event Action CancelRequested;
            public event Action<Button, Keys> MoveFocusRequested;

            public int ContentHeight { get; private set; }
            public int PreferredContentHeight => HeaderHeight + 1
                + Math.Max(1, buttons.Count) * (ItemHeight + RowGap);

            public int PreferredContentWidth
            {
                get
                {
                    int width = MeasureTextWidth(definition.Title, header.Font) + 40;
                    foreach (Button button in buttons)
                    {
                        if (button.Tag is PickerChoice choice)
                        {
                            width = Math.Max(
                                width,
                                MeasureTextWidth(choice.Primary, button.Font) + 18);
                        }
                    }
                    return width;
                }
            }

            public Button SelectedButton { get; }
            public Button FirstSelectableButton => SelectableButtons.FirstOrDefault();
            public IEnumerable<Button> SelectableButtons => buttons.Where(button => button.Enabled);

            public static int GetMaximumChoiceCount(int availableHeight)
            {
                return Math.Max(
                    1,
                    (availableHeight - HeaderHeight - 1) / (ItemHeight + RowGap));
            }

            public void RefreshPickerLayout(int width)
            {
                int innerWidth = Math.Max(1, width);
                header.SetBounds(0, 0, innerWidth, HeaderHeight);
                for (int index = 0; index < buttons.Count; index++)
                {
                    buttons[index].SetBounds(
                        0,
                        HeaderHeight + 1 + index * (ItemHeight + RowGap),
                        innerWidth,
                        ItemHeight);
                }
                ContentHeight = HeaderHeight + 1
                    + Math.Max(1, buttons.Count) * (ItemHeight + RowGap);
            }

            private static int MeasureTextWidth(string text, Font font)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return 0;
                }
                return TextRenderer.MeasureText(
                    text,
                    font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            }

            private void Header_Paint(object sender, PaintEventArgs e)
            {
                InspectorIcons.Draw(
                    e.Graphics,
                    new Rectangle(9, 3, 15, 15),
                    definition.Icon,
                    definition.Highlighted
                        ? UiPalette.Brand
                        : UiPalette.TextSecondary);
            }

            private static void ChoiceButton_Paint(object sender, PaintEventArgs e)
            {
                var button = sender as Button;
                var choice = button?.Tag as PickerChoice;
                if (button == null || choice == null)
                {
                    return;
                }
                TextRenderer.DrawText(
                    e.Graphics,
                    choice.Primary,
                    button.Font,
                    new Rectangle(
                        8,
                        0,
                        Math.Max(1, button.ClientSize.Width - 14),
                        button.ClientSize.Height),
                    button.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPadding);
                using (var pen = new Pen(UiPalette.Divider))
                {
                    e.Graphics.DrawLine(
                        pen,
                        0,
                        button.ClientSize.Height - 1,
                        button.ClientSize.Width,
                        button.ClientSize.Height - 1);
                }
            }

            private void ChoiceButton_Click(object sender, EventArgs e)
            {
                if (sender is Button button
                    && button.Tag is PickerChoice choice
                    && choice.Selectable)
                {
                    ValueSelected?.Invoke(choice.Value);
                }
            }

            private void ChoiceButton_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    CancelRequested?.Invoke();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right
                    || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    MoveFocusRequested?.Invoke((Button)sender, e.KeyCode);
                    e.Handled = true;
                }
            }

            private static void ChoiceButton_PreviewKeyDown(
                object sender,
                PreviewKeyDownEventArgs e)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Left
                    || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up
                    || e.KeyCode == Keys.Down)
                {
                    e.IsInputKey = true;
                }
            }
        }
    }

    internal static class InspectorSelectionPickerData
    {
        public static IReadOnlyList<PickerGroupDefinition> Build(
            InspectorSelectionPickerKind kind,
            object owner,
            PropertyDescriptor property,
            string currentValue)
        {
            PlatformRuntime runtime = EditorServiceRegistry.GetRuntime(owner);
            switch (kind)
            {
                case InspectorSelectionPickerKind.Variable:
                    return BuildVariables(runtime);
                case InspectorSelectionPickerKind.InputOutput:
                    return BuildInputOutput(runtime, property);
                case InspectorSelectionPickerKind.Point:
                    return BuildPoints(runtime, owner, property);
                case InspectorSelectionPickerKind.Address:
                    return BuildAddresses(runtime);
                default:
                    return Array.Empty<PickerGroupDefinition>();
            }
        }

        private static IReadOnlyList<PickerGroupDefinition> BuildVariables(PlatformRuntime runtime)
        {
            List<DicValue> values = runtime?.Stores.Values.GetValuesSnapshot()
                ?? new List<DicValue>();
            Guid currentProcId = GetCurrentProcessId(runtime);
            return new[]
            {
                CreateVariableGroup(
                    "系统变量",
                    values.Where(value => string.Equals(
                        value.Scope,
                        VariableScopeContract.System,
                        StringComparison.Ordinal))),
                CreateVariableGroup(
                    "公共变量",
                    values.Where(value => string.Equals(
                        value.Scope,
                        VariableScopeContract.Public,
                        StringComparison.Ordinal))),
                CreateVariableGroup(
                    "当前流程私有变量",
                    values.Where(value => string.Equals(
                            value.Scope,
                            VariableScopeContract.Process,
                            StringComparison.Ordinal)
                        && currentProcId != Guid.Empty
                        && value.OwnerProcId == currentProcId))
            };
        }

        private static PickerGroupDefinition CreateVariableGroup(
            string title,
            IEnumerable<DicValue> source)
        {
            List<PickerChoice> choices = source
                .OrderBy(value => value.Index)
                .Select(value => new PickerChoice(
                    value.Name,
                    value.Name,
                    string.Empty))
                .ToList();
            return new PickerGroupDefinition(
                title,
                InspectorIconKind.Data,
                false,
                EnsureChoices(choices, "暂无变量"));
        }

        private static IReadOnlyList<PickerGroupDefinition> BuildInputOutput(
            PlatformRuntime runtime,
            PropertyDescriptor property)
        {
            Type converterType = property?.Converter?.GetType();
            List<IO> configured = runtime?.Stores.IoConfiguration.ByName.Values
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
                return Array.Empty<PickerGroupDefinition>();
            }

            return new[] { CreateIoGroup(title, choices) };
        }

        private static PickerGroupDefinition CreateIoGroup(
            string title,
            IEnumerable<IO> source)
        {
            List<PickerChoice> choices = source.Select(item => new PickerChoice(
                    item.Name,
                    item.Name,
                    string.Empty))
                .ToList();
            return new PickerGroupDefinition(
                title,
                InspectorIconKind.InputOutput,
                false,
                EnsureChoices(choices, "暂无 IO"));
        }

        private static IReadOnlyList<PickerGroupDefinition> BuildPoints(
            PlatformRuntime runtime,
            object owner,
            PropertyDescriptor property)
        {
            string stationName = GetStationName(runtime, owner);
            DataStation station = runtime?.Stores.Stations.Items.FirstOrDefault(item =>
                item != null && string.Equals(item.Name, stationName, StringComparison.Ordinal));
            var groups = new List<PickerGroupDefinition>();
            if (property?.Converter?.GetType() == typeof(StationPosWithSpecial))
            {
                groups.Add(new PickerGroupDefinition(
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
            groups.Add(new PickerGroupDefinition(
                title,
                InspectorIconKind.Motion,
                true,
                EnsureChoices(points, emptyText)));
            return groups;
        }

        private static IReadOnlyList<PickerGroupDefinition> BuildAddresses(PlatformRuntime runtime)
        {
            PlatformEditorSelection selection = runtime?.EditorUi?.GetSelection();
            int procIndex = selection?.ProcIndex ?? -1;
            int currentStepIndex = selection?.StepIndex ?? -1;
            Proc process = runtime?.Stores.Processes.Items != null
                && procIndex >= 0 && procIndex < runtime.Stores.Processes.Items.Count
                ? runtime.Stores.Processes.Items[procIndex]
                : null;
            if (process?.steps == null)
            {
                return new[]
                {
                    new PickerGroupDefinition(
                        "地址",
                        InspectorIconKind.Process,
                        false,
                        EnsureChoices(new List<PickerChoice>(), "暂无流程地址"))
                };
            }
            var groups = new List<PickerGroupDefinition>();
            for (int stepIndex = 0; stepIndex < process.steps.Count; stepIndex++)
            {
                Step step = process.steps[stepIndex];
                var choices = new List<PickerChoice>();
                if (step?.Ops != null)
                {
                    for (int operationIndex = 0; operationIndex < step.Ops.Count; operationIndex++)
                    {
                        string address = $"{procIndex}-{stepIndex}-{operationIndex}";
                        choices.Add(new PickerChoice(
                            address,
                            address,
                            string.Empty));
                    }
                }
                string stepName = string.IsNullOrWhiteSpace(step?.Name)
                    ? $"步骤 {stepIndex + 1}"
                    : $"{stepIndex + 1}. {step.Name}";
                groups.Add(new PickerGroupDefinition(
                    stepName,
                    InspectorIconKind.Step,
                    stepIndex == currentStepIndex,
                    EnsureChoices(choices, "暂无指令")));
            }
            return groups;
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

        private static Guid GetCurrentProcessId(PlatformRuntime runtime)
        {
            int procIndex = runtime?.EditorUi?.GetSelection()?.ProcIndex ?? -1;
            if (runtime?.Stores.Processes.Items == null
                || procIndex < 0 || procIndex >= runtime.Stores.Processes.Items.Count)
            {
                return Guid.Empty;
            }
            return runtime.Stores.Processes.Items[procIndex]?.head?.Id ?? Guid.Empty;
        }

        private static string GetStationName(PlatformRuntime runtime, object owner)
        {
            string stationName = ReadStationName(owner);
            if (!string.IsNullOrWhiteSpace(stationName))
            {
                return stationName;
            }
            return ReadStationName(runtime?.EditorUi?.CurrentOperationContext);
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

    }

    internal sealed class PickerGroupDefinition
    {
        public PickerGroupDefinition(
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

        public PickerGroupDefinition CreateChunk(
            int offset,
            int count,
            bool continuation)
        {
            return new PickerGroupDefinition(
                continuation ? Title + "（续）" : Title,
                Icon,
                Highlighted,
                Choices.Skip(offset).Take(count).ToList());
        }
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
        private const int MinimumPickerWidth = 180;
        private const int MaximumPickerWidth = 760;
        private const int WorkingAreaMargin = 24;

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
            int maximumWidth = Math.Max(
                MinimumPickerWidth,
                Math.Min(MaximumPickerWidth, workingArea.Width - WorkingAreaMargin));
            int width = Math.Min(
                maximumWidth,
                Math.Max(
                    MinimumPickerWidth,
                    Math.Max(anchor.Width, panel.PreferredPickerWidth)));
            panel.Size = new Size(width, 80);
            panel.RefreshPickerLayout();
            int maximumHeight = Math.Min(
                480,
                Math.Max(160, workingArea.Height - WorkingAreaMargin));
            int height = Math.Min(panel.ContentHeight, maximumHeight);
            panel.Size = new Size(width, Math.Max(70, height));
            panel.RefreshPickerLayout();

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = panel.Size
            };
            var dropDown = new InstantToolStripDropDown
            {
                AutoClose = true,
                AutoSize = false,
                BackColor = UiPalette.Surface,
                DropShadowEnabled = false,
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
            dropDown.ShowInstant(
                anchor,
                new Point(Math.Min(0, anchor.Width - width), anchor.Height),
                panel);
            panel.FocusPicker();
            return dropDown;
        }
    }
}
