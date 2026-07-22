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
    internal sealed class InspectorValueCell : Control
    {
        private string displayText = string.Empty;
        private bool editable;
        private bool showDropDownArrow;
        private bool pointerOver;
        private bool pointerDown;

        public InspectorValueCell()
        {
            BackColor = UiPalette.Surface;
            Cursor = Cursors.Default;
            Font = InspectorFonts.Regular9;
            ForeColor = UiPalette.TextPrimary;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable
                | ControlStyles.UserPaint,
                true);
        }

        public string DisplayText
        {
            get => displayText;
            set
            {
                string next = value ?? string.Empty;
                if (string.Equals(displayText, next, StringComparison.Ordinal))
                {
                    return;
                }
                displayText = next;
                Invalidate();
            }
        }

        public bool Editable
        {
            get => editable;
            set
            {
                if (editable == value)
                {
                    return;
                }
                editable = value;
                TabStop = value;
                Cursor = value
                    ? ShowDropDownArrow ? Cursors.Hand : Cursors.IBeam
                    : Cursors.Default;
                if (!value)
                {
                    pointerOver = false;
                    pointerDown = false;
                }
                Invalidate();
            }
        }

        public bool ShowDropDownArrow
        {
            get => showDropDownArrow;
            set
            {
                if (showDropDownArrow == value)
                {
                    return;
                }
                showDropDownArrow = value;
                Cursor = Editable && value ? Cursors.Hand : Editable ? Cursors.IBeam : Cursors.Default;
                Invalidate();
            }
        }

        public event EventHandler ActivationRequested;

        protected override void OnMouseEnter(EventArgs e)
        {
            pointerOver = Editable;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            pointerOver = false;
            pointerDown = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Editable && e.Button == MouseButtons.Left)
            {
                pointerDown = true;
                Focus();
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool activate = Editable
                && pointerDown
                && e.Button == MouseButtons.Left
                && ClientRectangle.Contains(e.Location);
            pointerDown = false;
            Invalidate();
            base.OnMouseUp(e);
            if (activate)
            {
                ActivationRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Editable && (e.KeyCode == Keys.Enter
                    || e.KeyCode == Keys.Space
                    || e.KeyCode == Keys.F2
                    || e.Alt && e.KeyCode == Keys.Down))
            {
                ActivationRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = pointerDown
                ? UiPalette.SurfacePressed
                : pointerOver ? UiPalette.SurfaceHover : UiPalette.Surface;
            e.Graphics.Clear(background);

            int arrowWidth = ShowDropDownArrow ? 26 : 0;
            TextRenderer.DrawText(
                e.Graphics,
                string.IsNullOrEmpty(DisplayText) ? "未设置" : DisplayText,
                Font,
                new Rectangle(7, 0, Math.Max(1, Width - 11 - arrowWidth), Height),
                string.IsNullOrEmpty(DisplayText) ? UiPalette.TextDisabled : ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);

            if (ShowDropDownArrow)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float centerX = Width - 13F;
                float centerY = Height / 2F;
                using (var pen = new Pen(
                    Editable ? UiPalette.TextMuted : UiPalette.TextDisabled,
                    1.2F)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                })
                {
                    e.Graphics.DrawLine(
                        pen,
                        centerX - 3F,
                        centerY - 1.5F,
                        centerX,
                        centerY + 1.5F);
                    e.Graphics.DrawLine(
                        pen,
                        centerX,
                        centerY + 1.5F,
                        centerX + 3F,
                        centerY - 1.5F);
                }
            }

            if (Focused && ShowFocusCues && Editable)
            {
                using (var pen = new Pen(UiPalette.Focus))
                {
                    e.Graphics.DrawRectangle(
                        pen,
                        0,
                        0,
                        Math.Max(0, Width - 1),
                        Math.Max(0, Height - 1));
                }
            }
        }
    }

    internal sealed class InspectorTextBox : TextBox
    {
        private const int EmSetMargins = 0xD3;
        private const int EmSetRectNp = 0xB4;
        private const int EcLeftMargin = 0x1;
        private const int EcRightMargin = 0x2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        private static extern IntPtr SendMessageRectangle(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            ref NativeRectangle rectangle);

        public InspectorTextBox()
        {
            AutoSize = false;
            BorderStyle = BorderStyle.None;
            BackColor = UiPalette.Input;
            ForeColor = UiPalette.TextPrimary;
            Font = InspectorFonts.Regular9;
            Multiline = true;
            AcceptsReturn = false;
            WordWrap = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int margins = 6 | (6 << 16);
            SendMessage(
                Handle,
                EmSetMargins,
                new IntPtr(EcLeftMargin | EcRightMargin),
                new IntPtr(margins));
            UpdateFormattingRectangle();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateFormattingRectangle();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateFormattingRectangle();
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            BackColor = UiPalette.InputFocused;
            Invalidate();
        }

        protected override void OnLeave(EventArgs e)
        {
            BackColor = ReadOnly ? UiPalette.SurfaceSubtle : UiPalette.Input;
            base.OnLeave(e);
            Invalidate();
        }

        protected override void OnReadOnlyChanged(EventArgs e)
        {
            BackColor = ReadOnly ? UiPalette.SurfaceSubtle : UiPalette.Input;
            ForeColor = ReadOnly ? UiPalette.TextSecondary : UiPalette.TextPrimary;
            base.OnReadOnlyChanged(e);
            Invalidate();
        }

        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == 0x000F || message.Msg == 0x0085)
                && IsHandleCreated && Width > 2 && Height > 2)
            {
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                {
                    InspectorShapes.DrawRoundedBorder(
                        graphics,
                        new Rectangle(0, 0, Width - 1, Height - 1),
                        4,
                        Focused && !ReadOnly ? UiPalette.Brand : UiPalette.Stroke);
                }
            }
        }

        private void UpdateFormattingRectangle()
        {
            if (!IsHandleCreated || ClientSize.Width <= 12 || ClientSize.Height <= 4)
            {
                return;
            }
            int textHeight = TextRenderer.MeasureText(
                "中Ag",
                Font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
            int top = Math.Min(
                ClientSize.Height - textHeight - 1,
                Math.Max(1, (ClientSize.Height - textHeight) / 2));
            var rectangle = new NativeRectangle
            {
                Left = 6,
                Top = top,
                Right = Math.Max(7, ClientSize.Width - 6),
                Bottom = Math.Min(ClientSize.Height - 1, top + textHeight + 1)
            };
            SendMessageRectangle(Handle, EmSetRectNp, IntPtr.Zero, ref rectangle);
        }
    }

}
