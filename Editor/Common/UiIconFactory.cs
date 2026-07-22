// 模块：编辑器 / 通用 UI。
// 职责范围：编辑器共享的视觉、弹窗和 WinForms 交互基础设施。

using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Automation
{
    internal enum UiIconKind
    {
        Process,
        Station,
        Variable,
        Sliders,
        Communication,
        Plc,
        Debug,
        ControlCard,
        History,
        Ai,
        Save,
        Cancel,
        NavigateBack,
        NavigateForward,
        Undo,
        Redo,
        Pause,
        Stop,
        Step,
        Locate,
        Breakpoint,
        Alarm,
        Search,
        Monitor,
        Folder,
        Settings,
        StopAll
    }

    /// <summary>
    /// 使用 Windows 自带的 Fluent 图标字体生成位图，保证线条风格与桌面系统一致。
    /// </summary>
    internal static class UiIconFactory
    {
        public static Bitmap Create(UiIconKind kind, Color color, int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (IsNavigationPictogram(kind))
                {
                    DrawNavigationIcon(graphics, kind, size, false);
                }
                else
                {
                    DrawToolbarPictogram(graphics, kind, color, size);
                }
            }
            return bitmap;
        }

        public static Bitmap CreateNavigation(UiIconKind kind, int size, bool active)
        {
            if (!IsNavigationPictogram(kind) && kind != UiIconKind.Monitor)
            {
                throw new ArgumentOutOfRangeException(nameof(kind), "该图标不属于导航图标。");
            }

            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                DrawNavigationIcon(graphics, kind, size, active);
            }
            return bitmap;
        }

        private static bool IsNavigationPictogram(UiIconKind kind)
        {
            return kind >= UiIconKind.Process && kind <= UiIconKind.Ai;
        }

        private static void DrawNavigationIcon(Graphics graphics, UiIconKind kind, int size, bool active)
        {
            Color baseColor = active
                ? UiPalette.NavigationAccent
                : BlendColor(GetNavigationColor(kind), Color.FromArgb(184, 199, 210), 0.20F);
            Color primaryColor = Color.FromArgb(255, baseColor);
            Color secondaryColor = Color.FromArgb(active ? 210 : 190, baseColor);
            float glyphSize = size * 0.90F;
            float glyphLeft = (size - glyphSize) / 2F;
            float glyphTop = (size - glyphSize) / 2F;
            GraphicsState glyphState = graphics.Save();
            graphics.TranslateTransform(glyphLeft, glyphTop);
            graphics.ScaleTransform(glyphSize / 24F, glyphSize / 24F);
            DrawNavigationPictogram(graphics, kind, primaryColor, secondaryColor);
            graphics.Restore(glyphState);
        }

        private static Color GetNavigationColor(UiIconKind kind)
        {
            switch (kind)
            {
                case UiIconKind.Process:
                    return Color.FromArgb(64, 200, 255);
                case UiIconKind.Station:
                    return Color.FromArgb(255, 179, 64);
                case UiIconKind.Variable:
                    return Color.FromArgb(191, 90, 242);
                case UiIconKind.Sliders:
                    return Color.FromArgb(90, 210, 195);
                case UiIconKind.Communication:
                    return Color.FromArgb(100, 210, 255);
                case UiIconKind.Plc:
                    return Color.FromArgb(48, 209, 88);
                case UiIconKind.Debug:
                    return Color.FromArgb(255, 55, 95);
                case UiIconKind.ControlCard:
                    return Color.FromArgb(125, 122, 255);
                case UiIconKind.History:
                    return Color.FromArgb(255, 159, 10);
                case UiIconKind.Ai:
                    return Color.FromArgb(218, 143, 255);
                case UiIconKind.Monitor:
                    return Color.FromArgb(64, 205, 225);
                default:
                    return Color.FromArgb(202, 214, 223);
            }
        }

        private static Color BlendColor(Color source, Color target, float targetWeight)
        {
            float sourceWeight = 1F - targetWeight;
            return Color.FromArgb(
                (int)(source.R * sourceWeight + target.R * targetWeight),
                (int)(source.G * sourceWeight + target.G * targetWeight),
                (int)(source.B * sourceWeight + target.B * targetWeight));
        }

        private static void DrawNavigationPictogram(
            Graphics graphics,
            UiIconKind kind,
            Color primaryColor,
            Color secondaryColor)
        {
            using (Pen primaryPen = new Pen(primaryColor, 2F))
            using (Pen secondaryPen = new Pen(secondaryColor, 2F))
            using (SolidBrush primaryBrush = new SolidBrush(primaryColor))
            using (SolidBrush secondaryBrush = new SolidBrush(secondaryColor))
            {
                ConfigureNavigationPen(primaryPen);
                ConfigureNavigationPen(secondaryPen);

                switch (kind)
                {
                    case UiIconKind.Process:
                        graphics.DrawLines(secondaryPen, new[] { new PointF(5, 6), new PointF(12, 6), new PointF(12, 18), new PointF(19, 18) });
                        FillCircle(graphics, primaryBrush, 5, 6, 2.5F);
                        FillCircle(graphics, primaryBrush, 12, 12, 2.2F);
                        graphics.FillPolygon(primaryBrush, new[]
                        {
                            new PointF(19, 15), new PointF(22, 18),
                            new PointF(19, 21), new PointF(16, 18)
                        });
                        break;
                    case UiIconKind.Station:
                        FillRoundedRectangle(graphics, secondaryBrush, 5, 18, 12, 3, 1.2F);
                        graphics.DrawLine(secondaryPen, 8, 18, 8, 16);
                        graphics.DrawLine(primaryPen, 8, 16, 12, 11);
                        graphics.DrawLine(primaryPen, 12, 11, 16, 12);
                        graphics.DrawLine(primaryPen, 16, 12, 19, 8);
                        FillCircle(graphics, primaryBrush, 8, 16, 1.8F);
                        FillCircle(graphics, secondaryBrush, 12, 11, 1.8F);
                        FillCircle(graphics, primaryBrush, 16, 12, 1.8F);
                        graphics.DrawLine(secondaryPen, 19, 8, 21, 6);
                        graphics.DrawLine(secondaryPen, 19, 8, 21, 10);
                        break;
                    case UiIconKind.Variable:
                        graphics.DrawArc(secondaryPen, 4, 3, 8, 18, 90, 180);
                        graphics.DrawArc(secondaryPen, 12, 3, 8, 18, 270, 180);
                        graphics.DrawLine(primaryPen, 9.5F, 9, 14.5F, 15);
                        graphics.DrawLine(primaryPen, 14.5F, 9, 9.5F, 15);
                        break;
                    case UiIconKind.Sliders:
                        FillCircle(graphics, primaryBrush, 5, 6, 1.8F);
                        FillCircle(graphics, primaryBrush, 5, 12, 1.8F);
                        FillCircle(graphics, primaryBrush, 5, 18, 1.8F);
                        graphics.DrawLine(secondaryPen, 9, 6, 20, 6);
                        graphics.DrawLine(secondaryPen, 9, 12, 17, 12);
                        graphics.DrawLine(secondaryPen, 9, 18, 20, 18);
                        FillCircle(graphics, secondaryBrush, 20, 12, 1.3F);
                        break;
                    case UiIconKind.Debug:
                        PointF[] gearPoints = new PointF[16];
                        for (int i = 0; i < gearPoints.Length; i++)
                        {
                            double angle = -Math.PI / 2 + Math.PI * 2 * i / gearPoints.Length;
                            float radius = i % 2 == 0 ? 9F : 7F;
                            gearPoints[i] = new PointF(
                                12 + (float)Math.Cos(angle) * radius,
                                12 + (float)Math.Sin(angle) * radius);
                        }
                        graphics.DrawPolygon(secondaryPen, gearPoints);
                        graphics.DrawEllipse(secondaryPen, 7.5F, 7.5F, 9, 9);
                        graphics.DrawLine(primaryPen, 9.5F, 9.5F, 14.5F, 14.5F);
                        graphics.DrawLine(primaryPen, 14.5F, 9.5F, 9.5F, 14.5F);
                        break;
                    case UiIconKind.Communication:
                        DrawRoundedRectangle(graphics, secondaryPen, 2.5F, 3.5F, 9, 7, 1.3F);
                        DrawRoundedRectangle(graphics, secondaryPen, 12.5F, 13.5F, 9, 7, 1.3F);
                        graphics.DrawLine(secondaryPen, 5, 13, 9, 13);
                        graphics.DrawLine(secondaryPen, 7, 10.5F, 7, 13);
                        graphics.DrawLine(secondaryPen, 15, 23, 19, 23);
                        graphics.DrawLine(secondaryPen, 17, 20.5F, 17, 23);
                        graphics.DrawLine(primaryPen, 10, 9, 15, 14);
                        graphics.DrawLine(primaryPen, 12.5F, 14, 15, 14);
                        graphics.DrawLine(primaryPen, 15, 11.5F, 15, 14);
                        break;
                    case UiIconKind.Plc:
                        FillRoundedRectangle(graphics, primaryBrush, 9, 3, 6, 4, 1F);
                        graphics.DrawLine(secondaryPen, 12, 7, 12, 12);
                        graphics.DrawLine(secondaryPen, 5, 12, 19, 12);
                        graphics.DrawLine(secondaryPen, 5, 12, 5, 17);
                        graphics.DrawLine(secondaryPen, 12, 12, 12, 17);
                        graphics.DrawLine(secondaryPen, 19, 12, 19, 17);
                        FillCircle(graphics, primaryBrush, 5, 19, 2F);
                        FillCircle(graphics, primaryBrush, 12, 19, 2F);
                        FillCircle(graphics, primaryBrush, 19, 19, 2F);
                        break;
                    case UiIconKind.ControlCard:
                        DrawRoundedRectangle(graphics, secondaryPen, 3, 5, 18, 14, 2);
                        graphics.DrawLine(primaryPen, 7, 9, 17, 9);
                        graphics.DrawLine(primaryPen, 7, 14.5F, 13, 14.5F);
                        FillCircle(graphics, primaryBrush, 17, 14.5F, 1.7F);
                        graphics.DrawLine(secondaryPen, 7, 19, 7, 21);
                        graphics.DrawLine(secondaryPen, 11, 19, 11, 21);
                        graphics.DrawLine(secondaryPen, 15, 19, 15, 21);
                        break;
                    case UiIconKind.History:
                        graphics.DrawArc(secondaryPen, 4, 4, 16, 16, -65, 305);
                        graphics.DrawLine(primaryPen, 4, 5, 4, 10);
                        graphics.DrawLine(primaryPen, 4, 5, 9, 5);
                        graphics.DrawLine(primaryPen, 12, 7.5F, 12, 12);
                        graphics.DrawLine(primaryPen, 12, 12, 15.5F, 14);
                        break;
                    case UiIconKind.Ai:
                        DrawSparkle(graphics, primaryPen, 12, 11, 6.5F);
                        DrawSparkle(graphics, secondaryPen, 18.5F, 5.5F, 2.3F);
                        DrawSparkle(graphics, secondaryPen, 5.5F, 18.5F, 2.3F);
                        break;
                    case UiIconKind.Monitor:
                        DrawRoundedRectangle(graphics, secondaryPen, 3, 4, 18, 13, 2);
                        graphics.DrawLines(primaryPen, new[] { new PointF(6, 11), new PointF(9, 11), new PointF(11, 7), new PointF(14, 14), new PointF(18, 9) });
                        graphics.DrawLine(secondaryPen, 9, 21, 15, 21);
                        graphics.DrawLine(secondaryPen, 12, 17, 12, 21);
                        break;
                }
            }
        }

        private static void ConfigureNavigationPen(Pen pen)
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
        }

        private static void DrawToolbarPictogram(Graphics graphics, UiIconKind kind, Color color, int size)
        {
            float scale = size / 24F;
            graphics.ScaleTransform(scale, scale);
            using (Pen pen = new Pen(color, 1.9F))
            using (SolidBrush brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case UiIconKind.Save:
                        DrawRoundedRectangle(graphics, pen, 4, 3, 16, 18, 2);
                        FillRoundedRectangle(graphics, brush, 7, 4, 9, 5, 0.8F);
                        graphics.DrawLine(pen, 8, 16, 16, 16);
                        graphics.DrawLine(pen, 8, 19, 16, 19);
                        break;
                    case UiIconKind.Cancel:
                        graphics.DrawLine(pen, 6, 6, 18, 18);
                        graphics.DrawLine(pen, 18, 6, 6, 18);
                        break;
                    case UiIconKind.NavigateBack:
                        graphics.DrawLine(pen, 5, 12, 19, 12);
                        graphics.DrawLine(pen, 5, 12, 10, 7);
                        graphics.DrawLine(pen, 5, 12, 10, 17);
                        break;
                    case UiIconKind.NavigateForward:
                        graphics.DrawLine(pen, 5, 12, 19, 12);
                        graphics.DrawLine(pen, 19, 12, 14, 7);
                        graphics.DrawLine(pen, 19, 12, 14, 17);
                        break;
                    case UiIconKind.Undo:
                        graphics.DrawArc(pen, 5, 5, 14, 14, 205, 270);
                        graphics.DrawLine(pen, 4, 8, 9, 8);
                        graphics.DrawLine(pen, 4, 8, 7.5F, 4.5F);
                        graphics.DrawLine(pen, 4, 8, 7.5F, 11.5F);
                        break;
                    case UiIconKind.Redo:
                        graphics.DrawArc(pen, 5, 5, 14, 14, 65, 270);
                        graphics.DrawLine(pen, 15, 8, 20, 8);
                        graphics.DrawLine(pen, 20, 8, 16.5F, 4.5F);
                        graphics.DrawLine(pen, 20, 8, 16.5F, 11.5F);
                        break;
                    case UiIconKind.Pause:
                        FillRoundedRectangle(graphics, brush, 5, 4, 5, 16, 1.5F);
                        FillRoundedRectangle(graphics, brush, 14, 4, 5, 16, 1.5F);
                        break;
                    case UiIconKind.Stop:
                        FillRoundedRectangle(graphics, brush, 5, 5, 14, 14, 2.5F);
                        break;
                    case UiIconKind.Step:
                        graphics.FillPolygon(brush, new[] { new PointF(4, 4), new PointF(16, 12), new PointF(4, 20) });
                        FillRoundedRectangle(graphics, brush, 18, 4, 2.5F, 16, 1);
                        break;
                    case UiIconKind.Locate:
                        graphics.DrawEllipse(pen, 5, 5, 14, 14);
                        graphics.DrawEllipse(pen, 9.5F, 9.5F, 5, 5);
                        graphics.DrawLine(pen, 12, 2, 12, 7);
                        graphics.DrawLine(pen, 12, 17, 12, 22);
                        graphics.DrawLine(pen, 2, 12, 7, 12);
                        graphics.DrawLine(pen, 17, 12, 22, 12);
                        break;
                    case UiIconKind.Breakpoint:
                        graphics.DrawEllipse(pen, 4, 4, 16, 16);
                        FillCircle(graphics, brush, 12, 12, 4.5F);
                        break;
                    case UiIconKind.Alarm:
                        graphics.DrawPolygon(pen, new[] { new PointF(12, 3), new PointF(21, 20), new PointF(3, 20) });
                        graphics.DrawLine(pen, 12, 9, 12, 14);
                        FillCircle(graphics, brush, 12, 17, 1.2F);
                        break;
                    case UiIconKind.Search:
                        graphics.DrawEllipse(pen, 3.5F, 3.5F, 13, 13);
                        graphics.DrawLine(pen, 15, 15, 21, 21);
                        break;
                    case UiIconKind.Monitor:
                        DrawRoundedRectangle(graphics, pen, 3, 4, 18, 13, 2);
                        graphics.DrawLines(pen, new[] { new PointF(6, 11), new PointF(9, 11), new PointF(11, 7), new PointF(14, 14), new PointF(18, 9) });
                        graphics.DrawLine(pen, 9, 21, 15, 21);
                        graphics.DrawLine(pen, 12, 17, 12, 21);
                        break;
                    case UiIconKind.Folder:
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddLines(new[]
                            {
                                new PointF(3, 7), new PointF(9, 7), new PointF(11, 10),
                                new PointF(21, 10), new PointF(19, 20), new PointF(3, 20)
                            });
                            path.CloseFigure();
                            graphics.DrawPath(pen, path);
                        }
                        graphics.DrawLines(pen, new[] { new PointF(3, 7), new PointF(3, 4), new PointF(9, 4), new PointF(11, 7), new PointF(20, 7), new PointF(20, 10) });
                        break;
                    case UiIconKind.Settings:
                        PointF[] gearPoints = new PointF[24];
                        for (int i = 0; i < gearPoints.Length; i++)
                        {
                            double angle = -Math.PI / 2 + Math.PI * 2 * i / gearPoints.Length;
                            float radius = i % 3 == 1 ? 7F : 9F;
                            gearPoints[i] = new PointF(
                                12 + (float)Math.Cos(angle) * radius,
                                12 + (float)Math.Sin(angle) * radius);
                        }
                        graphics.DrawPolygon(pen, gearPoints);
                        graphics.DrawEllipse(pen, 8.5F, 8.5F, 7, 7);
                        FillCircle(graphics, brush, 12, 12, 1.5F);
                        break;
                    case UiIconKind.StopAll:
                        DrawRoundedRectangle(graphics, pen, 3, 3, 18, 18, 4);
                        FillRoundedRectangle(graphics, brush, 7, 7, 10, 10, 2);
                        break;
                }
            }
            graphics.ResetTransform();
        }

        private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2;
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawRoundedRectangle(Graphics graphics, Pen pen, float x, float y, float width, float height, float radius)
        {
            using (GraphicsPath path = RoundedRectangle(x, y, width, height, radius))
            {
                graphics.DrawPath(pen, path);
            }
        }

        private static void FillRoundedRectangle(Graphics graphics, Brush brush, float x, float y, float width, float height, float radius)
        {
            using (GraphicsPath path = RoundedRectangle(x, y, width, height, radius))
            {
                graphics.FillPath(brush, path);
            }
        }

        private static void FillCircle(Graphics graphics, Brush brush, float centerX, float centerY, float radius)
        {
            graphics.FillEllipse(brush, centerX - radius, centerY - radius, radius * 2, radius * 2);
        }

        private static void DrawSparkle(Graphics graphics, Pen pen, float centerX, float centerY, float radius)
        {
            graphics.DrawPolygon(pen, new[]
            {
                new PointF(centerX, centerY - radius),
                new PointF(centerX + radius * 0.24F, centerY - radius * 0.24F),
                new PointF(centerX + radius, centerY),
                new PointF(centerX + radius * 0.24F, centerY + radius * 0.24F),
                new PointF(centerX, centerY + radius),
                new PointF(centerX - radius * 0.24F, centerY + radius * 0.24F),
                new PointF(centerX - radius, centerY),
                new PointF(centerX - radius * 0.24F, centerY - radius * 0.24F)
            });
        }

    }
}
