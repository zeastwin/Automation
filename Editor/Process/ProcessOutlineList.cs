// 模块：编辑器 / 流程。
// 职责范围：以稳定 ID 驱动两级流程列表的展开、选择、滚动和无闪烁绘制。

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

    internal sealed class ProcessOutlineList : ListBox
    {
        private const int ToggleAreaWidth = 9;
        private const int RootImageLeft = 8;
        private const int StepIndent = 6;
        private const int ImageSize = 18;
        private const int TextGap = 2;
        private const int TextRightPadding = 8;

        private readonly List<ProcessOutlineItem> allItems =
            new List<ProcessOutlineItem>();
        private readonly HashSet<Guid> expandedProcIds =
            new HashSet<Guid>();
        private readonly Dictionary<Guid, ProcessOutlineItem> processItems =
            new Dictionary<Guid, ProcessOutlineItem>();
        private readonly Dictionary<Guid, ProcessOutlineItem> stepItems =
            new Dictionary<Guid, ProcessOutlineItem>();
        private bool suppressSelectionEvents;
        private bool userInteraction;

        public ProcessOutlineList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 25;
            IntegralHeight = false;
            BorderStyle = BorderStyle.None;
            SelectionMode = SelectionMode.One;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.AllPaintingInWmPaint,
                true);
            DoubleBuffered = true;
        }

        public event EventHandler UserSelectionChanged;

        public ImageList StateImages { get; set; }

        public Font ProcessFont { get; set; }

        public Font StepFont { get; set; }

        public ProcessOutlineItem SelectedOutlineItem =>
            SelectedItem as ProcessOutlineItem;

        public bool HasSelection => SelectedOutlineItem != null;

        public int VisibleItemCount => Items.Count;

        public IReadOnlyCollection<Guid> ExpandedProcIds =>
            expandedProcIds.ToArray();

        public Guid TopVisibleProcId
        {
            get
            {
                if (TopIndex < 0 || TopIndex >= Items.Count)
                {
                    return Guid.Empty;
                }
                return (Items[TopIndex] as ProcessOutlineItem)?.ProcId
                    ?? Guid.Empty;
            }
        }

        public void ReplaceItems(
            IEnumerable<ProcessOutlineItem> items,
            IEnumerable<Guid> expandedIds,
            Guid selectedProcId,
            Guid selectedStepId,
            Guid topProcId)
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
                    stepItems[item.StepId] = item;
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
                && stepItems.TryGetValue(selectedStepId, out ProcessOutlineItem selectedStep))
            {
                expandedProcIds.Add(selectedStep.ProcId);
            }

            RebuildVisibleItems(
                selectedProcId,
                selectedStepId,
                topProcId,
                false,
                false);
        }

        public bool SelectIdentity(
            Guid procId,
            Guid stepId,
            bool ensureVisible)
        {
            if (procId == Guid.Empty || !processItems.ContainsKey(procId))
            {
                ClearSelection();
                return false;
            }
            if (stepId != Guid.Empty)
            {
                if (!stepItems.TryGetValue(stepId, out ProcessOutlineItem step)
                    || step.ProcId != procId)
                {
                    stepId = Guid.Empty;
                }
                else if (!expandedProcIds.Contains(procId))
                {
                    expandedProcIds.Add(procId);
                    RebuildVisibleItems(
                        procId,
                        stepId,
                        TopVisibleProcId,
                        false,
                        false);
                }
            }

            int index = FindVisibleIndex(procId, stepId);
            if (index < 0)
            {
                return false;
            }
            SelectedIndex = index;
            if (ensureVisible)
            {
                TopIndex = Math.Max(0, Math.Min(index, Items.Count - 1));
            }
            return true;
        }

        public void ClearSelection()
        {
            SelectedIndex = -1;
        }

        public bool TryGetProcess(Guid procId, out ProcessOutlineItem item)
        {
            return processItems.TryGetValue(procId, out item);
        }

        public bool TryGetStep(Guid stepId, out ProcessOutlineItem item)
        {
            return stepItems.TryGetValue(stepId, out item);
        }

        public void InvalidateProcess(Guid procId)
        {
            for (int index = 0; index < Items.Count; index++)
            {
                if (Items[index] is ProcessOutlineItem item
                    && item.ProcId == procId)
                {
                    Invalidate(GetItemRectangle(index));
                }
            }
        }

        public ProcessOutlineItem GetVisibleItem(int index)
        {
            return index >= 0 && index < Items.Count
                ? Items[index] as ProcessOutlineItem
                : null;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count
                || !(Items[e.Index] is ProcessOutlineItem item))
            {
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color backgroundColor = selected ? UiPalette.Selection : BackColor;
            using (var backgroundBrush = new SolidBrush(backgroundColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            }
            if (selected)
            {
                using (var accentBrush = new SolidBrush(UiPalette.Brand))
                {
                    e.Graphics.FillRectangle(
                        accentBrush,
                        e.Bounds.Left,
                        e.Bounds.Top,
                        3,
                        e.Bounds.Height);
                }
            }

            if (item.IsProcess && item.HasChildren)
            {
                DrawChevron(
                    e.Graphics,
                    e.Bounds.Left + 4,
                    e.Bounds.Top + e.Bounds.Height / 2,
                    expandedProcIds.Contains(item.ProcId),
                    selected ? UiPalette.SelectionText : UiPalette.TextMuted);
            }

            int imageLeft = e.Bounds.Left
                + RootImageLeft
                + (item.IsProcess ? 0 : StepIndent);
            var imageBounds = new Rectangle(
                imageLeft,
                e.Bounds.Top + Math.Max(0, (e.Bounds.Height - ImageSize) / 2),
                ImageSize,
                ImageSize);
            if (StateImages != null
                && !string.IsNullOrEmpty(item.ImageKey)
                && StateImages.Images.ContainsKey(item.ImageKey))
            {
                e.Graphics.DrawImage(StateImages.Images[item.ImageKey], imageBounds);
            }

            int textLeft = imageBounds.Right + TextGap;
            var textBounds = new Rectangle(
                textLeft,
                e.Bounds.Top,
                Math.Max(
                    1,
                    ClientSize.Width - textLeft - TextRightPadding),
                e.Bounds.Height);
            Font font = item.IsProcess
                ? ProcessFont ?? Font
                : StepFont ?? Font;
            Color textColor = selected
                ? UiPalette.SelectionText
                : Enabled
                    ? item.ForeColor
                    : UiPalette.TextDisabled;
            TextRenderer.DrawText(
                e.Graphics,
                item.Text ?? string.Empty,
                font,
                textBounds,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            if (suppressSelectionEvents)
            {
                return;
            }
            base.OnSelectedIndexChanged(e);
            if (userInteraction)
            {
                UserSelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int index = IndexFromPoint(e.Location);
            userInteraction = true;
            try
            {
                if (index < 0)
                {
                    ClearSelection();
                    base.OnMouseDown(e);
                    return;
                }
                if (e.Button == MouseButtons.Right)
                {
                    SelectedIndex = index;
                }
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left
                    && e.X <= ToggleAreaWidth
                    && Items[index] is ProcessOutlineItem item
                    && item.IsProcess
                    && item.HasChildren)
                {
                    ToggleProcess(item.ProcId, true);
                }
            }
            finally
            {
                userInteraction = false;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || e.X <= ToggleAreaWidth)
            {
                return;
            }
            int index = IndexFromPoint(e.Location);
            if (index >= 0
                && Items[index] is ProcessOutlineItem item
                && item.IsProcess
                && item.HasChildren)
            {
                ToggleProcess(item.ProcId, true);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            ProcessOutlineItem selected = SelectedOutlineItem;
            if (selected != null)
            {
                if (e.KeyCode == Keys.Right
                    && selected.IsProcess
                    && selected.HasChildren
                    && !expandedProcIds.Contains(selected.ProcId))
                {
                    ToggleProcess(selected.ProcId, true);
                    e.Handled = true;
                    return;
                }
                if (e.KeyCode == Keys.Left)
                {
                    if (selected.IsProcess && expandedProcIds.Contains(selected.ProcId))
                    {
                        ToggleProcess(selected.ProcId, true);
                    }
                    else if (!selected.IsProcess)
                    {
                        userInteraction = true;
                        try
                        {
                            SelectIdentity(selected.ProcId, Guid.Empty, true);
                        }
                        finally
                        {
                            userInteraction = false;
                        }
                    }
                    e.Handled = true;
                    return;
                }
                if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                    && selected.IsProcess
                    && selected.HasChildren)
                {
                    ToggleProcess(selected.ProcId, true);
                    e.Handled = true;
                    return;
                }
            }

            userInteraction = true;
            try
            {
                base.OnKeyDown(e);
            }
            finally
            {
                userInteraction = false;
            }
        }

        private void ToggleProcess(Guid procId, bool initiatedByUser)
        {
            if (!processItems.TryGetValue(procId, out ProcessOutlineItem process)
                || !process.HasChildren)
            {
                return;
            }
            ProcessOutlineItem selected = SelectedOutlineItem;
            Guid selectedProcId = selected?.ProcId ?? procId;
            Guid selectedStepId = selected?.StepId ?? Guid.Empty;
            if (!expandedProcIds.Add(procId))
            {
                expandedProcIds.Remove(procId);
                if (selectedProcId == procId && selectedStepId != Guid.Empty)
                {
                    selectedStepId = Guid.Empty;
                }
            }
            RebuildVisibleItems(
                selectedProcId,
                selectedStepId,
                TopVisibleProcId,
                selected?.StepId != selectedStepId,
                initiatedByUser);
        }

        private void RebuildVisibleItems(
            Guid selectedProcId,
            Guid selectedStepId,
            Guid topProcId,
            bool notifySelectionChanged,
            bool initiatedByUser)
        {
            suppressSelectionEvents = true;
            BeginUpdate();
            try
            {
                Items.Clear();
                foreach (ProcessOutlineItem item in allItems)
                {
                    if (item.IsProcess || expandedProcIds.Contains(item.ProcId))
                    {
                        Items.Add(item);
                    }
                }
                int selectedIndex = FindVisibleIndex(selectedProcId, selectedStepId);
                if (selectedIndex < 0)
                {
                    selectedIndex = FindVisibleIndex(selectedProcId, Guid.Empty);
                }
                SelectedIndex = selectedIndex;

                int topIndex = FindVisibleIndex(topProcId, Guid.Empty);
                if (topIndex >= 0)
                {
                    TopIndex = topIndex;
                }
            }
            finally
            {
                EndUpdate();
                suppressSelectionEvents = false;
            }

            if (notifySelectionChanged)
            {
                base.OnSelectedIndexChanged(EventArgs.Empty);
                if (initiatedByUser)
                {
                    UserSelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            Invalidate();
        }

        private int FindVisibleIndex(Guid procId, Guid stepId)
        {
            if (procId == Guid.Empty)
            {
                return -1;
            }
            for (int index = 0; index < Items.Count; index++)
            {
                if (!(Items[index] is ProcessOutlineItem item)
                    || item.ProcId != procId)
                {
                    continue;
                }
                if (stepId == Guid.Empty && item.IsProcess)
                {
                    return index;
                }
                if (stepId != Guid.Empty && item.StepId == stepId)
                {
                    return index;
                }
            }
            return -1;
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
                    graphics.DrawLine(pen, centerX - 2, centerY - 1, centerX, centerY + 1);
                    graphics.DrawLine(pen, centerX, centerY + 1, centerX + 2, centerY - 1);
                    return;
                }
                graphics.DrawLine(pen, centerX - 1, centerY - 2, centerX + 1, centerY);
                graphics.DrawLine(pen, centerX + 1, centerY, centerX - 1, centerY + 2);
            }
        }
    }
}
