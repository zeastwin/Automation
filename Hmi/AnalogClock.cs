using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Automation.Hmi
{
    /// <summary>
    /// 图形化模拟时钟控件，在 HMI 主页面显示一个带刻度和时分秒指针的圆形时钟。
    /// </summary>
    public class AnalogClock : UserControl
    {
        private readonly System.Windows.Forms.Timer refreshTimer;

        public AnalogClock()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.DoubleBuffer
                   | ControlStyles.ResizeRedraw, true);

            refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            refreshTimer.Tick += (s, e) => Invalidate();
            refreshTimer.Start();

            Disposed += (s, e) => refreshTimer.Stop();

            BackColor = Color.White;
            MinimumSize = new Size(100, 100);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawClock(e.Graphics);
        }

        private void DrawClock(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int size = Math.Min(ClientSize.Width, ClientSize.Height);
            int cx = ClientSize.Width / 2;
            int cy = ClientSize.Height / 2;
            int radius = size / 2 - 4;

            if (radius <= 0)
                return;

            // --- 表盘外圈 ---
            using (var outerPen = new Pen(Color.FromArgb(47, 66, 82), 3))
            {
                g.DrawEllipse(outerPen, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            // --- 表盘底色（微渐变） ---
            using (var brush = new SolidBrush(Color.FromArgb(245, 248, 250)))
            {
                g.FillEllipse(brush, cx - radius + 3, cy - radius + 3,
                              radius * 2 - 6, radius * 2 - 6);
            }

            // --- 分钟刻度线（60 条） ---
            for (int i = 0; i < 60; i++)
            {
                double angle = i * 6.0 - 90.0;
                double rad = angle * Math.PI / 180.0;

                bool isHour = (i % 5 == 0);
                int innerOffset = isHour ? 18 : 10;
                int outerOffset = 4;

                int x1 = cx + (int)((radius - outerOffset) * Math.Cos(rad));
                int y1 = cy + (int)((radius - outerOffset) * Math.Sin(rad));
                int x2 = cx + (int)((radius - innerOffset) * Math.Cos(rad));
                int y2 = cy + (int)((radius - innerOffset) * Math.Sin(rad));

                using (var pen = new Pen(isHour ? Color.FromArgb(47, 66, 82) : Color.FromArgb(150, 170, 185),
                                         isHour ? 3 : 1))
                {
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }

            // --- 数字小时标记 (3, 6, 9, 12) ---
            using (var font = new Font("Microsoft YaHei UI", radius / 6f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(47, 66, 82)))
            {
                string[] hours = { "12", "3", "6", "9" };
                double[] angles = { -90.0, 0.0, 90.0, 180.0 };
                int textRadius = radius - radius / 5;

                for (int i = 0; i < 4; i++)
                {
                    double rad = angles[i] * Math.PI / 180.0;
                    float x = cx + (float)(textRadius * Math.Cos(rad));
                    float y = cy + (float)(textRadius * Math.Sin(rad));
                    SizeF textSize = g.MeasureString(hours[i], font);
                    g.DrawString(hours[i], font, brush,
                                 x - textSize.Width / 2f,
                                 y - textSize.Height / 2f);
                }
            }

            // --- 获取当前时间 ---
            DateTime now = DateTime.Now;
            double totalSeconds = now.Hour * 3600.0 + now.Minute * 60.0 + now.Second;

            // --- 时针 (每小时30度) ---
            double hourAngle = (totalSeconds / 3600.0) * 30.0 - 90.0;
            double hourRad = hourAngle * Math.PI / 180.0;
            int hourLen = radius / 2;
            using (var pen = new Pen(Color.FromArgb(47, 66, 82), 5)
            {
                EndCap = LineCap.Round,
                StartCap = LineCap.Round
            })
            {
                g.DrawLine(pen, cx, cy,
                    cx + (int)(hourLen * Math.Cos(hourRad)),
                    cy + (int)(hourLen * Math.Sin(hourRad)));
            }

            // --- 分针 (每分钟6度) ---
            double minAngle = (totalSeconds / 60.0) * 6.0 - 90.0;
            double minRad = minAngle * Math.PI / 180.0;
            int minLen = radius * 3 / 5;
            using (var pen = new Pen(Color.FromArgb(47, 66, 82), 3)
            {
                EndCap = LineCap.Round,
                StartCap = LineCap.Round
            })
            {
                g.DrawLine(pen, cx, cy,
                    cx + (int)(minLen * Math.Cos(minRad)),
                    cy + (int)(minLen * Math.Sin(minRad)));
            }

            // --- 秒针 (每秒6度) ---
            double secAngle = now.Second * 6.0 - 90.0;
            double secRad = secAngle * Math.PI / 180.0;
            int secLen = radius * 4 / 6;
            using (var pen = new Pen(Color.FromArgb(213, 55, 57), 2)
            {
                EndCap = LineCap.Round,
                StartCap = LineCap.Round
            })
            {
                g.DrawLine(pen, cx, cy,
                    cx + (int)(secLen * Math.Cos(secRad)),
                    cy + (int)(secLen * Math.Sin(secRad)));
            }

            // --- 中心圆点 ---
            using (var brush = new SolidBrush(Color.FromArgb(213, 55, 57)))
            {
                g.FillEllipse(brush, cx - 4, cy - 4, 8, 8);
            }

            // --- 底部显示数字时间 ---
            string timeStr = now.ToString("HH:mm:ss");
            using (var font = new Font("Microsoft YaHei UI", radius / 8f, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.FromArgb(100, 120, 140)))
            {
                SizeF ts = g.MeasureString(timeStr, font);
                g.DrawString(timeStr, font, brush,
                    cx - ts.Width / 2f,
                    cy + radius / 2f + 4);
            }
        }
    }
}
