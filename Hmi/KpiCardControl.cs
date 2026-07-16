using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Automation.Hmi
{
    /// <summary>
    /// KPI 指标卡片控件，显示计划产量、实际产量、良品数、良率。
    /// 独立控件，自绘，不依赖第三方库。
    /// </summary>
    public class KpiCardControl : UserControl
    {
        // --- 四个指标数据 ---
        private int _plannedOutput = 1200;
        private int _actualOutput = 1058;
        private int _goodCount = 1023;
        private double _yieldRate = 96.7;

        public int PlannedOutput
        {
            get => _plannedOutput;
            set { _plannedOutput = value; Invalidate(); }
        }

        public int ActualOutput
        {
            get => _actualOutput;
            set { _actualOutput = value; Invalidate(); }
        }

        public int GoodCount
        {
            get => _goodCount;
            set { _goodCount = value; Invalidate(); }
        }

        public double YieldRate
        {
            get => _yieldRate;
            set { _yieldRate = value; Invalidate(); }
        }

        public KpiCardControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.DoubleBuffer
                   | ControlStyles.ResizeRedraw, true);

            BackColor = Color.FromArgb(226, 234, 240);
            MinimumSize = new Size(600, 100);
            Size = new Size(1160, 110);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawCards(e.Graphics);
        }

        private void DrawCards(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int padding = 8;
            int cardGap = 12;

            // 四张卡片等宽
            int cardCount = 4;
            int cardWidth = (w - padding * 2 - cardGap * (cardCount - 1)) / cardCount;
            if (cardWidth < 120) cardWidth = 120;

            // 每张卡片的数据
            var cards = new (string Label, string Value, string Unit, Color ValueColor, Color Accent, bool IsYield)[]
            {
                ("计划产量", _plannedOutput.ToString("N0"), "件",
                 Color.FromArgb(20, 56, 82), Color.FromArgb(47, 66, 82), false),
                ("实际产量", _actualOutput.ToString("N0"), "件",
                 Color.FromArgb(20, 56, 82), Color.FromArgb(52, 152, 219), false),
                ("良品数", _goodCount.ToString("N0"), "件",
                 Color.FromArgb(20, 56, 82), Color.FromArgb(39, 174, 96), false),
                ("良率", _yieldRate.ToString("F1"), "%",
                 _yieldRate >= 98 ? Color.FromArgb(39, 174, 96)
                     : _yieldRate >= 95 ? Color.FromArgb(243, 156, 18)
                     : Color.FromArgb(231, 76, 60),
                 _yieldRate >= 98 ? Color.FromArgb(39, 174, 96)
                     : _yieldRate >= 95 ? Color.FromArgb(243, 156, 18)
                     : Color.FromArgb(231, 76, 60), true)
            };

            for (int i = 0; i < cardCount; i++)
            {
                int cx = padding + i * (cardWidth + cardGap);
                DrawSingleCard(g, cx, 6, cardWidth, h - 12, cards[i]);
            }
        }

        private void DrawSingleCard(Graphics g, int x, int y, int w, int h,
                                     (string Label, string Value, string Unit, Color ValueColor, Color Accent, bool IsYield) card)
        {
            // 圆角背景
            using (var path = CreateRoundedRectPath(x, y, w - 1, h - 1, 10))
            using (var bg = new SolidBrush(Color.White))
            using (var border = new Pen(Color.FromArgb(220, 228, 235), 1))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            // 顶部色条
            using (var barBrush = new SolidBrush(card.Accent))
            {
                g.FillRectangle(barBrush, x + 14, y, w - 28, 4);
            }

            // 标签
            using (var font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.FromArgb(130, 150, 168)))
            {
                SizeF labelSize = g.MeasureString(card.Label, font);
                g.DrawString(card.Label, font, brush,
                    x + (w - labelSize.Width) / 2f,
                    y + 16);
            }

            // 带单位的数值
            using (var valFont = new Font("Microsoft YaHei UI", card.IsYield ? 24f : 22f, FontStyle.Bold))
            using (var brush = new SolidBrush(card.ValueColor))
            {
                string text = card.Value;
                if (!card.IsYield) text = card.Value;
                SizeF valSize = g.MeasureString(text, valFont);
                float textX = x + (w - valSize.Width) / 2f;
                float textY = y + 42;
                g.DrawString(text, valFont, brush, textX, textY);
            }

            // 单位
            using (var unitFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.FromArgb(160, 180, 196)))
            {
                // 数值右边显示单位，靠近数值
                string fullText = card.Value + " " + card.Unit;
                using (var tmpFont = new Font("Microsoft YaHei UI", card.IsYield ? 24f : 22f, FontStyle.Bold))
                {
                    SizeF valSize = g.MeasureString(card.Value, tmpFont);
                    float unitX = x + (w - valSize.Width) / 2f + valSize.Width + 4;
                    float unitY = y + 46;
                    g.DrawString(card.Unit, unitFont, brush, unitX, unitY);
                }
            }

            // 良率卡片额外显示目标参考
            if (card.IsYield)
            {
                using (var refFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular))
                using (var brush = new SolidBrush(Color.FromArgb(170, 190, 206)))
                {
                    string refText = "目标 ≥ 98.0%";
                    SizeF refSize = g.MeasureString(refText, refFont);
                    g.DrawString(refText, refFont, brush,
                        x + (w - refSize.Width) / 2f,
                        y + h - 18);
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
    }
}
