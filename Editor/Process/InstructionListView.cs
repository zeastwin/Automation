// 模块：编辑器 / 流程。
// 职责范围：流程树、指令表、对象选择、搜索和导航。

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    /// <summary>
    /// 指令列表的轻量虚拟视图。仅创建可见项，整行由本控件绘制，
    /// 避免 DataGridView 的单元格边框、焦点框和大量样式对象。
    /// </summary>
    public sealed class InstructionListView : ListView
    {
        private const int FlowColumnWidth = 112;
        private const int JumpLaneSpacing = 9;
        private const int JumpVisibleLaneCount = 3;
        private const int LvmSetExtendedListViewStyle = 0x1036;
        private const int LvsExDoubleBuffer = 0x00010000;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

        private readonly ImageList rowHeightImages;
        private readonly Bitmap rowHeightBitmap;
        private readonly Font contentFont;
        private readonly Font headerFont;
        private readonly Timer jumpFlowTimer;
        private readonly Dictionary<int, Color> rowBackColors = new Dictionary<int, Color>();
        private Color allRowsBackColor = Color.Empty;
        private BindingSource bindingSource;
        private IList operationSource;
        private bool noteColumnVisible = true;
        private int runtimeIndex = -1;
        private ProcRunState runtimeState = ProcRunState.Ready;
        private bool runtimeBreakpoint;
        private readonly List<JumpLink> jumpLinks = new List<JumpLink>();
        private Proc flowProc;
        private int flowProcIndex = -1;
        private int flowStepIndex = -1;
        private int linkedNavigationDepth;
        private int selectionUpdateDepth;
        private bool selectionChangedPending;
        private float jumpFlowPhase;

        public event EventHandler<JumpLinkClickedEventArgs> JumpLinkClicked;

        private enum JumpKind
        {
            Automatic,
            Confirm,
            Reject,
            Cancel,
            True,
            False,
            Default,
            Success,
            Failure,
            Match,
            Generic
        }

        private sealed class GotoTargetRecord
        {
            public string Address { get; set; }
            public JumpKind Kind { get; set; }
        }

        private sealed class JumpLink
        {
            public int SourceStepIndex { get; set; }
            public int SourceOpIndex { get; set; }
            public int TargetStepIndex { get; set; }
            public int TargetOpIndex { get; set; }
            public int CrossId { get; set; }
            public List<JumpKind> Kinds { get; set; } = new List<JumpKind>();

            public bool IsCrossStep => SourceStepIndex != TargetStepIndex;
        }

        public sealed class JumpLinkClickedEventArgs : EventArgs
        {
            public JumpLinkClickedEventArgs(
                int procIndex,
                int sourceStepIndex,
                int sourceOpIndex,
                int targetStepIndex,
                int targetOpIndex,
                bool isOutgoing)
            {
                ProcIndex = procIndex;
                SourceStepIndex = sourceStepIndex;
                SourceOpIndex = sourceOpIndex;
                TargetStepIndex = targetStepIndex;
                TargetOpIndex = targetOpIndex;
                IsOutgoing = isOutgoing;
            }

            public int ProcIndex { get; }
            public int SourceStepIndex { get; }
            public int SourceOpIndex { get; }
            public int TargetStepIndex { get; }
            public int TargetOpIndex { get; }
            public bool IsOutgoing { get; }
        }

        private enum VisualState
        {
            None,
            Running,
            Paused,
            SingleStep,
            Breakpoint,
            Alarming,
            Stopping,
            Disabled
        }

        public InstructionListView()
        {
            View = View.Details;
            VirtualMode = true;
            OwnerDraw = true;
            FullRowSelect = true;
            MultiSelect = true;
            HideSelection = false;
            ShowItemToolTips = false;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            GridLines = false;
            BorderStyle = BorderStyle.None;
            AllowDrop = true;
            contentFont = ProcessPageFont.Create(10F, FontStyle.Regular);
            headerFont = ProcessPageFont.Create(9.5F, FontStyle.Bold);
            Font = contentFont;
            BackColor = UiPalette.Background;
            ForeColor = UiPalette.TextPrimary;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            base.Columns.Add(string.Empty, FlowColumnWidth, HorizontalAlignment.Center);
            base.Columns.Add("编号", 68, HorizontalAlignment.Center);
            base.Columns.Add("名称", 220, HorizontalAlignment.Left);
            base.Columns.Add("操作类型", 180, HorizontalAlignment.Center);
            base.Columns.Add("备注", 260, HorizontalAlignment.Left);

            rowHeightImages = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(1, 34),
                TransparentColor = Color.Transparent
            };
            rowHeightBitmap = new Bitmap(1, 34);
            rowHeightImages.Images.Add(rowHeightBitmap);
            SmallImageList = rowHeightImages;

            RetrieveVirtualItem += InstructionListView_RetrieveVirtualItem;
            DrawColumnHeader += InstructionListView_DrawColumnHeader;
            DrawItem += InstructionListView_DrawItem;
            DrawSubItem += InstructionListView_DrawSubItem;
            Resize += (sender, args) => RecalculateColumnWidths();

            jumpFlowTimer = new Timer { Interval = 55 };
            jumpFlowTimer.Tick += JumpFlowTimer_Tick;
            jumpFlowTimer.Start();

            // 状态图标保持静止，仅同步骤跳转虚线展示流动方向。
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SendMessage(
                Handle,
                LvmSetExtendedListViewStyle,
                new IntPtr(LvsExDoubleBuffer),
                new IntPtr(LvsExDoubleBuffer));
        }

        [Browsable(false)]
        public object DataSource
        {
            get => bindingSource ?? (object)operationSource;
            set
            {
                BindingSource nextBindingSource = value as BindingSource;
                IList nextOperationSource = nextBindingSource?.List ?? value as IList;
                bool bindingChanged = !ReferenceEquals(nextBindingSource, bindingSource);
                bool operationSourceChanged = !ReferenceEquals(nextOperationSource, operationSource);
                if (!bindingChanged && !operationSourceChanged)
                {
                    return;
                }
                ClearSelection();
                DetachBindingSource();
                rowBackColors.Clear();
                allRowsBackColor = Color.Empty;
                runtimeIndex = -1;
                runtimeState = ProcRunState.Ready;
                runtimeBreakpoint = false;
                bindingSource = nextBindingSource;
                if (bindingSource != null)
                {
                    bindingSource.ListChanged += BindingSource_ListChanged;
                }
                operationSource = nextOperationSource;
                RefreshOperations(true);
            }
        }

        [Browsable(false)]
        public int OperationCount => operationSource?.Count ?? 0;

        public void SetFlowContext(int procIndex, int stepIndex, Proc proc)
        {
            flowProcIndex = procIndex;
            flowStepIndex = stepIndex;
            flowProc = proc;
            RebuildJumpLinks();
            if (linkedNavigationDepth == 0)
            {
                Invalidate();
            }
        }

        internal void BeginLinkedNavigation()
        {
            linkedNavigationDepth++;
            BeginUpdate();
        }

        internal void EndLinkedNavigation()
        {
            if (linkedNavigationDepth <= 0)
            {
                return;
            }
            try
            {
                EndUpdate();
            }
            finally
            {
                linkedNavigationDepth--;
                if (linkedNavigationDepth == 0)
                {
                    Invalidate();
                }
            }
        }

        public OperationType GetOperation(int index)
        {
            return index >= 0 && index < OperationCount ? operationSource[index] as OperationType : null;
        }

        public IReadOnlyList<int> GetSelectedIndexes()
        {
            return SelectedIndices.Cast<int>().OrderBy(index => index).ToArray();
        }

        [Browsable(false)]
        public int CurrentIndex => FocusedItem?.Index
            ?? SelectedIndices.Cast<int>().DefaultIfEmpty(-1).First();

        [Browsable(false)]
        public int FirstVisibleIndex
        {
            get
            {
                if (!IsHandleCreated || OperationCount <= 0)
                {
                    return -1;
                }
                try
                {
                    return TopItem?.Index ?? -1;
                }
                catch (InvalidOperationException)
                {
                    return -1;
                }
                catch (NullReferenceException)
                {
                    return -1;
                }
            }
        }

        public void SetFirstVisibleIndex(int index)
        {
            if (!IsHandleCreated || OperationCount <= 0)
            {
                return;
            }
            int visibleRowCount = Math.Max(
                1,
                (ClientSize.Height - 28) / Math.Max(1, rowHeightImages.ImageSize.Height));
            int maxTopIndex = Math.Max(0, OperationCount - visibleRowCount);
            int targetIndex = Math.Max(0, Math.Min(index, maxTopIndex));
            try
            {
                TopItem = Items[targetIndex];
            }
            catch (InvalidOperationException)
            {
                EnsureVisible(targetIndex);
            }
            catch (NullReferenceException)
            {
                EnsureVisible(targetIndex);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        public void ClearSelection()
        {
            int[] selectedIndexes = SelectedIndices.Cast<int>().ToArray();
            int focusedIndex = FocusedItem?.Index ?? -1;
            if (selectedIndexes.Length == 0 && focusedIndex < 0)
            {
                return;
            }

            selectionUpdateDepth++;
            try
            {
                foreach (int index in selectedIndexes)
                {
                    Items[index].Selected = false;
                }
                if (FocusedItem != null)
                {
                    FocusedItem.Focused = false;
                }
            }
            finally
            {
                EndSelectionUpdate();
            }

            if (linkedNavigationDepth == 0)
            {
                foreach (int index in selectedIndexes.Append(focusedIndex).Where(index => index >= 0).Distinct())
                {
                    InvalidateRow(index);
                }
            }
        }

        public void SelectSingle(int index)
        {
            if (index < 0 || index >= OperationCount)
            {
                ClearSelection();
                return;
            }

            int[] previousIndexes = SelectedIndices.Cast<int>().ToArray();
            int previousFocusedIndex = FocusedItem?.Index ?? -1;
            if (previousIndexes.Length == 1
                && previousIndexes[0] == index
                && previousFocusedIndex == index)
            {
                return;
            }

            selectionUpdateDepth++;
            try
            {
                foreach (int selectedIndex in previousIndexes)
                {
                    if (selectedIndex != index)
                    {
                        Items[selectedIndex].Selected = false;
                    }
                }
                Items[index].Selected = true;
                Items[index].Focused = true;
            }
            finally
            {
                EndSelectionUpdate();
            }

            if (linkedNavigationDepth == 0)
            {
                foreach (int changedIndex in previousIndexes
                    .Append(previousFocusedIndex)
                    .Append(index)
                    .Where(changedIndex => changedIndex >= 0)
                    .Distinct())
                {
                    InvalidateRow(changedIndex);
                }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left
                && TryGetCrossStepJump(e.Location, out JumpLink link, out bool isOutgoing))
            {
                JumpLinkClicked?.Invoke(
                    this,
                    new JumpLinkClickedEventArgs(
                        flowProcIndex,
                        link.SourceStepIndex,
                        link.SourceOpIndex,
                        link.TargetStepIndex,
                        link.TargetOpIndex,
                        isOutgoing));
            }
            base.OnMouseClick(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor nextCursor = TryGetCrossStepJump(e.Location, out _, out _)
                ? Cursors.Hand
                : Cursors.Default;
            if (!ReferenceEquals(Cursor, nextCursor))
            {
                Cursor = nextCursor;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!ReferenceEquals(Cursor, Cursors.Default))
            {
                Cursor = Cursors.Default;
            }
            base.OnMouseLeave(e);
        }

        public int IndexFromPoint(Point point)
        {
            return GetItemAt(point.X, point.Y)?.Index ?? -1;
        }

        public void EnsureIndexVisible(int index)
        {
            if (index < 0 || index >= OperationCount)
            {
                return;
            }
            EnsureVisible(index);
        }

        public bool IsIndexVisible(int index)
        {
            if (index < 0 || index >= OperationCount || !IsHandleCreated)
            {
                return false;
            }
            try
            {
                Rectangle bounds = GetItemRect(index);
                return ClientRectangle.IntersectsWith(bounds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        public void SetRuntimeState(int index, ProcRunState state, bool isBreakpoint)
        {
            int previous = runtimeIndex;
            runtimeIndex = index >= 0 && index < OperationCount ? index : -1;
            runtimeState = state;
            runtimeBreakpoint = isBreakpoint;
            InvalidateRow(previous);
            InvalidateRow(runtimeIndex);
        }

        public void ClearRuntimeState()
        {
            int previous = runtimeIndex;
            runtimeIndex = -1;
            runtimeState = ProcRunState.Ready;
            runtimeBreakpoint = false;
            InvalidateRow(previous);
        }

        public void SetRowColor(int index, Color color)
        {
            if (index < 0 || index >= OperationCount)
            {
                return;
            }
            if (color.IsEmpty)
            {
                rowBackColors.Remove(index);
            }
            else
            {
                rowBackColors[index] = color;
            }
            InvalidateRow(index);
        }

        public void ClearRowColors()
        {
            if (rowBackColors.Count == 0 && allRowsBackColor.IsEmpty)
            {
                return;
            }
            rowBackColors.Clear();
            allRowsBackColor = Color.Empty;
            Invalidate();
        }

        public void SetAllRowsColor(Color color)
        {
            allRowsBackColor = color;
            Invalidate();
        }

        public void InvalidateRow(int index)
        {
            if (index < 0 || index >= OperationCount || !IsHandleCreated)
            {
                return;
            }
            try
            {
                Invalidate(GetItemRect(index));
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        public void SetNoteColumnVisible(bool visible)
        {
            if (noteColumnVisible == visible)
            {
                return;
            }
            noteColumnVisible = visible;
            RecalculateColumnWidths();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachBindingSource();
                jumpFlowTimer.Stop();
                jumpFlowTimer.Tick -= JumpFlowTimer_Tick;
                jumpFlowTimer.Dispose();
                rowHeightImages.Dispose();
                headerFont.Dispose();
                contentFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            if (selectionUpdateDepth > 0)
            {
                selectionChangedPending = true;
                return;
            }
            base.OnSelectedIndexChanged(e);
            InvalidateFlowColumn();
        }

        private void EndSelectionUpdate()
        {
            selectionUpdateDepth--;
            if (selectionUpdateDepth == 0 && selectionChangedPending)
            {
                selectionChangedPending = false;
                base.OnSelectedIndexChanged(EventArgs.Empty);
                InvalidateFlowColumn();
            }
        }

        private void InvalidateFlowColumn()
        {
            if (linkedNavigationDepth == 0 && base.Columns.Count > 0)
            {
                Invalidate(new Rectangle(0, 0, base.Columns[0].Width, ClientSize.Height));
            }
        }

        private void BindingSource_ListChanged(object sender, ListChangedEventArgs e)
        {
            IList currentOperationSource = bindingSource?.List;
            bool operationSourceChanged = !ReferenceEquals(currentOperationSource, operationSource);
            operationSource = currentOperationSource;
            RefreshOperations(operationSourceChanged);
        }

        private void DetachBindingSource()
        {
            if (bindingSource != null)
            {
                bindingSource.ListChanged -= BindingSource_ListChanged;
                bindingSource = null;
            }
        }

        private void RefreshOperations(bool resetViewport)
        {
            int selectedIndex = resetViewport ? -1 : CurrentIndex;
            int previousFirstVisibleIndex = FirstVisibleIndex;
            int firstVisibleIndex = resetViewport ? 0 : previousFirstVisibleIndex;
            int previousVirtualListSize = VirtualListSize;
            BeginUpdate();
            try
            {
                if (resetViewport)
                {
                    ClearSelection();
                }
                VirtualListSize = OperationCount;
                RebuildJumpLinks();
                RecalculateColumnWidths();
                rowBackColors.Keys.Where(index => index >= OperationCount).ToList()
                    .ForEach(index => rowBackColors.Remove(index));
                if (runtimeIndex >= OperationCount)
                {
                    ClearRuntimeState();
                }
            }
            finally
            {
                EndUpdate();
            }
            int visibleRowCount = Math.Max(
                1,
                (ClientSize.Height - 28) / Math.Max(1, rowHeightImages.ImageSize.Height));
            int maxTopIndex = Math.Max(0, OperationCount - visibleRowCount);
            bool nativeViewportOutOfRange = previousFirstVisibleIndex > maxTopIndex;
            if (linkedNavigationDepth == 0
                && previousVirtualListSize != OperationCount
                && nativeViewportOutOfRange
                && OperationCount > 0
                && IsHandleCreated)
            {
                // WinForms 的虚拟 ListView 在同一控件上换成更短的列表后，
                // 原生滚动原点可能仍停留在旧列表，导致首行悬在中部或部分行不绘制。
                // 仅当旧滚动原点超出新列表可用范围时重建句柄，普通步骤切换直接复用现有句柄。
                RecreateHandle();
            }
            if (OperationCount > 0)
            {
                SetFirstVisibleIndex(firstVisibleIndex < 0 ? 0 : firstVisibleIndex);
            }
            if (selectedIndex >= 0 && selectedIndex < OperationCount)
            {
                SelectSingle(selectedIndex);
            }
            else if (resetViewport || previousVirtualListSize != OperationCount)
            {
                ClearSelection();
            }
            if (linkedNavigationDepth == 0)
            {
                Invalidate();
            }
        }

        private void RecalculateColumnWidths()
        {
            if (base.Columns.Count < 5)
            {
                return;
            }
            int verticalScrollWidth = OperationCount * rowHeightImages.ImageSize.Height > ClientSize.Height - 28
                ? SystemInformation.VerticalScrollBarWidth
                : 0;
            int available = Math.Max(240, ClientSize.Width - FlowColumnWidth - 68 - verticalScrollWidth - 2);
            base.Columns[0].Width = FlowColumnWidth;
            base.Columns[1].Width = 68;
            if (noteColumnVisible)
            {
                base.Columns[2].Width = (int)(available * 0.32F);
                base.Columns[3].Width = (int)(available * 0.28F);
                base.Columns[4].Width = Math.Max(0, available - base.Columns[2].Width - base.Columns[3].Width);
            }
            else
            {
                base.Columns[2].Width = (int)(available * 0.54F);
                base.Columns[3].Width = Math.Max(0, available - base.Columns[2].Width);
                base.Columns[4].Width = 0;
            }
        }

        private void RebuildJumpLinks()
        {
            jumpLinks.Clear();
            if (flowProcIndex < 0 || flowStepIndex < 0 || flowProc?.steps == null
                || flowStepIndex >= flowProc.steps.Count)
            {
                return;
            }

            for (int stepIndex = 0; stepIndex < flowProc.steps.Count; stepIndex++)
            {
                List<OperationType> operations = flowProc.steps[stepIndex]?.Ops;
                if (operations == null)
                {
                    continue;
                }
                for (int opIndex = 0; opIndex < operations.Count; opIndex++)
                {
                    OperationType operation = operations[opIndex];
                    var targets = new List<GotoTargetRecord>();
                    CollectGotoTargets(operation, operation, targets);
                    foreach (GotoTargetRecord target in targets)
                    {
                        if (!ProcessDefinitionService.TryParseGotoKey(
                                target.Address,
                                out int targetProcIndex,
                                out int targetStepIndex,
                                out int targetOpIndex)
                            || targetProcIndex != flowProcIndex
                            || targetStepIndex < 0
                            || targetStepIndex >= flowProc.steps.Count
                            || targetOpIndex < 0
                            || targetOpIndex >= (flowProc.steps[targetStepIndex]?.Ops?.Count ?? 0))
                        {
                            continue;
                        }
                        jumpLinks.Add(new JumpLink
                        {
                            SourceStepIndex = stepIndex,
                            SourceOpIndex = opIndex,
                            TargetStepIndex = targetStepIndex,
                            TargetOpIndex = targetOpIndex,
                            Kinds = new List<JumpKind> { target.Kind }
                        });
                    }
                }
            }

            List<JumpLink> distinctLinks = jumpLinks
                .GroupBy(link => new
                {
                    link.SourceStepIndex,
                    link.SourceOpIndex,
                    link.TargetStepIndex,
                    link.TargetOpIndex
                })
                .Select(group =>
                {
                    JumpLink link = group.First();
                    link.Kinds = group.SelectMany(item => item.Kinds).Distinct().ToList();
                    return link;
                })
                .OrderBy(link => link.SourceStepIndex)
                .ThenBy(link => link.SourceOpIndex)
                .ThenBy(link => link.TargetStepIndex)
                .ThenBy(link => link.TargetOpIndex)
                .ToList();
            jumpLinks.Clear();
            jumpLinks.AddRange(distinctLinks);

            int crossId = 1;
            foreach (JumpLink link in jumpLinks.Where(link => link.IsCrossStep))
            {
                link.CrossId = crossId++;
            }
        }

        private static void CollectGotoTargets(
            object value,
            OperationType rootOperation,
            ICollection<GotoTargetRecord> targets)
        {
            if (value == null)
            {
                return;
            }
            if (value is IEnumerable enumerable && !(value is string))
            {
                foreach (object item in enumerable)
                {
                    CollectGotoTargets(item, rootOperation, targets);
                }
                return;
            }

            foreach (PropertyInfo property in value.GetType().GetProperties())
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                if (property.PropertyType == typeof(string)
                    && property.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string target = property.GetValue(value) as string;
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        targets.Add(new GotoTargetRecord
                        {
                            Address = target,
                            Kind = ResolveJumpKind(rootOperation, value, property)
                        });
                    }
                    continue;
                }
                if (property.PropertyType != typeof(string)
                    && typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                {
                    CollectGotoTargets(property.GetValue(value), rootOperation, targets);
                }
            }
        }

        private static JumpKind ResolveJumpKind(
            OperationType rootOperation,
            object owner,
            PropertyInfo property)
        {
            switch (property.Name)
            {
                case "Goto1":
                    return string.Equals(rootOperation?.AlarmType, "自动处理", StringComparison.Ordinal)
                        ? JumpKind.Automatic
                        : JumpKind.Confirm;
                case "Goto2":
                case "PopupGoto2":
                    return JumpKind.Reject;
                case "Goto3":
                case "PopupGoto3":
                    return JumpKind.Cancel;
                case "PopupGoto1":
                    return JumpKind.Confirm;
                case "TrueGoto":
                    return JumpKind.True;
                case "FalseGoto":
                    return JumpKind.False;
                case "DefaultGoto":
                    return JumpKind.Default;
                case "Goto" when owner is GotoParam:
                    return JumpKind.Match;
                default:
                    return JumpKind.Generic;
            }
        }

        private void InstructionListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            OperationType operation = GetOperation(e.ItemIndex);
            var item = new ListViewItem(string.Empty)
            {
                Tag = operation
            };
            item.SubItems.Add(operation?.Num.ToString() ?? e.ItemIndex.ToString());
            item.SubItems.Add(operation?.Name ?? string.Empty);
            item.SubItems.Add(operation?.OperaType ?? string.Empty);
            item.SubItems.Add(operation?.Note ?? string.Empty);
            e.Item = item;
        }

        private static Color GetJumpKindColor(JumpKind kind)
        {
            switch (kind)
            {
                case JumpKind.Automatic: return UiPalette.JumpAutomatic;
                case JumpKind.Confirm: return UiPalette.Success;
                case JumpKind.Reject: return UiPalette.Danger;
                case JumpKind.Cancel: return UiPalette.JumpCancel;
                case JumpKind.True: return UiPalette.Brand;
                case JumpKind.False: return UiPalette.Transition;
                case JumpKind.Default: return UiPalette.TextMuted;
                case JumpKind.Success: return UiPalette.Success;
                case JumpKind.Failure: return UiPalette.Breakpoint;
                case JumpKind.Match: return UiPalette.JumpMatch;
                default: return UiPalette.JumpDefault;
            }
        }

        private bool IsEndpointOnCurrentStep(JumpLink link, int opIndex)
        {
            return (link.SourceStepIndex == flowStepIndex && link.SourceOpIndex == opIndex)
                || (link.TargetStepIndex == flowStepIndex && link.TargetOpIndex == opIndex);
        }

        private int GetJumpFocusRow()
        {
            return SelectedIndices.Cast<int>().DefaultIfEmpty(-1).First();
        }

        private void InstructionListView_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush background = new SolidBrush(UiPalette.SurfaceSubtle))
            using (SolidBrush separator = new SolidBrush(UiPalette.Stroke))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
                e.Graphics.FillRectangle(separator, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Width, 1);
                e.Graphics.FillRectangle(separator, e.Bounds.Right - 1, e.Bounds.Top, 1, e.Bounds.Height);
            }
            if (e.ColumnIndex == 0)
            {
                DrawStatusHeader(e.Graphics, e.Bounds);
                return;
            }
            TextRenderer.DrawText(
                e.Graphics,
                e.Header.Text,
                headerFont,
                e.Bounds,
                UiPalette.TextSecondary,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                    | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void InstructionListView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            if (View != View.Details)
            {
                e.DrawDefault = true;
            }
        }

        private void InstructionListView_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            int index = e.ItemIndex;
            OperationType operation = GetOperation(index);
            bool selected = SelectedIndices.Contains(index);
            bool isRuntime = index == runtimeIndex;
            bool hasTransientColor = rowBackColors.ContainsKey(index) || !allRowsBackColor.IsEmpty;
            // 第0列承载流程轨道和跳转动画，不叠加选中蓝底。
            // 选择变化时只重绘轨道事实，避免原生选中刷新与动画刷新产生半拍色差。
            bool paintSelection = e.ColumnIndex != 0 && selected;
            Color backColor = ResolveRowBackColor(
                operation,
                index,
                paintSelection,
                isRuntime);
            Color foreColor = operation?.Disable == true && !hasTransientColor
                ? UiPalette.DisabledSoft
                : UiPalette.TextPrimary;

            using (SolidBrush background = new SolidBrush(backColor))
            using (SolidBrush separator = new SolidBrush(UiPalette.Stroke))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
                e.Graphics.FillRectangle(separator, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Width, 1);
                e.Graphics.FillRectangle(separator, e.Bounds.Right - 1, e.Bounds.Top, 1, e.Bounds.Height);
            }

            if (e.ColumnIndex == 0)
            {
                DrawStatusCell(e.Graphics, e.Bounds, operation, index, isRuntime);
                return;
            }

            Rectangle textBounds = Rectangle.Inflate(e.Bounds, -10, 0);
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            if (e.ColumnIndex == 1 || e.ColumnIndex == 3)
            {
                flags |= TextFormatFlags.HorizontalCenter;
            }
            else
            {
                flags |= TextFormatFlags.Left;
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, textBounds, foreColor, flags);
        }

        private Color ResolveRowBackColor(OperationType operation, int index, bool selected, bool isRuntime)
        {
            if (rowBackColors.TryGetValue(index, out Color transientColor))
            {
                return transientColor;
            }
            if (!allRowsBackColor.IsEmpty)
            {
                return allRowsBackColor;
            }
            if (operation?.Disable == true)
            {
                return selected ? UiPalette.TextPrimary : UiPalette.TextSecondary;
            }
            if (isRuntime)
            {
                return GetRuntimeRowBackColor(runtimeState, runtimeBreakpoint);
            }
            if (selected)
            {
                return UiPalette.Selection;
            }
            return index % 2 == 0 ? UiPalette.SurfaceStrong : UiPalette.Surface;
        }

        private void DrawStatusCell(
            Graphics graphics,
            Rectangle bounds,
            OperationType operation,
            int index,
            bool isRuntime)
        {
            VisualState state = operation?.Disable == true
                ? VisualState.Disabled
                : isRuntime ? GetRuntimeVisualState()
                : operation?.IsBreakpoint == true ? VisualState.Breakpoint
                : VisualState.None;
            GraphicsState savedState = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int centerX = GetRailCenterX(bounds);
            int centerY = bounds.Top + bounds.Height / 2;
            Color railColor = operation?.Disable == true
                ? UiPalette.TextDisabled
                : UiPalette.Stroke;
            using (Pen railPen = new Pen(railColor, 1.4F))
            {
                if (index > 0)
                {
                    graphics.DrawLine(railPen, centerX, bounds.Top, centerX, centerY - 5);
                }
                if (index < OperationCount - 1)
                {
                    graphics.DrawLine(railPen, centerX, centerY + 5, centerX, bounds.Bottom);
                }
            }
            DrawSameStepJumpPaths(graphics, bounds, index, centerX, centerY);
            if (isRuntime && state != VisualState.None)
            {
                using (SolidBrush accentBrush = new SolidBrush(GetStateColor(state)))
                {
                    graphics.FillRectangle(accentBrush, bounds.Left, bounds.Top + 2, 3, Math.Max(1, bounds.Height - 4));
                }
            }
            List<JumpLink> endpointLinks = jumpLinks
                .Where(link => IsEndpointOnCurrentStep(link, index))
                .ToList();
            List<JumpKind> outgoingKinds = endpointLinks.Where(link =>
                    link.SourceStepIndex == flowStepIndex && link.SourceOpIndex == index)
                .SelectMany(link => link.Kinds)
                .Distinct()
                .ToList();
            bool hasOutgoing = outgoingKinds.Count > 0;
            bool hasIncoming = endpointLinks.Any(link =>
                link.TargetStepIndex == flowStepIndex && link.TargetOpIndex == index);
            if (state == VisualState.None)
            {
                DrawFlowStateNode(graphics, centerX, centerY, outgoingKinds, hasIncoming);
                DrawCrossStepMarker(graphics, centerX, centerY, endpointLinks);
                graphics.Restore(savedState);
                return;
            }

            Rectangle iconBounds = new Rectangle(
                centerX - 12,
                bounds.Top + (bounds.Height - 24) / 2,
                24,
                24);
            DrawStateIcon(graphics, iconBounds, state, GetStateColor(state), GetStateBackColor(state));
            if (operation?.IsBreakpoint == true && state != VisualState.Breakpoint)
            {
                using (SolidBrush markerBrush = new SolidBrush(UiPalette.Danger))
                using (Pen markerBorder = new Pen(UiPalette.TextInverse, 1.5F))
                {
                    Rectangle marker = new Rectangle(iconBounds.Right - 6, iconBounds.Top - 1, 8, 8);
                    graphics.FillEllipse(markerBrush, marker);
                    graphics.DrawEllipse(markerBorder, marker);
                }
            }
            if (hasOutgoing)
            {
                DrawRuntimeJumpBadge(graphics, iconBounds, outgoingKinds);
            }
            DrawCrossStepMarker(graphics, centerX, centerY, endpointLinks);
            graphics.Restore(savedState);
        }

        private static void DrawStatusHeader(Graphics graphics, Rectangle bounds)
        {
            GraphicsState savedState = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int centerX = GetRailCenterX(bounds);
            int centerY = bounds.Top + bounds.Height / 2;
            using (Pen linePen = new Pen(UiPalette.StrokeStrong, 1.3F))
            using (SolidBrush nodeBrush = new SolidBrush(UiPalette.TextMuted))
            {
                graphics.DrawLine(linePen, centerX, centerY - 7, centerX, centerY + 7);
                graphics.FillEllipse(nodeBrush, centerX - 2, centerY - 9, 4, 4);
                graphics.FillEllipse(nodeBrush, centerX - 3, centerY - 3, 6, 6);
                graphics.FillEllipse(nodeBrush, centerX - 2, centerY + 5, 4, 4);
            }
            graphics.Restore(savedState);
        }

        private static int GetRailCenterX(Rectangle bounds)
        {
            return bounds.Left + bounds.Width / 2;
        }

        private static void DrawFlowStateNode(
            Graphics graphics,
            int centerX,
            int centerY,
            IReadOnlyList<JumpKind> outgoingKinds,
            bool hasIncoming)
        {
            bool hasOutgoing = outgoingKinds.Count > 0;
            Color borderColor = UiPalette.StrokeStrong;
            Color fillColor = UiPalette.SurfaceStrong;
            using (SolidBrush fillBrush = new SolidBrush(fillColor))
            using (Pen borderPen = new Pen(borderColor, 1.4F))
            using (SolidBrush centerBrush = new SolidBrush(borderColor))
            {
                if (!hasOutgoing)
                {
                    Rectangle nodeBounds = new Rectangle(centerX - 5, centerY - 5, 10, 10);
                    graphics.FillEllipse(fillBrush, nodeBounds);
                    graphics.DrawEllipse(borderPen, nodeBounds);
                }
                if (hasOutgoing)
                {
                    DrawJumpOperationMarker(graphics, centerX, centerY, outgoingKinds);
                }
                else if (hasIncoming)
                {
                    graphics.DrawEllipse(borderPen, centerX - 2, centerY - 2, 4, 4);
                }
                else
                {
                    graphics.FillEllipse(centerBrush, centerX - 1, centerY - 1, 3, 3);
                }
            }
        }

        private void DrawSameStepJumpPaths(
            Graphics graphics,
            Rectangle bounds,
            int rowIndex,
            int centerX,
            int centerY)
        {
            int focusRow = GetJumpFocusRow();
            if (focusRow < 0)
            {
                return;
            }
            List<JumpLink> outgoingLinks = jumpLinks.Where(link =>
                    !link.IsCrossStep
                    && link.SourceStepIndex == flowStepIndex
                    && link.SourceOpIndex == focusRow)
                .OrderBy(link => link.TargetOpIndex)
                .ToList();
            List<JumpLink> incomingLinks = jumpLinks.Where(link =>
                    !link.IsCrossStep
                    && link.TargetStepIndex == flowStepIndex
                    && link.TargetOpIndex == focusRow
                    && link.SourceOpIndex != focusRow)
                .OrderBy(link => link.SourceOpIndex)
                .ToList();
            foreach (JumpLink link in outgoingLinks.Concat(incomingLinks))
            {
                int first = Math.Min(link.SourceOpIndex, link.TargetOpIndex);
                int last = Math.Max(link.SourceOpIndex, link.TargetOpIndex);
                if (rowIndex < first || rowIndex > last)
                {
                    continue;
                }
                bool isOutput = link.SourceOpIndex == focusRow;
                List<JumpLink> directionLinks = isOutput ? outgoingLinks : incomingLinks;
                int laneIndex = directionLinks.IndexOf(link);
                int visibleLaneIndex = laneIndex % JumpVisibleLaneCount;
                int laneX = isOutput
                    ? centerX - 22 - visibleLaneIndex * JumpLaneSpacing
                    : centerX + 22 + visibleLaneIndex * JumpLaneSpacing;
                float portOffset = GetPortOffset(laneIndex, directionLinks.Count);
                float sourcePortY = link.SourceOpIndex == focusRow ? centerY + portOffset : centerY;
                float targetPortY = link.TargetOpIndex == focusRow ? centerY + portOffset : centerY;
                JumpKind primaryKind = link.Kinds.Count > 0 ? link.Kinds[0] : JumpKind.Generic;
                Color color = GetJumpKindColor(primaryKind);
                Color trackColor = link.Kinds.Count > 1 ? UiPalette.TextMuted : color;
                List<Pen> flowPens = CreateFlowPens(link.Kinds);
                using (Pen basePen = new Pen(Color.FromArgb(155, trackColor), 2.2F))
                using (SolidBrush arrowBrush = new SolidBrush(color))
                {
                    try
                    {
                        basePen.StartCap = LineCap.Round;
                        basePen.EndCap = LineCap.Round;
                        if (link.SourceOpIndex == link.TargetOpIndex)
                        {
                            if (rowIndex == link.SourceOpIndex)
                            {
                                Rectangle loopBounds = Rectangle.FromLTRB(
                                    laneX,
                                    centerY - 9,
                                    centerX - 6,
                                    centerY + 9);
                                graphics.DrawArc(basePen, loopBounds, 65, 235);
                                foreach (Pen flowPen in flowPens)
                                {
                                    graphics.DrawArc(flowPen, loopBounds, 65, 235);
                                }
                                DrawTargetArrow(graphics, arrowBrush, centerX, centerY, false);
                            }
                            continue;
                        }

                        float firstPortY = first == link.SourceOpIndex ? sourcePortY : targetPortY;
                        float lastPortY = last == link.SourceOpIndex ? sourcePortY : targetPortY;
                        float verticalTop = rowIndex == first ? firstPortY : bounds.Top;
                        float verticalBottom = rowIndex == last ? lastPortY : bounds.Bottom;
                        PointF verticalStart = link.TargetOpIndex > link.SourceOpIndex
                            ? new PointF(laneX, verticalTop)
                            : new PointF(laneX, verticalBottom);
                        PointF verticalEnd = link.TargetOpIndex > link.SourceOpIndex
                            ? new PointF(laneX, verticalBottom)
                            : new PointF(laneX, verticalTop);
                        DrawFlowSegment(graphics, basePen, flowPens, verticalStart, verticalEnd);
                        if (rowIndex == link.SourceOpIndex)
                        {
                            PointF sourceEdge = new PointF(
                                centerX + (isOutput ? -7 : 7),
                                centerY);
                            DrawFlowSegment(
                                graphics,
                                basePen,
                                flowPens,
                                sourceEdge,
                                new PointF(laneX, sourcePortY));
                        }
                        if (rowIndex == link.TargetOpIndex)
                        {
                            PointF targetEdge = new PointF(
                                centerX + (isOutput ? -7 : 7),
                                centerY);
                            DrawFlowSegment(
                                graphics,
                                basePen,
                                flowPens,
                                new PointF(laneX, targetPortY),
                                targetEdge);
                            DrawTargetArrow(graphics, arrowBrush, centerX, centerY, !isOutput);
                        }
                    }
                    finally
                    {
                        foreach (Pen flowPen in flowPens)
                        {
                            flowPen.Dispose();
                        }
                    }
                }
            }
        }

        private void JumpFlowTimer_Tick(object sender, EventArgs e)
        {
            if (!Visible || !IsHandleCreated || linkedNavigationDepth > 0)
            {
                return;
            }
            int focusRow = GetJumpFocusRow();
            if (focusRow < 0 || !jumpLinks.Any(link =>
                    !link.IsCrossStep
                    && ((link.SourceStepIndex == flowStepIndex && link.SourceOpIndex == focusRow)
                        || (link.TargetStepIndex == flowStepIndex && link.TargetOpIndex == focusRow))))
            {
                return;
            }
            jumpFlowPhase = (jumpFlowPhase + 0.9F) % 18F;
            InvalidateFlowColumn();
        }

        private List<Pen> CreateFlowPens(IReadOnlyList<JumpKind> kinds)
        {
            List<JumpKind> distinctKinds = kinds.Distinct().ToList();
            if (distinctKinds.Count == 0)
            {
                distinctKinds.Add(JumpKind.Generic);
            }
            var pens = new List<Pen>();
            float gap = distinctKinds.Count == 1 ? 3.2F : 3.2F * distinctKinds.Count;
            for (int i = 0; i < distinctKinds.Count; i++)
            {
                Color color = ControlPaint.Light(GetJumpKindColor(distinctKinds[i]), 0.55F);
                pens.Add(new Pen(color, 2.4F)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    DashCap = DashCap.Round,
                    DashPattern = new[] { 1.1F, gap },
                    DashOffset = -jumpFlowPhase - i * 3.2F
                });
            }
            return pens;
        }

        private static float GetPortOffset(int laneIndex, int laneCount)
        {
            if (laneCount <= 1)
            {
                return 0F;
            }
            return -6F + 12F * laneIndex / (laneCount - 1F);
        }

        private static void DrawFlowSegment(
            Graphics graphics,
            Pen basePen,
            IReadOnlyList<Pen> flowPens,
            PointF start,
            PointF end)
        {
            graphics.DrawLine(basePen, start, end);
            foreach (Pen flowPen in flowPens)
            {
                graphics.DrawLine(flowPen, start, end);
            }
        }

        private static void DrawTargetArrow(
            Graphics graphics,
            Brush brush,
            int centerX,
            int centerY,
            bool fromRight)
        {
            if (fromRight)
            {
                graphics.FillPolygon(brush, new[]
                {
                    new Point(centerX + 6, centerY),
                    new Point(centerX + 12, centerY - 4),
                    new Point(centerX + 12, centerY + 4)
                });
            }
            else
            {
                graphics.FillPolygon(brush, new[]
                {
                    new Point(centerX - 6, centerY),
                    new Point(centerX - 12, centerY - 4),
                    new Point(centerX - 12, centerY + 4)
                });
            }
        }

        private static void DrawJumpOperationMarker(
            Graphics graphics,
            int centerX,
            int centerY,
            IReadOnlyList<JumpKind> kinds)
        {
            List<Color> colors = kinds.Distinct().Select(GetJumpKindColor).ToList();
            if (colors.Count == 0)
            {
                colors.Add(GetJumpKindColor(JumpKind.Generic));
            }
            Rectangle marker = new Rectangle(centerX - 8, centerY - 8, 16, 16);
            Color fillColor = colors.Count == 1 ? colors[0] : UiPalette.TextPrimary;
            using (SolidBrush fill = new SolidBrush(fillColor))
            using (Pen glyph = new Pen(UiPalette.TextInverse, 1.7F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                graphics.FillEllipse(fill, marker);
                if (colors.Count > 1)
                {
                    Rectangle ring = Rectangle.Inflate(marker, 1, 1);
                    float sweep = 360F / colors.Count;
                    for (int i = 0; i < colors.Count; i++)
                    {
                        using (Pen segment = new Pen(colors[i], 2.4F))
                        {
                            graphics.DrawArc(segment, ring, -90F + i * sweep + 2F, sweep - 4F);
                        }
                    }
                }
                graphics.DrawLines(glyph, new[]
                {
                    new Point(centerX - 4, centerY + 3),
                    new Point(centerX, centerY + 3),
                    new Point(centerX, centerY - 2),
                    new Point(centerX + 4, centerY - 2)
                });
                graphics.DrawLine(glyph, centerX + 1, centerY - 5, centerX + 4, centerY - 2);
                graphics.DrawLine(glyph, centerX + 1, centerY + 1, centerX + 4, centerY - 2);
            }
        }

        private static void DrawRuntimeJumpBadge(
            Graphics graphics,
            Rectangle iconBounds,
            IReadOnlyList<JumpKind> kinds)
        {
            Rectangle badge = new Rectangle(iconBounds.Left - 3, iconBounds.Top - 2, 10, 10);
            JumpKind kind = kinds.Count > 0 ? kinds[0] : JumpKind.Generic;
            using (SolidBrush fill = new SolidBrush(GetJumpKindColor(kind)))
            using (Pen border = new Pen(UiPalette.TextInverse, 1.2F))
            {
                graphics.FillEllipse(fill, badge);
                graphics.DrawEllipse(border, badge);
            }
        }

        private void DrawCrossStepMarker(
            Graphics graphics,
            int centerX,
            int centerY,
            IReadOnlyCollection<JumpLink> endpointLinks)
        {
            List<JumpLink> outgoingLinks = endpointLinks
                .Where(link => link.IsCrossStep && link.SourceStepIndex == flowStepIndex)
                .OrderBy(link => link.CrossId)
                .ToList();
            List<JumpLink> incomingLinks = endpointLinks
                .Where(link => link.IsCrossStep && link.TargetStepIndex == flowStepIndex)
                .OrderBy(link => link.CrossId)
                .ToList();
            if (outgoingLinks.Count == 0 && incomingLinks.Count == 0)
            {
                return;
            }

            for (int i = 0; i < outgoingLinks.Count; i++)
            {
                DrawCrossStepBadge(graphics, centerX, centerY, true, i);
            }

            for (int i = 0; i < incomingLinks.Count; i++)
            {
                DrawCrossStepBadge(graphics, centerX, centerY, false, i);
            }
        }

        private static void DrawCrossStepBadge(
            Graphics graphics,
            int centerX,
            int centerY,
            bool isOutgoing,
            int laneIndex)
        {
            Color color = isOutgoing
                ? UiPalette.JumpCancel
                : UiPalette.JumpAutomatic;
            Rectangle badge = GetCrossStepBadgeBounds(centerX, centerY, isOutgoing, laneIndex);
            using (SolidBrush brush = new SolidBrush(color))
            using (Pen connector = new Pen(Color.FromArgb(165, color), 1.8F))
            using (Pen border = new Pen(UiPalette.TextInverse, 1.2F))
            using (Pen glyph = new Pen(UiPalette.TextInverse, 1.55F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                if (isOutgoing)
                {
                    graphics.DrawLine(connector, badge.Right, centerY, centerX - 8, centerY);
                    graphics.FillEllipse(brush, badge);
                    graphics.DrawEllipse(border, badge);
                    graphics.DrawLine(glyph, badge.Right - 3, badge.Bottom - 3, badge.Left + 3, badge.Top + 3);
                    graphics.DrawLine(glyph, badge.Left + 3, badge.Top + 3, badge.Left + 7, badge.Top + 3);
                    graphics.DrawLine(glyph, badge.Left + 3, badge.Top + 3, badge.Left + 3, badge.Top + 7);
                }
                else
                {
                    graphics.DrawLine(connector, centerX + 8, centerY, badge.Left, centerY);
                    graphics.FillEllipse(brush, badge);
                    graphics.DrawEllipse(border, badge);
                    graphics.DrawLine(glyph, badge.Right - 3, badge.Top + 3, badge.Left + 3, badge.Bottom - 3);
                    graphics.DrawLine(glyph, badge.Left + 3, badge.Bottom - 7, badge.Left + 3, badge.Bottom - 3);
                    graphics.DrawLine(glyph, badge.Left + 3, badge.Bottom - 3, badge.Left + 7, badge.Bottom - 3);
                }
            }
        }

        private static Rectangle GetCrossStepBadgeBounds(
            int centerX,
            int centerY,
            bool isOutgoing,
            int laneIndex)
        {
            int x = isOutgoing
                ? centerX - 25 - laneIndex * 16
                : centerX + 11 + laneIndex * 16;
            return new Rectangle(x, centerY - 7, 14, 14);
        }

        private bool TryGetCrossStepJump(Point point, out JumpLink link, out bool isOutgoing)
        {
            link = null;
            isOutgoing = false;
            int rowIndex = IndexFromPoint(point);
            if (rowIndex < 0 || rowIndex >= OperationCount || base.Columns.Count == 0
                || point.X < 0 || point.X >= base.Columns[0].Width)
            {
                return false;
            }

            Rectangle rowBounds;
            try
            {
                rowBounds = GetItemRect(rowIndex);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
            int centerX = GetRailCenterX(new Rectangle(0, rowBounds.Top, base.Columns[0].Width, rowBounds.Height));
            int centerY = rowBounds.Top + rowBounds.Height / 2;
            List<JumpLink> outgoingLinks = jumpLinks
                .Where(item => item.IsCrossStep
                    && item.SourceStepIndex == flowStepIndex
                    && item.SourceOpIndex == rowIndex)
                .OrderBy(item => item.CrossId)
                .ToList();
            List<JumpLink> incomingLinks = jumpLinks
                .Where(item => item.IsCrossStep
                    && item.TargetStepIndex == flowStepIndex
                    && item.TargetOpIndex == rowIndex)
                .OrderBy(item => item.CrossId)
                .ToList();

            for (int i = 0; i < outgoingLinks.Count; i++)
            {
                Rectangle badge = GetCrossStepBadgeBounds(centerX, centerY, true, i);
                if (IsCrossStepHit(point, badge, badge.Right, centerX - 8, centerY))
                {
                    link = outgoingLinks[i];
                    isOutgoing = true;
                    return true;
                }
            }
            for (int i = 0; i < incomingLinks.Count; i++)
            {
                Rectangle badge = GetCrossStepBadgeBounds(centerX, centerY, false, i);
                if (IsCrossStepHit(point, badge, centerX + 8, badge.Left, centerY))
                {
                    link = incomingLinks[i];
                    return true;
                }
            }
            return false;
        }

        private static bool IsCrossStepHit(
            Point point,
            Rectangle badge,
            int segmentStartX,
            int segmentEndX,
            int centerY)
        {
            if (Rectangle.Inflate(badge, 3, 3).Contains(point))
            {
                return true;
            }
            int left = Math.Min(segmentStartX, segmentEndX);
            int right = Math.Max(segmentStartX, segmentEndX);
            return point.X >= left
                && point.X <= right
                && Math.Abs(point.Y - centerY) <= 5;
        }

        private VisualState GetRuntimeVisualState()
        {
            if (runtimeBreakpoint)
            {
                return VisualState.Breakpoint;
            }
            switch (runtimeState)
            {
                case ProcRunState.Running:
                    return VisualState.Running;
                case ProcRunState.Paused:
                case ProcRunState.Pausing:
                    return VisualState.Paused;
                case ProcRunState.SingleStep:
                    return VisualState.SingleStep;
                case ProcRunState.Alarming:
                    return VisualState.Alarming;
                case ProcRunState.Stopping:
                    return VisualState.Stopping;
                default:
                    return VisualState.None;
            }
        }

        private static Color GetRuntimeRowBackColor(ProcRunState state, bool isBreakpoint)
        {
            if (isBreakpoint)
            {
                return UiPalette.BreakpointSoft;
            }
            switch (state)
            {
                case ProcRunState.Running:
                    return UiPalette.SuccessSoft;
                case ProcRunState.Paused:
                case ProcRunState.Pausing:
                    return UiPalette.WarningSoft;
                case ProcRunState.SingleStep:
                    return UiPalette.InfoSoft;
                case ProcRunState.Alarming:
                    return UiPalette.DangerSoft;
                case ProcRunState.Stopping:
                    return UiPalette.StoppingSoft;
                default:
                    return UiPalette.SurfaceSubtle;
            }
        }

        private static Color GetStateColor(VisualState state)
        {
            switch (state)
            {
                case VisualState.Running: return UiPalette.Success;
                case VisualState.Paused: return UiPalette.Warning;
                case VisualState.SingleStep: return UiPalette.Brand;
                case VisualState.Breakpoint: return UiPalette.Breakpoint;
                case VisualState.Alarming: return UiPalette.Danger;
                case VisualState.Stopping: return UiPalette.Stopping;
                case VisualState.Disabled: return UiPalette.DisabledSoft;
                default: return UiPalette.TextMuted;
            }
        }

        private static Color GetStateBackColor(VisualState state)
        {
            switch (state)
            {
                case VisualState.Running: return UiPalette.SuccessSoft;
                case VisualState.Paused: return UiPalette.WarningSoft;
                case VisualState.SingleStep: return UiPalette.InfoSoft;
                case VisualState.Breakpoint: return UiPalette.BreakpointSoft;
                case VisualState.Alarming: return UiPalette.DangerSoft;
                case VisualState.Stopping: return UiPalette.StoppingSoft;
                case VisualState.Disabled: return UiPalette.TextSecondary;
                default: return UiPalette.SurfaceSubtle;
            }
        }

        private static void DrawStateIcon(
            Graphics graphics,
            Rectangle bounds,
            VisualState state,
            Color accent,
            Color backColor)
        {
            Rectangle shadowBounds = bounds;
            shadowBounds.Offset(0, 1);
            using (SolidBrush shadow = new SolidBrush(UiPalette.Shadow))
            using (SolidBrush background = new SolidBrush(backColor))
            using (Pen outline = new Pen(Color.FromArgb(72, accent), 1F))
            {
                graphics.FillEllipse(shadow, shadowBounds);
                graphics.FillEllipse(background, bounds);
                graphics.DrawEllipse(outline, bounds);
            }
            Rectangle glyph = new Rectangle(bounds.Left + 6, bounds.Top + 6, 12, 12);
            using (Pen pen = new Pen(accent, 1.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (SolidBrush brush = new SolidBrush(accent))
            {
                switch (state)
                {
                    case VisualState.Running:
                        graphics.FillPolygon(brush, new[] { new Point(glyph.Left + 2, glyph.Top + 1), new Point(glyph.Right - 1, glyph.Top + 6), new Point(glyph.Left + 2, glyph.Bottom - 1) });
                        break;
                    case VisualState.Paused:
                        graphics.FillRectangle(brush, glyph.Left + 2, glyph.Top + 1, 3, glyph.Height - 2);
                        graphics.FillRectangle(brush, glyph.Right - 5, glyph.Top + 1, 3, glyph.Height - 2);
                        break;
                    case VisualState.SingleStep:
                        graphics.FillPolygon(brush, new[] { new Point(glyph.Left + 1, glyph.Top + 1), new Point(glyph.Right - 3, glyph.Top + 6), new Point(glyph.Left + 1, glyph.Bottom - 1) });
                        graphics.FillRectangle(brush, glyph.Right - 2, glyph.Top + 1, 2, glyph.Height - 2);
                        break;
                    case VisualState.Breakpoint:
                        graphics.DrawEllipse(pen, glyph.Left + 1, glyph.Top + 1, glyph.Width - 2, glyph.Height - 2);
                        graphics.FillEllipse(brush, glyph.Left + 4, glyph.Top + 4, 4, 4);
                        break;
                    case VisualState.Alarming:
                        graphics.DrawPolygon(pen, new[] { new Point(glyph.Left + 6, glyph.Top), new Point(glyph.Right, glyph.Bottom - 1), new Point(glyph.Left, glyph.Bottom - 1) });
                        graphics.DrawLine(pen, glyph.Left + 6, glyph.Top + 3, glyph.Left + 6, glyph.Top + 7);
                        graphics.FillEllipse(brush, glyph.Left + 5, glyph.Bottom - 3, 2, 2);
                        break;
                    case VisualState.Stopping:
                        graphics.FillRectangle(brush, glyph.Left + 2, glyph.Top + 2, glyph.Width - 4, glyph.Height - 4);
                        break;
                    case VisualState.Disabled:
                        graphics.DrawEllipse(pen, glyph);
                        graphics.DrawLine(pen, glyph.Left + 2, glyph.Top + 2, glyph.Right - 2, glyph.Bottom - 2);
                        break;
                }
            }
        }

    }

}
