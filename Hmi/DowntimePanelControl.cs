using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Automation.Hmi
{
    /// <summary>
    /// 停机原因排行及占比进度条控件。
    /// 独立控件，自绘，不依赖第三方库。
    /// </summary>
    public class DowntimePanelControl : UserControl
    {
        // --- 演示数据: 停机原因排行 ---
        private readonly List<DowntimeItem> _items;

        public DowntimePanelControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.DoubleBuffer
                   | ControlStyles.ResizeRedraw, true);

            BackColor = Color.White;
            MinimumSize = new Size(300, 200);
            Size = new Size(560, 300);

            // 静态演示数据
            _items = new List<DowntimeItem>
            {
                new DowntimeItem("换型调整", 185, Color.FromArgb(231, 76, 60)),
                new DowntimeItem("物料短缺", 120, Color.FromArgb(230, 126, 34)),
                new DowntimeItem("设备故障", 95, Color.FromArgb(243, 156, 18)),
                new DowntimeItem("质量异常", 72, Color.FromArgb(52, 152, 219)),
                new DowntimeItem("清洁保养", 45, Color.FromArgb(46, 204, 113)),
                new DowntimeItem("其他", 28, Color.FromArgb(149, 165, 166)),
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawPanel(e.Graphics);
        }

        private void DrawPanel(Graphics g)
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
                g.DrawString("停机原因排行", titleFont, brush, 16, 12);
            }

            // 计算总停机时间
            int totalMinutes = 0;
            foreach (var item in _items)
                totalMinutes += item.Minutes;

            if (totalMinutes == 0) return;

            // --- 绘制每行 ---
            int startY = 44;
            int rowHeight = (h - startY - 12) / _items.Count;
            if (rowHeight < 30) rowHeight = 30;

            using (var labelFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular))
            using (var valueFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
            using (var pctFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (var rankFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    int ry = startY + i * rowHeight;
                    var item = _items[i];
                    double pct = item.Minutes * 100.0 / totalMinutes;

                    // 排名序号圆圈
                    using (var rankBrush = new SolidBrush(item.Color))
                    {
                        g.FillEllipse(rankBrush, 16, ry + 4, 20, 20);
                    }
                    using (var whiteBrush = new SolidBrush(Color.White))
                    {
                        string rank = (i + 1).ToString();
                        SizeF rs = g.MeasureString(rank, rankFont);
                        g.DrawString(rank, rankFont, whiteBrush,
                            16 + (20 - rs.Width) / 2f,
                            ry + 4 + (20 - rs.Height) / 2f);
                    }

                    // 原因名称
                    using (var brush = new SolidBrush(Color.FromArgb(47, 66, 82)))
                    {
                        g.DrawString(item.Reason, labelFont, brush, 44, ry + 4);
                        string minutesText = $"{item.Minutes} min";
                        g.DrawString(minutesText, valueFont, brush, 44, ry + 22);
                    }

                    // 百分比
                    string pctText = $"{pct:F1}%";
                    using (var brush = new SolidBrush(Color.FromArgb(130, 150, 168)))
                    {
                        SizeF ps = g.MeasureString(pctText, pctFont);
                        g.DrawString(pctText, pctFont, brush, w - ps.Width - 16, ry + 6);
                    }

                    // 进度条
                    int barX = 44;
                    int barY = ry + rowHeight - 8;
                    int barW = w - barX - 16;
                    int barH = 6;

                    // 底色
                    using (var backBrush = new SolidBrush(Color.FromArgb(230, 236, 242)))
                    {
                        g.FillRectangle(backBrush, barX, barY, barW, barH);
                    }

                    // 进度
                    int fillW = (int)(barW * pct / 100.0);
                    if (fillW > 0)
                    {
                        using (var barRect = new GraphicsPath())
                        {
                            barRect.AddRectangle(new RectangleF(barX, barY, fillW, barH));
                            using (var fillBrush = new SolidBrush(item.Color))
                            {
                                g.FillPath(fillBrush, barRect);
                            }
                        }
                    }
                }
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

        private struct DowntimeItem
        {
            public string Reason { get; }
            public int Minutes { get; }
            public Color Color { get; }

            public DowntimeItem(string reason, int minutes, Color color)
            {
                Reason = reason;
                Minutes = minutes;
                Color = color;
            }
        }
    }
}
