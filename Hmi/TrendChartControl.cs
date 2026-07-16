using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Automation.Hmi
{
    /// <summary>
    /// 最近 12 小时产量趋势图控件，同时显示计划值和实际值。
    /// 纯 GDI+ 自绘，不引入第三方图表库。
    /// </summary>
    public class TrendChartControl : UserControl
    {
        // --- 演示数据: 最近 12 小时的产量数据 ---
        private readonly List<HourData> _data;

        public TrendChartControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.DoubleBuffer
                   | ControlStyles.ResizeRedraw, true);

            BackColor = Color.White;
            MinimumSize = new Size(400, 200);
            Size = new Size(760, 280);

            // 静态演示数据: 最近 12 小时
            _data = new List<HourData>
            {
                new HourData("07时", 80, 72),
                new HourData("08时", 100, 95),
                new HourData("09时", 100, 88),
                new HourData("10时", 100, 102),
                new HourData("11时", 100, 98),
                new HourData("12时", 80, 65),
                new HourData("13时", 80, 78),
                new HourData("14时", 100, 96),
                new HourData("15时", 100, 104),
                new HourData("16时", 100, 99),
                new HourData("17时", 100, 93),
                new HourData("18时", 80, 68),
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawChart(e.Graphics);
        }

        private void DrawChart(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            // --- 圆角背景 ---
            using (var path = CreateRoundedRectPath(0, 0, w - 1, h - 1, 10))
            using (var bg = new SolidBrush(Color.White))
            using (var border = new Pen(Color.FromArgb(210, 220, 228), 1))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            // --- 标题 ---
            using (var titleFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(47, 66, 82)))
            {
                g.DrawString("最近 12 小时产量趋势", titleFont, brush, 16, 12);
            }

            // --- 图例 ---
            DrawLegend(g, w);

            // --- 绘图区域边距 ---
            int marginLeft = 50;
            int marginRight = 20;
            int marginTop = 48;
            int marginBottom = 36;
            int plotX = marginLeft;
            int plotY = marginTop;
            int plotW = w - marginLeft - marginRight;
            int plotH = h - marginTop - marginBottom;

            if (plotW <= 10 || plotH <= 10) return;

            // --- 计算 Y 轴范围 ---
            double maxVal = 0;
            double minVal = double.MaxValue;
            foreach (var d in _data)
            {
                if (d.Planned > maxVal) maxVal = d.Planned;
                if (d.Actual > maxVal) maxVal = d.Actual;
                if (d.Planned < minVal) minVal = d.Planned;
                if (d.Actual < minVal) minVal = d.Actual;
            }
            // 留 20% 余量
            double yRange = maxVal - minVal;
            if (yRange < 1) yRange = 50;
            double yMax = maxVal + yRange * 0.15;
            double yMin = Math.Max(0, minVal - yRange * 0.1);

            // --- 绘制 Y 轴网格线和标签 ---
            int gridLines = 5;
            using (var gridPen = new Pen(Color.FromArgb(232, 238, 244), 1))
            using (var gridPenDash = new Pen(Color.FromArgb(232, 238, 244), 1) { DashStyle = DashStyle.Dash })
            using (var axisFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (var axisBrush = new SolidBrush(Color.FromArgb(130, 150, 168)))
            {
                for (int i = 0; i <= gridLines; i++)
                {
                    float ratio = (float)i / gridLines;
                    float yVal = (float)(yMax - (yMax - yMin) * ratio);
                    int gy = plotY + (int)(ratio * plotH);

                    string label = yVal >= 100 ? yVal.ToString("F0") : yVal.ToString("F1");
                    SizeF ls = g.MeasureString(label, axisFont);
                    g.DrawString(label, axisFont, axisBrush, plotX - ls.Width - 4, gy - ls.Height / 2f);

                    if (i > 0)
                        g.DrawLine(gridPenDash, plotX, gy, plotX + plotW, gy);
                }
                // X 轴主线
                g.DrawLine(gridPen, plotX, plotY + plotH, plotX + plotW, plotY + plotH);
            }

            // --- 绘制数据 ---
            int count = _data.Count;
            if (count < 2) return;

            float colWidth = (float)plotW / count;
            float halfCol = colWidth / 2f;

            // 实际值柱状图
            for (int i = 0; i < count; i++)
            {
                float x1 = plotX + i * colWidth + colWidth * 0.15f;
                float x2 = plotX + i * colWidth + colWidth * 0.85f;

                float valRatio = (float)((_data[i].Actual - yMin) / (yMax - yMin));
                float barH = valRatio * plotH;
                float barY = plotY + plotH - barH;

                if (barH > 0)
                {
                    using (var barBrush = new SolidBrush(Color.FromArgb(120, 52, 152, 219)))
                    {
                        g.FillRectangle(barBrush, x1, barY, x2 - x1, barH);
                    }
                }
            }

            // 计划值折线
            using (var plannedPen = new Pen(Color.FromArgb(231, 76, 60), 2.5f) { DashStyle = DashStyle.Dash })
            {
                PointF[] plannedPoints = new PointF[count];
                for (int i = 0; i < count; i++)
                {
                    float x = plotX + i * colWidth + halfCol;
                    float valRatio = (float)((_data[i].Planned - yMin) / (yMax - yMin));
                    float y = plotY + plotH - valRatio * plotH;
                    plannedPoints[i] = new PointF(x, y);
                }
                if (count >= 2)
                    g.DrawLines(plannedPen, plannedPoints);
            }

            // 实际值折线（在柱状图上方叠加）
            using (var actualPen = new Pen(Color.FromArgb(52, 152, 219), 2.5f))
            {
                PointF[] actualPoints = new PointF[count];
                for (int i = 0; i < count; i++)
                {
                    float x = plotX + i * colWidth + halfCol;
                    float valRatio = (float)((_data[i].Actual - yMin) / (yMax - yMin));
                    float y = plotY + plotH - valRatio * plotH;
                    actualPoints[i] = new PointF(x, y);
                }
                if (count >= 2)
                    g.DrawLines(actualPen, actualPoints);

                // 端点圆点
                if (count > 0)
                {
                    float lastX = actualPoints[count - 1].X;
                    float lastY = actualPoints[count - 1].Y;
                    using (var dotBrush = new SolidBrush(Color.FromArgb(52, 152, 219)))
                    {
                        g.FillEllipse(dotBrush, lastX - 4, lastY - 4, 8, 8);
                    }
                }
            }

            // --- X 轴标签 ---
            using (var axisFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (var axisBrush = new SolidBrush(Color.FromArgb(130, 150, 168)))
            {
                for (int i = 0; i < count; i++)
                {
                    if (i % 2 == 0 || i == count - 1) // 隔一个显示一个，避免拥挤
                    {
                        float x = plotX + i * colWidth + halfCol;
                        string label = _data[i].Hour;
                        SizeF ls = g.MeasureString(label, axisFont);
                        g.DrawString(label, axisFont, axisBrush, x - ls.Width / 2f, plotY + plotH + 8);
                    }
                }
            }
        }

        private void DrawLegend(Graphics g, int w)
        {
            int legendX = w - 180;
            int legendY = 14;

            // 实际值图例
            using (var pen = new Pen(Color.FromArgb(52, 152, 219), 2.5f))
            {
                g.DrawLine(pen, legendX, legendY + 7, legendX + 20, legendY + 7);
            }
            using (var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.FromArgb(47, 66, 82)))
            {
                g.DrawString("实际值", font, brush, legendX + 26, legendY + 1);
            }

            // 计划值图例
            using (var pen = new Pen(Color.FromArgb(231, 76, 60), 2.5f) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(pen, legendX + 76, legendY + 7, legendX + 96, legendY + 7);
            }
            using (var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.FromArgb(47, 66, 82)))
            {
                g.DrawString("计划值", font, brush, legendX + 102, legendY + 1);
            }
        }

        private static GraphicsPath CreateRoundedRectPath(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            r = Math.Min(r, Math.Min(w / 2, h / 2));
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        private struct HourData
        {
            public string Hour { get; }
            public double Planned { get; }
            public double Actual { get; }

            public HourData(string hour, double planned, double actual)
            {
                Hour = hour;
                Planned = planned;
                Actual = actual;
            }
        }
    }
}
