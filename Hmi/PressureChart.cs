using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// 模块：平台内置 HMI / 自绘控件。
// 职责范围：维护压力趋势的页面内绘制状态，不承担数据采集、持久化或报警判断。
// 排查入口：曲线不刷新时检查 UpdatePressureData 调用与控件可见性，再检查绘制缓存。

namespace Automation.Hmi
{
    /// <summary>
    /// 压力图表控件 —— 自绘折线图，带模拟数据演示。
    /// 保留 UpdatePressureData(double value) 接口供后续接入真实数据。
    /// </summary>
    public class PressureChart : Panel
    {
        private readonly List<double> _dataPoints = new List<double>();
        private const int MaxPoints = 60;          // 显示最近 60 个数据点
        private const int PaddingLeft = 60;
        private const int PaddingRight = 20;
        private const int PaddingTop = 40;
        private const int PaddingBottom = 50;

        // 模拟数据定时器
        private readonly Timer _demoTimer;
        private double _demoValue = 0.5;
        private bool _demoMode = true;

        // 图表外观
        // 标题
        private string _chartTitle = "压力趋势 (MPa)";
        private string _yAxisLabel = "MPa";
        private string _xAxisLabel = "时间 (秒)";

        public PressureChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            // 填充一些初始演示数据
            SeedDemoData();

            // 启动模拟数据定时器（仅演示用）
            _demoTimer = new Timer { Interval = 1000 };
            _demoTimer.Tick += DemoTimer_Tick;
            _demoTimer.Start();
        }

        /// <summary>
        /// 更新压力数据 —— 外部接入真实数据时调用此方法。
        /// 调用后自动退出演示模式。
        /// </summary>
        /// <param name="value">当前压力值，单位 MPa</param>
        public void UpdatePressureData(double value)
        {
            _demoMode = false;
            AddDataPoint(value);
        }

        /// <summary>
        /// 启用演示模式（填充模拟数据）
        /// </summary>
        public void EnableDemoMode()
        {
            _demoMode = true;
            _dataPoints.Clear();
            SeedDemoData();
            if (!_demoTimer.Enabled)
                _demoTimer.Start();
            Invalidate();
        }

        /// <summary>
        /// 设置图表标题
        /// </summary>
        public string ChartTitle
        {
            get => _chartTitle;
            set { _chartTitle = value; Invalidate(); }
        }

        /// <summary>
        /// 清空所有数据
        /// </summary>
        public void ClearData()
        {
            _dataPoints.Clear();
            Invalidate();
        }

        private void AddDataPoint(double value)
        {
            _dataPoints.Add(value);
            if (_dataPoints.Count > MaxPoints)
                _dataPoints.RemoveAt(0);
            Invalidate();
        }

        private void SeedDemoData()
        {
            _dataPoints.Clear();
            var rng = new Random(42);
            double val = 0.5;
            for (int i = 0; i < 20; i++)
            {
                val += (rng.NextDouble() - 0.48) * 0.15;
                val = Math.Max(0.05, Math.Min(1.0, val));
                _dataPoints.Add(val);
            }
        }

        private void DemoTimer_Tick(object sender, EventArgs e)
        {
            if (!_demoMode) return;

            var rng = new Random();
            _demoValue += (rng.NextDouble() - 0.48) * 0.12;
            _demoValue = Math.Max(0.05, Math.Min(1.0, _demoValue));
            AddDataPoint(_demoValue);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int width = ClientSize.Width;
            int height = ClientSize.Height;

            if (width <= 0 || height <= 0) return;

            // 背景
            using (var bgBrush = new SolidBrush(UiPalette.SurfaceStrong))
                g.FillRectangle(bgBrush, ClientRectangle);

            // 绘图区域
            int plotLeft = PaddingLeft;
            int plotRight = width - PaddingRight;
            int plotTop = PaddingTop;
            int plotBottom = height - PaddingBottom;
            int plotWidth = plotRight - plotLeft;
            int plotHeight = plotBottom - plotTop;

            if (plotWidth <= 10 || plotHeight <= 10) return;

            // ---- 标题 ----
            using (var titleFont = new Font("Microsoft YaHei UI", 12, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(UiPalette.TextPrimary))
            {
                var titleSize = g.MeasureString(_chartTitle, titleFont);
                g.DrawString(_chartTitle, titleFont, titleBrush,
                    new PointF((width - titleSize.Width) / 2, 8));
            }

            // ---- 网格线 ----
            const int gridLines = 5;
            using (var gridPen = new Pen(UiPalette.ChartGrid, 1))
            {
                for (int i = 0; i <= gridLines; i++)
                {
                    float y = plotTop + (plotHeight * i / (float)gridLines);
                    g.DrawLine(gridPen, plotLeft, y, plotRight, y);
                }
            }

            // ---- Y 轴标签 ----
            using (var labelFont = new Font("Microsoft YaHei UI", 8))
            using (var labelBrush = new SolidBrush(UiPalette.ChartLabel))
            {
                for (int i = 0; i <= gridLines; i++)
                {
                    float value = 1.0f - (i / (float)gridLines);
                    float y = plotTop + (plotHeight * i / (float)gridLines);
                    string label = value.ToString("0.0");
                    var labelSize = g.MeasureString(label, labelFont);
                    g.DrawString(label, labelFont, labelBrush,
                        new PointF(plotLeft - labelSize.Width - 6, y - labelSize.Height / 2));
                }

                // Y 轴单位
                g.DrawString(_yAxisLabel, labelFont, labelBrush,
                    new PointF(4, plotTop));
            }

            // ---- X 轴标签 ----
            using (var labelFont = new Font("Microsoft YaHei UI", 8))
            using (var labelBrush = new SolidBrush(UiPalette.ChartLabel))
            {
                int pointCount = _dataPoints.Count;
                if (pointCount > 0)
                {
                    int labelCount = Math.Min(6, pointCount);
                    for (int i = 0; i < labelCount; i++)
                    {
                        int index = pointCount - 1 - (pointCount - 1) * i / Math.Max(1, labelCount - 1);
                        float x = plotRight - (pointCount - 1 - index) * plotWidth / (float)Math.Max(1, pointCount - 1);
                        string label = (index + 1).ToString();
                        var labelSize = g.MeasureString(label, labelFont);
                        g.DrawString(label, labelFont, labelBrush,
                            new PointF(x - labelSize.Width / 2, plotBottom + 4));
                    }
                }

                // X 轴标题
                var xSize = g.MeasureString(_xAxisLabel, labelFont);
                g.DrawString(_xAxisLabel, labelFont, labelBrush,
                    new PointF(plotLeft + (plotWidth - xSize.Width) / 2, plotBottom + 24));
            }

            // ---- 绘制折线 & 填充 ----
            if (_dataPoints.Count < 2) return;

            var points = new List<PointF>();
            for (int i = 0; i < _dataPoints.Count; i++)
            {
                float x = plotRight - (_dataPoints.Count - 1 - i) * plotWidth / (float)Math.Max(1, _dataPoints.Count - 1);
                float y = plotBottom - (float)(_dataPoints[i] * plotHeight);
                y = Math.Max(plotTop, Math.Min(plotBottom, y));
                points.Add(new PointF(x, y));
            }

            // 填充区域（渐变到 X 轴）
            if (points.Count >= 2)
            {
                using (var fillPath = new GraphicsPath())
                {
                    fillPath.AddLines(points.ToArray());
                    fillPath.AddLine(points[points.Count - 1].X, plotBottom,
                                     points[0].X, plotBottom);
                    fillPath.CloseFigure();

                    using (var fillBrush = new LinearGradientBrush(
                        new Point(0, plotTop), new Point(0, plotBottom),
                        Color.FromArgb(80, UiPalette.ChartLine),
                        Color.FromArgb(5, UiPalette.ChartLine)))
                    {
                        g.FillPath(fillBrush, fillPath);
                    }
                }
            }

            // 折线
            using (var linePen = new Pen(UiPalette.ChartLine, 2.5f))
            {
                linePen.StartCap = LineCap.Round;
                linePen.EndCap = LineCap.Round;
                g.DrawLines(linePen, points.ToArray());
            }

            // 数据点标记
            using (var dotBrush = new SolidBrush(UiPalette.ChartLine))
            using (var dotPen = new Pen(UiPalette.TextInverse, 1.5f))
            {
                foreach (var pt in points)
                {
                    g.FillEllipse(dotBrush, pt.X - 3.5f, pt.Y - 3.5f, 7, 7);
                    g.DrawEllipse(dotPen, pt.X - 3.5f, pt.Y - 3.5f, 7, 7);
                }
            }

            // ---- 最新值标注 ----
            if (_dataPoints.Count > 0)
            {
                double lastVal = _dataPoints[_dataPoints.Count - 1];
                var lastPt = points[points.Count - 1];

                using (var valFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold))
                using (var valBrush = new SolidBrush(UiPalette.ChartLine))
                {
                    string valText = lastVal.ToString("0.00") + " MPa";
                    var valSize = g.MeasureString(valText, valFont);

                    float labelX = lastPt.X - valSize.Width / 2;
                    float labelY = lastPt.Y - valSize.Height - 8;

                    // 边界保护
                    if (labelX < plotLeft) labelX = plotLeft;
                    if (labelX + valSize.Width > plotRight) labelX = plotRight - valSize.Width;
                    if (labelY < plotTop) labelY = lastPt.Y + 6;

                    // 标签背景
                    using (var bgBrush = new SolidBrush(Color.FromArgb(220, UiPalette.SurfaceStrong)))
                    using (var borderPen = new Pen(Color.FromArgb(180, UiPalette.ChartLine), 1))
                    {
                        g.FillRectangle(bgBrush, labelX - 4, labelY - 2, valSize.Width + 8, valSize.Height + 4);
                        g.DrawRectangle(borderPen, labelX - 4, labelY - 2, valSize.Width + 8, valSize.Height + 4);
                    }

                    g.DrawString(valText, valFont, valBrush,
                        new PointF(labelX, labelY));
                }
            }

            // ---- 边框 ----
            using (var borderPen = new Pen(UiPalette.Stroke, 1))
            {
                g.DrawRectangle(borderPen, plotLeft, plotTop, plotWidth, plotHeight);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _demoTimer?.Stop();
                _demoTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
