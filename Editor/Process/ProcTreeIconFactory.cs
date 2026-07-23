// 模块：编辑器 / 流程。
// 职责范围：流程树、指令表、对象选择、搜索和导航。

using System.Drawing;
using System.Drawing.Drawing2D;

namespace Automation
{
    internal enum ProcTreeIconKind
    {
        Ready,
        Stopped,
        Running,
        Paused,
        SingleStep,
        Alarming,
        Pausing,
        Stopping,
        EmptyProc,
        EmptyProcDisabled,
        Disabled,
        Step,
        EmptyStep,
        EmptyStepDisabled,
        StepRunning,
        StepPaused,
        StepSingle,
        StepAlarming
    }

    /// <summary>
    /// 绘制流程树状态图形。颜色只映射引擎或配置能够确定的状态。
    /// </summary>
    internal static class ProcTreeIconFactory
    {
        public static Bitmap Create(ProcTreeIconKind kind, int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = size / 20F;
                graphics.ScaleTransform(scale, scale);
                switch (kind)
                {
                    case ProcTreeIconKind.Ready:
                        DrawReady(graphics);
                        break;
                    case ProcTreeIconKind.Stopped:
                        DrawStopped(graphics);
                        break;
                    case ProcTreeIconKind.Running:
                        DrawPlayBadge(graphics, UiPalette.Success);
                        break;
                    case ProcTreeIconKind.Paused:
                        DrawPauseBadge(graphics, UiPalette.Warning);
                        break;
                    case ProcTreeIconKind.SingleStep:
                        DrawStepBadge(graphics, UiPalette.Brand);
                        break;
                    case ProcTreeIconKind.Alarming:
                        DrawAlarm(graphics, UiPalette.Danger);
                        break;
                    case ProcTreeIconKind.Pausing:
                        DrawTransitionBadge(graphics, UiPalette.Transition, true);
                        break;
                    case ProcTreeIconKind.Stopping:
                        DrawTransitionBadge(graphics, UiPalette.Stopping, false);
                        break;
                    case ProcTreeIconKind.EmptyProc:
                        DrawEmptyProc(graphics, UiPalette.TextMuted, false);
                        break;
                    case ProcTreeIconKind.EmptyProcDisabled:
                        DrawEmptyProc(graphics, UiPalette.Disabled, true);
                        break;
                    case ProcTreeIconKind.Disabled:
                        DrawDisabled(graphics);
                        break;
                    case ProcTreeIconKind.Step:
                        DrawStepNode(graphics, UiPalette.TextMuted, false);
                        break;
                    case ProcTreeIconKind.EmptyStep:
                        DrawEmptyStepNode(graphics, UiPalette.TextMuted, false);
                        break;
                    case ProcTreeIconKind.EmptyStepDisabled:
                        DrawEmptyStepNode(graphics, UiPalette.Disabled, true);
                        break;
                    case ProcTreeIconKind.StepRunning:
                        DrawStepNode(graphics, UiPalette.Success, true);
                        break;
                    case ProcTreeIconKind.StepPaused:
                        DrawPauseBadge(graphics, UiPalette.Warning);
                        break;
                    case ProcTreeIconKind.StepSingle:
                        DrawStepBadge(graphics, UiPalette.Brand);
                        break;
                    case ProcTreeIconKind.StepAlarming:
                        DrawAlarm(graphics, UiPalette.Danger);
                        break;
                }
                graphics.ResetTransform();
            }
            return bitmap;
        }

        private static void DrawStopped(Graphics graphics)
        {
            using (Pen pen = CreatePen(UiPalette.TextMuted, 1.8F))
            {
                graphics.DrawEllipse(pen, 3, 3, 14, 14);
                graphics.DrawRectangle(pen, 7, 7, 6, 6);
            }
        }

        private static void DrawReady(Graphics graphics)
        {
            // Ready 同时表示尚未启动和执行完成。使用蓝色勾与绿色运行徽标区分，
            // 避免流程结束后仍被误认为处于运行态。
            using (Pen pen = CreatePen(UiPalette.BrandAccent, 1.8F))
            {
                graphics.DrawEllipse(pen, 3, 3, 14, 14);
                graphics.DrawLines(pen, new[]
                {
                    new PointF(6, 10),
                    new PointF(9, 13),
                    new PointF(14.5F, 7)
                });
            }
        }

        private static void DrawEmptyProc(Graphics graphics, Color color, bool disabled)
        {
            using (Pen pen = CreatePen(color, 1.8F))
            {
                pen.DashStyle = DashStyle.Dot;
                graphics.DrawEllipse(pen, 3, 3, 14, 14);
                pen.DashStyle = DashStyle.Solid;
                graphics.DrawRectangle(pen, 8, 8, 4, 4);
                if (disabled)
                {
                    graphics.DrawLine(pen, 5, 5, 15, 15);
                }
            }
        }

        private static void DrawPlayBadge(Graphics graphics, Color color)
        {
            using (SolidBrush badge = new SolidBrush(color))
            using (SolidBrush symbol = new SolidBrush(UiPalette.TextInverse))
            {
                graphics.FillEllipse(badge, 2, 2, 16, 16);
                graphics.FillPolygon(symbol, new[] { new PointF(8, 6), new PointF(14, 10), new PointF(8, 14) });
            }
        }

        private static void DrawPauseBadge(Graphics graphics, Color color)
        {
            using (SolidBrush badge = new SolidBrush(color))
            using (SolidBrush symbol = new SolidBrush(UiPalette.TextInverse))
            {
                graphics.FillEllipse(badge, 2, 2, 16, 16);
                graphics.FillRectangle(symbol, 7, 6, 2.3F, 8);
                graphics.FillRectangle(symbol, 11, 6, 2.3F, 8);
            }
        }

        private static void DrawStepBadge(Graphics graphics, Color color)
        {
            using (SolidBrush badge = new SolidBrush(color))
            using (SolidBrush symbol = new SolidBrush(UiPalette.TextInverse))
            {
                graphics.FillEllipse(badge, 2, 2, 16, 16);
                graphics.FillPolygon(symbol, new[] { new PointF(6, 6), new PointF(12, 10), new PointF(6, 14) });
                graphics.FillRectangle(symbol, 13, 6, 2, 8);
            }
        }

        private static void DrawAlarm(Graphics graphics, Color color)
        {
            using (Pen pen = CreatePen(color, 1.7F))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.DrawPolygon(pen, new[] { new PointF(10, 2), new PointF(18, 17), new PointF(2, 17) });
                graphics.DrawLine(pen, 10, 7, 10, 12);
                graphics.FillEllipse(brush, 9, 14, 2, 2);
            }
        }

        private static void DrawTransitionBadge(Graphics graphics, Color color, bool pausing)
        {
            using (Pen pen = CreatePen(color, 1.8F))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.DrawArc(pen, 2.5F, 2.5F, 15, 15, -65, 295);
                graphics.FillPolygon(brush, new[] { new PointF(16, 2), new PointF(18.5F, 6), new PointF(14, 6) });
                if (pausing)
                {
                    graphics.FillRectangle(brush, 7, 7, 2, 6);
                    graphics.FillRectangle(brush, 11, 7, 2, 6);
                }
                else
                {
                    graphics.FillRectangle(brush, 7, 7, 6, 6);
                }
            }
        }

        private static void DrawDisabled(Graphics graphics)
        {
            using (Pen pen = CreatePen(UiPalette.Disabled, 1.8F))
            {
                graphics.DrawEllipse(pen, 3, 3, 14, 14);
                graphics.DrawLine(pen, 5, 5, 15, 15);
            }
        }

        private static void DrawStepNode(Graphics graphics, Color color, bool active)
        {
            using (Pen pen = CreatePen(color, active ? 2F : 1.7F))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.DrawLine(pen, 4, 10, 16, 10);
                graphics.FillEllipse(brush, 2, 8, 4, 4);
                graphics.FillEllipse(brush, 8, 8, 4, 4);
                graphics.FillEllipse(brush, 14, 8, 4, 4);
                if (active)
                {
                    graphics.DrawEllipse(pen, 6, 6, 8, 8);
                }
            }
        }

        private static void DrawEmptyStepNode(Graphics graphics, Color color, bool disabled)
        {
            using (Pen pen = CreatePen(color, 1.7F))
            {
                pen.DashStyle = DashStyle.Dot;
                graphics.DrawLine(pen, 4, 10, 16, 10);
                pen.DashStyle = DashStyle.Solid;
                graphics.DrawEllipse(pen, 2.5F, 8.5F, 3, 3);
                graphics.DrawEllipse(pen, 8.5F, 8.5F, 3, 3);
                graphics.DrawEllipse(pen, 14.5F, 8.5F, 3, 3);
                if (disabled)
                {
                    graphics.DrawLine(pen, 5, 5, 15, 15);
                }
            }
        }

        private static Pen CreatePen(Color color, float width)
        {
            return new Pen(color, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
        }
    }
}
