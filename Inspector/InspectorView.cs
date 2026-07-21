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
    internal static class InspectorShapes
    {
        public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 1 || bounds.Height <= 1 || radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawRoundedBorder(
            Graphics graphics,
            Rectangle bounds,
            int radius,
            Color color)
        {
            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                return;
            }
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = CreateRoundedPath(bounds, radius))
            using (var pen = new Pen(color))
            {
                graphics.DrawPath(pen, path);
            }
        }
    }

    internal enum InspectorIconKind
    {
        None,
        Process,
        Step,
        Operation,
        Settings,
        Timing,
        Run,
        Warning,
        InputOutput,
        Motion,
        Communication,
        Data,
        Add,
        Save,
        Cancel,
        Edit,
        MoveUp,
        MoveDown,
        Delete
    }

    internal static class InspectorIcons
    {
        public static InspectorIconKind FromSectionTitle(string title)
        {
            string value = title ?? string.Empty;
            if (value.Contains("异常") || value.Contains("报警"))
            {
                return InspectorIconKind.Warning;
            }
            if (value.Contains("超时") || value.Contains("延时") || value.Contains("等待"))
            {
                return InspectorIconKind.Timing;
            }
            if (value.Contains("运行") || value.Contains("执行") || value.Contains("调试"))
            {
                return InspectorIconKind.Run;
            }
            if (value.IndexOf("IO", StringComparison.OrdinalIgnoreCase) >= 0
                || value.Contains("输入") || value.Contains("输出"))
            {
                return InspectorIconKind.InputOutput;
            }
            if (value.Contains("运动") || value.Contains("轴")
                || value.Contains("速度") || value.Contains("位置")
                || value.Contains("工站") || value.Contains("料盘"))
            {
                return InspectorIconKind.Motion;
            }
            if (value.Contains("通讯") || value.Contains("串口")
                || value.Contains("网络") || value.Contains("PLC"))
            {
                return InspectorIconKind.Communication;
            }
            if (value.Contains("数据") || value.Contains("变量")
                || value.Contains("文本") || value.Contains("结构"))
            {
                return InspectorIconKind.Data;
            }
            if (value.Contains("流程") || value.Contains("跳转"))
            {
                return InspectorIconKind.Process;
            }
            return InspectorIconKind.Settings;
        }

        public static void Draw(
            Graphics graphics,
            Rectangle bounds,
            InspectorIconKind kind,
            Color color)
        {
            if (graphics == null || bounds.Width < 4 || bounds.Height < 4
                || kind == InspectorIconKind.None)
            {
                return;
            }

            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 16F, bounds.Height / 16F);
            using (var pen = new Pen(color, 1.45F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            })
            using (var brush = new SolidBrush(color))
            {
                switch (kind)
                {
                    case InspectorIconKind.Process:
                        DrawProcess(graphics, pen, brush);
                        break;
                    case InspectorIconKind.Step:
                        DrawStep(graphics, pen, brush);
                        break;
                    case InspectorIconKind.Operation:
                        DrawOperation(graphics, pen, brush);
                        break;
                    case InspectorIconKind.Settings:
                        DrawSettings(graphics, pen, brush);
                        break;
                    case InspectorIconKind.Timing:
                        graphics.DrawEllipse(pen, 2.5F, 2.5F, 11F, 11F);
                        graphics.DrawLine(pen, 8F, 5F, 8F, 8.2F);
                        graphics.DrawLine(pen, 8F, 8.2F, 10.4F, 9.5F);
                        break;
                    case InspectorIconKind.Run:
                        graphics.DrawEllipse(pen, 2.5F, 2.5F, 11F, 11F);
                        graphics.FillPolygon(brush, new[]
                        {
                            new PointF(6.5F, 5.3F),
                            new PointF(11F, 8F),
                            new PointF(6.5F, 10.7F)
                        });
                        break;
                    case InspectorIconKind.Warning:
                        DrawWarning(graphics, pen, brush);
                        break;
                    case InspectorIconKind.InputOutput:
                        graphics.DrawRectangle(pen, 1.8F, 4F, 4F, 8F);
                        graphics.DrawRectangle(pen, 10.2F, 4F, 4F, 8F);
                        graphics.DrawLine(pen, 5.8F, 6.2F, 10.2F, 6.2F);
                        graphics.DrawLine(pen, 5.8F, 9.8F, 10.2F, 9.8F);
                        break;
                    case InspectorIconKind.Motion:
                        graphics.DrawLine(pen, 3F, 13F, 3F, 3F);
                        graphics.DrawLine(pen, 3F, 13F, 13F, 13F);
                        graphics.DrawLine(pen, 3F, 13F, 11.5F, 4.5F);
                        graphics.DrawLine(pen, 8.7F, 4.5F, 11.5F, 4.5F);
                        graphics.DrawLine(pen, 11.5F, 4.5F, 11.5F, 7.3F);
                        break;
                    case InspectorIconKind.Communication:
                        DrawCommunication(graphics, pen);
                        break;
                    case InspectorIconKind.Data:
                        DrawData(graphics, pen);
                        break;
                    case InspectorIconKind.Add:
                        graphics.DrawLine(pen, 8F, 3F, 8F, 13F);
                        graphics.DrawLine(pen, 3F, 8F, 13F, 8F);
                        break;
                    case InspectorIconKind.Save:
                        DrawSave(graphics, pen);
                        break;
                    case InspectorIconKind.Cancel:
                        graphics.DrawLine(pen, 3.5F, 3.5F, 12.5F, 12.5F);
                        graphics.DrawLine(pen, 12.5F, 3.5F, 3.5F, 12.5F);
                        break;
                    case InspectorIconKind.Edit:
                        graphics.DrawLine(pen, 3F, 12.8F, 5.8F, 12.2F);
                        graphics.DrawLine(pen, 3.2F, 10.2F, 10.5F, 2.9F);
                        graphics.DrawLine(pen, 5.8F, 12.2F, 13.1F, 4.9F);
                        graphics.DrawLine(pen, 10.5F, 2.9F, 13.1F, 4.9F);
                        break;
                    case InspectorIconKind.MoveUp:
                        DrawArrow(graphics, pen, true);
                        break;
                    case InspectorIconKind.MoveDown:
                        DrawArrow(graphics, pen, false);
                        break;
                    case InspectorIconKind.Delete:
                        graphics.DrawRectangle(pen, 4.3F, 5.1F, 7.4F, 8F);
                        graphics.DrawLine(pen, 3.2F, 4F, 12.8F, 4F);
                        graphics.DrawLine(pen, 6.2F, 2.4F, 9.8F, 2.4F);
                        graphics.DrawLine(pen, 6.7F, 7F, 6.7F, 11F);
                        graphics.DrawLine(pen, 9.3F, 7F, 9.3F, 11F);
                        break;
                }
            }
            graphics.Restore(state);
        }

        private static void DrawProcess(Graphics graphics, Pen pen, Brush brush)
        {
            graphics.DrawLine(pen, 4F, 4F, 8F, 8F);
            graphics.DrawLine(pen, 8F, 8F, 12F, 4F);
            graphics.DrawLine(pen, 8F, 8F, 12F, 12F);
            graphics.FillEllipse(brush, 2.3F, 2.3F, 3.4F, 3.4F);
            graphics.FillEllipse(brush, 6.3F, 6.3F, 3.4F, 3.4F);
            graphics.FillEllipse(brush, 10.3F, 2.3F, 3.4F, 3.4F);
            graphics.FillEllipse(brush, 10.3F, 10.3F, 3.4F, 3.4F);
        }

        private static void DrawStep(Graphics graphics, Pen pen, Brush brush)
        {
            for (int index = 0; index < 3; index++)
            {
                float y = 4F + index * 4F;
                graphics.FillEllipse(brush, 2.2F, y - 1F, 2F, 2F);
                graphics.DrawLine(pen, 6F, y, 13.5F, y);
            }
        }

        private static void DrawOperation(Graphics graphics, Pen pen, Brush brush)
        {
            graphics.DrawRectangle(pen, 2.2F, 3F, 11.6F, 10F);
            graphics.FillPolygon(brush, new[]
            {
                new PointF(6.5F, 5.4F),
                new PointF(10.7F, 8F),
                new PointF(6.5F, 10.6F)
            });
        }

        private static void DrawSettings(Graphics graphics, Pen pen, Brush brush)
        {
            graphics.DrawLine(pen, 2F, 4F, 14F, 4F);
            graphics.DrawLine(pen, 2F, 8F, 14F, 8F);
            graphics.DrawLine(pen, 2F, 12F, 14F, 12F);
            graphics.FillEllipse(brush, 5F, 2.4F, 3.2F, 3.2F);
            graphics.FillEllipse(brush, 9F, 6.4F, 3.2F, 3.2F);
            graphics.FillEllipse(brush, 4F, 10.4F, 3.2F, 3.2F);
        }

        private static void DrawWarning(Graphics graphics, Pen pen, Brush brush)
        {
            var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(8F, 2F),
                new PointF(14F, 13F),
                new PointF(2F, 13F)
            });
            path.CloseFigure();
            graphics.DrawPath(pen, path);
            path.Dispose();
            graphics.DrawLine(pen, 8F, 5.3F, 8F, 9F);
            graphics.FillEllipse(brush, 7.2F, 10.6F, 1.6F, 1.6F);
        }

        private static void DrawCommunication(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 2.5F, 5F, 12.5F, 5F);
            graphics.DrawLine(pen, 10F, 2.5F, 12.5F, 5F);
            graphics.DrawLine(pen, 10F, 7.5F, 12.5F, 5F);
            graphics.DrawLine(pen, 13.5F, 11F, 3.5F, 11F);
            graphics.DrawLine(pen, 6F, 8.5F, 3.5F, 11F);
            graphics.DrawLine(pen, 6F, 13.5F, 3.5F, 11F);
        }

        private static void DrawData(Graphics graphics, Pen pen)
        {
            graphics.DrawEllipse(pen, 3F, 2.5F, 10F, 4F);
            graphics.DrawArc(pen, 3F, 6F, 10F, 4F, 0F, 180F);
            graphics.DrawArc(pen, 3F, 9.5F, 10F, 4F, 0F, 180F);
            graphics.DrawLine(pen, 3F, 4.5F, 3F, 11.5F);
            graphics.DrawLine(pen, 13F, 4.5F, 13F, 11.5F);
        }

        private static void DrawSave(Graphics graphics, Pen pen)
        {
            graphics.DrawRectangle(pen, 2.5F, 2.5F, 11F, 11F);
            graphics.DrawRectangle(pen, 5F, 2.5F, 5.5F, 3.6F);
            graphics.DrawRectangle(pen, 5F, 9F, 6F, 4.5F);
        }

        private static void DrawArrow(Graphics graphics, Pen pen, bool up)
        {
            float top = up ? 3F : 13F;
            float bottom = up ? 13F : 3F;
            graphics.DrawLine(pen, 8F, bottom, 8F, top);
            graphics.DrawLine(pen, 8F, top, 4.5F, up ? 6.5F : 9.5F);
            graphics.DrawLine(pen, 8F, top, 11.5F, up ? 6.5F : 9.5F);
        }
    }

    internal sealed class InspectorIconButton : Button
    {
        private bool pointerOver;
        private bool pointerDown;

        public InspectorIconButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        public InspectorIconKind IconKind { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            pointerOver = true;
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
            pointerDown = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pointerDown = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? UiPalette.Surface);
            Color fillColor = BackColor;
            if (Enabled && pointerDown && FlatAppearance.MouseDownBackColor != Color.Empty)
            {
                fillColor = FlatAppearance.MouseDownBackColor;
            }
            else if (Enabled && pointerOver && FlatAppearance.MouseOverBackColor != Color.Empty)
            {
                fillColor = FlatAppearance.MouseOverBackColor;
            }
            using (GraphicsPath path = InspectorShapes.CreateRoundedPath(
                new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)),
                5))
            using (var brush = new SolidBrush(fillColor))
            {
                e.Graphics.FillPath(brush, path);
            }

            Color contentColor = Enabled ? ForeColor : UiPalette.TextDisabled;
            Size textSize = string.IsNullOrEmpty(Text)
                ? Size.Empty
                : TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int iconSize = IconKind == InspectorIconKind.None ? 0 : Math.Min(15, Height - 8);
            int gap = iconSize > 0 && textSize.Width > 0 ? 5 : 0;
            int groupWidth = iconSize + gap + textSize.Width;
            int startX = TextAlign == ContentAlignment.MiddleLeft
                ? Padding.Left
                : Math.Max(3, (Width - groupWidth) / 2);
            if (iconSize > 0)
            {
                InspectorIcons.Draw(
                    e.Graphics,
                    new Rectangle(startX, (Height - iconSize) / 2, iconSize, iconSize),
                    IconKind,
                    contentColor);
            }
            if (textSize.Width > 0)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    new Rectangle(startX + iconSize + gap, 0,
                        Math.Max(1, Width - startX - iconSize - gap - 4), Height),
                    contentColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPadding);
            }
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(2, 2, Width - 5, Height - 5));
            }
        }
    }

    internal sealed class InspectorSectionButton : Button
    {
        private bool pointerOver;
        private bool pointerDown;
        private bool expanded = true;

        public InspectorSectionButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        public InspectorIconKind IconKind { get; set; }

        public bool Expanded
        {
            get => expanded;
            set
            {
                if (expanded == value)
                {
                    return;
                }
                expanded = value;
                Invalidate();
            }
        }

        public bool ShowDivider { get; set; } = true;

        protected override void OnMouseEnter(EventArgs e)
        {
            pointerOver = true;
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
            pointerDown = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pointerDown = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = BackColor;
            if (pointerDown && FlatAppearance.MouseDownBackColor != Color.Empty)
            {
                background = FlatAppearance.MouseDownBackColor;
            }
            else if (pointerOver && FlatAppearance.MouseOverBackColor != Color.Empty)
            {
                background = FlatAppearance.MouseOverBackColor;
            }
            using (var brush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int centerY = Height / 2;
            using (var chevron = new Pen(UiPalette.TextSecondary, 1.35F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                if (expanded)
                {
                    e.Graphics.DrawLine(chevron, 10F, centerY - 2F, 14F, centerY + 2F);
                    e.Graphics.DrawLine(chevron, 14F, centerY + 2F, 18F, centerY - 2F);
                }
                else
                {
                    e.Graphics.DrawLine(chevron, 12F, centerY - 4F, 16F, centerY);
                    e.Graphics.DrawLine(chevron, 16F, centerY, 12F, centerY + 4F);
                }
            }

            int textLeft = 28;
            if (IconKind != InspectorIconKind.None)
            {
                const int iconSize = 16;
                InspectorIcons.Draw(
                    e.Graphics,
                    new Rectangle(27, (Height - iconSize) / 2, iconSize, iconSize),
                    IconKind,
                    UiPalette.Brand);
                textLeft = 50;
            }
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft - 8), Height),
                Enabled ? ForeColor : UiPalette.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);

            if (ShowDivider)
            {
                using (var divider = new Pen(UiPalette.Stroke))
                {
                    e.Graphics.DrawLine(divider, 9, Height - 1, Math.Max(9, Width - 9), Height - 1);
                }
            }
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    new Rectangle(3, 3, Math.Max(1, Width - 7), Math.Max(1, Height - 7)));
            }
        }
    }

    internal sealed class InspectorFlowPanel : FlowLayoutPanel
    {
        public InspectorFlowPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);
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
                Math.Max(1, (ClientSize.Height - textHeight) / 2 + 4));
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
            ItemHeight = 24;
            dropDownButton.AccessibleName = "展开选项";
            dropDownButton.Click += (sender, args) => OpenDropDownFromArrow();
            Controls.Add(dropDownButton);
        }

        internal bool UseSelectionPicker { get; set; }

        internal event EventHandler SelectionPickerRequested;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SendMessage(Handle, CbSetItemHeight, new IntPtr(-1), new IntPtr(26));
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
                    e.Bounds.X + 6,
                    e.Bounds.Y + (selectionField ? 2 : 0),
                    Math.Max(1, e.Bounds.Width - 8),
                    Math.Max(1, e.Bounds.Height - (selectionField ? 2 : 0))),
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
            if (CanOpenSelectionPicker())
            {
                if (DroppedDown)
                {
                    DroppedDown = false;
                }
                QueueSelectionPickerRequest();
                return;
            }
            base.OnDropDown(e);
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
            BackColor = Enabled ? UiPalette.Input : UiPalette.SurfaceSubtle;
            ForeColor = Enabled ? UiPalette.TextPrimary : UiPalette.TextDisabled;
            base.OnEnabledChanged(e);
            dropDownButton.Invalidate();
            Invalidate();
        }

        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (CanOpenSelectionPicker())
            {
                if (message.Msg == CbShowDropDown && message.WParam != IntPtr.Zero)
                {
                    message.Result = IntPtr.Zero;
                    QueueSelectionPickerRequest();
                    return;
                }
                if ((message.Msg == WmLeftButtonDown
                        || message.Msg == WmLeftButtonDoubleClick)
                    && ShouldOpenSelectionPickerFromMouse(message.LParam))
                {
                    Focus();
                    message.Result = IntPtr.Zero;
                    QueueSelectionPickerRequest();
                    return;
                }
                if ((message.Msg == WmKeyDown || message.Msg == WmSysKeyDown)
                    && ShouldOpenSelectionPickerFromKeyboard(message))
                {
                    message.Result = IntPtr.Zero;
                    QueueSelectionPickerRequest();
                    return;
                }
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
            if (CanOpenSelectionPicker())
            {
                QueueSelectionPickerRequest();
                return;
            }
            DroppedDown = true;
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

        private bool ShouldOpenSelectionPickerFromMouse(IntPtr position)
        {
            if (DropDownStyle == ComboBoxStyle.DropDownList)
            {
                return true;
            }
            int x = unchecked((short)(position.ToInt64() & 0xFFFF));
            return x >= Math.Max(0, ClientSize.Width - SelectionPickerArrowWidth);
        }

        private static bool ShouldOpenSelectionPickerFromKeyboard(
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

        internal void ClearTextSelection()
        {
            if (DropDownStyle == ComboBoxStyle.DropDownList || IsDisposed)
            {
                return;
            }
            SelectionStart = Text?.Length ?? 0;
            SelectionLength = 0;
        }
    }

    internal sealed class InspectorComboArrow : Control
    {
        private bool pointerOver;
        private bool pointerDown;

        public InspectorComboArrow()
        {
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            pointerOver = true;
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
            pointerDown = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pointerDown = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = !Enabled
                ? UiPalette.SurfaceSubtle
                : pointerDown
                    ? UiPalette.SurfacePressed
                    : (pointerOver ? UiPalette.SurfaceHover : UiPalette.Input);
            e.Graphics.Clear(background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(
                Enabled ? UiPalette.TextSecondary : UiPalette.TextDisabled,
                1.35F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                float centerX = ClientSize.Width / 2F;
                float centerY = ClientSize.Height / 2F;
                e.Graphics.DrawLine(pen, centerX - 3F, centerY - 1.5F, centerX, centerY + 1.5F);
                e.Graphics.DrawLine(pen, centerX, centerY + 1.5F, centerX + 3F, centerY - 1.5F);
            }
        }
    }

    internal sealed class InspectorToggle : CheckBox
    {
        private bool pointerOver;

        public InspectorToggle()
        {
            AutoSize = false;
            Cursor = Cursors.Hand;
            Font = InspectorFonts.Regular9;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            pointerOver = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            pointerOver = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? UiPalette.Surface);
            var track = new Rectangle(2, Math.Max(2, (Height - 18) / 2), 32, 18);
            Color trackColor;
            if (!Enabled)
            {
                trackColor = UiPalette.Disabled;
            }
            else if (Checked)
            {
                trackColor = pointerOver ? UiPalette.BrandHover : UiPalette.Brand;
            }
            else
            {
                trackColor = pointerOver
                    ? UiPalette.TextDisabled
                    : UiPalette.StrokeStrong;
            }
            using (GraphicsPath trackPath = InspectorShapes.CreateRoundedPath(track, 8))
            using (var trackBrush = new SolidBrush(trackColor))
            {
                e.Graphics.FillPath(trackBrush, trackPath);
            }
            int thumbX = Checked ? track.Right - 16 : track.Left + 2;
            using (var thumbBrush = new SolidBrush(UiPalette.SurfaceStrong))
            {
                e.Graphics.FillEllipse(thumbBrush, thumbX, track.Y + 2, 14, 14);
            }
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1));
            }
        }
    }

    internal static class InspectorFonts
    {
        private const string FontDirectory = @"D:\AutomationTools\Fonts\MiSans";
        private static readonly PrivateFontCollection PrivateFonts
            = new PrivateFontCollection();

        public static readonly string LoadFailureMessage;
        public static readonly Font Regular85;
        public static readonly Font Regular9;
        public static readonly Font Regular95;
        public static readonly Font Regular10;
        public static readonly Font Bold9;
        public static readonly Font Bold95;

        static InspectorFonts()
        {
            try
            {
                LoadFontFile("MiSans-Regular.ttf");
                LoadFontFile("MiSans-Semibold.ttf");
                FontFamily regularFamily = GetFamily("MiSans");
                FontFamily semiboldFamily = GetFamily("MiSans Semibold");
                Regular85 = CreateFont(regularFamily, 10F);
                Regular9 = CreateFont(regularFamily, 10.5F);
                Regular95 = CreateFont(regularFamily, 10.75F);
                Regular10 = CreateFont(regularFamily, 11.25F);
                Bold9 = CreateFont(semiboldFamily, 10.5F);
                Bold95 = CreateFont(semiboldFamily, 11.25F);
            }
            catch (Exception ex)
            {
                LoadFailureMessage = "Inspector 字体资源异常：" + ex.Message;
                FontFamily emergencyFamily = SystemFonts.MessageBoxFont.FontFamily;
                Regular85 = CreateFont(emergencyFamily, 10F);
                Regular9 = CreateFont(emergencyFamily, 10.5F);
                Regular95 = CreateFont(emergencyFamily, 10.75F);
                Regular10 = CreateFont(emergencyFamily, 11.25F);
                Bold9 = CreateFont(emergencyFamily, 10.5F, FontStyle.Bold);
                Bold95 = CreateFont(emergencyFamily, 11.25F, FontStyle.Bold);
                try
                {
                    var logger = new LocalFileLogger(@"D:\AutomationLogs\RuntimeExceptions");
                    logger.Log(LoadFailureMessage + Environment.NewLine + ex, LogLevel.Error);
                }
                catch
                {
                }
            }
        }

        private static Font CreateFont(
            FontFamily family,
            float size,
            FontStyle style = FontStyle.Regular)
        {
            FontStyle availableStyle = family.IsStyleAvailable(style)
                ? style
                : FontStyle.Regular;
            return new Font(family, size, availableStyle, GraphicsUnit.Point);
        }

        private static void LoadFontFile(string fileName)
        {
            string path = System.IO.Path.Combine(FontDirectory, fileName);
            if (!System.IO.File.Exists(path))
            {
                throw new System.IO.FileNotFoundException(
                    "Inspector 字体资源缺失：" + path,
                    path);
            }
            PrivateFonts.AddFontFile(path);
        }

        private static FontFamily GetFamily(string familyName)
        {
            FontFamily family = PrivateFonts.Families.FirstOrDefault(item => string.Equals(
                item.Name,
                familyName,
                StringComparison.OrdinalIgnoreCase));
            if (family == null)
            {
                throw new InvalidOperationException(
                    "Inspector 无法加载内置字体：" + familyName);
            }
            return family;
        }
    }

    internal sealed class InspectorView : UserControl
    {
        private const int MaxCachedPages = 8;
        private const int WmSetRedraw = 0x000B;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

        private readonly InspectorFlowPanel content = new InspectorFlowPanel();
        private readonly Label emptyLabel = new Label();
        private readonly ToolTip descriptionToolTip = new ToolTip
        {
            AutoPopDelay = 10000,
            InitialDelay = 450,
            ReshowDelay = 100,
            ShowAlways = true
        };
        private readonly List<InspectorSectionControl> sectionControls
            = new List<InspectorSectionControl>();
        private readonly Dictionary<string, CachedInspectorPage> pageCache
            = new Dictionary<string, CachedInspectorPage>(StringComparer.Ordinal);
        private InspectorDocument document;
        private object selectedObject;
        private bool editable;
        private long cacheSequence;
        private int updateDepth;
        private bool refreshPending;
        private bool redrawSuspended;
        private bool layoutRequired;

        public InspectorView()
        {
            BackColor = UiPalette.Background;
            DoubleBuffered = true;

            content.AutoScroll = true;
            content.BackColor = BackColor;
            content.Dock = DockStyle.Fill;
            content.FlowDirection = FlowDirection.TopDown;
            content.Padding = new Padding(6);
            content.WrapContents = false;
            Controls.Add(content);

            emptyLabel.AutoSize = false;
            emptyLabel.BackColor = BackColor;
            emptyLabel.Dock = DockStyle.Fill;
            emptyLabel.Font = InspectorFonts.Regular10;
            emptyLabel.ForeColor = UiPalette.TextSecondary;
            emptyLabel.Text = "选择流程、步骤、指令或配置对象后，\r\n可在这里查看和编辑参数。";
            emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(emptyLabel);

            Resize += (sender, args) => UpdateContentWidths();
        }

        public object SelectedObject => selectedObject;

        public event EventHandler FieldValueChanged;

        public void SetObject(object value, bool allowEdit)
        {
            bool objectChanged = !ReferenceEquals(selectedObject, value);
            selectedObject = value;
            editable = allowEdit;
            if (objectChanged || document == null)
            {
                InspectorDocument next = InspectorDefinitionBuilder.Build(selectedObject);
                if (CanRebind(next))
                {
                    Rebind(next);
                }
                else
                {
                    Rebuild(next);
                }
                return;
            }
            SetEditable(allowEdit);
            RefreshValues();
        }

        public void SetEditable(bool allowEdit)
        {
            editable = allowEdit;
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.SetEditable(allowEdit);
            }
        }

        public void BeginUpdate()
        {
            updateDepth++;
            if (updateDepth == 1)
            {
                if (IsHandleCreated)
                {
                    SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
                    redrawSuspended = true;
                }
                SuspendLayout();
                content.SuspendLayout();
            }
        }

        public void EndUpdate()
        {
            if (updateDepth <= 0)
            {
                return;
            }
            updateDepth--;
            if (updateDepth != 0)
            {
                return;
            }
            try
            {
                content.ResumeLayout(false);
                ResumeLayout(false);
                if (layoutRequired)
                {
                    bool scrollWasVisible = content.VerticalScroll.Visible;
                    UpdateContentWidths();
                    content.PerformLayout();
                    if (scrollWasVisible != content.VerticalScroll.Visible)
                    {
                        UpdateContentWidths();
                        content.PerformLayout();
                    }
                    PerformLayout();
                }
            }
            finally
            {
                layoutRequired = false;
                if (redrawSuspended)
                {
                    if (IsHandleCreated)
                    {
                        SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                        Invalidate(true);
                    }
                    redrawSuspended = false;
                }
            }
        }

        public void RefreshDocument()
        {
            if (selectedObject == null)
            {
                Rebuild();
                return;
            }
            InspectorDocument next = InspectorDefinitionBuilder.Build(selectedObject);
            if (document == null || !string.Equals(
                document.Signature,
                next.Signature,
                StringComparison.Ordinal))
            {
                Rebuild(next);
                return;
            }
            Rebind(next, false);
        }

        private void Rebuild(InspectorDocument next = null)
        {
            BeginUpdate();
            try
            {
                layoutRequired = true;
                StoreCurrentPage();
                document = next ?? InspectorDefinitionBuilder.Build(selectedObject);
                emptyLabel.Visible = selectedObject == null || document.Sections.Count == 0;
                content.Visible = !emptyLabel.Visible;
                if (emptyLabel.Visible)
                {
                    emptyLabel.BringToFront();
                    return;
                }

                if (TryRestorePage(document, out CachedInspectorPage page))
                {
                    sectionControls.AddRange(page.Sections);
                    int restoredWidth = GetContentWidth();
                    for (int index = 0; index < sectionControls.Count; index++)
                    {
                        sectionControls[index].Width = restoredWidth;
                        sectionControls[index].Rebind(document.Sections[index], editable);
                    }
                    content.Controls.AddRange(sectionControls.Cast<Control>().ToArray());
                    content.AutoScrollPosition = page.ScrollPosition;
                    return;
                }

                var builtSections = new List<Control>();
                int targetWidth = GetContentWidth();
                foreach (InspectorSectionDefinition section in document.Sections)
                {
                    if (section.Fields.Count == 0)
                    {
                        continue;
                    }
                    InspectorSectionControl sectionControl;
                    if (TryRestoreSection(section, out InspectorSectionControl restoredSection))
                    {
                        sectionControl = restoredSection;
                        sectionControl.Rebind(section, editable);
                    }
                    else
                    {
                        sectionControl = new InspectorSectionControl(
                            section,
                            editable,
                            descriptionToolTip);
                        sectionControl.FieldValueChanged += Editor_FieldValueChanged;
                        sectionControl.SizeChanged += SectionControl_SizeChanged;
                    }
                    sectionControl.Width = targetWidth;
                    sectionControls.Add(sectionControl);
                    builtSections.Add(sectionControl);
                }
                content.Controls.AddRange(builtSections.ToArray());
                content.AutoScrollPosition = Point.Empty;
            }
            finally
            {
                EndUpdate();
            }
        }

        private bool CanRebind(InspectorDocument next)
        {
            if (document == null || next == null
                || !string.Equals(document.Signature, next.Signature, StringComparison.Ordinal)
                || sectionControls.Count != next.Sections.Count)
            {
                return false;
            }
            for (int index = 0; index < sectionControls.Count; index++)
            {
                if (!sectionControls[index].CanRebind(next.Sections[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private void Rebind(InspectorDocument next, bool suspendRedraw = true)
        {
            if (suspendRedraw)
            {
                BeginUpdate();
            }
            try
            {
                document = next;
                for (int index = 0; index < sectionControls.Count; index++)
                {
                    sectionControls[index].Rebind(next.Sections[index], editable);
                }
            }
            finally
            {
                if (suspendRedraw)
                {
                    EndUpdate();
                }
            }
        }

        private void StoreCurrentPage()
        {
            Point scrollPosition = new Point(
                Math.Abs(content.AutoScrollPosition.X),
                Math.Abs(content.AutoScrollPosition.Y));
            content.Controls.Clear();
            if (document == null || string.IsNullOrEmpty(document.Signature)
                || sectionControls.Count == 0)
            {
                DisposeSections(sectionControls);
                sectionControls.Clear();
                return;
            }

            if (pageCache.TryGetValue(document.Signature, out CachedInspectorPage duplicate))
            {
                pageCache.Remove(document.Signature);
                DisposeSections(duplicate.Sections);
            }
            pageCache[document.Signature] = new CachedInspectorPage(
                document.Signature,
                sectionControls.ToList(),
                scrollPosition,
                ++cacheSequence);
            sectionControls.Clear();

            while (pageCache.Count > MaxCachedPages)
            {
                CachedInspectorPage oldest = pageCache.Values
                    .OrderBy(page => page.LastUsed).First();
                pageCache.Remove(oldest.Signature);
                DisposeSections(oldest.Sections);
            }
        }

        private bool TryRestorePage(
            InspectorDocument next,
            out CachedInspectorPage page)
        {
            var candidates = new List<CachedInspectorPage>();
            if (pageCache.TryGetValue(next.Signature, out CachedInspectorPage exact))
            {
                candidates.Add(exact);
            }
            candidates.AddRange(pageCache.Values
                .Where(candidate => !ReferenceEquals(candidate, exact))
                .OrderByDescending(candidate => candidate.LastUsed));
            foreach (CachedInspectorPage candidate in candidates)
            {
                if (candidate.Sections.Count != next.Sections.Count)
                {
                    continue;
                }
                bool compatible = true;
                for (int index = 0; index < candidate.Sections.Count; index++)
                {
                    if (!candidate.Sections[index].CanRebind(next.Sections[index]))
                    {
                        compatible = false;
                        break;
                    }
                }
                if (compatible)
                {
                    pageCache.Remove(candidate.Signature);
                    page = candidate;
                    page.LastUsed = ++cacheSequence;
                    return true;
                }
            }
            page = null;
            return false;
        }

        private bool TryRestoreSection(
            InspectorSectionDefinition definition,
            out InspectorSectionControl section)
        {
            foreach (CachedInspectorPage page in pageCache.Values
                .OrderByDescending(candidate => candidate.LastUsed).ToList())
            {
                section = page.Sections.FirstOrDefault(candidate =>
                    candidate.CanRebind(definition));
                if (section == null)
                {
                    continue;
                }
                page.Sections.Remove(section);
                page.LastUsed = ++cacheSequence;
                if (page.Sections.Count == 0)
                {
                    pageCache.Remove(page.Signature);
                }
                return true;
            }
            section = null;
            return false;
        }

        private static void DisposeSections(IEnumerable<InspectorSectionControl> sections)
        {
            foreach (InspectorSectionControl section in sections)
            {
                section.Dispose();
            }
        }

        private void Editor_FieldValueChanged(object sender, EventArgs e)
        {
            FieldValueChanged?.Invoke(this, EventArgs.Empty);
            if (!IsHandleCreated || IsDisposed || refreshPending)
            {
                return;
            }
            refreshPending = true;
            BeginInvoke((Action)(() =>
            {
                refreshPending = false;
                if (!IsDisposed)
                {
                    RefreshDocument();
                }
            }));
        }

        private void RefreshValues()
        {
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.RefreshValues();
            }
        }

        private void UpdateContentWidths()
        {
            int width = GetContentWidth();
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.Width = width;
            }
        }

        private int GetContentWidth()
        {
            return Math.Max(220, content.ClientSize.Width - content.Padding.Horizontal
                - (content.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (CachedInspectorPage page in pageCache.Values)
                {
                    DisposeSections(page.Sections);
                }
                pageCache.Clear();
                descriptionToolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        private void SectionControl_SizeChanged(object sender, EventArgs e)
        {
            if (updateDepth > 0)
            {
                layoutRequired = true;
            }
        }

        private sealed class CachedInspectorPage
        {
            public CachedInspectorPage(
                string signature,
                List<InspectorSectionControl> sections,
                Point scrollPosition,
                long lastUsed)
            {
                Signature = signature;
                Sections = sections;
                ScrollPosition = scrollPosition;
                LastUsed = lastUsed;
            }

            public string Signature { get; }
            public List<InspectorSectionControl> Sections { get; }
            public Point ScrollPosition { get; }
            public long LastUsed { get; set; }
        }
    }

    internal sealed class InspectorSectionControl : UserControl
    {
        private const int HeaderHeight = 32;
        private readonly InspectorSectionButton headerButton = new InspectorSectionButton();
        private readonly InspectorFlowPanel body = new InspectorFlowPanel();
        private readonly List<InspectorFieldControl> fields = new List<InspectorFieldControl>();
        private bool expanded = true;
        private bool updatingLayout;

        public InspectorSectionControl(
            InspectorSectionDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
        {
            AutoSize = false;
            BackColor = UiPalette.Surface;
            Margin = new Padding(0, 0, 0, 5);
            Padding = Padding.Empty;

            headerButton.AutoSize = false;
            headerButton.BackColor = UiPalette.SurfaceSubtle;
            headerButton.Cursor = Cursors.Hand;
            headerButton.FlatAppearance.BorderSize = 0;
            headerButton.FlatAppearance.MouseOverBackColor = UiPalette.SurfaceHover;
            headerButton.FlatAppearance.MouseDownBackColor = UiPalette.BrandSoft;
            headerButton.FlatStyle = FlatStyle.Flat;
            headerButton.Font = InspectorFonts.Bold9;
            headerButton.ForeColor = UiPalette.TextPrimary;
            headerButton.Height = HeaderHeight;
            headerButton.IconKind = InspectorIcons.FromSectionTitle(definition.Title);
            headerButton.Expanded = expanded;
            headerButton.Text = definition.Title;
            headerButton.Visible = definition.ShowHeader;
            headerButton.Click += (sender, args) => ToggleExpanded();
            Controls.Add(headerButton);

            body.AutoSize = false;
            body.BackColor = UiPalette.Surface;
            body.FlowDirection = FlowDirection.TopDown;
            body.Padding = new Padding(8, 3, 8, 5);
            body.WrapContents = false;
            Controls.Add(body);
            body.BringToFront();

            var editors = new List<Control>();
            foreach (InspectorFieldDefinition field in definition.Fields)
            {
                InspectorFieldControl editor = CreateEditor(
                    field,
                    editable,
                    descriptionToolTip);
                editor.FieldValueChanged += (sender, args) => FieldValueChanged?.Invoke(this, EventArgs.Empty);
                editor.SizeChanged += (sender, args) => UpdateWidths();
                fields.Add(editor);
                editors.Add(editor);
            }
            body.Controls.AddRange(editors.ToArray());
            Resize += (sender, args) =>
            {
                UpdateWidths();
            };
        }

        public event EventHandler FieldValueChanged;

        public void SetEditable(bool editable)
        {
            foreach (InspectorFieldControl field in fields)
            {
                field.SetEditable(editable);
            }
        }

        public void RefreshValues()
        {
            foreach (InspectorFieldControl field in fields)
            {
                field.RefreshValue();
            }
        }

        public bool CanRebind(InspectorSectionDefinition definition)
        {
            if (definition == null || fields.Count != definition.Fields.Count)
            {
                return false;
            }
            for (int index = 0; index < fields.Count; index++)
            {
                if (!fields[index].CanRebind(definition.Fields[index]))
                {
                    return false;
                }
            }
            return true;
        }

        public void Rebind(InspectorSectionDefinition definition, bool editable)
        {
            headerButton.IconKind = InspectorIcons.FromSectionTitle(definition.Title);
            headerButton.Text = definition.Title;
            headerButton.Visible = definition.ShowHeader;
            for (int index = 0; index < fields.Count; index++)
            {
                fields[index].Rebind(definition.Fields[index], editable);
            }
            UpdateWidths();
        }

        private static InspectorFieldControl CreateEditor(
            InspectorFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
        {
            if (definition is InspectorValueReferenceFieldDefinition reference)
            {
                return new InspectorValueReferenceFieldControl(
                    reference,
                    editable,
                    descriptionToolTip);
            }
            if (definition is InspectorCollectionFieldDefinition collection)
            {
                return new InspectorCollectionFieldControl(
                    collection,
                    editable,
                    descriptionToolTip);
            }
            return new InspectorScalarFieldControl(
                (InspectorScalarFieldDefinition)definition,
                editable,
                descriptionToolTip);
        }

        private void ToggleExpanded()
        {
            expanded = !expanded;
            body.Visible = expanded;
            headerButton.Expanded = expanded;
            UpdateWidths();
        }

        private void UpdateWidths()
        {
            if (updatingLayout)
            {
                return;
            }
            updatingLayout = true;
            try
            {
                int width = Math.Max(180, ClientSize.Width);
                int headerHeight = headerButton.Visible ? HeaderHeight : 0;
                headerButton.SetBounds(0, 0, width, headerHeight);
                body.Width = width;
                int fieldWidth = Math.Max(180, body.ClientSize.Width - body.Padding.Horizontal);
                foreach (InspectorFieldControl field in fields)
                {
                    field.Width = fieldWidth;
                }
                int bodyHeight = body.GetPreferredSize(new Size(width, 0)).Height;
                body.SetBounds(0, headerHeight, width, bodyHeight);
                Height = headerHeight + (expanded ? bodyHeight : 0);
            }
            finally
            {
                updatingLayout = false;
            }
        }
    }

    internal abstract class InspectorFieldControl : UserControl
    {
        protected const int PropertyRowHeight = 33;
        protected const int PropertyEditorHeight = 28;
        protected InspectorFieldDefinition Definition;
        protected readonly ToolTip DescriptionToolTip;
        protected bool Editable;

        protected InspectorFieldControl(
            InspectorFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
        {
            Definition = definition;
            DescriptionToolTip = descriptionToolTip;
            Editable = editable;
            AutoSize = false;
            BackColor = UiPalette.Surface;
            Margin = Padding.Empty;
        }

        public event EventHandler FieldValueChanged;

        public abstract void SetEditable(bool editable);
        public abstract void RefreshValue();
        public abstract bool FocusEditor();
        public abstract void Rebind(InspectorFieldDefinition definition, bool editable);

        public virtual bool CanRebind(InspectorFieldDefinition definition)
        {
            return definition != null
                && Definition.GetType() == definition.GetType();
        }

        protected void OnFieldValueChanged()
        {
            FieldValueChanged?.Invoke(this, EventArgs.Empty);
        }

        protected void AttachDescription(params Control[] controls)
        {
            if (DescriptionToolTip == null)
            {
                return;
            }
            string description = Definition.Description ?? string.Empty;
            foreach (Control control in controls.Where(control => control != null))
            {
                control.AccessibleDescription = description;
                DescriptionToolTip.SetToolTip(control, description);
            }
        }

        protected void DrawPropertyRowBackground(PaintEventArgs e, int labelWidth)
        {
            using (var labelBrush = new SolidBrush(UiPalette.SurfaceSubtle))
            {
                e.Graphics.FillRectangle(
                    labelBrush,
                    new Rectangle(0, 0, labelWidth, Math.Max(1, Height - 1)));
            }
            using (var divider = new Pen(UiPalette.Divider))
            {
                e.Graphics.DrawLine(
                    divider,
                    0,
                    Math.Max(0, Height - 1),
                    Math.Max(0, Width - 1),
                    Math.Max(0, Height - 1));
            }
        }

        protected static int GetLabelWidth(int availableWidth)
        {
            return Math.Min(124, Math.Max(92, availableWidth * 35 / 100));
        }

        protected static void PopulateStandardValues(
            InspectorComboBox comboBox,
            object owner,
            PropertyDescriptor property,
            object currentValue,
            bool includeOptions)
        {
            comboBox.BeginUpdate();
            try
            {
                comboBox.Items.Clear();
                if (includeOptions)
                {
                    foreach (InspectorStandardValue option
                        in InspectorValueConversion.GetStandardValues(owner, property))
                    {
                        comboBox.Items.Add(option);
                    }
                }
                InspectorStandardValue selected = comboBox.Items
                    .Cast<InspectorStandardValue>()
                    .FirstOrDefault(option => Equals(option.Value, currentValue));
                if (selected != null)
                {
                    comboBox.SelectedItem = selected;
                    return;
                }

                string displayText = InspectorValueConversion.ToDisplayText(
                    owner,
                    property,
                    currentValue);
                comboBox.SelectedIndex = -1;
                if (comboBox.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        var current = new InspectorStandardValue(currentValue, displayText);
                        comboBox.Items.Add(current);
                        comboBox.SelectedItem = current;
                    }
                }
                else
                {
                    comboBox.Text = displayText;
                }
            }
            finally
            {
                comboBox.EndUpdate();
                comboBox.ClearTextSelection();
            }
        }
    }

    internal sealed class InspectorScalarFieldControl : InspectorFieldControl
    {
        private InspectorScalarFieldDefinition definition;
        private readonly Control editor;
        private string validationMessage = string.Empty;
        private bool refreshing;
        private bool standardValuesLoaded;
        private bool gotoDropConfigured;
        private bool selectionPickerConfigured;
        private ToolStripDropDown activeSelectionPicker;

        public InspectorScalarFieldControl(
            InspectorScalarFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
            : base(definition, editable, descriptionToolTip)
        {
            this.definition = definition;
            AccessibleName = definition.Label;
            DoubleBuffered = true;

            editor = CreateEditor();
            editor.TabIndex = 0;
            Controls.Add(editor);

            AttachDescription(this, editor);

            Resize += (sender, args) => LayoutControls();
            SetEditable(editable);
            RefreshValue();
            LayoutControls();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int width = Math.Max(120, ClientSize.Width);
            int labelWidth = GetLabelWidth(width);
            DrawPropertyRowBackground(e, labelWidth);
            TextRenderer.DrawText(
                e.Graphics,
                definition.Label,
                InspectorFonts.Regular9,
                new Rectangle(6, 0, Math.Max(1, labelWidth - 10), PropertyRowHeight),
                UiPalette.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(validationMessage))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    validationMessage,
                    InspectorFonts.Regular85,
                    new Rectangle(labelWidth + 6, PropertyRowHeight,
                        Math.Max(48, width - labelWidth - 6), 20),
                    UiPalette.Danger,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPadding);
            }
        }

        public override void SetEditable(bool editable)
        {
            Editable = editable;
            bool allow = editable && !definition.IsReadOnly;
            if (editor is TextBox textBox)
            {
                textBox.ReadOnly = !allow;
                textBox.BackColor = allow ? UiPalette.Input : UiPalette.SurfaceSubtle;
            }
            else
            {
                editor.Enabled = allow;
            }
        }

        public override void RefreshValue()
        {
            RefreshValue(false);
        }

        private void RefreshValue(bool force)
        {
            if (!force && editor.Focused)
            {
                return;
            }
            refreshing = true;
            try
            {
                object value = definition.GetValue();
                if (editor is CheckBox checkBox)
                {
                    checkBox.Checked = value is bool flag && flag;
                    checkBox.Text = string.Empty;
                    checkBox.AccessibleName = definition.Label;
                    checkBox.AccessibleDescription = checkBox.Checked ? "已开启" : "已关闭";
                }
                else if (editor is InspectorComboBox comboBox)
                {
                    standardValuesLoaded = false;
                    PopulateStandardValues(
                        comboBox,
                        definition.Owner,
                        definition.Property,
                        value,
                        false);
                }
                else if (editor is TextBox textBox)
                {
                    textBox.Text = InspectorValueConversion.ToDisplayText(
                        definition.Owner,
                        definition.Property,
                        value);
                }
                ShowMessage(definition.Description, false);
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, true);
            }
            finally
            {
                refreshing = false;
            }
        }

        public override bool FocusEditor()
        {
            if (!editor.Enabled || (editor is TextBox textBox && textBox.ReadOnly))
            {
                return false;
            }
            editor.Focus();
            return true;
        }

        public override bool CanRebind(InspectorFieldDefinition next)
        {
            if (!base.CanRebind(next))
            {
                return false;
            }
            var scalar = (InspectorScalarFieldDefinition)next;
            Type type = Nullable.GetUnderlyingType(scalar.Property.PropertyType)
                ?? scalar.Property.PropertyType;
            if (editor is InspectorToggle)
            {
                return type == typeof(bool);
            }
            bool usesComboBox = InspectorValueConversion.HasStandardValues(
                scalar.Owner,
                scalar.Property) || type.IsEnum;
            return editor is InspectorComboBox ? usesComboBox : !usesComboBox;
        }

        public override void Rebind(InspectorFieldDefinition next, bool editable)
        {
            definition = (InspectorScalarFieldDefinition)next;
            Definition = next;
            AccessibleName = definition.Label;
            standardValuesLoaded = false;
            if (editor is InspectorComboBox comboBox)
            {
                comboBox.DropDownStyle = InspectorValueConversion.StandardValuesExclusive(
                    definition.Owner,
                    definition.Property)
                    ? ComboBoxStyle.DropDownList
                    : ComboBoxStyle.DropDown;
                ConfigureSelectionPicker(comboBox);
            }
            ConfigureGotoDrop(editor);
            AttachDescription(this, editor);
            SetEditable(editable);
            RefreshValue(true);
            Invalidate();
        }

        private Control CreateEditor()
        {
            Type type = Nullable.GetUnderlyingType(definition.Property.PropertyType)
                ?? definition.Property.PropertyType;
            if (type == typeof(bool))
            {
                var checkBox = new InspectorToggle
                {
                    AutoSize = false,
                    BackColor = UiPalette.Surface,
                    Font = InspectorFonts.Regular9,
                    ForeColor = UiPalette.TextSecondary,
                    Height = 28,
                    TextAlign = ContentAlignment.MiddleLeft,
                    UseVisualStyleBackColor = false
                };
                checkBox.CheckedChanged += (sender, args) =>
                {
                    if (!refreshing && checkBox.Enabled)
                    {
                        CommitValue(checkBox.Checked);
                    }
                };
                return checkBox;
            }

            if (InspectorValueConversion.HasStandardValues(definition.Owner, definition.Property)
                || type.IsEnum)
            {
                var comboBox = new InspectorComboBox
                {
                    DropDownHeight = 320,
                    Font = InspectorFonts.Regular9,
                    IntegralHeight = false
                };
                comboBox.DropDownStyle = InspectorValueConversion.StandardValuesExclusive(
                    definition.Owner,
                    definition.Property)
                    ? ComboBoxStyle.DropDownList
                    : ComboBoxStyle.DropDown;
                comboBox.DropDown += (sender, args) =>
                    EnsureStandardValuesLoaded(comboBox);
                comboBox.SelectionChangeCommitted += (sender, args) => CommitComboBox(comboBox);
                comboBox.Validated += (sender, args) =>
                {
                    if (comboBox.DropDownStyle != ComboBoxStyle.DropDownList)
                    {
                        CommitComboBox(comboBox);
                    }
                };
                ConfigureGotoDrop(comboBox);
                ConfigureSelectionPicker(comboBox);
                return comboBox;
            }

            var textEditor = new InspectorTextBox
            {
                Font = InspectorFonts.Regular9
            };
            textEditor.Validated += (sender, args) => CommitText(textEditor);
            textEditor.KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Enter)
                {
                    CommitText(textEditor);
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                }
            };
            ConfigureGotoDrop(textEditor);
            return textEditor;
        }

        private void ConfigureGotoDrop(Control control)
        {
            bool marked = definition.Property.Attributes[typeof(MarkedGotoAttribute)]
                is MarkedGotoAttribute;
            control.AllowDrop = marked;
            if (gotoDropConfigured)
            {
                return;
            }
            gotoDropConfigured = true;
            control.DragEnter += (sender, args) =>
            {
                args.Effect = control.AllowDrop && args.Data != null
                    && args.Data.GetDataPresent(FrmDataGrid.OperationAddressDragFormat)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            };
            control.DragDrop += (sender, args) =>
            {
                if (!control.AllowDrop)
                {
                    return;
                }
                string address = args.Data?.GetData(FrmDataGrid.OperationAddressDragFormat) as string;
                if (string.IsNullOrWhiteSpace(address))
                {
                    return;
                }
                if (control is TextBox textBox)
                {
                    textBox.Text = address;
                    CommitText(textBox);
                }
                else if (control is ComboBox comboBox)
                {
                    comboBox.Text = address;
                    CommitComboBox(comboBox);
                }
            };
        }

        private void ConfigureSelectionPicker(InspectorComboBox comboBox)
        {
            comboBox.UseSelectionPicker = InspectorSelectionPickerResolver.TryResolve(
                definition.Property,
                out InspectorSelectionPickerKind _);
            if (selectionPickerConfigured)
            {
                return;
            }
            selectionPickerConfigured = true;
            comboBox.SelectionPickerRequested += (sender, args) =>
                ShowSelectionPicker(comboBox);
        }

        private void ShowSelectionPicker(InspectorComboBox comboBox)
        {
            if (!Editable || definition.IsReadOnly
                || !InspectorSelectionPickerResolver.TryResolve(
                    definition.Property,
                    out InspectorSelectionPickerKind kind))
            {
                return;
            }
            activeSelectionPicker?.Close();
            activeSelectionPicker = InspectorSelectionPickerDropDown.Show(
                comboBox,
                kind,
                definition.Owner,
                definition.Property,
                Convert.ToString(definition.GetValue(), CultureInfo.CurrentCulture),
                selectedValue => CommitValue(selectedValue),
                () => activeSelectionPicker = null);
        }

        private void EnsureStandardValuesLoaded(InspectorComboBox comboBox)
        {
            if (standardValuesLoaded)
            {
                return;
            }
            bool wasRefreshing = refreshing;
            refreshing = true;
            try
            {
                PopulateStandardValues(
                    comboBox,
                    definition.Owner,
                    definition.Property,
                    definition.GetValue(),
                    true);
                standardValuesLoaded = true;
            }
            finally
            {
                refreshing = wasRefreshing;
            }
        }

        private void CommitComboBox(ComboBox comboBox)
        {
            if (refreshing || !comboBox.Enabled)
            {
                return;
            }
            object value = comboBox.SelectedItem is InspectorStandardValue selected
                ? selected.Value
                : InspectorValueConversion.FromText(
                    definition.Owner,
                    definition.Property,
                    comboBox.Text);
            CommitValue(value);
        }

        private void CommitText(TextBox textBox)
        {
            if (refreshing || textBox.ReadOnly)
            {
                return;
            }
            try
            {
                object value = InspectorValueConversion.FromText(
                    definition.Owner,
                    definition.Property,
                    textBox.Text);
                CommitValue(value);
            }
            catch (Exception ex)
            {
                ShowMessage(Unwrap(ex).Message, true);
                textBox.SelectAll();
                textBox.Focus();
            }
        }

        private void CommitValue(object value)
        {
            try
            {
                object current = definition.GetValue();
                if (Equals(current, value))
                {
                    ShowMessage(definition.Description, false);
                    return;
                }
                definition.SetValue(value);
                ShowMessage(definition.Description, false);
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                ShowMessage(Unwrap(ex).Message, true);
                RefreshValue();
            }
        }

        private void ShowMessage(string text, bool error)
        {
            string nextMessage = error ? text ?? string.Empty : string.Empty;
            if (string.Equals(validationMessage, nextMessage, StringComparison.Ordinal))
            {
                return;
            }
            validationMessage = nextMessage;
            LayoutControls();
            Invalidate();
        }

        private void LayoutControls()
        {
            int width = Math.Max(120, ClientSize.Width);
            int labelWidth = GetLabelWidth(width);
            int editorLeft = labelWidth + 6;
            int editorTop = 2;
            editor.SetBounds(
                editorLeft,
                editorTop,
                editor is InspectorToggle ? 38 : Math.Max(48, width - editorLeft),
                PropertyEditorHeight);
            Height = PropertyRowHeight
                + (string.IsNullOrEmpty(validationMessage) ? 0 : 20);
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                activeSelectionPicker?.Dispose();
                activeSelectionPicker = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class InspectorValueReferenceFieldControl : InspectorFieldControl
    {
        private InspectorValueReferenceFieldDefinition definition;
        private readonly InspectorComboBox kind = new InspectorComboBox();
        private readonly InspectorComboBox value = new InspectorComboBox();
        private string validationMessage = string.Empty;
        private bool refreshing;
        private bool valueOptionsLoaded;
        private ToolStripDropDown activeSelectionPicker;

        public InspectorValueReferenceFieldControl(
            InspectorValueReferenceFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
            : base(definition, editable, descriptionToolTip)
        {
            this.definition = definition;
            AccessibleName = definition.Label;
            DoubleBuffered = true;

            kind.DropDownStyle = ComboBoxStyle.DropDownList;
            kind.Font = InspectorFonts.Regular9;
            kind.SelectionChangeCommitted += Kind_SelectionChangeCommitted;
            Controls.Add(kind);

            value.Font = InspectorFonts.Regular9;
            value.IntegralHeight = false;
            value.DropDownHeight = 320;
            value.DropDown += (sender, args) => EnsureValueOptionsLoaded();
            value.SelectionPickerRequested += (sender, args) => ShowSelectionPicker();
            value.SelectionChangeCommitted += (sender, args) => CommitValue();
            value.Validated += (sender, args) => CommitValue();
            Controls.Add(value);

            AttachDescription(this, kind, value);

            Resize += (sender, args) => LayoutControls();
            SetEditable(editable);
            RefreshValue();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int width = Math.Max(180, ClientSize.Width);
            int labelWidth = GetLabelWidth(width);
            DrawPropertyRowBackground(e, labelWidth);
            TextRenderer.DrawText(
                e.Graphics,
                definition.Label,
                InspectorFonts.Regular9,
                new Rectangle(6, 0, Math.Max(1, labelWidth - 10), PropertyRowHeight),
                UiPalette.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(validationMessage))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    validationMessage,
                    InspectorFonts.Regular85,
                    new Rectangle(labelWidth + 6, PropertyRowHeight,
                        Math.Max(80, width - labelWidth - 6), 20),
                    UiPalette.Danger,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPadding);
            }
        }

        public override void SetEditable(bool editable)
        {
            Editable = editable;
            bool allow = editable && !definition.IsReadOnly;
            kind.Enabled = allow;
            value.Enabled = allow;
        }

        public override void RefreshValue()
        {
            RefreshValue(false);
        }

        private void RefreshValue(bool force)
        {
            if (!force && value.Focused)
            {
                return;
            }
            refreshing = true;
            try
            {
                InspectorValueReferenceKind current = definition.GetCurrentKind();
                kind.Items.Clear();
                if (current == InspectorValueReferenceKind.Conflict)
                {
                    kind.Items.Add(new InspectorReferenceKindItem(
                        InspectorValueReferenceKind.Conflict,
                        definition.GetKindDisplayName(InspectorValueReferenceKind.Conflict)));
                }
                foreach (InspectorValueReferenceKind option in definition.AvailableKinds)
                {
                    kind.Items.Add(new InspectorReferenceKindItem(
                        option,
                        definition.GetKindDisplayName(option)));
                }
                InspectorReferenceKindItem selectedKind = kind.Items.Cast<InspectorReferenceKindItem>()
                    .FirstOrDefault(item => item.Kind == current);
                kind.SelectedItem = selectedKind ?? kind.Items.Cast<InspectorReferenceKindItem>().FirstOrDefault();
                ConfigureValueEditor(CurrentKind());
                ShowMessage(definition.Description, false);
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, true);
            }
            finally
            {
                refreshing = false;
            }
        }

        public override bool FocusEditor()
        {
            if (!kind.Enabled)
            {
                return false;
            }
            value.Focus();
            return true;
        }

        public override void Rebind(InspectorFieldDefinition next, bool editable)
        {
            definition = (InspectorValueReferenceFieldDefinition)next;
            Definition = next;
            AccessibleName = definition.Label;
            valueOptionsLoaded = false;
            AttachDescription(this, kind, value);
            SetEditable(editable);
            RefreshValue(true);
            Invalidate();
        }

        private void Kind_SelectionChangeCommitted(object sender, EventArgs e)
        {
            if (refreshing || !(kind.SelectedItem is InspectorReferenceKindItem selected)
                || selected.Kind == InspectorValueReferenceKind.Conflict)
            {
                return;
            }
            try
            {
                definition.SetKind(selected.Kind);
                ConfigureValueEditor(selected.Kind);
                // 引用方式只有在写入新值后才可由模型事实反推；此处不触发整页刷新，
                // 避免空值状态立即回落到默认方式，导致用户无法继续输入索引。
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, true);
            }
        }

        private void ConfigureValueEditor(InspectorValueReferenceKind selectedKind)
        {
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            valueOptionsLoaded = false;
            if (property == null)
            {
                value.UseSelectionPicker = false;
                value.Items.Clear();
                value.Text = string.Empty;
                value.Enabled = false;
                return;
            }
            value.DropDownStyle = InspectorValueConversion.StandardValuesExclusive(
                definition.Owner,
                property)
                ? ComboBoxStyle.DropDownList
                : ComboBoxStyle.DropDown;
            value.UseSelectionPicker = InspectorSelectionPickerResolver.TryResolve(
                property,
                out InspectorSelectionPickerKind _);
            PopulateStandardValues(
                value,
                definition.Owner,
                property,
                definition.GetValue(selectedKind),
                false);
            value.Enabled = Editable && !definition.IsReadOnly;
        }

        private void ShowSelectionPicker()
        {
            InspectorValueReferenceKind selectedKind = CurrentKind();
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            if (!Editable || definition.IsReadOnly || property == null
                || !InspectorSelectionPickerResolver.TryResolve(
                    property,
                    out InspectorSelectionPickerKind kind))
            {
                return;
            }
            activeSelectionPicker?.Close();
            activeSelectionPicker = InspectorSelectionPickerDropDown.Show(
                value,
                kind,
                definition.Owner,
                property,
                Convert.ToString(
                    definition.GetValue(selectedKind),
                    CultureInfo.CurrentCulture),
                selectedValue => CommitPickerValue(selectedKind, selectedValue),
                () => activeSelectionPicker = null);
        }

        private void CommitPickerValue(
            InspectorValueReferenceKind selectedKind,
            string selectedValue)
        {
            try
            {
                definition.SetValue(selectedKind, selectedValue);
                ShowMessage(definition.Description, false);
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, true);
            }
        }

        private void EnsureValueOptionsLoaded()
        {
            if (valueOptionsLoaded)
            {
                return;
            }
            InspectorValueReferenceKind selectedKind = CurrentKind();
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            if (property == null)
            {
                return;
            }
            bool wasRefreshing = refreshing;
            refreshing = true;
            try
            {
                PopulateStandardValues(
                    value,
                    definition.Owner,
                    property,
                    definition.GetValue(selectedKind),
                    true);
                valueOptionsLoaded = true;
            }
            finally
            {
                refreshing = wasRefreshing;
            }
        }

        private void CommitValue()
        {
            if (refreshing || !value.Enabled)
            {
                return;
            }
            InspectorValueReferenceKind selectedKind = CurrentKind();
            PropertyDescriptor property = definition.GetActiveProperty(selectedKind);
            if (property == null)
            {
                return;
            }
            try
            {
                object converted = value.SelectedItem is InspectorStandardValue option
                    ? option.Value
                    : InspectorValueConversion.FromText(definition.Owner, property, value.Text);
                definition.SetValue(selectedKind, converted);
                ShowMessage(definition.Description, false);
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, true);
                value.Focus();
            }
        }

        private InspectorValueReferenceKind CurrentKind()
        {
            if (kind.SelectedItem is InspectorReferenceKindItem selected
                && selected.Kind != InspectorValueReferenceKind.Conflict)
            {
                return selected.Kind;
            }
            return definition.GetDefaultKind();
        }

        private void ShowMessage(string text, bool error)
        {
            string nextMessage = error ? text ?? string.Empty : string.Empty;
            if (string.Equals(validationMessage, nextMessage, StringComparison.Ordinal))
            {
                return;
            }
            validationMessage = nextMessage;
            LayoutControls();
            Invalidate();
        }

        private void LayoutControls()
        {
            int width = Math.Max(180, ClientSize.Width);
            int labelWidth = GetLabelWidth(width);
            int editorLeft = labelWidth + 6;
            int editorWidth = Math.Max(80, width - editorLeft);
            int kindWidth = Math.Min(92, Math.Max(72, editorWidth * 40 / 100));
            kind.SetBounds(editorLeft, 2, kindWidth, PropertyEditorHeight);
            value.SetBounds(
                editorLeft + kindWidth + 4,
                2,
                Math.Max(48, editorWidth - kindWidth - 4),
                PropertyEditorHeight);
            int messageHeight = string.IsNullOrEmpty(validationMessage) ? 0 : 20;
            Height = PropertyRowHeight + messageHeight;
        }

        private sealed class InspectorReferenceKindItem
        {
            public InspectorReferenceKindItem(InspectorValueReferenceKind kind, string text)
            {
                Kind = kind;
                Text = text;
            }

            public InspectorValueReferenceKind Kind { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                activeSelectionPicker?.Dispose();
                activeSelectionPicker = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class InspectorCollectionFieldControl : InspectorFieldControl
    {
        private InspectorCollectionFieldDefinition definition;
        private readonly Label title = new Label();
        private readonly InspectorIconButton addButton = new InspectorIconButton();
        private readonly InspectorFlowPanel itemsPanel = new InspectorFlowPanel();
        private bool showAddButton;
        private bool updatingLayout;

        public InspectorCollectionFieldControl(
            InspectorCollectionFieldDefinition definition,
            bool editable,
            ToolTip descriptionToolTip)
            : base(definition, editable, descriptionToolTip)
        {
            this.definition = definition;
            title.AutoEllipsis = true;
            title.Font = InspectorFonts.Bold9;
            title.ForeColor = UiPalette.TextPrimary;
            title.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(title);

            addButton.BackColor = UiPalette.BrandSoft;
            addButton.Cursor = Cursors.Hand;
            addButton.FlatAppearance.BorderSize = 0;
            addButton.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoftHover;
            addButton.FlatAppearance.MouseDownBackColor = UiPalette.Selection;
            addButton.FlatStyle = FlatStyle.Flat;
            addButton.Font = InspectorFonts.Regular85;
            addButton.ForeColor = UiPalette.Brand;
            addButton.IconKind = InspectorIconKind.Add;
            addButton.AccessibleName = "添加" + definition.Label;
            addButton.Text = "添加";
            addButton.Click += (sender, args) => AddItem();
            Controls.Add(addButton);

            AttachDescription(title, addButton);

            itemsPanel.AutoSize = false;
            itemsPanel.BackColor = UiPalette.Surface;
            itemsPanel.FlowDirection = FlowDirection.TopDown;
            itemsPanel.WrapContents = false;
            Controls.Add(itemsPanel);

            Resize += (sender, args) => LayoutControls();
            SetEditable(editable);
            RebuildItems();
        }

        public override void SetEditable(bool editable)
        {
            Editable = editable;
            showAddButton = editable && !definition.IsReadOnly && CanCreateItem();
            addButton.Visible = showAddButton;
            addButton.Enabled = definition.Items == null || definition.Items.Count < MaxItems;
            foreach (InspectorCollectionItemControl item in itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>())
            {
                item.SetEditable(editable && !definition.IsReadOnly);
            }
            LayoutControls();
        }

        public override void RefreshValue()
        {
            IList items = definition.Items;
            if (itemsPanel.Controls.Count != (items?.Count ?? 0))
            {
                RebuildItems();
                return;
            }
            foreach (InspectorCollectionItemControl item in itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>())
            {
                item.RefreshValues();
            }
            title.Text = $"{definition.Label}（{items?.Count ?? 0}）";
        }

        public override bool FocusEditor()
        {
            InspectorCollectionItemControl first = itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>().FirstOrDefault();
            return first?.FocusFirstEditor() == true;
        }

        public override bool CanRebind(InspectorFieldDefinition next)
        {
            if (!base.CanRebind(next))
            {
                return false;
            }
            IList items = ((InspectorCollectionFieldDefinition)next).Items;
            List<InspectorCollectionItemControl> itemControls = itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>().ToList();
            if (items == null || itemControls.Count != items.Count)
            {
                return false;
            }
            for (int index = 0; index < items.Count; index++)
            {
                if (!itemControls[index].CanRebind(items[index]))
                {
                    return false;
                }
            }
            return true;
        }

        public override void Rebind(InspectorFieldDefinition next, bool editable)
        {
            definition = (InspectorCollectionFieldDefinition)next;
            Definition = next;
            addButton.AccessibleName = "添加" + definition.Label;
            AttachDescription(title, addButton);
            Editable = editable;
            bool nextShowAddButton = editable && !definition.IsReadOnly && CanCreateItem();
            bool layoutChanged = showAddButton != nextShowAddButton;
            showAddButton = nextShowAddButton;
            addButton.Visible = showAddButton;
            addButton.Enabled = definition.Items == null || definition.Items.Count < MaxItems;
            if (!TryRebindItems())
            {
                RebuildItems();
                return;
            }
            if (layoutChanged)
            {
                LayoutControls();
            }
        }

        private bool TryRebindItems()
        {
            IList items = definition.Items;
            List<InspectorCollectionItemControl> itemControls = itemsPanel.Controls
                .OfType<InspectorCollectionItemControl>().ToList();
            if (items == null || itemControls.Count != items.Count)
            {
                return false;
            }
            itemsPanel.SuspendLayout();
            try
            {
                for (int index = 0; index < items.Count; index++)
                {
                    if (!itemControls[index].TryRebind(
                        definition.Label,
                        index,
                        items.Count,
                        items[index],
                        Editable && !definition.IsReadOnly))
                    {
                        return false;
                    }
                }
                title.Text = $"{definition.Label}（{items.Count}）";
                return true;
            }
            finally
            {
                itemsPanel.ResumeLayout(false);
            }
        }

        private void RebuildItems()
        {
            itemsPanel.SuspendLayout();
            try
            {
                foreach (Control control in itemsPanel.Controls.Cast<Control>().ToArray())
                {
                    control.Dispose();
                }
                itemsPanel.Controls.Clear();
                IList items = definition.Items;
                title.Text = $"{definition.Label}（{items?.Count ?? 0}）";
                addButton.Enabled = items == null || items.Count < MaxItems;
                if (items == null)
                {
                    return;
                }
                var itemControls = new List<Control>(items.Count);
                for (int index = 0; index < items.Count; index++)
                {
                    object item = items[index];
                    var itemControl = new InspectorCollectionItemControl(
                        definition.Label,
                        index,
                        items.Count,
                        item,
                        Editable && !definition.IsReadOnly,
                        items.Count <= 6 || index == 0,
                        DescriptionToolTip);
                    itemControl.DeleteRequested += (sender, args) => DeleteItem(itemControl.ItemIndex);
                    itemControl.MoveRequested += (sender, offset) => MoveItem(itemControl.ItemIndex, offset);
                    itemControl.FieldValueChanged += (sender, args) => OnFieldValueChanged();
                    itemControl.SizeChanged += (sender, args) => LayoutControls();
                    itemControls.Add(itemControl);
                }
                itemsPanel.Controls.AddRange(itemControls.ToArray());
            }
            finally
            {
                itemsPanel.ResumeLayout(true);
                LayoutControls();
            }
        }

        private bool CanCreateItem()
        {
            Type itemType = definition.ItemType;
            return itemType != null && !itemType.IsAbstract
                && itemType.GetConstructor(Type.EmptyTypes) != null;
        }

        private void AddItem()
        {
            IList items = definition.Items;
            Type itemType = definition.ItemType;
            if (items == null || itemType == null)
            {
                return;
            }
            int previousCount = items.Count;
            try
            {
                object item = Activator.CreateInstance(itemType);
                ApplyItemDefaults(item);
                if (items.Count >= MaxItems)
                {
                    throw new InvalidOperationException($"{definition.Label}最多允许 {MaxItems} 项。");
                }
                items.Add(item);
                RebuildItems();
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                while (items.Count > previousCount)
                {
                    items.RemoveAt(items.Count - 1);
                }
                MessageBox.Show(
                    Unwrap(ex).Message,
                    "添加配置项失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                RebuildItems();
            }
        }

        private void DeleteItem(int index)
        {
            IList items = definition.Items;
            if (items == null || index < 0 || index >= items.Count)
            {
                return;
            }
            if (items.Count <= MinItems)
            {
                MessageBox.Show(
                    $"{definition.Label}至少需要 {MinItems} 项。",
                    "删除配置项失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            object removed = items[index];
            items.RemoveAt(index);
            try
            {
                RebuildItems();
                OnFieldValueChanged();
            }
            catch (Exception ex)
            {
                items.Insert(index, removed);
                MessageBox.Show(
                    Unwrap(ex).Message,
                    "删除配置项失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                RebuildItems();
            }
        }

        private void MoveItem(int index, int offset)
        {
            IList items = definition.Items;
            int target = index + offset;
            if (items == null || index < 0 || index >= items.Count
                || target < 0 || target >= items.Count)
            {
                return;
            }
            object item = items[index];
            items.RemoveAt(index);
            items.Insert(target, item);
            RebuildItems();
            OnFieldValueChanged();
        }

        private int MinItems
        {
            get
            {
                InlineListAttribute attribute = definition.Property.Attributes[typeof(InlineListAttribute)]
                    as InlineListAttribute;
                return Math.Max(0, attribute?.MinItems ?? 0);
            }
        }

        private int MaxItems
        {
            get
            {
                InlineListAttribute attribute = definition.Property.Attributes[typeof(InlineListAttribute)]
                    as InlineListAttribute;
                return Math.Max(MinItems, attribute?.MaxItems ?? int.MaxValue);
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }

        private static void ApplyItemDefaults(object item)
        {
            if (item is ProcParam process)
            {
                process.DelayAfterMs = -1;
            }
        }

        private void LayoutControls()
        {
            if (updatingLayout)
            {
                return;
            }
            updatingLayout = true;
            try
            {
                int width = Math.Max(180, ClientSize.Width);
                int right = width;
                if (showAddButton)
                {
                    addButton.SetBounds(Math.Max(0, right - 64), 1, 64, 27);
                    right -= 68;
                }
                title.SetBounds(0, 0, Math.Max(70, right), 29);
                foreach (InspectorCollectionItemControl item in itemsPanel.Controls
                    .OfType<InspectorCollectionItemControl>())
                {
                    item.Width = width;
                }
                int itemsHeight = itemsPanel.GetPreferredSize(new Size(width, 0)).Height;
                itemsPanel.SetBounds(0, 31, width, itemsHeight);
                Height = 31 + itemsHeight;
            }
            finally
            {
                updatingLayout = false;
            }
        }
    }

    internal sealed class InspectorCollectionItemControl : UserControl
    {
        private const int HeaderHeight = 30;
        private readonly InspectorSectionButton header = new InspectorSectionButton();
        private readonly InspectorIconButton delete = new InspectorIconButton();
        private readonly InspectorIconButton moveUp = new InspectorIconButton();
        private readonly InspectorIconButton moveDown = new InspectorIconButton();
        private readonly InspectorFlowPanel fieldsPanel = new InspectorFlowPanel();
        private readonly List<InspectorFieldControl> fieldControls = new List<InspectorFieldControl>();
        private object item;
        private string itemLabel;
        private int itemCount;
        private readonly ToolTip descriptionToolTip;
        private bool expanded;
        private bool editable;
        private bool updatingLayout;

        public InspectorCollectionItemControl(
            string label,
            int index,
            int itemCount,
            object item,
            bool editable,
            bool expanded,
            ToolTip descriptionToolTip)
        {
            ItemIndex = index;
            this.itemCount = itemCount;
            this.item = item;
            itemLabel = label;
            this.descriptionToolTip = descriptionToolTip;
            this.editable = editable;
            this.expanded = expanded;
            AutoSize = false;
            BackColor = UiPalette.Stroke;
            Margin = new Padding(0, 0, 0, 3);
            Padding = new Padding(1);

            header.BackColor = UiPalette.SurfaceSubtle;
            header.AutoEllipsis = true;
            header.Cursor = Cursors.Hand;
            header.FlatAppearance.BorderSize = 0;
            header.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoft;
            header.FlatAppearance.MouseDownBackColor = UiPalette.BrandSoftHover;
            header.FlatStyle = FlatStyle.Flat;
            header.Font = InspectorFonts.Bold9;
            header.ForeColor = UiPalette.TextPrimary;
            header.Expanded = expanded;
            header.ShowDivider = false;
            header.Click += (sender, args) => ToggleExpanded();
            Controls.Add(header);

            ConfigureMiniButton(moveUp, InspectorIconKind.MoveUp);
            ConfigureMiniButton(moveDown, InspectorIconKind.MoveDown);
            ConfigureMiniButton(delete, InspectorIconKind.Delete);
            moveUp.AccessibleName = "上移配置项";
            moveDown.AccessibleName = "下移配置项";
            delete.AccessibleName = "删除配置项";
            delete.ForeColor = UiPalette.Danger;
            delete.FlatAppearance.MouseOverBackColor = UiPalette.DangerSoft;
            moveUp.Click += (sender, args) => MoveRequested?.Invoke(this, -1);
            moveDown.Click += (sender, args) => MoveRequested?.Invoke(this, 1);
            delete.Click += (sender, args) => DeleteRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(moveUp);
            Controls.Add(moveDown);
            Controls.Add(delete);

            fieldsPanel.AutoSize = false;
            fieldsPanel.BackColor = UiPalette.Surface;
            fieldsPanel.FlowDirection = FlowDirection.TopDown;
            fieldsPanel.Padding = new Padding(6, 2, 6, 3);
            fieldsPanel.WrapContents = false;
            fieldsPanel.Visible = expanded;
            Controls.Add(fieldsPanel);

            BuildFields();
            UpdateHeaderText();
            Resize += (sender, args) =>
            {
                LayoutControls();
            };
            SetEditable(editable);
            LayoutControls();
        }

        public int ItemIndex { get; private set; }
        public event EventHandler DeleteRequested;
        public event Action<object, int> MoveRequested;
        public event EventHandler FieldValueChanged;

        public void SetEditable(bool allowEdit)
        {
            editable = allowEdit;
            moveUp.Visible = allowEdit;
            moveDown.Visible = allowEdit;
            delete.Visible = allowEdit;
            moveUp.Enabled = allowEdit && ItemIndex > 0;
            moveDown.Enabled = allowEdit && ItemIndex < itemCount - 1;
            foreach (InspectorFieldControl field in fieldControls)
            {
                field.SetEditable(allowEdit);
            }
            LayoutControls();
        }

        public void RefreshValues()
        {
            foreach (InspectorFieldControl field in fieldControls)
            {
                field.RefreshValue();
            }
            UpdateHeaderText();
        }

        public bool FocusFirstEditor()
        {
            if (!expanded)
            {
                ToggleExpanded();
            }
            return fieldControls.Any(field => field.FocusEditor());
        }

        public bool TryRebind(
            string label,
            int index,
            int nextItemCount,
            object nextItem,
            bool allowEdit)
        {
            IReadOnlyList<InspectorFieldDefinition> definitions
                = InspectorDefinitionBuilder.BuildItemFields(nextItem, "item", index);
            if (definitions.Count != fieldControls.Count)
            {
                return false;
            }
            for (int fieldIndex = 0; fieldIndex < definitions.Count; fieldIndex++)
            {
                if (!fieldControls[fieldIndex].CanRebind(definitions[fieldIndex]))
                {
                    return false;
                }
            }

            itemLabel = label;
            ItemIndex = index;
            itemCount = nextItemCount;
            item = nextItem;
            bool layoutChanged = editable != allowEdit;
            for (int fieldIndex = 0; fieldIndex < definitions.Count; fieldIndex++)
            {
                fieldControls[fieldIndex].Rebind(definitions[fieldIndex], allowEdit);
            }
            editable = allowEdit;
            moveUp.Visible = allowEdit;
            moveDown.Visible = allowEdit;
            delete.Visible = allowEdit;
            moveUp.Enabled = allowEdit && ItemIndex > 0;
            moveDown.Enabled = allowEdit && ItemIndex < itemCount - 1;
            UpdateHeaderText();
            if (layoutChanged)
            {
                LayoutControls();
            }
            return true;
        }

        public bool CanRebind(object nextItem)
        {
            IReadOnlyList<InspectorFieldDefinition> definitions
                = InspectorDefinitionBuilder.BuildItemFields(nextItem, "item", ItemIndex);
            if (definitions.Count != fieldControls.Count)
            {
                return false;
            }
            for (int index = 0; index < definitions.Count; index++)
            {
                if (!fieldControls[index].CanRebind(definitions[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private void BuildFields()
        {
            IReadOnlyList<InspectorFieldDefinition> definitions
                = InspectorDefinitionBuilder.BuildItemFields(item, "item", ItemIndex);
            var builtFields = new List<Control>(definitions.Count);
            foreach (InspectorFieldDefinition definition in definitions)
            {
                InspectorFieldControl field;
                if (definition is InspectorValueReferenceFieldDefinition reference)
                {
                    field = new InspectorValueReferenceFieldControl(
                        reference,
                        editable,
                        descriptionToolTip);
                }
                else if (definition is InspectorCollectionFieldDefinition collection)
                {
                    field = new InspectorCollectionFieldControl(
                        collection,
                        editable,
                        descriptionToolTip);
                }
                else
                {
                    field = new InspectorScalarFieldControl(
                        (InspectorScalarFieldDefinition)definition,
                        editable,
                        descriptionToolTip);
                }
                field.FieldValueChanged += (sender, args) =>
                {
                    UpdateHeaderText();
                    FieldValueChanged?.Invoke(this, EventArgs.Empty);
                };
                field.SizeChanged += (sender, args) => LayoutControls();
                fieldControls.Add(field);
                builtFields.Add(field);
            }
            fieldsPanel.Controls.AddRange(builtFields.ToArray());
        }

        private void ToggleExpanded()
        {
            expanded = !expanded;
            fieldsPanel.Visible = expanded;
            header.Expanded = expanded;
            UpdateHeaderText();
            LayoutControls();
        }

        private void UpdateHeaderText()
        {
            header.Text = string.Equals(itemLabel, "条件", StringComparison.Ordinal)
                ? "第 " + (ItemIndex + 1) + " 条"
                : itemLabel + " " + (ItemIndex + 1);
        }

        private static void ConfigureMiniButton(
            InspectorIconButton button,
            InspectorIconKind iconKind)
        {
            button.BackColor = UiPalette.SurfaceSubtle;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoft;
            button.FlatAppearance.MouseDownBackColor = UiPalette.BrandSoftHover;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = InspectorFonts.Regular9;
            button.ForeColor = UiPalette.TextSecondary;
            button.IconKind = iconKind;
            button.Text = string.Empty;
        }

        private void LayoutControls()
        {
            if (updatingLayout)
            {
                return;
            }
            updatingLayout = true;
            try
            {
                int width = Math.Max(170, ClientSize.Width);
                int right = width - 4;
                if (editable)
                {
                    delete.SetBounds(right - 27, 2, 26, 26);
                    right -= 30;
                    moveDown.SetBounds(right - 27, 2, 26, 26);
                    right -= 30;
                    moveUp.SetBounds(right - 27, 2, 26, 26);
                    right -= 30;
                }
                header.SetBounds(1, 1, Math.Max(80, right - 1), HeaderHeight);
                fieldsPanel.Width = width - 2;
                int fieldWidth = Math.Max(
                    150,
                    fieldsPanel.ClientSize.Width - fieldsPanel.Padding.Horizontal);
                foreach (InspectorFieldControl field in fieldControls)
                {
                    field.Width = fieldWidth;
                }
                int fieldsHeight = fieldsPanel.GetPreferredSize(new Size(width - 2, 0)).Height;
                fieldsPanel.SetBounds(1, HeaderHeight + 2, width - 2, fieldsHeight);
                Height = expanded
                    ? HeaderHeight + 2 + fieldsHeight + 1
                    : HeaderHeight + 2;
            }
            finally
            {
                updatingLayout = false;
            }
        }
    }

    internal sealed class InspectorStandardValue
    {
        public InspectorStandardValue(object value, string text)
        {
            Value = value;
            Text = text;
        }

        public object Value { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    internal static class InspectorValueConversion
    {
        public static bool HasStandardValues(object owner, PropertyDescriptor property)
        {
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            if (targetType.IsEnum)
            {
                return true;
            }
            try
            {
                return property.Converter?.GetStandardValuesSupported(
                    new InspectorTypeDescriptorContext(owner, property)) == true;
            }
            catch
            {
                return false;
            }
        }

        public static bool StandardValuesExclusive(object owner, PropertyDescriptor property)
        {
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            if (targetType.IsEnum)
            {
                return true;
            }
            try
            {
                return property.Converter?.GetStandardValuesExclusive(
                    new InspectorTypeDescriptorContext(owner, property)) == true;
            }
            catch
            {
                return false;
            }
        }

        public static IReadOnlyList<InspectorStandardValue> GetStandardValues(
            object owner,
            PropertyDescriptor property)
        {
            var result = new List<InspectorStandardValue>();
            var context = new InspectorTypeDescriptorContext(owner, property);
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            IEnumerable values;
            try
            {
                if (property.Converter?.GetStandardValuesSupported(context) == true)
                {
                    values = property.Converter.GetStandardValues(context);
                }
                else if (targetType.IsEnum)
                {
                    values = Enum.GetValues(targetType);
                }
                else
                {
                    values = Array.Empty<object>();
                }
            }
            catch
            {
                values = Array.Empty<object>();
            }
            foreach (object value in values)
            {
                result.Add(new InspectorStandardValue(value, ToDisplayText(owner, property, value)));
            }
            return result;
        }

        public static string ToDisplayText(object owner, PropertyDescriptor property, object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            var context = new InspectorTypeDescriptorContext(owner, property);
            try
            {
                if (property.Converter?.CanConvertTo(context, typeof(string)) == true)
                {
                    return property.Converter.ConvertToString(context, CultureInfo.CurrentCulture, value);
                }
            }
            catch
            {
            }
            return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        public static object FromText(object owner, PropertyDescriptor property, string text)
        {
            Type propertyType = property.PropertyType;
            Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (targetType == typeof(string))
            {
                return text;
            }
            if (string.IsNullOrWhiteSpace(text) && Nullable.GetUnderlyingType(propertyType) != null)
            {
                return null;
            }
            var context = new InspectorTypeDescriptorContext(owner, property);
            object value;
            if (property.Converter?.CanConvertFrom(context, typeof(string)) == true)
            {
                value = property.Converter.ConvertFromString(context, CultureInfo.CurrentCulture, text);
            }
            else if (targetType.IsEnum)
            {
                value = Enum.Parse(targetType, text, true);
            }
            else
            {
                value = Convert.ChangeType(text, targetType, CultureInfo.CurrentCulture);
            }
            NumericRangeAttribute range = property.Attributes[typeof(NumericRangeAttribute)]
                as NumericRangeAttribute;
            if (range != null && !range.Contains(value))
            {
                throw new InvalidOperationException(
                    $"{property.DisplayName}必须为{range.Describe()}的数值。");
            }
            return value;
        }
    }
}
