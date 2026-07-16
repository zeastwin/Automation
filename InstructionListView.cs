using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    /// <summary>
    /// 指令列表的轻量虚拟视图。仅创建可见项，整行由本控件绘制，
    /// 避免 DataGridView 的单元格边框、焦点框和大量样式对象。
    /// </summary>
    public sealed class InstructionListView : ListView
    {
        private readonly ImageList rowHeightImages;
        private readonly Bitmap rowHeightBitmap;
        private readonly Font contentFont;
        private readonly Font headerFont;
        private readonly Dictionary<int, Color> rowBackColors = new Dictionary<int, Color>();
        private Color allRowsBackColor = Color.Empty;
        private BindingSource bindingSource;
        private IList operationSource;
        private bool noteColumnVisible = true;
        private int runtimeIndex = -1;
        private ProcRunState runtimeState = ProcRunState.Stopped;
        private bool runtimeBreakpoint;
        private bool pulseVisible;
        private readonly Timer pulseTimer;

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
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            GridLines = false;
            BorderStyle = BorderStyle.None;
            AllowDrop = true;
            contentFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            headerFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            Font = contentFont;
            BackColor = Color.White;
            ForeColor = Color.FromArgb(39, 52, 61);
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            base.Columns.Add(string.Empty, 54, HorizontalAlignment.Center);
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

            pulseTimer = new Timer { Interval = 520 };
            pulseTimer.Tick += PulseTimer_Tick;
            pulseTimer.Start();
        }

        [Browsable(false)]
        public object DataSource
        {
            get => bindingSource ?? (object)operationSource;
            set
            {
                if (ReferenceEquals(value, bindingSource) || ReferenceEquals(value, operationSource))
                {
                    RefreshOperations();
                    return;
                }
                ClearSelection();
                DetachBindingSource();
                rowBackColors.Clear();
                allRowsBackColor = Color.Empty;
                runtimeIndex = -1;
                runtimeState = ProcRunState.Stopped;
                runtimeBreakpoint = false;
                bindingSource = value as BindingSource;
                if (bindingSource != null)
                {
                    bindingSource.ListChanged += BindingSource_ListChanged;
                    operationSource = bindingSource.List;
                }
                else
                {
                    operationSource = value as IList;
                }
                RefreshOperations();
            }
        }

        [Browsable(false)]
        public int OperationCount => operationSource?.Count ?? 0;

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

        public void ClearSelection()
        {
            foreach (int index in SelectedIndices.Cast<int>().ToArray())
            {
                Items[index].Selected = false;
            }
            if (FocusedItem != null)
            {
                FocusedItem.Focused = false;
            }
            Invalidate();
        }

        public void SelectSingle(int index)
        {
            if (index < 0 || index >= OperationCount)
            {
                ClearSelection();
                return;
            }
            ClearSelection();
            Items[index].Selected = true;
            Items[index].Focused = true;
            InvalidateRow(index);
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
            runtimeState = ProcRunState.Stopped;
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
                pulseTimer.Stop();
                pulseTimer.Tick -= PulseTimer_Tick;
                pulseTimer.Dispose();
                rowHeightImages.Dispose();
                headerFont.Dispose();
                contentFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void BindingSource_ListChanged(object sender, ListChangedEventArgs e)
        {
            operationSource = bindingSource?.List;
            RefreshOperations();
        }

        private void DetachBindingSource()
        {
            if (bindingSource != null)
            {
                bindingSource.ListChanged -= BindingSource_ListChanged;
                bindingSource = null;
            }
        }

        private void RefreshOperations()
        {
            int selectedIndex = CurrentIndex;
            VirtualListSize = OperationCount;
            RecalculateColumnWidths();
            rowBackColors.Keys.Where(index => index >= OperationCount).ToList()
                .ForEach(index => rowBackColors.Remove(index));
            if (runtimeIndex >= OperationCount)
            {
                ClearRuntimeState();
            }
            if (selectedIndex >= 0 && selectedIndex < OperationCount)
            {
                SelectSingle(selectedIndex);
            }
            Invalidate();
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
            int available = Math.Max(240, ClientSize.Width - 54 - 68 - verticalScrollWidth - 2);
            base.Columns[0].Width = 54;
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

        private void PulseTimer_Tick(object sender, EventArgs e)
        {
            pulseVisible = !pulseVisible;
            if (runtimeIndex >= 0
                && (runtimeState == ProcRunState.Running || runtimeState == ProcRunState.SingleStep))
            {
                InvalidateRow(runtimeIndex);
            }
        }

        private void InstructionListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            OperationType operation = GetOperation(e.ItemIndex);
            var item = new ListViewItem(string.Empty)
            {
                Tag = operation,
                ToolTipText = BuildToolTip(operation, e.ItemIndex)
            };
            item.SubItems.Add(operation?.Num.ToString() ?? e.ItemIndex.ToString());
            item.SubItems.Add(operation?.Name ?? string.Empty);
            item.SubItems.Add(operation?.OperaType ?? string.Empty);
            item.SubItems.Add(operation?.Note ?? string.Empty);
            e.Item = item;
        }

        private string BuildToolTip(OperationType operation, int index)
        {
            if (operation == null)
            {
                return string.Empty;
            }
            var descriptions = new List<string>();
            if (operation.Disable)
            {
                descriptions.Add("该指令已禁用，运行时不会执行");
            }
            if (operation.isStopPoint)
            {
                descriptions.Add("该指令设有断点");
            }
            if (index == runtimeIndex)
            {
                descriptions.Add($"当前状态：{GetStateText(GetRuntimeVisualState())}");
            }
            return string.Join("；", descriptions);
        }

        private void InstructionListView_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush background = new SolidBrush(Color.FromArgb(242, 246, 248)))
            using (SolidBrush separator = new SolidBrush(Color.FromArgb(220, 227, 231)))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
                e.Graphics.FillRectangle(separator, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Width, 1);
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
                Color.FromArgb(67, 82, 92),
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
            Color backColor = ResolveRowBackColor(operation, index, selected, isRuntime);
            Color foreColor = operation?.Disable == true && !hasTransientColor
                ? Color.FromArgb(244, 246, 247)
                : Color.FromArgb(39, 52, 61);

            using (SolidBrush background = new SolidBrush(backColor))
            using (SolidBrush separator = new SolidBrush(operation?.Disable == true
                ? Color.FromArgb(118, 128, 134)
                : Color.FromArgb(235, 239, 242)))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
                e.Graphics.FillRectangle(separator, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Width, 1);
            }

            if (e.ColumnIndex == 0)
            {
                DrawStatusCell(e.Graphics, e.Bounds, operation, index, isRuntime, selected);
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
                return selected ? Color.FromArgb(72, 81, 87) : Color.FromArgb(91, 101, 107);
            }
            if (isRuntime)
            {
                return GetRuntimeRowBackColor(runtimeState, runtimeBreakpoint);
            }
            if (selected)
            {
                return Color.FromArgb(218, 239, 248);
            }
            return index % 2 == 0 ? Color.White : Color.FromArgb(250, 252, 253);
        }

        private void DrawStatusCell(
            Graphics graphics,
            Rectangle bounds,
            OperationType operation,
            int index,
            bool isRuntime,
            bool selected)
        {
            VisualState state = operation?.Disable == true
                ? VisualState.Disabled
                : isRuntime ? GetRuntimeVisualState()
                : operation?.isStopPoint == true ? VisualState.Breakpoint
                : VisualState.None;
            GraphicsState savedState = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            Color railColor = operation?.Disable == true
                ? Color.FromArgb(126, 137, 143)
                : Color.FromArgb(208, 220, 227);
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
            if (isRuntime && state != VisualState.None)
            {
                using (SolidBrush accentBrush = new SolidBrush(GetStateColor(state)))
                {
                    graphics.FillRectangle(accentBrush, bounds.Left, bounds.Top + 2, 3, Math.Max(1, bounds.Height - 4));
                }
            }
            if (state == VisualState.None)
            {
                DrawNeutralStateNode(graphics, centerX, centerY, selected);
                graphics.Restore(savedState);
                return;
            }

            Rectangle iconBounds = new Rectangle(
                bounds.Left + (bounds.Width - 24) / 2,
                bounds.Top + (bounds.Height - 24) / 2,
                24,
                24);
            DrawStateIcon(
                graphics,
                iconBounds,
                state,
                GetStateColor(state),
                GetStateBackColor(state),
                isRuntime && pulseVisible);
            if (operation?.isStopPoint == true && state != VisualState.Breakpoint)
            {
                using (SolidBrush markerBrush = new SolidBrush(Color.FromArgb(205, 58, 68)))
                using (Pen markerBorder = new Pen(Color.White, 1.5F))
                {
                    Rectangle marker = new Rectangle(iconBounds.Right - 6, iconBounds.Top - 1, 8, 8);
                    graphics.FillEllipse(markerBrush, marker);
                    graphics.DrawEllipse(markerBorder, marker);
                }
            }
            graphics.Restore(savedState);
        }

        private static void DrawStatusHeader(Graphics graphics, Rectangle bounds)
        {
            GraphicsState savedState = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            using (Pen linePen = new Pen(Color.FromArgb(166, 183, 193), 1.3F))
            using (SolidBrush nodeBrush = new SolidBrush(Color.FromArgb(116, 141, 154)))
            {
                graphics.DrawLine(linePen, centerX, centerY - 7, centerX, centerY + 7);
                graphics.FillEllipse(nodeBrush, centerX - 2, centerY - 9, 4, 4);
                graphics.FillEllipse(nodeBrush, centerX - 3, centerY - 3, 6, 6);
                graphics.FillEllipse(nodeBrush, centerX - 2, centerY + 5, 4, 4);
            }
            graphics.Restore(savedState);
        }

        private static void DrawNeutralStateNode(Graphics graphics, int centerX, int centerY, bool selected)
        {
            Color borderColor = selected
                ? Color.FromArgb(42, 139, 190)
                : Color.FromArgb(151, 171, 182);
            Color fillColor = selected
                ? Color.FromArgb(225, 243, 251)
                : Color.White;
            using (SolidBrush fillBrush = new SolidBrush(fillColor))
            using (Pen borderPen = new Pen(borderColor, selected ? 1.8F : 1.4F))
            using (SolidBrush centerBrush = new SolidBrush(borderColor))
            {
                Rectangle nodeBounds = new Rectangle(centerX - 5, centerY - 5, 10, 10);
                graphics.FillEllipse(fillBrush, nodeBounds);
                graphics.DrawEllipse(borderPen, nodeBounds);
                graphics.FillEllipse(centerBrush, centerX - 1, centerY - 1, 3, 3);
            }
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
                return Color.FromArgb(255, 241, 242);
            }
            switch (state)
            {
                case ProcRunState.Running:
                    return Color.FromArgb(235, 248, 241);
                case ProcRunState.Paused:
                case ProcRunState.Pausing:
                    return Color.FromArgb(255, 247, 229);
                case ProcRunState.SingleStep:
                    return Color.FromArgb(234, 245, 252);
                case ProcRunState.Alarming:
                    return Color.FromArgb(253, 235, 237);
                case ProcRunState.Stopping:
                    return Color.FromArgb(250, 237, 237);
                default:
                    return Color.FromArgb(240, 246, 249);
            }
        }

        private static string GetStateText(VisualState state)
        {
            switch (state)
            {
                case VisualState.Running: return "执行中";
                case VisualState.Paused: return "已暂停";
                case VisualState.SingleStep: return "单步";
                case VisualState.Breakpoint: return "断点";
                case VisualState.Alarming: return "报警";
                case VisualState.Stopping: return "停止中";
                case VisualState.Disabled: return "已禁用";
                default: return string.Empty;
            }
        }

        private static Color GetStateColor(VisualState state)
        {
            switch (state)
            {
                case VisualState.Running: return Color.FromArgb(37, 145, 99);
                case VisualState.Paused: return Color.FromArgb(190, 124, 24);
                case VisualState.SingleStep: return Color.FromArgb(42, 126, 180);
                case VisualState.Breakpoint: return Color.FromArgb(196, 52, 63);
                case VisualState.Alarming: return Color.FromArgb(190, 47, 54);
                case VisualState.Stopping: return Color.FromArgb(159, 62, 62);
                case VisualState.Disabled: return Color.FromArgb(241, 244, 245);
                default: return Color.FromArgb(128, 143, 154);
            }
        }

        private static Color GetStateBackColor(VisualState state)
        {
            switch (state)
            {
                case VisualState.Running: return Color.FromArgb(229, 245, 237);
                case VisualState.Paused: return Color.FromArgb(255, 244, 221);
                case VisualState.SingleStep: return Color.FromArgb(231, 243, 251);
                case VisualState.Breakpoint:
                case VisualState.Alarming: return Color.FromArgb(253, 234, 236);
                case VisualState.Stopping: return Color.FromArgb(249, 235, 235);
                case VisualState.Disabled: return Color.FromArgb(74, 84, 90);
                default: return Color.FromArgb(242, 245, 247);
            }
        }

        private static void DrawStateIcon(
            Graphics graphics,
            Rectangle bounds,
            VisualState state,
            Color accent,
            Color backColor,
            bool pulse)
        {
            Rectangle shadowBounds = bounds;
            shadowBounds.Offset(0, 1);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(22, 20, 36, 44)))
            using (SolidBrush background = new SolidBrush(backColor))
            using (Pen outline = new Pen(Color.FromArgb(72, accent), 1F))
            {
                graphics.FillEllipse(shadow, shadowBounds);
                graphics.FillEllipse(background, bounds);
                graphics.DrawEllipse(outline, bounds);
            }
            Rectangle glyph = new Rectangle(bounds.Left + 6, bounds.Top + 6, 12, 12);
            if (pulse)
            {
                using (Pen halo = new Pen(Color.FromArgb(72, accent), 2F))
                {
                    graphics.DrawEllipse(halo, Rectangle.Inflate(bounds, 2, 2));
                }
            }
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
