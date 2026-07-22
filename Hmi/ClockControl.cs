using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GdiStringFormat = System.Drawing.StringFormat;

namespace Automation.Hmi
{
    /// <summary>
    /// 模拟时钟控件，带有时针、分针、秒针和刻度盘动画。
    /// 使用 Timer 每秒刷新，适用于 HMI 调试页面。
    /// </summary>
    public class ClockControl : Control
    {
        private readonly Timer _timer;

        public ClockControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (_, _) => Invalidate();
            _timer.Start();

            BackColor = UiPalette.HmiBackground;
            MinimumSize = new Size(100, 100);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var now = DateTime.Now;
            var size = Math.Min(ClientSize.Width, ClientSize.Height);
            var cx = ClientSize.Width / 2f;
            var cy = ClientSize.Height / 2f;
            var radius = size / 2f - 8f;

            DrawFace(g, cx, cy, radius);
            DrawHands(g, cx, cy, radius, now);
        }

        private void DrawFace(Graphics g, float cx, float cy, float radius)
        {
            // 表盘背景
            using (var bgBrush = new SolidBrush(UiPalette.SurfaceStrong))
            {
                g.FillEllipse(bgBrush, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            // 表盘边框
            using (var pen = new Pen(UiPalette.HmiSection, 3f))
            {
                g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            // 刻度
            for (int i = 0; i < 60; i++)
            {
                float angle = i * 6f - 90f;
                float rad = (float)(angle * Math.PI / 180.0);

                bool isHour = i % 5 == 0;
                float inner = radius * (isHour ? 0.80f : 0.88f);
                float outer = radius * (isHour ? 0.92f : 0.94f);

                float x1 = cx + inner * (float)Math.Cos(rad);
                float y1 = cy + inner * (float)Math.Sin(rad);
                float x2 = cx + outer * (float)Math.Cos(rad);
                float y2 = cy + outer * (float)Math.Sin(rad);

                using (var pen = new Pen(isHour ? UiPalette.TextPrimary : UiPalette.TextMuted, isHour ? 2.5f : 1.2f))
                {
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }

            // 数字 3/6/9/12
            float labelRadius = radius * 0.72f;
            using (var font = new Font("Microsoft YaHei UI", radius * 0.14f, FontStyle.Bold))
            using (var brush = new SolidBrush(UiPalette.TextPrimary))
            using (var fmt = new GdiStringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("12", font, brush, cx, cy - labelRadius, fmt);
                g.DrawString("3", font, brush, cx + labelRadius, cy, fmt);
                g.DrawString("6", font, brush, cx, cy + labelRadius, fmt);
                g.DrawString("9", font, brush, cx - labelRadius, cy, fmt);
            }

            // 中心点
            using (var centerBrush = new SolidBrush(UiPalette.HmiSection))
            {
                g.FillEllipse(centerBrush, cx - 5f, cy - 5f, 10f, 10f);
            }
        }

        private void DrawHands(Graphics g, float cx, float cy, float radius, DateTime now)
        {
            // 时针
            float hourAngle = (now.Hour % 12) * 30f + now.Minute * 0.5f - 90f;
            float hourLen = radius * 0.50f;
            DrawHand(g, cx, cy, hourAngle, hourLen, UiPalette.TextPrimary, 5f);

            // 分针
            float minuteAngle = now.Minute * 6f + now.Second * 0.1f - 90f;
            float minuteLen = radius * 0.68f;
            DrawHand(g, cx, cy, minuteAngle, minuteLen, UiPalette.TextSecondary, 3.5f);

            // 秒针
            float secondAngle = now.Second * 6f - 90f;
            float secondLen = radius * 0.75f;
            DrawHand(g, cx, cy, secondAngle, secondLen, UiPalette.Danger, 1.8f);
        }

        private void DrawHand(Graphics g, float cx, float cy, float angleDeg, float length, Color color, float width)
        {
            float rad = (float)(angleDeg * Math.PI / 180.0);
            float ex = cx + length * (float)Math.Cos(rad);
            float ey = cy + length * (float)Math.Sin(rad);

            using (var pen = new Pen(color, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                g.DrawLine(pen, cx, cy, ex, ey);
            }
        }
    }
}
