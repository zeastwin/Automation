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
        Pause,
        Stop,
        Step,
        Locate,
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
                    DrawNavigationPictogram(graphics, kind, color, size);
                }
                else
                {
                    DrawToolbarPictogram(graphics, kind, color, size);
                }
            }
            return bitmap;
        }

        private static bool IsNavigationPictogram(UiIconKind kind)
        {
            return kind >= UiIconKind.Process && kind <= UiIconKind.Ai;
        }

        private static void DrawNavigationPictogram(Graphics graphics, UiIconKind kind, Color color, int size)
        {
            float scale = size / 24F;
            graphics.ScaleTransform(scale, scale);
            using (Pen pen = new Pen(color, 1.8F))
            using (SolidBrush brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (kind)
                {
                    case UiIconKind.Process:
                        graphics.DrawLines(pen, new[] { new PointF(5, 6), new PointF(12, 6), new PointF(12, 18), new PointF(19, 18) });
                        FillCircle(graphics, brush, 5, 6, 2.5F);
                        FillCircle(graphics, brush, 12, 12, 2.2F);
                        FillCircle(graphics, brush, 19, 18, 2.5F);
                        break;
                    case UiIconKind.Station:
                        DrawRoundedRectangle(graphics, pen, 3.5F, 7, 17, 13, 2);
                        graphics.DrawLine(pen, 3.5F, 12, 20.5F, 12);
                        graphics.DrawLine(pen, 9, 12, 9, 20);
                        graphics.FillRectangle(brush, 12, 15, 5, 5);
                        graphics.DrawLine(pen, 7, 4, 17, 4);
                        graphics.DrawLine(pen, 12, 4, 12, 7);
                        break;
                    case UiIconKind.Variable:
                        graphics.DrawBezier(pen, 9, 4, 6, 4, 8, 10, 5, 10.5F);
                        graphics.DrawBezier(pen, 5, 10.5F, 8, 11, 6, 20, 9, 20);
                        graphics.DrawBezier(pen, 15, 4, 18, 4, 16, 10, 19, 10.5F);
                        graphics.DrawBezier(pen, 19, 10.5F, 16, 11, 18, 20, 15, 20);
                        graphics.DrawLine(pen, 10, 9, 14, 15);
                        graphics.DrawLine(pen, 14, 9, 10, 15);
                        break;
                    case UiIconKind.Sliders:
                    case UiIconKind.Debug:
                        DrawSlider(graphics, pen, brush, 5, kind == UiIconKind.Debug ? 8 : 15);
                        DrawSlider(graphics, pen, brush, 12, kind == UiIconKind.Debug ? 16 : 9);
                        DrawSlider(graphics, pen, brush, 19, kind == UiIconKind.Debug ? 10 : 14);
                        break;
                    case UiIconKind.Communication:
                        FillCircle(graphics, brush, 12, 17.5F, 2);
                        graphics.DrawArc(pen, 7, 10, 10, 10, 205, 130);
                        graphics.DrawArc(pen, 3.5F, 5.5F, 17, 17, 215, 110);
                        break;
                    case UiIconKind.Plc:
                        DrawRoundedRectangle(graphics, pen, 5, 5, 14, 14, 2);
                        for (int i = 0; i < 4; i++)
                        {
                            float position = 7.5F + i * 3;
                            graphics.DrawLine(pen, position, 2.5F, position, 5);
                            graphics.DrawLine(pen, position, 19, position, 21.5F);
                            graphics.DrawLine(pen, 2.5F, position, 5, position);
                            graphics.DrawLine(pen, 19, position, 21.5F, position);
                        }
                        graphics.FillRectangle(brush, 8.5F, 8.5F, 7, 7);
                        break;
                    case UiIconKind.ControlCard:
                        DrawRoundedRectangle(graphics, pen, 3, 5, 18, 14, 2);
                        graphics.DrawLine(pen, 7, 9, 17, 9);
                        graphics.DrawLine(pen, 7, 14.5F, 13, 14.5F);
                        FillCircle(graphics, brush, 17, 14.5F, 1.7F);
                        graphics.DrawLine(pen, 7, 19, 7, 21);
                        graphics.DrawLine(pen, 11, 19, 11, 21);
                        graphics.DrawLine(pen, 15, 19, 15, 21);
                        break;
                    case UiIconKind.History:
                        graphics.DrawArc(pen, 4, 4, 16, 16, -65, 305);
                        graphics.DrawLine(pen, 4, 5, 4, 10);
                        graphics.DrawLine(pen, 4, 5, 9, 5);
                        graphics.DrawLine(pen, 12, 7.5F, 12, 12);
                        graphics.DrawLine(pen, 12, 12, 15.5F, 14);
                        break;
                    case UiIconKind.Ai:
                        DrawSparkle(graphics, pen, 12, 11, 6.5F);
                        DrawSparkle(graphics, pen, 18.5F, 5.5F, 2.3F);
                        DrawSparkle(graphics, pen, 5.5F, 18.5F, 2.3F);
                        break;
                }
            }
            graphics.ResetTransform();
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
                        graphics.DrawRectangle(pen, 7, 3, 9, 6);
                        graphics.DrawLine(pen, 8, 16, 16, 16);
                        graphics.DrawLine(pen, 8, 19, 16, 19);
                        break;
                    case UiIconKind.Cancel:
                        graphics.DrawArc(pen, 5, 5, 14, 14, -45, 285);
                        graphics.DrawLine(pen, 5, 6, 5, 11);
                        graphics.DrawLine(pen, 5, 6, 10, 6);
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

        private static void DrawSlider(Graphics graphics, Pen pen, Brush brush, float y, float knobX)
        {
            graphics.DrawLine(pen, 4, y, 20, y);
            FillCircle(graphics, brush, knobX, y, 2.2F);
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
