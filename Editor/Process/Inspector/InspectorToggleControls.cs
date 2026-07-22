// 模块：编辑器 / 流程 / Inspector。
// 职责范围：指令属性定义、编辑控件、选择器和值转换。

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class InspectorComboArrow : Control
    {
        private bool pointerOver;
        private bool pointerDown;

        public InspectorComboArrow()
        {
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            pointerOver = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            pointerOver = false;
            pointerDown = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pointerDown = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pointerDown = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = !Enabled
                ? UiPalette.SurfaceSubtle
                : pointerDown
                    ? UiPalette.SurfacePressed
                    : (pointerOver ? UiPalette.SurfaceHover : UiPalette.Input);
            e.Graphics.Clear(background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(
                Enabled ? UiPalette.TextSecondary : UiPalette.TextDisabled,
                1.35F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                float centerX = ClientSize.Width / 2F;
                float centerY = ClientSize.Height / 2F;
                e.Graphics.DrawLine(pen, centerX - 3F, centerY - 1.5F, centerX, centerY + 1.5F);
                e.Graphics.DrawLine(pen, centerX, centerY + 1.5F, centerX + 3F, centerY - 1.5F);
            }
        }
    }

    internal sealed class InspectorToggle : CheckBox
    {
        private bool pointerOver;

        public InspectorToggle()
        {
            AutoSize = false;
            Cursor = Cursors.Hand;
            Font = InspectorFonts.Regular9;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            pointerOver = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            pointerOver = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? UiPalette.Surface);
            var track = new Rectangle(2, Math.Max(2, (Height - 18) / 2), 32, 18);
            Color trackColor;
            if (!Enabled)
            {
                trackColor = UiPalette.Disabled;
            }
            else if (Checked)
            {
                trackColor = pointerOver ? UiPalette.BrandHover : UiPalette.Brand;
            }
            else
            {
                trackColor = pointerOver
                    ? UiPalette.TextDisabled
                    : UiPalette.StrokeStrong;
            }
            using (GraphicsPath trackPath = InspectorShapes.CreateRoundedPath(track, 8))
            using (var trackBrush = new SolidBrush(trackColor))
            {
                e.Graphics.FillPath(trackBrush, trackPath);
            }
            int thumbX = Checked ? track.Right - 16 : track.Left + 2;
            using (var thumbBrush = new SolidBrush(UiPalette.SurfaceStrong))
            {
                e.Graphics.FillEllipse(thumbBrush, thumbX, track.Y + 2, 14, 14);
            }
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1));
            }
        }
    }

    internal static class InspectorFonts
    {
        private const string FontDirectory = @"D:\AutomationTools\Fonts\MiSans";
        private static readonly PrivateFontCollection PrivateFonts
            = new PrivateFontCollection();

        public static readonly string LoadFailureMessage;
        public static readonly Font Regular85;
        public static readonly Font Regular9;
        public static readonly Font Regular95;
        public static readonly Font Regular10;
        public static readonly Font Bold9;
        public static readonly Font Bold95;

        static InspectorFonts()
        {
            try
            {
                LoadFontFile("MiSans-Regular.ttf");
                LoadFontFile("MiSans-Semibold.ttf");
                FontFamily regularFamily = GetFamily("MiSans");
                FontFamily semiboldFamily = GetFamily("MiSans Semibold");
                Regular85 = CreateFont(regularFamily, 10F);
                Regular9 = CreateFont(regularFamily, 10.5F);
                Regular95 = CreateFont(regularFamily, 10.75F);
                Regular10 = CreateFont(regularFamily, 11.25F);
                Bold9 = CreateFont(semiboldFamily, 10.5F);
                Bold95 = CreateFont(semiboldFamily, 11.25F);
            }
            catch (Exception ex)
            {
                LoadFailureMessage = "Inspector 字体资源异常：" + ex.Message;
                FontFamily emergencyFamily = SystemFonts.MessageBoxFont.FontFamily;
                Regular85 = CreateFont(emergencyFamily, 10F);
                Regular9 = CreateFont(emergencyFamily, 10.5F);
                Regular95 = CreateFont(emergencyFamily, 10.75F);
                Regular10 = CreateFont(emergencyFamily, 11.25F);
                Bold9 = CreateFont(emergencyFamily, 10.5F, FontStyle.Bold);
                Bold95 = CreateFont(emergencyFamily, 11.25F, FontStyle.Bold);
                try
                {
                    var logger = new LocalFileLogger(@"D:\AutomationLogs\RuntimeExceptions");
                    logger.Log(LoadFailureMessage + Environment.NewLine + ex, LogLevel.Error);
                }
                catch
                {
                }
            }
        }

        private static Font CreateFont(
            FontFamily family,
            float size,
            FontStyle style = FontStyle.Regular)
        {
            FontStyle availableStyle = family.IsStyleAvailable(style)
                ? style
                : FontStyle.Regular;
            return new Font(family, size, availableStyle, GraphicsUnit.Point);
        }

        private static void LoadFontFile(string fileName)
        {
            string path = System.IO.Path.Combine(FontDirectory, fileName);
            if (!System.IO.File.Exists(path))
            {
                throw new System.IO.FileNotFoundException(
                    "Inspector 字体资源缺失：" + path,
                    path);
            }
            PrivateFonts.AddFontFile(path);
        }

        private static FontFamily GetFamily(string familyName)
        {
            FontFamily family = PrivateFonts.Families.FirstOrDefault(item => string.Equals(
                item.Name,
                familyName,
                StringComparison.OrdinalIgnoreCase));
            if (family == null)
            {
                throw new InvalidOperationException(
                    "Inspector 无法加载内置字体：" + familyName);
            }
            return family;
        }
    }

}
