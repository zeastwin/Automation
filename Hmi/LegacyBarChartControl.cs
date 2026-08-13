using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Automation.Hmi;

internal sealed class LegacyBarChartControl : Control
{
	private IReadOnlyList<KeyValuePair<string, double>> values = Array.Empty<KeyValuePair<string, double>>();

	private string title = string.Empty;

	internal LegacyBarChartControl()
	{
		DoubleBuffered = true;
		Font = new Font("微软雅黑", 9f);
	}

	internal void SetValues(string title, IEnumerable<KeyValuePair<string, double>> values)
	{
		this.title = title ?? string.Empty;
		this.values = (values ?? Enumerable.Empty<KeyValuePair<string, double>>()).ToList();
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		Graphics graphics = e.Graphics;
		graphics.Clear(BackColor);
		using Pen pen = new Pen(Color.Silver);
		using Font font = new Font("微软雅黑", 11f, FontStyle.Bold);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(40, 105, 155));
		graphics.DrawRectangle(pen, 0, 0, Math.Max(0, base.Width - 1), Math.Max(0, base.Height - 1));
		graphics.DrawString(title, font, Brushes.Black, 10f, 8f);
		Rectangle rectangle = new Rectangle(48, 42, Math.Max(10, base.Width - 66), Math.Max(10, base.Height - 84));
		graphics.DrawLine(Pens.Gray, rectangle.Left, rectangle.Top, rectangle.Left, rectangle.Bottom);
		graphics.DrawLine(Pens.Gray, rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Bottom);
		if (values.Count == 0)
		{
			graphics.DrawString("暂无数据", Font, Brushes.Gray, rectangle.Left + 12, rectangle.Top + 12);
			return;
		}
		double num = Math.Max(1.0, values.Max((KeyValuePair<string, double> item) => Math.Abs(item.Value)));
		float num2 = (float)rectangle.Width / (float)values.Count;
		for (int num3 = 0; num3 < values.Count; num3++)
		{
			KeyValuePair<string, double> keyValuePair = values[num3];
			float num4 = Math.Max(8f, num2 * 0.55f);
			float num5 = (float)(Math.Abs(keyValuePair.Value) / num * (double)Math.Max(4, rectangle.Height - 8));
			float num6 = (float)rectangle.Left + (float)num3 * num2 + (num2 - num4) / 2f;
			float num7 = (float)rectangle.Bottom - num5;
			graphics.FillRectangle(brush, num6, num7, num4, num5);
			string s = keyValuePair.Value.ToString("0.##", CultureInfo.InvariantCulture);
			SizeF sizeF = graphics.MeasureString(s, Font);
			graphics.DrawString(s, Font, Brushes.Black, num6 + (num4 - sizeF.Width) / 2f, num7 - sizeF.Height);
			string text = keyValuePair.Key ?? string.Empty;
			if (text.Length > 8)
			{
				text = text.Substring(0, 8);
			}
			SizeF sizeF2 = graphics.MeasureString(text, Font);
			graphics.DrawString(text, Font, Brushes.Black, num6 + (num4 - sizeF2.Width) / 2f, (float)rectangle.Bottom + 4f);
		}
	}
}


