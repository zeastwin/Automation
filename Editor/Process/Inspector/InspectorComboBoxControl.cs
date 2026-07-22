// 模块：编辑器 / 流程 / Inspector。
// 职责范围：指令属性定义、编辑控件、选择器和值转换。

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class InspectorComboBox : ComboBox
    {
        private const int CbSetItemHeight = 0x0153;
        private const int CbShowDropDown = 0x014F;
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmLeftButtonDoubleClick = 0x0203;
        private const int SelectionPickerArrowWidth = 28;
        private readonly InspectorComboArrow dropDownButton = new InspectorComboArrow();
        private bool selectionPickerRequestPending;
        private bool standardValueDropDownRequestPending;
        private InstantToolStripDropDown activeStandardValueDropDown;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

        public InspectorComboBox()
        {
            BackColor = UiPalette.Input;
            ForeColor = UiPalette.TextPrimary;
            FlatStyle = FlatStyle.Flat;
            Font = InspectorFonts.Regular9;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 22;
            dropDownButton.AccessibleName = "展开选项";
            dropDownButton.Click += (sender, args) => OpenDropDownFromArrow();
            Controls.Add(dropDownButton);
        }

        internal bool UseSelectionPicker { get; set; }

        internal event EventHandler SelectionPickerRequested;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SendMessage(Handle, CbSetItemHeight, new IntPtr(-1), new IntPtr(22));
            LayoutDropDownButton();
            dropDownButton.BringToFront();
            ClearTextSelection();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutDropDownButton();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool selectionField = (e.State & DrawItemState.ComboBoxEdit)
                == DrawItemState.ComboBoxEdit;
            Color backColor = selectionField
                ? (Enabled ? UiPalette.Input : UiPalette.SurfaceSubtle)
                : (selected ? UiPalette.BrandSoft : UiPalette.Surface);
            Color foreColor = Enabled ? UiPalette.TextPrimary : UiPalette.TextDisabled;
            using (var brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            TextRenderer.DrawText(
                e.Graphics,
                GetItemText(Items[e.Index]),
                Font,
                new Rectangle(
                    e.Bounds.X + 7,
                    e.Bounds.Y,
                    Math.Max(1, e.Bounds.Width - 9),
                    Math.Max(1, e.Bounds.Height)),
                foreColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);
            if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
            {
                e.DrawFocusRectangle();
            }
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            BackColor = UiPalette.Input;
            Invalidate();
            ClearTextSelection();
        }

        protected override void OnLeave(EventArgs e)
        {
            ClearTextSelection();
            BackColor = UiPalette.Input;
            base.OnLeave(e);
            Invalidate();
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            base.OnDropDownClosed(e);
            ClearTextSelection();
        }

        protected override void OnDropDown(EventArgs e)
        {
            if (DroppedDown)
            {
                DroppedDown = false;
            }
            QueueDropDownRequest();
        }

        protected override void OnSelectionChangeCommitted(EventArgs e)
        {
            base.OnSelectionChangeCommitted(e);
            ClearTextSelection();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (SelectionLength == (Text?.Length ?? 0))
            {
                ClearTextSelection();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled)
            {
                activeStandardValueDropDown?.Close();
            }
            BackColor = Enabled ? UiPalette.Input : UiPalette.SurfaceSubtle;
            ForeColor = Enabled ? UiPalette.TextPrimary : UiPalette.TextDisabled;
            base.OnEnabledChanged(e);
            dropDownButton.Invalidate();
            Invalidate();
        }

        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (message.Msg == CbShowDropDown && message.WParam != IntPtr.Zero)
            {
                message.Result = IntPtr.Zero;
                QueueDropDownRequest();
                return;
            }
            if ((message.Msg == WmLeftButtonDown
                    || message.Msg == WmLeftButtonDoubleClick)
                && ShouldOpenDropDownFromMouse(message.LParam))
            {
                Focus();
                message.Result = IntPtr.Zero;
                QueueDropDownRequest();
                return;
            }
            if ((message.Msg == WmKeyDown || message.Msg == WmSysKeyDown)
                && ShouldOpenDropDownFromKeyboard(message))
            {
                message.Result = IntPtr.Zero;
                QueueDropDownRequest();
                return;
            }
            base.WndProc(ref message);
        }

        private void OpenDropDownFromArrow()
        {
            if (!Enabled || IsDisposed)
            {
                return;
            }
            Focus();
            QueueDropDownRequest();
        }

        private void LayoutDropDownButton()
        {
            if (dropDownButton.IsDisposed)
            {
                return;
            }
            dropDownButton.SetBounds(
                Math.Max(0, ClientSize.Width - 25),
                1,
                24,
                Math.Max(1, ClientSize.Height - 2));
        }

        private bool CanOpenSelectionPicker()
        {
            return UseSelectionPicker
                && SelectionPickerRequested != null
                && Enabled
                && !IsDisposed;
        }

        private bool ShouldOpenDropDownFromMouse(IntPtr position)
        {
            if (DropDownStyle == ComboBoxStyle.DropDownList)
            {
                return true;
            }
            int x = unchecked((short)(position.ToInt64() & 0xFFFF));
            return x >= Math.Max(0, ClientSize.Width - SelectionPickerArrowWidth);
        }

        private static bool ShouldOpenDropDownFromKeyboard(
            System.Windows.Forms.Message message)
        {
            Keys key = (Keys)message.WParam.ToInt32();
            return key == Keys.F4
                || message.Msg == WmSysKeyDown && key == Keys.Down;
        }

        private void QueueSelectionPickerRequest()
        {
            if (selectionPickerRequestPending || !IsHandleCreated || IsDisposed)
            {
                return;
            }
            selectionPickerRequestPending = true;
            BeginInvoke((Action)(() =>
            {
                selectionPickerRequestPending = false;
                if (!IsDisposed && CanOpenSelectionPicker())
                {
                    SelectionPickerRequested?.Invoke(this, EventArgs.Empty);
                }
            }));
        }

        private void QueueDropDownRequest()
        {
            if (CanOpenSelectionPicker())
            {
                QueueSelectionPickerRequest();
                return;
            }
            if (standardValueDropDownRequestPending || !IsHandleCreated || IsDisposed)
            {
                return;
            }
            standardValueDropDownRequestPending = true;
            BeginInvoke((Action)(() =>
            {
                standardValueDropDownRequestPending = false;
                if (!IsDisposed && Enabled)
                {
                    ShowStandardValueDropDown();
                }
            }));
        }

        private void ShowStandardValueDropDown()
        {
            activeStandardValueDropDown?.Close();
            base.OnDropDown(EventArgs.Empty);
            if (Items.Count == 0 || IsDisposed || !Visible)
            {
                return;
            }

            Rectangle anchorBounds = RectangleToScreen(ClientRectangle);
            Rectangle workingArea = Screen.FromRectangle(anchorBounds).WorkingArea;
            int itemHeight = Math.Max(22, ItemHeight);
            int desiredHeight = itemHeight * Items.Count + 2;
            int maximumHeight = Math.Max(
                itemHeight + 2,
                Math.Min(
                    DropDownHeight > 0 ? DropDownHeight : 320,
                    workingArea.Height - 24));
            int height = Math.Min(desiredHeight, maximumHeight);
            int measuredWidth = Items.Cast<object>()
                .Select(item => TextRenderer.MeasureText(
                    GetItemText(item),
                    Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width)
                .DefaultIfEmpty(0)
                .Max() + 18;
            if (desiredHeight > height)
            {
                measuredWidth += SystemInformation.VerticalScrollBarWidth;
            }
            int width = Math.Min(
                Math.Max(120, workingArea.Width - 24),
                Math.Max(Width, measuredWidth));

            var list = new InspectorStandardValueListBox(GetItemText)
            {
                CausesValidation = false,
                Font = Font,
                ItemHeight = itemHeight,
                Size = new Size(width, height)
            };
            foreach (object item in Items)
            {
                list.Items.Add(item);
            }
            list.SelectedIndex = SelectedIndex;

            var host = new ToolStripControlHost(list)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = list.Size
            };
            var dropDown = new InstantToolStripDropDown
            {
                AutoClose = true,
                AutoSize = false,
                BackColor = UiPalette.Surface,
                CausesValidation = false,
                DropShadowEnabled = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Renderer = new BorderlessDropDownRenderer(),
                Size = list.Size
            };
            dropDown.Items.Add(host);

            void CommitSelection()
            {
                int selectedIndex = list.SelectedIndex;
                if (selectedIndex < 0 || selectedIndex >= Items.Count)
                {
                    return;
                }
                SelectedIndex = selectedIndex;
                OnSelectionChangeCommitted(EventArgs.Empty);
                if (!dropDown.IsDisposed)
                {
                    dropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
                }
            }

            list.SelectionAccepted += CommitSelection;
            list.CancelRequested += () =>
                dropDown.Close(ToolStripDropDownCloseReason.Keyboard);
            dropDown.Closed += (sender, args) =>
            {
                if (ReferenceEquals(activeStandardValueDropDown, dropDown))
                {
                    activeStandardValueDropDown = null;
                }
                OnDropDownClosed(EventArgs.Empty);
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(new Action(dropDown.Dispose));
                }
                else
                {
                    dropDown.Dispose();
                }
            };
            activeStandardValueDropDown = dropDown;
            dropDown.ShowInstant(
                this,
                new Point(Math.Min(0, Width - width), Height),
                list);
            list.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                activeStandardValueDropDown?.Dispose();
                activeStandardValueDropDown = null;
            }
            base.Dispose(disposing);
        }

        internal void ClearTextSelection()
        {
            if (DropDownStyle == ComboBoxStyle.DropDownList || IsDisposed)
            {
                return;
            }
            SelectionStart = Text?.Length ?? 0;
            SelectionLength = 0;
        }

        private sealed class InspectorStandardValueListBox : ListBox
        {
            private readonly Func<object, string> getItemText;

            public InspectorStandardValueListBox(Func<object, string> getItemText)
            {
                this.getItemText = getItemText
                    ?? throw new ArgumentNullException(nameof(getItemText));
                BackColor = UiPalette.Surface;
                BorderStyle = BorderStyle.FixedSingle;
                DrawMode = DrawMode.OwnerDrawFixed;
                ForeColor = UiPalette.TextPrimary;
                IntegralHeight = false;
                TabStop = true;
            }

            public event Action SelectionAccepted;
            public event Action CancelRequested;

            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= Items.Count)
                {
                    return;
                }
                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using (var brush = new SolidBrush(
                    selected ? UiPalette.BrandSoft : UiPalette.Surface))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }
                TextRenderer.DrawText(
                    e.Graphics,
                    getItemText(Items[e.Index]),
                    Font,
                    new Rectangle(
                        e.Bounds.X + 7,
                        e.Bounds.Y,
                        Math.Max(1, e.Bounds.Width - 10),
                        e.Bounds.Height),
                    ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPadding);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int index = IndexFromPoint(e.Location);
                if (index >= 0 && index < Items.Count && SelectedIndex != index)
                {
                    SelectedIndex = index;
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Left && IndexFromPoint(e.Location) >= 0)
                {
                    SelectionAccepted?.Invoke();
                }
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    SelectionAccepted?.Invoke();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Escape
                    || e.KeyCode == Keys.F4
                    || e.Alt && e.KeyCode == Keys.Up)
                {
                    CancelRequested?.Invoke();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
                base.OnKeyDown(e);
            }
        }
    }

}
