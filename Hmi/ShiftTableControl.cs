using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Automation.Hmi
{
    /// <summary>
    /// 班次统计表控件，显示早班、中班、晚班的产量、良品和良率。
    /// 独立控件，自绘。
    /// </summary>
    public class ShiftTableControl : UserControl
    {
        // --- 演示数据: 三个班次 ---
        private int _morningOutput = 420;
        private int _morningGood = 408;
        private int _afternoonOutput = 380;
        private int _afternoonGood = 365;
        private int _nightOutput = 258;
        private int _nightGood = 250;

        public int MorningOutput
        {
            get => _morningOutput; set { _morningOutput = value; Invalidate(); }
        }
        public int MorningGood
        {
            get => _morningGood; set { _morningGood = value; Invalidate(); }
        }
        public int AfternoonOutput
        {
            get => _afternoonOutput; set { _afternoonOutput = value; Invalidate(); }
        }
        public int AfternoonGood
        {
            get => _afternoonGood; set { _afternoonGood = value; Invalidate(); }
        }
        public int NightOutput
        {
            get => _nightOutput; set { _nightOutput = value; Invalidate(); }
        }
        public int NightGood
        {
            get => _nightGood; set { _nightGood = value; Invalidate(); }
        }

        public ShiftTableControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.DoubleBuffer
                   | ControlStyles.ResizeRedraw, true);

            BackColor = Color.White;
            MinimumSize = new Size(300, 180);
            Size = new Size(560, 280);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawTable(e.Graphics);
        }

        private void DrawTable(Graphics g)
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
                g.DrawString("班次统计", titleFont, brush, 16, 12);
            }

            // --- 表格布局 ---
            int tableTop = 46;
            int tableLeft = 12;
            int tableW = w - 24;
            int headerH = 34;
            int rowH = (h - tableTop - 10 - headerH) / 3;
            if (rowH < 28) rowH = 28;

            // 列宽: 班次 | 产量 | 良品 | 良率
            int[] colWidths = { 80, 80, 80, 80 };
            int totalColW = 0;
            foreach (int cw in colWidths) totalColW += cw;

            // 居中表格
            int colStartX = tableLeft + (tableW - totalColW) / 2;
            if (colStartX < tableLeft) colStartX = tableLeft;

            // 表头列标题
            string[] headers = { "班次", "产量", "良品", "良率" };
            using (var headerFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
            using (var headerBg = new SolidBrush(Color.FromArgb(47, 66, 82)))
            using (var headerText = new SolidBrush(Color.White))
            {
                int hx = colStartX;
                for (int i = 0; i < headers.Length; i++)
                {
                    g.FillRectangle(headerBg, hx, tableTop, colWidths[i] - 1, headerH);
                    SizeF hs = g.MeasureString(headers[i], headerFont);
                    g.DrawString(headers[i], headerFont, headerText,
                        hx + (colWidths[i] - hs.Width) / 2f,
                        tableTop + (headerH - hs.Height) / 2f);
                    hx += colWidths[i];
                }
            }

            // 数据行
            var shifts = new (string Name, int Output, int Good, Color BarColor)[]
            {
                ("早班 06-14", _morningOutput, _morningGood, Color.FromArgb(52, 152, 219)),
                ("中班 14-22", _afternoonOutput, _afternoonGood, Color.FromArgb(243, 156, 18)),
                ("晚班 22-06", _nightOutput, _nightGood, Color.FromArgb(46, 204, 113)),
            };

            using (var rowFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular))
            using (var valFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
            using (var altBg = new SolidBrush(Color.FromArgb(245, 247, 249)))
            using (var textBrush = new SolidBrush(Color.FromArgb(47, 66, 82)))
            using (var valueBrush = new SolidBrush(Color.FromArgb(20, 56, 82)))
            using (var linePen = new Pen(Color.FromArgb(226, 234, 240), 1))
            {
                for (int i = 0; i < shifts.Length; i++)
                {
                    int ry = tableTop + headerH + i * rowH + 4;

                    // 交替行底色
                    if (i % 2 == 1)
                    {
                        g.FillRectangle(altBg, tableLeft, ry, tableW, rowH);
                    }

                    // 班次名称 + 带颜色圆点
                    int hx = colStartX;
                    using (var dotBrush = new SolidBrush(shifts[i].BarColor))
                    {
                        g.FillEllipse(dotBrush, hx + 6, ry + (rowH - 10) / 2f, 10, 10);
                    }

                    string shiftText = shifts[i].Name;
                    SizeF ss = g.MeasureString(shiftText, rowFont);
                    g.DrawString(shiftText, rowFont, textBrush,
                        hx + 22, ry + (rowH - ss.Height) / 2f);
                    hx += colWidths[0];

                    // 产量
                    string outText = shifts[i].Output.ToString("N0");
                    SizeF os = g.MeasureString(outText, valFont);
                    g.DrawString(outText, valFont, valueBrush,
                        hx + (colWidths[1] - os.Width) / 2f, ry + (rowH - os.Height) / 2f);
                    hx += colWidths[1];

                    // 良品
                    string goodText = shifts[i].Good.ToString("N0");
                    SizeF gs = g.MeasureString(goodText, valFont);
                    g.DrawString(goodText, valFont, valueBrush,
                        hx + (colWidths[2] - gs.Width) / 2f, ry + (rowH - gs.Height) / 2f);
                    hx += colWidths[2];

                    // 良率
                    double yield = shifts[i].Output > 0
                        ? shifts[i].Good * 100.0 / shifts[i].Output
                        : 0;
                    string yieldText = $"{yield:F1}%";
                    Color yieldColor = yield >= 98 ? Color.FromArgb(39, 174, 96)
                        : yield >= 95 ? Color.FromArgb(243, 156, 18)
                        : Color.FromArgb(231, 76, 60);

                    using (var yieldBrush = new SolidBrush(yieldColor))
                    {
                        SizeF ys = g.MeasureString(yieldText, valFont);
                        g.DrawString(yieldText, valFont, yieldBrush,
                            hx + (colWidths[3] - ys.Width) / 2f, ry + (rowH - ys.Height) / 2f);
                    }

                    // 行分隔线
                    g.DrawLine(linePen, tableLeft + 4, ry + rowH, tableLeft + tableW - 4, ry + rowH);
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
