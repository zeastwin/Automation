using System.Drawing;
using System.Drawing.Drawing2D;

namespace Automation
{
    internal enum ProcTreeIconKind
    {
        Stopped,
        Running,
        Paused,
        SingleStep,
        Alarming,
        Pausing,
        Stopping,
        Disabled,
        Step,
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
        private static readonly Color NeutralColor = Color.FromArgb(128, 143, 154);
        private static readonly Color RunningColor = Color.FromArgb(38, 153, 105);
        private static readonly Color PausedColor = Color.FromArgb(211, 145, 35);
        private static readonly Color SingleStepColor = Color.FromArgb(51, 128, 190);
        private static readonly Color AlarmColor = Color.FromArgb(210, 64, 64);
        private static readonly Color TransitionColor = Color.FromArgb(223, 116, 37);
        private static readonly Color StoppingColor = Color.FromArgb(174, 57, 57);
        private static readonly Color DisabledColor = Color.FromArgb(177, 187, 194);

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
                    case ProcTreeIconKind.Stopped:
                        DrawStopped(graphics);
                        break;
                    case ProcTreeIconKind.Running:
                        DrawPlayBadge(graphics, RunningColor);
                        break;
                    case ProcTreeIconKind.Paused:
                        DrawPauseBadge(graphics, PausedColor);
                        break;
                    case ProcTreeIconKind.SingleStep:
                        DrawStepBadge(graphics, SingleStepColor);
                        break;
                    case ProcTreeIconKind.Alarming:
                        DrawAlarm(graphics, AlarmColor);
                        break;
                    case ProcTreeIconKind.Pausing:
                        DrawTransitionBadge(graphics, TransitionColor, true);
                        break;
                    case ProcTreeIconKind.Stopping:
                        DrawTransitionBadge(graphics, StoppingColor, false);
                        break;
                    case ProcTreeIconKind.Disabled:
                        DrawDisabled(graphics);
                        break;
                    case ProcTreeIconKind.Step:
                        DrawStepNode(graphics, NeutralColor, false);
                        break;
                    case ProcTreeIconKind.StepRunning:
                        DrawStepNode(graphics, RunningColor, true);
                        break;
                    case ProcTreeIconKind.StepPaused:
                        DrawPauseBadge(graphics, PausedColor);
                        break;
                    case ProcTreeIconKind.StepSingle:
                        DrawStepBadge(graphics, SingleStepColor);
                        break;
                    case ProcTreeIconKind.StepAlarming:
                        DrawAlarm(graphics, AlarmColor);
                        break;
                }
                graphics.ResetTransform();
            }
            return bitmap;
        }

        private static void DrawStopped(Graphics graphics)
        {
            using (Pen pen = CreatePen(NeutralColor, 1.8F))
            {
                graphics.DrawEllipse(pen, 3, 3, 14, 14);
                graphics.DrawRectangle(pen, 7, 7, 6, 6);
            }
        }

        private static void DrawPlayBadge(Graphics graphics, Color color)
        {
            using (SolidBrush badge = new SolidBrush(color))
            using (SolidBrush symbol = new SolidBrush(Color.White))
            {
                graphics.FillEllipse(badge, 2, 2, 16, 16);
                graphics.FillPolygon(symbol, new[] { new PointF(8, 6), new PointF(14, 10), new PointF(8, 14) });
            }
        }

        private static void DrawPauseBadge(Graphics graphics, Color color)
        {
            using (SolidBrush badge = new SolidBrush(color))
            using (SolidBrush symbol = new SolidBrush(Color.White))
            {
                graphics.FillEllipse(badge, 2, 2, 16, 16);
                graphics.FillRectangle(symbol, 7, 6, 2.3F, 8);
                graphics.FillRectangle(symbol, 11, 6, 2.3F, 8);
            }
        }

        private static void DrawStepBadge(Graphics graphics, Color color)
        {
            using (SolidBrush badge = new SolidBrush(color))
            using (SolidBrush symbol = new SolidBrush(Color.White))
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
            using (Pen pen = CreatePen(DisabledColor, 1.8F))
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
