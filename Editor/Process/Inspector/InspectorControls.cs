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

        public Color DisabledForeColor { get; set; } = UiPalette.TextDisabled;

        public Color BorderColor { get; set; } = Color.Empty;

        public float BorderWidth { get; set; }

        public bool ShowDropDownArrow { get; set; }

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
                if (BorderWidth > 0F && BorderColor != Color.Empty)
                {
                    using (var pen = new Pen(BorderColor, BorderWidth))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }

            Color contentColor = Enabled ? ForeColor : DisabledForeColor;
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
                int trailingWidth = ShowDropDownArrow ? 22 : 4;
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    new Rectangle(startX + iconSize + gap, 0,
                        Math.Max(1, Width - startX - iconSize - gap - trailingWidth), Height),
                    contentColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPadding);
            }
            if (ShowDropDownArrow)
            {
                int centerX = Math.Max(8, Width - Padding.Right - 7);
                int centerY = Height / 2;
                using (var pen = new Pen(contentColor, 1.2F))
                {
                    e.Graphics.DrawLine(pen, centerX - 3, centerY - 2, centerX, centerY + 1);
                    e.Graphics.DrawLine(pen, centerX, centerY + 1, centerX + 3, centerY - 2);
                }
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

    /// <summary>
    /// 检查器的常态值单元格。默认只绘制信息，用户激活当前单元格后，
    /// 由字段控件在同一矩形内覆盖真实编辑器，避免整页始终呈现为输入表单。
    /// </summary>
}
