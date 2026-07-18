using System.Drawing;
using System.Drawing.Drawing2D;

namespace Automation
{
    /// <summary>
    /// UI 状态图标入口。资源清单异常时使用内存图标降级，不得阻断 HMI 初始化。
    /// </summary>
    internal static class UiStatusImages
    {
        public static Image CreateValidImage()
        {
            try
            {
                Image image = Properties.Resources.vaild;
                if (image != null) return image;
            }
            catch
            {
                // 资源缺失时按可用性优先原则降级。
            }
            return CreateFallback(UiPalette.Success, true);
        }

        public static Image CreateInvalidImage()
        {
            try
            {
                Image image = Properties.Resources.invalid;
                if (image != null) return image;
            }
            catch
            {
                // 资源缺失时按可用性优先原则降级。
            }
            return CreateFallback(UiPalette.Danger, false);
        }

        private static Bitmap CreateFallback(Color color, bool valid)
        {
            var bitmap = new Bitmap(20, 20);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var pen = new Pen(UiPalette.TextInverse, 2F))
            using (var brush = new SolidBrush(color))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillEllipse(brush, 1, 1, 18, 18);
                if (valid)
                {
                    graphics.DrawLines(pen, new[] { new Point(5, 10), new Point(9, 14), new Point(15, 6) });
                }
                else
                {
                    graphics.DrawLine(pen, 6, 6, 14, 14);
                    graphics.DrawLine(pen, 14, 6, 6, 14);
                }
            }
            return bitmap;
        }
    }
}
