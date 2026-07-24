// 模块：编辑器 / 流程。
// 职责范围：以单一缓冲画布呈现两级流程导航，独立管理展开、选择和像素滚动。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class ProcessOutlineItem
    {
        public Guid ProcId { get; set; }
        public Guid StepId { get; set; }
        public int ProcIndex { get; set; }
        public int StepIndex { get; set; }
        public string Text { get; set; }
        public string ImageKey { get; set; }
        public Color ForeColor { get; set; }
        public bool HasChildren { get; set; }

        public bool IsProcess => StepIndex < 0;

        public override string ToString()
        {
            return Text ?? string.Empty;
        }
    }

    internal struct ProcessOutlineScrollAnchor
    {
        public ProcessOutlineScrollAnchor(
            Guid procId,
            Guid stepId,
            int offsetWithinRow,
            int absoluteOffset)
        {
            ProcId = procId;
            StepId = stepId;
            OffsetWithinRow = Math.Max(0, offsetWithinRow);
            AbsoluteOffset = Math.Max(0, absoluteOffset);
        }

        public Guid ProcId { get; }

        public Guid StepId { get; }

        public int OffsetWithinRow { get; }

        public int AbsoluteOffset { get; }

        public bool IsEmpty => ProcId == Guid.Empty;

        public static ProcessOutlineScrollAnchor Empty =>
            new ProcessOutlineScrollAnchor(
                Guid.Empty,
                Guid.Empty,
                0,
                0);
    }

    internal struct ProcessOutlineStepIdentity : IEquatable<ProcessOutlineStepIdentity>
    {
        public ProcessOutlineStepIdentity(Guid procId, Guid stepId)
        {
            ProcId = procId;
            StepId = stepId;
        }

        public Guid ProcId { get; }

        public Guid StepId { get; }

        public bool Equals(ProcessOutlineStepIdentity other)
        {
            return ProcId == other.ProcId && StepId == other.StepId;
        }

        public override bool Equals(object obj)
        {
            return obj is ProcessOutlineStepIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ProcId.GetHashCode() * 397) ^ StepId.GetHashCode();
            }
        }
    }

    internal sealed class ProcessOutlineList : ScrollableControl
    {
        private const int ToggleAreaWidth = 9;
        private const int RootImageLeft = 8;
        private const int StepIndent = 6;
        private const int ImageSize = 18;
        private const int TextGap = 2;
        private const int TextRightPadding = 8;

        private readonly List<ProcessOutlineItem> allItems =
            new List<ProcessOutlineItem>();
        private readonly List<ProcessOutlineItem> visibleItems =
            new List<ProcessOutlineItem>();
        private readonly HashSet<Guid> expandedProcIds =
            new HashSet<Guid>();
        private readonly Dictionary<Guid, ProcessOutlineItem> processItems =
            new Dictionary<Guid, ProcessOutlineItem>();
        private readonly Dictionary<ProcessOutlineStepIdentity, ProcessOutlineItem> stepItems =
            new Dictionary<ProcessOutlineStepIdentity, ProcessOutlineItem>();
        private ProcessOutlineItem selectedItem;
        private ImageList stateImages;
        private Font processFont;
        private Font stepFont;
        private int itemHeight = 25;

        public ProcessOutlineList()
        {
            AutoScroll = true;
            TabStop = true;
            SetStyle(
                ControlStyles.UserPaint
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.Selectable,
                true);
            DoubleBuffered = true;
        }

        public event EventHandler SelectedIndexChanged;

        public event EventHandler UserSelectionChanged;

        public ImageList StateImages
        {
            get => stateImages;
            set
            {
                if (ReferenceEquals(stateImages, value))
                {
                    return;
                }
                stateImages = value;
                Invalidate();
            }
        }

        public Font ProcessFont
        {
            get => processFont;
            set
            {
                if (ReferenceEquals(processFont, value))
                {
                    return;
                }
                processFont = value;
                Invalidate();
            }
        }

        public Font StepFont
        {
            get => stepFont;
            set
            {
                if (ReferenceEquals(stepFont, value))
                {
                    return;
                }
                stepFont = value;
                Invalidate();
            }
        }

        public int ItemHeight
        {
            get => itemHeight;
            set
            {
                int nextHeight = Math.Max(1, value);
                if (itemHeight == nextHeight)
                {
                    return;
                }
                ProcessOutlineScrollAnchor anchor = CaptureScrollAnchor();
                itemHeight = nextHeight;
                UpdateScrollExtent();
                RestoreScrollAnchor(anchor);
                Invalidate();
            }
        }

        public ProcessOutlineItem SelectedOutlineItem => selectedItem;

        public bool HasSelection => selectedItem != null;

        public int VisibleItemCount => visibleItems.Count;

        public IReadOnlyCollection<Guid> ExpandedProcIds =>
            expandedProcIds.ToArray();

        public ProcessOutlineScrollAnchor ScrollAnchor =>
            CaptureScrollAnchor();

        internal int ScrollOffset => GetScrollOffset();

        public void ReplaceItems(
            IEnumerable<ProcessOutlineItem> items,
            IEnumerable<Guid> expandedIds,
            Guid selectedProcId,
            Guid selectedStepId,
            ProcessOutlineScrollAnchor scrollAnchor)
        {
            allItems.Clear();
            processItems.Clear();
            stepItems.Clear();
            foreach (ProcessOutlineItem item in items
                ?? Enumerable.Empty<ProcessOutlineItem>())
            {
                if (item == null || item.ProcId == Guid.Empty)
                {
                    continue;
                }
                allItems.Add(item);
                if (item.IsProcess)
                {
                    processItems[item.ProcId] = item;
                }
                else if (item.StepId != Guid.Empty)
                {
                    stepItems[
                        new ProcessOutlineStepIdentity(
                            item.ProcId,
                            item.StepId)] = item;
                }
            }

            expandedProcIds.Clear();
            foreach (Guid procId in expandedIds ?? Enumerable.Empty<Guid>())
            {
                if (processItems.ContainsKey(procId))
                {
                    expandedProcIds.Add(procId);
                }
            }
            if (selectedStepId != Guid.Empty
                && stepItems.TryGetValue(
                    new ProcessOutlineStepIdentity(
                        selectedProcId,
                        selectedStepId),
                    out ProcessOutlineItem selectedStep))
            {
                expandedProcIds.Add(selectedStep.ProcId);
            }

            RebuildVisibleItems();
            selectedItem = ResolveSelection(selectedProcId, selectedStepId);
            UpdateScrollExtent();
            RestoreScrollAnchor(scrollAnchor);
            Invalidate();
        }

        public bool SelectIdentity(
            Guid procId,
            Guid stepId,
            bool ensureVisible)
        {
            if (procId == Guid.Empty
                || !processItems.TryGetValue(
                    procId,
                    out ProcessOutlineItem process))
            {
                ClearSelection();
                return false;
            }

            ProcessOutlineItem target = process;
            if (stepId != Guid.Empty)
            {
                if (!stepItems.TryGetValue(
                        new ProcessOutlineStepIdentity(procId, stepId),
                        out ProcessOutlineItem step))
                {
                    return false;
                }
                if (!expandedProcIds.Contains(procId))
                {
                    ExpandProcess(procId);
                    UpdateScrollExtent();
                }
                target = step;
            }

            SetSelection(target, false, ensureVisible);
            return true;
        }

        public void ClearSelection()
        {
            SetSelection(null, false, false);
        }

        public bool TryGetProcess(Guid procId, out ProcessOutlineItem item)
        {
            return processItems.TryGetValue(procId, out item);
        }

        public bool TryGetStep(
            Guid procId,
            Guid stepId,
            out ProcessOutlineItem item)
        {
            return stepItems.TryGetValue(
                new ProcessOutlineStepIdentity(procId, stepId),
                out item);
        }

        public void InvalidateProcess(Guid procId)
        {
            Rectangle invalidBounds = Rectangle.Empty;
            for (int index = 0; index < visibleItems.Count; index++)
            {
                if (visibleItems[index].ProcId != procId)
                {
                    continue;
                }
                Rectangle rowBounds = GetItemRectangle(index);
                if (!rowBounds.IntersectsWith(ClientRectangle))
                {
                    continue;
                }
                invalidBounds = invalidBounds.IsEmpty
                    ? rowBounds
                    : Rectangle.Union(invalidBounds, rowBounds);
            }
            if (!invalidBounds.IsEmpty)
            {
                Invalidate(Rectangle.Intersect(
                    invalidBounds,
                    ClientRectangle));
            }
        }

        public ProcessOutlineItem GetVisibleItem(int index)
        {
            return index >= 0 && index < visibleItems.Count
                ? visibleItems[index]
                : null;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 背景与行在同一个缓冲帧中绘制，避免背景擦除后再绘制内容造成闪烁。
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var backgroundBrush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(
                    backgroundBrush,
                    ClientRectangle);
            }

            if (visibleItems.Count == 0 || ClientSize.Height <= 0)
            {
                return;
            }

            int scrollOffset = GetScrollOffset();
            int firstIndex = Math.Max(
                0,
                Math.Min(
                    visibleItems.Count - 1,
                    scrollOffset / ItemHeight));
            int rowTop = firstIndex * ItemHeight - scrollOffset;
            for (int index = firstIndex;
                index < visibleItems.Count && rowTop < ClientSize.Height;
                index++, rowTop += ItemHeight)
            {
                Rectangle rowBounds = new Rectangle(
                    0,
                    rowTop,
                    ClientSize.Width,
                    ItemHeight);
                if (rowBounds.Bottom > 0)
                {
                    DrawItem(
                        e.Graphics,
                        visibleItems[index],
                        rowBounds,
                        IsVisuallySelected(visibleItems[index]));
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (CanFocus)
            {
                Focus();
            }

            int index = IndexFromPoint(e.Location);
            if (index < 0)
            {
                if (e.Button == MouseButtons.Left)
                {
                    SetSelection(null, true, false);
                }
                return;
            }

            ProcessOutlineItem item = visibleItems[index];
            if (e.Button == MouseButtons.Left
                && e.Clicks == 1
                && e.X <= ToggleAreaWidth
                && item.IsProcess
                && item.HasChildren)
            {
                ToggleProcess(item.ProcId);
                return;
            }

            if (e.Button == MouseButtons.Left
                || e.Button == MouseButtons.Right)
            {
                SetSelection(item, true, false);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left
                || e.X <= ToggleAreaWidth)
            {
                return;
            }
            int index = IndexFromPoint(e.Location);
            if (index >= 0)
            {
                ProcessOutlineItem item = visibleItems[index];
                if (item.IsProcess && item.HasChildren)
                {
                    ToggleProcess(item.ProcId);
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (HandleExpansionKey(e)
                || HandleNavigationKey(e))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            ProcessOutlineScrollAnchor anchor = CaptureScrollAnchor();
            base.OnSizeChanged(e);
            UpdateScrollExtent();
            RestoreScrollAnchor(anchor);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        private void DrawItem(
            Graphics graphics,
            ProcessOutlineItem item,
            Rectangle bounds,
            bool selected)
        {
            Color backgroundColor = selected
                ? UiPalette.Selection
                : BackColor;
            using (var backgroundBrush = new SolidBrush(backgroundColor))
            {
                graphics.FillRectangle(backgroundBrush, bounds);
            }
            if (selected)
            {
                using (var accentBrush = new SolidBrush(UiPalette.Brand))
                {
                    graphics.FillRectangle(
                        accentBrush,
                        bounds.Left,
                        bounds.Top,
                        3,
                        bounds.Height);
                }
            }

            if (item.IsProcess && item.HasChildren)
            {
                DrawChevron(
                    graphics,
                    bounds.Left + 4,
                    bounds.Top + bounds.Height / 2,
                    expandedProcIds.Contains(item.ProcId),
                    selected
                        ? UiPalette.SelectionText
                        : UiPalette.TextMuted);
            }

            int imageLeft = bounds.Left
                + RootImageLeft
                + (item.IsProcess ? 0 : StepIndent);
            var imageBounds = new Rectangle(
                imageLeft,
                bounds.Top
                    + Math.Max(0, (bounds.Height - ImageSize) / 2),
                ImageSize,
                ImageSize);
            if (StateImages != null
                && !string.IsNullOrEmpty(item.ImageKey)
                && StateImages.Images.ContainsKey(item.ImageKey))
            {
                graphics.DrawImage(
                    StateImages.Images[item.ImageKey],
                    imageBounds);
            }

            int textLeft = imageBounds.Right + TextGap;
            var textBounds = new Rectangle(
                textLeft,
                bounds.Top,
                Math.Max(
                    1,
                    ClientSize.Width - textLeft - TextRightPadding),
                bounds.Height);
            Font font = item.IsProcess
                ? ProcessFont ?? Font
                : StepFont ?? Font;
            Color textColor = selected
                ? UiPalette.SelectionText
                : Enabled
                    ? item.ForeColor
                    : UiPalette.TextDisabled;
            TextRenderer.DrawText(
                graphics,
                item.Text ?? string.Empty,
                font,
                textBounds,
                textColor,
                TextFormatFlags.Left
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix);
        }

        private bool HandleExpansionKey(KeyEventArgs e)
        {
            if (selectedItem == null)
            {
                return false;
            }
            if (e.KeyCode == Keys.Right
                && selectedItem.IsProcess
                && selectedItem.HasChildren
                && !expandedProcIds.Contains(selectedItem.ProcId))
            {
                ToggleProcess(selectedItem.ProcId);
                return true;
            }
            if (e.KeyCode == Keys.Left)
            {
                if (selectedItem.IsProcess
                    && expandedProcIds.Contains(selectedItem.ProcId))
                {
                    ToggleProcess(selectedItem.ProcId);
                    return true;
                }
                if (!selectedItem.IsProcess
                    && processItems.TryGetValue(
                        selectedItem.ProcId,
                        out ProcessOutlineItem process))
                {
                    SetSelection(process, true, true);
                    return true;
                }
            }
            if ((e.KeyCode == Keys.Enter
                    || e.KeyCode == Keys.Space)
                && selectedItem.IsProcess
                && selectedItem.HasChildren)
            {
                ToggleProcess(selectedItem.ProcId);
                return true;
            }
            return false;
        }

        private bool HandleNavigationKey(KeyEventArgs e)
        {
            if (visibleItems.Count == 0)
            {
                return false;
            }
            int currentIndex = FindVisibleIndex(selectedItem);
            int targetIndex;
            switch (e.KeyCode)
            {
                case Keys.Up:
                    targetIndex = currentIndex < 0
                        ? 0
                        : Math.Max(0, currentIndex - 1);
                    break;
                case Keys.Down:
                    targetIndex = currentIndex < 0
                        ? 0
                        : Math.Min(
                            visibleItems.Count - 1,
                            currentIndex + 1);
                    break;
                case Keys.Home:
                    targetIndex = 0;
                    break;
                case Keys.End:
                    targetIndex = visibleItems.Count - 1;
                    break;
                case Keys.PageUp:
                    targetIndex = Math.Max(
                        0,
                        (currentIndex < 0 ? 0 : currentIndex)
                            - GetPageRowCount());
                    break;
                case Keys.PageDown:
                    targetIndex = Math.Min(
                        visibleItems.Count - 1,
                        (currentIndex < 0 ? 0 : currentIndex)
                            + GetPageRowCount());
                    break;
                default:
                    return false;
            }
            SetSelection(
                visibleItems[targetIndex],
                true,
                true);
            return true;
        }

        private int GetPageRowCount()
        {
            return Math.Max(
                1,
                ClientSize.Height / ItemHeight - 1);
        }

        private void SetSelection(
            ProcessOutlineItem target,
            bool initiatedByUser,
            bool ensureVisible)
        {
            if (SameIdentity(selectedItem, target))
            {
                if (ensureVisible)
                {
                    EnsureItemVisible(target);
                }
                return;
            }

            ProcessOutlineItem previous = selectedItem;
            selectedItem = target;
            InvalidateItem(previous);
            InvalidateItem(target);
            if (ensureVisible)
            {
                EnsureItemVisible(target);
            }

            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            if (initiatedByUser
                && SameIdentity(selectedItem, target))
            {
                UserSelectionChanged?.Invoke(
                    this,
                    EventArgs.Empty);
            }
        }

        private void ToggleProcess(Guid procId)
        {
            if (!processItems.TryGetValue(
                    procId,
                    out ProcessOutlineItem process)
                || !process.HasChildren)
            {
                return;
            }

            ProcessOutlineScrollAnchor anchor = CaptureScrollAnchor();
            if (expandedProcIds.Contains(procId))
            {
                CollapseProcess(procId);
            }
            else
            {
                ExpandProcess(procId);
            }
            UpdateScrollExtent();
            RestoreScrollAnchor(anchor);
            Invalidate();
        }

        private void ExpandProcess(Guid procId)
        {
            if (!expandedProcIds.Add(procId))
            {
                return;
            }
            int processIndex = FindVisibleIndex(procId, Guid.Empty);
            if (processIndex < 0)
            {
                return;
            }

            int insertIndex = processIndex + 1;
            foreach (ProcessOutlineItem item in allItems)
            {
                if (!item.IsProcess && item.ProcId == procId)
                {
                    visibleItems.Insert(insertIndex++, item);
                }
            }
        }

        private void CollapseProcess(Guid procId)
        {
            if (!expandedProcIds.Remove(procId))
            {
                return;
            }
            for (int index = visibleItems.Count - 1;
                index >= 0;
                index--)
            {
                ProcessOutlineItem item = visibleItems[index];
                if (!item.IsProcess && item.ProcId == procId)
                {
                    visibleItems.RemoveAt(index);
                }
            }
        }

        private void RebuildVisibleItems()
        {
            visibleItems.Clear();
            foreach (ProcessOutlineItem item in allItems)
            {
                if (item.IsProcess
                    || expandedProcIds.Contains(item.ProcId))
                {
                    visibleItems.Add(item);
                }
            }
        }

        private ProcessOutlineItem ResolveSelection(
            Guid procId,
            Guid stepId)
        {
            if (procId == Guid.Empty
                || !processItems.TryGetValue(
                    procId,
                    out ProcessOutlineItem process))
            {
                return null;
            }
            if (stepId == Guid.Empty)
            {
                return process;
            }
            return stepItems.TryGetValue(
                    new ProcessOutlineStepIdentity(procId, stepId),
                    out ProcessOutlineItem step)
                    ? step
                    : process;
        }

        private int IndexFromPoint(Point location)
        {
            if (location.X < 0
                || location.X >= ClientSize.Width
                || location.Y < 0
                || location.Y >= ClientSize.Height)
            {
                return -1;
            }
            int index = (location.Y + GetScrollOffset())
                / ItemHeight;
            return index >= 0 && index < visibleItems.Count
                ? index
                : -1;
        }

        private int FindVisibleIndex(ProcessOutlineItem item)
        {
            if (item == null)
            {
                return -1;
            }
            return FindVisibleIndex(item.ProcId, item.StepId);
        }

        private int FindVisibleIndex(Guid procId, Guid stepId)
        {
            if (procId == Guid.Empty)
            {
                return -1;
            }
            for (int index = 0;
                index < visibleItems.Count;
                index++)
            {
                ProcessOutlineItem item = visibleItems[index];
                if (item.ProcId != procId)
                {
                    continue;
                }
                if (stepId == Guid.Empty && item.IsProcess)
                {
                    return index;
                }
                if (stepId != Guid.Empty
                    && item.StepId == stepId)
                {
                    return index;
                }
            }
            return -1;
        }

        private Rectangle GetItemRectangle(int index)
        {
            return new Rectangle(
                0,
                index * ItemHeight - GetScrollOffset(),
                ClientSize.Width,
                ItemHeight);
        }

        private void InvalidateItem(ProcessOutlineItem item)
        {
            int index = FindVisibleIndex(item);
            if (index < 0)
            {
                return;
            }
            Rectangle bounds = GetItemRectangle(index);
            if (bounds.IntersectsWith(ClientRectangle))
            {
                Invalidate(Rectangle.Intersect(
                    bounds,
                    ClientRectangle));
            }
        }

        private void EnsureItemVisible(ProcessOutlineItem item)
        {
            int index = FindVisibleIndex(item);
            if (index < 0 || ClientSize.Height <= 0)
            {
                return;
            }

            int viewportTop = GetScrollOffset();
            int viewportBottom = viewportTop + ClientSize.Height;
            int rowTop = index * ItemHeight;
            int rowBottom = rowTop + ItemHeight;
            if (rowTop < viewportTop)
            {
                SetScrollOffset(rowTop);
            }
            else if (rowBottom > viewportBottom)
            {
                SetScrollOffset(
                    rowBottom - ClientSize.Height);
            }
        }

        private ProcessOutlineScrollAnchor CaptureScrollAnchor()
        {
            int absoluteOffset = GetScrollOffset();
            if (visibleItems.Count == 0)
            {
                return new ProcessOutlineScrollAnchor(
                    Guid.Empty,
                    Guid.Empty,
                    0,
                    absoluteOffset);
            }
            int index = Math.Max(
                0,
                Math.Min(
                    visibleItems.Count - 1,
                    absoluteOffset / ItemHeight));
            ProcessOutlineItem item = visibleItems[index];
            return new ProcessOutlineScrollAnchor(
                item.ProcId,
                item.StepId,
                absoluteOffset - index * ItemHeight,
                absoluteOffset);
        }

        private void RestoreScrollAnchor(
            ProcessOutlineScrollAnchor anchor)
        {
            if (anchor.IsEmpty)
            {
                SetScrollOffset(anchor.AbsoluteOffset);
                return;
            }

            int index = FindVisibleIndex(
                anchor.ProcId,
                anchor.StepId);
            if (index < 0)
            {
                index = FindVisibleIndex(
                    anchor.ProcId,
                    Guid.Empty);
            }
            int offset = index >= 0
                ? index * ItemHeight + anchor.OffsetWithinRow
                : anchor.AbsoluteOffset;
            SetScrollOffset(offset);
        }

        private int GetScrollOffset()
        {
            return Math.Max(0, -AutoScrollPosition.Y);
        }

        private void SetScrollOffset(int offset)
        {
            int maximumOffset = Math.Max(
                0,
                visibleItems.Count * ItemHeight
                    - ClientSize.Height);
            int boundedOffset = Math.Max(
                0,
                Math.Min(offset, maximumOffset));
            if (GetScrollOffset() == boundedOffset)
            {
                return;
            }
            AutoScrollPosition = new Point(
                0,
                boundedOffset);
            Invalidate();
        }

        private void UpdateScrollExtent()
        {
            AutoScrollMinSize = new Size(
                0,
                visibleItems.Count * ItemHeight);
            VerticalScroll.SmallChange = ItemHeight;
            VerticalScroll.LargeChange = Math.Max(
                ItemHeight,
                ClientSize.Height - ItemHeight);
        }

        private static bool SameIdentity(
            ProcessOutlineItem first,
            ProcessOutlineItem second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }
            return first != null
                && second != null
                && first.ProcId == second.ProcId
                && first.StepId == second.StepId;
        }

        private bool IsVisuallySelected(ProcessOutlineItem item)
        {
            if (SameIdentity(item, selectedItem))
            {
                return true;
            }
            return item?.IsProcess == true
                && selectedItem != null
                && !selectedItem.IsProcess
                && selectedItem.ProcId == item.ProcId
                && !expandedProcIds.Contains(item.ProcId);
        }

        private static void DrawChevron(
            Graphics graphics,
            int centerX,
            int centerY,
            bool expanded,
            Color color)
        {
            using (var pen = new Pen(color, 1.2F))
            {
                if (expanded)
                {
                    graphics.DrawLine(
                        pen,
                        centerX - 2,
                        centerY - 1,
                        centerX,
                        centerY + 1);
                    graphics.DrawLine(
                        pen,
                        centerX,
                        centerY + 1,
                        centerX + 2,
                        centerY - 1);
                    return;
                }
                graphics.DrawLine(
                    pen,
                    centerX - 1,
                    centerY - 2,
                    centerX + 1,
                    centerY);
                graphics.DrawLine(
                    pen,
                    centerX + 1,
                    centerY,
                    centerX - 1,
                    centerY + 2);
            }
        }
    }
}
