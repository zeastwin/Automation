using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Automation.Hmi
{
    /// <summary>
    /// 生产状态卡片控件，显示今日目标、当前产量和完成率。
    /// 独立绘制，不连接真实设备或流程。
    /// </summary>
    public class ProductionStatusCard : UserControl
    {
        // --- 数据属性 ---
        private int _dailyTarget = 1000;
        private int _currentOutput = 0;

        public int DailyTarget
        {
            get => _dailyTarget;
            set
            {
                _dailyTarget = value;
                Invalidate();
            }
        }

        public int CurrentOutput
        {
            get => _currentOutput;
            set
            {
                _currentOutput = value;
                Invalidate();
            }
        }

        public double CompletionRate =>
            _dailyTarget > 0
                ? (_currentOutput * 100.0 / _dailyTarget)
                : 0.0;

        public ProductionStatusCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.DoubleBuffer
                   | ControlStyles.ResizeRedraw, true);

            BackColor = Color.White;
            MinimumSize = new Size(400, 80);
            Size = new Size(760, 90);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawCard(e.Graphics);
        }

        private void DrawCard(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int padding = 16;
            double rate = CompletionRate;

            // --- 圆角矩形背景 ---
            using (var path = CreateRoundedRectPath(0, 0, w - 1, h - 1, 12))
            using (var bgBrush = new SolidBrush(Color.White))
            using (var borderPen = new Pen(Color.FromArgb(210, 220, 228), 1))
            {
                g.FillPath(bgBrush, path);
                g.DrawPath(borderPen, path);
            }

            // --- 顶部色条 ---
            using (var barBrush = new SolidBrush(Color.FromArgb(47, 66, 82)))
            {
                g.FillRectangle(barBrush, padding, 0, w - padding * 2, 4);
            }

            // --- 分段布局 ---
            // 分成 3 个区域：今日目标 | 当前产量 | 完成率
            int third = (w - padding * 2) / 3;

            // --- 区域 1：今日目标 ---
            DrawMetricCell(g, "今日目标", $"{_dailyTarget:N0}", Color.FromArgb(47, 66, 82),
                           padding, 20, third, h);

            // --- 分隔线 1 ---
            using (var sepPen = new Pen(Color.FromArgb(220, 228, 235), 1))
            {
                int x1 = padding + third;
                g.DrawLine(sepPen, x1, 24, x1, h - 20);
            }

            // --- 区域 2：当前产量 ---
            Color outputColor = rate >= 100
                ? Color.FromArgb(39, 174, 96)
                : Color.FromArgb(20, 56, 82);
            DrawMetricCell(g, "当前产量", $"{_currentOutput:N0}", outputColor,
                           padding + third, 20, third, h);

            // --- 分隔线 2 ---
            using (var sepPen = new Pen(Color.FromArgb(220, 228, 235), 1))
            {
                int x2 = padding + third * 2;
                g.DrawLine(sepPen, x2, 24, x2, h - 20);
            }

            // --- 区域 3：完成率（带进度环）---
            DrawCompletionCell(g, rate, padding + third * 2, 20, third, h);
        }

        private void DrawMetricCell(Graphics g, string label, string value,
                                     Color valueColor, int x, int y, int w, int h)
        {
            int cellCenterX = x + w / 2;

            // 标签
            using (var font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.FromArgb(130, 150, 168)))
            {
                SizeF labelSize = g.MeasureString(label, font);
                g.DrawString(label, font, brush,
                    cellCenterX - labelSize.Width / 2f,
                    y + 8);
            }

            // 数值
            using (var font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold))
            using (var brush = new SolidBrush(valueColor))
            {
                SizeF valSize = g.MeasureString(value, font);
                g.DrawString(value, font, brush,
                    cellCenterX - valSize.Width / 2f,
                    y + 32);
            }
        }

        private void DrawCompletionCell(Graphics g, double rate,
                                         int x, int y, int w, int h)
        {
            int cellCenterX = x + w / 2;
            int ringCx = cellCenterX;
            int ringCy = y + 54;
            int ringRadius = 22;
            int ringWidth = 4;

            // 标签
            using (var font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.FromArgb(130, 150, 168)))
            {
                SizeF labelSize = g.MeasureString("完成率", font);
                g.DrawString("完成率", font, brush,
                    cellCenterX - labelSize.Width / 2f,
                    y + 8);
            }

            // 百分比文本
            string pctText = $"{rate:F1}%";
            using (var font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
            using (var brush = new SolidBrush(rate >= 100
                ? Color.FromArgb(39, 174, 96)
                : Color.FromArgb(20, 56, 82)))
            {
                SizeF txtSize = g.MeasureString(pctText, font);
                g.DrawString(pctText, font, brush,
                    ringCx - txtSize.Width / 2f,
                    ringCy - txtSize.Height / 2f);
            }

            // --- 进度环 ---
            float sweepAngle = (float)(rate / 100.0 * 360.0);
            sweepAngle = sweepAngle > 360f ? 360f : sweepAngle;

            // 底色圆弧
            using (var backPen = new Pen(Color.FromArgb(230, 236, 242), ringWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                g.DrawArc(backPen,
                    ringCx - ringRadius, ringCy - ringRadius,
                    ringRadius * 2, ringRadius * 2,
                    90, 360);
            }

            // 进度圆弧
            if (sweepAngle > 0)
            {
                Color arcColor = rate >= 100
                    ? Color.FromArgb(39, 174, 96)
                    : rate >= 70
                        ? Color.FromArgb(52, 152, 219)
                        : Color.FromArgb(231, 76, 60);

                using (var progPen = new Pen(arcColor, ringWidth)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                })
                {
                    g.DrawArc(progPen,
                        ringCx - ringRadius, ringCy - ringRadius,
                        ringRadius * 2, ringRadius * 2,
                        -90, sweepAngle);
                }
            }
        }

        private static GraphicsPath CreateRoundedRectPath(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            r = System.Math.Min(r, System.Math.Min(w / 2, h / 2));
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
